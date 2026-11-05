using System.Globalization;
using System.Text;

namespace MyCRMRecords.Tests;

/// <summary>
/// Advance's machine tables (AdvanceConnector) in memory, keyed by MyMachines' names for the columns (serial_no…), read the
/// way <see cref="SqlAdvanceMachines"/> reads them. <see cref="AdvanceSql"/> makes the same tables in SQL Server with
/// their real names ([serial-no], [Meter_Type]…), so the SQL Server tests check the real reader answers as this one does.
/// </summary>
public sealed class FakeAdvanceMachines : IAdvanceMachines
{
    public bool Available { get; set; } = true;

    /// <summary>Each table's rows: MyMachines' column name → value.</summary>
    public Dictionary<string, List<Dictionary<string, object?>>> Tables { get; } = new(StringComparer.Ordinal);

    /// <summary>The tables, with the columns read and their SQL types (the real name is MyMachines' with "-" for "_", but for <see cref="KeepsUnderscore"/>).</summary>
    public static readonly IReadOnlyList<(string Table, (string Column, string Type)[] Columns)> Specs =
    [
        ("daily_mif", AdvanceMachineColumns.DailyMif.Select(c => (c, TypeOf(c))).ToArray()),
        ("serial", AdvanceMachineColumns.Serial.Select(c => (c, TypeOf(c))).ToArray()),
        ("customer", AdvanceMachineColumns.Customer.Select(c => (c, TypeOf(c))).ToArray()),
        ("meter_card", AdvanceMachineColumns.MeterCard.Select(c => (c, TypeOf(c))).ToArray()),
        ("Service", AdvanceMachineColumns.Service.Select(c => (c, TypeOf(c))).ToArray()),
        ("SMeter_History", AdvanceMachineColumns.SMeterHistory.Select(c => (c, TypeOf(c))).ToArray()),
        ("daily_TM", AdvanceMachineColumns.DailyTm.Select(c => (c, TypeOf(c))).ToArray()),
        ("CustomerMapping", AdvanceMachineColumns.CustomerMapping.Select(c => (c, TypeOf(c))).ToArray()),
        ("Email_Master", AdvanceMachineColumns.EmailMaster.Select(c => (c, TypeOf(c))).ToArray()),
        ("contract", AdvanceMachineColumns.Contract.Select(c => (c, TypeOf(c))).ToArray()),
        ("contract_lin", AdvanceMachineColumns.ContractLine.Select(c => (c, TypeOf(c))).ToArray()),
        ("contract_ip", AdvanceMachineColumns.ContractIp.Select(c => (c, TypeOf(c))).ToArray()),
        ("item", AdvanceMachineColumns.Item.Select(c => (c, TypeOf(c))).ToArray()),
        ("item2", AdvanceMachineColumns.Item2.Select(c => (c, TypeOf(c))).ToArray()),
        ("Contract_MeterGroup", AdvanceMachineColumns.ContractMeterGroup.Select(c => (c, TypeOf(c))).ToArray()),
        ("Contract_MGMember", AdvanceMachineColumns.ContractMgMember.Select(c => (c, TypeOf(c))).ToArray()),
        ("contract_grant", AdvanceMachineColumns.ContractGrant.Select(c => (c, TypeOf(c))).ToArray()),
    ];

    /// <summary>AdvanceConnector's Customer_Supp (a customer number's BRN), made by <see cref="AdvanceSql"/> when asked (FakeOldTables makes its own).</summary>
    public static readonly (string Table, (string Column, string Type)[] Columns) CustomerSupp =
        ("Customer_Supp", ExternalColumns.CustomerSupp.Select(c => (c, TypeOf(c))).ToArray());

    private static readonly string[] KeepsUnderscore = ["Meter_Type", "Meter_Reading", "Service_No", "Serial_No", "Min_Bill"];

    public static string RealName(string column) => KeepsUnderscore.Contains(column) ? column : column.Replace('_', '-');

    private static string TypeOf(string column)
    {
        var c = column.ToLowerInvariant();
        if (c.EndsWith("date") || c.EndsWith("_dt") || c.EndsWith("start") || c.EndsWith("end")) return "datetime";
        if (c is "invoice_no" or "copies" or "meter_reading" or "mincopies" or "line_no" or "seq_no" or "granted_copies" or "used_copies" || c.StartsWith("meter_read_", StringComparison.Ordinal)) return "int";
        if (c.StartsWith("meter_price_", StringComparison.Ordinal)) return "decimal(18,5)";
        if (c is "company_id") return "bigint";
        if (c is "min_bill" or "minrate" or "therafter") return "decimal(18,4)";
        return "nvarchar(100)";
    }

    private List<Dictionary<string, object?>> Rows(string table) => Tables.TryGetValue(table, out var rows) ? rows : new List<Dictionary<string, object?>>();

    public void Add(string table, object values)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in values.GetType().GetProperties()) row[p.Name] = p.GetValue(values);
        if (!Tables.TryGetValue(table, out var rows)) Tables[table] = rows = [];
        rows.Add(row);
    }

    /// <summary>A machine (daily_mif) with its serial row (Serial-stat the finance type).</summary>
    public void Machine(string serial, string brn, string name, string mifType, string model = "IM C3000", string item = "418283", string custNo = "C1",
        string contractNo = "", DateTime? start = null, DateTime? end = null, DateTime? sellStart = null, DateTime? sellEnd = null, string finance = "",
        string serviceRef = "", string invoiceRef = "", string contractRef = "", DateTime? installed = null, DateTime? transDate = null, string serialStat = "")
    {
        Add("daily_mif", new
        {
            serial_no = serial, item_no = item, description = model + " MFP", prod_group = "MFP", model, cust_no = custNo, name, BRN = brn,
            trans_date = transDate ?? new DateTime(2026, 10, 16), mif_type = mifType, contract_no = contractNo, contract_typ = contractNo.Length > 0 ? "MPS" : "",
            cntrct_start = start, cntrct_end = end, sell_start = sellStart, sell_end = sellEnd, service_ref = serviceRef, invoice_ref = invoiceRef, contract_ref = contractRef,
            serial_stat = serialStat,
        });
        Add("serial", new
        {
            Item_no = item, Serial_no = serial, Serial_stat = finance, Trans_date = new DateTime(2026, 1, 1), Install_date = installed ?? new DateTime(2025, 10, 3),
            Location = "Level 3", Primary_con = "", Primary_phon = "", Cust_no = custNo, Sell_start = sellStart, Sell_end = sellEnd,
        });
    }

    private static bool Same(object? a, string b) => string.Equals(OutSystemsValues.Text(a), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>A daily_mif row as the SQL reads it (its aliases), with its finance type.</summary>
    private Dictionary<string, object?> AsRead(Dictionary<string, object?> m)
    {
        var serial = Rows("serial").Where(s => Same(s["Serial_no"], OutSystemsValues.Text(m["serial_no"])) && Same(s["Item_no"], OutSystemsValues.Text(m["item_no"])))
            .OrderByDescending(s => s.Date("Trans_date")).FirstOrDefault();
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Serial"] = m["serial_no"], ["Item"] = m["item_no"], ["Model"] = m["model"], ["Description"] = m["description"], ["ProdGroup"] = m["prod_group"],
            ["CustNo"] = m["cust_no"], ["CustName"] = m["name"], ["Brn"] = m["BRN"], ["MifType"] = m["mif_type"], ["ContractNo"] = m["contract_no"],
            ["ContractType"] = m["contract_typ"], ["ContractStart"] = m["cntrct_start"], ["ContractEnd"] = m["cntrct_end"], ["SellStart"] = m["sell_start"],
            ["SellEnd"] = m["sell_end"], ["ServiceRef"] = m["service_ref"], ["InvoiceRef"] = m["invoice_ref"], ["ContractRef"] = m["contract_ref"],
            ["FinanceCode"] = serial?.Get("Serial_stat"),
            ["Installed"] = serial?.Get("Install_date"), ["Location"] = serial?.Get("Location"), ["SerialContact"] = serial?.Get("Primary_con"), ["SerialPhone"] = serial?.Get("Primary_phon"),
        };
    }

    public MachinePage Search(MachineSearch s)
    {
        var keyword = (s.Keyword ?? "").Trim();
        bool Has(object? value) => OutSystemsValues.Text(value).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        var today = s.Today.Date;
        var matching = Rows("daily_mif").Select(m => (m, r: AsRead(m))).Where(x =>
            {
                var r = x.r;
                var row = SqlAdvanceMachines.Row(r, today);
                var finance = OutSystemsValues.Text(r["FinanceCode"]);
                return (keyword.Length == 0 || Has(x.m["serial_no"]) || Has(x.m["model"]) || Has(x.m["name"]) || Has(x.m["BRN"]))
                       && (s.Brn is null || Same(x.m["BRN"], s.Brn))
                       && (s.Coverage.Length == 0 || (s.Coverage == Coverages.Active ? row.Active : row.Coverage == s.Coverage))
                       && (s.Finance.Length == 0 || (s.Finance == FinanceTypes.NoneKey
                           ? FinanceTypes.NameOf(finance).Length == 0
                           : string.Equals(finance, s.Finance, StringComparison.OrdinalIgnoreCase)))
                       && (s.Brns is null || s.Brns.Any(b => Same(x.m["BRN"], b)) || s.Names.Any(n => Same(x.m["name"], n)));
            })
            .OrderBy(x => OutSystemsValues.Text(x.m["name"]), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => OutSystemsValues.Text(x.m["serial_no"]), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => OutSystemsValues.Text(x.m["item_no"]), StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new MachinePage(matching.Count, matching.Skip(Math.Max(0, s.Skip)).Take(Math.Max(1, Math.Min(s.Take, 500)))
            .Select(x =>
            {
                if (s.LastServiced) x.r["LastServiced"] = LastServiced(x.r.Text("Serial"), x.r.Text("CustNo"));
                return SqlAdvanceMachines.Row(x.r, today);
            }).ToList());
    }

    /// <summary>When the last service call done (SO-status F90) at the customer (any, when it has no number) was completed.</summary>
    private DateTime? LastServiced(string serial, string customerNo)
    {
        var done = Rows("Service").Where(r => Same(r["Serial_no"], serial) && Same(r["SO_status"], "F90") && (customerNo.Length == 0 || Same(r["Cust_no"], customerNo)))
            .Select(r => r.Date("Complete_dt")).Where(d => d.HasValue).ToList();
        return done.Count == 0 ? null : done.Max();
    }

    public MachineDetail? Get(string serial, string item, DateTime today)
    {
        var m = Rows("daily_mif").Where(r => Same(r["serial_no"], serial) && (item.Trim().Length == 0 || Same(r["item_no"], item)))
            .OrderByDescending(r => r.Date("trans_date")).FirstOrDefault();
        if (m is null) return FromHistory(serial, item, today);
        var r = AsRead(m);
        var customer = Rows("customer").FirstOrDefault(c => Same(c["Cust_no"], OutSystemsValues.Text(m["cust_no"])));
        r["Address"] = customer?.Get("Address");
        r["City"] = customer?.Get("City");
        r["Zip"] = customer?.Get("Zip_code");
        r["Telephone"] = customer?.Get("Telephone");
        r["CustomerContact"] = customer?.Get("Contact");
        return SqlAdvanceMachines.Detail(r, today);
    }

    public IReadOnlyList<MeterReading> Meters(string serial, string contractNo, DateTime from) =>
        Rows("meter_card").Where(r => Same(r["serial_no"], serial) && (contractNo.Trim().Length == 0 || Same(r["Contract_no"], contractNo))
                                      && r.Date("Invoice_date") is { } at && at >= from)
            .GroupBy(r => (No: r.Long("Invoice_no"), At: r.Date("Invoice_date")))
            .OrderByDescending(g => g.Key.At).ThenByDescending(g => g.Key.No)
            .Select(g => SqlAdvanceMachines.MeterOf(new Dictionary<string, object?>
            {
                ["InvoiceNo"] = g.Key.No, ["InvoiceDate"] = g.Key.At,
                ["BlackWhite"] = g.Max(r => r.Text("Meter_Type").StartsWith("B", StringComparison.OrdinalIgnoreCase) ? r.Long("Copies") : 0),
                ["Colour"] = g.Max(r => r.Text("Meter_Type").StartsWith("C", StringComparison.OrdinalIgnoreCase) ? r.Long("Copies") : 0),
            }))
            .ToList();

    public IReadOnlyList<MonthCopies> MonthlyCopies(IReadOnlyList<MeterMachine> machines, DateTime from) =>
        Rows("meter_card").Where(r => r.Date("Invoice_date") is { } at && at >= from
                                      && machines.Any(m => Same(r["serial_no"], m.Serial) && (m.ContractNo.Trim().Length == 0 || Same(r["Contract_no"], m.ContractNo))))
            .GroupBy(r => (Serial: r.Text("serial_no"), No: r.Long("Invoice_no"), At: r.Date("Invoice_date")!.Value))
            .Select(g => (g.Key.At,
                BlackWhite: g.Max(r => r.Text("Meter_Type").StartsWith("B", StringComparison.OrdinalIgnoreCase) ? r.Long("Copies") : 0),
                Colour: g.Max(r => r.Text("Meter_Type").StartsWith("C", StringComparison.OrdinalIgnoreCase) ? r.Long("Copies") : 0)))
            .GroupBy(i => (i.At.Year, i.At.Month))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => SqlAdvanceMachines.MonthOf(new Dictionary<string, object?>
            {
                ["Year"] = g.Key.Year, ["Month"] = g.Key.Month, ["BlackWhite"] = g.Sum(i => i.BlackWhite), ["Colour"] = g.Sum(i => i.Colour),
            }))
            .ToList();

    public IReadOnlyList<YearCopies> YearlyCopies(IReadOnlyList<MeterMachine> machines, DateTime from) =>
        Rows("meter_card").Where(r => r.Date("Invoice_date") is { } at && at >= from
                                      && machines.Any(m => Same(r["serial_no"], m.Serial) && (m.ContractNo.Trim().Length == 0 || Same(r["Contract_no"], m.ContractNo))))
            .GroupBy(r => (Serial: r.Text("serial_no"), No: r.Long("Invoice_no"), At: r.Date("Invoice_date")!.Value))
            .Select(g => (g.Key.Serial, g.Key.At,
                BlackWhite: g.Max(r => r.Text("Meter_Type").StartsWith("B", StringComparison.OrdinalIgnoreCase) ? r.Long("Copies") : 0),
                Colour: g.Max(r => r.Text("Meter_Type").StartsWith("C", StringComparison.OrdinalIgnoreCase) ? r.Long("Copies") : 0)))
            .GroupBy(i => i.At.Year)
            .OrderBy(g => g.Key)
            .Select(g => SqlAdvanceMachines.YearOf(new Dictionary<string, object?>
            {
                ["Year"] = g.Key, ["Machines"] = g.Select(i => i.Serial.ToUpperInvariant()).Distinct().Count(),
                ["BlackWhite"] = g.Sum(i => i.BlackWhite), ["Colour"] = g.Sum(i => i.Colour),
            }))
            .ToList();

    public IReadOnlyList<ServiceCall> Services(string serial, string customerNo, string contractNo, DateTime from) =>
        Rows("Service").Where(r => Same(r["Serial_no"], serial) && (customerNo.Trim().Length == 0 || Same(r["Cust_no"], customerNo))
                                   && (contractNo.Trim().Length == 0 || Same(r["Contract_no"], contractNo)) && Same(r["SO_status"], "F90")
                                   && r.Date("Entry_date") is { } at && at >= from)
            .OrderByDescending(r => r.Date("Entry_date")).ThenByDescending(r => r.Text("Service_no"), StringComparer.OrdinalIgnoreCase)
            .Select(r =>
            {
                var readings = Rows("SMeter_History").Where(h => Same(h["Service_No"], r.Text("Service_no")) && Same(h["Serial_No"], r.Text("Serial_no"))).ToList();
                long? Max(string prefix)
                {
                    var found = readings.Where(h => h.Text("Meter_Type").StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && h.Get("Meter_Reading") is not null).ToList();
                    return found.Count == 0 ? null : found.Max(h => h.Long("Meter_Reading"));
                }
                return SqlAdvanceMachines.ServiceOf(new Dictionary<string, object?>
                {
                    ["ServiceNo"] = r["Service_no"], ["ServiceType"] = r["Service_type"], ["Technician"] = r["Tech_code"], ["Problem"] = r["Problem_code"],
                    ["Entered"] = r["Entry_date"], ["EnteredTime"] = r["Entry_time"], ["Completed"] = r["Complete_dt"], ["CompletedTime"] = r["Complete_tm"],
                    ["BlackWhite"] = Max("B"), ["Colour"] = Max("C"),
                });
            })
            .ToList();

    public IReadOnlyList<TonerOrder> Toner(string serial, string contractNo, DateTime from) =>
        Rows("Service").Where(r => Same(r["Serial_no"], serial) && (contractNo.Trim().Length == 0 || Same(r["Contract_no"], contractNo)) && Same(r["SO_status"], "P90")
                                   && r.Date("Entry_date") is { } at && at >= from)
            .OrderByDescending(r => r.Date("Entry_date")).ThenByDescending(r => r.Text("Service_no"), StringComparer.OrdinalIgnoreCase)
            .Select(r => SqlAdvanceMachines.TonerOf(new Dictionary<string, object?>
            {
                ["ServiceNo"] = r["Service_no"], ["Entered"] = r["Entry_date"], ["InvoiceNo"] = r["Invoice_no"], ["InvoiceDate"] = r["Invoice_date"],
            }))
            .ToList();

    public LastContract? LastContract(string serial, string item) =>
        SqlAdvanceMachines.LastContractOf(Rows("daily_TM").Where(r => Same(r["serial_no"], serial) && (item.Trim().Length == 0 || Same(r["item_no"], item)))
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["ContractNo"] = r["Contract_no"], ["Start"] = r["Cntrct_start"], ["End"] = r["Cntrct_end"], ["MeterType"] = r["Meter_Type"], ["MinBill"] = r["Min_Bill"],
                ["MinCopies"] = r["MinCopies"], ["MinRate"] = r["MinRate"], ["Thereafter"] = r["Therafter"], ["TransDate"] = r["trans_date"],
            }));

    public IReadOnlyList<MachineRow> DealerSold(string brn, DateTime today) =>
        Rows("daily_mif").Where(m => Same(m["BRN"], brn) && Same(m.Get("serial_stat"), "SALDLR") && Same(m["mif_type"], "MTR"))
            .OrderBy(m => OutSystemsValues.Text(m["serial_no"]), StringComparer.OrdinalIgnoreCase).ThenBy(m => OutSystemsValues.Text(m["item_no"]), StringComparer.OrdinalIgnoreCase)
            .Take(200).Select(m => SqlAdvanceMachines.Row(AsRead(m), today.Date)).ToList();

    public IReadOnlyList<CustomerEmail> CustomerEmails(long companyId) =>
        SqlAdvanceMachines.CustomerEmailsOf(Rows("CustomerMapping").Where(cm => cm.Get("Company_ID") is { } c && Convert.ToInt64(c, CultureInfo.InvariantCulture) == companyId)
            .SelectMany(cm => Rows("Email_Master").Where(e => Same(e["Cust_No"], OutSystemsValues.Text(cm["Cust_No"]))))
            .Select(e => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["ContractType"] = e["Contract_Type"], ["ContractNo"] = e["ContractNo"], ["Email"] = e["EmailAddress"], ["CcEmail"] = e["CCEmail"],
            }));

    private static string T(object? value) => OutSystemsValues.Text(value);
    private static DateTime? D(object? value) => value is DateTime d ? d : null;
    private static readonly Comparer<DateTime?> Dates = Comparer<DateTime?>.Default;
    private static long N(object? value) => value is null ? long.MinValue : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    private static bool In(object? value, IEnumerable<string> list) => list.Any(x => Same(value, x));
    private static bool Like(object? value, string start) => value is not null && T(value).StartsWith(start, StringComparison.OrdinalIgnoreCase);
    private static IReadOnlyDictionary<string, object?> Read(params (string Name, object? Value)[] values)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in values) row[name] = value;
        return row;
    }

    /// <summary>The latest contract line of each serial (by end) of the contracts not on instalment payment, as SqlAdvanceMachines ranks them.</summary>
    private List<Dictionary<string, object?>> Latest()
    {
        var ips = Rows("contract_ip");
        return Rows("contract_lin").Where(l => !ips.Any(ip => ip.Get("contract_no") is not null && Same(ip.Get("contract_no"), T(l.Get("Contract_no")))))
            .GroupBy(l => T(l.Get("Serial_no")).ToUpperInvariant())
            .Select(g => g.OrderByDescending(l => D(l.Get("Cntrct_end")), Dates).ThenByDescending(l => T(l.Get("Contract_no")), StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(l => N(l.Get("Line_no"))).First())
            .ToList();
    }

    /// <summary>A serial daily_mif does not have, from the contract history, as <see cref="SqlAdvanceMachines"/>' FromHistory reads it.</summary>
    private MachineDetail? FromHistory(string serial, string item, DateTime today)
    {
        var ips = Rows("contract_ip");
        // Any line of the serial (and item) not on instalment payment, the latest by end (rank 1 is its serial's latest).
        var lines = Rows("contract_lin").Where(l => Same(l.Get("Serial_no"), serial) && (item.Trim().Length == 0 || Same(l.Get("Item_no"), item))
                                                    && !ips.Any(ip => ip.Get("contract_no") is not null && Same(ip.Get("contract_no"), T(l.Get("Contract_no")))))
            .Select(l => (l, c: Rows("contract").FirstOrDefault(c => Same(c.Get("Contract_no"), T(l.Get("Contract_no"))))))
            .Where(x => x.c is not null)
            .OrderByDescending(x => D(x.l.Get("Cntrct_end")), Dates).ThenByDescending(x => T(x.l.Get("Contract_no")), StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => N(x.l.Get("Line_no")))
            .ToList();
        if (lines.Count == 0) return null;
        var (line, contract) = lines[0];
        var custNo = T(contract!.Get("Cust_no"));
        var customer = Rows("customer").FirstOrDefault(c => Same(c.Get("Cust_no"), custNo));
        var brn = Rows("Customer_Supp").Where(b => Same(b.Get("Cust_no"), custNo)).Select(b => T(b.Get("Business_reg"))).OrderBy(b => b, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        var it = Rows("item").FirstOrDefault(i => Same(i.Get("Item_no"), T(line.Get("Item_no"))));
        var sx = Rows("serial").Where(x => Same(x.Get("Serial_no"), T(line.Get("Serial_no"))) && Same(x.Get("Item_no"), T(line.Get("Item_no"))))
            .OrderByDescending(x => D(x.Get("Trans_date")), Dates).FirstOrDefault();
        var r = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Serial"] = line.Get("Serial_no"), ["Item"] = line.Get("Item_no"), ["Model"] = it?.Get("Description"), ["Description"] = it?.Get("Description"),
            ["ProdGroup"] = it?.Get("Prod_group"), ["CustNo"] = contract.Get("Cust_no"), ["CustName"] = customer?.Get("Name"), ["Brn"] = brn, ["MifType"] = "",
            ["ContractNo"] = line.Get("Contract_no"), ["ContractType"] = contract.Get("Contract_typ"), ["ContractStart"] = line.Get("Cntrct_start"),
            ["ContractEnd"] = line.Get("Cntrct_end"), ["SellStart"] = sx?.Get("Sell_start"), ["SellEnd"] = sx?.Get("Sell_end"), ["ServiceRef"] = "", ["InvoiceRef"] = "",
            ["ContractRef"] = "", ["FinanceCode"] = sx?.Get("Serial_stat"), ["Installed"] = sx?.Get("Install_date"), ["Location"] = sx?.Get("Location"),
            ["SerialContact"] = sx?.Get("Primary_con"), ["SerialPhone"] = sx?.Get("Primary_phon"), ["Address"] = customer?.Get("Address"), ["City"] = customer?.Get("City"),
            ["Zip"] = customer?.Get("Zip_code"), ["Telephone"] = customer?.Get("Telephone"), ["CustomerContact"] = customer?.Get("Contact"),
        };
        return SqlAdvanceMachines.Detail(r, today);
    }

    /// <summary>Many customers' Machines tabs, as <see cref="SqlAdvanceMachines.Customers"/> reads them: each BRN's own page.</summary>
    public IReadOnlyDictionary<string, CustomerMachines> Customers(IReadOnlyCollection<string> brns, DateTime today)
    {
        var answer = new Dictionary<string, CustomerMachines>(StringComparer.OrdinalIgnoreCase);
        foreach (var brn in brns.Select(b => (b ?? "").Trim()).Where(b => b.Length > 0))
            if (!answer.ContainsKey(brn)) answer[brn] = Customer(brn, "", today);
        return answer;
    }

    /// <summary>MyMachines' customer page, as <see cref="SqlAdvanceMachines.Customer"/> reads it (its joins, filters and order).</summary>
    public CustomerMachines Customer(string brn, string name, DateTime today)
    {
        brn = (brn ?? "").Trim();
        name = (name ?? "").Trim();
        if (brn.Length == 0 && name.Length == 0) return CustomerMachines.None;
        today = today.Date;
        var byBrn = brn.Length > 0;
        var ips = Rows("contract_ip");
        bool OnIp(object? contractNo) => ips.Any(ip => ip.Get("contract_no") is not null && Same(ip.Get("contract_no"), T(contractNo)));
        bool Who(Dictionary<string, object?> cs, Dictionary<string, object?> cu) => byBrn ? cs.Get("Business_reg") is not null && Same(cs.Get("Business_reg"), brn) : cu.Get("Name") is not null && Same(cu.Get("Name"), name);

        var contracts = (from c in Rows("contract")
                from cl in Rows("contract_lin") where Same(c.Get("Contract_no"), T(cl.Get("Contract_no")))
                from cu in Rows("customer") where Same(c.Get("Cust_no"), T(cu.Get("Cust_no")))
                from cs in Rows("Customer_Supp") where Same(c.Get("Cust_no"), T(cs.Get("Cust_no")))
                where D(cl.Get("Cntrct_end")) is { } end && end.Date >= today && D(cl.Get("Cntrct_start")) is { } start && start.Date <= today
                      && !OnIp(c.Get("Contract_no")) && Who(cs, cu)
                select (c, cu, cs))
            .GroupBy(x => (T(x.c.Get("Contract_no")).ToUpperInvariant(), D(x.c.Get("Cntrct_start")), D(x.c.Get("Cntrct_end")), T(x.cu.Get("Name")).ToUpperInvariant(),
                T(x.cs.Get("Business_reg")).ToUpperInvariant(), T(x.cu.Get("Cust_no")).ToUpperInvariant()))
            .Select(g => g.First())
            .OrderBy(x => D(x.c.Get("Cntrct_end")), Dates).ThenBy(x => T(x.c.Get("Contract_no")), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => T(x.cu.Get("Cust_no")), StringComparer.OrdinalIgnoreCase).ThenBy(x => T(x.cs.Get("Business_reg")), StringComparer.OrdinalIgnoreCase)
            .Select(x => Read(("ContractNo", x.c.Get("Contract_no")), ("Start", x.c.Get("Cntrct_start")), ("End", x.c.Get("Cntrct_end")),
                ("Customer", x.cu.Get("Name")), ("CustNo", x.cu.Get("Cust_no"))))
            .ToList();
        var numbers = contracts.Select(r => r.Text("ContractNo")).ToList();
        var machines = Rows("daily_mif").Where(m => In(m.Get("contract_no"), numbers))
            .OrderBy(m => T(m.Get("serial_no")), StringComparer.OrdinalIgnoreCase).ThenBy(m => T(m.Get("item_no")), StringComparer.OrdinalIgnoreCase)
            .Select(m => Read(("Serial", m.Get("serial_no")), ("Item", m.Get("item_no")), ("Model", m.Get("model")), ("Start", m.Get("cntrct_start")),
                ("End", m.Get("cntrct_end")), ("CustNo", m.Get("cust_no")), ("ContractNo", m.Get("contract_no"))))
            .ToList();
        var groups = Rows("contract_lin").Where(cl => In(cl.Get("Contract_no"), numbers))
            .SelectMany(cl =>
            {
                var members = Rows("Contract_MGMember").Where(g => g.Get("Line_No") is not null && cl.Get("Line_no") is not null && N(g.Get("Line_No")) == N(cl.Get("Line_no"))
                                                               && Same(g.Get("Contract_No"), T(cl.Get("Contract_no")))).ToList();
                return members.Count == 0 ? new[] { (cl, g: (Dictionary<string, object?>?)null) } : members.Select(g => (cl, g: (Dictionary<string, object?>?)g)).ToArray();
            })
            .OrderBy(x => T(x.cl.Get("Ar_entity")), StringComparer.OrdinalIgnoreCase).ThenBy(x => T(x.cl.Get("Contract_no")), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => T(x.cl.Get("Serial_no")), StringComparer.OrdinalIgnoreCase).ThenBy(x => N(x.cl.Get("Line_no")))
            .ThenBy(x => x.g is null ? null : T(x.g.Get("MGroup_No")), StringComparer.OrdinalIgnoreCase)
            .Select(x => Read(("ContractNo", x.cl.Get("Contract_no")), ("Serial", x.cl.Get("Serial_no")), ("Group", x.g?.Get("MGroup_No"))))
            .ToList();
        var meters = Rows("Contract_MeterGroup").Where(g => In(g.Get("Contract_No"), numbers) && In(g.Get("Meter_Type"), CustomerMachinesRules.MeterTypes))
            .OrderBy(g => T(g.Get("AR_Entity")), StringComparer.OrdinalIgnoreCase).ThenBy(g => T(g.Get("Contract_No")), StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => T(g.Get("MGroup_No")), StringComparer.OrdinalIgnoreCase)
            .Select(g => Read(("ContractNo", g.Get("Contract_No")), ("Group", g.Get("MGroup_No")), ("MeterType", g.Get("Meter_Type")),
                ("Read1", g.Get("Meter_Read_1")), ("Read2", g.Get("Meter_Read_2")), ("Read3", g.Get("Meter_Read_3")),
                ("Price1", g.Get("Meter_Price_1")), ("Price2", g.Get("Meter_Price_2")), ("Price3", g.Get("Meter_Price_3")), ("MinBill", g.Get("Min_Bill"))))
            .ToList();
        var grants = Rows("contract_grant").Where(g => In(g.Get("Contract_no"), numbers) && In(g.Get("MGroup_No"), CustomerMachinesRules.MeterTypes))
            .OrderBy(g => T(g.Get("Record_type")), StringComparer.OrdinalIgnoreCase).ThenBy(g => T(g.Get("Contract_no")), StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => N(g.Get("Line_no"))).ThenBy(g => N(g.Get("seq_no")))
            .Select(g => Read(("ContractNo", g.Get("Contract_no")), ("MeterType", g.Get("MGroup_No")), ("Granted", g.Get("granted_copies")), ("Used", g.Get("used_copies"))))
            .ToList();

        var warranty = Rows("daily_mif")
            .Where(m => D(m.Get("sell_end")) is { } end && end.Date >= today && Same(m.Get("mif_type"), "war")
                        && (byBrn ? m.Get("BRN") is not null && Same(m.Get("BRN"), brn) : m.Get("name") is not null && Same(m.Get("name"), name)))
            .GroupBy(m => (T(m.Get("serial_no")).ToUpperInvariant(), T(m.Get("item_no")).ToUpperInvariant(), T(m.Get("model")).ToUpperInvariant(),
                D(m.Get("sell_end")), D(m.Get("sell_start")), T(m.Get("description")).ToUpperInvariant()))
            .Select(g => g.First())
            .OrderBy(m => D(m.Get("sell_end")), Dates).ThenBy(m => T(m.Get("serial_no")), StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => T(m.Get("item_no")), StringComparer.OrdinalIgnoreCase).ThenBy(m => T(m.Get("model")), StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => T(m.Get("description")), StringComparer.OrdinalIgnoreCase)
            .Select(m => Read(("Serial", m.Get("serial_no")), ("Item", m.Get("item_no")), ("Model", m.Get("description")), ("Start", m.Get("sell_start")), ("End", m.Get("sell_end"))))
            .ToList();
        var extended = warranty.Select(w => w.Text("Serial")).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(serial => (from c in Rows("contract")
                    from cl in Rows("contract_lin") where Same(c.Get("Contract_no"), T(cl.Get("Contract_no")))
                    where CustomerMachinesRules.ExtendedWarrantyTypes.Any(x => Like(c.Get("Contract_typ"), x)) && Same(cl.Get("Serial_no"), serial)
                    orderby T(c.Get("Record_type")), T(c.Get("Contract_no")), N(cl.Get("Line_no"))
                    select Read(("Serial", cl.Get("Serial_no")), ("Start", cl.Get("Cntrct_start")), ("End", cl.Get("Cntrct_end"))))
                .FirstOrDefault())
            .Where(r => r is not null).Select(r => r!)
            .ToList();

        var latest = Latest();
        var expired = (from c in Rows("contract")
                from cl in Rows("contract_lin") where Same(cl.Get("Contract_no"), T(c.Get("Contract_no")))
                from cu in Rows("customer") where Same(cu.Get("Cust_no"), T(c.Get("Cust_no")))
                from cs in Rows("Customer_Supp") where Same(cs.Get("Cust_no"), T(c.Get("Cust_no")))
                from i2 in Rows("item2") where Same(i2.Get("Item_no"), T(cl.Get("Item_no")))
                from i in Rows("item") where Same(i.Get("Item_no"), T(cl.Get("Item_no")))
                from k in latest where Same(k.Get("Serial_no"), T(cl.Get("Serial_no"))) && Same(k.Get("Item_no"), T(cl.Get("Item_no"))) && Same(k.Get("Contract_no"), T(cl.Get("Contract_no")))
                where Who(cs, cu) && cs.Get("Business_reg") is not null && T(cs.Get("Business_reg")).Length > 0
                      && D(cl.Get("Cntrct_end")) is { } end && end <= today && !OnIp(c.Get("Contract_no"))
                select (c, cl, i, k))
            .OrderBy(x => T(x.cl.Get("Serial_no")), StringComparer.OrdinalIgnoreCase).ThenBy(x => T(x.cl.Get("Item_no")), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => T(x.k.Get("Contract_no")), StringComparer.OrdinalIgnoreCase).ThenBy(x => T(x.c.Get("Cust_no")), StringComparer.OrdinalIgnoreCase)
            .Select(x => Read(("Serial", x.cl.Get("Serial_no")), ("Model", x.i.Get("Description")), ("Item", x.cl.Get("Item_no")),
                ("ContractNo", x.k.Get("Contract_no")), ("Start", x.k.Get("Cntrct_start")), ("End", x.k.Get("Cntrct_end"))))
            .ToList();

        var noContract = (from cu in Rows("customer")
                from s in Rows("serial") where Same(cu.Get("Cust_no"), T(s.Get("Cust_no")))
                from i in Rows("item") where Same(s.Get("Item_no"), T(i.Get("Item_no")))
                where !Rows("contract_lin").Any(cl => Same(cl.Get("Serial_no"), T(s.Get("Serial_no"))))
                from cs in Rows("Customer_Supp") where Same(s.Get("Cust_no"), T(cs.Get("Cust_no")))
                where Who(cs, cu) && i.Get("Prod_group") is not null && !CustomerMachinesRules.NotMachines.Any(x => Like(i.Get("Prod_group"), x))
                select (s, i))
            .OrderByDescending(x => D(x.s.Get("Install_date")), Dates).ThenBy(x => T(x.s.Get("Serial_no")), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => T(x.s.Get("Item_no")), StringComparer.OrdinalIgnoreCase)
            .Select(x => Read(("Serial", x.s.Get("Serial_no")), ("Item", x.s.Get("Item_no")), ("Model", x.i.Get("Description")),
                ("Installed", x.s.Get("Install_date")), ("Start", x.s.Get("Sell_start")), ("End", x.s.Get("Sell_end"))))
            .ToList();

        return CustomerMachinesRules.Of(contracts, machines, groups, meters, grants, warranty, extended, expired, noContract);
    }

    /// <summary>The tables in SQL Server with their real column names ([serial-no]…), holding these rows (and Customer_Supp's when <paramref name="customerSupp"/>).</summary>
    public string AdvanceSql(bool customerSupp = true)
    {
        var sql = new StringBuilder();
        foreach (var (table, columns) in customerSupp ? Specs.Append(CustomerSupp) : Specs)
        {
            sql.AppendLine($"CREATE TABLE dbo.[{table}] ({string.Join(", ", columns.Select(c => $"[{RealName(c.Column)}] {c.Type} NULL"))});");
            foreach (var row in Rows(table))
                sql.AppendLine($"INSERT INTO dbo.[{table}] ({string.Join(", ", columns.Select(c => $"[{RealName(c.Column)}]"))}) " +
                               $"VALUES ({string.Join(", ", columns.Select(c => Literal(row.TryGetValue(c.Column, out var v) ? v : null)))});");
        }
        return sql.ToString();
    }

    private static string Literal(object? value) => value switch
    {
        null => "NULL",
        DateTime d => "'" + d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "'",
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        long or int => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        _ => "N'" + Convert.ToString(value, CultureInfo.InvariantCulture)!.Replace("'", "''") + "'",
    };
}

/// <summary>MyMachines' TempAccess in memory (in a <see cref="FakeMyOrders"/>, the OutSystems database of the tests), as <see cref="SqlMyMachinesAccess"/> reads it.</summary>
public sealed class FakeMyMachinesAccess(FakeMyOrders? tables = null) : IMyMachinesAccess
{
    public FakeMyOrders Tables { get; } = tables ?? new FakeMyOrders();
    public bool Available { get; set; } = true;
    public bool CanWrite { get; set; }

    private List<Dictionary<string, object?>> Rows() =>
        Tables.Tables.TryGetValue(Entities.TempAccess, out var rows) ? rows : new List<Dictionary<string, object?>>();

    /// <summary>A row for an OutSystems user (added to the users when new), returning its Id.</summary>
    public long Add(string login, string name, string email, string customerName, string brn, DateTime given)
    {
        var user = Tables.UserRows.FirstOrDefault(u => string.Equals(u.Login, login, StringComparison.OrdinalIgnoreCase))?.Id ?? Tables.User(login, name, email);
        return Tables.Add(Entities.TempAccess, new { USERID = user, CUSTOMERNAME = customerName, BRN = brn, DATETIME = given });
    }

    public IReadOnlyList<TempAccessRow> List() =>
        Rows().Select(r =>
            {
                var user = Tables.UserRows.FirstOrDefault(u => u.Id == r.Long("USERID"));
                return new TempAccessRow
                {
                    Id = r.Long("ID"), UserId = r.Id("USERID"), Login = user is null ? "" : OutSystemsImport.LoginOf(user.Login, "RSP"), UserName = user?.Name ?? "",
                    Email = user?.Email ?? "", CustomerName = r.Text("CUSTOMERNAME"), Brn = r.Text("BRN"), GivenAt = r.Date("DATETIME"),
                };
            })
            .Where(r => !TempAccessRules.IsRemoved(r.CustomerName, r.Brn))
            .OrderByDescending(r => r.GivenAt).ThenByDescending(r => r.Id).ToList();

    public long Give(string login, string email, string customerName, string brn, string by)
    {
        if (!CanWrite) throw new MachineAccessRefusedException(TempAccessRules.WritesOff);
        var user = Tables.UserRows.FirstOrDefault(u => email.Length > 0 && string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase))
                   ?? Tables.UserRows.FirstOrDefault(u => string.Equals(u.Login, login, StringComparison.OrdinalIgnoreCase));
        if (user is null) throw new MachineAccessRefusedException($"{login} is not a user of OutSystems: MyMachines' temporary access needs one.");
        return Tables.Add(Entities.TempAccess, new { USERID = user.Id, CUSTOMERNAME = customerName, BRN = brn, DATETIME = new DateTime(2026, 10, 17, 9, 30, 0) });
    }

    public void Remove(long id, string by)
    {
        if (!CanWrite) throw new MachineAccessRefusedException(TempAccessRules.WritesOff);
        var row = Rows().FirstOrDefault(r => r.Long("ID") == id) ?? throw new MachineAccessRefusedException("That temporary access is not there any more.");
        row["CUSTOMERNAME"] = TempAccessRules.Mark(row.Text("CUSTOMERNAME"), 0);
        row["BRN"] = TempAccessRules.Mark(row.Text("BRN"), 0);
    }
}
