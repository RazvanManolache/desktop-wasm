using WfWasm.ErrorLog;

namespace WfWasm.ErrorLog.Tests;

// FP-dw-shell-20261104T2335-265 stamp adoption (a): backend+generation
// accept/persist/index/render. Smallest focused coverage for the two new
// attribution keys; legacy rows and existing aggregation behavior untouched.
public sealed class StampAdoptionTests
{
    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wferrlog-stamp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static ErrorReport Report(string sig, string? backend = null, string? generation = null)
        => new(sig, "msg\nsecond line", "stack", "toolstrip-click", DateTimeOffset.UtcNow,
            Backend: backend, Generation: generation);

    [Fact]
    public void Log_Persists_Backend_And_Generation_In_Jsonl_And_Index()
    {
        string dir = NewDir();
        var entry = ErrorLog.Log(dir, Report("sig-stamp", "127.0.0.1:5390", "20261104T2144Zw6"));

        Assert.Equal(["127.0.0.1:5390"], entry.Backends ?? []);
        Assert.Equal(["20261104T2144Zw6"], entry.Generations ?? []);
        string line = File.ReadAllLines(Path.Combine(dir, ErrorLog.LogFileName)).Single();
        Assert.Contains("\"backend\":\"127.0.0.1:5390\"", line);
        Assert.Contains("\"generation\":\"20261104T2144Zw6\"", line);
        var fetched = Assert.Single(ErrorLog.ReadIndex(dir));
        Assert.Equal(["127.0.0.1:5390"], fetched.Backends ?? []);
        Assert.Equal(["20261104T2144Zw6"], fetched.Generations ?? []);
    }

    [Fact]
    public void Log_Merges_Backends_And_Generations_Across_Reports()
    {
        string dir = NewDir();
        ErrorLog.Log(dir, Report("sig-merge", "127.0.0.1:5390", "20261104T2144Zw6"));
        var second = ErrorLog.Log(dir, Report("sig-merge", "127.0.0.1:5230", "20261104T143900Zfinal"));

        Assert.Equal(2, second.Count);
        Assert.Equal(["127.0.0.1:5230", "127.0.0.1:5390"], second.Backends ?? []);
        Assert.Equal(["20261104T143900Zfinal", "20261104T2144Zw6"], second.Generations ?? []);
    }

    [Fact]
    public void ReadIndex_Legacy_Rows_Without_Stamps_Deserialize_With_Empty_Stamps()
    {
        string dir = NewDir();
        File.WriteAllText(Path.Combine(dir, ErrorLog.IndexFileName),
            """[{"signature":"s","message":"m","source":"src","firstSeen":"2026-11-04T00:00:00Z","lastSeen":"2026-11-04T00:00:00Z","count":1,"status":"open","fixedNote":null,"postFixCount":0}]""");

        var entry = Assert.Single(ErrorLog.ReadIndex(dir));
        Assert.Empty(entry.Backends ?? []);
        Assert.Empty(entry.Generations ?? []);
        Assert.Empty(entry.Apps ?? []);
    }

    // Assignment RVW-1105-0900-shell Job1: a real repeat of a legacy
    // (pre-attribution) signature must adopt the current occurrence's
    // backend+generation into the index without rewriting the known
    // first occurrence (FirstSeen + first message stay put).
    [Fact]
    public void Log_Repeat_Of_Legacy_Row_Adopts_Stamps_Without_Rewriting_FirstSeen()
    {
        string dir = NewDir();
        File.WriteAllText(Path.Combine(dir, ErrorLog.IndexFileName),
            """[{"signature":"sig-legacy","message":"legacy first message","source":"window.onerror","firstSeen":"2026-10-12T00:00:00Z","lastSeen":"2026-10-12T00:00:00Z","count":77,"status":"open","fixedNote":null,"postFixCount":0}]""");

        var repeatTime = new DateTimeOffset(2026, 11, 5, 7, 30, 0, TimeSpan.Zero);
        var updated = ErrorLog.Log(dir, new ErrorReport("sig-legacy", "new body message\nsecond line",
            "new stack", "window.onerror", repeatTime,
            Backend: "127.0.0.1:5390", Generation: "20261104T2144Zw6"));

        Assert.Equal(78, updated.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-10-12T00:00:00Z"), updated.FirstSeen);
        Assert.Equal(repeatTime, updated.LastSeen);
        Assert.Equal(["127.0.0.1:5390"], updated.Backends ?? []);
        Assert.Equal(["20261104T2144Zw6"], updated.Generations ?? []);
        Assert.Equal("legacy first message", updated.Message);
        string last = File.ReadAllLines(Path.Combine(dir, ErrorLog.LogFileName)).Last();
        Assert.Contains("\"backend\":\"127.0.0.1:5390\"", last);
        Assert.Contains("\"generation\":\"20261104T2144Zw6\"", last);
    }

    // Same Job1 clause: a genuinely new signature still stamps its own row
    // even when the store already holds legacy rows.
    [Fact]
    public void Log_New_Signature_Alongside_Legacy_Creates_Stamped_Row()
    {
        string dir = NewDir();
        File.WriteAllText(Path.Combine(dir, ErrorLog.IndexFileName),
            """[{"signature":"sig-legacy","message":"legacy first message","source":"window.onerror","firstSeen":"2026-10-12T00:00:00Z","lastSeen":"2026-10-12T00:00:00Z","count":77,"status":"open","fixedNote":null,"postFixCount":0}]""");

        var created = ErrorLog.Log(dir, Report("sig-fresh", "127.0.0.1:5390", "20261104T2144Zw6"));

        Assert.Equal(1, created.Count);
        Assert.Equal(["127.0.0.1:5390"], created.Backends ?? []);
        Assert.Equal(["20261104T2144Zw6"], created.Generations ?? []);
        Assert.Equal(2, ErrorLog.ReadIndex(dir).Count);
    }

    [Fact]
    public void InventoryPage_Renders_Stamps_Escaped()
    {
        string dir = NewDir();
        ErrorLog.Log(dir, Report("sig-page", "<b>host</b>", "<i>gen</i>"));

        string html = ErrorInventoryPage.Render(ErrorLog.ReadIndex(dir));
        Assert.Contains("Backends</th>", html);
        Assert.Contains("Generations</th>", html);
        Assert.DoesNotContain("<b>host</b>", html);
        Assert.DoesNotContain("<i>gen</i>", html);
        Assert.Contains("&lt;b&gt;host&lt;/b&gt;", html);
        Assert.Contains("&lt;i&gt;gen&lt;/i&gt;", html);
    }
}
