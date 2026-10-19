namespace MyCRMRecords.Domain;

/// <summary>
/// One choice of a list that is a static entity in MyCRM: its code here, its label on the pages, and the labels MyCRM
/// may have for it (the sync matches MyCRM's records by label, never by Id).
/// </summary>
public sealed record Choice(string Code, string Label, params string[] OldLabels)
{
    public IEnumerable<string> Labels => new[] { Label }.Concat(OldLabels);
}

/// <summary>A static list: its choices in their order, and the MyCRM entity it is.</summary>
public sealed class ChoiceList(string oldEntity, params Choice[] choices)
{
    public string OldEntity { get; } = oldEntity;
    public IReadOnlyList<Choice> All { get; } = choices;

    public bool IsKnown(string? code) => All.Any(c => c.Code == code);

    /// <summary>The label of a code ("" for none, the code itself when unknown).</summary>
    public string Label(string? code) => string.IsNullOrEmpty(code) ? "" : All.FirstOrDefault(c => c.Code == code)?.Label ?? code!;

    /// <summary>The code whose label (or one of MyCRM's labels) matches, letters and digits only, case ignored; "" when none.</summary>
    public string FromLabel(string? label)
    {
        var key = Key(label ?? "");
        if (key.Length == 0) return "";
        return All.FirstOrDefault(c => c.Labels.Any(l => Key(l) == key))?.Code ?? "";
    }

    public static string Key(string text) => new string(text.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}

/// <summary>What a feedback is about (MyCRM's FeedbackGroup, "Feedback Category" on the pages).</summary>
public static class FeedbackGroups
{
    public const string CustomerComplaint = "CustomerComplaint";
    public const string PotentialCustomerComplaint = "PotentialCustomerComplaint";
    public const string InternalChallenges = "InternalChallenges";
    public const string AreaOfImprovement = "AreaOfImprovement";

    public static readonly ChoiceList List = new("FeedbackGroup",
        new Choice(CustomerComplaint, "Customer Complaint"),
        new Choice(PotentialCustomerComplaint, "Potential Customer Complaint"),
        new Choice(InternalChallenges, "Internal Challenges"),
        new Choice(AreaOfImprovement, "Area of Improvement"));

    /// <summary>The Customer Details section (and its checks): customer complaints, areas of improvement, or no category yet.</summary>
    public static bool HasCustomerDetails(string group) => group is CustomerComplaint or AreaOfImprovement or "";

    /// <summary>The HR - ESS section: internal challenges, or no category yet.</summary>
    public static bool HasHr(string group) => group is InternalChallenges or "";
}

/// <summary>A feedback's status (MyCRM's FeedbackStatus), in the order the list counts them.</summary>
public static class FeedbackStatuses
{
    public const string PendingHr = "PendingHR";
    public const string New = "New";
    public const string Acknowledged = "Acknowledged";
    public const string Investigation = "InvestigationResolution";
    public const string ConfirmedSatisfaction = "ConfirmedSatisfaction";
    public const string Closed = "Closed";

    public static readonly ChoiceList List = new("FeedbackStatus",
        new Choice(New, "New"),
        new Choice(PendingHr, "Pending HR"),
        new Choice(Acknowledged, "Acknowledged"),
        new Choice(Investigation, "Investigation / Resolution"),
        new Choice(ConfirmedSatisfaction, "Confirmed Satisfaction", "Confirmed Statisfaction"),
        new Choice(Closed, "Closed"));
}

/// <summary>How serious a feedback is (MyCRM's FeedbackType: its identifiers and labels differ, the labels are what count).</summary>
public static class FeedbackTypes
{
    public const string VerySerious = "VerySerious";
    public const string Serious = "Serious";
    public const string Normal = "Normal";
    public const string Improvement = "Improvement";

    public static readonly ChoiceList List = new("FeedbackType",
        new Choice(VerySerious, "Very Serious Complaint"),
        new Choice(Serious, "Serious Complaint"),
        new Choice(Normal, "Normal Complaint"),
        new Choice(Improvement, "Feedback / Improvement"));

    /// <summary>The help MyCRM shows next to the Feedback Type.</summary>
    public const string Help =
        "Very Serious Complaint:\n1. Cause financial lost to Ricoh\n2. Customer's business affected and cause them financial lost and / or decrease productivity.\n" +
        "3. Complaint will cause bad reputation to Ricoh publicly\n4. Breach of contract between Ricoh and customer\n5. Reoccurance more than 3 times from the same customer\n" +
        "6. Violation of company policy / regulations\n\nSerious Complaint:\n1. Customer's business affected and the productivity decreased (no cost involved)\n" +
        "2. Complaint will cause bad reputation to Ricoh internally\n3. Reoccurance more than 1 time from the same customer\n4. Affect the relationship between Ricoh and Customer\n\n" +
        "Normal Complaint:\n1. Cause inconvenience to customer without affecting their cost or productivity\n2. Minor product failure that require simple troubleshoot";
}

/// <summary>How a feedback was acknowledged (MyCRM's AcknowledgedType; no page sets it, it is kept and synced).</summary>
public static class AcknowledgedTypes
{
    public static readonly ChoiceList List = new("AcknowledgedType",
        new Choice("Email", "Email"),
        new Choice("FaceToFace", "Face To Face"),
        new Choice("Fax", "Fax"),
        new Choice("Internet", "Internet"),
        new Choice("Phone", "Phone"),
        new Choice("PostalMail", "Postal Mail"),
        new Choice("TextMessage", "Text Message"));
}

/// <summary>The customer's (or employee's) satisfaction (MyCRM's CustomerSatisfactionLevel).</summary>
public static class SatisfactionLevels
{
    public static readonly ChoiceList List = new("CustomerSatisfactionLevel",
        new Choice("NoAcceptance", "No Acceptance (e.g. Abituary / Legal)"),
        new Choice("Accepted", "Customer Accepted Solution"),
        new Choice("Appreciated", "Customer Accepted Solution and Expressed Appreciation"));
}

/// <summary>A task's kind (MyCRM's TaskType).</summary>
public static class TaskTypes
{
    public const string Corrective = "Corrective";
    public const string Preventive = "Preventive";

    public static readonly ChoiceList List = new("TaskType",
        new Choice(Corrective, "Corrective Action"),
        new Choice(Preventive, "Preventive Action"));
}

/// <summary>Where a file is kept on a feedback: MyCRM's FeedbackAttachment.Type.</summary>
public static class FileKinds
{
    /// <summary>The Customer Details section's files (uploaded and removed by Feedback Admins, as in MyCRM).</summary>
    public const string CustomerDetails = "CD";
    /// <summary>The satisfaction section's files (anyone).</summary>
    public const string Satisfaction = "CS";

    public static bool IsKnown(string kind) => kind is CustomerDetails or Satisfaction;
}

/// <summary>HR's answer to an internal challenge's suggestion (MyCRM's HRAccepted).</summary>
public static class HrAnswers
{
    public const string None = "-";
    public const string Yes = "Yes";
    public const string No = "No";
    public static readonly string[] All = [None, Yes, No];
}

/// <summary>
/// The tick boxes of an internal challenge's suggestion, kept as MyCRM keeps them: each ticked value between bars, in
/// this order ("|Ricoh Way Value||Cost Saving|").
/// </summary>
public static class EssChoices
{
    /// <summary>"Does your suggestion meet any of the following criteria?": label, and the value kept.</summary>
    public static readonly IReadOnlyList<(string Label, string Value)> Criteria =
        [("Ricoh Way Value", "Ricoh Way Value"), ("Cost Savings", "Cost Saving"), ("Process Improvement", "Process Improvement")];

    /// <summary>"Which Ricoh Way value best represents your suggestions?"</summary>
    public static readonly IReadOnlyList<(string Label, string Value)> RicohWayValues =
    [
        ("Customer-Centric", "Customer-Centric"), ("Passion", "Passion"), ("Gemba (Learn from the frontlines)", "Gemba (Learn from the frontlines)"),
        ("Innovation", "Innovation"), ("Teamwork", "Teamwork"), ("Winning Spirit", "Winning Spirit"), ("Ethics, and Integrity", "Ethics, and Integrity"),
    ];

    /// <summary>The values ticked in a kept text.</summary>
    public static IReadOnlyList<string> Parse(IReadOnlyList<(string Label, string Value)> choices, string? text) =>
        choices.Where(c => (text ?? "").IndexOf("|" + c.Value + "|", StringComparison.Ordinal) >= 0).Select(c => c.Value).ToList();

    /// <summary>The text to keep for the ticked values (unknown ones left out), in the list's order.</summary>
    public static string Join(IReadOnlyList<(string Label, string Value)> choices, IEnumerable<string>? ticked)
    {
        var set = new HashSet<string>(ticked ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        return string.Concat(choices.Where(c => set.Contains(c.Value)).Select(c => "|" + c.Value + "|"));
    }
}
