using System.Collections.Concurrent;

namespace MyCRMRecords.Api;

/// <summary>
/// The companies' machines downloads (Download machines on the Companies page), built in the background: Advance is read
/// a few hundred BRNs at a time, with no page waiting on it, and the person saves the file when it is ready. One running
/// job per person; a job and its file are kept in memory for <see cref="KeepMinutes"/> after it ends (a website restart
/// loses it, and the page says to start again).
/// </summary>
public sealed class MachineDownloads
{
    public const string Running = "Running";
    public const string Ready = "Ready";
    public const string Failed = "Failed";

    /// <summary>How long a finished job (and its file) is kept.</summary>
    public const int KeepMinutes = 120;

    /// <summary>How many BRNs are read from Advance at once (a part that takes too long is read again in halves).</summary>
    public int ChunkSize { get; set; } = 500;

    /// <summary>Runs a job's work in the background (tests: at once, on the same thread).</summary>
    public Action<Action> Run { get; set; } = work =>
        System.Threading.Tasks.Task.Factory.StartNew(work, System.Threading.CancellationToken.None,
            System.Threading.Tasks.TaskCreationOptions.LongRunning, System.Threading.Tasks.TaskScheduler.Default);

    private readonly ConcurrentDictionary<string, Job> jobs = new(StringComparer.Ordinal);
    private readonly object starting = new();

    /// <summary>A download being built or built: how far it is, and its file when ready.</summary>
    public sealed class Job
    {
        private readonly object gate = new();
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Login { get; init; } = "";
        public DateTime Started { get; init; }
        /// <summary>The companies in the file, and the BRNs (each read once) Advance is read for.</summary>
        public int Companies { get; init; }
        public int Brns { get; init; }
        public int BrnsDone { get; private set; }
        public string State { get; private set; } = Running;
        public string Message { get; private set; } = "";
        public string FileName { get; private set; } = "";
        public byte[]? Content { get; private set; }
        public DateTime? Finished { get; private set; }
        /// <summary>Whether the person has saved the file once.</summary>
        public bool Saved { get; private set; }

        public void Done(int brns) { lock (gate) BrnsDone = Math.Min(Brns, BrnsDone + brns); }

        public void Complete(string fileName, byte[] content, string message, DateTime at)
        {
            lock (gate) { FileName = fileName; Content = content; Message = message; Finished = at; BrnsDone = Brns; State = Ready; }
        }

        public void Fail(string message, DateTime at)
        {
            lock (gate) { Message = message; Finished = at; State = Failed; }
        }

        public void MarkSaved() { lock (gate) Saved = true; }
    }

    private void Forget(DateTime now)
    {
        foreach (var job in jobs.Values)
            if (job.Finished is { } at && now - at > TimeSpan.FromMinutes(KeepMinutes)) jobs.TryRemove(job.Id, out _);
    }

    /// <summary>The person's latest job (running, or ended less than <see cref="KeepMinutes"/> ago), or null.</summary>
    public Job? Latest(string login, DateTime now)
    {
        Forget(now);
        return jobs.Values.Where(j => Strings.SameText(j.Login, login)).OrderByDescending(j => j.Started).FirstOrDefault();
    }

    /// <summary>The person's job by its Id, or null (another person's is never given).</summary>
    public Job? Get(string id, string login, DateTime now)
    {
        Forget(now);
        return jobs.TryGetValue((id ?? "").Trim(), out var job) && Strings.SameText(job.Login, login) ? job : null;
    }

    /// <summary>
    /// Starts a job for the person, its <paramref name="work"/> run in the background; while one of theirs is running, that
    /// one is given back instead (<c>Started</c> false) and nothing new starts.
    /// </summary>
    public (Job Job, bool Started) Start(string login, DateTime now, int companies, int brns, Action<Job> work)
    {
        Job job;
        lock (starting)
        {
            if (Latest(login, now) is { State: Running } running) return (running, false);
            job = new Job { Login = login.Trim(), Started = now, Companies = companies, Brns = brns };
            jobs[job.Id] = job;
        }
        Run(() =>
        {
            try { work(job); }
            catch (Exception ex) { job.Fail("The machines file could not be made: " + ex.Message, DateTime.Now); }
        });
        return (job, true);
    }
}
