using System.Globalization;
using System.Text;

namespace DesktopWasm.Wpf;

// 2D geometry + brush stack (PresentationCore subset, shim, not port):
// Geometry (path mini-language, shape geometries), Pen, Drawing/
// DrawingGroup/GeometryDrawing, TileBrush/DrawingBrush/VisualBrush and the
// gradient family. Names, member shapes and enum values match real WPF so the
// converter binds original IL by name; painters consume them through the
// existing retained DrawingContext ops (DrawGeometry/DrawRectangle), so no
// transport changes are needed for the covered app surface.

/// <summary>Tiling behavior for tile brushes (matches System.Windows.Media.TileMode; order and values verified against WPF).</summary>
public enum TileMode
{
    None,
    FlipX,
    FlipY,
    FlipXY,
    Tile,
}

/// <summary>Viewport units for tile brushes (matches System.Windows.Media.BrushMappingMode).</summary>
public enum BrushMappingMode
{
    Absolute,
    RelativeToBoundingBox,
}

/// <summary>Fill rule for composite geometries (matches System.Windows.Media.FillRule).</summary>
public enum FillRule
{
    EvenOdd,
    Nonzero,
}

/// <summary>Arc sweep direction (matches System.Windows.Media.SweepDirection).</summary>
public enum SweepDirection
{
    Counterclockwise,
    Clockwise,
}

/// <summary>2D geometry root (matches System.Windows.Media.Geometry).</summary>
public abstract class Geometry : Animatable
{
    /// <summary>Parses path mini-language data (M/L/H/V/C/S/Q/T/A/Z, absolute and relative).</summary>
    public static Geometry Parse(string pathData)
    {
        ArgumentException.ThrowIfNullOrEmpty(pathData);
        return GeometryParser.Parse(pathData);
    }

    /// <summary>Appends canonical absolute SVG path data for the retained painters.</summary>
    internal abstract void AppendSvg(StringBuilder target);

    /// <summary>Canonical absolute SVG path data (Skia and Canvas2D Path2D both read it).</summary>
    public string ToPathData()
    {
        var target = new StringBuilder();
        AppendSvg(target);
        return target.ToString();
    }

    /// <summary>Conservative bounds over control points (measure input, not a raster bound).</summary>
    public abstract Rect GetBounds();

    /// <summary>Empty geometry (matches Geometry.Empty).</summary>
    public static Geometry Empty { get; } = new PathGeometry();

    /// <summary>Axis-aligned bounding box with <see cref="Transform"/> applied (matches Bounds).</summary>
    public Rect Bounds => Transform is null ? GetBounds() : Transform.TransformBounds(GetBounds());

    /// <summary>Geometry transform (matches Transform; applied by Bounds and the paint path).</summary>
    public Transform? Transform { get; set; }

    /// <summary>Stroke-inflated render bounds (matches GetRenderBounds).</summary>
    public Rect GetRenderBounds(Pen? pen) => GetRenderBounds(pen, 0.0);

    /// <summary>
    /// Stroke-inflated render bounds with tolerance (matches GetRenderBounds):
    /// the box grows by half the pen width plus the tolerance on each side.
    /// </summary>
    public Rect GetRenderBounds(Pen? pen, double tolerance)
    {
        var bounds = Bounds;
        if (bounds.IsEmpty) return bounds;
        var grow = tolerance + (pen is null ? 0 : Math.Max(0, pen.Thickness) / 2);
        if (grow <= 0) return bounds;
        return new Rect(bounds.X - grow, bounds.Y - grow, bounds.Width + 2 * grow, bounds.Height + 2 * grow);
    }

    /// <summary>Standard flattening tolerance (matches Geometry.StandardFlatteningTolerance).</summary>
    public static double StandardFlatteningTolerance => 0.25;

    /// <summary>Polygonal approximation with standard tolerance (matches GetFlattenedPathGeometry()).</summary>
    public PathGeometry GetFlattenedPathGeometry() =>
        GetFlattenedPathGeometry(StandardFlatteningTolerance, ToleranceType.Absolute);

    /// <summary>
    /// Polygonal approximation of this geometry (matches
    /// GetFlattenedPathGeometry): lines/rectangles stay exact, beziers and
    /// arcs subdivide into line runs, and the source Transform carries over
    /// (it is not baked in, like WPF). Converted SelectControl calls this
    /// with (0, Absolute) for its window-region clip.
    /// </summary>
    public virtual PathGeometry GetFlattenedPathGeometry(double tolerance, ToleranceType toleranceType)
    {
        var flat = new PathGeometry { Transform = Transform };
        if (Bounds.IsEmpty) return flat;
        foreach (var figure in FlattenFigures(SubdivisionsFor(this, tolerance, toleranceType)))
            flat.Figures.Add(figure);
        return flat;
    }

    /// <summary>
    /// Combined outline (matches Geometry.Combine): both inputs flatten
    /// into one PathGeometry. Boolean path ops are a documented next step;
    /// like <see cref="CombinedGeometry"/> paint this keeps the union of
    /// both outlines instead of fabricating an intersection.
    /// </summary>
    public static PathGeometry Combine(Geometry? geometry1, Geometry? geometry2, GeometryCombineMode mode, Transform? transform) =>
        Combine(geometry1, geometry2, mode, transform, StandardFlatteningTolerance, ToleranceType.Absolute);

    /// <summary>Combined outline with an explicit flattening tolerance (matches Geometry.Combine).</summary>
    public static PathGeometry Combine(Geometry? geometry1, Geometry? geometry2, GeometryCombineMode mode, Transform? transform, double tolerance, ToleranceType toleranceType)
    {
        ArgumentNullException.ThrowIfNull(geometry1);
        ArgumentNullException.ThrowIfNull(geometry2);
        var flat = new PathGeometry { Transform = transform };
        foreach (var figure in geometry1.FlattenFigures(SubdivisionsFor(geometry1, tolerance, toleranceType)))
            flat.Figures.Add(figure);
        foreach (var figure in geometry2.FlattenFigures(SubdivisionsFor(geometry2, tolerance, toleranceType)))
            flat.Figures.Add(figure);
        return flat;
    }

    /// <summary>Figures this geometry flattens into (concrete geometries override; unknown ones degrade to their bounds).</summary>
    internal virtual IEnumerable<PathFigure> FlattenFigures(int subdivisions)
    {
        var bounds = Bounds;
        if (bounds.IsEmpty) yield break;
        yield return FlattenRectangle(bounds);
    }

    internal static int SubdivisionsFor(Geometry geometry, double tolerance, ToleranceType toleranceType)
    {
        var bounds = geometry.Bounds;
        var scale = Math.Max(bounds.Width, bounds.Height);
        if (!(scale > 0)) scale = 1;
        var absolute = Math.Max(0, tolerance) * (toleranceType == ToleranceType.Relative ? scale : 1);
        return absolute <= 0 ? 32 : (int)Math.Clamp(Math.Ceiling(16.0 / absolute), 4, 256);
    }

    internal static PathFigure FlattenRoundedRectangle(Rect rect, double radiusX, double radiusY)
    {
        var rx = Math.Min(Math.Max(0, radiusX), rect.Width / 2);
        var ry = Math.Min(Math.Max(0, radiusY), rect.Height / 2);
        var figure = new PathFigure
        {
            StartPoint = new Point(rect.X + rx, rect.Y),
            IsClosed = true,
            IsFilled = true,
        };
        ArcSegment cornerTo(double x, double y) => new ArcSegment
        {
            Point = new Point(x, y),
            Size = new Size(rx, ry),
            SweepDirection = SweepDirection.Clockwise,
        };
        figure.Segments.Add(new LineSegment { Point = new Point(rect.Right - rx, rect.Y) });
        figure.Segments.Add(cornerTo(rect.Right, rect.Y + ry));
        figure.Segments.Add(new LineSegment { Point = new Point(rect.Right, rect.Bottom - ry) });
        figure.Segments.Add(cornerTo(rect.Right - rx, rect.Bottom));
        figure.Segments.Add(new LineSegment { Point = new Point(rect.X + rx, rect.Bottom) });
        figure.Segments.Add(cornerTo(rect.X, rect.Bottom - ry));
        figure.Segments.Add(new LineSegment { Point = new Point(rect.X, rect.Y + ry) });
        figure.Segments.Add(cornerTo(rect.X + rx, rect.Y));
        return figure;
    }

    internal static PathFigure FlattenRectangle(Rect rect)
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(rect.X, rect.Y),
            IsClosed = true,
            IsFilled = true,
        };
        figure.Segments.Add(new LineSegment { Point = new Point(rect.Right, rect.Y) });
        figure.Segments.Add(new LineSegment { Point = new Point(rect.Right, rect.Bottom) });
        figure.Segments.Add(new LineSegment { Point = new Point(rect.X, rect.Bottom) });
        return figure;
    }

    internal static PathFigure FlattenFigure(PathFigure figure, int subdivisions)
    {
        var flat = new PathFigure
        {
            StartPoint = figure.StartPoint,
            IsClosed = figure.IsClosed,
            IsFilled = figure.IsFilled,
        };
        var current = figure.StartPoint;
        foreach (var segment in figure.Segments)
        {
            switch (segment)
            {
                case LineSegment line:
                    flat.Segments.Add(new LineSegment { Point = line.Point });
                    current = line.Point;
                    break;
                case BezierSegment bezier:
                    foreach (var point in SampleCubic(current, bezier.Point1, bezier.Point2, bezier.Point3, subdivisions))
                        flat.Segments.Add(new LineSegment { Point = point });
                    current = bezier.Point3;
                    break;
                case QuadraticBezierSegment quadratic:
                    foreach (var point in SampleQuadratic(current, quadratic.Point1, quadratic.Point2, subdivisions))
                        flat.Segments.Add(new LineSegment { Point = point });
                    current = quadratic.Point2;
                    break;
                case ArcSegment arc:
                    foreach (var point in SampleArc(current, arc, subdivisions))
                        flat.Segments.Add(new LineSegment { Point = point });
                    current = arc.Point;
                    break;
                case PolyLineSegment polyLine:
                    foreach (var point in polyLine.Points)
                    {
                        flat.Segments.Add(new LineSegment { Point = point });
                        current = point;
                    }
                    break;
                case PolyBezierSegment polyBezier:
                    current = SamplePolyCubic(flat, current, polyBezier.Points, subdivisions);
                    break;
                case PolyQuadraticBezierSegment polyQuadratic:
                    current = SamplePolyQuadratic(flat, current, polyQuadratic.Points, subdivisions);
                    break;
                default:
                    // Unknown segment kinds keep their end point so the
                    // outline stays connected instead of dropping the run.
                    current = segment.GetEnd(current);
                    flat.Segments.Add(new LineSegment { Point = current });
                    break;
            }
        }
        return flat;
    }

    private static Point SamplePolyCubic(PathFigure flat, Point current, PointCollection points, int subdivisions)
    {
        for (var i = 0; i + 2 < points.Count; i += 3)
        {
            foreach (var point in SampleCubic(current, points[i], points[i + 1], points[i + 2], subdivisions))
                flat.Segments.Add(new LineSegment { Point = point });
            current = points[i + 2];
        }
        return current;
    }

    private static Point SamplePolyQuadratic(PathFigure flat, Point current, PointCollection points, int subdivisions)
    {
        for (var i = 0; i + 1 < points.Count; i += 2)
        {
            foreach (var point in SampleQuadratic(current, points[i], points[i + 1], subdivisions))
                flat.Segments.Add(new LineSegment { Point = point });
            current = points[i + 1];
        }
        return current;
    }

    internal static IEnumerable<Point> SampleCubic(Point start, Point control1, Point control2, Point end, int subdivisions)
    {
        for (var i = 1; i <= subdivisions; i++)
        {
            var t = (double)i / subdivisions;
            var missing = 1 - t;
            yield return new Point(
                missing * missing * missing * start.X + 3 * missing * missing * t * control1.X +
                    3 * missing * t * t * control2.X + t * t * t * end.X,
                missing * missing * missing * start.Y + 3 * missing * missing * t * control1.Y +
                    3 * missing * t * t * control2.Y + t * t * t * end.Y);
        }
    }

    internal static IEnumerable<Point> SampleQuadratic(Point start, Point control, Point end, int subdivisions)
    {
        for (var i = 1; i <= subdivisions; i++)
        {
            var t = (double)i / subdivisions;
            var missing = 1 - t;
            yield return new Point(
                missing * missing * start.X + 2 * missing * t * control.X + t * t * end.X,
                missing * missing * start.Y + 2 * missing * t * control.Y + t * t * end.Y);
        }
    }

    internal static IEnumerable<Point> SampleArc(Point start, ArcSegment arc, int subdivisions)
    {
        var radiusX = Math.Abs(arc.Size.Width);
        var radiusY = Math.Abs(arc.Size.Height);
        if (!(radiusX > 0) || !(radiusY > 0) || start.Equals(arc.Point))
        {
            // Degenerate arcs draw as a straight line (matches WPF).
            yield return arc.Point;
            yield break;
        }
        var angle = arc.RotationAngle * Math.PI / 180;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var halfX = (start.X - arc.Point.X) / 2;
        var halfY = (start.Y - arc.Point.Y) / 2;
        var primeX = cos * halfX + sin * halfY;
        var primeY = -sin * halfX + cos * halfY;
        var radii = primeX * primeX / (radiusX * radiusX) + primeY * primeY / (radiusY * radiusY);
        if (radii > 1)
        {
            var fix = Math.Sqrt(radii);
            radiusX *= fix;
            radiusY *= fix;
        }
        var numerator = radiusX * radiusX * radiusY * radiusY - radiusX * radiusX * primeY * primeY -
            radiusY * radiusY * primeX * primeX;
        var denominator = radiusX * radiusX * primeY * primeY + radiusY * radiusY * primeX * primeX;
        var factor = Math.Sqrt(Math.Max(0, denominator <= 0 ? 0 : numerator / denominator)) *
            (arc.IsLargeArc == (arc.SweepDirection == SweepDirection.Clockwise) ? -1 : 1);
        var centerPrimeX = factor * radiusX * primeY / radiusY;
        var centerPrimeY = -factor * radiusY * primeX / radiusX;
        var centerX = cos * centerPrimeX - sin * centerPrimeY + (start.X + arc.Point.X) / 2;
        var centerY = sin * centerPrimeX + cos * centerPrimeY + (start.Y + arc.Point.Y) / 2;
        var startAngle = ArcAngle(1, 0, (primeX - centerPrimeX) / radiusX, (primeY - centerPrimeY) / radiusY);
        var sweep = ArcAngle((primeX - centerPrimeX) / radiusX, (primeY - centerPrimeY) / radiusY,
            (-primeX - centerPrimeX) / radiusX, (-primeY - centerPrimeY) / radiusY);
        if (arc.SweepDirection != SweepDirection.Clockwise && sweep > 0) sweep -= 2 * Math.PI;
        if (arc.SweepDirection == SweepDirection.Clockwise && sweep < 0) sweep += 2 * Math.PI;
        for (var i = 1; i <= subdivisions; i++)
        {
            var theta = startAngle + sweep * i / subdivisions;
            yield return new Point(
                centerX + radiusX * cos * Math.Cos(theta) - radiusY * sin * Math.Sin(theta),
                centerY + radiusX * sin * Math.Cos(theta) + radiusY * cos * Math.Sin(theta));
        }
    }

    private static double ArcAngle(double ux, double uy, double vx, double vy)
    {
        var dot = ux * vx + uy * vy;
        var length = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
        if (!(length > 0)) return 0;
        var angle = Math.Acos(Math.Clamp(dot / length, -1, 1));
        return ux * vy - uy * vx < 0 ? -angle : angle;
    }
}

/// <summary>Straight segment between two points (matches System.Windows.Media.LineGeometry).</summary>
public sealed class LineGeometry : Geometry
{
    public Point StartPoint { get; set; }
    public Point EndPoint { get; set; }

    internal override void AppendSvg(StringBuilder target) =>
        target.Append(CultureInfo.InvariantCulture,
            $"M {StartPoint.X} {StartPoint.Y} L {EndPoint.X} {EndPoint.Y} ");

    public override Rect GetBounds() => new(StartPoint, EndPoint);

    internal override IEnumerable<PathFigure> FlattenFigures(int subdivisions)
    {
        yield return new PathFigure
        {
            StartPoint = StartPoint,
            Segments = { new LineSegment { Point = EndPoint } },
        };
    }
}

/// <summary>Axis-aligned rectangle (matches System.Windows.Media.RectangleGeometry).</summary>
public sealed class RectangleGeometry : Geometry
{
    public RectangleGeometry()
    {
    }

    public RectangleGeometry(Rect rect) => Rect = rect;

    /// <summary>Rounded rectangle (matches
    /// System.Windows.Media.RectangleGeometry(Rect, double, double)).</summary>
    public RectangleGeometry(Rect rect, double radiusX, double radiusY)
    {
        Rect = rect;
        RadiusX = radiusX;
        RadiusY = radiusY;
    }

    public Rect Rect { get; set; }

    public double RadiusX { get; set; }

    public double RadiusY { get; set; }

    /// <summary>Parses WPF rectangle forms ("x,y,w,h" and "x,y w,h").</summary>
    public static Rect ParseRect(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        var normalized = value.Trim().Replace(' ', ',');
        while (normalized.Contains(",,", StringComparison.Ordinal))
            normalized = normalized.Replace(",,", ",", StringComparison.Ordinal);
        try
        {
            return Rect.Parse(normalized);
        }
        catch (FormatException ex)
        {
            throw new FormatException($"'{value}' is not a rectangle (x,y,w,h or x,y w,h).", ex);
        }
    }

    internal override void AppendSvg(StringBuilder target)
    {
        if (RadiusX <= 0 || RadiusY <= 0)
        {
            target.Append(CultureInfo.InvariantCulture,
                $"M {Rect.X} {Rect.Y} h {Rect.Width} v {Rect.Height} h {-Rect.Width} Z ");
            return;
        }
        var rx = Math.Min(RadiusX, Rect.Width / 2);
        var ry = Math.Min(RadiusY, Rect.Height / 2);
        var x = Rect.X; var y = Rect.Y; var r = Rect.Right; var b = Rect.Bottom;
        target.Append(CultureInfo.InvariantCulture,
            $"M {x + rx} {y} L {r - rx} {y} A {rx} {ry} 0 0 1 {r} {y + ry} " +
            $"L {r} {b - ry} A {rx} {ry} 0 0 1 {r - rx} {b} " +
            $"L {x + rx} {b} A {rx} {ry} 0 0 1 {x} {b - ry} " +
            $"L {x} {y + ry} A {rx} {ry} 0 0 1 {x + rx} {y} Z ");
    }

    public override Rect GetBounds() => Rect;

    internal override IEnumerable<PathFigure> FlattenFigures(int subdivisions)
    {
            if (RadiusX <= 0 || RadiusY <= 0)
                yield return FlattenRectangle(Rect);
            else
                yield return FlattenRoundedRectangle(Rect, RadiusX, RadiusY);
    }
}

/// <summary>Axis-aligned ellipse (matches System.Windows.Media.EllipseGeometry).</summary>
public sealed class EllipseGeometry : Geometry
{
    public Point Center { get; set; }
    public double RadiusX { get; set; }
    public double RadiusY { get; set; }

    internal override void AppendSvg(StringBuilder target)
    {
        var left = Center.X - RadiusX;
        target.Append(CultureInfo.InvariantCulture,
            $"M {left} {Center.Y} A {RadiusX} {RadiusY} 0 1 0 {left + 2 * RadiusX} {Center.Y} A {RadiusX} {RadiusY} 0 1 0 {left} {Center.Y} Z ");
    }

    public override Rect GetBounds() =>
        new(Center.X - RadiusX, Center.Y - RadiusY, Math.Max(0, 2 * RadiusX), Math.Max(0, 2 * RadiusY));

    internal override IEnumerable<PathFigure> FlattenFigures(int subdivisions)
    {
        var figure = new PathFigure { IsClosed = true, IsFilled = true };
        var count = Math.Max(12, subdivisions);
        for (var i = 0; i < count; i++)
        {
            var angle = 2 * Math.PI * i / count;
            var point = new Point(Center.X + RadiusX * Math.Cos(angle), Center.Y + RadiusY * Math.Sin(angle));
            if (i == 0) figure.StartPoint = point;
            else figure.Segments.Add(new LineSegment { Point = point });
        }
        yield return figure;
    }
}

/// <summary>Geometry list (matches System.Windows.Media.GeometryGroup).</summary>
public sealed class GeometryGroup : Geometry
{
    public GeometryCollection Children { get; } = new();
    public FillRule FillRule { get; set; } = FillRule.EvenOdd;

    internal override void AppendSvg(StringBuilder target)
    {
        foreach (var child in Children) child.AppendSvg(target);
    }

    public override Rect GetBounds()
    {
        var bounds = Rect.Empty;
        foreach (var child in Children) bounds.Union(child.GetBounds());
        return bounds;
    }

    internal override IEnumerable<PathFigure> FlattenFigures(int subdivisions)
    {
        foreach (var child in Children)
            foreach (var figure in child.FlattenFigures(subdivisions))
                yield return figure;
    }
}

/// <summary>Geometry list (matches System.Windows.Media.GeometryCollection).</summary>
public sealed class GeometryCollection : List<Geometry>
{
}

/// <summary>Boolean combination selector (matches System.Windows.Media.GeometryCombineMode; values verified against WPF).</summary>
public enum GeometryCombineMode
{
    Union,
    Intersect,
    Xor,
    Exclude,
}

/// <summary>
/// Boolean combination of two geometries (matches System.Windows.Media.CombinedGeometry).
/// The mode and both inputs are retained and queryable; paint emits the union
/// of both outlines because the retained ops carry no path-boolean operator
/// (a real path-op is the documented next step for this demo family).
/// </summary>
public sealed class CombinedGeometry : Geometry
{
    public Geometry? Geometry1 { get; set; }
    public Geometry? Geometry2 { get; set; }
    public GeometryCombineMode GeometryCombineMode { get; set; }

    internal override void AppendSvg(StringBuilder target)
    {
        Geometry1?.AppendSvg(target);
        Geometry2?.AppendSvg(target);
    }

    public override Rect GetBounds()
    {
        var bounds = Rect.Empty;
        if (Geometry1 is not null) bounds.Union(Geometry1.GetBounds());
        if (Geometry2 is not null) bounds.Union(Geometry2.GetBounds());
        return bounds;
    }

    internal override IEnumerable<PathFigure> FlattenFigures(int subdivisions)
    {
        if (Geometry1 is not null)
            foreach (var figure in Geometry1.FlattenFigures(subdivisions))
                yield return figure;
        if (Geometry2 is not null)
            foreach (var figure in Geometry2.FlattenFigures(subdivisions))
                yield return figure;
    }
}

/// <summary>Figure-based geometry (matches System.Windows.Media.PathGeometry).</summary>
public sealed class PathGeometry : Geometry
{
    public PathGeometry()
    {
    }

    /// <summary>
    /// Figure-backed geometry (matches PathGeometry(IEnumerable&lt;PathFigure&gt;);
    /// converted AvalonDock drop targets bind it for their preview paths).
    /// </summary>
    public PathGeometry(IEnumerable<PathFigure>? figures)
    {
        if (figures is not null) Figures.AddRange(figures);
    }

    public PathFigureCollection Figures { get; } = new();
    public FillRule FillRule { get; set; } = FillRule.EvenOdd;

    internal override void AppendSvg(StringBuilder target)
    {
        foreach (var figure in Figures) figure.AppendSvg(target);
    }

    public override Rect GetBounds()
    {
        var bounds = Rect.Empty;
        foreach (var figure in Figures) bounds.Union(figure.GetBounds());
        return bounds;
    }

    /// <summary>Flattened copy preserving the fill rule (matches GetFlattenedPathGeometry).</summary>
    public override PathGeometry GetFlattenedPathGeometry(double tolerance, ToleranceType toleranceType)
    {
        var flat = base.GetFlattenedPathGeometry(tolerance, toleranceType);
        flat.FillRule = FillRule;
        return flat;
    }

    internal override IEnumerable<PathFigure> FlattenFigures(int subdivisions)
    {
        foreach (var figure in Figures) yield return FlattenFigure(figure, subdivisions);
    }
}

/// <summary>One figure: start point, segments, optional close (matches System.Windows.Media.PathFigure).</summary>
public sealed class PathFigure
{
    public Point StartPoint { get; set; }
    public bool IsClosed { get; set; }
    public bool IsFilled { get; set; } = true;
    public PathSegmentCollection Segments { get; } = new();

    internal void AppendSvg(StringBuilder target)
    {
        target.Append(CultureInfo.InvariantCulture, $"M {StartPoint.X} {StartPoint.Y} ");
        var current = StartPoint;
        foreach (var segment in Segments) current = segment.AppendSvg(target, current);
        if (IsClosed) target.Append("Z ");
    }

    internal Rect GetBounds()
    {
        var bounds = new Rect(StartPoint, new Size(0, 0));
        var current = StartPoint;
        foreach (var segment in Segments)
        {
            bounds.Union(segment.GetEnd(current));
            foreach (var control in segment.GetControls(current)) bounds.Union(control);
            current = segment.GetEnd(current);
        }
        return bounds;
    }
}

/// <summary>Figure list (matches System.Windows.Media.PathFigureCollection).</summary>
public sealed class PathFigureCollection : List<PathFigure>
{
    /// <summary>Value enumerator (matches PathFigureCollection.Enumerator; wraps the list enumerator).</summary>
    public new Enumerator GetEnumerator() => new(this);

    /// <summary>Figure enumerator (matches System.Windows.Media.PathFigureCollection/Enumerator).</summary>
    public struct Enumerator : IEnumerator<PathFigure>
    {
        private readonly List<PathFigure> _list;
        private int _index;

        internal Enumerator(List<PathFigure> list)
        {
            _list = list;
            _index = -1;
        }

        public PathFigure Current => (uint)_index < (uint)_list.Count
            ? _list[_index]
            : throw new InvalidOperationException("Enumeration has not started or has already finished.");

        object System.Collections.IEnumerator.Current => Current;
        public bool MoveNext() => ++_index < _list.Count;
        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}

/// <summary>Segment list (matches System.Windows.Media.PathSegmentCollection).</summary>
public sealed class PathSegmentCollection : List<PathSegment>
{
    /// <summary>Value enumerator (matches PathSegmentCollection.Enumerator; wraps the list enumerator).</summary>
    public new Enumerator GetEnumerator() => new(this);

    /// <summary>Segment enumerator (matches System.Windows.Media.PathSegmentCollection/Enumerator).</summary>
    public struct Enumerator : IEnumerator<PathSegment>
    {
        private readonly List<PathSegment> _list;
        private int _index;

        internal Enumerator(List<PathSegment> list)
        {
            _list = list;
            _index = -1;
        }

        public PathSegment Current => (uint)_index < (uint)_list.Count
            ? _list[_index]
            : throw new InvalidOperationException("Enumeration has not started or has already finished.");

        object System.Collections.IEnumerator.Current => Current;
        public bool MoveNext() => ++_index < _list.Count;
        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}

/// <summary>Figure segment root (matches System.Windows.Media.PathSegment).</summary>
public abstract class PathSegment
{
    /// <summary>Stroke participation (matches PathSegment.IsStroked; default true like WPF).</summary>
    public bool IsStroked { get; set; } = true;

    /// <summary>Smooth join (matches PathSegment.IsSmoothJoin; default false like WPF).</summary>
    public bool IsSmoothJoin { get; set; }

    internal abstract Point AppendSvg(StringBuilder target, Point current);
    internal abstract Point GetEnd(Point current);
    internal abstract IEnumerable<Point> GetControls(Point current);
}

/// <summary>Straight line to a point (matches System.Windows.Media.LineSegment).</summary>
public sealed class LineSegment : PathSegment
{
    public Point Point { get; set; }

    public LineSegment()
    {
    }

    /// <summary>
    /// Point plus stroke participation (matches the WPF
    /// LineSegment(Point, bool) constructor the converted OxyPlot
    /// XamlRenderContext binds for grid/tick line figures).
    /// </summary>
    public LineSegment(Point point, bool isStroked)
    {
        Point = point;
        IsStroked = isStroked;
    }

    internal override Point AppendSvg(StringBuilder target, Point current)
    {
        target.Append(CultureInfo.InvariantCulture, $"L {Point.X} {Point.Y} ");
        return Point;
    }

    internal override Point GetEnd(Point current) => Point;
    internal override IEnumerable<Point> GetControls(Point current) => [];
}

/// <summary>Cubic Bezier to a point (matches System.Windows.Media.BezierSegment).</summary>
public sealed class BezierSegment : PathSegment
{
    public Point Point1 { get; set; }
    public Point Point2 { get; set; }
    public Point Point3 { get; set; }

    internal override Point AppendSvg(StringBuilder target, Point current)
    {
        target.Append(CultureInfo.InvariantCulture,
            $"C {Point1.X} {Point1.Y} {Point2.X} {Point2.Y} {Point3.X} {Point3.Y} ");
        return Point3;
    }

    internal override Point GetEnd(Point current) => Point3;
    internal override IEnumerable<Point> GetControls(Point current) => [Point1, Point2];
}

/// <summary>Quadratic Bezier to a point (matches System.Windows.Media.QuadraticBezierSegment).</summary>
public sealed class QuadraticBezierSegment : PathSegment
{
    public Point Point1 { get; set; }
    public Point Point2 { get; set; }

    internal override Point AppendSvg(StringBuilder target, Point current)
    {
        target.Append(CultureInfo.InvariantCulture, $"Q {Point1.X} {Point1.Y} {Point2.X} {Point2.Y} ");
        return Point2;
    }

    internal override Point GetEnd(Point current) => Point2;
    internal override IEnumerable<Point> GetControls(Point current) => [Point1];
}

/// <summary>Elliptical arc to a point (matches System.Windows.Media.ArcSegment).</summary>
public sealed class ArcSegment : PathSegment
{
    public Point Point { get; set; }
    public Size Size { get; set; }
    public double RotationAngle { get; set; }
    public bool IsLargeArc { get; set; }
    public SweepDirection SweepDirection { get; set; }

    internal override Point AppendSvg(StringBuilder target, Point current)
    {
        target.Append(CultureInfo.InvariantCulture,
            $"A {Size.Width} {Size.Height} {RotationAngle} {(IsLargeArc ? 1 : 0)} {(SweepDirection == SweepDirection.Clockwise ? 1 : 0)} {Point.X} {Point.Y} ");
        return Point;
    }

    internal override Point GetEnd(Point current) => Point;
    internal override IEnumerable<Point> GetControls(Point current) => [];
}

/// <summary>
/// Path mini-language reader (M/L/H/V/C/S/Q/T/A/Z with implicit repeats).
/// Numbers accept WPF separators (comma/whitespace); a sign starts a new
/// number without a separator. Smooth-segment reflection (S/T) follows the
/// previous control point like real WPF.
/// </summary>
internal static class GeometryParser
{
    internal static PathGeometry Parse(string text)
    {
        var tokens = Tokenize(text);
        var geometry = new PathGeometry();
        PathFigure? figure = null;
        var current = new Point(0, 0);
        var figureStart = current;
        var previousCubic = (Point?)null;
        var previousQuad = (Point?)null;
        var index = 0;
        char command = '\0';
        while (index < tokens.Count)
        {
            if (tokens[index] is CommandToken cmd)
            {
                command = cmd.Letter;
                index++;
                if (command is 'Z' or 'z')
                {
                    if (figure is null) throw new FormatException("Path data 'Z' needs an open figure (start with M).");
                    figure.IsClosed = true;
                    current = figureStart;
                    previousCubic = null;
                    previousQuad = null;
                    continue;
                }
            }
            if (command == '\0') throw new FormatException($"Path data '{text}' must start with a command (M/L/H/V/C/S/Q/T/A/Z).");
            var relative = char.IsLower(command);
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    var point = ReadPoint(tokens, ref index, text);
                    if (relative) point = Offset(point, current);
                    figure = new PathFigure { StartPoint = point };
                    geometry.Figures.Add(figure);
                    current = figureStart = point;
                    previousCubic = null;
                    previousQuad = null;
                    command = relative ? 'l' : 'L';
                    break;
                }
                case 'L':
                {
                    var point = ReadPoint(tokens, ref index, text);
                    if (relative) point = Offset(point, current);
                    Figure(figure, text).Segments.Add(new LineSegment { Point = point });
                    current = point;
                    previousCubic = null;
                    previousQuad = null;
                    break;
                }
                case 'H':
                {
                    var x = ReadNumber(tokens, ref index, text);
                    if (relative) x += current.X;
                    var point = new Point(x, current.Y);
                    Figure(figure, text).Segments.Add(new LineSegment { Point = point });
                    current = point;
                    previousCubic = null;
                    previousQuad = null;
                    break;
                }
                case 'V':
                {
                    var y = ReadNumber(tokens, ref index, text);
                    if (relative) y += current.Y;
                    var point = new Point(current.X, y);
                    Figure(figure, text).Segments.Add(new LineSegment { Point = point });
                    current = point;
                    previousCubic = null;
                    previousQuad = null;
                    break;
                }
                case 'C':
                {
                    var p1 = ReadPoint(tokens, ref index, text);
                    var p2 = ReadPoint(tokens, ref index, text);
                    var p3 = ReadPoint(tokens, ref index, text);
                    if (relative) { p1 = Offset(p1, current); p2 = Offset(p2, current); p3 = Offset(p3, current); }
                    Figure(figure, text).Segments.Add(new BezierSegment { Point1 = p1, Point2 = p2, Point3 = p3 });
                    previousCubic = p2;
                    previousQuad = null;
                    current = p3;
                    break;
                }
                case 'S':
                {
                    var p2 = ReadPoint(tokens, ref index, text);
                    var p3 = ReadPoint(tokens, ref index, text);
                    if (relative) { p2 = Offset(p2, current); p3 = Offset(p3, current); }
                    var p1 = previousCubic is { } reflected
                        ? new Point(2 * current.X - reflected.X, 2 * current.Y - reflected.Y)
                        : current;
                    Figure(figure, text).Segments.Add(new BezierSegment { Point1 = p1, Point2 = p2, Point3 = p3 });
                    previousCubic = p2;
                    previousQuad = null;
                    current = p3;
                    break;
                }
                case 'Q':
                {
                    var p1 = ReadPoint(tokens, ref index, text);
                    var p2 = ReadPoint(tokens, ref index, text);
                    if (relative) { p1 = Offset(p1, current); p2 = Offset(p2, current); }
                    Figure(figure, text).Segments.Add(new QuadraticBezierSegment { Point1 = p1, Point2 = p2 });
                    previousQuad = p1;
                    previousCubic = null;
                    current = p2;
                    break;
                }
                case 'T':
                {
                    var p2 = ReadPoint(tokens, ref index, text);
                    if (relative) p2 = Offset(p2, current);
                    var p1 = previousQuad is { } reflected
                        ? new Point(2 * current.X - reflected.X, 2 * current.Y - reflected.Y)
                        : current;
                    Figure(figure, text).Segments.Add(new QuadraticBezierSegment { Point1 = p1, Point2 = p2 });
                    previousQuad = p1;
                    previousCubic = null;
                    current = p2;
                    break;
                }
                case 'A':
                {
                    var size = ReadSize(tokens, ref index, text);
                    var angle = ReadNumber(tokens, ref index, text);
                    var large = ReadFlag(tokens, ref index, text);
                    var sweep = ReadFlag(tokens, ref index, text);
                    var point = ReadPoint(tokens, ref index, text);
                    if (relative) point = Offset(point, current);
                    Figure(figure, text).Segments.Add(new ArcSegment
                    {
                        Size = size,
                        RotationAngle = angle,
                        IsLargeArc = large,
                        SweepDirection = sweep ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                        Point = point,
                    });
                    current = point;
                    previousCubic = null;
                    previousQuad = null;
                    break;
                }
                default: throw new FormatException($"Path command '{command}' is not supported (M/L/H/V/C/S/Q/T/A/Z).");
            }
        }
        if (geometry.Figures.Count == 0) throw new FormatException($"Path data '{text}' holds no figures.");
        return geometry;
    }

    private static PathFigure Figure(PathFigure? figure, string text) =>
        figure ?? throw new FormatException($"Path data '{text}' needs a moveto (M) before drawing commands.");

    private static Point Offset(Point point, Point current) => new(point.X + current.X, point.Y + current.Y);

    private static Point ReadPoint(List<object> tokens, ref int index, string text)
    {
        var x = ReadNumber(tokens, ref index, text);
        var y = ReadNumber(tokens, ref index, text);
        return new Point(x, y);
    }

    private static Size ReadSize(List<object> tokens, ref int index, string text)
    {
        var width = ReadNumber(tokens, ref index, text);
        var height = ReadNumber(tokens, ref index, text);
        if (width < 0 || height < 0) throw new FormatException($"Arc radii in '{text}' must be non-negative.");
        return new Size(width, height);
    }

    private static double ReadNumber(List<object> tokens, ref int index, string text)
    {
        if (index >= tokens.Count || tokens[index] is not double value)
            throw new FormatException($"Path data '{text}' is missing a number.");
        index++;
        return value;
    }

    private static bool ReadFlag(List<object> tokens, ref int index, string text)
    {
        var value = ReadNumber(tokens, ref index, text);
        if (value is not (0 or 1)) throw new FormatException($"Arc flags in '{text}' must be 0 or 1.");
        return value == 1;
    }

    private sealed record CommandToken(char Letter);

    private static List<object> Tokenize(string text)
    {
        var tokens = new List<object>();
        var number = new StringBuilder();
        void Flush()
        {
            if (number.Length == 0) return;
            if (!double.TryParse(number.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new FormatException($"Path data '{text}' has a malformed number '{number}'.");
            tokens.Add(value);
            number.Clear();
        }
        foreach (var ch in text)
        {
            // 'e'/'E' is never a path command (commands are M/L/H/V/C/S/Q/T/A/Z):
            // inside a number it starts an exponent (real theme geometry data
            // carries values like 2.0535663480814E-07), so it joins the number.
            // A stray 'e' outside any number still fails loud at Flush.
            if (char.IsLetter(ch) && ch is not 'e' and not 'E')
            {
                Flush();
                tokens.Add(new CommandToken(ch));
            }
            else if (ch is ',' or ' ' or '\t' or '\r' or '\n')
            {
                Flush();
            }
            else if ((ch is '-' or '+') && number.Length > 0 && number[^1] is not 'e' and not 'E')
            {
                Flush();
                number.Append(ch);
            }
            else if (ch is '.' && number.ToString().Contains('.', StringComparison.Ordinal))
            {
                Flush();
                number.Append(ch);
            }
            else if (char.IsDigit(ch) || ch is '.' or '-' or '+' or 'e' or 'E')
            {
                number.Append(ch);
            }
            else
            {
                throw new FormatException($"Path data '{text}' has an unexpected character '{ch}'.");
            }
        }
        Flush();
        return tokens;
    }
}

/// <summary>Stroke description (matches System.Windows.Media.Pen).</summary>
public sealed class Pen : Freezable
{
    public Pen()
    {
    }

    public Pen(Brush brush, double thickness)
    {
        Brush = brush;
        Thickness = thickness;
    }

    public Brush? Brush { get; set; }
    public double Thickness { get; set; } = 1.0;

    /// <summary>Start cap (matches Pen.StartLineCap; Flat like WPF; retained, the retained stroke op is butt-capped).</summary>
    public PenLineCap StartLineCap { get; set; } = PenLineCap.Flat;

    /// <summary>End cap (matches Pen.EndLineCap; Flat like WPF; retained).</summary>
    public PenLineCap EndLineCap { get; set; } = PenLineCap.Flat;

    /// <summary>Dash cap (matches Pen.DashCap; Flat like WPF; retained).</summary>
    public PenLineCap DashCap { get; set; } = PenLineCap.Flat;

    /// <summary>Join (matches Pen.LineJoin; Miter like WPF; retained).</summary>
    public PenLineJoin LineJoin { get; set; } = PenLineJoin.Miter;

    /// <summary>Miter limit (matches Pen.MiterLimit; 10 like WPF; retained).</summary>
    public double MiterLimit { get; set; } = 10.0;

    /// <summary>Dash pattern (matches Pen.DashStyle; null is solid like WPF; retained).</summary>
    public DashStyle? DashStyle { get; set; }
}

/// <summary>Drawing root (matches System.Windows.Media.Drawing).</summary>
public abstract class Drawing : Freezable
{
}

/// <summary>Drawing list (matches System.Windows.Media.DrawingCollection).</summary>
public sealed class DrawingCollection : List<Drawing>
{
}

/// <summary>Geometry fill/stroke node (matches System.Windows.Media.GeometryDrawing).</summary>
public sealed class GeometryDrawing : Drawing
{
    public Brush? Brush { get; set; }
    public Pen? Pen { get; set; }
    public Geometry? Geometry { get; set; }

    public GeometryDrawing()
    {
    }

    /// <summary>Brush/pen/geometry drawing (matches
    /// GeometryDrawing(Brush, Pen, Geometry); converted icon factories
    /// bind it for their pack-icon geometry).</summary>
    public GeometryDrawing(Brush? brush, Pen? pen, Geometry? geometry)
    {
        Brush = brush;
        Pen = pen;
        Geometry = geometry;
    }
}

/// <summary>Tile brush root (matches System.Windows.Media.TileBrush).</summary>
public abstract class TileBrush : Brush
{
    public Rect Viewport { get; set; } = new(0, 0, 1, 1);
    public BrushMappingMode ViewportUnits { get; set; } = BrushMappingMode.RelativeToBoundingBox;
    public Rect Viewbox { get; set; }
    public BrushMappingMode ViewboxUnits { get; set; } = BrushMappingMode.RelativeToBoundingBox;
    public TileMode TileMode { get; set; } = TileMode.Tile;
    public Stretch Stretch { get; set; } = Stretch.Fill;
    public AlignmentX AlignmentX { get; set; } = AlignmentX.Center;
    public AlignmentY AlignmentY { get; set; } = AlignmentY.Center;
}

/// <summary>Drawing-backed tile brush (matches System.Windows.Media.DrawingBrush).</summary>
public sealed class DrawingBrush : TileBrush
{
    public Drawing? Drawing { get; set; }

    public DrawingBrush()
    {
    }

    /// <summary>Drawing-backed tile brush (matches
    /// DrawingBrush(Drawing); converted icon factories bind it).</summary>
    public DrawingBrush(Drawing? drawing)
    {
        Drawing = drawing;
    }
}

/// <summary>Visual-backed tile brush (matches System.Windows.Media.VisualBrush).</summary>
public sealed class VisualBrush : TileBrush
{
    public VisualBrush()
    {
    }

    /// <summary>
    /// Visual-backed brush (matches VisualBrush(Visual); converted docking
    /// managers bind it for their hidden-window previews).
    /// </summary>
    public VisualBrush(Visual? visual) => Visual = visual;

    /// <summary>Referenced visual (retained; the wireframe renderer is unlit and ignores it).</summary>
    public Visual? Visual { get; set; }
}

/// <summary>
/// Image-backed tile brush (matches System.Windows.Media.ImageBrush):
/// the pack/relative <see cref="ImageSource"/> resolves through the same
/// manifest store as images (absent stays null like Image). The fill
/// renderer paints the deterministic solid fallback (like gradients and
/// drawing brushes); the source object stays fully retained and queryable.
/// </summary>
public sealed class ImageBrush : TileBrush
{
    /// <summary>Image painted by the brush (null until set).</summary>
    public ImageSource? ImageSource { get; set; }

    public ImageBrush()
    {
    }

    /// <summary>Paints the given source (matches System.Windows.Media.ImageBrush..ctor(ImageSource)).</summary>
    public ImageBrush(ImageSource? imageSource)
    {
        ImageSource = imageSource;
    }
}

/// <summary>Gradient stop (matches System.Windows.Media.GradientStop).</summary>
public sealed class GradientStop : Freezable
{
    public Color Color { get; set; } = Color.FromRgb(0x00, 0x00, 0x00);
    public double Offset { get; set; }
}

/// <summary>Gradient stop list (matches System.Windows.Media.GradientStopCollection).</summary>
public sealed class GradientStopCollection : List<GradientStop>
{
    /// <summary>Value enumerator (matches GradientStopCollection.Enumerator; wraps the list enumerator).</summary>
    public new Enumerator GetEnumerator() => new(this);

    /// <summary>Stop enumerator (matches System.Windows.Media.GradientStopCollection/Enumerator).</summary>
    public struct Enumerator : IEnumerator<GradientStop>
    {
        private readonly List<GradientStop> _list;
        private int _index;

        internal Enumerator(List<GradientStop> list)
        {
            _list = list;
            _index = -1;
        }

        public GradientStop Current => (uint)_index < (uint)_list.Count
            ? _list[_index]
            : throw new InvalidOperationException("Enumeration has not started or has already finished.");

        object System.Collections.IEnumerator.Current => Current;
        public bool MoveNext() => ++_index < _list.Count;
        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}

/// <summary>Gradient brush root (matches System.Windows.Media.GradientBrush).</summary>
public abstract class GradientBrush : Brush
{
    /// <summary>Stops (settable so {StaticResource} shares the source collection like real WPF).</summary>
    public GradientStopCollection GradientStops { get; set; } = new();
    public BrushMappingMode MappingMode { get; set; } = BrushMappingMode.RelativeToBoundingBox;
}

/// <summary>Two-point gradient (matches System.Windows.Media.LinearGradientBrush).</summary>
public sealed class LinearGradientBrush : GradientBrush
{
    public Point StartPoint { get; set; }
    public Point EndPoint { get; set; } = new(1, 1);
}

/// <summary>Radial gradient (matches System.Windows.Media.RadialGradientBrush).</summary>
public sealed class RadialGradientBrush : GradientBrush
{
    public Point GradientOrigin { get; set; } = new(0.5, 0.5);
    public Point Center { get; set; } = new(0.5, 0.5);
    public double RadiusX { get; set; } = 0.5;
    public double RadiusY { get; set; } = 0.5;
}

/// <summary>
/// Deterministic solid fallback for non-solid brushes. Tiled drawing brushes
/// paint their first solid drawing fill (the grid backgrounds all carry a
/// full-bleed base rect); gradients paint their first stop. Tiling, grid
/// lines and gradient ramps need dedicated paint ops and stay a documented
/// limit; every brush object itself is fully retained and queryable.
/// </summary>
internal static class BrushRender
{
    internal static uint ResolveFill(Brush? brush, uint fallback = 0x00000000)
    {
        return brush switch
        {
            null => fallback,
            SolidColorBrush solid => solid.ToRgba(),
            DrawingBrush drawing => ResolveDrawingFill(drawing.Drawing) ?? fallback,
            LinearGradientBrush linear => FirstStop(linear.GradientStops) ?? fallback,
            RadialGradientBrush radial => FirstStop(radial.GradientStops) ?? fallback,
            VisualBrush => fallback,
            ImageBrush => fallback,
            _ => fallback,
        };
    }

    private static uint? ResolveDrawingFill(Drawing? drawing)
    {
        return drawing switch
        {
            GeometryDrawing direct when direct.Brush is SolidColorBrush solid => solid.ToRgba(),
            DrawingGroup group => group.Children.Select(ResolveDrawingFill).FirstOrDefault(fill => fill is not null),
            _ => null,
        };
    }

    private static uint? FirstStop(GradientStopCollection stops)
    {
        foreach (var stop in stops) return new SolidColorBrush(stop.Color).ToRgba();
        return null;
    }
}

/// <summary>Stroke cap style (matches System.Windows.Media.PenLineCap; values verified against WPF).</summary>
public enum PenLineCap
{
    Flat,
    Square,
    Round,
    Triangle,
}

/// <summary>Stroke join style (matches System.Windows.Media.PenLineJoin; values verified against WPF).</summary>
public enum PenLineJoin
{
    Miter,
    Bevel,
    Round,
}

/// <summary>Gradient color space (matches System.Windows.Media.ColorInterpolationMode; values verified against WPF).</summary>
public enum ColorInterpolationMode
{
    SRgbLinearInterpolation,
    ScRgbLinearInterpolation,
}

/// <summary>Double list (matches System.Windows.Media.DoubleCollection; parses "1,2 3").</summary>
public sealed class DoubleCollection : List<double>
{
    /// <summary>Empty collection (matches DoubleCollection()).</summary>
    public DoubleCollection()
    {
    }

    /// <summary>
    /// Copies values (matches DoubleCollection(IEnumerable&lt;double&gt;);
    /// converted OxyPlot dash arrays bind this constructor).
    /// </summary>
    public DoubleCollection(IEnumerable<double> values) : base(values)
    {
    }

    public static DoubleCollection Parse(string value)
    {
        var result = new DoubleCollection();
        foreach (var token in value.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                throw new FormatException($"'{value}' is not a double list.");
            result.Add(number);
        }
        return result;
    }

    /// <summary>Value enumerator (matches DoubleCollection.Enumerator; wraps the list enumerator).</summary>
    public new Enumerator GetEnumerator() => new(this);

    /// <summary>Double enumerator (matches System.Windows.Media.DoubleCollection/Enumerator).</summary>
    public struct Enumerator : IEnumerator<double>
    {
        private readonly List<double> _list;
        private int _index;

        internal Enumerator(List<double> list)
        {
            _list = list;
            _index = -1;
        }

        public double Current => (uint)_index < (uint)_list.Count
            ? _list[_index]
            : throw new InvalidOperationException("Enumeration has not started or has already finished.");

        object System.Collections.IEnumerator.Current => Current;
        public bool MoveNext() => ++_index < _list.Count;
        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}

/// <summary>Dash pattern (matches System.Windows.Media.DashStyle).</summary>
public sealed class DashStyle : Freezable
{
    public DoubleCollection Dashes { get; } = new();
    public double Offset { get; set; }
}

/// <summary>Stock dash patterns (matches System.Windows.Media.DashStyles; patterns verified against WPF).</summary>
public static class DashStyles
{
    public static DashStyle Solid { get; } = new();
    public static DashStyle Dash { get; } = Create(2, 2);
    public static DashStyle Dot { get; } = Create(0, 1);
    public static DashStyle DashDot { get; } = Create(2, 2, 0, 2);
    public static DashStyle DashDotDot { get; } = Create(2, 2, 0, 2, 0, 2);

    private static DashStyle Create(params double[] dashes)
    {
        var style = new DashStyle();
        foreach (var dash in dashes) style.Dashes.Add(dash);
        return style;
    }
}

/// <summary>Poly-line figure segment (matches System.Windows.Media.PolyLineSegment).</summary>
public sealed class PolyLineSegment : PathSegment
{
    public PointCollection Points { get; set; } = new();

    internal override Point AppendSvg(StringBuilder target, Point current)
    {
        foreach (var point in Points)
        {
            target.Append(CultureInfo.InvariantCulture, $"L {point.X} {point.Y} ");
            current = point;
        }
        return current;
    }

    internal override Point GetEnd(Point current)
    {
        foreach (var point in Points) current = point;
        return current;
    }

    internal override IEnumerable<Point> GetControls(Point current) => [];
}

/// <summary>Poly-cubic figure segment (matches System.Windows.Media.PolyBezierSegment).</summary>
public sealed class PolyBezierSegment : PathSegment
{
    public PointCollection Points { get; set; } = new();

    internal override Point AppendSvg(StringBuilder target, Point current)
    {
        for (var i = 0; i + 2 < Points.Count; i += 3)
        {
            var p1 = Points[i];
            var p2 = Points[i + 1];
            var p3 = Points[i + 2];
            target.Append(CultureInfo.InvariantCulture,
                $"C {p1.X} {p1.Y} {p2.X} {p2.Y} {p3.X} {p3.Y} ");
            current = p3;
        }
        return current;
    }

    internal override Point GetEnd(Point current)
    {
        for (var i = 0; i + 2 < Points.Count; i += 3) current = Points[i + 2];
        return current;
    }

    internal override IEnumerable<Point> GetControls(Point current)
    {
        for (var i = 0; i + 2 < Points.Count; i += 3)
        {
            yield return Points[i];
            yield return Points[i + 1];
        }
    }
}

/// <summary>Poly-quadratic figure segment (matches System.Windows.Media.PolyQuadraticBezierSegment).</summary>
public sealed class PolyQuadraticBezierSegment : PathSegment
{
    public PointCollection Points { get; set; } = new();

    internal override Point AppendSvg(StringBuilder target, Point current)
    {
        for (var i = 0; i + 1 < Points.Count; i += 2)
        {
            var p1 = Points[i];
            var p2 = Points[i + 1];
            target.Append(CultureInfo.InvariantCulture, $"Q {p1.X} {p1.Y} {p2.X} {p2.Y} ");
            current = p2;
        }
        return current;
    }

    internal override Point GetEnd(Point current)
    {
        for (var i = 0; i + 1 < Points.Count; i += 2) current = Points[i + 1];
        return current;
    }

    internal override IEnumerable<Point> GetControls(Point current)
    {
        for (var i = 0; i + 1 < Points.Count; i += 2) yield return Points[i];
    }
}

/// <summary>
/// Lightweight figure builder (matches System.Windows.Media.StreamGeometry):
/// figures accumulate through <see cref="Open"/> contexts and freeze shut.
/// Backed by a PathGeometry so path data, bounds and paint reuse the exact
/// figure pipeline (no parallel geometry model).
/// </summary>
public sealed class StreamGeometry : Geometry
{
    private readonly PathGeometry _figures = new();

    /// <summary>Fill rule (matches StreamGeometry.FillRule; EvenOdd like WPF).</summary>
    public FillRule FillRule { get; set; } = FillRule.EvenOdd;

    /// <summary>Opens a population context (matches StreamGeometry.Open).</summary>
    public StreamGeometryContext Open()
    {
        ThrowIfFrozen();
        return new StreamGeometryContext(_figures.Figures);
    }

    internal override void AppendSvg(StringBuilder target) => _figures.AppendSvg(target);

    public override Rect GetBounds() => _figures.GetBounds();

    /// <summary>Flattened copy preserving the fill rule (matches GetFlattenedPathGeometry).</summary>
    public override PathGeometry GetFlattenedPathGeometry(double tolerance, ToleranceType toleranceType)
    {
        var flat = base.GetFlattenedPathGeometry(tolerance, toleranceType);
        flat.FillRule = FillRule;
        return flat;
    }

    internal override IEnumerable<PathFigure> FlattenFigures(int subdivisions)
    {
        foreach (var figure in _figures.Figures) yield return FlattenFigure(figure, subdivisions);
    }
}

/// <summary>
/// Stream population context (matches System.Windows.Media.StreamGeometryContext):
/// BeginFigure/LineTo/BezierTo/QuadraticBezierTo/ArcTo plus the Poly* batch
/// forms append retained segments; Close/SetClosedState seal the open figure;
/// Dispose closes (the original Arrow pattern disposes via using).
/// </summary>
public sealed class StreamGeometryContext : IDisposable
{
    private readonly PathFigureCollection _figures;
    private PathFigure? _current;
    private bool _disposed;

    internal StreamGeometryContext(PathFigureCollection figures) => _figures = figures;

    /// <summary>Starts a figure (matches BeginFigure; IsFilled lands on the figure like WPF).</summary>
    public void BeginFigure(Point startPoint, bool isFilled, bool isClosed)
    {
        ThrowIfDisposed();
        _current = new PathFigure { StartPoint = startPoint, IsFilled = isFilled, IsClosed = isClosed };
        _figures.Add(_current);
    }

    /// <summary>Appends a line (matches LineTo).</summary>
    public void LineTo(Point point, bool isStroked, bool isSmoothJoin) =>
        Add(new LineSegment { Point = point, IsStroked = isStroked, IsSmoothJoin = isSmoothJoin });

    /// <summary>Appends a cubic (matches BezierTo).</summary>
    public void BezierTo(Point point1, Point point2, Point point3, bool isStroked, bool isSmoothJoin) =>
        Add(new BezierSegment { Point1 = point1, Point2 = point2, Point3 = point3, IsStroked = isStroked, IsSmoothJoin = isSmoothJoin });

    /// <summary>Appends a quadratic (matches QuadraticBezierTo).</summary>
    public void QuadraticBezierTo(Point point1, Point point2, bool isStroked, bool isSmoothJoin) =>
        Add(new QuadraticBezierSegment { Point1 = point1, Point2 = point2, IsStroked = isStroked, IsSmoothJoin = isSmoothJoin });

    /// <summary>Appends an arc (matches ArcTo).</summary>
    public void ArcTo(Point point, Size size, double rotationAngle, bool isLargeArc, SweepDirection sweepDirection, bool isStroked, bool isSmoothJoin) =>
        Add(new ArcSegment { Point = point, Size = size, RotationAngle = rotationAngle, IsLargeArc = isLargeArc, SweepDirection = sweepDirection, IsStroked = isStroked, IsSmoothJoin = isSmoothJoin });

    /// <summary>Appends a point run (matches PolyLineTo).</summary>
    public void PolyLineTo(IList<Point> points, bool isStroked, bool isSmoothJoin)
    {
        ArgumentNullException.ThrowIfNull(points);
        var segment = new PolyLineSegment { IsStroked = isStroked, IsSmoothJoin = isSmoothJoin };
        foreach (var point in points) segment.Points.Add(point);
        Add(segment);
    }

    /// <summary>Appends cubic triplets (matches PolyBezierTo).</summary>
    public void PolyBezierTo(IList<Point> points, bool isStroked, bool isSmoothJoin)
    {
        ArgumentNullException.ThrowIfNull(points);
        var segment = new PolyBezierSegment { IsStroked = isStroked, IsSmoothJoin = isSmoothJoin };
        foreach (var point in points) segment.Points.Add(point);
        Add(segment);
    }

    /// <summary>Appends quadratic pairs (matches PolyQuadraticBezierTo).</summary>
    public void PolyQuadraticBezierTo(IList<Point> points, bool isStroked, bool isSmoothJoin)
    {
        ArgumentNullException.ThrowIfNull(points);
        var segment = new PolyQuadraticBezierSegment { IsStroked = isStroked, IsSmoothJoin = isSmoothJoin };
        foreach (var point in points) segment.Points.Add(point);
        Add(segment);
    }

    /// <summary>Seals the open figure (matches Close).</summary>
    public void Close()
    {
        ThrowIfDisposed();
        if (_current is null) throw new InvalidOperationException("Close needs an open figure (call BeginFigure first).");
        _current.IsClosed = true;
    }

    /// <summary>Sets the closed state without ending the figure (matches SetClosedState).</summary>
    public void SetClosedState(bool closed)
    {
        ThrowIfDisposed();
        if (_current is null) throw new InvalidOperationException("SetClosedState needs an open figure (call BeginFigure first).");
        _current.IsClosed = closed;
    }

    public void Dispose()
    {
        _disposed = true;
        _current = null;
    }

    private void Add(PathSegment segment)
    {
        ThrowIfDisposed();
        if (_current is null) throw new InvalidOperationException("Segments need an open figure (call BeginFigure first).");
        _current.Segments.Add(segment);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(StreamGeometryContext));
    }
}
