using System.Text;
using System.Text.RegularExpressions;

namespace MyCRMRecords.Tests;

/// <summary>Rules of app.css that a later rule could silently undo.</summary>
public sealed class PageStyleTests
{
    private static string AppCss()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "MyCRMRecords.Web"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, "src", "MyCRMRecords.Web", "app.css"));
    }

    /// <summary>The rules outside @media (and other @ blocks), comments removed.</summary>
    private static string TopLevel(string css)
    {
        css = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
        var top = new StringBuilder();
        for (var i = 0; i < css.Length; i++)
        {
            if (css[i] != '@') { top.Append(css[i]); continue; }
            var depth = 0;
            for (i = css.IndexOf('{', i); i < css.Length; i++)
                if (css[i] == '{') depth++;
                else if (css[i] == '}' && --depth == 0) break;
        }
        return top.ToString();
    }

    [Fact]
    public void The_menu_button_is_hidden_on_wide_screens()
    {
        // The menu button is also an .icon-button (display:inline-flex). The rule hiding it must come after every rule that
        // shows it, outside the narrow-screen @media: the later of two equal rules wins, and the button then showed next
        // to the menu links on every screen.
        var displays = Regex.Matches(TopLevel(AppCss()), @"([^{}]+)\{([^{}]*)\}").Cast<Match>()
            .Select(m => (Selectors: m.Groups[1].Value.Split(',').Select(s => s.Trim()).ToList(),
                Display: Regex.Match(m.Groups[2].Value, @"display\s*:\s*([^;}]+)").Groups[1].Value.Trim()))
            .Where(r => r.Display.Length > 0 && (r.Selectors.Contains(".icon-button") || r.Selectors.Contains(".menu-button")))
            .ToList();
        Assert.NotEmpty(displays);
        var last = displays[displays.Count - 1];
        Assert.True(last.Selectors.Contains(".menu-button") && last.Display == "none",
            ".menu-button{display:none} must come after the .icon-button rules in app.css (outside @media).");
        Assert.Contains("@media", AppCss());
        Assert.Matches(@"@media\s*\(max-width:\s*\d+px\)\s*\{[^@]*\.menu-button\{display:inline-flex\}", AppCss());
    }
}
