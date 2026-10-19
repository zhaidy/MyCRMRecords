namespace MyCRMRecords.Domain;

/// <summary>
/// One choice of a list that is a static entity in MyCRM: its code here, its label on the pages, and the labels MyCRM
/// may have for it (the sync matches MyCRM's records by label, never by Id).
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

/// <summary>MyCRM's CustomerType (kept and synced; no page of MyCRM sets it).</summary>
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
