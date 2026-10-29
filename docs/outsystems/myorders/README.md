# How MyOrders' costing sheets work (now in MyCRMRecords)

Read from the OutSystems module **MyOrders** (`MyOrders_v5471.oml`, 7 January 2026), decoded without Service Studio with
the MyApps project's `oml-reader.py` (the file is `OML+`: the reader now takes it). MyOrders is big: 90 entities, 59
screens, 230 web blocks, 363 actions; requests, allocation, proposals, packets, commissions, forecasts, advance orders
and marketing are in it too. **MyCRMRecords takes only the costing sheets, read only**, to keep their history
readable once MyOrders is retired (the decision of October 2026: the rest of MyOrders is decommissioned).

## 1. What moved, and what did not

| MyOrders | MyCRMRecords |
|---|---|
| The costing sheets list (`CostingSheets` block of flow `CostingSheet`), its filters, sort and Download | `default.aspx` |
| A costing sheet's summary (`CostingSheetSummary` and its blocks) | `costingsheet.aspx` |
| Attachments (`Attachments` block), download | download only |
| The costing sheet PDF: "Old CS" (`CostingSheetPDF`) and "New CS" (`CostingSheetPDF_New`, `CostingSheetHTML_New`) | `print.aspx` |
| Making and editing a costing sheet (`CostingSheetDetail` → `Item` → `MeterCharges` → `Billing` → `Finish`), from a proposal, option or prospect | not moved (read only) |
| Approvals, rejections, recall, delete, credit's customer number, Refresh Calculation, the Head's checkboxes, Save Remarks, attachments' upload and delete | not moved |
| Specialists' complex items and packets (`Specialist_CostingSheet`), orders and invoices (`CostingSheetOrder`), allocation, request forms, proposals, commissions | not moved (their costing sheet data shows on the costing sheet's page) |
| Download With Details (one Excel row per item, sub item and complex item, ~45 columns) | not moved: ask if it is needed |

## 2. Who sees which costing sheets

MyOrders has two roles of its own (`Admin`, `MyOrdersUser`); what people see comes from their **MyCRM** role, read by
MyOrders' reference to MyCRM: `Role(UserId)` (the role of the person's first CUser row) and `GetSubList(UserId)`
(MyCRM's GetSub: the OutSystems user Ids of the people whose data they see, kept in `Session.UserList` as `|12|34|`).

- **The list's rule** (the aggregate `GetCostingSheetsByCreatedAt` of the CostingSheets block):
  - `ShowAll()` is True for the roles **CreditManager, Credit, CreditGM, StockAllocator, Allocator, SalesAdmin, AP,
    APManager, MD, Admin, Scheduler** and one more whose name did not decode: **Head**, inferred (the list's default
    status filter is *Pending Head* for that role, and Head approves *Pending Head*). They see every costing sheet.
  - Everyone else: `Index(Session.UserList, "|" + SalesmanId + "|") <> -1`: the costing sheet's salesperson is in
    their GetSub list.
  - **ServiceDeliveryManager**: also `Company LIKE '%dbs%'`.
  - Only active costing sheets (`IsActive`).
- **The summary** (`CostingSheetSummary`) has no check of its own: anyone signed in who knows the number opens it.
- **The PDF** (web entry `CostingSheet` / `CostingSheetNew`) has no sign-in check: an `MD5(yyyyMMddHH + "r1c0h5P@niF")`
  token valid for the clock hour.
- **The print links** (Old CS, New CS) show for statuses Pending Credit Check, Pending Credit GM Check, Pending
  Invoicing and Invoiced, and the roles Manager, Head, GM, PPSpecialistManager, SpecialistSeniorManager,
  StockAllocator, APManager, AP, CreditGM, MD, Admin and SalesAdmin.
- **The margins** (percentage above manager price, above landed cost, deal margin) on the summary: Head, Admin,
  SalesAdmin.

## 3. The list

Filters: **Keyword** (status, reference, company, number: `Like "%" + keyword + "%"`), **Costing Sheet Status**,
**Sales Rep**, **From/To Creation Date**, **Specialist** (MDS, PP or MITS specialist), **From/To Head Approval Date**,
**Has Specialist** (-, Yes: an MDS or MITS specialist, No: neither; PP specialists do not count), **From/To Invoice
Date** (`MinInvoiceDate`). **Search** applies them; **Reset** clears them; sorted by creation date, newest first, 50 a
page. Columns: Id, Salesman, Reference, Company, Contact (first name), Created At, Updated At, Printed At, Achieved
Price, Costing Sheet Status, Invoice No, OPO Checked (OPO could tick it in the list), and the links Old CS, New CS and
PA (payment advice). **Download** (`Costing sheets.xlsx`) and **Download With Details** (`Costing sheets with
details.xlsx`) export the filtered list.

## 4. A costing sheet's summary

Title "Costing Sheet # <Id>". In order:

1. **Header**: Reference, Salesman, Costing Sheet Status, Specialists (PP, MDS, "Fusion" for MITS; orange while
   `PendingPP/MDS/MITS`, else green), Created At, Updated At, Recall At, Company, Billing Address (of `AddressId`),
   Contact, Customer No, Customer Type (MyCRM's CompanyGroup), Is ZBA, Is Contract Extension, Is Fixed Term, Is Vital
   Pricing, Transferred On, Original Salesman, GMA Pricing, Event Tag. Heads (and Managers for ZBA and contract
   extension) could tick the flags here, which re-ran the price calculation.
2. **Attachments** (`IsComplex = False`; the specialists' page lists `IsComplex = True`): file name and remarks; the
   uploader's trash icon really **deleted** the row; a file picker and **Upload**.
3. **Costing sheet vs request form** (when the costing sheet has a request form): on every load MyOrders runs
   `CompareCostingRequest`, which **deletes** the costing sheet's `CSRFItemMatch` rows (SQL `DELETE`) and inserts them
   again: per EDP code, the quantities of the active items, sub items and complex items of each side; the codes whose
   quantities differ are listed under "There are differences between Costing Sheet and Request Form".
4. **Product**: per active item, its name ("(Is Subscription)", OAA Pricing), achieved price, and a table Name |
   Quantity | Street Price (Unit) | Street Price (Total) | Achieved Price of the item, its active sub items and its
   active complex items (named after their product when they have no name, sorted by name, "Not Available" when their
   STP is 1 or less), with the item's TotalSTP; then Request Total Street Price, Total Achieved Price, Commissionable
   Revenue and, for Head/Admin/SalesAdmin, the margins. All totals are stored on the costing sheet.
5. **Billing**: Is PPP, Finance Type (Internal Financing, External Financing, Outright Sale), Finance/Outright Sales
   Amount; with financing: Months, Interest Rates, Down Payment, Final Payment, "Hardware Monthly" (PPP) or "Monthly
   Investment", Lease Agreement Number; PPP rates and volumes with internal financing; Bill To (external financing);
   Transaction Type; Trade In To; Billing Address (not with external financing); Remarks (StockAllocator could edit).
6. **Variable Costs** and **Trade In(s)** (the R2R and O2R trade-in variables).
7. **Meter Charges**: per item, for a configuration, mainframe or packet that is not MDS Services, MITS Solutions or
   Production Printing Solutions and not MDS or SOL: Under FSA, Below GM CPC (read by mistake from the Product box's
   list), and the initial, monthly and thereafter copies and prices (in cents).
8. Buttons by status and role: Edit, Approve, Reject, Approve with customer number, Checklist, Recall, Delete.

## 5. Statuses, approvals and emails

Statuses (`CostingSheetStatus`, matched by label): Draft, Pending Specialist, Pending EDP Code, Pending Manager, Pending
Head, Pending MD, Pending Credit Check, Pending Credit GM Check, Pending Invoicing, Invoiced, Manager Rejected, Head
Rejected, MD Rejected, Credit Rejected.

- **Manager approves** (Pending Manager; Manager, Head, MITS/MDS/PP Specialist Manager, Specialist Senior Manager):
  if `TotalAchievedPrice < TotalManagerPrice` → **Pending Head** (an email to the Head role when
  `Site.EnableEmailForManagerHead`); else `TotalAchievedPrice + TotalVariableCost <= 60,000` → **Pending Credit
  Check**, otherwise **Pending Credit GM Check**; `ApprovedAt`/`ApprovedBy` set the first time; the email
  CostingSheetEmail to the Credit Manager or Credit GM role, cc `servicecontracts@ricoh.sg` for a contract extension.
- **Head approves** (Pending Head; Head, Specialist Senior Manager): `HeadApprovedAt`, then the 60,000 rule.
- **Credit approves** (Pending Credit Check / Credit GM Check; Credit Manager, Credit GM, MD; only once OPO checked
  it): → **Pending Invoicing**; with `Site.EnableCreateCustomer` the Credit Manager enters a customer number, written to
  the costing sheet and to MyCRM's company.
- **MD approves** (Pending MD). Each rejection gives its Rejected status and clears the recon model and serial.
- **Recall** (Stock Allocator at the credit and invoicing statuses; the salesperson, Head or Manager before invoicing
  when not recon-billed): back to an earlier status, `RecallAt`.
- **Delete** (the salesperson, Draft): `IsActive = False` with `DeletedAt`/`DeletedBy` (marked, not deleted).
- Timers touching costing sheets: `UpdateCostingSheetsInvoiceDate` (invoice dates from the orders), `EmailCreditReminder`
  (08:30), `ScheduleGMAPricingReport` (09:00), `UpdateExpireMachineFromCostingSheets` (05:00), `UpdateReceiptDaily`
  (08:45), `ManagerSTP_Patch`. Once MyOrders is retired they stop: the costing sheets no longer change.

## 6. The costing sheet PDF

- **Old CS**: the list's `Print` action builds the URL of web entry `CostingSheet` (Token, CostingSheetId, IsLong),
  converts it with HtmlToPdfConverter (page number in the footer), **writes `CostingSheet.PrintedAt`** (an SQL
  UPDATE) and downloads `CostingSheet #<Id>.pdf`. Pages: item pages (3 items a page, a transposed table) with
  PARTICULARS, the variable costs and the totals on the first; meter pages (6 items a page); the FINANCING page with,
  for internal financing, the calculation boxes (`CSCal`: deal interest, final payment interest at twice the rate,
  service credit, total lease payable, amount of installment, the stored monthly lease when within a dollar of the
  worked-out one) and VERIFICATION (salesperson, sales manager or head, dates); service credit pages per item when the
  items' monthly rates differ (not PPP); the specialists' solutions and packets page.
- **New CS**: `CostingSheetHTML_New` shows the newer one-page layout with a **Download** button (web entry
  `CostingSheetNew`, also writing PrintedAt): details, flags, an Items table (each item then its sub items, packet items
  and complex items as lines 1.1, 1.2…, with the PAL release = quantity × approved special unit price), Variables,
  Meter Charges, Summary (the same calculation boxes) and Others (submitted, approved, verified, head).
- MyOrders' logos (`RICOH_logo.png`, `Restricted.png`) are extracted into the site.

## 7. Where it meets the rest

- **MyCRM** (read): `Company` (name, customer type, BRN/incorporation number, credit terms), `Contacts`, `Address`,
  `Phone`, `CompanyGroup`, `CUser` (role, team, salesman code), `Role`, `EventTag`.
- **OutSystems users** (`OSSYS_USER`): salesperson, specialists, approvers, uploaders.
- MyOrders' own tables: the costing sheet entities below, `Product`, `ProductGroup`, `PacketItem`, `RequestForm*`,
  and the static entities.
- **Links into MyOrders**: the approval emails link to `CostingSheetSummary.aspx?CostingSheetId=<Id>` (the summary;
  the same number in MyCRMRecords: `costingsheet.aspx?id=<Id>`). The PDF entries (`CostingSheet.aspx`,
  `CostingSheetNew.aspx`) take a token and are not for people to open.

## 8. UI that could be friendlier (improved in MyCRMRecords)

- The list waited for **Search**; the filters took a whole table; ten columns scrolled sideways on a laptop.
- No size or uploader for attachments; no history of who approved when (the dates were kept but not shown together).
- The PDF needed a converter on the server and a token; it could not be looked at before downloading.

## 9. Rules MyCRMRecords changes on purpose (MyOrders' part)

1. **Read only.** Nothing in MyOrders or MyCRM is ever written: no creating, editing, approving, rejecting, recalling,
   deleting, uploading or deleting attachments, Refresh Calculation, the Head's and Manager's checkboxes, Save
   Remarks or the OPO tick. MyOrders is being retired; its costing sheets are kept for looking back.
2. **Printing writes nothing**: MyOrders set `PrintedAt` on every print; MyCRMRecords shows the last time MyOrders
   printed it. The print is a page printed or saved as PDF by the browser (no converter, no page numbers), named
   `CostingSheet #<Id>` as MyOrders named the PDF; both layouts are on one page (New CS, Old CS).
3. **One access rule everywhere**: a costing sheet opened by its address, its attachments and its print are checked
   with the list's rule (MyOrders' summary had no check, and its PDF only an hourly token). Administrators of
   MyCRMRecords see every costing sheet.
4. **Below GM CPC** shows each item's own value (MyOrders read it from the Product box's list, so items could show
   another item's value).
5. **Costing sheet vs request form** is worked out in memory, the same sums, never deleting and inserting
   `CSRFItemMatch` rows as MyOrders did on every load.
6. **The list**: the search also finds invoice and order numbers and treats `%`, `_` and `[` as plain characters; the
   filters apply as they change (no Search button) and are remembered; the dates sit under the dates column, the
   reference under the number, the contact (full name) under the company, the order number and invoice date under the
   invoice number; the row's buttons come first; sorted newest first by number (MyOrders: by creation date, the same
   order). The Head's default filter (Pending Head) is not kept: there is no pending work here. **Download** has the
   list's columns plus head approval date, invoice date and order number; **Download With Details** is not carried over.
7. **A History box** (new) lists created, approved, head approved, sent for credit check, credit endorsed, OPO checked,
   pending invoicing, order number, invoiced, printed, recalled, transferred, deleted and last updated, with who did it
   when MyOrders kept the name: all from columns MyOrders keeps on the costing sheet.
8. **Attachments** show their size, uploader and date, the specialists' attachments (`IsComplex`) on the same page,
   marked; the list never reads the files' bytes.
9. **Deleted costing sheets** (`IsActive = False`) are not listed, as in MyOrders; opened from a link they show a red
   note with when and by whom (MyOrders showed them as if active).
10. **Missing MyCRM rows**: the company, address and contact are joined with LEFT joins, so a costing sheet whose
    contact or address was removed in MyCRM is still listed and opened (MyOrders' INNER joins hid it).
11. **BRN/UEN** on the New CS shows the company's incorporation number, else its BRN.
12. **Flags** (ZBA, Vital Pricing...) show as Yes/No; nothing is editable.
13. A **retired MyOrders** module (inactive in OutSystems' catalog) is still read: its tables stay in the database.
14. **One person, two OutSystems users** (`RSP\alfriskuay` and `RSP\AlfrisKuay@ricoh.sg`; or two usernames with one
    email address): MyOrders went by the signed-in user's Id, so a salesperson saw only the costing sheets of the user
    they signed in as, and the Sales Rep and Specialist filters listed them twice, each finding one user's costing
    sheets. MyCRMRecords counts both users as the person (by login once the address is dropped, or by email
    address, `Access.SamePerson`): they see both users' costing sheets, so does everyone whose MyCRM list has
    either user (their manager), and the filters list them once and find both.

15. **Request forms** (MyOrders' RequestForms, RequestFormSummary and its request form PDF) are kept too, read only, as
    the costing sheets. Who sees which is MyOrders' list rule (as the costing sheets'), and the list also shows what
    MyOrders' separate specialists' list (SpecialistRequestForms) showed: the request forms a person is the MDS or Fusion
    specialist of; every request form with a specialist for MDS and Fusion specialist managers and specialist senior
    managers; and for PP specialists and their managers those whose PP specialist is in their MyCRM list (MyOrders' role
    there did not decode: PP Specialist is assumed). MyOrders' summary and print had no check of their own (anyone with
    the link opened them); here they follow the list's rule, as for the costing sheets.
16. **Request form list**: a Request Form Type filter is added; the search applies as it is typed; the Manager/Head
    default of Pending Head is dropped (as for the costing sheets); the Delv / Collection Date shows every date; the
    OPO check box (which wrote) is a Yes/No; Download Allocation (an export of every column of the allocations) is not
    carried over.
17. **Request form page**: the costing sheet is a link in the header (MyOrders reached it only through the differences
    box); the differences are worked out in memory (MyOrders rewrote CSRFItemMatch on every visit); money in the
    collection details is shown as money; the Connectivity Requests box (MyHelpdesk's requests) is not carried over.
18. **Request form print**: the delivery form is the one MyOrders printed last (its DeliveryFormPrint rows): MyOrders
    rebuilt it and wrote PrintedAt on every print; MyCRMRecords writes nothing, so a request form MyOrders never
    printed shows no delivery form. The QR code of the number is drawn in the page.
19. **Part of MyCRMRecords** (2026-10-30): MyCRMRecords was merged into MyCRMRecords, so people have one app for
    the companies and their costing sheets. The pages, rules and prints are the same; the costing sheets list is
    `costingsheets.aspx` (it was the home page), and the API's own paths are `orders/session`, `orders/people` and
    `costingsheets/filters`. The access rule stays MyOrders' (by OutSystems user, `OrdersAccess`), apart from MyCRMRecords'
    company rule. MyOrders' tables keep being only read (`IMyOrdersData`, no writer), whatever MyCRMRecords' switch for
    MyCRM's data says.
20. **Linked with the companies**: a costing sheet's or request form's company opens its page in MyCRMRecords, and a
    company's page lists its newest costing sheets and request forms (of those the person may see) with **See all**,
    which opens the list filtered to that company (a filter MyOrders did not have).

## 10. The data model (the costing sheet part)

Users are OutSystems users; MyCRM's entities are referenced. Static entities' Ids differ between environments: matched
by label.

**CostingSheet**

| Attribute | Type | Mandatory |
|---|---|---|
| Id | Long Integer (auto) | yes |
| ProspectId | → MyCRM Prospect |  |
| ProposalId | → Proposal |  |
| OptionId | → Option |  |
| RequestFormId | → RequestForm |  |
| CostingSheetStatusId | → CostingSheetStatus |  |
| ReferenceId | → Reference |  |
| CompanyId | → MyCRM Company | yes |
| ContactId | → MyCRM Contacts | yes |
| AddressId | → MyCRM Address | yes |
| SalesmanId | → (OutSystems user) |  |
| CreatedBy | → (OutSystems user) |  |
| CreatedAt | Date Time |  |
| UpdatedBy | → (OutSystems user) |  |
| UpdatedAt | Date Time |  |
| IsActive | Boolean |  |
| MDSSpecialistId | → (OutSystems user) |  |
| PPSpecialistId | → (OutSystems user) |  |
| MITSSpecialistId | → (OutSystems user) |  |
| TotalSTP | Currency |  |
| TotalManagerSTP | Currency |  |
| TotalVariableCost | Currency |  |
| BillTypeId | → BillType |  |
| Remarks | Text |  |
| FinanceOutrightAmount | Currency |  |
| Months | Integer |  |
| InterestRate | Decimal |  |
| DownPayment | Decimal |  |
| MonthlyLease | Decimal |  |
| FinalPercentage | Decimal |  |
| FinalPayment | Decimal |  |
| TransactionTypeId | → TransactionType |  |
| TradeInToId | → TradeInTo |  |
| BillingAddress | → MyCRM Address |  |
| IsPPP | Boolean |  |
| AllZero | Boolean |  |
| PPPInternalBWRate | Currency |  |
| PPPInternalColourRate | Currency |  |
| PPPCustomerBWRate | Currency |  |
| PPPCustomerColourRate | Currency |  |
| PPPMonthlyBWVolume | Integer |  |
| PPPMonthlyColourVolume | Integer |  |
| LeaseAgreementNumber | Text |  |
| BillTo | → ExternalFinanceCompany |  |
| TotalAchievedPrice | Currency |  |
| PendingMDS | Boolean |  |
| PendingPP | Boolean |  |
| PendingMITS | Boolean |  |
| PrintedAt | Date Time |  |
| ApprovedAt | Date Time |  |
| ApprovedBy | → (OutSystems user) |  |
| DeletedBy | → (OutSystems user) |  |
| DeletedAt | Date Time |  |
| RecallAt | Date Time |  |
| TotalPacketSTP | Currency |  |
| IsReconBilled | Boolean |  |
| UseNewItem | Boolean |  |
| IsEPP | Boolean |  |
| PendingCreditOn | Date Time |  |
| CreditEndorsedOn | Date Time |  |
| IsOPOChecked | Boolean |  |
| OPOCheckedOn | Date Time |  |
| OPOCheckedBy | → (OutSystems user) |  |
| EstimatedCommission | Currency |  |
| ConfirmedCommission | Currency |  |
| OrderNo | Text |  |
| OrderUpdatedBy | → (OutSystems user) |  |
| OrderUpdatedOn | Date Time |  |
| InvoiceNo | Text |  |
| CustomerNo | Text |  |
| IsVitalCase | Boolean |  |
| CreditTerms | Text |  |
| TotalApprovedPrice | Currency |  |
| PendingInvoicingOn | Date Time |  |
| IsZBA | Boolean |  |
| IsNovation | Boolean |  |
| ContactName | Text |  |
| Phone | Phone |  |
| Email | Email |  |
| Fax | Phone |  |
| BillToStreet | Text |  |
| BillToCity | Text |  |
| BillToStateProvince | Text |  |
| BillToCountry | Text |  |
| BillToPostalCode | Text |  |
| DeliveryAddressId | → MyCRM Address |  |
| DeliverToStreet | Text |  |
| DeliverToCity | Text |  |
| DeliverToStateProvince | Text |  |
| DeliverToCountry | Text |  |
| DeliverToPostalCode | Text |  |
| IsPOCompleted | Boolean |  |
| IsReceiptCompleted | Boolean |  |
| PaymentTermId | → (PaymentTerm, another module) |  |
| IsFixedTermAgreement | Boolean |  |
| ITSAdminChecked | Boolean |  |
| PRVersion | Integer |  |
| TransferredOn | Date Time |  |
| OriginalSalesmanId | → (OutSystems user) |  |
| HeadApprovedAt | Date Time |  |
| InvoiceDateText | Text |  |
| MinInvoiceDate | Date |  |
| GMAPricing | Boolean |  |
| EventTagId | → MyCRM EventTag |  |
| TotalManagerPrice | Currency |  |
| CommissionableRevenue | Currency |  |
| PercentageAboveManagerPrice | Decimal |  |
| TotalFOB | Currency |  |
| PercentageAboveFOB | Decimal |  |
| IsContractExtension | Boolean |  |

**CostingSheetItem**

| Attribute | Type | Mandatory |
|---|---|---|
| Id | Long Integer (auto) | yes |
| CostingSheetId | → CostingSheet |  |
| ProductType | Text |  |
| ProductName | Text |  |
| EDPCode | Text |  |
| OriProductName | Text |  |
| ProductId | → Product |  |
| ItemId | → Item |  |
| Quantity | Integer |  |
| UnitPrice | Currency |  |
| TotalSTP | Currency |  |
| ProspectUnitPrice | Currency |  |
| InitialBWCopies | Integer |  |
| InitialBWPrice | Decimal |  |
| InitialCLRCopies | Integer |  |
| InitialCLRPrice | Decimal |  |
| MonthlyBWCopies | Integer |  |
| MonthlyBWPrice | Decimal |  |
| MonthlyCLRCopies | Integer |  |
| MonthlyCLRPrice | Decimal |  |
| MonthlyMonths | Integer |  |
| ThereafterBWPrice | Decimal |  |
| ThereafterCLRPrice | Decimal |  |
| MeterBWOnly | Boolean |  |
| IsFSA | Boolean |  |
| AchievedPrice | Currency |  |
| IsActive | Boolean |  |
| RequestFormItemId | → RequestFormItem |  |
| OptionItemId | → OptionItem |  |
| ProspectItemId | → MyCRM ProspectItem |  |
| IsRetain | Boolean |  |
| Model | Text |  |
| Serial | Text |  |
| PacketId | → Packet |  |
| IsPacket | Boolean |  |
| IsMDS | Boolean |  |
| IsPP | Boolean |  |
| IsSOL | Boolean |  |
| IsRecon | Boolean |  |
| EstimatedCommission | Currency |  |
| ApprovedSpecialUnitPrice | Currency |  |
| ApprovedInitialBWCopies | Integer |  |
| ApprovedInitialBWPrice | Decimal |  |
| ApprovedInitialCLRCopies | Integer |  |
| ApprovedInitialCLRPrice | Decimal |  |
| ApprovedMonthlyBWCopies | Integer |  |
| ApprovedMonthlyBWPrice | Decimal |  |
| ApprovedMonthlyCLRCopies | Integer |  |
| ApprovedMonthlyCLRPrice | Decimal |  |
| ApprovedThereafterBWPrice | Decimal |  |
| ApprovedThereafterCLRPrice | Decimal |  |
| IsSubscription | Boolean |  |
| OAAPricing | Boolean |  |
| BelowGMCPC | Boolean |  |
| TotalManagerPrice | Currency |  |
| CommissionableRevenue | Currency |  |
| TotalFOB | Currency |  |

**CostingSheetSubItem**

| Attribute | Type | Mandatory |
|---|---|---|
| Id | Long Integer (auto) | yes |
| CostingSheetItemId | → CostingSheetItem |  |
| ProductType | Text |  |
| ProductName | Text |  |
| EDPCode | Text |  |
| ProductId | → Product |  |
| ItemId | → Item |  |
| Quantity | Integer |  |
| UnitPrice | Currency |  |
| IsActive | Boolean |  |
| ApprovedSpecialUnitPrice | Currency |  |
| TotalManagerPrice | Currency |  |
| TotalFOB | Currency |  |

**CostingSheetComplexItem**

| Attribute | Type | Mandatory |
|---|---|---|
| Id | Long Integer (auto) | yes |
| CostingSheetItemId | → CostingSheetItem |  |
| ComplexItemTypeId | → ComplexItemType | yes |
| ProductId | → Product |  |
| PacketId | → Packet |  |
| ItemId | → Item |  |
| ProductName | Text |  |
| ProductType | Text |  |
| EDPCode | Text |  |
| Quantity | Integer | yes |
| AchievedPrice | Currency | yes |
| IsActive | Boolean |  |
| IsPP | Boolean |  |
| IsMDS | Boolean |  |
| IsMITS | Boolean |  |
| ApprovedSpecialUnitPrice | Currency |  |
| STP | Currency |  |
| Description | Text |  |
| Vendor | Text |  |
| PONumber | Text |  |
| PODate | Date |  |
| TotalCost | Currency |  |
| ReceiptNumber | Text |  |
| ReceiptDate | Date |  |
| ReceiptQuantity | Integer |  |
| IsPartialReceipt | Boolean |  |
| MPGCode | Text |  |
| QuotationReferenceNo | Text |  |
| CurrencyCode | Text |  |
| USDRate | Decimal |  |
| EURRate | Decimal |  |
| HasChange | Boolean |  |
| VendorId | → (Vendor, another module) |  |
| TotalBasePrice | Currency |  |
| UnitCost | Currency |  |
| Remarks | Text |  |
| SerialFlag | Boolean |  |
| TotalManagerPrice | Currency |  |

**CostingSheetComplexItemSnapshot**

| Attribute | Type | Mandatory |
|---|---|---|
| Id | Long Integer (auto) | yes |
| CostingSheetComplexItemId | → CostingSheetComplexItem |  |
| ComplexItemTypeId | → ComplexItemType | yes |
| PacketId | → Packet |  |
| ItemId | → Item |  |
| ProductName | Text |  |
| EDPCode | Text |  |
| Quantity | Integer | yes |
| AchievedPrice | Currency | yes |
| Vendor | Text |  |
| TotalCost | Currency |  |
| MPGCode | Text |  |
| QuotationReferenceNo | Text |  |
| CurrencyCode | Text |  |
| USDRate | Decimal |  |
| EURRate | Decimal |  |

**CostingSheetAttachment**

| Attribute | Type | Mandatory |
|---|---|---|
| Id | Long Integer (auto) | yes |
| CostingSheetId | → CostingSheet |  |
| Binary | Binary Data |  |
| FileName | Text |  |
| UploadedBy | → (OutSystems user) |  |
| UploadedOn | Date Time |  |
| IsComplex | Boolean |  |
| Remarks | Text |  |

**Variable**

| Attribute | Type | Mandatory |
|---|---|---|
| Id | Long Integer (auto) | yes |
| CostingSheetId | → CostingSheet |  |
| VariableNameId | → VariableName |  |
| Cost | Currency |  |
| TradeInItem | Text |  |
| Manufacturer | Text |  |
| Model | Text |  |
| SerialNo | Text |  |
| Remarks | Text |  |

**CSRFItemMatch**

| Attribute | Type | Mandatory |
|---|---|---|
| Id | Long Integer (auto) | yes |
| EDPCode | Text |  |
| Description | Text |  |
| CostingSheetQuantity | Integer |  |
| RequestFormQuantity | Integer |  |
| CostingSheetId | → CostingSheet |  |
| RequestFormId | → RequestForm |  |

**Reference**

| Attribute | Type | Mandatory |
|---|---|---|
| Id | Long Integer (auto) | yes |
| Reference | Text |  |
| Datetime | Date Time |  |
| Number | Integer |  |

**CostingSheetStatus**

| Attribute | Type | Mandatory |
|---|---|---|
| Id | Integer (auto) | yes |
| Label | Text | yes |
| Order | Integer | yes |
| Is_Active | Boolean | yes |

**VariableName**

| Attribute | Type | Mandatory |
|---|---|---|
| Id | Integer (auto) | yes |
| Label | Text | yes |
| Order | Integer | yes |
| Is_Active | Boolean | yes |
| ItemNo | Text |  |

**BillType**

| Attribute | Type | Mandatory |
|---|---|---|
| Id | Integer (auto) | yes |
| Label | Text | yes |
| Order | Integer | yes |
| Is_Active | Boolean | yes |

**TradeInTo**

| Attribute | Type | Mandatory |
|---|---|---|
| Id | Integer (auto) | yes |
| Label | Text | yes |
| Order | Integer | yes |
| Is_Active | Boolean | yes |

**TransactionType**

| Attribute | Type | Mandatory |
|---|---|---|
| Id | Integer (auto) | yes |
| Label | Text | yes |
| Order | Integer | yes |
| Is_Active | Boolean | yes |

**ExternalFinanceCompany**

| Attribute | Type | Mandatory |
|---|---|---|
| Id | Long Integer (auto) | yes |
| Name | Text |  |
| IsActive | Boolean |  |
