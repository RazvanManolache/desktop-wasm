using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>MaterialDesign drawer paint: closed drawers must not bury the
/// drawer rows. The staged MainWindow boots with all drawers closed; the
/// drawer shadow chrome sits at zero opacity, so skipping fully-transparent
/// elements keeps the row ink on top (matches WPF: Opacity 0 paints nothing).</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class MaterialDesignDrawerPaintTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public void Dispose() => Application.Current = _previous;

    [Fact]
    public void Zero_opacity_border_paints_nothing()
    {
        // Mechanism pin: a fully-transparent element contributes no ink,
        // so faded-out chrome can never cover content beneath it. The black
        // fill is present at full opacity (control) and gone at zero.
        static IReadOnlyList<DrawCommand> Paint(double opacity)
        {
            var root = new Window();
            var grid = new Grid { Width = 400, Height = 200 };
            var backdrop = new Border
            {
                Width = 400,
                Height = 200,
                Background = new SolidColorBrush(Colors.Black),
                Opacity = opacity,
            };
            grid.AddChild(backdrop);
            root.Content = grid;
            var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
            host.AddWindow(root, new WpfSize(400, 200));
            host.Tick(new FrameContext(1, 0));
            return host.CurrentCommands;
        }
        Assert.Contains(Paint(1.0), c => c is DrawCommand.FillRect r && r.Rgba == 0x000000FF);
        Assert.DoesNotContain(Paint(0.0), c => c is DrawCommand.FillRect r && r.Rgba == 0x000000FF);
    }

    [Fact]
    public async Task Staged_md_closed_drawers_leave_row_ink_on_top()
    {
        // Workflow pin: staged boot + first frame must end with row content,
        // not with opaque fullscreen fills over it.
        var dir = StagedAppDirectory("material-design");
        var package = JsonSerializer.Deserialize<DesktopWasm.Core.ApplicationPackage>(
            File.ReadAllText(System.IO.Path.Combine(dir, "app-manifest.json")),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.NotNull(package);
        using var loader = await ApplicationPackageLoader.CreateAsync(package!, "material-design",
            file => Task.FromResult(File.ReadAllBytes(System.IO.Path.Combine(dir, file))));
        var window = loader.Boot();
        Assert.NotNull(window);
        var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
        host.AddWindow(window, new WpfSize(800, 600));
        host.Tick(new FrameContext(1, 0));
        var commands = host.CurrentCommands;
        var firstRow = -1;
        for (var i = 0; i < commands.Count; i++)
            if (commands[i] is DrawCommand.DrawText t && t.Text.Contains("Home"))
            {
                firstRow = i;
                break;
            }
        Assert.True(firstRow >= 0);
        Assert.DoesNotContain(commands.Skip(firstRow + 1),
            c => c is DrawCommand.FillRect r && r.W >= 790 && r.H >= 460);
    }

    private static string StagedAppDirectory(string slug)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "DesktopWasm.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return System.IO.Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", slug);
    }
}
