using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ScreenToGif ink/stylus slice: the board/editor workflows capture pen
/// input through the real routed pointer path (DrawingCanvas inking,
/// BoardRecorderViewModel DrawingAttributes/EditingMode, LightWindow
/// barrel-button resize). Enum numerics and defaults are pinned against
/// the real WPF assemblies; pointer/keyboard dispatch joins the serialized
/// ApplicationState collection like the other interaction tests.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class InkTests
{
    private static readonly WasmWindowHandle Handle = new(1);

    private static (WasmInputRouter Router, WpfInputManager Manager, Window Window, InkCanvas Canvas) Harness(
        InkCanvasEditingMode mode = InkCanvasEditingMode.Ink)
    {
        var canvas = new InkCanvas { Width = 400, Height = 300, EditingMode = mode };
        var window = new Window { Content = canvas };
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        manager.AddRoot(window);
        var mil = new WasmMilCore(new SkiaSurface(800, 600));
        mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        return (router, manager, window, canvas);
    }

    private static void Down(WasmInputRouter router, double x, double y, WasmPointerButton button = WasmPointerButton.Left) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, button, true) { Kind = WasmPointerKind.Down });

    private static void Move(WasmInputRouter router, double x, double y, WasmPointerButton button = WasmPointerButton.Left) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, button, true) { Kind = WasmPointerKind.Move });

    private static void Up(WasmInputRouter router, double x, double y, WasmPointerButton button = WasmPointerButton.Left) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, button, false) { Kind = WasmPointerKind.Up });

    [Fact]
    public void Stylus_and_mode_enum_values_match_wpf()
    {
        Assert.Equal(0, (int)StylusTip.Rectangle);
        Assert.Equal(1, (int)StylusTip.Ellipse);
        Assert.Equal(0, (int)InkCanvasEditingMode.None);
        Assert.Equal(1, (int)InkCanvasEditingMode.Ink);
        Assert.Equal(2, (int)InkCanvasEditingMode.GestureOnly);
        Assert.Equal(3, (int)InkCanvasEditingMode.InkAndGesture);
        Assert.Equal(4, (int)InkCanvasEditingMode.Select);
        Assert.Equal(5, (int)InkCanvasEditingMode.EraseByPoint);
        Assert.Equal(6, (int)InkCanvasEditingMode.EraseByStroke);
        Assert.Equal(0, (int)StylusButtonState.Up);
        Assert.Equal(1, (int)StylusButtonState.Down);
        Assert.Equal(0, (int)ApplicationGesture.AllGestures);
        Assert.Equal(61440, (int)ApplicationGesture.NoGesture);
        Assert.Equal(61680, (int)ApplicationGesture.Tap);
        Assert.Equal(61681, (int)ApplicationGesture.DoubleTap);
    }

    [Fact]
    public void Drawing_attributes_carry_verified_defaults_clone_and_change()
    {
        var attributes = new DrawingAttributes();
        Assert.Equal(Colors.Black, attributes.Color);
        Assert.Equal(2.0031496062992127, attributes.Width, precision: 12);
        Assert.Equal(2.0031496062992127, attributes.Height, precision: 12);
        Assert.Equal(StylusTip.Ellipse, attributes.StylusTip);
        Assert.False(attributes.FitToCurve);
        Assert.False(attributes.IsHighlighter);
        Assert.False(attributes.IgnorePressure);
        Assert.True(attributes.StylusTipTransform.IsIdentity);

        var changed = 0;
        attributes.AttributeChanged += (_, _) => changed++;
        attributes.Color = Colors.Red;
        attributes.Width = 5;
        attributes.StylusTip = StylusTip.Rectangle;
        attributes.FitToCurve = true;
        attributes.IsHighlighter = true;
        Assert.Equal(5, changed);

        var id = Guid.NewGuid();
        attributes.AddPropertyData(id, "tag");
        Assert.True(attributes.ContainsPropertyData(id));
        Assert.Equal("tag", attributes.GetPropertyData(id));
        Assert.Contains(id, attributes.GetPropertyDataIds());
        attributes.RemovePropertyData(id);
        Assert.False(attributes.ContainsPropertyData(id));

        var clone = attributes.Clone();
        Assert.Equal(attributes.Color, clone.Color);
        Assert.Equal(5, clone.Width);
        Assert.True(clone.IsHighlighter);
        Assert.NotSame(attributes, clone);
    }

    [Fact]
    public void Stylus_shapes_points_and_collections_match_wpf_shapes()
    {
        var rectangle = new RectangleStylusShape(10, 10);
        Assert.Equal(10, rectangle.Width);
        Assert.Equal(10, rectangle.Height);
        Assert.Equal(0, rectangle.Rotation);
        var rotated = new EllipseStylusShape(4, 5, 30);
        Assert.Equal(4, rotated.Width);
        Assert.Equal(5, rotated.Height);
        Assert.Equal(30, rotated.Rotation);
        Assert.Throws<ArgumentException>(() => new RectangleStylusShape(-1, 5));

        var points = new StylusPointCollection(new[] { new StylusPoint(0, 0), new StylusPoint(10, 5) });
        var changed = 0;
        points.Changed += (_, _) => changed++;
        points.Add(new StylusPointCollection(new[] { new StylusPoint(20, 5) }));
        Assert.Equal(3, points.Count);
        Assert.Equal(1, changed);
        Assert.Equal(new Point(20, 5), points[2].ToPoint());
        var clone = points.Clone();
        Assert.Equal(3, clone.Count);
        Assert.NotSame(points, clone);
    }

    [Fact]
    public void Stroke_bounds_geometry_clone_and_hit_match_wpf()
    {
        var stroke = new Stroke(new StylusPointCollection(new[] { new StylusPoint(0, 0), new StylusPoint(10, 5) }));
        Assert.NotNull(stroke.DrawingAttributes);
        var bounds = stroke.GetBounds();
        Assert.Equal(-1.0015748031496063, bounds.X, precision: 9);
        Assert.Equal(-1.0015748031496063, bounds.Y, precision: 9);
        Assert.Equal(12.003149606299212, bounds.Width, precision: 9);
        Assert.Equal(7.003149606299213, bounds.Height, precision: 9);

        var geometry = Assert.IsType<PathGeometry>(stroke.GetGeometry());
        Assert.Single(geometry.Figures);
        Assert.Equal(new Point(0, 0), geometry.Figures[0].StartPoint);
        Assert.True(stroke.HitTest(new Point(5, 2.5)));
        Assert.False(stroke.HitTest(new Point(5, 50)));

        var clone = stroke.Clone();
        Assert.Equal(2, clone.StylusPoints.Count);
        Assert.NotSame(stroke.DrawingAttributes, clone.DrawingAttributes);
        clone.StylusPoints.Add(new StylusPoint(99, 99));
        Assert.Equal(2, stroke.StylusPoints.Count);
    }

    [Fact]
    public void Stroke_collection_reports_real_deltas()
    {
        var strokes = new StrokeCollection();
        var deltas = new List<(int Added, int Removed)>();
        strokes.StrokesChanged += (_, e) => deltas.Add((e.Added.Count, e.Removed.Count));

        var first = new Stroke(new StylusPointCollection(new[] { new StylusPoint(0, 0) }));
        strokes.Add(first);
        Assert.Equal([(1, 0)], deltas);

        var batch = new StrokeCollection([first.Clone(), first.Clone()]);
        strokes.Add(batch);
        Assert.Equal(3, strokes.Count);
        Assert.Equal((2, 0), deltas[^1]);

        strokes.RemoveAt(0);
        Assert.Equal((0, 1), deltas[^1]);
        Assert.False(strokes.GetBounds().IsEmpty);

        var copy = strokes.Clone();
        Assert.Equal(2, copy.Count);
        copy.Clear();
        Assert.Equal(2, strokes.Count);
    }

    [Fact]
    public void Pointer_drag_collects_a_stroke_and_raises_collected()
    {
        var (router, _, _, canvas) = Harness();
        InkCanvasStrokeCollectedEventArgs? collected = null;
        canvas.StrokeCollected += (_, e) => collected = e;
        var pressed = new List<string>();
        canvas.PreviewMouseLeftButtonDown += (_, _) => pressed.Add("preview");
        canvas.MouseLeftButtonDown += (_, _) => pressed.Add("down");

        var r = canvas.RenderRect;
        Down(router, r.X + 50, r.Y + 60);
        Assert.True(canvas.IsCollecting);
        Move(router, r.X + 70, r.Y + 60);
        Move(router, r.X + 90, r.Y + 80);
        Up(router, r.X + 90, r.Y + 80);

        Assert.Equal(["preview", "down"], pressed);
        var stroke = Assert.Single(canvas.Strokes);
        Assert.Same(stroke, collected?.Stroke);
        Assert.Equal(3, stroke.StylusPoints.Count);
        Assert.False(canvas.IsCollecting);
        // The stroke stamps the live attributes: later edits do not rewrite history.
        canvas.DefaultDrawingAttributes.Color = Colors.Red;
        Assert.Equal(Colors.Black, stroke.DrawingAttributes.Color);
    }

    [Fact]
    public void Collected_strokes_paint_as_retained_lines()
    {
        var (router, _, window, canvas) = Harness();
        canvas.DefaultDrawingAttributes.Color = Colors.Red;
        canvas.DefaultDrawingAttributes.Width = 4;
        var r = canvas.RenderRect;
        Down(router, r.X + 50, r.Y + 60);
        Move(router, r.X + 90, r.Y + 80);
        Up(router, r.X + 90, r.Y + 80);

        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        var line = Assert.IsType<DrawCommand.DrawLine>(Assert.Single(commands, c => c is DrawCommand.DrawLine));
        Assert.Equal(r.X + 50, line.X1, precision: 3);
        Assert.Equal(r.Y + 60, line.Y1, precision: 3);
        Assert.Equal(r.X + 90, line.X2, precision: 3);
        Assert.Equal(r.Y + 80, line.Y2, precision: 3);
        Assert.Equal(4, line.Width, precision: 3);
        Assert.Equal(0xFF0000FFu, line.Rgba);
    }

    [Fact]
    public void Erase_by_stroke_removes_hit_strokes_through_the_pointer_path()
    {
        var (router, _, _, canvas) = Harness();
        var r = canvas.RenderRect;
        Down(router, r.X + 50, r.Y + 60);
        Move(router, r.X + 90, r.Y + 80);
        Up(router, r.X + 90, r.Y + 80);
        Assert.Single(canvas.Strokes);

        canvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
        Assert.Equal(InkCanvasEditingMode.EraseByStroke, canvas.ActiveEditingMode);
        Down(router, r.X + 70, r.Y + 70);
        Up(router, r.X + 70, r.Y + 70);
        Assert.Empty(canvas.Strokes);
    }

    [Fact]
    public void Ink_canvas_defaults_modes_gestures_selection_and_children()
    {
        var canvas = new InkCanvas();
        Assert.Equal(InkCanvasEditingMode.Ink, canvas.EditingMode);
        Assert.Equal(InkCanvasEditingMode.Ink, canvas.ActiveEditingMode);
        Assert.IsType<RectangleStylusShape>(canvas.EraserShape);
        Assert.Equal([ApplicationGesture.AllGestures], canvas.GetEnabledGestures());
        Assert.True(canvas.MoveEnabled);
        Assert.True(canvas.ResizeEnabled);
        Assert.False(canvas.UseCustomCursor);
        Assert.Empty(canvas.Strokes);

        canvas.SetEnabledGestures([ApplicationGesture.NoGesture]);
        Assert.Equal([ApplicationGesture.NoGesture], canvas.GetEnabledGestures());

        var modeChanged = 0;
        canvas.EditingModeChanged += (_, _) => modeChanged++;
        canvas.EditingMode = InkCanvasEditingMode.Select;
        Assert.Equal(1, modeChanged);

        var stroke = new Stroke(new StylusPointCollection(new[] { new StylusPoint(1, 1), new StylusPoint(9, 9) }));
        canvas.Strokes.Add(stroke);
        canvas.Select(new StrokeCollection([stroke]));
        Assert.Same(stroke, Assert.Single(canvas.GetSelectedStrokes()));
        Assert.Equal(stroke.GetBounds(), canvas.GetSelectionBounds());

        var child = new Border();
        canvas.Children.Add(child);
        Assert.Same(canvas, child.Parent);
        Assert.Contains(child, canvas.Children);
        canvas.Children.Remove(child);
        Assert.DoesNotContain(child, canvas.Children);
        Assert.Null(child.Parent);

        var attached = new Border();
        InkCanvas.SetLeft(attached, 12);
        Assert.Equal(12, InkCanvas.GetLeft(attached));
    }

    [Fact]
    public void Ink_canvas_collects_through_a_border_wrapper()
    {
        var pad = new InkCanvas { Width = 400, Height = 200 };
        var frame = new Border { Background = Brushes.White, BorderRgba = 0x808080FF, Thickness = 1 };
        frame.AddChild(pad);
        var window = new Window { Content = frame };
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        manager.AddRoot(window);
        var mil = new WasmMilCore(new SkiaSurface(800, 600));
        mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));

        var r = pad.RenderRect;
        Assert.True(r.Width > 0);
        Down(router, r.X + 40, r.Y + 40);
        Move(router, r.X + 120, r.Y + 80);
        Up(router, r.X + 120, r.Y + 80);
        var stroke = Assert.Single(pad.Strokes);
        // Pad-local points: ancestors must not be subtracted twice.
        Assert.Equal(40, stroke.StylusPoints[0].X, precision: 6);
        Assert.Equal(40, stroke.StylusPoints[0].Y, precision: 6);
        Assert.Equal(120, stroke.StylusPoints[1].X, precision: 6);
        Assert.Equal(80, stroke.StylusPoints[1].Y, precision: 6);
    }

    [Fact]
    public void Barrel_press_travels_the_stylus_button_route()
    {
        var (router, _, _, canvas) = Harness();
        var seen = new List<string>();
        canvas.PreviewStylusButtonDown += (_, e) =>
        {
            seen.Add($"preview:{e.StylusButton.StylusButtonState}");
            Assert.Same(canvas, e.Source);
        };
        canvas.StylusButtonDown += (_, e) => seen.Add($"down:{e.StylusButton.StylusButtonState}");
        canvas.StylusButtonUp += (_, e) => seen.Add($"up:{e.StylusButton.StylusButtonState}");

        var r = canvas.RenderRect;
        Down(router, r.X + 50, r.Y + 60, WasmPointerButton.Right);
        Up(router, r.X + 50, r.Y + 60, WasmPointerButton.Right);
        Assert.Equal(["preview:Down", "down:Down", "up:Up"], seen);
        // Barrel presses never start ink capture.
        Assert.Empty(canvas.Strokes);
    }

    [Fact]
    public void Handled_barrel_preview_suppresses_the_bubble()
    {
        var (router, _, _, canvas) = Harness();
        var bubbled = 0;
        canvas.StylusButtonDown += (_, _) => bubbled++;
        canvas.PreviewStylusButtonDown += (_, e) => e.Handled = true;

        var r = canvas.RenderRect;
        Down(router, r.X + 50, r.Y + 60, WasmPointerButton.Right);
        Assert.Equal(0, bubbled);
    }

    [Fact]
    public void Ink_doc_demo_wires_strokes_and_release_notes()
    {
        var window = BrowserHost.DemoCatalog.Find("ink-doc").Build();
        Assert.Equal("Ink & document lab", window.Title);
        var elements = Descendants(window).ToList();
        var pad = Assert.Single(elements.OfType<InkCanvas>());
        var viewer = Assert.Single(elements.OfType<FlowDocumentScrollViewer>());
        var readout = elements.OfType<TextBlock>().Single(t => t.Text.StartsWith("Strokes:"));
        var link = Assert.Single(elements.OfType<Hyperlink>());

        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        manager.AddRoot(window);
        var mil = new WasmMilCore(new SkiaSurface(800, 600));
        mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));

        var r = pad.RenderRect;
        Assert.True(r.Width > 0);
        router.DispatchPointer(new WasmPointerArgs(Handle, r.X + 40, r.Y + 40, WasmPointerButton.Left, true) { Kind = WasmPointerKind.Down });
        router.DispatchPointer(new WasmPointerArgs(Handle, r.X + 120, r.Y + 80, WasmPointerButton.Left, true) { Kind = WasmPointerKind.Move });
        router.DispatchPointer(new WasmPointerArgs(Handle, r.X + 120, r.Y + 80, WasmPointerButton.Left, false) { Kind = WasmPointerKind.Up });
        Assert.Single(pad.Strokes);
        Assert.Equal("Strokes: 1", readout.Text);

        var document = Assert.IsType<FlowDocument>(viewer.Document);
        Assert.Equal(2, document.Blocks.Count);
        Assert.Contains("Release notes", document.Text);

        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        var texts = commands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();
        Assert.Contains("Release notes", texts);
        Assert.Contains("Strokes: 1", texts);
        Assert.True(commands.OfType<DrawCommand.DrawLine>().Any());

        link.DoClick();
        Assert.Equal("Link: details opened", readout.Text);
    }

    private static IEnumerable<UIElement> Descendants(UIElement element)
    {
        yield return element;
        if (element is FrameworkElement framework)
            foreach (var child in framework.LogicalChildren)
                foreach (var item in Descendants(child)) yield return item;
    }

    [Fact]
    public void Ink_canvas_xaml_loads_mode_and_children()
    {
        const string xaml = """
            <Window Title="Board">
              <InkCanvas Width="400" Height="300" EditingMode="EraseByStroke" Background="White"/>
            </Window>
            """;
        var window = (Window)XamlLoader.Load(xaml).Root;
        var canvas = Assert.IsType<InkCanvas>(window.Content);
        Assert.Equal(InkCanvasEditingMode.EraseByStroke, canvas.EditingMode);
        Assert.NotNull(canvas.Background);
    }
}
