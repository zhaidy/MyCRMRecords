namespace MyCRMRecords;

/// <summary>
/// The user guide, docs/user-guide.md, built into this assembly so every release carries the guide of its own version.
/// guide.aspx writes it into the page, and guide.js shows it there and in the panel the ? button opens on every page.
/// </summary>
public static class UserGuide
{
    public const string ResourceName = "MyCRMRecords.user-guide.md";

    private static readonly Lazy<string> Text = new(() =>
    {
        using var stream = typeof(UserGuide).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is missing from {typeof(UserGuide).Assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    /// <summary>The guide's Markdown.</summary>
    public static string Markdown => Text.Value;
}
