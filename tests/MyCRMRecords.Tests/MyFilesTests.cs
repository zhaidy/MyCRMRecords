using Newtonsoft.Json.Linq;

namespace MyCRMRecords.Tests;

/// <summary>
/// The Files page: MyFiles' files, read only, searched and paged; who sees which is MyFile's rule on MyCRMRecords' company
/// rule (the roles that open every company see every file, everyone else the files of the companies they may open and the
/// files of no company), alike for the list, a company's files, a PDF and a zip.
/// </summary>
public sealed class MyFilesTests
{
    private static (TestWorld World, Company Tan, Company Lim, long TanFile, long LimFile, long NoCompany) World()
    {
        var world = new TestWorld();
        var tan = world.Company("Tan", "T1000");
        var lim = world.Company("Lim", "L1000");
        world.Territory("North", 1, TestWorld.Sam, tan.Id);
        world.Territory("South", 2, TestWorld.Ken, lim.Id);
        var tanFile = world.Files.Add("INV-1.pdf", "CostingSheet", tan.Id, "Tan", "T1000", invoice: "INV-1", uploaded: new DateTime(2026, 10, 5));
        var limFile = world.Files.Add("INV-2.pdf", "FSA", lim.Id, "Lim", "L1000", invoice: "INV-2", uploaded: new DateTime(2026, 10, 6));
        var none = world.Files.Add("DO 77.pdf", "DO", null, "Walk-in", "", doNumber: "DO77", uploaded: new DateTime(2026, 9, 1));
        world.Files.Add("old.pdf", "Others", tan.Id, "Tan", "T1000", active: false);
        return (world, tan, lim, tanFile, limFile, none);
    }

    private static JObject Call(TestWorld world, ApiUser user, Dictionary<string, string>? query = null) =>
        JObject.Parse(new MyFilesApi(world.D).Handle("GET", "files", "", query ?? new Dictionary<string, string>(), user).Json);

    private static FileAnswer Download(TestWorld world, ApiUser user, string kind, Dictionary<string, string>? query = null) =>
        new FilesApi(world.D).Download(kind, query ?? new Dictionary<string, string>(), user);

    [Fact]
    public void Each_person_lists_the_active_files_of_the_companies_they_may_open_and_of_no_company()
    {
        var (world, _, _, tanFile, limFile, none) = World();
        using (world)
        {
            long[] Ids(ApiUser user) => Call(world, user)["rows"]!.Select(r => (long)r["id"]!).ToArray();
            // Newest first; the inactive file never.
            Assert.Equal(new[] { limFile, tanFile, none }, Ids(TestWorld.Admin));
            Assert.Equal(new[] { limFile, tanFile, none }, Ids(TestWorld.SalesAdmin));
            Assert.Equal(new[] { tanFile, none }, Ids(TestWorld.Sam));
            Assert.Equal(new[] { tanFile, none }, Ids(TestWorld.Mia));
            Assert.Equal(new[] { limFile, none }, Ids(TestWorld.Ken));
            // No MyCRM role: only the files of no company.
            Assert.Equal(new[] { none }, Ids(TestWorld.Olga));
            Assert.True((bool)Call(world, TestWorld.Sam)["limited"]!);
            Assert.False((bool)Call(world, TestWorld.Dina)["limited"]!);
            var row = Call(world, TestWorld.Sam)["rows"]![0]!;
            Assert.Equal("Costing sheet", (string)row["type"]!);
            Assert.Equal("Invoice", (string)row["numbers"]![0]!["kind"]!);
            Assert.Equal(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 INV-1.pdf").Length, (long)row["size"]!);
        }
    }

    [Fact]
    public void The_search_and_filters_narrow_the_list()
    {
        var (world, tan, _, tanFile, limFile, none) = World();
        using (world)
        {
            long[] Ids(params (string, string)[] query) =>
                Call(world, TestWorld.Admin, query.ToDictionary(q => q.Item1, q => q.Item2))["rows"]!.Select(r => (long)r["id"]!).ToArray();
            Assert.Equal(new[] { limFile }, Ids(("q", "inv-2")));
            Assert.Equal(new[] { limFile }, Ids(("q", "L1000")));
            Assert.Equal(new[] { none }, Ids(("q", "walk")));
            Assert.Equal(new[] { none }, Ids(("q", "DO77")));
            Assert.Equal(new[] { tanFile }, Ids(("type", "Costing sheet")));
            Assert.Equal(new[] { tanFile }, Ids(("type", "CostingSheet")));
            Assert.Equal(new[] { limFile, tanFile }, Ids(("since", "2026-10-05")));
            Assert.Equal(new[] { tanFile }, Ids(("company", tan.Id.ToString())));
            // A type no file has, or a % searched as itself.
            Assert.Empty(Ids(("type", "Leasing")));
            Assert.Empty(Ids(("q", "%")));
        }
    }

    [Fact]
    public void A_company_s_files_are_refused_to_who_may_not_open_it()
    {
        var (world, tan, lim, tanFile, _, _) = World();
        using (world)
        {
            var sam = new MyFilesApi(world.D).Handle("GET", "files", "", new Dictionary<string, string> { ["company"] = tan.Id.ToString() }, TestWorld.Sam);
            Assert.Equal(200, sam.Status);
            Assert.Equal(new[] { tanFile }, JObject.Parse(sam.Json)["rows"]!.Select(r => (long)r["id"]!));
            Assert.Equal(403, new MyFilesApi(world.D).Handle("GET", "files", "", new Dictionary<string, string> { ["company"] = lim.Id.ToString() }, TestWorld.Sam).Status);
        }
    }

    [Fact]
    public void A_file_is_shown_or_downloaded_only_to_who_may_see_it()
    {
        var (world, _, _, tanFile, limFile, none) = World();
        using (world)
        {
            var shown = Download(world, TestWorld.Sam, "files/" + tanFile, new Dictionary<string, string> { ["view"] = "1" });
            Assert.Equal(200, shown.Status);
            Assert.True(shown.Inline);
            Assert.Equal("application/pdf", shown.ContentType);
            Assert.Equal("INV-1.pdf", shown.FileName);
            var saved = Download(world, TestWorld.Sam, "files/" + tanFile);
            Assert.False(saved.Inline);
            Assert.Equal(shown.Content, saved.Content);
            Assert.Equal(403, Download(world, TestWorld.Sam, "files/" + limFile).Status);
            Assert.Equal(200, Download(world, TestWorld.Olga, "files/" + none).Status);
            Assert.Equal(403, Download(world, TestWorld.Olga, "files/" + tanFile).Status);
            Assert.Equal(200, Download(world, TestWorld.Dina, "files/" + limFile).Status);
            // An inactive or missing file is not there.
            var old = world.Files.Search(new FileSearch()).Total;
            Assert.Equal(3, old);
            Assert.Equal(404, Download(world, TestWorld.Admin, "files/999999").Status);
            Assert.Equal(404, Download(world, TestWorld.Admin, "files/abc").Status);
        }
    }

    [Fact]
    public void Ticked_files_download_as_one_zip_only_when_every_one_may_be_seen()
    {
        var (world, _, _, tanFile, limFile, none) = World();
        using (world)
        {
            var ids = new Dictionary<string, string> { ["ids"] = $"{tanFile},{none}" };
            var zip = Download(world, TestWorld.Sam, "files/zip", ids);
            Assert.Equal(200, zip.Status);
            Assert.Equal("application/zip", zip.ContentType);
            using (var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip.Content)))
                Assert.Equal(new[] { "DO 77.pdf", "INV-1.pdf" }, archive.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(403, Download(world, TestWorld.Sam, "files/zip", new Dictionary<string, string> { ["ids"] = $"{tanFile},{limFile}" }).Status);
            Assert.Equal(404, Download(world, TestWorld.Sam, "files/zip").Status);
            var many = string.Join(",", Enumerable.Range(1, MyFiles.ZipMax + 1));
            Assert.Equal(400, Download(world, TestWorld.Admin, "files/zip", new Dictionary<string, string> { ["ids"] = many }).Status);
            // One file ticked: the file itself.
            Assert.Equal("INV-1.pdf", Download(world, TestWorld.Sam, "files/zip", new Dictionary<string, string> { ["ids"] = tanFile.ToString() }).FileName);
        }
    }

    [Fact]
    public void Without_the_OutSystems_database_the_page_says_so()
    {
        using var world = new TestWorld();
        world.Files.Available = false;
        Assert.Equal(503, new MyFilesApi(world.D).Handle("GET", "files", "", new Dictionary<string, string>(), TestWorld.Sam).Status);
        Assert.Equal(503, Download(world, TestWorld.Sam, "files/1").Status);
    }

    [Fact]
    public void The_types_are_matched_by_label_and_shown_in_plain_words()
    {
        Assert.Equal("CostingSheet", MyFilesTypes.LabelOf("costing sheet"));
        Assert.Equal("RequestForm", MyFilesTypes.LabelOf("Request form"));
        Assert.Equal("DO", MyFilesTypes.LabelOf("do"));
        Assert.Equal("", MyFilesTypes.LabelOf("Invoice"));
        Assert.Equal("Request form", MyFilesTypes.NameOf("RequestForm"));
        Assert.Equal("Something", MyFilesTypes.NameOf("Something"));
    }
}
