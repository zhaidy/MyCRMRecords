using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Data;

/// <summary>A data migration that has run, from dbo.CrMigrations.</summary>
public sealed record AppliedMigration(string Id, string Title, string AppliedBy, DateTime AppliedAtUtc);

/// <summary>What the admin page shows after connecting to a SQL Server database.</summary>
public sealed record SqlServerStatus(string ServerVersion, bool TablesExist, long Records);

/// <summary>
/// Where MyCRMRecords keeps its records: the JSON files or SQL Server, as the database settings say. The settings are
/// read on every operation, so a change on the admin page applies at once, in every IIS worker process.
/// </summary>
public sealed class DatabaseStorage(DatabaseSettingsFile settingsFile, JsonRecordStore jsonStore, Func<string, DbConnection> connectionFactory)
    : IRecordStore
{
    private static readonly Regex GoLine = new(@"^[ \t]*GO[ \t]*\r?$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public DatabaseSettingsFile SettingsFile => settingsFile;
    public JsonRecordStore JsonStore => jsonStore;
    public Func<string, DbConnection> ConnectionFactory => connectionFactory;

    public SqlRecordStore SqlServer(SqlServerSettings settings) => new(settings.ToConnectionString(), connectionFactory);

    /// <summary>The SQL Server store when it is in use, else null.</summary>
    public SqlRecordStore? CurrentSqlServer()
    {
        var settings = settingsFile.Load();
        return settings.Provider == StorageProviders.SqlServer ? SqlServer(settings.SqlServer) : null;
    }

    public IRecordStore Current() => (IRecordStore?)CurrentSqlServer() ?? jsonStore;

    public T? Get<T>(long id) where T : class, IRecord, new() => Current().Get<T>(id);
    public List<T> Find<T>(Query? query = null) where T : class, IRecord, new() => Current().Find<T>(query);
    public int Count<T>(Query? query = null) where T : class, IRecord, new() => Current().Count<T>(query);
    public T Insert<T>(T record) where T : class, IRecord, new() => Current().Insert(record);
    public void InsertKeepingIds<T>(IReadOnlyList<T> records) where T : class, IRecord, new() => Current().InsertKeepingIds(records);
    public void Update<T>(T record) where T : class, IRecord, new() => Current().Update(record);
    public void Delete<T>(long id) where T : class, IRecord, new() => Current().Delete<T>(id);
    public int Delete<T>(Query query) where T : class, IRecord, new() => Current().Delete<T>(query);
    public T Batch<T>(Func<IRecordStore, T> work) => Current().Batch(work);

    // ---------------------------------------------------------------- SQL Server: status and migrations

    /// <summary>Connects and reports whether MyCRMRecords's tables exist and how many records they hold.</summary>
    public SqlServerStatus Probe(SqlServerSettings settings)
    {
        using var connection = Open(settings);
        var version = connection.ServerVersion;
        var counts = Counts(connection);
        var exists = counts.Any(c => c.Rows >= 0);
        return new SqlServerStatus(version, exists, counts.Where(c => c.Rows > 0).Sum(c => c.Rows));
    }

    /// <summary>Each table with its row count; -1 for a table that does not exist yet.</summary>
    public IReadOnlyList<(string Table, long Rows)> TableCounts(SqlServerSettings settings)
    {
        using var connection = Open(settings);
        return Counts(connection);
    }

    private static List<(string Table, long Rows)> Counts(DbConnection connection)
    {
        var result = new List<(string, long)>();
        foreach (var name in Tables.All.Select(t => Table.Of(t).Name).Concat([DataMigrations.Table]))
        {
            if (!Sql.TableExists(connection, null, name)) { result.Add((name, -1)); continue; }
            using var command = Sql.Command(connection, null, $"SELECT COUNT_BIG(*) FROM {name}");
            result.Add((name, Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture)));
        }
        return result;
    }

    public IReadOnlyList<AppliedMigration> AppliedMigrations(SqlServerSettings settings)
    {
        using var connection = Open(settings);
        if (!Sql.TableExists(connection, null, DataMigrations.Table)) return [];
        return Sql.Rows(connection, null, $"SELECT [Id], [Title], [AppliedBy], [AppliedAtUtc] FROM {DataMigrations.Table} ORDER BY [AppliedAtUtc], [Id]")
            .Select(r => new AppliedMigration((string)r["Id"]!, (string)r["Title"]!, (string)r["AppliedBy"]!,
                DateTime.SpecifyKind((DateTime)r["AppliedAtUtc"]!, DateTimeKind.Utc)))
            .ToList();
    }

    /// <summary>
    /// Runs a migration: its SQL (batches separated by GO lines) and its record in dbo.CrMigrations, in one
    /// transaction, so a failure changes nothing. Refuses one that has run.
    /// </summary>
    public void RunMigration(SqlServerSettings settings, DataMigration migration, string appliedBy)
    {
        using var connection = Open(settings);
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        using (var lockCommand = Sql.Command(connection, transaction,
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = 'MyCRMRecords migrations', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; SELECT @r;"))
        {
            if (Convert.ToInt32(lockCommand.ExecuteScalar(), CultureInfo.InvariantCulture) < 0)
                throw new TimeoutException("Another migration is running; please try again in a moment.");
        }
        foreach (var sql in GoLine.Split(DataMigrations.CreateTableSql).Where(b => b.Trim().Length > 0))
            Sql.Execute(connection, transaction, sql);
        var ran = Sql.Rows(connection, transaction, $"SELECT [Id] FROM {DataMigrations.Table} WHERE [Id] = @id", ("@id", migration.Id));
        if (ran.Count > 0) throw new InvalidOperationException($"The migration {migration.Id} has already run.");
        foreach (var sql in GoLine.Split(migration.Sql).Where(b => b.Trim().Length > 0))
        {
            using var command = Sql.Command(connection, transaction, sql);
            command.CommandTimeout = 600;
            command.ExecuteNonQuery();
        }
        Sql.Execute(connection, transaction,
            $"INSERT INTO {DataMigrations.Table} ([Id], [Title], [AppliedBy], [AppliedAtUtc]) VALUES (@id, @title, @by, SYSUTCDATETIME())",
            ("@id", migration.Id), ("@title", migration.Title), ("@by", appliedBy));
        transaction.Commit();
    }

    /// <summary>Runs every migration that has not run yet, in order (a new database gets every table).</summary>
    public IReadOnlyList<string> RunPendingMigrations(SqlServerSettings settings, string appliedBy)
    {
        var applied = AppliedMigrations(settings).Select(m => m.Id).ToList();
        var run = new List<string>();
        foreach (var migration in DataMigrations.All.Where(m => !applied.Contains(m.Id)))
        {
            RunMigration(settings, migration, appliedBy);
            run.Add(migration.Id);
        }
        return run;
    }

    public DbConnection Open(SqlServerSettings settings)
    {
        var connection = connectionFactory(settings.ToConnectionString());
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Copies every record of every table from one store to another (switching storage), keeping the Ids.</summary>
    public static int CopyAll(IRecordStore from, IRecordStore to)
    {
        var copy = typeof(DatabaseStorage).GetMethod(nameof(CopyTable), BindingFlags.NonPublic | BindingFlags.Static)!;
        return Tables.All.Sum(type => (int)copy.MakeGenericMethod(type).Invoke(null, [from, to])!);
    }

    private static int CopyTable<T>(IRecordStore from, IRecordStore to) where T : class, IRecord, new()
    {
        var total = 0;
        long after = 0;
        while (true)
        {
            var page = from.Find<T>(Query.All().AtLeast("Id", after + 1).Sort("Id").First(1000));
            if (page.Count == 0) return total;
            to.InsertKeepingIds(page);
            total += page.Count;
            after = page[page.Count - 1].Id;
        }
    }

    /// <summary>Whether a store holds no record in any table.</summary>
    public static bool IsEmpty(IRecordStore store)
    {
        var count = typeof(IRecordStore).GetMethod(nameof(IRecordStore.Count))!;
        return Tables.All.All(type => (int)count.MakeGenericMethod(type).Invoke(store, [null])! == 0);
    }
}
