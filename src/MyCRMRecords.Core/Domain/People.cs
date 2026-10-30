using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>A problem with one field of a form, by the field's name.</summary>
public sealed record FieldError(string Field, string Message);

/// <summary>What a change did: whether it was made, and a message for the person.</summary>
public sealed record OperationResult
{
    public bool Ok { get; init; }
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public IReadOnlyList<FieldError> Errors { get; init; } = Array.Empty<FieldError>();
    public long? Id { get; init; }

    public static OperationResult Success(string message, long? id = null) => new() { Ok = true, Code = ResultCodes.Ok, Message = message, Id = id };

    public static OperationResult Failure(string code, string message, IReadOnlyList<FieldError>? errors = null) =>
        new() { Ok = false, Code = code, Message = message, Errors = errors ?? Array.Empty<FieldError>() };

    public static OperationResult Invalid(IReadOnlyList<FieldError> errors) =>
        Failure(ResultCodes.Validation, errors.Count == 1 ? errors[0].Message : "Please correct the fields marked.", errors);
}

public static class ResultCodes
{
    public const string Ok = "OK";
    public const string NotFound = "NOT_FOUND";
    public const string Forbidden = "FORBIDDEN";
    public const string Validation = "VALIDATION";
    public const string Conflict = "CONFLICT";
    public const string Failed = "FAILED";
}

/// <summary>
/// MyCRMRecords's own role: its administrators. What people may do in the CRM comes from their MyCRM role (their CRM
/// user record, kept on the CRM users page), as in MyCRM.
/// </summary>
public static class Roles
{
    /// <summary>MyCRMRecords's administrator: the admin pages, user roles and the audit log, and everything a Sales Admin does.</summary>
    public const string Admin = "Admin";

    public sealed record RoleInfo(string Name, string Label, string Description);

    public static readonly IReadOnlyList<RoleInfo> All =
    [
        new(Admin, "Administrator", "The admin pages, user roles and the audit log; also everything a Sales Admin does (CRM users, territories, company changes) and signing in as someone else."),
    ];

    public static bool IsKnown(string role) => All.Any(r => r.Name == role);
}

/// <summary>
/// The person making a change: their login, name, email (lower case), MyCRMRecords roles, who acts for them, and their
/// MyCRM role (<see cref="CrmRole"/>, the role of their first CRM user record, as MyCRM's Role()).
/// </summary>
public sealed record Actor(string Login, string DisplayName, string Email, IReadOnlyList<string> Roles, string? OnBehalfOf = null)
{
    /// <summary>The person's MyCRM role (<see cref="CrmRoles"/>), "" when they are not a CRM user.</summary>
    public string CrmRole { get; init; } = "";
    /// <summary>MyCRM's IsSalesAdmin: one of their CRM user records has the role Admin or SalesAdmin.</summary>
    public bool CrmSalesAdmin { get; init; }

    public bool Has(string role) => Roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
    public bool IsAdmin => Has(Domain.Roles.Admin);
    /// <summary>MyCRM's Users screens, the company form and the territories: Admin or SalesAdmin in MyCRM, or an administrator here.</summary>
    public bool IsSalesAdmin => IsAdmin || CrmSalesAdmin;
    /// <summary>The territories: Sales Admins, and MD (MyCRM's bulk transfer screen let MD move companies).</summary>
    public bool CanManageTerritories => IsSalesAdmin || CrmRole == CrmRoles.Md;
    /// <summary>MyCRM shows the address and contact buttons to every role but Marketing.</summary>
    public bool CanEditContacts => IsAdmin || CrmRole != CrmRoles.Marketing;
    /// <summary>MyCRM's UserCanAccessCompany: these roles open every company.</summary>
    public bool SeesAllCompanies => IsAdmin || CrmRoles.Is(CrmRole, CrmRoles.SeeAllCompanies);
    /// <summary>MyCRM hides a company's territory movements from Sales and Telemarketers.</summary>
    public bool SeesMovements => IsAdmin || (CrmRole != CrmRoles.Sales && CrmRole != CrmRoles.Telemarketer);

    private Lazy<OrdersProfile> orders = new(() => OrdersProfile.None);

    /// <summary>
    /// Where the person's MyOrders profile comes from (<see cref="OrdersAccess.Profile"/>): read the first time
    /// <see cref="Orders"/> is used, so only the costing sheet and request form pages read MyCRM's users for it.
    /// </summary>
    public Func<OrdersProfile> OrdersFrom { init => orders = new Lazy<OrdersProfile>(value); }

    /// <summary>
    /// The person as MyOrders knew them: their MyCRM role and the OutSystems users whose costing sheets and request forms
    /// they see (MyOrders' rule, by OutSystems user, kept as it was in MyOrdersRecords).
    /// </summary>
    public OrdersProfile Orders => orders.Value;

    /// <summary>MyOrders' ShowAll: these roles (and administrators) see every costing sheet and request form.</summary>
    public bool SeesAllCostingSheets => IsAdmin || CrmRoles.Is(Orders.Role, CrmRoles.SeeAllCostingSheets);

    /// <summary>"Jamie Lee", or "Alex Tan on behalf of Jamie Lee".</summary>
    public string ChangedBy => OnBehalfOf is null ? DisplayName : $"{OnBehalfOf} on behalf of {DisplayName}";

    /// <summary>Whether <paramref name="login"/> is this person's (case ignored; an empty one is nobody's).</summary>
    public bool IsLogin(string? login) => (login ?? "").Trim().Length > 0 && string.Equals((login ?? "").Trim(), Login, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="email"/> is this person's (case ignored; an empty one is nobody's).</summary>
    public bool Is(string? email) => Email.Length > 0 && string.Equals((email ?? "").Trim(), Email, StringComparison.OrdinalIgnoreCase);
}

/// <summary>People: who has signed in (or came from MyCRM's users), their MyCRMRecords roles (dbo.CrUser, dbo.CrUserRole) and MyCRM role.</summary>
public sealed class People(IRecordStore store, Func<DateTime> clock)
{
    public AppUser? User(string login) => store.FindFirst<AppUser>(Query.Where("Login", login));

    public IReadOnlyList<string> RolesOf(string login) =>
        store.Find<UserRole>(Query.Where("Login", login).And("IsActive", true)).Select(r => r.Role).Where(Roles.IsKnown).Distinct()
            .OrderBy(r => Roles.All.Select(k => k.Name).ToList().IndexOf(r)).ToList();

    /// <summary>MyCRM's Role(): the role of the person's first CRM user record (active or not); "" for none.</summary>
    public string CrmRoleOf(string login) =>
        store.Find<CrmUser>(Query.Where("Login", login)).OrderBy(u => u.Id).FirstOrDefault()?.Role ?? "";

    /// <summary>MyCRM's IsSalesAdmin: any of the person's CRM user records (active or not) has the role Admin or SalesAdmin.</summary>
    public bool IsCrmSalesAdmin(string login) =>
        store.Find<CrmUser>(Query.Where("Login", login)).Any(u => CrmRoles.Is(u.Role, CrmRoles.SalesAdmins));

    /// <summary>Records that the person opened MyCRMRecords (at most once an hour, so reading rarely writes).</summary>
    public void Touch(string login, string displayName, string email)
    {
        var now = clock();
        var user = User(login);
        if (user is null)
        {
            store.Insert(new AppUser { Login = login, DisplayName = Strings.Shorten(displayName, 256), Email = Strings.Shorten(email, 256), LastSeenAt = now });
            NamesChanged();
            return;
        }
        if (user.LastSeenAt is { } seen && now - seen < TimeSpan.FromHours(1) && (displayName.Length == 0 || user.DisplayName == displayName)) return;
        user.LastSeenAt = now;
        if (displayName.Length > 0) user.DisplayName = Strings.Shorten(displayName, 256);
        if (email.Length > 0) user.Email = Strings.Shorten(email, 256);
        store.Update(user);
        NamesChanged();
    }

    /// <summary>Gives or takes away roles. Only administrators give Admin, and nobody takes their own Admin role away.</summary>
    public OperationResult SetRoles(Actor actor, string login, string? displayName, string? email, IReadOnlyList<string> roles)
    {
        login = login.Trim();
        if (login.Length == 0 || login.Length > 256) return OperationResult.Failure(ResultCodes.Validation, "Choose a person.");
        var wanted = roles.Where(Roles.IsKnown).Distinct().ToList();
        var had = RolesOf(login);
        if (string.Equals(login, actor.Login, StringComparison.OrdinalIgnoreCase) && had.Contains(Roles.Admin) && !wanted.Contains(Roles.Admin))
            return OperationResult.Failure(ResultCodes.Forbidden, "You cannot take away your own Administrator role.");
        store.Batch(s =>
        {
            if (s.FindFirst<AppUser>(Query.Where("Login", login)) is null)
                s.Insert(new AppUser { Login = login, DisplayName = Strings.Shorten(displayName ?? login, 256), Email = Strings.Shorten(email ?? "", 256) });
            NamesChanged();
            // A role taken away is marked, never deleted: who had which role, and until when, stays known.
            var active = s.Find<UserRole>(Query.Where("Login", login).And("IsActive", true));
            foreach (var role in active.Where(r => !wanted.Contains(r.Role) || active.First(a => a.Role == r.Role) != r))
            {
                role.IsActive = false;
                role.RemovedOn = clock();
                role.RemovedBy = Strings.Shorten(actor.ChangedBy, 600);
                s.Update(role);
            }
            foreach (var role in wanted.Where(w => !active.Any(a => a.Role == w))) s.Insert(new UserRole { Login = login, Role = role });
        });
        return OperationResult.Success(wanted.Count == 0 ? $"{login} has no role now." : $"{login}: {string.Join(", ", wanted)}.");
    }

    /// <summary>The email addresses of everyone with one of the roles (for the emails to a role).</summary>
    public IReadOnlyList<string> EmailsWithRole(params string[] roles)
    {
        var logins = store.Find<UserRole>(Query.Where("IsActive", true).In("Role", roles.Cast<object>())).Select(r => r.Login).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (logins.Count == 0) return Array.Empty<string>();
        return store.Find<AppUser>(Query.All().In("Login", logins.Cast<object>())).Select(u => u.Email.Trim()).Where(Strings.IsEmail)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The people with one of the roles, by name .</summary>
    public IReadOnlyList<AppUser> WithRole(params string[] roles)
    {
        var logins = store.Find<UserRole>(Query.Where("IsActive", true).In("Role", roles.Cast<object>())).Select(r => r.Login).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (logins.Count == 0) return Array.Empty<AppUser>();
        return store.Find<AppUser>(Query.All().In("Login", logins.Cast<object>())).OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A person's name as last seen (their login when unknown, "" for none).</summary>
    public string NameOf(string login)
    {
        if (string.IsNullOrWhiteSpace(login)) return "";
        return Names().TryGetValue(login.Trim(), out var name) ? name : login;
    }

    // Everyone's name, read at once and kept a minute: the pages name dozens of people each (salesperson, created by,
    // updated by…), which read one by one took a query each. Read again after a change made here.
    private readonly object _namesGate = new();
    private Dictionary<string, string>? _names;
    private DateTime _namesAt;

    private Dictionary<string, string> Names()
    {
        lock (_namesGate)
        {
            if (_names is not null && DateTime.UtcNow - _namesAt < TimeSpan.FromMinutes(1)) return _names;
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in store.Find<AppUser>())
                if (u.Login.Trim().Length > 0 && u.DisplayName.Length > 0 && !names.ContainsKey(u.Login.Trim())) names[u.Login.Trim()] = u.DisplayName;
            _names = names;
            _namesAt = DateTime.UtcNow;
            return names;
        }
    }

    private void NamesChanged()
    {
        lock (_namesGate) _names = null;
    }

    /// <summary>
    /// NameOf for many people at once (one query, not one per person): the answer gives each login's name, the login
    /// itself when MyCRMRecords has no name for it.
    /// </summary>
    public Func<string, string> NamesOf(IEnumerable<string> logins)
    {
        var names = Names();
        return login => string.IsNullOrWhiteSpace(login) ? "" : names.TryGetValue(login.Trim(), out var n) ? n : login;
    }

    /// <summary>The person's email address as last seen, or "".</summary>
    public string EmailOf(string login) => User(login)?.Email.Trim() ?? "";

    /// <summary>
    /// The people offered in the people choices: everyone MyCRMRecords knows with an email address (MyCRM's users the
    /// import brought before MyCRMRecords read MyCRM's tables directly, and everyone who opened MyCRMRecords), by name.
    /// </summary>
    public IReadOnlyList<AppUser> Everyone() =>
        store.Find<AppUser>().Where(u => u.IsActive && u.Login.Length > 0 && Strings.IsEmail(u.Email))
            .OrderBy(u => u.DisplayName.Length > 0 ? u.DisplayName : u.Login, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Adds someone found in Active Directory, so they can be chosen (their record is kept as it is when known).</summary>
    public AppUser Remember(string login, string displayName, string email)
    {
        login = Strings.Shorten(login.Trim(), 256);
        if (User(login) is { } known)
        {
            if (known.Email.Length == 0 && Strings.IsEmail(email))
            {
                known.Email = Strings.Shorten(email.Trim(), 256);
                store.Update(known);
            }
            return known;
        }
        NamesChanged();
        return store.Insert(new AppUser { Login = login, DisplayName = Strings.Shorten(displayName.Trim().Length > 0 ? displayName.Trim() : login, 256), Email = Strings.Shorten(email.Trim(), 256) });
    }
}
