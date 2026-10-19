using System.Globalization;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Data;

/// <summary>
/// MyCRM's timer UpdateCustomerNo, as a scheduled task (daily at 02:45 once turned on): for each active company whose
/// BRN has more than 3 characters, Advance's customers with that BRN (AdvanceConnector's Customer_Supp: Business_reg)
/// give its customer number (Cust_no), with " #Multiple" after it when several share the BRN. A company with no such
/// customer keeps its number. Read through <see cref="ExternalTable"/> (the real column names); only changed numbers are
/// saved (and synced to MyCRM).
/// </summary>
public sealed class CustomerNumbers(IRecordStore store, ExternalDatabases databases, Func<CrmSettings> settings)
{
    /// <summary>Advance's customer numbers by BRN (trimmed, case ignored), in customer number order.</summary>
    public IReadOnlyDictionary<string, List<string>> ByBrn() => databases.Use(ExternalNames.AdvanceConnector, connection =>
    {
        var t = ExternalTable.Of(connection, settings().Connections.AdvanceTables.CustomerSupp);
        var numbers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Sql.Rows(connection, null,
                     $"SELECT {t["Business_reg"]} AS [Business_reg], {t["Cust_no"]} AS [Cust_no] FROM {t} WHERE {t["Business_reg"]} IS NOT NULL ORDER BY {t["Cust_no"]}"))
        {
            var brn = OutSystemsValues.Text(row["Business_reg"]);
            var number = OutSystemsValues.Text(row["Cust_no"]);
            if (brn.Length == 0 || number.Length == 0) continue;
            if (!numbers.TryGetValue(brn, out var list)) numbers[brn] = list = [];
            list.Add(number);
        }
        return (IReadOnlyDictionary<string, List<string>>)numbers;
    });

    /// <summary>The customer number a company gets from Advance's numbers for its BRN; null when it keeps its own.</summary>
    public static string? NumberFor(Company company, IReadOnlyDictionary<string, List<string>> byBrn)
    {
        var brn = company.Brn.Trim();
        if (!company.IsActive || brn.Length <= 3 || !byBrn.TryGetValue(brn, out var numbers) || numbers.Count == 0) return null;
        return Strings.Shorten(numbers[0] + (numbers.Count > 1 ? " #Multiple" : ""), 50);
    }

    public JobOutcome Run(string by)
    {
        var byBrn = ByBrn();
        var changed = 0;
        var checkedCount = 0;
        foreach (var company in store.Find<Company>(Query.Where("IsActive", true)))
        {
            if (NumberFor(company, byBrn) is not { } number) continue;
            checkedCount++;
            if (company.CustomerNo == number) continue;
            company.CustomerNo = number;
            store.Update(company);
            changed++;
        }
        return new JobOutcome($"{checkedCount.ToString(CultureInfo.InvariantCulture)} companies have a customer in Advance; {changed.ToString(CultureInfo.InvariantCulture)} customer number{(changed == 1 ? " was" : "s were")} changed.", changed);
    }
}
