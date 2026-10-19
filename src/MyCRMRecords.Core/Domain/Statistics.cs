using System.Globalization;
using MyCRMRecords.Data;
using MyCRMRecords.Documents;

namespace MyCRMRecords.Domain;

/// <summary>A month of the financial year's charts: the completed feedbacks received that month, and their averages.</summary>
public sealed record MonthFigures(DateTime Month, IReadOnlyList<long> Ids, int AcknowledgedHours, int StartResolutionDays, int CloseCaseDays,
    IReadOnlyList<long> WithinTarget, IReadOnlyList<long> OverTarget);

/// <summary>A slice of a pie: a label, and the feedbacks counted in it.</summary>
public sealed record Slice(string Label, IReadOnlyList<long> Ids);

public sealed record YearFigures(int Year, IReadOnlyList<MonthFigures> Months, IReadOnlyList<Slice> Categories, IReadOnlyList<Slice> Divisions, IReadOnlyList<Feedback> Feedbacks);

/// <summary>A line of the report (MyCRM's Report structure).</summary>
public sealed record ReportRow(long Id, string CmsInCharge, string CmsDivision, string CompanyName, DateTime? DateReceived, int AcknowledgedHours, int CloseCaseDays,
    string Status, string FeedbackType, string Description, string RootCause, string CorrectiveAction);

/// <summary>
/// The Statistics page (MyCRM's Statistic flow): for a financial year (April to March), the monthly averages of the
/// completed feedbacks' timings against their service targets, how many were closed within 10 days, and the
/// feedbacks by category and by division; and the report of the feedbacks received in some months.
/// A feedback counts in the month it was received (or, with no Date Received, created).
/// </summary>
public sealed class Statistics(IRecordStore store, Feedbacks feedbacks, People people, Func<DateTime> clock)
{
    /// <summary>The service targets (MyCRM's KPI panel): 24 hours, 2 and 10 working days.</summary>
    public const int AcknowledgeTarget = 24;
    public const int StartTarget = 2;
    public const int CloseTarget = 10;

    public static DateTime? MonthOf(Feedback f) => Feedbacks.ReceivedOn(f) is { } d ? new DateTime(d.Year, d.Month, 1) : null;

    /// <summary>The financial years to choose from: this one, and back to the first feedback's (at least the one before, as MyCRM).</summary>
    public IReadOnlyList<int> Years()
    {
        var current = FinancialYears.Of(clock());
        var first = store.Find<Feedback>(Query.Where("IsActive", true)).Select(Feedbacks.ReceivedOn).Where(d => d is not null).Select(d => FinancialYears.Of(d!.Value))
            .DefaultIfEmpty(current - 1).Min();
        return Enumerable.Range(Math.Min(first, current - 1), current - Math.Min(first, current - 1) + 1).Reverse().ToList();
    }

    private static int Average(IEnumerable<int> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0 : (int)Math.Round(list.Average(v => (decimal)v), MidpointRounding.AwayFromZero);
    }

    public YearFigures Year(int year)
    {
        var first = FinancialYears.First(year);
        var last = FinancialYears.Last(year);
        var completed = store.Find<Feedback>(Query.Where("IsActive", true).And("IsCompleted", true))
            .Where(f => MonthOf(f) is { } m && m >= first && m <= last).OrderBy(f => f.Id).ToList();
        var months = FinancialYears.Months(year).Select(month =>
        {
            var of = completed.Where(f => MonthOf(f) == month).ToList();
            return new MonthFigures(month, of.Select(f => f.Id).ToList(), Average(of.Select(f => f.AcknowledgedHours)), Average(of.Select(f => f.StartResolutionDays)),
                Average(of.Select(f => f.CloseCaseDays)), of.Where(f => f.CloseCaseDays <= CloseTarget).Select(f => f.Id).ToList(),
                of.Where(f => f.CloseCaseDays > CloseTarget).Select(f => f.Id).ToList());
        }).ToList();
        var categories = store.Find<FeedbackCategory>().ToDictionary(c => c.Id, c => c.Label);
        var divisions = feedbacks.DivisionNames();
        // MyCRM's pies leave out the feedbacks with no category (inner join) and no division.
        var byCategory = completed.Where(f => f.CategoryId is { } c && categories.ContainsKey(c)).GroupBy(f => categories[f.CategoryId!.Value])
            .Select(g => new Slice(g.Key, g.Select(f => f.Id).ToList())).OrderByDescending(s => s.Ids.Count).ThenBy(s => s.Label, StringComparer.OrdinalIgnoreCase).ToList();
        var byDivision = completed.Select(f => (f, d: Feedbacks.DivisionOf(f, divisions))).Where(x => x.d.Length > 0).GroupBy(x => x.d, StringComparer.OrdinalIgnoreCase)
            .Select(g => new Slice(g.Key, g.Select(x => x.f.Id).ToList())).OrderByDescending(s => s.Ids.Count).ThenBy(s => s.Label, StringComparer.OrdinalIgnoreCase).ToList();
        return new YearFigures(year, months, byCategory, byDivision, completed);
    }

    /// <summary>The report (MyCRM's Report_): the active feedbacks received in the months chosen, whatever their status.</summary>
    public IReadOnlyList<ReportRow> Report(IEnumerable<DateTime> months)
    {
        var wanted = new HashSet<DateTime>(months.Select(m => new DateTime(m.Year, m.Month, 1)));
        var divisions = feedbacks.DivisionNames();
        return store.Find<Feedback>(Query.Where("IsActive", true)).Where(f => MonthOf(f) is { } m && wanted.Contains(m)).OrderBy(f => f.Id)
            .Select(f => new ReportRow(f.Id, people.NameOf(f.PersonResponsible), Feedbacks.DivisionOf(f, divisions), f.CompanyName, f.DateReceived, f.AcknowledgedHours,
                f.CloseCaseDays, FeedbackStatuses.List.Label(f.Status), FeedbackTypes.List.Label(f.FeedbackType), f.Description, f.RootCause, f.CorrectiveAction))
            .ToList();
    }

    /// <summary>The report as MyCRM's download: "CMS Report - October 2026,November 2026.xlsx".</summary>
    public (string FileName, byte[] Content) ReportFile(IReadOnlyList<DateTime> months)
    {
        var rows = Report(months);
        var columns = new List<Xlsx.ColumnSpec>
        {
            new("Id", 8, "number"), new("CMS In Charge", 24), new("CMS Division", 18), new("Company Name", 30), new("Date Complaint Received", 14, "date"),
            new("No Of Hours Complaint Acknowledged", 14, "number"), new("No Of Days To Complete Feedback Resolution", 14, "number"), new("Status Of CMS", 20),
            new("Feedback Type", 20), new("Feedback Description", 50), new("Root Cause", 40), new("Corrective Action", 40),
        };
        var bytes = Xlsx.Create("CMS Report", columns, rows.Select(r => new object?[]
        {
            r.Id, r.CmsInCharge, r.CmsDivision, r.CompanyName, r.DateReceived, r.AcknowledgedHours, r.CloseCaseDays, r.Status, r.FeedbackType,
            Strings.Shorten(r.Description, 32000), Strings.Shorten(r.RootCause, 32000), Strings.Shorten(r.CorrectiveAction, 32000),
        }));
        var names = string.Join(",", months.OrderBy(m => m).Select(m => m.ToString("MMMM yyyy", CultureInfo.InvariantCulture)));
        return ("CMS Report - " + names + ".xlsx", bytes);
    }
}
