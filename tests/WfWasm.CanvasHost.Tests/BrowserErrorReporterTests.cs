using System.Net;
using System.Reflection;
using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

public sealed class BrowserErrorReporterTests
{
    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        public List<string> Bodies { get; } = [];
        public List<string> Paths { get; } = [];
        public string[] SnapshotBodies()
        {
            lock (_gate)
            {
                return Bodies.ToArray();
            }
        }
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_gate)
            {
                Paths.Add(request.RequestUri?.AbsolutePath ?? "");
                Bodies.Add(body);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Report_Posts_Signature_Batch_To_Client_Errors_Endpoint()
    {
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        try
        {
            var ex = new NotSupportedException("GDI handles are not available on web.");
            BrowserErrorReporter.Report("toolstrip-click", ex);

            for (int i = 0; i < 100 && handler.SnapshotBodies().Length == 0; i++)
                await Task.Delay(50);
            // Other tests may raise first-chance exceptions through the
            // shared static probe while this runs: assert ours arrived,
            // not that it arrived alone.
            Assert.Contains("/api/client-errors", handler.Paths);
            string ours = Assert.Single(handler.SnapshotBodies(), b => b.Contains("toolstrip-click"));
            Assert.Contains("NotSupportedException", ours);
            Assert.Contains("GDI handles are not available on web.", ours);
        }
        finally
        {
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }

    [Fact]
    public async Task Report_With_Explicit_Frame_Uses_It_In_Signature()
    {
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        try
        {
            BrowserErrorReporter.Report("first-chance", new NotSupportedException("late stack"), "Icon.FromHandle");
            for (int i = 0; i < 100 && handler.SnapshotBodies().Length == 0; i++)
                await Task.Delay(50);
            string ours = Assert.Single(handler.SnapshotBodies(), b => b.Contains("late stack"));
            Assert.Contains("NotSupportedException|Icon.FromHandle|late stack", ours);
        }
        finally
        {
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }

    [Fact]
    public async Task Report_Suppresses_Transport_Failures_Without_Posting()
    {
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        try
        {
            // The report POST itself fails this way when the backend is
            // gone; posting it would amplify one crash into thousands.
            BrowserErrorReporter.Report("first-chance", new HttpRequestException("TypeError: Failed to fetch"));
            BrowserErrorReporter.Report("first-chance",
                new Microsoft.JSInterop.JSException("TypeError: Failed to fetch"));
            await Task.Delay(500);
            Assert.Empty(handler.SnapshotBodies());
        }
        finally
        {
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }

    [Fact]
    public async Task Report_Rate_Limits_Repeated_Signatures()
    {
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        try
        {
            for (int i = 0; i < 8; i++)
                BrowserErrorReporter.Report("first-chance", new Exception("rate storm"), "F.M");
            for (int i = 0; i < 100 && handler.SnapshotBodies().Length < 5; i++)
                await Task.Delay(50);
            await Task.Delay(500);
            Assert.Equal(5, handler.SnapshotBodies().Count(b => b.Contains("rate storm")));
        }
        finally
        {
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }

    [Fact]
    public async Task Report_Includes_Inner_Exception_Chain_In_Payload()
    {
        // Catch sites (click handlers) catch reflection wrappers whose
        // outer message/stack say nothing (observed: a calendar day-click
        // filed only the TargetInvocationException wrapper row, no inner
        // row). The actionable inner must travel inside the payload while
        // the outer keeps its signature-row identity.
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        try
        {
            Exception inner;
            try
            {
                throw new InvalidOperationException("inner-boom-chain-marker");
            }
            catch (Exception caught)
            {
                inner = caught;
            }

            BrowserErrorReporter.Report("control-click", new TargetInvocationException("wrapper", inner));
            // Parallel test classes share the static reporter, and the
            // shared first-chance probe reports this test's own marker
            // throw too: wait for OUR wrapper post (marker + wrapper
            // identity together occur only there), not merely the marker.
            static bool IsOurs(string b) =>
                b.Contains("inner-boom-chain-marker") && b.Contains("TargetInvocationException");
            for (int i = 0; i < 200 && !handler.SnapshotBodies().Any(IsOurs); i++)
                await Task.Delay(50);
            string ours = Assert.Single(handler.SnapshotBodies(), IsOurs);
            Assert.Contains("InvalidOperationException", ours);
        }
        finally
        {
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }

    [Fact]
    public void ComputeSignature_Starts_With_Exception_Type_Name()
    {
        string sig = BrowserErrorReporter.ComputeSignature(new InvalidOperationException("nope"));
        Assert.StartsWith("InvalidOperationException|", sig);
        Assert.EndsWith("|nope", sig);
    }

    [Fact]
    public void ComputeSignature_Includes_Top_Frame_From_Stack_Text()
    {
        Exception ex;
        try
        {
            ThrowForSignature();
            throw new InvalidOperationException("unreachable");
        }
        catch (Exception caught)
        {
            ex = caught;
        }

        string sig = BrowserErrorReporter.ComputeSignature(ex);
        Assert.Contains(nameof(ThrowForSignature), sig);
    }

    private static void ThrowForSignature() => throw new InvalidOperationException("framed");

    [Fact]
    public void TopFrame_Parses_At_Lines_And_Ignores_Noise()
    {
        Assert.Equal("Icon.FromHandle",
            BrowserErrorReporter.TopFrame("   at System.Drawing.Icon.FromHandle(IntPtr handle)\n   at Other.Call()"));
        Assert.Equal("", BrowserErrorReporter.TopFrame(null));
        Assert.Equal("", BrowserErrorReporter.TopFrame("no frames here"));
    }

    [Fact]
    public async Task Report_Prefers_Js_Transport_And_Skips_HttpClient()
    {
        // WF-R-NRE-0730: the fire-and-forget HttpClient POST completes via
        // JavaScriptExports.CompleteTask, whose synchronous NRE takes the
        // runtime down (exit 1). The JS-side fetch creates no managed Task,
        // so when it is available no HTTP call may happen at all.
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        var seen = new List<(string Url, string Json)>();
        // Marker-scoped: parallel suites share the static reporter, so only
        // swallow our own marker; every other report falls through to HTTP.
        BrowserErrorReporter.JsTransportOverride = (url, json) =>
        {
            if (!json.Contains("js-transport-marker"))
                return false;
            seen.Add((url, json));
            return true;
        };
        try
        {
            BrowserErrorReporter.Report("first-chance", new NotSupportedException("js-transport-marker"), "F.M");
            await Task.Delay(500);
            Assert.Single(seen);
            Assert.Contains("api/client-errors", seen[0].Url);
            Assert.Contains("NotSupportedException|F.M|js-transport-marker", seen[0].Json);
            Assert.Contains("first-chance", seen[0].Json);
            // Marker-scoped: parallel suites share the global HttpClient, so
            // only our own marker's absence proves the JS-first skip.
            Assert.DoesNotContain(handler.SnapshotBodies(), b => b.Contains("js-transport-marker"));
        }
        finally
        {
            BrowserErrorReporter.JsTransportOverride = null;
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }

    [Fact]
    public async Task Report_Falls_Back_To_Http_When_Js_Transport_Fails()
    {
        // The JS hook may be absent (desktop tests, stale shell): a failed
        // JS dispatch must still post through HttpClient.
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        BrowserErrorReporter.JsTransportOverride = (url, json) => false;
        try
        {
            BrowserErrorReporter.Report("first-chance", new NotSupportedException("http-fallback-marker"), "F.M");
            for (int i = 0; i < 100 && handler.SnapshotBodies().Length == 0; i++)
                await Task.Delay(50);
            string ours = Assert.Single(handler.SnapshotBodies(), b => b.Contains("http-fallback-marker"));
            Assert.Contains("NotSupportedException|F.M|http-fallback-marker", ours);
        }
        finally
        {
            BrowserErrorReporter.JsTransportOverride = null;
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }

    [Fact]
    public async Task Report_Carries_Backend_And_Generation_In_Payload()
    {
        // Error triage must tell which serving generation produced a
        // crash (public vs staging vs stale): the payload carries both.
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        BrowserErrorReporter.JsTransportOverride = (url, json) => false;
        BrowserErrorReporter.BackendProvider = () => "be-identity-marker";
        BrowserErrorReporter.GenerationProvider = () => "gen-identity-marker";
        try
        {
            BrowserErrorReporter.Report("first-chance", new NotSupportedException("identity-payload-marker"), "F.M");
            for (int i = 0; i < 100 && !handler.SnapshotBodies().Any(b => b.Contains("identity-payload-marker")); i++)
                await Task.Delay(50);
            string ours = Assert.Single(handler.SnapshotBodies(), b => b.Contains("identity-payload-marker"));
            Assert.Contains("be-identity-marker", ours);
            Assert.Contains("gen-identity-marker", ours);
        }
        finally
        {
            BrowserErrorReporter.BackendProvider = null;
            BrowserErrorReporter.GenerationProvider = null;
            BrowserErrorReporter.JsTransportOverride = null;
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }

    // GAP-genprov-0836-k7d2: GenerationProvider must resolve the stamped
    // window.wfwasmRuntime.generation (cached sync value read once via JS
    // at init), mirroring BackendProvider. FAILS until the cache seam
    // lands; do not weaken to provider-only coverage.
    [Fact]
    public async Task Report_Carries_Cached_Generation_After_NoteGeneration()
    {
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        BrowserErrorReporter.JsTransportOverride = (url, json) => false;
        BrowserErrorReporter.GenerationProvider = null;
        BrowserErrorReporter.NoteGeneration(null);
        BrowserErrorReporter.GenerationProvider = BrowserErrorReporter.CachedGeneration;
        try
        {
            BrowserErrorReporter.NoteGeneration("gen-cache-marker");
            BrowserErrorReporter.Report("first-chance", new NotSupportedException("cached-gen-marker"), "F.M");
            for (int i = 0; i < 100 && !handler.SnapshotBodies().Any(b => b.Contains("cached-gen-marker")); i++)
                await Task.Delay(50);
            string ours = Assert.Single(handler.SnapshotBodies(), b => b.Contains("cached-gen-marker"));
            Assert.Contains("gen-cache-marker", ours);
        }
        finally
        {
            BrowserErrorReporter.GenerationProvider = null;
            BrowserErrorReporter.NoteGeneration(null);
            BrowserErrorReporter.JsTransportOverride = null;
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }

    [Fact]
    public void NoteGeneration_Blank_And_Overlong_Are_Normalized()
    {
        try
        {
            BrowserErrorReporter.NoteGeneration("   ");
            Assert.Null(BrowserErrorReporter.CachedGeneration());
            BrowserErrorReporter.NoteGeneration(new string('g', 500));
            string? got = BrowserErrorReporter.CachedGeneration();
            Assert.NotNull(got);
            Assert.True(got.Length <= 120);
        }
        finally
        {
            BrowserErrorReporter.NoteGeneration(null);
        }
    }

    [Fact]
    public async Task Report_Backend_Falls_Back_To_Http_Authority()
    {
        // No provider and no server change: the reporting client's own
        // origin still names the serving pair.
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        BrowserErrorReporter.JsTransportOverride = (url, json) => false;
        BrowserErrorReporter.BackendProvider = null;
        BrowserErrorReporter.GenerationProvider = null;
        try
        {
            BrowserErrorReporter.Report("first-chance", new NotSupportedException("authority-fallback-marker"), "F.M");
            for (int i = 0; i < 100 && !handler.SnapshotBodies().Any(b => b.Contains("authority-fallback-marker")); i++)
                await Task.Delay(50);
            string ours = Assert.Single(handler.SnapshotBodies(), b => b.Contains("authority-fallback-marker"));
            Assert.Contains("127.0.0.1:5210", ours);
        }
        finally
        {
            BrowserErrorReporter.JsTransportOverride = null;
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }

    [Fact]
    public void FormatReportHead_Orders_App_Backend_Gen_Before_Signature()
    {
        string head = BrowserErrorReporter.FormatReportHead(
            "first-chance", "Sig|F|m", "paint", "127.0.0.1:5230", "20261104T143900Zfinal");
        Assert.Equal("[report:first-chance] app=paint backend=127.0.0.1:5230 gen=20261104T143900Zfinal Sig|F|m", head);
    }

    [Fact]
    public void FormatReportHead_Omits_Blank_Segments()
    {
        Assert.Equal("[report:first-chance] Sig|F|m",
            BrowserErrorReporter.FormatReportHead("first-chance", "Sig|F|m", null, null, null));
        Assert.Equal("[report:first-chance] app=paint Sig|F|m",
            BrowserErrorReporter.FormatReportHead("first-chance", "Sig|F|m", "paint", "  ", ""));
    }

    [Fact]
    public void TopFrame_Strips_Assembly_Qualified_Generics()
    {
        // Observed live: the raw site segment was
        // "32325, Culture=neutral, PublicKeyToken=null]].<MaybeGetWicData>b__0".
        string frame = BrowserErrorReporter.TopFrame(
            "   at PaintDotNet.Clipboard.ClipboardTransactionExtensions.<>c__DisplayClass18_0`1[[PaintDotNet.Imaging.IIccBitmap, PaintDotNet.Windows.Core, Version=5.112.9563.32325, Culture=neutral, PublicKeyToken=null]].<MaybeGetWicData>b__0(Stream stream)");
        Assert.Equal("<>c__DisplayClass18_0`1.<MaybeGetWicData>b__0", frame);
        Assert.DoesNotContain("Culture=", frame);
    }
}
