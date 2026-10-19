# How MyCRM's users, roles and teams work

This is the reference for the **Users** part of MyCRM (OutSystems Traditional Web, eSpace `MyCRM`, tables
`OSUSR_824_…`) and for MyCRM's **Admin page**. It was written on 2026-10-19 from MyCRM's exported model (.oml),
decoded with a home-made reader because Service Studio did not answer. The reader is best-effort: everything below was
checked against the raw element tree; where that was not enough the text says **not certain**.

Names in `code` are entity, attribute, action and widget names as they are in MyCRM. Text in "quotes" is exactly what
MyCRM shows (labels, buttons, messages), including its typos and odd capitals.

## Contents

- [1. The model in one page](#1-the-model-in-one-page)
- [2. Entities](#2-entities)
- [3. Logging in, OutSystems roles and screen access](#3-logging-in-outsystems-roles-and-screen-access)
- [4. Screens: users](#4-screens-users)
- [5. Screens: teams](#5-screens-teams)
- [6. The Admin page and its pop-ups](#6-the-admin-page-and-its-pop-ups)
- [7. Company group names, company groups, number of employees](#7-company-group-names-company-groups-number-of-employees)
- [8. GetSub: who sees whose data](#8-getsub-who-sees-whose-data)
- [9. Other "who is under whom" actions](#9-other-who-is-under-whom-actions)
- [10. Role, team and user helper actions](#10-role-team-and-user-helper-actions)
- [11. Actions that write CUser, Team or Users.User](#11-actions-that-write-cuser-team-or-usersuser)
- [12. Provisioning from Advance (legacy) and the Team timer](#12-provisioning-from-advance-legacy-and-the-team-timer)
- [13. What MyCRM deletes, and what it only marks inactive](#13-what-mycrm-deletes-and-what-it-only-marks-inactive)
- [14. What the rest of MyCRM depends on](#14-what-the-rest-of-mycrm-depends-on)
- [15. UI that could be friendlier](#15-ui-that-could-be-friendlier)
- [16. Open questions](#16-open-questions)

## 1. The model in one page

- A person logs in with an **OutSystems user** (`Users.User`, shown by the decoder as `xposeP`). Its `Username` is the
  Windows login with the domain, `RSP\<logon>` (MyCRM strips `RSP\` or `rsp\` to get the AD / Advance logon, see
  `GetADByUserId` in 10). `Name` is the display name, `Is_Active` says whether the account may log in.
- MyCRM's own profile of that person is **`CUser`**: one row per (user, team). It holds the MyCRM **role**
  (`RoleId` → static entity `Role`, 45 records), the **team** (`TeamId` → `Team`), and `Designation`, `PhoneNumber`,
  `SalesmanCode`, `TerritoryCode`, `ProfileId` (a MyOrders profile), `CId` (the person's Id in the old CRM).
  A Sales or Manager person normally has one row; a person with one of the "other" roles has **one row per team they
  are assigned to**, all with the same role and details. A person with no active `CUser` row is "not a CRM user".
- **Teams** are named after a manager (`Team.Name` = the manager's `User.Name`, `ShortName` = his logon in the old
  Advance-based set-up). Sales people of a team see nothing of each other; the manager sees his team (section 8).
- Everything a person may see is decided by **`GetSub(UserId)`**, computed at login into `Session.UserList`
  ("|id||id|…"), from the person's MyCRM role and teams (section 8).
- Two OutSystems roles exist: `CRMUser` (granted automatically to every active user on every request) and `Admin`
  (used only to show some columns in the company territories block). Real permissions come from the MyCRM `Role` of
  the `CUser` row: `Admin` opens the Admin page; `Admin` or `SalesAdmin` open the Users screens.
- `VacantUsers` is never used. A "vacant user" (to-be-hired, "TBH") is an **inactive OutSystems user** renamed with
  the *Rename & Set To Vacant User* pop-up, so the territories of a person who left can be parked on it.

## 2. Entities

### 2.1 CUser (`OSUSR_824_CUser`, 2,573 rows)

| Attribute | Type | Notes |
|---|---|---|
| `Id` | Long Integer, auto | |
| `UserId` | → `Users.User` | Delete rule Protect. Not mandatory in the model, always set by the screens. |
| `RoleId` | → `Role` | Protect. |
| `TeamId` | → `Team` | Protect. Empty for CSST in some cases (11.3). |
| `IsActive` | Boolean, default True | The screens never set it to False: rows are **deleted** instead (13). |
| `CreatedBy`, `CreatedAt` (default CurrDateTime), `UpdatedBy`, `UpdatedAt` | | Only `CreateProfileFor_NewCSST*` fill `CreatedBy`/`UpdatedBy`/`UpdatedAt`. |
| `Designation` | Text 50 | Job title, printed on documents (14). |
| `PhoneNumber` | Phone 20 | |
| `SalesmanCode` | Text 50 | |
| `CId` | Integer | User id in the old CRM database; set only by `UpdateCUserCId` (11.6), which nothing calls. Lost whenever a user is saved on CUserDetails (rows are deleted and re-created). |
| `ProfileId` | → MyOrders `Profiles` | Set by CUser_Sales and CUser_Manager, **not** by CUser_Others (lost when such a user is saved). |
| `TerritoryCode` | Text 50 | Free text code typed on CUserDetails. |

### 2.2 Role (static entity, `OSUSR_824_Role`, 45 records)

Attributes: `Id` (Integer, auto-numbered by OutSystems when the static entity is published, so **the Id values are
those in the production table**, not in the model), `Label` (Text 50, mandatory), `Order` (Integer), `Is_Active`,
`Level` (Integer, added 2018). **No record has a `Level` value in the model** (all 0); nothing in the flows reads it.
All 45 records are active. The combo boxes on the role pickers list them by `Order`.

| Record (identifier in flows) | Label | Order |
|---|---|---|
| Sales | Sales | 1 |
| Manager | Manager | 2 |
| Head | Head | 3 |
| GM | GM | 4 |
| MD | MD | 5 |
| Admin | Admin | 6 |
| SalesAdmin | SalesAdmin | 7 |
| Telemarketer | Telemarketer | 8 |
| Contract | Contract | 9 |
| CLETrainer | CLETrainer | 10 |
| ChannelPartner | Channel Partner | 11 |
| NRM | NRM | 12 |
| Marketing | Marketing | 13 |
| MarketingGM | Marketing GM | 14 |
| PPSpecialist | PP Specialist | 15 |
| PPSpecialistManager | PP Specialist Manager | 16 |
| MDSSpecialist | MDS Specialist | 17 |
| MDSSpecialistManager | MDS Specialist Manager | 18 |
| MITSSpecialist | SOL Specialist | 19 |
| MITSSpecialistManager | SOL Specialist Manager | 20 |
| CLETrainerManager | CLETrainerManager | 21 |
| CreditManager | Credit Manager | 22 |
| CSMRequester | CSMRequester | 23 |
| StockAllocator | Stock Allocator | 24 |
| Allocator | Allocator | 25 |
| Scheduler | Scheduler | 26 |
| ITHelpdesk | ITHelpdesk | 27 |
| SRO | SRO | 28 |
| SpecialistSeniorManager | Specialist Senior Manager | 29 |
| Recon | Recon | 30 |
| CSST | CSST | 31 |
| CSSTManager | CSSTManager | 32 |
| Credit | Credit | 33 |
| Helpdesk | Helpdesk | 34 |
| ServiceEngineer | ServiceEngineer | 35 |
| OPO | OPO | 36 |
| AP | AP | 37 |
| APManager | AP Manager | 38 |
| CreditGM | Credit GM | 39 |
| SMOVE | SMOVE | 40 |
| Presales | Presales | 41 |
| CustomerSupport | Customer Support | 42 |
| Logistics | Logistics | 43 |
| ServiceDeliveryManager | Service Delivery Manager | 44 |
| CustomerSuccessExecutive | Customer Success Executive | 45 |

Note the two records whose label differs from their identifier: `MITSSpecialist` is shown as "SOL Specialist" and
`MITSSpecialistManager` as "SOL Specialist Manager". Flows always test the identifier (the Id), never the label,
except `GetSalesmanSub`, which filters on `Role.Label = "Telemarketer"`.

### 2.3 Team (`OSUSR_824_Team`, 52 rows)

`Id`, `Name` (Text 255), `ShortName` (Text 50, described "Not in use" in the model, but `GetUserTeams` matches on it),
`IsActive` (Boolean, default True). Teams are never deleted by the screens, only switched inactive (5.1, 6.4). The
legacy `Team` timer deletes them all (12.4).

### 2.4 VacantUsers (`OSUSR_824_VacantUsers`, empty)

`Id`, `UserId` (→ User), `PreviousOwner` (Long Integer), `IsActive`. **No flow, screen or widget references it.**

### 2.5 CompanyGroupName (`OSUSR_824_CompanyGroupName`, 1,584 rows) and CompanyGroup (static)

`CompanyGroupName`: `Id`, `GroupName` (Text 255). Referenced by `Company.CompanyGroupNameId` and
`Company.PreCompanyGroupNameId` (both delete rule Protect). Written by CompanyGroupNameDetail (7.1) and by the imports
`ImportGMA`, `ImportLG`, `ImportLMA` (create or update), and by SQL in `ImportGMAUpdateCompany`, `ImportLGUpdateCompany`,
`ImportLMAUpdateCompany`, `ImportSME` (company part, not described here).

`CompanyGroup` (static, `Label`, `Order`, `Is_Active`): SME "SME" 4; DEASME "SMEDEA" 12; Dealer "DEA" 9; JKA "JKA" 7;
Govt "GOV" 6; CA "CA" 10 (inactive); LG "LG" 5 (inactive); GA "GA" 11; MA "MA" 8; LMA "LMA" 3 (inactive);
GMA "GMA" 2.

### 2.6 MonthCoverage (`OSUSR_824_MonthCoverage`)

`Id`, `Months` (Integer), `Label` (Text 50). A list of month ranges edited on the Admin page (6.5) and used by the MIF
screens (`MIFs`, `MIFRetention`, `ChangeMonthCoverage`), which stay in MyCRM.

### 2.7 Things outside MyCRM that the Users part uses

- `Users.User` (System): `Id`, `Name` ("Full name"), `Username` ("Login name", holds `RSP\logon`), `Email`,
  `Password`, `MobilePhone`, `External_Id`, `Creation_Date`, `Last_Login`, `Is_Active`.
- MyOrders `Profiles` (sales target profiles, picked on CUser_Sales/CUser_Manager) and MyOrders `PPSpecialist`
  (read by `UserListByRole`). MyOrders' role `MyOrdersUser` appears in some screen permissions.
- AdvanceConnector `Users 2` (`User_Logon`, `User_TerritoryProfile`, `User_PrimaryTerritory`, `User_Disabled`,
  `User_Deleted`, `User_FirstName`, `User_LastName`, `User_Department`) and `Territories` (`Terr_TerritoryID`,
  `Terr_RangeEnd`): only the legacy provisioning actions read them (12).
- A `CRMUsers` table of the old CRM (`User_Logon`, `User_UserId`, `User_CreatedBy`): only `UpdateCUserCId`.
- Site property `DropdownShowActiveOnly` (drop-down lists built by `GetSubList…` hide inactive users when True).

## 3. Logging in, OutSystems roles and screen access

### 3.1 Every request (`OnBeginWebRequest`)

1. `Session.CurrUserId = GetUserId()`.
2. If a user is logged in and `IsActiveUser(CurrUserId)` (a `Users.User` with that Id and `Is_Active = True` exists),
   MyCRM **grants the OutSystems role `CRMUser`** to that user (`GrantCRMUserRole`). So `CRMUser` is held by every
   active user who has ever opened MyCRM; it is not an administrative choice.
3. Then, if the page is not a loading screen, it logs the page as a "recent item" unless its name contains "popup" or
   "companydetails".

At login (`bI9Zco3zhU2jJpd0h9OsZg`, the login action) MyCRM sets `Session.UserList = GetSub(Session.CurrUserId)` and
clears `Session.SelectedManagerUserId`, `Session.SelectedUserId`, `Session.SelectedHeadUserid`. `Session.UserList` is
**not recomputed** until the next login: a team change takes effect for a person only when they log in again.

### 3.2 OutSystems roles (`docs/*_Roles.txt`)

| Role | Persistent | Use |
|---|---|---|
| `CRMUser` | yes | Granted to every active user on every request (3.1). Required by almost every screen. |
| `Admin` | yes | Only `CheckAdminRole` in the `CompanyTerritories` block (shows extra columns, territory part). Who holds it is managed in the Users application. |

### 3.3 Who may open what (Users part)

| Screen | Screen roles | Extra check inside | Effective rule |
|---|---|---|---|
| Users | CRMUser | `IsSalesAdmin(CurrUserId)` else go to `InvalidPermissions` | MyCRM role Admin or SalesAdmin |
| CUserDetails | CRMUser | same | Admin or SalesAdmin |
| UserTerritory | CRMUser | same | Admin or SalesAdmin |
| SetVacant_PopUp | CRMUser | none | any logged-in user who knows the URL |
| Teams (pop-up) | Admin, CRMUser, MyOrdersUser, Registered | none | any logged-in user |
| TeamDetails_PopUp | CRMUser | none | any logged-in user |
| AssignUser_PopUp | CRMUser | none | any logged-in user |
| AdminPage | CRMUser | `Role(GetUserId()) = Role.Admin` else go to `InvalidPermissions` | MyCRM role Admin |
| CUserDetail_PopUp, TeamDetail_PopUp, MonthCoverageDetail_PopUp | CRMUser | none | any logged-in user who knows the URL |
| CompanyGroupNameDetail (pop-up) | Admin, CRMUser, MyOrdersUser, Registered | none | any logged-in user |

`IsSalesAdmin(UserId)`: True when the user has a `CUser` row (any, active or not) with `RoleId` = SalesAdmin or Admin.
`Role(UserId)`: the `RoleId` of the **first** `CUser` row of the user (no filter on `IsActive`, no sort: in practice
the lowest `Id`); `NullIdentifier()` when the user has none.

Inside the screens no button is hidden by role: whoever can open a screen can use all of it.

## 4. Screens: users

### 4.1 Users (flow Admin)

Opened from the menu item "Users". On load: `IsSalesAdmin(CurrUserId)` or redirect to `InvalidPermissions`.

**Search bar**: text box `txtKeyword` (prompt "Name / Username / Email", bound to `Session.Users_SearchKeyword`, so it
is remembered for the session), buttons "Search" (refreshes the list) and "Reset" (empties the keyword, refreshes).
On the right, "Create Vacant User" (confirmation "Are you sure you want to create a TBH user?", 4.1.1).

**List** `CUserTable` (query `GetUsersByIsActive`): every `Users.User`, left-joined to its `CUser` rows and their
`Role`, where the keyword (as `%keyword%`) is found in any of: `User.Name`, `User.Email`, `User.Username` with `RSP\`
/ `rsp\` removed, `CUser.TerritoryCode`, `CUser.SalesmanCode`, `Role.Label`; **and the keyword has at least 2
characters** (with an empty keyword the list is empty). Sorted by `User.Name`. Grouped by the user columns plus
`Role.Label`, `TerritoryCode`, `SalesmanCode`, so a user with several identical `CUser` rows shows once. Shows up to
200 rows; no paging widget was found.

| Column (header as shown) | Content |
|---|---|
| "User" | `User.Name`, bold |
| "Username" | `User.Username` |
| "Email" | `User.Email` |
| "sALESMAN cODE" | `CUser.SalesmanCode` |
| "tERRITORY cODE" | `CUser.TerritoryCode` |
| "Role" | `Role.Label` |
| "Is Active" | active user: a tick image. Inactive user: button "Rename & Set To Vacant User" (opens SetVacant_PopUp for the user; when it notifies, the list is searched again) |
| "CRM User" | `IsCRMUser(User.Id)` (has an active CUser row): "View CRM User", else "Create CRM User"; both open CUserDetails for the user |
| "Territory" | only when `Role(User.Id) = Sales`: button "Territory", opens UserTerritory for the user |

#### 4.1.1 Create Vacant User

Creates a `Users.User` with `Name = "TBH_"`, `Username = "TBH_"`, `Is_Active = False` (no email, no password),
sets the search keyword to "TBH_", refreshes the list and shows "TBH account created, please rename it" (success).
The new user is then renamed with "Rename & Set To Vacant User". A second click before renaming tries to create a
second "TBH_" username (**not certain** what happens; OutSystems usernames are normally unique, so it would fail with
the error shown as a message).

### 4.2 SetVacant_PopUp (flow Admin, pop-up)

Input `UserId`. On load reads the user **only if `Is_Active = False`**; if not found it closes the pop-up and shows
"User not exists" (error). Form `UserForm` on that user:

- "Display Name" → `User.Name` (mandatory)
- "Username" → `User.Username` (not mandatory)

"Save": if the form is valid, `UpdateEntity` on the `Users.User` record, notifies the opener, closes, shows
"Vacant User renamed" (success). "Cancel" closes. Nothing else changes (no `CUser`, no territory).

### 4.3 CUserDetails (flow Admin) — create or edit a person's CRM profile

Input `UserId` (an OutSystems user). On load: `UserId` empty → go to Users; not Admin/SalesAdmin → `InvalidPermissions`.
Reads the user and the **first** of their `CUser` rows into local variables `RoleId`, `Designation`, `PhoneNumber`,
`SalesmanCode`, `TerritoryCode`. Title: the user's name. Link "Back to Users".

Fields (none mandatory on this screen; each change does an Ajax refresh so the block below gets the new values):

- "Role": combo `dpRole`, all `Role` records (label, by `Order`), special entry "-" (no role).
- "Designation", "Phone Number", "Salesman Code", "Territory Code": text boxes.

Below, one block chosen by the role (container `TeamContainer`):

| Role chosen | Block |
|---|---|
| Sales, PP Specialist, CLETrainer | `CUser_Sales` (4.4) |
| else Manager, PP Specialist Manager, MDS Specialist Manager, SOL Specialist Manager, CLETrainerManager | `CUser_Manager` (4.5) |
| else any role except none, Sales, Manager, PP Specialist, PP Specialist Manager | `CUser_Others` (4.6) |
| none ("-") | nothing: a role must be chosen first |

The block receives `UserId`, `RoleId`, `Designation`, `PhoneNumber`, `SalesmanCode`, `TerritoryCode` and does the
saving. When it notifies, CUserDetails refreshes its query.

Button "Delete User And Territory" (style Danger, confirmation "Are you sure you want to delete?"), shown when the
user already has a `CUser` row (**not certain** whether the condition wraps only this button or the area around it):
calls `DeleteCUserNTerritory(UserId)` (11.4), shows "User and territory are delete for <user name>" (success) and goes
to Users.

At the bottom: block `TransferProspects` (UserId) — prospects part, not described here.

### 4.4 Block CUser_Sales (Sales, PP Specialist, CLETrainer)

On load: the user's `CUser` rows (first row gives the pre-selected team and profile), active teams sorted by name,
profiles (see Profile below).

- "Team": combo `dpTeam` — all **active** teams by `Name`, special entry "-". An info icon `btnTeamDetails` next to it
  (shown when a team is selected) opens TeamDetails_PopUp for that team in an info balloon.
- "Profile": combo — MyOrders `Profiles` joined to a second MyOrders entity on `Profiles.MonthlyTarget`, filtered on
  a Boolean attribute = True, label "<name of the joined record> - <MonthlyTarget>" (**not certain** of the MyOrders
  attribute names; the decoder could not name them). Not mandatory.
- "Save": if no team: `dpTeam` invalid with "Required Field !" and focus. Else:
  1. `RemoveAllCUserForReAssign(UserId)` — **deletes** all the user's active `CUser` rows (13);
  2. creates one `CUser`: `UserId`, `RoleId`, `TeamId`, `Designation`, `PhoneNumber`, `SalesmanCode`, `ProfileId`,
     `TerritoryCode` (from CUserDetails); `IsActive` True and `CreatedAt` now by default; `CreatedBy`, `UpdatedBy`,
     `UpdatedAt`, `CId` empty;
  3. notifies CUserDetails, shows "User saved !" (success), goes to Users.

### 4.5 Block CUser_Manager (Manager, PP/MDS/SOL Specialist Manager, CLETrainerManager)

On load: `GetTeamByUserName` = the team(s) whose `Team.Name` equals the user's `User.Name`; the user's `CUser` row in
that team gives the pre-selected team and profile.

- "Team": combo `dpTeam` listing **only the team named after the user** (so a manager can only be put in "his own"
  team), special entry "-"; info icon as in 4.4.
- Link "Create Team" (confirmation "Are you sure you want to create team?"): `CreateTeamByUserId(UserId)` (11.2),
  selects the team it returns, refreshes the combo.
- Link "Team Management": opens the Teams pop-up ("Manage Team", 600×500, 5.1).
- "Profile": as in 4.4.
- "Save": exactly as CUser_Sales (team required "Required Field !", delete all active rows, create one row with the same
  fields, "User saved !", go to Users).

### 4.6 Block CUser_Others (every other role)

On load builds `TeamAssignList`: every **active** team (by `Name`) with `IsAssigned = UserHasTeam(TeamId, UserId)`
(the user has an active `CUser` row in that team).

- "Team": links "Select All" / "Unselect All" (tick or untick every row), then a list of check boxes, one per team
  (team name, info icon opening TeamDetails_PopUp).
- Warning "At least one team has to be assigned" when the local `Error` is True. **It is never True**: the check that
  would set it is disabled (an `If` on the constant True, with the comment "Allow No Team"). A user can be saved with
  no team, which leaves them **with no `CUser` row at all** unless the role is CSST.
- No Profile field.
- "Save":
  1. counts unticked teams (result unused, see above);
  2. `RemoveAllCUserForReAssign(UserId)` — **deletes** all the user's active `CUser` rows;
  3. if the role is CSST: creates **one** `CUser` with `TeamId` = the team of the list's current row (outside a loop:
     normally the first team in the list, ticked or not — **not certain**), plus `UserId`, `RoleId`, `Designation`,
     `PhoneNumber`, `SalesmanCode`, `TerritoryCode`;
  4. otherwise creates one `CUser` per ticked team with the same fields;
  5. `ProfileId` is not set (an existing profile is lost); notifies, "User saved !", goes to Users.

### 4.7 UserTerritory (flow Admin) — entry to a salesperson's territory

Input `UserId`. On load: empty → Users; not Admin/SalesAdmin → `InvalidPermissions`. If the user owns **no**
`Territory` and `Role(UserId) = Sales`, MyCRM **creates a Territory** (`OwnerID = UserId`, `IsActive` True,
`IsTemporary` False, `IsOwner` True) and reloads the screen. Shows the user's name, "Back to Users", "Reset" (clears
the company-territory and company-owner search keywords) and, when the user has a territory, the blocks
`CompanyOwner` and `CompanyTerritories` (territory part, described with Territory).

### 4.8 AssignUser_PopUp (flow Admin)

Opened from the `CompanyTerritories` block ("Assign selected company to another user"). Lists users with an active
`CUser` row of role Sales, shown as "<Name> [Active]" or "<Name> [Inactive]" (from `User.Is_Active`), in a combo
`dpUsers`. "Assign": no user → "Required Field!"; else closes and notifies the opener with the `UserId` (the opener
does the transfer, see `TransferToUSER` in 11.7). "Cancel" closes.

## 5. Screens: teams

### 5.1 Teams (flow Admin, pop-up "Manage Team")

Opened from CUser_Manager's "Team Management". Search box (prompt "Type the Name or Short Name", kept in
`Session.Teams_SearchKeyword2`), "Search", "Reset". Table `TeamTable`, 50 rows per page with page navigation, every
team where `Name` or `ShortName` contains the keyword, sortable by "Name" (default), "Short Name", "Is Active".
Column "Is Active" is a check box bound to `Team.IsActive`; ticking or unticking it **saves at once**
(`UpdateEntity` of the row) and refreshes the list. No create, rename or delete here.

### 5.2 TeamDetails_PopUp and block TeamDetails

TeamDetails_PopUp (input `TeamId`) only shows the block `TeamDetails`: the team name as a heading, "Users assigned to
this team" (italic), then every **active** `CUser` row of the team (left-joined to `User` and `Role`) sorted by
`Role.Order`: the user's name and the block `ColorRole` (the role label in white on a coloured badge). Opened in an
info balloon from the info icons of CUser_Sales/Manager/Others and from the Admin page team list.

`ColorRole` colours (from `RoleColor`): Admin #6A45D8; MD #DF01A5; Head #2EFEC8; Telemarketer #FE2E64;
Manager #FE9A2E; GM, Customer Support, Customer Success Executive, Credit GM #FF00FF; SalesAdmin #01DF3A;
Sales #013ADF; any other role rgb(0,105,149). (`RoleColor`'s document is mixed with another action's status colours
by the decoder; the role mapping above is consistent, the rest was ignored.)

## 6. The Admin page and its pop-ups

### 6.1 AdminPage (flow BackOffice)

Screen role CRMUser; on load, if `Role(GetUserId())` is not Admin → `InvalidPermissions`. Menu item "AdminPage".
Two accordions:

**"Actions"** (mostly unrelated maintenance tools, listed for completeness):

- Block `UploadNoOfEmployees` (7.3): file box + "Upload no of employees".
- "Import Contract MIF": calls `ImportGainLossMIF` (MIF part).
- "Set Territory": `SetTerritoryForUser` then `AssignGlobalCompany(CurrUserId)`. `SetTerritoryForUser` runs
  `SetUser(CurrUserId)` (12.1: re-creates **the admin's own** `CUser` rows from Advance, possibly changing his role)
  and `SetTerritory(CurrUserId)` (territory part). Only ever for the logged-in admin.
- "Custom Imports" heading; link "Manage Territories" → Territories.
- "Range of Date": two date boxes (`ActivityFromDate`, `ActivityToDate`) with no button using them.
- "New Users" / "Import the following": a "MyCRM USER ID" box (`MyCRMUserId_New`) with no button using it.
- "Missing": "CRM Company Id" + button "Company" (`MissingCompany(CRMCompanyId)`, company import from the old CRM);
  "CRM Prospect Id" + "Missing Prospect" (**does nothing**: its action is empty).
- "Delete / Clean / Monitor Expired": "Del Empty Activity Type" (`DeleteLinkWithEmptyActivityType`, activities part).
- "REST API Token": "Generate REST token" (`GenerateAPIToken` then `GenerateToken`, shows the token in `tokenBox`).
- "Get Team Members": "Generate Team Members" copies `Session.UserList` into the box `txtUserList` (to inspect what
  GetSub gave the admin).
- "Generate Address By Postal Code": postal code box + "Generate Address" (`GetAddressByPostalCode`; shows
  "Address :", " Block : ", " Street : ").

**"Generate Data"** (the Users part): three tables, below.

### 6.2 "CRM Users" table

Link "Create a New User" (opens CUserDetail_PopUp with no `CUserId`, title "New User"; on notify, refresh). Search box
(prompt "Type the User, Role or Team", `Session.CUsers_SearchKeyword`), "Search", "Reset". Table `CUserTable`,
50 rows per page with page navigation, query `GetCUsers`: every `CUser` row (active or not) left-joined to `User`,
`Role`, `Team`, where `User.Name`, `Role.Label` or `Team.Name` contains the keyword. No default sort; sortable by
"User" (`{User}.[Name]`), "Role", "Team". The user name is a link opening CUserDetail_PopUp for that row (title
"User"; on notify, refresh). One line per `CUser` row, so an "other role" user appears once per team.

### 6.3 CUserDetail_PopUp (flow BackOffice)

Input `CUserId` (empty for a new row). Form `CUserForm` on that `CUser` row:

- "User": combo of **all** `Users.User` (by name), "-" allowed;
- "Role": all `Role` records;
- "Team": all `Team` records (active and inactive).

None is mandatory. "Save": if the form is valid, `CreateOrUpdate` the `CUser` row with these three values (other
attributes kept; a new row gets `IsActive` True), message "The user was successfully created." / "…updated.", the
pop-up stays open. "Cancel" navigates to AdminPage (RichWidgets closes the pop-up and reloads the page).

This pop-up bypasses everything CUserDetails does (no deletion of the old rows, no check that the user already has a
row, no designation etc.). It is a raw editor of one row.

### 6.4 "Team" table and TeamDetail_PopUp

Button "Create a New Team" (opens TeamDetail_PopUp, title "New Team"; on notify, refresh). Search box (prompt "Type
the Name or Short Name", `Session.Teams_SearchKeyword`), "Search", "Reset". Table `TeamTable`, 50 per page, all teams
where `Name` or `ShortName` contains the keyword, default sort `Name`, sortable "Name", "Short Name". Column
"Action": a link showing the team name (tooltip "Edit '<Name>'"; **not certain** which of the two is the visible text) that opens TeamDetail_PopUp, info icon (TeamDetails_PopUp balloon), and a check box bound
to `Team.IsActive` whose change calls the screen action **`DeleteTeam`**, which only saves the row (`UpdateEntity`):
despite its name it **does not delete**, it switches the team active/inactive.

TeamDetail_PopUp (input `TeamId`): form `TeamForm`, "Name" → `Team.Name`, "Short Name" → `Team.ShortName`, neither
mandatory. "Save": `CreateOrUpdate` Team (a new team is active by default), "Team '<Name>' was successfully created."
/ "…updated.", stays open. "Cancel" → AdminPage.

### 6.5 "Month Coverage" table and MonthCoverageDetail_PopUp

Link "Create a new Month Coverage" (pop-up, title "New Month Coverage"). Search box (prompt "Type the Label",
`Session.MonthCoverages_SearchKeyword`), "Search", "Reset". Table `MonthCoverageTable`, 50 per page, rows whose `Label`
contains the keyword, default sort `Label`, sortable "Months" and "Label", column "aCTION". Each row: the months (no decimals) as a link with tooltip "Edit '<Label>'" (**not certain** which is the visible
text) that opens the pop-up, the label, and button "Delete Month Coverage" (confirmation "Do
you want to delete the item?"), whose screen action `DeleteMonthCoverage` is **empty**: nothing is deleted.

MonthCoverageDetail_PopUp (input `MonthCoverageId`): "Months" (`MonthCoverage.Months`), "Label"; "Save" →
`CreateOrUpdate`, "Month Coverage '<Label>' was successfully created." / "…updated."; "Cancel" → AdminPage.

## 7. Company group names, company groups, number of employees

### 7.1 CompanyGroupNameDetail (flow Admin, pop-up)

Opened from the company detail block (company part). Input `CompanyGroupNameId`. Form: "Group Name" →
`CompanyGroupName.GroupName` (not mandatory). "Save": `CreateOrUpdate`, "Company Group Name '<GroupName>' was
successfully created." / "…updated.", notifies the opener with the new Id and closes. "Delete" (Danger, only for an
existing record, confirmation "Are you sure you want to delete?"): **`DeleteEntity` CompanyGroupName**, "Company
Group Name '<GroupName>' was successfully deleted", notifies "Delete", closes. If a company still uses it the
database refuses (Protect) and the error is shown. "Cancel" navigates to CompanyGroupNameDetail with the same Id
(reloads the pop-up; it does not close it).

### 7.2 Block CompanyGroup (flow Admin) — not used

A filter box, "Filter Group Name", "Select" and a check-box list (`GName`) of group names starting with the typed text;
"Select" refuses more than one ("You can only select 1 Company Group Name.") and notifies the parent with the chosen
`CompanyGroupNameId`. **No screen contains this block.** (`CompanyGroupName_LazyLoad`, flow Company, is the group-name
filter of the company list: company part.)

### 7.3 Block UploadNoOfEmployees (on the Admin page)

File box `Upload1` + "Upload no of employees". Reads the Excel file into a list of structure `NoOfEmployee` (`BRN`,
`NoOfEmployees`). For each row: the **first** active `Company` with that `BRN` (sorted by company name); if found,
sets `Company.NoOfEmployees` and saves the whole company (`UpdateEntity`), counting it. Rows with no matching company
are skipped silently. Ends with "<count> records uploaded successfully". It does **not** fill `PreNoOfEmployees`,
`UpdatedBy` or `UpdatedDate`, and other companies sharing the BRN are not updated. (The `NoOfEmployees` block, flow
Company, is the in-place editor on the company page: company part.)

## 8. GetSub: who sees whose data

`GetSub(UserId)` returns `UserList`, a text "|id||id|…" (each user id wrapped in "|", ids may repeat). Callers test
membership with `Index(UserList, "|" + id + "|") <> -1`. Computed at login into `Session.UserList`; also called
directly by reports, calendars, logs, the telemarketing summaries, `GetCompaniesByUserByName_API` and team/head
selectors on report screens.

Steps:

1. `RoleId = Role(UserId)` (first `CUser` row, see 3.3; `NullIdentifier()` when the user has no row).
2. "My teams" (used by several branches) = the distinct `TeamId` of **all** the user's `CUser` rows with a team
   (no `IsActive` filter).
3. A switch on `RoleId` builds the list (first matching case):

| # | Role(s) | Users added to the list |
|---|---|---|
| 1 | PP Specialist Manager, Specialist Senior Manager | For each of my teams: the users of that team whose role is PP Specialist, MDS Specialist, SOL Specialist, PP Specialist Manager, MDS Specialist Manager or SOL Specialist Manager |
| 2 | Sales, PP Specialist, CLETrainer, ITHelpdesk, SRO, Helpdesk, ServiceEngineer, **or no CUser at all** | Only the user himself |
| 3 | Head | For each of my teams: the users of that team whose role is Sales, Manager, Head or Telemarketer |
| 4 | Telemarketer | (a) every user with an active `CUser` row of role Telemarketer whose OutSystems account is active (sorted by designation); (b) the teams of the user(s) whose name contains **"Jay Choy"** (hard-coded SQL `CAManagers`), except the team whose name contains "Jay Choy": in each, the users with role Sales, PP Specialist or Telemarketer |
| 5 | MDS Specialist, SOL Specialist, MDS Specialist Manager, SOL Specialist Manager | For each of my teams: the users whose role is Sales, CLETrainer, Channel Partner, Telemarketer, Contract, SOL Specialist or MDS Specialist; then **`Session.CurrUserId`** (the logged-in person, not necessarily `UserId`) |
| 6 | Manager | For each of my teams: the users whose role is Sales, CLETrainer, Channel Partner, Telemarketer or Contract; then `Session.CurrUserId` |
| 7 | CSST | Only the user himself |
| 8 | Admin | Every user that has any `CUser` row (active or not) |
| 9 | SalesAdmin, MD, CreditGM, Credit, CreditManager, OPO, StockAllocator | Every user with an active `CUser` row (the role exclusion list in that query — CreditManager, CSMRequester, StockAllocator, Scheduler, Allocator, ITHelpdesk, SRO, Recon, CSST, CSSTManager, Admin — is joined with **or** of "not equal", so it excludes nobody) |
| 10 | CSSTManager | Every user with an active `CUser` row of role CSST or CSSTManager |
| — | any other role (GM, Contract, Channel Partner, NRM, Marketing, Marketing GM, CLETrainerManager, CSMRequester, Allocator, Scheduler, Recon, Credit… not listed above, AP, AP Manager, SMOVE, Presales, Customer Support, Logistics, Service Delivery Manager, Customer Success Executive) | For each of my teams: **every** user of the team with a role (any role) |

4. Finally the user himself is appended ("|UserId|") in every case (so in cases 2 and 7 he appears twice).

Rules and edge cases:

- "Users of a team" means `CUser` rows with that `TeamId` and a non-empty `RoleId`. Except where stated (cases 4a, 9,
  10) **inactive `CUser` rows and inactive OutSystems accounts are included**.
- The comments in the model show MDS/SOL Specialist (Manager) were once in cases 1 and 2 and were moved to case 5.
- A Manager's list includes managers' colleagues only through the team: a manager in two teams sees both.
- Cases 5 and 6 add `Session.CurrUserId`. At login it equals `UserId`; when GetSub is called for someone else
  (report filters for a selected head/team), the logged-in person is added to that person's list.
- `UserCanAccessCompany` gives all-company access to roles in `ShowAllCompany` (Admin, SalesAdmin, MD, CSST,
  CSSTManager, MDS Specialist Manager, SOL Specialist Manager, Head, Credit Manager, Credit GM) and to Telemarketer;
  for the others, company access goes through territories owned by the users in `Session.UserList`.

## 9. Other "who is under whom" actions

These return lists for drop-downs and filters. All read `CUser`; none writes.

- **`GetSubList(UserId)`** → `UserList` (list of `Users.User` records, sorted by name). Switch on `Role(UserId)`:
  SalesAdmin, MD → users with an active `CUser` (same no-op exclusion list as GetSub case 9); Sales, PP/MDS/SOL
  Specialist, ITHelpdesk, SRO, Helpdesk, ServiceEngineer or no role → only himself; Telemarketer, Marketing,
  Marketing GM → users with an active `CUser` of role Telemarketer, Marketing, Sales or Marketing GM; PP/MDS/SOL
  Specialist Manager, Specialist Senior Manager → users of my teams with a specialist or specialist-manager role;
  Admin, Head → **all** OutSystems users; Manager → users of my teams with role Sales, Manager or Telemarketer;
  otherwise → users of my teams with role Sales, Manager or Telemarketer. In every branch inactive accounts are left
  out when site property `DropdownShowActiveOnly` is True. If the user himself is not in the result he is added.
  Used by `GetCRMUsers`, `Calendar`, `CalendarBlock`.
- **`GetSubListForSalesOnly(UserId)`** → users list. Sales, PP Specialist, Customer Support, Customer Success Executive,
  SOL/MDS Specialist → himself; Admin, SalesAdmin, Telemarketer, Marketing, MD → Sales users (query `GetUsers`;
  **not certain** of its exact filter); Manager, PP Specialist Manager → Sales users of my teams; SOL/MDS Specialist
  Manager → users of my teams (filter not fully decoded); otherwise → Sales users of my teams. Used by the report
  filters (`ReportGenerator`, `ReportFilterUser`, head/team selectors).
- **`GetSubListAdmin()`** → "|id|…" of every user having a `CUser` row with role Sales, Manager or PP Specialist
  (active or not). Used by `ReportGenerator`, `ReportFilterUser`.
- **`GetSubSalesman()`** → list of {`Id`, `Display`} for users in `Session.UserList` with an active `CUser` of role
  Sales, PP Specialist, SOL Specialist or MDS Specialist; `Display` = name + " [Inactive]" when the account is
  inactive; sorted by `Designation`.
- **`GetSalesmanSub()`** → if `Role(CurrUserId)` is Sales: "|me|" plus every user with an active `CUser` whose role
  label is "Telemarketer"; otherwise `Session.UserList`. Used by the activity and prospect lists.
- **`UserListByRole(UserRoles)`** (`UserRoles` = "|roleId|roleId|…") → "|id|…": if PP Specialist is among the roles,
  first every user in MyOrders `PPSpecialist`; then every user with an active `CUser` whose role is in `UserRoles`,
  sorted by designation. Used by `Prospects`, `ITSProspects_`.
- **`TeamUsers()`** → "|id|…" of active Sales users of the teams in which `Session.SelectedManagerUserId` has an active
  `CUser` row with role Manager. Used by pipeline blocks.
- **`GetCRMUsers()`** → `GetSubList(GetUserId()).UserList` (a disabled branch would return all active users).
- **`GetHead(UserId)`** → `HeadId`: the first active Sales/Manager row of the user gives a team; the first `CUser` of
  that team with role Head. Empty user → empty. **Not called anywhere.**
- **`GetManagerId(UserId)`** → `ManagerId`: the first active `CUser` of the user with role Sales, PP Specialist, SOL
  Specialist or MDS Specialist gives a team; the first active `CUser` of that team with role Manager, PP Specialist
  Manager, SOL Specialist Manager or MDS Specialist Manager gives the manager. Only the first team counts. Used by
  approval screens (Approve, Reject, Save, Upload actions elsewhere in MyCRM).

## 10. Role, team and user helper actions

- **`Role(UserId)`** → `RoleId` of the first `CUser` row (3.3). Public; used in about 100 places.
- **`UserHasRole(UserId, RoleId)`** → True if the user has an active `CUser` with a role, and that role is `RoleId`
  (or `RoleId` is empty). Used by `MyDashboard`.
- **`UserHasTeam(TeamId, UserId)`** → True if the user has an active `CUser` in that team. Used by CUser_Others.
- **`IsSalesAdmin(UserId)`** → any `CUser` row with role SalesAdmin or Admin.
- **`ShowAllCompany(RoleId)`** → True for Admin, SalesAdmin, MD, CSST, CSSTManager, MDS Specialist Manager, SOL
  Specialist Manager, Head, Credit Manager, Credit GM.
- **`IsCRMUser(UserId)`** → the user has at least one active `CUser` row.
- **`IsActiveUser(UserId)`** → a `Users.User` with that Id and `Is_Active = True` exists (used every request, 3.1).
- **`UserIsActive(UserId)`** → same test, separate action (used by one block).
- **`GetCUserTeamName(UserId)`** → `Team.Name` of the user's first `CUser` row (no `IsActive` filter). Used by
  `TerritoryMovement`.
- **`UserDesignation(UserId)`** → `Designation` of the user's first active `CUser` with a non-empty designation
  (sorted by designation). Used by `PDFTonerCollection`.
- **`GetADByUserId(UserId)`** → `User.Username` with "RSP\" and "rsp\" removed (the Advance / AD logon).
- **`GetUserRole(UserId)`** (Advance, legacy, 12.2) and **`GetUserTeams(UserId)`** (12.3).
- **`RoleColor(RoleId)`** → badge colour (5.2).

## 11. Actions that write CUser, Team or Users.User

### 11.1 Summary of every write

| Entity | Operation | Where |
|---|---|---|
| CUser | **Delete** (each active row of a user) | `RemoveAllCUserForReAssign`, called by CUser_Sales/Manager/Others Save and by `DeleteCUserNTerritory` |
| CUser | Create | CUser_Sales Save, CUser_Manager Save, CUser_Others Save |
| CUser | Create or update | CUserDetail_PopUp Save, `SetUser`, `CreateProfileFor_NewCSST`, `CreateProfileFor_NewCSST_byUserId` |
| CUser | SQL `UPDATE … SET CId` | `UpdateCUserCId` (not called) |
| Team | Create | `CreateTeamByUserId` |
| Team | Create or update | TeamDetail_PopUp Save, `Team` timer |
| Team | Update (`IsActive`) | Teams pop-up check box, Admin page "Action" check box (`DeleteTeam`) |
| Team | **Delete** (all teams) | `Team` timer |
| Users.User | Create | Users "Create Vacant User" |
| Users.User | Update | SetVacant_PopUp Save |
| CompanyGroupName | Create or update / **Delete** | CompanyGroupNameDetail Save / Delete (and the imports) |
| MonthCoverage | Create or update | MonthCoverageDetail_PopUp Save |
| VacantUsers | — | never written or read |

### 11.2 CreateTeamByUserId(UserId) → TeamId

Reads the user; looks for an **active** team whose `Name` equals the user's `Name`. If none: creates a `Team` with
`Name` = the user's name (`ShortName` empty, `IsActive` True), shows "Team <b><name></b> created" (success), returns
the new Id. If one exists: shows "Team <b><name></b> already exists" (error style) and returns its Id.

### 11.3 RemoveAllCUserForReAssign(UserId)

Deletes (`DeleteEntity`) every `CUser` row of the user with `IsActive = True`. Inactive rows are left. Called before
every save on CUserDetails, so **saving a person always replaces their rows with new ones** (new Ids; `CId`,
`CreatedBy`, `UpdatedBy`, `UpdatedAt` lost; `CreatedAt` reset; `ProfileId` lost for "other" roles).

### 11.4 DeleteCUserNTerritory(UserId) — "Delete User And Territory"

1. `RemoveAllCUserForReAssign(UserId)` (deletes the active `CUser` rows);
2. for each active `Territory` with `OwnerID = UserId`: every active `CompanyTerritory` of it gets `IsActive = False`
   (saved one by one), then the territory gets `IsActive = False`;
3. `Audit.Log(CurrUserId, module name, "Territories for <user name> are deleted")`.

The OutSystems user itself is not touched. Territories and company links are only marked inactive.

### 11.5 SetUser(UserId) — legacy, from Advance

`ADName = GetADByUserId(UserId)`; reads the Advance user (`Users 2`, `User_Logon = ADName`, not disabled, not deleted)
for its territory profile (not used further); `TeamList = GetUserTeams(UserId)`; for each team in the list: the
user's `CUser` row in that team (or a new one) gets `RoleId = GetUserRole(UserId)`, `UserId`, `TeamId` and is created
or updated. Existing rows in other teams are left as they are. Called only through `SetTerritoryForUser` (Admin page
"Set Territory", for the logged-in admin).

### 11.6 UpdateCUserCId(UserId) and UpdateAllCUserCId()

`UpdateCUserCId`: looks up the old CRM's `CRMUsers` row whose trimmed `User_Logon` equals the user's `Username`
without "RSP\"/"rsp\" (sorted by `User_CreatedBy`); if found, runs `UPDATE {CUser} SET [CId] = @CId WHERE [UserId] =
@UserId` with its `User_UserId` (all the user's rows). `UpdateAllCUserCId` calls it for every user with an active
`CUser`. **Neither is called by anything** (a one-off migration from the old CRM).

### 11.7 CSST profiles and transfers

- **`CreateProfileFor_NewCSST(email, role)`**: finds the active OutSystems user with that `Email` and a login
  (`Last_Login` set; sorted by last login and creation date); if `Role(user)` is empty, CSSTManager or CSST, the
  user's first active `CUser` (or a new one) gets `UserId`, `RoleId = role`, `IsActive` True, `CreatedBy` (= current
  user, only for a new row), `UpdatedBy`, `UpdatedAt` now, `Designation` = "Customer Service Supplies & Admin Officer"
  (CSST) / "Customer Service Supplies & Admin Manager" (CSSTManager) / empty, and is saved; otherwise warning "User
  already have a Role and it's not CSST." Called by the screen action `AddCSSTUser` (CSST part).
- **`CreateProfileFor_NewCSST_byUserId(UserId, role)`**: the same without the e-mail lookup and without the role
  check. Called by the timer action `CSST_New` (CSST part).
- **`TransferToUSER(CompanyId, ToUserId)`** (territory part, listed because it uses AssignUser_PopUp): takes the first
  active permanent territory of `ToUserId` (none → warning "The selected salesperson have no permanent territory.")
  and creates or updates the company's `CompanyTerritory` row for it (`IsActive` True, `CreatedBy` = current user).

## 12. Provisioning from Advance (legacy) and the Team timer

In 2016–2017 MyCRM built teams and roles from the Advance user table. These actions remain but are reached only by
the Admin page "Set Territory" button (for the admin himself) and by the `Team` timer, which has no schedule.

### 12.1 SetUser — see 11.5.

### 12.2 GetUserRole(UserId) → RoleId

From Advance `Users 2` (`User_Logon = GetADByUserId(UserId)`, not deleted, not disabled):
logon "jchoy" or "stoh" → Head; `User_TerritoryProfile` 19 (and not those two) → Manager; profile 26 →
Telemarketer; profile 1 → SalesAdmin; anything else → Sales.

### 12.3 GetUserTeams(UserId) → TeamList

`ADName = GetADByUserId(UserId)`. If `GetUserRole(UserId)` is Sales: SQL `GetTeamByAD` returns the **one** Advance
manager (profile 19, active) whose territory range contains the user's primary territory, the deepest one (highest
`Terr_TerritoryID`). Otherwise SQL `GetTeamByAD2` returns every manager whose territory lies between the user's primary
territory and its range end (the managers under him). Each result row is (`NULL`, last name, logon, 1); MyCRM then
finds the `Team` whose `ShortName` equals the logon and puts its `Id` in the row. Both queries skip territory
-2147483640.

### 12.4 Timer "Team" (action `Team`, no schedule, timeout 20 min)

Runs only when started by hand in Service Center. **Deletes every `Team` row** (one by one, sorted by name), then
creates one team per Advance manager: `Name` = RTRIM(last name), `ShortName` = RTRIM(logon), `IsActive` 1, for users
with `User_TerritoryProfile = 19`, not disabled, having more than one active Advance user whose primary territory
lies in their territory range. Because `CUser.TeamId` is Protect, the first delete of a team still referenced by a
`CUser` row fails and the whole run rolls back. It must not be reproduced.

## 13. What MyCRM deletes, and what it only marks inactive

**Deleted (rows removed):**

| Entity | Where | When |
|---|---|---|
| `CUser` | `RemoveAllCUserForReAssign` | Every "Save" in CUser_Sales, CUser_Manager, CUser_Others (all active rows of the person, before new ones are created) |
| `CUser` | `DeleteCUserNTerritory` → `RemoveAllCUserForReAssign` | "Delete User And Territory" on CUserDetails |
| `Team` | `Team` timer | Every team, when the timer is run by hand (fails while any team is in use) |
| `CompanyGroupName` | CompanyGroupNameDetail "Delete" | On confirmation; fails if a company uses it |

**Only marked inactive:**

- `Team.IsActive` — Teams pop-up check box and the Admin page check box (`DeleteTeam`).
- `Territory.IsActive` and `CompanyTerritory.IsActive` — "Delete User And Territory".
- `Users.User.Is_Active` — never changed by MyCRM (vacant users are created inactive).
- `CUser.IsActive` — never set to False by MyCRM; nonetheless many queries filter on it (inactive rows may exist from
  before, or from other tools).

**Looks like a delete but is not:** "Delete Month Coverage" (does nothing), `DeleteTeam` (saves `IsActive`).

## 14. What the rest of MyCRM depends on

- **`Session.UserList`** (GetSub at login) decides what every person sees: companies (`UserCanAccessCompany`, company
  lists, group-name filter), activities, prospects, pipeline, calendars, reports, logs. Any change to `CUser` roles or
  teams must keep GetSub's result identical, and people only see the change after logging in again.
- **`Role(UserId)`** and `CUser.RoleId` are tested in about 100 screens and actions (CSST quotes and pricing,
  approvals, dashboards, reports, OAA forms, projects, training, sales goals…). The Role Ids must stay the production
  Ids, and the first `CUser` row of a person must carry the right role.
- `CUser.TeamId` and `Team` are read by the head/team/manager selectors of reports and pipeline
  (`OnHeadChange`, `OnTeamChange`, `TeamUsers`, `GetManagerId`, `ManagerName`, `ReportFilterUser`, summaries).
- `CUser.Designation`, `PhoneNumber` are printed on quotes and documents (`UserDesignation`, CSST templates, toner
  collection PDF) and shown in user lists sorted by designation.
- `CUser.SalesmanCode` and `TerritoryCode` are shown on the Users screen and used by imports and reports.
- `CUser.ProfileId` links to MyOrders sales targets (`Profiles`).
- `CUser.IsActive = True` is the test for "is a CRM user" (`IsCRMUser`, many lists).
- `Team.ShortName` is matched against the Advance logon by `GetUserTeams` (legacy).
- `MonthCoverage` is read by the MIF screens; `CompanyGroupName` by the company list, company detail and imports.
- Other applications: MyOrders (`MyOrdersRole`) and others read `CUser`/`Role` (see README section 9).

## 15. UI that could be friendlier

- Users: nothing shows until 2 characters are typed, and there is no paging (200 rows max). A plain list with a filter
  and paging would be clearer. Column headers "sALESMAN cODE", "tERRITORY cODE" (Admin page "aCTION") have inverted
  capitals.
- Two separate editors for the same data: CUserDetails (Users screen) and the raw CUserDetail_PopUp (Admin page), with
  different rules. One editor is enough.
- Saving a person deletes and re-creates their rows, losing history; editing in place would keep `CId`, creation
  data and the profile.
- CUser_Others' "At least one team has to be assigned" can never appear; a user can be saved with no team and then
  silently has no CRM profile at all.
- A Manager can only be put in the team that has his exact display name, and must click "Create Team" first; renaming
  the OutSystems user breaks the link.
- The Admin page mixes unrelated developer tools (token generator, postal-code test, unused date and id boxes, a
  "Missing Prospect" button that does nothing) with user administration.
- "Delete Month Coverage" and "DeleteTeam" are misleading names/buttons; team activation is a bare check box without
  a label.
- Pop-ups stay open after "Save" (CUserDetail_PopUp, TeamDetail_PopUp, MonthCoverageDetail_PopUp) and "Cancel"
  reloads the whole Admin page.
- Role badges and the role list show "SOL Specialist" while the flows call it MITS; one name would help.
- Team changes only take effect after the person logs in again.

## 16. Open questions

1. The production `Id` of each `Role` record (needed to read `CUser.RoleId`); the model does not contain them.
2. Whether any `CUser` rows with `IsActive = False` exist in production, and how they got there (MyCRM never sets it).
3. The MyOrders `Profiles` attributes used by the Profile drop-down (filter flag and label source).
4. The exact filter of `GetSubListForSalesOnly`'s Admin/SalesAdmin/Telemarketer/Marketing/MD branch and of its
   SOL/MDS Specialist Manager branch.
5. Which `TeamId` CUser_Others gives a CSST user (the list's "current" row after a loop).
6. Whether the condition "user has a CUser row" on CUserDetails wraps only the "Delete User And Territory" button.
7. Who holds the OutSystems role `Admin`, and whether the menu hides "Users" / "AdminPage" from other people (the menu
   block was not part of this reading).
8. Whether "Create Vacant User" twice in a row fails on the duplicate username "TBH_".
9. Whether the "Jay Choy" teams in GetSub's Telemarketer branch still exist (hard-coded name).
