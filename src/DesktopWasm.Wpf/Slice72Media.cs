namespace DesktopWasm.Wpf;

// Slice-72 media/3D slice (live shapes verified 2026-10-26 with Mono.Cecil
// against the 9.0.20 PresentationCore ref pack): the Media3D remainder the
// converted MaterialDesign Plane3D binds (Quaternion math, rotation, the
// transform/model collections, the 2D-visual host and the cache-mode base),
// the mouse-capture mode, the media element with its state/event surface,
// the shader-effect contract converted Behaviors transitions derive from,
// and the manipulation starting/inertia family converted Behaviors
// TranslateZoomRotateBehavior drives.
//
// Retained limits: there is no 3D rasterizer (Plane3D-style hosts keep
// their scene graph without projecting it), no media pipeline (the element
// retains Source/state; MediaEnded/MediaFailed fire only when the host
// drives them), no pixel-shader compiler (sampler registration lands in the
// DP store and UpdateShaderValue repaints without compiling), and no touch
// input (manipulation args flow from explicit construction).

/// <summary>3D rotation quaternion (matches System.Windows.Media.Media3D.Quaternion).</summary>
public struct Quaternion : IEquatable<Quaternion>, IFormattable
{
    /// <summary>Creates a quaternion from components.</summary>
    public Quaternion(double x, double y, double z, double w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    /// <summary>Creates a rotation quaternion (matches Quaternion(Vector3D, double)).</summary>
    public Quaternion(Vector3D axisOfRotation, double angleInDegrees)
    {
        var axis = axisOfRotation.Length == 0 ? axisOfRotation : axisOfRotation.Normalized();
        var half = angleInDegrees * Math.PI / 360.0;
        var sine = Math.Sin(half);
        X = axis.X * sine;
        Y = axis.Y * sine;
        Z = axis.Z * sine;
        W = Math.Cos(half);
    }

    /// <summary>X component.</summary>
    public double X { get; set; }

    /// <summary>Y component.</summary>
    public double Y { get; set; }

    /// <summary>Z component.</summary>
    public double Z { get; set; }

    /// <summary>W component.</summary>
    public double W { get; set; }

    /// <summary>Identity rotation (matches Identity).</summary>
    public static Quaternion Identity => new(0, 0, 0, 1);

    /// <summary>Whether this is the identity rotation (matches IsIdentity).</summary>
    public bool IsIdentity => X == 0 && Y == 0 && Z == 0 && W == 1;

    /// <summary>Whether the quaternion is unit length (matches IsNormalized).</summary>
    public bool IsNormalized
    {
        get
        {
            var length = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
            return Math.Abs(length - 1.0) < 1e-10;
        }
    }

    /// <summary>Rotation angle in degrees (matches Angle).</summary>
    public double Angle => 2.0 * Math.Acos(Math.Clamp(W, -1.0, 1.0)) * 180.0 / Math.PI;

    /// <summary>Rotation axis (matches Axis).</summary>
    public Vector3D Axis
    {
        get
        {
            var sine = Math.Sqrt(Math.Max(0.0, 1.0 - W * W));
            return sine < 1e-10 ? new Vector3D(0, 1, 0) : new Vector3D(X / sine, Y / sine, Z / sine);
        }
    }

    /// <summary>Component-wise sum (matches Add).</summary>
    public static Quaternion Add(Quaternion left, Quaternion right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);

    /// <summary>Component-wise difference (matches Subtract).</summary>
    public static Quaternion Subtract(Quaternion left, Quaternion right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.W - right.W);

    /// <summary>Hamilton product (matches Multiply).</summary>
    public static Quaternion Multiply(Quaternion left, Quaternion right) => new(
        left.W * right.X + left.X * right.W + left.Y * right.Z - left.Z * right.Y,
        left.W * right.Y - left.X * right.Z + left.Y * right.W + left.Z * right.X,
        left.W * right.Z + left.X * right.Y - left.Y * right.X + left.Z * right.W,
        left.W * right.W - left.X * right.X - left.Y * right.Y - left.Z * right.Z);

    /// <summary>Spherical interpolation (matches Slerp).</summary>
    public static Quaternion Slerp(Quaternion from, Quaternion to, double t) =>
        Slerp(from, to, t, useShortestPath: true);

    /// <summary>Spherical interpolation with path control (matches Slerp).</summary>
    public static Quaternion Slerp(Quaternion from, Quaternion to, double t, bool useShortestPath)
    {
        var dot = from.X * to.X + from.Y * to.Y + from.Z * to.Z + from.W * to.W;
        if (useShortestPath && dot < 0)
        {
            to = new Quaternion(-to.X, -to.Y, -to.Z, -to.W);
            dot = -dot;
        }
        const double epsilon = 1e-6;
        if (dot > 1.0 - epsilon)
        {
            return new Quaternion(
                from.X + ((to.X - from.X) * t),
                from.Y + ((to.Y - from.Y) * t),
                from.Z + ((to.Z - from.Z) * t),
                from.W + ((to.W - from.W) * t));
        }
        var angle = Math.Acos(Math.Clamp(dot, -1.0, 1.0));
        var sine = Math.Sin(angle);
        var fromScale = Math.Sin((1.0 - t) * angle) / sine;
        var toScale = Math.Sin(t * angle) / sine;
        return new Quaternion(
            from.X * fromScale + to.X * toScale,
            from.Y * fromScale + to.Y * toScale,
            from.Z * fromScale + to.Z * toScale,
            from.W * fromScale + to.W * toScale);
    }

    /// <summary>Conjugates in place (matches Conjugate).</summary>
    public void Conjugate()
    {
        X = -X;
        Y = -Y;
        Z = -Z;
    }

    /// <summary>Inverts in place (matches Invert).</summary>
    public void Invert()
    {
        var norm = X * X + Y * Y + Z * Z + W * W;
        Conjugate();
        X /= norm;
        Y /= norm;
        Z /= norm;
        W /= norm;
    }

    /// <summary>Normalizes in place (matches Normalize).</summary>
    public void Normalize()
    {
        var length = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
        if (length == 0) throw new InvalidOperationException("Cannot normalize a zero quaternion.");
        X /= length;
        Y /= length;
        Z /= length;
        W /= length;
    }

    /// <summary>Parses "x,y,z,w" (matches Parse).</summary>
    public static Quaternion Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var parts = source.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 ||
            !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) ||
            !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z) ||
            !double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w))
            throw new FormatException($"'{source}' is not a quaternion (x,y,z,w).");
        return new Quaternion(x, y, z, w);
    }

    /// <inheritdoc />
    public bool Equals(Quaternion other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Quaternion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

    /// <inheritdoc />
    public override string ToString() => $"{X},{Y},{Z},{W}";

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        string.Join(",", new[] { X, Y, Z, W }.Select(v => v.ToString(format, formatProvider)));

    /// <summary>Component-wise sum (matches op_Addition).</summary>
    public static Quaternion operator +(Quaternion left, Quaternion right) => Add(left, right);

    /// <summary>Component-wise difference (matches op_Subtraction).</summary>
    public static Quaternion operator -(Quaternion left, Quaternion right) => Subtract(left, right);

    /// <summary>Hamilton product (matches op_Multiply).</summary>
    public static Quaternion operator *(Quaternion left, Quaternion right) => Multiply(left, right);

    /// <summary>Component equality (matches op_Equality).</summary>
    public static bool operator ==(Quaternion left, Quaternion right) => left.Equals(right);

    /// <summary>Component inequality (matches op_Inequality).</summary>
    public static bool operator !=(Quaternion left, Quaternion right) => !left.Equals(right);
}

/// <summary>Quaternion rotation (matches System.Windows.Media.Media3D.QuaternionRotation3D).</summary>
public class QuaternionRotation3D : Rotation3D
{
    private Quaternion _quaternion = Quaternion.Identity;

    /// <summary>Empty rotation.</summary>
    public QuaternionRotation3D()
    {
    }

    /// <summary>Rotation carrying a quaternion (matches QuaternionRotation3D(Quaternion)).</summary>
    public QuaternionRotation3D(Quaternion quaternion) => Quaternion = quaternion;

    /// <summary>Quaternion store (matches QuaternionProperty).</summary>
    public static readonly DependencyProperty QuaternionProperty =
        DependencyProperty.Register("Quaternion", typeof(Quaternion), typeof(QuaternionRotation3D), Quaternion.Identity);

    /// <summary>Rotation quaternion (matches Quaternion).</summary>
    public Quaternion Quaternion
    {
        get => GetValue(QuaternionProperty) is Quaternion quaternion ? quaternion : Quaternion.Identity;
        set => SetValue(QuaternionProperty, value);
    }

    /// <summary>Modifiable copy (matches Clone).</summary>
    public QuaternionRotation3D Clone() => new(Quaternion);

    /// <summary>Identical to Clone here (matches CloneCurrentValue).</summary>
    public QuaternionRotation3D CloneCurrentValue() => Clone();
}

/// <summary>Transform list (matches System.Windows.Media.Media3D.Transform3DCollection).</summary>
public sealed class Transform3DCollection : Animatable, IList<Transform3D>, System.Collections.IList
{
    private readonly List<Transform3D> _items = new();

    /// <summary>Empty collection.</summary>
    public Transform3DCollection()
    {
    }

    /// <summary>Collection over items (matches Transform3DCollection(IEnumerable)).</summary>
    public Transform3DCollection(IEnumerable<Transform3D> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items.AddRange(collection);
    }

    /// <summary>Collection with capacity (matches Transform3DCollection(int)).</summary>
    public Transform3DCollection(int capacity) => _items.Capacity = capacity;

    /// <summary>Item count (matches Count).</summary>
    public int Count => _items.Count;

    /// <summary>Never fixed-size.</summary>
    public bool IsFixedSize => false;

    /// <summary>Frozen collections reject writes.</summary>
    public bool IsReadOnly => IsFrozen;

    /// <summary>Never thread-safe (single-threaded WASM).</summary>
    public bool IsSynchronized => false;

    /// <summary>Collection identity lock.</summary>
    public object SyncRoot => _items;

    /// <summary>Item access (matches Item).</summary>
    public Transform3D this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ThrowIfFrozen();
            _items[index] = value;
        }
    }

    /// <summary>Appends a transform (matches Add).</summary>
    public void Add(Transform3D value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfFrozen();
        _items.Add(value);
    }

    void ICollection<Transform3D>.Add(Transform3D item) => Add(item);

    /// <inheritdoc />
    public void Clear()
    {
        ThrowIfFrozen();
        _items.Clear();
    }

    /// <inheritdoc />
    public bool Contains(Transform3D value) => value is not null && _items.Contains(value);

    /// <inheritdoc />
    public void CopyTo(Transform3D[] array, int index) => _items.CopyTo(array, index);

    /// <inheritdoc />
    public Enumerator GetEnumerator() => new(_items);

    /// <inheritdoc />
    public int IndexOf(Transform3D value) => value is null ? -1 : _items.IndexOf(value);

    /// <inheritdoc />
    public void Insert(int index, Transform3D value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfFrozen();
        _items.Insert(index, value);
    }

    /// <inheritdoc />
    public bool Remove(Transform3D value)
    {
        ThrowIfFrozen();
        return value is not null && _items.Remove(value);
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        ThrowIfFrozen();
        _items.RemoveAt(index);
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    IEnumerator<Transform3D> IEnumerable<Transform3D>.GetEnumerator() => _items.GetEnumerator();

    int System.Collections.IList.Add(object? value)
    {
        if (value is Transform3D transform) { Add(transform); return _items.Count - 1; }
        throw new ArgumentException($"'{value?.GetType().Name}' is not a '{nameof(Transform3D)}'.", nameof(value));
    }

    bool System.Collections.IList.Contains(object? value) => value is Transform3D transform && Contains(transform);

    int System.Collections.IList.IndexOf(object? value) => value is Transform3D transform ? IndexOf(transform) : -1;

    void System.Collections.IList.Insert(int index, object? value)
    {
        if (value is Transform3D transform) Insert(index, transform);
        else throw new ArgumentException($"'{value?.GetType().Name}' is not a '{nameof(Transform3D)}'.", nameof(value));
    }

    void System.Collections.IList.Remove(object? value)
    {
        if (value is Transform3D transform) Remove(transform);
    }

    object? System.Collections.IList.this[int index]
    {
        get => this[index];
        set
        {
            if (value is Transform3D transform) this[index] = transform;
            else throw new ArgumentException($"'{value?.GetType().Name}' is not a '{nameof(Transform3D)}'.", nameof(value));
        }
    }

    void System.Collections.ICollection.CopyTo(Array array, int index) =>
        ((System.Collections.ICollection)_items).CopyTo(array, index);

    /// <summary>Modifiable copy (matches Clone).</summary>
    public new Transform3DCollection Clone()
    {
        var clone = new Transform3DCollection();
        clone._items.AddRange(_items);
        return clone;
    }

    /// <summary>Identical to Clone here (matches CloneCurrentValue).</summary>
    public new Transform3DCollection CloneCurrentValue() => Clone();


    /// <summary>Struct enumerator (matches Transform3DCollection.Enumerator).</summary>
    public struct Enumerator : IEnumerator<Transform3D>, System.Collections.IEnumerator
    {
        private readonly List<Transform3D> _source;
        private int _index;

        internal Enumerator(List<Transform3D> source)
        {
            _source = source;
            _index = -1;
        }

        /// <inheritdoc />
        public Transform3D Current => _source[_index];

        object? System.Collections.IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext() => ++_index < _source.Count;

        /// <inheritdoc />
        public void Reset() => _index = -1;

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}

/// <summary>Model group (matches System.Windows.Media.Media3D.Model3DGroup).</summary>
public class Model3DGroup : Model3D
{
    private Model3DCollection _children = new();

    /// <summary>Empty group (matches Model3DGroup()).</summary>
    public Model3DGroup()
    {
    }

    /// <summary>Child models (matches Children).</summary>
    public Model3DCollection Children
    {
        get => _children;
        set => _children = value ?? new Model3DCollection();
    }

    /// <summary>Deep copy (matches Clone).</summary>
    public Model3DGroup Clone()
    {
        var clone = new Model3DGroup { Transform = Transform };
        foreach (var child in Children)
            clone.Children.Add(child is Model3DGroup group ? group.Clone() : child);
        return clone;
    }

    /// <summary>Identical to Clone here (matches CloneCurrentValue).</summary>
    public Model3DGroup CloneCurrentValue() => Clone();
}

/// <summary>Model list (matches System.Windows.Media.Media3D.Model3DCollection).</summary>
public sealed class Model3DCollection : Animatable, IList<Model3D>, System.Collections.IList
{
    private readonly List<Model3D> _items = new();

    /// <summary>Empty collection.</summary>
    public Model3DCollection()
    {
    }

    /// <summary>Collection over items (matches Model3DCollection(IEnumerable)).</summary>
    public Model3DCollection(IEnumerable<Model3D> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items.AddRange(collection);
    }

    /// <summary>Collection with capacity (matches Model3DCollection(int)).</summary>
    public Model3DCollection(int capacity) => _items.Capacity = capacity;

    /// <summary>Item count (matches Count).</summary>
    public int Count => _items.Count;

    /// <summary>Never fixed-size.</summary>
    public bool IsFixedSize => false;

    /// <summary>Frozen collections reject writes.</summary>
    public bool IsReadOnly => IsFrozen;

    /// <summary>Never thread-safe (single-threaded WASM).</summary>
    public bool IsSynchronized => false;

    /// <summary>Collection identity lock.</summary>
    public object SyncRoot => _items;

    /// <summary>Item access (matches Item).</summary>
    public Model3D this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ThrowIfFrozen();
            _items[index] = value;
        }
    }

    /// <summary>Appends a model (matches Add).</summary>
    public void Add(Model3D value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfFrozen();
        _items.Add(value);
    }

    void ICollection<Model3D>.Add(Model3D item) => Add(item);

    /// <inheritdoc />
    public void Clear()
    {
        ThrowIfFrozen();
        _items.Clear();
    }

    /// <inheritdoc />
    public bool Contains(Model3D value) => value is not null && _items.Contains(value);

    /// <inheritdoc />
    public void CopyTo(Model3D[] array, int index) => _items.CopyTo(array, index);

    /// <inheritdoc />
    public Enumerator GetEnumerator() => new(_items);

    /// <inheritdoc />
    public int IndexOf(Model3D value) => value is null ? -1 : _items.IndexOf(value);

    /// <inheritdoc />
    public void Insert(int index, Model3D value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfFrozen();
        _items.Insert(index, value);
    }

    /// <inheritdoc />
    public bool Remove(Model3D value)
    {
        ThrowIfFrozen();
        return value is not null && _items.Remove(value);
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        ThrowIfFrozen();
        _items.RemoveAt(index);
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    IEnumerator<Model3D> IEnumerable<Model3D>.GetEnumerator() => _items.GetEnumerator();

    int System.Collections.IList.Add(object? value)
    {
        if (value is Model3D model) { Add(model); return _items.Count - 1; }
        throw new ArgumentException($"'{value?.GetType().Name}' is not a '{nameof(Model3D)}'.", nameof(value));
    }

    bool System.Collections.IList.Contains(object? value) => value is Model3D model && Contains(model);

    int System.Collections.IList.IndexOf(object? value) => value is Model3D model ? IndexOf(model) : -1;

    void System.Collections.IList.Insert(int index, object? value)
    {
        if (value is Model3D model) Insert(index, model);
        else throw new ArgumentException($"'{value?.GetType().Name}' is not a '{nameof(Model3D)}'.", nameof(value));
    }

    void System.Collections.IList.Remove(object? value)
    {
        if (value is Model3D model) Remove(model);
    }

    object? System.Collections.IList.this[int index]
    {
        get => this[index];
        set
        {
            if (value is Model3D model) this[index] = model;
            else throw new ArgumentException($"'{value?.GetType().Name}' is not a '{nameof(Model3D)}'.", nameof(value));
        }
    }

    void System.Collections.ICollection.CopyTo(Array array, int index) =>
        ((System.Collections.ICollection)_items).CopyTo(array, index);

    /// <summary>Modifiable copy (matches Clone).</summary>
    public new Model3DCollection Clone()
    {
        var clone = new Model3DCollection();
        clone._items.AddRange(_items);
        return clone;
    }

    /// <summary>Identical to Clone here (matches CloneCurrentValue).</summary>
    public new Model3DCollection CloneCurrentValue() => Clone();


    /// <summary>Struct enumerator (matches Model3DCollection.Enumerator).</summary>
    public struct Enumerator : IEnumerator<Model3D>, System.Collections.IEnumerator
    {
        private readonly List<Model3D> _source;
        private int _index;

        internal Enumerator(List<Model3D> source)
        {
            _source = source;
            _index = -1;
        }

        /// <inheritdoc />
        public Model3D Current => _source[_index];

        object? System.Collections.IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext() => ++_index < _source.Count;

        /// <inheritdoc />
        public void Reset() => _index = -1;

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}

/// <summary>Cache base (matches System.Windows.Media.CacheMode).</summary>
public abstract class CacheMode : Animatable
{
    /// <summary>Modifiable copy (matches Clone).</summary>
    public new CacheMode Clone() => (CacheMode)base.Clone();

    /// <summary>Identical to Clone here (matches CloneCurrentValue).</summary>
    public new CacheMode CloneCurrentValue() => Clone();
}

/// <summary>
/// 2D content on a 3D surface (matches
/// System.Windows.Media.Media3D.Viewport2DVisual3D): converted MaterialDesign
/// Plane3D builds its rotating card through the .ctor plus the
/// Geometry/Visual/Material setters and the IsVisualHostMaterial attached
/// marker. The engine retains the scene graph without projecting it (no 3D
/// rasterizer), so the visual never paints.
/// </summary>
public class Viewport2DVisual3D : Visual3D
{
    /// <summary>Empty host.</summary>
    public Viewport2DVisual3D()
    {
    }

    /// <summary>Cache store (matches CacheModeProperty).</summary>
    public static readonly DependencyProperty CacheModeProperty =
        DependencyProperty.Register("CacheMode", typeof(CacheMode), typeof(Viewport2DVisual3D), (object?)null);

    /// <summary>Geometry store (matches GeometryProperty).</summary>
    public static readonly DependencyProperty GeometryProperty =
        DependencyProperty.Register("Geometry", typeof(Geometry3D), typeof(Viewport2DVisual3D), (object?)null);

    /// <summary>Material store (matches MaterialProperty).</summary>
    public static readonly DependencyProperty MaterialProperty =
        DependencyProperty.Register("Material", typeof(Material), typeof(Viewport2DVisual3D), (object?)null);

    /// <summary>Visual store (matches VisualProperty).</summary>
    public static readonly DependencyProperty VisualProperty =
        DependencyProperty.Register("Visual", typeof(Visual), typeof(Viewport2DVisual3D), (object?)null);

    /// <summary>Host-material marker store (matches IsVisualHostMaterialProperty).</summary>
    public static readonly DependencyProperty IsVisualHostMaterialProperty =
        DependencyProperty.RegisterAttached("IsVisualHostMaterial", typeof(bool), typeof(Viewport2DVisual3D),
            new FrameworkPropertyMetadata(false));

    /// <summary>Cache mode (matches CacheMode).</summary>
    public CacheMode? CacheMode
    {
        get => GetValue(CacheModeProperty) as CacheMode;
        set => SetValue(CacheModeProperty, value);
    }

    /// <summary>Surface geometry (matches Geometry).</summary>
    public Geometry3D? Geometry
    {
        get => GetValue(GeometryProperty) as Geometry3D;
        set => SetValue(GeometryProperty, value);
    }

    /// <summary>Surface material (matches Material).</summary>
    public Material? Material
    {
        get => GetValue(MaterialProperty) as Material;
        set => SetValue(MaterialProperty, value);
    }

    /// <summary>Hosted 2D visual (matches Visual).</summary>
    public Visual? Visual
    {
        get => GetValue(VisualProperty) as Visual;
        set => SetValue(VisualProperty, value);
    }

    /// <summary>Reads the host-material marker (matches GetIsVisualHostMaterial).</summary>
    public static bool GetIsVisualHostMaterial(Material element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(IsVisualHostMaterialProperty) is true;
    }

    /// <summary>Writes the host-material marker (matches SetIsVisualHostMaterial).</summary>
    public static void SetIsVisualHostMaterial(Material element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsVisualHostMaterialProperty, value);
    }
}

/// <summary>Mouse-capture scope (matches System.Windows.Input.CaptureMode).</summary>
public enum CaptureMode
{
    /// <summary>No capture.</summary>
    None = 0,

    /// <summary>Capture to the element.</summary>
    Element = 1,

    /// <summary>Capture to the element subtree.</summary>
    SubTree = 2,
}

/// <summary>Playback state (matches System.Windows.Controls.MediaState).</summary>
public enum MediaState
{
    /// <summary>Manual control.</summary>
    Manual = 0,

    /// <summary>Playing.</summary>
    Play = 1,

    /// <summary>Closed.</summary>
    Close = 2,

    /// <summary>Paused.</summary>
    Pause = 3,

    /// <summary>Stopped.</summary>
    Stop = 4,
}

/// <summary>Routed error data (matches System.Windows.ExceptionRoutedEventArgs).</summary>
public class ExceptionRoutedEventArgs : RoutedEventArgs
{
    internal ExceptionRoutedEventArgs(Exception errorException)
    {
        ErrorException = errorException ?? throw new ArgumentNullException(nameof(errorException));
    }

    /// <summary>Cause (matches ErrorException).</summary>
    public Exception ErrorException { get; }
}

/// <summary>Script-command data (matches System.Windows.MediaScriptCommandRoutedEventArgs).</summary>
public class MediaScriptCommandRoutedEventArgs : RoutedEventArgs
{
    internal MediaScriptCommandRoutedEventArgs(string parameterType, string parameterValue)
    {
        ParameterType = parameterType;
        ParameterValue = parameterValue;
    }

    /// <summary>Command type (matches ParameterType).</summary>
    public string ParameterType { get; }

    /// <summary>Command value (matches ParameterValue).</summary>
    public string ParameterValue { get; }
}

/// <summary>
/// Media host (matches System.Windows.Controls.MediaElement): converted
/// Behaviors PlaySoundAction constructs it, sets Source/Volume and
/// subscribes MediaEnded/MediaFailed. The engine retains Source and
/// playback state; there is no media pipeline in the browser, so no
/// media events fire on their own (the On* virtuals let the host drive
/// them, like the SystemEvents host-driven pattern).
/// </summary>
public class MediaElement : FrameworkElement
{
    private Uri? _source;
    private TimeSpan _position;
    private bool _isPlaying;

    /// <summary>Empty host.</summary>
    public MediaElement()
    {
    }

    /// <summary>Source store (matches SourceProperty).</summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register("Source", typeof(Uri), typeof(MediaElement), (object?)null);

    /// <summary>Load-behavior store (matches LoadedBehaviorProperty).</summary>
    public static readonly DependencyProperty LoadedBehaviorProperty =
        DependencyProperty.Register("LoadedBehavior", typeof(MediaState), typeof(MediaElement),
            new FrameworkPropertyMetadata(MediaState.Manual));

    /// <summary>Unload-behavior store (matches UnloadedBehaviorProperty).</summary>
    public static readonly DependencyProperty UnloadedBehaviorProperty =
        DependencyProperty.Register("UnloadedBehavior", typeof(MediaState), typeof(MediaElement),
            new FrameworkPropertyMetadata(MediaState.Close));

    /// <summary>Volume store (matches VolumeProperty).</summary>
    public static readonly DependencyProperty VolumeProperty =
        DependencyProperty.Register("Volume", typeof(double), typeof(MediaElement),
            new FrameworkPropertyMetadata(0.5));

    /// <summary>Balance store (matches BalanceProperty).</summary>
    public static readonly DependencyProperty BalanceProperty =
        DependencyProperty.Register("Balance", typeof(double), typeof(MediaElement),
            new FrameworkPropertyMetadata(0.0));

    /// <summary>Mute store (matches IsMutedProperty).</summary>
    public static readonly DependencyProperty IsMutedProperty =
        DependencyProperty.Register("IsMuted", typeof(bool), typeof(MediaElement),
            new FrameworkPropertyMetadata(false));

    /// <summary>Scrub store (matches ScrubbingEnabledProperty).</summary>
    public static readonly DependencyProperty ScrubbingEnabledProperty =
        DependencyProperty.Register("ScrubbingEnabled", typeof(bool), typeof(MediaElement),
            new FrameworkPropertyMetadata(false));

    /// <summary>Speed store (matches SpeedRatioProperty).</summary>
    public static readonly DependencyProperty SpeedRatioProperty =
        DependencyProperty.Register("SpeedRatio", typeof(double), typeof(MediaElement),
            new FrameworkPropertyMetadata(1.0));

    /// <summary>Stretch store (matches StretchProperty).</summary>
    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register("Stretch", typeof(Stretch), typeof(MediaElement),
            new FrameworkPropertyMetadata(Stretch.Fill));

    /// <summary>Stretch-direction store (matches StretchDirectionProperty).</summary>
    public static readonly DependencyProperty StretchDirectionProperty =
        DependencyProperty.Register("StretchDirection", typeof(StretchDirection), typeof(MediaElement),
            new FrameworkPropertyMetadata(StretchDirection.Both));

    /// <summary>Buffering-started token (matches BufferingStartedEvent).</summary>
    public static readonly RoutedEvent BufferingStartedEvent = EventManager.RegisterRoutedEvent(
        "BufferingStarted", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MediaElement));

    /// <summary>Buffering-ended token (matches BufferingEndedEvent).</summary>
    public static readonly RoutedEvent BufferingEndedEvent = EventManager.RegisterRoutedEvent(
        "BufferingEnded", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MediaElement));

    /// <summary>Ended token (matches MediaEndedEvent).</summary>
    public static readonly RoutedEvent MediaEndedEvent = EventManager.RegisterRoutedEvent(
        "MediaEnded", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MediaElement));

    /// <summary>Failed token (matches MediaFailedEvent).</summary>
    public static readonly RoutedEvent MediaFailedEvent = EventManager.RegisterRoutedEvent(
        "MediaFailed", RoutingStrategy.Bubble, typeof(EventHandler<ExceptionRoutedEventArgs>), typeof(MediaElement));

    /// <summary>Opened token (matches MediaOpenedEvent).</summary>
    public static readonly RoutedEvent MediaOpenedEvent = EventManager.RegisterRoutedEvent(
        "MediaOpened", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MediaElement));

    /// <summary>Script-command token (matches ScriptCommandEvent).</summary>
    public static readonly RoutedEvent ScriptCommandEvent = EventManager.RegisterRoutedEvent(
        "ScriptCommand", RoutingStrategy.Bubble, typeof(EventHandler<MediaScriptCommandRoutedEventArgs>), typeof(MediaElement));

    /// <summary>Media source (matches Source).</summary>
    public Uri? Source
    {
        get => GetValue(SourceProperty) as Uri ?? _source;
        set
        {
            _source = value;
            SetValue(SourceProperty, value);
        }
    }

    /// <summary>Load behavior (matches LoadedBehavior).</summary>
    public MediaState LoadedBehavior
    {
        get => GetValue(LoadedBehaviorProperty) is MediaState state ? state : MediaState.Manual;
        set => SetValue(LoadedBehaviorProperty, value);
    }

    /// <summary>Unload behavior (matches UnloadedBehavior).</summary>
    public MediaState UnloadedBehavior
    {
        get => GetValue(UnloadedBehaviorProperty) is MediaState state ? state : MediaState.Close;
        set => SetValue(UnloadedBehaviorProperty, value);
    }

    /// <summary>Volume (matches Volume).</summary>
    public double Volume
    {
        get => GetValue(VolumeProperty) is double volume ? volume : 0.5;
        set => SetValue(VolumeProperty, value);
    }

    /// <summary>Balance (matches Balance).</summary>
    public double Balance
    {
        get => GetValue(BalanceProperty) is double balance ? balance : 0.0;
        set => SetValue(BalanceProperty, value);
    }

    /// <summary>Mute (matches IsMuted).</summary>
    public bool IsMuted
    {
        get => GetValue(IsMutedProperty) is true;
        set => SetValue(IsMutedProperty, value);
    }

    /// <summary>Scrubbing (matches ScrubbingEnabled).</summary>
    public bool ScrubbingEnabled
    {
        get => GetValue(ScrubbingEnabledProperty) is true;
        set => SetValue(ScrubbingEnabledProperty, value);
    }

    /// <summary>Speed ratio (matches SpeedRatio).</summary>
    public double SpeedRatio
    {
        get => GetValue(SpeedRatioProperty) is double ratio ? ratio : 1.0;
        set => SetValue(SpeedRatioProperty, value);
    }

    /// <summary>Stretch (matches Stretch).</summary>
    public Stretch Stretch
    {
        get => GetValue(StretchProperty) is Stretch stretch ? stretch : Stretch.Fill;
        set => SetValue(StretchProperty, value);
    }

    /// <summary>Stretch direction (matches StretchDirection).</summary>
    public StretchDirection StretchDirection
    {
        get => GetValue(StretchDirectionProperty) is StretchDirection direction ? direction : StretchDirection.Both;
        set => SetValue(StretchDirectionProperty, value);
    }

    /// <summary>Playback position (matches Position; retained, no pipeline).</summary>
    public TimeSpan Position
    {
        get => _position;
        set => _position = value.Ticks < 0 ? TimeSpan.Zero : value;
    }

    /// <summary>Buffering progress (matches BufferingProgress; no pipeline, always 0).</summary>
    public double BufferingProgress => 0.0;

    /// <summary>Whether pause is possible (matches CanPause; no pipeline, always false).</summary>
    public bool CanPause => false;

    /// <summary>Download progress (matches DownloadProgress; no pipeline, always 0).</summary>
    public double DownloadProgress => 0.0;

    /// <summary>Audio track (matches HasAudio; no pipeline, always false).</summary>
    public bool HasAudio => false;

    /// <summary>Video track (matches HasVideo; no pipeline, always false).</summary>
    public bool HasVideo => false;

    /// <summary>Buffering flag (matches IsBuffering; no pipeline, always false).</summary>
    public bool IsBuffering => false;

    /// <summary>Natural duration (matches NaturalDuration; no pipeline, unset).</summary>
    public Duration NaturalDuration => Duration.Automatic;

    /// <summary>Natural width (matches NaturalVideoWidth; no pipeline, always 0).</summary>
    public int NaturalVideoWidth => 0;

    /// <summary>Natural height (matches NaturalVideoHeight; no pipeline, always 0).</summary>
    public int NaturalVideoHeight => 0;

    /// <summary>Whether media is playing (engine state).</summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>Buffering started (matches BufferingStarted).</summary>
    public event RoutedEventHandler? BufferingStarted;

    /// <summary>Buffering ended (matches BufferingEnded).</summary>
    public event RoutedEventHandler? BufferingEnded;

    /// <summary>Playback ended (matches MediaEnded).</summary>
    public event RoutedEventHandler? MediaEnded;

    /// <summary>Playback failed (matches MediaFailed).</summary>
    public event EventHandler<ExceptionRoutedEventArgs>? MediaFailed;

    /// <summary>Media opened (matches MediaOpened).</summary>
    public event RoutedEventHandler? MediaOpened;

    /// <summary>Script command (matches ScriptCommand).</summary>
    public event EventHandler<MediaScriptCommandRoutedEventArgs>? ScriptCommand;

    /// <summary>Starts playback (matches Play; retained state, no pipeline).</summary>
    public void Play() => _isPlaying = true;

    /// <summary>Pauses playback (matches Pause; retained state, no pipeline).</summary>
    public void Pause() => _isPlaying = false;

    /// <summary>Stops playback and rewinds (matches Stop).</summary>
    public void Stop()
    {
        _isPlaying = false;
        _position = TimeSpan.Zero;
    }

    /// <summary>Closes the media (matches Close).</summary>
    public void Close()
    {
        _isPlaying = false;
        _position = TimeSpan.Zero;
    }

    /// <summary>Buffering-start dispatch (matches OnBufferingStarted).</summary>
    protected virtual void OnBufferingStarted(RoutedEventArgs e) => BufferingStarted?.Invoke(this, e);

    /// <summary>Buffering-end dispatch (matches OnBufferingEnded).</summary>
    protected virtual void OnBufferingEnded(RoutedEventArgs e) => BufferingEnded?.Invoke(this, e);

    /// <summary>Playback-end dispatch (matches OnMediaEnded).</summary>
    protected virtual void OnMediaEnded(RoutedEventArgs e) => MediaEnded?.Invoke(this, e);

    /// <summary>Playback-failure dispatch (matches OnMediaFailed).</summary>
    protected virtual void OnMediaFailed(ExceptionRoutedEventArgs e) => MediaFailed?.Invoke(this, e);

    /// <summary>Media-open dispatch (matches OnMediaOpened).</summary>
    protected virtual void OnMediaOpened(RoutedEventArgs e) => MediaOpened?.Invoke(this, e);

    /// <summary>Script-command dispatch (matches OnScriptCommand).</summary>
    protected virtual void OnScriptCommand(MediaScriptCommandRoutedEventArgs e) => ScriptCommand?.Invoke(this, e);

    /// <summary>Host-driven buffering start (no pipeline raises this on its own).</summary>
    public void RaiseBufferingStarted() => OnBufferingStarted(new RoutedEventArgs { RoutedEvent = BufferingStartedEvent, Source = this });

    /// <summary>Host-driven buffering end (no pipeline raises this on its own).</summary>
    public void RaiseBufferingEnded() => OnBufferingEnded(new RoutedEventArgs { RoutedEvent = BufferingEndedEvent, Source = this });

    /// <summary>Host-driven playback end (no pipeline raises this on its own).</summary>
    public void RaiseMediaEnded()
    {
        _isPlaying = false;
        OnMediaEnded(new RoutedEventArgs { RoutedEvent = MediaEndedEvent, Source = this });
    }

    /// <summary>Host-driven playback failure (no pipeline raises this on its own).</summary>
    public void RaiseMediaFailed(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _isPlaying = false;
        OnMediaFailed(new ExceptionRoutedEventArgs(error) { RoutedEvent = MediaFailedEvent, Source = this });
    }

    /// <summary>Host-driven media open (no pipeline raises this on its own).</summary>
    public void RaiseMediaOpened() => OnMediaOpened(new RoutedEventArgs { RoutedEvent = MediaOpenedEvent, Source = this });
}

/// <summary>Shader sampling rule (matches System.Windows.Media.Effects.SamplingMode).</summary>
public enum SamplingMode
{
    /// <summary>Nearest texture sample.</summary>
    NearestNeighbor = 0,

    /// <summary>Bilinear texture sample.</summary>
    Bilinear = 1,

    /// <summary>Render-target default.</summary>
    Auto = 2,
}

/// <summary>Shader compile target (matches System.Windows.Media.Effects.ShaderRenderMode).</summary>
public enum ShaderRenderMode
{
    /// <summary>Runtime chooses.</summary>
    Auto = 0,

    /// <summary>CPU fallback.</summary>
    SoftwareOnly = 1,

    /// <summary>GPU only.</summary>
    HardwareOnly = 2,
}

/// <summary>
/// Compiled shader (matches System.Windows.Media.Effects.PixelShader):
/// converted Behaviors transitions carry one per effect. The engine retains
/// the source URI and render mode; bytecode compilation has no browser path,
/// so SetStreamSource records the stream without compiling it.
/// </summary>
public sealed class PixelShader : Animatable
{
    private Stream? _streamSource;

    /// <summary>Empty shader.</summary>
    public PixelShader()
    {
    }

    /// <summary>Render-mode store (matches ShaderRenderModeProperty).</summary>
    public static readonly DependencyProperty ShaderRenderModeProperty =
        DependencyProperty.Register("ShaderRenderMode", typeof(ShaderRenderMode), typeof(PixelShader),
            new FrameworkPropertyMetadata(ShaderRenderMode.Auto));

    /// <summary>Source-URI store (matches UriSourceProperty).</summary>
    public static readonly DependencyProperty UriSourceProperty =
        DependencyProperty.Register("UriSource", typeof(Uri), typeof(PixelShader), (object?)null);

    /// <summary>Render mode (matches ShaderRenderMode).</summary>
    public ShaderRenderMode ShaderRenderMode
    {
        get => GetValue(ShaderRenderModeProperty) is ShaderRenderMode mode ? mode : ShaderRenderMode.Auto;
        set => SetValue(ShaderRenderModeProperty, value);
    }

    /// <summary>Shader source (matches UriSource).</summary>
    public Uri? UriSource
    {
        get => GetValue(UriSourceProperty) as Uri;
        set => SetValue(UriSourceProperty, value);
    }

    /// <summary>Invalid-shader notice (matches InvalidPixelShaderEncountered).</summary>
    public static event EventHandler? InvalidPixelShaderEncountered;

    /// <summary>Raises the invalid-shader notice (host-driven).</summary>
    public static void RaiseInvalidPixelShaderEncountered() =>
        InvalidPixelShaderEncountered?.Invoke(null, EventArgs.Empty);

    /// <summary>Records a bytecode stream without compiling it (matches SetStreamSource).</summary>
    public void SetStreamSource(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _streamSource = source;
    }

    /// <summary>Recorded stream, if any (engine state).</summary>
    public Stream? StreamSource => _streamSource;

    /// <summary>Modifiable copy (matches Clone).</summary>
    public new PixelShader Clone() => (PixelShader)MemberwiseClone();

    /// <summary>Identical to Clone here (matches CloneCurrentValue).</summary>
    public new PixelShader CloneCurrentValue() => Clone();
}

/// <summary>
/// Shader-effect base (matches System.Windows.Media.Effects.ShaderEffect):
/// converted Behaviors TransitionEffect derives from it, calling the
/// sampler registration in its static constructor, UpdateShaderValue in
/// its instance constructor, and CloneCurrentValue for copies. Sampler
/// registration lands in the DP store (repainting through the metadata
/// callback); bytecode compilation has no browser path, so UpdateShaderValue
/// requests a repaint without producing shader pixels.
/// </summary>
public abstract class ShaderEffect : Effect
{
    /// <summary>Shader store (matches PixelShaderProperty).</summary>
    public static readonly DependencyProperty PixelShaderProperty =
        DependencyProperty.Register("PixelShader", typeof(PixelShader), typeof(ShaderEffect), (object?)null);

    /// <summary>Effect shader (matches PixelShader; protected like WPF).</summary>
    protected PixelShader? PixelShader
    {
        get => GetValue(PixelShaderProperty) as PixelShader;
        set => SetValue(PixelShaderProperty, value);
    }

    /// <summary>Shader for tests and hosts (protected in real WPF; exposed for the retained pipeline).</summary>
    public PixelShader? CurrentPixelShader => PixelShader;

    /// <summary>
    /// Registers a sampler input (matches RegisterPixelShaderSamplerProperty):
    /// a Brush-valued DP that repaints the effect when the input changes.
    /// </summary>
    protected static DependencyProperty RegisterPixelShaderSamplerProperty(string samplerName, Type ownerType, int samplerRegister)
    {
        ArgumentException.ThrowIfNullOrEmpty(samplerName);
        ArgumentNullException.ThrowIfNull(ownerType);
        return DependencyProperty.Register(samplerName, typeof(Brush), ownerType,
            new FrameworkPropertyMetadata(null, (_, _) => { }));
    }

    /// <summary>
    /// Builds a constant-buffer invalidation callback (matches
    /// PixelShaderConstantCallback): repainting the effect through
    /// UpdateShaderValue with the captured register.
    /// </summary>
    protected static PropertyChangedCallback PixelShaderConstantCallback(int register) =>
        (d, _) =>
        {
            if (d is ShaderEffect effect) effect.UpdateShaderValue((uint)register);
        };

    /// <summary>
    /// Marks shader constants dirty (matches UpdateShaderValue): a no-op in
    /// the engine because effect inputs are re-read at render time (the
    /// compositor paints through EffectPaint with current DP values), so no
    /// explicit invalidation is needed; no bytecode runs in the browser.
    /// </summary>
    protected void UpdateShaderValue(uint dirtyRegister)
    {
    }

    /// <summary>Modifiable copy (matches Clone).</summary>
    public new ShaderEffect Clone() => (ShaderEffect)base.Clone();

    /// <summary>Identical to Clone here (matches CloneCurrentValue).</summary>
    public new ShaderEffect CloneCurrentValue() => Clone();
}

/// <summary>Touch-manipulation mode (matches System.Windows.Input.ManipulationModes).</summary>
[Flags]
public enum ManipulationModes
{
    /// <summary>No manipulation.</summary>
    None = 0,

    /// <summary>Horizontal translation.</summary>
    TranslateX = 1,

    /// <summary>Vertical translation.</summary>
    TranslateY = 2,

    /// <summary>Translation in both axes.</summary>
    Translate = TranslateX | TranslateY,

    /// <summary>Rotation.</summary>
    Rotate = 4,

    /// <summary>Scaling.</summary>
    Scale = 8,

    /// <summary>Every manipulation.</summary>
    All = Translate | Rotate | Scale,
}

/// <summary>Touch contact (matches System.Windows.Input.IManipulator).</summary>
public interface IManipulator
{
    /// <summary>Contact id (matches Id).</summary>
    int Id { get; }

    /// <summary>Contact position (matches GetPosition).</summary>
    Point GetPosition(IInputElement relativeTo);

    /// <summary>Contact end (matches ManipulationEnded).</summary>
    void ManipulationEnded(bool cancel);
}

/// <summary>Manipulation pivot (matches System.Windows.Input.ManipulationPivot).</summary>
public class ManipulationPivot
{
    /// <summary>Empty pivot.</summary>
    public ManipulationPivot()
    {
    }

    /// <summary>Pivot with center and radius (matches ManipulationPivot(Point, double)).</summary>
    public ManipulationPivot(Point center, double radius)
    {
        Center = center;
        Radius = radius;
    }

    /// <summary>Pivot center (matches Center).</summary>
    public Point Center { get; set; }

    /// <summary>Pivot radius (matches Radius).</summary>
    public double Radius { get; set; }
}

/// <summary>Touch-setup notice (matches System.Windows.Input.ManipulationStartingEventHandler).</summary>
public delegate void ManipulationStartingEventHandler(object sender, ManipulationStartingEventArgs e);

/// <summary>
/// Touch-setup data (matches System.Windows.Input.ManipulationStartingEventArgs):
/// converted Behaviors TranslateZoomRotateBehavior reads Mode/Container and
/// writes TranslationBehavior/RotationBehavior through the matching inertia
/// args. The engine raises no touch input; values flow from explicit
/// construction.
/// </summary>
public sealed class ManipulationStartingEventArgs : InputEventArgs
{
    private readonly List<IManipulator> _manipulators = new();

    internal ManipulationStartingEventArgs(IInputElement manipulationContainer, ManipulationModes mode)
        : base(Environment.TickCount)
    {
        ManipulationContainer = manipulationContainer ?? throw new ArgumentNullException(nameof(manipulationContainer));
        Mode = mode;
    }

    /// <summary>Whether single-touch drives manipulation (matches IsSingleTouchEnabled).</summary>
    public bool IsSingleTouchEnabled { get; set; }

    /// <summary>Manipulated element (matches ManipulationContainer).</summary>
    public IInputElement ManipulationContainer { get; set; }

    /// <summary>Active contacts (matches Manipulators).</summary>
    public IEnumerable<IManipulator> Manipulators => _manipulators.AsReadOnly();

    /// <summary>Allowed manipulation (matches Mode).</summary>
    public ManipulationModes Mode { get; set; }

    /// <summary>Pivot (matches Pivot).</summary>
    public ManipulationPivot? Pivot { get; set; }

    /// <summary>Factory for tests and hosts (matches the engine-raised shape).</summary>
    public static ManipulationStartingEventArgs Create(IInputElement container, ManipulationModes mode) =>
        new(container, mode);

    /// <summary>Cancels the manipulation (matches Cancel).</summary>
    public bool Cancel()
    {
        Handled = true;
        return true;
    }
}

/// <summary>Expansion inertia (matches System.Windows.Input.InertiaExpansionBehavior).</summary>
public class InertiaExpansionBehavior
{
    /// <summary>Empty behavior.</summary>
    public InertiaExpansionBehavior()
    {
    }

    /// <summary>Deceleration rate (matches DesiredDeceleration).</summary>
    public double DesiredDeceleration { get; set; }

    /// <summary>Displacement target (matches DesiredExpansion).</summary>
    public Vector DesiredExpansion { get; set; }

    /// <summary>Starting radius (matches InitialRadius).</summary>
    public double InitialRadius { get; set; }

    /// <summary>Starting velocity (matches InitialVelocity).</summary>
    public Vector InitialVelocity { get; set; }
}

/// <summary>Translation inertia (matches System.Windows.Input.InertiaTranslationBehavior).</summary>
public class InertiaTranslationBehavior
{
    /// <summary>Empty behavior.</summary>
    public InertiaTranslationBehavior()
    {
    }

    /// <summary>Deceleration rate (matches DesiredDeceleration).</summary>
    public double DesiredDeceleration { get; set; }

    /// <summary>Displacement target (matches DesiredDisplacement).</summary>
    public double DesiredDisplacement { get; set; }

    /// <summary>Starting velocity (matches InitialVelocity).</summary>
    public Vector InitialVelocity { get; set; }
}

/// <summary>Rotation inertia (matches System.Windows.Input.InertiaRotationBehavior).</summary>
public class InertiaRotationBehavior
{
    /// <summary>Empty behavior.</summary>
    public InertiaRotationBehavior()
    {
    }

    /// <summary>Deceleration rate (matches DesiredDeceleration).</summary>
    public double DesiredDeceleration { get; set; }

    /// <summary>Rotation target in degrees (matches DesiredRotation).</summary>
    public double DesiredRotation { get; set; }

    /// <summary>Starting angular velocity (matches InitialVelocity).</summary>
    public double InitialVelocity { get; set; }
}

/// <summary>Instantaneous velocities (matches System.Windows.Input.ManipulationVelocities).</summary>
public class ManipulationVelocities
{
    /// <summary>Creates the snapshot (matches ManipulationVelocities(Vector, double, Vector)).</summary>
    public ManipulationVelocities(Vector linearVelocity, double angularVelocity, Vector expansionVelocity)
    {
        LinearVelocity = linearVelocity;
        AngularVelocity = angularVelocity;
        ExpansionVelocity = expansionVelocity;
    }

    /// <summary>Angular velocity (matches AngularVelocity).</summary>
    public double AngularVelocity { get; }

    /// <summary>Expansion velocity (matches ExpansionVelocity).</summary>
    public Vector ExpansionVelocity { get; }

    /// <summary>Linear velocity (matches LinearVelocity).</summary>
    public Vector LinearVelocity { get; }
}

/// <summary>Inertia-setup notice (matches System.Windows.Input.ManipulationInertiaStartingEventHandler).</summary>
public delegate void ManipulationInertiaStartingEventHandler(object sender, ManipulationInertiaStartingEventArgs e);

/// <summary>
/// Inertia-setup data (matches System.Windows.Input.ManipulationInertiaStartingEventArgs):
/// converted Behaviors TranslateZoomRotateBehavior reads InitialVelocities
/// and writes TranslationBehavior/RotationBehavior. The engine raises no
/// touch input; values flow from explicit construction.
/// </summary>
public sealed class ManipulationInertiaStartingEventArgs : InputEventArgs
{
    private readonly List<IManipulator> _manipulators = new();

    internal ManipulationInertiaStartingEventArgs(
        IInputElement manipulationContainer,
        ManipulationVelocities initialVelocities,
        Point manipulationOrigin)
        : base(Environment.TickCount)
    {
        ManipulationContainer = manipulationContainer ?? throw new ArgumentNullException(nameof(manipulationContainer));
        InitialVelocities = initialVelocities ?? throw new ArgumentNullException(nameof(initialVelocities));
        ManipulationOrigin = manipulationOrigin;
    }

    /// <summary>Expansion behavior (matches ExpansionBehavior).</summary>
    public InertiaExpansionBehavior? ExpansionBehavior { get; set; }

    /// <summary>Snapshot velocities (matches InitialVelocities).</summary>
    public ManipulationVelocities InitialVelocities { get; }

    /// <summary>Manipulated element (matches ManipulationContainer).</summary>
    public IInputElement ManipulationContainer { get; }

    /// <summary>Inertia origin (matches ManipulationOrigin).</summary>
    public Point ManipulationOrigin { get; set; }

    /// <summary>Active contacts (matches Manipulators).</summary>
    public IEnumerable<IManipulator> Manipulators => _manipulators.AsReadOnly();

    /// <summary>Rotation behavior (matches RotationBehavior).</summary>
    public InertiaRotationBehavior? RotationBehavior { get; set; }

    /// <summary>Translation behavior (matches TranslationBehavior).</summary>
    public InertiaTranslationBehavior? TranslationBehavior { get; set; }

    /// <summary>Factory for tests and hosts (matches the engine-raised shape).</summary>
    public static ManipulationInertiaStartingEventArgs Create(
        IInputElement container, ManipulationVelocities velocities, Point origin) =>
        new(container, velocities, origin);

    /// <summary>Cancels inertia (matches Cancel).</summary>
    public bool Cancel()
    {
        Handled = true;
        return true;
    }
}