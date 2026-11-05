namespace MyCRMRecords.Data;

/// <summary>
/// A company's Machines tab: what MyMachines' customer page (CustomerDetail) shows of a customer, section by section, with
/// MyMachines' own queries. Contract machines: the customer's running contracts (not on instalment payment), each with its
/// machines in daily_mif, its BW and COL meter groups and its free copies. Warranty machines: daily_mif's machines under
/// a running warranty (mif-type WAR), with the extended warranty contract of each. Contract expired machines: the
/// machines whose latest contract line has ended (contract_lin, so also machines no longer in daily_mif). Machines without
/// contract: the customer's machines (serial) that no contract line ever had, but for some product groups.
/// </summary>
public sealed record CustomerMachines(
    IReadOnlyList<CustomerContract> Contracts,
    IReadOnlyList<WarrantyMachine> Warranty,
    IReadOnlyList<ExpiredMachine> Expired,
    IReadOnlyList<UncoveredMachine> NoContract)
{
    public static CustomerMachines None => new(Array.Empty<CustomerContract>(), Array.Empty<WarrantyMachine>(), Array.Empty<ExpiredMachine>(), Array.Empty<UncoveredMachine>());
}

/// <summary>A running contract of the customer (MyMachines' "Contract machines"), with its machines, meter groups and free copies.</summary>
public sealed record CustomerContract(
    string ContractNo, DateTime? Start, DateTime? End, string CustomerNo, string Customer,
    IReadOnlyList<ContractMachine> Machines, IReadOnlyList<MeterGroup> Meters, IReadOnlyList<CopyGrant> Grants);

/// <summary>A machine of a contract (daily_mif) with its meter groups (Contract_MGMember), MyMachines' MGroup column.</summary>
public sealed record ContractMachine(string Serial, string Item, string Model, DateTime? Start, DateTime? End, string Groups);

/// <summary>A contract's meter group (Contract_MeterGroup) of meter type BW or COL: its minimum bill and up to three steps.</summary>
public sealed record MeterGroup(string MeterType, string Group, decimal MinBill, IReadOnlyList<MeterStep> Steps);

/// <summary>A step of a meter group as MyMachines labels it (Free print, Step Rate 1…, Thereafter): its copies and rate, either may be blank.</summary>
public sealed record MeterStep(string Label, long? Copies, decimal? Price);

/// <summary>Free copies over a contract's life (contract_grant) of meter type BW or COL.</summary>
public sealed record CopyGrant(string MeterType, long Granted, long Used, long Remaining);

/// <summary>A machine under a running warranty (daily_mif, mif-type WAR), with its extended warranty contract (CMA-0A, 0B, 09, 04) when it has one.</summary>
public sealed record WarrantyMachine(string Serial, string Item, string Model, DateTime? Start, DateTime? End, DateTime? ExtendedStart, DateTime? ExtendedEnd);

/// <summary>A machine whose latest contract line has ended (MyMachines' SQLExpContByBRN): that line's contract, start and end.</summary>
public sealed record ExpiredMachine(string Serial, string Item, string Model, string ContractNo, DateTime? Start, DateTime? End);

/// <summary>A machine of the customer (serial) that no contract line has: its install date and its warranty (sell) start and end.</summary>
public sealed record UncoveredMachine(string Serial, string Item, string Model, DateTime? Installed, DateTime? Start, DateTime? End);

/// <summary>
/// How MyMachines' customer page turns the rows it reads into what it shows: the same for the SQL reader and the tests'
/// tables in memory, which hand it rows with the same names.
/// </summary>
public static class CustomerMachinesRules
{
    /// <summary>The extended warranty contracts' types (MyMachines' ExtendedWarranty blocks: Contract_typ like these).</summary>
    public static readonly string[] ExtendedWarrantyTypes = ["CMA-0A", "CMA-0B", "CMA-09", "CMA-04"];

    /// <summary>The product groups MyMachines leaves out of the machines without contract (item's Prod_group starting with these).</summary>
    public static readonly string[] NotMachines = ["0E", "0F", "0G", "0H", "08", "1"];

    /// <summary>The meter types of the meter groups and free copies shown: black and white, then colour.</summary>
    public static readonly string[] MeterTypes = ["BW", "COL"];

    /// <summary>A meter group's reading as MyMachines shows it: 9999999 or more (no limit) as 0.</summary>
    public static long Reading(long read) => read < 9999999 ? read : 0;

    /// <summary>
    /// MyMachines' MGroup column: the meter groups of the machine's lines, in the order read, joined with "," as its
    /// action does (If(MGroupName = "", "", MGroupName + ",") + the next one).
    /// </summary>
    public static string Groups(IEnumerable<string> groups)
    {
        var name = "";
        foreach (var group in groups) name = (name.Length == 0 ? "" : name + ",") + group;
        return name;
    }

    /// <summary>A meter group's steps as MyMachines' BW / COL blocks show them: the first always, the second and third when they have copies or a rate.</summary>
    public static IReadOnlyList<MeterStep> Steps(long meter1, long meter2, long meter3, decimal price1, decimal price2, decimal price3)
    {
        var steps = new List<MeterStep>
        {
            new(meter1 > 0 && price1 == 0 ? "Free print" : meter1 > 0 && price1 > 0 ? "Step Rate 1" : "Thereafter", meter1 > 0 ? meter1 : (long?)null, price1),
        };
        if (meter2 > 0 || price2 > 0)
            steps.Add(new MeterStep(
                price1 == 0 && meter1 > 0 && meter2 > 0 ? "Step rate 1" : price1 > 0 && meter1 > 0 && meter3 > 0 ? "Step rate 2" : meter2 == 0 && price2 == 0 ? "" : "Thereafter",
                meter2 > 0 ? meter2 : (long?)null, price2 > 0 ? price2 : (decimal?)null));
        if (meter3 > 0 || price3 > 0)
            steps.Add(new MeterStep(price3 > 0 ? "Thereafter" : "", meter3 > 0 ? meter3 : (long?)null, price3 > 0 ? price3 : (decimal?)null));
        return steps;
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The page from the rows read, each list in the order read. <paramref name="contracts"/>: ContractNo, Start, End,
    /// Customer, CustNo. <paramref name="machines"/> (daily_mif of those contracts): Serial, Item, Model, Start, End, CustNo,
    /// ContractNo. <paramref name="groups"/> (their lines' meter groups): ContractNo, Serial, Group. <paramref name="meters"/>:
    /// ContractNo, Group, MeterType, Read1-3, Price1-3, MinBill. <paramref name="grants"/>: ContractNo, MeterType, Granted,
    /// Used. <paramref name="warranty"/>: Serial, Item, Model, Start, End; <paramref name="extended"/>: Serial, Start, End
    /// (a row per serial). <paramref name="expired"/>: Serial, Item, Model, ContractNo, Start, End.
    /// <paramref name="noContract"/>: Serial, Item, Model, Installed, Start, End.
    /// </summary>
    public static CustomerMachines Of(
        IEnumerable<IReadOnlyDictionary<string, object?>> contracts, IEnumerable<IReadOnlyDictionary<string, object?>> machines,
        IEnumerable<IReadOnlyDictionary<string, object?>> groups, IEnumerable<IReadOnlyDictionary<string, object?>> meters,
        IEnumerable<IReadOnlyDictionary<string, object?>> grants, IEnumerable<IReadOnlyDictionary<string, object?>> warranty,
        IEnumerable<IReadOnlyDictionary<string, object?>> extended, IEnumerable<IReadOnlyDictionary<string, object?>> expired,
        IEnumerable<IReadOnlyDictionary<string, object?>> noContract)
    {
        var machineRows = machines.ToList();
        var groupRows = groups.ToList();
        var meterRows = meters.ToList();
        var grantRows = grants.ToList();
        var extendedRows = extended.ToList();
        var list = contracts.Select(c =>
        {
            var number = c.Text("ContractNo");
            var ofContract = machineRows.Where(m => Same(m.Text("ContractNo"), number) && Same(m.Text("CustNo"), c.Text("CustNo")))
                .Select(m => new ContractMachine(m.Text("Serial"), m.Text("Item"), m.Text("Model"), m.Date("Start"), m.Date("End"),
                    Groups(groupRows.Where(g => Same(g.Text("ContractNo"), number) && Same(g.Text("Serial"), m.Text("Serial"))).Select(g => g.Text("Group")))))
                .ToList();
            var meterGroups = MeterTypes.SelectMany(type => meterRows.Where(g => Same(g.Text("ContractNo"), number) && Same(g.Text("MeterType"), type))
                    .Select(g =>
                    {
                        var (m1, m2, m3) = (Reading(g.Long("Read1")), Reading(g.Long("Read2")), Reading(g.Long("Read3")));
                        return new MeterGroup(type, g.Text("Group"), g.Decimal("MinBill"), Steps(m1, m2, m3, g.Decimal("Price1"), g.Decimal("Price2"), g.Decimal("Price3")));
                    }))
                .ToList();
            var copyGrants = MeterTypes.SelectMany(type => grantRows.Where(g => Same(g.Text("ContractNo"), number) && Same(g.Text("MeterType"), type))
                    .Select(g => new CopyGrant(type, g.Long("Granted"), g.Long("Used"), g.Long("Granted") - g.Long("Used"))))
                .ToList();
            return new CustomerContract(number, c.Date("Start"), c.Date("End"), c.Text("CustNo"), c.Text("Customer"), ofContract, meterGroups, copyGrants);
        }).ToList();
        var warrantyList = warranty.Select(w =>
        {
            var ext = extendedRows.FirstOrDefault(x => Same(x.Text("Serial"), w.Text("Serial")));
            return new WarrantyMachine(w.Text("Serial"), w.Text("Item"), w.Text("Model"), w.Date("Start"), w.Date("End"),
                ext is null ? (DateTime?)null : ext.Date("Start"), ext is null ? (DateTime?)null : ext.Date("End"));
        }).ToList();
        var expiredList = expired.Select(x => new ExpiredMachine(x.Text("Serial"), x.Text("Item"), x.Text("Model"), x.Text("ContractNo"), x.Date("Start"), x.Date("End"))).ToList();
        var noContractList = noContract.Select(x => new UncoveredMachine(x.Text("Serial"), x.Text("Item"), x.Text("Model"), x.Date("Installed"), x.Date("Start"), x.Date("End"))).ToList();
        return new CustomerMachines(list, warrantyList, expiredList, noContractList);
    }
}

public sealed partial class SqlAdvanceMachines
{
    public CustomerMachines Customer(string brn, string name, DateTime today)
    {
        brn = (brn ?? "").Trim();
        name = (name ?? "").Trim();
        if (brn.Length == 0 && name.Length == 0) return CustomerMachines.None;
        return Databases.Use(ExternalNames.AdvanceConnector, connection =>
        {
            var t = Tables;
            var c = ExternalTable.Of(connection, t.Contract);
            var cl = ExternalTable.Of(connection, t.ContractLine);
            var ip = ExternalTable.Of(connection, t.ContractIp);
            var cu = ExternalTable.Of(connection, t.Customer);
            var cs = ExternalTable.Of(connection, t.CustomerSupp);
            var m = ExternalTable.Of(connection, t.DailyMif);
            var s = ExternalTable.Of(connection, t.Serial);
            var i = ExternalTable.Of(connection, t.Item);
            var i2 = ExternalTable.Of(connection, t.Item2);
            var g = ExternalTable.Of(connection, t.ContractMeterGroup);
            var gm = ExternalTable.Of(connection, t.ContractMgMember);
            var gr = ExternalTable.Of(connection, t.ContractGrant);
            var byBrn = brn.Length > 0;
            var who = new (string, object?)[] { ("@today", today.Date), ("@brn", brn), ("@name", name) };
            List<Dictionary<string, object?>> Rows(string sql, params (string, object?)[] parameters) => Sql.Rows(connection, null, sql, parameters);
            var empty = new List<Dictionary<string, object?>>();

            // Contract machines (GetContractsByBRN / ByName): the contracts with a line running today, not on instalment payment.
            var contracts = Rows(
                $"SELECT c.{c["Contract_no"]} AS [ContractNo], c.{c["Cntrct_start"]} AS [Start], c.{c["Cntrct_end"]} AS [End], " +
                $"cu.{cu["Name"]} AS [Customer], cu.{cu["Cust_no"]} AS [CustNo] " +
                $"FROM {c} c INNER JOIN {cl} cl ON c.{c["Contract_no"]} = cl.{cl["Contract_no"]} " +
                $"INNER JOIN {cu} cu ON c.{c["Cust_no"]} = cu.{cu["Cust_no"]} " +
                $"INNER JOIN {cs} cs ON c.{c["Cust_no"]} = cs.{cs["Cust_no"]} " +
                $"LEFT JOIN {ip} ip ON c.{c["Contract_no"]} = ip.{ip["contract_no"]} " +
                $"WHERE CAST(cl.{cl["Cntrct_end"]} AS date) >= @today AND CAST(cl.{cl["Cntrct_start"]} AS date) <= @today AND ip.{ip["contract_no"]} IS NULL AND " +
                (byBrn ? $"cs.{cs["Business_reg"]} = @brn" : $"cu.{cu["Name"]} = @name") +
                $" GROUP BY c.{c["Contract_no"]}, c.{c["Cntrct_start"]}, c.{c["Cntrct_end"]}, cu.{cu["Name"]}, cs.{cs["Business_reg"]}, cu.{cu["Cust_no"]} " +
                $"ORDER BY c.{c["Cntrct_end"]}, c.{c["Contract_no"]}, cu.{cu["Cust_no"]}, cs.{cs["Business_reg"]}",
                who);
            var numbers = ("@contracts", (object?)XmlList(contracts.Select(r => r.Text("ContractNo"))));
            var any = contracts.Count > 0;
            // Each contract's machines (GetDailyMifsByCustNo), their meter groups (GetContractLinsByContractNo), its BW and
            // COL meter groups (GetBW, GetCOL) and free copies (GetContractGrantsByContractNo): for all the contracts at once.
            var machines = !any ? empty : Rows(
                $"SELECT m.{m["serial_no"]} AS [Serial], m.{m["item_no"]} AS [Item], m.{m["model"]} AS [Model], m.{m["cntrct_start"]} AS [Start], " +
                $"m.{m["cntrct_end"]} AS [End], m.{m["cust_no"]} AS [CustNo], m.{m["contract_no"]} AS [ContractNo] " +
                $"FROM {m} m WHERE m.{m["contract_no"]} IN {InXml("@contracts")} ORDER BY m.{m["serial_no"]}, m.{m["item_no"]}",
                numbers);
            var groups = !any ? empty : Rows(
                $"SELECT cl.{cl["Contract_no"]} AS [ContractNo], cl.{cl["Serial_no"]} AS [Serial], gm.{gm["MGroup_No"]} AS [Group] " +
                $"FROM {cl} cl LEFT JOIN {gm} gm ON cl.{cl["Line_no"]} = gm.{gm["Line_No"]} AND cl.{cl["Contract_no"]} = gm.{gm["Contract_No"]} " +
                $"WHERE cl.{cl["Contract_no"]} IN {InXml("@contracts")} " +
                $"ORDER BY cl.{cl["Ar_entity"]}, cl.{cl["Contract_no"]}, cl.{cl["Serial_no"]}, cl.{cl["Line_no"]}, gm.{gm["MGroup_No"]}",
                numbers);
            var meters = !any ? empty : Rows(
                $"SELECT g.{g["Contract_No"]} AS [ContractNo], g.{g["MGroup_No"]} AS [Group], g.{g["Meter_Type"]} AS [MeterType], " +
                $"g.{g["Meter_Read_1"]} AS [Read1], g.{g["Meter_Read_2"]} AS [Read2], g.{g["Meter_Read_3"]} AS [Read3], " +
                $"g.{g["Meter_Price_1"]} AS [Price1], g.{g["Meter_Price_2"]} AS [Price2], g.{g["Meter_Price_3"]} AS [Price3], g.{g["Min_Bill"]} AS [MinBill] " +
                $"FROM {g} g WHERE g.{g["Contract_No"]} IN {InXml("@contracts")} AND g.{g["Meter_Type"]} IN (N'BW', N'COL') " +
                $"ORDER BY g.{g["AR_Entity"]}, g.{g["Contract_No"]}, g.{g["MGroup_No"]}",
                numbers);
            var grants = !any ? empty : Rows(
                $"SELECT gr.{gr["Contract_no"]} AS [ContractNo], gr.{gr["MGroup_No"]} AS [MeterType], gr.{gr["granted_copies"]} AS [Granted], gr.{gr["used_copies"]} AS [Used] " +
                $"FROM {gr} gr WHERE gr.{gr["Contract_no"]} IN {InXml("@contracts")} AND gr.{gr["MGroup_No"]} IN (N'BW', N'COL') " +
                $"ORDER BY gr.{gr["Record_type"]}, gr.{gr["Contract_no"]}, gr.{gr["Line_no"]}, gr.{gr["seq_no"]}",
                numbers);

            // Warranty machines (GetDailyMifsByBRN / ByName), and each one's extended warranty (ExtendedWarranty blocks: the first contract line by contract record type).
            var warranty = Rows(
                $"SELECT m.{m["serial_no"]} AS [Serial], m.{m["item_no"]} AS [Item], m.{m["description"]} AS [Model], m.{m["sell_start"]} AS [Start], m.{m["sell_end"]} AS [End] " +
                $"FROM {m} m WHERE CAST(m.{m["sell_end"]} AS date) >= @today AND m.{m["mif_type"]} = N'war' AND " +
                (byBrn ? $"m.{m["BRN"]} = @brn" : $"m.{m["name"]} = @name") +
                $" GROUP BY m.{m["serial_no"]}, m.{m["item_no"]}, m.{m["model"]}, m.{m["sell_end"]}, m.{m["sell_start"]}, m.{m["description"]} " +
                $"ORDER BY m.{m["sell_end"]}, m.{m["serial_no"]}, m.{m["item_no"]}, m.{m["model"]}, m.{m["description"]}",
                who);
            var types = string.Join(" OR ", CustomerMachinesRules.ExtendedWarrantyTypes.Select(x => $"c.{c["Contract_typ"]} LIKE N'{x}%'"));
            var extended = warranty.Count == 0 ? empty : Rows(
                $"SELECT x.[Serial], x.[Start], x.[End] FROM (SELECT cl.{cl["Serial_no"]} AS [Serial], cl.{cl["Cntrct_start"]} AS [Start], cl.{cl["Cntrct_end"]} AS [End], " +
                $"ROW_NUMBER() OVER (PARTITION BY cl.{cl["Serial_no"]} ORDER BY c.{c["Record_type"]}, c.{c["Contract_no"]}, cl.{cl["Line_no"]}) AS [N] " +
                $"FROM {c} c INNER JOIN {cl} cl ON c.{c["Contract_no"]} = cl.{cl["Contract_no"]} " +
                $"WHERE ({types}) AND cl.{cl["Serial_no"]} IN {InXml("@serials")}) x WHERE x.[N] = 1",
                ("@serials", XmlList(warranty.Select(r => r.Text("Serial")))));

            // Contract expired machines (SQLExpContByBRN / ByName, as MyMachines writes it): a machine's contract line that
            // has ended and is its latest (by end) of the contracts not on instalment payment.
            var expired = Rows(
                $"SELECT cl.{cl["Serial_no"]} AS [Serial], i.{i["Description"]} AS [Model], cl.{cl["Item_no"]} AS [Item], " +
                "k.[ContractNo], k.[Start], k.[End] " +
                $"FROM {c} c INNER JOIN {cl} cl ON cl.{cl["Contract_no"]} = c.{c["Contract_no"]} " +
                $"INNER JOIN {cu} cu ON cu.{cu["Cust_no"]} = c.{c["Cust_no"]} " +
                $"INNER JOIN {cs} cs ON cs.{cs["Cust_no"]} = c.{c["Cust_no"]} " +
                $"INNER JOIN {i2} i2 ON i2.{i2["Item_no"]} = cl.{cl["Item_no"]} " +
                $"INNER JOIN {i} i ON i.{i["Item_no"]} = cl.{cl["Item_no"]} " +
                $"INNER JOIN (SELECT l.{cl["Serial_no"]} AS [Serial], l.{cl["Item_no"]} AS [Item], l.{cl["Contract_no"]} AS [ContractNo], " +
                $"l.{cl["Cntrct_start"]} AS [Start], l.{cl["Cntrct_end"]} AS [End], " +
                $"ROW_NUMBER() OVER (PARTITION BY l.{cl["Serial_no"]} ORDER BY l.{cl["Cntrct_end"]} DESC, l.{cl["Contract_no"]} DESC, l.{cl["Line_no"]} DESC) AS [R] " +
                $"FROM {cl} l LEFT JOIN {ip} lip ON lip.{ip["contract_no"]} = l.{cl["Contract_no"]} WHERE lip.{ip["contract_no"]} IS NULL) k " +
                $"ON k.[Serial] = cl.{cl["Serial_no"]} AND k.[Item] = cl.{cl["Item_no"]} AND k.[ContractNo] = cl.{cl["Contract_no"]} AND k.[R] = 1 " +
                $"LEFT JOIN {ip} ip ON ip.{ip["contract_no"]} = c.{c["Contract_no"]} " +
                "WHERE " + (byBrn ? $"cs.{cs["Business_reg"]} = @brn" : $"cu.{cu["Name"]} = @name") +
                $" AND cs.{cs["Business_reg"]} <> N'' AND cl.{cl["Cntrct_end"]} <= @today AND ip.{ip["contract_no"]} IS NULL " +
                $"ORDER BY cl.{cl["Serial_no"]}, cl.{cl["Item_no"]}, k.[ContractNo], c.{c["Cust_no"]}",
                who);

            // Machines without contract (GetCustomersByBRN / ByName): the customer's serials with no contract line at all.
            var notMachines = string.Join(" OR ", CustomerMachinesRules.NotMachines.Select(x => $"i.{i["Prod_group"]} LIKE N'{x}%'"));
            var noContract = Rows(
                $"SELECT s.{s["Serial_no"]} AS [Serial], s.{s["Item_no"]} AS [Item], i.{i["Description"]} AS [Model], s.{s["Install_date"]} AS [Installed], " +
                $"s.{s["Sell_start"]} AS [Start], s.{s["Sell_end"]} AS [End] " +
                $"FROM {cu} cu INNER JOIN {s} s ON cu.{cu["Cust_no"]} = s.{s["Cust_no"]} " +
                $"INNER JOIN {i} i ON s.{s["Item_no"]} = i.{i["Item_no"]} " +
                $"LEFT JOIN {cl} cl ON s.{s["Serial_no"]} = cl.{cl["Serial_no"]} " +
                $"INNER JOIN {cs} cs ON s.{s["Cust_no"]} = cs.{cs["Cust_no"]} " +
                "WHERE " + (byBrn ? $"cs.{cs["Business_reg"]} = @brn" : $"cu.{cu["Name"]} = @name") +
                $" AND cl.{cl["Contract_no"]} IS NULL AND NOT ({notMachines}) " +
                $"ORDER BY s.{s["Install_date"]} DESC, s.{s["Serial_no"]}, s.{s["Item_no"]}",
                who);

            return CustomerMachinesRules.Of(contracts, machines, groups, meters, grants, warranty, extended, expired, noContract);
        });
    }
}
