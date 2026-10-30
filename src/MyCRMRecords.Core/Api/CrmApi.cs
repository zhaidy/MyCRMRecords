using System.Globalization;
using MyCRMRecords.Data;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Api;

/// <summary>
/// The API behind MyCRMRecords's pages (api.ashx/...), as the signed-in person: the companies with their addresses,
/// contacts and relationships, the territories, the CRM users and teams, and the people to choose from. Every change is
/// checked again in the domain.
/// </summary>
public sealed class CrmApi(CrmApp w, Func<string, IReadOnlyList<DirectoryPerson>>? findInDirectory = null)
{
    public sealed class PersonInput
    {
        public string? Login { get; set; }
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
    }

    public ApiResponse Handle(string method, string path, string body, IReadOnlyDictionary<string, string> query, ApiUser user)
    {
        var parts = path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return ApiResponse.NotFound();
        var get = method == "GET";
        var post = method == "POST";
        var actor = user.ToActor(w.People);
        string Q(string name) => query.TryGetValue(name, out var v) ? v ?? "" : "";
        long Id(int index) => parts.Length > index && long.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;
        int Skip() => int.TryParse(Q("skip"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var skip) ? Math.Max(0, skip) : 0;
        bool Flag(string name) => Q(name) is "1" or "true" or "yes";
        // What this person changes in MyCRM is recorded as theirs.
        using var author = MyCrmStore.As(actor.ChangedBy);
        try
        {
            switch (parts[0])
            {
                case "session" when get:
                    return ApiResponse.Of(new
                    {
                        user = new
                        {
                            login = actor.Login, name = actor.DisplayName, email = actor.Email, roles = actor.Roles, changedBy = actor.ChangedBy,
                            crmRole = actor.CrmRole, crmRoleLabel = CrmRoles.List.Label(actor.CrmRole),
                        },
                        can = Can(actor),
                        roles = Roles.All,
                        // Until an administrator lets it, MyCRMRecords only reads MyCRM's data: the pages say so.
                        myCrm = new { writes = w.CanWrite, writesOff = w.CanWrite ? "" : MyCrmStore.WritesOff },
                    });

                case "lists" when get:
                    return Lists(actor);

                // ---- People to choose from ----
                case "people" when get && parts.Length == 1:
                    return ApiResponse.Of(new { people = w.People.Everyone().Select(u => new { u.Login, name = u.DisplayName, u.Email }) });
                case "people" when get && parts.Length == 2 && parts[1] == "search":
                {
                    var q = Q("q").Trim();
                    if (q.Length < 2 || findInDirectory is null) return ApiResponse.Of(new { people = Array.Empty<DirectoryPerson>() });
                    try { return ApiResponse.Of(new { people = findInDirectory(q) }); }
                    catch (Exception ex) { return ApiResponse.Fail(503, "DIRECTORY_UNAVAILABLE", "Active Directory could not be searched: " + ex.Message); }
                }
                case "people" when post && parts.Length == 1:
                {
                    var input = Input.Read<PersonInput>(body);
                    var login = (input.Login ?? "").Trim();
                    if (login.Length == 0 || !Strings.IsEmail(input.Email)) return ApiResponse.Fail(400, ResultCodes.Validation, "Choose someone with an email address.");
                    var person = w.People.Remember(login, input.DisplayName ?? "", input.Email ?? "");
                    return ApiResponse.Ok($"{person.DisplayName} can be chosen now.", new { person = new { person.Login, name = person.DisplayName, person.Email } });
                }

                // ---- Companies ----
                case "companies" when get && parts.Length == 1:
                    return ListCompanies(actor, new Companies.ListFilter(Q("q"), Q("customerType"),
                        long.TryParse(Q("groupName"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var group) ? group : null, Q("postalCode"), Skip(), Q("show")));
                case "companies" when get && parts.Length == 2:
                    return CompanyDetail(actor, Id(1));
                case "companies" when post && parts.Length == 2:
                    return ApiResponse.Result(w.Companies.Save(actor, Id(1), Input.Read<Companies.CompanyInput>(body)));
                case "companies" when post && parts.Length == 3 && parts[2] == "employees":
                    return ApiResponse.Result(w.Companies.SaveEmployees(actor, Id(1), Input.Read<Companies.EmployeesInput>(body)));
                case "companies" when post && parts.Length == 3 && parts[2] == "remarks":
                    return ApiResponse.Result(w.Companies.SaveSalesRemarks(actor, Id(1), Input.Read<Companies.RemarksInput>(body)));
                case "companies" when post && parts.Length == 3 && parts[2] == "relationships":
                    return ApiResponse.Result(w.Companies.AddRelationship(actor, Id(1), Input.Read<Companies.RelationshipInput>(body)));
                case "companies" when post && parts.Length == 5 && parts[2] == "relationships" && parts[4] == "remove":
                    return ApiResponse.Result(w.Companies.RemoveRelationship(actor, Id(1), Id(3)));
                case "companies" when post && parts.Length == 4 && parts[2] == "addresses":
                    return ApiResponse.Result(w.Companies.SaveAddress(actor, Id(1), Id(3), Input.Read<Companies.AddressInput>(body)));
                case "companies" when post && parts.Length == 5 && parts[2] == "addresses" && parts[4] == "type":
                    return ApiResponse.Result(w.Companies.SetAddressType(actor, Id(1), Id(3), Input.Read<Companies.AddressTypeInput>(body)));
                case "companies" when post && parts.Length == 5 && parts[2] == "addresses" && parts[4] == "remove":
                    return ApiResponse.Result(w.Companies.RemoveAddress(actor, Id(1), Id(3)));
                case "companies" when get && parts.Length == 4 && parts[2] == "contacts":
                    return ContactEditor(actor, Id(1), Id(3));
                case "companies" when post && parts.Length == 4 && parts[2] == "contacts":
                    return ApiResponse.Result(w.Companies.SaveContact(actor, Id(1), Id(3), Input.Read<Companies.ContactInput>(body)));
                case "companies" when post && parts.Length == 5 && parts[2] == "contacts" && parts[4] == "remove":
                    return ApiResponse.Result(w.Companies.RemoveContact(actor, Id(1), Id(3)));

                // ---- Group names (Sales Admins, from the company form) ----
                case "groupnames" when get && parts.Length == 1:
                    return ApiResponse.Of(new { groupNames = w.Companies.GroupNames().Select(g => new { g.Id, g.GroupName }) });
                case "groupnames" when post && parts.Length == 2:
                    return ApiResponse.Result(w.Companies.SaveGroupName(actor, Id(1), Input.Read<Companies.GroupNameInput>(body)));
                case "groupnames" when post && parts.Length == 3 && parts[2] == "remove":
                    return ApiResponse.Result(w.Companies.RemoveGroupName(actor, Id(1)));

                // ---- Territories (Sales Admins and MD) ----
                case "territories" when !actor.CanManageTerritories:
                    return ApiResponse.Forbidden("Territories are kept by Sales Admins.");
                case "territories" when get && parts.Length == 1:
                    return ListTerritories(Flag("inactive"));
                case "territories" when get && parts.Length == 2 && parts[1] == "search":
                {
                    var (total, rows) = w.Territories.Search(Q("q"), Flag("unassigned"), Skip());
                    return ApiResponse.Of(new { total, skip = Skip(), take = Territories.PageSize, rows });
                }
                case "territories" when get && parts.Length == 2:
                    return TerritoryDetail(Id(1), Q("q"), Skip());
                case "territories" when get && parts.Length == 3 && parts[2] == "history":
                    return TerritoryHistory(actor, Id(1));
                case "territories" when post && parts.Length == 2:
                    return ApiResponse.Result(w.Territories.Save(actor, Id(1), Input.Read<Territories.TerritoryInput>(body)));
                case "territories" when post && parts.Length == 3 && parts[2] == "remove":
                    return ApiResponse.Result(w.Territories.Remove(actor, Id(1)));
                case "territories" when post && parts.Length == 3 && parts[2] == "add":
                    return ApiResponse.Result(w.Territories.AddCompanies(actor, Id(1), Input.Read<Territories.CompaniesInput>(body).Companies ?? new List<long>()));
                case "territories" when post && parts.Length == 3 && parts[2] == "takeout":
                    return ApiResponse.Result(w.Territories.RemoveCompanies(actor, Id(1), Input.Read<Territories.CompaniesInput>(body).Companies ?? new List<long>()));
                case "territories" when post && parts.Length == 3 && parts[2] == "match":
                    return ApiResponse.Of(new { matches = w.Territories.MatchList(Id(1), Input.Read<Territories.TextInput>(body).Text) });

                // ---- CRM users and teams (Sales Admins) ----
                case "crmusers" or "teams" when !actor.IsSalesAdmin:
                    return ApiResponse.Forbidden("The CRM users are kept by Sales Admins.");
                case "crmusers" when get && parts.Length == 1:
                    return ListCrmUsers(Q("q"), Flag("all"));
                case "crmusers" when get && parts.Length == 2 && parts[1] == "person":
                    return CrmPerson(Q("login"));
                case "crmusers" when post && parts.Length == 2 && parts[1] == "person":
                    return ApiResponse.Result(w.Users.Save(actor, Q("login"), Input.Read<CrmUsers.ProfileInput>(body)));
                case "crmusers" when post && parts.Length == 3 && parts[1] == "person" && parts[2] == "remove":
                    return ApiResponse.Result(w.Users.RemoveUserAndTerritories(actor, Q("login")));
                case "crmusers" when post && parts.Length == 3 && parts[1] == "person" && parts[2] == "team":
                    return ApiResponse.Result(w.Users.CreateTeamFor(actor, Q("login")));
                case "teams" when get && parts.Length == 1:
                    return ListTeams();
                case "teams" when get && parts.Length == 2:
                    return ApiResponse.Of(new { members = w.Users.Members(Id(1)).Select(m => new { m.Login, m.Name, role = CrmRoles.List.Label(m.Role) }) });
                case "teams" when post && parts.Length == 2:
                    return ApiResponse.Result(w.Users.SaveTeam(actor, Id(1), Input.Read<CrmUsers.TeamInput>(body)));
            }
            return ApiResponse.NotFound();
        }
        catch (BadInputException ex)
        {
            return ApiResponse.BadRequest(ex.Message);
        }
        catch (MyCrmRefusedException ex)
        {
            return ApiResponse.Fail(409, ResultCodes.Conflict, ex.Message);
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or TimeoutException or InvalidOperationException)
        {
            return ApiResponse.Fail(503, "UNAVAILABLE", ex.Message.StartsWith("The ", StringComparison.Ordinal) ? ex.Message : "A database could not be read: " + ex.Message);
        }
    }

    public static object Can(Actor actor) => new
    {
        admin = actor.IsAdmin, salesAdmin = actor.IsSalesAdmin, territories = actor.CanManageTerritories, editContacts = actor.CanEditContacts,
        seesAll = actor.SeesAllCompanies, uploadEmployees = actor.IsAdmin || actor.CrmRole == CrmRoles.Admin,
        // Signing in as someone else (recorded in the audit log): administrators only.
        actAs = actor.IsAdmin,
    };

    private static object Choices(ChoiceList list, params string[] inactive) =>
        list.All.Select(c => new { code = c.Code, label = c.Label, active = !inactive.Contains(c.Code) });

    private ApiResponse Lists(Actor actor) => ApiResponse.Of(new
    {
        roles = Choices(CrmRoles.List),
        oneTeamRoles = CrmRoles.OneTeam,
        managerRoles = CrmRoles.Managers,
        companyGroups = Choices(CompanyGroups.List, CompanyGroups.Inactive),
        compAccountTypes = Choices(CompAccountTypes.List),
        compOwnerships = Choices(CompOwnerships.List),
        advanceCustomerTypes = Choices(AdvanceCustomerTypes.List),
        addressTypes = Choices(AddressTypes.List),
        phoneTypes = Choices(PhoneTypes.List),
        groupNames = w.Companies.GroupNames().Select(g => new { g.Id, g.GroupName }),
        teams = w.Users.Teams(includeInactive: false).Select(t => new { t.Id, t.Name }),
        salespeople = actor.CanManageTerritories ? w.Territories.Salespeople().Select(p => (object)new { login = p.Login, name = p.Name }).ToList() : new List<object>(),
        directory = findInDirectory is not null,
    });

    private string Name(string login) => login.Length == 0 ? "" : w.People.NameOf(login);

    private object Person(string login) => new { login, name = Name(login) };

    private static string Date(DateTime? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    /// <summary>MyCRM's GetCUserTeamName: the team of the person's first CRM user record.</summary>
    private string TeamOf(string login, Dictionary<string, string> cache)
    {
        if (login.Length == 0) return "";
        if (cache.TryGetValue(login, out var known)) return known;
        var first = w.Store.Find<CrmUser>(Query.Where("Login", login)).OrderBy(u => u.Id).FirstOrDefault();
        var team = first?.TeamId is { } id ? w.Store.Get<CrmTeam>(id)?.Name ?? "" : "";
        cache[login] = team;
        return team;
    }

    private ApiResponse ListCompanies(Actor actor, Companies.ListFilter filter)
    {
        var (total, rows) = w.Companies.List(actor, filter);
        return ApiResponse.Of(new
        {
            total, skip = filter.Skip, take = Companies.PageSize,
            rows = rows.Select(r => new { r.Id, r.Name, r.Brn, r.PostalCode, salesperson = Name(r.Owner), r.Territory, r.CustomerType, createdBy = Name(r.CreatedBy) }),
            can = new { create = actor.IsSalesAdmin, uploadEmployees = actor.IsAdmin || actor.CrmRole == CrmRoles.Admin },
        });
    }

    private ApiResponse CompanyDetail(Actor actor, long id)
    {
        var c = w.Companies.Get(id);
        if (c is null) return ApiResponse.NotFound();
        if (!w.Companies.CanOpen(actor, c))
            return ApiResponse.Forbidden("You don't have permissions to view this company: it is not in your or your team's territories.");
        var owners = w.Territories.Owners([c.Id]);
        var owner = owners.TryGetValue(c.Id, out var o) ? o : ((string Owner, Territory Territory)?)null;
        var addresses = w.Companies.Addresses(c.Id);
        var (parent, siblings, subsidiaries) = w.Companies.Relationships(c.Id);
        string CompanyName(long companyId) => w.Store.Get<Company>(companyId)?.Name.Trim() ?? "#" + companyId.ToString(CultureInfo.InvariantCulture);
        var teams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // The related companies' territories, shown beside their names.
        var related = (parent is null ? Array.Empty<long>() : new[] { parent.CompanyId }).Concat(siblings.Select(r => r.RelatedCompanyId)).Concat(subsidiaries.Select(r => r.RelatedCompanyId));
        var relatedOwners = w.Territories.Owners(related.Distinct().ToList());
        string TerritoryOf(long companyId) => relatedOwners.TryGetValue(companyId, out var t) ? Territories.Title(t.Territory) : "";
        return ApiResponse.Of(new
        {
            company = new
            {
                c.Id, name = c.Name.Trim(), c.Brn, c.Website, c.IsActive, c.IncCertNo, c.NoOfEmployees, c.PaidUpCapital, c.CompanyGroup,
                companyGroupLabel = CompanyGroups.List.Label(c.CompanyGroup), c.CompanyGroupNameId,
                groupName = c.CompanyGroupNameId is { } g ? w.Store.Get<CompanyGroupName>(g)?.GroupName ?? "" : "", c.CrmCompanyId,
                createdDate = Date(c.CreatedDate), updatedDate = Date(c.UpdatedDate), c.CompAccountType, compAccountTypeLabel = CompAccountTypes.List.Label(c.CompAccountType),
                c.CompOwnership, compOwnershipLabel = CompOwnerships.List.Label(c.CompOwnership), c.IsCaretake, createdBy = Person(c.CreatedBy), updatedBy = Person(c.UpdatedBy),
                c.Remarks, c.IsChannelPartner, c.Phone, c.Fax, c.IsCreatedByMsd, c.MifPotential, c.Revenue, industry = CompanyIndustries.List.Label(c.CompanyIndustry),
                customerType = CustomerTypes.List.Label(c.CustomerType), c.CustomerNo, c.CreditTerms, c.SalesRemarks, c.AdvanceCustomerType,
                advanceCustomerTypeLabel = AdvanceCustomerTypes.List.Label(c.AdvanceCustomerType),
            },
            owner = owner is { } ow ? new { login = ow.Owner, name = Name(ow.Owner), territoryId = ow.Territory.Id, territory = Territories.Title(ow.Territory), team = TeamOf(ow.Owner, teams) } : null,
            caretakers = w.Projects.Caretakers(c.Id, owner is { } own ? own.Owner : "").Select(x => new
            {
                login = x.Member.User, name = Name(x.Member.User), role = ProjectRoles.List.Label(x.Member.Role), projectId = x.Project.Id, project = x.Project.ProjectName,
                endDate = Date(x.Project.EndDate),
            }),
            billingAddresses = addresses.Where(a => a.AddressType == AddressTypes.Billing).Select(Companies.AddressText),
            addresses = addresses.Select(a => new
            {
                a.Id, type = a.AddressType, typeLabel = AddressTypes.List.Label(a.AddressType), a.IsDefault, text = Companies.AddressText(a), a.UnitNumber, a.Block,
                a.Street, a.BuildingName, a.PostalCode, a.City, a.StateProvince, a.Country,
            }),
            contacts = w.Companies.Contacts(c.Id).Select(v => new
            {
                v.Contact.Id, name = Companies.ContactName(v.Contact), v.IsDefault, v.Contact.Department, v.Contact.Designation, v.Contact.IsEqList, v.Contact.IsLinkedIn,
                v.Contact.Salutation, v.Contact.FirstName, v.Contact.MiddleName, v.Contact.LastName, createdAt = v.Contact.CreatedAt, createdBy = Name(v.Contact.CreatedBy),
                updatedAt = v.Contact.UpdatedAt, updatedBy = Name(v.Contact.UpdatedBy),
                phones = v.Phones.Select(p => new { typeLabel = PhoneTypes.List.Label(p.PhoneType), number = p.Number }),
                emails = v.Emails.Select(e => new { address = e.Address, consent = e.ConsentToMarketingInfo }),
            }),
            relationships = new
            {
                parent = parent is null ? null : new { relationshipId = parent.Id, id = parent.CompanyId, name = CompanyName(parent.CompanyId), territory = TerritoryOf(parent.CompanyId) },
                siblings = siblings.Select(r => new { relationshipId = r.Id, id = r.RelatedCompanyId, name = CompanyName(r.RelatedCompanyId), territory = TerritoryOf(r.RelatedCompanyId) }),
                subsidiaries = subsidiaries.Select(r => new { relationshipId = r.Id, id = r.RelatedCompanyId, name = CompanyName(r.RelatedCompanyId), territory = TerritoryOf(r.RelatedCompanyId) }),
            },
            movements = actor.SeesMovements
                ? w.Territories.Movements(c.Id).Select(m => (object)new
                {
                    from = Name(m.FromUser), fromTeam = TeamOf(m.FromUser, teams), to = Name(m.ToUser), toTeam = TeamOf(m.ToUser, teams), by = Name(m.UpdatedBy),
                    at = m.UpdatedAt,
                }).ToList()
                : null,
            caretakes = w.Companies.Caretakes(c.Id).Select(r => r.CaretakeAccountRemarks),
            can = new { edit = actor.IsSalesAdmin, editContacts = actor.CanEditContacts, editRemarks = true, territories = actor.CanManageTerritories },
        });
    }

    private ApiResponse ContactEditor(Actor actor, long companyId, long contactId)
    {
        if (w.Companies.Get(companyId) is not { } company) return ApiResponse.NotFound();
        if (!w.Companies.CanOpen(actor, company)) return ApiResponse.Forbidden("You don't have permissions to view this company.");
        if (contactId == 0) return ApiResponse.Of(new { contact = (object?)null, emails = Array.Empty<object>(), phones = Array.Empty<object>() });
        if (w.Companies.ContactForEdit(companyId, contactId) is not { } edit) return ApiResponse.NotFound();
        var c = edit.Contact;
        return ApiResponse.Of(new
        {
            contact = new { c.Id, c.Salutation, c.FirstName, c.MiddleName, c.LastName, c.Department, c.Designation, c.IsLinkedIn, c.IsEqList, isDefault = edit.IsDefault },
            emails = edit.Emails.Select(e => new { e.Id, address = e.Address, consentToMarketingInfo = e.ConsentToMarketingInfo }),
            phones = edit.Phones.Select(p => new { p.Id, number = p.Number, phoneType = p.PhoneType }),
        });
    }

    private ApiResponse ListTerritories(bool includeInactive)
    {
        var counts = w.Territories.CompanyCounts();
        return ApiResponse.Of(new
        {
            territories = w.Territories.All(includeInactive).Select(t => new
            {
                t.Id, title = Territories.Title(t), t.Name, t.Number, owner = Person(t.Owner), t.IsActive, companies = counts.TryGetValue(t.Id, out var n) ? n : 0,
                t.CreatedOn, createdBy = Name(t.CreatedBy), t.RemovedOn, t.RemovedBy,
            }),
        });
    }

    private ApiResponse TerritoryDetail(long id, string q, int skip)
    {
        if (w.Territories.Get(id) is not { } t) return ApiResponse.NotFound();
        var (total, rows) = w.Territories.Companies(t.Id, q, skip);
        var postal = w.Companies.PostalCodes(rows.Select(c => c.Id));
        var since = w.Territories.Since(t.Id);
        return ApiResponse.Of(new
        {
            territory = new { t.Id, title = Territories.Title(t), t.Name, t.Number, owner = Person(t.Owner), t.IsActive, createdBy = Name(t.CreatedBy), t.CreatedOn, t.RemovedOn, t.RemovedBy },
            companies = new
            {
                total, skip, take = Territories.PageSize,
                rows = rows.Select(c => new
                {
                    c.Id, name = c.Name.Trim(), c.Brn, postalCode = postal.TryGetValue(c.Id, out var code) ? code : "", since = since.TryGetValue(c.Id, out var putIn) ? putIn : null,
                }),
            },
        });
    }

    /// <summary>A territory's History tab: the company moves to or from its salesperson, newest first.</summary>
    private ApiResponse TerritoryHistory(Actor actor, long id)
    {
        if (w.Territories.Get(id) is not { } t) return ApiResponse.NotFound();
        if (!actor.SeesMovements) return ApiResponse.Forbidden("You don't have permissions to see territory movements.");
        var moves = w.Territories.History(t);
        var names = new Dictionary<long, string>();
        foreach (var chunk in moves.Select(m => m.CompanyId).Distinct().Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => (object)x.v))
            foreach (var c in w.Store.Find<Company>(Query.All().In("Id", chunk)))
                names[c.Id] = c.Name.Trim();
        return ApiResponse.Of(new
        {
            owner = Person(t.Owner), max = Territories.HistoryMax,
            rows = moves.Select(m => new
            {
                at = m.UpdatedAt, companyId = m.CompanyId, company = names.TryGetValue(m.CompanyId, out var name) ? name : "",
                from = Name(m.FromUser), to = Name(m.ToUser), by = Name(m.UpdatedBy),
            }),
        });
    }

    private ApiResponse ListCrmUsers(string q, bool all)
    {
        var teams = w.Users.Teams().ToDictionary(t => t.Id, t => t.Name);
        return ApiResponse.Of(new
        {
            people = w.Users.List(q, all).Select(p => new
            {
                p.Login, p.Name, p.Email, p.Role, roleLabel = CrmRoles.List.Label(p.Role), teams = p.Teams.Select(t => teams.TryGetValue(t, out var n) ? n : "#" + t.ToString(CultureInfo.InvariantCulture)),
                p.SalesmanCode, p.TerritoryCode, p.IsCrmUser, p.Territories,
            }),
        });
    }

    private ApiResponse CrmPerson(string login)
    {
        login = (login ?? "").Trim();
        if (login.Length == 0) return ApiResponse.NotFound();
        var p = w.Users.PersonOf(login);
        var name = w.People.NameOf(login);
        return ApiResponse.Of(new
        {
            person = new { p.Login, p.Name, p.Email, p.Role, roleLabel = CrmRoles.List.Label(p.Role), p.Teams, p.Designation, p.PhoneNumber, p.SalesmanCode, p.TerritoryCode, p.IsCrmUser },
            teams = w.Users.Teams(includeInactive: false).Select(t => new { t.Id, t.Name, namedAfter = Strings.SameText(t.Name, name) }),
            territories = w.Territories.All().Where(t => Strings.SameText(t.Owner, login)).Select(t => new { t.Id, title = Territories.Title(t) }),
        });
    }

    private ApiResponse ListTeams()
    {
        var members = w.Store.Find<CrmUser>(Query.Where("IsActive", true)).Where(u => u.TeamId is not null).GroupBy(u => u.TeamId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(u => u.Login).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        return ApiResponse.Of(new
        {
            teams = w.Users.Teams().Select(t => new { t.Id, t.Name, t.ShortName, t.IsActive, members = members.TryGetValue(t.Id, out var n) ? n : 0 }),
        });
    }
}
