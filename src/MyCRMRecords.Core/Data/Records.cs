using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace MyCRMRecords.Data;

/// <summary>The SQL Server table a record type is kept in (e.g. "dbo.CrProject").</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TableAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>The length of a text column (nvarchar(n); more than 4000 is nvarchar(max)). Text is 400 long without it.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LenAttribute(int length) : Attribute
{
    public int Length { get; } = length;
}

/// <summary>A column searched or filtered on often: the table gets an index on it (text of 450 characters or fewer).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class IndexedAttribute : Attribute;

/// <summary>A property that is not a column (worked out, or kept in another table).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotStoredAttribute : Attribute;

/// <summary>A record kept in a table of its own, one row each, with a number the database gives it.</summary>
public interface IRecord
{
    long Id { get; set; }
}

/// <summary>A column of a record's table: its name, SQL type, and how its value is read from and written to a record.</summary>
public sealed class Column(PropertyInfo property)
{
    public PropertyInfo Property { get; } = property;
    public string Name => Property.Name;
    /// <summary>The property's type, without Nullable&lt;&gt;.</summary>
    public Type Type { get; } = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
    public bool IsNullable { get; } = Nullable.GetUnderlyingType(property.PropertyType) is not null;
    public int Length { get; } = property.GetCustomAttribute<LenAttribute>()?.Length ?? 400;
    public bool Indexed { get; } = property.GetCustomAttribute<IndexedAttribute>() is not null;

    /// <summary>Text nvarchar of its length, amounts decimal(37, 8), times datetime2(3), yes/no bit, a file's bytes varbinary(max).</summary>
    public string SqlType =>
        Type == typeof(string) ? $"nvarchar({(Length > 4000 ? "max" : Length.ToString(CultureInfo.InvariantCulture))})"
        : Type == typeof(byte[]) ? "varbinary(max)"
        : Type == typeof(decimal) ? "decimal(37, 8)"
        : Type == typeof(DateTime) ? "datetime2(3)"
        : Type == typeof(bool) ? "bit"
        : Type == typeof(int) ? "int"
        : Type == typeof(long) ? "bigint"
        : throw new NotSupportedException($"No SQL type for {Type.Name} ({Name}).");

    public object? Get(object record)
    {
        var value = Property.GetValue(record);
        return Type == typeof(string) ? value ?? "" : Type == typeof(byte[]) ? value ?? Array.Empty<byte>() : value;
    }

    /// <summary>Sets a value read from the database (or the JSON file), converted to the property's type.</summary>
    public void Set(object record, object? value)
    {
        if (value is null || value is DBNull)
        {
            Property.SetValue(record, Type == typeof(string) ? "" : Type == typeof(byte[]) ? Array.Empty<byte>() : IsNullable ? null : Activator.CreateInstance(Type));
            return;
        }
        Property.SetValue(record, Convert(value, Type));
    }

    public static object Convert(object value, Type type) =>
        value is DateTimeOffset offset ? Convert(offset.DateTime, type)
        // A file's bytes: as they are from SQL Server, from base64 in the JSON files.
        : type == typeof(byte[]) ? value as byte[] ?? System.Convert.FromBase64String(System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")
        : type == typeof(DateTime) ? DateTime.SpecifyKind(System.Convert.ToDateTime(value, CultureInfo.InvariantCulture), DateTimeKind.Unspecified)
        // decimal(37, 8) comes back with 8 decimals (1.50000000): without the trailing zeros, as it was saved.
        : type == typeof(decimal) ? System.Convert.ToDecimal(value, CultureInfo.InvariantCulture) / 1.0000000000000000000000000000m
        : System.Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
}

/// <summary>A record type's table: its name and columns (Id first).</summary>
public sealed class Table
{
    private static readonly Dictionary<Type, Table> Known = new();

    public Type Type { get; }
    public string Name { get; }
    public string ShortName => Name.Substring(Name.IndexOf('.') + 1);
    public IReadOnlyList<Column> Columns { get; }
    public Column Id => Columns[0];

    private Table(Type type)
    {
        Type = type;
        Name = type.GetCustomAttribute<TableAttribute>()?.Name ?? throw new InvalidOperationException($"{type.Name} has no [Table].");
        var columns = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetCustomAttribute<NotStoredAttribute>() is null)
            .Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) is var t && (t == typeof(string) || t == typeof(byte[]) || t.IsValueType))
            .OrderBy(p => p.MetadataToken)
            .Select(p => new Column(p)).ToList();
        Columns = columns.Where(c => c.Name == "Id").Concat(columns.Where(c => c.Name != "Id")).ToList();
        if (Columns.Count == 0 || Columns[0].Name != "Id" || Columns[0].Type != typeof(long))
            throw new InvalidOperationException($"{type.Name} needs a long Id.");
    }

    public static Table Of<T>() => Of(typeof(T));

    public static Table Of(Type type)
    {
        lock (Known)
        {
            if (!Known.TryGetValue(type, out var table)) Known[type] = table = new Table(type);
            return table;
        }
    }

    public Column Column(string name) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"{Type.Name} has no column {name}.");

    /// <summary>The CREATE TABLE statement (and its indexes) of the table as it is mapped now.</summary>
    public string CreateSql()
    {
        var sql = new StringBuilder();
        sql.Append("CREATE TABLE ").Append(Name).Append(" (\n");
        sql.Append("    [Id] bigint IDENTITY(1,1) NOT NULL,\n");
        foreach (var column in Columns.Skip(1))
            sql.Append("    [").Append(column.Name).Append("] ").Append(column.SqlType).Append(column.IsNullable ? " NULL" : " NOT NULL").Append(",\n");
        sql.Append("    CONSTRAINT PK_").Append(ShortName).Append(" PRIMARY KEY ([Id])\n);");
        foreach (var column in Columns.Where(c => c.Indexed))
            sql.Append("\nCREATE INDEX IX_").Append(ShortName).Append('_').Append(column.Name).Append(" ON ").Append(Name).Append(" ([").Append(column.Name).Append("]);");
        return sql.ToString();
    }
}

public enum Op
{
    Equal,
    NotEqual,
    AtLeast,
    Before,
    In,
    Contains,
}

public sealed record Condition(string Column, Op Op, object? Value);

/// <summary>
/// Which records to read: conditions (all must hold), a text search over some columns (any may contain it), the
/// order, and a page. Text comparisons ignore case, as SQL Server's default collation does.
/// </summary>
public sealed class Query
{
    public List<Condition> Conditions { get; } = [];
    public string? SearchText { get; private set; }
    public string[] SearchColumns { get; private set; } = [];
    public string OrderBy { get; private set; } = "Id";
    public bool Descending { get; private set; }
    public int Skip { get; private set; }
    public int? Take { get; private set; }

    public static Query All() => new();

    public static Query Where(string column, object? value) => new Query().And(column, value);

    public Query And(string column, object? value) => Add(column, Op.Equal, value);
    public Query Not(string column, object? value) => Add(column, Op.NotEqual, value);
    public Query AtLeast(string column, object value) => Add(column, Op.AtLeast, value);
    public Query Before(string column, object value) => Add(column, Op.Before, value);
    public Query In(string column, IEnumerable<object> values) => Add(column, Op.In, values.ToList());
    public Query Contains(string column, string text) => Add(column, Op.Contains, text);

    public Query Search(string? text, params string[] columns)
    {
        SearchText = string.IsNullOrWhiteSpace(text) ? null : text!.Trim();
        SearchColumns = columns;
        return this;
    }

    public Query Sort(string column, bool descending = false)
    {
        OrderBy = column;
        Descending = descending;
        return this;
    }

    public Query Newest() => Sort("Id", descending: true);

    public Query Page(int skip, int take)
    {
        Skip = Math.Max(0, skip);
        Take = Math.Max(0, take);
        return this;
    }

    public Query First(int take) => Page(0, take);

    private Query Add(string column, Op op, object? value)
    {
        Conditions.Add(new Condition(column, op, value));
        return this;
    }

    /// <summary>The same conditions and search, without the order and page (for counting).</summary>
    public Query Unpaged()
    {
        var copy = new Query { SearchText = SearchText, SearchColumns = SearchColumns };
        copy.Conditions.AddRange(Conditions);
        return copy;
    }
}

/// <summary>
/// Reads and writes records, one row each. Two implementations: <see cref="JsonRecordStore"/> (files, for development
/// and until an administrator switches to SQL Server) and <see cref="SqlRecordStore"/>.
/// </summary>
public interface IRecordStore
{
    T? Get<T>(long id) where T : class, IRecord, new();
    List<T> Find<T>(Query? query = null) where T : class, IRecord, new();
    int Count<T>(Query? query = null) where T : class, IRecord, new();
    /// <summary>Adds the record; its Id is set to the number the store gave it.</summary>
    T Insert<T>(T record) where T : class, IRecord, new();
    void Update<T>(T record) where T : class, IRecord, new();
    void Delete<T>(long id) where T : class, IRecord, new();
    /// <summary>Deletes the records the query finds (its order and page are ignored); returns how many.</summary>
    int Delete<T>(Query query) where T : class, IRecord, new();
    /// <summary>Adds records with the Ids they have (copying between stores, imports); later Ids follow the highest.</summary>
    void InsertKeepingIds<T>(IReadOnlyList<T> records) where T : class, IRecord, new();
    /// <summary>Runs <paramref name="work"/> as one change: everything it writes is kept, or nothing when it throws.</summary>
    T Batch<T>(Func<IRecordStore, T> work);
}

public static class RecordStoreExtensions
{
    public static T? FindFirst<T>(this IRecordStore store, Query query) where T : class, IRecord, new() =>
        store.Find<T>(query.First(1)).FirstOrDefault();

    public static void Batch(this IRecordStore store, Action<IRecordStore> work) =>
        store.Batch(s => { work(s); return 0; });
}

/// <summary>Applies a <see cref="Query"/> to records in memory, as SQL Server would.</summary>
public static class QueryInMemory
{
    public static IEnumerable<T> Apply<T>(IEnumerable<T> records, Query query, bool paged = true) where T : class, IRecord
    {
        var table = Table.Of<T>();
        var result = records.Where(r => Matches(table, r, query));
        var order = table.Column(query.OrderBy);
        var comparer = Comparer<object?>.Create(Compare);
        result = query.Descending
            ? result.OrderByDescending(r => order.Get(r), comparer).ThenByDescending(r => r.Id)
            : result.OrderBy(r => order.Get(r), comparer).ThenBy(r => r.Id);
        if (!paged) return result;
        result = result.Skip(query.Skip);
        return query.Take is { } take ? result.Take(take) : result;
    }

    private static bool Matches(Table table, object record, Query query)
    {
        foreach (var condition in query.Conditions)
        {
            var value = table.Column(condition.Column).Get(record);
            var ok = condition.Op switch
            {
                Op.Equal => Equal(value, condition.Value),
                Op.NotEqual => !Equal(value, condition.Value),
                Op.AtLeast => Compare(value, condition.Value) >= 0,
                Op.Before => Compare(value, condition.Value) < 0,
                Op.In => ((IEnumerable<object>)condition.Value!).Any(v => Equal(value, v)),
                Op.Contains => Text(value).IndexOf((string)condition.Value!, StringComparison.OrdinalIgnoreCase) >= 0,
                _ => false,
            };
            if (!ok) return false;
        }
        if (query.SearchText is { } text)
            return query.SearchColumns.Any(c => Text(table.Column(c).Get(record)).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
        return true;
    }

    private static string Text(object? value) => System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

    private static bool Equal(object? a, object? b)
    {
        if (a is string || b is string) return string.Equals(Text(a).TrimEnd(), Text(b).TrimEnd(), StringComparison.OrdinalIgnoreCase);
        if (a is null || b is null) return a is null && b is null;
        return Compare(a, b) == 0;
    }

    private static int Compare(object? a, object? b)
    {
        if (a is null || b is null) return a is null ? (b is null ? 0 : -1) : 1;
        if (a is string || b is string) return string.Compare(Text(a), Text(b), StringComparison.OrdinalIgnoreCase);
        if (a is DateTime da && b is DateTime db) return da.CompareTo(db);
        if (a is bool ba && b is bool bb) return ba.CompareTo(bb);
        return System.Convert.ToDecimal(a, CultureInfo.InvariantCulture).CompareTo(System.Convert.ToDecimal(b, CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Keeps every table in a JSON file of its own in a folder (App_Data/data): for development and until an
/// administrator switches to SQL Server. Every operation (or batch) holds a lock file and reads the files afresh, so it
/// stays correct when IIS overlaps two worker processes.
/// </summary>
public sealed class JsonRecordStore(string folder) : IRecordStore
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    private readonly string _folder = Path.GetFullPath(folder);
    private readonly object _gate = new();
    private readonly ThreadLocal<Session?> _session = new();

    public string Folder => _folder;

    private sealed class TableFile
    {
        public long NextId { get; set; } = 1;
        public List<Dictionary<string, object?>> Rows { get; set; } = [];
    }

    private sealed class Session(JsonRecordStore store)
    {
        private readonly Dictionary<string, (TableFile File, bool Dirty)> _tables = new();

        public TableFile Table(Table table, bool write)
        {
            if (!_tables.TryGetValue(table.ShortName, out var entry))
            {
                var path = store.PathOf(table);
                var file = File.Exists(path) ? Newtonsoft.Json.JsonConvert.DeserializeObject<TableFile>(File.ReadAllText(path), FileSettings) ?? new TableFile() : new TableFile();
                entry = (file, false);
            }
            if (write) entry.Dirty = true;
            _tables[table.ShortName] = entry;
            return entry.File;
        }

        public void Save()
        {
            foreach (var pair in _tables)
            {
                var (name, entry) = (pair.Key, pair.Value);
                if (!entry.Dirty) continue;
                var path = Path.Combine(store._folder, name + ".json");
                var temp = path + ".tmp";
                File.WriteAllText(temp, Newtonsoft.Json.JsonConvert.SerializeObject(entry.File, FileSettings));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
        }
    }

    private string PathOf(Table table) => Path.Combine(_folder, table.ShortName + ".json");

    /// <summary>Values are read as they were written (no text is taken for a date), the columns convert them.</summary>
    private static readonly Newtonsoft.Json.JsonSerializerSettings FileSettings = new()
    {
        DateParseHandling = Newtonsoft.Json.DateParseHandling.None,
        FloatParseHandling = Newtonsoft.Json.FloatParseHandling.Decimal,
        DateFormatString = "yyyy-MM-dd'T'HH:mm:ss.fff",
    };

    private TResult Run<TResult>(Func<Session, TResult> work)
    {
        if (_session.Value is { } current) return work(current);
        lock (_gate)
        {
            Directory.CreateDirectory(_folder);
            using var fileLock = AcquireLock();
            var session = new Session(this);
            _session.Value = session;
            try
            {
                var result = work(session);
                session.Save();
                return result;
            }
            finally
            {
                _session.Value = null;
            }
        }
    }

    private FileStream AcquireLock()
    {
        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            try
            {
                return new FileStream(Path.Combine(_folder, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static T Read<T>(Table table, Dictionary<string, object?> row) where T : class, IRecord, new()
    {
        var record = new T();
        foreach (var column in table.Columns)
            column.Set(record, row.TryGetValue(column.Name, out var value) ? Plain(value) : null);
        return record;
    }

    /// <summary>A value as Newtonsoft read it (a JValue for numbers and dates) as a plain value.</summary>
    private static object? Plain(object? value) => value is Newtonsoft.Json.Linq.JValue j ? j.Value : value;

    private static Dictionary<string, object?> Write(Table table, object record) =>
        table.Columns.ToDictionary(c => c.Name, c => c.Get(record));

    private static IEnumerable<T> All<T>(Session session) where T : class, IRecord, new()
    {
        var table = Table.Of<T>();
        return session.Table(table, write: false).Rows.Select(r => Read<T>(table, r)).ToList();
    }

    public T? Get<T>(long id) where T : class, IRecord, new() =>
        Run(s => All<T>(s).FirstOrDefault(r => r.Id == id));

    public List<T> Find<T>(Query? query = null) where T : class, IRecord, new() =>
        Run(s => QueryInMemory.Apply(All<T>(s), query ?? Query.All()).ToList());

    public int Count<T>(Query? query = null) where T : class, IRecord, new() =>
        Run(s => QueryInMemory.Apply(All<T>(s), query ?? Query.All(), paged: false).Count());

    public T Insert<T>(T record) where T : class, IRecord, new() =>
        Run(s =>
        {
            var table = Table.Of<T>();
            var file = s.Table(table, write: true);
            record.Id = file.NextId++;
            file.Rows.Add(Write(table, record));
            return record;
        });

    public void InsertKeepingIds<T>(IReadOnlyList<T> records) where T : class, IRecord, new() =>
        Run(s =>
        {
            var table = Table.Of<T>();
            var file = s.Table(table, write: true);
            var existing = new HashSet<long>(file.Rows.Select(r => Convert.ToInt64(Plain(r["Id"]), CultureInfo.InvariantCulture)));
            foreach (var record in records)
            {
                if (!existing.Add(record.Id)) throw new InvalidOperationException($"{table.Type.Name} {record.Id} exists already.");
                file.Rows.Add(Write(table, record));
                file.NextId = Math.Max(file.NextId, record.Id + 1);
            }
            return 0;
        });

    public void Update<T>(T record) where T : class, IRecord, new() =>
        Run(s =>
        {
            var table = Table.Of<T>();
            var file = s.Table(table, write: true);
            var index = file.Rows.FindIndex(r => Convert.ToInt64(Plain(r["Id"]), CultureInfo.InvariantCulture) == record.Id);
            if (index < 0) throw new InvalidOperationException($"{table.Type.Name} {record.Id} does not exist.");
            file.Rows[index] = Write(table, record);
            return 0;
        });

    public void Delete<T>(long id) where T : class, IRecord, new() =>
        Run(s =>
        {
            var file = s.Table(Table.Of<T>(), write: true);
            file.Rows.RemoveAll(r => Convert.ToInt64(Plain(r["Id"]), CultureInfo.InvariantCulture) == id);
            return 0;
        });

    public int Delete<T>(Query query) where T : class, IRecord, new() =>
        Run(s =>
        {
            var ids = QueryInMemory.Apply(All<T>(s), query.Unpaged(), paged: false).Select(r => r.Id).ToList();
            var file = s.Table(Table.Of<T>(), write: true);
            return file.Rows.RemoveAll(r => ids.Contains(Convert.ToInt64(Plain(r["Id"]), CultureInfo.InvariantCulture)));
        });

    public T Batch<T>(Func<IRecordStore, T> work) => Run(_ => work(this));
}

/// <summary>
/// Keeps every record in its SQL Server table (created by the data migrations). Each call is its own statement;
/// <see cref="Batch{T}"/> runs a group of them in one transaction.
/// </summary>
public sealed class SqlRecordStore : IRecordStore
{
    private readonly string _connectionString;
    private readonly Func<string, DbConnection> _connectionFactory;
    private readonly DbConnection? _connection;
    private readonly DbTransaction? _transaction;

    public SqlRecordStore(string connectionString, Func<string, DbConnection> connectionFactory)
    {
        _connectionString = connectionString;
        _connectionFactory = connectionFactory;
    }

    private SqlRecordStore(SqlRecordStore parent, DbConnection connection, DbTransaction transaction)
    {
        _connectionString = parent._connectionString;
        _connectionFactory = parent._connectionFactory;
        _connection = connection;
        _transaction = transaction;
    }

    public string ConnectionString => _connectionString;

    public DbConnection Open()
    {
        var connection = _connectionFactory(_connectionString);
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

    private TResult Use<TResult>(Func<DbConnection, DbTransaction?, TResult> action)
    {
        if (_connection is not null) return action(_connection, _transaction);
        using var connection = Open();
        return action(connection, null);
    }

    public T Batch<T>(Func<IRecordStore, T> work)
    {
        if (_connection is not null) return work(this);
        using var connection = Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var result = work(new SqlRecordStore(this, connection, transaction));
        transaction.Commit();
        return result;
    }

    public T? Get<T>(long id) where T : class, IRecord, new() =>
        Find<T>(Query.Where("Id", id).First(1)).FirstOrDefault();

    public List<T> Find<T>(Query? query = null) where T : class, IRecord, new()
    {
        query ??= Query.All();
        var table = Table.Of<T>();
        return Use((connection, transaction) =>
        {
            using var command = Sql.Command(connection, transaction, "");
            var where = Where(table, query, command);
            var order = table.Column(query.OrderBy);
            var direction = query.Descending ? " DESC" : "";
            var sql = new StringBuilder("SELECT ")
                .Append(string.Join(", ", table.Columns.Select(c => "[" + c.Name + "]")))
                .Append(" FROM ").Append(table.Name).Append(where)
                .Append(" ORDER BY [").Append(order.Name).Append(']').Append(direction);
            if (order.Name != "Id") sql.Append(", [Id]").Append(direction);
            if (query.Skip > 0 || query.Take is not null)
            {
                sql.Append(" OFFSET ").Append(query.Skip.ToString(CultureInfo.InvariantCulture)).Append(" ROWS");
                if (query.Take is { } take) sql.Append(" FETCH NEXT ").Append(take.ToString(CultureInfo.InvariantCulture)).Append(" ROWS ONLY");
            }
            command.CommandText = sql.ToString();
            var list = new List<T>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var record = new T();
                for (var i = 0; i < table.Columns.Count; i++) table.Columns[i].Set(record, reader.IsDBNull(i) ? null : reader.GetValue(i));
                list.Add(record);
            }
            return list;
        });
    }

    public int Count<T>(Query? query = null) where T : class, IRecord, new()
    {
        var table = Table.Of<T>();
        return Use((connection, transaction) =>
        {
            using var command = Sql.Command(connection, transaction, "");
            command.CommandText = "SELECT COUNT(*) FROM " + table.Name + Where(table, query ?? Query.All(), command);
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        });
    }

    public T Insert<T>(T record) where T : class, IRecord, new()
    {
        var table = Table.Of<T>();
        return Use((connection, transaction) =>
        {
            var columns = table.Columns.Skip(1).ToList();
            using var command = Sql.Command(connection, transaction,
                $"INSERT INTO {table.Name} ({string.Join(", ", columns.Select(c => "[" + c.Name + "]"))}) OUTPUT INSERTED.[Id] VALUES ({string.Join(", ", columns.Select((_, i) => "@p" + i.ToString(CultureInfo.InvariantCulture)))})");
            for (var i = 0; i < columns.Count; i++) Sql.Add(command, "@p" + i.ToString(CultureInfo.InvariantCulture), columns[i].Get(record), columns[i].Type);
            record.Id = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            return record;
        });
    }

    public void InsertKeepingIds<T>(IReadOnlyList<T> records) where T : class, IRecord, new()
    {
        if (records.Count == 0) return;
        var table = Table.Of<T>();
        Batch(store =>
        {
            ((SqlRecordStore)store).Use((connection, transaction) =>
            {
                Sql.Execute(connection, transaction, $"SET IDENTITY_INSERT {table.Name} ON");
                var names = string.Join(", ", table.Columns.Select(c => "[" + c.Name + "]"));
                var perStatement = Math.Max(1, 2000 / table.Columns.Count);
                foreach (var chunk in records.Select((r, i) => (r, i)).GroupBy(x => x.i / perStatement, x => x.r))
                {
                    using var command = Sql.Command(connection, transaction, "");
                    var values = new List<string>();
                    var n = 0;
                    foreach (var record in chunk)
                    {
                        var parameters = new List<string>();
                        foreach (var column in table.Columns)
                        {
                            var name = "@p" + (n++).ToString(CultureInfo.InvariantCulture);
                            Sql.Add(command, name, column.Get(record), column.Type);
                            parameters.Add(name);
                        }
                        values.Add("(" + string.Join(", ", parameters) + ")");
                    }
                    command.CommandText = $"INSERT INTO {table.Name} ({names}) VALUES {string.Join(", ", values)}";
                    command.CommandTimeout = 600;
                    command.ExecuteNonQuery();
                }
                Sql.Execute(connection, transaction, $"SET IDENTITY_INSERT {table.Name} OFF");
                return 0;
            });
            return 0;
        });
    }

    public void Update<T>(T record) where T : class, IRecord, new()
    {
        var table = Table.Of<T>();
        Use((connection, transaction) =>
        {
            var columns = table.Columns.Skip(1).ToList();
            using var command = Sql.Command(connection, transaction,
                $"UPDATE {table.Name} SET {string.Join(", ", columns.Select((c, i) => "[" + c.Name + "] = @p" + i.ToString(CultureInfo.InvariantCulture)))} WHERE [Id] = @id");
            for (var i = 0; i < columns.Count; i++) Sql.Add(command, "@p" + i.ToString(CultureInfo.InvariantCulture), columns[i].Get(record), columns[i].Type);
            Sql.Add(command, "@id", record.Id, typeof(long));
            if (command.ExecuteNonQuery() == 0) throw new InvalidOperationException($"{table.Type.Name} {record.Id} does not exist.");
            return 0;
        });
    }

    public void Delete<T>(long id) where T : class, IRecord, new() => Delete<T>(Query.Where("Id", id));

    public int Delete<T>(Query query) where T : class, IRecord, new()
    {
        var table = Table.Of<T>();
        return Use((connection, transaction) =>
        {
            using var command = Sql.Command(connection, transaction, "");
            command.CommandText = "DELETE FROM " + table.Name + Where(table, query.Unpaged(), command);
            command.CommandTimeout = 600;
            return command.ExecuteNonQuery();
        });
    }

    /// <summary>The WHERE clause of the query (empty when there is none), adding its parameters to the command.</summary>
    private static string Where(Table table, Query query, DbCommand command)
    {
        var parts = new List<string>();
        var n = 0;
        string Parameter(object? value, Type type)
        {
            var name = "@w" + (n++).ToString(CultureInfo.InvariantCulture);
            Sql.Add(command, name, value, type);
            return name;
        }
        foreach (var condition in query.Conditions)
        {
            var column = table.Column(condition.Column);
            var name = "[" + column.Name + "]";
            switch (condition.Op)
            {
                case Op.Equal when condition.Value is null:
                    parts.Add(name + " IS NULL");
                    break;
                case Op.NotEqual when condition.Value is null:
                    parts.Add(name + " IS NOT NULL");
                    break;
                case Op.Equal:
                    parts.Add(name + " = " + Parameter(condition.Value, column.Type));
                    break;
                case Op.NotEqual:
                    parts.Add("(" + name + " <> " + Parameter(condition.Value, column.Type) + (column.IsNullable ? " OR " + name + " IS NULL" : "") + ")");
                    break;
                case Op.AtLeast:
                    parts.Add(name + " >= " + Parameter(condition.Value, column.Type));
                    break;
                case Op.Before:
                    parts.Add(name + " < " + Parameter(condition.Value, column.Type));
                    break;
                case Op.In:
                    var values = ((IEnumerable<object>)condition.Value!).ToList();
                    // SQL Server takes at most 2100 parameters in a query: a long list (the work orders of thousands of requests)
                    // is written into the statement instead, numbers as they are and text quoted (Sql.Literal).
                    parts.Add(values.Count == 0 ? "1 = 0" : name + " IN (" + string.Join(", ", values.Select(v =>
                        values.Count > 500 && Sql.Literal(v, column.Type) is { } literal ? literal : Parameter(v, column.Type))) + ")");
                    break;
                case Op.Contains:
                    parts.Add(name + " LIKE " + Parameter("%" + Sql.EscapeLike((string)condition.Value!) + "%", typeof(string)) + " ESCAPE '\\'");
                    break;
            }
        }
        if (query.SearchText is { } text && query.SearchColumns.Length > 0)
        {
            var pattern = Parameter("%" + Sql.EscapeLike(text) + "%", typeof(string));
            parts.Add("(" + string.Join(" OR ", query.SearchColumns.Select(c => "[" + table.Column(c).Name + "] LIKE " + pattern + " ESCAPE '\\'")) + ")");
        }
        return parts.Count == 0 ? "" : " WHERE " + string.Join(" AND ", parts);
    }
}

/// <summary>Small helpers for writing SQL commands.</summary>
public static class Sql
{
    public static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = 120;
        return command;
    }

    public static void Add(DbCommand command, string name, object? value, Type? type = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        type ??= value?.GetType();
        type = type is null ? null : Nullable.GetUnderlyingType(type) ?? type;
        parameter.DbType = type == typeof(long) ? DbType.Int64
            : type == typeof(int) ? DbType.Int32
            : type == typeof(decimal) ? DbType.Decimal
            : type == typeof(bool) ? DbType.Boolean
            : type == typeof(DateTime) ? DbType.DateTime2
            : type == typeof(byte[]) ? DbType.Binary
            : DbType.String;
        if (parameter.DbType == DbType.Decimal) { parameter.Precision = 37; parameter.Scale = 8; }
        if (parameter.DbType == DbType.String) parameter.Size = value is string s && s.Length > 4000 ? -1 : 4000;
        if (parameter.DbType == DbType.Binary) parameter.Size = -1;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// A value of a column of that type written into SQL text (for long IN lists): a whole number as it is, text as
    /// N'...' with its quotes doubled; null for other values, which go as parameters.
    /// </summary>
    public static string? Literal(object? value, Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (value is null) return "NULL";
        if ((type == typeof(long) || type == typeof(int)) && value is long or int or short or byte)
            return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        if (type == typeof(string) && value is string text) return "N'" + text.Replace("'", "''") + "'";
        return null;
    }

    /// <summary>Text for a LIKE pattern, with % _ [ and \ taken literally (ESCAPE '\').</summary>
    public static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    public static bool TableExists(DbConnection connection, DbTransaction? transaction, string table)
    {
        using var command = Command(connection, transaction, "SELECT CASE WHEN OBJECT_ID(@table) IS NULL THEN 0 ELSE 1 END");
        Add(command, "@table", table);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    /// <summary>Runs a query and reads every row as name → value (DBNull as null).</summary>
    public static List<Dictionary<string, object?>> Rows(DbConnection connection, DbTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql);
        foreach (var (name, value) in parameters) Add(command, name, value);
        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    public static int Execute(DbConnection connection, DbTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql);
        foreach (var (name, value) in parameters) Add(command, name, value);
        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// A table or view name for SQL text: each part bracketed (dbo.My Table → [dbo].[My Table]). A part written in
    /// brackets keeps its dots: dbo.[OWS_Customer.ContactPerson] is the table OWS_Customer.ContactPerson of dbo.
    /// </summary>
    public static string Name(string name)
    {
        var parts = NameParts(name).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) throw new ArgumentException("No table name.");
        return string.Join(".", parts.Select(p => "[" + p.Replace("]", "]]") + "]"));
    }

    /// <summary>The parts of a name split at the dots outside brackets ("]]" in brackets is one "]").</summary>
    public static List<string> NameParts(string name)
    {
        var parts = new List<string>();
        var part = new StringBuilder();
        var bracketed = false;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (bracketed)
            {
                if (c != ']') part.Append(c);
                else if (i + 1 < name.Length && name[i + 1] == ']') { part.Append(']'); i++; }
                else bracketed = false;
            }
            else if (c == '[') bracketed = true;
            else if (c == '.') { parts.Add(part.ToString()); part.Clear(); }
            else part.Append(c);
        }
        parts.Add(part.ToString());
        return parts;
    }
}
