using System.Globalization;
using System.Text;

namespace MyCRMRecords.Data;

/// <summary>What the request forms list asks for: its filters (MyOrders' RequestForms screen), whose request forms, a page.</summary>
public sealed class RequestFormSearch
{
    /// <summary>Part of the number, status, reference or company.</summary>
    public string Keyword { get; set; } = "";
    public long? StatusId { get; set; }
    /// <summary>One company's request forms (MyCRM's company Id: a company's page links to them); null for any.</summary>
    public long? CompanyId { get; set; }
    /// <summary>The request form type chosen (MyOrders' list had no such filter).</summary>
    public long? TypeId { get; set; }
    /// <summary>The Sales Rep chosen: their OutSystems users (a person can have two, OrdersAccess.SamePerson); null for anyone.</summary>
    public IReadOnlyCollection<long>? SalesRepIds { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }

    // Whose request forms (set by RequestForms.List from Access): MyOrders' RequestForms list, and its specialists' list.
    /// <summary>The salespeople (OutSystems users) whose request forms the person sees; null for everyone's.</summary>
    public IReadOnlyCollection<long>? SalesmanIds { get; set; }
    /// <summary>Also the request forms where one of these users is the MDS or Fusion (MITS) specialist.</summary>
    public IReadOnlyCollection<long> SpecialistOf { get; set; } = Array.Empty<long>();
    /// <summary>Also every request form with a specialist (MDS and Fusion specialist managers, specialist senior managers).</summary>
    public bool AnyWithSpecialist { get; set; }
    /// <summary>Also the request forms whose PP specialist is one of these users (PP specialists and their managers: their MyCRM list).</summary>
    public IReadOnlyCollection<long> PpSpecialistOf { get; set; } = Array.Empty<long>();
    /// <summary>Only companies whose name contains this (MyOrders' Service Delivery Managers see DBS's only); null for any.</summary>
    public string? CompanyContains { get; set; }

    public string Sort { get; set; } = "created";
    public bool Descending { get; set; } = true;
    public int Skip { get; set; }
    public int Take { get; set; } = 50;

    public static readonly string[] Sorts = ["id", "salesman", "reference", "company", "contact", "type", "created", "updated", "recall", "status", "printed", "pendingprint", "opo"];

    /// <summary>The statuses at which MyOrders printed a request form (and called it pending print until printed).</summary>
    public static readonly string[] PrintableStatuses = ["Pending Credit Check", "Pending Allocation", "Pending Invoicing", "Backordered"];
}

/// <summary>A request form as the list shows it.</summary>
public sealed record RequestFormRow
{
    public long Id { get; init; }
    public long? SalesmanId { get; init; }
    public string Salesman { get; init; } = "";
    public string Reference { get; init; } = "";
    public string Company { get; init; } = "";
    public string Contact { get; init; } = "";
    public string Type { get; init; } = "";
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? RecallAt { get; init; }
    public DateTime? PrintedAt { get; init; }
    public string Status { get; init; } = "";
    /// <summary>MyOrders' Is Pending Print: a printable status, and not printed since it was last recalled.</summary>
    public bool IsPendingPrint { get; init; }
    public bool IsOpoChecked { get; init; }
    public long? CostingSheetId { get; init; }
    /// <summary>The delivery or collection dates of its active items (set by RequestForms.List for the page's rows).</summary>
    public IReadOnlyList<DateTime> Dates { get; init; } = Array.Empty<DateTime>();
    /// <summary>Whether MyOrders let the person print it (set by RequestForms.List).</summary>
    public bool CanPrint { get; init; }

    /// <summary>MyOrders' IsPendingPrint from the status and the two dates.</summary>
    public static bool PendingPrint(string status, DateTime? printedAt, DateTime? recallAt) =>
        RequestFormSearch.PrintableStatuses.Contains(status, StringComparer.OrdinalIgnoreCase)
        && (printedAt is null || (recallAt is { } recall && recall > printedAt.Value));
}

public sealed record RequestFormPage(int Total, IReadOnlyList<RequestFormRow> Rows);

public sealed partial class SqlMyOrdersData
{
    public IReadOnlyCollection<long> RequestFormSalesmen() =>
        Cached("rf-salesmen", () => Databases.Use(ExternalNames.OutSystems, connection =>
            (IReadOnlyCollection<long>)Sql.Rows(connection, null,
                    $"SELECT DISTINCT [SALESMANID] AS [ID] FROM {TableOf(connection, Entities.RequestForm)} WHERE [ISACTIVE] = 1 AND [SALESMANID] IS NOT NULL AND [SALESMANID] <> 0")
                .Select(r => Convert.ToInt64(r["ID"], CultureInfo.InvariantCulture)).ToList()));

    public RequestFormPage SearchRequestForms(RequestFormSearch search) => Databases.Use(ExternalNames.OutSystems, connection =>
    {
        var from = new StringBuilder()
            .Append($" FROM {TableOf(connection, Entities.RequestForm)} rf")
            .Append($" LEFT JOIN {TableOf(connection, Entities.RequestFormStatus)} st ON st.[ID] = rf.[REQUESTFORMSTATUSID]")
            .Append($" LEFT JOIN {TableOf(connection, Entities.RequestFormType)} ty ON ty.[ID] = rf.[REQUESTFORMTYPEID]")
            .Append($" LEFT JOIN {TableOf(connection, Entities.Reference)} re ON re.[ID] = rf.[REFERENCEID]")
            .Append($" LEFT JOIN {TableOf(connection, Entities.Company)} co ON co.[ID] = rf.[COMPANYID]")
            .Append($" LEFT JOIN {TableOf(connection, Entities.Contacts)} ct ON ct.[ID] = rf.[CONTACTID]")
            .Append(" LEFT JOIN dbo.OSSYS_USER u ON u.[ID] = rf.[SALESMANID]")
            .ToString();
        var where = new List<string> { "rf.[ISACTIVE] = 1" };
        var parameters = new List<(string, object?)>();
        string P(object value)
        {
            var name = "@p" + parameters.Count.ToString(CultureInfo.InvariantCulture);
            parameters.Add((name, value));
            return name;
        }
        string Ids(IEnumerable<long> ids) => string.Join(", ", ids.Distinct().Select(i => i.ToString(CultureInfo.InvariantCulture)));
        string Set(string column) => $"(rf.[{column}] IS NOT NULL AND rf.[{column}] <> 0)";

        if (search.SalesmanIds is { } allowed)
        {
            var whose = new List<string> { allowed.Count == 0 ? "1 = 0" : $"rf.[SALESMANID] IN ({Ids(allowed)})" };
            if (search.SpecialistOf.Count > 0)
                whose.Add($"rf.[MDSSPECIALISTID] IN ({Ids(search.SpecialistOf)}) OR rf.[MITSSPECIALISTID] IN ({Ids(search.SpecialistOf)})");
            if (search.AnyWithSpecialist) whose.Add($"{Set("MDSSPECIALISTID")} OR {Set("PPSPECIALISTID")} OR {Set("MITSSPECIALISTID")}");
            if (search.PpSpecialistOf.Count > 0) whose.Add($"rf.[PPSPECIALISTID] IN ({Ids(search.PpSpecialistOf)})");
            where.Add("(" + string.Join(" OR ", whose) + ")");
        }
        if (search.CompanyContains is { } part)
            where.Add($"co.[COMPANY] LIKE {P("%" + Sql.EscapeLike(part) + "%")} ESCAPE '\\'");
        var keyword = search.Keyword.Trim();
        if (keyword.Length > 0)
        {
            var like = P("%" + Sql.EscapeLike(keyword) + "%");
            where.Add($"(st.[LABEL] LIKE {like} ESCAPE '\\' OR re.[REFERENCE] LIKE {like} ESCAPE '\\' OR co.[COMPANY] LIKE {like} ESCAPE '\\' " +
                $"OR CAST(rf.[ID] AS nvarchar(20)) LIKE {like} ESCAPE '\\')");
        }
        if (search.StatusId is { } status) where.Add($"rf.[REQUESTFORMSTATUSID] = {P(status)}");
        if (search.CompanyId is { } companyId) where.Add($"rf.[COMPANYID] = {P(companyId)}");
        if (search.TypeId is { } type) where.Add($"rf.[REQUESTFORMTYPEID] = {P(type)}");
        if (search.SalesRepIds is { } reps) where.Add(reps.Count == 0 ? "1 = 0" : $"rf.[SALESMANID] IN ({Ids(reps)})");
        if (search.CreatedFrom is { } f) where.Add($"rf.[CREATEDAT] >= {P(f.Date)}");
        if (search.CreatedTo is { } t) where.Add($"rf.[CREATEDAT] < {P(t.Date.AddDays(1))}");

        var printable = string.Join(", ", RequestFormSearch.PrintableStatuses.Select(s => "N'" + s.Replace("'", "''") + "'"));
        // OutSystems' empty date is 1900-01-01.
        var pendingPrint = $"CASE WHEN st.[LABEL] IN ({printable}) AND (rf.[PRINTEDAT] IS NULL OR rf.[PRINTEDAT] < '1900-01-02' " +
            "OR (rf.[RECALLAT] >= '1900-01-02' AND rf.[RECALLAT] > rf.[PRINTEDAT])) THEN 1 ELSE 0 END";
        var whereSql = " WHERE " + string.Join(" AND ", where);
        var args = parameters.ToArray();
        var total = Convert.ToInt32(Sql.Rows(connection, null, "SELECT COUNT(*) AS [N]" + from + whereSql, args)[0]["N"], CultureInfo.InvariantCulture);
        var order = (search.Sort switch
        {
            "id" => "rf.[ID]",
            "salesman" => "u.[NAME]",
            "reference" => "re.[REFERENCE]",
            "company" => "co.[COMPANY]",
            "contact" => "ct.[FIRSTNAME]",
            "type" => "ty.[LABEL]",
            "updated" => "rf.[UPDATEDAT]",
            "recall" => "rf.[RECALLAT]",
            "status" => "st.[LABEL]",
            "printed" => "rf.[PRINTEDAT]",
            "pendingprint" => pendingPrint,
            "opo" => "rf.[ISOPOCHECKED]",
            _ => "rf.[CREATEDAT]",
        }) + (search.Descending ? " DESC" : " ASC") + (search.Sort == "id" ? "" : ", rf.[ID] DESC");
        var take = Math.Max(1, Math.Min(search.Take, 20000));
        var rows = Sql.Rows(connection, null,
            "SELECT rf.[ID], rf.[SALESMANID], u.[NAME] AS [SALESMAN], re.[REFERENCE] AS [REFERENCE], co.[COMPANY] AS [COMPANY], ct.[SALUTATION] AS [SALUTATION], " +
            "ct.[FIRSTNAME] AS [FIRSTNAME], ct.[MIDDLENAME] AS [MIDDLENAME], ct.[LASTNAME] AS [LASTNAME], ty.[LABEL] AS [TYPE], rf.[CREATEDAT], rf.[UPDATEDAT], " +
            "rf.[RECALLAT], rf.[PRINTEDAT], st.[LABEL] AS [STATUS], rf.[ISOPOCHECKED], rf.[COSTINGSHEETID]" + from + whereSql +
            $" ORDER BY {order} OFFSET {Math.Max(0, search.Skip).ToString(CultureInfo.InvariantCulture)} ROWS FETCH NEXT {take.ToString(CultureInfo.InvariantCulture)} ROWS ONLY",
            args);
        return new RequestFormPage(total, rows.Select(RequestFormListRow).ToList());
    });

    /// <summary>A row of the request form list query (or of the fake's join) as the list shows it.</summary>
    public static RequestFormRow RequestFormListRow(IReadOnlyDictionary<string, object?> r) => new()
    {
        Id = r.Long("ID"),
        SalesmanId = r.Id("SALESMANID"),
        Salesman = r.Text("SALESMAN"),
        Reference = r.Text("REFERENCE"),
        Company = r.Text("COMPANY"),
        // MyOrders' CompleteContact: salutation, first, middle and last name.
        Contact = string.Join(" ", new[] { r.Text("SALUTATION"), r.Text("FIRSTNAME"), r.Text("MIDDLENAME"), r.Text("LASTNAME") }.Select(t => t.Trim()).Where(t => t.Length > 0)),
        Type = r.Text("TYPE"),
        CreatedAt = r.Date("CREATEDAT"),
        UpdatedAt = r.Date("UPDATEDAT"),
        RecallAt = r.Date("RECALLAT"),
        PrintedAt = r.Date("PRINTEDAT"),
        Status = r.Text("STATUS"),
        IsPendingPrint = RequestFormRow.PendingPrint(r.Text("STATUS"), r.Date("PRINTEDAT"), r.Date("RECALLAT")),
        IsOpoChecked = r.Bool("ISOPOCHECKED"),
        CostingSheetId = r.Id("COSTINGSHEETID"),
    };
}
