using System.Collections;

namespace DesktopWasm.Wpf;

// Hit-test API family (PresentationCore subset, shim, not port): the
// retained-tree queries converted ScreenToGif controls call by name.
// VisualTreeHelper.HitTest walks the live tree (logical children plus
// VisualCollection-backed visual children) through the same
// Visibility/IsHitTestVisible/shape HitTest virtuals the input router
// uses, so callback/filter results agree with pointer routing. Point
// picks return the topmost visual; geometry picks report bounds-based
// intersection detail (not pixel-exact path intersection). Member names,
// signatures and enum values match WPF (verified against the
// Microsoft.WindowsDesktop.App.Ref assemblies); only members with a
// converted call site or a HitTest-signature duty are declared.

/// <summary>Flattening tolerance mode (matches System.Windows.Media.ToleranceType).</summary>
public enum ToleranceType
{
    Absolute,
    Relative,
}

/// <summary>Hit-test walk control (matches System.Windows.Media.HitTestResultBehavior).</summary>
public enum HitTestResultBehavior
{
    Stop,
    Continue,
}

/// <summary>Hit-test filter control (matches System.Windows.Media.HitTestFilterBehavior).</summary>
public enum HitTestFilterBehavior
{
    ContinueSkipSelfAndChildren,
    ContinueSkipChildren = 2,
    ContinueSkipSelf = 4,
    Continue = 6,
    Stop = 8,
}

/// <summary>Bounds intersection kind (matches System.Windows.Media.IntersectionDetail).</summary>
public enum IntersectionDetail
{
    NotCalculated,
    Empty,
    FullyInside,
    FullyContains,
    Intersects,
}

/// <summary>Hit-test callback (matches System.Windows.Media.HitTestResultCallback).</summary>
public delegate HitTestResultBehavior HitTestResultCallback(HitTestResult result);

/// <summary>Hit-test filter (matches System.Windows.Media.HitTestFilterCallback).</summary>
public delegate HitTestFilterBehavior HitTestFilterCallback(DependencyObject potentialHitTestTarget);

/// <summary>Hit-test parameters root (matches System.Windows.Media.HitTestParameters).</summary>
public abstract class HitTestParameters
{
    /// <summary>Restricts construction to the known parameter kinds.</summary>
    protected HitTestParameters()
    {
    }
}

/// <summary>Point hit-test parameters (matches System.Windows.Media.PointHitTestParameters).</summary>
public sealed class PointHitTestParameters : HitTestParameters
{
    /// <summary>Hit-test at a point in the reference visual's coordinates.</summary>
    public PointHitTestParameters(Point hitPoint) => HitPoint = hitPoint;

    /// <summary>Hit point in the reference visual's coordinates.</summary>
    public Point HitPoint { get; }
}

/// <summary>Geometry hit-test parameters (matches System.Windows.Media.GeometryHitTestParameters).</summary>
public sealed class GeometryHitTestParameters : HitTestParameters
{
    /// <summary>Hit-test against a geometry in the reference visual's coordinates.</summary>
    public GeometryHitTestParameters(Geometry geometry) =>
        HitGeometry = geometry ?? throw new ArgumentNullException(nameof(geometry));

    /// <summary>Hit geometry in the reference visual's coordinates.</summary>
    public Geometry HitGeometry { get; }
}

/// <summary>Hit-test result root (matches System.Windows.Media.HitTestResult).</summary>
public abstract class HitTestResult
{
    /// <summary>Hit visual (typed as DependencyObject like WPF).</summary>
    protected HitTestResult(DependencyObject visualHit) =>
        VisualHit = visualHit ?? throw new ArgumentNullException(nameof(visualHit));

    /// <summary>Visual the hit landed on.</summary>
    public DependencyObject VisualHit { get; }
}

/// <summary>Point hit-test result (matches System.Windows.Media.PointHitTestResult).</summary>
public sealed class PointHitTestResult : HitTestResult
{
    /// <summary>Hit visual plus the point that hit it.</summary>
    public PointHitTestResult(Visual visualHit, Point pointHit)
        : base(visualHit) => PointHit = pointHit;

    /// <summary>Hit point in the reference visual's coordinates.</summary>
    public Point PointHit { get; }

    /// <summary>Hit visual (narrows the DependencyObject-typed base member).</summary>
    public new Visual VisualHit => (Visual)base.VisualHit;
}

/// <summary>Geometry hit-test result (matches System.Windows.Media.GeometryHitTestResult).</summary>
public sealed class GeometryHitTestResult : HitTestResult
{
    /// <summary>Hit visual plus how it meets the hit geometry.</summary>
    public GeometryHitTestResult(Visual visualHit, IntersectionDetail intersectionDetail)
        : base(visualHit) => IntersectionDetail = intersectionDetail;

    /// <summary>How the hit visual meets the hit geometry.</summary>
    public IntersectionDetail IntersectionDetail { get; }

    /// <summary>Hit visual (narrows the DependencyObject-typed base member).</summary>
    public new Visual VisualHit => (Visual)base.VisualHit;
}

/// <summary>
/// 3D ray hit-test result (matches System.Windows.Media.Media3D.RayHitTestResult):
/// the Visual3D a pick ray struck and the world-space strike point.
/// (WPF keeps the constructor internal; the engine exposes it so tests and
/// the retained-tree pick can build results. PointHit matches the WPF
/// member; VisualHit narrows the DependencyObject-typed base member.)
/// </summary>
public class RayHitTestResult : HitTestResult
{
    /// <summary>Hit visual plus the world-space strike point.</summary>
    public RayHitTestResult(Visual3D visualHit, Point3D pointHit)
        : base(visualHit)
    {
        VisualHit = visualHit;
        PointHit = pointHit;
    }

    /// <summary>Hit visual (narrows the DependencyObject-typed base member).</summary>
    public new Visual3D VisualHit { get; }

    /// <summary>World-space strike point.</summary>
    public Point3D PointHit { get; }
}

/// <summary>
/// Mesh ray hit-test result (matches
/// System.Windows.Media.Media3D.RayMeshGeometry3DHitTestResult): a
/// RayHitTestResult plus the struck-mesh intersection and the barycentric
/// vertex weights (MeshHit = w1*v1 + w2*v2 + w3*v3). MeshHit is the
/// world-space intersection (transforms applied): the engine's
/// forward-only transforms do not carry the inverses WPF uses for
/// mesh-local reporting. (Constructor visibility matches
/// RayHitTestResult above.)
/// </summary>
public sealed class RayMeshGeometry3DHitTestResult : RayHitTestResult
{
    /// <summary>Hit visual plus the world-space mesh intersection and its barycentric weights.</summary>
    public RayMeshGeometry3DHitTestResult(
        Visual3D visualHit, Point3D meshHit,
        double vertexWeight1, double vertexWeight2, double vertexWeight3)
        : base(visualHit, meshHit)
    {
        MeshHit = meshHit;
        VertexWeight1 = vertexWeight1;
        VertexWeight2 = vertexWeight2;
        VertexWeight3 = vertexWeight3;
    }

    /// <summary>World-space point where the ray struck the mesh.</summary>
    public Point3D MeshHit { get; }

    /// <summary>Barycentric weight of the first triangle vertex.</summary>
    public double VertexWeight1 { get; }

    /// <summary>Barycentric weight of the second triangle vertex.</summary>
    public double VertexWeight2 { get; }

    /// <summary>Barycentric weight of the third triangle vertex.</summary>
    public double VertexWeight3 { get; }
}

/// <summary>
/// Retained child-visual store (matches System.Windows.Media.VisualCollection):
/// converted ScreenToGif adorners (CroppingAdorner, ElementAdorner,
/// ResizingAdorner) back their VisualChildrenCount/GetVisualChild overrides
/// with one of these. Adding parents the child to the owner (re-parenting
/// across owners is an error, like WPF); removal clears the parent link.
/// </summary>
public sealed class VisualCollection : IList, ICollection, IEnumerable
{
    private readonly List<Visual> _items = new();
    private readonly Visual _parent = null!;
    private readonly object _syncRoot = new();

    /// <summary>Hosts visuals under an owner (null owners are an error, like WPF).</summary>
    public VisualCollection(Visual parent) => _parent = parent ?? throw new ArgumentNullException(nameof(parent));

    /// <summary>Host this collection parents children to (kept for diagnostics; WPF exposes no Parent).</summary>
    internal Visual Owner => _parent;

    /// <summary>Child count.</summary>
    public int Count => _items.Count;

    /// <summary>Capacity hint (backed by the list capacity).</summary>
    public int Capacity
    {
        get => _items.Capacity;
        set => _items.Capacity = value;
    }

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => false;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => _syncRoot;

    /// <summary>Child at an index.</summary>
    public Visual this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var old = _items[index];
            if (ReferenceEquals(old, value)) return;
            GuardAdd(value);
            Visual.DetachVisualChild(_parent, old);
            Visual.AdoptVisualChild(_parent, value);
            _items[index] = value;
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = (Visual)value!;
    }

    /// <summary>Appends a child (returns its index, like WPF).</summary>
    public int Add(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        GuardAdd(visual);
        Visual.AdoptVisualChild(_parent, visual);
        _items.Add(visual);
        return _items.Count - 1;
    }

    int IList.Add(object? value) => Add((Visual)value!);

    /// <summary>Removes every child (clears their parent links).</summary>
    public void Clear()
    {
        foreach (var item in _items.ToArray()) Remove(item);
    }

    /// <summary>Whether the child is hosted here.</summary>
    public bool Contains(Visual visual) => visual is not null && _items.Contains(visual);

    bool IList.Contains(object? value) => value is Visual visual && Contains(visual);

    /// <summary>Copies children into an array.</summary>
    public void CopyTo(Visual[] array, int index) => _items.CopyTo(array, index);

    void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    /// <summary>Typed enumerator (matches VisualCollection.Enumerator).</summary>
    public Enumerator GetEnumerator() => new(_items);

    IEnumerator<Visual> AsEnumerable() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <summary>Index of a child (-1 when absent).</summary>
    public int IndexOf(Visual visual) => visual is null ? -1 : _items.IndexOf(visual);

    int IList.IndexOf(object? value) => value is Visual visual ? IndexOf(visual) : -1;

    /// <summary>Inserts a child at an index.</summary>
    public void Insert(int index, Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        GuardAdd(visual);
        Visual.AdoptVisualChild(_parent, visual);
        _items.Insert(index, visual);
    }

    void IList.Insert(int index, object? value) => Insert(index, (Visual)value!);

    /// <summary>Removes a child (absent children are a no-op, like WPF).</summary>
    public void Remove(Visual visual)
    {
        if (visual is null || !_items.Remove(visual)) return;
        Visual.DetachVisualChild(_parent, visual);
    }

    void IList.Remove(object? value)
    {
        if (value is Visual visual) Remove(visual);
    }

    /// <summary>Removes the child at an index.</summary>
    public void RemoveAt(int index)
    {
        var old = _items[index];
        _items.RemoveAt(index);
        Visual.DetachVisualChild(_parent, old);
    }

    /// <summary>Removes an index range.</summary>
    public void RemoveRange(int index, int count)
    {
        foreach (var item in _items.GetRange(index, count).ToArray()) Remove(item);
    }

    private void GuardAdd(Visual visual)
    {
        if (_items.Contains(visual))
            throw new InvalidOperationException(
                $"'{visual.GetType().Name}' is already a child of this collection.");
        if (visual.VisualParent is not null && !ReferenceEquals(visual.VisualParent, _parent))
            throw new InvalidOperationException(
                $"'{visual.GetType().Name}' is already parented to another visual.");
    }

    /// <summary>Visual enumerator (matches System.Windows.Media.VisualCollection/Enumerator).</summary>
    public struct Enumerator : IEnumerator<Visual>
    {
        private List<Visual>.Enumerator _inner;

        internal Enumerator(List<Visual> items) => _inner = items.GetEnumerator();

        /// <summary>Current child.</summary>
        public Visual Current => _inner.Current;

        object IEnumerator.Current => Current;

        /// <summary>Advances to the next child.</summary>
        public bool MoveNext() => _inner.MoveNext();

        /// <summary>Restarts enumeration.</summary>
        public void Reset() => ((IEnumerator)_inner).Reset();

        /// <summary>No-op (no unmanaged state).</summary>
        public void Dispose() => _inner.Dispose();
    }
}

/// <summary>Retained-tree hit testing (extends the engine VisualTreeHelper).</summary>
public static partial class VisualTreeHelper
{
    /// <summary>
    /// Point pick (matches VisualTreeHelper.HitTest(Visual, Point)): the
    /// topmost visual under the point in the reference's coordinates, or
    /// null. Converted recorder windows null-check the result, so misses
    /// stay null instead of throwing.
    /// </summary>
    public static HitTestResult? HitTest(Visual? reference, Point point)
    {
        ArgumentNullException.ThrowIfNull(reference);
        HitTestResult? found = null;
        HitTest(reference, null,
            result =>
            {
                found = result;
                return HitTestResultBehavior.Stop;
            },
            new PointHitTestParameters(point));
        return found;
    }

    /// <summary>
    /// Reference-space origin in absolute coordinates (the engine keeps
    /// absolute RenderRects and no transforms, so one addition lands
    /// reference-local parameters in the shared space).
    /// </summary>
    private static (double X, double Y) OriginOf(Visual? reference) =>
        reference is UIElement element
            ? (element.RenderRect.X, element.RenderRect.Y)
            : (0, 0);

    /// <summary>
    /// Callback walk (matches VisualTreeHelper.HitTest(Visual,
    /// HitTestFilterCallback, HitTestResultCallback, HitTestParameters)):
    /// the filter runs top-down (parents before children) and prunes with
    /// skip/stop; the result callback runs per hit, topmost first.
    /// Converted DrawingCanvas passes a null filter with a
    /// GeometryHitTestParameters rubber band.
    /// </summary>
    public static void HitTest(
        Visual? reference,
        HitTestFilterCallback? filterCallback,
        HitTestResultCallback resultCallback,
        HitTestParameters hitTestParameters)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(resultCallback);
        ArgumentNullException.ThrowIfNull(hitTestParameters);
        var origin = OriginOf(reference);
        var stopped = false;
        switch (hitTestParameters)
        {
            case PointHitTestParameters point:
                var absolute = new Point(point.HitPoint.X + origin.X, point.HitPoint.Y + origin.Y);
                Walk(reference, filterCallback, resultCallback,
                    node => MatchPoint(node, absolute, point.HitPoint), ref stopped);
                break;
            case GeometryHitTestParameters geometry:
                Walk(reference, filterCallback, resultCallback,
                    node => MatchGeometry(node, geometry.HitGeometry, origin), ref stopped);
                break;
            default:
                throw new ArgumentException(
                    $"Unsupported hit-test parameters '{hitTestParameters.GetType().Name}'.",
                    nameof(hitTestParameters));
        }
    }

    private static void Walk(
        DependencyObject node,
        HitTestFilterCallback? filter,
        HitTestResultCallback result,
        Func<DependencyObject, HitTestResult?> match,
        ref bool stopped)
    {
        if (stopped) return;
        var decision = filter?.Invoke(node) ?? HitTestFilterBehavior.Continue;
        if (decision == HitTestFilterBehavior.Stop)
        {
            stopped = true;
            return;
        }
        var skipSelf = decision is HitTestFilterBehavior.ContinueSkipSelf
            or HitTestFilterBehavior.ContinueSkipSelfAndChildren;
        var skipChildren = decision is HitTestFilterBehavior.ContinueSkipChildren
            or HitTestFilterBehavior.ContinueSkipSelfAndChildren;
        if (!skipChildren)
        {
            foreach (var child in VisualChildrenOf(node))
            {
                Walk(child, filter, result, match, ref stopped);
                if (stopped) return;
            }
        }
        if (!skipSelf && !stopped && match(node) is { } hit
            && result(hit) == HitTestResultBehavior.Stop)
            stopped = true;
    }

    private static IEnumerable<Visual> VisualChildrenOf(DependencyObject node)
    {
        var seen = new HashSet<Visual>(ReferenceEqualityComparer.Instance);
        // Visual children first, topmost (last) first; overridden
        // GetVisualChild stores (converted adorners) route through here.
        if (node is Visual visual)
        {
            for (var i = visual.VisualChildCount - 1; i >= 0; i--)
            {
                var child = visual.VisualChildAt(i);
                if (child is not null && seen.Add(child)) yield return child;
            }
        }
        // Logical children cover the engine panels/controls that host
        // through AddChild instead of a VisualCollection.
        if (node is FrameworkElement element)
        {
            for (var i = element.LogicalChildren.Count - 1; i >= 0; i--)
            {
                if (element.LogicalChildren[i] is Visual child && seen.Add(child))
                    yield return child;
            }
        }
    }

    private static HitTestResult? MatchPoint(DependencyObject node, Point absolute, Point original)
    {
        // A Viewport3D never reports a 2D fallback: the 2D point entry
        // unprojects into the camera ray and ray-casts the scene (this is
        // the path converted CoverFlow clicks bind through: the caller keeps
        // only RayMeshGeometry3DHitTestResult and reads MeshHit). Misses
        // stay null, like WPF.
        if (node is Viewport3D viewport)
            return viewport.RayCast(new WpfPoint(absolute.X, absolute.Y));
        if (node is not UIElement element) return null;
        var at = new WpfPoint(absolute.X, absolute.Y);
        return element.HitTest(at) ? new PointHitTestResult(element, original) : null;
    }

    private static HitTestResult? MatchGeometry(DependencyObject node, Geometry geometry, (double X, double Y) origin)
    {
        if (node is not UIElement element) return null;
        if (element.Visibility != Visibility.Visible || !element.IsHitTestVisible) return null;
        var slot = element.RenderRect;
        var rect = new Rect(slot.X, slot.Y, Math.Max(0, slot.Width), Math.Max(0, slot.Height));
        var bounds = geometry.Bounds;
        if (bounds.IsEmpty) return null;
        bounds.Offset(origin.X, origin.Y);
        if (rect.IsEmpty || !bounds.IntersectsWith(rect)) return null;
        // FullyInside: the hit geometry sits inside the visual;
        // FullyContains: the hit geometry swallows the visual.
        var detail = rect.Contains(bounds) ? IntersectionDetail.FullyInside
            : bounds.Contains(rect) ? IntersectionDetail.FullyContains
            : IntersectionDetail.Intersects;
        return new GeometryHitTestResult(element, detail);
    }
}
