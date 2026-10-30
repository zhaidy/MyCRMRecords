using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MyCRMRecords.Api;

/// <summary>
/// How long the API took for each kind of request since the website last started (kept in memory, nothing stored): the
/// administrators' Speed tab lists the slowest, so a page slower than the aim (0.3 s) shows which call to make faster.
/// A request is grouped by its address with its numbers taken out (projects/123 → projects/#).
/// </summary>
public static class RequestTimes
{
    /// <summary>The time a page's call should take at most.</summary>
    public const int AimMs = 300;

    private sealed class Stats
    {
        public int Count;
        public int OverAim;
        public double TotalMs;
        public double MaxMs;
        public readonly Queue<double> Recent = new();
        public DateTime Last;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Stats> ByRoute = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex Number = new(@"(?<=/|^)\d+(?=/|$)", RegexOptions.Compiled);

    public static DateTime Since { get; private set; } = DateTime.Now;

    /// <summary>"GET projects/#": the method and the address with its numbers taken out.</summary>
    public static string Route(string method, string path) => method.ToUpperInvariant() + " " + Number.Replace(path.Trim('/'), "#");

    public static void Record(string method, string path, double ms, DateTime now)
    {
        var route = Route(method, path);
        lock (Gate)
        {
            if (!ByRoute.TryGetValue(route, out var s)) ByRoute[route] = s = new Stats();
            s.Count++;
            if (ms > AimMs) s.OverAim++;
            s.TotalMs += ms;
            s.MaxMs = Math.Max(s.MaxMs, ms);
            s.Recent.Enqueue(ms);
            while (s.Recent.Count > 50) s.Recent.Dequeue();
            s.Last = now;
        }
    }

    public sealed record Line(string Route, int Count, int OverAim, int AverageMs, int TypicalMs, int SlowMs, int MaxMs, DateTime Last);

    /// <summary>Every kind of request, the slowest (by its slow time: 9 in 10 of the last 50 were faster) first.</summary>
    public static IReadOnlyList<Line> Lines()
    {
        lock (Gate)
            return ByRoute.Select(kv =>
                {
                    var recent = kv.Value.Recent.OrderBy(v => v).ToList();
                    double At(double share) => recent.Count == 0 ? 0 : recent[Math.Min(recent.Count - 1, (int)Math.Floor(share * recent.Count))];
                    return new Line(kv.Key, kv.Value.Count, kv.Value.OverAim, (int)Math.Round(kv.Value.TotalMs / Math.Max(1, kv.Value.Count)),
                        (int)Math.Round(At(0.5)), (int)Math.Round(At(0.9)), (int)Math.Round(kv.Value.MaxMs), kv.Value.Last);
                })
                .OrderByDescending(l => l.SlowMs).ThenBy(l => l.Route, StringComparer.Ordinal).ToList();
    }

    public static void Clear(DateTime now)
    {
        lock (Gate) ByRoute.Clear();
        Since = now;
    }

    /// <summary>The Server-Timing header value: the browser's developer tools show it beside the request.</summary>
    public static string Header(double ms) => "app;desc=\"MyCRMRecords\";dur=" + ms.ToString("0.0", CultureInfo.InvariantCulture);

    public static Stopwatch Start() => Stopwatch.StartNew();
}
