using Newtonsoft.Json.Linq;

namespace MyCRMRecords.Tests;

/// <summary>MyOrders' request forms, read only: the list and who sees what, a request form's page, its attachments and its print.</summary>
public sealed class RequestFormTests
{
    private static IReadOnlyList<long> Seen(OrdersWorld world, ApiUser user, RequestFormSearch? search = null) =>
        world.D.RequestForms.List(world.Actor(user), search ?? new RequestFormSearch()).Rows.Select(r => r.Id).OrderBy(i => i).ToList();

    [Fact]
    public void The_list_shows_whom_MyOrders_showed_newest_first_and_its_filters_work()
    {
        using var world = new OrdersWorld();
        var a = world.Form(world.SamId, world.Acme, "Pending Head", created: new DateTime(2026, 1, 5, 9, 0, 0));
        var b = world.Form(world.KenId, world.Zen, "Draft", "Collection", created: new DateTime(2026, 3, 1, 9, 0, 0));
        var c = world.Form(world.CaraId, world.Dbs, "Pending Allocation", created: new DateTime(2026, 2, 1, 9, 0, 0));
        world.Form(world.SamId, world.Acme, active: false);
        world.Data.Add(Entities.RequestFormItem, new { REQUESTFORMID = a, ISACTIVE = true, PRODUCTNAME = "IM C3000", QUANTITY = 1, DATE = new DateTime(2026, 1, 20) });
        world.Data.Add(Entities.RequestFormItem, new { REQUESTFORMID = a, ISACTIVE = true, PRODUCTNAME = "Tray", QUANTITY = 1, DATE = new DateTime(2026, 1, 22) });
        world.Data.Add(Entities.RequestFormItem, new { REQUESTFORMID = a, ISACTIVE = false, PRODUCTNAME = "Old", QUANTITY = 1, DATE = new DateTime(2026, 1, 25) });

        // Newest first (MyOrders' default: CreatedAt desc); deleted ones are not listed.
        var all = world.D.RequestForms.List(world.Actor(OrdersWorld.Admin), new RequestFormSearch());
        Assert.Equal(new[] { b, c, a }, all.Rows.Select(r => r.Id));
        Assert.Equal(new[] { new DateTime(2026, 1, 20), new DateTime(2026, 1, 22) }, all.Rows.Single(r => r.Id == a).Dates);
        IReadOnlyList<long> Find(RequestFormSearch s) => world.D.RequestForms.List(world.Actor(OrdersWorld.Admin), s).Rows.Select(r => r.Id).ToList();
        Assert.Equal(new[] { b }, Find(new RequestFormSearch { TypeId = world.FormType["Collection"] }));
        Assert.Equal(new[] { a }, Find(new RequestFormSearch { StatusId = world.FormStatus["Pending Head"] }));
        Assert.Equal(new[] { c }, Find(new RequestFormSearch { Keyword = "dbs" }));
        Assert.Equal(new[] { c }, Find(new RequestFormSearch { Keyword = c.ToString() }));
        Assert.Equal(new[] { a }, Find(new RequestFormSearch { SalesRepIds = new[] { world.SamId } }));
        Assert.Equal(new[] { b, c }, Find(new RequestFormSearch { CreatedFrom = new DateTime(2026, 2, 1) }));
        Assert.Equal(new[] { c, a }, Find(new RequestFormSearch { CreatedTo = new DateTime(2026, 2, 1) }));
        Assert.Equal(new[] { a, b, c }, Find(new RequestFormSearch { Sort = "id", Descending = false }));

        // Who sees what: MyOrders' rule, as for costing sheets.
        Assert.Equal(new[] { a }, Seen(world, OrdersWorld.Sam));
        Assert.Equal(new[] { a }, Seen(world, OrdersWorld.Mia));
        Assert.Equal(new[] { a, b, c }.OrderBy(i => i), Seen(world, OrdersWorld.Hana));
        Assert.Empty(Seen(world, OrdersWorld.Olga));
        // A Service Delivery Manager: their team's request forms of DBS only.
        Assert.Equal(new[] { c }, Seen(world, OrdersWorld.Sid));

        // The filters: statuses and types in MyOrders' order, and the sales reps the person may see.
        var filters = world.Json(world.Call("requestforms/filters", OrdersWorld.Mia));
        Assert.Equal("Draft", (string)filters["statuses"]![0]!["label"]!);
        Assert.Equal(new[] { "Delivery Order", "Collection", "Demo", "Loan", "Trial" }, filters["types"]!.Select(t => (string)t["label"]!));
        Assert.Equal(new[] { "Sam Sales" }, filters["salesReps"]!.Select(t => (string)t["label"]!));
    }

    [Fact]
    public void Specialists_see_the_request_forms_MyOrders_specialists_list_showed_them()
    {
        using var world = new OrdersWorld();
        var mds = world.Form(world.SamId, world.Acme, mds: world.DanId);
        var other = world.Form(world.KenId, world.Zen, mits: world.MiaId);
        world.Form(world.KenId, world.Zen);
        // Dan (MDS Specialist, Mia's team) sees his team's Sales' (Sam's) and the one where he is the MDS specialist.
        Assert.Equal(new[] { mds }, Seen(world, OrdersWorld.Dan));
        world.Form(world.KenId, world.Zen, mds: world.DanId, id: 9001);
        Assert.Equal(new[] { mds, 9001L }, Seen(world, OrdersWorld.Dan));
        Assert.NotNull(world.D.RequestForms.Get(world.Actor(OrdersWorld.Dan), 9001));
        Assert.Null(world.D.RequestForms.Get(world.Actor(OrdersWorld.Dan), other));

        // A specialist senior manager sees every request form with a specialist.
        var olgaId = world.OlgaId;
        var seniorRole = world.Data.Add(Entities.CrmRole, new { ID = 97L, LABEL = "Specialist Senior Manager" });
        world.Data.Add(Entities.CrmUser, new { USERID = olgaId, ROLEID = seniorRole, TEAMID = (long?)null, ISACTIVE = true });
        Assert.Equal(new[] { mds, other, 9001L }.OrderBy(i => i), Seen(world, OrdersWorld.Olga));
    }

    [Fact]
    public void A_request_form_shows_what_MyOrders_summary_showed()
    {
        using var world = new OrdersWorld();
        var sheet = world.Sheet(world.SamId, world.Acme, "Invoiced");
        var csItem = world.Data.Add(Entities.CostingSheetItem, new { COSTINGSHEETID = sheet, ISACTIVE = true, PRODUCTNAME = "IM C3000", EDPCODE = "418283", QUANTITY = 2 });
        world.Data.Add(Entities.CostingSheetSubItem, new { COSTINGSHEETITEMID = csItem, ISACTIVE = true, PRODUCTNAME = "Tray", EDPCODE = "T-1", QUANTITY = 1 });
        var id = world.Form(world.SamId, world.Acme, "Pending Allocation", costingSheet: sheet, mds: world.DanId, printed: new DateTime(2026, 2, 3, 10, 0, 0));
        world.Data.Add(Entities.Phone, new { CONTACTID = world.AcmeContact, PHONE = "6123 4567", ISACTIVE = true });
        world.Data.Add(Entities.Phone, new { CONTACTID = world.AcmeContact, PHONE = "9999 0000", ISACTIVE = false });
        var item = world.Data.Add(Entities.RequestFormItem, new
        {
            REQUESTFORMID = id, ISACTIVE = true, PRODUCTNAME = "IM C3000", PRODUCTTYPE = "Configuration", EDPCODE = "418283", QUANTITY = 1, UNITPRICE = 4000m,
            TOTALSTP = 4250m, ADDRESSID = world.AcmeAddress, CONTACTID = world.AcmeContact, DATE = new DateTime(2026, 2, 10), ISFSA = true, NOLIFT = true,
            REMARKS = "Level 20", MODEL = "IM C3000", SERIAL = "S123",
        });
        world.Data.Add(Entities.RequestFormSubItem, new { REQUESTFORMITEMID = item, ISACTIVE = true, PRODUCTNAME = "Tray", EDPCODE = "T-1", QUANTITY = 1, UNITPRICE = 250m });
        world.Data.Add(Entities.RequestFormSubItem, new { REQUESTFORMITEMID = item, ISACTIVE = false, PRODUCTNAME = "Gone", EDPCODE = "G-1", QUANTITY = 1, UNITPRICE = 1m });
        world.Data.Add(Entities.RequestFormComplexItem, new { REQUESTFORMITEMID = item, ISACTIVE = true, PRODUCTNAME = "Scan to cloud", EDPCODE = "SC-1", QUANTITY = 3 });
        world.Data.Add(Entities.RequestFormAttachment, new { REQUESTFORMID = id, BINARY = new byte[] { 1, 2, 3 }, FILENAME = "DO.pdf", REMARKS = "signed", UPLOADEDBY = world.SamId, UPLOADEDON = new DateTime(2026, 2, 4), ISCOMPLEX = false });

        var view = world.D.RequestForms.Get(world.Actor(OrdersWorld.Sam), id)!;
        Assert.Equal("Delivery Order", view.Type);
        Assert.False(view.IsCollection);
        Assert.Equal("Pending Allocation", view.Status);
        Assert.Equal("Sam Sales", view.Salesman);
        Assert.Equal("Acme Pte Ltd", view.Company);
        Assert.Equal(new[] { "6123 4567" }, view.Phones);
        Assert.Equal("MDS Specialist", view.Specialists.Single().Role);
        var line = view.Items.Single();
        Assert.Equal(4250m, line.TotalStp);
        Assert.Equal("Tray", line.SubItems.Single().ProductName);
        Assert.Equal("Scan to cloud", line.ComplexItems.Single().ProductName);
        Assert.True(line.NoLift);
        Assert.Equal(new DateTime(2026, 2, 10), line.Date);
        Assert.Equal(3L, view.Attachments.Single().Size);
        Assert.True(view.CanOpenCostingSheet);
        // The costing sheet has 2 of 418283; the request form 1, and 3 of SC-1 the costing sheet does not have.
        Assert.Equal(new[] { ("418283", 2, 1), ("SC-1", 0, 3) }, view.Differences.Select(d => (d.EdpCode, d.CostingSheetQuantity, d.RequestFormQuantity)));
        Assert.Contains(view.History, s => s.Step == "Printed" && s.At == new DateTime(2026, 2, 3, 10, 0, 0));
        // Sam (Sales) may not print it, as in MyOrders; a Sales Admin may.
        Assert.False(view.CanPrint);
        Assert.Null(world.D.RequestForms.Get(world.Actor(OrdersWorld.Ken), id));

        // Its attachment, for those who may see the request form only.
        var file = world.D.RequestForms.Attachment(world.Actor(OrdersWorld.Sam), id, world.Data.All(Entities.RequestFormAttachment).Single().Long("ID"));
        Assert.Equal(new byte[] { 1, 2, 3 }, file!.Value.Content);
        Assert.Null(world.D.RequestForms.Attachment(world.Actor(OrdersWorld.Ken), id, world.Data.All(Entities.RequestFormAttachment).Single().Long("ID")));

        // Through the API, and a deleted request form still opens from a link, saying so.
        Assert.Equal(id, (long)world.Json(world.Call("requestforms/" + id, OrdersWorld.Sam))["requestForm"]!["id"]!);
        Assert.Equal(404, world.Call("requestforms/" + id, OrdersWorld.Ken).Status);
        var deleted = world.Form(world.SamId, world.Acme, active: false);
        Assert.False(world.D.RequestForms.Get(world.Actor(OrdersWorld.Sam), deleted)!.IsActive);
    }

    [Fact]
    public void The_print_is_MyOrders_last_delivery_form_or_its_collection_form_and_follows_its_print_rule()
    {
        using var world = new OrdersWorld();
        var salesAdminRole = world.Data.Add(Entities.CrmRole, new { ID = 98L, LABEL = "SalesAdmin" });
        world.Data.Add(Entities.CrmUser, new { USERID = world.OlgaId, ROLEID = salesAdminRole, TEAMID = (long?)null, ISACTIVE = true });
        var id = world.Form(world.SamId, world.Acme, "Pending Allocation", printed: new DateTime(2026, 2, 3));
        var later = world.Data.Add(Entities.DeliveryFormPrint, new { REQUESTFORMID = id, ADDRESSID = world.AcmeAddress, CONTACTID = world.AcmeContact, DATE = new DateTime(2026, 2, 12), NOLIFT = false, REMARKS = "" });
        var first = world.Data.Add(Entities.DeliveryFormPrint, new { REQUESTFORMID = id, ADDRESSID = world.AcmeAddress, CONTACTID = world.AcmeContact, DATE = new DateTime(2026, 2, 10), NOLIFT = true, REMARKS = "Call first" });
        void Line(long print, long item, string code, string name, string type, int page, string model = "") =>
            world.Data.Add(Entities.DeliveryFormPrintItem, new { DELIVERYFORMPRINTID = print, REQUESTFORMID = id, REQUESTFORMITEMID = item, ITEMCODE = code, NAME = name + " ",
                SERIALNO = "", ALLOCATEDQUANTITY = 1, QUANTITY = 1, MODEL = model, PRODUCTTYPE = type, PAGE = page, ISFSA = false });
        Line(first, 5, "A-2", "Toner", "Consumable", 1);
        Line(first, 5, "M-1", "IM C3000", "Mainframe", 1, "IMC3000");
        Line(first, 4, "Z-9", "Earlier item", "Accessory", 1);
        Line(first, 6, "P-2", "Second page", "Accessory", 2);
        Line(later, 7, "L-1", "Later delivery", "Accessory", 1);

        Assert.Equal(403, world.Call("requestforms/" + id + "/print", OrdersWorld.Sam).Status);
        var print = world.D.RequestForms.Print(world.Actor(OrdersWorld.Olga), id)!.Value;
        Assert.True(print.CanPrint);
        var deliveries = print.Print.Deliveries;
        Assert.Equal(new DateTime?[] { new DateTime(2026, 2, 10), new DateTime(2026, 2, 12) }, deliveries.Select(d => d.Date));
        Assert.True(deliveries[0].NoLift);
        Assert.Equal(2, deliveries[0].Pages.Count);
        // By item, then Configuration, Mainframe, Accessory, Consumable; a mainframe shows its model.
        Assert.Equal(new[] { "Earlier item", "IM C3000 [IMC3000]", "Toner" }, deliveries[0].Pages[0].Select(l => l.Name));
        Assert.Equal("Singapore 079903", deliveries[0].CountryPostalCode);

        // Never printed in MyOrders: nothing to show (MyCRMRecords never builds the delivery form itself).
        var never = world.Form(world.SamId, world.Acme, "Pending Credit Check");
        Assert.Empty(world.D.RequestForms.Print(world.Actor(OrdersWorld.Olga), never)!.Value.Print.Deliveries);

        // A collection: boxes A to C from the first item, every item, and who approved it.
        var collection = world.Form(world.SamId, world.Acme, "Pending Invoicing", "Collection");
        world.Data.Add(Entities.RequestFormItem, new { REQUESTFORMID = collection, ISACTIVE = true, ASSETTYPEID = world.AssetType["Hardware"], COLLECTIONTYPEID = world.CollectionType["Trade-In"],
            MODEL = "MP 2014", SERIAL = "X1", QUANTITY = 1, HASFINISHER = true, DATE = new DateTime(2026, 3, 1), POSTALCODE = "079903" });
        world.Data.Add(Entities.RequestFormItem, new { REQUESTFORMID = collection, ISACTIVE = true, ASSETTYPEID = world.AssetType["Hardware"], COLLECTIONTYPEID = world.CollectionType["Trade-In"],
            MODEL = "MP 2015", SERIAL = "X2", QUANTITY = 1, HASFINISHER = false });
        var collected = world.D.RequestForms.Print(world.Actor(OrdersWorld.Olga), collection)!.Value.Print;
        Assert.True(collected.IsCollection);
        Assert.Equal("Hardware", collected.Collection!.AssetType);
        Assert.Equal("Trade-In", collected.Collection.CollectionType);
        Assert.Equal(new[] { "MP 2014", "MP 2015" }, collected.Collection.Items.Select(i => i.Model));
        Assert.Equal(new DateTime(2026, 3, 1), collected.Collection.Date);
    }

    [Fact]
    public void The_download_has_a_row_per_delivery_date_as_MyOrders_download()
    {
        using var world = new OrdersWorld();
        var id = world.Form(world.SamId, world.Acme, "Pending Allocation");
        world.Data.Add(Entities.RequestFormItem, new { REQUESTFORMID = id, ISACTIVE = true, PRODUCTNAME = "A", QUANTITY = 1, DATE = new DateTime(2026, 1, 20) });
        world.Data.Add(Entities.RequestFormItem, new { REQUESTFORMID = id, ISACTIVE = true, PRODUCTNAME = "B", QUANTITY = 1, DATE = new DateTime(2026, 1, 22) });
        world.Form(world.KenId, world.Zen);
        var file = new FilesApi(world.D).Download(FilesApi.RequestFormList, new Dictionary<string, string>(), OrdersWorld.Sam);
        Assert.Equal(200, file.Status);
        Assert.Equal(FileAnswer.XlsxType, file.ContentType);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(file.Content));
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = reader.ReadToEnd();
        // The header and two rows of Sam's request form (Ken's is not his).
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(sheet, "<row ").Count);
    }
}
