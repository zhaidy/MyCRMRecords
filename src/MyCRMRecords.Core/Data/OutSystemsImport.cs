using System.Data.Common;
using System.Globalization;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Data;

/// <summary>What the import found (preview) or copied.</summary>
public sealed record ImportCounts(Dictionary<string, int> Found, Dictionary<string, int> Copied, IReadOnlyList<string> Notes);

/// <summary>
/// Copies MyCRM's Users, Territory and Company part into MyCRMRecords: the teams and CRM user records, the group names,
/// the companies with their caretake remarks, relationships, addresses, contacts, phones and emails, the permanent
/// territories with their companies, the territory movements, and every OutSystems user (so names show). Each record is
/// made from MyCRM's row the way the sync makes it (<see cref="CrmSync"/>); records keep their Id and remember MyCRM's
/// (OutSystemsId), so the sync pairs them without copying. Nothing is changed in OutSystems. Records imported before are
/// skipped, so the import can run again just before switching over.
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
        CrmSync.GroupNames => "Company group names",
        CrmSync.Teams => "Teams",
        CrmSync.Users => "CRM user records",
        CrmSync.Companies => "Companies",
        CrmSync.Caretakes => "Caretake remarks",
        CrmSync.Relationships => "Company relationships",
        CrmSync.Addresses => "Addresses",
        CrmSync.AddressLinks => "Company addresses",
        CrmSync.Contacts => "Contacts",
        CrmSync.ContactLinks => "Company contacts",
        CrmSync.Phones => "Phone numbers",
        CrmSync.Emails => "Email addresses",
        CrmSync.Territories => "Territories (active, permanent)",
        CrmSync.CompanyTerritories => "Companies of territories",
        CrmSync.Movements => "Territory movements",
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
        copied["People"] = databases.Use(ExternalNames.OutSystems, ImportPeople);
        progress("Done.");
        return new ImportCounts(found, copied, notes);
    }

    /// <summary>
    /// Every OutSystems user with a username, as people MyCRMRecords knows (their name and email), so the salespeople,
    /// CRM users and the people who changed records show by name. People already known are left as they are.
    /// </summary>
    private int ImportPeople(DbConnection connection)
    {
        var known = new HashSet<string>(store.Find<AppUser>().Select(u => u.Login), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var row in Sql.Rows(connection, null, "SELECT [USERNAME], [NAME], [EMAIL] FROM dbo.OSSYS_USER"))
        {
            var login = LoginOf(OutSystemsValues.Text(row["USERNAME"]), Domain);
            if (login.Length == 0 || !known.Add(login)) continue;
            var name = OutSystemsValues.Text(row["NAME"]);
            store.Insert(new AppUser { Login = Strings.Shorten(login, 256), DisplayName = Strings.Shorten(name.Length > 0 ? name : login, 256), Email = Strings.Shorten(OutSystemsValues.Text(row["EMAIL"]), 256) });
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
            const int pageSize = 500;
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

    /// <summary>"jsmith", "RSP\jsmith", "jsmith@ricoh.sg" or "RSP\jsmith@ricoh.sg" (MyCRM's users) → "RSP\jsmith".</summary>
    public static string LoginOf(string username, string domain)
    {
        var name = username.Trim();
        if (name.Length == 0) return "";
        var slash = name.IndexOf('\\');
        if (slash >= 0)
        {
            // DOMAIN\name@mail (OutSystems keeps some users so): DOMAIN\name, whatever the domain.
            var account = name.Substring(slash + 1);
            var sign = account.IndexOf('@');
            var prefix = string.Equals(name.Substring(0, slash), domain, StringComparison.OrdinalIgnoreCase) ? domain : name.Substring(0, slash);
            return prefix + "\\" + (sign > 0 ? account.Substring(0, sign) : account);
        }
        var at = name.IndexOf('@');
        if (at > 0) name = name.Substring(0, at);
        return domain + "\\" + name;
    }
}
