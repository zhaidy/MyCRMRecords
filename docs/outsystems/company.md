# How MyCRM's Company part works (companies, addresses, contacts, phones, emails)

Read from MyCRM's decoded model (`omltest`, 2026-10-19). Names of screens, labels and messages are quoted as MyCRM
shows them, typos included. "Not certain:" marks what the decoded model could not settle.

Words used below:

- **Old CRM**: the CRM that was used before MyCRM. Its tables are read through the **AdvanceConnector** module:
  `Company 2`, `Person`, `Person_Link`, `Address 2`, `Address_Link`, `Phone 2`, `PhoneLink`, `Email 2`, `EmailLink`,
  `Users 2` (Sage-CRM-style column prefixes `Comp_`, `Pers_`, `PeLi_`, `Addr_`, `AdLi_`, `Phon_`, `PLink_`, `Emai_`,
  `ELink_`, `User_`). AdvanceConnector also exposes Advance's `Customer_Supp`, `customer`, `CustomerMapping`,
  `Email_Master` and the ranking tables `LMA_Rank` and `FG500_Rank`.
- **CRMUser**: MyCRM's OutSystems role that every MyCRM screen in this part requires.
- **Role(user)**: MyCRM's own `Role` static entity, read from `CUser.RoleId`.
- **IsSalesAdmin(user)**: true when the user's `CUser.RoleId` is `SalesAdmin` **or** `Admin`.
- **Privileged** (for company visibility): Telemarketer, Admin, SalesAdmin, or `ShowAllCompany(role)` (Admin,
  SalesAdmin, MD, CSST, CSSTManager, MDS/MITS Specialist Managers, Head, CreditManager, CreditGM).

## Contents

1. [The data](#1-the-data)
2. [The Pre… columns and the old-CRM Id columns](#2-the-pre-columns-and-the-old-crm-id-columns)
3. [Screens and blocks](#3-screens-and-blocks)
4. [Server actions](#4-server-actions)
5. [Import timers and batch actions](#5-import-timers-and-batch-actions)
6. [What MyCRM deletes, and what it only marks inactive](#6-what-mycrm-deletes-and-what-it-only-marks-inactive)
7. [What other parts of MyCRM depend on](#7-what-other-parts-of-mycrm-depend-on)
8. [UI that could be friendlier](#8-ui-that-could-be-friendlier)
9. [Open questions](#9-open-questions)

---

## 1. The data

### 1.1 Company (`OSUSR_824_Company`, 52 attributes)

| Attribute | Type | Notes |
|---|---|---|
| `Id` | LongInteger | auto |
| `Company` | Text 250, mandatory | the name |
| `BRN` | Text 50 | business registration number |
| `Website` | Text 100 | |
| `IsActive` | Boolean, default True | |
| `IncCertNo` | Text 50 | no screen edits it |
| `NoOfEmployees` | Integer | edited by the `NoOfEmployees` block and the upload `UploadNoOfEmployees` |
| `PaidUpCapital` | Integer | no screen edits it |
| `CompanyGroupId` | → `CompanyGroup` (static) | labelled **"Customer Type"** everywhere on screen |
| `CompanyGroupNameId` | → `CompanyGroupName` | labelled "Company Group Name" / "Company Group" |
| `CRMCompanyId` | LongInteger | the company's `Comp_CompanyId` in the old CRM (section 2) |
| `CreatedDate`, `UpdatedDate` | Date (CreatedDate default CurrDate) | dates only |
| `CompAccountTypeId` | → `CompAccountType` | "Account Type" / "A/C Type" |
| `CompOwnershipId` | → `CompOwnership` | "Comp Ownership" |
| `IsCaretake` | Boolean | set by the imports from the old CRM's `comp_caretake` |
| `CreatedBy`, `UpdatedBy` | → User | |
| `Remarks` | Text 10000 | "Remarks" |
| `IsChannelPartner` | Boolean | "Is Channel Partner" |
| `Phone`, `Fax` | PhoneNumber 20 | no screen edits them |
| `IsCreatedByMSD` | Boolean, default False | "Is Created By MSD"; only the CompanyDetail form touches it; nothing in MyCRM reads it |
| `MIFPotential` | Integer | no screen edits it |
| `Revenue` | Currency | no screen edits it |
| `CompanyIndustryId` | → `CompanyIndustry` | shown as "Industry:"; no screen edits it |
| `CustomerTypeId` | → `CustomerType` | not read or written by any MyCRM flow |
| `IsChannelPartnerForVSS`, `IsChannelPartnerForCS` | Boolean | not read or written by any MyCRM flow |
| `Pre…` (19 columns) | copies | section 2 |
| `CustomerNo` | Text 50 | Advance customer number, filled by timer `UpdateCustomerNo` |
| `CreditTerms` | Text 50 | not read or written by any MyCRM flow |
| `SalesRemarks` | Text 50000 | "Sales Remarks" |
| `AdvanceCustomerTypeId` | → `AdvanceCustomerType` | "Advance Customer Type" |

All references from Company use delete rule **Protect**.

### 1.2 Other entities

| Entity | Attributes |
|---|---|
| `CompanyCaretake` | `CompanyId`, `CaretakeAccountRemarks` (10000), `UpdatedDate`, `CreatedBy`, `UpdatedBy`, `DateCreated`. Only the imports write it (one row per caretake company); CompanyDetails lists it. |
| `CompanyRelationship` | `CompanyID` (the **parent**), `RelatedCompanyID` (the **child**), `CompanyRelationshipTypeID`, `IsActive` (default True). Every row MyCRM creates has type `Parent`. |
| `CompanyGroupName` | `GroupName` (255). |
| `Address` | `AddressTypeID`, `UnitNumber` (2000), `Block` (2000, mandatory), `Street` (2000, mandatory), `BuildingName` (2000), `PostalCode` (50, mandatory), `IsActive` (default True), `CRMAddressId`, `IsDefault`, `Country` (default "Singapore"), `City`, `StateProvince`, `Pre…` copies, `CreatedAt` (default CurrDateTime), `CreatedBy`, `UpdatedAt`, `UpdatedBy`. |
| `CompanyAddressLink` | `CompanyID`, `AddressID`, `CRMAddressLink` (old CRM `AdLi_AddressLinkId`). |
| `Contacts` | `FirstName`, `LastName` (mandatory), `IsActive` (default True), `Department` (50), `CId` (old CRM `Pers_PersonId`), `MiddleName` (labelled **"Christian Name"**), `CRMContactLinkId`, `Designation`, `Salutation`, `CreatedAt/By`, `UpdatedAt/By`, `Pre…` copies, `IsLinkedIn` (labelled **"Is Sales Navigator"**), `IsEqList` ("Is Eq. List"). |
| `CompanyContactLink` | `CompanyID`, `ContactID`, `CId` (old CRM `PeLi_PersonLinkId`), `IsDefault` (shown as "(Primary Contact)"). |
| `AddressContactLink` | `AddressID`, `ContactID`. **No flow reads or writes it** (empty in production). |
| `Phone` | `CompanyID`, `ContactID`, `PhoneTypeID`, `Phone` (20), `IsActive`, `CRMPhoneId` (old CRM `Phon_PhoneId`), `CRMPhoneLinkId` (never set), `CreatedAt/By`, `UpdatedAt/By`, `PrePhone`, `PrePhoneTypeID`, `PreIsActive`. |
| `Email` | `Email` (200), `CompanyID`, `ContactID`, `IsActive`, `CID` (old CRM email Id), `CRMEmailLinkId` (never set), `CreatedAt/By`, `UpdatedAt/By`, `PreEmail`, `PreIsActive`, `ConsentToMarketingInfo`. |

Phones and emails always belong to a **contact** (`ContactID`); MyCRM also fills `CompanyID` on the ones its screens
create (and on emails the email import creates), but no screen shows a phone or email that has no contact.

### 1.3 Static lists (record name → Label, Order, active)

Static record Ids are assigned by OutSystems at publish and are not in the model; read them from the tables.

- **CompanyGroup** (shown as "Customer Type"): SME → "SME" (4); DEASME → "SMEDEA" (12); Dealer → "DEA" (9);
  JKA → "JKA" (7); Govt → "GOV" (6); CA → "CA" (10, **inactive**); LG → "LG" (5, **inactive**); GA → "GA" (11);
  MA → "MA" (8); LMA → "LMA" (3, **inactive**); GMA → "GMA" (2).
- **CompAccountType**: User → "User" (1); NonUser → "Non-User" (2).
- **CompOwnership**: Owned → "Owned" (1); Declared → "Declared" (2).
- **CompanyIndustry** (40, all active): Legal (1), Banking and financial (2), Manufacturing (3), Construction and
  engineering (4), Utilities (5), Telecommunications (6), Education and training (7), Government departments (8),
  Medical (9), Insurance (10), Religious and non-profit public service (11), Retail groups, chains, franchises (12),
  Agriculture (13), Apparel (14), Banking (15), Biotechnology (16), Chemicals (17), Communications (18),
  Construction (19), Consulting (20), Education (21), Electronics (22), Energy (23), Engineering (24),
  Entertainment (25), Environmental (26), Finance (27), Food & Beverage (28), Government (29), Healthcare (30),
  Hospitality (31), Machinery (32), Media (33), Not For Profit (34), Other (35), Recreation (36), Retail (37),
  Shipping (38), Technology (39), Transportation (40). (Record names are the labels without spaces/punctuation,
  e.g. `Retailgroups_chains_franchises`, `FoodBeverage`, `NotForProfit`.)
- **CustomerType**: BDG_ZBA → "BDG - ZBA" (1); BDG_ITonMIF → "BDG - IT on MIF" (2).
- **AdvanceCustomerType** (Label, Order, `TypeCode`): Others "Others" 1 9OTH; Telecom_Media "Telecom, Media" 2 9TEL;
  Government "Government" 3 9GOV; GeneralServices "General Services" 4 9GSV; FinancialInstitute "Financial
  Institute" 5 9FIN; Construction "Construction" 6 9CON; WholesaleRetail "Wholesale & Retail" 7 9W&R; Healthcare
  "Healthcare" 8 9HEA; ConsultancyService "Consultancy Service" 9 9PRO; Education "Education" 10 9EDU;
  Restaurant_FoodCater "Restaurant, Food Cater" 11 9F&B; Manufacturing "Manufacturing" 12 9MFT; OilGas "Oil & Gas"
  13 9O&G; Agri_Forest_Fisheries "Agri, Forest, Fisheries" 14 9AFF; Logistic_Forwarder "Logistic, Forwarder" 15
  9LOG; RealEstate_MCST "Real Estate, MCST" 16 9REA; PublishingBroadcasting "Publishing Broadcasting" 17 9PUB;
  Automotive "Automotive" 18 9AUT; Infrastructure_Utility "Infrastructure, Utility" 19 9INF. All active.
- **AddressType**: Billing (1), Delivery (2), Storage (3), Mailing (4).
- **PhoneType**: Office (1), Mobile (2), Fax (3), Home (4).
- **CompanyRelationshipType**: Parent (1), Child (2), Peer (3). Only `Parent` is ever used.

---

## 2. The Pre… columns and the old-CRM Id columns

### 2.1 What the Pre… columns hold

**They hold the values a record had just before its last edit in a MyCRM screen** — a one-step change history. They
are not an approval workflow (nothing reads them to accept or reject a change) and they are not sent to any other
system: the only code that pushes data to the old CRM is disabled (2.3) and does not use them.

Who writes them, exactly:

| Entity | Where | What is written |
|---|---|---|
| Company | **CompanyDetail** (admin edit screen) → Save, **existing company only** | Re-reads the company from the database (`GetPreData`) and copies every current value into its `Pre…` column: `PreCompanyName`←`Company`, `PreBRN`, `PreWebsite`, `PreIsActive`, `PreIncCertNo`, `PreNoOfEmployees`, `PrePaidUpCapital`, `PreCompanyGroupId`, `PreCompanyGroupNameId`, `PreCompAccountTypeId`, `PreIsCaretake`, `PreCompOwnershipId`, `PreRemarks`, `PreIsChannelPartner`, `PrePhone`, `PreFax`, `PreMIFPotential`, `PreRevenue`, `PreCompanyIndustryId` — all 19, also for fields the form does not show. |
| Company | CompanyDetail → Save, **new company** | Nothing: `PreCompanyName` and `PreBRN` (mandatory columns) stay empty text. |
| Address | **AddressDetail_PopUp** → Save, existing address | Re-reads the address (`GetOldData`) and copies `AddressTypeID`, `UnitNumber`, `Block`, `Street`, `BuildingName`, `PostalCode`, `Country`, `City`, `StateProvince`, `IsActive` into the `Pre…` columns. |
| Address | **Address** block → delete (SQL `DeleteAddress`) | `IsActive = 0`, **`PreIsActive = 1`**, `UpdatedAt`, `UpdatedBy`. |
| Contacts | **ContactDetail_Popup** → Save, existing contact | `PreFirstName`, `PreLastName`, `PreIsActive`, `PreDepartment`, `PreMiddleName`, `PreDesignation`, `PreSalutation` ← the values **read when the popup was opened** (not re-read at save time). |
| Contacts | ContactDetail_Popup → "Delete Contact" | `IsActive = False`, **`PreIsActive = True`**. |
| Phone, Email | ContactDetail_Popup | Not maintained. New rows start from an empty record, so `Pre…` stay empty/false; removing a row sets `IsActive = False` without touching `PreIsActive`. |
| Contacts, Phone, Email | **ContactdDetail_PopupNew_CanDelete** (unused screen, 3.15) | Same rule as above, with a fresh re-read (`GetPreData`): Phone `PrePhone/PrePhoneTypeID/PreIsActive`, Email `PreEmail/PreIsActive`; deleting a phone/email/contact sets `IsActive = False`, `PreIsActive = True`. |

Never written by: the import timers (they create rows with empty `Pre…` and leave them alone on update), the
`NoOfEmployees`, `SalesRemarks` and `CurrentAddressType` blocks, `UploadNoOfEmployees`, and the SQL group updates.
Those keep whatever `Pre…` values the row already had (an UpdateEntity of the whole record writes them back
unchanged).

Who reads them: **no flow uses a Pre… value in logic.** They are only used as **sort keys** of list columns:

- The admin list **Company** (Admin flow) sorts its columns "Company", "BRN", "Website", "Is Active" and
  "Is Channel Partner" by `{Company}.[PreCompanyName]`, `[PreBRN]`, `[PreWebsite]`, `[PreIsActive]`,
  `[PreIsChannelPartner]` — i.e. by the *previous* values (a bug: the default order is `{Company}.[Company]`).
- Lists in parts that stay in MyCRM sort their "Company" column by `{Company}.[PreCompanyName]`:
  `GetExpiringMachines`, `GetExpiredMachines`, `GetProspects_List`, `GetProspectsByStage`, `TrainingEvents`,
  `GetCompanyCompetitorsMIFs`, `Leads` (which also sorts a contact column by `{Contacts}.[PreFirstName]`) and two
  other prospect lists.

So MyCRMRecords must **keep filling the Pre… columns exactly as above** (MyCRM's lists keep sorting on
`PreCompanyName`, which for companies never edited in CompanyDetail is empty).

### 2.2 The old-CRM Id columns

| Column | Holds | Written by |
|---|---|---|
| `Company.CRMCompanyId` | old CRM `Company 2.Comp_CompanyId` | timers Company, CompanyDaily, ChannelPartnerCompanies, MissingCompany (match key); UpdateCompanyCRMIdByBRN, UpdateCompanyCRMIdByName, UpdateOldCRMCompanyId (fill it in for companies created in MyCRM) |
| `Company.CustomerNo` | Advance customer number (from `Customer_Supp`), with " #Multiple" appended when several Advance customers share the BRN | timer UpdateCustomerNo |
| `Address.CRMAddressId` | old CRM `Addr_AddressId` | timer Address |
| `CompanyAddressLink.CRMAddressLink` | old CRM `AdLi_AddressLinkId` | timer CompanyAddressLink; AddressDetail_PopUp keeps the existing value |
| `Contacts.CId` | old CRM `Pers_PersonId` | timers Contact, ImportUpdateContact |
| `CompanyContactLink.CId` | old CRM `PeLi_PersonLinkId` | timers ContactLink, ImportUpdateContactLink |
| `Phone.CRMPhoneId` | old CRM `Phon_PhoneId` | timer Phone |
| `Email.CID` | old CRM email Id (`ELink_EmailId`) | timer Email (ImportEmail) |
| `Contacts.CRMContactLinkId`, `Phone.CRMPhoneLinkId`, `Email.CRMEmailLinkId` | — | **never set** by any flow |
| `Company.IsCreatedByMSD` | a flag ticked by hand | only the CompanyDetail form; nothing reads it |

The old-CRM Ids are the import timers' match keys (create the row when no MyCRM row has that Id, else overwrite it)
and are used by the mapping functions `CompanyId(CRMCompanyId)`, `ContactId(CRMContactId)`,
`AddressId(CRMAddressId)` and `GetCompanyID(CompanyID)` that other imports (prospects, activities, CSST, territory
codes) use to turn an old-CRM Id into a MyCRM Id.

### 2.3 Pushing to the old CRM: present but disabled

AddressDetail_PopUp → Save and ContactdDetail_PopupNew_CanDelete → SaveContact contain a branch "when the record is
new, and `GetOldCRMCompanyId` ≠ 0, and `GetServer() = "RSPOSAL"` (production): get an API token
(`GenerateAPIToken`, kept in `Session.SPANIFApiToken`) and call `CreateAddressesByCompanyId` /
`CreateContactInOldCRM`". **All of those nodes are disabled** in the model, and `GetOldCRMCompanyId` itself goes
straight from Start to End (returns 0). Nothing in this part sends data anywhere.

---

## 3. Screens and blocks

All screens below require the OutSystems role **CRMUser** unless stated. Roles inside screens use MyCRM's `Role`.

### 3.1 Companies (screen, flow "Company")

Menu entry `MenuSubItem.Company`. Title "Companies". Contains only the block **CompanyList** with `IsPopUp = False`.
No preparation.

### 3.2 CompanyList (block; input `IsPopUp`)

The company search, used by Companies, CompanyList_PopUp and CompanyRelationshipsAdd_PopUp.

**Filters** (in popup mode under a collapsible "More filters" heading, otherwise always shown):

| Label | Widget | Variable |
|---|---|---|
| "Customer Type" | drop-down of `CompanyGroup.Label`, empty option "-" | `CompanyGroupId` |
| "Filter by Company Group" (button) → replaced by "Group Name" | button sets `FilterByGroupName = True`; then the late-loaded block **CompanyGroupName_LazyLoad** shows a Select2 drop-down of group names | `GroupNameId` |
| "Postal Code" | text, max 50, tooltip "Postal Code" | `PostalCode` |
| "BRN" | text, max 50 | `BRN` |
| "Contract Start From" / "To" | date inputs (YYYY-MM-DD) with calendar pickers | `ContractStart`, `ContractEnd` |
| "Company Name" | text, max 100, placeholder **"Type the Company, BRN or Created By"** (it only searches the company name, see 3.3), focused on load | `CompanyName` |

**Buttons**

- "Search" and a change in Postal Code, BRN or the contract dates → `OnInputChange(Reset = False)`: sets
  `IsDisplayResults = True` and refreshes; the result table only appears after this.
- "Reset" → `OnInputChange(Reset = True)`: clears all filters (`CompanyGroupId`, `CompanyName`, `GroupNameId`,
  `PostalCode`, `BRN`, `ContractStart`, `ContractEnd`) and hides the table (`IsDisplayResults = False`).
- Changing "Customer Type" calls `CompanyGroupFilter`, whose steps (clear `GroupNameId`, refresh) are **disabled**:
  it does nothing; the new value is used on the next search.
- "Company Group Chart" (navigates to `CompanyPieChart`, a popup) — only when not in popup mode **and** Role is Admin,
  SalesAdmin, MD, GM, Logistics, CustomerSupport, CustomerSuccessExecutive, Head, CreditGM or CreditManager.

The table is the block **CompanyList_Table**, given all filters and `IsPopUp`. The block's own aggregate
`GetCompanyGroupNamesByGroupName` is disabled.

### 3.3 CompanyList_Table (block)

**Query `GetCompaniesByCompanyGroupNameId`**: Company left-joined to CompanyTerritory → Territory, CompanyAddressLink →
Address, `ExpireMachine` (on `ExpireMachine.BRN = Company.BRN`) and the creating User; grouped by Company name, BRN,
Id and creator name (one row per company); sorted by company name ascending. Conditions:

- `Company.IsActive = True` (always);
- territory visibility, unless privileged: `Territory.IsActive`, `CompanyTerritory.IsActive`, and
  `Session.UserList` contains `"|" + Territory.OwnerID + "|"`;
- `Company.Company LIKE %CompanyName%`; `Company.BRN LIKE %BRN%`; `Address.PostalCode LIKE %PostalCode%` (any linked
  address, active or not);
- when `ContractStart` is set: `ExpireMachine.ContractStart >= ContractStart`; when `ContractEnd` is set:
  `ExpireMachine.ContractEnd <= ContractEnd` (only companies having a machine contract in `ExpireMachine`, which stays
  in MyCRM);
- when `CompanyGroupId` is set: `Company.CompanyGroupId = CompanyGroupId`; when `GroupNameId` is set:
  `Company.CompanyGroupNameId = GroupNameId`.

Not certain: whether companies without any address appear when the postal-code filter is empty (`NULL LIKE '%%'`
depends on how OutSystems generates the SQL for a left join).

**Normal mode** (table `CompaniesTable`, 50 rows per page, counter and page navigation with 5 page links; empty
message "No companies to show..."). Columns:

| Header | Content |
|---|---|
| "Company" | the name (trimmed), link to **CompanyDetails**(CompanyId), tooltip "Edit '<name>'"; a comment icon opening **PopupInfoContacts_PopUp** in a 500 px info balloon on hover; the blocks `withMif` and `Debtor` (other parts) |
| "Postal Code" | block **GetPostalCodeByCompanyId** |
| "BRN" | `Company.BRN` |
| "Created By" | creator's user name |

**Popup mode** (`CompaniesTable_PopUp`, 50 rows, same paging): one column "Company"; clicking a name runs
`SelectCompany`: notifies the opener with the company Id and closes the popup.

`RefreshCompanyTable` / `RefreshCompanyPopUpTable` (page navigation): optionally clear
`Session.Companies_SearchKeyword` and reset paging, then refresh.

### 3.4 CompanyList_PopUp (screen)

Popup layout containing **CompanyList** with `IsPopUp = True`. It is the **company picker used by other parts of
MyCRM** (10 other UI flows link to it: activities, prospects, projects, CSST, reports, admin…). It returns the
chosen company Id through the popup notify.

### 3.5 CompanyGroupName_LazyLoad (block; inputs `GroupNameId`, `GroupNameId2`)

"Group Name" Select2 drop-down of `CompanyGroupName.GroupName`, listing only group names of companies whose
territory owner is in `Session.UserList`, or all of them when `ShowAllCompany(role)`; sorted by name. On change it
raises its `Notify` event with the chosen Id; CompanyList stores it in `GroupNameId`.

### 3.6 CompanyDetails (screen; input `CompanyId`) — the company page

**On load**

1. `SetRequestTimeout`.
2. `CompanyId` empty → go to **Companies**.
3. `UserCanAccessCompany(Role(CurrUser), Session.UserList, CompanyId)`; false → **InvalidPermissions** ("You don't
   have permissions to view the required screen. Please contact the system administrator.").
4. Reads the company (`GetCompanyById`), sets `AddressTypeId = Billing`, reads its `CompanyCaretake` rows, and writes a
   recent-item (`LogRecentItem`, other part) with the company name and URL.

**Header** (dark box; breadcrumb "Companies" › company name)

- Company name; **"Update"** button, only when `IsSalesAdmin(CurrUser)` → navigates to **CompanyDetail**(CompanyId)
  (3.7).
- Late-loaded lines: "BRN: …", "A/C Type: <CompAccountType label or '-'>", "Last Updated Date: …",
  "Last Updated By: <user name>", "Customer Code: <GetAdvanceCustNoByCompanyId>", the block **companyOwnerCaretake**,
  "Created Date: …", "Created By: …", "Customer Type: <CompanyGroup label or '-'>" (`GetCommercialType`),
  "Company Group: <CompanyGroupName(CompanyId)>", "MSD Sales Rep: <GetMSDCompanyOwner>" (module MyCRMMSD).
- Block **BillingAddresses**, block **NoOfEmployees**, "Industry: <CompanyIndustry label or '-'>".
- Block **SalesRemarks**.
- Expandable section **"More Company Details"** (collapsed): "Machines sold by dealer" (block `DealerSoldMachines`
  by BRN, other part), "Remarks" (`Company.Remarks`), "Caretake Remarks" (list of
  `CompanyCaretake.CaretakeAccountRemarks`, 50 rows).

**Tabs** ("Details", "Quick Look ( Prospects and Activities )", "Competitors", "MyMachines", "Purchase History")

- **Details**: a refresh icon (re-renders the contacts), block **Address**, block **Contacts** (both with
  `showEdit = Role ≠ Marketing`).
  - **CSST section** (only Admin, CSST, CSSTManager): tabs "Special Instruction", "Toner Collection", "Quote",
    "Supply History / Order" with the blocks `CSSTSpecialInstruction`, `CSSTTonerCollection`, `CSSTQuotes`,
    `CSSTOrder` (other parts; given CompanyId). Their notify handlers only set `Session.ActiveCSSTTab` and refresh.
- **Quick Look**: note "Company Activities and Prospects created from April 1, 2015"; panels with
  `CompanyActivity_List` (CompanyId, IsUpcoming = False) and `GetProspects_List` (other parts).
- **Competitors**: `CompanyCompetitorsMIFs` (MIF, other part).
- **MyMachines**: `MyMachinesIFrame` (other part).
- **Purchase History**: `MachinesByCompany` (other part).
- Below the tabs: `GetProjectByCompany` (only Manager, Admin, SalesAdmin, MD), `Document` (documents, other part),
  block **CompanyRelationships**, `TerritoryMovement` (hidden from Sales and Telemarketer; Territory part).

All other-part blocks take only `CompanyId` (the dealer block takes `Company.BRN`).

### 3.7 CompanyDetail (screen in the Admin flow; input `CompanyId`) — the company edit form

Not in the original list but it is **the only screen that edits a company's main fields** and the only writer of
Company `Pre…`. Reached from CompanyDetails "Update" and from the admin list **Company** ("Create a new Company",
or a company name). Role CRMUser; on load: not `IsSalesAdmin(CurrUser)` → **InvalidPermissions**. Sets
`IsMandatory = True`, `IsHomeUser = False`. Title "New Company" or the company name.

Form `CompanyForm` (fields in order):

| Label | Field | Rules |
|---|---|---|
| "Is Active" | `IsActive` | |
| "Is HOME USER" | screen variable `IsHomeUser` (not stored) | ticking it makes BRN optional (`IsMandatory = False`) |
| "Company Name" | `Company` (250) | mandatory |
| "BRN" | `BRN` (50) | mandatory unless "Is HOME USER" |
| "Website" | `Website` | |
| "Is Created By MSD" | `IsCreatedByMSD` | not certain: appears read-only |
| "Is Channel Partner" | `IsChannelPartner` | |
| "Customer Type" | `CompanyGroupId`, drop-down `CompanyGroup.Label`, "-" | |
| "Advance Customer Type" | `AdvanceCustomerTypeId`, drop-down `AdvanceCustomerType.Label`, "-" | |
| "Company Group Name" | `CompanyGroupNameId`, Select2 of `CompanyGroupName.GroupName`, "-"; link "Add Company Group Name" (or edit when one is chosen) opening **CompanyGroupNameDetail** in a popup | |
| "Account Type" | `CompAccountTypeId` | |
| "Comp Ownership" | `CompOwnershipId` | |
| "Created By", "Created Date", "Updated By", "Updated Date" | | read-only |
| "Remarks" | `Remarks` (10000) | |

"Cancel" → admin list **Company**. **"Save"**:

1. Form invalid → focus the first invalid field.
2. **New company** (`CompanyId` empty): `CreatedBy = CurrUser`, `CreatedDate = now`.
   - Not a home user: look for companies with `Trim(BRN) = Trim(form BRN)`; one exists → message **"Company is
     exists"**, nothing saved.
   - Home user: the BRN check is skipped (the flow tests an aggregate it never ran, which is empty).
3. **Existing company**: re-read the row, `UpdatedBy = CurrUser`, `UpdatedDate = now`, copy all 19 old values into
   `Pre…` (section 2.1). **No duplicate-BRN check on edit.**
4. `CreateOrUpdate Company`; message "Company '<name>' was successfully created." / "… updated.".
5. `GetCompanyMultipleOwners(CompanyId)`: when the company has an owner (an active, non-temporary territory) → go to
   **CompanyDetails**; otherwise → admin list **Company**.

Nothing else happens on save: no territory, contact or address change, even when `IsActive` is cleared.

The admin list **Company** (Admin flow, IsSalesAdmin only): search box "Company / BRN / Customer Type" (LIKE on
name, BRN, CompanyGroup label; kept in `Session.CompanyEdit_SearchKeyword`), "Search", "Reset", a "Chanel Partner "
check box that filters `IsChannelPartner = <checkbox>` (unticked shows only non-channel-partners), table of 50 with
columns "Company" (link to CompanyDetail), "BRN", "SALESPERSON" (block PrimarySalesperson), "Customer Type",
"Website", "Is Active", "Is Channel Partner"; inactive companies are included.

### 3.8 CompanyGroupNameDetail (popup, Admin flow; input `CompanyGroupNameId`)

Form "Group Name". "Save" (form valid) → CreateOrUpdate `CompanyGroupName`, message "Company Group Name '<name>' was
successfully created./updated.", notify opener with the Id, close. **"Delete"** (only for an existing one, confirm
"Are you sure you want to delete?") → **DeleteEntity CompanyGroupName**, message "Company Group Name '<name>' was
successfully deleted", notify "Delete", close. A group name still used by a company cannot be deleted (Protect rule;
the database error is shown).

### 3.9 companyOwnerCaretake (block; input `CompanyId`)

"Owner: <GetCompanyOwner(CompanyId)>". Then, when the company is in an active, not-closed project whose active members
have project role `SalespersonCaretaker` or `SalespersonAccountOwner` (ProjectCompany active): "Caretaker:" and the
list of those members' full names (`GetFullNameById`), skipping a member whose name equals the owner's.

### 3.10 BillingAddresses (block; input `CompanyId`)

Heading "Billing Address"; lists the company's **active Billing** addresses (one line each, `AddressDetail(Id)`:
block, street, unit, building, postal code), up to 50; "No items to show...".

### 3.11 NoOfEmployees (block; input `CompanyId`)

"No of Employees: <value>" and an edit icon (anyone who can open the company). Edit → an input (max 50) with "Save"
and "Cancel". "Save" → `UpdateEntity Company` with the whole record as read on load (only `NoOfEmployees` changed;
`UpdatedBy/UpdatedDate` are **not** set); "Cancel" → back to display.

### 3.12 SalesRemarks (block; input `CompanyId`)

Form with "Sales Remarks" (text, max 50000) and a small "Save" button (anyone who can open the company). Save →
`UpdateEntity Company` with the whole record as read on load (so it can overwrite a concurrent change), message
**"Remarks was saved successfully"**. `UpdatedBy/UpdatedDate` are not set.

### 3.13 Address (block; inputs `CompanyId`, `showEdit`)

Panel "Address" with an address-type filter drop-down (`AddressType`, first option "All"; on change refreshes the
lists). Legend "* B - Billing |  D - Delivery | S - Storage ".

- First list (`GetDefaultCompanyAddressesById`): the company's **active default** addresses (filtered by the chosen
  type when one is chosen). On load the filter is set to Billing (`SelectedAddressTypeId`).
- "More" (collapsible, closed): the other active, non-default addresses.

Each row: block **CurrentAddressType** (a type drop-down, see below), "Default Address" badge when `IsDefault`, the
address text `AddressDetail(Id)`. When `showEdit` (Role ≠ Marketing): a pencil opening **AddressDetail_PopUp**
("Company Address") with `CompanyId`, `AddressId`, `CompanyAddressLinkId = AddressLinkId(CompanyId, AddressId)`, and a
red cross **Delete** with confirm "Are you sure you want to delete the address?". "New Address" (shown when Role ≠
Marketing) opens AddressDetail_PopUp with no address.

**Delete**: reads the CompanyAddressLink rows of that address, **DeleteEntity CompanyAddressLink** (only the first
row found), then SQL `DeleteAddress`: `UPDATE Address SET IsActive = 0, PreIsActive = 1, UpdatedAt = now,
UpdatedBy = CurrUser WHERE Id = @AddressId`; message "Address has been deleted."; refresh.

**CurrentAddressType** (block; inputs `Type`, `AddressId`): drop-down of `AddressType`; on change it re-reads the
active address and saves the new `AddressTypeID` straight away (`UpdateEntity Address`; no `UpdatedAt/By`, no
`Pre…`). It is shown even when `showEdit` is false (not certain whether it is disabled for Marketing).

### 3.14 AddressDetail_PopUp (popup; inputs `CompanyId`, `AddressId`, `CompanyAddressLinkId`)

On load: reads the active address and the link; for an existing address also calls `GetAddressByPostalCode` (Google)
and keeps the formatted address in a screen variable (not shown).

Form: "Is Default" (check box), "Address Type" (drop-down, **mandatory**), "Postal Code" (max 50; on change →
`AddressChange`), "Unit Number", "Block" (**mandatory**), "Street" (**mandatory**), "Building Name", "City",
"State / Province", "Country" (default "Singapore"). Legend "* B - Billing |  D - Delivery | S - Storage ".

- **AddressChange** (postal code changed): `GetAddressByPostalCode(PostalCode)` → fills **Block** and **Street**
  from Google's answer.
- **Save**:
  1. "Is Default" ticked and `HasDefaultAddress(CompanyId)` → "Is Default" invalid with **"There is already a
     default address for this company"**. This also happens when the address being edited **is** the default one
     (the check does not exclude it), so a default address cannot be re-saved with "Is Default" ticked.
  2. Form invalid → focus first invalid field.
  3. New: `CreatedAt/By`. Existing: `UpdatedAt/By` and `Pre…` (section 2.1).
  4. `CreateOrUpdate Address`, then `CreateOrUpdate CompanyAddressLink` (`Id = CompanyAddressLinkId`, `CompanyID`,
     `AddressID`, `CRMAddressLink` kept).
  5. Audit log (`Log`, module Audits): "<user full name> updated Addresss <block> <street> <unit><building>
     <postal code> for Company <name> on <date time>."
  6. Notify "Refresh", close. (The old-CRM push is disabled, 2.3.)
- "Cancel" closes.

### 3.15 Contacts (block; inputs `CompanyId`, `showEdit`)

Panel "Contacts"; "Create Contact " (shown when Role ≠ Marketing) opens **ContactDetail_Popup** ("Contact") with no
contact. Accordion of the company's **active** contacts that have a first or last name, **default contact first**
(up to 50000). Title: `ContactDisplaytName` + ", (Primary Contact)" when `IsDefault` + ", <Department>" when set;
"Is Eq. List" tick when `IsEqList`; pencil (when `showEdit`) opens ContactDetail_Popup ("Edit Contact"). Content:
"Designation: …", block **GetPhoneByContactByCompany**, block **GetEmailByContactId**. Under the panel the block
`MyCustomerEmails` ("Invoice / SOA Email": Advance `CustomerMapping` (Company_ID = this company) joined to
`Email_Master`: Contract_Type, ContractNo, EmailAddress, CCEmail).

### 3.16 ContactDetail_Popup (popup, flow "ContactDetail"; inputs `CompanyId`, `ContactId`) — the contact editor in use

Permissions: Admin, MyOrdersUser, CRMUser and a system role (probably Registered).

On load: the contact, its CompanyContactLink for this company, its **active emails for this company**
(`Email.CompanyID = CompanyId and ContactID = ContactId`) and its **active phones** (`ContactID = ContactId`, any
company), copied into editable lists.

Form: "Salutation", "First Name", "Christian Name" (MiddleName), "Last Name / Surname" (**mandatory**), "Department"
(50), "Designation", "Is Sales Navigator" (IsLinkedIn), "Is Eq. List" (enabled only when the contact has at least
one email), "Is Default" (CompanyContactLink).

"Email" table (500 rows): columns "Email" and "Consent To Marketing Infomation"; "Add Email" inserts a row in edit
mode (consent ticked by default); per row: pencil (edit), floppy (save row), cross (cancel), red bin (remove,
confirm "Confirm delete?"). Saving a row: empty or invalid email (`Email_Validate`) → "Invalid Email".

"Phone" table: columns "Phone" (20) and "Phone Type" (drop-down `PhoneType`, "-"); "Add Phone"; same row icons.
Saving a row with an empty number → message on the phone field **"Invalid Email"** (sic).

Row edits only change the screen lists; nothing is stored until **Save**:

1. Form invalid → focus first invalid field.
2. New contact: `CreatedAt/By`. Existing: `UpdatedAt/By` and `Pre…` from the values read on load.
3. `CreateOrUpdate Contacts`; audit log "<user> updated Contact <first> <last> for Company <name> on <date time>.".
4. CompanyContactLink: `CompanyID = CompanyId`, `ContactID`; when "Is Default" → `RemoveOtherDefaultContact(CompanyId)`
   first (clears `IsDefault` on all the company's default links); `CreateOrUpdate CompanyContactLink`.
5. Emails: each email read on load that is no longer in the list → `IsActive = False`, `UpdatedAt/By`, saved. Each
   row in the list → `CompanyID = CompanyId`, `ContactID`, `CreatedAt/By` (new) or `UpdatedAt/By`, saved.
6. Phones: same as emails.
7. Message "Contact was successfully created." / "… updated."; notify opener with the contact Id; close.

**"Delete Contact"** (only for an existing contact, confirm "Are you sure you want to delete the Contact?"):
`IsActive = False`, `PreIsActive = True`, `UpdatedAt/By`, saved; audit "<user>deleted Contact <first> <last> for
Company <name> on <date time>."; message "Contact was deleted."; notify, close. The link, phones and emails are left
as they are.

"Cancel": notify with the contact Id, close.

### 3.17 ContactdDetail_PopupNew_CanDelete (popup, flow "ContactDetail") — not used

Nothing links to it. A newer version of the contact editor: phones and emails are saved row by row
(`OnPhoneTableRowSave` / `OnEmailTableRowSave` with `Pre…` and audit "… updated Email …"; row delete sets
`IsActive = False`, `PreIsActive = True` with audit "… deleted Phone/Email …"); `SaveContact` refuses a second
default with **"You can only have 1 default contact."** (via `CheckIfDefaultCompanyContactExist`), shows "Contact has
been successfully created.", and holds the disabled old-CRM push. Documented only so the rules are not lost.

### 3.18 EmailDetail_PopUp (popup) — not used

Nothing links to it. Form "Email", "Is Active" (plus hidden "Company", "Contact", "CID"); Save sets `CompanyID`,
`ContactID` from the inputs, `CreateOrUpdate Email`, message "Email '<email>' was successfully created./updated.".

### 3.19 PopupInfoContacts_PopUp (info balloon; input `CompanyId`)

Opened from the company list. Lists the company's active contacts that have a name **and at least one phone**
("Name: <first> <last>", up to 50) with block GetPhoneByContactByCompany. (It also computes
`GetContactIdByCompanyId`, unused.)

### 3.20 GetPhoneByContactByCompany / GetEmailByContactId / GetPostalCodeByCompanyId (blocks)

- **GetPhoneByContactByCompany**(ContactId): active, non-empty phones of the contact: "<PhoneType label> : <number>".
- **GetEmailByContactId**(ContactId): active emails of the contact (any company), sorted by email: "Email: <email>"
  and, when `ConsentToMarketingInfo`, a green tick "Consent To Receive Marketing Infomation ".
- **GetPostalCodeByCompanyId**(CompanyId): the postal code of the **first** address linked to the company, active
  or not, default or not.

### 3.21 CompanyRelationships (block; input `CompanyId`)

Panel "Company Relationships". Queries: parent (`RelatedCompanyID = CompanyId`, active), children
(`CompanyID = CompanyId`, active), siblings (children of the parent other than this company).

Buttons (only when Role ≠ Marketing), each opening **CompanyRelationshipsAdd_PopUp** (the company picker):

- **"Add Parent"** (only when there is no parent) → creates `CompanyRelationship(CompanyID = chosen,
  RelatedCompanyID = this, type Parent)`.
- **"Add Sibling"** (only when there is a parent) → if the chosen company already has a parent
  (`CheckParentExists`) → **"This company has a parent company exists( 1 company can only have 1 parent)"**; else
  creates `(CompanyID = this company's parent, RelatedCompanyID = chosen, Parent)`.
- **"Add Subsidiary"** → same check on the chosen company, same message; else creates `(CompanyID = this,
  RelatedCompanyID = chosen, Parent)`.

Sections "Parent Company" (name links to its CompanyDetails; "No parent company exists"), "Current / Sibling
companies" ("No sibling company exists"), "Subsidiary companies". Each related company has a red cross with confirm
"Are you sure you want to delete?" → **DeleteEntity CompanyRelationship** (RemoveParentCompany, RemoveSiblingCompany,
RemoveChildCompany). Nothing prevents a company from being its own parent or cycles.

### 3.22 CompanyRelationshipsAdd_PopUp (popup)

Popup layout with **CompanyList** (`IsPopUp = True`); the chosen Id is returned to CompanyRelationships.

### 3.23 UploadNoOfEmployees (block, BackOffice flow)

Not in the original list; it writes Company. Reads an uploaded Excel (BRN, NoOfEmployees); for each row takes the
first active company with that BRN and sets `NoOfEmployees` (`UpdateEntity`, no `UpdatedBy`); counts successes.

---

## 4. Server actions

| Action | What it does |
|---|---|
| **ValidateCompany**(TempCompany, ToUserId) → CompanyDescription | Used by TerritoryUpdate's import. Trims the uploaded `TempCompanies.Company` and `.BRN`. Both empty, or no active company with `Trim(Company) = name` **or** `Trim(BRN) = BRN` → Error, Note "Company could not be found.". The company already has an active CompanyTerritory of an active, non-temporary territory owned by `ToUserId` → Error, "Company is already assigned.". Otherwise returns the CompanyId and `OwnerId = GetCompanyOwnerUserId(CompanyId)`. |
| **CompanyHasRelationship**(CompanyId) | True when an active CompanyRelationship has the company as `CompanyID` or `RelatedCompanyID`. Not called by any flow. |
| **CheckParentExists**(CompanyId) | True when an active CompanyRelationship has `RelatedCompanyID = CompanyId`. |
| **CheckIfDefaultCompanyContactExist**(CompanyId, ContactId) → HasDefaultContact | Counts active contacts with a default link to the company; when exactly one exists, returns True if it is `ContactId`, else False; any other count → False. Only used by the unused popup (3.17). |
| **RemoveOtherDefaultContact**(CompanyId) | Sets `IsDefault = False` on every default CompanyContactLink of the company. |
| **HasDefaultAddress**(CompanyId) → HasDefault | True when the company has an active address with `IsDefault`. |
| **GetCompanyAddressId**(CompanyId) → AddressId | The company's active default address; if none, its first active address (joined to AddressType). Used by two CSST blocks. |
| **AddressDetail**(AddressId) | Block + Street + UnitNumber + BuildingName + PostalCode, each trimmed, joined with separators. **AddressDetailCountry** adds Country before the postal code. |
| **CompleteAddress**(AddressId) | Block, Street, UnitNumber, BuildingName (only non-empty parts, trimmed, joined). Used by CSST documents. |
| **CountryPostalCode**(AddressId) | Empty when no address; else Country (or "Singapore" when Country is empty — not certain which way round) followed by PostalCode. |
| **GetAddressByCompanyId**(CompanyId) | Display text (Block, Street, Unit, Building, …) of the company's default address. Used by a CSST download. |
| **GetAddressByPostalCode**(PostalCode) → Block, Street, Address, PostalCode | Calls the REST client **MapsGoogleapis** (Google Geocoding) with the postal code; when status "OK" with results, reverse-geocodes by latitude/longitude and takes `street_number` → Block (long name), `route` → Street (short name), formatted address → Address. |
| **AddressLinkId**(CompanyId, AddressId) | The CompanyAddressLink Id. **AddressTypeByAddrId**(AddressId): the address's AddressTypeID. |
| **CompleteContact**(ContactId) | Salutation, FirstName, MiddleName, LastName (non-empty, trimmed, joined). |
| **ContactName**(ContactId) | First + Last + Middle name (trimmed) of the active contact having a company link. Used by CSST documents and activities. |
| **ContactDisplaytName**(ContactId) | Salutation, FirstName, MiddleName, LastName — non-empty parts, trimmed, joined with spaces (`String_Join`). |
| **GetContactByContactId**(ContactId) → ContactName | First + last name of the active contact. |
| **GetContactIdByCompanyId**(CompanyId) | Id of the first active, named contact of the company. |
| **GetPhoneNumber**(PhoneId) | The number of an active phone. **GetPhoneTypeName**(PhoneTypeId): the active PhoneType label (not called). |
| **GetCompanyName**(CompanyID) | Name of an **active** company (empty for inactive). **GetCompanyNameById**(CompanyId): trimmed name, active or not (used widely by other parts and the audit messages). |
| **GetCompanyID**(CompanyID) | MyCRM Id of the company whose **`CRMCompanyId`** equals the parameter (old-CRM → MyCRM). Used by SetTerritory. |
| **CompanyId**(CRMCompanyId) | Same mapping (first match). **ContactId**(CRMContactId): Contacts with `CId` = parameter (non-zero). **AddressId**(CRMAddressId): Address with `CRMAddressId` = parameter. |
| **CompanyIdByName**(CRMCompanyName) | First company with exactly that name (active or not). **getCompanyIdByName**(compName): first **active** company with that name. |
| **GetCompanyAccountType**(CompanyId) | NonUser when the latest `ExpireMachine` of its BRN ended before today, User otherwise. Not called. |
| **GetAdvanceCustNoByCompanyId**(CompanyId) → CustNo | `Company.CustomerNo` when not empty; else the first Advance `customer` whose name equals the company name, or whose `Customer_Supp` BRN equals the company's non-empty BRN → its `Cust_no`. Shown as "Customer Code" and used by the Debtor block. |
| **CompanyGroupName**(CompanyId) | Group name of an active company. **GetCommercialType**(CompanyGroupId): the active CompanyGroup label. |
| **GetCompanyOwner**(CompanyId) → UserName | User name of the owner of the first active CompanyTerritory of an active, non-temporary territory (company active), ordered by the user's active flag and last login. **GetCompanyOwnerUserId** returns that user's Id; **GetCompanyMultipleOwners** returns "\|id\|" for the first one only. |
| **UserId**(CRMUserId) | Maps an old-CRM user to an OutSystems user: `Users 2.User_Logon` of that Id, then the first active user whose Email contains that logon; else null. |
| **DisableMembersWithCompDel**(ProjectId, CompanyId) | Called by ProjectDetails_PopUp / ProjectDetails_PopUp2 Save when a company leaves a project: for each member of the project, sets `IsActive = False` on the company's CompanyTerritory rows whose TerritoryID is that member's territory. This is the only thing in MyCRM that reacts to a company being "removed", and it concerns a project, not the company itself. |
| **GetOldCRMCompanyId**, **UpdateCustomerNo**, **UpdateCompanyCRMIdByBRN/ByName**, **UpdateOldCRMCompanyId**, **UpdateAdvanceTypeCode**, **BulkUpdateCustomerType**, **ChannelPartnerCompanies**, **MissingCompany**, the Import… actions | Section 5. |

Making a company inactive (CompanyDetail, or an import when the old CRM deleted it) changes nothing else: its
territories, contacts, addresses and relationships stay; it just disappears from lists and `UserCanAccessCompany`
refuses it.

---

## 5. Import timers and batch actions

**Schedules**: in the model only `UpdateCustomerNo` (daily 02:45) and `UpdateOldCRMCompanyId` (Sundays 05:00) have a
schedule. Every other timer below has **no schedule** and no flow wakes it, so it runs only when started by hand in
Service Center (or if Service Center holds an effective schedule, which the model does not show). "5 days" means
`CreatedDate` or `UpdatedDate` within the last 5 days.

All imports read the old CRM through AdvanceConnector and use `CreateOrUpdate`: when no MyCRM row has the old-CRM Id,
an empty record is filled and **created**; otherwise the row is **overwritten** (only the listed fields). None sets
`Pre…`.

| Timer (timeout) | Action | Source | Writes |
|---|---|---|---|
| Company (90 min) | **Company** | **all** `Company 2` rows, by name | Company by `CRMCompanyId = Comp_CompanyId`: `Company = Trim(Comp_Name)`, `BRN = comp_brn`, `Website = Comp_WebSite`, `IsActive = (Comp_Deleted is null)`, `CRMCompanyId`, `CreatedDate`, `UpdatedDate`, `CompAccountTypeId` ("User"/"Non-User" from `comp_accounttype`, else none), `CompOwnershipId` ("Owned"/"Declared" from `comp_declared`), `IsCaretake = (comp_caretake = 'Y')`, `UpdatedBy`/`CreatedBy = UserId(Comp_UpdatedBy/CreatedBy)`, `Remarks = comp_remarks`. When `comp_caretake = 'Y'`: the company's (first) CompanyCaretake ← `comp_caretake_account_remarks`, `UpdatedDate = Comp_TimeStamp`, `CreatedBy`, `UpdatedBy`, `DateCreated`. |
| CompanyDaily (90) | **CompanyDaily** | `Company 2` changed in the last 5 days | same as Company |
| ImportChannelPartnerCompanies (90) | **ChannelPartnerCompanies** | `Users 2` with `User_TerritoryProfile = 25` → `Company 2` whose `Comp_PrimaryUserId` is one of them | same as Company plus `IsChannelPartner = True` |
| (none; AdminPage "Missing" button) | **MissingCompany**(CRMCompanyId) | that `Company 2` row (not deleted) | same as Company but `IsActive = True` |
| Contact (90) | **Contact** | `Person` changed in 5 days | Contacts by `CId = Pers_PersonId`: FirstName, LastName, `IsActive = (Pers_Deleted is null)`, Department, CId, MiddleName, Salutation |
| RUN_ImportContacts (20) | **ImportUpdateContact** | `Person` not deleted, changed in the last **360** days | Contacts by CId: FirstName, LastName, `IsActive = True`, Department, CId, MiddleName |
| ContactLink (90) | **ContactLink** | `Person_Link` not deleted, changed in 5 days | CompanyContactLink by `CId = PeLi_PersonLinkId`: `CompanyID = CompanyId(PeLi_CompanyID)`, `ContactID = ContactId(PeLi_PersonId)`, CId |
| RUN_ImportContactLink (20) | **ImportUpdateContactLink** | **all** `Person_Link` not deleted | Only when a link already exists for the person (first CompanyContactLink of a contact with `CId = PeLi_PersonId`, whatever its company): rewrites CompanyID, ContactID, CId, and sets `IsDefault = True` when the person is the company's `Comp_PrimaryPersonId` in `Company 2`. Never sets `IsDefault = False`. Not certain which of the two branches sets IsDefault. |
| Address (90) | **Address** | `Address 2` created or updated since 2015-04-01 | Address by `CRMAddressId = Addr_AddressId`: `UnitNumber = Addr_Address2`, `Street = Addr_Address1`, `BuildingName = Addr_Address3` (each cut to 10000 characters, more than the 2000 the columns hold), `PostalCode = Addr_PostCode`, CRMAddressId, `IsActive`. **Block and AddressType are not set** (new rows get an empty Block). |
| CompanyAddressLink (360) | **CompanyAddressLink** | `Address_Link` changed in 5 days | CompanyAddressLink by `CRMAddressLink = AdLi_AddressLinkId`: `CompanyID = CompanyId(AdLi_CompanyID)`, `AddressID = AddressId(AdLi_AddressId)`, CRMAddressLink |
| Phone (90) | **Phone** | `Phone 2` joined to `PhoneLink`, changed in 5 days | Phone by `CRMPhoneId = Phon_PhoneId`: `ContactID = ContactId(PLink_RecordID)`, `Phone = Phon_Number`, CRMPhoneId, `IsActive`. No PhoneType, no CompanyID. |
| Email (20) | **ImportEmail** | `EmailLink` changed in 5 days | Skips the link when no contact has `CId = ELink_RecordID` (with a company link); else Email by `CID = ELink_EmailId`: `Email = Trim(Emai_EmailAddress)` (cut to 200), `CompanyID` = that contact's link company, `ContactID`, `IsActive = (Emai_Deleted is null)`, CID |
| UpdateCompanyCRMIdByBRN (20) | **UpdateCompanyCRMIdByBRN** | `Company 2` not deleted | For the first MyCRM company with `BRN = comp_brn`, BRN longer than 5 characters and no `CRMCompanyId`: sets `CRMCompanyId = Comp_CompanyId` |
| UpdateCompanyCRMIdByName (20) | **UpdateCompanyCRMIdByName** | `Company 2` not deleted | Same, matching `Company = Comp_Name` |
| UpdateOldCRMCompanyId (Sun 05:00) | **UpdateOldCRMCompanyId** | active companies with `CRMCompanyId` 0 or null | First `Company 2` (not deleted) with `Trim(Comp_Name) = Trim(Company)`, or same trimmed BRN when `comp_brn` is longer than 5 → `CRMCompanyId = Comp_CompanyId`. Saves every company, setting 0 when nothing matches. |
| UpdateCustomerNo (02:45 daily) | **UpdateCustomerNo** | active companies with BRN longer than 3 characters | Advance `Customer_Supp` rows with `Business_reg = BRN`; when found: `CustomerNo` = the matched row's customer number (the decoder shows the attribute as a second "Business_reg"; it is the one joined to `customer.Cust_no` elsewhere) + " #Multiple" when more than one row matches; `UpdateEntity` (no `UpdatedBy`) |
| F4eRswZidU6csBBKNbChIA (60) | **UpdateAdvanceTypeCode** | up to 5000 active companies without `AdvanceCustomerTypeId` | Advance `CustomerMapping` (`Company_ID = Company.Id`) joined to `customer` with a non-empty `Type_code` → the active AdvanceCustomerType whose `TypeCode` equals it (case-insensitive) → sets `AdvanceCustomerTypeId` |
| BulkUpdateCustomerType (20) | **BulkUpdateCustomerType** | an Excel file **embedded in the module** (columns Company, BRN, CustomerType) | For each row: the active company with exactly that name **and** BRN whose CompanyGroup label differs (or is empty) → `CompanyGroupId` = the active CompanyGroup with that label |
| "import 1st" EcS9jDrLeUuYtY5AHxG5pQ | **ImportLG** | Advance `LMA_Rank` joined to `FG500_Rank` | creates a CompanyGroupName for each `LMA_Rank.Group_Name` not yet present |
| "import 2nd" | **ImportGMA** | `FG500_Rank` | creates a CompanyGroupName for each `Grp_Name` not yet present |
| "import 3rd" | **ImportLMA** | `LMA_Rank` | creates a CompanyGroupName for each `Group_Name` not yet present |
| "import 4rth" UpdateCompany_GMA_ | **ImportGMAUpdateCompany** | `FG500_Rank` joined to `Customer_Supp` (`NE6 = Business_reg`) | for companies with `BRN = Business_reg` or `Company = NE6`: SQL `UPDATE Company SET CompanyGroupNameId = <group of Grp_Name>, CompanyGroupId = GMA WHERE Id = @CompanyId` |
| "import 5th" UpdateCompany_LMA_ | **ImportLMAUpdateCompany** | `LMA_Rank` joined to `Customer_Supp` | same with the LMA group name and `CompanyGroupId = LMA` |
| "import 6th" UpdateCompany_LG_ | **ImportLGUpdateCompany** | `FG500_Rank` ⋈ `LMA_Rank` ⋈ `Customer_Supp` | same with `CompanyGroupId = LG` |
| "import 7th" UpdateCompany_SME_ | **ImportSME** | **all** companies | every company whose group is not GMA, LMA or LG → SQL `UPDATE Company SET CompanyGroupId = SME, CompanyGroupNameId = <unchanged>` (this would overwrite Dealer, Govt, JKA…) |

Bug worth knowing in the 4th–7th imports: the `@CompanyId` they update is `CompanyId(CRMCompanyId = <MyCRM Company.Id>)`
(ImportSME passes `Company.CRMCompanyId`, correctly), i.e. they look up the company whose *old-CRM* Id equals the
MyCRM Id, so they update the wrong company or none. The LG, LMA and CA groups are inactive, which suggests these
imports are no longer used.

---

## 6. What MyCRM deletes, and what it only marks inactive

**Deletes (DeleteEntity) in this part**

| Entity | Where | When |
|---|---|---|
| `CompanyRelationship` | CompanyRelationships block: RemoveParentCompany, RemoveSiblingCompany, RemoveChildCompany | a user clicks the red cross and confirms |
| `CompanyAddressLink` | Address block → Delete | a user deletes an address (the first link row of that address only) |
| `CompanyGroupName` | CompanyGroupNameDetail → Delete | an admin deletes a group name (fails when a company uses it) |

(`TempCompanies` rows are deleted by the Territory part.) **Company, Address, Contacts, CompanyContactLink, Phone,
Email and CompanyCaretake are never deleted.**

**Only marked inactive**

- Address: `IsActive = 0`, `PreIsActive = 1` (Address block delete); imports set `IsActive` from the old CRM.
- Contacts: `IsActive = False`, `PreIsActive = True` ("Delete Contact"); imports set `IsActive`.
- Phone, Email: `IsActive = False` when removed in ContactDetail_Popup and saved.
- Company: `IsActive` ticked off in CompanyDetail, or set from `Comp_Deleted` by the imports.
- CompanyTerritory: `IsActive = False` by DisableMembersWithCompDel (project member removal).

---

## 7. What other parts of MyCRM depend on

- **Ids**: activities, prospects, projects, MIF, documents, CSST and training events point at `Company.Id`,
  `Contacts.Id` and `Address.Id` (section 2 of README); Advance's `CustomerMapping.Company_ID` also holds MyCRM
  `Company.Id` (used by `MyCustomerEmails` and `UpdateAdvanceTypeCode`), so Ids must never change.
- **`Company.IsActive`**: `UserCanAccessCompany`, the company search and `GetCompanyName` hide inactive companies.
- **`Company.PreCompanyName`** (and `Contacts.PreFirstName`): sort keys of several lists that stay in MyCRM (2.1).
- **CompanyList_PopUp**: the company picker of 10 other UI flows; it returns the company Id through the popup
  notify.
- **Mapping by old-CRM Id**: `CompanyId()`, `ContactId()`, `GetCompanyID()` (on `CRMCompanyId`, `Contacts.CId`) are
  used by the prospect, activity, CSST and territory-code imports (MissingActivity, MissingProspect, NewUser_*,
  Range_Communication_Import, ImportUpdateProspect, Prospect2_New, Import_CSST*, TerritoryCode, SetTerritory).
- **Text helpers** used by CSST templates and activities: `ContactName`, `GetPhoneNumber`, `CompleteAddress`,
  `CountryPostalCode`, `GetCompanyAddressId`, `GetAddressByCompanyId`, `GetCompanyNameById`, `GetCompanyName`.
- **`CompanyContactLink.IsDefault`** ("Primary Contact") and **`Address.IsDefault`** (default address for CSST
  documents).
- **`Company.BRN`**: the company list joins `ExpireMachine` and the dealer block on it; `Company.CustomerNo` feeds
  "Customer Code" and the Debtor block.
- **CompanyTerritory / Territory** (Territory part) decide who sees a company in the list and on CompanyDetails.
- MyCRMMSD's `GetMSDCompanyOwner` and the Audits module's `Log` are called from these screens.

---

## 8. UI that could be friendlier

- The search filters by "Customer Type" but the stored field is `CompanyGroupId`; "Company Group" means the group
  name. Two names for two concepts that look alike — label them consistently.
- The company-name box promises "Company, BRN or Created By" but searches the name only.
- No result list until "Search" is pressed; a first page of the user's companies would save a click.
- Company edit is split over three places (CompanyDetail for Sales Admins, NoOfEmployees and SalesRemarks inline for
  everyone) with different auditing; one edit form with the same rules would be clearer.
- Industry, phone, fax, paid-up capital, MIF potential, revenue and inc. cert. no. are shown or stored but cannot be
  edited anywhere.
- Saving the default address with "Is Default" ticked is refused ("There is already a default address for this
  company"); the check should ignore the address being edited.
- "Christian Name" for middle name and "Is Sales Navigator" for `IsLinkedIn` are unclear labels; the phone row error
  says "Invalid Email".
- The admin list's "Chanel Partner" box filters to *exactly* ticked/unticked, so there is no "all" view; its columns
  sort by the previous values.
- Relationships allow cycles and self-links; deleting a relationship deletes the row instead of deactivating it.

## 9. Open questions

1. The effective schedules of the import timers in Service Center (the model has none) — are any still running, and
   is the old CRM still alive?
2. Whether companies without an address show up in the company search when the postal-code filter is empty.
3. Which branch of ImportUpdateContactLink sets `IsDefault = True` (the decoded flow is ambiguous).
4. The name of the `Customer_Supp` attribute that UpdateCustomerNo copies into `CustomerNo` (decoded as a second
   "Business_reg").
5. The source module of `Company3` used by the (dead) GetOldCRMCompanyId.
6. Whether "Is Created By MSD" is read-only on CompanyDetail, and whether the address-type drop-down on each address
   row is disabled for Marketing.
7. The system role on ContactDetail_Popup (key `I1Fm79oZdUu8QBe+dLY9Cg`), probably Registered.
8. CountryPostalCode: whether "Singapore" is used when Country is empty or when it equals something else (the
   compared literal did not decode).
