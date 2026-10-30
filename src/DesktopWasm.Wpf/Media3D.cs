using System.Globalization;

namespace DesktopWasm.Wpf;

// Media3D (PresentationCore subset, shim, not port): CPU-side 3D math, model
// data holders and a wireframe Viewport3D. There is no 3D rasterizer, no
// lighting/shading and no texture sampling: meshes render as projected
// triangle edges (DrawLine, fully transport-supported). Materials, lights
// and cameras parse and store their parameters; only the camera rig and the
// model transform chain affect the wireframe.

/// <summary>ARGB color (matches System.Windows.Media.Color member layout).</summary>
public readonly struct Color : IEquatable<Color>
{
    public byte A { get; }
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    public Color(byte a, byte r, byte g, byte b) { A = a; R = r; G = g; B = b; }

    public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
    public static Color FromRgb(byte r, byte g, byte b) => new(0xFF, r, g, b);

    /// <summary>Parses #AARRGGBB or #RRGGBB (the XAML color form).</summary>
    public static Color Parse(string value)
    {
        var text = value.Trim();
        if (text.StartsWith("#", StringComparison.Ordinal))
        {
            var hex = text[1..];
            if ((hex.Length == 8 || hex.Length == 6) &&
                uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                if (hex.Length == 6) return FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                return FromArgb((byte)(rgb >> 24), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            }
        }
        throw new FormatException($"'{value}' is not a #RRGGBB or #AARRGGBB color.");
    }

    public bool Equals(Color other) => A == other.A && R == other.R && G == other.G && B == other.B;
    public override bool Equals(object? obj) => obj is Color other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(A, R, G, B);
    public static bool operator ==(Color left, Color right) => left.Equals(right);
    public static bool operator !=(Color left, Color right) => !left.Equals(right);
    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}

/// <summary>3D point (matches System.Windows.Media.Media3D.Point3D layout).</summary>
public readonly struct Point3D : IEquatable<Point3D>
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }

    /// <summary>Parses "x,y,z" (XAML Positions entries).</summary>
    public static Point3D Parse(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 3 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            return new Point3D(x, y, z);
        throw new FormatException($"'{value}' is not a 3D point (x,y,z).");
    }

    public static Vector3D operator -(Point3D left, Point3D right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static Point3D operator +(Point3D point, Vector3D vector) =>
        new(point.X + vector.X, point.Y + vector.Y, point.Z + vector.Z);

    public static Point3D operator -(Point3D point, Vector3D vector) =>
        new(point.X - vector.X, point.Y - vector.Y, point.Z - vector.Z);

    public bool Equals(Point3D other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is Point3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public static bool operator ==(Point3D left, Point3D right) => left.Equals(right);
    public static bool operator !=(Point3D left, Point3D right) => !left.Equals(right);
    public override string ToString() => $"{X},{Y},{Z}";
}

/// <summary>3D vector with the math the wireframe renderer needs.</summary>
public readonly struct Vector3D : IEquatable<Vector3D>
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public Vector3D(double x, double y, double z) { X = x; Y = y; Z = z; }

    public static Vector3D Parse(string value)
    {
        var point = Point3D.Parse(value);
        return new Vector3D(point.X, point.Y, point.Z);
    }

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public Vector3D Normalized()
    {
        var length = Length;
        if (length == 0) throw new InvalidOperationException("Cannot normalize a zero vector.");
        return new Vector3D(X / length, Y / length, Z / length);
    }

    public static double Dot(Vector3D left, Vector3D right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    public static Vector3D Cross(Vector3D left, Vector3D right) => new(
        left.Y * right.Z - left.Z * right.Y,
        left.Z * right.X - left.X * right.Z,
        left.X * right.Y - left.Y * right.X);

    public static Vector3D operator +(Vector3D left, Vector3D right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static Vector3D operator -(Vector3D left, Vector3D right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static Vector3D operator *(Vector3D vector, double scalar) =>
        new(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);

    public bool Equals(Vector3D other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is Vector3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public static bool operator ==(Vector3D left, Vector3D right) => left.Equals(right);
    public static bool operator !=(Vector3D left, Vector3D right) => !left.Equals(right);
    public override string ToString() => $"{X},{Y},{Z}";
}

/// <summary>Ordered 3D points (matches Point3DCollection; parses XAML lists).</summary>
public sealed class Point3DCollection : List<Point3D>
{
    public Point3DCollection()
    {
    }

    /// <summary>
    /// Copies points (matches Point3DCollection(IEnumerable&lt;Point3D&gt;);
    /// the converted MaterialDesign Plane3D wraps its mesh points this way).
    /// </summary>
    public Point3DCollection(IEnumerable<Point3D> points) : base(points)
    {
    }

    /// <summary>Parses "x,y,z x,y,z ..." (spaces and commas both separate).</summary>
    public static Point3DCollection Parse(string value)
    {
        var numbers = value.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (numbers.Length == 0 || numbers.Length % 3 != 0)
            throw new FormatException($"'{value}' is not a 3D point list (x,y,z triples).");
        var result = new Point3DCollection();
        for (var i = 0; i < numbers.Length; i += 3)
        {
            if (!double.TryParse(numbers[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(numbers[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(numbers[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                throw new FormatException($"'{value}' is not a 3D point list (x,y,z triples).");
            result.Add(new Point3D(x, y, z));
        }
        return result;
    }
}

/// <summary>Ordered 3D vectors (matches Vector3DCollection; parses XAML lists).</summary>
public sealed class Vector3DCollection : List<Vector3D>
{
    /// <summary>Parses "x,y,z x,y,z ..." (spaces and commas both separate).</summary>
    public static Vector3DCollection Parse(string value)
    {
        var numbers = value.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (numbers.Length == 0 || numbers.Length % 3 != 0)
            throw new FormatException($"'{value}' is not a 3D vector list (x,y,z triples).");
        var result = new Vector3DCollection();
        for (var i = 0; i < numbers.Length; i += 3)
        {
            if (!double.TryParse(numbers[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(numbers[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(numbers[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                throw new FormatException($"'{value}' is not a 3D vector list (x,y,z triples).");
            result.Add(new Vector3D(x, y, z));
        }
        return result;
    }
}

/// <summary>Integer list (TriangleIndices; parses "0,1,2 3,4,5" groups).</summary>
public sealed class Int32Collection : List<int>
{
    public Int32Collection()
    {
    }

    /// <summary>
    /// Copies values (matches Int32Collection(IEnumerable&lt;int&gt;); the
    /// converted MaterialDesign Plane3D wraps its triangle indices this way).
    /// </summary>
    public Int32Collection(IEnumerable<int> values) : base(values)
    {
    }

    public static Int32Collection Parse(string value)
    {
        var numbers = value.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var result = new Int32Collection();
        foreach (var token in numbers)
        {
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                throw new FormatException($"'{value}' is not an integer list.");
            result.Add(number);
        }
        return result;
    }
}

/// <summary>2D point list (matches System.Windows.Media.PointCollection; TextureCoordinates and poly segments share it; parses "x,y ..." pairs).</summary>
public sealed class PointCollection : List<Point>
{
    public PointCollection()
    {
    }

    /// <summary>
    /// Capacity hint (matches PointCollection(int); the converted OxyPlot
    /// XamlRenderContext sizes its balanced polylines up front).
    /// </summary>
    public PointCollection(int capacity) : base(capacity)
    {
    }

    /// <summary>
    /// Copies points (matches PointCollection(IEnumerable&lt;Point&gt;); the
    /// converted OxyPlot XamlRenderContext wraps snapped points this way).
    /// </summary>
    public PointCollection(IEnumerable<Point> points) : base(points)
    {
    }

    public static PointCollection Parse(string value)
    {
        var numbers = value.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (numbers.Length == 0 || numbers.Length % 2 != 0)
            throw new FormatException($"'{value}' is not a 2D point list (u,v pairs).");
        var result = new PointCollection();
        for (var i = 0; i < numbers.Length; i += 2)
        {
            if (!double.TryParse(numbers[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(numbers[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                throw new FormatException($"'{value}' is not a 2D point list (u,v pairs).");
            result.Add(new Point(x, y));
        }
        return result;
    }

    /// <summary>Value enumerator (matches PointCollection.Enumerator; wraps the list enumerator).</summary>
    public new Enumerator GetEnumerator() => new(this);

    /// <summary>Point enumerator (matches System.Windows.Media.PointCollection/Enumerator).</summary>
    public struct Enumerator : IEnumerator<Point>
    {
        private readonly List<Point> _list;
        private int _index;

        internal Enumerator(List<Point> list)
        {
            _list = list;
            _index = -1;
        }

        public Point Current => (uint)_index < (uint)_list.Count
            ? _list[_index]
            : throw new InvalidOperationException("Enumeration has not started or has already finished.");

        object System.Collections.IEnumerator.Current => Current;
        public bool MoveNext() => ++_index < _list.Count;
        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}

/// <summary>3D transform base (matches System.Windows.Media.Media3D.Transform3D : Animatable).</summary>
public abstract class Transform3D : Animatable
{
    public abstract Point3D Transform(Point3D point);
}

/// <summary>3D translation (matches System.Windows.Media.Media3D.TranslateTransform3D).</summary>
public class TranslateTransform3D : Transform3D
{
    /// <summary>Identifies the OffsetX store (matches TranslateTransform3D.OffsetXProperty).</summary>
    public static readonly DependencyProperty OffsetXProperty =
        DependencyProperty.Register("OffsetX", typeof(double), typeof(TranslateTransform3D), 0.0);

    /// <summary>Identifies the OffsetY store (matches TranslateTransform3D.OffsetYProperty).</summary>
    public static readonly DependencyProperty OffsetYProperty =
        DependencyProperty.Register("OffsetY", typeof(double), typeof(TranslateTransform3D), 0.0);

    /// <summary>Identifies the OffsetZ store (matches TranslateTransform3D.OffsetZProperty).</summary>
    public static readonly DependencyProperty OffsetZProperty =
        DependencyProperty.Register("OffsetZ", typeof(double), typeof(TranslateTransform3D), 0.0);

    /// <summary>Empty translation.</summary>
    public TranslateTransform3D()
    {
    }

    /// <summary>Translation by offsets (matches TranslateTransform3D(double, double, double)).</summary>
    public TranslateTransform3D(double offsetX, double offsetY, double offsetZ)
    {
        OffsetX = offsetX;
        OffsetY = offsetY;
        OffsetZ = offsetZ;
    }

    /// <summary>X offset.</summary>
    public double OffsetX
    {
        get => (double)GetValue(OffsetXProperty)!;
        set => SetValue(OffsetXProperty, value);
    }

    /// <summary>Y offset.</summary>
    public double OffsetY
    {
        get => (double)GetValue(OffsetYProperty)!;
        set => SetValue(OffsetYProperty, value);
    }

    /// <summary>Z offset.</summary>
    public double OffsetZ
    {
        get => (double)GetValue(OffsetZProperty)!;
        set => SetValue(OffsetZProperty, value);
    }

    public override Point3D Transform(Point3D point) =>
        new(point.X + OffsetX, point.Y + OffsetY, point.Z + OffsetZ);
}

/// <summary>3D scale about a center (matches System.Windows.Media.Media3D.ScaleTransform3D).</summary>
public class ScaleTransform3D : Transform3D
{
    /// <summary>Identifies the ScaleX store (matches ScaleTransform3D.ScaleXProperty).</summary>
    public static readonly DependencyProperty ScaleXProperty =
        DependencyProperty.Register("ScaleX", typeof(double), typeof(ScaleTransform3D), 1.0);

    /// <summary>Identifies the ScaleY store (matches ScaleTransform3D.ScaleYProperty).</summary>
    public static readonly DependencyProperty ScaleYProperty =
        DependencyProperty.Register("ScaleY", typeof(double), typeof(ScaleTransform3D), 1.0);

    /// <summary>Identifies the ScaleZ store (matches ScaleTransform3D.ScaleZProperty).</summary>
    public static readonly DependencyProperty ScaleZProperty =
        DependencyProperty.Register("ScaleZ", typeof(double), typeof(ScaleTransform3D), 1.0);

    /// <summary>Identifies the CenterX store (matches ScaleTransform3D.CenterXProperty).</summary>
    public static readonly DependencyProperty CenterXProperty =
        DependencyProperty.Register("CenterX", typeof(double), typeof(ScaleTransform3D), 0.0);

    /// <summary>Identifies the CenterY store (matches ScaleTransform3D.CenterYProperty).</summary>
    public static readonly DependencyProperty CenterYProperty =
        DependencyProperty.Register("CenterY", typeof(double), typeof(ScaleTransform3D), 0.0);

    /// <summary>Identifies the CenterZ store (matches ScaleTransform3D.CenterZProperty).</summary>
    public static readonly DependencyProperty CenterZProperty =
        DependencyProperty.Register("CenterZ", typeof(double), typeof(ScaleTransform3D), 0.0);

    /// <summary>X scale factor (default 1).</summary>
    public double ScaleX
    {
        get => (double)GetValue(ScaleXProperty)!;
        set => SetValue(ScaleXProperty, value);
    }

    /// <summary>Y scale factor (default 1).</summary>
    public double ScaleY
    {
        get => (double)GetValue(ScaleYProperty)!;
        set => SetValue(ScaleYProperty, value);
    }

    /// <summary>Z scale factor (default 1).</summary>
    public double ScaleZ
    {
        get => (double)GetValue(ScaleZProperty)!;
        set => SetValue(ScaleZProperty, value);
    }

    /// <summary>X center.</summary>
    public double CenterX
    {
        get => (double)GetValue(CenterXProperty)!;
        set => SetValue(CenterXProperty, value);
    }

    /// <summary>Y center.</summary>
    public double CenterY
    {
        get => (double)GetValue(CenterYProperty)!;
        set => SetValue(CenterYProperty, value);
    }

    /// <summary>Z center.</summary>
    public double CenterZ
    {
        get => (double)GetValue(CenterZProperty)!;
        set => SetValue(CenterZProperty, value);
    }

    public override Point3D Transform(Point3D point) => new(
        CenterX + ((point.X - CenterX) * ScaleX),
        CenterY + ((point.Y - CenterY) * ScaleY),
        CenterZ + ((point.Z - CenterZ) * ScaleZ));
}

/// <summary>Rotation base (matches Rotation3D : Animatable).</summary>
public abstract class Rotation3D : Animatable
{
}

/// <summary>Axis-angle rotation (Rodrigues form; matches AxisAngleRotation3D).</summary>
public class AxisAngleRotation3D : Rotation3D
{
    /// <summary>Identifies the Axis store (matches AxisAngleRotation3D.AxisProperty).</summary>
    public static readonly DependencyProperty AxisProperty =
        DependencyProperty.Register("Axis", typeof(Vector3D), typeof(AxisAngleRotation3D), new Vector3D(0, 1, 0));

    /// <summary>Identifies the Angle store (matches AxisAngleRotation3D.AngleProperty).</summary>
    public static readonly DependencyProperty AngleProperty =
        DependencyProperty.Register("Angle", typeof(double), typeof(AxisAngleRotation3D), 0.0);

    /// <summary>Rotation axis (normalized on use).</summary>
    public Vector3D Axis
    {
        get => (Vector3D)GetValue(AxisProperty)!;
        set => SetValue(AxisProperty, value);
    }

    /// <summary>Rotation angle in degrees.</summary>
    public double Angle
    {
        get => (double)GetValue(AngleProperty)!;
        set => SetValue(AngleProperty, value);
    }

    internal Point3D RotateAbout(Point3D center, Point3D point)
    {
        var stored = Axis;
        var axis = stored.Length == 0 ? new Vector3D(0, 1, 0) : stored.Normalized();
        var radians = Angle * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var arm = point - center;
        var parallel = axis * Vector3D.Dot(axis, new Vector3D(arm.X, arm.Y, arm.Z));
        var perpendicular = new Vector3D(arm.X, arm.Y, arm.Z) - parallel;
        var tangent = Vector3D.Cross(axis, new Vector3D(arm.X, arm.Y, arm.Z));
        var rotated = parallel + perpendicular * cos + tangent * sin;
        return new Point3D(center.X, center.Y, center.Z) + rotated;
    }
}

/// <summary>Rotation about a center (matches RotateTransform3D).</summary>
public class RotateTransform3D : Transform3D
{
    /// <summary>Identifies the Rotation store (matches RotateTransform3D.RotationProperty).</summary>
    public static readonly DependencyProperty RotationProperty =
        DependencyProperty.Register("Rotation", typeof(Rotation3D), typeof(RotateTransform3D), (object?)null);

    /// <summary>Identifies the CenterX store (matches RotateTransform3D.CenterXProperty).</summary>
    public static readonly DependencyProperty CenterXProperty =
        DependencyProperty.Register("CenterX", typeof(double), typeof(RotateTransform3D), 0.0);

    /// <summary>Identifies the CenterY store (matches RotateTransform3D.CenterYProperty).</summary>
    public static readonly DependencyProperty CenterYProperty =
        DependencyProperty.Register("CenterY", typeof(double), typeof(RotateTransform3D), 0.0);

    /// <summary>Identifies the CenterZ store (matches RotateTransform3D.CenterZProperty).</summary>
    public static readonly DependencyProperty CenterZProperty =
        DependencyProperty.Register("CenterZ", typeof(double), typeof(RotateTransform3D), 0.0);

    /// <summary>Empty rotation.</summary>
    public RotateTransform3D()
    {
    }

    /// <summary>Rotation about the origin (matches RotateTransform3D(Rotation3D)).</summary>
    public RotateTransform3D(Rotation3D rotation) => Rotation = rotation;

    /// <summary>Rotation applied about the center.</summary>
    public Rotation3D? Rotation
    {
        get => (Rotation3D?)GetValue(RotationProperty);
        set => SetValue(RotationProperty, value);
    }

    /// <summary>X center.</summary>
    public double CenterX
    {
        get => (double)GetValue(CenterXProperty)!;
        set => SetValue(CenterXProperty, value);
    }

    /// <summary>Y center.</summary>
    public double CenterY
    {
        get => (double)GetValue(CenterYProperty)!;
        set => SetValue(CenterYProperty, value);
    }

    /// <summary>Z center.</summary>
    public double CenterZ
    {
        get => (double)GetValue(CenterZProperty)!;
        set => SetValue(CenterZProperty, value);
    }

    public override Point3D Transform(Point3D point)
    {
        if (Rotation is not AxisAngleRotation3D axis)
            throw new InvalidOperationException(
                $"RotateTransform3D only runs AxisAngleRotation3D in v1, not {Rotation?.GetType().Name ?? "null"}.");
        return axis.RotateAbout(new Point3D(CenterX, CenterY, CenterZ), point);
    }
}

/// <summary>Ordered 3D transform list (matches Transform3DGroup).</summary>
public sealed class Transform3DGroup : Transform3D
{
    public Transform3DCollection Children { get; } = new();

    public override Point3D Transform(Point3D point)
    {
        var current = point;
        foreach (var child in Children) current = child.Transform(current);
        return current;
    }
}

/// <summary>Linear 3D-point interpolation (matches System.Windows.Media.Animation.Point3DAnimation).</summary>
public class Point3DAnimation : AnimationTimeline
{
    /// <summary>Start point (defaults to the target's current value at Begin).</summary>
    public Point3D? From { get; set; }

    /// <summary>End point.</summary>
    public Point3D? To { get; set; }

    /// <summary>Relative change (used when To is absent).</summary>
    public Point3D? By { get; set; }

    /// <summary>Easing hook (linear while null).</summary>
    public IEasingFunction? EasingFunction { get; set; }

    /// <summary>Empty animation.</summary>
    public Point3DAnimation()
    {
    }

    /// <summary>To-value over a duration (matches the HandyControl CoverFlow shape).</summary>
    public Point3DAnimation(Point3D toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    /// <summary>To-value over a duration with a fill rule (matches Point3DAnimation(Point3D, Duration, FillBehavior)).</summary>
    public Point3DAnimation(Point3D toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    /// <inheritdoc />
    public override Type TargetPropertyType => typeof(Point3D);

    internal Point3D Evaluate(Point3D from, double progress)
    {
        var start = From ?? from;
        var end = To ?? (By.HasValue ? start + new Vector3D(By.Value.X, By.Value.Y, By.Value.Z) : start);
        var eased = EasingFunction?.Ease(progress) ?? progress;
        return new Point3D(
            start.X + ((end.X - start.X) * eased),
            start.Y + ((end.Y - start.Y) * eased),
            start.Z + ((end.Z - start.Z) * eased));
    }

    /// <summary>Typed sample at the clock position.</summary>
    public Point3D GetCurrentValue(Point3D defaultOriginValue, Point3D defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        return Evaluate(From ?? defaultOriginValue, ApplyTimingCurve(animationClock.CurrentProgress ?? 1.0));
    }

    /// <inheritdoc />
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock) =>
        GetCurrentValue(ToPoint3D(defaultOriginValue), ToPoint3D(defaultDestinationValue), animationClock);

    private static Point3D ToPoint3D(object? value) => value switch
    {
        Point3D point => point,
        Vector3D vector => new Point3D(vector.X, vector.Y, vector.Z),
        _ => new Point3D(0, 0, 0),
    };
}

/// <summary>3D geometry base (matches Geometry3D).</summary>
public abstract class Geometry3D
{
}

/// <summary>Triangle mesh (matches MeshGeometry3D; texture/normals stored, unused by the wireframe).</summary>
public class MeshGeometry3D : Geometry3D
{
    public Point3DCollection Positions { get; set; } = new();
    public Int32Collection TriangleIndices { get; set; } = new();
    public PointCollection TextureCoordinates { get; set; } = new();
}

/// <summary>Material base (matches Material : Animatable; stored, lighting is out of scope).</summary>
public abstract class Material : Animatable
{
}

/// <summary>Diffuse material (matches DiffuseMaterial; Brush is a 2D brush, ignored by the wireframe renderer).</summary>
public class DiffuseMaterial : Material
{
    /// <summary>Empty material.</summary>
    public DiffuseMaterial()
    {
    }

    /// <summary>Material over a brush (matches DiffuseMaterial(Brush)).</summary>
    public DiffuseMaterial(Brush brush) => Brush = brush;

    public Brush? Brush { get; set; }
    public Color Color { get; set; } = Color.FromRgb(0xFF, 0xFF, 0xFF);
}

/// <summary>3D model base (matches Model3D).</summary>
public abstract class Model3D
{
    public Transform3D? Transform { get; set; }
}

/// <summary>Mesh model (matches GeometryModel3D).</summary>
public class GeometryModel3D : Model3D
{
    /// <summary>Empty model.</summary>
    public GeometryModel3D()
    {
    }

    /// <summary>Model over geometry and material (matches GeometryModel3D(Geometry3D, Material)).</summary>
    public GeometryModel3D(Geometry3D? geometry, Material? material)
    {
        Geometry = geometry;
        Material = material;
    }

    public Geometry3D? Geometry { get; set; }
    public Material? Material { get; set; }
    public Material? BackMaterial { get; set; }
}

/// <summary>Light base (matches Light; stored, the wireframe renderer is unlit).</summary>
public abstract class Light : Model3D
{
    public Color Color { get; set; } = Color.FromRgb(0xFF, 0xFF, 0xFF);
}

/// <summary>Ambient light (matches AmbientLight).</summary>
public class AmbientLight : Light
{
}

/// <summary>Directional light (matches DirectionalLight).</summary>
public class DirectionalLight : Light
{
    public DirectionalLight()
    {
    }

    /// <summary>Light with color and direction (matches
    /// DirectionalLight(Color, Vector3D); the converted MaterialDesign
    /// Plane3D builds its key light this way).</summary>
    public DirectionalLight(Color color, Vector3D direction)
    {
        Color = color;
        Direction = direction;
    }

    public Vector3D Direction { get; set; } = new(0, 0, -1);
}

/// <summary>3D visual base (matches Visual3D : DependencyObject).</summary>
public abstract class Visual3D : DependencyObject
{
    public Transform3D? Transform { get; set; }
}

/// <summary>Interactive 3D visual base (matches System.Windows.Media.Media3D.UIElement3D : Visual3D).</summary>
public abstract class UIElement3D : Visual3D
{
}

/// <summary>Element-hosting 3D container (matches System.Windows.Media.Media3D.ContainerUIElement3D).</summary>
public class ContainerUIElement3D : UIElement3D
{
    /// <summary>Hosted 3D children (matches ContainerUIElement3D.Children).</summary>
    public Visual3DCollection Children { get; } = new();
}

/// <summary>3D visual list (matches Visual3DCollection).</summary>
public sealed class Visual3DCollection : List<Visual3D>
{
}

/// <summary>Content/children 3D visual (matches ModelVisual3D).</summary>
public class ModelVisual3D : Visual3D
{
    public Model3D? Content { get; set; }
    public Visual3DCollection Children { get; } = new();
}

/// <summary>Camera base (matches System.Windows.Media.Media3D.Camera : Animatable).</summary>
public abstract class Camera : Animatable
{
}

/// <summary>Positioned camera base (matches System.Windows.Media.Media3D.ProjectionCamera).</summary>
public abstract class ProjectionCamera : Camera
{
    /// <summary>Identifies the Position store (matches ProjectionCamera.PositionProperty).</summary>
    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register("Position", typeof(Point3D), typeof(ProjectionCamera), new Point3D(0, 0, 0));

    /// <summary>Identifies the LookDirection store (matches ProjectionCamera.LookDirectionProperty).</summary>
    public static readonly DependencyProperty LookDirectionProperty =
        DependencyProperty.Register("LookDirection", typeof(Vector3D), typeof(ProjectionCamera), new Vector3D(0, 0, -1));

    /// <summary>Identifies the UpDirection store (matches ProjectionCamera.UpDirectionProperty).</summary>
    public static readonly DependencyProperty UpDirectionProperty =
        DependencyProperty.Register("UpDirection", typeof(Vector3D), typeof(ProjectionCamera), new Vector3D(0, 1, 0));

    /// <summary>Camera position.</summary>
    public Point3D Position
    {
        get => (Point3D)GetValue(PositionProperty)!;
        set => SetValue(PositionProperty, value);
    }

    /// <summary>View direction.</summary>
    public Vector3D LookDirection
    {
        get => (Vector3D)GetValue(LookDirectionProperty)!;
        set => SetValue(LookDirectionProperty, value);
    }

    /// <summary>Up direction.</summary>
    public Vector3D UpDirection
    {
        get => (Vector3D)GetValue(UpDirectionProperty)!;
        set => SetValue(UpDirectionProperty, value);
    }
}

/// <summary>Perspective projection parameters (matches PerspectiveCamera).</summary>
public class PerspectiveCamera : ProjectionCamera
{
    static PerspectiveCamera()
    {
        // Engine default view: the wireframe rig documents (0,0,5) as its
        // default camera position (real WPF defaults to the origin, which
        // would leave the default scene behind the near plane).
        PositionProperty.OverrideMetadata(
            typeof(PerspectiveCamera),
            new FrameworkPropertyMetadata(new Point3D(0, 0, 5)));
    }

    public double FieldOfView { get; set; } = 60.0;
    public double NearPlaneDistance { get; set; } = 0.1;
    public double FarPlaneDistance { get; set; } = double.PositiveInfinity;
}

/// <summary>
/// Viewport3D (shim, not port): hosts a 3D scene and renders a wireframe
/// projection (triangle edges as DrawLine). No rasterization, lighting,
/// textures, culling or hit-testing: a scene-graph host with a recognizable
/// motion preview, which is what converted sample viewers need to boot and
/// animate. Materials, lights and brushes parse and store; only the camera
/// rig and the model transform chain affect the projection.
/// </summary>
public class Viewport3D : FrameworkElement
{
    public Camera? Camera { get; set; }

    // Hides FrameworkElement.Children (UIElement list): a viewport's
    // children are Visual3D nodes, never UIElements.
    public new Visual3DCollection Children { get; } = new();

    /// <summary>Line ink (defaults to dark gray like a technical preview).</summary>
    public uint StrokeRgba { get; set; } = 0x404040FF;

    /// <summary>Fallback rig when no camera is attached (documented default view).</summary>
    public static PerspectiveCamera DefaultCamera { get; } = new();

    protected override WpfSize MeasureCore(WpfSize available) =>
        new(double.IsNaN(Width) ? available.Width : Width,
            double.IsNaN(Height) ? available.Height : Height);

    protected override void ArrangeCore(WpfRect finalRect) { }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = RenderRect;
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var camera = Camera as PerspectiveCamera ?? DefaultCamera;
        var projection = new WireProjection(camera, rect);
        foreach (var child in Children) RenderVisual(child, Transform3DIdentity.Instance, projection, dc);
    }

    private void RenderVisual(Visual3D visual, Transform3D accumulated, WireProjection projection, DrawingContext dc)
    {
        var local = visual.Transform is null ? accumulated : new ChainedTransform(accumulated, visual.Transform);
        if (visual is ModelVisual3D model)
        {
            RenderModel(model.Content, model.Transform is null ? local : new ChainedTransform(local, model.Transform), projection, dc);
            foreach (var child in model.Children) RenderVisual(child, local, projection, dc);
        }
    }

    private void RenderModel(Model3D? model, Transform3D accumulated, WireProjection projection, DrawingContext dc)
    {
        var local = model?.Transform is null ? accumulated : new ChainedTransform(accumulated, model.Transform!);
        if (model is GeometryModel3D geometry && geometry.Geometry is MeshGeometry3D mesh)
            RenderMesh(mesh, local, projection, dc);
    }

    private void RenderMesh(MeshGeometry3D mesh, Transform3D transform, WireProjection projection, DrawingContext dc)
    {
        var positions = mesh.Positions;
        var indices = mesh.TriangleIndices;
        if (positions.Count == 0 || indices.Count < 3) return;
        // Project once per vertex, then stroke indexed triangle edges.
        var projected = new WpfPoint?[positions.Count];
        for (var i = 0; i < positions.Count; i++)
            projected[i] = projection.Project(transform.Transform(positions[i]));
        var triples = indices.Count - (indices.Count % 3);
        for (var i = 0; i < triples; i += 3)
        {
            var a = indices[i] < projected.Length ? projected[indices[i]] : null;
            var b = indices[i + 1] < projected.Length ? projected[indices[i + 1]] : null;
            var c = indices[i + 2] < projected.Length ? projected[indices[i + 2]] : null;
            if (a is null || b is null || c is null) continue;
            projection.Stroke(dc, a.Value, b.Value, StrokeRgba);
            projection.Stroke(dc, b.Value, c.Value, StrokeRgba);
            projection.Stroke(dc, c.Value, a.Value, StrokeRgba);
        }
    }

    /// <summary>
    /// 3D ray-cast hit test for a viewport pixel (absolute coordinates, like
    /// the retained-tree point pick): unprojects the pixel into a world-space
    /// ray through the camera rig, runs a double-sided Moller-Trumbore test
    /// over every world-space mesh triangle under accumulated model/visual
    /// transforms, and returns the nearest hit. Misses stay null, like WPF
    /// (a Viewport3D with no mesh hit reports no result, not a 2D fallback).
    /// MeshHit is the world-space intersection (transforms applied): the
    /// engine's forward-only transforms do not carry the inverses WPF uses
    /// for mesh-local reporting.
    /// </summary>
    internal RayMeshGeometry3DHitTestResult? RayCast(WpfPoint pixel)
    {
        var rect = RenderRect;
        if (rect.Width <= 0 || rect.Height <= 0) return null;
        var camera = Camera as PerspectiveCamera ?? DefaultCamera;
        var projection = new WireProjection(camera, rect);
        var eye = projection.Origin;
        var direction = projection.RayDirection(pixel);
        if (direction.Length == 0) return null;
        var origin = new Vector3D(eye.X, eye.Y, eye.Z);
        RayMeshGeometry3DHitTestResult? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var child in Children)
            CastVisual(child, Transform3DIdentity.Instance, origin, direction, ref best, ref bestDistance);
        return best;
    }

    private static void CastVisual(
        Visual3D visual, Transform3D accumulated, Vector3D origin, Vector3D direction,
        ref RayMeshGeometry3DHitTestResult? best, ref double bestDistance)
    {
        var local = visual.Transform is null ? accumulated : new ChainedTransform(accumulated, visual.Transform);
        if (visual is ModelVisual3D model)
        {
            if (model.Content is GeometryModel3D geometry && geometry.Geometry is MeshGeometry3D mesh)
            {
                var meshLocal = geometry.Transform is null
                    ? local
                    : new ChainedTransform(local, geometry.Transform);
                CastMesh(model, mesh, meshLocal, origin, direction, ref best, ref bestDistance);
            }
            foreach (var child in model.Children)
                CastVisual(child, local, origin, direction, ref best, ref bestDistance);
            return;
        }
        if (visual is ContainerUIElement3D container)
        {
            foreach (var child in container.Children)
                CastVisual(child, local, origin, direction, ref best, ref bestDistance);
        }
    }

    private static void CastMesh(
        ModelVisual3D owner, MeshGeometry3D mesh, Transform3D transform,
        Vector3D origin, Vector3D direction,
        ref RayMeshGeometry3DHitTestResult? best, ref double bestDistance)
    {
        var positions = mesh.Positions;
        var indices = mesh.TriangleIndices;
        if (positions.Count == 0 || indices.Count < 3) return;
        var world = new Point3D[positions.Count];
        for (var i = 0; i < positions.Count; i++)
        {
            var moved = transform.Transform(positions[i]);
            // Poisoned chains project to nothing (same rule as the renderer).
            if (!double.IsFinite(moved.X + moved.Y + moved.Z)) return;
            world[i] = moved;
        }
        var triples = indices.Count - (indices.Count % 3);
        for (var i = 0; i < triples; i += 3)
        {
            var a = indices[i];
            var b = indices[i + 1];
            var c = indices[i + 2];
            if ((uint)a >= (uint)world.Length || (uint)b >= (uint)world.Length || (uint)c >= (uint)world.Length)
                continue;
            if (RayHitsTriangle(origin, direction, world[a], world[b], world[c],
                    out var distance, out var u, out var v) &&
                distance > 0 && distance < bestDistance)
            {
                bestDistance = distance;
                var w1 = 1.0 - u - v;
                best = new RayMeshGeometry3DHitTestResult(
                    owner,
                    new Point3D(
                        origin.X + (direction.X * distance),
                        origin.Y + (direction.Y * distance),
                        origin.Z + (direction.Z * distance)),
                    w1, u, v);
            }
        }
    }

    private static bool RayHitsTriangle(
        Vector3D origin, Vector3D direction, Point3D a, Point3D b, Point3D c,
        out double distance, out double u, out double v)
    {
        distance = 0;
        u = 0;
        v = 0;
        var edge1 = b - a;
        var edge2 = c - a;
        var pvec = Vector3D.Cross(direction, edge2);
        var det = Vector3D.Dot(edge1, pvec);
        if (Math.Abs(det) < 1e-9) return false;
        var invDet = 1.0 / det;
        var tvec = origin - new Vector3D(a.X, a.Y, a.Z);
        u = Vector3D.Dot(tvec, pvec) * invDet;
        if (u < 0.0 || u > 1.0) return false;
        var qvec = Vector3D.Cross(tvec, edge1);
        v = Vector3D.Dot(direction, qvec) * invDet;
        if (v < 0.0 || u + v > 1.0) return false;
        var t = Vector3D.Dot(edge2, qvec) * invDet;
        if (t <= 1e-9) return false;
        distance = t;
        return true;
    }

    private sealed class Transform3DIdentity : Transform3D
    {
        public static readonly Transform3DIdentity Instance = new();
        public override Point3D Transform(Point3D point) => point;
    }

    private sealed class ChainedTransform(Transform3D First, Transform3D Second) : Transform3D
    {
        public override Point3D Transform(Point3D point) => Second.Transform(First.Transform(point));
    }
}

/// <summary>
/// Camera projection for the wireframe renderer: view basis from the camera
/// rig, perspective divide from FieldOfView, uniform fit into the viewport.
/// Points on/behind the near plane project to null (triangle skipped).
/// </summary>
internal sealed class WireProjection
{
    private readonly Vector3D _right;
    private readonly Vector3D _up;
    private readonly Vector3D _forward;
    private readonly Point3D _eye;
    private readonly double _focal;
    private readonly double _near;
    private readonly WpfRect _viewport;
    private readonly double _fit;

    public WireProjection(PerspectiveCamera camera, WpfRect viewport)
    {
        _eye = camera.Position;
        _forward = camera.LookDirection.Length == 0
            ? new Vector3D(0, 0, -1)
            : camera.LookDirection.Normalized();
        var up = camera.UpDirection.Length == 0 ? new Vector3D(0, 1, 0) : camera.UpDirection.Normalized();
        _right = Vector3D.Cross(_forward, up);
        if (_right.Length == 0) _right = new Vector3D(1, 0, 0);
        _right = _right.Normalized();
        _up = Vector3D.Cross(_right, _forward).Normalized();
        _focal = 1.0 / Math.Tan(Math.Clamp(camera.FieldOfView, 1, 179) * Math.PI / 360.0);
        _near = camera.NearPlaneDistance;
        _viewport = viewport;
        // Fit the [-1,1] frustum square uniformly into the viewport.
        _fit = Math.Min(viewport.Width, viewport.Height) / 2.0;
    }

    /// <summary>Camera eye (ray origin for hit-testing).</summary>
    internal Point3D Origin => _eye;

    /// <summary>World-space ray direction through a viewport pixel.</summary>
    internal Vector3D RayDirection(WpfPoint pixel)
    {
        if (_fit <= 0 || !double.IsFinite(_focal))
            return _forward;
        var dx = (pixel.X - (_viewport.X + _viewport.Width / 2.0)) / _fit / _focal;
        var dy = ((_viewport.Y + _viewport.Height / 2.0) - pixel.Y) / _fit / _focal;
        if (!double.IsFinite(dx + dy))
            return _forward;
        var direction = _forward + (_right * dx) + (_up * dy);
        return direction.Length == 0 ? _forward : direction.Normalized();
    }

    /// <summary>Projects to viewport pixels, or null when behind the near plane.</summary>
    public WpfPoint? Project(Point3D point)
    {
        // Converted samples compute positions from ActualWidth before layout
        // (zero-size divide): non-finite inputs project to nothing instead of
        // poisoning the wireframe with NaN segments.
        if (!double.IsFinite(point.X + point.Y + point.Z)) return null;
        var offset = point - _eye;
        var view = new Vector3D(offset.X, offset.Y, offset.Z);
        var depth = Vector3D.Dot(view, _forward);
        if (depth <= _near) return null;
        var x = Vector3D.Dot(view, _right) * _focal / depth;
        var y = Vector3D.Dot(view, _up) * _focal / depth;
        return new WpfPoint(
            _viewport.X + _viewport.Width / 2.0 + x * _fit,
            _viewport.Y + _viewport.Height / 2.0 - y * _fit);
    }

    public void Stroke(DrawingContext dc, WpfPoint from, WpfPoint to, uint rgba) =>
        dc.DrawLine((float)from.X, (float)from.Y, (float)to.X, (float)to.Y, 1, rgba);
}
