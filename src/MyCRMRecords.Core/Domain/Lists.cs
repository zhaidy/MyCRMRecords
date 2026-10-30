namespace MyCRMRecords.Domain;

/// <summary>
/// One choice of a list that is a static entity in MyCRM: its code here, its label on the pages, and the labels MyCRM
/// may have for it (MyCRM's records are matched by label, never by Id).
/// </summary>
public sealed record Choice(string Code, string Label, params string[] OldLabels)
{
    public IEnumerable<string> Labels => new[] { Label }.Concat(OldLabels);
}

/// <summary>A static list: its choices in their order, and the MyCRM entity it is.</summary>
public sealed class ChoiceList(string oldEntity, params Choice[] choices)
{
    public string OldEntity { get; } = oldEntity;
    public IReadOnlyList<Choice> All { get; } = choices;

    public bool IsKnown(string? code) => All.Any(c => c.Code == code);

    /// <summary>The label of a code ("" for none, the code itself when unknown).</summary>
    public string Label(string? code) => string.IsNullOrEmpty(code) ? "" : All.FirstOrDefault(c => c.Code == code)?.Label ?? code!;

    /// <summary>The code whose label (or one of MyCRM's labels) matches, letters and digits only, case ignored; "" when none.</summary>
    public string FromLabel(string? label)
    {
        var key = Key(label ?? "");
        if (key.Length == 0) return "";
        return All.FirstOrDefault(c => c.Labels.Any(l => Key(l) == key))?.Code ?? "";
    }

    public static string Key(string text) => new string(text.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}

/// <summary>
/// MyCRM's roles (its static entity Role, read from a person's CUser), in MyCRM's order. Codes are MyCRM's identifiers;
/// two labels differ from them (MITSSpecialist is "SOL Specialist").
/// </summary>
public static class CrmRoles
{
    public const string Sales = "Sales";
    public const string Manager = "Manager";
    public const string Head = "Head";
    public const string Gm = "GM";
    public const string Md = "MD";
    public const string Admin = "Admin";
    public const string SalesAdmin = "SalesAdmin";
    public const string Telemarketer = "Telemarketer";
    public const string Contract = "Contract";
    public const string CleTrainer = "CLETrainer";
    public const string ChannelPartner = "ChannelPartner";
    public const string Marketing = "Marketing";
    public const string PpSpecialist = "PPSpecialist";
    public const string PpSpecialistManager = "PPSpecialistManager";
    public const string MdsSpecialist = "MDSSpecialist";
    public const string MdsSpecialistManager = "MDSSpecialistManager";
    public const string MitsSpecialist = "MITSSpecialist";
    public const string MitsSpecialistManager = "MITSSpecialistManager";
    public const string CleTrainerManager = "CLETrainerManager";
    public const string CreditManager = "CreditManager";
    public const string StockAllocator = "StockAllocator";
    public const string ItHelpdesk = "ITHelpdesk";
    public const string Sro = "SRO";
    public const string SpecialistSeniorManager = "SpecialistSeniorManager";
    public const string Csst = "CSST";
    public const string CsstManager = "CSSTManager";
    public const string Credit = "Credit";
    public const string Helpdesk = "Helpdesk";
    public const string ServiceEngineer = "ServiceEngineer";
    public const string Opo = "OPO";
    public const string CreditGm = "CreditGM";

    public static readonly ChoiceList List = new("Role",
        new Choice(Sales, "Sales"), new Choice(Manager, "Manager"), new Choice(Head, "Head"), new Choice(Gm, "GM"), new Choice(Md, "MD"),
        new Choice(Admin, "Admin"), new Choice(SalesAdmin, "SalesAdmin"), new Choice(Telemarketer, "Telemarketer"), new Choice(Contract, "Contract"),
        new Choice(CleTrainer, "CLETrainer"), new Choice(ChannelPartner, "Channel Partner"), new Choice("NRM", "NRM"), new Choice(Marketing, "Marketing"),
        new Choice("MarketingGM", "Marketing GM"), new Choice(PpSpecialist, "PP Specialist"), new Choice(PpSpecialistManager, "PP Specialist Manager"),
        new Choice(MdsSpecialist, "MDS Specialist"), new Choice(MdsSpecialistManager, "MDS Specialist Manager"), new Choice(MitsSpecialist, "SOL Specialist"),
        new Choice(MitsSpecialistManager, "SOL Specialist Manager"), new Choice(CleTrainerManager, "CLETrainerManager"), new Choice(CreditManager, "Credit Manager"),
        new Choice("CSMRequester", "CSMRequester"), new Choice(StockAllocator, "Stock Allocator"), new Choice("Allocator", "Allocator"),
        new Choice("Scheduler", "Scheduler"), new Choice(ItHelpdesk, "ITHelpdesk"), new Choice(Sro, "SRO"),
        new Choice(SpecialistSeniorManager, "Specialist Senior Manager"), new Choice("Recon", "Recon"), new Choice(Csst, "CSST"),
        new Choice(CsstManager, "CSSTManager"), new Choice(Credit, "Credit"), new Choice(Helpdesk, "Helpdesk"), new Choice(ServiceEngineer, "ServiceEngineer"),
        new Choice(Opo, "OPO"), new Choice("AP", "AP"), new Choice("APManager", "AP Manager"), new Choice(CreditGm, "Credit GM"), new Choice("SMOVE", "SMOVE"),
        new Choice("Presales", "Presales"), new Choice("CustomerSupport", "Customer Support"), new Choice("Logistics", "Logistics"),
        new Choice("ServiceDeliveryManager", "Service Delivery Manager"), new Choice("CustomerSuccessExecutive", "Customer Success Executive"));

    public const string Allocator = "Allocator";
    public const string Scheduler = "Scheduler";
    public const string Ap = "AP";
    public const string ApManager = "APManager";
    public const string ServiceDeliveryManager = "ServiceDeliveryManager";

    /// <summary>
    /// The roles that see every costing sheet: MyOrders' ShowAll (Credit Manager, Credit, Credit GM, Stock Allocator,
    /// Allocator, SalesAdmin, AP, AP Manager, MD, Admin, Scheduler and Head). Everyone else sees the costing sheets of the
    /// salespeople in their MyCRM list (MyCRM's GetSub: themselves, and their teams' people for the roles that have one).
    /// </summary>
    public static readonly string[] SeeAllCostingSheets =
        [CreditManager, Credit, CreditGm, StockAllocator, Allocator, SalesAdmin, Ap, ApManager, Md, Admin, Scheduler, Head];

    /// <summary>MyOrders' Service Delivery Managers see only the costing sheets of companies whose name contains "dbs".</summary>
    public const string ServiceDeliveryManagerCompanies = "dbs";

    /// <summary>The roles whose profile has one team (MyCRM's CUser_Sales block).</summary>
    public static readonly string[] OneTeam = [Sales, PpSpecialist, CleTrainer];

    /// <summary>The roles put in the team named after them (MyCRM's CUser_Manager block).</summary>
    public static readonly string[] Managers = [Manager, PpSpecialistManager, MdsSpecialistManager, MitsSpecialistManager, CleTrainerManager];

    /// <summary>MyCRM's IsSalesAdmin: the Users and territory screens.</summary>
    public static readonly string[] SalesAdmins = [Admin, SalesAdmin];

    /// <summary>
    /// The roles that see every company (MyCRM's UserCanAccessCompany: Telemarketer, Admin, SalesAdmin, and ShowAllCompany).
    /// </summary>
    public static readonly string[] SeeAllCompanies =
        [Telemarketer, Admin, SalesAdmin, Md, Csst, CsstManager, MdsSpecialistManager, MitsSpecialistManager, Head, CreditManager, CreditGm];

    public static bool Is(string role, params string[] roles) => roles.Contains(role, StringComparer.Ordinal);
}

/// <summary>"Customer Type" on the pages (MyCRM's CompanyGroup); three are inactive (not offered for new choices).</summary>
public static class CompanyGroups
{
    public static readonly ChoiceList List = new("CompanyGroup",
        new Choice("GMA", "GMA"), new Choice("LMA", "LMA"), new Choice("SME", "SME"), new Choice("LG", "LG"), new Choice("Govt", "GOV"),
        new Choice("JKA", "JKA"), new Choice("MA", "MA"), new Choice("Dealer", "DEA"), new Choice("CA", "CA"), new Choice("GA", "GA"),
        new Choice("DEASME", "SMEDEA"));

    public static readonly string[] Inactive = ["CA", "LG", "LMA"];
}

/// <summary>A company's account type (MyCRM's CompAccountType).</summary>
public static class CompAccountTypes
{
    public static readonly ChoiceList List = new("CompAccountType", new Choice("User", "User"), new Choice("NonUser", "Non-User"));
}

/// <summary>A company's ownership (MyCRM's CompOwnership).</summary>
public static class CompOwnerships
{
    public static readonly ChoiceList List = new("CompOwnership", new Choice("Owned", "Owned"), new Choice("Declared", "Declared"));
}

/// <summary>A company's industry (MyCRM's CompanyIndustry; shown, not edited, as in MyCRM).</summary>
public static class CompanyIndustries
{
    private static Choice C(string label) => new(ChoiceList.Key(label), label);

    public static readonly ChoiceList List = new("CompanyIndustry",
        C("Legal"), C("Banking and financial"), C("Manufacturing"), C("Construction and engineering"), C("Utilities"), C("Telecommunications"),
        C("Education and training"), C("Government departments"), C("Medical"), C("Insurance"), C("Religious and non-profit public service"),
        C("Retail groups, chains, franchises"), C("Agriculture"), C("Apparel"), C("Banking"), C("Biotechnology"), C("Chemicals"), C("Communications"),
        C("Construction"), C("Consulting"), C("Education"), C("Electronics"), C("Energy"), C("Engineering"), C("Entertainment"), C("Environmental"),
        C("Finance"), C("Food & Beverage"), C("Government"), C("Healthcare"), C("Hospitality"), C("Machinery"), C("Media"), C("Not For Profit"),
        C("Other"), C("Recreation"), C("Retail"), C("Shipping"), C("Technology"), C("Transportation"));
}

/// <summary>MyCRM's CustomerType (kept as it is; no page of MyCRM sets it).</summary>
public static class CustomerTypes
{
    public static readonly ChoiceList List = new("CustomerType", new Choice("BDG_ZBA", "BDG - ZBA"), new Choice("BDG_ITonMIF", "BDG - IT on MIF"));
}

/// <summary>Advance's customer type (MyCRM's AdvanceCustomerType), with Advance's type code.</summary>
public static class AdvanceCustomerTypes
{
    public static readonly ChoiceList List = new("AdvanceCustomerType",
        new Choice("Others", "Others"), new Choice("Telecom_Media", "Telecom, Media"), new Choice("Government", "Government"),
        new Choice("GeneralServices", "General Services"), new Choice("FinancialInstitute", "Financial Institute"), new Choice("Construction", "Construction"),
        new Choice("WholesaleRetail", "Wholesale & Retail"), new Choice("Healthcare", "Healthcare"), new Choice("ConsultancyService", "Consultancy Service"),
        new Choice("Education", "Education"), new Choice("Restaurant_FoodCater", "Restaurant, Food Cater"), new Choice("Manufacturing", "Manufacturing"),
        new Choice("OilGas", "Oil & Gas"), new Choice("Agri_Forest_Fisheries", "Agri, Forest, Fisheries"), new Choice("Logistic_Forwarder", "Logistic, Forwarder"),
        new Choice("RealEstate_MCST", "Real Estate, MCST"), new Choice("PublishingBroadcasting", "Publishing Broadcasting"), new Choice("Automotive", "Automotive"),
        new Choice("Infrastructure_Utility", "Infrastructure, Utility"));

    /// <summary>Advance's type code of each (MyCRM's TypeCode).</summary>
    public static readonly IReadOnlyDictionary<string, string> TypeCodes = new Dictionary<string, string>
    {
        ["Others"] = "9OTH", ["Telecom_Media"] = "9TEL", ["Government"] = "9GOV", ["GeneralServices"] = "9GSV", ["FinancialInstitute"] = "9FIN",
        ["Construction"] = "9CON", ["WholesaleRetail"] = "9W&R", ["Healthcare"] = "9HEA", ["ConsultancyService"] = "9PRO", ["Education"] = "9EDU",
        ["Restaurant_FoodCater"] = "9F&B", ["Manufacturing"] = "9MFT", ["OilGas"] = "9O&G", ["Agri_Forest_Fisheries"] = "9AFF", ["Logistic_Forwarder"] = "9LOG",
        ["RealEstate_MCST"] = "9REA", ["PublishingBroadcasting"] = "9PUB", ["Automotive"] = "9AUT", ["Infrastructure_Utility"] = "9INF",
    };
}

/// <summary>An address's type (MyCRM's AddressType).</summary>
public static class AddressTypes
{
    public const string Billing = "Billing";
    public const string Delivery = "Delivery";
    public const string Storage = "Storage";
    public const string Mailing = "Mailing";

    public static readonly ChoiceList List = new("AddressType",
        new Choice(Billing, "Billing"), new Choice(Delivery, "Delivery"), new Choice(Storage, "Storage"), new Choice(Mailing, "Mailing"));
}

/// <summary>A phone number's type (MyCRM's PhoneType).</summary>
public static class PhoneTypes
{
    public static readonly ChoiceList List = new("PhoneType",
        new Choice("Office", "Office"), new Choice("Mobile", "Mobile"), new Choice("Fax", "Fax"), new Choice("Home", "Home"));
}

/// <summary>A company relationship's type (MyCRM's CompanyRelationshipType); every one MyCRM makes is Parent.</summary>
public static class RelationshipTypes
{
    public const string Parent = "Parent";

    public static readonly ChoiceList List = new("CompanyRelationshipType",
        new Choice(Parent, "Parent"), new Choice("Child", "Child"), new Choice("Peer", "Peer"));
}

/// <summary>
/// An activity's action (MyCRM's CompanyActivityAction), in MyCRM's order. The inactive ones are only shown on old
/// activities (MyCRM's Cold Call cloning is not offered).
/// </summary>
public static class ActivityActions
{
    public const string FaceToFace = "FaceToFace";

    public static readonly ChoiceList List = new("CompanyActivityAction",
        new Choice(FaceToFace, "Face to Face"), new Choice("ByPhone", "By Phone"), new Choice("ByEmail", "By Email"), new Choice("Others", "Others"),
        new Choice("VideoConference", "Video Conference"), new Choice("TelemarketingCall", "Telemarketing Call"),
        new Choice("SalesNavigatorInMail", "Sales Navigator InMail"),
        new Choice("AnnualLeave", "Annual Leave"), new Choice("MedicalLeave", "Medical Leave"), new Choice("TeamMeeting", "Team Meeting"),
        new Choice("DivisionMeeting", "Division Meeting"), new Choice("TrainingInternal", "Training Internal"), new Choice("TrainingExternal", "Training External"),
        new Choice("MaternityLeave", "Maternity Leave"), new Choice("PaternityLeave", "Paternity Leave"), new Choice("Reservist", "Reservist"),
        new Choice("KeepInTouch", "Keep In Touch"), new Choice("Telesales", "Telesales"), new Choice("Update", "Update"), new Choice("KIV", "KIV"),
        new Choice("KOT", "KOT"), new Choice("ColdCall", "Cold Call"));

    /// <summary>MyCRM's inactive actions (Is_Active = False): not offered for a new choice.</summary>
    public static readonly string[] Inactive =
        ["AnnualLeave", "MedicalLeave", "TeamMeeting", "DivisionMeeting", "TrainingInternal", "TrainingExternal", "MaternityLeave", "PaternityLeave", "Reservist",
         "KeepInTouch", "Telesales", "Update", "KIV", "KOT", "ColdCall"];
}

/// <summary>An activity's types (MyCRM's ActivityType, many per activity), in MyCRM's order.</summary>
public static class ActivityTypes
{
    public const string FirstAppointment = "FirstAppointment";
    /// <summary>Never offered in MyCRM's activity form (set by MyCRM's own rule).</summary>
    public const string FirstAppointmentAttended = "FirstAppointmentAttended";

    public static readonly ChoiceList List = new("ActivityType",
        new Choice("NewCall", "New Call"), new Choice(FirstAppointment, "First Appointment"), new Choice(FirstAppointmentAttended, "First Appointment Attended"),
        new Choice("FollowUp", "Follow Up"), new Choice("File_MediaTest", "File/Media Test"), new Choice("Demo_POC", "Demo/POC"),
        new Choice("FirstProposalPresented", "First Proposal Presented"), new Choice("Negotiation", "Negotiation"), new Choice("CloseOrder", "Close Order"),
        new Choice("ImplementationCall", "Implementation Call"), new Choice("ComplaintCall", "Complaint Call"), new Choice("CustomerCare", "Customer Care"),
        new Choice("LOT", "LOT"), new Choice("KOT", "KOT"), new Choice("EventRelatedActivities", "Event Related Activities"));
}

/// <summary>MyCRM's CompanyActivityStatus (no screen of MyCRM sets it; kept as it is).</summary>
public static class ActivityStatuses
{
    public static readonly ChoiceList List = new("CompanyActivityStatus",
        new Choice("Pending", "Pending"), new Choice("Cancelled", "Cancelled"), new Choice("Complete", "Complete"));
}

/// <summary>A project's status (MyCRM's ProjectStatus).</summary>
public static class ProjectStatuses
{
    public const string ConceptionAndInitiation = "ConceptionAndInitiation";
    public const string Close = "Close";

    public static readonly ChoiceList List = new("ProjectStatus",
        new Choice(ConceptionAndInitiation, "Conception And Initiation"), new Choice("DefinitionAndPlanning", "Definition And Planning"),
        new Choice("LaunchOrExecution", "Launch Or Execution"), new Choice(Close, "Close"));
}

/// <summary>A project member's role (MyCRM's ProjectRoles), in MyCRM's order.</summary>
public static class ProjectRoles
{
    /// <summary>The project's owner: added when the project is made; needs no temporary territory.</summary>
    public const string ProjectManager = "ProjectManager";
    public const string SalespersonCaretaker = "SalespersonCaretaker";
    public const string SalespersonAccountOwner = "SalespersonAccountOwner";

    public static readonly ChoiceList List = new("ProjectRoles",
        new Choice(ProjectManager, "Project Manager"), new Choice("LogisticCoordinator", "Logistic Coordinator"), new Choice("ServicesSpecialist", "Services Specialist"),
        new Choice("PPSpecialist", "PP Specialist"), new Choice("SolutionSpecialist", "Solution Specialist"), new Choice("TechnicalSpecialist", "TechnicalSpecialist"),
        new Choice("CSSTSpecialist", "CSST Specialist"), new Choice("BillingCoordinator", "Billing Coordinator"), new Choice("Salesperson", "Salesperson"),
        new Choice(SalespersonCaretaker, "Salesperson (Caretaker)"), new Choice(SalespersonAccountOwner, "Salesperson (Account Owner)"),
        new Choice("Telemarketer", "Telemarketer"));
}

/// <summary>A prospect's stage (MyCRM's Stages), shown on a company's Prospects tab (view only).</summary>
public static class ProspectStages
{
    public static readonly ChoiceList List = new("Stages",
        new Choice("Plan", "Plan"), new Choice("Create", "Create"), new Choice("Qualify", "Qualify"), new Choice("Develop", "Develop"), new Choice("Prove", "Prove"),
        new Choice("Negotiation", "Negotiation"), new Choice("Close", "Close - Win"), new Choice("CloseLost", "Close - Lost"), new Choice("Implementation", "Implementation"));
}

/// <summary>A prospect's status (MyCRM's ProspectStatus), shown only.</summary>
public static class ProspectStatuses
{
    public static readonly ChoiceList List = new("ProspectStatus",
        new Choice("InProgress", "InProgress"), new Choice("Lost", "Lost"), new Choice("Won", "Won"), new Choice("PendingSpecialist", "Pending Specialist"),
        new Choice("PendingEDPCode", "Pending EDP Code"), new Choice("PendingManager", "Pending Manager"), new Choice("ManagerRejected", "Manager Rejected"),
        new Choice("PendingHead", "Pending Head"), new Choice("HeadRejected", "Head Rejected"), new Choice("PendingCreditCheck", "Pending Credit Check"),
        new Choice("PendingCreditGMCheck", "Pending Credit GM Check"), new Choice("CreditRejected", "Credit Rejected"), new Choice("PendingInvoicing", "Pending Invoicing"),
        new Choice("Invoiced", "Invoiced"));
}

/// <summary>A prospect's type (MyCRM's ProspectType), shown only.</summary>
public static class ProspectTypes
{
    public static readonly ChoiceList List = new("ProspectType",
        new Choice("C2R", "C2R"), new Choice("K2R", "K2R"), new Choice("NEW", "NEW"), new Choice("O2R", "O2R"), new Choice("OtoR", "OtoR"), new Choice("R2R", "R2R"),
        new Choice("RonO", "RonO"), new Choice("RonR", "RonR"), new Choice("RtoR", "RtoR"), new Choice("T2R", "T2R"), new Choice("W2R", "W2R"), new Choice("X2R", "X2R"));
}
