using System.Data.Common;
using System.Globalization;

namespace MyCRMRecords.Data;

/// <summary>Why MyMachines' temporary access was not changed: the switch is off, or the person or row is not there.</summary>
public sealed class MachineAccessRefusedException(string message) : InvalidOperationException(message);

/// <summary>A row of MyMachines' TempAccess: an OutSystems user who may see a customer's machines (by its name or BRN).</summary>
public sealed record TempAccessRow
{
    public long Id { get; init; }
    public long? UserId { get; init; }
    /// <summary>The OutSystems user as a login (RSP\name), their name and email.</summary>
    public string Login { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Email { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string Brn { get; init; } = "";
    public DateTime? GivenAt { get; init; }
}

/// <summary>
/// MyMachines' temporary access (its entity TempAccess in the OutSystems database). Read for who sees which machines;
/// changed by the Machine access page only when <c>Let MyCRMRecords change MyMachines' temporary access</c> is on: a row
/// is added, never deleted. TempAccess has no IsActive, so a row taken away is <b>marked</b>: "X:" goes before its
/// CustomerName and BRN, which then match no customer (MyMachines compares them with the customer's name and BRN), and
/// the row is no longer listed. Every value written is recorded with the value it replaced in dbo.CrChange.
/// </summary>
public interface IMyMachinesAccess
{
    /// <summary>Whether the OutSystems database is set up.</summary>
    bool Available { get; }
    /// <summary>Whether MyCRMRecords may change TempAccess now (the administrator's switch).</summary>
    bool CanWrite { get; }
    /// <summary>Every row not taken away, newest first.</summary>
    IReadOnlyList<TempAccessRow> List();
    /// <summary>Adds a row for the OutSystems user of this login or email; returns its Id.</summary>
    long Give(string login, string email, string customerName, string brn, string by);
    /// <summary>Marks a row taken away ("X:" before its CustomerName and BRN).</summary>
    void Remove(long id, string by);
}

public static class TempAccessRules
{
    /// <summary>What goes before a removed row's CustomerName and BRN.</summary>
    public const string Removed = "X:";

    public const string WritesOff =
        "MyCRMRecords only reads MyMachines' temporary access for now: an administrator turns on \"Let MyCRMRecords change MyMachines' temporary access\" (admin page, MyCRM tab).";

    /// <summary>The MyCRMRecords table name the changes are recorded under (dbo.CrChange).</summary>
    public const string ChangeTable = "MyMachines.TempAccess";

    public static bool IsRemoved(string customerName, string brn) =>
        customerName.StartsWith(Removed, StringComparison.Ordinal) || brn.StartsWith(Removed, StringComparison.Ordinal);

    /// <summary>A removed value: "X:" before it, shortened to fit the column (its length in characters; 0 or less: no limit).</summary>
    public static string Mark(string value, int length)
    {
        var marked = Removed + value;
        return length > 0 && marked.Length > length ? marked.Substring(0, length) : marked;
    }
}

/// <summary><see cref="IMyMachinesAccess"/> over MyMachines' TempAccess table (the <c>MyMachines module</c> setting).</summary>
public sealed class SqlMyMachinesAccess(ExternalDatabases databases, Func<ConnectionSettings> connections, Func<bool> canWrite, IRecordStore own,
    Func<DateTime> clock, string domain) : IMyMachinesAccess
{
    public bool Available => databases.IsConfigured(ExternalNames.OutSystems);

    public bool CanWrite => canWrite();

    private string TableOf(DbConnection connection) =>
        databases.EntityTable(connection, Entities.ModuleName(connections(), OsModule.MyMachines), Entities.TempAccess.Name, Entities.ReadOnly(OsModule.MyMachines));

    public IReadOnlyList<TempAccessRow> List() => databases.Use(ExternalNames.OutSystems, connection =>
        (IReadOnlyList<TempAccessRow>)Sql.Rows(connection, null,
                $"SELECT t.[ID], t.[USERID], t.[CUSTOMERNAME], t.[BRN], t.[DATETIME], u.[USERNAME], u.[NAME], u.[EMAIL] FROM {TableOf(connection)} t " +
                "LEFT JOIN dbo.OSSYS_USER u ON u.[ID] = t.[USERID] ORDER BY t.[DATETIME] DESC, t.[ID] DESC")
            .Select(r => new TempAccessRow
            {
                Id = r.Long("ID"),
                UserId = r.Id("USERID"),
                Login = OutSystemsImport.LoginOf(r.Text("USERNAME"), domain),
                UserName = r.Text("NAME"),
                Email = r.Text("EMAIL"),
                CustomerName = r.Text("CUSTOMERNAME"),
                Brn = r.Text("BRN"),
                GivenAt = r.Date("DATETIME"),
            })
            .Where(r => !TempAccessRules.IsRemoved(r.CustomerName, r.Brn))
            .ToList());

    public long Give(string login, string email, string customerName, string brn, string by)
    {
        if (!canWrite()) throw new MachineAccessRefusedException(TempAccessRules.WritesOff);
        var now = clock();
        var changes = new List<MyCrmChange>();
        var id = databases.Use(ExternalNames.OutSystems, connection =>
        {
            // The person's OutSystems user: by email, else by username (as MyCRM's users are matched).
            var users = Sql.Rows(connection, null, "SELECT [ID], [USERNAME], [EMAIL] FROM dbo.OSSYS_USER WHERE [IS_ACTIVE] = 1 ORDER BY [ID]");
            var user = users.FirstOrDefault(u => email.Trim().Length > 0 && string.Equals(u.Text("EMAIL"), email.Trim(), StringComparison.OrdinalIgnoreCase))
                       ?? users.FirstOrDefault(u => string.Equals(OutSystemsImport.LoginOf(u.Text("USERNAME"), domain), login.Trim(), StringComparison.OrdinalIgnoreCase));
            if (user is null) throw new MachineAccessRefusedException($"{login} is not a user of OutSystems: MyMachines' temporary access needs one.");
            var userId = user.Long("ID");
            var table = TableOf(connection);
            using var transaction = connection.BeginTransaction();
            using var command = Sql.Command(connection, transaction,
                $"INSERT INTO {table} ([USERID], [CUSTOMERNAME], [BRN], [DATETIME]) OUTPUT INSERTED.[ID] VALUES (@user, @name, @brn, @at)");
            Sql.Add(command, "@user", userId);
            Sql.Add(command, "@name", customerName.Trim());
            Sql.Add(command, "@brn", brn.Trim());
            Sql.Add(command, "@at", now);
            var added = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            transaction.Commit();
            changes.Add(Change(added, "USERID", "", userId.ToString(CultureInfo.InvariantCulture)));
            changes.Add(Change(added, "CUSTOMERNAME", "", customerName.Trim()));
            changes.Add(Change(added, "BRN", "", brn.Trim()));
            changes.Add(Change(added, "DATETIME", "", now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            return added;
        });
        Record(changes, now, by);
        return id;
    }

    public void Remove(long id, string by)
    {
        if (!canWrite()) throw new MachineAccessRefusedException(TempAccessRules.WritesOff);
        var now = clock();
        var changes = new List<MyCrmChange>();
        databases.Use(ExternalNames.OutSystems, connection =>
        {
            var table = TableOf(connection);
            using var transaction = connection.BeginTransaction();
            var rows = Sql.Rows(connection, transaction, $"SELECT [CUSTOMERNAME], [BRN] FROM {table} WITH (UPDLOCK) WHERE [ID] = @id", ("@id", id));
            if (rows.Count == 0 || TempAccessRules.IsRemoved(rows[0].Text("CUSTOMERNAME"), rows[0].Text("BRN")))
                throw new MachineAccessRefusedException("That temporary access is not there any more.");
            // The marked values must fit the columns (their length in characters: nvarchar's bytes halved; -1 for max).
            int Length(string column)
            {
                var found = Sql.Rows(connection, transaction, "SELECT COL_LENGTH(@table, @column) AS [N]", ("@table", table), ("@column", column));
                return found.Count == 1 && found[0]["N"] is { } n ? Convert.ToInt32(n, CultureInfo.InvariantCulture) / 2 : 0;
            }
            var name = Convert.ToString(rows[0]["CUSTOMERNAME"], CultureInfo.InvariantCulture) ?? "";
            var brn = Convert.ToString(rows[0]["BRN"], CultureInfo.InvariantCulture) ?? "";
            var markedName = TempAccessRules.Mark(name, Length("CUSTOMERNAME"));
            var markedBrn = TempAccessRules.Mark(brn, Length("BRN"));
            Sql.Execute(connection, transaction, $"UPDATE {table} SET [CUSTOMERNAME] = @name, [BRN] = @brn WHERE [ID] = @id",
                ("@name", markedName), ("@brn", markedBrn), ("@id", id));
            transaction.Commit();
            changes.Add(Change(id, "CUSTOMERNAME", name, markedName));
            changes.Add(Change(id, "BRN", brn, markedBrn));
            return 0;
        });
        Record(changes, now, by);
    }

    private static MyCrmChange Change(long id, string column, string before, string after) =>
        new() { TableName = TempAccessRules.ChangeTable, RowId = id, Column = column, Before = before, After = after };

    /// <summary>Every value written, with the value it replaced and who wrote it (dbo.CrChange), once the change is committed.</summary>
    private void Record(List<MyCrmChange> changes, DateTime now, string by)
    {
        if (changes.Count == 0) return;
        own.Batch(store =>
        {
            foreach (var change in changes)
            {
                change.ChangedOn = now;
                change.ChangedBy = Strings.Shorten(by, 600);
                store.Insert(change);
            }
        });
    }
}
