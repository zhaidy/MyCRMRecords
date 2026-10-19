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

/// <summary>Public holidays for the tests.</summary>
public sealed class FakeHolidays : IHolidays
{
    public List<DateTime> List { get; } = [];
    public bool Available => true;
    public IReadOnlyCollection<DateTime> Dates() => List;
}

/// <summary>
/// MyCRMRecords with its data in a temporary folder, a fixed clock (Saturday 17 October 2026, 09:30), email faked, public
/// holidays in memory, and MyCRM's tables in memory. Fay is a Feedback Admin, Ben is BE, Hana is HR, Sam and Olga have no
/// role.
/// </summary>
public sealed class TestWorld : IDisposable
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "MyCRMRecordsTests", Guid.NewGuid().ToString("N"));
    public DateTime Now { get; set; } = new(2026, 10, 17, 9, 30, 0);
    public FakeEmail Email { get; } = new();
    public SettingsFile<CrmSettings> SettingsFile { get; }
    public JsonRecordStore Store { get; }
    public CrmApp D { get; }
    /// <summary>MyCRM's tables, for the sync and the import.</summary>
    public FakeOldTables OldApp { get; } = FakeOldTables.MyCRM();
    public FakeHolidays Holidays { get; } = new();

    public static readonly ApiUser Admin = new(@"RSP\root", "Rob Root", "rob@example.com", IsListedAdmin: true);
    public static readonly ApiUser FeedbackAdmin = new(@"RSP\fay", "Fay Admin", "fay@example.com");
    public static readonly ApiUser Be = new(@"RSP\ben", "Ben Be", "ben@example.com");
    public static readonly ApiUser Hr = new(@"RSP\hana", "Hana Hr", "hana@example.com");
    public static readonly ApiUser Sam = new(@"RSP\sam", "Sam Staff", "sam@example.com");
    public static readonly ApiUser Olga = new(@"RSP\olga", "Olga Other", "olga@example.com");

    public TestWorld(Action<CrmSettings>? configure = null, IRecordStore? store = null, IOldTables? oldTables = null)
    {
        Directory.CreateDirectory(Folder);
        SettingsFile = new SettingsFile<CrmSettings>(Path.Combine(Folder, "crm-records-settings.json"), new FakeProtector());
        SettingsFile.Change(s => s.Email.Enabled = true);
        if (configure is not null) SettingsFile.Change(configure);
        Store = new JsonRecordStore(Path.Combine(Folder, "data"));
        D = CrmFactory.Create(store ?? Store, SettingsFile, _ => throw new InvalidOperationException("No database in this test."),
            Path.Combine(Folder, "scheduled-tasks.json"), () => Now, Email, oldTables ?? OldApp, Holidays);
        D.Store.Batch(s =>
        {
            s.Insert(new UserRole { Login = FeedbackAdmin.Login, Role = Roles.FeedbackAdmin });
            s.Insert(new UserRole { Login = Be.Login, Role = Roles.Be });
            s.Insert(new UserRole { Login = Hr.Login, Role = Roles.Hr });
        });
        foreach (var user in new[] { FeedbackAdmin, Be, Hr, Sam, Olga }) D.People.Touch(user.Login, user.DisplayName, user.Email);
    }

    public Actor Actor(ApiUser user) => user.ToActor(D.People);

    public ApiResponse Call(string method, string path, object? body = null, ApiUser? user = null, Dictionary<string, string>? query = null) =>
        new CrmApi(D).Handle(method, path, body is null ? "" : MyCRMRecords.Json.Serialize(body), query ?? new Dictionary<string, string>(), user ?? Admin);

    public JObject Json(ApiResponse response) => JObject.Parse(response.Json);

    public void Dispose()
    {
        try { Directory.Delete(Folder, recursive: true); }
        catch (IOException) { }
    }
}
