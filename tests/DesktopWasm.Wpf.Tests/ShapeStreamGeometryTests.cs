using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ScreenToGif geometry/drawing prerequisite slice, pinned by the original
/// ScreenToGif fixture (ScreenToGif/Controls/Shapes/Arrow.cs: Shape-derived
/// control whose DefiningGeometry builds a StreamGeometry via Open/
/// BeginFigure/LineTo and registers FrameworkPropertyMetadataOptions
/// AffectsRender|AffectsMeasure dependency properties) and by the live
/// screen-to-gif manifest (237 gaps: Shape/Line/Polyline, StreamGeometry(+Context),
/// FrameworkPropertyMetadataOptions, PenLineCap/Join, DashStyle,
/// ColorInterpolationMode, DoubleCollection, DrawingImage, Poly*Segments,
/// Coerce/Validate callbacks, DependencyPropertyChangedEventHandler, plus the
/// PathFigureCollection/PathSegmentCollection/GradientStopCollection nested
/// Enumerators KGySoft.Drawing.Wpf iterates).
/// </summary>
[ResetAmbientState]
public sealed class ShapeStreamGeometryTests
{
    /// <summary>Original Arrow registration/override shape (mirrors Arrow.cs).</summary>
    private sealed class ArrowLike : Shape
    {
        public static readonly DependencyProperty X1Property = DependencyProperty.Register(
            "X1", typeof(double), typeof(ArrowLike),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double X1
        {
            get => (double)GetValue(X1Property)!;
            set => SetValue(X1Property, value);
        }

        protected override Geometry DefiningGeometry
        {
            get
            {
                var geometry = new StreamGeometry();
                using (var sgc = geometry.Open())
                {
                    sgc.BeginFigure(new Point(68.98, 40), true, true);
                    sgc.LineTo(new Point(0, 40), true, true);
                    sgc.LineTo(new Point(0, 65), true, true);
                    sgc.LineTo(new Point(100, 50), true, true);
                    sgc.LineTo(new Point(0, 65), true, true);
                }
                geometry.Freeze();
                return geometry;
            }
        }
    }

    [Fact]
    public void Shape_exact_defaults_match_wpf()
    {
        var line = new Line();
        Assert.Null(line.Fill);
        Assert.Null(line.Stroke);
        Assert.Equal(1.0, line.StrokeThickness);
        Assert.Equal(Stretch.None, line.Stretch);
        Assert.Equal(PenLineCap.Flat, line.StrokeStartLineCap);
        Assert.Equal(PenLineCap.Flat, line.StrokeEndLineCap);
        Assert.Equal(PenLineCap.Flat, line.StrokeDashCap);
        Assert.Equal(PenLineJoin.Miter, line.StrokeLineJoin);
        Assert.Equal(10.0, line.StrokeMiterLimit);
        Assert.Null(line.StrokeDashArray);
        Assert.Equal(0.0, line.StrokeDashOffset);
        Assert.IsAssignableFrom<Shape>(line);
        Assert.IsAssignableFrom<FrameworkElement>(line);
    }

    [Fact]
    public void Enum_numeric_parity_matches_wpf()
    {
        Assert.Equal(0, (int)PenLineCap.Flat);
        Assert.Equal(1, (int)PenLineCap.Square);
        Assert.Equal(2, (int)PenLineCap.Round);
        Assert.Equal(3, (int)PenLineCap.Triangle);
        Assert.Equal(0, (int)PenLineJoin.Miter);
        Assert.Equal(1, (int)PenLineJoin.Bevel);
        Assert.Equal(2, (int)PenLineJoin.Round);
        Assert.Equal(0, (int)ColorInterpolationMode.SRgbLinearInterpolation);
        Assert.Equal(1, (int)ColorInterpolationMode.ScRgbLinearInterpolation);
        Assert.Equal(17, (int)(FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));
        Assert.Equal(0, (int)FrameworkPropertyMetadataOptions.None);
        Assert.Equal(2048, (int)FrameworkPropertyMetadataOptions.SubPropertiesDoNotAffectRender);
        Assert.Empty(DashStyles.Solid.Dashes);
        Assert.True(DashStyles.Dash.Dashes.SequenceEqual(new[] { 2.0, 2.0 }));
        Assert.True(DashStyles.Dot.Dashes.SequenceEqual(new[] { 0.0, 1.0 }));
        Assert.True(DashStyles.DashDot.Dashes.SequenceEqual(new[] { 2.0, 2.0, 0.0, 2.0 }));
        Assert.True(DashStyles.DashDotDot.Dashes.SequenceEqual(new[] { 2.0, 2.0, 0.0, 2.0, 0.0, 2.0 }));
    }

    [Fact]
    public void Arrow_pattern_registers_flags_and_builds_frozen_stream_geometry()
    {
        var arrow = new ArrowLike { Width = 100, Height = 100, Fill = Brushes.Red, Stroke = Brushes.Black, StrokeThickness = 2 };
        Assert.Equal(0.0, arrow.X1);
        arrow.X1 = 12.5;
        Assert.Equal(12.5, arrow.X1);

        var metadata = Assert.IsType<FrameworkPropertyMetadata>(ArrowLike.X1Property.Metadata);
        Assert.True(metadata.AffectsRender);
        Assert.True(metadata.AffectsMeasure);
        Assert.False(metadata.AffectsArrange);

        var window = new Window { Content = arrow };
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        var draw = Assert.IsType<DrawCommand.DrawGeometry>(Assert.Single(commands, c => c is DrawCommand.DrawGeometry));
        Assert.StartsWith("M 68.98 40", draw.PathData, StringComparison.Ordinal);
        Assert.Contains("L 100 50", draw.PathData, StringComparison.Ordinal);
        Assert.Equal("evenodd", draw.FillRule);
        Assert.NotEqual(0u, draw.FillRgba);
    }

    [Fact]
    public void StreamGeometry_context_covers_full_surface()
    {
        var geometry = new StreamGeometry();
        Assert.Equal(FillRule.EvenOdd, geometry.FillRule);
        using (var sgc = geometry.Open())
        {
            sgc.BeginFigure(new Point(0, 0), true, false);
            sgc.BezierTo(new Point(10, 0), new Point(10, 10), new Point(0, 10), true, false);
            sgc.QuadraticBezierTo(new Point(5, 5), new Point(0, 0), true, false);
            sgc.ArcTo(new Point(20, 20), new Size(10, 10), 0, false, SweepDirection.Clockwise, true, false);
            sgc.PolyLineTo(new List<Point> { new(30, 30), new(40, 40) }, true, false);
            sgc.PolyBezierTo(new List<Point> { new(1, 1), new(2, 2), new(3, 3) }, true, false);
            sgc.PolyQuadraticBezierTo(new List<Point> { new(4, 4), new(5, 5) }, true, false);
            sgc.SetClosedState(true);
            sgc.Close();
        }
        var data = geometry.ToPathData();
        Assert.Contains("C 10 0 10 10 0 10", data, StringComparison.Ordinal);
        Assert.Contains("Q 5 5 0 0", data, StringComparison.Ordinal);
        Assert.Contains("A 10 10 0 0 1 20 20", data, StringComparison.Ordinal);
        Assert.Contains("L 30 30 L 40 40", data, StringComparison.Ordinal);
        Assert.EndsWith("Z ", data, StringComparison.Ordinal);
        Assert.False(geometry.GetBounds().IsEmpty);

        geometry.Freeze();
        Assert.True(geometry.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => geometry.Open());
    }

    [Fact]
    public void StreamGeometry_context_rejects_segments_without_figure()
    {
        var geometry = new StreamGeometry();
        using var sgc = geometry.Open();
        Assert.Throws<InvalidOperationException>(() => sgc.LineTo(new Point(1, 1), true, false));
        Assert.Throws<InvalidOperationException>(() => sgc.Close());
        sgc.Dispose();
        Assert.Throws<ObjectDisposedException>(() => sgc.BeginFigure(new Point(0, 0), true, true));
    }

    [Fact]
    public void Poly_segments_emit_svg_and_bounds()
    {
        var figure = new PathFigure { StartPoint = new Point(0, 0) };
        var polyline = new PolyLineSegment();
        polyline.Points.Add(new Point(10, 0));
        polyline.Points.Add(new Point(10, 10));
        var polybezier = new PolyBezierSegment();
        polybezier.Points.Add(new Point(1, 1));
        polybezier.Points.Add(new Point(2, 2));
        polybezier.Points.Add(new Point(3, 3));
        var polyquad = new PolyQuadraticBezierSegment();
        polyquad.Points.Add(new Point(4, 4));
        polyquad.Points.Add(new Point(5, 5));
        figure.Segments.Add(polyline);
        figure.Segments.Add(polybezier);
        figure.Segments.Add(polyquad);
        var geometry = new PathGeometry { Figures = { figure } };
        var data = geometry.ToPathData();
        Assert.Contains("L 10 0 L 10 10", data, StringComparison.Ordinal);
        Assert.Contains("C 1 1 2 2 3 3", data, StringComparison.Ordinal);
        Assert.Contains("Q 4 4 5 5", data, StringComparison.Ordinal);
        var bounds = geometry.GetBounds();
        Assert.Equal(10, bounds.Width);
        Assert.Equal(10, bounds.Height);
    }

    [Fact]
    public void Line_polyline_measure_and_paint()
    {
        var line = new Line { X1 = 10, Y1 = 20, X2 = 110, Y2 = 20, Stroke = Brushes.Red, StrokeThickness = 2 };
        var polyline = new Polyline { Stroke = Brushes.Blue, StrokeThickness = 3 };
        polyline.Points.Add(new Point(0, 0));
        polyline.Points.Add(new Point(50, 50));
        polyline.Points.Add(new Point(100, 0));
        var canvas = new Canvas();
        canvas.AddChild(line);
        canvas.AddChild(polyline);
        var window = new Window { Content = canvas };
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        var draws = commands.OfType<DrawCommand.DrawGeometry>().ToList();
        Assert.Equal(2, draws.Count);
        Assert.Contains("M 10 20 L 110 20", draws[0].PathData, StringComparison.Ordinal);
        Assert.Contains("M 0 0 L 50 50 L 100 0", draws[1].PathData, StringComparison.Ordinal);

        var empty = new Polyline { Stroke = Brushes.Blue };
        var emptyWindow = new Window { Content = empty };
        Assert.Empty(WasmMilCore.RenderHeadless(emptyWindow, new WpfSize(800, 600)).OfType<DrawCommand.DrawGeometry>());
    }

    [Fact]
    public void Line_polyline_xaml_end_to_end()
    {
        const string xaml = """
            <Window Title="Shapes">
              <Canvas>
                <Line X1="10" Y1="20" X2="110" Y2="20" Stroke="Red" StrokeThickness="2" StrokeStartLineCap="Round" StrokeLineJoin="Bevel"/>
                <Polyline Points="0,0 50,50 100,0" Stroke="Blue" StrokeThickness="3" FillRule="Nonzero"/>
              </Canvas>
            </Window>
            """;
        var window = (Window)XamlLoader.Load(xaml).Root;
        var canvas = Assert.IsType<Canvas>(window.Content);
        var line = Assert.IsType<Line>(canvas.LogicalChildren[0]);
        var polyline = Assert.IsType<Polyline>(canvas.LogicalChildren[1]);
        Assert.Equal(10, line.X1);
        Assert.Equal(20, line.Y1);
        Assert.Equal(110, line.X2);
        Assert.Equal(2, line.StrokeThickness);
        Assert.Equal(PenLineCap.Round, line.StrokeStartLineCap);
        Assert.Equal(PenLineJoin.Bevel, line.StrokeLineJoin);
        Assert.Equal(3, polyline.Points.Count);
        Assert.Equal(FillRule.Nonzero, polyline.FillRule);

        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        Assert.Equal(2, commands.OfType<DrawCommand.DrawGeometry>().Count());
    }

    [Fact]
    public void Shape_xaml_rejects_bad_cap_and_points()
    {
        var lineXaml = """<Window><Line X1="0" Y1="0" X2="1" Y2="1" StrokeStartLineCap="Oval"/></Window>""";
        var lineError = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(lineXaml));
        Assert.Contains("PenLineCap", lineError.Message, StringComparison.Ordinal);
        var pointsXaml = """<Window><Polyline Points="0,0 5"/></Window>""";
        var pointsError = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(pointsXaml));
        Assert.Contains("point list", pointsError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Metadata_flags_coerce_and_validate_run_on_set()
    {
        var owner = new CoerceValidateOwner();
        Assert.Equal(5.0, owner.GetValue(CoerceValidateOwner.LevelProperty));
        owner.SetValue(CoerceValidateOwner.LevelProperty, 50.0);
        Assert.Equal(10.0, owner.GetValue(CoerceValidateOwner.LevelProperty));
        Assert.Throws<ArgumentException>(() => owner.SetValue(CoerceValidateOwner.LevelProperty, -1.0));
    }

    private sealed class CoerceValidateOwner : DependencyObject
    {
        public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
            "Level", typeof(double), typeof(CoerceValidateOwner),
            new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsRender, null, Coerce),
            Validate);

        private static object Coerce(DependencyObject d, object baseValue) =>
            Math.Min(10.0, (double)baseValue!);

        private static bool Validate(object value) => (double)value! >= 0;
    }

    [Fact]
    public void Nested_enumerators_match_list_semantics()
    {
        var figures = new PathFigureCollection { new() { StartPoint = new Point(1, 1) }, new() { StartPoint = new Point(2, 2) } };
        var seen = new List<Point>();
        foreach (var figure in figures) seen.Add(figure.StartPoint);
        Assert.True(seen.SequenceEqual(new[] { new Point(1, 1), new Point(2, 2) }));
        var enumerator = figures.GetEnumerator();
        Assert.Throws<InvalidOperationException>(() => enumerator.Current);
        Assert.True(enumerator.MoveNext());
        Assert.Equal(new Point(1, 1), enumerator.Current.StartPoint);
        enumerator.Reset();
        Assert.True(enumerator.MoveNext());
        Assert.Equal(new Point(1, 1), enumerator.Current.StartPoint);
        enumerator.Dispose();

        var segments = new PathSegmentCollection { new LineSegment { Point = new Point(3, 3) } };
        Assert.Equal(new Point(3, 3), Assert.Single(segments).GetEnd(new Point(0, 0)));

        var stops = new GradientStopCollection { new() };
        Assert.Single(stops);
        foreach (var stop in stops) Assert.NotNull(stop);

        var doubles = DoubleCollection.Parse("1,2 3");
        Assert.True(doubles.SequenceEqual(new[] { 1.0, 2.0, 3.0 }));
        Assert.Throws<FormatException>(() => DoubleCollection.Parse("1,x"));
        var points = PointCollection.Parse("0,0 50,50 100,0");
        Assert.Equal(3, points.Count);
        Assert.Equal(new Point(100, 0), points[2]);
    }

    [Fact]
    public void DrawingImage_retains_drawing_and_reports_bounds()
    {
        var image = new DrawingImage();
        Assert.Null(image.Drawing);
        Assert.Equal(0, image.Width);
        Assert.Equal(0, image.Height);
        var drawing = new GeometryDrawing
        {
            Brush = Brushes.Red,
            Geometry = new RectangleGeometry { Rect = new Rect(0, 0, 40, 30) },
        };
        image.Drawing = drawing;
        Assert.Same(drawing, image.Drawing);
        Assert.Equal(40, image.Width);
        Assert.Equal(30, image.Height);
    }

    [Fact]
    public void Arrow_pixels_show_fill_on_both_renderers()
    {
        var triangle = new StreamGeometry();
        using (var sgc = triangle.Open())
        {
            sgc.BeginFigure(new Point(10, 10), true, true);
            sgc.LineTo(new Point(90, 10), true, false);
            sgc.LineTo(new Point(50, 90), true, false);
            sgc.Close();
        }
        triangle.Freeze();
        var path = new Path { Data = triangle, Fill = Brushes.Red };
        var window = new Window { Content = path };
        using var surface = new SkiaSurface(800, 600);
        var mil = new WasmMilCore(surface);
        var commands = mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        mil.CommitChannel(commands);
        var draw = Assert.IsType<DrawCommand.DrawGeometry>(Assert.Single(commands, c => c is DrawCommand.DrawGeometry));
        Assert.Contains("L 50 90", draw.PathData, StringComparison.Ordinal);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        var inside = bitmap.GetPixel(50, 50);
        Assert.True(inside.Red > 200 && inside.Green < 80 && inside.Blue < 80, $"triangle interior {inside} is not the red fill");
        var outside = bitmap.GetPixel(750, 550);
        Assert.True(outside.Red > 200 && outside.Green > 200 && outside.Blue > 200, $"background {outside} is not white");
    }
}
