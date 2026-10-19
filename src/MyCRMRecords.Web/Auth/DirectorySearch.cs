using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using MyCRMRecords.Api;
using MyCRMRecords.Security;

namespace MyCRMRecords.Web.Auth;

/// <summary>
/// Finds people in Active Directory for the User roles page: enabled accounts whose login starts with, or whose name
/// contains, what the administrator typed. Read with the application pool's account, like the sign-in.
/// </summary>
public static class DirectorySearch
{
    private const int Limit = 25;

    public static IReadOnlyList<DirectoryPerson> Find(string query)
    {
        // "*" is the directory's wildcard; the rest is escaped by the directory classes.
        var term = query.Replace("*", "").Trim();
        var slash = term.IndexOf('\\');
        if (slash >= 0) term = term.Substring(slash + 1).Trim();
        if (term.Length == 0) return [];

        var found = new Dictionary<string, DirectoryPerson>(StringComparer.OrdinalIgnoreCase);
        using var domain = new PrincipalContext(ContextType.Domain);
        foreach (var makeFilter in new Func<UserPrincipal>[]
        {
            () => new UserPrincipal(domain) { SamAccountName = term + "*", Enabled = true },
            () => new UserPrincipal(domain) { DisplayName = "*" + term + "*", Enabled = true },
        })
        {
            using var filter = makeFilter();
            using var searcher = new PrincipalSearcher(filter);
            if (searcher.GetUnderlyingSearcher() is DirectorySearcher directory) directory.SizeLimit = Limit;
            using var results = searcher.FindAll();
            foreach (var person in results.OfType<UserPrincipal>())
            {
                if (found.Count >= Limit) break;
                if (string.IsNullOrEmpty(person.SamAccountName)) continue;
                var login = LoginNames.Format(SignIn.DomainName, person.SamAccountName);
                var name = string.IsNullOrWhiteSpace(person.DisplayName) ? person.SamAccountName : person.DisplayName.Trim();
                found[login] = new DirectoryPerson(login, name, person.EmailAddress ?? "");
            }
        }
        return found.Values.OrderBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
