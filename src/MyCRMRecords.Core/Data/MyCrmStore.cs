using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Data;

/// <summary>
/// A change MyCRMRecords will not make in MyCRM (writing is off, a person MyCRM does not know, a label MyCRM does not
/// have); its message says why, for the page.
/// </summary>
public sealed class MyCrmRefusedException(string message) : InvalidOperationException(message);

/// <summary>
/// MyCRM's Users, Territory and Company records, read and written where MyCRM keeps them: its tables in the OutSystems
/// database (<see cref="MyCrmMap"/> says where each property is). The record's Id is MyCRM's. MyCRMRecords keeps no copy:
/// both apps see the same rows at once.
/// <list type="bullet">
/// <item>Writing waits for an administrator's switch (<see cref="CanWrite"/>, off at first); until then every change is
/// refused with <see cref="WritesOff"/>.</item>
/// <item>Nothing is ever deleted in MyCRM: there is no delete. What is taken away is marked, as the pages always did.</item>
/// <item>Every value written is recorded with the value it replaced (dbo.CrChange), with who made the change
/// (<see cref="As"/>). A batch is one transaction in MyCRM; its records follow once it is committed.</item>
/// <item>Only the columns whose value changed are written, and a value that reads back the same (a person kept twice in
/// OutSystems, text with spaces at its ends) is no change.</item>
/// <item>Of the territories, only the permanent ones are read (the temporary ones are MyCRM's Projects and CSST parts'),
/// and of their company links only the active ones of active permanent territories: the millions of other rows are never
/// read.</item>
/// <item>What MyCRM has no column for is kept here, by MyCRM's entity and Id (<see cref="RecordNote"/>).</item>
/// </list>
/// </summary>
public sealed class MyCrmStore : IRecordStore, ITemporaryTerritories
{
    public const string WritesOff =
        "MyCRMRecords only reads MyCRM's data for now: an administrator turns on \"Let MyCRMRecords change MyCRM's data\" (admin page, MyCRM tab).";

    private static readonly DateTime NoDate = new(1900, 1, 1);
    private static readonly AsyncLocal<string?> Author = new();

    /// <summary>Who the changes made until the result is disposed are recorded as made by (the person, or a scheduled task).</summary>
    public static IDisposable As(string by)
    {
        var before = Author.Value;
        Author.Value = by;
        return new Restore(() => Author.Value = before);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    /// <summary>What every store of one MyCRM shares: the tables' columns, the static entities' labels and the people.</summary>
    private sealed class Shared
    {
        public readonly object Gate = new();
        public readonly Dictionary<string, IReadOnlyDictionary<string, OldColumn>> Columns = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, (DateTime At, IReadOnlyDictionary<long, string> Labels)> Labels = new(StringComparer.OrdinalIgnoreCase);
        public (DateTime At, PeopleIndex People)? People;
    }

    /// <summary>A batch: one connection and transaction, and what to record once it is committed.</summary>
    private sealed class Session(DbConnection connection, DbTransaction transaction)
    {
        public DbConnection Connection { get; } = connection;
        public DbTransaction Transaction { get; } = transaction;
        public List<MyCrmChange> Changes { get; } = [];
        public Dictionary<string, RecordNote> Notes { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>OutSystems' users as MyCRMRecords' logins, and back.</summary>
    private sealed class PeopleIndex
    {
        public Dictionary<long, string> LoginOf { get; } = new();
        public Dictionary<string, List<long>> IdsOf { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly IRecordStore _own;
    private readonly ExternalDatabases _databases;
    private readonly Func<string> _module;
    private readonly Func<bool> _canWrite;
    private readonly Func<DateTime> _clock;
    private readonly string _domain;
    private readonly Shared _shared;
    private readonly Session? _session;

    /// <param name="own">MyCRMRecords' own records: the notes, the record of changes, the people (for their email addresses).</param>
    /// <param name="module">MyCRM's OutSystems module (Connections tab).</param>
    /// <param name="canWrite">The administrator's switch.</param>
    public MyCrmStore(IRecordStore own, ExternalDatabases databases, Func<string> module, Func<bool> canWrite, Func<DateTime> clock, string domain = "RSP")
    {
        _own = own;
        _databases = databases;
        _module = module;
        _canWrite = canWrite;
        _clock = clock;
        _domain = domain;
        _shared = new Shared();
    }

    private MyCrmStore(MyCrmStore root, Session session)
    {
        _own = root._own;
        _databases = root._databases;
        _module = root._module;
        _canWrite = root._canWrite;
        _clock = root._clock;
        _domain = root._domain;
        _shared = root._shared;
        _session = session;
    }

    /// <summary>MyCRM's tables are set up (Connections tab).</summary>
    public bool Available => _databases.IsConfigured(ExternalNames.OutSystems);

    /// <summary>MyCRMRecords may change MyCRM's data (the switch is on and MyCRM's tables are set up).</summary>
    public bool CanWrite => Available && _canWrite();

    private static CrmEntity EntityOf<T>() => MyCrmMap.Of<T>() ?? throw new InvalidOperationException($"{typeof(T).Name} is not one of MyCRM's records.");

    private TResult Use<TResult>(Func<DbConnection, DbTransaction?, TResult> work) =>
        _session is { } s ? work(s.Connection, s.Transaction) : _databases.Use(ExternalNames.OutSystems, c => work(c, null));

    private string Table(DbConnection connection, string entity) => _databases.EntityTable(connection, _module(), entity);

    private IReadOnlyDictionary<string, OldColumn> Columns(DbConnection connection, DbTransaction? transaction, string table)
    {
        lock (_shared.Gate)
            if (_shared.Columns.TryGetValue(table, out var known)) return known;
        var columns = OutSystemsTables.ColumnsOf(connection, transaction, table);
        lock (_shared.Gate) _shared.Columns[table] = columns;
        return columns;
    }

    private IReadOnlyDictionary<long, string> Labels(DbConnection connection, DbTransaction? transaction, string entity)
    {
        lock (_shared.Gate)
            if (_shared.Labels.TryGetValue(entity, out var known) && DateTime.UtcNow - known.At < TimeSpan.FromMinutes(10)) return known.Labels;
        var labels = Sql.Rows(connection, transaction, $"SELECT [ID], [LABEL] FROM {Table(connection, entity)}")
            .ToDictionary(r => OldValues.Long(r["ID"]), r => OutSystemsValues.Text(r["LABEL"]));
        lock (_shared.Gate) _shared.Labels[entity] = (DateTime.UtcNow, labels);
        return labels;
    }

    /// <summary>
    /// OutSystems' users as logins: a user is the login here that signs in with the user's email address when there is one
    /// (OutSystems often keeps someone as RSP\cara.tan@ricoh.sg who signs in as RSP\ctan), else the username as a login.
    /// Read again every two minutes.
    /// </summary>
    private PeopleIndex People(DbConnection connection, DbTransaction? transaction)
    {
        lock (_shared.Gate)
            if (_shared.People is { } known && DateTime.UtcNow - known.At < TimeSpan.FromMinutes(2)) return known.People;
        var byEmail = _own.Find<AppUser>(Query.Where("IsActive", true)).Where(u => u.Email.Trim().Length > 0)
            .GroupBy(u => u.Email.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(u => u.LastSeenAt ?? DateTime.MinValue).ThenBy(u => u.Id).First().Login, StringComparer.OrdinalIgnoreCase);
        var people = new PeopleIndex();
        foreach (var row in Sql.Rows(connection, transaction, "SELECT [ID], [USERNAME], [EMAIL] FROM dbo.OSSYS_USER ORDER BY [ID]"))
        {
            var id = OldValues.Long(row["ID"]);
            var email = OutSystemsValues.Text(row["EMAIL"]);
            var login = email.Length > 0 && byEmail.TryGetValue(email, out var signsIn) ? signsIn : OutSystemsImport.LoginOf(OutSystemsValues.Text(row["USERNAME"]), _domain);
            if (login.Length == 0) continue;
            people.LoginOf[id] = login;
            if (!people.IdsOf.TryGetValue(login, out var ids)) people.IdsOf[login] = ids = [];
            ids.Add(id);
        }
        lock (_shared.Gate) _shared.People = (DateTime.UtcNow, people);
        return people;
    }

    // ---------------------------------------------------------------- values

    /// <summary>What reading a MyCRM record needs: the people and the static entities' labels.</summary>
    private sealed class Reading(MyCrmStore store, DbConnection connection, DbTransaction? transaction)
    {
        private PeopleIndex? _people;
        public PeopleIndex People => _people ??= store.People(connection, transaction);
        public IReadOnlyDictionary<long, string> Labels(string entity) => store.Labels(connection, transaction, entity);
    }

    private static object? FromDb(CrmField f, object? v, Reading r)
    {
        switch (f.Kind)
        {
            case CrmFieldKind.Day:
                return OutSystemsValues.Date(v)?.Date;
            case CrmFieldKind.Static:
                var label = r.Labels(f.List!.OldEntity).TryGetValue(OldValues.Long(v), out var l) ? l : "";
                var code = f.List.FromLabel(label);
                return code.Length == 0 ? f.StaticDefault : code;
            case CrmFieldKind.User:
                return r.People.LoginOf.TryGetValue(OldValues.Long(v), out var login) ? login : "";
            case CrmFieldKind.Ref:
                var id = OldValues.Long(v);
                return f.IsNullable && id == 0 ? null : id;
            default:
                return Plain(f, v);
        }
    }

    private static object? Plain(CrmField f, object? v)
    {
        var missing = v is null or DBNull;
        if (f.Type == typeof(string))
        {
            var text = OutSystemsValues.Text(v);
            return f.Length > 0 && text.Length > f.Length ? text.Substring(0, f.Length) : text;
        }
        if (f.Type == typeof(DateTime)) return OutSystemsValues.Date(v);
        if (missing) return f.IsNullable ? null : Activator.CreateInstance(f.Type);
        if (f.Type == typeof(bool)) return Convert.ToBoolean(v, CultureInfo.InvariantCulture);
        if (f.Type == typeof(int)) return Convert.ToInt32(Convert.ToDecimal(v, CultureInfo.InvariantCulture));
        if (f.Type == typeof(long)) return Convert.ToInt64(v, CultureInfo.InvariantCulture);
        if (f.Type == typeof(decimal)) return Convert.ToDecimal(v, CultureInfo.InvariantCulture);
        return Convert.ChangeType(v, f.Type, CultureInfo.InvariantCulture);
    }

    /// <summary>The value a column holds when it has none (OutSystems: empty text, 0, no, 1900-01-01) or NULL where it takes NULL.</summary>
    private static object Blank(OldColumn column) => column.Nullable ? DBNull.Value : Default(column.SqlType);

    private static object Default(string sqlType) => sqlType.ToLowerInvariant() switch
    {
        "nvarchar" or "varchar" or "nchar" or "char" or "ntext" or "text" => "",
        "bit" => false,
        "date" or "datetime" or "datetime2" or "smalldatetime" => NoDate,
        _ => 0,
    };

    /// <summary>MyCRM's Id of a static record for a code (by the code's labels), or why there is none.</summary>
    private static long StaticId(CrmField f, string code, Reading r)
    {
        var choice = f.List!.All.FirstOrDefault(c => c.Code == code) ?? throw new MyCrmRefusedException($"\"{code}\" is not one of MyCRM's {f.List.OldEntity} choices.");
        var wanted = choice.Labels.Select(ChoiceList.Key).Where(k => k.Length > 0).ToList();
        foreach (var pair in r.Labels(f.List.OldEntity))
            if (wanted.Contains(ChoiceList.Key(pair.Value))) return pair.Key;
        throw new MyCrmRefusedException($"MyCRM's {f.List.OldEntity} has no \"{choice.Label}\": add it in MyCRM first.");
    }

    /// <summary>The OutSystems user MyCRM keeps for a login (the first when OutSystems has several), or null.</summary>
    private static long? UserId(string login, Reading r) =>
        login.Length > 0 && r.People.IdsOf.TryGetValue(login, out var ids) && ids.Count > 0 ? ids.Min() : null;

    private static object? ToDb(CrmField f, object? value, OldColumn column, Reading r)
    {
        switch (f.Kind)
        {
            case CrmFieldKind.Day:
                return value is DateTime day ? day.Date : Blank(column);
            case CrmFieldKind.Static:
                return value is string code && code.Length > 0 ? StaticId(f, code, r) : Blank(column);
            case CrmFieldKind.User:
                var login = ((string?)value ?? "").Trim();
                if (login.Length == 0) return Blank(column);
                if (UserId(login, r) is { } user) return user;
                if (f.Required) throw new MyCrmRefusedException($"{login} has no OutSystems user, so MyCRM cannot record them.");
                return Blank(column);
            case CrmFieldKind.Ref:
                var id = value is null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return id == 0 ? Blank(column) : id;
            default:
                if (value is null) return Blank(column);
                if (value is string text)
                    return column.Length > 0 && text.Length > column.Length ? text.Substring(0, column.Length) : text;
                return value;
        }
    }

    private static bool Same(object? a, object? b)
    {
        if (a is string || b is string) return string.Equals(((string?)a ?? "").Trim(), ((string?)b ?? "").Trim(), StringComparison.Ordinal);
        if (a is DateTime da && b is DateTime db) return Math.Abs((da - db).TotalMilliseconds) < 4;
        return Equals(a, b);
    }

    private static string Describe(object? value) => value switch
    {
        null or DBNull => "",
        DateTime d => d <= NoDate ? "" : d.ToString(d.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        _ => OutSystemsValues.Text(value),
    };

    private static bool IsBlank(object? value) => value is null or DBNull || value is string { Length: 0 } || (value is DateTime d && d <= NoDate);

    // ---------------------------------------------------------------- notes

    private static string NoteKey(string entity, long id) => entity + "|" + id.ToString(CultureInfo.InvariantCulture);

    private static readonly Dictionary<string, PropertyInfo> NoteProperties =
        typeof(RecordNote).GetProperties().ToDictionary(p => p.Name, StringComparer.Ordinal);

    private static object? NoteValue(RecordNote note, CrmField f) =>
        f.Name == "IsActive" ? !note.Inactive : NoteProperties[f.Name].GetValue(note);

    private static void SetNoteValue(RecordNote note, CrmField f, object? value)
    {
        if (f.Name == "IsActive") note.Inactive = value is false;
        else NoteProperties[f.Name].SetValue(note, value);
    }

    /// <summary>The notes of these records (with those of the batch not yet saved).</summary>
    private Dictionary<long, RecordNote> Notes(CrmEntity e, IReadOnlyCollection<long> ids)
    {
        var notes = new Dictionary<long, RecordNote>();
        if (e.Side.Count == 0 || ids.Count == 0) return notes;
        foreach (var chunk in ids.Distinct().Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => (object)x.v))
            foreach (var note in _own.Find<RecordNote>(Query.Where("Entity", e.Entity).In("RecordId", chunk)).OrderBy(n => n.Id))
                if (!notes.ContainsKey(note.RecordId)) notes[note.RecordId] = note;
        if (_session is { } s)
            foreach (var id in ids)
                if (s.Notes.TryGetValue(NoteKey(e.Entity, id), out var pending)) notes[id] = pending;
        return notes;
    }

    /// <summary>Keeps (for after the batch) what the record says that MyCRM has no column for.</summary>
    private void Note(Session s, CrmEntity e, IRecord record, bool isNew)
    {
        if (e.Side.Count == 0) return;
        var key = NoteKey(e.Entity, record.Id);
        var note = s.Notes.TryGetValue(key, out var pending) ? pending
            : isNew ? null : _own.Find<RecordNote>(Query.Where("Entity", e.Entity).And("RecordId", record.Id)).OrderBy(n => n.Id).FirstOrDefault();
        var blank = new RecordNote();
        var wanted = note is null ? new RecordNote { Entity = e.Entity, RecordId = record.Id } : CopyOf(note);
        foreach (var f in e.Side) SetNoteValue(wanted, f, f.Get(record));
        var empty = e.Side.All(f => Same(NoteValue(wanted, f), NoteValue(blank, f)));
        if (note is null && empty) return;
        if (note is not null && e.Side.All(f => Same(NoteValue(wanted, f), NoteValue(note, f)))) return;
        s.Notes[key] = wanted;
    }

    private static RecordNote CopyOf(RecordNote note)
    {
        var copy = new RecordNote();
        foreach (var p in NoteProperties.Values) p.SetValue(copy, p.GetValue(note));
        return copy;
    }

    // ---------------------------------------------------------------- reading

    /// <summary>The rows MyCRMRecords reads of the entity (in SQL, its table aliased x).</summary>
    private string Rows(CrmEntity e, DbConnection connection)
    {
        switch (e.Rows)
        {
            case CrmRows.PermanentTerritories:
                var noted = _own.Find<RecordNote>(Query.Where("Entity", e.Entity)).Select(n => n.RecordId).ToList();
                if (_session is { } s) noted.AddRange(s.Notes.Values.Where(n => n.Entity == e.Entity).Select(n => n.RecordId));
                var ids = noted.Distinct().Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
                return "x.[ISTEMPORARY] = 0 AND (x.[ISACTIVE] = 1" + (ids.Count == 0 ? "" : " OR x.[ID] IN (" + string.Join(", ", ids) + ")") + ")";
            case CrmRows.ActiveLinksOfPermanentTerritories:
                return $"x.[ISACTIVE] = 1 AND x.[TERRITORYID] IN (SELECT t.[ID] FROM {Table(connection, MyCrmMap.Territories)} t WHERE t.[ISTEMPORARY] = 0 AND t.[ISACTIVE] = 1)";
            default:
                return "1 = 1";
        }
    }

    /// <summary>A query in SQL: the conditions MyCRM's columns can answer, and whether the rest is done in memory.</summary>
    private sealed class Translation
    {
        public List<string> Where { get; } = [];
        public bool InMemory { get; set; }
        public string? OrderBy { get; set; }
    }

    private Translation Translate(CrmEntity e, Query query, DbCommand command, Reading r)
    {
        var t = new Translation();
        var n = 0;
        string P(object value)
        {
            var name = "@q" + (n++).ToString(CultureInfo.InvariantCulture);
            Sql.Add(command, name, value);
            return name;
        }
        // An equality in SQL, or null when the field cannot be compared in SQL.
        string? Equal(CrmField? f, object? value)
        {
            if (f is null) return value is null ? "1 = 0" : "x.[ID] = " + P(Convert.ToInt64(value, CultureInfo.InvariantCulture));
            var column = "x.[" + f.Column + "]";
            switch (f.Kind)
            {
                case CrmFieldKind.Side:
                    return null;
                case CrmFieldKind.Static:
                    if (value is not string code || code.Length == 0) return $"({column} IS NULL OR {column} = 0)";
                    try { return column + " = " + StaticId(f, code, r).ToString(CultureInfo.InvariantCulture); }
                    catch (MyCrmRefusedException) { return "1 = 0"; }
                case CrmFieldKind.User:
                    var login = ((string?)value ?? "").Trim();
                    if (login.Length == 0) return $"({column} IS NULL OR {column} = 0)";
                    return r.People.IdsOf.TryGetValue(login, out var ids) && ids.Count > 0
                        ? column + " IN (" + string.Join(", ", ids.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")" : "1 = 0";
                case CrmFieldKind.Ref:
                    var id = value is null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
                    return id == 0 ? $"({column} IS NULL OR {column} = 0)" : column + " = " + id.ToString(CultureInfo.InvariantCulture);
                default:
                    if (IsBlank(value) || value is false || (value is not null && f.Type != typeof(string) && f.Type != typeof(DateTime) && f.Type != typeof(bool)
                            && Convert.ToDecimal(value, CultureInfo.InvariantCulture) == 0))
                        return f.Type == typeof(string) ? $"({column} IS NULL OR {column} = N'')"
                            : f.Type == typeof(DateTime) ? $"({column} IS NULL OR {column} <= '1900-01-01')"
                            : $"({column} IS NULL OR {column} = 0)";
                    var v = f.Kind == CrmFieldKind.Day && value is DateTime d ? d.Date : value!;
                    return column + " = " + P(v);
            }
        }
        foreach (var condition in query.Conditions)
        {
            var f = e.Field(condition.Column);
            string? sql;
            switch (condition.Op)
            {
                case Op.Equal:
                    sql = Equal(f, condition.Value);
                    break;
                case Op.NotEqual:
                    sql = Equal(f, condition.Value) is { } equal ? $"(CASE WHEN {equal} THEN 1 ELSE 0 END) = 0" : null;
                    break;
                case Op.In:
                    var values = ((IEnumerable<object>)condition.Value!).ToList();
                    var listable = f is null || f.Kind is CrmFieldKind.Ref
                        || (f.Kind is CrmFieldKind.Plain && (f.Type == typeof(string) || f.Type == typeof(long) || f.Type == typeof(int)));
                    if (values.Count == 0) sql = "1 = 0";
                    else if (listable)
                    {
                        // One IN list; a long one is written into the statement (a list of parameters has a limit), and an
                        // empty value also finds NULL (OutSystems keeps "none" as NULL, 0 or '').
                        var column = f is null ? "x.[ID]" : "x.[" + f.Column + "]";
                        var text = f is not null && f.Type == typeof(string);
                        bool Empty(object? v) => v is null || (text ? ((string)v).Trim().Length == 0 : Convert.ToInt64(v, CultureInfo.InvariantCulture) == 0);
                        var listed = values.Where(v => !Empty(v)).Select(v => text ? (object)(string)v : Convert.ToInt64(v, CultureInfo.InvariantCulture)).Distinct().ToList();
                        var options = new List<string>();
                        if (listed.Count > 0)
                            options.Add(column + " IN (" + string.Join(", ", listed.Select(v =>
                                listed.Count > 500 && Sql.Literal(v, text ? typeof(string) : typeof(long)) is { } literal ? literal : P(v))) + ")");
                        if (values.Any(Empty)) options.Add(Equal(f, text ? "" : 0L)!);
                        sql = "(" + string.Join(" OR ", options) + ")";
                    }
                    else
                    {
                        var parts = values.Select(v => Equal(f, v)).ToList();
                        sql = parts.Any(p => p is null) ? null : "(" + string.Join(" OR ", parts) + ")";
                    }
                    break;
                case Op.AtLeast or Op.Before when f is null || f.Kind is CrmFieldKind.Plain or CrmFieldKind.Day or CrmFieldKind.Ref:
                    var columnName = f is null ? "x.[ID]" : "x.[" + f.Column + "]";
                    sql = columnName + (condition.Op == Op.AtLeast ? " >= " : " < ") + P(condition.Value!);
                    break;
                case Op.Contains when f is { Kind: CrmFieldKind.Plain } && f.Type == typeof(string):
                    sql = $"x.[{f.Column}] LIKE {P("%" + Sql.EscapeLike((string)condition.Value!) + "%")} ESCAPE '\\'";
                    break;
                default:
                    sql = null;
                    break;
            }
            if (sql is null) t.InMemory = true;
            else t.Where.Add(sql);
        }
        if (query.SearchText is { } text && query.SearchColumns.Length > 0)
        {
            var fields = query.SearchColumns.Select(e.Field).ToList();
            if (fields.All(f => f is { Kind: CrmFieldKind.Plain } && f.Type == typeof(string)))
            {
                var pattern = P("%" + Sql.EscapeLike(text) + "%");
                t.Where.Add("(" + string.Join(" OR ", fields.Select(f => $"x.[{f!.Column}] LIKE {pattern} ESCAPE '\\'")) + ")");
            }
            else t.InMemory = true;
        }
        var order = e.Field(query.OrderBy);
        if (order is null) t.OrderBy = "x.[ID]";
        else if (order.Kind is CrmFieldKind.Plain or CrmFieldKind.Day or CrmFieldKind.Ref) t.OrderBy = "x.[" + order.Column + "]";
        else t.InMemory = true;
        return t;
    }

    private List<T> Select<T>(CrmEntity e, Query query, DbConnection connection, DbTransaction? transaction) where T : class, IRecord, new()
    {
        var r = new Reading(this, connection, transaction);
        var table = Table(connection, e.Entity);
        using var command = Sql.Command(connection, transaction, "");
        var t = Translate(e, query, command, r);
        var where = new[] { Rows(e, connection) }.Concat(t.Where);
        var direction = query.Descending ? " DESC" : "";
        var sql = new StringBuilder($"SELECT x.* FROM {table} x WHERE {string.Join(" AND ", where)}");
        if (!t.InMemory)
        {
            sql.Append(" ORDER BY ").Append(t.OrderBy).Append(direction).Append(", x.[ID]").Append(direction);
            if (query.Skip > 0 || query.Take is not null)
            {
                sql.Append(" OFFSET ").Append(query.Skip.ToString(CultureInfo.InvariantCulture)).Append(" ROWS");
                if (query.Take is { } take) sql.Append(" FETCH NEXT ").Append(take.ToString(CultureInfo.InvariantCulture)).Append(" ROWS ONLY");
            }
        }
        command.CommandText = sql.ToString();
        var rows = new List<Dictionary<string, object?>>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
        var records = rows.Select(row =>
        {
            var record = new T { Id = OldValues.Long(row["ID"]) };
            foreach (var f in e.Stored)
                f.Set(record, FromDb(f, row.TryGetValue(f.Column, out var v) ? v : throw new InvalidOperationException($"MyCRM's {e.Entity} has no column {f.Column}."), r));
            return record;
        }).ToList();
        var notes = Notes(e, records.Select(x => x.Id).ToList());
        foreach (var record in records)
            if (notes.TryGetValue(record.Id, out var note))
                foreach (var f in e.Side) f.Set(record, NoteValue(note, f));
        return t.InMemory ? QueryInMemory.Apply(records, query).ToList() : records;
    }

    public T? Get<T>(long id) where T : class, IRecord, new() => Find<T>(Query.Where("Id", id)).FirstOrDefault();

    public List<T> Find<T>(Query? query = null) where T : class, IRecord, new()
    {
        var e = EntityOf<T>();
        return Use((c, t) => Select<T>(e, query ?? Query.All(), c, t));
    }

    public int Count<T>(Query? query = null) where T : class, IRecord, new()
    {
        var e = EntityOf<T>();
        query ??= Query.All();
        return Use((c, tx) =>
        {
            using var command = Sql.Command(c, tx, "");
            var t = Translate(e, query, command, new Reading(this, c, tx));
            if (t.InMemory) return Select<T>(e, query.Unpaged(), c, tx).Count;
            command.CommandText = $"SELECT COUNT(*) FROM {Table(c, e.Entity)} x WHERE {string.Join(" AND ", new[] { Rows(e, c) }.Concat(t.Where))}";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        });
    }

    // ---------------------------------------------------------------- writing

    private void RequireWrites()
    {
        if (!_canWrite()) throw new MyCrmRefusedException(WritesOff);
    }

    private TResult InSession<TResult>(Func<Session, TResult> work) =>
        _session is { } s ? work(s) : Batch(store => ((MyCrmStore)store).InSession(work));

    private OldColumn ColumnOf(CrmEntity e, IReadOnlyDictionary<string, OldColumn> columns, string column) =>
        columns.TryGetValue(column, out var c) ? c : throw new InvalidOperationException($"MyCRM's {e.Entity} has no column {column}.");

    private MyCrmChange Change(CrmEntity e, long id, string column, object? before, object? after) => new()
    {
        TableName = e.Entity, RowId = id, Column = column, Before = Describe(before), After = Describe(after),
    };

    public T Insert<T>(T record) where T : class, IRecord, new()
    {
        var e = EntityOf<T>();
        RequireWrites();
        return InSession(s =>
        {
            var r = new Reading(this, s.Connection, s.Transaction);
            var table = Table(s.Connection, e.Entity);
            var columns = Columns(s.Connection, s.Transaction, table);
            var values = e.Stored.Select(f => (f.Column, Value: ToDb(f, f.Get(record), ColumnOf(e, columns, f.Column), r)))
                .Concat(e.FixedOnInsert.Select(p => (Column: p.Key, p.Value))).ToList();
            using var command = Sql.Command(s.Connection, s.Transaction,
                $"INSERT INTO {table} ({string.Join(", ", values.Select(v => "[" + v.Column + "]"))}) OUTPUT INSERTED.[ID] " +
                $"VALUES ({string.Join(", ", values.Select((_, i) => "@v" + i.ToString(CultureInfo.InvariantCulture)))})");
            for (var i = 0; i < values.Count; i++) Sql.Add(command, "@v" + i.ToString(CultureInfo.InvariantCulture), values[i].Value);
            record.Id = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            foreach (var (column, value) in values)
                if (!IsBlank(value)) s.Changes.Add(Change(e, record.Id, column, null, value));
            Note(s, e, record, isNew: true);
            return record;
        });
    }

    public void Update<T>(T record) where T : class, IRecord, new()
    {
        var e = EntityOf<T>();
        RequireWrites();
        InSession(s =>
        {
            var r = new Reading(this, s.Connection, s.Transaction);
            var table = Table(s.Connection, e.Entity);
            var columns = Columns(s.Connection, s.Transaction, table);
            var rows = Sql.Rows(s.Connection, s.Transaction, $"SELECT * FROM {table} WITH (UPDLOCK) WHERE [ID] = @id", ("@id", record.Id));
            if (rows.Count == 0) throw new InvalidOperationException($"MyCRM has no {e.Entity} {record.Id.ToString(CultureInfo.InvariantCulture)}.");
            var row = rows[0];
            var set = new List<(string Column, object? Value)>();
            foreach (var f in e.Stored)
            {
                var was = row.TryGetValue(f.Column, out var v) ? v : null;
                if (Same(FromDb(f, was, r), f.Get(record))) continue;
                var value = ToDb(f, f.Get(record), ColumnOf(e, columns, f.Column), r);
                set.Add((f.Column, value));
                s.Changes.Add(Change(e, record.Id, f.Column, was, value));
            }
            if (set.Count > 0)
            {
                using var command = Sql.Command(s.Connection, s.Transaction,
                    $"UPDATE {table} SET {string.Join(", ", set.Select((c, i) => "[" + c.Column + "] = @v" + i.ToString(CultureInfo.InvariantCulture)))} WHERE [ID] = @id");
                for (var i = 0; i < set.Count; i++) Sql.Add(command, "@v" + i.ToString(CultureInfo.InvariantCulture), set[i].Value);
                Sql.Add(command, "@id", record.Id);
                command.ExecuteNonQuery();
            }
            Note(s, e, record, isNew: false);
            return 0;
        });
    }

    /// <summary>Nothing is ever deleted in MyCRM: what is taken away is marked.</summary>
    public void Delete<T>(long id) where T : class, IRecord, new() => throw new NotSupportedException("Nothing is deleted in MyCRM: mark it instead.");

    public int Delete<T>(Query query) where T : class, IRecord, new() => throw new NotSupportedException("Nothing is deleted in MyCRM: mark it instead.");

    public void InsertKeepingIds<T>(IReadOnlyList<T> records) where T : class, IRecord, new() =>
        throw new NotSupportedException("MyCRM gives its records their numbers.");

    /// <summary>
    /// Runs the work as one transaction in MyCRM (its reads see its own changes); once committed, the record of the changes
    /// and the notes are saved here. MyCRM's tables are found before the transaction begins (finding one reads the platform's
    /// tables, which a command inside the transaction may not).
    /// </summary>
    public TResult Batch<TResult>(Func<IRecordStore, TResult> work)
    {
        if (_session is not null) return work(this);
        using var connection = _databases.Open(ExternalNames.OutSystems);
        foreach (var e in MyCrmMap.All) Table(connection, e.Entity);
        foreach (var l in MyCrmMap.StaticLists) Table(connection, l.OldEntity);
        var session = new Session(connection, connection.BeginTransaction());
        TResult result;
        try
        {
            result = work(new MyCrmStore(this, session));
            session.Transaction.Commit();
        }
        catch
        {
            try { session.Transaction.Rollback(); }
            catch (Exception ex) when (ex is DbException or InvalidOperationException) { }
            throw;
        }
        finally
        {
            session.Transaction.Dispose();
        }
        if (session.Changes.Count > 0 || session.Notes.Count > 0)
        {
            var now = _clock();
            var by = Strings.Shorten(Author.Value ?? "MyCRMRecords", 600);
            _own.Batch(own =>
            {
                foreach (var note in session.Notes.Values)
                {
                    if (note.Id == 0) own.Insert(note);
                    else own.Update(note);
                }
                foreach (var change in session.Changes)
                {
                    change.ChangedOn = now;
                    change.ChangedBy = by;
                    own.Insert(change);
                }
            });
        }
        return result;
    }

    // ---------------------------------------------------------------- MyCRM's temporary territories

    /// <summary>MyCRM's Ids of the active companies of the active temporary territories of these people (by login).</summary>
    public IReadOnlyCollection<long> CompanyIds(IReadOnlyCollection<string> logins) => Use((connection, transaction) =>
    {
        var people = People(connection, transaction);
        var ids = logins.SelectMany(l => people.IdsOf.TryGetValue(l, out var i) ? i : (IEnumerable<long>)Array.Empty<long>()).Distinct().ToList();
        var companies = new HashSet<long>();
        if (ids.Count == 0) return (IReadOnlyCollection<long>)companies;
        var links = Table(connection, MyCrmMap.CompanyTerritories);
        var territories = Table(connection, MyCrmMap.Territories);
        foreach (var chunk in ids.Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => x.v))
            foreach (var row in Sql.Rows(connection, transaction,
                         $"SELECT DISTINCT ct.[COMPANYID] FROM {links} ct JOIN {territories} t ON t.[ID] = ct.[TERRITORYID] " +
                         $"WHERE ct.[ISACTIVE] = 1 AND t.[ISACTIVE] = 1 AND t.[ISTEMPORARY] = 1 AND t.[OWNERID] IN ({string.Join(", ", chunk.Select(v => v.ToString(CultureInfo.InvariantCulture)))})"))
                companies.Add(OldValues.Long(row["COMPANYID"]));
        return (IReadOnlyCollection<long>)companies;
    });
}

/// <summary>
/// MyCRMRecords' records as the pages see them: MyCRM's (<see cref="MyCrmMap"/>) in MyCRM, the others in MyCRMRecords' own
/// storage. A batch is a batch of both; MyCRM's part is committed first.
/// </summary>
public sealed class RoutedStore(IRecordStore inner, IRecordStore crm) : IRecordStore
{
    /// <summary>MyCRMRecords' own records.</summary>
    public IRecordStore Own => inner;

    /// <summary>MyCRM's records.</summary>
    public IRecordStore Crm => crm;

    private static bool IsCrm<T>() => MyCrmMap.Of<T>() is not null;

    public T? Get<T>(long id) where T : class, IRecord, new() => IsCrm<T>() ? crm.Get<T>(id) : inner.Get<T>(id);
    public List<T> Find<T>(Query? query = null) where T : class, IRecord, new() => IsCrm<T>() ? crm.Find<T>(query) : inner.Find<T>(query);
    public int Count<T>(Query? query = null) where T : class, IRecord, new() => IsCrm<T>() ? crm.Count<T>(query) : inner.Count<T>(query);
    public T Insert<T>(T record) where T : class, IRecord, new() => IsCrm<T>() ? crm.Insert(record) : inner.Insert(record);
    public void Update<T>(T record) where T : class, IRecord, new()
    {
        if (IsCrm<T>()) crm.Update(record);
        else inner.Update(record);
    }
    // Only MyCRMRecords' own records can be deleted (by the stores' own work, never by the pages); nothing in MyCRM.
    public void Delete<T>(long id) where T : class, IRecord, new()
    {
        if (IsCrm<T>()) throw new NotSupportedException("Nothing is deleted in MyCRM: mark it instead.");
        DeleteOwn<T>(id);
    }

    public int Delete<T>(Query query) where T : class, IRecord, new()
    {
        if (IsCrm<T>()) throw new NotSupportedException("Nothing is deleted in MyCRM: mark it instead.");
        return DeleteOwn<T>(query);
    }

    private void DeleteOwn<T>(long id) where T : class, IRecord, new() => inner.Delete<T>(id);
    private int DeleteOwn<T>(Query query) where T : class, IRecord, new() => inner.Delete<T>(query);
    public void InsertKeepingIds<T>(IReadOnlyList<T> records) where T : class, IRecord, new()
    {
        if (IsCrm<T>()) crm.InsertKeepingIds(records);
        else inner.InsertKeepingIds(records);
    }

    public TResult Batch<TResult>(Func<IRecordStore, TResult> work) =>
        ReferenceEquals(inner, crm)
            ? inner.Batch(s => work(new RoutedStore(s, s)))
            : crm.Batch(c => inner.Batch(s => work(new RoutedStore(s, c))));
}
