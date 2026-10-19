using MyCRMRecords.Api;

namespace MyCRMRecords.Security;

/// <summary>What a person typed in the username box, as ReportGenerator3 reads it.</summary>
public sealed record LoginInput(string Name, bool IsUserPrincipalName);

/// <summary>
/// Username handling shared with ReportGenerator3's sign-in (UserStore.ValidateLogin): people may type
/// "jsmith", "RSP\jsmith" or "jsmith@domain", and the signed-in login is stored as "RSP\jsmith". MyCRMRecords also
/// accepts the email address when it differs from the user principal name.
/// </summary>
public static class LoginNames
{
    /// <summary>Strips any "DOMAIN\" prefix. Returns null when nothing usable was typed.</summary>
    public static LoginInput? Parse(string? userInput)
    {
        var name = (userInput ?? "").Trim();
        var slash = name.IndexOf('\\');
        if (slash >= 0) name = name.Substring(slash + 1).Trim();
        if (name.Length == 0 || name.Length > 256) return null;
        return new LoginInput(name, name.Contains("@"));
    }

    /// <summary>
    /// The email address to look the person up by when <paramref name="input"/> matches no user principal name, or null
    /// when it is not one. "*" is the directory's wildcard, so an input containing it never matches an address.
    /// </summary>
    public static string? EmailAddress(LoginInput input) =>
        input.IsUserPrincipalName && input.Name.IndexOf('*') < 0 ? input.Name : null;

    public static string Format(string domain, string samAccountName) => domain + "\\" + samAccountName;

    /// <summary>Whether <paramref name="login"/> is a whole signed-in login: DOMAIN\\name, with nothing missing.</summary>
    public static bool IsFullLogin(string? login)
    {
        login ??= "";
        var slash = login.IndexOf('\\');
        return slash > 0 && slash < login.Length - 1 && login.Length <= 256 && login.IndexOf('\\', slash + 1) < 0
            && login.Trim() == login;
    }

    /// <summary>Splits a "Group A; Group B" setting into group names; empty means any domain user may sign in.</summary>
    public static IReadOnlyList<string> ParseGroups(string? setting) =>
        (setting ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(group => group.Trim())
            .Where(group => group.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>True when no groups are required, or the person belongs to at least one of them (case-insensitive).</summary>
    public static bool IsAllowed(IReadOnlyList<string> requiredGroups, IEnumerable<string> memberOf) =>
        requiredGroups.Count == 0 || memberOf.Any(group => requiredGroups.Contains(group, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Whether <paramref name="login"/> is an administrator. Entries with a backslash are logins ("RSP\jsmith"),
    /// the others are Active Directory groups, which are only looked up (<paramref name="memberOf"/>) when listed.
    /// Nobody is an administrator when the setting is empty.
    /// </summary>
    public static bool IsAdmin(IReadOnlyList<string> admins, string login, Func<IEnumerable<string>> memberOf)
    {
        if (admins.Any(entry => entry.Contains("\\") && string.Equals(entry, login, StringComparison.OrdinalIgnoreCase))) return true;
        var groups = admins.Where(entry => !entry.Contains("\\")).ToList();
        return groups.Count > 0 && memberOf().Any(group => groups.Contains(group, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>Where to go after signing in: only paths inside this site, never another site (an "open redirect").</summary>
public static class ReturnUrls
{
    public static bool IsLocal(string? url, string applicationPath)
    {
        if (string.IsNullOrEmpty(url) || url![0] != '/') return false;
        // "//host" and "/\host" are treated by browsers as other sites.
        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\')) return false;
        if (url.IndexOfAny(new[] { '\r', '\n' }) >= 0) return false;
        var root = applicationPath.TrimEnd('/') + "/";
        return url.StartsWith(root, StringComparison.OrdinalIgnoreCase) || string.Equals(url, applicationPath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// What the sign-in cookie carries besides the login: the display name and email address (from Active Directory at
/// sign-in), and when an administrator signed in as someone (users.aspx, "Sign in as"), that administrator, so they can
/// return to their own account. The cookie is encrypted and signed by ASP.NET, so the browser cannot change it.
/// </summary>
public static class SignInTickets
{
    public const int Version = 1;

    private sealed record Data(string DisplayName, string? Email, string? ImpersonatorLogin, string? ImpersonatorName);

    public static string Encode(ApiUser user) =>
        Json.Serialize(new Data(user.DisplayName, user.Email, user.Impersonator?.Login, user.Impersonator?.DisplayName));

    public static ApiUser Decode(string login, string? userData)
    {
        Data? data;
        try
        {
            data = Json.Deserialize<Data?>(userData ?? "");
        }
        catch (Newtonsoft.Json.JsonException)
        {
            data = null;
        }
        if (data is null) return new ApiUser(login, login);
        var impersonator = LoginNames.IsFullLogin(data.ImpersonatorLogin)
            ? new ApiUser(data.ImpersonatorLogin!, string.IsNullOrWhiteSpace(data.ImpersonatorName) ? data.ImpersonatorLogin! : data.ImpersonatorName!)
            : null;
        return new ApiUser(login, string.IsNullOrWhiteSpace(data.DisplayName) ? login : data.DisplayName, data.Email ?? "", Impersonator: impersonator);
    }
}
