using DesktopWasm.Graphics;
using Microsoft.Win32;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Media/power/basecompat slice: tile alignment, session media player, power guards, startup switches.</summary>
[ResetAmbientState]
public sealed class MediaPowerTests
{
    [Fact]
    public void Enum_values_match_wpf()
    {
        Assert.Equal(0, (int)AlignmentX.Left);
        Assert.Equal(1, (int)AlignmentX.Center);
        Assert.Equal(2, (int)AlignmentX.Right);
        Assert.Equal(0, (int)AlignmentY.Top);
        Assert.Equal(1, (int)AlignmentY.Center);
        Assert.Equal(2, (int)AlignmentY.Bottom);
        Assert.Equal(1, (int)PowerModes.Resume);
        Assert.Equal(2, (int)PowerModes.StatusChange);
        Assert.Equal(3, (int)PowerModes.Suspend);
        Assert.Equal(0, (int)BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions.Continue);
        Assert.Equal(1, (int)BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions.Throw);
        Assert.Equal(2, (int)BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions.Reset);
    }

    [Fact]
    public void TileBrush_alignment_defaults_center()
    {
        // ScreenToGif transition brushes set Center/Center explicitly; the
        // engine default matches so converts without the attributes agree.
        var brush = new ImageBrush();
        Assert.Equal(AlignmentX.Center, brush.AlignmentX);
        Assert.Equal(AlignmentY.Center, brush.AlignmentY);
    }

    private const string Ns =
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    [Fact]
    public void Xaml_parses_alignment_on_image_and_drawing_brushes()
    {
        var root = XamlLoader.Load(
            $"<Window {Ns}><Border><Border.Background>" +
            "<ImageBrush ImageSource=\"img.png\" AlignmentX=\"Right\" AlignmentY=\"Bottom\" />" +
            "</Border.Background></Border></Window>").Root;
        var border = Assert.IsType<Border>(((Window)root).Content);
        var brush = Assert.IsType<ImageBrush>(border.Background);
        Assert.Equal(AlignmentX.Right, brush.AlignmentX);
        Assert.Equal(AlignmentY.Bottom, brush.AlignmentY);

        var drawing = XamlLoader.Load(
            $"<Window {Ns}><Window.Resources><ResourceDictionary>" +
            "<DrawingBrush x:Key=\"T\" Viewport=\"0,0,10,10\" AlignmentX=\"Left\" AlignmentY=\"Top\" />" +
            "</ResourceDictionary></Window.Resources></Window>").Root;
        var stored = Assert.IsType<DrawingBrush>(((Window)drawing).Resources["T"]);
        Assert.Equal(AlignmentX.Left, stored.AlignmentX);
        Assert.Equal(AlignmentY.Top, stored.AlignmentY);
    }

    [Fact]
    public void Xaml_rejects_unknown_alignment_with_line_info()
    {
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            $"<Window {Ns}><Border><Border.Background>" +
            "<ImageBrush ImageSource=\"img.png\" AlignmentX=\"Diagonal\" />" +
            "</Border.Background></Border></Window>"));
        Assert.Contains("AlignmentX", ex.Message);
    }

    [Fact]
    public void MediaPlayer_matches_upstream_preview_initializer()
    {
        // ScreenToGif VideoSourceViewModel: new() { Volume = 0, ScrubbingEnabled = true }.
        var player = new MediaPlayer { Volume = 0, ScrubbingEnabled = true };
        Assert.Equal(0, player.Volume);
        Assert.True(player.ScrubbingEnabled);
        Assert.Null(player.Source);
        Assert.False(player.CanPause);
        Assert.False(player.NaturalDuration.HasTimeSpan);
        Assert.Equal(TimeSpan.Zero, player.Position);
        Assert.Equal(0, player.NaturalVideoWidth);
        Assert.Equal(0, player.NaturalVideoHeight);
        Assert.False(player.HasAudio);
        Assert.False(player.HasVideo);
    }

    [Fact]
    public void Open_resolves_with_MediaOpened_and_Changed()
    {
        var player = new MediaPlayer();
        var opened = 0;
        var changed = 0;
        player.MediaOpened += (_, _) => opened++;
        player.Changed += (_, _) => changed++;
        player.Open(new Uri("file:///clip.mp4"));
        Assert.Equal(1, opened);
        Assert.True(changed >= 1);
        Assert.Equal("file:///clip.mp4", player.Source!.AbsoluteUri);
        Assert.True(player.CanPause);
        Assert.Equal(TimeSpan.Zero, player.Position);
        Assert.Throws<ArgumentNullException>(() => player.Open(null));
    }

    [Fact]
    public void Play_pause_stop_close_drive_state_and_Changed()
    {
        var player = new MediaPlayer();
        var changes = 0;
        player.Changed += (_, _) => changes++;
        player.Open(new Uri("file:///clip.mp4"));
        player.Play();
        player.Position = TimeSpan.FromMilliseconds(1500);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), player.Position);
        player.Pause();
        player.Position = TimeSpan.FromMilliseconds(-5);
        Assert.Equal(TimeSpan.Zero, player.Position);
        player.Stop();
        Assert.Equal(TimeSpan.Zero, player.Position);
        player.Close();
        Assert.Null(player.Source);
        Assert.False(player.CanPause);
        Assert.True(changes >= 6);
    }

    [Fact]
    public void Play_without_source_raises_MediaFailed()
    {
        var player = new MediaPlayer();
        Exception? error = null;
        player.MediaFailed += (_, e) => error = e.ErrorException;
        player.Play();
        Assert.IsType<InvalidOperationException>(error);
        Assert.Throws<ArgumentNullException>(() => new ExceptionEventArgs(null!));
    }

    [Fact]
    public void Volume_and_balance_clamp()
    {
        var player = new MediaPlayer();
        player.Volume = 2;
        Assert.Equal(1, player.Volume);
        player.Volume = -1;
        Assert.Equal(0, player.Volume);
        player.Balance = 5;
        Assert.Equal(1, player.Balance);
        player.Balance = -5;
        Assert.Equal(-1, player.Balance);
    }

    [Fact]
    public void DrawVideo_paints_surface_only_for_open_players()
    {
        var closed = new DrawingContext();
        closed.DrawVideo(new MediaPlayer(), new Rect(0, 0, 10, 10));
        Assert.Empty(closed.Commands);

        var player = new MediaPlayer();
        player.Open(new Uri("file:///clip.mp4"));
        var open = new DrawingContext();
        open.DrawVideo(player, new Rect(2, 4, 10, 10));
        var fill = Assert.Single(open.Commands);
        Assert.IsType<DrawCommand.FillRect>(fill);

        var empty = new DrawingContext();
        empty.DrawVideo(player, new Rect(0, 0, 0, 10));
        Assert.Empty(empty.Commands);
        Assert.Throws<ArgumentNullException>(() => new DrawingContext().DrawVideo(null!, new Rect(0, 0, 1, 1)));
    }

    [Fact]
    public void PowerModeChanged_drives_suspend_guards()
    {
        PowerModes? seen = null;
        PowerModeChangedEventHandler handler = (_, e) => seen = e.Mode;
        SystemEvents.PowerModeChanged += handler;
        try
        {
            SystemEvents.RaisePowerModeChanged(PowerModes.Suspend);
            Assert.Equal(PowerModes.Suspend, seen);
            SystemEvents.RaisePowerModeChanged(PowerModes.Resume);
            Assert.Equal(PowerModes.Resume, seen);
        }
        finally
        {
            SystemEvents.PowerModeChanged -= handler;
        }
        seen = null;
        SystemEvents.RaisePowerModeChanged(PowerModes.Suspend);
        Assert.Null(seen);
    }

    [Fact]
    public void BaseCompatibilityPreferences_retains_switches()
    {
        Assert.Equal(BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions.Continue,
            BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailure);
        BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailure =
            BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions.Reset;
        Assert.Equal(BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions.Reset,
            BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailure);
        BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailure =
            BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions.Throw;
        Assert.Equal(BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions.Throw,
            BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailure);
        BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailure =
            BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions.Continue;
        Assert.False(BaseCompatibilityPreferences.ReuseDispatcherSynchronizationContextInstance);
        BaseCompatibilityPreferences.ReuseDispatcherSynchronizationContextInstance = true;
        Assert.True(BaseCompatibilityPreferences.ReuseDispatcherSynchronizationContextInstance);
        BaseCompatibilityPreferences.ReuseDispatcherSynchronizationContextInstance = false;
    }
}
