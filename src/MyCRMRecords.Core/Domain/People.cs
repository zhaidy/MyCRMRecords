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

/// <summary>MyCRM's roles (Admin, BE, HR) and MyCRMRecords's administrators.</summary>
public static class Roles
{
    /// <summary>MyCRMRecords's administrator (the admin pages); not MyCRM's Admin, which is <see cref="FeedbackAdmin"/>.</summary>
    public const string Admin = "Admin";
    /// <summary>MyCRM's Admin role.</summary>
    public const string FeedbackAdmin = "FeedbackAdmin";
    public const string Be = "BE";
    public const string Hr = "HR";

    public sealed record RoleInfo(string Name, string Label, string Description);

    public static readonly IReadOnlyList<RoleInfo> All =
    [
        new(Admin, "Administrator", "Everything a Feedback Admin does, and the admin pages, user roles and the audit log."),
        new(FeedbackAdmin, "Feedback Admin", "MyCRM's Admin: sees every feedback, keeps the lists (divisions, categories, mediums, KPI dates), adds the customer details files, sets the satisfaction level and signs in as someone else."),
        new(Be, "BE", "Closes a feedback once the satisfaction is confirmed, and deletes feedbacks."),
        new(Hr, "HR", "Accepts or turns down the suggestions of internal challenges (HR - ESS), and sees the feedbacks waiting for HR."),
    ];

    public static bool IsKnown(string role) => All.Any(r => r.Name == role);
}

/// <summary>The person making a change: their login, name, email (lower case), roles, and who acts for them.</summary>
public sealed record Actor(string Login, string DisplayName, string Email, IReadOnlyList<string> Roles, string? OnBehalfOf = null)
{
    public bool Has(string role) => Roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
    public bool IsAdmin => Has(Domain.Roles.Admin);
    /// <summary>MyCRM's Admin: every feedback, the lists, the customer details files, the satisfaction level.</summary>
    public bool IsFeedbackAdmin => IsAdmin || Has(Domain.Roles.FeedbackAdmin);
    /// <summary>MyCRM's CheckBERole: Close Case and Delete.</summary>
    public bool IsBe => Has(Domain.Roles.Be);
    /// <summary>MyCRM's CheckHRRole: the HR - ESS section while a feedback is Pending HR.</summary>
    public bool IsHr => Has(Domain.Roles.Hr);

    /// <summary>"Jamie Lee", or "Alex Tan on behalf of Jamie Lee".</summary>
    public string ChangedBy => OnBehalfOf is null ? DisplayName : $"{OnBehalfOf} on behalf of {DisplayName}";

    /// <summary>Whether <paramref name="login"/> is this person's (case ignored; an empty one is nobody's).</summary>
    public bool IsLogin(string? login) => (login ?? "").Trim().Length > 0 && string.Equals((login ?? "").Trim(), Login, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="email"/> is this person's (case ignored; an empty one is nobody's).</summary>
    public bool Is(string? email) => Email.Length > 0 && string.Equals((email ?? "").Trim(), Email, StringComparison.OrdinalIgnoreCase);
}

/// <summary>People: who has signed in (or came from MyCRM's users), and their roles (dbo.CrUser, dbo.CrUserRole).</summary>
public sealed class People(IRecordStore store, Func<DateTime> clock)
{
    public AppUser? User(string login) => store.FindFirst<AppUser>(Query.Where("Login", login));

    public IReadOnlyList<string> RolesOf(string login) =>
        store.Find<UserRole>(Query.Where("Login", login).And("IsActive", true)).Select(r => r.Role).Where(Roles.IsKnown).Distinct()
            .OrderBy(r => Roles.All.Select(k => k.Name).ToList().IndexOf(r)).ToList();

    /// <summary>Records that the person opened MyCRMRecords (at most once an hour, so reading rarely writes).</summary>
    public void Touch(string login, string displayName, string email)
    {
        var now = clock();
        var user = User(login);
        if (user is null)
        {
            store.Insert(new AppUser { Login = login, DisplayName = Strings.Shorten(displayName, 256), Email = Strings.Shorten(email, 256), LastSeenAt = now });
            return;
        }
        if (user.LastSeenAt is { } seen && now - seen < TimeSpan.FromHours(1) && (displayName.Length == 0 || user.DisplayName == displayName)) return;
        user.LastSeenAt = now;
        if (displayName.Length > 0) user.DisplayName = Strings.Shorten(displayName, 256);
        if (email.Length > 0) user.Email = Strings.Shorten(email, 256);
        store.Update(user);
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
        var user = User(login);
        return user is not null && user.DisplayName.Length > 0 ? user.DisplayName : login;
    }

    /// <summary>The person's email address as last seen, or "".</summary>
    public string EmailOf(string login) => User(login)?.Email.Trim() ?? "";

    /// <summary>
    /// The people offered in the people choices (in charge, a task's person, Copy To): everyone MyCRMRecords knows with an
    /// email address (MyCRM's list of users came with the import, and everyone who opened MyCRMRecords), by name.
    /// </summary>
    public IReadOnlyList<AppUser> Everyone() =>
        store.Find<AppUser>().Where(u => u.Login.Length > 0 && Strings.IsEmail(u.Email))
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
        return store.Insert(new AppUser { Login = login, DisplayName = Strings.Shorten(displayName.Trim().Length > 0 ? displayName.Trim() : login, 256), Email = Strings.Shorten(email.Trim(), 256) });
    }
}
