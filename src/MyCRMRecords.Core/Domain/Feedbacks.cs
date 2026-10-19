using System.Globalization;
using MyCRMRecords.Data;
using MyCRMRecords.Integration;

namespace MyCRMRecords.Domain;

/// <summary>What the person may do with a feedback (MyCRM's FeedbackDetail conditions), worked out on the server.</summary>
public sealed record FeedbackRights
{
    public bool IsNew { get; init; }
    /// <summary>The person is in charge of the feedback (MyCRM's IsInCharge).</summary>
    public bool InCharge { get; init; }
    public bool NotClosed { get; init; }
    /// <summary>Feedback Category: always (as in MyCRM).</summary>
    public bool EditGroup { get; init; }
    /// <summary>The first sections (division, in charge, customer details, problem, ESS): a new feedback or a New one (MyCRM's EnableNewTabs).</summary>
    public bool EditNew { get; init; }
    /// <summary>HR - ESS: Pending HR and the person has HR (MyCRM's EnableHRTab).</summary>
    public bool EditHr { get; init; }
    /// <summary>Is Acknowledged: in charge, not closed, not Acknowledged (MyCRM's EnableAcknowledegeTab).</summary>
    public bool EditAcknowledge { get; init; }
    /// <summary>The investigation and resolution sections are shown (not a Feedback / Improvement).</summary>
    public bool ShowsResolution { get; init; }
    /// <summary>First Look At Feedback and Root Cause: in charge and not closed.</summary>
    public bool EditInvestigation { get; init; }
    /// <summary>The satisfaction level: Feedback Admins, not closed.</summary>
    public bool EditSatisfaction { get; init; }
    public bool AddCorrectiveTask { get; init; }
    public bool AddPreventiveTask { get; init; }
    /// <summary>The Customer Details files: Feedback Admins (MyCRM's CheckAdminRole), once saved.</summary>
    public bool CustomerFiles { get; init; }
    /// <summary>The satisfaction files: everyone, once saved.</summary>
    public bool SatisfactionFiles { get; init; }
    public bool CloseCase { get; init; }
    public bool Delete { get; init; }
}

/// <summary>What the list shows: the feedbacks found, and how many there are of each status (and in all).</summary>
public sealed record FeedbackList(IReadOnlyList<Feedback> Rows, IReadOnlyDictionary<string, int> Counts, int All);

/// <summary>
/// The feedbacks (MyCRM's Feedbacks and FeedbackDetail, CreateOrUpdateFeedback_, StatusHandler, UpdateTiming and the
/// task and attachment popups). Who may change what is checked here, for every change, as MyCRM's screens decided it.
/// Nothing is deleted: a feedback, a task or a file taken away is marked inactive.
/// </summary>
public sealed class Feedbacks(IRecordStore store, People people, Mailer mailer, Func<BusinessDays> businessDays, Func<CrmSettings> settings, Func<DateTime> clock)
{
    public const string RequestKind = "Feedback";

    /// <summary>What MyCRM's Save shows when a required field is empty.</summary>
    public const string Required = "Required field!";

    public sealed class FeedbackInput
    {
        public string? FeedbackGroup { get; set; }
        public long? DivisionId { get; set; }
        public string? PersonResponsible { get; set; }
        public string? CompanyName { get; set; }
        public string? ContactName { get; set; }
        public string? CompanyEmail { get; set; }
        public long? MediumId { get; set; }
        public DateTime? DateReceived { get; set; }
        public string? TimeReceived { get; set; }
        public string? FeedbackType { get; set; }
        public long? CategoryId { get; set; }
        public string? Description { get; set; }
        public List<string>? Cc { get; set; }
        public bool MightCauseCustomerComplaint { get; set; }
        public bool CreatorHasSolution { get; set; }
        public string? SolutionDetail { get; set; }
        public string? ExpectedOutcome { get; set; }
        public List<string>? SuggestionMeetCriteria { get; set; }
        public List<string>? RicohWayValue { get; set; }
        public string? HrAccepted { get; set; }
        public string? HrRemarks { get; set; }
        public bool IsAcknowledged { get; set; }
        public string? FirstLookAtFeedback { get; set; }
        public string? RootCause { get; set; }
        public string? SatisfactionLevel { get; set; }
    }

    public sealed class TaskInput
    {
        public string? TaskType { get; set; }
        public string? ActionPlan { get; set; }
        public string? Remarks { get; set; }
        public string? PersonInCharge { get; set; }
        public DateTime? DueDate { get; set; }
        public bool IsCompleted { get; set; }
    }

    /// <summary>The list's search and filters (MyCRM: a keyword, the status counters, the division column's combo).</summary>
    public sealed record ListFilter(string Search = "", string Status = "", string Division = "");

    // ---------------------------------------------------------------- reading

    public Feedback? Get(long id) => id == 0 ? null : store.Get<Feedback>(id) is { IsActive: true } f ? f : null;

    public IReadOnlyList<FeedbackTask> Tasks(long feedbackId) =>
        store.Find<FeedbackTask>(Query.Where("FeedbackId", feedbackId).And("IsActive", true)).OrderBy(t => t.Id).ToList();

    public IReadOnlyList<FeedbackFile> Files(long feedbackId) =>
        store.Find<FeedbackFile>(Query.Where("FeedbackId", feedbackId).And("IsActive", true)).OrderByDescending(f => f.Id).ToList();

    public FeedbackFile? File(long id) => id == 0 ? null : store.Get<FeedbackFile>(id) is { IsActive: true } file && Get(file.FeedbackId) is not null ? file : null;

    public IReadOnlyList<FeedbackDivision> Divisions() => store.Find<FeedbackDivision>(Query.Where("IsActive", true)).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<FeedbackCategory> Categories() =>
        store.Find<FeedbackCategory>(Query.Where("Removed", false)).OrderBy(c => c.SortOrder).ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<FeedbackMedium> Mediums() =>
        store.Find<FeedbackMedium>(Query.Where("Removed", false)).OrderBy(c => c.SortOrder).ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The division's name: MyCRM's old text when there is one, else the division chosen (fix: MyCRM read only the text).</summary>
    public static string DivisionOf(Feedback f, IReadOnlyDictionary<long, string> divisions) =>
        f.PersonResponsibleDivision.Trim().Length > 0 ? f.PersonResponsibleDivision.Trim()
        : f.DivisionId is { } id && divisions.TryGetValue(id, out var name) ? name : "";

    /// <summary>Every division's name by Id, those taken away too (old feedbacks keep theirs).</summary>
    public IReadOnlyDictionary<long, string> DivisionNames() => store.Find<FeedbackDivision>().ToDictionary(d => d.Id, d => d.Name);

    /// <summary>
    /// Whether the person sees the feedback in their list (MyCRM: created it, in charge, staff responsible, signed off, in
    /// Copy To, or a task's person in charge; every one for Feedback Admins; and here, those waiting for HR for HR).
    /// </summary>
    public static bool Sees(Actor actor, Feedback f, bool taskPerson)
    {
        if (!f.IsActive) return false;
        if (actor.IsFeedbackAdmin) return true;
        if (actor.IsHr && f.Status == FeedbackStatuses.PendingHr) return true;
        return actor.IsLogin(f.CreatedBy) || actor.IsLogin(f.PersonResponsible) || actor.IsLogin(f.StaffResponsibleForProblem)
               || actor.IsLogin(f.StaffResponsibleForSolution) || actor.IsLogin(f.SignOff) || taskPerson
               || (actor.Email.Length > 0 && f.Cc.IndexOf(actor.Email, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public FeedbackList List(Actor actor, ListFilter filter)
    {
        var divisions = DivisionNames();
        // The feedbacks where the person is a task's person in charge.
        var mine = new HashSet<long>(store.Find<FeedbackTask>(Query.Where("IsActive", true).And("PersonInCharge", actor.Login)).Select(t => t.FeedbackId));
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Name(string login)
        {
            if (!names.TryGetValue(login, out var name)) names[login] = name = people.NameOf(login);
            return name;
        }
        var search = (filter.Search ?? "").Trim();
        bool Has(string text) => text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        var division = (filter.Division ?? "").Trim();
        var found = store.Find<Feedback>(Query.Where("IsActive", true))
            .Where(f => Sees(actor, f, mine.Contains(f.Id)))
            .Where(f => search.Length == 0 || f.Id.ToString(CultureInfo.InvariantCulture) == search.TrimStart('#') || Has(DivisionOf(f, divisions))
                        || Has(FeedbackStatuses.List.Label(f.Status)) || Has(f.CreatedByUserName) || Has(Name(f.PersonResponsible)) || Has(f.CompanyName)
                        || Has(f.CompanyAddress) || Has(f.ContactName))
            .Where(f => division.Length == 0 || string.Equals(DivisionOf(f, divisions), division, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Id).ToList();
        var counts = FeedbackStatuses.List.All.ToDictionary(s => s.Code, s => found.Count(f => f.Status == s.Code));
        var status = (filter.Status ?? "").Trim();
        var rows = status.Length == 0 ? found : found.Where(f => f.Status == status).ToList();
        return new FeedbackList(rows, counts, found.Count);
    }

    /// <summary>The divisions the list's division filter offers: those of the feedbacks the person sees.</summary>
    public IReadOnlyList<string> DivisionChoices(IEnumerable<Feedback> feedbacks)
    {
        var divisions = DivisionNames();
        return feedbacks.Select(f => DivisionOf(f, divisions)).Where(d => d.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public FeedbackRights Rights(Actor actor, Feedback f)
    {
        var isNew = f.Id == 0;
        var notClosed = f.Status != FeedbackStatuses.Closed;
        var inCharge = !isNew && actor.IsLogin(f.PersonResponsible);
        var resolution = f.FeedbackType != FeedbackTypes.Improvement;
        bool TaskPerson(string type) => !isNew && Tasks(f.Id).Any(t => t.TaskType == type && actor.IsLogin(t.PersonInCharge));
        return new FeedbackRights
        {
            IsNew = isNew,
            InCharge = inCharge,
            NotClosed = notClosed,
            EditGroup = true,
            EditNew = isNew || f.Status == FeedbackStatuses.New,
            EditHr = !isNew && f.Status == FeedbackStatuses.PendingHr && actor.IsHr,
            EditAcknowledge = inCharge && notClosed && f.Status != FeedbackStatuses.Acknowledged,
            ShowsResolution = resolution,
            EditInvestigation = inCharge && notClosed && resolution,
            EditSatisfaction = !isNew && notClosed && resolution && actor.IsFeedbackAdmin,
            AddCorrectiveTask = !isNew && notClosed && resolution && (inCharge || TaskPerson(TaskTypes.Corrective)),
            AddPreventiveTask = !isNew && notClosed && resolution && (inCharge || TaskPerson(TaskTypes.Preventive)),
            CustomerFiles = !isNew && actor.IsFeedbackAdmin,
            SatisfactionFiles = !isNew && resolution,
            CloseCase = !isNew && actor.IsBe && f.Status == FeedbackStatuses.ConfirmedSatisfaction,
            Delete = !isNew && actor.IsBe,
        };
    }

    /// <summary>When the KPIs count from: Date and Time Received, or (none received) when the feedback was created.</summary>
    public static DateTime? ReceivedAt(Feedback f) => TimesOfDay.At(f.DateReceived, f.TimeReceived) ?? f.CreatedOn;

    /// <summary>The day the KPIs count days from: Date Received, or (none) the day it was created.</summary>
    public static DateTime? ReceivedOn(Feedback f) => f.DateReceived?.Date ?? f.CreatedOn?.Date;

    /// <summary>The task's "SLA Expected Completion": 10 working days after Date Received (MyCRM's GetSLAExpectedCompleteDate).</summary>
    public DateTime? SlaExpectedCompletion(Feedback f) => ReceivedOn(f) is { } received ? businessDays().Add(received, 10) : null;

    // ---------------------------------------------------------------- saving a feedback

    /// <summary>MyCRM's Save of FeedbackDetail: the checks in its order, then CreateOrUpdateFeedback_.</summary>
    public OperationResult Save(Actor actor, long id, FeedbackInput input)
    {
        var isNew = id == 0;
        var f = isNew ? new Feedback { CreatedBy = actor.Login } : Get(id);
        if (f is null) return OperationResult.Failure(ResultCodes.NotFound, "That feedback is not there.");
        var r = Rights(actor, f);
        var errors = new List<FieldError>();
        var today = clock().Date;

        if (r.EditGroup)
        {
            var group = (input.FeedbackGroup ?? "").Trim();
            if (group.Length > 0 && !FeedbackGroups.List.IsKnown(group)) errors.Add(new FieldError("feedbackGroup", "Choose a feedback category."));
            else f.FeedbackGroup = group;
        }
        if (r.EditNew)
        {
            if (input.DivisionId is { } divisionId && divisionId != 0)
            {
                if (store.Get<FeedbackDivision>(divisionId) is not { } division || (!division.IsActive && f.DivisionId != divisionId))
                    errors.Add(new FieldError("divisionId", "Choose a division from the list."));
                else f.DivisionId = divisionId;
            }
            else f.DivisionId = null;
            f.PersonResponsible = Strings.Shorten((input.PersonResponsible ?? "").Trim(), 256);
            f.CompanyName = Strings.Shorten((input.CompanyName ?? "").Trim(), 500);
            f.ContactName = Strings.Shorten((input.ContactName ?? "").Trim(), 50);
            f.CompanyEmail = Strings.Shorten((input.CompanyEmail ?? "").Trim(), 250);
            if (input.MediumId is { } mediumId && mediumId != 0)
            {
                if (store.Get<FeedbackMedium>(mediumId) is not { Removed: false } medium || (!medium.IsActive && f.MediumId != mediumId))
                    errors.Add(new FieldError("mediumId", "Choose a medium from the list."));
                else f.MediumId = mediumId;
            }
            else f.MediumId = null;
            f.DateReceived = input.DateReceived?.Date;
            var time = (input.TimeReceived ?? "").Trim();
            f.TimeReceived = TimesOfDay.Normalise(time);
            if (time.Length > 0 && f.TimeReceived.Length == 0) errors.Add(new FieldError("timeReceived", "Invalid Time"));
            var type = (input.FeedbackType ?? "").Trim();
            if (type.Length > 0 && !FeedbackTypes.List.IsKnown(type)) errors.Add(new FieldError("feedbackType", "Choose a feedback type."));
            else f.FeedbackType = type;
            if (input.CategoryId is { } categoryId && categoryId != 0)
            {
                if (store.Get<FeedbackCategory>(categoryId) is not { Removed: false } category || (!category.IsActive && f.CategoryId != categoryId))
                    errors.Add(new FieldError("categoryId", "Choose a category from the list."));
                else f.CategoryId = categoryId;
            }
            else f.CategoryId = null;
            f.Description = (input.Description ?? "").Trim();
            var cc = (input.Cc ?? new List<string>()).Select(e => (e ?? "").Trim()).Where(e => e.Length > 0).ToList();
            if (cc.Any(e => !Strings.IsEmail(e))) errors.Add(new FieldError("cc", "Copy To takes email addresses."));
            f.Cc = string.Join(",", cc.Distinct(StringComparer.OrdinalIgnoreCase));
            f.MightCauseCustomerComplaint = input.MightCauseCustomerComplaint;
            f.CreatorHasSolution = input.CreatorHasSolution;
            f.SolutionDetail = (input.SolutionDetail ?? "").Trim();
            f.ExpectedOutcome = (input.ExpectedOutcome ?? "").Trim();
            // Each box saved from itself (MyCRM saved Process Improvement from the Ricoh Way Value box).
            f.SuggestionMeetCriteria = EssChoices.Join(EssChoices.Criteria, input.SuggestionMeetCriteria);
            f.RicohWayValue = EssChoices.Join(EssChoices.RicohWayValues, input.RicohWayValue);
        }
        if (r.EditHr)
        {
            var answer = (input.HrAccepted ?? HrAnswers.None).Trim();
            f.HrAccepted = HrAnswers.All.Contains(answer) ? answer : HrAnswers.None;
            f.HrRemarks = (input.HrRemarks ?? "").Trim();
        }
        if (r.EditAcknowledge && input.IsAcknowledged != f.IsAcknowledged)
        {
            // MyCRM's OnFeedback_IsAcknowledgedChange: the date and time are now, and the person signs it off.
            f.IsAcknowledged = input.IsAcknowledged;
            if (f.IsAcknowledged)
            {
                var now = clock();
                f.AcknowledgedDate = now.Date;
                f.AcknowledgedTime = TimesOfDay.Of(now);
                f.SignOff = Strings.Shorten(actor.Login, 256);
            }
        }
        if (r.EditInvestigation)
        {
            f.FirstLookAtFeedback = (input.FirstLookAtFeedback ?? "").Trim();
            var rootCause = (input.RootCause ?? "").Trim();
            // MyCRM's OnFeedback_RootCauseChange: whoever writes the root cause is the staff responsible.
            if (rootCause != f.RootCause) f.StaffResponsibleForSolution = Strings.Shorten(actor.Login, 256);
            f.RootCause = rootCause;
        }
        if (r.EditSatisfaction)
        {
            var level = (input.SatisfactionLevel ?? "").Trim();
            if (level.Length > 0 && !SatisfactionLevels.List.IsKnown(level)) errors.Add(new FieldError("satisfactionLevel", "Choose a satisfaction level."));
            else f.SatisfactionLevel = level;
        }
        if (errors.Count > 0) return OperationResult.Invalid(errors);

        // 1. The contact email, and (where it is asked) the time received.
        if (f.FeedbackGroup != FeedbackGroups.InternalChallenges)
        {
            if (f.CompanyEmail.Length > 0 && !Strings.IsEmail(f.CompanyEmail)) errors.Add(new FieldError("companyEmail", "Invalid Email Address"));
            if (r.EditNew && FeedbackGroups.HasCustomerDetails(f.FeedbackGroup) && f.TimeReceived.Length == 0) errors.Add(new FieldError("timeReceived", "Invalid Time"));
        }
        // 2. HR's answer.
        if (r.EditHr && f.HrAccepted == HrAnswers.None) errors.Add(new FieldError("hrAccepted", "Invalid Option"));
        // 3. The acknowledgement's date and time.
        if (f.IsAcknowledged)
        {
            if (f.AcknowledgedDate is not { } acknowledged || acknowledged.Date > today || (f.DateReceived is { } received && acknowledged.Date < received.Date))
                errors.Add(new FieldError("acknowledgedDate", "Invalid Acknowledged Date"));
            if (f.AcknowledgedTime.Length == 0) errors.Add(new FieldError("acknowledgedTime", "Invalid Time"));
        }
        if (errors.Count > 0) return OperationResult.Invalid(errors);
        // 4. The customer details.
        if (FeedbackGroups.HasCustomerDetails(f.FeedbackGroup))
        {
            var missing = new List<FieldError>();
            if (f.CompanyName.Length == 0) missing.Add(new FieldError("companyName", Required));
            if (f.ContactName.Length == 0) missing.Add(new FieldError("contactName", Required));
            if (f.CompanyEmail.Length == 0) missing.Add(new FieldError("companyEmail", Required));
            if (f.MediumId is null) missing.Add(new FieldError("mediumId", Required));
            if (f.DateReceived is null) missing.Add(new FieldError("dateReceived", Required));
            if (f.TimeReceived.Length == 0) missing.Add(new FieldError("timeReceived", Required));
            if (missing.Count > 0) return OperationResult.Failure(ResultCodes.Validation, "Please fill in customer details", missing);
        }
        // 5. The required fields that the person can fill in.
        if (f.FeedbackGroup.Length == 0) errors.Add(new FieldError("feedbackGroup", Required));
        if (r.EditNew)
        {
            if (f.PersonResponsible.Length == 0) errors.Add(new FieldError("personResponsible", Required));
            if (f.FeedbackType.Length == 0) errors.Add(new FieldError("feedbackType", Required));
            if (f.CategoryId is null) errors.Add(new FieldError("categoryId", Required));
            if (f.Description.Length == 0) errors.Add(new FieldError("description", Required));
            if (f.FeedbackGroup == FeedbackGroups.InternalChallenges && f.CreatorHasSolution)
            {
                if (f.SolutionDetail.Length == 0) errors.Add(new FieldError("solutionDetail", Required));
                if (f.ExpectedOutcome.Length == 0) errors.Add(new FieldError("expectedOutcome", Required));
                if (f.SuggestionMeetCriteria.Length == 0) errors.Add(new FieldError("suggestionMeetCriteria", Required));
                if (f.RicohWayValue.Length == 0) errors.Add(new FieldError("ricohWayValue", Required));
            }
        }
        var tasks = isNew ? new List<FeedbackTask>() : Tasks(f.Id).ToList();
        if (r.EditInvestigation && f.Status == FeedbackStatuses.Acknowledged)
        {
            if (f.FirstLookAtFeedback.Length == 0) errors.Add(new FieldError("firstLookAtFeedback", Required));
            if (tasks.Count > 0 && tasks.All(t => t.IsCompleted) && f.RootCause.Length == 0) errors.Add(new FieldError("rootCause", Required));
        }
        if (r.EditSatisfaction && f.Status == FeedbackStatuses.Investigation && f.SatisfactionLevel.Length == 0)
            errors.Add(new FieldError("satisfactionLevel", Required));
        if (errors.Count > 0) return OperationResult.Failure(ResultCodes.Validation, "Invalid input", errors);
        // 6. A root cause needs a task.
        if (f.Status == FeedbackStatuses.Acknowledged && (f.RootCause.Length > 0 || f.CorrectiveAction.Length > 0) && tasks.Count == 0)
            return OperationResult.Failure(ResultCodes.Validation, "Please add at least one task");

        CreateOrUpdate(actor, f, isNew);
        return OperationResult.Success("The feedback was successfully " + (isNew ? "created." : "updated."), f.Id);
    }

    /// <summary>MyCRM's CreateOrUpdateFeedback_: who and when, saved, a new one's emails, then the status and the timings.</summary>
    private void CreateOrUpdate(Actor actor, Feedback f, bool isNew)
    {
        var now = clock();
        if (isNew)
        {
            f.CreatedOn = now;
            f.CreatedBy = Strings.Shorten(actor.Login, 256);
            f.CreatedByUserName = Strings.Shorten(actor.DisplayName, 500);
            f.Status = FeedbackStatuses.New;
            f.SessionId = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        Touch(actor, f, now);
        if (f.Status == FeedbackStatuses.PendingHr && f.HrAccepted != HrAnswers.None)
        {
            f.HrUpdatedOn = now;
            f.HrUpdatedBy = Strings.Shorten(actor.Login, 256);
        }
        if (isNew) store.Insert(f);
        else store.Update(f);
        if (isNew)
        {
            Notify(f);
            Send(f, EmailCase.New);
        }
        UpdateStatus(actor, f);
        TimeAndTell(f, now);
    }

    /// <summary>
    /// UpdateTiming after a change; when that completes a feedback in Investigation / Resolution, BE is told at once
    /// (MyCRM worked out the status first, so BE heard only at a later save).
    /// </summary>
    private void TimeAndTell(Feedback f, DateTime now)
    {
        var wasCompleted = f.IsCompleted;
        UpdateTiming(f, manual: false, now);
        if (!wasCompleted && f.IsCompleted && f.Status == FeedbackStatuses.Investigation) Send(f, EmailCase.Be);
    }

    private static void Touch(Actor actor, Feedback f, DateTime now)
    {
        f.UpdatedOn = now;
        f.UpdatedBy = Strings.Shorten(actor.Login, 256);
        f.UpdatedByUserName = Strings.Shorten(actor.DisplayName, 500);
    }

    /// <summary>MyCRM's StatusHandler: the next status, from the feedback as saved.</summary>
    public static string? NextStatus(Feedback f, IReadOnlyList<FeedbackTask> tasks)
    {
        string AfterInvestigation() => f.SatisfactionLevel.Length > 0 ? FeedbackStatuses.ConfirmedSatisfaction : FeedbackStatuses.Investigation;
        string AfterAcknowledged() =>
            f.RootCause.Length > 0 && tasks.Any(t => t.TaskType == TaskTypes.Corrective) && f.StaffResponsibleForSolution.Length > 0 && f.FirstLookAtFeedback.Length > 0
                ? AfterInvestigation() : FeedbackStatuses.Acknowledged;
        switch (f.Status)
        {
            case FeedbackStatuses.New:
                if (f.FeedbackGroup == FeedbackGroups.InternalChallenges && f.HrAccepted == HrAnswers.None && f.CreatorHasSolution) return FeedbackStatuses.PendingHr;
                return f.IsAcknowledged ? AfterAcknowledged() : FeedbackStatuses.New;
            case FeedbackStatuses.PendingHr:
                return f.HrAccepted == HrAnswers.Yes ? FeedbackStatuses.New : f.HrAccepted == HrAnswers.No ? FeedbackStatuses.Closed : FeedbackStatuses.PendingHr;
            case FeedbackStatuses.Acknowledged:
                return AfterAcknowledged();
            case FeedbackStatuses.Investigation:
                return AfterInvestigation();
            default:
                return null;
        }
    }

    private void UpdateStatus(Actor actor, Feedback f)
    {
        if (NextStatus(f, Tasks(f.Id)) is { } next) SetStatus(actor, f, next);
    }

    /// <summary>MyCRM's UpdateFeedbackStatusNEmail: the status saved; BE told when a completed feedback is (or stays) in Investigation / Resolution.</summary>
    private void SetStatus(Actor actor, Feedback f, string status)
    {
        Touch(actor, f, clock());
        f.Status = status;
        store.Update(f);
        if (status == FeedbackStatuses.Investigation && f.IsCompleted) Send(f, EmailCase.Be);
    }

    /// <summary>
    /// MyCRM's UpdateTiming: once acknowledged, the hours it took; with tasks, when the resolution started; with every
    /// task completed, when it was completed (or, without a root cause, a corrective task or a staff responsible, an email
    /// to the person in charge). <paramref name="manual"/> works them out again (KPI dates).
    /// </summary>
    public void UpdateTiming(Feedback f, bool manual, DateTime completedOn)
    {
        if (f.AcknowledgedDate is null) return;
        var days = businessDays();
        var received = ReceivedAt(f);
        var receivedOn = ReceivedOn(f);
        if (received is { } from && (f.AcknowledgedHours == 0 || manual))
        {
            f.AcknowledgedHours = days.Hours(from, TimesOfDay.At(f.AcknowledgedDate, f.AcknowledgedTime)!.Value);
            store.Update(f);
        }
        var tasks = Tasks(f.Id).OrderBy(t => t.CreatedOn ?? DateTime.MaxValue).ThenBy(t => t.Id).ToList();
        if (tasks.Count == 0) return;
        if ((f.StartedResolutionOn is null || manual) && tasks[0].CreatedOn is { } started && receivedOn is { } day)
        {
            f.StartedResolutionOn = started;
            f.StartResolutionDays = days.Days(day, started);
            store.Update(f);
        }
        if (!tasks.All(t => t.IsCompleted)) return;
        if (f.RootCause.Length > 0 && tasks.Any(t => t.TaskType == TaskTypes.Corrective) && f.StaffResponsibleForSolution.Length > 0)
        {
            if ((f.CompletedResolutionOn is null || manual) && receivedOn is { } day2)
            {
                f.CompletedResolutionOn = completedOn;
                f.CloseCaseDays = days.Days(day2, completedOn);
                f.IsCompleted = true;
                store.Update(f);
            }
        }
        else Send(f, EmailCase.TasksCompleted);
    }

    /// <summary>Delete (BE): the feedback is marked inactive, as in MyCRM, never deleted.</summary>
    public OperationResult Delete(Actor actor, long id)
    {
        if (Get(id) is not { } f) return OperationResult.Failure(ResultCodes.NotFound, "That feedback is not there.");
        if (!Rights(actor, f).Delete) return OperationResult.Failure(ResultCodes.Forbidden, "Feedbacks are deleted by BE.");
        var now = clock();
        f.IsActive = false;
        f.RemovedOn = now;
        f.RemovedBy = Strings.Shorten(actor.ChangedBy, 600);
        Touch(actor, f, now);
        store.Update(f);
        return OperationResult.Success("The feedback was successfully deleted.", f.Id);
    }

    /// <summary>Close Case (BE, Confirmed Satisfaction): the feedback is Closed. Nothing else on the page is saved, as in MyCRM.</summary>
    public OperationResult CloseCase(Actor actor, long id)
    {
        if (Get(id) is not { } f) return OperationResult.Failure(ResultCodes.NotFound, "That feedback is not there.");
        if (!actor.IsBe) return OperationResult.Failure(ResultCodes.Forbidden, "Feedbacks are closed by BE.");
        if (f.Status != FeedbackStatuses.ConfirmedSatisfaction) return OperationResult.Failure(ResultCodes.Conflict, "A feedback is closed once its satisfaction is confirmed.");
        SetStatus(actor, f, FeedbackStatuses.Closed);
        return OperationResult.Success($"Feedback #{f.Id.ToString(CultureInfo.InvariantCulture)} is closed.", f.Id);
    }

    // ---------------------------------------------------------------- tasks

    /// <summary>Adds (task 0) or changes a task (MyCRM's TaskDetail_PopUp).</summary>
    public OperationResult SaveTask(Actor actor, long feedbackId, long taskId, TaskInput input)
    {
        if (Get(feedbackId) is not { } f) return OperationResult.Failure(ResultCodes.NotFound, "That feedback is not there.");
        var r = Rights(actor, f);
        var isNew = taskId == 0;
        var task = isNew ? new FeedbackTask() : Tasks(f.Id).FirstOrDefault(t => t.Id == taskId);
        if (task is null) return OperationResult.Failure(ResultCodes.NotFound, "That task is not there.");
        if (isNew)
        {
            var type = (input.TaskType ?? "").Trim();
            if (!TaskTypes.List.IsKnown(type)) return OperationResult.Failure(ResultCodes.Validation, "Choose corrective or preventive.");
            if (!(type == TaskTypes.Corrective ? r.AddCorrectiveTask : r.AddPreventiveTask))
                return OperationResult.Failure(ResultCodes.Forbidden, "Tasks are added by the person in charge of the feedback (or of a task of this kind), until it is closed.");
            task.TaskType = type;
        }
        var taskPerson = !isNew && actor.IsLogin(task.PersonInCharge);
        if (!isNew && !(r.NotClosed && (r.InCharge || taskPerson)))
            return OperationResult.Failure(ResultCodes.Forbidden, "A task is changed by the person in charge of the feedback or of the task, until the feedback is closed.");
        var errors = new List<FieldError>();
        // The action plan and the person: the feedback's person in charge (and whoever adds the task).
        if (isNew || r.InCharge)
        {
            task.ActionPlan = (input.ActionPlan ?? "").Trim();
            task.PersonInCharge = Strings.Shorten((input.PersonInCharge ?? "").Trim(), 256);
            if (task.ActionPlan.Length == 0) errors.Add(new FieldError("actionPlan", Required));
            if (task.PersonInCharge.Length == 0) errors.Add(new FieldError("personInCharge", Required));
        }
        // The remarks: the task's person in charge.
        if (isNew || taskPerson) task.Remarks = Strings.Shorten((input.Remarks ?? "").Trim(), 5000);
        task.DueDate = input.DueDate?.Date;
        if (task.DueDate is null) errors.Add(new FieldError("dueDate", Required));
        task.IsCompleted = input.IsCompleted;
        if (errors.Count > 0) return OperationResult.Invalid(errors);
        var now = clock();
        if (isNew)
        {
            task.CreatedOn = now;
            task.CreatedBy = Strings.Shorten(actor.Login, 256);
            task.FeedbackId = f.Id;
            task.SessionId = f.SessionId;
            store.Insert(task);
        }
        else store.Update(task);
        TimeAndTell(f, now);
        if (isNew) Send(f, EmailCase.Task, task);
        return OperationResult.Success("The task was successfully " + (isNew ? "created." : "updated."), task.Id);
    }

    /// <summary>The Is Completed toggle of a task's row: saved at once, the timings worked out again.</summary>
    public OperationResult CompleteTask(Actor actor, long taskId, bool completed)
    {
        if (store.Get<FeedbackTask>(taskId) is not { IsActive: true } task || Get(task.FeedbackId) is not { } f)
            return OperationResult.Failure(ResultCodes.NotFound, "That task is not there.");
        var r = Rights(actor, f);
        if (!(r.NotClosed && (r.InCharge || actor.IsLogin(task.PersonInCharge))))
            return OperationResult.Failure(ResultCodes.Forbidden, "A task is completed by its person in charge or the feedback's, until the feedback is closed.");
        task.IsCompleted = completed;
        store.Update(task);
        TimeAndTell(f, clock());
        return OperationResult.Success(completed ? "The task is completed." : "The task is not completed.", task.Id);
    }

    /// <summary>Removes a task (the feedback's person in charge): marked inactive, never deleted.</summary>
    public OperationResult RemoveTask(Actor actor, long taskId)
    {
        if (store.Get<FeedbackTask>(taskId) is not { IsActive: true } task || Get(task.FeedbackId) is not { } f)
            return OperationResult.Failure(ResultCodes.NotFound, "That task is not there.");
        var r = Rights(actor, f);
        if (!(r.NotClosed && r.InCharge)) return OperationResult.Failure(ResultCodes.Forbidden, "Tasks are removed by the feedback's person in charge, until it is closed.");
        task.IsActive = false;
        task.RemovedOn = clock();
        task.RemovedBy = Strings.Shorten(actor.ChangedBy, 600);
        store.Update(task);
        return OperationResult.Success("The task was removed.", task.Id);
    }

    // ---------------------------------------------------------------- files

    public const int MaxFileBytes = 20 * 1024 * 1024;

    public OperationResult Attach(Actor actor, long feedbackId, string kind, string fileName, byte[] content)
    {
        if (Get(feedbackId) is not { } f) return OperationResult.Failure(ResultCodes.NotFound, "Save the feedback first.");
        if (!FileKinds.IsKnown(kind)) return OperationResult.Failure(ResultCodes.Validation, "Choose where the file goes.");
        if (!MayKeepFiles(actor, f, kind)) return OperationResult.Failure(ResultCodes.Forbidden, "The customer details files are added by Feedback Admins.");
        var name = Path.GetFileName((fileName ?? "").Replace('\\', '/').Split('/').Last()).Trim();
        if (name.Length == 0 || content.Length == 0) return OperationResult.Failure(ResultCodes.Validation, "Choose a file.");
        if (content.Length > MaxFileBytes) return OperationResult.Failure(ResultCodes.Validation, "A file can be at most 20 MB.");
        var file = store.Insert(new FeedbackFile
        {
            FeedbackId = f.Id, FileName = Strings.Shorten(name, 500), Content = content, Kind = kind, UploadedBy = Strings.Shorten(actor.Login, 256), UploadedOn = clock(),
            SessionId = f.SessionId,
        });
        return OperationResult.Success("File uploaded successfully", file.Id);
    }

    public OperationResult RemoveFile(Actor actor, long fileId)
    {
        if (File(fileId) is not { } file || Get(file.FeedbackId) is not { } f) return OperationResult.Failure(ResultCodes.NotFound, "That file is not there.");
        if (!MayKeepFiles(actor, f, file.Kind)) return OperationResult.Failure(ResultCodes.Forbidden, "The customer details files are removed by Feedback Admins.");
        file.IsActive = false;
        file.RemovedOn = clock();
        file.RemovedBy = Strings.Shorten(actor.ChangedBy, 600);
        store.Update(file);
        return OperationResult.Success($"{file.FileName} was removed.", file.Id);
    }

    private bool MayKeepFiles(Actor actor, Feedback f, string kind)
    {
        var r = Rights(actor, f);
        return kind == FileKinds.CustomerDetails ? r.CustomerFiles : r.SatisfactionFiles;
    }

    // ---------------------------------------------------------------- emails

    public enum EmailCase { New, Task, TasksCompleted, Be }

    private string Link(Feedback f) => settings().Email.LinkTo("feedback.aspx?id=" + f.Id.ToString(CultureInfo.InvariantCulture));

    /// <summary>MyCRM's Email subject: "MyCRMRecords - Feedback #12 - New - Acme Pte Ltd[RESTRICTED]".</summary>
    public static string Subject(Feedback f) =>
        $"MyCRMRecords - Feedback #{f.Id.ToString(CultureInfo.InvariantCulture)} - {FeedbackStatuses.List.Label(f.Status)} - {f.CompanyName}[RESTRICTED]";

    private IEnumerable<(string Label, string? Value)> Facts(Feedback f)
    {
        var category = f.CategoryId is { } c ? store.Get<FeedbackCategory>(c)?.Label : null;
        return
        [
            ("Feedback", "#" + f.Id.ToString(CultureInfo.InvariantCulture)),
            ("Status", FeedbackStatuses.List.Label(f.Status)),
            ("Feedback Category", FeedbackGroups.List.Label(f.FeedbackGroup)),
            ("Feedback Type", FeedbackTypes.List.Label(f.FeedbackType)),
            ("Category", category),
            ("Company", f.CompanyName),
            ("Contact", f.ContactName),
            ("Date Received", f.DateReceived?.ToString("d MMM yyyy", CultureInfo.InvariantCulture)),
            ("Created by", f.CreatedByUserName),
            ("CMS in Charge", people.NameOf(f.PersonResponsible)),
        ];
    }

    private string Description(Feedback f) =>
        f.Description.Length == 0 ? "" : "<p style=\"white-space:pre-wrap;background:#f4f6fb;padding:10px 12px;border-radius:6px\">" + EmailHtml.E(Strings.Shorten(f.Description, 3000)) + "</p>";

    /// <summary>MyCRM's Notify: a new feedback, to the recipients of its category (settings, Email tab).</summary>
    private void Notify(Feedback f)
    {
        var s = settings().Email;
        var to = f.FeedbackGroup switch
        {
            FeedbackGroups.InternalChallenges => s.InternalChallengesNotify,
            FeedbackGroups.CustomerComplaint or FeedbackGroups.AreaOfImprovement => s.ComplaintsNotify,
            _ => s.OthersNotify,
        };
        var html = EmailHtml.Page("A new feedback was created", $"<p>{EmailHtml.E(f.CreatedByUserName)} created feedback #{f.Id.ToString(CultureInfo.InvariantCulture)}.</p>"
            + EmailHtml.Facts(Facts(f)) + Description(f), Link(f), "Open the feedback in MyCRMRecords");
        mailer.Send(new Email(RequestKind, f.Id, Strings.Emails(to), Array.Empty<string>(), "MyCRMRecords - Feedback Created by " + f.CreatedByUserName, html));
    }

    /// <summary>MyCRM's Email: to the person in charge (new, tasks completed), a task's person (task) or BE.</summary>
    private void Send(Feedback f, EmailCase kind, FeedbackTask? task = null)
    {
        var s = settings().Email;
        var inCharge = people.EmailOf(f.PersonResponsible);
        var id = f.Id.ToString(CultureInfo.InvariantCulture);
        IReadOnlyList<string> to;
        IReadOnlyList<string> cc = Array.Empty<string>();
        string heading, intro;
        switch (kind)
        {
            case EmailCase.New:
                to = [inCharge];
                cc = Strings.Emails(f.Cc);
                heading = $"Feedback #{id} is yours";
                intro = "<p>You are in charge of this feedback. Please acknowledge it within 24 hours, then look into it and add its corrective actions.</p>";
                break;
            case EmailCase.Task when task is not null:
                to = [people.EmailOf(task.PersonInCharge)];
                heading = $"A task of feedback #{id} for you";
                intro = "<p>You are in charge of this " + EmailHtml.E(TaskTypes.List.Label(task.TaskType).ToLowerInvariant()) + ":</p>"
                        + EmailHtml.Facts([("Action plan", task.ActionPlan), ("Due date", task.DueDate?.ToString("d MMM yyyy", CultureInfo.InvariantCulture))]);
                break;
            case EmailCase.TasksCompleted:
                to = [inCharge];
                heading = $"Every task of feedback #{id} is completed";
                intro = "<p>Please fill in the root cause, and make sure there is a corrective action, so the feedback can be resolved.</p>";
                break;
            case EmailCase.Be:
                to = Strings.Emails(s.BeTo);
                heading = $"Feedback #{id} is resolved";
                intro = "<p>Its tasks are completed and its root cause is known. Please confirm the customer's (or employee's) satisfaction.</p>";
                break;
            default:
                return;
        }
        mailer.Send(new Email(RequestKind, f.Id, to, cc, Subject(f), EmailHtml.Page(heading, intro + EmailHtml.Facts(Facts(f)) + Description(f), Link(f), "Open the feedback in MyCRMRecords")));
    }
}
