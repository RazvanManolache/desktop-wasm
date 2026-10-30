using DesktopWasm.BrowserHost;
using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

[Collection("ApplicationState")]

/// <summary>Hit-test lab wiring (DynamicResource + hit-test slice): the lab
/// the browser proof tours reports exact readout strings through the real
/// pointer path and swaps a live resource behind its frames.</summary>
[ResetAmbientState]
public sealed class HitTestLabTests
{
    private static (WasmInputRouter Router, WpfInputManager Manager) Harness(Window window)
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        manager.AddRoot(window);
        var mil = new WasmMilCore(new SkiaSurface(800, 600));
        mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        return (router, manager);
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static void Move(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true)
        { Kind = WasmPointerKind.Move });

    private static void Up(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, false));

    [Fact]
    public void Hit_test_lab_picks_selects_and_swaps_theme()
    {
        var first = DemoCatalog.Find("hittest").Build();
        var second = DemoCatalog.Find("hittest").Build();
        Assert.NotSame(first, second);
        Assert.Equal("Hit-test lab", first.Title);

        var lab = HitTestLab.Current;
        Assert.NotNull(lab);
        var (router, _) = Harness((Window)second);

        var alpha = lab.Alpha.RenderRect;
        Down(router, alpha.X + alpha.Width / 2, alpha.Y + alpha.Height / 2);
        Up(router, alpha.X + alpha.Width / 2, alpha.Y + alpha.Height / 2);
        Assert.Equal("Pick: Alpha", lab.PickReadout.Text);

        var play = lab.Play.RenderRect;
        Down(router, play.X + 40, play.Y + 40);
        Move(router, play.X + 430, play.Y + 220);
        Up(router, play.X + 430, play.Y + 220);
        Assert.Equal("Selected: Alpha+Beta", lab.SelectReadout.Text);
        Assert.NotNull(lab.Alpha.Stroke);
        Assert.Null(lab.Gamma.Stroke);

        lab.SwapTheme();
        Assert.Equal("Theme: dark", lab.ThemeReadout.Text);
        lab.SwapTheme();
        Assert.Equal("Theme: light", lab.ThemeReadout.Text);
    }

    [Fact]
    public void Theme_swap_repaints_frames_through_the_live_resource()
    {
        var window = DemoCatalog.Find("hittest").Build();
        var lab = HitTestLab.Current;
        Assert.NotNull(lab);
        Harness((Window)window);

        var before = lab.Alpha.Parent is Border frame ? frame.BorderRgba : null;
        lab.SwapTheme();
        var after = lab.Alpha.Parent is Border swapped ? swapped.BorderRgba : null;
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.NotEqual(before, after);
    }
}
