using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// P1-1/P1-5 lifecycle regressions: moves are not releases, handled previews
/// suppress bubbles, release-outside/cancel never click, keyboard activates,
/// and display coordinates map back to the logical scene (recorded probe:
/// backing 800x600 vs displayed 747x794).
/// </summary>
[ResetAmbientState]
public sealed class InputLifecycleTests
{
    private sealed class Recorder : Canvas
    {
        public readonly List<string> Seen = new();
        public bool SwallowPreviewDown;

        public override void OnRoutedEvent(WasmRoutedEvent e)
        {
            Seen.Add($"{e.Phase}:{e.Name}");
            if (SwallowPreviewDown && e.Phase == WasmRoutedPhase.Tunneling &&
                e.Name == WasmRoutedEvent.PreviewPointerDownName)
                e.Handled = true;
        }
    }

    private sealed class RecordingButton : Button
    {
        public readonly List<string> Seen = new();

        public override void OnRoutedEvent(WasmRoutedEvent e)
        {
            Seen.Add($"{e.Phase}:{e.Name}");
            base.OnRoutedEvent(e);
        }
    }

    private sealed class Harness
    {
        public WasmInputRouter Router { get; } = new();
        public WpfInputManager Manager { get; }
        public Recorder Root { get; } = new();
        public RecordingButton First { get; } = new() { Content = "First" };
        public RecordingButton Second { get; } = new() { Content = "Second" };

        public Harness()
        {
            Manager = new WpfInputManager(Router);
            Root.AddChild(First);
            Root.AddChild(Second);
            Manager.AddRoot(Root);
            Root.Measure(new WpfSize(800, 600));
            Root.Arrange(new WpfRect(0, 0, 800, 600));
            // Deterministic geometry (Canvas leaves children at DesiredSize).
            First.Arrange(new WpfRect(350, 270, 100, 60));   // centre (400, 300)
            Second.Arrange(new WpfRect(350, 340, 100, 60));  // centre (400, 370)
        }

        public void Down(double x, double y, int pointerId = 1) =>
            Router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, WasmPointerButton.Left, true)
            { Kind = WasmPointerKind.Down, PointerId = pointerId });

        public void Move(double x, double y, int pointerId = 1) =>
            Router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, WasmPointerButton.None, false)
            { Kind = WasmPointerKind.Move, PointerId = pointerId });

        public void Up(double x, double y, int pointerId = 1) =>
            Router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, WasmPointerButton.Left, false)
            { Kind = WasmPointerKind.Up, PointerId = pointerId });

        public void Cancel(double x, double y, int pointerId = 1) =>
            Router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, WasmPointerButton.Left, false)
            { Kind = WasmPointerKind.Cancel, PointerId = pointerId });

        public void KeyDown(string key, bool shift = false) =>
            Router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, true, false, shift, false));
    }

    [Fact]
    public void Move_is_not_a_release_and_clicks_only_on_release()
    {
        var h = new Harness();
        var clicks = 0;
        h.First.Click += (_, _) => clicks++;

        h.Down(400, 300);
        Assert.Equal(0, clicks); // press arms, does not click
        h.Move(500, 500);
        h.Move(10, 590);

        Assert.DoesNotContain(h.First.Seen, s => s.Contains("PointerUp", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Root.Seen, s => s.Contains("PointerUp", StringComparison.Ordinal));
        Assert.Contains("Bubbling:PointerMove", h.First.Seen);
        Assert.Equal(0, clicks);

        h.Up(400, 300); // captured target, back inside -> click
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Handled_preview_suppresses_paired_bubble()
    {
        var h = new Harness();
        h.Root.SwallowPreviewDown = true;

        h.Down(400, 300);

        Assert.Contains("Tunneling:PreviewPointerDown", h.Root.Seen);
        Assert.DoesNotContain(h.Root.Seen, s => s.StartsWith("Bubbling:", StringComparison.Ordinal));
        Assert.DoesNotContain(h.First.Seen, s => s.StartsWith("Bubbling:", StringComparison.Ordinal));
    }

    [Fact]
    public void Release_outside_cancels_click_but_reaches_button()
    {
        var h = new Harness();
        var clicks = 0;
        h.First.Click += (_, _) => clicks++;

        h.Down(400, 300);
        h.Move(10, 590);   // drag far outside the button
        h.Up(10, 590);     // capture still routes release to the button

        Assert.Contains("Bubbling:PointerUp", h.First.Seen);
        Assert.Equal(0, clicks);

        // Armed state was consumed: a later inside-release without press is inert.
        h.Up(400, 300);
        Assert.Equal(0, clicks);

        // Fresh press/release inside still clicks.
        h.Down(400, 300);
        h.Up(400, 300);
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Cancel_never_clicks()
    {
        var h = new Harness();
        var clicks = 0;
        h.First.Click += (_, _) => clicks++;

        h.Down(400, 300);
        h.Cancel(400, 300);
        Assert.Equal(0, clicks);

        h.Up(400, 300); // disarmed by the cancel
        Assert.Equal(0, clicks);
    }

    [Fact]
    public void Keyboard_activates_and_tabs_focus()
    {
        var h = new Harness();
        var first = 0; var second = 0;
        h.First.Click += (_, _) => first++;
        h.Second.Click += (_, _) => second++;

        h.Down(400, 300); // implicit focus on First
        Assert.Same(h.First, h.Manager.Focused);

        h.KeyDown("Enter");
        Assert.Equal(1, first);

        h.KeyDown(" ");
        Assert.Equal(2, first);

        h.KeyDown("Tab");
        Assert.Same(h.Second, h.Manager.Focused);

        h.KeyDown("Enter");
        Assert.Equal(1, second);

        h.KeyDown("Tab", shift: true);
        Assert.Same(h.First, h.Manager.Focused);
    }

    [Fact]
    public void Viewport_maps_recorded_probe_pixels_to_logical_scene()
    {
        // Review probe: backing 800x600, displayed bounds 747x794.
        var (scale, offX, offY) = WpfViewport.ContainFit(747, 794);
        Assert.Equal(747.0 / 800, scale, precision: 9);
        Assert.Equal(0, offX, precision: 9);
        Assert.Equal((794 - 600 * scale) / 2, offY, precision: 9);

        // Visible centre of the element maps to the logical centre.
        var (x, y) = WpfViewport.MapToLogical(747.0 / 2, 794.0 / 2, 747, 794);
        Assert.Equal(400, x, precision: 6);
        Assert.Equal(300, y, precision: 6);

        // Round-trip is exact.
        var (ox, oy) = WpfViewport.MapToClient(400, 300, 747, 794);
        Assert.Equal(747.0 / 2, ox, precision: 6);
        Assert.Equal(794.0 / 2, oy, precision: 6);
    }

    [Fact]
    public void Mapped_centre_click_hits_after_resize_naive_offsets_miss()
    {
        // Same 747x794 displayed element over the 800x600 scene: the visible
        // centre of the button must map back onto it; raw CSS offsets must not.
        var h = new Harness();
        var clicks = 0;
        h.First.Click += (_, _) => clicks++;

        var (ox, oy) = WpfViewport.MapToClient(400, 300, 747, 794);
        var (lx, ly) = WpfViewport.MapToLogical(ox, oy, 747, 794);
        h.Down(lx, ly);
        h.Up(lx, ly);
        Assert.Equal(1, clicks);

        h.Down(ox, oy); // the old bug: unmapped CSS offsets as scene coords
        h.Up(ox, oy);
        Assert.Equal(1, clicks); // no second click: (373.5,397) is not on the button
    }
}
