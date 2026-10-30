using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ScreenToGif primitives slice repro (fails before the slice).
/// Original call sites:
/// - ScreenToGif/Controls/CroppingAdorner.cs: nine Thumb fields with
///   DragDelta handlers (HandleBottomLeft/...) reading HorizontalChange/
///   VerticalChange, plus `new DragDeltaEventArgs(dx, dy)` keyboard nudges.
/// - ScreenToGif/Controls/ElementAdorner.cs: BuildAdornerThumb
///   (`new Thumb { Cursor, Height=10, Width=10, Style }`) plus
///   RotationThumb DragStarted/DragDelta handlers (DragStartedEventArgs).
/// - ScreenToGif/Controls/ExtendedRepeatButton.cs: `class ExtendedRepeatButton
///   : RepeatButton` with DefaultStyleKeyProperty.OverrideMetadata.
/// - ScreenToGif/Controls/{Integer,Double,Decimal,NullableInteger}UpDown.cs:
///   `Template.FindName("UpButton"/"DownButton", this) as RepeatButton`.
/// - ScreenToGif/Controls/SplitButton.cs: `Template.FindName("Popup", this)
///   as Popup` + `_mainPopup.IsOpen = false` close-on-click.
/// - ScreenToGif/Themes/Button.xaml + ComboBox.xaml + Generic.xaml:
///   `<Popup Placement="Bottom" IsOpen="{Binding ...}" AllowsTransparency
///   StaysOpen PopupAnimation HorizontalOffset>` with direct Border content;
///   `<Thumb x:Name="PART_*"` column grippers; RepeatButton Up/Down buttons.
/// Pointer dispatch runs through process-wide input statics, so this joins
/// the serialized ApplicationState collection like the other interaction
/// tests.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class PrimitivesTests
{
    private static (WasmInputRouter Router, WpfInputManager Manager) Harness(UIElement content)
    {
        var window = new Window { Content = content };
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        manager.AddRoot(window);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(800, 600));
        mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        return (router, manager);
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static void Move(WasmInputRouter router, double x, double y) =>
        // DOM-faithful drag move: the button stays held (matches ScrollViewerTests).
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true)
        { Kind = WasmPointerKind.Move });

    private static void Up(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, false));

    private static void Cancel(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, false)
        { Kind = WasmPointerKind.Cancel });

    [Fact]
    public void Drag_event_args_carry_original_ctor_shapes()
    {
        // CroppingAdorner keyboard-nudge shape: new DragDeltaEventArgs(dx, dy).
        var delta = new DragDeltaEventArgs(3, -4);
        Assert.Equal(3, delta.HorizontalChange);
        Assert.Equal(-4, delta.VerticalChange);

        var started = new DragStartedEventArgs(10, 20);
        Assert.Equal(10, started.HorizontalOffset);
        Assert.Equal(20, started.VerticalOffset);

        var completed = new DragCompletedEventArgs(3, -4, canceled: true);
        Assert.Equal(3, completed.HorizontalChange);
        Assert.Equal(-4, completed.VerticalChange);
        Assert.True(completed.Canceled);
    }

    [Fact]
    public void Drag_routed_events_match_original_identity()
    {
        Assert.Same(Thumb.DragDeltaEvent, Thumb.DragDeltaEvent);
        Assert.Equal("DragDelta", Thumb.DragDeltaEvent.Name);
        Assert.Equal(RoutingStrategy.Bubble, Thumb.DragDeltaEvent.RoutingStrategy);
        Assert.Equal(typeof(DragDeltaEventHandler), Thumb.DragDeltaEvent.HandlerType);
        Assert.Equal(typeof(Thumb), Thumb.DragDeltaEvent.OwnerType);
        Assert.Equal("DragStarted", Thumb.DragStartedEvent.Name);
        Assert.Equal(typeof(DragStartedEventHandler), Thumb.DragStartedEvent.HandlerType);
        Assert.Equal("DragCompleted", Thumb.DragCompletedEvent.Name);
        Assert.Equal(typeof(DragCompletedEventHandler), Thumb.DragCompletedEvent.HandlerType);
    }

    [Fact]
    public void Thumb_drag_reports_started_deltas_completed_through_pointer_path()
    {
        var thumb = new Thumb { Width = 20, Height = 20 };
        var panel = new StackPanel();
        panel.AddChild(thumb);
        var (router, _) = Harness(panel);

        var started = new List<DragStartedEventArgs>();
        var deltas = new List<DragDeltaEventArgs>();
        var completed = new List<DragCompletedEventArgs>();
        thumb.DragStarted += (_, e) => started.Add(e);
        thumb.DragDelta += (_, e) => deltas.Add(e);
        thumb.DragCompleted += (_, e) => completed.Add(e);

        var r = thumb.RenderRect;
        Down(router, r.X + 5, r.Y + 5);
        Assert.True(thumb.IsDragging);
        Assert.Single(started);

        Move(router, r.X + 8, r.Y + 9);
        Move(router, r.X + 11, r.Y + 7);
        Assert.Equal(2, deltas.Count);
        // Incremental deltas sum to the total pointer displacement.
        Assert.Equal(6, deltas.Sum(d => d.HorizontalChange), precision: 6);
        Assert.Equal(2, deltas.Sum(d => d.VerticalChange), precision: 6);

        Up(router, r.X + 11, r.Y + 7);
        Assert.False(thumb.IsDragging);
        var done = Assert.Single(completed);
        Assert.False(done.Canceled);
        Assert.Equal(6, done.HorizontalChange, precision: 6);
        Assert.Equal(2, done.VerticalChange, precision: 6);
    }

    [Fact]
    public void Thumb_capture_tracks_outside_and_cancel_completes_canceled()
    {
        var thumb = new Thumb { Width = 20, Height = 20 };
        var panel = new StackPanel();
        panel.AddChild(thumb);
        var (router, _) = Harness(panel);

        var deltas = 0;
        var completed = new List<DragCompletedEventArgs>();
        thumb.DragDelta += (_, _) => deltas++;
        thumb.DragCompleted += (_, e) => completed.Add(e);

        var r = thumb.RenderRect;
        Down(router, r.X + 5, r.Y + 5);
        // Capture keeps the drag alive outside the thumb rect.
        Move(router, r.X + 500, r.Y + 500);
        Assert.Equal(1, deltas);

        Cancel(router, r.X + 500, r.Y + 500);
        Assert.False(thumb.IsDragging);
        var done = Assert.Single(completed);
        Assert.True(done.Canceled);
        // Release after cancel no longer tracks.
        Up(router, r.X + 500, r.Y + 500);
        Assert.Equal(1, deltas);
    }

    [Fact]
    public void Thumb_CancelDrag_ends_press_as_canceled()
    {
        var thumb = new Thumb { Width = 20, Height = 20 };
        var panel = new StackPanel();
        panel.AddChild(thumb);
        var (router, _) = Harness(panel);

        var completed = new List<DragCompletedEventArgs>();
        thumb.DragCompleted += (_, e) => completed.Add(e);

        var r = thumb.RenderRect;
        Down(router, r.X + 5, r.Y + 5);
        Assert.True(thumb.IsDragging);

        thumb.CancelDrag();
        Assert.False(thumb.IsDragging);
        Assert.True(Assert.Single(completed).Canceled);
    }

    [Fact]
    public void RepeatButton_hold_repeats_until_release()
    {
        var button = new RepeatButton { Delay = 500, Interval = 100, Width = 120, Height = 32 };
        var panel = new StackPanel();
        panel.AddChild(button);
        var (router, _) = Harness(panel);
        var r = button.RenderRect;
        try
        {
            var clicks = 0;
            button.Click += (_, _) => clicks++;

            Down(router, r.X + r.Width / 2, r.Y + r.Height / 2);
            var start = DateTime.UtcNow;
            DispatcherTimer.TickAll(start.AddMilliseconds(400));
            Assert.Equal(0, clicks); // delay not elapsed: silent
            DispatcherTimer.TickAll(start.AddMilliseconds(600));
            Assert.Equal(1, clicks); // first fire after Delay
            DispatcherTimer.TickAll(start.AddMilliseconds(650));
            Assert.Equal(1, clicks); // interval not elapsed yet
            DispatcherTimer.TickAll(start.AddMilliseconds(750));
            Assert.Equal(2, clicks); // repeats every Interval
            DispatcherTimer.TickAll(start.AddMilliseconds(950));
            Assert.Equal(3, clicks);

            Up(router, r.X + r.Width / 2, r.Y + r.Height / 2);
            DispatcherTimer.TickAll(start.AddSeconds(5));
            Assert.Equal(3, clicks); // release stops the repeat
        }
        finally
        {
            Up(router, r.X + r.Width / 2, r.Y + r.Height / 2);
            DispatcherTimer.TickAll(DateTime.UtcNow.AddHours(1));
        }
    }

    [Fact]
    public void RepeatButton_quick_press_fires_once_on_release()
    {
        var button = new RepeatButton { Delay = 500, Interval = 100, Width = 120, Height = 32 };
        var panel = new StackPanel();
        panel.AddChild(button);
        var (router, _) = Harness(panel);
        var r = button.RenderRect;
        try
        {
            var clicks = 0;
            button.Click += (_, _) => clicks++;

            Down(router, r.X + r.Width / 2, r.Y + r.Height / 2);
            var start = DateTime.UtcNow;
            DispatcherTimer.TickAll(start.AddMilliseconds(100));
            Up(router, r.X + r.Width / 2, r.Y + r.Height / 2);
            Assert.Equal(1, clicks); // quick press+release still clicks once
            DispatcherTimer.TickAll(start.AddSeconds(5));
            Assert.Equal(1, clicks);
        }
        finally
        {
            Up(router, r.X + r.Width / 2, r.Y + r.Height / 2);
            DispatcherTimer.TickAll(DateTime.UtcNow.AddHours(1));
        }
    }

    [Fact]
    public void Popup_bottom_placement_anchors_child_below_target()
    {
        var target = new Border { Width = 100, Height = 30 };
        var body = new Border { Width = 100, Height = 50 };
        var popup = new Popup { Placement = PlacementMode.Bottom, Child = body };
        popup.PlacementTarget = target;
        var panel = new StackPanel();
        panel.AddChild(target);
        panel.AddChild(popup);
        var opened = 0;
        var closed = 0;
        popup.Opened += (_, _) => opened++;
        popup.Closed += (_, _) => closed++;

        var (_, _) = Harness(panel);
        Assert.Equal(0, opened);

        popup.IsOpen = true;
        Assert.Equal(1, opened);
        // Re-layout with the open popup, then read the arranged child rect.
        WasmMilCore.RenderHeadless(panel, new WpfSize(800, 600));
        Assert.Equal(target.RenderRect.X, body.RenderRect.X, precision: 3);
        Assert.Equal(target.RenderRect.Y + target.RenderRect.Height, body.RenderRect.Y, precision: 3);

        var commands = WasmMilCore.RenderHeadless(panel, new WpfSize(800, 600));
        Assert.Contains(commands.OfType<DrawCommand.FillRect>(), f => f.W == 100 && f.H == 50);

        popup.IsOpen = false;
        Assert.Equal(1, closed);
        var shut = WasmMilCore.RenderHeadless(panel, new WpfSize(800, 600));
        Assert.DoesNotContain(shut.OfType<DrawCommand.FillRect>(), f => f.W == 100 && f.H == 50);
    }

    [Fact]
    public void Popup_light_dismiss_closes_on_outside_press_only_when_not_stays_open()
    {
        var target = new Border { Width = 100, Height = 30 };
        var body = new Border { Width = 100, Height = 50 };
        var light = new Popup { Placement = PlacementMode.Bottom, StaysOpen = false, Child = body };
        light.PlacementTarget = target;
        var sticky = new Popup { Placement = PlacementMode.Bottom, StaysOpen = true, Child = new Border { Width = 60, Height = 20 } };
        var panel = new StackPanel();
        panel.AddChild(target);
        panel.AddChild(light);
        panel.AddChild(sticky);
        var (router, _) = Harness(panel);
        light.IsOpen = true;
        sticky.IsOpen = true;

        Down(router, 700, 500); // outside both popups
        Assert.False(light.IsOpen);
        Assert.True(sticky.IsOpen);
    }

    [Fact]
    public void Popup_focus_moves_into_child_and_back_to_target()
    {
        var target = new TextBox { Width = 100, Height = 30 };
        var body = new TextBox { Width = 100, Height = 30 };
        var popup = new Popup { Placement = PlacementMode.Bottom, Child = body };
        popup.PlacementTarget = target;
        var panel = new StackPanel();
        panel.AddChild(target);
        panel.AddChild(popup);
        var (router, manager) = Harness(panel);

        Assert.True(manager.TryFocus(target));
        popup.IsOpen = true;
        Assert.Same(body, WpfInputManager.Current?.Focused);

        popup.IsOpen = false;
        Assert.Same(target, WpfInputManager.Current?.Focused);
        _ = router;
    }

    [Fact]
    public void Primitives_lab_wires_real_input_to_readouts()
    {
        // Engine-native demo proof: the lab the browser proof tours reports
        // exact readout strings through the real pointer path.
        var first = DesktopWasm.BrowserHost.DemoCatalog.Find("primitives").Build();
        var second = DesktopWasm.BrowserHost.DemoCatalog.Find("primitives").Build();
        Assert.NotSame(first, second);
        Assert.Equal("Primitives lab", first.Title);

        var lab = DesktopWasm.BrowserHost.PrimitivesLab.Current;
        Assert.NotNull(lab);
        var (router, _) = Harness(second);

        var grip = lab.Grip.RenderRect;
        Down(router, grip.X + 5, grip.Y + 5);
        Move(router, grip.X + 15, grip.Y + 9);
        Up(router, grip.X + 15, grip.Y + 9);
        Assert.Equal("Drag: done (10, 4)", lab.DragReadout.Text);

        var toggle = lab.PopupToggle.RenderRect;
        Down(router, toggle.X + toggle.Width / 2, toggle.Y + toggle.Height / 2);
        Up(router, toggle.X + toggle.Width / 2, toggle.Y + toggle.Height / 2);
        Assert.True(lab.Details.IsOpen);
        Assert.Equal("Popup: open", lab.PopupReadout.Text);

        Down(router, 700, 550); // outside: light dismiss closes
        Assert.False(lab.Details.IsOpen);
        Assert.Equal("Popup: closed", lab.PopupReadout.Text);
    }

    [Fact]
    public void Popup_xaml_loads_placement_child_and_names()
    {
        var window = (Window)XamlLoader.Load("""
            <Window Title="PopupLab">
              <StackPanel>
                <Popup Placement="Bottom" IsOpen="True" AllowsTransparency="True"
                       StaysOpen="False" HorizontalOffset="2" VerticalOffset="3">
                  <Border Thickness="1"/>
                </Popup>
                <Thumb Width="10" Height="10"/>
                <RepeatButton Delay="500" Interval="100"/>
              </StackPanel>
            </Window>
            """).Root;

        var panel = (StackPanel)window.Content!;
        var popup = Assert.IsType<Popup>(panel.LogicalChildren[0]);
        Assert.Equal(PlacementMode.Bottom, popup.Placement);
        Assert.True(popup.IsOpen);
        Assert.True(popup.AllowsTransparency);
        Assert.False(popup.StaysOpen);
        Assert.Equal(2, popup.HorizontalOffset);
        Assert.Equal(3, popup.VerticalOffset);
        Assert.IsType<Border>(popup.Child);
        var thumb = Assert.IsType<Thumb>(panel.LogicalChildren[1]);
        Assert.Equal(10, thumb.Width);
        var repeat = Assert.IsType<RepeatButton>(panel.LogicalChildren[2]);
        Assert.Equal(500, repeat.Delay);
        Assert.Equal(100, repeat.Interval);
    }
}
