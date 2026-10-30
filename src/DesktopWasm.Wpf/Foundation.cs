using System.Globalization;

namespace DesktopWasm.Wpf;

// Foundational value types (WindowsBase subset, shim, not port): Point,
// Vector, Size, Rect and Int32Rect with the WPF member surface converted
// apps actually call (ctors, operators, Parse, Empty, hit-test helpers).
// Layout/render internals keep the engine WpfPoint/WpfSize/WpfRect records;
// implicit conversions bridge converted call sites. Number formatting is
// invariant (deterministic for transport), unlike desktop current-culture
// formatting; unknown members stay precise runtime gaps, not silent zeros.

/// <summary>Splits native converter text (live-verified against the 10.0
/// converters: comma-separated strict with no empty components, else
/// whitespace runs; trailing commas fail).</summary>
internal static class ConverterText
{
    internal static string[] Split(string value)
    {
        var text = value.Trim();
        return text.Contains(',')
            ? text.Split(',', StringSplitOptions.TrimEntries)
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }
}

/// <summary>2D point (matches System.Windows.Point member layout).</summary>
public struct Point : IEquatable<Point>, IFormattable
{
    public double X { get; set; }
    public double Y { get; set; }

    public Point(double x, double y) { X = x; Y = y; }

    public void Offset(double offsetX, double offsetY)
    {
        X += offsetX;
        Y += offsetY;
    }

    /// <summary>Parses "x,y" or "x y" (invariant; matches PointConverter form).</summary>
    public static Point Parse(string value)
    {
        var parts = ConverterText.Split(value);
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return new Point(x, y);
        throw new FormatException($"'{value}' is not a point (x,y).");
    }

    public static Point Add(Point point, Vector vector) => point + vector;
    public static Point Subtract(Point point, Vector vector) => point - vector;
    public static Vector Subtract(Point point1, Point point2) => point1 - point2;

    public static Point operator +(Point point, Vector vector) =>
        new(point.X + vector.X, point.Y + vector.Y);

    public static Point operator -(Point point, Vector vector) =>
        new(point.X - vector.X, point.Y - vector.Y);

    public static Vector operator -(Point point1, Point point2) =>
        new(point1.X - point2.X, point1.Y - point2.Y);

    public static bool operator ==(Point left, Point right) => left.Equals(right);
    public static bool operator !=(Point left, Point right) => !left.Equals(right);

    public bool Equals(Point other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Point other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"{X.ToString(CultureInfo.InvariantCulture)},{Y.ToString(CultureInfo.InvariantCulture)}";
    public string ToString(string? format, IFormatProvider? provider) => ToString();

    public static implicit operator WpfPoint(Point point) => new(point.X, point.Y);
    public static implicit operator Point(WpfPoint point) => new(point.X, point.Y);

    /// <summary>Narrowing conversion (matches explicit Point-to-Size; converted shell chrome binds it).</summary>
    public static explicit operator Size(Point point) => new(point.X, point.Y);

    /// <summary>Narrowing conversion (matches explicit Point-to-Vector).</summary>
    public static explicit operator Vector(Point point) => new(point.X, point.Y);
}

/// <summary>2D vector (matches System.Windows.Vector member layout).</summary>
public struct Vector : IEquatable<Vector>, IFormattable
{
    public double X { get; set; }
    public double Y { get; set; }

    public Vector(double x, double y) { X = x; Y = y; }

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    /// <summary>Scales to unit length in place (matches Vector.Normalize).</summary>
    public void Normalize()
    {
        var length = Length;
        X /= length;
        Y /= length;
    }

    /// <summary>Unsigned angle in degrees, [0, 180] (matches AngleBetween).</summary>
    public static double AngleBetween(Vector vector1, Vector vector2)
    {
        var dot = Multiply(vector1, vector2);
        var cross = CrossProduct(vector1, vector2);
        return Math.Atan2(Math.Abs(cross), dot) * 180.0 / Math.PI;
    }

    public static double CrossProduct(Vector vector1, Vector vector2) =>
        vector1.X * vector2.Y - vector1.Y * vector2.X;

    public static double Determinant(Vector vector1, Vector vector2) => CrossProduct(vector1, vector2);

    public static double Multiply(Vector vector1, Vector vector2) =>
        vector1.X * vector2.X + vector1.Y * vector2.Y;

    /// <summary>Parses "x,y" or "x y" (invariant).</summary>
    public static Vector Parse(string value)
    {
        var point = Point.Parse(value);
        return new Vector(point.X, point.Y);
    }

    public static Vector operator +(Vector left, Vector right) =>
        new(left.X + right.X, left.Y + right.Y);
    public static Vector operator -(Vector left, Vector right) =>
        new(left.X - right.X, left.Y - right.Y);
    public static Vector operator -(Vector vector) => new(-vector.X, -vector.Y);
    public static Vector operator *(Vector vector, double scalar) =>
        new(vector.X * scalar, vector.Y * scalar);
    public static Vector operator *(double scalar, Vector vector) => vector * scalar;
    public static Vector operator /(Vector vector, double scalar) =>
        new(vector.X / scalar, vector.Y / scalar);
    public static bool operator ==(Vector left, Vector right) => left.Equals(right);
    public static bool operator !=(Vector left, Vector right) => !left.Equals(right);

    public bool Equals(Vector other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Vector other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"{X.ToString(CultureInfo.InvariantCulture)},{Y.ToString(CultureInfo.InvariantCulture)}";
    public string ToString(string? format, IFormatProvider? provider) => ToString();
}

/// <summary>Width/height pair (matches System.Windows.Size member layout).</summary>
public struct Size : IEquatable<Size>, IFormattable
{
    public double Width { get; set; }
    public double Height { get; set; }

    public Size(double width, double height)
    {
        if (double.IsNaN(width) || double.IsNaN(height) || width < 0 || height < 0)
            throw new ArgumentException("Width and Height must be non-negative numbers.");
        Width = width;
        Height = height;
    }

    public static Size Empty { get; } = new(double.NegativeInfinity, double.NegativeInfinity, skipValidation: true);

    private Size(double width, double height, bool skipValidation)
    {
        Width = width;
        Height = height;
    }

    public bool IsEmpty => Width < 0;

    /// <summary>Parses "width,height" or space-separated (invariant).</summary>
    public static Size Parse(string value)
    {
        var point = Point.Parse(value);
        return new Size(point.X, point.Y);
    }

    public static explicit operator Vector(Size size) => new(size.Width, size.Height);
    public static explicit operator Point(Size size) => new(size.Width, size.Height);

    public static bool operator ==(Size left, Size right) => left.Equals(right);
    public static bool operator !=(Size left, Size right) => !left.Equals(right);

    public bool Equals(Size other) => Width == other.Width && Height == other.Height;
    public override bool Equals(object? obj) => obj is Size other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Width, Height);
    public override string ToString() => $"{Width.ToString(CultureInfo.InvariantCulture)},{Height.ToString(CultureInfo.InvariantCulture)}";
    public string ToString(string? format, IFormatProvider? provider) => ToString();

    public static implicit operator WpfSize(Size size) => new(size.Width, size.Height);
    public static implicit operator Size(WpfSize size) => new(size.Width, size.Height);
}

/// <summary>Rectangle (matches System.Windows.Rect member layout).</summary>
public struct Rect : IEquatable<Rect>, IFormattable
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public Rect(double x, double y, double width, double height)
    {
        if (double.IsNaN(width) || double.IsNaN(height) || width < 0 || height < 0)
            throw new ArgumentException("Width and Height must be non-negative numbers.");
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public Rect(Point location, Size size) : this(location.X, location.Y, size.Width, size.Height) { }

    public Rect(Point point1, Point point2)
    {
        X = Math.Min(point1.X, point2.X);
        Y = Math.Min(point1.Y, point2.Y);
        Width = Math.Abs(point2.X - point1.X);
        Height = Math.Abs(point2.Y - point1.Y);
    }

    public Rect(Point location, Vector vector)
        : this(location.X, location.Y, Math.Abs(vector.X), Math.Abs(vector.Y)) { }

    public Rect(Size size) : this(0, 0, size.Width, size.Height) { }

    public static Rect Empty { get; } = new()
    {
        X = double.PositiveInfinity,
        Y = double.PositiveInfinity,
        Width = double.NegativeInfinity,
        Height = double.NegativeInfinity,
    };

    public bool IsEmpty => Width < 0;
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public Point TopLeft => new(X, Y);
    public Point TopRight => new(Right, Y);
    public Point BottomLeft => new(X, Bottom);
    public Point BottomRight => new(Right, Bottom);

    public Point Location
    {
        get => new(X, Y);
        set { X = value.X; Y = value.Y; }
    }

    public Size Size
    {
        get => new(Width, Height);
        set { Width = value.Width; Height = value.Height; }
    }

    public bool Contains(Point point) => Contains(point.X, point.Y);

    public bool Contains(double x, double y) =>
        !IsEmpty && x >= X && x <= Right && y >= Y && y <= Bottom;

    public bool Contains(Rect rect) =>
        !IsEmpty && !rect.IsEmpty &&
        rect.X >= X && rect.Right <= Right && rect.Y >= Y && rect.Bottom <= Bottom;

    public bool Contains(double x, double y, double width, double height) =>
        Contains(new Rect(x, y, width, height));

    public void Inflate(Size size) => Inflate(size.Width, size.Height);

    public void Inflate(double width, double height)
    {
        X -= width;
        Y -= height;
        Width += 2 * width;
        Height += 2 * height;
    }

    public void Intersect(Rect rect)
    {
        if (!IntersectsWith(rect)) { this = Empty; return; }
        var left = Math.Max(X, rect.X);
        var top = Math.Max(Y, rect.Y);
        var right = Math.Min(Right, rect.Right);
        var bottom = Math.Min(Bottom, rect.Bottom);
        X = left;
        Y = top;
        Width = right - left;
        Height = bottom - top;
    }

    public static Rect Intersect(Rect rect1, Rect rect2)
    {
        rect1.Intersect(rect2);
        return rect1;
    }

    public bool IntersectsWith(Rect rect) =>
        !IsEmpty && !rect.IsEmpty &&
        rect.X <= Right && X <= rect.Right && rect.Y <= Bottom && Y <= rect.Bottom;

    public void Offset(Vector offsetVector) => Offset(offsetVector.X, offsetVector.Y);

    public void Offset(double offsetX, double offsetY)
    {
        X += offsetX;
        Y += offsetY;
    }

    public void Union(Point point)
    {
        if (IsEmpty) { this = new Rect(point, new Size(0, 0)); return; }
        var left = Math.Min(X, point.X);
        var top = Math.Min(Y, point.Y);
        var right = Math.Max(Right, point.X);
        var bottom = Math.Max(Bottom, point.Y);
        X = left;
        Y = top;
        Width = right - left;
        Height = bottom - top;
    }

    public void Union(Rect rect)
    {
        if (IsEmpty) { this = rect; return; }
        if (rect.IsEmpty) return;
        Union(rect.TopLeft);
        Union(rect.BottomRight);
    }

    public static Rect Union(Rect rect1, Rect rect2)
    {
        rect1.Union(rect2);
        return rect1;
    }

    public static Rect Union(Rect rect, Point point)
    {
        rect.Union(point);
        return rect;
    }

    /// <summary>Parses "x,y,width,height" (invariant).</summary>
    public static Rect Parse(string value)
    {
        var parts = ConverterText.Split(value);
        if (parts.Length == 4 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) &&
            double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
            return new Rect(x, y, w, h);
        throw new FormatException($"'{value}' is not a rectangle (x,y,width,height).");
    }

    public static bool operator ==(Rect left, Rect right) => left.Equals(right);
    public static bool operator !=(Rect left, Rect right) => !left.Equals(right);

    public bool Equals(Rect other) =>
        X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    public override bool Equals(object? obj) => obj is Rect other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    public override string ToString() =>
        string.Join(",", new[] { X, Y, Width, Height }.Select(v => v.ToString(CultureInfo.InvariantCulture)));
    public string ToString(string? format, IFormatProvider? provider) => ToString();

    public static implicit operator WpfRect(Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    public static implicit operator Rect(WpfRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}

/// <summary>Integer rectangle (matches System.Windows.Int32Rect).</summary>
public struct Int32Rect : IEquatable<Int32Rect>, IFormattable
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public Int32Rect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public static Int32Rect Empty { get; } = new(0, 0, 0, 0);

    public bool HasArea => Width > 0 && Height > 0;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Parses "x,y,width,height" or space-separated (invariant).</summary>
    public static Int32Rect Parse(string value)
    {
        var parts = ConverterText.Split(value);
        if (parts.Length == 4 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) &&
            int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) &&
            int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
            return new Int32Rect(x, y, w, h);
        throw new FormatException($"'{value}' is not an integer rectangle (x,y,width,height).");
    }

    public static bool operator ==(Int32Rect left, Int32Rect right) => left.Equals(right);
    public static bool operator !=(Int32Rect left, Int32Rect right) => !left.Equals(right);

    public bool Equals(Int32Rect other) =>
        X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    public override bool Equals(object? obj) => obj is Int32Rect other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    public override string ToString() => $"{X},{Y},{Width},{Height}";
    public string ToString(string? format, IFormatProvider? provider) => ToString();
}

/// <summary>
/// Corner radii (matches System.Windows.CornerRadius): per-corner values the
/// converted gallery sets individually (TopLeft/TopRight/BottomRight/BottomLeft
/// setters) or uniformly. The canvas bridge renders a single radius (TopLeft);
/// per-corner curvature is a later Graphics milestone.
/// </summary>
public struct CornerRadius : IEquatable<CornerRadius>, IFormattable
{
    public double TopLeft { get; set; }
    public double TopRight { get; set; }
    public double BottomRight { get; set; }
    public double BottomLeft { get; set; }

    public CornerRadius(double uniform)
        : this(uniform, uniform, uniform, uniform)
    {
    }

    public CornerRadius(double topLeft, double topRight, double bottomRight, double bottomLeft)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
    }

    /// <summary>Parses "u" or "tl,tr,br,bl" (invariant; matches XAML type conversion).</summary>
    public static CornerRadius Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        // WPF accepts comma- and space-separated forms ("8 1 8 8" gallery titles).
        var parts = value.Split([',', ' '], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var u))
            return new CornerRadius(u);
        if (parts.Length == 4 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var tl) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var tr) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var br) &&
            double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var bl))
            return new CornerRadius(tl, tr, br, bl);
        throw new FormatException($"'{value}' is not a corner radius ('u' or 'tl,tr,br,bl').");
    }

    public static bool operator ==(CornerRadius left, CornerRadius right) => left.Equals(right);
    public static bool operator !=(CornerRadius left, CornerRadius right) => !left.Equals(right);

    public bool Equals(CornerRadius other) =>
        TopLeft == other.TopLeft && TopRight == other.TopRight &&
        BottomRight == other.BottomRight && BottomLeft == other.BottomLeft;
    public override bool Equals(object? obj) => obj is CornerRadius other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(TopLeft, TopRight, BottomRight, BottomLeft);
    public override string ToString() => $"{TopLeft},{TopRight},{BottomRight},{BottomLeft}";
    public string ToString(string? format, IFormatProvider? provider) => ToString();
}
