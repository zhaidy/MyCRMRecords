using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

// MyCRMRecords's tables (dbo.Cu…), one row per record. People are kept as logins (RSP\jsmith) with their name next to them,
// not as OutSystems user ids. OutSystemsId is the record's Id in MyCRM when it came from there (imported or synced).

/// <summary>An email sent (or not, and why).</summary>
[Table("dbo.CrEmail")]
public sealed class EmailMessage : IRecord
{
    public long Id { get; set; }
    [Indexed] public DateTime At { get; set; }
    [Len(20)] public string RequestKind { get; set; } = "";
    public long RequestId { get; set; }
    [Len(4000)] public string To { get; set; } = "";
    [Len(4000)] public string Cc { get; set; } = "";
    [Len(500)] public string Subject { get; set; } = "";
    public bool Sent { get; set; }
    [Len(4000)] public string Error { get; set; } = "";
}

/// <summary>One run of a scheduled task (or of its Run now button), or of the import.</summary>
[Table("dbo.CrJobRun")]
public sealed class JobRun : IRecord
{
    public long Id { get; set; }
    [Indexed, Len(50)] public string Job { get; set; } = "";
    [Indexed] public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    [Len(20)] public string Outcome { get; set; } = "";
    [Len(600)] public string By { get; set; } = "";
    public int Processed { get; set; }
    [Len(int.MaxValue)] public string Summary { get; set; } = "";
    public DateTime? Watermark { get; set; }
}

/// <summary>Someone who has opened MyCRMRecords (or was given a role).</summary>
[Table("dbo.CrUser")]
public sealed class AppUser : IRecord
{
    public long Id { get; set; }
    [Indexed, Len(256)] public string Login { get; set; } = "";
    [Len(256)] public string DisplayName { get; set; } = "";
    [Len(256)] public string Email { get; set; } = "";
    public DateTime? LastSeenAt { get; set; }
}

[Table("dbo.CrUserRole")]
public sealed class UserRole : IRecord
{
    public long Id { get; set; }
    [Indexed, Len(256)] public string Login { get; set; } = "";
    [Len(50)] public string Role { get; set; } = "";
    /// <summary>False once the role was taken away (records are never deleted): when and by whom.</summary>
    public bool IsActive { get; set; } = true;
    public DateTime? RemovedOn { get; set; }
    [Len(600)] public string RemovedBy { get; set; } = "";
}

/// <summary>An action through MyCRMRecords (every change and sign-in), for the audit log.</summary>
[Table("dbo.CrAudit")]
public sealed class AuditEntry : IRecord
{
    public long Id { get; set; }
    [Indexed] public DateTime At { get; set; }
    [Len(256)] public string Login { get; set; } = "";
    [Len(256)] public string DisplayName { get; set; } = "";
    [Len(256)] public string ImpersonatorLogin { get; set; } = "";
    [Len(256)] public string ImpersonatorName { get; set; } = "";
    [Len(200)] public string Action { get; set; } = "";
    [Len(10)] public string Method { get; set; } = "";
    [Len(500)] public string Path { get; set; } = "";
    [Len(500)] public string Target { get; set; } = "";
    public int Status { get; set; }
    public bool Succeeded { get; set; }
    [Len(4000)] public string Message { get; set; } = "";
    [Len(int.MaxValue)] public string Details { get; set; } = "";
    [Len(100)] public string Address { get; set; } = "";
}

/// <summary>
/// A record paired with its row in MyCRM by the sync (OldAppSync), with both sides' fingerprints as they were
/// at the last sync. RemovedHere: deleted here, marked in MyCRM (LocalId is negative for a number taken there
/// but not used). RemovedThere: deleted in MyCRM, marked here. Pending: written to MyCRM, not yet
/// confirmed. Unpaired: no longer a pair.
/// </summary>
[Table("dbo.CrSyncLink")]
public sealed class SyncLink : IRecord
{
    public long Id { get; set; }
    [Indexed, Len(50)] public string Entity { get; set; } = "";
    [Indexed] public long LocalId { get; set; }
    [Indexed, Len(400)] public string OldKey { get; set; } = "";
    [Len(64)] public string LocalHash { get; set; } = "";
    [Len(64)] public string OldHash { get; set; } = "";
    public bool RemovedHere { get; set; }
    public bool RemovedThere { get; set; }
    public bool Pending { get; set; }
    public DateTime SyncedOn { get; set; }
    /// <summary>No longer a pair (one side was replaced, or the row's insert never reached the old app); kept, never deleted.</summary>
    public bool Unpaired { get; set; }
}

/// <summary>A sync with MyCRM: when, who started it, what it did.</summary>
[Table("dbo.CrSyncRun")]
public sealed class SyncRun : IRecord
{
    public long Id { get; set; }
    [Indexed] public DateTime StartedOn { get; set; }
    public DateTime? FinishedOn { get; set; }
    [Len(256)] public string By { get; set; } = "";
    public bool Ok { get; set; }
    [Len(1000)] public string Message { get; set; } = "";
    public int ToOldApp { get; set; }
    public int ToHere { get; set; }
    [Len(int.MaxValue)] public string Details { get; set; } = "";
}

/// <summary>A value the sync wrote in MyCRM, with the value it replaced, so any change can be put back by hand.</summary>
[Table("dbo.CrSyncChange")]
public sealed class SyncChange : IRecord
{
    public long Id { get; set; }
    [Indexed] public long RunId { get; set; }
    [Indexed] public DateTime ChangedOn { get; set; }
    [Len(100)] public string OldTable { get; set; } = "";
    [Indexed, Len(400)] public string OldKey { get; set; } = "";
    [Len(200)] public string Record { get; set; } = "";
    [Len(20)] public string Action { get; set; } = "";
    [Len(128)] public string Column { get; set; } = "";
    [Len(int.MaxValue)] public string Before { get; set; } = "";
    [Len(int.MaxValue)] public string After { get; set; } = "";
}

// ---- MyCRM's records ----

/// <summary>
/// A feedback category (MyCRM's FeedbackCategory). <see cref="IsActive"/> is MyCRM's own "Is Active" (offered on new
/// feedbacks); one taken away is <see cref="Removed"/> (MyCRM deleted it; MyCRMRecords marks it, and sets ISACTIVE 0 there).
/// </summary>
[Table("dbo.CrFeedbackCategory")]
public sealed class FeedbackCategory : IRecord
{
    public long Id { get; set; }
    [Len(50)] public string Label { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool Removed { get; set; }
    public DateTime? RemovedOn { get; set; }
    [Len(600)] public string RemovedBy { get; set; } = "";
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>How a feedback was received (MyCRM's FeedbackMedium), kept like <see cref="FeedbackCategory"/>.</summary>
[Table("dbo.CrFeedbackMedium")]
public sealed class FeedbackMedium : IRecord
{
    public long Id { get; set; }
    [Len(50)] public string Label { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool Removed { get; set; }
    public DateTime? RemovedOn { get; set; }
    [Len(600)] public string RemovedBy { get; set; } = "";
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>
/// A division a feedback is for, with its person in charge (MyCRM's FeedbackDivision). One taken away is inactive, as
/// in MyCRM.
/// </summary>
[Table("dbo.CrFeedbackDivision")]
public sealed class FeedbackDivision : IRecord
{
    public long Id { get; set; }
    [Len(50)] public string Name { get; set; } = "";
    /// <summary>The login of the person who becomes in charge of the division's feedbacks.</summary>
    [Len(256)] public string PersonInCharge { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime? RemovedOn { get; set; }
    [Len(600)] public string RemovedBy { get; set; } = "";
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>
/// A feedback (MyCRM's Feedback): a customer complaint, a potential one, an internal challenge or an area of
/// improvement, from its receipt to its closing. Its number (Id) is MyCRM's while the sync is on. Deleted, it is
/// inactive (MyCRM's own mark). People are logins; the times of day are "HH:mm" ("" none).
/// </summary>
[Table("dbo.CrFeedback")]
public sealed class Feedback : IRecord
{
    public long Id { get; set; }
    /// <summary>MyCRM's FeedbackGroup ("Feedback Category" on the page): <see cref="FeedbackGroups"/>.</summary>
    [Len(50)] public string FeedbackGroup { get; set; } = "";
    /// <summary><see cref="FeedbackStatuses"/>.</summary>
    [Indexed, Len(50)] public string Status { get; set; } = FeedbackStatuses.New;
    /// <summary>The person in charge ("CMS in Charge").</summary>
    [Indexed, Len(256)] public string PersonResponsible { get; set; } = "";
    /// <summary>The division as text, as MyCRM kept it once (old feedbacks); new ones have <see cref="DivisionId"/>.</summary>
    [Len(50)] public string PersonResponsibleDivision { get; set; } = "";
    public long? DivisionId { get; set; }
    [Len(500)] public string CompanyName { get; set; } = "";
    [Len(5000)] public string CompanyAddress { get; set; } = "";
    [Len(50)] public string PostalCode { get; set; } = "";
    [Len(20)] public string CompanyPhone { get; set; } = "";
    [Len(20)] public string CompanyFax { get; set; } = "";
    /// <summary>"Contact Email" on the page.</summary>
    [Len(250)] public string CompanyEmail { get; set; } = "";
    [Len(50)] public string ContactName { get; set; } = "";
    public long? MediumId { get; set; }
    public DateTime? DateReceived { get; set; }
    [Len(8)] public string TimeReceived { get; set; } = "";
    /// <summary><see cref="FeedbackTypes"/>.</summary>
    [Len(50)] public string FeedbackType { get; set; } = "";
    public long? CategoryId { get; set; }
    [Len(int.MaxValue)] public string Description { get; set; } = "";
    /// <summary>MyCRM's StaffResponsibleForProblem (no page sets it).</summary>
    [Len(256)] public string StaffResponsibleForProblem { get; set; } = "";
    /// <summary>"Copy To": email addresses separated by commas.</summary>
    [Len(int.MaxValue)] public string Cc { get; set; } = "";
    public bool IsAcknowledged { get; set; }
    /// <summary><see cref="AcknowledgedTypes"/> (no page sets it).</summary>
    [Len(50)] public string AcknowledgedType { get; set; } = "";
    public DateTime? AcknowledgedDate { get; set; }
    [Len(8)] public string AcknowledgedTime { get; set; } = "";
    [Len(256)] public string SignOff { get; set; } = "";
    [Len(int.MaxValue)] public string FirstLookAtFeedback { get; set; } = "";
    [Len(int.MaxValue)] public string RootCause { get; set; } = "";
    /// <summary>MyCRM's CorrectiveAction text (no page sets it).</summary>
    [Len(int.MaxValue)] public string CorrectiveAction { get; set; } = "";
    /// <summary>"Staff Responsible": who wrote the root cause.</summary>
    [Len(256)] public string StaffResponsibleForSolution { get; set; } = "";
    /// <summary><see cref="SatisfactionLevels"/>.</summary>
    [Len(50)] public string SatisfactionLevel { get; set; } = "";
    public DateTime? CreatedOn { get; set; }
    [Indexed, Len(256)] public string CreatedBy { get; set; } = "";
    [Len(500)] public string CreatedByUserName { get; set; } = "";
    public DateTime? UpdatedOn { get; set; }
    [Len(256)] public string UpdatedBy { get; set; } = "";
    [Len(500)] public string UpdatedByUserName { get; set; } = "";
    /// <summary>False once deleted (MyCRM's own mark), with when and by whom here.</summary>
    public bool IsActive { get; set; } = true;
    public DateTime? RemovedOn { get; set; }
    [Len(600)] public string RemovedBy { get; set; } = "";
    /// <summary>MyCRM's SessionId: when the feedback was started, as text (MyCRM ties tasks and files to it).</summary>
    [Len(50)] public string SessionId { get; set; } = "";
    /// <summary>The hours it took to acknowledge, less the weekends and public holidays (MyCRM's AcknowlegedHours).</summary>
    public int AcknowledgedHours { get; set; }
    public int StartResolutionDays { get; set; }
    public int CloseCaseDays { get; set; }
    public DateTime? StartedResolutionOn { get; set; }
    public DateTime? CompletedResolutionOn { get; set; }
    public bool IsCompleted { get; set; }
    /// <summary>Internal challenges (ESS): "Do you have any suggestion / solution?" and its answers.</summary>
    public bool CreatorHasSolution { get; set; }
    [Len(int.MaxValue)] public string SolutionDetail { get; set; } = "";
    [Len(int.MaxValue)] public string ExpectedOutcome { get; set; } = "";
    /// <summary>"|Ricoh Way Value||Cost Saving||Process Improvement|", as MyCRM keeps it (<see cref="EssChoices"/>).</summary>
    [Len(500)] public string SuggestionMeetCriteria { get; set; } = "";
    [Len(500)] public string RicohWayValue { get; set; } = "";
    /// <summary>HR's answer: "-" (not yet), "Yes" or "No".</summary>
    [Len(50)] public string HrAccepted { get; set; } = HrAnswers.None;
    [Len(int.MaxValue)] public string HrRemarks { get; set; } = "";
    public DateTime? HrUpdatedOn { get; set; }
    [Len(256)] public string HrUpdatedBy { get; set; } = "";
    /// <summary>MyCRM's PreventiveAction text (no page sets it).</summary>
    [Len(int.MaxValue)] public string PreventiveAction { get; set; } = "";
    public bool MightCauseCustomerComplaint { get; set; }
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>A corrective or preventive action of a feedback (MyCRM's Task). Removed, it is inactive (MyCRM deleted it).</summary>
[Table("dbo.CrTask")]
public sealed class FeedbackTask : IRecord
{
    public long Id { get; set; }
    [Indexed] public long FeedbackId { get; set; }
    [Len(int.MaxValue)] public string ActionPlan { get; set; } = "";
    [Len(5000)] public string Remarks { get; set; } = "";
    [Indexed, Len(256)] public string PersonInCharge { get; set; } = "";
    public DateTime? DueDate { get; set; }
    /// <summary>MyCRM's SLAExpectedCompletionDate (never set; the page works it out from the feedback).</summary>
    public DateTime? SlaExpectedCompletionDate { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    [Len(256)] public string CreatedBy { get; set; } = "";
    [Len(50)] public string SessionId { get; set; } = "";
    /// <summary><see cref="TaskTypes"/>.</summary>
    [Len(50)] public string TaskType { get; set; } = TaskTypes.Corrective;
    public bool IsActive { get; set; } = true;
    public DateTime? RemovedOn { get; set; }
    [Len(600)] public string RemovedBy { get; set; } = "";
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>
/// A file kept with a feedback (MyCRM's FeedbackAttachment): <see cref="FileKinds.CustomerDetails"/> or
/// <see cref="FileKinds.Satisfaction"/>. Removed, it is inactive (MyCRM deleted it).
/// </summary>
[Table("dbo.CrFeedbackFile")]
public sealed class FeedbackFile : IRecord
{
    public long Id { get; set; }
    [Indexed] public long FeedbackId { get; set; }
    [Len(500)] public string FileName { get; set; } = "";
    public byte[] Content { get; set; } = [];
    [Len(50)] public string Kind { get; set; } = FileKinds.CustomerDetails;
    [Len(256)] public string UploadedBy { get; set; } = "";
    public DateTime? UploadedOn { get; set; }
    [Len(50)] public string SessionId { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime? RemovedOn { get; set; }
    [Len(600)] public string RemovedBy { get; set; } = "";
    [Indexed] public long? OutSystemsId { get; set; }
}

/// <summary>Every table, in the order the migrations create them.</summary>
public static class Tables
{
    public static readonly IReadOnlyList<Type> All =
    [
        typeof(EmailMessage), typeof(JobRun), typeof(AppUser), typeof(UserRole), typeof(AuditEntry), typeof(SyncLink), typeof(SyncRun), typeof(SyncChange),
        typeof(FeedbackCategory), typeof(FeedbackMedium), typeof(FeedbackDivision), typeof(Feedback), typeof(FeedbackTask), typeof(FeedbackFile),
    ];
}
