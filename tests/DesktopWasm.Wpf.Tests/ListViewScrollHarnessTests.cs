using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// WPF-HARNESS-1252-h7k2 task 1: in-process scroll harness that drives
/// ListView content rows below the fold (real router, real WasmMilCore/
/// SkiaSurface render, no serves). The served wall: below-fold rows have no
/// rects and synthetic wheel/End/PageDown do not move gallery content, so a
/// naive press at unscrolled layout coordinates can never select them
/// (<see cref="Press_below_fold_layout_coords_selects_nothing"/> pins that
/// wall). <see cref="ScrollPressRow"/> scrolls the row into view first —
/// wheel events through the router (served-faithful), API fallback only when
/// a wheel step moves nothing — then presses the fresh viewport coords.
/// </summary>
[ResetAmbientState]
public sealed class ListViewScrollHarnessTests
{
    private static readonly WasmWindowHandle Handle = new(1);

    private sealed class Harness
    {
        public WasmInputRouter Router { get; } = new();
        public WpfInputManager Manager { get; }
        public Window Root { get; } = new();
        public ScrollViewer Scroller { get; } = new() { Width = 400, Height = 300 };
        public ListView List { get; } = new() { SelectionMode = SelectionMode.Single };
        public WasmMilCore Mil { get; }
        public DesktopWasm.Graphics.SkiaSurface Surface { get; } = new(800, 600);
        public WpfSize Size { get; } = new(800, 600);

        public Harness(int rows)
        {
            Manager = new WpfInputManager(Router);
            var items = new List<string>(rows);
            for (var i = 0; i < rows; i++) items.Add($"R{i:00}");
            List.ItemsSource = items;
            Scroller.Content = List;
            Root.Content = Scroller;
            Manager.AddRoot(Root);
            Mil = new WasmMilCore(Surface);
            Render();
        }

        public void Render() => Mil.Render(Root, Size, new WpfRect(0, 0, Size.Width, Size.Height));
    }

    /// <summary>
    /// Scrolls row <paramref name="index"/> into the scroller viewport and
    /// presses it. Returns true when the press selected exactly that row.
    /// </summary>
    private static bool ScrollPressRow(Harness h, int index, int maxSteps = 40)
    {
        for (var i = 0; i < maxSteps; i++)
        {
            h.Render();
            var row = (UIElement?)h.List.ItemContainerGenerator.ContainerFromIndex(index);
            if (row is null)
            {
                h.Scroller.ScrollToVerticalOffset(h.Scroller.VerticalOffset + 100);
                continue;
            }
            var r = row.RenderRect;
            var vr = h.Scroller.RenderRect;
            var cx = r.X + r.Width / 2;
            var cy = r.Y + r.Height / 2;
            if (cx >= vr.X && cx < vr.X + vr.Width && cy >= vr.Y && cy < vr.Y + vr.Height)
            {
                h.Router.DispatchPointer(new WasmPointerArgs(Handle, cx, cy, WasmPointerButton.Left, true));
                h.Render();
                return h.List.SelectedIndex == index;
            }
            // Served-faithful first: wheel the row toward the viewport.
            var before = h.Scroller.VerticalOffset;
            var wy = Math.Clamp(cy, vr.Y + 1, vr.Y + vr.Height - 1);
            h.Router.DispatchWheel(new WasmWheelArgs(Handle, cx, wy, Math.Sign(r.Y - vr.Y) * 120));
            h.Render();
            if (h.Scroller.VerticalOffset == before)
                h.Scroller.ScrollToVerticalOffset(before + (r.Y > vr.Y ? 120 : -120));
        }
        return false;
    }

    [Fact]
    public void Press_below_fold_layout_coords_selects_nothing()
    {
        // Wall proof: the served harness has no rects for below-fold rows, so
        // a press at the row's unscrolled layout position cannot select it.
        var h = new Harness(60);
        var row = Assert.IsAssignableFrom<UIElement>(h.List.ItemContainerGenerator.ContainerFromIndex(50));
        var r = row.RenderRect;
        var vr = h.Scroller.RenderRect;
        Assert.True(r.Y >= vr.Y + vr.Height, "row 50 must start below the fold");
        h.Router.DispatchPointer(new WasmPointerArgs(Handle, r.X + r.Width / 2, r.Y + r.Height / 2, WasmPointerButton.Left, true));
        h.Render();
        Assert.NotEqual(50, h.List.SelectedIndex);
    }

    [Fact]
    public void Scroll_harness_drives_below_fold_row_to_selection()
    {
        var h = new Harness(60);
        Assert.True(h.Scroller.ScrollableHeight > 0, "fixture must actually scroll");
        Assert.True(ScrollPressRow(h, 50), "harness must select below-fold row 50");
        Assert.Equal(50, h.List.SelectedIndex);
        Assert.True(h.Scroller.VerticalOffset > 0, "harness must have scrolled");
        // Keyboard chain stays intact after a scrolled press.
        h.Router.DispatchKeyboard(new WasmKeyArgs(Handle, "ArrowDown", true, false, false, false));
        Assert.Equal(51, h.List.SelectedIndex);
    }
}
