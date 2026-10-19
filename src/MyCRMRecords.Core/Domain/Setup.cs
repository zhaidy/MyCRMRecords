using System.Globalization;
using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>
/// The Lists page (MyCRM's Admin screen), for Feedback Admins: the divisions with their person in charge, the
/// feedback categories and mediums, and the KPI dates (MyCRM's BackOffice). Nothing is deleted: a division taken away
/// is inactive (as in MyCRM), a category or medium is marked removed (MyCRM deleted it).
/// </summary>
public sealed class Setup(IRecordStore store, Feedbacks feedbacks, Func<DateTime> clock)
{
    public sealed class ChoiceInput
    {
        public string? Label { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public sealed class DivisionInput
    {
        public string? Name { get; set; }
        public string? PersonInCharge { get; set; }
    }

    public sealed class KpiInput
    {
        public DateTime? CreatedOn { get; set; }
        public DateTime? DateReceived { get; set; }
        public string? TimeReceived { get; set; }
        public DateTime? AcknowledgedDate { get; set; }
        public string? AcknowledgedTime { get; set; }
        public DateTime? CompletedResolutionOn { get; set; }
    }

    private static OperationResult NotAllowed() => OperationResult.Failure(ResultCodes.Forbidden, "The lists are kept by Feedback Admins.");

    public OperationResult SaveDivision(Actor actor, long id, DivisionInput input)
    {
        if (!actor.IsFeedbackAdmin) return NotAllowed();
        var division = id == 0 ? new FeedbackDivision() : store.Get<FeedbackDivision>(id) is { IsActive: true } d ? d : null;
        if (division is null) return OperationResult.Failure(ResultCodes.NotFound, "That division is not there.");
        var name = (input.Name ?? "").Trim();
        if (name.Length == 0) return OperationResult.Invalid([new FieldError("name", Feedbacks.Required)]);
        if (name.Length > 50) return OperationResult.Invalid([new FieldError("name", "At most 50 characters.")]);
        if (store.Find<FeedbackDivision>(Query.Where("IsActive", true)).Any(o => o.Id != division.Id && Strings.SameText(o.Name, name)))
            return OperationResult.Invalid([new FieldError("name", "There is a division with this name.")]);
        division.Name = name;
        division.PersonInCharge = Strings.Shorten((input.PersonInCharge ?? "").Trim(), 256);
        if (id == 0) store.Insert(division);
        else store.Update(division);
        return OperationResult.Success($"Division {division.Name} saved.", division.Id);
    }

    public OperationResult RemoveDivision(Actor actor, long id)
    {
        if (!actor.IsFeedbackAdmin) return NotAllowed();
        if (store.Get<FeedbackDivision>(id) is not { IsActive: true } division) return OperationResult.Failure(ResultCodes.NotFound, "That division is not there.");
        division.IsActive = false;
        division.RemovedOn = clock();
        division.RemovedBy = Strings.Shorten(actor.ChangedBy, 600);
        store.Update(division);
        return OperationResult.Success($"Division {division.Name} was taken away. Its feedbacks keep it.", division.Id);
    }

    public OperationResult SaveCategory(Actor actor, long id, ChoiceInput input) => SaveChoice<FeedbackCategory>(actor, id, input, "category",
        (c, label, order, active) => { c.Label = label; c.SortOrder = order; c.IsActive = active; }, c => c.Label, c => c.Removed);

    public OperationResult SaveMedium(Actor actor, long id, ChoiceInput input) => SaveChoice<FeedbackMedium>(actor, id, input, "medium",
        (c, label, order, active) => { c.Label = label; c.SortOrder = order; c.IsActive = active; }, c => c.Label, c => c.Removed);

    public OperationResult RemoveCategory(Actor actor, long id) => RemoveChoice<FeedbackCategory>(actor, id, "category", c => c.Removed, c => c.Label,
        c => { c.Removed = true; c.RemovedOn = clock(); c.RemovedBy = Strings.Shorten(actor.ChangedBy, 600); });

    public OperationResult RemoveMedium(Actor actor, long id) => RemoveChoice<FeedbackMedium>(actor, id, "medium", c => c.Removed, c => c.Label,
        c => { c.Removed = true; c.RemovedOn = clock(); c.RemovedBy = Strings.Shorten(actor.ChangedBy, 600); });

    private OperationResult SaveChoice<T>(Actor actor, long id, ChoiceInput input, string what, Action<T, string, int, bool> set, Func<T, string> label, Func<T, bool> removed)
        where T : class, IRecord, new()
    {
        if (!actor.IsFeedbackAdmin) return NotAllowed();
        var record = id == 0 ? new T() : store.Get<T>(id) is { } r && !removed(r) ? r : null;
        if (record is null) return OperationResult.Failure(ResultCodes.NotFound, $"That {what} is not there.");
        var text = (input.Label ?? "").Trim();
        if (text.Length == 0) return OperationResult.Invalid([new FieldError("label", Feedbacks.Required)]);
        if (text.Length > 50) return OperationResult.Invalid([new FieldError("label", "At most 50 characters.")]);
        if (store.Find<T>().Any(o => o.Id != record.Id && !removed(o) && Strings.SameText(label(o), text)))
            return OperationResult.Invalid([new FieldError("label", $"There is a {what} with this name.")]);
        set(record, text, input.SortOrder, input.IsActive);
        if (id == 0) store.Insert(record);
        else store.Update(record);
        return OperationResult.Success($"{Capital(what)} {text} saved.", record.Id);
    }

    private OperationResult RemoveChoice<T>(Actor actor, long id, string what, Func<T, bool> removed, Func<T, string> label, Action<T> mark) where T : class, IRecord, new()
    {
        if (!actor.IsFeedbackAdmin) return NotAllowed();
        if (store.Get<T>(id) is not { } record || removed(record)) return OperationResult.Failure(ResultCodes.NotFound, $"That {what} is not there.");
        mark(record);
        store.Update(record);
        return OperationResult.Success($"{Capital(what)} {label(record)} was taken away. Its feedbacks keep it.", record.Id);
    }

    private static string Capital(string text) => char.ToUpperInvariant(text[0]) + text.Substring(1);

    /// <summary>
    /// MyCRM's BackOffice row: the dates are saved as they are (no status change, nobody recorded), then the timings are
    /// worked out again from them.
    /// </summary>
    public OperationResult SaveKpiDates(Actor actor, long id, KpiInput input)
    {
        if (!actor.IsFeedbackAdmin) return NotAllowed();
        if (feedbacks.Get(id) is not { } f) return OperationResult.Failure(ResultCodes.NotFound, "That feedback is not there.");
        var errors = new List<FieldError>();
        string Time(string? text, string field)
        {
            var normal = TimesOfDay.Normalise(text);
            if ((text ?? "").Trim().Length > 0 && normal.Length == 0) errors.Add(new FieldError(field, "Invalid Time"));
            return normal;
        }
        var received = Time(input.TimeReceived, "timeReceived");
        var acknowledged = Time(input.AcknowledgedTime, "acknowledgedTime");
        if (errors.Count > 0) return OperationResult.Invalid(errors);
        f.CreatedOn = input.CreatedOn ?? f.CreatedOn;
        f.DateReceived = input.DateReceived?.Date;
        f.TimeReceived = received;
        f.AcknowledgedDate = input.AcknowledgedDate?.Date;
        f.AcknowledgedTime = acknowledged;
        f.CompletedResolutionOn = input.CompletedResolutionOn;
        store.Update(f);
        feedbacks.UpdateTiming(f, manual: true, input.CompletedResolutionOn ?? clock());
        return OperationResult.Success($"The KPI dates of feedback #{f.Id.ToString(CultureInfo.InvariantCulture)} are saved and its timings worked out again.", f.Id);
    }
}
