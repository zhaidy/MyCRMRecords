using System.Globalization;

namespace MyCRMRecords.Tests;

/// <summary>
/// An old OutSystems app's tables in memory, for the sync's tests: rows with an identity ID, columns typed by their name
/// as OutSystems makes them, users and static entities. Like the real writer it can add and change rows, never delete
/// them; changes count only once committed, and <see cref="FailNextCommit"/> makes the next commit fail. Of MyCRM's
/// territories it gives the sync only the active permanent ones and their active companies, as MyCRMRecords reads them
/// does, and it answers which companies people reach through the temporary ones.
/// </summary>
public sealed class FakeOldTables : IOldTables, ITemporaryTerritories
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
    /// MyCRM's Users, Territory and Company entities as OutSystems made them, its static entities with database Ids unlike
    /// MyCRMRecords's lists (Role 201..., so a mapping by Id or by order fails; MITSSpecialist labelled "SOL Specialist"),
    /// and its users.
    /// </summary>
    public static FakeOldTables MyCRM()
    {
        var t = new FakeOldTables();
        t.Define("CompanyGroupName", "ID:id GROUPNAME:255");
        t.Define("Team", "ID:id NAME:255 SHORTNAME:50 ISACTIVE:bit");
        t.Define("CUser", "ID:id USERID:user ROLEID:ref TEAMID:ref ISACTIVE:bit CREATEDBY:user CREATEDAT:datetime UPDATEDBY:user UPDATEDAT:datetime DESIGNATION:50 " +
            "PHONENUMBER:20 SALESMANCODE:50 CID:int PROFILEID:ref TERRITORYCODE:50");
        t.Define("Company", "ID:id COMPANY:250 BRN:50 WEBSITE:100 ISACTIVE:bit INCCERTNO:50 NOOFEMPLOYEES:int PAIDUPCAPITAL:int COMPANYGROUPID:ref COMPANYGROUPNAMEID:ref " +
            "CRMCOMPANYID:long CREATEDDATE:date UPDATEDDATE:date COMPACCOUNTTYPEID:ref COMPOWNERSHIPID:ref ISCARETAKE:bit UPDATEDBY:user CREATEDBY:user REMARKS:10000 " +
            "ISCHANNELPARTNER:bit PHONE:20 FAX:20 ISCREATEDBYMSD:bit MIFPOTENTIAL:int REVENUE:money COMPANYINDUSTRYID:ref PRECOMPANYNAME:250 PREBRN:50 PREWEBSITE:100 " +
            "PREISACTIVE:bit PREINCCERTNO:50 PRENOOFEMPLOYEES:int PREPAIDUPCAPITAL:int PRECOMPANYGROUPID:ref PRECOMPANYGROUPNAMEID:ref PRECOMPACCOUNTTYPEID:ref " +
            "PRECOMPOWNERSHIPID:ref PREISCARETAKE:bit PREREMARKS:10000 PREISCHANNELPARTNER:bit PREPHONE:20 PREFAX:20 PREMIFPOTENTIAL:int PREREVENUE:money " +
            "PRECOMPANYINDUSTRYID:ref CUSTOMERTYPEID:ref ISCHANNELPARTNERFORVSS:bit ISCHANNELPARTNERFORCS:bit CUSTOMERNO:50 CREDITTERMS:50 SALESREMARKS:max ADVANCECUSTOMERTYPEID:ref");
        t.Define("CompanyCaretake", "ID:id COMPANYID:ref CARETAKEACCOUNTREMARKS:10000 UPDATEDDATE:date CREATEDBY:user UPDATEDBY:user DATECREATED:date");
        t.Define("CompanyRelationship", "ID:id COMPANYID:ref COMPANYRELATIONSHIPTYPEID:ref RELATEDCOMPANYID:ref ISACTIVE:bit");
        t.Define("Address", "ID:id ADDRESSTYPEID:ref UNITNUMBER:2000 BLOCK:2000 STREET:2000 BUILDINGNAME:2000 POSTALCODE:50 ISACTIVE:bit CRMADDRESSID:long ISDEFAULT:bit " +
            "COUNTRY:255 CITY:255 STATEPROVINCE:255 PREADDRESSTYPEID:ref PREUNITNUMBER:2000 PREBLOCK:2000 PRESTREET:2000 PREBUILDINGNAME:2000 PREPOSTALCODE:50 PRECOUNTRY:255 " +
            "PRECITY:255 PRESTATEPROVINCE:255 PREISACTIVE:bit CREATEDAT:datetime CREATEDBY:user UPDATEDAT:datetime UPDATEDBY:user");
        t.Define("CompanyAddressLink", "ID:id COMPANYID:ref ADDRESSID:ref CRMADDRESSLINK:long");
        t.Define("Contacts", "ID:id FIRSTNAME:250 LASTNAME:250 ISACTIVE:bit DEPARTMENT:50 CID:long MIDDLENAME:250 CRMCONTACTLINKID:long DESIGNATION:250 SALUTATION:250 " +
            "CREATEDAT:datetime CREATEDBY:user UPDATEDAT:datetime UPDATEDBY:user PREFIRSTNAME:250 PRELASTNAME:250 PREISACTIVE:bit PREDEPARTMENT:50 PREMIDDLENAME:250 " +
            "PREDESIGNATION:250 PRESALUTATION:250 ISLINKEDIN:bit ISEQLIST:bit");
        t.Define("CompanyContactLink", "ID:id COMPANYID:ref CONTACTID:ref CID:long ISDEFAULT:bit");
        t.Define("Phone", "ID:id COMPANYID:ref CONTACTID:ref PHONETYPEID:ref PHONE:20 ISACTIVE:bit CRMPHONEID:long CRMPHONELINKID:long CREATEDAT:datetime CREATEDBY:user " +
            "UPDATEDAT:datetime UPDATEDBY:user PREPHONE:20 PREPHONETYPEID:ref PREISACTIVE:bit");
        t.Define("Email", "ID:id EMAIL:200 COMPANYID:ref CONTACTID:ref ISACTIVE:bit CID:long CRMEMAILLINKID:long CREATEDAT:datetime CREATEDBY:user UPDATEDAT:datetime " +
            "UPDATEDBY:user PREEMAIL:250 PREISACTIVE:bit CONSENTTOMARKETINGINFO:bit");
        t.Define("Territory", "ID:id TERRITORY:long OWNERID:user ISACTIVE:bit ISTEMPORARY:bit ISOWNER:bit");
        t.Define("CompanyTerritory", "ID:id TERRITORYID:ref COMPANYID:ref ISACTIVE:bit CREATEDBY:user");
        t.Define("TerritoryMovement", "ID:id COMPANYID:ref FROMUSERID:user TOUSERID:user UPDATEDBY:user UPDATEDAT:datetime");
        // Static entities: Ids unlike any list's position, in an order unlike MyCRMRecords's lists.
        t.Static["Role"] = CrmRoles.List.All.Select((c, i) => (Id: 245L - i, c.Label)).ToDictionary(x => x.Id, x => x.Label);
        t.Static["CompanyGroup"] = new() { [301] = "SME", [302] = "SMEDEA", [303] = "DEA", [304] = "JKA", [305] = "GOV", [306] = "CA", [307] = "LG", [308] = "GA", [309] = "MA", [310] = "LMA", [311] = "GMA" };
        t.Static["CompAccountType"] = new() { [321] = "Non-User", [322] = "User" };
        t.Static["CompOwnership"] = new() { [331] = "Declared", [332] = "Owned" };
        t.Static["CompanyIndustry"] = CompanyIndustries.List.All.Select((c, i) => (Id: 440L - i, c.Label)).ToDictionary(x => x.Id, x => x.Label);
        t.Static["CustomerType"] = new() { [451] = "BDG - IT on MIF", [452] = "BDG - ZBA" };
        t.Static["AdvanceCustomerType"] = AdvanceCustomerTypes.List.All.Select((c, i) => (Id: 480L - i, c.Label)).ToDictionary(x => x.Id, x => x.Label);
        t.Static["AddressType"] = new() { [491] = "Mailing", [492] = "Storage", [493] = "Delivery", [494] = "Billing" };
        t.Static["PhoneType"] = new() { [501] = "Home", [502] = "Fax", [503] = "Mobile", [504] = "Office" };
        t.Static["CompanyRelationshipType"] = new() { [511] = "Peer", [512] = "Child", [513] = "Parent" };
        t.UserList.AddRange([
            new OldUser(101, @"RSP\sam", "Sam Sales", "sam@example.com"), new OldUser(102, @"RSP\sara", "Sara Admin", "sara@example.com"),
            new OldUser(103, @"RSP\mia", "Mia Manager", "mia@example.com"), new OldUser(104, @"RSP\mark", "Mark Marketing", "mark@example.com"),
            new OldUser(105, @"RSP\olga", "Olga Other", "olga@example.com"), new OldUser(106, @"RSP\dina", "Dina Director", "dina@example.com"),
            new OldUser(107, @"RSP\ken", "Ken Kwan", "ken@example.com"), new OldUser(108, @"RSP\tom", "Tom Telesales", "tom@example.com"),
            new OldUser(1, @"RSP\root", "Rob Root", "rob@example.com"),
        ]);
        return t;
    }

    /// <summary>The Id of a static entity's record by its label (for rows written as MyCRM writes them).</summary>
    public long StaticId(string entity, string label) => Static[entity].Single(p => p.Value == label).Key;

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
            .Where(r => SyncSees(table, r))
            .Select(r => new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase)).ToList();

    private static bool True(object? value) => value is bool b && b;

    /// <summary>As MyCrmStore: only active permanent territories, and the active companies of those.</summary>
    private bool SyncSees(string table, Dictionary<string, object?> row)
    {
        if (string.Equals(table, MyCrmMap.Territories, StringComparison.OrdinalIgnoreCase))
            return True(row["ISACTIVE"]) && !True(row["ISTEMPORARY"]);
        if (string.Equals(table, MyCrmMap.CompanyTerritories, StringComparison.OrdinalIgnoreCase))
            return True(row["ISACTIVE"]) && row["TERRITORYID"] is not null
                && _rows[MyCrmMap.Territories].Any(t => (long)t["ID"]! == Convert.ToInt64(row["TERRITORYID"], CultureInfo.InvariantCulture) && True(t["ISACTIVE"]) && !True(t["ISTEMPORARY"]));
        return true;
    }

    /// <summary>As MyCrmStore: MyCRM's Ids of the active companies of these people's active temporary territories.</summary>
    public IReadOnlyCollection<long> CompanyIds(IReadOnlyCollection<string> logins)
    {
        var users = UserList.Where(u => logins.Any(l => string.Equals(l, u.Login, StringComparison.OrdinalIgnoreCase))).Select(u => u.Id).ToHashSet();
        var territories = _rows[MyCrmMap.Territories].Where(t => True(t["ISACTIVE"]) && True(t["ISTEMPORARY"]) && t["OWNERID"] is not null
            && users.Contains(Convert.ToInt64(t["OWNERID"], CultureInfo.InvariantCulture))).Select(t => (long)t["ID"]!).ToHashSet();
        return _rows[MyCrmMap.CompanyTerritories].Where(ct => True(ct["ISACTIVE"]) && ct["TERRITORYID"] is not null
                && territories.Contains(Convert.ToInt64(ct["TERRITORYID"], CultureInfo.InvariantCulture)))
            .Select(ct => Convert.ToInt64(ct["COMPANYID"], CultureInfo.InvariantCulture)).Distinct().ToList();
    }

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
        // MyOrders (read for the costing sheets and request forms) is in the same OutSystems database: its entities, empty,
        // and the columns the costing sheets read from MyCRM's, so the connection test passes as it does in production.
        sql.AppendLine("INSERT INTO dbo.OSSYS_ESPACE VALUES (2, N'MyOrders', 1);");
        sql.AppendLine("INSERT INTO dbo.OSSYS_ESPACE VALUES (3, N'MyFiles', 1);");
        sql.AppendLine("INSERT INTO dbo.OSSYS_ESPACE VALUES (4, N'MyMachines', 1);");
        foreach (var (entity, columns) in Entities.Checked)
        {
            string Type(string column) => column == "ID" || column.EndsWith("ID", StringComparison.Ordinal) ? "bigint" : "nvarchar(2000)";
            var known = entity.Module == OsModule.MyCRM
                ? _schema.TryGetValue(entity.Name, out var schema) ? schema.Values.Select(c => c.Name).ToList()
                : Static.ContainsKey(entity.Name) ? new List<string> { "ID", "LABEL", "ORDER", "IS_ACTIVE" } : null
                : null;
            if (known is not null)
            {
                var table = "OSUSR_ab1_" + entity.Name.ToUpperInvariant();
                foreach (var column in columns.Where(c => !known.Contains(c, StringComparer.OrdinalIgnoreCase)))
                    sql.AppendLine($"ALTER TABLE dbo.[{table}] ADD [{column}] {Type(column)} NULL;");
                continue;
            }
            var physical = (entity.Module == OsModule.MyOrders ? "OSUSR_or1_" : entity.Module == OsModule.MyFiles ? "OSUSR_mf1_"
                : entity.Module == OsModule.MyMachines ? "OSUSR_mm1_" : "OSUSR_ab1_") + entity.Name.ToUpperInvariant();
            sql.AppendLine($"CREATE TABLE dbo.[{physical}] ({string.Join(", ", columns.Select(c => $"[{c}] {Type(c)} NULL"))});");
            var espace = entity.Module == OsModule.MyOrders ? 2 : entity.Module == OsModule.MyFiles ? 3 : entity.Module == OsModule.MyMachines ? 4 : 1;
            sql.AppendLine($"INSERT INTO dbo.OSSYS_ENTITY ([ESPACE_ID], [NAME], [PHYSICAL_TABLE_NAME], [IS_ACTIVE]) VALUES ({espace}, {Q(entity.Name)}, {Q(physical)}, 1);");
        }
        return sql.ToString();
    }

    /// <summary>
    /// AdvanceConnector's Customer_Supp (for the SQL Server tests), with real-looking column names unlike MyCRM's
    /// ([Business reg] for Business_reg, [Cust-no] for Cust_no): ExternalTable must find them. Two customers share a BRN.
    /// </summary>
    public static string AdvanceSql() => """
        CREATE TABLE dbo.Customer_Supp ([Cust-no] nvarchar(20) NOT NULL, [Business reg] nvarchar(50) NULL, [Cust name] nvarchar(100) NULL);
        INSERT INTO dbo.Customer_Supp VALUES (N'C0002', N'200012345K', N'Tan & Sons'), (N'C0001', N'199912345K', N'Lim Pte Ltd'),
          (N'C0003', N'199912345K', N'Lim Pte Ltd (2)'), (N'C0004', NULL, N'No BRN'), (N'C0005', N'ABC', N'Short BRN');
        """;

    public static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
}
