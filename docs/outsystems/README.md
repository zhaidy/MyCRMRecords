# How MyCRM's Users, Territory and Company part works

MyCRMRecords takes over the **Users**, **Territory** and **Company** part of the OutSystems application **MyCRM**
(a Traditional web module, eSpace `MyCRM`, tables `OSUSR_824_…` in the OutSystems database). A company's addresses,
contacts, phones and emails come with it. **The rest of MyCRM stays in OutSystems and keeps being used**: activities,
leads, prospects, projects, OAA forms, MIF, training events, sales goals, the dashboard and the reports.

This document is what MyCRMRecords keeps of MyCRM's behaviour. Service Studio could not answer any request about MyCRM
(the module is too big: every request timed out after 60 seconds), so MyCRM was read from its exported model
(`MyCRM_v1037.oml`, decoded with the MyApps project's `oml-reader.py` on 2026-10-19) and its data model (OSSYS). The
behaviour of each area is described in detail in:

- [users.md](users.md): users (CUser), MyCRM's roles, teams, the Admin page, and `GetSub` (who sees whose data).
- [territory.md](territory.md): territories (which salesperson looks after which company), `UserCanAccessCompany`.
- [company.md](company.md): companies, addresses, contacts, phones and emails, the `Pre…` columns, the import timers.

This page is the overview: what moves, where it meets the rest of MyCRM, the rules MyCRMRecords changes on purpose
and what to switch off in MyCRM.

## Contents

- [1. What moves and what stays](#1-what-moves-and-what-stays)
- [2. Where the part meets the rest of MyCRM](#2-where-the-part-meets-the-rest-of-mycrm)
- [3. Users](#3-users)
- [4. Territory](#4-territory)
- [5. Company](#5-company)
- [6. Addresses, contacts, phones and emails](#6-addresses-contacts-phones-and-emails)
- [7. The "Pre…" columns](#7-the-pre-columns)
- [8. Screens, roles and timers](#8-screens-roles-and-timers)
- [9. Other applications that read these tables](#9-other-applications-that-read-these-tables)
- [10. What MyCRM deletes](#10-what-mycrm-deletes)
- [11. Questions still open](#11-questions-still-open)
- [12. Rules MyCRMRecords changes on purpose](#12-rules-mycrmrecords-changes-on-purpose)
- [13. Production counts that shaped the design (2026-10-19)](#13-production-counts-that-shaped-the-design-2026-10-19)
- [14. Switching over: what to switch off in MyCRM](#14-switching-over-what-to-switch-off-in-mycrm)
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

A person's MyCRM profile is `CUser`, **one row per (person, team)**, with MyCRM's `Role` (45 static records), the
team, designation, phone number, salesman and territory codes, `CId` (the old CRM's user Id) and `ProfileId` (MyOrders'
sales target profile). Teams are named after their manager. MyCRM's `Role(user)` is the role of the person's **first**
row (active or not); `IsSalesAdmin` is any row with Admin or SalesAdmin; `GetSub` builds, at sign-in, the list of
people whose data the person sees, from the role and the teams of all their rows. MyCRM's user page **deletes** all a
person's rows at every save and creates new ones. `VacantUsers` is never used: a "vacant user" is an inactive
OutSystems user renamed "TBH_…". Details: [users.md](users.md).

## 4. Territory

A `Territory` row is "this user holds territory number N"; a `CompanyTerritory` row puts a company in it. Permanent
rows were rebuilt from Advance by a daily timer that only ever adds rows (hence millions of inactive rows); temporary
rows give project members (Projects, CSST, the telemarketer) access to the project's companies. The salesperson ("owner")
of a company is worked out: the user of its first active link to an active permanent territory. `UserCanAccessCompany`
lets some roles open every company, and everyone else the active companies of the active territories (permanent **or
temporary**) of the people in their `GetSub` list. Each change of salesperson is in `TerritoryMovement`.
`TerritoryCode` (`TempTerritoryCode`), `TerritoryRelationship` and `CompOwner` are read by nothing. Details:
[territory.md](territory.md).

## 5. Company

`Company` (52 attributes) is edited on one admin form (CompanyDetail, Sales Admins: name, BRN, website, flags, customer
type, Advance customer type, group name, account type, ownership, remarks), plus the number of employees and the sales
remarks on the company page (anyone who opens it). Industry, phone, fax, paid-up capital, MIF potential and revenue are
shown or stored but edited nowhere. `CustomerNo` comes from Advance (timer `UpdateCustomerNo`). The old-CRM Id
columns (`CRMCompanyId`, …) were filled by import timers from the old CRM. Relationships are parent → subsidiary
(type Parent). Details: [company.md](company.md).

## 6. Addresses, contacts, phones and emails

A company's addresses (`CompanyAddressLink` → `Address`, one default), contacts (`CompanyContactLink` → `Contacts`, one
primary contact), and each contact's phones and emails (always of a contact; MyCRM fills the company too).
`AddressContactLink` is never read or written (empty). Details: [company.md](company.md), sections 3.13 to 3.20.

## 7. The "Pre…" columns

They hold **the values a record had just before its last edit on a MyCRM screen** (a one-step history): written by the
company form (all 19, existing companies only), the address and contact editors, and the deletes (`PreIsActive = 1`).
No logic reads them; lists that stay in MyCRM **sort** by `PreCompanyName`. MyCRMRecords fills them exactly as MyCRM.
Details: [company.md](company.md), section 2.

## 8. Screens, roles and timers

Every screen, block, server action and timer of the part is described in the three documents: users.md sections 3 to
12, territory.md sections 4 to 6, company.md sections 3 to 5. What MyCRMRecords does with each is in the README's
*What it does* table, and the timers to switch off are in section 14.

## 9. Other applications that read these tables

- **MyFile** reads `Company`, `CUser`, `Role`, `Territory` and `CompanyTerritory` (company names, BRN look-up, a
  person's MyCRM role, MyCRM's access rule since MyFile's pull request 4).
- **MyCustomers** matches BRNs between Advance and MyCRM's companies.
- **MyCRMMSD** (the "MSD Sales Rep" on the company page) and the rest of MyCRM.

They keep reading MyCRM's tables, which the sync keeps up to date.

## 10. What MyCRM deletes

`CUser` (every save of a person), `CompanyRelationship` (the red crosses), `CompanyAddressLink` (with an address),
`CompanyGroupName`, `CompanyTerritory` (only the developer button "Set Territory"), `TempCompanies` (a scratch list),
and every `Team` (the legacy timer, which always fails). Everything else is only marked inactive. MyCRMRecords deletes
nothing, and marks what it removes in MyCRM (`ISACTIVE = 0` where the table has it): see rule 3 and rules 11 to 13 below.

## 11. Questions still open

1. The real schedules of MyCRM's timers in Service Center (the model has almost none): which still run (section 14).
2. Whether the old CRM still exists (its Id columns are kept and synced as they are).
3. MyOrders' `Profiles` attributes behind the Profile drop-down: only needed if the choice is wanted later (not for
   now, decided 2026-10-19); MyCRMRecords keeps a person's `ProfileId` as it is.
4. The Role Ids in production (MyCRMRecords matches roles by label, so it does not need them).

## 12. Rules MyCRMRecords changes on purpose

1. **Territories are created and assigned by Sales Admin, not built from Advance.** MyCRM built its territories from
   Advance (`Users 2`, `Territories`, `Company 2`) with `SetTerritory_Update` and related actions; that is deprecated
   (decided 2026-10-19). In MyCRMRecords, Sales Admin creates **named territories** (a name and a number), puts
   companies in them and gives each one a salesperson; giving a territory to someone else moves all its companies,
   and each company's move is recorded in `TerritoryMovement`. MyCRM's tables keep working as before: each territory is
   one active `Territory` row (`Territory` = the number, `OwnerID` = the salesperson, `IsTemporary` = 0) with one
   `CompanyTerritory` row per company, so MyCRM's `UserCanAccessCompany`, its lists and MyFile read them unchanged. The
   name is kept in MyCRMRecords only (MyCRM's table has no column for it). MyCRM's Advance territory actions and timers
   are to be switched off in OutSystems before MyCRMRecords' territories are used.
2. **Only the permanent territories move.** The temporary territories MyCRM's Projects and CSST parts create (31,898
   active, with about a million company links on 2026-10-19) stay MyCRM's: MyCRMRecords reads them only to decide who may
   see a company, and never copies them. Since 2026-10-30 the **projects'** temporary territories are made and taken
   away by MyCRMRecords' Projects pages (rule 23); the CSST part's are still never changed. The 4 million inactive territory rows left by the Advance rebuild
   are neither imported nor touched.
3. **A person's CUser records are updated, not deleted and made again.** MyCRM's user page deletes all of a person's
   CUser rows at every save and creates new ones (losing `CId`, created/updated data and `ProfileId`); MyCRMRecords
   changes the rows in place, adds rows for new teams and marks the others inactive.

4. **MyCRM's import timers from the old CRM are not carried over** (decided 2026-10-19): `Company`, `CompanyDaily`,
   `Contact`, `ContactLink`, `Address`, `CompanyAddressLink`, `Phone`, `Email`, `ImportUpdateContact`,
   `ImportUpdateContactLink`, `ImportLG`/`LMA`/`GMA`/`SME` and their `UpdateCompany` variants, `UpdateCompanyCRMIdByBRN`,
   `UpdateCompanyCRMIdByName`, `UpdateOldCRMCompanyId`, `UpdateAdvanceTypeCode`, `BulkUpdateCustomerType`,
   `ChannelPartnerCompanies`, `MissingCompany`. Only **`UpdateCustomerNo`** (Advance's customer number into
   `Company.CustomerNo`, daily at 02:45) is kept, as a scheduled task of MyCRMRecords. The old-CRM Id columns
   (`CRMCompanyId`, `CId`, `CRMAddressId`, …) are kept and synced as they are, never filled by MyCRMRecords.
5. **MyCRM's bugs in this part are fixed** (decided 2026-10-19): re-saving the default address with "Is Default"
   ticked is accepted (MyCRM refused it: "There is already a default address for this company"); every save records who
   changed the record and when (MyCRM's number-of-employees and sales-remarks saves did not); the phone error says
   "Invalid phone number" (MyCRM: "Invalid Email").
6. **A duplicate BRN is refused on new and edited companies** (MyCRM checked new companies only); a company marked
   "Is HOME USER" still skips the check, as in MyCRM (decided 2026-10-19).
7. **The `Pre…` columns are filled exactly as MyCRM fills them** (the values just before the last edit, on the saves
   that write them in MyCRM), because lists that stay in MyCRM sort by `PreCompanyName`.

8. **No vacant ("TBH") users are made.** MyCRM created and renamed inactive OutSystems users to park a leaver's
   territories; MyCRMRecords never writes OutSystems' user table. A territory with no salesperson is simply vacant
   (`OwnerID` empty in MyCRM), and existing TBH users still show as salespeople where MyCRM has them.
9. **One rule moves a company.** Putting a company in a territory takes it out of every other territory (as MyCRM's
   bulk transfer screen did; its "take from the current owner" block took it from the first owner only, and its
   reassign block from this user only). A movement is recorded only when the salesperson changes. Giving a territory
   to another salesperson moves all its companies, each recorded. A company put back in a territory gets a new link
   (an old link is never made active again).
10. **One editor for a person's CRM profile, editing in place** (rule 3), listing every CRM user without typing (MyCRM
    showed nothing before two letters, and at most 200). The raw "CRM Users" editor of MyCRM's Admin page is not
    carried over. MyCRM's team rules per role are kept: Sales, PP Specialist and CLETrainer one team; the managers'
    roles the team named after them (with *Create team*); CSST one team or none; other roles any number. A person
    saved with no team is no longer a CRM user, as in MyCRM, but the page now says so before saving (MyCRM's warning
    could never show). The MyOrders profile is kept as it is but not offered (not needed for now, decided 2026-10-19).
11. **A CRM user record taken away keeps no team, and no role once the person has no team left.** MyCRM reads every
    record's role and team whatever its `IsActive` (`Role()`, `IsSalesAdmin`, `GetSub`), so an inactive record keeping
    its team or role would change what MyCRM shows; emptied, MyCRM reads exactly what it read when the records were
    deleted. The team is kept here (`FormerTeamId`).
12. **Relationships are marked, not deleted, and cannot loop.** A removed relationship is `IsActive = 0` in MyCRM
    (MyCRM deleted it; its screens read only active ones). A company can no longer be related to itself, nor be put
    below a company that is below it (MyCRM allowed both).
13. **A deleted address keeps its link in MyCRM**: the address is inactive, as MyCRM does, but the `CompanyAddressLink`
    row MyCRM deleted is left there (MyCRMRecords never deletes in MyCRM; MyCRM's screens read only active addresses).
    A removed group name is inactive here and left in MyCRM (no column to mark it); removing one still used by a
    company is refused with a message (MyCRM showed the database error).
14. **The bulk list of companies** (MyCRM's TerritoryUpdate): pasted or from Excel; an empty BRN no longer matches
    every company with an empty BRN; companies already in the territory are shown instead of skipped silently; the
    list is kept on the page (MyCRM's `TempCompanies` scratch table is not used).
15. **MD keeps the territories like a Sales Admin** (MyCRM let MD only use the bulk transfer screen), and **only
    administrators sign in as someone else**.
16. **Not carried over**: the postal-code look-up from Google on the address form (not wanted, decided 2026-10-19); the company list's contract-date filters (they read `ExpireMachine`, which stays in MyCRM); the
    caretakers from projects and the MSD sales rep on the company page (Projects and MyCRMMSD stay in MyCRM); the
    recent-items log; MyCRM's raw Territories list and TerritoryDetail flags (replaced by the named territories); the
    Admin page's developer tools and Month Coverage (MIF, stays in MyCRM); "Customer code" falls back no more to a live
    look-up in Advance by name (it shows `CustomerNo`, which the scheduled task fills); `AddressContactLink` (never
    used) is not read; the push to the old CRM (switched off in MyCRM).
17. **No copy, no sync: MyCRM's tables are read and written where they are** (decided 2026-10-19). MyCRMRecords first
    copied this part of MyCRM and synced it both ways; the first sync pairs ~700,000 records and the territory tables
    hold millions of rows. Now its pages read and write MyCRM's own tables (`MyCrmStore`), so MyCRM, MyCRMRecords and
    the apps reading MyCRM's companies see a change at once. Writing waits for an administrator's switch; nothing is
    deleted there; every value written is recorded; what MyCRM has no column for (a territory's name, who took something
    away and when, a former team, a company address link or group name taken away) is kept by MyCRMRecords by MyCRM's Id.
18. **MyOrders' costing sheets and request forms are here too, read only** (2026-10-30, merged from MyOrdersRecords):
    how MyOrders' part works and the rules changed for it are in [myorders/README.md](myorders/README.md) (section 9)
    and the `myorders/request-forms-*.md` files.

19. **MyFile's files are shown here too, read only** (2026-10-30): the Files page and a company's Files tab read
    MyFiles' `File` and `FileType` entities where MyFile keeps them (the *MyFiles module* setting), a file's size as
    `DATALENGTH` and its bytes only for one download. Who sees which follows MyFile's rule with MyCRMRecords' company
    rule (`Access`, so territories and MyCRM's projects count): active files only; the roles that open every company
    see every file; everyone else the files of the companies they may open and the files of no company. Differences
    from MyFile: **MyFile's department rule is not applied** (people there see only their Active Directory department's
    files and files of no department): MyCRMRecords does not read a person's AD department, so everyone sees the files
    of every department; the search needs no minimum of three characters and does not look in serial numbers or tags;
    the list is always newest first; a zip holds at most 50 files.

20. **The company list shows a salesperson, territory and customer type, and can show "Mine" or "No salesperson"**
    (2026-10-30). MyCRM's CompanyList_Table showed Company, Postal Code, BRN and Created By, and showed no list before
    Search; MyCRMRecords shows the first page at once, one line per company, and adds the salesperson and territory
    (MyCRM's GetCompanyOwner) and the customer type. Those who open every company also choose **Mine** (the active
    companies of the active permanent territories of the people in their `Session.UserList`: their own, and by their
    role their team's; MyCRM's project territories don't count) or **No salesperson** (MyCRM's admin panel
    "Unassigned Companies": active companies with no active link to an active permanent territory, a vacant territory
    counting as a territory). With No salesperson, those who keep territories tick companies and put them in a
    territory with the Territories page's own change (rule 9), from the list instead of from a user's territory.
21. **MyMachines' machines are shown here, read only, by MyCRMRecords' company rule** (2026-10-30): the Machines page, a
    machine's page and a company's Machines tab read Advance's `daily_mif`, `serial`, `customer`, `meter_card`,
    `Service`, `SMeter_History` and `daily_TM` through AdvanceConnector (the table names are settings; every column by
    its real name through `ExternalTable`), with MyMachines' own queries' logic (coverage from mif-type and the contract
    and warranty dates; finance type from `serial.[Serial-stat]`: SALIL, SALEL, SALCAS, SALDLS). Differences from
    MyMachines and MyCRM, decided by the user:
    - **Who sees which machines** is MyCRMRecords' company rule (`Access`: territories with the person's list, MyCRM's
      project and CSST territories, the roles that open every company), by the company's BRN, **plus MyMachines'
      temporary access** (a `TempAccess` row of the person with the customer's BRN or name). MyMachines' own rule (its
      specialist and marketing roles seeing every machine; "MyCRM says the user owns the company") is not used. The
      roles that open every company see no machine until they type 3 characters.
    - **Giving and removing temporary access** writes MyMachines' `TempAccess` only when *Let MyCRMRecords change
      MyMachines' temporary access* is on, recorded in `dbo.CrChange` (table `MyMachines.TempAccess`). `TempAccess` has
      no IsActive, so a row removed is **marked**, not deleted: "X:" goes before its CustomerName and BRN, which then
      match no customer (MyMachines compares them with the customer's), and the row is no longer listed. A row given
      here stores the MyCRM company's name and BRN.
    - The company Machines tab keeps MyCRM's rule (BRN, contract or warranty ending after today) as **Active**, and adds
      **All**. A coverage is **Expired** when the contract or warranty has ended, **No contract** otherwise.
    - Service calls are filtered by the machine's customer, and by its contract only when it has one (MyMachines always
      filtered by both). **Not shown**: technicians' names (in the Datacenter database's `tech_team`, not connected),
      the parts of a toner order (`SL_Parts_List2`, a function in Advance whose body is not known), MyMachines' charts
      from `Meter_Bill_History`, `MIF_TDV`, `MIF_Summary` and `Rev_Summary`, its "Wrong BRNs"/"Missing BRNs" lists and
      the PDF download.
22. **A company's Overview adds a summary print and its copies per month** (2026-10-30): MyCRM's CompanyDetails had
    neither. **Print summary** (`companyprint.aspx`) prints what the company page reads (details, salesperson and
    caretake remarks, addresses, primary contacts, machines with a contract or warranty running), nothing more.
    **Copies per month** adds up `meter_card` (Advance) by month over the last 12 months for every machine of the
    company's BRN, each machine's invoices as its own page reads them (of its contract when it has one, the most copies
    of each meter type per invoice): one read-only query (`IAdvanceMachines.MonthlyCopies`).

23. **Activities and projects move to MyCRMRecords, in MyCRM's tables** (decided 2026-10-30; this replaces rule 2 for
    the projects' temporary territories). The Activities page, the Projects pages and a company's Activities, Prospects
    and Projects tabs read and write MyCRM's `CompanyActivity`, `ActivityTypeLink`, `ActivityParticipants`,
    `CompanyActivityContactLink`, `CompanyActivityProspectLink`, `ProjectLink`, `Project`, `ProjectCompany`,
    `ProjectMembers` and the projects' **temporary** `Territory` and `CompanyTerritory` rows, behind the same switch
    (*Let MyCRMRecords change MyCRM's data*), every value recorded in `dbo.CrChange`, nothing deleted. Prospects are
    **only read** (`Prospect` is a read-only entity: an insert or update is refused). Differences from MyCRM:
    - **What MyCRM deleted is kept.** An activity type, attendee or prospect taken off an activity keeps its row with an
      empty `CompanyActivityId` (MyCRM deleted it); a participant or project link taken off is inactive (as in MyCRM).
    - **The temporary territory rule, made exact.** While a project is open (active, not closed, `EndDate` today or
      later) every active member but the Project Manager has one temporary territory (`IsTemporary` = 1, `IsOwner` = 0,
      `Territory` = their OutSystems user Id followed by a company's Id, as MyCRM numbers them) linked to exactly the
      project's active companies they do not own through an active permanent territory (MyCRM's
      `CheckUserHasPermTerritory`). Adding or removing a member or a company, changing the end date, closing or deleting
      the project keep it so; MyCRM's handlers could leave links of other members or of removed companies active.
    - **Closing keeps the members and the activity links**: MyCRM's `DisableMembersWhenProjClose` also made the
      participants of every activity linked to the project inactive (they disappeared from those people's activities);
      here closing takes the temporary territories away only. Deleting marks the project, its companies, members and
      activity links inactive and takes the temporary territories away; the activities and their participants stay.
    - **The daily task *Projects that have ended*** (MyCRM's `SetInactiveAllProjectThatEnds` with `FixTerritory`,
      05:30, off until an administrator turns it on): open projects whose end date has come are closed, then every active
      temporary territory whose member is inactive, whose project is not open, or that no member has, is made
      inactive. A project with no end date is left alone (MyCRM's Save never allowed one).
    - **Who sees what**: the Projects list keeps MyCRM's rule (projects with an active member in the person's list)
      and adds the projects they own; Sales Admins see every project. The activity list shows the activities owned or
      taken part in by the person ("Me") or their list ("My team"); a company's Activities tab keeps MyCRM's rule (from
      1 April 2015, a participant in the person's list or among the company's owners).
    - **Not built** (decided 2026-10-30): reminders and notifications (`CompanyActivityNotification`, `IsNotification`,
      `SendEmail`, the reminder email), the Telemarketer's Send Email and Reminder Date, Cold Call's Number Of Companies
      (cloning), the CSST role's shorter action and type lists, project colours and the timeline chart, reports, and
      MyCRM's First Appointment Attended adjustment (that type is not offered, as in MyCRM's form). A member's role is
      changed by removing and adding them again. The project's **History** lists the changes made in MyCRMRecords
      (`dbo.CrChange`), not MyCRM's own.
24. **A company's Usage & history and its machines by coverage** (2026-10-30): two read-only pages MyCRM did not have,
    reached from a company (the Overview's Copies per month and the Machines tab), with the company's machine rule
    (`Machines.MaySeeCompany`). **Usage & history** (`usage.aspx`) adds up `meter_card` as the Overview does
    (`IAdvanceMachines.MonthlyCopies`) over 6 to 96 months or months chosen, each month beside the same month a year
    before, and per year with how many of the machines were billed (`IAdvanceMachines.YearlyCopies`, one read-only
    query); **Export to Excel** downloads both. MyMachines' yearly "machines in field" (full year, added, expired,
    temporary), revenue and TDV are **not shown**: they come from `MIF_Summary`, `Rev_Summary` and `MIF_TDV`
    (Datacenter), which are not read and whose columns' meaning is not known. **Machines by coverage**
    (`companymachines.aspx`) groups every machine of the company's BRN as the Machines page labels them (On contract
    by contract, Warranty, Time & material, Contract expired, No contract); a T&M machine's **Rates** open its last
    contract on its own page. Installed date, location and last service of every machine are not shown there (they are
    read one machine at a time, on its page).

## 13. Production counts that shaped the design (2026-10-19)

The sync starts with the shared engine as it is (every row of the synced tables read at each sync, every 10 minutes):
Preview's time is measured on production first, and reading only changed rows is added to the engine only if needed
(decided 2026-10-19). Company and contact tables: about 725,000 rows.

Active territories 32,099, of which temporary 31,898; salespeople with a permanent territory 199; active
`CompanyTerritory` rows 1,019,601, of which in active permanent territories 22,716; companies with a salesperson 22,712,
of which 3 with more than one.

## 14. Switching over: what to switch off in MyCRM

MyCRM keeps running, so only what would fight MyCRMRecords is switched off (Service Center, eSpace MyCRM, *Timers*;
the screens by redirect). Nothing is unpublished or deleted.

**Timers to switch off** (deactivate in Service Center):

| Timer | Why |
|---|---|
| `SCA_SalesManCompanyAssignmentDaily` (`SetTerritory_Update`) | rebuilds territories from Advance: it would undo the Sales Admins' moves (rule 1) |
| `SCA_AssignGlobalCompany`, `AssignCompaniesToSales`, `TerritoryCode` | Advance territory one-offs and the unused territory codes |
| `Company`, `CompanyDaily`, `ImportChannelPartnerCompanies`, `Contact`, `RUN_ImportContacts`, `ContactLink`, `RUN_ImportContactLink`, `Address`, `CompanyAddressLink`, `Phone`, `Email` | old-CRM imports (rule 4): they would overwrite companies, contacts and addresses changed in MyCRMRecords |
| `UpdateCompanyCRMIdByBRN`, `UpdateCompanyCRMIdByName`, `UpdateOldCRMCompanyId` (Sundays 05:00), `UpdateAdvanceTypeCode`, `BulkUpdateCustomerType`, the `import 1st`–`7th` group-name imports | old-CRM and one-off company updates (rule 4) |
| `UpdateCustomerNo` (02:45) | once MyCRMRecords' scheduled task *Customer numbers from Advance* is on (both write the same values) |
| `Team` | the legacy timer that deletes every team (it always fails, but must never succeed) |

**Timers that stay on**: `CSST_Temp`, `CSST_05_AssignAllCompany`, `CSST_06_AssignUpdate`, `TelemarketerOtherComp`,
and every timer of the parts that stay (they write MyCRM's CSST temporary territories). `SetInactiveAllProjectThatEnds`
(05:30) stays on until MyCRMRecords' scheduled task *Projects that have ended* is turned on, then is switched off (both
close the same projects and take away the same temporary territories; rule 23).

**Screens sent to MyCRMRecords** (`Set-OldAppRedirects.ps1`; each screen's input to the page's parameter):

| MyCRM screen | MyCRMRecords |
|---|---|
| Companies | `./` |
| CompanyDetails (`CompanyId`) | `company.aspx?id=` |
| CompanyDetail (Admin flow, `CompanyId`) and the admin list Company | `company.aspx?id=` (the form is on the company page) / `./` |
| Users, CUserDetails (`UserId`), Teams, TeamDetails | `people.aspx` |
| UserTerritory (`UserId`), Territories, TerritoryDetail, TerritoryUpdate | `territories.aspx` |

**Stays in MyCRM**: `CompanyList_PopUp` (the company picker of ten other flows), the CompanyDetails blocks of the parts
that stay (activities, prospects, MIF, CSST, documents, projects) — they open from MyCRM's own screens — and the Admin
page (its MIF and developer tools). Before the redirects, the territories should have their names (rule 1) and the
sync should have run for a day without notes.

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

