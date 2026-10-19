# MyCRMRecords: companies, territories and CRM users

MyCRMRecords takes over the **Users, Territory and Company part** of the OutSystems module **MyCRM**: the companies with
their addresses, contacts (with their phones and emails), relationships and group names; the named territories that
give companies to salespeople; and the CRM users (each person's MyCRM role and teams) and teams. The rest of MyCRM
(activities, prospects, projects, CSST, MIF, reports...) stays in OutSystems and keeps reading and writing the same
tables: MyCRMRecords reads and writes them where MyCRM keeps them, so both always show the same data. It is an ASP.NET (.NET Framework 4.8) application for IIS,
built like MyAllocation, MyWarehouse, MyServiceParts, MyDeliveries, MyVendors, MyCustomers and MyCEV.

How MyCRM works, and every rule MyCRMRecords changed on purpose, is in
[`docs/outsystems/`](docs/outsystems/README.md). The user guide (`docs/user-guide.md`) is shown in the application.

## What it does

| Work | MyCRM | MyCRMRecords |
|---|---|---|
| The companies a person may open, searched by name, BRN, customer type, group name, postal code | Companies (CompanyList) | `default.aspx` (`Companies.List`, `Access`) |
| A company: details, billing address, number of employees, sales remarks, territory movements | CompanyDetails | `company.aspx` |
| The company form (Sales Admins), duplicate BRN check, the Pre… values | CompanyDetail | `Companies.Save` |
| Addresses, the default address | Address block, AddressDetail_PopUp | `Companies.SaveAddress`, `SetAddressType`, `RemoveAddress` |
| Contacts with their emails and phones, the primary contact | Contacts block, ContactDetail_Popup | `Companies.SaveContact`, `RemoveContact` |
| Parent, sibling and subsidiary companies | CompanyRelationships | `Companies.AddRelationship`, `RemoveRelationship` |
| Group names | CompanyGroupNameDetail | `Companies.SaveGroupName`, `RemoveGroupName` |
| Numbers of employees from Excel | AdminPage, UploadNoOfEmployees | `file.ashx/employees` (`Companies.UploadEmployees`) |
| Who may open a company | UserCanAccessCompany, GetSub | `Access` (MyCRM's temporary territories read live) |
| Named territories: a number, a salesperson, companies; moves recorded | Territories, TerritoryDetail, TerritoryUpdate, UserTerritory | `territories.aspx` (`Territories`) |
| CRM users (role, teams, codes) and teams | Users, CUserDetails, Teams, AdminPage | `people.aspx` (`CrmUsers`) |
| Advance's customer number of each company | timer UpdateCustomerNo (02:45) | scheduled task *Customer numbers from Advance* (`CustomerNumbers`) |
| Sign in as someone else | (none) | `users/impersonate` (administrators) |

MyCRM's territories were rebuilt from Advance every day; that is deprecated: Sales Admins now create the territories
and put companies in them. MyCRM's imports from the old CRM are not carried over. Everything that sends or changes
something outside MyCRMRecords starts **switched off** (admin page): changing MyCRM's data, the emails and the
scheduled task. So MyCRMRecords can be installed, set up and checked while MyCRM still runs.

## Project layout

```
src/MyCRMRecords.Core/         net48 + net8.0: everything but the web host
  Data/                     record storage (JSON or SQL Server), migrations, settings, other databases (External,
                            ExternalTable), scheduler, MyCRM's tables (MyCrmMap: where each property is; MyCrmStore:
                            reading and writing them; RoutedStore), CustomerNumbers; OldAppSync/OldTables (the engine
                            shared with the other apps, not used by MyCRMRecords)
  Integration/              email (Mailer)
  Documents/Xlsx.cs         Excel files
  Domain/                   Models (the tables), Lists (MyCRM's static entities), Companies, Territories, CrmUsers,
                            Access, People
  Api/                      CrmApi (pages), FilesApi (Excel samples and uploads), AdminApi, UsersApi, CrmApp (factory)
src/MyCRMRecords.Web/          net48 web root: pages, api.ashx, file.ashx, scripts, Web.config
tests/MyCRMRecords.Tests/      xunit (net8.0); SQL Server tests when MYCRMRECORDS_TEST_SQLSERVER is set
Deployment/                 build, poller and install scripts (as MyAllocation)
docs/                       user guide; docs/outsystems: how MyCRM works
```

## Develop

Requires the .NET 8 SDK (it also builds the .NET Framework 4.8 project, on any OS).

```powershell
dotnet test
# The SQL Server tests also run when this is set (they create and drop their own databases):
$env:MYCRMRECORDS_TEST_SQLSERVER = 'Server=(localdb)\MSSQLLocalDB;Integrated Security=true'
dotnet build src/MyCRMRecords.Web
& "C:\Program Files\IIS Express\iisexpress.exe" /path:"$PWD\src\MyCRMRecords.Web" /port:8080
```

On a machine without Active Directory, add `<add key="MyCRMRecords:LoginMode" value="Development" />` to a local
copy of `Web.config` and set `compilation debug="true"`: any username then signs in without a password check (as
`name@example.com`), but only for requests from that same machine. Never set it on the server. With
`MyCRMRecords:DemoTools` on, *Add sample data* on the admin page adds two companies with an address and a contact, a
group name, a team and a sample territory of yours.

## Sign-in and roles

People sign in with their **Windows (domain) username and password** on `login.aspx`; the login is recorded as
`RSP\jsmith` (MyCRM's users are `RSP\jsmith@ricoh.sg`: MyCRMRecords matches them by that login, or by the email address
of the login that signs in).

What people may do comes from their **MyCRM role**, as in MyCRM: the role of their first CRM user record (MyCRM's
`Role()`), kept on the CRM users page.

- **Sales Admins** (MyCRM role Admin or SalesAdmin on any of their records, MyCRM's `IsSalesAdmin`): the CRM users and
  teams, the company form and group names, and the territories. **MD** also keeps the territories (MyCRM let MD move
  companies in bulk).
- **Opening a company** (MyCRM's `UserCanAccessCompany`): Telemarketer, Admin, SalesAdmin, MD, CSST, CSSTManager, MDS
  and SOL Specialist Managers, Head, Credit Manager and Credit GM open every company; everyone else opens the active
  companies of the active territories of the people in their list (MyCRM's `GetSub`: a Manager his team's
  salespeople, a Sales person himself...), the permanent territories kept here and MyCRM's temporary ones (Projects,
  CSST) read from MyCRM.
- Addresses, contacts and relationships: every role but Marketing. Number of employees and sales remarks: anyone who
  opens the company. Uploading numbers of employees: MyCRM's Admin role.

MyCRMRecords's own role is **Administrator** (the admin pages, user roles, the audit log, and everything a Sales Admin
does), given on the User roles page or listed in `MyCRMRecords:Admins` in the server's `Web.Local.config`. *Sign in as*
opens MyCRMRecords as another person (administrators only; recorded with both names).

## Storage

**MyCRM's data stays in MyCRM.** The companies with their caretake remarks, relationships, group names, addresses,
contacts, phones and emails, the CRM users and teams, the permanent territories with their companies and the territory
movements are read and written in MyCRM's own tables in the OutSystems database (`Data/MyCrmStore.cs`; `MyCrmMap` says
where each property is). A record's Id is MyCRM's. MyCRMRecords keeps no copy and does not sync: both apps see the same
rows at once, and so do the other apps that read MyCRM's companies (MyFile, MyProjectManagement, MyCEV...).

- **Writing is switched off** until an administrator ticks *Let MyCRMRecords change MyCRM's data* (admin page, *MyCRM*
  tab; `MyCrm.WritesEnabled` in the settings). Until then MyCRMRecords only reads, every page says so, and a save is
  refused (409) with that message.
- **Nothing is ever deleted in MyCRM**: `MyCrmStore` has no delete. What is taken away is `ISACTIVE = 0` there; a CRM
  user record taken away is inactive with no team and, when the person has no team left, no role, so MyCRM's `Role()`,
  `IsSalesAdmin` and `GetSub` read exactly what they read when MyCRM deleted the records.
- **What MyCRM has no column for** is kept in `dbo.CrRecordNote` by MyCRM's entity and Id: a territory's name, when and
  by whom something was made or taken away, a CRM user's former team, a company address link or group name taken away
  (MyCRM cannot mark those; the address itself is inactive there).
- **Every value written** is recorded with the value it replaced and who changed it in `dbo.CrChange` (*Changes made in
  MyCRM* on the *MyCRM* tab). A save is one transaction in MyCRM; only columns whose value changed are written, and a
  value that reads back the same (a person OutSystems keeps twice, text with spaces at its ends) is no change.
- **Only MyCRM's permanent territories** are read: the active permanent `Territory` rows and those MyCRMRecords keeps a
  note of (taken away here), and the active `CompanyTerritory` rows of active permanent territories, never the 32,000
  temporary territories of MyCRM's Projects and CSST parts nor the millions of inactive rows. The temporary ones are only
  read for who may open a company.
- **Static entities** (role, customer type, address type...) are matched by their label, never by their Id, which
  differs between environments. **People** are their OutSystems user: a user is the login that signs in with the user's
  email address, else the username as a login (`RSP\jsmith@ricoh.sg` is `RSP\jsmith`); a person MyCRM does not know
  cannot be given a CRM user record or a territory.

**MyCRMRecords's own data** (people, roles, the audit log, the emails, task runs, `dbo.CrRecordNote` and `dbo.CrChange`)
is in the JSON files (`App_Data/data/<table>.json`) until an administrator switches to SQL Server on the admin page
(*Database* tab): *Save and switch* runs the data migrations and copies the data in. The tables are `dbo.Cr…`, one row
per record, as mapped from `Domain/Models.cs`. MyCRMRecords used to keep a copy of MyCRM's data and sync it; the
migration `2026-10-19-mycrm-tables-directly` keeps what MyCRM has no column for as notes and drops the copies.

**No record is ever deleted**: what people delete or take away is marked (a company, address, contact, phone, email,
relationship, territory, company link, CRM user record or role `IsActive = false`, with when and by whom) and the pages
read only what is in use. The test `Nothing_in_MyCRMRecords_deletes_a_record` fails on a `Delete` anywhere outside the
stores.

**The settings** (connections, email, MyCRM's switch, scheduled task) are in `App_Data/crm-records-settings.json`, every
password encrypted with DPAPI and never sent to the browser.

**The OutSystems database** (*Connections* tab): MyCRM's entities `Company`, `CompanyCaretake`, `CompanyRelationship`,
`CompanyGroupName`, `Address`, `CompanyAddressLink`, `Contacts`, `CompanyContactLink`, `Phone`, `Email`, `CUser`,
`Team`, `Territory`, `CompanyTerritory`, `TerritoryMovement`, the static entities `Role`, `CompanyGroup`,
`CompAccountType`, `CompOwnership`, `CompanyIndustry`, `CustomerType`, `AdvanceCustomerType`, `AddressType`,
`PhoneType`, `CompanyRelationshipType`, and the platform's users. Reading needs `SELECT`; changing MyCRM's data needs
`INSERT` and `UPDATE` on the fifteen entity tables, and never `DELETE`.

**AdvanceConnector**: `Customer_Supp` (`Business_reg`, `Cust_no`), read by the scheduled task *Customer numbers from
Advance*. Reading needs `SELECT` only.

The table names are settings. The real column names of AdvanceConnector may differ from MyCRM's: every column is
written through `ExternalTable` (`CustomerNumbers`). *Test* on the Connections tab lists every table and column
MyCRMRecords reads in each database.

## The pages' API

`api.ashx` returns JSON and needs a signed-in session (otherwise 401). Changes are POSTed as `application/json` and
answer `{ result: { ok, code, message, errors, id }, data }`. The routes are in `Api/CrmApi.cs` (`session`, `lists`,
`people`, `companies`, `companies/{id}` and its `employees`, `remarks`, `relationships`, `addresses/{id}`,
`contacts/{id}`, `groupnames`, `territories`, `territories/{id}` and its `add`, `takeout`, `match`, `remove`,
`territories/search`, `crmusers`, `crmusers/person?login=`, `teams`), `Api/AdminApi.cs` (`admin/...`) and
`Api/UsersApi.cs`. `file.ashx` downloads `sample?of=employees` and `sample?of=territory`; a POST (multipart, with the
`X-MyCRMRecords` header) to `employees` or `territory-list?territory=` uploads an Excel workbook.

## IIS server setup (one time)

As for MyVendors: create the IIS application `MyCRMRecords` (folder `C:\inetpub\wwwroot\MyCRMRecords`), and install the
poller with `Install-MyApps.ps1` (or `Deployment\Install.ps1` from a copy of the repository). Then, on the admin page:
switch to SQL Server (a new database), set up the OutSystems and AdvanceConnector connections and email, and test
them.

### Switching over from MyCRM

MyCRMRecords and the rest of MyCRM share MyCRM's tables for good: there is nothing to copy or sync.

1. Give the logins `SELECT` on the tables above (and the OutSystems platform's user table), and `INSERT` and `UPDATE`
   on MyCRM's fifteen entity tables. Never `DELETE`.
2. In OutSystems, switch off MyCRM's timers that would fight MyCRMRecords (`docs/outsystems/README.md`, section 14):
   the Advance territory rebuild and the old-CRM imports, and MyCRM's `UpdateCustomerNo` once MyCRMRecords' task is on.
3. On the admin page, *MyCRM* tab: tick *Let MyCRMRecords change MyCRM's data*.
4. Check the CRM users and name the territories (MyCRM's come without names: "Territory 1203").
5. Turn on the scheduled task *Customer numbers from Advance* (daily at 02:45).
6. Send MyCRM's Users, territory and company screens to MyCRMRecords (`Set-OldAppRedirects.ps1`, section 14 of
   `docs/outsystems/README.md`); the company picker other parts of MyCRM use stays in MyCRM.

## Deploying

As MyAllocation and MyWarehouse: a pull request into `master` runs *PR checks* and merges itself when they pass;
`master` builds *Build IIS release*, and the server's poller installs it within a minute (never touching
`Web.Local.config` or `App_Data`), restoring the backup if the website does not answer.

### Self-hosted runner

The builds and pull request checks run on GitHub's Windows runners, free while the repository is public (a private
repository's Windows minutes count double against the account's monthly allowance). The repository's Actions variable `SELF_HOSTED_RUNNER` = `true` runs every
job (PR checks, the master build and its clean-up) on a Windows machine of our own instead, as in MyAllocation, and
uses no Actions minutes. A runner belongs to one repository of a personal account, so a machine that already runs
MyAllocation's runner gets a second one for this repository, in a folder of its own.

1. **Install on the machine** (Windows 10/11 or Server 2016+, which have Windows PowerShell 5.1):
   [Git for Windows](https://git-scm.com/download/win), the
   [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (it builds the net48 site too: the reference
   assemblies come from NuGet), and SQL Server Express **LocalDB** (from the SQL Server Express installer, *Download
   Media → LocalDB*) for the SQL Server tests, which are skipped without it. In PowerShell, `dotnet --list-sdks`,
   `git --version` and `SqlLocalDB info` should each answer. If `Get-ExecutionPolicy` says `Restricted` (the Windows
   10/11 default), run `Set-ExecutionPolicy RemoteSigned -Scope LocalMachine` as Administrator so the workflows'
   PowerShell scripts can run.
2. **Register the runner.** On GitHub: *Settings → Actions → Runners → New self-hosted runner*, Windows, x64. Run the
   commands it shows in PowerShell as Administrator, in `C:\actions-runner\MyCRMRecords` instead of
   `C:\actions-runner`. Keep the default runner group, name and labels (`self-hosted`, `Windows`, `X64`: the workflows
   ask for `self-hosted` and `Windows`). Answer **Y** to *run the runner as a service*, and give it a **user account**
   (`.\builduser` or `RSP\builduser`) rather than the default NETWORK SERVICE: LocalDB runs only for an account with a
   user profile. The service is *GitHub Actions Runner (zhaidy-MyCRMRecords.<name>)* and starts with Windows.
3. **Switch the workflows to it.** *Settings → Secrets and variables → Actions → Variables → New repository variable*:
   name `SELF_HOSTED_RUNNER`, value `true`. The next run uses the runner; re-run a run that failed for lack of
   minutes with *Re-run all jobs*. The runner page then shows it *Active* while it works.

While the variable is `true`, jobs wait (up to a day) for the runner when it is offline: they never fall back to
GitHub's runners. Delete the variable to go back to them. To make sure GitHub's runners never bill, set a $0 budget
for Actions (*Settings → Billing and licensing → Budgets and alerts*). The runner keeps NuGet's packages in its
account's profile, so builds download nothing again. Update the runner when GitHub asks; it updates itself while
idle.

### Settings

| `appSettings` key | Default | Meaning |
|---|---|---|
| `ADDomain` | `RSP` | Windows domain name used in logins. |
| `MyCRMRecords:AllowedGroups` | *(empty)* | AD groups allowed to sign in, `;`-separated. Empty = any domain user. |
| `MyCRMRecords:Admins` | *(empty)* | Administrators besides the Admin role (logins and/or AD groups). |
| `MyCRMRecords:DemoTools` | `true` | Sample data on the admin page and in a new JSON data folder. Set `false` on the server. |
| `MyCRMRecords:DataFolder` | `~/App_Data/data` | The JSON data files, used until an administrator switches to SQL Server. |
