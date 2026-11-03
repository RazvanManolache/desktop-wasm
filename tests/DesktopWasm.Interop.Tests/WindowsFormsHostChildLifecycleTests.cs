extern alias Fork;
extern alias WasmDraw;

using DesktopWasm.Core;
using DesktopWasm.Graphics;
using DesktopWasm.Interop;
using DesktopWasm.Wpf;
using ForkForms = Fork::System.Windows.Forms;
using WfColor = WasmDraw::System.Drawing.Color;
using WfSize = WasmDraw::System.Drawing.Size;
using WfiHost = System.Windows.Forms.Integration.WindowsFormsHost;

namespace DesktopWasm.Interop.Tests;

/// <summary>
/// Bridge child-lifetime slice: Child replacement and detach against real
/// fork controls, slot-clamped sizing, and input-driven state change with
/// painted-output evidence. No activation/registration involvement (runtime
/// seam): the bridge is constructed directly, like production codebehind.
/// Text stays out (off-browser raster limit); pixels prove solid fills.
/// </summary>
public sealed class WindowsFormsHostChildLifecycleTests
{
    private static WfiHost HostAt(ForkForms.Control child, double x, double y, double w, double h)
    {
        var host = new WfiHost { Child = child };
        host.Measure(new WpfSize(800, 600));
        host.Arrange(new WpfRect(x, y, w, h));
        return host;
    }

    private static byte[] FirstPixel(WfiHost host)
    {
        var image = Assert.Single(host.RenderFrame().Commands.OfType<DrawCommand.DrawImage>());
        Assert.True(image.Rgba.Length >= 4);
        return image.Rgba[..4];
    }

    [Fact]
    public void Child_replacement_disposes_old_session_and_routes_to_new()
    {
        var first = new ForkForms.Panel { BackColor = WfColor.Red, Size = new WfSize(100, 40) };
        using var host = new WfiHost { Child = first };
        host.Measure(new WpfSize(800, 600));
        Assert.Equal(new Size(100, 40), host.DesiredSize);

        var second = new ForkForms.Panel { BackColor = WfColor.Blue, Size = new WfSize(60, 30) };
        host.Child = second;

        // The replaced session (and its hosted control, per the guest
        // contract) is torn down exactly once.
        Assert.True(first.IsDisposed);
        Assert.Same(second, host.Child);

        host.Measure(new WpfSize(800, 600));
        Assert.Equal(new Size(60, 30), host.DesiredSize);
        host.Arrange(new WpfRect(0, 0, 60, 30));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, FirstPixel(host));

        // Same instance re-set is a no-op: no session churn, no dispose.
        host.Child = second;
        Assert.False(second.IsDisposed);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, FirstPixel(host));
    }

    [Fact]
    public void Child_null_detach_tears_down_and_goes_silent()
    {
        var child = new ForkForms.Panel { BackColor = WfColor.Red, Size = new WfSize(100, 40) };
        using var host = new WfiHost { Child = child };
        host.Measure(new WpfSize(800, 600));
        Assert.Equal(new Size(100, 40), host.DesiredSize);

        host.Child = null;

        Assert.True(child.IsDisposed);
        host.Measure(new WpfSize(800, 600));
        Assert.Equal(new Size(0, 0), host.DesiredSize);
        // Precise errors preserved: no session exists to serve these.
        Assert.Throws<InvalidOperationException>(() => host.RenderFrame());
        Assert.Throws<InvalidOperationException>(
            () => host.DispatchPointer(10, 10, WasmPointerButton.Left, WasmPointerKind.Down));
        // Paint path stays silent (nothing to rasterize).
        Assert.Empty(WasmMilCore.RenderHeadless(host, new WpfSize(800, 600)));
    }

    [Fact]
    public void Measure_clamps_to_available_slot_and_session_follows_arrange()
    {
        using var host = new WfiHost
        {
            Child = new ForkForms.Panel { BackColor = WfColor.Red, Size = new WfSize(100, 40) }
        };
        host.Measure(new WpfSize(50, 20));
        Assert.Equal(new Size(50, 20), host.DesiredSize);

        host.Arrange(new WpfRect(5, 5, 50, 20));
        var frame = host.RenderFrame();
        Assert.Equal(50, frame.Width);
        Assert.Equal(20, frame.Height);
    }

    [Fact]
    public void Click_recolors_guest_and_pixels_follow_state()
    {
        // Input-driven state change with painted-output evidence: the click
        // routes through the real button surface, the handler mutates the
        // panel, and the next frame carries the new pixels (no text painted,
        // so the off-browser raster limit does not apply).
        var panel = new ForkForms.Panel { BackColor = WfColor.Red, Size = new WfSize(200, 100) };
        var button = new ForkForms.Button { Text = "", Location = new WasmDraw::System.Drawing.Point(10, 10), Size = new WfSize(120, 40) };
        button.Click += (_, _) => panel.BackColor = WfColor.Blue;
        panel.Controls.Add(button);

        using var host = HostAt(panel, 10, 20, 200, 100);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, FirstPixel(host));

        // Button occupies panel-local (10,10)-(130,50); host sits at (10,20).
        Assert.True(host.DispatchPointer(80, 50, WasmPointerButton.Left, WasmPointerKind.Down));
        Assert.True(host.DispatchPointer(80, 50, WasmPointerButton.Left, WasmPointerKind.Up));

        Assert.Equal(WfColor.Blue, panel.BackColor);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, FirstPixel(host));
    }
}
