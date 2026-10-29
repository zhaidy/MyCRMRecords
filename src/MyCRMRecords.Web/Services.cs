using System.Collections.Concurrent;
using System.Configuration;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Hosting;
using MyCRMRecords.Api;
using MyCRMRecords.Data;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Web;

/// <summary>The application's shared objects, created on first use from Web.config and App_Data.</summary>
public static class Services
{
    public static string Domain => Auth.SignIn.DomainName;

    public static bool DemoTools => !string.Equals(ConfigurationManager.AppSettings["MyCRMRecords:DemoTools"], "false", StringComparison.OrdinalIgnoreCase);

    private static readonly Lazy<DatabaseStorage> LazyStorage = new(() =>
    {
        var dataFolder = ConfigurationManager.AppSettings["MyCRMRecords:DataFolder"];
        if (string.IsNullOrWhiteSpace(dataFolder)) dataFolder = "~/App_Data/data";
        var settingsFile = new DatabaseSettingsFile(MapPath("~/App_Data/database-settings.json"), new DpapiSecretProtector());
        var json = new JsonRecordStore(MapPath(dataFolder!));
        return new DatabaseStorage(settingsFile, json, connectionString => new SqlConnection(connectionString));
    });

    private static readonly Lazy<CrmApp> LazyApp = new(() =>
    {
        var settingsFile = new SettingsFile<CrmSettings>(MapPath("~/App_Data/crm-records-settings.json"), new DpapiSecretProtector());
        Func<string, System.Data.Common.DbConnection> connect = connectionString => new SqlConnection(connectionString);
        return CrmFactory.Create(LazyStorage.Value, settingsFile, connect, MapPath("~/App_Data/scheduled-tasks.json"), () => DateTime.Now,
            domain: Auth.SignIn.DomainName);
    });

    private static readonly Lazy<CrmApi> LazyApi = new(() => new CrmApi(LazyApp.Value, Auth.DirectorySearch.Find));
    private static readonly Lazy<OrdersApi> LazyOrdersApi = new(() => new OrdersApi(LazyApp.Value));
    private static readonly Lazy<FilesApi> LazyFilesApi = new(() => new FilesApi(LazyApp.Value));
    private static readonly Lazy<UsersApi> LazyUsersApi = new(() => new UsersApi(LazyApp.Value, Auth.DirectorySearch.Find));

    private static readonly Lazy<AdminApi> LazyAdminApi = new(() => new AdminApi(LazyApp.Value, LazyStorage.Value, DemoTools));

    // Build.ps1 writes the version marker and change log into each release; the server's poller writes the status.
    private static readonly Lazy<DeploymentApi> LazyDeploymentApi = new(() => new DeploymentApi(
        MapPath("~/Content/deployment-version.txt"), MapPath("~/bin/changelog.json"), MapPath("~/App_Data/deployment-status.json")));

    public static DatabaseStorage Store => LazyStorage.Value;
    public static CrmApp App => LazyApp.Value;
    public static CrmApi Api => LazyApi.Value;
    /// <summary>The costing sheets and request forms (read only from MyOrders).</summary>
    public static OrdersApi OrdersApi => LazyOrdersApi.Value;
    public static FilesApi FilesApi => LazyFilesApi.Value;
    public static UsersApi UsersApi => LazyUsersApi.Value;
    public static AdminApi AdminApi => LazyAdminApi.Value;
    public static DeploymentApi DeploymentApi => LazyDeploymentApi.Value;

    /// <summary>Starts the scheduled tasks' timer in this worker process (on every request; it does nothing after the first).</summary>
    public static void EnsureStarted()
    {
        try
        {
            LazyApp.Value.Scheduler.Start();
        }
        catch (Exception)
        {
            // The pages still open, so an administrator can fix the settings.
        }
    }

    /// <summary>Runs work after the answer is sent; IIS waits for it (up to 30 seconds) before recycling the process.</summary>
    public static void RunInBackground(Action work) =>
        HostingEnvironment.QueueBackgroundWorkItem(_ =>
        {
            try { work(); }
            catch (Exception) { /* Recorded by the work itself where it matters. */ }
        });

    /// <summary>Records an action in the audit log (dbo.CrAudit); a log that cannot be written never stops the action.</summary>
    public static void Record(HttpContext context, ApiUser? user, string action, string path, bool succeeded, string message,
        string details = "", string method = "POST", int status = 200, string target = "")
    {
        try
        {
            LazyStorage.Value.Insert(new AuditEntry
            {
                At = DateTime.Now,
                Login = Cut(user?.Login, 256),
                DisplayName = Cut(user?.DisplayName, 256),
                ImpersonatorLogin = Cut(user?.Impersonator?.Login, 256),
                ImpersonatorName = Cut(user?.Impersonator?.DisplayName, 256),
                Action = Cut(action, 200),
                Method = Cut(method, 10),
                Path = Cut(path, 500),
                Target = Cut(target, 500),
                Status = status,
                Succeeded = succeeded,
                Message = Cut(message, 4000),
                Details = details,
                Address = Cut(context.Request.UserHostAddress, 100),
            });
        }
        catch (Exception)
        {
            // The audit log is best effort.
        }
    }

    private static string Cut(string? text, int length) => Strings.Shorten(text ?? "", length);

    /// <summary>"~/" is the application folder; other paths are used as they are.</summary>
    private static string MapPath(string path) =>
        path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(HttpRuntime.AppDomainAppPath, path.Substring(2).Replace('/', Path.DirectorySeparatorChar))
            : path;
}

/// <summary>
/// Encrypts the stored passwords and keys with Windows DPAPI for this machine, so the settings files are useless elsewhere.
/// The settings files are read on every request (often several times), so each secret is decrypted once and kept.
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MyCRMRecords settings");
    private static readonly ConcurrentDictionary<string, string> Decrypted = new();

    public string Protect(string plainText) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.LocalMachine));

    public string Unprotect(string protectedText) =>
        Decrypted.GetOrAdd(protectedText, text =>
            Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(text), Entropy, DataProtectionScope.LocalMachine)));
}
