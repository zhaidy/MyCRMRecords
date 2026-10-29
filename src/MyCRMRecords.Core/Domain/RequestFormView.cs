namespace MyCRMRecords.Domain;

// A request form as its page shows it (MyOrders' RequestFormSummary) and as MyOrders printed it (its delivery form and
// collection form), read only. Every value comes from MyOrders' and MyCRM's tables as they are: the totals are the ones
// MyOrders stored, never worked out again.

/// <summary>A request form's page: header, declaration, attachments, items, delivery or collection, history and facts.</summary>
public sealed record RequestFormView
{
    public long Id { get; init; }
    /// <summary>Whether MyOrders let the person print it (a printable status and a role that prints): the Print button.</summary>
    public bool CanPrint { get; init; }
    /// <summary>False for a request form deleted in MyOrders (it is not in the list; its page says so).</summary>
    public bool IsActive { get; init; }
    public DateTime? DeletedAt { get; init; }
    public string DeletedBy { get; init; } = "";

    public string Type { get; init; } = "";
    /// <summary>A Collection request form shows Collection Items; the others Items and Delivery.</summary>
    public bool IsCollection { get; init; }
    public string Reference { get; init; } = "";
    public string Salesman { get; init; } = "";
    public string Status { get; init; } = "";
    public IReadOnlyList<CostingSheetSpecialist> Specialists { get; init; } = Array.Empty<CostingSheetSpecialist>();
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? RecallAt { get; init; }
    public DateTime? PrintedAt { get; init; }
    /// <summary>MyCRM's Id of the company (the company's page here), or null.</summary>
    public long? CompanyId { get; init; }
    public string Company { get; init; } = "";
    public string Address { get; init; } = "";
    public string Contact { get; init; } = "";
    public IReadOnlyList<string> Phones { get; init; } = Array.Empty<string>();
    public string CustomerNo { get; init; } = "";
    public string CustomerType { get; init; } = "";
    public string EventTag { get; init; } = "";
    /// <summary>The delivery declaration (MyOrders' Covid-19 checkbox).</summary>
    public bool Declared { get; init; }

    public IReadOnlyList<CostingSheetAttachmentView> Attachments { get; init; } = Array.Empty<CostingSheetAttachmentView>();
    /// <summary>The costing sheet of the request form, or null.</summary>
    public long? CostingSheetId { get; init; }
    /// <summary>Whether that costing sheet is one the person may open (its link).</summary>
    public bool CanOpenCostingSheet { get; init; }
    /// <summary>MyOrders' CS/RF differences: EDP codes whose quantities differ between the costing sheet and the request form.</summary>
    public IReadOnlyList<CostingSheetDifference> Differences { get; init; } = Array.Empty<CostingSheetDifference>();
    public IReadOnlyList<RequestFormItemView> Items { get; init; } = Array.Empty<RequestFormItemView>();
    /// <summary>Request Total Street Price, as MyOrders stored it.</summary>
    public decimal TotalStp { get; init; }
    public IReadOnlyList<CostingSheetStep> History { get; init; } = Array.Empty<CostingSheetStep>();
    public RequestFormFacts Facts { get; init; } = new();
}

/// <summary>An item of the request form: what is delivered (or collected), with its sub items, complex items and packet, and where and when.</summary>
public sealed record RequestFormItemView
{
    public long Id { get; init; }
    public string ProductName { get; init; } = "";
    public string ProductType { get; init; } = "";
    public string EdpCode { get; init; } = "";
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    /// <summary>The item's street price total as MyOrders stored it.</summary>
    public decimal TotalStp { get; init; }
    public IReadOnlyList<CostingSheetSubItemView> SubItems { get; init; } = Array.Empty<CostingSheetSubItemView>();
    /// <summary>Complex items: MyOrders showed them at $0.00 (a request form's complex items have no price).</summary>
    public IReadOnlyList<RequestFormComplexItemView> ComplexItems { get; init; } = Array.Empty<RequestFormComplexItemView>();
    public IReadOnlyList<CostingSheetPacketItemView> PacketItems { get; init; } = Array.Empty<CostingSheetPacketItemView>();

    public string Address { get; init; } = "";
    public string PostalCode { get; init; } = "";
    /// <summary>The delivery or collection date.</summary>
    public DateTime? Date { get; init; }
    public string Contact { get; init; } = "";
    public string Phone { get; init; } = "";
    public bool IsFsa { get; init; }
    public bool NoLift { get; init; }
    public string Remarks { get; init; } = "";
    public string Model { get; init; } = "";
    public string Serial { get; init; } = "";
    public string Description { get; init; } = "";
    public string Brand { get; init; } = "";
    public string AssetType { get; init; } = "";
    public string CollectionType { get; init; } = "";
    public string ContractNo { get; init; } = "";
    public string ReasonOfTermination { get; init; } = "";
    public decimal RemovalFee { get; init; }
    public string InvoiceNo { get; init; } = "";
    public decimal BalanceToInvoice { get; init; }
    public decimal RolloverAmount { get; init; }
    public string SecondaryContact { get; init; } = "";
    public string SecondaryContactNo { get; init; } = "";
    public bool CallBeforeCollection { get; init; }
    public string SpecialInstructions { get; init; } = "";
    public bool HasFinisher { get; init; }
}

public sealed record RequestFormComplexItemView(string ProductName, string EdpCode, int Quantity);

/// <summary>Further facts MyOrders keeps on the request form.</summary>
public sealed record RequestFormFacts
{
    public long? ProposalId { get; init; }
    public long? OptionId { get; init; }
    public long? PreviousRequestFormId { get; init; }
    public string SalesOrderNumber { get; init; } = "";
    public string BackorderedSalesOrderNumber { get; init; } = "";
    public string PickingSlipNumber { get; init; } = "";
    public string InvoiceNumber { get; init; } = "";
    public string Warehouse { get; init; } = "";
    public bool IsOpoChecked { get; init; }
    public bool IsEpp { get; init; }
    public bool IsReconBilled { get; init; }
}

/// <summary>
/// What MyOrders' request form PDF shows: its header, then the delivery form (every type but Collection: one part per
/// delivery, 25 lines a page, from the delivery form MyOrders printed last) or the collection form.
/// </summary>
public sealed record RequestFormPrint
{
    public long Id { get; init; }
    public string Reference { get; init; } = "";
    public string Type { get; init; } = "";
    public bool IsCollection { get; init; }
    /// <summary>MyOrders' running number on the header (its Agility running number less one).</summary>
    public int AgilityRunningNumber { get; init; }
    /// <summary>The "(Updated ...)" date: when it was last updated, else created.</summary>
    public DateTime? UpdatedOn { get; init; }
    public string Company { get; init; } = "";
    public string CustomerNo { get; init; } = "";
    public string Salesman { get; init; } = "";
    public string SalesmanCode { get; init; } = "";
    public DateTime? CreatedAt { get; init; }
    public DateTime? PrintedAt { get; init; }
    public bool Declared { get; init; }
    /// <summary>The deliveries of the delivery form MyOrders printed last (empty when MyOrders never printed it, or for a collection).</summary>
    public IReadOnlyList<RequestFormDelivery> Deliveries { get; init; } = Array.Empty<RequestFormDelivery>();
    /// <summary>The collection form (Collection type only).</summary>
    public RequestFormCollection? Collection { get; init; }
}

/// <summary>One delivery of a delivery form: where, when, to whom, and its lines page by page.</summary>
public sealed record RequestFormDelivery
{
    public string Address { get; init; } = "";
    public string CountryPostalCode { get; init; } = "";
    public string Contact { get; init; } = "";
    public string Phone { get; init; } = "";
    public DateTime? Date { get; init; }
    public bool NoLift { get; init; }
    public string Remarks { get; init; } = "";
    public IReadOnlyList<IReadOnlyList<RequestFormPrintLine>> Pages { get; init; } = Array.Empty<IReadOnlyList<RequestFormPrintLine>>();
}

/// <summary>A line of a delivery form: Item Code, IsFSA, Name, Serial No., Quantity (0: empty), Allocated Qty.</summary>
public sealed record RequestFormPrintLine(string ItemCode, bool IsFsa, string Name, string SerialNo, int Quantity, int AllocatedQuantity);

/// <summary>The collection form: boxes A to C from the first item, every item, and who created and approved it.</summary>
public sealed record RequestFormCollection
{
    public string AssetType { get; init; } = "";
    public string CollectionType { get; init; } = "";
    public string Contact { get; init; } = "";
    public string ContactNo { get; init; } = "";
    public string SecondaryContact { get; init; } = "";
    public string SecondaryContactNo { get; init; } = "";
    public string SpecialInstructions { get; init; } = "";
    public string Address { get; init; } = "";
    public string PostalCode { get; init; } = "";
    public DateTime? Date { get; init; }
    public IReadOnlyList<RequestFormItemView> Items { get; init; } = Array.Empty<RequestFormItemView>();
    public string ApprovedByManager { get; init; } = "";
    public DateTime? ApprovedByManagerOn { get; init; }
    public string ApprovedByHead { get; init; } = "";
    public DateTime? ApprovedByHeadOn { get; init; }
}
