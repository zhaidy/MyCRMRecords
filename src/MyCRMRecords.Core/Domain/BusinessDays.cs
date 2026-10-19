namespace MyCRMRecords.Domain;

/// <summary>The public holidays (AdvanceConnector's public_holiday): their dates.</summary>
public interface IHolidays
{
    /// <summary>Whether the holidays can be read; when not, only weekends are left out.</summary>
    bool Available { get; }
    IReadOnlyCollection<DateTime> Dates();
}

/// <summary>No public holidays (AdvanceConnector not set up): only Saturdays and Sundays are left out.</summary>
public sealed class NoHolidays : IHolidays
{
    public bool Available => false;
    public IReadOnlyCollection<DateTime> Dates() => Array.Empty<DateTime>();
}

/// <summary>
/// MyCRM's business-day arithmetic: Saturdays, Sundays and public holidays are not working days.
/// <list type="bullet">
/// <item><see cref="DaysOff"/> is NoOfHolidaysBetweenTwoDates: the days from the first date up to (not including) the
/// second that are not working days.</item>
/// <item><see cref="Hours"/> is HourDiff: the hours between, less 24 for each such day.</item>
/// <item><see cref="Days"/> is DayDiff: the days between, less those days.</item>
/// <item><see cref="Add"/> is PlusDate: the date some working days later.</item>
/// </list>
/// OutSystems' DiffHours counts whole hours, DiffDays whole days between the dates (times ignored) *(inferred)*.
/// </summary>
public sealed class BusinessDays(IHolidays holidays)
{
    private HashSet<DateTime>? _dates;

    private HashSet<DateTime> Holidays => _dates ??= new HashSet<DateTime>(holidays.Dates().Select(d => d.Date));

    public bool IsWorkingDay(DateTime date) =>
        date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday && !Holidays.Contains(date.Date);

    public int DaysOff(DateTime from, DateTime to)
    {
        var count = 0;
        for (var date = from.Date; date < to.Date; date = date.AddDays(1))
            if (!IsWorkingDay(date)) count++;
        return count;
    }

    public int Hours(DateTime from, DateTime to) => (int)Math.Truncate((to - from).TotalHours) - 24 * DaysOff(from, to);

    public int Days(DateTime from, DateTime to) => (to.Date - from.Date).Days - DaysOff(from, to);

    /// <summary>The date <paramref name="days"/> working days after <paramref name="from"/>.</summary>
    public DateTime Add(DateTime from, int days)
    {
        var date = from.Date;
        for (var i = 0; i < days;)
        {
            date = date.AddDays(1);
            if (IsWorkingDay(date)) i++;
        }
        return date;
    }
}

/// <summary>The financial year, April to March (MyCRM's GetFY and FirstLastDayByFY).</summary>
public static class FinancialYears
{
    /// <summary>The year a date's financial year starts in: before April, the year before.</summary>
    public static int Of(DateTime date) => date.Month <= 3 ? date.Year - 1 : date.Year;

    public static DateTime First(int year) => new(year, 4, 1);

    public static DateTime Last(int year) => new(year + 1, 3, 31);

    /// <summary>The first day of each month of the year, April first.</summary>
    public static IReadOnlyList<DateTime> Months(int year) => Enumerable.Range(0, 12).Select(i => First(year).AddMonths(i)).ToList();

    /// <summary>"FY 2026/27".</summary>
    public static string Label(int year) => $"FY {year}/{((year + 1) % 100).ToString("00", System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>Times of day as MyCRM's Time attributes, kept here as "HH:mm:ss" ("" for none).</summary>
public static class TimesOfDay
{
    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>"9:5", "09:05", "09:05:30" or "0905" → "09:05:00" (or "09:05:30"); "" when not a time.</summary>
    public static string Normalise(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return "";
        var parts = t.Length == 4 && t.All(char.IsDigit) ? new[] { t.Substring(0, 2), t.Substring(2) } : t.Split(':');
        if (parts.Length is < 2 or > 3) return "";
        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, Invariant, out numbers[i])) return "";
        if (numbers[0] >= 24 || numbers[1] >= 60 || numbers[2] >= 60) return "";
        return new TimeSpan(numbers[0], numbers[1], numbers[2]).ToString(@"hh\:mm\:ss", Invariant);
    }

    public static TimeSpan Span(string? time) =>
        Normalise(time) is { Length: 8 } t ? TimeSpan.ParseExact(t, @"hh\:mm\:ss", Invariant) : TimeSpan.Zero;

    /// <summary>"09:05" for the pages.</summary>
    public static string Short(string? time) => Normalise(time) is { Length: 8 } t ? t.Substring(0, 5) : "";

    /// <summary>The date and the time of day together (MyCRM's BuildDateTime).</summary>
    public static DateTime? At(DateTime? date, string? time) => date is { } d ? d.Date + Span(time) : null;

    /// <summary>A time of day as OutSystems keeps it: 1900-01-01 with the time.</summary>
    public static DateTime ToOutSystems(string? time) => new DateTime(1900, 1, 1) + Span(time);

    /// <summary>The time of day of an OutSystems Time value ("" for 00:00:00, which is also its empty value).</summary>
    public static string FromOutSystems(object? value) =>
        value is DateTime d && d.TimeOfDay > TimeSpan.Zero ? new TimeSpan(d.Hour, d.Minute, d.Second).ToString(@"hh\:mm\:ss", Invariant) : "";

    public static string Of(DateTime at) => new TimeSpan(at.Hour, at.Minute, at.Second).ToString(@"hh\:mm\:ss", Invariant);
}
