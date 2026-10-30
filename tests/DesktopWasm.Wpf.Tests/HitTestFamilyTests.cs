namespace DesktopWasm.Wpf.Tests;

[Collection("ApplicationState")]

/// <summary>Hit-test API family over the real retained tree (ScreenToGif
/// DrawingCanvas rubber-band selection, Recorder/NewRecorder point picks,
/// and the CroppingAdorner/ElementAdorner/ResizingAdorner VisualCollection
/// backing with VisualChildrenCount/GetVisualChild overrides).</summary>
[ResetAmbientState]
public sealed class HitTestFamilyTests
{
    private static Canvas ArrangeTree(out Border back, out Border front)
    {
        var canvas = new Canvas { Width = 200, Height = 200 };
        back = new Border { Width = 100, Height = 100 };
        front = new Border { Width = 100, Height = 100 };
        canvas.AddChild(back);
        canvas.AddChild(front);
        Canvas.SetLeft(front, 50);
        Canvas.SetTop(front, 50);
        canvas.Measure(new WpfSize(200, 200));
        canvas.Arrange(new WpfRect(0, 0, 200, 200));
        return canvas;
    }

    [Fact]
    public void Enum_values_match_wpf()
    {
        Assert.Equal(0, (int)ToleranceType.Absolute);
        Assert.Equal(1, (int)ToleranceType.Relative);
        Assert.Equal(0, (int)HitTestResultBehavior.Stop);
        Assert.Equal(1, (int)HitTestResultBehavior.Continue);
        Assert.Equal(0, (int)HitTestFilterBehavior.ContinueSkipSelfAndChildren);
        Assert.Equal(2, (int)HitTestFilterBehavior.ContinueSkipChildren);
        Assert.Equal(4, (int)HitTestFilterBehavior.ContinueSkipSelf);
        Assert.Equal(6, (int)HitTestFilterBehavior.Continue);
        Assert.Equal(8, (int)HitTestFilterBehavior.Stop);
        Assert.Equal(0, (int)IntersectionDetail.NotCalculated);
        Assert.Equal(1, (int)IntersectionDetail.Empty);
        Assert.Equal(2, (int)IntersectionDetail.FullyInside);
        Assert.Equal(3, (int)IntersectionDetail.FullyContains);
        Assert.Equal(4, (int)IntersectionDetail.Intersects);
    }

    [Fact]
    public void Point_hit_returns_topmost_visual()
    {
        var canvas = ArrangeTree(out var back, out var front);

        var overlap = VisualTreeHelper.HitTest(canvas, new Point(75, 75));
        Assert.NotNull(overlap);
        Assert.Same(front, overlap.VisualHit);

        var backOnly = VisualTreeHelper.HitTest(canvas, new Point(10, 10));
        Assert.NotNull(backOnly);
        Assert.Same(back, backOnly.VisualHit);

        // Inside the canvas but on no child: the canvas itself is the hit.
        var canvasHit = VisualTreeHelper.HitTest(canvas, new Point(180, 180));
        Assert.NotNull(canvasHit);
        Assert.Same(canvas, canvasHit.VisualHit);

        Assert.Null(VisualTreeHelper.HitTest(canvas, new Point(250, 250)));
    }

    [Fact]
    public void Point_hit_skips_hidden_and_untestable_subtrees()
    {
        var canvas = ArrangeTree(out var back, out var front);
        front.Visibility = Visibility.Hidden;

        var hit = VisualTreeHelper.HitTest(canvas, new Point(75, 75));
        Assert.NotNull(hit);
        Assert.Same(back, hit.VisualHit);

        front.Visibility = Visibility.Visible;
        front.IsHitTestVisible = false;
        hit = VisualTreeHelper.HitTest(canvas, new Point(75, 75));
        Assert.NotNull(hit);
        Assert.Same(back, hit.VisualHit);
    }

    [Fact]
    public void Point_is_interpreted_in_reference_coordinates()
    {
        var canvas = ArrangeTree(out var back, out _);
        canvas.Measure(new WpfSize(200, 200));
        canvas.Arrange(new WpfRect(10, 20, 200, 200));

        // Back now sits at (10, 20)..(110, 120) absolute; the local point
        // (5, 5) lands on it through the reference origin.
        var hit = VisualTreeHelper.HitTest(canvas, new Point(5, 5));
        Assert.NotNull(hit);
        Assert.Same(back, hit.VisualHit);
        Assert.Equal(new Point(5, 5), ((PointHitTestResult)hit).PointHit);
    }

    [Fact]
    public void Point_hit_result_carries_the_point()
    {
        var canvas = ArrangeTree(out _, out _);
        var hit = VisualTreeHelper.HitTest(canvas, new Point(75, 75));
        var pointHit = Assert.IsType<PointHitTestResult>(hit);
        Assert.Equal(new Point(75, 75), pointHit.PointHit);
    }

    [Fact]
    public void Geometry_hit_collects_shapes_like_drawing_canvas()
    {
        // Mirrors DrawingCanvas.GetSelectedShapes: rubber-band geometry over
        // the element, result callback gathering hits, Continue to keep going.
        var canvas = ArrangeTree(out var back, out var front);
        var hits = new List<Visual>();
        VisualTreeHelper.HitTest(canvas, null,
            result =>
            {
                hits.Add((Visual)result.VisualHit);
                return HitTestResultBehavior.Continue;
            },
            new GeometryHitTestParameters(new RectangleGeometry(new Rect(40, 40, 120, 120))));

        // Topmost first: front beats back, and the canvas frame follows.
        Assert.Equal(front, hits[0]);
        Assert.Contains(back, hits);
    }

    [Fact]
    public void Geometry_hit_reports_intersection_detail()
    {
        var canvas = ArrangeTree(out var back, out _);
        IntersectionDetail? seen = null;
        VisualTreeHelper.HitTest(canvas, null,
            result =>
            {
                if (ReferenceEquals(result.VisualHit, back))
                    seen = ((GeometryHitTestResult)result).IntersectionDetail;
                return HitTestResultBehavior.Continue;
            },
            // Rubber band fully inside the back border.
            new GeometryHitTestParameters(new RectangleGeometry(new Rect(200, 200, 10, 10))));

        Assert.Null(seen);

        VisualTreeHelper.HitTest(canvas, null,
            result =>
            {
                if (ReferenceEquals(result.VisualHit, back))
                    seen = ((GeometryHitTestResult)result).IntersectionDetail;
                return HitTestResultBehavior.Continue;
            },
            new GeometryHitTestParameters(new RectangleGeometry(new Rect(10, 10, 20, 20))));

        Assert.Equal(IntersectionDetail.FullyInside, seen);
    }

    [Fact]
    public void Result_callback_stop_ends_the_walk()
    {
        var canvas = ArrangeTree(out _, out _);
        var count = 0;
        VisualTreeHelper.HitTest(canvas, null,
            _ =>
            {
                count++;
                return HitTestResultBehavior.Stop;
            },
            new GeometryHitTestParameters(new RectangleGeometry(new Rect(0, 0, 200, 200))));

        Assert.Equal(1, count);
    }

    [Fact]
    public void Filter_skip_children_prunes_the_subtree()
    {
        var canvas = ArrangeTree(out var back, out var front);
        var inner = new Border { Width = 20, Height = 20 };
        front.AddChild(inner);
        canvas.Measure(new WpfSize(200, 200));
        canvas.Arrange(new WpfRect(0, 0, 200, 200));
        var hits = new List<Visual>();
        VisualTreeHelper.HitTest(canvas,
            target => ReferenceEquals(target, front)
                ? HitTestFilterBehavior.ContinueSkipChildren
                : HitTestFilterBehavior.Continue,
            result =>
            {
                hits.Add((Visual)result.VisualHit);
                return HitTestResultBehavior.Continue;
            },
            new GeometryHitTestParameters(new RectangleGeometry(new Rect(0, 0, 200, 200))));

        // The skipped node itself still reports; its subtree does not.
        Assert.Contains(back, hits);
        Assert.Contains(front, hits);
        Assert.DoesNotContain(inner, hits);
    }

    [Fact]
    public void Filter_stop_ends_before_any_result()
    {
        var canvas = ArrangeTree(out _, out _);
        var count = 0;
        VisualTreeHelper.HitTest(canvas,
            _ => HitTestFilterBehavior.Stop,
            _ =>
            {
                count++;
                return HitTestResultBehavior.Continue;
            },
            new GeometryHitTestParameters(new RectangleGeometry(new Rect(0, 0, 200, 200))));

        Assert.Equal(0, count);
    }

    [Fact]
    public void Hit_test_rejects_null_arguments()
    {
        var canvas = ArrangeTree(out _, out _);
        Assert.Throws<ArgumentNullException>(() =>
            VisualTreeHelper.HitTest(null!, new Point(0, 0)));
        Assert.Throws<ArgumentNullException>(() =>
            VisualTreeHelper.HitTest(canvas, null, null!, new GeometryHitTestParameters(new RectangleGeometry())));
        Assert.Throws<ArgumentNullException>(() =>
            VisualTreeHelper.HitTest(canvas, null, _ => HitTestResultBehavior.Continue, null!));
    }

    private sealed class ProbeAdorner : Adorner
    {
        private readonly VisualCollection _visuals;

        public ProbeAdorner(UIElement adorned)
            : base(adorned)
        {
            _visuals = new VisualCollection(this);
        }

        public void Host(Visual visual) => _visuals.Add(visual);

        public int ProbeCount => VisualChildrenCount;

        public Visual ProbeChild(int index) => GetVisualChild(index);

        // Same override shape as CroppingAdorner/ResizingAdorner upstream.
        protected override int VisualChildrenCount => _visuals.Count;

        protected override Visual GetVisualChild(int index) => _visuals[index];
    }

    private sealed class ProbeHost : Canvas
    {
        public int ProbeCount => VisualChildrenCount;

        public Visual ProbeChild(int index) => GetVisualChild(index);
    }

    [Fact]
    public void Visual_collection_backs_adorner_overrides()
    {
        var canvas = ArrangeTree(out var back, out _);
        var adorner = new ProbeAdorner(back);
        var mask = new Border { Width = 10, Height = 10 };
        var thumbs = new Canvas { Width = 10, Height = 10 };
        adorner.Host(mask);
        adorner.Host(thumbs);

        Assert.Equal(2, adorner.ProbeCount);
        Assert.Same(mask, adorner.ProbeChild(0));
        Assert.Same(thumbs, adorner.ProbeChild(1));
        Assert.Same(adorner, VisualTreeHelper.GetParent(mask));
        Assert.Throws<ArgumentOutOfRangeException>(() => adorner.ProbeChild(2));
    }

    [Fact]
    public void Visual_collection_add_returns_index_and_rejects_reparenting()
    {
        var canvas = ArrangeTree(out var back, out _);
        var first = new VisualCollection(back);
        var second = new VisualCollection(canvas);
        var child = new Border { Width = 5, Height = 5 };

        Assert.Equal(0, first.Add(child));
        Assert.Equal(1, first.Add(new Border { Width = 5, Height = 5 }));
        Assert.Equal(2, first.Count);
        Assert.True(first.Contains(child));
        Assert.Equal(0, first.IndexOf(child));
        Assert.Same(back, VisualTreeHelper.GetParent(child));

        Assert.Throws<InvalidOperationException>(() => second.Add(child));

        first.Remove(child);
        Assert.Null(VisualTreeHelper.GetParent(child));
        Assert.Equal(0, second.Add(child));
        Assert.Same(canvas, VisualTreeHelper.GetParent(child));
    }

    [Fact]
    public void Visual_collection_collection_initializer_like_cropping_adorner()
    {
        var host = new ProbeHost();
        var collection = new VisualCollection(host) { new Border(), new Canvas() };

        Assert.Equal(2, collection.Count);
        Assert.Equal(2, host.ProbeCount);
        Assert.Same(collection[0], host.ProbeChild(0));
    }

    [Fact]
    public void Flattened_path_geometry_approximates_shapes()
    {
        var flat = new RectangleGeometry(new Rect(0, 0, 10, 20))
            .GetFlattenedPathGeometry(0, ToleranceType.Absolute);
        var figure = Assert.Single(flat.Figures);
        Assert.True(figure.IsClosed);
        Assert.Equal(new Point(0, 0), figure.StartPoint);
        Assert.Equal(3, figure.Segments.Count);

        var curve = new PathGeometry();
        var curveFigure = new PathFigure
        {
            StartPoint = new Point(0, 0),
            IsClosed = true,
        };
        curveFigure.Segments.Add(new BezierSegment
        {
            Point1 = new Point(0, 10),
            Point2 = new Point(10, 10),
            Point3 = new Point(10, 0),
        });
        curve.Figures.Add(curveFigure);
        var flatCurve = curve.GetFlattenedPathGeometry(0, ToleranceType.Absolute);
        var flatFigure = Assert.Single(flatCurve.Figures);
        Assert.True(flatFigure.Segments.Count > 1);
        Assert.Equal(new Point(0, 0), flatFigure.StartPoint);
        var last = Assert.IsType<LineSegment>(flatFigure.Segments[^1]);
        Assert.Equal(new Point(10, 0), last.Point);
    }

    [Fact]
    public void Combine_keeps_both_outlines_like_combined_geometry()
    {
        var union = Geometry.Combine(
            new RectangleGeometry(new Rect(0, 0, 10, 10)),
            new RectangleGeometry(new Rect(100, 100, 10, 10)),
            GeometryCombineMode.Union, Transform.Identity);
        Assert.Equal(new Rect(0, 0, 110, 110), union.Bounds);
        Assert.Equal(2, union.Figures.Count);
    }
}
