using System.Text.RegularExpressions;

namespace MyCRMRecords.Tests;

/// <summary>The pages' shared parts, and what goes into the release.</summary>
public sealed class PageStyleTests
{
    private static string Web(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "MyCRMRecords.Web"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "MyCRMRecords.Web", file));
    }

    [Fact]
    public void The_top_bar_search_opens_the_companies_search_box()
    {
        // The round search button of the top bar goes to the Companies page at #search, which puts the cursor in its box.
        Assert.Contains("href=\\\"./#search\\\"", Web("AppPage.cs"));
        Assert.Contains("location.hash !== '#search'", Web("companies.js"));
        Assert.Contains("$('#q').focus()", Web("companies.js"));
    }

    [Fact]
    public void Every_page_and_script_goes_into_the_release()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "MyCRMRecords.Web"))) root = root.Parent;
        Assert.NotNull(root);
        var listed = Regex.Match(File.ReadAllText(Path.Combine(root!.FullName, "Deployment", "Build.ps1")), @"\$pages = @\(([^)]*)\)").Groups[1].Value;
        Assert.NotEmpty(listed);
        var files = Directory.GetFiles(Path.Combine(root.FullName, "src", "MyCRMRecords.Web"))
            .Select(f => Path.GetFileName(f)!)
            .Where(f => f == "Web.config" || new[] { ".aspx", ".ashx", ".js", ".css", ".png" }.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
        Assert.Contains("company.aspx", files);
        Assert.All(files, f => Assert.Contains("'" + f + "'", listed));
    }
}
