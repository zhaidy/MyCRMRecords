using Newtonsoft.Json.Linq;

namespace MyCRMRecords.Tests;

/// <summary>
/// The two-way sync with MyCRM (OldAppSync, CrmSync), against MyCRM's tables in memory: a feedback made here takes
/// MyCRM's number, its fields, tasks and files reach MyCRM with MyCRM's users and static Ids, MyCRM's changes come here,
/// and what is removed on either side is marked, never deleted.
/// </summary>
public sealed class SyncTests
{
    private static SyncOutcome Sync(TestWorld world, bool allowLarge = false)
    {
        var outcome = world.D.Sync.Run("test", allowLarge);
        Assert.True(outcome.Ok, outcome.Message + "\n" + string.Join("\n", outcome.Details));
        return outcome;
    }

    private static void InStep(TestWorld world)
    {
        var outcome = Sync(world);
        Assert.True(outcome.ToOldApp + outcome.ToHere == 0, "Not in step:\n" + string.Join("\n", outcome.Details));
    }

    private static long L(object? value) => Convert.ToInt64(value);

    [Fact]
    public void Nothing_can_be_deleted_in_the_old_app()
    {
        Assert.DoesNotContain(typeof(IOldTablesWriter).GetMethods(), m => m.Name.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.DoesNotContain(typeof(IOldTables).GetMethods(), m => m.Name.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void A_files_bytes_are_compared_by_their_length_and_hash()
    {
        Assert.Equal("", OldValues.Canonical(Array.Empty<byte>()));
        Assert.Equal(OldValues.Canonical(new byte[] { 1, 2, 3 }), OldValues.Canonical(new byte[] { 1, 2, 3 }));
        Assert.NotEqual(OldValues.Canonical(new byte[] { 1, 2, 3 }), OldValues.Canonical(new byte[] { 1, 2, 4 }));
        Assert.StartsWith("bytes:3:", OldValues.Canonical(new byte[] { 1, 2, 3 }));
    }

    /// <summary>A division (in charge: Sam), a category and a medium, made here.</summary>
    public static (long Division, long Category, long Medium) Lists(TestWorld world)
    {
        var fay = world.Actor(TestWorld.FeedbackAdmin);
        var division = world.D.Setup.SaveDivision(fay, 0, new Setup.DivisionInput { Name = "Service", PersonInCharge = TestWorld.Sam.Login });
        var category = world.D.Setup.SaveCategory(fay, 0, new Setup.ChoiceInput { Label = "Billing", SortOrder = 1 });
        var medium = world.D.Setup.SaveMedium(fay, 0, new Setup.ChoiceInput { Label = "Email", SortOrder = 1 });
        Assert.True(division.Ok && category.Ok && medium.Ok, division.Message + category.Message + medium.Message);
        return (division.Id!.Value, category.Id!.Value, medium.Id!.Value);
    }

    /// <summary>A customer complaint as Olga makes it (in charge: Sam, Hana copied).</summary>
    public static Feedbacks.FeedbackInput Complaint((long Division, long Category, long Medium) lists) => new()
    {
        FeedbackGroup = FeedbackGroups.CustomerComplaint, DivisionId = lists.Division, PersonResponsible = TestWorld.Sam.Login, CompanyName = "Tan & Sons",
        ContactName = "Mr Tan", CompanyEmail = "tan@tan.example", MediumId = lists.Medium, DateReceived = new DateTime(2026, 10, 15), TimeReceived = "09:00",
        FeedbackType = FeedbackTypes.Serious, CategoryId = lists.Category, Description = "The invoice went to the wrong address.", Cc = ["hana@example.com"],
    };

    /// <summary>A feedback made here by Olga, acknowledged by Sam with a corrective task (Ben's) and a file.</summary>
    private static long MadeHere(TestWorld world)
    {
        var lists = Lists(world);
        var made = world.D.Feedbacks.Save(world.Actor(TestWorld.Olga), 0, Complaint(lists));
        Assert.True(made.Ok, made.Message);
        var id = made.Id!.Value;
        var sam = world.Actor(TestWorld.Sam);
        var input = FeedbackTests.InputOf(world, id);
        input.IsAcknowledged = true;
        Assert.True(world.D.Feedbacks.Save(sam, id, input).Ok);
        var task = world.D.Feedbacks.SaveTask(sam, id, 0, new Feedbacks.TaskInput { TaskType = TaskTypes.Corrective, ActionPlan = "Correct the address", PersonInCharge = TestWorld.Be.Login, DueDate = new DateTime(2026, 10, 20) });
        Assert.True(task.Ok, task.Message);
        Assert.True(world.D.Feedbacks.Attach(world.Actor(TestWorld.Olga), id, FileKinds.Satisfaction, "letter.pdf", [1, 2, 3]).Ok);
        return id;
    }

    [Fact]
    public void A_feedback_made_here_takes_MyCRM_number_and_reaches_MyCRM()
    {
        using var world = new TestWorld(s => s.Sync.Enabled = true);
        var old = world.OldApp;
        var id = MadeHere(world);
        // MyCRM's number: the feedback's number is the same in both.
        Assert.Equal(1001, id);

        Sync(world);
        var row = old.Row("Feedback", 1001);
        Assert.Equal(11L, L(row["FEEDBACKGROUPID"]));
        Assert.Equal(23L, L(row["FEEDBACKSTATUSID"]));
        Assert.Equal(32L, L(row["FEEDBACKTYPEID"]));
        Assert.Equal(101L, L(row["PERSONRESPONSIBLE"]));
        Assert.Equal(101L, L(row["SIGNOFF"]));
        Assert.Equal("Tan & Sons", FakeOldTables.Text(row["COMPANYNAME"]));
        Assert.Equal(new DateTime(1900, 1, 1, 9, 0, 0), row["TIMERECEIVED"]);
        Assert.Equal(new DateTime(2026, 10, 15), row["DATERECEIVED"]);
        Assert.Equal("hana@example.com", FakeOldTables.Text(row["CC"]));
        Assert.True((bool)row["ISACTIVE"]!);
        Assert.Equal(L(old.All("FeedbackDivision").Single()["ID"]), L(row["FEEDBACKDIVISIONID"]));
        Assert.Equal(L(old.All("FeedbackCategory").Single()["ID"]), L(row["FEEDBACKCATEGORYID"]));
        Assert.Equal(101L, L(old.All("FeedbackDivision").Single()["PERSONINCHARGE"]));
        var task = old.All("Task").Single();
        Assert.Equal(1001L, L(task["FEEDBACKID"]));
        Assert.Equal(61L, L(task["TASKTYPEID"]));
        Assert.Equal(104L, L(task["PERSONINCHARGE"]));
        Assert.Equal(FakeOldTables.Text(row["SESSIONID"]), FakeOldTables.Text(task["SESSIONID"]));
        var file = old.All("FeedbackAttachment").Single();
        Assert.Equal(1001L, L(file["FEEDBACKID"]));
        Assert.Equal("CS", FakeOldTables.Text(file["TYPE"]));
        Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])file["FILEBINARY"]!);
        InStep(world);
    }

    [Fact]
    public void Every_synced_field_goes_to_MyCRM_and_back_unchanged()
    {
        using var here = new TestWorld(s => s.Sync.Enabled = true);
        var id = MadeHere(here);
        var internalOne = here.D.Feedbacks.Save(here.Actor(TestWorld.Olga), 0, new Feedbacks.FeedbackInput
        {
            FeedbackGroup = FeedbackGroups.InternalChallenges, PersonResponsible = TestWorld.Sam.Login, FeedbackType = FeedbackTypes.Improvement,
            CategoryId = here.Store.Find<FeedbackCategory>().Single().Id, Description = "Reports typed twice.", CreatorHasSolution = true, SolutionDetail = "Tablets",
            ExpectedOutcome = "Time saved", SuggestionMeetCriteria = ["Process Improvement"], RicohWayValue = ["Innovation", "Teamwork"], MightCauseCustomerComplaint = true,
        });
        Assert.True(internalOne.Ok, internalOne.Message);
        Assert.Equal(FeedbackStatuses.PendingHr, here.D.Feedbacks.Get(internalOne.Id!.Value)!.Status);
        Sync(here);
        // Another MyCRMRecords synced with the same MyCRM gets every record with the same values.
        using var there = new TestWorld(s => s.Sync.Enabled = true, oldTables: here.OldApp);
        // A row added in the last two minutes waits for the next sync.
        there.Now = here.Now.AddMinutes(5);
        Sync(there, allowLarge: true);
        void Same<T>(params string[] notInMyCRM) where T : class, IRecord, new()
        {
            string Text(T record)
            {
                var json = JObject.FromObject(record);
                foreach (var name in notInMyCRM.Append("Id")) json.Remove(name);
                return json.ToString(Newtonsoft.Json.Formatting.None);
            }
            var mine = here.Store.Find<T>().Select(Text).OrderBy(t => t, StringComparer.Ordinal).ToList();
            Assert.NotEmpty(mine);
            Assert.Equal(mine, there.Store.Find<T>().Select(Text).OrderBy(t => t, StringComparer.Ordinal).ToList());
        }
        Same<Feedback>();
        Same<FeedbackTask>();
        Same<FeedbackFile>();
        Same<FeedbackDivision>();
        Same<FeedbackCategory>();
        Same<FeedbackMedium>();
        Assert.Equal(id, there.D.Feedbacks.Get(id)!.Id);
        InStep(there);
        InStep(here);
    }

    [Fact]
    public void What_is_removed_here_is_marked_in_MyCRM_and_never_deleted()
    {
        using var world = new TestWorld(s => s.Sync.Enabled = true);
        var old = world.OldApp;
        var id = MadeHere(world);
        Sync(world);
        var sam = world.Actor(TestWorld.Sam);
        var task = world.D.Feedbacks.Tasks(id).Single();
        var file = world.D.Feedbacks.Files(id).Single();
        Assert.True(world.D.Feedbacks.RemoveTask(sam, task.Id).Ok);
        Assert.True(world.D.Feedbacks.RemoveFile(sam, file.Id).Ok);
        var fay = world.Actor(TestWorld.FeedbackAdmin);
        Assert.True(world.D.Setup.RemoveCategory(fay, world.Store.Find<FeedbackCategory>().Single().Id).Ok);
        Assert.True(world.D.Setup.RemoveDivision(fay, world.Store.Find<FeedbackDivision>().Single().Id).Ok);
        Assert.True(world.D.Feedbacks.Delete(world.Actor(TestWorld.Be), id).Ok);
        Sync(world);

        // Every row is still there, marked: detached from its feedback, its session saying which one.
        var oldTask = old.All("Task").Single();
        Assert.Null(oldTask["FEEDBACKID"]);
        Assert.StartsWith("X:1001:", FakeOldTables.Text(oldTask["SESSIONID"]));
        var oldFile = old.All("FeedbackAttachment").Single();
        Assert.Null(oldFile["FEEDBACKID"]);
        Assert.Equal("(removed) letter.pdf", FakeOldTables.Text(oldFile["FILENAME"]));
        Assert.False((bool)old.All("FeedbackCategory").Single()["ISACTIVE"]!);
        Assert.False((bool)old.All("FeedbackDivision").Single()["ISACTIVE"]!);
        Assert.False((bool)old.Row("Feedback", 1001)["ISACTIVE"]!);
        Assert.Equal(1, old.All("Feedback").Count);

        // The marks are read back as they were written: nothing to do on either side, and the records stay removed here.
        InStep(world);
        var kept = world.Store.Get<FeedbackTask>(task.Id)!;
        Assert.False(kept.IsActive);
        Assert.Equal(id, kept.FeedbackId);
        var keptFile = world.Store.Get<FeedbackFile>(file.Id)!;
        Assert.False(keptFile.IsActive);
        Assert.Equal("letter.pdf", keptFile.FileName);
        Assert.True(world.Store.Find<FeedbackCategory>().Single().Removed);
        Assert.Null(world.D.Feedbacks.Get(id));
    }

    /// <summary>MyCRM's feedback 1001 (created by Fay, user 102, in charge Sam) with a task and a file, and its lists.</summary>
    private static void MyCrmRows(FakeOldTables old)
    {
        var category = old.Add("FeedbackCategory", new() { ["LABEL"] = "Service", ["ORDER"] = 1, ["ISACTIVE"] = true });
        var medium = old.Add("FeedbackMedium", new() { ["LABEL"] = "Phone", ["ORDER"] = 1, ["ISACTIVE"] = true });
        var division = old.Add("FeedbackDivision", new() { ["DIVISION"] = "Logistics", ["PERSONINCHARGE"] = 101L, ["ISACTIVE"] = true });
        var feedback = old.Add("Feedback", new()
        {
            ["FEEDBACKGROUPID"] = 11L, ["PERSONRESPONSIBLE"] = 101L, ["FEEDBACKSTATUSID"] = 25L, ["COMPANYNAME"] = "Lim Pte Ltd", ["CONTACTNAME"] = "Ms Lim",
            ["COMPANYEMAIL"] = "lim@lim.example", ["FEEDBACKMEDIUMID"] = medium, ["DATERECEIVED"] = new DateTime(2026, 9, 1), ["TIMERECEIVED"] = new DateTime(1900, 1, 1, 14, 5, 30),
            ["FEEDBACKTYPEID"] = 33L, ["FEEDBACKCATEGORYID"] = category, ["FEEDBACKDESCRIPTION"] = "Late delivery", ["ISACKNOWLEDGED"] = true,
            ["ACKNOWLEDGEDDATE"] = new DateTime(2026, 9, 1), ["ACKNOWLEDGEDTIME"] = new DateTime(1900, 1, 1, 15, 0, 0), ["SIGNOFF"] = 101L, ["ROOTCAUSE"] = "Wrong slot",
            ["FIRSTLOOKATFEEDBACK"] = "Checked", ["STAFFRESPONSIBLEFORSOLUTION"] = 101L, ["CUSTOMERSATISFACTIONLEVELID"] = 52L, ["CREATEDON"] = new DateTime(2026, 9, 1, 14, 10, 0),
            ["CREATEDBY"] = 102L, ["CREATEDBYUSERNAME"] = "Fay Admin", ["ISACTIVE"] = true, ["SESSIONID"] = "2026-09-01 14:09:00", ["HRACCEPTED"] = "-",
            ["FEEDBACKDIVISIONID"] = division, ["ACKNOWLEGEDHOURS"] = 1, ["ISCOMPLETED"] = true, ["CLOSECASEDAYS"] = 3,
        });
        old.Add("Task", new()
        {
            ["FEEDBACKID"] = feedback, ["ACTIONPLAN"] = "Rebook", ["PERSONINCHARGE"] = 104L, ["DUEDATE"] = new DateTime(2026, 9, 3), ["ISCOMPLETED"] = true,
            ["CREATEDON"] = new DateTime(2026, 9, 2, 8, 0, 0), ["CREATEDBY"] = 101L, ["SESSIONID"] = "2026-09-01 14:09:00", ["TASKTYPEID"] = 61L,
        });
        old.Add("FeedbackAttachment", new() { ["FEEDBACKID"] = feedback, ["FILENAME"] = "lim.pdf", ["FILEBINARY"] = new byte[] { 9, 8 }, ["TYPE"] = "CD", ["UPLOADEDBY"] = 102L });
    }

    [Fact]
    public void MyCRM_changes_come_here_and_what_MyCRM_deletes_is_marked_here()
    {
        using var world = new TestWorld(s => s.Sync.Enabled = true);
        var old = world.OldApp;
        MyCrmRows(old);
        Assert.Equal(6, Sync(world).ToHere);
        var f = world.D.Feedbacks.Get(1001)!;
        Assert.Equal(FeedbackGroups.CustomerComplaint, f.FeedbackGroup);
        // "Confirmed Statisfaction" in MyCRM.
        Assert.Equal(FeedbackStatuses.ConfirmedSatisfaction, f.Status);
        Assert.Equal(FeedbackTypes.Normal, f.FeedbackType);
        Assert.Equal("Accepted", f.SatisfactionLevel);
        Assert.Equal(TestWorld.Sam.Login, f.PersonResponsible);
        Assert.Equal(TestWorld.FeedbackAdmin.Login, f.CreatedBy);
        Assert.Equal("14:05:30", f.TimeReceived);
        Assert.Equal("Logistics", Feedbacks.DivisionOf(f, world.D.Feedbacks.DivisionNames()));
        var task = world.D.Feedbacks.Tasks(1001).Single();
        Assert.Equal(TaskTypes.Corrective, task.TaskType);
        Assert.Equal(TestWorld.Be.Login, task.PersonInCharge);
        Assert.Equal(new byte[] { 9, 8 }, world.D.Feedbacks.Files(1001).Single().Content);
        InStep(world);

        // Changed in MyCRM: changed here.
        old.Change("Feedback", 1001, new() { ["FEEDBACKSTATUSID"] = 26L, ["UPDATEDON"] = new DateTime(2026, 10, 16, 8, 0, 0) });
        Sync(world);
        Assert.Equal(FeedbackStatuses.Closed, world.D.Feedbacks.Get(1001)!.Status);

        // MyCRM deletes the task and the file (its Delete buttons): marked removed here, kept.
        old.DeleteRows("Task", _ => true);
        old.DeleteRows("FeedbackAttachment", _ => true);
        Sync(world);
        Assert.Empty(world.D.Feedbacks.Tasks(1001));
        Assert.Empty(world.D.Feedbacks.Files(1001));
        var kept = world.Store.Find<FeedbackTask>().Single();
        Assert.False(kept.IsActive);
        Assert.Equal("MyCRM", kept.RemovedBy);
        InStep(world);
    }

    [Fact]
    public void A_task_MyCRM_keeps_for_a_feedback_never_saved_is_left_there_and_the_notes_say_which()
    {
        using var world = new TestWorld(s => s.Sync.Enabled = true);
        var old = world.OldApp;
        var lost = old.Add("Task", new() { ["ACTIONPLAN"] = "Never saved", ["PERSONINCHARGE"] = 101L, ["SESSIONID"] = "2026-01-01 10:00:00", ["TASKTYPEID"] = 61L });
        var outcome = Sync(world, allowLarge: true);
        Assert.Contains($"#{lost}", string.Join("\n", outcome.Details));
        Assert.Empty(world.Store.Find<FeedbackTask>());
        Assert.Equal("Never saved", FakeOldTables.Text(old.Row("Task", lost)["ACTIONPLAN"]));
        InStep(world);
    }

    [Fact]
    public void Records_imported_before_the_sync_are_paired_without_copying_even_a_row_that_differs()
    {
        using var world = new TestWorld();
        var old = world.OldApp;
        MyCrmRows(old);
        // As the import leaves them (MyCRM's Ids kept, OutSystemsId set); the description was changed here since. MyCRM never
        // edited the row (UPDATEDON empty), which must not count as older: MyCRM's version is kept.
        CopyAsImported(world);
        var imported = world.Store.Get<Feedback>(1001)!;
        imported.Description = "As imported";
        world.Store.Update(imported);
        world.SettingsFile.Change(s => s.Sync.Enabled = true);

        var preview = world.D.Sync.Preview();
        Assert.Contains("nothing has been changed yet", preview.Message);
        // The first sync after the import writes nothing back to MyCRM: where they differ, MyCRM's version is kept.
        var first = Sync(world);
        Assert.Equal(0, first.ToOldApp);
        Assert.Equal(1, first.ToHere);
        Assert.Equal("Late delivery", world.D.Feedbacks.Get(1001)!.Description);
        Assert.Equal(6, world.D.Store.Count<SyncLink>());
        InStep(world);
    }

    /// <summary>The records as the import makes them (each with MyCRM's Id), in the world's store (not through the sync).</summary>
    private static void CopyAsImported(TestWorld world)
    {
        var context = new SyncContext(world.OldApp, [], world.Now);
        foreach (var entity in CrmSync.Entities(() => world.Now)) entity.Accept(new Copy(world.OldApp, context, world.Store));
    }

    private sealed class Copy(FakeOldTables old, SyncContext context, IRecordStore store) : ISyncEntityVisitor
    {
        public void Visit<T>(SyncEntity<T> entity) where T : class, IRecord, new()
        {
            foreach (var row in old.Rows(entity.OldTable))
            {
                if (entity.FromOld(row, context, null) is not { } record) continue;
                record.Id = OldValues.Long(row, "ID");
                store.InsertKeepingIds([record]);
                context.Add(new SyncLink { Entity = entity.Name, LocalId = record.Id, OldKey = record.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            }
        }
    }

    [Fact]
    public void A_large_sync_stops_before_changing_anything()
    {
        using var world = new TestWorld(s => { s.Sync.Enabled = true; s.Sync.LargeSync = 2; });
        MyCrmRows(world.OldApp);
        var outcome = world.D.Sync.Run("test");
        Assert.False(outcome.Ok);
        Assert.Contains("Stopped before changing anything", outcome.Message);
        Assert.Empty(world.D.Store.Find<Feedback>());
        Assert.NotEmpty(world.SettingsFile.Load().Sync.Stopped);
        Assert.Equal(6, Sync(world, allowLarge: true).ToHere);
    }

    [Fact]
    public void The_import_counts_MyCRM_records_and_changes_nothing()
    {
        using var world = new TestWorld();
        MyCrmRows(world.OldApp);
        var counts = world.D.Import.Preview();
        Assert.Equal(1, counts.Found["Feedbacks"]);
        Assert.Equal(1, counts.Found["Tasks"]);
        Assert.Equal(1, counts.Found["Attachments"]);
        Assert.Equal(1, counts.Found["Divisions"]);
        Assert.Empty(world.Store.Find<Feedback>());
        Assert.Equal(0, world.OldApp.Commits);
    }

    [Theory]
    [InlineData("ADMIN", "FeedbackAdmin")]
    [InlineData("BE", "BE")]
    [InlineData("HR", "HR")]
    [InlineData("Registered", null)]
    public void MyCRM_roles_are_kept_and_its_Admin_is_a_Feedback_Admin(string oldRole, string? role) =>
        Assert.Equal(role, OutSystemsImport.RoleOf(oldRole));

    [Theory]
    [InlineData(@"RSP\jsmith", @"RSP\jsmith")]
    [InlineData(@"RSP\jsmith@ricoh.sg", @"RSP\jsmith")]
    [InlineData("jsmith@ricoh.sg", @"RSP\jsmith")]
    [InlineData("jsmith", @"RSP\jsmith")]
    public void MyCRM_usernames_become_logins(string username, string login) => Assert.Equal(login, OutSystemsImport.LoginOf(username, "RSP"));

    [Theory]
    [InlineData(@"RSP\jsmith@ricoh.sg", "jsmith@ricoh.sg", true, true)]
    [InlineData(@"RSP\jsmith@rapp.ricoh.com", "jsmith@rapp.ricoh.com", true, true)]
    [InlineData(@"RSP\zhaidy", "zhaidy@ricoh.sg", true, true)]
    [InlineData(@"RSP\jsmith", "jsmith@ricoh.sg", true, false)]
    [InlineData(@"RSP\jsmith@ricoh.sg", "jsmith@gmail.com", true, false)]
    [InlineData(@"RSP\jsmith@ricoh.sg", "jsmith@ricoh.sg", false, false)]
    public void The_people_offered_are_those_MyCRM_offered(string username, string email, bool active, bool offered) =>
        Assert.Equal(offered, OutSystemsImport.IsOffered(username, email, active));
}
