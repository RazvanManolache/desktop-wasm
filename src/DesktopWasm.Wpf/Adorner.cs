namespace DesktopWasm.Wpf;

// Decorator/adorner hosting (System.Windows.Controls.Decorator +
// System.Windows.Documents.Adorner/AdornerLayer, System.Windows.Controls
// AdornerDecorator): single-child hosting with real measure/arrange/paint,
// plus the layer that paints adorners above their adorned elements.
// Driven by the converted ScreenToGif call sites: AdornedControl hosts a
// FrameworkElementAdorner through AdornerLayer.GetAdornerLayer/Add/Remove
// (ShowAdornerInternal/HideAdornerInternal), and Editor/Generic XAML wraps
// crop content in <AdornerDecorator>.

/// <summary>
/// Single-child host (matches System.Windows.Controls.Decorator): the child
/// fills the decorator's slot and paints through it. The child is both the
/// Child property and the Children entry, so XAML attach and code-behind
/// assignment stay in sync.
/// </summary>
public class Decorator : FrameworkElement
{
    private UIElement? _child;

    /// <summary>Hosted child (null clears the host).</summary>
    public virtual UIElement? Child
    {
        get => _child;
        set
        {
            if (ReferenceEquals(_child, value)) return;
            if (_child is not null) RemoveChild(_child);
            _child = value;
            if (value is not null) AddChild(value);
        }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (Child is null) return new WpfSize(0, 0);
        var margin = Child is FrameworkElement element ? element.Margin : new Thickness(0);
        var insetW = margin.Left + margin.Right;
        var insetH = margin.Top + margin.Bottom;
        Child.Measure(new WpfSize(Math.Max(0, available.Width - insetW), Math.Max(0, available.Height - insetH)));
        var s = Child.DesiredSize;
        return new WpfSize(s.Width + insetW, s.Height + insetH);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (Child is null) return;
        var margin = Child is FrameworkElement element ? element.Margin : new Thickness(0);
        Child.Arrange(new WpfRect(
            finalRect.X + margin.Left,
            finalRect.Y + margin.Top,
            Math.Max(0, finalRect.Width - margin.Left - margin.Right),
            Math.Max(0, finalRect.Height - margin.Top - margin.Bottom)));
    }

    protected override void OnRender(DrawingContext dc) => RenderVisible(dc, Child);
}

/// <summary>
/// Element-attached overlay (matches System.Windows.Documents.Adorner):
/// decorates one <see cref="AdornedElement"/> and paints above it when
/// hosted in an <see cref="AdornerLayer"/>. Layout routes through the
/// Size-typed MeasureOverride/ArrangeOverride hooks so converted
/// ScreenToGif adorners (FrameworkElementAdorner position their child
/// there) bind and run.
/// </summary>
public abstract class Adorner : FrameworkElement
{
    /// <summary>Adorned element (must already exist; the layer arranges over its bounds).</summary>
    protected Adorner(UIElement adornedElement)
    {
        AdornedElement = adornedElement;
    }

    /// <summary>Element this adorner decorates.</summary>
    public UIElement AdornedElement { get; }

    /// <summary>Hosting layer (set by Add, cleared by Remove).</summary>
    public AdornerLayer? AdornerLayer { get; private set; }

    internal void SetLayer(AdornerLayer? layer) => AdornerLayer = layer;

    protected override WpfSize MeasureCore(WpfSize available) =>
        MeasureOverride(new Size(Slot(available.Width), Slot(available.Height)));

    protected override void ArrangeCore(WpfRect finalRect) =>
        ArrangeOverride(new Size(Slot(finalRect.Width), Slot(finalRect.Height)));

    /// <summary>Subclasses paint their chrome here (default paints nothing, like WPF).</summary>
    protected override void OnRender(DrawingContext dc) { }

    private static double Slot(double value) =>
        double.IsNaN(value) || value < 0 ? 0 : value;
}

/// <summary>
/// Overlay host (matches System.Windows.Documents.AdornerLayer): adorners
/// added here arrange over their adorned elements' bounds and paint after
/// (above) the content beneath the layer. GetAdornerLayer walks the parent
/// chain to the nearest layer, or to the layer owned by the nearest
/// AdornerDecorator (the Editor crop overlay shape).
/// </summary>
public sealed class AdornerLayer : FrameworkElement
{
    private readonly List<Adorner> _adorners = new();

    /// <summary>Currently hosted adorners (paint order).</summary>
    public IReadOnlyList<Adorner> Adorners => _adorners;

    /// <summary>Hosts an adorner above its adorned element (duplicate adds are no-ops).</summary>
    public void Add(Adorner adorner)
    {
        ArgumentNullException.ThrowIfNull(adorner);
        if (_adorners.Contains(adorner)) return;
        _adorners.Add(adorner);
        AddChild(adorner);
        adorner.SetLayer(this);
        InvalidateVisual();
    }

    /// <summary>Unhosts an adorner (absent adorners are no-ops, like WPF).</summary>
    public void Remove(Adorner adorner)
    {
        ArgumentNullException.ThrowIfNull(adorner);
        if (!_adorners.Remove(adorner)) return;
        RemoveChild(adorner);
        adorner.SetLayer(null);
        InvalidateVisual();
    }

    /// <summary>
    /// Nearest layer above an element (matches GetAdornerLayer): the first
    /// AdornerLayer on the parent chain, or the layer owned by the first
    /// AdornerDecorator there. Null when the element has no layer above it.
    /// </summary>
    public static AdornerLayer? GetAdornerLayer(Visual? visual)
    {
        for (var node = visual as UIElement; node is not null; node = node.Parent)
        {
            if (node is AdornerLayer layer) return layer;
            if (node is AdornerDecorator decorator) return decorator.AdornerLayer;
        }

        return null;
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        foreach (var adorner in _adorners)
            adorner.Measure(available);
        return new WpfSize(0, 0);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        foreach (var adorner in _adorners)
        {
            var bounds = adorner.AdornedElement.RenderRect;
            adorner.Arrange(new WpfRect(
                bounds.X, bounds.Y,
                Math.Max(0, bounds.Width), Math.Max(0, bounds.Height)));
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        foreach (var adorner in _adorners.ToArray())
            RenderVisible(dc, adorner);
    }
}

/// <summary>
/// Decorator with a built-in adorner layer (matches
/// System.Windows.Controls.AdornerDecorator): the Child content paints
/// first and the owned <see cref="AdornerLayer"/> paints after it, so
/// AdornerLayer.GetAdornerLayer resolves from inside the content (the
/// ScreenToGif Editor crop-overlay shape).
/// </summary>
public class AdornerDecorator : Decorator
{
    private readonly AdornerLayer _layer = new();

    public AdornerDecorator()
    {
        AddChild(_layer);
    }

    /// <summary>Layer adorners added under this decorator paint through.</summary>
    public AdornerLayer AdornerLayer => _layer;

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var desired = base.MeasureCore(available);
        _layer.Measure(available);
        return desired;
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        base.ArrangeCore(finalRect);
        _layer.Arrange(new WpfRect(finalRect.X, finalRect.Y,
            Math.Max(0, finalRect.Width), Math.Max(0, finalRect.Height)));
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        RenderVisible(dc, _layer);
    }
}
