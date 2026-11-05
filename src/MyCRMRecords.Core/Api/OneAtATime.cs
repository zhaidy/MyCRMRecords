using System.Collections.Concurrent;

namespace MyCRMRecords.Api;

/// <summary>
/// Work a person may run only once at a time (a big download): <see cref="TryStart"/> gives a handle to dispose when the
/// work ends, or null while the same key is still running. Kept in memory by the one <see cref="CrmApp"/> of the website.
/// </summary>
public sealed class OneAtATime
{
    private readonly ConcurrentDictionary<string, byte> running = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Starts <paramref name="key"/>; null when it is running already.</summary>
    public IDisposable? TryStart(string key) => running.TryAdd(key, 0) ? new Done(running, key) : null;

    /// <summary>Whether <paramref name="key"/> is running.</summary>
    public bool IsRunning(string key) => running.ContainsKey(key);

    private sealed class Done(ConcurrentDictionary<string, byte> running, string key) : IDisposable
    {
        public void Dispose() => running.TryRemove(key, out _);
    }
}
