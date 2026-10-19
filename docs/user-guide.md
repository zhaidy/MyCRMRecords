# MyCRMRecords user guide

MyCRMRecords keeps the customers: the companies with their addresses, contacts (with their phones and emails),
parent and subsidiary companies, and who sells to them, in named **territories**; and the **CRM users**, MyCRM's
profile of each person (their MyCRM role and teams). It replaces the Users, Territory and Company part of MyCRM; the
rest of MyCRM (activities, prospects, projects, MIF, CSST…) stays in MyCRM and keeps working with the same companies.
Sign in with your Windows (RSP) username and password.

Nothing is ever deleted for good: a company, address, contact, phone, email, relationship, territory, territory's
company or CRM user taken away is kept in the database, marked, and no longer shown.

Who does what (your MyCRM role is set on the **CRM users** page):

- **Everyone** signed in opens the companies MyCRM lets them open: the companies of their own and their team's
  territories (and of the MyCRM projects they are in). Telemarketer, Admin, SalesAdmin, MD, Head, CSST, CSST Manager,
  MDS and SOL Specialist Managers, Credit Manager and Credit GM open every company.
- Anyone who opens a company may change its **No of Employees** and **Sales Remarks**.
- Every role but **Marketing** adds and changes a company's addresses, contacts and relationships.
- **Sales Admins** (MyCRM role Admin or SalesAdmin) add companies and change their details, keep the **CRM users**
  and teams, and the **Territories**. **MD** keeps the territories too.
- **Administrators** (MyCRMRecords's own role) do everything, keep the settings and the user roles, and may sign in as
  someone else.

## Companies

The companies you may open, by name, 50 at a time; the first page shows straight away.

- Type in **Company name or BRN** to search; choose a **Customer Type** or **Group Name**, or type a **Postal Code**
  (of any of the company's addresses), to narrow the list. **Reset** clears them.
- Each row shows the company's BRN, the postal code of its first address, its **Salesperson** and who created it.
  Press **Open** (or the name) to open the company.
- **New company** (Sales Admins) opens the company form; see [A company](#a-company).
- **Upload number of employees** (MyCRM Admins): choose an Excel workbook with the columns **BRN** and
  **NoOfEmployees** (**Download a sample** gives one). The first active company with each BRN gets the number; rows
  with no such company are skipped and counted.
- **Sign in as…** (administrators) opens MyCRMRecords as someone else; see [User roles](#user-roles).

## A company

The company's page has everything about it in one place.

- **Company**: BRN, A/C Type, Customer Code (Advance's customer number), the **Salesperson** with their territory,
  Customer Type, Company Group, Advance Customer Type, Industry, Website, created and last updated, and the billing
  addresses.
- **No of Employees**: press the pencil, type the number and press **Save** (or **Cancel**).
- **Sales Remarks**: type and press **Save**.
- **Edit company** (Sales Admins) opens the company form: **Is Active**, **Is HOME USER** (a home user's BRN may be
  left empty), **Company Name**, **BRN**, **Website**, **Is Channel Partner**, **Customer Type**, **Advance Customer
  Type**, **Company Group Name**, **Account Type**, **Comp Ownership** and **Remarks**. Two companies cannot have the
  same BRN (MyCRM checked new companies only; MyCRMRecords checks edited ones too); a home user's is not checked.
  Next to **Company Group Name**, **Add a group name**, **Rename** and the bin keep the list of group names; a group
  name a company still has cannot be removed.
- **Addresses**: the default address first. Choose a type at the top to show only that type. **New address** and
  **Edit** open the address form: **Is Default**, **Address Type**, **Postal Code**, **Block**, **Street**, **Unit
  Number**, **Building Name**, **City**, **State / Province** and **Country**. A company has one default address: tick
  **Is Default** on another only after unticking it on the default one. Change an address's type straight in the list.
  The bin deletes an address (after asking).
- **Contacts**: the primary contact first, with their phones and emails. **Create Contact** and **Edit** open the
  contact form: **Salutation**, **First Name**, **Christian Name (middle name)**, **Last Name / Surname**,
  **Department**, **Designation**, **Is Default** (the company's primary contact: ticking it takes it off any other),
  **Is Sales Navigator** and **Is Eq. List** (only for a contact with an email). Under **Email** and **Phone**, **Add
  Email** and **Add Phone** add a row; the bin takes a row off. Nothing is saved until **Save**. **Delete Contact** (or
  the bin in the list) deletes the contact, after asking.
- **Company Relationships**: the parent company, the siblings (the parent's other subsidiaries) and the subsidiaries.
  **Add Parent**, **Add Sibling** and **Add Subsidiary** open a search: type a company's name or BRN and choose it. A
  company has one parent at most, and a company cannot be related to itself or put below a company that is below it.
  **Remove** takes a relationship away (after asking); both companies stay.
- **Territory Movement History** (not shown to Sales and Telemarketers): every change of the company's salesperson,
  newest first, with their teams, who made it and when.
- **More Company Details**: remarks, caretake remarks and the other facts MyCRM keeps.

## Territories

For Sales Admins and MD. Each territory has a name, a number and a salesperson, and holds companies. A company is in
one territory: putting it in another moves it there. MyCRM's territories built from Advance are replaced by these.

- The list shows each territory with its salesperson and how many companies it has; search by territory, number or
  salesperson. **Show territories taken away** lists those too.
- **New territory**: give its **Name**, **Number** (not used by another territory) and **Salesperson** (people with
  the MyCRM role Sales), or none: a territory with no salesperson is vacant.
- **Open** a territory to work on it:
  - **Edit** changes its name, number or salesperson. A new salesperson has all its companies from then on; each
    company's move is recorded in its Territory Movement History.
  - **Take away** takes the territory away: its companies are taken out of it (each move recorded). It is kept,
    marked as taken away.
  - **Companies of the territory**: search by name or BRN; tick companies (or **Tick all shown**) and press **Take out
    of territory** to take them out (after asking).
  - **Put companies in this territory**, three ways:
    - **Search**: type 2 letters or more of a company's name or BRN, or tick **Only companies with no salesperson**;
      tick companies and press **Put the ticked companies here**.
    - **Paste a list**: paste company names or BRNs, one per line (or both, as copied from Excel), and press **Find the
      companies**. The list shows what was found, and why a line was not (**Company could not be found.**, or already
      in this territory). Press **Put the … found companies here**.
    - **Excel workbook**: choose a workbook with the columns **Company** and **BRN** (**Download a sample** gives one);
      the companies found are shown the same way.
    Before anything changes you are asked to confirm, and told how many companies leave another salesperson.

## CRM users

For Sales Admins. A person's MyCRM role and teams decide what they see and do, here and in MyCRM.

- **CRM users** lists everyone with a CRM profile, with their role, teams, salesman and territory codes and number of
  territories. Type to search by name, login, email, code or role; **Show everyone** also lists the people who are not
  CRM users (everyone MyCRM knows, and everyone who has opened MyCRMRecords). **Find someone in Active Directory**
  finds a person MyCRMRecords does not know yet.
- **Edit** opens the person:
  - **Role**: their MyCRM role.
  - **Team**: Sales, PP Specialist and CLETrainer have one team. Manager, PP, MDS and SOL Specialist Managers and
    CLETrainerManager are put in the team named after them: press **Create team** if it is not there. CSST has one
    team or none. Every other role ticks any number of teams (**Select All**, **Unselect All**); with none ticked the
    person is no longer a CRM user, as in MyCRM (the page warns you).
  - **Designation**, **Phone Number**, **Salesman Code** and **Territory Code**.
  - **Save** keeps the person's records and changes them (MyCRM deleted and made them again, losing their history).
  - **Remove CRM user and territories** (after asking) makes the person no longer a CRM user and takes their
    territories away with their companies.
- **Teams**: **New team** and **Edit** set a team's **Name**, **Short Name** and **Active**; the **Active** box in the
  list switches a team on or off at once. Teams are never deleted. **People** shows who is in a team.
- A change to someone's role or teams counts in MyCRMRecords at once; in MyCRM, from their next sign-in.

## User roles

For administrators. Everyone signed in can open MyCRMRecords; what they do in the CRM comes from their MyCRM role,
set on the [CRM users](#crm-users) page. The role here is MyCRMRecords's own: **Administrator**.

- **Find a person** searches Active Directory; tick **Administrator** and press **Save**.
- **People with a role** lists the administrators; untick and press **Save** to take the role away. The role is kept
  in the database, marked as taken away (when, and by whom), not deleted.
- **Sign in as** opens MyCRMRecords as that person, to see what they see. A bar at the top says so; **Return to**
  goes back to your own account. Everything you do is recorded as done by you on their behalf.

## Settings

For administrators. Everything that sends or changes something outside MyCRMRecords starts switched off.

- **Database**: where MyCRMRecords keeps its data. **Test connection** checks a SQL Server database; **Save and
  switch** moves to it.
- **Data migrations**: changes to the database, each run with **Run migration**.
- **Connections**: the OutSystems database (MyCRM's tables and its users) and AdvanceConnector (Advance's customers,
  for the customer numbers). **Test** saves the tab and checks every table and column MyCRMRecords reads.
- **Email**: the mail server. **Send emails** switches emails on; **Send a test email** checks it. **Emails** lists
  every email, sent or not, and why not. MyCRM's Users, Territory and Company screens send no emails.
- **Scheduled tasks**: **Run the scheduled tasks** switches them on, and each task has its own **On**, time and days.
  **Customer numbers from Advance** (MyCRM's UpdateCustomerNo, daily at 02:45) gives each active company the
  customer number of Advance's customers with its BRN (with " #Multiple" after it when several share the BRN).
  **Run now** runs a task at once. Switch MyCRM's own timer off when this one runs.
- **Import from MyCRM**: **Read MyCRM's data** counts what is there; **Import** copies the CRM users and teams, the
  companies with their addresses, contacts, phones, emails, relationships and group names, the active permanent
  territories with their companies, the territory movements, and every OutSystems user (so names show). It can be run
  again: what was imported before is skipped. If the website restarts while an import runs, the import shows
  **Stopped**: press **Import** again to carry on from where it stopped.
- **Sync with MyCRM**: while MyCRM is still used, keeps both in step. Tick **Sync with MyCRM** and press
  **Save** to turn it on (right after the import, before people use MyCRMRecords); it syncs at once, then every few
  minutes (**Every (minutes)**).
  - What is added or changed in either app reaches the other: the CRM users and teams, the companies with their
    addresses, contacts, phones, emails, relationships and group names, the permanent territories with their
    companies, and the territory movements of the last 60 days. Changed in both since the last sync, MyCRMRecords's
    version is kept.
  - MyCRM's temporary territories (its projects and CSST) are never copied; MyCRMRecords reads them only to decide who
    may open a company.
  - Nothing is ever deleted in MyCRM: what is taken away here is made inactive there (a company's address link or a
    group name, which MyCRM cannot mark, is left there as it is).
  - While it is on, a new company, address or contact takes its number from MyCRM, so the numbers are the same in both.
  - The first sync after the import pairs each imported record with its row in MyCRM. Where the two differ, the
    record keeps MyCRM's version: what was imported is never written back to MyCRM.
  - **Preview** shows what a sync would do without changing anything. **Sync now** syncs at once. Both run in the
    background: the tab shows the preview's result (and how long it took) and the sync under **Syncs** when they end.
    The first sync after the import pairs every record and takes several minutes. A sync shown as "Cut off before it
    finished" was stopped (the website restarted): nothing it did was kept, and the next sync does it again.
  - A sync that would change more than **Stop a sync that would change more than** records stops before changing
    anything, and the syncs pause until you look: press **Preview**, and if it is right, tick **Allow a large sync** and
    press **Sync now**.
  - **Syncs** lists every sync and what it did; **Changes made in MyCRM** lists every value the sync wrote there
    with the value it replaced, so anything can be put back.

## Deployment

For administrators: the version installed, when, and what changed in it.

## Audit log

For administrators: every change made through MyCRMRecords and every sign-in, newest first. **Only what did not
succeed** shows the failures.
