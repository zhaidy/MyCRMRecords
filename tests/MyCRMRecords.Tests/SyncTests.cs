using Newtonsoft.Json.Linq;

namespace MyCRMRecords.Tests;

/// <summary>
/// The two-way sync with MyCRM (OldAppSync, CrmSync), against MyCRM's tables in memory: a company, address or contact
/// made here takes MyCRM's number, its fields reach MyCRM with MyCRM's users and static Ids, MyCRM's changes come here,
/// only MyCRM's active permanent territories are synced, and what is removed on either side is marked, never deleted.
/// </summary>
public sealed class SyncTests
{
    private static SyncOutcome Sync(TestWorld world, bool allowLarge = false)
    {
        var outcome = world.D.Sync.Run("test", allowLarge);
        Assert.True(outcome.Ok, outcome.Message + "\n" + string.Join("\n", outcome.Details));
        return outcome;
    }

    private static void InStep(TestWorld world)
    {
        var outcome = Sync(world);
        Assert.True(outcome.ToOldApp + outcome.ToHere == 0, "Not in step:\n" + string.Join("\n", outcome.Details));
    }

    private static long L(object? value) => Convert.ToInt64(value);

    private static string Str(object? value) => FakeOldTables.Text(value);

    private static bool B(object? value) => value is bool b && b;

    [Fact]
    public void Nothing_can_be_deleted_in_the_old_app()
    {
        Assert.DoesNotContain(typeof(IOldTablesWriter).GetMethods(), m => m.Name.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.DoesNotContain(typeof(IOldTables).GetMethods(), m => m.Name.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>
    /// A sync cut off before it finished (the website restarted, or ASP.NET ended the page that started it) stayed
    /// "Running…" for good: the next sync, which holds the sync so none other runs, marks it as cut off.
    /// </summary>
    [Fact]
    public void A_sync_cut_off_before_it_finished_is_marked_so_by_the_next_one()
    {
        using var world = new TestWorld(s => s.Sync.Enabled = true, crmUsers: false);
        var cut = world.Store.Insert(new SyncRun { StartedOn = world.Now.AddMinutes(-20), By = "Rob Root", Message = "Running…" });
        Sync(world);
        var marked = world.Store.Get<SyncRun>(cut.Id)!;
        Assert.NotNull(marked.FinishedOn);
        Assert.False(marked.Ok);
        Assert.StartsWith("Cut off before it finished", marked.Message);
        Assert.All(world.Store.Find<SyncRun>(), r => Assert.NotNull(r.FinishedOn));
    }

    /// <summary>The users are looked up by login, then name, then email, the first of the list winning, as before the index.</summary>
    [Fact]
    public void The_sync_finds_the_old_apps_users_by_login_then_name_then_email()
    {
        var users = new List<OldUser>
        {
            new(5, @"RSP\ann", "Ann Lee", "ann@example.com"), new(6, @"RSP\bob", "Ann Lee", "bob@example.com"),
            new(7, @"RSP\cat", "Cat Tan", "ann@example.com"), new(8, @"rsp\ANN", "Other", "other@example.com"),
        };
        var context = new SyncContext(new UsersOnly(users), [], DateTime.Now);
        Assert.Equal(5L, context.OldUser(@" rsp\Ann "));
        Assert.Equal(5L, context.OldUser("ann lee"));
        Assert.Equal(6L, context.OldUser("bob@example.com"));
        Assert.Equal(5L, context.OldUser("ann@example.com"));
        Assert.Null(context.OldUser("nobody"));
        Assert.Null(context.OldUser(""));
        Assert.Equal("Cat Tan", context.User(7L)!.Name);
        Assert.Null(context.User(0));
        Assert.Null(context.User(99L));
    }

    private sealed class UsersOnly(IReadOnlyList<OldUser> users) : IOldTables
    {
        public string Title => "Old";
        public IReadOnlyList<Dictionary<string, object?>> Rows(string table, string? sinceColumn = null, DateTime? since = null) => [];
        public IReadOnlyList<Dictionary<string, object?>> Rows(string table, string column, IReadOnlyCollection<long> values) => [];
        public Dictionary<string, object?>? Find(string table, IReadOnlyDictionary<string, object?> key) => null;
        public IReadOnlyDictionary<string, OldColumn> Columns(string table) => new Dictionary<string, OldColumn>();
        public IReadOnlyDictionary<long, string> Labels(string table) => new Dictionary<long, string>();
        public IReadOnlyList<OldUser> Users() => users;
        public IOldTablesWriter BeginWrite() => throw new NotSupportedException();
    }

    /// <summary>
    /// Made here by Rob (an administrator): a group name, the company Tan &amp; Sons in it, its billing address, Mr Tan as its
    /// primary contact with an email and a phone; returns the company's Id.
    /// </summary>
    private static long MadeHere(TestWorld world)
    {
        var admin = world.Actor(TestWorld.Admin);
        var group = world.D.Companies.SaveGroupName(admin, 0, new Companies.GroupNameInput { GroupName = "Tan Group" });
        Assert.True(group.Ok, group.Message);
        var company = world.D.Companies.Save(admin, 0, new Companies.CompanyInput
        {
            Name = "Tan & Sons", Brn = "200012345K", Website = "www.tan.example", CompanyGroup = "SME", CompAccountType = "User", CompOwnership = "Owned",
            AdvanceCustomerType = "Healthcare", CompanyGroupNameId = group.Id, Remarks = "Good customer", IsChannelPartner = true,
        });
        Assert.True(company.Ok, company.Message);
        var id = company.Id!.Value;
        var address = world.D.Companies.SaveAddress(admin, id, 0, new Companies.AddressInput
        {
            IsDefault = true, AddressType = AddressTypes.Billing, Block = "1", Street = "Tan Road", PostalCode = "123456", UnitNumber = "#01-02",
        });
        Assert.True(address.Ok, address.Message);
        var contact = world.D.Companies.SaveContact(admin, id, 0, new Companies.ContactInput
        {
            Salutation = "Mr", FirstName = "Ah", LastName = "Tan", Department = "Finance", Designation = "Director", IsDefault = true, IsEqList = true, IsLinkedIn = true,
            Emails = [new Companies.EmailInput { Address = "tan@tan.example", ConsentToMarketingInfo = true }],
            Phones = [new Companies.PhoneInput { Number = "61234567", PhoneType = "Office" }],
        });
        Assert.True(contact.Ok, contact.Message);
        return id;
    }

    [Fact]
    public void A_company_made_here_takes_MyCRM_number_and_reaches_MyCRM_with_its_address_and_contact()
    {
        using var world = new TestWorld(s => s.Sync.Enabled = true, crmUsers: false);
        var old = world.OldApp;
        var id = MadeHere(world);
        // MyCRM's numbers: the company, the address and the contact have the same number in both.
        Assert.Equal(1001, id);
        Assert.Equal(1001, world.Store.Find<Address>().Single().Id);
        Assert.Equal(1001, world.Store.Find<Contact>().Single().Id);

        Sync(world);
        var row = old.Row("Company", 1001);
        Assert.Equal("Tan & Sons", Str(row["COMPANY"]));
        Assert.Equal(old.StaticId("CompanyGroup", "SME"), L(row["COMPANYGROUPID"]));
        Assert.Equal(old.StaticId("CompAccountType", "User"), L(row["COMPACCOUNTTYPEID"]));
        Assert.Equal(old.StaticId("CompOwnership", "Owned"), L(row["COMPOWNERSHIPID"]));
        Assert.Equal(old.StaticId("AdvanceCustomerType", "Healthcare"), L(row["ADVANCECUSTOMERTYPEID"]));
        Assert.Equal(L(old.All("CompanyGroupName").Single()["ID"]), L(row["COMPANYGROUPNAMEID"]));
        Assert.Equal(1L, L(row["CREATEDBY"]));
        Assert.Equal(world.Now.Date, row["CREATEDDATE"]);
        Assert.True(B(row["ISCHANNELPARTNER"]));
        Assert.Equal("", Str(row["PRECOMPANYNAME"]));
        var address = old.Row("Address", 1001);
        Assert.Equal(old.StaticId("AddressType", "Billing"), L(address["ADDRESSTYPEID"]));
        Assert.Equal("Singapore", Str(address["COUNTRY"]));
        Assert.True(B(address["ISDEFAULT"]));
        var link = old.All("CompanyAddressLink").Single();
        Assert.Equal(1001L, L(link["COMPANYID"]));
        Assert.Equal(1001L, L(link["ADDRESSID"]));
        Assert.Equal("Tan", Str(old.Row("Contacts", 1001)["LASTNAME"]));
        Assert.True(B(old.All("CompanyContactLink").Single()["ISDEFAULT"]));
        var phone = old.All("Phone").Single();
        Assert.Equal(old.StaticId("PhoneType", "Office"), L(phone["PHONETYPEID"]));
        Assert.Equal(1001L, L(phone["CONTACTID"]));
        Assert.Equal(1001L, L(phone["COMPANYID"]));
        Assert.Equal("tan@tan.example", Str(old.All("Email").Single()["EMAIL"]));
        InStep(world);

        // Edited here: MyCRM gets the new values and, in the Pre… columns, the ones before (its lists sort by PRECOMPANYNAME).
        var admin = world.Actor(TestWorld.Admin);
        var input = new Companies.CompanyInput { Name = "Tan and Sons", Brn = "200012345K", CompanyGroup = "GMA" };
        Assert.True(world.D.Companies.Save(admin, id, input).Ok);
        Sync(world);
        row = old.Row("Company", 1001);
        Assert.Equal("Tan and Sons", Str(row["COMPANY"]));
        Assert.Equal("Tan & Sons", Str(row["PRECOMPANYNAME"]));
        Assert.Equal(old.StaticId("CompanyGroup", "SME"), L(row["PRECOMPANYGROUPID"]));
        Assert.Equal(old.StaticId("CompanyGroup", "GMA"), L(row["COMPANYGROUPID"]));
        Assert.Equal(1L, L(row["UPDATEDBY"]));
        Assert.NotEmpty(world.Store.Find<SyncChange>(Query.Where("OldTable", "Company")));
        InStep(world);
    }

    [Fact]
    public void Every_synced_field_goes_to_MyCRM_and_back_unchanged()
    {
        using var here = new TestWorld(s => s.Sync.Enabled = true);
        var id = MadeHere(here);
        var admin = here.Actor(TestWorld.Admin);
        var other = here.D.Companies.Save(admin, 0, new Companies.CompanyInput { Name = "Tan Services", Brn = "200099999K" }).Id!.Value;
        Assert.True(here.D.Companies.AddRelationship(admin, id, new Companies.RelationshipInput { CompanyId = other, Kind = "subsidiary" }).Ok);
        Assert.True(here.D.Companies.Save(admin, id, new Companies.CompanyInput { Name = "Tan & Sons", Brn = "200012345K", CompanyGroup = "GMA", Remarks = "Edited" }).Ok);
        here.Store.Insert(new CompanyCaretake { CompanyId = id, CaretakeAccountRemarks = "Caretaken", DateCreated = here.Now.Date, CreatedBy = TestWorld.Sam.Login });
        here.Territory("North", 1203, TestWorld.Sam, id, other);
        Assert.True(here.D.Users.Save(admin, TestWorld.Olga.Login, new CrmUsers.ProfileInput { Role = CrmRoles.Marketing, Designation = "Writer", Teams = [here.MiaTeam, here.KenTeam] }).Ok);
        Sync(here);
        // Another MyCRMRecords synced with the same MyCRM gets every record with the same values.
        using var there = new TestWorld(s => s.Sync.Enabled = true, oldTables: here.OldApp, crmUsers: false);
        // A row added in the last two minutes waits for the next sync.
        there.Now = here.Now.AddMinutes(5);
        Sync(there, allowLarge: true);
        void Same<T>(params string[] notInMyCRM) where T : class, IRecord, new()
        {
            string Text(T record)
            {
                var json = JObject.FromObject(record);
                foreach (var name in notInMyCRM.Append("Id")) json.Remove(name);
                return json.ToString(Newtonsoft.Json.Formatting.None);
            }
            var mine = here.Store.Find<T>().Select(Text).OrderBy(t => t, StringComparer.Ordinal).ToList();
            Assert.NotEmpty(mine);
            Assert.Equal(mine, there.Store.Find<T>().Select(Text).OrderBy(t => t, StringComparer.Ordinal).ToList());
        }
        Same<Company>();
        Same<CompanyGroupName>("IsActive", "RemovedOn", "RemovedBy");
        Same<CompanyCaretake>();
        Same<CompanyRelationship>("RemovedOn", "RemovedBy");
        Same<Address>();
        Same<CompanyAddressLink>("IsActive", "RemovedOn", "RemovedBy");
        Same<Contact>();
        Same<CompanyContactLink>();
        Same<Phone>();
        Same<ContactEmail>();
        Same<CrmTeam>();
        Same<CrmUser>("FormerTeamId", "RemovedOn", "RemovedBy");
        // The territory's name is kept here only.
        Same<Territory>("Name", "CreatedOn", "CreatedBy", "RemovedOn", "RemovedBy");
        Same<CompanyTerritory>("CreatedOn", "RemovedOn", "RemovedBy");
        Same<TerritoryMovement>();
        Assert.Equal("", there.Store.Find<Territory>().Single().Name);
        Assert.Equal(id, there.D.Companies.Get(id)!.Id);
        InStep(there);
        InStep(here);
    }

    [Fact]
    public void What_is_removed_here_is_marked_in_MyCRM_and_never_deleted()
    {
        using var world = new TestWorld(s => s.Sync.Enabled = true);
        var old = world.OldApp;
        var admin = world.Actor(TestWorld.Admin);
        var id = MadeHere(world);
        var other = world.D.Companies.Save(admin, 0, new Companies.CompanyInput { Name = "Tan Services", Brn = "200099999K" }).Id!.Value;
        Assert.True(world.D.Companies.AddRelationship(admin, id, new Companies.RelationshipInput { CompanyId = other, Kind = "subsidiary" }).Ok);
        var unused = world.D.Companies.SaveGroupName(admin, 0, new Companies.GroupNameInput { GroupName = "Unused" }).Id!.Value;
        var north = world.Territory("North", 1203, TestWorld.Sam, id, other);
        Sync(world);
        var before = new[] { "Company", "CompanyRelationship", "CompanyGroupName", "Address", "CompanyAddressLink", "Contacts", "Phone", "Email", "CUser", "Team", "Territory", "CompanyTerritory" }
            .ToDictionary(t => t, t => old.All(t).Select(r => L(r["ID"])).ToList());

        var contact = world.Store.Find<Contact>().Single();
        Assert.True(world.D.Companies.RemoveRelationship(admin, id, world.D.Companies.Relationships(id).Subsidiaries.Single().Id).Ok);
        Assert.True(world.D.Companies.RemoveGroupName(admin, unused).Ok);
        Assert.True(world.D.Companies.RemoveAddress(admin, id, world.Store.Find<Address>().Single().Id).Ok);
        // The phone taken off the contact's list, then the contact deleted.
        var edit = world.D.Companies.ContactForEdit(id, contact.Id)!.Value;
        Assert.True(world.D.Companies.SaveContact(admin, id, contact.Id, new Companies.ContactInput
        {
            LastName = "Tan", Emails = [new Companies.EmailInput { Id = edit.Emails.Single().Id, Address = "tan@tan.example", ConsentToMarketingInfo = true }],
        }).Ok);
        Assert.True(world.D.Companies.RemoveContact(admin, id, contact.Id).Ok);
        Assert.True(world.D.Territories.Remove(admin, north.Id).Ok);
        // Mark, Marketing in Ken's team, now in Mia's: his record changes team in place.
        Assert.True(world.D.Users.Save(admin, TestWorld.Mark.Login, new CrmUsers.ProfileInput { Role = CrmRoles.Marketing, Teams = [world.MiaTeam] }).Ok);
        // Sam is no longer a CRM user.
        Assert.True(world.D.Users.RemoveUserAndTerritories(admin, TestWorld.Sam.Login).Ok);
        Sync(world);

        // Every row is still there, marked.
        foreach (var (table, ids) in before) Assert.All(ids, i => Assert.Contains(old.All(table), r => L(r["ID"]) == i));
        Assert.False(B(old.All("CompanyRelationship").Single()["ISACTIVE"]));
        Assert.Equal("Unused", Str(old.All("CompanyGroupName").Single(r => L(r["ID"]) != L(old.Row("Company", 1001)["COMPANYGROUPNAMEID"]))["GROUPNAME"]));
        var address = old.All("Address").Single();
        Assert.False(B(address["ISACTIVE"]));
        Assert.True(B(address["PREISACTIVE"]));
        Assert.Single(old.All("CompanyAddressLink"));
        var oldContact = old.Row("Contacts", contact.Id);
        Assert.False(B(oldContact["ISACTIVE"]));
        Assert.True(B(oldContact["PREISACTIVE"]));
        Assert.False(B(old.All("Phone").Single()["ISACTIVE"]));
        Assert.True(B(old.All("Email").Single()["ISACTIVE"]));
        var territory = old.All("Territory").Single();
        Assert.False(B(territory["ISACTIVE"]));
        Assert.All(old.All("CompanyTerritory"), ct => Assert.False(B(ct["ISACTIVE"])));
        Assert.Contains(old.All("TerritoryMovement"), m => L(m["FROMUSERID"]) == 101 && m["TOUSERID"] is null);
        var markRows = old.All("CUser").Where(r => L(r["USERID"]) == 104).ToList();
        Assert.Single(markRows);
        Assert.Equal(L(old.All("Team").Single(t => Str(t["NAME"]) == "Mia Manager")["ID"]), L(markRows[0]["TEAMID"]));
        var samRow = old.All("CUser").Single(r => L(r["USERID"]) == 101);
        Assert.False(B(samRow["ISACTIVE"]));
        Assert.Null(samRow["TEAMID"]);
        Assert.Null(samRow["ROLEID"]);

        // The marks are read back as they were written: nothing to do on either side, and the records stay removed here.
        InStep(world);
        Assert.False(world.D.Territories.Get(north.Id)!.IsActive);
        Assert.All(world.Store.Find<CompanyTerritory>(), ct => Assert.False(ct.IsActive));
        Assert.False(world.Store.Get<Contact>(contact.Id)!.IsActive);
        Assert.False(world.Store.Get<CompanyGroupName>(unused)!.IsActive);
        Assert.False(world.Store.Find<CompanyRelationship>().Single().IsActive);
        Assert.Equal(world.MiaTeam, world.Store.Find<CrmUser>(Query.Where("Login", TestWorld.Sam.Login)).Single().FormerTeamId);
    }

    /// <summary>
    /// MyCRM's rows: a team and Sam's CUser (Sales), a group name, the company Lim Pte Ltd (created by Sara, 102) with its
    /// address, contact, phone and email, Sam's permanent territory 1203 with the company, and two movements (one 138 days
    /// old). Also rows that are not MyCRMRecords': a project's temporary territory of Ken's with the company, an inactive
    /// territory, an inactive link.
    /// </summary>
    private static (long Company, long Territory, long Phone) MyCrmRows(FakeOldTables old)
    {
        var team = old.Add("Team", new() { ["NAME"] = "Mia Manager", ["ISACTIVE"] = true });
        old.Add("CUser", new()
        {
            ["USERID"] = 101L, ["ROLEID"] = old.StaticId("Role", "Sales"), ["TEAMID"] = team, ["ISACTIVE"] = true, ["CREATEDAT"] = new DateTime(2026, 1, 5, 8, 0, 0),
            ["DESIGNATION"] = "Sales Executive", ["CID"] = 12,
        });
        var group = old.Add("CompanyGroupName", new() { ["GROUPNAME"] = "Lim Group" });
        var company = old.Add("Company", new()
        {
            ["COMPANY"] = "Lim Pte Ltd", ["BRN"] = "199912345K", ["ISACTIVE"] = true, ["COMPANYGROUPID"] = old.StaticId("CompanyGroup", "SME"), ["COMPANYGROUPNAMEID"] = group,
            ["COMPACCOUNTTYPEID"] = old.StaticId("CompAccountType", "Non-User"), ["COMPANYINDUSTRYID"] = old.StaticId("CompanyIndustry", "Food & Beverage"),
            ["ADVANCECUSTOMERTYPEID"] = old.StaticId("AdvanceCustomerType", "Wholesale & Retail"), ["CREATEDDATE"] = new DateTime(2025, 3, 1), ["CREATEDBY"] = 102L,
            ["PRECOMPANYNAME"] = "Lim Ltd", ["CUSTOMERNO"] = "C0001 #Multiple", ["REVENUE"] = 1000.25m, ["CRMCOMPANYID"] = 555L, ["SALESREMARKS"] = "Call first",
        });
        var address = old.Add("Address", new()
        {
            ["ADDRESSTYPEID"] = old.StaticId("AddressType", "Billing"), ["BLOCK"] = "2", ["STREET"] = "Lim Street", ["POSTALCODE"] = "654321", ["ISACTIVE"] = true,
            ["ISDEFAULT"] = true, ["COUNTRY"] = "Singapore", ["CREATEDAT"] = new DateTime(2025, 3, 1, 10, 0, 0), ["CREATEDBY"] = 102L,
        });
        old.Add("CompanyAddressLink", new() { ["COMPANYID"] = company, ["ADDRESSID"] = address });
        var contact = old.Add("Contacts", new() { ["FIRSTNAME"] = "Mei", ["LASTNAME"] = "Lim", ["ISACTIVE"] = true, ["CREATEDAT"] = new DateTime(2025, 3, 1, 10, 5, 0), ["CREATEDBY"] = 102L });
        old.Add("CompanyContactLink", new() { ["COMPANYID"] = company, ["CONTACTID"] = contact, ["ISDEFAULT"] = true });
        var phone = old.Add("Phone", new()
        {
            ["COMPANYID"] = company, ["CONTACTID"] = contact, ["PHONETYPEID"] = old.StaticId("PhoneType", "Mobile"), ["PHONE"] = "91234567", ["ISACTIVE"] = true,
            ["CREATEDAT"] = new DateTime(2025, 3, 1, 10, 6, 0),
        });
        old.Add("Email", new()
        {
            ["EMAIL"] = "mei@lim.example", ["COMPANYID"] = company, ["CONTACTID"] = contact, ["ISACTIVE"] = true, ["CONSENTTOMARKETINGINFO"] = true,
            ["CREATEDAT"] = new DateTime(2025, 3, 1, 10, 7, 0),
        });
        var territory = old.Add("Territory", new() { ["TERRITORY"] = 1203L, ["OWNERID"] = 101L, ["ISACTIVE"] = true, ["ISOWNER"] = true });
        old.Add("CompanyTerritory", new() { ["TERRITORYID"] = territory, ["COMPANYID"] = company, ["ISACTIVE"] = true });
        old.Add("TerritoryMovement", new() { ["COMPANYID"] = company, ["FROMUSERID"] = 107L, ["TOUSERID"] = 101L, ["UPDATEDBY"] = 102L, ["UPDATEDAT"] = new DateTime(2026, 10, 1, 9, 0, 0) });
        old.Add("TerritoryMovement", new() { ["COMPANYID"] = company, ["TOUSERID"] = 107L, ["UPDATEDBY"] = 102L, ["UPDATEDAT"] = new DateTime(2026, 6, 1, 9, 0, 0) });
        // Not MyCRMRecords': a project's temporary territory, an inactive territory, an inactive link.
        var project = old.Add("Territory", new() { ["TERRITORY"] = 1071001L, ["OWNERID"] = 107L, ["ISACTIVE"] = true, ["ISTEMPORARY"] = true });
        old.Add("CompanyTerritory", new() { ["TERRITORYID"] = project, ["COMPANYID"] = company, ["ISACTIVE"] = true });
        old.Add("Territory", new() { ["TERRITORY"] = 1203L, ["OWNERID"] = 101L, ["ISACTIVE"] = false });
        old.Add("CompanyTerritory", new() { ["TERRITORYID"] = territory, ["COMPANYID"] = company, ["ISACTIVE"] = false });
        return (company, territory, phone);
    }

    /// <summary>The rows of <see cref="MyCrmRows"/> MyCRMRecords copies (the second movement is older than 60 days).</summary>
    private const int Copied = 13;

    [Fact]
    public void MyCRM_changes_come_here_and_what_MyCRM_deletes_or_turns_off_is_marked_here()
    {
        using var world = new TestWorld(s => s.Sync.Enabled = true, crmUsers: false);
        var old = world.OldApp;
        var (companyId, territoryId, phoneId) = MyCrmRows(old);
        Assert.Equal(Copied, Sync(world).ToHere);
        var c = world.D.Companies.Get(companyId)!;
        Assert.Equal(1001, c.Id);
        Assert.Equal("Lim Pte Ltd", c.Name);
        Assert.Equal("SME", c.CompanyGroup);
        Assert.Equal("NonUser", c.CompAccountType);
        Assert.Equal(ChoiceList.Key("Food & Beverage"), c.CompanyIndustry);
        Assert.Equal("Food & Beverage", CompanyIndustries.List.Label(c.CompanyIndustry));
        Assert.Equal("WholesaleRetail", c.AdvanceCustomerType);
        Assert.Equal(TestWorld.SalesAdmin.Login, c.CreatedBy);
        Assert.Equal("Lim Ltd", c.PreName);
        Assert.Equal(1000.25m, c.Revenue);
        Assert.Equal(555, c.CrmCompanyId);
        Assert.Equal("Lim Group", world.Store.Get<CompanyGroupName>(c.CompanyGroupNameId!.Value)!.GroupName);
        var sam = world.Store.Find<CrmUser>().Single();
        Assert.Equal(TestWorld.Sam.Login, sam.Login);
        Assert.Equal(CrmRoles.Sales, sam.Role);
        Assert.Equal(12, sam.CId);
        Assert.Equal("Mia Manager", world.Store.Get<CrmTeam>(sam.TeamId!.Value)!.Name);
        Assert.Equal(CrmRoles.Sales, world.D.People.CrmRoleOf(TestWorld.Sam.Login));
        // Only the active permanent territory, with its one active company; no name (MyCRM has none).
        var territory = world.Store.Find<Territory>().Single();
        Assert.Equal(1203, territory.Number);
        Assert.Equal(TestWorld.Sam.Login, territory.Owner);
        Assert.Equal("", territory.Name);
        Assert.Equal("Territory 1203", Territories.Title(territory));
        Assert.Single(world.Store.Find<CompanyTerritory>(Query.Where("IsActive", true)));
        Assert.Equal(TestWorld.Sam.Login, world.D.Territories.OwnerOf(companyId));
        var movement = world.Store.Find<TerritoryMovement>().Single();
        Assert.Equal(TestWorld.Ken.Login, movement.FromUser);
        Assert.Equal(TestWorld.Sam.Login, movement.ToUser);
        var view = world.D.Companies.Contacts(companyId).Single();
        Assert.Equal("91234567", view.Phones.Single().Number);
        Assert.Equal("Mobile", view.Phones.Single().PhoneType);
        Assert.Equal("mei@lim.example", view.Emails.Single().Address);
        Assert.Equal(AddressTypes.Billing, world.D.Companies.Addresses(companyId).Single().AddressType);
        // Ken opens the company through MyCRM's temporary territory of his project (never copied here).
        Assert.True(world.D.Access.CanOpen(world.Actor(TestWorld.Ken), c));
        InStep(world);

        // Changed in MyCRM: changed here; a new salesperson for the territory there is its salesperson here.
        old.Change("Company", companyId, new() { ["COMPANY"] = "Lim Pte. Ltd.", ["UPDATEDDATE"] = new DateTime(2026, 10, 16), ["UPDATEDBY"] = 102L });
        old.Change("Territory", territoryId, new() { ["OWNERID"] = 107L });
        Sync(world);
        Assert.Equal("Lim Pte. Ltd.", world.D.Companies.Get(companyId)!.Name);
        Assert.Equal(TestWorld.Ken.Login, world.D.Territories.OwnerOf(companyId));

        // MyCRM turns the territory off, its user page deletes Sam's CUser and its contact page a phone: all marked here.
        old.Change("Territory", territoryId, new() { ["ISACTIVE"] = false });
        old.DeleteRows("CUser", _ => true);
        old.DeleteRows("Phone", r => L(r["ID"]) == phoneId);
        Sync(world);
        territory = world.Store.Find<Territory>().Single();
        Assert.False(territory.IsActive);
        Assert.Equal("MyCRM", territory.RemovedBy);
        Assert.All(world.Store.Find<CompanyTerritory>(), ct => Assert.False(ct.IsActive));
        Assert.Equal("", world.D.Territories.OwnerOf(companyId));
        sam = world.Store.Find<CrmUser>().Single();
        Assert.False(sam.IsActive);
        Assert.Null(sam.TeamId);
        Assert.NotNull(sam.FormerTeamId);
        Assert.Equal("MyCRM", sam.RemovedBy);
        Assert.False(world.Store.Find<Phone>().Single().IsActive);
        Assert.Empty(world.D.Companies.Contacts(companyId).Single().Phones);
        InStep(world);
    }

    [Fact]
    public void A_phone_MyCRM_keeps_for_a_contact_it_does_not_have_is_left_there_and_the_notes_say_which()
    {
        using var world = new TestWorld(s => s.Sync.Enabled = true, crmUsers: false);
        var old = world.OldApp;
        var lost = old.Add("Phone", new() { ["CONTACTID"] = 9999L, ["PHONE"] = "60000000", ["ISACTIVE"] = true, ["CREATEDAT"] = new DateTime(2025, 1, 1) });
        var outcome = Sync(world, allowLarge: true);
        var notes = string.Join("\n", outcome.Details);
        Assert.Contains($"#{lost}", notes);
        Assert.Contains("contact #9999", notes);
        Assert.Empty(world.Store.Find<Phone>());
        Assert.Equal("60000000", Str(old.Row("Phone", lost)["PHONE"]));
        InStep(world);
    }

    [Fact]
    public void Records_imported_before_the_sync_are_paired_without_copying_even_a_row_that_differs()
    {
        using var world = new TestWorld(crmUsers: false);
        var old = world.OldApp;
        var (companyId, _, _) = MyCrmRows(old);
        // As the import leaves them (MyCRM's Ids kept, OutSystemsId set); the remarks were changed here since. MyCRM never
        // edited the row (UPDATEDDATE empty), which must not count as older: MyCRM's version is kept.
        CopyAsImported(world);
        var imported = world.Store.Get<Company>(companyId)!;
        imported.Remarks = "As imported";
        world.Store.Update(imported);
        world.SettingsFile.Change(s => s.Sync.Enabled = true);

        var preview = world.D.Sync.Preview();
        Assert.Contains("nothing has been changed yet", preview.Message);
        // The first sync after the import writes nothing back to MyCRM: where they differ, MyCRM's version is kept.
        var first = Sync(world);
        Assert.Equal(0, first.ToOldApp);
        Assert.Equal(1, first.ToHere);
        Assert.Equal("", world.D.Companies.Get(companyId)!.Remarks);
        Assert.Equal(Copied, world.D.Store.Count<SyncLink>());
        InStep(world);
    }

    /// <summary>The records as the import makes them (each with MyCRM's Id), in the world's store (not through the sync).</summary>
    private static void CopyAsImported(TestWorld world)
    {
        var context = new SyncContext(world.OldApp, [], world.Now);
        foreach (var entity in CrmSync.Entities(() => world.Now)) entity.Accept(new Copy(world.OldApp, context, world.Store));
    }

    private sealed class Copy(FakeOldTables old, SyncContext context, IRecordStore store) : ISyncEntityVisitor
    {
        public void Visit<T>(SyncEntity<T> entity) where T : class, IRecord, new()
        {
            foreach (var row in old.Rows(entity.OldTable))
            {
                if (entity.FromOld(row, context, null) is not { } record) continue;
                record.Id = OldValues.Long(row, "ID");
                store.InsertKeepingIds([record]);
                context.Add(new SyncLink { Entity = entity.Name, LocalId = record.Id, OldKey = record.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            }
        }
    }

    [Fact]
    public void A_large_sync_stops_before_changing_anything()
    {
        using var world = new TestWorld(s => { s.Sync.Enabled = true; s.Sync.LargeSync = 2; }, crmUsers: false);
        MyCrmRows(world.OldApp);
        var outcome = world.D.Sync.Run("test");
        Assert.False(outcome.Ok);
        Assert.Contains("Stopped before changing anything", outcome.Message);
        Assert.Empty(world.D.Store.Find<Company>());
        Assert.NotEmpty(world.SettingsFile.Load().Sync.Stopped);
        Assert.Equal(Copied, Sync(world, allowLarge: true).ToHere);
    }

    [Fact]
    public void The_sync_runs_every_10_minutes_once_turned_on()
    {
        var settings = new CrmSettings();
        Assert.False(settings.Sync.Enabled);
        Assert.Equal(10, settings.Sync.IntervalMinutes);
    }

    [Fact]
    public void The_import_counts_MyCRM_records_and_changes_nothing()
    {
        using var world = new TestWorld(crmUsers: false);
        MyCrmRows(world.OldApp);
        var counts = world.D.Import.Preview();
        Assert.Equal(1, counts.Found["Companies"]);
        Assert.Equal(1, counts.Found["CRM user records"]);
        // Only the active permanent territory and its active link.
        Assert.Equal(1, counts.Found["Territories (active, permanent)"]);
        Assert.Equal(1, counts.Found["Companies of territories"]);
        Assert.Equal(2, counts.Found["Territory movements"]);
        Assert.Empty(world.Store.Find<Company>());
        Assert.Equal(0, world.OldApp.Commits);
    }

    [Theory]
    [InlineData(@"RSP\jsmith", @"RSP\jsmith")]
    [InlineData(@"RSP\jsmith@ricoh.sg", @"RSP\jsmith")]
    [InlineData(@"RSP\alexthan@ricoh.sg", @"RSP\alexthan")]
    [InlineData(@"OTHER\x@y", @"OTHER\x")]
    [InlineData("jsmith@ricoh.sg", @"RSP\jsmith")]
    [InlineData("jsmith", @"RSP\jsmith")]
    public void An_OutSystems_username_becomes_the_Windows_login(string username, string login) => Assert.Equal(login, OutSystemsImport.LoginOf(username, "RSP"));
}
