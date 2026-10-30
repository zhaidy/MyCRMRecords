using System.Globalization;
using MyCRMRecords.Data;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Api;

/// <summary>
/// The Activities and Projects pages' API (api.ashx/activities..., api.ashx/projects..., api.ashx/prospects?company=), as
/// the signed-in person: MyCRM's activities, projects and (read only) prospects, in MyCRM's tables. Every change is
/// checked again in the domain and made only while "Let MyCRMRecords change MyCRM's data" is on.
/// </summary>
public sealed class ProjectsApi(CrmApp w)
{
    public static bool Handles(string path) => path.Trim('/').Split('/')[0] is "activities" or "projects" or "prospects";

    public sealed class LoginInput
    {
        public string? Login { get; set; }
    }

    public ApiResponse Handle(string method, string path, string body, IReadOnlyDictionary<string, string> query, ApiUser user)
    {
        var parts = path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var actor = user.ToActor(w.People);
        var get = method == "GET";
        var post = method == "POST";
        string Q(string name) => query.TryGetValue(name, out var v) ? (v ?? "").Trim() : "";
        long Id(int index) => parts.Length > index && long.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;
        long QId(string name) => long.TryParse(Q(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;
        int Skip() => int.TryParse(Q("skip"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var skip) ? Math.Max(0, skip) : 0;
        using var author = MyCrmStore.As(actor.ChangedBy);
        try
        {
            switch (parts[0])
            {
                case "prospects" when get && parts.Length == 1:
                    return Prospects(actor, QId("company"));

                case "activities" when get && parts.Length == 1 && QId("company") > 0:
                    return CompanyActivities(actor, QId("company"), Q("when"), Q("action"), Skip());
                case "activities" when get && parts.Length == 1:
                    return ListActivities(actor, Filter(query));
                case "activities" when get && parts.Length == 2 && parts[1] == "choices":
                    return ActivityChoices(actor, QId("company"));
                case "activities" when get && parts.Length == 2 && parts[1] == "companies":
                    return ApiResponse.Of(new { companies = w.Activities.FindCompanies(actor, Q("q")).Select(c => new { id = c.Id, name = c.Name }) });
                case "activities" when get && parts.Length == 2:
                    return ActivityDetail(actor, Id(1));
                case "activities" when post && parts.Length == 2:
                    return ApiResponse.Result(w.Activities.Save(actor, Id(1), Input.Read<Activities.ActivityInput>(body)));
                case "activities" when post && parts.Length == 3 && parts[2] == "delete":
                    return ApiResponse.Result(w.Activities.Delete(actor, Id(1)));

                case "projects" when get && parts.Length == 1 && QId("company") > 0:
                    return CompanyProjects(actor, QId("company"));
                case "projects" when get && parts.Length == 1:
                    return ListProjects(actor, Q("show"), Q("q"), Skip());
                case "projects" when get && parts.Length == 2 && parts[1] == "companies":
                    return ApiResponse.Of(new { companies = w.Projects.FindCompanies(Q("q")).Select(CompanyJson) });
                case "projects" when get && parts.Length == 2:
                    return ProjectDetail(actor, Id(1));
                case "projects" when get && parts.Length == 3 && parts[2] == "reach":
                {
                    if (w.Projects.Get(Id(1)) is not { } p || !w.Projects.MaySee(actor, p)) return ApiResponse.NotFound();
                    var (temporary, companies) = w.Projects.WouldReach(p.Id, Q("login"));
                    return ApiResponse.Of(new { temporary, companies, name = w.People.NameOf(Q("login")) });
                }
                case "projects" when post && parts.Length == 2 && Id(1) == 0:
                    return ApiResponse.Result(w.Projects.Create(actor, Input.Read<Projects.ProjectInput>(body)));
                case "projects" when post && parts.Length == 2:
                    return ApiResponse.Result(w.Projects.Save(actor, Id(1), Input.Read<Projects.ProjectInput>(body)));
                case "projects" when post && parts.Length == 3 && parts[2] == "companies":
                    return ApiResponse.Result(w.Projects.AddCompanies(actor, Id(1), Input.Read<Projects.CompaniesInput>(body).Companies ?? new List<long>()));
                case "projects" when post && parts.Length == 5 && parts[2] == "companies" && parts[4] == "remove":
                    return ApiResponse.Result(w.Projects.RemoveCompany(actor, Id(1), Id(3)));
                case "projects" when post && parts.Length == 3 && parts[2] == "members":
                    return ApiResponse.Result(w.Projects.AddMember(actor, Id(1), Input.Read<Projects.MemberInput>(body)));
                case "projects" when post && parts.Length == 5 && parts[2] == "members" && parts[4] == "remove":
                    return ApiResponse.Result(w.Projects.RemoveMember(actor, Id(1), Id(3)));
                case "projects" when post && parts.Length == 3 && parts[2] == "close":
                    return ApiResponse.Result(w.Projects.Close(actor, Id(1)));
                case "projects" when post && parts.Length == 3 && parts[2] == "delete":
                    return ApiResponse.Result(w.Projects.Delete(actor, Id(1)));
            }
            return ApiResponse.NotFound();
        }
        catch (BadInputException ex)
        {
            return ApiResponse.BadRequest(ex.Message);
        }
        catch (MyCrmRefusedException ex)
        {
            return ApiResponse.Fail(409, ResultCodes.Conflict, ex.Message);
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or TimeoutException or InvalidOperationException)
        {
            return ApiResponse.Fail(503, "UNAVAILABLE", "MyCRM's tables could not be read: " + ex.Message);
        }
    }

    private static string Date(DateTime? date) => date is { } d && d.Year > 1900 ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";

    private static object CompanyJson(Projects.CompanyRow c) => new { id = c.Id, name = c.Name, brn = c.Brn, salesperson = c.Salesperson, territory = c.Territory, active = c.Active };

    /// <summary>The Activities page's filter from the address: when, q, type, owner, from, to, skip.</summary>
    public static Activities.Filter Filter(IReadOnlyDictionary<string, string> query)
    {
        string Q(string name) => query.TryGetValue(name, out var v) ? (v ?? "").Trim() : "";
        DateTime? Day(string name) => DateTime.TryParseExact(Q(name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        var when = Q("when") is "today" or "past" or "all" ? Q("when") : "upcoming";
        return new Activities.Filter(when, Strings.Shorten(Q("q"), 100), Q("type"), Q("owner").Length > 0 ? Q("owner") : "team", Day("from"), Day("to"),
            int.TryParse(Q("skip"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var skip) ? Math.Max(0, skip) : 0);
    }

    private static object ActivityJson(Activities.Row r) => new
    {
        id = r.Activity.Id, date = Date(r.Activity.VisitDate), endDate = Date(r.Activity.ToDate), start = r.Activity.StartTime, end = r.Activity.EndTime,
        action = ActivityActions.List.Label(r.Activity.Action), types = r.Types, remarks = r.Activity.Remarks, companyId = r.Activity.CompanyId, company = r.Company,
        contacts = r.Contacts, owner = r.OwnerName, ownerLogin = r.Activity.Owner, status = r.Status,
    };

    private ApiResponse ListActivities(Actor actor, Activities.Filter filter)
    {
        var (total, rows) = w.Activities.List(actor, filter);
        var list = w.Access.UserList(actor.Login, actor.CrmRole);
        return ApiResponse.Of(new
        {
            total, skip = filter.Skip, take = Activities.PageSize, rows = rows.Select(ActivityJson),
            types = ActivityTypes.List.All.Select(c => new { code = c.Code, label = c.Label }),
            team = list.Where(l => !actor.IsLogin(l)).Select(l => new { login = l, name = w.People.NameOf(l) }).OrderBy(p => p.name, StringComparer.OrdinalIgnoreCase),
            me = actor.Login, canCreate = Activities.MaySave(actor), today = Date(w.Clock().Date),
        });
    }

    private ApiResponse CompanyActivities(Actor actor, long companyId, string when, string action, int skip)
    {
        if (w.Companies.Get(companyId) is not { } company) return ApiResponse.NotFound();
        if (!w.Companies.CanOpen(actor, company)) return ApiResponse.Forbidden("You don't have permissions to view this company.");
        var (total, rows) = w.Activities.ForCompany(actor, companyId, when, action, skip);
        return ApiResponse.Of(new
        {
            total, skip, take = Activities.PageSize, rows = rows.Select(ActivityJson), canCreate = Activities.MaySave(actor),
            actions = ActivityActions.List.All.Where(c => !ActivityActions.Inactive.Contains(c.Code)).Select(c => new { code = c.Code, label = c.Label }),
        });
    }

    private ApiResponse ActivityChoices(Actor actor, long companyId)
    {
        Company? company = null;
        if (companyId > 0)
        {
            company = w.Companies.Get(companyId);
            if (company is null) return ApiResponse.NotFound();
        }
        var c = w.Activities.ChoicesFor(actor, company?.Id ?? 0);
        var companies = company is null ? w.Activities.CompaniesFor(actor) : null;
        return ApiResponse.Of(new
        {
            me = actor.Login,
            actions = ActivityActions.List.All.Select(x => new { code = x.Code, label = x.Label, active = !ActivityActions.Inactive.Contains(x.Code) }),
            types = ActivityTypes.List.All.Where(x => x.Code != ActivityTypes.FirstAppointmentAttended).Select(x => new { code = x.Code, label = x.Label }),
            participants = c.Participants.Select(p => new { login = p.Login, name = p.Name }),
            owners = c.Owners.Select(p => new { login = p.Login, name = p.Name }),
            contacts = c.Contacts.Select(x => new { id = x.Id, name = x.Name }),
            prospects = c.Prospects.Select(x => new { id = x.Id, name = x.Name }),
            projects = c.Projects.Select(x => new { id = x.Id, name = x.Name }),
            projectsShown = c.ProjectsShown,
            // Every company for the roles that open every company (found by a search), else the person's companies.
            searchCompanies = company is null && companies is null,
            companies = companies?.Select(x => new { id = x.Id, name = x.Name }),
        });
    }

    private ApiResponse ActivityDetail(Actor actor, long id)
    {
        if (w.Activities.Read(actor, id) is not { } d) return ApiResponse.NotFound();
        var a = d.Activity;
        return ApiResponse.Of(new
        {
            activity = new
            {
                id = a.Id, companyId = a.CompanyId, company = d.Company, action = a.Action, actionLabel = ActivityActions.List.Label(a.Action), remarks = a.Remarks,
                date = Date(a.VisitDate), endDate = Date(a.ToDate), start = a.StartTime, end = a.EndTime, owner = a.Owner, ownerName = d.OwnerName, completed = a.IsVisited,
                status = d.Status, typeLabels = d.Types,
                types = w.Store.Find<ActivityTypeLink>(Query.Where("CompanyActivityId", a.Id)).Select(t => t.ActivityType).Distinct(),
                participants = d.Participants.Select(p => new { login = p.Login, name = p.Name }),
                contacts = d.Contacts.Select(x => new { id = x.Id, name = x.Name }),
                prospects = d.Prospects.Select(x => new { id = x.Id, name = x.Name }),
                projects = d.Projects.Select(x => new { id = x.Id, name = x.Name }),
                updatedBy = w.People.NameOf(a.UpdatedBy), updatedDate = Date(a.UpdatedDate),
            },
            canEdit = d.CanEdit,
        });
    }

    private ApiResponse Prospects(Actor actor, long companyId)
    {
        if (w.Companies.Get(companyId) is not { } company) return ApiResponse.NotFound();
        if (!w.Companies.CanOpen(actor, company)) return ApiResponse.Forbidden("You don't have permissions to view this company.");
        return ApiResponse.Of(new
        {
            rows = w.Activities.Prospects(companyId).Select(p => new
            {
                id = p.Id, remarks = p.Remarks, type = ProspectTypes.List.Label(p.ProspectType), stage = ProspectStages.List.Label(p.Stage),
                status = ProspectStatuses.List.Label(p.ProspectStatus), forecast = p.Forecast, totalPrice = p.TotalPrice, sumUpMonth = p.SumUpMonth, sumUpYear = p.SumUpYear,
                owner = w.People.NameOf(p.Owner), created = Date(p.DateCreated), updated = Date(p.UpdatedDate), updatedBy = w.People.NameOf(p.UpdatedBy),
                closed = Date(p.DateClosed), estimatedOrder = Date(p.EstimatedOrderDate), isClosed = p.IsClosed, isCancelled = p.IsCancelled, contact = p.ContactName,
            }),
            myCrm = w.Settings().MyCrm.Url,
        });
    }

    private ApiResponse ListProjects(Actor actor, string show, string q, int skip)
    {
        var (total, rows) = w.Projects.List(actor, show.Length == 0 ? "open" : show, q, skip);
        var name = w.People.NamesOf(rows.SelectMany(r => new[] { r.Project.Owner, r.Project.UpdatedBy }));
        return ApiResponse.Of(new
        {
            total, skip, take = Projects.PageSize,
            rows = rows.Select(r => new
            {
                id = r.Project.Id, name = r.Project.ProjectName, status = ProjectStatuses.List.Label(r.Project.Status), companies = r.Companies, companyCount = r.CompanyCount, members = r.Members,
                caretaker = r.Caretaker, owner = name(r.Project.Owner), endDate = Date(r.Project.EndDate), updatedDate = Date(r.Project.UpdatedDate),
                updatedBy = name(r.Project.UpdatedBy), closed = r.Project.IsClosed,
            }),
            canCreate = Projects.MayCreate(actor),
            statuses = ProjectStatuses.List.All.Where(c => c.Code != ProjectStatuses.Close).Select(c => new { code = c.Code, label = c.Label }),
            today = Date(w.Clock().Date),
        });
    }

    private ApiResponse ProjectDetail(Actor actor, long id)
    {
        if (w.Projects.Get(id) is not { } p)
        {
            // A deleted project (MyCRM keeps it, inactive): said as such; its name, day and who deleted it only to its owner and Sales Admins.
            if (id <= 0 || w.Store.Get<Project>(id) is not { IsActive: false } gone) return ApiResponse.NotFound();
            var told = actor.IsSalesAdmin || actor.IsLogin(gone.Owner);
            var when = gone.UpdatedDate is { } day ? " on " + day.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : "";
            var by = gone.UpdatedBy.Trim().Length > 0 ? " by " + w.People.NameOf(gone.UpdatedBy) : "";
            return ApiResponse.Fail(404, "DELETED", told ? $"The project {gone.ProjectName.Trim()} was deleted{when}{by}." : "This project was deleted.");
        }
        if (!w.Projects.MaySee(actor, p)) return ApiResponse.Forbidden("You don't have permissions to view this project: neither you nor your team are in it.");
        var today = w.Clock().Date;
        var (losing, companyCount) = w.Projects.LosingAccess(p.Id);
        var (companyTotal, companyRows) = w.Projects.Companies(p.Id, Projects.ListedOnPage);
        var activities = w.Projects.Activities(p.Id);
        var names = activities.Where(a => a.CompanyId is not null).Select(a => a.CompanyId!.Value).Distinct()
            .ToDictionary(c => c, c => w.Store.Get<Company>(c)?.Name.Trim() ?? "");
        var mayChangeMembers = Projects.MayChangeMembers(actor, p);
        var personName = w.People.NamesOf(activities.Select(a => a.Owner).Concat(new[] { p.Owner, p.UpdatedBy }));
        return ApiResponse.Of(new
        {
            project = new
            {
                id = p.Id, name = p.ProjectName, status = p.Status, statusLabel = ProjectStatuses.List.Label(p.Status), remarks = p.Remarks, startDate = Date(p.StartDate),
                endDate = Date(p.EndDate), owner = p.Owner, ownerName = personName(p.Owner), closed = p.IsClosed, open = ProjectTerritories.IsOpen(p, today),
                updatedDate = Date(p.UpdatedDate), updatedBy = personName(p.UpdatedBy),
            },
            companies = companyRows.Select(CompanyJson), companyTotal,
            members = w.Projects.Members(p.Id).Select(m => new
            {
                id = m.Member.Id, login = m.Member.User, name = m.Name, role = m.Member.Role, roleLabel = ProjectRoles.List.Label(m.Member.Role), reached = m.Reached, reachedCount = m.ReachedCount,
                isOwner = m.Member.Role == ProjectRoles.ProjectManager && Strings.SameText(m.Member.User, p.Owner),
            }),
            activities = activities.Select(a => new
            {
                id = a.Id, date = Date(a.VisitDate), action = ActivityActions.List.Label(a.Action), remarks = a.Remarks, companyId = a.CompanyId,
                company = a.CompanyId is { } c && names.TryGetValue(c, out var n) ? n : "", owner = personName(a.Owner), status = Activities.StatusOf(a, today),
            }),
            history = w.Projects.History(p.Id).Select(h => new { at = h.At, what = h.What, by = h.By }),
            losing, companyCount,
            can = new { change = Projects.MayChange(actor, p) && !p.IsClosed, delete = Projects.MayChange(actor, p), members = mayChangeMembers && !p.IsClosed },
            statuses = ProjectStatuses.List.All.Where(c => c.Code != ProjectStatuses.Close || p.Status == ProjectStatuses.Close).Select(c => new { code = c.Code, label = c.Label }),
            roles = ProjectRoles.List.All.Where(c => c.Code != ProjectRoles.ProjectManager).Select(c => new { code = c.Code, label = c.Label }),
            candidates = mayChangeMembers && !p.IsClosed ? w.Projects.Candidates().Select(c => (object)new { login = c.Login, name = c.Name }).ToList() : new List<object>(),
            today = Date(today),
        });
    }

    private ApiResponse CompanyProjects(Actor actor, long companyId)
    {
        if (w.Companies.Get(companyId) is not { } company) return ApiResponse.NotFound();
        if (!w.Companies.CanOpen(actor, company)) return ApiResponse.Forbidden("You don't have permissions to view this company.");
        return ApiResponse.Of(new
        {
            rows = w.Projects.ForCompany(companyId).Select(x => new
            {
                id = x.Project.Id, name = x.Project.ProjectName, status = ProjectStatuses.List.Label(x.Project.Status), owner = w.People.NameOf(x.Project.Owner),
                endDate = Date(x.Project.EndDate), closed = x.Project.IsClosed, canOpen = w.Projects.MaySee(actor, x.Project),
                members = x.Members.Select(m => new { name = w.People.NameOf(m.User), role = ProjectRoles.List.Label(m.Role) }),
            }),
            canCreate = Projects.MayCreate(actor),
        });
    }
}
