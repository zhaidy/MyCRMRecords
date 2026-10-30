using Newtonsoft.Json.Linq;

namespace MyCRMRecords.Tests;

/// <summary>
/// Dormant opportunities: the old prospects and visited companies worth a second look, in the person's (or their team's)
/// territories, what they decided about each, and the Excel download with everything.
/// </summary>
public sealed class DormantTests
{
    private sealed class Scene
    {
        public Company Lost = null!, Stale = null!, Fresh = null!, Won = null!, Cancelled = null!, Reopened = null!, Visited = null!, Called = null!, KenCo = null!;
        public Prospect LostProspect = null!;
    }

    private static DateTime Day(int year, int month, int day) => new(year, month, day);

    private static Prospect Prospect(TestWorld world, Company company, string stage, string status, DateTime created, Action<Prospect>? more = null)
    {
        var p = new Prospect { CompanyId = company.Id, Stage = stage, ProspectStatus = status, DateCreated = created, IsActive = true, ProspectType = "NEW" };
        more?.Invoke(p);
        return world.Store.Insert(p);
    }

    private static void Visit(TestWorld world, Company company, DateTime day, params string[] types)
    {
        var activity = world.Store.Insert(new Activity { CompanyId = company.Id, VisitDate = day, IsActive = true, Owner = TestWorld.Sam.Login });
        foreach (var type in types) world.Store.Insert(new ActivityTypeLink { CompanyActivityId = activity.Id, ActivityType = type });
    }

    /// <summary>Today is 17 Oct 2026. Sam's North territory has the first eight companies; Ken's has the last.</summary>
    private static Scene Build(TestWorld world)
    {
        var s = new Scene
        {
            Lost = world.Company("Lost Co", "L1"), Stale = world.Company("Stale Co", "S1"), Fresh = world.Company("Fresh Co", "F1"), Won = world.Company("Won Co", "W1"),
            Cancelled = world.Company("Cancelled Co", "C1"), Reopened = world.Company("Reopened Co", "R1"), Visited = world.Company("Visited Co", "V1"),
            Called = world.Company("Called Co", "K1"), KenCo = world.Company("Ken Co", "N1"),
        };
        world.Territory("North", 1, TestWorld.Sam, s.Lost.Id, s.Stale.Id, s.Fresh.Id, s.Won.Id, s.Cancelled.Id, s.Reopened.Id, s.Visited.Id, s.Called.Id);
        world.Territory("South", 2, TestWorld.Ken, s.KenCo.Id);
        // Lost at the end of a negotiation: High.
        s.LostProspect = Prospect(world, s.Lost, "CloseLost", "Lost", Day(2024, 6, 1), p => { p.DateClosed = Day(2024, 11, 5); p.TotalPrice = 42000; });
        Visit(world, s.Lost, Day(2024, 9, 1), "FollowUp");
        Visit(world, s.Lost, Day(2024, 10, 1), "Negotiation", "FollowUp");
        // Open, untouched for more than 90 days, as far as a demo: Medium.
        Prospect(world, s.Stale, "Prove", "InProgress", Day(2024, 5, 1), p => p.Forecast = 9000);
        Visit(world, s.Stale, Day(2024, 6, 1), "Demo_POC");
        // Open with an activity last week, won, and cancelled ones are not dormant.
        Prospect(world, s.Fresh, "Prove", "InProgress", Day(2026, 1, 1));
        Visit(world, s.Fresh, Day(2026, 10, 10), "FollowUp");
        Prospect(world, s.Won, "Close", "Won", Day(2024, 1, 1), p => p.IsClosed = true);
        Prospect(world, s.Cancelled, "CloseLost", "Lost", Day(2024, 1, 1), p => p.IsCancelled = true);
        // A lost prospect the company has made a newer open one after.
        Prospect(world, s.Reopened, "CloseLost", "Lost", Day(2024, 1, 1));
        Prospect(world, s.Reopened, "Qualify", "InProgress", Day(2026, 9, 1));
        // Visited with no prospect: a demo is worth a look, a first call alone is not.
        Visit(world, s.Visited, Day(2025, 3, 1), "Demo_POC");
        Visit(world, s.Called, Day(2025, 3, 1), "NewCall");
        // Another team's.
        Prospect(world, s.KenCo, "CloseLost", "Lost", Day(2024, 2, 1), p => p.DateClosed = Day(2024, 3, 1));
        return s;
    }

    private static Dormant.Filter Filter(string list = "prospects", string decision = "todo", string owner = "team") => new() { List = list, Decision = decision, Owner = owner };

    private static string[] Names(Dormant.Page page) => page.Rows.Select(r => r.Company).ToArray();

    [Fact]
    public void Lost_and_untouched_prospects_and_visited_companies_are_listed_best_first()
    {
        using var world = new TestWorld();
        var s = Build(world);
        var sam = world.Actor(TestWorld.Sam);
        var prospects = world.D.Dormant.List(sam, Filter());
        // Not the fresh, won, cancelled or reopened ones, and not Ken's.
        Assert.Equal(new[] { "Lost Co", "Stale Co" }, Names(prospects));
        var lost = prospects.Rows[0];
        Assert.Equal(("High", "Negotiation", 42000m, "North"), (lost.Priority, lost.Furthest, lost.Value, lost.Territory));
        Assert.Equal("Lost, type NEW, Nov 2024", lost.Why);
        Assert.Equal(2, lost.Activities);
        Assert.Equal(new[] { "Negotiation", "Follow Up" }, lost.History.Select(h => h.Label));
        var stale = prospects.Rows[1];
        Assert.Equal(("Medium", "Open and untouched since Jun 2024", 9000m), (stale.Priority, stale.Why, stale.Value));
        Assert.Equal(new[] { "Visited Co" }, Names(world.D.Dormant.List(sam, Filter("visited"))));
        Assert.Equal(new Dormant.Counts(2, 1, 2, 0, 0, 0, 51000m), prospects.Counts);
    }

    [Fact]
    public void A_salesperson_sees_their_territories_and_a_manager_their_teams()
    {
        using var world = new TestWorld();
        var s = Build(world);
        Assert.Equal(new[] { "Ken Co" }, Names(world.D.Dormant.List(world.Actor(TestWorld.Ken), Filter())));
        // Mia manages Sam: the team's, or one person's.
        var mia = world.Actor(TestWorld.Mia);
        Assert.Equal(new[] { "Lost Co", "Stale Co" }, Names(world.D.Dormant.List(mia, Filter())));
        Assert.Empty(world.D.Dormant.List(mia, Filter(owner: "me")).Rows);
        Assert.Equal(2, world.D.Dormant.List(mia, Filter(owner: TestWorld.Sam.Login)).Total);
        Assert.Contains(world.D.Dormant.Team(mia), p => p.Login == TestWorld.Sam.Login);
        Assert.DoesNotContain(world.D.Dormant.Team(mia), p => p.Login == TestWorld.Mia.Login);
        // A territory filter.
        var north = world.Store.Find<Territory>().Single(t => t.Name == "North");
        var filter = Filter();
        filter.Territory = north.Id + 99;
        Assert.Empty(world.D.Dormant.List(mia, filter).Rows);
        filter.Territory = north.Id;
        Assert.Equal(2, world.D.Dormant.List(mia, filter).Total);
    }

    [Fact]
    public void The_filters_narrow_the_list_and_the_download_never_follows_them()
    {
        using var world = new TestWorld();
        Build(world);
        var sam = world.Actor(TestWorld.Sam);
        var filter = Filter();
        filter.Reached = 5;
        Assert.Equal(new[] { "Lost Co" }, Names(world.D.Dormant.List(sam, filter)));
        filter = Filter();
        filter.LookBackYears = 1;
        Assert.Empty(world.D.Dormant.List(sam, filter).Rows);
        filter = Filter();
        filter.Q = "stale";
        Assert.Equal(new[] { "Stale Co" }, Names(world.D.Dormant.List(sam, filter)));
        filter = Filter();
        filter.Type = "R2R";
        Assert.Empty(world.D.Dormant.List(sam, filter).Rows);
        // The download: everything in the person's scope, both lists, with the history, whatever the page's filters.
        var query = new Dictionary<string, string> { ["reached"] = "6", ["years"] = "1", ["q"] = "nothing", ["decision"] = "snoozed" };
        var file = new FilesApi(world.D).Download(FilesApi.DormantList, query, TestWorld.Sam);
        Assert.Equal(200, file.Status);
        Assert.Equal("DormantOpportunities.xlsx", file.FileName);
        using var stream = new MemoryStream(file.Content);
        var rows = MyCRMRecords.Documents.Xlsx.Read(stream);
        Assert.Equal(new[] { "Lost Co", "Stale Co" }, rows.Select(r => r["Company"]));
        Assert.Contains("Negotiation", rows[0]["Activity history"]);
        Assert.Equal("To follow up", rows[0]["Decision"]);
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(file.Content)))
            Assert.Equal(2, zip.Entries.Count(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_decision_moves_the_row_to_its_tab_and_a_new_one_replaces_it_without_deleting()
    {
        using var world = new TestWorld();
        var s = Build(world);
        var sam = world.Actor(TestWorld.Sam);
        Dormant.DecisionInput Input(string decision, string? reason = null, string? until = null) =>
            new() { Kind = DormantKinds.Prospect, RecordId = s.LostProspect.Id, Decision = decision, Reason = reason, SnoozeUntil = until };
        // No opportunity needs a reason, snooze a day after today.
        Assert.Contains(world.D.Dormant.Decide(sam, Input(DormantDecisions.NoOpportunity)).Errors, e => e.Field == "reason");
        Assert.Contains(world.D.Dormant.Decide(sam, Input(DormantDecisions.Snoozed, until: "2026-10-17")).Errors, e => e.Field == "snoozeUntil");
        Assert.Contains(world.D.Dormant.Decide(sam, Input(DormantDecisions.Snoozed)).Errors, e => e.Field == "snoozeUntil");
        Assert.Empty(world.Store.Find<DormantDecision>());
        Assert.True(world.D.Dormant.Decide(sam, Input(DormantDecisions.NoOpportunity, "  Bought a competitor's machine  ")).Ok);
        var tab = world.D.Dormant.List(sam, Filter(decision: "noopportunity"));
        Assert.Equal(new[] { "Lost Co" }, Names(tab));
        Assert.Equal(("Bought a competitor's machine", "Sam Sales"), (tab.Rows[0].Reason, tab.Rows[0].DecidedBy));
        Assert.Contains(world.D.Dormant.Decide(sam, Input(DormantDecisions.NoOpportunity, new string('x', Dormant.MaxReason + 1))).Errors, e => e.Field == "reason");
        Assert.Equal(new[] { "Stale Co" }, Names(world.D.Dormant.List(sam, Filter())));
        // Snooze it: hidden until the day, back after.
        Assert.True(world.D.Dormant.Decide(sam, Input(DormantDecisions.Snoozed, until: "2026-12-01")).Ok);
        Assert.Equal(new[] { "Lost Co" }, Names(world.D.Dormant.List(sam, Filter(decision: "snoozed"))));
        Assert.Equal(new DateTime(2026, 12, 1), world.D.Dormant.List(sam, Filter(decision: "snoozed")).Rows[0].SnoozeUntil);
        // After the day it is to follow up again (and Reopened Co's newer prospect has been quiet for 90 days by now).
        world.Now = new DateTime(2026, 12, 2, 9, 0, 0);
        Assert.Equal(new[] { "Lost Co", "Stale Co", "Reopened Co" }, Names(world.D.Dormant.List(sam, Filter())));
        // Followed up: the earlier decisions are kept, inactive.
        Assert.True(world.D.Dormant.Decide(sam, Input(DormantDecisions.FollowedUp)).Ok);
        var all = world.Store.Find<DormantDecision>();
        Assert.Equal(3, all.Count);
        Assert.Equal(new[] { false, false, true }, all.OrderBy(d => d.Id).Select(d => d.IsActive));
        Assert.Equal(new[] { "Lost Co" }, Names(world.D.Dormant.List(sam, Filter(decision: "followedup"))));
        // The download shows who decided and what.
        var rows = MyCRMRecords.Documents.Xlsx.Read(new MemoryStream(new FilesApi(world.D).Download(FilesApi.DormantList, new Dictionary<string, string>(), TestWorld.Sam).Content));
        Assert.Equal(("Followed up", "Sam Sales"), (rows[0]["Decision"], rows[0]["Decided by"]));
    }

    [Fact]
    public void Only_who_may_decide_decides_for_a_company_in_their_territories()
    {
        using var world = new TestWorld();
        var s = Build(world);
        var input = new Dormant.DecisionInput { Kind = DormantKinds.Prospect, RecordId = s.LostProspect.Id, Decision = DormantDecisions.FollowedUp };
        // Ken has no part in Sam's company; Marketing reads but does not decide; nothing is made up.
        Assert.Equal(ResultCodes.Forbidden, world.D.Dormant.Decide(world.Actor(TestWorld.Ken), input).Code);
        Assert.Equal(ResultCodes.Forbidden, world.D.Dormant.Decide(world.Actor(TestWorld.Mark), input).Code);
        Assert.Equal(ResultCodes.Validation, world.D.Dormant.Decide(world.Actor(TestWorld.Sam), new Dormant.DecisionInput { Kind = "Other", RecordId = 1, Decision = "FollowedUp" }).Code);
        Assert.Equal(ResultCodes.Validation, world.D.Dormant.Decide(world.Actor(TestWorld.Sam), new Dormant.DecisionInput { Kind = DormantKinds.Company, RecordId = s.Visited.Id, Decision = "Done" }).Code);
        Assert.Equal(ResultCodes.NotFound, world.D.Dormant.Decide(world.Actor(TestWorld.Sam), new Dormant.DecisionInput { Kind = DormantKinds.Prospect, RecordId = 9999, Decision = "FollowedUp" }).Code);
        Assert.Empty(world.Store.Find<DormantDecision>());
        // A company visited with no prospect is decided by the company; the manager of the team may too.
        Assert.True(world.D.Dormant.Decide(world.Actor(TestWorld.Mia), new Dormant.DecisionInput { Kind = DormantKinds.Company, RecordId = s.Visited.Id, Decision = "FollowedUp" }).Ok);
        Assert.Equal(new[] { "Visited Co" }, Names(world.D.Dormant.List(world.Actor(TestWorld.Sam), Filter("visited", "followedup"))));
        Assert.Empty(world.D.Dormant.List(world.Actor(TestWorld.Sam), Filter("visited")).Rows);
    }

    [Fact]
    public void The_api_lists_decides_and_keeps_the_filters_in_the_address()
    {
        using var world = new TestWorld();
        var s = Build(world);
        var api = new ProjectsApi(world.D);
        var list = JObject.Parse(api.Handle("GET", "dormant", "", new Dictionary<string, string> { ["list"] = "prospects", ["owner"] = "me" }, TestWorld.Sam).Json);
        Assert.Equal(2, (int)list["total"]!);
        Assert.Equal("Lost Co", (string)list["rows"]![0]!["company"]!);
        Assert.Equal("High", (string)list["rows"]![0]!["priority"]!);
        Assert.Equal(2, (int)list["counts"]!["prospectsToFollowUp"]!);
        Assert.True((bool)list["canDecide"]!);
        Assert.False((bool)JObject.Parse(api.Handle("GET", "dormant", "", new Dictionary<string, string>(), TestWorld.Mark).Json)["canDecide"]!);
        var body = MyCRMRecords.Json.Serialize(new { kind = "Prospect", recordId = s.LostProspect.Id, decision = "NoOpportunity", reason = "Closed down" });
        Assert.Equal(200, api.Handle("POST", "dormant/decision", body, new Dictionary<string, string>(), TestWorld.Sam).Status);
        Assert.Equal(400, api.Handle("POST", "dormant/decision", MyCRMRecords.Json.Serialize(new { kind = "Prospect", recordId = s.LostProspect.Id, decision = "NoOpportunity" }), new Dictionary<string, string>(), TestWorld.Sam).Status);
        var done = JObject.Parse(api.Handle("GET", "dormant", "", new Dictionary<string, string> { ["decision"] = "noopportunity" }, TestWorld.Sam).Json);
        Assert.Equal("Closed down", (string)done["rows"]![0]!["reason"]!);
    }
}
