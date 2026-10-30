using System.Globalization;
using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>
/// MyCRM's temporary territories of its projects (TempTerritoryHandler, TempCompanyTerritoryHandler,
/// TempTerritoryHandlerWhenDel, CheckUserHasPermTerritory): while a project is open (active, not closed, ending today or
/// later), each active member but its Project Manager has one temporary territory with the project's companies they do not
/// own through an active permanent territory of their own. The territory is MyCRM's Territory row (IsTemporary) and its
/// companies are CompanyTerritory rows; nothing is deleted: what is taken away is inactive, and comes back active.
/// </summary>
public sealed class ProjectTerritories(Func<string, long?> userIdOf)
{
    /// <summary>A project gives access while it is active, not closed and ends today or later.</summary>
    public static bool IsOpen(Project p, DateTime today) => p.IsActive && !p.IsClosed && p.EndDate is { } end && end.Date >= today.Date;

    /// <summary>MyCRM's CheckUserHasPermTerritory: the person owns an active permanent territory with an active link to the company.</summary>
    public static bool OwnsPermanently(IRecordStore s, string login, long companyId)
    {
        var links = s.Find<CompanyTerritory>(Query.Where("CompanyId", companyId).And("IsActive", true));
        if (links.Count == 0) return false;
        return s.Find<Territory>(Query.Where("IsActive", true).In("Id", links.Select(l => (object)l.TerritoryId).Distinct()))
            .Any(t => Strings.SameText(t.Owner, login));
    }

    /// <summary>The companies the member reaches through the project only: none for its Project Manager or a project that is not open.</summary>
    public static IReadOnlyList<long> Needed(IRecordStore s, Project p, ProjectMember m, IReadOnlyCollection<long> companies, DateTime today) =>
        !IsOpen(p, today) || !m.IsActive || m.Role == ProjectRoles.ProjectManager
            ? Array.Empty<long>()
            : companies.Where(c => !OwnsPermanently(s, m.User, c)).ToList();

    /// <summary>MyCRM's number of a temporary territory: the member's OutSystems user Id followed by the company's Id (0 when unknown).</summary>
    public long NumberOf(string login, long companyId) =>
        userIdOf(login) is { } user && long.TryParse(user.ToString(CultureInfo.InvariantCulture) + companyId.ToString(CultureInfo.InvariantCulture),
            NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;

    /// <summary>
    /// Gives the member the temporary territory the project gives them now, with exactly the companies they need (made,
    /// changed or taken away); returns how many companies it gives them.
    /// </summary>
    public int Apply(IRecordStore s, Project p, ProjectMember m, IReadOnlyCollection<long> companies, DateTime today, string by)
    {
        var needed = Needed(s, p, m, companies, today);
        var territory = m.TerritoryId is { } id ? s.Get<TemporaryTerritory>(id) : null;
        if (territory is not null && !Strings.SameText(territory.Owner, m.User)) territory = null;
        if (needed.Count == 0)
        {
            if (territory is not null) TakeAway(s, territory);
            return 0;
        }
        if (territory is null)
        {
            territory = s.Insert(new TemporaryTerritory { Number = NumberOf(m.User, needed[0]), Owner = m.User, IsActive = true, IsOwner = false });
            m.TerritoryId = territory.Id;
            s.Update(m);
        }
        else if (!territory.IsActive)
        {
            territory.IsActive = true;
            s.Update(territory);
        }
        var links = s.Find<TemporaryCompanyTerritory>(Query.Where("TerritoryId", territory.Id));
        foreach (var company in needed)
        {
            var existing = links.Where(l => l.CompanyId == company).OrderBy(l => l.Id).ToList();
            if (existing.Any(l => l.IsActive)) continue;
            if (existing.FirstOrDefault() is { } again)
            {
                again.IsActive = true;
                s.Update(again);
            }
            else s.Insert(new TemporaryCompanyTerritory { TerritoryId = territory.Id, CompanyId = company, IsActive = true, CreatedBy = by });
        }
        foreach (var link in links.Where(l => l.IsActive && !needed.Contains(l.CompanyId)))
        {
            link.IsActive = false;
            s.Update(link);
        }
        return needed.Count;
    }

    /// <summary>MyCRM's TempTerritoryHandlerWhenDel: the member's temporary territory and its companies become inactive.</summary>
    public static void TakeAway(IRecordStore s, ProjectMember m)
    {
        if (m.TerritoryId is { } id && s.Get<TemporaryTerritory>(id) is { } territory) TakeAway(s, territory);
    }

    private static void TakeAway(IRecordStore s, TemporaryTerritory territory)
    {
        foreach (var link in s.Find<TemporaryCompanyTerritory>(Query.Where("TerritoryId", territory.Id).And("IsActive", true)))
        {
            link.IsActive = false;
            s.Update(link);
        }
        if (!territory.IsActive) return;
        territory.IsActive = false;
        s.Update(territory);
    }

    /// <summary>The companies of the member's temporary territory now (none when it is inactive).</summary>
    public static IReadOnlyList<long> Reached(IRecordStore s, ProjectMember m)
    {
        if (m.TerritoryId is not { } id || s.Get<TemporaryTerritory>(id) is not { IsActive: true } territory) return Array.Empty<long>();
        return s.Find<TemporaryCompanyTerritory>(Query.Where("TerritoryId", territory.Id).And("IsActive", true)).Select(l => l.CompanyId).Distinct().ToList();
    }
}

/// <summary>
/// MyCRM's projects (its Projects screen and ProjectDetails_PopUp), read and written in MyCRM's tables: a name, a status,
/// an end date, companies and members. Managers, Admins, Sales Admins and MD create projects; the owner (Project Manager)
/// or a Sales Admin changes, closes and deletes one; only the owner adds and removes members. Members who do not own a
/// company of the project get a temporary territory with it while the project is open (<see cref="ProjectTerritories"/>);
/// closing or deleting the project, removing a member or a company, or the daily task "Projects that have ended" takes it
/// away. Nothing is deleted in MyCRM.
/// </summary>
public sealed class Projects(IRecordStore store, IRecordStore own, Access access, People people, Territories territories, ProjectTerritories temporary, Func<DateTime> clock)
{
    public const int PageSize = 50;

    public sealed class ProjectInput
    {
        public string? Name { get; set; }
        public string? Status { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public string? Remarks { get; set; }
        /// <summary>A new project's companies.</summary>
        public List<long>? Companies { get; set; }
    }

    public sealed class CompaniesInput
    {
        public List<long>? Companies { get; set; }
    }

    public sealed class MemberInput
    {
        public string? Login { get; set; }
        public string? Role { get; set; }
    }

    public sealed record Row(Project Project, IReadOnlyList<string> Companies, int Members, string Caretaker);

    public sealed record CompanyRow(long Id, string Name, string Brn, string Salesperson, string Territory, bool Active);

    public sealed record MemberRow(ProjectMember Member, string Name, IReadOnlyList<string> Reached);

    public sealed record HistoryLine(DateTime At, string What, string By);

    /// <summary>MyCRM's Create Project button: Manager, Admin, Sales Admin and MD (and MyCRMRecords' administrators).</summary>
    public static bool MayCreate(Actor actor) => actor.IsAdmin || CrmRoles.Is(actor.CrmRole, CrmRoles.Manager, CrmRoles.Admin, CrmRoles.SalesAdmin, CrmRoles.Md);

    /// <summary>MyCRM's Save, Close and Delete Project: the owner, or a Sales Admin.</summary>
    public static bool MayChange(Actor actor, Project p) => actor.IsSalesAdmin || actor.IsLogin(p.Owner);

    /// <summary>MyCRM's Add as Member: only the owner (and MyCRMRecords' administrators).</summary>
    public static bool MayChangeMembers(Actor actor, Project p) => actor.IsAdmin || actor.IsLogin(p.Owner);

    private static DateTime? Day(string? text) =>
        DateTime.TryParseExact((text ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string Plural(int n, string one, string many) => n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many);

    // ---------------------------------------------------------------- reading

    /// <summary>The project's active companies' Ids.</summary>
    public IReadOnlyList<long> CompanyIds(IRecordStore s, long projectId) =>
        s.Find<ProjectCompany>(Query.Where("ProjectId", projectId).And("IsActive", true)).Select(c => c.CompanyId).Distinct().ToList();

    private IReadOnlyList<ProjectMember> ActiveMembers(IRecordStore s, long projectId) =>
        s.Find<ProjectMember>(Query.Where("ProjectId", projectId).And("IsActive", true)).OrderBy(m => m.Id).ToList();

    /// <summary>
    /// Whether the person sees the project: MyCRM lists the projects with a member in the person's list (themselves and,
    /// by their role, their team); its owner and Sales Admins see it too.
    /// </summary>
    public bool MaySee(Actor actor, Project p)
    {
        if (actor.IsSalesAdmin || actor.IsLogin(p.Owner)) return true;
        var list = access.UserList(actor.Login, actor.CrmRole);
        return ActiveMembers(store, p.Id).Any(m => list.Contains(m.User));
    }

    public Project? Get(long id) => store.Get<Project>(id) is { IsActive: true } p ? p : null;

    private Dictionary<long, string> CompanyNames(IEnumerable<long> ids)
    {
        var names = new Dictionary<long, string>();
        foreach (var chunk in ids.Distinct().Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => (object)x.v))
            foreach (var c in store.Find<Company>(Query.All().In("Id", chunk))) names[c.Id] = c.Name.Trim();
        return names;
    }

    /// <summary>
    /// MyCRM's Projects list: the active projects with an active member in the person's list (or that they own; every
    /// project for Sales Admins), open, closed or all, searched by name, remarks or company, newest first.
    /// </summary>
    public (int Total, IReadOnlyList<Row> Rows) List(Actor actor, string? show, string? q, int skip)
    {
        List<Project> projects;
        if (actor.IsSalesAdmin) projects = store.Find<Project>(Query.Where("IsActive", true));
        else
        {
            var list = access.UserList(actor.Login, actor.CrmRole);
            var ids = store.Find<ProjectMember>(Query.Where("IsActive", true).In("User", list.Cast<object>())).Select(m => (object)m.ProjectId).Distinct().ToList();
            projects = ids.Count == 0 ? new List<Project>() : store.Find<Project>(Query.Where("IsActive", true).In("Id", ids));
            projects.AddRange(store.Find<Project>(Query.Where("IsActive", true).And("Owner", actor.Login)).Where(p => projects.All(x => x.Id != p.Id)));
        }
        projects = (show ?? "open") switch
        {
            "closed" => projects.Where(p => p.IsClosed).ToList(),
            "all" => projects,
            _ => projects.Where(p => !p.IsClosed).ToList(),
        };
        if (projects.Count == 0) return (0, Array.Empty<Row>());
        var pids = projects.Select(p => (object)p.Id).ToList();
        var links = store.Find<ProjectCompany>(Query.Where("IsActive", true).In("ProjectId", pids));
        var names = CompanyNames(links.Select(l => l.CompanyId));
        var members = store.Find<ProjectMember>(Query.Where("IsActive", true).In("ProjectId", pids));
        var text = (q ?? "").Trim();
        var rows = projects.Select(p =>
            {
                var companies = links.Where(l => l.ProjectId == p.Id).OrderBy(l => l.Id).Select(l => names.TryGetValue(l.CompanyId, out var n) ? n : "#" + l.CompanyId.ToString(CultureInfo.InvariantCulture))
                    .Distinct().ToList();
                var own = members.Where(m => m.ProjectId == p.Id).ToList();
                var lead = own.FirstOrDefault(m => m.Role is ProjectRoles.SalespersonCaretaker or ProjectRoles.SalespersonAccountOwner)
                           ?? own.FirstOrDefault(m => m.Role != ProjectRoles.ProjectManager);
                var caretaker = lead is null ? "" : people.NameOf(lead.User) + " (" + ProjectRoles.List.Label(lead.Role) + ")";
                return new Row(p, companies, own.Select(m => m.User).Distinct(StringComparer.OrdinalIgnoreCase).Count(), caretaker);
            })
            .Where(r => text.Length == 0 || r.Project.ProjectName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                || r.Project.Remarks.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                || r.Companies.Any(c => c.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0))
            .OrderByDescending(r => r.Project.Id).ToList();
        return (rows.Count, rows.Skip(Math.Max(0, skip)).Take(PageSize).ToList());
    }

    /// <summary>The project's active companies, by name, with their salesperson and territory.</summary>
    public IReadOnlyList<CompanyRow> Companies(long projectId)
    {
        var ids = CompanyIds(store, projectId);
        if (ids.Count == 0) return Array.Empty<CompanyRow>();
        var companies = new List<Company>();
        foreach (var chunk in ids.Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => (object)x.v)) companies.AddRange(store.Find<Company>(Query.All().In("Id", chunk)));
        var owners = territories.Owners(ids);
        return companies.Select(c => owners.TryGetValue(c.Id, out var o)
                ? new CompanyRow(c.Id, c.Name.Trim(), c.Brn, people.NameOf(o.Owner), Territories.Title(o.Territory), c.IsActive)
                : new CompanyRow(c.Id, c.Name.Trim(), c.Brn, "", "", c.IsActive))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The project's active members, the Project Manager first, with the companies their temporary territory gives them.</summary>
    public IReadOnlyList<MemberRow> Members(long projectId)
    {
        var names = CompanyNames(CompanyIds(store, projectId));
        return ActiveMembers(store, projectId)
            .Select(m => new MemberRow(m, people.NameOf(m.User), ProjectTerritories.Reached(store, m).Select(c => names.TryGetValue(c, out var n) ? n : "").Where(n => n.Length > 0).ToList()))
            .OrderBy(m => m.Member.Role == ProjectRoles.ProjectManager ? 0 : 1).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The active activities linked to the project, newest first.</summary>
    public IReadOnlyList<Activity> Activities(long projectId)
    {
        var ids = store.Find<ProjectLink>(Query.Where("ProjectId", projectId).And("IsActive", true)).Select(l => (object)l.CompanyActivityId!).Where(i => i is not null).Distinct().ToList();
        if (ids.Count == 0) return Array.Empty<Activity>();
        return store.Find<Activity>(Query.Where("IsActive", true).In("Id", ids)).OrderByDescending(a => a.VisitDate).ThenByDescending(a => a.Id).ToList();
    }

    /// <summary>The projects that include the company (active ones), newest first, with their members.</summary>
    public IReadOnlyList<(Project Project, IReadOnlyList<ProjectMember> Members)> ForCompany(long companyId)
    {
        var ids = store.Find<ProjectCompany>(Query.Where("CompanyId", companyId).And("IsActive", true)).Select(l => (object)l.ProjectId).Distinct().ToList();
        if (ids.Count == 0) return Array.Empty<(Project, IReadOnlyList<ProjectMember>)>();
        var projects = store.Find<Project>(Query.Where("IsActive", true).In("Id", ids));
        var members = store.Find<ProjectMember>(Query.Where("IsActive", true).In("ProjectId", projects.Select(p => (object)p.Id)));
        return projects.OrderBy(p => p.IsClosed).ThenByDescending(p => p.Id)
            .Select(p => (p, (IReadOnlyList<ProjectMember>)members.Where(m => m.ProjectId == p.Id).OrderBy(m => m.Role == ProjectRoles.ProjectManager ? 0 : 1).ThenBy(m => m.Id).ToList()))
            .ToList();
    }

    /// <summary>
    /// The company's caretakers (MyCRM's companyOwnerCaretake): the active members with the role Salesperson (Caretaker)
    /// or Salesperson (Account Owner) of its active projects that are not closed, each once per project, skipping anyone
    /// whose name is the owner's.
    /// </summary>
    public IReadOnlyList<(Project Project, ProjectMember Member)> Caretakers(long companyId, string owner)
    {
        var ownerName = people.NameOf(owner);
        return ForCompany(companyId).Where(x => !x.Project.IsClosed)
            .SelectMany(x => x.Members.Where(m => m.Role is ProjectRoles.SalespersonCaretaker or ProjectRoles.SalespersonAccountOwner).Select(m => (x.Project, m)))
            .Where(x => ownerName.Length == 0 || !Strings.SameText(people.NameOf(x.m.User), ownerName))
            .GroupBy(x => (x.Project.Id, x.m.User.ToUpperInvariant())).Select(g => g.First())
            .ToList();
    }

    /// <summary>The open projects of a company an activity can be linked to (MyCRM's activity form).</summary>
    public IReadOnlyList<Project> OpenForCompany(long companyId) =>
        ForCompany(companyId).Select(x => x.Project).Where(p => !p.IsClosed).ToList();

    /// <summary>Active companies whose name, BRN or website has the text (MyCRM's "Type the Company, BRN or Website"), by name, at most 50.</summary>
    public IReadOnlyList<CompanyRow> FindCompanies(string? q)
    {
        var text = (q ?? "").Trim();
        if (text.Length < 2) return Array.Empty<CompanyRow>();
        var found = store.Find<Company>(Query.Where("IsActive", true).Search(text, "Name", "Brn", "Website")).OrderBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).Take(50).ToList();
        var owners = territories.Owners(found.Select(c => c.Id));
        return found.Select(c => owners.TryGetValue(c.Id, out var o)
            ? new CompanyRow(c.Id, c.Name.Trim(), c.Brn, people.NameOf(o.Owner), Territories.Title(o.Territory), true)
            : new CompanyRow(c.Id, c.Name.Trim(), c.Brn, "", "", true)).ToList();
    }

    /// <summary>The people who can be made members: everyone with an active CRM user record, by name.</summary>
    public IReadOnlyList<(string Login, string Name)> Candidates() =>
        store.Find<CrmUser>(Query.Where("IsActive", true)).Select(u => u.Login).Where(l => l.Trim().Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(l => (l, people.NameOf(l))).OrderBy(p => p.Item2, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>How many of the project's companies the person would reach only through the project (a temporary territory), and how many there are.</summary>
    public (int Temporary, int Companies) WouldReach(long projectId, string login)
    {
        var companies = CompanyIds(store, projectId);
        return (companies.Count(c => !ProjectTerritories.OwnsPermanently(store, login, c)), companies.Count);
    }

    /// <summary>
    /// What was changed in the project through MyCRMRecords (dbo.CrChange: the project, its companies and its members),
    /// newest first. MyCRM's own changes are not recorded there.
    /// </summary>
    public IReadOnlyList<HistoryLine> History(long projectId)
    {
        var lines = new List<HistoryLine>();
        var companyRows = store.Find<ProjectCompany>(Query.Where("ProjectId", projectId));
        var memberRows = store.Find<ProjectMember>(Query.Where("ProjectId", projectId));
        var names = CompanyNames(companyRows.Select(c => c.CompanyId));
        var changes = own.Find<MyCrmChange>(Query.Where("TableName", MyCrmMap.Projects).And("RowId", projectId)).ToList();
        if (companyRows.Count > 0)
            changes.AddRange(own.Find<MyCrmChange>(Query.Where("TableName", MyCrmMap.ProjectCompanies).In("RowId", companyRows.Select(c => (object)c.Id))));
        if (memberRows.Count > 0)
            changes.AddRange(own.Find<MyCrmChange>(Query.Where("TableName", MyCrmMap.ProjectMembers).In("RowId", memberRows.Select(m => (object)m.Id))));
        // One change is the values written to one row at one time. Two changes made within the same instant (closing,
        // then deleting) share ChangedOn: a column met again starts the next change.
        var groups = new List<(string TableName, long RowId, List<MyCrmChange> Rows)>();
        foreach (var same in changes.GroupBy(c => (c.ChangedOn, c.ChangedBy, c.TableName, c.RowId)))
        {
            var current = new List<MyCrmChange>();
            foreach (var c in same.OrderBy(c => c.Id))
            {
                if (current.Any(x => string.Equals(x.Column, c.Column, StringComparison.OrdinalIgnoreCase)))
                {
                    groups.Add((same.Key.TableName, same.Key.RowId, current));
                    current = new List<MyCrmChange>();
                }
                current.Add(c);
            }
            groups.Add((same.Key.TableName, same.Key.RowId, current));
        }
        foreach (var g in groups.Select(x => x.Rows))
        {
            var key = (TableName: g[0].TableName, RowId: g[0].RowId, ChangedOn: g[0].ChangedOn, ChangedBy: g[0].ChangedBy);
            var cols = g.ToDictionary(c => c.Column, StringComparer.OrdinalIgnoreCase);
            bool Has(string column, string? after = null) => cols.TryGetValue(column, out var c) && (after is null || c.After == after);
            var created = g.All(c => c.Before.Length == 0) && g.Any(c => c.After.Length > 0) && (Has("PROJECTID") || Has("PROJECTNAME"));
            string what;
            switch (key.TableName)
            {
                case MyCrmMap.Projects:
                    if (created) what = "Created the project";
                    else if (Has("ISACTIVE", "0")) what = "Deleted the project";
                    else if (Has("ISCLOSED", "1")) what = "Closed the project";
                    else
                    {
                        var parts = new List<string>();
                        if (Has("PROJECTNAME")) parts.Add("name → " + cols["PROJECTNAME"].After);
                        if (Has("PROJECTSTATUSID")) parts.Add("status");
                        if (Has("STARTDATE")) parts.Add("start date → " + cols["STARTDATE"].After);
                        if (Has("ENDDATE")) parts.Add("end date → " + cols["ENDDATE"].After);
                        if (Has("REMARKS")) parts.Add("remarks");
                        if (parts.Count == 0) continue;
                        what = "Changed " + string.Join("; ", parts);
                    }
                    break;
                case MyCrmMap.ProjectCompanies:
                    var link = companyRows.FirstOrDefault(c => c.Id == key.RowId);
                    var company = link is not null && names.TryGetValue(link.CompanyId, out var n) ? n : "a company";
                    if (created) what = "Added company " + company;
                    else if (Has("ISACTIVE", "0")) what = "Removed company " + company;
                    else if (Has("ISACTIVE", "1")) what = "Added company " + company + " again";
                    else continue;
                    break;
                default:
                    var member = memberRows.FirstOrDefault(m => m.Id == key.RowId);
                    var who = member is null ? "a member" : people.NameOf(member.User);
                    if (created) what = "Added member " + who + (member is null ? "" : " as " + ProjectRoles.List.Label(member.Role));
                    else if (Has("ISACTIVE", "0")) what = "Removed member " + who;
                    else if (Has("TERRITORYID")) what = "Temporary territory given to " + who;
                    else continue;
                    break;
            }
            lines.Add(new HistoryLine(key.ChangedOn, what, key.ChangedBy));
        }
        return lines.OrderByDescending(l => l.At).ToList();
    }

    // ---------------------------------------------------------------- changing

    private static OperationResult NotFound() => OperationResult.Failure(ResultCodes.NotFound, "That project is not there.");

    private List<FieldError> Validate(ProjectInput input, Project? existing, out DateTime? start, out DateTime? end)
    {
        var errors = new List<FieldError>();
        var today = clock().Date;
        var name = (input.Name ?? "").Trim();
        if (name.Length == 0) errors.Add(new FieldError("name", "Enter the project's name."));
        else if (name.Length > 250) errors.Add(new FieldError("name", "At most 250 characters."));
        var status = (input.Status ?? "").Trim();
        if (status.Length > 0 && !ProjectStatuses.List.IsKnown(status)) errors.Add(new FieldError("status", "Choose a status."));
        start = Day(input.StartDate) ?? existing?.StartDate ?? today;
        end = Day(input.EndDate);
        if (end is null) errors.Add(new FieldError("endDate", "Enter the end date."));
        // MyCRM's Save: "Project End Date must not be less than the current date." (an end date of today is refused too).
        else if (end.Value <= today) errors.Add(new FieldError("endDate", "Project End Date must not be less than the current date: choose a day after today."));
        else if (start is { } s && s > end) errors.Add(new FieldError("startDate", "The start date cannot be after the end date."));
        if ((input.Remarks ?? "").Trim().Length > 2000) errors.Add(new FieldError("remarks", "At most 2000 characters."));
        return errors;
    }

    /// <summary>
    /// MyCRM's Save of a new project: its details and companies (at least one), with its creator as owner and Project
    /// Manager. Members are added on the project's page.
    /// </summary>
    public OperationResult Create(Actor actor, ProjectInput input)
    {
        if (!MayCreate(actor)) return OperationResult.Failure(ResultCodes.Forbidden, "Projects are created by Managers, Admins, Sales Admins and MD.");
        var errors = Validate(input, null, out var start, out var end);
        var ids = (input.Companies ?? new List<long>()).Where(i => i > 0).Distinct().ToList();
        var companies = ids.Count == 0 ? new List<Company>() : store.Find<Company>(Query.Where("IsActive", true).In("Id", ids.Cast<object>()));
        if (companies.Count == 0) errors.Add(new FieldError("companies", "You must have a Company Associated with this Project."));
        if (errors.Count > 0) return OperationResult.Invalid(errors);
        var today = clock().Date;
        var project = new Project
        {
            ProjectName = Strings.Shorten((input.Name ?? "").Trim(), 250), Status = (input.Status ?? "").Trim().Length > 0 ? input.Status!.Trim() : ProjectStatuses.ConceptionAndInitiation,
            CompanyId = companies[0].Id, Remarks = (input.Remarks ?? "").Trim(), IsActive = true, StartDate = start, EndDate = end, Owner = actor.Login,
            DateCreated = today, UpdatedDate = today, UpdatedBy = actor.Login,
        };
        store.Batch(s =>
        {
            s.Insert(project);
            foreach (var c in companies.OrderBy(c => ids.IndexOf(c.Id))) s.Insert(new ProjectCompany { ProjectId = project.Id, CompanyId = c.Id, IsActive = true });
            s.Insert(new ProjectMember { ProjectId = project.Id, User = actor.Login, Role = ProjectRoles.ProjectManager, IsActive = true });
        });
        return OperationResult.Success($"Successfully created Project {project.ProjectName} with {Plural(companies.Count, "company", "companies")}.", project.Id);
    }

    /// <summary>Gives every active member the temporary territory the project gives them now; returns how many members have one.</summary>
    private int ApplyAll(IRecordStore s, Project p, Actor actor)
    {
        var companies = CompanyIds(s, p.Id);
        var today = clock().Date;
        return ActiveMembers(s, p.Id).Count(m => temporary.Apply(s, p, m, companies, today, actor.Login) > 0);
    }

    /// <summary>MyCRM's Save of a project's details (owner or Sales Admin, while it is not closed); access follows a new end date.</summary>
    public OperationResult Save(Actor actor, long id, ProjectInput input)
    {
        if (Get(id) is not { } p) return NotFound();
        if (!MayChange(actor, p)) return OperationResult.Failure(ResultCodes.Forbidden, "Only the project's owner or a Sales Admin changes it.");
        if (p.IsClosed) return OperationResult.Failure(ResultCodes.Conflict, "This project is closed: it cannot be changed.");
        var errors = Validate(input, p, out var start, out var end);
        if (errors.Count > 0) return OperationResult.Invalid(errors);
        p.ProjectName = Strings.Shorten((input.Name ?? "").Trim(), 250);
        if ((input.Status ?? "").Trim().Length > 0) p.Status = input.Status!.Trim();
        p.StartDate = start;
        p.EndDate = end;
        p.Remarks = (input.Remarks ?? "").Trim();
        p.UpdatedDate = clock().Date;
        p.UpdatedBy = actor.Login;
        store.Batch(s =>
        {
            s.Update(p);
            ApplyAll(s, p, actor);
        });
        return OperationResult.Success($"Saved {p.ProjectName}.", p.Id);
    }

    /// <summary>Adds companies to an open project; members who do not own them get them through their temporary territory.</summary>
    public OperationResult AddCompanies(Actor actor, long id, IReadOnlyList<long> ids)
    {
        if (Get(id) is not { } p) return NotFound();
        if (!MayChange(actor, p)) return OperationResult.Failure(ResultCodes.Forbidden, "Only the project's owner or a Sales Admin changes it.");
        if (p.IsClosed) return OperationResult.Failure(ResultCodes.Conflict, "This project is closed: it cannot be changed.");
        var wanted = ids.Where(i => i > 0).Distinct().ToList();
        var companies = wanted.Count == 0 ? new List<Company>() : store.Find<Company>(Query.Where("IsActive", true).In("Id", wanted.Cast<object>()));
        if (companies.Count == 0) return OperationResult.Failure(ResultCodes.Validation, "Tick the companies to add.");
        var added = 0;
        var given = 0;
        store.Batch(s =>
        {
            var links = s.Find<ProjectCompany>(Query.Where("ProjectId", p.Id));
            foreach (var c in companies)
            {
                if (links.Any(l => l.CompanyId == c.Id && l.IsActive)) continue;
                if (links.FirstOrDefault(l => l.CompanyId == c.Id) is { } again)
                {
                    again.IsActive = true;
                    s.Update(again);
                }
                else s.Insert(new ProjectCompany { ProjectId = p.Id, CompanyId = c.Id, IsActive = true });
                added++;
            }
            given = ApplyAll(s, p, actor);
        });
        return OperationResult.Success(added == 0 ? "Those companies are in the project already."
            : $"Added {Plural(added, "company", "companies")}. {Plural(given, "member has", "members have")} temporary access to the project's companies they do not own.", p.Id);
    }

    /// <summary>MyCRM's DisableMembersWithCompDel: the company leaves the project and the members' temporary access to it goes.</summary>
    public OperationResult RemoveCompany(Actor actor, long id, long companyId)
    {
        if (Get(id) is not { } p) return NotFound();
        if (!MayChange(actor, p)) return OperationResult.Failure(ResultCodes.Forbidden, "Only the project's owner or a Sales Admin changes it.");
        if (p.IsClosed) return OperationResult.Failure(ResultCodes.Conflict, "This project is closed: it cannot be changed.");
        var links = store.Find<ProjectCompany>(Query.Where("ProjectId", p.Id).And("IsActive", true));
        var link = links.Where(l => l.CompanyId == companyId).ToList();
        if (link.Count == 0) return OperationResult.Failure(ResultCodes.NotFound, "That company is not in the project.");
        if (links.Select(l => l.CompanyId).Distinct().Count() == 1)
            return OperationResult.Failure(ResultCodes.Conflict, "You must have a Company Associated with this Project: add another company before removing this one, or close the project.");
        store.Batch(s =>
        {
            foreach (var l in link)
            {
                l.IsActive = false;
                s.Update(l);
            }
            ApplyAll(s, p, actor);
        });
        var name = store.Get<Company>(companyId)?.Name.Trim() ?? "The company";
        return OperationResult.Success($"Removed {name} from the project.", p.Id);
    }

    /// <summary>
    /// MyCRM's Add as Member (the owner only): the person joins with a role and, while the project is open, gets a
    /// temporary territory with the project's companies they do not own.
    /// </summary>
    public OperationResult AddMember(Actor actor, long id, MemberInput input)
    {
        if (Get(id) is not { } p) return NotFound();
        if (!MayChangeMembers(actor, p)) return OperationResult.Failure(ResultCodes.Forbidden, "You don't have permission to add members: only the project's owner adds them.");
        if (p.IsClosed) return OperationResult.Failure(ResultCodes.Conflict, "This project is closed: it cannot be changed.");
        var login = (input.Login ?? "").Trim();
        var role = (input.Role ?? "").Trim();
        var errors = new List<FieldError>();
        var candidate = Candidates().FirstOrDefault(c => Strings.SameText(c.Login, login));
        if (candidate.Login is null) errors.Add(new FieldError("login", "Choose a person with a CRM user record."));
        if (!ProjectRoles.List.IsKnown(role)) errors.Add(new FieldError("role", "Choose the person's role in this project."));
        else if (role == ProjectRoles.ProjectManager) errors.Add(new FieldError("role", "Project Manager is by default the Owner of this Project."));
        if (candidate.Login is not null && ActiveMembers(store, p.Id).Any(m => Strings.SameText(m.User, candidate.Login)))
            errors.Add(new FieldError("login", candidate.Name + " is a member already."));
        if (errors.Count > 0) return OperationResult.Invalid(errors);
        var reached = 0;
        var member = new ProjectMember { ProjectId = p.Id, User = candidate.Login!, Role = role, IsActive = true };
        store.Batch(s =>
        {
            s.Insert(member);
            reached = temporary.Apply(s, p, member, CompanyIds(s, p.Id), clock().Date, actor.Login);
        });
        return OperationResult.Success($"Added {candidate.Name} as {ProjectRoles.List.Label(role)}."
            + (reached > 0 ? $" They can now see {Plural(reached, "company", "companies")} of the project until {p.EndDate!.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}." : ""), member.Id);
    }

    /// <summary>A member leaves the project (the owner only; never the Project Manager): their temporary territory goes.</summary>
    public OperationResult RemoveMember(Actor actor, long id, long memberId)
    {
        if (Get(id) is not { } p) return NotFound();
        if (!MayChangeMembers(actor, p)) return OperationResult.Failure(ResultCodes.Forbidden, "Only the project's owner removes members.");
        if (p.IsClosed) return OperationResult.Failure(ResultCodes.Conflict, "This project is closed: it cannot be changed.");
        if (store.Get<ProjectMember>(memberId) is not { IsActive: true } m || m.ProjectId != p.Id) return OperationResult.Failure(ResultCodes.NotFound, "That member is not in the project.");
        if (m.Role == ProjectRoles.ProjectManager && Strings.SameText(m.User, p.Owner))
            return OperationResult.Failure(ResultCodes.Conflict, "You cannot edit the owner of this Project.");
        store.Batch(s =>
        {
            m.IsActive = false;
            s.Update(m);
            ProjectTerritories.TakeAway(s, m);
        });
        return OperationResult.Success($"Removed {people.NameOf(m.User)} from the project.", p.Id);
    }

    /// <summary>Who would lose access if the project closed: the members with a temporary territory now, and the companies.</summary>
    public (int Members, int Companies) LosingAccess(long id)
    {
        var members = ActiveMembers(store, id).Count(m => ProjectTerritories.Reached(store, m).Count > 0);
        return (members, CompanyIds(store, id).Count);
    }

    /// <summary>MyCRM's DisableMembersWhenProjClose: every member's temporary territory goes (members, companies and activity links stay).</summary>
    private static int TakeAwayAll(IRecordStore s, long projectId)
    {
        var count = 0;
        foreach (var m in s.Find<ProjectMember>(Query.Where("ProjectId", projectId)))
            if (m.TerritoryId is not null)
            {
                if (ProjectTerritories.Reached(s, m).Count > 0) count++;
                ProjectTerritories.TakeAway(s, m);
            }
        return count;
    }

    /// <summary>MyCRM's Close Project: closed for good; the members lose their temporary access.</summary>
    public OperationResult Close(Actor actor, long id)
    {
        if (Get(id) is not { } p) return NotFound();
        if (!MayChange(actor, p)) return OperationResult.Failure(ResultCodes.Forbidden, "Only the project's owner or a Sales Admin closes it.");
        if (p.IsClosed) return OperationResult.Failure(ResultCodes.Conflict, "This project is closed already.");
        var count = 0;
        store.Batch(s =>
        {
            p.IsClosed = true;
            p.UpdatedDate = clock().Date;
            p.UpdatedBy = actor.Login;
            s.Update(p);
            count = TakeAwayAll(s, p.Id);
        });
        return OperationResult.Success($"Project closed. {Plural(count, "temporary territory was", "temporary territories were")} taken away.", p.Id);
    }

    /// <summary>
    /// MyCRM's Delete Project (DisableMembersWhenProjDeleted): the project, its companies, members and activity links become
    /// inactive and the members lose their temporary access. Activities stay on each company.
    /// </summary>
    public OperationResult Delete(Actor actor, long id)
    {
        if (Get(id) is not { } p) return NotFound();
        if (!MayChange(actor, p)) return OperationResult.Failure(ResultCodes.Forbidden, "Only the project's owner or a Sales Admin deletes it.");
        store.Batch(s =>
        {
            p.IsActive = false;
            p.UpdatedDate = clock().Date;
            p.UpdatedBy = actor.Login;
            s.Update(p);
            TakeAwayAll(s, p.Id);
            foreach (var c in s.Find<ProjectCompany>(Query.Where("ProjectId", p.Id).And("IsActive", true)))
            {
                c.IsActive = false;
                s.Update(c);
            }
            foreach (var m in s.Find<ProjectMember>(Query.Where("ProjectId", p.Id).And("IsActive", true)))
            {
                m.IsActive = false;
                s.Update(m);
            }
            foreach (var l in s.Find<ProjectLink>(Query.Where("ProjectId", p.Id).And("IsActive", true)))
            {
                l.IsActive = false;
                s.Update(l);
            }
        });
        return OperationResult.Success($"Project {p.ProjectName} was deleted. Its members no longer have temporary access to its companies.", p.Id);
    }

    // ---------------------------------------------------------------- the daily task

    /// <summary>
    /// The scheduled task "Projects that have ended" (MyCRM's timer SetInactiveAllProjectThatEnds, daily at 05:30): every
    /// open project whose end date has come is closed and its members lose their temporary access; then, as MyCRM's
    /// FixTerritory, every active temporary territory whose member is no longer active, whose project is not open (inactive,
    /// closed or past its end date) or that no member has is made inactive. Every change is recorded (dbo.CrChange).
    /// </summary>
    public JobOutcome RunDaily(string by)
    {
        var today = clock().Date;
        var closed = 0;
        var fixedTerritories = 0;
        using (MyCrmStore.As(by == "Scheduled" ? "Scheduled task: Projects that have ended" : by))
        {
            var due = store.Find<Project>(Query.Where("IsActive", true).And("IsClosed", false).AtLeast("EndDate", new DateTime(1900, 1, 2)).Before("EndDate", today.AddDays(1)));
            foreach (var p in due)
                store.Batch(s =>
                {
                    p.IsClosed = true;
                    s.Update(p);
                    TakeAwayAll(s, p.Id);
                    closed++;
                });
            var active = store.Find<TemporaryTerritory>(Query.Where("IsActive", true));
            if (active.Count > 0)
            {
                var members = new List<ProjectMember>();
                foreach (var chunk in active.Select((t, i) => (t, i)).GroupBy(x => x.i / 1000, x => (object)(long?)x.t.Id))
                    members.AddRange(store.Find<ProjectMember>(Query.All().In("TerritoryId", chunk)));
                var projects = new Dictionary<long, Project>();
                foreach (var chunk in members.Select(m => m.ProjectId).Distinct().Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => (object)x.v))
                    foreach (var p in store.Find<Project>(Query.All().In("Id", chunk))) projects[p.Id] = p;
                foreach (var t in active)
                {
                    var of = members.Where(m => m.TerritoryId == t.Id).ToList();
                    var keep = of.Any(m => m.IsActive && projects.TryGetValue(m.ProjectId, out var p) && ProjectTerritories.IsOpen(p, today));
                    if (keep) continue;
                    store.Batch(s =>
                    {
                        t.IsActive = false;
                        s.Update(t);
                    });
                    fixedTerritories++;
                }
            }
        }
        return new JobOutcome($"{Plural(closed, "project", "projects")} closed; {Plural(fixedTerritories, "temporary territory", "temporary territories")} made inactive.",
            closed + fixedTerritories);
    }
}
