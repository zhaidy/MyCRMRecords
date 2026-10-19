using System.Globalization;
using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>
/// The CRM users and teams (MyCRM's Users screens, for Sales Admins): a person's MyCRM role, teams and details. A person
/// has one CRM user record per team, as in MyCRM; saving changes the records in place (MyCRM deleted them and made new
/// ones), adds one for a new team and makes the others inactive without their team, so MyCRM still reads the person's
/// role, teams and colleagues as it did.
/// </summary>
public sealed class CrmUsers(IRecordStore store, People people, Func<DateTime> clock)
{
    public sealed class ProfileInput
    {
        public string? Role { get; set; }
        public string? Designation { get; set; }
        public string? PhoneNumber { get; set; }
        public string? SalesmanCode { get; set; }
        public string? TerritoryCode { get; set; }
        public List<long>? Teams { get; set; }
    }

    public sealed class TeamInput
    {
        public string? Name { get; set; }
        public string? ShortName { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>A person on the CRM users page: their first record's role and details, and their active teams.</summary>
    public sealed record Person(string Login, string Name, string Email, string Role, IReadOnlyList<long> Teams, string Designation, string PhoneNumber,
        string SalesmanCode, string TerritoryCode, bool IsCrmUser, int Territories);

    private static OperationResult NotAllowed() => OperationResult.Failure(ResultCodes.Forbidden, "The CRM users are kept by Sales Admins.");

    public IReadOnlyList<CrmTeam> Teams(bool includeInactive = true) =>
        store.Find<CrmTeam>(includeInactive ? Query.All() : Query.Where("IsActive", true)).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The person as the page shows them (no record: not a CRM user).</summary>
    public Person PersonOf(string login, IReadOnlyList<CrmUser>? records = null, IReadOnlyDictionary<string, int>? territories = null)
    {
        var mine = (records ?? store.Find<CrmUser>(Query.Where("Login", login))).Where(u => Strings.SameText(u.Login, login)).OrderBy(u => u.Id).ToList();
        var first = mine.FirstOrDefault();
        var active = mine.Where(u => u.IsActive).ToList();
        var user = people.User(login);
        var count = territories is not null ? (territories.TryGetValue(login, out var n) ? n : 0)
            : store.Count<Territory>(Query.Where("Owner", login).And("IsActive", true));
        return new Person(login, people.NameOf(login), user?.Email ?? "", first?.Role ?? "",
            active.Where(u => u.TeamId is not null).Select(u => u.TeamId!.Value).Distinct().ToList(), (active.FirstOrDefault() ?? first)?.Designation ?? "",
            (active.FirstOrDefault() ?? first)?.PhoneNumber ?? "", (active.FirstOrDefault() ?? first)?.SalesmanCode ?? "", (active.FirstOrDefault() ?? first)?.TerritoryCode ?? "",
            active.Count > 0, count);
    }

    /// <summary>
    /// The people of the CRM users page: every CRM user, or, with a search, everyone known whose name, login, email,
    /// salesman or territory code or role has the text (MyCRM showed nobody before two letters were typed).
    /// </summary>
    public IReadOnlyList<Person> List(string? q, bool includeNotCrm = false)
    {
        var text = (q ?? "").Trim();
        var records = store.Find<CrmUser>();
        var territories = store.Find<Territory>(Query.Where("IsActive", true)).Where(t => t.Owner.Length > 0)
            .GroupBy(t => t.Owner, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var logins = new HashSet<string>(records.Where(u => u.IsActive).Select(u => u.Login), StringComparer.OrdinalIgnoreCase);
        if (text.Length > 0 || includeNotCrm)
        {
            logins.UnionWith(records.Select(u => u.Login));
            logins.UnionWith(store.Find<AppUser>().Select(u => u.Login));
        }
        var byLogin = records.GroupBy(u => u.Login, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => (IReadOnlyList<CrmUser>)g.ToList(), StringComparer.OrdinalIgnoreCase);
        bool Has(string? value) => (value ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        return logins.Where(l => l.Length > 0)
            .Select(l => PersonOf(l, byLogin.TryGetValue(l, out var r) ? r : Array.Empty<CrmUser>(), territories))
            .Where(p => text.Length == 0 || Has(p.Name) || Has(p.Login) || Has(p.Email) || Has(p.SalesmanCode) || Has(p.TerritoryCode) || Has(CrmRoles.List.Label(p.Role)))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Sets a person's MyCRM role, teams and details. The teams a role takes follow MyCRM: Sales, PP Specialist and
    /// CLETrainer one team; the managers' roles the team named after them; CSST one team or none; every other role any
    /// number of teams. With no team, the person is no longer a CRM user (as in MyCRM).
    /// </summary>
    public OperationResult Save(Actor actor, string login, ProfileInput input)
    {
        if (!actor.IsSalesAdmin) return NotAllowed();
        login = (login ?? "").Trim();
        if (login.Length == 0 || login.Length > 256) return OperationResult.Failure(ResultCodes.Validation, "Choose a person.");
        var errors = new List<FieldError>();
        var role = (input.Role ?? "").Trim();
        if (!CrmRoles.List.IsKnown(role)) errors.Add(new FieldError("role", "Choose a role."));
        var active = Teams(includeInactive: false).ToDictionary(t => t.Id);
        var teams = (input.Teams ?? new List<long>()).Distinct().ToList();
        if (teams.Any(t => !active.ContainsKey(t))) errors.Add(new FieldError("teams", "Choose active teams only."));
        if (CrmRoles.Is(role, CrmRoles.OneTeam) || CrmRoles.Is(role, CrmRoles.Managers))
        {
            if (teams.Count == 0) errors.Add(new FieldError("teams", "Choose a team."));
            else if (teams.Count > 1) errors.Add(new FieldError("teams", "Choose one team for this role."));
            else if (CrmRoles.Is(role, CrmRoles.Managers) && active.TryGetValue(teams[0], out var team) && !Strings.SameText(team.Name, people.NameOf(login)))
                errors.Add(new FieldError("teams", $"A manager is put in the team named after them ({people.NameOf(login)}): create it with Create team."));
        }
        else if (role == CrmRoles.Csst && teams.Count > 1) errors.Add(new FieldError("teams", "Choose one team for this role (or none)."));
        string Field(string? value, int length, string field, string label)
        {
            var text = (value ?? "").Trim();
            if (text.Length > length) errors.Add(new FieldError(field, $"{label}: at most {length.ToString(CultureInfo.InvariantCulture)} characters."));
            return text;
        }
        var designation = Field(input.Designation, 50, "designation", "Designation");
        var phone = Field(input.PhoneNumber, 20, "phoneNumber", "Phone Number");
        var salesman = Field(input.SalesmanCode, 50, "salesmanCode", "Salesman Code");
        var territoryCode = Field(input.TerritoryCode, 50, "territoryCode", "Territory Code");
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        var now = clock();
        var by = Strings.Shorten(actor.ChangedBy, 600);
        store.Batch(s =>
        {
            var mine = s.Find<CrmUser>(Query.Where("Login", login)).OrderBy(u => u.Id).ToList();
            var keep = mine.Where(u => u.IsActive && u.TeamId is { } t && teams.Contains(t)).GroupBy(u => u.TeamId).Select(g => g.First()).ToList();
            var spare = mine.Where(u => !keep.Contains(u)).ToList();
            var profile = mine.FirstOrDefault(u => u.IsActive) ?? mine.FirstOrDefault();
            void Fill(CrmUser u)
            {
                u.Role = role;
                u.Designation = designation;
                u.PhoneNumber = phone;
                u.SalesmanCode = salesman;
                u.TerritoryCode = territoryCode;
                u.UpdatedBy = actor.Login;
                u.UpdatedAt = now;
            }
            foreach (var u in keep)
            {
                Fill(u);
                s.Update(u);
            }
            foreach (var team in teams.Where(t => !keep.Any(k => k.TeamId == t)))
            {
                // A record taken away before is used again (its Id stays the person's first), else a new one.
                var reused = spare.FirstOrDefault();
                if (reused is not null)
                {
                    spare.Remove(reused);
                    reused.TeamId = team;
                    reused.IsActive = true;
                    reused.RemovedOn = null;
                    reused.RemovedBy = "";
                    Fill(reused);
                    s.Update(reused);
                }
                else
                {
                    var record = new CrmUser { Login = login, TeamId = team, IsActive = true, CreatedBy = actor.Login, CreatedAt = now, CId = profile?.CId ?? 0, ProfileId = profile?.ProfileId };
                    Fill(record);
                    s.Insert(record);
                }
            }
            // The others: inactive with no team (MyCRM reads every record's team and role). With no team left, no role
            // either: the person is not a CRM user, as when MyCRM deleted their records.
            foreach (var u in spare)
            {
                var changed = u.IsActive || u.TeamId is not null;
                if (u.TeamId is not null) u.FormerTeamId = u.TeamId;
                u.TeamId = null;
                if (u.IsActive)
                {
                    u.IsActive = false;
                    u.RemovedOn = now;
                    u.RemovedBy = by;
                }
                var newRole = teams.Count == 0 ? "" : role;
                if (u.Role != newRole) { u.Role = newRole; changed = true; }
                if (changed)
                {
                    u.UpdatedBy = actor.Login;
                    u.UpdatedAt = now;
                    s.Update(u);
                }
            }
        });
        return OperationResult.Success(teams.Count == 0
            ? $"{people.NameOf(login)} has no team, so is no longer a CRM user."
            : $"{people.NameOf(login)} saved: {CrmRoles.List.Label(role)}, {string.Join(", ", teams.Select(t => active[t].Name))}.");
    }

    /// <summary>
    /// MyCRM's "Delete User And Territory": the person's CRM user records become inactive (no team, no role), and their
    /// territories with their companies' links too.
    /// </summary>
    public OperationResult RemoveUserAndTerritories(Actor actor, string login)
    {
        if (!actor.IsSalesAdmin) return NotAllowed();
        login = (login ?? "").Trim();
        var records = store.Find<CrmUser>(Query.Where("Login", login));
        var territories = store.Find<Territory>(Query.Where("Owner", login).And("IsActive", true));
        if (records.All(u => !u.IsActive) && territories.Count == 0) return OperationResult.Failure(ResultCodes.NotFound, "That person is not a CRM user and has no territory.");
        var now = clock();
        var by = Strings.Shorten(actor.ChangedBy, 600);
        store.Batch(s =>
        {
            foreach (var u in records.Where(u => u.IsActive || u.TeamId is not null || u.Role.Length > 0))
            {
                if (u.TeamId is not null) u.FormerTeamId = u.TeamId;
                u.TeamId = null;
                if (u.IsActive) { u.RemovedOn = now; u.RemovedBy = by; }
                u.IsActive = false;
                u.Role = "";
                u.UpdatedBy = actor.Login;
                u.UpdatedAt = now;
                s.Update(u);
            }
            foreach (var t in territories) Domain.Territories.Deactivate(s, t, actor, now);
        });
        return OperationResult.Success($"{people.NameOf(login)} is no longer a CRM user; {territories.Count.ToString(CultureInfo.InvariantCulture)} territor{(territories.Count == 1 ? "y was" : "ies were")} taken away.");
    }

    /// <summary>Creates or changes a team (MyCRM's TeamDetail). Teams are never deleted: switched inactive.</summary>
    public OperationResult SaveTeam(Actor actor, long id, TeamInput input)
    {
        if (!actor.IsSalesAdmin) return NotAllowed();
        var team = id == 0 ? new CrmTeam() : store.Get<CrmTeam>(id);
        if (team is null) return OperationResult.Failure(ResultCodes.NotFound, "That team is not there.");
        var name = (input.Name ?? "").Trim();
        var shortName = (input.ShortName ?? "").Trim();
        var errors = new List<FieldError>();
        if (name.Length == 0) errors.Add(new FieldError("name", "Enter the team's name."));
        else if (name.Length > 255) errors.Add(new FieldError("name", "At most 255 characters."));
        if (shortName.Length > 50) errors.Add(new FieldError("shortName", "At most 50 characters."));
        if (name.Length > 0 && store.Find<CrmTeam>(Query.Where("IsActive", true)).Any(t => t.Id != team.Id && Strings.SameText(t.Name, name)) && input.IsActive)
            errors.Add(new FieldError("name", "There is an active team with this name."));
        if (errors.Count > 0) return OperationResult.Invalid(errors);
        team.Name = name;
        team.ShortName = shortName;
        team.IsActive = input.IsActive;
        if (id == 0) store.Insert(team);
        else store.Update(team);
        return OperationResult.Success($"Team {team.Name} {(id == 0 ? "created" : "saved")}{(team.IsActive ? "" : " (inactive)")}.", team.Id);
    }

    /// <summary>MyCRM's CreateTeamByUserId: the active team named after the person, made when there is none.</summary>
    public OperationResult CreateTeamFor(Actor actor, string login)
    {
        if (!actor.IsSalesAdmin) return NotAllowed();
        var name = Strings.Shorten(people.NameOf((login ?? "").Trim()), 255);
        if (name.Length == 0) return OperationResult.Failure(ResultCodes.Validation, "Choose a person.");
        if (store.Find<CrmTeam>(Query.Where("IsActive", true)).FirstOrDefault(t => Strings.SameText(t.Name, name)) is { } existing)
            return OperationResult.Success($"Team {existing.Name} already exists.", existing.Id);
        var team = store.Insert(new CrmTeam { Name = name, IsActive = true });
        return OperationResult.Success($"Team {team.Name} created.", team.Id);
    }

    /// <summary>The people in a team (MyCRM's TeamDetails): its active records, by role order.</summary>
    public IReadOnlyList<(string Login, string Name, string Role)> Members(long teamId)
    {
        var order = CrmRoles.List.All.Select(c => c.Code).ToList();
        return store.Find<CrmUser>(Query.Where("TeamId", teamId).And("IsActive", true))
            .Select(u => (u.Login, people.NameOf(u.Login), u.Role)).Distinct()
            .OrderBy(m => order.IndexOf(m.Role) < 0 ? int.MaxValue : order.IndexOf(m.Role)).ThenBy(m => m.Item2, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
