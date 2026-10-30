# MyCRMRecords user guide

MyCRMRecords keeps the customers: the companies with their addresses, contacts (with their phones and emails),
parent and subsidiary companies, and who sells to them, in named **territories**; and the **CRM users**, MyCRM's
profile of each person (their MyCRM role and teams). It replaces the Users, Territory and Company part of MyCRM; the
rest of MyCRM (activities, prospects, projects, MIF, CSST…) stays in MyCRM and keeps working with the same companies.
It also keeps MyOrders' **costing sheets** and **request forms** readable, read only (see
[Costing sheets](#costing-sheets)), shows MyFile's **files** (see [Files](#files)) and Advance's **machines** (see [Machines](#machines)). Sign in with your Windows (RSP) username and password.

MyCRMRecords and MyCRM share the same data: what you save here is in MyCRM at once, and the other way round. Until an
administrator lets MyCRMRecords change MyCRM's data, MyCRMRecords only shows it: a bar at the top of every page says so,
and saving is refused with that message.

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
- **Everyone** sees the costing sheets and request forms MyOrders let them see; see [Costing sheets](#costing-sheets).
- **Everyone** sees MyFile's files of the companies they may open, and the files of no company; see [Files](#files).
- **Everyone** sees Advance's machines of the companies they may open, and of the companies they were given temporary
  access to; see [Machines](#machines). **Sales Admins** give and remove the temporary access
  ([Machine access](#machine-access)).
- **Administrators** (MyCRMRecords's own role) do everything, keep the settings and the user roles, and may sign in as
  someone else.

## Finding your way around

The menu is in the top bar. When you scroll down a page the menu folds to the page you are on and the bar shows the
page's title; scroll up a little and the whole menu comes back. Click your initials at the right of the bar to see who
you are signed in as, and **Sign out**.

Above the title of a record's page (a company, a costing sheet, a request form, a territory, a machine, a project) is the
way you came, for example **Costing sheets › Costing Sheet # 23052 › TRUE COLOURS PRINT HOUSE PTE. LTD.**: click any
step to go back to it (a company opens again on the tab you were on). Choosing a page in the menu starts the way again;
a page you open from a link or a bookmark shows its usual list instead (**Companies › …**). On a phone only the step
back is shown, as the round back button at the left of the top bar.

Forms and questions open in a window over the page. Its title is in the middle; the round **×** at the left closes it
without saving (so does the Esc key); the button at the right does what the window is for, for example **Save**,
**Create territory**, **Save changes** or **Take away** (red when it takes something away). When something cannot be
saved, the window stays open, says why under the form and marks the fields to correct.

## Using MyCRMRecords on a phone

MyCRMRecords works on a phone as well as on a computer. On a phone the menu is a bar at the bottom of the screen:
**Companies**, **Files**, **Machines**, **Orders** (the costing sheets and request forms) and **More**.

**More** opens a screen with everything else: your name and MyCRM role, **Install the app** (see below), then the
other pages in groups: **Sales** (Projects, Activities), **Records** (Territories and CRM users, for the roles that keep
them), **Machines** (Machine access, for Sales Admins) and **App** (User guide, and for administrators User roles,
Settings, Deployment and Audit log, marked **Admin**). **Sign out** is at the bottom. The round **×** at the top left,
**More** again or the phone's Back closes it.

On a record's page (a company, a territory, a project, a machine…) the round back button at the left of the top bar
goes back the way you came (in a company's section, back to the company). Once you scroll past a page's large title,
the title shows in the middle of the top bar.

On Territories, Activities, Projects and a company's page, the round **+** beside the bottom bar is the page's create
button: **New territory**, **New activity** or **Create project**.

To put MyCRMRecords on your phone's home screen, so it opens like an app, full screen:

- **On a computer**: click **Install app** in the top bar. Scan the QR code with your phone's camera, sign in on the
  phone, then install it there as below. When your browser can, **Install on this computer** at the top right of the
  window installs MyCRMRecords on the computer too.
- **On an Android phone**: tap the install button (the phone with an arrow) in the top bar, or **Install** on the
  **More** screen, then **Install**.
- **On an iPhone**: tap the install button (or **Install** on the **More** screen) for the steps: in Safari, tap
  **Share**, then **Add to Home Screen**.

The button is not shown once MyCRMRecords runs as the installed app (the **More** screen then says **Installed**). The phone needs the office network or the VPN,
as on a computer; without it, the app says MyCRMRecords can't be reached, with **Try again**.

## Companies

The companies you may open, by name, 50 at a time, one line each; the first page shows straight away. The line under
the title says how many there are.

- Type in **Company name or BRN** to search; choose a **Customer type** or **Group name**, or type a **Postal code**
  (of any of the company's addresses), to narrow the list. **Reset** clears them. On a phone, tap the round filter
  button beside the search for **Customer type**, **Group name** and **Postal code**, then **Apply** (**Reset
  filters** clears them); a dot on the button means filters are on.
- Each row shows the company's **BRN**, the postal code of its first address, its **Salesperson** (or **No
  salesperson**), its **Territory**, **Customer type** and who created it. Click the name, or anywhere on the row, to
  open the company. On a phone each company is a card: tap it.
- If you see every company (Sales Admins, for example), choose what to show above the list:
  - **All**: every active company.
  - **Mine**: the companies of your territories (and, by your role, your team's). MyCRM's project territories don't
    count.
  - **No salesperson**: active companies that are in no permanent territory.
- **Put companies in a territory** (Sales Admins and MD): choose **No salesperson**, tick companies (the box in the
  heading ticks all those shown), and press **Put in a territory…** at the top. Choose the **Territory**; the dialog
  says what changes: the companies get that territory's salesperson and leave the No salesperson list, and the move is
  recorded. Press **Put … in …** to save it in MyCRM. **Clear** unticks them. This is done on a computer.
- **New company** (Sales Admins) opens the company form; see [A company](#a-company).
- **Upload number of employees** (MyCRM Admins): choose an Excel workbook with the columns **BRN** and
  **NoOfEmployees** (**Download a sample** gives one). The first active company with each BRN gets the number; rows
  with no such company are skipped and counted.
- **Sign in as…** (administrators) opens MyCRMRecords as someone else; see [User roles](#user-roles).

## A company

The company's page has everything about it. Under its name, one line says its territory, salesperson, BRN and
customer code. At the top right:

- **Print summary** opens the company's summary on one A4 page: its details, salesperson and caretake remarks,
  addresses, primary contacts and the machines whose contract or warranty is running. Press **Print / Save as PDF**
  (or **Back to the company**). On a phone it is the printer button.
- **Edit company** (Sales Admins; on a phone the pencil) opens the company form: **Is Active**, **Is HOME USER** (a
  home user's BRN may be left empty), **Company Name**, **BRN**, **Website**, **Is Channel Partner**, **Customer
  Type**, **Advance Customer Type**, **Company Group Name**, **Account Type**, **Comp Ownership** and **Remarks**. Two
  companies cannot have the same BRN (MyCRM checked new companies only; MyCRMRecords checks edited ones too); a home
  user's is not checked. Next to **Company Group Name**, **Add a group name**, **Rename** and the bin keep the list of
  group names; a group name a company still has cannot be removed.

The rest is in tabs: **Overview**, **Contacts**, **Activities**, **Prospects**, **Projects**, **Files**, **Machines**, **Costing sheets**, **Request forms** and
**Relationships & history** (with how many there are). Clicking a tab shows it in place; the page remembers the tab,
so coming back from a costing sheet opens the same one. On a phone the Overview shows the company's details and then
a list of sections, **More details** first (sales notes, addresses, salesperson, copies per month and more company
details): tap one to open it on the whole screen, and the company's name at the top to go back.

**Overview**:

- **Company details**: BRN, A/C Type, Customer Code (Advance's customer number), the **Salesperson** with their
  territory, Customer Type, Company Group, Advance Customer Type, Industry, Website, Is Channel Partner, created and
  last updated (with by whom). Change them with **Edit company**.
- **Copies per month**: the black (teal) and colour (coral) copies billed on all the company's machines in Advance
  (found by its BRN) in each of the last 12 months, each machine's of its contract as on its own page. Point at a
  month to see its numbers; the line below adds up the year. **Machines** opens the Machines tab.
- The four tiles count the company's **Machines** (contract or warranty running), **Files**, **Contacts** and
  **Costing sheets**; click one to open its tab.
- **Salesperson**: the company's owner and their territory.
- **Sales notes**, for anyone who opens the company: **No of Employees** (press the pencil, type the number and press
  **Save**, or **Cancel**) and **Sales Remarks** (type and press **Save remarks**).
- **Addresses**: the default address first. Choose a type at the top to show only that type. **New address** and
  **Edit** open the address form: **Is Default**, **Address Type**, **Postal Code**, **Block**, **Street**, **Unit
  Number**, **Building Name**, **City**, **State / Province** and **Country**. A company has one default address: tick
  **Is Default** on another only after unticking it on the default one. Change an address's type straight in the list.
  The bin deletes an address (after asking).
- **More company details**: remarks, caretake remarks and the other facts MyCRM keeps.

**Contacts**: the primary contact first, with their phones and emails. **Create Contact** and **Edit** open the
contact form: **Salutation**, **First Name**, **Christian Name (middle name)**, **Last Name / Surname**,
**Department**, **Designation**, **Is Default** (the company's primary contact: ticking it takes it off any other),
**Is Sales Navigator** and **Is Eq. List** (only for a contact with an email). Under **Email** and **Phone**, **Add
Email** and **Add Phone** add a row; the bin takes a row off. Nothing is saved until **Save**. **Delete Contact** (or
the bin in the list) deletes the contact, after asking.

**Relationships & history**:

- **Company Relationships**: the parent company, the siblings (the parent's other subsidiaries) and the subsidiaries.
  **Add Parent**, **Add Sibling** and **Add Subsidiary** open a search: type a company's name or BRN and choose it. A
  company has one parent at most, and a company cannot be related to itself or put below a company that is below it.
  **Remove** takes a relationship away (after asking); both companies stay.
- **Territory Movement History** (not shown to Sales and Telemarketers): every change of the company's salesperson,
  newest first, with their teams, who made it and when.

**Files**: the company's newest ten files from MyFile (the PDFs uploaded in MyFile: costing sheets, request forms,
FSAs, leasing papers, DOs…), with their type, number, upload date and size; click a file's name to open its PDF.
**See all** opens the [Files](#files) page with only this company's files.

**Machines**: the company's machines in Advance (found by its BRN), as MyCRM's Machines tab showed them. **Active**
(the first) lists those whose contract or warranty ends after today; **All** adds those on time and materials, those
whose contract or warranty has ended and those with none. Each row shows the **Serial** (click it to open the
machine), **Model**, **Status** (Contract, Warranty, T&M, Expired or No contract), **Contract** number and type,
**Contract start** and **end**, **Warranty start** and **end** (dates already past are grey) and **Finance type**
(Internal lease, External lease, Outright or Dealer). On a phone each machine is a card. See [Machines](#machines).

**Costing sheets** and **Request forms**: the company's newest costing sheets and request forms from MyOrders, of those
you may see; click a number to open it. **See all** opens the list of all of them.

**New activity** (top right) logs an activity with this company: see [Activities](#activities) for the form.

**Activities**: the company's activities in MyCRM (from 1 April 2015) of you, your team and the company's
salespeople. Choose **Upcoming**, **Past** or **All**, and an **Activity Action**. Click an activity's remarks to read
it; its owner can **Edit** or **Delete** it there.

**Prospects**: the company's prospects in MyCRM, **view only**: click one to see its stage, status, type, forecast,
dates and owner beside the list. Prospects are created and changed in MyCRM: **Open in MyCRM** opens the company there.

**Projects**: the projects that include the company, with their status, owner, members and roles, end date and
whether they are closed. Click a project to open it (if you or your team are in it). A salesperson added to a project
as **Salesperson (Caretaker)** can see and work on the company until the project's end date.

## Activities

The **Activities** page lists the sales activities in MyCRM that you own or take part in (**Owner: Me**), or your
team's (**My team**, or one person of your team). Everything is saved in MyCRM.

- Type in the search box to find activities by company, remarks, action or type. Choose **Upcoming** (today and
  later), **Today**, **Past** or **All**, and a **Type**. MyCRMRecords remembers your choices.
- **List** shows the activities by day with their time, type, remarks and action, company, contacts, owner and status:
  **Completed**, **Pending**, or **Not completed** (its start date has passed). **Calendar** shows a month: the arrows
  change the month and **Today** comes back; a day with more than three shows **+ more**.
- **Download Excel** downloads the activities shown (CompanyActivity.xlsx).
- Click an activity to read it. Only its **owner** can change it: **Edit**, or **Delete** (after asking).

**New activity** opens the form (MyCRM's): **Company** (only your companies are listed; Sales Admins and the roles that
see every company type 2 letters of its name or BRN), **Action**, **Remarks**, **Start Date**, **End Date**, **Start
Time** and **End Time** (no time: the start is now and the end an hour later), **Owner** (you or someone in your team)
and **Completed**. The form says how many other activities the owner has that day, with a link to the calendar. Then
choose and **Add**:

- **Activity Type** (at least one is needed), **Participants** (the salespeople of the company in your team; the
  owner always takes part), **Project** (Managers, Admins, Sales Admins and MD: the open projects with the company),
  **Attendees** (the company's contacts) and **Related Prospects**. The cross on a chip takes it off.

Press **Save**. The Marketing role can read activities but not save them. Reminders and notifications are not kept here.

## Projects

The **Projects** page lists the projects in MyCRM that you or your team are in (Sales Admins see all of them):
**Open**, **Closed** or **All**, newest first. Search by the project's name, remarks or a company. Click a project to
open it. A project gives its members **temporary access** to its companies: a member who does not own a company through
their own territory gets a temporary territory with it while the project is open. Projects past their end date close
automatically each morning (the scheduled task *Projects that have ended*, once an administrator turns it on).

**Create project** (Managers, Admins, Sales Admins and MD) has two steps: the **Project name**, **Status**, **Start
date**, **End date** (after today) and **Remarks**, then **Next: add companies**: type a company's name, BRN or
website and tick the companies (at least one). **Create project in MyCRM** creates it, with you as its owner and
Project Manager, and opens it.

### A project

The project's page shows its status (**Open**, **Ended** or **Closed**), owner and end date, and four tabs:

- **Companies**, with their salesperson. **Add companies** (search and tick) and **Remove** (after asking; a project
  keeps at least one company) are for the project's owner and Sales Admins.
- **Members**, with their role and the companies their temporary territory gives them. Only the project's owner adds
  and removes members: **Add member**, choose the **Person** and their **Role in this project**; the box says whether
  they get temporary access. **Remove** (after asking) takes their temporary territory away. To change a role, remove
  the member and add them again.
- **Activities** linked to the project (from an activity's Project field); click one to read it.
- **History**: the changes made to the project in MyCRMRecords.

At the top right, the owner and Sales Admins have **Edit** (name, status, dates, remarks), **Close project** and
**Delete**. Both ask first and say how many members lose their temporary access. A closed project cannot be changed or
opened again; a deleted one disappears from Projects. Activities stay on their companies either way.

## Territories

For Sales Admins and MD. Each territory has a name, a number and a salesperson, and holds companies. A company is in
one territory: putting it in another moves it there. MyCRM's territories built from Advance are replaced by these.

- The list shows each territory on one line with its number, salesperson, how many companies it has and when it was
  created (and by whom); search by territory, number or salesperson. The switch beside the search shows **All** the
  territories in use, the **Vacant** ones (no salesperson, so their companies have no salesperson either) or those
  **Taken away**. Click a territory's name, or anywhere on its line, to open it.
- **New territory**: give its **Name**, **Number** (not used by another territory: the form says at once when it is)
  and its salesperson: search the people with the MyCRM role Sales and pick one, or **No salesperson (vacant)**.
  Press **Create territory**; it starts with no companies.
- A territory shows its number and salesperson under its name. Its buttons:
  - **Edit** changes its name, number or salesperson (**Save changes**). A new salesperson has all its companies from
    then on (the form says so before you save); each company's move is recorded in its Territory Movement History.
  - **Take away** takes the territory away: its companies are taken out of it (each move recorded). It is kept,
    marked as taken away. On a phone the button is **Take away territory**, at the bottom of the page.
  - **Companies** (the first tab, with how many there are): each company with its BRN, postal code and the date it was
    put in the territory. Search by name or BRN; tick companies (or **Tick all shown**): a bar shows how many are
    ticked, with **Clear** and **Take out of territory**, which takes them out (after asking).
  - **Add companies** (the second tab) puts companies in the territory. Pick one of three ways:
    - **Search**: type 2 letters or more of a company's name or BRN, or tick **Only companies with no salesperson**;
      tick companies (or **Tick all shown**) and press **Put the ticked companies here**. Each company shows where it
      is now, and whose it leaves.
    - **Paste a list**: paste company names or BRNs, one per line (or both, as copied from Excel), and press **Find the
      companies**.
    - **Excel workbook**: choose a workbook with the columns **Company** and **BRN** (**Download a sample** gives one).
    For a pasted list or a workbook, **Check the list** shows the companies **Found** (marked when they leave another
    salesperson) and those **Not found**, with why (**Company could not be found.**, or already in this territory).
    Press **Put the … found companies here**.
    Before anything changes you are asked to confirm (**Put them here**), with the companies that leave another
    salesperson listed.

## CRM users

For Sales Admins. A person's MyCRM role and teams decide what they see and do, here and in MyCRM.

- **People** (the first tab) lists everyone with a CRM profile, A to Z, one line each: name and login, role, teams,
  salesman code, territory code and number of territories. Type to search by name, login, email, code or role;
  **Role** shows the people of one role; **Show everyone** also lists the people who are not CRM users (everyone MyCRM
  knows, and everyone who has opened MyCRMRecords). **Find someone in Active Directory** finds a person MyCRMRecords
  does not know yet. On a phone, **Role:** opens a list of the roles (with **Show everyone** under it).
- **Edit** (or **Add** for someone who is not a CRM user), or a click on the person's line, opens them in a panel at
  the side (the whole screen on a phone):
  - **Role**: their MyCRM role; the line under it says how many teams the role takes.
  - **Team**: Sales, PP Specialist and CLETrainer pick one team. Manager, PP, MDS and SOL Specialist Managers and
    CLETrainerManager are put in the team named after them: press **Create team** if it is not there. CSST picks one
    team or **None**. Every other role ticks any number of teams (**Select All**, **Unselect All**); with none ticked
    the person is no longer a CRM user, as in MyCRM (the panel warns you).
  - **Details**: **Designation**, **Phone Number**, **Salesman Code** and **Territory Code**, and the person's
    territories (each opens the territory).
  - **Save** (**Add as CRM user** for someone new) keeps the person's records and changes them (MyCRM deleted and
    made them again, losing their history).
  - **Remove CRM user and territories** (under **Remove access**, after asking) makes the person no longer a CRM user
    and takes their territories away with their companies.
- **Teams** (the second tab): **New team** and **Edit** set a team's **Name**, **Short Name** and **Active**; the
  switch in the **Active** column switches a team on or off at once. Teams are never deleted. **People** (or the number
  of people) shows who is in a team.
- A change to someone's role or teams counts in MyCRMRecords at once; in MyCRM, from their next sign-in.

## Files

The PDFs kept in MyFile (MyFile's own app is where they are uploaded), newest first, 50 at a time. They are only shown
here: nothing is uploaded, changed or deleted on this page.

You see the files of the companies you may open (see [Companies](#companies)) and the files of no company. The roles
that open every company see every file.

- Type in **Search files** to find a file by its name, one of its numbers (invoice, DO, contract, service, reference,
  PPS, RMA, PSD or MSD reference), or its company's name or BRN.
- Choose a type: **All**, **Costing sheet**, **Request form**, **FSA**, **Leasing**, **DO** or **Others**; and when it
  was uploaded: **Uploaded: any time**, **Last 30 days**, **Last 12 months** or **This year**. The search and filters
  are kept for next time.
- Each row shows the file's name, type, company (click it to open the company), its first number (or the BRN), when it
  was uploaded and its size. Point at the number to see all of them.
- On a computer, click a file's name (or the eye) to show its PDF beside the list, with its numbers, company and who
  uploaded it; **Open full size** opens it in a new tab and **Download** saves it. The arrow in the row downloads it too.
- Tick files (or the box at the top for the whole page) and press **Download as zip** to save them in one zip file, at
  most 50 at a time; **Clear** unticks them. A zip is refused if one of the files is of a company you may not open.
- On a phone each file is a card: tap it to open its PDF on the whole screen.
- From a company's **Files** tab, **See all** opens this page with only that company's files; **Show every company**
  goes back to all of them.

Downloaded files are recorded in the audit log (showing a PDF in the page is not).

## Machines

Advance's machines in the field (through AdvanceConnector), 50 at a time, by customer. They are only shown here:
nothing is changed in Advance.

You see the machines of the companies you may open (see [Companies](#companies)): a machine belongs to the company with
its BRN. You also see the machines of the companies a Sales Admin gave you **temporary access** to (see
[Machine access](#machine-access)). The roles that open every company see every machine, once they search for one.

- Type in the search box at least 3 characters of a serial number, model, company name or BRN.
- Choose a coverage: **All**, **Contract** (a contract running today), **Warranty** (a warranty running today),
  **T&M** (time and materials), **Expired** (its contract or warranty has ended) or **No contract**; and a **Finance
  type**: Internal lease, External lease, Outright, Dealer or none. The search and filters are kept for next time.
- Each row shows the serial number (click it to open the machine), model, company (click it to open the company's
  Machines tab), **Access via** (your territory's name, **Project**, **Temporary**, or **All companies**), its
  coverage with the contract number and when it ends, and its finance type.
- **Machine access** (Sales Admins) opens the [Machine access](#machine-access) page.
- On a phone each machine is a card: tap it to open the machine.

## A machine

A machine's page: its serial number and coverage, its model, company and how you see it.

- The facts: when it was installed, its contract (number, type and dates), finance type, warranty, contact, location,
  address, BRN and Advance's customer number. For a machine on time and materials, its last service and invoice.
- **Copies per month**: the black and white (teal) and colour (coral) copies billed each month, from Advance's meter
  cards; choose **6 months** or **12 months**. Point at a month to see its numbers.
- **Service history**: the service calls done in the last 12 months, newest first, with the service type (and EM, PM
  or Others), the technician's code, the black and white and colour meter readings, and when they were entered and
  completed.
- **Toner supply**: the toner orders of the last 12 months, with their invoice.
- **Meter readings**: each invoice of the last 12 months with its black and white and colour copies.
- **Last contract** (a machine on time and materials that had one): its last contract's dates and, per meter type, the
  minimum bill, minimum copies, minimum rate and the rate thereafter.

## Machine access

For Sales Admins (open it with **Machine access** on the [Machines](#machines) page). It lists the people with
**temporary access**: they see a company's machines without the company being in their territories. The card at the
top says who can see a company's machines: 1 **Territory**, 2 **Project**, 3 **Temporary access**.

The temporary access is MyMachines' own: what is given here is given in MyMachines too, and the other way round. Until
an administrator ticks **Let MyCRMRecords change MyMachines' temporary access** (Settings, MyCRM tab) the page only
lists it and says so.

- **Give temporary access**: choose the **User**, type at least 2 characters of the **Company** name or BRN and choose
  it, then press **Give access**. The person sees the machines with the company's BRN (or Advance's customer name)
  from then on. A company without a BRN cannot be chosen; someone who already has access to a company is not given it
  twice.
- The list shows each person with their company, BRN and when it was given. The cross removes it (after asking); it is
  kept in MyMachines, marked as removed, not deleted.

## Costing sheets

MyCRMRecords keeps MyOrders' **costing sheets** and **request forms** readable: the lists, each one as MyOrders showed
it, their attachments and their prints. They are read where MyOrders keeps them and never changed: there is nothing to
create, edit, approve or delete in them here.

Who sees which costing sheets is decided as in MyOrders, by your MyCRM role and teams:

- **Sales** see their own costing sheets.
- **Managers** see their own and those of the salespeople in their teams; **MDS and SOL Specialists** those of the
  salespeople and specialists in their teams; the other roles with a team those of the people in their teams.
- **Head, MD, Admin, SalesAdmin, Credit, Credit Manager, Credit GM, Stock Allocator, Allocator, AP, AP Manager and
  Scheduler** see every costing sheet.
- **Service Delivery Managers** see their teams' costing sheets of DBS only (companies whose name has "dbs").
- **Administrators** (MyCRMRecords's own role) see every costing sheet.

If you see no costing sheets but expected some, your MyCRM user may have no role or team: ask a Sales Admin to check
it in MyCRM.


**Costing sheets** in the menu lists the costing sheets you may see, newest first, 50 at a time (**Previous** and **Next** at the
bottom). The line under the title says how many there are. On a phone, **Orders** in the bottom bar opens this list,
and **Costing sheets** / **Request forms** at the top switches between the two lists.

- **Search** finds a costing sheet by its number, status, reference, company, invoice number or order number. The list
  changes as you type.
- **Status**, **Sales rep** and **Specialist** narrow the list. **Specialist** is **Any**, **Has specialist** (the
  costing sheets with an MDS or Fusion (SOL) specialist, as in MyOrders), **No specialist**, or one specialist.
  On a phone, **Status:** opens the list of statuses, and the round filter button the other filters (press **Apply**).
  Someone MyOrders knew under two usernames (for example *RSP\alfriskuay* and *RSP\AlfrisKuay@ricoh.sg*) is one
  person here: listed once under **Sales Rep** and **Specialist**, with the costing sheets of both; and they see the
  costing sheets of both themselves.
- **Dates** shows the date filters: **From/To Creation Date**, **From/To Head Approval Date** and **From/To Invoice
  Date**.
- **Reset** clears the search and filters. MyCRMRecords remembers your search and filters for next time.
- Click a column heading to sort by it; click it again to reverse the order.
- **Download** saves the list as it is filtered, every page of it, as an Excel workbook.
- Click a costing sheet's number, its arrow or anywhere on its line to open it. The printer button prints it, when
  MyOrders would have let you (see [Printing a costing sheet](#printing-a-costing-sheet)).
- Each line shows the number, the reference, the company with its contact, the sales rep, the status, when it was
  created (and, on wide screens, updated and printed), the achieved price, the invoice number with the order number,
  the invoice date and (on wide screens) whether OPO checked it. On a phone each costing sheet is a card.
- A costing sheet deleted in MyOrders is not in the list.
- Opened from a company's page (**See all** under *Costing Sheets and Request Forms*), the list shows only that
  company's costing sheets, with no other filter; **Show every company** goes back to the whole list (and your own
  search and filters).
- **Sign in as…** (administrators) opens MyCRMRecords as someone else, chosen among everyone MyCRM or a costing sheet
  names, to see the costing sheets they see; see [User roles](#user-roles).

## A costing sheet

A costing sheet's page shows what MyOrders' costing sheet summary showed, read only. Under the number and status are
the reference, the company (click it to open the company) and the salesman. Then the key details: contact, customer number and type, billing address,
request form, the specialists (a yellow tag while their part was still pending), the dates, who it was transferred
from, the event tag, and the flags (ZBA, contract extension, fixed term, vital pricing, GMA pricing; those that apply
in blue).

The tabs **Product**, **Meter charges**, **Billing**, **Variable costs & trade-ins** and **More details** show one part
at a time, with **History** and **Attachments** beside them. On a phone each part is a card: tap it to open or close it.

- **History**: when it was created, approved, head approved, sent for the credit check, endorsed by credit, checked by
  OPO, made pending invoicing, given its order number, invoiced, printed, recalled, transferred and last updated, with
  who did it when MyOrders kept the name; then the order and invoice numbers, credit terms and total approved price.
- **Attachments**: the files attached in MyOrders, with their size and who uploaded them. **Download** saves one.
  Specialists' attachments are marked.
- **Product**: each product with its items (sub items and complex items) and their street and achieved prices, and the
  totals. A complex item shows *Not Available* for its street price when MyOrders had none for it. Heads, Admins and
  Sales Admins also see the percentages above the manager price and the landed cost, the deal margin and the
  commissions.
- **Meter Charges**: for each machine, whether it is under FSA, whether it is below the GM CPC, and its initial,
  monthly and thereafter copies and prices.
- **Billing**: finance type and amounts (months, interest, down and final payment, the monthly amount, the PPP rates),
  the external finance company, lease agreement number, transaction type, trade-in, billing address and remarks.
- **Variable costs & trade-ins**: the **Variable Costs** and the **Trade In(s)**.
- **More details**: the proposal, option and request form numbers, and what MyOrders kept on the costing sheet (contact
  name, phone, email, fax, delivery address, EPP, novation, PO and receipt completed).
- When the costing sheet has a request form whose products differ from it, a yellow note and the **Costing sheet and
  request form** box list the products whose quantities differ.
- A costing sheet deleted in MyOrders still opens from a link, with a red note saying when and by whom it was deleted.

## Printing a costing sheet

**Print** (on a costing sheet's page, or the printer button in the list) opens MyOrders' costing sheet PDF as a page in
a new tab. On the costing sheet's page, **Layout** beside **Print** chooses **New CS** or **Old CS** first (and is
remembered); the print page can switch too. As in MyOrders, it is there once the costing sheet is **Pending Credit Check**, **Pending Credit GM Check**,
**Pending Invoicing** or **Invoiced**, for Managers, Heads, GM, PP Specialist Managers, Specialist Senior Managers,
Stock Allocators, AP, AP Managers, Credit GM, MD, Admins and Sales Admins.

- **New CS** is MyOrders' newer costing sheet: details, items, variables, meter charges, the financing summary and the
  approvals. **Old CS** is the first one: the items three to a page with the particulars and variable costs, the meter
  charges, the financing page with the internal financing calculation and verification, the service credit per machine
  when the machines' rates differ, and the specialists' solutions and packets.
- **Print / Save as PDF** opens the browser's print. To keep a PDF, choose **Save as PDF** as the printer: the file is
  named *CostingSheet #&lt;number&gt;*, as MyOrders named it.
- Printing here does not change the costing sheet's *Printed At* (MyOrders set it when it printed).

## Request forms

**Request forms** in the menu lists the request forms you may see, newest first, 50 at a time. Who sees which request
forms is decided as for the costing sheets (your own, your team's, or every one for the roles that see all; Service
Delivery Managers DBS's only). As in MyOrders' specialists' list, you also see the request forms you are the MDS or
Fusion specialist of; MDS and Fusion specialist managers and specialist senior managers see every request form with a
specialist, and PP specialists and their managers those whose PP specialist is in their team.

- **Search** finds a request form by its number, status, reference or company. The list changes as you type.
- **All** / each type (for example **Delivery** and **Collection**), **Status** and **Sales rep** narrow the list;
  **Created** shows **From/To Creation Date**. On a phone, **Costing sheets** / **Request forms** at the top switches
  between the two lists, **Status:** opens the list of statuses, and the round filter button the other filters (press
  **Apply**).
- **Reset** clears the search and filters. MyCRMRecords remembers your search and filters for next time.
- Click a column heading to sort by it; click it again to reverse the order.
- **Download** saves the list as it is filtered as an Excel workbook, MyOrders' Download: a row for each request form
  and delivery or collection date.
- Click a request form's number, its arrow or anywhere on its line to open it. The printer button prints it, when
  MyOrders would have let you (see [Printing a request form](#printing-a-request-form)).
- Each line shows the number with the reference, the type, the company with its contact, the sales rep, the status
  (with **Pending print** when MyOrders had not printed it since it reached a printable status or was recalled), the
  delivery or collection dates, when it was created and (on wide screens) printed. Point at the status or the date to
  see when it was updated, recalled and printed and whether OPO checked it. On a phone each request form is a card
  with all of these.
- A request form deleted in MyOrders is not in the list.
- Opened from a company's page, the list shows only that company's request forms; **Show every company** goes back
  to the whole list.

## A request form

A request form's page shows what MyOrders' request form summary showed, read only. Under the number are its type and
status, the reference, the company (click it to open the company) and the salesman. Then the key details: contact and their phone numbers, customer number
and type, billing address, its costing sheet (click it to open the costing sheet), the specialists (a yellow tag while
their part was still pending), the dates, the event tag and the delivery declaration.

The tabs **Items** and **Delivery** (or **Collection items**) and **More details** show one part at a time, with
**History** and **Attachments** beside them. On a phone each part is a card: tap it to open or close it.

- **Items** (every type but Collection): each product with its sub items and complex items, their quantities and street
  prices, the item's street price and the **Request Total Street Price**, as MyOrders stored them.
- **Delivery**: each product with its parts, where and when it is delivered, to whom, Is FSA, No Lift, the remarks,
  and the model and serial number.
- **Collection items** (Collection): the collection details of the first item (collection type, contract, fees, asset
  type, brand, date, address, contacts, instructions), then every item collected.
- **History**: when it was created, approved by the manager and the head, sent for the credit check, endorsed and
  approved by credit, checked by OPO, sent, printed, recalled and last updated, with who did it when MyOrders kept the
  name; then the sales order, picking slip and invoice numbers.
- **Attachments**: the files attached in MyOrders, with their size and who uploaded them. **Download** saves one.
- **More details**: the proposal, option and previous request form, when it was printed, EPP and recon billed.
- When its costing sheet's products differ from it, a yellow note and the **Costing sheet and request form** box list
  the products whose quantities differ.
- A request form deleted in MyOrders still opens from a link, with a red note saying when and by whom it was deleted.

## Printing a request form

**Print delivery form** or **Print collection form** (on a request form's page), or the printer button in the list,
opens MyOrders' request form PDF as a page in a new tab. As in MyOrders, it is there once the request form is **Pending Credit Check**, **Pending Allocation**,
**Pending Invoicing** or **Backordered**, for Sales Admins, Admins, Managers, Heads, MD, Stock Allocators, Schedulers,
Credit GM, PP Specialist Managers and Specialist Senior Managers.

- A **Collection** prints the collection form: the type of request, the customer and collection information, every item,
  who created and approved it, and the signature boxes.
- The other types print the **delivery form** as MyOrders printed it last: a part for each delivery (address, date,
  contact), its lines 25 to a page with the item codes, serial numbers and allocated quantities, and the QR code of the
  request form's number. MyOrders made the delivery form each time it printed; a request form MyOrders never printed has
  none to show, and the page says so.
- **Print / Save as PDF** opens the browser's print. To keep a PDF, choose **Save as PDF** as the printer: the file is
  named *RequestForm #&lt;number&gt;*, as MyOrders named it.
- Printing here does not change the request form's *Printed At*.

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
- **Connections**: the OutSystems database (MyCRM's tables and its users, MyOrders' for the costing sheets and
  request forms, and MyFiles' for the Files page, only read; MyMachines' temporary access; their module names are under
  **Names in those databases**) and AdvanceConnector (Advance's customers, for the customer numbers, and Advance's
  machines for the Machines pages; the tables' names are under **Names in those databases** too). **Test** saves the tab and checks every table and column MyCRMRecords reads.
- **Email**: the mail server. **Send emails** switches emails on; **Send a test email** checks it. **Emails** lists
  every email, sent or not, and why not. MyCRM's Users, Territory and Company screens send no emails.
- **Scheduled tasks**: **Run the scheduled tasks** switches them on, and each task has its own **On**, time and days.
  **Customer numbers from Advance** (MyCRM's UpdateCustomerNo, daily at 02:45) gives each active company the
  customer number of Advance's customers with its BRN (with " #Multiple" after it when several share the BRN).
  **Projects that have ended** (MyCRM's SetInactiveAllProjectThatEnds, daily at 05:30) closes the open projects whose
  end date has come and takes away the temporary territories of members whose project is no longer open.
  **Run now** runs a task at once. Switch MyCRM's own timer off when this one runs.
- **MyCRM**: MyCRMRecords reads and writes the CRM users and teams, the companies with their addresses, contacts,
  phones, emails, relationships and group names, the permanent territories with their companies, the territory
  movements, the activities, and the projects with their temporary territories in MyCRM's own tables (**Connections**),
  so both always show the same data. Prospects and the CSST part's temporary territories are only read.
  - **Let MyCRMRecords change MyCRM's data**: tick it and press **Save** to let people save in MyCRMRecords. Until then
    MyCRMRecords only reads. It adds and changes rows of MyCRM's tables and never deletes one: what is taken away is
    made inactive there (a company's address link or a group name, which MyCRM cannot mark, is kept and marked here).
    The OutSystems database's login needs to be allowed to add and change rows (INSERT and UPDATE) in MyCRM's tables.
  - **Let MyCRMRecords change MyMachines' temporary access**: tick it and press **Save** to let Sales Admins give and
    remove temporary access to machines on the [Machine access](#machine-access) page. Until then the page only lists
    it. Giving adds a row to MyMachines' TempAccess; removing marks the row ("X:" before its customer name and BRN) and
    never deletes it. Every value written is listed under **Changes made in MyCRM** (table MyMachines.TempAccess).
  - **Changes made in MyCRM** lists every value MyCRMRecords wrote in MyCRM's tables, newest first, with the value it
    replaced and who made the change, so anything can be put back. The search box finds a table, column, value or
    person.
  - MyCRMRecords used to keep its own copy of MyCRM's data and sync it. To remove the copy, run **MyCRM's data stays in
    MyCRM** on the **Data migrations** tab. It keeps what MyCRM has no place for (each territory's name, who took
    something away and when, a CRM user's former team), removes only MyCRMRecords' copy (MyCRM keeps everything), and
    keeps people, roles and the audit log. It does nothing when something would be lost: an import still recorded as
    running, a record that exists only in MyCRMRecords, or a change the last sync never confirmed.

## Deployment

For administrators: the version installed, when, and what changed in it.

## Audit log

For administrators: every change made through MyCRMRecords and every sign-in, newest first. **Only what did not
succeed** shows the failures.
