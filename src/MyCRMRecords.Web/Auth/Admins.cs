using System.Collections.Concurrent;
using System.Configuration;
using System.DirectoryServices.AccountManagement;
using System.Web;
using MyCRMRecords.Api;
using MyCRMRecords.Security;

namespace MyCRMRecords.Web.Auth;

/// <summary>
/// The administrators listed in the appSetting <c>MyCRMRecords:Admins</c>, as logins (RSP\jsmith) and/or Active
/// Directory groups separated by ";". They are administrators even when the database cannot be reached, so they can
/// fix its settings; everyone else's roles come from the User roles page.
/// </summary>
public static class Admins
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, (DateTime At, IReadOnlyList<string> Groups)> GroupCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The signed-in person, with whether Web.config lists them as an administrator.</summary>
    public static ApiUser Resolve(HttpContext context, ApiUser user) => user with { IsListedAdmin = IsListed(context, user.Login) };

    public static bool IsListed(HttpContext context, string login)
    {
        var admins = LoginNames.ParseGroups(ConfigurationManager.AppSettings["MyCRMRecords:Admins"]);
        return LoginNames.IsAdmin(admins, login, () => GroupsOf(context, login));
    }

    /// <summary>The person's Active Directory groups, kept for a few minutes; none when the directory cannot be read.</summary>
    private static IReadOnlyList<string> GroupsOf(HttpContext context, string login)
    {
        if (SignIn.DevelopmentMode(context)) return [];
        if (GroupCache.TryGetValue(login, out var cached) && DateTime.UtcNow - cached.At < CacheFor) return cached.Groups;
        try
        {
            var groups = LookUpGroups(login);
            GroupCache[login] = (DateTime.UtcNow, groups);
            return groups;
        }
        catch (Exception)
        {
            return [];
        }
    }

    // Kept separate so the directory assembly only loads when Active Directory is used.
    private static IReadOnlyList<string> LookUpGroups(string login)
    {
        var name = login.Substring(login.IndexOf('\\') + 1);
        using var domain = new PrincipalContext(ContextType.Domain);
        using var user = UserPrincipal.FindByIdentity(domain, IdentityType.SamAccountName, name);
        if (user is null) return [];
        return user.GetAuthorizationGroups().Select(group => group.SamAccountName ?? group.Name ?? "").Where(group => group.Length > 0).ToList();
    }
}
