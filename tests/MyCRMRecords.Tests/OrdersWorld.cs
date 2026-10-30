using Newtonsoft.Json.Linq;

namespace MyCRMRecords.Tests;

/// <summary>
/// MyCRMRecords' costing sheets and request forms, with its data in a temporary folder, a fixed clock (Tuesday 20 October 2026, 09:30) and MyOrders' and
/// MyCRM's tables in memory (<see cref="FakeMyOrders"/>). MyCRM's people: Sam (Sales) and Mia (Manager) in Mia's team, Ken
/// (Sales) and Cara (Sales, whose OutSystems username is not her login) in Ken's team, Hana (Head), Carl (Credit), Sid
/// (Service Delivery Manager, Ken's team) and Dan (MDS Specialist, Mia's team); Olga has no MyCRM role. Rob is an
/// administrator of MyCRMRecords (Web.config). Static entities have Ids no list starts with (11, 12...), as in
/// production they are the database's.
/// </summary>
public sealed class OrdersWorld : IDisposable
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "MyCRMRecordsTests", Guid.NewGuid().ToString("N"));
    public DateTime Now { get; set; } = new(2026, 10, 20, 9, 30, 0);
    public SettingsFile<CrmSettings> SettingsFile { get; }
    public JsonRecordStore Store { get; }
    public FakeMyOrders Data { get; } = new();
    /// <summary>MyFiles' files, in the same OutSystems database as MyOrders' tables.</summary>
    public FakeMyFiles Files { get; }
    public CrmApp D { get; }

    public static readonly ApiUser Admin = new(@"RSP\root", "Rob Root", "rob@example.com", IsListedAdmin: true);
    public static readonly ApiUser Sam = new(@"RSP\sam", "Sam Sales", "sam@example.com");
    public static readonly ApiUser Mia = new(@"RSP\mia", "Mia Manager", "mia@example.com");
    public static readonly ApiUser Ken = new(@"RSP\ken", "Ken Kwan", "ken@example.com");
    public static readonly ApiUser Cara = new(@"RSP\ctan", "Cara Tan", "cara.tan@ricoh.sg");
    public static readonly ApiUser Hana = new(@"RSP\hana", "Hana Head", "hana@example.com");
    public static readonly ApiUser Carl = new(@"RSP\carl", "Carl Credit", "carl@example.com");
    public static readonly ApiUser Sid = new(@"RSP\sid", "Sid Service", "sid@example.com");
    public static readonly ApiUser Dan = new(@"RSP\dan", "Dan MDS", "dan@example.com");
    public static readonly ApiUser Olga = new(@"RSP\olga", "Olga Other", "olga@example.com");

    /// <summary>OutSystems user Ids.</summary>
    public long SamId, MiaId, KenId, CaraId, HanaId, CarlId, SidId, DanId, OlgaId;
    /// <summary>Static records by label → Id.</summary>
    public Dictionary<string, long> Status { get; } = new();
    public Dictionary<string, long> BillType { get; } = new();
    public Dictionary<string, long> VariableName { get; } = new();
    public Dictionary<string, long> FormStatus { get; } = new();
    public Dictionary<string, long> FormType { get; } = new();
    public Dictionary<string, long> CollectionType { get; } = new();
    public Dictionary<string, long> AssetType { get; } = new();
    public long Acme, Dbs, Zen, AcmeContact, AcmeAddress, AcmeBilling, Reference1;

    public OrdersWorld(Action<CrmSettings>? configure = null)
    {
        Directory.CreateDirectory(Folder);
        SettingsFile = new SettingsFile<CrmSettings>(Path.Combine(Folder, "crm-records-settings.json"), new FakeProtector());
        if (configure is not null) SettingsFile.Change(configure);
        Store = new JsonRecordStore(Path.Combine(Folder, "data"));
        Files = new FakeMyFiles(Data);
        // MyCRM's own records are kept in memory with MyCRMRecords' (the company pages are not what these tests look at).
        D = CrmFactory.Create(Store, SettingsFile, _ => throw new InvalidOperationException("No database in this test."),
            Path.Combine(Folder, "scheduled-tasks.json"), () => Now, new FakeEmail(), Store, orders: Data, domain: "RSP", files: Files);
        foreach (var user in new[] { Admin, Sam, Mia, Ken, Cara, Hana, Carl, Sid, Dan, Olga }) D.People.Touch(user.Login, user.DisplayName, user.Email);

        // OutSystems' users: Cara's username is her email address, not her Windows login.
        SamId = Data.User(@"RSP\sam", "Sam Sales", "sam@example.com");
        MiaId = Data.User(@"RSP\mia", "Mia Manager", "mia@example.com");
        KenId = Data.User(@"RSP\ken", "Ken Kwan", "ken@example.com");
        CaraId = Data.User(@"RSP\cara.tan", "Cara Tan", "cara.tan@ricoh.sg");
        HanaId = Data.User(@"RSP\hana", "Hana Head", "hana@example.com");
        CarlId = Data.User(@"RSP\carl", "Carl Credit", "carl@example.com");
        SidId = Data.User(@"RSP\sid", "Sid Service", "sid@example.com");
        DanId = Data.User(@"RSP\dan", "Dan MDS", "dan@example.com");
        OlgaId = Data.User(@"RSP\olga", "Olga Other", "olga@example.com");

        // MyCRM's roles (Ids unlike the list's order), teams and CUser rows.
        var roles = new Dictionary<string, long>();
        var roleId = 31L;
        foreach (var label in new[] { "Sales", "Manager", "Head", "SalesAdmin", "Credit", "Service Delivery Manager", "MDS Specialist" })
            roles[label] = Data.Add(Entities.CrmRole, new { ID = roleId += 3, LABEL = label });
        const long miaTeam = 7, kenTeam = 8;
        void Crm(long user, string role, long? team) => Data.Add(Entities.CrmUser, new { USERID = user, ROLEID = roles[role], TEAMID = team, ISACTIVE = true });
        Crm(SamId, "Sales", miaTeam);
        Crm(MiaId, "Manager", miaTeam);
        Crm(KenId, "Sales", kenTeam);
        Crm(CaraId, "Sales", kenTeam);
        Crm(HanaId, "Head", null);
        Crm(CarlId, "Credit", null);
        Crm(SidId, "Service Delivery Manager", kenTeam);
        Crm(DanId, "MDS Specialist", miaTeam);

        // MyOrders' static entities.
        var id = 11L;
        foreach (var label in new[] { "Draft", "Pending Manager", "Pending Head", "Pending Credit Check", "Pending Invoicing", "Invoiced" })
            Status[label] = Data.Add(Entities.CostingSheetStatus, new { ID = id++, LABEL = label, ORDER = (int)id });
        foreach (var label in new[] { "Internal Financing", "External Financing", "Outright Sale" })
            BillType[label] = Data.Add(Entities.BillType, new { ID = id++, LABEL = label });
        foreach (var label in new[] { "Rollover / Refinancing", "R2R Trade In", "Accessories", "O2R Trade In", "Others" })
            VariableName[label] = Data.Add(Entities.VariableName, new { ID = id++, LABEL = label });
        var order = 0;
        foreach (var label in new[] { "Draft", "Pending Manager", "Pending Head", "Pending Credit Check", "Pending Allocation", "Backordered", "Pending Invoicing",
            "Manager Rejected", "Head Rejected", "Credit Rejected", "Pending Specialist", "Pending EDP Code", "Pending Recon Allocation", "Stock Allocator Rejected", "Invoiced" })
            FormStatus[label] = Data.Add(Entities.RequestFormStatus, new { ID = 200L + 3 * ++order, LABEL = label, ORDER = order });
        order = 0;
        foreach (var label in new[] { "Delivery Order", "Collection", "Demo", "Loan", "Trial" })
            FormType[label] = Data.Add(Entities.RequestFormType, new { ID = 300L + 7 * ++order, LABEL = label, ORDER = order });
        foreach (var label in new[] { "Exchange", "Trade-In", "Termination" })
            CollectionType[label] = Data.Add(Entities.CollectionType, new { ID = 400L + CollectionType.Count * 5, LABEL = label });
        foreach (var label in new[] { "Hardware", "Software", "Others" })
            AssetType[label] = Data.Add(Entities.AssetType, new { ID = 450L + AssetType.Count * 5, LABEL = label });
        Data.Add(Entities.Brand, new { ID = 480L, LABEL = "RICOH" });
        Data.Add(Entities.TradeInTo, new { ID = 41L, LABEL = "Ricoh" });
        Data.Add(Entities.TransactionType, new { ID = 43L, LABEL = "New" });
        Data.Add(Entities.ExternalFinanceCompany, new { ID = 45L, NAME = "Orix Leasing", ISACTIVE = true });
        Data.Add(Entities.CompanyGroup, new { ID = 47L, LABEL = "SME" });

        // MyCRM's companies, contacts and addresses.
        Acme = Data.Add(Entities.Company, new { COMPANY = "Acme Pte Ltd", COMPANYGROUPID = 47L });
        Dbs = Data.Add(Entities.Company, new { COMPANY = "DBS Bank Ltd", COMPANYGROUPID = 47L });
        Zen = Data.Add(Entities.Company, new { COMPANY = "Zen Trading", COMPANYGROUPID = (long?)null });
        AcmeContact = Data.Add(Entities.Contacts, new { FIRSTNAME = "Alice", MIDDLENAME = "", LASTNAME = "Lim" });
        AcmeAddress = Data.Add(Entities.Address, new { BLOCK = "10", STREET = "Anson Road", UNITNUMBER = "#20-01", BUILDINGNAME = "International Plaza", POSTALCODE = "079903", CITY = "", STATEPROVINCE = "", COUNTRY = "" });
        AcmeBilling = Data.Add(Entities.Address, new { BLOCK = "", STREET = "1 Raffles Place", UNITNUMBER = "", BUILDINGNAME = "", POSTALCODE = "048616", CITY = "", STATEPROVINCE = "", COUNTRY = "Singapore" });
        Reference1 = Data.Add(Entities.Reference, new { REFERENCE = "Q2026-0101" });
    }

    /// <summary>A costing sheet of MyOrders (active unless said), returning its Id.</summary>
    public long Sheet(long salesman, long company, string status = "Draft", DateTime? created = null, decimal achieved = 0m, string invoiceNo = "",
        long? mds = null, long? pp = null, long? mits = null, bool active = true, DateTime? headApproved = null, DateTime? invoiceDate = null, long? id = null) =>
        Data.Add(Entities.CostingSheet, new
        {
            ID = id,
            ISACTIVE = active,
            SALESMANID = salesman,
            COMPANYID = company,
            CONTACTID = AcmeContact,
            ADDRESSID = AcmeAddress,
            COSTINGSHEETSTATUSID = Status[status],
            REFERENCEID = Reference1,
            CREATEDAT = created ?? Now.AddDays(-30),
            CREATEDBY = salesman,
            UPDATEDAT = created ?? Now.AddDays(-30),
            TOTALACHIEVEDPRICE = achieved,
            INVOICENO = invoiceNo,
            ORDERNO = "",
            ISOPOCHECKED = false,
            MDSSPECIALISTID = mds,
            PPSPECIALISTID = pp,
            MITSSPECIALISTID = mits,
            HEADAPPROVEDAT = headApproved ?? OutSystemsValues.NullDate,
            MININVOICEDATE = invoiceDate ?? OutSystemsValues.NullDate,
            PRINTEDAT = OutSystemsValues.NullDate,
        });

    /// <summary>A request form of MyOrders (active unless said), returning its Id.</summary>
    public long Form(long salesman, long company, string status = "Draft", string type = "Delivery Order", DateTime? created = null, long? mds = null,
        long? pp = null, long? mits = null, bool active = true, long? costingSheet = null, DateTime? printed = null, DateTime? recalled = null, long? id = null) =>
        Data.Add(Entities.RequestForm, new
        {
            ID = id,
            ISACTIVE = active,
            SALESMANID = salesman,
            CREATEDBY = salesman,
            COMPANYID = company,
            CONTACTID = AcmeContact,
            ADDRESSID = AcmeAddress,
            REQUESTFORMSTATUSID = FormStatus[status],
            REQUESTFORMTYPEID = FormType[type],
            REFERENCEID = Reference1,
            CREATEDAT = created ?? Now.AddDays(-20),
            UPDATEDAT = created ?? Now.AddDays(-20),
            PRINTEDAT = printed ?? OutSystemsValues.NullDate,
            RECALLAT = recalled ?? OutSystemsValues.NullDate,
            COSTINGSHEETID = costingSheet,
            MDSSPECIALISTID = mds,
            PPSPECIALISTID = pp,
            MITSSPECIALISTID = mits,
            TOTALSTP = 0m,
            ISOPOCHECKED = false,
            CUSTOMERNO = "C-100",
            DECLARED = false,
            AGILITYRUNNINGNUMBER = 1,
        });

    public Actor Actor(ApiUser user) => user.ToActor(D.People, D.OrdersAccess);

    public ApiResponse Call(string path, ApiUser? user = null, Dictionary<string, string>? query = null) =>
        new OrdersApi(D).Handle("GET", path, "", query ?? new Dictionary<string, string>(), user ?? Admin);

    public JObject Json(ApiResponse response) => JObject.Parse(response.Json);

    public void Dispose()
    {
        try { Directory.Delete(Folder, recursive: true); }
        catch (IOException) { }
    }
}
