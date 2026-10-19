using System.Data.Common;

namespace MyCRMRecords.Data;

public static class StorageProviders
{
    public const string JsonFile = "JsonFile";
    public const string SqlServer = "SqlServer";
    public static readonly string[] All = [JsonFile, SqlServer];
}

public static class SqlAuthentication
{
    /// <summary>Connects as the IIS application pool's Windows account.</summary>
    public const string Windows = "Windows";
    /// <summary>Connects with a SQL Server login name and password.</summary>
    public const string SqlLogin = "SqlLogin";
    public static readonly string[] All = [Windows, SqlLogin];
}

/// <summary>Where MyCRMRecords keeps its own data, as chosen on the admin page.</summary>
public sealed class DatabaseSettings
{
    public string Provider { get; set; } = StorageProviders.JsonFile;
    public SqlServerSettings SqlServer { get; set; } = new();
    /// <summary>Who last saved the settings, and when; null until the admin page saves them.</summary>
    public string? ChangedBy { get; set; }
    public DateTimeOffset? ChangedAt { get; set; }
}

public sealed class SqlServerSettings
{
    public string Server { get; set; } = "";
    public string Database { get; set; } = "";
    public string Authentication { get; set; } = SqlAuthentication.Windows;
    public string UserName { get; set; } = "";
    /// <summary>Plain text in memory only; the settings file keeps it encrypted.</summary>
    [Secret] public string Password { get; set; } = "";
    public bool Encrypt { get; set; }
    public bool TrustServerCertificate { get; set; }

    /// <summary>Field name → problem; empty when the settings can be used.</summary>
    public Dictionary<string, string> Validate()
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(Server)) errors["server"] = "Enter the SQL Server name, e.g. SQLSERVER01 or SQLSERVER01\\INSTANCE.";
        else if (Server.Length > 256 || Server.IndexOfAny(['\r', '\n']) >= 0) errors["server"] = "The server name is not valid.";
        if (string.IsNullOrWhiteSpace(Database)) errors["database"] = "Enter the database name.";
        else if (Database.Length > 128 || Database.IndexOfAny(['\r', '\n']) >= 0) errors["database"] = "The database name is not valid.";
        if (!SqlAuthentication.All.Contains(Authentication)) errors["authentication"] = "Choose Windows or SQL Server authentication.";
        if (Authentication == SqlAuthentication.SqlLogin)
        {
            if (string.IsNullOrWhiteSpace(UserName)) errors["userName"] = "Enter the SQL Server login name.";
            else if (UserName.Length > 128) errors["userName"] = "The login name is too long.";
            if (string.IsNullOrEmpty(Password)) errors["password"] = "Enter the password for the SQL Server login.";
        }
        return errors;
    }

    /// <summary>
    /// The ADO.NET connection string, using only keywords that System.Data.SqlClient (IIS, .NET Framework)
    /// and Microsoft.Data.SqlClient (tests) both understand. The builder quotes values, so no field can add keywords.
    /// </summary>
    public string ToConnectionString()
    {
        var builder = new DbConnectionStringBuilder
        {
            ["Data Source"] = Server.Trim(),
            ["Initial Catalog"] = Database.Trim(),
        };
        if (Authentication == SqlAuthentication.SqlLogin)
        {
            builder["User ID"] = UserName.Trim();
            builder["Password"] = Password;
        }
        else
        {
            builder["Integrated Security"] = "True";
        }
        builder["Encrypt"] = Encrypt ? "True" : "False";
        builder["TrustServerCertificate"] = TrustServerCertificate ? "True" : "False";
        builder["Application Name"] = "MyCRMRecords";
        builder["Connect Timeout"] = "15";
        return builder.ConnectionString;
    }
}

/// <summary>Encrypts the SQL Server password at rest (DPAPI on the IIS server).</summary>
public interface ISecretProtector
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}

/// <summary>
/// Reads and writes the database settings file (App_Data/database-settings.json on the server, which deployments
/// never replace). The password is stored only in encrypted form. A missing file means "use the JSON data file".
/// </summary>
public sealed class DatabaseSettingsFile(string path, ISecretProtector protector)
{
    private readonly string _path = Path.GetFullPath(path);

    public string FilePath => _path;

    public DatabaseSettings Load()
    {
        if (!File.Exists(_path)) return new DatabaseSettings();
        var stored = Json.Deserialize<StoredSettings>(File.ReadAllText(_path))
            ?? throw new InvalidDataException($"{_path} is empty.");
        var sql = stored.SqlServer ?? new StoredSqlServer();
        return new DatabaseSettings
        {
            Provider = StorageProviders.All.Contains(stored.Provider) ? stored.Provider! : StorageProviders.JsonFile,
            ChangedBy = stored.ChangedBy,
            ChangedAt = stored.ChangedAt,
            SqlServer = new SqlServerSettings
            {
                Server = sql.Server ?? "",
                Database = sql.Database ?? "",
                Authentication = SqlAuthentication.All.Contains(sql.Authentication) ? sql.Authentication! : SqlAuthentication.Windows,
                UserName = sql.UserName ?? "",
                Password = Unprotect(sql.ProtectedPassword),
                Encrypt = sql.Encrypt,
                TrustServerCertificate = sql.TrustServerCertificate,
            },
        };
    }

    /// <summary>
    /// A password that cannot be decrypted (say, a settings file copied from another server) reads as blank: the
    /// connection then fails with a clear message and an administrator can enter it again, instead of every page failing.
    /// </summary>
    private string Unprotect(string? protectedPassword)
    {
        if (string.IsNullOrEmpty(protectedPassword)) return "";
        try
        {
            return protector.Unprotect(protectedPassword!);
        }
        catch (Exception)
        {
            return "";
        }
    }

    public void Save(DatabaseSettings settings)
    {
        var sql = settings.SqlServer;
        var stored = new StoredSettings
        {
            Provider = settings.Provider,
            ChangedBy = settings.ChangedBy,
            ChangedAt = settings.ChangedAt,
            SqlServer = new StoredSqlServer
            {
                Server = sql.Server.Trim(),
                Database = sql.Database.Trim(),
                Authentication = sql.Authentication,
                UserName = sql.Authentication == SqlAuthentication.SqlLogin ? sql.UserName.Trim() : "",
                ProtectedPassword = sql.Authentication == SqlAuthentication.SqlLogin && sql.Password.Length > 0
                    ? protector.Protect(sql.Password) : null,
                Encrypt = sql.Encrypt,
                TrustServerCertificate = sql.TrustServerCertificate,
            },
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, Json.Serialize(stored, indented: true));
        if (File.Exists(_path)) File.Replace(temp, _path, null);
        else File.Move(temp, _path);
    }

    private sealed class StoredSettings
    {
        public string? Provider { get; set; }
        public StoredSqlServer? SqlServer { get; set; }
        public string? ChangedBy { get; set; }
        public DateTimeOffset? ChangedAt { get; set; }
    }

    private sealed class StoredSqlServer
    {
        public string? Server { get; set; }
        public string? Database { get; set; }
        public string? Authentication { get; set; }
        public string? UserName { get; set; }
        public string? ProtectedPassword { get; set; }
        public bool Encrypt { get; set; }
        public bool TrustServerCertificate { get; set; }
    }
}
