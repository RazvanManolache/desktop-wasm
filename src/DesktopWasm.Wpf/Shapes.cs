using System.Reflection;

namespace DesktopWasm.Wpf;

// Shape + decorator elements (PresentationFramework subset, shim, not port):
// Path (geometry stroke/fill through the retained DrawGeometry op),
// Rectangle, Viewbox (single-child uniform fitter), ContentPresenter (text
// host for templates), Span/Run (text-inline holders the loader folds into
// TextBlock text) and NavigationWindow (Page host for StartupUri pages, the
// WPF behavior Geometry relies on). Names match real WPF so converted
// original IL binds.

/// <summary>
/// Shape root (matches System.Windows.Shapes.Shape): fill/stroke/stretch
/// stroke-style contract plus the abstract DefiningGeometry every Shape
/// (Path, Rectangle, Ellipse, Line, Polyline, converted originals like
/// ScreenToGif's Arrow) renders through the retained DrawGeometry op.
/// Stretch is parsed and retained; the retained paint uses absolute geometry
/// coordinates (uniform scaling needs a geometry-transform op, the
/// documented next step for the stretch family).
/// </summary>
public abstract class Shape : FrameworkElement
{
    /// <summary>Interior identity (matches System.Windows.Shapes.Shape.FillProperty).</summary>
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register("Fill", typeof(Brush), typeof(Shape),
            new FrameworkPropertyMetadata(null) { AffectsRender = true });

    /// <summary>Interior brush (null paints nothing; matches Shape.Fill).</summary>
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Outline identity (matches
    /// System.Windows.Shapes.Shape.StrokeProperty; the WPF-identical shared
    /// instance, so converted template bindings and setters bind; the CLR
    /// wrapper stays store-backed so factory-applied values paint).</summary>
    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register("Stroke", typeof(Brush), typeof(Shape),
            new FrameworkPropertyMetadata(null) { AffectsRender = true });

    /// <summary>Outline brush (null paints no outline; matches Shape.Stroke;
    /// store-backed so template/style setters land through the DP store).</summary>
    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Outline-width identity (matches
    /// System.Windows.Shapes.Shape.StrokeThicknessProperty; 1 like WPF).</summary>
    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register("StrokeThickness", typeof(double), typeof(Shape),
            new FrameworkPropertyMetadata(1.0) { AffectsRender = true });

    /// <summary>Outline width (matches Shape.StrokeThickness; 1 like WPF;
    /// store-backed so template/style setters land through the DP store).</summary>
    public double StrokeThickness
    {
        get => (double)(GetValue(StrokeThicknessProperty) ?? 1.0);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>Geometry-fit identity (matches System.Windows.Shapes.Shape.StretchProperty; retained).</summary>
    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register("Stretch", typeof(Stretch), typeof(Shape),
            new FrameworkPropertyMetadata(Stretch.None));

    /// <summary>Geometry fit (matches Shape.Stretch; None like WPF; retained).</summary>
    public Stretch Stretch
    {
        get => (Stretch?)GetValue(StretchProperty) ?? Stretch.None;
        set => SetValue(StretchProperty, value);
    }

    /// <summary>Start cap (matches Shape.StrokeStartLineCap; Flat like WPF; retained).</summary>
    public PenLineCap StrokeStartLineCap { get; set; } = PenLineCap.Flat;

    /// <summary>End cap (matches Shape.StrokeEndLineCap; Flat like WPF; retained).</summary>
    public PenLineCap StrokeEndLineCap { get; set; } = PenLineCap.Flat;

    /// <summary>Dash cap (matches Shape.StrokeDashCap; Flat like WPF; retained).</summary>
    public PenLineCap StrokeDashCap { get; set; } = PenLineCap.Flat;

    /// <summary>Join (matches Shape.StrokeLineJoin; Miter like WPF; retained).</summary>
    public PenLineJoin StrokeLineJoin { get; set; } = PenLineJoin.Miter;

    /// <summary>Miter limit (matches Shape.StrokeMiterLimit; 10 like WPF; retained).</summary>
    public double StrokeMiterLimit { get; set; } = 10.0;

    /// <summary>Dash pattern (matches Shape.StrokeDashArray; null is solid like WPF; retained).</summary>
    public DoubleCollection? StrokeDashArray { get; set; }

    /// <summary>Dash offset (matches Shape.StrokeDashOffset; retained).</summary>
    public double StrokeDashOffset { get; set; }

    /// <summary>Rendered geometry (matches Shape.DefiningGeometry).</summary>
    protected abstract Geometry? DefiningGeometry { get; }

    /// <summary>Final rendered geometry (matches
    /// System.Windows.Shapes.Shape.RenderedGeometry, verified against
    /// dotnet/wpf: public virtual, returning the rendered copy of the
    /// defining geometry). The converted HandyControl Expression shapes
    /// (PrimitiveShape/Arc) inherit this slot for their converted IShape
    /// interface map, which names get_RenderedGeometry (staged-bin
    /// Mono.Cecil evidence: without the slot the converted Arc fails to
    /// load with "Method 'get_RenderedGeometry' does not have an
    /// implementation"). The base returns the defining geometry (empty when
    /// null); converted overrides replace it.</summary>
    public virtual Geometry RenderedGeometry => DefiningGeometry ?? Geometry.Empty;

    /// <summary>Measures the defining geometry plus the outline width.</summary>
    protected WpfSize MeasureGeometry(WpfSize available, Geometry? geometry)
    {
        if (geometry is null) return new WpfSize(0, 0);
        var bounds = geometry.GetBounds();
        if (bounds.IsEmpty) return new WpfSize(0, 0);
        var stroke = Stroke is not null ? StrokeThickness : 0;
        return new WpfSize(
            Math.Max(0, bounds.Width + stroke),
            Math.Max(0, bounds.Height + stroke));
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        // Geometry coordinates are absolute (a Shape carries no offset of
        // its own); RenderRect positions the paint via the transform below.
    }

    protected override void OnRender(DrawingContext dc)
    {
        var geometry = DefiningGeometry;
        if (geometry is null) return;
        PaintGeometry(dc, geometry);
    }

    /// <summary>Paints one geometry with this shape's fill/stroke.</summary>
    protected void PaintGeometry(DrawingContext dc, Geometry geometry)
    {
        var data = geometry.ToPathData();
        if (string.IsNullOrWhiteSpace(data)) return;
        var fill = Fill is null ? 0x00000000u : BrushRender.ResolveFill(Fill);
        var stroke = Stroke is null ? null : (uint?)BrushRender.ResolveFill(Stroke);
        var r = RenderRect;
        dc.PushTransform(1, 0, 0, 1, (float)r.X, (float)r.Y);
        try
        {
            var evenOdd = geometry switch
            {
                GeometryGroup group => group.FillRule == FillRule.EvenOdd,
                PathGeometry path => path.FillRule == FillRule.EvenOdd,
                StreamGeometry stream => stream.FillRule == FillRule.EvenOdd,
                _ => false,
            };
            dc.DrawGeometry(data, fill, stroke, (float)StrokeThickness, evenOdd ? "evenodd" : "nonzero");
        }
        finally
        {
            dc.PopTransform();
        }
    }
}

/// <summary>Geometry shape (matches System.Windows.Shapes.Path).</summary>
public class Path : Shape
{
    /// <summary>Rendered-geometry identity (matches System.Windows.Shapes.Path.DataProperty).</summary>
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register("Data", typeof(Geometry), typeof(Path),
            new FrameworkPropertyMetadata(null) { AffectsMeasure = true });

    /// <summary>Rendered geometry (null draws nothing; changes re-measure).</summary>
    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Geometry? DefiningGeometry => Data;

    protected override WpfSize MeasureCore(WpfSize available) => MeasureGeometry(available, Data);
}

/// <summary>Rectangle shape (matches System.Windows.Shapes.Rectangle).</summary>
public class Rectangle : Shape
{
    /// <summary>Corner-radius identity (matches Rectangle.RadiusXProperty;
    /// store-backed so converted RectangleAttach.OnCircularChanged binds it
    /// through a MultiBinding).</summary>
    public static readonly DependencyProperty RadiusXProperty =
        DependencyProperty.Register("RadiusX", typeof(double), typeof(Rectangle),
            new PropertyMetadata(0.0));

    /// <summary>Corner radius (paint-only change).</summary>
    public double RadiusX
    {
        get => (double)(GetValue(RadiusXProperty) ?? 0.0);
        set => SetValue(RadiusXProperty, value);
    }

    /// <summary>Corner-radius identity (matches Rectangle.RadiusYProperty;
    /// store-backed like RadiusX).</summary>
    public static readonly DependencyProperty RadiusYProperty =
        DependencyProperty.Register("RadiusY", typeof(double), typeof(Rectangle),
            new PropertyMetadata(0.0));

    /// <summary>Corner radius (paint-only change).</summary>
    public double RadiusY
    {
        get => (double)(GetValue(RadiusYProperty) ?? 0.0);
        set => SetValue(RadiusYProperty, value);
    }

    protected override Geometry? DefiningGeometry => new RectangleGeometry
    {
        Rect = new Rect(0, 0, Math.Max(0, RenderRect.Width), Math.Max(0, RenderRect.Height)),
    };

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var width = double.IsNaN(Width)
            ? Math.Min(available.Width, 0) : Width;
        var height = double.IsNaN(Height)
            ? Math.Min(available.Height, 0) : Height;
        return new WpfSize(Math.Max(0, width), Math.Max(0, height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        var fill = BrushRender.ResolveFill(Fill, 0x00000000);
        if (RadiusX > 0 || RadiusY > 0)
            dc.DrawRoundedRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height,
                (float)Math.Max(RadiusX, RadiusY), fill);
        else
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, fill);
        if (Stroke is not null && StrokeThickness > 0)
        {
            var ink = BrushRender.ResolveFill(Stroke);
            var t = (float)Math.Min(StrokeThickness, Math.Min(r.Width, r.Height) / 2);
            if (t <= 0) return;
            if (RadiusX > 0 || RadiusY > 0)
            {
                dc.DrawRoundedRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height,
                    (float)Math.Max(RadiusX, RadiusY), ink);
                dc.DrawRoundedRectangle((float)(r.X + t), (float)(r.Y + t),
                    (float)Math.Max(0, r.Width - 2 * t), (float)Math.Max(0, r.Height - 2 * t),
                    (float)Math.Max(0, Math.Max(RadiusX, RadiusY) - t), fill);
            }
            else
            {
                dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, ink);
                dc.DrawRectangle((float)(r.X + t), (float)(r.Y + t),
                    (float)Math.Max(0, r.Width - 2 * t), (float)Math.Max(0, r.Height - 2 * t), fill);
            }
        }
    }
}

/// <summary>Ellipse shape (matches System.Windows.Shapes.Ellipse).</summary>
public class Ellipse : Shape
{
    protected override Geometry? DefiningGeometry => new EllipseGeometry
    {
        Center = new Point(Math.Max(0, RenderRect.Width) / 2, Math.Max(0, RenderRect.Height) / 2),
        RadiusX = Math.Max(0, RenderRect.Width) / 2,
        RadiusY = Math.Max(0, RenderRect.Height) / 2,
    };

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var width = double.IsNaN(Width)
            ? Math.Min(available.Width, 0) : Width;
        var height = double.IsNaN(Height)
            ? Math.Min(available.Height, 0) : Height;
        return new WpfSize(Math.Max(0, width), Math.Max(0, height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        var x = (float)r.X;
        var y = (float)r.Y;
        var w = (float)r.Width;
        var h = (float)r.Height;
        if (Stroke is not null && StrokeThickness > 0)
            dc.DrawEllipse(x, y, w, h, (float)StrokeThickness,
                BrushRender.ResolveFill(Stroke),
                Fill is null ? null : BrushRender.ResolveFill(Fill));
        else if (Fill is not null)
            dc.DrawEllipse(x, y, w, h, 0, 0x00000000u, BrushRender.ResolveFill(Fill));
    }

    public override bool HitTest(WpfPoint point)
    {
        if (!base.HitTest(point)) return false;
        var rx = RenderRect.Width / 2;
        var ry = RenderRect.Height / 2;
        if (rx <= 0 || ry <= 0) return false;
        var dx = (point.X - RenderRect.X - rx) / rx;
        var dy = (point.Y - RenderRect.Y - ry) / ry;
        return dx * dx + dy * dy <= 1.0;
    }
}

/// <summary>Line shape (matches System.Windows.Shapes.Line).</summary>
public class Line : Shape
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }

    protected override Geometry? DefiningGeometry => new LineGeometry
    {
        StartPoint = new Point(X1, Y1),
        EndPoint = new Point(X2, Y2),
    };

    protected override WpfSize MeasureCore(WpfSize available) => MeasureGeometry(available, DefiningGeometry);
}

/// <summary>Polyline shape (matches System.Windows.Shapes.Polyline).</summary>
public class Polyline : Shape
{
    /// <summary>Vertices (matches Polyline.Points; empty draws nothing like WPF).</summary>
    public PointCollection Points { get; set; } = new();

    /// <summary>Fill rule (matches Polyline.FillRule; EvenOdd like WPF).</summary>
    public FillRule FillRule { get; set; } = FillRule.EvenOdd;

    protected override Geometry? DefiningGeometry
    {
        get
        {
            if (Points.Count == 0) return null;
            var figure = new PathFigure { StartPoint = Points[0] };
            var segment = new PolyLineSegment();
            for (var i = 1; i < Points.Count; i++) segment.Points.Add(Points[i]);
            figure.Segments.Add(segment);
            var geometry = new PathGeometry { Figures = { figure } };
            geometry.FillRule = FillRule;
            return geometry;
        }
    }

    protected override WpfSize MeasureCore(WpfSize available) => MeasureGeometry(available, DefiningGeometry);
}

/// <summary>Polygon shape (matches System.Windows.Shapes.Polygon): a closed
/// polyline (the DataGrid validation template draws its marker this way).</summary>
public class Polygon : Shape
{
    /// <summary>Vertices (matches Polygon.Points; empty draws nothing like WPF).</summary>
    public PointCollection Points { get; set; } = new();

    /// <summary>Fill rule (matches Polygon.FillRule; EvenOdd like WPF).</summary>
    public FillRule FillRule { get; set; } = FillRule.EvenOdd;

    protected override Geometry? DefiningGeometry
    {
        get
        {
            if (Points.Count == 0) return null;
            var figure = new PathFigure { StartPoint = Points[0], IsClosed = true };
            var segment = new PolyLineSegment();
            for (var i = 1; i < Points.Count; i++) segment.Points.Add(Points[i]);
            figure.Segments.Add(segment);
            var geometry = new PathGeometry { Figures = { figure } };
            geometry.FillRule = FillRule;
            return geometry;
        }
    }

    protected override WpfSize MeasureCore(WpfSize available) => MeasureGeometry(available, DefiningGeometry);
}

/// <summary>Scale direction (matches System.Windows.Controls.StretchDirection).</summary>
public enum StretchDirection
{
    UpOnly,
    DownOnly,
    Both,
}

/// <summary>Single-child scaler (matches System.Windows.Controls.Viewbox).</summary>
public class Viewbox : FrameworkElement
{
    /// <summary>Fit rule (parsed; single-child scale layout is real).</summary>
    public Stretch Stretch { get; set; } = Stretch.Uniform;

    /// <summary>Scale direction (UpOnly never shrinks, DownOnly never enlarges).</summary>
    public StretchDirection StretchDirection { get; set; } = StretchDirection.Both;

    public UIElement? Child
    {
        get => Children.Count > 0 ? Children[0] : null;
        set
        {
            var old = Child;
            if (ReferenceEquals(old, value)) return;
            if (old is not null) RemoveChild(old);
            if (value is not null) AddChild(value);
        }
    }

    private double _scale = 1.0;
    private WpfSize _childNatural;

    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (Child is null) return new WpfSize(0, 0);
        Child.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        _childNatural = Child.DesiredSize;
        if (_childNatural.Width <= 0 || _childNatural.Height <= 0) return new WpfSize(0, 0);
        var width = double.IsNaN(Width) ? available.Width : Width;
        var height = double.IsNaN(Height) ? available.Height : Height;
        if (double.IsInfinity(width) || double.IsInfinity(height))
            return new WpfSize(
                double.IsInfinity(width) ? _childNatural.Width : width,
                double.IsInfinity(height) ? _childNatural.Height : height);
        _scale = Stretch switch
        {
            Stretch.None => 1.0,
            Stretch.Fill => 1.0,
            Stretch.UniformToFill => Math.Max(width / _childNatural.Width, height / _childNatural.Height),
            _ => Math.Min(width / _childNatural.Width, height / _childNatural.Height),
        };
        if (StretchDirection == StretchDirection.UpOnly && _scale < 1.0) _scale = 1.0;
        else if (StretchDirection == StretchDirection.DownOnly && _scale > 1.0) _scale = 1.0;
        return Stretch == Stretch.Fill ? new WpfSize(width, height) : new WpfSize(width, height);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (Child is null || _childNatural.Width <= 0 || _childNatural.Height <= 0) return;
        var width = Stretch == Stretch.Fill ? finalRect.Width : _childNatural.Width * _scale;
        var height = Stretch == Stretch.Fill ? finalRect.Height : _childNatural.Height * _scale;
        var x = finalRect.X + (finalRect.Width - width) / 2;
        var y = finalRect.Y + (finalRect.Height - height) / 2;
        Child.Arrange(new WpfRect(x, y, Math.Max(0, width), Math.Max(0, height)));
    }

    protected override void OnRender(DrawingContext dc) => RenderVisible(dc, Child);
}

/// <summary>Template content host (matches System.Windows.Controls.ContentPresenter).</summary>
public class ContentPresenter : FrameworkElement
{
    private static readonly Dictionary<Type, PropertyInfo?> ContentProperties = new();

    /// <summary>Reads the templated parent's Content (presenters with no
    /// explicit Content mirror it at inflation, like WPF's recognition).</summary>
    internal static object? ReadParentContent(object parent)
    {
        var type = parent.GetType();
        lock (ContentProperties)
        {
            if (!ContentProperties.TryGetValue(type, out var property))
            {
                property = type.GetProperty("Content",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                ContentProperties[type] = property;
            }
            return property?.CanRead == true ? property.GetValue(parent) : null;
        }
    }

    private readonly TextBlock _text = new();

    /// <summary>
    /// Access-key recognition (matches ContentPresenter.RecognizesAccessKey):
    /// retained on the presenter; underscore-mnemonic adornment stays a
    /// precise rendering boundary (text renders literally).
    /// </summary>
    public bool RecognizesAccessKey { get; set; }

    /// <summary>
    /// Templated-parent property to present (matches
    /// System.Windows.Controls.ContentPresenter.ContentSource): the PC Space
    /// Map TabItem template presents "Header". The inflation engine mirrors
    /// that property instead of the parent's Content; template
    /// HeaderTemplate/ContentTemplate aliasing stays a boundary.
    /// </summary>
    public string? ContentSource { get; set; }

    /// <summary>Content token (matches System.Windows.Controls.ContentPresenter.ContentProperty: the shared ContentControl instance).</summary>
    public static readonly DependencyProperty ContentProperty =
        ContentControl.ContentProperty.AddOwner(typeof(ContentPresenter));

    /// <summary>Template token (matches System.Windows.Controls.ContentPresenter.ContentTemplateProperty).</summary>
    public static readonly DependencyProperty ContentTemplateProperty =
        DependencyProperty.Register("ContentTemplate", typeof(DataTemplate), typeof(ContentPresenter),
            new FrameworkPropertyMetadata(null));

    /// <summary>Template-selector token (matches System.Windows.Controls.ContentPresenter.ContentTemplateSelectorProperty).</summary>
    public static readonly DependencyProperty ContentTemplateSelectorProperty =
        DependencyProperty.Register("ContentTemplateSelector", typeof(DataTemplateSelector), typeof(ContentPresenter),
            new FrameworkPropertyMetadata(null));

    /// <summary>String-format token (matches System.Windows.Controls.ContentPresenter.ContentStringFormatProperty).</summary>
    public static readonly DependencyProperty ContentStringFormatProperty =
        DependencyProperty.Register("ContentStringFormat", typeof(string), typeof(ContentPresenter),
            new FrameworkPropertyMetadata((string?)null));

    private object? _content;

    /// <summary>Hosted content (object like real WPF; text renders as a label, visuals host live).</summary>
    public object? Content
    {
        get => _content;
        set
        {
            if (Equals(_content, value)) return;
            _content = value;
            SyncHosted();
            InvalidateMeasure();
        }
    }

    private DataTemplate? _contentTemplate;

    /// <summary>
    /// Fixed visual factory for the content (matches
    /// System.Windows.Controls.ContentPresenter.ContentTemplate; converted
    /// TabControlEx.CreateChildContentPresenter installs the selected tab
    /// template here; inflates with Content as its DataContext like WPF).
    /// </summary>
    public DataTemplate? ContentTemplate
    {
        get => _contentTemplate;
        set
        {
            if (ReferenceEquals(_contentTemplate, value)) return;
            _contentTemplate = value;
            SyncHosted();
            InvalidateMeasure();
        }
    }

    private DataTemplateSelector? _contentTemplateSelector;

    /// <summary>
    /// Per-content visual choice (matches
    /// System.Windows.Controls.ContentPresenter.ContentTemplateSelector;
    /// consulted when <see cref="ContentTemplate"/> is null, like WPF).
    /// </summary>
    public DataTemplateSelector? ContentTemplateSelector
    {
        get => _contentTemplateSelector;
        set
        {
            if (ReferenceEquals(_contentTemplateSelector, value)) return;
            _contentTemplateSelector = value;
            SyncHosted();
            InvalidateMeasure();
        }
    }

    private string? _contentStringFormat;

    /// <summary>
    /// Format applied to non-visual content without a template (matches
    /// System.Windows.Controls.ContentPresenter.ContentStringFormat; converted
    /// TabControlEx.CreateChildContentPresenter forwards the tab strip format).
    /// </summary>
    public string? ContentStringFormat
    {
        get => _contentStringFormat;
        set
        {
            if (_contentStringFormat == value) return;
            _contentStringFormat = value;
            SyncHosted();
            InvalidateMeasure();
        }
    }

    private UIElement? _hosted;

    /// <summary>
    /// Rebuilds the hosted visual: the effective template (fixed first, then
    /// the selector, like WPF) inflates with Content as its DataContext; a
    /// bare UIElement hosts directly; anything else renders as text with the
    /// string format applied.
    /// </summary>
    private void SyncHosted()
    {
        if (_hosted is not null)
        {
            DetachVisualChild(_hosted);
            _hosted = null;
        }
        var template = _contentTemplate ?? _contentTemplateSelector?.SelectTemplate(_content, this);
        if (template is not null && _content is not null)
        {
            var inflated = template.LoadContent();
            if (inflated is FrameworkElement fresh && _content is not null)
                fresh.DataContext = _content;
            if (inflated is UIElement visual)
            {
                AttachVisualChild(visual);
                _hosted = visual;
                return;
            }
        }
        if (_content is UIElement direct)
        {
            AttachVisualChild(direct);
            _hosted = direct;
            return;
        }
        SyncText();
    }

    private void SyncText() => _text.Text = FormatText();

    private string FormatText() => ContentControl.FormatContentText(_content, _contentStringFormat);

    private void Sync() => SyncText();

    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (_hosted is not null)
        {
            _hosted.Measure(available);
            return _hosted.DesiredSize;
        }
        SyncText();
        _text.Measure(available);
        return _text.DesiredSize;
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (_hosted is not null) _hosted.Arrange(finalRect);
        else _text.Arrange(finalRect);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_hosted is not null)
        {
            RenderVisible(dc, _hosted);
            return;
        }
        SyncText();
        _text.Render(dc);
    }
}

/// <summary>
/// Scroll content host (matches
/// System.Windows.Controls.ScrollContentPresenter): a content presenter
/// that scrolls with its ScrollViewer (the DataGrid template carries one);
/// offset sync rides the parent viewer, like the engine's scroll math.
/// </summary>
public class ScrollContentPresenter : ContentPresenter
{
    /// <summary>Identifies the CanContentScroll store (the only presenter
    /// DP, like WPF).</summary>
    public static readonly DependencyProperty CanContentScrollProperty =
        DependencyProperty.Register("CanContentScroll", typeof(bool), typeof(ScrollContentPresenter),
            new FrameworkPropertyMetadata(false));

    /// <summary>Content-scroll opt-in (false by default, like WPF).</summary>
    public bool CanContentScroll
    {
        get => (bool)(GetValue(CanContentScrollProperty) ?? false);
        set => SetValue(CanContentScrollProperty, value);
    }

    /// <summary>Horizontal scrolling enabled (false by default; retained,
    /// no scroller consumes it in v1).</summary>
    public bool CanHorizontallyScroll { get; set; }

    /// <summary>Vertical scrolling enabled (false by default; retained,
    /// no scroller consumes it in v1).</summary>
    public bool CanVerticallyScroll { get; set; }

    /// <summary>Owning scroller (null by default; retained).</summary>
    public ScrollViewer? ScrollOwner { get; set; }

    /// <summary>Scrolled extent width (zero: no scroller measures it in v1).</summary>
    public double ExtentWidth => 0.0;

    /// <summary>Scrolled extent height (zero: no scroller measures it in v1).</summary>
    public double ExtentHeight => 0.0;

    /// <summary>Horizontal scroll offset (zero: no scroller moves it in v1).</summary>
    public double HorizontalOffset => 0.0;

    /// <summary>Vertical scroll offset (zero: no scroller moves it in v1).</summary>
    public double VerticalOffset => 0.0;

    /// <summary>Viewport width (zero: no scroller measures it in v1).</summary>
    public double ViewportWidth => 0.0;

    /// <summary>Viewport height (zero: no scroller measures it in v1).</summary>
    public double ViewportHeight => 0.0;
}

/// <summary>
/// Text inline span (matches System.Windows.Documents.Span): an Inline, so
/// runs bind the converted Inline positions in TextBlocks and Paragraphs.
/// Fonts/ink live on TextElement with host inheritance (the old Span-local
/// font defaults now resolve through the host, identically for default
/// hosts); layout stays zero-size because hosts fold the text.
/// </summary>
public class Span : Inline
{
    private string _text = string.Empty;

    /// <summary>Inline text (hosts fold it into their text).</summary>
    public virtual string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            InvalidateMeasure();
            InlineTextChanged?.Invoke();
        }
    }

    /// <summary>Fires when Text changes so hosting TextBlocks can re-fold.</summary>
    internal event Action? InlineTextChanged;

    /// <summary>Raises the text-changed hook after a DP-backed text change.</summary>
    protected void RaiseInlineTextChanged()
    {
        InvalidateMeasure();
        InlineTextChanged?.Invoke();
    }

    protected override WpfSize MeasureCore(WpfSize available) => new(0, 0);
    protected override void OnRender(DrawingContext dc) { }
}

/// <summary>
/// Text inline run (matches System.Windows.Documents.Run): DP-backed Text
/// plus the string constructor the ScreenToGif code-behind uses.
/// </summary>
public sealed class Run : Span
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register("Text", typeof(string), typeof(Run),
            new FrameworkPropertyMetadata(string.Empty));

    public Run()
    {
    }

    public Run(string text) => Text = text ?? string.Empty;

    /// <summary>Run text (DP-backed like WPF).</summary>
    public override string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set
        {
            var current = (string)(GetValue(TextProperty) ?? string.Empty);
            if (current == value) return;
            SetValue(TextProperty, value);
            InvalidateMeasure();
            RaiseInlineTextChanged();
        }
    }
}

/// <summary>Emphasis inline (matches System.Windows.Documents.Bold).</summary>
public sealed class Bold : Span
{
    public Bold() => FontWeight = FontWeights.Bold;
}

/// <summary>Emphasis inline (matches System.Windows.Documents.Italic).</summary>
public sealed class Italic : Span
{
    public Italic() => FontStyle = FontStyles.Italic;
}

/// <summary>
/// Emphasis inline (matches System.Windows.Documents.Underline): parses and
/// folds like its siblings; underline paint is per-segment styling work.
/// </summary>
public sealed class Underline : Span
{
}

/// <summary>
/// Link inline (matches System.Windows.Documents.Hyperlink): a Span that
/// stays in the TextBlock's logical children so it can take focus, hit-test
/// its text range and raise Click/RequestNavigate. Activation mirrors
/// ButtonBase press/release (press arms, release inside clicks) plus
/// keyboard Enter/Space when focused. Unhandled requests perform no external
/// navigation: the browser sandbox cannot ShellExecute, so in-session hosts
/// (a Frame page swap in the demo) handle RequestNavigate; absolute URIs
/// left unhandled stay a precise platform boundary.
/// </summary>
public sealed class Hyperlink : Span
{
    /// <summary>Click token (matches Hyperlink.ClickEvent; routed like WPF).</summary>
    public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent(
        "Click", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Hyperlink));

    /// <summary>Navigate token (matches Hyperlink.RequestNavigateEvent; bubbles like WPF).</summary>
    public static readonly RoutedEvent RequestNavigateEvent = EventManager.RegisterRoutedEvent(
        "RequestNavigate", RoutingStrategy.Bubble, typeof(RequestNavigateEventHandler), typeof(Hyperlink));

    public static readonly DependencyProperty NavigateUriProperty =
        DependencyProperty.Register("NavigateUri", typeof(Uri), typeof(Hyperlink),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty TargetNameProperty =
        DependencyProperty.Register("TargetName", typeof(string), typeof(Hyperlink),
            new FrameworkPropertyMetadata(string.Empty));

    private readonly InlineCollection _inlines;

    /// <summary>Empty link (children arrive through <see cref="Inlines"/>).</summary>
    public Hyperlink()
    {
        _inlines = new InlineCollection(this);
        _inlines.ContentsChanged += RefoldChildren;
    }

    /// <summary>Link wrapping one inline (matches Hyperlink(Inline)).</summary>
    public Hyperlink(Inline childInline)
        : this()
    {
        ArgumentNullException.ThrowIfNull(childInline);
        _inlines.Add(childInline);
    }

    /// <summary>Hosted children (their text folds into <see cref="Text"/>).</summary>
    public InlineCollection Inlines => _inlines;

    /// <summary>Link text: folded children when hosted, else the direct text.</summary>
    public override string Text
    {
        get => _inlines.Count > 0 ? FoldChildren() : base.Text;
        set => base.Text = value;
    }

    private string FoldChildren() => string.Concat(_inlines.Select(static child => child switch
    {
        LineBreak => "\n",
        Span span => span.Text,
        _ => string.Empty,
    }));

    private void RefoldChildren() => base.Text = FoldChildren();

    private Uri? _navigateUri;

    /// <summary>Navigation target (matches Hyperlink.NavigateUri).</summary>
    public Uri? NavigateUri
    {
        get => _navigateUri;
        set
        {
            if (Equals(_navigateUri, value)) return;
            _navigateUri = value;
            InvalidateVisual();
        }
    }

    private string _targetName = string.Empty;

    /// <summary>Frame target name (matches Hyperlink.TargetName; retained).</summary>
    public string TargetName
    {
        get => _targetName;
        set
        {
            if (_targetName == value) return;
            _targetName = value;
            InvalidateVisual();
        }
    }

    /// <summary>Leaf activation (matches Hyperlink.Click).</summary>
    public event RoutedEventHandler? Click;

    /// <summary>Navigation request (matches Hyperlink.RequestNavigate).</summary>
    public event RequestNavigateEventHandler? RequestNavigate;

    public override bool Focusable => true;

    private bool _pressed;
    private bool _focused;

    public override void OnGotFocus() { _focused = true; InvalidateVisual(); }
    public override void OnLostFocus() { _focused = false; _pressed = false; InvalidateVisual(); }

    /// <summary>
    /// Activates the link: raises Click (CLR then routed), then
    /// RequestNavigate when NavigateUri is set (CLR then routed). A handler
    /// marking the request Handled stops further routing; with no handlers
    /// the request simply stays unhandled (no external launch in-session).
    /// </summary>
    public void DoClick()
    {
        if (!IsEffectivelyEnabled) return;
        var click = new RoutedEventArgs { RoutedEvent = ClickEvent, Source = this };
        Click?.Invoke(this, click);
        if (!click.Handled) RaiseEvent(click);
        if (NavigateUri is null) return;
        var navigate = new RequestNavigateEventArgs(NavigateUri, TargetName) { Source = this };
        RequestNavigate?.Invoke(this, navigate);
        if (!navigate.Handled) RaiseEvent(navigate);
    }

    /// <summary>Hit-tests the host's range for this link (TextBlock or Paragraph).</summary>
    public override bool HitTest(WpfPoint point)
    {
        if (Visibility != Visibility.Visible || !IsHitTestVisible) return false;
        WpfRect? rect = Parent switch
        {
            TextBlock host => host.GetLinkRect(this),
            Paragraph paragraph => paragraph.GetLinkRect(this),
            _ => null,
        };
        return rect is { } range &&
            point.X >= range.X && point.X < range.X + range.Width &&
            point.Y >= range.Y && point.Y < range.Y + range.Height;
    }

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        switch (e.Name)
        {
            case WasmRoutedEvent.PointerDownName when e.IsLeftButtonDown:
                Focus();
                _pressed = true;
                e.Handled = true;
                break;
            case WasmRoutedEvent.PointerUpName
                when e.Source is Core.WasmInputEvent.Pointer p && p.Args.Kind == Core.WasmPointerKind.Up:
                if (!_pressed) break;
                _pressed = false;
                if (HitTest(new WpfPoint(p.Args.X, p.Args.Y)))
                {
                    DoClick();
                    e.Handled = true;
                }
                break;
            case WasmRoutedEvent.PointerCancelName:
                _pressed = false;
                break;
            case WasmRoutedEvent.KeyDownName
                when IsKeyboardFocused && e.Source is Core.WasmInputEvent.Key k && k.Args.IsDown &&
                (string.Equals(k.Args.Key, "Enter", StringComparison.Ordinal) || k.Args.Key == " "):
                DoClick();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Whether the focus ring paints (focus affordance for keyboard users).</summary>
    internal bool ShowsFocusRing => _focused && IsEffectivelyEnabled;
}

/// <summary>
/// Line break inline (matches System.Windows.Documents.LineBreak): a newline
/// in the folded text (v1 TextBlock layout keeps single-line metrics, so it
/// renders as spacing, not a new line).
/// </summary>
public sealed class LineBreak : Span
{
    public LineBreak() => Text = "\n";
}

/// <summary>
/// Page host window (matches System.Windows.Navigation.NavigationWindow):
/// real WPF shows StartupUri pages inside one automatically, so converted
/// Page-rooted apps boot here instead of failing the Window cast.
/// </summary>
public class NavigationWindow : Window
{
    /// <summary>Navigates to a page (or any element); raises no journal events in v1.</summary>
    public void Navigate(object? content)
    {
        if (content is not UIElement element)
            throw new InvalidOperationException(
                $"NavigationWindow.Navigate needs a UIElement, not '{content?.GetType().Name ?? "null"}'.");
        Content = element;
    }
}
