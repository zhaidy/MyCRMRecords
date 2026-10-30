using System.Collections.Concurrent;
using System.Data.Common;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Data;

public static class ExternalNames
{
    public const string OutSystems = "OutSystems";
    public const string AdvanceConnector = "AdvanceConnector";
    public static readonly string[] All = [OutSystems, AdvanceConnector];

    public static string Title(string name) => name switch
    {
        OutSystems => "OutSystems database",
        AdvanceConnector => "AdvanceConnector",
        _ => name,
    };
}

/// <summary>
/// The other databases MyCRMRecords works with, as set on the admin page: the OutSystems platform database (MyCRM's
/// tables, for the import and the sync; its users and roles) and AdvanceConnector (Advance's customers, read only). An
/// OutSystems entity's table has a generated name (OSUSR_ab1_FEEDBACK): it is looked up in the platform's catalog
/// (OSSYS_ESPACE, OSSYS_ENTITY) by module and entity name; its columns are the attribute names.
/// </summary>
public sealed class ExternalDatabases(Func<CrmSettings> settings, Func<string, DbConnection> connectionFactory)
{
    private static readonly ConcurrentDictionary<string, (string Table, DateTime At)> EntityTables = new();

    public ExternalDatabase Settings(string name)
    {
        var connections = settings().Connections;
        return name switch
        {
            ExternalNames.OutSystems => connections.OutSystems,
            ExternalNames.AdvanceConnector => connections.AdvanceConnector,
            _ => throw new ArgumentException("No database " + name),
        };
    }

    public bool IsConfigured(string name) => Settings(name) is { Configured: true } database && database.SqlServer.Validate().Count == 0;

    /// <summary>An open connection to the database; throws <see cref="InvalidOperationException"/> when it is not set up.</summary>
    public DbConnection Open(string name)
    {
        if (!IsConfigured(name)) throw new InvalidOperationException($"The {ExternalNames.Title(name)} is not set up (admin page, Connections tab).");
        var connection = connectionFactory(Settings(name).SqlServer.ToConnectionString());
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

    public T Use<T>(string name, Func<DbConnection, T> work)
    {
        using var connection = Open(name);
        return work(connection);
    }

    /// <summary>
    /// The table of an OutSystems entity, e.g. "dbo.[OSUSR_ab1_PARTREQUEST]" (cached for ten minutes). With
    /// <paramref name="retiredToo"/> (MyOrders, which is read only), a module deleted in OutSystems is still found while
    /// its table is there; MyCRM, which MyCRMRecords may write, is only ever found active.
    /// </summary>
    public string EntityTable(DbConnection outSystems, string module, string entity, bool retiredToo = false)
    {
        var key = outSystems.ConnectionString + "|" + module + "|" + entity + "|" + retiredToo;
        if (EntityTables.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(10)) return cached.Table;
        var rows = Sql.Rows(outSystems, null,
            "SELECT TOP 1 e.PHYSICAL_TABLE_NAME FROM dbo.OSSYS_ENTITY e JOIN dbo.OSSYS_ESPACE s ON s.ID = e.ESPACE_ID " +
            "WHERE s.NAME = @module AND s.IS_ACTIVE = 1 AND e.NAME = @entity AND e.IS_ACTIVE = 1 ORDER BY e.ID DESC",
            ("@module", module), ("@entity", entity));
        // A module deleted in OutSystems (MyOrders once it is retired) is inactive in the catalog, but OutSystems keeps its
        // tables and their data: its latest entity whose table is still there is the one.
        if (rows.Count == 0 && retiredToo)
            rows = Sql.Rows(outSystems, null,
                "SELECT TOP 1 e.PHYSICAL_TABLE_NAME FROM dbo.OSSYS_ENTITY e JOIN dbo.OSSYS_ESPACE s ON s.ID = e.ESPACE_ID " +
                "WHERE s.NAME = @module AND e.NAME = @entity AND OBJECT_ID(N'dbo.' + QUOTENAME(e.PHYSICAL_TABLE_NAME)) IS NOT NULL ORDER BY s.IS_ACTIVE DESC, e.ID DESC",
                ("@module", module), ("@entity", entity));
        if (rows.Count == 0 || rows[0]["PHYSICAL_TABLE_NAME"] is not string physical)
            throw new InvalidOperationException($"The OutSystems database has no entity {entity} in the module {module}.");
        var table = "dbo." + Sql.Name(physical);
        EntityTables[key] = (table, DateTime.UtcNow);
        return table;
    }

    /// <summary>The Id of a static entity's record by its label (spaces and case ignored), e.g. "Pending for Collection".</summary>
    public long? StaticRecordId(DbConnection outSystems, string module, string entity, string label)
    {
        var table = EntityTable(outSystems, module, entity);
        var wanted = Normalise(label);
        foreach (var row in Sql.Rows(outSystems, null, $"SELECT [ID], [LABEL] FROM {table}"))
            if (Normalise(Convert.ToString(row["LABEL"]) ?? "") == wanted) return Convert.ToInt64(row["ID"]);
        return null;
    }

    /// <summary>
    /// For a table name with a dot outside brackets that was not found: how to write a table whose own name has a dot
    /// (OWS_Customer.ContactPerson is the table ContactPerson of the schema OWS_Customer unless written in brackets).
    /// </summary>
    public static string DottedNameHint(string table)
    {
        if (table.IndexOf('[') >= 0) return "";
        var parts = Sql.NameParts(table).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count < 2) return "";
        var suggestion = parts.Count == 2 ? $"dbo.[{table.Trim()}]" : $"{parts[0]}.[{string.Join(".", parts.Skip(1))}]";
        return $" If the table's own name has a dot, write it in brackets: {suggestion}";
    }

    private static string Normalise(string text) => new string(text.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>
    /// Connects and checks what MyCRMRecords needs from the database is there: each table (or OutSystems entity) and its
    /// columns. Returns one line per check.
    /// </summary>
    public IReadOnlyList<(bool Ok, string Line)> Test(string name)
    {
        var lines = new List<(bool, string)>();
        DbConnection connection;
        try
        {
            connection = Open(name);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            lines.Add((false, "Cannot connect: " + ex.Message));
            return lines;
        }
        using (connection)
        {
            lines.Add((true, "Connected to SQL Server " + connection.ServerVersion + "."));
            var c = settings().Connections;
            void Check(string table, params string[] columns)
            {
                try
                {
                    // Found also by the old app's name for the column: Customer_Name is [Customer Name] in AdvanceConnector.
                    var have = ExternalTable.ColumnsOf(connection, null, Sql.Name(table));
                    var missing = columns.Where(col => ExternalTable.Find(have, col) is null).ToList();
                    lines.Add(missing.Count == 0
                        ? (true, $"{table}: found.")
                        : (false, $"{table}: found, but without the column{(missing.Count == 1 ? "" : "s")} {string.Join(", ", missing)}."));
                }
                catch (DbException ex)
                {
                    lines.Add((false, $"{table}: {ex.Message}" + DottedNameHint(table)));
                }
            }
            void Entity(string module, string entity, string[] columns, bool retiredToo = false)
            {
                try
                {
                    Check(EntityTable(connection, module, entity, retiredToo), columns);
                }
                catch (Exception ex) when (ex is DbException or InvalidOperationException)
                {
                    lines.Add((false, $"{module}.{entity}: {ex.Message}"));
                }
            }
            switch (name)
            {
                case ExternalNames.OutSystems:
                    foreach (var (entity, columns) in MyCrmMap.Checked)
                        Entity(c.CrmModule, entity, columns);
                    // What the costing sheets and request forms read (MyOrders' tables, and MyCRM's they name), and the Files page (MyFiles').
                    foreach (var check in Entities.Checked)
                        Entity(Entities.ModuleName(c, check.Entity.Module), check.Entity.Name, check.Columns, Entities.ReadOnly(check.Entity.Module));
                    Check("dbo.OSSYS_USER", "ID", "USERNAME", "NAME", "EMAIL", "IS_ACTIVE");
                    break;
                case ExternalNames.AdvanceConnector:
                    Check(c.AdvanceTables.CustomerSupp, ExternalColumns.CustomerSupp);
                    break;
            }
        }
        return lines;
    }
}

/// <summary>OutSystems keeps an empty date as 1900-01-01 00:00:00 rather than NULL.</summary>
public static class OutSystemsValues
{
    public static readonly DateTime NullDate = new(1900, 1, 1);

    public static object Db(DateTime? value) => value ?? NullDate;

    public static DateTime? Date(object? value) =>
        value is DateTime date && date.Year > 1900 ? DateTime.SpecifyKind(date, DateTimeKind.Unspecified) : null;

    public static string Text(object? value) => (Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "").Trim();
}

/// <summary>
/// The columns MyCRMRecords reads in the other databases, by MyCRM's names for them (its extension entities' attributes).
/// The real columns may be written differently (Cust_no is [Cust-no] in AdvanceConnector): SQL names them through
/// <see cref="ExternalTable"/>, and the connection test finds them the same way.
/// </summary>
public static class ExternalColumns
{
    /// <summary>AdvanceConnector's Customer_Supp (MyCRM's entity of the same name): a customer's BRN and number.</summary>
    public static readonly string[] CustomerSupp = ["Business_reg", "Cust_no"];
}
