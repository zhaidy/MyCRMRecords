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
    public Access Access { get; init; } = null!;
    public CrmUsers Users { get; init; } = null!;
    public Territories Territories { get; init; } = null!;
    public Companies Companies { get; init; } = null!;
    public CustomerNumbers CustomerNumbers { get; init; } = null!;
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
    /// <param name="temporary">MyCRM's temporary territories (tests); else read from the OutSystems database.</param>
    public static CrmApp Create(IRecordStore store, SettingsFile<CrmSettings> settingsFile, Func<string, DbConnection> connectionFactory,
        string schedulerStateFile, Func<DateTime> clock, IEmailSender? emailSender = null, IOldTables? oldTables = null, ITemporaryTerritories? temporary = null)
    {
        Func<CrmSettings> settings = settingsFile.Load;
        var databases = new ExternalDatabases(settings, connectionFactory);
        // The sync with MyCRM sees the records as they are; the app's own changes go through SyncedStore. Of MyCRM's
        // territories only the active permanent ones are read (CrmTables).
        Func<string> module = () => settings().Connections.CrmModule;
        var myCrm = new CrmTables(databases, module, new OutSystemsTables(databases, module, "MyCRM", "RSP"));
        Func<IOldTables?> tables = () => oldTables ?? (databases.IsConfigured(ExternalNames.OutSystems) ? myCrm : null);
        Func<ITemporaryTerritories?> temporaryTerritories = () => temporary ?? (oldTables is null && databases.IsConfigured(ExternalNames.OutSystems) ? myCrm : null);
        var sync = new OldAppSync(store, tables,
            CrmSync.Entities(clock), () => settings().Sync, change => settingsFile.Change(s => change(s.Sync)),
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(schedulerStateFile))!, "old-app-sync.lock"), clock);
        var records = store;
        store = new SyncedStore(store, sync);
        var mailer = new Mailer(store, settings, clock, emailSender);
        var people = new People(store, clock);
        var access = new Access(store, temporaryTerritories);
        var territories = new Territories(store, people, clock);
        var customerNumbers = new CustomerNumbers(store, databases, settings);
        return new CrmApp
        {
            Store = store,
            Settings = settings,
            SettingsFile = settingsFile,
            People = people,
            Access = access,
            Users = new CrmUsers(store, people, clock),
            Territories = territories,
            Companies = new Companies(store, access, territories, clock),
            CustomerNumbers = customerNumbers,
            Mailer = mailer,
            Databases = databases,
            Scheduler = new Scheduler(store, settings, schedulerStateFile,
                new Dictionary<string, Func<string, JobOutcome>> { [Jobs.CustomerNumbers] = customerNumbers.Run }, clock, sync.Tick),
            Sync = sync,
            Import = new OutSystemsImport(records, databases, settings, clock, sync),
            Clock = clock,
        };
    }
}
