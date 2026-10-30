using System.Globalization;
using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>
/// Named territories, kept by Sales Admins (and MD): a name and a number, a salesperson, and companies. They replace
/// MyCRM's territories built from Advance (README, rules changed on purpose). One rule moves a company: putting it in a
/// territory takes it out of every other territory (as MyCRM's bulk transfer screen did); each change of salesperson is
/// recorded as a territory movement. Nothing is deleted: a territory or a company link taken away is inactive, and a
/// company put back gets a new link.
/// </summary>
public sealed class Territories(IRecordStore store, People people, Func<DateTime> clock)
{
    public const int PageSize = 50;

    public sealed class TerritoryInput
    {
        public string? Name { get; set; }
        public long? Number { get; set; }
        /// <summary>The salesperson's login; empty for none (vacant).</summary>
        public string? Owner { get; set; }
    }

    public sealed class CompaniesInput
    {
        public List<long>? Companies { get; set; }
    }

    public sealed class TextInput
    {
        public string? Text { get; set; }
    }

    /// <summary>A line of a pasted list of companies, as found (MyCRM's ValidateCompany).</summary>
    public sealed record Match(int Line, string Text, long? CompanyId, string Company, string Brn, string Salesperson, string Error);

    /// <summary>A company as the territory pages show it: its salesperson and territory.</summary>
    public sealed record CompanyRow(long Id, string Name, string Brn, string Owner, string OwnerName, long? TerritoryId, string Territory);

    /// <summary>"North 1", or "Territory 1203" for a territory with no name (those MyCRM made).</summary>
    public static string Title(Territory t) =>
        t.Name.Length > 0 ? t.Name : t.Number != 0 ? "Territory " + t.Number.ToString(CultureInfo.InvariantCulture) : "Territory #" + t.Id.ToString(CultureInfo.InvariantCulture);

    private static OperationResult NotAllowed() => OperationResult.Failure(ResultCodes.Forbidden, "Territories are kept by Sales Admins.");

    public IReadOnlyList<Territory> All(bool includeInactive = false) =>
        store.Find<Territory>(includeInactive ? Query.All() : Query.Where("IsActive", true))
            .OrderBy(t => !t.IsActive).ThenBy(t => Title(t), StringComparer.OrdinalIgnoreCase).ToList();

    public Territory? Get(long id) => store.Get<Territory>(id);

    /// <summary>The number of active companies of each territory.</summary>
    public IReadOnlyDictionary<long, int> CompanyCounts() =>
        store.Find<CompanyTerritory>(Query.Where("IsActive", true)).GroupBy(ct => ct.TerritoryId).ToDictionary(g => g.Key, g => g.Select(ct => ct.CompanyId).Distinct().Count());

    /// <summary>The people a territory can be given to: everyone with an active CRM user record of role Sales (MyCRM's choice), by name.</summary>
    public IReadOnlyList<(string Login, string Name)> Salespeople() =>
        store.Find<CrmUser>(Query.Where("IsActive", true).And("Role", CrmRoles.Sales)).Select(u => u.Login).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(l => (l, people.NameOf(l))).OrderBy(p => p.Item2, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// The salesperson of each company (MyCRM's GetCompanyOwner): the owner of its first active link to an active
    /// territory with a salesperson; with that territory.
    /// </summary>
    public IReadOnlyDictionary<long, (string Owner, Territory Territory)> Owners(IEnumerable<long> companyIds)
    {
        var result = new Dictionary<long, (string, Territory)>();
        var ids = companyIds.Distinct().ToList();
        if (ids.Count == 0) return result;
        var links = new List<CompanyTerritory>();
        foreach (var chunk in ids.Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => (object)x.v))
            links.AddRange(store.Find<CompanyTerritory>(Query.Where("IsActive", true).In("CompanyId", chunk)));
        var territories = links.Count == 0 ? new Dictionary<long, Territory>()
            : store.Find<Territory>(Query.Where("IsActive", true).In("Id", links.Select(l => (object)l.TerritoryId).Distinct())).ToDictionary(t => t.Id);
        foreach (var link in links.OrderBy(l => l.Id))
            if (!result.ContainsKey(link.CompanyId) && territories.TryGetValue(link.TerritoryId, out var t))
                result[link.CompanyId] = (t.Owner, t);
        return result;
    }

    /// <summary>
    /// The companies with an active link to an active permanent territory: those not in it are MyCRM's "Unassigned
    /// Companies" (the companies list's "No salesperson").
    /// </summary>
    public HashSet<long> AssignedCompanyIds()
    {
        var active = new HashSet<long>(store.Find<Territory>(Query.Where("IsActive", true)).Select(t => t.Id));
        return new HashSet<long>(store.Find<CompanyTerritory>(Query.Where("IsActive", true)).Where(ct => active.Contains(ct.TerritoryId)).Select(ct => ct.CompanyId));
    }

    /// <summary>The company's salesperson's login, or "".</summary>
    public string OwnerOf(long companyId) => Owners([companyId]).TryGetValue(companyId, out var o) ? o.Owner : "";

    /// <summary>A company's territory movements, newest first.</summary>
    public IReadOnlyList<TerritoryMovement> Movements(long companyId) =>
        store.Find<TerritoryMovement>(Query.Where("CompanyId", companyId)).OrderByDescending(m => m.UpdatedAt).ThenByDescending(m => m.Id).ToList();

    /// <summary>The active companies of a territory, by name, searched by name or BRN.</summary>
    public (int Total, IReadOnlyList<Company> Rows) Companies(long territoryId, string? q, int skip, int take = PageSize)
    {
        var ids = store.Find<CompanyTerritory>(Query.Where("TerritoryId", territoryId).And("IsActive", true)).Select(ct => ct.CompanyId).Distinct().ToList();
        var companies = new List<Company>();
        foreach (var chunk in ids.Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => (object)x.v))
            companies.AddRange(store.Find<Company>(Query.Where("IsActive", true).In("Id", chunk).Search(q, "Name", "Brn")));
        var sorted = companies.OrderBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).ToList();
        return (sorted.Count, sorted.Skip(Math.Max(0, skip)).Take(take).ToList());
    }

    /// <summary>When each company of a territory was put in it (its active link's CreatedOn; the earliest when it has two).</summary>
    public IReadOnlyDictionary<long, DateTime?> Since(long territoryId) =>
        store.Find<CompanyTerritory>(Query.Where("TerritoryId", territoryId).And("IsActive", true)).GroupBy(ct => ct.CompanyId)
            .ToDictionary(g => g.Key, g => g.Select(ct => ct.CreatedOn).Where(d => d.HasValue).OrderBy(d => d).FirstOrDefault());

    /// <summary>Active companies whose name or BRN has the text, by name, with their salesperson (to put in a territory).</summary>
    public (int Total, IReadOnlyList<CompanyRow> Rows) Search(string? q, bool unassignedOnly, int skip, int take = PageSize)
    {
        var text = (q ?? "").Trim();
        if (text.Length < 2 && !unassignedOnly) return (0, Array.Empty<CompanyRow>());
        var found = store.Find<Company>(Query.Where("IsActive", true).Search(text.Length > 0 ? text : null, "Name", "Brn"));
        var owners = Owners(found.Select(c => c.Id));
        var rows = found.Where(c => !unassignedOnly || !owners.ContainsKey(c.Id)).OrderBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).ToList();
        return (rows.Count, rows.Skip(Math.Max(0, skip)).Take(take).Select(c => Row(c, owners)).ToList());
    }

    private CompanyRow Row(Company c, IReadOnlyDictionary<long, (string Owner, Territory Territory)> owners) =>
        owners.TryGetValue(c.Id, out var o)
            ? new CompanyRow(c.Id, c.Name.Trim(), c.Brn, o.Owner, people.NameOf(o.Owner), o.Territory.Id, Title(o.Territory))
            : new CompanyRow(c.Id, c.Name.Trim(), c.Brn, "", "", null, "");

    /// <summary>Creates or changes a territory. A new salesperson takes all its companies (each move recorded).</summary>
    public OperationResult Save(Actor actor, long id, TerritoryInput input)
    {
        if (!actor.CanManageTerritories) return NotAllowed();
        var territory = id == 0 ? new Territory() : store.Get<Territory>(id) is { IsActive: true } t ? t : null;
        if (territory is null) return OperationResult.Failure(ResultCodes.NotFound, "That territory is not there.");
        var errors = new List<FieldError>();
        var name = (input.Name ?? "").Trim();
        if (name.Length == 0) errors.Add(new FieldError("name", "Enter the territory's name."));
        else if (name.Length > 100) errors.Add(new FieldError("name", "At most 100 characters."));
        var active = store.Find<Territory>(Query.Where("IsActive", true)).Where(o => o.Id != territory.Id).ToList();
        if (name.Length > 0 && active.Any(o => Strings.SameText(o.Name, name))) errors.Add(new FieldError("name", "There is a territory with this name."));
        var number = input.Number ?? 0;
        // A territory MyCRM made by hand has the number 0, and some share a number: kept as they are unless changed.
        if (id == 0 || number != territory.Number)
        {
            if (number <= 0) errors.Add(new FieldError("number", "Enter the territory's number (1 or more)."));
            else if (active.FirstOrDefault(o => o.Number == number) is { } used)
                errors.Add(new FieldError("number", $"Territory number {number.ToString(CultureInfo.InvariantCulture)} is {Title(used)}'s."));
        }
        var owner = (input.Owner ?? "").Trim();
        if (owner.Length > 0 && !Strings.SameText(owner, territory.Owner) && !Salespeople().Any(p => Strings.SameText(p.Login, owner)))
            errors.Add(new FieldError("owner", "Choose a salesperson (someone with the CRM role Sales)."));
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        var now = clock();
        var previous = territory.Owner;
        territory.Name = name;
        territory.Number = number;
        territory.Owner = Strings.Shorten(owner, 256);
        if (id == 0)
        {
            territory.CreatedOn = now;
            territory.CreatedBy = actor.Login;
            territory.IsOwner = true;
            store.Insert(territory);
            return OperationResult.Success($"Territory {Title(territory)} created.", territory.Id);
        }
        var moved = 0;
        store.Batch(s =>
        {
            s.Update(territory);
            if (Strings.SameText(previous, territory.Owner)) return;
            foreach (var company in s.Find<CompanyTerritory>(Query.Where("TerritoryId", territory.Id).And("IsActive", true)).Select(ct => ct.CompanyId).Distinct())
            {
                s.Insert(new TerritoryMovement { CompanyId = company, FromUser = previous, ToUser = territory.Owner, UpdatedBy = actor.Login, UpdatedAt = now });
                moved++;
            }
        });
        return OperationResult.Success(Strings.SameText(previous, territory.Owner) ? $"Territory {Title(territory)} saved."
            : $"Territory {Title(territory)} saved: its {moved.ToString(CultureInfo.InvariantCulture)} compan{(moved == 1 ? "y is" : "ies are")} now {(territory.Owner.Length == 0 ? "without a salesperson" : people.NameOf(territory.Owner) + "'s")}.", territory.Id);
    }

    /// <summary>Takes a territory away: it and its companies' links become inactive (MyCRM's "Delete User And Territory").</summary>
    public OperationResult Remove(Actor actor, long id)
    {
        if (!actor.CanManageTerritories) return NotAllowed();
        if (store.Get<Territory>(id) is not { IsActive: true } territory) return OperationResult.Failure(ResultCodes.NotFound, "That territory is not there.");
        var count = 0;
        store.Batch(s => count = Deactivate(s, territory, actor, clock()));
        return OperationResult.Success($"Territory {Title(territory)} was taken away; its {count.ToString(CultureInfo.InvariantCulture)} compan{(count == 1 ? "y has" : "ies have")} no salesperson from it now.", territory.Id);
    }

    /// <summary>Marks a territory and its active links inactive, recording each company's move to nobody; returns how many companies.</summary>
    internal static int Deactivate(IRecordStore s, Territory territory, Actor actor, DateTime now)
    {
        var by = Strings.Shorten(actor.ChangedBy, 600);
        var companies = new HashSet<long>();
        foreach (var link in s.Find<CompanyTerritory>(Query.Where("TerritoryId", territory.Id).And("IsActive", true)))
        {
            link.IsActive = false;
            link.RemovedOn = now;
            link.RemovedBy = by;
            s.Update(link);
            if (companies.Add(link.CompanyId) && territory.Owner.Length > 0)
                s.Insert(new TerritoryMovement { CompanyId = link.CompanyId, FromUser = territory.Owner, ToUser = "", UpdatedBy = actor.Login, UpdatedAt = now });
        }
        territory.IsActive = false;
        territory.RemovedOn = now;
        territory.RemovedBy = by;
        s.Update(territory);
        return companies.Count;
    }

    /// <summary>Puts companies in a territory, taking each out of every other territory; each change of salesperson is recorded.</summary>
    public OperationResult AddCompanies(Actor actor, long territoryId, IReadOnlyCollection<long> companyIds)
    {
        if (!actor.CanManageTerritories) return NotAllowed();
        if (store.Get<Territory>(territoryId) is not { IsActive: true } territory) return OperationResult.Failure(ResultCodes.NotFound, "That territory is not there.");
        var ids = companyIds.Distinct().ToList();
        if (ids.Count == 0) return OperationResult.Failure(ResultCodes.Validation, "Choose the companies to put in the territory.");
        var added = 0;
        var already = 0;
        var notFound = 0;
        var now = clock();
        var by = Strings.Shorten(actor.ChangedBy, 600);
        store.Batch(s =>
        {
            foreach (var companyId in ids)
            {
                if (s.Get<Company>(companyId) is not { IsActive: true })
                {
                    notFound++;
                    continue;
                }
                var links = s.Find<CompanyTerritory>(Query.Where("CompanyId", companyId).And("IsActive", true)).OrderBy(l => l.Id).ToList();
                if (links.Any(l => l.TerritoryId == territory.Id))
                {
                    already++;
                    continue;
                }
                var territories = links.Count == 0 ? new Dictionary<long, Territory>()
                    : s.Find<Territory>(Query.All().In("Id", links.Select(l => (object)l.TerritoryId).Distinct())).ToDictionary(t => t.Id);
                var from = links.Select(l => territories.TryGetValue(l.TerritoryId, out var t) && t.IsActive ? t.Owner : "").FirstOrDefault(o => o.Length > 0) ?? "";
                foreach (var link in links)
                {
                    link.IsActive = false;
                    link.RemovedOn = now;
                    link.RemovedBy = by;
                    s.Update(link);
                }
                s.Insert(new CompanyTerritory { TerritoryId = territory.Id, CompanyId = companyId, IsActive = true, CreatedBy = actor.Login, CreatedOn = now });
                if (!Strings.SameText(from, territory.Owner))
                    s.Insert(new TerritoryMovement { CompanyId = companyId, FromUser = from, ToUser = territory.Owner, UpdatedBy = actor.Login, UpdatedAt = now });
                added++;
            }
        });
        var message = $"{added.ToString(CultureInfo.InvariantCulture)} compan{(added == 1 ? "y" : "ies")} put in {Title(territory)}.";
        if (already > 0) message += $" {already.ToString(CultureInfo.InvariantCulture)} {(already == 1 ? "was" : "were")} in it already.";
        if (notFound > 0) message += $" {notFound.ToString(CultureInfo.InvariantCulture)} {(notFound == 1 ? "is" : "are")} not an active company.";
        return added == 0 && (already > 0 || notFound > 0) ? OperationResult.Failure(ResultCodes.Validation, message) : OperationResult.Success(message, territory.Id);
    }

    /// <summary>Takes companies out of a territory (they have no salesperson from it then).</summary>
    public OperationResult RemoveCompanies(Actor actor, long territoryId, IReadOnlyCollection<long> companyIds)
    {
        if (!actor.CanManageTerritories) return NotAllowed();
        if (store.Get<Territory>(territoryId) is not { IsActive: true } territory) return OperationResult.Failure(ResultCodes.NotFound, "That territory is not there.");
        var ids = new HashSet<long>(companyIds);
        if (ids.Count == 0) return OperationResult.Failure(ResultCodes.Validation, "Choose the companies to take out of the territory.");
        var removed = 0;
        var now = clock();
        var by = Strings.Shorten(actor.ChangedBy, 600);
        store.Batch(s =>
        {
            var done = new HashSet<long>();
            foreach (var link in s.Find<CompanyTerritory>(Query.Where("TerritoryId", territory.Id).And("IsActive", true)).Where(l => ids.Contains(l.CompanyId)))
            {
                link.IsActive = false;
                link.RemovedOn = now;
                link.RemovedBy = by;
                s.Update(link);
                if (!done.Add(link.CompanyId)) continue;
                removed++;
                if (territory.Owner.Length > 0)
                    s.Insert(new TerritoryMovement { CompanyId = link.CompanyId, FromUser = territory.Owner, ToUser = "", UpdatedBy = actor.Login, UpdatedAt = now });
            }
        });
        return removed == 0 ? OperationResult.Failure(ResultCodes.Validation, "None of those companies is in the territory.")
            : OperationResult.Success($"{removed.ToString(CultureInfo.InvariantCulture)} compan{(removed == 1 ? "y" : "ies")} taken out of {Title(territory)}.", territory.Id);
    }

    /// <summary>
    /// Finds the companies of a pasted list (one per line: the company name, or the BRN, or both separated by a tab or a
    /// comma), as MyCRM's bulk transfer read its Excel file: an active company whose name or BRN is the line's. A company
    /// already in the territory is shown as such (MyCRM skipped it without saying).
    /// </summary>
    public IReadOnlyList<Match> MatchList(long territoryId, string? text)
    {
        var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var companies = store.Find<Company>(Query.Where("IsActive", true));
        var byName = companies.GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id).First(), StringComparer.OrdinalIgnoreCase);
        var byBrn = companies.Where(c => c.Brn.Trim().Length > 0).GroupBy(c => c.Brn.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id).First(), StringComparer.OrdinalIgnoreCase);
        var inTerritory = new HashSet<long>(store.Find<CompanyTerritory>(Query.Where("TerritoryId", territoryId).And("IsActive", true)).Select(l => l.CompanyId));
        var matches = new List<(int Line, string Text, Company? Company, string Error)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(new[] { '\t', ',' }, StringSplitOptions.None).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            Company? found = null;
            foreach (var part in parts.Count == 0 ? new List<string> { line } : parts)
                if ((byName.TryGetValue(part, out var n) ? n : byBrn.TryGetValue(part, out var b) ? b : null) is { } c) { found = c; break; }
            if (found is null && parts.Count > 1 && byName.TryGetValue(line, out var whole)) found = whole;
            var error = found is null ? "Company could not be found." : inTerritory.Contains(found.Id) ? "Company is already in this territory." : "";
            matches.Add((i + 1, line, found, error));
        }
        var owners = Owners(matches.Where(m => m.Company is not null).Select(m => m.Company!.Id));
        return matches.Select(m => m.Company is { } c
            ? new Match(m.Line, m.Text, c.Id, c.Name.Trim(), c.Brn, owners.TryGetValue(c.Id, out var o) ? people.NameOf(o.Owner) : "", m.Error)
            : new Match(m.Line, m.Text, null, "", "", "", m.Error)).ToList();
    }
}
