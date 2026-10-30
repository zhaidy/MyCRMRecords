using MyCRMRecords.Integration;
using Newtonsoft.Json.Linq;

namespace MyCRMRecords.Tests;

/// <summary>Stands in for Windows DPAPI: marks the text, so the tests can see it was protected.</summary>
public sealed class FakeProtector : ISecretProtector
{
    public string Protect(string plainText) => "protected:" + plainText;
    public string Unprotect(string protectedText) =>
        protectedText.StartsWith("protected:", StringComparison.Ordinal) ? protectedText.Substring(10) : throw new FormatException("Not protected here.");
}

/// <summary>Keeps the emails instead of sending them.</summary>
public sealed class FakeEmail : IEmailSender
{
    public List<(IReadOnlyList<string> To, IReadOnlyList<string> Cc, string Subject, IReadOnlyList<EmailAttachment> Attachments, string Html)> Sent { get; } = [];

    /// <summary>The address each email was sent from.</summary>
    public List<string> From { get; } = [];

    public void Send(EmailSettings settings, IReadOnlyList<string> to, IReadOnlyList<string> cc, string subject, string html, IReadOnlyList<EmailAttachment> attachments)
    {
        Sent.Add((to, cc, subject, attachments, html));
        From.Add(settings.From);
    }
}

/// <summary>
/// MyCRMRecords with its data in a temporary folder, a fixed clock (Saturday 17 October 2026, 09:30), email faked, and
/// MyCRM's records kept with its own (MyCRM's temporary territories in memory). With <c>crmUsers</c> (the default) the people have their
/// MyCRM roles: Sara is SalesAdmin, Sam Sales and Mia Manager (both in Mia's team), Ken Sales in his own team, Mark
/// Marketing, Dina MD and Tom Telemarketer; Olga has no CRM role. Rob is an administrator of MyCRMRecords (Web.config).
/// </summary>
public sealed class TestWorld : IDisposable
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "MyCRMRecordsTests", Guid.NewGuid().ToString("N"));
    public DateTime Now { get; set; } = new(2026, 10, 17, 9, 30, 0);
    public FakeEmail Email { get; } = new();
    public SettingsFile<CrmSettings> SettingsFile { get; }
    public JsonRecordStore Store { get; }
    public CrmApp D { get; }
    /// <summary>MyCRM's tables, for its temporary territories.</summary>
    public FakeOldTables OldApp { get; }
    /// <summary>MyFiles' files, in memory.</summary>
    public FakeMyFiles Files { get; } = new();
    /// <summary>Advance's machines and MyMachines' temporary access, in memory.</summary>
    public FakeAdvanceMachines Machines { get; } = new();
    public FakeMyMachinesAccess MachineAccess { get; } = new();

    public static readonly ApiUser Admin = new(@"RSP\root", "Rob Root", "rob@example.com", IsListedAdmin: true);
    public static readonly ApiUser SalesAdmin = new(@"RSP\sara", "Sara Admin", "sara@example.com");
    public static readonly ApiUser Sam = new(@"RSP\sam", "Sam Sales", "sam@example.com");
    public static readonly ApiUser Mia = new(@"RSP\mia", "Mia Manager", "mia@example.com");
    public static readonly ApiUser Ken = new(@"RSP\ken", "Ken Kwan", "ken@example.com");
    public static readonly ApiUser Mark = new(@"RSP\mark", "Mark Marketing", "mark@example.com");
    public static readonly ApiUser Dina = new(@"RSP\dina", "Dina Director", "dina@example.com");
    public static readonly ApiUser Tom = new(@"RSP\tom", "Tom Telesales", "tom@example.com");
    public static readonly ApiUser Olga = new(@"RSP\olga", "Olga Other", "olga@example.com");

    /// <summary>Mia's team (Mia, Sam) and Ken's team (Ken), when the world has CRM users.</summary>
    public long MiaTeam { get; private set; }
    public long KenTeam { get; private set; }

    public TestWorld(Action<CrmSettings>? configure = null, IRecordStore? store = null, FakeOldTables? oldTables = null, bool crmUsers = true)
    {
        Directory.CreateDirectory(Folder);
        OldApp = oldTables ?? FakeOldTables.MyCRM();
        SettingsFile = new SettingsFile<CrmSettings>(Path.Combine(Folder, "crm-records-settings.json"), new FakeProtector());
        SettingsFile.Change(s => s.Email.Enabled = true);
        if (configure is not null) SettingsFile.Change(configure);
        Store = new JsonRecordStore(Path.Combine(Folder, "data"));
        D = CrmFactory.Create(store ?? Store, SettingsFile, _ => throw new InvalidOperationException("No database in this test."),
            Path.Combine(Folder, "scheduled-tasks.json"), () => Now, Email, store ?? Store, OldApp, files: Files,
            machines: Machines, machineAccess: MachineAccess);
        foreach (var user in new[] { Admin, SalesAdmin, Sam, Mia, Ken, Mark, Dina, Tom, Olga }) D.People.Touch(user.Login, user.DisplayName, user.Email);
        if (!crmUsers) return;
        // Straight into the records.
        var records = store ?? Store;
        MiaTeam = records.Insert(new CrmTeam { Name = "Mia Manager" }).Id;
        KenTeam = records.Insert(new CrmTeam { Name = "Ken Kwan" }).Id;
        void Profile(ApiUser user, string role, long? team) => records.Insert(new CrmUser { Login = user.Login, Role = role, TeamId = team, CreatedAt = Now });
        Profile(SalesAdmin, CrmRoles.SalesAdmin, MiaTeam);
        Profile(Sam, CrmRoles.Sales, MiaTeam);
        Profile(Mia, CrmRoles.Manager, MiaTeam);
        Profile(Ken, CrmRoles.Sales, KenTeam);
        Profile(Mark, CrmRoles.Marketing, KenTeam);
        Profile(Dina, CrmRoles.Md, null);
        Profile(Tom, CrmRoles.Telemarketer, MiaTeam);
    }

    public Actor Actor(ApiUser user) => user.ToActor(D.People);

    /// <summary>A company made by the Sales Admin's form (asserting it was saved).</summary>
    public Company Company(string name, string brn = "")
    {
        var result = D.Companies.Save(Actor(Admin), 0, new Companies.CompanyInput { Name = name, Brn = brn, IsHomeUser = brn.Length == 0 });
        Assert.True(result.Ok, result.Message);
        return D.Companies.Get(result.Id!.Value)!;
    }

    /// <summary>A territory of the person (none: vacant), asserting it was saved.</summary>
    public Territory Territory(string name, long number, ApiUser? owner = null, params long[] companies)
    {
        var admin = Actor(Admin);
        var result = D.Territories.Save(admin, 0, new Territories.TerritoryInput { Name = name, Number = number, Owner = owner?.Login });
        Assert.True(result.Ok, result.Message);
        if (companies.Length > 0) Assert.True(D.Territories.AddCompanies(admin, result.Id!.Value, companies).Ok);
        return D.Territories.Get(result.Id!.Value)!;
    }

    public ApiResponse Call(string method, string path, object? body = null, ApiUser? user = null, Dictionary<string, string>? query = null) =>
        new CrmApi(D).Handle(method, path, body is null ? "" : MyCRMRecords.Json.Serialize(body), query ?? new Dictionary<string, string>(), user ?? Admin);

    public JObject Json(ApiResponse response) => JObject.Parse(response.Json);

    public void Dispose()
    {
        try { Directory.Delete(Folder, recursive: true); }
        catch (IOException) { }
    }
}
