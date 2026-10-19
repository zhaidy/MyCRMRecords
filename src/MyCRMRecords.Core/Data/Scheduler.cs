using System.Globalization;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Data;

/// <summary>What a scheduled task did.</summary>
public sealed record JobOutcome(string Summary, int Processed = 0, DateTime? Watermark = null);

/// <summary>
/// Runs MyCRMRecords's scheduled tasks inside the IIS application (none yet: MyCRM had no scheduled timer), and the sync with MyCRM: checks every 30 seconds and
/// runs each task that is turned on when it is due (every N minutes, or daily at a time). When each started is kept in
/// App_Data/scheduled-tasks.json under a lock file, so two IIS worker processes never run a task twice; every run is
/// recorded in dbo.CrJobRun.
/// </summary>
public sealed class Scheduler(IRecordStore store, Func<CrmSettings> settings, string stateFile,
    IReadOnlyDictionary<string, Func<string, JobOutcome>> jobs, Func<DateTime> clock, Action? everyTick = null)
{
    private readonly object _gate = new();
    private Timer? _timer;

    private sealed class State
    {
        public DateTime? LastStart { get; set; }
        public DateTime? RunningSince { get; set; }
    }

    public void Start()
    {
        lock (_gate)
        {
            _timer ??= new Timer(_ => Tick(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }
    }

    public void Tick()
    {
        try
        {
            // The sync with MyCRM keeps its own timing and switch (Settings, Sync with MyCRM tab).
            everyTick?.Invoke();
        }
        catch (Exception)
        {
            // Tried again at the next tick; every sync is logged.
        }
        try
        {
            var s = settings();
            if (!s.Jobs.Enabled) return;
            var now = clock();
            foreach (var job in Jobs.All)
            {
                var schedule = s.Jobs.For(job);
                if (!schedule.Enabled || !jobs.ContainsKey(job)) continue;
                if (!Claim(job, schedule, now)) continue;
                Run(job, "Scheduled", claimed: true);
            }
        }
        catch (Exception)
        {
            // A failed check is tried again at the next tick.
        }
    }

    /// <summary>Whether the task is due at <paramref name="now"/>, given when it last started.</summary>
    public static bool IsDue(JobSchedule schedule, DateTime? lastStart, DateTime now)
    {
        if (!OnDay(schedule, now)) return false;
        if (schedule.EveryMinutes > 0) return lastStart is null || lastStart.Value.AddMinutes(schedule.EveryMinutes) <= now.AddSeconds(5);
        if (TimeSpan.TryParseExact(schedule.DailyAt.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out var at))
            return now.TimeOfDay >= at && (lastStart is null || lastStart.Value.Date < now.Date || lastStart.Value.TimeOfDay < at);
        return false;
    }

    /// <summary>Whether <paramref name="now"/> is one of the task's days (0 Sunday to 6 Saturday); every day when none is set.</summary>
    public static bool OnDay(JobSchedule schedule, DateTime now)
    {
        var days = (schedule.Days ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => int.TryParse(d, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : -1).Where(n => n is >= 0 and <= 6).ToList();
        return days.Count == 0 || days.Contains((int)now.DayOfWeek);
    }

    private bool Claim(string job, JobSchedule schedule, DateTime now) => WithLock(states =>
    {
        var state = states.TryGetValue(job, out var found) ? found : states[job] = new State();
        if (state.RunningSince is { } since && now - since < TimeSpan.FromHours(2)) return false;
        if (!IsDue(schedule, state.LastStart, now)) return false;
        state.LastStart = now;
        state.RunningSince = now;
        return true;
    });

    /// <summary>Runs a task now (its Run now button, or when due); returns the record of the run.</summary>
    public JobRun Run(string job, string by, bool claimed = false)
    {
        if (!jobs.TryGetValue(job, out var work)) throw new InvalidOperationException("No such task.");
        if (!claimed)
        {
            var refused = WithLock(states =>
            {
                var state = states.TryGetValue(job, out var found) ? found : states[job] = new State();
                if (state.RunningSince is { } since && clock() - since < TimeSpan.FromHours(2)) return true;
                state.RunningSince = clock();
                return false;
            });
            if (refused) throw new InvalidOperationException("The task is running now; try again when it has finished.");
        }
        var run = store.Insert(new JobRun { Job = job, StartedAt = clock(), Outcome = "Running", By = by });
        BackgroundRuns.Started(run.Id);
        try
        {
            var outcome = work(by);
            run.Outcome = "Done";
            run.Summary = outcome.Summary;
            run.Processed = outcome.Processed;
            run.Watermark = outcome.Watermark;
        }
        catch (Exception ex)
        {
            run.Outcome = "Failed";
            run.Summary = ex.Message;
        }
        run.FinishedAt = clock();
        store.Update(run);
        BackgroundRuns.Ended(run.Id);
        WithLock(states =>
        {
            if (states.TryGetValue(job, out var state)) state.RunningSince = null;
            return true;
        });
        return run;
    }

    public IReadOnlyDictionary<string, DateTime?> LastStarts() =>
        WithLock(states => (IReadOnlyDictionary<string, DateTime?>)states.ToDictionary(s => s.Key, s => s.Value.LastStart), save: false);

    private T WithLock<T>(Func<Dictionary<string, State>, T> action, bool save = true)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
            var deadline = DateTime.UtcNow.AddSeconds(15);
            FileStream? fileLock = null;
            while (fileLock is null)
            {
                try { fileLock = new FileStream(stateFile + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(50); }
            }
            using (fileLock)
            {
                Dictionary<string, State> states;
                try
                {
                    states = File.Exists(stateFile) ? Json.Deserialize<Dictionary<string, State>>(File.ReadAllText(stateFile)) ?? new() : new();
                }
                catch (Exception)
                {
                    states = new();
                }
                var result = action(states);
                if (save)
                {
                    var temp = stateFile + ".tmp";
                    File.WriteAllText(temp, Json.Serialize(states, indented: true));
                    if (File.Exists(stateFile)) File.Replace(temp, stateFile, null);
                    else File.Move(temp, stateFile);
                }
                return result;
            }
        }
    }
}
