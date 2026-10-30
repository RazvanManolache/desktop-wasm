using System.ComponentModel;
using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery SliderPage slice: the original WPFGallery BasicInput/SliderPage and
/// the UserDashboard age slider need real RangeBase/Slider behavior (min/max/
/// value coercion, change events, pointer capture and dragging, keyboard
/// control, binding and actual painted track/thumb).
/// Pointer/keyboard dispatch runs through process-wide input statics, so this
/// joins the serialized ApplicationState collection like the other
/// interaction tests.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class SliderTests
{
    private const string SliderXaml = """
        <Window Title="SliderLab">
          <StackPanel>
            <Slider Minimum="0" Maximum="100" Value="25" Width="200"
                    TickFrequency="10" TickPlacement="Both" IsSnapToTickEnabled="True" />
            <TextBlock Text="Output:"/>
          </StackPanel>
        </Window>
        """;

    private sealed class Volume : INotifyPropertyChanged
    {
        private double _level;
        public event PropertyChangedEventHandler? PropertyChanged;
        public double Level
        {
            get => _level;
            set { if (_level == value) return; _level = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Level))); }
        }
    }

    private static (WasmInputRouter Router, WpfInputManager Manager, Slider Slider) Harness(double min = 0, double max = 100)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Width = 200, Height = 30 };
        var window = new Window { Content = slider };
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        manager.AddRoot(window);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(800, 600));
        mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        return (router, manager, slider);
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

    [Fact]
    public void Slider_xaml_loads_parses_lays_out_and_paints()
    {
        var window = (Window)XamlLoader.Load(SliderXaml).Root;
        var panel = (StackPanel)window.Content!;
        var slider = Assert.IsType<Slider>(panel.LogicalChildren[0]);
        Assert.Equal(0, slider.Minimum);
        Assert.Equal(100, slider.Maximum);
        Assert.Equal(25, slider.Value);
        Assert.Equal(10, slider.TickFrequency);
        Assert.Equal(TickPlacement.Both, slider.TickPlacement);
        Assert.True(slider.IsSnapToTickEnabled);

        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        // Track, thumb edge + face paint as filled rects; TickFrequency=10 over
        // 0..100 with Both placement paints 11 ticks above and 11 below.
        var rects = commands.OfType<DrawCommand.FillRect>().ToList();
        Assert.True(rects.Count >= 3, $"expected track+thumb rects, got {rects.Count}");
        var lines = commands.OfType<DrawCommand.DrawLine>().ToList();
        Assert.Equal(22, lines.Count);
        // Value=25 puts the thumb center a quarter along the 200px track.
        var track = rects.First(r => r.H == 4);
        var thumb = rects.Where(r => r.W == 12 && r.H == 12).OrderBy(r => r.X).Last();
        Assert.Equal(track.X + 0.25 * track.W, thumb.X + thumb.W / 2, precision: 3);
    }

    [Fact]
    public void Defaults_match_wpf_range_tick_and_direction_contract()
    {
        var slider = new Slider();
        Assert.Equal(0, slider.Minimum);
        Assert.Equal(1, slider.Maximum);
        Assert.Equal(0, slider.Value);
        Assert.Equal(0.1, slider.SmallChange);
        Assert.Equal(1, slider.LargeChange);
        Assert.Equal(Orientation.Horizontal, slider.Orientation);
        Assert.False(slider.IsDirectionReversed);
        Assert.False(slider.IsSnapToTickEnabled);
        Assert.Equal(1, slider.TickFrequency);
        // TickPlacement defaults to None (bare sliders render a clean track).
        Assert.Equal(TickPlacement.None, slider.TickPlacement);
    }

    [Fact]
    public void Value_coerces_into_range_and_limits_coerce_each_other()
    {
        var slider = new Slider();
        slider.Value = 5;
        Assert.Equal(1, slider.Value); // default range is 0..1
        slider.Value = -5;
        Assert.Equal(0, slider.Value);

        slider.Minimum = 10; // coerced down to Maximum (1), value follows
        Assert.Equal(1, slider.Minimum);
        Assert.Equal(1, slider.Value);
        slider.Maximum = -3; // coerced up to Minimum (1)
        Assert.Equal(1, slider.Maximum);
    }

    [Fact]
    public void ValueChanged_carries_coerced_old_and_new_values_once()
    {
        var slider = new Slider { Minimum = 0, Maximum = 100 };
        var seen = new List<(double Old, double New)>();
        slider.ValueChanged += (_, e) => seen.Add((e.OldValue, e.NewValue));

        slider.Value = 30;
        slider.Value = 30; // equal sets are silent
        slider.Value = 200; // coerced to 100
        Assert.Equal([(0, 30), (30, 100)], seen);
    }

    [Fact]
    public void Snap_to_tick_rounds_every_set_when_enabled()
    {
        var slider = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10, IsSnapToTickEnabled = true };
        slider.Value = 26;
        Assert.Equal(30, slider.Value);
        slider.Value = 24;
        Assert.Equal(20, slider.Value);

        slider.IsSnapToTickEnabled = false;
        slider.Value = 26;
        Assert.Equal(26, slider.Value);
    }

    [Fact]
    public void Thumb_drag_moves_value_with_grab_offset_and_releases()
    {
        var (router, _, slider) = Harness();
        slider.Value = 50;
        var r = slider.RenderRect;
        // Track spans the rect minus half a thumb on each side.
        var x0 = r.X + 6;
        var span = r.Width - 12;
        var thumbX = x0 + 0.5 * span;
        var midY = r.Y + r.Height / 2;

        Down(router, thumbX, midY);
        Move(router, thumbX + span * 0.25, midY); // +25 range units
        Assert.Equal(75, slider.Value, precision: 6);
        // Release ends the drag: further moves no longer track.
        Up(router, thumbX + span * 0.25, midY);
        Move(router, thumbX + span * 0.4, midY);
        Assert.Equal(75, slider.Value, precision: 6);
    }

    [Fact]
    public void Track_press_steps_one_large_change_toward_click()
    {
        var (router, _, slider) = Harness();
        slider.LargeChange = 10;
        slider.Value = 20;
        var r = slider.RenderRect;
        var farX = r.X + r.Width - 8; // near Maximum end
        var midY = r.Y + r.Height / 2;

        Down(router, farX, midY);
        Up(router, farX, midY);
        Assert.Equal(30, slider.Value, precision: 6);
    }

    [Fact]
    public void Keyboard_steps_home_end_and_reversal_need_focus()
    {
        var (router, manager, slider) = Harness();
        slider.SmallChange = 1;
        slider.LargeChange = 10;
        slider.Value = 50;

        router.DispatchKeyboard(new WasmKeyArgs(Handle, "ArrowRight", true, false, false, false));
        Assert.Equal(50, slider.Value); // unfocused: ignored
        Assert.True(manager.TryFocus(slider));

        router.DispatchKeyboard(new WasmKeyArgs(Handle, "ArrowRight", true, false, false, false));
        Assert.Equal(51, slider.Value);
        router.DispatchKeyboard(new WasmKeyArgs(Handle, "PageUp", true, false, false, false));
        Assert.Equal(61, slider.Value);
        router.DispatchKeyboard(new WasmKeyArgs(Handle, "Home", true, false, false, false));
        Assert.Equal(0, slider.Value);
        router.DispatchKeyboard(new WasmKeyArgs(Handle, "End", true, false, false, false));
        Assert.Equal(100, slider.Value);

        slider.IsDirectionReversed = true;
        router.DispatchKeyboard(new WasmKeyArgs(Handle, "ArrowRight", true, false, false, false));
        Assert.Equal(99, slider.Value);
    }

    [Fact]
    public void TwoWay_binding_pushes_edits_back_to_source()
    {
        var vm = new Volume { Level = 25 };
        var slider = new Slider { Minimum = 0, Maximum = 100 };
        slider.SetBinding("Value", new Binding("Level") { Source = vm, Mode = BindingMode.TwoWay });
        Assert.Equal(25, slider.Value);

        slider.Value = 60;
        Assert.Equal(60, vm.Level);
        vm.Level = 70;
        Assert.Equal(70, slider.Value);
    }

    [Fact]
    public void Focus_ring_paints_an_outline_not_a_solid_fill()
    {
        var (_, manager, slider) = Harness();
        Assert.True(manager.TryFocus(slider));
        var commands = WasmMilCore.RenderHeadless(slider, new WpfSize(800, 600));
        var ring = commands.OfType<DrawCommand.FillRect>()
            .Where(r => r.Rgba == 0x285B45FFu)
            .ToList();
        // The ring is present but only as 1px edge bars; a solid fill would
        // cover the track and thumb (caught by the browser proof).
        Assert.NotEmpty(ring);
        Assert.All(ring, bar => Assert.True(bar.W == 1 || bar.H == 1,
            $"focus bar must be 1px thin, got {bar.W}x{bar.H}"));
        Assert.Contains(commands.OfType<DrawCommand.FillRect>(),
            r => r.W == 12 && r.H == 12); // thumb still paints
    }

    [Fact]
    public void Vertical_slider_maps_bottom_to_minimum_and_top_to_maximum()
    {
        var (router, manager, slider) = Harness();
        slider.Orientation = Orientation.Vertical;
        slider.Height = 200;
        slider.Width = 30;
        Assert.True(manager.TryFocus(slider));
        slider.Value = 0;
        router.DispatchKeyboard(new WasmKeyArgs(Handle, "ArrowUp", true, false, false, false));
        Assert.Equal(slider.SmallChange, slider.Value);
    }
}
