using System.Globalization;
using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>
/// Sample records for trying MyCRMRecords out (demo tools on): a division, feedback categories and mediums when there are
/// none, and three feedbacks in charge of the person adding them: a new customer complaint, an acknowledged one with a
/// corrective task, and an internal challenge waiting for HR. Nothing is written to another database.
/// </summary>
public static class SeedData
{
    public const string SampleCompany = "Sample Holdings Pte Ltd";

    public static string Add(IRecordStore store, DateTime now, Actor actor)
    {
        if (store.Count<Feedback>(Query.Where("CompanyName", SampleCompany)) > 0) return "The sample records are there already.";
        var login = Strings.Shorten(actor.Login, 256);
        if (store.Count<FeedbackCategory>() == 0)
            foreach (var (label, i) in new[] { "Service", "Billing", "Delivery", "Product", "Others" }.Select((l, i) => (l, i)))
                store.Insert(new FeedbackCategory { Label = label, SortOrder = i + 1 });
        if (store.Count<FeedbackMedium>() == 0)
            foreach (var (label, i) in new[] { "Email", "Phone", "Face to face", "Letter" }.Select((l, i) => (l, i)))
                store.Insert(new FeedbackMedium { Label = label, SortOrder = i + 1 });
        var division = store.FindFirst<FeedbackDivision>(Query.Where("IsActive", true))
                       ?? store.Insert(new FeedbackDivision { Name = "Sample Service Division", PersonInCharge = login });
        var category = store.Find<FeedbackCategory>(Query.Where("Removed", false)).OrderBy(c => c.SortOrder).First();
        var medium = store.Find<FeedbackMedium>(Query.Where("Removed", false)).OrderBy(c => c.SortOrder).First();
        Feedback Make(string group, string status, int daysAgo, string description) => store.Insert(new Feedback
        {
            FeedbackGroup = group, Status = status, PersonResponsible = login, DivisionId = division.Id, CompanyName = SampleCompany, ContactName = "Casey Customer",
            CompanyEmail = "casey@example.com", MediumId = medium.Id, DateReceived = now.Date.AddDays(-daysAgo), TimeReceived = "09:15:00",
            FeedbackType = FeedbackTypes.Normal, CategoryId = category.Id, Description = description, CreatedOn = now.AddDays(-daysAgo), CreatedBy = login,
            CreatedByUserName = actor.DisplayName, UpdatedOn = now, UpdatedBy = login, UpdatedByUserName = actor.DisplayName,
            SessionId = now.AddDays(-daysAgo).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        });
        var fresh = Make(FeedbackGroups.CustomerComplaint, FeedbackStatuses.New, 0, "The copier on level 3 jams every morning (sample).");
        var acknowledged = Make(FeedbackGroups.CustomerComplaint, FeedbackStatuses.Acknowledged, 3, "The invoice was sent to the wrong department (sample).");
        acknowledged.IsAcknowledged = true;
        acknowledged.AcknowledgedDate = now.Date.AddDays(-2);
        acknowledged.AcknowledgedTime = "10:00:00";
        acknowledged.SignOff = login;
        acknowledged.AcknowledgedHours = 24;
        acknowledged.FirstLookAtFeedback = "The billing contact changed last month (sample).";
        store.Update(acknowledged);
        store.Insert(new FeedbackTask
        {
            FeedbackId = acknowledged.Id, ActionPlan = "Update the billing contact and send the invoice again (sample).", PersonInCharge = login,
            DueDate = now.Date.AddDays(2), CreatedOn = now.AddDays(-1), CreatedBy = login, SessionId = acknowledged.SessionId, TaskType = TaskTypes.Corrective,
        });
        var internalOne = Make(FeedbackGroups.InternalChallenges, FeedbackStatuses.PendingHr, 1, "Service reports are typed twice (sample).");
        internalOne.CompanyName = "";
        internalOne.CreatorHasSolution = true;
        internalOne.SolutionDetail = "Send the service report from the tablet straight to the system (sample).";
        internalOne.ExpectedOutcome = "Half an hour saved for each engineer every day (sample).";
        internalOne.SuggestionMeetCriteria = "|Cost Saving||Process Improvement|";
        internalOne.RicohWayValue = "|Customer-Centric||Innovation|";
        store.Update(internalOne);
        return $"Sample records added by {actor.ChangedBy}: feedbacks #{fresh.Id.ToString(CultureInfo.InvariantCulture)}, #{acknowledged.Id.ToString(CultureInfo.InvariantCulture)} and #{internalOne.Id.ToString(CultureInfo.InvariantCulture)}.";
    }
}
