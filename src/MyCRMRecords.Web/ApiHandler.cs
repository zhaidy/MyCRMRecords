using System.IO.Compression;
using System.Web;
using MyCRMRecords.Api;
using MyCRMRecords.Domain;
using MyCRMRecords.Web.Auth;

namespace MyCRMRecords.Web;

/// <summary>
/// Serves <c>api.ashx/...</c> (IIS maps .ashx to ASP.NET by default, so nothing is registered in IIS). The pages call it
/// as the signed-in person (<see cref="CrmApi"/>, <see cref="OrdersApi"/> and <see cref="MyFilesApi"/> (read only), <see cref="MachinesApi"/>, <see cref="UsersApi"/>, and for
/// administrators <see cref="AdminApi"/> and <see cref="DeploymentApi"/>). Every change (each POST) goes into the audit log.
/// </summary>
public sealed class ApiHandler : IHttpHandler
{
    public bool IsReusable => true;

    private const int CompressFrom = 2048;

    public void ProcessRequest(HttpContext context)
    {
        Services.EnsureStarted();
        var request = context.Request;
        var response = context.Response;
        response.Cache.SetCacheability(HttpCacheability.NoCache);
        response.Cache.SetNoStore();
        response.ContentType = "application/json";
        response.ContentEncoding = System.Text.Encoding.UTF8;
        response.TrySkipIisCustomErrors = true;
        response.SuppressFormsAuthenticationRedirect = true;
        var path = request.PathInfo ?? "";
        var query = request.QueryString.AllKeys.Where(k => k is not null)
            .ToDictionary(k => k!, k => request.QueryString[k] ?? "", StringComparer.OrdinalIgnoreCase);

        // Anonymous requests reach this handler so it can answer 401 as JSON instead of a redirect to the sign-in page.
        var signedIn = SignIn.Current(context);
        if (signedIn is null)
        {
            response.StatusCode = 401;
            response.Write("{\"result\":{\"ok\":false,\"code\":\"UNAUTHORIZED\",\"message\":\"Please sign in again.\"}}");
            return;
        }
        // Only GET reads and POST changes: anything else (HEAD, PUT…) would otherwise reach the routes that change things
        // without the JSON check below or the audit log.
        if (request.HttpMethod != "GET" && request.HttpMethod != "POST")
        {
            response.StatusCode = 405;
            response.AppendHeader("Allow", "GET, POST");
            response.Write("{\"result\":{\"ok\":false,\"code\":\"METHOD_NOT_ALLOWED\",\"message\":\"Use GET or POST.\"}}");
            return;
        }
        // Changes are sent as JSON: browsers cannot send that cross-site without a CORS preflight, which this API never
        // grants, so other sites cannot make a signed-in person change anything.
        if (request.HttpMethod == "POST" && !(request.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 415;
            response.Write("{\"result\":{\"ok\":false,\"code\":\"UNSUPPORTED_MEDIA_TYPE\",\"message\":\"Send JSON.\"}}");
            return;
        }
        var body = ReadBody(request);
        var user = Admins.Resolve(context, signedIn);
        ApiResponse result;
        try
        {
            result = Handle(context, path, query, body, user);
        }
        catch (Exception ex)
        {
            result = ApiResponse.Fail(500, "SERVER_ERROR", "Something went wrong: " + ex.Message);
        }
        if (request.HttpMethod == "POST")
        {
            var (ok, message) = Outcome(result);
            Services.Record(context, user, Describe(path), path, ok, message, Hide(path, body), status: result.Status);
        }
        Write(context, result);
    }

    internal static string ReadBody(HttpRequest request)
    {
        using var reader = new StreamReader(request.InputStream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static void Write(HttpContext context, ApiResponse result)
    {
        var request = context.Request;
        var response = context.Response;
        response.StatusCode = result.Status;
        if (result.Json.Length >= CompressFrom && (request.Headers["Accept-Encoding"] ?? "").IndexOf("gzip", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            response.AppendHeader("Content-Encoding", "gzip");
            response.AppendHeader("Vary", "Accept-Encoding");
            response.Filter = new GZipStream(response.Filter, CompressionLevel.Fastest);
        }
        response.Write(result.Json);
    }

    private static ApiResponse Handle(HttpContext context, string path, IReadOnlyDictionary<string, string> query, string body, ApiUser user)
    {
        var request = context.Request;
        var route = path.Trim('/');
        if (DeploymentApi.Handles(path) || AdminApi.Handles(path))
        {
            if (!IsAdmin(user)) return ApiResponse.Forbidden("Only administrators can open this page.");
            return DeploymentApi.Handles(path) ? Services.DeploymentApi.Handle(request.HttpMethod, path)
                : Services.AdminApi.Handle(request.HttpMethod, path, body, query, user);
        }
        if (route == "users/impersonate" && request.HttpMethod == "POST") return Impersonate(context, body, user);
        if (UsersApi.Handles(path)) return Services.UsersApi.Handle(request.HttpMethod, path, body, query, user);
        if (OrdersApi.Handles(path)) return Services.OrdersApi.Handle(request.HttpMethod, path, body, query, user);
        if (MyFilesApi.Handles(path)) return Services.MyFilesApi.Handle(request.HttpMethod, path, body, query, user);
        if (MachinesApi.Handles(path)) return Services.MachinesApi.Handle(request.HttpMethod, path, body, query, user);
        return Services.Api.Handle(request.HttpMethod, path, body, query, user);
    }

    /// <summary>
    /// Whether the person is an administrator: those Web.config lists always are, so they reach the admin page to fix
    /// the database settings even when the data cannot be read.
    /// </summary>
    private static bool IsAdmin(ApiUser user)
    {
        if (user.IsListedAdmin) return true;
        try { return user.ToActor(Services.App.People).IsAdmin; }
        catch (Exception) { return false; }
    }

    private sealed class ImpersonateInput
    {
        public string? Login { get; set; }
    }

    /// <summary>
    /// "Sign in as" on the User roles page and the Costing sheets page (administrators only; recorded): the person's
    /// cookie replaces theirs, carrying who to return to.
    /// </summary>
    private static ApiResponse Impersonate(HttpContext context, string body, ApiUser user)
    {
        if (user.Impersonator is not null) return ApiResponse.Forbidden("Return to your own account first.");
        if (!IsAdmin(user)) return ApiResponse.Forbidden("Only administrators can sign in as someone else.");
        ImpersonateInput input;
        try { input = MyCRMRecords.Json.Deserialize<ImpersonateInput>(body) ?? new ImpersonateInput(); }
        catch (Newtonsoft.Json.JsonException) { return ApiResponse.BadRequest(); }
        var login = (input.Login ?? "").Trim();
        if (string.Equals(login, user.Login, StringComparison.OrdinalIgnoreCase)) return ApiResponse.BadRequest("That is you.");
        // Someone MyCRMRecords knows, or anyone MyCRM or a costing sheet names (who may never have opened it yet).
        var person = Services.App.People.User(login);
        PersonToSignInAs? listed = null;
        try { listed = Services.App.OrdersAccess.PeopleToSignInAs(Services.App.People.Everyone()).FirstOrDefault(p => string.Equals(p.Login, login, StringComparison.OrdinalIgnoreCase)); }
        catch (Exception) { /* MyOrders cannot be read just now: only the people MyCRMRecords knows. */ }
        if (person is null && listed is null && Services.App.People.RolesOf(login).Count == 0) return ApiResponse.NotFound();
        var name = person is { DisplayName.Length: > 0 } ? person.DisplayName : listed?.Name ?? login;
        var email = person is { Email.Length: > 0 } ? person.Email : listed?.Email ?? "";
        var target = new ApiUser(person?.Login ?? listed?.Login ?? login, name, email, Impersonator: new ApiUser(user.Login, user.DisplayName, user.Email));
        SignIn.IssueCookie(context.Response, target, remember: false);
        return ApiResponse.Of(new { result = new { ok = true, code = ResultCodes.Ok, message = $"You are now signed in as {target.DisplayName}." } });
    }

    /// <summary>What the audit log keeps of a change: never the passwords and keys typed on the admin page.</summary>
    private static string Hide(string path, string body)
    {
        if (path.Trim('/').StartsWith("admin/", StringComparison.Ordinal))
        {
            try
            {
                var json = Newtonsoft.Json.Linq.JToken.Parse(body);
                foreach (var property in json.SelectTokens("$..*").OfType<Newtonsoft.Json.Linq.JValue>().Select(v => v.Parent).OfType<Newtonsoft.Json.Linq.JProperty>().ToList())
                    if (property.Name.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 || property.Name.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
                        || string.Equals(property.Name, "key", StringComparison.OrdinalIgnoreCase))
                        property.Value = property.Value.ToString().Length > 0 ? "(hidden)" : "";
                body = json.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                body = "";
            }
        }
        return body.Length > 4000 ? body.Substring(0, 4000) : body;
    }

    public static (bool Ok, string Message) Outcome(ApiResponse result)
    {
        try
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(result.Json);
            var outcome = json["result"];
            return (outcome?["ok"]?.ToObject<bool>() ?? result.Status < 400, outcome?["message"]?.ToString() ?? "");
        }
        catch (Exception)
        {
            return (result.Status < 400, "");
        }
    }

    /// <summary>The action, in words, for the audit log: "companies/12/addresses/0" → "Add an address to company 12".</summary>
    public static string Describe(string path)
    {
        var parts = path.Trim('/').Split('/');
        long Number(int i) => parts.Length > i && long.TryParse(parts[i], out var n) ? n : 0;
        string Save(string what, int i) => Number(i) == 0 ? $"Add {what}" : $"Save {what} {Number(i)}";
        switch (parts[0])
        {
            case "companies" when parts.Length == 2: return Number(1) == 0 ? "Create a company" : $"Save company {Number(1)}";
            case "companies" when parts.Length == 3 && parts[2] == "employees": return $"Save the number of employees of company {Number(1)}";
            case "companies" when parts.Length == 3 && parts[2] == "remarks": return $"Save the sales remarks of company {Number(1)}";
            case "companies" when parts.Length == 3 && parts[2] == "relationships": return $"Add a relationship to company {Number(1)}";
            case "companies" when parts.Length == 5 && parts[2] == "relationships": return $"Remove relationship {Number(3)} of company {Number(1)}";
            case "companies" when parts.Length == 4 && parts[2] == "addresses": return Number(3) == 0 ? $"Add an address to company {Number(1)}" : $"Save address {Number(3)} of company {Number(1)}";
            case "companies" when parts.Length == 5 && parts[2] == "addresses" && parts[4] == "type": return $"Change the type of address {Number(3)} of company {Number(1)}";
            case "companies" when parts.Length == 5 && parts[2] == "addresses": return $"Delete address {Number(3)} of company {Number(1)}";
            case "companies" when parts.Length == 4 && parts[2] == "contacts": return Number(3) == 0 ? $"Add a contact to company {Number(1)}" : $"Save contact {Number(3)} of company {Number(1)}";
            case "companies" when parts.Length == 5 && parts[2] == "contacts": return $"Delete contact {Number(3)} of company {Number(1)}";
            case "groupnames" when parts.Length == 2: return Save("a group name", 1);
            case "groupnames" when parts.Length == 3: return $"Remove group name {Number(1)}";
            case "territories" when parts.Length == 2: return Number(1) == 0 ? "Create a territory" : $"Save territory {Number(1)}";
            case "territories" when parts.Length == 3 && parts[2] == "remove": return $"Take territory {Number(1)} away";
            case "territories" when parts.Length == 3 && parts[2] == "add": return $"Put companies in territory {Number(1)}";
            case "territories" when parts.Length == 3 && parts[2] == "takeout": return $"Take companies out of territory {Number(1)}";
            case "territories" when parts.Length == 3 && parts[2] == "match": return $"Find a list of companies for territory {Number(1)}";
            case "crmusers" when parts.Length == 2: return "Save a CRM user";
            case "crmusers" when parts.Length == 3 && parts[2] == "remove": return "Remove a CRM user and their territories";
            case "crmusers" when parts.Length == 3 && parts[2] == "team": return "Create the team named after a manager";
            case "teams" when parts.Length == 2: return Number(1) == 0 ? "Create a team" : $"Save team {Number(1)}";
            case "people": return "Add someone to choose from";
            case "machineaccess" when parts.Length == 1: return "Give temporary access to a company's machines (MyMachines)";
            case "machineaccess" when parts.Length == 3: return $"Remove temporary access {Number(1)} to a company's machines (MyMachines)";
        }
        return Capitalise(string.Join(" ", parts).Replace('-', ' '));
    }

    public static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
}
