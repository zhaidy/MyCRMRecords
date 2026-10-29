# MyCRMRecords user guide

MyCRMRecords keeps the customers: the companies with their addresses, contacts (with their phones and emails),
parent and subsidiary companies, and who sells to them, in named **territories**; and the **CRM users**, MyCRM's
profile of each person (their MyCRM role and teams). It replaces the Users, Territory and Company part of MyCRM; the
rest of MyCRM (activities, prospects, projects, MIF, CSST…) stays in MyCRM and keeps working with the same companies.
It also keeps MyOrders' **costing sheets** and **request forms** readable, read only (see
[Costing sheets](#costing-sheets)). Sign in with your Windows (RSP) username and password.

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
- **Costing Sheets and Request Forms**: the company's newest costing sheets and request forms from MyOrders, of those
  you may see; click a number to open it. **See all** opens the list of all of them.

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
bottom).

- **Search** finds a costing sheet by its number, status, reference, company, invoice number or order number. The list
  changes as you type.
- **Costing Sheet Status**, **Sales Rep**, **Specialist** and **Has Specialist** narrow the list. **Has Specialist**
  **Yes** shows the costing sheets with an MDS or Fusion (SOL) specialist, **No** those without, as in MyOrders.
  Someone MyOrders knew under two usernames (for example *RSP\alfriskuay* and *RSP\AlfrisKuay@ricoh.sg*) is one
  person here: listed once under **Sales Rep** and **Specialist**, with the costing sheets of both; and they see the
  costing sheets of both themselves.
- **Dates** shows the date filters: **From/To Creation Date**, **From/To Head Approval Date** and **From/To Invoice
  Date**.
- **Reset** clears the search and filters. MyCRMRecords remembers your search and filters for next time.
- Click a column heading to sort by it; click it again to reverse the order.
- **Download** saves the list as it is filtered, every page of it, as an Excel workbook.
- **Open** (or the number) opens the costing sheet. The printer button prints it, when MyOrders would have let you
  (see [Printing a costing sheet](#printing-a-costing-sheet)).
- Each row shows the reference under the number, the contact under the company, when it was created, updated and
  printed, the achieved price, the invoice number with its date and the order number, and whether OPO checked it.
- A costing sheet deleted in MyOrders is not in the list.
- Opened from a company's page (**See all** under *Costing Sheets and Request Forms*), the list shows only that
  company's costing sheets, with no other filter; **Show every company** goes back to the whole list (and your own
  search and filters).
- **Sign in as…** (administrators) opens MyCRMRecords as someone else, chosen among everyone MyCRM or a costing sheet
  names, to see the costing sheets they see; see [User roles](#user-roles).

## A costing sheet

A costing sheet's page shows what MyOrders' costing sheet summary showed, read only:

- **Costing sheet**: reference, salesman, status, the specialists (a yellow tag while their part was still pending),
  dates, company (click it to open the company), billing address, contact, customer number and type, and the flags (ZBA, contract extension, fixed
  term, vital pricing, GMA pricing), with who it was transferred from.
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
- **Variable Costs** and **Trade In(s)**.
- **More details**: the proposal, option and request form numbers, and what MyOrders kept on the costing sheet (contact
  name, phone, email, fax, delivery address, EPP, novation, PO and receipt completed).
- When the costing sheet has a request form whose products differ from it, a yellow note and the **Costing sheet and
  request form** box list the products whose quantities differ.
- A costing sheet deleted in MyOrders still opens from a link, with a red note saying when and by whom it was deleted.

## Printing a costing sheet

**Print** (on a costing sheet's page, or the printer button in the list) opens MyOrders' costing sheet PDF as a page in
a new tab. As in MyOrders, it is there once the costing sheet is **Pending Credit Check**, **Pending Credit GM Check**,
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
- **Request Form Status**, **Request Form Type**, **Sales Rep** and **From/To Creation Date** narrow the list.
- **Reset** clears the search and filters. MyCRMRecords remembers your search and filters for next time.
- Click a column heading to sort by it; click it again to reverse the order.
- **Download** saves the list as it is filtered as an Excel workbook, MyOrders' Download: a row for each request form
  and delivery or collection date.
- **Open** (or the number) opens the request form. The printer button prints it, when MyOrders would have let you (see
  [Printing a request form](#printing-a-request-form)).
- Each row shows the reference and type under the number, the contact under the company, when it was created, updated
  and recalled with its delivery or collection dates, its status (with **Pending print** when MyOrders had not printed
  it since it reached a printable status or was recalled), when it was printed, and whether OPO checked it.
- A request form deleted in MyOrders is not in the list.
- Opened from a company's page, the list shows only that company's request forms; **Show every company** goes back
  to the whole list.

## A request form

A request form's page shows what MyOrders' request form summary showed, read only:

- **Request form**: type, reference, salesman, status, the specialists (a yellow tag while their part was still
  pending), dates, company (click it to open the company), billing address, contact and their phone numbers, customer number and type, event tag, its
  costing sheet (click it to open the costing sheet), and the delivery declaration.
- **Items** (every type but Collection): each product with its sub items and complex items, their quantities and street
  prices, the item's street price and the **Request Total Street Price**, as MyOrders stored them.
- **Delivery**: each product with its parts, where and when it is delivered, to whom, Is FSA, No Lift, the remarks,
  and the model and serial number.
- **Collection Items** (Collection): the collection details of the first item (collection type, contract, fees, asset
  type, brand, date, address, contacts, instructions), then every item collected.
- **History**: when it was created, approved by the manager and the head, sent for the credit check, endorsed and
  approved by credit, checked by OPO, sent, printed, recalled and last updated, with who did it when MyOrders kept the
  name; then the sales order, picking slip and invoice numbers.
- **Attachments**: the files attached in MyOrders, with their size and who uploaded them. **Download** saves one.
- When its costing sheet's products differ from it, a yellow note and the **Costing sheet and request form** box list
  the products whose quantities differ.
- A request form deleted in MyOrders still opens from a link, with a red note saying when and by whom it was deleted.

## Printing a request form

**Print** (on a request form's page, or the printer button in the list) opens MyOrders' request form PDF as a page in a
new tab. As in MyOrders, it is there once the request form is **Pending Credit Check**, **Pending Allocation**,
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
- **Connections**: the OutSystems database (MyCRM's tables and its users, and MyOrders' for the costing sheets and
  request forms, only read) and AdvanceConnector (Advance's customers,
  for the customer numbers). **Test** saves the tab and checks every table and column MyCRMRecords reads.
- **Email**: the mail server. **Send emails** switches emails on; **Send a test email** checks it. **Emails** lists
  every email, sent or not, and why not. MyCRM's Users, Territory and Company screens send no emails.
- **Scheduled tasks**: **Run the scheduled tasks** switches them on, and each task has its own **On**, time and days.
  **Customer numbers from Advance** (MyCRM's UpdateCustomerNo, daily at 02:45) gives each active company the
  customer number of Advance's customers with its BRN (with " #Multiple" after it when several share the BRN).
  **Run now** runs a task at once. Switch MyCRM's own timer off when this one runs.
- **MyCRM**: MyCRMRecords reads and writes the CRM users and teams, the companies with their addresses, contacts,
  phones, emails, relationships and group names, the permanent territories with their companies, and the territory
  movements in MyCRM's own tables (**Connections**), so both always show the same data. MyCRM's temporary territories
  (its projects and CSST) are only read, to decide who may open a company.
  - **Let MyCRMRecords change MyCRM's data**: tick it and press **Save** to let people save in MyCRMRecords. Until then
    MyCRMRecords only reads. It adds and changes rows of MyCRM's tables and never deletes one: what is taken away is
    made inactive there (a company's address link or a group name, which MyCRM cannot mark, is kept and marked here).
    The OutSystems database's login needs to be allowed to add and change rows (INSERT and UPDATE) in MyCRM's tables.
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
