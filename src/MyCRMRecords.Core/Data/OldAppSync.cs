using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MyCRMRecords.Domain;
using OldRow = System.Collections.Generic.Dictionary<string, object?>;

namespace MyCRMRecords.Data;

// The two-way sync with the old OutSystems app this app replaces, while both are used. The same engine is in every app
// that replaces an OutSystems app (MyWarehouse, MyServiceParts, MyDeliveries, MyCRMRecords); only the entities (the table mapping,
// PartRequestsSync.cs here) differ. The rules, as the user set them:
//  - what is added or changed on either side is copied to the other, every few minutes;
//  - nothing is ever deleted in the old app (IOldTablesWriter cannot delete): a record removed here is marked there
//    (IsActive = false) or left as it is; nor here: a record the old app deleted is marked here or kept, and a pair that
//    ends is marked Unpaired;
//  - new records take their number from the old app (Add), so the two never give one number to different records;
//  - every change made in the old app is kept with the value it replaced (SyncChange), every sync is logged (SyncRun),
//    and a sync that would change more than people do in a few minutes stops before changing anything;
//  - it runs only once an administrator turns it on (SyncSettings.Enabled starts off).

/// <summary>The sync with the old app, as set on the admin page.</summary>
public sealed class SyncSettings
{
    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; } = 5;
    /// <summary>A sync that would add or change more records than this on either side stops before changing anything.</summary>
    public int LargeSync { get; set; } = 200;
    /// <summary>Why the scheduled syncs are paused (a sync stopped for safety); empty while they run. A sync from the admin page clears it.</summary>
    public string Stopped { get; set; } = "";
}

/// <summary>What a sync did (or, previewed, would do): a sentence and the details.</summary>
public sealed record SyncOutcome(bool Ok, string Message, IReadOnlyList<string> Details, int ToOldApp = 0, int ToHere = 0);

/// <summary>The fields of one record type that are synced, and how they map to the old app's table.</summary>
public interface ISyncEntity
{
    string Name { get; }
    string OldTable { get; }
    Type RecordType { get; }
    bool TakesOldNumbers { get; }
    void Accept(ISyncEntityVisitor visitor);
}

/// <summary>Something done with an entity of whatever record type (the sync's steps).</summary>
public interface ISyncEntityVisitor
{
    void Visit<T>(SyncEntity<T> entity) where T : class, IRecord, new();
}

/// <summary>
/// How one record type maps to one old table. <see cref="ToOld"/> gives the old columns of a record (with the old row as it
/// is, when there is one); <see cref="FromOld"/> makes the record from an old row (keeping the fields of <c>existing</c> that
/// the old app has no column for; null when it cannot be placed yet, e.g. its parent is not here).
/// </summary>
/// <summary>An entity synced for recent records only (<see cref="SyncEntity{T}.RecentDays"/>).</summary>
public interface IRecentOnly
{
    int? RecentDays { get; }
}

public sealed class SyncEntity<T> : ISyncEntity, IRecentOnly where T : class, IRecord, new()
{
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public string OldTable { get; init; } = "";
    public Type RecordType => typeof(T);
    public void Accept(ISyncEntityVisitor visitor) => visitor.Visit(this);
    /// <summary>The columns that find a row; "ID" for OutSystems entities.</summary>
    public string[] KeyColumns { get; init; } = ["ID"];
    /// <summary>Every synced column with the record's value (for an empty record too: its keys are the columns the sync compares).</summary>
    public Func<T, SyncContext, OldRow?, Dictionary<string, object?>> ToOld { get; init; } = (_, _, _) => new Dictionary<string, object?>();
    /// <summary>Whether the record can be written to the old app now (what it refers to is there); null: always.</summary>
    public Func<T, SyncContext, bool>? CanWrite { get; init; }
    public Func<OldRow, SyncContext, T?, T?> FromOld { get; init; } = (_, _, _) => null;
    /// <summary>The old app's Id kept on the record (OutSystemsId): pairs records imported before the sync was turned on.</summary>
    public Func<T, long?>? OldIdOf { get; init; }
    public Action<T, long>? SetOldId { get; init; }
    /// <summary>Pairs a record with an old row when no Id does (a natural key; the context translates its parent's Id).</summary>
    public Func<T, OldRow, SyncContext, bool>? SameRecord { get; init; }
    /// <summary>When the record last changed, to choose between the two versions the first time a record is paired.</summary>
    public Func<T, DateTime?>? UpdatedOn { get; init; }
    public string? OldUpdatedOnColumn { get; init; } = "UPDATEDON";
    /// <summary>A row added in the old app this recently is left for the next sync (it may be one this app is adding).</summary>
    public string? OldCreatedOnColumn { get; init; } = "CREATEDON";
    /// <summary>
    /// Only records made in the last this many days are synced (large logs, which are added to and seldom changed):
    /// older ones are left as they are on both sides. Needs <see cref="CreatedOnColumn"/> and <see cref="OldCreatedOnColumn"/>.
    /// </summary>
    public int? RecentDays { get; init; }
    /// <summary>The record's column of when it was made ("CreatedOn"), for <see cref="RecentDays"/>.</summary>
    public string? CreatedOnColumn { get; init; }
    /// <summary>
    /// Lines with no date of their own, synced with their parent's recent records only: the parent entity (synced with
    /// <see cref="RecentDays"/>, and before this one), the old column of the parent's Id and the record's column of it.
    /// Set <see cref="RecentDays"/> to the parent's too, so the pairs of older lines are left alone.
    /// </summary>
    public (string Entity, string OldColumn, string Column)? RecentParent { get; init; }
    /// <summary>What marks an old row removed (ISACTIVE = false); null: the row is left as it is.</summary>
    public Dictionary<string, object?>? MarkRemovedThere { get; init; }
    /// <summary>What marks a record removed here when the old app deleted it (IsActive = false); null: it is deleted here.</summary>
    public Action<T>? MarkRemovedHere { get; init; }
    /// <summary>New records take their Id from the old app's table (see <see cref="OldAppSync.Add{T}"/>).</summary>
    public bool TakesOldNumbers { get; init; }
    /// <summary>The records this one refers to (entity name, the record's Id in it), which must be in the old app first.</summary>
    public IReadOnlyList<(string Entity, Func<T, long?> Id)> Parents { get; init; } = [];
    /// <summary>
    /// The record an old row belongs to (its entity, label and the old column with its Id): named in the sync's notes
    /// when the row cannot be copied here, with whether the old app has it at all.
    /// </summary>
    public (string Entity, string Label, string OldColumn)? OldParent { get; init; }
    public Func<T, string> Describe { get; init; } = r => r.Id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>What the entities' mappings need: the pairs made so far (to translate references), users and static entities.</summary>
public sealed class SyncContext
{
    private readonly Dictionary<string, Dictionary<long, SyncLink>> _byLocal = new();
    private readonly Dictionary<string, Dictionary<string, SyncLink>> _byOld = new();
    private readonly IOldTables _tables;
    private readonly Dictionary<string, IReadOnlyDictionary<long, string>> _labels = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<OldUser>? _users;

    public SyncContext(IOldTables tables, IEnumerable<SyncLink> links, DateTime now)
    {
        _tables = tables;
        Now = now;
        foreach (var link in links) Add(link);
    }

    public DateTime Now { get; }
    public string OldApp => _tables.Title;

    public void Add(SyncLink link)
    {
        if (!_byLocal.TryGetValue(link.Entity, out var local)) _byLocal[link.Entity] = local = new Dictionary<long, SyncLink>();
        if (!_byOld.TryGetValue(link.Entity, out var old)) _byOld[link.Entity] = old = new Dictionary<string, SyncLink>(StringComparer.Ordinal);
        local[link.LocalId] = link;
        old[link.OldKey] = link;
    }

    public void Remove(SyncLink link)
    {
        if (_byLocal.TryGetValue(link.Entity, out var local) && local.TryGetValue(link.LocalId, out var a) && ReferenceEquals(a, link)) local.Remove(link.LocalId);
        if (_byOld.TryGetValue(link.Entity, out var old) && old.TryGetValue(link.OldKey, out var b) && ReferenceEquals(b, link)) old.Remove(link.OldKey);
    }

    public IEnumerable<SyncLink> Links(string entity) =>
        _byLocal.TryGetValue(entity, out var links) ? links.Values.ToList() : (IEnumerable<SyncLink>)[];

    public SyncLink? ByLocal(string entity, long localId) =>
        _byLocal.TryGetValue(entity, out var links) && links.TryGetValue(localId, out var link) ? link : null;

    public SyncLink? ByOld(string entity, string oldKey) =>
        _byOld.TryGetValue(entity, out var links) && links.TryGetValue(oldKey, out var link) ? link : null;

    /// <summary>The old app's Id of a record here (null when it is not there, or not yet).</summary>
    public long? OldId(string entity, long? localId) =>
        localId is { } id && id != 0 && ByLocal(entity, id) is { RemovedThere: false } link
            && long.TryParse(link.OldKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var old) ? old : null;

    /// <summary>The Id here of a row of the old app (null when it has no record here, or not yet).</summary>
    public long? LocalId(string entity, long? oldId) =>
        oldId is { } id && id != 0 && ByOld(entity, id.ToString(CultureInfo.InvariantCulture)) is { RemovedHere: false } link ? link.LocalId : null;

    public IReadOnlyList<OldUser> Users => _users ??= _tables.Users();

    // The users by Id, login, name and email (the first of each, as a search of the list would find), made once: a sync
    // looks people up for every record, and searching thousands of users each time made a sync of hundreds of thousands
    // of records (MyCRMRecords' first sync after the import) take hours.
    private Dictionary<long, OldUser>? _byId;
    private Dictionary<string, OldUser>? _byLogin, _byName, _byEmail;

    private void Index()
    {
        if (_byId is not null) return;
        _byId = new Dictionary<long, OldUser>();
        _byLogin = new Dictionary<string, OldUser>(StringComparer.OrdinalIgnoreCase);
        _byName = new Dictionary<string, OldUser>(StringComparer.OrdinalIgnoreCase);
        _byEmail = new Dictionary<string, OldUser>(StringComparer.OrdinalIgnoreCase);
        static void Keep<TKey>(Dictionary<TKey, OldUser> index, TKey key, OldUser user) where TKey : notnull
        {
            if (!index.ContainsKey(key)) index[key] = user;
        }
        foreach (var user in Users)
        {
            Keep(_byId, user.Id, user);
            if ((user.Login ?? "").Trim() is { Length: > 0 } login) Keep(_byLogin, login, user);
            if ((user.Name ?? "").Trim() is { Length: > 0 } name) Keep(_byName, name, user);
            if ((user.Email ?? "").Trim() is { Length: > 0 } email) Keep(_byEmail, email, user);
        }
    }

    /// <summary>The old app's user of a login (RSP\jsmith), or of a name when no login matches; null when unknown.</summary>
    public long? OldUser(string? loginOrName)
    {
        var text = (loginOrName ?? "").Trim();
        if (text.Length == 0) return null;
        Index();
        return (_byLogin!.TryGetValue(text, out var byLogin) ? byLogin : _byName!.TryGetValue(text, out var byName) ? byName
            : _byEmail!.TryGetValue(text, out var byEmail) ? byEmail : null)?.Id;
    }

    public OldUser? User(object? id)
    {
        var value = OldValues.Long(id);
        if (value == 0) return null;
        Index();
        return _byId!.TryGetValue(value, out var user) ? user : null;
    }

    /// <summary>A static entity's Id whose label matches (letters and digits only, ignoring case); null when none does.</summary>
    public long? LabelId(string staticEntity, params string[] labels)
    {
        static string Key(string text) => new string(text.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var wanted = labels.Select(Key).Where(k => k.Length > 0).ToList();
        foreach (var pair in Labels(staticEntity))
            if (wanted.Contains(Key(pair.Value))) return pair.Key;
        return null;
    }

    public IReadOnlyDictionary<long, string> Labels(string staticEntity)
    {
        if (!_labels.TryGetValue(staticEntity, out var labels)) _labels[staticEntity] = labels = _tables.Labels(staticEntity);
        return labels;
    }

    public string Label(string staticEntity, object? id) => Labels(staticEntity).TryGetValue(OldValues.Long(id), out var label) ? label : "";
}

/// <summary>Reading old rows' values the way OutSystems keeps them (1900-01-01 is no date, NULL is 0 or empty).</summary>
public static class OldValues
{
    public static readonly DateTime NoDate = new(1900, 1, 1);

    public static string Text(OldRow row, string column) => row.TryGetValue(column, out var v) ? OutSystemsValues.Text(v) : "";
    public static DateTime? Date(OldRow row, string column) => row.TryGetValue(column, out var v) ? OutSystemsValues.Date(v) : null;
    public static long Long(OldRow row, string column) => row.TryGetValue(column, out var v) ? Long(v) : 0;
    public static long Long(object? value) => value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    public static int Int(OldRow row, string column) =>
        row.TryGetValue(column, out var v) && v is not null and not DBNull ? Convert.ToInt32(Convert.ToDecimal(v, CultureInfo.InvariantCulture)) : 0;
    public static decimal Decimal(OldRow row, string column) =>
        row.TryGetValue(column, out var v) && v is not null and not DBNull ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : 0;
    public static bool Bool(OldRow row, string column) =>
        row.TryGetValue(column, out var v) && v is not null and not DBNull && Convert.ToBoolean(v, CultureInfo.InvariantCulture);

    /// <summary>A value as one comparable text: equal texts mean the old app would show the same thing.</summary>
    private static string Sha256(byte[] bytes)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    public static string Canonical(object? value) => value switch
    {
        null or DBNull => "",
        string s => s,
        DateTime d => d <= NoDate ? "" : d.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset o => Canonical(o.DateTime),
        bool b => b ? "1" : "0",
        // A file's bytes: its length and SHA-256, so a changed file is seen without keeping it in the fingerprints.
        byte[] bytes => bytes.Length == 0 ? "" : "bytes:" + bytes.Length.ToString(CultureInfo.InvariantCulture) + ":" + Sha256(bytes),
        decimal m => (m / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture),
        double f => ((decimal)f / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture),
        float f => ((decimal)f / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture),
        IFormattable n => n.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}

/// <summary>A sync stopped before changing anything (too large), or a preview: what it found.</summary>
internal sealed class SyncStoppedException(string message, IReadOnlyList<string> details, bool preview = false) : Exception(message)
{
    public IReadOnlyList<string> Details { get; } = details;
    public bool Preview { get; } = preview;
}

/// <summary>
/// Keeps this app and the old app in step both ways (see the rules at the top of this file). Each record is paired with its
/// old row in dbo.<c>SyncLink</c>, which keeps a fingerprint of both as they were at the last sync: the side whose
/// fingerprint changed since has changed, and its version is copied to the other. Changed on both sides, this app's
/// version is kept (and the details say so).
/// </summary>
public sealed class OldAppSync
{
    private readonly IRecordStore _store;
    private readonly Func<IOldTables?> _tables;
    private readonly IReadOnlyList<ISyncEntity> _entities;
    private readonly Func<SyncSettings> _settings;
    private readonly Action<Action<SyncSettings>> _changeSettings;
    private readonly string _lockFile;
    private readonly Func<DateTime> _clock;

    /// <param name="store">This app's records (not wrapped in a <see cref="SyncedStore"/>).</param>
    /// <param name="tables">The old app's tables; null while its database is not set up.</param>
    /// <param name="entities">The synced records, parents before children.</param>
    public OldAppSync(IRecordStore store, Func<IOldTables?> tables, IReadOnlyList<ISyncEntity> entities, Func<SyncSettings> settings,
        Action<Action<SyncSettings>> changeSettings, string lockFile, Func<DateTime> clock)
    {
        _store = store;
        _tables = tables;
        _entities = entities;
        _settings = settings;
        _changeSettings = changeSettings;
        _lockFile = lockFile;
        _clock = clock;
    }

    public IReadOnlyList<ISyncEntity> Entities => _entities;
    public bool Enabled => _settings().Enabled;
    public IOldTables? Tables => _tables();

    /// <summary>Runs the sync when it is on and due (the scheduler's timer, every 30 seconds); not while stopped for safety.</summary>
    public void Tick()
    {
        var settings = _settings();
        if (!settings.Enabled || settings.Stopped.Length > 0 || _tables() is null) return;
        var last = _store.FindFirst<SyncRun>(Query.All().Sort("Id", descending: true));
        if (last is not null && last.StartedOn > _clock().AddMinutes(-Math.Max(1, settings.IntervalMinutes)).AddSeconds(20)
            && last.StartedOn <= _clock()) return;
        Run("Scheduled");
    }

    /// <summary>What a sync would do now, changing nothing on either side.</summary>
    public SyncOutcome Preview() => Execute("Preview", allowLarge: true, preview: true);

    /// <summary>
    /// Syncs now. A sync that would change more than <see cref="SyncSettings.LargeSync"/> records on either side stops
    /// before changing anything (and the scheduled syncs pause) unless <paramref name="allowLarge"/>.
    /// </summary>
    public SyncOutcome Run(string by, bool allowLarge = false) => Execute(by, allowLarge, preview: false);

    private SyncOutcome Execute(string by, bool allowLarge, bool preview)
    {
        var tables = _tables();
        if (tables is null) return new SyncOutcome(false, "The OutSystems database is not set up (Connections tab).", []);
        using var held = Hold(TimeSpan.Zero);
        if (held is null) return new SyncOutcome(false, $"A sync with {tables.Title} is already running.", []);
        StopCutOffRuns();
        var startedOn = _clock();
        var run = preview ? null : _store.Insert(new SyncRun { StartedOn = startedOn, By = Strings.Shorten(by, 256), Message = "Running…" });
        SyncOutcome outcome;
        var stopped = "";
        try
        {
            outcome = SyncNow(tables, run, allowLarge, preview);
        }
        catch (SyncStoppedException ex) when (ex.Preview)
        {
            outcome = new SyncOutcome(true, ex.Message, ex.Details);
        }
        catch (SyncStoppedException ex)
        {
            outcome = new SyncOutcome(false, ex.Message, ex.Details);
            stopped = ex.Message;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Nothing was kept on either side (both are undone together), except what the Pending pairs say.
            outcome = new SyncOutcome(false, $"The sync with {tables.Title} failed: {ex.Message}", [ex.GetType().Name + ": " + ex.Message]);
        }
        if (run is null) return outcome;
        run.FinishedOn = _clock();
        run.Ok = outcome.Ok;
        run.Message = Strings.Shorten(outcome.Message, 1000);
        run.Details = string.Join("\n", outcome.Details);
        run.ToOldApp = outcome.ToOldApp;
        run.ToHere = outcome.ToHere;
        _store.Update(run);
        if (stopped.Length > 0) _changeSettings(s => s.Stopped = stopped);
        else if (outcome.Ok && by != "Scheduled" && _settings().Stopped.Length > 0) _changeSettings(s => s.Stopped = "");
        return outcome;
    }

    private SyncOutcome SyncNow(IOldTables tables, SyncRun? run, bool allowLarge, bool preview)
    {
        var limit = Math.Max(1, _settings().LargeSync);
        using var writer = new RecordingWriter(preview ? null : tables.BeginWrite(), _clock);
        Pass? pass = null;
        _store.Batch(s =>
        {
            pass = new Pass(s, tables, writer, new SyncContext(tables, Links(s), _clock()));
            foreach (var entity in _entities) entity.Accept(pass);
            if (preview)
                throw new SyncStoppedException(pass.Total == 0 ? $"Preview: in step with {tables.Title}, nothing to copy."
                        : $"Preview (nothing has been changed yet): a sync now would change {pass.ToOldApp} record{(pass.ToOldApp == 1 ? "" : "s")} in {tables.Title} and {pass.ToHere} here:",
                    pass.Details(), preview: true);
            if (!allowLarge && (pass.ToOldApp > limit || pass.ToHere > limit))
                throw new SyncStoppedException(
                    $"Stopped before changing anything: this sync would change {pass.ToOldApp} records in {tables.Title} and {pass.ToHere} here, more than people change "
                    + $"in a few minutes (the limit is {limit}). Press Preview to see what it would do; if that is right, tick \"Allow a large sync\" and press Sync now.",
                    pass.Details().Take(200).ToList());
        });
        // This app's changes are saved, with the pairs of what was written to the old app marked Pending: if the old app's
        // commit fails, the next sync sees that the rows were not changed there and writes them again.
        writer.Commit();
        _store.Batch(s =>
        {
            foreach (var link in pass!.Pending)
            {
                link.Pending = false;
                s.Update(link);
            }
            foreach (var change in writer.Changes)
            {
                change.RunId = run!.Id;
                s.Insert(change);
            }
        });
        var total = pass!.Total;
        return new SyncOutcome(true,
            total == 0 ? $"In step with {tables.Title}: nothing to copy."
                : $"Synced with {tables.Title}: {pass.ToOldApp} change{(pass.ToOldApp == 1 ? "" : "s")} copied there, {pass.ToHere} here.",
            pass.Details(), pass.ToOldApp, pass.ToHere);
    }

    /// <summary>
    /// Adds a record (through <see cref="SyncedStore"/>). While the sync is on, a record whose type takes the old app's
    /// numbers is first added to the old app, and takes the Id it got there, so the two never give the same number to
    /// different records; its parents are added there first when they are not yet.
    /// </summary>
    public T Add<T>(IRecordStore store, T record) where T : class, IRecord, new()
    {
        var entity = _entities.OfType<SyncEntity<T>>().FirstOrDefault(e => e.TakesOldNumbers);
        if (entity is null || !_settings().Enabled) return store.Insert(record);
        var tables = _tables() ?? throw new InvalidOperationException("The OutSystems database is not set up (Connections tab): no number can be taken.");
        using var held = Hold(TimeSpan.FromSeconds(60))
            ?? throw new InvalidOperationException($"A sync with {tables.Title} is taking long: try again in a minute.");
        var context = new SyncContext(tables, Links(_store), _clock());
        using var writer = new RecordingWriter(tables.BeginWrite(), _clock);
        var pass = new Pass(_store, tables, writer, context);
        foreach (var (parentName, parentId) in entity.Parents)
            if (parentId(record) is { } id && id != 0 && context.OldId(parentName, id) is null)
                _entities.First(e => e.Name == parentName).Accept(new PushParent(pass, id));
        for (var attempt = 0; ; attempt++)
        {
            var (newId, link, values) = pass.PushNew(entity, record);
            if (store.Get<T>(newId) is null)
            {
                writer.Commit();
                record.Id = newId;
                entity.SetOldId?.Invoke(record, newId);
                store.InsertKeepingIds([record]);
                _store.Batch(s =>
                {
                    foreach (var l in pass.Pending)
                    {
                        l.Pending = false;
                        if (l.Id == 0) s.Insert(l);
                        else s.Update(l);
                    }
                    foreach (var change in writer.Changes) s.Insert(change);
                });
                return record;
            }
            // A record made here before the sync was on has this number: the row just added is marked as removed (the old
            // app keeps it, it is never deleted) and another number is taken.
            if (attempt >= 20) throw new InvalidOperationException($"No free number could be taken from {tables.Title}.");
            pass.Abandon(entity, link, newId, values);
        }
    }

    /// <summary>
    /// Ends the syncs still recorded as running: this one holds the sync, so none of them is (each was cut off: the website
    /// restarted, or the page that started it waited too long and ASP.NET ended it). Nothing they did was kept: both sides
    /// are undone together.
    /// </summary>
    private void StopCutOffRuns()
    {
        foreach (var run in _store.Find<SyncRun>(Query.Where("FinishedOn", null)))
        {
            run.FinishedOn = _clock();
            run.Ok = false;
            run.Message = "Cut off before it finished (the website restarted, or the page that started it waited too long): nothing it did was kept.";
            _store.Update(run);
        }
    }

    /// <summary>The pairs a sync works with: all of them, but of an entity synced for recent records only, those paired in that time.</summary>
    private List<SyncLink> Links(IRecordStore store)
    {
        var links = new List<SyncLink>();
        foreach (var entity in _entities)
        {
            var days = entity is IRecentOnly recent ? recent.RecentDays : null;
            var query = Query.Where("Entity", entity.Name).And("Unpaired", false);
            links.AddRange(store.Find<SyncLink>(days is { } d ? query.AtLeast("SyncedOn", _clock().AddDays(-d - 1)) : query));
        }
        return links;
    }

    private sealed class PushParent(Pass pass, long localId) : ISyncEntityVisitor
    {
        public void Visit<T>(SyncEntity<T> entity) where T : class, IRecord, new() => pass.PushExisting(entity, localId);
    }

    /// <summary>Holds the sync (one at a time, across the IIS worker processes): null when it could not within <paramref name="wait"/>.</summary>
    private FileStream? Hold(TimeSpan wait)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_lockFile))!);
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            try { return new FileStream(_lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(100); }
            catch (IOException) { return null; }
        }
    }

    internal static string Hash(IEnumerable<KeyValuePair<string, object?>> values)
    {
        var text = new StringBuilder();
        foreach (var pair in values.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            text.Append(pair.Key.ToUpperInvariant()).Append('=').Append(OldValues.Canonical(pair.Value).Replace("\n", "\\n")).Append('\n');
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>One sync: the records of every entity compared with their old rows.</summary>
    private sealed class Pass(IRecordStore store, IOldTables tables, RecordingWriter writer, SyncContext context) : ISyncEntityVisitor
    {
        private readonly List<string> _notes = [];
        /// <summary>Of each entity synced for recent records only: the Ids here and there of the records read in this pass.</summary>
        private readonly Dictionary<string, (List<long> Here, List<long> There)> _recent = new();
        private readonly Dictionary<string, HashSet<string>> _oldKeys = new();
        private readonly List<string> _there = [];
        private readonly List<string> _here = [];
        public List<SyncLink> Pending { get; } = [];
        public int ToOldApp => _there.Count;
        public int ToHere => _here.Count;
        public int Total => ToOldApp + ToHere;

        public List<string> Details() =>
            _there.Select(t => $"In {tables.Title}: {t}").Concat(_here.Select(h => "Here: " + h)).Concat(_notes).ToList();

        public void Visit<T>(SyncEntity<T> entity) where T : class, IRecord, new() => ProcessTyped(entity);

        /// <summary>Adds a record that is here but not yet in the old app (a parent of a record being added).</summary>
        public void PushExisting<T>(SyncEntity<T> entity, long localId) where T : class, IRecord, new()
        {
            var record = store.Get<T>(localId);
            if (record is null) return;
            var columns = tables.Columns(entity.OldTable);
            if (Prepare(entity, record, null, columns) is { } values) Insert(entity, record, values, keepIdHere: true);
        }

        /// <summary>Adds a new record (not yet saved here) to the old app; returns the Id it got there and its pair.</summary>
        public (long Id, SyncLink Link, Dictionary<string, object?> Values) PushNew<T>(SyncEntity<T> entity, T record) where T : class, IRecord, new()
        {
            var columns = tables.Columns(entity.OldTable);
            var values = Prepare(entity, record, null, columns)
                ?? throw new InvalidOperationException($"The {entity.Label} cannot be added to {tables.Title} yet: what it refers to is not there.");
            var id = writer.Insert(entity.OldTable, values, entity.Describe(record));
            var link = new SyncLink
            {
                Entity = entity.Name, LocalId = id, OldKey = id.ToString(CultureInfo.InvariantCulture), LocalHash = Hash(values), OldHash = Hash(values),
                Pending = true, SyncedOn = context.Now,
            };
            context.Add(link);
            Pending.Add(link);
            return (id, link, values);
        }

        /// <summary>
        /// A row added for a number that is used here: marked removed in the old app (never deleted), and kept paired with
        /// no record here (a negative Id) so it is not copied here.
        /// </summary>
        public void Abandon<T>(SyncEntity<T> entity, SyncLink link, long id, Dictionary<string, object?> values) where T : class, IRecord, new()
        {
            context.Remove(link);
            if (entity.MarkRemovedThere is { } mark)
                writer.Update(entity.OldTable, new Dictionary<string, object?> { ["ID"] = id }, mark, new OldRow(values, StringComparer.OrdinalIgnoreCase), $"number {id} (not used)");
            link.LocalId = -id;
            link.RemovedHere = true;
            link.OldHash = HashOfRow(Merged(new OldRow(values, StringComparer.OrdinalIgnoreCase), entity.MarkRemovedThere), MappedColumns(entity, tables.Columns(entity.OldTable), null));
            context.Add(link);
        }

        private string KeyOf<T>(SyncEntity<T> entity, OldRow row) where T : class, IRecord, new() =>
            string.Join("|", entity.KeyColumns.Select(c => OldValues.Canonical(row.TryGetValue(c, out var v) ? v : null)));

        /// <summary>A pair's old key as the key columns' values (null when it does not split into them).</summary>
        private static Dictionary<string, object?>? OldKeyValues<T>(SyncEntity<T> entity, string oldKey) where T : class, IRecord, new()
        {
            var parts = entity.KeyColumns.Length == 1 ? new[] { oldKey } : oldKey.Split('|');
            if (parts.Length != entity.KeyColumns.Length) return null;
            var key = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < parts.Length; i++) key[entity.KeyColumns[i]] = parts[i];
            return key;
        }

        private static Dictionary<string, object?> KeyValues<T>(SyncEntity<T> entity, OldRow row) where T : class, IRecord, new() =>
            entity.KeyColumns.ToDictionary(c => c, c => row.TryGetValue(c, out var v) ? v : null, StringComparer.OrdinalIgnoreCase);

        /// <summary>The record's old columns as they would be written: only the table's columns, text cut to their length, no NULL where none is allowed, whole seconds.</summary>
        private Dictionary<string, object?>? Prepare<T>(SyncEntity<T> entity, T record, OldRow? row, IReadOnlyDictionary<string, OldColumn> columns)
            where T : class, IRecord, new()
        {
            if (entity.CanWrite is { } canWrite && !canWrite(record, context)) return null;
            var values = entity.ToOld(record, context, row);
            var prepared = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in values)
            {
                if (!columns.TryGetValue(pair.Key, out var column) || column.Identity) continue;
                var value = pair.Value;
                if (value is string text && column.Length > 0 && text.Length > column.Length) value = text.Substring(0, column.Length);
                if (value is DateTime date) value = new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second);
                if (value is null && !column.Nullable)
                    value = column.SqlType switch
                    {
                        "datetime" or "datetime2" or "date" or "smalldatetime" => OldValues.NoDate,
                        "bit" => false,
                        "int" or "bigint" or "smallint" or "tinyint" => 0,
                        "decimal" or "numeric" or "float" or "real" or "money" => 0m,
                        _ => "",
                    };
                prepared[column.Name] = value;
            }
            return prepared;
        }

        /// <summary>The fingerprint of a record here: its old columns as they would be written to a new row (the same whatever the old row has).</summary>
        private string LocalHash<T>(SyncEntity<T> entity, T record, IReadOnlyDictionary<string, OldColumn> columns) where T : class, IRecord, new() =>
            Prepare(entity, record, null, columns) is { } values ? Hash(values) : "";

        private static string HashOfRow(OldRow row, IEnumerable<string> columns) =>
            Hash(columns.Select(c => new KeyValuePair<string, object?>(c, row.TryGetValue(c, out var v) ? v : null)));

        /// <summary>The columns the sync writes for this entity (those its mapping gives for an empty record).</summary>
        private List<string> MappedColumns<T>(SyncEntity<T> entity, IReadOnlyDictionary<string, OldColumn> columns, OldRow? sample) where T : class, IRecord, new()
        {
            var empty = entity.ToOld(new T(), context, sample);
            return empty.Keys.Where(k => columns.TryGetValue(k, out var c) && !c.Identity).Select(k => columns[k].Name).ToList();
        }

        private void ProcessTyped<T>(SyncEntity<T> entity) where T : class, IRecord, new()
        {
            var columns = tables.Columns(entity.OldTable);
            DateTime? since = entity.RecentDays is { } days && entity.CreatedOnColumn is not null && entity.OldCreatedOnColumn is not null
                ? context.Now.AddDays(-days) : null;
            var parent = entity.RecentParent is { } p && _recent.TryGetValue(p.Entity, out var recent) ? recent : ((List<long> Here, List<long> There)?)null;
            var windowed = since is not null || parent is not null;
            IReadOnlyList<OldRow> rows;
            List<T> found;
            if (parent is { } lines && entity.RecentParent is { } of)
            {
                rows = tables.Rows(entity.OldTable, of.OldColumn, lines.There);
                found = [];
                foreach (var chunk in lines.Here.Select((id, i) => (id, i)).GroupBy(x => x.i / 1000, x => (object)x.id))
                    found.AddRange(store.Find<T>(Query.All().In(of.Column, chunk)));
            }
            else
            {
                rows = since is null ? tables.Rows(entity.OldTable) : tables.Rows(entity.OldTable, entity.OldCreatedOnColumn, since);
                found = since is null ? store.Find<T>() : store.Find<T>(Query.All().AtLeast(entity.CreatedOnColumn!, since.Value));
            }
            var mapped = MappedColumns(entity, columns, rows.FirstOrDefault());
            var rowsByKey = new Dictionary<string, OldRow>(StringComparer.Ordinal);
            foreach (var row in rows) rowsByKey[KeyOf(entity, row)] = row;
            if (!windowed) _oldKeys[entity.Name] = new HashSet<string>(rowsByKey.Keys, StringComparer.Ordinal);
            var locals = found.ToDictionary(r => r.Id);
            if (since is not null)
                _recent[entity.Name] = (locals.Keys.ToList(), rows.Select(r => OldValues.Long(r, "ID")).Where(id => id != 0).ToList());
            var pairedLocal = new HashSet<long>();
            var pairedOld = new HashSet<string>(StringComparer.Ordinal);

            foreach (var link in context.Links(entity.Name))
            {
                locals.TryGetValue(link.LocalId, out var x);
                rowsByKey.TryGetValue(link.OldKey, out var r);
                if (windowed && (x is null || r is null))
                {
                    // Synced for recent records only: one side is older than that, or gone; it is looked up to know which.
                    if (x is null && r is null) continue;
                    if (x is null && link.LocalId > 0 && store.Get<T>(link.LocalId) is { } older) x = older;
                    if (r is null && OldKeyValues(entity, link.OldKey) is { } key && tables.Find(entity.OldTable, key) is { } olderRow) r = olderRow;
                }
                if (link.Pending)
                {
                    // The old app's commit of the last sync did not happen (or was not confirmed): what it has decides.
                    if (r is not null && HashOfRow(r, mapped) == link.OldHash) link.Pending = false;
                    else if (r is null)
                    {
                        Unpair(link);
                        continue;
                    }
                    else
                    {
                        // Written again: as changed here (or, for a mark, as removed here and not yet marked).
                        link.Pending = false;
                        link.LocalHash = "";
                        link.RemovedHere = false;
                    }
                }
                pairedLocal.Add(link.LocalId);
                pairedOld.Add(link.OldKey);
                // A record that cannot be written to the old app now (CanWrite) does not count as changed here: the old row may still change.
                var localHash = x is null ? "" : LocalHash(entity, x, columns);
                var localChanged = x is not null && !link.RemovedHere && localHash.Length > 0 && localHash != link.LocalHash;
                var oldChanged = r is not null && HashOfRow(r, mapped) != link.OldHash;

                if (x is not null && r is not null)
                {
                    if (link.RemovedThere || link.RemovedHere)
                    {
                        // Back on both sides (removed, then changed again): paired as usual from now on.
                        link.RemovedThere = link.RemovedHere = false;
                    }
                    if (localChanged)
                    {
                        if (oldChanged) _notes.Add($"The {entity.Label} {entity.Describe(x)} was changed here and in {tables.Title}: this app's version was kept.");
                        Push(entity, x, r, link, columns, mapped);
                    }
                    else if (oldChanged) Pull(entity, x, r, link, columns, mapped);
                }
                else if (x is null && r is not null)
                {
                    if (!link.RemovedHere)
                    {
                        // Removed here: marked in the old app (never deleted there), and kept paired so it is not copied back.
                        if (entity.MarkRemovedThere is { } mark && Differs(mark, r))
                        {
                            writer.Update(entity.OldTable, KeyValues(entity, r), Changed(mark, r), r, Describe(entity, r));
                            _there.Add($"the {entity.Label} {Describe(entity, r)} marked as removed (it was deleted here).");
                            Pending.Add(link);
                            link.Pending = true;
                        }
                        else _notes.Add($"The {entity.Label} {Describe(entity, r)} was deleted here; {tables.Title} keeps it (the sync never deletes there).");
                        link.RemovedHere = true;
                        link.OldHash = HashOfRow(Merged(r, entity.MarkRemovedThere), mapped);
                        Save(link);
                    }
                    else if (oldChanged)
                    {
                        // Removed here, but changed in the old app since: copied here again.
                        if (entity.FromOld(r, context, null) is { } fresh)
                        {
                            AddHere(entity, fresh, r, link.LocalId);
                            link.LocalId = fresh.Id;
                            link.RemovedHere = false;
                            Remember(entity, link, fresh, r, columns, mapped);
                            _here.Add($"the {entity.Label} {entity.Describe(fresh)} added again (it was deleted here but changed in {tables.Title} since).");
                        }
                    }
                }
                else if (x is not null && r is null)
                {
                    if (link.RemovedThere && !localChanged) continue;
                    if (!localChanged || link.RemovedThere)
                    {
                        // Deleted in the old app: marked here (records are never deleted here either), or left as it is.
                        if (entity.MarkRemovedHere is { } markHere)
                        {
                            var table = Table.Of<T>();
                            var before = table.Columns.Select(c => c.Get(x)).ToList();
                            markHere(x);
                            if (table.Columns.Select(c => c.Get(x)).Where((v, i) => !Equals(v, before[i])).Any())
                            {
                                store.Update(x);
                                _here.Add($"the {entity.Label} {entity.Describe(x)} marked as removed (it was deleted in {tables.Title}).");
                            }
                        }
                        else _notes.Add($"The {entity.Label} {entity.Describe(x)} was deleted in {tables.Title}; it is kept here (nothing is deleted).");
                        link.RemovedThere = true;
                        link.LocalHash = LocalHash(entity, x, columns);
                        Save(link);
                    }
                    else
                    {
                        // Deleted in the old app but changed here since: added there again.
                        Unpair(link);
                        pairedLocal.Remove(x.Id);
                        _notes.Add($"The {entity.Label} {entity.Describe(x)} was deleted in {tables.Title} but changed here since, so it is added there again.");
                    }
                }
                else Unpair(link);
            }

            // Records here not paired yet: paired with their old row (imported before the sync was on), or added there.
            var unpairedRows = rowsByKey.Where(p => !pairedOld.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            foreach (var x in locals.Values.Where(r => !pairedLocal.Contains(r.Id)).OrderBy(r => r.Id))
            {
                OldRow? match = null;
                if (entity.OldIdOf?.Invoke(x) is { } oldId && oldId != 0 && unpairedRows.TryGetValue(oldId.ToString(CultureInfo.InvariantCulture), out var byId))
                    match = byId;
                else if (entity.SameRecord is { } same) match = unpairedRows.Values.FirstOrDefault(r => same(x, r, context));
                if (match is not null)
                {
                    var key = KeyOf(entity, match);
                    unpairedRows.Remove(key);
                    var link = new SyncLink { Entity = entity.Name, LocalId = x.Id, OldKey = key, SyncedOn = context.Now };
                    context.Add(link);
                    var values = Prepare(entity, x, match, columns);
                    if (values is null) { context.Remove(link); continue; }
                    if (!Differs(values, match))
                    {
                        Remember(entity, link, x, match, columns, mapped);
                        continue;
                    }
                    // Both have it, and they differ: the one changed last is kept. The old app's row was last changed when it
                    // was updated, else when it was created (OutSystems leaves UPDATEDON empty on a row nobody edited). Its
                    // version is kept unless this app's is known to be newer: a record imported from it and not changed here
                    // since is never written back to it.
                    var mine = entity.UpdatedOn?.Invoke(x);
                    var theirs = (entity.OldUpdatedOnColumn is { } c ? OldValues.Date(match, c) : null)
                        ?? (entity.OldCreatedOnColumn is { } created ? OldValues.Date(match, created) : null);
                    if (mine is not null && theirs is not null && mine > theirs) Push(entity, x, match, link, columns, mapped);
                    else Pull(entity, x, match, link, columns, mapped);
                    continue;
                }
                if (Prepare(entity, x, null, columns) is { } fresh) Insert(entity, x, fresh, keepIdHere: true);
                else _notes.Add($"The {entity.Label} {entity.Describe(x)} cannot be added to {tables.Title} yet: what it refers to is not there.");
            }

            // Rows of the old app not paired yet: copied here (a row added in the last two minutes waits for the next sync).
            foreach (var r in unpairedRows.Values)
            {
                if (entity.OldCreatedOnColumn is { } created && OldValues.Date(r, created) is { } on && on > context.Now.AddMinutes(-2)) continue;
                if (entity.FromOld(r, context, null) is not { } fresh)
                {
                    _notes.Add(NotCopied(entity, r));
                    continue;
                }
                var oldId = OldValues.Long(r, "ID");
                AddHere(entity, fresh, r, entity.TakesOldNumbers ? oldId : 0);
                var link = new SyncLink { Entity = entity.Name, LocalId = fresh.Id, OldKey = KeyOf(entity, r), SyncedOn = context.Now };
                context.Add(link);
                Remember(entity, link, fresh, r, columns, mapped);
                if (entity.TakesOldNumbers && oldId != 0 && fresh.Id != oldId)
                    _notes.Add($"The {entity.Label} {oldId} of {tables.Title} is {fresh.Id} here: its number was already used here.");
                _here.Add($"the {entity.Label} {entity.Describe(fresh)} added (from {tables.Title}).");
            }
        }

        /// <summary>Why an old row is not copied here, naming the record it belongs to when the entity says which (<see cref="SyncEntity{T}.OldParent"/>).</summary>
        private string NotCopied<T>(SyncEntity<T> entity, OldRow row) where T : class, IRecord, new()
        {
            var what = $"The {entity.Label} {Describe(entity, row)} of {tables.Title}";
            if (entity.OldParent is not { } parent) return $"{what} cannot be copied here yet: what it refers to is not here.";
            var id = Math.Abs(OldValues.Long(row, parent.OldColumn));
            if (id == 0) return $"{what} is not copied here: it has no {parent.Label} there. It is left there as it is.";
            var key = id.ToString(CultureInfo.InvariantCulture);
            var its = $"{tables.Title}'s {parent.Label} #{key}";
            if (context.ByOld(parent.Entity, key) is { RemovedHere: true })
                return $"{what} is not copied here: it belongs to {its}, which was deleted here. It is left there as it is.";
            if (_oldKeys.TryGetValue(parent.Entity, out var keys) && !keys.Contains(key))
                return $"{what} is not copied here: it belongs to {its}, which {tables.Title} does not have (a row left over there). It is left there as it is.";
            if (keys is not null && context.ByOld(parent.Entity, key) is null)
                return $"{what} is not copied here: it belongs to {its}, which is not copied here either. It is left there as it is.";
            return $"{what} cannot be copied here yet: it belongs to {its}, which is not here yet.";
        }

        private string Describe<T>(SyncEntity<T> entity, OldRow row) where T : class, IRecord, new() =>
            entity.KeyColumns.Length == 1 && entity.KeyColumns[0] == "ID" ? "#" + OldValues.Long(row, "ID").ToString(CultureInfo.InvariantCulture) : KeyOf(entity, row);

        /// <summary>Adds a record here, with the Id wanted when it is free.</summary>
        private void AddHere<T>(SyncEntity<T> entity, T record, OldRow row, long wantedId) where T : class, IRecord, new()
        {
            if (wantedId > 0 && store.Get<T>(wantedId) is null)
            {
                record.Id = wantedId;
                store.InsertKeepingIds([record]);
            }
            else
            {
                record.Id = 0;
                store.Insert(record);
            }
            if (entity.SetOldId is { } set && OldValues.Long(row, "ID") is var id && id != 0)
            {
                set(record, id);
                store.Update(record);
            }
        }

        /// <summary>Adds a record that is here to the old app.</summary>
        private void Insert<T>(SyncEntity<T> entity, T record, Dictionary<string, object?> values, bool keepIdHere) where T : class, IRecord, new()
        {
            var id = writer.Insert(entity.OldTable, values, entity.Describe(record));
            var key = entity.KeyColumns.Length == 1 && entity.KeyColumns[0] == "ID"
                ? id.ToString(CultureInfo.InvariantCulture)
                : string.Join("|", entity.KeyColumns.Select(c => OldValues.Canonical(values.TryGetValue(c, out var v) ? v : null)));
            var link = new SyncLink { Entity = entity.Name, LocalId = record.Id, OldKey = key, LocalHash = Hash(values), OldHash = Hash(values), Pending = true, SyncedOn = context.Now };
            context.Add(link);
            Save(link);
            Pending.Add(link);
            if (entity.SetOldId is { } set && id != 0)
            {
                set(record, id);
                store.Update(record);
            }
            _there.Add($"the {entity.Label} {entity.Describe(record)} added" + (entity.TakesOldNumbers && id != record.Id && id > 0 ? $" (as {id}: its number is used there)." : "."));
        }

        private void Push<T>(SyncEntity<T> entity, T x, OldRow r, SyncLink link, IReadOnlyDictionary<string, OldColumn> columns, List<string> mapped)
            where T : class, IRecord, new()
        {
            var values = Prepare(entity, x, r, columns);
            if (values is null) return;
            var changed = Changed(values, r);
            if (changed.Count > 0)
            {
                writer.Update(entity.OldTable, KeyValues(entity, r), changed, r, entity.Describe(x));
                _there.Add($"the {entity.Label} {entity.Describe(x)} changed ({string.Join(", ", changed.Keys)}).");
                link.Pending = true;
                Pending.Add(link);
            }
            // A change to a key column (a mark that changes the key, e.g. a negative project number) moves the pair to the new key.
            if (changed.Keys.Any(k => entity.KeyColumns.Contains(k, StringComparer.OrdinalIgnoreCase)))
            {
                context.Remove(link);
                link.OldKey = KeyOf(entity, Merged(r, changed));
                context.Add(link);
            }
            link.LocalHash = LocalHash(entity, x, columns);
            link.OldHash = HashOfRow(Merged(r, changed), mapped);
            link.SyncedOn = context.Now;
            Save(link);
        }

        private void Pull<T>(SyncEntity<T> entity, T x, OldRow r, SyncLink link, IReadOnlyDictionary<string, OldColumn> columns, List<string> mapped)
            where T : class, IRecord, new()
        {
            var fresh = entity.FromOld(r, context, x);
            if (fresh is null)
            {
                _notes.Add($"The {entity.Label} {entity.Describe(x)} changed in {tables.Title} cannot be copied here yet: what it refers to is not here.");
                return;
            }
            fresh.Id = x.Id;
            if (Table.Of<T>().Columns.Any(c => !Equals(c.Get(fresh), c.Get(x))))
            {
                store.Update(fresh);
                _here.Add($"the {entity.Label} {entity.Describe(fresh)} changed (from {tables.Title}).");
            }
            Remember(entity, link, fresh, r, columns, mapped);
        }

        /// <summary>Keeps both sides' fingerprints as they are now (in step).</summary>
        private void Remember<T>(SyncEntity<T> entity, SyncLink link, T x, OldRow r, IReadOnlyDictionary<string, OldColumn> columns, List<string> mapped)
            where T : class, IRecord, new()
        {
            link.LocalHash = LocalHash(entity, x, columns);
            link.OldHash = HashOfRow(r, mapped);
            link.SyncedOn = context.Now;
            Save(link);
        }

        private void Save(SyncLink link)
        {
            if (link.Id == 0) store.Insert(link);
            else store.Update(link);
        }

        /// <summary>Ends a pair: kept as Unpaired (nothing is deleted), no longer read by the syncs.</summary>
        private void Unpair(SyncLink link)
        {
            context.Remove(link);
            if (link.Id == 0) return;
            link.Unpaired = true;
            link.Pending = false;
            store.Update(link);
        }

        private static bool Differs(IReadOnlyDictionary<string, object?> values, OldRow row) => Changed(values, row).Count > 0;

        private static Dictionary<string, object?> Changed(IReadOnlyDictionary<string, object?> values, OldRow row)
        {
            var changed = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in values)
                if (OldValues.Canonical(pair.Value) != OldValues.Canonical(row.TryGetValue(pair.Key, out var v) ? v : null)) changed[pair.Key] = pair.Value;
            return changed;
        }

        private static OldRow Merged(OldRow row, IReadOnlyDictionary<string, object?>? values)
        {
            var merged = new OldRow(row, StringComparer.OrdinalIgnoreCase);
            if (values is not null) foreach (var pair in values) merged[pair.Key] = pair.Value;
            return merged;
        }
    }

    /// <summary>
    /// Passes the changes on to the old app's writer (none in a preview) and keeps each with the value it replaced, for the
    /// admin page's list of changes made in the old app.
    /// </summary>
    private sealed class RecordingWriter(IOldTablesWriter? inner, Func<DateTime> clock) : IDisposable
    {
        private long _previewIds;
        public List<SyncChange> Changes { get; } = [];

        public long Insert(string table, IReadOnlyDictionary<string, object?> values, string what)
        {
            var id = inner?.Insert(table, values) ?? --_previewIds;
            foreach (var pair in values.Where(p => OldValues.Canonical(p.Value).Length > 0))
                Changes.Add(new SyncChange { OldTable = table, OldKey = id.ToString(CultureInfo.InvariantCulture), Record = Strings.Shorten(what, 200), Action = "Added",
                    Column = pair.Key, After = OldValues.Canonical(pair.Value) });
            return id;
        }

        public void Update(string table, IReadOnlyDictionary<string, object?> key, IReadOnlyDictionary<string, object?> values, OldRow? before, string what)
        {
            inner?.Update(table, key, values);
            var keyText = string.Join("|", key.Values.Select(OldValues.Canonical));
            foreach (var pair in values)
                Changes.Add(new SyncChange { OldTable = table, OldKey = keyText, Record = Strings.Shorten(what, 200), Action = "Changed", Column = pair.Key,
                    Before = OldValues.Canonical(before is not null && before.TryGetValue(pair.Key, out var v) ? v : null), After = OldValues.Canonical(pair.Value) });
        }

        public void Commit()
        {
            inner?.Commit();
            var now = clock();
            foreach (var change in Changes) change.ChangedOn = now;
        }

        public void Dispose() => inner?.Dispose();
    }
}

/// <summary>
/// The records as the app's pages change them: adding a record whose type takes the old app's numbers goes through
/// <see cref="OldAppSync.Add{T}"/> while the sync is on. Everything else goes straight to the store.
/// </summary>
public sealed class SyncedStore(IRecordStore inner, OldAppSync sync) : IRecordStore
{
    public IRecordStore Inner => inner;
    public T? Get<T>(long id) where T : class, IRecord, new() => inner.Get<T>(id);
    public List<T> Find<T>(Query? query = null) where T : class, IRecord, new() => inner.Find<T>(query);
    public int Count<T>(Query? query = null) where T : class, IRecord, new() => inner.Count<T>(query);
    public T Insert<T>(T record) where T : class, IRecord, new() => sync.Add(inner, record);
    public void Update<T>(T record) where T : class, IRecord, new() => inner.Update(record);
    public void Delete<T>(long id) where T : class, IRecord, new() => inner.Delete<T>(id);
    public int Delete<T>(Query query) where T : class, IRecord, new() => inner.Delete<T>(query);
    public void InsertKeepingIds<T>(IReadOnlyList<T> records) where T : class, IRecord, new() => inner.InsertKeepingIds(records);
    public T Batch<T>(Func<IRecordStore, T> work) => inner.Batch(s => work(new SyncedStore(s, sync)));
}
