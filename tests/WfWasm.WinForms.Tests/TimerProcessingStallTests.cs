// WC1230 vendor processing-stall probe (token WC1230-VENDOR-PROCSTALL-5001):
// INTENTIONAL triage pin, FAILS until the bridge lane wires a tick source.
// Chain (all evidenced in reports/WC-20261103T1230-vendor-procstall.md):
//   wizard page3 NextClick -> MessageBoxAdv.Show returns immediately-None
//   headless (fork Form.ShowDialog WasmDialogHost branch) -> NextClick
//   resumes, wizard advances (post-m_ok tree shows one page v=True) ->
//   page4 PageLoad arms timer1 (timer1.Enabled = true) -> timer1_Tick
//   NEVER runs -> progressBarEx1 frozen at 0 -> Back stays hidden.
// Root cause: fork PInvoke.SetTimer
// (src/WfWasm.WinForms/Interop/PInvoke.Methods.cs:1316) is a `=> 1` stub:
// it reports success without arming anything, so no WM_TIMER is ever
// delivered and the timer reports Enabled=true while dead silent
// (no exception, zero pageerrors served). Fixing needs a bridge-backed
// tick source (browser setInterval -> WM_TIMER dispatch; no such bridge
// exists yet) — owned by the Host/bridge lane, NOT this file's lane.
// Handoff acceptance: this test green (tick observed headless), and/or a
// served gate showing progress ticks + Next enabled after modal OK.
// NOTE (WC1230 procstall, 2026-11-03): the SetTimer-stub theory above is
// disproven — upstream inputs/.../Timer.cs is not compiled; the fork
// overlay (src/WfWasm.WinForms/Timer.cs, Threading.Timer-based) ships
// (markers in served closure) and ticks in-process. Served silence is a
// wasm callback-delivery fault (host/runtime lane), not this file's stub.
public sealed class TimerProcessingStallTests
{
    [Fact]
    public void Fork_Timer_Enabled_Fires_Tick_Headless()
    {
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        int ticks = 0;
        timer.Tick += (_, __) => ticks++;
        timer.Enabled = true;
        Assert.True(timer.Enabled);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (ticks == 0 && sw.ElapsedMilliseconds < 2000)
            System.Threading.Thread.Sleep(50);
        Assert.True(ticks >= 1,
            $"fork Timer must tick headless (got {ticks} in 2s)");
    }

    [Fact]
    public void Fork_Timer_Container_Sited_Fires_Tick_Headless()
    {
        // The wizard demo constructs timer1 via new Timer(this.components):
        // a container site must not flip DesignMode and swallow the arm.
        using var components = new System.ComponentModel.Container();
        using var timer = new System.Windows.Forms.Timer(components) { Interval = 50 };
        int ticks = 0;
        timer.Tick += (_, __) => ticks++;
        timer.Enabled = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (ticks == 0 && sw.ElapsedMilliseconds < 2000)
            System.Threading.Thread.Sleep(50);
        Assert.True(ticks >= 1,
            $"container-sited fork Timer must tick (got {ticks} in 2s; " +
            "a silent DesignMode skip would wedge wizard processing at 0%)");
    }
}
