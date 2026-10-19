using MyCRMRecords.Domain;

namespace MyCRMRecords.Data;

/// <summary>
/// AdvanceConnector's public holidays (MyCRM's public_holiday: date, name), read through <see cref="ExternalTable"/>
/// (the real column names) and kept for an hour. When AdvanceConnector is not set up, or cannot be read, there are
/// none: only weekends are left out of the working days.
/// </summary>
public sealed class SqlHolidays(ExternalDatabases databases, Func<CrmSettings> settings, Func<DateTime> clock) : IHolidays
{
    private readonly object _gate = new();
    private (DateTime At, IReadOnlyCollection<DateTime> Dates)? _cached;

    public bool Available => databases.IsConfigured(ExternalNames.AdvanceConnector);

    public IReadOnlyCollection<DateTime> Dates()
    {
        if (!Available) return Array.Empty<DateTime>();
        lock (_gate)
            if (_cached is { } cached && clock() - cached.At < TimeSpan.FromHours(1)) return cached.Dates;
        IReadOnlyCollection<DateTime> dates;
        try
        {
            dates = databases.Use(ExternalNames.AdvanceConnector, connection =>
            {
                var t = ExternalTable.Of(connection, settings().Connections.AdvanceTables.PublicHolidays);
                return (IReadOnlyCollection<DateTime>)Sql.Rows(connection, null, $"SELECT {t["date"]} AS [date] FROM {t}")
                    .Select(r => r["date"]).OfType<DateTime>().Select(d => d.Date).Distinct().ToList();
            });
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException or TimeoutException)
        {
            return Array.Empty<DateTime>();
        }
        lock (_gate) _cached = (clock(), dates);
        return dates;
    }
}
