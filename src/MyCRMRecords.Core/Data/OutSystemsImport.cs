using System.Data.Common;
using System.Globalization;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Data;

/// <summary>What the import found (preview) or copied.</summary>
public sealed record ImportCounts(Dictionary<string, int> Found, Dictionary<string, int> Copied, IReadOnlyList<string> Notes);

/// <summary>
/// Copies MyCRM's data into MyCRMRecords: its divisions, feedback categories and mediums, the feedbacks with their tasks and
/// attachments (the files themselves), the people with MyCRM's roles, and the people MyCRM offered in its people
/// choices. Each record is made from MyCRM's row the way the sync makes it (<see cref="CrmSync"/>); records keep their
/// Id and remember MyCRM's (OutSystemsId), so the sync pairs them without copying. Nothing is changed in OutSystems.
/// Records imported before are skipped, so the import can run again just before switching over.
/// </summary>
public sealed class OutSystemsImport(IRecordStore store, ExternalDatabases databases, Func<CrmSettings> settings, Func<DateTime> clock, OldAppSync sync)
{
    public const string Job = "outsystems-import";

    private const string Domain = "RSP";

    /// <summary>The records, not through the sync (nothing imported is written back to MyCRM).</summary>
    private IRecordStore Records => store is SyncedStore synced ? synced.Inner : store;

    /// <summary>How the import names each kind of record.</summary>
    private static string Title(string entity) => entity switch
    {
        CrmSync.Categories => "Feedback categories",
        CrmSync.Mediums => "Feedback mediums",
        CrmSync.Divisions => "Divisions",
        CrmSync.Feedbacks => "Feedbacks",
        CrmSync.Tasks => "Tasks",
        CrmSync.Attachments => "Attachments",
        _ => entity,
    };

    public ImportCounts Preview() => Run(copy: false, "", _ => { });

    public ImportCounts Import(string by, Action<string> progress) => Run(copy: true, by, progress);

    private ImportCounts Run(bool copy, string by, Action<string> progress)
    {
        var found = new Dictionary<string, int>();
        var copied = new Dictionary<string, int>();
        var notes = new List<string>();
        var tables = sync.Tables ?? throw new InvalidOperationException("The OutSystems database is not set up (Connections tab).");
        // MyCRM's Ids of the records copied so far, to place each record's parents (as the sync's pairs would).
        var context = new SyncContext(tables, [], clock());
        foreach (var entity in sync.Entities)
        {
            progress(Title(entity.Name) + "…");
            entity.Accept(new Copier(this, tables, context, copy, found, copied, notes));
        }
        if (!copy) return new ImportCounts(found, copied, notes);
        progress("People…");
        copied["People with a role"] = databases.Use(ExternalNames.OutSystems, connection => ImportRoles(connection, settings().Connections.CrmModule));
        copied["People to choose from"] = databases.Use(ExternalNames.OutSystems, ImportPeople);
        progress("Done.");
        return new ImportCounts(found, copied, notes);
    }

    /// <summary>
    /// The people MyCRM offered in its people choices (GetUsers): active OutSystems users with a ricoh.sg or
    /// rapp.ricoh.com email and a RSP\…@ricoh.sg or RSP\…@rapp.ricoh.com username, and three named ones.
    /// </summary>
    public static bool IsOffered(string username, string email, bool active)
    {
        if (!active) return false;
        var mail = (email ?? "").Trim();
        if (!mail.EndsWith("@ricoh.sg", StringComparison.OrdinalIgnoreCase) && !mail.EndsWith("@rapp.ricoh.com", StringComparison.OrdinalIgnoreCase)) return false;
        var name = (username ?? "").Trim();
        return (name.StartsWith(Domain + "\\", StringComparison.OrdinalIgnoreCase)
                && (name.EndsWith("@ricoh.sg", StringComparison.OrdinalIgnoreCase) || name.EndsWith("@rapp.ricoh.com", StringComparison.OrdinalIgnoreCase)))
               || Named.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string[] Named = [Domain + "\\zhaidy", Domain + "\\adrianlim", Domain + "\\ralphp"];

    private int ImportPeople(DbConnection connection)
    {
        var added = 0;
        foreach (var row in Sql.Rows(connection, null, "SELECT [USERNAME], [NAME], [EMAIL], [IS_ACTIVE] FROM dbo.OSSYS_USER"))
        {
            var username = OutSystemsValues.Text(row["USERNAME"]);
            var email = OutSystemsValues.Text(row["EMAIL"]);
            if (!IsOffered(username, email, row["IS_ACTIVE"] is bool b ? b : Convert.ToBoolean(row["IS_ACTIVE"], CultureInfo.InvariantCulture))) continue;
            var login = LoginOf(username, Domain);
            if (login.Length == 0 || store.FindFirst<AppUser>(Query.Where("Login", login)) is not null) continue;
            var name = OutSystemsValues.Text(row["NAME"]);
            store.Insert(new AppUser { Login = Strings.Shorten(login, 256), DisplayName = Strings.Shorten(name.Length > 0 ? name : login, 256), Email = Strings.Shorten(email, 256) });
            added++;
        }
        return added;
    }

    /// <summary>Copies the rows of one entity, made as the sync makes them, keeping MyCRM's Ids where they are free.</summary>
    private sealed class Copier(OutSystemsImport import, IOldTables tables, SyncContext context, bool copy, Dictionary<string, int> found,
        Dictionary<string, int> copied, List<string> notes) : ISyncEntityVisitor
    {
        public void Visit<T>(SyncEntity<T> entity) where T : class, IRecord, new()
        {
            var store = import.Records;
            var title = Title(entity.Name);
            if (!copy)
            {
                // Counted, not read: reading every attachment's fingerprint made the files be read.
                found[title] = tables is CrmTables counted ? counted.Count(entity.OldTable) : tables.Rows(entity.OldTable).Count;
                return;
            }
            var rows = tables.Rows(entity.OldTable);
            found[title] = rows.Count;
            var existing = store.Find<T>();
            // Imported (or synced) before: the record with MyCRM's Id, or for a table without numbers the same key.
            var byOldId = new Dictionary<long, T>();
            if (entity.OldIdOf is { } oldIdOf)
                foreach (var record in existing)
                    if (oldIdOf(record) is { } id && id != 0 && !byOldId.ContainsKey(id)) byOldId[id] = record;
            foreach (var pair in byOldId)
                context.Add(new SyncLink { Entity = entity.Name, LocalId = pair.Value.Id, OldKey = pair.Key.ToString(CultureInfo.InvariantCulture) });
            var keys = entity.OldIdOf is null ? new HashSet<string>(existing.Select(r => Key(entity, entity.ToOld(r, context, null))), StringComparer.OrdinalIgnoreCase) : null;
            var page = new List<T>();
            var pageSize = typeof(T) == typeof(FeedbackFile) ? 20 : 500;
            var count = 0;
            var skipped = 0;
            var notPlaced = 0;
            void Flush()
            {
                if (page.Count == 0) return;
                store.InsertKeepingIds(page.ToList());
                page.Clear();
            }
            var taken = new HashSet<long>(existing.Select(r => r.Id));
            foreach (var row in rows)
            {
                var oldId = OldValues.Long(row, "ID");
                if (keys is null ? byOldId.ContainsKey(oldId) : !keys.Add(Key(entity, row)))
                {
                    skipped++;
                    continue;
                }
                if (entity.FromOld(row, context, null) is not { } record)
                {
                    notPlaced++;
                    continue;
                }
                if (oldId > 0 && taken.Add(oldId))
                {
                    record.Id = oldId;
                    page.Add(record);
                    if (page.Count >= pageSize) Flush();
                }
                else
                {
                    record.Id = 0;
                    store.Insert(record);
                    taken.Add(record.Id);
                }
                if (oldId > 0) context.Add(new SyncLink { Entity = entity.Name, LocalId = record.Id, OldKey = oldId.ToString(CultureInfo.InvariantCulture) });
                count++;
            }
            Flush();
            copied[title] = count;
            if (skipped > 0) notes.Add($"{title}: {skipped.ToString(CultureInfo.InvariantCulture)} imported or synced before, skipped.");
            if (notPlaced > 0) notes.Add($"{title}: {notPlaced.ToString(CultureInfo.InvariantCulture)} not copied (what they belong to is not in MyCRM, or was not copied).");
        }

        private static string Key<T>(SyncEntity<T> entity, IReadOnlyDictionary<string, object?> values) where T : class, IRecord, new() =>
            string.Join("|", entity.KeyColumns.Select(c => OldValues.Canonical(values.TryGetValue(c, out var v) ? v : null)));
    }

    /// <summary>MyCRM's role names; its Admin is a Feedback Admin here (an administrator of MyCRMRecords is another role).</summary>
    public static string? RoleOf(string oldRole) => oldRole.Trim().ToUpperInvariant() switch
    {
        "ADMIN" => Roles.FeedbackAdmin,
        "BE" => Roles.Be,
        "HR" => Roles.Hr,
        _ => null,
    };

    /// <summary>Everyone with one of MyCRM's roles (given to them or to a group of theirs), as MyCRMRecords users with those roles.</summary>
    private int ImportRoles(DbConnection connection, string module)
    {
        var espace = Sql.Rows(connection, null, "SELECT TOP 1 [ID] FROM dbo.OSSYS_ESPACE WHERE [NAME] = @m AND [IS_ACTIVE] = 1 ORDER BY [ID] DESC", ("@m", module));
        if (espace.Count == 0) return 0;
        var id = espace[0]["ID"];
        var grants = new List<Dictionary<string, object?>>();
        if (Sql.TableExists(connection, null, "dbo.OSSYS_USER_ROLE"))
            grants.AddRange(Sql.Rows(connection, null,
                "SELECT u.[USERNAME], u.[NAME], u.[EMAIL], r.[NAME] AS ROLE FROM dbo.OSSYS_USER_ROLE ur JOIN dbo.OSSYS_ROLE r ON r.[ID] = ur.[ROLE_ID] " +
                "JOIN dbo.OSSYS_USER u ON u.[ID] = ur.[USER_ID] WHERE r.[ESPACE_ID] = @e AND u.[IS_ACTIVE] = 1", ("@e", id)));
        if (Sql.TableExists(connection, null, "dbo.OSSYS_GROUP_ROLE") && Sql.TableExists(connection, null, "dbo.OSSYS_USER_GROUP"))
            grants.AddRange(Sql.Rows(connection, null,
                "SELECT u.[USERNAME], u.[NAME], u.[EMAIL], r.[NAME] AS ROLE FROM dbo.OSSYS_USER_GROUP ug JOIN dbo.OSSYS_GROUP_ROLE gr ON gr.[GROUP_ID] = ug.[GROUP_ID] " +
                "JOIN dbo.OSSYS_ROLE r ON r.[ID] = gr.[ROLE_ID] JOIN dbo.OSSYS_USER u ON u.[ID] = ug.[USER_ID] WHERE r.[ESPACE_ID] = @e AND u.[IS_ACTIVE] = 1", ("@e", id)));
        var people = 0;
        foreach (var person in grants.GroupBy(g => LoginOf(OutSystemsValues.Text(g["USERNAME"]), Domain), StringComparer.OrdinalIgnoreCase))
        {
            if (person.Key.Length == 0) continue;
            var roles = person.Select(g => RoleOf(OutSystemsValues.Text(g["ROLE"]))).Where(r => r is not null).Select(r => r!).Distinct().ToList();
            if (roles.Count == 0) continue;
            var first = person.First();
            if (store.FindFirst<AppUser>(Query.Where("Login", person.Key)) is null)
                store.Insert(new AppUser
                {
                    Login = person.Key, DisplayName = Strings.Shorten(OutSystemsValues.Text(first["NAME"]), 256), Email = Strings.Shorten(OutSystemsValues.Text(first["EMAIL"]), 256),
                });
            var have = store.Find<UserRole>(Query.Where("Login", person.Key).And("IsActive", true)).Select(r => r.Role).ToList();
            foreach (var role in roles.Where(r => !have.Contains(r, StringComparer.OrdinalIgnoreCase)))
                store.Insert(new UserRole { Login = person.Key, Role = role });
            people++;
        }
        return people;
    }

    /// <summary>"jsmith", "RSP\jsmith", "jsmith@ricoh.sg" or "RSP\jsmith@ricoh.sg" (MyCRM's users) → "RSP\jsmith".</summary>
    public static string LoginOf(string username, string domain)
    {
        var name = username.Trim();
        if (name.Length == 0) return "";
        var slash = name.IndexOf('\\');
        if (slash >= 0)
        {
            if (!string.Equals(name.Substring(0, slash), domain, StringComparison.OrdinalIgnoreCase)) return name;
            var account = name.Substring(slash + 1);
            var sign = account.IndexOf('@');
            return domain + "\\" + (sign > 0 ? account.Substring(0, sign) : account);
        }
        var at = name.IndexOf('@');
        if (at > 0) name = name.Substring(0, at);
        return domain + "\\" + name;
    }
}
