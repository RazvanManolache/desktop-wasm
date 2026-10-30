using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// P1-4: property and tree mutations invalidate layout/rendering without user
/// input and without explicit host Invalidate() from app code. Each test
/// mutates a clean tree, then asserts invalidation state and next-frame
/// content (never an initial-frame happy path).
/// </summary>
[ResetAmbientState]
public sealed class InvalidationTests
{
    private sealed class RecordingSink : IFrameSink
    {
        public int Paints;
        public string LastJson = string.Empty;

        public Task UploadDefAsync(string layerId, byte[] rgba, int width, int height, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UploadPatchAsync(string layerId, byte[] rgba, int x, int y, int width, int height, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<string> PaintJsonAsync(string canvasId, string json, CancellationToken ct = default)
        {
            Paints++;
            LastJson = json;
            return Task.FromResult($"painted-{Paints}");
        }
    }

    private static string RenderJson(Window window, double w = 800, double h = 600)
    {
        var size = new WpfSize(w, h);
        return WasmMilCore.ExportFrameJson(WasmMilCore.RenderHeadless(window, size));
    }

    [Fact]
    public void Text_change_invalidates_measure_and_next_frame_shows_new_text()
    {
        var window = new Window();
        var label = new TextBlock { Text = "Hello" };
        window.Content = label;
        window.ClearInvalidation();
        var kinds = new List<InvalidationKind>();
        window.Invalidated += kinds.Add;
        Assert.False(window.IsDirty);

        label.Text = "World";

        Assert.True(label.IsMeasureDirty);
        Assert.True(window.IsDirty);
        Assert.Equal([InvalidationKind.Measure], kinds);

        var json = RenderJson(window);
        Assert.Contains("World", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Hello", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Width_change_invalidates_measure_and_resizes_next_frame()
    {
        var window = new Window();
        var panel = new StackPanel();
        var box = new Border
        {
            BackgroundRgba = 0x0000FFFF,
            Width = 100,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        panel.AddChild(box);
        window.Content = panel;
        window.ClearInvalidation();
        var kinds = new List<InvalidationKind>();
        window.Invalidated += kinds.Add;

        box.Width = 300; // DependencyProperty path (FrameworkPropertyMetadata.AffectsMeasure)

        Assert.True(window.IsMeasureDirty);
        Assert.Equal([InvalidationKind.Measure], kinds);

        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        var fill = Assert.Single(commands.OfType<DrawCommand.FillRect>(), r => r.Rgba == 0x0000FFFF);
        Assert.Equal(300f, fill.W);
        Assert.Equal(300, box.ArrangedRect.Width);
    }

    [Fact]
    public void UpdateLayout_flushes_pending_measure_and_arrange()
    {
        // Gallery navigation calls UIElement.UpdateLayout before navigating;
        // without it the frame throws MissingMethodException on card clicks.
        var window = new Window();
        var panel = new StackPanel();
        var box = new Border { Width = 100, Height = 40 };
        panel.AddChild(box);
        window.Content = panel;
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));
        window.ClearInvalidation();

        box.Width = 300;
        Assert.True(box.IsMeasureDirty);

        box.UpdateLayout();
        Assert.False(box.IsMeasureDirty);
        Assert.False(box.IsArrangeDirty);
        Assert.Equal(300, box.DesiredSize.Width, precision: 5);
    }

    [Fact]
    public void Background_change_is_paint_only_and_repaints_next_frame()
    {
        var window = new Window();
        var box = new Border { BackgroundRgba = 0xFF0000FF, Width = 200, Height = 40 };
        window.Content = box;
        window.ClearInvalidation();
        var kinds = new List<InvalidationKind>();
        window.Invalidated += kinds.Add;

        box.BackgroundRgba = 0x0000FFFF;

        Assert.True(box.IsRenderDirty);
        Assert.False(box.IsMeasureDirty);
        Assert.False(box.IsArrangeDirty);
        Assert.Equal([InvalidationKind.Render], kinds);

        var json = RenderJson(window);
        Assert.Contains("#0000ffff", json, StringComparison.Ordinal);
        Assert.DoesNotContain("#ff0000ff", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Attached_position_change_rearranges_child_on_next_frame()
    {
        var window = new Window();
        var canvas = new Canvas();
        var label = new TextBlock { Text = "Hi" };
        canvas.AddChild(label);
        window.Content = canvas;
        window.ClearInvalidation();
        var kinds = new List<InvalidationKind>();
        window.Invalidated += kinds.Add;

        label.SetAttached("Canvas.Left", 50.0);
        label.SetAttached("Canvas.Top", 20.0);

        Assert.True(window.IsDirty);
        Assert.Equal([InvalidationKind.Measure, InvalidationKind.Measure], kinds);

        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        var text = Assert.Single(commands.OfType<DrawCommand.DrawText>());
        Assert.Equal(50f, text.X);
        Assert.Equal(20f, text.Y);
    }

    [Fact]
    public void AddChild_propagates_to_root_and_next_frame_shows_child()
    {
        var window = new Window();
        var panel = new StackPanel();
        window.Content = panel;
        window.ClearInvalidation();
        var kinds = new List<InvalidationKind>();
        window.Invalidated += kinds.Add;

        panel.AddChild(new TextBlock { Text = "Late" });

        Assert.True(window.IsMeasureDirty);
        Assert.Equal([InvalidationKind.Measure], kinds);
        Assert.Contains("Late", RenderJson(window), StringComparison.Ordinal);
    }

    [Fact]
    public void Content_setter_propagates_and_replaces_frame_content()
    {
        var window = new Window { Content = new TextBlock { Text = "Old" } };
        Assert.Contains("Old", RenderJson(window), StringComparison.Ordinal);
        window.ClearInvalidation();
        var kinds = new List<InvalidationKind>();
        window.Invalidated += kinds.Add;

        window.Content = new TextBlock { Text = "New" };

        Assert.True(window.IsDirty);
        Assert.NotEmpty(kinds);
        var json = RenderJson(window);
        Assert.Contains("New", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Old", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Parent_links_track_add_reparent_and_remove()
    {
        var panel = new StackPanel();
        var other = new StackPanel();
        var label = new TextBlock { Text = "Hi" };

        panel.AddChild(label);
        Assert.Same(panel, label.Parent);

        other.AddChild(label); // reparent detaches from the previous parent
        Assert.Same(other, label.Parent);
        Assert.DoesNotContain(label, panel.LogicalChildren);

        Assert.True(other.RemoveChild(label));
        Assert.Null(label.Parent);

        var window = new Window { Content = panel };
        Assert.Same(window, panel.Parent);
        window.Content = other;
        Assert.Null(panel.Parent);
        Assert.Same(window, other.Parent);
    }

    [Fact]
    public void Child_cycles_are_rejected()
    {
        var panel = new StackPanel();
        Assert.Throws<InvalidOperationException>(() => panel.AddChild(panel));

        var window = new Window { Content = panel };
        Assert.Throws<InvalidOperationException>(() => panel.AddChild(window));
    }

    [Fact]
    public void Unchanged_values_do_not_invalidate()
    {
        var window = new Window();
        var label = new TextBlock { Text = "Hello" };
        var box = new Border { BackgroundRgba = 0xFF0000FF };
        label.SetAttached("Canvas.Left", 10.0);
        window.Content = new StackPanel();
        ((StackPanel)window.Content).AddChild(label);
        ((StackPanel)window.Content).AddChild(box);
        window.ClearInvalidation();
        var count = 0;
        window.Invalidated += _ => count++;

        label.Text = "Hello";
        box.BackgroundRgba = 0xFF0000FF;
        box.Width = box.Width;
        label.SetAttached("Canvas.Left", 10.0);

        Assert.False(window.IsDirty);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Host_sees_mutation_through_root_subscription_with_no_explicit_invalidate()
    {
        // Host-side wiring contract: the host subscribes once per owned window
        // (the line WasmWpfHost.AddWindow must adopt); the body below performs
        // no input and no explicit invalidation.
        var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
        var window = new Window();
        var label = new TextBlock { Text = "Hello" };
        window.Content = label;
        window.Invalidated += _ => host.Invalidate();
        host.AddWindow(window, new WpfSize(800, 600));

        var first = host.Tick(new FrameContext(1, 16.6));
        Assert.False(host.IsDirty);
        Assert.Contains("Hello", first.Json, StringComparison.Ordinal);

        label.Text = "World";

        Assert.True(host.IsDirty);
        var second = host.Tick(new FrameContext(2, 33.2));
        Assert.NotSame(first, second);
        Assert.Contains("World", second.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("Hello", second.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Published_frame_reflects_programmatic_mutations()
    {
        var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
        var window = new Window();
        var panel = new StackPanel();
        var label = new TextBlock { Text = "Hello" };
        var box = new Border { BackgroundRgba = 0xFF0000FF, Width = 200, Height = 40 };
        panel.AddChild(label);
        panel.AddChild(box);
        window.Content = panel;
        window.Invalidated += _ => host.Invalidate();
        host.AddWindow(window, new WpfSize(800, 600));
        var sink = new RecordingSink();

        await host.PublishAsync("main", sink, new FrameContext(1, 16.6));
        Assert.Equal(1, sink.Paints);
        Assert.Contains("Hello", sink.LastJson, StringComparison.Ordinal);

        label.Text = "World";
        box.BackgroundRgba = 0x0000FFFF;
        await host.PublishAsync("main", sink, new FrameContext(2, 33.2));

        Assert.Equal(2, sink.Paints);
        Assert.Contains("World", sink.LastJson, StringComparison.Ordinal);
        Assert.Contains("#0000ffff", sink.LastJson, StringComparison.Ordinal);
    }
}
