using MyCRMRecords.Domain;

namespace MyCRMRecords.Api;

/// <summary>An answer: the HTTP status and its JSON.</summary>
public sealed record ApiResponse(int Status, string Json)
{
    public static ApiResponse Of(object value, int status = 200) => new(status, MyCRMRecords.Json.Serialize(value));

    public static ApiResponse Result(OperationResult result, object? extra = null)
    {
        var status = result.Ok ? 200 : result.Code switch
        {
            ResultCodes.NotFound => 404,
            ResultCodes.Forbidden => 403,
            ResultCodes.Conflict => 409,
            ResultCodes.Failed => 502,
            _ => 400,
        };
        return Of(new { result = new { ok = result.Ok, code = result.Code, message = result.Message, errors = result.Errors, id = result.Id }, data = extra }, status);
    }

    public static ApiResponse Ok(string message, object? extra = null) => Result(OperationResult.Success(message), extra);

    public static ApiResponse Fail(int status, string code, string message) =>
        Of(new { result = new { ok = false, code, message, errors = Array.Empty<FieldError>() } }, status);

    public static ApiResponse NotFound() => Fail(404, ResultCodes.NotFound, "Not found.");

    public static ApiResponse BadRequest(string message = "The request could not be read.") => Fail(400, "BAD_REQUEST", message);

    public static ApiResponse Forbidden(string message = "You do not have the role for this.") => Fail(403, ResultCodes.Forbidden, message);
}

/// <summary>
/// The signed-in person: login as DOMAIN\name, display name and email address from Active Directory, whether Web.config
/// lists them as an administrator, and, while an administrator is signed in as them, that person.
/// </summary>
public sealed record ApiUser(string Login, string DisplayName, string Email = "", bool IsListedAdmin = false, ApiUser? Impersonator = null)
{
    public string ChangedBy => Impersonator is null ? DisplayName : $"{Impersonator.DisplayName} on behalf of {DisplayName}";

    /// <summary>
    /// The person as the rules see them: their roles from the User roles page (Administrator too when Web.config lists
    /// them) and, when <paramref name="orders"/> is given, their MyOrders profile (read only when a rule needs it).
    /// </summary>
    public Actor ToActor(People people, OrdersAccess? orders = null)
    {
        var roles = people.RolesOf(Login).ToList();
        if (IsListedAdmin && !roles.Contains(Roles.Admin)) roles.Add(Roles.Admin);
        // Their MyCRM role comes from their CRM user records (the CRM users page).
        return new Actor(Login, DisplayName.Length > 0 ? DisplayName : Login, Email.Trim().ToLowerInvariant(), roles, Impersonator?.DisplayName)
        {
            CrmRole = people.CrmRoleOf(Login),
            CrmSalesAdmin = people.IsCrmSalesAdmin(Login),
            OrdersFrom = orders is null ? () => OrdersProfile.None : () => orders.Profile(Login, Email),
        };
    }
}

/// <summary>A person found in Active Directory (User roles page).</summary>
public sealed record DirectoryPerson(string Login, string DisplayName, string Email);
