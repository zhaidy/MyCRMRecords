using System.Globalization;

namespace MyCRMRecords.Tests;

/// <summary>
/// An old OutSystems app's tables in memory, for the sync's tests: rows with an identity ID, columns typed by their name
/// as OutSystems makes them, users and static entities. Like the real writer it can add and change rows, never delete
/// them; changes count only once committed, and <see cref="FailNextCommit"/> makes the next commit fail.
/// </summary>
public sealed class FakeOldTables : IOldTables
{
    private readonly Dictionary<string, Dictionary<string, OldColumn>> _schema = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Dictionary<string, object?>>> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _identity = new(StringComparer.OrdinalIgnoreCase);

    public string Title { get; init; } = "MyCRM";
    public List<OldUser> UserList { get; } = [];
    public Dictionary<string, Dictionary<long, string>> Static { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool FailNextCommit { get; set; }
    public int Commits { get; private set; }

    /// <summary>
    /// MyCRM's entities as OutSystems made them (Feedback, Task, FeedbackAttachment and the three lists), its static
    /// entities with database Ids unlike MyCRMRecords's (11, 12... so a mapping by Id fails; "Confirmed Statisfaction" spelt as
    /// MyCRM spells it), and its users. Time attributes are datetime columns holding 1900-01-01 and the time.
    /// </summary>
    public static FakeOldTables MyCRM()
    {
        var t = new FakeOldTables();
        t.Define("FeedbackCategory", "ID:id LABEL:50 ORDER:int ISACTIVE:bit");
        t.Define("FeedbackMedium", "ID:id LABEL:50 ORDER:int ISACTIVE:bit");
        t.Define("FeedbackDivision", "ID:id DIVISION:50 PERSONINCHARGE:user ISACTIVE:bit");
        t.Define("Feedback", "ID:id FEEDBACKGROUPID:ref PERSONRESPONSIBLE:user PERSONRESPONSIBLEDIVISION:50 FEEDBACKSTATUSID:ref COMPANYNAME:500 COMPANYADDRESS:5000 " +
            "POSTALCODE:50 COMPANYPHONE:20 COMPANYFAX:20 COMPANYEMAIL:250 CONTACTNAME:50 FEEDBACKMEDIUMID:ref DATERECEIVED:date TIMERECEIVED:datetime FEEDBACKTYPEID:ref " +
            "FEEDBACKCATEGORYID:ref FEEDBACKDESCRIPTION:max STAFFRESPONSIBLEFORPROBLEM:user CC:max ISACKNOWLEDGED:bit ACKNOWLEDGEDTYPEID:ref ACKNOWLEDGEDDATE:date " +
            "ACKNOWLEDGEDTIME:datetime SIGNOFF:user FIRSTLOOKATFEEDBACK:max ROOTCAUSE:max CORRECTIVEACTION:max STAFFRESPONSIBLEFORSOLUTION:user CUSTOMERSATISFACTIONLEVELID:ref " +
            "CREATEDON:datetime CREATEDBY:user CREATEDBYUSERNAME:500 UPDATEDON:datetime UPDATEDBY:user UPDATEDBYUSERNAME:500 ISACTIVE:bit SESSIONID:50 ACKNOWLEGEDHOURS:int " +
            "STARTRESOLUTIONDAYS:int CLOSECASEDAYS:int STARTEDRESOLUTIONON:datetime COMPLETEDRESOLUTIONON:datetime ISCOMPLETED:bit CREATORHASSOLUTION:bit " +
            "ESS_CREATORSOLUTIONDETAIL:max ESS_EXPECTEDOUTCOME:max ESS_SUGGESTIONMEETCRITERIA:500 ESS_RICOHWAYVALUE:500 HRACCEPTED:50 HRREMARKS:max HRUPDATEDON:datetime " +
            "HRUPDATEDBY:user PREVENTIVEACTION:max MIGHTCAUSECUSTOMERCOMPLAINT:bit FEEDBACKDIVISIONID:ref");
        t.Define("Task", "ID:id FEEDBACKID:ref ACTIONPLAN:max REMARKS:5000 PERSONINCHARGE:user DUEDATE:date SLAEXPECTEDCOMPLETIONDATE:date ISCOMPLETED:bit CREATEDON:datetime " +
            "CREATEDBY:user SESSIONID:50 TASKTYPEID:ref");
        t.Define("FeedbackAttachment", "ID:id FEEDBACKID:ref FILENAME:500 FILEBINARY:binary TYPE:50 UPLOADEDBY:user UPLOADEDON:datetime SESSIONID:50");
        t.Static["FeedbackGroup"] = new() { [11] = "Customer Complaint", [12] = "Potential Customer Complaint", [13] = "Internal Challenges", [14] = "Area of Improvement" };
        t.Static["FeedbackStatus"] = new()
        {
            [21] = "Pending HR", [22] = "New", [23] = "Acknowledged", [24] = "Investigation / Resolution", [25] = "Confirmed Statisfaction", [26] = "Closed",
        };
        t.Static["FeedbackType"] = new() { [31] = "Very Serious Complaint", [32] = "Serious Complaint", [33] = "Normal Complaint", [34] = "Feedback / Improvement" };
        t.Static["AcknowledgedType"] = new()
        {
            [41] = "Email", [42] = "Face To Face", [43] = "Fax", [44] = "Internet", [45] = "Phone", [46] = "Postal Mail", [47] = "Text Message",
        };
        t.Static["CustomerSatisfactionLevel"] = new()
        {
            [51] = "No Acceptance (e.g. Abituary / Legal)", [52] = "Customer Accepted Solution", [53] = "Customer Accepted Solution and Expressed Appreciation",
        };
        t.Static["TaskType"] = new() { [61] = "Corrective Action", [62] = "Preventive Action" };
        t.UserList.AddRange([
            new OldUser(101, @"RSP\sam", "Sam Staff", "sam@example.com"), new OldUser(102, @"RSP\fay", "Fay Admin", "fay@example.com"),
            new OldUser(103, @"RSP\hana", "Hana Hr", "hana@example.com"), new OldUser(104, @"RSP\ben", "Ben Be", "ben@example.com"),
            new OldUser(105, @"RSP\olga", "Olga Other", "olga@example.com"), new OldUser(1, @"RSP\admin", "Administrator", "admin@example.com"),
        ]);
        return t;
    }

    /// <summary>
    /// A table with the given columns, "NAME:type" with type id (identity), ref (a reference: bigint, NULL allowed), user (a
    /// user: bigint, NULL allowed), int, long, money (decimal), bit, datetime, date, binary (a file), max (text without limit) or a
    /// number (text of that length).
    /// </summary>
    public void Define(string table, string columns)
    {
        var schema = new Dictionary<string, OldColumn>(StringComparer.OrdinalIgnoreCase);
        const bool nullable = false;
        foreach (var spec in columns.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = spec.Substring(0, spec.IndexOf(':'));
            var type = spec.Substring(spec.IndexOf(':') + 1);
            schema[name] = type switch
            {
                "id" => new OldColumn(name, "bigint", -1, false, true),
                "ref" or "user" => new OldColumn(name, "bigint", -1, true, false),
                "int" => new OldColumn(name, "int", -1, nullable, false),
                "long" => new OldColumn(name, "bigint", -1, nullable, false),
                "money" => new OldColumn(name, "decimal", -1, nullable, false),
                "bit" => new OldColumn(name, "bit", -1, nullable, false),
                "datetime" => new OldColumn(name, "datetime", -1, nullable, false),
                "date" => new OldColumn(name, "date", -1, nullable, false),
                "binary" => new OldColumn(name, "varbinary", -1, true, false),
                "max" => new OldColumn(name, "nvarchar", -1, nullable, false),
                _ => new OldColumn(name, "nvarchar", int.Parse(type, CultureInfo.InvariantCulture), nullable, false),
            };
        }
        _schema[table] = schema;
        _rows[table] = [];
        _identity[table] = 1000;
    }

    /// <summary>Adds a row as MyCRM would (missing columns get their defaults); returns its ID.</summary>
    public long Add(string table, Dictionary<string, object?> values) => AddTo(_rows, _identity, table, values);

    public void Change(string table, long id, Dictionary<string, object?> values)
    {
        var row = _rows[table].Single(r => (long)r["ID"]! == id);
        foreach (var pair in values) row[pair.Key] = pair.Value;
    }

    public void DeleteRow(string table, long id) => _rows[table].RemoveAll(r => (long)r["ID"]! == id);

    /// <summary>Deletes rows as the old app itself would (the sync never does).</summary>
    public void DeleteRows(string table, Func<Dictionary<string, object?>, bool> which) => _rows[table].RemoveAll(r => which(r));

    public Dictionary<string, object?> Row(string table, long id) => _rows[table].Single(r => (long)r["ID"]! == id);

    public IReadOnlyList<Dictionary<string, object?>> All(string table) => _rows[table];

    private long AddTo(Dictionary<string, List<Dictionary<string, object?>>> rows, Dictionary<string, long> identity, string table, IReadOnlyDictionary<string, object?> values)
    {
        var id = ++identity[table];
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in _schema[table].Values)
            row[column.Name] = column.Identity ? id : column.Nullable ? null : column.SqlType switch
            {
                "datetime" or "date" => OldValues.NoDate,
                "bit" => false,
                "int" => 0,
                "bigint" => 0L,
                "decimal" => 0m,
                _ => "",
            };
        foreach (var pair in values)
        {
            if (!_schema[table].ContainsKey(pair.Key)) throw new InvalidOperationException($"{table} has no column {pair.Key}.");
            row[pair.Key] = pair.Value;
        }
        rows[table].Add(row);
        return _schema[table].Values.Any(c => c.Identity) ? id : 0;
    }

    public IReadOnlyList<Dictionary<string, object?>> Rows(string table, string? sinceColumn = null, DateTime? since = null) =>
        _rows[table].Where(r => sinceColumn is null || since is null || r[sinceColumn] is DateTime d && d >= since)
            .Select(r => new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<Dictionary<string, object?>> Rows(string table, string column, IReadOnlyCollection<long> values) =>
        _rows[table].Where(r => r[column] is not null && values.Contains(Convert.ToInt64(r[column], CultureInfo.InvariantCulture)))
            .Select(r => new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase)).ToList();

    public Dictionary<string, object?>? Find(string table, IReadOnlyDictionary<string, object?> key) =>
        _rows[table].Where(r => key.All(k => OldValues.Canonical(r[k.Key]) == OldValues.Canonical(k.Value)))
            .Select(r => new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase)).SingleOrDefault();

    public IReadOnlyDictionary<string, OldColumn> Columns(string table) => _schema[table];
    public IReadOnlyDictionary<long, string> Labels(string table) => Static[table];
    public IReadOnlyList<OldUser> Users() => UserList;
    public IOldTablesWriter BeginWrite() => new Writer(this);

    private sealed class Writer(FakeOldTables tables) : IOldTablesWriter
    {
        private readonly Dictionary<string, List<Dictionary<string, object?>>> _rows =
            tables._rows.ToDictionary(p => p.Key, p => p.Value.Select(r => new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase)).ToList(), StringComparer.OrdinalIgnoreCase);
        // Like an identity, a number taken is not given again even when the transaction is undone.
        private readonly Dictionary<string, long> _identity = tables._identity;

        public long Insert(string table, IReadOnlyDictionary<string, object?> values) => tables.AddTo(_rows, _identity, table, values);

        public void Update(string table, IReadOnlyDictionary<string, object?> key, IReadOnlyDictionary<string, object?> values)
        {
            var matches = _rows[table].Where(r => key.All(k => OldValues.Canonical(r[k.Key]) == OldValues.Canonical(k.Value))).ToList();
            if (matches.Count > 1) throw new InvalidOperationException("More than one row.");
            foreach (var row in matches)
                foreach (var pair in values)
                {
                    if (!tables._schema[table].ContainsKey(pair.Key)) throw new InvalidOperationException($"{table} has no column {pair.Key}.");
                    row[pair.Key] = pair.Value;
                }
        }

        public void Commit()
        {
            if (tables.FailNextCommit)
            {
                tables.FailNextCommit = false;
                throw new InvalidOperationException("The OutSystems database went away.");
            }
            foreach (var pair in _rows) tables._rows[pair.Key] = pair.Value;
            tables.Commits++;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// The same tables in an OutSystems database (for the SQL Server tests): the platform's catalog (OSSYS_ESPACE,
    /// OSSYS_ENTITY), its users, each entity's OSUSR_ab1_ table, and the static entities with their records.
    /// </summary>
    public string OutSystemsSql(string module)
    {
        static string Q(string text) => "N'" + text.Replace("'", "''") + "'";
        var sql = new System.Text.StringBuilder();
        sql.AppendLine("CREATE TABLE dbo.OSSYS_ESPACE ([ID] int NOT NULL, [NAME] nvarchar(200) NOT NULL, [IS_ACTIVE] bit NOT NULL);");
        sql.AppendLine($"INSERT INTO dbo.OSSYS_ESPACE VALUES (1, {Q(module)}, 1);");
        sql.AppendLine("CREATE TABLE dbo.OSSYS_ENTITY ([ID] int IDENTITY(1,1) NOT NULL, [ESPACE_ID] int NOT NULL, [NAME] nvarchar(200) NOT NULL, [PHYSICAL_TABLE_NAME] nvarchar(200) NOT NULL, [IS_ACTIVE] bit NOT NULL);");
        sql.AppendLine("CREATE TABLE dbo.OSSYS_USER ([ID] int NOT NULL, [USERNAME] nvarchar(250) NOT NULL, [NAME] nvarchar(250) NOT NULL, [EMAIL] nvarchar(250) NOT NULL, [IS_ACTIVE] bit NOT NULL DEFAULT 1);");
        // MyCRM's users are "RSP\name@ricoh.sg" (and three "RSP\name"): the login is RSP\name.
        foreach (var user in UserList)
            sql.AppendLine($"INSERT INTO dbo.OSSYS_USER ([ID], [USERNAME], [NAME], [EMAIL]) VALUES ({user.Id}, {Q(user.Login + "@ricoh.sg")}, {Q(user.Name)}, {Q(user.Email.Replace("@example.com", "@ricoh.sg"))});");
        foreach (var pair in _schema)
        {
            var physical = "OSUSR_ab1_" + pair.Key.ToUpperInvariant();
            var columns = pair.Value.Values.Select(c => c.Identity ? $"[{c.Name}] bigint IDENTITY(1001,1) NOT NULL PRIMARY KEY"
                : $"[{c.Name}] " + (c.SqlType switch
                {
                    "bigint" => "bigint",
                    "datetime" => "datetime",
                    "date" => "date",
                    "bit" => "bit",
                    "int" => "int",
                    "decimal" => "decimal(37, 8)",
                    "varbinary" => "varbinary(max)",
                    _ => $"nvarchar({(c.Length < 0 || c.Length > 4000 ? "max" : c.Length.ToString(CultureInfo.InvariantCulture))})",
                }) + (c.Nullable ? " NULL" : c.SqlType switch
                {
                    "datetime" or "date" => " NOT NULL DEFAULT '1900-01-01'",
                    "bit" or "int" or "bigint" or "decimal" => " NOT NULL DEFAULT 0",
                    _ => " NOT NULL DEFAULT ''",
                }));
            sql.AppendLine($"CREATE TABLE dbo.[{physical}] ({string.Join(", ", columns)});");
            sql.AppendLine($"INSERT INTO dbo.OSSYS_ENTITY ([ESPACE_ID], [NAME], [PHYSICAL_TABLE_NAME], [IS_ACTIVE]) VALUES (1, {Q(pair.Key)}, {Q(physical)}, 1);");
        }
        foreach (var pair in Static)
        {
            var physical = "OSUSR_ab1_" + pair.Key.ToUpperInvariant();
            sql.AppendLine($"CREATE TABLE dbo.[{physical}] ([ID] int NOT NULL PRIMARY KEY, [LABEL] nvarchar(100) NOT NULL, [ORDER] int NOT NULL, [IS_ACTIVE] bit NOT NULL);");
            foreach (var record in pair.Value)
                sql.AppendLine($"INSERT INTO dbo.[{physical}] VALUES ({record.Key}, {Q(record.Value)}, {record.Key}, 1);");
            sql.AppendLine($"INSERT INTO dbo.OSSYS_ENTITY ([ESPACE_ID], [NAME], [PHYSICAL_TABLE_NAME], [IS_ACTIVE]) VALUES (1, {Q(pair.Key)}, {Q(physical)}, 1);");
        }
        return sql.ToString();
    }

    /// <summary>
    /// AdvanceConnector's public holidays (for the SQL Server tests), with real-looking column names unlike MyCRM's
    /// (other case, a look-alike column): ExternalTable must find them.
    /// </summary>
    public static string AdvanceSql() => """
        CREATE TABLE dbo.public_holiday ([Date] datetime NOT NULL, [Name] nvarchar(100) NOT NULL, [date-added] datetime NULL);
        INSERT INTO dbo.public_holiday VALUES ('2026-10-16', N'Deepavali (test)', '2026-01-01'), ('2026-12-25', N'Christmas Day', NULL);
        """;

    public static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
}
