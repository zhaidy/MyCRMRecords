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
