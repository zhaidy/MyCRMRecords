using System.Data.Common;
using System.Globalization;

namespace MyCRMRecords.Data;

/// <summary>A column of an old app's table: its SQL type, length in characters (-1: no limit) and whether it takes NULL.</summary>
public sealed record OldColumn(string Name, string SqlType, int Length, bool Nullable, bool Identity);

/// <summary>A user of the old app (OutSystems' OSSYS_USER): its Id, login (RSP\name), name and email.</summary>
public sealed record OldUser(long Id, string Login, string Name, string Email);

/// <summary>
/// The old app's tables, as the sync with it reads and writes them. Rows are column → value, the column names in upper
/// case as OutSystems makes them (looked up without regard to case).
/// </summary>
public interface IOldTables
{
    /// <summary>The old app's name, for messages ("MyPartRequests").</summary>
    string Title { get; }
    /// <summary>The table's rows; with <paramref name="sinceColumn"/>, only those whose date in it is at least <paramref name="since"/>.</summary>
    IReadOnlyList<Dictionary<string, object?>> Rows(string table, string? sinceColumn = null, DateTime? since = null);
    /// <summary>The rows whose <paramref name="column"/> is one of <paramref name="values"/> (the lines of some records).</summary>
    IReadOnlyList<Dictionary<string, object?>> Rows(string table, string column, IReadOnlyCollection<long> values);
    /// <summary>The row whose key columns have these values, or null.</summary>
    Dictionary<string, object?>? Find(string table, IReadOnlyDictionary<string, object?> key);
    IReadOnlyDictionary<string, OldColumn> Columns(string table);
    /// <summary>A static entity's records: Id → label.</summary>
    IReadOnlyDictionary<long, string> Labels(string table);
    IReadOnlyList<OldUser> Users();
    IOldTablesWriter BeginWrite();
}

/// <summary>
/// Changes to the old app's tables, kept together: all of them when <see cref="Commit"/> is called, none otherwise.
/// There is deliberately no way to delete: the sync never deletes anything in the old app (a record deleted in the new
/// app is marked in the old one instead, or left as it is).
/// </summary>
public interface IOldTablesWriter : IDisposable
{
    /// <summary>Adds a row; returns its Id (0 for a table without one).</summary>
    long Insert(string table, IReadOnlyDictionary<string, object?> values);
    /// <summary>Changes the given columns of the row whose key columns have the given values.</summary>
    void Update(string table, IReadOnlyDictionary<string, object?> key, IReadOnlyDictionary<string, object?> values);
    void Commit();
}

/// <summary>
/// The tables of an OutSystems module in the OutSystems database (as set on the Connections tab): an entity's table is
/// looked up by module and entity name (OSUSR_ab1_PARTREQUEST). Writing needs a login that may change those tables.
/// </summary>
public sealed class OutSystemsTables(ExternalDatabases databases, Func<string> module, string title, string domain) : IOldTables
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, OldColumn>> _columns = new(StringComparer.OrdinalIgnoreCase);

    public string Title => title;

    private string TableOf(DbConnection connection, string entity) => databases.EntityTable(connection, module(), entity);

    public IReadOnlyList<Dictionary<string, object?>> Rows(string table, string? sinceColumn = null, DateTime? since = null) =>
        databases.Use(ExternalNames.OutSystems, connection => (IReadOnlyList<Dictionary<string, object?>>)(sinceColumn is null || since is null
            ? Sql.Rows(connection, null, $"SELECT * FROM {TableOf(connection, table)} ORDER BY [ID]")
            : Sql.Rows(connection, null, $"SELECT * FROM {TableOf(connection, table)} WHERE {Sql.Name(sinceColumn)} >= @since ORDER BY [ID]", ("@since", since.Value))));

    public IReadOnlyList<Dictionary<string, object?>> Rows(string table, string column, IReadOnlyCollection<long> values) =>
        databases.Use(ExternalNames.OutSystems, connection =>
        {
            var rows = new List<Dictionary<string, object?>>();
            // The values are numbers, written into the statement (a list of parameters has a limit), 1000 at a time.
            foreach (var chunk in values.Distinct().Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => x.v))
                rows.AddRange(Sql.Rows(connection, null,
                    $"SELECT * FROM {TableOf(connection, table)} WHERE {Sql.Name(column)} IN ({string.Join(", ", chunk.Select(v => v.ToString(CultureInfo.InvariantCulture)))}) ORDER BY [ID]"));
            return (IReadOnlyList<Dictionary<string, object?>>)rows;
        });

    public Dictionary<string, object?>? Find(string table, IReadOnlyDictionary<string, object?> key) => databases.Use(ExternalNames.OutSystems, connection =>
    {
        var columns = key.Keys.ToList();
        var where = string.Join(" AND ", columns.Select((c, i) => $"{Sql.Name(c)} = @k{i.ToString(CultureInfo.InvariantCulture)}"));
        return Sql.Rows(connection, null, $"SELECT TOP 2 * FROM {TableOf(connection, table)} WHERE {where}",
            columns.Select((c, i) => ("@k" + i.ToString(CultureInfo.InvariantCulture), key[c])).ToArray()) is { Count: 1 } rows ? rows[0] : null;
    });

    public IReadOnlyDictionary<string, OldColumn> Columns(string table)
    {
        lock (_columns)
            if (_columns.TryGetValue(table, out var known)) return known;
        var columns = databases.Use(ExternalNames.OutSystems, connection => ColumnsOf(connection, null, TableOf(connection, table)));
        lock (_columns) _columns[table] = columns;
        return columns;
    }

    public IReadOnlyDictionary<long, string> Labels(string table) => databases.Use(ExternalNames.OutSystems, connection =>
        (IReadOnlyDictionary<long, string>)Sql.Rows(connection, null, $"SELECT [ID], [LABEL] FROM {TableOf(connection, table)}")
            .ToDictionary(r => Convert.ToInt64(r["ID"], CultureInfo.InvariantCulture), r => OutSystemsValues.Text(r["LABEL"])));

    public IReadOnlyList<OldUser> Users() => databases.Use(ExternalNames.OutSystems, connection =>
        (IReadOnlyList<OldUser>)Sql.Rows(connection, null, "SELECT [ID], [USERNAME], [NAME], [EMAIL] FROM dbo.OSSYS_USER")
            .Select(r => new OldUser(Convert.ToInt64(r["ID"], CultureInfo.InvariantCulture), OutSystemsImport.LoginOf(OutSystemsValues.Text(r["USERNAME"]), domain),
                OutSystemsValues.Text(r["NAME"]), OutSystemsValues.Text(r["EMAIL"]))).ToList());

    public IOldTablesWriter BeginWrite()
    {
        var connection = databases.Open(ExternalNames.OutSystems);
        return new SqlOldTablesWriter(connection, (c, t, table) => TableOf(c, table));
    }

    /// <summary>The columns of a table (dbo.[OSUSR_ab1_PARTREQUEST]), by name.</summary>
    public static IReadOnlyDictionary<string, OldColumn> ColumnsOf(DbConnection connection, DbTransaction? transaction, string table) =>
        Sql.Rows(connection, transaction,
                "SELECT c.[name] AS NAME, t.[name] AS TYPE, c.[max_length] AS LEN, c.[is_nullable] AS NULLABLE, c.[is_identity] AS IDENTITY_COLUMN " +
                "FROM sys.columns c JOIN sys.types t ON t.[user_type_id] = c.[user_type_id] WHERE c.[object_id] = OBJECT_ID(@table)", ("@table", table))
            .Select(r =>
            {
                var type = OutSystemsValues.Text(r["TYPE"]).ToLowerInvariant();
                var bytes = Convert.ToInt32(r["LEN"], CultureInfo.InvariantCulture);
                var length = type is "nvarchar" or "nchar" ? (bytes < 0 ? -1 : bytes / 2) : type is "varchar" or "char" ? bytes : -1;
                return new OldColumn(OutSystemsValues.Text(r["NAME"]), type, length, Convert.ToBoolean(r["NULLABLE"], CultureInfo.InvariantCulture),
                    Convert.ToBoolean(r["IDENTITY_COLUMN"], CultureInfo.InvariantCulture));
            })
            .ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Writes an old app's tables in one transaction on one connection; disposing without <see cref="Commit"/> undoes everything.</summary>
public sealed class SqlOldTablesWriter : IOldTablesWriter
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly Func<DbConnection, DbTransaction, string, string> _tableOf;
    private bool _done;

    public SqlOldTablesWriter(DbConnection connection, Func<DbConnection, DbTransaction, string, string> tableOf)
    {
        _connection = connection;
        _tableOf = tableOf;
        _transaction = connection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);
    }

    public long Insert(string table, IReadOnlyDictionary<string, object?> values)
    {
        var name = _tableOf(_connection, _transaction, table);
        var columns = values.Keys.ToList();
        using var command = Sql.Command(_connection, _transaction,
            $"INSERT INTO {name} ({string.Join(", ", columns.Select(c => "[" + c + "]"))}) VALUES ({string.Join(", ", columns.Select((_, i) => "@p" + i.ToString(CultureInfo.InvariantCulture)))}); " +
            "SELECT CAST(ISNULL(SCOPE_IDENTITY(), 0) AS bigint);");
        for (var i = 0; i < columns.Count; i++) Sql.Add(command, "@p" + i.ToString(CultureInfo.InvariantCulture), values[columns[i]]);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void Update(string table, IReadOnlyDictionary<string, object?> key, IReadOnlyDictionary<string, object?> values)
    {
        if (values.Count == 0) return;
        var name = _tableOf(_connection, _transaction, table);
        var set = values.Keys.ToList();
        var where = key.Keys.ToList();
        using var command = Sql.Command(_connection, _transaction,
            $"UPDATE {name} SET {string.Join(", ", set.Select((c, i) => $"[{c}] = @v{i.ToString(CultureInfo.InvariantCulture)}"))} " +
            $"WHERE {string.Join(" AND ", where.Select((c, i) => $"[{c}] = @k{i.ToString(CultureInfo.InvariantCulture)}"))}");
        for (var i = 0; i < set.Count; i++) Sql.Add(command, "@v" + i.ToString(CultureInfo.InvariantCulture), values[set[i]]);
        for (var i = 0; i < where.Count; i++) Sql.Add(command, "@k" + i.ToString(CultureInfo.InvariantCulture), key[where[i]]);
        var changed = command.ExecuteNonQuery();
        if (changed > 1) throw new InvalidOperationException($"{changed} rows of {table} have the key {string.Join(", ", key.Values)}: nothing was changed.");
    }

    public void Commit()
    {
        _transaction.Commit();
        _done = true;
    }

    public void Dispose()
    {
        if (!_done)
        {
            try { _transaction.Rollback(); }
            catch (Exception ex) when (ex is DbException or InvalidOperationException) { }
        }
        _transaction.Dispose();
        _connection.Dispose();
    }
}
