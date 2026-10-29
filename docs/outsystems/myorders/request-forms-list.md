<!-- Read from MyOrders_v5471.oml (2026-10-20) for MyCRMRecords' request forms. "The viewer" is MyCRMRecords; $S/out was the decoded module. -->

# MyOrders request forms: list, access and data model (spec for the read-only viewer)

Source: MyOrders .oml decode ($S/out). Same reading notes as spec-detail.md: MyCRM / OSSYS_USER attribute names decode
shifted (`Users.Username` on screen = **OSSYS_USER.NAME**; joins on `Contacts.FirstName`, `Addres.AddressTypeID`,
`Companies.5P7q…` are the **Id** columns). Role keys: `XyDg8VkHm0aABtYp6Il6CA` = **Head** (decodes as
`~hokYi5u_X0KFW2otDl0` in the linear dump), `9PD_Is3O5UC51jGmtBri3A` = **Manager** (`~Manager`),
`PUWbTG+_MkCACjNM3CTp3w` = **ServiceDeliveryManager** (`~Service Delivery Manager`). One role `~Os` is unresolved (§2.3).
"(inferred)" = not read directly from the decode.

Two role sources are used, as in the costing sheets:
- `Session.RoleId` (set at login): used by `ShowAll()`, the Manager/Head default filter, the Download buttons.
- `Role(Session.CurrUserId).RoleId` (MyCRM action run in the preparation): used by the SDM "dbs" filter, the Print link.

---------------------------------------------------------------------------------------------------------------------

## 1. Screen `RequestForms` (flow RequestForm, menu item `MenuItem.RequestForms`, title "Request Forms")

The blocks `RequestFormIdList`, `RequestFormIdListSafe`, `RequestFormStatusId` are NOT used here: they sit in the
Allocation flow and decode with no widgets and no preparation. The only own block used is `DeliveryDate` (§1.4).

### 1.1 Preparation (in order)

1. `Role(UserId = Session.CurrUserId)` → `RoleId` (MyCRM).
2. `GetSubList(Session.CurrUserId)` (MyCRM) → list of users for the Sales Rep combo.
3. Switch: `Session.RoleId = Manager` **or** `Session.RoleId = Head` → `Session.RequestForms_RequestFormStatusId :=
   RequestFormStatus.PendingHead` (runs on every page load, so these two roles always arrive filtered on Pending Head;
   decoded as both Manager and Head, switch label 'Head').
4. Aggregate `GetRequestFormsByCreatedAt` (§1.2).

### 1.2 Aggregate GetRequestFormsByCreatedAt (the list)

```
RequestForm
  INNER Companies (MyCRM Company)   ON RequestForm.CompanyId = Company.Id
  INNER RequestFormType             ON RequestForm.RequestFormTypeId = RequestFormType.Id
  INNER Contacts (MyCRM Contacts)   ON RequestForm.ContactId = Contacts.Id
  LEFT  RequestFormStatus           ON RequestForm.RequestFormStatusId = RequestFormStatus.Id
  LEFT  Reference                   ON RequestForm.ReferenceId = Reference.Id
  LEFT  Users (OSSYS_USER)          ON RequestForm.SalesmanId = Users.Id
calculated IsPendingPrint =
  (StatusId IN (PendingCreditCheck, PendingAllocation, PendingInvoicing, Backordered))
  AND (RecallAt > PrintedAt OR PrintedAt = NullDate())
```
Filters (all ANDed), as decoded (prefix form unfolded):
1. Keyword: `RequestFormStatus.Label LIKE "%"+kw+"%" OR Reference.Reference LIKE "%"+kw+"%" OR Companies.Company LIKE
   "%"+kw+"%" OR IntegerToText(IdentifierToInteger(RequestForm.Id)) LIKE "%"+kw+"%"` (kw =
   `Session.RequestForms_SearchKeyword`; "" matches all rows that have a status/reference/company, i.e. all, since
   Company is INNER).
2. Access: `Index(If(ShowAll(), "|", Session.UserList), If(ShowAll(), "|", "|" + RequestForm.SalesmanId + "|")) <> -1`.
3. `RequestForm.IsActive = True`.
4. Status: `If(Session.RequestForms_RequestFormStatusId <> NullIdentifier(), RequestForm.RequestFormStatusId, 0) =
   If(Session.RequestForms_RequestFormStatusId <> NullIdentifier(), Session.RequestForms_RequestFormStatusId, 0)`.
5. Sales Rep: same pattern with `RequestForm.SalesmanId` / `Session.RequestForms_SalesmanId`.
6. `DateTimeToDate(RequestForm.CreatedAt) >= FromDate OR FromDate = NullDate()`.
7. `DateTimeToDate(RequestForm.CreatedAt) <= ToDate OR ToDate = NullDate()`.
8. SDM: `(Role.RoleId = ServiceDeliveryManager AND Companies.Company LIKE '%dbs%') OR Role.RoleId <>
   ServiceDeliveryManager`.

Sort: `List_SortColumn_GetOrderBy(RequestFormTable.Id, '{RequestForm}.[CreatedAt] desc')` → default **CreatedAt desc**.
No status condition: Draft and rejected forms are listed like any other. No specialist columns in this rule.

### 1.3 Filters (top of page)

| Label | Widget | Bound to | Default |
|---|---|---|---|
| Keyword | text input `SearchInput`, max 100, title "Type the Prospect, Costing Sheet, Request Form Status or Reference" | `Session.RequestForms_SearchKeyword` (Text) | "" (session var, survives visits) |
| Request Form Status | combo, source entity RequestFormStatus (all records; order by Order (inferred)), special option "All" = 0 | `Session.RequestForms_RequestFormStatusId` | NullIdentifier = All; **Pending Head** forced on load for Manager/Head (§1.1) |
| Sales Rep | combo, source = `GetSubList` output list (MyCRM subordinates of the signed-in user, not all users even for ShowAll roles), label `Users.Username` (= NAME), special option "-" = 0 | `Session.RequestForms_SalesmanId` (OSSYS_USER id) | NullIdentifier |
| From Creation Date | date input `txtFromDate` + Input_Calendar | screen local `FromDate` (Date) | NullDate (each visit) |
| To Creation Date | date input `txtToDate` + Input_Calendar | screen local `ToDate` (Date) | NullDate |

The keyword text says "Prospect, Costing Sheet" but the filter searches status label, reference, company name and the
request form Id only (§1.2 filter 1).

Buttons: **Search** (default button) → `RefreshRequestFormTable(ResetPagination, ResetFilters=False)`; **Reset** (link)
→ `RefreshRequestFormTable(ResetPagination, ResetFilters)`; with ResetFilters it clears keyword "", Sales Rep and
Status to NullIdentifier, FromDate/ToDate to NullDate, then refreshes; ResetPagination resets the start index.

Top link: **Create a new Request Form** (style ActionAdd) → `RequestFormDetail(RequestFormId = NullIdentifier())`
(always shown; left out of the viewer).

**Download** and **Download Allocation** (links styled Button): shown when `Session.RoleId` ∈ {SalesAdmin,
StockAllocator, OPO, Scheduler} (§1.6).

### 1.4 Table `RequestFormTable` (source `GetRequestFormsByCreatedAt.List`)

Empty message "No request forms to show...". **50 rows per page**, `List_Navigation` (MaxPageNumbers 5) and
`List_Counter` below, start index remembered (`RememberStartIndex True`). Every header except the two noted is a
`List_SortColumn` with the sort key below.

| # | Header | Sort key | Cell |
|---|---|---|---|
| 1 | Id (right) | `{RequestForm}.[Id]` | link "# " + `FormatDecimal(RequestForm.Id, 0, Site.DecimalSeparator, Site.GroupSeparator)` → `RequestFormSummary(RequestFormId = Id)` |
| 2 | Salesman | `{User}.[Name]` | `Users.Username` (= OSSYS_USER.NAME of SalesmanId) |
| 3 | Reference | `{Reference}.[Reference]` | link with `Reference.Reference` (decode also shows a "# " text node in it) → `RequestFormSummary(RequestFormId = Id)` |
| 4 | Company | `{Company}.[Company]` | `Companies.Company` |
| 5 | Contact | `{Contacts}.[FirstName]` | `CompleteContact(RequestForm.ContactId)` (salutation first middle last, see spec-pdf.md) |
| 6 | Request Form Type | `{RequestFormType}.[Label]` | `RequestFormType.Label` |
| 7 | Created At (right) | `{RequestForm}.[CreatedAt]` | `DateTime_PrettyFormat(CreatedAt)` |
| 8 | Updated At (right) | `{RequestForm}.[UpdatedAt]` | `DateTime_PrettyFormat(UpdatedAt)` |
| 9 | Recall At (right) | `{RequestForm}.[RecallAt]` | `DateTime_PrettyFormat(RecallAt)` |
| 10 | Delv /  Collection Date (two spaces, not sortable) | — | block `DeliveryDate(RequestFormId)`, below |
| 11 | Request Form Status | `{RequestFormStatus}.[Label]` | `RequestFormStatus.Label` |
| 12 | Printed At (right) | `{RequestForm}.[PrintedAt]` | `DateTime_PrettyFormat(PrintedAt)` |
| 13 | Is Pending Print | `[IsPendingPrint]` | green check icon (`Icon_Check_Green`) when `IsPendingPrint` |
| 14 | OPO Checked (center) | `{RequestForm}.[IsOPOChecked]` | if `Session.RoleId` ∈ {StockAllocator, OPO, CreditGM}: checkbox bound to IsOPOChecked, enabled only when `IsOPOChecked = False AND RequestFormStatusId = PendingCreditCheck`, on change → `OPOCheck(RequestFormId)` → `CheckRequestForm` (sets IsOPOChecked = True, OPOCheckedOn = CurrDateTime(), OPOCheckedBy = Session.CurrUserId) then refresh; otherwise green check icon when IsOPOChecked (inferred from the If/branches) |
| 15 | (no header text) | — | link "Print" (download icon) → `PrintNew(RequestFormId, RequestFormTypeId)`; shown when `Role.RoleId` ∈ {SalesAdmin, Admin, StockAllocator, Manager, Head, PPSpecialistManager, SpecialistSeniorManager, MD, CreditGM, Scheduler} **and** status ∈ {PendingCreditCheck, PendingAllocation, PendingInvoicing, Backordered} |

`DateTime_PrettyFormat` as in spec-detail.md (empty date = 1900-01-01 → show "-").

Block **DeliveryDate**(RequestFormId): aggregate `GetRequestFormItemsByRequestFormId` = RequestFormItem WHERE
`RequestFormId = @id AND IsActive = True`, **GROUP BY RequestFormItem.Date** (sort decodes as RequestFormItem.Id).
ListRecords shows each date as `FormatDateTime_(Date)` = "dd/MM/yyyy" ("dd/MM/yyyy HH:mm" when the time is not
00:00:00; Date type so always dd/MM/yyyy; NullDate → empty). When more than 1 date: an Expand / Collapse link
toggles (`Toggle`, local `Expanded`); collapsed shows the first date only (inferred).

`PrintNew` (left out of the viewer, writes): SQL `UPDATE RequestForm SET PrintedAt = CurrDateTime() WHERE Id = @id`;
if type ≠ Collection → `SetDeliveryFormItem(id)`; URL = `GetEntryURL("MyOrders", If(type = Collection,
"CollectionForm", "DeliveryFormNew"), Token = GenerateToken(), RequestFormId = id)` → HTML-to-PDF (page number "F")
→ download "RequestForm #<id>.pdf". The PDF entries (flow RequestFormPDF: CollectionForm, DeliveryFormNew,
DeliveryForm, DeliveryFormPDF, DeliveryFormPDFNew, screen CollectionFormPDF) only check `Token = GenerateToken()`.

### 1.5 Download (Excel "Request Forms.xlsx", action `Download`)

Aggregate `GetRequestForms`: same filters 1–8 as §1.2 (same session variables, `ShowAll()`, Role) but joins
`INNER Companies, INNER RequestFormType, LEFT RequestFormItem ON RequestFormItem.RequestFormId = RequestForm.Id,
INNER Users ON SalesmanId (so forms without salesman drop out), LEFT RequestFormStatus, LEFT Reference`; **no**
`RequestFormItem.IsActive` filter; GROUP BY RequestFormType.Label, RequestForm.CostingSheetId, RequestFormItem.Date,
RequestForm.Id, RequestFormStatus.Label, RequestForm.CreatedAt, Users.Username, Companies.Company; sorted by
RequestForm.Id ascending (no IsDescending in the decode). One row per (form, distinct item date).

Columns (structure `RequestFormDownload`, label = Excel header):
| Header | Value |
|---|---|
| Request Form Id | RequestForm.Id |
| Type | RequestFormType.Label |
| Status | RequestFormStatus.Label |
| Company | Companies.Company |
| Delv / Collection Date | RequestFormItem.Date (Date) |
| Salesperson | Users.Username (= NAME) |
| Costing Sheet Id | RequestForm.CostingSheetId |
| Created On | RequestForm.CreatedAt (DateTime) |

### 1.6 Download Allocation (Excel "Allocations.xlsx", action `DownloadAllocation`)

Aggregate `GetRequestFormsByCreatedAt2` = the list aggregate (§1.2, same joins, IsPendingPrint, filters 1–8, same
sort) plus `LEFT AllocationItem ON AllocationItem.RequestFormId = RequestForm.Id` and filter `AllocationItem.IsActive`
(boolean filter, so it behaves as an inner join on active allocation items). `RecordListToExcel` of the whole
aggregate list: every attribute of every source (RequestForm, RequestFormType, Companies, Contacts,
RequestFormStatus, Reference, Users, AllocationItem) plus IsPendingPrint (inferred: OutSystems exports all columns).

---------------------------------------------------------------------------------------------------------------------

## 2. Who sees which request forms

### 2.1 `ShowAll()` (user function, also used by the costing sheets)
```
ShowAll = Session.RoleId IN (CreditManager, Credit, CreditGM, StockAllocator, Allocator, SalesAdmin, AP, APManager,
                             MD, Admin, Scheduler, Head[XyDg8VkHm0aABtYp6Il6CA])
```

### 2.2 The RequestForms list (and both Excel downloads)
```
RequestForm.IsActive = True
AND ( ShowAll()  OR  Index(Session.UserList, "|" + RequestForm.SalesmanId + "|") <> -1 )
AND ( Role(Session.CurrUserId).RoleId <> ServiceDeliveryManager  OR  Companies.Company LIKE '%dbs%' )
```
Only **SalesmanId** is checked (not CreatedBy, not the specialists, not ApprovedBy*). No status condition.
`Session.UserList` = "|id|id|…|" from MyCRM GetSub (set at login, as for costing sheets). Identical to the costing
sheet rule except the SDM part is in the same aggregate. The SDM rule applies even to ShowAll roles (but SDM is not a
ShowAll role, so it only matters for SDM).

Home page search (block `PossibleResults` on HomePage, `GetRequestFormsByReferenceId`): `RequestForm.IsActive = True
AND IntegerToText(RequestForm.Id) = Session.Home_SearchKeyword AND (Index(Session.UserList, "|"+SalesmanId+"|") <> -1
OR ShowAll())` — **no SDM rule** there; hits link via `SearchResult` → RequestFormSummary.

### 2.3 Specialists' list (screen `SpecialistRequestForms`, flow Specialist_RequestForm; key pCXFV9pGMUiMeQGQ+G1+Mg)
Second list of request forms, for specialists. Aggregate `GetRequestForms` (RequestForm INNER Companies, LEFT
RequestFormStatus, LEFT Reference, LEFT Users on SalesmanId), filters:
```
keyword: Status.Label LIKE %kw% OR Reference.Reference LIKE %kw% OR Companies.Company LIKE %kw%
         (kw = Session.RequestForms_SearchKeyword, shared with RequestForms; no Id search)
AND ( MDSSpecialistId = Session.CurrUserId
   OR MITSSpecialistId = Session.CurrUserId
   OR Session.RoleId = MDSSpecialistManager
   OR Session.RoleId = MITSSpecialistManager
   OR Session.RoleId = SpecialistSeniorManager
   OR ( Index(Session.UserList, "|" + PPSpecialistId + "|") <> -1
        AND (Session.RoleId = ~Os [unresolved role] OR Session.RoleId = PPSpecialistManager) )
   OR IsSalesAdmin(Session.CurrUserId) )
AND RequestForm.IsActive = True
AND ( PPSpecialistId <> NullIdentifier() OR MITSSpecialistId <> NullIdentifier() OR MDSSpecialistId <> NullIdentifier() )
sort: List_SortColumn_GetOrderBy(RequestFormTable.Id, '{RequestForm}.[CreatedAt] desc')
```
(Prefix decode `OR OR OR OR OR OR … AND <> Index … OR = ~Os = PPSpecialistManager IsSalesAdmin` unfolded as above.)

### 2.4 RequestFormSummary preparation (the detail page)
`If RequestFormId = NullIdentifier() → RequestForms`; `Role(Session.CurrUserId)`; `GetRequestFormById` filter only
`RequestForm.Id = RequestFormId` (**no IsActive, no user, no SDM filter**); then items. **No access check**: anyone
who has the URL can open any request form, deleted ones included. Same as CostingSheetSummary.

GetRequestFormById joins: INNER RequestFormType, INNER Companies, INNER Address (on AddressId), INNER Contacts,
LEFT Reference, LEFT Users (SalesmanId), LEFT RequestFormStatus, LEFT CompanyGroup (Company.CompanyGroupId),
LEFT Event Tag (EventTagId). Items: RequestFormItem WHERE RequestFormId = @id AND IsActive = True, sort Id; LEFT
Brand, CollectionType, AssetType, Phones (PhoneId), Product (DeliveryProductId) → ProductGroup.
(Viewer: use LEFT joins for Company/Address/Contact/Type and apply the list rule §2.2 before showing.)

Buttons on the summary (all write, left out): Edit (→ RequestFormDetail), Approve / Approve2 / Reject (several
role-specific pairs), Recall, Print, Converttocostingsheet (shown when SalesmanId = Session.CurrUserId and a status
condition), Delete, UpdateModelSerial.

### 2.5 RequestFormDetail preparation (edit page)
`GetRequestFormById` by Id only (no access check, no IsActive). The pick lists are user-filtered with
`Index(Session.UserList, "|"+SalesmanId+"|") <> -1` (no ShowAll): previous request forms (same company, active, Id ≠
this, type ≠ Collection), costing sheets (company, active), proposals (company, active), prospects (company, active,
not cancelled, DateClosed ≥ CurrDate − 18 months, CRMProspectId in UserList); `GetCompaniesByUser(Session.UserList)`.
Not needed by the viewer.

### 2.6 RequestPDF
`RequestPDF` is **not** a request form page: it is the MyHelpdesk service-request PDF (flow MyHelpdeskPDF, input
RequestId, reads Request / Request Job). The request form PDFs are in §1.4 (token only).

---------------------------------------------------------------------------------------------------------------------

## 3. Data model

Type keys: %uROO Text, %oD0 Boolean, %EYiT Currency, %snOQ Decimal, %j2Lc Integer, %H6G Long Integer, %TI32 DateTime,
%Dnfd Date, %T8i8 Binary, %Qs4r Time, %uCRx Phone Number. "→ user" = OSSYS_USER. M = mandatory. Delete rules: only
those decoded are given (others: OutSystems default).

**RequestForm** (73)

| Attribute | Type | Notes |
|---|---|---|
| Id | Long Integer (auto) | M |
| RequestFormTypeId | → RequestFormType | M |
| PrevRequestFormId | → RequestForm (self) | previous request form (Detail pick list) |
| ProspectId | → MyCRM Prospect (entity 7fotb_1y10KFmxBnkOlT2g) | |
| ProposalId | → Proposal | |
| OptionId | → Option | |
| CostingSheetId | → CostingSheet | set by Converttocostingsheet (SQL) or chosen in Detail |
| RequestFormStatusId | → RequestFormStatus | not mandatory (list joins LEFT) |
| ReferenceId | → Reference | |
| CompanyId | → MyCRM Company | M |
| ContactId | → MyCRM Contacts | M |
| AddressId | → MyCRM Address | M, del=Ignore |
| SalesmanId | → user | = creator on create; access rule column |
| CreatedBy | → user | |
| CreatedAt | DateTime | |
| UpdatedBy | → user | |
| UpdatedAt | DateTime | |
| IsActive | Boolean | false = deleted |
| TotalSTP | Currency | recomputed by TotalRequestFormSTP (SQL) |
| PrintedAt | DateTime | |
| ApprovedAt | DateTime | first approval (§4) |
| ApprovedBy | → user | |
| IsSent | Boolean | Allocation "Send" |
| SentAt | DateTime | |
| SentBy | → user | |
| PickingSlipNumber | Text | |
| IsPickingSlipPrinted | Boolean | |
| InvoiceNumber | Text | |
| IsInvoicePrinted | Boolean | |
| SalesOrderNumber | Text | |
| BackorderedSalesOrderNumber | Text | |
| BRN | Text | |
| CustomerName | Text | |
| DeliveryAddress1 | Text | |
| DeliveryAddress2 | Text | |
| PostalCode | Text | |
| Country | Text | |
| Warehouse | Text | |
| IsSNEntryCompleted | Boolean | |
| MDSSpecialistId | → user | |
| PPSpecialistId | → user | |
| MITSSpecialistId | → user | |
| PendingMDS | Boolean | specialist still to act |
| PendingPP | Boolean | |
| PendingMITS | Boolean | |
| Color | Text | GenerateColorByRequestFormId (SQL) |
| CreditApprovedBy | → user | |
| CreditApprovedAt | DateTime | |
| IsBackorderCleared | Boolean | Allocation "BackorderCleared" (SQL) |
| DeletedBy | → user | |
| DeletedAt | DateTime | |
| RecallAt | DateTime | |
| IsReconBilled | Boolean | |
| NoOfWorkstation | Integer | IT infrastructure page |
| ServerAvailability | Text | |
| IsUserCodeSetting | Boolean | |
| OperatingSystem | Text | |
| IsICE | Boolean | |
| UseNewItem | Boolean | |
| IsEPP | Boolean | |
| PendingCreditOn | DateTime | |
| CreditEndorsedOn | DateTime | |
| IsOPOChecked | Boolean | |
| OPOCheckedOn | DateTime | |
| OPOCheckedBy | → user | |
| CustomerNo | Text | |
| Declared | Boolean | |
| AgilityRunningNumber | Integer | |
| ApprovedByManagerId | → user | |
| ApprovedByManagerOn | DateTime | |
| ApprovedByHeadId | → user | |
| ApprovedByHeadOn | DateTime | |
| EventTagId | → Event Tag (other module, entity E9NV3zLCvkSJ07f6JxmCCw; as CostingSheet.EventTagId) | |

Delete: never deleted. Summary/Detail **Delete** = SQL `UPDATE RequestForm SET IsActive = 0, DeletedBy = @UserId,
DeletedAt = @Datetime WHERE Id = @RequestFormId`, then → RequestForms.

**RequestFormItem** (52) — `IsActive = False` on row delete (tblCollectionItem / tblItem delete, DelRequestFormItem).

| Attribute | Type | Notes |
|---|---|---|
| Id | Long Integer (auto) | M |
| RequestFormId | → RequestForm | |
| ProductType | Text | |
| ProductName | Text | |
| EDPCode | Text | |
| DeliveryProductId | → Product | |
| DeliveryItemId | → Item | |
| Quantity | Integer | |
| ProspectUnitPrice | Currency | |
| UnitPrice | Currency | |
| TotalSTP | Currency | |
| IsActive | Boolean | |
| CompanyId | → MyCRM Company | delivery/collection site |
| BRN | Text | |
| AddressId | → MyCRM Address | |
| ContactId | → MyCRM Contacts | |
| PhoneId | → MyCRM Phones (entity wxTjnZZaWEKRixIwBnf+gw) | |
| Date | Date | delivery / collection date (list column 10) |
| Remarks | Text | |
| Model | Text | |
| Serial | Text | |
| Description | Text | |
| BrandId | → Brand | static: OTHERS, KONICA MINOLTA, WYWY (RICOH), TOSHIBA, ESPON, BROTHER, IBM, SAMSUNG, KYOCERA, FUJI XEROX, LEXMARK, HP, CANON, RICOH |
| AssetTypeId | → AssetType | static: Hardware, Software, Others |
| CollectionTypeID | → CollectionType | static: Exchange, Other, Loan, Pre-Termination (With Rollover), Trial, Termination, Trade-In, Contract Expired, Pre-Termination (No Rollover), Demo |
| SpecialInstructions | Text | |
| NoLift | Boolean | |
| CallBeforeCollection | Boolean | |
| SecondaryContact | Text | |
| SecondaryContactNo | Phone Number | |
| PostalCode | Text | |
| AchievedPrice | Currency | |
| CostingSheetItemId | → CostingSheetItem | |
| OptionItemId | → OptionItem | |
| IsRetain | Boolean | |
| PacketId | → Packet | |
| IsPacket | Boolean | |
| IsFSA | Boolean | |
| IsMDS | Boolean | |
| IsPP | Boolean | |
| IsSOL | Boolean | |
| IsRecon | Boolean | |
| ProspectItemId | → MyCRM prospect item (entity IXa98V+x9Em3veJGlyiauw) | |
| HasFinisher | Boolean | |
| Time | Time | |
| DeliveryTeam | Text | |
| ContractNo | Text | |
| ReasonOfTermination | Text | |
| RemovalFee | Currency | |
| InvoiceNo | Text | |
| BalanceToInvoice | Currency | |
| RolloverAmount | Currency | |

**RequestFormSubItem** (10) — `IsActive = False` on delete (tblSubItem).

| Attribute | Type |
|---|---|
| Id | Long Integer (auto), M |
| RequestFormItemId | → RequestFormItem |
| ProductType | Text |
| ProductName | Text |
| EDPCode | Text |
| ProductId | → Product |
| ItemId | → Item |
| Quantity | Integer |
| UnitPrice | Currency |
| IsActive | Boolean |

**RequestFormComplexItem** (14) — `IsActive = False` on delete (tblComplexItem).

| Attribute | Type |
|---|---|
| Id | Long Integer (auto), M |
| RequestFormItemId | → RequestFormItem |
| ComplexItemTypeId | → ComplexItemType, M (static: Registered OS Items [NonEnterprise], New, Enterprise) |
| ProductId | → Product |
| ItemId | → Item |
| ProductName | Text |
| ProductType | Text |
| EDPCode | Text |
| Quantity | Integer, M |
| IsActive | Boolean |
| IsPP | Boolean |
| IsMDS | Boolean |
| IsMITS | Boolean |
| Description | Text |

**RequestFormAttachment** (8) — **hard-deleted** (`DeleteEntity` in RequestFormDetail's attachment list); no IsActive.

| Attribute | Type |
|---|---|
| Id | Long Integer (auto), M |
| RequestFormId | → RequestForm |
| Binary | Binary Data (read as DATALENGTH; bytes only for download) |
| FileName | Text |
| UploadedBy | → user |
| UploadedOn | DateTime |
| IsComplex | Boolean |
| Remarks | Text |

**RequestFormSolution** (3): Id Long Integer (auto) M; RequestFormId → RequestForm; SolutionId → Solution_Decrepated
(deprecated static, single record "@Remote Accounting Report 100DL"). No IsActive (delete behaviour not checked).

**RequestFormStatus** (static, 15): Id Integer (auto) M, Label Text M, Order Integer M, Is_Active Boolean M. All
records Is_Active = True. Match by Label.

| Order | Record name | Label |
|---|---|---|
| 1 | Draft | Draft |
| 2 | PendingManager | Pending Manager |
| 3 | PendingHead | Pending Head |
| 4 | PendingCreditCheck | Pending Credit Check |
| 5 | PendingAllocation | Pending Allocation |
| 6 | Backordered | Backordered |
| 7 | PendingInvoicing | Pending Invoicing |
| 8 | ManagerRejected | Manager Rejected |
| 9 | HeadRejected | Head Rejected |
| 10 | CreditRejected | Credit Rejected |
| 11 | PendingSpecialist | Pending Specialist |
| 12 | PendingEDPCode | Pending EDP Code |
| 13 | PendingReconAllocation | Pending Recon Allocation |
| 14 | StockAllocatorRejected | Stock Allocator Rejected |
| 15 | Invoiced | Invoiced |

**RequestFormType** (static, 5): same 4 attributes. All Is_Active = True.

| Order | Record name | Label |
|---|---|---|
| 1 | DeliveryOrder | Delivery Order |
| 2 | Collection | Collection |
| 3 | Demo | Demo |
| 4 | Loan | Loan |
| 5 | Trial | Trial |

**Entities that reference request forms**
- RequestForm.Id ← CostingSheet.RequestFormId, AdvanceOrder.RequestFormId (M), AllocationItem.RequestFormId,
  PurchaseRequisition.RequestFormId, CSRFItemMatch.RequestFormId, DeliveryFormPrint.RequestFormId,
  DeliveryFormPrintItem.RequestFormId, RequestFormItem / RequestFormAttachment / RequestFormSolution .RequestFormId,
  RequestForm.PrevRequestFormId.
- RequestFormItem.Id ← RequestFormSubItem, RequestFormComplexItem, CostingSheetItem.RequestFormItemId,
  AllocationItem.RequestformItemId (lower-case f), AdvanceOrderItem.RequestFormItemId,
  DeliveryFormPrintItem.RequestFormItemId.
- The costing sheet's request form: `CostingSheet.RequestFormId` when set, else the first active `RequestForm WHERE
  CostingSheetId = @cs` (GetRFIdByCSId, spec-detail §1.7).

---------------------------------------------------------------------------------------------------------------------

## 4. Statuses: transitions and the columns that record them

Central action `UpdateRequestformStatus(RequestFormId, RequestFormStatusId, IsCredit)`:
1. reads the form, `GetEmailByUser(SalesmanId)`;
2. if new status = PendingManager and the salesman is a "dealer" (`Index(ToLower(email), "@ricoh.sg") = -1 OR
   ToLower(email) = "amiechong@ricoh.sg"`) → new status := PendingHead;
3. SQL `UPDATE RequestForm SET RequestFormStatusId = @status WHERE Id = @id`;
4. email RequestFormEmail to the salesman when email ≠ "";
5. new status = PendingCreditCheck → `PendingCreditOn = CurrDateTime()`; new status ∈ {CreditRejected,
   PendingAllocation, PendingInvoicing} and IsCredit → `CreditEndorsedOn = CurrDateTime()`; UpdateEntity.

| Step (action, screen) | Status set | Columns written |
|---|---|---|
| Create (Next, RequestFormDetail, RequestFormId = null) | Draft | SalesmanId = CreatedBy = Session.CurrUserId, CreatedAt = now, IsActive = True |
| Save (Next, RequestFormDetail, existing) | — | UpdatedBy, UpdatedAt |
| Submit (DeliveryCollection; also Next on ChooseRecon) | PendingSpecialist if ShowMDS/ShowPP/ShowMITS_RequestForm(id); else dealer → PendingHead; else PendingManager | status only (no submit date) |
| Specialist Submit (Specialist_RequestForm) | clears own PendingMDS/PendingPP/PendingMITS; when none left: PendingEDPCode if new items need EDP codes ("HasNew"), else dealer → PendingHead, else PendingManager (inferred order) | Pending* flags only |
| Approve by Manager / PPSpecialistManager (RequestFormSummary) | PendingHead | ApprovedAt, ApprovedBy, ApprovedByManagerId, ApprovedByManagerOn (= now / current user) |
| Approve by Head / SpecialistSeniorManager | form has recon items: PendingCreditCheck if IsReconAllocated else PendingReconAllocation; no recon: PendingCreditCheck | ApprovedByHeadId, ApprovedByHeadOn; and ApprovedAt/ApprovedBy only if ApprovedAt was NullDate (manager skipped); PendingCreditOn via UpdateRequestformStatus |
| Approve by CreditManager / Credit / CreditGM / MD | Collection → PendingInvoicing, else PendingAllocation (IsCredit = True) | CreditApprovedAt, CreditApprovedBy, CreditEndorsedOn; UpdateCustomerNo |
| Reject (RequestFormSummary) | Manager/PPSpecialistManager → ManagerRejected; Head/SpecialistSeniorManager → HeadRejected; CreditManager/Credit → CreditRejected | status only (CreditEndorsedOn only if IsCredit, not passed by Reject (inferred)) |
| UpdateModelSerial (summary), AllocateRecon | PendingReconAllocation → PendingCreditCheck | PendingCreditOn |
| Allocate (Allocation flow) | Backordered or PendingInvoicing | — |
| RejectRequestForm (Allocation) | StockAllocatorRejected (AllocationItem.IsActive = 0 for the form) | — |
| UpdateBackorders (Allocation) | Backordered → PendingInvoicing | — |
| OPO check (list) | — | IsOPOChecked = True, OPOCheckedOn, OPOCheckedBy |
| Print (summary) / Print link (list, PrintNew) / Admin Print | — | PrintedAt (SQL); no "printed by" |
| Send (Allocation) | — | IsSent = 1, SentAt, SentBy (SQL) |
| BackorderCleared (Allocation) | — | IsBackorderCleared = 1 |
| Recall (summary → RecallRequestForm) | Draft | RecallAt = now; **clears** PrintedAt, PendingCreditOn, CreditEndorsedOn (NullDate), IsOPOChecked = False, OPOCheckedOn/OPOCheckedBy (cleared, inferred); also ClearReconModelSerial, RevertMyRecon, CancelRequestFormChechlists (if Site.EnableHelpdesk) |
| Converttocostingsheet (summary) | — | CostingSheetId (SQL) |
| Delete (summary/detail) | — | IsActive = 0, DeletedBy, DeletedAt |
| Admin "Fix" | — | CreditEndorsedOn, CreditApprovedAt, CreditApprovedBy |
| (none found) | Invoiced | no action in MyOrders sets it (only read by RequestFormCanCreateHelpdesk) |

History box (viewer), one line per non-empty date, sorted by date: Created (CreatedAt, CreatedBy) · Updated
(UpdatedAt, UpdatedBy) · Approved by manager (ApprovedByManagerOn, ApprovedByManagerId) · Approved (ApprovedAt,
ApprovedBy) · Approved by head (ApprovedByHeadOn, ApprovedByHeadId) · Pending credit since (PendingCreditOn) · Credit
endorsed (CreditEndorsedOn) · Credit approved (CreditApprovedAt, CreditApprovedBy) · OPO checked (OPOCheckedOn,
OPOCheckedBy) · Printed (PrintedAt) · Sent (SentAt, SentBy) · Recalled (RecallAt) · Deleted (DeletedAt, DeletedBy).
Caveats: submit, specialist steps and rejections leave no date; a recall clears PrintedAt / PendingCreditOn /
CreditEndorsedOn / OPO fields but keeps the approval dates, so after a recall and re-approval only the latest values
remain. Empty dates are 1900-01-01.

---------------------------------------------------------------------------------------------------------------------

## 5. Links into the request form screens

E-mails (URL = `"https://" + GetReverseProxyServer().ServerName + ".ricoh.sg/MyOrders/…"`):
- `RequestFormEmail` (sent by UpdateRequestformStatus, Approve, Submit…): `RequestFormSummary.aspx?RequestFormId=<id>`;
  when `IsSpecialist` → `ComplexItems_RequestForm.aspx?RequestFormId=<id>`; when status = PendingReconAllocation →
  `.ricoh.sg/MyRecon/MyOrdersRequest.aspx`. Subject part: "Rejected" (Manager/Head/Credit Rejected), "Approved"
  (Pending Invoicing), else "Pending Approval". Body: "Request Form # ", "Status: ", "Click or copy the following url
  to your browser", Company, Salesman, Created At.
- `CollectionFormEmail`: same three URLs and subjects.
- `DeliveryDateChangeEmail`: `RequestFormSummary.aspx?RequestFormId=<id>`.
- email screen muMAAP_2WUCyVjJjnlZBUw (Approve, Collection with Pre-Termination collection type, to "rspopo@ricoh.sg,
  servicecontracts@ricoh.sg"): `RequestFormSummary.aspx?RequestFormId=<id>`.

Screens linking to `RequestFormSummary(RequestFormId)`: RequestForms (Id and Reference cells), SearchResult (home page
search), CSRFItemCompare (costing sheet CS/RF block), AdvanceOrders, AdvanceOrderDetail (OPPA flow), CreditCharts
(Dashboard flow), AllocationHistory (Allocation flow), ComplexItems_RequestForm (Specialist flow), plus WebDestinations
after Approve/Reject/Recall (same screen). Parameter is `RequestFormId` (the summary's only input; per screen
not re-checked).
`RequestFormDetail(RequestFormId)`: RequestForms ("Create a new Request Form", NullIdentifier), RequestFormSummary
(Edit), Item / Item_New (back), Specialist_RequestForm flow (a go-to node).
`RequestForms`: menu, and the destination after Submit, Delete, and RequestFormSummary with no id.
