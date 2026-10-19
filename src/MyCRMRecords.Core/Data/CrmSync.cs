using System.Data.Common;
using System.Globalization;
using MyCRMRecords.Domain;
using static MyCRMRecords.Data.OldValues;
using OldRow = System.Collections.Generic.Dictionary<string, object?>;

namespace MyCRMRecords.Data;

/// <summary>
/// What the sync with MyCRM copies, both ways (OldAppSync): its feedback categories, mediums and divisions, the
/// feedbacks, their tasks and their attachments, in the OutSystems database. People are written as their OutSystems
/// user (found by login, name or email), never added to OutSystems; MyCRM's static entities are matched by label.
/// <list type="bullet">
/// <item>Nothing is deleted on either side. A feedback or a division removed here is inactive there (MyCRM's own
/// mark). A category or a medium removed here gets ISACTIVE 0 there. A task or an attachment removed here is
/// detached from its feedback there (FEEDBACKID emptied, SESSIONID "X:&lt;feedback&gt;:&lt;session&gt;", so MyCRM no
/// longer shows it, and the feedback stays known); an attachment's FILENAME also gets "(removed) " before it. A row
/// MyCRM deleted is marked removed here.</item>
/// <item>A task or an attachment MyCRM keeps for a feedback never saved (no FEEDBACKID, a session no feedback has) is
/// left there.</item>
/// <item>An attachment's bytes are not read at every sync: the rows give their length and SHA-256 (<see cref="CrmTables"/>:
/// SQL Server hashes only the files not kept here), and the bytes are read only to copy a new or changed file here.</item>
/// </list>
/// </summary>
public static class CrmSync
{
    public const string Categories = "FeedbackCategory";
    public const string Mediums = "FeedbackMedium";
    public const string Divisions = "FeedbackDivision";
    public const string Feedbacks = "Feedback";
    public const string Tasks = "Task";
    public const string Attachments = "FeedbackAttachment";

    /// <summary>Before the file name of an attachment removed here, in MyCRM.</summary>
    public const string RemovedFile = "(removed) ";

    /// <summary>Before the SESSIONID of a task or attachment removed here (detached from its feedback), in MyCRM.</summary>
    public const string Detached = "X:";

    /// <summary>"X:&lt;MyCRM's feedback Id&gt;:&lt;session&gt;", at most 50 characters (MyCRM's SessionId).</summary>
    public static string DetachedSession(long? oldFeedbackId, string session)
    {
        var text = Detached + (oldFeedbackId ?? 0).ToString(CultureInfo.InvariantCulture) + ":" + session;
        return text.Length <= 50 ? text : text.Substring(0, 50);
    }

    /// <summary>The feedback and session of a row detached by MyCRMRecords (no FEEDBACKID, SESSIONID "X:12:..."); false for any other row.</summary>
    public static bool IsDetached(OldRow row, out long oldFeedbackId, out string session)
    {
        oldFeedbackId = 0;
        session = OldValues.Text(row, "SESSIONID");
        if (Long(row, "FEEDBACKID") != 0 || !session.StartsWith(Detached, StringComparison.Ordinal)) return false;
        var rest = session.Substring(Detached.Length);
        var colon = rest.IndexOf(':');
        if (colon <= 0 || !long.TryParse(rest.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out oldFeedbackId)) return false;
        session = rest.Substring(colon + 1);
        return true;
    }

    /// <summary>A login of an OutSystems user's Id, or the record's own when the user is not known.</summary>
    private static string LoginOf(SyncContext c, OldRow row, string column, string? existing) =>
        c.User(row.TryGetValue(column, out var v) ? v : null) is { } u ? Strings.Shorten(u.Login, 256) : existing ?? "";

    private static object? Day(DateTime? date) => date?.Date;

    private static string Text(OldRow row, string column, int length) => Strings.Shorten(OldValues.Text(row, column), length);

    /// <summary>MyCRM's Id of a static entity's record for a code (by its labels); null for none or unknown.</summary>
    private static long? Static(SyncContext c, ChoiceList list, string code) =>
        list.All.FirstOrDefault(x => x.Code == code) is { } choice ? c.LabelId(list.OldEntity, choice.Labels.ToArray()) : null;

    /// <summary>The code of a static entity's record in a row ("" for none or one not known here).</summary>
    private static string Code(SyncContext c, ChoiceList list, OldRow row, string column) =>
        list.FromLabel(c.Label(list.OldEntity, row.TryGetValue(column, out var v) ? v : null));

    private static string Time(OldRow row, string column) => TimesOfDay.FromOutSystems(row.TryGetValue(column, out var v) ? v : null);

    public static IReadOnlyList<ISyncEntity> Entities(Func<DateTime> clock, Func<IOldTables?>? tables = null)
    {
        // A record found inactive in MyCRM: when and by whom, kept from here when it was taken away here.
        (DateTime? On, string By) Removal(bool active, DateTime? on, string? by, bool wasActive) =>
            active ? (null, "") : !wasActive ? (on, by ?? "") : (clock(), "MyCRM");

        return
        [
            new SyncEntity<FeedbackCategory>
            {
                Name = Categories, Label = "feedback category", OldTable = Categories, OldCreatedOnColumn = null, OldUpdatedOnColumn = null,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                MarkRemovedHere = r => { r.Removed = true; r.RemovedOn = clock(); r.RemovedBy = "MyCRM"; },
                Describe = r => r.Label,
                // Removed here: inactive there (MyCRM deleted it; its own Is Active is the nearest mark).
                ToOld = (r, _, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["LABEL"] = r.Label, ["ORDER"] = r.SortOrder, ["ISACTIVE"] = r.IsActive && !r.Removed,
                },
                FromOld = (row, _, x) => new FeedbackCategory
                {
                    Label = Text(row, "LABEL", 50), SortOrder = Int(row, "ORDER"), IsActive = x is { Removed: true } ? x.IsActive : Bool(row, "ISACTIVE"),
                    Removed = x?.Removed ?? false, RemovedOn = x?.RemovedOn, RemovedBy = x?.RemovedBy ?? "", OutSystemsId = Long(row, "ID"),
                },
            },
            new SyncEntity<FeedbackMedium>
            {
                Name = Mediums, Label = "feedback medium", OldTable = Mediums, OldCreatedOnColumn = null, OldUpdatedOnColumn = null,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                MarkRemovedHere = r => { r.Removed = true; r.RemovedOn = clock(); r.RemovedBy = "MyCRM"; },
                Describe = r => r.Label,
                ToOld = (r, _, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["LABEL"] = r.Label, ["ORDER"] = r.SortOrder, ["ISACTIVE"] = r.IsActive && !r.Removed,
                },
                FromOld = (row, _, x) => new FeedbackMedium
                {
                    Label = Text(row, "LABEL", 50), SortOrder = Int(row, "ORDER"), IsActive = x is { Removed: true } ? x.IsActive : Bool(row, "ISACTIVE"),
                    Removed = x?.Removed ?? false, RemovedOn = x?.RemovedOn, RemovedBy = x?.RemovedBy ?? "", OutSystemsId = Long(row, "ID"),
                },
            },
            new SyncEntity<FeedbackDivision>
            {
                Name = Divisions, Label = "division", OldTable = Divisions, OldCreatedOnColumn = null, OldUpdatedOnColumn = null,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                MarkRemovedHere = r => { r.IsActive = false; r.RemovedOn = clock(); r.RemovedBy = "MyCRM"; },
                Describe = r => r.Name,
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DIVISION"] = r.Name, ["PERSONINCHARGE"] = c.OldUser(r.PersonInCharge), ["ISACTIVE"] = r.IsActive,
                },
                FromOld = (row, c, x) =>
                {
                    var active = Bool(row, "ISACTIVE");
                    var removal = Removal(active, x?.RemovedOn, x?.RemovedBy, x?.IsActive ?? true);
                    return new FeedbackDivision
                    {
                        Name = Text(row, "DIVISION", 50), PersonInCharge = LoginOf(c, row, "PERSONINCHARGE", x?.PersonInCharge), IsActive = active,
                        RemovedOn = removal.On, RemovedBy = removal.By, OutSystemsId = Long(row, "ID"),
                    };
                },
            },
            new SyncEntity<Feedback>
            {
                // The feedback's number is the same in both while the sync is on.
                Name = Feedbacks, Label = "feedback", OldTable = Feedbacks, TakesOldNumbers = true,
                OldCreatedOnColumn = "CREATEDON", OldUpdatedOnColumn = "UPDATEDON", UpdatedOn = r => r.UpdatedOn ?? r.CreatedOn,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                Parents = [(Categories, r => r.CategoryId), (Mediums, r => r.MediumId), (Divisions, r => r.DivisionId)],
                MarkRemovedHere = r => { r.IsActive = false; r.RemovedOn = clock(); r.RemovedBy = "MyCRM"; },
                Describe = r => $"#{r.Id.ToString(CultureInfo.InvariantCulture)} ({r.CompanyName})",
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["FEEDBACKGROUPID"] = Static(c, FeedbackGroups.List, r.FeedbackGroup), ["PERSONRESPONSIBLE"] = c.OldUser(r.PersonResponsible),
                    ["PERSONRESPONSIBLEDIVISION"] = r.PersonResponsibleDivision, ["FEEDBACKSTATUSID"] = Static(c, FeedbackStatuses.List, r.Status),
                    ["COMPANYNAME"] = r.CompanyName, ["COMPANYADDRESS"] = r.CompanyAddress, ["POSTALCODE"] = r.PostalCode, ["COMPANYPHONE"] = r.CompanyPhone,
                    ["COMPANYFAX"] = r.CompanyFax, ["COMPANYEMAIL"] = r.CompanyEmail, ["CONTACTNAME"] = r.ContactName, ["FEEDBACKMEDIUMID"] = c.OldId(Mediums, r.MediumId),
                    ["DATERECEIVED"] = Day(r.DateReceived), ["TIMERECEIVED"] = TimesOfDay.ToOutSystems(r.TimeReceived),
                    ["FEEDBACKTYPEID"] = Static(c, FeedbackTypes.List, r.FeedbackType), ["FEEDBACKCATEGORYID"] = c.OldId(Categories, r.CategoryId),
                    ["FEEDBACKDESCRIPTION"] = r.Description, ["STAFFRESPONSIBLEFORPROBLEM"] = c.OldUser(r.StaffResponsibleForProblem), ["CC"] = r.Cc,
                    ["ISACKNOWLEDGED"] = r.IsAcknowledged, ["ACKNOWLEDGEDTYPEID"] = Static(c, AcknowledgedTypes.List, r.AcknowledgedType),
                    ["ACKNOWLEDGEDDATE"] = Day(r.AcknowledgedDate), ["ACKNOWLEDGEDTIME"] = TimesOfDay.ToOutSystems(r.AcknowledgedTime), ["SIGNOFF"] = c.OldUser(r.SignOff),
                    ["FIRSTLOOKATFEEDBACK"] = r.FirstLookAtFeedback, ["ROOTCAUSE"] = r.RootCause, ["CORRECTIVEACTION"] = r.CorrectiveAction,
                    ["STAFFRESPONSIBLEFORSOLUTION"] = c.OldUser(r.StaffResponsibleForSolution),
                    ["CUSTOMERSATISFACTIONLEVELID"] = Static(c, SatisfactionLevels.List, r.SatisfactionLevel),
                    ["CREATEDON"] = r.CreatedOn, ["CREATEDBY"] = c.OldUser(r.CreatedBy), ["CREATEDBYUSERNAME"] = r.CreatedByUserName, ["UPDATEDON"] = r.UpdatedOn,
                    ["UPDATEDBY"] = c.OldUser(r.UpdatedBy), ["UPDATEDBYUSERNAME"] = r.UpdatedByUserName, ["ISACTIVE"] = r.IsActive, ["SESSIONID"] = r.SessionId,
                    ["ACKNOWLEGEDHOURS"] = r.AcknowledgedHours, ["STARTRESOLUTIONDAYS"] = r.StartResolutionDays, ["CLOSECASEDAYS"] = r.CloseCaseDays,
                    ["STARTEDRESOLUTIONON"] = r.StartedResolutionOn, ["COMPLETEDRESOLUTIONON"] = r.CompletedResolutionOn, ["ISCOMPLETED"] = r.IsCompleted,
                    ["CREATORHASSOLUTION"] = r.CreatorHasSolution, ["ESS_CREATORSOLUTIONDETAIL"] = r.SolutionDetail, ["ESS_EXPECTEDOUTCOME"] = r.ExpectedOutcome,
                    ["ESS_SUGGESTIONMEETCRITERIA"] = r.SuggestionMeetCriteria, ["ESS_RICOHWAYVALUE"] = r.RicohWayValue, ["HRACCEPTED"] = r.HrAccepted,
                    ["HRREMARKS"] = r.HrRemarks, ["HRUPDATEDON"] = r.HrUpdatedOn, ["HRUPDATEDBY"] = c.OldUser(r.HrUpdatedBy), ["PREVENTIVEACTION"] = r.PreventiveAction,
                    ["MIGHTCAUSECUSTOMERCOMPLAINT"] = r.MightCauseCustomerComplaint, ["FEEDBACKDIVISIONID"] = c.OldId(Divisions, r.DivisionId),
                },
                FromOld = (row, c, x) =>
                {
                    var active = Bool(row, "ISACTIVE");
                    var removal = Removal(active, x?.RemovedOn, x?.RemovedBy, x?.IsActive ?? true);
                    var status = Code(c, FeedbackStatuses.List, row, "FEEDBACKSTATUSID");
                    var hr = OldValues.Text(row, "HRACCEPTED");
                    return new Feedback
                    {
                        FeedbackGroup = Code(c, FeedbackGroups.List, row, "FEEDBACKGROUPID"), Status = status.Length > 0 ? status : x?.Status ?? FeedbackStatuses.New,
                        PersonResponsible = LoginOf(c, row, "PERSONRESPONSIBLE", x?.PersonResponsible), PersonResponsibleDivision = Text(row, "PERSONRESPONSIBLEDIVISION", 50),
                        DivisionId = c.LocalId(Divisions, Long(row, "FEEDBACKDIVISIONID")), CompanyName = Text(row, "COMPANYNAME", 500),
                        CompanyAddress = Text(row, "COMPANYADDRESS", 5000), PostalCode = Text(row, "POSTALCODE", 50), CompanyPhone = Text(row, "COMPANYPHONE", 20),
                        CompanyFax = Text(row, "COMPANYFAX", 20), CompanyEmail = Text(row, "COMPANYEMAIL", 250), ContactName = Text(row, "CONTACTNAME", 50),
                        MediumId = c.LocalId(Mediums, Long(row, "FEEDBACKMEDIUMID")), DateReceived = Date(row, "DATERECEIVED")?.Date, TimeReceived = Time(row, "TIMERECEIVED"),
                        FeedbackType = Code(c, FeedbackTypes.List, row, "FEEDBACKTYPEID"), CategoryId = c.LocalId(Categories, Long(row, "FEEDBACKCATEGORYID")),
                        Description = OldValues.Text(row, "FEEDBACKDESCRIPTION"), StaffResponsibleForProblem = LoginOf(c, row, "STAFFRESPONSIBLEFORPROBLEM", x?.StaffResponsibleForProblem),
                        Cc = OldValues.Text(row, "CC"), IsAcknowledged = Bool(row, "ISACKNOWLEDGED"), AcknowledgedType = Code(c, AcknowledgedTypes.List, row, "ACKNOWLEDGEDTYPEID"),
                        AcknowledgedDate = Date(row, "ACKNOWLEDGEDDATE")?.Date, AcknowledgedTime = Time(row, "ACKNOWLEDGEDTIME"),
                        SignOff = LoginOf(c, row, "SIGNOFF", x?.SignOff), FirstLookAtFeedback = OldValues.Text(row, "FIRSTLOOKATFEEDBACK"),
                        RootCause = OldValues.Text(row, "ROOTCAUSE"), CorrectiveAction = OldValues.Text(row, "CORRECTIVEACTION"),
                        StaffResponsibleForSolution = LoginOf(c, row, "STAFFRESPONSIBLEFORSOLUTION", x?.StaffResponsibleForSolution),
                        SatisfactionLevel = Code(c, SatisfactionLevels.List, row, "CUSTOMERSATISFACTIONLEVELID"), CreatedOn = Date(row, "CREATEDON"),
                        CreatedBy = LoginOf(c, row, "CREATEDBY", x?.CreatedBy), CreatedByUserName = Text(row, "CREATEDBYUSERNAME", 500), UpdatedOn = Date(row, "UPDATEDON"),
                        UpdatedBy = LoginOf(c, row, "UPDATEDBY", x?.UpdatedBy), UpdatedByUserName = Text(row, "UPDATEDBYUSERNAME", 500), IsActive = active,
                        RemovedOn = removal.On, RemovedBy = removal.By, SessionId = Text(row, "SESSIONID", 50), AcknowledgedHours = Int(row, "ACKNOWLEGEDHOURS"),
                        StartResolutionDays = Int(row, "STARTRESOLUTIONDAYS"), CloseCaseDays = Int(row, "CLOSECASEDAYS"), StartedResolutionOn = Date(row, "STARTEDRESOLUTIONON"),
                        CompletedResolutionOn = Date(row, "COMPLETEDRESOLUTIONON"), IsCompleted = Bool(row, "ISCOMPLETED"), CreatorHasSolution = Bool(row, "CREATORHASSOLUTION"),
                        SolutionDetail = OldValues.Text(row, "ESS_CREATORSOLUTIONDETAIL"), ExpectedOutcome = OldValues.Text(row, "ESS_EXPECTEDOUTCOME"),
                        SuggestionMeetCriteria = Text(row, "ESS_SUGGESTIONMEETCRITERIA", 500), RicohWayValue = Text(row, "ESS_RICOHWAYVALUE", 500),
                        HrAccepted = hr.Length > 0 ? Strings.Shorten(hr, 50) : HrAnswers.None, HrRemarks = OldValues.Text(row, "HRREMARKS"), HrUpdatedOn = Date(row, "HRUPDATEDON"),
                        HrUpdatedBy = LoginOf(c, row, "HRUPDATEDBY", x?.HrUpdatedBy), PreventiveAction = OldValues.Text(row, "PREVENTIVEACTION"),
                        MightCauseCustomerComplaint = Bool(row, "MIGHTCAUSECUSTOMERCOMPLAINT"), OutSystemsId = Long(row, "ID"),
                    };
                },
            },
            new SyncEntity<FeedbackTask>
            {
                Name = Tasks, Label = "task", OldTable = Tasks, OldCreatedOnColumn = "CREATEDON", OldUpdatedOnColumn = null,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                Parents = [(Feedbacks, r => r.FeedbackId)],
                OldParent = (Feedbacks, "feedback", "FEEDBACKID"),
                CanWrite = (r, c) => c.OldId(Feedbacks, r.FeedbackId) is not null,
                MarkRemovedHere = r => { r.IsActive = false; r.RemovedOn = clock(); r.RemovedBy = "MyCRM"; },
                Describe = r => $"{Strings.Shorten(r.ActionPlan, 60)} of feedback #{r.FeedbackId.ToString(CultureInfo.InvariantCulture)}",
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    // Removed here: detached from its feedback there (never deleted), the feedback kept in its session.
                    ["FEEDBACKID"] = r.IsActive ? c.OldId(Feedbacks, r.FeedbackId) : null, ["ACTIONPLAN"] = r.ActionPlan, ["REMARKS"] = r.Remarks,
                    ["PERSONINCHARGE"] = c.OldUser(r.PersonInCharge), ["DUEDATE"] = Day(r.DueDate), ["SLAEXPECTEDCOMPLETIONDATE"] = Day(r.SlaExpectedCompletionDate),
                    ["ISCOMPLETED"] = r.IsCompleted, ["CREATEDON"] = r.CreatedOn, ["CREATEDBY"] = c.OldUser(r.CreatedBy),
                    ["SESSIONID"] = r.IsActive ? r.SessionId : DetachedSession(c.OldId(Feedbacks, r.FeedbackId), r.SessionId),
                    ["TASKTYPEID"] = Static(c, TaskTypes.List, r.TaskType),
                },
                FromOld = (row, c, x) =>
                {
                    var removed = IsDetached(row, out var oldFeedback, out var session);
                    if (c.LocalId(Feedbacks, removed ? oldFeedback : Long(row, "FEEDBACKID")) is not { } feedback) return null;
                    var removal = Removal(!removed, x?.RemovedOn, x?.RemovedBy, x?.IsActive ?? true);
                    var type = Code(c, TaskTypes.List, row, "TASKTYPEID");
                    return new FeedbackTask
                    {
                        FeedbackId = feedback, ActionPlan = OldValues.Text(row, "ACTIONPLAN"), Remarks = Text(row, "REMARKS", 5000),
                        PersonInCharge = LoginOf(c, row, "PERSONINCHARGE", x?.PersonInCharge), DueDate = Date(row, "DUEDATE")?.Date,
                        SlaExpectedCompletionDate = Date(row, "SLAEXPECTEDCOMPLETIONDATE")?.Date, IsCompleted = Bool(row, "ISCOMPLETED"), CreatedOn = Date(row, "CREATEDON"),
                        CreatedBy = LoginOf(c, row, "CREATEDBY", x?.CreatedBy), SessionId = Strings.Shorten(session, 50),
                        TaskType = type.Length > 0 ? type : x?.TaskType ?? TaskTypes.Corrective, IsActive = !removed, RemovedOn = removal.On, RemovedBy = removal.By,
                        OutSystemsId = Long(row, "ID"),
                    };
                },
            },
            new SyncEntity<FeedbackFile>
            {
                Name = Attachments, Label = "attachment", OldTable = Attachments, OldCreatedOnColumn = "UPLOADEDON", OldUpdatedOnColumn = null,
                OldIdOf = r => r.OutSystemsId, SetOldId = (r, id) => r.OutSystemsId = id,
                Parents = [(Feedbacks, r => r.FeedbackId)],
                OldParent = (Feedbacks, "feedback", "FEEDBACKID"),
                CanWrite = (r, c) => c.OldId(Feedbacks, r.FeedbackId) is not null,
                MarkRemovedHere = r => { r.IsActive = false; r.RemovedOn = clock(); r.RemovedBy = "MyCRM"; },
                Describe = r => $"{r.FileName} of feedback #{r.FeedbackId.ToString(CultureInfo.InvariantCulture)}",
                ToOld = (r, c, _) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["FEEDBACKID"] = r.IsActive ? c.OldId(Feedbacks, r.FeedbackId) : null,
                    ["FILENAME"] = Strings.Shorten((r.IsActive ? "" : RemovedFile) + r.FileName, 500), ["FILEBINARY"] = r.Content, ["TYPE"] = r.Kind,
                    ["UPLOADEDBY"] = c.OldUser(r.UploadedBy), ["UPLOADEDON"] = r.UploadedOn,
                    ["SESSIONID"] = r.IsActive ? r.SessionId : DetachedSession(c.OldId(Feedbacks, r.FeedbackId), r.SessionId),
                },
                FromOld = (row, c, x) =>
                {
                    var removed = IsDetached(row, out var oldFeedback, out var session);
                    if (c.LocalId(Feedbacks, removed ? oldFeedback : Long(row, "FEEDBACKID")) is not { } feedback) return null;
                    var removal = Removal(!removed, x?.RemovedOn, x?.RemovedBy, x?.IsActive ?? true);
                    var name = OldValues.Text(row, "FILENAME");
                    if (removed && name.StartsWith(RemovedFile, StringComparison.Ordinal)) name = name.Substring(RemovedFile.Length);
                    // The rows of the sync hold the file's fingerprint: its bytes are read when they are not here yet, or changed.
                    var content = row.TryGetValue("FILEBINARY", out var value) && value is byte[] bytes ? bytes
                        : x is not null && Canonical(x.Content) == OldValues.Text(row, "FILEBINARY") ? x.Content
                        : CrmTables.Bytes(tables?.Invoke(), Long(row, "ID"));
                    return new FeedbackFile
                    {
                        FeedbackId = feedback, FileName = Strings.Shorten(name, 500), Content = content, Kind = Text(row, "TYPE", 50),
                        UploadedBy = LoginOf(c, row, "UPLOADEDBY", x?.UploadedBy), UploadedOn = Date(row, "UPLOADEDON"), SessionId = Strings.Shorten(session, 50),
                        IsActive = !removed, RemovedOn = removal.On, RemovedBy = removal.By, OutSystemsId = Long(row, "ID"),
                    };
                },
            },
        ];
    }
}

/// <summary>The columns of MyCRM's tables that MyCRMRecords reads and writes, for the connection test (Connections tab).</summary>
public static class OldTableColumns
{
    public static readonly string[] Choice = ["ID", "LABEL", "ORDER", "ISACTIVE"];
    public static readonly string[] Division = ["ID", "DIVISION", "PERSONINCHARGE", "ISACTIVE"];
    public static readonly string[] Feedback =
    [
        "ID", "FEEDBACKGROUPID", "PERSONRESPONSIBLE", "PERSONRESPONSIBLEDIVISION", "FEEDBACKSTATUSID", "COMPANYNAME", "COMPANYADDRESS", "POSTALCODE", "COMPANYPHONE",
        "COMPANYFAX", "COMPANYEMAIL", "CONTACTNAME", "FEEDBACKMEDIUMID", "DATERECEIVED", "TIMERECEIVED", "FEEDBACKTYPEID", "FEEDBACKCATEGORYID", "FEEDBACKDESCRIPTION",
        "STAFFRESPONSIBLEFORPROBLEM", "CC", "ISACKNOWLEDGED", "ACKNOWLEDGEDTYPEID", "ACKNOWLEDGEDDATE", "ACKNOWLEDGEDTIME", "SIGNOFF", "FIRSTLOOKATFEEDBACK", "ROOTCAUSE",
        "CORRECTIVEACTION", "STAFFRESPONSIBLEFORSOLUTION", "CUSTOMERSATISFACTIONLEVELID", "CREATEDON", "CREATEDBY", "CREATEDBYUSERNAME", "UPDATEDON", "UPDATEDBY",
        "UPDATEDBYUSERNAME", "ISACTIVE", "SESSIONID", "ACKNOWLEGEDHOURS", "STARTRESOLUTIONDAYS", "CLOSECASEDAYS", "STARTEDRESOLUTIONON", "COMPLETEDRESOLUTIONON",
        "ISCOMPLETED", "CREATORHASSOLUTION", "ESS_CREATORSOLUTIONDETAIL", "ESS_EXPECTEDOUTCOME", "ESS_SUGGESTIONMEETCRITERIA", "ESS_RICOHWAYVALUE", "HRACCEPTED", "HRREMARKS",
        "HRUPDATEDON", "HRUPDATEDBY", "PREVENTIVEACTION", "MIGHTCAUSECUSTOMERCOMPLAINT", "FEEDBACKDIVISIONID",
    ];
    public static readonly string[] Task =
        ["ID", "FEEDBACKID", "ACTIONPLAN", "REMARKS", "PERSONINCHARGE", "DUEDATE", "SLAEXPECTEDCOMPLETIONDATE", "ISCOMPLETED", "CREATEDON", "CREATEDBY", "SESSIONID", "TASKTYPEID"];
    public static readonly string[] Attachment = ["ID", "FEEDBACKID", "FILENAME", "FILEBINARY", "TYPE", "UPLOADEDBY", "UPLOADEDON", "SESSIONID"];
    public static readonly string[] Static = ["ID", "LABEL"];

    public static readonly IReadOnlyList<(string Entity, string[] Columns)> Checked =
    [
        (CrmSync.Categories, Choice), (CrmSync.Mediums, Choice), (CrmSync.Divisions, Division), (CrmSync.Feedbacks, Feedback), (CrmSync.Tasks, Task),
        (CrmSync.Attachments, Attachment), (FeedbackGroups.List.OldEntity, Static), (FeedbackStatuses.List.OldEntity, Static), (FeedbackTypes.List.OldEntity, Static),
        (AcknowledgedTypes.List.OldEntity, Static), (SatisfactionLevels.List.OldEntity, Static), (TaskTypes.List.OldEntity, Static),
    ];
}

/// <summary>
/// MyCRM's tables in the OutSystems database (<see cref="OutSystemsTables"/>), but an attachment's row gives its file's
/// fingerprint ("bytes:&lt;length&gt;:&lt;sha-256&gt;") rather than its bytes.
/// <para>The fingerprint is not worked out by SQL Server for every row at every sync (HASHBYTES over every file reads
/// the whole file store): the rows give each file's length (DATALENGTH, which does not read the file), and a file MyCRMRecords
/// keeps for that row with that length (<paramref name="knownFiles"/>: MyCRM's Id → fingerprint) is taken as it is (a
/// file replaced there by one of exactly the same length would not be seen). Only the others are hashed, a few at a time.</para>
/// </summary>
public sealed class CrmTables(ExternalDatabases databases, Func<string> module, IOldTables outSystems,
    Func<IReadOnlyDictionary<long, string>>? knownFiles = null) : IOldTables
{
    public string Title => "MyCRM";

    private static readonly string Fingerprint =
        "CASE WHEN [FILEBINARY] IS NULL OR DATALENGTH([FILEBINARY]) = 0 THEN N'' ELSE CONCAT(N'bytes:', DATALENGTH([FILEBINARY]), N':', " +
        "LOWER(CONVERT(varchar(64), HASHBYTES('SHA2_256', [FILEBINARY]), 2))) END AS [FILEBINARY]";

    private const string AttachmentColumns = "[ID], [FEEDBACKID], [FILENAME], [TYPE], [UPLOADEDBY], [UPLOADEDON], [SESSIONID], CAST(DATALENGTH([FILEBINARY]) AS bigint) AS [FILEBINARY]";

    /// <summary>How many files SQL Server was asked to hash (the tests check a sync does not hash the files it knows).</summary>
    public int FilesHashed { get; private set; }

    /// <summary>The length a fingerprint ("bytes:&lt;length&gt;:&lt;sha-256&gt;") names, or -1.</summary>
    private static long LengthOf(string fingerprint)
    {
        var parts = fingerprint.Split(':');
        return parts.Length == 3 && parts[0] == "bytes" && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) ? length : -1;
    }

    /// <summary>Turns each row's file length into the file's fingerprint: the one kept here when the length is the same, else SQL Server's.</summary>
    private IReadOnlyList<OldRow> Fingerprints(DbConnection connection, List<OldRow> rows)
    {
        var known = knownFiles?.Invoke() ?? new Dictionary<long, string>();
        var unknown = new List<OldRow>();
        foreach (var row in rows)
        {
            var length = row.TryGetValue("FILEBINARY", out var v) && v is not null and not DBNull ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : 0;
            if (length == 0) row["FILEBINARY"] = "";
            else if (known.TryGetValue(Long(row, "ID"), out var mine) && LengthOf(mine) == length) row["FILEBINARY"] = mine;
            else unknown.Add(row);
        }
        // The others, 20 files a query.
        foreach (var chunk in unknown.Select((r, i) => (r, i)).GroupBy(x => x.i / 20, x => x.r))
        {
            var byId = chunk.ToDictionary(r => Long(r, "ID"));
            foreach (var hashed in Sql.Rows(connection, null,
                         $"SELECT [ID], {Fingerprint} FROM {TableOf(connection)} WHERE [ID] IN ({string.Join(", ", byId.Keys.Select(id => id.ToString(CultureInfo.InvariantCulture)))})"))
                if (byId.TryGetValue(Long(hashed, "ID"), out var row)) row["FILEBINARY"] = OldValues.Text(hashed, "FILEBINARY");
            FilesHashed += byId.Count;
        }
        return rows;
    }

    /// <summary>How many rows the table has; MyCRM's attachments are counted without reading them (the import's "Read MyCRM's data").</summary>
    public int Count(string table) => !IsAttachments(table) ? Rows(table).Count
        : databases.Use(ExternalNames.OutSystems, connection =>
            Convert.ToInt32(Sql.Rows(connection, null, $"SELECT COUNT(*) AS N FROM {TableOf(connection)}")[0]["N"], CultureInfo.InvariantCulture));

    public IReadOnlyList<OldRow> Rows(string table, string? sinceColumn = null, DateTime? since = null)
    {
        if (!IsAttachments(table)) return outSystems.Rows(table, sinceColumn, since);
        return databases.Use(ExternalNames.OutSystems, connection => Fingerprints(connection, sinceColumn is null || since is null
            ? Sql.Rows(connection, null, $"SELECT {AttachmentColumns} FROM {TableOf(connection)} ORDER BY [ID]")
            : Sql.Rows(connection, null, $"SELECT {AttachmentColumns} FROM {TableOf(connection)} WHERE {Sql.Name(sinceColumn)} >= @since ORDER BY [ID]", ("@since", since.Value))));
    }

    public IReadOnlyList<OldRow> Rows(string table, string column, IReadOnlyCollection<long> values)
    {
        if (!IsAttachments(table)) return outSystems.Rows(table, column, values);
        return databases.Use(ExternalNames.OutSystems, connection =>
        {
            var rows = new List<OldRow>();
            foreach (var chunk in values.Distinct().Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => x.v))
                rows.AddRange(Sql.Rows(connection, null,
                    $"SELECT {AttachmentColumns} FROM {TableOf(connection)} WHERE {Sql.Name(column)} IN ({string.Join(", ", chunk.Select(v => v.ToString(CultureInfo.InvariantCulture)))}) ORDER BY [ID]"));
            return Fingerprints(connection, rows);
        });
    }

    public OldRow? Find(string table, IReadOnlyDictionary<string, object?> key) => outSystems.Find(table, key);
    public IReadOnlyDictionary<string, OldColumn> Columns(string table) => outSystems.Columns(table);
    public IReadOnlyDictionary<long, string> Labels(string table) => outSystems.Labels(table);
    public IReadOnlyList<OldUser> Users() => outSystems.Users();
    public IOldTablesWriter BeginWrite() => outSystems.BeginWrite();

    private static bool IsAttachments(string table) => string.Equals(table, CrmSync.Attachments, StringComparison.OrdinalIgnoreCase);

    private string TableOf(DbConnection connection) => databases.EntityTable(connection, module(), CrmSync.Attachments);

    /// <summary>The bytes of MyCRM's attachment <paramref name="id"/> (empty when it has none, or cannot be read).</summary>
    public static byte[] Bytes(IOldTables? tables, long id)
    {
        var row = tables?.Find(CrmSync.Attachments, new Dictionary<string, object?> { ["ID"] = id });
        return row is not null && row.TryGetValue("FILEBINARY", out var value) && value is byte[] bytes ? bytes : [];
    }
}
