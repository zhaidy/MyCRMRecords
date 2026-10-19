namespace MyCRMRecords.Tests;

/// <summary>Runs cut off by a restart of the website (BackgroundRuns).</summary>
public sealed class BackgroundRunsTests
{
    [Fact]
    public void A_run_left_running_by_an_earlier_process_is_marked_stopped_and_can_be_started_again()
    {
        using var world = new TestWorld();
        var store = world.Store;
        // Ids no other test's runs have (BackgroundRuns is shared by the tests running at the same time).
        JobRun Add(long id, DateTime startedAt, string summary = "")
        {
            store.InsertKeepingIds([new JobRun { Id = id, Job = OutSystemsImport.Job, StartedAt = startedAt, Outcome = "Running", Summary = summary }]);
            return store.Get<JobRun>(id)!;
        }
        // Cut off by a restart: still Running, done by no process.
        var cutOff = Add(987_000_001, world.Now.AddMinutes(-30), "Toner requests: 34400…");
        // Running in this process.
        var mine = Add(987_000_002, world.Now.AddMinutes(-30));
        BackgroundRuns.Started(mine.Id);
        // Just started (perhaps by another process starting up): left alone.
        var fresh = Add(987_000_003, world.Now.AddSeconds(-20));
        try
        {
            Assert.Equal(1, BackgroundRuns.StopAbandoned(store, () => world.Now));
            var stopped = store.Get<JobRun>(cutOff.Id)!;
            Assert.Equal(BackgroundRuns.Stopped, stopped.Outcome);
            Assert.Equal(world.Now, stopped.FinishedAt);
            Assert.StartsWith("Toner requests: 34400…", stopped.Summary);
            Assert.Contains("Run it again", stopped.Summary);
            Assert.Equal("Running", store.Get<JobRun>(mine.Id)!.Outcome);
            Assert.Equal("Running", store.Get<JobRun>(fresh.Id)!.Outcome);
            Assert.Equal(0, BackgroundRuns.StopAbandoned(store, () => world.Now));
        }
        finally
        {
            BackgroundRuns.Ended(mine.Id);
        }
    }
}
