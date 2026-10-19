using MyCRMRecords.Data;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Api;

/// <summary>
/// The User roles page's API (api.ashx/users...), administrators only: who has opened MyCRMRecords and their roles,
/// finding people in Active Directory, and giving or taking away roles. "Sign in as" is answered by the web host, which
/// issues the cookie.
/// </summary>
public sealed class UsersApi(CrmApp w, Func<string, IReadOnlyList<DirectoryPerson>> findInDirectory)
{
    public static bool Handles(string path)
    {
        var route = path.Trim('/');
        return route == "users" || route.StartsWith("users/", StringComparison.Ordinal);
    }

    public sealed class RolesInput
    {
        public string? Login { get; set; }
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
        public List<string>? Roles { get; set; }
    }

    public ApiResponse Handle(string method, string path, string body, IReadOnlyDictionary<string, string> query, ApiUser user)
    {
        var route = path.Trim('/');
        var actor = user.ToActor(w.People);
        if (!actor.IsAdmin) return ApiResponse.Forbidden("Only administrators give roles.");
        try
        {
            switch (route)
            {
                case "users" when method == "GET":
                {
                    var roles = w.Store.Find<UserRole>(Query.Where("IsActive", true)).GroupBy(r => r.Login, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Select(r => r.Role).Where(Roles.IsKnown).Distinct().ToList(), StringComparer.OrdinalIgnoreCase);
                    var users = w.Store.Find<AppUser>().ToList();
                    // Someone given a role before ever opening MyCRMRecords has roles but no user record yet.
                    foreach (var login in roles.Keys.Where(l => !users.Any(u => string.Equals(u.Login, l, StringComparison.OrdinalIgnoreCase))))
                        users.Add(new AppUser { Login = login, DisplayName = login });
                    return ApiResponse.Of(new
                    {
                        me = actor.Login,
                        canGiveAdmin = true,
                        roles = Roles.All,
                        users = users.OrderBy(u => u.DisplayName.Length > 0 ? u.DisplayName : u.Login, StringComparer.OrdinalIgnoreCase).Select(u => new
                        {
                            login = u.Login,
                            displayName = u.DisplayName,
                            email = u.Email,
                            lastSeenAt = u.LastSeenAt,
                            roles = roles.TryGetValue(u.Login, out var r) ? r : new List<string>(),
                        }),
                    });
                }
                case "users/search" when method == "GET":
                {
                    var q = query.TryGetValue("q", out var v) ? (v ?? "").Trim() : "";
                    if (q.Length < 2) return ApiResponse.Of(new { people = Array.Empty<DirectoryPerson>() });
                    try
                    {
                        return ApiResponse.Of(new { people = findInDirectory(q) });
                    }
                    catch (Exception ex)
                    {
                        return ApiResponse.Fail(503, "DIRECTORY_UNAVAILABLE", "Active Directory could not be searched: " + ex.Message);
                    }
                }
                case "users/roles" when method == "POST":
                {
                    var input = Input.Read<RolesInput>(body);
                    return ApiResponse.Result(w.People.SetRoles(actor, input.Login ?? "", input.DisplayName, input.Email, input.Roles ?? new List<string>()));
                }
            }
            return ApiResponse.NotFound();
        }
        catch (BadInputException ex)
        {
            return ApiResponse.BadRequest(ex.Message);
        }
    }
}
