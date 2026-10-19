using System.Configuration;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Web;
using System.Web.Security;
using MyCRMRecords.Api;
using MyCRMRecords.Security;

namespace MyCRMRecords.Web.Auth;

public sealed record SignInResult(ApiUser? User, string? Error);

/// <summary>
/// Sign-in the same way as ReportGenerator3: a Forms authentication cookie, issued after the username and
/// password are checked against Active Directory. Logins are stored as DOMAIN\name (appSetting ADDomain).
/// </summary>
public static class SignIn
{
    public const int RememberDays = 7;

    public static string DomainName
    {
        get
        {
            var configured = ConfigurationManager.AppSettings["ADDomain"];
            return string.IsNullOrEmpty(configured) ? Environment.UserDomainName : configured!;
        }
    }

    /// <summary>
    /// "Development" signs anyone in without a password check, but only for requests from the server itself while
    /// Web.config has compilation debug="true". It exists for machines without Active Directory; never set it on IIS.
    /// </summary>
    internal static bool DevelopmentMode(HttpContext context) =>
        string.Equals(ConfigurationManager.AppSettings["MyCRMRecords:LoginMode"], "Development", StringComparison.OrdinalIgnoreCase)
        && context.IsDebuggingEnabled && context.Request.IsLocal;

    public static SignInResult Validate(HttpContext context, string? userInput, string? password)
    {
        var input = LoginNames.Parse(userInput);
        if (input is null || string.IsNullOrEmpty(password)) return new SignInResult(null, "Enter your username and password.");

        if (DevelopmentMode(context))
        {
            var name = input.IsUserPrincipalName ? input.Name.Substring(0, input.Name.IndexOf('@')) : input.Name;
            // An email too, since who someone is on a work order comes from it.
            return new SignInResult(new ApiUser(LoginNames.Format(DomainName, name), name, name.ToLowerInvariant() + "@example.com"), null);
        }

        try
        {
            return ValidateWithDirectory(input, password!);
        }
        catch (Exception)
        {
            return new SignInResult(null, "Cannot contact the domain to check your password. Please try again later.");
        }
    }

    // Kept separate so the directory assembly only loads when Active Directory is used.
    private static SignInResult ValidateWithDirectory(LoginInput input, string password)
    {
        const string NoAccess = "Your account does not have access to MyCRMRecords. Please contact an administrator.";
        using var domain = new PrincipalContext(ContextType.Domain);
        using var user = FindUser(domain, input);
        if (user is null || string.IsNullOrEmpty(user.SamAccountName) || !domain.ValidateCredentials(user.SamAccountName, password))
            return new SignInResult(null, "Incorrect username or password.");
        if (user.Enabled == false) return new SignInResult(null, NoAccess);

        var requiredGroups = LoginNames.ParseGroups(ConfigurationManager.AppSettings["MyCRMRecords:AllowedGroups"]);
        if (requiredGroups.Count > 0)
        {
            var memberOf = user.GetAuthorizationGroups().Select(group => group.SamAccountName ?? group.Name ?? "").ToList();
            if (!LoginNames.IsAllowed(requiredGroups, memberOf)) return new SignInResult(null, NoAccess);
        }

        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.SamAccountName : user.DisplayName.Trim();
        return new SignInResult(new ApiUser(LoginNames.Format(DomainName, user.SamAccountName), displayName, user.EmailAddress?.Trim() ?? ""), null);
    }

    /// <summary>
    /// The account for what was typed: by username, or for "name@domain" by user principal name and then by email
    /// address, for people whose email differs from their user principal name. An email address shared by several
    /// accounts signs in none of them.
    /// </summary>
    private static UserPrincipal? FindUser(PrincipalContext domain, LoginInput input)
    {
        var identityType = input.IsUserPrincipalName ? IdentityType.UserPrincipalName : IdentityType.SamAccountName;
        var user = UserPrincipal.FindByIdentity(domain, identityType, input.Name);
        if (user is not null) return user;

        var email = LoginNames.EmailAddress(input);
        if (email is null) return null;
        using var filter = new UserPrincipal(domain) { EmailAddress = email };
        using var searcher = new PrincipalSearcher(filter);
        if (searcher.GetUnderlyingSearcher() is DirectorySearcher directory) directory.SizeLimit = 2;
        using var results = searcher.FindAll();
        var matches = results.OfType<UserPrincipal>().Take(2).ToList();
        if (matches.Count == 1) return matches[0];
        foreach (var match in matches) match.Dispose();
        return null;
    }

    /// <summary>
    /// Issues the Forms cookie. The display name (and, while an administrator is signed in as someone, that administrator)
    /// rides in the ticket so pages need no directory lookup; see <see cref="SignInTickets"/>.
    /// </summary>
    public static void IssueCookie(HttpResponse response, ApiUser user, bool remember)
    {
        var now = DateTime.Now;
        // FormsAuthentication.Timeout is Web.config's forms timeout (480 minutes); fall back to it if a runtime reports none.
        var timeout = FormsAuthentication.Timeout > TimeSpan.Zero ? FormsAuthentication.Timeout : TimeSpan.FromMinutes(480);
        var expires = remember ? now.AddDays(RememberDays) : now.Add(timeout);
        var ticket = new FormsAuthenticationTicket(SignInTickets.Version, user.Login, now, expires, remember,
            SignInTickets.Encode(user), FormsAuthentication.FormsCookiePath);
        var cookie = new HttpCookie(FormsAuthentication.FormsCookieName, FormsAuthentication.Encrypt(ticket))
        {
            HttpOnly = true,
            Path = FormsAuthentication.FormsCookiePath,
            Secure = FormsAuthentication.RequireSSL,
        };
        if (remember) cookie.Expires = expires;
        response.Cookies.Add(cookie);
    }

    /// <summary>The signed-in person, or null.</summary>
    public static ApiUser? Current(HttpContext context)
    {
        // Read through IIdentity: FormsIdentity derives from ClaimsIdentity only on .NET Framework, not on Mono.
        System.Security.Principal.IIdentity? identity = context.User?.Identity;
        if (identity is null || !identity.IsAuthenticated || identity is not FormsIdentity forms) return null;
        return SignInTickets.Decode(identity.Name, forms.Ticket.UserData);
    }
}
