using System.Globalization;
using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>What a decision's record is: a prospect (its Id) or a company visited without a prospect (its Id).</summary>
public static class DormantKinds
{
    public const string Prospect = "Prospect";
    public const string Company = "Company";
}

/// <summary>What was decided about a dormant opportunity. "To follow up" is no decision: it is a row nobody decided about.</summary>
public static class DormantDecisions
{
    public const string ToFollowUp = "ToFollowUp";
    public const string FollowedUp = "FollowedUp";
    public const string NoOpportunity = "NoOpportunity";
    public const string Snoozed = "Snoozed";

    public static readonly ChoiceList List = new("DormantDecision",
        new Choice(FollowedUp, "Followed up"), new Choice(NoOpportunity, "No opportunity"), new Choice(Snoozed, "Snoozed"));
}

/// <summary>
/// Dormant opportunities: old prospects that were lost or left open, and companies visited with no prospect made, in the
/// territories of the person (or of their team, by MyCRM's GetSub), worth a second look. MyCRM's prospects and activities
/// are only read; what people decide is kept in MyCRMRecords' own table (<see cref="DormantDecision"/>). Following up is
/// done in D365: this page only lists, ranks and records the decision.
/// </summary>
public sealed class Dormant(IRecordStore store, Access access, People people, Func<DateTime> clock)
{
    public const int PageSize = 100;
    public const int MaxReason = 500;
    /// <summary>An open prospect with no activity for this many days is dormant (a lost one always is).</summary>
    public const int StalledDays = 90;
    public const string Prospects = "prospects";
    public const string Visited = "visited";
    public const string High = "High";
    public const string Medium = "Medium";
    public const string Low = "Low";

    /// <summary>The page's filters. Decision: todo (default), followedup, noopportunity or snoozed. Reached: the least activity rank (0 for any).</summary>
    public sealed class Filter
    {
        public string? List { get; set; }
        public string? Owner { get; set; }
        public long Territory { get; set; }
        public int Reached { get; set; }
        public int LookBackYears { get; set; }
        public string? Type { get; set; }
        public string? Decision { get; set; }
        public string? Q { get; set; }
        public int Skip { get; set; }
    }

    public sealed class Row
    {
        public string Kind { get; set; } = "";
        public long RecordId { get; set; }
        public long CompanyId { get; set; }
        public string Company { get; set; } = "";
        public string Brn { get; set; } = "";
        public long TerritoryId { get; set; }
        public string Territory { get; set; } = "";
        public string OwnerLogin { get; set; } = "";
        public string Owner { get; set; } = "";
        public string Priority { get; set; } = Low;
        public int PriorityRank { get; set; }
        public string Why { get; set; } = "";
        public int FurthestRank { get; set; }
        public string Furthest { get; set; } = "";
        public string ProspectType { get; set; } = "";
        public string ProspectTypeCode { get; set; } = "";
        public string Status { get; set; } = "";
        public decimal Value { get; set; }
        public DateTime? ClosedOn { get; set; }
        public DateTime? LastActivity { get; set; }
        public int Activities { get; set; }
        /// <summary>The date the look-back filter and the order use.</summary>
        public DateTime? When { get; set; }
        public string State { get; set; } = DormantDecisions.ToFollowUp;
        public string Reason { get; set; } = "";
        public DateTime? SnoozeUntil { get; set; }
        public string DecidedBy { get; set; } = "";
        public DateTime? DecidedAt { get; set; }
        /// <summary>The company's activities that count (newest first): their date and the furthest type of each.</summary>
        public List<(DateTime? Date, string Label)> History { get; set; } = new();
    }

    public sealed record Counts(int ProspectsToFollowUp, int VisitedToFollowUp, int ToFollowUp, int FollowedUp, int NoOpportunity, int Snoozed, decimal ValueToFollowUp);

    public sealed record Page(int Total, IReadOnlyList<Row> Rows, Counts Counts);

    private sealed record Held(long CompanyId, string Name, string Brn, string OwnerLogin, long TerritoryId, string Territory);

    private sealed record Seen(DateTime? Date, int Rank, string Label);

    private static IEnumerable<IEnumerable<object>> Chunks<T>(IEnumerable<T> values) =>
        values.Distinct().Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => (object)x.v!);

    /// <summary>How far an activity type goes towards a sale (0: it says nothing about a deal).</summary>
    public static int ActivityRank(string type) => type switch
    {
        "NewCall" => 1,
        "FirstAppointment" or "FirstAppointmentAttended" or "FollowUp" => 2,
        "File_MediaTest" => 3,
        "Demo_POC" => 4,
        "FirstProposalPresented" => 5,
        "Negotiation" => 6,
        _ => 0,
    };

    private static int StageRank(string stage) => stage switch
    {
        "Plan" or "Create" => 1,
        "Qualify" => 2,
        "Develop" => 3,
        "Prove" => 4,
        "Negotiation" => 6,
        _ => 0,
    };

    private static string PriorityOf(int rank) => rank >= 5 ? High : rank >= 3 ? Medium : Low;

    public static bool IsLost(Prospect p) => p.ProspectStatus == "Lost" || p.Stage == "CloseLost";

    /// <summary>Still being worked: not closed, cancelled or lost, and not on its way to an order.</summary>
    private static bool IsOpen(Prospect p) =>
        !p.IsClosed && !p.IsCancelled && !IsLost(p) && p.Stage != "Close" && p.Stage != "Implementation" && (p.ProspectStatus.Length == 0 || p.ProspectStatus == "InProgress");

    private static bool Newer(Prospect q, Prospect p) =>
        q.DateCreated is { } a && p.DateCreated is { } b ? a > b : q.Id > p.Id;

    private static string Month(DateTime d) => d.ToString("MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>The companies (active, in the territories of the chosen people) with who has them now: the territory's owner.</summary>
    private Dictionary<long, Held> Territories(Actor actor, string? owner, long territory)
    {
        var list = access.UserList(actor.Login, actor.CrmRole);
        IReadOnlyCollection<string> who = (owner ?? "team") switch
        {
            "me" => new[] { actor.Login },
            "team" or "" => list,
            var login => list.FirstOrDefault(l => Strings.SameText(l, login)) is { } one ? new[] { one } : new[] { actor.Login },
        };
        var result = new Dictionary<long, Held>();
        var territories = store.Find<Territory>(Query.Where("IsActive", true).In("Owner", who.Cast<object>()));
        if (territory > 0) territories = territories.Where(t => t.Id == territory).ToList();
        if (territories.Count == 0) return result;
        var ordered = territories.OrderBy(t => Strings.SameText(t.Owner, actor.Login) ? 0 : 1).ThenBy(t => t.Id).ToList();
        var links = store.Find<CompanyTerritory>(Query.Where("IsActive", true).In("TerritoryId", ordered.Select(t => (object)t.Id)));
        var companyOf = new Dictionary<long, Territory>();
        foreach (var t in ordered)
            foreach (var link in links.Where(l => l.TerritoryId == t.Id))
                if (!companyOf.ContainsKey(link.CompanyId)) companyOf[link.CompanyId] = t;
        foreach (var chunk in Chunks(companyOf.Keys))
            foreach (var c in store.Find<Company>(Query.Where("IsActive", true).In("Id", chunk)))
            {
                var t = companyOf[c.Id];
                var name = t.Name.Trim().Length > 0 ? t.Name.Trim() : "Territory " + t.Number.ToString(CultureInfo.InvariantCulture);
                result[c.Id] = new Held(c.Id, c.Name.Trim(), c.Brn.Trim(), t.Owner, t.Id, name);
            }
        return result;
    }

    /// <summary>The territories the person's list holds (for the Territory filter): Id and name, by name.</summary>
    public IReadOnlyList<(long Id, string Name)> TerritoryChoices(Actor actor, string? owner)
    {
        var held = Territories(actor, owner, 0);
        return held.Values.GroupBy(h => h.TerritoryId).Select(g => (g.Key, g.First().Territory)).OrderBy(t => t.Item2, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The people whose territories the page can show: the person and, by their role, their team.</summary>
    public IReadOnlyList<(string Login, string Name)> Team(Actor actor)
    {
        var list = access.UserList(actor.Login, actor.CrmRole);
        return list.Where(l => !Strings.SameText(l, actor.Login)).Select(l => (l, people.NameOf(l))).OrderBy(p => p.Item2, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private Dictionary<long, DormantDecision> Decisions(string kind, IEnumerable<long> ids)
    {
        var result = new Dictionary<long, DormantDecision>();
        foreach (var chunk in Chunks(ids))
            foreach (var d in store.Find<DormantDecision>(Query.Where("Kind", kind).And("IsActive", true).In("RecordId", chunk)).OrderBy(d => d.Id))
                result[d.RecordId] = d;
        return result;
    }

    private static void Decide(Row row, DormantDecision? d, DateTime today, Func<string, string> nameOf)
    {
        if (d is null) return;
        row.Reason = d.Reason;
        row.DecidedBy = d.DecidedByName.Length > 0 ? d.DecidedByName : nameOf(d.DecidedBy);
        row.DecidedAt = d.DecidedAt;
        row.SnoozeUntil = d.SnoozeUntil;
        row.State = d.Decision switch
        {
            DormantDecisions.FollowedUp => DormantDecisions.FollowedUp,
            DormantDecisions.NoOpportunity => DormantDecisions.NoOpportunity,
            DormantDecisions.Snoozed when d.SnoozeUntil is { } until && until.Date > today => DormantDecisions.Snoozed,
            _ => DormantDecisions.ToFollowUp,
        };
    }

    /// <summary>
    /// Both lists for the territories of the chosen people, with what was decided, before any filter: the prospects lost or
    /// left open for <see cref="StalledDays"/> days (not those the company has a newer prospect after), and the companies
    /// visited beyond a first call with no prospect at all. Best first.
    /// </summary>
    private (List<Row> Prospects, List<Row> Visited) Build(Actor actor, string? owner, long territory)
    {
        var today = clock().Date;
        var held = Territories(actor, owner, territory);
        var prospectRows = new List<Row>();
        var visitedRows = new List<Row>();
        if (held.Count == 0) return (prospectRows, visitedRows);

        var prospects = new List<Prospect>();
        var activities = new List<Activity>();
        foreach (var chunk in Chunks(held.Keys))
        {
            prospects.AddRange(store.Find<Prospect>(Query.Where("IsActive", true).In("CompanyId", chunk)));
            activities.AddRange(store.Find<Activity>(Query.Where("IsActive", true).In("CompanyId", chunk)));
        }
        var typesOf = new Dictionary<long, List<string>>();
        foreach (var chunk in Chunks(activities.Select(a => a.Id)))
            foreach (var t in store.Find<ActivityTypeLink>(Query.All().In("CompanyActivityId", chunk)))
                if (t.CompanyActivityId is { } id)
                {
                    if (!typesOf.TryGetValue(id, out var list)) typesOf[id] = list = new List<string>();
                    list.Add(t.ActivityType);
                }
        var seen = new Dictionary<long, List<Seen>>();
        foreach (var a in activities)
        {
            if (a.CompanyId is not { } company) continue;
            var types = typesOf.TryGetValue(a.Id, out var found) ? found : new List<string>();
            var best = types.OrderByDescending(ActivityRank).FirstOrDefault() ?? "";
            if (!seen.TryGetValue(company, out var list)) seen[company] = list = new List<Seen>();
            list.Add(new Seen(a.VisitDate, ActivityRank(best), best.Length > 0 ? ActivityTypes.List.Label(best) : "Activity"));
        }
        Seen[] Of(long company) => seen.TryGetValue(company, out var l) ? l.OrderByDescending(s => s.Date ?? DateTime.MinValue).ToArray() : Array.Empty<Seen>();

        var byCompany = prospects.Where(p => !p.IsCancelled).GroupBy(p => p.CompanyId).ToDictionary(g => g.Key, g => g.ToList());
        string NameOf(string login) => login.Length == 0 ? "" : people.NameOf(login);
        void Fill(Row row, Held h, Seen[] own)
        {
            row.CompanyId = h.CompanyId;
            row.Company = h.Name;
            row.Brn = h.Brn;
            row.TerritoryId = h.TerritoryId;
            row.Territory = h.Territory;
            row.OwnerLogin = h.OwnerLogin;
            row.Owner = NameOf(h.OwnerLogin);
            row.Activities = own.Length;
            row.LastActivity = own.Select(s => s.Date).Where(d => d is not null).DefaultIfEmpty(null).Max();
            var furthest = own.OrderByDescending(s => s.Rank).ThenByDescending(s => s.Date ?? DateTime.MinValue).FirstOrDefault();
            row.FurthestRank = furthest?.Rank ?? 0;
            row.Furthest = furthest is { Rank: > 0 } ? furthest.Label : "";
            row.History = own.Select(s => (s.Date, s.Label)).ToList();
        }

        foreach (var p in prospects.Where(p => !p.IsCancelled))
        {
            if (!held.TryGetValue(p.CompanyId, out var h)) continue;
            var lost = IsLost(p);
            if (!lost && !IsOpen(p)) continue;
            if (byCompany[p.CompanyId].Any(q => q.Id != p.Id && !IsLost(q) && Newer(q, p))) continue;
            var own = Of(p.CompanyId);
            var last = own.Select(s => s.Date).Where(d => d is not null).DefaultIfEmpty(null).Max();
            var quiet = last ?? p.DateCreated;
            if (!lost && quiet is { } q && q.Date >= today.AddDays(-StalledDays)) continue;
            var row = new Row { Kind = DormantKinds.Prospect, RecordId = p.Id };
            Fill(row, h, own);
            row.ProspectTypeCode = p.ProspectType;
            row.ProspectType = ProspectTypes.List.Label(p.ProspectType);
            row.Status = lost ? "Lost" : ProspectStages.List.Label(p.Stage);
            row.Value = p.TotalPrice > 0 ? p.TotalPrice : p.Forecast;
            row.ClosedOn = lost ? p.DateClosed ?? p.UpdatedDate ?? p.DateCreated : null;
            row.When = lost ? row.ClosedOn : last ?? p.DateCreated;
            row.PriorityRank = Math.Max(lost ? 0 : StageRank(p.Stage), row.FurthestRank);
            row.Priority = PriorityOf(row.PriorityRank);
            row.Why = lost
                ? "Lost" + (row.ProspectType.Length > 0 ? ", type " + row.ProspectType : "") + (row.ClosedOn is { } closed ? ", " + Month(closed) : "")
                : quiet is { } since ? "Open and untouched since " + Month(since) : "Open, no activity recorded";
            prospectRows.Add(row);
        }

        // Visited beyond a first call, and no prospect at all (cancelled ones count: someone made one).
        var withProspect = new HashSet<long>(prospects.Select(p => p.CompanyId));
        foreach (var h in held.Values)
        {
            if (withProspect.Contains(h.CompanyId)) continue;
            var own = Of(h.CompanyId);
            if (own.Length == 0 || own.Max(s => s.Rank) < 2) continue;
            var row = new Row { Kind = DormantKinds.Company, RecordId = h.CompanyId };
            Fill(row, h, own);
            row.When = row.LastActivity;
            row.PriorityRank = row.FurthestRank;
            row.Priority = PriorityOf(row.PriorityRank);
            row.Why = row.Furthest + ", no prospect created";
            visitedRows.Add(row);
        }

        var prospectDecisions = Decisions(DormantKinds.Prospect, prospectRows.Select(r => r.RecordId));
        foreach (var r in prospectRows) Decide(r, prospectDecisions.TryGetValue(r.RecordId, out var d) ? d : null, today, NameOf);
        var companyDecisions = Decisions(DormantKinds.Company, visitedRows.Select(r => r.RecordId));
        foreach (var r in visitedRows) Decide(r, companyDecisions.TryGetValue(r.RecordId, out var d) ? d : null, today, NameOf);
        return (Order(prospectRows), Order(visitedRows));
    }

    private static List<Row> Order(IEnumerable<Row> rows) =>
        rows.OrderByDescending(r => r.PriorityRank >= 5 ? 3 : r.PriorityRank >= 3 ? 2 : 1).ThenByDescending(r => r.Value)
            .ThenByDescending(r => r.When ?? DateTime.MinValue).ThenBy(r => r.Company, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.RecordId).ToList();

    private List<Row> Apply(List<Row> rows, Filter f)
    {
        var today = clock().Date;
        var text = (f.Q ?? "").Trim();
        var type = (f.Type ?? "").Trim();
        return rows
            .Where(r => f.Reached <= 0 || r.FurthestRank >= f.Reached)
            .Where(r => f.LookBackYears <= 0 || (r.When is { } w && w.Date >= today.AddYears(-f.LookBackYears)))
            .Where(r => type.Length == 0 || r.ProspectTypeCode == type)
            .Where(r => text.Length == 0 || new[] { r.Company, r.Brn, r.Why, r.Territory }.Any(s => s.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();
    }

    private static string WantedState(string? decision) => (decision ?? "").Trim().ToLowerInvariant() switch
    {
        "followedup" => DormantDecisions.FollowedUp,
        "noopportunity" => DormantDecisions.NoOpportunity,
        "snoozed" => DormantDecisions.Snoozed,
        _ => DormantDecisions.ToFollowUp,
    };

    /// <summary>The page: the chosen list by the filters, with the counts of each decision (and of both lists) beside it.</summary>
    public Page List(Actor actor, Filter f)
    {
        var (prospects, visited) = Build(actor, f.Owner, f.Territory);
        prospects = Apply(prospects, f);
        visited = Apply(visited, f);
        var list = f.List == Visited ? visited : prospects;
        var counts = new Counts(
            prospects.Count(r => r.State == DormantDecisions.ToFollowUp), visited.Count(r => r.State == DormantDecisions.ToFollowUp),
            list.Count(r => r.State == DormantDecisions.ToFollowUp), list.Count(r => r.State == DormantDecisions.FollowedUp),
            list.Count(r => r.State == DormantDecisions.NoOpportunity), list.Count(r => r.State == DormantDecisions.Snoozed),
            list.Where(r => r.State == DormantDecisions.ToFollowUp).Sum(r => r.Value));
        var wanted = WantedState(f.Decision);
        var shown = list.Where(r => r.State == wanted).ToList();
        return new Page(shown.Count, shown.Skip(Math.Max(0, f.Skip)).Take(PageSize).ToList(), counts);
    }

    /// <summary>Everything in the person's team's territories, whatever the page's filters (the Excel workbook): both lists, best first.</summary>
    public (IReadOnlyList<Row> Prospects, IReadOnlyList<Row> Visited) ForDownload(Actor actor)
    {
        var (prospects, visited) = Build(actor, "team", 0);
        return (prospects, visited);
    }

    public sealed class DecisionInput
    {
        public string? Kind { get; set; }
        public long RecordId { get; set; }
        public string? Decision { get; set; }
        public string? Reason { get; set; }
        public string? SnoozeUntil { get; set; }
    }

    /// <summary>
    /// Records what the person decided about a dormant opportunity of a company in their territories (or their team's). The
    /// decision before it is marked inactive, never deleted. The Marketing role reads but does not decide.
    /// </summary>
    public OperationResult Decide(Actor actor, DecisionInput input)
    {
        if (!Activities.MaySave(actor)) return OperationResult.Failure(ResultCodes.Forbidden, "The Marketing role can read this page but not record decisions.");
        var kind = (input.Kind ?? "").Trim();
        var decision = (input.Decision ?? "").Trim();
        if (kind != DormantKinds.Prospect && kind != DormantKinds.Company) return OperationResult.Failure(ResultCodes.Validation, "That opportunity is not one of the lists.");
        if (!DormantDecisions.List.IsKnown(decision)) return OperationResult.Failure(ResultCodes.Validation, "Choose Followed up, No opportunity or Snooze.");
        var errors = new List<FieldError>();
        var reason = (input.Reason ?? "").Trim();
        DateTime? until = null;
        if (decision == DormantDecisions.NoOpportunity && reason.Length == 0) errors.Add(new FieldError("reason", "Say why there is no opportunity."));
        else if (reason.Length > MaxReason) errors.Add(new FieldError("reason", "Keep the reason to " + MaxReason + " characters."));
        if (decision != DormantDecisions.NoOpportunity) reason = "";
        if (decision == DormantDecisions.Snoozed)
        {
            if (DateTime.TryParseExact((input.SnoozeUntil ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day.Date > clock().Date) until = day.Date;
            else errors.Add(new FieldError("snoozeUntil", "Choose a day after today."));
        }
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        long companyId;
        if (kind == DormantKinds.Prospect)
        {
            if (store.Get<Prospect>(input.RecordId) is not { IsActive: true } prospect) return OperationResult.Failure(ResultCodes.NotFound, "That prospect is not there.");
            companyId = prospect.CompanyId;
        }
        else companyId = input.RecordId;
        if (!access.OwnCompanyIds(actor).Contains(companyId)) return OperationResult.Failure(ResultCodes.Forbidden, "That company is not in your territories.");

        var now = clock();
        store.Batch(s =>
        {
            foreach (var old in s.Find<DormantDecision>(Query.Where("Kind", kind).And("RecordId", input.RecordId).And("IsActive", true)))
            {
                old.IsActive = false;
                s.Update(old);
            }
            s.Insert(new DormantDecision
            {
                Kind = kind, RecordId = input.RecordId, CompanyId = companyId, Decision = decision, Reason = reason, SnoozeUntil = until,
                IsActive = true, DecidedBy = actor.Login, DecidedByName = actor.ChangedBy, DecidedAt = now,
            });
        });
        return OperationResult.Success(decision switch
        {
            DormantDecisions.FollowedUp => "Marked as followed up.",
            DormantDecisions.NoOpportunity => "Marked as no opportunity.",
            _ => "Snoozed until " + until!.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture) + ".",
        }, input.RecordId);
    }
}
