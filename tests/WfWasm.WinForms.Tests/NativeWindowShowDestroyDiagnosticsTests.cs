extern alias Fork;

using ForkForms = Fork::System.Windows.Forms;
using Windows.Win32.Foundation;

namespace WfWasm.WinForms.Tests;

// RVW-1105-0900-runtime Job1 (WC2315 section4, fork items 1-2): the
// show/destroy notify paths must emit structured NRE diagnostics carrying
// the resource/dialog/handle identity plus the throwing-site type+method
// and a UTC correlation stamp (matching host [NRE-FUNNEL] lines), without
// changing the swallow-and-continue behavior. FAILS until the structured
// diagnostics land; do not weaken to the legacy full-ToString line.
public sealed class NativeWindowShowDestroyDiagnosticsTests
{
    public NativeWindowShowDestroyDiagnosticsTests()
    {
        // Hermetic gate: the fork notify budgets are static and shared
        // process-wide (every form show/destroy in the suite consumes
        // handle-keyed budgets; recycled handle values exhaust them
        // nondeterministically). Reset per test so only the diagnostics
        // contract is pinned, never the suite order. Assertions unchanged.
        ForkForms::NativeWindow.NotifyGateResetForTests();
    }

    private const int WM_SHOWWINDOW = 0x0018;
    private const int WM_DESTROY = 0x0002;

    private sealed class ShowBoomWindow : ForkForms::NativeWindow
    {
        public string Note = "show-boom-red";

        protected override void WndProc(ref ForkForms::Message m)
        {
            if (m.Msg == WM_SHOWWINDOW)
                throw new InvalidOperationException(Note);
            base.WndProc(ref m);
        }
    }

    private sealed class DestroyBoomWindow : ForkForms::NativeWindow
    {
        protected override void WndProc(ref ForkForms::Message m)
        {
            if (m.Msg == WM_DESTROY)
                throw new InvalidOperationException("destroy-boom-red");
            base.WndProc(ref m);
        }
    }

    private sealed class QuietWindow : ForkForms::NativeWindow
    {
        public readonly List<int> Seen = new();

        protected override void WndProc(ref ForkForms::Message m)
        {
            Seen.Add(m.Msg);
            base.WndProc(ref m);
        }
    }

    private static string Capture(Action action)
    {
        var sb = new System.Text.StringBuilder();
        var old = System.Console.Out;
        System.Console.SetOut(new System.IO.StringWriter(sb));
        try
        {
            action();
        }
        finally
        {
            System.Console.SetOut(old);
        }
        return sb.ToString();
    }

    [Fact]
    public void Show_ThrowingWndProc_Emits_Structured_Diagnostic_And_Does_Not_Throw()
    {
        using var box = new ForkForms::TextBox { Name = "showDiagRed" };
        nint handle = box.Handle;
        var spy = new ShowBoomWindow();
        spy.AssignHandle(handle);
        string log;
        try
        {
            log = Capture(() => ForkForms::NativeWindow.WasmNotifyShowWindow(new HWND(handle), true));
        }
        finally
        {
            spy.ReleaseHandle();
        }

        Assert.Contains("[WasmNotifyShowWindow]", log);
        Assert.Contains("site=", log);
        Assert.Contains(nameof(ShowBoomWindow), log);
        Assert.Contains("show-boom-red", log);
        Assert.Contains("utc=", log);
    }

    [Fact]
    public void Destroy_ThrowingWndProc_Emits_Structured_Diagnostic_And_Does_Not_Throw()
    {
        using var box = new ForkForms::TextBox { Name = "destroyDiagRed" };
        nint handle = box.Handle;
        var spy = new DestroyBoomWindow();
        spy.AssignHandle(handle);
        string log;
        try
        {
            log = Capture(() => ForkForms::NativeWindow.WasmNotifyDestroyWindow(new HWND(handle)));
        }
        finally
        {
            spy.ReleaseHandle();
        }

        Assert.Contains("[WasmNotifyDestroyWindow]", log);
        Assert.Contains("site=", log);
        Assert.Contains(nameof(DestroyBoomWindow), log);
        Assert.Contains("destroy-boom-red", log);
        Assert.Contains("utc=", log);
    }

    [Fact]
    public void Show_Benign_Delivers_WmShowWindow_With_Correlation_Line()
    {
        using var box = new ForkForms::TextBox { Name = "showBenignRed" };
        nint handle = box.Handle;
        var spy = new QuietWindow();
        spy.AssignHandle(handle);
        string log;
        try
        {
            log = Capture(() => ForkForms::NativeWindow.WasmNotifyShowWindow(new HWND(handle), true));
        }
        finally
        {
            spy.ReleaseHandle();
        }

        Assert.Contains(WM_SHOWWINDOW, spy.Seen);
        Assert.Contains("h=", log);
        Assert.Contains("utc=", log);
    }

    [Fact]
    public void Unknown_Handle_Is_Silent_NoOp()
    {
        // Behavior-preservation pin: handles with no attached window stay
        // silent no-ops (existing early-return), diagnostics included.
        string showLog = Capture(() => ForkForms::NativeWindow.WasmNotifyShowWindow(new HWND(new IntPtr(0x12345678)), true));
        string destroyLog = Capture(() => ForkForms::NativeWindow.WasmNotifyDestroyWindow(new HWND(new IntPtr(0x12345678))));
        Assert.Equal("", showLog);
        Assert.Equal("", destroyLog);
    }

    [Fact]
    public void Repeated_Throwing_Show_Diagnostics_Are_Bounded()
    {
        using var box = new ForkForms::TextBox { Name = "showBoundRed" };
        nint handle = box.Handle;
        var spy = new ShowBoomWindow { Note = "show-boom-bound" };
        spy.AssignHandle(handle);
        string log;
        try
        {
            log = Capture(() =>
            {
                for (int i = 0; i < 20; i++)
                    ForkForms::NativeWindow.WasmNotifyShowWindow(new HWND(handle), true);
            });
        }
        finally
        {
            spy.ReleaseHandle();
        }

        int lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(l => l.Contains("[WasmNotifyShowWindow]"));
        Assert.True(lines <= 16, $"expected bounded diagnostics, got {lines} lines for 20 throwing shows");
        Assert.True(lines >= 2, "expected at least the entry + diagnostic pair to be logged");
    }
}
