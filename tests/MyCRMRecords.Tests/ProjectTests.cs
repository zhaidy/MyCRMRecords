namespace MyCRMRecords.Tests;

/// <summary>
/// MyCRM's projects, made and changed here: companies and members, and the temporary territories that give a member a
/// company they do not own while the project is open, taken away when a member or company leaves, the project closes or
/// is deleted, or its end date passes (the daily task). Nothing is deleted.
/// </summary>
public sealed class ProjectTests
{
    private static Projects.ProjectInput Input(string name, DateTime end, params long[] companies) =>
        new() { Name = name, EndDate = end.ToString("yyyy-MM-dd"), Companies = companies.ToList() };

    /// <summary>Tan in Sam's territory North, Lim in nobody's; Mia's project "Cover" with both.</summary>
    private static (TestWorld World, Company Tan, Company Lim, Project Project) Setup()
    {
        var world = new TestWorld();
        var tan = world.Company("Tan", "T1000");
        var lim = world.Company("Lim", "L1000");
        world.Territory("North", 1, TestWorld.Sam, tan.Id);
        var made = world.D.Projects.Create(world.Actor(TestWorld.Mia), Input("Cover", world.Now.Date.AddDays(30), tan.Id, lim.Id));
        Assert.True(made.Ok, made.Message);
        return (world, tan, lim, world.D.Projects.Get(made.Id!.Value)!);
    }

    [Fact]
    public void A_project_needs_a_name_an_end_date_after_today_and_a_company_and_its_creator_is_its_Project_Manager()
    {
        using var world = new TestWorld();
        var mia = world.Actor(TestWorld.Mia);
        var tan = world.Company("Tan", "T1000");
        var bad = world.D.Projects.Create(mia, Input("", world.Now.Date, Array.Empty<long>()));
        Assert.Contains(bad.Errors, e => e.Field == "name");
        Assert.Contains(bad.Errors, e => e.Field == "endDate" && e.Message.StartsWith("Project End Date must not be less than the current date"));
        Assert.Contains(bad.Errors, e => e.Field == "companies" && e.Message == "You must have a Company Associated with this Project.");
        // Sales and Marketing do not create projects (MyCRM's Create Project button).
        Assert.Equal(ResultCodes.Forbidden, world.D.Projects.Create(world.Actor(TestWorld.Sam), Input("X", world.Now.Date.AddDays(3), tan.Id)).Code);
        var made = world.D.Projects.Create(mia, Input("Cover", world.Now.Date.AddDays(3), tan.Id));
        Assert.True(made.Ok, made.Message);
        var p = world.D.Projects.Get(made.Id!.Value)!;
        Assert.Equal((TestWorld.Mia.Login, ProjectStatuses.ConceptionAndInitiation, tan.Id), (p.Owner, p.Status, p.CompanyId!.Value));
        var member = world.Store.Find<ProjectMember>().Single();
        Assert.Equal((TestWorld.Mia.Login, ProjectRoles.ProjectManager), (member.User, member.Role));
        // The Project Manager needs no temporary territory.
        Assert.Null(member.TerritoryId);
        Assert.Empty(world.Store.Find<TemporaryTerritory>());
    }

    [Fact]
    public void A_member_who_does_not_own_a_company_of_the_project_gets_a_temporary_territory_with_it()
    {
        var (world, tan, lim, p) = Setup();
        using var _ = world;
        var mia = world.Actor(TestWorld.Mia);
        Assert.False(world.D.Access.CanOpen(world.Actor(TestWorld.Ken), lim));
        var added = world.D.Projects.AddMember(mia, p.Id, new Projects.MemberInput { Login = TestWorld.Ken.Login, Role = ProjectRoles.SalespersonCaretaker });
        Assert.True(added.Ok, added.Message);
        Assert.Contains("can now see 2 companies", added.Message);
        var ken = world.Store.Get<ProjectMember>(added.Id!.Value)!;
        var territory = world.Store.Get<TemporaryTerritory>(ken.TerritoryId!.Value)!;
        // MyCRM's number: Ken's OutSystems user (107) followed by the company's Id.
        Assert.Equal((TestWorld.Ken.Login, true, false, long.Parse("107" + tan.Id)), (territory.Owner, territory.IsActive, territory.IsOwner, territory.Number));
        Assert.Equal(new[] { tan.Id, lim.Id }.OrderBy(i => i), world.Store.Find<TemporaryCompanyTerritory>(Query.Where("IsActive", true)).Select(l => l.CompanyId).OrderBy(i => i));
        Assert.True(world.D.Access.CanOpen(world.Actor(TestWorld.Ken), lim));
        Assert.Equal(Access.Project, world.D.Access.CompanyReasons(world.Actor(TestWorld.Ken))[lim.Id]);
        // Sam owns Tan (territory North): his temporary territory has only Lim.
        var sam = world.D.Projects.AddMember(mia, p.Id, new Projects.MemberInput { Login = TestWorld.Sam.Login, Role = "Salesperson" });
        var samTerritory = world.Store.Get<ProjectMember>(sam.Id!.Value)!.TerritoryId!.Value;
        Assert.Equal(new[] { lim.Id }, world.Store.Find<TemporaryCompanyTerritory>(Query.Where("TerritoryId", samTerritory)).Select(l => l.CompanyId));
        // The temporary territories are not MyCRMRecords' territories: no page that lists territories shows them.
        Assert.Equal(new[] { "North" }, world.D.Territories.All(includeInactive: true).Select(t => t.Name));
        Assert.Equal(1, world.Store.Find<CompanyTerritory>().Count);
        Assert.False(world.D.Access.OwnCompanyIds(world.Actor(TestWorld.Ken)).Contains(lim.Id));
        // Only the owner adds members, once each, never as Project Manager.
        Assert.Equal(ResultCodes.Forbidden, world.D.Projects.AddMember(world.Actor(TestWorld.SalesAdmin), p.Id, new Projects.MemberInput { Login = TestWorld.Tom.Login, Role = "Telemarketer" }).Code);
        Assert.Contains(world.D.Projects.AddMember(mia, p.Id, new Projects.MemberInput { Login = TestWorld.Ken.Login, Role = "Telemarketer" }).Errors, e => e.Field == "login");
        Assert.Contains(world.D.Projects.AddMember(mia, p.Id, new Projects.MemberInput { Login = TestWorld.Tom.Login, Role = ProjectRoles.ProjectManager }).Errors, e => e.Field == "role");
        // Olga has no CRM user record: she cannot be a member.
        Assert.Contains(world.D.Projects.AddMember(mia, p.Id, new Projects.MemberInput { Login = TestWorld.Olga.Login, Role = "Telemarketer" }).Errors, e => e.Field == "login");
    }

    [Fact]
    public void Removing_a_member_or_a_company_takes_their_temporary_access_away_and_keeps_every_row()
    {
        var (world, tan, lim, p) = Setup();
        using var _ = world;
        var mia = world.Actor(TestWorld.Mia);
        var ken = world.D.Projects.AddMember(mia, p.Id, new Projects.MemberInput { Login = TestWorld.Ken.Login, Role = "Salesperson" }).Id!.Value;
        var tom = world.D.Projects.AddMember(mia, p.Id, new Projects.MemberInput { Login = TestWorld.Tom.Login, Role = "Telemarketer" }).Id!.Value;
        // Lim leaves the project: nobody reaches it through the project any more; Tan stays.
        Assert.True(world.D.Projects.RemoveCompany(mia, p.Id, lim.Id).Ok);
        Assert.False(world.D.Access.CanOpen(world.Actor(TestWorld.Ken), lim));
        Assert.True(world.D.Access.CanOpen(world.Actor(TestWorld.Ken), tan));
        Assert.False(world.Store.Find<ProjectCompany>(Query.Where("CompanyId", lim.Id)).Single().IsActive);
        // The last company cannot be removed.
        Assert.Equal(ResultCodes.Conflict, world.D.Projects.RemoveCompany(mia, p.Id, tan.Id).Code);
        // Added again: its row comes back active, and so do the links.
        Assert.True(world.D.Projects.AddCompanies(mia, p.Id, [lim.Id]).Ok);
        Assert.Single(world.Store.Find<ProjectCompany>(Query.Where("CompanyId", lim.Id)));
        Assert.True(world.D.Access.CanOpen(world.Actor(TestWorld.Ken), lim));
        // Ken leaves: his territory and its links are inactive; Tom keeps his.
        Assert.True(world.D.Projects.RemoveMember(mia, p.Id, ken).Ok);
        Assert.False(world.Store.Get<ProjectMember>(ken)!.IsActive);
        Assert.False(world.D.Access.CanOpen(world.Actor(TestWorld.Ken), tan));
        Assert.True(world.D.Access.CanOpen(world.Actor(TestWorld.Tom), lim) || world.Actor(TestWorld.Tom).SeesAllCompanies);
        Assert.NotEmpty(ProjectTerritories.Reached(world.Store, world.Store.Get<ProjectMember>(tom)!));
        // The owner is never removed.
        var owner = world.Store.Find<ProjectMember>(Query.Where("Role", ProjectRoles.ProjectManager)).Single();
        Assert.Equal(ResultCodes.Conflict, world.D.Projects.RemoveMember(mia, p.Id, owner.Id).Code);
        Assert.Equal(3, world.Store.Find<ProjectMember>().Count);
    }

    [Fact]
    public void Closing_or_deleting_a_project_takes_every_temporary_territory_away()
    {
        var (world, tan, lim, p) = Setup();
        using var _ = world;
        var mia = world.Actor(TestWorld.Mia);
        world.D.Projects.AddMember(mia, p.Id, new Projects.MemberInput { Login = TestWorld.Ken.Login, Role = "Salesperson" });
        Assert.Equal((1, 2), world.D.Projects.LosingAccess(p.Id));
        // Only the owner or a Sales Admin closes it.
        Assert.Equal(ResultCodes.Forbidden, world.D.Projects.Close(world.Actor(TestWorld.Ken), p.Id).Code);
        var closed = world.D.Projects.Close(mia, p.Id);
        Assert.True(closed.Ok, closed.Message);
        Assert.Contains("1 temporary territory was taken away", closed.Message);
        Assert.False(world.D.Access.CanOpen(world.Actor(TestWorld.Ken), lim));
        Assert.All(world.Store.Find<TemporaryTerritory>(), t => Assert.False(t.IsActive));
        Assert.All(world.Store.Find<TemporaryCompanyTerritory>(), l => Assert.False(l.IsActive));
        // Closed: nothing changes any more, members stay listed.
        Assert.Equal(ResultCodes.Conflict, world.D.Projects.AddMember(mia, p.Id, new Projects.MemberInput { Login = TestWorld.Tom.Login, Role = "Telemarketer" }).Code);
        Assert.Equal(2, world.D.Projects.Members(p.Id).Count);

        // Deleted (by a Sales Admin): the project, its companies, members and activity links are inactive, never deleted.
        var activity = world.Store.Insert(new Activity { CompanyId = tan.Id, Owner = TestWorld.Mia.Login, IsActive = true });
        world.Store.Insert(new ProjectLink { CompanyActivityId = activity.Id, ProjectId = p.Id, IsActive = true });
        Assert.True(world.D.Projects.Delete(world.Actor(TestWorld.SalesAdmin), p.Id).Ok);
        Assert.Null(world.D.Projects.Get(p.Id));
        Assert.False(world.Store.Get<Project>(p.Id)!.IsActive);
        Assert.All(world.Store.Find<ProjectCompany>(), c => Assert.False(c.IsActive));
        Assert.All(world.Store.Find<ProjectMember>(), m => Assert.False(m.IsActive));
        Assert.False(world.Store.Find<ProjectLink>().Single().IsActive);
        Assert.True(world.Store.Get<Activity>(activity.Id)!.IsActive);
        // Its page says it was deleted: when and by whom to its owner and Sales Admins, only that to others.
        Newtonsoft.Json.Linq.JObject Page(ApiUser user) => Newtonsoft.Json.Linq.JObject.Parse(
            new ProjectsApi(world.D).Handle("GET", "projects/" + p.Id, "", new Dictionary<string, string>(), user).Json);
        Assert.Equal("DELETED", (string)Page(TestWorld.Mia)["result"]!["code"]!);
        Assert.Equal("The project Cover was deleted on 17 Oct 2026 by Sara Admin.", (string)Page(TestWorld.Mia)["result"]!["message"]!);
        Assert.Equal("This project was deleted.", (string)Page(TestWorld.Ken)["result"]!["message"]!);
        Assert.Equal("NOT_FOUND", (string)Newtonsoft.Json.Linq.JObject.Parse(
            new ProjectsApi(world.D).Handle("GET", "projects/999999", "", new Dictionary<string, string>(), TestWorld.Mia).Json)["result"]!["code"]!);
    }

    [Fact]
    public void A_later_end_date_and_the_daily_task_follow_MyCRMs_end_date_rule()
    {
        var (world, _, lim, p) = Setup();
        using var __ = world;
        var mia = world.Actor(TestWorld.Mia);
        world.D.Projects.AddMember(mia, p.Id, new Projects.MemberInput { Login = TestWorld.Ken.Login, Role = "Salesperson" });
        // An orphan temporary territory (no member has it), as MyCRM's other parts left some.
        var orphan = world.Store.Insert(new TemporaryTerritory { Number = 1, Owner = TestWorld.Tom.Login, IsActive = true });
        world.Store.Insert(new TemporaryCompanyTerritory { TerritoryId = orphan.Id, CompanyId = lim.Id, IsActive = true });
        // Before the end date: nothing closes; the orphan is made inactive (FixTerritory).
        var run = world.D.Projects.RunDaily("Scheduled");
        Assert.Equal("0 projects closed; 1 temporary territory made inactive.", run.Summary);
        Assert.False(world.Store.Get<TemporaryTerritory>(orphan.Id)!.IsActive);
        Assert.True(world.D.Access.CanOpen(world.Actor(TestWorld.Ken), lim));
        // The morning of its end date the project closes and Ken loses Lim.
        world.Now = p.EndDate!.Value.AddHours(5.5);
        run = world.D.Projects.RunDaily("Scheduled");
        Assert.StartsWith("1 project closed", run.Summary);
        Assert.True(world.Store.Get<Project>(p.Id)!.IsClosed);
        Assert.False(world.D.Access.CanOpen(world.Actor(TestWorld.Ken), lim));
        // The task is in the scheduled tasks, off until turned on.
        Assert.Contains(Jobs.ProjectsEnded, Jobs.All);
        Assert.False(Jobs.Default(Jobs.ProjectsEnded).Enabled);
        Assert.Equal("05:30", Jobs.Default(Jobs.ProjectsEnded).DailyAt);
    }

    [Fact]
    public void The_projects_list_reads_only_its_page_and_still_counts_and_searches_them_all()
    {
        var (world, tan, lim, _) = Setup();
        using var _w = world;
        var mia = world.Actor(TestWorld.Mia);
        for (var i = 1; i <= 55; i++)
            Assert.True(world.D.Projects.Create(mia, Input("Batch " + i, world.Now.Date.AddDays(30), lim.Id)).Ok);
        var admin = world.Actor(TestWorld.SalesAdmin);
        var first = world.D.Projects.List(admin, "open", null, 0);
        Assert.Equal(56, first.Total);
        Assert.Equal(Projects.PageSize, first.Rows.Count);
        Assert.Equal("Batch 55", first.Rows[0].Project.ProjectName);
        Assert.Equal(6, world.D.Projects.List(admin, "open", null, Projects.PageSize).Rows.Count);
        // By a company's name: Tan is only in the first project; by the project's name.
        Assert.Equal(new[] { "Cover" }, world.D.Projects.List(admin, "open", "tan", 0).Rows.Select(r => r.Project.ProjectName));
        Assert.Equal(56, world.D.Projects.List(admin, "open", "lim", 0).Total);
        Assert.Equal(7, world.D.Projects.List(admin, "open", "batch 5", 0).Total);
        Assert.Equal(new[] { "Lim" }, first.Rows[0].Companies);
    }

    [Fact]
    public void Projects_are_listed_to_their_members_and_their_teams()
    {
        var (world, tan, _, p) = Setup();
        using var _w = world;
        world.D.Projects.AddMember(world.Actor(TestWorld.Mia), p.Id, new Projects.MemberInput { Login = TestWorld.Ken.Login, Role = "Salesperson" });
        Assert.Single(world.D.Projects.List(world.Actor(TestWorld.Ken), "open", null, 0).Rows);
        // Sam is in Mia's team: Mia's projects are not his (MyCRM lists those with a member in his list: only himself).
        Assert.Empty(world.D.Projects.List(world.Actor(TestWorld.Sam), "open", null, 0).Rows);
        Assert.Single(world.D.Projects.List(world.Actor(TestWorld.SalesAdmin), "all", "tan", 0).Rows);
        Assert.Empty(world.D.Projects.List(world.Actor(TestWorld.SalesAdmin), "closed", null, 0).Rows);
        Assert.Equal(new[] { "Cover" }, world.D.Projects.ForCompany(tan.Id).Select(x => x.Project.ProjectName));
        Assert.False(world.D.Projects.MaySee(world.Actor(TestWorld.Olga), p));
        // Through the API: the page's data.
        var answer = ActivityTests.Api(world, "projects/" + p.Id, TestWorld.Ken);
        Assert.Equal(200, answer.Status);
        Assert.Equal(2, world.Json(answer)["members"]!.Count());
        Assert.Equal(403, ActivityTests.Api(world, "projects/" + p.Id, TestWorld.Olga).Status);
    }

    [Fact]
    public void The_company_page_shows_the_caretakers_of_its_open_projects_but_not_its_salesperson()
    {
        var (world, tan, lim, p) = Setup();
        using var _ = world;
        var mia = world.Actor(TestWorld.Mia);
        Assert.True(world.D.Projects.AddMember(mia, p.Id, new Projects.MemberInput { Login = TestWorld.Ken.Login, Role = ProjectRoles.SalespersonCaretaker }).Ok);
        Assert.True(world.D.Projects.AddMember(mia, p.Id, new Projects.MemberInput { Login = TestWorld.Sam.Login, Role = ProjectRoles.SalespersonAccountOwner }).Ok);
        Assert.True(world.D.Projects.AddMember(mia, p.Id, new Projects.MemberInput { Login = TestWorld.Tom.Login, Role = "BillingCoordinator" }).Ok);
        // Sam owns Tan: he is its salesperson, not a caretaker; Tom's role takes no care of the company; Mia is the Project Manager.
        var detail = world.Json(world.Call("GET", "companies/" + tan.Id, user: TestWorld.SalesAdmin));
        var caretakers = detail["caretakers"]!.ToList();
        Assert.Single(caretakers);
        Assert.Equal("Ken Kwan", (string)caretakers[0]["name"]!);
        Assert.Equal("Salesperson (Caretaker)", (string)caretakers[0]["role"]!);
        Assert.Equal("Cover", (string)caretakers[0]["project"]!);
        Assert.Equal(new[] { "Ken Kwan", "Sam Sales" }, world.D.Projects.Caretakers(lim.Id, "").Select(x => world.D.People.NameOf(x.Member.User)).OrderBy(n => n).ToArray());
        // A closed project has no caretakers.
        Assert.True(world.D.Projects.Close(mia, p.Id).Ok);
        Assert.Empty(world.Json(world.Call("GET", "companies/" + tan.Id, user: TestWorld.SalesAdmin))["caretakers"]!);
    }
}
