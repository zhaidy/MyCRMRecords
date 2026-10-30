using Newtonsoft.Json.Linq;

namespace MyCRMRecords.Tests;

/// <summary>
/// The Machines pages: Advance's machines, read only, seen by MyCRMRecords' company rule (by the company's BRN) plus
/// MyMachines' temporary access, alike for the list, a company's Machines tab and a machine's page; and the Machine
/// access page, which changes MyMachines' TempAccess only when switched on and never deletes there.
/// </summary>
public sealed class MachinesTests
{
    private sealed record Setup(TestWorld World, Company Tan, Company Lim, Company Zen);

    /// <summary>
    /// Tan (Sam's territory North): a machine on contract, under warranty, on T&amp;M, expired and with nothing; Lim (Ken's
    /// South): one on contract and one whose warranty ended; Zen Trading (no territory): one on contract. Today is 17 Oct 2026.
    /// </summary>
    private static Setup World()
    {
        var world = new TestWorld();
        var tan = world.Company("Tan", "T1000ABC");
        var lim = world.Company("Lim", "L2000XYZ");
        var zen = world.Company("Zen Trading", "Z9000QRS");
        world.Territory("North", 1, TestWorld.Sam, tan.Id);
        world.Territory("South", 2, TestWorld.Ken, lim.Id);
        Fill(world.Machines);
        return new Setup(world, tan, lim, zen);
    }

    /// <summary>Advance's machines of the tests (also read from SQL Server by <see cref="OrdersSqlServerTests"/>).</summary>
    public static void Fill(FakeAdvanceMachines m)
    {
        m.Machine("3100R0001", "T1000ABC", "TAN PTE LTD", "CMA", contractNo: "CT-1", start: new DateTime(2025, 10, 1), end: new DateTime(2028, 9, 30),
            sellStart: new DateTime(2025, 10, 3), sellEnd: new DateTime(2026, 10, 2), finance: "SALIL", custNo: "C1");
        m.Machine("3100R0002", "T1000ABC", "TAN PTE LTD", "WAR", sellStart: new DateTime(2026, 3, 15), sellEnd: new DateTime(2027, 3, 14), finance: "SALCAS", custNo: "C1");
        m.Machine("3100R0003", "T1000ABC", "TAN PTE LTD", "TM", contractRef: "CT-OLD", invoiceRef: "35120212", serviceRef: "SR-9", finance: "SALDLS", custNo: "C1");
        m.Machine("3100R0004", "T1000ABC", "TAN PTE LTD", "CMA", contractNo: "CT-4", start: new DateTime(2023, 9, 1), end: new DateTime(2026, 8, 31), finance: "SALEL", custNo: "C1");
        m.Machine("3100R0005", "T1000ABC", "TAN PTE LTD", "", custNo: "C1");
        m.Machine("4100L0001", "L2000XYZ", "LIM PTE LTD", "MTR", contractNo: "CT-L", start: new DateTime(2026, 1, 1), end: new DateTime(2029, 1, 1), custNo: "C2");
        m.Machine("4100L0002", "L2000XYZ", "LIM PTE LTD", "WAR", sellStart: new DateTime(2025, 1, 1), sellEnd: new DateTime(2026, 1, 1), custNo: "C2");
        m.Machine("5100Z0001", "Z9000QRS", "Zen Trading", "MTR", contractNo: "CT-Z", start: new DateTime(2026, 1, 1), end: new DateTime(2029, 1, 1), custNo: "C3");
        // History of the first machine: copies billed, service calls done (with readings), a toner order, older ones not shown.
        m.Add("meter_card", new { serial_no = "3100R0001", Contract_no = "CT-1", Invoice_no = 501L, Invoice_date = new DateTime(2026, 9, 20), Copies = 4000L, Meter_Type = "B1" });
        m.Add("meter_card", new { serial_no = "3100R0001", Contract_no = "CT-1", Invoice_no = 501L, Invoice_date = new DateTime(2026, 9, 20), Copies = 900L, Meter_Type = "C1" });
        m.Add("meter_card", new { serial_no = "3100R0001", Contract_no = "CT-0", Invoice_no = 502L, Invoice_date = new DateTime(2026, 9, 20), Copies = 7L, Meter_Type = "B1" });
        m.Add("meter_card", new { serial_no = "3100R0001", Contract_no = "CT-1", Invoice_no = 400L, Invoice_date = new DateTime(2025, 1, 20), Copies = 1L, Meter_Type = "B1" });
        // Copies of other machines: Tan's warranty machine (no contract) and Lim's, for a company's copies per month.
        m.Add("meter_card", new { serial_no = "3100R0002", Contract_no = "", Invoice_no = 601L, Invoice_date = new DateTime(2026, 9, 25), Copies = 1000L, Meter_Type = "B1" });
        m.Add("meter_card", new { serial_no = "3100R0002", Contract_no = "", Invoice_no = 602L, Invoice_date = new DateTime(2026, 6, 25), Copies = 300L, Meter_Type = "B2" });
        m.Add("meter_card", new { serial_no = "4100L0001", Contract_no = "CT-L", Invoice_no = 701L, Invoice_date = new DateTime(2026, 9, 25), Copies = 5555L, Meter_Type = "B1" });
        m.Add("Service", new { Service_no = "SR-1", Serial_no = "3100R0001", Cust_no = "C1", Contract_no = "CT-1", Service_type = "FC", SO_status = "F90", Tech_code = "T7",
            Problem_code = "JAM", Entry_date = new DateTime(2026, 10, 1), Entry_time = "09:12", Complete_dt = new DateTime(2026, 10, 1), Complete_tm = "11:40", Invoice_no = 0L, Invoice_date = (DateTime?)null });
        m.Add("Service", new { Service_no = "SR-2", Serial_no = "3100R0001", Cust_no = "C1", Contract_no = "CT-1", Service_type = "XX", SO_status = "F10", Tech_code = "T7",
            Problem_code = "", Entry_date = new DateTime(2026, 10, 2), Entry_time = "", Complete_dt = (DateTime?)null, Complete_tm = "", Invoice_no = 0L, Invoice_date = (DateTime?)null });
        m.Add("Service", new { Service_no = "TN-1", Serial_no = "3100R0001", Cust_no = "C1", Contract_no = "CT-1", Service_type = "SUP", SO_status = "P90", Tech_code = "",
            Problem_code = "", Entry_date = new DateTime(2026, 8, 5), Entry_time = "", Complete_dt = (DateTime?)null, Complete_tm = "", Invoice_no = 3510L, Invoice_date = new DateTime(2026, 8, 7) });
        m.Add("SMeter_History", new { Service_No = "SR-1", Serial_No = "3100R0001", Meter_Type = "B1", Meter_Reading = 41882L });
        m.Add("SMeter_History", new { Service_No = "SR-1", Serial_No = "3100R0001", Meter_Type = "C1", Meter_Reading = 11204L });
        m.Add("daily_TM", new { trans_date = new DateTime(2026, 10, 16), item_no = "418283", serial_no = "3100R0003", Contract_no = "CT-OLD", Cntrct_start = new DateTime(2021, 2, 1),
            Cntrct_end = new DateTime(2024, 1, 31), Meter_Type = "B1", Min_Bill = 120m, MinCopies = 3000L, MinRate = 0.012m, Therafter = 0.011m });
        m.Add("daily_TM", new { trans_date = new DateTime(2026, 10, 16), item_no = "418283", serial_no = "3100R0003", Contract_no = "CT-OLD", Cntrct_start = new DateTime(2021, 2, 1),
            Cntrct_end = new DateTime(2024, 1, 31), Meter_Type = "C1", Min_Bill = 80m, MinCopies = 500L, MinRate = 0.06m, Therafter = 0.055m });
        m.Add("customer", new { Cust_no = "C1", Name = "TAN PTE LTD", Address = "18 Tan Street", City = "Singapore", Zip_code = "123456", Telephone = "61234567", Contact = "Mei" });
    }

    private static ApiResponse Call(TestWorld world, ApiUser user, string path = "machines", Dictionary<string, string>? query = null, string method = "GET", object? body = null) =>
        new MachinesApi(world.D).Handle(method, path, body is null ? "" : MyCRMRecords.Json.Serialize(body), query ?? new Dictionary<string, string>(), user);

    private static string[] Serials(TestWorld world, ApiUser user, params (string, string)[] query)
    {
        var answer = Call(world, user, query: query.ToDictionary(q => q.Item1, q => q.Item2));
        Assert.Equal(200, answer.Status);
        return JObject.Parse(answer.Json)["rows"]!.Select(r => (string)r["serial"]!).ToArray();
    }

    [Fact]
    public void Each_person_sees_the_machines_of_the_companies_they_may_open()
    {
        var s = World();
        using var world = s.World;
        Assert.Equal(new[] { "3100R0001", "3100R0002", "3100R0003", "3100R0004", "3100R0005" }, Serials(world, TestWorld.Sam));
        Assert.Equal(new[] { "3100R0001", "3100R0002", "3100R0003", "3100R0004", "3100R0005" }, Serials(world, TestWorld.Mia));
        Assert.Equal(new[] { "4100L0001", "4100L0002" }, Serials(world, TestWorld.Ken));
        Assert.Empty(Serials(world, TestWorld.Olga));
        var row = JObject.Parse(Call(world, TestWorld.Sam).Json)["rows"]![0]!;
        Assert.Equal("North", (string)row["via"]!);
        Assert.Equal(s.Tan.Id, (long)row["companyId"]!);
        Assert.Equal("Contract", (string)row["coverage"]!);
        Assert.Equal("Internal lease", (string)row["finance"]!);
        // The roles that open every company see every machine, once they search for one.
        var dina = JObject.Parse(Call(world, TestWorld.Dina).Json);
        Assert.True((bool)dina["needsSearch"]!);
        Assert.Empty(dina["rows"]!);
        Assert.Equal(new[] { "4100L0001", "4100L0002", "3100R0001", "3100R0002", "3100R0003", "3100R0004", "3100R0005", "5100Z0001" },
            Serials(world, TestWorld.Dina, ("q", "100")));
        Assert.Equal("All companies", (string)JObject.Parse(Call(world, TestWorld.Dina, query: new() { ["q"] = "5100Z" }).Json)["rows"]![0]!["via"]!);
        Assert.Equal(new[] { "5100Z0001" }, Serials(world, TestWorld.Dina, ("q", "zen tr")));
        Assert.Equal(new[] { "4100L0001", "4100L0002" }, Serials(world, TestWorld.Admin, ("q", "L2000")));
        // A search narrows a person's own machines only.
        Assert.Equal(new[] { "3100R0004" }, Serials(world, TestWorld.Sam, ("q", "R0004")));
        Assert.Empty(Serials(world, TestWorld.Sam, ("q", "4100L")));
    }

    [Fact]
    public void Coverage_and_finance_type_filter_the_list()
    {
        var s = World();
        using var world = s.World;
        Assert.Equal(new[] { "3100R0001" }, Serials(world, TestWorld.Sam, ("coverage", "Contract")));
        Assert.Equal(new[] { "3100R0002" }, Serials(world, TestWorld.Sam, ("coverage", "warranty")));
        Assert.Equal(new[] { "3100R0003" }, Serials(world, TestWorld.Sam, ("coverage", "T&M")));
        Assert.Equal(new[] { "3100R0004" }, Serials(world, TestWorld.Sam, ("coverage", "Expired")));
        Assert.Equal(new[] { "3100R0005" }, Serials(world, TestWorld.Sam, ("coverage", "No contract")));
        // A warranty that ended is expired too.
        Assert.Equal(new[] { "4100L0002" }, Serials(world, TestWorld.Ken, ("coverage", "Expired")));
        Assert.Equal(new[] { "3100R0001" }, Serials(world, TestWorld.Sam, ("finance", "SALIL")));
        Assert.Equal(new[] { "3100R0003" }, Serials(world, TestWorld.Sam, ("finance", "dealer")));
        Assert.Equal(new[] { "3100R0005" }, Serials(world, TestWorld.Sam, ("finance", "none")));
    }

    [Fact]
    public void Temporary_access_in_MyMachines_adds_a_customer_by_BRN_or_by_name()
    {
        var s = World();
        using var world = s.World;
        world.MachineAccess.Add(TestWorld.Olga.Login, "Olga Other", TestWorld.Olga.Email, "Someone else", "Z9000QRS", new DateTime(2026, 10, 1));
        Assert.Equal(new[] { "5100Z0001" }, Serials(world, TestWorld.Olga));
        Assert.Equal("Temporary", (string)JObject.Parse(Call(world, TestWorld.Olga).Json)["rows"]![0]!["via"]!);
        Assert.Equal(200, Call(world, TestWorld.Olga, "machines/detail", new() { ["serial"] = "5100Z0001" }).Status);
        Assert.Equal(200, Call(world, TestWorld.Olga, query: new() { ["company"] = s.Zen.Id.ToString() }).Status);
        // By Advance's customer name, with no BRN; added to Sam's own.
        world.MachineAccess.Add(TestWorld.Sam.Login, "Sam Sales", TestWorld.Sam.Email, "LIM PTE LTD", "", new DateTime(2026, 10, 2));
        Assert.Contains("4100L0001", Serials(world, TestWorld.Sam));
        Assert.Contains("3100R0001", Serials(world, TestWorld.Sam));
        // Removed rows (marked "X:") give nothing.
        world.MachineAccess.CanWrite = true;
        foreach (var row in world.MachineAccess.List()) world.MachineAccess.Remove(row.Id, "test");
        Assert.Empty(Serials(world, TestWorld.Olga));
        Assert.DoesNotContain("4100L0001", Serials(world, TestWorld.Sam));
    }

    [Fact]
    public void A_company_s_Machines_tab_shows_MyCRMs_active_machines_or_all_of_them()
    {
        var s = World();
        using var world = s.World;
        JObject Tab(ApiUser user, Company company, bool all = false)
        {
            var answer = Call(world, user, query: all ? new() { ["company"] = company.Id.ToString(), ["all"] = "1" } : new() { ["company"] = company.Id.ToString() });
            Assert.Equal(200, answer.Status);
            return JObject.Parse(answer.Json);
        }
        // MyCRM's rule: the contract or the warranty ends after today.
        var active = Tab(TestWorld.Sam, s.Tan);
        Assert.Equal(new[] { "3100R0001", "3100R0002" }, active["rows"]!.Select(r => (string)r["serial"]!));
        Assert.Equal(2, (int)active["active"]!);
        var all = Tab(TestWorld.Sam, s.Tan, all: true);
        Assert.Equal(5, (int)all["total"]!);
        Assert.Equal(2, (int)all["active"]!);
        Assert.Equal(403, Call(world, TestWorld.Sam, query: new() { ["company"] = s.Lim.Id.ToString() }).Status);
        Assert.Equal(new[] { "4100L0001" }, Tab(TestWorld.Dina, s.Lim)["rows"]!.Select(r => (string)r["serial"]!));
        Assert.Equal(403, Call(world, TestWorld.Olga, query: new() { ["company"] = s.Zen.Id.ToString() }).Status);
    }

    [Fact]
    public void A_company_s_copies_per_month_add_up_its_machines_meters()
    {
        var s = World();
        using var world = s.World;
        var answer = Call(world, TestWorld.Sam, "machines/copies", new() { ["company"] = s.Tan.Id.ToString() });
        Assert.Equal(200, answer.Status);
        var d = JObject.Parse(answer.Json);
        Assert.Equal(5, (int)d["machines"]!);
        // The last 12 months, oldest first, a month without invoices as 0 (today is 17 Oct 2026).
        var months = d["months"]!.ToArray();
        Assert.Equal(12, months.Length);
        Assert.Equal((2025, 11), ((int)months[0]["year"]!, (int)months[0]["month"]!));
        Assert.Equal((2026, 10), ((int)months[11]["year"]!, (int)months[11]["month"]!));
        // September: 3100R0001's contract invoice (not CT-0's) and 3100R0002's (no contract: every invoice); not Lim's.
        var september = months[10];
        Assert.Equal(5000, (long)september["blackWhite"]!);
        Assert.Equal(900, (long)september["colour"]!);
        Assert.Equal(300, (long)months[7]["blackWhite"]!);
        Assert.Equal(0, (long)months[0]["blackWhite"]!);
        // The company rule, as for its Machines tab.
        Assert.Equal(403, Call(world, TestWorld.Sam, "machines/copies", new() { ["company"] = s.Lim.Id.ToString() }).Status);
        Assert.Equal(5555, (long)JObject.Parse(Call(world, TestWorld.Dina, "machines/copies", new() { ["company"] = s.Lim.Id.ToString() }).Json)["months"]![10]!["blackWhite"]!);
    }

    [Fact]
    public void Machines_are_listed_for_their_copies_as_XML_whatever_they_hold()
    {
        Assert.Equal("<l><m s=\"A&amp;1\" c=\"&quot;C&lt;2&gt;\"/><m s=\"B\" c=\"\"/></l>",
            SqlAdvanceMachines.XmlMachines(new[] { new MeterMachine(" A&1 ", "\"C<2>"), new MeterMachine("B", ""), new MeterMachine(" ", "X") }));
    }

    [Fact]
    public void A_machine_s_page_shows_its_history_only_to_who_sees_it()
    {
        var s = World();
        using var world = s.World;
        var answer = Call(world, TestWorld.Sam, "machines/detail", new() { ["serial"] = "3100R0001" });
        Assert.Equal(200, answer.Status);
        var d = JObject.Parse(answer.Json);
        Assert.Equal("Level 3", (string)d["location"]!);
        // The contract's invoices in the last 12 months, the most copies of each meter type.
        var meters = d["meters"]!.ToArray();
        Assert.Single(meters);
        Assert.Equal(4000, (long)meters[0]["blackWhite"]!);
        Assert.Equal(900, (long)meters[0]["colour"]!);
        // Service calls done (F90) with their readings; toner orders (P90).
        var service = d["services"]!.Single();
        Assert.Equal("SR-1", (string)service["serviceNo"]!);
        Assert.Equal("EM", (string)service["group"]!);
        Assert.Equal(41882, (long)service["blackWhite"]!);
        Assert.Equal("TN-1", (string)d["toner"]!.Single()["serviceNo"]!);
        Assert.Equal(JTokenType.Null, d["lastContract"]!.Type);
        // A T&M machine's last contract: its rates per meter type.
        var tm = JObject.Parse(Call(world, TestWorld.Sam, "machines/detail", new() { ["serial"] = "3100R0003" }).Json);
        Assert.Equal("CT-OLD", (string)tm["lastContract"]!["contractNo"]!);
        Assert.Equal(new[] { "B1", "C1" }, tm["lastContract"]!["rates"]!.Select(r => (string)r["meterType"]!));
        Assert.Equal(120m, (decimal)tm["lastContract"]!["rates"]![0]!["minBill"]!);
        Assert.Equal(403, Call(world, TestWorld.Sam, "machines/detail", new() { ["serial"] = "4100L0001" }).Status);
        Assert.Equal(404, Call(world, TestWorld.Sam, "machines/detail", new() { ["serial"] = "NOPE" }).Status);
        Assert.Equal(200, Call(world, TestWorld.Dina, "machines/detail", new() { ["serial"] = "4100L0001" }).Status);
    }

    [Fact]
    public void Temporary_access_is_given_and_removed_by_Sales_Admins_only_when_switched_on()
    {
        var s = World();
        using var world = s.World;
        var access = world.MachineAccess;
        access.Tables.User(TestWorld.Olga.Login, "Olga Other", TestWorld.Olga.Email);
        Assert.Equal(403, Call(world, TestWorld.Sam, "machineaccess").Status);
        var list = JObject.Parse(Call(world, TestWorld.SalesAdmin, "machineaccess").Json);
        Assert.False((bool)list["canWrite"]!);
        Assert.Empty(list["rows"]!);
        // Off: refused, nothing written.
        Assert.Equal(409, Call(world, TestWorld.SalesAdmin, "machineaccess", method: "POST", body: new { login = TestWorld.Olga.Login, companyId = s.Zen.Id }).Status);
        Assert.Empty(access.List());
        access.CanWrite = true;
        var given = Call(world, TestWorld.SalesAdmin, "machineaccess", method: "POST", body: new { login = TestWorld.Olga.Login, companyId = s.Zen.Id });
        Assert.Equal(200, given.Status);
        var row = access.List().Single();
        Assert.Equal("Zen Trading", row.CustomerName);
        Assert.Equal("Z9000QRS", row.Brn);
        Assert.Equal(new[] { "5100Z0001" }, Serials(world, TestWorld.Olga));
        // Once only; a company without a BRN cannot be given.
        Assert.Equal(400, Call(world, TestWorld.SalesAdmin, "machineaccess", method: "POST", body: new { login = TestWorld.Olga.Login, companyId = s.Zen.Id }).Status);
        var noBrn = world.Company("Walk-in");
        Assert.Equal(400, Call(world, TestWorld.SalesAdmin, "machineaccess", method: "POST", body: new { login = TestWorld.Olga.Login, companyId = noBrn.Id }).Status);
        Assert.Equal(403, Call(world, TestWorld.Sam, "machineaccess", method: "POST", body: new { login = TestWorld.Olga.Login, companyId = s.Zen.Id }).Status);
        // Removed: the row is kept, marked, and gives nothing.
        Assert.Equal(200, Call(world, TestWorld.SalesAdmin, $"machineaccess/{row.Id}/remove", method: "POST").Status);
        Assert.Empty(access.List());
        Assert.Equal("X:Z9000QRS", access.Tables.Tables[Entities.TempAccess].Single().Text("BRN"));
        Assert.Empty(Serials(world, TestWorld.Olga));
        Assert.Equal(404, Call(world, TestWorld.SalesAdmin, $"machineaccess/{row.Id}/remove", method: "POST").Status);
    }

    [Fact]
    public void Without_AdvanceConnector_the_pages_say_so()
    {
        using var world = new TestWorld();
        world.Machines.Available = false;
        Assert.Equal(503, Call(world, TestWorld.Sam).Status);
        Assert.Equal(503, Call(world, TestWorld.Sam, "machines/detail", new() { ["serial"] = "X" }).Status);
    }

    [Fact]
    public void Coverage_is_worked_out_as_MyMachines_sorts_its_machines()
    {
        var today = new DateTime(2026, 10, 17);
        Assert.Equal(Coverages.Contract, Coverages.Of("CMA", new DateTime(2026, 1, 1), new DateTime(2026, 10, 17), null, today));
        Assert.Equal(Coverages.Contract, Coverages.Of("MTR ", new DateTime(2026, 1, 1), new DateTime(2027, 1, 1), null, today));
        Assert.Equal(Coverages.None, Coverages.Of("CMA", new DateTime(2026, 11, 1), new DateTime(2027, 1, 1), null, today));
        Assert.Equal(Coverages.Warranty, Coverages.Of("WAR", null, null, today, today));
        Assert.Equal(Coverages.Expired, Coverages.Of("WAR", null, null, today.AddDays(-1), today));
        Assert.Equal(Coverages.TimeAndMaterials, Coverages.Of("tm", null, new DateTime(2020, 1, 1), null, today));
        Assert.Equal(Coverages.Expired, Coverages.Of("CMA", new DateTime(2020, 1, 1), new DateTime(2026, 10, 16), null, today));
        Assert.Equal(Coverages.None, Coverages.Of("", null, null, null, today));
        // MyCRM's Machines tab: ends after today.
        Assert.False(Coverages.IsActive(today, today, today));
        Assert.True(Coverages.IsActive(null, today.AddDays(1), today));
        Assert.Equal("Internal lease", FinanceTypes.NameOf("salil"));
        Assert.Equal("", FinanceTypes.NameOf("SALDLR"));
        Assert.Equal("SALEL", FinanceTypes.Of("External lease"));
        Assert.Equal(Coverages.TimeAndMaterials, Coverages.Of("T&M"));
        Assert.Equal(Coverages.None, Coverages.Of("no contract"));
        Assert.Equal("X:ABC", TempAccessRules.Mark("ABC", 0));
        Assert.Equal("X:AB", TempAccessRules.Mark("ABC", 4));
    }
}
