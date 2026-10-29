namespace MyCRMRecords.Domain;

// A costing sheet as its page shows it (MyOrders' CostingSheetSummary), read only. Every value comes from MyOrders' and
// MyCRM's tables as they are: the totals are the ones MyOrders stored on the costing sheet, never worked out again.

/// <summary>A costing sheet's page: header, attachments, products, billing, costs, meter charges and its history.</summary>
public sealed record CostingSheetView
{
    public long Id { get; init; }
    /// <summary>Whether MyOrders let the person print it (a printable status and a role that prints): the Print button.</summary>
    public bool CanPrint { get; init; }
    /// <summary>False for a costing sheet deleted in MyOrders (it is not in the list; its page says so).</summary>
    public bool IsActive { get; init; }
    public DateTime? DeletedAt { get; init; }
    public string DeletedBy { get; init; } = "";

    public string Reference { get; init; } = "";
    public string Salesman { get; init; } = "";
    public string Status { get; init; } = "";
    public IReadOnlyList<CostingSheetSpecialist> Specialists { get; init; } = Array.Empty<CostingSheetSpecialist>();
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? RecallAt { get; init; }
    /// <summary>MyCRM's Id of the company (the company's page here), or null.</summary>
    public long? CompanyId { get; init; }
    public string Company { get; init; } = "";
    public string Address { get; init; } = "";
    public string Contact { get; init; } = "";
    public string CustomerNo { get; init; } = "";
    public string CustomerType { get; init; } = "";
    public bool IsZba { get; init; }
    public bool IsContractExtension { get; init; }
    public bool IsFixedTerm { get; init; }
    public bool IsVitalPricing { get; init; }
    public bool GmaPricing { get; init; }
    public DateTime? TransferredOn { get; init; }
    public string OriginalSalesman { get; init; } = "";
    public string EventTag { get; init; } = "";

    public IReadOnlyList<CostingSheetAttachmentView> Attachments { get; init; } = Array.Empty<CostingSheetAttachmentView>();
    /// <summary>The request form of the costing sheet (MyOrders' GetRFIdByCSId), or null.</summary>
    public long? RequestFormId { get; init; }
    /// <summary>The products whose quantities differ between the costing sheet and its request form (MyOrders' CS/RF compare).</summary>
    public IReadOnlyList<CostingSheetDifference> Differences { get; init; } = Array.Empty<CostingSheetDifference>();

    public IReadOnlyList<CostingSheetItemView> Items { get; init; } = Array.Empty<CostingSheetItemView>();
    public decimal TotalStp { get; init; }
    public decimal TotalAchievedPrice { get; init; }
    public decimal CommissionableRevenue { get; init; }
    /// <summary>The margins and commissions: only for MyCRM's Head, Admin and SalesAdmin (and administrators here), as in MyOrders.</summary>
    public CostingSheetMargins? Margins { get; init; }

    public CostingSheetBilling Billing { get; init; } = new();
    public IReadOnlyList<CostingSheetVariable> Variables { get; init; } = Array.Empty<CostingSheetVariable>();
    public IReadOnlyList<CostingSheetTradeIn> TradeIns { get; init; } = Array.Empty<CostingSheetTradeIn>();

    /// <summary>What happened to the costing sheet, from the dates and people MyOrders keeps on it.</summary>
    public IReadOnlyList<CostingSheetStep> History { get; init; } = Array.Empty<CostingSheetStep>();
    public CostingSheetFacts Facts { get; init; } = new();
}

/// <summary>A specialist of the costing sheet: "PP Specialist", "MDS Specialist" or "Fusion Specialist", and whether their part is still pending.</summary>
public sealed record CostingSheetSpecialist(string Role, string Name, bool Pending);

public sealed record CostingSheetAttachmentView(long Id, string FileName, string Remarks, long Size, string UploadedBy, DateTime? UploadedOn, bool IsComplex);

public sealed record CostingSheetDifference(string EdpCode, string Description, int CostingSheetQuantity, int RequestFormQuantity);

/// <summary>An item (a product) of the costing sheet with its sub items, complex items, packet items and meter charges.</summary>
public sealed record CostingSheetItemView
{
    public long Id { get; init; }
    public string ProductName { get; init; } = "";
    public string ProductType { get; init; } = "";
    public string ProductGroup { get; init; } = "";
    public string EdpCode { get; init; } = "";
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal TotalStp { get; init; }
    public decimal AchievedPrice { get; init; }
    public bool IsSubscription { get; init; }
    public bool OaaPricing { get; init; }
    public bool IsPacket { get; init; }
    public string Model { get; init; } = "";
    public string Serial { get; init; } = "";
    public IReadOnlyList<CostingSheetSubItemView> SubItems { get; init; } = Array.Empty<CostingSheetSubItemView>();
    public IReadOnlyList<CostingSheetComplexItemView> ComplexItems { get; init; } = Array.Empty<CostingSheetComplexItemView>();
    public IReadOnlyList<CostingSheetPacketItemView> PacketItems { get; init; } = Array.Empty<CostingSheetPacketItemView>();
    /// <summary>The meter charges, for a configuration, mainframe or packet that is not MDS, solutions or production printing; else null.</summary>
    public CostingSheetMeter? Meter { get; init; }
}

public sealed record CostingSheetSubItemView(string ProductName, string EdpCode, int Quantity, decimal UnitPrice);

/// <summary>A complex item; its street price is "Not Available" in MyOrders when <see cref="Stp"/> is 1 or less (<see cref="HasStp"/>).</summary>
public sealed record CostingSheetComplexItemView(string ProductName, string EdpCode, string MpgCode, int Quantity, decimal Stp, decimal AchievedPrice)
{
    public bool HasStp => Stp > 1;
}

public sealed record CostingSheetPacketItemView(string ProductName, int Quantity);

/// <summary>An item's meter charges: under FSA or not, below GM CPC, and the copies and prices (in cents) when under FSA.</summary>
public sealed record CostingSheetMeter
{
    public bool IsFsa { get; init; }
    public bool BelowGmCpc { get; init; }
    public bool BlackAndWhiteOnly { get; init; }
    public int InitialBwCopies { get; init; }
    public decimal InitialBwPrice { get; init; }
    public int InitialClrCopies { get; init; }
    public decimal InitialClrPrice { get; init; }
    public int MonthlyBwCopies { get; init; }
    public decimal MonthlyBwPrice { get; init; }
    public int MonthlyClrCopies { get; init; }
    public decimal MonthlyClrPrice { get; init; }
    public int MonthlyMonths { get; init; }
    public decimal ThereafterBwPrice { get; init; }
    public decimal ThereafterClrPrice { get; init; }
}

public sealed record CostingSheetMargins
{
    public decimal PercentageAboveManagerPrice { get; init; }
    public decimal PercentageAboveLanded { get; init; }
    /// <summary>(Total achieved price − total landed cost) / total achieved price × 100; 0 when nothing was achieved.</summary>
    public decimal DealMargin { get; init; }
    public decimal TotalManagerPrice { get; init; }
    public decimal TotalLanded { get; init; }
    public decimal EstimatedCommission { get; init; }
    public decimal ConfirmedCommission { get; init; }
}

/// <summary>The Billing box; which lines show depends on the finance type and Is PPP, as in MyOrders.</summary>
public sealed record CostingSheetBilling
{
    public bool IsPpp { get; init; }
    public string FinanceType { get; init; } = "";
    public decimal FinanceOutrightAmount { get; init; }
    /// <summary>Internal or External Financing: months, interest, down payment, final payment and the monthly amount are shown.</summary>
    public bool IsFinancing { get; init; }
    public int Months { get; init; }
    public decimal InterestRate { get; init; }
    public decimal DownPayment { get; init; }
    public decimal FinalPercentage { get; init; }
    public decimal FinalPayment { get; init; }
    /// <summary>"Hardware Monthly" with PPP, else "Monthly Investment".</summary>
    public string MonthlyLabel { get; init; } = "";
    public decimal MonthlyLease { get; init; }
    /// <summary>PPP with internal financing: the PPP rates and volumes are shown.</summary>
    public bool ShowsPppRates { get; init; }
    public decimal PppInternalBwRate { get; init; }
    public decimal PppInternalColourRate { get; init; }
    public decimal PppCustomerBwRate { get; init; }
    public decimal PppCustomerColourRate { get; init; }
    public int PppMonthlyBwVolume { get; init; }
    public int PppMonthlyColourVolume { get; init; }
    /// <summary>The external finance company, with external financing; else "".</summary>
    public string BillTo { get; init; } = "";
    public bool IsExternalFinancing { get; init; }
    public string LeaseAgreementNumber { get; init; } = "";
    public string TransactionType { get; init; } = "";
    public string TradeInTo { get; init; } = "";
    /// <summary>The billing address (not shown with external financing).</summary>
    public string BillingAddress { get; init; } = "";
    public string Remarks { get; init; } = "";
}

public sealed record CostingSheetVariable(string Name, decimal Cost, string Remarks);

public sealed record CostingSheetTradeIn(string Item, string Manufacturer, string Model, string SerialNo, decimal Amount, string Type, string Remarks);

/// <summary>One step of the costing sheet's history: what, when, who (when MyOrders kept it) and a detail (a number).</summary>
public sealed record CostingSheetStep(string Step, DateTime? At, string By, string Detail);

/// <summary>Further facts MyOrders keeps on the costing sheet.</summary>
public sealed record CostingSheetFacts
{
    public long? ProposalId { get; init; }
    public long? OptionId { get; init; }
    public string OrderNo { get; init; } = "";
    public string InvoiceNo { get; init; } = "";
    public string InvoiceDates { get; init; } = "";
    public string CreditTerms { get; init; } = "";
    public decimal TotalApprovedPrice { get; init; }
    public decimal TotalVariableCost { get; init; }
    public bool IsEpp { get; init; }
    public bool IsNovation { get; init; }
    public bool IsOpoChecked { get; init; }
    public bool IsPoCompleted { get; init; }
    public bool IsReceiptCompleted { get; init; }
    public string ContactName { get; init; } = "";
    public string Phone { get; init; } = "";
    public string Email { get; init; } = "";
    public string Fax { get; init; } = "";
    public string DeliverTo { get; init; } = "";
}
