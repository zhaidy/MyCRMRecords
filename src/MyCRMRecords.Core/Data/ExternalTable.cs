using System.Collections.Concurrent;
using System.Data.Common;

namespace MyCRMRecords.Data;

/// <summary>
/// A table or view of another database (the D365 data lake, RSPSIS, AdvanceConnector) with its columns as they are
/// really named there. The OutSystems apps reached those tables through extensions whose attribute names hold only
/// letters, digits and "_", so a column the old app calls Customer_Name may be [Customer Name] in the database, and
/// Serial_no [Serial-no]. Here a column is found by either name: as written (ignoring case), else by its letters and
/// digits alone, when only one column has them.
/// </summary>
public sealed class ExternalTable
{
    private static readonly ConcurrentDictionary<string, (IReadOnlyList<string> Columns, DateTime At)> Known = new();
    private static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(10);

    private ExternalTable(string name, IReadOnlyList<string> columns)
    {
        Name = name;
        Columns = columns;
    }

    /// <summary>The table's name for SQL text ([ADES].[dbo].[Serial]).</summary>
    public string Name { get; }

    /// <summary>The table's columns, as named in the database.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>The column for SQL text, as named in the database: Customer_Name → [Customer Name]. Unknown, as written.</summary>
    public string this[string column] => "[" + (Find(Columns, column) ?? column).Replace("]", "]]") + "]";

    public override string ToString() => Name;

    /// <summary>The table in the database <paramref name="connection"/> is open on; its columns are read again after 10 minutes.</summary>
    public static ExternalTable Of(DbConnection connection, string table, DbTransaction? transaction = null)
    {
        var name = Sql.Name(table);
        var key = connection.DataSource + "|" + connection.Database + "|" + name;
        if (!Known.TryGetValue(key, out var known) || DateTime.UtcNow - known.At > KeepFor)
        {
            known = (ColumnsOf(connection, transaction, name), DateTime.UtcNow);
            Known[key] = known;
        }
        return new ExternalTable(name, known.Columns);
    }

    /// <summary>The columns of a table (its name as for SQL text), as named in the database.</summary>
    public static IReadOnlyList<string> ColumnsOf(DbConnection connection, DbTransaction? transaction, string name)
    {
        using var command = Sql.Command(connection, transaction, $"SELECT TOP 0 * FROM {name}");
        using var reader = command.ExecuteReader();
        return Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
    }

    /// <summary>
    /// The column of <paramref name="columns"/> that <paramref name="name"/> means: the one of that name ignoring case,
    /// else the only one with the same letters and digits (Customer_Name → Customer Name); null when there is none.
    /// </summary>
    public static string? Find(IEnumerable<string> columns, string name)
    {
        var list = columns as IReadOnlyList<string> ?? columns.ToList();
        var exact = list.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var key = Key(name);
        var same = list.Where(c => Key(c) == key).Take(2).ToList();
        return same.Count == 1 ? same[0] : null;
    }

    /// <summary>A column name's letters and digits, in lower case: what is left of it in its OutSystems name.</summary>
    public static string Key(string name) => new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// The value of the row's column that <paramref name="column"/> means, as <see cref="Find"/> finds it: row
    /// "Customer_Name" is the value of [Customer Name] when the row has no Customer_Name. False when there is none.
    /// </summary>
    public static bool TryGetValue(IReadOnlyDictionary<string, object?> row, string column, out object? value)
    {
        if (row.TryGetValue(column, out value)) return true;
        var found = Find(row.Keys, column);
        if (found is not null) return row.TryGetValue(found, out value);
        value = null;
        return false;
    }
}
