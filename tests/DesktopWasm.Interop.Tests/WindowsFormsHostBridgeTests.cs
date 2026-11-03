extern alias Fork;
extern alias WasmDraw;

using DesktopWasm.Core;
using DesktopWasm.Graphics;
using DesktopWasm.Interop;
using DesktopWasm.Packaging;
using DesktopWasm.Wpf;
using ForkForms = Fork::System.Windows.Forms;
using Path = System.IO.Path;
using WfSize = WasmDraw::System.Drawing.Size;
using WfColor = WasmDraw::System.Drawing.Color;
using WfiHost = System.Windows.Forms.Integration.WindowsFormsHost;

namespace DesktopWasm.Interop.Tests;

/// <summary>
/// Real WindowsFormsHost bridge: a WPF element hosting a REAL fork
/// WinForms control (the joint interface the AvalonDock closure names as
/// missing assembly WindowsFormsIntegration). Layout, paint, input and
/// lifetime run headlessly against real controls; text rasterization
/// stays off-limits (Panel/Button/TextBox-keyboard only, no text paint).
/// </summary>
public sealed class WindowsFormsHostBridgeTests
{
    private static WfiHost HostAt(ForkForms.Control child, double x, double y)
    {
        var host = new WfiHost { Child = child };
        host.Measure(new WpfSize(800, 600));
        var size = host.DesiredSize;
        host.Arrange(new WpfRect(x, y, size.Width, size.Height));
        return host;
    }

    [Fact]
    public void Contract_matches_the_joint_interface_gap()
    {
        var type = typeof(WfiHost);
        Assert.Equal("System.Windows.Forms.Integration.WindowsFormsHost", type.FullName);
        Assert.Equal(typeof(ForkForms.Control),
            type.GetProperty(nameof(WfiHost.Child))!.PropertyType);
        Assert.True(typeof(FrameworkElement).IsAssignableFrom(type));
        Assert.True(typeof(IHostedGuest).IsAssignableFrom(type));
    }

    [Fact]
    public void Layout_measures_real_control_natural_size()
    {
        using var host = new WfiHost();
        host.Child = new ForkForms.Panel { BackColor = WfColor.Red, Size = new WfSize(100, 40) };

        host.Measure(new WpfSize(800, 600));
        Assert.Equal(new Size(100, 40), host.DesiredSize);

        host.Arrange(new WpfRect(10, 20, 100, 40));
        Assert.Equal(new WpfRect(10, 20, 100, 40), host.RenderRect);

        var frame = host.RenderFrame();
        Assert.Equal(100, frame.Width);
        Assert.Equal(40, frame.Height);
    }

    [Fact]
    public void Empty_host_is_silent()
    {
        using var host = new WfiHost();
        host.Measure(new WpfSize(800, 600));
        Assert.Equal(new Size(0, 0), host.DesiredSize);
        Assert.Empty(WasmMilCore.RenderHeadless(host, new WpfSize(800, 600)));
        Assert.Throws<InvalidOperationException>(() => host.RenderFrame());
    }

    [Fact]
    public void Headless_render_paints_real_control_pixels()
    {
        using var host = new WfiHost
        {
            Child = new ForkForms.Panel { BackColor = WfColor.Red, Size = new WfSize(40, 30) }
        };
        var commands = WasmMilCore.RenderHeadless(host, new WpfSize(40, 30));

        var image = Assert.Single(commands.OfType<DrawCommand.DrawImage>());
        Assert.Equal(40 * 30 * 4, image.Rgba.Length);
        Assert.Equal(255, image.Rgba[0]);
        Assert.Equal(0, image.Rgba[1]);
        Assert.Equal(0, image.Rgba[2]);
        Assert.Equal(255, image.Rgba[3]);
    }

    [Fact]
    public void Render_offsets_guest_pixels_to_arranged_rect()
    {
        // A preceding sibling pushes the bridge off the origin, so the
        // guest texture must ride the arranged rect (headless re-arrange
        // to (0,0) would hide a missing offset, hence the nesting).
        var panel = new StackPanel();
        panel.AddChild(new Button { Content = "Hi" });
        using var host = new WfiHost
        {
            Child = new ForkForms.Panel { BackColor = WfColor.Red, Size = new WfSize(40, 30) }
        };
        panel.AddChild(host);
        var commands = WasmMilCore.RenderHeadless(panel, new WpfSize(800, 600));

        var rect = host.RenderRect;
        Assert.Equal(30, rect.Height);
        Assert.True(rect.Width >= 40, "arrange slot must fit the 40px guest");
        Assert.True(rect.X > 0 || rect.Y > 0, "bridge must sit off-origin for the offset to mean anything");
        var frame = host.RenderFrame();
        Assert.Equal((int)rect.Width, frame.Width);
        Assert.Equal((int)rect.Height, frame.Height);
        var image = Assert.Single(commands.OfType<DrawCommand.DrawImage>());
        Assert.Equal((float)rect.X, image.X);
        Assert.Equal((float)rect.Y, image.Y);
        Assert.Contains(commands.OfType<DrawCommand.PushClip>(),
            c => c == new DrawCommand.PushClip((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height));
        Assert.NotEmpty(commands.OfType<DrawCommand.PopClip>());
    }

    [Fact]
    public void Pointer_click_reaches_real_button_through_arranged_origin()
    {
        using var host = HostAt(
            new ForkForms.Button { Text = "Go", Size = new WfSize(120, 40) }, 10, 20);
        var button = Assert.IsType<ForkForms.Button>(host.Child);
        var clicks = 0;
        button.Click += (_, _) => clicks++;

        Assert.False(host.DispatchPointer(700, 500, WasmPointerButton.Left, WasmPointerKind.Down));
        Assert.Equal(0, clicks);

        Assert.True(host.DispatchPointer(70, 40, WasmPointerButton.Left, WasmPointerKind.Down));
        Assert.True(host.DispatchPointer(70, 40, WasmPointerButton.Left, WasmPointerKind.Up));
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Keyboard_reaches_focused_control_with_real_keycode()
    {
        using var host = HostAt(new ForkForms.TextBox { Text = "hello", Size = new WfSize(200, 24) }, 0, 0);
        var box = Assert.IsType<ForkForms.TextBox>(host.Child);

        Assert.True(host.DispatchPointer(100, 12, WasmPointerButton.Left, WasmPointerKind.Down));
        Assert.True(host.DispatchPointer(100, 12, WasmPointerButton.Left, WasmPointerKind.Up));
        Assert.True(box.Focused);

        ForkForms.Keys? seen = null;
        box.KeyDown += (_, e) => seen = e.KeyCode;
        host.DispatchKey(new HostKeyEvent("B", IsDown: true));
        host.DispatchKey(new HostKeyEvent("B", IsDown: false));
        Assert.Equal(ForkForms.Keys.B, seen);
    }

    [Fact]
    public void Dpi_2x_renders_device_pixels()
    {
        using var host = new WfiHost
        {
            Child = new ForkForms.Panel { BackColor = WfColor.Red, Size = new WfSize(40, 30) },
            Dpi = InteropDpi.ForScale(2),
        };
        host.Measure(new WpfSize(800, 600));
        Assert.Equal(new Size(40, 30), host.DesiredSize);
        host.Arrange(new WpfRect(0, 0, 40, 30));

        var frame = host.RenderFrame();
        Assert.Equal(80, frame.Width);
        Assert.Equal(60, frame.Height);
        Assert.Equal(2, frame.Dpi.ScaleX);
        var image = Assert.Single(frame.Commands.OfType<DrawCommand.DrawImage>());
        Assert.Equal(255, image.Rgba[0]);
    }

    [Fact]
    public void Dispose_tears_down_session_once()
    {
        var host = new WfiHost
        {
            Child = new ForkForms.Panel { Size = new WfSize(40, 30) }
        };
        Assert.False(host.IsDisposed);
        host.Dispose();
        Assert.True(host.IsDisposed);
        host.Dispose();

        Assert.Throws<ObjectDisposedException>(() => host.DispatchPointer(1, 1, WasmPointerButton.Left, WasmPointerKind.Down));
        Assert.Throws<ObjectDisposedException>(() => host.DispatchKey(new HostKeyEvent("A", true)));
        Assert.Throws<ObjectDisposedException>(() => host.RenderFrame());
        Assert.Throws<ObjectDisposedException>(() => host.Child = new ForkForms.Panel());
    }

    [Fact]
    public void Staged_TestApp_closure_names_only_the_bridge_gap()
    {
        var staged = StagedDirectory();
        var closure = JointClosure.Build(
            Path.Combine(staged, "TestApp.exe"), [staged], NetFrameworkProvided());

        var entry = closure.Nodes.FirstOrDefault(n => n.Name == "TestApp");
        Assert.True(entry is not null, "Closure has no TestApp node.");
        Assert.Equal(FrameworkCapabilities.Mixed, entry.Capabilities);

        // The bridge changes no routing and introduces no splits: the only
        // remaining gaps name the runtime-provided joint interface the
        // bridge implements (packaging registers the assembly name; this
        // lane owns the contract, not the closure map).
        Assert.DoesNotContain(closure.Gaps,
            g => g.Kind is JointGap.VersionConflict or JointGap.HashMismatch);
        Assert.All(closure.Gaps, g =>
            Assert.Contains("WindowsFormsIntegration", g.Detail, StringComparison.Ordinal));
    }

    private static string StagedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DesktopWasm.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repository root (DesktopWasm.slnx) not found above the test binaries.");
        var staged = Path.Combine(dir.FullName, "inputs", "mixed", "avalondock");
        Assert.True(Directory.Exists(staged),
            $"Staged AvalonDock closure absent at '{staged}'.");
        return staged;
    }

    private static IReadOnlySet<string> NetFrameworkProvided()
    {
        var provided = new HashSet<string>(
            JointClosure.ProvidedAssemblies(Path.GetDirectoryName(typeof(object).Assembly.Location)!),
            StringComparer.OrdinalIgnoreCase);
        provided.UnionWith([
            "mscorlib", "netstandard",
            "System", "System.Core", "System.Xml", "System.Xml.Linq",
            "System.Numerics", "System.ValueTuple",
        ]);
        return provided;
    }
}
