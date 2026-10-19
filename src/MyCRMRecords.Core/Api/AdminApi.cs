using System.Collections;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MyCRMRecords.Data;
using MyCRMRecords.Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MyCRMRecords.Api;

/// <summary>
/// The admin page's API (api.ashx/admin/...), administrators only: where MyCRMRecords keeps its data, the data
/// migrations, the settings (connections, email, scheduled tasks), the import from MyCRM, the sync with it and the
/// audit log. Secrets are never sent back: a blank secret keeps the one saved.
/// </summary>
public sealed class AdminApi(CrmApp w, DatabaseStorage storage, OutSystemsImport import, Action<Action> runInBackground, bool demoTools)
{
    public static bool Handles(string path) => path.Trim('/') == "admin" || path.Trim('/').StartsWith("admin/", StringComparison.Ordinal);

    public ApiResponse Handle(string method, string path, string body, IReadOnlyDictionary<string, string> query, ApiUser user)
    {
        var route = path.Trim('/');
        var get = method == "GET";
        string Q(string name) => query.TryGetValue(name, out var v) ? v ?? "" : "";
        try
        {
            switch (route)
            {
                case "admin/database" when get: return Database();
                case "admin/database/test" when !get: return TestDatabase(Input.Read<DatabaseInput>(body));
                case "admin/database" when !get: return SaveDatabase(Input.Read<DatabaseInput>(body), user);
                case "admin/migrations" when get: return Migrations();
                case "admin/migrations/run" when !get: return RunMigration(Input.Read<NameInput>(body).Name ?? "", user);
                case "admin/settings" when get: return Settings();
                case "admin/connections/test" when !get:
                {
                    var name = Input.Read<NameInput>(body).Name ?? "";
                    if (!ExternalNames.All.Contains(name)) return ApiResponse.BadRequest("No such database.");
                    var lines = w.Databases.Test(name);
                    return ApiResponse.Of(new { result = new { ok = lines.All(l => l.Ok), code = ResultCodes.Ok, message = lines.All(l => l.Ok) ? "Everything MyCRMRecords needs is there." : "Something is missing." }, lines = lines.Select(l => new { ok = l.Ok, text = l.Line }) });
                }
                case "admin/email/test" when !get:
                {
                    var to = (Input.Read<NameInput>(body).Name ?? "").Trim();
                    if (!Strings.IsEmail(to)) return ApiResponse.Fail(400, ResultCodes.Validation, "Enter an email address.");
                    var sent = w.Mailer.Send(new Integration.Email("", 0, [to], Array.Empty<string>(), "MyCRMRecords test email",
                        Integration.EmailHtml.Page("MyCRMRecords test email", "<p>The email settings work.</p>", "", "")));
                    return sent.Sent ? ApiResponse.Ok("Sent to " + to + ".") : ApiResponse.Fail(400, "NOT_SENT", sent.Error);
                }
                case "admin/emails" when get:
                {
                    var q = Query.All().Search(Q("q"), "To", "Cc", "Subject", "Error").Newest();
                    var skip = int.TryParse(Q("skip"), out var from) ? Math.Max(0, from) : 0;
                    return ApiResponse.Of(new { total = w.Store.Count<EmailMessage>(q.Unpaged()), rows = w.Store.Find<EmailMessage>(q.Page(skip, 50)) });
                }
                case "admin/jobs" when get:
                    BackgroundRuns.StopAbandoned(w.Store, w.Clock);
                    return Jobs();
                case "admin/jobs/run" when !get:
                {
                    var job = Input.Read<NameInput>(body).Name ?? "";
                    if (!Data.Jobs.All.Contains(job)) return ApiResponse.NotFound();
                    var run = w.Scheduler.Run(job, user.ChangedBy);
                    return ApiResponse.Result(run.Outcome == "Done" ? OperationResult.Success(run.Summary) : OperationResult.Failure(ResultCodes.Failed, run.Summary), new { run });
                }
                case "admin/import" when get:
                    BackgroundRuns.StopAbandoned(w.Store, w.Clock);
                    return ApiResponse.Of(new { runs = w.Store.Find<JobRun>(Query.Where("Job", OutSystemsImport.Job).Newest().First(5)), configured = w.Databases.IsConfigured(ExternalNames.OutSystems) });
                case "admin/import/preview" when !get:
                {
                    if (!w.Databases.IsConfigured(ExternalNames.OutSystems))
                        return ApiResponse.Fail(400, ResultCodes.Validation, "Set up the OutSystems database on the Connections tab first.");
                    var counts = import.Preview();
                    return ApiResponse.Of(new { result = new { ok = true, code = ResultCodes.Ok, message = "Read MyCRM's data." }, counts });
                }
                case "admin/import/run" when !get:
                {
                    BackgroundRuns.StopAbandoned(w.Store, w.Clock);
                    if (w.Store.Count<JobRun>(Query.Where("Job", OutSystemsImport.Job).And("Outcome", "Running")) > 0)
                        return ApiResponse.Fail(409, ResultCodes.Conflict, "An import is running already.");
                    var run = w.Store.Insert(new JobRun { Job = OutSystemsImport.Job, StartedAt = w.Clock(), Outcome = "Running", By = user.ChangedBy, Summary = "Starting…" });
                    BackgroundRuns.Started(run.Id);
                    runInBackground(() => Import(run, user.ChangedBy));
                    return ApiResponse.Ok("The import is running; this page shows its progress.", new { run });
                }
                case "admin/audit" when get:
                {
                    var q = Query.All().Search(Q("q"), "Login", "DisplayName", "Action", "Target", "Message", "Path").Newest();
                    if (Q("failed") == "1") q.And("Succeeded", false);
                    var skip = int.TryParse(Q("skip"), out var s) ? Math.Max(0, s) : 0;
                    return ApiResponse.Of(new { total = w.Store.Count<AuditEntry>(q.Unpaged()), rows = w.Store.Find<AuditEntry>(q.Page(skip, 100)) });
                }
                case "admin/sample-data" when !get && demoTools:
                    if (w.Settings().Sync.Enabled)
                        return ApiResponse.Fail(409, ResultCodes.Conflict, "The sync with MyCRM is on: sample data would be copied there. Turn the sync off first.");
                    return ApiResponse.Ok(SeedData.Add(w.Store, w.Clock(), user.ToActor(w.People)));
                case "admin/sync" when get:
                    return ApiResponse.Of(new
                    {
                        settings = w.Settings().Sync, configured = w.Databases.IsConfigured(ExternalNames.OutSystems),
                        runs = w.Store.Find<SyncRun>(Query.All().Sort("Id", descending: true).First(20)),
                        pairs = w.Store.Count<SyncLink>(),
                        preview = StoppedAbandoned().FindFirst<JobRun>(Query.Where("Job", SyncPreviewJob).Newest()),
                    });
                case "admin/sync" when !get:
                    return SaveSync(Input.Read<SyncInput>(body), user);
                case "admin/sync/preview" when !get:
                {
                    // In the background: with hundreds of thousands of records (the first sync after the import pairs them all)
                    // it takes longer than ASP.NET lets a page wait.
                    BackgroundRuns.StopAbandoned(w.Store, w.Clock);
                    if (w.Store.Count<JobRun>(Query.Where("Job", SyncPreviewJob).And("Outcome", "Running")) > 0)
                        return ApiResponse.Fail(409, ResultCodes.Conflict, "A preview is running already.");
                    var run = w.Store.Insert(new JobRun { Job = SyncPreviewJob, StartedAt = w.Clock(), Outcome = "Running", By = user.ChangedBy, Summary = "Reading both…" });
                    BackgroundRuns.Started(run.Id);
                    runInBackground(() => Preview(run));
                    return ApiResponse.Ok("The preview is running; this page shows what it finds.", new { run });
                }
                case "admin/sync/run" when !get:
                {
                    if (!w.Settings().Sync.Enabled) return ApiResponse.Fail(409, ResultCodes.Conflict, "Turn the sync on first.");
                    var allowLarge = Input.Read<SyncInput>(body).AllowLarge;
                    runInBackground(() => w.Sync.Run(user.ChangedBy, allowLarge));
                    return ApiResponse.Ok("The sync is running; this page shows it under Syncs when it ends.");
                }
                case "admin/sync/changes" when get:
                {
                    var q = Query.All().Search(Q("q"), "OldTable", "OldKey", "Record", "Column", "Before", "After").Sort("Id", descending: true);
                    var skip = int.TryParse(Q("skip"), out var from) ? Math.Max(0, from) : 0;
                    return ApiResponse.Of(new { total = w.Store.Count<SyncChange>(q.Unpaged()), rows = w.Store.Find<SyncChange>(q.Page(skip, 100)) });
                }
            }
            if (route.StartsWith("admin/settings/", StringComparison.Ordinal) && !get)
                return SaveSection(route.Substring("admin/settings/".Length), body, user);
            return ApiResponse.NotFound();
        }
        catch (BadInputException ex)
        {
            return ApiResponse.BadRequest(ex.Message);
        }
    }

    public sealed class NameInput { public string? Name { get; set; } }

    public sealed class SyncInput
    {
        public bool Enabled { get; set; }
        public int IntervalMinutes { get; set; } = 5;
        public int LargeSync { get; set; } = 200;
        public bool AllowLarge { get; set; }
    }

    /// <summary>Turns the sync with MyCRM on or off; turned on, it syncs at once (stopping if that would change a lot).</summary>
    private ApiResponse SaveSync(SyncInput input, ApiUser user)
    {
        if (input.IntervalMinutes is < 1 or > 1440) return ApiResponse.Fail(400, ResultCodes.Validation, "Sync every 1 to 1440 minutes.");
        if (input.LargeSync is < 1 or > 100000) return ApiResponse.Fail(400, ResultCodes.Validation, "Enter how many changes make a sync large: 1 to 100000.");
        if (input.Enabled && !w.Databases.IsConfigured(ExternalNames.OutSystems))
            return ApiResponse.Fail(400, ResultCodes.Validation, "Set up the OutSystems database on the Connections tab first: MyCRM keeps its tables there.");
        var wasOn = w.Settings().Sync.Enabled;
        w.SettingsFile.Change(s =>
        {
            s.Sync.Enabled = input.Enabled;
            s.Sync.IntervalMinutes = input.IntervalMinutes;
            s.Sync.LargeSync = input.LargeSync;
            if (input.Enabled && !wasOn) s.Sync.Stopped = "";
            s.ChangedBy = user.ChangedBy;
            s.ChangedAt = DateTimeOffset.Now;
        });
        if (!input.Enabled) return ApiResponse.Ok("Saved: the sync with MyCRM is off.");
        if (wasOn) return ApiResponse.Ok("Saved.");
        // The first sync runs in the background: after the import it pairs every record, longer than a page may wait.
        runInBackground(() => w.Sync.Run(user.ChangedBy));
        return ApiResponse.Ok("The sync is on, and the first sync is running: this page shows it under Syncs when it ends.");
    }

    public const string SyncPreviewJob = "sync-preview";

    private IRecordStore StoppedAbandoned()
    {
        BackgroundRuns.StopAbandoned(w.Store, w.Clock);
        return w.Store;
    }

    /// <summary>A preview of the sync, in the background: what it would change, kept in the run's summary.</summary>
    private void Preview(JobRun run)
    {
        try
        {
            var outcome = w.Sync.Preview();
            run.Outcome = outcome.Ok ? "Done" : "Failed";
            run.Processed = outcome.Details.Count;
            run.Summary = outcome.Message + (outcome.Details.Count == 0 ? "" : "\n" + string.Join("\n", outcome.Details.Take(300))
                + (outcome.Details.Count > 300 ? $"\n… and {(outcome.Details.Count - 300).ToString(System.Globalization.CultureInfo.InvariantCulture)} more." : ""));
        }
        catch (Exception ex)
        {
            run.Outcome = "Failed";
            run.Summary = "The preview stopped: " + ex.Message;
        }
        run.FinishedAt = w.Clock();
        w.Store.Update(run);
        BackgroundRuns.Ended(run.Id);
    }

    public sealed class SqlInput
    {
        public string? Server { get; set; }
        public string? Database { get; set; }
        public string? Authentication { get; set; }
        public string? UserName { get; set; }
        /// <summary>Blank keeps the password saved before.</summary>
        public string? Password { get; set; }
        public bool Encrypt { get; set; }
        public bool TrustServerCertificate { get; set; }

        public SqlServerSettings ToSettings(SqlServerSettings saved) => new()
        {
            Server = (Server ?? "").Trim(),
            Database = (Database ?? "").Trim(),
            Authentication = Authentication ?? SqlAuthentication.Windows,
            UserName = (UserName ?? "").Trim(),
            Password = string.IsNullOrEmpty(Password) && (UserName ?? "").Trim() == saved.UserName ? saved.Password : Password ?? "",
            Encrypt = Encrypt,
            TrustServerCertificate = TrustServerCertificate,
        };
    }

    public sealed class DatabaseInput
    {
        public string? Provider { get; set; }
        public SqlInput? SqlServer { get; set; }
        public bool CopyData { get; set; }
    }

    private static ApiResponse Invalid(Dictionary<string, string> errors) =>
        ApiResponse.Result(OperationResult.Invalid(errors.Select(e => new FieldError(e.Key, e.Value)).ToList()));

    private static object SqlView(SqlServerSettings sql) => new
    {
        sql.Server, sql.Database, sql.Authentication, sql.UserName, hasPassword = sql.Password.Length > 0, sql.Encrypt, sql.TrustServerCertificate,
    };

    // ---------------------------------------------------------------- database

    private ApiResponse Database()
    {
        var settings = storage.SettingsFile.Load();
        string? error = null;
        IReadOnlyList<(string Table, long Rows)> tables = Array.Empty<(string, long)>();
        if (settings.Provider == StorageProviders.SqlServer)
        {
            try { tables = storage.TableCounts(settings.SqlServer); }
            catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException) { error = ex.Message; }
        }
        return ApiResponse.Of(new
        {
            provider = settings.Provider,
            sqlServer = SqlView(settings.SqlServer),
            settings.ChangedBy,
            settings.ChangedAt,
            dataFolder = storage.JsonStore.Folder,
            error,
            tables = tables.Select(t => new { name = t.Table, rows = t.Rows }),
            demoTools,
        });
    }

    private ApiResponse TestDatabase(DatabaseInput input)
    {
        var sql = (input.SqlServer ?? new SqlInput()).ToSettings(storage.SettingsFile.Load().SqlServer);
        var errors = sql.Validate();
        if (errors.Count > 0) return Invalid(errors);
        try
        {
            var status = storage.Probe(sql);
            return ApiResponse.Ok(status.TablesExist
                ? $"Connected (SQL Server {status.ServerVersion}). The database holds MyCRMRecords's tables, with {status.Records} records."
                : $"Connected (SQL Server {status.ServerVersion}). MyCRMRecords's tables are created when you switch to it.");
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or ArgumentException)
        {
            return ApiResponse.Fail(400, "CONNECTION_FAILED", "Could not connect: " + ex.Message);
        }
    }

    /// <summary>
    /// Saves where the data is kept and switches to it at once. Switching to SQL Server runs the migrations not run yet;
    /// empty storage gets a copy of the data in use (or any storage, when asked).
    /// </summary>
    private ApiResponse SaveDatabase(DatabaseInput input, ApiUser user)
    {
        var saved = storage.SettingsFile.Load();
        var provider = input.Provider == StorageProviders.SqlServer ? StorageProviders.SqlServer : StorageProviders.JsonFile;
        var sql = (input.SqlServer ?? new SqlInput()).ToSettings(saved.SqlServer);
        var current = storage.Current();
        var copied = "";
        try
        {
            if (provider == StorageProviders.SqlServer)
            {
                var errors = sql.Validate();
                if (errors.Count > 0) return Invalid(errors);
                var ran = storage.RunPendingMigrations(sql, user.ChangedBy);
                var target = storage.SqlServer(sql);
                if (ran.Count > 0) copied += $" Ran {ran.Count} data migration{(ran.Count == 1 ? "" : "s")}.";
                if (saved.Provider != StorageProviders.SqlServer && (DatabaseStorage.IsEmpty(target) || input.CopyData))
                    copied += $" {DatabaseStorage.CopyAll(current, target)} records copied into it.";
            }
            else if (saved.Provider == StorageProviders.SqlServer && (DatabaseStorage.IsEmpty(storage.JsonStore) || input.CopyData))
            {
                copied += $" {DatabaseStorage.CopyAll(current, storage.JsonStore)} records copied into it.";
            }
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            return ApiResponse.Fail(400, "CONNECTION_FAILED", "Not switched: " + ex.Message);
        }
        storage.SettingsFile.Save(new DatabaseSettings { Provider = provider, SqlServer = sql, ChangedBy = user.ChangedBy, ChangedAt = DateTimeOffset.Now });
        return ApiResponse.Ok((provider == StorageProviders.SqlServer ? "Now using SQL Server." : "Now using the JSON files.") + copied);
    }

    private ApiResponse Migrations()
    {
        var settings = storage.SettingsFile.Load();
        IReadOnlyList<AppliedMigration> applied = Array.Empty<AppliedMigration>();
        string? error = null;
        if (settings.Provider == StorageProviders.SqlServer)
        {
            try { applied = storage.AppliedMigrations(settings.SqlServer); }
            catch (Exception ex) when (ex is DbException or InvalidOperationException) { error = ex.Message; }
        }
        return ApiResponse.Of(new
        {
            sqlServer = settings.Provider == StorageProviders.SqlServer,
            error,
            migrations = DataMigrations.All.Select(m =>
            {
                var run = applied.FirstOrDefault(a => a.Id == m.Id);
                return new { m.Id, m.Title, m.Description, m.Sql, applied = run is not null, appliedBy = run?.AppliedBy, appliedAtUtc = run?.AppliedAtUtc };
            }),
        });
    }

    private ApiResponse RunMigration(string id, ApiUser user)
    {
        var migration = DataMigrations.Find(id);
        if (migration is null) return ApiResponse.NotFound();
        var settings = storage.SettingsFile.Load();
        if (settings.Provider != StorageProviders.SqlServer) return ApiResponse.Fail(400, "NOT_SQL_SERVER", "Data migrations run on SQL Server; the data is in the JSON files now.");
        try
        {
            storage.RunMigration(settings.SqlServer, migration, user.ChangedBy);
            return ApiResponse.Ok($"The migration {migration.Title} ran.");
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            return ApiResponse.Fail(400, "MIGRATION_FAILED", "The migration did not run, nothing was changed: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------- settings

    private ApiResponse Settings()
    {
        var s = w.Settings();
        return ApiResponse.Of(new
        {
            settings = SettingsView.Of(s),
            jobs = Data.Jobs.All.Select(j => new { job = j, title = Data.Jobs.Title(j), schedule = s.Jobs.For(j) }),
            databases = ExternalNames.All.Select(n => new { name = n, title = ExternalNames.Title(n), configured = w.Databases.IsConfigured(n) }),
        });
    }

    /// <summary>Saves one section of the settings. A secret left blank keeps the one saved before.</summary>
    private ApiResponse SaveSection(string section, string body, ApiUser user)
    {
        JToken input;
        try { input = JToken.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); }
        catch (JsonException) { throw new BadInputException(); }
        var serializer = JsonSerializer.Create(MyCRMRecords.Json.Settings);
        string? error = null;
        w.SettingsFile.Change(s =>
        {
            switch (section)
            {
                case "connections": s.Connections = KeepSecrets(s.Connections, input.ToObject<ConnectionSettings>(serializer)!); break;
                case "email":
                    var email = KeepSecrets(s.Email, input.ToObject<EmailSettings>(serializer)!);
                    error = email.Port is < 1 or > 65535 ? "Enter the port: a number from 1 to 65535, usually 25."
                        : !Strings.IsEmail(email.From) ? "Enter the sender's email address."
                        : email.RedirectTo.Trim().Length > 0 && !Strings.IsEmail(email.RedirectTo) ? "Enter one email address to redirect to, or leave it blank."
                        : null;
                    if (error is null) s.Email = email;
                    break;
                case "jobs": s.Jobs = input.ToObject<JobSettings>(serializer)!; break;
                default: error = "No such settings."; return;
            }
            if (error is not null) return;
            s.ChangedBy = user.ChangedBy;
            s.ChangedAt = DateTimeOffset.Now;
        });
        return error is null ? ApiResponse.Ok("Saved.") : ApiResponse.Fail(400, ResultCodes.Validation, error);
    }

    /// <summary>Why a list of email addresses (separated by commas) cannot be kept, or null.</summary>
    private static string? BadList(string list)
    {
        var bad = (list ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).Where(e => e.Length > 0 && !Strings.IsEmail(e)).ToList();
        return bad.Count == 0 ? null : $"{bad[0]} is not an email address. Separate the addresses with commas.";
    }

    /// <summary>The new settings, with each blank secret taken from the saved ones (also inside nested settings).</summary>
    public static T KeepSecrets<T>(T saved, T incoming) where T : class
    {
        Keep(saved, incoming);
        return incoming;
    }

    private static void Keep(object saved, object incoming)
    {
        foreach (var property in incoming.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite))
        {
            var mine = property.GetValue(incoming);
            var theirs = property.GetValue(saved);
            if (property.PropertyType == typeof(string))
            {
                if (property.GetCustomAttribute<SecretAttribute>() is not null && string.IsNullOrEmpty((string?)mine)) property.SetValue(incoming, theirs ?? "");
            }
            else if (mine is not null && theirs is not null && property.PropertyType.IsClass && mine is not IList
                && property.PropertyType.Namespace?.StartsWith("MyCRMRecords", StringComparison.Ordinal) == true)
            {
                Keep(theirs, mine);
            }
        }
    }

    private ApiResponse Jobs()
    {
        var s = w.Settings();
        var starts = w.Scheduler.LastStarts();
        return ApiResponse.Of(new
        {
            enabled = s.Jobs.Enabled,
            jobs = Data.Jobs.All.Select(j => new
            {
                job = j,
                title = Data.Jobs.Title(j),
                schedule = s.Jobs.For(j),
                lastStart = starts.TryGetValue(j, out var at) ? at : null,
                runs = w.Store.Find<JobRun>(Query.Where("Job", j).Newest().First(5)),
            }),
        });
    }

    private void Import(JobRun run, string by)
    {
        try
        {
            var counts = import.Import(by, progress =>
            {
                // Marked stopped meanwhile (the website restarted and the new one took over): this one stops too.
                if (w.Store.Get<JobRun>(run.Id) is { } saved && saved.Outcome != "Running") throw new OperationCanceledException();
                run.Summary = progress;
                w.Store.Update(run);
            });
            run.Outcome = "Done";
            run.Processed = counts.Copied.Values.Sum();
            run.Summary = string.Join("; ", counts.Copied.Select(c => $"{c.Key}: {c.Value} copied")) + (counts.Notes.Count == 0 ? "" : ". " + string.Join(" ", counts.Notes));
        }
        catch (OperationCanceledException)
        {
            BackgroundRuns.Ended(run.Id);
            return;
        }
        catch (Exception ex)
        {
            run.Outcome = "Failed";
            run.Summary = "The import stopped: " + ex.Message + " What was copied before stays; importing again skips it.";
        }
        run.FinishedAt = w.Clock();
        w.Store.Update(run);
        BackgroundRuns.Ended(run.Id);
    }
}
