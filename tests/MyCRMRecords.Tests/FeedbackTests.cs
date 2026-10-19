namespace MyCRMRecords.Tests;

/// <summary>
/// MyCRM's rules as MyCRMRecords keeps them: the checks of Save, the statuses (StatusHandler), the timings (UpdateTiming)
/// with weekends and public holidays left out, who may change what, who sees which feedback, the emails, and the
/// statistics.
/// </summary>
public sealed class FeedbackTests
{
    /// <summary>The input that saves a feedback as it is.</summary>
    public static Feedbacks.FeedbackInput InputOf(TestWorld world, long id)
    {
        var f = world.D.Feedbacks.Get(id)!;
        return new Feedbacks.FeedbackInput
        {
            FeedbackGroup = f.FeedbackGroup, DivisionId = f.DivisionId, PersonResponsible = f.PersonResponsible, CompanyName = f.CompanyName, ContactName = f.ContactName,
            CompanyEmail = f.CompanyEmail, MediumId = f.MediumId, DateReceived = f.DateReceived, TimeReceived = f.TimeReceived, FeedbackType = f.FeedbackType,
            CategoryId = f.CategoryId, Description = f.Description, Cc = Strings.Emails(f.Cc).ToList(), MightCauseCustomerComplaint = f.MightCauseCustomerComplaint,
            CreatorHasSolution = f.CreatorHasSolution, SolutionDetail = f.SolutionDetail, ExpectedOutcome = f.ExpectedOutcome,
            SuggestionMeetCriteria = EssChoices.Parse(EssChoices.Criteria, f.SuggestionMeetCriteria).ToList(),
            RicohWayValue = EssChoices.Parse(EssChoices.RicohWayValues, f.RicohWayValue).ToList(), HrAccepted = f.HrAccepted, HrRemarks = f.HrRemarks,
            IsAcknowledged = f.IsAcknowledged, FirstLookAtFeedback = f.FirstLookAtFeedback, RootCause = f.RootCause, SatisfactionLevel = f.SatisfactionLevel,
        };
    }

    private static long NewComplaint(TestWorld world, ApiUser? by = null)
    {
        var lists = world.Store.Count<FeedbackDivision>() == 0 ? SyncTests.Lists(world)
            : (world.Store.Find<FeedbackDivision>()[0].Id, world.Store.Find<FeedbackCategory>()[0].Id, world.Store.Find<FeedbackMedium>()[0].Id);
        var made = world.D.Feedbacks.Save(world.Actor(by ?? TestWorld.Olga), 0, SyncTests.Complaint(lists));
        Assert.True(made.Ok, made.Message);
        return made.Id!.Value;
    }

    private static OperationResult Change(TestWorld world, ApiUser user, long id, Action<Feedbacks.FeedbackInput> change)
    {
        var input = InputOf(world, id);
        change(input);
        return world.D.Feedbacks.Save(world.Actor(user), id, input);
    }

    [Fact]
    public void A_new_feedback_is_New_and_tells_its_category_recipients_and_the_person_in_charge()
    {
        using var world = new TestWorld();
        var id = NewComplaint(world);
        var f = world.D.Feedbacks.Get(id)!;
        Assert.Equal(FeedbackStatuses.New, f.Status);
        Assert.Equal(TestWorld.Olga.Login, f.CreatedBy);
        Assert.Equal("Olga Other", f.CreatedByUserName);
        Assert.Equal("2026-10-17 09:30:00", f.SessionId);
        Assert.Equal("09:00:00", f.TimeReceived);
        var notify = world.Email.Sent.Single(e => e.Subject == "MyCRMRecords - Feedback Created by Olga Other");
        Assert.Equal(new[] { "rspbe@ricoh.sg", "rayeyeo@ricoh.sg", "joannechen@ricoh.sg" }, notify.To);
        var mine = world.Email.Sent.Single(e => e.Subject == $"MyCRMRecords - Feedback #{id} - New - Tan & Sons[RESTRICTED]");
        Assert.Equal(new[] { "sam@example.com" }, mine.To);
        Assert.Equal(new[] { "hana@example.com" }, mine.Cc);
        Assert.Contains("feedback.aspx?id=" + id, mine.Html);
    }

    [Fact]
    public void An_internal_challenge_is_told_to_HR()
    {
        using var world = new TestWorld(s => s.Email.InternalChallengesNotify = "hr@example.com");
        var lists = SyncTests.Lists(world);
        var made = world.D.Feedbacks.Save(world.Actor(TestWorld.Olga), 0, new Feedbacks.FeedbackInput
        {
            FeedbackGroup = FeedbackGroups.InternalChallenges, PersonResponsible = TestWorld.Sam.Login, FeedbackType = FeedbackTypes.Normal, CategoryId = lists.Category,
            Description = "Too many forms",
        });
        Assert.True(made.Ok, made.Message);
        Assert.Equal(new[] { "hr@example.com" }, world.Email.Sent.Single(e => e.Subject.StartsWith("MyCRMRecords - Feedback Created", StringComparison.Ordinal)).To);
        // Without a suggestion it does not wait for HR.
        Assert.Equal(FeedbackStatuses.New, world.D.Feedbacks.Get(made.Id!.Value)!.Status);
    }

    [Fact]
    public void Save_checks_what_MyCRM_checked_in_its_order()
    {
        using var world = new TestWorld();
        var lists = SyncTests.Lists(world);
        var olga = world.Actor(TestWorld.Olga);
        var input = SyncTests.Complaint(lists);
        input.CompanyEmail = "not an email";
        var bad = world.D.Feedbacks.Save(olga, 0, input);
        Assert.False(bad.Ok);
        Assert.Contains(bad.Errors, e => e.Field == "companyEmail" && e.Message == "Invalid Email Address");

        input = SyncTests.Complaint(lists);
        input.ContactName = "";
        input.MediumId = null;
        var missing = world.D.Feedbacks.Save(olga, 0, input);
        Assert.Equal("Please fill in customer details", missing.Message);
        Assert.Equal(new[] { "contactName", "mediumId" }, missing.Errors.Select(e => e.Field));

        input = SyncTests.Complaint(lists);
        input.Description = "";
        input.FeedbackType = "";
        var required = world.D.Feedbacks.Save(olga, 0, input);
        Assert.Equal("Invalid input", required.Message);
        Assert.Contains(required.Errors, e => e.Field == "description");
        Assert.Contains(required.Errors, e => e.Field == "feedbackType");

        // A potential customer complaint has no customer details to fill in, nor a time received.
        var potential = world.D.Feedbacks.Save(olga, 0, new Feedbacks.FeedbackInput
        {
            FeedbackGroup = FeedbackGroups.PotentialCustomerComplaint, PersonResponsible = TestWorld.Sam.Login, FeedbackType = FeedbackTypes.Normal, CategoryId = lists.Category,
            Description = "Might complain",
        });
        Assert.True(potential.Ok, potential.Message);
        Assert.Empty(world.Store.Find<Feedback>().Where(f => f.CompanyName == "Tan & Sons"));
    }

    [Fact]
    public void A_suggestion_waits_for_HR_and_HR_accepts_or_turns_it_down()
    {
        using var world = new TestWorld();
        var lists = SyncTests.Lists(world);
        Feedbacks.FeedbackInput Suggestion() => new()
        {
            FeedbackGroup = FeedbackGroups.InternalChallenges, PersonResponsible = TestWorld.Sam.Login, FeedbackType = FeedbackTypes.Improvement, CategoryId = lists.Category,
            Description = "Reports typed twice", CreatorHasSolution = true, SolutionDetail = "Tablets", ExpectedOutcome = "Time saved",
            SuggestionMeetCriteria = ["Process Improvement"], RicohWayValue = ["Innovation"],
        };
        var olga = world.Actor(TestWorld.Olga);
        var incomplete = Suggestion();
        incomplete.RicohWayValue = [];
        Assert.Contains(world.D.Feedbacks.Save(olga, 0, incomplete).Errors, e => e.Field == "ricohWayValue");
        var one = world.D.Feedbacks.Save(olga, 0, Suggestion()).Id!.Value;
        var two = world.D.Feedbacks.Save(olga, 0, Suggestion()).Id!.Value;
        Assert.Equal(FeedbackStatuses.PendingHr, world.D.Feedbacks.Get(one)!.Status);
        // Process Improvement is kept from its own box (MyCRM kept it from the Ricoh Way Value box).
        Assert.Equal("|Process Improvement|", world.D.Feedbacks.Get(one)!.SuggestionMeetCriteria);

        // Only HR answers; someone else's answer is not taken.
        Assert.True(Change(world, TestWorld.Sam, one, i => i.HrAccepted = HrAnswers.Yes).Ok);
        Assert.Equal(HrAnswers.None, world.D.Feedbacks.Get(one)!.HrAccepted);
        Assert.Equal("Invalid Option", Change(world, TestWorld.Hr, one, i => i.HrRemarks = "Thinking").Errors.Single().Message);

        Assert.True(Change(world, TestWorld.Hr, one, i => { i.HrAccepted = HrAnswers.Yes; i.HrRemarks = "Good idea"; }).Ok);
        var accepted = world.D.Feedbacks.Get(one)!;
        Assert.Equal(FeedbackStatuses.New, accepted.Status);
        Assert.Equal(TestWorld.Hr.Login, accepted.HrUpdatedBy);
        Assert.Equal(world.Now, accepted.HrUpdatedOn);
        Assert.True(Change(world, TestWorld.Hr, two, i => i.HrAccepted = HrAnswers.No).Ok);
        Assert.Equal(FeedbackStatuses.Closed, world.D.Feedbacks.Get(two)!.Status);
    }

    [Fact]
    public void The_status_follows_the_steps_and_BE_closes_the_case()
    {
        using var world = new TestWorld();
        var id = NewComplaint(world);
        // The creator is not in charge: the acknowledgement is not theirs to tick.
        Assert.True(Change(world, TestWorld.Olga, id, i => i.IsAcknowledged = true).Ok);
        Assert.False(world.D.Feedbacks.Get(id)!.IsAcknowledged);

        Assert.True(Change(world, TestWorld.Sam, id, i => i.IsAcknowledged = true).Ok);
        var f = world.D.Feedbacks.Get(id)!;
        Assert.Equal(FeedbackStatuses.Acknowledged, f.Status);
        Assert.Equal(world.Now.Date, f.AcknowledgedDate);
        Assert.Equal("09:30:00", f.AcknowledgedTime);
        Assert.Equal(TestWorld.Sam.Login, f.SignOff);

        Assert.Equal("Required field!", Change(world, TestWorld.Sam, id, i => i.FirstLookAtFeedback = "").Errors.Single().Message);
        Assert.Equal("Please add at least one task", Change(world, TestWorld.Sam, id, i => { i.FirstLookAtFeedback = "Looked"; i.RootCause = "Wrong address"; }).Message);
        var sam = world.Actor(TestWorld.Sam);
        Assert.True(world.D.Feedbacks.SaveTask(sam, id, 0, new Feedbacks.TaskInput { TaskType = TaskTypes.Corrective, ActionPlan = "Fix", PersonInCharge = TestWorld.Be.Login, DueDate = world.Now }).Ok);
        Assert.True(Change(world, TestWorld.Sam, id, i => { i.FirstLookAtFeedback = "Looked"; i.RootCause = "Wrong address"; }).Ok);
        f = world.D.Feedbacks.Get(id)!;
        Assert.Equal(FeedbackStatuses.Investigation, f.Status);
        Assert.Equal(TestWorld.Sam.Login, f.StaffResponsibleForSolution);

        // The satisfaction is a Feedback Admin's: Sam's is not taken; while it waits it is required of them.
        Assert.True(Change(world, TestWorld.Sam, id, i => i.SatisfactionLevel = "Accepted").Ok);
        Assert.Equal("", world.D.Feedbacks.Get(id)!.SatisfactionLevel);
        Assert.Equal("satisfactionLevel", Change(world, TestWorld.FeedbackAdmin, id, i => { }).Errors.Single().Field);
        Assert.True(Change(world, TestWorld.FeedbackAdmin, id, i => i.SatisfactionLevel = "Accepted").Ok);
        Assert.Equal(FeedbackStatuses.ConfirmedSatisfaction, world.D.Feedbacks.Get(id)!.Status);

        Assert.Equal(ResultCodes.Forbidden, world.D.Feedbacks.CloseCase(world.Actor(TestWorld.FeedbackAdmin), id).Code);
        Assert.True(world.D.Feedbacks.CloseCase(world.Actor(TestWorld.Be), id).Ok);
        Assert.Equal(FeedbackStatuses.Closed, world.D.Feedbacks.Get(id)!.Status);
        // Closed: nothing more to do on it.
        Assert.False(world.D.Feedbacks.Rights(world.Actor(TestWorld.Sam), world.D.Feedbacks.Get(id)!).EditInvestigation);
        Assert.Equal(ResultCodes.Forbidden, world.D.Feedbacks.SaveTask(sam, id, 0, new Feedbacks.TaskInput { TaskType = TaskTypes.Preventive, ActionPlan = "x", PersonInCharge = TestWorld.Sam.Login, DueDate = world.Now }).Code);
    }

    [Fact]
    public void A_resolved_feedback_with_every_task_completed_is_completed_and_BE_is_told()
    {
        using var world = new TestWorld(s => s.Email.BeTo = "be-team@example.com");
        var id = NewComplaint(world);
        var sam = world.Actor(TestWorld.Sam);
        Assert.True(Change(world, TestWorld.Sam, id, i => i.IsAcknowledged = true).Ok);
        var task = world.D.Feedbacks.SaveTask(sam, id, 0, new Feedbacks.TaskInput { TaskType = TaskTypes.Corrective, ActionPlan = "Fix", PersonInCharge = TestWorld.Be.Login, DueDate = world.Now });
        Assert.Contains(world.Email.Sent, e => e.To.SequenceEqual(new[] { "ben@example.com" }) && e.Subject.StartsWith($"MyCRMRecords - Feedback #{id} - Acknowledged", StringComparison.Ordinal));
        // Every task completed without a root cause: the person in charge is reminded.
        world.Email.Sent.Clear();
        Assert.True(world.D.Feedbacks.CompleteTask(world.Actor(TestWorld.Be), task.Id!.Value, true).Ok);
        Assert.Contains(world.Email.Sent, e => e.To.SequenceEqual(new[] { "sam@example.com" }) && e.Html.Contains("root cause"));
        Assert.False(world.D.Feedbacks.Get(id)!.IsCompleted);
        // The task's person may complete it, not remove it.
        Assert.Equal(ResultCodes.Forbidden, world.D.Feedbacks.RemoveTask(world.Actor(TestWorld.Be), task.Id.Value).Code);

        world.Now = new DateTime(2026, 10, 20, 11, 0, 0);
        Assert.True(Change(world, TestWorld.Sam, id, i => { i.FirstLookAtFeedback = "Looked"; i.RootCause = "Wrong address"; }).Ok);
        var f = world.D.Feedbacks.Get(id)!;
        Assert.True(f.IsCompleted);
        Assert.Equal(world.Now, f.CompletedResolutionOn);
        Assert.Equal(FeedbackStatuses.Investigation, f.Status);
        // Received Thursday 15 October; completed Tuesday 20: five days less the weekend.
        Assert.Equal(3, f.CloseCaseDays);
        Assert.Contains(world.Email.Sent, e => e.To.SequenceEqual(new[] { "be-team@example.com" }));
    }

    [Fact]
    public void Working_days_leave_out_weekends_and_public_holidays()
    {
        var holidays = new FakeHolidays();
        holidays.List.Add(new DateTime(2026, 10, 16));
        var days = new BusinessDays(holidays);
        // Thursday 15 October 09:00 to Monday 19 October 10:00: 97 hours, less Friday (a holiday here), Saturday and Sunday.
        Assert.Equal(97 - 72, days.Hours(new DateTime(2026, 10, 15, 9, 0, 0), new DateTime(2026, 10, 19, 10, 0, 0)));
        Assert.Equal(4 - 3, days.Days(new DateTime(2026, 10, 15), new DateTime(2026, 10, 19, 8, 0, 0)));
        Assert.Equal(new DateTime(2026, 10, 20), days.Add(new DateTime(2026, 10, 15), 2));
        Assert.Equal(new DateTime(2026, 10, 30), days.Add(new DateTime(2026, 10, 15), 10));
        Assert.Equal(2026, FinancialYears.Of(new DateTime(2027, 3, 31)));
        Assert.Equal(2027, FinancialYears.Of(new DateTime(2027, 4, 1)));
    }

    [Fact]
    public void The_acknowledgement_hours_count_from_receipt_or_else_from_creation()
    {
        using var world = new TestWorld();
        world.Holidays.List.Add(new DateTime(2026, 10, 16));
        var id = NewComplaint(world);
        world.Now = new DateTime(2026, 10, 19, 10, 0, 0);
        Assert.True(Change(world, TestWorld.Sam, id, i => i.IsAcknowledged = true).Ok);
        Assert.Equal(97 - 72, world.D.Feedbacks.Get(id)!.AcknowledgedHours);

        // An internal challenge has no Date Received: it counts from when it was created (MyCRM counted from 1900).
        world.Now = new DateTime(2026, 10, 19, 11, 0, 0);
        var lists = (world.Store.Find<FeedbackDivision>()[0].Id, world.Store.Find<FeedbackCategory>()[0].Id, world.Store.Find<FeedbackMedium>()[0].Id);
        var other = world.D.Feedbacks.Save(world.Actor(TestWorld.Olga), 0, new Feedbacks.FeedbackInput
        {
            FeedbackGroup = FeedbackGroups.InternalChallenges, PersonResponsible = TestWorld.Sam.Login, FeedbackType = FeedbackTypes.Normal, CategoryId = lists.Item2, Description = "x",
        }).Id!.Value;
        world.Now = new DateTime(2026, 10, 19, 15, 30, 0);
        Assert.True(Change(world, TestWorld.Sam, other, i => i.IsAcknowledged = true).Ok);
        Assert.Equal(4, world.D.Feedbacks.Get(other)!.AcknowledgedHours);
    }

    [Fact]
    public void Each_person_sees_the_feedbacks_they_are_involved_in()
    {
        using var world = new TestWorld();
        var id = NewComplaint(world);
        var sam = world.Actor(TestWorld.Sam);
        Assert.True(Change(world, TestWorld.Sam, id, i => i.IsAcknowledged = true).Ok);
        Assert.True(world.D.Feedbacks.SaveTask(sam, id, 0, new Feedbacks.TaskInput { TaskType = TaskTypes.Corrective, ActionPlan = "Fix", PersonInCharge = TestWorld.Be.Login, DueDate = world.Now }).Ok);
        int Sees(ApiUser user) => world.D.Feedbacks.List(world.Actor(user), new Feedbacks.ListFilter()).All;
        Assert.Equal(1, Sees(TestWorld.Olga));
        Assert.Equal(1, Sees(TestWorld.Sam));
        Assert.Equal(1, Sees(TestWorld.Hr));
        Assert.Equal(1, Sees(TestWorld.Be));
        Assert.Equal(1, Sees(TestWorld.FeedbackAdmin));
        Assert.Equal(0, Sees(TestWorld.Admin with { Login = @"RSP\nobody", IsListedAdmin = false }));

        // Deleted (BE): nobody sees it, it is kept.
        Assert.Equal(ResultCodes.Forbidden, world.D.Feedbacks.Delete(world.Actor(TestWorld.FeedbackAdmin), id).Code);
        Assert.True(world.D.Feedbacks.Delete(world.Actor(TestWorld.Be), id).Ok);
        Assert.Equal(0, Sees(TestWorld.FeedbackAdmin));
        Assert.False(world.Store.Get<Feedback>(id)!.IsActive);
    }

    [Fact]
    public void HR_sees_the_feedbacks_waiting_for_them()
    {
        using var world = new TestWorld();
        var lists = SyncTests.Lists(world);
        var id = world.D.Feedbacks.Save(world.Actor(TestWorld.Olga), 0, new Feedbacks.FeedbackInput
        {
            FeedbackGroup = FeedbackGroups.InternalChallenges, PersonResponsible = TestWorld.Sam.Login, FeedbackType = FeedbackTypes.Normal, CategoryId = lists.Category,
            Description = "x", CreatorHasSolution = true, SolutionDetail = "a", ExpectedOutcome = "b", SuggestionMeetCriteria = ["Cost Saving"], RicohWayValue = ["Passion"],
        }).Id!.Value;
        Assert.Equal(id, world.D.Feedbacks.List(world.Actor(TestWorld.Hr), new Feedbacks.ListFilter()).Rows.Single().Id);
    }

    [Fact]
    public void The_counters_count_every_status_and_the_division_filter_reads_the_chosen_division()
    {
        using var world = new TestWorld();
        var first = NewComplaint(world);
        var second = NewComplaint(world);
        Assert.True(Change(world, TestWorld.Sam, second, i => i.IsAcknowledged = true).Ok);
        // An old feedback with only MyCRM's division text.
        world.Store.Insert(new Feedback { Status = FeedbackStatuses.Closed, PersonResponsibleDivision = "Old Division", CreatedBy = TestWorld.Olga.Login, CompanyName = "Old" });
        var fay = world.Actor(TestWorld.FeedbackAdmin);
        var list = world.D.Feedbacks.List(fay, new Feedbacks.ListFilter(Status: FeedbackStatuses.New));
        Assert.Equal(new[] { first }, list.Rows.Select(f => f.Id));
        Assert.Equal(1, list.Counts[FeedbackStatuses.Acknowledged]);
        Assert.Equal(1, list.Counts[FeedbackStatuses.Closed]);
        Assert.Equal(3, list.All);
        Assert.Equal(2, world.D.Feedbacks.List(fay, new Feedbacks.ListFilter(Division: "Service")).All);
        Assert.Equal(1, world.D.Feedbacks.List(fay, new Feedbacks.ListFilter(Division: "Old Division")).All);
        Assert.Equal(2, world.D.Feedbacks.List(fay, new Feedbacks.ListFilter(Search: "service")).All);
        Assert.Equal(1, world.D.Feedbacks.List(fay, new Feedbacks.ListFilter(Search: "#" + second)).All);
    }

    [Fact]
    public void Customer_details_files_are_a_Feedback_Admins_and_a_file_removed_is_kept()
    {
        using var world = new TestWorld();
        var id = NewComplaint(world);
        Assert.Equal(ResultCodes.Forbidden, world.D.Feedbacks.Attach(world.Actor(TestWorld.Olga), id, FileKinds.CustomerDetails, "a.pdf", [1]).Code);
        var file = world.D.Feedbacks.Attach(world.Actor(TestWorld.FeedbackAdmin), id, FileKinds.CustomerDetails, @"C:\Users\fay\a.pdf", [1, 2]);
        Assert.True(file.Ok, file.Message);
        Assert.Equal("a.pdf", world.D.Feedbacks.File(file.Id!.Value)!.FileName);
        Assert.True(world.D.Feedbacks.Attach(world.Actor(TestWorld.Olga), id, FileKinds.Satisfaction, "b.pdf", [3]).Ok);
        Assert.Equal(ResultCodes.Forbidden, world.D.Feedbacks.RemoveFile(world.Actor(TestWorld.Olga), file.Id.Value).Code);
        Assert.True(world.D.Feedbacks.RemoveFile(world.Actor(TestWorld.FeedbackAdmin), file.Id.Value).Ok);
        Assert.Single(world.D.Feedbacks.Files(id));
        Assert.False(world.Store.Get<FeedbackFile>(file.Id.Value)!.IsActive);
    }

    [Fact]
    public void The_lists_are_kept_by_Feedback_Admins_and_taken_away_never_deleted()
    {
        using var world = new TestWorld();
        var lists = SyncTests.Lists(world);
        Assert.Equal(ResultCodes.Forbidden, world.D.Setup.SaveCategory(world.Actor(TestWorld.Sam), 0, new Setup.ChoiceInput { Label = "X" }).Code);
        var fay = world.Actor(TestWorld.FeedbackAdmin);
        Assert.False(world.D.Setup.SaveCategory(fay, 0, new Setup.ChoiceInput { Label = "billing" }).Ok);
        Assert.True(world.D.Setup.RemoveCategory(fay, lists.Category).Ok);
        Assert.Empty(world.D.Feedbacks.Categories());
        Assert.True(world.Store.Get<FeedbackCategory>(lists.Category)!.Removed);
        Assert.True(world.D.Setup.RemoveDivision(fay, lists.Division).Ok);
        Assert.False(world.Store.Get<FeedbackDivision>(lists.Division)!.IsActive);
    }

    [Fact]
    public void Choosing_a_division_on_the_page_gives_its_person_in_charge_and_the_KPI_dates_work_the_timings_out_again()
    {
        using var world = new TestWorld();
        var id = NewComplaint(world);
        Assert.True(Change(world, TestWorld.Sam, id, i => i.IsAcknowledged = true).Ok);
        var fay = world.Actor(TestWorld.FeedbackAdmin);
        Assert.Equal(ResultCodes.Forbidden, world.D.Setup.SaveKpiDates(world.Actor(TestWorld.Sam), id, new Setup.KpiInput()).Code);
        var saved = world.D.Setup.SaveKpiDates(fay, id, new Setup.KpiInput
        {
            CreatedOn = new DateTime(2026, 10, 15, 9, 5, 0), DateReceived = new DateTime(2026, 10, 15), TimeReceived = "09:00", AcknowledgedDate = new DateTime(2026, 10, 15),
            AcknowledgedTime = "15:00",
        });
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal(6, world.D.Feedbacks.Get(id)!.AcknowledgedHours);
    }

    [Fact]
    public void The_statistics_average_the_completed_feedbacks_by_the_month_they_were_received()
    {
        using var world = new TestWorld();
        var lists = SyncTests.Lists(world);
        void Completed(DateTime received, int hours, int start, int close, string division = "") => world.Store.Insert(new Feedback
        {
            Status = FeedbackStatuses.ConfirmedSatisfaction, IsCompleted = true, DateReceived = received, AcknowledgedHours = hours, StartResolutionDays = start,
            CloseCaseDays = close, CategoryId = lists.Category, DivisionId = division.Length == 0 ? lists.Division : null, PersonResponsibleDivision = division,
        });
        Completed(new DateTime(2026, 4, 3), 10, 1, 4);
        Completed(new DateTime(2026, 4, 20), 31, 2, 12, "Old Division");
        Completed(new DateTime(2027, 3, 1), 5, 1, 2);
        Completed(new DateTime(2027, 4, 1), 5, 1, 2);
        world.Store.Insert(new Feedback { DateReceived = new DateTime(2026, 4, 5), AcknowledgedHours = 99 });
        var year = world.D.Statistics.Year(2026);
        var april = year.Months[0];
        Assert.Equal(new DateTime(2026, 4, 1), april.Month);
        Assert.Equal(2, april.Ids.Count);
        Assert.Equal(21, april.AcknowledgedHours);
        Assert.Equal(2, april.StartResolutionDays);
        Assert.Equal(8, april.CloseCaseDays);
        Assert.Single(april.WithinTarget);
        Assert.Single(april.OverTarget);
        Assert.Single(year.Months[11].Ids);
        Assert.Equal(3, year.Categories.Single().Ids.Count);
        Assert.Equal(new[] { ("Service", 2), ("Old Division", 1) }, year.Divisions.Select(d => (d.Label, d.Ids.Count)));
        Assert.Contains(2025, world.D.Statistics.Years());

        // The report: every active feedback received in the months chosen, whatever its status.
        Assert.Equal(3, world.D.Statistics.Report([new DateTime(2026, 4, 1)]).Count);
        var (name, bytes) = world.D.Statistics.ReportFile([new DateTime(2026, 5, 1), new DateTime(2026, 4, 1)]);
        Assert.Equal("CMS Report - April 2026,May 2026.xlsx", name);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void The_page_gets_what_the_person_may_change()
    {
        using var world = new TestWorld();
        var id = NewComplaint(world);
        var olga = world.Json(world.Call("GET", "feedbacks/" + id, user: TestWorld.Olga));
        Assert.False((bool)olga["rights"]!["inCharge"]!);
        Assert.True((bool)olga["rights"]!["editNew"]!);
        Assert.Equal("Sam Staff", (string)olga["feedback"]!["personResponsible"]!["name"]!);
        var sam = world.Json(world.Call("GET", "feedbacks/" + id, user: TestWorld.Sam));
        Assert.True((bool)sam["rights"]!["editAcknowledge"]!);
        var list = world.Json(world.Call("GET", "feedbacks", user: TestWorld.Sam));
        Assert.Equal(1, (int)list["counts"]!["New"]!);
        Assert.Equal(404, world.Call("GET", "feedbacks/999", user: TestWorld.Sam).Status);
        Assert.Equal(403, world.Call("GET", "setup", user: TestWorld.Sam).Status);
        Assert.Equal(200, world.Call("GET", "setup", user: TestWorld.FeedbackAdmin).Status);
        Assert.Equal(200, world.Call("GET", "statistics", user: TestWorld.Sam).Status);
    }

    [Fact]
    public void The_lists_match_MyCRM_by_label()
    {
        Assert.Equal(FeedbackStatuses.ConfirmedSatisfaction, FeedbackStatuses.List.FromLabel("Confirmed Statisfaction"));
        Assert.Equal(FeedbackTypes.VerySerious, FeedbackTypes.List.FromLabel("very serious complaint"));
        Assert.Equal("", FeedbackGroups.List.FromLabel("Something else"));
        Assert.Equal("|Ricoh Way Value||Process Improvement|", EssChoices.Join(EssChoices.Criteria, ["Process Improvement", "Ricoh Way Value", "Nonsense"]));
        Assert.Equal(new[] { "Cost Saving" }, EssChoices.Parse(EssChoices.Criteria, "|Cost Saving|"));
        Assert.Equal("09:05:00", TimesOfDay.Normalise("9:5"));
        Assert.Equal("", TimesOfDay.Normalise("25:00"));
        Assert.Equal("14:05:30", TimesOfDay.FromOutSystems(new DateTime(1900, 1, 1, 14, 5, 30)));
        Assert.Equal("", TimesOfDay.FromOutSystems(new DateTime(1900, 1, 1)));
    }
}
