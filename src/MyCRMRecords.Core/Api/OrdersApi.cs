using System.Globalization;
using MyCRMRecords.Data;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Api;

/// <summary>
/// The API behind MyCRMRecords's pages (api.ashx/...), as the signed-in person: the costing sheets they may see (the
/// list and its filters, a costing sheet's page) and, for administrators, the people to sign in as. Everything is read
/// only: MyCRMRecords changes nothing in MyOrders or MyCRM.
/// </summary>
public sealed class OrdersApi(CrmApp w)
{
    /// <summary>The paths of this API: orders/..., costingsheets..., requestforms...</summary>
    public static bool Handles(string path)
    {
        var first = path.Trim('/').Split('/')[0];
        return first is "orders" or "costingsheets" or "requestforms";
    }

    public ApiResponse Handle(string method, string path, string body, IReadOnlyDictionary<string, string> query, ApiUser user)
    {
        var parts = path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || method != "GET") return ApiResponse.NotFound();
        long Id(int index) => parts.Length > index && long.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;
        try
        {
            switch (parts[0])
            {
                case "orders" when parts.Length == 2 && parts[1] == "session":
                {
                    var actor = Actor(user);
                    return ApiResponse.Of(new
                    {
                        user = new
                        {
                            login = actor.Login, name = actor.DisplayName, email = actor.Email, roles = actor.Roles, changedBy = actor.ChangedBy,
                            crmRole = actor.Orders.Role, crmRoleLabel = CrmRoles.List.Label(actor.Orders.Role),
                        },
                        can = Can(actor),
                        connected = w.OrdersData.Available,
                    });
                }
                case "costingsheets" when parts.Length == 2 && parts[1] == "filters":
                {
                    var actor = Actor(user);
                    if (!w.OrdersData.Available) return NotConnected(actor);
                    var (statuses, salesReps, specialists) = w.CostingSheets.Filters(actor);
                    return ApiResponse.Of(new { statuses, salesReps, specialists });
                }
                case "costingsheets" when parts.Length == 1:
                {
                    var actor = Actor(user);
                    if (!w.OrdersData.Available) return NotConnected(actor);
                    var search = Search(query);
                    var page = w.CostingSheets.List(actor, search);
                    return ApiResponse.Of(new { total = page.Total, skip = search.Skip, take = search.Take, rows = page.Rows, can = Can(actor) });
                }
                case "costingsheets" when parts.Length == 3 && parts[2] == "print":
                {
                    var actor = Actor(user);
                    if (!w.OrdersData.Available) return NotConnected(actor);
                    var print = w.CostingSheets.Print(actor, Id(1));
                    if (print is null) return ApiResponse.Fail(404, ResultCodes.NotFound, "There is no such costing sheet, or it is not one you may see.");
                    if (!print.Sheet.CanPrint)
                        return ApiResponse.Forbidden("As in MyOrders, a costing sheet is printed once it is Pending Credit Check, Pending Credit GM Check, Pending Invoicing or Invoiced, by managers, heads, MD, Sales Admins, AP and stock allocators.");
                    return ApiResponse.Of(new { print });
                }
                case "costingsheets" when parts.Length == 2:
                {
                    var actor = Actor(user);
                    if (!w.OrdersData.Available) return NotConnected(actor);
                    var sheet = w.CostingSheets.Get(actor, Id(1));
                    return sheet is null
                        ? ApiResponse.Fail(404, ResultCodes.NotFound, "There is no such costing sheet, or it is not one you may see.")
                        : ApiResponse.Of(new { costingSheet = sheet, can = Can(actor) });
                }
                case "requestforms" when parts.Length == 2 && parts[1] == "filters":
                {
                    var actor = Actor(user);
                    if (!w.OrdersData.Available) return NotConnected(actor);
                    var (statuses, types, salesReps) = w.RequestForms.Filters(actor);
                    return ApiResponse.Of(new { statuses, types, salesReps });
                }
                case "requestforms" when parts.Length == 1:
                {
                    var actor = Actor(user);
                    if (!w.OrdersData.Available) return NotConnected(actor);
                    var search = RequestFormQuery(query);
                    var page = w.RequestForms.List(actor, search);
                    return ApiResponse.Of(new { total = page.Total, skip = search.Skip, take = search.Take, rows = page.Rows, can = Can(actor) });
                }
                case "requestforms" when parts.Length == 3 && parts[2] == "print":
                {
                    var actor = Actor(user);
                    if (!w.OrdersData.Available) return NotConnected(actor);
                    var found = w.RequestForms.Print(actor, Id(1));
                    if (found is not { } answer) return ApiResponse.Fail(404, ResultCodes.NotFound, "There is no such request form, or it is not one you may see.");
                    if (!answer.CanPrint)
                        return ApiResponse.Forbidden("As in MyOrders, a request form is printed once it is Pending Credit Check, Pending Allocation, Pending Invoicing or Backordered, by Sales Admins, Admins, managers, heads, MD, stock allocators, schedulers, Credit GM and the specialist managers.");
                    return ApiResponse.Of(new { print = answer.Print });
                }
                case "requestforms" when parts.Length == 2:
                {
                    var actor = Actor(user);
                    if (!w.OrdersData.Available) return NotConnected(actor);
                    var form = w.RequestForms.Get(actor, Id(1));
                    return form is null
                        ? ApiResponse.Fail(404, ResultCodes.NotFound, "There is no such request form, or it is not one you may see.")
                        : ApiResponse.Of(new { requestForm = form, can = Can(actor) });
                }
                case "orders" when parts.Length == 2 && parts[1] == "people":
                {
                    // The people an administrator may sign in as.
                    var actor = user.ToActor(w.People);
                    if (!actor.IsAdmin) return ApiResponse.Forbidden("Only administrators sign in as someone else.");
                    return ApiResponse.Of(new { people = w.OrdersAccess.PeopleToSignInAs(w.People.Everyone()).Select(p => new { p.Login, p.Name, p.Email, p.Role }) });
                }
            }
            return ApiResponse.NotFound();
        }
        catch (BadInputException ex)
        {
            return ApiResponse.BadRequest(ex.Message);
        }
    }

    private Actor Actor(ApiUser user) => user.ToActor(w.People, w.OrdersAccess);

    private static object Can(Actor actor) => new
    {
        seesAll = actor.SeesAllCostingSheets,
        seesMargins = CostingSheets.SeesMargins(actor),
        actAs = actor.IsAdmin,
        admin = actor.IsAdmin,
    };

    private static ApiResponse NotConnected(Actor actor) => ApiResponse.Fail(503, "NOT_CONNECTED", actor.IsAdmin
        ? "MyOrders' tables cannot be read yet: set up the OutSystems database on the admin page (Settings, Connections tab)."
        : "The costing sheets cannot be read now: an administrator has to set up the connection to MyOrders first.");

    /// <summary>The request forms list's search from the address: q, status, type, salesRep, createdFrom, createdTo, sort, desc, skip.</summary>
    public static RequestFormSearch RequestFormQuery(IReadOnlyDictionary<string, string> query)
    {
        string Q(string name) => query.TryGetValue(name, out var v) ? (v ?? "").Trim() : "";
        long? Long(string name) => long.TryParse(Q(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
        DateTime? Day(string name) =>
            DateTime.TryParseExact(Q(name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        var sort = Q("sort").ToLowerInvariant();
        return new RequestFormSearch
        {
            Keyword = Strings.Shorten(Q("q"), 200),
            StatusId = Long("status"),
            CompanyId = Long("company"),
            TypeId = Long("type"),
            SalesRepIds = Long("salesRep") is { } rep ? new[] { rep } : null,
            CreatedFrom = Day("createdFrom"),
            CreatedTo = Day("createdTo"),
            Sort = RequestFormSearch.Sorts.Contains(sort) ? sort : "created",
            Descending = Q("desc") != "0",
            Skip = int.TryParse(Q("skip"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var skip) ? Math.Max(0, skip) : 0,
            Take = 50,
        };
    }

    /// <summary>The list's search from the address: q, status, salesRep, specialist, hasSpecialist, created/approved/invoice From and To, sort, desc, skip.</summary>
    public static CostingSheetSearch Search(IReadOnlyDictionary<string, string> query)
    {
        string Q(string name) => query.TryGetValue(name, out var v) ? (v ?? "").Trim() : "";
        long? Long(string name) => long.TryParse(Q(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
        DateTime? Day(string name) =>
            DateTime.TryParseExact(Q(name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        var sort = Q("sort").ToLowerInvariant();
        return new CostingSheetSearch
        {
            Keyword = Strings.Shorten(Q("q"), 200),
            StatusId = Long("status"),
            CompanyId = Long("company"),
            SalesRepIds = Long("salesRep") is { } rep ? new[] { rep } : null,
            SpecialistIds = Long("specialist") is { } specialist ? new[] { specialist } : null,
            HasSpecialist = Q("hasSpecialist") is "Yes" or "No" ? Q("hasSpecialist") : "",
            CreatedFrom = Day("createdFrom"),
            CreatedTo = Day("createdTo"),
            HeadApprovedFrom = Day("approvedFrom"),
            HeadApprovedTo = Day("approvedTo"),
            InvoiceFrom = Day("invoiceFrom"),
            InvoiceTo = Day("invoiceTo"),
            Sort = CostingSheetSearch.Sorts.Contains(sort) ? sort : "id",
            Descending = Q("desc") != "0",
            Skip = int.TryParse(Q("skip"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var skip) ? Math.Max(0, skip) : 0,
            Take = 50,
        };
    }
}
