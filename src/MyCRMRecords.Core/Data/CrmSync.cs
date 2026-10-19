using System.Data.Common;
using System.Globalization;
using MyCRMRecords.Domain;
using static MyCRMRecords.Data.OldValues;
using OldRow = System.Collections.Generic.Dictionary<string, object?>;

namespace MyCRMRecords.Data;

/// <summary>
/// What the sync with MyCRM copies, both ways (OldAppSync): the Users, Territory and Company part of MyCRM, in the
/// OutSystems database. People are written as their OutSystems user (found by login, name or email), never added to
/// OutSystems; MyCRM's static entities are matched by label.
/// <list type="bullet">
/// <item>Nothing is deleted on either side. What is taken away here is inactive there (MyCRM's own IsActive) or, for a
/// company's address link and a group name (no such column in MyCRM), left there as it is. A row MyCRM deleted is marked
/// here.</item>
/// <item>Only MyCRM's permanent territories are synced (<see cref="CrmTables"/> reads the active permanent territories
/// and their active companies): the temporary ones of MyCRM's Projects and CSST parts stay MyCRM's, and the millions of
/// inactive rows are never read. A territory or a company link that leaves that set in MyCRM is marked inactive here.</item>
/// <item>A territory's name is kept here only. Territory movements are synced for the last 60 days.</item>
/// </list>
/// </summary>
public static class CrmSync
{
    public const string GroupNames = "CompanyGroupName";
    public const string Teams = "Team";
    public const string Users = "CUser";
    public const string Companies = "Company";
    public const string Caretakes = "CompanyCaretake";
    public const string Relationships = "CompanyRelationship";
    public const string Addresses = "Address";
    public const string AddressLinks = "CompanyAddressLink";
    public const string Contacts = "Contacts";
    public const string ContactLinks = "CompanyContactLink";
    public const string Phones = "Phone";
    public const string Emails = "Email";
    public const string Territories = "Territory";
    public const string CompanyTerritories = "CompanyTerritory";
    public const string Movements = "TerritoryMovement";

    /// <summary>How many days of territory movements are synced (older ones are left as they are on both sides).</summary>
    public const int MovementDays = 60;

    /// <summary>A login of an OutSystems user's Id, or the record's own when the user is not known.</summary>
    private static string LoginOf(SyncContext c, OldRow row, string column, string? existing) =>
        c.User(row.TryGetValue(column, out var v) ? v : null) is { } u ? Strings.Shorten(u.Login, 256) : existing ?? "";

    private static object? Day(DateTime? date) => date?.Date;

    private static string Text(OldRow row, string column, int length) => Strings.Shorten(OldValues.Text(row, column), length);

    /// <summary>MyCRM's Id of a static entity's record for a code (by its labels); null for none or unknown.</summary>
    private static long? Static(SyncContext c, ChoiceList list, string code) =>
        list.All.FirstOrDefault(x => x.Code == code) is { } choice ? c.LabelId(list.OldEntity, choice.Labels.ToArray()) : null;

    /// <summary>The code of a static entity's record in a row ("" for none or one not known here).</summary>
    private static string Code(SyncContext c, ChoiceList list, OldRow row, string column) =>
        list.FromLabel(c.Label(list.OldEntity, row.TryGetValue(column, out var v) ? v : null));

    /// <summary>A reference to another synced record: its Id here, the existing one when the row's is not here yet.</summary>
    private static long? Ref(SyncContext c, string entity, OldRow row, string column) => c.LocalId(entity, Long(row, column));

    private static Dictionary<string, object?> Values() => new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ISyncEntity> Entities(Func<DateTime> clock)
    {
        // A record found inactive in MyCRM: when and by whom, kept from here when it was taken away here.
        (DateTime? On, string By) Removal(bool active, DateTime? on, string? by, bool wasActive) =>
            active ? (null, "") : !wasActive ? (on, by ?? "") : (clock(), "MyCRM");

        return
        [
            new SyncEntity<CompanyGroupName>
            {
                Name = GroupNames, Label = "company group name", OldTable = GroupNames, OldCreatedOnColumn = null, OldUpdatedOnColumn = null,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                MarkRemovedHere = r => { r.IsActive = false; r.RemovedOn = clock(); r.RemovedBy = "MyCRM"; },
                Describe = r => r.GroupName,
                // Removed here: MyCRM has no mark for it, so it keeps it (as a choice nobody uses).
                ToOld = (r, _, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["GROUPNAME"] = r.GroupName },
                FromOld = (row, _, x) => new CompanyGroupName
                {
                    GroupName = Text(row, "GROUPNAME", 255), IsActive = x?.IsActive ?? true, RemovedOn = x?.RemovedOn, RemovedBy = x?.RemovedBy ?? "",
                    OutSystemsId = Long(row, "ID"),
                },
            },
            new SyncEntity<CrmTeam>
            {
                Name = Teams, Label = "team", OldTable = Teams, OldCreatedOnColumn = null, OldUpdatedOnColumn = null,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                MarkRemovedHere = r => r.IsActive = false,
                Describe = r => r.Name,
                ToOld = (r, _, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["NAME"] = r.Name, ["SHORTNAME"] = r.ShortName, ["ISACTIVE"] = r.IsActive,
                },
                FromOld = (row, _, _) => new CrmTeam
                {
                    Name = Text(row, "NAME", 255), ShortName = Text(row, "SHORTNAME", 50), IsActive = Bool(row, "ISACTIVE"), OutSystemsId = Long(row, "ID"),
                },
            },
            new SyncEntity<CrmUser>
            {
                Name = Users, Label = "CRM user", OldTable = Users, OldCreatedOnColumn = "CREATEDAT", OldUpdatedOnColumn = "UPDATEDAT",
                UpdatedOn = r => r.UpdatedAt ?? r.CreatedAt,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                Parents = [(Teams, r => r.TeamId)],
                // A profile is written for a person MyCRM knows (an OutSystems user), never with no person.
                CanWrite = (r, c) => c.OldUser(r.Login) is not null,
                // Deleted in MyCRM (its user page deletes a person's profiles at every save): inactive here, without its team.
                MarkRemovedHere = r =>
                {
                    if (r.TeamId is not null) r.FormerTeamId = r.TeamId;
                    r.TeamId = null;
                    r.IsActive = false;
                    r.RemovedOn ??= clock();
                    if (r.RemovedBy.Length == 0) r.RemovedBy = "MyCRM";
                },
                Describe = r => $"{r.Login} ({CrmRoles.List.Label(r.Role)})",
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["USERID"] = c.OldUser(r.Login), ["ROLEID"] = Static(c, CrmRoles.List, r.Role), ["TEAMID"] = c.OldId(Teams, r.TeamId), ["ISACTIVE"] = r.IsActive,
                    ["CREATEDBY"] = c.OldUser(r.CreatedBy), ["CREATEDAT"] = r.CreatedAt, ["UPDATEDBY"] = c.OldUser(r.UpdatedBy), ["UPDATEDAT"] = r.UpdatedAt,
                    ["DESIGNATION"] = r.Designation, ["PHONENUMBER"] = r.PhoneNumber, ["SALESMANCODE"] = r.SalesmanCode, ["CID"] = r.CId,
                    ["PROFILEID"] = r.ProfileId is { } p && p != 0 ? p : null, ["TERRITORYCODE"] = r.TerritoryCode,
                },
                FromOld = (row, c, x) =>
                {
                    var login = LoginOf(c, row, "USERID", x?.Login);
                    if (login.Length == 0) return null;
                    var active = Bool(row, "ISACTIVE");
                    var removal = Removal(active, x?.RemovedOn, x?.RemovedBy, x?.IsActive ?? true);
                    var profile = Long(row, "PROFILEID");
                    return new CrmUser
                    {
                        Login = login, Role = Code(c, CrmRoles.List, row, "ROLEID"), TeamId = Ref(c, Teams, row, "TEAMID"), IsActive = active,
                        CreatedBy = LoginOf(c, row, "CREATEDBY", x?.CreatedBy), CreatedAt = Date(row, "CREATEDAT"), UpdatedBy = LoginOf(c, row, "UPDATEDBY", x?.UpdatedBy),
                        UpdatedAt = Date(row, "UPDATEDAT"), Designation = Text(row, "DESIGNATION", 50), PhoneNumber = Text(row, "PHONENUMBER", 20),
                        SalesmanCode = Text(row, "SALESMANCODE", 50), CId = Int(row, "CID"), ProfileId = profile == 0 ? null : profile,
                        TerritoryCode = Text(row, "TERRITORYCODE", 50), FormerTeamId = x?.FormerTeamId, RemovedOn = removal.On, RemovedBy = removal.By,
                        OutSystemsId = Long(row, "ID"),
                    };
                },
            },
            new SyncEntity<Company>
            {
                // The company's number is the same in both while the sync is on (other parts of MyCRM and Advance refer to it).
                Name = Companies, Label = "company", OldTable = Companies, TakesOldNumbers = true,
                OldCreatedOnColumn = "CREATEDDATE", OldUpdatedOnColumn = "UPDATEDDATE", UpdatedOn = r => r.UpdatedDate ?? r.CreatedDate,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                Parents = [(GroupNames, r => r.CompanyGroupNameId), (GroupNames, r => r.PreCompanyGroupNameId)],
                MarkRemovedHere = r => r.IsActive = false,
                Describe = r => $"#{r.Id.ToString(CultureInfo.InvariantCulture)} ({r.Name})",
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["COMPANY"] = r.Name, ["BRN"] = r.Brn, ["WEBSITE"] = r.Website, ["ISACTIVE"] = r.IsActive, ["INCCERTNO"] = r.IncCertNo,
                    ["NOOFEMPLOYEES"] = r.NoOfEmployees, ["PAIDUPCAPITAL"] = r.PaidUpCapital, ["COMPANYGROUPID"] = Static(c, CompanyGroups.List, r.CompanyGroup),
                    ["COMPANYGROUPNAMEID"] = c.OldId(GroupNames, r.CompanyGroupNameId), ["CRMCOMPANYID"] = r.CrmCompanyId, ["CREATEDDATE"] = Day(r.CreatedDate),
                    ["UPDATEDDATE"] = Day(r.UpdatedDate), ["COMPACCOUNTTYPEID"] = Static(c, CompAccountTypes.List, r.CompAccountType),
                    ["COMPOWNERSHIPID"] = Static(c, CompOwnerships.List, r.CompOwnership), ["ISCARETAKE"] = r.IsCaretake, ["UPDATEDBY"] = c.OldUser(r.UpdatedBy),
                    ["CREATEDBY"] = c.OldUser(r.CreatedBy), ["REMARKS"] = r.Remarks, ["ISCHANNELPARTNER"] = r.IsChannelPartner, ["PHONE"] = r.Phone, ["FAX"] = r.Fax,
                    ["ISCREATEDBYMSD"] = r.IsCreatedByMsd, ["MIFPOTENTIAL"] = r.MifPotential, ["REVENUE"] = r.Revenue,
                    ["COMPANYINDUSTRYID"] = Static(c, CompanyIndustries.List, r.CompanyIndustry), ["PRECOMPANYNAME"] = r.PreName, ["PREBRN"] = r.PreBrn,
                    ["PREWEBSITE"] = r.PreWebsite, ["PREISACTIVE"] = r.PreIsActive, ["PREINCCERTNO"] = r.PreIncCertNo, ["PRENOOFEMPLOYEES"] = r.PreNoOfEmployees,
                    ["PREPAIDUPCAPITAL"] = r.PrePaidUpCapital, ["PRECOMPANYGROUPID"] = Static(c, CompanyGroups.List, r.PreCompanyGroup),
                    ["PRECOMPANYGROUPNAMEID"] = c.OldId(GroupNames, r.PreCompanyGroupNameId), ["PRECOMPACCOUNTTYPEID"] = Static(c, CompAccountTypes.List, r.PreCompAccountType),
                    ["PRECOMPOWNERSHIPID"] = Static(c, CompOwnerships.List, r.PreCompOwnership), ["PREISCARETAKE"] = r.PreIsCaretake, ["PREREMARKS"] = r.PreRemarks,
                    ["PREISCHANNELPARTNER"] = r.PreIsChannelPartner, ["PREPHONE"] = r.PrePhone, ["PREFAX"] = r.PreFax, ["PREMIFPOTENTIAL"] = r.PreMifPotential,
                    ["PREREVENUE"] = r.PreRevenue, ["PRECOMPANYINDUSTRYID"] = Static(c, CompanyIndustries.List, r.PreCompanyIndustry),
                    ["CUSTOMERTYPEID"] = Static(c, CustomerTypes.List, r.CustomerType), ["ISCHANNELPARTNERFORVSS"] = r.IsChannelPartnerForVss,
                    ["ISCHANNELPARTNERFORCS"] = r.IsChannelPartnerForCs, ["CUSTOMERNO"] = r.CustomerNo, ["CREDITTERMS"] = r.CreditTerms,
                    ["SALESREMARKS"] = r.SalesRemarks, ["ADVANCECUSTOMERTYPEID"] = Static(c, AdvanceCustomerTypes.List, r.AdvanceCustomerType),
                },
                FromOld = (row, c, x) => new Company
                {
                    Name = Text(row, "COMPANY", 250), Brn = Text(row, "BRN", 50), Website = Text(row, "WEBSITE", 100), IsActive = Bool(row, "ISACTIVE"),
                    IncCertNo = Text(row, "INCCERTNO", 50), NoOfEmployees = Int(row, "NOOFEMPLOYEES"), PaidUpCapital = Int(row, "PAIDUPCAPITAL"),
                    CompanyGroup = Code(c, CompanyGroups.List, row, "COMPANYGROUPID"), CompanyGroupNameId = Ref(c, GroupNames, row, "COMPANYGROUPNAMEID"),
                    CrmCompanyId = Long(row, "CRMCOMPANYID"), CreatedDate = Date(row, "CREATEDDATE")?.Date, UpdatedDate = Date(row, "UPDATEDDATE")?.Date,
                    CompAccountType = Code(c, CompAccountTypes.List, row, "COMPACCOUNTTYPEID"), CompOwnership = Code(c, CompOwnerships.List, row, "COMPOWNERSHIPID"),
                    IsCaretake = Bool(row, "ISCARETAKE"), UpdatedBy = LoginOf(c, row, "UPDATEDBY", x?.UpdatedBy), CreatedBy = LoginOf(c, row, "CREATEDBY", x?.CreatedBy),
                    Remarks = Text(row, "REMARKS", 10000), IsChannelPartner = Bool(row, "ISCHANNELPARTNER"), Phone = Text(row, "PHONE", 20), Fax = Text(row, "FAX", 20),
                    IsCreatedByMsd = Bool(row, "ISCREATEDBYMSD"), MifPotential = Int(row, "MIFPOTENTIAL"), Revenue = Decimal(row, "REVENUE"),
                    CompanyIndustry = Code(c, CompanyIndustries.List, row, "COMPANYINDUSTRYID"), PreName = Text(row, "PRECOMPANYNAME", 250),
                    PreBrn = Text(row, "PREBRN", 50), PreWebsite = Text(row, "PREWEBSITE", 100), PreIsActive = Bool(row, "PREISACTIVE"),
                    PreIncCertNo = Text(row, "PREINCCERTNO", 50), PreNoOfEmployees = Int(row, "PRENOOFEMPLOYEES"), PrePaidUpCapital = Int(row, "PREPAIDUPCAPITAL"),
                    PreCompanyGroup = Code(c, CompanyGroups.List, row, "PRECOMPANYGROUPID"), PreCompanyGroupNameId = Ref(c, GroupNames, row, "PRECOMPANYGROUPNAMEID"),
                    PreCompAccountType = Code(c, CompAccountTypes.List, row, "PRECOMPACCOUNTTYPEID"), PreCompOwnership = Code(c, CompOwnerships.List, row, "PRECOMPOWNERSHIPID"),
                    PreIsCaretake = Bool(row, "PREISCARETAKE"), PreRemarks = Text(row, "PREREMARKS", 10000), PreIsChannelPartner = Bool(row, "PREISCHANNELPARTNER"),
                    PrePhone = Text(row, "PREPHONE", 20), PreFax = Text(row, "PREFAX", 20), PreMifPotential = Int(row, "PREMIFPOTENTIAL"),
                    PreRevenue = Decimal(row, "PREREVENUE"), PreCompanyIndustry = Code(c, CompanyIndustries.List, row, "PRECOMPANYINDUSTRYID"),
                    CustomerType = Code(c, CustomerTypes.List, row, "CUSTOMERTYPEID"), IsChannelPartnerForVss = Bool(row, "ISCHANNELPARTNERFORVSS"),
                    IsChannelPartnerForCs = Bool(row, "ISCHANNELPARTNERFORCS"), CustomerNo = Text(row, "CUSTOMERNO", 50), CreditTerms = Text(row, "CREDITTERMS", 50),
                    SalesRemarks = Text(row, "SALESREMARKS", 50000), AdvanceCustomerType = Code(c, AdvanceCustomerTypes.List, row, "ADVANCECUSTOMERTYPEID"),
                    OutSystemsId = Long(row, "ID"),
                },
            },
            new SyncEntity<CompanyCaretake>
            {
                Name = Caretakes, Label = "caretake remark", OldTable = Caretakes, OldCreatedOnColumn = "DATECREATED", OldUpdatedOnColumn = "UPDATEDDATE",
                UpdatedOn = r => r.UpdatedDate ?? r.DateCreated,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                Parents = [(Companies, r => r.CompanyId)],
                OldParent = (Companies, "company", "COMPANYID"),
                CanWrite = (r, c) => c.OldId(Companies, r.CompanyId) is not null,
                Describe = r => $"of company #{r.CompanyId.ToString(CultureInfo.InvariantCulture)}",
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["COMPANYID"] = c.OldId(Companies, r.CompanyId), ["CARETAKEACCOUNTREMARKS"] = r.CaretakeAccountRemarks, ["UPDATEDDATE"] = Day(r.UpdatedDate),
                    ["CREATEDBY"] = c.OldUser(r.CreatedBy), ["UPDATEDBY"] = c.OldUser(r.UpdatedBy), ["DATECREATED"] = Day(r.DateCreated),
                },
                FromOld = (row, c, x) => Ref(c, Companies, row, "COMPANYID") is not { } company ? null : new CompanyCaretake
                {
                    CompanyId = company, CaretakeAccountRemarks = Text(row, "CARETAKEACCOUNTREMARKS", 10000), UpdatedDate = Date(row, "UPDATEDDATE")?.Date,
                    CreatedBy = LoginOf(c, row, "CREATEDBY", x?.CreatedBy), UpdatedBy = LoginOf(c, row, "UPDATEDBY", x?.UpdatedBy),
                    DateCreated = Date(row, "DATECREATED")?.Date, OutSystemsId = Long(row, "ID"),
                },
            },
            new SyncEntity<CompanyRelationship>
            {
                Name = Relationships, Label = "company relationship", OldTable = Relationships, OldCreatedOnColumn = null, OldUpdatedOnColumn = null,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                Parents = [(Companies, r => r.CompanyId), (Companies, r => r.RelatedCompanyId)],
                OldParent = (Companies, "company", "COMPANYID"),
                CanWrite = (r, c) => c.OldId(Companies, r.CompanyId) is not null && c.OldId(Companies, r.RelatedCompanyId) is not null,
                MarkRemovedHere = r => { r.IsActive = false; r.RemovedOn ??= clock(); if (r.RemovedBy.Length == 0) r.RemovedBy = "MyCRM"; },
                Describe = r => $"#{r.CompanyId.ToString(CultureInfo.InvariantCulture)} → #{r.RelatedCompanyId.ToString(CultureInfo.InvariantCulture)}",
                // Removed here: inactive there (MyCRM deleted it; its screens read only active ones).
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["COMPANYID"] = c.OldId(Companies, r.CompanyId), ["COMPANYRELATIONSHIPTYPEID"] = Static(c, RelationshipTypes.List, r.RelationshipType),
                    ["RELATEDCOMPANYID"] = c.OldId(Companies, r.RelatedCompanyId), ["ISACTIVE"] = r.IsActive,
                },
                FromOld = (row, c, x) =>
                {
                    if (Ref(c, Companies, row, "COMPANYID") is not { } parent || Ref(c, Companies, row, "RELATEDCOMPANYID") is not { } child) return null;
                    var active = Bool(row, "ISACTIVE");
                    var removal = Removal(active, x?.RemovedOn, x?.RemovedBy, x?.IsActive ?? true);
                    var type = Code(c, RelationshipTypes.List, row, "COMPANYRELATIONSHIPTYPEID");
                    return new CompanyRelationship
                    {
                        CompanyId = parent, RelatedCompanyId = child, RelationshipType = type.Length > 0 ? type : RelationshipTypes.Parent, IsActive = active,
                        RemovedOn = removal.On, RemovedBy = removal.By, OutSystemsId = Long(row, "ID"),
                    };
                },
            },
            new SyncEntity<Address>
            {
                Name = Addresses, Label = "address", OldTable = Addresses, TakesOldNumbers = true, OldCreatedOnColumn = "CREATEDAT", OldUpdatedOnColumn = "UPDATEDAT",
                UpdatedOn = r => r.UpdatedAt ?? r.CreatedAt,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                MarkRemovedHere = r => r.IsActive = false,
                Describe = r => $"#{r.Id.ToString(CultureInfo.InvariantCulture)} ({Domain.Companies.AddressText(r)})",
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ADDRESSTYPEID"] = Static(c, AddressTypes.List, r.AddressType), ["UNITNUMBER"] = r.UnitNumber, ["BLOCK"] = r.Block, ["STREET"] = r.Street,
                    ["BUILDINGNAME"] = r.BuildingName, ["POSTALCODE"] = r.PostalCode, ["ISACTIVE"] = r.IsActive, ["CRMADDRESSID"] = r.CrmAddressId,
                    ["ISDEFAULT"] = r.IsDefault, ["COUNTRY"] = r.Country, ["CITY"] = r.City, ["STATEPROVINCE"] = r.StateProvince,
                    ["PREADDRESSTYPEID"] = Static(c, AddressTypes.List, r.PreAddressType), ["PREUNITNUMBER"] = r.PreUnitNumber, ["PREBLOCK"] = r.PreBlock,
                    ["PRESTREET"] = r.PreStreet, ["PREBUILDINGNAME"] = r.PreBuildingName, ["PREPOSTALCODE"] = r.PrePostalCode, ["PRECOUNTRY"] = r.PreCountry,
                    ["PRECITY"] = r.PreCity, ["PRESTATEPROVINCE"] = r.PreStateProvince, ["PREISACTIVE"] = r.PreIsActive, ["CREATEDAT"] = r.CreatedAt,
                    ["CREATEDBY"] = c.OldUser(r.CreatedBy), ["UPDATEDAT"] = r.UpdatedAt, ["UPDATEDBY"] = c.OldUser(r.UpdatedBy),
                },
                FromOld = (row, c, x) => new Address
                {
                    AddressType = Code(c, AddressTypes.List, row, "ADDRESSTYPEID"), UnitNumber = Text(row, "UNITNUMBER", 2000), Block = Text(row, "BLOCK", 2000),
                    Street = Text(row, "STREET", 2000), BuildingName = Text(row, "BUILDINGNAME", 2000), PostalCode = Text(row, "POSTALCODE", 50),
                    IsActive = Bool(row, "ISACTIVE"), CrmAddressId = Long(row, "CRMADDRESSID"), IsDefault = Bool(row, "ISDEFAULT"), Country = Text(row, "COUNTRY", 255),
                    City = Text(row, "CITY", 255), StateProvince = Text(row, "STATEPROVINCE", 255), PreAddressType = Code(c, AddressTypes.List, row, "PREADDRESSTYPEID"),
                    PreUnitNumber = Text(row, "PREUNITNUMBER", 2000), PreBlock = Text(row, "PREBLOCK", 2000), PreStreet = Text(row, "PRESTREET", 2000),
                    PreBuildingName = Text(row, "PREBUILDINGNAME", 2000), PrePostalCode = Text(row, "PREPOSTALCODE", 50), PreCountry = Text(row, "PRECOUNTRY", 255),
                    PreCity = Text(row, "PRECITY", 255), PreStateProvince = Text(row, "PRESTATEPROVINCE", 255), PreIsActive = Bool(row, "PREISACTIVE"),
                    CreatedAt = Date(row, "CREATEDAT"), CreatedBy = LoginOf(c, row, "CREATEDBY", x?.CreatedBy), UpdatedAt = Date(row, "UPDATEDAT"),
                    UpdatedBy = LoginOf(c, row, "UPDATEDBY", x?.UpdatedBy), OutSystemsId = Long(row, "ID"),
                },
            },
            new SyncEntity<CompanyAddressLink>
            {
                Name = AddressLinks, Label = "company address", OldTable = AddressLinks, OldCreatedOnColumn = null, OldUpdatedOnColumn = null,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                Parents = [(Companies, r => r.CompanyId), (Addresses, r => r.AddressId)],
                OldParent = (Companies, "company", "COMPANYID"),
                CanWrite = (r, c) => c.OldId(Companies, r.CompanyId) is not null && c.OldId(Addresses, r.AddressId) is not null,
                // Deleted in MyCRM (with its address): inactive here. Taken away here: MyCRM has no mark, its address is inactive.
                MarkRemovedHere = r => { r.IsActive = false; r.RemovedOn ??= clock(); if (r.RemovedBy.Length == 0) r.RemovedBy = "MyCRM"; },
                Describe = r => $"address #{r.AddressId.ToString(CultureInfo.InvariantCulture)} of company #{r.CompanyId.ToString(CultureInfo.InvariantCulture)}",
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["COMPANYID"] = c.OldId(Companies, r.CompanyId), ["ADDRESSID"] = c.OldId(Addresses, r.AddressId), ["CRMADDRESSLINK"] = r.CrmAddressLink,
                },
                FromOld = (row, c, x) =>
                {
                    if (Ref(c, Companies, row, "COMPANYID") is not { } company || Ref(c, Addresses, row, "ADDRESSID") is not { } address) return null;
                    return new CompanyAddressLink
                    {
                        CompanyId = company, AddressId = address, CrmAddressLink = Long(row, "CRMADDRESSLINK"), IsActive = x?.IsActive ?? true,
                        RemovedOn = x?.RemovedOn, RemovedBy = x?.RemovedBy ?? "", OutSystemsId = Long(row, "ID"),
                    };
                },
            },
            new SyncEntity<Contact>
            {
                Name = Contacts, Label = "contact", OldTable = Contacts, TakesOldNumbers = true, OldCreatedOnColumn = "CREATEDAT", OldUpdatedOnColumn = "UPDATEDAT",
                UpdatedOn = r => r.UpdatedAt ?? r.CreatedAt,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                MarkRemovedHere = r => r.IsActive = false,
                Describe = r => $"#{r.Id.ToString(CultureInfo.InvariantCulture)} ({(r.FirstName + " " + r.LastName).Trim()})",
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["FIRSTNAME"] = r.FirstName, ["LASTNAME"] = r.LastName, ["ISACTIVE"] = r.IsActive, ["DEPARTMENT"] = r.Department, ["CID"] = r.CId,
                    ["MIDDLENAME"] = r.MiddleName, ["CRMCONTACTLINKID"] = r.CrmContactLinkId, ["DESIGNATION"] = r.Designation, ["SALUTATION"] = r.Salutation,
                    ["CREATEDAT"] = r.CreatedAt, ["CREATEDBY"] = c.OldUser(r.CreatedBy), ["UPDATEDAT"] = r.UpdatedAt, ["UPDATEDBY"] = c.OldUser(r.UpdatedBy),
                    ["PREFIRSTNAME"] = r.PreFirstName, ["PRELASTNAME"] = r.PreLastName, ["PREISACTIVE"] = r.PreIsActive, ["PREDEPARTMENT"] = r.PreDepartment,
                    ["PREMIDDLENAME"] = r.PreMiddleName, ["PREDESIGNATION"] = r.PreDesignation, ["PRESALUTATION"] = r.PreSalutation, ["ISLINKEDIN"] = r.IsLinkedIn,
                    ["ISEQLIST"] = r.IsEqList,
                },
                FromOld = (row, c, x) => new Contact
                {
                    FirstName = Text(row, "FIRSTNAME", 250), LastName = Text(row, "LASTNAME", 250), IsActive = Bool(row, "ISACTIVE"), Department = Text(row, "DEPARTMENT", 50),
                    CId = Long(row, "CID"), MiddleName = Text(row, "MIDDLENAME", 250), CrmContactLinkId = Long(row, "CRMCONTACTLINKID"),
                    Designation = Text(row, "DESIGNATION", 250), Salutation = Text(row, "SALUTATION", 250), CreatedAt = Date(row, "CREATEDAT"),
                    CreatedBy = LoginOf(c, row, "CREATEDBY", x?.CreatedBy), UpdatedAt = Date(row, "UPDATEDAT"), UpdatedBy = LoginOf(c, row, "UPDATEDBY", x?.UpdatedBy),
                    PreFirstName = Text(row, "PREFIRSTNAME", 250), PreLastName = Text(row, "PRELASTNAME", 250), PreIsActive = Bool(row, "PREISACTIVE"),
                    PreDepartment = Text(row, "PREDEPARTMENT", 50), PreMiddleName = Text(row, "PREMIDDLENAME", 250), PreDesignation = Text(row, "PREDESIGNATION", 250),
                    PreSalutation = Text(row, "PRESALUTATION", 250), IsLinkedIn = Bool(row, "ISLINKEDIN"), IsEqList = Bool(row, "ISEQLIST"), OutSystemsId = Long(row, "ID"),
                },
            },
            new SyncEntity<CompanyContactLink>
            {
                Name = ContactLinks, Label = "company contact", OldTable = ContactLinks, OldCreatedOnColumn = null, OldUpdatedOnColumn = null,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                Parents = [(Companies, r => r.CompanyId), (Contacts, r => r.ContactId)],
                OldParent = (Contacts, "contact", "CONTACTID"),
                CanWrite = (r, c) => c.OldId(Companies, r.CompanyId) is not null && c.OldId(Contacts, r.ContactId) is not null,
                Describe = r => $"contact #{r.ContactId.ToString(CultureInfo.InvariantCulture)} of company #{r.CompanyId.ToString(CultureInfo.InvariantCulture)}",
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["COMPANYID"] = c.OldId(Companies, r.CompanyId), ["CONTACTID"] = c.OldId(Contacts, r.ContactId), ["CID"] = r.CId, ["ISDEFAULT"] = r.IsDefault,
                },
                FromOld = (row, c, _) =>
                {
                    if (Ref(c, Companies, row, "COMPANYID") is not { } company || Ref(c, Contacts, row, "CONTACTID") is not { } contact) return null;
                    return new CompanyContactLink { CompanyId = company, ContactId = contact, CId = Long(row, "CID"), IsDefault = Bool(row, "ISDEFAULT"), OutSystemsId = Long(row, "ID") };
                },
            },
            new SyncEntity<Phone>
            {
                Name = Phones, Label = "phone number", OldTable = Phones, OldCreatedOnColumn = "CREATEDAT", OldUpdatedOnColumn = "UPDATEDAT",
                UpdatedOn = r => r.UpdatedAt ?? r.CreatedAt,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                Parents = [(Companies, r => r.CompanyId), (Contacts, r => r.ContactId)],
                OldParent = (Contacts, "contact", "CONTACTID"),
                CanWrite = (r, c) => (r.CompanyId is null || c.OldId(Companies, r.CompanyId) is not null) && (r.ContactId is null || c.OldId(Contacts, r.ContactId) is not null),
                MarkRemovedHere = r => r.IsActive = false,
                Describe = r => $"{r.Number} of contact #{(r.ContactId ?? 0).ToString(CultureInfo.InvariantCulture)}",
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["COMPANYID"] = c.OldId(Companies, r.CompanyId), ["CONTACTID"] = c.OldId(Contacts, r.ContactId), ["PHONETYPEID"] = Static(c, PhoneTypes.List, r.PhoneType),
                    ["PHONE"] = r.Number, ["ISACTIVE"] = r.IsActive, ["CRMPHONEID"] = r.CrmPhoneId, ["CRMPHONELINKID"] = r.CrmPhoneLinkId, ["CREATEDAT"] = r.CreatedAt,
                    ["CREATEDBY"] = c.OldUser(r.CreatedBy), ["UPDATEDAT"] = r.UpdatedAt, ["UPDATEDBY"] = c.OldUser(r.UpdatedBy), ["PREPHONE"] = r.PrePhone,
                    ["PREPHONETYPEID"] = Static(c, PhoneTypes.List, r.PrePhoneType), ["PREISACTIVE"] = r.PreIsActive,
                },
                FromOld = (row, c, x) =>
                {
                    // A phone of a company or contact that is not here (yet) waits; one with none is kept as it is.
                    var company = Long(row, "COMPANYID") == 0 ? null : Ref(c, Companies, row, "COMPANYID");
                    var contact = Long(row, "CONTACTID") == 0 ? null : Ref(c, Contacts, row, "CONTACTID");
                    if ((Long(row, "COMPANYID") != 0 && company is null) || (Long(row, "CONTACTID") != 0 && contact is null)) return null;
                    return new Phone
                    {
                        CompanyId = company, ContactId = contact, PhoneType = Code(c, PhoneTypes.List, row, "PHONETYPEID"), Number = Text(row, "PHONE", 20),
                        IsActive = Bool(row, "ISACTIVE"), CrmPhoneId = Long(row, "CRMPHONEID"), CrmPhoneLinkId = Long(row, "CRMPHONELINKID"), CreatedAt = Date(row, "CREATEDAT"),
                        CreatedBy = LoginOf(c, row, "CREATEDBY", x?.CreatedBy), UpdatedAt = Date(row, "UPDATEDAT"), UpdatedBy = LoginOf(c, row, "UPDATEDBY", x?.UpdatedBy),
                        PrePhone = Text(row, "PREPHONE", 20), PrePhoneType = Code(c, PhoneTypes.List, row, "PREPHONETYPEID"), PreIsActive = Bool(row, "PREISACTIVE"),
                        OutSystemsId = Long(row, "ID"),
                    };
                },
            },
            new SyncEntity<ContactEmail>
            {
                Name = Emails, Label = "email address", OldTable = Emails, OldCreatedOnColumn = "CREATEDAT", OldUpdatedOnColumn = "UPDATEDAT",
                UpdatedOn = r => r.UpdatedAt ?? r.CreatedAt,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                Parents = [(Companies, r => r.CompanyId), (Contacts, r => r.ContactId)],
                OldParent = (Contacts, "contact", "CONTACTID"),
                CanWrite = (r, c) => (r.CompanyId is null || c.OldId(Companies, r.CompanyId) is not null) && (r.ContactId is null || c.OldId(Contacts, r.ContactId) is not null),
                MarkRemovedHere = r => r.IsActive = false,
                Describe = r => $"{r.Address} of contact #{(r.ContactId ?? 0).ToString(CultureInfo.InvariantCulture)}",
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["EMAIL"] = r.Address, ["COMPANYID"] = c.OldId(Companies, r.CompanyId), ["CONTACTID"] = c.OldId(Contacts, r.ContactId), ["ISACTIVE"] = r.IsActive,
                    ["CID"] = r.Cid, ["CRMEMAILLINKID"] = r.CrmEmailLinkId, ["CREATEDAT"] = r.CreatedAt, ["CREATEDBY"] = c.OldUser(r.CreatedBy),
                    ["UPDATEDAT"] = r.UpdatedAt, ["UPDATEDBY"] = c.OldUser(r.UpdatedBy), ["PREEMAIL"] = r.PreEmail, ["PREISACTIVE"] = r.PreIsActive,
                    ["CONSENTTOMARKETINGINFO"] = r.ConsentToMarketingInfo,
                },
                FromOld = (row, c, x) =>
                {
                    var company = Long(row, "COMPANYID") == 0 ? null : Ref(c, Companies, row, "COMPANYID");
                    var contact = Long(row, "CONTACTID") == 0 ? null : Ref(c, Contacts, row, "CONTACTID");
                    if ((Long(row, "COMPANYID") != 0 && company is null) || (Long(row, "CONTACTID") != 0 && contact is null)) return null;
                    return new ContactEmail
                    {
                        Address = Text(row, "EMAIL", 200), CompanyId = company, ContactId = contact, IsActive = Bool(row, "ISACTIVE"), Cid = Long(row, "CID"),
                        CrmEmailLinkId = Long(row, "CRMEMAILLINKID"), CreatedAt = Date(row, "CREATEDAT"), CreatedBy = LoginOf(c, row, "CREATEDBY", x?.CreatedBy),
                        UpdatedAt = Date(row, "UPDATEDAT"), UpdatedBy = LoginOf(c, row, "UPDATEDBY", x?.UpdatedBy), PreEmail = Text(row, "PREEMAIL", 250),
                        PreIsActive = Bool(row, "PREISACTIVE"), ConsentToMarketingInfo = Bool(row, "CONSENTTOMARKETINGINFO"), OutSystemsId = Long(row, "ID"),
                    };
                },
            },
            new SyncEntity<Territory>
            {
                // Only active permanent rows are read (CrmTables): one that MyCRM turns off, or makes temporary, is inactive here.
                Name = Territories, Label = "territory", OldTable = Territories, OldCreatedOnColumn = null, OldUpdatedOnColumn = null,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                MarkRemovedHere = r => { r.IsActive = false; r.RemovedOn ??= clock(); if (r.RemovedBy.Length == 0) r.RemovedBy = "MyCRM"; },
                Describe = r => Domain.Territories.Title(r),
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TERRITORY"] = r.Number, ["OWNERID"] = c.OldUser(r.Owner), ["ISACTIVE"] = r.IsActive, ["ISTEMPORARY"] = false, ["ISOWNER"] = r.IsOwner,
                },
                FromOld = (row, c, x) => new Territory
                {
                    Name = x?.Name ?? "", Number = Long(row, "TERRITORY"), Owner = LoginOf(c, row, "OWNERID", Long(row, "OWNERID") == 0 ? "" : x?.Owner),
                    IsActive = Bool(row, "ISACTIVE") && !Bool(row, "ISTEMPORARY"), IsOwner = Bool(row, "ISOWNER"), CreatedOn = x?.CreatedOn, CreatedBy = x?.CreatedBy ?? "",
                    RemovedOn = x?.RemovedOn, RemovedBy = x?.RemovedBy ?? "", OutSystemsId = Long(row, "ID"),
                },
            },
            new SyncEntity<CompanyTerritory>
            {
                // Only the active links of active permanent territories are read (CrmTables): one MyCRM turns off is inactive here.
                Name = CompanyTerritories, Label = "company of a territory", OldTable = CompanyTerritories, OldCreatedOnColumn = null, OldUpdatedOnColumn = null,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                Parents = [(Territories, r => r.TerritoryId), (Companies, r => r.CompanyId)],
                OldParent = (Territories, "territory", "TERRITORYID"),
                CanWrite = (r, c) => c.OldId(Territories, r.TerritoryId) is not null && c.OldId(Companies, r.CompanyId) is not null,
                MarkRemovedHere = r => { r.IsActive = false; r.RemovedOn ??= clock(); if (r.RemovedBy.Length == 0) r.RemovedBy = "MyCRM"; },
                Describe = r => $"company #{r.CompanyId.ToString(CultureInfo.InvariantCulture)} in territory #{r.TerritoryId.ToString(CultureInfo.InvariantCulture)}",
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TERRITORYID"] = c.OldId(Territories, r.TerritoryId), ["COMPANYID"] = c.OldId(Companies, r.CompanyId), ["ISACTIVE"] = r.IsActive,
                    ["CREATEDBY"] = c.OldUser(r.CreatedBy),
                },
                FromOld = (row, c, x) =>
                {
                    if (Ref(c, Territories, row, "TERRITORYID") is not { } territory || Ref(c, Companies, row, "COMPANYID") is not { } company) return null;
                    var active = Bool(row, "ISACTIVE");
                    var removal = Removal(active, x?.RemovedOn, x?.RemovedBy, x?.IsActive ?? true);
                    return new CompanyTerritory
                    {
                        TerritoryId = territory, CompanyId = company, IsActive = active, CreatedBy = LoginOf(c, row, "CREATEDBY", x?.CreatedBy), CreatedOn = x?.CreatedOn,
                        RemovedOn = removal.On, RemovedBy = removal.By, OutSystemsId = Long(row, "ID"),
                    };
                },
            },
            new SyncEntity<TerritoryMovement>
            {
                Name = Movements, Label = "territory movement", OldTable = Movements, OldCreatedOnColumn = "UPDATEDAT", OldUpdatedOnColumn = null,
                RecentDays = MovementDays, CreatedOnColumn = "UpdatedAt",
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                Parents = [(Companies, r => r.CompanyId)],
                OldParent = (Companies, "company", "COMPANYID"),
                CanWrite = (r, c) => c.OldId(Companies, r.CompanyId) is not null,
                Describe = r => $"of company #{r.CompanyId.ToString(CultureInfo.InvariantCulture)}",
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["COMPANYID"] = c.OldId(Companies, r.CompanyId), ["FROMUSERID"] = c.OldUser(r.FromUser), ["TOUSERID"] = c.OldUser(r.ToUser),
                    ["UPDATEDBY"] = c.OldUser(r.UpdatedBy), ["UPDATEDAT"] = r.UpdatedAt,
                },
                FromOld = (row, c, x) => Ref(c, Companies, row, "COMPANYID") is not { } company ? null : new TerritoryMovement
                {
                    CompanyId = company, FromUser = LoginOf(c, row, "FROMUSERID", Long(row, "FROMUSERID") == 0 ? "" : x?.FromUser),
                    ToUser = LoginOf(c, row, "TOUSERID", Long(row, "TOUSERID") == 0 ? "" : x?.ToUser), UpdatedBy = LoginOf(c, row, "UPDATEDBY", x?.UpdatedBy),
                    UpdatedAt = Date(row, "UPDATEDAT"), OutSystemsId = Long(row, "ID"),
                },
            },
        ];
    }
}

/// <summary>The columns of MyCRM's tables that MyCRMRecords reads and writes, for the connection test (Connections tab).</summary>
public static class OldTableColumns
{
    public static readonly string[] GroupName = ["ID", "GROUPNAME"];
    public static readonly string[] Team = ["ID", "NAME", "SHORTNAME", "ISACTIVE"];
    public static readonly string[] User =
        ["ID", "USERID", "ROLEID", "TEAMID", "ISACTIVE", "CREATEDBY", "CREATEDAT", "UPDATEDBY", "UPDATEDAT", "DESIGNATION", "PHONENUMBER", "SALESMANCODE", "CID", "PROFILEID", "TERRITORYCODE"];
    public static readonly string[] Company =
    [
        "ID", "COMPANY", "BRN", "WEBSITE", "ISACTIVE", "INCCERTNO", "NOOFEMPLOYEES", "PAIDUPCAPITAL", "COMPANYGROUPID", "COMPANYGROUPNAMEID", "CRMCOMPANYID", "CREATEDDATE",
        "UPDATEDDATE", "COMPACCOUNTTYPEID", "COMPOWNERSHIPID", "ISCARETAKE", "UPDATEDBY", "CREATEDBY", "REMARKS", "ISCHANNELPARTNER", "PHONE", "FAX", "ISCREATEDBYMSD",
        "MIFPOTENTIAL", "REVENUE", "COMPANYINDUSTRYID", "PRECOMPANYNAME", "PREBRN", "PREWEBSITE", "PREISACTIVE", "PREINCCERTNO", "PRENOOFEMPLOYEES", "PREPAIDUPCAPITAL",
        "PRECOMPANYGROUPID", "PRECOMPANYGROUPNAMEID", "PRECOMPACCOUNTTYPEID", "PRECOMPOWNERSHIPID", "PREISCARETAKE", "PREREMARKS", "PREISCHANNELPARTNER", "PREPHONE",
        "PREFAX", "PREMIFPOTENTIAL", "PREREVENUE", "PRECOMPANYINDUSTRYID", "CUSTOMERTYPEID", "ISCHANNELPARTNERFORVSS", "ISCHANNELPARTNERFORCS", "CUSTOMERNO",
        "CREDITTERMS", "SALESREMARKS", "ADVANCECUSTOMERTYPEID",
    ];
    public static readonly string[] Caretake = ["ID", "COMPANYID", "CARETAKEACCOUNTREMARKS", "UPDATEDDATE", "CREATEDBY", "UPDATEDBY", "DATECREATED"];
    public static readonly string[] Relationship = ["ID", "COMPANYID", "COMPANYRELATIONSHIPTYPEID", "RELATEDCOMPANYID", "ISACTIVE"];
    public static readonly string[] Address =
    [
        "ID", "ADDRESSTYPEID", "UNITNUMBER", "BLOCK", "STREET", "BUILDINGNAME", "POSTALCODE", "ISACTIVE", "CRMADDRESSID", "ISDEFAULT", "COUNTRY", "CITY", "STATEPROVINCE",
        "PREADDRESSTYPEID", "PREUNITNUMBER", "PREBLOCK", "PRESTREET", "PREBUILDINGNAME", "PREPOSTALCODE", "PRECOUNTRY", "PRECITY", "PRESTATEPROVINCE", "PREISACTIVE",
        "CREATEDAT", "CREATEDBY", "UPDATEDAT", "UPDATEDBY",
    ];
    public static readonly string[] AddressLink = ["ID", "COMPANYID", "ADDRESSID", "CRMADDRESSLINK"];
    public static readonly string[] Contact =
    [
        "ID", "FIRSTNAME", "LASTNAME", "ISACTIVE", "DEPARTMENT", "CID", "MIDDLENAME", "CRMCONTACTLINKID", "DESIGNATION", "SALUTATION", "CREATEDAT", "CREATEDBY", "UPDATEDAT",
        "UPDATEDBY", "PREFIRSTNAME", "PRELASTNAME", "PREISACTIVE", "PREDEPARTMENT", "PREMIDDLENAME", "PREDESIGNATION", "PRESALUTATION", "ISLINKEDIN", "ISEQLIST",
    ];
    public static readonly string[] ContactLink = ["ID", "COMPANYID", "CONTACTID", "CID", "ISDEFAULT"];
    public static readonly string[] Phone =
        ["ID", "COMPANYID", "CONTACTID", "PHONETYPEID", "PHONE", "ISACTIVE", "CRMPHONEID", "CRMPHONELINKID", "CREATEDAT", "CREATEDBY", "UPDATEDAT", "UPDATEDBY", "PREPHONE", "PREPHONETYPEID", "PREISACTIVE"];
    public static readonly string[] Email =
        ["ID", "EMAIL", "COMPANYID", "CONTACTID", "ISACTIVE", "CID", "CRMEMAILLINKID", "CREATEDAT", "CREATEDBY", "UPDATEDAT", "UPDATEDBY", "PREEMAIL", "PREISACTIVE", "CONSENTTOMARKETINGINFO"];
    public static readonly string[] Territory = ["ID", "TERRITORY", "OWNERID", "ISACTIVE", "ISTEMPORARY", "ISOWNER"];
    public static readonly string[] CompanyTerritory = ["ID", "TERRITORYID", "COMPANYID", "ISACTIVE", "CREATEDBY"];
    public static readonly string[] Movement = ["ID", "COMPANYID", "FROMUSERID", "TOUSERID", "UPDATEDBY", "UPDATEDAT"];
    public static readonly string[] Static = ["ID", "LABEL"];

    /// <summary>MyCRM's static entities MyCRMRecords matches by label.</summary>
    public static readonly ChoiceList[] StaticLists =
    [
        CrmRoles.List, CompanyGroups.List, CompAccountTypes.List, CompOwnerships.List, CompanyIndustries.List, CustomerTypes.List, AdvanceCustomerTypes.List,
        AddressTypes.List, PhoneTypes.List, RelationshipTypes.List,
    ];

    public static readonly IReadOnlyList<(string Entity, string[] Columns)> Checked =
        new (string, string[])[]
        {
            (CrmSync.GroupNames, GroupName), (CrmSync.Teams, Team), (CrmSync.Users, User), (CrmSync.Companies, Company), (CrmSync.Caretakes, Caretake),
            (CrmSync.Relationships, Relationship), (CrmSync.Addresses, Address), (CrmSync.AddressLinks, AddressLink), (CrmSync.Contacts, Contact),
            (CrmSync.ContactLinks, ContactLink), (CrmSync.Phones, Phone), (CrmSync.Emails, Email), (CrmSync.Territories, Territory),
            (CrmSync.CompanyTerritories, CompanyTerritory), (CrmSync.Movements, Movement),
        }.Concat(StaticLists.Select(l => (l.OldEntity, Static))).ToList();
}

/// <summary>
/// MyCRM's tables in the OutSystems database (<see cref="OutSystemsTables"/>), but of its territories only the active
/// permanent ones, and of its companies' territory links only the active ones of those territories: MyCRM's Projects and
/// CSST parts keep tens of thousands of temporary territories with a million links, and the Advance rebuild left millions
/// of inactive rows; none of them is MyCRMRecords'. Also reads, for the access rule, which companies people reach through
/// MyCRM's temporary territories (<see cref="ITemporaryTerritories"/>).
/// </summary>
public sealed class CrmTables(ExternalDatabases databases, Func<string> module, IOldTables outSystems) : IOldTables, ITemporaryTerritories
{
    public string Title => outSystems.Title;

    private string TableOf(DbConnection connection, string entity) => databases.EntityTable(connection, module(), entity);

    private static bool Is(string table, string entity) => string.Equals(table, entity, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<OldRow> Rows(string table, string? sinceColumn = null, DateTime? since = null)
    {
        if (Is(table, CrmSync.Territories))
            return databases.Use(ExternalNames.OutSystems, connection => (IReadOnlyList<OldRow>)Sql.Rows(connection, null,
                $"SELECT * FROM {TableOf(connection, CrmSync.Territories)} WHERE [ISACTIVE] = 1 AND [ISTEMPORARY] = 0 ORDER BY [ID]"));
        if (Is(table, CrmSync.CompanyTerritories))
            return databases.Use(ExternalNames.OutSystems, connection => (IReadOnlyList<OldRow>)Sql.Rows(connection, null,
                $"SELECT ct.* FROM {TableOf(connection, CrmSync.CompanyTerritories)} ct JOIN {TableOf(connection, CrmSync.Territories)} t ON t.[ID] = ct.[TERRITORYID] " +
                "WHERE ct.[ISACTIVE] = 1 AND t.[ISACTIVE] = 1 AND t.[ISTEMPORARY] = 0 ORDER BY ct.[ID]"));
        return outSystems.Rows(table, sinceColumn, since);
    }

    public IReadOnlyList<OldRow> Rows(string table, string column, IReadOnlyCollection<long> values) => outSystems.Rows(table, column, values);
    public OldRow? Find(string table, IReadOnlyDictionary<string, object?> key) => outSystems.Find(table, key);
    public IReadOnlyDictionary<string, OldColumn> Columns(string table) => outSystems.Columns(table);
    public IReadOnlyDictionary<long, string> Labels(string table) => outSystems.Labels(table);
    public IReadOnlyList<OldUser> Users() => outSystems.Users();
    public IOldTablesWriter BeginWrite() => outSystems.BeginWrite();

    /// <summary>How many rows the import would read (the territories as filtered above).</summary>
    public int Count(string table) => Rows(table).Count;

    /// <summary>MyCRM's Ids of the active companies of the active temporary territories of these people (by login).</summary>
    public IReadOnlyCollection<long> CompanyIds(IReadOnlyCollection<string> logins)
    {
        var users = outSystems.Users();
        var ids = logins.SelectMany(l => users.Where(u => Strings.SameText(u.Login, l)).Select(u => u.Id)).Distinct().ToList();
        if (ids.Count == 0) return Array.Empty<long>();
        return databases.Use(ExternalNames.OutSystems, connection =>
        {
            var companies = new HashSet<long>();
            foreach (var chunk in ids.Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => x.v))
                foreach (var row in Sql.Rows(connection, null,
                             $"SELECT DISTINCT ct.[COMPANYID] FROM {TableOf(connection, CrmSync.CompanyTerritories)} ct JOIN {TableOf(connection, CrmSync.Territories)} t ON t.[ID] = ct.[TERRITORYID] " +
                             $"WHERE ct.[ISACTIVE] = 1 AND t.[ISACTIVE] = 1 AND t.[ISTEMPORARY] = 1 AND t.[OWNERID] IN ({string.Join(", ", chunk.Select(v => v.ToString(CultureInfo.InvariantCulture)))})"))
                    companies.Add(Long(row, "COMPANYID"));
            return (IReadOnlyCollection<long>)companies;
        });
    }
}
