namespace MyCRMRecords.Tests;

/// <summary>
/// MyCRM's sales activities, logged and changed here: MyCRM's checks, the owner alone changes one, the links taken off are
/// kept without their activity or made inactive, nothing is deleted; and a company's prospects are only read.
/// </summary>
public sealed class ActivityTests
{
    public static ApiResponse Api(TestWorld world, string path, ApiUser user, Dictionary<string, string>? query = null) =>
        new ProjectsApi(world.D).Handle("GET", path, "", query ?? new Dictionary<string, string>(), user);

    private static Activities.ActivityInput Input(long company, params string[] types) => new()
    {
        CompanyId = company, Action = ActivityActions.FaceToFace, Remarks = "Review", StartDate = "2026-10-20", Types = types.ToList(),
    };

    [Fact]
    public void An_activity_needs_a_company_a_start_date_and_a_type_and_its_owner_takes_part()
    {
        using var world = new TestWorld();
        var tan = world.Company("Tan", "T1000");
        world.Territory("North", 1, TestWorld.Sam, tan.Id);
        var sam = world.Actor(TestWorld.Sam);
        var bad = world.D.Activities.Save(sam, 0, new Activities.ActivityInput { Action = "FaceToFace" });
        Assert.Contains(bad.Errors, e => e.Field == "company" && e.Message == "You must select a company.");
        Assert.Contains(bad.Errors, e => e.Field == "types" && e.Message == "At least one activity type must be set");
        Assert.Contains(bad.Errors, e => e.Field == "startDate");
        // An inactive action (Cold Call) is not offered.
        var coldCall = Input(tan.Id, "FollowUp");
        coldCall.Action = "ColdCall";
        Assert.Contains(world.D.Activities.Save(sam, 0, coldCall).Errors, e => e.Field == "action");
        // Ken may not log one for Sam's company.
        Assert.Contains(world.D.Activities.Save(world.Actor(TestWorld.Ken), 0, Input(tan.Id, "FollowUp")).Errors, e => e.Field == "company");
        var saved = world.D.Activities.Save(sam, 0, Input(tan.Id, "FollowUp", "Negotiation"));
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal("Company Activity has been saved for Tan.", saved.Message);
        var a = world.Store.Get<Activity>(saved.Id!.Value)!;
        // No times: now, and an hour later (MyCRM).
        Assert.Equal((TestWorld.Sam.Login, "09:30", "10:30", new DateTime(2026, 10, 20), new DateTime(2026, 10, 17)),
            (a.Owner, a.StartTime, a.EndTime, a.ToDate!.Value, a.CreatedDate!.Value));
        Assert.Equal(TestWorld.Sam.Login, world.Store.Find<ActivityParticipant>().Single().User);
        Assert.Equal(new[] { "FollowUp", "Negotiation" }, world.Store.Find<ActivityTypeLink>().Select(t => t.ActivityType));
        // Marketing reads but does not save (MyCRM hides Save).
        Assert.Equal(ResultCodes.Forbidden, world.D.Activities.Save(world.Actor(TestWorld.Mark), 0, Input(tan.Id, "FollowUp")).Code);
    }

    [Fact]
    public void Only_the_owner_changes_an_activity_and_what_is_taken_off_is_kept()
    {
        using var world = new TestWorld();
        var tan = world.Company("Tan", "T1000");
        world.Territory("North", 1, TestWorld.Sam, tan.Id);
        var contact = world.D.Companies.SaveContact(world.Actor(TestWorld.Admin), tan.Id, 0, new Companies.ContactInput { FirstName = "Mei", LastName = "Lim" });
        Assert.True(contact.Ok, contact.Message);
        var prospect = world.Store.Insert(new Prospect { CompanyId = tan.Id, Remarks = "6 MFPs", IsActive = true });
        var sam = world.Actor(TestWorld.Sam);
        var input = Input(tan.Id, "FollowUp", "Negotiation");
        input.Contacts = [contact.Id!.Value];
        input.Prospects = [prospect.Id];
        var id = world.D.Activities.Save(sam, 0, input).Id!.Value;
        Assert.True(world.D.Activities.Read(sam, id)!.CanEdit);
        // Mia (Sam's manager) reads it but may not change it.
        Assert.False(world.D.Activities.Read(world.Actor(TestWorld.Mia), id)!.CanEdit);
        Assert.Equal(ResultCodes.Forbidden, world.D.Activities.Save(world.Actor(TestWorld.Mia), id, input).Code);
        Assert.Equal(ResultCodes.Forbidden, world.D.Activities.Delete(world.Actor(TestWorld.Mia), id).Code);
        Assert.Null(world.D.Activities.Read(world.Actor(TestWorld.Ken), id));
        // Taken off: the type, the attendee and the prospect keep their rows without the activity.
        var changed = Input(tan.Id, "FollowUp");
        changed.Completed = true;
        Assert.True(world.D.Activities.Save(sam, id, changed).Ok);
        Assert.Equal(2, world.Store.Find<ActivityTypeLink>().Count);
        Assert.Equal(new[] { "FollowUp" }, world.Store.Find<ActivityTypeLink>(Query.Where("CompanyActivityId", id)).Select(t => t.ActivityType));
        Assert.Null(world.Store.Find<ActivityContact>().Single().CompanyActivityId);
        Assert.Null(world.Store.Find<ActivityProspect>().Single().CompanyActivityId);
        Assert.Equal("Completed", world.D.Activities.Read(sam, id)!.Status);
        // Deleted: inactive.
        Assert.Equal("Successfully Deleted.", world.D.Activities.Delete(sam, id).Message);
        Assert.False(world.Store.Get<Activity>(id)!.IsActive);
        Assert.Null(world.D.Activities.Get(id));
    }

    [Fact]
    public void The_Activities_page_lists_the_persons_and_their_teams_activities_by_when()
    {
        using var world = new TestWorld();
        var tan = world.Company("Tan", "T1000");
        world.Territory("North", 1, TestWorld.Sam, tan.Id);
        var sam = world.Actor(TestWorld.Sam);
        var past = Input(tan.Id, "FollowUp");
        past.StartDate = "2026-10-10";
        Assert.True(world.D.Activities.Save(sam, 0, past).Ok);
        var later = Input(tan.Id, "Demo_POC");
        later.StartDate = "2026-10-25";
        later.Remarks = "Demo";
        Assert.True(world.D.Activities.Save(sam, 0, later).Ok);
        Assert.Equal(new[] { "Demo" }, world.D.Activities.List(sam, new Activities.Filter("upcoming")).Rows.Select(r => r.Activity.Remarks));
        Assert.Equal(new[] { "Review" }, world.D.Activities.List(sam, new Activities.Filter("past")).Rows.Select(r => r.Activity.Remarks));
        Assert.Single(world.D.Activities.List(sam, new Activities.Filter("all", Type: "FollowUp")).Rows);
        Assert.Single(world.D.Activities.List(sam, new Activities.Filter("all", Q: "demo/")).Rows);
        // Mia sees her team's; Ken does not.
        Assert.Equal(2, world.D.Activities.List(world.Actor(TestWorld.Mia), new Activities.Filter("all")).Total);
        Assert.Equal(0, world.D.Activities.List(world.Actor(TestWorld.Ken), new Activities.Filter("all")).Total);
        // The month: from–to.
        Assert.Equal(2, world.D.Activities.List(sam, new Activities.Filter(From: new DateTime(2026, 10, 1), To: new DateTime(2026, 10, 31))).Total);
        // The company's tab.
        Assert.Equal(2, world.D.Activities.ForCompany(world.Actor(TestWorld.Mia), tan.Id, "all", null, 0).Total);
        Assert.Equal(200, Api(world, "activities", TestWorld.Sam, new() { ["when"] = "all" }).Status);
    }

    [Fact]
    public void The_Prospects_page_lists_the_prospects_of_the_person_and_their_team_by_the_filters()
    {
        using var world = new TestWorld();
        var tan = world.Company("Tan", "T1000");
        var lim = world.Company("Lim Trading", "T2000");
        world.Territory("North", 1, TestWorld.Sam, tan.Id);
        world.Store.Insert(new Prospect { CompanyId = tan.Id, Remarks = "6 MFPs", IsActive = true, Stage = "Negotiation", ProspectType = "R2R", Owner = TestWorld.Sam.Login,
            DateCreated = new DateTime(2026, 9, 1), EstimatedOrderDate = new DateTime(2026, 11, 15), Forecast = 12000 });
        world.Store.Insert(new Prospect { CompanyId = lim.Id, Remarks = "Renewal", IsActive = true, Stage = "Close", ProspectType = "NEW", Owner = TestWorld.Sam.Login,
            DateCreated = new DateTime(2026, 10, 1), IsClosed = true });
        world.Store.Insert(new Prospect { CompanyId = lim.Id, Remarks = "Dropped", IsActive = true, Owner = TestWorld.Sam.Login, DateCreated = new DateTime(2026, 10, 2), IsCancelled = true });
        world.Store.Insert(new Prospect { CompanyId = tan.Id, Remarks = "Gone", IsActive = false, Owner = TestWorld.Sam.Login });
        world.Store.Insert(new Prospect { CompanyId = tan.Id, Remarks = "Ken's", IsActive = true, Owner = TestWorld.Ken.Login });
        var sam = world.Actor(TestWorld.Sam);
        string[] Remarks(Activities.ProspectFilter f) => world.D.Activities.ProspectList(sam, f).Rows.Select(r => r.Prospect.Remarks).ToArray();
        Assert.Equal(new[] { "6 MFPs" }, Remarks(new Activities.ProspectFilter()));
        Assert.Equal(new[] { "Dropped", "Renewal", "6 MFPs" }, Remarks(new Activities.ProspectFilter(Status: "all")));
        Assert.Equal(new[] { "Renewal" }, Remarks(new Activities.ProspectFilter(Status: "closed")));
        Assert.Equal(new[] { "Dropped" }, Remarks(new Activities.ProspectFilter(Status: "cancelled")));
        Assert.Equal(new[] { "Dropped", "Renewal" }, Remarks(new Activities.ProspectFilter(Q: "lim tr", Status: "all")));
        Assert.Equal(new[] { "6 MFPs" }, Remarks(new Activities.ProspectFilter(Q: "mfp", Status: "all")));
        Assert.Equal(new[] { "Renewal" }, Remarks(new Activities.ProspectFilter(Stage: "Close", Status: "all")));
        Assert.Equal(new[] { "6 MFPs" }, Remarks(new Activities.ProspectFilter(Type: "R2R", Status: "all")));
        Assert.Equal(new[] { "6 MFPs" }, Remarks(new Activities.ProspectFilter(Status: "all", From: new DateTime(2026, 11, 1), To: new DateTime(2026, 11, 15))));
        Assert.Empty(Remarks(new Activities.ProspectFilter(Status: "all", To: new DateTime(2026, 11, 14), From: new DateTime(2026, 11, 1))));
        // Ken sees only his; the row names its company and owner.
        Assert.Equal(new[] { "Ken's" }, world.D.Activities.ProspectList(world.Actor(TestWorld.Ken), new Activities.ProspectFilter()).Rows.Select(r => r.Prospect.Remarks));
        var answer = Api(world, "prospects", TestWorld.Sam, new() { ["status"] = "all", ["q"] = "mfp" });
        Assert.Equal(200, answer.Status);
        var json = world.Json(answer);
        Assert.Equal(1, (int)json["total"]!);
        Assert.Equal("Tan", (string?)json["rows"]![0]!["company"]);
        Assert.Equal("Negotiation", (string?)json["rows"]![0]!["stage"]);
        Assert.Equal("2026-11-15", (string?)json["rows"]![0]!["estimatedOrder"]);
    }

    [Fact]
    public void A_companys_prospects_are_only_read()
    {
        using var world = new TestWorld();
        var tan = world.Company("Tan", "T1000");
        world.Territory("North", 1, TestWorld.Sam, tan.Id);
        world.Store.Insert(new Prospect { CompanyId = tan.Id, Remarks = "6 MFPs", IsActive = true, Stage = "Negotiation", ProspectType = "R2R" });
        world.Store.Insert(new Prospect { CompanyId = tan.Id, Remarks = "Gone", IsActive = false });
        var answer = Api(world, "prospects", TestWorld.Sam, new() { ["company"] = tan.Id.ToString() });
        Assert.Equal(200, answer.Status);
        Assert.Equal("Negotiation", (string?)world.Json(answer)["rows"]![0]!["stage"]);
        Assert.Single(world.Json(answer)["rows"]!);
        Assert.Equal(403, Api(world, "prospects", TestWorld.Ken, new() { ["company"] = tan.Id.ToString() }).Status);
        Assert.True(MyCrmMap.Of<Prospect>()!.ReadOnly);
        // Nothing in the activities' and projects' code writes a prospect.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "MyCRMRecords.Core"))) root = root.Parent;
        var core = Path.Combine(root!.FullName, "src", "MyCRMRecords.Core");
        var source = string.Concat(new[] { "Domain/Activities.cs", "Domain/Projects.cs", "Api/ProjectsApi.cs" }.Select(f => File.ReadAllText(Path.Combine(core, f))));
        Assert.DoesNotContain("new Prospect", source);
        Assert.DoesNotContain("Update(prospect", source, StringComparison.OrdinalIgnoreCase);
    }
}
