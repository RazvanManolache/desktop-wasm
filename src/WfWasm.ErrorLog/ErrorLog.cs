using System.Text.Json;

namespace WfWasm.ErrorLog;

/// <summary>
/// One browser/client error report, as posted by the JS hook
/// (canvas-bridge.js) or the in-WASM <c>BrowserErrorReporter</c>.
/// </summary>
public sealed record ErrorReport(
    string Signature,
    string Message,
    string? Stack,
    string Source,
    DateTimeOffset Time,
    string? Context = null,
    string? App = null,
    string? SessionId = null,
    string? AppLabel = null,
    string? Backend = null,
    string? Generation = null);

/// <summary>
/// Aggregated inventory entry per error signature.
/// </summary>
public sealed record ErrorIndexEntry(
    string Signature,
    string Message,
    string Source,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    int Count,
    string Status,
    string? FixedNote,
    int PostFixCount,
    string[]? Apps = null,
    string[]? Backends = null,
    string[]? Generations = null,
    ReopenRecord[]? ReopenHistory = null);

/// <summary>
/// One reopen event: a fixed signature returned to open with a reason.
/// The original <see cref="ErrorIndexEntry.FixedNote"/> is preserved as
/// the fix evidence; history records why it was reopened.
/// </summary>
public sealed record ReopenRecord(DateTimeOffset AtUtc, string Reason);

/// <summary>
/// File-backed browser error sink. Appends every report to
/// <c>client-errors.jsonl</c> and maintains <c>error-index.json</c>,
/// keyed by signature. Reports arriving after a signature was marked
/// fixed bump <see cref="ErrorIndexEntry.PostFixCount"/> so regressions
/// stay visible instead of silently reopening.
/// <para/>
/// <c>logDir</c> is an explicit parameter (not a process-global path) so
/// tests can point at a temp directory.
/// </summary>
public static class ErrorLog
{
    public const string LogFileName = "client-errors.jsonl";
    public const string IndexFileName = "error-index.json";
    public const string StatusOpen = "open";
    public const string StatusFixed = "fixed";

    private static readonly object s_gate = new();
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string ComputeSignature(string exceptionType, string? topFrame, string? firstLine)
    {
        string type = Truncate((exceptionType ?? "").Trim(), 120);
        string frame = Truncate((topFrame ?? "").Trim(), 200);
        string line = Truncate((firstLine ?? "").Trim(), 300);
        if (type.Length == 0 && frame.Length == 0 && line.Length == 0)
            return "unknown";
        return $"{type}|{frame}|{line}";
    }

    public static ErrorIndexEntry Log(string logDir, ErrorReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDir);
        ArgumentNullException.ThrowIfNull(report);
        Directory.CreateDirectory(logDir);

        string signature = Truncate(report.Signature.Trim(), 640);
        if (signature.Length == 0)
            signature = ComputeSignature("", null, report.Message);
        string message = Truncate(FirstLine(report.Message), 500);
        string? stack = report.Stack == null ? null : Truncate(report.Stack, 8000);
        string? context = report.Context == null ? null : Truncate(report.Context, 8000);
        string source = Truncate(report.Source.Trim(), 120);
        string? app = string.IsNullOrWhiteSpace(report.App) ? null : Truncate(report.App.Trim(), 120);
        string? backend = string.IsNullOrWhiteSpace(report.Backend) ? null : Truncate(report.Backend.Trim(), 120);
        string? generation = string.IsNullOrWhiteSpace(report.Generation) ? null : Truncate(report.Generation.Trim(), 120);
        var stamped = report with
        {
            Signature = signature,
            Message = message,
            Stack = stack,
            Source = source,
            Context = context,
            App = app,
            SessionId = report.SessionId == null ? null : Truncate(report.SessionId.Trim(), 120),
            AppLabel = report.AppLabel == null ? null : Truncate(report.AppLabel.Trim(), 200),
            Backend = backend,
            Generation = generation,
        };

        lock (s_gate)
        {
            File.AppendAllText(Path.Combine(logDir, LogFileName), JsonSerializer.Serialize(stamped, s_json) + "\n");
            var entries = ReadIndexCore(logDir);
            int at = entries.FindIndex(e => e.Signature == signature);
            ErrorIndexEntry updated;
            if (at < 0)
            {
                updated = new ErrorIndexEntry(signature, message, source, stamped.Time, stamped.Time, 1, StatusOpen, null, 0,
                    app == null ? [] : [app],
                    backend == null ? [] : [backend],
                    generation == null ? [] : [generation]);
                entries.Add(updated);
            }
            else
            {
                var cur = entries[at];
                bool fixedNow = cur.Status == StatusFixed;
                updated = cur with
                {
                    LastSeen = stamped.Time,
                    Count = cur.Count + 1,
                    PostFixCount = cur.PostFixCount + (fixedNow ? 1 : 0),
                    Apps = MergeToken(cur.Apps, app),
                    Backends = MergeToken(cur.Backends, backend),
                    Generations = MergeToken(cur.Generations, generation),
                };
                entries[at] = updated;
            }

            WriteIndexCore(logDir, entries);
            return updated;
        }
    }

    public static IReadOnlyList<ErrorIndexEntry> ReadIndex(string logDir, string? app = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDir);
        lock (s_gate)
        {
            var entries = ReadIndexCore(logDir);
            if (string.IsNullOrWhiteSpace(app))
                return entries;
            string want = app.Trim();
            return entries.Where(e => (e.Apps ?? []).Contains(want, StringComparer.OrdinalIgnoreCase)).ToList();
        }
    }

    public static bool MarkFixed(string logDir, string signature, string note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        lock (s_gate)
        {
            var entries = ReadIndexCore(logDir);
            int at = entries.FindIndex(e => e.Signature == signature);
            if (at < 0)
                return false;
            entries[at] = entries[at] with { Status = StatusFixed, FixedNote = Truncate(note.Trim(), 500) };
            WriteIndexCore(logDir, entries);
            return true;
        }
    }

    /// <summary>
    /// Reopens a fixed signature: status returns to open with the reason
    /// recorded in history. Count, FirstSeen, LastSeen, PostFixCount,
    /// FixedNote and attribution are preserved; the original fixed-note
    /// stays as the fix evidence. Unknown signatures return false.
    /// </summary>
    public static bool MarkOpen(string logDir, string signature, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        lock (s_gate)
        {
            var entries = ReadIndexCore(logDir);
            int at = entries.FindIndex(e => e.Signature == signature);
            if (at < 0)
                return false;
            var history = new List<ReopenRecord>(entries[at].ReopenHistory ?? [])
            {
                new ReopenRecord(DateTimeOffset.UtcNow, Truncate(reason.Trim(), 500)),
            };
            while (history.Count > 20)
                history.RemoveAt(0);
            entries[at] = entries[at] with { Status = StatusOpen, ReopenHistory = history.ToArray() };
            WriteIndexCore(logDir, entries);
            return true;
        }
    }

    /// <summary>
    /// Server-side stamp adoption (SHELL-STAMP-1301-p5r3): the receiving
    /// backend fills in backend+generation the client omitted, so repeats
    /// of legacy unattributed signatures adopt current-occurrence stamps
    /// through the normal merge path. Client-sent values always win;
    /// first-occurrence message/source are untouched (handled in Log).
    /// </summary>
    public static ErrorReport WithServerAttribution(ErrorReport report, string? backend, string? generation)
    {
        ArgumentNullException.ThrowIfNull(report);
        string? b = string.IsNullOrWhiteSpace(report.Backend) && !string.IsNullOrWhiteSpace(backend)
            ? backend.Trim() : report.Backend;
        string? g = string.IsNullOrWhiteSpace(report.Generation) && !string.IsNullOrWhiteSpace(generation)
            ? generation.Trim() : report.Generation;
        if (ReferenceEquals(b, report.Backend) && ReferenceEquals(g, report.Generation))
            return report;
        return report with { Backend = b, Generation = g };
    }

    private static List<ErrorIndexEntry> ReadIndexCore(string logDir)
    {
        string path = Path.Combine(logDir, IndexFileName);
        if (!File.Exists(path))
            return [];
        try
        {
            var entries = JsonSerializer.Deserialize<List<ErrorIndexEntry>>(File.ReadAllText(path), s_json) ?? [];
            // Pre-attribution rows lack Apps/Backends/Generations: normalize
            // so consumers never see null. Pre-reopen rows lack history too.
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Apps is null || entries[i].Backends is null || entries[i].Generations is null || entries[i].ReopenHistory is null)
                    entries[i] = entries[i] with
                    {
                        Apps = entries[i].Apps ?? [],
                        Backends = entries[i].Backends ?? [],
                        Generations = entries[i].Generations ?? [],
                        ReopenHistory = entries[i].ReopenHistory ?? [],
                    };
            }

            return entries;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // Distinct sorted tokens per signature, capped so one noisy shared
    // signature (e.g. a framework first-chance) cannot grow the index row.
    // Serves app slugs, backend hosts and generation ids alike.
    private static string[] MergeToken(string[]? current, string? value)
    {
        var set = new SortedSet<string>(current ?? [], StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(value))
            set.Add(value);
        return set.Take(8).ToArray();
    }

    private static void WriteIndexCore(string logDir, List<ErrorIndexEntry> entries)
    {
        string path = Path.Combine(logDir, IndexFileName);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(entries, s_json));
        File.Move(tmp, path, overwrite: true);
    }

    private static string FirstLine(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        int nl = value.IndexOf('\n');
        return (nl < 0 ? value : value.Substring(0, nl)).Trim();
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value;
        return value.Substring(0, max);
    }
}
