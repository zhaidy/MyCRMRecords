using System.Globalization;
using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>
/// Two of MyCRM's reports as downloads of the pages' lists: the Company summary (MyCRM's report 09: every company of the
/// Companies list with its salesperson, territory, customer type, addresses, caretake and caretakers) and the projects
/// with their companies' caretakers (reports 17 "Projects" and 08 "Caretake Account"). Everything is read in bulk (a
/// query per thousand companies, never one per company) and nothing is written.
/// </summary>
public sealed class Reports(IRecordStore store, Companies companies, Projects projects, Territories territories, People people, Func<DateTime> clock)
{
    /// <summary>At most this many companies in a Company summary (MyCRM has about 49,000 active ones).</summary>
    public const int MaxCompanies = 100000;
    /// <summary>At most this many projects, and this many project companies, in a projects download.</summary>
    public const int MaxProjects = 10000;
    public const int MaxProjectCompanies = 100000;

    private const int Chunk = 1000;

    /// <summary>A line of the Company summary.</summary>
    public sealed record CompanyLine(
        Company Company, string CustomerType, string GroupName, string AccountType, string Industry, string Salesperson, string Team, string Manager,
        string Territory, string PostalCode, string BillingAddress, string CaretakeRemarks, string Caretakers, string CreatedBy, string UpdatedBy);

    /// <summary>A project of the projects download: its state (Open, Ended or Closed), companies counted and members with their roles.</summary>
    public sealed record ProjectLine(Project Project, string Status, string State, string Owner, int Companies, string Members, string UpdatedBy);

    /// <summary>A company of a project: its account owner (salesperson and territory) and the project's caretakers, with whether each reaches it through the project.</summary>
    public sealed record CaretakeLine(
        Project Project, string ProjectStatus, long CompanyId, string Company, string Brn, string AccountOwner, string Territory, string Caretakers,
        string TemporaryAccess, string CaretakeRemarks);

    private static IEnumerable<List<object>> Chunks(IEnumerable<long> ids) =>
        ids.Distinct().Select((v, i) => (v, i)).GroupBy(x => x.i / Chunk, x => (object)x.v).Select(g => g.ToList());

    /// <summary>"Open" while active, not closed and not past its end date; "Ended" past it (not closed yet); "Closed".</summary>
    public static string StateOf(Project p, DateTime today) =>
        p.IsClosed ? "Closed" : p.EndDate is { } end && end.Date < today.Date ? "Ended" : "Open";

    // ---------------------------------------------------------------- Company summary

    /// <summary>The Company summary of the Companies list's search, filters and Show, as the person may open them (at most <see cref="MaxCompanies"/>).</summary>
    public (int Total, IReadOnlyList<CompanyLine> Lines) CompanySummary(Actor actor, Companies.ListFilter filter, int max = MaxCompanies)
    {
        var (total, found) = companies.ForDownload(actor, filter with { Skip = 0 }, max);
        if (found.Count == 0) return (total, Array.Empty<CompanyLine>());
        var ids = found.Select(c => c.Id).ToList();
        var owners = territories.Owners(ids);
        var name = people.NamesOf(Array.Empty<string>());
        var groupNames = store.Find<CompanyGroupName>().GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First().GroupName.Trim());

        // The salesperson's team(s) and their managers: MyCRM's CRM user records (a few hundred), read once.
        var crmUsers = store.Find<CrmUser>(Query.Where("IsActive", true));
        var teams = store.Find<CrmTeam>().GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First().Name.Trim());
        var teamsOf = crmUsers.Where(u => u.TeamId is not null && u.Login.Trim().Length > 0).GroupBy(u => u.Login.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(u => u.TeamId!.Value).Distinct().ToList(), StringComparer.OrdinalIgnoreCase);
        var managersOf = crmUsers.Where(u => u.TeamId is not null && CrmRoles.Is(u.Role, CrmRoles.Managers)).GroupBy(u => u.TeamId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(u => u.Login).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        var teamText = new Dictionary<string, (string Team, string Manager)>(StringComparer.OrdinalIgnoreCase);
        (string Team, string Manager) TeamOf(string login)
        {
            if (login.Trim().Length == 0) return ("", "");
            if (teamText.TryGetValue(login.Trim(), out var known)) return known;
            var ofPerson = teamsOf.TryGetValue(login.Trim(), out var t) ? t : new List<long>();
            var team = string.Join(", ", ofPerson.Select(id => teams.TryGetValue(id, out var n) ? n : "").Where(n => n.Length > 0).Distinct());
            var managers = string.Join(", ", ofPerson.SelectMany(id => managersOf.TryGetValue(id, out var m) ? m : new List<string>())
                .Where(m => !Strings.SameText(m, login)).Select(name).Distinct(StringComparer.OrdinalIgnoreCase));
            return teamText[login.Trim()] = (team, managers);
        }

        // Addresses: the first one's postal code (as the list shows it) and the billing address (else the default, else the first active one).
        var postal = new Dictionary<long, string>();
        var billing = new Dictionary<long, string>();
        var remarks = new Dictionary<long, List<string>>();
        foreach (var chunk in Chunks(ids))
        {
            var links = store.Find<CompanyAddressLink>(Query.All().In("CompanyId", chunk)).OrderBy(l => l.Id).ToList();
            var addresses = new Dictionary<long, Address>();
            foreach (var part in Chunks(links.Select(l => l.AddressId)))
                foreach (var a in store.Find<Address>(Query.All().In("Id", part))) addresses[a.Id] = a;
            foreach (var group in links.GroupBy(l => l.CompanyId))
            {
                var all = group.Where(l => addresses.ContainsKey(l.AddressId)).Select(l => (Link: l, Address: addresses[l.AddressId])).ToList();
                if (all.Count > 0) postal[group.Key] = all[0].Address.PostalCode.Trim();
                var active = all.Where(x => x.Link.IsActive && x.Address.IsActive).Select(x => x.Address).ToList();
                var chosen = active.FirstOrDefault(a => a.AddressType == AddressTypes.Billing) ?? active.FirstOrDefault(a => a.IsDefault) ?? active.FirstOrDefault();
                if (chosen is not null) billing[group.Key] = Companies.AddressText(chosen);
            }
            foreach (var r in store.Find<CompanyCaretake>(Query.All().In("CompanyId", chunk)).OrderBy(r => r.Id))
                if (r.CaretakeAccountRemarks.Trim().Length > 0)
                {
                    if (!remarks.TryGetValue(r.CompanyId, out var list)) remarks[r.CompanyId] = list = new List<string>();
                    list.Add(r.CaretakeAccountRemarks.Trim());
                }
        }

        var caretakers = Caretakers(ids, owners);
        var lines = found.Select(c =>
        {
            var owned = owners.TryGetValue(c.Id, out var o);
            var owner = owned ? o.Owner : "";
            var (team, manager) = TeamOf(owner);
            return new CompanyLine(c, CompanyGroups.List.Label(c.CompanyGroup),
                c.CompanyGroupNameId is { } g && groupNames.TryGetValue(g, out var gn) ? gn : "",
                CompAccountTypes.List.Label(c.CompAccountType), CompanyIndustries.List.Label(c.CompanyIndustry),
                name(owner), team, manager, owned ? Territories.Title(o.Territory) : "",
                postal.TryGetValue(c.Id, out var p) ? p : "", billing.TryGetValue(c.Id, out var b) ? b : "",
                remarks.TryGetValue(c.Id, out var r) ? string.Join("; ", r.Take(10)) : "",
                caretakers.TryGetValue(c.Id, out var ct) ? ct : "", name(c.CreatedBy), name(c.UpdatedBy));
        }).ToList();
        return (total, lines);
    }

    /// <summary>
    /// MyCRM's companyOwnerCaretake for many companies at once: the active members with the role Salesperson (Caretaker)
    /// or Salesperson (Account Owner) of the company's active projects that are not closed, once per project, leaving out
    /// anyone whose name is the salesperson's; "Name (Project)" joined by "; ".
    /// </summary>
    private Dictionary<long, string> Caretakers(IReadOnlyList<long> companyIds, IReadOnlyDictionary<long, (string Owner, Territory Territory)> owners)
    {
        var result = new Dictionary<long, string>();
        var open = store.Find<Project>(Query.Where("IsActive", true).And("IsClosed", false)).ToDictionary(p => p.Id);
        if (open.Count == 0) return result;
        var links = new List<ProjectCompany>();
        foreach (var chunk in Chunks(companyIds))
            links.AddRange(store.Find<ProjectCompany>(Query.Where("IsActive", true).In("ProjectId", open.Keys.Cast<object>()).In("CompanyId", chunk)));
        if (links.Count == 0) return result;
        var members = new List<ProjectMember>();
        foreach (var chunk in Chunks(links.Select(l => l.ProjectId)))
            members.AddRange(store.Find<ProjectMember>(Query.Where("IsActive", true).In("ProjectId", chunk))
                .Where(m => m.Role is ProjectRoles.SalespersonCaretaker or ProjectRoles.SalespersonAccountOwner));
        var byProject = members.GroupBy(m => m.ProjectId).ToDictionary(g => g.Key, g => g.OrderBy(m => m.Id).ToList());
        var name = people.NamesOf(Array.Empty<string>());
        foreach (var group in links.GroupBy(l => l.CompanyId))
        {
            var ownerName = owners.TryGetValue(group.Key, out var o) ? name(o.Owner) : "";
            var named = group.Select(l => l.ProjectId).Distinct().Where(open.ContainsKey).OrderByDescending(id => id)
                .SelectMany(id => (byProject.TryGetValue(id, out var m) ? m : new List<ProjectMember>())
                    .GroupBy(x => x.User.Trim().ToUpperInvariant()).Select(x => x.First())
                    .Where(x => ownerName.Length == 0 || !Strings.SameText(name(x.User), ownerName))
                    .Select(x => name(x.User) + " (" + open[id].ProjectName.Trim() + ")"))
                .ToList();
            if (named.Count > 0) result[group.Key] = string.Join("; ", named);
        }
        return result;
    }

    // ---------------------------------------------------------------- Projects and caretake accounts

    /// <summary>
    /// The Projects list's projects (Open, Closed or All, and its search, as the person sees them; at most
    /// <see cref="MaxProjects"/>) with their members, and a line for each of their active companies (at most
    /// <paramref name="maxCompanies"/> in all) with the company's account owner and the project's caretakers.
    /// </summary>
    public (int Total, IReadOnlyList<ProjectLine> Projects, IReadOnlyList<CaretakeLine> Companies, int CompanyTotal) ProjectsReport(
        Actor actor, string? show, string? q, int max = MaxProjects, int maxCompanies = MaxProjectCompanies)
    {
        var (total, found) = projects.ForDownload(actor, string.IsNullOrWhiteSpace(show) ? "open" : show, q, max);
        if (found.Count == 0) return (total, Array.Empty<ProjectLine>(), Array.Empty<CaretakeLine>(), 0);
        var today = clock().Date;
        var name = people.NamesOf(Array.Empty<string>());
        var counts = new Dictionary<long, int>();
        foreach (var chunk in Chunks(found.Select(p => p.Id)))
            foreach (var pair in projects.CompanySummaries(chunk.Select(x => (long)x).ToList())) counts[pair.Key] = pair.Value.Count;
        var members = new List<ProjectMember>();
        foreach (var chunk in Chunks(found.Select(p => p.Id))) members.AddRange(store.Find<ProjectMember>(Query.Where("IsActive", true).In("ProjectId", chunk)));
        var membersOf = members.GroupBy(m => m.ProjectId).ToDictionary(g => g.Key,
            g => g.OrderBy(m => m.Role == ProjectRoles.ProjectManager ? 0 : 1).ThenBy(m => m.Id).ToList());
        static string Status(Project p) => ProjectStatuses.List.Label(p.Status);
        var projectLines = found.Select(p => new ProjectLine(p, Status(p), StateOf(p, today), name(p.Owner), counts.TryGetValue(p.Id, out var n) ? n : 0,
            string.Join("; ", (membersOf.TryGetValue(p.Id, out var m) ? m : new List<ProjectMember>()).Select(x => name(x.User) + " (" + ProjectRoles.List.Label(x.Role) + ")")),
            name(p.UpdatedBy))).ToList();

        // The companies, project by project (newest first), each project's first added first, up to the cap.
        var companyTotal = counts.Values.Sum();
        var links = new List<ProjectCompany>();
        foreach (var p in found)
        {
            var left = maxCompanies - links.Count;
            if (left <= 0) break;
            if (!counts.TryGetValue(p.Id, out var n) || n == 0) continue;
            links.AddRange(store.Find<ProjectCompany>(Query.Where("ProjectId", p.Id).And("IsActive", true).Sort("Id").Page(0, left)));
        }
        var byId = new Dictionary<long, Company>();
        var remarks = new Dictionary<long, List<string>>();
        foreach (var chunk in Chunks(links.Select(l => l.CompanyId)))
        {
            foreach (var c in store.Find<Company>(Query.All().In("Id", chunk))) byId[c.Id] = c;
            foreach (var r in store.Find<CompanyCaretake>(Query.All().In("CompanyId", chunk)).OrderBy(r => r.Id))
                if (r.CaretakeAccountRemarks.Trim().Length > 0)
                {
                    if (!remarks.TryGetValue(r.CompanyId, out var list)) remarks[r.CompanyId] = list = new List<string>();
                    list.Add(r.CaretakeAccountRemarks.Trim());
                }
        }
        var owners = territories.Owners(links.Select(l => l.CompanyId));

        // Whether each caretaker reaches the company through the project: an active link of their active temporary territory.
        var caretakersOf = membersOf.ToDictionary(x => x.Key, x => x.Value
            .Where(m => m.Role is ProjectRoles.SalespersonCaretaker or ProjectRoles.SalespersonAccountOwner)
            .GroupBy(m => m.User.Trim().ToUpperInvariant()).Select(g => g.First()).ToList());
        var territoryIds = caretakersOf.Values.SelectMany(v => v).Where(m => m.TerritoryId is not null).Select(m => m.TerritoryId!.Value).Distinct().ToList();
        var activeTerritories = new HashSet<long>();
        foreach (var chunk in Chunks(territoryIds))
            foreach (var t in store.Find<TemporaryTerritory>(Query.Where("IsActive", true).In("Id", chunk))) activeTerritories.Add(t.Id);
        var reached = new HashSet<(long Territory, long Company)>();
        if (activeTerritories.Count > 0)
            foreach (var chunk in Chunks(links.Select(l => l.CompanyId)))
                foreach (var t in store.Find<TemporaryCompanyTerritory>(Query.Where("IsActive", true).In("TerritoryId", activeTerritories.Cast<object>()).In("CompanyId", chunk)))
                    reached.Add((t.TerritoryId, t.CompanyId));

        var projectOf = found.ToDictionary(p => p.Id);
        var companyLines = new List<CaretakeLine>();
        foreach (var link in links)
        {
            var p = projectOf[link.ProjectId];
            var c = byId.TryGetValue(link.CompanyId, out var found1) ? found1 : null;
            var owned = owners.TryGetValue(link.CompanyId, out var o);
            var caretakers = caretakersOf.TryGetValue(p.Id, out var ct) ? ct : new List<ProjectMember>();
            bool Reaches(ProjectMember m) => m.TerritoryId is { } t && reached.Contains((t, link.CompanyId));
            var access = caretakers.Count == 0 ? ""
                : caretakers.Count == 1 ? (Reaches(caretakers[0]) ? "Yes" : "No")
                : string.Join("; ", caretakers.Select(m => name(m.User) + ": " + (Reaches(m) ? "Yes" : "No")));
            companyLines.Add(new CaretakeLine(p, Status(p) + " · " + StateOf(p, today), link.CompanyId,
                c is null ? "#" + link.CompanyId.ToString(CultureInfo.InvariantCulture) : c.Name.Trim(), c is null ? "" : c.Brn,
                owned ? name(o.Owner) : "", owned ? Territories.Title(o.Territory) : "",
                string.Join("; ", caretakers.Select(m => name(m.User) + " (" + ProjectRoles.List.Label(m.Role) + ")")), access,
                remarks.TryGetValue(link.CompanyId, out var r) ? string.Join("; ", r.Take(10)) : ""));
        }
        return (total, projectLines, companyLines, companyTotal);
    }
}
