using System.Globalization;
using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>
/// MyCRM's sales activities (its Activity page and CompanyActivityDetails_PopUp), read and written in MyCRM's tables: an
/// activity with a company, its action, dates and times, owner, types, participants, attendees, prospects and projects.
/// Only the owner changes or deletes an activity (MyCRM's Enable); the Marketing role only reads. Deleted, it is inactive;
/// a type, attendee or prospect taken off keeps its row without its activity, a participant or project link is inactive.
/// Not offered (as asked): reminders and notifications, the Telemarketer's Send Email and Reminder Date, Cold Call cloning
/// and the CSST role's filtered choices.
/// </summary>
public sealed class Activities(IRecordStore store, Access access, People people, Territories territories, Projects projects, Func<DateTime> clock)
{
    public const int PageSize = 100;
    public const int MaxRows = 2000;
    /// <summary>MyCRM's company Activities list shows the activities created from 1 April 2015.</summary>
    public static readonly DateTime ListedFrom = new(2015, 4, 1);

    public sealed class ActivityInput
    {
        public long? CompanyId { get; set; }
        public string? Action { get; set; }
        public string? Remarks { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public string? Owner { get; set; }
        public bool Completed { get; set; }
        public List<string>? Types { get; set; }
        public List<string>? Participants { get; set; }
        public List<long>? Contacts { get; set; }
        public List<long>? Prospects { get; set; }
        public List<long>? Projects { get; set; }
    }

    /// <summary>The Activities page's filter: when (upcoming, today, past, all) or a month (from–to), a search, a type, whose.</summary>
    public sealed record Filter(string When = "upcoming", string? Q = null, string? Type = null, string? Owner = "team", DateTime? From = null, DateTime? To = null, int Skip = 0);

    public sealed record Row(Activity Activity, string Company, IReadOnlyList<string> Types, IReadOnlyList<string> Contacts, string OwnerName, string Status);

    public sealed record Detail(Activity Activity, string Company, IReadOnlyList<string> Types, IReadOnlyList<(string Login, string Name)> Participants,
        IReadOnlyList<(long Id, string Name)> Contacts, IReadOnlyList<(long Id, string Name)> Prospects, IReadOnlyList<(long Id, string Name)> Projects,
        string OwnerName, string Status, bool CanEdit);

    public sealed record Choices(IReadOnlyList<(string Login, string Name)> Participants, IReadOnlyList<(string Login, string Name)> Owners,
        IReadOnlyList<(long Id, string Name)> Contacts, IReadOnlyList<(long Id, string Name)> Prospects, IReadOnlyList<(long Id, string Name)> Projects, bool ProjectsShown);

    /// <summary>MyCRM's activity form shows the Project field to Manager, Admin, SalesAdmin and MD.</summary>
    public static bool SeesProjects(Actor actor) => actor.IsAdmin || CrmRoles.Is(actor.CrmRole, CrmRoles.Manager, CrmRoles.Admin, CrmRoles.SalesAdmin, CrmRoles.Md);

    /// <summary>MyCRM hides Save from the Marketing role.</summary>
    public static bool MaySave(Actor actor) => actor.IsAdmin || actor.CrmRole != CrmRoles.Marketing;

    /// <summary>Completed (MyCRM's IsVisited), Not completed (its start date has passed) or Pending.</summary>
    public static string StatusOf(Activity a, DateTime today) => a.IsVisited ? "Completed" : a.VisitDate is { } d && d.Date < today.Date ? "Not completed" : "Pending";

    private static DateTime? Day(string? text) =>
        DateTime.TryParseExact((text ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string? Time(string? text) =>
        TimeSpan.TryParseExact((text ?? "").Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out var t) ? new DateTime(1900, 1, 1).Add(t).ToString("HH:mm", CultureInfo.InvariantCulture) : null;

    private static IEnumerable<IEnumerable<object>> Chunks<T>(IEnumerable<T> values) =>
        values.Distinct().Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => (object)x.v!);

    private Dictionary<long, string> CompanyNames(IEnumerable<long> ids)
    {
        var names = new Dictionary<long, string>();
        foreach (var chunk in Chunks(ids)) foreach (var c in store.Find<Company>(Query.All().In("Id", chunk))) names[c.Id] = c.Name.Trim();
        return names;
    }

    private Dictionary<long, List<string>> TypesOf(IReadOnlyCollection<long> ids)
    {
        var types = new Dictionary<long, List<string>>();
        foreach (var chunk in Chunks(ids.Select(i => (long?)i)))
            foreach (var l in store.Find<ActivityTypeLink>(Query.All().In("CompanyActivityId", chunk)).OrderBy(l => l.Id))
                if (l.CompanyActivityId is { } a)
                {
                    if (!types.TryGetValue(a, out var list)) types[a] = list = new List<string>();
                    if (!list.Contains(l.ActivityType)) list.Add(l.ActivityType);
                }
        return types;
    }

    private Dictionary<long, List<string>> ContactsOf(IReadOnlyCollection<long> ids)
    {
        var result = new Dictionary<long, List<string>>();
        var links = new List<ActivityContact>();
        foreach (var chunk in Chunks(ids.Select(i => (long?)i))) links.AddRange(store.Find<ActivityContact>(Query.All().In("CompanyActivityId", chunk)));
        var contactIds = links.Where(l => l.ContactId is not null).Select(l => l.ContactId!.Value).ToList();
        var names = new Dictionary<long, string>();
        foreach (var chunk in Chunks(contactIds)) foreach (var c in store.Find<Contact>(Query.All().In("Id", chunk))) names[c.Id] = Companies.ContactName(c);
        foreach (var l in links.OrderBy(l => l.Id))
            if (l.CompanyActivityId is { } a && l.ContactId is { } c && names.TryGetValue(c, out var n))
            {
                if (!result.TryGetValue(a, out var list)) result[a] = list = new List<string>();
                list.Add(n);
            }
        return result;
    }

    /// <summary>The people whose activities the person sees on the Activities page: themselves ("me"), their list ("team": MyCRM's GetSub) or one of it.</summary>
    public IReadOnlyCollection<string> PeopleOf(Actor actor, string? owner)
    {
        var list = access.UserList(actor.Login, actor.CrmRole);
        return (owner ?? "team") switch
        {
            "me" => new[] { actor.Login },
            "team" or "" => list,
            var login => list.FirstOrDefault(l => Strings.SameText(l, login)) is { } one ? new[] { one } : new[] { actor.Login },
        };
    }

    /// <summary>The visit dates of the filter: from (inclusive) and before (exclusive), null for no limit (as <see cref="Dates"/>).</summary>
    private static (DateTime? From, DateTime? Before) Range(Filter f, DateTime today)
    {
        if (f.From is not null || f.To is not null) return (f.From?.Date, f.To?.Date.AddDays(1));
        return f.When switch
        {
            "today" => (today, today.AddDays(1)),
            "past" => (null, today),
            "all" => (null, null),
            _ => (today, null),
        };
    }

    private Query Dates(Query q, Filter f, DateTime today)
    {
        if (f.From is { } from) q = q.AtLeast("VisitDate", from.Date);
        if (f.To is { } to) return q.Before("VisitDate", to.Date.AddDays(1));
        if (f.From is not null) return q;
        return f.When switch
        {
            "today" => q.AtLeast("VisitDate", today).Before("VisitDate", today.AddDays(1)),
            "past" => q.Before("VisitDate", today),
            "all" => q,
            _ => q.AtLeast("VisitDate", today),
        };
    }

    /// <summary>
    /// The Activities page (MyCRM's Activity page): the active activities the chosen people own or take part in, by the
    /// filter; upcoming and today's soonest first, the others newest first. With From–To (the calendar's month) all of them.
    /// </summary>
    public (int Total, IReadOnlyList<Row> Rows) List(Actor actor, Filter f, bool all = false)
    {
        var today = clock().Date;
        var who = PeopleOf(actor, f.Owner);
        // In MyCRM, without a search or a type: the page is found, ordered and counted in SQL.
        if (store is RoutedStore { Crm: MyCrmStore crm } && crm.Available && (f.Q ?? "").Trim().Length == 0 && (f.Type ?? "").Trim().Length == 0)
        {
            var (from, before) = Range(f, today);
            var soonest = f.From is not null || f.When is "upcoming" or "today" or "" or null;
            var (total, pageIds) = f.From is not null || all
                ? crm.ActivityIds(who, from, before, soonest, 0, MaxRows)
                : crm.ActivityIds(who, from, before, soonest, Math.Max(0, f.Skip), PageSize);
            var byId = new Dictionary<long, Activity>();
            foreach (var chunk in Chunks(pageIds)) foreach (var a in store.Find<Activity>(Query.Where("IsActive", true).In("Id", chunk))) byId[a.Id] = a;
            var page = pageIds.Where(byId.ContainsKey).Select(i => byId[i]).ToList();
            return (total, Rows(page, f with { Skip = 0 }, today, all: true).Rows);
        }
        var ids = new HashSet<long>();
        foreach (var chunk in Chunks(who))
            foreach (var p in store.Find<ActivityParticipant>(Query.Where("IsActive", true).In("User", chunk)))
                if (p.CompanyEventId is { } a) ids.Add(a);
        var found = new Dictionary<long, Activity>();
        foreach (var chunk in Chunks(ids)) foreach (var a in store.Find<Activity>(Dates(Query.Where("IsActive", true).In("Id", chunk), f, today))) found[a.Id] = a;
        foreach (var chunk in Chunks(who)) foreach (var a in store.Find<Activity>(Dates(Query.Where("IsActive", true).In("Owner", chunk), f, today))) found[a.Id] = a;
        return Rows(found.Values.ToList(), f, today, all);
    }

    private (int Total, IReadOnlyList<Row> Rows) Rows(List<Activity> activities, Filter f, DateTime today, bool all = false)
    {
        var text = (f.Q ?? "").Trim();
        var type = (f.Type ?? "").Trim();
        // Without a search or a type, only the page shown needs its types and company names: a person's past can hold
        // thousands of activities. With one, they are read for all, to find the matches.
        var narrowed = text.Length > 0 || type.Length > 0;
        var types = narrowed ? TypesOf(activities.Select(a => a.Id).ToList()) : new Dictionary<long, List<string>>();
        var names = narrowed ? CompanyNames(activities.Where(a => a.CompanyId is not null).Select(a => a.CompanyId!.Value)) : new Dictionary<long, string>();
        string CompanyOf(Activity a) => a.CompanyId is { } c && names.TryGetValue(c, out var n) ? n : "";
        IReadOnlyList<string> TypeCodes(Activity a) => types.TryGetValue(a.Id, out var t) ? t : new List<string>();
        var matched = activities
            .Where(a => type.Length == 0 || TypeCodes(a).Contains(type))
            .Where(a => text.Length == 0 || new[] { CompanyOf(a), a.Remarks, ActivityActions.List.Label(a.Action) }.Concat(TypeCodes(a).Select(ActivityTypes.List.Label))
                .Any(s => s.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();
        var soonest = f.From is not null || f.When is "upcoming" or "today" or "" or null;
        matched = soonest
            ? matched.OrderBy(a => a.VisitDate ?? DateTime.MaxValue).ThenBy(a => a.StartTime, StringComparer.Ordinal).ThenBy(a => a.Id).ToList()
            : matched.OrderByDescending(a => a.VisitDate ?? DateTime.MinValue).ThenBy(a => a.StartTime, StringComparer.Ordinal).ThenByDescending(a => a.Id).ToList();
        var page = (f.From is not null || all ? matched.Take(MaxRows) : matched.Skip(Math.Max(0, f.Skip)).Take(PageSize)).ToList();
        if (!narrowed)
        {
            types = TypesOf(page.Select(a => a.Id).ToList());
            names = CompanyNames(page.Where(a => a.CompanyId is not null).Select(a => a.CompanyId!.Value));
        }
        var contacts = ContactsOf(page.Select(a => a.Id).ToList());
        var ownerName = people.NamesOf(page.Select(a => a.Owner));
        var rows = page.Select(a => new Row(a, CompanyOf(a), TypeCodes(a).Select(ActivityTypes.List.Label).ToList(),
            contacts.TryGetValue(a.Id, out var c) ? c : new List<string>(), ownerName(a.Owner), StatusOf(a, today))).ToList();
        return (matched.Count, rows);
    }

    /// <summary>Every activity of the filter for the Excel download (MyCRM's Download, CompanyActivity.xls), at most <see cref="MaxRows"/>.</summary>
    public IReadOnlyList<Row> ForDownload(Actor actor, Filter f) => List(actor, f with { Skip = 0 }, all: true).Rows;

    /// <summary>
    /// The people whose activities a company's Activities tab shows (MyCRM's CompanyActivity_List): the person's list and
    /// everyone who has owned the company (its salesperson now and its territory movements).
    /// </summary>
    private HashSet<string> CompanyWatchers(Actor actor, long companyId)
    {
        var who = new HashSet<string>(access.UserList(actor.Login, actor.CrmRole), StringComparer.OrdinalIgnoreCase);
        var owner = territories.OwnerOf(companyId);
        if (owner.Length > 0) who.Add(owner);
        foreach (var m in territories.Movements(companyId))
        {
            if (m.FromUser.Length > 0) who.Add(m.FromUser);
            if (m.ToUser.Length > 0) who.Add(m.ToUser);
        }
        return who;
    }

    /// <summary>
    /// A company's Activities tab: its active activities made from 1 April 2015 with a participant (or owner) in the person's
    /// list or among the company's owners, by action and when, as on the Activities page.
    /// </summary>
    public (int Total, IReadOnlyList<Row> Rows) ForCompany(Actor actor, long companyId, string? when, string? action, int skip)
    {
        var today = clock().Date;
        var f = new Filter(When: string.IsNullOrEmpty(when) ? "all" : when!, Skip: skip);
        var activities = store.Find<Activity>(Dates(Query.Where("CompanyId", companyId).And("IsActive", true).AtLeast("CreatedDate", ListedFrom), f, today));
        if (!string.IsNullOrEmpty(action)) activities = activities.Where(a => a.Action == action).ToList();
        var who = CompanyWatchers(actor, companyId);
        var participants = new List<ActivityParticipant>();
        foreach (var chunk in Chunks(activities.Select(a => (long?)a.Id))) participants.AddRange(store.Find<ActivityParticipant>(Query.Where("IsActive", true).In("CompanyEventId", chunk)));
        var shown = activities.Where(a => who.Contains(a.Owner) || participants.Any(p => p.CompanyEventId == a.Id && who.Contains(p.User))).ToList();
        return Rows(shown, f, today);
    }

    /// <summary>Whether the person may read the activity: they or someone in their list own it or take part in it, or they may open its company.</summary>
    public bool MaySee(Actor actor, Activity a)
    {
        var list = access.UserList(actor.Login, actor.CrmRole);
        if (list.Contains(a.Owner)) return true;
        if (store.Find<ActivityParticipant>(Query.Where("CompanyEventId", a.Id).And("IsActive", true)).Any(p => list.Contains(p.User))) return true;
        return a.CompanyId is { } c && store.Get<Company>(c) is { } company && access.CanOpen(actor, company);
    }

    public Activity? Get(long id) => store.Get<Activity>(id) is { IsActive: true } a ? a : null;

    /// <summary>An activity as its panel shows it, with whether the person may change it (its owner, not Marketing).</summary>
    public Detail? Read(Actor actor, long id)
    {
        if (Get(id) is not { } a || !MaySee(actor, a)) return null;
        var today = clock().Date;
        var company = a.CompanyId is { } c ? store.Get<Company>(c)?.Name.Trim() ?? "" : "";
        var types = TypesOf([a.Id]).TryGetValue(a.Id, out var t) ? t.Select(ActivityTypes.List.Label).ToList() : new List<string>();
        var participants = store.Find<ActivityParticipant>(Query.Where("CompanyEventId", a.Id).And("IsActive", true)).Select(p => p.User)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(l => (l, people.NameOf(l))).ToList();
        var contactIds = store.Find<ActivityContact>(Query.Where("CompanyActivityId", a.Id)).Where(l => l.ContactId is not null).Select(l => l.ContactId!.Value).Distinct().ToList();
        var contacts = contactIds.Count == 0 ? new List<(long, string)>()
            : store.Find<Contact>(Query.All().In("Id", contactIds.Cast<object>())).Select(x => (x.Id, Companies.ContactName(x))).ToList();
        var prospectIds = store.Find<ActivityProspect>(Query.Where("CompanyActivityId", a.Id)).Where(l => l.ProspectId is not null).Select(l => l.ProspectId!.Value).Distinct().ToList();
        var prospects = prospectIds.Count == 0 ? new List<(long, string)>()
            : store.Find<Prospect>(Query.All().In("Id", prospectIds.Cast<object>())).Select(x => (x.Id, ProspectName(x))).ToList();
        var projectIds = store.Find<ProjectLink>(Query.Where("CompanyActivityId", a.Id).And("IsActive", true)).Where(l => l.ProjectId is not null).Select(l => l.ProjectId!.Value).Distinct().ToList();
        var linked = projectIds.Count == 0 ? new List<(long, string)>()
            : store.Find<Project>(Query.All().In("Id", projectIds.Cast<object>())).Select(x => (x.Id, x.ProjectName)).ToList();
        return new Detail(a, company, types, participants, contacts, prospects, linked, people.NameOf(a.Owner), StatusOf(a, today),
            MaySave(actor) && actor.IsLogin(a.Owner));
    }

    public static string ProspectName(Prospect p)
    {
        var remarks = p.Remarks.Trim();
        return (remarks.Length > 80 ? remarks.Substring(0, 80) + "…" : remarks.Length > 0 ? remarks : "Prospect " + p.Id.ToString(CultureInfo.InvariantCulture))
               + (p.ProspectType.Length > 0 ? " · " + ProspectTypes.List.Label(p.ProspectType) : "");
    }

    /// <summary>A company's active prospects (MyCRM's Prospect), newest first: only read.</summary>
    public IReadOnlyList<Prospect> Prospects(long companyId) =>
        store.Find<Prospect>(Query.Where("CompanyId", companyId).And("IsActive", true)).OrderByDescending(p => p.DateCreated).ThenByDescending(p => p.Id).ToList();

    /// <summary>
    /// What the activity form offers for a company: participants (MyCRM's: the salespeople of the company's territories in
    /// the person's list, and the person), owners (the person's list), the company's contacts and prospects, and its open
    /// projects (Manager, Admin, Sales Admin and MD).
    /// </summary>
    public Choices ChoicesFor(Actor actor, long companyId)
    {
        var list = access.UserList(actor.Login, actor.CrmRole);
        var owners = list.Select(l => (l, people.NameOf(l))).OrderBy(p => Strings.SameText(p.l, actor.Login) ? 0 : 1).ThenBy(p => p.Item2, StringComparer.OrdinalIgnoreCase).ToList();
        if (companyId == 0) return new Choices(Array.Empty<(string, string)>(), owners, Array.Empty<(long, string)>(), Array.Empty<(long, string)>(), Array.Empty<(long, string)>(), SeesProjects(actor));
        var participants = new List<string> { actor.Login };
        var links = store.Find<CompanyTerritory>(Query.Where("CompanyId", companyId).And("IsActive", true));
        if (links.Count > 0)
            participants.AddRange(store.Find<Territory>(Query.Where("IsActive", true).In("Id", links.Select(l => (object)l.TerritoryId).Distinct()))
                .Select(t => t.Owner).Where(o => o.Length > 0 && list.Contains(o)));
        var contacts = ContactsOfCompany(companyId);
        return new Choices(
            participants.Distinct(StringComparer.OrdinalIgnoreCase).Select(l => (l, people.NameOf(l))).ToList(), owners, contacts,
            Prospects(companyId).Select(p => (p.Id, ProspectName(p))).ToList(),
            SeesProjects(actor) ? projects.OpenForCompany(companyId).Select(p => (p.Id, p.ProjectName)).ToList() : new List<(long, string)>(), SeesProjects(actor));
    }

    private List<(long, string)> ContactsOfCompany(long companyId)
    {
        var links = store.Find<CompanyContactLink>(Query.Where("CompanyId", companyId));
        if (links.Count == 0) return new List<(long, string)>();
        return store.Find<Contact>(Query.Where("IsActive", true).In("Id", links.Select(l => (object)l.ContactId).Distinct()))
            .Where(c => Companies.ContactName(c).Trim().Length > 0).Select(c => (c.Id, Companies.ContactName(c)))
            .OrderBy(c => c.Item2, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The companies a new activity can be for: those the person opens, by name (null: every company, found by a search).</summary>
    public IReadOnlyList<(long Id, string Name)>? CompaniesFor(Actor actor)
    {
        var ids = access.CompanyIds(actor);
        if (ids is null) return null;
        return CompanyNames(ids).Select(p => (p.Key, p.Value)).OrderBy(p => p.Value, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Companies with the text in their name or BRN, for the roles that open every company (at most 30).</summary>
    public IReadOnlyList<(long Id, string Name)> FindCompanies(Actor actor, string? q)
    {
        var text = (q ?? "").Trim();
        if (text.Length < 2) return Array.Empty<(long, string)>();
        var found = store.Find<Company>(Query.Where("IsActive", true).Search(text, "Name", "Brn")).OrderBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).Take(30);
        var mine = access.CompanyIds(actor);
        return found.Where(c => mine is null || mine.Contains(c.Id)).Select(c => (c.Id, c.Name.Trim())).ToList();
    }

    // ---------------------------------------------------------------- saving

    /// <summary>
    /// MyCRM's CompanyActivity_Edit.Save: a new activity (any role but Marketing) or the owner's own. A company, a start
    /// date, at least one type and one participant are needed (the owner is always one); no time sets the start to now and
    /// the end an hour later. The links are made, taken off (kept, without their activity, or inactive) or put back.
    /// </summary>
    public OperationResult Save(Actor actor, long id, ActivityInput input)
    {
        if (!MaySave(actor)) return OperationResult.Failure(ResultCodes.Forbidden, "The Marketing role can read activities but not save them.");
        Activity? existing = null;
        if (id != 0)
        {
            existing = Get(id);
            if (existing is null) return OperationResult.Failure(ResultCodes.NotFound, "That activity is not there.");
            if (!actor.IsLogin(existing.Owner)) return OperationResult.Failure(ResultCodes.Forbidden, $"Only the owner, {people.NameOf(existing.Owner)}, can change this activity.");
        }
        var errors = new List<FieldError>();
        var company = input.CompanyId is { } cid && cid > 0 ? store.Get<Company>(cid) : null;
        if (company is null) errors.Add(new FieldError("company", "You must select a company."));
        else if (!access.CanOpen(actor, company) && existing?.CompanyId != company.Id)
            errors.Add(new FieldError("company", "You may only log activities for the companies you can open."));
        var action = (input.Action ?? "").Trim();
        if (!ActivityActions.List.IsKnown(action) || (ActivityActions.Inactive.Contains(action) && existing?.Action != action))
            errors.Add(new FieldError("action", "Choose an action."));
        var start = Day(input.StartDate);
        var end = Day(input.EndDate) ?? start;
        if (start is null) errors.Add(new FieldError("startDate", "Enter the start date."));
        else if (end < start) errors.Add(new FieldError("endDate", "The end date cannot be before the start date."));
        var startTime = Time(input.StartTime);
        var endTime = Time(input.EndTime);
        if ((input.StartTime ?? "").Trim().Length > 0 && startTime is null) errors.Add(new FieldError("startTime", "Enter a time such as 09:30."));
        if ((input.EndTime ?? "").Trim().Length > 0 && endTime is null) errors.Add(new FieldError("endTime", "Enter a time such as 10:30."));
        var remarks = (input.Remarks ?? "").Trim();
        if (remarks.Length > 4000) errors.Add(new FieldError("remarks", "At most 4000 characters."));
        var list = access.UserList(actor.Login, actor.CrmRole);
        var owner = (input.Owner ?? "").Trim();
        if (owner.Length == 0) owner = existing?.Owner ?? actor.Login;
        else if (!list.Contains(owner) && !Strings.SameText(owner, existing?.Owner)) errors.Add(new FieldError("owner", "Choose yourself or someone in your team."));
        else owner = list.FirstOrDefault(l => Strings.SameText(l, owner)) ?? owner;
        var types = (input.Types ?? new List<string>()).Select(t => (t ?? "").Trim()).Where(t => t.Length > 0).Distinct().ToList();
        if (types.Count == 0) errors.Add(new FieldError("types", "At least one activity type must be set"));
        else if (types.Any(t => !ActivityTypes.List.IsKnown(t))) errors.Add(new FieldError("types", "Choose activity types from the list."));
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        var choices = ChoicesFor(actor, company!.Id);
        var currentParticipants = existing is null ? new List<ActivityParticipant>()
            : store.Find<ActivityParticipant>(Query.Where("CompanyEventId", existing.Id));
        var allowed = new HashSet<string>(choices.Participants.Select(p => p.Login).Concat(currentParticipants.Where(p => p.IsActive).Select(p => p.User)), StringComparer.OrdinalIgnoreCase) { owner };
        var participants = (input.Participants ?? new List<string>()).Select(p => (p ?? "").Trim()).Where(p => p.Length > 0).Append(owner)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (participants.Any(p => !allowed.Contains(p))) errors.Add(new FieldError("participants", "Choose participants from the list: the salespeople of this company in your team."));
        if (participants.Count == 0) errors.Add(new FieldError("participants", "At least one participant must be set"));
        var contacts = (input.Contacts ?? new List<long>()).Distinct().ToList();
        if (contacts.Any(c => choices.Contacts.All(x => x.Id != c))) errors.Add(new FieldError("contacts", "Choose attendees among the company's contacts."));
        var prospects = (input.Prospects ?? new List<long>()).Distinct().ToList();
        if (prospects.Any(p => choices.Prospects.All(x => x.Id != p))) errors.Add(new FieldError("prospects", "Choose prospects of this company."));
        var linkedNow = existing is null ? new List<ProjectLink>() : store.Find<ProjectLink>(Query.Where("CompanyActivityId", existing.Id));
        var wantedProjects = choices.ProjectsShown
            ? (input.Projects ?? new List<long>()).Distinct().ToList()
            : linkedNow.Where(l => l.IsActive && l.ProjectId is not null).Select(l => l.ProjectId!.Value).Distinct().ToList();
        if (choices.ProjectsShown && wantedProjects.Any(p => choices.Projects.All(x => x.Id != p) && !linkedNow.Any(l => l.IsActive && l.ProjectId == p)))
            errors.Add(new FieldError("projects", "Choose open projects of this company."));
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        var now = clock();
        var a = existing ?? new Activity { CreatedDate = now.Date, IsActive = true };
        a.CompanyId = company.Id;
        a.Action = action;
        a.Remarks = remarks;
        a.VisitDate = start;
        a.ToDate = end;
        // MyCRM: no start time is now, no end time an hour after it.
        a.StartTime = startTime ?? now.ToString("HH:mm", CultureInfo.InvariantCulture);
        a.EndTime = endTime ?? (startTime is null ? now.AddMinutes(60) : DateTime.ParseExact(startTime, "HH:mm", CultureInfo.InvariantCulture).AddMinutes(60)).ToString("HH:mm", CultureInfo.InvariantCulture);
        a.Owner = owner;
        a.IsVisited = input.Completed;
        a.IsActive = true;
        a.UpdatedBy = actor.Login;
        a.UpdatedDate = now.Date;
        store.Batch(s =>
        {
            if (existing is null) s.Insert(a);
            else s.Update(a);
            // Types: added, or taken off (the row stays without its activity).
            var typeLinks = existing is null ? new List<ActivityTypeLink>() : s.Find<ActivityTypeLink>(Query.Where("CompanyActivityId", a.Id));
            foreach (var t in types.Where(t => typeLinks.All(l => l.ActivityType != t))) s.Insert(new ActivityTypeLink { CompanyActivityId = a.Id, ActivityType = t });
            foreach (var l in typeLinks.Where(l => !types.Contains(l.ActivityType)))
            {
                l.CompanyActivityId = null;
                s.Update(l);
            }
            // Participants: added, put back or made inactive.
            foreach (var p in participants)
            {
                var rows = currentParticipants.Where(r => Strings.SameText(r.User, p)).OrderBy(r => r.Id).ToList();
                if (rows.Any(r => r.IsActive)) continue;
                if (rows.FirstOrDefault() is { } again)
                {
                    again.IsActive = true;
                    s.Update(again);
                }
                else s.Insert(new ActivityParticipant { CompanyEventId = a.Id, User = p, IsActive = true });
            }
            foreach (var r in currentParticipants.Where(r => r.IsActive && !participants.Contains(r.User, StringComparer.OrdinalIgnoreCase)))
            {
                r.IsActive = false;
                s.Update(r);
            }
            // Attendees and prospects: added, or taken off (the row stays without its activity).
            var contactLinks = existing is null ? new List<ActivityContact>() : s.Find<ActivityContact>(Query.Where("CompanyActivityId", a.Id));
            foreach (var c in contacts.Where(c => contactLinks.All(l => l.ContactId != c))) s.Insert(new ActivityContact { CompanyActivityId = a.Id, ContactId = c });
            foreach (var l in contactLinks.Where(l => l.ContactId is not { } c || !contacts.Contains(c)))
            {
                l.CompanyActivityId = null;
                s.Update(l);
            }
            var prospectLinks = existing is null ? new List<ActivityProspect>() : s.Find<ActivityProspect>(Query.Where("CompanyActivityId", a.Id));
            foreach (var p in prospects.Where(p => prospectLinks.All(l => l.ProspectId != p))) s.Insert(new ActivityProspect { CompanyActivityId = a.Id, ProspectId = p });
            foreach (var l in prospectLinks.Where(l => l.ProspectId is not { } p || !prospects.Contains(p)))
            {
                l.CompanyActivityId = null;
                s.Update(l);
            }
            // Projects: linked, put back or made inactive.
            foreach (var p in wantedProjects)
            {
                var rows = linkedNow.Where(l => l.ProjectId == p).OrderBy(l => l.Id).ToList();
                if (rows.Any(l => l.IsActive)) continue;
                if (rows.FirstOrDefault() is { } again)
                {
                    again.IsActive = true;
                    s.Update(again);
                }
                else s.Insert(new ProjectLink { CompanyActivityId = a.Id, ProjectId = p, IsActive = true });
            }
            foreach (var l in linkedNow.Where(l => l.IsActive && (l.ProjectId is not { } p || !wantedProjects.Contains(p))))
            {
                l.IsActive = false;
                s.Update(l);
            }
        });
        return OperationResult.Success($"Company Activity has been saved for {company.Name.Trim()}.", a.Id);
    }

    /// <summary>MyCRM's Delete: the owner's activity becomes inactive ("Successfully Deleted.").</summary>
    public OperationResult Delete(Actor actor, long id)
    {
        if (Get(id) is not { } a) return OperationResult.Failure(ResultCodes.NotFound, "That activity is not there.");
        if (!MaySave(actor) || !actor.IsLogin(a.Owner)) return OperationResult.Failure(ResultCodes.Forbidden, $"Only the owner, {people.NameOf(a.Owner)}, can delete this activity.");
        a.IsActive = false;
        a.UpdatedBy = actor.Login;
        a.UpdatedDate = clock().Date;
        store.Update(a);
        return OperationResult.Success("Successfully Deleted.", a.Id);
    }
}
