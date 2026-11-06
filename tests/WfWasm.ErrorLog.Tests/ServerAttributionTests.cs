using WfWasm.ErrorLog;

namespace WfWasm.ErrorLog.Tests;

// SHELL-STAMP-1301-p5r3 (RVW-1105-0900-shell Job1): the live store proves
// repeats arrive WITHOUT backend+generation (77/77 rows unattributed), so
// merge alone can never adopt them. The receiving backend must stamp its
// own identity + served generation at ingestion, backfilling only what the
// client omitted. First occurrence message/source stay untouched.
public sealed class ServerAttributionTests
{
    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wferrlog-srvattr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void WithServerAttribution_Backfills_Missing_Backend_And_Generation()
    {
        var report = new ErrorReport("sig", "msg", null, "window.onerror",
            DateTimeOffset.UtcNow, Backend: null, Generation: "  ");

        var stamped = ErrorLog.WithServerAttribution(report, "127.0.0.1:5390", "20261105T1243Zw10");

        Assert.Equal("127.0.0.1:5390", stamped.Backend);
        Assert.Equal("20261105T1243Zw10", stamped.Generation);
    }

    [Fact]
    public void WithServerAttribution_Preserves_Client_Sent_Values()
    {
        var report = new ErrorReport("sig", "msg", null, "window.onerror",
            DateTimeOffset.UtcNow, Backend: "127.0.0.1:5391", Generation: "20261104T143900Zfinal");

        var stamped = ErrorLog.WithServerAttribution(report, "127.0.0.1:5390", "20261105T1243Zw10");

        Assert.Equal("127.0.0.1:5391", stamped.Backend);
        Assert.Equal("20261104T143900Zfinal", stamped.Generation);
    }

    [Fact]
    public void Log_Repeat_Without_Client_Stamps_Adopts_Server_Attribution()
    {
        string dir = NewDir();
        File.WriteAllText(Path.Combine(dir, ErrorLog.IndexFileName),
            """[{"signature":"sig-legacy","message":"legacy first message","source":"window.onerror","firstSeen":"2026-10-12T00:00:00Z","lastSeen":"2026-10-12T00:00:00Z","count":41,"status":"open","fixedNote":null,"postFixCount":0}]""");

        var bare = new ErrorReport("sig-legacy", "repeat body", null, "window.onerror",
            new DateTimeOffset(2026, 11, 5, 13, 5, 0, TimeSpan.Zero));
        var updated = ErrorLog.Log(dir,
            ErrorLog.WithServerAttribution(bare, "127.0.0.1:5390", "20261105T1243Zw10"));

        Assert.Equal(42, updated.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-10-12T00:00:00Z"), updated.FirstSeen);
        Assert.Equal("legacy first message", updated.Message);
        Assert.Equal(["127.0.0.1:5390"], updated.Backends ?? []);
        Assert.Equal(["20261105T1243Zw10"], updated.Generations ?? []);
    }
}
