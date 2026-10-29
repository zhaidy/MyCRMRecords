using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>
/// A person as MyCRM knows them: their MyCRM role (the role of their first CUser row, MyCRM's Role()), their OutSystems
/// users, and the OutSystems users whose costing sheets they see (MyCRM's GetSub list: themselves and, for the roles that
/// have one, the people of their teams).
/// </summary>
public sealed record OrdersProfile(string Role, IReadOnlyCollection<long> Me, IReadOnlyCollection<long> People)
{
    public static readonly OrdersProfile None = new("", Array.Empty<long>(), Array.Empty<long>());
}

/// <summary>Someone an administrator may sign in as, with their MyCRM role's label ("" without one).</summary>
public sealed record PersonToSignInAs(string Login, string Name, string Email, string Role);

/// <summary>A CUser row of MyCRM: one per person and team, with MyCRM's role (a <see cref="CrmRoles"/> code).</summary>
public sealed record CrmUserRow(long Id, long UserId, string Role, long? TeamId, bool IsActive, string SalesmanCode = "");

/// <summary>
/// Who sees which costing sheets: MyOrders' rule on its CostingSheets screen. The roles of
/// <see cref="CrmRoles.SeeAllCostingSheets"/> (and MyCRMRecords' administrators) see every costing sheet; everyone
/// else sees those whose salesperson is in their MyCRM list (<see cref="UserList"/>, MyCRM's GetSub), and a Service
/// Delivery Manager only those of companies whose name contains "dbs". Read live from MyCRM's tables.
/// </summary>
public sealed class OrdersAccess(IMyOrdersData data)
{
    /// <summary>
    /// The person's MyCRM profile. Their OutSystems users are found by login or, as the other apps do, by email address
    /// (an OutSystems username is not always the Windows login: RSP\cara.tan@ricoh.sg for RSP\ctan).
    /// </summary>
    public OrdersProfile Profile(string login, string email)
    {
        if (!data.Available) return OrdersProfile.None;
        var me = new HashSet<long>(data.Users()
            .Where(u => Strings.SameText(u.Login, login) || (Strings.IsEmail(email) && Strings.SameText(u.Email, email)))
            .Select(u => u.Id));
        if (me.Count == 0) return OrdersProfile.None;
        var rows = CrmUsers();
        var role = rows.Where(r => me.Contains(r.UserId)).OrderBy(r => r.Id).Select(r => r.Role).FirstOrDefault() ?? "";
        // The people they see, each with their other OutSystems user (a salesperson kept twice: SamePerson).
        return new OrdersProfile(role, me.ToList(), SamePerson(UserList(rows, me, role)));
    }

    /// <summary>
    /// The people an administrator may sign in as, to see what they see: everyone MyCRM has a user for and everyone who
    /// is the salesperson or a specialist of a costing sheet (their OutSystems users), whether or not they have opened
    /// MyCRMRecords yet, and the people MyCRMRecords knows (<paramref name="known"/>). Someone known here under
    /// another login than their OutSystems username is listed once, under the login they sign in with (matched by email
    /// address, as <see cref="Profile"/> finds them).
    /// </summary>
    public IReadOnlyList<PersonToSignInAs> PeopleToSignInAs(IReadOnlyList<AppUser> known)
    {
        var people = new Dictionary<string, PersonToSignInAs>(StringComparer.OrdinalIgnoreCase);
        var knownByEmail = new Dictionary<string, AppUser>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in known)
            if (Strings.IsEmail(u.Email) && !knownByEmail.ContainsKey(u.Email.Trim())) knownByEmail[u.Email.Trim()] = u;
        if (data.Available)
        {
            var roles = new Dictionary<long, string>();
            foreach (var row in CrmUsers().OrderBy(r => r.Id))
                if (!roles.ContainsKey(row.UserId)) roles[row.UserId] = row.Role;
            var sheets = data.People();
            var wanted = new HashSet<long>(roles.Keys);
            wanted.UnionWith(sheets.Salesmen);
            wanted.UnionWith(sheets.Specialists);
            foreach (var u in data.Users().Where(u => wanted.Contains(u.Id) && u.Login.Length > 0))
            {
                var email = u.Email.Trim();
                var login = Strings.IsEmail(email) && knownByEmail.TryGetValue(email, out var k) ? k.Login : u.Login;
                var role = roles.TryGetValue(u.Id, out var code) ? CrmRoles.List.Label(code) : "";
                if (people.TryGetValue(login, out var seen))
                {
                    if (seen.Role.Length == 0 && role.Length > 0) people[login] = seen with { Role = role };
                    continue;
                }
                people[login] = new PersonToSignInAs(login, u.Name.Length > 0 ? u.Name : login, email, role);
            }
        }
        foreach (var u in known)
            if (!people.ContainsKey(u.Login)) people[u.Login] = new PersonToSignInAs(u.Login, u.DisplayName.Length > 0 ? u.DisplayName : u.Login, u.Email, "");
        return people.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Login, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// These OutSystems users and the other users of the same people: OutSystems often keeps someone twice
    /// (RSP\alfriskuay and RSP\AlfrisKuay@ricoh.sg, one login once the address is dropped, or two usernames with one
    /// email address). The same rule as <see cref="Profile"/>, which gives a person all their users.
    /// </summary>
    public IReadOnlyCollection<long> SamePerson(IEnumerable<long> userIds)
    {
        var ids = new HashSet<long>(userIds);
        if (!data.Available || ids.Count == 0) return ids.ToList();
        var users = data.Users();
        var logins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in users.Where(u => ids.Contains(u.Id)))
        {
            if (u.Login.Trim().Length > 0) logins.Add(u.Login.Trim());
            if (Strings.IsEmail(u.Email)) emails.Add(u.Email.Trim());
        }
        foreach (var u in users)
            if (logins.Contains(u.Login.Trim()) || (Strings.IsEmail(u.Email) && emails.Contains(u.Email.Trim()))) ids.Add(u.Id);
        return ids.ToList();
    }

    /// <summary>MyCRM's CUser rows with their roles as <see cref="CrmRoles"/> codes (matched by label, never by Id).</summary>
    public IReadOnlyList<CrmUserRow> CrmUsers()
    {
        var roles = data.All(Entities.CrmRole).ToDictionary(r => r.Long("ID"), r => CrmRoles.List.FromLabel(r.Text("LABEL")));
        return data.All(Entities.CrmUser)
            .Select(r => new CrmUserRow(r.Long("ID"), r.Long("USERID"), r.Id("ROLEID") is { } id && roles.TryGetValue(id, out var code) ? code : "",
                r.Id("TEAMID"), r.Bool("ISACTIVE"), r.Text("SALESMANCODE")))
            .ToList();
    }

    /// <summary>
    /// MyCRM's GetSub: the OutSystems users whose data the person sees, from their role and the teams of all their CUser
    /// rows (active or not, as MyCRM reads them). The same as <see cref="Access.UserList"/> (which works by login), by OutSystems user.
    /// </summary>
    public static IReadOnlyCollection<long> UserList(IReadOnlyList<CrmUserRow> all, IReadOnlyCollection<long> me, string role)
    {
        var list = new HashSet<long>(me);
        var myTeams = all.Where(u => me.Contains(u.UserId) && u.TeamId is not null).Select(u => u.TeamId!.Value).Distinct().ToList();
        // "Users of a team": its rows with a role, active or not.
        IEnumerable<long> TeamUsers(params string[] roles) =>
            all.Where(u => u.TeamId is { } t && myTeams.Contains(t) && u.Role.Length > 0 && (roles.Length == 0 || CrmRoles.Is(u.Role, roles))).Select(u => u.UserId);
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
                // Every person with an active CRM user row.
                list.UnionWith(all.Where(u => u.IsActive).Select(u => u.UserId));
                break;
            default:
                // Any other role: everyone in their teams with a role.
                list.UnionWith(TeamUsers());
                break;
        }
        list.Remove(0);
        return list;
    }

    /// <summary>What the person sees in the list: everyone's costing sheets (null) or those of these salespeople, and a company filter.</summary>
    public (IReadOnlyCollection<long>? Salesmen, string? CompanyContains) Scope(Actor actor)
    {
        if (actor.SeesAllCostingSheets) return (null, null);
        return (actor.Orders.People, actor.Orders.Role == CrmRoles.ServiceDeliveryManager ? CrmRoles.ServiceDeliveryManagerCompanies : null);
    }

    /// <summary>Whether the person may open a costing sheet of this salesperson and company (the list's rule).</summary>
    public bool CanOpen(Actor actor, long? salesmanId, string companyName)
    {
        var (salesmen, company) = Scope(actor);
        if (salesmen is not null && (salesmanId is not { } id || !salesmen.Contains(id))) return false;
        return company is null || (companyName ?? "").IndexOf(company, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Whose request forms the person sees in the list: MyOrders' RequestForms list (the ShowAll roles every one; else
    /// those of the salespeople in their MyCRM list; Service Delivery Managers DBS's only), together with what its
    /// specialists' list (SpecialistRequestForms) showed them: the request forms where they are the MDS or Fusion
    /// specialist; for MDS and Fusion specialist managers and specialist senior managers every request form with a
    /// specialist; for PP specialists and PP specialist managers those whose PP specialist is in their MyCRM list.
    /// </summary>
    public RequestFormScope RequestFormsScope(Actor actor)
    {
        var company = actor.Orders.Role == CrmRoles.ServiceDeliveryManager ? CrmRoles.ServiceDeliveryManagerCompanies : null;
        if (actor.SeesAllCostingSheets) return new RequestFormScope(null, Array.Empty<long>(), false, Array.Empty<long>(), company);
        var role = actor.Orders.Role;
        return new RequestFormScope(actor.Orders.People, actor.Orders.Me,
            CrmRoles.Is(role, CrmRoles.MdsSpecialistManager, CrmRoles.MitsSpecialistManager, CrmRoles.SpecialistSeniorManager),
            CrmRoles.Is(role, CrmRoles.PpSpecialist, CrmRoles.PpSpecialistManager) ? actor.Orders.People : Array.Empty<long>(),
            company);
    }

    /// <summary>Whether the person may open a request form (the list's rule, <see cref="RequestFormsScope"/>), from its row and company name.</summary>
    public bool CanOpenRequestForm(Actor actor, IReadOnlyDictionary<string, object?> requestForm, string companyName)
    {
        var scope = RequestFormsScope(actor);
        if (scope.CompanyContains is { } part && (companyName ?? "").IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) return false;
        if (scope.Salesmen is null) return true;
        bool In(IReadOnlyCollection<long> ids, string column) => requestForm.Id(column) is { } id && ids.Contains(id);
        return In(scope.Salesmen, "SALESMANID")
            || In(scope.SpecialistOf, "MDSSPECIALISTID") || In(scope.SpecialistOf, "MITSSPECIALISTID")
            || (scope.AnyWithSpecialist && (requestForm.Id("MDSSPECIALISTID") is not null || requestForm.Id("PPSPECIALISTID") is not null || requestForm.Id("MITSSPECIALISTID") is not null))
            || In(scope.PpSpecialistOf, "PPSPECIALISTID");
    }
}

/// <summary>Whose request forms a person sees (<see cref="OrdersAccess.RequestFormsScope"/>); Salesmen is null for every request form.</summary>
public sealed record RequestFormScope(IReadOnlyCollection<long>? Salesmen, IReadOnlyCollection<long> SpecialistOf, bool AnyWithSpecialist,
    IReadOnlyCollection<long> PpSpecialistOf, string? CompanyContains);
