namespace MyCRMRecords.Tests;

/// <summary>
/// MyOrders' and MyCRM's tables in memory, read the way <see cref="SqlMyOrdersData"/> reads them in the OutSystems
/// database: rows of column → value (upper case), a file column read as its length, the list's search worked out the
/// same way. The SQL Server tests check the real one answers as this one does.
/// </summary>
public sealed class FakeMyOrders : IMyOrdersData
{
    private readonly Dictionary<OsEntity, List<Dictionary<string, object?>>> _tables = new();
    private long _nextId = 1000;

    public bool Available { get; set; } = true;
    /// <summary>The rows as added, by entity (for the SQL Server tests, which put them in a real OutSystems database).</summary>
    public IReadOnlyDictionary<OsEntity, List<Dictionary<string, object?>>> Tables => _tables;
    public List<OldUser> UserRows { get; } = [];
    /// <summary>How many times each entity's rows were asked for (the page must not read more than it needs).</summary>
    public Dictionary<string, int> Reads { get; } = new();

    /// <summary>Adds a row (its ID given, or the next one) and returns its ID.</summary>
    public long Add(OsEntity entity, object values)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in values.GetType().GetProperties()) row[p.Name] = p.GetValue(values);
        if (!row.TryGetValue("ID", out var id) || id is null) row["ID"] = id = ++_nextId;
        if (!_tables.TryGetValue(entity, out var rows)) _tables[entity] = rows = [];
        rows.Add(row);
        return Convert.ToInt64(id);
    }

    public long User(string login, string name, string email = "")
    {
        var id = 50 + UserRows.Count;
        UserRows.Add(new OldUser(id, login, name, email));
        return id;
    }

    private IEnumerable<Dictionary<string, object?>> Table(OsEntity entity)
    {
        Reads[entity.ToString()] = Reads.TryGetValue(entity.ToString(), out var n) ? n + 1 : 1;
        return _tables.TryGetValue(entity, out var rows) ? rows : Enumerable.Empty<Dictionary<string, object?>>();
    }

    /// <summary>A row as the SQL implementation reads it: a file as its length.</summary>
    private static Dictionary<string, object?> AsRead(Dictionary<string, object?> row) =>
        row.ToDictionary(p => p.Key, p => p.Value is byte[] bytes ? (object?)(long)bytes.Length : p.Value, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Dictionary<string, object?>> Rows(OsEntity entity, string column, IReadOnlyCollection<long> values) =>
        Table(entity).Where(r => r.Get(column) is { } v && values.Contains(Convert.ToInt64(v))).OrderBy(r => r.Long("ID")).Select(AsRead).ToList();

    public IReadOnlyList<Dictionary<string, object?>> All(OsEntity entity) => Table(entity).OrderBy(r => r.Long("ID")).Select(AsRead).ToList();

    public IReadOnlyList<OldUser> Users() => UserRows;

    public byte[]? File(OsEntity entity, string column, long id) => Table(entity).FirstOrDefault(r => r.Long("ID") == id)?.Get(column) as byte[];

    public CostingSheetPeople People()
    {
        var active = Table(Entities.CostingSheet).Where(r => r.Bool("ISACTIVE")).ToList();
        return new CostingSheetPeople(
            active.Select(r => r.Id("SALESMANID")).Where(i => i is not null).Select(i => i!.Value).Distinct().ToList(),
            active.SelectMany(r => new[] { r.Id("MDSSPECIALISTID"), r.Id("PPSPECIALISTID"), r.Id("MITSSPECIALISTID") }).Where(i => i is not null).Select(i => i!.Value).Distinct().ToList());
    }

    public CostingSheetPage Search(CostingSheetSearch s)
    {
        string Label(OsEntity entity, long? id, string column) =>
            id is { } value ? Table(entity).FirstOrDefault(r => r.Long("ID") == value)?.Text(column) ?? "" : "";
        var rows = Table(Entities.CostingSheet).Where(r => r.Bool("ISACTIVE")).Select(cs =>
        {
            var contact = cs.Id("CONTACTID") is { } c ? Table(Entities.Contacts).FirstOrDefault(r => r.Long("ID") == c) : null;
            var row = new Dictionary<string, object?>(cs, StringComparer.OrdinalIgnoreCase)
            {
                ["SALESMAN"] = cs.Id("SALESMANID") is { } u ? UserRows.FirstOrDefault(x => x.Id == u)?.Name ?? "" : "",
                ["REFERENCE"] = Label(Entities.Reference, cs.Id("REFERENCEID"), "REFERENCE"),
                ["COMPANY"] = Label(Entities.Company, cs.Id("COMPANYID"), "COMPANY"),
                ["FIRSTNAME"] = contact?.Text("FIRSTNAME") ?? "",
                ["LASTNAME"] = contact?.Text("LASTNAME") ?? "",
                ["STATUS"] = Label(Entities.CostingSheetStatus, cs.Id("COSTINGSHEETSTATUSID"), "LABEL"),
            };
            return row;
        }).ToList();
        bool Has(Dictionary<string, object?> r, string column, string text) => r.Text(column).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        bool In(DateTime? value, DateTime? from, DateTime? to) =>
            (from is null || (value is { } a && a >= from.Value.Date)) && (to is null || (value is { } b && b < to.Value.Date.AddDays(1)));
        var keyword = s.Keyword.Trim();
        var matching = rows.Where(r =>
            (s.SalesmanIds is null || (r.Id("SALESMANID") is { } sm && s.SalesmanIds.Contains(sm)))
            && (s.CompanyContains is null || Has(r, "COMPANY", s.CompanyContains))
            && (keyword.Length == 0 || Has(r, "STATUS", keyword) || Has(r, "REFERENCE", keyword) || Has(r, "COMPANY", keyword) || r.Long("ID").ToString().Contains(keyword)
                || Has(r, "INVOICENO", keyword) || Has(r, "ORDERNO", keyword))
            && (s.StatusId is null || r.Id("COSTINGSHEETSTATUSID") == s.StatusId)
            && (s.CompanyId is null || r.Id("COMPANYID") == s.CompanyId)
            && (s.SalesRepIds is null || (r.Id("SALESMANID") is { } rep && s.SalesRepIds.Contains(rep)))
            && (s.SpecialistIds is null || new[] { r.Id("MDSSPECIALISTID"), r.Id("PPSPECIALISTID"), r.Id("MITSSPECIALISTID") }.Any(x => x is { } v && s.SpecialistIds.Contains(v)))
            && In(r.Date("CREATEDAT"), s.CreatedFrom, s.CreatedTo)
            && In(r.Date("HEADAPPROVEDAT"), s.HeadApprovedFrom, s.HeadApprovedTo)
            && In(r.Date("MININVOICEDATE"), s.InvoiceFrom, s.InvoiceTo)
            && (s.HasSpecialist != "Yes" || r.Id("MITSSPECIALISTID") is not null || r.Id("MDSSPECIALISTID") is not null)
            && (s.HasSpecialist != "No" || (r.Id("MITSSPECIALISTID") is null && r.Id("MDSSPECIALISTID") is null)))
            .ToList();
        Func<Dictionary<string, object?>, object?> key = s.Sort switch
        {
            "salesman" => r => r.Text("SALESMAN"),
            "company" => r => r.Text("COMPANY"),
            "created" => r => r.Date("CREATEDAT"),
            "achieved" => r => r.Decimal("TOTALACHIEVEDPRICE"),
            "status" => r => r.Text("STATUS"),
            _ => r => r.Long("ID"),
        };
        var ordered = s.Descending ? matching.OrderByDescending(key) : matching.OrderBy(key);
        var sorted = s.Sort is "id" or "" ? ordered : ordered.ThenByDescending(r => r.Long("ID"));
        return new CostingSheetPage(matching.Count, sorted.Skip(s.Skip).Take(s.Take).Select(SqlMyOrdersData.ListRow).ToList());
    }

    public IReadOnlyCollection<long> RequestFormSalesmen() =>
        Table(Entities.RequestForm).Where(r => r.Bool("ISACTIVE")).Select(r => r.Id("SALESMANID")).Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();

    public RequestFormPage SearchRequestForms(RequestFormSearch s)
    {
        string Label(OsEntity entity, long? id, string column) =>
            id is { } value ? Table(entity).FirstOrDefault(r => r.Long("ID") == value)?.Text(column) ?? "" : "";
        var rows = Table(Entities.RequestForm).Where(r => r.Bool("ISACTIVE")).Select(rf =>
        {
            var contact = rf.Id("CONTACTID") is { } c ? Table(Entities.Contacts).FirstOrDefault(r => r.Long("ID") == c) : null;
            return new Dictionary<string, object?>(rf, StringComparer.OrdinalIgnoreCase)
            {
                ["SALESMAN"] = rf.Id("SALESMANID") is { } u ? UserRows.FirstOrDefault(x => x.Id == u)?.Name ?? "" : "",
                ["REFERENCE"] = Label(Entities.Reference, rf.Id("REFERENCEID"), "REFERENCE"),
                ["COMPANY"] = Label(Entities.Company, rf.Id("COMPANYID"), "COMPANY"),
                ["SALUTATION"] = contact?.Text("SALUTATION") ?? "",
                ["FIRSTNAME"] = contact?.Text("FIRSTNAME") ?? "",
                ["MIDDLENAME"] = contact?.Text("MIDDLENAME") ?? "",
                ["LASTNAME"] = contact?.Text("LASTNAME") ?? "",
                ["TYPE"] = Label(Entities.RequestFormType, rf.Id("REQUESTFORMTYPEID"), "LABEL"),
                ["STATUS"] = Label(Entities.RequestFormStatus, rf.Id("REQUESTFORMSTATUSID"), "LABEL"),
            };
        }).ToList();
        bool Has(Dictionary<string, object?> r, string column, string text) => r.Text(column).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        bool In(Dictionary<string, object?> r, IReadOnlyCollection<long> ids, string column) => r.Id(column) is { } id && ids.Contains(id);
        bool AnySpecialist(Dictionary<string, object?> r) => r.Id("MDSSPECIALISTID") is not null || r.Id("PPSPECIALISTID") is not null || r.Id("MITSSPECIALISTID") is not null;
        var keyword = s.Keyword.Trim();
        var matching = rows.Where(r =>
            (s.SalesmanIds is null || In(r, s.SalesmanIds, "SALESMANID") || In(r, s.SpecialistOf, "MDSSPECIALISTID") || In(r, s.SpecialistOf, "MITSSPECIALISTID")
                || (s.AnyWithSpecialist && AnySpecialist(r)) || In(r, s.PpSpecialistOf, "PPSPECIALISTID"))
            && (s.CompanyContains is null || Has(r, "COMPANY", s.CompanyContains))
            && (keyword.Length == 0 || Has(r, "STATUS", keyword) || Has(r, "REFERENCE", keyword) || Has(r, "COMPANY", keyword) || r.Long("ID").ToString().Contains(keyword))
            && (s.StatusId is null || r.Id("REQUESTFORMSTATUSID") == s.StatusId)
            && (s.CompanyId is null || r.Id("COMPANYID") == s.CompanyId)
            && (s.TypeId is null || r.Id("REQUESTFORMTYPEID") == s.TypeId)
            && (s.SalesRepIds is null || In(r, s.SalesRepIds, "SALESMANID"))
            && (s.CreatedFrom is null || (r.Date("CREATEDAT") is { } a && a >= s.CreatedFrom.Value.Date))
            && (s.CreatedTo is null || (r.Date("CREATEDAT") is { } b && b < s.CreatedTo.Value.Date.AddDays(1))))
            .ToList();
        Func<Dictionary<string, object?>, object?> key = s.Sort switch
        {
            "id" => r => r.Long("ID"),
            "salesman" => r => r.Text("SALESMAN"),
            "company" => r => r.Text("COMPANY"),
            "type" => r => r.Text("TYPE"),
            "status" => r => r.Text("STATUS"),
            "printed" => r => r.Date("PRINTEDAT"),
            "pendingprint" => r => RequestFormRow.PendingPrint(r.Text("STATUS"), r.Date("PRINTEDAT"), r.Date("RECALLAT")) ? 1 : 0,
            _ => r => r.Date("CREATEDAT"),
        };
        var ordered = s.Descending ? matching.OrderByDescending(key) : matching.OrderBy(key);
        var sorted = s.Sort == "id" ? ordered : ordered.ThenByDescending(r => r.Long("ID"));
        return new RequestFormPage(matching.Count, sorted.Skip(s.Skip).Take(s.Take).Select(SqlMyOrdersData.RequestFormListRow).ToList());
    }
}
