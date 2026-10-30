using System.Globalization;
using MyCRMRecords.Data;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Api;

/// <summary>
/// The Machines pages' API (api.ashx/machines..., api.ashx/machineaccess...), as the signed-in person: Advance's machines
/// they see (read only), a company's (machines?company=) and its copies per month (machines/copies?company=), a machine's page (machines/detail?serial=&amp;item=), and for
/// Sales Admins MyMachines' temporary access, given and removed only when switched on.
/// </summary>
public sealed class MachinesApi(CrmApp w)
{
    public static bool Handles(string path) => path.Trim('/').Split('/')[0] is "machines" or "machineaccess";

    public sealed class GiveInput
    {
        public string? Login { get; set; }
        public long CompanyId { get; set; }
    }

    public ApiResponse Handle(string method, string path, string body, IReadOnlyDictionary<string, string> query, ApiUser user)
    {
        var parts = path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var actor = user.ToActor(w.People);
        string Q(string name) => query.TryGetValue(name, out var v) ? (v ?? "").Trim() : "";
        try
        {
            if (parts[0] == "machineaccess") return Access(method, parts, body, actor);
            if (method != "GET") return ApiResponse.NotFound();
            if (!w.Machines.Advance.Available)
                return ApiResponse.Fail(503, "NOT_CONNECTED", actor.IsAdmin
                    ? "Advance's machines cannot be read yet: set up AdvanceConnector on the admin page (Settings, Connections tab)."
                    : "The machines cannot be read now: an administrator has to set up the connection to Advance first.");
            if (parts.Length == 2 && parts[1] == "detail")
            {
                var months = Q("months") == "6" ? 6 : 12;
                var (detail, allowed) = w.Machines.Get(actor, Q("serial"), Q("item"), months);
                if (!allowed) return ApiResponse.Forbidden("You don't have permissions to view this machine: its customer is not in your or your team's territories, your projects or your temporary access.");
                if (detail is null) return ApiResponse.Fail(404, ResultCodes.NotFound, "Advance has no machine with that serial number.");
                return ApiResponse.Of(new
                {
                    machine = Row(new MachineListRow(detail.Machine.Machine, detail.Via, detail.CompanyId)),
                    installed = detail.Machine.Installed, location = detail.Machine.Location, contact = detail.Machine.Contact,
                    phone = detail.Machine.Phone, address = detail.Machine.Address,
                    meters = detail.Meters, services = detail.Services, toner = detail.Toner, lastContract = detail.LastContract,
                    months, from = Machines.From(w.Clock().Date, months),
                });
            }
            if (parts.Length == 2 && parts[1] == "copies")
            {
                if (!long.TryParse(Q("company"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ofCompany) || ofCompany <= 0) return ApiResponse.NotFound();
                if (w.Machines.CompanyCopies(actor, ofCompany) is not { } copies)
                    return ApiResponse.Forbidden("You don't have permissions to view this company's machines: it is not in your or your team's territories.");
                return ApiResponse.Of(new
                {
                    machines = copies.Machines,
                    months = copies.Months.Select(m => new { year = m.Year, month = m.Month, blackWhite = m.BlackWhite, colour = m.Colour }),
                });
            }
            if (parts.Length != 1) return ApiResponse.NotFound();
            if (long.TryParse(Q("company"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var companyId) && companyId > 0)
            {
                var all = Q("all") is "1" or "true";
                var found = w.Machines.ForCompany(actor, companyId, all);
                if (found is not { } company)
                    return ApiResponse.Forbidden("You don't have permissions to view this company's machines: it is not in your or your team's territories.");
                return ApiResponse.Of(new { total = company.Total, active = company.ActiveCount, all, rows = company.Rows.Select(Row) });
            }
            var search = Search(query);
            var page = w.Machines.List(actor, search);
            return ApiResponse.Of(new
            {
                total = page.Total, skip = search.Skip, take = Machines.PageSize, rows = page.Rows.Select(Row), needsSearch = page.NeedsSearch,
                searchFrom = Machines.SearchFrom, limited = !actor.SeesAllCompanies, manageAccess = Machines.MayManageAccess(actor),
                coverages = Coverages.All.Select(c => new { code = c.Code, name = c.Name }),
                financeTypes = FinanceTypes.All.Select(f => new { code = f.Code, name = f.Name }),
            });
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or TimeoutException or InvalidOperationException)
        {
            return ApiResponse.Fail(503, "UNAVAILABLE", "Advance's machines could not be read: " + ex.Message);
        }
    }

    /// <summary>A machine as the pages read it.</summary>
    private static object Row(MachineListRow r)
    {
        var m = r.Machine;
        return new
        {
            serial = m.Serial, item = m.Item, model = m.Model, description = m.Description, customerNo = m.CustomerNo, customer = m.Customer, brn = m.Brn,
            mifType = m.MifType, contractNo = m.ContractNo, contractType = m.ContractType, contractStart = m.ContractStart, contractEnd = m.ContractEnd,
            warrantyStart = m.WarrantyStart, warrantyEnd = m.WarrantyEnd, lastService = m.LastService, lastInvoice = m.LastInvoice, lastContract = m.LastContract, lastServiced = m.LastServiced,
            financeCode = m.FinanceCode, finance = m.Finance, coverage = m.Coverage, coverageName = Coverages.NameOf(m.Coverage), active = m.Active,
            via = r.Via, companyId = r.CompanyId,
        };
    }

    /// <summary>The list's search from the address: q, coverage, finance, skip.</summary>
    public static MachineSearch Search(IReadOnlyDictionary<string, string> query)
    {
        string Q(string name) => query.TryGetValue(name, out var v) ? (v ?? "").Trim() : "";
        return new MachineSearch
        {
            Keyword = Strings.Shorten(Q("q"), 100),
            Coverage = Coverages.Of(Q("coverage")) is var coverage && coverage != Coverages.Active ? coverage : "",
            Finance = FinanceTypes.Of(Q("finance")),
            Skip = int.TryParse(Q("skip"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var skip) ? Math.Max(0, skip) : 0,
        };
    }

    /// <summary>The Machine access page: MyMachines' temporary access (Sales Admins), given and removed only when switched on.</summary>
    private ApiResponse Access(string method, string[] parts, string body, Actor actor)
    {
        if (!Machines.MayManageAccess(actor)) return ApiResponse.Forbidden("The Machine access page is for Sales Admins (MyCRM role Admin or SalesAdmin).");
        var temporary = w.Machines.Temporary;
        if (!temporary.Available)
            return ApiResponse.Fail(503, "NOT_CONNECTED", "MyMachines' temporary access cannot be read yet: an administrator has to set up the OutSystems database first.");
        using var author = MyCrmStore.As(actor.ChangedBy);
        if (method == "GET" && parts.Length == 1)
            return ApiResponse.Of(new
            {
                canWrite = temporary.CanWrite, writesOff = temporary.CanWrite ? "" : TempAccessRules.WritesOff,
                rows = temporary.List().Select(r => new { r.Id, login = r.Login, name = r.UserName.Length > 0 ? r.UserName : r.Login, r.Email, customer = r.CustomerName, brn = r.Brn, given = r.GivenAt }),
                people = w.People.Everyone().Select(u => new { u.Login, name = u.DisplayName, u.Email }),
            });
        if (method == "POST" && parts.Length == 1)
        {
            var input = Input.Read<GiveInput>(body);
            return ApiResponse.Result(w.Machines.Give(actor, input.Login ?? "", input.CompanyId));
        }
        if (method == "POST" && parts.Length == 3 && parts[2] == "remove"
            && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return ApiResponse.Result(w.Machines.Remove(actor, id));
        return ApiResponse.NotFound();
    }
}
