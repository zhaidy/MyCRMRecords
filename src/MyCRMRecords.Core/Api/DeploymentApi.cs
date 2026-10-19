using MyCRMRecords.Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MyCRMRecords.Api;

/// <summary>
/// The deployment page's API, for administrators: which build the website runs, whether it is the latest master
/// commit, and the change log. It only reads three files that deployment writes; it changes nothing.
/// </summary>
/// <remarks>
///   GET /admin/deployment   { installed, status, changeLog }
/// <list type="bullet">
/// <item><c>Content/deployment-version.txt</c>: the installed build (revision, build number), from Build.ps1.</item>
/// <item><c>bin/changelog.json</c>: the master history up to that build, from Build.ps1.</item>
/// <item><c>App_Data/deployment-status.json</c>: what the server's poller last found (Poll.ps1, every minute).</item>
/// </list>
/// A missing or unreadable file is returned as null, so the page can say what is unknown.
/// </remarks>
public sealed class DeploymentApi(string versionFile, string changeLogFile, string statusFile)
{
    public static bool Handles(string path) =>
        path.Trim('/').Equals("admin/deployment", StringComparison.Ordinal);

    public ApiResponse Handle(string method, string path)
    {
        if (!Handles(path)) return Result(404, ResultCodes.NotFound, "Not found.");
        if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase)) return Result(405, "METHOD_NOT_ALLOWED", "Use GET.");
        return new ApiResponse(200, Json.Serialize(new
        {
            installed = Read(versionFile),
            status = Read(statusFile),
            changeLog = Read(changeLogFile),
        }));
    }

    /// <summary>The file's JSON object, or null when it is missing, being replaced or not valid JSON.</summary>
    private static JObject? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            // File.ReadAllText drops a byte order mark if there is one. Dates stay text, exactly as written.
            using var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))) { DateParseHandling = DateParseHandling.None };
            return JToken.ReadFrom(reader) as JObject;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static ApiResponse Result(int status, string code, string message) =>
        new(status, Json.Serialize(new { result = new { ok = false, code, message } }));
}
