using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace MyCRMRecords.Data;

/// <summary>A setting kept encrypted in the settings file (Windows DPAPI for the server) and never sent to the browser.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SecretAttribute : Attribute;

public static class Environments
{
    public const string Production = "Production";
    public const string Test = "Test";
    public static readonly string[] All = [Production, Test];
}

/// <summary>
/// Everything an administrator sets on the admin page, apart from where MyCRMRecords keeps its own data
/// (<see cref="DatabaseSettings"/>): App_Data/crm-records-settings.json, which deployments never replace. Everything that sends
/// or changes something outside MyCRMRecords starts off (the sync with MyCRM, the emails).
/// </summary>
public sealed class CrmSettings
{
    public ConnectionSettings Connections { get; set; } = new();
    /// <summary>Whether MyCRMRecords may change MyCRM's data (off until an administrator turns it on).</summary>
    public MyCrmSettings MyCrm { get; set; } = new();
    /// <summary>Whether MyCRMRecords may give and remove MyMachines' temporary access (off until an administrator turns it on).</summary>
    public MyMachinesSettings MyMachines { get; set; } = new();
    public EmailSettings Email { get; set; } = new();
    public JobSettings Jobs { get; set; } = new();
    public string? ChangedBy { get; set; }
    public DateTimeOffset? ChangedAt { get; set; }
}

/// <summary>MyCRM's data, which MyCRMRecords reads and writes in MyCRM's own tables.</summary>
public sealed class MyCrmSettings
{
    /// <summary>MyCRMRecords may change MyCRM's data (off until an administrator turns it on): until then it only reads.</summary>
    public bool WritesEnabled { get; set; }

    /// <summary>MyCRM's address, for the "Open in MyCRM" links (a company's prospects are changed there).</summary>
    public string Url { get; set; } = "https://apps.ricoh.sg/MyCRM/";
}

/// <summary>MyMachines' temporary access (its entity TempAccess), which the Machine access page reads and may change.</summary>
public sealed class MyMachinesSettings
{
    /// <summary>
    /// MyCRMRecords may add and mark rows of MyMachines' TempAccess (off until an administrator turns it on): until then
    /// the Machine access page only lists them.
    /// </summary>
    public bool WritesEnabled { get; set; }
}

/// <summary>The other databases MyCRMRecords reads and (MyCRM's tables, when switched on) writes.</summary>
public sealed class ConnectionSettings
{
    /// <summary>The OutSystems platform database: MyCRM's tables, MyOrders' (read only), its users and roles.</summary>
    public ExternalDatabase OutSystems { get; set; } = new();

    /// <summary>AdvanceConnector: Advance's customers, for the customer numbers (read only).</summary>
    public ExternalDatabase AdvanceConnector { get; set; } = new();

    public string CrmModule { get; set; } = "MyCRM";

    /// <summary>The OutSystems module with the costing sheets and request forms (read only).</summary>
    public string OrdersModule { get; set; } = "MyOrders";

    /// <summary>The OutSystems module with MyFiles' files (read only, the Files page), as MyFile's FilesModule.</summary>
    public string FilesModule { get; set; } = "MyFiles";

    /// <summary>The OutSystems module with MyMachines' temporary access (TempAccess), for who sees a company's machines.</summary>
    public string MachinesModule { get; set; } = "MyMachines";

    public AdvanceTables AdvanceTables { get; set; } = new();
}

/// <summary>
/// The names of the tables MyCRMRecords reads in AdvanceConnector (MyCRM's AdvanceConnector extension entities). A table in
/// another database on the same server is written with the database first (Advance.dbo.Customer_Supp); a table whose
/// own name has a dot, in brackets.
/// </summary>
public sealed class AdvanceTables
{
    /// <summary>MyCRM's entity Customer_Supp (Business_reg, Cust_no): Advance's customer number of each BRN.</summary>
    public string CustomerSupp { get; set; } = "dbo.Customer_Supp";

    /// <summary>MyMachines' daily_mif: Advance's machines in the field, a row per machine, with its customer, BRN, contract and warranty.</summary>
    public string DailyMif { get; set; } = "dbo.daily_mif";

    /// <summary>MyMachines' serial: a machine's install date, location and Serial-stat (the finance type).</summary>
    public string Serial { get; set; } = "dbo.serial";

    /// <summary>MyMachines' customer: Advance's customers (name, address, telephone).</summary>
    public string Customer { get; set; } = "dbo.customer";

    /// <summary>MyMachines' meter_card: the copies billed per invoice and meter type.</summary>
    public string MeterCard { get; set; } = "dbo.meter_card";

    /// <summary>MyMachines' Service: the service orders (SO-status F90 a service call done, P90 a toner order).</summary>
    public string Service { get; set; } = "dbo.Service";

    /// <summary>MyMachines' SMeter_History: the meter readings taken on a service call.</summary>
    public string SMeterHistory { get; set; } = "dbo.SMeter_History";

    /// <summary>MyMachines' daily_TM: the last contract's rates of a machine now on time and materials.</summary>
    public string DailyTm { get; set; } = "dbo.daily_TM";

    /// <summary>MyCRM's entity CustomerMapping: Advance's customer number (Cust_No) of each MyCRM company (Company_ID, MyCRM's Company Id).</summary>
    public string CustomerMapping { get; set; } = "dbo.CustomerMapping";

    /// <summary>MyCRM's entity Email_Master: where a customer's invoices and statements (SOA) are e-mailed, per contract.</summary>
    public string EmailMaster { get; set; } = "dbo.Email_Master";
}

public sealed class ExternalDatabase
{
    public bool Configured { get; set; }
    public SqlServerSettings SqlServer { get; set; } = new();
}

/// <summary>The SMTP server the emails go through. Off: emails are only written to the email log.</summary>
public sealed class EmailSettings
{
    public bool Enabled { get; set; }
    public string Server { get; set; } = "172.19.109.20";
    public int Port { get; set; } = 25;
    public string UserName { get; set; } = "";
    [Secret] public string Password { get; set; } = "";
    public string From { get; set; } = "rspapps@ricoh.sg";
    public string FromName { get; set; } = "MyCRMRecords";
    /// <summary>The site's address, for the links in the emails.</summary>
    public string SiteUrl { get; set; } = "https://apps.ricoh.sg/MyCRMRecords/";
    /// <summary>Every email goes to this address only (for trying it out); blank to send to the real people.</summary>
    public string RedirectTo { get; set; } = "";

    public string LinkTo(string page) => SiteUrl.Trim().TrimEnd('/') + "/" + page;
}

/// <summary>The scheduled tasks: all off until an administrator turns them on.</summary>
public sealed class JobSettings
{
    public bool Enabled { get; set; }
    public List<JobSchedule> Schedules { get; set; } = [];

    public JobSchedule For(string job) => Schedules.FirstOrDefault(s => s.Job == job) ?? Jobs.Default(job);
}

public sealed class JobSchedule
{
    public string Job { get; set; } = "";
    public bool Enabled { get; set; }
    /// <summary>Every <see cref="EveryMinutes"/> minutes, or daily at <see cref="DailyAt"/> (HH:mm, Singapore time).</summary>
    public int EveryMinutes { get; set; }
    public string DailyAt { get; set; } = "";
    /// <summary>The days a daily task runs, as numbers (0 Sunday to 6 Saturday) separated by commas; blank: every day.</summary>
    public string Days { get; set; } = "";
}

/// <summary>MyCRMRecords's scheduled tasks (the sync with MyCRM keeps its own timing).</summary>
public static class Jobs
{
    /// <summary>MyCRM's timer UpdateCustomerNo: Advance's customer number of each company's BRN, daily at 02:45.</summary>
    public const string CustomerNumbers = "customer-numbers";
    /// <summary>MyCRM's timer SetInactiveAllProjectThatEnds (with FixTerritory): projects past their end date close, daily at 05:30.</summary>
    public const string ProjectsEnded = "projects-ended";

    public static readonly string[] All = [CustomerNumbers, ProjectsEnded];

    public static JobSchedule Default(string job) => job switch
    {
        CustomerNumbers => new JobSchedule { Job = job, Enabled = false, DailyAt = "02:45" },
        ProjectsEnded => new JobSchedule { Job = job, Enabled = false, DailyAt = "05:30" },
        _ => throw new ArgumentException("No job " + job),
    };

    public static string Title(string job) => job switch
    {
        CustomerNumbers => "Customer numbers from Advance",
        ProjectsEnded => "Projects that have ended",
        _ => job,
    };
}

/// <summary>
/// Keeps an object in a JSON file with its <see cref="SecretAttribute"/> text encrypted (DPAPI on the server); a
/// secret that cannot be decrypted (the file came from another server) reads as empty.
/// </summary>
public sealed class SettingsFile<T>(string path, ISecretProtector protector) where T : class, new()
{
    private readonly string _path = System.IO.Path.GetFullPath(path);
    private static readonly object Gate = new();

    public string Path => _path;

    public T Load()
    {
        lock (Gate)
        {
            if (!File.Exists(_path)) return new T();
            var settings = Json.Deserialize<T>(File.ReadAllText(_path)) ?? new T();
            Transform(settings, protect: false);
            return settings;
        }
    }

    public void Save(T settings)
    {
        lock (Gate)
        {
            var copy = Json.Clone(settings);
            Transform(copy, protect: true);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, Json.Serialize(copy, indented: true));
            if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);
        }
    }

    /// <summary>Loads, changes and saves the settings as one step.</summary>
    public T Change(Action<T> change)
    {
        lock (Gate)
        {
            var settings = Load();
            change(settings);
            Save(settings);
            return settings;
        }
    }

    private void Transform(object target, bool protect)
    {
        foreach (var property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite))
        {
            var value = property.GetValue(target);
            if (value is null) continue;
            if (property.PropertyType == typeof(string))
            {
                if (property.GetCustomAttribute<SecretAttribute>() is null) continue;
                var text = (string)value;
                property.SetValue(target, text.Length == 0 ? "" : protect ? protector.Protect(text) : Unprotect(text));
            }
            else if (value is IList list && property.PropertyType.IsGenericType)
            {
                foreach (var item in list) if (item is not null && !item.GetType().IsValueType && item is not string) Transform(item, protect);
            }
            else if (property.PropertyType.IsClass && property.PropertyType.Namespace?.StartsWith("MyCRMRecords", StringComparison.Ordinal) == true)
            {
                Transform(value, protect);
            }
        }
    }

    private string Unprotect(string text)
    {
        try
        {
            return protector.Unprotect(text);
        }
        catch (Exception)
        {
            return "";
        }
    }
}

/// <summary>A settings object as the admin page sees it: every secret replaced by whether it is set.</summary>
public static class SettingsView
{
    public static JToken Of(object settings)
    {
        var json = JToken.FromObject(settings, Newtonsoft.Json.JsonSerializer.Create(Json.Settings));
        Hide(settings, json);
        return json;
    }

    private static void Hide(object target, JToken json)
    {
        if (json is not JObject obj) return;
        foreach (var property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
            var value = property.GetValue(target);
            if (property.GetCustomAttribute<SecretAttribute>() is not null)
            {
                obj.Remove(name);
                obj[name + "Set"] = value is string s && s.Length > 0;
            }
            else if (value is IList list && obj[name] is JArray array)
            {
                for (var i = 0; i < list.Count && i < array.Count; i++) if (list[i] is { } item && item is not string) Hide(item, array[i]);
            }
            else if (value is not null && property.PropertyType.IsClass && property.PropertyType != typeof(string) && obj[name] is JObject child)
            {
                Hide(value, child);
            }
        }
    }
}
