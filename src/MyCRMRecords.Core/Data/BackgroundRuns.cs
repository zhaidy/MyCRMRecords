using System.Collections.Concurrent;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Data;

/// <summary>
/// The runs this website process is doing in the background (the import, scheduled tasks). A run still recorded as
/// Running that no process is doing was cut off when the website restarted (an update was installed, IIS recycled it):
/// it is marked Stopped, so it no longer shows as running and can be started again.
/// </summary>
public static class BackgroundRuns
{
    public const string Stopped = "Stopped";
    private static readonly ConcurrentDictionary<long, bool> Mine = new();

    public static void Started(long runId) => Mine[runId] = true;

    public static void Ended(long runId) => Mine.TryRemove(runId, out _);

    /// <summary>
    /// Marks Stopped the runs recorded as Running that this process is not doing (started more than a minute ago, so a
    /// run being started by another process at that moment is left alone); returns how many.
    /// </summary>
    public static int StopAbandoned(IRecordStore store, Func<DateTime> clock)
    {
        var stopped = 0;
        foreach (var run in store.Find<JobRun>(Query.Where("Outcome", "Running")))
        {
            if (Mine.ContainsKey(run.Id) || clock() - run.StartedAt < TimeSpan.FromMinutes(1)) continue;
            run.Outcome = Stopped;
            run.FinishedAt = clock();
            run.Summary = (run.Summary.Length > 0 ? run.Summary + " " : "") +
                "Stopped: the website restarted while it ran (an update was installed). Run it again: what was done before is kept.";
            store.Update(run);
            stopped++;
        }
        return stopped;
    }
}
