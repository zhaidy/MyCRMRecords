namespace MyCRMRecords.Tests;

/// <summary>
/// The companies (MyCRM's CompanyDetail, CompanyDetails and its blocks): the company form with its BRN check and Pre…
/// fields, the number of employees and sales remarks, group names, relationships, addresses and contacts.
/// </summary>
public sealed class CompanyTests
{
    private static Companies.CompanyInput Input(string name, string brn, bool homeUser = false) =>
        new() { Name = name, Brn = brn, IsHomeUser = homeUser, Website = "www.tan.example", CompanyGroup = "SME", CompAccountType = "User", Remarks = "Good customer" };

    [Fact]
    public void A_new_company_is_saved_with_its_creator_and_no_previous_values()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        var result = world.D.Companies.Save(sara, 0, Input("Tan & Sons", "200012345K"));
        Assert.True(result.Ok, result.Message);
        Assert.Equal("Company 'Tan & Sons' was successfully created.", result.Message);
        var c = world.D.Companies.Get(result.Id!.Value)!;
        Assert.Equal(TestWorld.SalesAdmin.Login, c.CreatedBy);
        Assert.Equal(world.Now.Date, c.CreatedDate);
        Assert.Equal("", c.UpdatedBy);
        Assert.Null(c.UpdatedDate);
        // MyCRM leaves the Pre… columns empty on a new company.
        Assert.Equal("", c.PreName);
        Assert.Equal("", c.PreBrn);
        Assert.False(c.PreIsActive);
        Assert.Equal("SME", c.CompanyGroup);
    }

    [Fact]
    public void Editing_a_company_keeps_the_values_just_before_the_edit_in_the_Pre_fields()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        var id = world.D.Companies.Save(sara, 0, Input("Tan & Sons", "200012345K")).Id!.Value;
        var c = world.D.Companies.Get(id)!;
        // Fields the form does not show are copied too.
        c.Phone = "61234567";
        c.Revenue = 1500.5m;
        c.CompanyIndustry = "Legal";
        c.NoOfEmployees = 12;
        world.Store.Update(c);
        world.Now = world.Now.AddDays(1);
        var edited = Input("Tan and Sons", "200012345K");
        edited.CompanyGroup = "GMA";
        Assert.True(world.D.Companies.Save(sara, id, edited).Ok);
        c = world.D.Companies.Get(id)!;
        Assert.Equal("Tan and Sons", c.Name);
        Assert.Equal("Tan & Sons", c.PreName);
        Assert.Equal("200012345K", c.PreBrn);
        Assert.Equal("SME", c.PreCompanyGroup);
        Assert.True(c.PreIsActive);
        Assert.Equal("61234567", c.PrePhone);
        Assert.Equal(1500.5m, c.PreRevenue);
        Assert.Equal("Legal", c.PreCompanyIndustry);
        Assert.Equal(12, c.PreNoOfEmployees);
        Assert.Equal(TestWorld.SalesAdmin.Login, c.UpdatedBy);
        Assert.Equal(world.Now.Date, c.UpdatedDate);
    }

    [Fact]
    public void A_BRN_another_company_has_is_refused_for_new_and_edited_companies_but_not_for_a_home_user()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        Assert.True(world.D.Companies.Save(sara, 0, Input("Tan & Sons", "200012345K")).Ok);
        var other = world.D.Companies.Save(sara, 0, Input("Lim Pte Ltd", "199912345K")).Id!.Value;

        var again = world.D.Companies.Save(sara, 0, Input("Tan Again", " 200012345k "));
        Assert.False(again.Ok);
        Assert.Contains(again.Errors, e => e.Field == "brn" && e.Message.Contains("Tan & Sons"));
        // MyCRM checked new companies only: an edit to another company's BRN is refused here too.
        var edit = world.D.Companies.Save(sara, other, Input("Lim Pte Ltd", "200012345K"));
        Assert.False(edit.Ok);
        Assert.Contains(edit.Errors, e => e.Field == "brn");
        // Saving a company with its own BRN is fine.
        Assert.True(world.D.Companies.Save(sara, other, Input("Lim Pte Ltd", "199912345K")).Ok);
        // "Is HOME USER": the BRN may be empty or shared, and is not checked.
        Assert.True(world.D.Companies.Save(sara, 0, Input("Mr Tan (home)", "200012345K", homeUser: true)).Ok);
        Assert.True(world.D.Companies.Save(sara, 0, Input("Ms Lee (home)", "", homeUser: true)).Ok);
        var noBrn = world.D.Companies.Save(sara, 0, Input("No BRN", ""));
        Assert.Contains(noBrn.Errors, e => e.Field == "brn" && e.Message == Companies.Required);
        Assert.Contains(world.D.Companies.Save(sara, 0, Input("", "123")).Errors, e => e.Field == "name");
    }

    [Fact]
    public void Only_Sales_Admins_save_the_company_form()
    {
        using var world = new TestWorld();
        foreach (var user in new[] { TestWorld.Sam, TestWorld.Mia, TestWorld.Dina, TestWorld.Olga })
            Assert.Equal(ResultCodes.Forbidden, world.D.Companies.Save(world.Actor(user), 0, Input("Tan", "1")).Code);
        Assert.True(world.D.Companies.Save(world.Actor(TestWorld.SalesAdmin), 0, Input("Tan", "1")).Ok);
        Assert.True(world.Actor(TestWorld.SalesAdmin).IsSalesAdmin);
        Assert.True(world.Actor(TestWorld.Admin).IsSalesAdmin);
    }

    [Fact]
    public void The_number_of_employees_and_the_sales_remarks_record_who_changed_them()
    {
        using var world = new TestWorld();
        var c = world.Company("Tan & Sons", "200012345K");
        world.Territory("North", 1, TestWorld.Sam, c.Id);
        var sam = world.Actor(TestWorld.Sam);
        world.Now = world.Now.AddDays(2);
        Assert.True(world.D.Companies.SaveEmployees(sam, c.Id, new Companies.EmployeesInput { NoOfEmployees = 40 }).Ok);
        var saved = world.D.Companies.Get(c.Id)!;
        Assert.Equal(40, saved.NoOfEmployees);
        // MyCRM did not record who and when: MyCRMRecords does.
        Assert.Equal(TestWorld.Sam.Login, saved.UpdatedBy);
        Assert.Equal(world.Now.Date, saved.UpdatedDate);
        Assert.Equal("", saved.PreName);
        Assert.True(world.D.Companies.SaveSalesRemarks(sam, c.Id, new Companies.RemarksInput { SalesRemarks = "Visit in December" }).Ok);
        Assert.Equal("Visit in December", world.D.Companies.Get(c.Id)!.SalesRemarks);
        Assert.False(world.D.Companies.SaveEmployees(sam, c.Id, new Companies.EmployeesInput { NoOfEmployees = -1 }).Ok);
        // Someone who cannot open the company cannot change it.
        Assert.Equal(ResultCodes.Forbidden, world.D.Companies.SaveEmployees(world.Actor(TestWorld.Ken), c.Id, new Companies.EmployeesInput { NoOfEmployees = 1 }).Code);
    }

    [Fact]
    public void Numbers_of_employees_are_uploaded_by_BRN_by_MyCRM_Admins()
    {
        using var world = new TestWorld();
        var a = world.Company("Alpha", "A1234");
        world.Company("Beta", "B1234");
        var rows = new List<(string, string)> { ("A1234", "15"), ("Z9999", "3"), ("B1234", "x") };
        Assert.Equal(ResultCodes.Forbidden, world.D.Companies.UploadEmployees(world.Actor(TestWorld.SalesAdmin), rows).Code);
        var result = world.D.Companies.UploadEmployees(world.Actor(TestWorld.Admin), rows);
        Assert.True(result.Ok);
        Assert.StartsWith("1 records uploaded successfully; 2 skipped", result.Message);
        Assert.Equal(15, world.D.Companies.Get(a.Id)!.NoOfEmployees);
    }

    [Fact]
    public void A_group_name_is_taken_away_only_when_no_company_has_it()
    {
        using var world = new TestWorld();
        var sara = world.Actor(TestWorld.SalesAdmin);
        var group = world.D.Companies.SaveGroupName(sara, 0, new Companies.GroupNameInput { GroupName = "Tan Group" });
        Assert.True(group.Ok, group.Message);
        Assert.False(world.D.Companies.SaveGroupName(sara, 0, new Companies.GroupNameInput { GroupName = "tan group" }).Ok);
        var input = Input("Tan & Sons", "200012345K");
        input.CompanyGroupNameId = group.Id;
        var company = world.D.Companies.Save(sara, 0, input).Id!.Value;
        Assert.Equal(ResultCodes.Conflict, world.D.Companies.RemoveGroupName(sara, group.Id!.Value).Code);
        input.CompanyGroupNameId = null;
        Assert.True(world.D.Companies.Save(sara, company, input).Ok);
        // No company has it now (only as a previous value): it can be taken away here; MyCRM keeps it.
        var removed = world.D.Companies.RemoveGroupName(sara, group.Id!.Value);
        Assert.True(removed.Ok, removed.Message);
        var kept = world.Store.Get<CompanyGroupName>(group.Id!.Value)!;
        Assert.False(kept.IsActive);
        Assert.Equal("Sara Admin", kept.RemovedBy);
        Assert.Empty(world.D.Companies.GroupNames());
        Assert.Equal(ResultCodes.Forbidden, world.D.Companies.SaveGroupName(world.Actor(TestWorld.Sam), 0, new Companies.GroupNameInput { GroupName = "X" }).Code);
    }

    [Fact]
    public void A_company_has_one_parent_and_no_company_is_its_own_parent_or_below_itself()
    {
        using var world = new TestWorld();
        var admin = world.Actor(TestWorld.Admin);
        var holding = world.Company("Holding", "H1000");
        var tan = world.Company("Tan", "T1000");
        var lim = world.Company("Lim", "L1000");
        var lee = world.Company("Lee", "E1000");
        Companies.RelationshipInput As(long company, string kind) => new() { CompanyId = company, Kind = kind };

        Assert.False(world.D.Companies.AddRelationship(admin, tan.Id, As(tan.Id, "parent")).Ok);
        Assert.True(world.D.Companies.AddRelationship(admin, tan.Id, As(holding.Id, "parent")).Ok);
        var second = world.D.Companies.AddRelationship(admin, tan.Id, As(lim.Id, "parent"));
        Assert.False(second.Ok);
        Assert.Contains("only have 1 parent", second.Message);
        // A sibling is another subsidiary of the parent.
        Assert.True(world.D.Companies.AddRelationship(admin, tan.Id, As(lim.Id, "sibling")).Ok);
        var (parent, siblings, subsidiaries) = world.D.Companies.Relationships(tan.Id);
        Assert.Equal(holding.Id, parent!.CompanyId);
        Assert.Equal(lim.Id, siblings.Single().RelatedCompanyId);
        Assert.Empty(subsidiaries);
        // The company page shows each related company's territory beside its name.
        world.Territory("North", 1, TestWorld.Sam, holding.Id);
        var shown = world.Json(world.Call("GET", "companies/" + tan.Id, user: TestWorld.Admin))["relationships"]!;
        Assert.Equal("North", (string)shown["parent"]!["territory"]!);
        Assert.Equal("", (string)shown["siblings"]![0]!["territory"]!);
        // The holding cannot be put below Tan (it is above it), nor Lim (it has a parent).
        Assert.False(world.D.Companies.AddRelationship(admin, tan.Id, As(holding.Id, "subsidiary")).Ok);
        Assert.False(world.D.Companies.AddRelationship(admin, tan.Id, As(lim.Id, "subsidiary")).Ok);
        Assert.True(world.D.Companies.AddRelationship(admin, tan.Id, As(lee.Id, "subsidiary")).Ok);
        // Lee is below Tan: it cannot become the holding's parent.
        Assert.False(world.D.Companies.AddRelationship(admin, holding.Id, As(lee.Id, "parent")).Ok);
        Assert.Single(world.D.Companies.Relationships(holding.Id).Subsidiaries.Where(r => r.RelatedCompanyId == tan.Id));

        // Removed: inactive, not deleted; only a relationship the page shows.
        var leeLink = world.D.Companies.Relationships(tan.Id).Subsidiaries.Single();
        Assert.False(world.D.Companies.RemoveRelationship(admin, holding.Id, leeLink.Id).Ok);
        Assert.True(world.D.Companies.RemoveRelationship(admin, tan.Id, leeLink.Id).Ok);
        Assert.False(world.Store.Get<CompanyRelationship>(leeLink.Id)!.IsActive);
        Assert.Empty(world.D.Companies.Relationships(tan.Id).Subsidiaries);
        Assert.Equal(3, world.Store.Count<CompanyRelationship>());
    }

    [Fact]
    public void Marketing_cannot_change_addresses_contacts_or_relationships()
    {
        using var world = new TestWorld();
        var c = world.Company("Tan", "T1000");
        var mark = world.Actor(TestWorld.Mark);
        Assert.False(mark.CanEditContacts);
        Assert.True(world.Actor(TestWorld.Sam).CanEditContacts);
        Assert.Equal(ResultCodes.Forbidden, world.D.Companies.SaveAddress(mark, c.Id, 0, Address()).Code);
        Assert.Equal(ResultCodes.Forbidden, world.D.Companies.SaveContact(mark, c.Id, 0, new Companies.ContactInput { LastName = "Tan" }).Code);
        Assert.Equal(ResultCodes.Forbidden, world.D.Companies.AddRelationship(mark, c.Id, new Companies.RelationshipInput { CompanyId = 1, Kind = "parent" }).Code);
    }

    private static Companies.AddressInput Address(bool isDefault = false, string block = "1") => new()
    {
        IsDefault = isDefault, AddressType = AddressTypes.Billing, Block = block, Street = "Tan Road", PostalCode = "123456", UnitNumber = "#01-02",
    };

    [Fact]
    public void The_default_address_can_be_saved_again_and_a_second_default_is_refused()
    {
        using var world = new TestWorld();
        var c = world.Company("Tan", "T1000");
        var admin = world.Actor(TestWorld.Admin);
        var first = world.D.Companies.SaveAddress(admin, c.Id, 0, Address(isDefault: true));
        Assert.True(first.Ok, first.Message);
        var address = world.Store.Get<Address>(first.Id!.Value)!;
        Assert.Equal("Singapore", address.Country);
        Assert.Equal(TestWorld.Admin.Login, address.CreatedBy);
        Assert.Equal("1 Tan Road, #01-02, 123456", Companies.AddressText(address));
        Assert.Single(world.Store.Find<CompanyAddressLink>(Query.Where("CompanyId", c.Id)));

        // MyCRM refused this ("There is already a default address for this company"): the address itself is the default.
        world.Now = world.Now.AddHours(1);
        var again = world.D.Companies.SaveAddress(admin, c.Id, address.Id, Address(isDefault: true, block: "2"));
        Assert.True(again.Ok, again.Message);
        address = world.Store.Get<Address>(address.Id)!;
        Assert.Equal("2", address.Block);
        Assert.Equal("1", address.PreBlock);
        Assert.Equal(AddressTypes.Billing, address.PreAddressType);
        Assert.True(address.PreIsActive);
        Assert.Equal(world.Now, address.UpdatedAt);

        var second = world.D.Companies.SaveAddress(admin, c.Id, 0, Address(isDefault: true, block: "3"));
        Assert.False(second.Ok);
        Assert.Contains(second.Errors, e => e.Field == "isDefault" && e.Message == "There is already a default address for this company");
        var missing = world.D.Companies.SaveAddress(admin, c.Id, 0, new Companies.AddressInput());
        Assert.Contains(missing.Errors, e => e.Field == "addressType");
        Assert.Contains(missing.Errors, e => e.Field == "block");
        Assert.Contains(missing.Errors, e => e.Field == "street");
    }

    [Fact]
    public void A_deleted_address_is_inactive_as_MyCRM_marks_it()
    {
        using var world = new TestWorld();
        var c = world.Company("Tan", "T1000");
        var admin = world.Actor(TestWorld.Admin);
        var id = world.D.Companies.SaveAddress(admin, c.Id, 0, Address()).Id!.Value;
        Assert.True(world.D.Companies.SetAddressType(admin, c.Id, id, new Companies.AddressTypeInput { AddressType = AddressTypes.Delivery }).Ok);
        Assert.Equal(AddressTypes.Delivery, world.Store.Get<Address>(id)!.AddressType);
        Assert.Equal(TestWorld.Admin.Login, world.Store.Get<Address>(id)!.UpdatedBy);
        var removed = world.D.Companies.RemoveAddress(admin, c.Id, id);
        Assert.Equal("Address has been deleted.", removed.Message);
        var address = world.Store.Get<Address>(id)!;
        Assert.False(address.IsActive);
        Assert.True(address.PreIsActive);
        Assert.False(world.Store.Find<CompanyAddressLink>().Single().IsActive);
        Assert.Empty(world.D.Companies.Addresses(c.Id));
        // The list's postal code is that of the company's first address, active or not (as MyCRM).
        Assert.Equal("123456", world.D.Companies.PostalCodes([c.Id])[c.Id]);
    }

    private static Companies.ContactInput Contact(string last, bool isDefault = false) => new()
    {
        Salutation = "Mr", FirstName = "Ah", LastName = last, Department = "Finance", Designation = "Manager", IsDefault = isDefault, IsEqList = true,
        Emails = [new Companies.EmailInput { Address = last.ToLowerInvariant() + "@tan.example", ConsentToMarketingInfo = true }],
        Phones = [new Companies.PhoneInput { Number = "61234567", PhoneType = "Office" }],
    };

    [Fact]
    public void A_contact_is_saved_with_its_emails_and_phones_and_one_primary_contact()
    {
        using var world = new TestWorld();
        var c = world.Company("Tan", "T1000");
        var admin = world.Actor(TestWorld.Admin);
        var tan = world.D.Companies.SaveContact(admin, c.Id, 0, Contact("Tan", isDefault: true));
        Assert.True(tan.Ok, tan.Message);
        Assert.Equal("Contact was successfully created.", tan.Message);
        var lim = world.D.Companies.SaveContact(admin, c.Id, 0, Contact("Lim", isDefault: true)).Id!.Value;
        // One primary contact: the last one ticked.
        var contacts = world.D.Companies.Contacts(c.Id);
        Assert.Equal(lim, contacts[0].Contact.Id);
        Assert.True(contacts[0].IsDefault);
        Assert.False(contacts[1].IsDefault);
        Assert.Equal("Mr Ah Tan", Companies.ContactName(contacts[1].Contact));
        Assert.Equal("61234567", contacts[1].Phones.Single().Number);
        Assert.Equal("tan@tan.example", contacts[1].Emails.Single().Address);

        // Edited: the email changed, the phone taken off the list (inactive), a second phone added; Pre… from before.
        var edit = world.D.Companies.ContactForEdit(c.Id, tan.Id!.Value)!.Value;
        var input = Contact("Tan Senior");
        input.Emails = [new Companies.EmailInput { Id = edit.Emails.Single().Id, Address = "senior@tan.example", ConsentToMarketingInfo = false }];
        input.Phones = [new Companies.PhoneInput { Number = "98765432", PhoneType = "Mobile" }];
        world.Now = world.Now.AddHours(1);
        Assert.True(world.D.Companies.SaveContact(admin, c.Id, tan.Id!.Value, input).Ok);
        var contact = world.Store.Get<Contact>(tan.Id!.Value)!;
        Assert.Equal("Tan Senior", contact.LastName);
        Assert.Equal("Tan", contact.PreLastName);
        Assert.True(contact.PreIsActive);
        Assert.Equal(world.Now, contact.UpdatedAt);
        var phones = world.Store.Find<Phone>(Query.Where("ContactId", (long?)contact.Id));
        Assert.Equal(2, phones.Count);
        Assert.False(phones.Single(p => p.Number == "61234567").IsActive);
        Assert.True(phones.Single(p => p.Number == "98765432").IsActive);
        var email = world.Store.Find<ContactEmail>(Query.Where("ContactId", (long?)contact.Id)).Single();
        Assert.Equal("senior@tan.example", email.Address);
        Assert.False(email.ConsentToMarketingInfo);
        Assert.Equal(c.Id, email.CompanyId);

        // Deleted: inactive (with PreIsActive, as MyCRM); its phones and emails are left as they are.
        Assert.Equal("Contact was deleted.", world.D.Companies.RemoveContact(admin, c.Id, contact.Id).Message);
        contact = world.Store.Get<Contact>(contact.Id)!;
        Assert.False(contact.IsActive);
        Assert.True(contact.PreIsActive);
        Assert.Single(world.D.Companies.Contacts(c.Id));
        Assert.True(world.Store.Get<ContactEmail>(email.Id)!.IsActive);
    }

    [Fact]
    public void A_contact_needs_a_last_name_valid_emails_and_phone_numbers()
    {
        using var world = new TestWorld();
        var c = world.Company("Tan", "T1000");
        var admin = world.Actor(TestWorld.Admin);
        var input = Contact("");
        input.Emails = [new Companies.EmailInput { Address = "not an email" }];
        input.Phones = [new Companies.PhoneInput { Number = " " }];
        var result = world.D.Companies.SaveContact(admin, c.Id, 0, input);
        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Field == "lastName");
        Assert.Contains(result.Errors, e => e.Field == "emails.0" && e.Message == "Invalid Email");
        // MyCRM said "Invalid Email" for a phone too.
        Assert.Contains(result.Errors, e => e.Field == "phones.0" && e.Message == "Invalid phone number");
        Assert.Empty(world.Store.Find<Contact>());

        // "Is Eq. List" is for a contact with an email only.
        var noEmail = Contact("Lim");
        noEmail.Emails = [];
        var id = world.D.Companies.SaveContact(admin, c.Id, 0, noEmail).Id!.Value;
        Assert.False(world.Store.Get<Contact>(id)!.IsEqList);
        Assert.True(world.Store.Get<Contact>(world.D.Companies.SaveContact(admin, c.Id, 0, Contact("Lee")).Id!.Value)!.IsEqList);
    }

    [Fact]
    public void The_company_list_shows_the_companies_people_may_open_by_name()
    {
        using var world = new TestWorld();
        var tan = world.Company("Tan & Sons", "T1000");
        var lim = world.Company("Lim Pte Ltd", "L1000");
        var gone = world.Company("Gone Pte Ltd", "G1000");
        gone.IsActive = false;
        world.Store.Update(gone);
        world.Territory("North", 1, TestWorld.Sam, tan.Id);
        Assert.True(world.D.Companies.SaveAddress(world.Actor(TestWorld.Admin), lim.Id, 0, Address()).Ok);

        var all = world.D.Companies.List(world.Actor(TestWorld.Admin), new Companies.ListFilter());
        Assert.Equal(2, all.Total);
        Assert.Equal(new[] { "Lim Pte Ltd", "Tan & Sons" }, all.Rows.Select(r => r.Name));
        Assert.Equal(TestWorld.Sam.Login, all.Rows[1].Owner);
        Assert.Equal("123456", all.Rows[0].PostalCode);
        Assert.Single(world.D.Companies.List(world.Actor(TestWorld.Admin), new Companies.ListFilter(Q: "l1000")).Rows);
        Assert.Single(world.D.Companies.List(world.Actor(TestWorld.Admin), new Companies.ListFilter(PostalCode: "1234")).Rows);
        // Sam sees his territory's company only; Mia (his manager) too; Ken nothing.
        Assert.Equal(new[] { tan.Id }, world.D.Companies.List(world.Actor(TestWorld.Sam), new Companies.ListFilter()).Rows.Select(r => r.Id));
        Assert.Equal(new[] { tan.Id }, world.D.Companies.List(world.Actor(TestWorld.Mia), new Companies.ListFilter()).Rows.Select(r => r.Id));
        Assert.Empty(world.D.Companies.List(world.Actor(TestWorld.Ken), new Companies.ListFilter()).Rows);
    }

    [Fact]
    public void The_company_list_shows_mine_or_those_with_no_salesperson()
    {
        using var world = new TestWorld();
        var tan = world.Company("Tan & Sons", "T1000");
        var lim = world.Company("Lim Pte Ltd", "L1000");
        var kwan = world.Company("Kwan Trading", "K1000");
        var vacant = world.Company("Vacant Pte Ltd", "V1000");
        var ended = world.Company("Ended Pte Ltd", "E1000");
        var gone = world.Company("Gone Pte Ltd", "G1000");
        gone.IsActive = false;
        world.Store.Update(gone);
        world.Territory("North", 1, TestWorld.Sam, tan.Id);
        world.Territory("East", 2, TestWorld.Ken, kwan.Id);
        // A territory with no salesperson still counts as a permanent territory (MyCRM's "Unassigned Companies").
        world.Territory("West", 3, null, vacant.Id);
        var old = world.Territory("South", 4, TestWorld.Ken, ended.Id);
        Assert.True(world.D.Territories.Remove(world.Actor(TestWorld.Admin), old.Id).Ok);

        Companies.ListFilter Show(string show, string? q = null) => new(Q: q, Show: show);
        // No salesperson: active companies with no active link to an active permanent territory.
        var none = world.D.Companies.List(world.Actor(TestWorld.SalesAdmin), Show(Companies.NoSalesperson));
        Assert.Equal(new[] { "Ended Pte Ltd", "Lim Pte Ltd" }, none.Rows.Select(r => r.Name));
        Assert.Equal(2, none.Total);
        Assert.Equal(new[] { lim.Id }, world.D.Companies.List(world.Actor(TestWorld.SalesAdmin), Show(Companies.NoSalesperson, "l1000")).Rows.Select(r => r.Id));
        // Mine: the person's (and by their role their team's) permanent territories. Mia manages Sam; Ken has his own.
        Assert.Equal(new[] { tan.Id }, world.D.Companies.List(world.Actor(TestWorld.Mia), Show(Companies.Mine)).Rows.Select(r => r.Id));
        Assert.Equal(new[] { kwan.Id }, world.D.Companies.List(world.Actor(TestWorld.Ken), Show(Companies.Mine)).Rows.Select(r => r.Id));
        Assert.Empty(world.D.Companies.List(world.Actor(TestWorld.Admin), Show(Companies.Mine)).Rows);
        // Someone who opens only their territories' companies has none without a salesperson.
        Assert.Empty(world.D.Companies.List(world.Actor(TestWorld.Sam), Show(Companies.NoSalesperson)).Rows);

        var row = world.D.Companies.List(world.Actor(TestWorld.Admin), new Companies.ListFilter(Q: "t1000")).Rows.Single();
        Assert.Equal("North", row.Territory);
    }

    [Fact]
    public void The_company_page_shows_movements_to_all_but_Sales_and_Telemarketers()
    {
        using var world = new TestWorld();
        var tan = world.Company("Tan & Sons", "T1000");
        world.Territory("North", 1, TestWorld.Sam, tan.Id);
        var asSam = world.Json(world.Call("GET", "companies/" + tan.Id, user: TestWorld.Sam));
        Assert.Equal("Tan & Sons", (string?)asSam["company"]!["name"]);
        Assert.Equal("Sam Sales", (string?)asSam["owner"]!["name"]);
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, asSam["movements"]!.Type);
        var asMia = world.Json(world.Call("GET", "companies/" + tan.Id, user: TestWorld.Mia));
        Assert.Equal("Sam Sales", (string?)asMia["movements"]![0]!["to"]);
        Assert.Equal("Mia Manager", (string?)asMia["movements"]![0]!["toTeam"]);
        Assert.Equal(403, world.Call("GET", "companies/" + tan.Id, user: TestWorld.Ken).Status);
    }
}
