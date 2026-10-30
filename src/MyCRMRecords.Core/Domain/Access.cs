using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>
/// MyCRM's temporary territories (its Projects and CSST parts, which stay in OutSystems): which companies people reach
/// through them. Read live from MyCRM, never copied.
/// </summary>
public interface ITemporaryTerritories
{
    /// <summary>MyCRM's Ids of the active companies of the active temporary territories of these people (by login).</summary>
    IReadOnlyCollection<long> CompanyIds(IReadOnlyCollection<string> logins);
}

/// <summary>
/// Who may open which company: MyCRM's UserCanAccessCompany with its Session.UserList (GetSub). Some roles open every
/// company (<see cref="CrmRoles.SeeAllCompanies"/>); everyone else opens the active companies of the active territories
/// of the people in their list: the permanent territories kept here, and MyCRM's temporary ones (read from MyCRM when
/// it is connected). An inactive company is opened only by the roles that open every company, as in MyCRM.
/// </summary>
public sealed class Access(IRecordStore store, Func<ITemporaryTerritories?> temporary)
{
    /// <summary>
    /// MyCRM's GetSub for the roles that do not open every company: the people whose territories' companies the person
    /// opens. The roles that open every company are not listed (their list does not change what they open).
    /// </summary>
    public IReadOnlyCollection<string> UserList(string login, string role)
    {
        var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { login };
        var all = store.Find<CrmUser>();
        // "My teams": the teams of all the person's records with a team (MyCRM reads them all, active or not).
        var myTeams = all.Where(u => Strings.SameText(u.Login, login) && u.TeamId is not null).Select(u => u.TeamId!.Value).Distinct().ToList();
        // "Users of a team": its records with a role, active or not.
        IEnumerable<string> TeamUsers(params string[] roles) =>
            all.Where(u => u.TeamId is { } t && myTeams.Contains(t) && u.Role.Length > 0 && (roles.Length == 0 || CrmRoles.Is(u.Role, roles))).Select(u => u.Login);
        switch (role)
        {
            case CrmRoles.PpSpecialistManager or CrmRoles.SpecialistSeniorManager:
                list.UnionWith(TeamUsers(CrmRoles.PpSpecialist, CrmRoles.MdsSpecialist, CrmRoles.MitsSpecialist, CrmRoles.PpSpecialistManager,
                    CrmRoles.MdsSpecialistManager, CrmRoles.MitsSpecialistManager));
                break;
            case "" or CrmRoles.Sales or CrmRoles.PpSpecialist or CrmRoles.CleTrainer or CrmRoles.ItHelpdesk or CrmRoles.Sro or CrmRoles.Helpdesk or CrmRoles.ServiceEngineer:
                break;
            case CrmRoles.MdsSpecialist or CrmRoles.MitsSpecialist:
                list.UnionWith(TeamUsers(CrmRoles.Sales, CrmRoles.CleTrainer, CrmRoles.ChannelPartner, CrmRoles.Telemarketer, CrmRoles.Contract,
                    CrmRoles.MitsSpecialist, CrmRoles.MdsSpecialist));
                break;
            case CrmRoles.Manager:
                list.UnionWith(TeamUsers(CrmRoles.Sales, CrmRoles.CleTrainer, CrmRoles.ChannelPartner, CrmRoles.Telemarketer, CrmRoles.Contract));
                break;
            case CrmRoles.Credit or CrmRoles.Opo or CrmRoles.StockAllocator:
                // Every person with an active CRM user record.
                list.UnionWith(all.Where(u => u.IsActive).Select(u => u.Login));
                break;
            default:
                // Any other role: everyone in their teams with a role.
                list.UnionWith(TeamUsers());
                break;
        }
        list.RemoveWhere(l => l.Length == 0);
        return list;
    }

    /// <summary>The companies the person opens; null for every company.</summary>
    public HashSet<long>? CompanyIds(Actor actor) => actor.SeesAllCompanies ? null : new HashSet<long>(CompanyReasons(actor).Keys);

    /// <summary>What a company opened through a temporary territory (MyCRM's projects and CSST) says it was opened by.</summary>
    public const string Project = "Project";

    /// <summary>
    /// The active companies the person opens (whatever their role: for the roles that open every company this is only what
    /// their territories give), each with how: the name of a territory of the person or of someone in their list, else
    /// <see cref="Project"/> for MyCRM's temporary territories.
    /// </summary>
    public Dictionary<long, string> CompanyReasons(Actor actor)
    {
        var people = UserList(actor.Login, actor.CrmRole);
        var reasons = new Dictionary<long, string>();
        var territories = store.Find<Territory>(Query.Where("IsActive", true).In("Owner", people.Cast<object>()));
        if (territories.Count > 0)
        {
            // The person's own territories first, then their list's.
            var names = territories.OrderBy(t => Strings.SameText(t.Owner, actor.Login) ? 0 : 1).ThenBy(t => t.Id)
                .Select(t => (t.Id, Name: t.Name.Trim().Length > 0 ? t.Name.Trim() : "Territory " + t.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();
            var links = store.Find<CompanyTerritory>(Query.Where("IsActive", true).In("TerritoryId", territories.Select(t => (object)t.Id)));
            foreach (var (territoryId, name) in names)
                foreach (var link in links.Where(l => l.TerritoryId == territoryId))
                    if (!reasons.ContainsKey(link.CompanyId)) reasons[link.CompanyId] = name;
        }
        // MyCRM's temporary territories give MyCRM's company Ids, which are the companies' Ids.
        if (temporary() is { } more)
            foreach (var id in more.CompanyIds(people.ToList()))
                if (!reasons.ContainsKey(id)) reasons[id] = Project;
        // Only active companies, as MyCRM.
        if (reasons.Count > 0)
        {
            var active = new HashSet<long>();
            foreach (var chunk in reasons.Keys.Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => (object)x.v))
                active.UnionWith(store.Find<Company>(Query.Where("IsActive", true).In("Id", chunk)).Select(c => c.Id));
            foreach (var id in reasons.Keys.Where(id => !active.Contains(id)).ToList()) reasons.Remove(id);
        }
        return reasons;
    }

    /// <summary>
    /// The companies list's "Mine": the active companies of the active permanent territories of the people in the
    /// person's list (<see cref="UserList"/>: their own and, by their role, their team's), whatever the person opens.
    /// MyCRM's temporary (project) territories do not count.
    /// </summary>
    public HashSet<long> OwnCompanyIds(Actor actor)
    {
        var people = UserList(actor.Login, actor.CrmRole);
        var territories = store.Find<Territory>(Query.Where("IsActive", true).In("Owner", people.Cast<object>())).Select(t => (object)t.Id).ToList();
        var ids = new HashSet<long>();
        if (territories.Count > 0)
            ids.UnionWith(store.Find<CompanyTerritory>(Query.Where("IsActive", true).In("TerritoryId", territories)).Select(ct => ct.CompanyId));
        return ids;
    }

    /// <summary>MyCRM's UserCanAccessCompany: whether the person may open the company.</summary>
    public bool CanOpen(Actor actor, Company company)
    {
        if (actor.SeesAllCompanies) return true;
        if (!company.IsActive) return false;
        var people = UserList(actor.Login, actor.CrmRole);
        var links = store.Find<CompanyTerritory>(Query.Where("CompanyId", company.Id).And("IsActive", true));
        if (links.Count > 0)
        {
            var territories = store.Find<Territory>(Query.Where("IsActive", true).In("Id", links.Select(l => (object)l.TerritoryId)));
            if (territories.Any(t => people.Contains(t.Owner))) return true;
        }
        return temporary() is { } more && more.CompanyIds(people.ToList()).Contains(company.Id);
    }
}
