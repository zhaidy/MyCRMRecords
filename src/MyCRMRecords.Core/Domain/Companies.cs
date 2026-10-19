using System.Globalization;
using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>
/// The companies with their addresses, contacts (phones and emails), relationships and group names, as MyCRM's Company
/// screens keep them. Who may open a company is MyCRM's rule (<see cref="Access"/>); the company form is for Sales
/// Admins; the number of employees and the sales remarks for anyone who opens the company; addresses, contacts and
/// relationships for every role but Marketing. Every save records who changed the record and when, and the Pre…
/// fields are filled as MyCRM fills them. Nothing is deleted: what is taken away is inactive.
/// </summary>
public sealed class Companies(IRecordStore store, Access access, Territories territories, Func<DateTime> clock)
{
    public const int PageSize = 50;
    public const string Required = "Required Field!";

    public sealed record ListFilter(string? Q = null, string? CustomerType = null, long? GroupNameId = null, string? PostalCode = null, int Skip = 0);

    /// <summary>A company of the list: its first address's postal code, salesperson and creator.</summary>
    public sealed record Row(long Id, string Name, string Brn, string PostalCode, string Owner, string CreatedBy);

    public sealed class CompanyInput
    {
        public bool IsActive { get; set; } = true;
        /// <summary>"Is HOME USER": the BRN may be empty and is not checked (not kept, as in MyCRM).</summary>
        public bool IsHomeUser { get; set; }
        public string? Name { get; set; }
        public string? Brn { get; set; }
        public string? Website { get; set; }
        public bool IsChannelPartner { get; set; }
        public string? CompanyGroup { get; set; }
        public string? AdvanceCustomerType { get; set; }
        public long? CompanyGroupNameId { get; set; }
        public string? CompAccountType { get; set; }
        public string? CompOwnership { get; set; }
        public string? Remarks { get; set; }
    }

    public sealed class EmployeesInput { public int? NoOfEmployees { get; set; } }

    public sealed class RemarksInput { public string? SalesRemarks { get; set; } }

    public sealed class GroupNameInput { public string? GroupName { get; set; } }

    public sealed class RelationshipInput
    {
        public long CompanyId { get; set; }
        /// <summary>"parent", "sibling" or "subsidiary".</summary>
        public string? Kind { get; set; }
    }

    public sealed class AddressInput
    {
        public bool IsDefault { get; set; }
        public string? AddressType { get; set; }
        public string? PostalCode { get; set; }
        public string? UnitNumber { get; set; }
        public string? Block { get; set; }
        public string? Street { get; set; }
        public string? BuildingName { get; set; }
        public string? City { get; set; }
        public string? StateProvince { get; set; }
        public string? Country { get; set; }
    }

    public sealed class AddressTypeInput { public string? AddressType { get; set; } }

    public sealed class EmailInput
    {
        public long Id { get; set; }
        public string? Address { get; set; }
        public bool ConsentToMarketingInfo { get; set; } = true;
    }

    public sealed class PhoneInput
    {
        public long Id { get; set; }
        public string? Number { get; set; }
        public string? PhoneType { get; set; }
    }

    public sealed class ContactInput
    {
        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? Department { get; set; }
        public string? Designation { get; set; }
        public bool IsLinkedIn { get; set; }
        public bool IsEqList { get; set; }
        public bool IsDefault { get; set; }
        public List<EmailInput>? Emails { get; set; }
        public List<PhoneInput>? Phones { get; set; }
    }

    /// <summary>A contact of a company: the person, whether they are the primary contact, their phones and emails.</summary>
    public sealed record ContactView(Contact Contact, bool IsDefault, IReadOnlyList<Phone> Phones, IReadOnlyList<ContactEmail> Emails);

    /// <summary>MyCRM's AddressDetail: block, street, unit, building and postal code, each trimmed, the empty ones left out.</summary>
    public static string AddressText(Address a)
    {
        var first = string.Join(" ", new[] { a.Block, a.Street }.Select(p => (p ?? "").Trim()).Where(p => p.Length > 0));
        return string.Join(", ", new[] { first, (a.UnitNumber ?? "").Trim(), (a.BuildingName ?? "").Trim(), (a.PostalCode ?? "").Trim() }.Where(p => p.Length > 0));
    }

    /// <summary>MyCRM's ContactDisplaytName: salutation, first, middle and last names, the empty ones left out.</summary>
    public static string ContactName(Contact c) =>
        string.Join(" ", new[] { c.Salutation, c.FirstName, c.MiddleName, c.LastName }.Select(p => (p ?? "").Trim()).Where(p => p.Length > 0));

    private static OperationResult NotFound() => OperationResult.Failure(ResultCodes.NotFound, "That company is not there.");

    private static OperationResult NoAccess() =>
        OperationResult.Failure(ResultCodes.Forbidden, "You don't have permissions to view this company (it is not in your or your team's territories).");

    public Company? Get(long id) => store.Get<Company>(id);

    public bool CanOpen(Actor actor, Company company) => access.CanOpen(actor, company);

    /// <summary>The company, when the person may open it (else why not).</summary>
    private OperationResult? Open(Actor actor, long id, out Company company)
    {
        company = store.Get<Company>(id)!;
        if (company is null) return NotFound();
        return access.CanOpen(actor, company) ? null : NoAccess();
    }

    /// <summary>
    /// The active companies the person may open, by name, 50 at a time: searched by name or BRN, by customer type, group
    /// name and postal code (any of its addresses), as MyCRM's company search.
    /// </summary>
    public (int Total, IReadOnlyList<Row> Rows) List(Actor actor, ListFilter filter)
    {
        var query = Query.Where("IsActive", true).Search(filter.Q, "Name", "Brn");
        if (!string.IsNullOrWhiteSpace(filter.CustomerType)) query = query.And("CompanyGroup", filter.CustomerType!.Trim());
        if (filter.GroupNameId is { } group && group != 0) query = query.And("CompanyGroupNameId", group);
        HashSet<long>? ids = access.CompanyIds(actor);
        var postal = (filter.PostalCode ?? "").Trim();
        if (postal.Length > 0)
        {
            var addresses = store.Find<Address>(Query.All().Contains("PostalCode", postal)).Select(a => (object)a.Id).ToList();
            var withPostal = new HashSet<long>();
            foreach (var chunk in addresses.Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => x.v))
                withPostal.UnionWith(store.Find<CompanyAddressLink>(Query.All().In("AddressId", chunk)).Select(l => l.CompanyId));
            ids = ids is null ? withPostal : new HashSet<long>(ids.Where(withPostal.Contains));
        }
        int total;
        List<Company> page;
        var skip = Math.Max(0, filter.Skip);
        if (ids is null)
        {
            total = store.Count<Company>(query.Unpaged());
            page = store.Find<Company>(query.Sort("Name").Page(skip, PageSize));
        }
        else
        {
            var found = new List<Company>();
            foreach (var chunk in ids.Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => (object)x.v))
            {
                var part = Query.Where("IsActive", true).Search(filter.Q, "Name", "Brn").In("Id", chunk);
                if (!string.IsNullOrWhiteSpace(filter.CustomerType)) part = part.And("CompanyGroup", filter.CustomerType!.Trim());
                if (filter.GroupNameId is { } g && g != 0) part = part.And("CompanyGroupNameId", g);
                found.AddRange(store.Find<Company>(part));
            }
            total = found.Count;
            page = found.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Skip(skip).Take(PageSize).ToList();
        }
        return (total, Rows(page));
    }

    private IReadOnlyList<Row> Rows(IReadOnlyList<Company> page)
    {
        var owners = territories.Owners(page.Select(c => c.Id));
        var postal = PostalCodes(page.Select(c => c.Id));
        return page.Select(c => new Row(c.Id, c.Name.Trim(), c.Brn, postal.TryGetValue(c.Id, out var p) ? p : "",
            owners.TryGetValue(c.Id, out var o) ? o.Owner : "", c.CreatedBy)).ToList();
    }

    /// <summary>MyCRM's GetPostalCodeByCompanyId: the postal code of each company's first address, active or not.</summary>
    public IReadOnlyDictionary<long, string> PostalCodes(IEnumerable<long> companyIds)
    {
        var ids = companyIds.Distinct().Select(i => (object)i).ToList();
        var result = new Dictionary<long, string>();
        if (ids.Count == 0) return result;
        var links = store.Find<CompanyAddressLink>(Query.All().In("CompanyId", ids)).OrderBy(l => l.Id).ToList();
        if (links.Count == 0) return result;
        var addresses = store.Find<Address>(Query.All().In("Id", links.Select(l => (object)l.AddressId).Distinct())).ToDictionary(a => a.Id);
        foreach (var link in links)
            if (!result.ContainsKey(link.CompanyId) && addresses.TryGetValue(link.AddressId, out var a)) result[link.CompanyId] = a.PostalCode;
        return result;
    }

    // ---- The company form (MyCRM's CompanyDetail, Sales Admins) ----

    public OperationResult Save(Actor actor, long id, CompanyInput input)
    {
        if (!actor.IsSalesAdmin) return OperationResult.Failure(ResultCodes.Forbidden, "Companies are added and changed by Sales Admins.");
        var company = id == 0 ? new Company() : store.Get<Company>(id);
        if (company is null) return NotFound();
        var errors = new List<FieldError>();
        string Field(string? value, int length, string field, bool required = false)
        {
            var text = (value ?? "").Trim();
            if (required && text.Length == 0) errors.Add(new FieldError(field, Required));
            else if (text.Length > length) errors.Add(new FieldError(field, $"At most {length.ToString(CultureInfo.InvariantCulture)} characters."));
            return text;
        }
        string Code(string? value, ChoiceList list, string field, string current)
        {
            var code = (value ?? "").Trim();
            if (code.Length > 0 && !list.IsKnown(code)) errors.Add(new FieldError(field, "Choose one of the list."));
            return code;
        }
        var name = Field(input.Name, 250, "name", required: true);
        var brn = Field(input.Brn, 50, "brn", required: !input.IsHomeUser);
        var website = Field(input.Website, 100, "website");
        var remarks = Field(input.Remarks, 10000, "remarks");
        var group = Code(input.CompanyGroup, CompanyGroups.List, "companyGroup", company.CompanyGroup);
        if (group.Length > 0 && group != company.CompanyGroup && CompanyGroups.Inactive.Contains(group)) errors.Add(new FieldError("companyGroup", "That customer type is no longer used."));
        var advance = Code(input.AdvanceCustomerType, AdvanceCustomerTypes.List, "advanceCustomerType", company.AdvanceCustomerType);
        var accountType = Code(input.CompAccountType, CompAccountTypes.List, "compAccountType", company.CompAccountType);
        var ownership = Code(input.CompOwnership, CompOwnerships.List, "compOwnership", company.CompOwnership);
        var groupName = input.CompanyGroupNameId is { } g && g != 0 ? g : (long?)null;
        if (groupName is { } gn && gn != company.CompanyGroupNameId && store.Get<CompanyGroupName>(gn) is not { IsActive: true })
            errors.Add(new FieldError("companyGroupNameId", "Choose a group name of the list."));
        // A BRN another company has is refused, for a new company and an edited one (MyCRM checked new ones only); a home
        // user's is not checked.
        if (!input.IsHomeUser && brn.Length > 0)
        {
            var other = store.Find<Company>(Query.All().Contains("Brn", brn)).OrderBy(c => c.Id)
                .FirstOrDefault(c => c.Id != company.Id && Strings.SameText(c.Brn, brn));
            if (other is not null)
                errors.Add(new FieldError("brn", $"Company is exists: {other.Name.Trim()} (#{other.Id.ToString(CultureInfo.InvariantCulture)}) has this BRN."));
        }
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        var now = clock();
        if (id == 0)
        {
            company.CreatedBy = actor.Login;
            company.CreatedDate = now.Date;
        }
        else
        {
            // The values just before this edit, all 19 (MyCRM's GetPreData), also of fields the form does not show.
            company.PreName = company.Name;
            company.PreBrn = company.Brn;
            company.PreWebsite = company.Website;
            company.PreIsActive = company.IsActive;
            company.PreIncCertNo = company.IncCertNo;
            company.PreNoOfEmployees = company.NoOfEmployees;
            company.PrePaidUpCapital = company.PaidUpCapital;
            company.PreCompanyGroup = company.CompanyGroup;
            company.PreCompanyGroupNameId = company.CompanyGroupNameId;
            company.PreCompAccountType = company.CompAccountType;
            company.PreIsCaretake = company.IsCaretake;
            company.PreCompOwnership = company.CompOwnership;
            company.PreRemarks = company.Remarks;
            company.PreIsChannelPartner = company.IsChannelPartner;
            company.PrePhone = company.Phone;
            company.PreFax = company.Fax;
            company.PreMifPotential = company.MifPotential;
            company.PreRevenue = company.Revenue;
            company.PreCompanyIndustry = company.CompanyIndustry;
            company.UpdatedBy = actor.Login;
            company.UpdatedDate = now.Date;
        }
        company.IsActive = input.IsActive;
        company.Name = name;
        company.Brn = brn;
        company.Website = website;
        company.IsChannelPartner = input.IsChannelPartner;
        company.CompanyGroup = group;
        company.AdvanceCustomerType = advance;
        company.CompanyGroupNameId = groupName;
        company.CompAccountType = accountType;
        company.CompOwnership = ownership;
        company.Remarks = remarks;
        if (id == 0) store.Insert(company);
        else store.Update(company);
        return OperationResult.Success($"Company '{company.Name}' was successfully {(id == 0 ? "created" : "updated")}.", company.Id);
    }

    /// <summary>The number of employees (anyone who opens the company), recording who changed it and when (MyCRM did not).</summary>
    public OperationResult SaveEmployees(Actor actor, long id, EmployeesInput input)
    {
        if (Open(actor, id, out var company) is { } refused) return refused;
        var number = input.NoOfEmployees ?? 0;
        if (number < 0) return OperationResult.Invalid([new FieldError("noOfEmployees", "Enter a number of 0 or more.")]);
        company.NoOfEmployees = number;
        Touched(company, actor);
        store.Update(company);
        return OperationResult.Success("No of Employees saved.", company.Id);
    }

    /// <summary>The sales remarks (anyone who opens the company), recording who changed them and when (MyCRM did not).</summary>
    public OperationResult SaveSalesRemarks(Actor actor, long id, RemarksInput input)
    {
        if (Open(actor, id, out var company) is { } refused) return refused;
        var text = (input.SalesRemarks ?? "").Trim();
        if (text.Length > 50000) return OperationResult.Invalid([new FieldError("salesRemarks", "At most 50000 characters.")]);
        company.SalesRemarks = text;
        Touched(company, actor);
        store.Update(company);
        return OperationResult.Success("Remarks were saved.", company.Id);
    }

    private void Touched(Company company, Actor actor)
    {
        company.UpdatedBy = actor.Login;
        company.UpdatedDate = clock().Date;
    }

    /// <summary>
    /// MyCRM's UploadNoOfEmployees (its Admin page): for each row, the first active company with the BRN (by name) gets
    /// the number; rows with no such company are counted as skipped.
    /// </summary>
    public OperationResult UploadEmployees(Actor actor, IReadOnlyList<(string Brn, string Number)> rows)
    {
        if (!actor.IsAdmin && actor.CrmRole != CrmRoles.Admin) return OperationResult.Failure(ResultCodes.Forbidden, "Numbers of employees are uploaded by MyCRM Admins.");
        var updated = 0;
        var skipped = 0;
        store.Batch(s =>
        {
            foreach (var (brnText, numberText) in rows)
            {
                var brn = (brnText ?? "").Trim();
                if (brn.Length == 0 || !int.TryParse((numberText ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 0)
                {
                    skipped++;
                    continue;
                }
                var company = s.Find<Company>(Query.Where("Brn", brn).And("IsActive", true)).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (company is null)
                {
                    skipped++;
                    continue;
                }
                company.NoOfEmployees = number;
                Touched(company, actor);
                s.Update(company);
                updated++;
            }
        });
        return OperationResult.Success($"{updated.ToString(CultureInfo.InvariantCulture)} records uploaded successfully" +
            (skipped > 0 ? $"; {skipped.ToString(CultureInfo.InvariantCulture)} skipped (no active company with that BRN, or no number)." : "."));
    }

    // ---- Group names ----

    public IReadOnlyList<CompanyGroupName> GroupNames() =>
        store.Find<CompanyGroupName>(Query.Where("IsActive", true)).OrderBy(g => g.GroupName, StringComparer.OrdinalIgnoreCase).ToList();

    public OperationResult SaveGroupName(Actor actor, long id, GroupNameInput input)
    {
        if (!actor.IsSalesAdmin) return OperationResult.Failure(ResultCodes.Forbidden, "Group names are kept by Sales Admins.");
        var group = id == 0 ? new CompanyGroupName() : store.Get<CompanyGroupName>(id) is { IsActive: true } g ? g : null;
        if (group is null) return OperationResult.Failure(ResultCodes.NotFound, "That group name is not there.");
        var name = (input.GroupName ?? "").Trim();
        if (name.Length == 0) return OperationResult.Invalid([new FieldError("groupName", Required)]);
        if (name.Length > 255) return OperationResult.Invalid([new FieldError("groupName", "At most 255 characters.")]);
        if (GroupNames().Any(o => o.Id != group.Id && Strings.SameText(o.GroupName, name)))
            return OperationResult.Invalid([new FieldError("groupName", "There is a group name like this.")]);
        group.GroupName = name;
        if (id == 0) store.Insert(group);
        else store.Update(group);
        return OperationResult.Success($"Company Group Name '{name}' was successfully {(id == 0 ? "created" : "updated")}.", group.Id);
    }

    /// <summary>Takes a group name away (inactive here, kept in MyCRM); refused while a company has it (MyCRM's Protect rule).</summary>
    public OperationResult RemoveGroupName(Actor actor, long id)
    {
        if (!actor.IsSalesAdmin) return OperationResult.Failure(ResultCodes.Forbidden, "Group names are kept by Sales Admins.");
        if (store.Get<CompanyGroupName>(id) is not { IsActive: true } group) return OperationResult.Failure(ResultCodes.NotFound, "That group name is not there.");
        var used = store.Count<Company>(Query.Where("CompanyGroupNameId", id));
        if (used > 0)
            return OperationResult.Failure(ResultCodes.Conflict, $"{used.ToString(CultureInfo.InvariantCulture)} compan{(used == 1 ? "y has" : "ies have")} the group name '{group.GroupName}': choose another for them first.");
        group.IsActive = false;
        group.RemovedOn = clock();
        group.RemovedBy = Strings.Shorten(actor.ChangedBy, 600);
        store.Update(group);
        return OperationResult.Success($"Company Group Name '{group.GroupName}' was successfully deleted.", group.Id);
    }

    // ---- Relationships (parent, siblings, subsidiaries) ----

    /// <summary>The company's parent (MyCRM's RelatedCompanyID = the company), its siblings and its subsidiaries, active only.</summary>
    public (CompanyRelationship? Parent, IReadOnlyList<CompanyRelationship> Siblings, IReadOnlyList<CompanyRelationship> Subsidiaries) Relationships(long id)
    {
        var parent = store.Find<CompanyRelationship>(Query.Where("RelatedCompanyId", id).And("IsActive", true)).OrderBy(r => r.Id).FirstOrDefault();
        var subsidiaries = store.Find<CompanyRelationship>(Query.Where("CompanyId", id).And("IsActive", true)).OrderBy(r => r.Id).ToList();
        var siblings = parent is null ? new List<CompanyRelationship>()
            : store.Find<CompanyRelationship>(Query.Where("CompanyId", parent.CompanyId).And("IsActive", true)).Where(r => r.RelatedCompanyId != id).OrderBy(r => r.Id).ToList();
        return (parent, siblings, subsidiaries);
    }

    private long? ParentOf(long id) => store.Find<CompanyRelationship>(Query.Where("RelatedCompanyId", id).And("IsActive", true)).OrderBy(r => r.Id).FirstOrDefault()?.CompanyId;

    /// <summary>Whether <paramref name="ancestor"/> is <paramref name="id"/> or above it.</summary>
    private bool IsAtOrAbove(long ancestor, long id)
    {
        var seen = new HashSet<long>();
        for (long? at = id; at is { } current && seen.Add(current); at = ParentOf(current))
            if (current == ancestor) return true;
        return false;
    }

    /// <summary>
    /// Adds a parent, a sibling (a subsidiary of the parent) or a subsidiary, as MyCRM: a company has one parent at most.
    /// A company cannot be related to itself, nor be put below a company that is below it (MyCRM let both happen).
    /// </summary>
    public OperationResult AddRelationship(Actor actor, long id, RelationshipInput input)
    {
        if (!actor.CanEditContacts) return OperationResult.Failure(ResultCodes.Forbidden, "Your role does not change companies' relationships.");
        if (Open(actor, id, out var company) is { } refused) return refused;
        if (store.Get<Company>(input.CompanyId) is not { } chosen) return OperationResult.Failure(ResultCodes.Validation, "Choose a company.");
        if (chosen.Id == company.Id) return OperationResult.Failure(ResultCodes.Validation, "A company cannot be related to itself.");
        var kind = (input.Kind ?? "").Trim().ToLowerInvariant();
        string OneParent(Company c) => $"{c.Name.Trim()} has a parent company already (a company can only have 1 parent).";
        long parentId, childId;
        switch (kind)
        {
            case "parent":
                if (ParentOf(company.Id) is not null) return OperationResult.Failure(ResultCodes.Validation, OneParent(company));
                if (IsAtOrAbove(company.Id, chosen.Id)) return OperationResult.Failure(ResultCodes.Validation, $"{chosen.Name.Trim()} is below {company.Name.Trim()}: it cannot be its parent.");
                parentId = chosen.Id;
                childId = company.Id;
                break;
            case "sibling":
                if (ParentOf(company.Id) is not { } parent) return OperationResult.Failure(ResultCodes.Validation, "Add a parent company first.");
                if (ParentOf(chosen.Id) is not null) return OperationResult.Failure(ResultCodes.Validation, OneParent(chosen));
                if (IsAtOrAbove(chosen.Id, parent)) return OperationResult.Failure(ResultCodes.Validation, $"{chosen.Name.Trim()} is above {company.Name.Trim()}: it cannot be its sibling.");
                parentId = parent;
                childId = chosen.Id;
                break;
            case "subsidiary":
                if (ParentOf(chosen.Id) is not null) return OperationResult.Failure(ResultCodes.Validation, OneParent(chosen));
                if (IsAtOrAbove(chosen.Id, company.Id)) return OperationResult.Failure(ResultCodes.Validation, $"{chosen.Name.Trim()} is above {company.Name.Trim()}: it cannot be its subsidiary.");
                parentId = company.Id;
                childId = chosen.Id;
                break;
            default:
                return OperationResult.Failure(ResultCodes.Validation, "Choose parent, sibling or subsidiary.");
        }
        var relationship = store.Insert(new CompanyRelationship { CompanyId = parentId, RelatedCompanyId = childId, RelationshipType = RelationshipTypes.Parent, IsActive = true });
        return OperationResult.Success($"{chosen.Name.Trim()} added as {(kind == "parent" ? "the parent company" : kind == "sibling" ? "a sibling company" : "a subsidiary")}.", relationship.Id);
    }

    /// <summary>Takes a relationship away (inactive; MyCRM deleted it).</summary>
    public OperationResult RemoveRelationship(Actor actor, long id, long relationshipId)
    {
        if (!actor.CanEditContacts) return OperationResult.Failure(ResultCodes.Forbidden, "Your role does not change companies' relationships.");
        if (Open(actor, id, out _) is { } refused) return refused;
        // Only a relationship the company's page shows: its parent's, its subsidiaries' and its siblings'.
        if (store.Get<CompanyRelationship>(relationshipId) is not { IsActive: true } relationship
            || (relationship.CompanyId != id && relationship.RelatedCompanyId != id && relationship.CompanyId != ParentOf(id)))
            return OperationResult.Failure(ResultCodes.NotFound, "That relationship is not there.");
        relationship.IsActive = false;
        relationship.RemovedOn = clock();
        relationship.RemovedBy = Strings.Shorten(actor.ChangedBy, 600);
        store.Update(relationship);
        return OperationResult.Success("The relationship was removed.", relationship.Id);
    }

    // ---- Addresses ----

    /// <summary>The company's active addresses, the default ones first.</summary>
    public IReadOnlyList<Address> Addresses(long companyId)
    {
        var links = store.Find<CompanyAddressLink>(Query.Where("CompanyId", companyId).And("IsActive", true));
        if (links.Count == 0) return Array.Empty<Address>();
        return store.Find<Address>(Query.Where("IsActive", true).In("Id", links.Select(l => (object)l.AddressId).Distinct()))
            .OrderByDescending(a => a.IsDefault).ThenBy(a => a.Id).ToList();
    }

    private OperationResult? EditAddress(Actor actor, long companyId, long addressId, out Company company, out Address? address)
    {
        address = null;
        if (!actor.CanEditContacts)
        {
            company = null!;
            return OperationResult.Failure(ResultCodes.Forbidden, "Your role does not change companies' addresses.");
        }
        if (Open(actor, companyId, out company) is { } refused) return refused;
        if (addressId == 0) return null;
        address = Addresses(companyId).FirstOrDefault(a => a.Id == addressId);
        return address is null ? OperationResult.Failure(ResultCodes.NotFound, "That address is not there.") : null;
    }

    /// <summary>
    /// Adds or changes a company's address (MyCRM's AddressDetail_PopUp). Only one default address: ticking "Is Default"
    /// is refused while another address is the default (MyCRM also refused re-saving the default address itself).
    /// </summary>
    public OperationResult SaveAddress(Actor actor, long companyId, long addressId, AddressInput input)
    {
        if (EditAddress(actor, companyId, addressId, out var company, out var existing) is { } refused) return refused;
        var errors = new List<FieldError>();
        string Field(string? value, int length, string field, bool required = false)
        {
            var text = (value ?? "").Trim();
            if (required && text.Length == 0) errors.Add(new FieldError(field, Required));
            else if (text.Length > length) errors.Add(new FieldError(field, $"At most {length.ToString(CultureInfo.InvariantCulture)} characters."));
            return text;
        }
        var type = (input.AddressType ?? "").Trim();
        if (!AddressTypes.List.IsKnown(type)) errors.Add(new FieldError("addressType", Required));
        var postal = Field(input.PostalCode, 50, "postalCode");
        var unit = Field(input.UnitNumber, 2000, "unitNumber");
        var block = Field(input.Block, 2000, "block", required: true);
        var street = Field(input.Street, 2000, "street", required: true);
        var building = Field(input.BuildingName, 2000, "buildingName");
        var city = Field(input.City, 255, "city");
        var state = Field(input.StateProvince, 255, "stateProvince");
        var country = existing is null && input.Country is null ? "Singapore" : Field(input.Country, 255, "country");
        if (input.IsDefault && Addresses(company.Id).Any(a => a.IsDefault && a.Id != addressId))
            errors.Add(new FieldError("isDefault", "There is already a default address for this company"));
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        var now = clock();
        var address = existing ?? new Address { IsActive = true, CreatedAt = now, CreatedBy = actor.Login };
        if (existing is not null)
        {
            address.PreAddressType = address.AddressType;
            address.PreUnitNumber = address.UnitNumber;
            address.PreBlock = address.Block;
            address.PreStreet = address.Street;
            address.PreBuildingName = address.BuildingName;
            address.PrePostalCode = address.PostalCode;
            address.PreCountry = address.Country;
            address.PreCity = address.City;
            address.PreStateProvince = address.StateProvince;
            address.PreIsActive = address.IsActive;
            address.UpdatedAt = now;
            address.UpdatedBy = actor.Login;
        }
        address.IsDefault = input.IsDefault;
        address.AddressType = type;
        address.PostalCode = postal;
        address.UnitNumber = unit;
        address.Block = block;
        address.Street = street;
        address.BuildingName = building;
        address.City = city;
        address.StateProvince = state;
        address.Country = country;
        if (existing is null)
        {
            store.Insert(address);
            store.Insert(new CompanyAddressLink { CompanyId = company.Id, AddressId = address.Id, IsActive = true });
        }
        else store.Update(address);
        return OperationResult.Success(existing is null ? "Address added." : "Address saved.", address.Id);
    }

    /// <summary>MyCRM's CurrentAddressType: an address's type changed on its own (recording who and when, which MyCRM did not).</summary>
    public OperationResult SetAddressType(Actor actor, long companyId, long addressId, AddressTypeInput input)
    {
        if (EditAddress(actor, companyId, addressId, out _, out var address) is { } refused) return refused;
        var type = (input.AddressType ?? "").Trim();
        if (!AddressTypes.List.IsKnown(type)) return OperationResult.Invalid([new FieldError("addressType", "Choose an address type.")]);
        address!.AddressType = type;
        address.UpdatedAt = clock();
        address.UpdatedBy = actor.Login;
        store.Update(address);
        return OperationResult.Success($"Address type set to {AddressTypes.List.Label(type)}.", address.Id);
    }

    /// <summary>Deletes an address as MyCRM: inactive (with PreIsActive set, as MyCRM's SQL does); its link here is inactive too.</summary>
    public OperationResult RemoveAddress(Actor actor, long companyId, long addressId)
    {
        if (EditAddress(actor, companyId, addressId, out var company, out var address) is { } refused) return refused;
        var now = clock();
        store.Batch(s =>
        {
            address!.IsActive = false;
            address.PreIsActive = true;
            address.UpdatedAt = now;
            address.UpdatedBy = actor.Login;
            s.Update(address);
            foreach (var link in s.Find<CompanyAddressLink>(Query.Where("AddressId", address.Id).And("CompanyId", company.Id).And("IsActive", true)))
            {
                link.IsActive = false;
                link.RemovedOn = now;
                link.RemovedBy = Strings.Shorten(actor.ChangedBy, 600);
                s.Update(link);
            }
        });
        return OperationResult.Success("Address has been deleted.", address!.Id);
    }

    // ---- Contacts with their phones and emails ----

    /// <summary>
    /// The company's active contacts with a first or last name, the primary contact first, then by name; with their
    /// active phones (any company) and emails (any company, by address), as MyCRM's Contacts block shows them.
    /// </summary>
    public IReadOnlyList<ContactView> Contacts(long companyId)
    {
        var links = store.Find<CompanyContactLink>(Query.Where("CompanyId", companyId));
        if (links.Count == 0) return Array.Empty<ContactView>();
        var contacts = store.Find<Contact>(Query.Where("IsActive", true).In("Id", links.Select(l => (object)l.ContactId).Distinct()))
            .Where(c => c.FirstName.Trim().Length > 0 || c.LastName.Trim().Length > 0).ToList();
        if (contacts.Count == 0) return Array.Empty<ContactView>();
        var ids = contacts.Select(c => (object)(long?)c.Id).ToList();
        var phones = store.Find<Phone>(Query.Where("IsActive", true).In("ContactId", ids)).Where(p => p.Number.Trim().Length > 0).ToList();
        var emails = store.Find<ContactEmail>(Query.Where("IsActive", true).In("ContactId", ids)).ToList();
        var defaults = new HashSet<long>(links.Where(l => l.IsDefault).Select(l => l.ContactId));
        return contacts
            .Select(c => new ContactView(c, defaults.Contains(c.Id), phones.Where(p => p.ContactId == c.Id).OrderBy(p => p.Id).ToList(),
                emails.Where(e => e.ContactId == c.Id).OrderBy(e => e.Address, StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderByDescending(v => v.IsDefault).ThenBy(v => ContactName(v.Contact), StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A contact as its editor shows it: its emails for this company and its phones (MyCRM's ContactDetail_Popup).</summary>
    public (Contact Contact, bool IsDefault, IReadOnlyList<ContactEmail> Emails, IReadOnlyList<Phone> Phones)? ContactForEdit(long companyId, long contactId)
    {
        var link = store.Find<CompanyContactLink>(Query.Where("CompanyId", companyId).And("ContactId", contactId)).OrderBy(l => l.Id).FirstOrDefault();
        if (link is null || store.Get<Contact>(contactId) is not { IsActive: true } contact) return null;
        var emails = store.Find<ContactEmail>(Query.Where("ContactId", contactId).And("IsActive", true)).Where(e => e.CompanyId == companyId).OrderBy(e => e.Id).ToList();
        var phones = store.Find<Phone>(Query.Where("ContactId", contactId).And("IsActive", true)).OrderBy(p => p.Id).ToList();
        return (contact, link.IsDefault, emails, phones);
    }

    /// <summary>
    /// Adds or changes a contact with its emails and phones, all in one save (MyCRM's ContactDetail_Popup). Emails and
    /// phones taken off the list are inactive. Ticking "Is Default" makes the contact the company's only primary contact.
    /// </summary>
    public OperationResult SaveContact(Actor actor, long companyId, long contactId, ContactInput input)
    {
        if (!actor.CanEditContacts) return OperationResult.Failure(ResultCodes.Forbidden, "Your role does not change companies' contacts.");
        if (Open(actor, companyId, out var company) is { } refused) return refused;
        var editing = contactId == 0 ? null : ContactForEdit(companyId, contactId);
        if (contactId != 0 && editing is null) return OperationResult.Failure(ResultCodes.NotFound, "That contact is not there.");
        var errors = new List<FieldError>();
        string Field(string? value, int length, string field, bool required = false)
        {
            var text = (value ?? "").Trim();
            if (required && text.Length == 0) errors.Add(new FieldError(field, Required));
            else if (text.Length > length) errors.Add(new FieldError(field, $"At most {length.ToString(CultureInfo.InvariantCulture)} characters."));
            return text;
        }
        var salutation = Field(input.Salutation, 250, "salutation");
        var first = Field(input.FirstName, 250, "firstName");
        var middle = Field(input.MiddleName, 250, "middleName");
        var last = Field(input.LastName, 250, "lastName", required: true);
        var department = Field(input.Department, 50, "department");
        var designation = Field(input.Designation, 250, "designation");
        var emails = (input.Emails ?? new List<EmailInput>()).ToList();
        var phones = (input.Phones ?? new List<PhoneInput>()).ToList();
        for (var i = 0; i < emails.Count; i++)
        {
            var address = (emails[i].Address ?? "").Trim();
            if (address.Length == 0 || address.Length > 200 || !Strings.IsEmail(address)) errors.Add(new FieldError("emails." + i.ToString(CultureInfo.InvariantCulture), "Invalid Email"));
        }
        for (var i = 0; i < phones.Count; i++)
        {
            var number = (phones[i].Number ?? "").Trim();
            if (number.Length == 0 || number.Length > 20) errors.Add(new FieldError("phones." + i.ToString(CultureInfo.InvariantCulture), "Invalid phone number"));
            var type = (phones[i].PhoneType ?? "").Trim();
            if (type.Length > 0 && !PhoneTypes.List.IsKnown(type)) errors.Add(new FieldError("phones." + i.ToString(CultureInfo.InvariantCulture), "Choose a phone type of the list."));
        }
        var knownEmails = editing?.Emails.ToDictionary(e => e.Id) ?? new Dictionary<long, ContactEmail>();
        var knownPhones = editing?.Phones.ToDictionary(p => p.Id) ?? new Dictionary<long, Phone>();
        if (emails.Any(e => e.Id != 0 && !knownEmails.ContainsKey(e.Id)) || phones.Any(p => p.Id != 0 && !knownPhones.ContainsKey(p.Id)))
            errors.Add(new FieldError("", "The contact was changed by someone else: open it again."));
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        var now = clock();
        var contact = editing?.Contact ?? new Contact { IsActive = true, CreatedAt = now, CreatedBy = actor.Login };
        if (editing is not null)
        {
            contact.PreFirstName = contact.FirstName;
            contact.PreLastName = contact.LastName;
            contact.PreIsActive = contact.IsActive;
            contact.PreDepartment = contact.Department;
            contact.PreMiddleName = contact.MiddleName;
            contact.PreDesignation = contact.Designation;
            contact.PreSalutation = contact.Salutation;
            contact.UpdatedAt = now;
            contact.UpdatedBy = actor.Login;
        }
        contact.Salutation = salutation;
        contact.FirstName = first;
        contact.MiddleName = middle;
        contact.LastName = last;
        contact.Department = department;
        contact.Designation = designation;
        contact.IsLinkedIn = input.IsLinkedIn;
        // "Is Eq. List" only for a contact with an email (MyCRM enabled it only then).
        contact.IsEqList = input.IsEqList && emails.Count > 0;
        // A new contact takes MyCRM's number (the sync adds it there first), outside the batch, as MyCEV adds its feedbacks.
        if (editing is null) store.Insert(contact);
        var saved = contact;
        store.Batch(s =>
        {
            if (editing is not null) s.Update(contact);

            if (input.IsDefault)
                foreach (var other in s.Find<CompanyContactLink>(Query.Where("CompanyId", company.Id).And("IsDefault", true)).Where(l => l.ContactId != contact.Id))
                {
                    other.IsDefault = false;
                    s.Update(other);
                }
            var link = s.Find<CompanyContactLink>(Query.Where("CompanyId", company.Id).And("ContactId", contact.Id)).OrderBy(l => l.Id).FirstOrDefault();
            if (link is null) s.Insert(new CompanyContactLink { CompanyId = company.Id, ContactId = contact.Id, IsDefault = input.IsDefault });
            else if (link.IsDefault != input.IsDefault)
            {
                link.IsDefault = input.IsDefault;
                s.Update(link);
            }

            var keptEmails = new HashSet<long>(emails.Where(e => e.Id != 0).Select(e => e.Id));
            foreach (var gone in knownEmails.Values.Where(e => !keptEmails.Contains(e.Id)))
            {
                gone.IsActive = false;
                gone.UpdatedAt = now;
                gone.UpdatedBy = actor.Login;
                s.Update(gone);
            }
            foreach (var e in emails)
            {
                var address = (e.Address ?? "").Trim();
                if (e.Id == 0)
                {
                    s.Insert(new ContactEmail
                    {
                        Address = address, CompanyId = company.Id, ContactId = contact.Id, IsActive = true, ConsentToMarketingInfo = e.ConsentToMarketingInfo,
                        CreatedAt = now, CreatedBy = actor.Login,
                    });
                    continue;
                }
                var known = knownEmails[e.Id];
                if (known.Address == address && known.ConsentToMarketingInfo == e.ConsentToMarketingInfo) continue;
                known.Address = address;
                known.ConsentToMarketingInfo = e.ConsentToMarketingInfo;
                known.CompanyId = company.Id;
                known.UpdatedAt = now;
                known.UpdatedBy = actor.Login;
                s.Update(known);
            }

            var keptPhones = new HashSet<long>(phones.Where(p => p.Id != 0).Select(p => p.Id));
            foreach (var gone in knownPhones.Values.Where(p => !keptPhones.Contains(p.Id)))
            {
                gone.IsActive = false;
                gone.UpdatedAt = now;
                gone.UpdatedBy = actor.Login;
                s.Update(gone);
            }
            foreach (var p in phones)
            {
                var number = (p.Number ?? "").Trim();
                var type = (p.PhoneType ?? "").Trim();
                if (p.Id == 0)
                {
                    s.Insert(new Phone { Number = number, PhoneType = type, CompanyId = company.Id, ContactId = contact.Id, IsActive = true, CreatedAt = now, CreatedBy = actor.Login });
                    continue;
                }
                var known = knownPhones[p.Id];
                if (known.Number == number && known.PhoneType == type) continue;
                known.Number = number;
                known.PhoneType = type;
                known.CompanyId = company.Id;
                known.UpdatedAt = now;
                known.UpdatedBy = actor.Login;
                s.Update(known);
            }
        });
        return OperationResult.Success(editing is null ? "Contact was successfully created." : "Contact was successfully updated.", saved.Id);
    }

    /// <summary>MyCRM's "Delete Contact": inactive (with PreIsActive set, as MyCRM); its link, phones and emails are left as they are.</summary>
    public OperationResult RemoveContact(Actor actor, long companyId, long contactId)
    {
        if (!actor.CanEditContacts) return OperationResult.Failure(ResultCodes.Forbidden, "Your role does not change companies' contacts.");
        if (Open(actor, companyId, out _) is { } refused) return refused;
        if (ContactForEdit(companyId, contactId) is not { } editing) return OperationResult.Failure(ResultCodes.NotFound, "That contact is not there.");
        var contact = editing.Contact;
        contact.IsActive = false;
        contact.PreIsActive = true;
        contact.UpdatedAt = clock();
        contact.UpdatedBy = actor.Login;
        store.Update(contact);
        return OperationResult.Success("Contact was deleted.", contact.Id);
    }

    /// <summary>The company's caretake remarks (MyCRM's CompanyCaretake), as MyCRM lists them.</summary>
    public IReadOnlyList<CompanyCaretake> Caretakes(long companyId) =>
        store.Find<CompanyCaretake>(Query.Where("CompanyId", companyId)).OrderBy(c => c.Id).Take(50).ToList();
}
