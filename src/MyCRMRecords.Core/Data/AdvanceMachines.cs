using System.Data.Common;
using System.Globalization;
using System.Text;

namespace MyCRMRecords.Data;

/// <summary>
/// A machine's coverage, as MyMachines sorts its machines: on a running contract (mif-type CMA or MTR), under a running
/// warranty (WAR), on time and materials (TM), with a contract or warranty that has ended, or with none.
/// </summary>
public static class Coverages
{
    public const string Contract = "Contract";
    public const string Warranty = "Warranty";
    public const string TimeAndMaterials = "TM";
    public const string Expired = "Expired";
    public const string None = "None";
    /// <summary>Not a coverage but MyCRM's company Machines tab: the contract or the warranty ends after today.</summary>
    public const string Active = "Active";

    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        (Contract, "Contract"), (Warranty, "Warranty"), (TimeAndMaterials, "T&M"), (Expired, "Expired"), (None, "No contract"),
    ];

    /// <summary>The coverage of a filter on the page ("contract", "T&amp;M", "active"…), or "" for every machine.</summary>
    public static string Of(string? text)
    {
        var key = new string((text ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (key.Length == 0) return "";
        if (key == "ACTIVE") return Active;
        foreach (var (code, name) in All)
            if (code.ToUpperInvariant() == key || new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant() == key) return code;
        return "";
    }

    public static string NameOf(string code) => All.Where(c => c.Code == code).Select(c => c.Name).FirstOrDefault() ?? code;

    private static bool Is(string value, params string[] codes) => codes.Any(c => string.Equals(value.Trim(), c, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The coverage of a daily_mif row on <paramref name="today"/>: written the same way in SQL (<see cref="SqlAdvanceMachines"/>),
    /// so a filter and the row's label agree. Dates of 1900 or before are no date.
    /// </summary>
    public static string Of(string mifType, DateTime? contractStart, DateTime? contractEnd, DateTime? sellEnd, DateTime today)
    {
        today = today.Date;
        if (Is(mifType, "CMA", "MTR") && contractStart is { } start && start <= today && contractEnd is { } end && end >= today) return Contract;
        if (Is(mifType, "WAR") && sellEnd is { } warrantyEnd && warrantyEnd >= today) return Warranty;
        if (Is(mifType, "TM")) return TimeAndMaterials;
        if (contractEnd is { } ended && ended < today) return Expired;
        if (Is(mifType, "WAR") && sellEnd is { } warrantyEnded && warrantyEnded < today) return Expired;
        return None;
    }

    /// <summary>MyCRM's company Machines tab: the contract or the warranty ends after today.</summary>
    public static bool IsActive(DateTime? contractEnd, DateTime? sellEnd, DateTime today) =>
        (contractEnd is { } end && end > today.Date) || (sellEnd is { } warrantyEnd && warrantyEnd > today.Date);
}

/// <summary>A machine's finance type: MyCRM's reading of Advance's serial.[Serial-stat].</summary>
public static class FinanceTypes
{
    public const string NoneKey = "none";

    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        ("SALIL", "Internal lease"), ("SALEL", "External lease"), ("SALCAS", "Outright"), ("SALDLS", "Dealer"),
    ];

    /// <summary>The code of a filter on the page ("SALIL", "internal lease"…), "none" for the machines with no finance type, "" for all.</summary>
    public static string Of(string? text)
    {
        var key = new string((text ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (key.Length == 0) return "";
        if (key == "NONE") return NoneKey;
        foreach (var (code, name) in All)
            if (code == key || new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant() == key) return code;
        return "";
    }

    /// <summary>The words for a Serial-stat ("SALIL" → "Internal lease"); "" for any other.</summary>
    public static string NameOf(string? code) =>
        All.Where(c => string.Equals(c.Code, (code ?? "").Trim(), StringComparison.OrdinalIgnoreCase)).Select(c => c.Name).FirstOrDefault() ?? "";
}

/// <summary>What the Machines page and a company's Machines tab ask for.</summary>
public sealed class MachineSearch
{
    /// <summary>Part of the serial number, model, customer's name or BRN.</summary>
    public string Keyword { get; set; } = "";
    /// <summary>A coverage (<see cref="Coverages"/>), <see cref="Coverages.Active"/>, or "" for every machine.</summary>
    public string Coverage { get; set; } = "";
    /// <summary>A Serial-stat code (<see cref="FinanceTypes"/>), "none", or "" for every machine.</summary>
    public string Finance { get; set; } = "";
    /// <summary>One customer's machines, by BRN (a company's Machines tab); null for any.</summary>
    public string? Brn { get; set; }

    /// <summary>
    /// The BRNs whose machines the person sees, with <see cref="Names"/> (customers' names): null for every machine. Set by
    /// the rules (<see cref="Domain.Machines"/>), never by the page.
    /// </summary>
    public IReadOnlyCollection<string>? Brns { get; set; }
    /// <summary>Customers' names (daily_mif.name) whose machines the person also sees (MyMachines' temporary access by name).</summary>
    public IReadOnlyCollection<string> Names { get; set; } = Array.Empty<string>();

    /// <summary>The day the coverage is worked out for.</summary>
    public DateTime Today { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
}

/// <summary>A machine of Advance (a daily_mif row) as the pages show it.</summary>
public sealed record MachineRow
{
    public string Serial { get; init; } = "";
    public string Item { get; init; } = "";
    public string Model { get; init; } = "";
    public string Description { get; init; } = "";
    public string ProdGroup { get; init; } = "";
    public string CustomerNo { get; init; } = "";
    public string Customer { get; init; } = "";
    public string Brn { get; init; } = "";
    public string MifType { get; init; } = "";
    public string ContractNo { get; init; } = "";
    public string ContractType { get; init; } = "";
    public DateTime? ContractStart { get; init; }
    public DateTime? ContractEnd { get; init; }
    public DateTime? WarrantyStart { get; init; }
    public DateTime? WarrantyEnd { get; init; }
    /// <summary>A T&amp;M machine's last service, invoice and contract (daily_mif's service-ref, invoice-ref, contract-ref).</summary>
    public string LastService { get; init; } = "";
    public string LastInvoice { get; init; } = "";
    public string LastContract { get; init; } = "";
    /// <summary>serial.[Serial-stat] (SALIL…) and its words; "" when none.</summary>
    public string FinanceCode { get; init; } = "";
    public string Finance { get; init; } = "";
    /// <summary><see cref="Coverages"/>' code, and whether MyCRM's company tab counts it (contract or warranty ends after today).</summary>
    public string Coverage { get; init; } = "";
    public bool Active { get; init; }
}

public sealed record MachinePage(int Total, IReadOnlyList<MachineRow> Rows);

/// <summary>A machine's page: its row, and from Advance's serial and customer where it is and who to call.</summary>
public sealed record MachineDetail
{
    public MachineRow Machine { get; init; } = new();
    public DateTime? Installed { get; init; }
    public string Location { get; init; } = "";
    public string Contact { get; init; } = "";
    public string Phone { get; init; } = "";
    public string Address { get; init; } = "";
}

/// <summary>An invoice's copies (meter_card): the most black-and-white (meter type B…) and colour (C…) copies on it.</summary>
public sealed record MeterReading(string InvoiceNo, DateTime? InvoiceDate, long BlackWhite, long Colour);

/// <summary>A machine whose copies are added up: its serial number and its contract (none: every invoice of the serial).</summary>
public sealed record MeterMachine(string Serial, string ContractNo);

/// <summary>A month's copies on some machines together (<see cref="IAdvanceMachines.MonthlyCopies"/>).</summary>
public sealed record MonthCopies(int Year, int Month, long BlackWhite, long Colour);

/// <summary>A service call done (Service, SO-status F90) with its meter readings (SMeter_History).</summary>
public sealed record ServiceCall
{
    public string ServiceNo { get; init; } = "";
    public string ServiceType { get; init; } = "";
    /// <summary>MyMachines' grouping of the service type: EM (a breakdown), PM (preventive), Others.</summary>
    public string Group { get; init; } = "";
    public string Technician { get; init; } = "";
    public string Problem { get; init; } = "";
    public DateTime? Entered { get; init; }
    public string EnteredTime { get; init; } = "";
    public DateTime? Completed { get; init; }
    public string CompletedTime { get; init; } = "";
    public long? BlackWhite { get; init; }
    public long? Colour { get; init; }
}

/// <summary>A toner (supplies) order: Service with SO-status P90.</summary>
public sealed record TonerOrder(string ServiceNo, DateTime? Entered, string InvoiceNo, DateTime? InvoiceDate);

/// <summary>A meter type's rates in a T&amp;M machine's last contract (daily_TM).</summary>
public sealed record ContractRate(string MeterType, decimal MinBill, long MinCopies, decimal MinRate, decimal Thereafter);

public sealed record LastContract(string ContractNo, DateTime? Start, DateTime? End, IReadOnlyList<ContractRate> Rates);

/// <summary>
/// Advance's machines, read only through AdvanceConnector (MyMachines' extension entities daily_mif, serial, customer,
/// meter_card, Service, SMeter_History and daily_TM, their table names settings): MyCRMRecords never writes to them, and
/// there is no method that could.
/// </summary>
public interface IAdvanceMachines
{
    /// <summary>Whether AdvanceConnector is set up on the Connections tab.</summary>
    bool Available { get; }
    /// <summary>A page of machines matching the search, by customer and serial number, and how many match.</summary>
    MachinePage Search(MachineSearch search);
    /// <summary>A machine by serial number (and item number when given; else its latest row), or null.</summary>
    MachineDetail? Get(string serial, string item, DateTime today);
    /// <summary>The copies billed per invoice since <paramref name="from"/>, newest first (of the contract, when there is one).</summary>
    IReadOnlyList<MeterReading> Meters(string serial, string contractNo, DateTime from);
    /// <summary>
    /// The copies billed per month since <paramref name="from"/> on these machines together, oldest month first: each
    /// machine's invoices as <see cref="Meters"/> reads them (of its contract, when it has one), added up by month.
    /// </summary>
    IReadOnlyList<MonthCopies> MonthlyCopies(IReadOnlyList<MeterMachine> machines, DateTime from);
    /// <summary>The service calls done since <paramref name="from"/> at this customer (and contract, when there is one), newest first.</summary>
    IReadOnlyList<ServiceCall> Services(string serial, string customerNo, string contractNo, DateTime from);
    /// <summary>The toner orders since <paramref name="from"/> (of the contract, when there is one), newest first.</summary>
    IReadOnlyList<TonerOrder> Toner(string serial, string contractNo, DateTime from);
    /// <summary>A machine's last contract's rates (daily_TM), or null when Advance has none.</summary>
    LastContract? LastContract(string serial, string item);
}

/// <summary>The tables and columns read in AdvanceConnector for the machines, by MyMachines' names (the connection test checks each).</summary>
public static class AdvanceMachineColumns
{
    public static readonly string[] DailyMif =
    [
        "serial_no", "item_no", "description", "prod_group", "model", "cust_no", "name", "BRN", "trans_date", "mif_type", "contract_no", "contract_typ",
        "cntrct_start", "cntrct_end", "sell_start", "sell_end", "service_ref", "invoice_ref", "contract_ref",
    ];
    public static readonly string[] Serial = ["Item_no", "Serial_no", "Serial_stat", "Trans_date", "Install_date", "Location", "Primary_con", "Primary_phon"];
    public static readonly string[] Customer = ["Cust_no", "Name", "Address", "City", "Zip_code", "Telephone", "Contact"];
    public static readonly string[] MeterCard = ["serial_no", "Contract_no", "Invoice_no", "Invoice_date", "Copies", "Meter_Type"];
    public static readonly string[] Service =
    [
        "Service_no", "Serial_no", "Cust_no", "Contract_no", "Service_type", "SO_status", "Tech_code", "Problem_code", "Entry_date", "Entry_time",
        "Complete_dt", "Complete_tm", "Invoice_no", "Invoice_date",
    ];
    public static readonly string[] SMeterHistory = ["Service_No", "Serial_No", "Meter_Type", "Meter_Reading"];
    public static readonly string[] DailyTm = ["trans_date", "item_no", "serial_no", "Contract_no", "Cntrct_start", "Cntrct_end", "Meter_Type", "Min_Bill", "MinCopies", "MinRate", "Therafter"];

    /// <summary>Each table (its name as set) with the columns read from it.</summary>
    public static IReadOnlyList<(string Table, string[] Columns)> Checked(AdvanceTables t) =>
    [
        (t.DailyMif, DailyMif), (t.Serial, Serial), (t.Customer, Customer), (t.MeterCard, MeterCard), (t.Service, Service),
        (t.SMeterHistory, SMeterHistory), (t.DailyTm, DailyTm),
    ];

    /// <summary>MyMachines' grouping of Service-type: EM (breakdowns), PM (preventive maintenance), else Others.</summary>
    public static string GroupOf(string serviceType)
    {
        var type = serviceType.Trim().ToUpperInvariant();
        if (new[] { "APFC", "APFW", "APRT-EXX", "FC", "FW", "RT-EXXON", "RT-O", "RT-OTHER", "PPP", "APPPP" }.Contains(type)) return "EM";
        if (new[] { "PM", "RCFC", "RCFW", "RCRT-EXX", "RCRT-OTH", "SCFC", "SCFW", "RCPPP" }.Contains(type)) return "PM";
        return "Others";
    }
}

/// <summary>
/// <see cref="IAdvanceMachines"/> over AdvanceConnector: every column named through <see cref="ExternalTable"/> (MyMachines'
/// names are not the real ones: serial_no is [serial-no]). Only SELECTs.
/// </summary>
public sealed class SqlAdvanceMachines(ExternalDatabases databases, Func<ConnectionSettings> connections) : IAdvanceMachines
{
    private static readonly DateTime NoDate = new(1900, 1, 1);

    public bool Available => databases.IsConfigured(ExternalNames.AdvanceConnector);

    private AdvanceTables Tables => connections().AdvanceTables;

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A list of values as XML text, for "IN (the list)" with one parameter however long the list.</summary>
    public static string XmlList(IEnumerable<string> values)
    {
        var xml = new StringBuilder("<l>");
        foreach (var value in values.Select(v => (v ?? "").Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            xml.Append("<c>");
            foreach (var ch in value)
            {
                if (ch == '&') xml.Append("&amp;");
                else if (ch == '<') xml.Append("&lt;");
                else if (ch == '>') xml.Append("&gt;");
                else if (ch >= ' ' || ch == '\t') xml.Append(ch);
            }
            xml.Append("</c>");
        }
        return xml.Append("</l>").ToString();
    }

    private static string InXml(string parameter) =>
        $"(SELECT x.c.value('.', 'nvarchar(400)') FROM (SELECT CAST({parameter} AS xml) AS d) q CROSS APPLY q.d.nodes('/l/c') AS x(c))";

    /// <summary>What a machine row is read with (daily_mif m, the finance type from serial f), aliased to the names <see cref="Row"/> reads.</summary>
    private static string Select(ExternalTable m) =>
        $"m.{m["serial_no"]} AS [Serial], m.{m["item_no"]} AS [Item], m.{m["model"]} AS [Model], m.{m["description"]} AS [Description], " +
        $"m.{m["prod_group"]} AS [ProdGroup], m.{m["cust_no"]} AS [CustNo], m.{m["name"]} AS [CustName], m.{m["BRN"]} AS [Brn], " +
        $"m.{m["mif_type"]} AS [MifType], m.{m["contract_no"]} AS [ContractNo], m.{m["contract_typ"]} AS [ContractType], " +
        $"m.{m["cntrct_start"]} AS [ContractStart], m.{m["cntrct_end"]} AS [ContractEnd], m.{m["sell_start"]} AS [SellStart], m.{m["sell_end"]} AS [SellEnd], " +
        $"m.{m["service_ref"]} AS [ServiceRef], m.{m["invoice_ref"]} AS [InvoiceRef], m.{m["contract_ref"]} AS [ContractRef], f.[Stat] AS [FinanceCode]";

    /// <summary>The machine's Serial-stat: its serial row's (the latest, when Advance keeps several).</summary>
    private static string Finance(ExternalTable m, ExternalTable s) =>
        $" OUTER APPLY (SELECT TOP 1 sr.{s["Serial_stat"]} AS [Stat] FROM {s} sr WHERE sr.{s["Serial_no"]} = m.{m["serial_no"]} AND sr.{s["Item_no"]} = m.{m["item_no"]} " +
        $"ORDER BY sr.{s["Trans_date"]} DESC) f";

    /// <summary>The coverage as SQL (<see cref="Coverages.Of(string, DateTime?, DateTime?, DateTime?, DateTime)"/> written the same way).</summary>
    private static string CoverageSql(ExternalTable m) =>
        $"CASE WHEN m.{m["mif_type"]} IN (N'CMA', N'MTR') AND m.{m["cntrct_start"]} > @none AND m.{m["cntrct_start"]} <= @today AND m.{m["cntrct_end"]} >= @today THEN N'{Coverages.Contract}' " +
        $"WHEN m.{m["mif_type"]} = N'WAR' AND m.{m["sell_end"]} >= @today THEN N'{Coverages.Warranty}' " +
        $"WHEN m.{m["mif_type"]} = N'TM' THEN N'{Coverages.TimeAndMaterials}' " +
        $"WHEN m.{m["cntrct_end"]} > @none AND m.{m["cntrct_end"]} < @today THEN N'{Coverages.Expired}' " +
        $"WHEN m.{m["mif_type"]} = N'WAR' AND m.{m["sell_end"]} > @none AND m.{m["sell_end"]} < @today THEN N'{Coverages.Expired}' " +
        $"ELSE N'{Coverages.None}' END";

    public MachinePage Search(MachineSearch search) => databases.Use(ExternalNames.AdvanceConnector, connection =>
    {
        var m = ExternalTable.Of(connection, Tables.DailyMif);
        var s = ExternalTable.Of(connection, Tables.Serial);
        var parameters = new List<(string, object?)> { ("@today", search.Today.Date), ("@none", NoDate) };
        string P(object value)
        {
            var name = "@p" + parameters.Count.ToString(CultureInfo.InvariantCulture);
            parameters.Add((name, value));
            return name;
        }
        var where = new List<string>();
        var keyword = (search.Keyword ?? "").Trim();
        if (keyword.Length > 0)
        {
            var like = P("%" + Sql.EscapeLike(keyword) + "%");
            where.Add("(" + string.Join(" OR ", new[] { "serial_no", "model", "name", "BRN" }.Select(c => $"m.{m[c]} LIKE {like} ESCAPE '\\'")) + ")");
        }
        if (search.Brn is { } brn) where.Add($"m.{m["BRN"]} = {P(brn.Trim())}");
        if (search.Coverage == Coverages.Active)
            where.Add($"(m.{m["cntrct_end"]} > @today OR m.{m["sell_end"]} > @today)");
        else if (search.Coverage.Length > 0)
            where.Add($"({CoverageSql(m)}) = {P(search.Coverage)}");
        if (search.Finance == FinanceTypes.NoneKey)
            where.Add("(f.[Stat] IS NULL OR f.[Stat] NOT IN (" + string.Join(", ", FinanceTypes.All.Select(c => "N'" + c.Code + "'")) + "))");
        else if (search.Finance.Length > 0)
            where.Add($"f.[Stat] = {P(search.Finance)}");
        if (search.Brns is { } brns)
        {
            var allowed = new List<string>();
            if (brns.Count > 0) allowed.Add($"m.{m["BRN"]} IN {InXml(P(XmlList(brns)))}");
            if (search.Names.Count > 0) allowed.Add($"m.{m["name"]} IN {InXml(P(XmlList(search.Names)))}");
            where.Add(allowed.Count == 0 ? "1 = 0" : "(" + string.Join(" OR ", allowed) + ")");
        }
        var from = $" FROM {m} m{Finance(m, s)}" + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "");
        var args = parameters.ToArray();
        var total = Convert.ToInt32(Sql.Rows(connection, null, "SELECT COUNT(*) AS [N]" + from, args)[0]["N"], CultureInfo.InvariantCulture);
        var take = Math.Max(1, Math.Min(search.Take, 500));
        var rows = Sql.Rows(connection, null,
            $"SELECT {Select(m)}{from} ORDER BY m.{m["name"]}, m.{m["serial_no"]}, m.{m["item_no"]} OFFSET {N(Math.Max(0, search.Skip))} ROWS FETCH NEXT {N(take)} ROWS ONLY", args);
        return new MachinePage(total, rows.Select(r => Row(r, search.Today)).ToList());
    });

    public MachineDetail? Get(string serial, string item, DateTime today) => databases.Use(ExternalNames.AdvanceConnector, connection =>
    {
        var m = ExternalTable.Of(connection, Tables.DailyMif);
        var s = ExternalTable.Of(connection, Tables.Serial);
        var c = ExternalTable.Of(connection, Tables.Customer);
        var itemSql = item.Trim().Length > 0 ? $" AND m.{m["item_no"]} = @item" : "";
        var rows = Sql.Rows(connection, null,
            $"SELECT TOP 1 {Select(m)}, sx.{s["Install_date"]} AS [Installed], sx.{s["Location"]} AS [Location], sx.{s["Primary_con"]} AS [SerialContact], " +
            $"sx.{s["Primary_phon"]} AS [SerialPhone], cu.{c["Address"]} AS [Address], cu.{c["City"]} AS [City], cu.{c["Zip_code"]} AS [Zip], " +
            $"cu.{c["Telephone"]} AS [Telephone], cu.{c["Contact"]} AS [CustomerContact] " +
            $"FROM {m} m{Finance(m, s)} " +
            $"OUTER APPLY (SELECT TOP 1 * FROM {s} sr WHERE sr.{s["Serial_no"]} = m.{m["serial_no"]} AND sr.{s["Item_no"]} = m.{m["item_no"]} ORDER BY sr.{s["Trans_date"]} DESC) sx " +
            $"OUTER APPLY (SELECT TOP 1 * FROM {c} cr WHERE cr.{c["Cust_no"]} = m.{m["cust_no"]}) cu " +
            $"WHERE m.{m["serial_no"]} = @serial{itemSql} ORDER BY m.{m["trans_date"]} DESC",
            ("@serial", serial.Trim()), ("@item", item.Trim()));
        return rows.Count == 0 ? null : Detail(rows[0], today);
    });

    /// <summary>A machine's page from its row (the SQL's, or the fake's with the same names).</summary>
    public static MachineDetail Detail(IReadOnlyDictionary<string, object?> r, DateTime today)
    {
        var contact = r.Text("SerialContact");
        var phone = r.Text("SerialPhone");
        return new MachineDetail
        {
            Machine = Row(r, today),
            Installed = r.Date("Installed"),
            Location = r.Text("Location"),
            Contact = contact.Length > 0 ? contact : r.Text("CustomerContact"),
            Phone = phone.Length > 0 ? phone : r.Text("Telephone"),
            Address = string.Join(", ", new[] { r.Text("Address"), r.Text("City"), r.Text("Zip") }.Where(p => p.Length > 0)),
        };
    }

    public IReadOnlyList<MeterReading> Meters(string serial, string contractNo, DateTime from) => databases.Use(ExternalNames.AdvanceConnector, connection =>
    {
        var t = ExternalTable.Of(connection, Tables.MeterCard);
        var contract = contractNo.Trim().Length > 0 ? $" AND {t["Contract_no"]} = @contract" : "";
        return (IReadOnlyList<MeterReading>)Sql.Rows(connection, null,
                $"SELECT {t["Invoice_no"]} AS [InvoiceNo], {t["Invoice_date"]} AS [InvoiceDate], " +
                $"MAX(CASE WHEN {t["Meter_Type"]} LIKE 'B%' THEN {t["Copies"]} ELSE 0 END) AS [BlackWhite], " +
                $"MAX(CASE WHEN {t["Meter_Type"]} LIKE 'C%' THEN {t["Copies"]} ELSE 0 END) AS [Colour] " +
                $"FROM {t} WHERE {t["serial_no"]} = @serial{contract} AND {t["Invoice_date"]} >= @from " +
                $"GROUP BY {t["Invoice_no"]}, {t["Invoice_date"]} ORDER BY {t["Invoice_date"]} DESC, {t["Invoice_no"]} DESC",
                ("@serial", serial.Trim()), ("@contract", contractNo.Trim()), ("@from", from))
            .Select(MeterOf).ToList();
    });

    public static MeterReading MeterOf(IReadOnlyDictionary<string, object?> r) =>
        new(r.Text("InvoiceNo"), r.Date("InvoiceDate"), r.Long("BlackWhite"), r.Long("Colour"));

    /// <summary>The machines as XML text (&lt;l&gt;&lt;m s="serial" c="contract"/&gt;&lt;/l&gt;), one parameter however many.</summary>
    public static string XmlMachines(IEnumerable<MeterMachine> machines)
    {
        static string Attribute(string? value)
        {
            var text = new StringBuilder();
            foreach (var ch in (value ?? "").Trim())
            {
                if (ch == '&') text.Append("&amp;");
                else if (ch == '<') text.Append("&lt;");
                else if (ch == '>') text.Append("&gt;");
                else if (ch == '"') text.Append("&quot;");
                else if (ch >= ' ') text.Append(ch);
            }
            return text.ToString();
        }
        var xml = new StringBuilder("<l>");
        foreach (var m in machines.Where(m => (m.Serial ?? "").Trim().Length > 0))
            xml.Append("<m s=\"").Append(Attribute(m.Serial)).Append("\" c=\"").Append(Attribute(m.ContractNo)).Append("\"/>");
        return xml.Append("</l>").ToString();
    }

    public IReadOnlyList<MonthCopies> MonthlyCopies(IReadOnlyList<MeterMachine> machines, DateTime from)
    {
        if (machines.Count == 0) return Array.Empty<MonthCopies>();
        return databases.Use(ExternalNames.AdvanceConnector, connection =>
        {
            var t = ExternalTable.Of(connection, Tables.MeterCard);
            // An invoice counts once whatever the machines listed: EXISTS, not a join.
            var listed = "SELECT x.m.value('@s', 'nvarchar(400)') AS [s], x.m.value('@c', 'nvarchar(400)') AS [c] " +
                         "FROM (SELECT CAST(@machines AS xml) AS d) q CROSS APPLY q.d.nodes('/l/m') AS x(m)";
            return (IReadOnlyList<MonthCopies>)Sql.Rows(connection, null,
                    "SELECT YEAR(i.[InvoiceDate]) AS [Year], MONTH(i.[InvoiceDate]) AS [Month], SUM(i.[BlackWhite]) AS [BlackWhite], SUM(i.[Colour]) AS [Colour] FROM (" +
                    $"SELECT mc.{t["Invoice_date"]} AS [InvoiceDate], " +
                    $"MAX(CASE WHEN mc.{t["Meter_Type"]} LIKE 'B%' THEN mc.{t["Copies"]} ELSE 0 END) AS [BlackWhite], " +
                    $"MAX(CASE WHEN mc.{t["Meter_Type"]} LIKE 'C%' THEN mc.{t["Copies"]} ELSE 0 END) AS [Colour] " +
                    $"FROM {t} mc WHERE mc.{t["Invoice_date"]} >= @from AND EXISTS (SELECT 1 FROM ({listed}) k " +
                    $"WHERE k.[s] = mc.{t["serial_no"]} AND (k.[c] = N'' OR k.[c] = mc.{t["Contract_no"]})) " +
                    $"GROUP BY mc.{t["serial_no"]}, mc.{t["Invoice_no"]}, mc.{t["Invoice_date"]}) i " +
                    "GROUP BY YEAR(i.[InvoiceDate]), MONTH(i.[InvoiceDate]) ORDER BY [Year], [Month]",
                    ("@machines", XmlMachines(machines)), ("@from", from))
                .Select(MonthOf).ToList();
        });
    }

    public static MonthCopies MonthOf(IReadOnlyDictionary<string, object?> r) =>
        new((int)r.Long("Year"), (int)r.Long("Month"), r.Long("BlackWhite"), r.Long("Colour"));

    public IReadOnlyList<ServiceCall> Services(string serial, string customerNo, string contractNo, DateTime from) => databases.Use(ExternalNames.AdvanceConnector, connection =>
    {
        var t = ExternalTable.Of(connection, Tables.Service);
        var h = ExternalTable.Of(connection, Tables.SMeterHistory);
        var customer = customerNo.Trim().Length > 0 ? $" AND sv.{t["Cust_no"]} = @customer" : "";
        var contract = contractNo.Trim().Length > 0 ? $" AND sv.{t["Contract_no"]} = @contract" : "";
        return (IReadOnlyList<ServiceCall>)Sql.Rows(connection, null,
                $"SELECT sv.{t["Service_no"]} AS [ServiceNo], sv.{t["Service_type"]} AS [ServiceType], sv.{t["Tech_code"]} AS [Technician], " +
                $"sv.{t["Problem_code"]} AS [Problem], sv.{t["Entry_date"]} AS [Entered], sv.{t["Entry_time"]} AS [EnteredTime], " +
                $"sv.{t["Complete_dt"]} AS [Completed], sv.{t["Complete_tm"]} AS [CompletedTime], mh.[BlackWhite], mh.[Colour] " +
                $"FROM {t} sv OUTER APPLY (SELECT MAX(CASE WHEN h.{h["Meter_Type"]} LIKE 'B%' THEN h.{h["Meter_Reading"]} END) AS [BlackWhite], " +
                $"MAX(CASE WHEN h.{h["Meter_Type"]} LIKE 'C%' THEN h.{h["Meter_Reading"]} END) AS [Colour] FROM {h} h " +
                $"WHERE h.{h["Service_No"]} = sv.{t["Service_no"]} AND h.{h["Serial_No"]} = sv.{t["Serial_no"]}) mh " +
                $"WHERE sv.{t["Serial_no"]} = @serial{customer}{contract} AND sv.{t["SO_status"]} LIKE 'F90' AND sv.{t["Entry_date"]} >= @from " +
                $"ORDER BY sv.{t["Entry_date"]} DESC, sv.{t["Service_no"]} DESC",
                ("@serial", serial.Trim()), ("@customer", customerNo.Trim()), ("@contract", contractNo.Trim()), ("@from", from))
            .Select(ServiceOf).ToList();
    });

    public static ServiceCall ServiceOf(IReadOnlyDictionary<string, object?> r) => new()
    {
        ServiceNo = r.Text("ServiceNo"),
        ServiceType = r.Text("ServiceType"),
        Group = AdvanceMachineColumns.GroupOf(r.Text("ServiceType")),
        Technician = r.Text("Technician"),
        Problem = r.Text("Problem"),
        Entered = r.Date("Entered"),
        EnteredTime = r.Text("EnteredTime"),
        Completed = r.Date("Completed"),
        CompletedTime = r.Text("CompletedTime"),
        BlackWhite = r.Get("BlackWhite") is null ? null : r.Long("BlackWhite"),
        Colour = r.Get("Colour") is null ? null : r.Long("Colour"),
    };

    public IReadOnlyList<TonerOrder> Toner(string serial, string contractNo, DateTime from) => databases.Use(ExternalNames.AdvanceConnector, connection =>
    {
        var t = ExternalTable.Of(connection, Tables.Service);
        var contract = contractNo.Trim().Length > 0 ? $" AND {t["Contract_no"]} = @contract" : "";
        return (IReadOnlyList<TonerOrder>)Sql.Rows(connection, null,
                $"SELECT {t["Service_no"]} AS [ServiceNo], {t["Entry_date"]} AS [Entered], {t["Invoice_no"]} AS [InvoiceNo], {t["Invoice_date"]} AS [InvoiceDate] " +
                $"FROM {t} WHERE {t["Serial_no"]} = @serial{contract} AND {t["SO_status"]} LIKE 'P90' AND {t["Entry_date"]} >= @from " +
                $"ORDER BY {t["Entry_date"]} DESC, {t["Service_no"]} DESC",
                ("@serial", serial.Trim()), ("@contract", contractNo.Trim()), ("@from", from))
            .Select(TonerOf).ToList();
    });

    public static TonerOrder TonerOf(IReadOnlyDictionary<string, object?> r) =>
        new(r.Text("ServiceNo"), r.Date("Entered"), r.Text("InvoiceNo") == "0" ? "" : r.Text("InvoiceNo"), r.Date("InvoiceDate"));

    public LastContract? LastContract(string serial, string item) => databases.Use(ExternalNames.AdvanceConnector, connection =>
    {
        var t = ExternalTable.Of(connection, Tables.DailyTm);
        var itemSql = item.Trim().Length > 0 ? $" AND {t["item_no"]} = @item" : "";
        var rows = Sql.Rows(connection, null,
            $"SELECT {t["Contract_no"]} AS [ContractNo], {t["Cntrct_start"]} AS [Start], {t["Cntrct_end"]} AS [End], {t["Meter_Type"]} AS [MeterType], " +
            $"{t["Min_Bill"]} AS [MinBill], {t["MinCopies"]} AS [MinCopies], {t["MinRate"]} AS [MinRate], {t["Therafter"]} AS [Thereafter], {t["trans_date"]} AS [TransDate] " +
            $"FROM {t} WHERE {t["serial_no"]} = @serial{itemSql}",
            ("@serial", serial.Trim()), ("@item", item.Trim()));
        return LastContractOf(rows);
    });

    /// <summary>The last contract from daily_TM's rows: the latest contract's (by its end, then the row's day), a rate per meter type.</summary>
    public static LastContract? LastContractOf(IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0) return null;
        var latest = list.OrderByDescending(r => r.Date("End") ?? DateTime.MinValue).ThenByDescending(r => r.Date("TransDate") ?? DateTime.MinValue)
            .ThenByDescending(r => r.Text("ContractNo"), StringComparer.Ordinal).First();
        var contract = latest.Text("ContractNo");
        var rates = list.Where(r => r.Text("ContractNo") == contract)
            .GroupBy(r => r.Text("MeterType"), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(r => r.Date("TransDate") ?? DateTime.MinValue).First())
            .OrderBy(r => r.Text("MeterType"), StringComparer.Ordinal)
            .Select(r => new ContractRate(r.Text("MeterType"), r.Decimal("MinBill"), r.Long("MinCopies"), r.Decimal("MinRate"), r.Decimal("Thereafter")))
            .ToList();
        return new LastContract(contract, latest.Date("Start"), latest.Date("End"), rates);
    }

    /// <summary>A machine row (the SQL's, or the fake's with the same names) as the pages show it.</summary>
    public static MachineRow Row(IReadOnlyDictionary<string, object?> r, DateTime today)
    {
        var finance = r.Text("FinanceCode");
        var mifType = r.Text("MifType");
        return new MachineRow
        {
            Serial = r.Text("Serial"),
            Item = r.Text("Item"),
            Model = r.Text("Model"),
            Description = r.Text("Description"),
            ProdGroup = r.Text("ProdGroup"),
            CustomerNo = r.Text("CustNo"),
            Customer = r.Text("CustName"),
            Brn = r.Text("Brn"),
            MifType = mifType,
            ContractNo = r.Text("ContractNo"),
            ContractType = r.Text("ContractType"),
            ContractStart = r.Date("ContractStart"),
            ContractEnd = r.Date("ContractEnd"),
            WarrantyStart = r.Date("SellStart"),
            WarrantyEnd = r.Date("SellEnd"),
            LastService = r.Text("ServiceRef"),
            LastInvoice = r.Text("InvoiceRef"),
            LastContract = r.Text("ContractRef"),
            FinanceCode = FinanceTypes.NameOf(finance).Length > 0 ? finance.ToUpperInvariant() : finance,
            Finance = FinanceTypes.NameOf(finance),
            Coverage = Coverages.Of(mifType, r.Date("ContractStart"), r.Date("ContractEnd"), r.Date("SellEnd"), today),
            Active = Coverages.IsActive(r.Date("ContractEnd"), r.Date("SellEnd"), today),
        };
    }
}
