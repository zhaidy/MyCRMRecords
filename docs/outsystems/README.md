# How MyCRM's Users, Territory and Company part works

MyCRMRecords takes over the **Users**, **Territory** and **Company** part of the OutSystems application **MyCRM**
(a Traditional web module, eSpace `MyCRM`, tables `OSUSR_824_…` in the OutSystems database). A company's addresses,
contacts, phones and emails come with it. **The rest of MyCRM stays in OutSystems and keeps being used**: activities,
leads, prospects, projects, OAA forms, MIF, training events, sales goals, the dashboard and the reports.

This document is what MyCRMRecords keeps of MyCRM's behaviour. It is written from MyCRM's data model (OSSYS_ENTITY and
OSSYS_ENTITY_ATTR, read on 2026-10-19). **MyCRM's screens, actions, roles, timers and emails could not be read yet**:
Service Studio did not answer any request about MyCRM (every one timed out after 60 seconds, while the other modules
answered at once). The sections marked *(to read)* are completed once it answers.

## Contents

- [1. What moves and what stays](#1-what-moves-and-what-stays)
- [2. Where the part meets the rest of MyCRM](#2-where-the-part-meets-the-rest-of-mycrm)
- [3. Users](#3-users)
- [4. Territory](#4-territory)
- [5. Company](#5-company)
- [6. Addresses, contacts, phones and emails](#6-addresses-contacts-phones-and-emails)
- [7. The "Pre…" columns](#7-the-pre-columns)
- [8. Screens, roles, timers and emails (to read)](#8-screens-roles-timers-and-emails-to-read)
- [9. Other applications that read these tables](#9-other-applications-that-read-these-tables)
- [10. What MyCRM deletes (to read)](#10-what-mycrm-deletes-to-read)
- [11. Questions still open](#11-questions-still-open)
- [12. Rules MyCRMRecords changes on purpose](#12-rules-mycrmrecords-changes-on-purpose)
- [Appendix: the data model](#appendix-the-data-model)

## 1. What moves and what stays

MyCRM has 102 entities. The part MyCRMRecords takes over is 32 of them:

| Area | Entities | Static entities (lists) |
|---|---|---|
| Users | `CUser`, `Team`, `VacantUsers` | `Role` |
| Territory | `Territory`, `CompanyTerritory`, `TerritoryRelationship`, `TerritoryMovement`, `TerritoryCode` (table `TempTerritoryCode`) | `TerritoryRelationshipType` |
| Company | `Company`, `CompOwner`, `CompanyCaretake`, `CompanyRelationship`, `CompanyGroupName`, `TempCompanies` | `CompanyGroup`, `CompAccountType`, `CompOwnership`, `CompanyIndustry`, `CustomerType`, `AdvanceCustomerType`, `CompanyRelationshipType` |
| Addresses, contacts, phones, emails | `Address`, `CompanyAddressLink`, `Contacts`, `CompanyContactLink`, `AddressContactLink`, `Phone`, `Email` | `AddressType`, `PhoneType` |

Rows in production (2026-10-19): `Territory` 4,461,908; `CompanyTerritory` 3,541,794; `TempTerritoryCode` 1,384,629;
`CompanyAddressLink` 177,346; `TerritoryMovement` 141,881; `Phone` 137,869; `Address` 107,241; `CompanyContactLink`
103,963; `Contacts` 98,647; `Company` 61,228; `Email` 39,486; `CUser` 2,573; `CompanyGroupName` 1,584;
`CompanyCaretake` 713; `CompanyRelationship` 295; `Team` 52; `Role` 45. **Empty**: `CompOwner`, `TempCompanies`,
`AddressContactLink`, `TerritoryRelationship`, `VacantUsers`.

Everything else stays in MyCRM. Because MyCRM keeps running, **its tables stay the shared copy**: MyCRMRecords keeps
its own records and a two-way sync keeps MyCRM's tables up to date, permanently (not only until a switch-over, as with
the earlier apps). MyCRM's other screens, its timers and the other applications that read these tables (section 9)
therefore need no change.

## 2. Where the part meets the rest of MyCRM

Found from the references between entities:

- **Company** is referenced by 15 entities that stay in MyCRM: `CompanyActivity`, `CompanyCompetitorsMIF`,
  `DocumentFile`, `LeadProspects`, `MIFGroupChart`, `OAAForm`, `Project`, `ProjectCompany`, `ProjectMembers`,
  `Prospect`, `TrainingEvent` (and `CompanyAddressLink`, `CompanyContactLink`, `Phone`, `Email`, which move).
- **Contacts** are referenced by `CompanyActivity`, `CompanyActivityContactLink`, `LeadProspects`, `ProjectMembers`
  and `Prospect`; **Address** by `Prospect` (bill and delivery address).
- **Territory** is referenced by `ProjectMembers` only.
- **CUser**, **Role** and **Team** are referenced by nothing outside this part: the rest of MyCRM points at OutSystems'
  own users (`OSSYS_USER`), not at `CUser`.

So a company, contact, address or territory keeps **the same Id in both apps**: one made in MyCRMRecords is added to
MyCRM first and takes the Id it got there (the sync's "same numbers" rule), and MyCRM's activities, prospects and
projects keep pointing at it.

## 3. Users

- `CUser`: a person's MyCRM profile, one per OutSystems user (`UserId`): their MyCRM `Role` and `Team`,
  `Designation`, `PhoneNumber`, `SalesmanCode`, `TerritoryCode`, `IsActive`, `CId` (an Id in another CRM, see 11),
  `ProfileId` (a reference to an entity of another module, see 11), created and updated by and on.
- `Role` (static): MyCRM's own role list with a `Level`. This is not an OutSystems role: who may sign in to MyCRM and
  what they see is decided by the OutSystems roles *(to read)*; `Role` is what the sales screens use (MyFiles called
  MyCRM's `Role(user)` and `UserCanAccessCompany(RoleId, UserList, CompanyId)` with it).
- `Team`: a name and short name, active or not.
- `VacantUsers`: an OutSystems user and a `PreviousOwner` (a number), active or not: probably the accounts of someone
  who left, kept for whoever takes them over *(to read)*.

## 4. Territory

- `Territory`: a territory number (`Territory`, a number) with an owner (`OwnerID`, an OutSystems user) and the flags
  `IsActive`, `IsTemporary` and `IsOwner`. A territory number can have several rows (several people, one of them the
  owner, some temporary) *(to read)*.
- `CompanyTerritory`: a company in a territory (by the territory row's Id), active or not, created by.
- `TerritoryRelationship`: a territory related to another, of a `TerritoryRelationshipType` (static), active or not.
- `TerritoryMovement`: the history of a company moving from one user to another (from, to, by, on).
- `TerritoryCode` (table `OSUSR_824_TempTerritoryCode`): a company's territory code and its parent territory, as
  text. The table name says *Temp*: possibly filled by an upload *(to read)*.
- **The sizes say these are worked out, not typed**: 4.5 million `Territory` rows and 3.5 million `CompanyTerritory`
  rows for 61,000 companies and 2,600 users (about 58 per company), and 1.4 million `TempTerritoryCode` rows. They look
  like an access list MyCRM builds from a hierarchy of territory codes (who may see which company), probably by a timer
  or a batch, rather than records people keep one by one *(to read)*. They cannot be synced like the other tables
  (reading every row every few minutes); how MyCRMRecords handles them is decided once MyCRM's logic is read.

## 5. Company

- `Company` (52 attributes): name, BRN, website, incorporation certificate number, employees, paid-up capital, group
  (`CompanyGroup`, static) and group name (`CompanyGroupName`), account type, ownership, industry, customer type and
  Advance customer type (static lists), `CustomerNo`, `CreditTerms`, remarks and sales remarks (up to 50,000
  characters), phone and fax, the flags `IsActive`, `IsCaretake`, `IsChannelPartner` (and for VSS and CS),
  `IsCreatedByMSD`, MIF potential, revenue, `CRMCompanyId` (an Id in another CRM, see 11), created and updated by and
  on (dates only). Most attributes have a `Pre…` copy (section 7).
- `CompOwner`: the company's owners (OutSystems users). **It is empty in production**: MyFile's provisional
  territory rule ("the person owns the company in `CompOwner`") therefore lets no MyCRM Sales, Manager or Head open a
  file of a MyCRM company. The real rule is in the territory tables.
- `CompanyCaretake`: a caretake account's remarks, created and updated by and on.
- `CompanyRelationship`: a company related to another, of a `CompanyRelationshipType` (static), active or not.
- `CompanyGroupName`: the group names a company can belong to.
- `TempCompanies`: a company name and BRN (long texts) with a company and who updated it: by its name a working table,
  possibly for an upload or a bulk match *(to read)*.

## 6. Addresses, contacts, phones and emails

- `Address`: type (`AddressType`, static), unit number, block, street, building, postal code, country, city, state,
  `IsDefault`, `IsActive`, `CRMAddressId`, created and updated by and at, and `Pre…` copies.
- `CompanyAddressLink`: an address of a company (`CRMAddressLink`: an Id in another CRM).
- `Contacts`: first, middle and last name, salutation, designation, department, `IsLinkedIn`, `IsEqList`, `IsActive`,
  `CId`, `CRMContactLinkId`, created and updated by and at, and `Pre…` copies.
- `CompanyContactLink`: a contact of a company, `IsDefault`, `CId`.
- `AddressContactLink`: a contact at an address.
- `Phone`: a number of a company or a contact, of a `PhoneType` (static), `IsActive`, `CRMPhoneId`, `CRMPhoneLinkId`,
  created and updated, and `Pre…` copies.
- `Email`: an address of a company or a contact, `ConsentToMarketingInfo`, `IsActive`, `CID`, `CRMEmailLinkId`,
  created and updated, and `Pre…` copies.

## 7. The "Pre…" columns

`Company`, `Address`, `Contacts`, `Phone` and `Email` keep a `Pre…` copy of most of their attributes
(`PreCompanyName`, `PreBRN`, … `PreIsActive`). What they hold is not known yet *(to read)*: the values before the last
change (shown to someone who approves it, or written to another CRM, which the `CRM…Id` columns suggest), or the
values last sent somewhere. MyCRMRecords keeps them and writes them back unchanged until MyCRM's logic is read, then
fills them exactly as MyCRM does.

## 8. Screens, roles, timers and emails (to read)

Not read yet: MyCRM's screens for users, territories and companies (and the screen that opens one record, with its
input parameter, for the redirects), its OutSystems roles and who sees what, the server actions behind them
(`UserCanAccessCompany`, `Role`, anything that writes these tables), timers, emails and the integrations
(Advance, the other CRM).

## 9. Other applications that read these tables

- **MyFile** reads `Company`, `CUser`, `Role` and `CompOwner` (company names, BRN look-up, a person's MyCRM role, the
  provisional territory rule).
- **MyCustomers** matches BRNs between Advance and MyCRM's companies.
- **MyVendors** once considered MyCRM's `IsITS` (not used).

They keep reading MyCRM's tables, which the sync keeps up to date.

## 10. What MyCRM deletes (to read)

Most references to `Company` are *Protect* (a company with activities, contacts and so on cannot be deleted); the
static entities' are *Ignore*. Whether MyCRM's screens delete rows or only set `IsActive` is to be read.
MyCRMRecords deletes nothing either way, and marks what it removes in MyCRM (`ISACTIVE = 0` where the table has it).

## 11. Questions still open

1. What the `Pre…` columns are for (section 7).
2. What `CId`, `CRMCompanyId`, `CRMAddressId`, `CRMContactLinkId`, `CRMPhoneId`, `CRMEmailLinkId` refer to (another
   CRM, perhaps D365), and whether MyCRM still writes them.
3. `CUser.ProfileId` refers to an entity of another module: which.
4. What `TempCompanies`, `TempTerritoryCode` and `VacantUsers` are used for.
5. MyCRM's `UserCanAccessCompany` rule (MyFile uses a provisional "owners only" rule until it is read, and `CompOwner` is
   empty).
6. How `Territory`, `CompanyTerritory` and `TempTerritoryCode` are filled (millions of rows: section 4).

## 12. Rules MyCRMRecords changes on purpose

None yet.

## Appendix: the data model

### Users

**CUser** (`OSUSR_824_CUser`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| UserId | → (OutSystems user) |  |  |
| RoleId | → Role |  |  |
| TeamId | → Team |  |  |
| IsActive | Boolean |  |  |
| CreatedBy | → (OutSystems user) |  |  |
| CreatedAt | Date Time |  |  |
| UpdatedBy | → (OutSystems user) |  |  |
| UpdatedAt | Date Time |  |  |
| Designation | Text | 50 |  |
| PhoneNumber | Phone | 20 |  |
| SalesmanCode | Text | 50 |  |
| CId | Integer |  |  |
| ProfileId | → (Users.Profile?) |  |  |
| TerritoryCode | Text | 50 |  |

**Role** (`OSUSR_824_Role`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Integer (auto) |  | yes |
| Label | Text | 50 | yes |
| Order | Integer |  | yes |
| Is_Active | Boolean |  | yes |
| Level | Integer |  |  |

**Team** (`OSUSR_824_Team`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| Name | Text | 255 |  |
| ShortName | Text | 50 |  |
| IsActive | Boolean |  |  |

**VacantUsers** (`OSUSR_824_VacantUsers`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| UserId | → (OutSystems user) |  |  |
| PreviousOwner | Long Integer |  |  |
| IsActive | Boolean |  |  |

### Territory

**Territory** (`OSUSR_824_Territory`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| Territory | Long Integer |  |  |
| OwnerID | → (OutSystems user) |  |  |
| IsActive | Boolean |  |  |
| IsTemporary | Boolean |  |  |
| IsOwner | Boolean |  |  |

**CompanyTerritory** (`OSUSR_824_CompanyTerritory`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| TerritoryID | → Territory |  |  |
| CompanyID | → Company |  |  |
| IsActive | Boolean |  |  |
| CreatedBy | → (OutSystems user) |  |  |

**TerritoryRelationship** (`OSUSR_824_TerritoryRelationship`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| Territory | → Territory |  |  |
| RelatedTerritory | → Territory |  |  |
| TerritoryRelationshipTypeID | → TerritoryRelationshipType |  |  |
| IsActive | Boolean |  |  |

**TerritoryRelationshipType** (`OSUSR_824_TerritoryRelationshipType`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Integer (auto) |  | yes |
| Label | Text | 50 | yes |
| Order | Integer |  | yes |
| Is_Active | Boolean |  | yes |

**TerritoryMovement** (`OSUSR_824_TerritoryMovement`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| CompanyId | → Company |  |  |
| FromUserId | → (OutSystems user) |  |  |
| ToUserId | → (OutSystems user) |  |  |
| UpdatedBy | → (OutSystems user) |  |  |
| UpdatedAt | Date Time |  |  |

**TerritoryCode** (`OSUSR_824_TempTerritoryCode`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| TerritoryCode | Text | 60 |  |
| CompanyId | → Company |  |  |
| ParentTerritory | Text | 60 |  |

### Company

**Company** (`OSUSR_824_Company`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| Company | Text | 250 | yes |
| BRN | Text | 50 |  |
| Website | Text | 100 |  |
| IsActive | Boolean |  |  |
| IncCertNo | Text | 50 |  |
| NoOfEmployees | Integer |  |  |
| PaidUpCapital | Integer |  |  |
| CompanyGroupId | → CompanyGroup |  |  |
| CompanyGroupNameId | → CompanyGroupName |  |  |
| CRMCompanyId | Long Integer |  |  |
| CreatedDate | Date |  |  |
| UpdatedDate | Date |  |  |
| CompAccountTypeId | → CompAccountType |  |  |
| CompOwnershipId | → CompOwnership |  |  |
| IsCaretake | Boolean |  |  |
| UpdatedBy | → (OutSystems user) |  |  |
| CreatedBy | → (OutSystems user) |  |  |
| Remarks | Text | 10000 |  |
| IsChannelPartner | Boolean |  |  |
| Phone | Phone | 20 |  |
| Fax | Phone | 20 |  |
| IsCreatedByMSD | Boolean |  |  |
| MIFPotential | Integer |  |  |
| Revenue | Currency |  |  |
| CompanyIndustryId | → CompanyIndustry |  |  |
| PreCompanyName | Text | 250 | yes |
| PreBRN | Text | 50 | yes |
| PreWebsite | Text | 100 |  |
| PreIsActive | Boolean |  |  |
| PreIncCertNo | Text | 50 |  |
| PreNoOfEmployees | Integer |  |  |
| PrePaidUpCapital | Integer |  |  |
| PreCompanyGroupId | → CompanyGroup |  |  |
| PreCompanyGroupNameId | → CompanyGroupName |  |  |
| PreCompAccountTypeId | → CompAccountType |  |  |
| PreCompOwnershipId | → CompOwnership |  |  |
| PreIsCaretake | Boolean |  |  |
| PreRemarks | Text | 10000 |  |
| PreIsChannelPartner | Boolean |  |  |
| PrePhone | Phone | 20 |  |
| PreFax | Phone | 20 |  |
| PreMIFPotential | Integer |  |  |
| PreRevenue | Currency |  |  |
| PreCompanyIndustryId | → CompanyIndustry |  |  |
| CustomerTypeId | → CustomerType |  |  |
| IsChannelPartnerForVSS | Boolean |  |  |
| IsChannelPartnerForCS | Boolean |  |  |
| CustomerNo | Text | 50 |  |
| CreditTerms | Text | 50 |  |
| SalesRemarks | Text | 50000 |  |
| AdvanceCustomerTypeId | → AdvanceCustomerType |  |  |

**CompOwner** (`OSUSR_824_CompOwner`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| CompanyId | → Company |  |  |
| UserId | → (OutSystems user) |  |  |

**CompanyCaretake** (`OSUSR_824_CompanyCaretake`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| CompanyId | → Company |  |  |
| CaretakeAccountRemarks | Text | 10000 |  |
| UpdatedDate | Date |  |  |
| CreatedBy | → (OutSystems user) |  |  |
| UpdatedBy | → (OutSystems user) |  |  |
| DateCreated | Date |  |  |

**CompanyRelationship** (`OSUSR_824_CompanyRelationship`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| CompanyID | → Company |  |  |
| CompanyRelationshipTypeID | → CompanyRelationshipType |  |  |
| RelatedCompanyID | → Company |  |  |
| IsActive | Boolean |  |  |

**CompanyRelationshipType** (`OSUSR_824_CompanyRelationshipType`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Integer (auto) |  | yes |
| Label | Text | 50 | yes |
| Order | Integer |  | yes |
| Is_Active | Boolean |  | yes |

**CompanyGroupName** (`OSUSR_824_CompanyGroupName`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| GroupName | Text | 255 |  |

**CompanyGroup** (`OSUSR_824_CompanyGroup`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Integer (auto) |  | yes |
| Label | Text | 50 | yes |
| Order | Integer |  | yes |
| Is_Active | Boolean |  | yes |

**CompAccountType** (`OSUSR_824_CompAccountType`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Integer (auto) |  | yes |
| Label | Text | 50 | yes |
| Order | Integer |  | yes |
| Is_Active | Boolean |  | yes |

**CompOwnership** (`OSUSR_824_CompOwnership`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Integer (auto) |  | yes |
| Label | Text | 50 | yes |
| Order | Integer |  | yes |
| Is_Active | Boolean |  | yes |

**CompanyIndustry** (`OSUSR_824_CompanyIndustry`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Integer (auto) |  | yes |
| Label | Text | 50 | yes |
| Order | Integer |  | yes |
| Is_Active | Boolean |  | yes |

**CustomerType** (`OSUSR_824_CustomerType`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Integer (auto) |  | yes |
| Label | Text | 50 | yes |
| Order | Integer |  | yes |
| Is_Active | Boolean |  | yes |

**AdvanceCustomerType** (`OSUSR_824_AdvanceCustomerType`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Integer (auto) |  | yes |
| Label | Text | 50 | yes |
| Order | Integer |  | yes |
| Is_Active | Boolean |  | yes |
| TypeCode | Text | 50 |  |

**TempCompanies** (`OSUSR_824_TempCompanies`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| Company | Text | 2500 |  |
| BRN | Text | 500 |  |
| CompanyId | → Company |  |  |
| UpdatedBy | → (OutSystems user) |  |  |

### Addresses, contacts, phones and emails

**Address** (`OSUSR_824_Address`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| AddressTypeID | → AddressType |  |  |
| UnitNumber | Text | 2000 |  |
| Block | Text | 2000 | yes |
| Street | Text | 2000 | yes |
| BuildingName | Text | 2000 |  |
| PostalCode | Text | 50 | yes |
| IsActive | Boolean |  |  |
| CRMAddressId | Long Integer |  |  |
| IsDefault | Boolean |  |  |
| Country | Text | 255 |  |
| City | Text | 255 |  |
| StateProvince | Text | 255 |  |
| PreAddressTypeID | → AddressType |  |  |
| PreUnitNumber | Text | 2000 |  |
| PreBlock | Text | 2000 | yes |
| PreStreet | Text | 2000 | yes |
| PreBuildingName | Text | 2000 |  |
| PrePostalCode | Text | 50 | yes |
| PreCountry | Text | 255 |  |
| PreCity | Text | 255 |  |
| PreStateProvince | Text | 255 |  |
| PreIsActive | Boolean |  |  |
| CreatedAt | Date Time |  |  |
| CreatedBy | → (OutSystems user) |  |  |
| UpdatedAt | Date Time |  |  |
| UpdatedBy | → (OutSystems user) |  |  |

**AddressType** (`OSUSR_824_AddressType`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Integer (auto) |  | yes |
| Label | Text | 50 | yes |
| Order | Integer |  | yes |
| Is_Active | Boolean |  | yes |

**CompanyAddressLink** (`OSUSR_824_CompanyAddressLink`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| CompanyID | → Company |  |  |
| AddressID | → Address |  |  |
| CRMAddressLink | Long Integer |  |  |

**Contacts** (`OSUSR_824_Contacts`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| FirstName | Text | 250 |  |
| LastName | Text | 250 | yes |
| IsActive | Boolean |  |  |
| Department | Text | 50 |  |
| CId | Long Integer |  |  |
| MiddleName | Text | 250 |  |
| CRMContactLinkId | Long Integer |  |  |
| Designation | Text | 250 |  |
| Salutation | Text | 250 |  |
| CreatedAt | Date Time |  |  |
| CreatedBy | → (OutSystems user) |  |  |
| UpdatedAt | Date Time |  |  |
| UpdatedBy | → (OutSystems user) |  |  |
| PreFirstName | Text | 250 |  |
| PreLastName | Text | 250 | yes |
| PreIsActive | Boolean |  |  |
| PreDepartment | Text | 50 |  |
| PreMiddleName | Text | 250 |  |
| PreDesignation | Text | 250 |  |
| PreSalutation | Text | 250 |  |
| IsLinkedIn | Boolean |  |  |
| IsEqList | Boolean |  |  |

**CompanyContactLink** (`OSUSR_824_CompanyContactLink`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| CompanyID | → Company |  |  |
| ContactID | → Contacts |  |  |
| CId | Long Integer |  |  |
| IsDefault | Boolean |  |  |

**AddressContactLink** (`OSUSR_824_AddressContactLink`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| AddressID | → Address |  |  |
| ContactID | → Contacts |  |  |

**Phone** (`OSUSR_824_Phone`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| CompanyID | → Company |  |  |
| ContactID | → Contacts |  |  |
| PhoneTypeID | → PhoneType |  |  |
| Phone | Phone | 20 |  |
| IsActive | Boolean |  |  |
| CRMPhoneId | Long Integer |  |  |
| CRMPhoneLinkId | Long Integer |  |  |
| CreatedAt | Date Time |  |  |
| CreatedBy | → (OutSystems user) |  |  |
| UpdatedAt | Date Time |  |  |
| UpdatedBy | → (OutSystems user) |  |  |
| PrePhone | Phone | 20 |  |
| PrePhoneTypeID | → PhoneType |  |  |
| PreIsActive | Boolean |  |  |

**PhoneType** (`OSUSR_824_PhoneType`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Integer (auto) |  | yes |
| Label | Text | 50 | yes |
| Order | Integer |  | yes |
| Is_Active | Boolean |  | yes |

**Email** (`OSUSR_824_Email`)

| Attribute | Type | Length | Mandatory |
|---|---|---|---|
| Id | Long Integer (auto) |  | yes |
| Email | Text | 200 |  |
| CompanyID | → Company |  |  |
| ContactID | → Contacts |  |  |
| IsActive | Boolean |  |  |
| CID | Long Integer |  |  |
| CRMEmailLinkId | Long Integer |  |  |
| CreatedAt | Date Time |  |  |
| CreatedBy | → (OutSystems user) |  |  |
| UpdatedAt | Date Time |  |  |
| UpdatedBy | → (OutSystems user) |  |  |
| PreEmail | Email | 250 |  |
| PreIsActive | Boolean |  |  |
| ConsentToMarketingInfo | Boolean |  |  |

