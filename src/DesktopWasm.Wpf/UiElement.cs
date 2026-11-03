using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf;

/// <summary>Layout size box.</summary>
public readonly record struct WpfSize(double Width, double Height);

/// <summary>Layout rect box.</summary>
public readonly record struct WpfRect(double X, double Y, double Width, double Height);

/// <summary>Horizontal alignment (WPF subset: vertical panels honor this).</summary>
public enum HorizontalAlignment { Left, Center, Right, Stretch }

/// <summary>Vertical alignment (WPF subset: honored by text rendering).</summary>
public enum VerticalAlignment { Top, Center, Bottom, Stretch }

/// <summary>Margin thickness: uniform, symmetric (h,v), or full (l,t,r,b).</summary>
public readonly record struct Thickness(double Left, double Top, double Right, double Bottom)
{
    public Thickness(double uniform) : this(uniform, uniform, uniform, uniform) { }

    public Thickness(double horizontal, double vertical) : this(horizontal, vertical, horizontal, vertical) { }

    public static Thickness Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        // WPF accepts comma- and space-separated forms ("8 1 8 8" gallery titles).
        var parts = value.Split([',', ' ', '\t'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var nums = parts.Select(p => double.Parse(p, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return nums.Length switch
        {
            1 => new Thickness(nums[0]),
            2 => new Thickness(nums[0], nums[1]),
            4 => new Thickness(nums[0], nums[1], nums[2], nums[3]),
            _ => throw new FormatException($"'{value}' is not a Thickness (use 'u', 'h,v' or 'l,t,r,b')."),
        };
    }
}

/// <summary>Pipeline stage an invalidation reaches (layout vs paint-only).</summary>
public enum InvalidationKind
{
    /// <summary>Repaint only; layout is still valid.</summary>
    Render,
    /// <summary>Re-arrange (implies repaint).</summary>
    Arrange,
    /// <summary>Re-measure (implies arrange + repaint).</summary>
    Measure,
}

/// <summary>
/// UIElement: two-pass Measure/Arrange + OnRender(DrawingContext).
/// Collapse of WPF's UI-thread + render-thread model: MediaContext.CommitChannel
/// synchronously replays the DrawingContext into DrawCommands on the UI thread.
///
/// Invalidation: property and tree mutations mark the affected stage and walk
/// the <see cref="Parent"/> chain to the visual root, which raises
/// <see cref="Invalidated"/> (the host subscribes on its windows and marks its
/// frame dirty). Measure-affecting changes set all flags; arrange-affecting
/// changes set arrange + render; paint-only changes set render alone.
/// </summary>
/// <summary>Render/hit-test participation (PresentationCore subset).</summary>
public enum Visibility
{
    Visible,
    Hidden,
    Collapsed,
}

public class UIElement : Visual, IInputElement
{
    /// <summary>
    /// Desired size (matches UIElement.DesiredSize): Size-typed like WPF so
    /// converted layout readers bind; set through the WPF never-negative
    /// choke point, like the arrange pass below.
    /// </summary>
    public Size DesiredSize { get; protected set; }

    /// <summary>Layout-computed size from the last arrange (matches UIElement.RenderSize).</summary>
    public Size RenderSize => new(RenderRect.Width, RenderRect.Height);

    private Transform? _renderTransform;

    /// <summary>
    /// Render transform (matches UIElement.RenderTransform; parsed and
    /// retained, honored by TransformToAncestor; converted OxyPlot text
    /// layout binds this UIElement-level setter).
    /// </summary>
    public Transform? RenderTransform
    {
        get => _renderTransform;
        set
        {
            if (ReferenceEquals(_renderTransform, value)) return;
            _renderTransform = value;
            InvalidateVisual();
        }
    }

    /// <summary>Render-transform origin (matches UIElement.RenderTransformOrigin; retained).</summary>
    public Point RenderTransformOrigin { get; set; }

    /// <summary>Render-transform origin dependency property (matches
    /// UIElement.RenderTransformOriginProperty; the staged HandyControl
    /// RightPathStyle setter addresses it qualified off the UIElement owner,
    /// and style apply lands it through the CLR-first path like the other
    /// chrome setters).</summary>
    public static readonly DependencyProperty RenderTransformOriginProperty =
        DependencyProperty.Register("RenderTransformOrigin", typeof(Point), typeof(UIElement),
            new FrameworkPropertyMetadata(new Point(0, 0)) { AffectsRender = true });

    /// <summary>Render-transform dependency property (matches
    /// UIElement.RenderTransformProperty; the staged HandyControl
    /// RightPathStyle setter addresses it short-form off member -134 with a
    /// TransformGroup complex value, landing through the CLR-first path like
    /// the other chrome setters).</summary>
    public static readonly DependencyProperty RenderTransformProperty =
        DependencyProperty.Register("RenderTransform", typeof(Transform), typeof(UIElement),
            new FrameworkPropertyMetadata(Transform.Identity) { AffectsRender = true });

    /// <summary>Bitmap-cache hint dependency property (matches
    /// UIElement.CacheModeProperty; retained, never rasterized in this
    /// engine. Converted theme templates bind it, e.g. MaterialDesign
    /// DataGrid adorner decorators bound to vendor shadow assists).</summary>
    public static readonly DependencyProperty CacheModeProperty =
        DependencyProperty.Register("CacheMode", typeof(CacheMode), typeof(UIElement),
            new FrameworkPropertyMetadata(null));

    /// <summary>Cache hint (matches UIElement.CacheMode).</summary>
    public CacheMode? CacheMode
    {
        get => (CacheMode?)GetValue(CacheModeProperty);
        set => SetValue(CacheModeProperty, value);
    }

    /// <summary>
    /// Clip geometry (matches UIElement.Clip; parsed and retained; converted
    /// OxyPlot text/clip layout binds this UIElement-level getter).
    /// </summary>
    public Geometry? Clip { get; set; }
    /// <summary>Layout slot allocated by the parent (kept for layout diagnostics).</summary>
    public WpfRect ArrangedRect { get; private set; }
    /// <summary>Bounds after explicit sizing/alignment; used by painting and hit testing.</summary>
    public WpfRect RenderRect { get; private set; }

    private UIElement? _parent;

    /// <summary>Logical parent (set by AddChild/Content setters; null at the root).</summary>
    public UIElement? Parent
    {
        get => _parent;
        internal set
        {
            if (ReferenceEquals(_parent, value)) return;
            _parent = value;
            RefreshIsVisible();
        }
    }

    /// <summary>Visibility dependency property (animation target for show/hide key frames).</summary>
    public static readonly DependencyProperty VisibilityProperty =
        DependencyProperty.Register("Visibility", typeof(Visibility), typeof(UIElement),
            new FrameworkPropertyMetadata(Visibility.Visible) { AffectsRender = true });

    /// <summary>
    /// Render/hit-test participation. Hidden and Collapsed both skip paint
    /// and input in v1; Collapsed keeps its layout slot (unlike real WPF),
    /// so both behave like Hidden until measure honors collapse.
    /// </summary>
    public Visibility Visibility
    {
        get => (Visibility)(GetValue(VisibilityProperty) ?? Visibility.Visible);
        set => SetValue(VisibilityProperty, value);
    }

    /// <summary>Opacity dependency property (animation target for fade in/out).</summary>
    public static readonly DependencyProperty OpacityProperty =
        DependencyProperty.Register("Opacity", typeof(double), typeof(UIElement),
            new FrameworkPropertyMetadata(1.0) { AffectsRender = true });

    /// <summary>Layout rounding identity (matches UIElement.UseLayoutRoundingProperty; default false like WPF).</summary>
    public static readonly DependencyProperty UseLayoutRoundingProperty =
        DependencyProperty.Register("UseLayoutRounding", typeof(bool), typeof(UIElement),
            new FrameworkPropertyMetadata(false));

    /// <summary>Whether layout rounds to device pixels (retained; the renderer is unlit).</summary>
    public bool UseLayoutRounding
    {
        get => (bool)(GetValue(UseLayoutRoundingProperty) ?? false);
        set => SetValue(UseLayoutRoundingProperty, value);
    }

    /// <summary>
    /// Effect dependency property (matches UIElement.EffectProperty;
    /// ContainerVisual reuses this slot for its own Effect, like WPF).
    /// </summary>
    public static readonly DependencyProperty EffectProperty =
        DependencyProperty.Register("Effect", typeof(Effect), typeof(UIElement),
            new FrameworkPropertyMetadata(null) { AffectsRender = true });

    /// <summary>Visual effect (null by default).</summary>
    public Effect? Effect
    {
        get => (Effect?)GetValue(EffectProperty);
        set => SetValue(EffectProperty, value);
    }

    /// <summary>
    /// Opacity-mask dependency property (matches UIElement.OpacityMaskProperty;
    /// retained; the fill renderer paints the deterministic solid fallback
    /// like other non-solid brushes).
    /// </summary>
    public static readonly DependencyProperty OpacityMaskProperty =
        DependencyProperty.Register("OpacityMask", typeof(Brush), typeof(UIElement),
            new FrameworkPropertyMetadata(null) { AffectsRender = true });

    /// <summary>Opacity mask brush (retained; null by default).</summary>
    public Brush? OpacityMask
    {
        get => (Brush?)GetValue(OpacityMaskProperty);
        set => SetValue(OpacityMaskProperty, value);
    }

    public static readonly DependencyProperty ClipToBoundsProperty =
        DependencyProperty.Register("ClipToBounds", typeof(bool), typeof(UIElement),
            new PropertyMetadata(false, OnClipToBoundsChanged));

    /// <summary>Clips content to the layout rect (Border honors it in paint).</summary>
    public bool ClipToBounds
    {
        get => (bool)(GetValue(ClipToBoundsProperty) ?? false);
        set => SetValue(ClipToBoundsProperty, value);
    }

    private static void OnClipToBoundsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element) element.InvalidateVisual();
    }

    /// <summary>
    /// Starts a value animation on a dependency property (matches
    /// UIElement.BeginAnimation; null removes the animation and reverts to
    /// the base value). The deterministic animation clock pumps samples;
    /// see WpfAnimationClock.
    /// </summary>
    public void BeginAnimation(DependencyProperty dp, AnimationTimeline? animation) =>
        WpfAnimationClock.BeginAnimation(this, dp, animation, HandoffBehavior.SnapshotAndReplace);

    /// <summary>
    /// Starts a value animation with a handoff rule (matches the
    /// UIElement.BeginAnimation overload; Compose keeps the current animated
    /// value as the new base, SnapshotAndReplace restores the base first).
    /// </summary>
    public void BeginAnimation(DependencyProperty dp, AnimationTimeline? animation, HandoffBehavior handoffBehavior) =>
        WpfAnimationClock.BeginAnimation(this, dp, animation, handoffBehavior);

    /// <summary>Hit-test flag identity (matches UIElement.IsHitTestVisibleProperty; default true like WPF).</summary>
    public static readonly DependencyProperty IsHitTestVisibleProperty =
        DependencyProperty.Register("IsHitTestVisible", typeof(bool), typeof(UIElement),
            new PropertyMetadata(true));

    /// <summary>
    /// Hit-test participation independent of paint (matches the real
    /// property; hidden subtrees are still skipped by Visibility).
    /// </summary>
    public bool IsHitTestVisible
    {
        get => (bool)(GetValue(IsHitTestVisibleProperty) ?? true);
        set => SetValue(IsHitTestVisibleProperty, value);
    }

    private bool _isVisibleCache = true;

    private static readonly DependencyPropertyKey IsVisibleKey =
        DependencyProperty.RegisterReadOnly("IsVisible", typeof(bool), typeof(UIElement),
            new PropertyMetadata(false));

    /// <summary>Visibility-computed identity (matches UIElement.IsVisibleProperty; read-only like WPF).</summary>
    public static readonly DependencyProperty IsVisibleProperty = IsVisibleKey.DependencyProperty;

    /// <summary>
    /// Effective visibility (matches UIElement.IsVisible): own
    /// Visibility plus every ancestor's, so hiding a parent hides the
    /// subtree. Read-only like WPF; drive it through Visibility.
    /// </summary>
    public bool IsVisible => IsVisibleCore && (Parent?.IsVisible ?? true);

    /// <summary>
    /// Self visibility term (matches the internal WPF computation):
    /// own Visibility only. Window overrides it to fold its show flag in,
    /// so the property and the change event share one source of truth.
    /// </summary>
    internal virtual bool IsVisibleCore => Visibility == Visibility.Visible;

    /// <summary>Effective-visibility flip (matches UIElement.IsVisibleChanged).</summary>
    public event DependencyPropertyChangedEventHandler? IsVisibleChanged;

    /// <summary>
    /// Recomputes effective visibility and raises
    /// <see cref="IsVisibleChanged"/> on flips, cascading to logical
    /// children (parent hides hide the subtree, detach re-shows it).
    /// Runs synchronously like the WPF invalidation it mirrors.
    /// </summary>
    internal void RefreshIsVisible()
    {
        var now = IsVisible;
        if (now == _isVisibleCache) return;
        var old = _isVisibleCache;
        _isVisibleCache = now;
        IsVisibleChanged?.Invoke(this, new DependencyPropertyChangedEventArgs(IsVisibleProperty, old, now));
        if (this is FrameworkElement fe)
            foreach (var child in fe.LogicalChildren.ToArray())
                child.RefreshIsVisible();
    }

    /// <summary>
    /// Opacity factor. Zero (or negative) skips paint through RenderVisible
    /// (matches WPF: fully-transparent elements are invisible); intermediate
    /// values stay stored but uncomposited (a precise boundary). Code and
    /// animations may still drive intermediate values for state/readout.
    /// </summary>
    public double Opacity
    {
        get => (double)(GetValue(OpacityProperty) ?? 1.0);
        set => SetValue(OpacityProperty, value);
    }

    /// <summary>Pending re-measure (cleared by the next <see cref="Measure"/>).</summary>
    public bool IsMeasureDirty { get; private set; }

    /// <summary>Pending re-arrange (cleared by the next <see cref="Arrange"/>).</summary>
    public bool IsArrangeDirty { get; private set; }

    /// <summary>Pending repaint (cleared by <see cref="ClearInvalidation"/> after presenting).</summary>
    public bool IsRenderDirty { get; private set; }

    /// <summary>Any pending layout or repaint work in this subtree root-ward.</summary>
    public bool IsDirty => IsMeasureDirty || IsArrangeDirty || IsRenderDirty;

    /// <summary>
    /// Raised on the visual root when this subtree invalidates. The host
    /// subscribes on each window it owns and marks its frame dirty, so app
    /// code never calls host Invalidate() after ordinary setters.
    /// </summary>
    public event Action<InvalidationKind>? Invalidated;

    private readonly Dictionary<string, object?> _attached = new();

    /// <summary>
    /// Attached-property bag (Grid.Row, Canvas.Left, ...). A changed value
    /// reselects the parent's arrange slot, so it invalidates measure.
    /// </summary>
    public void SetAttached(string name, object? value)
    {
        if (_attached.TryGetValue(name, out var old) && Equals(old, value)) return;
        _attached[name] = value;
        EffectPaint.PushAttached(this, name, value);
        InvalidateMeasure();
    }

    /// <summary>Attached-property bag lookup (null when unset).</summary>
    public object? GetAttached(string name) => _attached.TryGetValue(name, out var v) ? v : null;

    /// <summary>Marks measure (+ arrange + render) dirty up to the visual root.</summary>
    public void InvalidateMeasure() => RequestInvalidation(InvalidationKind.Measure);

    /// <summary>Marks arrange (+ render) dirty up to the visual root.</summary>
    public void InvalidateArrange() => RequestInvalidation(InvalidationKind.Arrange);

    /// <summary>Marks repaint-only dirty up to the visual root.</summary>
    public void InvalidateVisual() => RequestInvalidation(InvalidationKind.Render);

    /// <summary>
    /// Clears pending flags for this subtree (host calls it on its roots after
    /// presenting a frame; tests use it to establish a clean baseline).
    /// </summary>
    internal void ClearInvalidation()
    {
        IsMeasureDirty = false;
        IsArrangeDirty = false;
        IsRenderDirty = false;
        if (this is FrameworkElement fe)
            foreach (var child in fe.LogicalChildren)
                child.ClearInvalidation();
    }

    private void RequestInvalidation(InvalidationKind kind)
    {
        var node = this;
        while (true)
        {
            node.MarkDirty(kind);
            var parent = node.Parent;
            if (parent is null)
            {
                node.Invalidated?.Invoke(kind);
                return;
            }

            node = parent;
        }
    }

    private void MarkDirty(InvalidationKind kind)
    {
        switch (kind)
        {
            case InvalidationKind.Measure:
                IsMeasureDirty = true;
                IsArrangeDirty = true;
                IsRenderDirty = true;
                break;
            case InvalidationKind.Arrange:
                IsArrangeDirty = true;
                IsRenderDirty = true;
                break;
            default:
                IsRenderDirty = true;
                break;
        }
    }

    /// <summary>Turns FrameworkPropertyMetadata effects into invalidation.</summary>
    protected override void OnPropertyChanged(DependencyProperty dp, object? oldValue, object? newValue)
    {
        if (dp.Metadata is FrameworkPropertyMetadata meta)
        {
            if (meta.AffectsMeasure) InvalidateMeasure();
            else if (meta.AffectsArrange) InvalidateArrange();
            else if (meta.AffectsRender) InvalidateVisual();
        }
        if (ReferenceEquals(dp, EffectProperty))
        {
            if (oldValue is DropShadowEffect oldFx) oldFx.Changed -= OnEffectChanged;
            if (newValue is DropShadowEffect newFx) newFx.Changed += OnEffectChanged;
            EffectPaint.PullAttached(this);
        }
        if (ReferenceEquals(dp, VisibilityProperty)) RefreshIsVisible();
    }

    private void OnEffectChanged() => InvalidateVisual();

    /// <summary>
    /// Hit-test against the arranged rect (inclusive start, exclusive end).
    /// Non-visible elements never hit (input skips hidden subtrees).
    /// </summary>
    public virtual bool HitTest(WpfPoint point) =>
        Visibility == Visibility.Visible && IsHitTestVisible &&
        point.X >= RenderRect.X && point.X < RenderRect.X + RenderRect.Width &&
        point.Y >= RenderRect.Y && point.Y < RenderRect.Y + RenderRect.Height;

    /// <summary>
    /// Overlay hit: an element OWNED by this one that paints above later
    /// siblings (menu drop-down panels; real WPF uses popups). Routing and
    /// hit-testing consult this before the regular children walk, so an open
    /// panel wins over content arranged beneath it. Default: none.
    /// </summary>
    public virtual UIElement? HitTestOverlay(WpfPoint point) => null;

    /// <summary>
    /// Renders a child only when it participates (single choke point so
    /// Visibility.Hidden/Collapsed skips paint in every container; also
    /// used by the compositor for roots and overlays).
    /// </summary>
    internal static void RenderVisible(DrawingContext dc, UIElement? child)
    {
        if (child is null || child.Visibility != Visibility.Visible) return;
        // Fully-transparent elements paint nothing (matches WPF): faded-out
        // chrome (closed drawer shadows, finished ripples) can never cover
        // content beneath it. Intermediate opacity stays uncomposited (see
        // Opacity); only the exact-zero case skips here.
        if (child.Opacity <= 0) return;
        EffectPaint.PaintUnderlay(dc, child.RenderRect, child.Effect);
        child.OnRender(dc);
        child.RenderConvertedChildrenIfNeeded(dc);
    }

    /// <summary>
    /// Converted paint-less OnRender backstop (no-op here): converted
    /// ElementGroup/ButtonGroup run corner-radius/style code in OnRender
    /// instead of painting, while real WPF renders their children through
    /// the visual tree. ItemsControl replays that child walk for exactly
    /// those two types; engine types keep their own choreography untouched.
    /// See Slice111HandyPanels.
    /// </summary>
    internal virtual void RenderConvertedChildrenIfNeeded(DrawingContext dc)
    {
    }

    /// <summary>Focusability flag (matches UIElement.FocusableProperty; default false, Control overrides the default to true like WPF).</summary>
    public static readonly DependencyProperty FocusableProperty =
        DependencyProperty.Register("Focusable", typeof(bool), typeof(UIElement),
            new FrameworkPropertyMetadata(false));

    /// <summary>Focus-state identity (matches UIElement.IsKeyboardFocusedProperty; read-only like WPF, the live state stays manager-authoritative).</summary>
    public static readonly DependencyPropertyKey IsKeyboardFocusedKey =
        DependencyProperty.RegisterReadOnly("IsKeyboardFocused", typeof(bool), typeof(UIElement),
            new PropertyMetadata(false));

    /// <summary>Focus-state identity (matches UIElement.IsKeyboardFocusedProperty; converted member refs bind the field).</summary>
    public static readonly DependencyProperty IsKeyboardFocusedProperty = IsKeyboardFocusedKey.DependencyProperty;

    /// <summary>Whether keyboard focus can land here (store-backed; converted XAML/styles/code-built triggers set this).</summary>
    public virtual bool Focusable
    {
        get => (bool?)GetValue(FocusableProperty) ?? false;
        set => SetValue(FocusableProperty, value);
    }

    /// <summary>Pixel-snapping hint (matches UIElement.SnapsToDevicePixelsProperty; parsed and retained; rendering is already snapped in v1).</summary>
    public static readonly DependencyProperty SnapsToDevicePixelsProperty =
        DependencyProperty.Register("SnapsToDevicePixels", typeof(bool), typeof(UIElement),
            new FrameworkPropertyMetadata(false) { AffectsRender = true });

    /// <summary>Pixel-snapping hint (parsed and retained; rendering is already snapped in v1).</summary>
    public bool SnapsToDevicePixels
    {
        get => (bool?)GetValue(SnapsToDevicePixelsProperty) ?? false;
        set => SetValue(SnapsToDevicePixelsProperty, value);
    }

    /// <summary>Theme-style opt-out (matches FrameworkElement.OverridesDefaultStyleProperty;
    /// retained on UIElement for non-framework hosts, default styling always applies in v1).</summary>
    public bool OverridesDefaultStyle
    {
        get => this is FrameworkElement fe
            ? (bool?)fe.GetValue(FrameworkElement.OverridesDefaultStyleProperty) ?? false
            : _overridesDefaultStyle;
        set
        {
            if (this is FrameworkElement fe) fe.SetValue(FrameworkElement.OverridesDefaultStyleProperty, value);
            else _overridesDefaultStyle = value;
        }
    }

    private bool _overridesDefaultStyle;

    /// <summary>Enabled flag (matches UIElement.IsEnabledProperty; default
    /// true like WPF; store-backed so XAML setters and short-form style
    /// setters land through the DP store).</summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.Register("IsEnabled", typeof(bool), typeof(UIElement),
            new FrameworkPropertyMetadata(true));

    /// <summary>Local enabled flag (default true; disabling repaints).</summary>
    public bool IsEnabled
    {
        get => (bool?)GetValue(IsEnabledProperty) ?? true;
        set
        {
            if (((bool?)GetValue(IsEnabledProperty) ?? true) == value) return;
            SetValue(IsEnabledProperty, value);
            InvalidateVisual();
            if (this is Control control) TemplateApplier.RefreshTemplate(control);
        }
    }

    /// <summary>
    /// Effective availability: local flag AND command gate (buttons extend
    /// this with CanExecute). Input routing and greyed rendering use this.
    /// </summary>
    public virtual bool IsEffectivelyEnabled => IsEnabled;

    public event KeyEventHandler? PreviewKeyDown;
    public event KeyEventHandler? KeyDown;
    public event KeyEventHandler? PreviewKeyUp;
    public event KeyEventHandler? KeyUp;
    public event MouseButtonEventHandler? PreviewMouseDown;
    public event MouseButtonEventHandler? MouseDown;
    /// <summary>Left-button press (matches UIElement.PreviewMouseLeftButtonDown).</summary>
    public event MouseButtonEventHandler? PreviewMouseLeftButtonDown;
    /// <summary>Left-button press (matches UIElement.MouseLeftButtonDown).</summary>
    public event MouseButtonEventHandler? MouseLeftButtonDown;
    public event MouseButtonEventHandler? PreviewMouseUp;
    public event MouseButtonEventHandler? MouseUp;
    /// <summary>Pen barrel press, tunneling (matches UIElement.PreviewStylusButtonDown).</summary>
    public event StylusButtonEventHandler? PreviewStylusButtonDown;
    /// <summary>Pen barrel press (matches UIElement.StylusButtonDown).</summary>
    public event StylusButtonEventHandler? StylusButtonDown;
    /// <summary>Pen barrel release, tunneling (matches UIElement.PreviewStylusButtonUp).</summary>
    public event StylusButtonEventHandler? PreviewStylusButtonUp;
    /// <summary>Pen barrel release (matches UIElement.StylusButtonUp).</summary>
    public event StylusButtonEventHandler? StylusButtonUp;
    /// <summary>Left-button release after press (matches UIElement.MouseLeftButtonUp).</summary>
    public event MouseButtonEventHandler? MouseLeftButtonUp;
    /// <summary>Tunneling left-button release (matches UIElement.PreviewMouseLeftButtonUp).</summary>
    public event MouseButtonEventHandler? PreviewMouseLeftButtonUp;
    /// <summary>Right-button press (matches UIElement.PreviewMouseRightButtonDown).</summary>
    public event MouseButtonEventHandler? PreviewMouseRightButtonDown;
    /// <summary>Right-button press (matches UIElement.MouseRightButtonDown).</summary>
    public event MouseButtonEventHandler? MouseRightButtonDown;
    /// <summary>Tunneling right-button release (matches UIElement.PreviewMouseRightButtonUp).</summary>
    public event MouseButtonEventHandler? PreviewMouseRightButtonUp;
    /// <summary>Right-button release after press (matches UIElement.MouseRightButtonUp).</summary>
    public event MouseButtonEventHandler? MouseRightButtonUp;
    /// <summary>Explicit capture taken (matches UIElement.GotMouseCapture).</summary>
    public event MouseEventHandler? GotMouseCapture;
    /// <summary>Explicit capture released (matches UIElement.LostMouseCapture).</summary>
    public event MouseEventHandler? LostMouseCapture;
    public event MouseEventHandler? PreviewMouseMove;
    public event MouseEventHandler? MouseMove;
    public event MouseWheelEventHandler? PreviewMouseWheel;
    public event MouseWheelEventHandler? MouseWheel;
    /// <summary>Touch press, tunneling (matches UIElement.PreviewTouchDown).</summary>
    public event EventHandler<TouchEventArgs>? PreviewTouchDown;
    /// <summary>Touch press (matches UIElement.TouchDown).</summary>
    public event EventHandler<TouchEventArgs>? TouchDown;
    /// <summary>Touch contact entered, direct (matches UIElement.TouchEnter).</summary>
    public event EventHandler<TouchEventArgs>? TouchEnter;
    /// <summary>Touch contact left, direct (matches UIElement.TouchLeave).</summary>
    public event EventHandler<TouchEventArgs>? TouchLeave;
    /// <summary>Touch contact moved, tunneling (matches UIElement.PreviewTouchMove).</summary>
    public event EventHandler<TouchEventArgs>? PreviewTouchMove;
    /// <summary>Touch contact moved (matches UIElement.TouchMove).</summary>
    public event EventHandler<TouchEventArgs>? TouchMove;
    /// <summary>Touch release, tunneling (matches UIElement.PreviewTouchUp).</summary>
    public event EventHandler<TouchEventArgs>? PreviewTouchUp;
    /// <summary>Touch release (matches UIElement.TouchUp).</summary>
    public event EventHandler<TouchEventArgs>? TouchUp;
    /// <summary>Touch capture taken (matches UIElement.GotTouchCapture).</summary>
    public event EventHandler<TouchEventArgs>? GotTouchCapture;
    /// <summary>Touch capture released (matches UIElement.LostTouchCapture).</summary>
    public event EventHandler<TouchEventArgs>? LostTouchCapture;

    /// <summary>
    /// Layout pass completion (matches UIElement.LayoutUpdated): raised at
    /// the end of every Arrange, which is the engine's layout-pass choke
    /// point. Converted OxyPlot.Wpf PlotViewBase subscribes in its
    /// constructor, so the engine must own this event or converted views
    /// fail at instantiation with MissingMethodException.
    /// </summary>
    public event EventHandler? LayoutUpdated;

    /// <summary>Whether HTML5/engine drags may target this element (matches UIElement.AllowDrop).</summary>
    public bool AllowDrop { get; set; }

    /// <summary>Drag crossed the target, tunneling (matches UIElement.PreviewDragEnter).</summary>
    public event DragEventHandler? PreviewDragEnter;
    /// <summary>Drag crossed the target (matches UIElement.DragEnter).</summary>
    public event DragEventHandler? DragEnter;
    /// <summary>Drag moved over the target, tunneling (matches UIElement.PreviewDragOver).</summary>
    public event DragEventHandler? PreviewDragOver;
    /// <summary>Drag moved over the target (matches UIElement.DragOver).</summary>
    public event DragEventHandler? DragOver;
    /// <summary>Drag left the target, tunneling (matches UIElement.PreviewDragLeave).</summary>
    public event DragEventHandler? PreviewDragLeave;
    /// <summary>Drag left the target (matches UIElement.DragLeave).</summary>
    public event DragEventHandler? DragLeave;
    /// <summary>Payload released over the target, tunneling (matches UIElement.PreviewDrop).</summary>
    public event DragEventHandler? PreviewDrop;
    /// <summary>Payload released over the target (matches UIElement.Drop).</summary>
    public event DragEventHandler? Drop;
    /// <summary>
    /// Drag-source cursor feedback (matches UIElement.GiveFeedback): converted
    /// AvalonEdit text-area input handlers attach/detach here, so the engine
    /// must own this event or converted editors fail at attach with
    /// MissingMethodException.
    /// </summary>
    public event GiveFeedbackEventHandler? GiveFeedback;
    /// <summary>
    /// Drag-source continuation query (matches UIElement.QueryContinueDrag):
    /// converted AvalonEdit text-area input handlers attach/detach here, like
    /// <see cref="GiveFeedback"/>.
    /// </summary>
    public event QueryContinueDragEventHandler? QueryContinueDrag;
    /// <summary>Cursor-query token (matches UIElement.QueryCursorEvent; Bubble owned by Mouse like WPF).</summary>
    public static readonly RoutedEvent QueryCursorEvent = EventManager.RegisterRoutedEvent(
        "QueryCursor", RoutingStrategy.Bubble, typeof(QueryCursorEventHandler), typeof(Mouse));
    /// <summary>
    /// Cursor query (matches UIElement.QueryCursor): converted AvalonEdit
    /// text-area input handlers attach/detach here, like
    /// <see cref="GiveFeedback"/>. Handlers commit <see cref="QueryCursorEventArgs.Cursor"/>
    /// (null selects <see cref="Cursor"/>) and mark Handled, as
    /// textArea_QueryCursor and the link-text override do.
    /// </summary>
    public event QueryCursorEventHandler? QueryCursor;
    /// <summary>Text-preview token (matches UIElement.PreviewTextInputEvent; Tunnel owned by the composition scope like WPF's TextCompositionManager).</summary>
    public static readonly RoutedEvent PreviewTextInputEvent = EventManager.RegisterRoutedEvent(
        "PreviewTextInput", RoutingStrategy.Tunnel, typeof(TextCompositionEventHandler), typeof(InputManager));
    /// <summary>Text-input token (matches UIElement.TextInputEvent; Bubble owned by the composition scope like WPF's TextCompositionManager).</summary>
    public static readonly RoutedEvent TextInputEvent = EventManager.RegisterRoutedEvent(
        "TextInput", RoutingStrategy.Bubble, typeof(TextCompositionEventHandler), typeof(InputManager));
    /// <summary>
    /// Text preview (matches UIElement.PreviewTextInput): converted AvalonEdit
    /// completion windows attach/detach here through the AddHandler route, so
    /// the engine must own this event or converted editors fail at attach with
    /// MissingMethodException.
    /// </summary>
    public event TextCompositionEventHandler? PreviewTextInput
    {
        add => AddHandler(PreviewTextInputEvent, value);
        remove => RemoveHandler(PreviewTextInputEvent, value);
    }
    /// <summary>
    /// Text input (matches UIElement.TextInput): converted AvalonEdit text
    /// areas override <see cref="OnTextInput"/> and route through here, like
    /// <see cref="PreviewTextInput"/>.
    /// </summary>
    public event TextCompositionEventHandler? TextInput
    {
        add => AddHandler(TextInputEvent, value);
        remove => RemoveHandler(TextInputEvent, value);
    }

    /// <summary>Whether this element currently holds keyboard focus (matches UIElement.IsFocused).</summary>
    public virtual bool IsFocused => IsKeyboardFocused;

    /// <summary>Moves focus here through the active input manager (matches UIElement.Focus).</summary>
    public bool Focus() => WpfInputManager.Current?.TryFocus(this) ?? false;

    /// <summary>True while this element holds keyboard focus.</summary>
    public virtual bool IsKeyboardFocused => WpfInputManager.Current?.Focused == this;

    /// <summary>Subtree focus (approximates IsKeyboardFocusWithin: same as IsKeyboardFocused here).</summary>
    public virtual bool IsKeyboardFocusWithin => IsKeyboardFocused;

    /// <summary>True while the pointer is directly over this element.</summary>
    public virtual bool IsMouseDirectlyOver => WpfInputManager.Current?.DirectlyOverElement == this;

    /// <summary>
    /// Hover-state identity (matches UIElement.IsMouseOverProperty; converted
    /// code-built triggers bind the field). The live hover state stays
    /// CLR-authoritative through the input manager (no browser hover feed
    /// writes the store); the field gives converted trigger/style code its
    /// member identity and the DP name feeds the name-based evaluation.
    /// </summary>
    public static readonly DependencyProperty IsMouseOverProperty =
        DependencyProperty.Register("IsMouseOver", typeof(bool), typeof(UIElement),
            new FrameworkPropertyMetadata(false));

    /// <summary>True while the pointer is over this element or a descendant.</summary>
    public virtual bool IsMouseOver => WpfInputManager.Current?.IsOverElement(this) ?? false;

    /// <summary>Pointer cursor (matches UIElement.Cursor; retained, no cursor layer yet).</summary>
    public Cursor? Cursor { get; set; }

    /// <summary>Right-press token (matches UIElement.PreviewMouseRightButtonDownEvent; Direct like WPF).</summary>
    public static readonly RoutedEvent PreviewMouseRightButtonDownEvent = EventManager.RegisterRoutedEvent(
        "PreviewMouseRightButtonDown", RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>Right-press token (matches UIElement.MouseRightButtonDownEvent; Direct like WPF).</summary>
    public static readonly RoutedEvent MouseRightButtonDownEvent = EventManager.RegisterRoutedEvent(
        "MouseRightButtonDown", RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>Right-release token (matches UIElement.PreviewMouseRightButtonUpEvent; Direct like WPF).</summary>
    public static readonly RoutedEvent PreviewMouseRightButtonUpEvent = EventManager.RegisterRoutedEvent(
        "PreviewMouseRightButtonUp", RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>Right-release token (matches UIElement.MouseRightButtonUpEvent; Direct like WPF).</summary>
    public static readonly RoutedEvent MouseRightButtonUpEvent = EventManager.RegisterRoutedEvent(
        "MouseRightButtonUp", RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>Left-press token (matches UIElement.PreviewMouseLeftButtonDownEvent;
    /// Tunnel like WPF; vendor ListBoxAssist registers class handlers on it).</summary>
    public static readonly RoutedEvent PreviewMouseLeftButtonDownEvent = EventManager.RegisterRoutedEvent(
        "PreviewMouseLeftButtonDown", RoutingStrategy.Tunnel, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>Left-press token (matches UIElement.MouseLeftButtonDownEvent; Bubble like WPF).</summary>
    public static readonly RoutedEvent MouseLeftButtonDownEvent = EventManager.RegisterRoutedEvent(
        "MouseLeftButtonDown", RoutingStrategy.Bubble, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>Left-release token (matches UIElement.PreviewMouseLeftButtonUpEvent; Tunnel like WPF).</summary>
    public static readonly RoutedEvent PreviewMouseLeftButtonUpEvent = EventManager.RegisterRoutedEvent(
        "PreviewMouseLeftButtonUp", RoutingStrategy.Tunnel, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>Left-release token (matches UIElement.MouseLeftButtonUpEvent; Bubble like WPF).</summary>
    public static readonly RoutedEvent MouseLeftButtonUpEvent = EventManager.RegisterRoutedEvent(
        "MouseLeftButtonUp", RoutingStrategy.Bubble, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>Focus token (matches UIElement.GotFocusEvent; Bubble like WPF).</summary>
    public static readonly RoutedEvent GotFocusEvent = EventManager.RegisterRoutedEvent(
        "GotFocus", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>Focus-loss token (matches UIElement.LostFocusEvent; Bubble like WPF).</summary>
    public static readonly RoutedEvent LostFocusEvent = EventManager.RegisterRoutedEvent(
        "LostFocus", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>Keyboard-focus token (matches UIElement.GotKeyboardFocusEvent; Bubble like WPF).</summary>
    public static readonly RoutedEvent GotKeyboardFocusEvent = EventManager.RegisterRoutedEvent(
        "GotKeyboardFocus", RoutingStrategy.Bubble, typeof(KeyboardFocusChangedEventHandler), typeof(UIElement));

    /// <summary>Keyboard-focus-loss token (matches UIElement.LostKeyboardFocusEvent; Bubble like WPF).</summary>
    public static readonly RoutedEvent LostKeyboardFocusEvent = EventManager.RegisterRoutedEvent(
        "LostKeyboardFocus", RoutingStrategy.Bubble, typeof(KeyboardFocusChangedEventHandler), typeof(UIElement));

    /// <summary>Keyboard-focus preview token (matches UIElement.PreviewGotKeyboardFocusEvent; Tunnel like WPF).</summary>
    public static readonly RoutedEvent PreviewGotKeyboardFocusEvent = EventManager.RegisterRoutedEvent(
        "PreviewGotKeyboardFocus", RoutingStrategy.Tunnel, typeof(KeyboardFocusChangedEventHandler), typeof(UIElement));

    /// <summary>Keyboard-focus-loss preview token (matches UIElement.PreviewLostKeyboardFocusEvent; Tunnel like WPF).</summary>
    public static readonly RoutedEvent PreviewLostKeyboardFocusEvent = EventManager.RegisterRoutedEvent(
        "PreviewLostKeyboardFocus", RoutingStrategy.Tunnel, typeof(KeyboardFocusChangedEventHandler), typeof(UIElement));

    /// <summary>
    /// Captures all pointer input to this element (matches
    /// UIElement.CaptureMouse): routes through the input manager, which
    /// only captures rooted elements. Returns false with no input
    /// manager or when this element is not in a live tree.
    /// </summary>
    public virtual bool CaptureMouse() => Mouse.Capture(this);

    /// <summary>
    /// Releases explicit capture (matches UIElement.ReleaseMouseCapture):
    /// no-op unless this element currently holds it, like WPF.
    /// </summary>
    public virtual void ReleaseMouseCapture()
    {
        if (Mouse.Captured == this) Mouse.Capture(null);
    }

    /// <summary>Whether this element holds explicit mouse capture (matches UIElement.IsMouseCaptured).</summary>
    public virtual bool IsMouseCaptured => Mouse.Captured == this;

    /// <summary>
    /// Captures a touch contact to this element (matches
    /// UIElement.CaptureTouch): subsequent touch events for the contact
    /// route here instead of hit testing. False with no input manager, an
    /// inactive contact, or when this element is not in a live tree.
    /// </summary>
    public virtual bool CaptureTouch(TouchDevice touchDevice)
    {
        ArgumentNullException.ThrowIfNull(touchDevice);
        // Owner-first like TouchDevice.Capture (same cross-talk rule).
        return touchDevice.Capture(this);
    }

    /// <summary>
    /// Releases a touch contact held by this element (matches
    /// UIElement.ReleaseTouchCapture): false unless this element holds it.
    /// </summary>
    public virtual bool ReleaseTouchCapture(TouchDevice touchDevice)
    {
        ArgumentNullException.ThrowIfNull(touchDevice);
        if (!ReferenceEquals(touchDevice.Captured, this)) return false;
        return (touchDevice.OwnerManager ?? WpfInputManager.Current)?.ReleaseTouchCapture(this, touchDevice) ?? false;
    }

    /// <summary>
    /// Releases every touch contact held by this element (matches
    /// UIElement.ReleaseAllTouchCaptures).
    /// </summary>
    public virtual void ReleaseAllTouchCaptures() =>
        (TouchManager ?? WpfInputManager.Current)?.ReleaseAllTouchCaptures(this);

    /// <summary>
    /// Last manager that routed touch to this element (null until the first
    /// touch route; queries follow it before the ambient Current, so a
    /// parallel suite's manager cannot skew them).
    /// </summary>
    internal WpfInputManager? TouchManager { get; set; }

    private WpfInputManager? TouchQueryManager => TouchManager ?? WpfInputManager.Current;

    /// <summary>Contacts captured to this element (matches UIElement.TouchesCaptured).</summary>
    public IEnumerable<TouchDevice> TouchesCaptured =>
        TouchQueryManager?.CapturedTouches(this) ?? Enumerable.Empty<TouchDevice>();

    /// <summary>Contacts captured into this subtree (matches UIElement.TouchesCapturedWithin).</summary>
    public IEnumerable<TouchDevice> TouchesCapturedWithin =>
        TouchQueryManager?.CapturesWithin(this) ?? Enumerable.Empty<TouchDevice>();

    /// <summary>Contacts directly over this element (matches UIElement.TouchesDirectlyOver).</summary>
    public IEnumerable<TouchDevice> TouchesDirectlyOver =>
        TouchQueryManager?.DirectlyOverTouches(this) ?? Enumerable.Empty<TouchDevice>();

    /// <summary>Contacts over this element or a descendant (matches UIElement.TouchesOver).</summary>
    public IEnumerable<TouchDevice> TouchesOver =>
        TouchQueryManager?.OverTouches(this) ?? Enumerable.Empty<TouchDevice>();

    /// <summary>Whether any contact is captured here (matches UIElement.AreAnyTouchesCaptured).</summary>
    public bool AreAnyTouchesCaptured => TouchesCaptured.Any();

    /// <summary>Whether any contact is captured in this subtree (matches UIElement.AreAnyTouchesCapturedWithin).</summary>
    public bool AreAnyTouchesCapturedWithin => TouchesCapturedWithin.Any();

    /// <summary>Whether any contact is directly over this element (matches UIElement.AreAnyTouchesDirectlyOver).</summary>
    public bool AreAnyTouchesDirectlyOver => TouchesDirectlyOver.Any();

    /// <summary>Whether any contact is over this element or a descendant (matches UIElement.AreAnyTouchesOver).</summary>
    public bool AreAnyTouchesOver => TouchesOver.Any();

    /// <summary>
    /// Topmost hit under a point in this element's coordinates (matches
    /// UIElement.InputHitTest): walks the same Visibility /
    /// IsHitTestVisible / shape-aware tree the input router uses, deepest
    /// topmost first; null when nothing under the point hits.
    /// </summary>
    public IInputElement? InputHitTest(Point point)
    {
        var result = VisualTreeHelper.HitTest(this, point);
        return result?.VisualHit as IInputElement;
    }

    public event MouseEventHandler? MouseEnter;
    public event MouseEventHandler? MouseLeave;

    /// <summary>Moves focus along tab order (directional requests return false: no spatial nav).</summary>
    public bool MoveFocus(TraversalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return WpfInputManager.Current?.MoveFocus(request) ?? false;
    }

    /// <summary>
    /// Maps a point to another visual's space (matches
    /// UIElement.TranslatePoint; both chains compose to the root and the
    /// point re-bases through the target's inverse, like WPF, so siblings
    /// and descendants map as well as ancestors).
    /// </summary>
    public Point TranslatePoint(Point point, UIElement relativeTo)
    {
        ArgumentNullException.ThrowIfNull(relativeTo);
        var fromRoot = TransformToRoot();
        var toRoot = relativeTo.TransformToRoot();
        if (!toRoot.HasInverse)
            throw new InvalidOperationException("TranslatePoint needs an invertible visual chain.");
        return toRoot.Inverse.Transform(fromRoot.Transform(point));
    }

    public event KeyboardFocusChangedEventHandler? GotKeyboardFocus;
    public event KeyboardFocusChangedEventHandler? LostKeyboardFocus;
    public event KeyboardFocusChangedEventHandler? PreviewGotKeyboardFocus;
    public event KeyboardFocusChangedEventHandler? PreviewLostKeyboardFocus;
    public event DependencyPropertyChangedEventHandler? IsKeyboardFocusedChanged;
    /// <summary>Subtree-focus flip (matches UIElement.IsKeyboardFocusWithinChanged;
    /// within tracks focused here, so it fires with the focused flip).</summary>
    public event DependencyPropertyChangedEventHandler? IsKeyboardFocusWithinChanged;
    public event RoutedEventHandler? GotFocus;
    public event RoutedEventHandler? LostFocus;

    internal void RaiseGotFocus(KeyboardFocusChangedEventArgs preview)
    {
        OnGotKeyboardFocus(preview);
        OnGotFocus(new RoutedEventArgs());
    }

    internal void RaiseLostFocus(KeyboardFocusChangedEventArgs preview)
    {
        OnLostKeyboardFocus(preview);
        OnLostFocus(new RoutedEventArgs());
    }

    /// <summary>Command bindings consulted by routed command routing.</summary>
    public CommandBindingCollection CommandBindings { get; } = new();

    /// <summary>Accelerator table (matches UIElement.InputBindings).</summary>
    public InputBindingCollection InputBindings { get; } = new();

    internal void RaiseKey(KeyEventArgs args, bool preview)
    {
        if (preview)
        {
            if (args.IsDown) OnPreviewKeyDown(args);
            else OnPreviewKeyUp(args);
        }
        else
        {
            if (args.IsDown) OnKeyDown(args);
            else OnKeyUp(args);
        }
    }

    internal void RaiseMouse(MouseEventArgs args, bool preview)
    {
        switch (args)
        {
            case MouseWheelEventArgs wheel:
                if (preview) OnPreviewMouseWheel(wheel);
                else OnMouseWheel(wheel);
                break;
            case MouseButtonEventArgs button:
                if (preview)
                {
                    if (button.ButtonState == MouseButtonState.Pressed) OnPreviewMouseDown(button);
                    else OnPreviewMouseUp(button);
                    if (button.ChangedButton == MouseButton.Left)
                    {
                        if (button.ButtonState == MouseButtonState.Pressed) OnPreviewMouseLeftButtonDown(button);
                        else OnPreviewMouseLeftButtonUp(button);
                    }
                    else if (button.ChangedButton == MouseButton.Right)
                    {
                        if (button.ButtonState == MouseButtonState.Pressed) OnPreviewMouseRightButtonDown(button);
                        else OnPreviewMouseRightButtonUp(button);
                        button.RoutedEvent = button.ButtonState == MouseButtonState.Pressed
                            ? PreviewMouseRightButtonDownEvent
                            : PreviewMouseRightButtonUpEvent;
                        RaiseEvent(button);
                    }
                }
                else
                {
                    if (button.ButtonState == MouseButtonState.Pressed) OnMouseDown(button);
                    else OnMouseUp(button);
                    if (button.ButtonState == MouseButtonState.Released && button.ChangedButton == MouseButton.Left)
                        OnMouseLeftButtonUp(button);
                    else if (button.ButtonState == MouseButtonState.Released && button.ChangedButton == MouseButton.Right)
                    {
                        OnMouseRightButtonUp(button);
                        button.RoutedEvent = MouseRightButtonUpEvent;
                        RaiseEvent(button);
                    }
                    else if (button.ButtonState == MouseButtonState.Pressed && button.ChangedButton == MouseButton.Right)
                    {
                        button.RoutedEvent = MouseRightButtonDownEvent;
                        RaiseEvent(button);
                    }
                }
                break;
            default:
                if (preview) OnPreviewMouseMove(args);
                else OnMouseMove(args);
                break;
        }
    }

    internal void RaiseMouseEnter(MouseEventArgs args) => OnMouseEnter(args);

    internal void RaiseMouseLeave(MouseEventArgs args) => OnMouseLeave(args);

    /// <summary>Touch route (tunneling preview, then bubble; enter/leave ride direct delivery below).</summary>
    internal void RaiseTouch(TouchEventArgs args, TouchAction action, bool preview)
    {
        if (preview)
        {
            if (action == TouchAction.Down) OnPreviewTouchDown(args);
            else if (action == TouchAction.Move) OnPreviewTouchMove(args);
            else OnPreviewTouchUp(args);
        }
        else
        {
            if (action == TouchAction.Down) OnTouchDown(args);
            else if (action == TouchAction.Move) OnTouchMove(args);
            else OnTouchUp(args);
        }
    }

    /// <summary>Delivers a direct touch-enter (matches the WPF direct TouchEnter route).</summary>
    internal void RaiseTouchEnter(TouchEventArgs args) => OnTouchEnter(args);

    /// <summary>Delivers a direct touch-leave (matches the WPF direct TouchLeave route).</summary>
    internal void RaiseTouchLeave(TouchEventArgs args) => OnTouchLeave(args);

    /// <summary>Delivers touch capture (matches the WPF capture path raising GotTouchCapture).</summary>
    internal void RaiseGotTouchCapture(TouchEventArgs args) => OnGotTouchCapture(args);

    /// <summary>Delivers touch capture loss (matches the WPF capture path raising LostTouchCapture).</summary>
    internal void RaiseLostTouchCapture(TouchEventArgs args) => OnLostTouchCapture(args);

    /// <summary>Pen barrel route (tunneling preview, then bubble).</summary>
    internal void RaiseStylusButton(StylusButtonEventArgs args, bool preview)
    {
        if (args.StylusButton.StylusButtonState == StylusButtonState.Down)
        {
            if (preview) OnPreviewStylusButtonDown(args);
            else OnStylusButtonDown(args);
        }
        else
        {
            if (preview) OnPreviewStylusButtonUp(args);
            else OnStylusButtonUp(args);
        }
    }

    /// <summary>Input virtuals (real UIElement surface): the default implementations raise the matching CLR event so existing routing is unchanged; converted controls override them.</summary>
    protected virtual void OnPreviewKeyDown(KeyEventArgs e) => PreviewKeyDown?.Invoke(this, e);
    protected virtual void OnKeyDown(KeyEventArgs e) => KeyDown?.Invoke(this, e);
    protected virtual void OnPreviewKeyUp(KeyEventArgs e) => PreviewKeyUp?.Invoke(this, e);
    protected virtual void OnKeyUp(KeyEventArgs e) => KeyUp?.Invoke(this, e);
    protected virtual void OnPreviewMouseDown(MouseButtonEventArgs e) => PreviewMouseDown?.Invoke(this, e);
    protected virtual void OnMouseDown(MouseButtonEventArgs e)
    {
        MouseDown?.Invoke(this, e);
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            if (e.ChangedButton == MouseButton.Left)
                OnMouseLeftButtonDown(e);
            else if (e.ChangedButton == MouseButton.Right)
                OnMouseRightButtonDown(e);
        }
    }
    /// <summary>Left-button press (matches UIElement.OnMouseLeftButtonDown):
    /// the default routes through <see cref="RaiseEvent"/> so class handlers fire, like WPF.</summary>
    protected virtual void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        MouseLeftButtonDown?.Invoke(this, e);
        e.RoutedEvent ??= MouseLeftButtonDownEvent;
        RaiseEvent(e);
    }
    /// <summary>Left-button press, tunneling (matches UIElement.OnPreviewMouseLeftButtonDown):
    /// the default routes through <see cref="RaiseEvent"/> so class handlers fire, like WPF.</summary>
    protected virtual void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        PreviewMouseLeftButtonDown?.Invoke(this, e);
        e.RoutedEvent ??= PreviewMouseLeftButtonDownEvent;
        RaiseEvent(e);
    }
    protected virtual void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        PreviewMouseUp?.Invoke(this, e);
        // Route the release too, so class handlers registered on
        // Mouse.PreviewMouseUpEvent (vendor ripples) observe it like WPF.
        e.RoutedEvent ??= Mouse.PreviewMouseUpEvent;
        RaiseEvent(e);
    }
    protected virtual void OnMouseUp(MouseButtonEventArgs e) => MouseUp?.Invoke(this, e);
    /// <summary>Barrel press, tunneling (matches OnPreviewStylusButtonDown).</summary>
    protected virtual void OnPreviewStylusButtonDown(StylusButtonEventArgs e) => PreviewStylusButtonDown?.Invoke(this, e);
    /// <summary>Barrel press (matches OnStylusButtonDown).</summary>
    protected virtual void OnStylusButtonDown(StylusButtonEventArgs e) => StylusButtonDown?.Invoke(this, e);
    /// <summary>Barrel release, tunneling (matches OnPreviewStylusButtonUp).</summary>
    protected virtual void OnPreviewStylusButtonUp(StylusButtonEventArgs e) => PreviewStylusButtonUp?.Invoke(this, e);
    /// <summary>Barrel release (matches OnStylusButtonUp).</summary>
    protected virtual void OnStylusButtonUp(StylusButtonEventArgs e) => StylusButtonUp?.Invoke(this, e);
    /// <summary>Left-button release, tunneling (matches OnPreviewMouseLeftButtonUp):
    /// the default routes through <see cref="RaiseEvent"/> so class handlers fire, like WPF.</summary>
    protected virtual void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        PreviewMouseLeftButtonUp?.Invoke(this, e);
        e.RoutedEvent ??= PreviewMouseLeftButtonUpEvent;
        RaiseEvent(e);
    }
    /// <summary>Left-button release (matches OnMouseLeftButtonUp):
    /// the default routes through <see cref="RaiseEvent"/> so class handlers fire, like WPF.</summary>
    protected virtual void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        MouseLeftButtonUp?.Invoke(this, e);
        e.RoutedEvent ??= MouseLeftButtonUpEvent;
        RaiseEvent(e);
    }
    /// <summary>Right-button press, tunneling (matches UIElement.OnPreviewMouseRightButtonDown).</summary>
    protected virtual void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e) => PreviewMouseRightButtonDown?.Invoke(this, e);
    /// <summary>Right-button press (matches UIElement.OnMouseRightButtonDown).</summary>
    protected virtual void OnMouseRightButtonDown(MouseButtonEventArgs e) => MouseRightButtonDown?.Invoke(this, e);
    /// <summary>Right-button release, tunneling (matches UIElement.OnPreviewMouseRightButtonUp).</summary>
    protected virtual void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e) => PreviewMouseRightButtonUp?.Invoke(this, e);
    /// <summary>Right-button release (matches UIElement.OnMouseRightButtonUp).</summary>
    protected virtual void OnMouseRightButtonUp(MouseButtonEventArgs e) => MouseRightButtonUp?.Invoke(this, e);
    /// <summary>Explicit capture taken (matches UIElement.OnGotMouseCapture).</summary>
    protected virtual void OnGotMouseCapture(MouseEventArgs e) => GotMouseCapture?.Invoke(this, e);
    /// <summary>Explicit capture released (matches UIElement.OnLostMouseCapture).</summary>
    protected virtual void OnLostMouseCapture(MouseEventArgs e) => LostMouseCapture?.Invoke(this, e);

    /// <summary>Delivers explicit capture (matches the WPF capture path raising GotMouseCapture).</summary>
    internal void RaiseGotMouseCapture(MouseEventArgs e) => OnGotMouseCapture(e);

    /// <summary>Delivers explicit capture loss (matches the WPF capture path raising LostMouseCapture).</summary>
    internal void RaiseLostMouseCapture(MouseEventArgs e) => OnLostMouseCapture(e);
    protected virtual void OnPreviewMouseMove(MouseEventArgs e) => PreviewMouseMove?.Invoke(this, e);
    protected virtual void OnMouseMove(MouseEventArgs e)
    {
        MouseMove?.Invoke(this, e);
        // Route moves too, so class handlers registered on
        // Mouse.MouseMoveEvent (vendor ripples) track the pointer.
        e.RoutedEvent ??= Mouse.MouseMoveEvent;
        RaiseEvent(e);
    }
    /// <summary>Touch press, tunneling (matches UIElement.OnPreviewTouchDown).</summary>
    protected virtual void OnPreviewTouchDown(TouchEventArgs e) => PreviewTouchDown?.Invoke(this, e);
    /// <summary>Touch press (matches UIElement.OnTouchDown).</summary>
    protected virtual void OnTouchDown(TouchEventArgs e) => TouchDown?.Invoke(this, e);
    /// <summary>Touch contact entered (matches UIElement.OnTouchEnter).</summary>
    protected virtual void OnTouchEnter(TouchEventArgs e) => TouchEnter?.Invoke(this, e);
    /// <summary>Touch contact left (matches UIElement.OnTouchLeave).</summary>
    protected virtual void OnTouchLeave(TouchEventArgs e) => TouchLeave?.Invoke(this, e);
    /// <summary>Touch contact moved, tunneling (matches UIElement.OnPreviewTouchMove).</summary>
    protected virtual void OnPreviewTouchMove(TouchEventArgs e) => PreviewTouchMove?.Invoke(this, e);
    /// <summary>Touch contact moved (matches UIElement.OnTouchMove).</summary>
    protected virtual void OnTouchMove(TouchEventArgs e) => TouchMove?.Invoke(this, e);
    /// <summary>Touch release, tunneling (matches UIElement.OnPreviewTouchUp).</summary>
    protected virtual void OnPreviewTouchUp(TouchEventArgs e) => PreviewTouchUp?.Invoke(this, e);
    /// <summary>Touch release (matches UIElement.OnTouchUp).</summary>
    protected virtual void OnTouchUp(TouchEventArgs e) => TouchUp?.Invoke(this, e);
    /// <summary>Touch capture taken (matches UIElement.OnGotTouchCapture).</summary>
    protected virtual void OnGotTouchCapture(TouchEventArgs e) => GotTouchCapture?.Invoke(this, e);
    /// <summary>Touch capture released (matches UIElement.OnLostTouchCapture).</summary>
    protected virtual void OnLostTouchCapture(TouchEventArgs e) => LostTouchCapture?.Invoke(this, e);
    protected virtual void OnPreviewMouseWheel(MouseWheelEventArgs e) => PreviewMouseWheel?.Invoke(this, e);
    protected virtual void OnMouseWheel(MouseWheelEventArgs e) => MouseWheel?.Invoke(this, e);
    protected virtual void OnPreviewDragEnter(DragEventArgs e) => PreviewDragEnter?.Invoke(this, e);
    protected virtual void OnDragEnter(DragEventArgs e) => DragEnter?.Invoke(this, e);
    protected virtual void OnPreviewDragOver(DragEventArgs e) => PreviewDragOver?.Invoke(this, e);
    protected virtual void OnDragOver(DragEventArgs e) => DragOver?.Invoke(this, e);
    protected virtual void OnPreviewDragLeave(DragEventArgs e) => PreviewDragLeave?.Invoke(this, e);
    protected virtual void OnDragLeave(DragEventArgs e) => DragLeave?.Invoke(this, e);
    protected virtual void OnPreviewDrop(DragEventArgs e) => PreviewDrop?.Invoke(this, e);
    protected virtual void OnDrop(DragEventArgs e) => Drop?.Invoke(this, e);
    protected virtual void OnGiveFeedback(GiveFeedbackEventArgs e) => GiveFeedback?.Invoke(this, e);
    protected virtual void OnQueryContinueDrag(QueryContinueDragEventArgs e) => QueryContinueDrag?.Invoke(this, e);
    /// <summary>Cursor query (matches UIElement.OnQueryCursor).</summary>
    protected virtual void OnQueryCursor(QueryCursorEventArgs e) => QueryCursor?.Invoke(this, e);
    /// <summary>
    /// Text preview (matches UIElement.OnPreviewTextInput): the default routes
    /// through <see cref="RaiseEvent"/> so AddHandler subscribers fire, like
    /// the converted completion-window preview path.
    /// </summary>
    protected virtual void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        e.RoutedEvent ??= PreviewTextInputEvent;
        RaiseEvent(e);
    }
    /// <summary>
    /// Text input (matches UIElement.OnTextInput): the default routes through
    /// <see cref="RaiseEvent"/> so AddHandler subscribers fire; converted
    /// AvalonEdit text areas override this slot and call back here.
    /// </summary>
    protected virtual void OnTextInput(TextCompositionEventArgs e)
    {
        e.RoutedEvent ??= TextInputEvent;
        RaiseEvent(e);
    }
    /// <summary>
    /// Delivers one text-input phase (matches the WPF preview-then-bubble
    /// TextInput route): no DOM IME composition feeds this in the session, so
    /// only explicit construction and tests drive it.
    /// </summary>
    internal void RaiseTextInput(TextCompositionEventArgs e, bool preview)
    {
        if (preview) OnPreviewTextInput(e);
        else OnTextInput(e);
    }

    /// <summary>
    /// Touch promotion gate (matches UIElement.IsManipulationEnabled, default
    /// false). Converted OxyPlot PlotViewBase sets it; the engine raises no
    /// touch input, so the value is retained but never consumed.
    /// </summary>
    public bool IsManipulationEnabled { get; set; }

    /// <summary>
    /// Touch start (matches UIElement.OnManipulationStarted): no engine touch
    /// pipeline exists, so the default is a no-op. Converted controls
    /// (OxyPlot PlotViewBase) override this and call the base implementation.
    /// </summary>
    protected virtual void OnManipulationStarted(ManipulationStartedEventArgs e)
    {
    }

    /// <summary>
    /// Touch move (matches UIElement.OnManipulationDelta): no engine touch
    /// pipeline exists, so the default is a no-op. Converted controls
    /// (OxyPlot PlotViewBase) override this and call the base implementation.
    /// </summary>
    protected virtual void OnManipulationDelta(ManipulationDeltaEventArgs e)
    {
    }

    /// <summary>
    /// Touch end (matches UIElement.OnManipulationCompleted): no engine touch
    /// pipeline exists, so the default is a no-op. Converted controls
    /// (OxyPlot PlotViewBase) override this and call the base implementation.
    /// </summary>
    protected virtual void OnManipulationCompleted(ManipulationCompletedEventArgs e)
    {
    }

    /// <summary>Touch-setup notice (matches UIElement.ManipulationStarting).</summary>
    public event ManipulationStartingEventHandler? ManipulationStarting;

    /// <summary>Inertia-setup notice (matches UIElement.ManipulationInertiaStarting).</summary>
    public event ManipulationInertiaStartingEventHandler? ManipulationInertiaStarting;

    /// <summary>
    /// Touch setup (matches UIElement.OnManipulationStarting): no engine
    /// touch pipeline exists, so the default invokes subscribers.
    /// Converted behaviors (Behaviors TranslateZoomRotateBehavior) attach
    /// here through the handlers below.
    /// </summary>
    protected virtual void OnManipulationStarting(ManipulationStartingEventArgs e) =>
        ManipulationStarting?.Invoke(this, e);

    /// <summary>
    /// Inertia setup (matches UIElement.OnManipulationInertiaStarting): no
    /// engine touch pipeline exists, so the default invokes subscribers.
    /// </summary>
    protected virtual void OnManipulationInertiaStarting(ManipulationInertiaStartingEventArgs e) =>
        ManipulationInertiaStarting?.Invoke(this, e);

    /// <summary>
    /// Delivers one drag-drop phase to this element: the preview CLR event,
    /// then the tunneled preview routed event, then (unless handled) the
    /// bubble CLR event and the bubbled routed event. The same args instance
    /// travels both legs with its <see cref="RoutedEventArgs.RoutedEvent"/>
    /// retargeted, like desktop drag routing. Used by the HTML5 file-drop
    /// bridge and engine-native raisers.
    /// </summary>
    public void RaiseDragEvent(DragEventArgs args, RoutedEvent previewEvent, RoutedEvent bubbleEvent)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(previewEvent);
        ArgumentNullException.ThrowIfNull(bubbleEvent);
        var (previewClr, bubbleClr) = DragPhaseHandlers(previewEvent, bubbleEvent);
        args.Source ??= this;
        args.OriginalSource ??= args.Source;
        args.RoutedEvent = previewEvent;
        previewClr(this, args);
        RaiseEvent(args);
        if (args.Handled) return;
        args.RoutedEvent = bubbleEvent;
        bubbleClr(this, args);
        RaiseEvent(args);
    }

    private (DragEventHandler Preview, DragEventHandler Bubble) DragPhaseHandlers(
        RoutedEvent previewEvent, RoutedEvent bubbleEvent)
    {
        if (ReferenceEquals(previewEvent, DragDrop.PreviewDragEnterEvent) &&
            ReferenceEquals(bubbleEvent, DragDrop.DragEnterEvent))
            return ((_, e) => OnPreviewDragEnter(e), (_, e) => OnDragEnter(e));
        if (ReferenceEquals(previewEvent, DragDrop.PreviewDragOverEvent) &&
            ReferenceEquals(bubbleEvent, DragDrop.DragOverEvent))
            return ((_, e) => OnPreviewDragOver(e), (_, e) => OnDragOver(e));
        if (ReferenceEquals(previewEvent, DragDrop.PreviewDragLeaveEvent) &&
            ReferenceEquals(bubbleEvent, DragDrop.DragLeaveEvent))
            return ((_, e) => OnPreviewDragLeave(e), (_, e) => OnDragLeave(e));
        if (ReferenceEquals(previewEvent, DragDrop.PreviewDropEvent) &&
            ReferenceEquals(bubbleEvent, DragDrop.DropEvent))
            return ((_, e) => OnPreviewDrop(e), (_, e) => OnDrop(e));
        throw new ArgumentException("RaiseDragEvent needs a matching DragDrop preview/bubble event pair.");
    }
    protected virtual void OnMouseEnter(MouseEventArgs e) => MouseEnter?.Invoke(this, e);
    protected virtual void OnMouseLeave(MouseEventArgs e) => MouseLeave?.Invoke(this, e);
    /// <summary>Keyboard focus arrived (matches OnGotKeyboardFocus):
    /// the default routes through <see cref="RaiseEvent"/> so class handlers fire, like WPF.</summary>
    protected virtual void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        GotKeyboardFocus?.Invoke(this, e);
        e.RoutedEvent ??= GotKeyboardFocusEvent;
        RaiseEvent(e);
    }
    /// <summary>Keyboard focus left (matches OnLostKeyboardFocus):
    /// the default routes through <see cref="RaiseEvent"/> so class handlers fire, like WPF.</summary>
    protected virtual void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        LostKeyboardFocus?.Invoke(this, e);
        e.RoutedEvent ??= LostKeyboardFocusEvent;
        RaiseEvent(e);
    }

    /// <summary>
    /// Focus virtuals with routed args (real UIElement surface). The defaults
    /// raise the CLR events and then run the legacy parameterless hooks so
    /// existing focus behavior (TextBox caret) is unchanged.
    /// </summary>
    protected virtual void OnGotFocus(RoutedEventArgs e)
    {
        GotFocus?.Invoke(this, e);
        OnGotFocus();
        e.RoutedEvent ??= GotFocusEvent;
        RaiseEvent(e);
    }

    protected virtual void OnLostFocus(RoutedEventArgs e)
    {
        LostFocus?.Invoke(this, e);
        OnLostFocus();
        e.RoutedEvent ??= LostFocusEvent;
        RaiseEvent(e);
    }

    private readonly Dictionary<RoutedEvent, List<(Delegate Handler, bool HandledToo)>> _routedHandlers = new();

    /// <summary>Attaches a routed handler (matches UIElement.AddHandler).</summary>
    public void AddHandler(RoutedEvent routedEvent, Delegate handler) => AddHandler(routedEvent, handler, false);

    /// <summary>Attaches a routed handler, optionally invoked even when handled.</summary>
    public void AddHandler(RoutedEvent routedEvent, Delegate handler, bool handledEventsToo)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);
        lock (_routedHandlers)
        {
            if (!_routedHandlers.TryGetValue(routedEvent, out var list))
                _routedHandlers[routedEvent] = list = new();
            list.Add((handler, handledEventsToo));
        }
    }

    /// <summary>Detaches a routed handler (matches UIElement.RemoveHandler).</summary>
    public void RemoveHandler(RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);
        lock (_routedHandlers)
        {
            if (!_routedHandlers.TryGetValue(routedEvent, out var list)) return;
            list.RemoveAll(entry => entry.Handler == handler);
            if (list.Count == 0) _routedHandlers.Remove(routedEvent);
        }
    }

    /// <summary>
    /// Raises a routed event (matches UIElement.RaiseEvent): target handlers,
    /// then Bubble up the parent chain, Tunnel down from the root, or target
    /// only for Direct. Handled stops normal handlers; handledEventsToo
    /// handlers still run while routing continues.
    /// </summary>
    public void RaiseEvent(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var routed = e.RoutedEvent
            ?? throw new InvalidOperationException("RoutedEventArgs.RoutedEvent must be set before RaiseEvent.");
        e.Source ??= this;
        e.OriginalSource ??= e.Source;
        var route = routed.RoutingStrategy switch
        {
            RoutingStrategy.Direct => new[] { this },
            RoutingStrategy.Tunnel => AncestorsAndSelf().Reverse().ToArray(),
            _ => AncestorsAndSelf().ToArray(),
        };
        foreach (var element in route) element.InvokeRoutedHandlers(routed, e);
    }

    internal void InvokeRoutedHandlers(RoutedEvent routed, RoutedEventArgs e)
    {
        // Class handlers run ahead of the instance handlers at every route
        // node, like WPF (converted AvalonDock drop-down areas register
        // theirs for the whole class).
        EventManager.InvokeClassHandlers(this, routed, e);
        List<(Delegate Handler, bool HandledToo)> snapshot;
        lock (_routedHandlers)
        {
            if (!_routedHandlers.TryGetValue(routed, out var list)) return;
            snapshot = new(list);
        }
        foreach (var (handler, handledToo) in snapshot)
        {
            if (e.Handled && !handledToo) continue;
            try
            {
                handler.DynamicInvoke(this, e);
            }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }
    }

    private IEnumerable<UIElement> AncestorsAndSelf()
    {
        for (var current = this; current is not null; current = current.Parent) yield return current;
    }

    /// <summary>
    /// Mints the automation peer (matches UIElement.OnCreateAutomationPeer,
    /// the slot converted AvalonEdit text peers override). Null by default,
    /// like WPF; CreatePeerForElement falls back to a direct peer then.
    /// </summary>
    protected virtual AutomationPeer? OnCreateAutomationPeer() => null;

    /// <summary>
    /// Invokes the protected peer slot (the engine-internal path
    /// CreatePeerForElement uses: the static peer factory cannot reach the
    /// protected slot directly).
    /// </summary>
    internal AutomationPeer? CreateAutomationPeer() => OnCreateAutomationPeer();

    /// <summary>Keyboard focus arrived (caret/show-focus affordances go here).</summary>
    public virtual void OnGotFocus() { }

    /// <summary>
    /// Focus preview hook (WPF parity): the input manager calls this before
    /// moving focus; setting <see cref="RoutedEventArgs.Handled"/> vetoes the
    /// move. Converted read-only displays (calculator MyTextBox) override it.
    /// The default raises <see cref="PreviewGotKeyboardFocus"/>.
    /// </summary>
    /// <summary>Keyboard-focus preview hook (WPF parity): the default raises
    /// <see cref="PreviewGotKeyboardFocus"/> then routes so class handlers fire, like WPF.</summary>
    protected internal virtual void OnPreviewGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        PreviewGotKeyboardFocus?.Invoke(this, e);
        e.RoutedEvent ??= PreviewGotKeyboardFocusEvent;
        RaiseEvent(e);
    }

    /// <summary>
    /// Focus-loss preview hook (WPF parity): the input manager calls this on
    /// the old holder before releasing focus; setting
    /// <see cref="RoutedEventArgs.Handled"/> vetoes the move.
    /// The default raises <see cref="PreviewLostKeyboardFocus"/>.
    /// </summary>
    /// <summary>Keyboard-focus-loss preview hook (WPF parity): the default raises
    /// <see cref="PreviewLostKeyboardFocus"/> then routes so class handlers fire, like WPF.</summary>
    protected internal virtual void OnPreviewLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        PreviewLostKeyboardFocus?.Invoke(this, e);
        e.RoutedEvent ??= PreviewLostKeyboardFocusEvent;
        RaiseEvent(e);
    }

    /// <summary>Reports a focus-state flip (the input manager calls this after each committed move).</summary>
    internal void RaiseIsKeyboardFocusedChanged(bool oldValue, bool newValue)
    {
        IsKeyboardFocusedChanged?.Invoke(this, new DependencyPropertyChangedEventArgs(IsKeyboardFocusedProperty, oldValue, newValue));
        IsKeyboardFocusWithinChanged?.Invoke(this, new DependencyPropertyChangedEventArgs(IsKeyboardFocusedProperty, oldValue, newValue));
    }

    /// <summary>Keyboard focus left (hide caret/show-focus affordances here).</summary>
    public virtual void OnLostFocus() { }

    /// <summary>Routed-event entry point (tunneling Preview* first, then bubbling).</summary>
    public virtual void OnRoutedEvent(WasmRoutedEvent e) { }

    private WpfSize _lastAvailable;
    private bool _measuredOnce;

    public void Measure(WpfSize available)
    {
        _lastAvailable = available;
        _measuredOnce = true;
        var desired = ClampDesired(MeasureCore(ClampAvailable(available)));
        // Layout invariant (matches RenderSize never going negative or NaN):
        // the Size contract throws on negatives, so coerce here instead of
        // letting one cell kill the whole measure pass.
        DesiredSize = new Size(
            double.IsNaN(desired.Width) || desired.Width < 0 ? 0 : desired.Width,
            double.IsNaN(desired.Height) || desired.Height < 0 ? 0 : desired.Height);
        IsMeasureDirty = false;
    }

    /// <summary>
    /// WPF-shape measure (matches UIElement.Measure(Size)): converted OxyPlot
    /// text layout binds this signature; the engine layout core stays on
    /// WpfSize, converted implicitly both ways.
    /// </summary>
    public void Measure(Size available) => Measure((WpfSize)available);

    /// <summary>
    /// WPF-shape arrange (matches UIElement.Arrange(Rect)): converted
    /// AvalonDock tab panels bind this signature through their
    /// ArrangeOverride base calls; the engine layout core stays on
    /// WpfRect, converted implicitly both ways.
    /// </summary>
    public void Arrange(Rect finalRect) => Arrange((WpfRect)finalRect);

    /// <summary>
    /// Forces pending layout to run now (matches UIElement.UpdateLayout;
    /// the gallery navigation service calls it before navigating, so the
    /// frame must be measurable without waiting for the next tick).
    /// </summary>
    public void UpdateLayout()
    {
        if (IsMeasureDirty && _measuredOnce) Measure(_lastAvailable);
        if (IsArrangeDirty && (ArrangedRect.Width > 0 || ArrangedRect.Height > 0)) Arrange(ArrangedRect);
    }

    /// <summary>Layout-system hooks so Width/Height beat content size.</summary>
    protected virtual WpfSize ClampAvailable(WpfSize available) => available;

    /// <summary>Layout-system hooks so Width/Height beat content size.</summary>
    protected virtual WpfSize ClampDesired(WpfSize desired) => desired;

    public void Arrange(WpfRect finalRect)
    {
        var oldSize = new WpfSize(RenderRect.Width, RenderRect.Height);
        ArrangedRect = finalRect;
        finalRect = ClampArrange(finalRect);
        // Layout invariant (matches RenderSize never going negative or NaN):
        // parents may over-subtract margins/padding, so the choke point
        // coerces instead of letting one cell kill the whole arrange pass.
        if (double.IsNaN(finalRect.Width) || finalRect.Width < 0) finalRect = finalRect with { Width = 0 };
        if (double.IsNaN(finalRect.Height) || finalRect.Height < 0) finalRect = finalRect with { Height = 0 };
        RenderRect = finalRect;
        ArrangeCore(finalRect);
        IsArrangeDirty = false;
        var newSize = new WpfSize(RenderRect.Width, RenderRect.Height);
        if (newSize.Width != oldSize.Width || newSize.Height != oldSize.Height)
            OnRenderSizeChanged(oldSize, newSize);
        // Layout-pass completion (matches UIElement.LayoutUpdated): every
        // arranged element notifies once its slot is final, like the
        // framework dispatcher does after a layout pass.
        LayoutUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Size-change hook (matches FrameworkElement.OnRenderSizeChanged): the
    /// base is a no-op; FrameworkElement raises <c>SizeChanged</c> here so
    /// only real size deltas notify (same-size re-arranges stay silent).
    /// </summary>
    protected virtual void OnRenderSizeChanged(WpfSize oldSize, WpfSize newSize)
    {
    }

    protected virtual WpfSize MeasureCore(WpfSize available) => new(0, 0);

    protected virtual void ArrangeCore(WpfRect finalRect) { }
    protected virtual WpfRect ClampArrange(WpfRect finalRect) => finalRect;

    /// <summary>
    /// Paint hook (matches System.Windows.UIElement.OnRender): protected
    /// virtual no-op. Converted controls override this protected override,
    /// so the engine must not widen it to public abstract (that breaks
    /// converted overrides with TypeLoadException "cannot reduce access").
    /// The compositor and tests paint through <see cref="Render"/>.
    /// </summary>
    protected virtual void OnRender(DrawingContext dc)
    {
    }

    /// <summary>
    /// Engine-internal paint entry: virtual-dispatches to the protected
    /// <see cref="OnRender"/> override so containers, the compositor and
    /// tests can paint an element they do not derive from.
    /// </summary>
    internal void Render(DrawingContext dc) => OnRender(dc);
}

/// <summary>FrameworkElement: Width/Height + children collection.</summary>
public abstract class FrameworkElement : UIElement
{
    protected override WpfRect ClampArrange(WpfRect slot) =>
        ArrangeAligned(slot, Width, Height, HorizontalAlignment, VerticalAlignment,
            Math.Max(0, DesiredSize.Width), Math.Max(0, DesiredSize.Height));

    /// <summary>
    /// Shared arrange-slot resolution (matches FrameworkElement arrange):
    /// an explicit dimension wins; an Auto dimension fills the slot under
    /// Stretch and sizes to content (clamped into the slot) under any
    /// other alignment, with the offset docking it (a Bottom-anchored
    /// panel with no explicit size sits at the slot's bottom instead of
    /// overlapping the whole slot). Explicit-size offsets keep their
    /// existing behavior.
    /// </summary>
    internal static WpfRect ArrangeAligned(WpfRect slot, double width, double height,
        HorizontalAlignment ha, VerticalAlignment va, double desiredW, double desiredH)
    {
        var stretchW = ha == HorizontalAlignment.Stretch;
        var stretchH = va == VerticalAlignment.Stretch;
        var w = double.IsNaN(width)
            ? (stretchW ? slot.Width : Math.Min(slot.Width, desiredW))
            : Math.Max(0, width);
        var h = double.IsNaN(height)
            ? (stretchH ? slot.Height : Math.Min(slot.Height, desiredH))
            : Math.Max(0, height);
        var x = slot.X + (double.IsNaN(width) && stretchW ? 0 : ha switch
        { HorizontalAlignment.Center => (slot.Width - w) / 2,
          HorizontalAlignment.Stretch when w <= slot.Width => (slot.Width - w) / 2,
          HorizontalAlignment.Right => slot.Width - w, _ => 0 });
        var y = slot.Y + (double.IsNaN(height) && stretchH ? 0 : va switch
        { VerticalAlignment.Center => (slot.Height - h) / 2,
          VerticalAlignment.Bottom => slot.Height - h, _ => 0 });
        return new WpfRect(x, y, w, h);
    }

    public static readonly DependencyProperty NameProperty =
        DependencyProperty.Register("Name", typeof(string), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(string.Empty));
    public string Name { get => (string)(GetValue(NameProperty) ?? string.Empty); set => SetValue(NameProperty, value); }

    /// <summary>
    /// Root visual states attach to (matches FrameworkElement.StateGroupsRoot;
    /// only templated controls override this; null keeps GoToState false).
    /// </summary>
    internal virtual FrameworkElement? StateGroupsRoot => null;

    /// <summary>
    /// Default style key (matches FrameworkElement.DefaultStyleKeyProperty /
    /// DefaultStyleKey): converted OxyPlot.Wpf PlotViewBase overrides the
    /// metadata in its static constructor, so the engine must own this
    /// property or converted views fail at instantiation with
    /// MissingFieldException. Retained only; implicit-style lookup keeps
    /// its existing type-walk behavior.
    /// </summary>
    public static readonly DependencyProperty DefaultStyleKeyProperty =
        DependencyProperty.Register("DefaultStyleKey", typeof(object), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(null));

    /// <summary>Default style key (matches FrameworkElement.DefaultStyleKey).</summary>
    public object? DefaultStyleKey
    {
        get => GetValue(DefaultStyleKeyProperty);
        set => SetValue(DefaultStyleKeyProperty, value);
    }

    /// <summary>Theme-style opt-out identity (matches
    /// FrameworkElement.OverridesDefaultStyleProperty; default false like
    /// WPF. Store-backed so style setters land through the DP store; the
    /// UIElement CLR wrapper bridges here for framework elements).</summary>
    public static readonly DependencyProperty OverridesDefaultStyleProperty =
        DependencyProperty.Register("OverridesDefaultStyle", typeof(bool), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(false));

    /// <summary>Pointer cursor identity (matches
    /// FrameworkElement.CursorProperty; default null like WPF, retained with
    /// no cursor layer). Store-backed so the staged RowHeaderGripperStyle
    /// Cursor setter lands through the DP store; hides the UIElement CLR so
    /// element, style and template writes share one store.</summary>
    public static readonly DependencyProperty CursorProperty =
        DependencyProperty.Register("Cursor", typeof(Cursor), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(null));

    /// <summary>Pointer cursor (matches FrameworkElement.Cursor).</summary>
    public new Cursor? Cursor
    {
        get => (Cursor?)GetValue(CursorProperty);
        set => SetValue(CursorProperty, value);
    }

    /// <summary>Document language identity (matches
    /// FrameworkElement.LanguageProperty; default en-US like WPF.
    /// Store-backed so converted vendor code reading Language (font
    /// fallback, text shaping) binds against the engine type).</summary>
    public static readonly DependencyProperty LanguageProperty =
        DependencyProperty.Register("Language", typeof(XmlLanguage), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("en-US")));

    /// <summary>Document language (matches FrameworkElement.Language).</summary>
    public XmlLanguage Language
    {
        get => (XmlLanguage)(GetValue(LanguageProperty) ?? XmlLanguage.GetLanguage("en-US"));
        set => SetValue(LanguageProperty, value);
    }

    /// <summary>Reads the language (matches FrameworkElement.GetLanguage).</summary>
    public static XmlLanguage GetLanguage(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (XmlLanguage)(target.GetValue(LanguageProperty) ?? XmlLanguage.GetLanguage("en-US"));
    }

    /// <summary>Stores the language (matches FrameworkElement.SetLanguage).</summary>
    public static void SetLanguage(DependencyObject target, XmlLanguage value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(LanguageProperty, value);
    }

    /// <summary>Arbitrary payload identity (matches FrameworkElement.TagProperty).</summary>
    public static readonly DependencyProperty TagProperty =
        DependencyProperty.Register("Tag", typeof(object), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(null));

    /// <summary>
    /// Arbitrary payload (matches FrameworkElement.Tag): converted gallery
    /// code-behind reads it (MenuPage distinguishes its icon MenuItems by
    /// Tag), and loose XAML sets it through the plain-CLR fallback.
    /// </summary>
    public object? Tag
    {
        get => GetValue(TagProperty);
        set => SetValue(TagProperty, value);
    }

    /// <summary>
    /// Namescope search (ElementName bindings and code-behind FindName calls
    /// resolve here): walks up to the logical root, then depth-first down,
    /// so siblings and ancestors resolve like a real WPF namescope.
    /// When elements reside in custom layout models (such as AvalonDock anchorables
    /// or unparented popups), falls back to the attached NameScope on the root.
    /// </summary>
    public object? FindName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var root = this;
        while (root.Parent is FrameworkElement parent) root = parent;
        var found = root.FindDescendant(name);
        if (found is not null) return found;

        if (NameScope.GetNameScope(root)?.FindName(name) is { } fromRootScope)
            return fromRootScope;

        for (FrameworkElement? node = this; node is not null && node != root; node = node.Parent as FrameworkElement)
        {
            if (NameScope.GetNameScope(node)?.FindName(name) is { } fromNodeScope)
                return fromNodeScope;
        }

        return null;
    }

    private object? FindDescendant(string name)
    {
        if (string.Equals(Name, name, StringComparison.Ordinal)) return this;
        foreach (var child in LogicalChildren)
            if (child is FrameworkElement fe && fe.FindDescendant(name) is { } found)
                return found;
        return null;
    }

    /// <summary>
    /// Real-WPF-shaped logical parent: WPF declares this on
    /// FrameworkElement returning DependencyObject, and converted
    /// codebinds that exact signature (e.g. CalculatorDemo's Calc
    /// walks Button.Parent). Same stored link as UIElement.Parent.
    /// </summary>
    public new DependencyObject? Parent => base.Parent;

    /// <summary>
    /// Raised once the element tree is constructed and wired (converted
    /// Page/Window Loaded handlers bind here; v1 raises at hydration,
    /// synchronously, rather than on first presentation like real WPF).
    /// </summary>
    public event RoutedEventHandler? Loaded;

    /// <summary>Raises <see cref="Loaded"/> on this subtree, parents first.</summary>
    internal void RaiseLoadedTree()
    {
        _isLoaded = true;
        Loaded?.Invoke(this, new RoutedEventArgs { Source = this });
        // Snapshot: Loaded handlers (deferred binding re-eval) attach
        // visuals mid-walk; same re-entrancy contract as RefreshBindingsTree.
        foreach (var child in LogicalChildren.ToArray())
            if (child is FrameworkElement fe)
                fe.RaiseLoadedTree();
    }

    /// <summary>
    /// Clears <see cref="IsLoaded"/> and raises <see cref="Unloaded"/> on
    /// this subtree, parents first (mirrors <see cref="RaiseLoadedTree"/>).
    /// </summary>
    internal void RaiseUnloadedTree()
    {
        if (_isLoaded)
        {
            _isLoaded = false;
            Unloaded?.Invoke(this, new RoutedEventArgs { Source = this });
        }
        foreach (var child in LogicalChildren.ToArray())
            if (child is FrameworkElement fe)
                fe.RaiseUnloadedTree();
    }

    private int _initDepth;
    private bool _isInitialized;

    /// <summary>
    /// Initialization state (matches FrameworkElement.IsInitialized; set by
    /// <see cref="EndInit"/>, so code-built trees read false until their
    /// initializer completes, like real WPF).
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>Signals initialization start (matches ISupportInitialize.BeginInit).</summary>
    public virtual void BeginInit() => _initDepth++;

    /// <summary>
    /// Signals initialization end (matches ISupportInitialize.EndInit): the
    /// EndInit that unwinds the last <see cref="BeginInit"/> flips
    /// <see cref="IsInitialized"/> and runs <see cref="OnInitialized"/>,
    /// like real WPF (an unmatched EndInit completes a never-begun init).
    /// </summary>
    public virtual void EndInit()
    {
        if (_initDepth > 0) _initDepth--;
        if (_initDepth != 0 || _isInitialized) return;
        _isInitialized = true;
        OnInitialized(EventArgs.Empty);
    }

    /// <summary>
    /// Initialization hook (matches FrameworkElement.OnInitialized; converted
    /// layout controls override this and the base raises
    /// <see cref="Initialized"/>).
    /// </summary>
    protected virtual void OnInitialized(EventArgs e) => Initialized?.Invoke(this, e);

    /// <summary>Raised when initialization completes (matches FrameworkElement.Initialized).</summary>
    public event EventHandler? Initialized;

    private bool _isLoaded;

    /// <summary>
    /// Presentation state (matches FrameworkElement.IsLoaded; set by the
    /// load walk, cleared when the element leaves its parent).
    /// </summary>
    public bool IsLoaded => _isLoaded;

    /// <summary>Raised when the element leaves the live tree (matches FrameworkElement.Unloaded).</summary>
    public event RoutedEventHandler? Unloaded;

    private readonly List<object?> _nonVisualLogicalChildren = new();

    /// <summary>
    /// Logical-tree attach (matches FrameworkElement.AddLogicalChild):
    /// visual children join the child list with parenting, resource and
    /// binding refresh; non-visual children (layout models converted
    /// docking managers pass) are retained without throwing, like WPF.
    /// </summary>
    protected internal void AddLogicalChild(object? child)
    {
        // Native parity (D1 oracle: Add(null) is NO-THROW): ignore null like
        // RemoveLogicalChild does; never retain phantom entries.
        if (child is null) return;
        if (child is UIElement element)
        {
            AddChild(element);
            return;
        }
        if (!_nonVisualLogicalChildren.Contains(child))
            _nonVisualLogicalChildren.Add(child);
    }

    /// <summary>
    /// Logical-tree detach (matches FrameworkElement.RemoveLogicalChild).
    /// </summary>
    protected internal void RemoveLogicalChild(object? child)
    {
        if (child is null) return;
        if (child is UIElement element)
        {
            RemoveChild(element);
            return;
        }
        _nonVisualLogicalChildren.Remove(child);
    }

    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register("Width", typeof(double), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(double.NaN) { AffectsMeasure = true });
    public static readonly DependencyProperty HeightProperty =
        DependencyProperty.Register("Height", typeof(double), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(double.NaN) { AffectsMeasure = true });

    public double Width { get => (double)(GetValue(WidthProperty) ?? double.NaN); set => SetValue(WidthProperty, value); }
    public double Height { get => (double)(GetValue(HeightProperty) ?? double.NaN); set => SetValue(HeightProperty, value); }

    /// <summary>Horizontal alignment (matches FrameworkElement.HorizontalAlignmentProperty; Stretch default like WPF).</summary>
    public static readonly DependencyProperty HorizontalAlignmentProperty =
        DependencyProperty.Register("HorizontalAlignment", typeof(HorizontalAlignment), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(HorizontalAlignment.Stretch) { AffectsArrange = true });

    /// <summary>Horizontal alignment inside the allotted slot (default Stretch).</summary>
    public HorizontalAlignment HorizontalAlignment
    {
        get => (HorizontalAlignment?)GetValue(HorizontalAlignmentProperty) ?? HorizontalAlignment.Stretch;
        set => SetValue(HorizontalAlignmentProperty, value);
    }

    /// <summary>Vertical alignment (matches FrameworkElement.VerticalAlignmentProperty; Stretch default like WPF).</summary>
    public static readonly DependencyProperty VerticalAlignmentProperty =
        DependencyProperty.Register("VerticalAlignment", typeof(VerticalAlignment), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(VerticalAlignment.Stretch) { AffectsArrange = true });

    /// <summary>Vertical alignment inside the allotted slot (default Stretch, like WPF).</summary>
    public VerticalAlignment VerticalAlignment
    {
        get => (VerticalAlignment?)GetValue(VerticalAlignmentProperty) ?? VerticalAlignment.Stretch;
        set => SetValue(VerticalAlignmentProperty, value);
    }

    /// <summary>Margin dependency property (animation target for slide in/out).</summary>
    public static readonly DependencyProperty MarginProperty =
        DependencyProperty.Register("Margin", typeof(Thickness), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(new Thickness(0)) { AffectsMeasure = true });

    /// <summary>Outer margin around the element (default zero).</summary>
    public Thickness Margin
    {
        get => (Thickness)(GetValue(MarginProperty) ?? new Thickness(0));
        set => SetValue(MarginProperty, value);
    }

    /// <summary>
    /// Hover-hint slot (matches FrameworkElement.ToolTipProperty): the shared
    /// DP backs the CLR so converted SetValue(ToolTipProperty) callers land
    /// visibly; non-null values still wire the hover service exactly once
    /// per change like before.
    /// </summary>
    public static readonly DependencyProperty ToolTipProperty =
        DependencyProperty.Register("ToolTip", typeof(object), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(null, (d, e) =>
            {
                if (e.NewValue is not null) ToolTipService.EnsureWired((FrameworkElement)d);
            }));

    /// <summary>
    /// Hover hint (string paints as text, a ToolTip element hosts live
    /// content). ToolTipService opens it on hover and paints it in the
    /// overlay pass, so owners never reflow around it.
    /// </summary>
    public object? ToolTip
    {
        get => GetValue(ToolTipProperty);
        set => SetValue(ToolTipProperty, value);
    }

    /// <summary>
    /// Explicit-style slot (matches FrameworkElement.StyleProperty): the
    /// shared DP backs the CLR so converted SetValue(StyleProperty) callers
    /// land visibly; applying stays immediate like before.
    /// </summary>
    public static readonly DependencyProperty StyleProperty =
        DependencyProperty.Register("Style", typeof(Style), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(null, (d, e) =>
            {
                if (!ReferenceEquals(e.OldValue, e.NewValue))
                    ((FrameworkElement)d).ApplyStyled((Style?)e.NewValue);
            }));

    /// <summary>Explicit style (applied immediately; implicit styles apply at load).</summary>
    public Style? Style
    {
        get => (Style?)GetValue(StyleProperty);
        set => SetValue(StyleProperty, value);
    }

    private void ApplyStyled(Style? style) => style?.ApplyTo(this);

    /// <summary>
    /// Right-click menu slot (matches FrameworkElement.ContextMenuProperty;
    /// retained, pop-up placement stays a boundary like the TextBox slot it
    /// now backs).
    /// </summary>
    public static readonly DependencyProperty ContextMenuProperty =
        DependencyProperty.Register("ContextMenu", typeof(ContextMenu), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(null));

    /// <summary>Right-click menu (parsed and retained; pop-up placement stays a boundary).</summary>
    public ContextMenu? ContextMenu
    {
        get => (ContextMenu?)GetValue(ContextMenuProperty);
        set => SetValue(ContextMenuProperty, value);
    }

    /// <summary>Menu-opening token (matches FrameworkElement.ContextMenuOpeningEvent; Bubble like WPF).</summary>
    public static readonly RoutedEvent ContextMenuOpeningEvent = EventManager.RegisterRoutedEvent(
        "ContextMenuOpening", RoutingStrategy.Bubble, typeof(ContextMenuEventHandler), typeof(FrameworkElement));

    /// <summary>Menu-closing token (matches FrameworkElement.ContextMenuClosingEvent; Bubble like WPF).</summary>
    public static readonly RoutedEvent ContextMenuClosingEvent = EventManager.RegisterRoutedEvent(
        "ContextMenuClosing", RoutingStrategy.Bubble, typeof(ContextMenuEventHandler), typeof(FrameworkElement));

    /// <summary>
    /// Menu opening (matches FrameworkElement.ContextMenuOpening; converted
    /// TextFieldAssist subscribes this to inject spelling suggestions).
    /// </summary>
    public event ContextMenuEventHandler? ContextMenuOpening
    {
        add => AddHandler(ContextMenuOpeningEvent, value);
        remove => RemoveHandler(ContextMenuOpeningEvent, value);
    }

    /// <summary>Menu closing (matches FrameworkElement.ContextMenuClosing).</summary>
    public event ContextMenuEventHandler? ContextMenuClosing
    {
        add => AddHandler(ContextMenuClosingEvent, value);
        remove => RemoveHandler(ContextMenuClosingEvent, value);
    }

    /// <summary>Raises <see cref="ContextMenuOpening"/> (matches FrameworkElement.OnContextMenuOpening).</summary>
    protected virtual void OnContextMenuOpening(ContextMenuEventArgs e) => RaiseEvent(e);

    /// <summary>Raises <see cref="ContextMenuClosing"/> (matches FrameworkElement.OnContextMenuClosing).</summary>
    protected virtual void OnContextMenuClosing(ContextMenuEventArgs e) => RaiseEvent(e);

    /// <summary>Dispatches the opening notification (the menu host calls this before opening).</summary>
    internal void RaiseContextMenuOpening(ContextMenuEventArgs e)
    {
        e.RoutedEvent = ContextMenuOpeningEvent;
        e.Source ??= this;
        OnContextMenuOpening(e);
    }

    /// <summary>Dispatches the closing notification (the menu host calls this while closing).</summary>
    internal void RaiseContextMenuClosing(ContextMenuEventArgs e)
    {
        e.RoutedEvent = ContextMenuClosingEvent;
        e.Source ??= this;
        OnContextMenuClosing(e);
    }

    /// <summary>
    /// Base text direction (matches FrameworkElement.FlowDirectionProperty;
    /// Inherits metadata like WPF). The CLR keeps the parsed/retained
    /// behavior gallery pages rely on, now store-backed so converted
    /// SetValue(FlowDirectionProperty) callers land visibly; right-to-left
    /// layout stays a boundary and LeftToRight renders exactly as before.
    /// </summary>
    public static readonly DependencyProperty FlowDirectionProperty =
        DependencyProperty.Register("FlowDirection", typeof(FlowDirection), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(FlowDirection.LeftToRight, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Base text direction (inherited; default LeftToRight).</summary>
    public FlowDirection FlowDirection
    {
        get => (FlowDirection?)GetValue(FlowDirectionProperty) ?? FlowDirection.LeftToRight;
        set => SetValue(FlowDirectionProperty, value);
    }

    /// <summary>
    /// Effective text direction (matches FrameworkElement.GetFlowDirection;
    /// live-verified against PresentationFramework 9.0.21): the nearest
    /// self-or-ancestor value wins, defaulting to LeftToRight. Reads the
    /// DP-store provenance so local and style/template values inherit like
    /// real WPF instead of stopping at the CLR default.
    /// </summary>
    public static FlowDirection GetFlowDirection(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        for (var node = element; node is FrameworkElement fe; node = fe.Parent)
        {
            var source = DependencyPropertyHelper.GetValueSource(fe, FlowDirectionProperty).BaseValueSource;
            // Inherited provenance marks exactly the nodes the value flows
            // through: they carry no value of their own (the store has no
            // DP-level inheritance), so only value-bearing provenances win.
            if (source != BaseValueSource.Default && source != BaseValueSource.Inherited)
                return (FlowDirection?)fe.GetValue(FlowDirectionProperty) ?? FlowDirection.LeftToRight;
        }
        return FlowDirection.LeftToRight;
    }

    /// <summary>Direction write (matches FrameworkElement.SetFlowDirection).</summary>
    public static void SetFlowDirection(DependencyObject element, FlowDirection value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FlowDirectionProperty, value);
    }

    /// <summary>
    /// Layout-time transform slot (matches
    /// FrameworkElement.LayoutTransformProperty; default Identity like WPF).
    /// Retained and measure-invalidating; applying the transform through
    /// measure/arrange stays a boundary.
    /// </summary>
    public static readonly DependencyProperty LayoutTransformProperty =
        DependencyProperty.Register("LayoutTransform", typeof(Transform), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(Transform.Identity) { AffectsMeasure = true });

    /// <summary>Layout-time transform (default Identity; retained).</summary>
    public Transform LayoutTransform
    {
        get => (Transform?)GetValue(LayoutTransformProperty) ?? Transform.Identity;
        set => SetValue(LayoutTransformProperty, value);
    }

    public static readonly DependencyProperty FocusVisualStyleProperty =
        DependencyProperty.Register("FocusVisualStyle", typeof(Style), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(null));

    /// <summary>
    /// Focus visuals (matches FrameworkElement.FocusVisualStyle): retained,
    /// no focus rectangle is painted in this engine. Converted theme styles
    /// (OxyPlot PlotViewBase) set it to {x:Null}.
    /// </summary>
    public Style? FocusVisualStyle
    {
        get => (Style?)GetValue(FocusVisualStyleProperty);
        set => SetValue(FocusVisualStyleProperty, value);
    }

    /// <summary>Template hook (matches FrameworkElement.OnApplyTemplate).</summary>
    protected virtual void OnApplyTemplate()
    {
    }

    /// <summary>
    /// Builds the visual tree for the current template (matches
    /// FrameworkElement.ApplyTemplate; base has no template and returns
    /// false, controls override to inflate theirs). Converted chrome
    /// workers call this through the base type.
    /// </summary>
    public virtual bool ApplyTemplate() => false;

    /// <summary>
    /// Paints a visual child from a derived OnRender (matches the
    /// DrawingVisual/Visual render path converted ScreenToGif controls use
    /// for their template and selection visuals).
    /// </summary>
    protected void RenderChild(DrawingContext dc, UIElement child) => RenderVisible(dc, child);

    /// <summary>
    /// Named template part (matches FrameworkElement.GetTemplateChild):
    /// converted ScreenToGif StatusBand/ExWindow resolve their Chrome and
    /// warning-grid parts here; null without an applied template.
    /// </summary>
    protected internal DependencyObject? GetTemplateChild(string childName)
    {
        if (this is Control control) return control.GetTemplateChildCore(childName);
        return null;
    }

    /// <summary>
    /// Brush/font sync hooks: Control's real-typed properties invoke these so
    /// Rgba-rendered controls repaint without re-implementing the property.
    /// </summary>
    protected virtual void OnBackgroundChanged(Brush? brush) { }
    protected virtual void OnForegroundChanged(Brush? brush) { }
    protected virtual void OnBorderBrushChanged(Brush? brush) { }
    protected virtual void OnFontChanged() { }

    private object? _dataContext;
    private readonly List<BindingExpression> _bindings = new();
    private readonly List<BindingExpressionBase> _multiBindings = new();

    /// <summary>
    /// Data-context token (matches FrameworkElement.DataContextProperty so
    /// converted member references bind; the value itself keeps the CLR
    /// inherited store, which the DP metadata below does not duplicate).
    /// </summary>
    public static readonly DependencyProperty DataContextProperty =
        DependencyProperty.Register("DataContext", typeof(object), typeof(FrameworkElement),
            new PropertyMetadata(null));

    /// <summary>
    /// Binding source object. Null inherits through the <see cref="UIElement.Parent"/>
    /// chain (WPF DataContext inheritance); setting re-evaluates every binding
    /// on this subtree.
    /// </summary>
    /// <summary>
    /// Whether a local DataContext was assigned (by XAML or code), as
    /// opposed to an inherited one. Converted-app boot copies the parsed
    /// root's DataContext onto the component only in this case, so a
    /// codebehind value assigned before InitializeComponent (gallery
    /// MainWindow sets DataContext=this first) survives XAML that declares
    /// none, while XAML-declared view-models still win like real WPF.
    /// </summary>
    internal bool HasLocalDataContext => _dataContext is not null;

    public object? DataContext
    {
        get => _dataContext ?? (Parent as FrameworkElement)?.DataContext;
        set
        {
            if (ReferenceEquals(_dataContext, value)) return;
            var old = DataContext;
            _dataContext = value;
            RefreshBindingsTree();
            DataContextChanged?.Invoke(this, new DependencyPropertyChangedEventArgs(DataContextProperty, old, value));
        }
    }

    /// <summary>
    /// Data-context handoff (matches FrameworkElement.DataContextChanged;
    /// converted ScreenToGif AdornedControl subscribes to push the context
    /// into its adorner content).
    /// </summary>
    public event EventHandler<DependencyPropertyChangedEventArgs>? DataContextChanged;

    /// <summary>
    /// Links a CLR property one-way to <paramref name="binding"/> (initial push
    /// included; INotifyPropertyChanged sources push on change unless OneTime).
    /// </summary>
    /// <summary>Re-evaluates CLR bindings (context menus refresh on placement, like WPF).</summary>
    internal void RefreshBindings()
    {
        foreach (var expression in _bindings)
            expression.UpdateTarget();
    }

    public BindingExpression SetBinding(string propertyName, Binding binding)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(binding);
        var expression = new BindingExpression(this, propertyName, binding);
        _bindings.Add(expression);
        return expression;
    }

    /// <summary>
    /// DP-typed binding entry point (matches
    /// FrameworkElement.SetBinding(DependencyProperty, BindingBase)):
    /// converted AvalonDock layout code binds through this pervasively
    /// (pane/model/view wiring); forwards to the BindingOperations transfer
    /// over the DP-named CLR property, like the engine's other DP paths.
    /// </summary>
    public BindingExpressionBase? SetBinding(DependencyProperty dp, BindingBase binding) =>
        BindingOperations.SetBinding(this, dp, binding);

    /// <summary>
    /// Binds an attached value to a source path (used by XAML bindings on
    /// AutomationProperties and other attached targets; replaces by name).
    /// </summary>
    internal BindingExpression SetAttachedBinding(string attachedName, Binding binding)
    {
        ArgumentException.ThrowIfNullOrEmpty(attachedName);
        ArgumentNullException.ThrowIfNull(binding);
        for (var i = _bindings.Count - 1; i >= 0; i--)
        {
            if (_bindings[i].TargetProperty == attachedName)
            {
                _bindings[i].Detach();
                _bindings.RemoveAt(i);
            }
        }
        var expression = new BindingExpression(this, attachedName, binding, attached: true);
        _bindings.Add(expression);
        return expression;
    }

    /// <summary>
    /// Binds an attached value to a PriorityBinding (ordered fallback like
    /// the CLR composite path; replaces any transfer on the attached name).
    /// </summary>
    internal PriorityBindingExpression SetAttachedBinding(string attachedName, PriorityBinding priority)
    {
        ArgumentException.ThrowIfNullOrEmpty(attachedName);
        ArgumentNullException.ThrowIfNull(priority);
        ClearBindingsFor(attachedName);
        return new PriorityBindingExpression(this, attachedName, priority, attached: true);
    }

    /// <summary>
    /// Template parent for RelativeSource TemplatedParent/TemplateBinding
    /// resolution (set while a template inflates this element's subtree).
    /// </summary>
    internal FrameworkElement? TemplateParent { get; set; }

    internal void RemoveBinding(BindingExpression expression) => _bindings.Remove(expression);

    internal void AddBindingExpression(BindingExpression expression) => _bindings.Add(expression);

    internal IReadOnlyList<BindingExpression> ActiveBindings => _bindings;

    internal void AddMultiBindingExpression(BindingExpressionBase expression) => _multiBindings.Add(expression);

    internal void RemoveMultiBinding(BindingExpressionBase expression) => _multiBindings.Remove(expression);

    /// <summary>
    /// Transfer on the DP-named CLR property (matches
    /// FrameworkElement.GetBindingExpression; TimeBox reads Text this way
    /// before UpdateSource). Null when the property carries no transfer
    /// (composite MultiBinding transfers surface through
    /// <see cref="GetBindingExpressionBase"/> instead).
    /// </summary>
    public BindingExpression? GetBindingExpression(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        return _bindings.FirstOrDefault(b => b.TargetProperty == dp.Name);
    }

    /// <summary>Base-typed transfer lookup (matches GetBindingExpressionBase).</summary>
    public BindingExpressionBase? GetBindingExpressionBase(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        return (BindingExpressionBase?)GetBindingExpression(dp)
            ?? _multiBindings.FirstOrDefault(b => b.TargetProperty == dp.Name);
    }

    /// <summary>Detaches transfers on one named property (replace path for SetBinding).</summary>
    internal void ClearBindingsFor(string propertyName)
    {
        foreach (var expression in _bindings.Where(b => b.TargetProperty == propertyName).ToArray())
            expression.Detach();
        foreach (var composite in _multiBindings.Where(b => b.TargetProperty == propertyName).ToArray())
            composite.Detach();
    }

    /// <summary>Detaches every transfer on this element.</summary>
    internal void ClearAllBindings()
    {
        foreach (var expression in _bindings.ToArray())
            expression.Detach();
        foreach (var composite in _multiBindings.ToArray())
            composite.Detach();
    }

    /// <summary>
    /// Binds a property to a resource key (matches
    /// FrameworkElement.SetResourceReference): resolves now when present
    /// (missing keys stay unset, like WPF) and refreshes on resource or
    /// theme changes. A CLR property with the dependency-property name wins;
    /// otherwise the value lands in the dependency-property store.
    /// </summary>
    public void SetResourceReference(DependencyProperty dp, object key)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ArgumentNullException.ThrowIfNull(key);
        var marker = "{DynamicResource " + key + "}";
        var prop = GetType().GetProperty(dp.Name,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (prop is not null && prop.CanWrite && prop.SetMethod?.IsPublic == true)
        {
            if (TryFindResource(key, out var current) && current is object currentValue)
                prop.SetValue(this, Setter.CoerceResourceValue(currentValue, prop.PropertyType, marker));
            Application.DynamicResourceManager.SubscribeProperty(this, dp, key,
                (target, value) => prop.SetValue(target, Setter.CoerceResourceValue(value, prop.PropertyType, marker)));
            return;
        }
        if (TryFindResource(key, out var dpCurrent) && dpCurrent is object dpValue)
            SetValue(dp, Setter.CoerceResourceValue(dpValue, dp.PropertyType, marker));
        Application.DynamicResourceManager.SubscribeProperty(this, dp, key,
            (target, value) => target.SetValue(dp, Setter.CoerceResourceValue(value, dp.PropertyType, marker)));
    }

    /// <summary>
    /// Scrolls the nearest enclosing ScrollViewer just enough to show this
    /// element (top-align when above, bottom-align when below; no-op without
    /// an ancestor scroller, like WPF outside a scrollable parent).
    /// </summary>
    /// <summary>
    /// Shows a sub-rectangle (matches UIElement.BringIntoView(Rect)): raises
    /// the RequestBringIntoView route with the rectangle, then scrolls
    /// directly unless a handler marked it handled.
    /// </summary>
    public void BringIntoView(Rect targetRect)
    {
        var args = new RequestBringIntoViewEventArgs(this, targetRect);
        RaiseEvent(args);
        OnRequestBringIntoView(args);
        if (args.Handled) return;
        BringIntoViewCore();
    }

    public void BringIntoView()
    {
        var args = new RequestBringIntoViewEventArgs(this);
        RaiseEvent(args);
        OnRequestBringIntoView(args);
        if (args.Handled) return;
        BringIntoViewCore();
    }

    private void BringIntoViewCore()
    {
        var height = RenderRect.Height;
        var y = 0.0;
        for (var node = (FrameworkElement?)this; node is not null; node = node.Parent as FrameworkElement)
        {
            if (node is ScrollViewer scroller)
            {
                var top = y;
                var bottom = y + height;
                if (top < scroller.VerticalOffset) scroller.VerticalOffset = top;
                else if (bottom > scroller.VerticalOffset + scroller.ActualHeight)
                    scroller.VerticalOffset = bottom - scroller.ActualHeight;
                return;
            }
            y += node.RenderRect.Y;
        }
    }

    /// <summary>
    /// Runs a synchronous measure/arrange pass from the root with its last
    /// known size (matches UIElement.UpdateLayout; converted navigators call
    /// this after expanding items so containers exist immediately).
    /// </summary>
    public void UpdateLayout()
    {
        var root = (UIElement)this;
        while (root.Parent is UIElement parent) root = parent;
        var size = root.RenderRect;
        var width = size.Width > 0 ? size.Width : 800;
        var height = size.Height > 0 ? size.Height : 600;
        root.Measure(new WpfSize(width, height));
        root.Arrange(new WpfRect(0, 0, width, height));
    }

    /// <summary>
    /// Pushes edited CLR properties back into their sources, honoring each
    /// binding's <see cref="UpdateSourceTrigger"/> (Explicit/LostFocus wait
    /// for an explicit <see cref="BindingExpression.UpdateSource"/> call).
    /// </summary>
    internal void UpdateSources()
    {
        foreach (var expression in _bindings.ToArray()) expression.PushSource(force: false);
    }

    private bool _inRefreshBindings;

    internal void RefreshBindingsTree()
    {
        if (_inRefreshBindings) return;
        _inRefreshBindings = true;
        try
        {
            foreach (var expression in _bindings.ToArray()) expression.Rebind();
            // Snapshot: rebinds (ancestor-resolved content bindings) attach
            // visuals mid-walk; the added subtree is refreshed by AddChild
            // itself, so the snapshot loses nothing.
            foreach (var child in Children.ToArray())
                if (child is FrameworkElement fe) fe.RefreshBindingsTree();
        }
        finally
        {
            _inRefreshBindings = false;
        }
    }

    /// <summary>
    /// Element-scope resources (empty until XAML/code fills it). Lookup order
    /// for <see cref="TryFindResource"/>: this dictionary (local, then merged),
    /// then ancestors, then <see cref="Application.Current"/> resources.
    /// </summary>
    public ResourceDictionary Resources { get; private set; } = new();

    /// <summary>Replaces the resource dictionary (XAML Resources assignment).</summary>
    internal void SetResources(ResourceDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        Resources = dictionary;
    }

    /// <summary>
    /// Resolves a resource key through element, ancestor and application
    /// scopes (false when absent anywhere). The application step carries
    /// the framework theme fallback (see Application.TryFindResource).
    /// </summary>
    public bool TryFindResource(object key, out object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        for (var node = (FrameworkElement?)this; node is not null; node = (node.Parent ?? (node as Visual)?.VisualParent) as FrameworkElement)
            if (node.Resources.TryGetValue(key, out value)) return true;
        var app = Application.Current;
        if (app is not null && app.TryFindResource(key, out value)) return true;
        value = null;
        return false;
    }

    /// <summary>
    /// Resolves a resource key or returns null (matches
    /// FrameworkElement.TryFindResource; converted ToggleDockingManager
    /// style resolution binds here and falls back when absent).
    /// </summary>
    public object? TryFindResource(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return TryFindResource(key, out var value) ? value : null;
    }

    /// <summary>
    /// Resolves a resource key or throws (converted FindResource callers,
    /// e.g. Geometry's storyboard lookup, bind here).
    /// </summary>
    public object FindResource(object key)
    {
        if (TryFindResource(key, out var value) && value is not null) return value;
        throw new InvalidOperationException($"Resource '{key}' was not found in element, ancestor or application scopes.");
    }

    /// <summary>Layout-computed width from the last arrange (matches FrameworkElement.ActualWidth).</summary>
    public double ActualWidth => RenderRect.Width;

    /// <summary>Layout identity (matches FrameworkElement.ActualWidthProperty;
    /// read-only like WPF; converted RectangleAttach.OnCircularChanged binds
    /// through its Name, and the store mirrors RenderRect on every size
    /// change so GetValue agrees with the CLR read).</summary>
    internal static readonly DependencyPropertyKey ActualWidthPropertyKey =
        DependencyProperty.RegisterReadOnly("ActualWidth", typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(0.0));

    /// <summary>Layout identity (matches FrameworkElement.ActualWidthProperty).</summary>
    public static readonly DependencyProperty ActualWidthProperty = ActualWidthPropertyKey.DependencyProperty;

    /// <summary>Layout-computed height from the last arrange (matches FrameworkElement.ActualHeight).</summary>
    public double ActualHeight => RenderRect.Height;

    /// <summary>Layout identity (matches FrameworkElement.ActualHeightProperty;
    /// read-only like WPF; mirrors RenderRect on every size change).</summary>
    internal static readonly DependencyPropertyKey ActualHeightPropertyKey =
        DependencyProperty.RegisterReadOnly("ActualHeight", typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(0.0));

    /// <summary>Layout identity (matches FrameworkElement.ActualHeightProperty).</summary>
    public static readonly DependencyProperty ActualHeightProperty = ActualHeightPropertyKey.DependencyProperty;

    public static readonly RoutedEvent SizeChangedEvent = EventManager.RegisterRoutedEvent(
        "SizeChanged", RoutingStrategy.Bubble, typeof(SizeChangedEventHandler), typeof(FrameworkElement));

    /// <summary>Bring-into-view token (matches FrameworkElement.RequestBringIntoViewEvent; Bubble like WPF).</summary>
    public static readonly RoutedEvent RequestBringIntoViewEvent = EventManager.RegisterRoutedEvent(
        "RequestBringIntoView", RoutingStrategy.Bubble, typeof(RequestBringIntoViewEventHandler), typeof(FrameworkElement));

    /// <summary>Bring-into-view route (matches UIElement.RequestBringIntoView).</summary>
    public event RequestBringIntoViewEventHandler? RequestBringIntoView;

    /// <summary>
    /// Bring-into-view dispatch (matches UIElement.OnRequestBringIntoView):
    /// invokes subscribers; converted stack panels override this to pad.
    /// </summary>
    protected virtual void OnRequestBringIntoView(RequestBringIntoViewEventArgs e) =>
        RequestBringIntoView?.Invoke(this, e);

    /// <summary>
    /// Raised after an arrange pass actually changes the size (matches
    /// FrameworkElement.SizeChanged; same-size re-arranges stay silent).
    /// </summary>
    public event SizeChangedEventHandler? SizeChanged;

    /// <summary>Raises <see cref="SizeChanged"/> (matches FrameworkElement.OnSizeChanged).</summary>
    protected virtual void OnSizeChanged(SizeChangedEventArgs e) => SizeChanged?.Invoke(this, e);

    protected override void OnRenderSizeChanged(WpfSize oldSize, WpfSize newSize)
    {
        // Layout-identity mirror (matches the read-only ActualWidth/
        // ActualHeight DPs real WPF updates from layout): the store tracks
        // RenderRect so binding and GetValue reads agree with the CLR.
        SetValue(ActualWidthPropertyKey, newSize.Width);
        SetValue(ActualHeightPropertyKey, newSize.Height);
        var args = new SizeChangedEventArgs(
            new Size(oldSize.Width, oldSize.Height),
            new Size(newSize.Width, newSize.Height))
        {
            RoutedEvent = SizeChangedEvent,
            Source = this,
        };
        OnSizeChanged(args);
    }

    /// <summary>Maximum-width token (matches FrameworkElement.MaxWidthProperty;
    /// store-backed so style setters land visibly; default infinite like WPF,
    /// whose metadata is plain PropertyMetadata - the CLR keeps the engine
    /// InvalidateMeasure like before).</summary>
    public static readonly DependencyProperty MaxWidthProperty =
        DependencyProperty.Register("MaxWidth", typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(double.PositiveInfinity));

    /// <summary>Maximum measured width (default infinite; clamps the available width).</summary>
    public double MaxWidth
    {
        get => (double?)GetValue(MaxWidthProperty) ?? double.PositiveInfinity;
        set
        {
            if (MaxWidth == value) return;
            SetValue(MaxWidthProperty, value);
            InvalidateMeasure();
        }
    }

    /// <summary>Minimum-width token (matches FrameworkElement.MinWidthProperty;
    /// store-backed so template bindings land visibly, AffectsMeasure like WPF).</summary>
    public static readonly DependencyProperty MinWidthProperty =
        DependencyProperty.Register("MinWidth", typeof(double), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(0.0) { AffectsMeasure = true });

    /// <summary>Minimum measured width (default zero).</summary>
    public double MinWidth
    {
        get => (double?)GetValue(MinWidthProperty) ?? 0.0;
        set => SetValue(MinWidthProperty, value);
    }

    /// <summary>Minimum-height token (matches FrameworkElement.MinHeightProperty;
    /// store-backed so template bindings land visibly, AffectsMeasure like WPF).</summary>
    public static readonly DependencyProperty MinHeightProperty =
        DependencyProperty.Register("MinHeight", typeof(double), typeof(FrameworkElement),
            new FrameworkPropertyMetadata(0.0) { AffectsMeasure = true });

    /// <summary>Minimum measured height (default zero).</summary>
    public double MinHeight
    {
        get => (double?)GetValue(MinHeightProperty) ?? 0.0;
        set => SetValue(MinHeightProperty, value);
    }

    private double _maxHeight = double.PositiveInfinity;

    /// <summary>Maximum measured height (default infinite; converted windows/panes set this).</summary>
    public double MaxHeight
    {
        get => _maxHeight;
        set
        {
            if (_maxHeight == value) return;
            _maxHeight = value;
            InvalidateMeasure();
        }
    }


    /// <summary>Element triggers (parsed; event triggers attach on load).</summary>
    public TriggerCollection Triggers { get; } = new();

    protected override WpfSize ClampAvailable(WpfSize available) => new(
        Math.Min(double.IsNaN(Width) ? available.Width : Math.Min(Math.Max(0, available.Width), Width), MaxWidth),
        Math.Min(double.IsNaN(Height) ? available.Height : Math.Min(Math.Max(0, available.Height), Height), MaxHeight));

    protected override WpfSize ClampDesired(WpfSize desired) => new(
        double.IsNaN(Width) ? desired.Width : Width,
        double.IsNaN(Height) ? desired.Height : Height);

    /// <summary>
    /// Measure hook (matches FrameworkElement.MeasureOverride): converted
    /// ScreenToGif adorners override this Size-typed hook with a plain
    /// protected override, like upstream; the engine Adorner routes its
    /// core through it. Base returns zero (the engine controls keep
    /// overriding MeasureCore directly).
    /// </summary>
    protected virtual Size MeasureOverride(Size constraint) => new(0, 0);

    /// <summary>
    /// Arrange hook (matches FrameworkElement.ArrangeOverride): converted
    /// adorners position their child here; base keeps the arranged size.
    /// </summary>
    protected virtual Size ArrangeOverride(Size finalSize) => finalSize;

    protected readonly List<UIElement> Children = new();
    public IReadOnlyList<UIElement> LogicalChildren => Children;

    /// <summary>
    /// Appends a child, maintaining the <see cref="UIElement.Parent"/> link
    /// (reparenting detaches from the previous parent) and invalidating layout
    /// up to the visual root.
    /// </summary>
    public void AddChild(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        ThrowIfAncestor(child);
        if (!ReferenceEquals(child.Parent, this))
        {
            DetachFromParent(child);
            Children.Add(child);
            child.Parent = this;
        }
        else
        {
            Children.Add(child); // preserve legacy duplicate-add behavior
        }

        // New parent, possibly new inherited DataContext: re-resolve bindings.
        if (child is FrameworkElement fe) fe.RefreshBindingsTree();
        InvalidateMeasure();
    }

    /// <summary>
    /// Visual-only attach for template-inflated content (matches AddChild
    /// parenting and binding refresh without item-collection routing).
    /// </summary>
    internal void AttachVisualChild(UIElement child) => AddChild(child);

    /// <summary>Visual-only detach (matches RemoveChild parenting semantics).</summary>
    internal bool DetachVisualChild(UIElement child) => RemoveChild(child);

    /// <summary>
    /// Removes a child, clearing its parent link and invalidating layout.
    /// A removed loaded subtree raises <see cref="Unloaded"/> and clears
    /// <see cref="IsLoaded"/>, like real WPF detach.
    /// </summary>
    public bool RemoveChild(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        var removed = Children.Remove(child);
        if (removed)
        {
            if (ReferenceEquals(child.Parent, this)) child.Parent = null;
            if (child is FrameworkElement fe) fe.RaiseUnloadedTree();
            InvalidateMeasure();
        }

        return removed;
    }

    /// <summary>Live child list (backs Panel.Children so converted code sees loader children too).</summary>
    internal List<UIElement> ChildList => Children;

    /// <summary>
    /// Inserts a child at an index, maintaining the <see cref="UIElement.Parent"/>
    /// link (reparenting detaches from the previous parent) and invalidating
    /// layout up to the visual root (positional AddChild for collection views).
    /// </summary>
    internal void InsertChildAt(int index, UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        ThrowIfAncestor(child);
        if (!ReferenceEquals(child.Parent, this))
        {
            DetachFromParent(child);
            Children.Insert(index, child);
            child.Parent = this;
        }
        else
        {
            Children.Remove(child);
            Children.Insert(Math.Min(index, Children.Count), child);
        }

        if (child is FrameworkElement fe) fe.RefreshBindingsTree();
        InvalidateMeasure();
    }

    private static void DetachFromParent(UIElement child)
    {
        if (child.Parent is FrameworkElement oldParent) oldParent.RemoveChild(child);
    }

    private void ThrowIfAncestor(UIElement child)
    {
        if (ReferenceEquals(child, this))
            throw new InvalidOperationException("An element cannot be its own child.");
        for (var p = (UIElement?)Parent; p is not null; p = p.Parent)
            if (ReferenceEquals(p, child))
                throw new InvalidOperationException("Adding an ancestor as a child would create a cycle.");
    }

    /// <summary>
    /// Shaped text measurement for layout. Never throws: an unloadable font
    /// falls back to the legacy per-character heuristic.
    /// </summary>
    protected static (double Width, double Height) TryMeasure(string text, string fontCss, double fallbackW, double fallbackH)
    {
        try
        {
            var (w, h) = TextShaper.Measure(text, fontCss);
            return (w, h);
        }
        catch (Exception)
        {
            return (fallbackW, fallbackH);
        }
    }
}

/// <summary>Border: single child with uniform thickness + background fill.</summary>
/// <summary>Single-child chrome host (matches System.Windows.Controls.Border:
/// live WPF declares it public non-sealed over Decorator, so converted
/// Border children such as HandyControl RunningBorder load; the Child
/// property stays list-backed so AddChild/Child/list paths stay in sync).</summary>
public class Border : Decorator
{
    /// <summary>Hosted child (list-backed: AddChild and the child list land here).</summary>
    public override UIElement? Child
    {
        get => ChildList.Count > 0 ? ChildList[0] : null;
        set
        {
            var current = Child;
            if (ReferenceEquals(current, value)) return;
            foreach (var existing in ChildList.ToArray()) RemoveChild(existing);
            if (value is not null) AddChild(value);
        }
    }

    /// <summary>
    /// Inner padding identity (matches System.Windows.Controls.Border.PaddingProperty:
    /// live-verified as Border's own registration, not the shared Control
    /// instance, of type Thickness defaulting to 0,0,0,0; the CLR wrapper
    /// stays store-backed so template bindings and setters bind and applied
    /// values measure).
    /// </summary>
    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register("Padding", typeof(Thickness), typeof(Border),
            new FrameworkPropertyMetadata(new Thickness(0)) { AffectsMeasure = true });

    /// <summary>Inner padding (changes desired size).</summary>
    public Thickness Padding
    {
        get => (Thickness)(GetValue(PaddingProperty) ?? new Thickness(0));
        set { SetValue(PaddingProperty, value); InvalidateMeasure(); }
    }
    private uint? _borderRgba;
    public uint? BorderRgba { get => _borderRgba; set { if (_borderRgba == value) return; _borderRgba = value; InvalidateVisual(); } }
    private uint _backgroundRgba = 0xFFFFFFFF;

    /// <summary>Fill color (paint-only change).</summary>
    public uint BackgroundRgba
    {
        get => _backgroundRgba;
        set
        {
            if (_backgroundRgba == value) return;
            _backgroundRgba = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Fill identity (matches System.Windows.Controls.Border.BackgroundProperty;
    /// the WPF-identical shared Control instance, so converted code-built
    /// templates and setters bind; the CLR wrapper stays store-backed so
    /// factory-applied values paint).
    /// </summary>
    public static readonly DependencyProperty BackgroundProperty =
        Control.BackgroundProperty.AddOwner(typeof(Border));

    /// <summary>Fill brush (solid brushes sync the color; others paint their resolved fallback).</summary>
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set
        {
            SetValue(BackgroundProperty, value);
            OnBackgroundChanged(value);
            InvalidateVisual();
        }
    }

    private double _thickness;

    /// <summary>Uniform border thickness (changes desired size).</summary>
    public double Thickness
    {
        get => _thickness;
        set
        {
            if (_thickness == value) return;
            _thickness = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Border thickness identity (matches
    /// System.Windows.Controls.Border.BorderThicknessProperty; the
    /// WPF-identical shared Control instance, so qualified style setters
    /// and template bindings bind. The CLR wrapper stays store-backed with
    /// the uniform-Left render bridge, like Background above).
    /// </summary>
    public static readonly DependencyProperty BorderThicknessProperty =
        Control.BorderThicknessProperty.AddOwner(typeof(Border));

    /// <summary>
    /// Border thickness (matches Border.BorderThickness; the bridge renders
    /// uniformly from Left).
    /// </summary>
    public Thickness BorderThickness
    {
        get => (Thickness)(GetValue(BorderThicknessProperty) ?? new Thickness(0));
        set { SetValue(BorderThicknessProperty, value); Thickness = value.Left; InvalidateMeasure(); }
    }

    /// <summary>Corner radii (matches System.Windows.Controls.Border.CornerRadiusProperty).</summary>
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register("CornerRadius", typeof(CornerRadius), typeof(Border),
            new FrameworkPropertyMetadata(new CornerRadius(0)) { AffectsRender = true });

    /// <summary>Corner radii (DP-backed so qualified style setters and template bindings share it).</summary>
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)(GetValue(CornerRadiusProperty) ?? new CornerRadius(0));
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Edge brush (matches System.Windows.Controls.Border.BorderBrushProperty).</summary>
    public static readonly DependencyProperty BorderBrushProperty =
        DependencyProperty.Register("BorderBrush", typeof(Brush), typeof(Border),
            new FrameworkPropertyMetadata(null, (d, e) => ((Border)d).OnBorderBrushChanged((Brush?)e.NewValue)) { AffectsRender = true });

    private Brush? _borderBrush;

    /// <summary>Edge brush (null keeps the default; solid brushes sync BorderRgba and repaint).</summary>
    public Brush? BorderBrush
    {
        get => _borderBrush;
        set
        {
            if (ReferenceEquals(_borderBrush, value)) return;
            _borderBrush = value;
            OnBorderBrushChanged(value);
            InvalidateVisual();
        }
    }

    protected override void OnBackgroundChanged(Brush? brush)
    {
        if (brush is SolidColorBrush solid) BackgroundRgba = solid.ToRgba();
    }

    protected override void OnBorderBrushChanged(Brush? brush)
    {
        BorderRgba = brush is SolidColorBrush solid ? solid.ToRgba() : null;
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var insetW = Thickness * 2 + Padding.Left + Padding.Right;
        var insetH = Thickness * 2 + Padding.Top + Padding.Bottom;
        if (Children.Count == 0) return new WpfSize(insetW, insetH);
        var margin = Children[0] is FrameworkElement element ? element.Margin : new Thickness(0);
        insetW += margin.Left + margin.Right; insetH += margin.Top + margin.Bottom;
        Children[0].Measure(new WpfSize(Math.Max(0, available.Width - insetW), Math.Max(0, available.Height - insetH)));
        var s = Children[0].DesiredSize;
        return new WpfSize(s.Width + insetW, s.Height + insetH);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (Children.Count > 0)
        {
            var margin = Children[0] is FrameworkElement element ? element.Margin : new Thickness(0);
            Children[0].Arrange(new WpfRect(finalRect.X + Thickness + Padding.Left + margin.Left, finalRect.Y + Thickness + Padding.Top + margin.Top,
                Math.Max(0, finalRect.Width - Thickness * 2 - Padding.Left - Padding.Right - margin.Left - margin.Right),
                Math.Max(0, finalRect.Height - Thickness * 2 - Padding.Top - Padding.Bottom - margin.Top - margin.Bottom)));
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        var background = Background;
        var bg = background is null ? BackgroundRgba : BrushRender.ResolveFill(background, BackgroundRgba);
        var radius = (float)CornerRadius.TopLeft;
        if (radius > 0)
            dc.DrawRoundedRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, radius, bg);
        else
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, bg);
        if (BorderRgba is { } border && Thickness > 0)
        {
            var t = Math.Min(Thickness, Math.Min(r.Width, r.Height) / 2);
            dc.DrawRoundedRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, radius, border);
            dc.DrawRoundedRectangle((float)(r.X + t), (float)(r.Y + t), (float)Math.Max(0, r.Width - 2 * t),
                (float)Math.Max(0, r.Height - 2 * t), (float)Math.Max(0, radius - t), bg);
        }
        if (Children.Count > 0)
        {
            if (ClipToBounds)
            {
                dc.PushClip((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);
                RenderVisible(dc, Children[0]);
                dc.PopClip();
            }
            else RenderVisible(dc, Children[0]);
        }
    }
}

/// <summary>
/// Child host (matches System.Windows.Controls.UIElementCollection): ordered
/// visuals with logical parenting on insert and unparenting on remove, so
/// hosted children join hit testing, focus order and routed bubbling. Used
/// live by InkCanvas.Children and Panel.Children (the latter as a live view
/// over the panel's child list, so converted code sees loader children too).
/// </summary>
public class UIElementCollection : IList<UIElement>
{
    private readonly List<UIElement> _items = new();
    private readonly UIElement _owner;
    private readonly bool _live;

    /// <summary>Hosts children for an owner (parenting target for attach/detach).</summary>
    public UIElementCollection(UIElement owner) => _owner = owner;

    /// <summary>
    /// Live view over the owner's child list (Panel.Children): reads and
    /// writes go straight to the owner instead of a private copy.
    /// </summary>
    internal UIElementCollection(UIElement owner, bool live)
    {
        _owner = owner;
        _live = live;
    }

    /// <summary>Hosts children for a visual/logical pair (parenting target is the visual owner).</summary>
    public UIElementCollection(UIElement visualParent, FrameworkElement logicalParent)
    {
        ArgumentNullException.ThrowIfNull(visualParent);
        _owner = visualParent;
    }

    /// <summary>Logical parent receiving the hosted children.</summary>
    public UIElement Owner => _owner;

    private List<UIElement> Items =>
        _live && _owner is FrameworkElement host ? host.ChildList : _items;

    public int Count => Items.Count;
    public bool IsReadOnly => false;

    public UIElement this[int index]
    {
        get => Items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_live && _owner is FrameworkElement host)
            {
                var current = host.ChildList[index];
                if (ReferenceEquals(current, value)) return;
                host.RemoveChild(current);
                host.InsertChildAt(index, value);
                return;
            }
            var old = _items[index];
            if (ReferenceEquals(old, value)) return;
            Detach(old);
            _items[index] = value;
            Attach(value);
        }
    }

    /// <summary>
    /// Hosts a child and returns its index (matches real WPF, whose
    /// UIElementCollection.Add returns int32: converted OxyPlot grid assembly
    /// binds the int-returning shape, and a void-only engine raises
    /// MissingMethodException at OnApplyTemplate time).
    /// </summary>
    public new virtual int Add(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (_live && _owner is FrameworkElement host)
        {
            host.AddChild(child);
            return host.ChildList.Count - 1;
        }
        _items.Add(child);
        Attach(child);
        return _items.Count - 1;
    }

    void ICollection<UIElement>.Add(UIElement child) => Add(child);

    /// <summary>Unhosts a child (matches real WPF, whose Remove returns void).</summary>
    public new void Remove(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (_live && _owner is FrameworkElement host)
        {
            host.RemoveChild(child);
            return;
        }
        foreach (var item in _items.ToArray())
        {
            if (!ReferenceEquals(item, child)) continue;
            _items.Remove(item);
            Detach(item);
            return;
        }
    }

    bool ICollection<UIElement>.Remove(UIElement child)
    {
        var had = Contains(child);
        Remove(child);
        return had;
    }

    /// <summary>Inserts a hosted child (matches Insert).</summary>
    public void Insert(int index, UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (_live && _owner is FrameworkElement host)
        {
            host.InsertChildAt(index, child);
            return;
        }
        _items.Insert(index, child);
        Attach(child);
    }

    /// <summary>Unhosts at an index (matches RemoveAt).</summary>
    public virtual void RemoveAt(int index)
    {
        if (_live && _owner is FrameworkElement host)
        {
            host.RemoveChild(host.ChildList[index]);
            return;
        }
        var old = _items[index];
        _items.RemoveAt(index);
        Detach(old);
    }

    /// <summary>Unhosts a range (matches RemoveRange; direct removal, like the WPF base).</summary>
    public virtual void RemoveRange(int index, int count)
    {
        if (index < 0 || count < 0 || index + count > Items.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (_live && _owner is FrameworkElement host)
        {
            for (var removed = 0; removed < count; removed++)
                host.RemoveChild(host.ChildList[index]);
            return;
        }
        var detached = _items.GetRange(index, count);
        _items.RemoveRange(index, count);
        foreach (var item in detached) Detach(item);
    }

    /// <summary>Unhosts every child (matches Clear).</summary>
    public virtual void Clear()
    {
        if (_live && _owner is FrameworkElement host)
        {
            foreach (var item in host.ChildList.ToArray()) host.RemoveChild(item);
            return;
        }
        var removed = _items.ToArray();
        _items.Clear();
        foreach (var item in removed) Detach(item);
    }

    public bool Contains(UIElement child) => child is not null && Items.Contains(child);
    public int IndexOf(UIElement child) => child is null ? -1 : Items.IndexOf(child);
    public void CopyTo(UIElement[] array, int arrayIndex) => Items.CopyTo(array, arrayIndex);

    /// <summary>
    /// Non-generic enumerator (matches real WPF UIElementCollection.GetEnumerator,
    /// which returns System.Collections.IEnumerator; C# cannot overload by
    /// return type alone, so the generic enumerator below is explicit).
    /// Converted panel layout loops (foreach over Children/InternalChildren,
    /// as in HandyControl HoneycombPanel.ArrangeOverride) bind this member;
    /// see Slice111HandyPanels. Engine duck-typed foreach sites name the
    /// UIElement element type explicitly; LINQ keeps the generic interface.
    /// Iteration is snapshot-based.
    /// </summary>
    public System.Collections.IEnumerator GetEnumerator() => Items.ToArray().GetEnumerator();

    IEnumerator<UIElement> IEnumerable<UIElement>.GetEnumerator() =>
        Items.ToArray().AsEnumerable().GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    private void Attach(UIElement child)
    {
        if (_owner is FrameworkElement host) host.AddChild(child);
    }

    private void Detach(UIElement child)
    {
        if (_owner is FrameworkElement host) host.RemoveChild(child);
    }
}

/// <summary>Panel base (matches System.Windows.Controls.Panel): children host.</summary>
public abstract class Panel : FrameworkElement
{
    private UIElementCollection? _children;

    /// <summary>
    /// Child collection (matches Panel.Children): a live view over the
    /// panel's child list, so converted code (OxyPlot PlotViewBase grid
    /// assembly) and the loader observe the same children.
    /// </summary>
    public new UIElementCollection Children => _children ??= new UIElementCollection(this, live: true);

    /// <summary>
    /// Fill identity (matches System.Windows.Controls.Panel.BackgroundProperty;
    /// the WPF-identical shared Control instance, so converted code-built
    /// grids and setters bind; the CLR wrapper stays store-backed so
    /// SetValue-applied values paint, like Border).
    /// </summary>
    public static readonly DependencyProperty BackgroundProperty =
        Control.BackgroundProperty.AddOwner(typeof(Panel));

    /// <summary>
    /// Fill brush (matches Panel.Background; null paints nothing, like WPF's
    /// null default). Panels with their own retained brush (Grid, StackPanel,
    /// DockPanel) keep painting it; Canvas paints this one.
    /// </summary>
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set
        {
            if (ReferenceEquals(GetValue(BackgroundProperty), value)) return;
            SetValue(BackgroundProperty, value);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Generated-children view (matches Panel.InternalChildren; same live
    /// collection as Children, like WPF where InternalChildren backs
    /// Children). Converted AvalonDock grid controls walk it for property
    /// change handlers.
    /// </summary>
    protected UIElementCollection InternalChildren => Children;

    /// <summary>
    /// Paint-order slot (matches Panel.ZIndexProperty; attached, default 0
    /// like WPF; Canvas reuses the same instance via AddOwner exactly as
    /// real WPF does. Paint order stays child order until a z-sorted walk
    /// lands; the stored order value is real state for styles, triggers
    /// and converted code, like WPF).
    /// </summary>
    public static readonly DependencyProperty ZIndexProperty =
        DependencyProperty.RegisterAttached("ZIndex", typeof(int), typeof(Panel),
            new FrameworkPropertyMetadata(0));

    /// <summary>Reads the paint-order slot (attached bag plus DP store, 0 when unset like WPF).</summary>
    public static int GetZIndex(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.GetAttached("Panel.ZIndex") is int fromBag) return fromBag;
        if (element.GetAttached("Canvas.ZIndex") is int canvasBag) return canvasBag;
        if (element.HasLocalValue(ZIndexProperty) &&
            element.GetValue(ZIndexProperty) is int fromStore) return fromStore;
        return 0;
    }

    /// <summary>Sets the paint-order slot (DP store plus attached bag, like the Canvas offsets).</summary>
    public static void SetZIndex(UIElement element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetAttached("Panel.ZIndex", value);
        element.SetValue(ZIndexProperty, value);
    }

    /// <summary>Items-host marker (parsed; the owning ItemsControl reads it).</summary>
    public bool IsItemsHost { get; set; }

    /// <summary>
    /// Default child paint (matches the WPF visual-layer walk): panels
    /// without their own chrome render their children in order. Engine
    /// panels with chrome (Canvas background, StackPanel fill, Grid lines)
    /// keep overriding; converted panels without OnRender (HandyControl
    /// UniformSpacingPanel, which has no upstream OnRender either) gain
    /// honest child ink instead of painting nothing.
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        foreach (UIElement c in Children) RenderVisible(dc, c);
    }

    /// <summary>
    /// Converted-panel layout routing (matches the WPF Panel contract and
    /// the engine Adorner precedent): converted HandyControl panels
    /// (UniformSpacingPanel) override the Size-typed MeasureOverride hook,
    /// not the engine core, so the core routes through it here. Engine
    /// panels keep overriding MeasureCore directly, so their paths are
    /// untouched; panels overriding neither still measure zero through the
    /// base hook.
    /// </summary>
    protected override WpfSize MeasureCore(WpfSize available)
    {
        var result = MeasureOverride((Size)available);
        return (WpfSize)result;
    }

    /// <summary>
    /// Converted-panel arrange routing (same contract): the override
    /// positions children in place. The arranged slot keeps standing (the
    /// render rect is owned by the arrange pass); only the positioning side
    /// effect matters here.
    /// </summary>
    protected override void ArrangeCore(WpfRect finalRect) =>
        ArrangeOverride((Size)new WpfSize(finalRect.Width, finalRect.Height));
}

/// <summary>Canvas: absolute positioning (no measuring beyond children union).</summary>
public class Canvas : Panel
{
    /// <summary>
    /// Left offset (matches System.Windows.Controls.Canvas.LeftProperty;
    /// attached, default NaN like WPF; Window reuses the same instance via
    /// AddOwner exactly as real WPF does).
    /// </summary>
    public static readonly DependencyProperty LeftProperty =
        DependencyProperty.RegisterAttached("Left", typeof(double), typeof(Canvas),
            new FrameworkPropertyMetadata(double.NaN));

    /// <summary>
    /// Top offset (matches System.Windows.Controls.Canvas.TopProperty;
    /// attached, default NaN like WPF; Window reuses the same instance via
    /// AddOwner exactly as real WPF does).
    /// </summary>
    public static readonly DependencyProperty TopProperty =
        DependencyProperty.RegisterAttached("Top", typeof(double), typeof(Canvas),
            new FrameworkPropertyMetadata(double.NaN));

    /// <summary>Reads the Canvas.Left offset (attached bag plus DP store, NaN when unset like WPF).</summary>
    public static double GetLeft(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.GetAttached("Canvas.Left") is double fromBag) return fromBag;
        if (element.GetAttached("Canvas.Left") is int bagInt) return bagInt;
        if (element.HasLocalValue(LeftProperty) &&
            element.GetValue(LeftProperty) is double fromStore) return fromStore;
        return double.NaN;
    }

    /// <summary>Sets the Canvas.Left attached offset (DP store plus attached bag, like the ScrollViewer knobs).</summary>
    public static void SetLeft(UIElement element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetAttached("Canvas.Left", value);
        element.SetValue(LeftProperty, value);
    }

    /// <summary>Reads the Canvas.Top offset (attached bag plus DP store, NaN when unset like WPF).</summary>
    public static double GetTop(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.GetAttached("Canvas.Top") is double fromBag) return fromBag;
        if (element.GetAttached("Canvas.Top") is int bagInt) return bagInt;
        if (element.HasLocalValue(TopProperty) &&
            element.GetValue(TopProperty) is double fromStore) return fromStore;
        return double.NaN;
    }

    /// <summary>Sets the Canvas.Top attached offset (DP store plus attached bag, like the ScrollViewer knobs).</summary>
    public static void SetTop(UIElement element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetAttached("Canvas.Top", value);
        element.SetValue(TopProperty, value);
    }

    /// <summary>
    /// Paint-order slot (matches Canvas.ZIndexProperty; the WPF-identical
    /// shared Panel instance via AddOwner, so converted code-built panels
    /// and setters bind).
    /// </summary>
    public static readonly DependencyProperty ZIndexProperty =
        Panel.ZIndexProperty.AddOwner(typeof(Canvas));

    /// <summary>Reads the paint-order slot (like Panel.GetZIndex).</summary>
    public static int GetZIndex(UIElement element) => Panel.GetZIndex(element);

    /// <summary>Sets the paint-order slot (like Panel.SetZIndex).</summary>
    public static void SetZIndex(UIElement element, int value) => Panel.SetZIndex(element, value);

    /// <summary>
    /// Right offset (matches System.Windows.Controls.Canvas.RightProperty;
    /// attached, default NaN like WPF: live-probe-verified NaN default on
    /// net10.0-windows).
    /// </summary>
    public static readonly DependencyProperty RightProperty =
        DependencyProperty.RegisterAttached("Right", typeof(double), typeof(Canvas),
            new FrameworkPropertyMetadata(double.NaN));

    /// <summary>Reads the Canvas.Right offset (attached bag plus DP store, NaN when unset like WPF).</summary>
    public static double GetRight(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.GetAttached("Canvas.Right") is double fromBag) return fromBag;
        if (element.GetAttached("Canvas.Right") is int bagInt) return bagInt;
        if (element.HasLocalValue(RightProperty) &&
            element.GetValue(RightProperty) is double fromStore) return fromStore;
        return double.NaN;
    }

    /// <summary>Sets the Canvas.Right attached offset (DP store plus attached bag, like the ScrollViewer knobs).</summary>
    public static void SetRight(UIElement element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetAttached("Canvas.Right", value);
        element.SetValue(RightProperty, value);
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var w = 0.0; var h = 0.0;
        foreach (UIElement c in Children) { c.Measure(available); w = Math.Max(w, c.DesiredSize.Width); h = Math.Max(h, c.DesiredSize.Height); }
        return new WpfSize(w, h);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        foreach (UIElement c in Children)
        {
            var left = OffsetOrZero(GetLeft(c));
            var top = OffsetOrZero(GetTop(c));
            c.Arrange(new WpfRect(finalRect.X + left, finalRect.Y + top, c.DesiredSize.Width, c.DesiredSize.Height));
        }
    }

    private static double OffsetOrZero(double offset) => double.IsNaN(offset) ? 0 : offset;

    protected override void OnRender(DrawingContext dc)
    {
        if (Background is not null)
        {
            var r = RenderRect;
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height,
                BrushRender.ResolveFill(Background, 0x00000000));
        }
        foreach (UIElement c in Children) RenderVisible(dc, c);
    }
}

/// <summary>StackPanel (default <see cref="Orientation.Vertical"/>).</summary>
public class StackPanel : Panel
{
    private Brush? _background;

    /// <summary>Fill brush (solid brushes paint exactly; others paint their resolved fallback).</summary>
    public Brush? Background
    {
        get => _background;
        set
        {
            if (ReferenceEquals(_background, value)) return;
            _background = value;
            InvalidateVisual();
        }
    }

    /// <summary>Stack direction (matches StackPanel.OrientationProperty; Vertical default like WPF).</summary>
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register("Orientation", typeof(Orientation), typeof(StackPanel),
            new FrameworkPropertyMetadata(Orientation.Vertical) { AffectsMeasure = true });

    /// <summary>Stack direction (store-backed; re-layouts on change).</summary>
    public Orientation Orientation
    {
        get => (Orientation?)GetValue(OrientationProperty) ?? Orientation.Vertical;
        set => SetValue(OrientationProperty, value);
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (Orientation == Orientation.Horizontal)
        {
            var w = 0.0; var h = 0.0;
            foreach (UIElement c in Children)
            {
                var m = GetMargin(c);
                c.Measure(new WpfSize(available.Width, Math.Max(0, available.Height - m.Top - m.Bottom)));
                w += c.DesiredSize.Width + m.Left + m.Right;
                h = Math.Max(h, c.DesiredSize.Height + m.Top + m.Bottom);
            }
            return new WpfSize(w, h);
        }
        var vw = 0.0; var vh = 0.0;
        foreach (UIElement c in Children)
        {
            var m = GetMargin(c);
            c.Measure(new WpfSize(Math.Max(0, available.Width - m.Left - m.Right), available.Height));
            vw = Math.Max(vw, c.DesiredSize.Width + m.Left + m.Right);
            vh += c.DesiredSize.Height + m.Top + m.Bottom;
        }
        return new WpfSize(vw, vh);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (Orientation == Orientation.Horizontal)
        {
            var x = finalRect.X;
            foreach (UIElement c in Children)
            {
                var m = GetMargin(c);
                var va = c is FrameworkElement fe ? fe.VerticalAlignment : VerticalAlignment.Top;
                var slotH = Math.Max(0, finalRect.Height - m.Top - m.Bottom);
                var childH = Math.Min(slotH, c.DesiredSize.Height);
                var offset = va switch
                {
                    VerticalAlignment.Center => (slotH - childH) / 2,
                    VerticalAlignment.Bottom => slotH - childH,
                    _ => 0.0,
                };
                var childW = c.DesiredSize.Width;
                c.Arrange(new WpfRect(x + m.Left, finalRect.Y + m.Top + offset, childW, childH));
                x += m.Left + childW + m.Right;
            }
            return;
        }
        var y = finalRect.Y;
        foreach (UIElement c in Children)
        {
            var m = GetMargin(c);
            var ha = c is FrameworkElement fe ? fe.HorizontalAlignment : HorizontalAlignment.Stretch;
            var slotW = Math.Max(0, finalRect.Width - m.Left - m.Right);
            var childW = ha == HorizontalAlignment.Stretch ? slotW : Math.Min(slotW, c.DesiredSize.Width);
            var offset = ha switch
            {
                HorizontalAlignment.Center => (slotW - childW) / 2,
                HorizontalAlignment.Right => slotW - childW,
                _ => 0.0,
            };
            var x = finalRect.X + m.Left + offset;
            var childH = c.DesiredSize.Height;
            c.Arrange(new WpfRect(x, y + m.Top, childW, childH));
            y += m.Top + childH + m.Bottom;
        }
    }

    private static Thickness GetMargin(UIElement c) =>
        c is FrameworkElement fe ? fe.Margin : new Thickness(0);

    protected override void OnRender(DrawingContext dc)
    {
        if (_background is not null)
        {
            var r = RenderRect;
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height,
                BrushRender.ResolveFill(_background, 0x00000000));
        }
        foreach (UIElement c in Children) RenderVisible(dc, c);
    }
}

/// <summary>WrapPanel (default <see cref="Orientation.Horizontal"/>): fills rows (or columns) and wraps.</summary>
public sealed class WrapPanel : Panel
{
    /// <summary>Wrap direction (matches WrapPanel.OrientationProperty; Horizontal default like WPF).</summary>
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register("Orientation", typeof(Orientation), typeof(WrapPanel),
            new FrameworkPropertyMetadata(Orientation.Horizontal) { AffectsMeasure = true });

    /// <summary>Wrap direction (store-backed; re-layouts on change).</summary>
    public Orientation Orientation
    {
        get => (Orientation?)GetValue(OrientationProperty) ?? Orientation.Horizontal;
        set => SetValue(OrientationProperty, value);
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        return Orientation == Orientation.Horizontal
            ? MeasureFlow(Children, available.Width, horizontal: true)
            : MeasureFlow(Children, available.Height, horizontal: false);
    }

    /// <summary>
    /// Wrap-flow measure over an explicit child list (matches the instance
    /// flow; the legacy ListBox routes its generated rows through the same
    /// math when its retained ItemsPanel inflates to a WrapPanel).
    /// </summary>
    internal static WpfSize MeasureFlow(IEnumerable<UIElement> children, double extent, bool horizontal)
    {
        var runMain = 0.0; var runCross = 0.0;
        var main = 0.0; var cross = 0.0;
        foreach (var c in children)
        {
            var m = GetWrapMargin(c);
            c.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
            var childMain = (horizontal ? c.DesiredSize.Width : c.DesiredSize.Height)
                + (horizontal ? m.Left + m.Right : m.Top + m.Bottom);
            var childCross = (horizontal ? c.DesiredSize.Height : c.DesiredSize.Width)
                + (horizontal ? m.Top + m.Bottom : m.Left + m.Right);
            if (runMain > 0 && runMain + childMain > extent && !double.IsPositiveInfinity(extent))
            {
                main = Math.Max(main, runMain);
                cross += runCross;
                runMain = 0; runCross = 0;
            }
            runMain += childMain;
            runCross = Math.Max(runCross, childCross);
        }
        main = Math.Max(main, runMain);
        cross += runCross;
        return horizontal ? new WpfSize(main, cross) : new WpfSize(cross, main);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        ArrangeFlow(Children, finalRect, Orientation);
    }

    /// <summary>
    /// Wrap-flow arrange over an explicit child list (matches the instance
    /// flow; the legacy ListBox routes its generated rows through the same
    /// math when its retained ItemsPanel inflates to a WrapPanel).
    /// </summary>
    internal static void ArrangeFlow(IEnumerable<UIElement> children, WpfRect finalRect, Orientation orientation)
    {
        var extent = orientation == Orientation.Horizontal ? finalRect.Width : finalRect.Height;
        var x = finalRect.X; var y = finalRect.Y;
        var runCross = 0.0;
        foreach (var c in children)
        {
            var m = GetWrapMargin(c);
            var childW = c.DesiredSize.Width;
            var childH = c.DesiredSize.Height;
            var childMain = (orientation == Orientation.Horizontal ? childW : childH)
                + (orientation == Orientation.Horizontal ? m.Left + m.Right : m.Top + m.Bottom);
            var childCross = (orientation == Orientation.Horizontal ? childH : childW)
                + (orientation == Orientation.Horizontal ? m.Top + m.Bottom : m.Left + m.Right);
            var cursorMain = orientation == Orientation.Horizontal ? x - finalRect.X : y - finalRect.Y;
            if (cursorMain > 0 && cursorMain + childMain > extent && !double.IsPositiveInfinity(extent))
            {
                if (orientation == Orientation.Horizontal) { x = finalRect.X; y += runCross; }
                else { y = finalRect.Y; x += runCross; }
                runCross = 0;
            }
            c.Arrange(new WpfRect(x + m.Left, y + m.Top, childW, childH));
            if (orientation == Orientation.Horizontal) x += m.Left + childW + m.Right;
            else y += m.Top + childH + m.Bottom;
            runCross = Math.Max(runCross, childCross);
        }
    }

    private static Thickness GetWrapMargin(UIElement c) =>
        c is FrameworkElement fe ? fe.Margin : new Thickness(0);

    protected override void OnRender(DrawingContext dc)
    {
        foreach (UIElement c in Children) RenderVisible(dc, c);
    }
}

/// <summary>Dock edge for <see cref="DockPanel"/> children (default Left).</summary>
public enum Dock { Left, Top, Right, Bottom }

/// <summary>
/// DockPanel: children dock against the remaining rect in order (Left/Right
/// take vertical slabs, Top/Bottom horizontal slabs); the last child fills
/// what is left when <see cref="LastChildFill"/> (default true), otherwise
/// it docks like the rest. Undocked children default to Left.
/// </summary>
public class DockPanel : Panel
{
    private Brush? _background;

    /// <summary>Fill brush (solid brushes paint exactly; others paint their resolved fallback).</summary>
    public Brush? Background
    {
        get => _background;
        set
        {
            if (ReferenceEquals(_background, value)) return;
            _background = value;
            InvalidateVisual();
        }
    }

    /// <summary>Whether the last child fills the remaining rect (matches DockPanel.LastChildFillProperty; true default like WPF).</summary>
    public static readonly DependencyProperty LastChildFillProperty =
        DependencyProperty.Register("LastChildFill", typeof(bool), typeof(DockPanel),
            new FrameworkPropertyMetadata(true) { AffectsMeasure = true });

    /// <summary>Whether the last child fills the remaining rect (store-backed; re-measures on change).</summary>
    public bool LastChildFill
    {
        get => (bool?)GetValue(LastChildFillProperty) ?? true;
        set => SetValue(LastChildFillProperty, value);
    }

    /// <summary>Reads the DockPanel.Dock attached edge (default Left).</summary>
    public static Dock GetDock(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetAttached("DockPanel.Dock") is Dock d ? d
            : element.GetAttached("DockPanel.Dock") is string s && TryParseDock(s, out var parsed) ? parsed
            : Dock.Left;
    }

    /// <summary>Sets the DockPanel.Dock attached edge.</summary>
    public static void SetDock(UIElement element, Dock dock)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetAttached("DockPanel.Dock", dock);
    }

    /// <summary>Parses a dock edge name (case-insensitive).</summary>
    public static bool TryParseDock(string? value, out Dock dock)
    {
        dock = Dock.Left;
        if (string.IsNullOrWhiteSpace(value)) return false;
        foreach (var name in Enum.GetNames<Dock>())
            if (string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase))
            { dock = Enum.Parse<Dock>(name); return true; }
        return false;
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        // WPF accumulate/max shape: docked slabs shrink the remaining box;
        // cross-axis maxima (and the fill child) fold in via maxima.
        var remW = available.Width; var remH = available.Height;
        var usedW = 0.0; var usedH = 0.0;
        var maxW = 0.0; var maxH = 0.0;
        for (var i = 0; i < Children.Count; i++)
        {
            var c = Children[i];
            var m = GetMargin(c);
            c.Measure(new WpfSize(Math.Max(0, remW - m.Left - m.Right), Math.Max(0, remH - m.Top - m.Bottom)));
            var cw = c.DesiredSize.Width + m.Left + m.Right;
            var ch = c.DesiredSize.Height + m.Top + m.Bottom;
            if (IsFillChild(i))
            {
                maxW = Math.Max(maxW, usedW + cw);
                maxH = Math.Max(maxH, usedH + ch);
                continue;
            }
            if (GetDock(c) is Dock.Left or Dock.Right)
            {
                usedW += cw;
                maxH = Math.Max(maxH, usedH + ch);
                remW = Math.Max(0, remW - cw);
            }
            else
            {
                usedH += ch;
                maxW = Math.Max(maxW, usedW + cw);
                remH = Math.Max(0, remH - ch);
            }
        }
        return new WpfSize(Math.Max(usedW, maxW), Math.Max(usedH, maxH));
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        var x = finalRect.X; var y = finalRect.Y;
        var w = Math.Max(0, finalRect.Width); var h = Math.Max(0, finalRect.Height);
        for (var i = 0; i < Children.Count; i++)
        {
            var c = Children[i];
            var m = GetMargin(c);
            if (IsFillChild(i))
            {
                c.Arrange(new WpfRect(x + m.Left, y + m.Top,
                    Math.Max(0, w - m.Left - m.Right), Math.Max(0, h - m.Top - m.Bottom)));
                return;
            }
            switch (GetDock(c))
            {
                case Dock.Left:
                {
                    var cw = Math.Min(w, c.DesiredSize.Width + m.Left + m.Right);
                    c.Arrange(new WpfRect(x + m.Left, y + m.Top,
                        Math.Max(0, cw - m.Left - m.Right), Math.Max(0, h - m.Top - m.Bottom)));
                    x += cw; w = Math.Max(0, w - cw);
                    break;
                }
                case Dock.Right:
                {
                    var cw = Math.Min(w, c.DesiredSize.Width + m.Left + m.Right);
                    c.Arrange(new WpfRect(x + w - cw + m.Left, y + m.Top,
                        Math.Max(0, cw - m.Left - m.Right), Math.Max(0, h - m.Top - m.Bottom)));
                    w = Math.Max(0, w - cw);
                    break;
                }
                case Dock.Top:
                {
                    var ch = Math.Min(h, c.DesiredSize.Height + m.Top + m.Bottom);
                    c.Arrange(new WpfRect(x + m.Left, y + m.Top,
                        Math.Max(0, w - m.Left - m.Right), Math.Max(0, ch - m.Top - m.Bottom)));
                    y += ch; h = Math.Max(0, h - ch);
                    break;
                }
                default:
                {
                    var ch = Math.Min(h, c.DesiredSize.Height + m.Top + m.Bottom);
                    c.Arrange(new WpfRect(x + m.Left, y + h - ch + m.Top,
                        Math.Max(0, w - m.Left - m.Right), Math.Max(0, ch - m.Top - m.Bottom)));
                    h = Math.Max(0, h - ch);
                    break;
                }
            }
        }
    }

    private bool IsFillChild(int index) =>
        LastChildFill && index == Children.Count - 1;

    private static Thickness GetMargin(UIElement c) =>
        c is FrameworkElement fe ? fe.Margin : new Thickness(0);

    protected override void OnRender(DrawingContext dc)
    {
        if (_background is not null)
        {
            var r = RenderRect;
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height,
                BrushRender.ResolveFill(_background, 0x00000000));
        }
        foreach (UIElement c in Children) RenderVisible(dc, c);
    }
}

/// <summary>GridLength: pixel, star-weighted, or auto (WPF subset).</summary>
[System.ComponentModel.TypeConverter(typeof(GridLengthConverter))]
public readonly record struct GridLength(double Value, GridUnitType Unit)
{
    /// <summary>Pixel length (matches GridLength(double); converted layout engines bind it).</summary>
    public GridLength(double pixels) : this(pixels, GridUnitType.Pixel)
    {
    }

    /// <summary>Unit select (matches GridLength.GridUnitType; the positional Unit twin stays for engine callers).</summary>
    public GridUnitType GridUnitType => Unit;

    /// <summary>Pixel lengths only (matches GridLength.IsAbsolute).</summary>
    public bool IsAbsolute => Unit == GridUnitType.Pixel;

    /// <summary>Auto lengths only (matches GridLength.IsAuto).</summary>
    public bool IsAuto => Unit == GridUnitType.Auto;

    /// <summary>Star-weighted lengths only (matches GridLength.IsStar).</summary>
    public bool IsStar => Unit == GridUnitType.Star;

    public static GridLength Pixel(double value) => new(value, GridUnitType.Pixel);
    public static GridLength Star(double weight = 1) => new(weight, GridUnitType.Star);
    public static GridLength Auto => new(1, GridUnitType.Auto);

    public static GridLength Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var s = value.Trim();
        if (string.Equals(s, "Auto", StringComparison.OrdinalIgnoreCase)) return Auto;
        if (s.EndsWith("*", StringComparison.Ordinal))
        {
            var weight = s.Length == 1 ? 1 : double.Parse(s[..^1], System.Globalization.CultureInfo.InvariantCulture);
            return Star(weight);
        }
        return Pixel(double.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
    }
}

/// <summary>GridLength unit.</summary>
public enum GridUnitType { Pixel, Star, Auto }

/// <summary>
/// GridLength converter (matches System.Windows.GridLengthConverter; behavior
/// verified live against the 9.0.21 runtime): case-insensitive Auto, star
/// weights ("*" and "N*"), pixel lengths with device-independent unit
/// suffixes (px/in/cm/pt), ConvertTo string round-trips, ConvertTo any other
/// type is false. Garbage throws FormatException; Infinity/NaN throw
/// ArgumentException, like the framework. Placed on GridLength through
/// TypeConverterAttribute, as in WPF.
/// </summary>
public class GridLengthConverter : System.ComponentModel.TypeConverter
{
    public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string);

    public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string);

    public override object? ConvertFrom(System.ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is not string text)
            throw new NotSupportedException($"GridLengthConverter converts strings only, not {value?.GetType().FullName ?? "null"}.");
        return Parse(text, culture ?? System.Globalization.CultureInfo.InvariantCulture);
    }

    public override object? ConvertTo(System.ComponentModel.ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is GridLength length)
            return length.Unit switch
            {
                GridUnitType.Auto => "Auto",
                GridUnitType.Star => length.Value == 1
                    ? "*"
                    : length.Value.ToString(culture ?? System.Globalization.CultureInfo.InvariantCulture) + "*",
                _ => length.Value.ToString(culture ?? System.Globalization.CultureInfo.InvariantCulture),
            };
        throw new NotSupportedException($"GridLengthConverter converts GridLength to string only, not to {destinationType.FullName}.");
    }

    internal static GridLength Parse(string text, System.Globalization.CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(text);
        var s = text.Trim();
        if (string.Equals(s, "Auto", StringComparison.OrdinalIgnoreCase))
            return GridLength.Auto;
        if (s.EndsWith("*", StringComparison.Ordinal))
        {
            var weightText = s[..^1].Trim();
            var weight = weightText.Length == 0 ? 1 : double.Parse(weightText, culture);
            RejectNonFinite(weight);
            return GridLength.Star(weight);
        }
        var (numberText, factor) = SplitUnit(s);
        var pixels = double.Parse(numberText, System.Globalization.NumberStyles.Float, culture) * factor;
        RejectNonFinite(pixels);
        return GridLength.Pixel(pixels);
    }

    private static (string Number, double Factor) SplitUnit(string s)
    {
        if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase)) return (s[..^2].Trim(), 1);
        if (s.EndsWith("in", StringComparison.OrdinalIgnoreCase)) return (s[..^2].Trim(), 96);
        if (s.EndsWith("cm", StringComparison.OrdinalIgnoreCase)) return (s[..^2].Trim(), 96 / 2.54);
        if (s.EndsWith("pt", StringComparison.OrdinalIgnoreCase)) return (s[..^2].Trim(), 96.0 / 72);
        return (s, 1);
    }

    private static void RejectNonFinite(double value)
    {
        if (double.IsInfinity(value) || double.IsNaN(value))
            throw new ArgumentException("'value' parameter cannot be Infinity or NaN.", nameof(value));
    }
}

/// <summary>Row definition (matches System.Windows.Controls.RowDefinition): length plus min/max clamps.</summary>
public sealed class RowDefinition
{
    /// <summary>Row length (default star).</summary>
    public GridLength Height { get; set; } = GridLength.Star();

    /// <summary>Minimum row height (default 0).</summary>
    public double MinHeight { get; set; }

    /// <summary>Maximum row height (default infinite).</summary>
    public double MaxHeight { get; set; } = double.PositiveInfinity;

    /// <summary>Shared-size group (matches DefinitionBase.SharedSizeGroup;
    /// null means unsized like WPF; synced by the Grid scope).</summary>
    public string? SharedSizeGroup { get; set; }
}

/// <summary>Column definition (matches System.Windows.Controls.ColumnDefinition): length plus min/max clamps.</summary>
public sealed class ColumnDefinition
{
    /// <summary>Column length (default star).</summary>
    public GridLength Width { get; set; } = GridLength.Star();

    /// <summary>Minimum column width (default 0).</summary>
    public double MinWidth { get; set; }

    /// <summary>Maximum column width (default infinite).</summary>
    public double MaxWidth { get; set; } = double.PositiveInfinity;

    /// <summary>Shared-size group (matches DefinitionBase.SharedSizeGroup;
    /// null means unsized like WPF; synced by the Grid scope).</summary>
    public string? SharedSizeGroup { get; set; }
}

/// <summary>
/// Grid: uniform rows x cols (Rows/Cols, legacy) or explicit
/// RowDefinitions/ColumnDefinitions with pixel/star/auto lengths (real XAML).
/// Spanning children measure against their spanned extent; auto tracks ignore
/// spanning children (documented simplification).
/// </summary>
public class Grid : Panel
{
    private int _rows = 1;

    /// <summary>Legacy uniform row count (changes track layout).</summary>
    public int Rows
    {
        get => _rows;
        set
        {
            if (_rows == value) return;
            _rows = value;
            InvalidateMeasure();
        }
    }

    private int _cols = 1;

    /// <summary>Legacy uniform column count (changes track layout).</summary>
    public int Cols
    {
        get => _cols;
        set
        {
            if (_cols == value) return;
            _cols = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Row definitions (matches System.Windows.Controls.RowDefinitionCollection).</summary>
    public RowDefinitionCollection RowDefinitions { get; } = new();

    /// <summary>Column definitions (matches System.Windows.Controls.ColumnDefinitionCollection).</summary>
    public ColumnDefinitionCollection ColumnDefinitions { get; } = new();

    private int RowCount => RowDefinitions.Count > 0 ? RowDefinitions.Count : Math.Max(1, Rows);
    private int ColCount => ColumnDefinitions.Count > 0 ? ColumnDefinitions.Count : Math.Max(1, Cols);

    private double[]? _colWidths;
    private double[]? _rowHeights;

    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (RowDefinitions.Count == 0 && ColumnDefinitions.Count == 0)
        {
            var cell = new WpfSize(available.Width / ColCount, available.Height / RowCount);
            var widest = 0.0;
            var tallest = 0.0;
            foreach (UIElement c in Children)
            {
                c.Measure(cell);
                widest = Math.Max(widest, c.DesiredSize.Width);
                tallest = Math.Max(tallest, c.DesiredSize.Height);
            }
            // Definition-less grids size to content under infinite
            // constraints (scroll content) instead of propagating infinity
            // into desired size and every downstream rect.
            return new WpfSize(
                double.IsPositiveInfinity(available.Width) ? widest * ColCount : available.Width,
                double.IsPositiveInfinity(available.Height) ? tallest * RowCount : available.Height);
        }

        foreach (UIElement c in Children) c.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        var (rows, cols, rowOf, colOf, rowSpanOf, colSpanOf) = ComputePlacements();
        _colWidths = ResolveLengths(cols, available.Width, c => c.DesiredSize.Width, colOf, colSpanOf,
            i => i < ColumnDefinitions.Count ? ColumnDefinitions[i].Width : GridLength.Auto,
            i => i < ColumnDefinitions.Count ? ColumnDefinitions[i].MinWidth : 0,
            i => i < ColumnDefinitions.Count ? ColumnDefinitions[i].MaxWidth : double.PositiveInfinity);
        _rowHeights = ResolveLengths(rows, available.Height, c => c.DesiredSize.Height, rowOf, rowSpanOf,
            i => i < RowDefinitions.Count ? RowDefinitions[i].Height : GridLength.Auto,
            i => i < RowDefinitions.Count ? RowDefinitions[i].MinHeight : 0,
            i => i < RowDefinitions.Count ? RowDefinitions[i].MaxHeight : double.PositiveInfinity);
        // Constrained second pass (matches Grid measuring Star/Pixel children
        // against their resolved share): unconstrained naturals inflate
        // scrollable children (ScrollViewer, text) to infinity, which then
        // poisons arrange/render with non-finite rects. Auto tracks stay
        // unconstrained in their own dimension.
        for (var k = 0; k < Children.Count; k++)
        {
            var colUnit = colOf[k] < ColumnDefinitions.Count ? ColumnDefinitions[colOf[k]].Width : GridLength.Auto;
            var rowUnit = rowOf[k] < RowDefinitions.Count ? RowDefinitions[rowOf[k]].Height : GridLength.Auto;
            var w = colSpanOf[k] == 1 && colUnit.Unit != GridUnitType.Auto ? _colWidths[colOf[k]]
                : colSpanOf[k] > 1 ? Sum(_colWidths[colOf[k]..(colOf[k] + colSpanOf[k])]) : available.Width;
            var h = rowSpanOf[k] == 1 && rowUnit.Unit != GridUnitType.Auto ? _rowHeights[rowOf[k]]
                : rowSpanOf[k] > 1 ? Sum(_rowHeights[rowOf[k]..(rowOf[k] + rowSpanOf[k])]) : available.Height;
            var mw = colUnit.Unit == GridUnitType.Auto ? double.PositiveInfinity : w;
            var mh = rowUnit.Unit == GridUnitType.Auto ? double.PositiveInfinity : h;
            if (mw != double.PositiveInfinity || mh != double.PositiveInfinity)
                Children[k].Measure(new WpfSize(mw, mh));
        }
        _colWidths = ResolveLengths(cols, available.Width, c => c.DesiredSize.Width, colOf, colSpanOf,
            i => i < ColumnDefinitions.Count ? ColumnDefinitions[i].Width : GridLength.Auto,
            i => i < ColumnDefinitions.Count ? ColumnDefinitions[i].MinWidth : 0,
            i => i < ColumnDefinitions.Count ? ColumnDefinitions[i].MaxWidth : double.PositiveInfinity);
        _rowHeights = ResolveLengths(rows, available.Height, c => c.DesiredSize.Height, rowOf, rowSpanOf,
            i => i < RowDefinitions.Count ? RowDefinitions[i].Height : GridLength.Auto,
            i => i < RowDefinitions.Count ? RowDefinitions[i].MinHeight : 0,
            i => i < RowDefinitions.Count ? RowDefinitions[i].MaxHeight : double.PositiveInfinity);
        return new WpfSize(Sum(_colWidths), Sum(_rowHeights));
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        // No early empty-children return: track spans are stashed even for
        // childless grids so ShowGridLines guides still paint (WPF parity).
        if (RowDefinitions.Count == 0 && ColumnDefinitions.Count == 0)
        {
            var cols = ColCount;
            var cellW = finalRect.Width / cols;
            var cellH = finalRect.Height / RowCount;
            _arrangedColWidths = UniformSpans(cols, cellW);
            _arrangedRowHeights = UniformSpans(RowCount, cellH);
            for (var i = 0; i < Children.Count; i++)
            {
                // Same-cell children overlap (real WPF Grid.Row/Column
                // default to track 0, and out-of-range tracks clamp into
                // range): the by-index fallback only places legacy uniform
                // tables (Rows/Cols with no definitions), and the clamp
                // keeps overflow inside the grid instead of stacking it
                // below the scene (OxyPlot plot hosts at y = 692 / 1384).
                var row = Math.Clamp(ChildRow(Children[i], i / cols), 0, RowCount - 1);
                var col = Math.Clamp(ChildCol(Children[i], i % cols), 0, ColCount - 1);
                var rowSpan = Math.Clamp(ChildRowSpan(Children[i]), 1, RowCount - row);
                var colSpan = Math.Clamp(ChildColSpan(Children[i]), 1, ColCount - col);
                Children[i].Arrange(new WpfRect(
                    finalRect.X + col * cellW, finalRect.Y + row * cellH,
                    cellW * colSpan, cellH * rowSpan));
            }
            return;
        }

        // Arrange always re-resolves against the final slot (implicit tracks
        // fill here even though they measure as content): reusing the
        // measure resolution would freeze star/auto rows at desired size.
        var (_, cols2, rowOf, colOf, rowSpanOf, colSpanOf) = ComputePlacements();
        var colW = ResolveLengths(cols2, finalRect.Width, c => c.DesiredSize.Width, colOf, colSpanOf,
            i => i < ColumnDefinitions.Count ? ColumnDefinitions[i].Width : GridLength.Star(),
            i => i < ColumnDefinitions.Count ? ColumnDefinitions[i].MinWidth : 0,
            i => i < ColumnDefinitions.Count ? ColumnDefinitions[i].MaxWidth : double.PositiveInfinity);
        var rowH = ResolveLengths(RowCount, finalRect.Height, c => c.DesiredSize.Height, rowOf, rowSpanOf,
            i => i < RowDefinitions.Count ? RowDefinitions[i].Height : GridLength.Star(),
            i => i < RowDefinitions.Count ? RowDefinitions[i].MinHeight : 0,
            i => i < RowDefinitions.Count ? RowDefinitions[i].MaxHeight : double.PositiveInfinity);
        _arrangedColWidths = colW;
        _arrangedRowHeights = rowH;
        for (var i = 0; i < Children.Count; i++)
        {
            var row = rowOf[i]; var col = colOf[i];
            Children[i].Arrange(new WpfRect(
                finalRect.X + Sum(colW[..col]), finalRect.Y + Sum(rowH[..row]),
                Sum(colW[col..(col + colSpanOf[i])]), Sum(rowH[row..(row + rowSpanOf[i])])));
        }
    }

    /// <summary>Last measured column span in pixels (GridSplitter drag math).</summary>
    internal double ColumnPixelWidth(int index)
    {
        if (_colWidths is not null && (uint)index < (uint)_colWidths.Length) return Math.Max(0, _colWidths[index]);
        var def = (uint)index < (uint)ColumnDefinitions.Count ? ColumnDefinitions[index] : null;
        return def is not null && def.Width.Unit == GridUnitType.Pixel ? Math.Max(0, def.Width.Value) : 0;
    }

    /// <summary>Last measured row span in pixels (GridSplitter drag math).</summary>
    internal double RowPixelHeight(int index)
    {
        if (_rowHeights is not null && (uint)index < (uint)_rowHeights.Length) return Math.Max(0, _rowHeights[index]);
        var def = (uint)index < (uint)RowDefinitions.Count ? RowDefinitions[index] : null;
        return def is not null && def.Height.Unit == GridUnitType.Pixel ? Math.Max(0, def.Height.Value) : 0;
    }

    /// <summary>Resolved (track, span) per child, clamped into range.</summary>
    private (int rows, int cols, int[] rowOf, int[] colOf, int[] rowSpanOf, int[] colSpanOf) ComputePlacements()
    {
        var rows = RowCount; var cols = ColCount;
        var rowOf = new int[Children.Count];
        var colOf = new int[Children.Count];
        var rowSpanOf = new int[Children.Count];
        var colSpanOf = new int[Children.Count];
        // Uniform tables (Rows/Cols with no definitions) place children by
        // index; definition grids default every unplaced child to track 0
        // (real WPF Grid.Row/Column default), otherwise the second child
        // lands a row down (gallery HeaderTile image over its title).
        var byIndex = RowDefinitions.Count == 0 && ColumnDefinitions.Count == 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            rowOf[i] = Math.Clamp(ChildRow(child, byIndex ? i / cols : 0), 0, rows - 1);
            colOf[i] = Math.Clamp(ChildCol(child, byIndex ? i % cols : 0), 0, cols - 1);
            rowSpanOf[i] = Math.Clamp(ChildRowSpan(child), 1, rows - rowOf[i]);
            colSpanOf[i] = Math.Clamp(ChildColSpan(child), 1, cols - colOf[i]);
        }
        return (rows, cols, rowOf, colOf, rowSpanOf, colSpanOf);
    }

    private double[] ResolveLengths(
        int count, double available,
        Func<UIElement, double> natural, int[] trackOf, int[] spanOf,
        Func<int, GridLength> lengthAt, Func<int, double> minAt, Func<int, double> maxAt)
    {
        var widths = new double[count];
        var starTotal = 0.0;
        var fixedTotal = 0.0;
        for (var i = 0; i < count; i++)
        {
            var def = lengthAt(i);
            if (def.Unit == GridUnitType.Pixel) { widths[i] = Math.Max(0, def.Value); fixedTotal += widths[i]; }
            else if (def.Unit == GridUnitType.Star) starTotal += def.Value;
            else
            {
                // Auto: largest natural size among non-spanning children in this track.
                double best = 0;
                for (var k = 0; k < Children.Count; k++)
                    if (trackOf[k] == i && spanOf[k] == 1)
                        best = Math.Max(best, natural(Children[k]));
                widths[i] = best;
                fixedTotal += best;
            }
        }

        // Star under an infinite constraint sizes to content (matches Grid
        // treating unbounded star tracks as Auto); otherwise one unbounded
        // ancestor inflates every star descendant to infinity.
        var unbounded = double.IsPositiveInfinity(available);
        var starEach = starTotal > 0 && !unbounded ? Math.Max(0, (available - fixedTotal) / starTotal) : 0;
        for (var i = 0; i < count; i++)
        {
            var def = lengthAt(i);
            if (def.Unit == GridUnitType.Star && unbounded)
            {
                double best = 0;
                for (var k = 0; k < Children.Count; k++)
                    if (trackOf[k] == i && spanOf[k] == 1)
                        best = Math.Max(best, natural(Children[k]));
                widths[i] = best;
            }
            else if (def.Unit == GridUnitType.Star) widths[i] = Math.Max(0, starEach * def.Value);
            // Min/max clamps (a zero max below the min resolves to the min).
            widths[i] = Math.Max(minAt(i), Math.Min(maxAt(i), widths[i]));
        }
        return widths;
    }

    private static double Sum(double[] values)
    {
        var total = 0.0;
        foreach (var v in values) total += v;
        return total;
    }

    private static double[] UniformSpans(int count, double each)
    {
        var spans = new double[count];
        for (var i = 0; i < count; i++) spans[i] = each;
        return spans;
    }

    /// <summary>Row token (matches System.Windows.Controls.Grid.RowProperty; attached, default 0 like WPF).</summary>
    public static readonly DependencyProperty RowProperty =
        DependencyProperty.RegisterAttached("Row", typeof(int), typeof(Grid),
            new FrameworkPropertyMetadata(0));

    /// <summary>Column token (matches System.Windows.Controls.Grid.ColumnProperty; attached, default 0 like WPF).</summary>
    public static readonly DependencyProperty ColumnProperty =
        DependencyProperty.RegisterAttached("Column", typeof(int), typeof(Grid),
            new FrameworkPropertyMetadata(0));

    /// <summary>Row-span token (matches System.Windows.Controls.Grid.RowSpanProperty; attached, default 1 like WPF).</summary>
    public static readonly DependencyProperty RowSpanProperty =
        DependencyProperty.RegisterAttached("RowSpan", typeof(int), typeof(Grid),
            new FrameworkPropertyMetadata(1));

    /// <summary>Column-span token (matches System.Windows.Controls.Grid.ColumnSpanProperty; attached, default 1 like WPF).</summary>
    public static readonly DependencyProperty ColumnSpanProperty =
        DependencyProperty.RegisterAttached("ColumnSpan", typeof(int), typeof(Grid),
            new FrameworkPropertyMetadata(1));

    /// <summary>Reads the Grid.Row offset (DP store plus attached bag, 0 when unset like WPF).</summary>
    public static int GetRow(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.HasLocalValue(RowProperty) && element.GetValue(RowProperty) is int fromStore) return fromStore;
        return AttachedInt(element, "Grid.Row", AttachedInt(element, "Row", 0));
    }

    /// <summary>Sets the Grid.Row attached offset (DP store plus attached bag, like the Canvas knobs).</summary>
    public static void SetRow(UIElement element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetAttached("Grid.Row", value);
        element.SetValue(RowProperty, value);
    }

    /// <summary>Reads the Grid.Column offset (DP store plus attached bag, 0 when unset like WPF).</summary>
    public static int GetColumn(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.HasLocalValue(ColumnProperty) && element.GetValue(ColumnProperty) is int fromStore) return fromStore;
        return AttachedInt(element, "Grid.Column", AttachedInt(element, "Column", 0));
    }

    /// <summary>Sets the Grid.Column attached offset (DP store plus attached bag, like the Canvas knobs).</summary>
    public static void SetColumn(UIElement element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetAttached("Grid.Column", value);
        element.SetValue(ColumnProperty, value);
    }

    /// <summary>Shared-size scope token (matches Grid.IsSharedSizeScopeProperty; attached, default false like WPF).</summary>
    public static readonly DependencyProperty IsSharedSizeScopeProperty =
        DependencyProperty.RegisterAttached("IsSharedSizeScope", typeof(bool), typeof(Grid),
            new FrameworkPropertyMetadata(false));

    /// <summary>Reads the Grid.IsSharedSizeScope flag (DP store plus attached bag, false when unset like WPF).</summary>
    public static bool GetIsSharedSizeScope(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.HasLocalValue(IsSharedSizeScopeProperty) && element.GetValue(IsSharedSizeScopeProperty) is bool fromStore) return fromStore;
        return element.GetAttached("Grid.IsSharedSizeScope") is bool fromBag ? fromBag : false;
    }

    /// <summary>Sets the Grid.IsSharedSizeScope flag (DP store plus attached bag, like the other grid knobs).</summary>
    public static void SetIsSharedSizeScope(UIElement element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetAttached("Grid.IsSharedSizeScope", value);
        element.SetValue(IsSharedSizeScopeProperty, value);
    }

    /// <summary>Reads the Grid.RowSpan (DP store plus attached bag, 1 when unset like WPF).</summary>
    public static int GetRowSpan(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.HasLocalValue(RowSpanProperty) && element.GetValue(RowSpanProperty) is int fromStore) return Math.Max(1, fromStore);
        return Math.Max(1, AttachedInt(element, "Grid.RowSpan", AttachedInt(element, "RowSpan", 1)));
    }

    /// <summary>Sets the Grid.RowSpan attached span (DP store plus attached bag, like the Canvas knobs).</summary>
    public static void SetRowSpan(UIElement element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetAttached("Grid.RowSpan", value);
        element.SetValue(RowSpanProperty, value);
    }

    /// <summary>Reads the Grid.ColumnSpan (DP store plus attached bag, 1 when unset like WPF).</summary>
    public static int GetColumnSpan(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.HasLocalValue(ColumnSpanProperty) && element.GetValue(ColumnSpanProperty) is int fromStore) return Math.Max(1, fromStore);
        return Math.Max(1, AttachedInt(element, "Grid.ColumnSpan", AttachedInt(element, "ColumnSpan", 1)));
    }

    /// <summary>Sets the Grid.ColumnSpan attached span (DP store plus attached bag, like the Canvas knobs).</summary>
    public static void SetColumnSpan(UIElement element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetAttached("Grid.ColumnSpan", value);
        element.SetValue(ColumnSpanProperty, value);
    }

    private int ChildRow(UIElement c, int fallback)
    {
        if (c.HasLocalValue(RowProperty) && c.GetValue(RowProperty) is int fromStore) return fromStore;
        return AttachedInt(c, "Grid.Row", AttachedInt(c, "Row", fallback));
    }

    private int ChildCol(UIElement c, int fallback)
    {
        if (c.HasLocalValue(ColumnProperty) && c.GetValue(ColumnProperty) is int fromStore) return fromStore;
        return AttachedInt(c, "Grid.Column", AttachedInt(c, "Column", fallback));
    }

    private static int ChildRowSpan(UIElement c) => GetRowSpan(c);
    private static int ChildColSpan(UIElement c) => GetColumnSpan(c);

    private static int AttachedInt(UIElement el, string name, int fallback) =>
        el.GetAttached(name) is int i ? i : fallback;

    /// <summary>Fill brush (solid brushes paint exactly; others paint their resolved fallback.
    /// Store-backed like Panel so SetValue-applied values (BAML PropertyCustom) paint.</summary>
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set
        {
            if (ReferenceEquals(GetValue(BackgroundProperty), value)) return;
            SetValue(BackgroundProperty, value);
            InvalidateVisual();
        }
    }

    private bool _showGridLines;

    /// <summary>
    /// Layout-debug adornment (matches System.Windows.Controls.Grid.ShowGridLines):
    /// dotted lines on every row/column boundary, painted over content like WPF.
    /// Engine shim: 1px black dashes (2px on, 2px off) emitted as DrawLine ops
    /// so every renderer (Skia, Canvas2D, WebGL) replays them with no protocol change.
    /// </summary>
    public bool ShowGridLines
    {
        get => _showGridLines;
        set
        {
            if (_showGridLines == value) return;
            _showGridLines = value;
            InvalidateVisual();
        }
    }

    private double[]? _arrangedColWidths;
    private double[]? _arrangedRowHeights;

    protected override void OnRender(DrawingContext dc)
    {
        if (Background is not null)
        {
            var r = RenderRect;
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height,
                BrushRender.ResolveFill(Background, 0x00000000));
        }
        foreach (UIElement c in Children) RenderVisible(dc, c);
        PaintGridLines(dc);
    }

    /// <summary>Dotted row/column boundary guides (ShowGridLines debug adornment).</summary>
    private void PaintGridLines(DrawingContext dc)
    {
        if (!_showGridLines) return;
        if (_arrangedColWidths is not { Length: > 0 } colW) return;
        if (_arrangedRowHeights is not { Length: > 0 } rowH) return;
        if (colW.Length != ColCount || rowH.Length != RowCount) return;
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        const uint ink = 0x000000FFu;
        var x = r.X;
        DottedVertical(dc, (float)x, (float)r.Y, (float)(r.Y + r.Height), ink);
        foreach (var w in colW)
        {
            x += w;
            DottedVertical(dc, (float)x, (float)r.Y, (float)(r.Y + r.Height), ink);
        }
        var y = r.Y;
        DottedHorizontal(dc, (float)r.X, (float)(r.X + r.Width), (float)y, ink);
        foreach (var h in rowH)
        {
            y += h;
            DottedHorizontal(dc, (float)r.X, (float)(r.X + r.Width), (float)y, ink);
        }
    }

    private static void DottedVertical(DrawingContext dc, float x, float y0, float y1, uint rgba)
    {
        for (var y = y0; y < y1;)
        {
            var end = Math.Min(y + 2, y1);
            dc.DrawLine(x, y, x, end, 1, rgba);
            y += 4;
        }
    }

    private static void DottedHorizontal(DrawingContext dc, float x0, float x1, float y, uint rgba)
    {
        for (var x = x0; x < x1;)
        {
            var end = Math.Min(x + 2, x1);
            dc.DrawLine(x, y, end, y, 1, rgba);
            x += 4;
        }
    }
}

/// <summary>Button: bordered box with centered label.</summary>
/// <summary>
/// ContentControl (WPF subset): single-content host. Real WPF carries
/// DataTemplate/content-template machinery; the engine keeps the subset
/// converted apps bind against (generated ContentControl.get_Content).
/// </summary>
// Matches the real ContentProperty("Content") on
// System.Windows.Controls.ContentControl: converted subclasses inherit it,
// so their XAML children parent through TryAttachCustomContent.
[ContentProperty("Content")]
public class ContentControl : Control
{
    /// <summary>
    /// Content token (matches System.Windows.Controls.ContentControl.ContentProperty;
    /// converted DockingManager.DisconnectFromVisualParent clears bindings through it
    /// and LayoutFloatingWindowControl overrides its metadata; the store is the
    /// one authority behind the Content CLR, like real WPF).
    /// </summary>
    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.Register("Content", typeof(object), typeof(ContentControl),
            new FrameworkPropertyMetadata(string.Empty));

    private static readonly DependencyPropertyKey HasContentPropertyKey =
        DependencyProperty.RegisterReadOnly("HasContent", typeof(bool), typeof(ContentControl),
            new FrameworkPropertyMetadata(false));

    /// <summary>Content-presence token (matches ContentControl.HasContentProperty; read-only like real WPF).</summary>
    public static readonly DependencyProperty HasContentProperty = HasContentPropertyKey.DependencyProperty;

    /// <summary>Hosted content (object like real WPF; text renders as a label, visuals host live).
    /// The DP store is the one authority (CLR and SetValue read back
    /// identically), so theme TemplateBindings of ContentControl.Content
    /// see XAML-routed content like WPF.</summary>
    public virtual object Content
    {
        get => GetValue(ContentProperty) ?? string.Empty;
        set
        {
            if (Equals(GetValue(ContentProperty), value)) return;
            SetValue(ContentProperty, value);
            SetValue(HasContentPropertyKey, value is not null);
            InvalidateMeasure();
        }
    }

    /// <summary>Whether content is present (matches ContentControl.HasContent; live-verified against real WPF on net10.0-windows: null is absent, everything else including string.Empty counts as content).</summary>
    public bool HasContent => GetValue(ContentProperty) is not null;

    /// <summary>
    /// String-format token (matches System.Windows.Controls.ContentControl.ContentStringFormatProperty;
    /// null default like WPF; applied to non-visual content with no template, like ContentPresenter).
    /// </summary>
    public static readonly DependencyProperty ContentStringFormatProperty =
        DependencyProperty.Register("ContentStringFormat", typeof(string), typeof(ContentControl),
            new FrameworkPropertyMetadata((string?)null) { AffectsMeasure = true });

    /// <summary>
    /// Composite format applied when the content displays as a string (matches
    /// System.Windows.Controls.ContentControl.ContentStringFormat; null keeps
    /// Content.ToString(), like WPF).
    /// </summary>
    public string? ContentStringFormat
    {
        get => (string?)GetValue(ContentStringFormatProperty);
        set => SetValue(ContentStringFormatProperty, value);
    }

    /// <summary>Template token (matches System.Windows.Controls.ContentControl.ContentTemplateProperty).</summary>
    public static readonly DependencyProperty ContentTemplateProperty =
        DependencyProperty.Register("ContentTemplate", typeof(DataTemplate), typeof(ContentControl),
            new FrameworkPropertyMetadata(null));

    /// <summary>
    /// Fixed visual factory for the content (matches
    /// System.Windows.Controls.ContentControl.ContentTemplate; template parts
    /// bind it through TemplateBinding, like the Card content host).
    /// </summary>
    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    /// <summary>Template-selector token (matches System.Windows.Controls.ContentControl.ContentTemplateSelectorProperty).</summary>
    public static readonly DependencyProperty ContentTemplateSelectorProperty =
        DependencyProperty.Register("ContentTemplateSelector", typeof(DataTemplateSelector), typeof(ContentControl),
            new FrameworkPropertyMetadata(null));

    /// <summary>
    /// Per-content visual choice (matches
    /// System.Windows.Controls.ContentControl.ContentTemplateSelector;
    /// consulted when <see cref="ContentTemplate"/> is null, like WPF).
    /// </summary>
    public DataTemplateSelector? ContentTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ContentTemplateSelectorProperty);
        set => SetValue(ContentTemplateSelectorProperty, value);
    }

    /// <summary>
    /// Shared content-to-text rule (live-verified against real WPF on
    /// net10.0-windows: 500.0 with "#0.00" renders "500.00", "{0:0.00}"
    /// renders "500.00", "Value: {0}" renders "Value: 500", strings ignore
    /// the format, and de-DE still renders "500.00"): null renders empty,
    /// strings render as-is, an empty format keeps ToString, a
    /// brace-carrying format runs composite string.Format, otherwise the
    /// format applies as the numeric format ("{0:format}"), all invariant.
    /// </summary>
    internal static string FormatContentText(object? content, string? format)
    {
        if (content is null) return string.Empty;
        if (content is string text) return text;
        if (string.IsNullOrEmpty(format)) return content.ToString() ?? string.Empty;
        if (format.Contains('{'))
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, format, content);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:" + format + "}", content);
    }

    /// <summary>
    /// Moves visual content in and out of the visual tree (opt-in per
    /// subclass: UserControl and Button host live visuals; Expander manages
    /// its own visual slots and ToggleButton/CheckBox stay text-only).
    /// </summary>
    protected void SyncContentVisual(object? oldValue, object? newValue)
    {
        if (oldValue is UIElement oldVisual) DetachVisualChild(oldVisual);
        if (newValue is UIElement visual) AttachVisualChild(visual);
    }

    /// <summary>
    /// Base host paints its applied template, if any, and nothing otherwise
    /// (matches Button: TemplateRoot first; subclasses render content).
    /// Converted templated controls (CardDemo hc:Card) paint their default
    /// template through this path.
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        if (TemplateRoot is not null) RenderVisible(dc, TemplateRoot);
    }
}

/// <summary>
/// Expander (matches System.Windows.Controls.Expander): a header row plus
/// collapsible content. Header click toggles <see cref="IsExpanded"/>; content
/// hosts real visuals when expanded and detaches when collapsed.
/// </summary>
public class Expander : ContentControl
{
    static Expander()
    {
        // Expander headers are not tab stops (live-verified WPF default), so
        // keyboard Tab walks past collapsed/expanded group headers.
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(Expander), new FrameworkPropertyMetadata(false));
    }

    private object? _header;
    private bool _isExpanded;
    private ExpandDirection _expandDirection;
    private bool _pressed;

    /// <summary>
    /// Open direction (matches System.Windows.Controls.ExpandDirection;
    /// layout stays downward, the direction is stored).
    /// </summary>
    public ExpandDirection ExpandDirection
    {
        get => _expandDirection;
        set
        {
            if (_expandDirection == value) return;
            _expandDirection = value;
            InvalidateMeasure();
        }
    }
    private UIElement? _headerVisual;
    private UIElement? _contentVisual;
    private readonly StackPanel _layout = new() { Orientation = Orientation.Vertical };
    private readonly StackPanel _headerRow = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _chevron = new() { Text = ">" };

    public Expander()
    {
        AddChild(_layout);
        _layout.AddChild(_headerRow);
        _headerRow.AddChild(_chevron);
        SyncHeader();
        SyncExpanded();
    }

    /// <summary>Expanded/collapsed notifications (routed in real WPF; direct here).</summary>
    public event RoutedEventHandler? Expanded;

    /// <summary>Expanded/collapsed notifications (routed in real WPF; direct here).</summary>
    public event RoutedEventHandler? Collapsed;

    /// <summary>Header content (string renders as text; visuals host live).</summary>
    public object? Header
    {
        get => _header;
        set
        {
            if (Equals(_header, value)) return;
            _header = value;
            SyncHeader();
            InvalidateMeasure();
        }
    }

    public override object Content
    {
        get => base.Content;
        set
        {
            if (Equals(base.Content, value)) return;
            DetachContentVisual();
            base.Content = value;
            SyncExpanded();
            InvalidateMeasure();
        }
    }

    /// <summary>Whether the content shows (chevron and events follow).</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            SyncExpanded();
            InvalidateMeasure();
            if (value) Expanded?.Invoke(this, new RoutedEventArgs { Source = this });
            else Collapsed?.Invoke(this, new RoutedEventArgs { Source = this });
        }
    }

    private void SyncHeader()
    {
        if (_headerVisual is not null)
        {
            _headerRow.RemoveChild(_headerVisual);
            _headerVisual = null;
        }
        _headerVisual = _header is UIElement visual ? visual : new TextBlock { Text = _header?.ToString() ?? string.Empty };
        _headerRow.AddChild(_headerVisual);
    }

    private void DetachContentVisual()
    {
        if (_contentVisual is null) return;
        _layout.RemoveChild(_contentVisual);
        _contentVisual = null;
    }

    private void SyncExpanded()
    {
        DetachContentVisual();
        _chevron.Text = _isExpanded ? "v" : ">";
        // WPF parity: visual content stays in the expander's logical tree
        // whether or not it is expanded, so template-time walks (TemplateParent
        // assignment, ItemsPresenter row hosting, header bindings) resolve for
        // collapsed content and rows re-attach on expand without a rebuild.
        // Only the visual slot (_layout) is expansion-gated.
        if (base.Content is UIElement visual && !ReferenceEquals(visual.Parent, this))
            AddChild(visual);
        if (!_isExpanded) return;
        _contentVisual = base.Content is UIElement hosted
            ? hosted
            : new TextBlock { Text = base.Content?.ToString() ?? string.Empty };
        _layout.AddChild(_contentVisual);
    }

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        switch (e.Name)
        {
            case WasmRoutedEvent.PointerDownName
                when e.IsLeftButtonDown && e.Source is Core.WasmInputEvent.Pointer down &&
                    _headerRow.HitTest(new WpfPoint(down.Args.X, down.Args.Y)):
                _pressed = true;
                e.Handled = true;
                break;
            case WasmRoutedEvent.PointerUpName
                when e.Source is Core.WasmInputEvent.Pointer p && p.Args.Kind == Core.WasmPointerKind.Up:
                if (!_pressed) break;
                _pressed = false;
                if (_headerRow.HitTest(new WpfPoint(p.Args.X, p.Args.Y)))
                {
                    IsExpanded = !IsExpanded;
                    e.Handled = true;
                }
                break;
            case WasmRoutedEvent.PointerCancelName:
                _pressed = false;
                break;
        }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        _layout.Measure(available);
        return _layout.DesiredSize;
    }

    protected override void ArrangeCore(WpfRect finalRect) => _layout.Arrange(finalRect);

    protected override void OnRender(DrawingContext dc) => RenderVisible(dc, _layout);
}

/// <summary>
/// GroupBox (matches System.Windows.Controls.GroupBox): a bordered frame
/// with a header caption overlapping the top edge and hosted content below.
/// Header text renders through an internal caption; header visuals host
/// live like HeaderedContentControl. The frame always paints (real WPF
/// draws the group box even with no explicit BorderThickness); an explicit
/// BorderThickness widens it and BorderBrush/Background recolor it.
/// </summary>
public class GroupBox : HeaderedContentControl
{
    static GroupBox()
    {
        // Group frames never take keyboard focus (live-verified WPF default).
        FocusableProperty.OverrideMetadata(typeof(GroupBox), new FrameworkPropertyMetadata(false));
    }

    private const double HeaderInset = 8.0;
    private const uint DefaultFrameRgba = 0x808080FF;

    private readonly TextBlock _caption = new();
    private double _headerW;
    private double _headerH;

    private double FrameThickness => BorderThickness.Left > 0 ? BorderThickness.Left : 1.0;

    private uint FrameRgba => BorderBrush is null
        ? DefaultFrameRgba
        : BrushRender.ResolveFill(BorderBrush, DefaultFrameRgba);

    private UIElement? HeaderVisual =>
        Header is UIElement visual ? visual : null;

    private (double Width, double Height) MeasureHeader(WpfSize available)
    {
        if (!HasHeader)
        {
            _headerW = 0;
            _headerH = 0;
            return (0, 0);
        }
        if (HeaderVisual is { } visual)
        {
            visual.Measure(available);
            _headerW = visual.DesiredSize.Width;
            _headerH = visual.DesiredSize.Height;
            return (_headerW, _headerH);
        }
        _caption.Text = Header?.ToString() ?? string.Empty;
        _caption.Measure(available);
        _headerW = _caption.DesiredSize.Width;
        _headerH = _caption.DesiredSize.Height;
        return (_headerW, _headerH);
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var frame = FrameThickness;
        var (headerW, headerH) = MeasureHeader(available);
        var insetW = frame * 2 + Padding.Left + Padding.Right;
        // The frame top rides the header midline, but content starts fully
        // below the header caption so the first row never collides with it.
        var insetH = headerH + frame + Padding.Top + Padding.Bottom + frame;
        var contentW = 0.0;
        var contentH = 0.0;
        if (Content is UIElement visual)
        {
            visual.Measure(new WpfSize(
                Math.Max(0, available.Width - insetW),
                Math.Max(0, available.Height - insetH)));
            contentW = visual.DesiredSize.Width;
            contentH = visual.DesiredSize.Height;
        }
        return new WpfSize(
            Math.Max(headerW + HeaderInset * 2, contentW + insetW),
            headerH + frame + Padding.Top + contentH + Padding.Bottom + frame);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        var frame = FrameThickness;
        var (headerW, headerH) = MeasureHeader(
            new WpfSize(Math.Max(0, finalRect.Width - HeaderInset * 2), double.PositiveInfinity));
        var headerX = finalRect.X + HeaderInset;
        if (HeaderVisual is { } visual)
            visual.Arrange(new WpfRect(headerX, finalRect.Y, headerW, headerH));
        else
            _caption.Arrange(new WpfRect(headerX, finalRect.Y, headerW, headerH));
        var body = new WpfRect(
            finalRect.X + frame + Padding.Left,
            finalRect.Y + headerH + frame + Padding.Top,
            Math.Max(0, finalRect.Width - frame * 2 - Padding.Left - Padding.Right),
            Math.Max(0, finalRect.Height - headerH - frame * 2 - Padding.Top - Padding.Bottom));
        if (Content is UIElement content) content.Arrange(body);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        var frame = FrameThickness;
        var edge = FrameRgba;
        // Arrange-time header metrics (never measure inside the render pass).
        var headerW = _headerW;
        var headerH = _headerH;
        var top = (float)(r.Y + headerH / 2);
        // Frame: full bottom/left/right edges plus a top edge with a gap
        // where the header caption overlaps (the classic group-box look).
        var headerX0 = (float)(r.X + HeaderInset - 2);
        var headerX1 = (float)(r.X + HeaderInset + headerW + 2);
        var right = (float)(r.X + r.Width);
        var bottom = (float)(r.Y + r.Height);
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height,
            BrushRender.ResolveFill(Background, 0x00000000));
        dc.DrawLine((float)r.X, bottom, (float)r.X, top, (float)frame, edge);
        dc.DrawLine(right, bottom, right, top, (float)frame, edge);
        dc.DrawLine((float)r.X, bottom, right, bottom, (float)frame, edge);
        if (HasHeader)
        {
            dc.DrawLine((float)r.X, top, Math.Min(headerX0, right), top, (float)frame, edge);
            dc.DrawLine(Math.Min(headerX1, right), top, right, top, (float)frame, edge);
        }
        else
        {
            dc.DrawLine((float)r.X, top, right, top, (float)frame, edge);
        }
        if (HasHeader)
        {
            if (HeaderVisual is { } visual) RenderVisible(dc, visual);
            else
            {
                _caption.Text = Header?.ToString() ?? string.Empty;
                _caption.Render(dc);
            }
        }
        if (Content is UIElement content) RenderVisible(dc, content);
    }
}

/// <summary>Separator: a 1px rule between items (matches System.Windows.Controls.Separator).</summary>
public class Separator : Control
{
    static Separator()
    {
        // Menu rows never take keyboard focus in the engine model (presses
        // route through highlight, not focus; MenuItem is likewise
        // unfocusable), so the separator opts out of the Control true
        // default: a focusable separator would steal menu focus on press
        // and Menu.OnLostFocus would drop the open submenu.
        FocusableProperty.OverrideMetadata(typeof(Separator), new FrameworkPropertyMetadata(false));
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var w = double.IsNaN(Width) ? 0 : Width;
        var h = double.IsNaN(Height) ? 1 : Height;
        return new WpfSize(Math.Min(available.Width, Math.Max(0, w)), Math.Min(available.Height, Math.Max(0, h)));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        var y = (float)(r.Y + r.Height / 2);
        dc.DrawRectangle((float)r.X, y, (float)r.Width, 1, 0x707070FF);
    }
}

/// <summary>Click timing (matches System.Windows.Controls.ClickMode:
/// Release fires on release-inside, Press fires on press, Hover fires when
/// the pointer enters; Release is the default like real WPF).</summary>
public enum ClickMode
{
    Release = 0,
    Press = 1,
    Hover = 2,
}

/// <summary>
/// ButtonBase (WPF subset): Click plus the command source. Converted apps
/// bind generated <c>ButtonBase.add_Click</c> references here; behavior
/// matches the old Button surface exactly.
/// </summary>
public class ButtonBase : ContentControl
{
    /// <summary>
    /// Click timing (matches System.Windows.Controls.Primitives.ButtonBase.ClickMode):
    /// Release clicks on release-inside (the default), Press clicks on press,
    /// Hover clicks when the pointer enters. The PC Space Map ComboBox
    /// template drives its drop-down toggle with ClickMode="Press".
    /// </summary>
    public ClickMode ClickMode { get; set; } = ClickMode.Release;
    /// <summary>Click token (matches ButtonBase.ClickEvent; style EventSetters bind through it).</summary>
    public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent(
        "Click", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ButtonBase));

    /// <summary>Leaf activation (routed in real WPF; direct here).</summary>
    public event RoutedEventHandler? Click;

    private static readonly DependencyPropertyKey IsPressedKey =
        DependencyProperty.RegisterReadOnly("IsPressed", typeof(bool), typeof(ButtonBase),
            new FrameworkPropertyMetadata(false));

    /// <summary>Press token (matches ButtonBase.IsPressedProperty; read-only like real WPF).</summary>
    public static readonly DependencyProperty IsPressedProperty = IsPressedKey.DependencyProperty;

    /// <summary>Whether the button is currently pressed (drives IsPressed triggers).</summary>
    public bool IsPressed => (bool)(GetValue(IsPressedProperty) ?? false);

    /// <summary>Press-state write for subclasses (matches the read-only DP contract).</summary>
    protected void SetIsPressed(bool value) => SetValue(IsPressedKey, value);

    /// <summary>
    /// Fires Click, then executes <see cref="Command"/> when it can execute.
    /// Buttons with a command that currently cannot execute behave (and look)
    /// disabled: no press, no click, no keyboard activation.
    /// </summary>
    public void RaiseClick()
    {
        if (!IsEffectivelyEnabled) return;
        Click?.Invoke(this, new RoutedEventArgs { Source = this });
        if (Command is RoutedCommand routed)
            routed.Execute(CommandParameter, CommandTarget as UIElement ?? Keyboard.FocusedElement);
        else Command?.Execute(CommandParameter);
    }

    /// <summary>
    /// Click dispatch (matches ButtonBase.OnClick; converted AvalonDock
    /// drop-down buttons override it to open their popups). The press paths
    /// below route through it, so overrides fire exactly like WPF; the base
    /// behavior is RaiseClick.
    /// </summary>
    protected virtual void OnClick() => RaiseClick();

    /// <summary>
    /// Keyboard activation entry (matches ButtonBase keyboard handling:
    /// Enter/Space route through the virtual OnClick so toggle buttons flip
    /// through ToggleButton.OnClick, like WPF; calling RaiseClick directly
    /// would fire Click without toggling).
    /// </summary>
    internal void ActivateByKeyboard() => OnClick();

    private ICommand? _command;
    private bool _commandCanExecute = true;

    private IInputElement? _commandTarget;

    /// <summary>Command target (defaults to the focused element when null).</summary>
    public IInputElement? CommandTarget
    {
        get => _commandTarget;
        set
        {
            if (ReferenceEquals(_commandTarget, value)) return;
            _commandTarget = value;
            UpdateCanExecute();
        }
    }

    /// <summary>Bound command (executes on click; drives greyed state).</summary>
    public ICommand? Command
    {
        get => _command;
        set
        {
            if (ReferenceEquals(_command, value)) return;
            if (_command is not null) _command.CanExecuteChanged -= OnCommandCanExecuteChanged;
            _command = value;
            if (_command is not null) _command.CanExecuteChanged += OnCommandCanExecuteChanged;
            UpdateCanExecute();
        }
    }

    private object? _commandParameter;

    /// <summary>Parameter forwarded to <see cref="Command"/> (plain value or binding).</summary>
    public object? CommandParameter
    {
        get => _commandParameter;
        set
        {
            if (Equals(_commandParameter, value)) return;
            _commandParameter = value;
            UpdateCanExecute();
        }
    }

    /// <summary>Local flag AND command gate.</summary>
    public override bool IsEffectivelyEnabled => IsEnabled && _commandCanExecute;

    private void OnCommandCanExecuteChanged(object? sender, EventArgs e) => UpdateCanExecute();

    private void UpdateCanExecute()
    {
        // Routed commands requery against the command target (focused
        // element when unset), mirroring CommandHelpers; plain commands
        // evaluate directly.
        var can = _command switch
        {
            RoutedCommand routed => routed.CanExecute(CommandParameter, CommandTarget as UIElement ?? Keyboard.FocusedElement),
            null => true,
            _ => _command.CanExecute(CommandParameter),
        };
        if (_commandCanExecute == can) return;
        _commandCanExecute = can;
        InvalidateVisual();
    }
}

public class Button : ButtonBase
{
    public uint BackgroundRgba { get; set; } = 0xDDDDDDFF;
    public uint BorderRgba { get; set; } = 0x707070FF;
    public uint ForegroundRgba { get; set; } = 0x000000FF;
    public string FontCss { get; set; } = "14px sans-serif";

    /// <summary>Double-click detection window (test seam; real systems use the OS setting).</summary>
    internal static TimeSpan DoubleClickWindow { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Clock for double-click detection (tests pin this).</summary>
    internal static Func<DateTime> Clock { get; set; } = static () => DateTime.UtcNow;

    /// <summary>Buttons take focus (store-backed; the Control default is true).</summary>
    public override bool Focusable
    {
        get => (bool?)GetValue(FocusableProperty) ?? true;
        set => SetValue(FocusableProperty, value);
    }

    private static readonly DependencyPropertyKey IsDefaultedKey =
        DependencyProperty.RegisterReadOnly("IsDefaulted", typeof(bool), typeof(Button),
            new FrameworkPropertyMetadata(false));

    /// <summary>Default-button token (matches Button.IsDefaultedProperty; read-only like real WPF).</summary>
    public static readonly DependencyProperty IsDefaultedProperty = IsDefaultedKey.DependencyProperty;

    /// <summary>Whether the button is the active default button (drives IsDefaulted triggers;
    /// focus-scope activation tracking stays a precise boundary).</summary>
    public bool IsDefaulted => (bool)(GetValue(IsDefaultedProperty) ?? false);

    /// <summary>Default-state write for subclasses (matches the read-only DP contract).</summary>
    protected void SetIsDefaulted(bool value) => SetValue(IsDefaultedKey, value);

    private DateTime _lastClickAt = DateTime.MinValue;
    private WpfPoint _lastClickAtPoint;
    private bool _hoverInside;

    protected override void OnBackgroundChanged(Brush? brush)
    {
        if (brush is SolidColorBrush solid) BackgroundRgba = solid.ToRgba();
    }

    protected override void OnBorderBrushChanged(Brush? brush)
    {
        if (brush is SolidColorBrush solid) BorderRgba = solid.ToRgba();
    }

    protected override void OnForegroundChanged(Brush? brush)
    {
        if (brush is SolidColorBrush solid) ForegroundRgba = solid.ToRgba();
    }

    protected override void OnFontChanged() => FontCss = ComposeFontCss();

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        // ButtonBase press/capture/release: press arms the button, Click fires
        // on release inside the button, cancel/drag-out never click. The input
        // manager captures the pointer to the pressed target, so release and
        // cancel still reach us outside our rect; HitTest decides "inside".
        // Disabled (or command-blocked) buttons ignore all pointer input.
        // A second qualifying click inside the window raises MouseDoubleClick.
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        switch (e.Name)
        {
            case WasmRoutedEvent.PointerDownName when e.IsLeftButtonDown:
                SetIsPressed(true);
                if (ClickMode == ClickMode.Press)
                    OnClick();
                e.Handled = true;
                break;
            case WasmRoutedEvent.PointerUpName
                when e.Source is Core.WasmInputEvent.Pointer p && p.Args.Kind == Core.WasmPointerKind.Up:
                if (!IsPressed) break;
                SetIsPressed(false);
                // Press already clicked on the way down; only Release clicks here.
                if (ClickMode != ClickMode.Release) break;
                var at = new WpfPoint(p.Args.X, p.Args.Y);
                if (HitTest(at))
                {
                    OnClick();
                    var now = Clock();
                    if (now - _lastClickAt <= DoubleClickWindow &&
                        Math.Abs(at.X - _lastClickAtPoint.X) < 4 &&
                        Math.Abs(at.Y - _lastClickAtPoint.Y) < 4)
                    {
                        RaiseMouseDoubleClick(new MouseButtonEventArgs(
                            Mouse.PrimaryDevice, Environment.TickCount,
                            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
                            MouseButtonState.Released, MouseButtonState.Released,
                            MouseButton.Left, MouseButtonState.Pressed, 2));
                        _lastClickAt = DateTime.MinValue;
                    }
                    else
                    {
                        _lastClickAt = now;
                        _lastClickAtPoint = at;
                    }
                    e.Handled = true;
                }
                break;
            case WasmRoutedEvent.PointerCancelName:
                SetIsPressed(false);
                _hoverInside = false;
                break;
            case WasmRoutedEvent.PointerMoveName
                when e.Source is Core.WasmInputEvent.Pointer hover && hover.Args.Kind == Core.WasmPointerKind.Move:
                // Hover clicks on pointer entry (like real WPF); other modes
                // only track entry so a later Press/Release still arms.
                var over = HitTest(new WpfPoint(hover.Args.X, hover.Args.Y));
                if (over && !_hoverInside && ClickMode == ClickMode.Hover)
                    OnClick();
                _hoverInside = over;
                break;
        }
    }

    public override object Content
    {
        get => base.Content;
        set
        {
            if (Equals(base.Content, value)) return;
            var old = base.Content;
            base.Content = value;
            SyncContentVisual(old, value);
        }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        // Templated buttons measure to their inflated root (the template
        // replaces the default chrome, like WPF).
        if (TemplateRoot is not null)
        {
            TemplateRoot.Measure(available);
            return TemplateRoot.DesiredSize;
        }
        // Visual content (HeaderTile tiles, icon buttons) sizes to the live
        // visual; text keeps the shaped-metrics path and legacy minimums.
        if (Content is UIElement visual)
        {
            visual.Measure(available);
            var want = visual.DesiredSize;
            return new(
                double.IsNaN(Width) ? Math.Min(available.Width, Math.Max(120, want.Width + 16)) : Width,
                double.IsNaN(Height) ? Math.Max(32, want.Height + 12) : Height);
        }
        // Shaped metrics size the content; the legacy minimums keep small
        // controls (and all pre-existing layout snapshots) stable.
        var label = Content?.ToString() ?? string.Empty;
        var (shapedW, shapedH) = TryMeasure(label, FontCss, label.Length * 8, 16);
        return new(
            double.IsNaN(Width) ? Math.Min(available.Width, Math.Max(120, shapedW + 16)) : Width,
            double.IsNaN(Height) ? Math.Max(32, shapedH + 12) : Height);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (TemplateRoot is not null)
        {
            TemplateRoot.Arrange(finalRect);
            return;
        }
        if (Content is UIElement visual)
            visual.Arrange(new WpfRect(
                finalRect.X + 1, finalRect.Y + 1,
                Math.Max(0, finalRect.Width - 2), Math.Max(0, finalRect.Height - 2)));
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (TemplateRoot is not null)
        {
            RenderVisible(dc, TemplateRoot);
            return;
        }
        var r = RenderRect;
        // Command-blocked or disabled buttons render greyed out.
        var border = IsEffectivelyEnabled ? BorderRgba : 0x909090FF;
        var fill = IsEffectivelyEnabled ? BackgroundRgba : 0xCCCCCCFF;
        var ink = IsEffectivelyEnabled ? ForegroundRgba : 0x707070FF;
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, border);
        if (r.Width > 2 && r.Height > 2)
            dc.DrawRectangle((float)r.X + 1, (float)r.Y + 1, (float)r.Width - 2, (float)r.Height - 2, fill);
        if (Content is UIElement visual)
        {
            RenderVisible(dc, visual);
            return;
        }
        var label = Content?.ToString() ?? string.Empty;
        if (!string.IsNullOrEmpty(label))
            dc.DrawText(label, (float)(r.X + r.Width / 2), (float)(r.Y + r.Height / 2), FontCss, ink, 1, 2, -1);
    }
}

/// <summary>Image: bitmap host (matches System.Windows.Controls.Image).</summary>
public class Image : FrameworkElement
{
    /// <summary>
    /// Bitmap-source identity (matches Image.SourceProperty; the converted
    /// HandyControl ImageAttach fallback reads and writes it through the
    /// store, so the CLR wrapper is store-backed).
    /// </summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register("Source", typeof(ImageSource), typeof(Image),
            new PropertyMetadata(null));

    /// <summary>Bitmap source (undecoded sources measure zero until pixels arrive).</summary>
    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set
        {
            if (ReferenceEquals(Source, value)) return;
            SetValue(SourceProperty, value);
            InvalidateMeasure();
            InvalidateVisual();
            if (value is BitmapImage bitmap && IsPackAbsent(bitmap))
                ImageFailed?.Invoke(this, new ExceptionRoutedEventArgs(
                    new System.IO.FileNotFoundException($"Pack image '{bitmap.UriSource}' is absent.")));
        }
    }

    /// <summary>
    /// Source-load failure hook (matches Image.ImageFailed, which converted
    /// HandyControl ImageAttach re-hooks to drive its SourceFailed fallback;
    /// real WPF shape: EventHandler&lt;ExceptionRoutedEventArgs&gt;). Raised
    /// when a pack-URI source names bytes the staged manifest does not
    /// carry; nothing else loads eagerly, so nothing else fails here.
    /// </summary>
    public event EventHandler<ExceptionRoutedEventArgs>? ImageFailed;

    private static bool IsPackAbsent(BitmapImage bitmap)
    {
        if (bitmap.UriSource is null || bitmap.StreamSource is not null) return false;
        try
        {
            var uri = bitmap.UriSource;
            var parsed = PackUri.Parse(uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString);
            return WpfAppBoot.FindPackBytes(parsed.Path, parsed.Assembly) is null;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public Stretch Stretch { get; set; } = Stretch.Uniform;

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var wantW = double.IsNaN(Width) ? 0 : Width;
        var wantH = double.IsNaN(Height) ? 0 : Height;
        var w = Source?.RgbaPixels is not null
            ? Math.Min(available.Width, Math.Max(wantW, Source.Width))
            : Width;
        var h = Source?.RgbaPixels is not null
            ? Math.Min(available.Height, Math.Max(wantH, Source.Height))
            : Height;
        return new(
            double.IsNaN(w) ? 0 : w,
            double.IsNaN(h) ? 0 : h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Source?.RgbaPixels is not { } rgba) return;
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        var (dw, dh) = Stretch switch
        {
            Stretch.None => ((double)rgba.Width, (double)rgba.Height),
            Stretch.Fill => (r.Width, r.Height),
            Stretch.UniformToFill => ScaleToFill(rgba.Width, rgba.Height, r.Width, r.Height, fill: true),
            _ => ScaleToFill(rgba.Width, rgba.Height, r.Width, r.Height, fill: false),
        };
        var dx = Stretch == Stretch.None ? r.X : r.X + (r.Width - dw) / 2;
        var dy = Stretch == Stretch.None ? r.Y : r.Y + (r.Height - dh) / 2;
        dc.DrawImage(rgba.Pixels, rgba.Width, rgba.Height, (float)dx, (float)dy, (float)dw, (float)dh);
    }

    private static (double W, double H) ScaleToFill(double sw, double sh, double dw, double dh, bool fill)
    {
        if (sw <= 0 || sh <= 0) return (0, 0);
        var scale = fill
            ? Math.Max(dw / sw, dh / sh)
            : Math.Min(dw / sw, dh / sh);
        return (sw * scale, sh * scale);
    }
}

/// <summary>TextBlock: single text run.</summary>
/// <summary>Text trimming (matches System.Windows.TextTrimming; retained, layout keeps full text).</summary>
public enum TextTrimming
{
    None,
    CharacterEllipsis,
    WordEllipsis,
}

/// <summary>Horizontal text alignment (matches System.Windows.TextAlignment).</summary>
public enum TextAlignment
{
    Left,
    Right,
    Center,
    Justify,
}

/// <summary>Line stacking strategy (matches System.Windows.LineStackingStrategy; retained).</summary>
public enum LineStackingStrategy
{
    MaxHeight,
    BlockLineHeight,
    BaselineToBaseline,
}

/// <summary>Text wrapping behavior (matches System.Windows.TextWrapping).</summary>
public enum TextWrapping
{
    WrapWithOverflow,
    NoWrap,
    Wrap,
}

public class TextBlock : FrameworkElement, IDocumentFontSource
{
    /// <summary>Wrapping rule (parsed and retained; layout keeps single-line metrics in v1).</summary>
    public TextWrapping TextWrapping { get; set; } = TextWrapping.WrapWithOverflow;

    /// <summary>Trimming identity (matches System.Windows.Controls.TextBlock.TextTrimmingProperty;
    /// verified against the net10.0-windows runtime: TextBlock-owned, TextTrimming-typed,
    /// None default with AffectsMeasure|AffectsRender metadata. The CLR wrapper stays
    /// store-backed so template/style setters land through the DP store; layout keeps
    /// full text in v1 like the other retained text knobs.)</summary>
    public static readonly DependencyProperty TextTrimmingProperty =
        DependencyProperty.Register("TextTrimming", typeof(TextTrimming), typeof(TextBlock),
            new FrameworkPropertyMetadata(TextTrimming.None, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Trimming rule (parsed and retained; layout keeps full text in v1).</summary>
    public TextTrimming TextTrimming
    {
        get => (TextTrimming?)GetValue(TextTrimmingProperty) ?? TextTrimming.None;
        set { SetValue(TextTrimmingProperty, value); InvalidateMeasure(); InvalidateVisual(); }
    }

    /// <summary>
    /// Font identities (matches TextBlock.Font*Property; the WPF-identical shared
    /// TextElement instances, so template/style setters and converted static field
    /// loads bind; the CLR wrappers stay field-backed).
    /// </summary>
    public static readonly DependencyProperty FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner(typeof(TextBlock));
    public static readonly DependencyProperty FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner(typeof(TextBlock));
    public static readonly DependencyProperty FontStretchProperty =
        TextElement.FontStretchProperty.AddOwner(typeof(TextBlock));
    public static readonly DependencyProperty FontStyleProperty =
        TextElement.FontStyleProperty.AddOwner(typeof(TextBlock));
    public static readonly DependencyProperty FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner(typeof(TextBlock));

    /// <summary>Horizontal alignment of text lines (parsed and retained; layout keeps left origin in v1).</summary>
    public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;

    /// <summary>Line stacking rule (parsed and retained; layout keeps MaxHeight behavior in v1).</summary>
    public LineStackingStrategy LineStackingStrategy { get; set; } = LineStackingStrategy.MaxHeight;

    private FontFamily _fontFamily = new("sans-serif");
    private double _fontSize = 14.0;
    private FontWeight _fontWeight = FontWeights.Normal;
    private FontStyle _fontStyle = FontStyles.Normal;
    private FontStretch _fontStretch = FontStretches.Normal;

    /// <summary>Font family (recomposes the legacy CSS string).</summary>
    public FontFamily FontFamily
    {
        get => _fontFamily;
        set { if (value is null || _fontFamily.Equals(value)) return; _fontFamily = value; OnFontChanged(); InvalidateMeasure(); }
    }

    /// <summary>Font size in device-independent pixels (recomposes the legacy CSS string).</summary>
    public double FontSize
    {
        get => _fontSize;
        set { if (_fontSize == value) return; _fontSize = value; OnFontChanged(); InvalidateMeasure(); }
    }

    /// <summary>Font weight (recomposes the legacy CSS string).</summary>
    public FontWeight FontWeight
    {
        get => _fontWeight;
        set { if (_fontWeight == value) return; _fontWeight = value; OnFontChanged(); InvalidateMeasure(); }
    }

    /// <summary>Font slant (recomposes the legacy CSS string).</summary>
    public FontStyle FontStyle
    {
        get => _fontStyle;
        set { if (_fontStyle == value) return; _fontStyle = value; OnFontChanged(); InvalidateMeasure(); }
    }

    /// <summary>Font stretch (retained-only in v1; the CSS recomposition keeps family/size/slant).</summary>
    public FontStretch FontStretch
    {
        get => _fontStretch;
        set { if (_fontStretch == value) return; _fontStretch = value; OnFontChanged(); InvalidateMeasure(); }
    }

    protected override void OnFontChanged() => FontCss = ComposeSegmentCss(FontWeight, FontStyle, FontSize, FontFamily);

    /// <summary>Legacy CSS font description with a bold channel (matches the
    /// TextBlock composition; the shaper tolerates the token and the browser
    /// canvas honors it, while Skia measures regular advances for bold in v1).
    /// Bold is OpenType 700+ like the canvas keyword.</summary>
    internal static string ComposeSegmentCss(FontWeight weight, FontStyle style, double size, FontFamily family)
    {
        var bold = weight.CompareTo(FontWeights.Bold) >= 0 ? "bold " : string.Empty;
        var slant = style == FontStyles.Italic ? "italic " : string.Empty;
        var px = size.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{bold}{slant}{px}px {family}";
    }

    FontFamily IDocumentFontSource.InheritedFamily => FontFamily;
    double IDocumentFontSource.InheritedSize => FontSize;
    FontStyle IDocumentFontSource.InheritedStyle => FontStyle;
    FontWeight IDocumentFontSource.InheritedWeight => FontWeight;
    FontStretch IDocumentFontSource.InheritedStretch => FontStretches.Normal;
    Brush? IDocumentFontSource.InheritedForeground => Foreground;
    Brush? IDocumentFontSource.InheritedBackground => Background;

    private Thickness _padding;
    public Thickness Padding { get => _padding; set { if (_padding == value) return; _padding = value; InvalidateMeasure(); } }
    /// <summary>Text run (changes measured size; replaces hosted inlines like WPF;
    /// store-backed so the WPF-identical TextProperty stays the single source
    /// of truth for template, style and binding loads, including CLR-pushed
    /// binding values that readers observe through the DP store).</summary>
    public string Text
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set
        {
            // WPF coerces null to String.Empty (DP null-coercion); a null
            // binding/source must clear the run, never poison the store.
            value ??= string.Empty;
            var current = (string?)GetValue(TextProperty) ?? string.Empty;
            if (current == value && Inlines.Count == 0 && _literals.Count == 0) return;
            Inlines.Clear();
            _literals.Clear();
            SetFoldedText(value);
        }
    }

    private void SetFoldedText(string value)
    {
        var current = (string?)GetValue(TextProperty) ?? string.Empty;
        if (current == value) return;
        SetValue(TextProperty, value);
        InvalidateMeasure();
    }

    /// <summary>Creates a text block with an empty inline collection.</summary>
    public TextBlock()
    {
        Inlines = new InlineCollection(this);
        Inlines.ContentsChanged += RefoldFromInlines;
        Inlines.ContentsChanged += ClearLiteralsWhenEmpty;
    }

    /// <summary>
    /// Inline runs hosted for activation (matches TextBlock.Inlines:
    /// Hyperlink hit/focus; spans fold text-only). Converted controls
    /// (HighlightTextBlock) rebuild runs here on SourceText changes, so the
    /// store is the shared InlineCollection, not a private list. Direct-text
    /// chunks from mixed content stay out of this collection (legacy
    /// IReadOnlyList shape): they ride the positional literal list below in
    /// encounter order, which keeps Count/enumeration owner-only like before.
    /// </summary>
    public InlineCollection Inlines { get; }

    /// <summary>Direct-text chunks in encounter order (loader mixed content).</summary>
    private readonly List<(int At, string Text)> _literals = new();

    /// <summary>
    /// Hosts an inline: keeps the element in the logical children (Parent
    /// links, focus order, routed bubbling) and folds its text into
    /// <see cref="Text"/> in encounter order, preserving the legacy
    /// fold. Later Text edits on the span re-fold automatically through the
    /// collection watch.
    /// </summary>
    internal void AddInline(Span span) => AddInline((Inline)span);

    internal void AddInline(Inline inline)
    {
        ArgumentNullException.ThrowIfNull(inline);
        Inlines.Add(inline);
        SetFoldedText(FoldInlineText());
    }

    /// <summary>Appends a direct-text chunk in encounter order (loader mixed content).</summary>
    internal void AppendLiteral(string chunk)
    {
        _literals.Add((Inlines.Count, chunk));
        SetFoldedText(FoldInlineText());
    }

    /// <summary>Folds owner texts and positional literals in encounter order.</summary>
    private string FoldInlineText()
    {
        var folded = new System.Text.StringBuilder();
        var nextLiteral = 0;
        for (var i = 0; i < Inlines.Count; i++)
        {
            while (nextLiteral < _literals.Count && _literals[nextLiteral].At <= i)
            {
                folded.Append(_literals[nextLiteral].Text);
                nextLiteral++;
            }
            if (Inlines[i] is Span span) folded.Append(span.Text);
        }
        while (nextLiteral < _literals.Count)
        {
            folded.Append(_literals[nextLiteral].Text);
            nextLiteral++;
        }
        return folded.ToString();
    }

    private void RefoldFromInlines() => SetFoldedText(FoldInlineText());

    /// <summary>
    /// Empties the positional literal list when the last owner leaves
    /// (Clear/RemoveAt): literals anchor to owner positions, so an
    /// owner-less block folds empty instead of resurrecting stale chunks.
    /// Converted HighlightTextBlock.RefreshInlines relies on this: its
    /// Clear() must reset the text even though it never sees the literals.
    /// </summary>
    private void ClearLiteralsWhenEmpty()
    {
        if (Inlines.Count == 0 && _literals.Count > 0)
        {
            _literals.Clear();
            SetFoldedText(string.Empty);
        }
    }

    /// <summary>Trims trailing whitespace off the folded text and its tail runs.</summary>
    internal void TrimTrailingLiteralWhitespace()
    {
        var trimmed = Text.TrimEnd();
        var excess = Text.Length - trimmed.Length;
        if (excess <= 0) return;
        for (var i = _literals.Count - 1; i >= 0 && excess > 0; i--)
        {
            var take = Math.Min(excess, _literals[i].Text.Length);
            _literals[i] = (_literals[i].At, _literals[i].Text[..^take]);
            excess -= take;
        }
        for (var i = Inlines.Count - 1; i >= 0 && excess > 0; i--)
        {
            // Literal-like runs trim live (legacy snapshots trimmed the same
            // tail text). Spans with activation (hyperlinks) keep their text:
            // trimming link text would change navigation, not spacing.
            if (Inlines[i] is not Run run || string.IsNullOrEmpty(run.Text)) continue;
            var take = Math.Min(excess, run.Text.Length);
            run.Text = run.Text[..^take];
            excess -= take;
        }
        SetFoldedText(trimmed);
    }

    /// <summary>Text range of a hosted link in TextBlock coordinates (null when absent/empty).</summary>
    internal WpfRect? GetLinkRect(Hyperlink link)
    {
        var prefix = new System.Text.StringBuilder();
        string? linkText = null;

        bool WalkSpan(Span span)
        {
            var nextLit = 0;
            for (var j = 0; j < span.Inlines.Count; j++)
            {
                while (nextLit < span.Literals.Count && span.Literals[nextLit].At <= j)
                {
                    prefix.Append(span.Literals[nextLit++].Text);
                }
                var child = span.Inlines[j];
                if (ReferenceEquals(child, link))
                {
                    linkText = (child as Span)?.Text;
                    return true;
                }
                if (child is Span childSpan)
                {
                    if (WalkSpan(childSpan)) return true;
                }
            }
            while (nextLit < span.Literals.Count)
            {
                prefix.Append(span.Literals[nextLit++].Text);
            }
            return false;
        }

        var nextLiteral = 0;
        for (var i = 0; i < Inlines.Count; i++)
        {
            while (nextLiteral < _literals.Count && _literals[nextLiteral].At <= i)
            {
                prefix.Append(_literals[nextLiteral].Text);
                nextLiteral++;
            }
            var inline = Inlines[i];
            if (ReferenceEquals(inline, link)) { linkText = (inline as Span)?.Text; break; }
            if (inline is Span span)
            {
                if (WalkSpan(span)) break;
            }
        }
        if (string.IsNullOrEmpty(linkText)) return null;
        var (originX, originY) = TextOrigin();
        var (prefixW, _) = TryMeasure(prefix.ToString(), FontCss, prefix.Length * 8, 20);
        var (linkW, linkH) = TryMeasure(linkText, FontCss, linkText.Length * 8, 20);
        return new WpfRect(originX + prefixW, originY, linkW, linkH);
    }

    private uint _foregroundRgba = 0x000000FF;

    /// <summary>Text color (paint-only change).</summary>
    public uint ForegroundRgba
    {
        get => _foregroundRgba;
        set
        {
            if (_foregroundRgba == value) return;
            _foregroundRgba = value;
            InvalidateVisual();
        }
    }

    /// <summary>Ink identity (matches System.Windows.Controls.TextBlock.ForegroundProperty;
    /// the WPF-identical instance, so converted template bindings and style
    /// setters bind; the CLR wrapper stays store-backed with the Rgba sync
    /// so factory-applied values paint; direct store writes sync through the
    /// metadata callback for the same reason).</summary>
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register("Foreground", typeof(Brush), typeof(TextBlock),
            new FrameworkPropertyMetadata(null, (d, e) =>
            {
                if (e.NewValue is SolidColorBrush solid) ((TextBlock)d).ForegroundRgba = solid.ToRgba();
            })
            { AffectsRender = true });

    /// <summary>Text identity (matches System.Windows.Controls.TextBlock.TextProperty;
    /// registered so converted template bindings resolve the Text target
    /// through the DP store. The CLR slot is store-backed with its inline
    /// folding semantics (no metadata callback: folding pushes through the
    /// CLR setter, so a callback would re-enter and wipe the inlines being
    /// folded; direct store writes land verbatim like Control.Foreground).</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register("Text", typeof(string), typeof(TextBlock),
            new FrameworkPropertyMetadata(string.Empty));

    /// <summary>Ink brush (null keeps the default; solid brushes repaint;
    /// store-backed so template/style setters land through the DP store).</summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set { SetValue(ForegroundProperty, value); if (value is SolidColorBrush solid) ForegroundRgba = solid.ToRgba(); InvalidateVisual(); }
    }

    private Brush? _background;

    /// <summary>Fill brush (stored; TextBlock renders no fill in v1).</summary>
    public Brush? Background
    {
        get => _background;
        set { _background = value; InvalidateVisual(); }
    }

    /// <summary>Decorations (matches TextBlock.TextDecorations; painted as ink lines).</summary>
    public static readonly DependencyProperty TextDecorationsProperty =
        DependencyProperty.Register("TextDecorations", typeof(TextDecorationCollection), typeof(TextBlock),
            new FrameworkPropertyMetadata(null));

    private TextDecorationCollection _textDecorations = new();

    /// <summary>Underline/strikethrough/overline/baseline runs (null clears, like WPF).</summary>
    public TextDecorationCollection? TextDecorations
    {
        get => _textDecorations;
        set { _textDecorations = value ?? new TextDecorationCollection(); InvalidateVisual(); }
    }

    private string _fontCss = "14px sans-serif";

    /// <summary>Font description (changes measured size).</summary>
    public string FontCss
    {
        get => _fontCss;
        set
        {
            if (_fontCss == value) return;
            _fontCss = value;
            InvalidateMeasure();
        }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var (shapedW, shapedH) = TryMeasure(Text, FontCss, Text.Length * 8, 20);
        return new(Math.Min(available.Width, Math.Max(40, shapedW) + Padding.Left + Padding.Right), Math.Max(20, shapedH) + Padding.Top + Padding.Bottom);
    }

    /// <summary>Paint segment in encounter order (direct-text literal or inline
    /// text with its resolved style). Hyperlinks ride the main pass in block
    /// style like before; <see cref="PaintLinks"/> still overdraws their ink.</summary>
    private sealed record TextSegment(string Text, string FontCss, uint Ink);

    /// <summary>Builds paint segments in the same encounter order as the
    /// folded text (literals anchored against inline positions, walked recursively).</summary>
    private List<TextSegment> BuildSegments()
    {
        var segments = new List<TextSegment>();
        // Direct-text blocks (Text set with no inlines/literals) paint the
        // legacy single segment; inline content refines into styled pieces.
        if (Inlines.Count == 0 && _literals.Count == 0)
        {
            if (!string.IsNullOrEmpty(Text)) segments.Add(new TextSegment(Text, FontCss, ForegroundRgba));
            return segments;
        }

        void CollectSpan(Span span, uint parentInk)
        {
            var spanInk = span.ResolveInk(parentInk);
            var spanCss = ComposeSegmentCss(span.FontWeight, span.FontStyle, span.FontSize, span.FontFamily);

            if (span.Inlines.Count == 0 && span.Literals.Count == 0)
            {
                if (!string.IsNullOrEmpty(span.Text))
                {
                    if (span is Hyperlink) segments.Add(new TextSegment(span.Text, FontCss, ForegroundRgba));
                    else segments.Add(new TextSegment(span.Text, spanCss, spanInk));
                }
                return;
            }

            var nextLit = 0;
            for (var j = 0; j < span.Inlines.Count; j++)
            {
                while (nextLit < span.Literals.Count && span.Literals[nextLit].At <= j)
                {
                    var lit = span.Literals[nextLit++].Text;
                    if (!string.IsNullOrEmpty(lit)) segments.Add(new TextSegment(lit, spanCss, spanInk));
                }
                if (span.Inlines[j] is Span childSpan)
                {
                    CollectSpan(childSpan, spanInk);
                }
            }
            while (nextLit < span.Literals.Count)
            {
                var lit = span.Literals[nextLit++].Text;
                if (!string.IsNullOrEmpty(lit)) segments.Add(new TextSegment(lit, spanCss, spanInk));
            }
        }

        var nextLiteral = 0;
        for (var i = 0; i < Inlines.Count; i++)
        {
            while (nextLiteral < _literals.Count && _literals[nextLiteral].At <= i)
            {
                var literal = _literals[nextLiteral++].Text;
                if (!string.IsNullOrEmpty(literal)) segments.Add(new TextSegment(literal, FontCss, ForegroundRgba));
            }
            if (Inlines[i] is Span span)
            {
                CollectSpan(span, ForegroundRgba);
            }
        }
        while (nextLiteral < _literals.Count)
        {
            var literal = _literals[nextLiteral++].Text;
            if (!string.IsNullOrEmpty(literal)) segments.Add(new TextSegment(literal, FontCss, ForegroundRgba));
        }
        return segments;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (string.IsNullOrEmpty(Text)) return;
        var (x, y) = TextOrigin();
        // Adjacent same-style segments coalesce, so plain blocks emit the
        // exact legacy single DrawText; only genuinely styled runs split it.
        string? pendingText = null;
        var pendingCss = FontCss;
        var pendingInk = ForegroundRgba;
        var dx = x;
        void Flush()
        {
            if (pendingText is null) return;
            dc.DrawText(pendingText, (float)dx, (float)y, pendingCss, pendingInk, 0, 1, -1);
            var (w, _) = TryMeasure(pendingText, pendingCss, pendingText.Length * 8, 20);
            dx += w;
            pendingText = null;
        }
        foreach (var segment in BuildSegments())
        {
            if (pendingText is not null && pendingCss == segment.FontCss && pendingInk == segment.Ink)
                pendingText += segment.Text;
            else
            {
                Flush();
                pendingText = segment.Text;
                pendingCss = segment.FontCss;
                pendingInk = segment.Ink;
            }
        }
        Flush();
        PaintLinks(dc);
        PaintDecorations(dc, x, y);
    }

    /// <summary>
    /// Paints TextDecorations as ink lines over the text run (matches
    /// TextBlock decoration rendering): underline below the baseline,
    /// strikethrough through the x-height, overline at the cap top and
    /// baseline at the baseline, with pen/offset/unit resolution off the
    /// resolved typeface (FontRecommended falls back to its metrics).
    /// </summary>
    private void PaintDecorations(DrawingContext dc, double x, double y)
    {
        if (_textDecorations.Count == 0 || string.IsNullOrEmpty(Text)) return;
        var face = new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal);
        FormattedText? probe = null;
        double baseline;
        try
        {
            probe = new FormattedText(Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                face, Math.Max(1, FontSize), Brushes.Black);
            baseline = y + probe.Baseline;
        }
        catch (Exception)
        {
            return;
        }
        var (runWidth, runHeight) = TryMeasure(Text, FontCss, Text.Length * 8, 20);
        var width = Math.Min(runWidth, Math.Max(0, RenderRect.Width - Padding.Left - Padding.Right));
        foreach (var decoration in _textDecorations)
        {
            ResolveDecorationGeometry(decoration, probe, runHeight, baseline, y,
                out var lineY, out var thickness);
            var ink = decoration.Pen?.Brush is null ? ForegroundRgba : BrushRender.ResolveFill(decoration.Pen.Brush);
            if (thickness <= 0) thickness = 1;
            dc.DrawLine((float)x, (float)lineY, (float)(x + width), (float)lineY, (float)thickness, ink);
        }
    }

    private static void ResolveDecorationGeometry(TextDecoration decoration, FormattedText probe,
        double runHeight, double baseline, double top, out double lineY, out double thickness)
    {
        var em = Math.Max(1, probe.EmSize);
        var typeface = probe.Typeface;
        var offsetDip = decoration.PenOffsetUnit switch
        {
            TextDecorationUnit.Pixel => decoration.PenOffset,
            TextDecorationUnit.FontRenderingEmSize => decoration.PenOffset * em,
            _ => decoration.PenOffset * em,
        };
        double recommendedThickness = decoration.Location switch
        {
            TextDecorationLocation.Strikethrough => Math.Max(0.5, typeface.StrikethroughThickness * em),
            _ => Math.Max(0.5, typeface.UnderlineThickness * em),
        };
        thickness = decoration.PenThicknessUnit switch
        {
            TextDecorationUnit.Pixel => decoration.Pen?.Thickness ?? recommendedThickness,
            TextDecorationUnit.FontRenderingEmSize => (decoration.Pen?.Thickness ?? 0) switch
            {
                0 => recommendedThickness,
                var t => t * em,
            },
            _ => recommendedThickness,
        };
        lineY = decoration.Location switch
        {
            TextDecorationLocation.Underline => baseline + Math.Max(1, typeface.UnderlinePosition * em) + offsetDip,
            TextDecorationLocation.Strikethrough => baseline - Math.Max(1, typeface.XHeight * em / 2) + offsetDip,
            TextDecorationLocation.OverLine => top + offsetDip,
            _ => baseline + offsetDip,
        };
    }

    private (double X, double Y) TextOrigin()
    {
        var r = RenderRect;
        r = new WpfRect(r.X + Padding.Left, r.Y + Padding.Top,
            Math.Max(0, r.Width - Padding.Left - Padding.Right), Math.Max(0, r.Height - Padding.Top - Padding.Bottom));
        // Text-level alignment inside the arranged box (mirrors the visual
        // result of WPF's box-level HorizontalAlignment + TextAlignment).
        var w = Math.Min(DesiredSize.Width, r.Width);
        var h = Math.Min(DesiredSize.Height, r.Height);
        var x = r.X + HorizontalAlignment switch
        {
            HorizontalAlignment.Center => (r.Width - w) / 2,
            HorizontalAlignment.Right => r.Width - w,
            _ => 0.0,
        };
        var y = r.Y + VerticalAlignment switch
        {
            VerticalAlignment.Center => (r.Height - h) / 2,
            VerticalAlignment.Bottom => r.Height - h,
            _ => 0.0,
        };
        return (x, y);
    }

    /// <summary>Default hyperlink ink (HotTrack blue; a per-link Foreground wins).</summary>
    internal const uint LinkRgba = 0x0066CCFF;

    /// <summary>Link ink: hosted hyperlinks repaint in link color with an underline.</summary>
    private void PaintLinks(DrawingContext dc)
    {
        foreach (var inline in Inlines)
        {
            if (inline is not Hyperlink link || string.IsNullOrEmpty(link.Text)) continue;
            var rect = GetLinkRect(link);
            if (rect is null) continue;
            var ink = link.Foreground is SolidColorBrush solid ? solid.ToRgba() : LinkRgba;
            dc.DrawText(link.Text, (float)rect.Value.X, (float)rect.Value.Y, FontCss, ink, 0, 1, -1);
            dc.DrawLine((float)rect.Value.X, (float)(rect.Value.Y + rect.Value.Height - 2),
                (float)(rect.Value.X + rect.Value.Width), (float)(rect.Value.Y + rect.Value.Height - 2), 1, ink);
            if (link.ShowsFocusRing)
            {
                const uint ring = 0x285B45FF;
                var fx = (float)rect.Value.X - 1;
                var fy = (float)rect.Value.Y - 1;
                var fw = (float)rect.Value.Width + 2;
                var fh = (float)rect.Value.Height + 2;
                dc.DrawRectangle(fx, fy, fw, 1, ring);
                dc.DrawRectangle(fx, fy + fh - 1, fw, 1, ring);
                dc.DrawRectangle(fx, fy + 1, 1, Math.Max(0, fh - 2), ring);
                dc.DrawRectangle(fx + fw - 1, fy + 1, 1, Math.Max(0, fh - 2), ring);
            }
        }
    }
}

/// <summary>
/// Single-line text editor: focusable with caret, basic keyboard editing
/// (type, Backspace/Delete, arrows, Home/End), and TwoWay source push.
/// </summary>
public class TextBox : TextBoxBase
{
    private const int Pad = 4;

    /// <summary>Text identity (matches
    /// System.Windows.Controls.TextBox.Text; registered so bindings
    /// resolve the Text target through the DP store, like
    /// TextBlock.Text. The CLR slot stays store-backed with the existing
    /// setter semantics (no metadata callback: pushes flow through the
    /// CLR setter like the field-backed version did).</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register("Text", typeof(string), typeof(TextBox),
            new FrameworkPropertyMetadata(string.Empty));
    private int _selectionStart;
    private int _selectionLength;
    private bool _focused;

    /// <summary>
    /// Fill identity (matches System.Windows.Controls.TextBox.Background;
    /// the WPF-identical shared Control instance, so converted code-built
    /// templates and setters bind; the TextBox-owned metadata callback
    /// forwards every write path (including DP-store SetValue) to
    /// OnBackgroundChanged so the Rgba paint state stays synced, and the
    /// CLR wrapper stays store-backed so SetValue-applied values land).
    /// </summary>
    public static readonly DependencyProperty BackgroundProperty =
        Control.BackgroundProperty.AddOwner(typeof(TextBox),
            new FrameworkPropertyMetadata(null, OnTextBoxBackgroundChanged));

    private static void OnTextBoxBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox box) box.OnBackgroundChanged((Brush?)e.NewValue);
    }

    /// <summary>Fill brush (store-backed; Transparent and named brushes repaint).</summary>
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set
        {
            if (ReferenceEquals(GetValue(BackgroundProperty), value)) return;
            SetValue(BackgroundProperty, value);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Border thickness identity (matches
    /// System.Windows.Controls.TextBox.BorderThickness; the WPF-identical
    /// shared Control instance, so converted code-built templates and
    /// setters bind; the CLR wrapper stays store-backed with Control's
    /// layout/template side effects, like Background above).
    /// </summary>
    public static readonly DependencyProperty BorderThicknessProperty =
        Control.BorderThicknessProperty.AddOwner(typeof(TextBox));

    /// <summary>Border thickness (store-backed so SetValue-applied values land).</summary>
    public Thickness BorderThickness
    {
        get => (Thickness)(GetValue(BorderThicknessProperty) ?? new Thickness(0));
        set { SetValue(BorderThicknessProperty, value); InvalidateMeasure(); TemplateApplier.RefreshTemplate(this); }
    }

    /// <summary>Wrapping rule (parsed and retained; layout keeps current metrics in v1).</summary>
    public TextWrapping TextWrapping { get; set; } = TextWrapping.NoWrap;

    /// <summary>Edited text (changes measured size; TwoWay bindings push on edits).</summary>
    public string Text
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set
        {
            // WPF coerces null to String.Empty; without it a null template
            // binding poisons the store and SelectionLength throws on
            // Text.Length (gallery ControlExample XamlCode bindings).
            value ??= string.Empty;
            if (string.Equals((string?)GetValue(TextProperty) ?? string.Empty, value, StringComparison.Ordinal)) return;
            SetValue(TextProperty, value);
            SelectionLength = 0;
            CaretIndex = Math.Min(CaretIndex, value.Length);
            InvalidateMeasure();
            // Source push (and validation) first, like a WPF DP change
            // notifying bindings before TextChanged reaches handlers, so
            // handlers already observe the pushed value and error state.
            UpdateSources();
            OnTextChanged(new TextChangedEventArgs(TextChangedEvent, UndoAction.None));
        }
    }

    /// <summary>
    /// Insertion point (the active end of the selection). Setting it collapses
    /// the selection, like real WPF.
    /// </summary>
    public int CaretIndex
    {
        get => SelectionStart + SelectionLength;
        set
        {
            SelectionStart = value;
            SelectionLength = 0;
        }
    }

    /// <summary>Anchor of the selection (clamped to the text).</summary>
    public int SelectionStart
    {
        get => _selectionStart;
        set
        {
            var clamped = Math.Clamp(value, 0, Text.Length);
            if (_selectionStart == clamped) return;
            _selectionStart = clamped;
            SelectionLength = Math.Min(SelectionLength, Text.Length - _selectionStart);
            InvalidateVisual();
        }
    }

    /// <summary>Selected length from the anchor (clamped to the text).</summary>
    public int SelectionLength
    {
        get => _selectionLength;
        set
        {
            var clamped = Math.Clamp(value, 0, Text.Length - SelectionStart);
            if (_selectionLength == clamped) return;
            _selectionLength = clamped;
            InvalidateVisual();
        }
    }

    /// <summary>Selected text (setting it replaces the selection).</summary>
    public string SelectedText
    {
        get => SelectionLength == 0 ? string.Empty : Text.Substring(SelectionStart, SelectionLength);
        set
        {
            if (IsReadOnly) return;
            var start = SelectionStart;
            Text = Text.Remove(start, SelectionLength).Insert(start, value ?? string.Empty);
            SelectionStart = start + (value?.Length ?? 0);
        }
    }

    /// <summary>Selects the whole text (allowed on read-only boxes, like real WPF).</summary>
    public override void SelectAll()
    {
        SelectionStart = 0;
        SelectionLength = Text.Length;
    }

    /// <summary>Selects a text range (clamped to the text through the setters, like real WPF).</summary>
    public override void Select(int start, int length)
    {
        SelectionStart = start;
        SelectionLength = length;
    }

    protected override void OnBackgroundChanged(Brush? brush)
    {
        if (brush is SolidColorBrush solid) BackgroundRgba = solid.ToRgba();
    }

    protected override void OnBorderBrushChanged(Brush? brush)
    {
        if (brush is SolidColorBrush solid) BorderRgba = solid.ToRgba();
    }

    protected override void OnForegroundChanged(Brush? brush)
    {
        if (brush is SolidColorBrush solid) ForegroundRgba = solid.ToRgba();
    }

    protected override void OnFontChanged() => FontCss = ComposeFontCss();

    /// <summary>Whether the caret is currently shown (mirrors the focus gate).</summary>
    public override bool IsFocused => _focused;

    /// <summary>Text editors take focus (store-backed; the Control default is true).</summary>
    public override bool Focusable
    {
        get => (bool?)GetValue(FocusableProperty) ?? true;
        set => SetValue(FocusableProperty, value);
    }

    public override void OnGotFocus()
    {
        _focused = true;
        InvalidateVisual();
    }

    public override void OnLostFocus()
    {
        _focused = false;
        InvalidateVisual();
    }

    private string _fontCss = "14px sans-serif";

    /// <summary>Font description (changes measured size).</summary>
    public string FontCss
    {
        get => _fontCss;
        set
        {
            if (_fontCss == value) return;
            _fontCss = value;
            InvalidateMeasure();
        }
    }

    private uint _foregroundRgba = 0x000000FF;

    /// <summary>Text color (paint-only change).</summary>
    public uint ForegroundRgba
    {
        get => _foregroundRgba;
        set
        {
            if (_foregroundRgba == value) return;
            _foregroundRgba = value;
            InvalidateVisual();
        }
    }

    /// <summary>Field fill (paint-only; set directly).</summary>
    public uint BackgroundRgba { get; set; } = 0xFFFFFFFF;

    /// <summary>1px border color (paint-only; set directly).</summary>
    public uint BorderRgba { get; set; } = 0x707070FF;

    /// <summary>Display lines (multiline display; editing stays first-line).</summary>
    internal string[] DisplayLines => Text.Split('\n');

    protected override WpfSize MeasureCore(WpfSize available)
    {
        // Multiline display (calculator paper trail): widest line sizes the
        // width, stacked line heights size the height. Editing (caret) stays
        // first-line by design; paper-trail boxes never take focus.
        var width = 0.0;
        var height = 0.0;
        foreach (var line in DisplayLines)
        {
            var (shapedW, shapedH) = TryMeasure(line, FontCss, line.Length * 8, 20);
            width = Math.Max(width, shapedW);
            height += shapedH;
        }
        return new(
            double.IsNaN(Width) ? Math.Min(available.Width, Math.Max(40, width + Pad * 2 + 2)) : Width,
            double.IsNaN(Height) ? Math.Max(28, height + Pad * 2 + 2) : Height);
    }

    /// <summary>Validation-error border ink (matches the WPF red adorner).</summary>
    internal const uint ErrorBorderRgba = 0xE81123FF;

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        // Invalid input keeps editing live but paints the error border, the
        // v1 form of WPF's validation adorner (same box, red edge).
        var border = Validation.GetHasError(this) ? ErrorBorderRgba : BorderRgba;
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, border);
        if (r.Width > 2 && r.Height > 2)
            dc.DrawRectangle((float)r.X + 1, (float)r.Y + 1, (float)r.Width - 2, (float)r.Height - 2, BackgroundRgba);
        var y = (float)r.Y + Pad;
        var firstLine = DisplayLines.FirstOrDefault() ?? string.Empty;
        var highlight = SelectionLength > 0 && SelectionStart < firstLine.Length
            ? (Start: SelectionStart, Length: Math.Min(SelectionLength, firstLine.Length - SelectionStart))
            : ((int Start, int Length)?)null;
        if (highlight is { } sel && r.Width > 2 && r.Height > 2)
        {
            var (selX, _) = TryMeasure(firstLine[..sel.Start], FontCss, sel.Start * 8, 20);
            var (selW, selH) = TryMeasure(firstLine.Substring(sel.Start, sel.Length), FontCss, sel.Length * 8, 20);
            dc.DrawRectangle((float)r.X + Pad + (float)selX, (float)r.Y + Pad,
                (float)Math.Max(1, selW), (float)selH, SelectionFillRgba);
        }
        foreach (var line in DisplayLines)
        {
            if (!string.IsNullOrEmpty(line))
                dc.DrawText(line, (float)r.X + Pad, y, FontCss, ForegroundRgba, 0, 1, -1);
            var (_, lineH) = TryMeasure(line, FontCss, line.Length * 8, 20);
            y += (float)lineH;
            if (y > r.Y + r.Height) break;
        }
        if (_focused && SelectionLength == 0 && r.Width > 2 && r.Height > 2)
        {
            // Caret x tracks the shaped width of the text before the caret.
            var (prefixW, lineH) = TryMeasure(Text[..CaretIndex], FontCss, CaretIndex * 8, 20);
            dc.DrawRectangle((float)r.X + Pad + (float)prefixW, (float)r.Y + Pad, 1, (float)lineH, CaretFillRgba);
        }
    }

    /// <summary>Selection fill with SelectionOpacity folded into the alpha channel.</summary>
    internal uint SelectionFillRgba
    {
        get
        {
            var rgba = BrushRender.ResolveFill(SelectionBrush, SystemColors.HighlightBrush.ToRgba());
            var alpha = (byte)(rgba & 0xFF);
            var scaled = (byte)(alpha * Math.Clamp(SelectionOpacity, 0, 1));
            return (rgba & 0xFFFFFF00u) | scaled;
        }
    }

    /// <summary>Caret paint (CaretBrush, else the legacy black caret).</summary>
    internal uint CaretFillRgba =>
        CaretBrush is null ? 0x000000FFu : BrushRender.ResolveFill(CaretBrush, 0x000000FFu);

    /// <summary>Deletes the selection, collapsing the caret to its anchor.</summary>
    private bool DeleteSelection()
    {
        if (SelectionLength == 0) return false;
        Text = Text.Remove(SelectionStart, SelectionLength);
        SelectionStart = Math.Min(SelectionStart, Text.Length);
        return true;
    }

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling) return;
        if (e.Name != WasmRoutedEvent.KeyDownName || !_focused || !IsEffectivelyEnabled) return;
        if (e.Source is not Core.WasmInputEvent.Key k || !k.Args.IsDown) return;
        switch (k.Args.Key)
        {
            // The Text setter clamps an out-of-range caret into the edited
            // text; Backspace additionally steps back onto the deletion point
            // and insertion steps forward past the new char. Selections edit
            // as a unit; read-only boxes navigate and select but never mutate.
            case "Backspace":
                if (IsReadOnly) break;
                if (DeleteSelection()) { e.Handled = true; break; }
                if (CaretIndex > 0) { CaretIndex--; Text = Text.Remove(CaretIndex, 1); e.Handled = true; }
                break;
            case "Delete":
                if (IsReadOnly) break;
                if (DeleteSelection()) { e.Handled = true; break; }
                if (CaretIndex < Text.Length) { Text = Text.Remove(CaretIndex, 1); e.Handled = true; }
                break;
            case "ArrowLeft":
                CaretIndex = SelectionLength > 0 ? SelectionStart : CaretIndex - 1;
                e.Handled = true;
                break;
            case "ArrowRight":
                CaretIndex = SelectionLength > 0 ? SelectionStart + SelectionLength : CaretIndex + 1;
                e.Handled = true;
                break;
            case "Home": CaretIndex = 0; e.Handled = true; break;
            case "End": CaretIndex = Text.Length; e.Handled = true; break;
            default:
                var key = k.Args.Key;
                if (key.Length == 1 && (key == "a" || key == "A") && k.Args.Ctrl && !k.Args.Alt)
                {
                    SelectAll();
                    e.Handled = true;
                }
                else if (key.Length == 1 && !k.Args.Ctrl && !k.Args.Alt && !IsReadOnly)
                {
                    DeleteSelection();
                    Text = Text.Insert(CaretIndex, key);
                    CaretIndex++;
                    e.Handled = true;
                }
                break;
        }
    }
}

/// <summary>
/// Vertical scroller (v1: single content child, wheel/drag/keys, always-on
/// scrollbar gutter; horizontal scrolling and virtualization are out of scope).
/// The child is arranged at full extent height shifted by -VerticalOffset, so
/// the existing RenderRect hit-testing keeps working; rendering clips to
/// the viewport. Changing the offset re-arranges (not re-measures) the child.
/// </summary>
public class ScrollViewer : Control
{
    private const double ScrollbarWidth = 12;
    private const double MinThumbHeight = 20;
    private const double LineStep = 16;

    private double _offset;
    private double _viewportH;
    private double _extentH;
    private double _offsetX;
    private double _viewportW;
    private double _extentW;

    public static readonly RoutedEvent ScrollChangedEvent = EventManager.RegisterRoutedEvent(
        "ScrollChanged", RoutingStrategy.Bubble, typeof(ScrollChangedEventHandler), typeof(ScrollViewer));

    /// <summary>
    /// Raised after the scroll state actually changes: offset moves (wheel,
    /// drag, keys, code) or the extent/viewport transitions during layout.
    /// Converted ScreenToGif handlers (ZoomBox, ImageViewer, ScrollSynchronizer)
    /// subscribe here; matches ScrollViewer.ScrollChanged.
    /// </summary>
    public event ScrollChangedEventHandler ScrollChanged
    {
        add => AddHandler(ScrollChangedEvent, value);
        remove => RemoveHandler(ScrollChangedEvent, value);
    }

    /// <summary>Raises <see cref="ScrollChanged"/> (matches ScrollViewer.OnScrollChanged).</summary>
    protected virtual void OnScrollChanged(ScrollChangedEventArgs e) => RaiseEvent(e);

    /// <summary>Attaches a ScrollChanged handler to any element (matches ScrollViewer.AddScrollChangedHandler).</summary>
    public static void AddScrollChangedHandler(DependencyObject element, ScrollChangedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is UIElement ui) ui.AddHandler(ScrollChangedEvent, handler);
        else throw new NotSupportedException("ScrollChanged handlers attach to UIElement targets in the session.");
    }

    /// <summary>Detaches a ScrollChanged handler (matches ScrollViewer.RemoveScrollChangedHandler).</summary>
    public static void RemoveScrollChangedHandler(DependencyObject element, ScrollChangedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is UIElement ui) ui.RemoveHandler(ScrollChangedEvent, handler);
        else throw new NotSupportedException("ScrollChanged handlers attach to UIElement targets in the session.");
    }

    private readonly struct ScrollSnapshot(double horizontalOffset, double verticalOffset, double extentWidth, double extentHeight, double viewportWidth, double viewportHeight)
    {
        public readonly double HorizontalOffset = horizontalOffset;
        public readonly double VerticalOffset = verticalOffset;
        public readonly double ExtentWidth = extentWidth;
        public readonly double ExtentHeight = extentHeight;
        public readonly double ViewportWidth = viewportWidth;
        public readonly double ViewportHeight = viewportHeight;

        public bool Equals(ScrollSnapshot other) =>
            HorizontalOffset == other.HorizontalOffset && VerticalOffset == other.VerticalOffset &&
            ExtentWidth == other.ExtentWidth && ExtentHeight == other.ExtentHeight &&
            ViewportWidth == other.ViewportWidth && ViewportHeight == other.ViewportHeight;
    }

    /// <summary>Invalidates scroll metrics and triggers measure (matches ScrollViewer.InvalidateScrollInfo).</summary>
    public void InvalidateScrollInfo() => InvalidateMeasure();

    private ScrollSnapshot CaptureScrollState() =>
        new(_offsetX, _offset, _extentW, _extentH, _viewportW, _viewportH);

    /// <summary>
    /// Fires once when the scroll state differs from the captured snapshot
    /// (offset, extent or viewport transition); total no-ops stay silent so
    /// nested ScrollTo* calls from handlers converge instead of recursing.
    /// </summary>
    private void RaiseScrollChangedIfNeeded(ScrollSnapshot before)
    {
        var after = CaptureScrollState();
        if (before.Equals(after)) return;
        var args = new ScrollChangedEventArgs(
            new Vector(after.HorizontalOffset, after.VerticalOffset),
            new Vector(after.HorizontalOffset - before.HorizontalOffset, after.VerticalOffset - before.VerticalOffset),
            new Size(Math.Max(0, after.ExtentWidth), Math.Max(0, after.ExtentHeight)),
            new Vector(after.ExtentWidth - before.ExtentWidth, after.ExtentHeight - before.ExtentHeight),
            new Size(Math.Max(0, after.ViewportWidth), Math.Max(0, after.ViewportHeight)),
            new Vector(after.ViewportWidth - before.ViewportWidth, after.ViewportHeight - before.ViewportHeight))
        {
            RoutedEvent = ScrollChangedEvent,
            Source = this,
        };
        OnScrollChanged(args);
    }

    private Brush? _background;

    /// <summary>Fill brush (paints the inner face instead of white).</summary>
    public Brush? Background
    {
        get => _background;
        set
        {
            if (ReferenceEquals(_background, value)) return;
            _background = value;
            InvalidateVisual();
        }
    }

    private Thickness _padding;

    /// <summary>
    /// Content inset (matches ContentControl.Padding, which ScrollViewer
    /// inherits in WPF). Unset is zero, so existing scroll behavior is
    /// unchanged; gallery sample pages pad their scroll content.
    /// </summary>
    public Thickness Padding
    {
        get => _padding;
        set { if (_padding == value) return; _padding = value; InvalidateMeasure(); }
    }

    private double PadLeft => double.IsNaN(_padding.Left) ? 0 : _padding.Left;
    private double PadTop => double.IsNaN(_padding.Top) ? 0 : _padding.Top;
    private double PadRight => double.IsNaN(_padding.Right) ? 0 : _padding.Right;
    private double PadBottom => double.IsNaN(_padding.Bottom) ? 0 : _padding.Bottom;

    /// <summary>Tab-stop participation (store-backed through KeyboardNavigation like Control; traversal order stays a boundary).</summary>
    /// <summary>Horizontal scrollbar policy (matches ScrollViewer.HorizontalScrollBarVisibilityProperty: attached, Disabled default, inherits to scrollable descendants like WPF).</summary>
    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty =
        DependencyProperty.RegisterAttached("HorizontalScrollBarVisibility", typeof(ScrollBarVisibility), typeof(ScrollViewer),
            new FrameworkPropertyMetadata(ScrollBarVisibility.Disabled, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Vertical scrollbar policy (matches ScrollViewer.VerticalScrollBarVisibilityProperty: attached, Visible default, inherits to scrollable descendants like WPF).</summary>
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty =
        DependencyProperty.RegisterAttached("VerticalScrollBarVisibility", typeof(ScrollBarVisibility), typeof(ScrollViewer),
            new FrameworkPropertyMetadata(ScrollBarVisibility.Visible, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Scrollable-content opt-in (matches ScrollViewer.CanContentScrollProperty: attached, false default, inherits like WPF).</summary>
    public static readonly DependencyProperty CanContentScrollProperty =
        DependencyProperty.RegisterAttached("CanContentScroll", typeof(bool), typeof(ScrollViewer),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Deferred panning opt-in (matches ScrollViewer.IsDeferredScrollingEnabledProperty: attached, false default, inherits like WPF).</summary>
    public static readonly DependencyProperty IsDeferredScrollingEnabledProperty =
        DependencyProperty.RegisterAttached("IsDeferredScrollingEnabled", typeof(bool), typeof(ScrollViewer),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Reads the effective horizontal policy (matches ScrollViewer.GetHorizontalScrollBarVisibility; unset inherits from ancestors, else Disabled).</summary>
    public static ScrollBarVisibility GetHorizontalScrollBarVisibility(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return GetInheritedVisibility(element, HorizontalScrollBarVisibilityProperty, "ScrollViewer.HorizontalScrollBarVisibility", ScrollBarVisibility.Disabled);
    }

    /// <summary>Writes the horizontal policy (matches ScrollViewer.SetHorizontalScrollBarVisibility).</summary>
    public static void SetHorizontalScrollBarVisibility(DependencyObject element, ScrollBarVisibility value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(HorizontalScrollBarVisibilityProperty, value);
        if (element is UIElement ui) ui.SetAttached("ScrollViewer.HorizontalScrollBarVisibility", value);
    }

    /// <summary>Reads the effective vertical policy (matches ScrollViewer.GetVerticalScrollBarVisibility; unset inherits from ancestors, else Visible).</summary>
    public static ScrollBarVisibility GetVerticalScrollBarVisibility(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return GetInheritedVisibility(element, VerticalScrollBarVisibilityProperty, "ScrollViewer.VerticalScrollBarVisibility", ScrollBarVisibility.Visible);
    }

    /// <summary>Writes the vertical policy (matches ScrollViewer.SetVerticalScrollBarVisibility).</summary>
    public static void SetVerticalScrollBarVisibility(DependencyObject element, ScrollBarVisibility value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(VerticalScrollBarVisibilityProperty, value);
        if (element is UIElement ui) ui.SetAttached("ScrollViewer.VerticalScrollBarVisibility", value);
    }

    /// <summary>Reads the effective content-scroll opt-in (matches ScrollViewer.GetCanContentScroll).</summary>
    public static bool GetCanContentScroll(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return GetInheritedBool(element, CanContentScrollProperty, "ScrollViewer.CanContentScroll", false);
    }

    /// <summary>Writes the content-scroll opt-in (matches ScrollViewer.SetCanContentScroll).</summary>
    public static void SetCanContentScroll(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CanContentScrollProperty, value);
        if (element is UIElement ui) ui.SetAttached("ScrollViewer.CanContentScroll", value);
    }

    /// <summary>Reads the effective deferred-panning opt-in (matches ScrollViewer.GetIsDeferredScrollingEnabled).</summary>
    public static bool GetIsDeferredScrollingEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return GetInheritedBool(element, IsDeferredScrollingEnabledProperty, "ScrollViewer.IsDeferredScrollingEnabled", false);
    }

    /// <summary>Writes the deferred-panning opt-in (matches ScrollViewer.SetIsDeferredScrollingEnabled).</summary>
    public static void SetIsDeferredScrollingEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsDeferredScrollingEnabledProperty, value);
        if (element is UIElement ui) ui.SetAttached("ScrollViewer.IsDeferredScrollingEnabled", value);
    }

    /// <summary>Touch-panning axis policy (matches ScrollViewer.PanningModeProperty: attached, None default, no inherit flag like WPF).</summary>
    public static readonly DependencyProperty PanningModeProperty =
        DependencyProperty.RegisterAttached("PanningMode", typeof(PanningMode), typeof(ScrollViewer),
            new PropertyMetadata(PanningMode.None));

    /// <summary>Reads the panning policy (matches ScrollViewer.GetPanningMode; local value only, None default).</summary>
    public static PanningMode GetPanningMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement ui && ui.GetAttached("ScrollViewer.PanningMode") is PanningMode fromBag) return fromBag;
        if (element is UIElement uiBag && uiBag.GetAttached("ScrollViewer.PanningMode") is string bagText &&
            Enum.TryParse<PanningMode>(bagText, ignoreCase: true, out var bagParsed)) return bagParsed;
        if (element.HasLocalValue(PanningModeProperty) && element.GetValue(PanningModeProperty) is PanningMode local) return local;
        return PanningMode.None;
    }

    /// <summary>Writes the panning policy (matches ScrollViewer.SetPanningMode; retained-only, no touch physics behind it).</summary>
    public static void SetPanningMode(DependencyObject element, PanningMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PanningModeProperty, value);
        if (element is UIElement ui) ui.SetAttached("ScrollViewer.PanningMode", value);
    }

    /// <summary>
    /// Touch-inertia opt-in (HandyControl.Controls.ScrollViewer.IsInertiaEnabled
    /// compat for converted HandyControlDemo XAML: attached, false default, no
    /// inherit flag like the converted registration; retained-only, no
    /// touch-inertia physics behind it).
    /// </summary>
    public static readonly DependencyProperty IsInertiaEnabledProperty =
        DependencyProperty.RegisterAttached("IsInertiaEnabled", typeof(bool), typeof(ScrollViewer),
            new PropertyMetadata(false));

    /// <summary>Reads the inertia opt-in (local value only, false default).</summary>
    public static bool GetIsInertiaEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement ui && ui.GetAttached("ScrollViewer.IsInertiaEnabled") is bool fromBag) return fromBag;
        if (element is UIElement uiBag && uiBag.GetAttached("ScrollViewer.IsInertiaEnabled") is string bagText &&
            bool.TryParse(bagText, out var bagParsed)) return bagParsed;
        if (element.HasLocalValue(IsInertiaEnabledProperty) && element.GetValue(IsInertiaEnabledProperty) is bool local) return local;
        return false;
    }

    /// <summary>Writes the inertia opt-in (retained; drives no physics).</summary>
    public static void SetIsInertiaEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsInertiaEnabledProperty, value);
        if (element is UIElement ui) ui.SetAttached("ScrollViewer.IsInertiaEnabled", value);
    }

    /// <summary>
    /// Direct-attribute inertia opt-in (matches the converted
    /// HandyControl.Controls.ScrollViewer instance surface, which carries
    /// get_/set_IsInertiaEnabled over the same attached registration; the
    /// staged BorderDemo view sets it directly on its hc:ScrollViewer, which
    /// resolves to this engine type). Retained-only, like the static pair.
    /// </summary>
    public bool IsInertiaEnabled
    {
        get => GetIsInertiaEnabled(this);
        set => SetIsInertiaEnabled(this, value);
    }

    /// <summary>
    /// Effective-value walk for an inheritable visibility (local DP value,
    /// then the XAML attached bag, then ancestors; the engine keeps no
    /// inherited store, so inheritance resolves on read like WPF).
    /// </summary>
    private static ScrollBarVisibility GetInheritedVisibility(DependencyObject? node, DependencyProperty dp, string attachedName, ScrollBarVisibility fallback)
    {
        for (; node is UIElement ui; node = ui.Parent)
        {
            if (ui.GetAttached(attachedName) is ScrollBarVisibility fromBag) return fromBag;
            if (ui.GetAttached(attachedName) is string bagText &&
                Enum.TryParse<ScrollBarVisibility>(bagText, ignoreCase: true, out var bagParsed)) return bagParsed;
            if (ui.HasLocalValue(dp) && ui.GetValue(dp) is ScrollBarVisibility local) return local;
        }
        return fallback;
    }

    /// <summary>Effective-value walk for an inheritable flag (same shape as the visibility walk).</summary>
    private static bool GetInheritedBool(DependencyObject? node, DependencyProperty dp, string attachedName, bool fallback)
    {
        for (; node is UIElement ui; node = ui.Parent)
        {
            if (ui.GetAttached(attachedName) is bool fromBag) return fromBag;
            if (ui.GetAttached(attachedName) is string bagText &&
                bool.TryParse(bagText, out var bagParsed)) return bagParsed;
            if (ui.HasLocalValue(dp) && ui.GetValue(dp) is bool local) return local;
        }
        return fallback;
    }

    /// <summary>Horizontal scrollbar policy (parsed and retained; the gutter is always reserved).</summary>
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => GetHorizontalScrollBarVisibility(this);
        set => SetHorizontalScrollBarVisibility(this, value);
    }

    /// <summary>Vertical scrollbar policy (parsed and retained; the gutter is always reserved).</summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => GetVerticalScrollBarVisibility(this);
        set => SetVerticalScrollBarVisibility(this, value);
    }

    /// <summary>Scrollable-content opt-in (parsed and retained; logical scrolling stays a boundary).</summary>
    public bool CanContentScroll
    {
        get => GetCanContentScroll(this);
        set => SetCanContentScroll(this, value);
    }

    /// <summary>Deferred panning opt-in (parsed and retained; panning stays a boundary).</summary>
    public bool IsDeferredScrollingEnabled
    {
        get => GetIsDeferredScrollingEnabled(this);
        set => SetIsDeferredScrollingEnabled(this, value);
    }

    /// <summary>Viewport height from the last arrange.</summary>
    public double ViewportHeight => _viewportH;

    /// <summary>Content extent height from the last measure.</summary>
    public double ExtentHeight => _extentH;

    /// <summary>Scrollable range (extent minus viewport, never negative).</summary>
    public double ScrollableHeight => Math.Max(0, _extentH - _viewportH);

    /// <summary>Vertical scroll position, clamped to the scrollable range.</summary>
    public double VerticalOffset
    {
        get => _offset;
        set
        {
            var clamped = Math.Clamp(value, 0, ScrollableHeight);
            if (_offset == clamped) return;
            var before = CaptureScrollState();
            _offset = clamped;
            var scrollInfo = CanContentScroll ? ScrollInfo : null;
            if (scrollInfo is not null)
            {
                scrollInfo.SetVerticalOffset(clamped);
                _offset = scrollInfo.VerticalOffset;
            }
            else
            {
                Reposition();
            }
            RaiseScrollChangedIfNeeded(before);
        }
    }

    public void ScrollToTop() => VerticalOffset = 0;
    public void ScrollToBottom() => VerticalOffset = ScrollableHeight;

    /// <summary>Scrolls to an absolute vertical offset (matches ScrollViewer.ScrollToVerticalOffset).</summary>
    public void ScrollToVerticalOffset(double offset) => VerticalOffset = offset;

    /// <summary>Viewport width from the last arrange.</summary>
    public double ViewportWidth => _viewportW;

    /// <summary>Content extent width from the last measure.</summary>
    public double ExtentWidth => _extentW;

    /// <summary>Horizontal scrollable range (extent minus viewport, never negative).</summary>
    public double ScrollableWidth => CanScrollHorizontally ? Math.Max(0, _extentW - _viewportW) : 0;

    private bool CanScrollHorizontally => HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;

    /// <summary>Horizontal scroll position, clamped to the scrollable range.</summary>
    public double HorizontalOffset
    {
        get => _offsetX;
        set
        {
            var clamped = Math.Clamp(value, 0, ScrollableWidth);
            if (_offsetX == clamped) return;
            var before = CaptureScrollState();
            _offsetX = clamped;
            var scrollInfo = CanContentScroll ? ScrollInfo : null;
            if (scrollInfo is not null)
            {
                scrollInfo.SetHorizontalOffset(clamped);
                _offsetX = scrollInfo.HorizontalOffset;
            }
            else
            {
                Reposition();
            }
            RaiseScrollChangedIfNeeded(before);
        }
    }

    /// <summary>Scrolls to an absolute horizontal offset (matches ScrollViewer.ScrollToHorizontalOffset).</summary>
    public void ScrollToHorizontalOffset(double offset) => HorizontalOffset = offset;
    public void LineUp()
    {
        var scrollInfo = CanContentScroll ? ScrollInfo : null;
        if (scrollInfo is not null) scrollInfo.LineUp();
        else VerticalOffset -= LineStep;
    }
    public void LineDown()
    {
        var scrollInfo = CanContentScroll ? ScrollInfo : null;
        if (scrollInfo is not null) scrollInfo.LineDown();
        else VerticalOffset += LineStep;
    }
    public void PageUp()
    {
        var scrollInfo = CanContentScroll ? ScrollInfo : null;
        if (scrollInfo is not null) scrollInfo.PageUp();
        else VerticalOffset -= Math.Max(LineStep, _viewportH);
    }
    public void PageDown()
    {
        var scrollInfo = CanContentScroll ? ScrollInfo : null;
        if (scrollInfo is not null) scrollInfo.PageDown();
        else VerticalOffset += Math.Max(LineStep, _viewportH);
    }

    private IScrollInfo? ScrollInfo
    {
        get
        {
            if (Content is IScrollInfo si) return si;
            if (Content is ItemsControl ic && ic.ItemsHost is IScrollInfo icsi) return icsi;
            if (Content is Panel p)
            {
                foreach (UIElement child in p.Children)
                    if (child is IScrollInfo csi) return csi;
            }
            return null;
        }
    }

    public UIElement? Content
    {
        // Logical content only: the applied template root is chrome, not
        // content (otherwise content presenters would mirror and host their
        // own template root, like the nested DataGrid scroll viewer).
        get
        {
            foreach (var child in Children)
                if (!ReferenceEquals(child, TemplateRoot))
                    return child;
            return null;
        }
        set
        {
            var old = Content;
            if (ReferenceEquals(old, value)) return;
            if (old is not null) RemoveChild(old);
            if (value is not null) AddChild(value);
        }
    }

    /// <summary>
    /// Template token (matches ContentControl.ContentTemplateProperty, which
    /// ScrollViewer shares in WPF by inheritance; AddOwner keeps the one DP
    /// instance so template transfers read the templated parent).
    /// </summary>
    public static readonly DependencyProperty ContentTemplateProperty =
        ContentControl.ContentTemplateProperty.AddOwner(typeof(ScrollViewer));

    /// <summary>Fixed visual factory for the content (matches ScrollViewer.ContentTemplate).</summary>
    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    /// <summary>
    /// Template-selector token (matches
    /// ContentControl.ContentTemplateSelectorProperty, shared like WPF).
    /// </summary>
    public static readonly DependencyProperty ContentTemplateSelectorProperty =
        ContentControl.ContentTemplateSelectorProperty.AddOwner(typeof(ScrollViewer));

    /// <summary>Per-content visual choice (matches ScrollViewer.ContentTemplateSelector).</summary>
    public DataTemplateSelector? ContentTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ContentTemplateSelectorProperty);
        set => SetValue(ContentTemplateSelectorProperty, value);
    }

    /// <summary>
    /// String-format token (matches
    /// ContentControl.ContentStringFormatProperty, shared like WPF).
    /// </summary>
    public static readonly DependencyProperty ContentStringFormatProperty =
        ContentControl.ContentStringFormatProperty.AddOwner(typeof(ScrollViewer));

    /// <summary>Composite format for string content (matches ScrollViewer.ContentStringFormat).</summary>
    public string? ContentStringFormat
    {
        get => (string?)GetValue(ContentStringFormatProperty);
        set => SetValue(ContentStringFormatProperty, value);
    }

    public override bool Focusable { get; set; } = true;

    private static readonly DependencyPropertyKey ComputedVerticalScrollBarVisibilityPropertyKey =
        DependencyProperty.RegisterReadOnly("ComputedVerticalScrollBarVisibility", typeof(Visibility), typeof(ScrollViewer),
            new FrameworkPropertyMetadata(Visibility.Visible));

    /// <summary>Vertical bar visibility token (matches
    /// ScrollViewer.ComputedVerticalScrollBarVisibilityProperty; read-only,
    /// Visible default, like WPF).</summary>
    public static readonly DependencyProperty ComputedVerticalScrollBarVisibilityProperty =
        ComputedVerticalScrollBarVisibilityPropertyKey.DependencyProperty;

    /// <summary>Effective vertical bar visibility (Visible sticks, Auto
    /// follows overflow, anything else collapses, live-verified).</summary>
    public Visibility ComputedVerticalScrollBarVisibility =>
        ComputeScrollBarVisibility(GetVerticalScrollBarVisibility(this), ScrollableHeight);

    private static readonly DependencyPropertyKey ComputedHorizontalScrollBarVisibilityPropertyKey =
        DependencyProperty.RegisterReadOnly("ComputedHorizontalScrollBarVisibility", typeof(Visibility), typeof(ScrollViewer),
            new FrameworkPropertyMetadata(Visibility.Visible));

    /// <summary>Horizontal bar visibility token (matches
    /// ScrollViewer.ComputedHorizontalScrollBarVisibilityProperty; read-only,
    /// Visible default, like WPF).</summary>
    public static readonly DependencyProperty ComputedHorizontalScrollBarVisibilityProperty =
        ComputedHorizontalScrollBarVisibilityPropertyKey.DependencyProperty;

    /// <summary>Effective horizontal bar visibility (Visible sticks, Auto
    /// follows overflow, anything else collapses, live-verified).</summary>
    public Visibility ComputedHorizontalScrollBarVisibility =>
        ComputeScrollBarVisibility(GetHorizontalScrollBarVisibility(this), ScrollableWidth);

    private static Visibility ComputeScrollBarVisibility(ScrollBarVisibility setting, double scrollable) =>
        setting == ScrollBarVisibility.Visible || (setting == ScrollBarVisibility.Auto && scrollable > 0)
            ? Visibility.Visible
            : Visibility.Collapsed;

    protected override WpfSize MeasureCore(WpfSize available)
    {
        // A theme template owns the visuals (the DataGrid scroll chrome);
        // the custom scroll math only measures template-less viewers.
        if (TemplateRoot is not null) return base.MeasureCore(available);
        var before = CaptureScrollState();
        // Gutter is always reserved (ScrollBarVisibility.Visible semantics):
        // deterministic layout, no shift when content grows past the viewport.
        var innerW = Math.Max(0, (double.IsNaN(Width) ? available.Width : Width) - 2 - ScrollbarWidth - PadLeft - PadRight);
        var scrollInfo = CanContentScroll ? ScrollInfo : null;
        if (scrollInfo is not null && Content is not null)
        {
            scrollInfo.ScrollOwner = this;
            scrollInfo.CanVerticallyScroll = VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;
            scrollInfo.CanHorizontallyScroll = HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;

            var m = GetMargin(Content);
            var measureW = CanScrollHorizontally ? double.PositiveInfinity : Math.Max(0, innerW - m.Left - m.Right);
            var viewportAvailH = double.IsNaN(Height) ? available.Height : Height;
            var measureH = double.IsPositiveInfinity(viewportAvailH)
                ? (double.IsNaN(Height) ? 400.0 : Height)
                : Math.Max(0, viewportAvailH - 2 - PadTop - PadBottom - m.Top - m.Bottom);

            Content.Measure(new WpfSize(measureW, measureH));
            _extentH = scrollInfo.ExtentHeight;
            _extentW = scrollInfo.ExtentWidth;
            _viewportH = scrollInfo.ViewportHeight;
            _viewportW = scrollInfo.ViewportWidth;
            _offset = scrollInfo.VerticalOffset;
            _offsetX = scrollInfo.HorizontalOffset;
            var w = double.IsNaN(Width) ? Math.Min(available.Width, innerW + 2 + ScrollbarWidth + PadLeft + PadRight) : Width;
            var h = double.IsNaN(Height) ? Math.Min(available.Height, _extentH + 2 + PadTop + PadBottom) : Height;
            RaiseScrollChangedIfNeeded(before);
            return new(w, h);
        }
        if (Content is not null)
        {
            var m = GetMargin(Content);
            var measureW = CanScrollHorizontally ? double.PositiveInfinity : Math.Max(0, innerW - m.Left - m.Right);
            Content.Measure(new WpfSize(measureW, double.PositiveInfinity));
            _extentH = Content.DesiredSize.Height + m.Top + m.Bottom;
            _extentW = Content.DesiredSize.Width + m.Left + m.Right;
        }
        else { _extentH = 0; _extentW = 0; }
        // Shrinking content clamps a stale offset (arrange repositions).
        _offset = Math.Clamp(_offset, 0, ScrollableHeight);
        _offsetX = Math.Clamp(_offsetX, 0, ScrollableWidth);
        var w2 = double.IsNaN(Width) ? Math.Min(available.Width, innerW + 2 + ScrollbarWidth + PadLeft + PadRight) : Width;
        var h2 = double.IsNaN(Height) ? Math.Min(available.Height, _extentH + 2 + PadTop + PadBottom) : Height;
        RaiseScrollChangedIfNeeded(before);
        return new(w2, h2);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (TemplateRoot is not null) { base.ArrangeCore(finalRect); return; }
        var before = CaptureScrollState();
        _viewportH = Math.Max(0, finalRect.Height - 2);
        _viewportW = Math.Max(0, finalRect.Width - 2);
        _offset = Math.Clamp(_offset, 0, ScrollableHeight);
        _offsetX = Math.Clamp(_offsetX, 0, ScrollableWidth);
        var scrollInfo = CanContentScroll ? ScrollInfo : null;
        if (scrollInfo is not null && Content is not null)
        {
            var m = GetMargin(Content);
            Content.Arrange(new WpfRect(
                finalRect.X + 1 + m.Left + PadLeft,
                finalRect.Y + 1 + m.Top + PadTop,
                Math.Max(0, finalRect.Width - 2 - ScrollbarWidth - m.Left - m.Right - PadLeft - PadRight),
                Math.Max(0, finalRect.Height - 2 - PadTop - PadBottom - m.Top - m.Bottom)));
            InvalidateVisual();
            RaiseScrollChangedIfNeeded(before);
            return;
        }
        Reposition();
        RaiseScrollChangedIfNeeded(before);
    }

    private void Reposition()
    {
        // Reuses the last arrange rect (offset never changes sizes).
        if (_viewportH <= 0 || Content is null) { InvalidateVisual(); return; }
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        var m = GetMargin(Content);
        Content.Arrange(new WpfRect(
            r.X + 1 + m.Left + PadLeft - _offsetX, r.Y + 1 + m.Top + PadTop - _offset,
            Math.Max(0, r.Width - 2 - ScrollbarWidth - m.Left - m.Right - PadLeft - PadRight),
            Math.Max(0, _extentH - m.Top - m.Bottom)));
        InvalidateVisual();
    }

    private static Thickness GetMargin(UIElement c) =>
        c is FrameworkElement fe ? fe.Margin : new Thickness(0);

    private WpfRect TrackRect()
    {
        var r = RenderRect;
        return new(r.X + r.Width - 1 - ScrollbarWidth, r.Y + 1, ScrollbarWidth, Math.Max(0, r.Height - 2));
    }

    private WpfRect ThumbRect()
    {
        var track = TrackRect();
        if (_extentH <= 0 || ScrollableHeight <= 0) return new(track.X, track.Y, track.Width, track.Height);
        var h = Math.Max(MinThumbHeight, track.Height * _viewportH / _extentH);
        var y = track.Y + (track.Height - h) * (_offset / ScrollableHeight);
        return new(track.X, y, track.Width, h);
    }

    private bool _dragging;
    private double _grabOffsetY;

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        if (e.Source is not Core.WasmInputEvent ev) return;
        switch (ev)
        {
            case Core.WasmInputEvent.Pointer pd when pd.Args.Kind == Core.WasmPointerKind.Down && e.IsLeftButtonDown:
            {
                var thumb = ThumbRect();
                var p = new WpfPoint(pd.Args.X, pd.Args.Y);
                if (p.X >= thumb.X && p.X < thumb.X + thumb.Width && p.Y >= thumb.Y && p.Y < thumb.Y + thumb.Height)
                {
                    _dragging = true;
                    _grabOffsetY = p.Y - thumb.Y;
                    e.Handled = true;
                }
                break;
            }
            case Core.WasmInputEvent.Pointer pm when pm.Args.Kind == Core.WasmPointerKind.Move:
                if (_dragging)
                {
                    var track = TrackRect();
                    var thumbH = ThumbRect().Height;
                    var y = pm.Args.Y - _grabOffsetY;
                    var range = Math.Max(1, track.Height - thumbH);
                    VerticalOffset = (y - track.Y) / range * ScrollableHeight;
                    e.Handled = true;
                }
                break;
            case Core.WasmInputEvent.Pointer pu when pu.Args.Kind is Core.WasmPointerKind.Up or Core.WasmPointerKind.Cancel:
                _dragging = false;
                break;
            case Core.WasmInputEvent.Key k when k.Args.IsDown && _focused:
                switch (k.Args.Key)
                {
                    case "ArrowUp": LineUp(); e.Handled = true; break;
                    case "ArrowDown": LineDown(); e.Handled = true; break;
                    case "PageUp": PageUp(); e.Handled = true; break;
                    case "PageDown": PageDown(); e.Handled = true; break;
                    case "Home": ScrollToTop(); e.Handled = true; break;
                    case "End": ScrollToBottom(); e.Handled = true; break;
                }
                break;
            case Core.WasmInputEvent.Wheel w:
                VerticalOffset += w.Args.DeltaY;
                e.Handled = true;
                break;
        }
    }

    private bool _focused;
    public override void OnGotFocus() { _focused = true; InvalidateVisual(); }
    public override void OnLostFocus() { _focused = false; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        // A theme template owns the visuals (the DataGrid scroll chrome);
        // the gray ring and scrollbar chrome only paint template-less viewers.
        if (TemplateRoot is not null) { base.OnRender(dc); return; }
        var r = RenderRect;
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, 0x707070FF);
        if (r.Width > 2 && r.Height > 2)
            dc.DrawRectangle((float)r.X + 1, (float)r.Y + 1, (float)r.Width - 2, (float)r.Height - 2,
                _background is null ? 0xFFFFFFFF : BrushRender.ResolveFill(_background, 0xFFFFFFFF));
        // Viewport clip (content gutter excluded): oversized content paints inside only.
        var vx = (float)r.X + 1 + (float)PadLeft; var vy = (float)r.Y + 1 + (float)PadTop;
        var vw = (float)Math.Max(0, r.Width - 2 - ScrollbarWidth - PadLeft - PadRight);
        var vh = (float)Math.Max(0, r.Height - 2 - PadTop - PadBottom);
        dc.PushClip(vx, vy, vw, vh);
        RenderVisible(dc, Content);
        dc.PopClip();
        // Scrollbar track + thumb (outside the clip).
        var track = TrackRect();
        dc.DrawRectangle((float)track.X, (float)track.Y, (float)track.Width, (float)track.Height, 0xEEEEEEFF);
        if (ScrollableHeight > 0)
        {
            var thumb = ThumbRect();
            var thumbRgba = _dragging ? 0x707070FFu : 0xA0A0A0FFu;
            dc.DrawRectangle((float)thumb.X, (float)thumb.Y, (float)thumb.Width, (float)thumb.Height, thumbRgba);
        }
    }
}

/// <summary>
/// ListBoxItem: single selectable row (matches System.Windows.Controls.ListBoxItem).
/// A Control like its sibling item containers (ListViewItem, ComboBoxItem,
/// TreeViewItem), so the Background/BorderBrush/BorderThickness/Template style
/// setters resolve through the inherited Control dependency properties; the
/// row text stays a plain string and the parent <see cref="ListBox"/> paints
/// the selection highlight behind the item while the item itself only
/// switches its text ink to white while selected (set by the parent, never
/// by user code). A null Background paints nothing so the parent highlight
/// shows through; a set Background fills the row like the template Border.
/// </summary>
public sealed class ListBoxItem : Control, IRowTemplateTarget
{
    static ListBoxItem()
    {
        // Rows are not tab stops (WPF tab order walks past item rows;
        // arrows, pointer press and programmatic focus still land on
        // them). Without this, Tab sinks into every generated row and
        // never reaches the controls after a grouped list.
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(ListBoxItem), new FrameworkPropertyMetadata(false));
    }

    private string _content = string.Empty;

    /// <summary>Row text (changes measured size).</summary>
    public string Content
    {
        get => _content;
        set
        {
            if (_content == value) return;
            _content = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Template token (matches System.Windows.Controls.ContentControl.ContentTemplateProperty;
    /// the row shell keeps the templated-parent contract so item container
    /// templates bind it like WPF).</summary>
    public static readonly DependencyProperty ContentTemplateProperty =
        DependencyProperty.Register("ContentTemplate", typeof(DataTemplate), typeof(ListBoxItem),
            new FrameworkPropertyMetadata(null));

    /// <summary>
    /// Fixed visual factory for the row content (matches
    /// System.Windows.Controls.ContentControl.ContentTemplate on ListBoxItem;
    /// template parts bind it through TemplateBinding, like the
    /// MaterialDesign drawer item presenter).
    /// </summary>
    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    /// <summary>Template-selector token (matches System.Windows.Controls.ContentControl.ContentTemplateSelectorProperty).</summary>
    public static readonly DependencyProperty ContentTemplateSelectorProperty =
        DependencyProperty.Register("ContentTemplateSelector", typeof(DataTemplateSelector), typeof(ListBoxItem),
            new FrameworkPropertyMetadata(null));

    /// <summary>
    /// Per-content visual choice (matches
    /// System.Windows.Controls.ContentControl.ContentTemplateSelector on
    /// ListBoxItem; consulted when <see cref="ContentTemplate"/> is null).
    /// </summary>
    public DataTemplateSelector? ContentTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ContentTemplateSelectorProperty);
        set => SetValue(ContentTemplateSelectorProperty, value);
    }

    private uint _foregroundRgba = 0x000000FF;

    /// <summary>Text ink for the unselected state (paint-only change).</summary>
    public uint ForegroundRgba
    {
        get => _foregroundRgba;
        set
        {
            if (_foregroundRgba == value) return;
            _foregroundRgba = value;
            InvalidateVisual();
        }
    }

    private string _fontCss = "14px sans-serif";

    /// <summary>Row font (changes measured size).</summary>
    public string FontCss
    {
        get => _fontCss;
        set
        {
            if (_fontCss == value) return;
            _fontCss = value;
            InvalidateMeasure();
        }
    }

    private uint? _backgroundRgba;

    private uint? _borderRgba;

    /// <summary>Syncs the fill cache from the inherited Background (null stays transparent).</summary>
    protected override void OnBackgroundChanged(Brush? brush)
    {
        _backgroundRgba = brush is SolidColorBrush solid ? solid.ToRgba() : null;
    }

    /// <summary>Syncs the edge cache from the inherited BorderBrush (null paints no ring).</summary>
    protected override void OnBorderBrushChanged(Brush? brush)
    {
        _borderRgba = brush is SolidColorBrush solid ? solid.ToRgba() : null;
    }

    /// <summary>Syncs the text ink from the inherited Foreground.</summary>
    protected override void OnForegroundChanged(Brush? brush)
    {
        if (brush is SolidColorBrush solid) ForegroundRgba = solid.ToRgba();
    }

    /// <summary>Row selection flag (matches
    /// System.Windows.Controls.Primitives.Selector.IsSelected on ListBoxItem;
    /// converted MaterialDesign row-click handlers reference this field, and
    /// style trigger conditions (Trigger Property="IsSelected") resolve
    /// through it like WPF). The owner pushes the effective selection into
    /// the store (see ListBox.PushRowSelection), so the flag can never go
    /// stale when rows are added or removed around it; writes route back
    /// through the parent's SelectedItem. Rows parented into a converted
    /// items-panel host (see Slice111HandyPanels) resolve their owner up the
    /// ancestor chain, like grouped rows parented into GroupItem containers;
    /// the nearest ListBox ancestor always wins, so nested lists keep
    /// routing to the inner owner.</summary>
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register("IsSelected", typeof(bool), typeof(ListBoxItem),
            new FrameworkPropertyMetadata(false, (d, e) => ((ListBoxItem)d).OnIsSelectedChanged(e)));

    private bool _syncingSelection;

    private void OnIsSelectedChanged(DependencyPropertyChangedEventArgs e)
    {
        if (_syncingSelection) return;
        var owner = OwningListBox;
        if (owner is null)
        {
            // Orphan rows hold no selection (Slice68): without an owner the
            // flag cannot route anywhere, so a direct set coerces back to
            // false instead of lingering true where triggers would read it.
            if (e.NewValue is true)
            {
                _syncingSelection = true;
                try { SetValue(IsSelectedProperty, false); }
                finally { _syncingSelection = false; }
            }
            return;
        }
        if (e.NewValue is true)
        {
            if (!ReferenceEquals(owner.SelectedItem, this)) owner.SelectedItem = this;
        }
        else if (ReferenceEquals(owner.SelectedItem, this))
        {
            owner.SelectedItem = null;
        }
    }

    public bool IsSelected
    {
        get => (bool?)GetValue(IsSelectedProperty) ?? false;
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Row focus feeds the owner's keyboard gate (native lists keep arrow
    /// navigation live when a row holds focus; the press focuses the row,
    /// not the list, so the list must hear about it).
    /// </summary>
    public override void OnGotFocus() => OwningListBox?.NoteRowFocus(true);

    /// <summary>Clears the owner's row-focus flag (focus left the rows).</summary>
    public override void OnLostFocus() => OwningListBox?.NoteRowFocus(false);

    /// <summary>Owner-driven selection sync (skips the route-back callback).</summary>
    internal void SetSelectedFromOwner(bool value)
    {
        if ((bool?)GetValue(IsSelectedProperty) == value) return;
        _syncingSelection = true;
        try { SetValue(IsSelectedProperty, value); }
        finally { _syncingSelection = false; }
    }

    /// <summary>
    /// Nearest owning ListBox above this row (direct parent normally; the
    /// converted host panel or a GroupItem container in between).
    /// </summary>
    private ListBox? OwningListBox
    {
        get
        {
            for (FrameworkElement? node = Parent as FrameworkElement; node is not null; node = node.Parent as FrameworkElement)
                if (node is ListBox owner) return owner;
            return null;
        }
    }

    private UIElement? _templatedContent;

    /// <summary>
    /// Templated row visual (matches the ItemTemplate-inflated content WPF hosts
    /// in a generated ListBoxItem): measured/arranged inside the ring and painted
    /// instead of the ToString text, with the row chrome and the parent selection
    /// highlight unchanged.
    /// </summary>
    internal bool HasTemplatedContent => _templatedContent is not null;

    /// <summary>Inflated row visual (null without ItemTemplate).</summary>
    internal UIElement? TemplatedContent => _templatedContent;

    /// <summary>Hosts ItemTemplate-inflated content for a generated data row.</summary>
    internal void SetTemplatedContent(UIElement visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        if (ReferenceEquals(_templatedContent, visual)) return;
        if (_templatedContent is not null) DetachVisualChild(_templatedContent);
        _templatedContent = visual;
        AttachVisualChild(visual);
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Detaches the template-inflated visual (template removed or row recycled to text).</summary>
    internal void ClearTemplatedContent()
    {
        if (_templatedContent is null) return;
        DetachVisualChild(_templatedContent);
        _templatedContent = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var border = BorderThickness;
        if (_templatedContent is not null)
        {
            // The parent applies the templated visual's margin (WPF slot
            // semantics, e.g. the staged CardDemo Card Margin="8" pitch).
            var tm = _templatedContent is FrameworkElement marginSource ? marginSource.Margin : new Thickness(0);
            _templatedContent.Measure(new WpfSize(
                Math.Max(0, available.Width - border.Left - border.Right - tm.Left - tm.Right),
                Math.Max(0, available.Height - border.Top - border.Bottom - tm.Top - tm.Bottom)));
            var want = _templatedContent.DesiredSize;
            return new WpfSize(want.Width + tm.Left + tm.Right + border.Left + border.Right, want.Height + tm.Top + tm.Bottom + border.Top + border.Bottom);
        }
        var (shapedW, shapedH) = TryMeasure(Content, FontCss, Content.Length * 8, 16);
        return new(
            Math.Min(available.Width, Math.Max(0, shapedW + 12 + border.Left + border.Right)),
            Math.Max(28, shapedH + 10 + border.Top + border.Bottom));
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (_templatedContent is null) return;
        var border = BorderThickness;
        var tm = _templatedContent is FrameworkElement marginSource ? marginSource.Margin : new Thickness(0);
        _templatedContent.Arrange(new WpfRect(
            finalRect.X + border.Left + tm.Left,
            finalRect.Y + border.Top + tm.Top,
            Math.Max(0, finalRect.Width - border.Left - border.Right - tm.Left - tm.Right),
            Math.Max(0, finalRect.Height - border.Top - border.Bottom - tm.Top - tm.Bottom)));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        if (Background is not null)
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height,
                BrushRender.ResolveFill(Background, _backgroundRgba ?? 0x00000000));
        if (_borderRgba is { } border)
            PaintBorder(dc, r, border);
        if (_templatedContent is not null)
        {
            RenderVisible(dc, _templatedContent);
            return;
        }
        if (!string.IsNullOrEmpty(Content))
            dc.DrawText(Content, (float)(r.X + 6), (float)(r.Y + r.Height / 2), FontCss,
                !IsEffectivelyEnabled ? 0x707070FFu : IsSelected ? 0xFFFFFFFFu : ForegroundRgba, 0, 2, -1);
    }

    /// <summary>
    /// Paints the border inside the row edges (uniform and non-uniform alike),
    /// so a transparent Background keeps the parent selection highlight
    /// visible inside the ring.
    /// </summary>
    private void PaintBorder(DrawingContext dc, WpfRect r, uint border)
    {
        var t = BorderThickness;
        var left = Math.Min(t.Left, r.Width);
        var right = Math.Min(t.Right, r.Width);
        var top = Math.Min(t.Top, r.Height);
        var bottom = Math.Min(t.Bottom, r.Height);
        if (left > 0)
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)left, (float)r.Height, border);
        if (right > 0)
            dc.DrawRectangle((float)(r.X + r.Width - right), (float)r.Y, (float)right, (float)r.Height, border);
        if (top > 0)
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)top, border);
        if (bottom > 0)
            dc.DrawRectangle((float)r.X, (float)(r.Y + r.Height - bottom), (float)r.Width, (float)bottom, border);
    }
}

/// <summary>
/// ListBox v1: vertical stack of <see cref="ListBoxItem"/> rows with
/// single selection (Selector subset). Children other than ListBoxItem are
/// rejected at the loader boundaries; programmatic misuse renders nothing
/// for non-items. <see cref="ItemsSource"/> binds a data collection (one
/// generated ListBoxItem per item, DisplayMemberPath/ToString text,
/// INotifyCollectionChanged live sync, WPF parity guards). No internal
/// scrolling (nest in a ScrollViewer or size to content); virtualization
/// and multi-selection are out of scope. Control-grade chrome (matches
/// System.Windows.Controls.ListBox): a Control like its sibling item
/// containers, so Background/BorderBrush/BorderThickness/Template setters
/// resolve through the inherited Control dependency properties (OxyPlot
/// ExampleBrowser MainWindow.xaml sets BorderThickness="0,0,0,1" on the
/// list itself). A null Background keeps the default white fill and a null
/// BorderBrush the default gray frame (real WPF draws the list with its
/// default style the same way). An explicitly set BorderThickness wins per
/// edge; otherwise the legacy 1px frame applies (an all-zero thickness is
/// the default value, so it keeps that frame instead of removing it).
/// </summary>
public class ListBox : Control
{
    /// <summary>
    /// Currency sync knob (matches Selector.IsSynchronizedWithCurrentItemProperty;
    /// the WPF-identical shared Selector instance, so template attributes and
    /// qualified setters bind; ListBox keeps its own row model, so the CLR
    /// wrapper stays store-backed).
    /// </summary>
    public static readonly DependencyProperty IsSynchronizedWithCurrentItemProperty =
        Selector.IsSynchronizedWithCurrentItemProperty.AddOwner(typeof(ListBox));

    /// <summary>Currency sync knob (store-backed; null default like WPF).</summary>
    public bool? IsSynchronizedWithCurrentItem
    {
        get => (bool?)GetValue(IsSynchronizedWithCurrentItemProperty);
        set => SetValue(IsSynchronizedWithCurrentItemProperty, value);
    }

    private const uint DefaultFillRgba = 0xFFFFFFFF;
    private const uint DefaultFrameRgba = 0x707070FF;
    private static readonly Thickness DefaultFrame = new(1);

    /// <summary>
    /// Frame edges (an explicitly set BorderThickness wins per edge; the
    /// default zero value keeps the legacy 1px frame).
    /// </summary>
    private Thickness EffectiveBorder => BorderThickness == default ? DefaultFrame : BorderThickness;

    /// <summary>Fill (a null Background keeps the default white, like the WPF default style).</summary>
    private uint FillRgba => Background is null
        ? DefaultFillRgba
        : BrushRender.ResolveFill(Background, DefaultFillRgba);

    /// <summary>Frame ink (a null BorderBrush keeps the default gray).</summary>
    private uint FrameRgba => BorderBrush is null
        ? DefaultFrameRgba
        : BrushRender.ResolveFill(BorderBrush, DefaultFrameRgba);

    /// <summary>
    /// Rows in order (flat direct children, or depth-first across the
    /// generated <see cref="GroupItem"/> containers while grouped).
    /// </summary>
    public IReadOnlyList<ListBoxItem> Items
    {
        get
        {
            if (_groupItems.Count > 0)
            {
                var flat = new List<ListBoxItem>();
                foreach (var group in _groupItems)
                    foreach (var child in group.ItemsPanel.LogicalChildren)
                        if (child is ListBoxItem row) flat.Add(row);
                return flat;
            }
            if (_customHost is not null)
            {
                var hosted = new List<ListBoxItem>();
                foreach (UIElement c in _customHost.Children)
                    if (c is ListBoxItem item) hosted.Add(item);
                return hosted;
            }
            var items = new List<ListBoxItem>();
            foreach (UIElement c in Children)
                if (c is ListBoxItem item) items.Add(item);
            return items;
        }
    }

    private DataTemplate? _itemTemplate;

    /// <summary>
    /// Row template (matches ItemsControl.ItemTemplate): parsed and retained
    /// from inline XAML (<![CDATA[<ListBox.ItemTemplate><DataTemplate>]]>).
    /// Generated rows inflate it per data item with the item as DataContext
    /// (like ItemsControl); without it rows render DisplayMemberPath/ToString
    /// text like before.
    /// </summary>
    public DataTemplate? ItemTemplate
    {
        get => _itemTemplate;
        set
        {
            if (ReferenceEquals(_itemTemplate, value)) return;
            _itemTemplate = value;
            if (_itemsSource is not null) SyncGeneratedRows(clearPool: true);
            else InvalidateMeasure();
        }
    }

    /// <summary>Template-selector token (matches ItemsControl.ItemTemplateSelectorProperty).</summary>
    public static readonly DependencyProperty ItemTemplateSelectorProperty =
        DependencyProperty.Register("ItemTemplateSelector", typeof(DataTemplateSelector), typeof(ListBox),
            new FrameworkPropertyMetadata((DataTemplateSelector?)null, (d, _) =>
            {
                if (((ListBox)d)._itemsSource is not null) ((ListBox)d).SyncGeneratedRows(clearPool: true);
                else ((ListBox)d).InvalidateMeasure();
            }));

    private DataTemplateSelector? _itemTemplateSelector;

    /// <summary>Per-item visual selector (matches ItemsControl.ItemTemplateSelector).</summary>
    public DataTemplateSelector? ItemTemplateSelector
    {
        get => _itemTemplateSelector ?? (DataTemplateSelector?)GetValue(ItemTemplateSelectorProperty);
        set
        {
            if (ReferenceEquals(_itemTemplateSelector, value)) return;
            _itemTemplateSelector = value;
            SetValue(ItemTemplateSelectorProperty, value);
            if (_itemsSource is not null) SyncGeneratedRows(clearPool: true);
            else InvalidateMeasure();
        }
    }

    private StyleSelector? _itemContainerStyleSelector;

    /// <summary>
    /// Item container style selector (matches ItemsControl.ItemContainerStyleSelector):
    /// changing it re-applies per-row styles so realized and recycled rows follow.
    /// </summary>
    public StyleSelector? ItemContainerStyleSelector
    {
        get => _itemContainerStyleSelector;
        set
        {
            if (ReferenceEquals(_itemContainerStyleSelector, value)) return;
            _itemContainerStyleSelector = value;
            if (value is null) { InvalidateMeasure(); return; }
            foreach (var (row, item) in _generatedData.ToArray()) ApplyItemContainerStyle(row, item);
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Group visuals (matches ItemsControl.GroupStyle): each entry styles
    /// one group level. Grouping only engages while <see cref="ItemsSource"/>
    /// is an <see cref="ICollectionView"/> exposing Groups; otherwise rows
    /// stay flat and layout/paint are unchanged.
    /// </summary>
    public ObservableCollection<GroupStyle> GroupStyle { get; } = new();

    /// <summary>
    /// Items host factory (matches ItemsControl.ItemsPanelProperty on the
    /// legacy row model): a real DP so style setters resolve it through
    /// DependencyProperty.FromName like WPF; the store is the value
    /// authority, the change callback keeps the classification side
    /// effects (same-value writes never reclassify, like the old guard).
    /// When it inflates to a WrapPanel the rows arrange in the same wrap
    /// flow (the staged CardDemo host); when it inflates to a horizontal
    /// StackPanel the rows stack left-to-right (StackPanel horizontal
    /// semantics); when it inflates to a converted (non-engine-assembly)
    /// Panel the rows parent into that panel and its converted
    /// MeasureOverride/ArrangeOverride run (the staged HoneycombPanelDemo
    /// host, see Slice111HandyPanels); any other host keeps the legacy
    /// vertical stack, the same honest-hint treatment as IsVirtualizing.
    /// Grouped lists ignore the custom host and keep the grouped path.
    /// </summary>
    public static readonly DependencyProperty ItemsPanelProperty =
        DependencyProperty.Register("ItemsPanel", typeof(ItemsPanelTemplate), typeof(ListBox),
            new FrameworkPropertyMetadata((ItemsPanelTemplate?)null,
                (d, _) => ((ListBox)d).OnItemsPanelChanged()));

    /// <summary>
    /// Items host factory (matches ItemsControl.ItemsPanel on the legacy row
    /// model): retained and queryable (see ItemsPanelProperty).
    /// </summary>
    public ItemsPanelTemplate? ItemsPanel
    {
        get => (ItemsPanelTemplate?)GetValue(ItemsPanelProperty);
        set => SetValue(ItemsPanelProperty, value);
    }

    private void OnItemsPanelChanged()
    {
        ClassifyItemsPanel((ItemsPanelTemplate?)GetValue(ItemsPanelProperty));
        InvalidateMeasure();
    }

    private static readonly DependencyPropertyKey IsGroupingPropertyKey =
        DependencyProperty.RegisterReadOnly("IsGrouping", typeof(bool), typeof(ListBox),
            new FrameworkPropertyMetadata(false));

    /// <summary>Grouping flag (matches ItemsControl.IsGroupingProperty on the
    /// legacy row model; read-only like WPF).</summary>
    public static readonly DependencyProperty IsGroupingProperty = IsGroupingPropertyKey.DependencyProperty;

    /// <summary>Whether items are grouped (tracks the grouped row path).</summary>
    public bool IsGrouping => (bool)(GetValue(IsGroupingProperty) ?? false);

    private void SetIsGrouping(bool value) => SetValue(IsGroupingPropertyKey, value);

    private Orientation? _wrapOrientation;
    private bool _stackHorizontal;
    private Panel? _customHost;

    /// <summary>
    /// Classifies the retained panel with a single inflation: WrapPanel sets
    /// the wrap flow, a horizontal StackPanel the side-by-side stack, and a
    /// converted Panel becomes the real rows host (re-homing any rows the
    /// previous home holds). Engine panels that are neither keep the legacy
    /// stack; unparsable hosts never fail the set, like the prior
    /// retain-only behavior.
    /// </summary>
    private void ClassifyItemsPanel(ItemsPanelTemplate? panel)
    {
        _wrapOrientation = null;
        _stackHorizontal = false;
        Panel? host = null;
        if (panel is not null)
        {
            try
            {
                var content = panel.LoadContent();
                if (content is WrapPanel wrap) _wrapOrientation = wrap.Orientation;
                else if (content is StackPanel stack && stack.Orientation == Orientation.Horizontal)
                    _stackHorizontal = true;
                else if (content is VirtualizingPanel vp)
                    host = vp;
                else if (content is Panel converted &&
                    content.GetType().Assembly != typeof(Panel).Assembly)
                    host = converted;
            }
            catch
            {
            }
        }
        if (!ReferenceEquals(_customHost, host))
        {
            var rows = new List<ListBoxItem>();
            if (_customHost is not null)
            {
                foreach (var row in _customHost.Children.OfType<ListBoxItem>().ToArray())
                {
                    _customHost.RemoveChild(row);
                    rows.Add(row);
                }
                base.RemoveChild(_customHost);
            }
            foreach (var row in base.Children.OfType<ListBoxItem>().ToArray())
            {
                base.RemoveChild(row);
                rows.Add(row);
            }
            _customHost = host;
            if (_customHost is not null)
            {
                base.AddChild(_customHost);
                foreach (var row in rows) _customHost.AddChild(row);
            }
            else
            {
                foreach (var row in rows) base.AddChild(row);
            }
        }
    }

    /// <summary>Rows home (the converted host when engaged, else this list).</summary>
    private bool UseCustomHost => _customHost is not null;

    private void AddRowHome(UIElement row)
    {
        if (UseCustomHost) _customHost!.AddChild(row);
        else base.AddChild(row);
    }

    private bool RemoveRowHome(UIElement child)
    {
        if (child is ListBoxItem && _customHost is not null && _customHost.Children.Contains(child))
            return _customHost.RemoveChild(child);
        return base.RemoveChild(child);
    }

    private Style? _itemContainerStyle;

    /// <summary>
    /// Row style (matches ItemsControl.ItemContainerStyle): applied to every
    /// generated <see cref="ListBoxItem"/>, existing and future.
    /// </summary>
    public Style? ItemContainerStyle
    {
        get => _itemContainerStyle;
        set
        {
            if (ReferenceEquals(_itemContainerStyle, value)) return;
            _itemContainerStyle = value;
            if (value is null) { InvalidateMeasure(); return; }
            foreach (var (row, item) in _generatedData.ToArray()) ApplyItemContainerStyle(row, item);
            InvalidateMeasure();
        }
    }

    private readonly List<GroupItem> _groupItems = new();

    public ListBox()
    {
        GroupStyle.CollectionChanged += (_, _) => SyncGeneratedRows();
        ItemContainerGenerator = new ItemContainerGenerator(new LegacyListBoxOwner(this));
    }

    /// <summary>
    /// Row-container map (matches ItemsControl.ItemContainerGenerator):
    /// converted code subscribes ItemsChanged (ChatBox scroll-to-bottom) and
    /// looks up containers. Backed over the legacy row sync, which raises
    /// Reset after every rebuild (see NotifyItemsReset).
    /// </summary>
    public ItemContainerGenerator ItemContainerGenerator { get; }

    /// <summary>
    /// Generator owner behind the legacy row model: direct generator lookups
    /// mint rows through the same factory and track them in the same map, so
    /// both owners observe the same rows. Row generation itself stays in
    /// SyncGeneratedRows (the owner only serves explicit lookups, which no
    /// staged view performs today).
    /// </summary>
    private sealed class LegacyListBoxOwner(ListBox owner) : IItemContainerOwner
    {
        private readonly Dictionary<DependencyObject, object?> _pending = new();

        public System.Collections.IList ContainerItems
        {
            get
            {
                if (owner._itemsSource is System.Collections.IList live) return live;
                var snapshot = new System.Collections.ArrayList();
                if (owner._itemsSource is not null)
                    foreach (var item in owner._itemsSource) snapshot.Add(item);
                return snapshot;
            }
        }

        public DataTemplate? ItemTemplate => owner.ItemTemplate;

        public DataTemplateSelector? ItemTemplateSelector => owner.ItemTemplateSelector;

        public bool IsItemItsOwnContainer(object item) => item is ListBoxItem;

        public DependencyObject GetContainerForItem() => new ListBoxItem();

        public void PrepareContainerForItem(DependencyObject container, object? item)
        {
            if (container is ListBoxItem row && !ReferenceEquals(row, item))
            {
                owner.PrepareRow(row, item);
                owner.ApplyItemContainerStyle(row, item);
                _pending[container] = item;
                var index = ContainerItems.IndexOf(item);
                var isSelected = index >= 0 && index == owner.SelectedIndex;
                row.SetSelectedFromOwner(isSelected);
            }
            else if (container is FrameworkElement visual && !ReferenceEquals(visual, item))
            {
                // Template-selector roots are not ListBoxItem shells: they carry
                // the item as DataContext (mirroring ItemsControl), refreshed on
                // every reuse so recycled visuals never show stale data.
                visual.DataContext = item;
            }
        }

        public void ClearContainerForItem(DependencyObject container, object item)
        {
            if (container is ListBoxItem row)
            {
                owner._generatedData.Remove(row);
                row.SetSelectedFromOwner(false);
                row.DataContext = null;
                row.Content = null;
            }
            else if (container is FrameworkElement visual)
            {
                visual.DataContext = null;
            }
        }

        public void AddGeneratedChild(DependencyObject container)
        {
            if (container is ListBoxItem row)
            {
                owner._generatedData[row] = _pending.TryGetValue(container, out var item) ? item : null;
                _pending.Remove(container);
            }
            if (container is UIElement element)
            {
                if (owner.UseCustomHost) owner._customHost!.AddChild(element);
                else owner.AddChildInternal(element);
            }
        }

        public void RemoveGeneratedChild(DependencyObject container)
        {
            if (container is UIElement element)
            {
                if (owner.UseCustomHost) owner._customHost!.RemoveChild(element);
                else owner.RemoveChildInternal(element);
            }
        }
    }

    internal void AddChildInternal(UIElement child) => base.AddChild(child);

    internal bool RemoveChildInternal(UIElement child) => base.RemoveChild(child);

    private int _selectedIndex = -1;

    /// <summary>
    /// Selection breadth (matches ListBox.SelectionMode; retained, Single
    /// default like WPF). The engine list selects one row at a time, so
    /// Multiple/Extended load but stay a selection breadth boundary.
    /// </summary>
    public SelectionMode SelectionMode { get; set; } = SelectionMode.Single;

    /// <summary>
    /// Selected row index (-1 = none). The raw value is kept even when no
    /// row qualifies yet (XAML/BAML attributes arrive before child rows, as
    /// in real WPF); the getter reports the effective index, -1 while the
    /// raw value is negative or beyond the current rows.
    /// </summary>
    public int SelectedIndex
    {
        get
        {
            if (_customHost is VirtualizingPanel && _itemsSource is not null)
            {
                var count = ItemContainerGenerator.ContainerItems.Count;
                return _selectedIndex >= 0 && _selectedIndex < count ? _selectedIndex : -1;
            }
            return _selectedIndex >= 0 && _selectedIndex < Items.Count ? _selectedIndex : -1;
        }
        set
        {
            var raw = value < 0 ? -1 : value;
            if (_selectedIndex == raw) return;
            var old = SelectedIndex;
            _selectedIndex = raw;
            if (SelectedIndex != old)
            {
                PushRowSelection();
                // Native parity: selecting a row brings it into view (WPF
                // scrolls the selected ListBoxItem container so keyboard and
                // pointer selection in a scrolled list stay visible). Without
                // a parent ScrollViewer this walks nowhere and stays silent.
                if (_customHost is VirtualizingStackPanel vsp && _selectedIndex >= 0)
                    vsp.SetVerticalOffset(vsp.GetTotalExtent(_selectedIndex));
                else
                    SelectedItem?.BringIntoView();
                InvalidateVisual();
                SelectionChanged?.Invoke();
                PushSelectedItemSource();
            }
        }
    }

    /// <summary>
    /// Bound data items (bindable ItemsSource half of the WPF contract,
    /// mirroring ItemsControl.ItemsSource on the legacy row model): setting
    /// a collection mints one ListBoxItem container per data item (ListBoxItem
    /// data is its own container; other items render through
    /// <see cref="DisplayMemberPath"/> or ToString text, like the
    /// ItemsControl Label path renders data). The raw SelectedIndex pending
    /// semantics carry over the sync, INotifyCollectionChanged sources push
    /// live adds/removes/resets while the selected data item survives, and
    /// templated per-item visuals stay a boundary (like DisplayMemberPath
    /// styling on ItemsControl). WPF parity guards: setting a source while
    /// direct rows exist throws, and direct AddChild/RemoveChild while a
    /// source is set throws (the internal sync bypasses through the base).
    /// Clearing (null) drops the generated rows and keeps the raw index,
    /// like removing rows. SelectedItem keeps the container contract
    /// (ListBoxItem?); the data behind a generated row is its Content text,
    /// or the templated visual's DataContext when ItemTemplate is set.
    /// </summary>
    public System.Collections.IEnumerable? ItemsSource
    {
        get => _itemsSource;
        set
        {
            if (ReferenceEquals(_itemsSource, value)) return;
            if (value is not null)
                foreach (var row in Items)
                    if (!_generatedData.ContainsKey(row))
                        throw new InvalidOperationException(
                            "Items collection must be empty before using ItemsSource.");
            if (_itemsSourceSubscription is not null && _itemsSource is INotifyCollectionChanged oldIncc)
                oldIncc.CollectionChanged -= _itemsSourceSubscription;
            _itemsSourceSubscription = null;
            _itemsSource = value;
            SyncGeneratedRows();
            if (_itemsSource is INotifyCollectionChanged incc)
            {
                NotifyCollectionChangedEventHandler handler = (_, _) => SyncGeneratedRows();
                _itemsSourceSubscription = handler;
                incc.CollectionChanged += handler;
            }
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Property path rendered for data items (stored; re-resolves generated
    /// row text when changed, like the ItemsControl DisplayMemberPath knob).
    /// </summary>
    public string? DisplayMemberPath
    {
        get => _displayMemberPath;
        set
        {
            if (_displayMemberPath == value) return;
            _displayMemberPath = value;
            foreach (var (row, data) in _generatedData)
                if (!ReferenceEquals(row, data))
                    row.Content = ResolveItemText(data);
        }
    }

    private System.Collections.IEnumerable? _itemsSource;
    private string? _displayMemberPath;
    private NotifyCollectionChangedEventHandler? _itemsSourceSubscription;
    private readonly Dictionary<ListBoxItem, object?> _generatedData = new();

    private string ResolveItemText(object? item)
    {
        if (item is null) return string.Empty;
        if (!string.IsNullOrEmpty(_displayMemberPath)
            && BindingExpression.TryRead(item, _displayMemberPath, out var value))
            return value?.ToString() ?? string.Empty;
        return item.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Style scope for generated rows (matches the loader's document-order
    /// scope): generated rows are not parented yet when styled, so the
    /// list's own ancestry stands in for the loader scope, letting
    /// StaticResource setters in ItemContainerStyle resolve window-level
    /// keys; the tree lookup still runs first, like WPF's apply-time lookup.
    /// </summary>
    private StyleApplyContext GeneratedStyleContext()
    {
        var ancestry = new List<FrameworkElement>();
        for (var node = (FrameworkElement?)this; node is not null; node = node.Parent as FrameworkElement)
            ancestry.Add(node);
        ancestry.Reverse();
        return new StyleApplyContext { Scope = ancestry };
    }

    /// <summary>
    /// Pushes the selected data item back into a bound SelectedItem source
    /// (WPF target-follows-source in reverse: the container contract keeps
    /// SelectedItem as the row, but the binding carries the data item, so
    /// the push sends the data, honoring the binding mode and trigger).
    /// </summary>
    private void PushSelectedItemSource()
    {
        var expression = ActiveBindings.FirstOrDefault(b => b.TargetProperty == "SelectedItem");
        expression?.PushSourceValue(SelectedData());
    }

    /// <summary>
    /// Mints a row for a data item (matches the ItemsControl container path):
    /// with ItemTemplate set the template inflates per row with the item as
    /// DataContext (Content stays empty so no ToString text paints); without
    /// it rows render DisplayMemberPath/ToString text like before.
    /// </summary>
    private ListBoxItem NewDataRow(object? item)
    {
        var row = new ListBoxItem();
        PrepareRow(row, item);
        return row;
    }

    /// <summary>
    /// Fills a row for a data item (shared by the legacy sync and the
    /// generator owner above): template inflation with the item as
    /// DataContext, else DisplayMemberPath/ToString text.
    /// </summary>
    private void PrepareRow(ListBoxItem row, object? item)
    {
        var previousTemplate = row.ContentTemplate;
        row.ContentTemplate = ItemTemplate;
        row.ContentTemplateSelector = ItemTemplateSelector;
        var template = ItemTemplate ?? ItemTemplateSelector?.SelectTemplate(item ?? string.Empty, row);
        if (template is null)
        {
            if (row.HasTemplatedContent) row.ClearTemplatedContent();
            row.Content = ResolveItemText(item);
            return;
        }
        // Recycling reuse: when the same template already produced this row's
        // visual, only the DataContext advances (no re-inflation, no re-wiring,
        // no heap churn during steady scrolling). A template change re-inflates.
        if (row.HasTemplatedContent && ReferenceEquals(previousTemplate, template) &&
            row.TemplatedContent is FrameworkElement existing)
        {
            row.ContentTemplate = template;
            existing.DataContext = item;
            return;
        }
        row.ContentTemplate = template;
        var visual = template.LoadContent();
        if (visual is FrameworkElement fresh) fresh.DataContext = item;
        row.SetTemplatedContent(visual);
    }

    private void AddGeneratedRow(object? item)
    {
        var row = item as ListBoxItem ?? NewDataRow(item);
        _generatedData[row] = item;
        ApplyItemContainerStyle(row, item);
        AddRowHome(row);
    }

    /// <summary>
    /// Applies the row style (fixed or selector-chosen) with a per-row applied
    /// guard, so recycled rows pick up selector/style changes without
    /// double-wiring event setters on every re-prepare.
    /// </summary>
    private void ApplyItemContainerStyle(ListBoxItem row, object? item)
    {
        var style = ItemContainerStyle ?? ItemContainerStyleSelector?.SelectStyle(item ?? string.Empty, row);
        if (style is null) return;
        if (ReferenceEquals(row.GetAttached("ListBox.AppliedItemContainerStyle"), style)) return;
        row.SetAttached("ListBox.AppliedItemContainerStyle", style);
        style.ApplyTo(row, GeneratedStyleContext());
    }

    private object? SelectedData()
    {
        var index = SelectedIndex;
        if (index < 0) return null;
        if (_customHost is VirtualizingPanel && _itemsSource is not null)
        {
            var containerItems = ItemContainerGenerator.ContainerItems;
            if (index < containerItems.Count) return containerItems[index];
            return null;
        }
        if ((uint)index >= (uint)Items.Count) return null;
        var row = Items[index];
        return _generatedData.TryGetValue(row, out var data) ? data : row;
    }

    private bool TryIndexOfData(object? data, out int index)
    {
        if (_customHost is VirtualizingPanel && _itemsSource is not null)
        {
            var containerItems = ItemContainerGenerator.ContainerItems;
            index = containerItems.IndexOf(data);
            return index >= 0;
        }
        var items = Items;
        for (var i = 0; i < items.Count; i++)
        {
            if (!_generatedData.TryGetValue(items[i], out var candidate)) continue;
            if (candidate is null ? data is null : candidate.Equals(data))
            {
                index = i;
                return true;
            }
        }
        index = -1;
        return false;
    }

    /// <summary>
    /// Builds grouped containers when the source view exposes groups and at
    /// least one GroupStyle exists (true); flat rows stay the default.
    /// </summary>
    private bool TrySyncGroupedRows()
    {
        if (GroupStyle.Count == 0) return false;
        if (_itemsSource is not ICollectionView view) return false;
        var groups = view.Groups;
        if (groups is null || groups.Count == 0) return false;
        foreach (var node in groups)
            if (node is CollectionViewGroup group)
                AddGroupedRows(group, 0);
        var grouped = _groupItems.Count > 0;
        SetIsGrouping(grouped);
        return grouped;
    }

    /// <summary>
    /// Generates one GroupItem per group (nested groups recurse as sibling
    /// containers with the deeper style level); direct children become rows.
    /// </summary>
    private void AddGroupedRows(CollectionViewGroup group, int level)
    {
        var style = GroupStyle[Math.Min(level, GroupStyle.Count - 1)];
        if (group.ItemCount == 0 && style.HidesIfEmpty) return;
        var container = new GroupItem { Group = group };
        container.ApplyGroupStyle(style, GeneratedStyleContext());
        var nested = new List<CollectionViewGroup>();
        foreach (var child in group.Items)
        {
            if (child is CollectionViewGroup sub) nested.Add(sub);
            else AddGroupedRow(container, child);
        }
        base.AddChild(container);
        _groupItems.Add(container);
        foreach (var sub in nested) AddGroupedRows(sub, level + 1);
    }

    /// <summary>Mints one row for a grouped data item (container style applied).</summary>
    private void AddGroupedRow(GroupItem container, object? item)
    {
        var row = item as ListBoxItem ?? NewDataRow(item);
        _generatedData[row] = item;
        ItemContainerStyle?.ApplyTo(row, GeneratedStyleContext());
        container.AddGroupRow(row);
    }

    /// <summary>
    /// Re-seats the selection from a bound SelectedItem data value (WPF
    /// target-follows-source; a null data value clears). Skipped without a
    /// SelectedItem binding or while its source is unresolvable.
    /// </summary>
    private void SyncBoundSelectedItem()
    {
        var expression = ActiveBindings.FirstOrDefault(b => b.TargetProperty == "SelectedItem");
        if (expression?.Source is null) return;
        if (!BindingExpression.TryRead(expression.Source, expression.Binding.Path, out var data)) return;
        if (data is null) { _selectedIndex = -1; return; }
        if (TryIndexOfData(data, out var index)) _selectedIndex = index;
    }

    /// <summary>
    /// Rebuilds generated rows from the current source (single fire at most):
    /// the selected data item survives when still present, otherwise the raw
    /// index keeps legacy sliding semantics (effective -1 past the end).
    /// Generator mappings are dropped with the unparented rows (they would
    /// otherwise hand out orphaned, unprepared containers); the pool survives
    /// plain mutations but not template/selector changes.
    /// </summary>
    private void SyncGeneratedRows(bool clearPool = false)
    {
        var old = SelectedIndex;
        var selectedData = SelectedData();
        // Virtualized rows re-enter through the generator on the next measure:
        // park them first so the rebuild reuses shells instead of minting a
        // fresh window per mutation. (Non-virtualized rows rebuild directly.)
        if (_customHost is VirtualizingPanel)
            ItemContainerGenerator.ParkRealizedContainers();
        // Keep the raw index across the rebuild (a pending selection
        // materializes when rows arrive; a lost item slides onto the row
        // moving in, or reports -1 past the end, like removing rows); the
        // selected data is re-seated below when still present.
        foreach (var row in _generatedData.Keys.ToArray())
            RemoveRowHome(row);
        _generatedData.Clear();
        ItemContainerGenerator.ResetMappings(clearPool);
        foreach (var group in _groupItems.ToArray())
            base.RemoveChild(group);
        _groupItems.Clear();
        SetIsGrouping(false);
        if (_itemsSource is not null)
        {
            if (!TrySyncGroupedRows())
            {
                if (_customHost is VirtualizingPanel)
                {
                    // Defer container generation to the virtualizing panel
                }
                else
                {
                    foreach (var item in _itemsSource)
                        AddGeneratedRow(item);
                }
            }
        }
        if (_itemsSource is not null && old >= 0 && selectedData is not null
            && TryIndexOfData(selectedData, out var restored))
            _selectedIndex = restored;
        SyncBoundSelectedItem();
        var now = SelectedIndex;
        var nowData = SelectedData();
        var dataChanged = selectedData is null ? nowData is not null
            : nowData is null || !selectedData.Equals(nowData);
        if (now != old || dataChanged)
        {
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }
        // Rebuilt rows are new objects (default unselected): always re-seat
        // the flag even when the effective selection did not change.
        PushRowSelection();
        InvalidateMeasure();
        ItemContainerGenerator.NotifyItemsReset();
    }

    /// <summary>
    /// Appends a row. Only ListBoxItem rows are accepted (loader boundaries
    /// reject anything else first; this is the programmatic backstop).
    /// Materializing a pending selection fires <see cref="SelectionChanged"/>.
    /// While <see cref="ItemsSource"/> is set the rows are generated, so a
    /// direct append throws like WPF.
    /// </summary>
    public new void AddChild(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (_itemsSource is not null)
            throw new InvalidOperationException(
                "Operation is not valid while ItemsSource is in use. Modify the source collection instead.");
        if (child is not ListBoxItem)
            throw new InvalidOperationException(
                $"<ListBox> rows must be <ListBoxItem> (have <{child.GetType().Name}>).");
        var old = SelectedIndex;
        AddRowHome(child);
        if (SelectedIndex != old)
        {
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }
    }

    /// <summary>
    /// Removes a row. The selection tracks its item (Selector semantics):
    /// removing a row above shifts the raw index down, removing the selected
    /// row hands selection to the row sliding into its place (-1 when the
    /// list empties below it). Any effective change fires
    /// <see cref="SelectionChanged"/>. While <see cref="ItemsSource"/> is
    /// set the rows are generated, so a direct remove throws like WPF.
    /// </summary>
    public new bool RemoveChild(UIElement child)
    {
        if (_itemsSource is not null)
            throw new InvalidOperationException(
                "Operation is not valid while ItemsSource is in use. Modify the source collection instead.");

        // Index and item identity both matter: removing the selected row
        // keeps the raw index but hands selection to the row sliding in.
        var oldIndex = SelectedIndex;
        var oldItem = SelectedItem;
        var items = Items;
        var at = -1;
        for (var i = 0; i < items.Count; i++)
            if (ReferenceEquals(items[i], child)) { at = i; break; }
        var removed = RemoveRowHome(child);
        if (removed && at >= 0 && at < _selectedIndex) _selectedIndex--;
        if (removed && (SelectedIndex != oldIndex || !ReferenceEquals(SelectedItem, oldItem)))
        {
            PushRowSelection();
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }
        return removed;
    }

    /// <summary>Selected row (null when nothing is effectively selected).</summary>
    public ListBoxItem? SelectedItem
    {
        get
        {
            var i = SelectedIndex;
            if (i < 0) return null;
            if (_customHost is VirtualizingPanel)
            {
                return ItemContainerGenerator.IsRealized(i)
                    ? ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem
                    : null;
            }
            return i < Items.Count ? Items[i] : null;
        }
        set
        {
            if (value is null) { SelectedIndex = -1; return; }
            if (_customHost is VirtualizingPanel)
            {
                var index = ItemContainerGenerator.IndexFromContainer(value);
                if (index >= 0)
                {
                    SelectedIndex = index;
                    return;
                }
            }
            var items = Items;
            for (var i = 0; i < items.Count; i++)
                if (ReferenceEquals(items[i], value)) { SelectedIndex = i; return; }
            SelectedIndex = -1;
        }
    }

    /// <summary>Pushes the effective selection into each row's IsSelected
    /// store (keeps style triggers and converted reads fresh; writes from
    /// the push skip the route-back callback).</summary>
    internal void PushRowSelection()
    {
        var selected = SelectedItem;
        if (_customHost is VirtualizingPanel)
        {
            var selIdx = SelectedIndex;
            foreach (var row in Items)
            {
                var rowIdx = ItemContainerGenerator.IndexFromContainer(row);
                row.SetSelectedFromOwner((rowIdx >= 0 && rowIdx == selIdx) || (selected is not null && ReferenceEquals(row, selected)));
            }
            return;
        }
        foreach (var row in Items)
            row.SetSelectedFromOwner(selected is not null && ReferenceEquals(row, selected));
    }

    /// <summary>Raised after the effective selection actually changes.</summary>
    public event Action? SelectionChanged;

    /// <summary>Value path (mirrors the Selector/ComboBox contract, which
    /// native WPF shares across selectors; stored, no dependency property).</summary>
    public string? SelectedValuePath { get; set; }

    /// <summary>Selected value (mirrors the Selector/ComboBox contract:
    /// reads the path off the selected row, writes select by path match,
    /// null clears, miss lands at -1). Generated rows resolve the path
    /// off the backing data item (native selectors read data, not
    /// containers); direct rows read off themselves.</summary>
    public object? SelectedValue
    {
        get
        {
            var item = SelectedItem;
            if (item is null) return null;
            if (SelectedValuePath is null) return item;
            return BindingExpression.TryRead(SelectedSource(item), SelectedValuePath, out var value) ? value : null;
        }
        set
        {
            if (value is null) { SelectedIndex = -1; return; }
            var items = Items;
            for (var i = 0; i < items.Count; i++)
            {
                var candidate = SelectedValuePath is null
                    ? items[i]
                    : BindingExpression.TryRead(SelectedSource(items[i]), SelectedValuePath, out var v) ? v : null;
                if (Equals(candidate, value)) { SelectedIndex = i; return; }
            }
            SelectedIndex = -1;
        }
    }

    /// <summary>Backing data item for a generated row, else the row itself.</summary>
    private object? SelectedSource(ListBoxItem row) =>
        _generatedData.TryGetValue(row, out var data) ? data : row;

    /// <summary>Lists take focus (store-backed; the Control default is true).</summary>
    public override bool Focusable
    {
        get => (bool?)GetValue(FocusableProperty) ?? true;
        set => SetValue(FocusableProperty, value);
    }

    /// <summary>Selects a row by hit position; disabled rows never select
    /// (native parity); returns false when no enabled row covers it.</summary>
    public bool SelectAt(double x, double y)
    {
        var items = Items;
        for (var i = 0; i < items.Count; i++)
            if (items[i].IsEffectivelyEnabled && items[i].HitTest(new WpfPoint(x, y))) { SelectedIndex = i; return true; }
        return false;
    }

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        // Selector press-to-select: a left press on a row selects it (WPF
        // selects on press, not release). Disabled lists ignore all input.
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        if (e.Source is not Core.WasmInputEvent ev) return;
        switch (ev)
        {
            case Core.WasmInputEvent.Pointer pd when pd.Args.Kind == Core.WasmPointerKind.Down && e.IsLeftButtonDown:
                if (SelectAt(pd.Args.X, pd.Args.Y)) e.Handled = true;
                break;
            // Native parity: the wheel over the list scrolls whole rows
            // through the ancestor scroller (viewer idiom: pixels through
            // the row stride, minimum a row so small trackpad ticks still
            // move). Without an ancestor scroller there is nothing to
            // scroll; a zero tick keeps bubbling.
            case Core.WasmInputEvent.Wheel w:
                if (w.Args.DeltaY == 0) break;
                for (var node = Parent as FrameworkElement; node is not null; node = node.Parent as FrameworkElement)
                    if (node is ScrollViewer scroller)
                    {
                        var stride = Items.Count == 0 ? 24 : Items[0].RenderRect.Height;
                        if (stride <= 0) stride = 24;
                        var lines = (int)(w.Args.DeltaY / stride);
                        if (lines == 0) lines = w.Args.DeltaY > 0 ? 1 : -1;
                        scroller.VerticalOffset += lines * stride;
                        e.Handled = true;
                        break;
                    }
                break;
            case Core.WasmInputEvent.Key k when k.Args.IsDown && (_focused || _focusWithin):
                var count = Items.Count;
                if (count == 0) break;
                switch (k.Args.Key)
                {
                    case "ArrowDown": SelectedIndex = SelectedIndex < 0 ? 0 : Math.Min(count - 1, SelectedIndex + 1); e.Handled = true; break;
                    case "ArrowUp": SelectedIndex = SelectedIndex < 0 ? count - 1 : Math.Max(0, SelectedIndex - 1); e.Handled = true; break;
                    case "Home": SelectedIndex = 0; e.Handled = true; break;
                    case "End": SelectedIndex = count - 1; e.Handled = true; break;
                    // Native parity: paging moves the selection (the J13
                    // BringIntoView in the setter scrolls it visible); the
                    // parent viewer only pages its own viewport when it has
                    // focus, so the list must page its own selection here.
                    case "PageDown": SelectedIndex = SelectedIndex < 0 ? Math.Min(count - 1, PageSize() - 1) : Math.Min(count - 1, SelectedIndex + PageSize()); e.Handled = true; break;
                    case "PageUp": SelectedIndex = SelectedIndex < 0 ? 0 : Math.Max(0, SelectedIndex - PageSize()); e.Handled = true; break;
                    // Native parity: type-ahead (WPF TextSearch jumps to the
                    // next prefix match). Ctrl/Alt combinations stay for
                    // shortcuts; the focus gate above already keeps template
                    // editors (focused deeper than a row) out of reach.
                    default:
                        // Native parity: TextSearch matches any printable
                        // prefix (spaces, hyphens), not just letters/digits.
                        if (!k.Args.Ctrl && !k.Args.Alt && k.Args.Key.Length == 1 && !char.IsControl(k.Args.Key[0]))
                            e.Handled = TypeAhead(k.Args.Key[0]);
                        break;
                }
                break;
        }
    }

    private bool _focused;

    /// <summary>
    /// True while a row holds keyboard focus (set through
    /// <see cref="NoteRowFocus"/>; the press focuses the row itself, so the
    /// list's own flag stays false while arrows must still navigate).
    /// </summary>
    private bool _focusWithin;

    /// <summary>Row-focus feed for the keyboard gate (old-lost runs before
    /// new-got, so row-to-row moves end true and row-to-outside end
    /// false).</summary>
    internal void NoteRowFocus(bool within) => _focusWithin = within;

    /// <summary>Type-ahead prefix (resets after a quiet second).</summary>
    private string _typePrefix = string.Empty;

    /// <summary>Tick of the last type-ahead keystroke.</summary>
    private int _lastTypeTick;

    /// <summary>
    /// Jumps selection to the next row whose text starts with the
    /// accumulated prefix (case-insensitive, wraps past the end; a repeated
    /// single character cycles through that letter's rows like WPF
    /// TextSearch). Returns false when nothing matches (key stays
    /// unhandled). The J13 BringIntoView in the setter scrolls the match.
    /// </summary>
    private bool TypeAhead(char ch)
    {
        var rows = Items;
        if (rows.Count == 0) return false;
        if (Environment.TickCount - _lastTypeTick > 1000) _typePrefix = string.Empty;
        _lastTypeTick = Environment.TickCount;
        _typePrefix += char.ToLowerInvariant(ch);
        string needle;
        int start;
        if (_typePrefix.Length > 1 && _typePrefix.All(c => c == _typePrefix[0]))
        {
            needle = _typePrefix[..1];
            start = SelectedIndex < 0 ? 0 : (SelectedIndex + 1) % rows.Count;
        }
        else
        {
            needle = _typePrefix;
            start = SelectedIndex < 0 ? 0 : SelectedIndex + 1;
        }
        for (var n = 0; n < rows.Count; n++)
        {
            var at = (start + n) % rows.Count;
            if ((rows[at].Content ?? string.Empty).StartsWith(needle, StringComparison.OrdinalIgnoreCase))
            {
                SelectedIndex = at;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Paging stride (rows per viewport of the parent scroller; the list's
    /// own height when un-nested; at least one so paging always moves).
    /// </summary>
    private int PageSize()
    {
        var rows = Items;
        if (rows.Count == 0) return 1;
        var rowH = rows[0].RenderRect.Height;
        if (rowH <= 0) return 1;
        var viewport = RenderRect.Height;
        for (var node = Parent as FrameworkElement; node is not null; node = node.Parent as FrameworkElement)
            if (node is ScrollViewer scroller) { viewport = scroller.ActualHeight; break; }
        return Math.Max(1, (int)(viewport / rowH));
    }

    public override void OnGotFocus() { _focused = true; InvalidateVisual(); }
    public override void OnLostFocus() { _focused = false; InvalidateVisual(); }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        // A set Template sizes its chrome through the template (matches
        // Button/ScrollViewer); the rows still drive the list's own size.
        if (TemplateRoot is not null)
            TemplateRoot.Measure(available);
        // Grouped rows stack per container; flat rows keep the legacy path.
        if (_groupItems.Count > 0) return MeasureGrouped(available);
        // The converted host measures its rows through converted code (see
        // Slice111HandyPanels); the WrapPanel host runs the shared wrap flow
        // over the rows; the horizontal StackPanel host stacks left-to-right;
        // anything else stacks full-width slots.
        if (_customHost is not null) return MeasureCustomHost(available);
        if (_wrapOrientation is { } wrap) return MeasureWrapped(available, wrap);
        if (_stackHorizontal) return MeasureStacked(available);
        // The set BorderThickness grows the desired size (Control parity);
        // the default zero value adds nothing, like the legacy measure.
        var w = 0.0; var h = 0.0;
        var border = BorderThickness;
        foreach (var c in Items)
        {
            var m = GetMargin(c);
            c.Measure(new WpfSize(Math.Max(0, available.Width - m.Left - m.Right - border.Left - border.Right), available.Height));
            w = Math.Max(w, c.DesiredSize.Width + m.Left + m.Right);
            h += c.DesiredSize.Height + m.Top + m.Bottom;
        }
        return new WpfSize(w + border.Left + border.Right, h + border.Top + border.Bottom);
    }

    /// <summary>
    /// Wrap-flow measure over the generated rows (matches WrapPanel with the
    /// same orientation: rows size to content, margins fold into the pitch).
    /// </summary>
    private WpfSize MeasureWrapped(WpfSize available, Orientation orientation)
    {
        var border = BorderThickness;
        var extent = orientation == Orientation.Horizontal
            ? Math.Max(0, available.Width - border.Left - border.Right)
            : Math.Max(0, available.Height - border.Top - border.Bottom);
        var flowed = WrapPanel.MeasureFlow(Items, extent, orientation == Orientation.Horizontal);
        return new WpfSize(flowed.Width + border.Left + border.Right, flowed.Height + border.Top + border.Bottom);
    }

    /// <summary>
    /// Converted-host measure over the generated rows (matches the engine
    /// ItemsControl host path): the inflated panel measures its rows through
    /// converted MeasureOverride with the frame-chrome-adjusted constraint,
    /// and the list desired folds the chrome back in.
    /// </summary>
    private WpfSize MeasureCustomHost(WpfSize available)
    {
        var border = BorderThickness;
        _customHost!.Measure(new WpfSize(
            Math.Max(0, available.Width - border.Left - border.Right),
            Math.Max(0, available.Height - border.Top - border.Bottom)));
        return new WpfSize(
            _customHost.DesiredSize.Width + border.Left + border.Right,
            _customHost.DesiredSize.Height + border.Top + border.Bottom);
    }

    /// <summary>
    /// Horizontal-stack measure over the generated rows (matches a
    /// horizontal StackPanel host: rows size to content side by side).
    /// </summary>
    private WpfSize MeasureStacked(WpfSize available)
    {
        var border = BorderThickness;
        var w = 0.0; var h = 0.0;
        foreach (var c in Items)
        {
            var m = GetMargin(c);
            c.Measure(new WpfSize(available.Width, Math.Max(0, available.Height - m.Top - m.Bottom)));
            w += c.DesiredSize.Width + m.Left + m.Right;
            h = Math.Max(h, c.DesiredSize.Height + m.Top + m.Bottom);
        }
        return new WpfSize(w + border.Left + border.Right, h + border.Top + border.Bottom);
    }

    /// <summary>Stacks the group containers full-width (row-slot semantics per group).</summary>
    private WpfSize MeasureGrouped(WpfSize available)
    {
        var border = BorderThickness;
        var w = 0.0; var h = 0.0;
        var inner = new WpfSize(Math.Max(0, available.Width - border.Left - border.Right), available.Height);
        foreach (var group in _groupItems)
        {
            group.Measure(inner);
            w = Math.Max(w, group.DesiredSize.Width);
            h += group.DesiredSize.Height;
        }
        return new WpfSize(w + border.Left + border.Right, h + border.Top + border.Bottom);
    }

    /// <summary>Arranges the group containers full-width inside the frame chrome.</summary>
    private void ArrangeGrouped(WpfRect finalRect)
    {
        var t = EffectiveBorder;
        var y = finalRect.Y + t.Top;
        var slotW = Math.Max(0, finalRect.Width - t.Left - t.Right);
        foreach (var group in _groupItems)
        {
            var childH = group.DesiredSize.Height;
            group.Arrange(new WpfRect(finalRect.X + t.Left, y, slotW, childH));
            y += childH;
        }
    }

    /// <summary>
    /// Converted-host arrange inside the frame chrome (matches the engine
    /// ItemsControl host path): the panel gets the full inner width at its
    /// desired height so converted centering math (HoneycombPanel centers on
    /// the final width) sees the real slot.
    /// </summary>
    private void ArrangeCustomHost(WpfRect finalRect)
    {
        var t = EffectiveBorder;
        _customHost!.Arrange(new WpfRect(
            finalRect.X + t.Left, finalRect.Y + t.Top,
            Math.Max(0, finalRect.Width - t.Left - t.Right),
            Math.Max(0, _customHost.DesiredSize.Height)));
    }

    /// <summary>
    /// Horizontal-stack arrange over the generated rows inside the frame
    /// chrome (matches a horizontal StackPanel host: rows sit side by side
    /// at content width with vertical-alignment docking).
    /// </summary>
    private void ArrangeStacked(WpfRect finalRect)
    {
        var t = EffectiveBorder;
        var x = finalRect.X + t.Left;
        var slotH = Math.Max(0, finalRect.Height - t.Top - t.Bottom);
        foreach (var c in Items)
        {
            var m = GetMargin(c);
            var va = c is FrameworkElement fe ? fe.VerticalAlignment : VerticalAlignment.Top;
            var innerH = Math.Max(0, slotH - m.Top - m.Bottom);
            var childH = Math.Min(innerH, c.DesiredSize.Height);
            var offset = va switch
            {
                VerticalAlignment.Center => (innerH - childH) / 2,
                VerticalAlignment.Bottom => innerH - childH,
                _ => 0.0,
            };
            var childW = c.DesiredSize.Width;
            c.Arrange(new WpfRect(x + m.Left, finalRect.Y + t.Top + m.Top + offset, childW, childH));
            x += m.Left + childW + m.Right;
        }
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        // A set Template fills the slot (matches Button/ScrollViewer); the
        // rows keep the legacy arrangement below (they parent to the list,
        // not into the template).
        if (TemplateRoot is not null)
            TemplateRoot.Arrange(finalRect);
        // Grouped containers stack full-width; flat rows keep the legacy path.
        if (_groupItems.Count > 0)
        {
            ArrangeGrouped(finalRect);
            return;
        }
        // The converted host positions its rows through converted
        // ArrangeOverride (see Slice111HandyPanels); the WrapPanel host runs
        // the shared wrap flow over the rows at content size (no slot
        // stretch); the horizontal StackPanel host stacks left-to-right;
        // anything else stacks below.
        if (_customHost is not null)
        {
            ArrangeCustomHost(finalRect);
            return;
        }
        if (_wrapOrientation is { } wrap)
        {
            var frame = EffectiveBorder;
            WrapPanel.ArrangeFlow(Items, new WpfRect(
                finalRect.X + frame.Left, finalRect.Y + frame.Top,
                Math.Max(0, finalRect.Width - frame.Left - frame.Right),
                Math.Max(0, finalRect.Height - frame.Top - frame.Bottom)), wrap);
            return;
        }
        if (_stackHorizontal)
        {
            ArrangeStacked(finalRect);
            return;
        }
        // StackPanel slot semantics: Stretch (the default) fills the slot so
        // clicks and the selection highlight span the full row width.
        // Rows live inside the frame chrome (matches ScrollViewer): the
        // legacy 1px frame when BorderThickness is unset, the set edges
        // otherwise.
        var t = EffectiveBorder;
        var y = finalRect.Y + t.Top;
        foreach (var c in Items)
        {
            var m = GetMargin(c);
            var ha = c is FrameworkElement fe ? fe.HorizontalAlignment : HorizontalAlignment.Stretch;
            var slotW = Math.Max(0, finalRect.Width - t.Left - t.Right - m.Left - m.Right);
            var childW = ha == HorizontalAlignment.Stretch ? slotW : Math.Min(slotW, c.DesiredSize.Width);
            var offset = ha switch
            {
                HorizontalAlignment.Center => (slotW - childW) / 2,
                HorizontalAlignment.Right => slotW - childW,
                _ => 0.0,
            };
            var x = finalRect.X + t.Left + m.Left + offset;
            var childH = c.DesiredSize.Height;
            c.Arrange(new WpfRect(x, y + m.Top, childW, childH));
            y += m.Top + childH + m.Bottom;
        }
    }

    private static Thickness GetMargin(UIElement c) =>
        c is FrameworkElement fe ? fe.Margin : new Thickness(0);

    protected override void OnRender(DrawingContext dc)
    {
        // A set Template paints its chrome in place of the default
        // fill/frame (matches Button); the rows still paint through the
        // loop below, because generated rows parent to the list, not into
        // the template.
        if (TemplateRoot is not null)
            RenderVisible(dc, TemplateRoot);
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        var t = EffectiveBorder;
        if (TemplateRoot is null)
        {
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, FillRgba);
            PaintFrame(dc, r, t, FrameRgba);
        }
        var innerW = r.Width - t.Left - t.Right;
        var innerH = r.Height - t.Top - t.Bottom;
        if (innerW <= 0 || innerH <= 0) return;
        // Overflow clip: v1 has no internal scroller, so a height-clamped
        // list paints inside its rect only (nest in a ScrollViewer to scroll).
        dc.PushClip((float)(r.X + t.Left), (float)(r.Y + t.Top), (float)Math.Max(0, innerW), (float)Math.Max(0, innerH));
        var enabled = IsEffectivelyEnabled;
        if (_groupItems.Count > 0)
        {
            var selected = SelectedItem;
            if (selected is not null)
            {
                var sr = selected.RenderRect;
                dc.DrawRectangle((float)sr.X, (float)sr.Y, (float)sr.Width, (float)sr.Height,
                    enabled ? 0x0078D4FFu : 0x909090FFu);
            }
            foreach (UIElement child in Children)
                if (!ReferenceEquals(child, TemplateRoot))
                    RenderVisible(dc, child);
            dc.PopClip();
            return;
        }
        if (_customHost is not null)
        {
            foreach (var item in Items)
            {
                if (item.IsSelected)
                {
                    var ir = item.RenderRect;
                    dc.DrawRectangle((float)ir.X, (float)ir.Y, (float)ir.Width, (float)ir.Height,
                        enabled ? 0x0078D4FFu : 0x909090FFu);
                }
            }
            RenderVisible(dc, _customHost);
            dc.PopClip();
            return;
        }
        foreach (var item in Items)
        {
            if (item.IsSelected)
            {
                var ir = item.RenderRect;
                dc.DrawRectangle((float)ir.X, (float)ir.Y, (float)ir.Width, (float)ir.Height,
                    enabled ? 0x0078D4FFu : 0x909090FFu);
            }
            RenderVisible(dc, item);
        }
        dc.PopClip();
    }

    /// <summary>
    /// Paints the frame inside the list edges (uniform and non-uniform
    /// alike), so a transparent row Background keeps the list fill visible
    /// inside the ring.
    /// </summary>
    private static void PaintFrame(DrawingContext dc, WpfRect r, Thickness t, uint frame)
    {
        var left = Math.Min(t.Left, r.Width);
        var right = Math.Min(t.Right, r.Width);
        var top = Math.Min(t.Top, r.Height);
        var bottom = Math.Min(t.Bottom, r.Height);
        if (left > 0)
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)left, (float)r.Height, frame);
        if (right > 0)
            dc.DrawRectangle((float)(r.X + r.Width - right), (float)r.Y, (float)right, (float)r.Height, frame);
        if (top > 0)
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)top, frame);
        if (bottom > 0)
            dc.DrawRectangle((float)r.X, (float)(r.Y + r.Height - bottom), (float)r.Width, (float)bottom, frame);
    }
}

/// <summary>
/// TreeViewItem: one hierarchical node. The header paints like a list row;
/// child TreeViewItems form the subtree. The owning <see cref="TreeView"/>
/// paints selection and expander affordances and arranges visible rows.
/// </summary>
public sealed class TreeViewItem : HeaderedItemsControl
{
    private object? _header = string.Empty;

    /// <summary>
    /// Node header (matches HeaderedItemsControl.Header): strings render as
    /// text; a UIElement header hosts as the row visual (data-template roots
    /// land here with the data item as DataContext).
    /// </summary>
    public new object? Header
    {
        get => _header;
        set
        {
            if (Equals(_header, value)) return;
            if (_header is UIElement oldVisual) DetachVisualChild(oldVisual);
            _header = value;
            if (value is UIElement visual) AttachVisualChild(visual);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private uint _foregroundRgba = 0x000000FF;

    /// <summary>Text ink for the unselected state (paint-only change).</summary>
    public uint ForegroundRgba
    {
        get => _foregroundRgba;
        set
        {
            if (_foregroundRgba == value) return;
            _foregroundRgba = value;
            InvalidateVisual();
        }
    }

    private string _fontCss = "14px sans-serif";

    private Brush? _foreground;

    /// <summary>Node ink brush (null keeps the default; solid brushes repaint).</summary>
    public new Brush? Foreground
    {
        get => _foreground;
        set { _foreground = value; if (value is SolidColorBrush solid) ForegroundRgba = solid.ToRgba(); InvalidateVisual(); }
    }

    /// <summary>Node font (changes measured size).</summary>
    public string FontCss
    {
        get => _fontCss;
        set
        {
            if (_fontCss == value) return;
            _fontCss = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Child nodes in order (TreeViewItem children only; header visuals excluded).</summary>
    public new IReadOnlyList<TreeViewItem> Items
    {
        get
        {
            var items = new List<TreeViewItem>();
            foreach (UIElement c in Children)
                if (c is TreeViewItem item) items.Add(item);
            return items;
        }
    }

    /// <summary>Whether child nodes exist (header visuals are not items).</summary>
    public new bool HasItems => Items.Count > 0;

    private bool _isExpanded;

    /// <summary>
    /// Whether children are visible (default collapsed, as in WPF). A hidden
    /// selection is kept (as in WPF); it reappears when re-expanded.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            InvalidateMeasure();
            (value ? Expanded : Collapsed)?.Invoke();
        }
    }

    /// <summary>Raised when this node expands.</summary>
    public event Action? Expanded;

    /// <summary>Raised when this node collapses.</summary>
    public event Action? Collapsed;

    /// <summary>
    /// Appends a child node. Only TreeViewItem children are accepted (loader
    /// boundaries reject anything else first; this is the programmatic
    /// backstop). Visible-row changes invalidate up the parent chain.
    /// </summary>
    public new void AddChild(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child is not TreeViewItem)
            throw new InvalidOperationException(
                $"<TreeViewItem> nodes must be <TreeViewItem> (have <{child.GetType().Name}>).");
        base.AddChild(child);
    }

    /// <summary>
    /// Removes a child node. Detaching the selected node (directly or via
    /// this subtree) clears the owner's selection with an event.
    /// </summary>
    public new bool RemoveChild(UIElement child)
    {
        var owner = OwnerTree;
        var old = owner?.SelectedItem;
        var removed = base.RemoveChild(child);
        if (removed && owner is not null && !ReferenceEquals(owner.SelectedItem, old))
            owner.NotifySelectionChanged();
        return removed;
    }

    /// <summary>Owning tree found by walking up past TreeViewItem ancestors (null when detached).</summary>
    internal TreeView? OwnerTree
    {
        get
        {
            var p = Parent;
            while (p is TreeViewItem tvi) p = tvi.Parent;
            return p as TreeView;
        }
    }

    /// <summary>
    /// Whether the owning tree currently selects this node (derived from the
    /// tree, so it can never go stale when rows are added, removed or
    /// hidden around it; matches TreeViewItem.IsSelected).
    /// </summary>
    public bool IsSelected
    {
        get => OwnerTree?.SelectedNode is { } node && ReferenceEquals(node, this);
        set
        {
            var owner = OwnerTree;
            if (owner is null) return;
            if (value) owner.SelectedItem = owner.ItemContainerGenerator.ItemFromContainer(this) ?? this;
            else if (IsSelected) owner.SelectedItem = null;
        }
    }

    /// <summary>Nesting depth (root nodes are 0), derived from TreeViewItem ancestors.</summary>
    internal int Depth
    {
        get
        {
            var depth = 0;
            for (var p = Parent; p is TreeViewItem tvi; p = tvi.Parent) depth++;
            return depth;
        }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        // Template-inflated headers measure their visual; strings use the
        // shaped-text row (the legacy 28px minimum keeps rows tappable).
        if (Header is UIElement visual)
        {
            visual.Measure(new WpfSize(Math.Max(0, available.Width), available.Height));
            var desired = visual.DesiredSize;
            return new WpfSize(Math.Min(available.Width, Math.Max(0, desired.Width)), Math.Max(0, desired.Height));
        }
        var text = Header?.ToString() ?? string.Empty;
        var (shapedW, shapedH) = TryMeasure(text, FontCss, text.Length * 8, 16);
        return new(
            Math.Min(available.Width, Math.Max(0, shapedW + 12)),
            Math.Max(28, shapedH + 10));
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (Header is UIElement visual)
        {
            var offset = Depth * TreeView.IndentPerLevel + TreeView.TextOffset;
            visual.Arrange(new WpfRect(finalRect.X + offset, finalRect.Y,
                Math.Max(0, finalRect.Width - offset), finalRect.Height));
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Template headers paint their hosted visual; otherwise the header
        // text paints (the tree paints the highlight and expander).
        // Indent reserves one 16px gutter per level plus the expander gutter.
        if (Header is UIElement visual)
        {
            RenderVisible(dc, visual);
            return;
        }
        var text = Header?.ToString() ?? string.Empty;
        var r = RenderRect;
        if (!string.IsNullOrEmpty(text) && r.Width > 0 && r.Height > 0)
            dc.DrawText(text, (float)(r.X + Depth * TreeView.IndentPerLevel + TreeView.TextOffset), (float)(r.Y + r.Height / 2), FontCss,
                IsSelected ? 0xFFFFFFFF : ForegroundRgba, 0, 2, -1);
    }

    /// <summary>
    /// Prepares a generated node (matches PrepareContainerForItemOverride):
    /// the data item becomes DataContext, the effective template inflates
    /// the header visual and a hierarchical template feeds child items.
    /// Direct nodes (the node itself as item) are left untouched.
    /// </summary>
    internal static void PrepareDataNode(TreeViewItem node, object item, DataTemplate? template)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(item);
        node.DataContext = item;
        var effective = template ?? node.ItemTemplate ?? node.FindAncestorTemplate();
        if (effective is not null && node.ItemTemplate is null)
            node.ItemTemplate = effective;
        if (effective is not null)
        {
            var visual = effective.LoadContent();
            if (visual is FrameworkElement fe) fe.DataContext = item;
            node.Header = visual;
        }
        else node.Header = item.ToString() ?? string.Empty;
        if (effective is HierarchicalDataTemplate hierarchical && hierarchical.ItemsSource?.Path?.Path is string path
            && !string.IsNullOrEmpty(path)
            && BindingExpression.TryRead(item, path, out var children) && children is System.Collections.IEnumerable enumerable
            && children is not string)
            node.ItemsSource = enumerable;
    }

    /// <summary>Clears a generated node (matches ClearContainerForItemOverride).</summary>
    internal void ClearDataNode()
    {
        ItemsSource = null;
        DataContext = null;
        Header = string.Empty;
    }

    private DataTemplate? FindAncestorTemplate()
    {
        for (var p = Parent as FrameworkElement; p is not null; p = p.Parent as FrameworkElement)
        {
            if (p is TreeViewItem tvi && tvi.ItemTemplate is not null) return tvi.ItemTemplate;
            if (p is TreeView tree && tree.ItemTemplate is not null) return tree.ItemTemplate;
        }
        return null;
    }

    protected internal override DependencyObject GetContainerForItemOverride() => new TreeViewItem();

    protected internal override bool IsItemItsOwnContainerOverride(object item) => item is TreeViewItem;

    protected internal override void PrepareContainerForItemOverride(DependencyObject container, object item)
    {
        if (container is not TreeViewItem node || ReferenceEquals(node, item)) return;
        PrepareDataNode(node, item, node.ItemTemplate);
    }

    protected internal override void ClearContainerForItemOverride(DependencyObject container, object item)
    {
        if (container is TreeViewItem node && !ReferenceEquals(node, item)) node.ClearDataNode();
    }
}

/// <summary>
/// TreeView (matches System.Windows.Controls.TreeView): hierarchical nodes
/// with expand/collapse and single selection over the flattened visible
/// rows. Like real WPF it derives from ItemsControl (not Selector), so
/// there is no SelectedIndex; SelectedItem is the data item (direct nodes
/// map to themselves). ItemsSource plus a HierarchicalDataTemplate
/// generates nodes with inflated header visuals and recursive children.
/// No internal scrolling (nest in a ScrollViewer or size to content);
/// virtualization, multi-selection and drag-reorder are out of scope.
/// </summary>
/// <summary>
/// Hierarchical container map (matches the WPF tree generator): nested data
/// items resolve through their realized parent-node generators, so the root
/// generator answers ContainerFromItem/ItemFromContainer for the whole tree.
/// Gallery card navigation depends on this (nested Button/CheckBox items).
/// </summary>
internal sealed class TreeViewContainerGenerator : ItemContainerGenerator
{
    private readonly TreeView _tree;

    internal TreeViewContainerGenerator(TreeView tree) : base(tree) => _tree = tree;

    public override DependencyObject? ContainerFromItem(object? item)
    {
        if (item is null) return null;
        if (TryGetContainer(item, out var direct)) return direct;
        foreach (var node in AllNodes())
            if (node.ItemContainerGenerator.TryGetContainer(item, out var nested))
                return nested;
        return base.ContainerFromItem(item);
    }

    public override object? ItemFromContainer(DependencyObject container)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (TryGetItem(container, out var direct)) return direct;
        foreach (var node in AllNodes())
            if (node.ItemContainerGenerator.TryGetItem(container, out var nested))
                return nested;
        return null;
    }

    private List<TreeViewItem> AllNodes()
    {
        var found = new List<TreeViewItem>();
        foreach (var root in _tree.Items) AppendNode(root, found);
        return found;
    }

    private static void AppendNode(TreeViewItem node, List<TreeViewItem> found)
    {
        found.Add(node);
        foreach (var child in node.Items) AppendNode(child, found);
    }

    /// <summary>Non-creating hierarchical lookup (never fabricates nodes).</summary>
    internal bool TryResolveNode(object? item, out TreeViewItem? node)
    {
        node = null;
        if (item is null) return false;
        if (TryGetContainer(item, out var direct) && direct is TreeViewItem top)
        {
            node = top;
            return true;
        }
        foreach (var parent in AllNodes())
            if (parent.ItemContainerGenerator.TryGetContainer(item, out var nested) &&
                nested is TreeViewItem child)
            {
                node = child;
                return true;
            }
        return false;
    }
}

public class TreeView : ItemsControl
{
    public TreeView() => ItemContainerGenerator = new TreeViewContainerGenerator(this);

    /// <summary>Indent per nesting level, in px.</summary>
    internal const double IndentPerLevel = 16;

    /// <summary>Text offset past the indent gutters (expander gutter + pad), in px.</summary>
    internal const double TextOffset = 20;

    private const double ExpanderBox = 9;

    /// <summary>Root nodes in order (TreeViewItem children only; header visuals excluded).</summary>
    public new IReadOnlyList<TreeViewItem> Items
    {
        get
        {
            var items = new List<TreeViewItem>();
            foreach (UIElement c in Children)
                if (c is TreeViewItem item) items.Add(item);
            return items;
        }
    }

    /// <summary>Data items behind the root nodes (matches ItemsControl.Items).</summary>
    internal ItemCollection DataItems => ((ItemsControl)this).Items;

    /// <summary>Visible rows in order (expanded subtrees flattened).</summary>
    public IReadOnlyList<TreeViewItem> VisibleRows
    {
        get
        {
            var rows = new List<TreeViewItem>();
            foreach (var item in Items) AppendVisible(item, rows);
            return rows;
        }
    }

    private static void AppendVisible(TreeViewItem item, List<TreeViewItem> rows)
    {
        rows.Add(item);
        if (!item.IsExpanded) return;
        foreach (var child in item.Items) AppendVisible(child, rows);
    }

    private TreeViewItem? _selected;

    /// <summary>Selected node (null when nothing is selected or it detached).</summary>
    internal TreeViewItem? SelectedNode =>
        _selected is not null && ReferenceEquals(_selected.OwnerTree, this) ? _selected : null;

    /// <summary>
    /// Selected data item (matches TreeView.SelectedItem): the data behind
    /// the selected node (direct nodes map to themselves). Any owned node
    /// can be selected, including hidden ones, as in WPF; detaching the node
    /// clears the selection. Setting an unknown item clears, like WPF.
    /// </summary>
    public object? SelectedItem
    {
        get
        {
            var node = SelectedNode;
            if (node is null) return null;
            return ItemContainerGenerator.ItemFromContainer(node) ?? node;
        }
        set
        {
            var next = ResolveNode(value);
            if (ReferenceEquals(_selected, next)) return;
            _selected = next;
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }
    }

    private TreeViewItem? ResolveNode(object? value)
    {
        if (value is null) return null;
        if (value is TreeViewItem node) return ReferenceEquals(node.OwnerTree, this) ? node : null;
        // Data items resolve through realized containers only (nested data
        // lives in parent-node generators, not the top data list):
        // resolving an unknown item must not fabricate (and attach) a node.
        if (ItemContainerGenerator is TreeViewContainerGenerator hierarchical &&
            hierarchical.TryResolveNode(value, out var nested))
            return nested;
        return null;
    }

    protected internal override DependencyObject GetContainerForItemOverride() => new TreeViewItem();

    protected internal override bool IsItemItsOwnContainerOverride(object item) => item is TreeViewItem;

    protected internal override void PrepareContainerForItemOverride(DependencyObject container, object item)
    {
        if (container is not TreeViewItem node || ReferenceEquals(node, item)) return;
        TreeViewItem.PrepareDataNode(node, item, ItemTemplate);
    }

    protected internal override void ClearContainerForItemOverride(DependencyObject container, object item)
    {
        if (container is TreeViewItem node && !ReferenceEquals(node, item)) node.ClearDataNode();
    }

    /// <summary>Visible-row index of a node (-1 when hidden or absent).</summary>
    private int VisibleIndexOf(TreeViewItem? node)
    {
        if (node is null) return -1;
        var rows = VisibleRows;
        for (var i = 0; i < rows.Count; i++)
            if (ReferenceEquals(rows[i], node)) return i;
        return -1;
    }

    /// <summary>
    /// Appends a root node. Only TreeViewItem nodes are accepted (loader
    /// boundaries reject anything else first; this is the programmatic
    /// backstop).
    /// </summary>
    public new void AddChild(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child is not TreeViewItem)
            throw new InvalidOperationException(
                $"<TreeView> nodes must be <TreeViewItem> (have <{child.GetType().Name}>).");
        base.AddChild(child);
    }

    /// <summary>
    /// Removes a root node. Detaching the selected node (directly or via
    /// this subtree) clears the selection with an event.
    /// </summary>
    public new bool RemoveChild(UIElement child)
    {
        var old = SelectedItem;
        var removed = base.RemoveChild(child);
        if (removed && !ReferenceEquals(SelectedItem, old))
            NotifySelectionChanged();
        return removed;
    }

    /// <summary>Repaints and raises <see cref="SelectionChanged"/> (nested-removal path).</summary>
    internal void NotifySelectionChanged()
    {
        InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    /// <summary>Raised after the selection actually changes.</summary>
    public event Action? SelectionChanged;

    /// <summary>Trees take focus (store-backed; the Control default is true).</summary>
    public override bool Focusable
    {
        get => (bool?)GetValue(FocusableProperty) ?? true;
        set => SetValue(FocusableProperty, value);
    }

    /// <summary>Selects a visible row by hit position; false when no row covers it.</summary>
    public bool SelectAt(double x, double y)
    {
        var rows = VisibleRows;
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].HitTest(new WpfPoint(x, y))) { SelectedItem = rows[i]; return true; }
        return false;
    }

    /// <summary>Expander gutter rect for a row rect (generous click target).</summary>
    private static WpfRect ExpanderHitRect(WpfRect row, int depth) =>
        new(row.X + depth * IndentPerLevel, row.Y, IndentPerLevel, row.Height);

    /// <summary>Toggles the row under the point when it has children; false otherwise.</summary>
    public bool ToggleAt(double x, double y)
    {
        var rows = VisibleRows;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (!row.HasItems) continue;
            var hit = ExpanderHitRect(row.RenderRect, row.Depth);
            if (x >= hit.X && x < hit.X + hit.Width && y >= hit.Y && y < hit.Y + hit.Height)
            {
                row.IsExpanded = !row.IsExpanded;
                return true;
            }
        }
        return false;
    }

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        // Press on the expander gutter toggles; press elsewhere selects (WPF
        // selects on press, not release). Disabled trees ignore all input.
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        if (e.Source is not Core.WasmInputEvent ev) return;
        switch (ev)
        {
            case Core.WasmInputEvent.Pointer pd when pd.Args.Kind == Core.WasmPointerKind.Down && e.IsLeftButtonDown:
                if (ToggleAt(pd.Args.X, pd.Args.Y)) { e.Handled = true; break; }
                if (SelectAt(pd.Args.X, pd.Args.Y)) e.Handled = true;
                break;
            case Core.WasmInputEvent.Key k when k.Args.IsDown && _focused:
                HandleKey(k.Args.Key, e);
                break;
        }
    }

    private void HandleKey(string key, WasmRoutedEvent e)
    {
        // Navigation runs over visible rows; a hidden selection behaves
        // like no selection (Down/Up land on the first/last visible row).
        var rows = VisibleRows;
        if (rows.Count == 0) return;
        var index = VisibleIndexOf(SelectedNode);
        switch (key)
        {
            case "ArrowDown": SelectedItem = rows[index < 0 ? 0 : Math.Min(rows.Count - 1, index + 1)]; e.Handled = true; break;
            case "ArrowUp": SelectedItem = rows[index < 0 ? rows.Count - 1 : Math.Max(0, index - 1)]; e.Handled = true; break;
            case "Home": SelectedItem = rows[0]; e.Handled = true; break;
            case "End": SelectedItem = rows[^1]; e.Handled = true; break;
            case "ArrowRight":
                if (index >= 0)
                {
                    var row = rows[index];
                    if (row.HasItems && !row.IsExpanded) { row.IsExpanded = true; e.Handled = true; }
                    else if (row is { HasItems: true, IsExpanded: true }) { SelectedItem = rows[index + 1]; e.Handled = true; }
                }
                break;
            case "ArrowLeft":
                if (index >= 0)
                {
                    var row = rows[index];
                    if (row.IsExpanded) { row.IsExpanded = false; e.Handled = true; }
                    else if (row.Parent is TreeViewItem parent) { SelectedItem = parent; e.Handled = true; }
                }
                break;
        }
    }

    private bool _focused;
    public override void OnGotFocus() { _focused = true; InvalidateVisual(); }
    public override void OnLostFocus() { _focused = false; InvalidateVisual(); }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var w = 0.0; var h = 0.0;
        foreach (var row in VisibleRows)
        {
            var m = GetMargin(row);
            var indent = row.Depth * IndentPerLevel + TextOffset;
            row.Measure(new WpfSize(Math.Max(0, available.Width - m.Left - m.Right - indent), available.Height));
            w = Math.Max(w, row.DesiredSize.Width + m.Left + m.Right + indent);
            h += row.DesiredSize.Height + m.Top + m.Bottom;
        }
        return new WpfSize(w, h);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        // Full-width rows like ListBox so clicks and highlights span the slot.
        // Rows live inside the 1px border chrome (matches ScrollViewer).
        var y = finalRect.Y + 1;
        foreach (var row in VisibleRows)
        {
            var m = GetMargin(row);
            var slotW = Math.Max(0, finalRect.Width - 2 - m.Left - m.Right);
            var x = finalRect.X + 1 + m.Left;
            var childH = row.DesiredSize.Height;
            row.Arrange(new WpfRect(x, y + m.Top, slotW, childH));
            y += m.Top + childH + m.Bottom;
        }
    }

    private static Thickness GetMargin(UIElement c) =>
        c is FrameworkElement fe ? fe.Margin : new Thickness(0);

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, 0x707070FF);
        if (r.Width > 2 && r.Height > 2)
            dc.DrawRectangle((float)r.X + 1, (float)r.Y + 1, (float)r.Width - 2, (float)r.Height - 2, 0xFFFFFFFF);
        if (r.Width <= 2 || r.Height <= 2) return;
        // Overflow clip: v1 has no internal scroller (nest in a ScrollViewer).
        dc.PushClip((float)r.X + 1, (float)r.Y + 1, (float)Math.Max(0, r.Width - 2), (float)Math.Max(0, r.Height - 2));
        var enabled = IsEffectivelyEnabled;
        foreach (var row in VisibleRows)
        {
            if (row.IsSelected)
            {
                var ir = row.RenderRect;
                dc.DrawRectangle((float)ir.X, (float)ir.Y, (float)ir.Width, (float)ir.Height,
                    enabled ? 0x0078D4FFu : 0x909090FFu);
            }
            DrawExpander(dc, row);
            RenderVisible(dc, row);
        }
        dc.PopClip();
    }

    private static void DrawExpander(DrawingContext dc, TreeViewItem row)
    {
        // Classic +/- box (rectangles + ASCII text only: the Canvas2D
        // transport skips geometry ops, and glyph coverage for triangles
        // cannot be assumed in headless fonts).
        if (!row.HasItems) return;
        var r = row.RenderRect;
        var cx = (float)(r.X + row.Depth * IndentPerLevel + (IndentPerLevel - ExpanderBox) / 2);
        var cy = (float)(r.Y + (r.Height - ExpanderBox) / 2);
        dc.DrawRectangle(cx, cy, (float)ExpanderBox, (float)ExpanderBox, 0x707070FF);
        dc.DrawRectangle(cx + 1, cy + 1, (float)ExpanderBox - 2, (float)ExpanderBox - 2, 0xFFFFFFFF);
        dc.DrawText(row.IsExpanded ? "-" : "+", cx + (float)ExpanderBox / 2, cy + (float)ExpanderBox / 2,
            "10px sans-serif", 0x000000FF, 1, 2, -1);
    }
}

/// <summary>
/// Text column (matches System.Windows.Controls.DataGridTextColumn):
/// the v1 column surface (Header/Width/BindingPath behavior pinned by
/// DataGrid/XAML/BAML tests) rebased onto <see cref="DataGridBoundColumn"/>
/// so converted code can treat it as a DataGridColumn and read back its
/// <see cref="DataGridBoundColumn.Binding"/>.
/// </summary>
// Unsealed like WPF: vendor columns (MaterialDesign DataGridTextColumn)
// derive from the engine column type.
public class DataGridTextColumn : DataGridBoundColumn
{
    private string _bindingPath = string.Empty;

    /// <summary>
    /// Property path read off each row object for cell text (engine
    /// extension; empty renders blank cells). A set
    /// <see cref="DataGridBoundColumn.Binding"/> path wins over this.
    /// </summary>
    public string BindingPath
    {
        get => _bindingPath;
        set
        {
            if (_bindingPath == value) return;
            _bindingPath = value ?? string.Empty;
            OnColumnChanged();
        }
    }

    /// <summary>Effective property path: Binding.Path.Path wins, else BindingPath.</summary>
    internal string EffectivePath
    {
        get
        {
            var bound = Binding?.Path?.Path;
            return !string.IsNullOrEmpty(bound) ? bound : _bindingPath;
        }
    }

    /// <summary>Cell text for a row object (missing path renders blank).</summary>
    internal string CellTextFor(object? row)
    {
        var path = EffectivePath;
        if (row is null || string.IsNullOrEmpty(path)) return string.Empty;
        return BindingExpression.TryRead(row, path, out var value) && value is not null
            ? value.ToString() ?? string.Empty
            : string.Empty;
    }

    protected internal override FrameworkElement? GenerateElement(DataGridCell cell, object? dataItem) =>
        new TextBlock { Text = CellTextFor(dataItem) };

    protected internal override FrameworkElement? GenerateEditingElement(DataGridCell cell, object? dataItem) =>
        new TextBox { Text = CellTextFor(dataItem) };
}

/// <summary>
/// DataGrid v2: read-only text table with single row selection (Selector
/// subset: SelectedIndex/SelectedItem/SelectionChanged) plus row/cell
/// containers and sorting. Rows are plain CLR data in <see cref="Items"/>
/// (a <see cref="DataGridItemCollection"/>: adds/removes re-measure and
/// track selection automatically; <see cref="SortDescription"/> rules
/// reorder the rendered view without touching source order); cells read
/// the column binding path via the binding engine's property reader.
/// <see cref="ItemContainerGenerator"/> hands out <see cref="DataGridRow"/>
/// containers (each hosting a <see cref="DataGridCellsPresenter"/> of
/// <see cref="DataGridCell"/> containers plus a
/// <see cref="DataGridDetailsPresenter"/>), matching the lookups converted
/// ScreenToGif code performs. Generated rows stay out of the grid's own
/// visual tree: the grid keeps painting itself. No internal scrolling
/// (nest in a ScrollViewer or size to content); cell/column selection,
/// editing, grouping layout, virtualization and auto-generated columns
/// are out of scope.
/// </summary>
public class DataGrid : Control, IItemContainerOwner
{
    /// <summary>Details-scrolling converter (matches
    /// DataGrid.RowDetailsScrollingConverter; the DataGrid template binds
    /// through it, like WPF).</summary>
    public static IValueConverter RowDetailsScrollingConverter { get; } = new BooleanToSelectiveScrollingOrientationConverter();

    /// <summary>Header-band visibility converter (matches
    /// DataGrid.HeadersVisibilityConverter; the DataGrid template binds
    /// through it, like WPF).</summary>
    public static IValueConverter HeadersVisibilityConverter { get; } = new DataGridHeadersVisibilityToVisibilityConverter();

    /// <summary>Select-all command (matches DataGrid.SelectAllCommand;
    /// execution routes through the grid's command bindings, like WPF).</summary>
    public static RoutedUICommand SelectAllCommand { get; } =
        new RoutedUICommand("Select All", nameof(SelectAllCommand), typeof(DataGrid));

    /// <summary>Details visibility mode token (matches
    /// DataGrid.RowDetailsVisibilityModeProperty; VisibleWhenSelected
    /// default, like WPF).</summary>
    public static readonly DependencyProperty RowDetailsVisibilityModeProperty =
        DependencyProperty.Register("RowDetailsVisibilityMode", typeof(DataGridRowDetailsVisibilityMode), typeof(DataGrid),
            new FrameworkPropertyMetadata(DataGridRowDetailsVisibilityMode.VisibleWhenSelected));

    /// <summary>Details visibility mode (VisibleWhenSelected default, like WPF).</summary>
    public DataGridRowDetailsVisibilityMode RowDetailsVisibilityMode
    {
        get => (DataGridRowDetailsVisibilityMode)(GetValue(RowDetailsVisibilityModeProperty) ?? DataGridRowDetailsVisibilityMode.VisibleWhenSelected);
        set => SetValue(RowDetailsVisibilityModeProperty, value);
    }

    /// <summary>Cells-panel horizontal offset token (matches
    /// DataGrid.CellsPanelHorizontalOffsetProperty; zero default, like WPF).</summary>
    public static readonly DependencyProperty CellsPanelHorizontalOffsetProperty =
        DependencyProperty.Register("CellsPanelHorizontalOffset", typeof(double), typeof(DataGrid),
            new FrameworkPropertyMetadata(0.0));

    /// <summary>Cells-panel horizontal offset (zero default, like WPF).</summary>
    public double CellsPanelHorizontalOffset
    {
        get => (double)(GetValue(CellsPanelHorizontalOffsetProperty) ?? 0.0);
        set => SetValue(CellsPanelHorizontalOffsetProperty, value);
    }

    /// <summary>New-item margin token (matches DataGrid.NewItemMarginProperty;
    /// zero default, like WPF).</summary>
    public static readonly DependencyProperty NewItemMarginProperty =
        DependencyProperty.Register("NewItemMargin", typeof(Thickness), typeof(DataGrid),
            new FrameworkPropertyMetadata(new Thickness()));

    /// <summary>New-item margin (zero default, like WPF).</summary>
    public Thickness NewItemMargin
    {
        get => (Thickness)(GetValue(NewItemMarginProperty) ?? new Thickness());
        set => SetValue(NewItemMarginProperty, value);
    }

    private static readonly DependencyPropertyKey IsGroupingPropertyKey =
        DependencyProperty.RegisterReadOnly("IsGrouping", typeof(bool), typeof(DataGrid),
            new FrameworkPropertyMetadata(false));

    /// <summary>Grouping token (matches ItemsControl.IsGroupingProperty;
    /// read-only, false default, like WPF; registered on DataGrid so row
    /// triggers resolve it, like the native lookup).</summary>
    public static readonly DependencyProperty IsGroupingProperty =
        IsGroupingPropertyKey.DependencyProperty;

    /// <summary>Whether the items view groups (false until grouping lands;
    /// the DataGrid theme keys group visuals off this, like WPF).</summary>
    public bool IsGrouping => (bool)(GetValue(IsGroupingProperty) ?? false);

    private static readonly DependencyPropertyKey NonFrozenColumnsViewportHorizontalOffsetPropertyKey =
        DependencyProperty.RegisterReadOnly("NonFrozenColumnsViewportHorizontalOffset", typeof(double), typeof(DataGrid),
            new FrameworkPropertyMetadata(0.0));

    /// <summary>Non-frozen columns offset token (matches
    /// DataGrid.NonFrozenColumnsViewportHorizontalOffsetProperty; read-only,
    /// zero default, like WPF).</summary>
    public static readonly DependencyProperty NonFrozenColumnsViewportHorizontalOffsetProperty =
        NonFrozenColumnsViewportHorizontalOffsetPropertyKey.DependencyProperty;

    /// <summary>Non-frozen columns viewport offset (zero until
    /// horizontal-scroll tracking lands; the DataGrid template binds it).</summary>
    public double NonFrozenColumnsViewportHorizontalOffset
    {
        get => (double)(GetValue(NonFrozenColumnsViewportHorizontalOffsetProperty) ?? 0.0);
        set => SetValue(NonFrozenColumnsViewportHorizontalOffsetPropertyKey, value);
    }

    private readonly List<DataGridTextColumn> _columns = new();
    private readonly DataGridItemCollection _items = new();
    private List<object?> _view = new();
    private readonly Dictionary<object, DataGridRow> _rows = new(ReferenceEqualityComparer.Instance);
    private int _selectedIndex = -1;
    private object? _selectedItem;
    private List<double> _colWidths = new();
    private List<WpfRect> _rowRects = new();
    private double _headerHeight;

    public DataGrid()
    {
        _view = new List<object?>(_items);
        ItemContainerGenerator = new ItemContainerGenerator(this);
        Items.IsDirectEditAllowed = () => _itemsSource is null || _syncingFromSource;
        _items.CollectionChanged += OnItemsChanged;
        _items.SortDescriptions.CollectionChanged += OnSortDescriptionsChanged;
        _selectedItems.CollectionChanged += OnSelectedItemsChanged;
    }

    /// <summary>Row containers by data item (created on demand).</summary>
    public ItemContainerGenerator ItemContainerGenerator { get; }

    /// <summary>Columns in order (add via <see cref="AddColumn"/>).</summary>
    public IReadOnlyList<DataGridTextColumn> Columns => _columns;

    /// <summary>Row objects with grid sort/group state (plain CLR data, not visuals).</summary>
    public DataGridItemCollection Items => _items;

    /// <summary>
    /// Source identity (matches System.Windows.Controls.ItemsControl.ItemsSourceProperty;
    /// registered on DataGrid so the loader, style setters and BAML bindings resolve
    /// the bindable property; the CLR wrapper keeps the snapshot flow and dual-lands
    /// the DP store so GetValue readers see CLR-applied sources too).
    /// </summary>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(DataGrid),
            new FrameworkPropertyMetadata(null));

    private IEnumerable? _itemsSource;
    private bool _syncingFromSource;
    private NotifyCollectionChangedEventHandler? _itemsSourceSubscription;

    /// <summary>
    /// Bound data rows (the converted gallery DataGrid page binds
    /// ItemsSource="{Binding ...}"): setting a collection fills
    /// <see cref="Items"/> with its rows (plain data, like direct rows),
    /// INotifyCollectionChanged sources push live adds/removes/replaces/
    /// moves/resets, and clearing (null) drops the rows. WPF parity guards:
    /// setting a source while direct rows exist throws, and direct
    /// <see cref="Items"/> edits while a source is set throw (the internal
    /// sync bypasses through the flag).
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => _itemsSource;
        set
        {
            if (ReferenceEquals(_itemsSource, value)) return;
            if (value is not null && _itemsSource is null && Items.Count > 0)
                throw new InvalidOperationException(
                    "Items collection must be empty before using ItemsSource.");
            DetachItemsSource();
            _itemsSource = value;
            SetValue(ItemsSourceProperty, value);
            _syncingFromSource = true;
            try
            {
                Items.Clear();
                if (value is not null)
                    foreach (var item in value) Items.Add(item!);
            }
            finally
            {
                _syncingFromSource = false;
            }
            AttachItemsSource(value);
            InvalidateMeasure();
        }
    }

    private void AttachItemsSource(IEnumerable? value)
    {
        if (value is INotifyCollectionChanged incc)
        {
            NotifyCollectionChangedEventHandler handler = (_, e) => SyncItemsSource(e);
            _itemsSourceSubscription = handler;
            incc.CollectionChanged += handler;
        }
    }

    private void DetachItemsSource()
    {
        if (_itemsSourceSubscription is not null && _itemsSource is INotifyCollectionChanged oldIncc)
            oldIncc.CollectionChanged -= _itemsSourceSubscription;
        _itemsSourceSubscription = null;
    }

    /// <summary>Mirrors source edits into <see cref="Items"/> (the item-changed
    /// handler rebuilds the view, reseats selection and repaints, like direct edits).</summary>
    private void SyncItemsSource(NotifyCollectionChangedEventArgs e)
    {
        if (_itemsSource is null) return;
        _syncingFromSource = true;
        try
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewStartingIndex >= 0 && e.NewItems is not null)
                    {
                        var at = e.NewStartingIndex;
                        foreach (var item in e.NewItems) Items.Insert(at++, item);
                    }
                    else if (e.NewItems is not null)
                    {
                        foreach (var item in e.NewItems) Items.Add(item);
                    }
                    break;
                case NotifyCollectionChangedAction.Remove:
                    if (e.OldStartingIndex >= 0 && e.OldItems is not null)
                    {
                        for (var i = 0; i < e.OldItems.Count; i++) Items.RemoveAt(e.OldStartingIndex);
                    }
                    else if (e.OldItems is not null)
                    {
                        foreach (var item in e.OldItems) Items.Remove(item);
                    }
                    break;
                case NotifyCollectionChangedAction.Replace:
                    if (e.NewStartingIndex >= 0 && e.NewItems is not null && e.NewItems.Count > 0)
                        Items[e.NewStartingIndex] = e.NewItems[0];
                    break;
                case NotifyCollectionChangedAction.Move:
                    if (e.OldStartingIndex >= 0 && e.NewStartingIndex >= 0)
                        Items.Move(e.OldStartingIndex, e.NewStartingIndex);
                    break;
                case NotifyCollectionChangedAction.Reset:
                default:
                    Items.Clear();
                    foreach (var item in _itemsSource) Items.Add(item!);
                    break;
            }
        }
        finally
        {
            _syncingFromSource = false;
        }
        InvalidateMeasure();
    }

    /// <summary>Rendered view order (source order without sort descriptions).</summary>
    internal IReadOnlyList<object?> View => _view;

    /// <summary>View index of an item (-1 when absent).</summary>
    internal int ViewIndexOf(object? item) => item is null ? -1 : _view.IndexOf(item);

    System.Collections.IList IItemContainerOwner.ContainerItems => _view;

    DataTemplate? IItemContainerOwner.ItemTemplate => null;

    DataTemplateSelector? IItemContainerOwner.ItemTemplateSelector => null;

    bool IItemContainerOwner.IsItemItsOwnContainer(object item) => item is DataGridRow;

    DependencyObject IItemContainerOwner.GetContainerForItem() => new DataGridRow(this);

    void IItemContainerOwner.PrepareContainerForItem(DependencyObject container, object? item)
    {
        if (container is not DataGridRow row) return;
        if (!ReferenceEquals(item, row)) row.Item = item;
        TrackRow(row);
        row.CellsPresenter.SyncColumns(_columns);
        row.SyncSelection();
        OnLoadingRow(new DataGridRowEventArgs(row));
    }

    void IItemContainerOwner.ClearContainerForItem(DependencyObject container, object item)
    {
        if (container is DataGridRow row) UntrackRow(row);
    }

    void IItemContainerOwner.AddGeneratedChild(DependencyObject container)
    {
        // Rows stay out of the grid's visual tree by design (the grid
        // paints itself); containers remain reachable via the generator.
    }

    void IItemContainerOwner.RemoveGeneratedChild(DependencyObject container)
    {
    }

    private void TrackRow(DataGridRow row)
    {
        if (row.Item is not null) _rows[row.Item] = row;
    }

    private void UntrackRow(DataGridRow row)
    {
        if (row.Item is not null) _rows.Remove(row.Item);
    }

    /// <summary>Realized row container for a data item (created on demand).</summary>
    internal DataGridRow? ContainerForItem(object? item) =>
        item is null ? null : ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;

    /// <summary>Resolved column widths from the last arrange.</summary>
    public IReadOnlyList<double> ColumnWidths => _colWidths;

    /// <summary>Row rects (header excluded) from the last arrange.</summary>
    public IReadOnlyList<WpfRect> RowRects => _rowRects;

    private string _fontCss = "14px sans-serif";

    /// <summary>Header and cell font (changes measured size).</summary>
    public string FontCss
    {
        get => _fontCss;
        set
        {
            if (_fontCss == value) return;
            _fontCss = value;
            InvalidateMeasure();
        }
    }

    private uint _foregroundRgba = 0x000000FF;

    /// <summary>Header and cell ink (paint-only change).</summary>
    public uint ForegroundRgba
    {
        get => _foregroundRgba;
        set
        {
            if (_foregroundRgba == value) return;
            _foregroundRgba = value;
            InvalidateVisual();
        }
    }

    /// <summary>Grid ink fast path (synced from Control.Foreground, like
    /// the menu chrome; solid brushes repaint).</summary>
    protected override void OnForegroundChanged(Brush? brush)
    {
        if (brush is SolidColorBrush solid) ForegroundRgba = solid.ToRgba();
    }

    /// <summary>Visible gridlines (matches System.Windows.Controls.DataGrid.GridLinesVisibility; default All like real WPF).</summary>
    public DataGridGridLinesVisibility GridLinesVisibility { get; set; } = DataGridGridLinesVisibility.All;

    /// <summary>Horizontal gridline ink (null keeps the legacy gray; the PC Space Map style sets it).</summary>
    public Brush? HorizontalGridLinesBrush { get; set; }

    /// <summary>Vertical gridline ink (null keeps the legacy gray).</summary>
    public Brush? VerticalGridLinesBrush { get; set; }

    /// <summary>Even-row fill override (null keeps rows transparent over the face).</summary>
    public Brush? RowBackground { get; set; }

    /// <summary>Odd-row fill override (null falls back to RowBackground).</summary>
    public Brush? AlternatingRowBackground { get; set; }

    /// <summary>Visible headers (matches System.Windows.Controls.DataGrid.HeadersVisibility; default All like real WPF).</summary>
    public DataGridHeadersVisibility HeadersVisibility { get; set; } = DataGridHeadersVisibility.All;

    private double _rowHeight = double.NaN;

    /// <summary>Fixed row height (NaN keeps content-sized rows like real WPF's default; the PC Space Map style sets 32).</summary>
    public double RowHeight
    {
        get => _rowHeight;
        set { _rowHeight = value; InvalidateMeasure(); }
    }

    /// <summary>Add-row affordance (parsed and retained; the engine has no inline row editor).</summary>
    public bool CanUserAddRows { get; set; } = true;

    /// <summary>Delete-row affordance (parsed and retained; the engine has no inline row deletion).</summary>
    public bool CanUserDeleteRows { get; set; } = true;

    /// <summary>Read-only gate (parsed and retained; the engine grid never opens cell editors).</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>Selection cardinality (parsed and retained; the engine selects single rows only).</summary>
    public DataGridSelectionMode SelectionMode { get; set; } = DataGridSelectionMode.Extended;

    /// <summary>Selection granularity (parsed and retained; the engine selects full rows only).</summary>
    public DataGridSelectionUnit SelectionUnit { get; set; } = DataGridSelectionUnit.FullRow;

    /// <summary>Row recycling hint (parsed and retained; the engine renders all rows).</summary>
    public bool EnableRowVirtualization { get; set; } = true;

    /// <summary>Column recycling hint (parsed and retained; the engine renders all columns).</summary>
    public bool EnableColumnVirtualization { get; set; } = true;

    /// <summary>
    /// Automatic columns (matches System.Windows.Controls.DataGrid.AutoGenerateColumns;
    /// default true like real WPF; retained: the engine renders declared
    /// <see cref="DataGridTextColumn"/> columns only, so the PC Space Map
    /// grids set it False explicitly).
    /// </summary>
    public bool AutoGenerateColumns { get; set; } = true;

    /// <summary>
    /// Double-press activation (matches System.Windows.Controls.Control.MouseDoubleClick;
    /// the PC Space Map grids reveal/inspect rows here).
    /// </summary>
    public event MouseButtonEventHandler? MouseDoubleClick;

    /// <summary>Double-press detection window (test seam; mirrors Button).</summary>
    internal static TimeSpan DoubleClickWindow { get; set; } = TimeSpan.FromMilliseconds(500);

    private DateTime _lastClickAt = DateTime.MinValue;
    private WpfPoint _lastClickAtPoint;

    /// <summary>Raises <see cref="MouseDoubleClick"/> for a qualifying second press.</summary>
    protected virtual void OnMouseDoubleClick(MouseButtonEventArgs e) => MouseDoubleClick?.Invoke(this, e);

    /// <summary>
    /// Double-press tracker (mirrors Button's window/point gate): a second
    /// qualifying press raises <see cref="MouseDoubleClick"/>.
    /// </summary>
    internal void TrackDoublePress(double x, double y)
    {
        var now = DateTime.UtcNow;
        if (now - _lastClickAt <= DoubleClickWindow &&
            Math.Abs(x - _lastClickAtPoint.X) < 4 &&
            Math.Abs(y - _lastClickAtPoint.Y) < 4)
        {
            _lastClickAt = DateTime.MinValue;
            OnMouseDoubleClick(new MouseButtonEventArgs(
                Mouse.PrimaryDevice, Environment.TickCount,
                MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
                MouseButtonState.Released, MouseButtonState.Released,
                MouseButton.Left, MouseButtonState.Pressed, 2));
        }
        else
        {
            _lastClickAt = now;
            _lastClickAtPoint = new WpfPoint(x, y);
        }
    }

    /// <summary>
    /// Appends a column (loader boundaries construct columns directly; this
    /// is the programmatic path).
    /// </summary>
    public void AddColumn(DataGridTextColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        _columns.Add(column);
        column.SetOwner(this);
        ReindexColumns();
        SyncColumnsToRows();
        InvalidateMeasure();
    }

    /// <summary>Removes a column (clears its owner link).</summary>
    public bool RemoveColumn(DataGridTextColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        var removed = _columns.Remove(column);
        if (removed)
        {
            column.SetOwner(null);
            column.DisplayIndex = -1;
            ReindexColumns();
            SyncColumnsToRows();
            InvalidateMeasure();
        }
        return removed;
    }

    private void ReindexColumns()
    {
        for (var i = 0; i < _columns.Count; i++) _columns[i].DisplayIndex = i;
    }

    private void SyncColumnsToRows()
    {
        foreach (var row in _rows.Values) row.CellsPresenter.SyncColumns(_columns);
    }

    private DataGridCellInfo _currentCell = DataGridCellInfo.Unset;

    /// <summary>Current cell address (unset until assigned; setting it syncs CurrentColumn).</summary>
    public DataGridCellInfo CurrentCell
    {
        get => _currentCell;
        set
        {
            _currentCell = value;
            CurrentColumn = value.Column;
        }
    }

    /// <summary>Current column (follows CurrentCell; independently settable).</summary>
    public DataGridColumn? CurrentColumn { get; set; }

    /// <summary>
    /// Realizes the row container for an item (no-op for null or unknown
    /// items; the v1 grid has no scroller to bring it into view).
    /// </summary>
    public void ScrollIntoView(object? item)
    {
        if (item is null) return;
        UpdateLayout();
        ContainerForItem(item);
    }

    /// <summary>
    /// Realizes the row and cell containers for an item plus column
    /// (no-op for nulls or unknown pairs).
    /// </summary>
    public void ScrollIntoView(object? item, DataGridColumn? column)
    {
        if (item is null || column is null) return;
        UpdateLayout();
        ContainerForItem(item)?.CellsPresenter.ContainerForColumn(column);
    }

    private bool SortActive => _items.SortDescriptions.Count > 0;

    /// <summary>Rebuilds the rendered view (sorted when descriptions exist).</summary>
    private void RefreshView()
    {
        if (!SortActive)
        {
            _view = new List<object?>(_items);
            return;
        }
        var ordered = _items
            .Select((item, index) => (item, index))
            .OrderBy(entry => entry, Comparer<(object? item, int index)>.Create(CompareEntries))
            .Select(entry => entry.item)
            .ToList();
        _view = ordered;
    }

    private int CompareEntries((object? item, int index) left, (object? item, int index) right)
    {
        foreach (var sort in _items.SortDescriptions)
        {
            var c = CompareSortValues(ReadSortValue(left.item, sort.PropertyName), ReadSortValue(right.item, sort.PropertyName));
            if (c != 0) return sort.Direction == ListSortDirection.Ascending ? c : -c;
        }
        return left.index.CompareTo(right.index);
    }

    private static object? ReadSortValue(object? item, string propertyName)
    {
        if (item is null || string.IsNullOrEmpty(propertyName)) return null;
        return BindingExpression.TryRead(item, propertyName, out var value) ? value : null;
    }

    private static int CompareSortValues(object? left, object? right)
    {
        if (left is null) return right is null ? 0 : -1;
        if (right is null) return 1;
        if (left.GetType() == right.GetType() && left is IComparable comparable)
        {
            try { return comparable.CompareTo(right); }
            catch (ArgumentException) { }
        }
        return string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }

    private void OnSortDescriptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var oldIndex = _effIndex;
        var oldItem = _effItem;
        RefreshView();
        _selectedIndex = _selectedItem is null ? -1 : _view.IndexOf(_selectedItem);
        ApplySelectionChange(oldIndex, oldItem);
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Selected row index (-1 = none). The raw value is kept even when no
    /// row qualifies yet (markup attributes arrive before rows, as in real
    /// WPF); the getter reports the effective index.
    /// </summary>
    public int SelectedIndex
    {
        get => _selectedIndex >= 0 && _selectedIndex < _view.Count ? _selectedIndex : -1;
        set => SetRaw(value);
    }

    /// <summary>Selected row object (null when nothing is effectively selected).</summary>
    public object? SelectedItem
    {
        get
        {
            var i = SelectedIndex;
            return i >= 0 ? _view[i] : null;
        }
        set
        {
            if (value is null) { SetRaw(-1); return; }
            SetRaw(_view.IndexOf(value));
        }
    }

    /// <summary>Raised after the effective selection actually changes.</summary>
    public event Action? SelectionChanged;

    /// <summary>
    /// Sort-request gate (matches System.Windows.Controls.DataGrid.Sorting):
    /// converted HandyControl DataGridAttach code adds/removes its
    /// tri-state handler here (member-evidenced via Mono.Cecil on the
    /// upstream net10.0-windows HandyControl.dll). A press on a column
    /// header raises it through <see cref="OnSorting"/>; an unhandled
    /// request falls back to the engine default sort.
    /// </summary>
    public event DataGridSortingEventHandler? Sorting;

    /// <summary>
    /// Row-realization gate (matches
    /// System.Windows.Controls.DataGrid.LoadingRow): converted HandyControl
    /// DataGridAttach code adds/removes its row-number handler here
    /// (member-evidenced via Mono.Cecil on the upstream net10.0-windows
    /// HandyControl.dll). Raised from
    /// <see cref="IItemContainerOwner.PrepareContainerForItem"/> when a row
    /// container is prepared for a data item.
    /// </summary>
    public event EventHandler<DataGridRowEventArgs>? LoadingRow;

    /// <summary>
    /// Header press-to-sort gate (matches
    /// System.Windows.Controls.DataGrid.CanUserSortColumns; default true
    /// like real WPF). Clearing it keeps header presses from raising
    /// <see cref="Sorting"/>.
    /// </summary>
    public bool CanUserSortColumns { get; set; } = true;

    private readonly ObservableCollection<object?> _selectedItems = new();
    private bool _syncingSelectedItems;

    /// <summary>
    /// Selected data items (matches
    /// System.Windows.Controls.Primitives.MultiSelector.SelectedItems,
    /// which converted HandyControl SelectAllButtonAttach code reads for
    /// its count; the DataGrid IS a MultiSelector in real WPF): live set
    /// mirroring the single selection until <see cref="SelectAll"/> fills
    /// it with the rendered view. External edits re-seat the single
    /// selection on the first listed item.
    /// </summary>
    public IList SelectedItems => _selectedItems;

    /// <summary>
    /// Selects every rendered row (matches
    /// System.Windows.Controls.Primitives.MultiSelector.SelectAll, which
    /// converted HandyControl DataGridAttach/SelectAllButtonAttach code
    /// calls): the single selection moves to the first row and the set
    /// fills with the full view. Row highlight paints the current row only
    /// (multi-row tint stays a boundary, like cell selection).
    /// </summary>
    public void SelectAll()
    {
        if (_view.Count == 0)
        {
            SetRaw(-1);
            return;
        }
        SetRaw(0);
        _syncingSelectedItems = true;
        try
        {
            _selectedItems.Clear();
            foreach (var item in _view) _selectedItems.Add(item);
        }
        finally
        {
            _syncingSelectedItems = false;
        }
        InvalidateVisual();
    }

    /// <summary>
    /// Clears the selection (matches
    /// System.Windows.Controls.Primitives.MultiSelector.UnselectAll, which
    /// converted HandyControl DataGridAttach code calls on preview press
    /// over the scroll area).
    /// </summary>
    public void UnselectAll()
    {
        _syncingSelectedItems = true;
        try
        {
            _selectedItems.Clear();
        }
        finally
        {
            _syncingSelectedItems = false;
        }
        SetRaw(-1);
        InvalidateVisual();
    }

    /// <summary>
    /// Commits the pending row/cell edit (matches
    /// System.Windows.Controls.DataGrid.CommitEdit, which converted
    /// HandyControl DataGridAttach code calls before clearing): the engine
    /// grid never opens inline editors, so there is nothing pending and the
    /// commit reports success like real WPF does with no active edit.
    /// </summary>
    public bool CommitEdit() => true;

    /// <summary>
    /// Sort-request entry (matches
    /// System.Windows.Controls.DataGrid.OnSorting, protected virtual
    /// like real WPF): raises <see cref="Sorting"/>; when no handler marks
    /// the request handled, the engine default sort runs (toggle the column
    /// glyph plus its sort description, clearing the other columns).
    /// </summary>
    protected virtual void OnSorting(DataGridSortingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Sorting?.Invoke(this, e);
        if (!e.Handled) ApplyDefaultSort(e.Column);
    }

    /// <summary>
    /// Row-realization entry (matches
    /// System.Windows.Controls.DataGrid.OnLoadingRow, protected virtual
    /// like real WPF): raises <see cref="LoadingRow"/>.
    /// </summary>
    protected virtual void OnLoadingRow(DataGridRowEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        LoadingRow?.Invoke(this, e);
    }

    /// <summary>
    /// Engine default sort (WPF parity for an unhandled
    /// <see cref="Sorting"/> request): resolves the column's property path
    /// (SortMemberPath, else the text column's effective binding path),
    /// toggles its glyph, replaces that path's description and clears the
    /// other columns' glyphs. A pathless column is a silent no-op.
    /// </summary>
    private void ApplyDefaultSort(DataGridColumn column)
    {
        var path = !string.IsNullOrEmpty(column.SortMemberPath)
            ? column.SortMemberPath
            : column is DataGridTextColumn text ? text.EffectivePath : string.Empty;
        if (string.IsNullOrEmpty(path)) return;
        var next = column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        foreach (var other in _columns)
            if (!ReferenceEquals(other, column)) other.SortDirection = null;
        column.SortDirection = next;
        _items.SortDescriptions.Clear();
        _items.SortDescriptions.Add(new SortDescription(path, next));
    }

    private void OnSelectedItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_syncingSelectedItems) return;
        _syncingSelectedItems = true;
        try
        {
            if (_selectedItems.Count == 0) SetRaw(-1);
            else SetRaw(_view.IndexOf(_selectedItems[0]));
        }
        finally
        {
            _syncingSelectedItems = false;
        }
    }

    public override bool Focusable { get; set; } = true;

    /// <summary>
    /// Raises <see cref="Sorting"/> for the column header covering a press
    /// (WPF parity: header press sorts; the request carries that column and
    /// an unhandled request runs the engine default sort). False when the
    /// press is outside the header band, sorting is gated off, or no
    /// column covers the position.
    /// </summary>
    public bool TrySortAt(double x, double y)
    {
        if (!CanUserSortColumns || !ShowColumnHeaders || _columns.Count == 0) return false;
        var r = RenderRect;
        if (y < r.Y + 1 || y >= r.Y + 1 + _headerHeight) return false;
        var widths = _colWidths.Count == _columns.Count ? _colWidths : ResolveColumnWidths(Math.Max(0, r.Width - 2));
        var cx = r.X + 1;
        for (var c = 0; c < _columns.Count; c++)
        {
            var cw = c < widths.Count ? widths[c] : 0;
            if (x >= cx && x < cx + cw)
            {
                OnSorting(new DataGridSortingEventArgs(_columns[c]));
                return true;
            }
            cx += cw;
        }
        return false;
    }

    /// <summary>Selects a row by hit position; false when no row covers it.</summary>
    public bool SelectAt(double x, double y)
    {
        for (var i = 0; i < _rowRects.Count && i < _view.Count; i++)
        {
            var r = _rowRects[i];
            if (x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height)
            {
                SetRaw(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>Cell text for a row object (missing path renders blank).</summary>
    internal string CellText(object? row, DataGridTextColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return column.CellTextFor(row);
    }

    private void SetRaw(int raw)
    {
        // The raw value is kept even when no row qualifies yet (markup
        // attributes arrive before rows, as in real WPF); the event fires
        // only when the effective selection actually changes.
        if (raw < 0) raw = -1;
        if (_selectedIndex == raw) return;
        var oldEff = SelectedIndex;
        var oldItem = SelectedItem;
        _selectedIndex = raw;
        ApplySelectionChange(oldEff, oldItem);
    }

    private void ApplySelectionChange(int oldEff, object? oldItem)
    {
        var nowEff = SelectedIndex;
        var nowItem = SelectedItem;
        _selectedItem = nowItem;
        _effIndex = nowEff;
        _effItem = nowItem;
        if (!_syncingSelectedItems)
        {
            _syncingSelectedItems = true;
            try
            {
                _selectedItems.Clear();
                if (nowItem is not null) _selectedItems.Add(nowItem);
            }
            finally
            {
                _syncingSelectedItems = false;
            }
        }
        foreach (var row in _rows.Values) row.SyncSelection();
        if (nowEff != oldEff || !ReferenceEquals(nowItem, oldItem))
        {
            InvalidateVisual();
            SelectionChanged?.Invoke();
        }
    }

    private int _effIndex = -1;
    private object? _effItem;

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Cached pre-change effective state (the event fires post-change):
        // rows above the raw index shift it; identity decides the event.
        // Pending selections materialize on add; Reset drops everything.
        // The rendered view rebuilds first; realized containers rebuild
        // lazily on the next lookup, so stale rows never linger.
        var oldIndex = _effIndex;
        var oldItem = _effItem;
        RefreshView();
        ItemContainerGenerator.Refresh();
        _rows.Clear();
        if (SortActive)
        {
            _selectedIndex = _selectedItem is null ? -1 : _view.IndexOf(_selectedItem);
        }
        else switch (e.Action)
        {
            case NotifyCollectionChangedAction.Remove when e.OldStartingIndex >= 0 && e.OldStartingIndex < _selectedIndex:
                _selectedIndex -= e.OldItems?.Count ?? 1;
                break;
            case NotifyCollectionChangedAction.Move when _selectedItem is not null:
                _selectedIndex = _view.IndexOf(_selectedItem);
                break;
            case NotifyCollectionChangedAction.Reset:
                _selectedIndex = -1;
                break;
        }
        ApplySelectionChange(oldIndex, oldItem);
        InvalidateMeasure();
    }

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        // Selector press-to-select: a left press on a row selects it (WPF
        // selects on press, not release). Disabled grids ignore all input.
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        if (e.Source is not Core.WasmInputEvent ev) return;
        switch (ev)
        {
            case Core.WasmInputEvent.Pointer pd when pd.Args.Kind == Core.WasmPointerKind.Down && e.IsLeftButtonDown:
                if (TrySortAt(pd.Args.X, pd.Args.Y)) e.Handled = true;
                else if (SelectAt(pd.Args.X, pd.Args.Y)) e.Handled = true;
                TrackDoublePress(pd.Args.X, pd.Args.Y);
                break;
            case Core.WasmInputEvent.Key k when k.Args.IsDown && _focused:
                var count = _view.Count;
                if (count == 0) break;
                switch (k.Args.Key)
                {
                    case "ArrowDown": SelectedIndex = SelectedIndex < 0 ? 0 : Math.Min(count - 1, SelectedIndex + 1); e.Handled = true; break;
                    case "ArrowUp": SelectedIndex = SelectedIndex < 0 ? count - 1 : Math.Max(0, SelectedIndex - 1); e.Handled = true; break;
                    case "Home": SelectedIndex = 0; e.Handled = true; break;
                    case "End": SelectedIndex = count - 1; e.Handled = true; break;
                }
                break;
        }
    }

    private bool _focused;
    public override void OnGotFocus() { _focused = true; InvalidateVisual(); }
    public override void OnLostFocus() { _focused = false; InvalidateVisual(); }

    private (double Width, double Height) MeasureText(string text)
    {
        try
        {
            var (w, h) = TextShaper.Measure(text, FontCss);
            return (w, h);
        }
        catch (Exception)
        {
            return (text.Length * 8, 16);
        }
    }

    private List<double> ResolveColumnWidths(double available)
    {
        var widths = new List<double>(new double[_columns.Count]);
        var fixedTotal = 0.0;
        var starTotal = 0.0;
        for (var c = 0; c < _columns.Count; c++)
        {
            var def = _columns[c].Width;
            if (def.Unit == GridUnitType.Pixel) { widths[c] = Math.Max(0, def.Value); fixedTotal += widths[c]; }
            else if (def.Unit == GridUnitType.Star) starTotal += def.Value;
            else
            {
                // Auto: widest header/cell text plus padding.
                var (hw, _) = MeasureText(_columns[c].Header);
                var best = hw;
                foreach (var row in _view)
                {
                    var (w, _) = MeasureText(CellText(row, _columns[c]));
                    best = Math.Max(best, w);
                }
                widths[c] = best + 12;
                fixedTotal += widths[c];
            }
        }
        var starEach = starTotal > 0 ? Math.Max(0, (available - fixedTotal) / starTotal) : 0;
        for (var c = 0; c < _columns.Count; c++)
            if (_columns[c].Width.Unit == GridUnitType.Star)
                widths[c] = Math.Max(0, starEach * _columns[c].Width.Value);
        return widths;
    }

    private double RowContentHeight(object? row)
    {
        var best = 0.0;
        foreach (var col in _columns)
        {
            var (_, h) = MeasureText(CellText(row, col));
            best = Math.Max(best, h);
        }
        return best;
    }

    /// <summary>Column header band is painted (real WPF hides it for None/Row).</summary>
    private bool ShowColumnHeaders =>
        HeadersVisibility is DataGridHeadersVisibility.Column or DataGridHeadersVisibility.All;

    /// <summary>Row slot height (explicit RowHeight wins, else content-sized like before).</summary>
    private double RowSlotHeight(object? row) =>
        !double.IsNaN(RowHeight) && RowHeight > 0 ? RowHeight : Math.Max(28, RowContentHeight(row) + 10);

    protected override WpfSize MeasureCore(WpfSize available)
    {
        // A theme template owns the visuals (the MaterialDesign grid chrome);
        // the custom grid math only measures template-less grids.
        if (TemplateRoot is not null) return base.MeasureCore(available);
        var widths = ResolveColumnWidths(Math.Max(0, available.Width));
        var (_, headerH) = MeasureText("Mg"); // body-height representative
        _headerHeight = ShowColumnHeaders ? Math.Max(28, headerH + 10) : 0;
        var w = 0.0; var h = _headerHeight;
        foreach (var width in widths) w += width;
        foreach (var row in _view) h += RowSlotHeight(row);
        return new WpfSize(w, h);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (TemplateRoot is not null) { base.ArrangeCore(finalRect); return; }
        // Full-width rows like ListBox so clicks and highlights span the slot.
        // Rows live inside the 1px border chrome (matches ScrollViewer).
        _colWidths = ResolveColumnWidths(Math.Max(0, finalRect.Width - 2));
        _rowRects = new List<WpfRect>(_view.Count);
        var (_, headerH) = MeasureText("Mg");
        _headerHeight = ShowColumnHeaders ? Math.Max(28, headerH + 10) : 0;
        var y = finalRect.Y + 1 + _headerHeight;
        foreach (var row in _view)
        {
            var childH = RowSlotHeight(row);
            _rowRects.Add(new WpfRect(finalRect.X + 1, y, Math.Max(0, finalRect.Width - 2), childH));
            y += childH;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        // A theme template owns the visuals (the MaterialDesign grid chrome);
        // the legacy ring and rows only paint template-less grids.
        if (TemplateRoot is not null) { base.OnRender(dc); return; }
        var r = RenderRect;
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height,
            BrushRender.ResolveFill(BorderBrush, 0x707070FF));
        if (r.Width > 2 && r.Height > 2)
            dc.DrawRectangle((float)r.X + 1, (float)r.Y + 1, (float)r.Width - 2, (float)r.Height - 2,
                BrushRender.ResolveFill(Background, 0xFFFFFFFF));
        if (r.Width <= 2 || r.Height <= 2 || _columns.Count == 0) return;
        // Overflow clip: v1 has no internal scroller (nest in a ScrollViewer).
        dc.PushClip((float)r.X + 1, (float)r.Y + 1, (float)Math.Max(0, r.Width - 2), (float)Math.Max(0, r.Height - 2));
        var enabled = IsEffectivelyEnabled;
        var widths = _colWidths.Count == _columns.Count ? _colWidths : ResolveColumnWidths(Math.Max(0, r.Width - 2));
        var showHorizontal = GridLinesVisibility is DataGridGridLinesVisibility.Horizontal or DataGridGridLinesVisibility.All;
        var showVertical = GridLinesVisibility is DataGridGridLinesVisibility.Vertical or DataGridGridLinesVisibility.All;
        var horizontalInk = BrushRender.ResolveFill(HorizontalGridLinesBrush, 0xD0D0D0FF);
        var verticalInk = BrushRender.ResolveFill(VerticalGridLinesBrush, 0xD0D0D0FF);
        if (ShowColumnHeaders)
        {
            // Header band.
            dc.DrawRectangle((float)r.X + 1, (float)r.Y + 1, (float)Math.Max(0, r.Width - 2), (float)_headerHeight, 0xEEEEEEFF);
            var cx = (float)r.X + 1;
            for (var c = 0; c < _columns.Count; c++)
            {
                var cw = (float)widths[c];
                if (cw > 0 && !string.IsNullOrEmpty(_columns[c].Header))
                    dc.DrawText(_columns[c].Header, cx + 6, (float)r.Y + 1 + (float)_headerHeight / 2, FontCss, ForegroundRgba, 0, 2, -1);
                cx += cw;
            }
        }
        for (var i = 0; i < _rowRects.Count && i < _view.Count; i++)
        {
            var rr = _rowRects[i];
            var selected = SelectedIndex == i;
            // Zebra fills land under the selection tint, like WPF.
            var zebra = (i % 2 == 1 ? AlternatingRowBackground : RowBackground)
                ?? (i % 2 == 1 ? RowBackground : null);
            if (zebra is not null && !selected)
                dc.DrawRectangle((float)rr.X, (float)rr.Y, (float)rr.Width, (float)rr.Height,
                    BrushRender.ResolveFill(zebra, 0x00000000));
            if (selected)
                dc.DrawRectangle((float)rr.X, (float)rr.Y, (float)rr.Width, (float)rr.Height,
                    enabled ? 0x0078D4FFu : 0x909090FFu);
            var ink = selected ? 0xFFFFFFFFu : ForegroundRgba;
            // Horizontal gridlines under each row; verticals at column edges.
            if (showHorizontal)
                dc.DrawRectangle((float)rr.X, (float)(rr.Y + rr.Height - 1), (float)rr.Width, 1, horizontalInk);
            var cellX = (float)rr.X;
            for (var c = 0; c < _columns.Count; c++)
            {
                var cw = (float)widths[c];
                var text = CellText(_view[i], _columns[c]);
                if (cw > 12 && !string.IsNullOrEmpty(text))
                    dc.DrawText(text, cellX + 6, (float)(rr.Y + rr.Height / 2), FontCss, ink, 0, 2, -1);
                cellX += cw;
                if (showVertical && c + 1 < _columns.Count)
                    dc.DrawRectangle(cellX - 1, (float)rr.Y, 1, (float)rr.Height, verticalInk);
            }
        }
        dc.PopClip();
    }
}

/// <summary>
/// MenuItem v1: one menu entry. Leaves invoke <see cref="Click"/> and
/// <see cref="Command"/> on release-inside (Button press semantics);
/// parents open their submenu on press. Only MenuItem children are allowed.
/// Rendering (highlight, submenu panel) is owned by the parent
/// <see cref="Menu"/>; the item paints its own text and submenu arrow.
/// Unsealed like real WPF: converted AvalonDock MenuItemEx derives it (a
/// sealed parent fails the converted subtype load with TypeLoadException).
/// </summary>
/// <summary>Menu placement kind (matches System.Windows.Controls.MenuItemRole).</summary>
public enum MenuItemRole
{
    TopLevelItem = 0,
    TopLevelHeader = 1,
    SubmenuItem = 2,
    SubmenuHeader = 3,
}

public class MenuItem : Control
{
    /// <summary>Click token (matches MenuItem.ClickEvent; style EventSetters bind through it).</summary>
    public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent(
        "Click", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MenuItem));

    private object? _header = string.Empty;

    /// <summary>
    /// Entry header (matches MenuItem.Header): strings render as text; a
    /// UIElement header hosts as the entry visual (gallery MenuPage icon
    /// buttons carry TextBlock glyphs), like TreeViewItem headers.
    /// </summary>
    public object? Header
    {
        get => _header;
        set
        {
            if (Equals(_header, value)) return;
            if (_header is UIElement oldVisual) DetachVisualChild(oldVisual);
            _header = value;
            if (value is UIElement visual) AttachVisualChild(visual);
            _caption.Text = value switch
            {
                string text => text,
                TextBlock block => block.Text ?? string.Empty,
                null => string.Empty,
                _ => value.ToString() ?? string.Empty,
            };
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Icon identity (matches System.Windows.Controls.MenuItem.IconProperty;
    /// converted MenuItemEx registers it in its static constructor and reads
    /// it in UpdateIcon; the CLR wrapper stays store-backed so SetValue- and
    /// CLR-applied icons read back identically).
    /// </summary>
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register("Icon", typeof(object), typeof(MenuItem),
            new FrameworkPropertyMetadata(null));

    /// <summary>
    /// Entry icon (matches MenuItem.Icon; retained, never rendered: menu
    /// rows paint text plus the submenu arrow, like the loader boundary).
    /// </summary>
    public object? Icon
    {
        get => GetValue(IconProperty);
        set
        {
            if (Equals(GetValue(IconProperty), value)) return;
            SetValue(IconProperty, value);
            InvalidateMeasure();
        }
    }

    /// <summary>Text renderer for string headers (font/ink follow the entry).</summary>
    private readonly TextBlock _caption = new();

    /// <summary>Header text for state channels and tests (visuals report their text).</summary>
    internal string HeaderText => _header is string text ? text : _caption.Text;

    /// <summary>
    /// Focus leaving a menu entry dismisses the open menu (native: the submenu
    /// closes once keyboard focus moves outside the menu scope), unless focus
    /// stays inside the same menu subtree (intra-menu navigation).
    /// </summary>
    public override void OnLostFocus()
    {
        var next = WpfInputManager.Current?.Focused;
        Menu.FindRootMenu(this)?.NotifyItemFocusLost(next);
    }

    /// <summary>Text ink for the idle state (synced from Foreground, like Button).</summary>
    public uint ForegroundRgba { get; set; } = 0x000000FF;

    /// <summary>Entry font (synced from FontFamily/FontSize/FontWeight, like Button).</summary>
    public string FontCss { get; set; } = "14px sans-serif";

    protected override void OnForegroundChanged(Brush? brush)
    {
        if (brush is SolidColorBrush solid) ForegroundRgba = solid.ToRgba();
    }

    protected override void OnFontChanged() => FontCss = ComposeFontCss();

    private static readonly DependencyPropertyKey RolePropertyKey =
        DependencyProperty.RegisterReadOnly("Role", typeof(MenuItemRole), typeof(MenuItem),
            new FrameworkPropertyMetadata(MenuItemRole.TopLevelItem));

    /// <summary>Role token (matches MenuItem.RoleProperty; read-only like real WPF).</summary>
    public static readonly DependencyProperty RoleProperty = RolePropertyKey.DependencyProperty;

    /// <summary>Menu role (computed from the parent chain and submenu rows, like WPF).</summary>
    public MenuItemRole Role
    {
        get
        {
            var role = ComputeRole();
            SetValue(RolePropertyKey, role);
            return role;
        }
    }

    private MenuItemRole ComputeRole()
    {
        var submenu = false;
        for (var node = Parent as FrameworkElement; node is not null; node = node.Parent as FrameworkElement)
            if (node is MenuItem) { submenu = true; break; }
        return (submenu, HasItems) switch
        {
            (false, false) => MenuItemRole.TopLevelItem,
            (false, true) => MenuItemRole.TopLevelHeader,
            (true, false) => MenuItemRole.SubmenuItem,
            (true, true) => MenuItemRole.SubmenuHeader,
        };
    }

    /// <summary>Recomputes the stored Role value (idempotent; announces only on change, which re-evaluates style triggers).</summary>
    internal void RefreshRole() => SetValue(RolePropertyKey, ComputeRole());

    /// <summary>Recomputes the stored Role value for this entry and its submenu (idempotent).</summary>
    internal void RefreshRoleSubtree()
    {
        RefreshRole();
        foreach (var child in Items)
            child.RefreshRoleSubtree();
    }

    // Background, Padding, FontFamily, FontWeight and VerticalContentAlignment
    // come from Control (same defaults: null/zero/sans-serif/Normal/Top).

    /// <summary>Submenu entries in order (MenuItem children only).</summary>
    public IReadOnlyList<MenuItem> Items
    {
        get
        {
            var items = new List<MenuItem>();
            foreach (UIElement c in Children)
                if (c is MenuItem item) items.Add(item);
            return items;
        }
    }

    /// <summary>Whether this entry opens a submenu (separators and header visuals are not entries).</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>Panel rows in order (submenu entries plus separators; header visuals excluded).</summary>
    internal IReadOnlyList<UIElement> PanelRows
    {
        get
        {
            var rows = new List<UIElement>();
            foreach (UIElement c in Children)
                if (c is MenuItem || c is Separator) rows.Add(c);
            return rows;
        }
    }

    private bool _isSubmenuOpen;

    /// <summary>
    /// Whether the submenu is currently dropped down (default closed, as in
    /// WPF). Open submenus paint above siblings; the menu closes them on
    /// toggle, switch, invoke, Escape and focus loss.
    /// </summary>
    public bool IsSubmenuOpen
    {
        get => _isSubmenuOpen;
        set
        {
            if (_isSubmenuOpen == value) return;
            _isSubmenuOpen = value;
            InvalidateMeasure();
            (value ? SubmenuOpened : SubmenuClosed)?.Invoke();
        }
    }

    /// <summary>Raised when this entry's submenu opens.</summary>
    public event Action? SubmenuOpened;

    /// <summary>Raised when this entry's submenu closes.</summary>
    public event Action? SubmenuClosed;

    /// <summary>Drop-down panel rect, assigned by the Menu during arrange.</summary>
    internal WpfRect DropRect { get; set; }

    private static readonly DependencyPropertyKey IsSuspendingPopupAnimationPropertyKey =
        DependencyProperty.RegisterReadOnly("IsSuspendingPopupAnimation", typeof(bool), typeof(MenuItem),
            new FrameworkPropertyMetadata(false));

    /// <summary>Popup-animation suspension token (matches MenuItem.IsSuspendingPopupAnimationProperty; read-only like real WPF).</summary>
    public static readonly DependencyProperty IsSuspendingPopupAnimationProperty = IsSuspendingPopupAnimationPropertyKey.DependencyProperty;

    /// <summary>Whether popup animation is suspended while scrolling (matches MenuItem.IsSuspendingPopupAnimation; public get, internal set, default false like real WPF, verified on net10.0-windows).</summary>
    public bool IsSuspendingPopupAnimation
    {
        get => (bool)(GetValue(IsSuspendingPopupAnimationProperty) ?? false);
        internal set => SetValue(IsSuspendingPopupAnimationPropertyKey, value);
    }

    private bool _highlighted;

    /// <summary>Hot-tracked entry (paints highlighted; set by the Menu).</summary>
    public bool IsHighlighted
    {
        get => _highlighted;
        internal set
        {
            if (_highlighted == value) return;
            _highlighted = value;
            InvalidateVisual();
        }
    }

    /// <summary>Fires Click, then executes <see cref="Command"/> when it can execute.</summary>
    public void RaiseClick()
    {
        if (!IsEffectivelyEnabled) return;
        if (IsCheckable) IsChecked = !IsChecked;
        Click?.Invoke(this, new RoutedEventArgs { Source = this, OriginalSource = this });
        if (Command is RoutedCommand routed)
            routed.Execute(CommandParameter, CommandTarget as UIElement ?? Keyboard.FocusedElement);
        else Command?.Execute(CommandParameter);
    }

    /// <summary>Leaf activation (routed in real WPF; direct here).</summary>
    public event RoutedEventHandler? Click;

    private bool _isCheckable;

    /// <summary>Checkable entries toggle IsChecked on invoke (calculator View menu).</summary>
    public bool IsCheckable
    {
        get => _isCheckable;
        set
        {
            if (_isCheckable == value) return;
            _isCheckable = value;
            InvalidateVisual();
        }
    }

    private bool _isChecked;

    /// <summary>Check state (paints a mark when true; toggled on invoke when checkable).</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            InvalidateVisual();
            CheckedChanged?.Invoke();
        }
    }

    /// <summary>Raised after IsChecked changes.</summary>
    public event Action? CheckedChanged;

    private ICommand? _command;
    private bool _commandCanExecute = true;

    private IInputElement? _commandTarget;

    /// <summary>Command target (defaults to the focused element when null).</summary>
    public IInputElement? CommandTarget
    {
        get => _commandTarget;
        set
        {
            if (ReferenceEquals(_commandTarget, value)) return;
            _commandTarget = value;
            UpdateCanExecute();
        }
    }

    /// <summary>Bound command (invoked on click; drives greyed state).</summary>
    public ICommand? Command
    {
        get => _command;
        set
        {
            if (ReferenceEquals(_command, value)) return;
            if (_command is not null) _command.CanExecuteChanged -= OnCommandCanExecuteChanged;
            _command = value;
            if (_command is not null) _command.CanExecuteChanged += OnCommandCanExecuteChanged;
            // WPF shows the command text when Header is unset (Copy row).
            if ((_header is null || (_header is string hs && hs.Length == 0)) && value is RoutedUICommand routed && routed.Text.Length > 0)
                Header = routed.Text;
            UpdateCanExecute();
        }
    }

    private object? _commandParameter;

    /// <summary>Parameter forwarded to <see cref="Command"/>.</summary>
    public object? CommandParameter
    {
        get => _commandParameter;
        set
        {
            if (Equals(_commandParameter, value)) return;
            _commandParameter = value;
            UpdateCanExecute();
        }
    }

    /// <summary>Local flag AND command gate.</summary>
    public override bool IsEffectivelyEnabled => IsEnabled && _commandCanExecute;

    private void OnCommandCanExecuteChanged(object? sender, EventArgs e) => UpdateCanExecute();

    private void UpdateCanExecute()
    {
        var can = _command switch
        {
            RoutedCommand routed => routed.CanExecute(CommandParameter, CommandTarget as UIElement ?? Keyboard.FocusedElement),
            null => true,
            _ => _command.CanExecute(CommandParameter),
        };
        if (_commandCanExecute == can) return;
        _commandCanExecute = can;
        InvalidateVisual();
    }

    /// <summary>
    /// Appends a submenu entry or a separator row. Only MenuItem and
    /// Separator children are accepted (loader boundaries reject anything
    /// else first; this is the programmatic backstop).
    /// </summary>
    public new void AddChild(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child is not MenuItem && child is not Separator)
            throw new InvalidOperationException(
                $"<MenuItem> entries must be <MenuItem> (have <{child.GetType().Name}>).");
        base.AddChild(child);
        RefreshRole();
        if (child is MenuItem mi) mi.RefreshRoleSubtree();
    }

    /// <summary>
    /// Removes a submenu entry or separator row, refreshing the stored Role
    /// values it affects (own HasItems plus the detached subtree).
    /// </summary>
    public new bool RemoveChild(UIElement child)
    {
        var removed = base.RemoveChild(child);
        if (removed)
        {
            RefreshRole();
            if (child is MenuItem mi) mi.RefreshRoleSubtree();
        }
        return removed;
    }

    /// <summary>
    /// Hit-test covers the entry rect plus the open submenu panel, so the
    /// recursive input walk reaches dropped-down entries.
    /// </summary>
    public override bool HitTest(WpfPoint point) =>
        Visibility == Visibility.Visible && (base.HitTest(point) || (IsSubmenuOpen &&
            point.X >= DropRect.X && point.X < DropRect.X + DropRect.Width &&
            point.Y >= DropRect.Y && point.Y < DropRect.Y + DropRect.Height));

    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (TemplateRoot is not null) return base.MeasureCore(available);
        if (Header is UIElement visual)
        {
            visual.Measure(new WpfSize(Math.Max(0, available.Width - 24), available.Height));
            return new(
                Math.Min(available.Width, Math.Max(0, visual.DesiredSize.Width + 24)),
                Math.Max(28, visual.DesiredSize.Height + 10));
        }
        var text = HeaderText;
        var (shapedW, shapedH) = TryMeasure(text, FontCss, text.Length * 8, 16);
        return new(
            Math.Min(available.Width, Math.Max(0, shapedW + 24)),
            Math.Max(28, shapedH + 10));
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (TemplateRoot is not null) { base.ArrangeCore(finalRect); return; }
        if (Header is UIElement visual)
            visual.Arrange(new WpfRect(
                finalRect.X + 12, finalRect.Y,
                Math.Max(0, finalRect.Width - 12), finalRect.Height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        // A theme template owns the visuals (PART_Popup, SubMenuBorder...);
        // the custom header/arrow chrome only paints template-less entries.
        if (TemplateRoot is not null) { base.OnRender(dc); return; }
        // Header and submenu arrow only; the Menu paints bar, panels and
        // highlight behind the entries.
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        var hot = IsHighlighted || IsSubmenuOpen;
        var ink = !IsEffectivelyEnabled ? 0x909090FFu
            : hot ? 0xFFFFFFFFu : ForegroundRgba;
        if (IsChecked)
            dc.DrawText("✓", (float)(r.X + 2), (float)(r.Y + r.Height / 2), FontCss,
                ink, 0, 2, -1);
        if (Header is UIElement visual)
            RenderVisible(dc, visual);
        else if (HeaderText.Length > 0)
            dc.DrawText(HeaderText, (float)(r.X + 12), (float)(r.Y + r.Height / 2), FontCss,
                ink, 0, 2, -1);
        // Submenu arrow only inside drop-down panels; top-level bar entries
        // (parented directly to the Menu) never show one, like real WPF.
        if (HasItems && Parent is MenuItem)
            dc.DrawText(">", (float)(r.X + r.Width - 16), (float)(r.Y + r.Height / 2), FontCss,
                ink, 0, 2, -1);
    }
}

/// <summary>
/// Menu v1: a horizontal bar of <see cref="MenuItem"/> entries with
/// drop-down submenus. Press toggles a submenu; hover highlights and
/// switches while open; leaves invoke on release-inside (drag across works:
/// release over any enabled leaf invokes it); Escape, focus loss and
/// release-outside-after-press-inside close the menus. Full keyboard
/// navigation when focused. Presses on dead background with no prior press
/// never reach the menu (input routes to the hit target only): that
/// light-dismiss path stays a v1 gap.
/// </summary>
/// <summary>Menu base (matches System.Windows.Controls.Primitives.MenuBase;
/// the Control chrome (Background/FontFamily/Template) is settable like WPF;
/// the ItemsControl item-host inheritance stays a boundary, like the other
/// item hosts that keep their own collections).</summary>
public class MenuBase : Control
{
}

public class Menu : MenuBase
{
    private const double BarHeight = 28;

    /// <summary>Separator slot (bar width and panel row height; the 1px rule centers inside).</summary>
    private const double SeparatorSlot = 9;

    /// <summary>Top-level entries in order (MenuItem children only; separators excluded).</summary>
    public IReadOnlyList<MenuItem> Items
    {
        get
        {
            var items = new List<MenuItem>();
            foreach (UIElement c in Children)
                if (c is MenuItem item) items.Add(item);
            return items;
        }
    }

    /// <summary>Top-level rows in order (entries plus separators).</summary>
    private IReadOnlyList<UIElement> TopRows
    {
        get
        {
            var rows = new List<UIElement>();
            foreach (UIElement c in Children)
                if (c is MenuItem || c is Separator) rows.Add(c);
            return rows;
        }
    }

    /// <summary>Hot-tracked entry, if any (set by hover and keyboard).</summary>
    internal MenuItem? Highlighted { get; private set; }

    /// <summary>
    /// Appends a top-level entry or separator. Only MenuItem and Separator
    /// children are accepted (loader boundaries reject anything else first;
    /// this is the programmatic backstop).
    /// </summary>
    public new void AddChild(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child is not MenuItem && child is not Separator)
            throw new InvalidOperationException(
                $"<Menu> entries must be <MenuItem> (have <{child.GetType().Name}>).");
        base.AddChild(child);
    }

    /// <summary>
    /// Closes every open submenu and clears the highlight (and any pending
    /// press: a release after a close must not invoke).
    /// </summary>
    public void CloseMenus()
    {
        _pressed = null;
        var changed = Highlighted is not null;
        Highlighted = null;
        foreach (var item in Items) CloseChain(item);
        if (changed) InvalidateVisual();
    }

    private static void CloseChain(MenuItem item)
    {
        if (item.IsSubmenuOpen)
        {
            item.IsSubmenuOpen = false;
            item.IsHighlighted = false;
        }
        else if (item.IsHighlighted) item.IsHighlighted = false;
        foreach (var child in item.Items) CloseChain(child);
    }

    /// <summary>Top-level entry whose submenu is open, if any.</summary>
    internal MenuItem? OpenTopItem
    {
        get
        {
            foreach (var item in Items)
                if (item.IsSubmenuOpen) return item;
            return null;
        }
    }

    /// <summary>Deepest open entry (the panel keyboard nav acts on).</summary>
    private MenuItem? DeepestOpen()
    {
        var top = OpenTopItem;
        if (top is null) return null;
        var node = top;
        while (true)
        {
            MenuItem? next = null;
            foreach (var child in node.Items)
                if (child.IsSubmenuOpen) { next = child; break; }
            if (next is null) return node;
            node = next;
        }
    }

    /// <summary>Sibling entries of the highlighted/open context for nav.</summary>
    private List<MenuItem> CurrentPanel()
    {
        var deep = DeepestOpen();
        if (deep is null) return new List<MenuItem>(Items);
        // Deepest open panel shows the deep entry's children; if it has none
        // (open flag on a leaf can't happen via input), fall back to its peers.
        if (deep.Items.Count > 0) return new List<MenuItem>(deep.Items);
        return deep.Parent is MenuItem parent ? new List<MenuItem>(parent.Items) : new List<MenuItem>(Items);
    }

    /// <summary>
    /// Next enabled entry from <paramref name="from"/> in direction
    /// <paramref name="dir"/> (+1/-1), wrapping; -1 when none is enabled.
    /// Starts after <paramref name="from"/> (use -1/0 for Down/Up with no
    /// highlight so Down lands on the first and Up on the last entry).
    /// </summary>
    private static int StepEnabled(List<MenuItem> panel, int from, int dir)
    {
        if (panel.Count == 0) return -1;
        if (from < 0) from = dir > 0 ? -1 : 0;
        for (var n = 0; n < panel.Count; n++)
        {
            from = (from + dir + panel.Count) % panel.Count;
            if (panel[from].IsEffectivelyEnabled) return from;
        }
        return -1;
    }

    private static int FirstEnabled(List<MenuItem> panel)
    {
        for (var i = 0; i < panel.Count; i++)
            if (panel[i].IsEffectivelyEnabled) return i;
        return -1;
    }

    private static int LastEnabled(List<MenuItem> panel)
    {
        for (var i = panel.Count - 1; i >= 0; i--)
            if (panel[i].IsEffectivelyEnabled) return i;
        return -1;
    }

    private void SetHighlighted(MenuItem? item)
    {
        if (ReferenceEquals(Highlighted, item)) return;
        if (Highlighted is not null) Highlighted.IsHighlighted = false;
        Highlighted = item;
        if (item is not null) item.IsHighlighted = true;
        InvalidateVisual();
    }

    /// <summary>Visible entry under the point (bar plus open panels).</summary>
    private MenuItem? ItemAt(double x, double y)
    {
        var point = new WpfPoint(x, y);
        foreach (var item in Items)
        {
            var hit = HitVisible(item, point);
            if (hit is not null) return hit;
        }
        return null;
    }

    private static MenuItem? HitVisible(MenuItem item, WpfPoint point)
    {
        // Open submenus paint above the bar beneath them; test children
        // first so a dropped-down entry wins. Panel padding (DropRect hits
        // outside every own rect) deliberately misses: pressing it keeps the
        // menu open instead of toggling.
        if (item.IsSubmenuOpen)
            foreach (var child in item.Items)
            {
                var hit = HitVisible(child, point);
                if (hit is not null) return hit;
            }
        return InOwnRect(item, point) ? item : null;
    }

    private static bool InOwnRect(MenuItem item, WpfPoint point)
    {
        var r = item.RenderRect;
        return point.X >= r.X && point.X < r.X + r.Width &&
            point.Y >= r.Y && point.Y < r.Y + r.Height;
    }

    private MenuItem? _pressed;

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        // Disabled menus ignore all input; hover never marks handled so
        // overlapping controls keep receiving moves.
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        if (e.Source is not Core.WasmInputEvent ev) return;
        switch (ev)
        {
            case Core.WasmInputEvent.Pointer pd when pd.Args.Kind == Core.WasmPointerKind.Down && e.IsLeftButtonDown:
            {
                var item = ItemAt(pd.Args.X, pd.Args.Y);
                if (item is null || !item.IsEffectivelyEnabled) break;
                if (item.HasItems)
                {
                    if (item.IsSubmenuOpen) CloseFrom(item);
                    else { CloseMenus(); OpenPath(item); }
                }
                // Latched after any CloseMenus above (which clears the press).
                _pressed = item;
                SetHighlighted(item.HasItems && item.IsSubmenuOpen ? FirstLeaf(item) ?? item : item);
                e.Handled = true;
                break;
            }
            case Core.WasmInputEvent.Pointer pu when pu.Args.Kind == Core.WasmPointerKind.Up:
            {
                if (_pressed is null) break;
                var pressed = _pressed;
                _pressed = null;
                var item = ItemAt(pu.Args.X, pu.Args.Y);
                if (item is null || !item.IsEffectivelyEnabled) { CloseMenus(); e.Handled = true; break; }
                // Release over the pressed parent keeps the press-time toggle
                // (toggling twice would reopen on release).
                if (ReferenceEquals(item, pressed) && item.HasItems) SetHighlighted(item);
                else if (item.HasItems && !item.IsSubmenuOpen)
                { CloseMenus(); OpenPath(item); SetHighlighted(FirstLeaf(item) ?? item); }
                else if (!item.HasItems) { item.RaiseClick(); CloseMenus(); }
                else SetHighlighted(item); // open parent that was never pressed
                e.Handled = true;
                break;
            }
            case Core.WasmInputEvent.Pointer pc when pc.Args.Kind == Core.WasmPointerKind.Cancel:
                _pressed = null;
                break;
            case Core.WasmInputEvent.Pointer pm when pm.Args.Kind == Core.WasmPointerKind.Move:
            {
                var item = ItemAt(pm.Args.X, pm.Args.Y);
                if (item is null || !item.IsEffectivelyEnabled) break;
                SetHighlighted(item);
                // Hover-switch: dragging or moving across top-level entries
                // while a menu is open swaps the open submenu.
                var open = OpenTopItem;
                if (open is not null && IsTopLevel(item) && !ReferenceEquals(item, open) && item.HasItems)
                {
                    CloseMenus();
                    OpenPath(item);
                    SetHighlighted(FirstLeaf(item) ?? item);
                }
                break;
            }
            case Core.WasmInputEvent.Key k when k.Args.IsDown && _focused:
                HandleKey(k.Args.Key, e);
                break;
        }
    }

    private static bool IsTopLevel(MenuItem item) => item.Parent is Menu;

    private static MenuItem? FirstLeaf(MenuItem item)
    {
        foreach (var child in item.Items)
            if (child.IsEffectivelyEnabled) return child;
        return null;
    }

    private void OpenPath(MenuItem item)
    {
        // Opens the chain from the bar down to (but not including) item's
        // own submenu: every ancestor's submenu opens.
        ArgumentNullException.ThrowIfNull(item);
        for (var p = item.Parent; p is MenuItem parent; p = parent.Parent)
            parent.IsSubmenuOpen = true;
        item.IsSubmenuOpen = true;
        InvalidateVisual();
    }

    private void CloseFrom(MenuItem item)
    {
        // Closes item's submenu but keeps ancestors open.
        item.IsSubmenuOpen = false;
        foreach (var child in item.Items) CloseChain(child);
        SetHighlighted(item);
        InvalidateVisual();
    }

    private void HandleKey(string key, WasmRoutedEvent e)
    {
        var panel = CurrentPanel();
        var index = Highlighted is null ? -1 : panel.IndexOf(Highlighted);
        switch (key)
        {
            case "Escape":
                if (OpenTopItem is not null) { CloseMenus(); e.Handled = true; }
                break;
            case "ArrowDown":
                if (OpenTopItem is null)
                {
                    if (Items.Count > 0) { OpenPath(Items[0]); SetHighlighted(FirstLeaf(Items[0]) ?? Items[0]); e.Handled = true; }
                }
                else if (panel.Count > 0)
                {
                    var next = StepEnabled(panel, index, +1);
                    if (next >= 0) { SetHighlighted(panel[next]); e.Handled = true; }
                }
                break;
            case "ArrowUp":
                if (panel.Count > 0 && OpenTopItem is not null)
                {
                    var prev = StepEnabled(panel, index, -1);
                    if (prev >= 0) { SetHighlighted(panel[prev]); e.Handled = true; }
                }
                break;
            case "ArrowRight":
                if (OpenTopItem is null)
                {
                    if (Items.Count > 0)
                    {
                        var next = Items[(index < 0 ? 0 : (index + 1) % Items.Count)];
                        SetHighlighted(next);
                        e.Handled = true;
                    }
                }
                else if (Highlighted is not null && Highlighted.HasItems && Highlighted.IsEffectivelyEnabled)
                {
                    OpenPath(Highlighted);
                    Highlighted.IsSubmenuOpen = true;
                    SetHighlighted(FirstLeaf(Highlighted) ?? Highlighted);
                    e.Handled = true;
                }
                break;
            case "ArrowLeft":
            {
                var deep = DeepestOpen();
                if (deep is null)
                {
                    if (Items.Count > 0)
                    {
                        var prev = Items[(index < 0 ? Items.Count - 1 : (index - 1 + Items.Count) % Items.Count)];
                        SetHighlighted(prev);
                        e.Handled = true;
                    }
                }
                else if (deep.Parent is MenuItem parent)
                {
                    deep.IsSubmenuOpen = false;
                    SetHighlighted(parent);
                    e.Handled = true;
                }
                else
                {
                    CloseMenus();
                    var at = -1;
                    for (var i = 0; i < Items.Count; i++)
                        if (ReferenceEquals(Items[i], deep)) { at = i; break; }
                    var prev = Items.Count > 0 ? Items[(at < 0 ? Items.Count - 1 : (at - 1 + Items.Count) % Items.Count)] : null;
                    SetHighlighted(prev);
                    e.Handled = true;
                }
                break;
            }
            case "Home":
                if (panel.Count > 0)
                {
                    var first = FirstEnabled(panel);
                    if (first >= 0) { SetHighlighted(panel[first]); e.Handled = true; }
                }
                break;
            case "End":
                if (panel.Count > 0)
                {
                    var last = LastEnabled(panel);
                    if (last >= 0) { SetHighlighted(panel[last]); e.Handled = true; }
                }
                break;
            case "Enter":
            case " ":
                // Capture the target first: CloseMenus clears Highlighted.
                var target = Highlighted;
                if (target is not null && target.IsEffectivelyEnabled)
                {
                    if (target.HasItems && !target.IsSubmenuOpen)
                    {
                        CloseMenus();
                        OpenPath(target);
                        SetHighlighted(FirstLeaf(target) ?? target);
                    }
                    else if (!target.HasItems) { target.RaiseClick(); CloseMenus(); }
                    e.Handled = true;
                }
                break;
        }
    }

    private bool _focused;
    public override void OnGotFocus() { _focused = true; InvalidateVisual(); }
    public override void OnLostFocus()
    {
        _focused = false;
        // Intra-menu focus moves (a press resolving to another entry, e.g. a
        // separator row mapping to its parent) are navigation, not dismissal:
        // only focus leaving the menu closes it, like native.
        var next = WpfInputManager.Current?.Focused;
        if (next is not null && !ReferenceEquals(next, this) && WpfInputManager.IsInSubtree(this, next))
        {
            InvalidateVisual();
            return;
        }
        CloseMenus();
    }

    /// <summary>Topmost <see cref="Menu"/> ancestor of an element (null when detached).</summary>
    internal static Menu? FindRootMenu(UIElement element)
    {
        Menu? root = null;
        for (var p = element.Parent; p is not null; p = p.Parent)
            if (p is Menu menu) root = menu;
        return root;
    }

    /// <summary>
    /// Dismisses the open menu when focus leaves it from one of its entries
    /// (native menu-mode exit); intra-menu moves are navigation, not dismissal.
    /// </summary>
    internal void NotifyItemFocusLost(UIElement? next)
    {
        if (next is null || !WpfInputManager.IsInSubtree(this, next)) CloseMenus();
    }

    public override bool Focusable { get; set; } = true;

    /// <summary>
    /// Hit-test covers the bar plus open submenu panels, so presses over a
    /// dropped-down panel route into the menu.
    /// </summary>
    public override bool HitTest(WpfPoint point)
    {
        if (Visibility != Visibility.Visible) return false;
        if (base.HitTest(point)) return true;
        var top = OpenTopItem;
        return top is not null && PanelContains(top, point);
    }

    private static bool PanelContains(MenuItem item, WpfPoint point)
    {
        if (!item.IsSubmenuOpen) return false;
        var r = item.DropRect;
        if (point.X >= r.X && point.X < r.X + r.Width &&
            point.Y >= r.Y && point.Y < r.Y + r.Height) return true;
        foreach (var child in item.Items)
            if (PanelContains(child, point)) return true;
        return false;
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        // The bar defines layout size; drop-down panels overlay and never
        // affect it. All rows are measured so open panels arrange clean.
        var w = 0.0;
        foreach (var top in TopRows)
        {
            if (top is MenuItem entry) { MeasureSubtree(entry, available); w += entry.DesiredSize.Width; }
            else { top.Measure(new WpfSize(SeparatorSlot, BarHeight)); w += SeparatorSlot; }
        }
        return new(w, BarHeight);
    }

    private static void MeasureSubtree(MenuItem item, WpfSize available)
    {
        item.Measure(new WpfSize(Math.Max(0, available.Width), double.PositiveInfinity));
        foreach (var row in item.PanelRows)
        {
            if (row is MenuItem child) MeasureSubtree(child, available);
            else row.Measure(new WpfSize(Math.Max(0, available.Width), double.PositiveInfinity));
        }
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        var x = finalRect.X;
        foreach (var top in TopRows)
        {
            if (top is MenuItem entry)
            {
                var w = entry.DesiredSize.Width;
                entry.Arrange(new WpfRect(x, finalRect.Y, w, BarHeight));
                x += w;
            }
            else
            {
                top.Arrange(new WpfRect(x, finalRect.Y, SeparatorSlot, BarHeight));
                x += SeparatorSlot;
            }
        }
        var windowWidth = double.PositiveInfinity;
        for (var p = (UIElement?)Parent; p is not null; p = p.Parent)
            if (p is Window win) { windowWidth = win.RenderRect.Width; break; }
        foreach (var top in Items) ArrangePanels(top, finalRect, windowWidth);
    }

    private static void ArrangePanels(MenuItem opener, WpfRect bar, double windowWidth)
    {
        if (!opener.IsSubmenuOpen) return;
        var w = 0.0; var h = 0.0;
        foreach (var row in opener.PanelRows)
        {
            var rh = row is Separator ? SeparatorSlot : row.DesiredSize.Height;
            w = Math.Max(w, row.DesiredSize.Width);
            h += rh;
        }
        var r = opener.RenderRect;
        double px, py;
        if (opener.Parent is Menu)
        {
            px = r.X; py = bar.Y + BarHeight;
        }
        else
        {
            px = r.X + r.Width; py = r.Y;
        }
        if (!double.IsPositiveInfinity(windowWidth)) px = Math.Min(px, Math.Max(bar.X, bar.X + windowWidth - w));
        else if (opener.Parent is not Menu) px = r.X + r.Width;
        opener.DropRect = new WpfRect(px, py, w, h);
        var y = py;
        foreach (var row in opener.PanelRows)
        {
            if (row is MenuItem child)
            {
                child.Arrange(new WpfRect(px, y, w, child.DesiredSize.Height));
                y += child.DesiredSize.Height;
                ArrangePanels(child, bar, windowWidth);
            }
            else
            {
                row.Arrange(new WpfRect(px, y, w, SeparatorSlot));
                y += SeparatorSlot;
            }
        }
    }

    private static Thickness GetMargin(UIElement c) =>
        c is FrameworkElement fe ? fe.Margin : new Thickness(0);

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        // Menu strip band with a bottom rule. Drop-down panels paint in a
        // post-pass (RenderOpenPanels, driven by WasmMilCore after the whole
        // tree) so they cover later siblings like real popups. Bar separators
        // paint as vertical rules (panels paint their own horizontal ones).
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)BarHeight, 0xF5F5F5FF);
        dc.DrawRectangle((float)r.X, (float)(r.Y + BarHeight - 1), (float)r.Width, 1, 0x707070FF);
        foreach (var top in TopRows)
        {
            if (top is MenuItem entry) RenderVisible(dc, entry);
            else
            {
                var sr = top.RenderRect;
                if (sr.Width > 0 && sr.Height > 0)
                    dc.DrawLine(
                        (float)(sr.X + sr.Width / 2), (float)(sr.Y + 4),
                        (float)(sr.X + sr.Width / 2), (float)(sr.Y + sr.Height - 4),
                        1, 0x707070FF);
            }
        }
    }

    /// <summary>
    /// Overlay hit: the open top-level panel, so routing reaches dropped-down
    /// entries even when later siblings (content below the bar) overlap the
    /// panel area. Bar hits return null and route through the normal children
    /// walk, keeping the full root-to-entry chain.
    /// </summary>
    public override UIElement? HitTestOverlay(WpfPoint point)
    {
        foreach (var top in Items)
            if (top.IsSubmenuOpen && PanelContains(top, point) && !InOwnRect(top, point))
                return top;
        return null;
    }

    /// <summary>
    /// Paints open drop-down panels above everything else (called after the
    /// main render pass; popup-like, unclipped by ancestors).
    /// </summary>
    internal void RenderOpenPanels(DrawingContext dc)
    {
        var open = OpenTopItem;
        if (open is not null) RenderPanels(dc, open, IsEffectivelyEnabled);
    }

    private static void RenderPanels(DrawingContext dc, MenuItem opener, bool enabled)
    {
        var p = opener.DropRect;
        if (p.Width <= 0 || p.Height <= 0) return;
        dc.DrawRectangle((float)p.X, (float)p.Y, (float)p.Width, (float)p.Height, 0x707070FF);
        if (p.Width > 2 && p.Height > 2)
            dc.DrawRectangle((float)p.X + 1, (float)p.Y + 1, (float)p.Width - 2, (float)p.Height - 2, 0xFFFFFFFF);
        if (p.Width <= 2 || p.Height <= 2) return;
        foreach (var row in opener.PanelRows)
        {
            if (row is not MenuItem child)
            {
                RenderVisible(dc, row);
                continue;
            }
            if (child.IsHighlighted || child.IsSubmenuOpen)
            {
                var ir = child.RenderRect;
                dc.DrawRectangle((float)ir.X, (float)ir.Y, (float)ir.Width, (float)ir.Height, 0xCCE8FFFFu);
            }
            RenderVisible(dc, child);
            if (child.IsSubmenuOpen) RenderPanels(dc, child, enabled);
        }
    }
}

/// <summary>
/// Context menu host (matches System.Windows.Controls.ContextMenu):
/// parsed and retained with its rows (style Setter.Value and loader
/// paths); pop-up placement stays a boundary.
/// </summary>
public class ContextMenu : MenuBase
{
    /// <summary>Closed token (matches System.Windows.Controls.ContextMenu.ClosedEvent; style EventSetters bind through it).</summary>
    public static readonly RoutedEvent ClosedEvent = EventManager.RegisterRoutedEvent(
        "Closed", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ContextMenu));

    /// <summary>Opened token (matches System.Windows.Controls.ContextMenu.OpenedEvent; style EventSetters bind through it).</summary>
    public static readonly RoutedEvent OpenedEvent = EventManager.RegisterRoutedEvent(
        "Opened", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ContextMenu));

    /// <summary>Placement token (matches System.Windows.Controls.ContextMenu.PlacementProperty).</summary>
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.Register("Placement", typeof(PlacementMode), typeof(ContextMenu),
            new FrameworkPropertyMetadata(PlacementMode.Bottom));

    /// <summary>Placement-target token (matches System.Windows.Controls.ContextMenu.PlacementTargetProperty).</summary>
    public static readonly DependencyProperty PlacementTargetProperty =
        DependencyProperty.Register("PlacementTarget", typeof(UIElement), typeof(ContextMenu),
            new FrameworkPropertyMetadata(null));

    /// <summary>Horizontal-offset token (matches System.Windows.Controls.ContextMenu.HorizontalOffsetProperty).</summary>
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.Register("HorizontalOffset", typeof(double), typeof(ContextMenu),
            new FrameworkPropertyMetadata(0.0));

    /// <summary>Vertical-offset token (matches System.Windows.Controls.ContextMenu.VerticalOffsetProperty).</summary>
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register("VerticalOffset", typeof(double), typeof(ContextMenu),
            new FrameworkPropertyMetadata(0.0));

    private readonly List<MenuItem> _items = new();

    /// <summary>Menu rows in order.</summary>
    public IReadOnlyList<MenuItem> Items => _items;

    /// <summary>Adds a row (loader and code paths).</summary>
    public void AddItem(MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        AddChild(item);
    }

    private PlacementMode _placement = PlacementMode.Bottom;

    /// <summary>
    /// Anchor rule for the menu pop-up (matches
    /// System.Windows.Controls.ContextMenu.Placement; converted
    /// WindowChromeWorker._HandleNCRButtonUp and DropDownButton.OnClick open
    /// with Mouse/Right placement; retained like the Popup knobs, inline
    /// painting stays a boundary).
    /// </summary>
    public PlacementMode Placement
    {
        get => _placement;
        set
        {
            if (_placement == value) return;
            _placement = value;
            InvalidateMeasure();
        }
    }

    // FontFamily comes from Control (rows keep their own entry font until
    // value inheritance lands).
    private UIElement? _placementTarget;

    /// <summary>
    /// Anchor element for the menu pop-up (matches
    /// System.Windows.Controls.ContextMenu.PlacementTarget; converted
    /// LayoutAnchorableFloatingWindowControl.OpenContextMenu and
    /// LayoutAnchorControl.OnMouseRightButtonUp anchor here).
    /// </summary>
    public UIElement? PlacementTarget
    {
        get => _placementTarget;
        set
        {
            if (ReferenceEquals(_placementTarget, value)) return;
            _placementTarget = value;
            InvalidateMeasure();
            RefreshBindings();
        }
    }

    private double _horizontalOffset;

    /// <summary>
    /// Horizontal nudge of the menu pop-up (matches
    /// System.Windows.Controls.ContextMenu.HorizontalOffset; converted
    /// DropDownControlArea.PreviewMouseRightButtonUpCallback nudges here).
    /// </summary>
    public double HorizontalOffset
    {
        get => _horizontalOffset;
        set
        {
            if (_horizontalOffset == value) return;
            _horizontalOffset = value;
            InvalidateMeasure();
        }
    }

    private double _verticalOffset;

    /// <summary>
    /// Vertical nudge of the menu pop-up (matches
    /// System.Windows.Controls.ContextMenu.VerticalOffset; converted
    /// DropDownControlArea.PreviewMouseRightButtonUpCallback nudges here).
    /// </summary>
    public double VerticalOffset
    {
        get => _verticalOffset;
        set
        {
            if (_verticalOffset == value) return;
            _verticalOffset = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Menu dismissed (routed in real WPF; direct here).</summary>
    public event RoutedEventHandler? Closed;

    /// <summary>Menu opened (routed in real WPF; direct here).</summary>
    public event RoutedEventHandler? Opened;

    /// <summary>Closed entry (matches System.Windows.Controls.ContextMenu.OnClosed; converted ContextMenuEx does not override this one).</summary>
    protected virtual void OnClosed(RoutedEventArgs e) => Closed?.Invoke(this, e);

    /// <summary>Opened entry (matches System.Windows.Controls.ContextMenu.OnOpened; converted ContextMenuEx overrides it to refresh ItemsSource bindings).</summary>
    protected virtual void OnOpened(RoutedEventArgs e) => Opened?.Invoke(this, e);

    private bool _isOpen;

    /// <summary>
    /// Open state (false-to-true raises the target's ContextMenuOpening
    /// first — Handled aborts the open, like WPF â€” then Opened;
    /// true-to-false raises Closed, then the target's ContextMenuClosing).
    /// </summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value) return;
            if (value && PlacementTarget is FrameworkElement target)
            {
                var opening = new ContextMenuEventArgs(target, opening: true) { TargetElement = target };
                target.RaiseContextMenuOpening(opening);
                if (opening.Handled) return;
            }
            _isOpen = value;
            InvalidateMeasure();
            if (value) OnOpened(new RoutedEventArgs { Source = this });
            else
            {
                OnClosed(new RoutedEventArgs { Source = this });
                if (PlacementTarget is FrameworkElement closingTarget)
                    closingTarget.RaiseContextMenuClosing(new ContextMenuEventArgs(closingTarget, opening: false) { TargetElement = closingTarget });
            }
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        // A theme template owns the visuals when one applies; otherwise
        // popup placement stays a boundary: the menu paints through the
        // placement host, so inline rendering is a no-op (like VisualBrush).
        if (TemplateRoot is not null) { base.OnRender(dc); return; }
    }
}

/// <summary>Window: root visual with title + content.</summary>
/// <summary>
/// Window host (matches System.Windows.Window): a ContentControl, so the
/// shared Control chrome (Background/Foreground/BorderBrush/BorderThickness/
/// Template plus the font set) resolves through the inherited Control
/// dependency properties with WPF-identical sharing. The converted PC Space
/// Map App.xaml implicit Window style sets exactly this chrome.
/// </summary>
public class Window : ContentControl
{
    private readonly Application? _ownerApp;

    /// <summary>
    /// Registers with the current application (matches WPF: constructed
    /// windows join Application.Windows until closed).
    /// </summary>
    public Window()
    {
        _ownerApp = Application.Current;
        _ownerApp?.RegisterWindow(this);
    }

    // Window chrome is browser-owned and not rendered into the scene,
    // so Title carries no invalidation (no visual effect by design).
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Containing window for a dependency object (matches Window.GetWindow):
    /// walks the visual/logical parent chain to the outermost Window, or
    /// null when the object is windowless. Converted OxyPlot DPI sync uses
    /// it as the ancestor-transform fallback.
    /// </summary>
    public static Window? GetWindow(DependencyObject dependencyObject)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        Window? found = null;
        for (var current = dependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Window window) found = window;
        }
        return found;
    }

    /// <summary>
    /// Icon source URI, parsed and retained but not rendered (window chrome
    /// is browser-owned; same policy as ResizeMode in the XAML loader).
    /// </summary>
    public string? IconSource { get; set; }

    private bool _isVisible = true;

    /// <summary>
    /// Whether the window renders and participates in input. Closed dialogs
    /// hide instead of unrooting, so re-show is cheap. Hiding repaints.
    /// Hides the read-only UIElement.IsVisible (no settable member exists
    /// on Window in real WPF either; converted code never sets it) so the
    /// dialog/host show/hide protocol keeps working while UIElement-typed
    /// readers see the same effective value through
    /// <see cref="UIElement.IsVisibleCore"/>.
    /// </summary>
    public new bool IsVisible
    {
        get => IsVisibleCore && ((Parent as UIElement)?.IsVisible ?? true);
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            IsActive = value;
            InvalidateMeasure();
            RefreshIsVisible();
            if (value) Activated?.Invoke(this, EventArgs.Empty);
            else Deactivated?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Window self-visibility term (folds the dialog show flag into the
    /// shared UIElement.IsVisible computation, so hiding a window flips the
    /// effective value and raises IsVisibleChanged like WPF).
    /// </summary>
    internal override bool IsVisibleCore => _isVisible && Visibility == Visibility.Visible;

    /// <summary>
    /// Dialog result (true/false). Plain value with no auto-close magic:
    /// <see cref="WpfInputManager.CloseDialog"/> and
    /// <see cref="WasmWpfHost.CloseDialog"/> record the passed result.
    /// </summary>
    public bool? DialogResult { get; set; }

    /// <summary>Resize-behavior token (matches System.Windows.Window.ResizeModeProperty).</summary>
    public static readonly DependencyProperty ResizeModeProperty =
        DependencyProperty.Register("ResizeMode", typeof(ResizeMode), typeof(Window),
            new PropertyMetadata(ResizeMode.CanResize));

    /// <summary>
    /// Window chrome knobs accepted by the XAML loader and converters
    /// (browser chrome is browser-owned; parsed and queryable, not rendered).
    /// </summary>
    public ResizeMode ResizeMode
    {
        get => (ResizeMode)(GetValue(ResizeModeProperty) ?? ResizeMode.CanResize);
        set => SetValue(ResizeModeProperty, value);
    }

    /// <summary>Chrome style token (matches System.Windows.Window.WindowStyleProperty).</summary>
    public static readonly DependencyProperty WindowStyleProperty =
        DependencyProperty.Register("WindowStyle", typeof(WindowStyle), typeof(Window),
            new PropertyMetadata(WindowStyle.SingleBorderWindow));

    /// <summary>
    /// Chrome style (matches System.Windows.Window.WindowStyle; parsed and
    /// queryable like the other chrome knobs, browser chrome stays
    /// browser-owned and nothing repaints on change).
    /// </summary>
    public WindowStyle WindowStyle
    {
        get => (WindowStyle)(GetValue(WindowStyleProperty) ?? WindowStyle.SingleBorderWindow);
        set => SetValue(WindowStyleProperty, value);
    }

    /// <summary>Transparency token (matches System.Windows.Window.AllowsTransparencyProperty).</summary>
    public static readonly DependencyProperty AllowsTransparencyProperty =
        DependencyProperty.Register("AllowsTransparency", typeof(bool), typeof(Window),
            new PropertyMetadata(false));

    /// <summary>
    /// Per-pixel transparency (matches System.Windows.Window.AllowsTransparency;
    /// default false like WPF; retained only, the host compositor owns real
    /// transparency so nothing repaints on change).
    /// </summary>
    public bool AllowsTransparency
    {
        get => (bool)(GetValue(AllowsTransparencyProperty) ?? false);
        set => SetValue(AllowsTransparencyProperty, value);
    }

    /// <summary>Left edge (matches System.Windows.Window.LeftProperty: the shared Canvas.LeftProperty instance).</summary>
    public static readonly DependencyProperty LeftProperty =
        Canvas.LeftProperty.AddOwner(typeof(Window));

    /// <summary>
    /// Left edge in device-independent pixels (matches
    /// System.Windows.Window.Left; default NaN like WPF; retained only, the
    /// host window stays host-owned so no arrange follows a change).
    /// </summary>
    public double Left
    {
        get => (double)(GetValue(LeftProperty) ?? double.NaN);
        set => SetValue(LeftProperty, value);
    }

    /// <summary>Top edge (matches System.Windows.Window.TopProperty: the shared Canvas.TopProperty instance).</summary>
    public static readonly DependencyProperty TopProperty =
        Canvas.TopProperty.AddOwner(typeof(Window));

    /// <summary>
    /// Top edge in device-independent pixels (matches
    /// System.Windows.Window.Top; default NaN like WPF; retained only, the
    /// host window stays host-owned so no arrange follows a change).
    /// </summary>
    public double Top
    {
        get => (double)(GetValue(TopProperty) ?? double.NaN);
        set => SetValue(TopProperty, value);
    }

    /// <summary>Icon token (matches System.Windows.Window.IconProperty).</summary>
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register("Icon", typeof(ImageSource), typeof(Window),
            new PropertyMetadata(null));

    /// <summary>
    /// Window icon (matches System.Windows.Window.Icon; retained, never
    /// rendered: window chrome is browser-owned like IconSource).
    /// </summary>
    public ImageSource? Icon
    {
        get => (ImageSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Owner window (matches System.Windows.Window.Owner; plain CLR like WPF,
    /// which exposes no OwnerProperty; retained only, the browser has no
    /// Win32 owner chain to update).
    /// </summary>
    public Window? Owner { get; set; }

    /// <summary>
    /// Restore bounds (matches System.Windows.Window.RestoreBounds; the
    /// session never minimizes through the OS chrome, so this honestly
    /// reports Empty instead of fabricating screen coordinates).
    /// </summary>
    public Rect RestoreBounds => Rect.Empty;

    /// <summary>Autosize-rule token (matches Window.SizeToContentProperty).</summary>
    public static readonly DependencyProperty SizeToContentProperty =
        DependencyProperty.Register("SizeToContent", typeof(SizeToContent), typeof(Window),
            new FrameworkPropertyMetadata(SizeToContent.Manual));

    private SizeToContent _sizeToContent;

    /// <summary>
    /// Autosize rule (matches Window.SizeToContent): parsed and retained from
    /// XAML and converted setters. Browser window bounds stay host-owned, so
    /// this records the rule and re-measures instead of resizing the host.
    /// </summary>
    public SizeToContent SizeToContent
    {
        get => _sizeToContent;
        set
        {
            if (_sizeToContent == value) return;
            _sizeToContent = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Taskbar-state token (matches Window.TaskbarItemInfoProperty).</summary>
    public static readonly DependencyProperty TaskbarItemInfoProperty =
        DependencyProperty.Register("TaskbarItemInfo", typeof(TaskbarItemInfo), typeof(Window),
            new FrameworkPropertyMetadata(null));

    private TaskbarItemInfo? _taskbarItemInfo;

    /// <summary>
    /// Taskbar state (matches Window.TaskbarItemInfo): lazily created on
    /// first read like WPF, so converted progress/overlay writers never meet
    /// a null slot. The browser has no taskbar; values are retained.
    /// </summary>
    public TaskbarItemInfo TaskbarItemInfo
    {
        get => _taskbarItemInfo ??= new TaskbarItemInfo();
        set => _taskbarItemInfo = value;
    }

    private WindowState _windowState = WindowState.Normal;

    /// <summary>Window state (setting raises StateChanged, like WPF).</summary>
    public WindowState WindowState
    {
        get => _windowState;
        set
        {
            if (_windowState == value) return;
            _windowState = value;
            InvalidateMeasure();
            OnStateChanged(EventArgs.Empty);
        }
    }

    /// <summary>Raised after WindowState changes (converted chrome buttons refresh here).</summary>
    public event EventHandler? StateChanged;

    /// <summary>State-change entry (matches System.Windows.Window.OnStateChanged).</summary>
    protected virtual void OnStateChanged(EventArgs e) => StateChanged?.Invoke(this, e);

    /// <summary>Raised when the window activates (fired by Show, like WPF).</summary>
    public event EventHandler? Activated;

    /// <summary>
    /// DPI-change notification (matches Window.DpiChanged): the host session
    /// raises this when the display scale moves; converted recorder/editor
    /// windows rescale capture math off NewDpi here.
    /// </summary>
    public event DpiChangedEventHandler? DpiChanged;

    /// <summary>Raises DpiChanged with old/new session scales (host DPI sync).</summary>
    internal void RaiseDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        var args = new DpiChangedEventArgs(oldDpi, newDpi) { Source = this };
        DpiChanged?.Invoke(this, args);
    }

    /// <summary>Raised when the window deactivates (fired by Hide/Close, like WPF).</summary>
    public event EventHandler? Deactivated;

    /// <summary>
    /// Location-change notification (matches Window.LocationChanged;
    /// converted windows subscribe/unsubscribe here). The browser host owns
    /// bounds, so engine moves never raise it, like HasShutdownStarted
    /// staying false.
    /// </summary>
    public event EventHandler? LocationChanged;

    /// <summary>Whether the window is the active one (matches Window.IsActive).</summary>
    public bool IsActive { get; internal set; } = true;

    /// <summary>Taskbar token (matches System.Windows.Window.ShowInTaskbarProperty).</summary>
    public static readonly DependencyProperty ShowInTaskbarProperty =
        DependencyProperty.Register("ShowInTaskbar", typeof(bool), typeof(Window),
            new PropertyMetadata(true));

    /// <summary>
    /// Taskbar presence (matches System.Windows.Window.ShowInTaskbar; default
    /// true like WPF; retained only, the browser has no taskbar to update).
    /// </summary>
    public bool ShowInTaskbar
    {
        get => (bool)(GetValue(ShowInTaskbarProperty) ?? true);
        set => SetValue(ShowInTaskbarProperty, value);
    }

    /// <summary>Topmost token (matches System.Windows.Window.TopmostProperty).</summary>
    public static readonly DependencyProperty TopmostProperty =
        DependencyProperty.Register("Topmost", typeof(bool), typeof(Window),
            new PropertyMetadata(false));

    /// <summary>
    /// Topmost presence (matches System.Windows.Window.Topmost; default
    /// false like WPF; retained only, the browser owns real z-order).
    /// </summary>
    public bool Topmost
    {
        get => (bool)(GetValue(TopmostProperty) ?? false);
        set => SetValue(TopmostProperty, value);
    }

    public WindowStartupLocation WindowStartupLocation { get; set; } = WindowStartupLocation.Manual;

    /// <summary>
    /// Attached custom chrome (null when none). Set through
    /// <see cref="WindowChrome.SetWindowChrome"/>; the caption band, glass
    /// base and frame edges paint in <see cref="OnRender"/> and gate
    /// caption hit-testing. Content still measures and arranges full-window.
    /// </summary>
    public WindowChrome? Chrome { get; internal set; }

    /// <summary>Input manager owning the modal session (null when not modal).</summary>
    internal WpfInputManager? DialogOwner { get; set; }

    /// <summary>Whether this window is currently shown as a modal dialog.</summary>
    public bool IsModal => DialogOwner is not null;

    /// <summary>
    /// Raised when the window closes (matches System.Windows.Window.Closed:
    /// EventHandler like WPF, so converted add_Closed(EventHandler)
    /// subscribers bind; modal teardown runs first).
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Closing gate (matches System.Windows.Window.Closing, verified against
    /// the .NET 9 WindowsDesktop reference assemblies): cancelable; the PC
    /// Space Map MainWindow persists state here.
    /// </summary>
    public event System.ComponentModel.CancelEventHandler? Closing;

    /// <summary>Activation-on-show token (matches System.Windows.Window.ShowActivatedProperty).</summary>
    public static readonly DependencyProperty ShowActivatedProperty =
        DependencyProperty.Register("ShowActivated", typeof(bool), typeof(Window),
            new PropertyMetadata(true));

    /// <summary>
    /// Activation-on-show gate (matches System.Windows.Window.ShowActivated;
    /// default true like real WPF; false shows without activating).
    /// </summary>
    public bool ShowActivated
    {
        get => (bool)(GetValue(ShowActivatedProperty) ?? true);
        set => SetValue(ShowActivatedProperty, value);
    }

    /// <summary>
    /// Source-initialized gate (matches System.Windows.Window.SourceInitialized;
    /// retained but never auto-raised: the session composes with no HWND, so
    /// no source ever initializes; converted WindowChromeWorker glass-frame
    /// handlers stay subscribed but inert instead of crashing on a null
    /// source).
    /// </summary>
    public event EventHandler? SourceInitialized;

    /// <summary>Source-initialized entry (matches System.Windows.Window.OnSourceInitialized).</summary>
    protected virtual void OnSourceInitialized(EventArgs e) => SourceInitialized?.Invoke(this, e);

    /// <summary>
    /// Shows the window (matches System.Windows.Window.Show): repaints and
    /// activates, unless <see cref="ShowActivated"/> is false. The first
    /// shown window becomes the application's MainWindow when none is
    /// assigned, like real WPF (converted code-driven startups Show their
    /// main window without assigning it, e.g. the PC Space Map OnStartup).
    /// </summary>
    public void Show()
    {
        if (_ownerApp is not null && _ownerApp.MainWindow is null)
            _ownerApp.MainWindow = this;
        IsVisible = true;
        if (!ShowActivated) IsActive = false;
    }

    /// <summary>Hides the window (matches System.Windows.Window.Hide).</summary>
    public void Hide() => IsVisible = false;

    /// <summary>
    /// Attempts to bring the window to the foreground (matches
    /// System.Windows.Window.Activate, which returns bool): marks the
    /// session-active window without re-raising Activated, and reports false
    /// for a hidden window like WPF instead of showing it.
    /// </summary>
    public bool Activate()
    {
        if (!IsVisible) return false;
        IsActive = true;
        return true;
    }

    /// <summary>
    /// Shows the window modally (matches System.Windows.Window.ShowDialog,
    /// which returns bool?): the session has no blocking modal pump, so this
    /// shows the window like Show and honestly reports the current
    /// DialogResult (null unless converted code set one) instead of
    /// fabricating a dialog result.
    /// </summary>
    public bool? ShowDialog()
    {
        Show();
        return DialogResult;
    }

    /// <summary>
    /// Closes the window: Closing fires first and may cancel (like real WPF);
    /// modal dialogs unmodal+hide, modeless windows hide.
    /// </summary>
    public void Close()
    {
        var closing = new System.ComponentModel.CancelEventArgs();
        OnClosing(closing);
        if (closing.Cancel) return;
        if (DialogOwner is not null) DialogOwner.CloseDialog(this, DialogResult);
        else { IsVisible = false; RaiseClosed(); }
    }

    /// <summary>Closing entry (matches System.Windows.Window.OnClosing).</summary>
    protected virtual void OnClosing(System.ComponentModel.CancelEventArgs e) => Closing?.Invoke(this, e);

    /// <summary>Closed entry (matches System.Windows.Window.OnClosed).</summary>
    protected virtual void OnClosed(EventArgs e) => Closed?.Invoke(this, e);

    internal void RaiseClosed()
    {
        _ownerApp?.UnregisterWindow(this);
        OnClosed(EventArgs.Empty);
    }

    /// <summary>Windows take keyboard focus only as modal dialogs.</summary>
    public override bool Focusable { get => IsModal; set { } }

    private object? _content;

    /// <summary>
    /// Window content (matches System.Windows.Window.Content): the single
    /// hosted visual (XAML child, dialogs, Hydrate copies). Non-visual values
    /// are retained without visual hosting, like ContentControl; an empty
    /// window still reads null so loader attach guards keep working.
    /// </summary>
    public override object Content
    {
        get
        {
            if (Children.Count > 0) return Children[0];
            return _content!;
        }
        set
        {
            var oldVisual = Children.Count > 0 ? Children[0] : null;
            if (ReferenceEquals(oldVisual, value)) return;
            if (oldVisual is not null) RemoveChild(oldVisual);
            _content = value is UIElement ? null : value;
            if (value is UIElement visual) AddChild(visual);
            else InvalidateMeasure();
        }
    }

    private uint? _backgroundRgba;

    private uint? _borderRgba;

    /// <summary>Syncs the fill cache from the inherited Background (null keeps the legacy white/glass).</summary>
    protected override void OnBackgroundChanged(Brush? brush)
    {
        _backgroundRgba = brush is SolidColorBrush solid ? solid.ToRgba() : null;
    }

    /// <summary>Syncs the edge cache from the inherited BorderBrush (null paints no ring).</summary>
    protected override void OnBorderBrushChanged(Brush? brush)
    {
        _borderRgba = brush is SolidColorBrush solid ? solid.ToRgba() : null;
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        // A set Template replaces the default chrome (Button parity).
        if (TemplateRoot is not null)
        {
            TemplateRoot.Measure(available);
            return TemplateRoot.DesiredSize;
        }
        if (Content is not UIElement visual) return new WpfSize(800, 600);
        var m = GetContentMargin();
        visual.Measure(new WpfSize(Math.Max(0, available.Width - m.Left - m.Right), Math.Max(0, available.Height - m.Top - m.Bottom)));
        var s = visual.DesiredSize;
        return new WpfSize(s.Width + m.Left + m.Right, s.Height + m.Top + m.Bottom);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        // A set Template replaces the default chrome (Button parity).
        if (TemplateRoot is not null)
        {
            TemplateRoot.Arrange(finalRect);
            return;
        }
        if (Content is not UIElement visual) return;
        var m = GetContentMargin();
        visual.Arrange(new WpfRect(
            finalRect.X + m.Left, finalRect.Y + m.Top,
            Math.Max(0, finalRect.Width - m.Left - m.Right),
            Math.Max(0, finalRect.Height - m.Top - m.Bottom)));
    }

    protected override WpfRect ClampArrange(WpfRect slot)
    {
        // Host-viewport confinement (browser window bounds stay
        // host-owned, like SizeToContent): a window is arranged by its
        // host at the scene size, so an explicit Width/Height larger than
        // the slot is confined to it instead of overflowing (the
        // converted 1280x720 ExampleBrowser window clipped the 800x600
        // scene on the right/bottom, hiding the plot and stranding the
        // renderer panel below the fold). The explicit size still wins
        // inside the slot; dialogs arranged at their own size are
        // unaffected.
        var width = double.IsNaN(Width) ? Width : Math.Min(Math.Max(0, Width), Math.Max(0, slot.Width));
        var height = double.IsNaN(Height) ? Height : Math.Min(Math.Max(0, Height), Math.Max(0, slot.Height));
        return ArrangeAligned(slot, width, height, HorizontalAlignment, VerticalAlignment,
            Math.Max(0, DesiredSize.Width), Math.Max(0, DesiredSize.Height));
    }

    private Thickness GetContentMargin() =>
        Content is FrameworkElement fe ? fe.Margin : new Thickness(0);

    protected override void OnRender(DrawingContext dc)
    {
        // A set Template replaces the default chrome (Button parity).
        if (TemplateRoot is not null)
        {
            RenderVisible(dc, TemplateRoot);
            return;
        }
        var r = RenderRect;
        var chrome = Chrome;
        var glass = chrome is not null && chrome.GlassCoversAll;
        var legacy = glass ? WindowChrome.GlassRgba : 0xFFFFFFFF;
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height,
            Background is null ? legacy : BrushRender.ResolveFill(Background, _backgroundRgba ?? legacy));
        chrome?.PaintChrome(dc, r);
        if (_borderRgba is { } border) PaintBorder(dc, r, border);
        if (Content is UIElement visual) RenderVisible(dc, visual);
    }

    /// <summary>
    /// Paints the border inside the window edges (uniform and non-uniform
    /// alike), so a transparent Background keeps the legacy fill visible
    /// inside the ring. The ring never grows layout (dialogs keep legacy
    /// measure); an unset BorderBrush paints nothing.
    /// </summary>
    private void PaintBorder(DrawingContext dc, WpfRect r, uint border)
    {
        var t = BorderThickness;
        var left = Math.Min(t.Left, r.Width);
        var right = Math.Min(t.Right, r.Width);
        var top = Math.Min(t.Top, r.Height);
        var bottom = Math.Min(t.Bottom, r.Height);
        if (left > 0)
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)left, (float)r.Height, border);
        if (right > 0)
            dc.DrawRectangle((float)(r.X + r.Width - right), (float)r.Y, (float)right, (float)r.Height, border);
        if (top > 0)
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)top, border);
        if (bottom > 0)
            dc.DrawRectangle((float)r.X, (float)(r.Y + r.Height - bottom), (float)r.Width, (float)bottom, border);
    }
}

/// <summary>UIElement-typed fallback so loader code holding a UIElement receiver compiles; hosts only on FrameworkElement.</summary>
internal static class UiElementAttachExtensions
{
    internal static void AttachVisualChild(this UIElement parent, UIElement child)
    {
        if (parent is FrameworkElement fe) fe.AttachVisualChild(child);
    }
}
