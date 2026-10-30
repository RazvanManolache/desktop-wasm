using System.Collections.ObjectModel;

namespace DesktopWasm.Wpf;

// Ink/stylus cluster (PresentationCore System.Windows.Ink + System.Windows.Input
// stylus points, PresentationFramework System.Windows.Controls InkCanvas):
// stylus input capture through the real browser pointer path, stroke
// collection/rendering for the ScreenToGif board/editor workflows
// (DrawingCanvas inking, BoardRecorderViewModel DrawingAttributes/EditingMode,
// LightWindow barrel-button resize). Enum numerics and member shapes were
// verified against the real WPF assemblies (net9.0-windows oracle probe,
// 2026-10-23); engine deviations from WPF inheritance are noted inline.

/// <summary>Pen tip shape (matches System.Windows.Ink.StylusTip; verified: Rectangle=0, Ellipse=1).</summary>
public enum StylusTip
{
    Rectangle = 0,
    Ellipse = 1,
}

/// <summary>
/// Recognizable pen gesture (matches System.Windows.Ink.ApplicationGesture;
/// values verified against WPF: AllGestures=0, NoGesture=61440, the shape
/// gestures 61441+, flicks 61528+, DoubleTap=61681). The engine retains the
/// enabled set (InkCanvas.Get/SetEnabledGestures); recognition itself stays
/// a boundary (no recognizer ships for the browser).
/// </summary>
public enum ApplicationGesture
{
    AllGestures = 0,
    NoGesture = 61440,
    ScratchOut = 61441,
    Triangle = 61442,
    Square = 61443,
    Star = 61444,
    Check = 61445,
    Curlicue = 61456,
    DoubleCurlicue = 61457,
    Circle = 61472,
    DoubleCircle = 61473,
    SemicircleLeft = 61480,
    SemicircleRight = 61481,
    ChevronUp = 61488,
    ChevronDown = 61489,
    ChevronLeft = 61490,
    ChevronRight = 61491,
    ArrowUp = 61496,
    ArrowDown = 61497,
    ArrowLeft = 61498,
    ArrowRight = 61499,
    Up = 61528,
    Down = 61529,
    Left = 61530,
    Right = 61531,
    UpDown = 61536,
    DownUp = 61537,
    LeftRight = 61538,
    RightLeft = 61539,
    UpLeftLong = 61540,
    UpRightLong = 61541,
    DownLeftLong = 61542,
    DownRightLong = 61543,
    UpLeft = 61544,
    UpRight = 61545,
    DownLeft = 61546,
    DownRight = 61547,
    LeftUp = 61548,
    LeftDown = 61549,
    RightUp = 61550,
    RightDown = 61551,
    Exclamation = 61604,
    Tap = 61680,
    DoubleTap = 61681,
}

/// <summary>Eraser nib shape (matches System.Windows.Ink.StylusShape).</summary>
public abstract class StylusShape
{
    /// <summary>Nib width in device-independent pixels.</summary>
    public double Width { get; }

    /// <summary>Nib height in device-independent pixels.</summary>
    public double Height { get; }

    /// <summary>Clockwise nib rotation in degrees.</summary>
    public double Rotation { get; }

    /// <summary>Guards the nib size (matches WPF: dimensions stay finite and non-negative).</summary>
    protected StylusShape(double width, double height, double rotation = 0)
    {
        if (double.IsNaN(width) || double.IsNaN(height) || width < 0 || height < 0)
            throw new ArgumentException("Stylus dimensions must be non-negative numbers.");
        Width = width;
        Height = height;
        Rotation = rotation;
    }
}

/// <summary>Rectangular eraser nib (matches System.Windows.Ink.RectangleStylusShape).</summary>
public sealed class RectangleStylusShape : StylusShape
{
    public RectangleStylusShape(double width, double height)
        : base(width, height)
    {
    }

    public RectangleStylusShape(double width, double height, double rotation)
        : base(width, height, rotation)
    {
    }
}

/// <summary>Elliptical eraser nib (matches System.Windows.Ink.EllipseStylusShape).</summary>
public sealed class EllipseStylusShape : StylusShape
{
    public EllipseStylusShape(double width, double height)
        : base(width, height)
    {
    }

    public EllipseStylusShape(double width, double height, double rotation)
        : base(width, height, rotation)
    {
    }
}

/// <summary>Attribute-bag change (matches System.Windows.Ink.PropertyDataChangedEventArgs).</summary>
public sealed class PropertyDataChangedEventArgs : EventArgs
{
    public PropertyDataChangedEventArgs(Guid propertyDataId, object? newValue)
    {
        PropertyDataId = propertyDataId;
        NewValue = newValue;
    }

    /// <summary>Changed bag entry id.</summary>
    public Guid PropertyDataId { get; }

    /// <summary>New entry value (null after removal).</summary>
    public object? NewValue { get; }
}

/// <summary>Attribute-bag change handler (matches System.Windows.Ink.PropertyDataChangedEventHandler).</summary>
public delegate void PropertyDataChangedEventHandler(object sender, PropertyDataChangedEventArgs e);

/// <summary>
/// Pen attributes (matches System.Windows.Ink.DrawingAttributes): color, nib
/// size/tip, curve fitting, pressure and highlighter behavior plus the
/// custom property bag. Defaults verified against WPF (black, ~2px nib,
/// ellipse tip, no fit/highlighter/pressure-ignore). Every mutation raises
/// <see cref="AttributeChanged"/> like WPF.
/// </summary>
public sealed class DrawingAttributes : ICloneable
{
    /// <summary>Smallest nib dimension WPF accepts (verified).</summary>
    public const double MinWidth = 3.77952755905512E-05;

    /// <summary>Largest nib dimension WPF accepts (verified).</summary>
    public const double MaxWidth = 162329.461417323;

    /// <summary>Smallest nib height WPF accepts (verified).</summary>
    public const double MinHeight = 3.77952755905512E-05;

    /// <summary>Largest nib height WPF accepts (verified).</summary>
    public const double MaxHeight = 162329.461417323;

    private Color _color = Colors.Black;
    private double _width = 2.0031496062992127;
    private double _height = 2.0031496062992127;
    private StylusTip _stylusTip = StylusTip.Ellipse;
    private bool _fitToCurve;
    private bool _ignorePressure;
    private bool _isHighlighter;
    private readonly Dictionary<Guid, object?> _bag = new();

    /// <summary>Ink color (default black).</summary>
    public Color Color
    {
        get => _color;
        set { if (_color.Equals(value)) return; _color = value; OnAttributeChanged(); }
    }

    /// <summary>Nib width (clamped to the WPF min/max like desktop).</summary>
    public double Width
    {
        get => _width;
        set
        {
            var clamped = Math.Min(MaxWidth, Math.Max(MinWidth, value));
            if (_width == clamped) return;
            _width = clamped;
            OnAttributeChanged();
        }
    }

    /// <summary>Nib height (clamped to the WPF min/max like desktop).</summary>
    public double Height
    {
        get => _height;
        set
        {
            var clamped = Math.Min(MaxHeight, Math.Max(MinHeight, value));
            if (_height == clamped) return;
            _height = clamped;
            OnAttributeChanged();
        }
    }

    /// <summary>Nib tip (default ellipse).</summary>
    public StylusTip StylusTip
    {
        get => _stylusTip;
        set { if (_stylusTip == value) return; _stylusTip = value; OnAttributeChanged(); }
    }

    /// <summary>Curve smoothing (default off).</summary>
    public bool FitToCurve
    {
        get => _fitToCurve;
        set { if (_fitToCurve == value) return; _fitToCurve = value; OnAttributeChanged(); }
    }

    /// <summary>Pressure sampling (default honors pressure).</summary>
    public bool IgnorePressure
    {
        get => _ignorePressure;
        set { if (_ignorePressure == value) return; _ignorePressure = value; OnAttributeChanged(); }
    }

    /// <summary>Translucent marker rendering (default opaque).</summary>
    public bool IsHighlighter
    {
        get => _isHighlighter;
        set { if (_isHighlighter == value) return; _isHighlighter = value; OnAttributeChanged(); }
    }

    /// <summary>
    /// Nib transform (identity default; retained, stroke paint reads the
    /// nib size, not the matrix, like the caps/dash retained limits).
    /// </summary>
    public Matrix StylusTipTransform { get; set; } = Matrix.Identity;

    /// <summary>Fires after any attribute or bag mutation.</summary>
    public event PropertyDataChangedEventHandler? AttributeChanged;

    private void OnAttributeChanged() =>
        AttributeChanged?.Invoke(this, new PropertyDataChangedEventArgs(Guid.Empty, null));

    /// <summary>Custom bag entry (matches AddPropertyData).</summary>
    public void AddPropertyData(Guid propertyDataId, object? propertyData)
    {
        _bag[propertyDataId] = propertyData;
        PropertyDataChanged?.Invoke(this, new PropertyDataChangedEventArgs(propertyDataId, propertyData));
    }

    /// <summary>Custom bag removal (matches RemovePropertyData).</summary>
    public void RemovePropertyData(Guid propertyDataId)
    {
        if (!_bag.Remove(propertyDataId)) return;
        PropertyDataChanged?.Invoke(this, new PropertyDataChangedEventArgs(propertyDataId, null));
    }

    /// <summary>Custom bag lookup (matches GetPropertyData).</summary>
    public object? GetPropertyData(Guid propertyDataId) =>
        _bag.TryGetValue(propertyDataId, out var value) ? value : null;

    /// <summary>Custom bag ids (matches GetPropertyDataIds).</summary>
    public Guid[] GetPropertyDataIds() => _bag.Keys.ToArray();

    /// <summary>Custom bag membership (matches ContainsPropertyData).</summary>
    public bool ContainsPropertyData(Guid propertyDataId) => _bag.ContainsKey(propertyDataId);

    /// <summary>Bag change (matches PropertyDataChanged).</summary>
    public event PropertyDataChangedEventHandler? PropertyDataChanged;

    /// <summary>Deep copy (attributes plus the bag).</summary>
    public DrawingAttributes Clone()
    {
        var copy = new DrawingAttributes
        {
            Color = _color,
            StylusTip = _stylusTip,
            FitToCurve = _fitToCurve,
            IgnorePressure = _ignorePressure,
            IsHighlighter = _isHighlighter,
            StylusTipTransform = StylusTipTransform,
        };
        copy._width = _width;
        copy._height = _height;
        foreach (var (id, value) in _bag) copy._bag[id] = value;
        return copy;
    }

    object ICloneable.Clone() => Clone();
}

/// <summary>
/// One digitizer sample (matches System.Windows.Input.StylusPoint layout:
/// X/Y plus pressure; the packet-property surface stays a boundary).
/// </summary>
public struct StylusPoint : IEquatable<StylusPoint>
{
    /// <summary>Widest coordinate WPF accepts (verified).</summary>
    public const double MaxXY = 81164736.2834643;

    /// <summary>Narrowest coordinate WPF accepts (verified).</summary>
    public const double MinXY = -81164736.3212596;

    /// <summary>Logical X.</summary>
    public double X { get; set; }

    /// <summary>Logical Y.</summary>
    public double Y { get; set; }

    /// <summary>Tip pressure in [0, 1] (default 0.5 like desktop).</summary>
    public float PressureFactor { get; set; }

    public StylusPoint(double x, double y, float pressureFactor = 0.5f)
    {
        X = x;
        Y = y;
        PressureFactor = pressureFactor;
    }

    /// <summary>Plain point projection (matches ToPoint).</summary>
    public Point ToPoint() => new(X, Y);

    public bool Equals(StylusPoint other) =>
        X == other.X && Y == other.Y && PressureFactor == other.PressureFactor;

    public override bool Equals(object? obj) => obj is StylusPoint other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, PressureFactor);

    public static bool operator ==(StylusPoint left, StylusPoint right) => left.Equals(right);

    public static bool operator !=(StylusPoint left, StylusPoint right) => !left.Equals(right);
}

/// <summary>
/// Ordered digitizer samples (matches System.Windows.Input.StylusPointCollection:
/// packet descriptions stay a boundary, point data is real). Mutations raise
/// <see cref="Changed"/> like WPF.
/// </summary>
public sealed class StylusPointCollection : Collection<StylusPoint>
{
    public StylusPointCollection()
    {
    }

    public StylusPointCollection(IEnumerable<StylusPoint> stylusPoints)
    {
        ArgumentNullException.ThrowIfNull(stylusPoints);
        foreach (var point in stylusPoints) Add(point);
    }

    public StylusPointCollection(IEnumerable<Point> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        foreach (var point in points) Add(new StylusPoint(point.X, point.Y));
    }

    /// <summary>Fires after any insert/remove/replace/clear.</summary>
    public event EventHandler? Changed;

    /// <summary>Appends another packet run (matches Add(StylusPointCollection)).</summary>
    public void Add(StylusPointCollection stylusPoints)
    {
        ArgumentNullException.ThrowIfNull(stylusPoints);
        foreach (var point in stylusPoints) Add(point);
    }

    /// <summary>Deep copy (matches Clone).</summary>
    public StylusPointCollection Clone() => new(this);

    protected override void InsertItem(int index, StylusPoint item)
    {
        base.InsertItem(index, item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void RemoveItem(int index)
    {
        base.RemoveItem(index);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void SetItem(int index, StylusPoint item)
    {
        base.SetItem(index, item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void ClearItems()
    {
        base.ClearItems();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// One pen run (matches System.Windows.Ink.Stroke): sample points plus the
/// attributes snapshot taken at collection time. Bounds expand by half the
/// nib like desktop; <see cref="GetGeometry"/> builds the spine path the
/// editor combines into clip masks.
/// </summary>
public sealed class Stroke
{
    private DrawingAttributes _drawingAttributes = new();
    private StylusPointCollection _stylusPoints = new();

    public Stroke(StylusPointCollection stylusPoints)
    {
        ArgumentNullException.ThrowIfNull(stylusPoints);
        _stylusPoints = stylusPoints;
    }

    public Stroke(StylusPointCollection stylusPoints, DrawingAttributes drawingAttributes)
    {
        ArgumentNullException.ThrowIfNull(stylusPoints);
        ArgumentNullException.ThrowIfNull(drawingAttributes);
        _stylusPoints = stylusPoints;
        _drawingAttributes = drawingAttributes;
    }

    /// <summary>Pen attributes (replacing raises DrawingAttributesReplaced).</summary>
    public DrawingAttributes DrawingAttributes
    {
        get => _drawingAttributes;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_drawingAttributes, value)) return;
            var old = _drawingAttributes;
            _drawingAttributes = value;
            DrawingAttributesReplaced?.Invoke(this, new DrawingAttributesReplacedEventArgs(old, value));
        }
    }

    /// <summary>Sample points (replacing raises StylusPointsReplaced).</summary>
    public StylusPointCollection StylusPoints
    {
        get => _stylusPoints;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_stylusPoints, value)) return;
            var old = _stylusPoints;
            _stylusPoints = value;
            StylusPointsReplaced?.Invoke(this, new StylusPointsReplacedEventArgs(old, value));
        }
    }

    /// <summary>Spine bounds expanded by half the nib (matches GetBounds).</summary>
    public Rect GetBounds()
    {
        var bounds = Rect.Empty;
        foreach (var point in _stylusPoints) bounds.Union(new Point(point.X, point.Y));
        if (bounds.IsEmpty) return bounds;
        var half = Math.Max(_drawingAttributes.Width, _drawingAttributes.Height) / 2;
        bounds.Inflate(half, half);
        return bounds;
    }

    /// <summary>Spine path (matches GetGeometry; nib outline stays a boundary).</summary>
    public Geometry GetGeometry() => GetGeometry(_drawingAttributes);

    /// <summary>Spine path under explicit attributes (matches GetGeometry(attrs)).</summary>
    public Geometry GetGeometry(DrawingAttributes drawingAttributes)
    {
        ArgumentNullException.ThrowIfNull(drawingAttributes);
        var geometry = new PathGeometry();
        if (_stylusPoints.Count == 0) return geometry;
        var figure = new PathFigure { StartPoint = _stylusPoints[0].ToPoint() };
        if (_stylusPoints.Count > 1)
        {
            var segment = new PolyLineSegment();
            for (var i = 1; i < _stylusPoints.Count; i++) segment.Points.Add(_stylusPoints[i].ToPoint());
            figure.Segments.Add(segment);
        }
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>Spine hit within half the nib (matches HitTest(point)).</summary>
    public bool HitTest(Point point) => HitTest(point, Math.Max(_drawingAttributes.Width, _drawingAttributes.Height));

    /// <summary>Spine hit within a diameter (matches HitTest(point, diameter)).</summary>
    public bool HitTest(Point point, double diameter)
    {
        if (_stylusPoints.Count == 0) return false;
        if (_stylusPoints.Count == 1)
            return Distance(_stylusPoints[0].ToPoint(), point) <= diameter / 2;
        for (var i = 1; i < _stylusPoints.Count; i++)
        {
            if (DistanceToSegment(point, _stylusPoints[i - 1].ToPoint(), _stylusPoints[i].ToPoint()) <= diameter / 2)
                return true;
        }
        return false;
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0) return Distance(p, a);
        var t = Math.Min(1, Math.Max(0, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared));
        return Distance(p, new Point(a.X + t * dx, a.Y + t * dy));
    }

    /// <summary>Deep copy (points plus an attribute clone).</summary>
    public Stroke Clone() => new(_stylusPoints.Clone(), _drawingAttributes.Clone());

    /// <summary>Fires when <see cref="DrawingAttributes"/> is replaced.</summary>
    public event DrawingAttributesReplacedEventHandler? DrawingAttributesReplaced;

    /// <summary>Fires when <see cref="StylusPoints"/> is replaced.</summary>
    public event StylusPointsReplacedEventHandler? StylusPointsReplaced;
}

/// <summary>Attribute replacement (matches System.Windows.Ink.DrawingAttributesReplacedEventArgs).</summary>
public sealed class DrawingAttributesReplacedEventArgs : EventArgs
{
    public DrawingAttributesReplacedEventArgs(DrawingAttributes oldDrawingAttributes, DrawingAttributes newDrawingAttributes)
    {
        OldDrawingAttributes = oldDrawingAttributes;
        NewDrawingAttributes = newDrawingAttributes;
    }

    public DrawingAttributes OldDrawingAttributes { get; }
    public DrawingAttributes NewDrawingAttributes { get; }
}

/// <summary>Attribute replacement handler (matches DrawingAttributesReplacedEventHandler).</summary>
public delegate void DrawingAttributesReplacedEventHandler(object sender, DrawingAttributesReplacedEventArgs e);

/// <summary>Sample replacement (matches System.Windows.Ink.StylusPointsReplacedEventArgs).</summary>
public sealed class StylusPointsReplacedEventArgs : EventArgs
{
    public StylusPointsReplacedEventArgs(StylusPointCollection oldStylusPoints, StylusPointCollection newStylusPoints)
    {
        OldStylusPoints = oldStylusPoints;
        NewStylusPoints = newStylusPoints;
    }

    public StylusPointCollection OldStylusPoints { get; }
    public StylusPointCollection NewStylusPoints { get; }
}

/// <summary>Sample replacement handler (matches StylusPointsReplacedEventHandler).</summary>
public delegate void StylusPointsReplacedEventHandler(object sender, StylusPointsReplacedEventArgs e);

/// <summary>Collection delta (matches System.Windows.Ink.StrokeCollectionChangedEventArgs).</summary>
public sealed class StrokeCollectionChangedEventArgs : EventArgs
{
    public StrokeCollectionChangedEventArgs(StrokeCollection added, StrokeCollection removed)
    {
        Added = added;
        Removed = removed;
    }

    public StrokeCollection Added { get; }
    public StrokeCollection Removed { get; }
}

/// <summary>Collection delta handler (matches StrokeCollectionChangedEventHandler).</summary>
public delegate void StrokeCollectionChangedEventHandler(object sender, StrokeCollectionChangedEventArgs e);

/// <summary>
/// Live stroke set (matches System.Windows.Ink.StrokeCollection): add/remove/
/// replace/clone/bounds with <see cref="StrokesChanged"/> deltas. Batch
/// operations raise one delta (per-item notifications stay quiet inside the
/// batch); ISF persistence (Save/Stream ctor) is absent â€” the browser has no
/// ISF codec, and nothing converted references it.
/// </summary>
public sealed class StrokeCollection : Collection<Stroke>
{
    /// <summary>ISF codec name (matches InkSerializedFormat).</summary>
    public const string InkSerializedFormat = "Ink Serialized Format";

    private bool _silent;

    public StrokeCollection()
    {
    }

    public StrokeCollection(IEnumerable<Stroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        _silent = true;
        try
        {
            foreach (var stroke in strokes) base.Add(stroke);
        }
        finally
        {
            _silent = false;
        }
    }

    private static StrokeCollection Singleton(Stroke stroke)
    {
        var single = new StrokeCollection { _silent = true };
        single.Add(stroke);
        single._silent = false;
        return single;
    }

    /// <summary>Fires after any membership change with the real delta.</summary>
    public event StrokeCollectionChangedEventHandler? StrokesChanged;

    /// <summary>Appends a whole set (matches Add(StrokeCollection)).</summary>
    public void Add(StrokeCollection strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        if (strokes.Count == 0) return;
        _silent = true;
        try
        {
            foreach (var stroke in strokes) base.Add(stroke);
        }
        finally
        {
            _silent = false;
        }
        StrokesChanged?.Invoke(this, new StrokeCollectionChangedEventArgs(strokes, new StrokeCollection()));
    }

    /// <summary>Removes a whole set (matches Remove(StrokeCollection)).</summary>
    public void Remove(StrokeCollection strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        var removed = new StrokeCollection { _silent = true };
        _silent = true;
        try
        {
            foreach (var stroke in strokes.ToArray())
            {
                if (base.Remove(stroke)) removed.Add(stroke);
            }
        }
        finally
        {
            _silent = false;
        }
        removed._silent = false;
        if (removed.Count > 0)
            StrokesChanged?.Invoke(this, new StrokeCollectionChangedEventArgs(new StrokeCollection(), removed));
    }

    /// <summary>Swaps one stroke for a set (matches Replace).</summary>
    public void Replace(Stroke stroke, StrokeCollection strokes)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        ArgumentNullException.ThrowIfNull(strokes);
        var index = IndexOf(stroke);
        if (index < 0) throw new ArgumentException("The stroke is not in the collection.", nameof(stroke));
        var added = new StrokeCollection { _silent = true };
        _silent = true;
        try
        {
            base.RemoveAt(index);
            foreach (var replacement in strokes)
            {
                base.Insert(index++, replacement);
                added.Add(replacement);
            }
        }
        finally
        {
            _silent = false;
        }
        added._silent = false;
        StrokesChanged?.Invoke(this, new StrokeCollectionChangedEventArgs(added, Singleton(stroke)));
    }

    /// <summary>Swaps a set for a set (matches Replace).</summary>
    public void Replace(StrokeCollection oldStrokes, StrokeCollection newStrokes)
    {
        ArgumentNullException.ThrowIfNull(oldStrokes);
        ArgumentNullException.ThrowIfNull(newStrokes);
        var removed = new StrokeCollection { _silent = true };
        var added = new StrokeCollection { _silent = true };
        _silent = true;
        try
        {
            foreach (var stroke in oldStrokes.ToArray())
            {
                if (base.Remove(stroke)) removed.Add(stroke);
            }
            foreach (var stroke in newStrokes)
            {
                base.Add(stroke);
                added.Add(stroke);
            }
        }
        finally
        {
            _silent = false;
        }
        removed._silent = false;
        added._silent = false;
        StrokesChanged?.Invoke(this, new StrokeCollectionChangedEventArgs(added, removed));
    }

    /// <summary>Union of member bounds (matches GetBounds).</summary>
    public Rect GetBounds()
    {
        var bounds = Rect.Empty;
        foreach (var stroke in this) bounds.Union(stroke.GetBounds());
        return bounds;
    }

    /// <summary>Deep copy (matches Clone).</summary>
    public StrokeCollection Clone()
    {
        var copy = new StrokeCollection { _silent = true };
        foreach (var stroke in this) copy.Add(stroke.Clone());
        copy._silent = false;
        return copy;
    }

    protected override void InsertItem(int index, Stroke item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.InsertItem(index, item);
        if (!_silent)
            StrokesChanged?.Invoke(this, new StrokeCollectionChangedEventArgs(Singleton(item), new StrokeCollection()));
    }

    protected override void RemoveItem(int index)
    {
        var removed = this[index];
        base.RemoveItem(index);
        if (!_silent)
            StrokesChanged?.Invoke(this, new StrokeCollectionChangedEventArgs(new StrokeCollection(), Singleton(removed)));
    }

    protected override void SetItem(int index, Stroke item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var removed = this[index];
        base.SetItem(index, item);
        if (!_silent)
            StrokesChanged?.Invoke(this, new StrokeCollectionChangedEventArgs(Singleton(item), Singleton(removed)));
    }

    protected override void ClearItems()
    {
        if (Count == 0) return;
        var removed = new StrokeCollection(this);
        _silent = true;
        try
        {
            base.ClearItems();
        }
        finally
        {
            _silent = false;
        }
        StrokesChanged?.Invoke(this, new StrokeCollectionChangedEventArgs(new StrokeCollection(), removed));
    }
}

/// <summary>
/// Pen behavior (matches System.Windows.Controls.InkCanvasEditingMode;
/// verified: None=0, Ink=1, GestureOnly=2, InkAndGesture=3, Select=4,
/// EraseByPoint=5, EraseByStroke=6).
/// </summary>
public enum InkCanvasEditingMode
{
    None = 0,
    Ink = 1,
    GestureOnly = 2,
    InkAndGesture = 3,
    Select = 4,
    EraseByPoint = 5,
    EraseByStroke = 6,
}

/// <summary>Collected stroke (matches System.Windows.Controls.InkCanvasStrokeCollectedEventArgs).</summary>
public sealed class InkCanvasStrokeCollectedEventArgs : RoutedEventArgs
{
    public InkCanvasStrokeCollectedEventArgs(Stroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        Stroke = stroke;
    }

    /// <summary>The stroke just committed to <see cref="InkCanvas.Strokes"/>.</summary>
    public Stroke Stroke { get; }
}

/// <summary>Collected-stroke handler (matches InkCanvasStrokeCollectedEventHandler).</summary>
public delegate void InkCanvasStrokeCollectedEventHandler(object sender, InkCanvasStrokeCollectedEventArgs e);

/// <summary>Barrel-button state (matches System.Windows.Input.StylusButtonState; verified Up=0, Down=1).</summary>
public enum StylusButtonState
{
    Up = 0,
    Down = 1,
}

/// <summary>
/// Pen barrel button (matches the System.Windows.Input.StylusButton shape
/// converted handlers name: id plus state; the device-owner link stays a
/// boundary â€” the engine has no StylusDevice).
/// </summary>
public sealed class StylusButton
{
    public StylusButton(Guid buttonGuid, StylusButtonState state)
    {
        ButtonGuid = buttonGuid;
        StylusButtonState = state;
    }

    /// <summary>Button id.</summary>
    public Guid ButtonGuid { get; }

    /// <summary>Button state.</summary>
    public StylusButtonState StylusButtonState { get; }
}

/// <summary>Barrel-button handler (matches System.Windows.Input.StylusButtonEventHandler).</summary>
public delegate void StylusButtonEventHandler(object sender, StylusButtonEventArgs e);

/// <summary>
/// Pen surface (matches System.Windows.Controls.InkCanvas): stroke capture
/// from the real routed pointer path, stroke collection/rendering, erasing,
/// selection bookkeeping and the editing-mode contract. Capture runs in the
/// bubble mouse overrides so converted Preview/Mouse handlers (DrawingCanvas,
/// Board) keep firing; the active stroke commits to <see cref="Strokes"/>
/// with a <see cref="DefaultDrawingAttributes"/> snapshot and raises
/// <see cref="StrokeCollected"/> like desktop. Gesture recognition,
/// clipboard verbs and adorner selection visuals stay boundaries.
/// </summary>
public class InkCanvas : FrameworkElement
{
    /// <summary>Collected-stroke token (matches InkCanvas.StrokeCollectedEvent).</summary>
    public static readonly RoutedEvent StrokeCollectedEvent = EventManager.RegisterRoutedEvent(
        "StrokeCollected", RoutingStrategy.Bubble, typeof(InkCanvasStrokeCollectedEventHandler), typeof(InkCanvas));

    /// <summary>Mode-change token (matches InkCanvas.EditingModeChangedEvent).</summary>
    public static readonly RoutedEvent EditingModeChangedEvent = EventManager.RegisterRoutedEvent(
        "EditingModeChanged", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(InkCanvas));

    /// <summary>Active-mode-change token (matches InkCanvas.ActiveEditingModeChangedEvent).</summary>
    public static readonly RoutedEvent ActiveEditingModeChangedEvent = EventManager.RegisterRoutedEvent(
        "ActiveEditingModeChanged", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(InkCanvas));

    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register("Background", typeof(Brush), typeof(InkCanvas),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty StrokesProperty =
        DependencyProperty.Register("Strokes", typeof(StrokeCollection), typeof(InkCanvas),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty DefaultDrawingAttributesProperty =
        DependencyProperty.Register("DefaultDrawingAttributes", typeof(DrawingAttributes), typeof(InkCanvas),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty EditingModeProperty =
        DependencyProperty.Register("EditingMode", typeof(InkCanvasEditingMode), typeof(InkCanvas),
            new FrameworkPropertyMetadata(InkCanvasEditingMode.Ink));

    public static readonly DependencyProperty EditingModeInvertedProperty =
        DependencyProperty.Register("EditingModeInverted", typeof(InkCanvasEditingMode), typeof(InkCanvas),
            new FrameworkPropertyMetadata(InkCanvasEditingMode.EraseByStroke));

    private static readonly DependencyPropertyKey ActiveEditingModeKey =
        DependencyProperty.RegisterReadOnly("ActiveEditingMode", typeof(InkCanvasEditingMode), typeof(InkCanvas),
            new FrameworkPropertyMetadata(InkCanvasEditingMode.Ink));

    /// <summary>Read-only active mode (matches ActiveEditingModeProperty).</summary>
    public static DependencyProperty ActiveEditingModeProperty => ActiveEditingModeKey.DependencyProperty;

    private Brush? _background;
    private StrokeCollection _strokes = new();
    private DrawingAttributes _defaultDrawingAttributes = new();
    private InkCanvasEditingMode _editingMode = InkCanvasEditingMode.Ink;
    private InkCanvasEditingMode _editingModeInverted = InkCanvasEditingMode.EraseByStroke;
    private readonly List<ApplicationGesture> _enabledGestures = [ApplicationGesture.AllGestures];
    private readonly HashSet<Stroke> _selection = new();
    private readonly UIElementCollection _children;
    private StylusPointCollection? _activeStroke;

    public InkCanvas()
    {
        _children = new UIElementCollection(this);
        _strokes.StrokesChanged += (_, _) => { InvalidateMeasure(); InvalidateVisual(); };
        SetValue(ActiveEditingModeKey, InkCanvasEditingMode.Ink);
    }

    /// <summary>Fill brush (null keeps the transparent default; solid brushes repaint).</summary>
    public Brush? Background
    {
        get => _background;
        set { _background = value; InvalidateVisual(); }
    }

    /// <summary>Committed strokes (replacing rebinds change routing and raises StrokesReplaced).</summary>
    public StrokeCollection Strokes
    {
        get => _strokes;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_strokes, value)) return;
            _strokes.StrokesChanged -= OnStrokesChanged;
            _strokes = value;
            _strokes.StrokesChanged += OnStrokesChanged;
            _selection.RemoveWhere(s => !_strokes.Contains(s));
            StrokesReplaced?.Invoke(this, EventArgs.Empty);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private void OnStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        _selection.RemoveWhere(s => !_strokes.Contains(s));
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Attributes stamped onto the next collected stroke.</summary>
    public DrawingAttributes DefaultDrawingAttributes
    {
        get => _defaultDrawingAttributes;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_defaultDrawingAttributes, value)) return;
            var old = _defaultDrawingAttributes;
            _defaultDrawingAttributes = value;
            DefaultDrawingAttributesReplaced?.Invoke(this, new DrawingAttributesReplacedEventArgs(old, value));
            InvalidateVisual();
        }
    }

    /// <summary>Eraser nib (default 10x10 rectangle like the ScreenToGif default).</summary>
    public StylusShape EraserShape { get; set; } = new RectangleStylusShape(10, 10);

    /// <summary>Pen behavior (changing refreshes the active mode and raises EditingModeChanged).</summary>
    public InkCanvasEditingMode EditingMode
    {
        get => _editingMode;
        set
        {
            if (_editingMode == value) return;
            _editingMode = value;
            SyncActiveEditingMode();
            var args = new RoutedEventArgs { RoutedEvent = EditingModeChangedEvent, Source = this };
            EditingModeChanged?.Invoke(this, args);
            if (!args.Handled) RaiseEvent(args);
        }
    }

    /// <summary>Inverted-pen behavior (retained; the active mode follows it when set).</summary>
    public InkCanvasEditingMode EditingModeInverted
    {
        get => _editingModeInverted;
        set
        {
            if (_editingModeInverted == value) return;
            _editingModeInverted = value;
            SyncActiveEditingMode();
        }
    }

    /// <summary>Effective mode (follows EditingMode; the inverted switch stays a boundary).</summary>
    public InkCanvasEditingMode ActiveEditingMode => (InkCanvasEditingMode)(GetValue(ActiveEditingModeProperty) ?? InkCanvasEditingMode.Ink);

    private void SyncActiveEditingMode()
    {
        var active = _editingMode;
        if ((InkCanvasEditingMode)(GetValue(ActiveEditingModeProperty) ?? active) == active) return;
        SetValue(ActiveEditingModeKey, active);
        var args = new RoutedEventArgs { RoutedEvent = ActiveEditingModeChangedEvent, Source = this };
        ActiveEditingModeChanged?.Invoke(this, args);
        if (!args.Handled) RaiseEvent(args);
    }

    /// <summary>Custom cursor (retained; cursor rendering stays a boundary).</summary>
    public bool UseCustomCursor { get; set; }

    /// <summary>Selection dragging (retained).</summary>
    public bool MoveEnabled { get; set; } = true;

    /// <summary>Selection resizing (retained).</summary>
    public bool ResizeEnabled { get; set; } = true;

    /// <summary>Hosted visuals (live: adding parents the child like WPF).</summary>
    public new UIElementCollection Children => _children;

    /// <summary>Collected stroke (CLR then routed, like WPF).</summary>
    public event InkCanvasStrokeCollectedEventHandler? StrokeCollected;

    /// <summary>Stroke-set replacement.</summary>
    public event EventHandler? StrokesReplaced;

    /// <summary>Default-attributes replacement.</summary>
    public event DrawingAttributesReplacedEventHandler? DefaultDrawingAttributesReplaced;

    /// <summary>Mode change (CLR then routed).</summary>
    public event RoutedEventHandler? EditingModeChanged;

    /// <summary>Active-mode change (CLR then routed).</summary>
    public event RoutedEventHandler? ActiveEditingModeChanged;

    /// <summary>Selection change.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Enabled gestures (live list; recognition stays a boundary).</summary>
    public IReadOnlyList<ApplicationGesture> GetEnabledGestures() => _enabledGestures.ToArray();

    /// <summary>Replaces the enabled gesture set (matches SetEnabledGestures).</summary>
    public void SetEnabledGestures(IEnumerable<ApplicationGesture> gestures)
    {
        ArgumentNullException.ThrowIfNull(gestures);
        _enabledGestures.Clear();
        _enabledGestures.AddRange(gestures);
    }

    /// <summary>Currently selected strokes (matches GetSelectedStrokes).</summary>
    public StrokeCollection GetSelectedStrokes()
    {
        var selected = new StrokeCollection();
        foreach (var stroke in _strokes)
        {
            if (_selection.Contains(stroke)) selected.Add(stroke);
        }
        return selected;
    }

    /// <summary>Selection bounds (empty when nothing is selected).</summary>
    public Rect GetSelectionBounds() => GetSelectedStrokes().GetBounds();

    /// <summary>Selects strokes (matches Select; unknown members are ignored like WPF).</summary>
    public void Select(StrokeCollection strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        _selection.Clear();
        foreach (var stroke in strokes)
        {
            if (_strokes.Contains(stroke)) _selection.Add(stroke);
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Selects hosted elements (retained: clears the stroke selection).</summary>
    public void Select(IEnumerable<UIElement> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        _selection.Clear();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Canvas-top attach (matches InkCanvas.Top).</summary>
    public static double GetTop(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetAttached("InkCanvas.Top") as double? ?? 0;
    }

    /// <summary>Canvas-top attach (matches InkCanvas.SetTop).</summary>
    public static void SetTop(UIElement element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetAttached("InkCanvas.Top", value);
    }

    /// <summary>Canvas-bottom attach (matches InkCanvas.Bottom).</summary>
    public static double GetBottom(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetAttached("InkCanvas.Bottom") as double? ?? 0;
    }

    /// <summary>Canvas-bottom attach (matches InkCanvas.SetBottom).</summary>
    public static void SetBottom(UIElement element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetAttached("InkCanvas.Bottom", value);
    }

    /// <summary>Canvas-left attach (matches InkCanvas.Left).</summary>
    public static double GetLeft(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetAttached("InkCanvas.Left") as double? ?? 0;
    }

    /// <summary>Canvas-left attach (matches InkCanvas.SetLeft).</summary>
    public static void SetLeft(UIElement element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetAttached("InkCanvas.Left", value);
    }

    /// <summary>Canvas-right attach (matches InkCanvas.Right).</summary>
    public static double GetRight(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetAttached("InkCanvas.Right") as double? ?? 0;
    }

    /// <summary>Canvas-right attach (matches InkCanvas.SetRight).</summary>
    public static void SetRight(UIElement element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetAttached("InkCanvas.Right", value);
    }

    /// <summary>Whether the point collector is running (test seam).</summary>
    internal bool IsCollecting => _activeStroke is not null;

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed) return;
        var at = e.GetPosition(this);
        switch (ActiveEditingMode)
        {
            case InkCanvasEditingMode.Ink:
            case InkCanvasEditingMode.InkAndGesture:
                _activeStroke = new StylusPointCollection { new StylusPoint(at.X, at.Y) };
                break;
            case InkCanvasEditingMode.EraseByPoint:
            case InkCanvasEditingMode.EraseByStroke:
                EraseAt(at);
                _activeStroke = new StylusPointCollection { new StylusPoint(at.X, at.Y) };
                break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_activeStroke is null) return;
        var at = e.GetPosition(this);
        switch (ActiveEditingMode)
        {
            case InkCanvasEditingMode.Ink:
            case InkCanvasEditingMode.InkAndGesture:
                _activeStroke.Add(new StylusPoint(at.X, at.Y));
                InvalidateVisual();
                break;
            case InkCanvasEditingMode.EraseByPoint:
            case InkCanvasEditingMode.EraseByStroke:
                _activeStroke.Add(new StylusPoint(at.X, at.Y));
                EraseAt(at);
                break;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_activeStroke is null || e.ChangedButton != MouseButton.Left) return;
        var points = _activeStroke;
        _activeStroke = null;
        switch (ActiveEditingMode)
        {
            case InkCanvasEditingMode.Ink:
            case InkCanvasEditingMode.InkAndGesture:
                if (points.Count == 0) break;
                var stroke = new Stroke(points, _defaultDrawingAttributes.Clone());
                _strokes.Add(stroke);
                var args = new InkCanvasStrokeCollectedEventArgs(stroke) { RoutedEvent = StrokeCollectedEvent, Source = this };
                StrokeCollected?.Invoke(this, args);
                if (!args.Handled) RaiseEvent(args);
                break;
            default:
                InvalidateVisual();
                break;
        }
    }

    private void EraseAt(Point at)
    {
        if (ActiveEditingMode == InkCanvasEditingMode.EraseByStroke)
        {
            for (var i = _strokes.Count - 1; i >= 0; i--)
            {
                if (_strokes[i].HitTest(at, Math.Max(EraserShape.Width, EraserShape.Height)))
                    _strokes.RemoveAt(i);
            }
            return;
        }
        var eraser = Math.Max(EraserShape.Width, EraserShape.Height);
        var radius = eraser / 2;
        foreach (var stroke in _strokes.ToArray())
        {
            var kept = stroke.StylusPoints
                .Where(p => Distance(p.X, p.Y, at.X, at.Y) > radius)
                .Select(p => new StylusPoint(p.X, p.Y, p.PressureFactor))
                .ToArray();
            if (kept.Length == stroke.StylusPoints.Count) continue;
            _strokes.Remove(stroke);
            foreach (var run in SplitRuns(kept, eraser))
                _strokes.Add(new Stroke(new StylusPointCollection(run), stroke.DrawingAttributes.Clone()));
        }
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static IEnumerable<StylusPoint[]> SplitRuns(StylusPoint[] kept, double eraser)
    {
        var run = new List<StylusPoint>();
        foreach (var point in kept)
        {
            if (run.Count == 0 || Distance(run[^1].X, run[^1].Y, point.X, point.Y) <= eraser)
            {
                run.Add(point);
                continue;
            }
            if (run.Count > 0) yield return run.ToArray();
            run = [point];
        }
        if (run.Count > 0) yield return run.ToArray();
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var width = 0.0;
        var height = 0.0;
        foreach (UIElement child in _children)
        {
            child.Measure(available);
            width = Math.Max(width, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }
        var bounds = _strokes.GetBounds();
        if (!bounds.IsEmpty)
        {
            width = Math.Max(width, bounds.Right);
            height = Math.Max(height, bounds.Bottom);
        }
        return new WpfSize(
            double.IsNaN(Width) ? Math.Min(available.Width, width) : Width,
            double.IsNaN(Height) ? Math.Min(available.Height, height) : Height);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        foreach (UIElement child in _children)
        {
            var x = finalRect.X + GetLeft(child);
            var y = finalRect.Y + GetTop(child);
            child.Arrange(new WpfRect(x, y, child.DesiredSize.Width, child.DesiredSize.Height));
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (_background is SolidColorBrush fill && r.Width > 0 && r.Height > 0)
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, fill.ToRgba());
        foreach (var stroke in _strokes) PaintStroke(dc, stroke);
        if (_activeStroke is not null && _activeStroke.Count > 1 &&
            (ActiveEditingMode is InkCanvasEditingMode.Ink or InkCanvasEditingMode.InkAndGesture))
            PaintLiveStroke(dc, _activeStroke, _defaultDrawingAttributes, r);
        foreach (UIElement child in _children) RenderVisible(dc, child);
    }

    private void PaintStroke(DrawingContext dc, Stroke stroke)
    {
        if (stroke.StylusPoints.Count == 0) return;
        var origin = RenderRect;
        if (stroke.StylusPoints.Count == 1)
        {
            var dot = stroke.StylusPoints[0].ToPoint();
            var half = (float)(Math.Max(stroke.DrawingAttributes.Width, stroke.DrawingAttributes.Height) / 2);
            dc.DrawEllipse((float)(origin.X + dot.X) - half, (float)(origin.Y + dot.Y) - half,
                half * 2, half * 2, 0, 0, StrokeRgba(stroke.DrawingAttributes));
            return;
        }
        PaintLiveStroke(dc, stroke.StylusPoints, stroke.DrawingAttributes, origin);
    }

    private void PaintLiveStroke(DrawingContext dc, StylusPointCollection points, DrawingAttributes attributes, WpfRect origin)
    {
        var rgba = StrokeRgba(attributes);
        var width = (float)Math.Max(1, attributes.Width);
        for (var i = 1; i < points.Count; i++)
            dc.DrawLine((float)(origin.X + points[i - 1].X), (float)(origin.Y + points[i - 1].Y),
                (float)(origin.X + points[i].X), (float)(origin.Y + points[i].Y), width, rgba);
    }

    private static uint StrokeRgba(DrawingAttributes attributes)
    {
        var brush = new SolidColorBrush(attributes.Color);
        var rgba = brush.ToRgba();
        if (!attributes.IsHighlighter) return rgba;
        var alpha = (byte)(((rgba >> 24) & 0xFF) / 2);
        return (rgba & 0x00FFFFFF) | ((uint)alpha << 24);
    }
}

/// <summary>
/// Barrel-button press (matches System.Windows.Input.StylusButtonEventArgs:
/// the button plus the routed timestamp; the StylusDevice link stays a
/// boundary â€” the engine raises this from the real pointer path, where a
/// pen barrel press arrives as the non-left button).
/// </summary>
public class StylusButtonEventArgs : InputEventArgs
{
    internal StylusButtonEventArgs(StylusButton stylusButton, int timestamp)
        : base(timestamp) => StylusButton = stylusButton;

    /// <summary>Pressed barrel button.</summary>
    public StylusButton StylusButton { get; }
}
