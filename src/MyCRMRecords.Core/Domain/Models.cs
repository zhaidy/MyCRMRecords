using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

// MyCRMRecords's tables (dbo.Cr…), one row per record. People are kept as logins (RSP\jsmith) with their name next to them,
// not as OutSystems user ids. OutSystemsId is the record's Id in MyCRM when it came from there (imported or synced).

/// <summary>An email sent (or not, and why).</summary>
[Table("dbo.CrEmail")]
public sealed class EmailMessage : IRecord
{
    public long Id { get; set; }
    [Indexed] public DateTime At { get; set; }
    [Len(20)] public string RequestKind { get; set; } = "";
    public long RequestId { get; set; }
    [Len(4000)] public string To { get; set; } = "";
    [Len(4000)] public string Cc { get; set; } = "";
    [Len(500)] public string Subject { get; set; } = "";
    public bool Sent { get; set; }
    [Len(4000)] public string Error { get; set; } = "";
}

/// <summary>One run of a scheduled task (or of its Run now button), or of the import.</summary>
[Table("dbo.CrJobRun")]
public sealed class JobRun : IRecord
{
    public long Id { get; set; }
    [Indexed, Len(50)] public string Job { get; set; } = "";
    [Indexed] public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    [Len(20)] public string Outcome { get; set; } = "";
    [Len(600)] public string By { get; set; } = "";
    public int Processed { get; set; }
    [Len(int.MaxValue)] public string Summary { get; set; } = "";
    public DateTime? Watermark { get; set; }
}

/// <summary>Someone who has opened MyCRMRecords (or was given a role).</summary>
[Table("dbo.CrUser")]
public sealed class AppUser : IRecord
{
    public long Id { get; set; }
    [Indexed, Len(256)] public string Login { get; set; } = "";
    [Len(256)] public string DisplayName { get; set; } = "";
    [Len(256)] public string Email { get; set; } = "";
    public DateTime? LastSeenAt { get; set; }
}

[Table("dbo.CrUserRole")]
public sealed class UserRole : IRecord
{
    public long Id { get; set; }
    [Indexed, Len(256)] public string Login { get; set; } = "";
    [Len(50)] public string Role { get; set; } = "";
    /// <summary>False once the role was taken away (records are never deleted): when and by whom.</summary>
    public bool IsActive { get; set; } = true;
    public DateTime? RemovedOn { get; set; }
    [Len(600)] public string RemovedBy { get; set; } = "";
}

/// <summary>An action through MyCRMRecords (every change and sign-in), for the audit log.</summary>
[Table("dbo.CrAudit")]
public sealed class AuditEntry : IRecord
{
    public long Id { get; set; }
    [Indexed] public DateTime At { get; set; }
    [Len(256)] public string Login { get; set; } = "";
    [Len(256)] public string DisplayName { get; set; } = "";
    [Len(256)] public string ImpersonatorLogin { get; set; } = "";
    [Len(256)] public string ImpersonatorName { get; set; } = "";
    [Len(200)] public string Action { get; set; } = "";
    [Len(10)] public string Method { get; set; } = "";
    [Len(500)] public string Path { get; set; } = "";
    [Len(500)] public string Target { get; set; } = "";
    public int Status { get; set; }
    public bool Succeeded { get; set; }
    [Len(4000)] public string Message { get; set; } = "";
    [Len(int.MaxValue)] public string Details { get; set; } = "";
    [Len(100)] public string Address { get; set; } = "";
}

/// <summary>
/// A record paired with its row in MyCRM by the sync (OldAppSync), with both sides' fingerprints as they were
/// at the last sync. RemovedHere: deleted here, marked in MyCRM (LocalId is negative for a number taken there
/// but not used). RemovedThere: deleted in MyCRM, marked here. Pending: written to MyCRM, not yet
/// confirmed. Unpaired: no longer a pair.
/// </summary>
[Table("dbo.CrSyncLink")]
public sealed class SyncLink : IRecord
{
    public long Id { get; set; }
    [Indexed, Len(50)] public string Entity { get; set; } = "";
    [Indexed] public long LocalId { get; set; }
    [Indexed, Len(400)] public string OldKey { get; set; } = "";
    [Len(64)] public string LocalHash { get; set; } = "";
    [Len(64)] public string OldHash { get; set; } = "";
    public bool RemovedHere { get; set; }
    public bool RemovedThere { get; set; }
    public bool Pending { get; set; }
    public DateTime SyncedOn { get; set; }
    /// <summary>No longer a pair (one side was replaced, or the row's insert never reached the old app); kept, never deleted.</summary>
    public bool Unpaired { get; set; }
}

/// <summary>A sync with MyCRM: when, who started it, what it did.</summary>
[Table("dbo.CrSyncRun")]
public sealed class SyncRun : IRecord
{
    public long Id { get; set; }
    [Indexed] public DateTime StartedOn { get; set; }
    public DateTime? FinishedOn { get; set; }
    [Len(256)] public string By { get; set; } = "";
    public bool Ok { get; set; }
    [Len(1000)] public string Message { get; set; } = "";
    public int ToOldApp { get; set; }
    public int ToHere { get; set; }
    [Len(int.MaxValue)] public string Details { get; set; } = "";
}

/// <summary>A value the sync wrote in MyCRM, with the value it replaced, so any change can be put back by hand.</summary>
[Table("dbo.CrSyncChange")]
public sealed class SyncChange : IRecord
{
    public long Id { get; set; }
    [Indexed] public long RunId { get; set; }
    [Indexed] public DateTime ChangedOn { get; set; }
    [Len(100)] public string OldTable { get; set; } = "";
    [Indexed, Len(400)] public string OldKey { get; set; } = "";
    [Len(200)] public string Record { get; set; } = "";
    [Len(20)] public string Action { get; set; } = "";
    [Len(128)] public string Column { get; set; } = "";
    [Len(int.MaxValue)] public string Before { get; set; } = "";
    [Len(int.MaxValue)] public string After { get; set; } = "";
}

// ---- MyCRM's records (Users, Territory and Company) ----
// Static entities of MyCRM (Role, CompanyGroup, AddressType...) are kept as codes of the lists in Lists.cs and matched
// by label in MyCRM, never by Id. "Removed" fields say when and by whom something was taken away here.

/// <summary>A team (MyCRM's Team), named after its manager. Never deleted: switched inactive.</summary>
[Table("dbo.CrTeam")]
public sealed class CrmTeam : IRecord
{
    public long Id { get; set; }
    [Len(255)] public string Name { get; set; } = "";
    /// <summary>MyCRM's ShortName (the manager's logon in the Advance set-up).</summary>
    [Len(50)] public string ShortName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>
/// One of a person's CRM profiles (MyCRM's CUser): one per (person, team), with the person's MyCRM role. A person with
/// no active one is not a CRM user. Taken away, it is inactive with no team (MyCRM deleted it).
/// </summary>
[Table("dbo.CrCrmUser")]
public sealed class CrmUser : IRecord
{
    public long Id { get; set; }
    [Indexed, Len(256)] public string Login { get; set; } = "";
    /// <summary><see cref="CrmRoles"/>; "" for none.</summary>
    [Len(50)] public string Role { get; set; } = "";
    public long? TeamId { get; set; }
    public bool IsActive { get; set; } = true;
    [Len(256)] public string CreatedBy { get; set; } = "";
    public DateTime? CreatedAt { get; set; }
    [Len(256)] public string UpdatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    [Len(50)] public string Designation { get; set; } = "";
    [Len(20)] public string PhoneNumber { get; set; } = "";
    [Len(50)] public string SalesmanCode { get; set; } = "";
    /// <summary>The person's Id in the old CRM (kept as it is).</summary>
    public int CId { get; set; }
    /// <summary>MyOrders' sales target profile (kept as it is: MyCRMRecords does not offer MyOrders' profiles).</summary>
    public long? ProfileId { get; set; }
    [Len(50)] public string TerritoryCode { get; set; } = "";
    /// <summary>The team it had when it was taken away (MyCRM's TeamId is emptied then).</summary>
    public long? FormerTeamId { get; set; }
    public DateTime? RemovedOn { get; set; }
    [Len(600)] public string RemovedBy { get; set; } = "";
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>A company group name (MyCRM's CompanyGroupName). Removed here, it is inactive here and kept in MyCRM.</summary>
[Table("dbo.CrCompanyGroupName")]
public sealed class CompanyGroupName : IRecord
{
    public long Id { get; set; }
    [Len(255)] public string GroupName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime? RemovedOn { get; set; }
    [Len(600)] public string RemovedBy { get; set; } = "";
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>
/// A company (MyCRM's Company). Its number is MyCRM's while the sync is on. The Pre… fields are the values just before
/// the last edit on the company form, as MyCRM keeps them (its lists sort by PreName).
/// </summary>
[Table("dbo.CrCompany")]
public sealed class Company : IRecord
{
    public long Id { get; set; }
    /// <summary>MyCRM's "Company" (the name).</summary>
    [Len(250)] public string Name { get; set; } = "";
    [Indexed, Len(50)] public string Brn { get; set; } = "";
    [Len(100)] public string Website { get; set; } = "";
    public bool IsActive { get; set; } = true;
    [Len(50)] public string IncCertNo { get; set; } = "";
    public int NoOfEmployees { get; set; }
    public int PaidUpCapital { get; set; }
    /// <summary>"Customer Type" on the pages: <see cref="CompanyGroups"/>.</summary>
    [Len(50)] public string CompanyGroup { get; set; } = "";
    public long? CompanyGroupNameId { get; set; }
    /// <summary>The company's Id in the old CRM (kept as it is).</summary>
    public long CrmCompanyId { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
    [Len(50)] public string CompAccountType { get; set; } = "";
    [Len(50)] public string CompOwnership { get; set; } = "";
    public bool IsCaretake { get; set; }
    [Len(256)] public string UpdatedBy { get; set; } = "";
    [Len(256)] public string CreatedBy { get; set; } = "";
    [Len(10000)] public string Remarks { get; set; } = "";
    public bool IsChannelPartner { get; set; }
    [Len(20)] public string Phone { get; set; } = "";
    [Len(20)] public string Fax { get; set; } = "";
    public bool IsCreatedByMsd { get; set; }
    public int MifPotential { get; set; }
    public decimal Revenue { get; set; }
    [Len(50)] public string CompanyIndustry { get; set; } = "";
    [Len(250)] public string PreName { get; set; } = "";
    [Len(50)] public string PreBrn { get; set; } = "";
    [Len(100)] public string PreWebsite { get; set; } = "";
    public bool PreIsActive { get; set; }
    [Len(50)] public string PreIncCertNo { get; set; } = "";
    public int PreNoOfEmployees { get; set; }
    public int PrePaidUpCapital { get; set; }
    [Len(50)] public string PreCompanyGroup { get; set; } = "";
    public long? PreCompanyGroupNameId { get; set; }
    [Len(50)] public string PreCompAccountType { get; set; } = "";
    [Len(50)] public string PreCompOwnership { get; set; } = "";
    public bool PreIsCaretake { get; set; }
    [Len(10000)] public string PreRemarks { get; set; } = "";
    public bool PreIsChannelPartner { get; set; }
    [Len(20)] public string PrePhone { get; set; } = "";
    [Len(20)] public string PreFax { get; set; } = "";
    public int PreMifPotential { get; set; }
    public decimal PreRevenue { get; set; }
    [Len(50)] public string PreCompanyIndustry { get; set; } = "";
    [Len(50)] public string CustomerType { get; set; } = "";
    public bool IsChannelPartnerForVss { get; set; }
    public bool IsChannelPartnerForCs { get; set; }
    /// <summary>Advance's customer number (the scheduled task Customer numbers from Advance).</summary>
    [Len(50)] public string CustomerNo { get; set; } = "";
    [Len(50)] public string CreditTerms { get; set; } = "";
    [Len(50000)] public string SalesRemarks { get; set; } = "";
    [Len(50)] public string AdvanceCustomerType { get; set; } = "";
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>A caretake account's remarks (MyCRM's CompanyCaretake): only MyCRM's old imports wrote them; shown.</summary>
[Table("dbo.CrCompanyCaretake")]
public sealed class CompanyCaretake : IRecord
{
    public long Id { get; set; }
    [Indexed] public long CompanyId { get; set; }
    [Len(10000)] public string CaretakeAccountRemarks { get; set; } = "";
    public DateTime? UpdatedDate { get; set; }
    [Len(256)] public string CreatedBy { get; set; } = "";
    [Len(256)] public string UpdatedBy { get; set; } = "";
    public DateTime? DateCreated { get; set; }
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>
/// A parent company and one of its subsidiaries (MyCRM's CompanyRelationship: CompanyID is the parent, RelatedCompanyID
/// the subsidiary). Removed, it is inactive (MyCRM deleted it).
/// </summary>
[Table("dbo.CrCompanyRelationship")]
public sealed class CompanyRelationship : IRecord
{
    public long Id { get; set; }
    [Indexed] public long CompanyId { get; set; }
    [Indexed] public long RelatedCompanyId { get; set; }
    [Len(50)] public string RelationshipType { get; set; } = RelationshipTypes.Parent;
    public bool IsActive { get; set; } = true;
    public DateTime? RemovedOn { get; set; }
    [Len(600)] public string RemovedBy { get; set; } = "";
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>An address (MyCRM's Address). Its number is MyCRM's while the sync is on. Deleted, it is inactive (as in MyCRM).</summary>
[Table("dbo.CrAddress")]
public sealed class Address : IRecord
{
    public long Id { get; set; }
    /// <summary><see cref="AddressTypes"/>.</summary>
    [Len(50)] public string AddressType { get; set; } = "";
    [Len(2000)] public string UnitNumber { get; set; } = "";
    [Len(2000)] public string Block { get; set; } = "";
    [Len(2000)] public string Street { get; set; } = "";
    [Len(2000)] public string BuildingName { get; set; } = "";
    [Len(50)] public string PostalCode { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public long CrmAddressId { get; set; }
    public bool IsDefault { get; set; }
    [Len(255)] public string Country { get; set; } = "";
    [Len(255)] public string City { get; set; } = "";
    [Len(255)] public string StateProvince { get; set; } = "";
    [Len(50)] public string PreAddressType { get; set; } = "";
    [Len(2000)] public string PreUnitNumber { get; set; } = "";
    [Len(2000)] public string PreBlock { get; set; } = "";
    [Len(2000)] public string PreStreet { get; set; } = "";
    [Len(2000)] public string PreBuildingName { get; set; } = "";
    [Len(50)] public string PrePostalCode { get; set; } = "";
    [Len(255)] public string PreCountry { get; set; } = "";
    [Len(255)] public string PreCity { get; set; } = "";
    [Len(255)] public string PreStateProvince { get; set; } = "";
    public bool PreIsActive { get; set; }
    public DateTime? CreatedAt { get; set; }
    [Len(256)] public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    [Len(256)] public string UpdatedBy { get; set; } = "";
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>A company's address (MyCRM's CompanyAddressLink). Its address deleted, it is inactive here (MyCRM deleted it).</summary>
[Table("dbo.CrCompanyAddressLink")]
public sealed class CompanyAddressLink : IRecord
{
    public long Id { get; set; }
    [Indexed] public long CompanyId { get; set; }
    [Indexed] public long AddressId { get; set; }
    public long CrmAddressLink { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? RemovedOn { get; set; }
    [Len(600)] public string RemovedBy { get; set; } = "";
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>A contact person (MyCRM's Contacts). Its number is MyCRM's while the sync is on. Deleted, it is inactive (as in MyCRM).</summary>
[Table("dbo.CrContact")]
public sealed class Contact : IRecord
{
    public long Id { get; set; }
    [Len(250)] public string FirstName { get; set; } = "";
    [Len(250)] public string LastName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    [Len(50)] public string Department { get; set; } = "";
    /// <summary>The person's Id in the old CRM (kept as it is).</summary>
    public long CId { get; set; }
    /// <summary>"Christian Name" in MyCRM.</summary>
    [Len(250)] public string MiddleName { get; set; } = "";
    public long CrmContactLinkId { get; set; }
    [Len(250)] public string Designation { get; set; } = "";
    [Len(250)] public string Salutation { get; set; } = "";
    public DateTime? CreatedAt { get; set; }
    [Len(256)] public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    [Len(256)] public string UpdatedBy { get; set; } = "";
    [Len(250)] public string PreFirstName { get; set; } = "";
    [Len(250)] public string PreLastName { get; set; } = "";
    public bool PreIsActive { get; set; }
    [Len(50)] public string PreDepartment { get; set; } = "";
    [Len(250)] public string PreMiddleName { get; set; } = "";
    [Len(250)] public string PreDesignation { get; set; } = "";
    [Len(250)] public string PreSalutation { get; set; } = "";
    /// <summary>"Is Sales Navigator" in MyCRM.</summary>
    public bool IsLinkedIn { get; set; }
    public bool IsEqList { get; set; }
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>A company's contact (MyCRM's CompanyContactLink); IsDefault is the company's primary contact.</summary>
[Table("dbo.CrCompanyContactLink")]
public sealed class CompanyContactLink : IRecord
{
    public long Id { get; set; }
    [Indexed] public long CompanyId { get; set; }
    [Indexed] public long ContactId { get; set; }
    public long CId { get; set; }
    public bool IsDefault { get; set; }
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>A contact's phone number (MyCRM's Phone). Removed, it is inactive (as in MyCRM).</summary>
[Table("dbo.CrPhone")]
public sealed class Phone : IRecord
{
    public long Id { get; set; }
    public long? CompanyId { get; set; }
    [Indexed] public long? ContactId { get; set; }
    /// <summary><see cref="PhoneTypes"/>.</summary>
    [Len(50)] public string PhoneType { get; set; } = "";
    /// <summary>MyCRM's "Phone" (the number).</summary>
    [Len(20)] public string Number { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public long CrmPhoneId { get; set; }
    public long CrmPhoneLinkId { get; set; }
    public DateTime? CreatedAt { get; set; }
    [Len(256)] public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    [Len(256)] public string UpdatedBy { get; set; } = "";
    [Len(20)] public string PrePhone { get; set; } = "";
    [Len(50)] public string PrePhoneType { get; set; } = "";
    public bool PreIsActive { get; set; }
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>A contact's email address (MyCRM's Email). Removed, it is inactive (as in MyCRM).</summary>
[Table("dbo.CrContactEmail")]
public sealed class ContactEmail : IRecord
{
    public long Id { get; set; }
    /// <summary>MyCRM's "Email" (the address).</summary>
    [Len(200)] public string Address { get; set; } = "";
    public long? CompanyId { get; set; }
    [Indexed] public long? ContactId { get; set; }
    public bool IsActive { get; set; } = true;
    public long Cid { get; set; }
    public long CrmEmailLinkId { get; set; }
    public DateTime? CreatedAt { get; set; }
    [Len(256)] public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    [Len(256)] public string UpdatedBy { get; set; } = "";
    [Len(250)] public string PreEmail { get; set; } = "";
    public bool PreIsActive { get; set; }
    public bool ConsentToMarketingInfo { get; set; }
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>
/// A named territory (one permanent row of MyCRM's Territory): a number and a name, its salesperson and its companies.
/// The name is kept here only (MyCRM has no column for it). No salesperson: vacant. Taken away, it is inactive with its
/// companies' links (as MyCRM's "Delete User And Territory").
/// </summary>
[Table("dbo.CrTerritory")]
public sealed class Territory : IRecord
{
    public long Id { get; set; }
    [Len(100)] public string Name { get; set; } = "";
    /// <summary>MyCRM's Territory (its number; 0 on territories MyCRM made by hand).</summary>
    public long Number { get; set; }
    /// <summary>The salesperson's login (MyCRM's OwnerID); "" while vacant.</summary>
    [Indexed, Len(256)] public string Owner { get; set; } = "";
    public bool IsActive { get; set; } = true;
    /// <summary>MyCRM's IsOwner (its dashboard lists the companies of territories marked so).</summary>
    public bool IsOwner { get; set; } = true;
    public DateTime? CreatedOn { get; set; }
    [Len(256)] public string CreatedBy { get; set; } = "";
    public DateTime? RemovedOn { get; set; }
    [Len(600)] public string RemovedBy { get; set; } = "";
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>A company in a territory (MyCRM's CompanyTerritory). Taken out, it is inactive (as in MyCRM); never made active again.</summary>
[Table("dbo.CrCompanyTerritory")]
public sealed class CompanyTerritory : IRecord
{
    public long Id { get; set; }
    [Indexed] public long TerritoryId { get; set; }
    [Indexed] public long CompanyId { get; set; }
    public bool IsActive { get; set; } = true;
    [Len(256)] public string CreatedBy { get; set; } = "";
    public DateTime? CreatedOn { get; set; }
    public DateTime? RemovedOn { get; set; }
    [Len(600)] public string RemovedBy { get; set; } = "";
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>A company moving from one salesperson to another (MyCRM's TerritoryMovement); "" is nobody.</summary>
[Table("dbo.CrTerritoryMovement")]
public sealed class TerritoryMovement : IRecord
{
    public long Id { get; set; }
    [Indexed] public long CompanyId { get; set; }
    [Len(256)] public string FromUser { get; set; } = "";
    [Len(256)] public string ToUser { get; set; } = "";
    [Len(256)] public string UpdatedBy { get; set; } = "";
    [Indexed] public DateTime? UpdatedAt { get; set; }
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>Every table, in the order the migrations create them.</summary>
public static class Tables
{
    public static readonly IReadOnlyList<Type> All =
    [
        typeof(EmailMessage), typeof(JobRun), typeof(AppUser), typeof(UserRole), typeof(AuditEntry), typeof(SyncLink), typeof(SyncRun), typeof(SyncChange),
        typeof(CrmTeam), typeof(CrmUser), typeof(CompanyGroupName), typeof(Company), typeof(CompanyCaretake), typeof(CompanyRelationship), typeof(Address),
        typeof(CompanyAddressLink), typeof(Contact), typeof(CompanyContactLink), typeof(Phone), typeof(ContactEmail), typeof(Territory), typeof(CompanyTerritory),
        typeof(TerritoryMovement),
    ];
}
