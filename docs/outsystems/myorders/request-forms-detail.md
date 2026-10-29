<!-- Read from MyOrders_v5471.oml (2026-10-20) for MyCRMRecords' request forms. "The viewer" is MyCRMRecords; $S/out was the decoded module. -->

# MyOrders request form detail: what it shows (spec for the read-only viewer)

Source: MyOrders_v5471.oml, decoded with oml-reader (+ tools/widgets.py, tools/linear.py for expressions). The screen
people look at a request form on is **RequestFormSummary** (flow RequestForm, key IesqZ1cA9UOhZyk2Zsqg_A), input
parameter **`RequestFormId`** (redirect: `RequestFormSummary.aspx?RequestFormId=<id>`; the CS/RF compare links and the
Approve/Reject/Recall actions all come back to it with that name). If `RequestFormId = NullIdentifier()` it goes to
the RequestForms list.

`RequestFormDetail` is only step 1 of the edit wizard (company, prospect, proposal, costing sheet, contact, address,
previous request form, type; "Next >> Item"): editing, left out. Blocks named in the task that are **not** on the
summary: `RequestFormItem`, `SelectedRequestForm` (flow Allocation), `ComplexItem_RequestForm(_New)`,
`Packets_RequestForm(_New)` (flow Specialist_RequestForm; row Save/Delete actions = editing), `RequestFormHeader`
(flow RequestFormPDF: the print's header, §5). No solutions section on the summary (`RequestFormSolution` is edited in
the wizard only).

The summary embeds: `Phone` (Common), `Attachments` (the RequestForm-flow block, key qkM8lIhC…, IsComplex = False),
`CSRFItemCompare` (in a late-load wrapper), `RequestFormItemSummary` (one per item, Items box),
`RequestFormItemPreview` (one per item, Delivery table; embeds `PacketPreview_New` / `PacketPreview`), `Helpdesk`
(Connectivity Requests).

Notes on reading the decode: as in spec-detail.md. In addition:
- Widgets decode role keys with guessed names: `~hokYi5u…` = **Head** (= XyDg8VkHm0aABtYp6Il6CA in the flows: it is
  the role with Approve at Pending Head), `~Manager` = **Manager** (9PD_Is3O5UC51jGmtBri3A). One role key
  `s0ZijIrDCEijLhkFzYzXYg` stays unresolved (only used for the Model/Serial edit rights, left out).
- `Company Group.SME` = MyCRM CompanyGroup **Label**; `vent T.TechTarget` = the Event Tag record's label (as in the
  costing sheet header). `Phones.CRMPhoneId` = the phone **number** column (inferred; MyCRM names shifted).
- Address shown in the header decodes as `BuildingName, UnitNumber, BuildingName, CRMAddressId` + "Singapore " +
  postal code: same expression as the costing sheet header, i.e. Block, Street, UnitNumber, BuildingName,
  "Singapore " + PostalCode by intent. Contact: FirstName, (MiddleName, decodes as CRMContactLinkId), LastName.
- Formats: `FormatCurrency(v, Site.Currency, 2, Site.DecimalSeparator, Site.GroupSeparator)` unless said otherwise.

---------------------------------------------------------------------------------------------------------------------

## 1. Data sources (preparation of RequestFormSummary)

`Role(Session.CurrUserId)` (MyCRM role, used for the buttons), then:

### 1.1 GetRequestFormById (one row)
```
RequestForm (filter RequestForm.Id = @RequestFormId)        -- note: NO IsActive filter
  INNER RequestFormType          ON RequestForm.RequestFormTypeId = RequestFormType.Id
  INNER Companies (MyCRM)        ON RequestForm.CompanyId = Company.Id
  INNER Address   (MyCRM)        ON RequestForm.AddressId = Address.Id
  INNER Contacts  (MyCRM)        ON RequestForm.ContactId = Contacts.Id
  LEFT  Reference                ON RequestForm.ReferenceId = Reference.Id
  LEFT  Users (OSSYS_USER)       ON RequestForm.SalesmanId = Users.Id
  LEFT  RequestFormStatus        ON RequestForm.RequestFormStatusId = RequestFormStatus.Id
  LEFT  CompanyGroup (MyCRM)     ON Company.CompanyGroupId = CompanyGroup.Id
  LEFT  Event Tag (other module) ON RequestForm.EventTagId = EventTag.Id
```
Viewer: LEFT joins for type/company/address/contact too; "-" when missing.

### 1.2 GetRequestFormItemsByRequestFormId (list; Items box, Delivery table, Collection Items)
```
RequestFormItem (filter RequestFormId = @id AND IsActive = 1)  ORDER BY RequestFormItem.Id
  LEFT Brand           ON RequestFormItem.BrandId = Brand.Id
  LEFT CollectionType  ON RequestFormItem.CollectionTypeID = CollectionType.Id
  LEFT AssetType       ON RequestFormItem.AssetTypeId = AssetType.Id
  LEFT Phones (MyCRM)  ON RequestFormItem.PhoneId = Phones.Id
  LEFT Product         ON RequestFormItem.DeliveryProductId = Product.Id
  LEFT ProductGroup    ON Product.ProductGroupId = ProductGroup.Id
```

### 1.3 Per item (RequestFormItemSummary / RequestFormItemPreview, input RequestFormItemId)
```
GetRequestFormItemById:   RequestFormItem WHERE Id = @itemId
GetRequestFormSubItemsByRequestFormItemId:
  RequestFormSubItem WHERE RequestFormItemId = @itemId AND IsActive = 1        (no sort decoded; use Id)
GetRequestFormComplexItemsByRequestFormItemId:
  RequestFormComplexItem WHERE RequestFormItemId = @itemId AND IsActive = 1
    LEFT Product ON ComplexItem.ProductId = Product.Id
  calculated ProductName = If(ComplexItem.ProductName = "", Product.Name, ComplexItem.ProductName)
  (no sort decoded; the costing sheet's twin sorts by ProductName (inferred same))
```
Packet items (when `RequestFormItem.IsPacket`, input `PacketId = RequestFormItem.PacketId`): as spec-detail §1.5
(`PacketItem WHERE PacketId = @p AND IsActive = 1`, each `ProductName + " x " + Quantity`, small). `Site.UseNewItem`
picks PacketPreview_New, else PacketPreview.

### 1.4 Attachments (RequestForm block, IsComplex = False)
`RequestFormAttachment WHERE RequestFormId = @id AND IsComplex = @isComplex` (no sort, no IsActive column).

### 1.5 CS/RF comparison (CSRFItemCompare, late-loaded)
Shown only when `RequestForm.CostingSheetId <> NullIdentifier()`, with `CostingSheetId = RequestForm.CostingSheetId`,
`RequestFormId = @id`. (The costing sheet side finds its request form with GetRFIdByCSId instead; spec-detail §1.7.)
MyOrders runs **CompareCostingRequest** first (writes: DELETE + INSERT of CSRFItemMatch; spec-detail §1.7), then
`CSRFItemMatch WHERE CostingSheetId = @cs AND RequestFormId = @rf AND CostingSheetQuantity <> RequestFormQuantity`,
default sort EDPCode, max 500 per page. Viewer: never write; read existing CSRFItemMatch rows or compute the same sums
in memory (EDP code → sum of quantities of active items, active sub items, active complex items on each side).

`MatchRequestFormWithCostingSheet` is **not** used here: it is called from the OPPA flow (advance order "Ok") and
writes `RequestFormItem.CostingSheetItemId` (match by EDP code or product name and quantity). Left out.

### 1.6 Helpdesk requests (Helpdesk block; entity of the MyHelpdesk module, names decode shifted)
Only when `Site.EnableHelpdesk`. `Request WHERE <request's request form id> = @id AND <status> <> Cancelled AND <a
boolean flag> = True` (inferred: IsActive), joined Request Status (INNER) and OSSYS_USER (LEFT, the requester);
default sort Request.Id, 50 per page, sortable. Columns by intent (§2.6).

---------------------------------------------------------------------------------------------------------------------

## 2. Screen, in display order

Title: `"Request Form # " + RequestForm.Id`.

### 2.1 Header box (ShowRecord tblRequestForm)

| Label | Value | Visible |
|---|---|---|
| Request Form Type | `RequestFormType.Label` (Trial / Collection / Delivery Order / Demo / Loan) | always |
| Reference | `"# " + Reference.Reference` | always |
| Salesman | salesman's OSSYS_USER.NAME (decodes as Users.Username) | always |
| Request Form Status | `RequestFormStatus.Label` | always |
| Specialists | one label each, `GetNameByUser(id)` (OSSYS_USER.NAME): "PP Specialist" (PPSpecialistId), "MDS Specialist" (MDSSpecialistId), "Fusion Specialist" (MITSSpecialistId); background `orange` when `PendingPP` / `PendingMDS` / `PendingMITS`, else `lightgreen` | each when its Id ≠ null |
| Created At | `DateTime_PrettyFormat(CreatedAt)` | always |
| Updated At | `DateTime_PrettyFormat(UpdatedAt)` | always |
| Recall At | `DateTime_PrettyFormat(RecallAt)` | always (1900-01-01 → "-") |
| Company | `Trim(Company.Company)` | always |
| Billing Address | address of `RequestForm.AddressId`: non-empty trimmed parts joined by spaces, then `"Singapore " + PostalCode` (see notes) | always |
| Contact | non-empty trimmed FirstName, MiddleName, LastName joined by spaces | always |
| Phone | block Phone(ContactId): every active, non-empty MyCRM phone of the contact, one per line (Phones of ContactId, joined Phone Types; sort decoded as the number column (inferred)) | always |
| Customer No | `RequestForm.CustomerNo` | always |
| Customer Type | MyCRM `CompanyGroup.Label` | always |
| Event Tag | Event Tag label (`EventTagId`) | always |

### 2.2 Declaration box
Checkbox `chkDeclare` bound to `RequestForm.Declared` (decoded with a False beside it: disabled, inferred), label
verbatim: "I declare the following: Area of visit by Ricoh’s delivery crew is not  Covid-19 affected area. For
delivery to customers who are individuals, there is no one in the premise under SHN" (two spaces before "Covid-19"
as decoded). No visibility condition decoded. Viewer: "Declared: Yes/No" with that text.

### 2.3 Attachments (block, IsComplex = False)
Heading "Attachments". List (max 50 in MyOrders; viewer: all), empty "No attachments to show...":
- link `RequestFormAttachment.FileName` → Download: bytes `Binary`, name `FileName`, `application/octet-stream`;
- `" (" + Remarks + ") "` when Remarks ≠ "";
- red trash link only for the uploader (`UploadedBy = Session.CurrUserId`), confirm "Are you sure you want to delete
  this attachment?": **DeleteEntity, really deletes** (left out).
Below: file picker + **Upload** (left out). Viewer adds size (`DATALENGTH(BINARY)`), Uploaded by (NAME), Uploaded on.

### 2.4 CS/RF differences (late-loaded; only when RequestForm.CostingSheetId is set; §1.5)
When the list is not empty: box with text "There are differences between Costing Sheet and Request Form" and a table:
**EDP Code** (`EDPCode`) | **Description** | **Costing Sheet Quantity** (FormatDecimal 0 dp, right; a link to
CostingSheetSummary with `CostingSheetId = CSRFItemMatch.CostingSheetId`) | **Request Form Quantity** (FormatDecimal 0
dp, right; link to RequestFormSummary with `RequestFormId` (inferred link, decodes as Text with that parameter)).
Empty: the whole box hidden ("No csrf item matches to show..." never seen). Paging/counter hidden (`False`).
This is the only way to the costing sheet from the summary (there is no costing sheet field in the header).
Viewer: also show a plain "Costing sheet # n" link from `RequestForm.CostingSheetId` (inferred useful; add to the
README's list of differences).

### 2.5 Items, Delivery (when `RequestFormTypeId <> Collection`) — else Collection Items (inferred If/else)

**Items** (heading "Items", box). Table header **Product | Items**; one row per item (§1.2, up to 50000) = block
RequestFormItemSummary:
- **Product** cell (35%): `RequestFormItem.ProductName`.
- **Items** cell: inner table **Name | Quantity | Street Price (Unit) | Street Price (Total)**:
  1. the item: `ProductName`, `Quantity`, `FormatCurrency(UnitPrice)`, `FormatCurrency(Quantity * UnitPrice)`;
  2. each active sub item: `RequestFormSubItem.ProductName`, `Quantity`, `FormatCurrency(UnitPrice)`,
     `FormatCurrency(Quantity * UnitPrice)`;
  3. each active complex item: calculated `ProductName`, `RequestFormComplexItem.Quantity`, `FormatCurrency(0)`,
     `FormatCurrency(0)` (literally zero: request form complex items have no price);
  4. total row: `FormatCurrency(RequestFormItem.TotalSTP)` under Street Price (Total) (stored, not recomputed).
- No achieved price column, no packet lines here (packets appear only in the Delivery table).
Under the list: **Request Total Street Price:** `FormatCurrency(RequestForm.TotalSTP)` (stored).

**Delivery** (heading "Delivery", box). TableRecords over §1.2 (all rows), columns:

| Header | Value |
|---|---|
| Product | block RequestFormItemPreview: `ProductName + " x " + Quantity`; then each active sub item `ProductName + " x " + Quantity` (small); each active complex item `ProductName + " x " + Quantity` (small); packet items when `IsPacket` (§1.3) |
| Address | `CompleteAddress(RequestFormItem.AddressId)` (MyCRM address of the item; helper not decoded: as §2.1 plus city/state/country outside Singapore, see spec-detail §2.5) |
| Delivery Date | `Date_PrettyFormat(RequestFormItem.Date)` (right) |
| Contact Person | `CompleteContact(RequestFormItem.ContactId)` (helper not decoded: name parts as §2.1) |
| Phone | `Trim(Phones.<number>)` of `RequestFormItem.PhoneId` |
| Is FSA | green tick image when `IsFSA` (centre) |
| No Lift | tick when `NoLift` (centre; decoded as a checkbox/label pair, read-only (inferred)) |
| Remarks | `RequestFormItem.Remarks` |
| Model | `RequestFormItem.Model` (an input for editors, left out) |
| Serial | `RequestFormItem.Serial` (an input for editors, left out) |

Model/Serial are inputs when role ∈ {Recon, s0ZijIrD… (unresolved), PPSpecialistManager} and the item is a
reconditioned one (`ProductGroup.Name = "B/W Reconditioned Copiers"` or `ProductName` ∈ {"Colour Reconditioned
Copiers", "BW Recondition PP", "COL Recondition PP"}, as decoded); else text. Button **Update Model & Serial** (left
out) when (role Recon and `HasRecon_RequestForm(id)`) or (role PPSpecialistManager / s0ZijIrD… and status ∈ {Draft,
Pending Manager, Pending Head}).

**Collection Items** (heading "Collection Items", box; Collection type). Two-column ShowRecord of the **first** item
(`GetRequestFormItemsByRequestFormId.List.Current`):

| Label | Value | Label | Value |
|---|---|---|---|
| Collection Type | `CollectionType.Label` | Address | `CompleteAddress(item.AddressId)` |
| Contract No | `ContractNo` | Postal Code | `item.PostalCode` |
| Reason Of Termination | `ReasonOfTermination` | Contact | `CompleteContact(item.ContactId)` |
| Removal Fee (Per Unit Only For Leasing) | `RemovalFee` (no format) | Phone | `Phones.<number>` (PhoneId) |
| Invoice No | `InvoiceNo` | Secondary Contact | `SecondaryContact` |
| Balance To Invoice | `BalanceToInvoice` (no format) | Secondary Contact No. | `SecondaryContactNo` |
| Rollover Amount | `RolloverAmount` (no format) | No Lift | tick when `NoLift` |
| Asset Type | `AssetType.Label` | Call Before Collection | tick when `CallBeforeCollection` |
| Brand | `Brand.Label` | Special Instructions | `SpecialInstructions` |
| Collection Date | `Date_PrettyFormat(item.Date)` | | |

Then table tblCollectionItem over all items (empty "No items to show..."): **Model** | **Brand** (`Brand.Label`) |
**Serial** | **EDPCode** | **Description** | **Quantity** (FormatDecimal 0 dp, right) | **Remarks** | **Has
Finisher** (tick when `HasFinisher`). Collection labels (static CollectionType): Exchange, Other, Loan,
Pre-Termination (With Rollover), Trial, Termination, Trade-In, Contract Expired, Pre-Termination (No Rollover), Demo.
Viewer: money columns (RemovalFee, BalanceToInvoice, RolloverAmount) may use FormatCurrency (a deliberate difference).

### 2.6 Connectivity Requests (only when `Site.EnableHelpdesk`; block Helpdesk)
Heading "Connectivity Requests". Link **Generate Connectivity Request** (salesman of the form, EnableHelpdesk):
creates a MyHelpdesk request, left out. Table (empty "No requests to show...", 50 per page):
**Id** (right; link, new tab, to MyHelpdesk `RequestDetail?RequestId=<Id>&RequestFormId=<rf>&RequestSystemId=1` when
`Environment_Name = "Production"`, else `…&RequestSystemId=2`; external-link icon) | **Request Status** (status label;
when status = New: label + " (Pending Credit Approval)" in green if the request's mobile number, preferred
installation date, OS platform and number of workstations are all filled, else label + " (Pending User Input)";
field names inferred, decode shifted) | **Model** | **Serial Number** | **Location Address** | **Machine Arrival
Date** (Date_PrettyFormat, right). Viewer: optional (needs the MyHelpdesk tables); else leave out and note it.

### 2.7 Buttons at the bottom (all left out, §4)
Edit, Approve / Reject (three role/status pairs), "Waiting for OPO to check", Recall, Print, Convert to costing sheet,
Delete; modal "Enter customer numberW" (sic) with input `CustomerNo` and Approve.

---------------------------------------------------------------------------------------------------------------------

## 3. Added for the viewer (all from RequestForm columns): approval trail and order facts

| Step | When | Who |
|---|---|---|
| Created | `CreatedAt` | `CreatedBy` |
| Last updated | `UpdatedAt` | `UpdatedBy` |
| Manager approved | `ApprovedByManagerOn` | `ApprovedByManagerId` |
| First approval | `ApprovedAt` | `ApprovedBy` |
| Head approved | `ApprovedByHeadOn` | `ApprovedByHeadId` |
| Sent for credit check | `PendingCreditOn` | – |
| Credit endorsed | `CreditEndorsedOn` | – |
| OPO checked | `OPOCheckedOn` (`IsOPOChecked`) | `OPOCheckedBy` |
| Credit approved | `CreditApprovedAt` | `CreditApprovedBy` |
| Sent | `SentAt` (`IsSent`) | `SentBy` |
| Printed | `PrintedAt` | – |
| Recalled | `RecallAt` | – |
Each row only when the date is set (not null/1900-01-01). Also read-only facts: Costing Sheet (`CostingSheetId`,
link), Proposal / Option / Prospect, Previous request form (`PrevRequestFormId`, link), Sales Order Number,
Backordered SO Number, Picking Slip Number, Invoice Number, Agility running number, Warehouse, Delivery address 1/2,
Postal code, Country, BRN, Customer name, IsEPP, IsReconBilled, ICE/workstation fields (`IsICE`, `NoOfWorkstation`,
`ServerAvailability`, `OperatingSystem`, `IsUserCodeSetting`).

How MyOrders moves the status (for the README; the viewer only shows it). Statuses (RequestFormStatus labels, match by
label): Draft, Pending Specialist, Pending EDP Code, Pending Manager, Pending Head, Pending Recon Allocation, Pending
Credit Check, Pending Allocation, Backordered, Pending Invoicing, Invoiced, Manager Rejected, Head Rejected, Credit
Rejected, Stock Allocator Rejected.
- Approve, role Manager / PPSpecialistManager (button at Pending Manager): → **Pending Head**;
  `ApprovedByManagerId/On`, `ApprovedAt/By` when empty; email RequestFormEmail to the Head role when
  `Site.EnableEmailForManagerHead`.
- Approve, role Head / SpecialistSeniorManager (button at Pending Manager or Pending Head): if
  `HasRecon_RequestForm` and not `IsReconAllocated` → **Pending Recon Allocation** (email RSP_RECON@Ricoh.sg); else →
  **Pending Credit Check**; Collection type → CollectionFormEmail to servicecontracts@ricoh.sg; RequestFormEmail to the
  CreditManager and Credit roles; `ApprovedByHeadId/On`, `ApprovedAt/By` when empty.
- Approve, role CreditManager / Credit / CreditGM / MD (Pending Credit Check, only when `IsOPOChecked`; via the
  customer-number modal): saves `CustomerNo`, writes it to the MyCRM company (`UpdateCustomerNo`); → **Pending
  Invoicing** (Collection) or **Pending Allocation**; `CreditApprovedAt/By`; with EnableHelpdesk
  `UpdateAllStatus_Helpdesk`; Collection with a Pre-Termination item → email rspopo@ricoh.sg, servicecontracts@ricoh.sg.
- Reject: credit roles → Credit Rejected; Manager / PPSpecialistManager → Manager Rejected; Head /
  SpecialistSeniorManager → Head Rejected; then `ClearReconModelSerial_RequestForm`, `RevertMyRecon_RequestForm`.
- Recall (`RecallRequestForm`): status → Draft, `RecallAt = now`, clears PrintedAt, PendingCreditOn,
  CreditEndorsedOn, IsOPOChecked/On/By; clears recon model/serial, reverts MyRecon, cancels helpdesk checklists.
- Update Model & Serial (Recon): when status Pending Recon Allocation and now allocated → Pending Credit Check + emails.

---------------------------------------------------------------------------------------------------------------------

## 4. Data-changing actions on the summary (left out of the viewer)

| Action | Visible when | What it writes |
|---|---|---|
| Edit | status Draft | opens RequestFormDetail (edit wizard) |
| Approve / Reject (1) | status Pending Manager and role ∈ {Manager, Head, PPSpecialistManager, SpecialistSeniorManager} | §3 |
| Approve / Reject (2) | status Pending Head and role ∈ {Head, SpecialistSeniorManager} | §3 |
| Approve (modal) / Reject (3) | status Pending Credit Check and role ∈ {CreditManager, Credit, CreditGM, MD} and `IsOPOChecked`; otherwise text "Waiting for OPO to check" | §3, MyCRM Company.CustomerNo |
| Recall (confirm "Are you sure you want to recall this request form?") | status ∉ {Draft, Pending Allocation, Backordered, Pending Invoicing, Invoiced} and (salesman of the form or Head or Manager) and `IsReconBilled = False` | RecallRequestForm |
| Print | role ∈ {SalesAdmin, Admin, Manager, Head, PPSpecialistManager, SpecialistSeniorManager, MD} and status ∈ {Pending Credit Check, Pending Allocation, Pending Invoicing, Backordered} | SQL UPDATE `PrintedAt = now`; non-Collection: `SetDeliveryFormItem` (DeliveryFormPrint / DeliveryFormPrintItem rows); §5 |
| Convert to costing sheet | type ≠ Collection and salesman of the form (no status check) | creates a Draft CostingSheet + items + sub items from the form, sets `RequestForm.CostingSheetId`, `CostingSheetItem_RequestFormItem`, totals; opens the costing sheet |
| Delete (confirm "Are you sure you want to delete this request form?") | salesman of the form and status Draft | SQL UPDATE: IsActive = 0, DeletedBy, DeletedAt (columns inferred); message "Request form # n deleted" |
| Update Model & Serial | §2.5 | RequestFormItem Model/Serial, status, emails |
| Attachments Upload / Delete (trash) | everyone / uploader | INSERT / **DeleteEntity** RequestFormAttachment |
| Generate Connectivity Request | EnableHelpdesk and salesman | MyHelpdesk request |
| CS/RF compare (on every load) | automatic | CompareCostingRequest: DELETE + INSERT CSRFItemMatch |

## 5. Print (for a separate spec)
Print builds a token URL with `GetEntryURL(eSpace "MyOrders", entry If(type = Collection, "CollectionForm",
"DeliveryFormNew"), Token = GenerateToken(), RequestFormId)`, converts it to PDF (footer font size 8) and downloads
`"RequestForm #" + Id + ".pdf"` (`application/pdf`). The pages are in flow RequestFormPDF (`CollectionFormPDF`, and the
delivery form built from `DeliveryFormPrint*` rows; blocks RequestFormHeader, DeliveryFormItem(Page), Product,
SubItem, ComplexItem, QRCode, Table, Table2). RequestFormHeader: Ricoh logo, `Reference + "-" + RequestFormId + "-" +
(AgilityRunningNumber - 1)` underlined blue, " OPO / Warehouse / Scv / IT", "REQUEST FORM for - Request Form/" +
type (non-Collection) or "COLLECTION FORM", " (Updated " + date dd MMM yyyy + ")". `RequestPDF` (flow MyHelpdeskPDF)
is the helpdesk "Job Details #" page, not the request form print. A viewer print must be computed in memory (no
DeliveryFormPrint writes).

## 6. Columns to SELECT

- **RequestForm**: ID, REQUESTFORMTYPEID, PREVREQUESTFORMID, PROSPECTID, PROPOSALID, OPTIONID, COSTINGSHEETID,
  REQUESTFORMSTATUSID, REFERENCEID, COMPANYID, CONTACTID, ADDRESSID, SALESMANID, CREATEDBY, CREATEDAT, UPDATEDBY,
  UPDATEDAT, ISACTIVE, TOTALSTP, PRINTEDAT, APPROVEDAT, APPROVEDBY, ISSENT, SENTAT, SENTBY, PICKINGSLIPNUMBER,
  INVOICENUMBER, SALESORDERNUMBER, BACKORDEREDSALESORDERNUMBER, BRN, CUSTOMERNAME, DELIVERYADDRESS1, DELIVERYADDRESS2,
  POSTALCODE, COUNTRY, WAREHOUSE, MDSSPECIALISTID, PPSPECIALISTID, MITSSPECIALISTID, PENDINGMDS, PENDINGPP,
  PENDINGMITS, CREDITAPPROVEDBY, CREDITAPPROVEDAT, DELETEDBY, DELETEDAT, RECALLAT, ISRECONBILLED, NOOFWORKSTATION,
  SERVERAVAILABILITY, ISUSERCODESETTING, OPERATINGSYSTEM, ISICE, USENEWITEM, ISEPP, PENDINGCREDITON, CREDITENDORSEDON,
  ISOPOCHECKED, OPOCHECKEDON, OPOCHECKEDBY, CUSTOMERNO, DECLARED, AGILITYRUNNINGNUMBER, APPROVEDBYMANAGERID,
  APPROVEDBYMANAGERON, APPROVEDBYHEADID, APPROVEDBYHEADON, EVENTTAGID.
- **RequestFormItem**: ID, REQUESTFORMID, PRODUCTTYPE, PRODUCTNAME, EDPCODE, DELIVERYPRODUCTID, QUANTITY, UNITPRICE,
  TOTALSTP, ISACTIVE, ADDRESSID, CONTACTID, PHONEID, DATE, REMARKS, MODEL, SERIAL, DESCRIPTION, BRANDID, ASSETTYPEID,
  COLLECTIONTYPEID, SPECIALINSTRUCTIONS, NOLIFT, CALLBEFORECOLLECTION, SECONDARYCONTACT, SECONDARYCONTACTNO,
  POSTALCODE, PACKETID, ISPACKET, ISFSA, HASFINISHER, CONTRACTNO, REASONOFTERMINATION, REMOVALFEE, INVOICENO,
  BALANCETOINVOICE, ROLLOVERAMOUNT (also COSTINGSHEETITEMID for a link).
- **RequestFormSubItem**: ID, REQUESTFORMITEMID, PRODUCTNAME, EDPCODE, QUANTITY, UNITPRICE, ISACTIVE.
- **RequestFormComplexItem**: ID, REQUESTFORMITEMID, PRODUCTID, PRODUCTNAME, EDPCODE, QUANTITY, ISACTIVE.
- **RequestFormAttachment**: ID, REQUESTFORMID, FILENAME, UPLOADEDBY, UPLOADEDON, ISCOMPLEX, REMARKS,
  DATALENGTH(BINARY); BINARY only for one download.
- **PacketItem**: ID, PACKETID, PRODUCTNAME, QUANTITY, ISACTIVE. **Product**: ID, NAME, PRODUCTGROUPID.
  **ProductGroup**: ID, NAME. **Reference**: ID, REFERENCE.
- **CSRFItemMatch** (read only): COSTINGSHEETID, REQUESTFORMID, EDPCODE, DESCRIPTION, COSTINGSHEETQUANTITY,
  REQUESTFORMQUANTITY (+ costing sheet item tables of spec-detail §6 for an in-memory compare).
- Static (ID, LABEL; match by label): **RequestFormType**, **RequestFormStatus**, **CollectionType**, **AssetType**,
  **Brand**.
- Other modules: **OSSYS_USER** (ID, NAME, USERNAME: salesman, specialists, approvers, uploader); **MyCRM** Company
  (ID, COMPANY, COMPANYGROUPID), CompanyGroup (ID, LABEL), Address (ID, BLOCK, STREET, UNITNUMBER, BUILDINGNAME,
  POSTALCODE, CITY, STATEPROVINCE, COUNTRY), Contacts (ID, FIRSTNAME, MIDDLENAME, LASTNAME), Phones (ID, CONTACTID,
  number, PHONETYPEID, ISACTIVE); **Event Tag** (as spec-detail §6); MyHelpdesk Request / Request Status (only for §2.6).

## 7. Visibility rules worth keeping in the viewer
- The summary has no access check of its own (only `Role()` for buttons); who may open which request form is the
  RequestForms list's rule (GetSubList of the current user; ServiceDeliveryManager only companies like "%dbs%"; ShowAll
  roles as for costing sheets (inferred, list not re-read here)). Apply it to the form, its attachments and its print alike.
- No field on the summary is role-restricted (unlike the costing sheet margins); only the buttons and the Model/Serial
  inputs depend on roles.
- Items/Delivery vs Collection Items by `RequestFormType` label "Collection"; Connectivity Requests by
  `Site.EnableHelpdesk`; CS/RF differences by `CostingSheetId` set and a non-empty difference list.
