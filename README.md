# MyCRMRecords: customer and employee feedbacks

MyCRMRecords replaces the OutSystems module **MyCRM** (its emails already said "MyCRMRecords"): the feedbacks of customers and
employees (customer complaints, potential customer complaints, internal challenges and areas of improvement), each
acknowledged, investigated and resolved with corrective and preventive tasks, its satisfaction confirmed and closed,
with the time each step took against the service targets (24 hours, 2 and 10 business days, without weekends and
AdvanceConnector's public holidays); the statistics of a financial year and the Excel report; and the lists the
feedbacks are chosen from. It is an ASP.NET (.NET Framework 4.8) application for IIS, built like MyAllocation,
MyPurchaseOrders, MyWarehouse, MyServiceParts, MyDeliveries, MyVendors and MyCustomers.

How MyCRM works, and every rule MyCRMRecords changed on purpose, is in
[`docs/outsystems/`](docs/outsystems/README.md). The user guide (`docs/user-guide.md`) is shown in the application.

## What it does

| Work | MyCRM | MyCRMRecords |
|---|---|---|
| The feedbacks with a count per status, search, division filter | Feedbacks | `default.aspx` (`Feedbacks.List`) |
| A feedback: the sections by category and type, rights by role and status | FeedbackDetail | `feedback.aspx` (`Feedbacks.Save`, `Rights`, `NextStatus`) |
| Tasks (corrective, preventive), their completion | FeedbackDetail's task popups | `Feedbacks.SaveTask`, `CompleteTask`, `RemoveTask` |
| Files (customer details, satisfaction) | FeedbackAttachment upload | `Feedbacks.Attach`, `RemoveFile`, `file.ashx/attachment` |
| Timings: acknowledgement hours, start and close days | UpdateTiming, NoOfHolidaysBetweenTwoDates | `Feedbacks.UpdateTiming`, `BusinessDays`, `SqlHolidays` |
| Emails: new feedback, new task, tasks completed, resolved for BE | the email templates | `Feedbacks.Notify`, `Send` |
| HR - ESS (internal challenges' suggestions) | CheckHRRole, Pending HR | `Feedbacks.Save` (`EditHr`) |
| Close case and delete (BE) | CheckBERole | `Feedbacks.CloseCase`, `Delete` |
| Statistics and the report | Statistics, ExportReport | `statistics.aspx` (`Statistics.Year`, `ReportFile`), `file.ashx/report` |
| Divisions, categories, mediums, KPI dates | Admin, BackOffice | `lists.aspx` (`Setup`) |
| Sign in as someone else | the user switch | `users/impersonate` (Feedback Admins and administrators) |

MyCRM has no timers, so MyCRMRecords has no scheduled tasks of its own. Everything that sends or changes something outside
MyCRMRecords starts **switched off** (admin page): the sync with MyCRM and the emails. So MyCRMRecords can be installed, set up and
checked while MyCRM still runs.

## Project layout

```
src/MyCRMRecords.Core/         net48 + net8.0: everything but the web host
  Data/                     record storage (JSON or SQL Server), migrations, settings, other databases (External,
                            ExternalTable, Holidays), scheduler, import (OutSystemsImport), the sync (OldAppSync,
                            CrmSync with CrmTables)
  Integration/              email (Mailer)
  Documents/Xlsx.cs         Excel files
  Domain/                   Models (the tables), Lists (MyCRM's static entities), Feedbacks, BusinessDays, Setup,
                            Statistics, People, SeedData
  Api/                      CrmApi (pages), FilesApi (attachments, report), AdminApi, UsersApi, CrmApp (factory)
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
`MyCRMRecords:DemoTools` on, *Add sample data* on the admin page adds a division, the categories and mediums, and three
feedbacks.

## Sign-in and roles

People sign in with their **Windows (domain) username and password** on `login.aspx`; the login is recorded as
`RSP\jsmith` (MyCRM's users are `RSP\jsmith@ricoh.sg`: the import and the sync match them by that login). MyCRM's
roles are kept and given on the User roles page: **Feedback Admin** (MyCRM's Admin: every feedback, the lists, the
customer details files, the satisfaction level, *Sign in as*), **BE** (close case, delete) and **HR** (HR - ESS).
Everyone signed in creates feedbacks and sees those they created, are in charge of, have a task in or are copied on.
Administrators are the people with the Admin role and those listed in `MyCRMRecords:Admins` in the server's
`Web.Local.config`; they also do what a Feedback Admin does. *Sign in as* opens MyCRMRecords as another person (recorded
with both names; a Feedback Admin cannot sign in as an administrator).

## Storage

**MyCRMRecords's own data** is in the JSON files (`App_Data/data/<table>.json`) until an administrator switches to
SQL Server on the admin page (*Database* tab): *Save and switch* runs the data migrations and copies the data in. The
tables are `dbo.Cr…`, one row per record, as mapped from `Domain/Models.cs`; a feedback's file is a
`varbinary(max)` column of `dbo.CrFeedbackFile`. What was sent or changed has tables of its own (`dbo.CrEmail`,
`dbo.CrAudit`, `dbo.CrSyncChange`).

**No record is ever deleted**: what people delete or take away is marked (a feedback, task, file, division or role
`IsActive = false` with when and by whom; a category or medium `Removed`; a sync pair that ends is `Unpaired`) and the
pages read only what is in use. The test `Nothing_in_MyCRMRecords_deletes_a_record` fails on a `Delete` anywhere outside the
stores.

**The settings** (connections, email, sync) are in `App_Data/crm-records-settings.json`, every password encrypted with DPAPI
and never sent to the browser.

**The OutSystems database** (*Connections* tab): MyCRM's entities (`Feedback`, `Task`, `FeedbackAttachment`,
`FeedbackDivision`, `FeedbackCategory`, `FeedbackMedium`, and the static entities `FeedbackGroup`, `FeedbackStatus`,
`FeedbackType`, `AcknowledgedType`, `CustomerSatisfactionLevel`, `TaskType`), its users and roles. Reading needs
`SELECT`; the sync needs `INSERT` and `UPDATE` on the six entity tables, and never `DELETE`.

**AdvanceConnector**: `public_holiday` (the public holidays left out of the business days; read, cached for an hour).
Reading needs `SELECT` only. Without it, only weekends are left out (the feedback page says so).

The table names are settings. The real column names of AdvanceConnector may differ from MyCRM's: every column is
written through `ExternalTable` (`SqlHolidays`). *Test* on the Connections tab lists every table and column MyCRMRecords
reads in each database.

## The pages' API

`api.ashx` returns JSON and needs a signed-in session (otherwise 401). Changes are POSTed as `application/json` and
answer `{ result: { ok, code, message, errors, id }, data }`. The routes are in `Api/CrmApi.cs` (`session`, `lists`,
`people`, `feedbacks`, `feedbacks/{id}`, `feedbacks/{id}/delete`, `feedbacks/{id}/close`, `feedbacks/{id}/tasks/{task}`,
`tasks/{id}/complete`, `tasks/{id}/remove`, `files/{id}/remove`, `setup`, `setup/kpi`, `setup/divisions/{id}`,
`setup/categories/{id}`, `setup/mediums/{id}` (and their `/remove`), `setup/kpi/{id}`, `statistics`,
`statistics/report`), `Api/AdminApi.cs` (`admin/...`) and `Api/UsersApi.cs`. `file.ashx` downloads
`attachment?id=` and `report?months=`; a POST (multipart, with the `X-MyCRMRecords` header) to
`attachment?feedback=&kind=` (`CD` or `CS`) attaches a file.

## IIS server setup (one time)

As for MyVendors: create the IIS application `MyCRMRecords` (folder `C:\inetpub\wwwroot\MyCRMRecords`), and install the
poller with `Install-MyApps.ps1` (or `Deployment\Install.ps1` from a copy of the repository). Then, on the admin page:
switch to SQL Server (a new database), set up the OutSystems and AdvanceConnector connections and email, and test
them.

### Switching over from MyCRM

Both can be used side by side while people move over: the sync keeps them in step (next section).

1. Give the logins `SELECT` on the tables above (and the OutSystems platform's user and role tables), and `INSERT` and
   `UPDATE` on MyCRM's six synced entity tables. Never `DELETE`.
2. Import MyCRM's data (admin page, *Import from MyCRM*), then at once turn on *Sync with MyCRM* (before
   anyone uses MyCRMRecords, so every new feedback takes MyCRM's next number).
3. Check the roles on the User roles page (the import copies MyCRM's).
4. Check the email recipients (*Email* tab: the same addresses as MyCRM's).
5. Tell people to use MyCRMRecords, and switch the emails on the same day (MyCRM sends its own while people use it). When
   nobody uses MyCRM any more, turn the sync off.

### Sync with MyCRM

`OldAppSync` (the engine, the same in every app that replaces an OutSystems app) with `CrmSync` (what is synced)
keeps MyCRMRecords and MyCRM in step both ways, every few minutes, from the scheduler's timer, once *Sync with
MyCRM* is on: the feedbacks, their tasks and attachments, the divisions, categories and mediums. The static entities
(status, type...) are matched by their label, never by their Id, which differs between environments.

- Each record is paired with its row in `dbo.CrSyncLink`, with a fingerprint of each side as it was at the last sync;
  the side whose fingerprint changed has changed, and its version is copied to the other (both changed: MyCRMRecords's
  version). Records imported before the sync was on are paired by their `OutSystemsId`; the first sync after the import
  writes nothing back to MyCRM.
- **Nothing is ever deleted in MyCRM.** `IOldTablesWriter` has no delete. A feedback, division, category or medium
  taken away here is `ISACTIVE = 0` there. A task or attachment (no such column) is detached from its feedback:
  `FEEDBACKID` emptied (MyCRM no longer shows it) and `SESSIONID` `X:<feedback>:<session>` so the row still says
  whose it was; an attachment's `FILENAME` also gets `(removed) ` before it. A task or attachment MyCRM keeps for a
  feedback never saved is not copied.
- **Files**: `CrmTables` reads MyCRM's attachments with each file's length (`DATALENGTH`, which does not read the file)
  instead of its bytes. A file kept here for that row with that length is taken as it is (a file replaced in MyCRM by
  one of exactly the same length would not be seen); only the others get their SHA-256 worked out by SQL Server, a few
  at a time, so a sync does not read every file. The bytes are read once, to copy a new or changed file here. *Read
  MyCRM's data* counts the attachments.
- **Numbers**: while the sync is on, a new feedback is first added to MyCRM and takes the Id it got there, so the
  feedback numbers (in the emails' subjects) are the same in both.
- People are written as their OutSystems user (found by login, name or email), never added to OutSystems; roles are not
  synced.
- **Safety**: what was written there is marked `Pending` until the commit is confirmed (written again otherwise); a sync
  that would change more than *Stop a sync that would change more than* records stops before changing anything and
  pauses until an administrator syncs. Every sync is in `dbo.CrSyncRun`, every value written in MyCRM in
  `dbo.CrSyncChange` with the value it replaced.
- Adding a synced record or column: add it to `CrmSync` (both ways); the round-trip test
  `Every_synced_field_goes_to_MyCRM_and_back_unchanged` fails until `FromOld` keeps every field.

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
