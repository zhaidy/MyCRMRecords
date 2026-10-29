using System.Globalization;
using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>
/// MyOrders' costing sheets, read only: the list (MyOrders' CostingSheets screen) of the costing sheets the person may
/// see (<see cref="OrdersAccess"/>), a costing sheet's page (its CostingSheetSummary) and its attachments. Nothing here writes:
/// MyCRMRecords keeps the costing sheets' history readable once MyOrders is retired.
/// </summary>
public sealed partial class CostingSheets(IMyOrdersData data, OrdersAccess access)
{
    // Static entities of MyOrders, matched by label (never by Id: the Ids differ between environments).
    public const string InternalFinancing = "Internal Financing";
    public const string ExternalFinancing = "External Financing";
    public static readonly string[] TradeInVariables = ["R2R Trade In", "O2R Trade In"];
    /// <summary>Product groups whose configurations have no meter charges (MyOrders' Meter Charges box).</summary>
    public static readonly string[] NoMeterGroups = ["MDS Services", "MITS Solutions", "Production Printing Solutions"];

    /// <summary>A page of the costing sheets the person may see, matching the search (newest first by default).</summary>
    public CostingSheetPage List(Actor actor, CostingSheetSearch search)
    {
        var (salesmen, company) = access.Scope(actor);
        search.SalesmanIds = salesmen;
        search.CompanyContains = company;
        // The Sales Rep or specialist chosen, with their other OutSystems user when OutSystems keeps them twice.
        if (search.SalesRepIds is { } reps) search.SalesRepIds = access.SamePerson(reps);
        if (search.SpecialistIds is { } specialists) search.SpecialistIds = access.SamePerson(specialists);
        var page = data.Search(search);
        return page with { Rows = page.Rows.Select(r => r with { CanPrint = CanPrint(actor, r.Status) }).ToList() };
    }

    /// <summary>A choice of a filter: its Id and label.</summary>
    public sealed record Choice(long Id, string Label);

    /// <summary>The choices of the list's filters: statuses, and the sales reps and specialists of the costing sheets the person may see.</summary>
    public (IReadOnlyList<Choice> Statuses, IReadOnlyList<Choice> SalesReps, IReadOnlyList<Choice> Specialists) Filters(Actor actor)
    {
        var statuses = data.All(Entities.CostingSheetStatus).OrderBy(r => r.Int("ORDER")).ThenBy(r => r.Long("ID"))
            .Select(r => new Choice(r.Long("ID"), r.Text("LABEL"))).ToList();
        var people = data.People();
        var names = data.Users().GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First().Name);
        var (salesmen, _) = access.Scope(actor);
        // One choice per person: a person OutSystems keeps twice (RSP\name and RSP\name@ricoh.sg) is listed once, under
        // their first user, and choosing them finds the costing sheets of both (List).
        IReadOnlyList<Choice> Named(IEnumerable<long> ids) => ids.Select(id => access.SamePerson(new[] { id }).Min()).Distinct()
            .Select(id => new Choice(id, names.TryGetValue(id, out var name) && name.Length > 0 ? name : "#" + id.ToString(CultureInfo.InvariantCulture)))
            .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase).ToList();
        return (statuses, Named(salesmen is null ? people.Salesmen : people.Salesmen.Where(id => salesmen.Contains(id))), Named(people.Specialists));
    }

    /// <summary>The costing sheet's page, or null when it is not there or the person may not see it.</summary>
    public CostingSheetView? Get(Actor actor, long id)
    {
        var row = data.Rows(Entities.CostingSheet, "ID", [id]).FirstOrDefault();
        if (row is null) return null;
        var company = One(Entities.Company, row.Id("COMPANYID"));
        if (!access.CanOpen(actor, row.Id("SALESMANID"), company?.Text("COMPANY") ?? "")) return null;
        return Build(actor, row, company);
    }

    /// <summary>One attachment's file (name and bytes), when the person may see its costing sheet; else null.</summary>
    public (string FileName, byte[] Content)? Attachment(Actor actor, long costingSheetId, long attachmentId)
    {
        var attachment = data.Rows(Entities.CostingSheetAttachment, "ID", [attachmentId]).FirstOrDefault();
        if (attachment is null || attachment.Long("COSTINGSHEETID") != costingSheetId) return null;
        var row = data.Rows(Entities.CostingSheet, "ID", [costingSheetId]).FirstOrDefault();
        if (row is null) return null;
        var company = One(Entities.Company, row.Id("COMPANYID"));
        if (!access.CanOpen(actor, row.Id("SALESMANID"), company?.Text("COMPANY") ?? "")) return null;
        var bytes = data.File(Entities.CostingSheetAttachment, "BINARY", attachmentId);
        if (bytes is null) return null;
        var name = attachment.Text("FILENAME");
        return (name.Length > 0 ? name : "attachment-" + attachmentId.ToString(CultureInfo.InvariantCulture), bytes);
    }

    /// <summary>Whether the person sees the margins and commissions: MyCRM's Head, Admin and SalesAdmin, and administrators here.</summary>
    public static bool SeesMargins(Actor actor) => actor.IsAdmin || CrmRoles.Is(actor.Orders.Role, CrmRoles.Head, CrmRoles.Admin, CrmRoles.SalesAdmin);

    private Dictionary<string, object?>? One(OsEntity entity, long? id) =>
        id is { } value ? data.Rows(entity, "ID", [value]).FirstOrDefault() : null;

    private Dictionary<long, string> Labels(OsEntity entity, string column = "LABEL") =>
        data.All(entity).GroupBy(r => r.Long("ID")).ToDictionary(g => g.Key, g => g.First().Text(column));

    private static string Label(Dictionary<long, string> labels, long? id) => id is { } value && labels.TryGetValue(value, out var label) ? label : "";

    private CostingSheetView Build(Actor actor, Dictionary<string, object?> cs, Dictionary<string, object?>? company)
    {
        var id = cs.Long("ID");
        var users = data.Users().GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First().Name);
        string User(string column) => cs.Id(column) is { } user && users.TryGetValue(user, out var name) ? name : "";
        string UserOf(long? user) => user is { } value && users.TryGetValue(value, out var name) ? name : "";

        var address = One(Entities.Address, cs.Id("ADDRESSID"));
        var contact = One(Entities.Contacts, cs.Id("CONTACTID"));
        var reference = One(Entities.Reference, cs.Id("REFERENCEID"));
        var billType = Label(Labels(Entities.BillType), cs.Id("BILLTYPEID"));
        var isFinancing = billType == InternalFinancing || billType == ExternalFinancing;
        var isPpp = cs.Bool("ISPPP");

        var specialists = new List<CostingSheetSpecialist>();
        void Specialist(string role, string column, string pending)
        {
            if (cs.Id(column) is { } user) specialists.Add(new CostingSheetSpecialist(role, UserOf(user), cs.Bool(pending)));
        }
        Specialist("PP Specialist", "PPSPECIALISTID", "PENDINGPP");
        Specialist("MDS Specialist", "MDSSPECIALISTID", "PENDINGMDS");
        Specialist("Fusion Specialist", "MITSSPECIALISTID", "PENDINGMITS");

        var items = Items(id);
        var variableNames = Labels(Entities.VariableName);
        var variables = data.Rows(Entities.Variable, "COSTINGSHEETID", [id]).OrderBy(r => r.Long("VARIABLENAMEID")).ThenBy(r => r.Long("ID")).ToList();
        var requestFormId = cs.Id("REQUESTFORMID")
            ?? data.Rows(Entities.RequestForm, "COSTINGSHEETID", [id]).Where(r => r.Bool("ISACTIVE")).OrderBy(r => r.Long("ID")).Select(r => (long?)r.Long("ID")).FirstOrDefault();

        var status = Label(Labels(Entities.CostingSheetStatus), cs.Id("COSTINGSHEETSTATUSID"));
        return new CostingSheetView
        {
            Id = id,
            CanPrint = CanPrint(actor, status),
            IsActive = cs.Bool("ISACTIVE"),
            DeletedAt = cs.Date("DELETEDAT"),
            DeletedBy = User("DELETEDBY"),
            Reference = reference?.Text("REFERENCE") ?? "",
            Salesman = User("SALESMANID"),
            Status = status,
            Specialists = specialists,
            CreatedAt = cs.Date("CREATEDAT"),
            UpdatedAt = cs.Date("UPDATEDAT"),
            RecallAt = cs.Date("RECALLAT"),
            CompanyId = cs.Id("COMPANYID"),
            Company = company?.Text("COMPANY") ?? "",
            Address = AddressText(address),
            Contact = ContactText(contact),
            CustomerNo = cs.Text("CUSTOMERNO"),
            CustomerType = company?.Id("COMPANYGROUPID") is { } group ? Label(Labels(Entities.CompanyGroup), group) : "",
            IsZba = cs.Bool("ISZBA"),
            IsContractExtension = cs.Bool("ISCONTRACTEXTENSION"),
            IsFixedTerm = cs.Bool("ISFIXEDTERMAGREEMENT"),
            IsVitalPricing = cs.Bool("ISVITALCASE"),
            GmaPricing = cs.Bool("GMAPRICING"),
            TransferredOn = cs.Date("TRANSFERREDON"),
            OriginalSalesman = User("ORIGINALSALESMANID"),
            EventTag = EventTag(cs.Id("EVENTTAGID")),
            Attachments = data.Rows(Entities.CostingSheetAttachment, "COSTINGSHEETID", [id])
                .Select(r => new CostingSheetAttachmentView(r.Long("ID"), r.Text("FILENAME"), r.Text("REMARKS"), r.Long("BINARY"), UserOf(r.Id("UPLOADEDBY")),
                    r.Date("UPLOADEDON"), r.Bool("ISCOMPLEX")))
                .OrderBy(a => a.IsComplex).ThenBy(a => a.Id).ToList(),
            RequestFormId = requestFormId,
            Differences = requestFormId is { } form ? Differences(items, form) : Array.Empty<CostingSheetDifference>(),
            Items = items,
            TotalStp = cs.Decimal("TOTALSTP"),
            TotalAchievedPrice = cs.Decimal("TOTALACHIEVEDPRICE"),
            CommissionableRevenue = cs.Decimal("COMMISSIONABLEREVENUE"),
            Margins = SeesMargins(actor) ? new CostingSheetMargins
            {
                PercentageAboveManagerPrice = cs.Decimal("PERCENTAGEABOVEMANAGERPRICE"),
                PercentageAboveLanded = cs.Decimal("PERCENTAGEABOVEFOB"),
                DealMargin = cs.Decimal("TOTALACHIEVEDPRICE") > 0
                    ? Math.Round((cs.Decimal("TOTALACHIEVEDPRICE") - cs.Decimal("TOTALFOB")) / cs.Decimal("TOTALACHIEVEDPRICE") * 100m, 2) : 0m,
                TotalManagerPrice = cs.Decimal("TOTALMANAGERPRICE"),
                TotalLanded = cs.Decimal("TOTALFOB"),
                EstimatedCommission = cs.Decimal("ESTIMATEDCOMMISSION"),
                ConfirmedCommission = cs.Decimal("CONFIRMEDCOMMISSION"),
            } : null,
            Billing = new CostingSheetBilling
            {
                IsPpp = isPpp,
                FinanceType = billType,
                FinanceOutrightAmount = cs.Decimal("FINANCEOUTRIGHTAMOUNT"),
                IsFinancing = isFinancing,
                Months = cs.Int("MONTHS"),
                InterestRate = cs.Decimal("INTERESTRATE"),
                DownPayment = cs.Decimal("DOWNPAYMENT"),
                FinalPercentage = cs.Decimal("FINALPERCENTAGE"),
                FinalPayment = cs.Decimal("FINALPAYMENT"),
                MonthlyLabel = isPpp ? "Hardware Monthly" : "Monthly Investment",
                MonthlyLease = cs.Decimal("MONTHLYLEASE"),
                ShowsPppRates = isPpp && billType == InternalFinancing,
                PppInternalBwRate = cs.Decimal("PPPINTERNALBWRATE"),
                PppInternalColourRate = cs.Decimal("PPPINTERNALCOLOURRATE"),
                PppCustomerBwRate = cs.Decimal("PPPCUSTOMERBWRATE"),
                PppCustomerColourRate = cs.Decimal("PPPCUSTOMERCOLOURRATE"),
                PppMonthlyBwVolume = cs.Int("PPPMONTHLYBWVOLUME"),
                PppMonthlyColourVolume = cs.Int("PPPMONTHLYCOLOURVOLUME"),
                IsExternalFinancing = billType == ExternalFinancing,
                BillTo = billType == ExternalFinancing ? Label(Labels(Entities.ExternalFinanceCompany, "NAME"), cs.Id("BILLTO")) : "",
                LeaseAgreementNumber = cs.Text("LEASEAGREEMENTNUMBER"),
                TransactionType = Label(Labels(Entities.TransactionType), cs.Id("TRANSACTIONTYPEID")),
                TradeInTo = Label(Labels(Entities.TradeInTo), cs.Id("TRADEINTOID")),
                BillingAddress = billType == ExternalFinancing ? "" : AddressText(One(Entities.Address, cs.Id("BILLINGADDRESS"))),
                Remarks = cs.Text("REMARKS"),
            },
            Variables = variables.Select(v => new CostingSheetVariable(Label(variableNames, v.Id("VARIABLENAMEID")), v.Decimal("COST"), v.Text("REMARKS"))).ToList(),
            TradeIns = variables.Where(v => TradeInVariables.Contains(Label(variableNames, v.Id("VARIABLENAMEID")), StringComparer.OrdinalIgnoreCase))
                .Select(v => new CostingSheetTradeIn(v.Text("TRADEINITEM"), v.Text("MANUFACTURER"), v.Text("MODEL"), v.Text("SERIALNO"), v.Decimal("COST"),
                    Label(variableNames, v.Id("VARIABLENAMEID")), v.Text("REMARKS"))).ToList(),
            History = History(cs, User),
            Facts = new CostingSheetFacts
            {
                ProposalId = cs.Id("PROPOSALID"),
                OptionId = cs.Id("OPTIONID"),
                OrderNo = cs.Text("ORDERNO"),
                InvoiceNo = cs.Text("INVOICENO"),
                InvoiceDates = cs.Text("INVOICEDATETEXT"),
                CreditTerms = cs.Text("CREDITTERMS"),
                TotalApprovedPrice = cs.Decimal("TOTALAPPROVEDPRICE"),
                TotalVariableCost = cs.Decimal("TOTALVARIABLECOST"),
                IsEpp = cs.Bool("ISEPP"),
                IsNovation = cs.Bool("ISNOVATION"),
                IsOpoChecked = cs.Bool("ISOPOCHECKED"),
                IsPoCompleted = cs.Bool("ISPOCOMPLETED"),
                IsReceiptCompleted = cs.Bool("ISRECEIPTCOMPLETED"),
                ContactName = cs.Text("CONTACTNAME"),
                Phone = cs.Text("PHONE"),
                Email = cs.Text("EMAIL"),
                Fax = cs.Text("FAX"),
                DeliverTo = Join(cs.Text("DELIVERTOSTREET"), cs.Text("DELIVERTOCITY"), cs.Text("DELIVERTOSTATEPROVINCE"), cs.Text("DELIVERTOCOUNTRY"),
                    cs.Text("DELIVERTOPOSTALCODE")),
            },
        };
    }

    /// <summary>The active items in MyOrders' order (by Id) with their active sub items, complex items, packet items and meter charges.</summary>
    private IReadOnlyList<CostingSheetItemView> Items(long costingSheetId)
    {
        var items = data.Rows(Entities.CostingSheetItem, "COSTINGSHEETID", [costingSheetId]).Where(r => r.Bool("ISACTIVE")).OrderBy(r => r.Long("ID")).ToList();
        if (items.Count == 0) return Array.Empty<CostingSheetItemView>();
        var itemIds = items.Select(r => r.Long("ID")).ToList();
        var subItems = data.Rows(Entities.CostingSheetSubItem, "COSTINGSHEETITEMID", itemIds).Where(r => r.Bool("ISACTIVE")).OrderBy(r => r.Long("ID")).ToList();
        var complexItems = data.Rows(Entities.CostingSheetComplexItem, "COSTINGSHEETITEMID", itemIds).Where(r => r.Bool("ISACTIVE")).ToList();
        var productIds = items.Concat(complexItems).Select(r => r.Id("PRODUCTID")).Where(p => p is not null).Select(p => p!.Value).Distinct().ToList();
        var products = productIds.Count == 0 ? new Dictionary<long, Dictionary<string, object?>>()
            : data.Rows(Entities.Product, "ID", productIds).GroupBy(r => r.Long("ID")).ToDictionary(g => g.Key, g => g.First());
        var groupIds = products.Values.Select(p => p.Id("PRODUCTGROUPID")).Where(g => g is not null).Select(g => g!.Value).Distinct().ToList();
        var groups = groupIds.Count == 0 ? new Dictionary<long, string>()
            : data.Rows(Entities.ProductGroup, "ID", groupIds).GroupBy(r => r.Long("ID")).ToDictionary(g => g.Key, g => g.First().Text("NAME"));
        var packetIds = items.Where(r => r.Bool("ISPACKET")).Select(r => r.Id("PACKETID")).Where(p => p is not null).Select(p => p!.Value).Distinct().ToList();
        var packetItems = packetIds.Count == 0 ? new List<Dictionary<string, object?>>()
            : data.Rows(Entities.PacketItem, "PACKETID", packetIds).Where(r => r.Bool("ISACTIVE")).OrderBy(r => r.Long("ID")).ToList();

        return items.Select(item =>
        {
            var itemId = item.Long("ID");
            var product = item.Id("PRODUCTID") is { } p && products.TryGetValue(p, out var found) ? found : null;
            var group = product?.Id("PRODUCTGROUPID") is { } g && groups.TryGetValue(g, out var name) ? name : "";
            var type = item.Text("PRODUCTTYPE");
            var isPacket = item.Bool("ISPACKET");
            var hasMeter = (type == "Configuration" || type == "Mainframe" || isPacket) && !NoMeterGroups.Contains(group, StringComparer.OrdinalIgnoreCase)
                && !item.Bool("ISMDS") && !item.Bool("ISSOL");
            return new CostingSheetItemView
            {
                Id = itemId,
                ProductName = item.Text("PRODUCTNAME"),
                ProductType = type,
                ProductGroup = group,
                EdpCode = item.Text("EDPCODE"),
                Quantity = item.Int("QUANTITY"),
                UnitPrice = item.Decimal("UNITPRICE"),
                TotalStp = item.Decimal("TOTALSTP"),
                AchievedPrice = item.Decimal("ACHIEVEDPRICE"),
                IsSubscription = item.Bool("ISSUBSCRIPTION"),
                OaaPricing = item.Bool("OAAPRICING"),
                IsPacket = isPacket,
                Model = item.Text("MODEL"),
                Serial = item.Text("SERIAL"),
                SubItems = subItems.Where(s => s.Long("COSTINGSHEETITEMID") == itemId)
                    .Select(s => new CostingSheetSubItemView(s.Text("PRODUCTNAME"), s.Text("EDPCODE"), s.Int("QUANTITY"), s.Decimal("UNITPRICE"))).ToList(),
                // A complex item's name is its own, else its product's; sorted by that name as MyOrders does.
                ComplexItems = complexItems.Where(c => c.Long("COSTINGSHEETITEMID") == itemId)
                    .Select(c => new CostingSheetComplexItemView(
                        c.Text("PRODUCTNAME").Length > 0 ? c.Text("PRODUCTNAME") : c.Id("PRODUCTID") is { } cp && products.TryGetValue(cp, out var pr) ? pr.Text("NAME") : "",
                        c.Text("EDPCODE"), c.Text("MPGCODE"), c.Int("QUANTITY"), c.Decimal("STP"), c.Decimal("ACHIEVEDPRICE")))
                    .OrderBy(c => c.ProductName, StringComparer.OrdinalIgnoreCase).ToList(),
                PacketItems = isPacket && item.Id("PACKETID") is { } packet
                    ? packetItems.Where(pi => pi.Long("PACKETID") == packet).Select(pi => new CostingSheetPacketItemView(pi.Text("PRODUCTNAME"), pi.Int("QUANTITY"))).ToList()
                    : Array.Empty<CostingSheetPacketItemView>(),
                Meter = hasMeter ? new CostingSheetMeter
                {
                    IsFsa = item.Bool("ISFSA"),
                    // MyOrders read Below GM CPC from the Product box's list (a bug); each item's own value is shown here.
                    BelowGmCpc = item.Bool("BELOWGMCPC"),
                    BlackAndWhiteOnly = item.Bool("METERBWONLY"),
                    InitialBwCopies = item.Int("INITIALBWCOPIES"),
                    InitialBwPrice = item.Decimal("INITIALBWPRICE"),
                    InitialClrCopies = item.Int("INITIALCLRCOPIES"),
                    InitialClrPrice = item.Decimal("INITIALCLRPRICE"),
                    MonthlyBwCopies = item.Int("MONTHLYBWCOPIES"),
                    MonthlyBwPrice = item.Decimal("MONTHLYBWPRICE"),
                    MonthlyClrCopies = item.Int("MONTHLYCLRCOPIES"),
                    MonthlyClrPrice = item.Decimal("MONTHLYCLRPRICE"),
                    MonthlyMonths = item.Int("MONTHLYMONTHS"),
                    ThereafterBwPrice = item.Decimal("THEREAFTERBWPRICE"),
                    ThereafterClrPrice = item.Decimal("THEREAFTERCLRPRICE"),
                } : null,
            };
        }).ToList();
    }

    /// <summary>
    /// MyOrders' CompareCostingRequest, worked out without writing anything: the quantity of each EDP code on the
    /// costing sheet (active items, sub items and complex items) and on the request form (likewise), where they differ.
    /// </summary>
    private IReadOnlyList<CostingSheetDifference> Differences(IReadOnlyList<CostingSheetItemView> items, long requestFormId)
    {
        var sheet = new Dictionary<string, (string Name, int Quantity)>(StringComparer.OrdinalIgnoreCase);
        var form = new Dictionary<string, (string Name, int Quantity)>(StringComparer.OrdinalIgnoreCase);
        void Add(Dictionary<string, (string Name, int Quantity)> sums, string code, string name, int quantity)
        {
            code = code.Trim();
            if (code.Length == 0) return;
            sums[code] = sums.TryGetValue(code, out var had) ? (had.Name, had.Quantity + quantity) : (name, quantity);
        }
        foreach (var item in items)
        {
            Add(sheet, item.EdpCode, item.ProductName, item.Quantity);
            foreach (var sub in item.SubItems) Add(sheet, sub.EdpCode, sub.ProductName, sub.Quantity);
            foreach (var complex in item.ComplexItems) Add(sheet, complex.EdpCode, complex.ProductName, complex.Quantity);
        }
        var formItems = data.Rows(Entities.RequestFormItem, "REQUESTFORMID", [requestFormId]).Where(r => r.Bool("ISACTIVE")).ToList();
        var formItemIds = formItems.Select(r => r.Long("ID")).ToList();
        var formRows = formItems
            .Concat(formItemIds.Count == 0 ? Enumerable.Empty<Dictionary<string, object?>>()
                : data.Rows(Entities.RequestFormSubItem, "REQUESTFORMITEMID", formItemIds).Where(r => r.Bool("ISACTIVE")))
            .Concat(formItemIds.Count == 0 ? Enumerable.Empty<Dictionary<string, object?>>()
                : data.Rows(Entities.RequestFormComplexItem, "REQUESTFORMITEMID", formItemIds).Where(r => r.Bool("ISACTIVE")));
        foreach (var r in formRows) Add(form, r.Text("EDPCODE"), r.Text("PRODUCTNAME"), r.Int("QUANTITY"));
        return sheet.Keys.Union(form.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(code => new CostingSheetDifference(code,
                sheet.TryGetValue(code, out var s) ? s.Name : form[code].Name,
                sheet.TryGetValue(code, out var sq) ? sq.Quantity : 0,
                form.TryGetValue(code, out var fq) ? fq.Quantity : 0))
            .Where(d => d.CostingSheetQuantity != d.RequestFormQuantity)
            .OrderBy(d => d.EdpCode, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The costing sheet's history from the dates and people MyOrders keeps on it (a step whose date is empty is left out).</summary>
    private static IReadOnlyList<CostingSheetStep> History(Dictionary<string, object?> cs, Func<string, string> user)
    {
        var steps = new List<CostingSheetStep>();
        void Step(string step, string dateColumn, string byColumn = "", string detail = "")
        {
            if (cs.Date(dateColumn) is { } at) steps.Add(new CostingSheetStep(step, at, byColumn.Length > 0 ? user(byColumn) : "", detail));
        }
        Step("Created", "CREATEDAT", "CREATEDBY");
        Step("Approved", "APPROVEDAT", "APPROVEDBY");
        Step("Head approved", "HEADAPPROVEDAT");
        Step("Sent for credit check", "PENDINGCREDITON");
        Step("Credit endorsed", "CREDITENDORSEDON");
        Step("OPO checked", "OPOCHECKEDON", "OPOCHECKEDBY");
        Step("Pending invoicing", "PENDINGINVOICINGON");
        Step("Order number entered", "ORDERUPDATEDON", "ORDERUPDATEDBY", cs.Text("ORDERNO"));
        Step("Invoiced", "MININVOICEDATE", "", cs.Text("INVOICENO"));
        Step("Printed", "PRINTEDAT");
        Step("Recalled", "RECALLAT");
        Step("Transferred", "TRANSFERREDON", "", user("ORIGINALSALESMANID") is { Length: > 0 } from ? $"From {from} to {user("SALESMANID")}" : "");
        Step("Deleted", "DELETEDAT", "DELETEDBY");
        Step("Last updated", "UPDATEDAT", "UPDATEDBY");
        return steps.OrderBy(s => s.At).ToList();
    }

    /// <summary>The event tag's name (MyCRM's EventTag), or "" when there is none or it cannot be read.</summary>
    private string EventTag(long? id)
    {
        if (id is null) return "";
        try
        {
            var row = One(Entities.EventTag, id);
            if (row is null) return "";
            var label = row.Text("LABEL");
            return label.Length > 0 ? label : row.Text("NAME");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Data.Common.DbException)
        {
            return "";
        }
    }

    /// <summary>
    /// An address of MyCRM as one line: block, street, unit and building, then "Singapore" and the postal code, or the
    /// city, state and country for an address outside Singapore.
    /// </summary>
    public static string AddressText(IReadOnlyDictionary<string, object?>? address)
    {
        if (address is null) return "";
        var country = address.Text("COUNTRY");
        var local = country.Length == 0 || country.IndexOf("Singapore", StringComparison.OrdinalIgnoreCase) >= 0;
        return local
            ? Join(address.Text("BLOCK"), address.Text("STREET"), address.Text("UNITNUMBER"), address.Text("BUILDINGNAME"), "Singapore " + address.Text("POSTALCODE"))
            : Join(address.Text("BLOCK"), address.Text("STREET"), address.Text("UNITNUMBER"), address.Text("BUILDINGNAME"), address.Text("CITY"),
                address.Text("STATEPROVINCE"), country, address.Text("POSTALCODE"));
    }

    /// <summary>A contact of MyCRM as a name: first, middle and last name.</summary>
    public static string ContactText(IReadOnlyDictionary<string, object?>? contact) =>
        contact is null ? "" : Join(contact.Text("FIRSTNAME"), contact.Text("MIDDLENAME"), contact.Text("LASTNAME"));

    private static string Join(params string[] parts) => string.Join(" ", parts.Select(p => (p ?? "").Trim()).Where(p => p.Length > 0));
}
