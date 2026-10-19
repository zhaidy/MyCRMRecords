# MyCRMRecords user guide

MyCRMRecords keeps the customers' and employees' feedbacks: customer complaints, potential customer complaints, internal
challenges and areas of improvement. Each feedback is acknowledged by its person in charge, investigated and resolved
with tasks, and its satisfaction confirmed, against the service targets: acknowledged within 24 hours, resolution
started within 2 business days and resolved within 10 (weekends and public holidays are left out). It replaces MyCRM.
Sign in with your Windows (RSP) username and password.

Nothing is ever deleted for good: a feedback, task or file deleted, or a list's entry or a role taken away, is kept in
the database, marked, and no longer shown.

Who does what:

- **Everyone** signed in creates feedbacks, and sees the feedbacks they created, are in charge of, have a task in or
  are copied on.
- The feedback's **person in charge** acknowledges it, investigates it, adds and removes its tasks and works out the
  root cause.
- A **task's person in charge** completes it, and may add more tasks of the same kind.
- **Feedback Admins** (MyCRM's Admin) see every feedback, keep the **Lists**, add the customer details files, set the
  satisfaction level and may sign in as someone else.
- **BE** closes the cases whose satisfaction is confirmed, and deletes feedbacks.
- **HR** accepts or turns down the suggestions of internal challenges.
- **Administrators** do everything, and keep the settings and the user roles.

## Feedbacks

The first page: the feedbacks you see, newest first (click a column heading to sort by it), 50 a page.

- The buttons at the top show how many feedbacks there are in each status (**All**, **Pending HR**, **New**,
  **Acknowledged**, **Investigation / Resolution**, **Confirmed Satisfaction**, **Closed**): click one to show only
  those.
- Search by number, company, contact, status, division or a person; **Division** shows one division's feedbacks.
- The button with the number opens the feedback.
- **New feedback** opens an empty feedback.
- Feedback Admins: **Sign in as…** opens MyCRMRecords as someone else, to see and do what they would. A bar at the top says
  so; **Return to** goes back to your own account. Everything you do is recorded as done by you on their behalf. An
  administrator cannot be chosen.

## A feedback

The page shows the sections that fit the feedback's category and type; a field you cannot change shows as text.

- **Feedback**: the **Feedback category** (Customer Complaint, Potential Customer Complaint, Internal Challenges or
  Area of Improvement), the **Division** and **In charge**. Choosing a division makes its person in charge the
  feedback's. Once saved, the KPI table shows the service targets and the time taken (red when late).
- **Customer details** (customer complaints and areas of improvement): the company, contact, email, **Medium**, **Date
  received** and **Time received**, and the customer details **Files** (uploaded and removed by Feedback Admins).
- **Problem identification**: the **Feedback type** (**What the feedback types mean** explains them), **Category**,
  **Description** and **Copy to** (type a name or an email address and press Enter; the × takes one off). For an
  internal challenge, tick **Might cause a customer complaint**, and **I have a suggestion or solution** to describe
  it and tick its criteria and Ricoh Way values.
- **Create the feedback** saves a new feedback and keeps it open, so you can add its files; the person in charge, the
  Copy To people and the category's recipients are told by email. While a feedback is **New**, its details can still
  be changed.
- **HR - ESS** (internal challenges): a suggestion waits in **Pending HR** until HR chooses **Yes** (it becomes
  **New**) or **No** (it is closed) in **HR accept** and saves.
- **Acknowledgement**: the person in charge ticks **Acknowledged** and saves; the date, the time and their name are
  kept, and the feedback is **Acknowledged**.
- **Preliminary investigation / findings** and **Resolution** (not for a Feedback / Improvement): the person in charge
  fills in **First look at the feedback** and the **Root cause**, and adds **Corrective actions** and **Preventive
  actions** with **Add task** (an action plan, a person in charge and a due date; the person is told by email). Each
  task's person ticks it done or changes it with **Edit**; the bin removes it. With the first look, the root cause and
  a corrective task saved, the feedback moves to **Investigation / Resolution**; once every task is done, BE is told.
- **Customer / employee satisfaction**: a Feedback Admin chooses the **Satisfaction level** and saves: the feedback is
  **Confirmed Satisfaction**. Its **Files** can be uploaded by anyone who opens it.
- **Save** saves what you changed and goes back to the list. Files (up to 20 MB) are uploaded at once with
  **Upload**; **Remove** takes one off after you confirm.
- BE: **Close case** closes a feedback whose satisfaction is confirmed; **Delete** takes a feedback away, after you
  confirm.

## Statistics

The figures of a financial year (April to March; choose it in **Financial year**), from the completed feedbacks:

- **Average of the 24hr acknowledgement for the month**, **Average of 2 days resolution for the month** and **Average
  of 10 days close case for the month**: each month's average against its target (the line); months over the target are
  red.
- **Resolved within the service standard for the month**: how many were resolved within 10 business days, and how many
  were not.
- **Feedback category** and **Division involved in feedback**: how many feedbacks of each.
- Point to (or tab to) a column or bar for its figures; click it to list the feedbacks behind it.
- **Report**: tick the months and press **Download** for the feedbacks received in them, whatever their status, in
  Excel.

## Lists

For Feedback Admins: what the feedbacks are chosen from.

- **Divisions**: each division with its person in charge. **Add a division**, or **Edit** one; the bin takes it away.
- **Feedback categories** and **Feedback mediums**: their order, and whether new feedbacks are offered them
  (**Active**). Feedbacks keep the one they have.
- **KPI dates**: each feedback's dates the timings count from. Press the number of a feedback, correct its dates and
  press **Save**: its timings are worked out again.

## User roles

For administrators. Everyone signed in can open MyCRMRecords and create feedbacks; the roles open the rest.

- **Find a person** searches Active Directory; tick their roles and press **Save**.
- **People with a role** lists everyone with a role; untick a role and press **Save** to take it away. The role is kept
  in the database, marked as taken away (when, and by whom), not deleted.
- **Sign in as** opens MyCRMRecords as that person, to see what they see. A bar at the top says so; **Return to**
  goes back to your own account. Everything you do is recorded as done by you on their behalf.

The roles: **Feedback Admin**, **BE** and **HR** (as above), and **Administrator** (everything).

## Settings

For administrators. Everything that sends or changes something outside MyCRMRecords starts switched off.

- **Database**: where MyCRMRecords keeps its data. **Test connection** checks a SQL Server database; **Save and
  switch** moves to it.
- **Data migrations**: changes to the database, each run with **Run migration**.
- **Connections**: the OutSystems database (MyCRM's tables and its users) and AdvanceConnector (the public holidays,
  left out of the business days). **Test** saves the tab and checks every table and column MyCRMRecords reads.
- **Email**: the mail server and who is told of each new feedback by its category (**Told of each new internal
  challenge**, **Told of each new customer complaint or area of improvement**, **Told of each other new feedback**) and
  BE's address for the resolved feedbacks. **Send emails** switches the emails on; **Send a test email** checks it.
  **Emails** lists every email, sent or not, and why not. While MyCRM is still used, its own emails go too: switch
  these on when people move to MyCRMRecords.
- **Import from MyCRM**: **Read MyCRM's data** counts what is there; **Import** copies the feedbacks with their tasks and
  files, the divisions, categories and mediums, and the people with MyCRM's roles (MyCRM's Admin becomes **Feedback
  Admin**). It can be run again: what was imported before is skipped. If the website restarts while an import runs, the
  import shows **Stopped**: press **Import** again to carry on from where it stopped.
- **Sync with MyCRM**: while MyCRM is still used, keeps both in step. Tick **Sync with MyCRM** and press
  **Save** to turn it on (right after the import, before people use MyCRMRecords); it syncs at once, then every few
  minutes (**Every (minutes)**).
  - What is added or changed in either app reaches the other: the feedbacks, their tasks and files, and the lists.
    Changed in both since the last sync, MyCRMRecords's version is kept.
  - Nothing is ever deleted in MyCRM: a feedback, division, category or medium taken away here is made inactive there,
    a task or file is taken off its feedback, and a file gets "(removed)" before its name.
  - While it is on, a new feedback takes its number from MyCRM, so the numbers are the same in both.
  - The first sync after the import pairs each imported record with its row in MyCRM. Where the two differ, the
    record keeps MyCRM's version: what was imported is never written back to MyCRM.
  - **Preview** shows what a sync would do without changing anything. **Sync now** syncs at once.
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
