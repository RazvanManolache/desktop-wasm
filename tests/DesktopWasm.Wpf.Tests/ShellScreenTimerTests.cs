using DesktopWasm.BrowserHost;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-31 ScreenToGif shell/windowing continuation: startup placement
/// geometry over the slice-30 Screen viewport plus the slice-30 Forms.Timer
/// surface as driven by the shell lab. All placement math is Drawing-free
/// struct geometry (no GDI+ execution); the timer ticks on the dispatcher
/// queue like the capture/highlight timers converted code expects.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ShellScreenTimerTests
{
    [Fact]
    public void PlaceDialog_centers_in_the_session_working_area()
    {
        // Session viewport is pinned to 1024x768 (slice-30 Screen shim).
        var rect = WindowPlacement.PlaceDialog(480, 360, WindowStartupLocation.CenterScreen);
        Assert.Equal(new System.Drawing.Rectangle(272, 204, 480, 360), rect);
    }

    [Fact]
    public void PlaceDialog_clamps_oversized_dialogs_to_the_working_area()
    {
        var rect = WindowPlacement.PlaceDialog(1600, 1200, WindowStartupLocation.CenterScreen);
        Assert.Equal(new System.Drawing.Rectangle(0, 0, 1024, 768), rect);
    }

    [Fact]
    public void PlaceDialog_manual_keeps_the_area_origin()
    {
        var rect = WindowPlacement.PlaceDialog(200, 100, WindowStartupLocation.Manual);
        Assert.Equal(new System.Drawing.Rectangle(0, 0, 200, 100), rect);
    }

    [Fact]
    public void PlaceDialog_center_owner_uses_the_owner_rect_then_clamps_inside_the_area()
    {
        var owner = new System.Drawing.Rectangle(400, 300, 400, 300);
        var rect = WindowPlacement.PlaceDialog(200, 100, WindowStartupLocation.CenterOwner, owner);
        Assert.Equal(new System.Drawing.Rectangle(500, 400, 200, 100), rect);

        // No owner falls back to the working-area center.
        Assert.Equal(
            WindowPlacement.PlaceDialog(200, 100, WindowStartupLocation.CenterScreen),
            WindowPlacement.PlaceDialog(200, 100, WindowStartupLocation.CenterOwner));

        // An owner hanging off the edge still lands fully inside the area.
        var edge = new System.Drawing.Rectangle(900, 700, 400, 300);
        var clamped = WindowPlacement.PlaceDialog(200, 100, WindowStartupLocation.CenterOwner, edge);
        Assert.Equal(new System.Drawing.Rectangle(824, 668, 200, 100), clamped);
        Assert.True(System.Windows.Forms.Screen.PrimaryScreen.WorkingArea.Contains(clamped));
    }

    [Fact]
    public void ShellLab_reports_screen_place_and_tick_state()
    {
        var window = ShellLab.Build();
        var lab = ShellLab.Current;
        Assert.NotNull(lab);
        Assert.Same(window, lab.Window);
        try
        {
            Assert.Equal("Screen: 1024x768 work=1024x768", lab.Notes["screen"]);
            Assert.Equal("Place: -", lab.Notes["place"]);
            Assert.Equal("Ticks: 0", lab.Notes["ticks"]);

            lab.CenterButton.RaiseClick();
            Assert.Equal("Place: 272,204 480x360", lab.Notes["place"]);
            Assert.Equal("Place: 272,204 480x360", lab.PlaceReadout.Text);

            lab.ClampButton.RaiseClick();
            Assert.Equal("Place: 0,0 1024x768", lab.Notes["place"]);
        }
        finally
        {
            lab.StopBeats();
        }
    }

    [Fact]
    public void ShellLab_timer_ticks_on_the_dispatcher_and_stops_silently()
    {
        ShellLab.Build();
        var lab = ShellLab.Current;
        Assert.NotNull(lab);
        try
        {
            lab.StartTimerButton.RaiseClick();
            Assert.True(lab.BeatTimer.Enabled);
            var start = DateTime.UtcNow;
            DispatcherTimer.TickAll(start.AddMilliseconds(50));
            Assert.Equal("Ticks: 0", lab.Notes["ticks"]);
            DispatcherTimer.TickAll(start.AddMilliseconds(150));
            Assert.Equal("Ticks: 1", lab.Notes["ticks"]);
            DispatcherTimer.TickAll(start.AddMilliseconds(300));
            Assert.Equal("Ticks: 2", lab.Notes["ticks"]);

            lab.StopTimerButton.RaiseClick();
            Assert.False(lab.BeatTimer.Enabled);
            DispatcherTimer.TickAll(start.AddSeconds(5));
            Assert.Equal("Ticks: 2", lab.Notes["ticks"]);
        }
        finally
        {
            lab.StopBeats();
        }
    }
}
