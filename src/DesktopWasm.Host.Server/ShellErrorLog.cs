using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Host.Server;

/// <summary>
/// Per-app error inventory for the unified shell. Same reporting shape as the
/// imported WinForms host (batch cap 50, signature index, fixed status) with
/// app + session attribution aligned across both frameworks.
/// </summary>
public sealed record ShellErrorReport(
    string? App, string? Session, string Kind, string Message, string? Stack, string? Source);

public static class ShellErrorLog
{
    public const string StatusOpen = "open";
    public const string StatusFixed = "fixed";
    private const int MaxBatch = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string SignatureOf(string app, string session, string kind, string message) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{app}\n{session}\n{kind}\n{message}")))[..32];

    public static int LogBatch(string logDir, IEnumerable<ShellErrorReport> batch)
    {
        var reports = batch?.Where(r => r is not null).Take(MaxBatch + 1).ToList()
            ?? throw new ArgumentException("empty batch");
        if (reports.Count == 0)
            throw new ArgumentException("empty batch");
        if (reports.Count > MaxBatch)
            throw new ArgumentException("batch too large (max 50)");
        Directory.CreateDirectory(logDir);
        int logged = 0;
        foreach (var report in reports)
        {
            string app = string.IsNullOrWhiteSpace(report.App) ? "unknown" : ShellIds.Normalize(report.App);
            string session = string.IsNullOrWhiteSpace(report.Session) ? "-" : report.Session.Trim();
            string kind = string.IsNullOrWhiteSpace(report.Kind) ? "error" : report.Kind.Trim();
            string message = (report.Message ?? "").Split('\n')[0];
            if (string.IsNullOrWhiteSpace(message))
                continue;
            string signature = SignatureOf(app, session, kind, message);
            var entry = new
            {
                signature, app, session, kind, message,
                stack = report.Stack, source = report.Source,
                status = StatusOpen,
                atUtc = DateTime.UtcNow.ToString("o"),
            };
            File.AppendAllText(Path.Combine(logDir, "shell-errors.jsonl"),
                JsonSerializer.Serialize(entry, Json) + "\n");
            logged++;
        }
        if (logged == 0)
            throw new ArgumentException("empty batch");
        return logged;
    }

    public static IReadOnlyList<JsonElement> ReadIndex(string logDir, string? app)
    {
        string path = Path.Combine(logDir, "shell-errors.jsonl");
        if (!File.Exists(path))
            return [];
        string? filter = string.IsNullOrWhiteSpace(app) ? null : ShellIds.Normalize(app);
        var fixedBy = ReadFixed(Path.Combine(logDir, "shell-fixed.json"));
        var latest = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement.Clone();
                string signature = root.GetProperty("signature").GetString() ?? "";
                if (filter is not null &&
                    !string.Equals(root.GetProperty("app").GetString(), filter, StringComparison.Ordinal))
                    continue;
                latest[signature] = root;
            }
            catch (JsonException) { }
        }
        var result = new List<JsonElement>();
        foreach (var (signature, element) in latest.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            using var doc = JsonDocument.Parse(element.GetRawText());
            var obj = doc.RootElement.Clone();
            var mutable = JsonSerializer.Deserialize<Dictionary<string, object?>>(obj.GetRawText(), Json)!;
            if (fixedBy.TryGetValue(signature, out string? note))
            {
                mutable["status"] = StatusFixed;
                mutable["fixNote"] = note;
            }
            result.Add(JsonSerializer.SerializeToElement(mutable, Json));
        }
        return result;
    }

    public static bool MarkFixed(string logDir, string signature, string note)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return false;
        Directory.CreateDirectory(logDir);
        string path = Path.Combine(logDir, "shell-fixed.json");
        var fixedBy = ReadFixed(path);
        string key = signature.Trim();
        if (!IndexContains(Path.Combine(logDir, "shell-errors.jsonl"), key))
            return false;
        fixedBy[key] = note ?? "";
        File.WriteAllText(path, JsonSerializer.Serialize(fixedBy, Json));
        return true;
    }

    private static Dictionary<string, string> ReadFixed(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(path), Json) ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static bool IndexContains(string path, string signature)
    {
        if (!File.Exists(path))
            return false;
        foreach (string line in File.ReadLines(path))
        {
            if (line.Contains(signature, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
