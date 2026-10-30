using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class GpuCommandSourceTests
{
    [Fact]
    public void Baseline_requires_real_apps_switching_context_recovery_and_live_gpu_screenshots()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "DesktopWasm.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        using var baseline = System.Text.Json.JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(directory.FullName, "benchmarks/baselines/latest.json")));
        var renderer = baseline.RootElement.GetProperty("gpuSpike"); // Existing schema key; now real app evidence.
        foreach (var name in new[] { "switchPreservesState", "contextLossPreservesState", "retryPreservesState", "liveGpuScreenshot", "diagnosticsOptional", "originalBindingUpdatesBothControls" })
            Assert.True(renderer.GetProperty(name).GetBoolean(), name);
        Assert.Empty(renderer.GetProperty("errors").EnumerateArray());
        foreach (var demo in DesktopWasm.BrowserHost.DemoCatalog.All)
        {
            var app = renderer.GetProperty("applications").GetProperty(demo.Slug);
            Assert.StartsWith("WebGL", app.GetProperty("backend").GetString());
            Assert.True(app.GetProperty("inkPixels").GetInt32() > 50, demo.Slug);
        }
    }

    [Fact]
    public void Gpu_commands_are_the_live_application_tree_and_clean_frames_reuse_them()
    {
        var router = new WasmInputRouter();
        var host = new WasmWpfHost(new WasmDispatcherPump(), router);
        var panel = new StackPanel();
        var label = new TextBlock { Text = "Count: 0" };
        var button = new Button { Content = "Increment" };
        button.Command = new RelayCommand(() => label.Text = "Count: 1");
        panel.AddChild(label); panel.AddChild(button);
        host.AddWindow(new Window { Content = panel }, new WpfSize(800, 600));
        host.Tick(new FrameContext(1, 0));
        Assert.Contains(host.CurrentCommands.OfType<DrawCommand.DrawText>(), t => t.Text == "Count: 0");
        var old = host.CurrentCommands; var revision = host.GeneratedRevision;
        host.Tick(new FrameContext(2, 1)); Assert.Same(old, host.CurrentCommands); Assert.Equal(revision, host.GeneratedRevision);
        var r = button.ArrangedRect; var x = r.X + r.Width / 2; var y = r.Y + r.Height / 2;
        router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, WasmPointerButton.Left, true) { Kind = WasmPointerKind.Down });
        router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, WasmPointerButton.Left, false) { Kind = WasmPointerKind.Up });
        host.Tick(new FrameContext(3, 2));
        Assert.True(host.GeneratedRevision > revision);
        Assert.Contains(host.CurrentCommands.OfType<DrawCommand.DrawText>(), t => t.Text == "Count: 1");
        Assert.DoesNotContain(host.CurrentCommands.OfType<DrawCommand.DrawText>(), t => t.Text == "Count: 0");
    }
}
