<!-- Read from MyOrders_v5471.oml (2026-10-20) for MyCRMRecords' request forms. "The viewer" is MyCRMRecords; $S/out was the decoded module. -->

# MyOrders request form PDF: build spec for MyCRMRecords

Source: MyOrders_v5471.oml (decoded with the scratchpad's `tools/oml-reader.py`, `tools/widgets.py`, `tools/linear.py`,
`tools/raw.py`). Raw renderings kept in the scratchpad under `rfp/`: `w_<key>.txt` (widgets with expressions),
`s_<key>.txt` (screen + preparation), `f_SetDFI.txt` / `l_SetDFI.txt` (the snapshot action), `w_RequestForms.txt`,
`s_RFSummary.txt`, `w_RFSummary.txt`.

Notation: `RF` = the request form row (`RequestForm`); `DFP` = a `DeliveryFormPrint` row; `Line` = a
`DeliveryFormPrintItem` row; `Item` = a `RequestFormItem` row. Text in quotes is verbatim from the widgets (keep
spaces, case, punctuation and typos). Bold = OutSystems `Bold` style. N col = N/12 of the width.

**Which screen.** The screen named `RequestPDF` is **not** the request form: it lives in UI flow `MyHelpdeskPDF`
(web entry `Request`, input `RequestId`) and prints a helpdesk job sheet ("Job Details # ", "Requester Details",
"Symptom Code", "Cause Code", "Action Code", "Customer Signature"…) from `xposeP`/`Request Job`/`Code` entities. Nothing
in the request form screens opens it. The request form PDF is the UI flow **`RequestFormPDF`**: screen
**`DeliveryFormPDFNew`** for every request form type except Collection, and screen **`CollectionFormPDF`** for
Collection. (The decode labels these screens as "WebEntry"; the entries' Sequence links prove which is which:
entry `DeliveryFormNew` → screen `DeliveryFormPDFNew` {Hf+Rmm4AGUqM3x1hztQKpA}; entry `CollectionForm` → screen
`CollectionFormPDF` {4w9mTFe_7UquD3LL3WS0bg}; entry `DeliveryForm` → old screen `DeliveryFormPDF`, and entry
`MasterSheet` → `MasterSheetPDF`, neither used by the Print buttons.)

---------------------------------------------------------------------------------------------------------------------
## 1. How MyOrders makes and serves it

- **Screens / entries**: `DeliveryFormPDFNew` (entry `DeliveryFormNew`, `/MyOrders/DeliveryFormNew.aspx`) and
  `CollectionFormPDF` (entry `CollectionForm`, `/MyOrders/CollectionForm.aspx`). Inputs of both: `Token` (Text,
  mandatory), `RequestFormId` (mandatory).
- **Token**: same as the costing sheet PDF (`GenerateToken()` = MD5 of server yyyyMMddHH + secret). Each preparation
  starts with `If Token = GenerateToken()`; else it goes to the same unnamed error screen (`ENYWxFeUa0qGzxz3NZDQWg`).
  No role check in the preparations. (The screens carry role entries Admin, MyOrdersUser and two system roles; the
  converter fetches the page without a session, so anonymous access is in effect — inferred.) MyCRMRecords: serve
  the print behind its own sign-in and access rule; no token.
- **Print button → PDF** (same flow in both places, `PrintNew` in `RequestForms`, `Print` in `RequestFormSummary`):
  1. **Writes** `RequestForm.PrintedAt = CurrDateTime()` (advanced SQL `UPDATE {RequestForm} SET [PrintedAt] =
     @Datetime WHERE [Id] = @RequestFormId`), first.
  2. If `RequestFormTypeId <> Collection`: **writes the snapshot** `SetDeliveryFormItem(RequestFormId)` (section 5):
     deletes and recreates the form's `DeliveryFormPrint` / `DeliveryFormPrintItem` rows.
  3. `URL = GetEntryURL(If(RequestFormTypeId = Collection, "CollectionForm", "DeliveryFormNew"), eSpace "MyOrders",
     Token = GenerateToken(), RequestFormId)` made absolute (`MakeAbsoluteURL`).
  4. `HtmlToPdfConverter(URL, WhereToAddPageNumber = "F", options "--footer-font-size 8")` (page number in the
     footer, 8 pt; the list's action shows the "F" argument and the summary's shows the options string: each decode
     shows one of the two — assume both).
  5. Downloads **`RequestForm #<Id>.pdf`**, `application/pdf`. On an exception: feedback message with the exception
     text.
  MyCRMRecords is read-only: do NOT write PrintedAt and do NOT rebuild the snapshot (section 5.3).
- **Who sees the Print button**:
  - `RequestFormSummary`: yellow button **"Print"**, shown when the viewer's MyCRM role (`Role(Session.CurrUserId)`)
    is one of **SalesAdmin, Admin, Manager, Head, PPSpecialistManager, SpecialistSeniorManager, MD** AND
    `RF.RequestFormStatusId` is **Pending Credit Check, Pending Allocation, Pending Invoicing or Backordered**.
  - `RequestForms` list: per row a **"Print"** link (icon "download"), shown when the role is one of **SalesAdmin,
    Admin, StockAllocator, Manager, Head, PPSpecialistManager, SpecialistSeniorManager, MD, CreditGM, Scheduler**
    (first term compares `Session.RoleId`, the others the `Role()` output) AND the status is one of the same four.
    (Role keys: `9PD_Is3O5UC51jGmtBri3A` = Manager, `XyDg8VkHm0aABtYp6Il6CA` = Head, as in spec-detail.md.)
  - The list also has columns "Printed At" (`DateTime_PrettyFormat(RF.PrintedAt)`) and "Is Pending Print" (green
    tick when `IsPendingPrint` = status in those four AND (`RF.RecallAt > RF.PrintedAt` or `RF.PrintedAt` null)).
    MyCRMRecords may show both read-only.
- Recommendation for MyCRMRecords: one print page `requestformprint.aspx?id=…` choosing the layout by type
  (Collection → section 4, else section 3), A4, the CSS of section 6, a "Print / Save as PDF" button, document title
  `RequestForm #<Id>`. Same visibility rule as the summary button (roles + the four statuses) or MyCRMRecords'
  own access rule for request forms (decide with the user; MyOrders' rule is above).

---------------------------------------------------------------------------------------------------------------------
## 2. Header block (`RequestFormHeader`, top of every page of both layouts)

Reads `RequestForm` (Id = RequestFormId) INNER join `RequestFormType` (on `RF.RequestFormTypeId`) LEFT join
`Reference` (on `RF.ReferenceId`). Local `Type = RequestFormType.Label`. Container class `myHeader`
(`font-size: 9pt`), one row:

- **Left, 3 col** (align left): image **RICOH_logo** (`pdf/img/RICOH_logo.png`), width 170 px.
- **Middle, 6 col**:
  - right-aligned line: `Reference.Reference + "-" + RequestFormId + "-" + (RF.AgilityRunningNumber - 1)` (style
    `Underline`, `color:blue;`), immediately followed by the text " OPO / Warehouse / Scv / IT".
  - below (class `fakeSelect OSAutoMarginTop`, align left, style `background:black;color:white;`):
    - if `RF.RequestFormTypeId <> Collection`: "REQUEST FORM for - Request Form/" + `Type` (example "Trial");
    - else: "COLLECTION FORM";
    - then " (Updated " + `FormatDateTime(If(RF.UpdatedAt = NullDate(), RF.CreatedAt, RF.UpdatedAt), "dd MMM yyyy")`
      + ")" (example "28 Apr 2017").
- **Right, 3 col** (align right): image **Restricted** (key `durT+MWqXEmAIBeMOPzmWw`, the same image as the costing
  sheet header; `pdf/img/Restricted.png`), width 170 px.

Request form types (static, by label): Trial, Collection, Delivery Order, Demo, Loan.

---------------------------------------------------------------------------------------------------------------------
## 3. Delivery form (`DeliveryFormPDFNew`, every type except Collection)

### 3.1 Structure and page breaks

- `GetDeliveryFormPrints`: `DeliveryFormPrint` where `DFP.RequestFormId = RequestFormId`, sorted by `DFP.Date`
  ascending. One per distinct delivery (address, contact, date, no-lift, remarks, phone) — section 5.
- For each DFP: block `DeliveryFormItem(DeliveryFormPrintId, RequestFormId)`, which lists
  `DeliveryFormPrintItem` where `Line.DeliveryFormPrintId = DFP.Id`, **grouped by `Line.Page`** (one row per page
  number; sorted by Page — the sort key decodes unresolved, inferred). For each page number: one `.Page` container
  (forces a page break before it) holding sections 3.2–3.6. So the document is: delivery 1 page 1, delivery 1 page 2,
  …, delivery 2 page 1, …

### 3.2 Header
`RequestFormHeader` (section 2).

### 3.3 Customer table (class `OSAutoMarginTop`, table class `table`, 3 rows × 4 cells, label cells bold)

Reads `GetRequestFormById` = `RequestForm` (Id) INNER join MyCRM `Companies` (on `RF.CompanyId`) LEFT join
`Reference` LEFT join `Users` (OSSYS_USER, on `RF.SalesmanId`); and `GetDeliveryFormPrintById` = the DFP.

| Label (bold) | Value | Label (bold) | Value |
|---|---|---|---|
| "Customer" | `Trim(Company.Company)` | "A/C Number" | `RF.CustomerNo` |
| "Contact Person" | `CompleteContact(DFP.ContactId)` | "Contact Number" | `Phone(DFP.PhoneId)` (MyCRM phone text) |
| "Salesman Name" | salesman's OSSYS_USER **NAME** (decodes as `Users.Username`) + " - " + `SalesmanCode(RF.SalesmanId)` | "Submission Date " (trailing space) | `FormatDateTime(RF.CreatedAt, "dd/MM/yyyy")` |

`CompleteContact`, `SalesmanCode`: as in spec-pdf.md section 6 (helpers). `Phone(phoneId)`: MyCRM `Phones` row's
number (helper did not decode; inferred).

### 3.4 Delivery table (class `OSAutoMarginTop`, table class `table`, 4 rows × 2 cells, labels not bold)

| Label | Value |
|---|---|
| "Delivery Address" | `CompleteAddress(DFP.AddressId)`, then on its own line (container margin-top 0) `CountryPostalCode(DFP.AddressId)` |
| "Delivery Date" | **bold** `FormatDateTime(DFP.Date, "dd/MM/yyyy")` |
| "No Lift" | green tick image `Icon_Check_Green` when `DFP.NoLift`, else empty |
| "Remarks" | **bold** `DFP.Remarks` |

(`CompleteAddress` / `CountryPostalCode`: MyCRM address helpers, as for the New CS layout in spec-pdf.md.)

### 3.5 Item table (block `DeliveryFormItemPage(DeliveryFormPrintId, Page)`, class `OSAutoMarginTop`)

Header table (class `table`), one row, all bold:

| "Item Code" | "Tick" | "IsFSA" | "Name" | "Serial No." | "Quantity" | "Allocated Qty" | "Check" |
|---|---|---|---|---|---|---|---|
| 15% | 5% | 8% | 30% | 15% | 10% | 10% | 7% |

Rows: `Line` where `DeliveryFormPrintId = DFP.Id` and `Page = page`, with `Sort = Configuration→1, Mainframe→2,
Accessory→3, Consumable→4, else 0` (by `Line.ProductType`), sorted by `RequestFormItemId`, `Sort`, `ItemCode`. Each
row is its own table (class `table2`: borders without the top border), same widths, no separator:

| Column | Content |
|---|---|
| Item Code | `Line.ItemCode` if it is not empty, else a few spaces; cell `word-break: break-word;` |
| Tick | empty (a box to tick by hand) |
| IsFSA | green tick when `Line.IsFSA` |
| Name | `Trim(Line.Name)`, then if `Line.ProductType = "Mainframe"` and `Line.Model <> ""`: `" [" + Line.Model + "]"`; `word-break: break-word;` |
| Serial No. | `Line.SerialNo`; `word-break: break-word;` |
| Quantity | `Line.Quantity` when > 0, else empty |
| Allocated Qty | `Line.AllocatedQuantity` (shown even when 0) |
| Check | empty |

Filler: block `Table2(Length = 25 - number of rows on this page)`: that many empty `table2` rows with the same 8
widths (first cell holds spaces so the row has height). So every page's table has 25 rows. None when Length <= 0.

### 3.6 Bottom of the page

- Container `margin-top: 20px`, one row:
  - **8 col**: a 6 col "APPROVED BY MANAGER" (bold), then a 6 col `UnderLineBox` (blank line; nothing printed in it).
  - **4 col**, align right: QR code of `RF.Id` (block `QRCode`, width 120 px): `GenerateQRCode(Text)` → PNG →
    `<img width=120 src='data:image/png;base64,…' />`. Any QR generator encoding the Id as text will do.
- Then, **only when `RF.Declared`** (an If on `RequestForm.Declared`; branch inferred as the True one): full-width
  container with the green tick image `Icon_Check_Green` followed by (style `margin-left: 10px;`):
  "I declare the following: Area of visit by Ricoh’s delivery crew is not Covid-19 affected area. For delivery to
  customers who are individuals, there is no one in the premise under SHN" (note the curly apostrophe in "Ricoh’s",
  no final full stop).

---------------------------------------------------------------------------------------------------------------------
## 4. Collection form (`CollectionFormPDF`, type Collection)

One `.Page` container; everything below flows inside it (no explicit page breaks; long lists simply continue on the
next sheet — inferred from the converter).

Data:
- `GetRequestFormById`: `RequestForm` (Id) INNER join MyCRM `Companies` (on CompanyId) LEFT join `Reference` LEFT join
  `Users` (OSSYS_USER on SalesmanId).
- `GetRequestFormItemsByRequestFormId`: active `RequestFormItem` of the form (`IsActive = True`), sorted by `Id`,
  LEFT join `AssetType` (on `Item.AssetTypeId`), LEFT join `CollectionType` (on `Item.CollectionTypeID`), LEFT join
  MyCRM `Phones` (on `Item.PhoneId`). "First item" below = its first row (OutSystems Current outside the list).

### 4.1 Top
1. `RequestFormHeader` (section 2; shows "COLLECTION FORM").
2. Table (class `OSAutoMarginTop`), 2 rows:
   - row 1: Heading2 (margin-top 0) bold "Request for Collection" | cell rowspan 2, vertical-align middle, align
     right: QR code of `RF.Id`, width 120 px.
   - row 2: Heading3 (margin-top 0) `Reference.Reference` in `color:blue;`.
3. "* required fields" (italic), class `OSAutoMarginTop`.

### 4.2 Boxes A–C (each a container `borderBox box OSAutoMarginTop`: 1px border; first line bold title)

**"A. Type of Request"**: centred (style `font-size:14pt;`): first item's `AssetType.Label` + " (" +
`CollectionType.Label` + ")" (e.g. "Hardware (Trade-In)").

**"B. Customer Infomation"** (sic). Rows of two halves (6 col each); each half = a label (OutSystems Label, 4 col)
then the value in bold:
| Left label → value | Right label → value |
|---|---|
| "Customer Name *:" → `Company.Company` | "A/C No. *" → `RF.CustomerNo` |
| "Contact Person *" → `CompleteContact(firstItem.ContactId)` | "Contact No. *" → first item's phone (`Phones` joined on `Item.PhoneId`; the attribute decodes as `Phones.CRMPhoneId`, shifted: use the phone number — inferred) |
| "Secondary Contact" → `firstItem.SecondaryContact` | "Contact No." → `firstItem.SecondaryContactNo` |
Then a full row: "Special Instructions" (label 2 col) → `firstItem.SpecialInstructions` (bold).

**"C. Collection Information"**:
| Left | Right |
|---|---|
| "Collection Address" → `CompleteAddress(firstItem.AddressId)` | "Postal Code" → `firstItem.PostalCode` |
| "Collection Date" → `FormatDateTime(firstItem.Date, "dd MMM yyyy")` | (empty) |

### 4.3 Item list
- Heading2 (margin-top 20px): first item's `CollectionType.Label` + " " + `AssetType.Label`.
- List (margin-top 20px) of all active items (empty list text "No items to show..."). Each item: left 1 col
  `(row number + 1) + "."`; right 11 col, rows of two halves (6 col; label 4 col plain text, value bold):
  | Left | Right |
  |---|---|
  | "Model *" → `Item.Model` | "Serial No." → `Item.Serial` |
  | "Quantity" → `Item.Quantity` | "Description" → `Item.Description` |
  | "Remark" → `Item.Remarks` | "Has Finisher" → `If(Item.HasFinisher, "Yes", "No")` |
  | "EDP Code" → `Item.EDPCode` | (empty) |

### 4.4 Signatures (each row class `OSAutoMarginTop`, label 4 col, value 8 col)
- "CREATED BY" (not bold) → salesman's OSSYS_USER NAME + " - " + `SalesmanCode(RF.SalesmanId)`.
- "APPROVED BY MANAGER" (bold) → `UnderLineBox`: if `RF.ApprovedByManagerId <> NullIdentifier()`:
  `GetNameByUserId(RF.ApprovedByManagerId) + " " + FormatDateTime_(RF.ApprovedByManagerOn)`; else blank line.
- "APPROVED BY HEAD" (bold) → same with `ApprovedByHeadId` / `ApprovedByHeadOn` (the If is inferred like the one
  above).
- `FormatDateTime_(d)` = "" when null; `"dd/MM/yyyy"` when the time is 00:00:00; else `"dd/MM/yyyy HH:mm"`.
  `GetNameByUserId` = OSSYS_USER NAME.
- Container margin-top 20px, two halves:
  - left 6 col: an `UnderLineBox` with `margin-top: 60px` (signature line), then centred "Authorised signature, stamp
    and date".
  - right 6 col: table (style `border-style: solid; border-width: 1px; text-align: center !important;`), every cell
    `border-style: solid; border-width: 1px;`:
    - row 1 (colspan 2, centred, `font-style: normal;`): "Please indicate the no. of unused toner cartridge collected"
    - row 2: "Qty" (30%) | "Customer's Signature & Stamp" (70%)
    - row 3: two empty cells, height 60px.

Collection types (static): Exchange, Other, Loan, Pre-Termination (With Rollover), Trial, Termination, Trade-In,
Contract Expired. Asset types: Hardware, Software, Others.

---------------------------------------------------------------------------------------------------------------------
## 5. The delivery snapshot (`SetDeliveryFormItem`, run by Print before the delivery form)

### 5.1 Tables (MyOrders entities)
- **DeliveryFormPrint**: Id, RequestFormId, AddressId, ContactId, Date (DateTime), NoLift (Boolean), Remarks, PhoneId.
- **DeliveryFormPrintItem**: Id, DeliveryFormPrintId, RequestFormId, RequestFormItemId, ItemCode, Tick, Name,
  SerialNo, AllocatedQuantity (Integer), Quantity (Integer), Check, Model, ProductType, Page (Integer), IsFSA
  (Boolean). Tick and Check are never filled.

### 5.2 What Print does (for the record; MyCRMRecords must not run it)
1. `DELETE FROM {DeliveryFormPrintItem} WHERE RequestFormId = @RequestFormId`, then the same for
   `{DeliveryFormPrint}`.
2. `GetRequestFormItemsGroup`: active items of the form grouped by (AddressId, ContactId, Date, NoLift, Remarks,
   PhoneId). For each group: create a DFP with those six values.
3. `GetItems`: active items of the form whose six values equal the group's, LEFT join `Product` on
   `Item.DeliveryProductId` and `ProductGroup`; `ProductName`/`EDPCode` = the item's own unless "", then the
   product's; `ProductType` = the item's unless "", then `Replace(Product.Type, "Product::", "")`. For each item
   (every line gets `RequestFormItemId = Item.Id`, `IsFSA = Item.IsFSA`, `AllocatedQuantity =
   GetAllocatedQuantity(RequestFormId, Item.Id, line's ItemCode)`):
   - **packet** (`Item.IsPacket`): for each active `PacketItem` of `Item.PacketId` (same name/EDP/type fallbacks):
     if its ProductType = "Configuration" → one line per `Attachment` of its product (AttacherId = product,
     AttachedId → Product): ItemCode/Name = Trim(Product.EDPCode/Name), SerialNo = Item.Serial, Quantity =
     Attachment.Quantity × Item.Quantity × PacketItem.Quantity, Model = Item.Model, ProductType = product type without
     "Product::"; else one line: PacketItem EDP/name/type, SerialNo = Item.Serial, Quantity = Item.Quantity ×
     PacketItem.Quantity, Model = Item.Model.
   - **configuration** (`Product.Type = "Product::Configuration"` and the group is not Services, Solutions or
     Production Printing): one line per Attachment of `Item.DeliveryProductId` (sorted by Attachment.Type), Quantity
     = Attachment.Quantity × Item.Quantity, SerialNo/Model from the item; an attachment whose EDP code is "Rcon-BW-0"
     or "Rcon-COL-0" gets its code from `GetReconEDPs(Item.Id)` (looked up from the item's serial numbers in the
     serial/MyGreenMachines data; falls back to `Item.EDPCode`).
   - **otherwise**: one line for the item itself (EDPCode, ProductName, SerialNo = Item.Serial, Quantity =
     Item.Quantity, Model = Item.Model, ProductType) — skipped when `IsComplexHeader(ProductName)` (a `ComplexHeader`
     row with that Name exists).
   - then (non-packet items) one line per `RequestFormSubItem` of the item (LEFT join Product, same fallbacks):
     Trim(EDPCode), Trim(ProductName), Quantity = SubItem.Quantity, no serial or model;
   - and one line per active `RequestFormComplexItem` of the item: if its product type is
     "Product::Configuration" and the product has attachments → one line per attachment (Quantity =
     Attachment.Quantity × ComplexItem.Quantity), else the complex item itself (EDPCode, Trim(ProductName), Quantity
     = ComplexItem.Quantity, ProductType). (Order sub-items then complex items: inferred from the links.)
4. `UpdateDeliveryFormItem`: saves the lines and numbers the pages: per DFP (by Date), lines sorted by
   RequestFormItemId, Sort (section 3.5), Name; a counter `i` and `Page = RoundUp(i / 25)`, with a line whose
   RequestFormItemId equals the previous line's handled separately (it takes the previous line's page — inferred;
   the decode of this branch is garbled). So about 25 lines per page, matching the 25-row tables.
- `GetAllocatedQuantity(RequestFormId, RequestFormItemId, ItemNo)` = `AllocatedQuantity` of the first active
  `AllocationItem` with that RequestFormId, RequestformItemId and `ItemNumber = ItemNo` (sorted by ProductType); 0 if
  none (OutSystems default for an empty list, inferred).

### 5.3 For MyCRMRecords
- Print the delivery form from the **stored** `DeliveryFormPrint` / `DeliveryFormPrintItem` rows (what MyOrders
  printed last, `RF.PrintedAt`), reading them by `RequestFormId`; never write them.
- A non-Collection request form with no DFP rows was never printed in MyOrders: show "Not printed in MyOrders" (or
  build the lines in memory with 5.2 without saving — a larger job touching Product, ProductGroup, PacketItem,
  Attachment, ComplexHeader, AllocationItem and the serial data; decide with the user). The rest of the page (header,
  customer, address) can still be shown from the request form.
- Note it in `docs/outsystems/README.md` as a changed rule: MyOrders refreshed the snapshot and PrintedAt on every
  print; MyCRMRecords shows the last snapshot and writes nothing.

---------------------------------------------------------------------------------------------------------------------
## 6. Page CSS (verbatim)

`DeliveryFormPDFNew` style sheet:
```css
body{ background-color: white; font-family: Arial; font-size: 10pt; }
@page { size: 21cm 29.7cm; margin: 25mm 40mm 25mm 40mm; }
@font-face { font-family: Frutiger; src: url('/MyOrders/Fonts/FrutigerLTStd-Light.otf'); }   /* declared, not used */
.Page{ position: relative; width: 21cm; min-height: 29.7cm; max-height: 29.7cm; padding: 0cm; margin: 1cm auto;
       background: white; border: 0px; page-break-before: always; }
.title{ font-size: 18pt; text-align: center; }
.footer{ font-size: 16pt; text-align: right; }
.HeaderBox{ position: absolute; top: 10px; right: 10px; }
.FooterBox{ page-break-after: always; position: absolute; width: 21cm; bottom: -2cm; z-index: 10; }
.table th, .table td{ text-align: center; border: 1px solid; vertical-align: middle; }
.table2 th, .table2 td{ text-align: center; border: 1px solid; border-top-style: none; vertical-align: middle; }
.BorderBox{ border: 1px solid; height: 25px; }
.UnderLineBox{ border-bottom: 1px solid; height: 25px; }
.CheckBorderBox{ border: 1px solid; height: 20px; width: 20px; }
.BlackContainer{ background: black; color: white; font-weight: bold; }
.FakeLink{ color: blue; }
.borderBox{ border: 1px solid; }
```
`CollectionFormPDF` style sheet: the same except `.table, .table th, .table td{ text-align: center; border: 1px solid;
vertical-align: middle; }` (the table itself bordered too) and no `.table2`. Block `DeliveryFormItem` repeats the
`.Page` rule; block `RequestFormHeader`: `.myHeader{ font-size: 9pt; }`. Theme classes used but not defined here:
`Heading2`, `Heading3`, `Italic`, `Underline`, `fakeSelect`, `box`, `OSAutoMarginTop` (= a top margin): plain
equivalents are fine.

Browser print notes (as for the costing sheet): use `.Page{width:auto; min-height:auto; max-height:none; margin:0}`,
keep `page-break-before` but not for the first page; page numbers were added by the converter (optional).

---------------------------------------------------------------------------------------------------------------------
## 7. Everything read (for the data layer)

MyOrders: **RequestForm** (Id, CompanyId, ReferenceId, SalesmanId, RequestFormTypeId, RequestFormStatusId,
CustomerNo, CreatedAt, UpdatedAt, AgilityRunningNumber, Declared, ApprovedByManagerId, ApprovedByManagerOn,
ApprovedByHeadId, ApprovedByHeadOn; PrintedAt and RecallAt for the list columns), **RequestFormType** /
**RequestFormStatus** / **AssetType** / **CollectionType** (static, Label), **Reference** (Id, Reference),
**DeliveryFormPrint**, **DeliveryFormPrintItem** (section 5.1), **RequestFormItem** for Collection (Id, RequestFormId,
IsActive, AssetTypeId, CollectionTypeID, ContactId, PhoneId, SecondaryContact, SecondaryContactNo,
SpecialInstructions, AddressId, PostalCode, Date, Model, Serial, Quantity, Description, Remarks, HasFinisher,
EDPCode).
MyCRM: **Companies** (Id, Company), **Contacts** (CompleteContact), **Address** (CompleteAddress, CountryPostalCode),
**Phones** (number by Id), **CUser** (SalesmanCode). OutSystems: **OSSYS_USER** (ID, NAME).
Images: `pdf/img/RICOH_logo.png`, `pdf/img/Restricted.png`; `Icon_Check_Green` is not embedded (draw a green tick);
QR codes generated.
