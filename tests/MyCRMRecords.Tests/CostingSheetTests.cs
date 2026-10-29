namespace MyCRMRecords.Tests;

public sealed class CostingSheetTests
{
    [Fact]
    public void The_list_is_searched_and_filtered_as_MyOrders_does_newest_first()
    {
        using var world = new OrdersWorld();
        var a = world.Sheet(world.SamId, world.Acme, "Invoiced", created: new DateTime(2025, 3, 4, 10, 0, 0), achieved: 1200m, invoiceNo: "INV-77",
            mds: world.DanId, headApproved: new DateTime(2025, 3, 10), invoiceDate: new DateTime(2025, 4, 1));
        var b = world.Sheet(world.KenId, world.Zen, "Draft", created: new DateTime(2026, 1, 5, 9, 0, 0), achieved: 300m);
        var c = world.Sheet(world.SamId, world.Dbs, "Pending Head", created: new DateTime(2026, 2, 1, 9, 0, 0), pp: world.DanId);
        world.Sheet(world.SamId, world.Acme, "Draft", active: false);
        var admin = world.Actor(OrdersWorld.Admin);
        IReadOnlyList<long> Find(CostingSheetSearch s) => world.D.CostingSheets.List(admin, s).Rows.Select(r => r.Id).ToList();

        // Deleted ones are not listed; newest (highest number) first.
        Assert.Equal(new[] { c, b, a }, Find(new CostingSheetSearch()));
        Assert.Equal(new[] { a }, Find(new CostingSheetSearch { Keyword = "inv-77" }));
        Assert.Equal(new[] { c }, Find(new CostingSheetSearch { Keyword = "dbs" }));
        Assert.Equal(new[] { c }, Find(new CostingSheetSearch { Keyword = "pending" }));
        Assert.Equal(new[] { b }, Find(new CostingSheetSearch { StatusId = world.Status["Draft"] }));
        Assert.Equal(new[] { c, a }, Find(new CostingSheetSearch { SalesRepIds = new[] { world.SamId } }));
        Assert.Equal(new[] { c, a }, Find(new CostingSheetSearch { SpecialistIds = new[] { world.DanId } }));
        // Has Specialist counts MDS and MITS specialists only, as MyOrders does.
        Assert.Equal(new[] { a }, Find(new CostingSheetSearch { HasSpecialist = "Yes" }));
        Assert.Equal(new[] { c, b }, Find(new CostingSheetSearch { HasSpecialist = "No" }));
        Assert.Equal(new[] { c, b }, Find(new CostingSheetSearch { CreatedFrom = new DateTime(2026, 1, 5) }));
        Assert.Equal(new[] { b, a }, Find(new CostingSheetSearch { CreatedTo = new DateTime(2026, 1, 5) }));
        Assert.Equal(new[] { a }, Find(new CostingSheetSearch { HeadApprovedFrom = new DateTime(2025, 3, 10), HeadApprovedTo = new DateTime(2025, 3, 10) }));
        Assert.Equal(new[] { a }, Find(new CostingSheetSearch { InvoiceFrom = new DateTime(2025, 1, 1) }));
        Assert.Equal(new[] { a, b, c }, Find(new CostingSheetSearch { Sort = "id", Descending = false }));
        Assert.Equal(new[] { a, b, c }, Find(new CostingSheetSearch { Sort = "achieved", Descending = true }));

        var row = world.D.CostingSheets.List(admin, new CostingSheetSearch { Keyword = "INV-77" }).Rows.Single();
        Assert.Equal("Sam Sales", row.Salesman);
        Assert.Equal("Acme Pte Ltd", row.Company);
        Assert.Equal("Alice Lim", row.Contact);
        Assert.Equal("Invoiced", row.Status);
        Assert.Equal("Q2026-0101", row.Reference);
        Assert.Equal(1200m, row.TotalAchievedPrice);
        Assert.Null(row.PrintedAt);
    }

    [Fact]
    public void The_lists_filters_offer_the_statuses_and_only_the_sales_reps_the_person_sees()
    {
        using var world = new OrdersWorld();
        world.Sheet(world.SamId, world.Acme, mds: world.DanId);
        world.Sheet(world.KenId, world.Zen);
        var (statuses, salesReps, specialists) = world.D.CostingSheets.Filters(world.Actor(OrdersWorld.Mia));
        Assert.Equal("Draft", statuses[0].Label);
        Assert.Equal(new[] { "Sam Sales" }, salesReps.Select(s => s.Label));
        Assert.Equal(new[] { "Dan MDS" }, specialists.Select(s => s.Label));
        Assert.Equal(new[] { "Ken Kwan", "Sam Sales" }, world.D.CostingSheets.Filters(world.Actor(OrdersWorld.Hana)).SalesReps.Select(s => s.Label));
    }

    [Fact]
    public void A_costing_sheets_page_shows_what_MyOrders_summary_showed()
    {
        using var world = new OrdersWorld();
        var d = world.Data;
        var group = d.Add(Entities.ProductGroup, new { NAME = "Office MFP" });
        var mds = d.Add(Entities.ProductGroup, new { NAME = "MDS Services" });
        var mfp = d.Add(Entities.Product, new { NAME = "IM C3000", PRODUCTGROUPID = group });
        var service = d.Add(Entities.Product, new { NAME = "Managed print", PRODUCTGROUPID = mds });
        var finisher = d.Add(Entities.Product, new { NAME = "Finisher SR3270", PRODUCTGROUPID = group });
        var id = d.Add(Entities.CostingSheet, new
        {
            ISACTIVE = true, SALESMANID = world.SamId, COMPANYID = world.Acme, CONTACTID = world.AcmeContact, ADDRESSID = world.AcmeAddress,
            BILLINGADDRESS = world.AcmeBilling, COSTINGSHEETSTATUSID = world.Status["Invoiced"], REFERENCEID = world.Reference1,
            CREATEDAT = new DateTime(2025, 5, 2, 10, 0, 0), CREATEDBY = world.SamId, UPDATEDAT = new DateTime(2025, 6, 1, 8, 0, 0), UPDATEDBY = world.MiaId,
            APPROVEDAT = new DateTime(2025, 5, 3, 9, 0, 0), APPROVEDBY = world.MiaId, HEADAPPROVEDAT = OutSystemsValues.NullDate, RECALLAT = OutSystemsValues.NullDate,
            PENDINGCREDITON = new DateTime(2025, 5, 3, 9, 0, 1), CREDITENDORSEDON = new DateTime(2025, 5, 4, 15, 0, 0), PENDINGINVOICINGON = new DateTime(2025, 5, 4, 15, 0, 0),
            OPOCHECKEDON = new DateTime(2025, 5, 4, 11, 0, 0), OPOCHECKEDBY = world.CarlId, ISOPOCHECKED = true,
            ORDERNO = "SO-5", ORDERUPDATEDON = new DateTime(2025, 5, 5, 9, 0, 0), ORDERUPDATEDBY = world.CarlId,
            INVOICENO = "INV-1", MININVOICEDATE = new DateTime(2025, 5, 20), INVOICEDATETEXT = "20/05/2025", PRINTEDAT = new DateTime(2025, 5, 2, 11, 0, 0),
            TRANSFERREDON = OutSystemsValues.NullDate, DELETEDAT = OutSystemsValues.NullDate,
            MDSSPECIALISTID = world.DanId, PENDINGMDS = false, PPSPECIALISTID = (long?)null, MITSSPECIALISTID = (long?)null,
            CUSTOMERNO = "C123", ISZBA = true, ISCONTRACTEXTENSION = false, ISFIXEDTERMAGREEMENT = false, ISVITALCASE = false, GMAPRICING = false,
            TOTALSTP = 12000m, TOTALACHIEVEDPRICE = 9000m, COMMISSIONABLEREVENUE = 8500m, TOTALMANAGERPRICE = 8000m, TOTALFOB = 6000m,
            PERCENTAGEABOVEMANAGERPRICE = 12.5m, PERCENTAGEABOVEFOB = 50m, ESTIMATEDCOMMISSION = 300m, CONFIRMEDCOMMISSION = 280m,
            BILLTYPEID = world.BillType["Internal Financing"], ISPPP = true, FINANCEOUTRIGHTAMOUNT = 9000m, MONTHS = 60, INTERESTRATE = 3.5m, DOWNPAYMENT = 0m,
            FINALPERCENTAGE = 0m, FINALPAYMENT = 0m, MONTHLYLEASE = 165.2m, PPPINTERNALBWRATE = 0.004m, PPPINTERNALCOLOURRATE = 0.04m,
            PPPCUSTOMERBWRATE = 0.006m, PPPCUSTOMERCOLOURRATE = 0.06m, PPPMONTHLYBWVOLUME = 3000, PPPMONTHLYCOLOURVOLUME = 500,
            BILLTO = 45L, LEASEAGREEMENTNUMBER = "L-9", TRANSACTIONTYPEID = 43L, TRADEINTOID = 41L, REMARKS = "Deliver after 2pm",
            TOTALVARIABLECOST = 700m, REQUESTFORMID = (long?)null,
        });
        var item = d.Add(Entities.CostingSheetItem, new
        {
            COSTINGSHEETID = id, ISACTIVE = true, PRODUCTID = mfp, PRODUCTNAME = "IM C3000", PRODUCTTYPE = "Configuration", EDPCODE = "418283", QUANTITY = 2,
            UNITPRICE = 4000m, TOTALSTP = 10000m, ACHIEVEDPRICE = 7500m, ISSUBSCRIPTION = false, OAAPRICING = true, ISPACKET = false, ISMDS = false, ISSOL = false,
            MODEL = "", SERIAL = "", ISFSA = true, BELOWGMCPC = true, METERBWONLY = false, INITIALBWCOPIES = 1000, INITIALBWPRICE = 0.5m, INITIALCLRCOPIES = 200,
            INITIALCLRPRICE = 5m, MONTHLYBWCOPIES = 3000, MONTHLYBWPRICE = 0.6m, MONTHLYCLRCOPIES = 500, MONTHLYCLRPRICE = 6m, MONTHLYMONTHS = 60,
            THEREAFTERBWPRICE = 0.7m, THEREAFTERCLRPRICE = 7m,
        });
        var service2 = d.Add(Entities.CostingSheetItem, new
        {
            COSTINGSHEETID = id, ISACTIVE = true, PRODUCTID = service, PRODUCTNAME = "Managed print", PRODUCTTYPE = "Configuration", EDPCODE = "MDS-1", QUANTITY = 1,
            UNITPRICE = 2000m, TOTALSTP = 2000m, ACHIEVEDPRICE = 1500m, ISSUBSCRIPTION = true, ISPACKET = false, ISFSA = true,
        });
        d.Add(Entities.CostingSheetItem, new { COSTINGSHEETID = id, ISACTIVE = false, PRODUCTNAME = "Removed", QUANTITY = 1, UNITPRICE = 1m, EDPCODE = "X" });
        d.Add(Entities.CostingSheetSubItem, new { COSTINGSHEETITEMID = item, ISACTIVE = true, PRODUCTNAME = "Paper tray", EDPCODE = "T-1", QUANTITY = 2, UNITPRICE = 250m });
        d.Add(Entities.CostingSheetSubItem, new { COSTINGSHEETITEMID = item, ISACTIVE = false, PRODUCTNAME = "Gone", EDPCODE = "G-1", QUANTITY = 9, UNITPRICE = 1m });
        d.Add(Entities.CostingSheetComplexItem, new { COSTINGSHEETITEMID = item, ISACTIVE = true, PRODUCTID = finisher, PRODUCTNAME = "", EDPCODE = "F-1", MPGCODE = "M7", QUANTITY = 1, STP = 900m, ACHIEVEDPRICE = 800m });
        d.Add(Entities.CostingSheetComplexItem, new { COSTINGSHEETITEMID = item, ISACTIVE = true, PRODUCTID = (long?)null, PRODUCTNAME = "Cabling", EDPCODE = "", MPGCODE = "", QUANTITY = 3, STP = 1m, ACHIEVEDPRICE = 30m });
        d.Add(Entities.Variable, new { COSTINGSHEETID = id, VARIABLENAMEID = world.VariableName["Accessories"], COST = 500m, TRADEINITEM = "", MANUFACTURER = "", MODEL = "", SERIALNO = "", REMARKS = "Stand" });
        d.Add(Entities.Variable, new { COSTINGSHEETID = id, VARIABLENAMEID = world.VariableName["R2R Trade In"], COST = 200m, TRADEINITEM = "Old MFP", MANUFACTURER = "Ricoh", MODEL = "MP 2014", SERIALNO = "S-1", REMARKS = "" });
        var attachment = d.Add(Entities.CostingSheetAttachment, new { COSTINGSHEETID = id, BINARY = new byte[2048], FILENAME = "PO.pdf", REMARKS = "Signed", UPLOADEDBY = world.SamId, UPLOADEDON = new DateTime(2025, 5, 2, 12, 0, 0), ISCOMPLEX = false });

        var view = world.D.CostingSheets.Get(world.Actor(OrdersWorld.Sam), id)!;
        Assert.Equal("Q2026-0101", view.Reference);
        Assert.Equal("Sam Sales", view.Salesman);
        Assert.Equal("Invoiced", view.Status);
        Assert.Equal(new CostingSheetSpecialist("MDS Specialist", "Dan MDS", false), view.Specialists.Single());
        Assert.Equal("10 Anson Road #20-01 International Plaza Singapore 079903", view.Address);
        Assert.Equal("Alice Lim", view.Contact);
        Assert.Equal("SME", view.CustomerType);
        Assert.True(view.IsZba);
        Assert.Null(view.RecallAt);

        // Active items only, in MyOrders' order; a complex item's name is its product's when it has none; "Not Available" when its STP is 1 or less.
        Assert.Equal(new[] { "IM C3000", "Managed print" }, view.Items.Select(i => i.ProductName));
        var mfpItem = view.Items[0];
        Assert.Equal(new[] { "Paper tray" }, mfpItem.SubItems.Select(s => s.ProductName));
        Assert.Equal(new[] { "Cabling", "Finisher SR3270" }, mfpItem.ComplexItems.Select(x => x.ProductName));
        Assert.False(mfpItem.ComplexItems[0].HasStp);
        Assert.True(mfpItem.ComplexItems[1].HasStp);
        Assert.True(mfpItem.OaaPricing);
        // Meter charges for an office configuration; none for MDS Services.
        Assert.NotNull(mfpItem.Meter);
        Assert.True(mfpItem.Meter!.BelowGmCpc);
        Assert.Equal(60, mfpItem.Meter.MonthlyMonths);
        Assert.Null(view.Items[1].Meter);

        // Stored totals; the margins are not for a salesperson.
        Assert.Equal(12000m, view.TotalStp);
        Assert.Equal(9000m, view.TotalAchievedPrice);
        Assert.Null(view.Margins);
        var head = world.D.CostingSheets.Get(world.Actor(OrdersWorld.Hana), id)!;
        Assert.Equal(33.33m, head.Margins!.DealMargin);
        Assert.Equal(12.5m, head.Margins.PercentageAboveManagerPrice);

        // Billing: internal financing with PPP shows the rates; the billing address, not the bill-to company.
        Assert.True(view.Billing.IsFinancing);
        Assert.True(view.Billing.ShowsPppRates);
        Assert.Equal("Hardware Monthly", view.Billing.MonthlyLabel);
        Assert.Equal("", view.Billing.BillTo);
        Assert.Equal("1 Raffles Place Singapore 048616", view.Billing.BillingAddress);
        Assert.Equal("Ricoh", view.Billing.TradeInTo);
        Assert.Equal("New", view.Billing.TransactionType);

        // Every variable cost; the trade-ins (R2R, O2R) on their own too.
        Assert.Equal(new[] { "R2R Trade In", "Accessories" }, view.Variables.Select(v => v.Name));
        Assert.Equal("Old MFP", view.TradeIns.Single().Item);

        // The attachment's size, never its bytes; who uploaded it.
        var file = view.Attachments.Single();
        Assert.Equal(new CostingSheetAttachmentView(attachment, "PO.pdf", "Signed", 2048, "Sam Sales", new DateTime(2025, 5, 2, 12, 0, 0), false), file);

        // The history from the dates MyOrders kept, in order, without the empty ones.
        Assert.Equal(new[] { "Created", "Printed", "Approved", "Sent for credit check", "OPO checked", "Credit endorsed", "Pending invoicing", "Order number entered", "Invoiced", "Last updated" },
            view.History.Select(s => s.Step));
        Assert.Equal("Carl Credit", view.History.Single(s => s.Step == "OPO checked").By);
        Assert.Equal("SO-5", view.History.Single(s => s.Step == "Order number entered").Detail);
    }

    [Fact]
    public void Differences_with_the_request_form_are_worked_out_without_writing_anything()
    {
        using var world = new OrdersWorld();
        var d = world.Data;
        var id = world.Sheet(world.SamId, world.Acme);
        var item = d.Add(Entities.CostingSheetItem, new { COSTINGSHEETID = id, ISACTIVE = true, PRODUCTNAME = "IM C3000", EDPCODE = "418283", QUANTITY = 2, PRODUCTTYPE = "Mainframe" });
        d.Add(Entities.CostingSheetSubItem, new { COSTINGSHEETITEMID = item, ISACTIVE = true, PRODUCTNAME = "Tray", EDPCODE = "T-1", QUANTITY = 2, UNITPRICE = 1m });
        var form = d.Add(Entities.RequestForm, new { COSTINGSHEETID = id, ISACTIVE = true });
        var formItem = d.Add(Entities.RequestFormItem, new { REQUESTFORMID = form, ISACTIVE = true, PRODUCTNAME = "IM C3000", EDPCODE = "418283", QUANTITY = 2 });
        d.Add(Entities.RequestFormSubItem, new { REQUESTFORMITEMID = formItem, ISACTIVE = true, PRODUCTNAME = "Tray", EDPCODE = "T-1", QUANTITY = 1 });
        d.Add(Entities.RequestFormComplexItem, new { REQUESTFORMITEMID = formItem, ISACTIVE = true, PRODUCTNAME = "Stand", EDPCODE = "S-9", QUANTITY = 1 });

        var view = world.D.CostingSheets.Get(world.Actor(OrdersWorld.Sam), id)!;
        Assert.Equal(form, view.RequestFormId);
        Assert.Equal(new[] { new CostingSheetDifference("S-9", "Stand", 0, 1), new CostingSheetDifference("T-1", "Tray", 2, 1) }, view.Differences);
    }

    [Fact]
    public void A_costing_sheet_deleted_in_MyOrders_still_opens_and_says_so()
    {
        using var world = new OrdersWorld();
        var id = world.Sheet(world.SamId, world.Acme, active: false);
        var view = world.D.CostingSheets.Get(world.Actor(OrdersWorld.Sam), id)!;
        Assert.False(view.IsActive);
        Assert.Empty(world.D.CostingSheets.List(world.Actor(OrdersWorld.Sam), new CostingSheetSearch()).Rows);
    }

    [Fact]
    public void The_api_answers_the_list_and_a_page_and_says_when_MyOrders_is_not_connected()
    {
        using var world = new OrdersWorld();
        var id = world.Sheet(world.SamId, world.Acme, achieved: 10m);
        var list = world.Json(world.Call("costingsheets", OrdersWorld.Sam, new Dictionary<string, string> { ["q"] = "acme", ["sort"] = "company", ["desc"] = "0" }));
        Assert.Equal(1, (int)list["total"]!);
        Assert.Equal(id, (long)list["rows"]![0]!["id"]!);
        Assert.False((bool)list["can"]!["seesAll"]!);
        var page = world.Json(world.Call("costingsheets/" + id, OrdersWorld.Sam));
        Assert.Equal("Acme Pte Ltd", (string)page["costingSheet"]!["company"]!);

        world.Data.Available = false;
        Assert.Equal(503, world.Call("costingsheets", OrdersWorld.Sam).Status);
        Assert.Equal(404, new OrdersApi(world.D).Handle("POST", "costingsheets/" + id, "{}", new Dictionary<string, string>(), OrdersWorld.Admin).Status);
    }

    [Fact]
    public void The_list_downloads_as_an_Excel_workbook_with_what_the_person_sees()
    {
        using var world = new OrdersWorld();
        world.Sheet(world.SamId, world.Acme, achieved: 10m);
        world.Sheet(world.KenId, world.Zen, achieved: 20m);
        var file = new FilesApi(world.D).Download(FilesApi.List, new Dictionary<string, string>(), OrdersWorld.Sam);
        Assert.Equal(200, file.Status);
        using var stream = new MemoryStream(file.Content);
        var rows = MyCRMRecords.Documents.Xlsx.Read(stream);
        Assert.Equal("Acme Pte Ltd", rows.Single()["Company"]);
    }

    [Fact]
    public void A_costing_sheet_is_printed_as_MyOrders_let_by_status_and_role()
    {
        using var world = new OrdersWorld();
        var invoiced = world.Sheet(world.SamId, world.Acme, "Invoiced");
        var draft = world.Sheet(world.SamId, world.Acme, "Draft");
        Assert.True(world.D.CostingSheets.Get(world.Actor(OrdersWorld.Mia), invoiced)!.CanPrint);
        Assert.True(world.D.CostingSheets.Get(world.Actor(OrdersWorld.Hana), invoiced)!.CanPrint);
        Assert.False(world.D.CostingSheets.Get(world.Actor(OrdersWorld.Sam), invoiced)!.CanPrint);
        Assert.False(world.D.CostingSheets.Get(world.Actor(OrdersWorld.Mia), draft)!.CanPrint);
        Assert.Equal(new[] { false, true }, world.D.CostingSheets.List(world.Actor(OrdersWorld.Mia), new CostingSheetSearch()).Rows.Select(r => r.CanPrint));
        Assert.Equal(403, world.Call($"costingsheets/{invoiced}/print", OrdersWorld.Sam).Status);
        Assert.Equal(200, world.Call($"costingsheets/{invoiced}/print", OrdersWorld.Mia).Status);
        Assert.Equal(404, world.Call($"costingsheets/{invoiced}/print", OrdersWorld.Ken).Status);
    }

    [Fact]
    public void The_print_works_out_MyOrders_financing_and_lists_each_items_lines()
    {
        using var world = new OrdersWorld();
        var d = world.Data;
        var tray = d.Add(Entities.Product, new { NAME = "Paper tray", EDPCODE = "T-9", PRODUCTGROUPID = (long?)null });
        var id = d.Add(Entities.CostingSheet, new
        {
            ISACTIVE = true, SALESMANID = world.SamId, COMPANYID = world.Acme, CONTACTID = world.AcmeContact, ADDRESSID = world.AcmeAddress,
            COSTINGSHEETSTATUSID = world.Status["Invoiced"], REFERENCEID = world.Reference1, CREATEDAT = new DateTime(2025, 5, 2, 10, 0, 0),
            APPROVEDAT = new DateTime(2025, 5, 3, 9, 0, 0), MDSSPECIALISTID = world.DanId,
            TOTALACHIEVEDPRICE = 9000m, TOTALVARIABLECOST = 700m, FINALPAYMENT = 0m, DOWNPAYMENT = 0m, INTERESTRATE = 3.5m, MONTHS = 60, MONTHLYLEASE = 165.2m,
            BILLTYPEID = world.BillType["Internal Financing"], ISPPP = true, PPPMONTHLYBWVOLUME = 3000, PPPINTERNALBWRATE = 0.004m,
            PPPMONTHLYCOLOURVOLUME = 500, PPPINTERNALCOLOURRATE = 0.04m, BILLTO = 45L,
        });
        var a = d.Add(Entities.CostingSheetItem, new { COSTINGSHEETID = id, ISACTIVE = true, PRODUCTNAME = "IM C3000", EDPCODE = "418283", PRODUCTTYPE = "Configuration",
            QUANTITY = 2, TOTALSTP = 8000m, ACHIEVEDPRICE = 7000m, APPROVEDSPECIALUNITPRICE = 3400m, MONTHLYBWPRICE = 0.6m, MONTHLYCLRPRICE = 6m, MONTHLYMONTHS = 60,
            MONTHLYBWCOPIES = 1000, MONTHLYCLRCOPIES = 100 });
        d.Add(Entities.CostingSheetSubItem, new { COSTINGSHEETITEMID = a, ISACTIVE = true, PRODUCTNAME = "Tray", PRODUCTTYPE = "Accessory", EDPCODE = "T-1", QUANTITY = 1, UNITPRICE = 250m });
        d.Add(Entities.CostingSheetComplexItem, new { COSTINGSHEETITEMID = a, ISACTIVE = true, PRODUCTID = tray, PRODUCTNAME = "", PRODUCTTYPE = "Service", EDPCODE = "", QUANTITY = 3, ACHIEVEDPRICE = 30m, STP = 10m });
        d.Add(Entities.CostingSheetItem, new { COSTINGSHEETID = id, ISACTIVE = true, PRODUCTNAME = "IM 2702", EDPCODE = "418300", PRODUCTTYPE = "Configuration",
            QUANTITY = 1, TOTALSTP = 2000m, ACHIEVEDPRICE = 2000m, MONTHLYBWPRICE = 0.8m, MONTHLYCLRPRICE = 0m, MONTHLYMONTHS = 60, MONTHLYBWCOPIES = 500 });
        d.Add(Entities.Variable, new { COSTINGSHEETID = id, VARIABLENAMEID = world.VariableName["Accessories"], COST = 500m, TRADEINITEM = "", MANUFACTURER = "", MODEL = "", SERIALNO = "", REMARKS = "Stand" });
        d.Add(Entities.Variable, new { COSTINGSHEETID = id, VARIABLENAMEID = world.VariableName["R2R Trade In"], COST = 200m, TRADEINITEM = "Old MFP", MANUFACTURER = "Ricoh", MODEL = "MP 2014", SERIALNO = "S-1", REMARKS = "" });

        var print = world.D.CostingSheets.Print(world.Actor(OrdersWorld.Mia), id)!;
        Assert.Equal("Q2026-0101-" + id, print.ReferenceWithId);
        Assert.Equal("Mia Manager", print.ManagerName);
        Assert.Equal(new[] { "Dan MDS" }, print.SpecialistNames);
        Assert.Equal("Orix Leasing", print.BillToName);
        Assert.True(print.IsInternalFinancing);

        // MyOrders' CSCal with PPP: the PPP volumes and rates give the service credit.
        var f = print.Finance;
        Assert.Equal(9700m, f.Products);
        Assert.Equal(5m, f.NoOfYear);
        Assert.Equal(1697.5m, f.DealInterest);
        Assert.Equal(720m, f.BwTotal);
        Assert.Equal(1200m, f.ColTotal);
        Assert.Equal(1920m, f.ServiceCredit);
        Assert.Equal(13317.5m, f.LeasePayable);
        // 13317.50 / 60 = 221.96: more than a dollar from the stored 165.20, so the worked-out amount is shown.
        Assert.Equal(221.96m, Math.Round(f.PerMonth, 2));
        // The items' monthly rates differ, but with PPP the service credit is not per item.
        Assert.False(print.RatesDiffer);

        // The New CS lines: each item, then its sub items and complex items (the product's name and EDP code when the item has none).
        Assert.Equal(new[] { "1|", "|1.1", "|1.2", "2|" }, print.Lines.Select(l => l.Seq + "|" + l.Line));
        Assert.Equal(6800m, print.Lines[0].PalRelease);
        Assert.Equal(("Paper tray", "T-9"), (print.Lines[2].Items, print.Lines[2].EdpCode));
        Assert.Equal("Tray x 1", print.Items[0].SubItemsText);
        Assert.True(print.Items[0].HasComplex);
        Assert.Equal("Paper tray", print.ComplexGroups.Single().Parts.Single().ProductName);

        // The Old CS's variable costs by name; the subtotal leaves the trade-ins out.
        Assert.Equal(new[] { 500m }, print.VariableRows.Single(r => r.Label == "Accessories").Costs);
        Assert.Equal(new[] { "Old MFP,Ricoh,MP 2014,S-1" }, print.VariableRows.Single(r => r.Label == "R2R Trade In").Remarks);
        Assert.Equal(500m, print.SubtotalBeforeTradeIn);
    }

    [Fact]
    public void Without_PPP_items_with_different_rates_have_their_service_credit_per_item()
    {
        var cs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["TOTALACHIEVEDPRICE"] = 10000m, ["TOTALVARIABLECOST"] = 0m, ["FINALPAYMENT"] = 1000m, ["DOWNPAYMENT"] = 500m, ["INTERESTRATE"] = 4m, ["MONTHS"] = 36,
            ["MONTHLYLEASE"] = 300m,
        };
        var f = CostingSheets.Finance(cs, isPpp: false, ((6m, 36, 0.6m), 2000, 200), serviceCreditBw: 432m, serviceCreditClr: 432m);
        Assert.Equal(8500m, f.Products);
        Assert.Equal(1020m, f.DealInterest);
        Assert.Equal(240m, f.FinalPaymentInterest);
        Assert.Equal(2000, f.BwNoOfCopies);
        Assert.Equal(0.006m, f.BwRate);
        Assert.Equal(432m, f.BwTotal);
        Assert.Equal(864m, f.ServiceCredit);
        Assert.Equal(9500m, f.ProductsWithFinalPayment);
        Assert.Equal(11624m, f.LeasePayable);
        // (11624 - 1000) / 36 = 295.11: more than a dollar from 300, so worked out; within a dollar, the stored amount.
        Assert.Equal(295.11m, Math.Round(f.PerMonth, 2));
        cs["MONTHLYLEASE"] = 295.5m;
        Assert.Equal(295.5m, CostingSheets.Finance(cs, false, ((6m, 36, 0.6m), 2000, 200), 432m, 432m).PerMonth);
    }

    [Fact]
    public void A_companys_page_lists_its_costing_sheets_and_request_forms_and_each_links_back_to_it()
    {
        using var world = new OrdersWorld();
        var acme = world.Sheet(world.SamId, world.Acme, "Invoiced");
        world.Sheet(world.SamId, world.Zen, "Draft");
        var ken = world.Sheet(world.KenId, world.Acme, "Draft");
        var form = world.Form(world.SamId, world.Acme);
        world.Form(world.SamId, world.Zen);
        var company = new Dictionary<string, string> { ["company"] = world.Acme.ToString(System.Globalization.CultureInfo.InvariantCulture) };

        // Only that company's, and of those only the ones the person may see (Sam: his own, not Ken's).
        var sheets = world.Json(world.Call("costingsheets", OrdersWorld.Sam, company));
        Assert.Equal(new[] { acme }, sheets["rows"]!.Select(r => (long)r["id"]!));
        Assert.Equal(new[] { ken, acme }, world.Json(world.Call("costingsheets", OrdersWorld.Admin, company))["rows"]!.Select(r => (long)r["id"]!));
        Assert.Equal(new[] { form }, world.Json(world.Call("requestforms", OrdersWorld.Sam, company))["rows"]!.Select(r => (long)r["id"]!));

        // A costing sheet and a request form name their company's Id, for the link to its page.
        Assert.Equal(world.Acme, (long)world.Json(world.Call("costingsheets/" + acme, OrdersWorld.Sam))["costingSheet"]!["companyId"]!);
        Assert.Equal(world.Acme, (long)world.Json(world.Call("requestforms/" + form, OrdersWorld.Sam))["requestForm"]!["companyId"]!);
    }
}
