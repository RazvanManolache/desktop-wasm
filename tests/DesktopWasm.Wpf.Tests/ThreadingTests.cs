namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Threading + key enums (Step 37): generated Key/ModifierKeys/
/// DispatcherPriority values pinned against the Microsoft Learn tables,
/// and Dispatcher/DispatcherObject behavior over the single-threaded pump.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ThreadingTests
{
    public ThreadingTests()
    {
        // Rebind the loop pump to this thread: statics bind thread affinity
        // at first touch, and xunit does not pin test threads.
        WasmDispatcher.Current = new WasmDispatcher(new DesktopWasm.Core.WasmDispatcherPump());
    }

    [Fact]
    public void Key_values_match_the_platform_table()
    {
        Assert.Equal(0, (int)Key.None);
        Assert.Equal(3, (int)Key.Tab);
        Assert.Equal(6, (int)Key.Enter);
        Assert.Equal(6, (int)Key.Return);
        Assert.Equal(13, (int)Key.Escape);
        Assert.Equal(18, (int)Key.Space);
        Assert.Equal(44, (int)Key.A);
        Assert.Equal(69, (int)Key.Z);
        Assert.Equal(34, (int)Key.D0);
        Assert.Equal(43, (int)Key.D9);
        Assert.Equal(90, (int)Key.F1);
        Assert.Equal(113, (int)Key.F24);
        Assert.Equal(114, (int)Key.NumLock);
        Assert.Equal(116, (int)Key.LeftShift);
        Assert.Equal(172, (int)Key.DeadCharProcessed);
        Assert.Equal(Key.Enter, Key.Return);
        Assert.Equal(Key.CapsLock, Key.Capital);
        Assert.Equal(Key.KanaMode, Key.HangulMode);
        // Full table: 202 members, 173 distinct values (aliases preserved).
        Assert.Equal(202, Enum.GetValues<Key>().Length);
        Assert.Equal(173, Enum.GetValues<Key>().Distinct().Count());
    }

    [Fact]
    public void ModifierKeys_are_flags()
    {
        Assert.Equal(0, (int)ModifierKeys.None);
        Assert.Equal(1, (int)ModifierKeys.Alt);
        Assert.Equal(2, (int)ModifierKeys.Control);
        Assert.Equal(4, (int)ModifierKeys.Shift);
        Assert.Equal(8, (int)ModifierKeys.Windows);
        Assert.Equal(6, (int)(ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True((ModifierKeys.Control | ModifierKeys.Shift).HasFlag(ModifierKeys.Shift));
        Assert.True(typeof(ModifierKeys).IsDefined(typeof(FlagsAttribute), inherit: false));
    }

    [Fact]
    public void DispatcherPriority_values_match_the_platform_table()
    {
        Assert.Equal(-1, (int)DispatcherPriority.Invalid);
        Assert.Equal(0, (int)DispatcherPriority.Inactive);
        Assert.Equal(4, (int)DispatcherPriority.Background);
        Assert.Equal(5, (int)DispatcherPriority.Input);
        Assert.Equal(9, (int)DispatcherPriority.Normal);
        Assert.Equal(10, (int)DispatcherPriority.Send);
        Assert.True(DispatcherPriority.Send > DispatcherPriority.Normal);
    }

    [Fact]
    public void Dispatcher_runs_inline_and_defers_posted_work()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        Assert.Same(dispatcher, Dispatcher.CurrentDispatcher);
        Assert.Same(dispatcher, new DispatcherObject().Dispatcher);
        Assert.False(dispatcher.HasShutdownStarted);
        Assert.True(dispatcher.CheckAccess());

        var inline = 0;
        dispatcher.Invoke(() => inline = 42);
        Assert.Equal(42, inline);

        var deferred = 0;
        var pending = dispatcher.BeginInvoke((Action)(() => deferred = 7));
        Assert.Equal(DispatcherOperationStatus.Pending, pending.Status);
        Assert.Equal(0, deferred);

        var prioritized = 0;
        var background = dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)(() => prioritized = 9));
        Assert.Equal(DispatcherPriority.Background, background.Priority);
        Assert.Equal(0, prioritized);
    }

    [Fact]
    public void VerifyAccess_passes_on_the_loop_thread()
    {
        Dispatcher.CurrentDispatcher.VerifyAccess();
        new DispatcherObject().VerifyAccess();
    }
}
