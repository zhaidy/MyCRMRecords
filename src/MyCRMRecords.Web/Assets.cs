using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Web;

namespace MyCRMRecords.Web;

/// <summary>
/// Script and stylesheet links that change when the file changes ("admin.js?v=3f2a9c01"), so after a deployment
/// browsers load the new file instead of a copy they kept from the previous version.
/// </summary>
public static class Assets
{
    private static readonly ConcurrentDictionary<string, string> Versions = new();

    public static string Url(string path)
    {
        var file = HttpContext.Current.Server.MapPath("~/" + path);
        if (!File.Exists(file)) return path;
        // Keyed by the file's time too, so a deployment that replaces the file is picked up without a restart.
        var key = file + "|" + File.GetLastWriteTimeUtc(file).Ticks;
        var version = Versions.GetOrAdd(key, _ =>
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(file);
            return BitConverter.ToString(sha.ComputeHash(stream), 0, 4).Replace("-", "").ToLowerInvariant();
        });
        return path + "?v=" + version;
    }
}

/// <summary>
/// MyCRMRecords's mark: a thin outline picture of a contact card, in the accent colour, beside the name in the top bar
/// and on the sign-in page; the same picture is the browser tab's icon. Every app has its own picture in this style
/// (the MyApps project's transfer guide lists them), never a letter on a coloured tile.
/// </summary>
public static class Brand
{
    private const string Shapes = "<rect x='3' y='5' width='18' height='14' rx='2'/><circle cx='9' cy='11' r='2.2'/><path d='M5.8 16a3.4 3.4 0 0 1 6.4 0'/><path d='M14.5 10h3.5'/><path d='M14.5 13.5h2.5'/>";

    /// <summary>The mark in the top bar and on the sign-in page, drawn in the theme's accent colour (app.css <c>.brand-mark</c>).</summary>
    public static string Mark { get; } =
        "<span class=\"brand-mark\" aria-hidden=\"true\"><svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'>"
        + Shapes + "</svg></span>";

    /// <summary>The browser tab's icon: the same picture, written into the page so there is no file to deploy.</summary>
    public static string TabIcon { get; } =
        "<link rel=\"icon\" type=\"image/svg+xml\" href=\"data:image/svg+xml,"
        + System.Uri.EscapeDataString("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' stroke='#3346c8' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>" + Shapes + "</svg>")
        + "\">";
}
