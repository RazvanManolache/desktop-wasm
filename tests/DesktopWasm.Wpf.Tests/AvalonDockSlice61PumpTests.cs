using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-61 pump-serialized probes (ApplicationState collection): the
/// DispatcherOperationCallback end-to-end post (BeginInvoke with an explicit
/// arg, like converted AvalonDock's MessageWindow teardown) and the
/// IsAnimated flag (registered-but-unticked reports false, live-verified;
/// ticked reports true until removal). The WasmDispatcher rebind mirrors
/// DispatcherOperationTests: statics bind thread affinity at first touch.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class AvalonDockSlice61PumpTests : IDisposable
{
    public AvalonDockSlice61PumpTests()
    {
        WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
        WpfAnimationClock.ResetForTests();
    }

    public void Dispose() => WpfAnimationClock.ResetForTests();

    [Fact]
    public void Callback_posts_through_BeginInvoke_with_result()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var seen = new List<object?>();
        var operation = dispatcher.BeginInvoke(
            new DispatcherOperationCallback(arg => { seen.Add(arg); return arg ?? "null"; }),
            DispatcherPriority.Normal,
            "payload");
        Assert.Equal(DispatcherOperationStatus.Pending, operation.Status);
        Assert.True(operation.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(DispatcherOperationStatus.Completed, operation.Status);
        Assert.Equal(["payload"], seen);
        Assert.Equal("payload", operation.Result);
    }

    [Fact]
    public void IsAnimated_tracks_produced_values_only()
    {
        var button = new Button { Width = 100 };
        var animation = new DoubleAnimation { To = 200, Duration = new Duration(TimeSpan.FromSeconds(2)) };
        button.BeginAnimation(FrameworkElement.WidthProperty, animation);
        Assert.False(DependencyPropertyHelper.GetValueSource(button, FrameworkElement.WidthProperty).IsAnimated);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(0.5));
        var ticked = DependencyPropertyHelper.GetValueSource(button, FrameworkElement.WidthProperty);
        Assert.True(ticked.IsAnimated);
        Assert.Equal(BaseValueSource.Local, ticked.BaseValueSource);
        button.BeginAnimation(FrameworkElement.WidthProperty, null);
        Assert.False(DependencyPropertyHelper.GetValueSource(button, FrameworkElement.WidthProperty).IsAnimated);
    }
}
