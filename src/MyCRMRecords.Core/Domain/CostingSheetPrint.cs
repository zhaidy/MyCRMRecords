using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

// MyOrders' costing sheet PDF as a page to print (print.aspx): the "New CS" layout (one flowing page) and the "Old CS"
// layout (item pages, meter pages, the financing page with its calculation and verification, the service credit and
// specialists pages). Everything either layout shows, worked out here; print.js only lays it out.

/// <summary>What the two print layouts of a costing sheet show.</summary>
public sealed record CostingSheetPrint
{
    public CostingSheetView Sheet { get; init; } = new();
    /// <summary>"Reference-Id", the Ref # of the Old CS particulars (201605010001-1000).</summary>
    public string ReferenceWithId { get; init; } = "";
    /// <summary>MyCRM's company: its incorporation number (BRN/UEN; its BRN when it has none) and credit terms.</summary>
    public string Brn { get; init; } = "";
    public string CompanyCreditTerms { get; init; } = "";
    /// <summary>The contact with their salutation (MyOrders' CompleteContact) and their phone numbers.</summary>
    public string Contact { get; init; } = "";
    public string ContactPhone { get; init; } = "";
    public string SalesmanName { get; init; } = "";
    public string SalesmanCode { get; init; } = "";
    /// <summary>The salesperson's manager (MyOrders' GetManagerName) and head (GetHead), from MyCRM's teams.</summary>
    public string ManagerName { get; init; } = "";
    public string HeadName { get; init; } = "";
    /// <summary>The MDS, MITS and PP specialists' names, in MyOrders' order.</summary>
    public IReadOnlyList<string> SpecialistNames { get; init; } = Array.Empty<string>();
    public DateTime? CreatedAt { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public bool IsOutrightSale { get; init; }
    public bool IsInternalFinancing { get; init; }
    public bool IsExternalFinancing { get; init; }
    public bool IsVitalCase { get; init; }
    public bool IsEpp { get; init; }
    public bool IsNovation { get; init; }
    public bool IsZba { get; init; }
    public decimal TotalVariableCost { get; init; }
    public string InvoiceNo { get; init; } = "";
    /// <summary>The external finance company of the costing sheet, whatever its finance type (the Old CS's "Bill to").</summary>
    public string BillToName { get; init; } = "";

    /// <summary>The active items, for the Old CS item and meter pages.</summary>
    public IReadOnlyList<PrintItem> Items { get; init; } = Array.Empty<PrintItem>();
    /// <summary>The New CS's Items table: each item, then its sub items, packet items and complex items as lines 1.1, 1.2…</summary>
    public IReadOnlyList<PrintLine> Lines { get; init; } = Array.Empty<PrintLine>();
    /// <summary>The Old CS's variable costs table: a row per variable cost name, in MyOrders' order.</summary>
    public IReadOnlyList<PrintVariableRow> VariableRows { get; init; } = Array.Empty<PrintVariableRow>();
    /// <summary>Every variable cost but the R2R and O2R trade-ins.</summary>
    public decimal SubtotalBeforeTradeIn { get; init; }
    /// <summary>The New CS's Variables table: every variable cost of the costing sheet.</summary>
    public IReadOnlyList<PrintVariable> Variables { get; init; } = Array.Empty<PrintVariable>();
    public PrintFinance Finance { get; init; } = new();
    /// <summary>The items have different monthly meter rates (and it is not PPP): service credit is shown per item.</summary>
    public bool RatesDiffer { get; init; }
    public IReadOnlyList<PrintComplexGroup> ComplexGroups { get; init; } = Array.Empty<PrintComplexGroup>();
    public IReadOnlyList<PrintPacket> Packets { get; init; } = Array.Empty<PrintPacket>();
    /// <summary>An item's sub items run longer than 200 characters: the Old CS lists only the variable costs it has.</summary>
    public bool IsLong { get; init; }
}

public sealed record PrintItem
{
    public long Id { get; init; }
    public string ProductName { get; init; } = "";
    public int Quantity { get; init; }
    public decimal AchievedPrice { get; init; }
    public decimal TotalStp { get; init; }
    /// <summary>"NameA x 1,NameB x 2": the item's active sub items.</summary>
    public string SubItemsText { get; init; } = "";
    /// <summary>It has active complex items or is a packet ("Please refer to the last page").</summary>
    public bool HasComplex { get; init; }
    public int InitialBwCopies { get; init; }
    public decimal InitialBwPrice { get; init; }
    public int InitialClrCopies { get; init; }
    public decimal InitialClrPrice { get; init; }
    public int MonthlyBwCopies { get; init; }
    public decimal MonthlyBwPrice { get; init; }
    public int MonthlyClrCopies { get; init; }
    public decimal MonthlyClrPrice { get; init; }
    public int MonthlyMonths { get; init; }
    public decimal ThereafterBwPrice { get; init; }
    public decimal ThereafterClrPrice { get; init; }
}

/// <summary>A line of the New CS's Items table; the quantity and money only on an item's own line (<see cref="IsMain"/>).</summary>
public sealed record PrintLine(string Seq, string Line, string EdpCode, string ProductType, string Items, bool IsSubscription, int Qty,
    decimal TotalStp, decimal TotalAchieved, decimal PalRelease, bool IsMain);

/// <summary>A row of the Old CS's variable costs: the label, and per variable cost of that name its cost (0: blank) and remarks.</summary>
public sealed record PrintVariableRow(string Label, IReadOnlyList<decimal> Costs, IReadOnlyList<string> Remarks);

public sealed record PrintVariable(string Name, decimal Cost, string TradeInItem, string Manufacturer, string Model, string SerialNo, string Remarks);

public sealed record PrintComplexGroup(string ProductName, IReadOnlyList<PrintPart> Parts);

public sealed record PrintPacket(string ProductName, decimal AchievedPrice, IReadOnlyList<PrintPart> Parts);

/// <summary>A complex item or packet item: name, EDP code, quantity and (complex items) allocated price.</summary>
public sealed record PrintPart(string ProductName, string EdpCode, int Quantity, decimal AllocatedPrice);

/// <summary>MyOrders' finance calculation (CSCal) of the internal financing boxes; the money as MyOrders works it out.</summary>
public sealed record PrintFinance
{
    public decimal Products { get; init; }
    public decimal ProductsRate { get; init; }
    public decimal NoOfYear { get; init; }
    public decimal DealInterest { get; init; }
    public decimal FinalPayment { get; init; }
    public decimal FinalPaymentRate { get; init; }
    public decimal FinalPaymentInterest { get; init; }
    public int BwNoOfCopies { get; init; }
    public decimal BwRate { get; init; }
    public int BwNoOfMonth { get; init; }
    public decimal BwTotal { get; init; }
    public int ColNoOfCopies { get; init; }
    public decimal ColRate { get; init; }
    public int ColNoOfMonth { get; init; }
    public decimal ColTotal { get; init; }
    public decimal MachineInterest { get; init; }
    public decimal ServiceCredit { get; init; }
    public decimal ProductsWithFinalPayment { get; init; }
    public decimal LeasePayable { get; init; }
    public int NoOfInstal { get; init; }
    public decimal PerMonth { get; init; }
}

public sealed partial class CostingSheets
{
    /// <summary>The statuses at which MyOrders let a costing sheet be printed.</summary>
    public static readonly string[] PrintableStatuses = ["Pending Credit Check", "Pending Credit GM Check", "Pending Invoicing", "Invoiced"];

    /// <summary>The MyCRM roles MyOrders let print (and administrators here).</summary>
    public static readonly string[] PrintRoles =
    [
        CrmRoles.Manager, CrmRoles.Head, CrmRoles.Gm, CrmRoles.PpSpecialistManager, CrmRoles.SpecialistSeniorManager, CrmRoles.StockAllocator,
        CrmRoles.ApManager, CrmRoles.Ap, CrmRoles.CreditGm, CrmRoles.Md, CrmRoles.Admin, CrmRoles.SalesAdmin,
    ];

    /// <summary>MyOrders' rule for its Old CS and New CS links: a printable status and a role that prints.</summary>
    public static bool CanPrint(Actor actor, string status) =>
        PrintableStatuses.Contains(status, StringComparer.OrdinalIgnoreCase) && (actor.IsAdmin || CrmRoles.Is(actor.Orders.Role, PrintRoles));

    /// <summary>The variable cost names of the Old CS's table, in its order (the trade-ins after the subtotal).</summary>
    public static readonly string[] VariableRowNames =
        ["Rollover / Refinancing", "Advertisement & Promotion", "Accessories", "Free Prints", "Service Dept", "Accruals", "Others", "R2R Trade In", "O2R Trade In"];

    /// <summary>What the print layouts show, or null when the person may not see the costing sheet; <see cref="CostingSheetView.CanPrint"/> says whether MyOrders let them print it.</summary>
    public CostingSheetPrint? Print(Actor actor, long id)
    {
        var row = data.Rows(Entities.CostingSheet, "ID", [id]).FirstOrDefault();
        if (row is null) return null;
        var company = One(Entities.Company, row.Id("COMPANYID"));
        if (!access.CanOpen(actor, row.Id("SALESMANID"), company?.Text("COMPANY") ?? "")) return null;
        var sheet = Build(actor, row, company);
        var users = data.Users().GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First().Name);
        string Name(long? user) => user is { } value && users.TryGetValue(value, out var name) ? name : "";

        // MyCRM's teams: the salesperson's code, manager and head.
        var crm = access.CrmUsers();
        var salesman = row.Id("SALESMANID");
        var mine = crm.Where(u => u.UserId == salesman && u.IsActive).ToList();
        var managerTeams = mine.Where(u => u.Role == CrmRoles.Sales && u.TeamId is not null).Select(u => u.TeamId).ToList();
        var manager = crm.Where(u => u.IsActive && u.Role == CrmRoles.Manager && managerTeams.Contains(u.TeamId)).Select(u => (long?)u.UserId).FirstOrDefault();
        var headTeams = mine.Where(u => (u.Role == CrmRoles.Sales || u.Role == CrmRoles.Manager) && u.TeamId is not null).Select(u => u.TeamId).ToList();
        var head = crm.Where(u => u.IsActive && u.Role == CrmRoles.Head && headTeams.Contains(u.TeamId)).Select(u => (long?)u.UserId).FirstOrDefault();

        var contact = One(Entities.Contacts, row.Id("CONTACTID"));
        var phones = row.Id("CONTACTID") is { } contactId
            ? data.Rows(Entities.Phone, "CONTACTID", [contactId]).Where(p => p.Bool("ISACTIVE")).Select(p => p.Text("PHONE")).Where(p => p.Length > 0).Distinct().ToList()
            : new List<string>();
        var billType = sheet.Billing.FinanceType;

        // The items with what the print needs that the page does not show.
        var items = data.Rows(Entities.CostingSheetItem, "COSTINGSHEETID", [id]).Where(r => r.Bool("ISACTIVE")).OrderBy(r => r.Long("ID")).ToList();
        var itemIds = items.Select(r => r.Long("ID")).ToList();
        var subItems = itemIds.Count == 0 ? new List<Dictionary<string, object?>>()
            : data.Rows(Entities.CostingSheetSubItem, "COSTINGSHEETITEMID", itemIds).Where(r => r.Bool("ISACTIVE")).OrderBy(r => r.Long("ID")).ToList();
        var complexItems = itemIds.Count == 0 ? new List<Dictionary<string, object?>>()
            : data.Rows(Entities.CostingSheetComplexItem, "COSTINGSHEETITEMID", itemIds).Where(r => r.Bool("ISACTIVE")).OrderBy(r => r.Long("ID")).ToList();
        var packetIds = items.Where(r => r.Bool("ISPACKET")).Select(r => r.Id("PACKETID")).Where(p => p is not null).Select(p => p!.Value).Distinct().ToList();
        var packetItems = packetIds.Count == 0 ? new List<Dictionary<string, object?>>()
            : data.Rows(Entities.PacketItem, "PACKETID", packetIds).Where(r => r.Bool("ISACTIVE")).OrderBy(r => r.Long("ID")).ToList();
        var productIds = complexItems.Concat(packetItems).Select(r => r.Id("PRODUCTID")).Where(p => p is not null).Select(p => p!.Value).Distinct().ToList();
        var products = productIds.Count == 0 ? new Dictionary<long, Dictionary<string, object?>>()
            : data.Rows(Entities.Product, "ID", productIds).GroupBy(r => r.Long("ID")).ToDictionary(g => g.Key, g => g.First());
        string ProductText(Dictionary<string, object?> part, string column, string productColumn) =>
            part.Text(column).Length > 0 ? part.Text(column)
            : part.Id("PRODUCTID") is { } p && products.TryGetValue(p, out var product) ? product.Text(productColumn) : "";

        string SubText(long itemId) => string.Join(",", subItems.Where(s => s.Long("COSTINGSHEETITEMID") == itemId).Select(s => s.Text("PRODUCTNAME") + " x " + s.Int("QUANTITY")));
        var printItems = items.Select(i => new PrintItem
        {
            Id = i.Long("ID"),
            ProductName = i.Text("PRODUCTNAME"),
            Quantity = i.Int("QUANTITY"),
            AchievedPrice = i.Decimal("ACHIEVEDPRICE"),
            TotalStp = i.Decimal("TOTALSTP"),
            SubItemsText = SubText(i.Long("ID")),
            HasComplex = i.Bool("ISPACKET") || complexItems.Any(c => c.Long("COSTINGSHEETITEMID") == i.Long("ID")),
            InitialBwCopies = i.Int("INITIALBWCOPIES"),
            InitialBwPrice = i.Decimal("INITIALBWPRICE"),
            InitialClrCopies = i.Int("INITIALCLRCOPIES"),
            InitialClrPrice = i.Decimal("INITIALCLRPRICE"),
            MonthlyBwCopies = i.Int("MONTHLYBWCOPIES"),
            MonthlyBwPrice = i.Decimal("MONTHLYBWPRICE"),
            MonthlyClrCopies = i.Int("MONTHLYCLRCOPIES"),
            MonthlyClrPrice = i.Decimal("MONTHLYCLRPRICE"),
            MonthlyMonths = i.Int("MONTHLYMONTHS"),
            ThereafterBwPrice = i.Decimal("THEREAFTERBWPRICE"),
            ThereafterClrPrice = i.Decimal("THEREAFTERCLRPRICE"),
        }).ToList();

        // The New CS's lines: each item (Seq i), then its sub items, packet items and complex items (i.1, i.2…).
        var lines = new List<PrintLine>();
        var seq = 0;
        foreach (var item in items)
        {
            seq++;
            var itemId = item.Long("ID");
            lines.Add(new PrintLine(seq.ToString(System.Globalization.CultureInfo.InvariantCulture), "", item.Text("EDPCODE"), item.Text("PRODUCTTYPE"), item.Text("PRODUCTNAME"),
                item.Bool("ISSUBSCRIPTION"), item.Int("QUANTITY"), item.Decimal("TOTALSTP"), item.Decimal("ACHIEVEDPRICE"),
                item.Int("QUANTITY") * item.Decimal("APPROVEDSPECIALUNITPRICE"), true));
            var sub = 0;
            void Part(Dictionary<string, object?> part, string name, string edp)
            {
                sub++;
                lines.Add(new PrintLine("", $"{seq}.{sub}", edp, part.Text("PRODUCTTYPE"), name, false, part.Int("QUANTITY"), 0m, 0m, 0m, false));
            }
            foreach (var s in subItems.Where(s => s.Long("COSTINGSHEETITEMID") == itemId)) Part(s, s.Text("PRODUCTNAME"), s.Text("EDPCODE"));
            if (item.Bool("ISPACKET") && item.Id("PACKETID") is { } packet)
                foreach (var p in packetItems.Where(p => p.Long("PACKETID") == packet)) Part(p, ProductText(p, "PRODUCTNAME", "NAME"), ProductText(p, "EDPCODE", "EDPCODE"));
            foreach (var c in complexItems.Where(c => c.Long("COSTINGSHEETITEMID") == itemId)) Part(c, ProductText(c, "PRODUCTNAME", "NAME"), ProductText(c, "EDPCODE", "EDPCODE"));
        }

        // Variable costs: per name for the Old CS, every row for the New CS.
        var variableNames = Labels(Entities.VariableName);
        var variables = data.Rows(Entities.Variable, "COSTINGSHEETID", [id]).OrderBy(r => r.Long("ID"))
            .Select(v => new { Name = Label(variableNames, v.Id("VARIABLENAMEID")), Row = v }).ToList();
        static string Remarks(Dictionary<string, object?> v) =>
            string.Join(",", new[] { v.Text("TRADEINITEM"), v.Text("MANUFACTURER"), v.Text("MODEL"), v.Text("SERIALNO"), v.Text("REMARKS") }.Where(t => t.Length > 0));
        var variableRows = VariableRowNames.Select(name =>
        {
            var of = variables.Where(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            return new PrintVariableRow(name, of.Select(v => v.Row.Decimal("COST")).ToList(), of.Select(v => Remarks(v.Row)).ToList());
        }).ToList();

        // MyOrders' NoOfCopiesFOCMonthly: the items grouped by their monthly rates and months, in the items' order.
        var groups = items.GroupBy(i => (Clr: i.Decimal("MONTHLYCLRPRICE"), Months: i.Int("MONTHLYMONTHS"), Bw: i.Decimal("MONTHLYBWPRICE")))
            .Select(g => (g.Key, Bw: g.Sum(i => i.Int("MONTHLYBWCOPIES") * i.Int("QUANTITY")), Clr: g.Sum(i => i.Int("MONTHLYCLRCOPIES") * i.Int("QUANTITY")))).ToList();
        var isPpp = sheet.Billing.IsPpp;
        var cs = row;
        var finance = Finance(cs, isPpp, groups.Count > 0 ? groups[0] : ((0m, 0, 0m), 0, 0),
            items.Sum(i => i.Int("MONTHLYBWCOPIES") * i.Int("MONTHLYMONTHS") * (isPpp ? cs.Decimal("PPPINTERNALBWRATE") : i.Decimal("MONTHLYBWPRICE") / 100m) * i.Int("QUANTITY")),
            items.Sum(i => i.Int("MONTHLYCLRCOPIES") * i.Int("MONTHLYMONTHS") * (isPpp ? cs.Decimal("PPPINTERNALCOLOURRATE") : i.Decimal("MONTHLYCLRPRICE") / 100m) * i.Int("QUANTITY")));

        return new CostingSheetPrint
        {
            Sheet = sheet,
            ReferenceWithId = sheet.Reference + "-" + id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Brn = company is null ? "" : company.Text("INCCERTNO").Length > 0 ? company.Text("INCCERTNO") : company.Text("BRN"),
            CompanyCreditTerms = company?.Text("CREDITTERMS") ?? "",
            Contact = contact is null ? "" : string.Join(" ", new[] { contact.Text("SALUTATION"), contact.Text("FIRSTNAME"), contact.Text("MIDDLENAME"), contact.Text("LASTNAME") }.Where(t => t.Length > 0)),
            ContactPhone = string.Join(" / ", phones),
            SalesmanName = Name(salesman),
            SalesmanCode = mine.Select(u => u.SalesmanCode).FirstOrDefault(c => c.Length > 0) ?? "",
            ManagerName = Name(manager),
            HeadName = Name(head),
            SpecialistNames = new[] { row.Id("MDSSPECIALISTID"), row.Id("MITSSPECIALISTID"), row.Id("PPSPECIALISTID") }.Where(u => u is not null).Select(Name).Where(n => n.Length > 0).ToList(),
            CreatedAt = row.Date("CREATEDAT"),
            ApprovedAt = row.Date("APPROVEDAT"),
            IsOutrightSale = string.Equals(billType, "Outright Sale", StringComparison.OrdinalIgnoreCase),
            IsInternalFinancing = billType == InternalFinancing,
            IsExternalFinancing = billType == ExternalFinancing,
            IsVitalCase = row.Bool("ISVITALCASE"),
            IsEpp = row.Bool("ISEPP"),
            IsNovation = row.Bool("ISNOVATION"),
            IsZba = row.Bool("ISZBA"),
            TotalVariableCost = row.Decimal("TOTALVARIABLECOST"),
            InvoiceNo = row.Text("INVOICENO"),
            BillToName = Label(Labels(Entities.ExternalFinanceCompany, "NAME"), row.Id("BILLTO")),
            Items = printItems,
            Lines = lines,
            VariableRows = variableRows,
            SubtotalBeforeTradeIn = variables.Where(v => !TradeInVariables.Contains(v.Name, StringComparer.OrdinalIgnoreCase)).Sum(v => v.Row.Decimal("COST")),
            Variables = variables.Select(v => new PrintVariable(v.Name, v.Row.Decimal("COST"), v.Row.Text("TRADEINITEM"), v.Row.Text("MANUFACTURER"), v.Row.Text("MODEL"),
                v.Row.Text("SERIALNO"), v.Row.Text("REMARKS"))).ToList(),
            Finance = finance,
            RatesDiffer = groups.Count > 1 && !isPpp,
            ComplexGroups = items.Where(i => complexItems.Any(c => c.Long("COSTINGSHEETITEMID") == i.Long("ID")))
                .Select(i => new PrintComplexGroup(i.Text("PRODUCTNAME"), complexItems.Where(c => c.Long("COSTINGSHEETITEMID") == i.Long("ID"))
                    .Select(c => new PrintPart(ProductText(c, "PRODUCTNAME", "NAME"), ProductText(c, "EDPCODE", "EDPCODE"), c.Int("QUANTITY"), c.Decimal("ACHIEVEDPRICE"))).ToList()))
                .ToList(),
            Packets = items.Where(i => i.Bool("ISPACKET"))
                .Select(i => new PrintPacket(i.Text("PRODUCTNAME"), i.Decimal("ACHIEVEDPRICE"), i.Id("PACKETID") is { } packet
                    ? packetItems.Where(p => p.Long("PACKETID") == packet).Select(p => new PrintPart(ProductText(p, "PRODUCTNAME", "NAME"), ProductText(p, "EDPCODE", "EDPCODE"), p.Int("QUANTITY"), 0m))
                        .OrderBy(p => p.ProductName, StringComparer.OrdinalIgnoreCase).ToList()
                    : new List<PrintPart>()))
                .ToList(),
            IsLong = printItems.Any(i => i.SubItemsText.Length > 200),
        };
    }

    /// <summary>
    /// MyOrders' CSCal: deal interest, final payment interest, service credit and the lease payable, and the amount per
    /// month (the stored monthly lease when it is within a dollar of the one worked out).
    /// </summary>
    public static PrintFinance Finance(IReadOnlyDictionary<string, object?> cs, bool isPpp, ((decimal Clr, int Months, decimal Bw) Key, int Bw, int Clr) firstGroup,
        decimal serviceCreditBw, decimal serviceCreditClr)
    {
        var months = cs.Int("MONTHS");
        var products = cs.Decimal("TOTALACHIEVEDPRICE") + cs.Decimal("TOTALVARIABLECOST") - cs.Decimal("FINALPAYMENT") - cs.Decimal("DOWNPAYMENT");
        var rate = cs.Decimal("INTERESTRATE");
        var years = months / 12.000m;
        var dealInterest = products * (rate / 100m) * years;
        var finalPayment = cs.Decimal("FINALPAYMENT");
        var finalPaymentInterest = finalPayment * (rate * 2 / 100m) * years;
        var bwCopies = isPpp ? cs.Int("PPPMONTHLYBWVOLUME") : firstGroup.Bw;
        var bwRate = isPpp ? cs.Decimal("PPPINTERNALBWRATE") : firstGroup.Key.Bw / 100m;
        var bwMonths = isPpp ? months : firstGroup.Key.Months;
        var colCopies = isPpp ? cs.Int("PPPMONTHLYCOLOURVOLUME") : firstGroup.Clr;
        var colRate = isPpp ? cs.Decimal("PPPINTERNALCOLOURRATE") : firstGroup.Key.Clr / 100m;
        var colMonths = isPpp ? months : firstGroup.Key.Months;
        var bwTotal = bwCopies * bwRate * bwMonths;
        var colTotal = colCopies * colRate * colMonths;
        var machineInterest = dealInterest + finalPaymentInterest;
        var serviceCredit = isPpp ? bwTotal + colTotal : serviceCreditBw + serviceCreditClr;
        var productsWithFinalPayment = cs.Decimal("TOTALACHIEVEDPRICE") + cs.Decimal("TOTALVARIABLECOST") - cs.Decimal("DOWNPAYMENT");
        var leasePayable = machineInterest + serviceCredit + productsWithFinalPayment;
        var computed = months == 0 ? 0m : (leasePayable - finalPayment) / months;
        var monthlyLease = cs.Decimal("MONTHLYLEASE");
        return new PrintFinance
        {
            Products = products,
            ProductsRate = rate,
            NoOfYear = years,
            DealInterest = dealInterest,
            FinalPayment = finalPayment,
            FinalPaymentRate = rate * 2,
            FinalPaymentInterest = finalPaymentInterest,
            BwNoOfCopies = bwCopies,
            BwRate = bwRate,
            BwNoOfMonth = bwMonths,
            BwTotal = bwTotal,
            ColNoOfCopies = colCopies,
            ColRate = colRate,
            ColNoOfMonth = colMonths,
            ColTotal = colTotal,
            MachineInterest = machineInterest,
            ServiceCredit = serviceCredit,
            ProductsWithFinalPayment = productsWithFinalPayment,
            LeasePayable = leasePayable,
            NoOfInstal = months,
            PerMonth = Math.Abs(computed - monthlyLease) <= 1m ? monthlyLease : computed,
        };
    }
}
