namespace DesktopWasm.Wpf.Tests;

/// <summary>Ambient capture never leaves a test with a dispatcher owned by a
/// finished thread: <c>Dispatcher.Invoke</c> on a stale pump blocks forever
/// (Send waits for an owner that never pumps again).</summary>
public sealed class TestAmbientTests
{
    [Fact]
    public void Capture_rebinds_foreign_owned_pump_to_current_thread()
    {
        DesktopWasm.Core.WasmDispatcherPump? foreign = null;
        var thread = new Thread(() => { foreign = new DesktopWasm.Core.WasmDispatcherPump(); });
        thread.Start();
        thread.Join();
        Assert.NotNull(foreign);
        var stale = WasmDispatcher.Current;
        try
        {
            WasmDispatcher.Current = new WasmDispatcher(foreign);
            Assert.False(WasmDispatcher.Current.CheckAccess());
            TestAmbient.Capture();
            Assert.True(WasmDispatcher.Current.CheckAccess());
        }
        finally
        {
            WasmDispatcher.Current = stale;
        }
    }
}
