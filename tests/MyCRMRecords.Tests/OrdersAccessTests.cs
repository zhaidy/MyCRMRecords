namespace MyCRMRecords.Tests;

/// <summary>Who sees which costing sheets: MyOrders' rule (ShowAll roles, else the salespeople of MyCRM's GetSub list).</summary>
public sealed class OrdersAccessTests
{
    [Fact]
    public void A_salesperson_sees_their_own_a_manager_their_teams_and_the_see_all_roles_everyones()
    {
        using var world = new OrdersWorld();
        var sam = world.Sheet(world.SamId, world.Acme);
        var ken = world.Sheet(world.KenId, world.Zen);
        var cara = world.Sheet(world.CaraId, world.Acme);
        IReadOnlyList<long> Seen(ApiUser user) => world.D.CostingSheets.List(world.Actor(user), new CostingSheetSearch()).Rows.Select(r => r.Id).OrderBy(i => i).ToList();

        Assert.Equal(new[] { sam }, Seen(OrdersWorld.Sam));
        Assert.Equal(new[] { ken }, Seen(OrdersWorld.Ken));
        // Mia manages Sam: her team's Sales.
        Assert.Equal(new[] { sam }, Seen(OrdersWorld.Mia));
        // Head and Credit see every costing sheet (MyOrders' ShowAll), and so do MyCRMRecords' administrators.
        Assert.Equal(new[] { sam, ken, cara }, Seen(OrdersWorld.Hana));
        Assert.Equal(new[] { sam, ken, cara }, Seen(OrdersWorld.Carl));
        Assert.Equal(new[] { sam, ken, cara }, Seen(OrdersWorld.Admin));
        // Someone without a MyCRM role sees nothing.
        Assert.Empty(Seen(OrdersWorld.Olga));
    }

    [Fact]
    public void Someone_whose_OutSystems_username_is_not_their_login_is_found_by_their_email_address()
    {
        using var world = new OrdersWorld();
        var cara = world.Sheet(world.CaraId, world.Acme);
        var actor = world.Actor(OrdersWorld.Cara);
        Assert.Equal(CrmRoles.Sales, actor.Orders.Role);
        Assert.Contains(world.CaraId, actor.Orders.Me);
        Assert.Equal(new[] { cara }, world.D.CostingSheets.List(actor, new CostingSheetSearch()).Rows.Select(r => r.Id));
    }

    [Fact]
    public void A_Service_Delivery_Manager_sees_only_DBS_costing_sheets_of_their_team()
    {
        using var world = new OrdersWorld();
        var kenDbs = world.Sheet(world.KenId, world.Dbs);
        world.Sheet(world.KenId, world.Zen);
        world.Sheet(world.SamId, world.Dbs);
        var sid = world.Actor(OrdersWorld.Sid);
        Assert.Equal(CrmRoles.ServiceDeliveryManager, sid.Orders.Role);
        Assert.Equal(new[] { kenDbs }, world.D.CostingSheets.List(sid, new CostingSheetSearch()).Rows.Select(r => r.Id));
        Assert.True(world.D.OrdersAccess.CanOpen(sid, world.KenId, "DBS Bank Ltd"));
        Assert.False(world.D.OrdersAccess.CanOpen(sid, world.KenId, "Zen Trading"));
    }

    [Fact]
    public void Each_role_lists_the_people_MyCRMs_GetSub_gives_it()
    {
        using var world = new OrdersWorld();
        var rows = world.D.OrdersAccess.CrmUsers();
        IReadOnlyCollection<long> List(long user, string role) => OrdersAccess.UserList(rows, new[] { user }, role);
        Assert.Equal(new[] { world.SamId }, List(world.SamId, CrmRoles.Sales));
        // A manager: the Sales (and the like) of their teams, not the MDS specialist.
        Assert.Equal(new[] { world.MiaId, world.SamId }.OrderBy(i => i), List(world.MiaId, CrmRoles.Manager).OrderBy(i => i));
        // An MDS specialist: their teams' Sales and specialists.
        Assert.Equal(new[] { world.DanId, world.SamId }.OrderBy(i => i), List(world.DanId, CrmRoles.MdsSpecialist).OrderBy(i => i));
        // Credit: everyone with an active CRM user row.
        Assert.Equal(8, List(world.CarlId, CrmRoles.Credit).Count);
        // Any other role: everyone in their teams with a role.
        Assert.Equal(new[] { world.KenId, world.CaraId, world.SidId }.OrderBy(i => i), List(world.SidId, CrmRoles.ServiceDeliveryManager).OrderBy(i => i));
    }

    [Fact]
    public void Roles_are_matched_by_their_label_whatever_their_Ids()
    {
        using var world = new OrdersWorld();
        // The test world's role Ids start at 34 and step by 3: nothing like the list's order.
        Assert.Equal(CrmRoles.Head, world.Actor(OrdersWorld.Hana).Orders.Role);
        Assert.Equal(CrmRoles.ServiceDeliveryManager, world.Actor(OrdersWorld.Sid).Orders.Role);
        Assert.True(world.Actor(OrdersWorld.Hana).SeesAllCostingSheets);
        Assert.False(world.Actor(OrdersWorld.Mia).SeesAllCostingSheets);
    }

    [Fact]
    public void A_costing_sheet_the_person_may_not_see_cannot_be_opened_nor_its_attachments()
    {
        using var world = new OrdersWorld();
        var ken = world.Sheet(world.KenId, world.Zen);
        var file = world.Data.Add(Entities.CostingSheetAttachment, new { COSTINGSHEETID = ken, BINARY = new byte[] { 1, 2, 3 }, FILENAME = "po.pdf", REMARKS = "", UPLOADEDBY = world.KenId, UPLOADEDON = world.Now, ISCOMPLEX = false });
        Assert.Null(world.D.CostingSheets.Get(world.Actor(OrdersWorld.Sam), ken));
        Assert.Null(world.D.CostingSheets.Attachment(world.Actor(OrdersWorld.Sam), ken, file));
        Assert.NotNull(world.D.CostingSheets.Get(world.Actor(OrdersWorld.Ken), ken));
        Assert.Equal(new byte[] { 1, 2, 3 }, world.D.CostingSheets.Attachment(world.Actor(OrdersWorld.Ken), ken, file)!.Value.Content);
        // An attachment asked for with another costing sheet's number is not given.
        var sam = world.Sheet(world.SamId, world.Acme);
        Assert.Null(world.D.CostingSheets.Attachment(world.Actor(OrdersWorld.Admin), sam, file));
        Assert.Equal(404, world.Call("costingsheets/" + ken, OrdersWorld.Sam).Status);
    }

    [Fact]
    public void An_administrator_can_sign_in_as_anyone_MyCRM_or_a_costing_sheet_names_before_they_open_MyCRMRecords()
    {
        using var world = new OrdersWorld();
        // Jean has a MyCRM user and Pat is a costing sheet's salesperson without one; neither has opened MyCRMRecords.
        var jean = world.Data.User(@"RSP\jean", "Jean Ng", "jean@example.com");
        var sales = world.Data.All(Entities.CrmRole).Single(r => r.Text("LABEL") == "Sales").Long("ID");
        world.Data.Add(Entities.CrmUser, new { USERID = jean, ROLEID = sales, TEAMID = 8L, ISACTIVE = true });
        var pat = world.Data.User(@"RSP\pat", "Pat Lee", "pat@example.com");
        world.Sheet(pat, world.Acme);
        // An OutSystems user neither MyCRM nor a costing sheet names is not listed.
        world.Data.User(@"RSP\svc_batch", "Batch Service");

        var people = world.Json(world.Call("orders/people"))["people"]!.ToObject<List<PersonToSignInAs>>()!;
        var byLogin = people.ToDictionary(p => p.Login, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Sales", byLogin[@"RSP\jean"].Role);
        Assert.Equal("Jean Ng", byLogin[@"RSP\jean"].Name);
        Assert.Equal("", byLogin[@"RSP\pat"].Role);
        Assert.False(byLogin.ContainsKey(@"RSP\svc_batch"));
        // Cara signs in as RSP\ctan though OutSystems knows her as RSP\cara.tan: listed once, under the login she uses.
        Assert.Equal("Sales", byLogin[@"RSP\ctan"].Role);
        Assert.False(byLogin.ContainsKey(@"RSP\cara.tan"));
        Assert.Equal("Head", byLogin[@"RSP\hana"].Role);
        // People MyCRMRecords knows who are not in MyCRM stay in the list.
        Assert.Contains(@"RSP\root", byLogin.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(people.Select(p => p.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase), people.Select(p => p.Name));

        // Signed in as Jean, MyCRMRecords shows what she sees: her team's costing sheets are not hers.
        var asJean = new ApiUser(@"RSP\jean", "Jean Ng", "jean@example.com", Impersonator: OrdersWorld.Admin);
        Assert.Equal(CrmRoles.Sales, world.Actor(asJean).Orders.Role);
        var hers = world.Sheet(jean, world.Zen);
        world.Sheet(world.KenId, world.Zen);
        Assert.Equal(new[] { hers }, world.D.CostingSheets.List(world.Actor(asJean), new CostingSheetSearch()).Rows.Select(r => r.Id));
        // Only administrators get the list.
        Assert.Equal(403, world.Call("orders/people", OrdersWorld.Sam).Status);
    }

    [Fact]
    public void Someone_OutSystems_keeps_twice_is_one_person_who_sees_and_is_found_by_the_costing_sheets_of_both_users()
    {
        using var world = new OrdersWorld();
        // OutSystems keeps Alfris as RSP\alfriskuay and as RSP\AlfrisKuay@ricoh.sg (one login once the address is
        // dropped); his MyCRM user is the first, and he made costing sheets as both.
        Assert.Equal(@"RSP\AlfrisKuay", OutSystemsImport.LoginOf(@"RSP\AlfrisKuay@ricoh.sg", "RSP"));
        var first = world.Data.User(@"RSP\alfriskuay", "Alfris Kuay", "alfriskuay@ricoh.sg");
        var second = world.Data.User(OutSystemsImport.LoginOf(@"RSP\AlfrisKuay@ricoh.sg", "RSP"), "Alfris Kuay");
        var sales = world.Data.All(Entities.CrmRole).Single(r => r.Text("LABEL") == "Sales").Long("ID");
        world.Data.Add(Entities.CrmUser, new { USERID = first, ROLEID = sales, TEAMID = 8L, ISACTIVE = true });
        var a = world.Sheet(first, world.Acme);
        var b = world.Sheet(second, world.Zen);
        world.Sheet(world.KenId, world.Zen);

        // He sees the costing sheets of both users, and opens either.
        var alfris = new ApiUser(@"RSP\alfriskuay", "Alfris Kuay", "alfriskuay@ricoh.sg");
        var actor = world.Actor(alfris);
        Assert.Equal(CrmRoles.Sales, actor.Orders.Role);
        Assert.Equal(new[] { b, a }, world.D.CostingSheets.List(actor, new CostingSheetSearch()).Rows.Select(r => r.Id));
        Assert.NotNull(world.D.CostingSheets.Get(actor, b));
        // So does a manager whose MyCRM team has his first user only (Ken's team gets a manager here).
        var manager = world.Data.User(@"RSP\max", "Max Manager", "max@example.com");
        var managerRole = world.Data.All(Entities.CrmRole).Single(r => r.Text("LABEL") == "Manager").Long("ID");
        world.Data.Add(Entities.CrmUser, new { USERID = manager, ROLEID = managerRole, TEAMID = 8L, ISACTIVE = true });
        var max = world.Actor(new ApiUser(@"RSP\max", "Max Manager", "max@example.com"));
        Assert.Contains(b, world.D.CostingSheets.List(max, new CostingSheetSearch()).Rows.Select(r => r.Id));

        // The Sales Rep filter lists him once, and choosing him finds both users' costing sheets.
        var reps = world.Json(world.Call("costingsheets/filters"))["salesReps"]!.Select(c => (string)c["label"]!).ToList();
        Assert.Single(reps, "Alfris Kuay");
        var chosen = world.Json(world.Call("costingsheets", query: new Dictionary<string, string> { ["salesRep"] = second.ToString() }));
        Assert.Equal(new[] { b, a }, chosen["rows"]!.Select(r => (long)r["id"]!));

        // Sign in as lists him once.
        var people = world.Json(world.Call("orders/people"))["people"]!.Select(p => (string)p["login"]!).ToList();
        Assert.Single(people, l => string.Equals(l, @"RSP\alfriskuay", StringComparison.OrdinalIgnoreCase));
    }
}
