# How MyCRM's territories work (who owns which company)

This is the "how the old app works" reference for the **Territory** part of MyCRM: how companies are assigned to
salespeople. It was read from MyCRM's decoded model (screens, actions, timers) on 2026-10-19. Names of screens, labels
and messages are quoted as MyCRM shows them. "Not certain" marks what the decoded model could not settle.

Abbreviations: **CT** = a `CompanyTerritory` row; **User** = the OutSystems user (`Users` module, shown in flows as
`xposeP`); **CUser** = MyCRM's own user row (role, team); **Role(...)** = MyCRM's `Role` static entity via `CUser.RoleId`.

## Contents

- [1. The model in one page](#1-the-model-in-one-page)
- [2. The data](#2-the-data)
- [3. How Advance is read and matched](#3-how-advance-is-read-and-matched)
- [4. Timers and the daily rebuild](#4-timers-and-the-daily-rebuild)
- [5. Server actions](#5-server-actions)
- [6. Screens and blocks](#6-screens-and-blocks)
- [7. Hand-made changes versus the timers](#7-hand-made-changes-versus-the-timers)
- [8. Why there are millions of rows](#8-why-there-are-millions-of-rows)
- [9. What MyCRM deletes, and what it only marks inactive](#9-what-mycrm-deletes-and-what-it-only-marks-inactive)
- [10. What other parts of MyCRM depend on](#10-what-other-parts-of-mycrm-depend-on)
- [11. UI that could be friendlier](#11-ui-that-could-be-friendlier)
- [12. Open questions](#12-open-questions)

## 1. The model in one page

- A **Territory row** is "user X holds territory number N". It is not a shared territory that several people join:
  every holder has their own row. `OwnerID` is the holder; `Territory` is a number (the Advance territory id for
  rows built from Advance, `0` for rows created by hand-made transfers, and the digits of user id + company id for
  temporary rows).
- A **CT row** puts one company into one Territory row, so effectively "user X has company C". A company can be in
  several users' territories at once (several active CTs); MyCRM never enforces one owner.
- **Permanent** territories (`IsTemporary = False`) are the salespeople's own accounts. They come from Advance
  (daily timer) and from hand-made assignments on three screens.
- **Temporary** territories (`IsTemporary = True`) give a project member (or CSST staff, or the telemarketer)
  access to the project's companies. They are created by the **Projects** part of MyCRM, which stays in OutSystems.
- **The "owner" of a company** (shown everywhere as Owner / Salesperson) is computed, not stored: the User of the
  first active CT of an active, permanent territory, preferring active users, then the most recent `Last_Login`
  (`GetCompanyOwner`, `GetCompanyOwnerUserId`).
- **Access**: `UserCanAccessCompany` lets a user open a company when they (or anyone in their `Session.UserList`)
  hold an active CT of an active territory for it, permanent **or temporary**; some roles see everything
  (`ShowAllCompany`).
- Every change of holder is logged in `TerritoryMovement` (From, To, by, at).
- `TerritoryCode` (table `TempTerritoryCode`) is a copy of each company's Advance territory caption and parent
  caption. Nothing in MyCRM reads it.
- `TerritoryRelationship`, `TerritoryRelationshipType` and `CompOwner` are not used by any flow, screen or timer
  (they appear only in the entity diagram).

## 2. The data

| Entity (table) | Attributes | Who writes it |
|---|---|---|
| `Territory` (`OSUSR_824_Territory`) | `Territory` (Long Integer), `OwnerID` (User), `IsActive` (default True), `IsTemporary` (default False), `IsOwner` | daily timer, hand-made transfers, project membership, TerritoryDetail |
| `CompanyTerritory` (`OSUSR_824_CompanyTerritory`) | `TerritoryID`, `CompanyID`, `IsActive` (default True), `CreatedBy` (User) | same |
| `TerritoryMovement` (`OSUSR_824_TerritoryMovement`) | `CompanyId`, `FromUserId` ("Previous Sales Rep"), `ToUserId` ("Current Sales Rep"), `UpdatedBy`, `UpdatedAt` | only the `TerritoryMovement` action (CreateEntity) |
| `TerritoryCode` (`OSUSR_824_TempTerritoryCode`) | `TerritoryCode` (text 60), `CompanyId`, `ParentTerritory` (text 60) | only the `TerritoryCode` timer |
| `TempCompanies` (`OSUSR_824_TempCompanies`) | `Company` (text 2500), `BRN` (text 500), `CompanyId`, `UpdatedBy` | TerritoryUpdate screen: a per-user scratch list for an Excel import |
| `TerritoryRelationship`, `TerritoryRelationshipType`, `CompOwner` | (see README) | nothing |

What each flag means in practice:

- `IsActive` (Territory): the row counts. Set False by hand (TerritoryDetail), by "delete user and territory", by the
  05:30 timer for ended projects' temporary rows, and when a project is deleted. A deactivated row is never
  reactivated by the daily timer: it creates a new row instead (section 7).
- `IsTemporary`: True only for project/CSST/telemarketer rows (`TempTerritoryHandler`). Permanent rows are the only
  ones used for "owner", for the TerritoryUpdate screen, for UserTerritory and for transfers.
- `IsOwner`: on rows from Advance, True when this user is the Advance company owner (`Company 2.comp_owner`) of the
  **last company the daily timer processed** for that user (the value is overwritten for every company in the loop,
  so on a permanent row it is effectively random). Hand-made permanent rows get True. Temporary rows get False. It is
  read only by the dashboard block `GetCompanyToVisitBy` (`IsOwner = True`) and as `IsOwner <> True` in
  `TempTerritoryHandler`/`TempTerritoryHandlerWhenDel`.
- `IsActive` (CT): the assignment counts. Every "remove" and "transfer" sets it False; nothing in the UI deletes CTs.

## 3. How Advance is read and matched

Advance is read through the AdvanceConnector entities `Users 2`, `Territories` and `Company 2`.

| Advance entity | Columns used | Where |
|---|---|---|
| `Users 2` | `User_UserId`, `User_Logon`, `User_PrimaryTerritory`, `User_TerritoryProfile`, `User_Disabled`, `User_Deleted` | `SetTerritory`, `SetTerritory_Update`, `UserId` |
| `Territories` | `Terr_TerritoryID`, `Terr_RangeEnd`, `Terr_Deleted`, `Terr_Caption`, `Terr_ParentID` | same, `TerritoryCode` |
| `Company 2` | `Comp_CompanyId`, `Comp_SecTerr`, `Comp_Deleted`, `comp_owner`, `Comp_Name`, `Comp_UpdatedDate`, `Comp_CreatedDate` | same |

Matching rules:

- **MyCRM user to Advance user**: `GetADByUserId(UserId)` takes the User's `Email` and removes `RSP\` and `rsp\`;
  the result must equal `Users 2.User_Logon`. Only Advance users with `User_Disabled` and `User_Deleted` empty count.
- **Advance user to MyCRM user** (`UserId(CRMUserId)`, used for `comp_owner`): the `Users 2` row with
  `User_UserId = CRMUserId`, then the first **active** User whose `Email` contains `Trim(User_Logon)`; none gives null.
- **Advance company to MyCRM company**: `Company.CRMCompanyId = Company 2.Comp_CompanyId` (`GetCompanyID`,
  `CompanyId`). Companies not yet imported into `Company` are skipped.
- **Which companies a user gets**: the user's `User_PrimaryTerritory` joined to `Territories.Terr_TerritoryID`
  (not deleted, not `-2147483640`, the root), and every `Company 2` whose `Comp_SecTerr` is between
  `Terr_TerritoryID` and `Terr_RangeEnd` (Advance's nested territory ranges: a manager's range contains the ranges of
  the territories below it). `Terr_RangeStart` is not used.
- The resulting MyCRM `Territory.Territory` is the user's `Terr_TerritoryID`.

## 4. Timers and the daily rebuild

Timers in the model that touch territories:

| Timer (name in MyCRM) | Runs action | Schedule in the model | Timeout (min) | What it does |
|---|---|---|---|---|
| `SCA_SalesManCompanyAssignmentDaily` | `SCA_SalesmanCompanyAssignment` | none | 90 | for every CUser with role Sales: `SetTerritory_Update` (the Advance rebuild) |
| `SCA_AssignGlobalCompany` | `SCA_AssignGlobalCompany` | none | 90 | meant to give every Sales user the test companies "MotherBear", "BrotherBear", "SisterBear"; does nothing (passes `Session.CurrUserId`, empty in a timer) |
| `AssignCompaniesToSales` | `AssignCompaniesToSales` | none | 20 | one-off load from a resource Excel, sheet "GA" (columns BRN, Email); see 5.4 |
| `TerritoryCode` | `TerritoryCode` | none | 20 | refreshes `TempTerritoryCode` for Advance companies changed in the last 5 days |
| `CSST_Temp` | `SetTempTerritory` | none | 20 | temporary territories for CSST project members who have none |
| `CSST_05_AssignAllCompany` | `AssignAlloROneCompToACSST` | none | 60 | CSST staff: companies by first letter, into their CSST project |
| `CSST_06_AssignUpdate` | `AssignCSSTUpdate` | 04:00 | 60 | CSST: re-applies letters and exemptions (CT on/off via `TempCompanyTerritoryHandler2`) |
| `TelemarketerOtherComp` | `AllOtherComp` | none | 60 | one-off: project "All Other Companies" for the telemarketer (5.6) |
| `SetInactiveAllProjectThatEnds` | `SetAllProjectsThatEnds` | 05:30 | 20 | closes ended projects (`DisableMembersWhenProjClose`) and runs `FixTerritory` |
| `Company`, `CompanyDaily` | company import from Advance | none | 90 | prerequisite: creates/updates `Company` rows (with `CRMCompanyId`) that the rebuild matches |

Rules about timers:

- **No action wakes another timer** (no Wake calls anywhere in the model). Timers without a schedule in the model run
  only when someone starts them in Service Center or when a schedule was set there; Service Center's schedule is the
  real one. The production schedules are not in the model (open question 1).
- **The order of a full rebuild is therefore not enforced by code.** The order the logic needs is:
  1. Company import (`Company` / `CompanyDaily`), so new Advance companies exist in `Company`.
  2. `SCA_SalesManCompanyAssignmentDaily` (permanent territories and CTs from Advance).
  3. `TerritoryCode` (independent; nothing reads its output).
  4. CSST: `CSST_Temp`, then `CSST_05_AssignAllCompany` / `CSST_06_AssignUpdate` (04:00).
  5. `SetInactiveAllProjectThatEnds` (05:30): turns off temporary territories of ended projects.
- There is **no full rebuild**: nothing clears the tables first. Every timer only creates or updates rows (section 7).

## 5. Server actions

Each entry: inputs, what it reads and writes, and its rules. Entity actions are named as in OutSystems
(CreateEntity / UpdateEntity / CreateOrUpdateEntity / DeleteEntity). "First" means `List.Current` of an aggregate
without a loop, that is, the first row in the aggregate's sort order.

### 5.1 The Advance rebuild

**`SCA_SalesmanCompanyAssignment`** (timer): `GetCUsers` = distinct `CUser.UserId` where `CUser.RoleId = Sales`
(no `IsActive` filter on CUser or User). For each: `SetTerritory_Update(UserId)`.

**`SetTerritory_Update(UserId)`** (the daily rebuild for one user):

1. `ADName = GetADByUserId(UserId)`.
2. `GetCompaniesListByUser`: `Users 2` (`User_Logon = ADName`, not disabled, not deleted) × `Territories`
   (`Terr_TerritoryID = User_PrimaryTerritory`, not deleted, `<> -2147483640`) × `Company 2` (`Comp_SecTerr` within
   `Terr_TerritoryID..Terr_RangeEnd`). **Deleted Advance companies are included** (no `Comp_Deleted` filter here).
   Empty: end.
3. For each row: `GetCompanyID(Comp_CompanyId)`; no MyCRM company: next row.
4. `GetUserTerritory`: Territory with `Territory = Terr_TerritoryID`, `OwnerID = UserId`, `IsActive = True` (no
   `IsTemporary` filter). Set on the first row (or on an empty record when none): `Territory = Terr_TerritoryID`,
   `OwnerID = UserId`, `IsActive = True`, `IsTemporary = False`, `IsOwner = (UserId(comp_owner) = UserId)`;
   **CreateOrUpdateEntity Territory** (updates the found row, or creates one).
5. CT lookup: `TerritoryID = that Territory`, `CompanyID`, **`IsActive = True`**. Set on the first row (or an empty
   record): `TerritoryID`, `CompanyID`, `IsActive = (Comp_Deleted is empty)`; **CreateOrUpdateEntity CT**.
   `CreatedBy` is not set.

Consequences (all follow from the filters above):

- A company deleted in Advance: its active CT is set inactive on the first run; from then on the lookup (active only)
  finds nothing, so **every run creates a new inactive CT row** for it, for every Sales user whose range covers it.
- An inactive CT (removed or transferred by hand) for a company still in the user's Advance range: the next run
  **creates a new active CT**: the company comes back.
- An inactive Territory row: the next run **creates a new Territory row**.
- Nothing is ever turned off when Advance moves a company out of a user's range, or when a user leaves Sales: old CTs
  stay active.

**`SetTerritory(UserId)`** (older variant, only reachable from AdminPage "Set Territory", see 6.10): same as above
with these differences: the Advance user must also have `User_TerritoryProfile <> 19`; `Comp_Deleted` must be empty;
`GetUserTerritory` also requires `IsTemporary = False`; and for CTs it **DeleteEntity's every active CT** for
(territory, company) and then CreateOrUpdates one active CT.

**`SetTerritoryForUser`**: `SetUser(Session.CurrUserId)`, then (the role test is `Role = Sales or True`, always true)
`SetTerritory(Session.CurrUserId)`.

### 5.2 Transfers and assignments

**`TransferCompany(CompanyId, FromUserId, ToUserId)`** (Public; used by CompanyOwner, `AssignCompaniesToSales`,
`TransferCompanyToUserId`, and possibly other modules):

1. `UserHasCompany(ToUserId, CompanyId)` true: **nothing happens** (the From user keeps the company).
2. `GetToUserTerritory`: To user's active permanent Territory. None: **CreateEntity Territory** (`OwnerID = ToUserId`,
   `IsActive`, not temporary, `IsOwner = True`, `Territory` left at 0). Otherwise the first one.
3. **CreateEntity CT** (`TerritoryID`, `CompanyID`, `IsActive = True`, `CreatedBy = Session.CurrUserId`).
4. If `FromUserId` is given: `GetFromUserTerritory` (active CT of an active permanent territory of FromUserId for the
   company); **only the first** is set `IsActive = False` (UpdateEntity). (Not certain which branch is True/False;
   this is the only reading that makes sense.)
5. `TerritoryMovement(CompanyId, FromUserId, ToUserId)`.

**`TransferCompanyToUserId(UserId, CompanyId)`**: `From = GetCompanyOwnerUserId(CompanyId)`, `TransferCompany`, then
`TerritoryMovement` again (so two movement rows). Nothing calls it.

**`TransferToUSER(CompanyId, ToUserId)`** (TerritoryUpdate): `GetTerritories` = To user's active permanent
territories. None: message (Warning) "The selected salesperson have no permanent territory." and end. Otherwise the CT
for (first territory, company) regardless of `IsActive` is set `IsActive = True`, `CreatedBy = Session.CurrUserId`
and **CreateOrUpdateEntity** (reactivates an old row or creates one).

**`AssignCompanyTerritory(CompanyId, UserId)`**: first Territory with `OwnerID = UserId`, `IsTemporary = False` (max
1, no `IsActive` filter); none: end. CT for (territory, company) exists (any state): end. Otherwise **CreateEntity CT**
(active, `CreatedBy = Session.CurrUserId`).

**`AssignGlobalCompany(UserId)`**: for the companies named exactly "MotherBear", "BrotherBear", "SisterBear":
`AssignCompanyTerritory`. Test data.

**`TerritoryMovement(CompanyId, FromUserId, ToUserId)`**: CreateEntity TerritoryMovement with
`UpdatedBy = Session.CurrUserId`, `UpdatedAt = CurrDateTime()`. Called by TransferCompany, TransferCompanyToUserId,
TerritoryUpdate's Transfer, CompanyTerritories' Add / Remove / Assign. Not called by the timers or by project flows.

**`TransferProspect(ProspectId, ToUserId)`** (Public): sets `Prospect.OwnerId = ToUserId` and the prospect's OAA form
`CreatedBy = ToUserId`. It does not touch territories.

### 5.3 Reading ownership and access

| Action | Returns | Rule |
|---|---|---|
| `GetCompanyOwner(CompanyId)` (Public) | `UserName` | first User of an active CT of an active permanent territory of an active company, sorted `Is_Active` desc, `Last_Login` desc |
| `GetCompanyOwnerUserId(CompanyId)` (Public) | `UserId` | same, without the company `IsActive` filter |
| `GetCompanyMultipleOwners(CompanyId)` (Public) | `UserIdList` | despite the name, max 1 row: `"|" + that same owner + "|"` |
| `GetCompanyOwnerByBRN_API(BRN)` (Public) | `UserName` | as `GetCompanyOwnerUserId` (no company `IsActive` filter), company found by `Company.BRN = BRN` |
| `GetAllPreviousCompOwner(CompanyId)` | `AllPreviousOwner` | `"|id|id|…"` of every `OwnerID` that ever had a CT for the company (any `IsActive`) |
| `UserHasCompany(UserId, CompanyId)` (Public) | `HasCompany` | active CT of an active permanent territory of that user, active company |
| `CheckUserHasPermTerritory(UserId, CompanyId)` | `HasTerritory` | same, without the company filter |
| `UserCanAccessCompany(RoleId, UserList, CompanyId)` (Public) | `CanAccess` | Telemarketer, Admin, SalesAdmin or `ShowAllCompany(RoleId)`: true; else an active CT of an active territory (**temporary included**) whose `OwnerID` is in `UserList`, active company |
| `ShowAllCompany(RoleId)` | `ShowAll` | Admin, SalesAdmin, MD, CSST, CSSTManager, MDSSpecialistManager, MITSSpecialistManager, Head, CreditManager, CreditGM |
| `GetCompaniesByUser(UserList)` (Public, cached 30 min) | list Id/Company/BRN | distinct active companies with an active CT of an active territory (temporary included) owned by someone in `UserList` |
| `GetCompaniesListByUser(UserList)` (cached 30 min) | `"|id|id|"` | same set as text |
| `GetCompanyByUserV2(RoleId, UserList, CompanyId)` | list | PPSpecialist and PPSpecialistManager: all active companies; others: as `GetCompaniesByUser`, optionally one `CompanyId` |
| `GetCompanyByUserList(UserList)` | list | as above, then only companies with an "F90" service order completed 3 days ago (service database) |
| `GetCompaniesByUserByName_API(...)` (Public) | list | by name/BRN, limited to `GetSub` users' companies unless its `IsOwner` input says otherwise (not fully decoded) |

### 5.4 One-off and helper actions

**`AssignCompaniesToSales`** (timer, no schedule): reads a resource Excel, sheet "GA"
(`ExcelFormat_CompanyAssignment`: BRN, Email). For each row with a BRN: company by `Trim(BRN)`,
`GetCompanyMultipleOwners`, the matching User(s), `TransferCompany` for each. The rest of the flow could not be decoded
(not certain who is From and To; probably the Email column is the new owner).

**`ClearTempComp`**: **DeleteEntity** every `TempCompanies` row with `UpdatedBy = GetUserId()`.

**`ValidateCompany(TempCompany, ToUserId)`** (TerritoryUpdate import), returns `CompanyDescription` (Error, Note,
OwnerId, CompanyId):

- `CompName = Trim(Company)`, `BRN = Trim(BRN)`. Both empty: Error, "Company could not be found.".
- Active companies where `Trim(Company) = CompName` **or** `Trim(BRN) = BRN`, grouped by Id; the first is used. None:
  Error, "Company could not be found.". (An Excel row with a name but an empty BRN also matches every company whose BRN
  is empty; not verified against data.)
- The company already has an active CT of an active permanent territory of `ToUserId`: Error, "Company is already
  assigned.".
- Otherwise `CompanyId` and `OwnerId = GetCompanyOwnerUserId(CompanyId)`.

### 5.5 Temporary territories (written by the Projects part, which stays in OutSystems)

**`TempTerritoryHandler(OwnerId, ProjectId, CompanyId, IsActive)`**:

1. `GetTerritoryById`: Territory joined to `ProjectMembers.TerritoryId` with `ProjectMembers.UserID = OwnerId`,
   `ProjectID = ProjectId`, `Territory.IsTemporary = True`, `ProjectRoleID <> ProjectManager`, `IsOwner <> True`.
2. Found: nothing (an existing temporary territory is not reactivated or deactivated).
3. Not found: CreateOrUpdateEntity Territory with `Territory = TextToLongInteger(OwnerId digits + CompanyId digits)`,
   `OwnerID = OwnerId`, `IsActive = IsActive`, `IsTemporary = True`; then the first active `ProjectMembers` row of
   (user, project) gets `TerritoryId` and `IsActive = IsActive` (CreateOrUpdateEntity ProjectMembers; with no active
   member row this **creates** a ProjectMembers row holding only those two values).
4. Because of the `ProjectRoleID <> ProjectManager` filter, a **project manager never finds** their temporary
   territory, so every call for a project manager creates a new Territory row (one per project company per save).

Callers: project screens `SelectUser`, `OnSelectedAsMembersRowSave` (`IsActive = True`), `OnSelectedAsMembersRowDelete`
(`IsActive = False`), each once per project company and only when `CheckUserHasPermTerritory` is false; `SetTempTerritory`;
`AllOtherComp`.

**`TempCompanyTerritoryHandler(ProjectId, CompanyId, IsActive)`**: for every active project member with a
`TerritoryId`: the CT for (member's territory, company), any state, gets `IsActive`, and `CreatedBy = GetUserId()`
when there is a user (else keeps the old value); CreateOrUpdateEntity.

**`TempCompanyTerritoryHandler2(TerritoryId, CompanyId, IsActive)`**: same for one territory (`CreatedBy = GetUserId()`).
Called by the CSST actions `ProjectForCSST`, `Remove_ProjectForCSST`, `AssignCSSTUpdate`, `AddAccount`.

**`TempTerritoryHandlerWhenDel(OwnerId, ProjectId, CompanyId)`** (project deleted, `DisableMembersWhenProjDeleted`):
the same territory lookup as above; found: Territory `IsActive = False` (UpdateEntity), then the first CT of that
territory for the company within the project's companies: `IsActive = False`.

**`DisableMembersWithCompDel(ProjectId, CompanyId)`** (a company removed from a project): for each project member, the
first CT of (member's territory, company) is set `IsActive = False`.

**`FixTerritory`** (from the 05:30 timer): every active temporary Territory linked to a project member that is inactive,
or whose project is inactive, closed or past `EndDate`, and every active temporary Territory linked to no project
member: `IsActive = False` (UpdateEntity). Their CTs stay active (they no longer count, because every reader requires
an active territory).

**`SetTempTerritory`** (timer `CSST_Temp`, and the CSST screen's `RefreshCSSTList`): active project members whose CUser
is active with role CSST or CSSTManager, in active projects named like "CSSTProject%", grouped by user, project and
`TerritoryId`; for each without a `TerritoryId`: the first active company (`Get1Comp`, filter partly undecoded) and
`TempTerritoryHandler(…, IsActive = True)`.

### 5.6 CSST and telemarketer one-offs (summary)

- **CSST** (`AssignAlloROneCompToACSST`, `AssignCSSTUpdate`, `CSST_Proj`, `CSST_Proj_Remove`, `ProjectForCSST`,
  `Remove_ProjectForCSST`, `AddAccount`): CSST staff are given companies by the first letter of the company name
  (`CSSTCompanyAssignment.Accounts`) minus exemptions (`CSSTCompanyAssignment.Exemption`). Each company is added to the
  person's "CSSTProject…" project (`ProjectCompany`) and to their temporary territory (`TempCompanyTerritoryHandler2`).
  `CSST_Proj_Remove` **DeleteEntity's** the `CSSTCompanyAssignment` row (not a territory table).
- **`AllOtherComp`** (timer `TelemarketerOtherComp`): creates the project "All Other Companies" (end date 2050-01-01)
  if missing, with the User named "fabelteng@ricoh.sg" as project manager and "phyllislim@ricoh.sg" (the telemarketer)
  as member, gives her a temporary territory and adds companies (`ProjectCompany` and CT, CreateOrUpdateEntity). Which
  companies exactly could not be fully decoded (names in flow: `GetCompanyTerritories`, `CurrentlyAssignToPhyllis`).
  Hard-coded people: not for the new app.

## 6. Screens and blocks

### 6.1 Territories (screen, flow BackOffice)

- **Who**: screen roles Registered, CRMUser, Admin, MyOrdersUser, but Preparation sends anyone whose MyCRM role is not
  Admin (`Role(Session.CurrUserId) = Admin`) to a Common-flow screen (key `p2ryMfs3nUuuPBYmCQsJJQ`, probably the
  no-permission page; not certain).
- **Title** "Territories". Link "Create a new Territory" (to TerritoryDetail, no id).
- **Search**: one box (title "Type the Owner"), buttons "Search" and "Reset" (clears the keyword). Filter:
  owner `User.Name` like `%keyword%` (keyword kept in `Session.Territories_SearchKeyword`).
- **Table** `TerritoryTable`, 50 rows a page, "No territories to show...": columns **Territory** (the number, link
  "Edit Territory" to TerritoryDetail), **Owner** (User name), **Is Active** and **Is Temporary** (tick images).
  Every column header sorts. Source: Territory left join User. It pages through all 4.5 million rows.

### 6.2 TerritoryDetail (screen, flow BackOffice)

- **Who**: as Territories (Admin only).
- **Title** "New Territory" or "Territory". Form `TerritoryForm`: **Territory** (text box, max 20, not mandatory),
  **Owner** (drop-down of every User by name, first entry "-"), **Is Active**, **Is Temporary**, **Is Owner**
  (check boxes). No field is mandatory.
- **Save**: if the form is valid, CreateOrUpdateEntity Territory, message "The territory was successfully created." or
  "…updated.", back to Territories. **Cancel**: back to Territories. No validation of duplicates, no movement log, CTs
  untouched.

### 6.3 TerritoryUpdate (screen, flow TerritoryUPDATE): bulk transfer from Excel

- **Who**: screen roles Registered, CRMUser, Admin, MyOrdersUser; Preparation lets through MyCRM roles Admin,
  SalesAdmin and MD. Anyone else gets the message (Warning) "You have no permission to view the page." and an empty
  page (the drop-down is not loaded).
- **On load**: `LoggedUserId = GetUserId()`; `GetTempCompaniesByUpdatedBy` (this user's `TempCompanies`, by Company);
  `GetUsers`: Users that have a CUser with a role and an active permanent territory, and are active or named like
  "TBH%", grouped by name, sorted by name.
- Note above the drop-down: "note: only salesperson with permanent territory are shown on this list".
- **"Select Salesperson"** drop-down (`salesSelect`, value `SelectedUSER`). On change (`OnUserChange`): `ClearTempComp`
  (deletes this user's scratch list), clears the error list, refreshes.
- The rest shows only when a salesperson is selected:
  - Box "File Upload": "Please select a file with the data to be imported and hit the Import button. Click **here**
    for a sample." ("here" downloads the resource `Company.xlsx`). "Data FIle" file input and **Import** button.
  - Box "Companies from Excel" with a "clear" link (`OnClearClick`: `ClearTempComp`, clears errors), headings
    Company / BRN / Owner and the **TempComp** block (6.4).
  - Box "Errors" with a download icon (`Download`: the error list as Excel, file "CompanyWithError_" + the
    salesperson's name; exact suffix not certain) and a list Company / BRN / Error.
  - A clickable arrow "assign to <salesperson name>" with the confirmation "Are you sure you want to assign <n>
    companies to <salesperson name>?" (runs `TransferTerritory`).
  - Box "Assigned Companies" with the **CurrentCompanies** block (6.5) for the selected salesperson.
- **Import**: reads sheet "Sheet1" (columns Company, BRN). For each row: `ValidateCompany(row, SelectedUSER)`.
  - Valid: if this user's scratch list has no row with the same BRN, company name or CompanyId (exact grouping of the
    conditions not certain), **CreateEntity TempCompanies** (Company, BRN, CompanyId, `UpdatedBy`); otherwise skipped.
  - Error "Company is already assigned.": skipped silently.
  - Other errors: added to the error list.
  - Afterwards, if there are errors: message (Error) "All records with errors." when every row failed, else "<n> records
    with errors.".
- **Transfer** (`TransferTerritory(ToUserID = SelectedUSER)`), validations in order: no salesperson: "Please select a
  salesperson."; empty list: (Warning) "No company is found on the list.". Then for each scratch company:
  1. All its active CTs of active permanent territories, sorted owner `Is_Active` desc, `Last_Login` desc.
  2. Each is set `IsActive = False` (UpdateEntity): **every current owner loses it**.
  3. `TerritoryMovement(company, From = owner of the first of those rows (empty if none), To = SelectedUSER)`.
  4. `TransferToUSER(company, SelectedUSER)` (reactivates or creates the target's CT).
  Then (Success) "<n> companies have been successfully assigned to <name>.", `ClearTempComp`, refresh. Errors abort the
  whole transaction.

### 6.4 TempComp (block, on TerritoryUpdate)

- This user's `TempCompanies`. Search box (title "Type the Company, BRN, Company or Updated By"; filters Company or BRN
  like the keyword), "Search", "Reset". Table 50 a page, "No temp companies to show...": **Company**, **BRN**,
  **Owner** (`GetUsernameByUserId(GetCompanyOwnerUserId(CompanyId))`), and a remove icon.
- Remove icon (`DeleteRowData`): **DeleteEntity TempCompanies** row, no confirmation; tells TerritoryUpdate to refresh
  its count.

### 6.5 CurrentCompanies (block, on TerritoryUpdate)

- Input `SalespersonId`. Companies (active) with an active CT of an active permanent territory owned by that person,
  sorted by name. Search (title "Type the Company or Created By", but only the company name is searched), "Search",
  "Reset". Table 50 a page, one column **Company**, "No company territories to show...". Read only.

### 6.6 UserTerritory (screen, flow Admin) with the CompanyOwner and CompanyTerritories blocks

The screen that hosts these two blocks (it belongs to the Users part; summarised here because it creates territories):

- Input `UserId`; none: go to Users. Preparation: `IsSalesAdmin(Session.CurrUserId)` (CUser role SalesAdmin or Admin)
  else the Common-flow screen. If the user has no active permanent territory and their role is Sales:
  **CreateEntity Territory** (`OwnerID`, active, permanent, `IsOwner = True`, `Territory` 0).
- Shows the user's name, link "Back to Users", button "Reset" (clears the three search keywords). If the user has an
  active permanent territory: the **CompanyOwner** block (`ToUserId = UserId`) and the **CompanyTerritories** block
  (`UserId`, `TerritoryId` = the **first** active permanent territory).

### 6.7 CompanyOwner (block): take a company from its current owner

- "Search Company", box "Company Name / BRN", "Search", "Reset". Until a keyword is typed: "Key in company name / brn
  to search". Results: all companies (active or not) whose name or BRN contains the keyword, by name, 50 a page, in a
  scrolling box. Each row: company name, current owner's name (blank when none) and a transfer icon.
- Transfer icon, confirmation "Are you sure you want to transfer this company to <user name>?": `TransferCompany(company,
  From = GetCompanyOwnerUserId(company), To = ToUserId)`, refresh, notify UserTerritory (refresh). No message.
- The block also holds leftover actions `Save` ("Vacant User renamed") and `Cancel` with no widget using them.

### 6.8 CompanyTerritories (block): the user's companies, add / remove / reassign

Inputs `UserId`, `TerritoryId`. Two panels:

- **"Unassigned Companies"** (`GetCompanies`, SQL): active companies that have **no** active CT of an active permanent
  territory at all, whose name or BRN contains the keyword. Search box (Enter = Search), "Search", "Reset". List 50 a
  page with check boxes, "No items to show...". Links "Select All Shown Records", "Unselected All" (the shown page),
  and "Add selected company to user's territory".
- **"Assigned Companies"** (`GetCompanyTerritoriesByTerritoryID`): active companies with an active CT in `TerritoryId`
  (the territory must be active and permanent), name or BRN like the keyword. Up to 50,000 rows at once, check boxes;
  Admins also see the CT Id and "Territory ID: <TerritoryId>". Links "Select All", "Unselected All", "Remove selected
  company from territory" and "Assign selected company to another user".

Actions:

- **Add selected company to user's territory** (`AddSelectedCompany`): for each ticked unassigned company:
  **CreateEntity CT** (`TerritoryID = TerritoryId`, active, `CreatedBy = Session.CurrUserId`),
  `TerritoryMovement(company, From = empty, To = UserId)`. Then an audit `Log` "<companies> are assigned to <name>",
  refresh, (Success) "<n> companies are added to user's territory".
- **Remove selected company from territory** (`RemoveSelected`), confirmation "Are you sure you want to remove?": for
  each ticked company: CT `IsActive = False` (UpdateEntity), `TerritoryMovement(company, From = UserId, To = empty)`.
  `Log` "<companies> are removed from <name>", (Success) "<n> companies are removed from user's territory".
- **Assign selected company to another user**: opens **AssignUser_PopUp** ("Choose a user to assign": active CUsers with
  role Sales, shown as "Name [Active]" / "Name [Inactive]"; "Assign" / "Cancel"). On Assign (`OnAssignNotify`): no user:
  (Error) "No user is selected". Otherwise the chosen user's first active permanent territory, or a new one
  (CreateEntity Territory, `Territory` 0, `IsOwner = True`). For each ticked company:
  - the chosen user already has it: `TerritoryMovement(From = UserId, To = empty)`;
  - otherwise **CreateEntity CT** in the chosen user's territory and `TerritoryMovement(From = UserId, To = chosen)`;
  - in both cases this user's CT is set `IsActive = False`.
  `Log` "<companies> are assigned to <name>", (Success) "<n> companies are assigned to <name>, <m> companies are removed
  from user's territory".
- `DeleteUserAndTerritory` ("User and territory are delete for <name>", then Users): `DeleteCUserNTerritory`, which
  **DeleteEntity's the user's active CUser rows** (`RemoveAllCUserForReAssign`) and sets every active Territory of the
  user and their active CTs `IsActive = False`, with `Log` "Territories for <name> are deleted". **No widget calls this
  action** in the decoded model (not certain; it may be reached from another screen).

### 6.9 PrimarySalesperson (block, on the Admin "Company" list)

Input `CompanyId`. Lists the distinct names of Users with an active CT of an active permanent territory for the company,
whose CUser is active with role **Sales** and whose User is active. One line per name. Read only.

### 6.10 AdminPage "Set Territory" (button)

On AdminPage (MyCRM role Admin): `SetTerritoryForUser` (the Advance rebuild for the admin themselves, with the CT
deletes of 5.1) then `AssignGlobalCompany(Session.CurrUserId)` (the three test companies). A developer tool.

### 6.11 TerritoryMovement (block, on CompanyDetails)

- Shown when the viewer's MyCRM role is neither Sales nor Telemarketer.
- "Territory Movement History", all rows for the company, newest first (`UpdatedAt` desc), "No items to show...":
  **Previous Sales rep** (From name), **Previous Sales Manager** (`GetCUserTeamName(From)`, the team name),
  **Current Sales Rep**, **Current Sales Manager**, **Updated By**, **Updated At** (pretty date).

### 6.12 companyOwnerCaretake (block, on CompanyDetails)

"Owner:" = `GetCompanyOwner(CompanyId)`; "Caretaker:" = full names of members of projects linked to the company
(`ProjectCompany` → `ProjectMembers`; the exact member filter was not decoded).

## 7. Hand-made changes versus the timers

What people do by hand:

| Where | What | Writes |
|---|---|---|
| TerritoryUpdate | bulk transfer from Excel to one salesperson | CTs off for all current owners, target CT on, movement |
| UserTerritory › CompanyOwner | take one company to this user | TransferCompany |
| UserTerritory › CompanyTerritories | add unassigned companies, remove, reassign to another Sales user | CT create / off, movement |
| TerritoryDetail | edit a Territory row (owner, flags) | Territory |
| Projects (stays in OutSystems) | add/remove project members and companies, close/delete projects | temporary Territory and CTs |
| CSST screens (stay) | letters and exemptions | temporary CTs |

What the daily rebuild (`SetTerritory_Update`) does to them:

- **Hand-made assignment of a company the user does not have in Advance** (Add, Transfer, TerritoryUpdate,
  CompanyOwner): kept. The rebuild never turns anything off.
- **Hand-made removal or transfer away of a company that Advance still puts in the old owner's range**: undone in
  effect. The next run creates a new active CT for the old owner. After a transfer both users hold the company, and
  "owner" (first active user by last login) may flip back.
- **TerritoryDetail edits**: a row set inactive is replaced by a new row on the next run; an owner change moves the
  row (and its CTs) to the new owner, and the old owner gets a new row next run.
- **Advance changes** (a company moves to another range, a salesperson leaves): the new holder gets a CT, the old CT
  stays active forever.
- **Territory numbers**: hand-made rows have `Territory = 0`, Advance rows the Advance id, so a salesperson can hold two
  active permanent territories. Every "first permanent territory" choice (TransferCompany, TransferToUSER,
  AssignCompanyTerritory, OnAssignNotify, UserTerritory's `TerritoryId`) then takes whichever comes first, and the
  UserTerritory screen shows only that one territory's companies.
- Temporary territories are not touched by the rebuild; the 05:30 timer turns off those of ended projects.

## 8. Why there are millions of rows

Production: `Territory` 4,461,908; `CompanyTerritory` 3,541,794; `TempTerritoryCode` 1,384,629; `TerritoryMovement`
141,881; for 61,228 companies and 2,573 CUsers.

What the model proves:

- **CompanyTerritory grows every run** for companies deleted in Advance: `SetTerritory_Update` creates a new inactive
  CT per (Sales user whose range covers the company, deleted company) **on every run** (5.1). A daily run since 2017
  (about 3,000 runs) times a few hundred to a thousand such pairs gives millions of inactive rows. Every hand
  removal/transfer of an Advance company also adds one row per run that undoes it.
- **No path deletes** Territory, TerritoryMovement or TerritoryCode rows, and CTs are deleted only by AdminPage
  "Set Territory" (section 9). Everything is soft and accumulates.
- **Territory**: the rebuild keeps one row per (Advance territory, user) while it stays active. Rows that do multiply:
  `TempTerritoryHandler` creates a new temporary row on every call for a **project manager** (one per project company
  per save, active or inactive), and a new row each time a deactivated row would have been reused. This explains growth
  but not obviously 4.4 million; older versions of the logic may have left rows too (open question 2).
- **TempTerritoryCode**: the current `TerritoryCode` timer keeps at most one row per company (it updates the first row
  for the company), which cannot reach 1.4 million rows for 61,000 companies; the rows must come from an earlier logic
  or a bulk load (open question 3).

Queries that settle it (physical column names are usually the attribute names in capitals; check them first):

```sql
SELECT ISTEMPORARY, ISACTIVE, COUNT(*) FROM OSUSR_824_TERRITORY GROUP BY ISTEMPORARY, ISACTIVE;
SELECT TOP 20 OWNERID, TERRITORY, ISTEMPORARY, COUNT(*) N FROM OSUSR_824_TERRITORY
  GROUP BY OWNERID, TERRITORY, ISTEMPORARY ORDER BY N DESC;
SELECT ISACTIVE, COUNT(*) FROM OSUSR_824_COMPANYTERRITORY GROUP BY ISACTIVE;
SELECT TOP 20 TERRITORYID, COMPANYID, ISACTIVE, COUNT(*) N FROM OSUSR_824_COMPANYTERRITORY
  GROUP BY TERRITORYID, COMPANYID, ISACTIVE ORDER BY N DESC;
SELECT COUNT(*), COUNT(DISTINCT COMPANYID) FROM OSUSR_824_TEMPTERRITORYCODE;
```

For the new app: only **active CTs of active territories** carry meaning; the live picture is "for each company, the
set of users holding it permanently, plus temporary project access". That set is small (tens of thousands of rows) and
is what MyCRMRecords needs to keep.

## 9. What MyCRM deletes, and what it only marks inactive

DeleteEntity in this area:

| Entity | Where | When |
|---|---|---|
| `CompanyTerritory` | `SetTerritory` | AdminPage "Set Territory": every active CT of (the admin's Advance territory, company) is deleted before one is re-created |
| `TempCompanies` | `ClearTempComp` | TerritoryUpdate: salesperson changed, "clear" clicked, after a successful transfer (this user's rows) |
| `TempCompanies` | TempComp `DeleteRowData` | the remove icon on a scratch row |
| `CUser` (Users part) | `RemoveAllCUserForReAssign` via `DeleteCUserNTerritory` | CompanyTerritories `DeleteUserAndTerritory` (no widget found that calls it) |
| `CSSTCompanyAssignment` (CSST, not moving) | `CSST_Proj_Remove` | removing a letter from a CSST person |

`Territory`, `TerritoryMovement`, `TerritoryCode`, `TerritoryRelationship` and `CompOwner` are never deleted.

Marked inactive instead:

- `Territory.IsActive = False`: TerritoryDetail (by hand), `DeleteCUserNTerritory`, `FixTerritory` (05:30, temporary
  rows of ended projects or without a member), `TempTerritoryHandlerWhenDel` (project deleted).
- `CompanyTerritory.IsActive = False`: CompanyTerritories Remove and Assign-to-another-user, `TransferCompany` (first
  old row), TerritoryUpdate transfer (all old rows), `DeleteCUserNTerritory`, `TempTerritoryHandlerWhenDel`,
  `DisableMembersWithCompDel`, `TempCompanyTerritoryHandler`/`2` with `IsActive = False` (member removed, CSST letter
  removed, project closed), and the rebuild for Advance-deleted companies.
- `TempCompanies` is a scratch table; the new app can keep it in the page instead of a table.

## 10. What other parts of MyCRM depend on

Inside MyCRM (these stay in OutSystems and keep reading the tables):

- **Access to a company**: `CompanyDetails` (`UserCanAccessCompany`); company lists `CompanyList`, `CompanyList_Table`,
  `CompanyGroupName_LazyLoad` (`ShowAllCompany` plus the territory join).
- **Activities**: `CompanyActivity_Edit` (`GetCompanyByUserV2`), `CompanyActivitiesByOthers`/`2`
  (`GetCompaniesListByUser`), `CompanyActivity_List` and `GetProspects_List` (`GetAllPreviousCompOwner`: a company's
  activities and prospects stay visible to every former holder), `GetActivityTypeCount`, `CalendarBlock`.
- **Dashboard and MIF**: `GetCompanyToVisitBy` (needs `Territory.IsOwner = True`), `GetExpiredMachines`,
  `GetExpiringMachines`, `MIFRetentionCompanies`(`_Table`), `GetCompanyCompetitorsMIFs`, `withMif`, `CompanyGroupPieChart`,
  `Summary`, `ReportGenerator`, `Report`, `Download`, timer `GenerateMIFGroupChart` (07:00, uses `GetCompanyOwnerUserId`).
- **Service alerts**: `LastServicesCompaniesList` (`GetCompanyByUserList`).
- **Projects**: `ProjectDetails_PopUp`/`2` (companies of a user via CT), project member and company screens and the
  05:30 timer (write temporary territories, section 5.5). **Projects and CSST stay in OutSystems but write `Territory`
  and `CompanyTerritory`**: the new app must either accept these writes or provide the same operations.
- **Owner names** shown on Company screens: `GetCompanyOwner` (companyOwnerCaretake), `GetCompanyOwnerUserId`
  (TempComp, CompanyOwner), `PrimarySalesperson`.
- `Session.UserList` (from `GetSub` at sign-in) is the key into all territory reads.

Public actions other modules may call (consumers not visible in this model): `TransferCompany`, `UserHasCompany`,
`UserCanAccessCompany`, `GetCompanyOwner`, `GetCompanyOwnerUserId`, `GetCompanyMultipleOwners`, `GetCompaniesByUser`,
`GetCompaniesByUserByName_API`, `GetCompanyOwnerByBRN_API`, `TransferProspect`. Their results must stay the same.

Not read anywhere in MyCRM: `TerritoryCode` (`TempTerritoryCode`), `TerritoryRelationship`, `CompOwner` (MyFile reads
`CompOwner`, see README section 9).

## 11. UI that could be friendlier

- Territories lists 4.5 million raw rows with only an owner search; a per-user view of active holdings is what people
  need. TerritoryDetail allows creating duplicates and editing flags with no movement log.
- Three different screens do transfers with three different rules (TerritoryUpdate takes the company from all owners,
  CompanyOwner from the first only and does nothing if the target already holds it, CompanyTerritories from this user
  only). One "Transfer company" operation with one rule would remove the confusion.
- TerritoryUpdate needs an Excel file even for one company, deletes the scratch list on every salesperson change, and
  skips "already assigned" rows silently.
- CompanyTerritories loads up to 50,000 assigned companies at once; "Select All Shown Records" only selects the page.
- Search placeholders promise fields that are not searched ("…or Created By", "…Updated By").
- No confirmation on removing a scratch row; the Owner column is computed per row (slow).
- Labels: "Unselected All", "Data FIle", "have no permanent territory", "User and territory are delete for".

## 12. Open questions

1. The real timer schedules (Service Center): when `SCA_SalesManCompanyAssignmentDaily`, `TerritoryCode`, `CSST_Temp`,
   `CSST_05_AssignAllCompany`, `AssignCompaniesToSales`, `TelemarketerOtherComp` and the company import run, and
   whether the unscheduled one-offs still run at all.
2. The composition of the 4.46 million `Territory` rows (queries in section 8).
3. Where the 1.38 million `TempTerritoryCode` rows came from, and whether any other application reads that table.
4. `TransferCompany`: the If on `FromUserId = NullIdentifier()` has unreadable branch labels (section 5.2 gives the
   only sensible reading).
5. `AssignCompaniesToSales`: the end of the flow did not decode (From/To of each transfer).
6. The User attribute shown here as `User.Name` (key `DbJTGVIxP0i_A+TxvGI_LQ`) is compared with values such as
   "phyllislim@ricoh.sg"; it may be `Username` rather than `Name`.
7. The Common-flow screen non-permitted users are sent to (key `p2ryMfs3nUuuPBYmCQsJJQ`).
8. In TerritoryUpdate Import, how the duplicate check on the scratch list groups its conditions (BRN / name /
   CompanyId); in `ValidateCompany`, whether an empty BRN really matches every company with an empty BRN.
9. The message type used for errors (shown as `Message Type` record `ixn8_qr+_UC+jp4OBVRCTg`) is assumed to be Error.
10. The created-date window in `TerritoryCode` (updated within 5 days is certain; created within ? days not decoded).
11. Whether `DeleteUserAndTerritory` is reachable from any screen; `AllOtherComp`'s exact company selection; the
    caretaker filter in `companyOwnerCaretake`.
