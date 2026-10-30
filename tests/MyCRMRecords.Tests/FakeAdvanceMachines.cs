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
    ];

    private static readonly string[] KeepsUnderscore = ["Meter_Type", "Meter_Reading", "Service_No", "Serial_No", "Min_Bill"];

    public static string RealName(string column) => KeepsUnderscore.Contains(column) ? column : column.Replace('_', '-');

    private static string TypeOf(string column)
    {
        var c = column.ToLowerInvariant();
        if (c.EndsWith("date") || c.EndsWith("_dt") || c.EndsWith("start") || c.EndsWith("end")) return "datetime";
        if (c is "invoice_no" or "copies" or "meter_reading" or "mincopies") return "int";
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
        string serviceRef = "", string invoiceRef = "", string contractRef = "", DateTime? installed = null, DateTime? transDate = null)
    {
        Add("daily_mif", new
        {
            serial_no = serial, item_no = item, description = model + " MFP", prod_group = "MFP", model, cust_no = custNo, name, BRN = brn,
            trans_date = transDate ?? new DateTime(2026, 10, 16), mif_type = mifType, contract_no = contractNo, contract_typ = contractNo.Length > 0 ? "MPS" : "",
            cntrct_start = start, cntrct_end = end, sell_start = sellStart, sell_end = sellEnd, service_ref = serviceRef, invoice_ref = invoiceRef, contract_ref = contractRef,
        });
        Add("serial", new
        {
            Item_no = item, Serial_no = serial, Serial_stat = finance, Trans_date = new DateTime(2026, 1, 1), Install_date = installed ?? new DateTime(2025, 10, 3),
            Location = "Level 3", Primary_con = "", Primary_phon = "",
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
            .Select(x => SqlAdvanceMachines.Row(x.r, today)).ToList());
    }

    public MachineDetail? Get(string serial, string item, DateTime today)
    {
        var m = Rows("daily_mif").Where(r => Same(r["serial_no"], serial) && (item.Trim().Length == 0 || Same(r["item_no"], item)))
            .OrderByDescending(r => r.Date("trans_date")).FirstOrDefault();
        if (m is null) return null;
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

    /// <summary>The tables in SQL Server with their real column names ([serial-no]…), holding these rows.</summary>
    public string AdvanceSql()
    {
        var sql = new StringBuilder();
        foreach (var (table, columns) in Specs)
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
