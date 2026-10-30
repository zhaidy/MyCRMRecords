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
    /// <summary>The records: MyCRM's in MyCRM's tables, MyCRMRecords' own in its storage (<see cref="RoutedStore"/>).</summary>
    public IRecordStore Store { get; init; } = null!;
    /// <summary>MyCRMRecords' own records (people, roles, the audit log, task runs, notes, the changes made in MyCRM).</summary>
    public IRecordStore Own { get; init; } = null!;
    /// <summary>MyCRM's tables, when the app reads them there (null in the tests that keep MyCRM's records in memory).</summary>
    public MyCrmStore? MyCrm { get; init; }
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
    public Func<DateTime> Clock { get; init; } = null!;

    /// <summary>MyOrders' and MyCRM's tables for the costing sheets and request forms: read only, never written.</summary>
    public IMyOrdersData OrdersData { get; init; } = null!;
    /// <summary>Who sees which costing sheets and request forms (MyOrders' rule, by OutSystems user).</summary>
    public OrdersAccess OrdersAccess { get; init; } = null!;
    public CostingSheets CostingSheets { get; init; } = null!;
    public RequestForms RequestForms { get; init; } = null!;

    /// <summary>MyFiles' files in the OutSystems database: read only, never written.</summary>
    public IMyFilesData MyFilesData { get; init; } = null!;
    /// <summary>The Files page and a company's Files tab (MyCRMRecords' company rule decides who sees which).</summary>
    public MyFiles MyFiles { get; init; } = null!;

    /// <summary>Advance's machines through AdvanceConnector: read only, never written.</summary>
    public IAdvanceMachines AdvanceMachines { get; init; } = null!;
    /// <summary>MyMachines' temporary access (OutSystems), changed only when switched on.</summary>
    public IMyMachinesAccess MachineAccess { get; init; } = null!;
    /// <summary>The Machines pages, a company's Machines tab and the Machine access page.</summary>
    public Machines Machines { get; init; } = null!;

    /// <summary>Whether MyCRMRecords may change MyCRM's data now (the administrator's switch).</summary>
    public bool CanWrite => MyCrm is null || MyCrm.CanWrite;
}

/// <summary>Builds MyCRMRecords's objects, the same for the web site and the tests.</summary>
public static class CrmFactory
{
    /// <param name="store">MyCRMRecords' own records.</param>
    /// <param name="myCrm">Where MyCRM's records are (tests: in memory); else MyCRM's tables in the OutSystems database.</param>
    /// <param name="temporary">MyCRM's temporary territories (tests); else read from the OutSystems database.</param>
    /// <param name="orders">MyOrders' and MyCRM's tables for the costing sheets (tests: in memory); else read in the OutSystems database.</param>
    /// <param name="files">MyFiles' files (tests: in memory); else read in the OutSystems database.</param>
    /// <param name="machines">Advance's machines (tests: in memory); else read through AdvanceConnector.</param>
    /// <param name="machineAccess">MyMachines' temporary access (tests: in memory); else in the OutSystems database.</param>
    /// <param name="domain">The Windows domain of the logins (RSP), for OutSystems' usernames.</param>
    public static CrmApp Create(IRecordStore store, SettingsFile<CrmSettings> settingsFile, Func<string, DbConnection> connectionFactory,
        string schedulerStateFile, Func<DateTime> clock, IEmailSender? emailSender = null, IRecordStore? myCrm = null, ITemporaryTerritories? temporary = null,
        IMyOrdersData? orders = null, string domain = "RSP", IMyFilesData? files = null,
        IAdvanceMachines? machines = null, IMyMachinesAccess? machineAccess = null)
    {
        Func<CrmSettings> settings = settingsFile.Load;
        var databases = new ExternalDatabases(settings, connectionFactory);
        var own = store;
        var tables = myCrm is null ? new MyCrmStore(own, databases, () => settings().Connections.CrmModule, () => settings().MyCrm.WritesEnabled, clock) : null;
        Func<ITemporaryTerritories?> temporaryTerritories = () => temporary ?? (tables is not null && tables.Available ? tables : null);
        store = new RoutedStore(own, myCrm ?? tables!);
        var mailer = new Mailer(store, settings, clock, emailSender);
        var people = new People(store, clock);
        var access = new Access(store, temporaryTerritories);
        var territories = new Territories(store, people, clock);
        var customerNumbers = new CustomerNumbers(store, databases, settings);
        orders ??= new SqlMyOrdersData(databases, () => settings().Connections, domain);
        var ordersAccess = new OrdersAccess(orders);
        files ??= new SqlMyFilesData(databases, () => settings().Connections);
        machines ??= new SqlAdvanceMachines(databases, () => settings().Connections);
        machineAccess ??= new SqlMyMachinesAccess(databases, () => settings().Connections, () => settings().MyMachines.WritesEnabled, own, clock, domain);
        return new CrmApp
        {
            Store = store,
            Own = own,
            MyCrm = tables,
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
                new Dictionary<string, Func<string, JobOutcome>> { [Jobs.CustomerNumbers] = customerNumbers.Run }, clock),
            Clock = clock,
            OrdersData = orders,
            OrdersAccess = ordersAccess,
            CostingSheets = new CostingSheets(orders, ordersAccess),
            RequestForms = new RequestForms(orders, ordersAccess),
            MyFilesData = files,
            MyFiles = new MyFiles(files, access, store, clock),
            AdvanceMachines = machines,
            MachineAccess = machineAccess,
            Machines = new Machines(machines, machineAccess, access, store, clock),
        };
    }
}
