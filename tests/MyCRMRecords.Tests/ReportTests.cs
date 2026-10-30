using MyCRMRecords.Documents;

namespace MyCRMRecords.Tests;

/// <summary>
/// The Companies page's Download (MyCRM's report 09, Company summary) and the Projects page's Download (reports 17 and
/// 08, projects and caretake accounts): the companies and projects of the page's search and filters, as the person may
/// see them, with their salesperson, team, addresses and caretakers.
/// </summary>
public sealed class ReportTests
{
    /// <summary>Tan (billing address) in Sam's territory North, Lim in nobody's; Mia's project "Cover" with both, Ken its caretaker and Sam its account owner.</summary>
    private static (TestWorld World, Company Tan, Company Lim, Project Project) Setup()
    {
        var world = new TestWorld();
        var tan = world.Company("Tan Holdings", "T1000");
        var lim = world.Company("Lim Trading", "L1000");
        var admin = world.Actor(TestWorld.Admin);
        Assert.True(world.D.Companies.SaveAddress(admin, tan.Id, 0, new Companies.AddressInput
            { AddressType = AddressTypes.Billing, Block = "1", Street = "Harbour Road", PostalCode = "123456", Country = "Singapore" }).Ok);
        world.Territory("North", 1, TestWorld.Sam, tan.Id);
        var mia = world.Actor(TestWorld.Mia);
        var made = world.D.Projects.Create(mia, new Projects.ProjectInput
            { Name = "Cover", EndDate = world.Now.Date.AddDays(30).ToString("yyyy-MM-dd"), Companies = new List<long> { tan.Id, lim.Id } });
        Assert.True(made.Ok, made.Message);
        Assert.True(world.D.Projects.AddMember(mia, made.Id!.Value, new Projects.MemberInput { Login = TestWorld.Ken.Login, Role = ProjectRoles.SalespersonCaretaker }).Ok);
        Assert.True(world.D.Projects.AddMember(mia, made.Id!.Value, new Projects.MemberInput { Login = TestWorld.Sam.Login, Role = ProjectRoles.SalespersonAccountOwner }).Ok);
        return (world, tan, lim, world.D.Projects.Get(made.Id!.Value)!);
    }

    private static List<Dictionary<string, string>> Sheet(FileAnswer file, int sheet = 0)
    {
        Assert.Equal(200, file.Status);
        using var stream = new MemoryStream(file.Content);
        return Xlsx.Read(stream, sheet);
    }

    [Fact]
    public void The_company_summary_has_every_company_of_the_filters_with_its_salesperson_team_address_and_caretakers()
    {
        var (world, tan, lim, _) = Setup();
        using var _w = world;
        var (total, lines) = world.D.Reports.CompanySummary(world.Actor(TestWorld.Admin), new Companies.ListFilter());
        Assert.Equal(2, total);
        Assert.Equal(new[] { "Lim Trading", "Tan Holdings" }, lines.Select(l => l.Company.Name));
        var t = lines.Single(l => l.Company.Id == tan.Id);
        Assert.Equal(("Sam Sales", "Mia Manager", "Mia Manager", "North", "123456"), (t.Salesperson, t.Team, t.Manager, t.Territory, t.PostalCode));
        Assert.Equal("1 Harbour Road, 123456", t.BillingAddress);
        // MyCRM's companyOwnerCaretake: the caretakers of its open projects, leaving out its own salesperson (Sam).
        Assert.Equal("Ken Kwan (Cover)", t.Caretakers);
        var l = lines.Single(x => x.Company.Id == lim.Id);
        Assert.Equal(("", "", ""), (l.Salesperson, l.Territory, l.BillingAddress));
        Assert.Equal("Ken Kwan (Cover); Sam Sales (Cover)", l.Caretakers);
        // The page's Show and search, and at most the number asked for (the total still says how many match).
        Assert.Equal(new[] { lim.Id }, world.D.Reports.CompanySummary(world.Actor(TestWorld.Admin), new Companies.ListFilter(Show: Companies.NoSalesperson)).Lines.Select(x => x.Company.Id));
        Assert.Equal(new[] { tan.Id }, world.D.Reports.CompanySummary(world.Actor(TestWorld.Admin), new Companies.ListFilter(Q: "T1000")).Lines.Select(x => x.Company.Id));
        var capped = world.D.Reports.CompanySummary(world.Actor(TestWorld.Admin), new Companies.ListFilter(), max: 1);
        Assert.Equal((2, 1), (capped.Total, capped.Lines.Count));
        // A closed project's caretakers no longer count.
        Assert.True(world.D.Projects.Close(world.Actor(TestWorld.Mia), world.D.Projects.ForCompany(tan.Id).Single().Project.Id).Ok);
        Assert.All(world.D.Reports.CompanySummary(world.Actor(TestWorld.Admin), new Companies.ListFilter()).Lines, x => Assert.Equal("", x.Caretakers));
    }

    [Fact]
    public void The_company_summary_download_has_only_the_companies_the_person_may_open()
    {
        var (world, tan, _, _) = Setup();
        using var _w = world;
        var files = new FilesApi(world.D);
        // Olga sees no company; the admin every one.
        Assert.Empty(Sheet(files.Download(FilesApi.CompanySummary, new Dictionary<string, string>(), TestWorld.Olga)));
        var all = Sheet(files.Download(FilesApi.CompanySummary, new Dictionary<string, string>(), TestWorld.Admin));
        Assert.Equal(new[] { "Lim Trading", "Tan Holdings" }, all.Select(r => r["Company"]));
        var row = all[1];
        Assert.Equal(("T1000", "Sam Sales", "Mia Manager", "North", "No", "Yes"), (row["BRN"], row["Salesperson"], row["Team"], row["Territory"], row["Caretake"], row["Active"]));
        Assert.Equal("Ken Kwan (Cover)", row["Caretakers (open projects)"]);
        // Searched as the page searches.
        Assert.Equal(new[] { "Tan Holdings" }, Sheet(files.Download(FilesApi.CompanySummary, new Dictionary<string, string> { ["q"] = "Tan" }, TestWorld.Sam)).Select(r => r["Company"]));
        Assert.StartsWith("CompanySummary-", files.Download(FilesApi.CompanySummary, new Dictionary<string, string>(), TestWorld.Sam).FileName);
        Assert.Equal(tan.Brn, Sheet(files.Download(FilesApi.CompanySummary, new Dictionary<string, string> { ["postalCode"] = "123456" }, TestWorld.Admin)).Single()["BRN"]);
    }

    [Fact]
    public void The_projects_download_lists_the_projects_and_a_line_for_each_company_with_its_owner_and_caretakers()
    {
        var (world, tan, lim, project) = Setup();
        using var _w = world;
        var files = new FilesApi(world.D);
        var file = files.Download(FilesApi.ProjectsReport, new Dictionary<string, string> { ["show"] = "open" }, TestWorld.Mia);
        Assert.StartsWith("Projects-", file.FileName);
        var projects = Sheet(file);
        var p = Assert.Single(projects);
        Assert.Equal(("Cover", "Open", "Mia Manager", "2"), (p["Project"], p["Open / Ended / Closed"], p["Owner"], p["Companies"]));
        Assert.Equal("Mia Manager (Project Manager); Ken Kwan (Salesperson (Caretaker)); Sam Sales (Salesperson (Account Owner))", p["Members"]);
        var companies = Sheet(file, 1);
        Assert.Equal(new[] { "Tan Holdings", "Lim Trading" }, companies.Select(r => r["Company"]));
        var tanRow = companies[0];
        Assert.Equal(("T1000", "Sam Sales", "North", "Cover"), (tanRow["BRN"], tanRow["Account owner"], tanRow["Territory"], tanRow["Project"]));
        Assert.Equal("Ken Kwan (Salesperson (Caretaker)); Sam Sales (Salesperson (Account Owner))", tanRow["Caretakers"]);
        // Ken reaches both through the project; Sam owns Tan himself, so only Lim through the project.
        Assert.Equal("Ken Kwan: Yes; Sam Sales: No", tanRow["Temporary access"]);
        Assert.Equal("Ken Kwan: Yes; Sam Sales: Yes", companies[1]["Temporary access"]);
        // As the Projects list: Olga is in no project; Closed has none yet; a cap keeps the first lines.
        Assert.Empty(Sheet(files.Download(FilesApi.ProjectsReport, new Dictionary<string, string>(), TestWorld.Olga)));
        Assert.Empty(Sheet(files.Download(FilesApi.ProjectsReport, new Dictionary<string, string> { ["show"] = "closed" }, TestWorld.Mia)));
        var capped = world.D.Reports.ProjectsReport(world.Actor(TestWorld.Mia), "all", null, maxCompanies: 1);
        Assert.Equal((1, 1, 2), (capped.Projects.Count, capped.Companies.Count, capped.CompanyTotal));
        // Closed: in Closed and All, its state said; nobody has temporary access any more.
        Assert.True(world.D.Projects.Close(world.Actor(TestWorld.Mia), project.Id).Ok);
        var closed = world.D.Reports.ProjectsReport(world.Actor(TestWorld.SalesAdmin), "closed", "Cover");
        Assert.Equal("Closed", Assert.Single(closed.Projects).State);
        Assert.All(closed.Companies, c => Assert.Equal("Ken Kwan: No; Sam Sales: No", c.TemporaryAccess));
        Assert.Equal(new[] { tan.Id, lim.Id }, closed.Companies.Select(c => c.CompanyId));
    }

    [Fact]
    public void A_project_past_its_end_date_and_not_closed_is_Ended()
    {
        var today = new DateTime(2026, 10, 17);
        Assert.Equal("Open", Reports.StateOf(new Project { EndDate = today }, today));
        Assert.Equal("Ended", Reports.StateOf(new Project { EndDate = today.AddDays(-1) }, today));
        Assert.Equal("Closed", Reports.StateOf(new Project { EndDate = today.AddDays(5), IsClosed = true }, today));
    }
}
