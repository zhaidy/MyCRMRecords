using System.Data.Common;
using MyCRMRecords.Data;
using MyCRMRecords.Domain;
using MyCRMRecords.Integration;
using Newtonsoft.Json;

namespace MyCRMRecords.Api;

/// <summary>Thrown for a body that cannot be read; answered 400.</summary>
public sealed class BadInputException(string message = "The request could not be read.") : Exception(message);

public static class Input
{
    public static T Read<T>(string body) where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(body)) return new T();
        try
        {
            return MyCRMRecords.Json.Deserialize<T>(body) ?? new T();
        }
        catch (JsonException)
        {
            throw new BadInputException();
        }
    }
}

/// <summary>Everything MyCRMRecords does, for its pages and the tests.</summary>
public sealed class CrmApp
{
    public IRecordStore Store { get; init; } = null!;
    public Func<CrmSettings> Settings { get; init; } = null!;
    public SettingsFile<CrmSettings> SettingsFile { get; init; } = null!;
    public People People { get; init; } = null!;
    public Feedbacks Feedbacks { get; init; } = null!;
    public Setup Setup { get; init; } = null!;
    public Statistics Statistics { get; init; } = null!;
    public IHolidays Holidays { get; init; } = null!;
    public Mailer Mailer { get; init; } = null!;
    public ExternalDatabases Databases { get; init; } = null!;
    public Scheduler Scheduler { get; init; } = null!;
    /// <summary>The two-way sync with MyCRM (its tables in the OutSystems database).</summary>
    public OldAppSync Sync { get; init; } = null!;
    /// <summary>The copy of MyCRM's data (admin page).</summary>
    public OutSystemsImport Import { get; init; } = null!;
    public Func<DateTime> Clock { get; init; } = null!;
}

/// <summary>Builds MyCRMRecords's objects, the same for the web site and the tests.</summary>
public static class CrmFactory
{
    /// <param name="oldTables">MyCRM's tables (tests); else those of the OutSystems database.</param>
    /// <param name="holidays">The public holidays (tests); else AdvanceConnector's.</param>
    public static CrmApp Create(IRecordStore store, SettingsFile<CrmSettings> settingsFile, Func<string, DbConnection> connectionFactory,
        string schedulerStateFile, Func<DateTime> clock, IEmailSender? emailSender = null, IOldTables? oldTables = null, IHolidays? holidays = null)
    {
        Func<CrmSettings> settings = settingsFile.Load;
        var databases = new ExternalDatabases(settings, connectionFactory);
        // The sync with MyCRM sees the records as they are; the app's own changes go through SyncedStore.
        // An attachment's bytes are read only when a file is copied here, and SQL Server hashes only the files not kept here
        // already (CrmTables).
        Func<string> module = () => settings().Connections.CrmModule;
        var own = store;
        Func<IReadOnlyDictionary<long, string>> knownFiles = () =>
        {
            var known = new Dictionary<long, string>();
            foreach (var a in own.Find<FeedbackFile>())
                if (a.OutSystemsId is { } id && id != 0 && a.Content.Length > 0) known[id] = OldValues.Canonical(a.Content);
            return known;
        };
        var myCms = oldTables ?? new CrmTables(databases, module, new OutSystemsTables(databases, module, "MyCRM", "RSP"), knownFiles);
        Func<IOldTables?> tables = () => oldTables ?? (databases.IsConfigured(ExternalNames.OutSystems) ? myCms : null);
        var sync = new OldAppSync(store, tables,
            CrmSync.Entities(clock, tables), () => settings().Sync, change => settingsFile.Change(s => change(s.Sync)),
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(schedulerStateFile))!, "old-app-sync.lock"), clock);
        var records = store;
        store = new SyncedStore(store, sync);
        var mailer = new Mailer(store, settings, clock, emailSender);
        var people = new People(store, clock);
        var publicHolidays = holidays ?? new SqlHolidays(databases, settings, clock);
        var feedbacks = new Feedbacks(store, people, mailer, () => new BusinessDays(publicHolidays), settings, clock);
        return new CrmApp
        {
            Store = store,
            Settings = settings,
            SettingsFile = settingsFile,
            People = people,
            Feedbacks = feedbacks,
            Setup = new Setup(store, feedbacks, clock),
            Statistics = new Statistics(store, feedbacks, people, clock),
            Holidays = publicHolidays,
            Mailer = mailer,
            Databases = databases,
            Scheduler = new Scheduler(store, settings, schedulerStateFile, new Dictionary<string, Func<string, JobOutcome>>(), clock, sync.Tick),
            Sync = sync,
            Import = new OutSystemsImport(records, databases, settings, clock, sync),
            Clock = clock,
        };
    }
}
