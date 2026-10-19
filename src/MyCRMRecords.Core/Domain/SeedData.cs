using System.Globalization;
using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>
/// Sample records for trying MyCRMRecords out (demo tools on): a team, a group name, two companies (a parent and its
/// subsidiary) with a billing address and a primary contact with a phone and an email, and a sample territory of the
/// person adding them holding the parent company. Nothing is written to another database.
/// </summary>
public static class SeedData
{
    public const string SampleCompany = "Sample Holdings Pte Ltd";

    public static string Add(IRecordStore store, DateTime now, Actor actor)
    {
        if (store.Count<Company>(Query.Where("Name", SampleCompany)) > 0) return "The sample records are there already.";
        var login = Strings.Shorten(actor.Login, 256);
        if (store.Find<CrmTeam>(Query.Where("Name", "Sample Team")).Count == 0) store.Insert(new CrmTeam { Name = "Sample Team", IsActive = true });
        var group = store.Insert(new CompanyGroupName { GroupName = "Sample Group" });
        Company Make(string name, string brn) => store.Insert(new Company
        {
            Name = name, Brn = brn, Website = "www.example.com", IsActive = true, CompanyGroup = "SME", CompanyGroupNameId = group.Id, CreatedBy = login,
            CreatedDate = now.Date, CompAccountType = "User", NoOfEmployees = 25,
        });
        var parent = Make(SampleCompany, "SAMPLE0001X");
        var child = Make("Sample Services Pte Ltd", "SAMPLE0002X");
        store.Insert(new CompanyRelationship { CompanyId = parent.Id, RelatedCompanyId = child.Id, RelationshipType = RelationshipTypes.Parent, IsActive = true });
        var address = store.Insert(new Address
        {
            AddressType = AddressTypes.Billing, Block = "1", Street = "Sample Road", UnitNumber = "#01-01", BuildingName = "Sample Building", PostalCode = "000001",
            Country = "Singapore", IsDefault = true, IsActive = true, CreatedAt = now, CreatedBy = login,
        });
        store.Insert(new CompanyAddressLink { CompanyId = parent.Id, AddressId = address.Id, IsActive = true });
        var contact = store.Insert(new Contact { Salutation = "Ms", FirstName = "Casey", LastName = "Customer", Designation = "Office Manager", IsActive = true, CreatedAt = now, CreatedBy = login });
        store.Insert(new CompanyContactLink { CompanyId = parent.Id, ContactId = contact.Id, IsDefault = true });
        store.Insert(new Phone { CompanyId = parent.Id, ContactId = contact.Id, PhoneType = "Office", Number = "60000001", IsActive = true, CreatedAt = now, CreatedBy = login });
        store.Insert(new ContactEmail { CompanyId = parent.Id, ContactId = contact.Id, Address = "casey@example.com", IsActive = true, ConsentToMarketingInfo = true, CreatedAt = now, CreatedBy = login });
        var number = Math.Max(9000, store.Find<Territory>().Select(t => t.Number).DefaultIfEmpty(0).Max() + 1);
        var territory = store.Insert(new Territory { Name = "Sample territory", Number = number, Owner = login, IsActive = true, IsOwner = true, CreatedOn = now, CreatedBy = login });
        store.Insert(new CompanyTerritory { TerritoryId = territory.Id, CompanyId = parent.Id, IsActive = true, CreatedBy = login, CreatedOn = now });
        store.Insert(new TerritoryMovement { CompanyId = parent.Id, FromUser = "", ToUser = login, UpdatedBy = login, UpdatedAt = now });
        return $"Sample records added by {actor.ChangedBy}: companies #{parent.Id.ToString(CultureInfo.InvariantCulture)} and #{child.Id.ToString(CultureInfo.InvariantCulture)}, and territory {Territories.Title(territory)}.";
    }
}
