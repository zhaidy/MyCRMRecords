using System.Globalization;
using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>
/// MyOrders' request forms, read only: the list (MyOrders' RequestForms screen, with what its specialists' list showed)
/// of the request forms the person may see (<see cref="OrdersAccess.RequestFormsScope"/>), a request form's page (its
/// RequestFormSummary), its attachments and its print (MyOrders' delivery form and collection form). Nothing here writes.
/// </summary>
public sealed class RequestForms(IMyOrdersData data, OrdersAccess access)
{
    // Static entities of MyOrders, matched by label (never by Id: the Ids differ between environments).
    public const string Collection = "Collection";

    /// <summary>The MyCRM roles that MyOrders' Print link let print (and administrators here).</summary>
    public static readonly string[] PrintRoles =
    [
        CrmRoles.SalesAdmin, CrmRoles.Admin, CrmRoles.StockAllocator, CrmRoles.Manager, CrmRoles.Head, CrmRoles.PpSpecialistManager,
        CrmRoles.SpecialistSeniorManager, CrmRoles.Md, CrmRoles.CreditGm, CrmRoles.Scheduler,
    ];

    /// <summary>MyOrders' rule for its Print link: Pending Credit Check, Pending Allocation, Pending Invoicing or Backordered, and a role that prints.</summary>
    public static bool CanPrint(Actor actor, string status) =>
        RequestFormSearch.PrintableStatuses.Contains(status, StringComparer.OrdinalIgnoreCase) && (actor.IsAdmin || CrmRoles.Is(actor.Orders.Role, PrintRoles));

    /// <summary>A page of the request forms the person may see, matching the search (newest first by default), with each row's dates.</summary>
    public RequestFormPage List(Actor actor, RequestFormSearch search)
    {
        var scope = access.RequestFormsScope(actor);
        search.SalesmanIds = scope.Salesmen;
        search.SpecialistOf = scope.SpecialistOf;
        search.AnyWithSpecialist = scope.AnyWithSpecialist;
        search.PpSpecialistOf = scope.PpSpecialistOf;
        search.CompanyContains = scope.CompanyContains;
        // The Sales Rep chosen, with their other OutSystems user when OutSystems keeps them twice.
        if (search.SalesRepIds is { } reps) search.SalesRepIds = access.SamePerson(reps);
        var page = data.SearchRequestForms(search);
        var ids = page.Rows.Select(r => r.Id).ToList();
        // MyOrders' Delv / Collection Date: the distinct dates of the active items.
        var dates = ids.Count == 0 ? new Dictionary<long, List<DateTime>>()
            : data.Rows(Entities.RequestFormItem, "REQUESTFORMID", ids).Where(r => r.Bool("ISACTIVE") && r.Date("DATE") is not null)
                .GroupBy(r => r.Long("REQUESTFORMID"))
                .ToDictionary(g => g.Key, g => g.Select(r => r.Date("DATE")!.Value.Date).Distinct().OrderBy(d => d).ToList());
        return page with
        {
            Rows = page.Rows.Select(r => r with
            {
                CanPrint = CanPrint(actor, r.Status),
                Dates = dates.TryGetValue(r.Id, out var d) ? d : Array.Empty<DateTime>(),
            }).ToList(),
        };
    }

    /// <summary>The choices of the list's filters: statuses and types in MyOrders' order, and the sales reps of the request forms the person may see.</summary>
    public (IReadOnlyList<CostingSheets.Choice> Statuses, IReadOnlyList<CostingSheets.Choice> Types, IReadOnlyList<CostingSheets.Choice> SalesReps) Filters(Actor actor)
    {
        IReadOnlyList<CostingSheets.Choice> Static(OsEntity entity) => data.All(entity).OrderBy(r => r.Int("ORDER")).ThenBy(r => r.Long("ID"))
            .Select(r => new CostingSheets.Choice(r.Long("ID"), r.Text("LABEL"))).ToList();
        var names = data.Users().GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First().Name);
        var salesmen = access.RequestFormsScope(actor).Salesmen;
        var reps = data.RequestFormSalesmen().Where(id => salesmen is null || salesmen.Contains(id))
            // One choice per person (a person OutSystems keeps twice is listed once, and choosing them finds both users' request forms).
            .Select(id => access.SamePerson(new[] { id }).Min()).Distinct()
            .Select(id => new CostingSheets.Choice(id, names.TryGetValue(id, out var name) && name.Length > 0 ? name : "#" + id.ToString(CultureInfo.InvariantCulture)))
            .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase).ToList();
        return (Static(Entities.RequestFormStatus), Static(Entities.RequestFormType), reps);
    }

    private Dictionary<string, object?>? One(OsEntity entity, long? id) =>
        id is { } value ? data.Rows(entity, "ID", [value]).FirstOrDefault() : null;

    private Dictionary<long, string> Labels(OsEntity entity) =>
        data.All(entity).GroupBy(r => r.Long("ID")).ToDictionary(g => g.Key, g => g.First().Text("LABEL"));

    private static string Label(Dictionary<long, string> labels, long? id) => id is { } value && labels.TryGetValue(value, out var label) ? label : "";

    /// <summary>The request form's row and company when it is there and the person may see it; else null.</summary>
    private (Dictionary<string, object?> Row, Dictionary<string, object?>? Company)? Open(Actor actor, long id)
    {
        var row = data.Rows(Entities.RequestForm, "ID", [id]).FirstOrDefault();
        if (row is null) return null;
        var company = One(Entities.Company, row.Id("COMPANYID"));
        return access.CanOpenRequestForm(actor, row, company?.Text("COMPANY") ?? "") ? (row, company) : null;
    }

    /// <summary>The request form's page, or null when it is not there or the person may not see it.</summary>
    public RequestFormView? Get(Actor actor, long id) => Open(actor, id) is { } found ? Build(actor, found.Row, found.Company) : null;

    /// <summary>One attachment's file (name and bytes), when the person may see its request form; else null.</summary>
    public (string FileName, byte[] Content)? Attachment(Actor actor, long requestFormId, long attachmentId)
    {
        var attachment = data.Rows(Entities.RequestFormAttachment, "ID", [attachmentId]).FirstOrDefault();
        if (attachment is null || attachment.Long("REQUESTFORMID") != requestFormId || Open(actor, requestFormId) is null) return null;
        var bytes = data.File(Entities.RequestFormAttachment, "BINARY", attachmentId);
        if (bytes is null) return null;
        var name = attachment.Text("FILENAME");
        return (name.Length > 0 ? name : "attachment-" + attachmentId.ToString(CultureInfo.InvariantCulture), bytes);
    }

    private RequestFormView Build(Actor actor, Dictionary<string, object?> rf, Dictionary<string, object?>? company)
    {
        var id = rf.Long("ID");
        var users = data.Users().GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First().Name);
        string UserOf(long? user) => user is { } value && users.TryGetValue(value, out var name) ? name : "";
        string User(string column) => UserOf(rf.Id(column));

        var specialists = new List<CostingSheetSpecialist>();
        void Specialist(string role, string column, string pending)
        {
            if (rf.Id(column) is { } user) specialists.Add(new CostingSheetSpecialist(role, UserOf(user), rf.Bool(pending)));
        }
        Specialist("PP Specialist", "PPSPECIALISTID", "PENDINGPP");
        Specialist("MDS Specialist", "MDSSPECIALISTID", "PENDINGMDS");
        Specialist("Fusion Specialist", "MITSSPECIALISTID", "PENDINGMITS");

        var type = Label(Labels(Entities.RequestFormType), rf.Id("REQUESTFORMTYPEID"));
        var status = Label(Labels(Entities.RequestFormStatus), rf.Id("REQUESTFORMSTATUSID"));
        var items = Items(id);
        var costingSheetId = rf.Id("COSTINGSHEETID");
        var costingSheet = One(Entities.CostingSheet, costingSheetId);
        var contactId = rf.Id("CONTACTID");
        return new RequestFormView
        {
            Id = id,
            CanPrint = CanPrint(actor, status),
            IsActive = rf.Bool("ISACTIVE"),
            DeletedAt = rf.Date("DELETEDAT"),
            DeletedBy = User("DELETEDBY"),
            Type = type,
            IsCollection = string.Equals(type, Collection, StringComparison.OrdinalIgnoreCase),
            Reference = One(Entities.Reference, rf.Id("REFERENCEID"))?.Text("REFERENCE") ?? "",
            Salesman = User("SALESMANID"),
            Status = status,
            Specialists = specialists,
            CreatedAt = rf.Date("CREATEDAT"),
            UpdatedAt = rf.Date("UPDATEDAT"),
            RecallAt = rf.Date("RECALLAT"),
            PrintedAt = rf.Date("PRINTEDAT"),
            CompanyId = rf.Id("COMPANYID"),
            Company = (company?.Text("COMPANY") ?? "").Trim(),
            Address = CostingSheets.AddressText(One(Entities.Address, rf.Id("ADDRESSID"))),
            Contact = CostingSheets.ContactText(One(Entities.Contacts, contactId)),
            Phones = contactId is { } c
                ? data.Rows(Entities.Phone, "CONTACTID", [c]).Where(p => p.Bool("ISACTIVE")).Select(p => p.Text("PHONE").Trim()).Where(p => p.Length > 0).Distinct().ToList()
                : Array.Empty<string>(),
            CustomerNo = rf.Text("CUSTOMERNO"),
            CustomerType = company?.Id("COMPANYGROUPID") is { } group ? Label(Labels(Entities.CompanyGroup), group) : "",
            EventTag = EventTag(rf.Id("EVENTTAGID")),
            Declared = rf.Bool("DECLARED"),
            Attachments = data.Rows(Entities.RequestFormAttachment, "REQUESTFORMID", [id])
                .Select(r => new CostingSheetAttachmentView(r.Long("ID"), r.Text("FILENAME"), r.Text("REMARKS"), r.Long("BINARY"), UserOf(r.Id("UPLOADEDBY")),
                    r.Date("UPLOADEDON"), r.Bool("ISCOMPLEX")))
                .OrderBy(a => a.IsComplex).ThenBy(a => a.Id).ToList(),
            CostingSheetId = costingSheetId,
            CanOpenCostingSheet = costingSheet is not null
                && access.CanOpen(actor, costingSheet.Id("SALESMANID"), One(Entities.Company, costingSheet.Id("COMPANYID"))?.Text("COMPANY") ?? ""),
            Differences = costingSheet is not null ? Differences(costingSheet.Long("ID"), items) : Array.Empty<CostingSheetDifference>(),
            Items = items,
            TotalStp = rf.Decimal("TOTALSTP"),
            History = History(rf, User),
            Facts = new RequestFormFacts
            {
                ProposalId = rf.Id("PROPOSALID"),
                OptionId = rf.Id("OPTIONID"),
                PreviousRequestFormId = rf.Id("PREVREQUESTFORMID"),
                SalesOrderNumber = rf.Text("SALESORDERNUMBER"),
                BackorderedSalesOrderNumber = rf.Text("BACKORDEREDSALESORDERNUMBER"),
                PickingSlipNumber = rf.Text("PICKINGSLIPNUMBER"),
                InvoiceNumber = rf.Text("INVOICENUMBER"),
                Warehouse = rf.Text("WAREHOUSE"),
                IsOpoChecked = rf.Bool("ISOPOCHECKED"),
                IsEpp = rf.Bool("ISEPP"),
                IsReconBilled = rf.Bool("ISRECONBILLED"),
            },
        };
    }

    /// <summary>The active items in MyOrders' order (by Id) with their active sub items, complex items and packet items, and where they go.</summary>
    private IReadOnlyList<RequestFormItemView> Items(long requestFormId)
    {
        var items = data.Rows(Entities.RequestFormItem, "REQUESTFORMID", [requestFormId]).Where(r => r.Bool("ISACTIVE")).OrderBy(r => r.Long("ID")).ToList();
        if (items.Count == 0) return Array.Empty<RequestFormItemView>();
        var itemIds = items.Select(r => r.Long("ID")).ToList();
        var subItems = data.Rows(Entities.RequestFormSubItem, "REQUESTFORMITEMID", itemIds).Where(r => r.Bool("ISACTIVE")).OrderBy(r => r.Long("ID")).ToList();
        var complexItems = data.Rows(Entities.RequestFormComplexItem, "REQUESTFORMITEMID", itemIds).Where(r => r.Bool("ISACTIVE")).ToList();
        List<long> IdsOf(IEnumerable<Dictionary<string, object?>> rows, string column) =>
            rows.Select(r => r.Id(column)).Where(v => v is not null).Select(v => v!.Value).Distinct().ToList();
        Dictionary<long, Dictionary<string, object?>> ById(OsEntity entity, List<long> ids) => ids.Count == 0
            ? new Dictionary<long, Dictionary<string, object?>>()
            : data.Rows(entity, "ID", ids).GroupBy(r => r.Long("ID")).ToDictionary(g => g.Key, g => g.First());
        var products = ById(Entities.Product, IdsOf(complexItems, "PRODUCTID"));
        var addresses = ById(Entities.Address, IdsOf(items, "ADDRESSID"));
        var contacts = ById(Entities.Contacts, IdsOf(items, "CONTACTID"));
        var phones = ById(Entities.Phone, IdsOf(items, "PHONEID"));
        var packetIds = IdsOf(items.Where(r => r.Bool("ISPACKET")), "PACKETID");
        var packetItems = packetIds.Count == 0 ? new List<Dictionary<string, object?>>()
            : data.Rows(Entities.PacketItem, "PACKETID", packetIds).Where(r => r.Bool("ISACTIVE")).OrderBy(r => r.Long("ID")).ToList();
        var brands = Labels(Entities.Brand);
        var assetTypes = Labels(Entities.AssetType);
        var collectionTypes = Labels(Entities.CollectionType);
        Dictionary<string, object?>? Find(Dictionary<long, Dictionary<string, object?>> rows, long? key) => key is { } k && rows.TryGetValue(k, out var row) ? row : null;

        return items.Select(item =>
        {
            var itemId = item.Long("ID");
            return new RequestFormItemView
            {
                Id = itemId,
                ProductName = item.Text("PRODUCTNAME"),
                ProductType = item.Text("PRODUCTTYPE"),
                EdpCode = item.Text("EDPCODE"),
                Quantity = item.Int("QUANTITY"),
                UnitPrice = item.Decimal("UNITPRICE"),
                TotalStp = item.Decimal("TOTALSTP"),
                SubItems = subItems.Where(s => s.Long("REQUESTFORMITEMID") == itemId)
                    .Select(s => new CostingSheetSubItemView(s.Text("PRODUCTNAME"), s.Text("EDPCODE"), s.Int("QUANTITY"), s.Decimal("UNITPRICE"))).ToList(),
                // A complex item's name is its own, else its product's; sorted by that name, as the costing sheet's.
                ComplexItems = complexItems.Where(x => x.Long("REQUESTFORMITEMID") == itemId)
                    .Select(x => new RequestFormComplexItemView(
                        x.Text("PRODUCTNAME").Length > 0 ? x.Text("PRODUCTNAME") : Find(products, x.Id("PRODUCTID"))?.Text("NAME") ?? "", x.Text("EDPCODE"), x.Int("QUANTITY")))
                    .OrderBy(x => x.ProductName, StringComparer.OrdinalIgnoreCase).ToList(),
                PacketItems = item.Bool("ISPACKET") && item.Id("PACKETID") is { } packet
                    ? packetItems.Where(pi => pi.Long("PACKETID") == packet).Select(pi => new CostingSheetPacketItemView(pi.Text("PRODUCTNAME"), pi.Int("QUANTITY"))).ToList()
                    : Array.Empty<CostingSheetPacketItemView>(),
                Address = CostingSheets.AddressText(Find(addresses, item.Id("ADDRESSID"))),
                PostalCode = item.Text("POSTALCODE"),
                Date = item.Date("DATE"),
                Contact = CompleteContact(Find(contacts, item.Id("CONTACTID"))),
                Phone = Find(phones, item.Id("PHONEID"))?.Text("PHONE").Trim() ?? "",
                IsFsa = item.Bool("ISFSA"),
                NoLift = item.Bool("NOLIFT"),
                Remarks = item.Text("REMARKS"),
                Model = item.Text("MODEL"),
                Serial = item.Text("SERIAL"),
                Description = item.Text("DESCRIPTION"),
                Brand = Label(brands, item.Id("BRANDID")),
                AssetType = Label(assetTypes, item.Id("ASSETTYPEID")),
                CollectionType = Label(collectionTypes, item.Id("COLLECTIONTYPEID")),
                ContractNo = item.Text("CONTRACTNO"),
                ReasonOfTermination = item.Text("REASONOFTERMINATION"),
                RemovalFee = item.Decimal("REMOVALFEE"),
                InvoiceNo = item.Text("INVOICENO"),
                BalanceToInvoice = item.Decimal("BALANCETOINVOICE"),
                RolloverAmount = item.Decimal("ROLLOVERAMOUNT"),
                SecondaryContact = item.Text("SECONDARYCONTACT"),
                SecondaryContactNo = item.Text("SECONDARYCONTACTNO"),
                CallBeforeCollection = item.Bool("CALLBEFORECOLLECTION"),
                SpecialInstructions = item.Text("SPECIALINSTRUCTIONS"),
                HasFinisher = item.Bool("HASFINISHER"),
            };
        }).ToList();
    }

    /// <summary>
    /// MyOrders' CS/RF differences (CompareCostingRequest), worked out without writing anything: the quantity of each EDP
    /// code on the costing sheet (active items, sub items and complex items) and on the request form, where they differ.
    /// </summary>
    private IReadOnlyList<CostingSheetDifference> Differences(long costingSheetId, IReadOnlyList<RequestFormItemView> items)
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
            Add(form, item.EdpCode, item.ProductName, item.Quantity);
            foreach (var sub in item.SubItems) Add(form, sub.EdpCode, sub.ProductName, sub.Quantity);
            foreach (var complex in item.ComplexItems) Add(form, complex.EdpCode, complex.ProductName, complex.Quantity);
        }
        var sheetItems = data.Rows(Entities.CostingSheetItem, "COSTINGSHEETID", [costingSheetId]).Where(r => r.Bool("ISACTIVE")).ToList();
        var sheetItemIds = sheetItems.Select(r => r.Long("ID")).ToList();
        var sheetRows = sheetItems
            .Concat(sheetItemIds.Count == 0 ? Enumerable.Empty<Dictionary<string, object?>>()
                : data.Rows(Entities.CostingSheetSubItem, "COSTINGSHEETITEMID", sheetItemIds).Where(r => r.Bool("ISACTIVE")))
            .Concat(sheetItemIds.Count == 0 ? Enumerable.Empty<Dictionary<string, object?>>()
                : data.Rows(Entities.CostingSheetComplexItem, "COSTINGSHEETITEMID", sheetItemIds).Where(r => r.Bool("ISACTIVE")));
        foreach (var r in sheetRows) Add(sheet, r.Text("EDPCODE"), r.Text("PRODUCTNAME"), r.Int("QUANTITY"));
        return sheet.Keys.Union(form.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(code => new CostingSheetDifference(code,
                sheet.TryGetValue(code, out var s) ? s.Name : form[code].Name,
                sheet.TryGetValue(code, out var sq) ? sq.Quantity : 0,
                form.TryGetValue(code, out var fq) ? fq.Quantity : 0))
            .Where(d => d.CostingSheetQuantity != d.RequestFormQuantity)
            .OrderBy(d => d.EdpCode, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The request form's history from the dates and people MyOrders keeps on it (a step whose date is empty is left out).</summary>
    private static IReadOnlyList<CostingSheetStep> History(Dictionary<string, object?> rf, Func<string, string> user)
    {
        var steps = new List<CostingSheetStep>();
        void Step(string step, string dateColumn, string byColumn = "", string detail = "")
        {
            if (rf.Date(dateColumn) is { } at) steps.Add(new CostingSheetStep(step, at, byColumn.Length > 0 ? user(byColumn) : "", detail));
        }
        Step("Created", "CREATEDAT", "CREATEDBY");
        Step("Manager approved", "APPROVEDBYMANAGERON", "APPROVEDBYMANAGERID");
        Step("Approved", "APPROVEDAT", "APPROVEDBY");
        Step("Head approved", "APPROVEDBYHEADON", "APPROVEDBYHEADID");
        Step("Sent for credit check", "PENDINGCREDITON");
        Step("Credit endorsed", "CREDITENDORSEDON");
        Step("Credit approved", "CREDITAPPROVEDAT", "CREDITAPPROVEDBY");
        Step("OPO checked", "OPOCHECKEDON", "OPOCHECKEDBY");
        Step("Sent", "SENTAT", "SENTBY");
        Step("Printed", "PRINTEDAT");
        Step("Recalled", "RECALLAT");
        Step("Deleted", "DELETEDAT", "DELETEDBY");
        Step("Last updated", "UPDATEDAT", "UPDATEDBY");
        return steps.OrderBy(s => s.At).ToList();
    }

    /// <summary>What the print shows, or null when the person may not see the request form; <see cref="RequestFormView.CanPrint"/> says whether MyOrders let them print it.</summary>
    public (RequestFormPrint Print, bool CanPrint)? Print(Actor actor, long id)
    {
        if (Open(actor, id) is not { } found) return null;
        var (rf, company) = found;
        var users = data.Users().GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First().Name);
        string UserOf(long? user) => user is { } value && users.TryGetValue(value, out var name) ? name : "";
        var type = Label(Labels(Entities.RequestFormType), rf.Id("REQUESTFORMTYPEID"));
        var status = Label(Labels(Entities.RequestFormStatus), rf.Id("REQUESTFORMSTATUSID"));
        var isCollection = string.Equals(type, Collection, StringComparison.OrdinalIgnoreCase);
        var salesman = rf.Id("SALESMANID");
        var print = new RequestFormPrint
        {
            Id = rf.Long("ID"),
            Reference = One(Entities.Reference, rf.Id("REFERENCEID"))?.Text("REFERENCE") ?? "",
            Type = type,
            IsCollection = isCollection,
            AgilityRunningNumber = rf.Int("AGILITYRUNNINGNUMBER"),
            UpdatedOn = rf.Date("UPDATEDAT") ?? rf.Date("CREATEDAT"),
            Company = (company?.Text("COMPANY") ?? "").Trim(),
            CustomerNo = rf.Text("CUSTOMERNO"),
            Salesman = UserOf(salesman),
            SalesmanCode = access.CrmUsers().Where(u => u.UserId == salesman && u.IsActive).Select(u => u.SalesmanCode).FirstOrDefault(c => c.Length > 0) ?? "",
            CreatedAt = rf.Date("CREATEDAT"),
            PrintedAt = rf.Date("PRINTEDAT"),
            Declared = rf.Bool("DECLARED"),
        };
        if (isCollection)
        {
            var items = Items(print.Id);
            var first = items.FirstOrDefault();
            return (print with
            {
                Collection = new RequestFormCollection
                {
                    AssetType = first?.AssetType ?? "",
                    CollectionType = first?.CollectionType ?? "",
                    Contact = first?.Contact ?? "",
                    ContactNo = first?.Phone ?? "",
                    SecondaryContact = first?.SecondaryContact ?? "",
                    SecondaryContactNo = first?.SecondaryContactNo ?? "",
                    SpecialInstructions = first?.SpecialInstructions ?? "",
                    Address = first?.Address ?? "",
                    PostalCode = first?.PostalCode ?? "",
                    Date = first?.Date,
                    Items = items,
                    ApprovedByManager = UserOf(rf.Id("APPROVEDBYMANAGERID")),
                    ApprovedByManagerOn = rf.Date("APPROVEDBYMANAGERON"),
                    ApprovedByHead = UserOf(rf.Id("APPROVEDBYHEADID")),
                    ApprovedByHeadOn = rf.Date("APPROVEDBYHEADON"),
                },
            }, CanPrint(actor, status));
        }
        return (print with { Deliveries = Deliveries(print.Id) }, CanPrint(actor, status));
    }

    /// <summary>
    /// The delivery form as MyOrders printed it last (its DeliveryFormPrint rows, by date, and their lines page by page),
    /// read as it is: MyCRMRecords never rebuilds it (MyOrders' Print did, each time).
    /// </summary>
    private IReadOnlyList<RequestFormDelivery> Deliveries(long requestFormId)
    {
        var prints = data.Rows(Entities.DeliveryFormPrint, "REQUESTFORMID", [requestFormId])
            .OrderBy(p => p.Date("DATE") ?? DateTime.MinValue).ThenBy(p => p.Long("ID")).ToList();
        if (prints.Count == 0) return Array.Empty<RequestFormDelivery>();
        var lines = data.Rows(Entities.DeliveryFormPrintItem, "DELIVERYFORMPRINTID", prints.Select(p => p.Long("ID")).ToList());
        Dictionary<string, object?>? OneOf(OsEntity entity, long? key) => One(entity, key);
        static int Sort(string productType) => productType switch { "Configuration" => 1, "Mainframe" => 2, "Accessory" => 3, "Consumable" => 4, _ => 0 };
        return prints.Select(p =>
        {
            var address = OneOf(Entities.Address, p.Id("ADDRESSID"));
            var pages = lines.Where(l => l.Long("DELIVERYFORMPRINTID") == p.Long("ID"))
                .GroupBy(l => l.Int("PAGE")).OrderBy(g => g.Key)
                .Select(g => (IReadOnlyList<RequestFormPrintLine>)g
                    .OrderBy(l => l.Long("REQUESTFORMITEMID")).ThenBy(l => Sort(l.Text("PRODUCTTYPE"))).ThenBy(l => l.Text("ITEMCODE"), StringComparer.Ordinal)
                    .Select(l => new RequestFormPrintLine(l.Text("ITEMCODE"), l.Bool("ISFSA"),
                        l.Text("NAME").Trim() + (l.Text("PRODUCTTYPE") == "Mainframe" && l.Text("MODEL").Length > 0 ? " [" + l.Text("MODEL") + "]" : ""),
                        l.Text("SERIALNO"), l.Int("QUANTITY"), l.Int("ALLOCATEDQUANTITY")))
                    .ToList())
                .ToList();
            return new RequestFormDelivery
            {
                Address = AddressLine(address),
                CountryPostalCode = CountryPostalCode(address),
                Contact = CompleteContact(OneOf(Entities.Contacts, p.Id("CONTACTID"))),
                Phone = OneOf(Entities.Phone, p.Id("PHONEID"))?.Text("PHONE").Trim() ?? "",
                Date = p.Date("DATE"),
                NoLift = p.Bool("NOLIFT"),
                Remarks = p.Text("REMARKS"),
                Pages = pages,
            };
        }).ToList();
    }

    /// <summary>MyOrders' CompleteContact: salutation, first, middle and last name.</summary>
    public static string CompleteContact(IReadOnlyDictionary<string, object?>? contact) => contact is null ? ""
        : string.Join(" ", new[] { contact.Text("SALUTATION"), contact.Text("FIRSTNAME"), contact.Text("MIDDLENAME"), contact.Text("LASTNAME") }
            .Select(t => t.Trim()).Where(t => t.Length > 0));

    /// <summary>An address's first line on the delivery form: block, street, unit and building.</summary>
    public static string AddressLine(IReadOnlyDictionary<string, object?>? address) => address is null ? ""
        : string.Join(" ", new[] { address.Text("BLOCK"), address.Text("STREET"), address.Text("UNITNUMBER"), address.Text("BUILDINGNAME") }
            .Select(t => t.Trim()).Where(t => t.Length > 0));

    /// <summary>An address's second line: "Singapore" and the postal code, or the city, state, country and postal code outside Singapore.</summary>
    public static string CountryPostalCode(IReadOnlyDictionary<string, object?>? address)
    {
        if (address is null) return "";
        var country = address.Text("COUNTRY").Trim();
        var local = country.Length == 0 || country.IndexOf("Singapore", StringComparison.OrdinalIgnoreCase) >= 0;
        var parts = local ? new[] { "Singapore", address.Text("POSTALCODE") }
            : new[] { address.Text("CITY"), address.Text("STATEPROVINCE"), country, address.Text("POSTALCODE") };
        return string.Join(" ", parts.Select(t => t.Trim()).Where(t => t.Length > 0));
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
}
