namespace DesktopWasm.Wpf;

// ScreenToGif primitives slice (crop overlay, spinners, split-button menus):
// the editor's CroppingAdorner/ElementAdorner drag Thumb handles with
// DragDelta/DragStarted through the real pointer path, the UpDown spinners
// subclass RepeatButton for press-and-hold repeat, and SplitButton plus the
// Button/ComboBox templates open Popup panels with placement and light
// dismiss. Retained limits: no ControlTemplate inflation (template Popup/
// Thumb/RepeatButton shells still do not instantiate), no PopupAnimation
// fade, no ToolTip (next slice), no Visual.TranslatePoint for adorner
// rotation math (ElementAdorner still needs VisualCollection).

/// <summary>
/// Popup anchor rule (matches System.Windows.Controls.Primitives.PlacementMode,
/// declaration order preserved so converted IL constants keep their values).
/// </summary>
public enum PlacementMode
{
    Absolute,
    Relative,
    Bottom,
    Center,
    Right,
    Left,
    Top,
    Mouse,
    MousePoint,
    AbsolutePoint,
    RelativePoint,
    Custom,
}

/// <summary>
/// Thumb drag payload (matches System.Windows.Controls.Primitives.DragDeltaEventArgs).
/// Converted crop handlers read HorizontalChange/VerticalChange; the keyboard
/// nudges construct these directly.
/// </summary>
public class DragDeltaEventArgs : RoutedEventArgs
{
    public DragDeltaEventArgs(double horizontalChange, double verticalChange)
    {
        HorizontalChange = horizontalChange;
        VerticalChange = verticalChange;
    }

    /// <summary>Pointer travel since the previous drag step.</summary>
    public double HorizontalChange { get; }

    /// <summary>Pointer travel since the previous drag step.</summary>
    public double VerticalChange { get; }
}

/// <summary>Thumb drag handler shape (matches System.Windows.Controls.Primitives.DragDeltaEventHandler).</summary>
public delegate void DragDeltaEventHandler(object sender, DragDeltaEventArgs e);

/// <summary>
/// Custom-placement axis (matches System.Windows.Controls.Primitives.PopupPrimaryAxis).
/// Slice-69 MaterialDesignDemo entry prerequisite (the popup themes hand the
/// callback a placement plus this axis); numerics verified live (None=0,
/// Horizontal=1, Vertical=2).
/// </summary>
public enum PopupPrimaryAxis
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
}

/// <summary>
/// Custom popup placement (matches System.Windows.Controls.Primitives.CustomPopupPlacement):
/// an offset point plus the axis the popup prefers when the first choice
/// does not fit. Value type with value equality, like the original.
/// </summary>
public readonly struct CustomPopupPlacement : IEquatable<CustomPopupPlacement>
{
    public CustomPopupPlacement(Point point, PopupPrimaryAxis primaryAxis)
    {
        Point = point;
        PrimaryAxis = primaryAxis;
    }

    /// <summary>Offset of the popup relative to the target.</summary>
    public Point Point { get; }

    /// <summary>Preferred axis when the first choice does not fit.</summary>
    public PopupPrimaryAxis PrimaryAxis { get; }

    public bool Equals(CustomPopupPlacement other) =>
        Point.Equals(other.Point) && PrimaryAxis == other.PrimaryAxis;
    public override bool Equals(object? obj) => obj is CustomPopupPlacement other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Point, PrimaryAxis);
    public static bool operator ==(CustomPopupPlacement left, CustomPopupPlacement right) => left.Equals(right);
    public static bool operator !=(CustomPopupPlacement left, CustomPopupPlacement right) => !left.Equals(right);
}

/// <summary>
/// Custom-placement callback shape (matches
/// System.Windows.Controls.Primitives.CustomPopupPlacementCallback):
/// computes candidate placements from the popup size, target size and offset.
/// </summary>
public delegate CustomPopupPlacement[] CustomPopupPlacementCallback(Size popupSize, Size targetSize, Point offset);

/// <summary>
/// Drag-start payload (matches System.Windows.Controls.Primitives.DragStartedEventArgs).
/// Offsets are the press point relative to the thumb origin.
/// </summary>
public class DragStartedEventArgs : RoutedEventArgs
{
    public DragStartedEventArgs(double horizontalOffset, double verticalOffset)
    {
        HorizontalOffset = horizontalOffset;
        VerticalOffset = verticalOffset;
    }

    /// <summary>Press X relative to the thumb origin.</summary>
    public double HorizontalOffset { get; }

    /// <summary>Press Y relative to the thumb origin.</summary>
    public double VerticalOffset { get; }
}

/// <summary>Drag-start handler shape (matches System.Windows.Controls.Primitives.DragStartedEventHandler).</summary>
public delegate void DragStartedEventHandler(object sender, DragStartedEventArgs e);

/// <summary>
/// Drag-end payload (matches System.Windows.Controls.Primitives.DragCompletedEventArgs).
/// Changes carry the total pointer travel since the press; Canceled is true
/// for CancelDrag/pointer-cancel, false for a normal release.
/// </summary>
public class DragCompletedEventArgs : RoutedEventArgs
{
    public DragCompletedEventArgs(double horizontalChange, double verticalChange, bool canceled)
    {
        HorizontalChange = horizontalChange;
        VerticalChange = verticalChange;
        Canceled = canceled;
    }

    /// <summary>Total pointer travel since the press.</summary>
    public double HorizontalChange { get; }

    /// <summary>Total pointer travel since the press.</summary>
    public double VerticalChange { get; }

    /// <summary>True when the drag was cancelled instead of released.</summary>
    public bool Canceled { get; }
}

/// <summary>Drag-end handler shape (matches System.Windows.Controls.Primitives.DragCompletedEventHandler).</summary>
public delegate void DragCompletedEventHandler(object sender, DragCompletedEventArgs e);

/// <summary>
/// Drag handle (matches System.Windows.Controls.Primitives.Thumb): press
/// captures the pointer through the real input path, moves raise DragDelta
/// with per-step deltas, release raises DragCompleted with the total travel,
/// cancel (or CancelDrag) completes as canceled. Paints a plain grip box;
/// template inflation stays a boundary, so styled thumbs keep this paint.
/// </summary>
public class Thumb : Control
{
    /// <summary>Drag-start token (matches Thumb.DragStartedEvent).</summary>
    public static readonly RoutedEvent DragStartedEvent = EventManager.RegisterRoutedEvent(
        "DragStarted", RoutingStrategy.Bubble, typeof(DragStartedEventHandler), typeof(Thumb));

    /// <summary>Drag-step token (matches Thumb.DragDeltaEvent).</summary>
    public static readonly RoutedEvent DragDeltaEvent = EventManager.RegisterRoutedEvent(
        "DragDelta", RoutingStrategy.Bubble, typeof(DragDeltaEventHandler), typeof(Thumb));

    /// <summary>Drag-end token (matches Thumb.DragCompletedEvent).</summary>
    public static readonly RoutedEvent DragCompletedEvent = EventManager.RegisterRoutedEvent(
        "DragCompleted", RoutingStrategy.Bubble, typeof(DragCompletedEventHandler), typeof(Thumb));

    private static readonly DependencyPropertyKey IsDraggingKey =
        DependencyProperty.RegisterReadOnly("IsDragging", typeof(bool), typeof(Thumb),
            new FrameworkPropertyMetadata(false));

    /// <summary>Drag-state token (matches Thumb.IsDraggingProperty; read-only like real WPF).</summary>
    public static readonly DependencyProperty IsDraggingProperty = IsDraggingKey.DependencyProperty;

    /// <summary>Whether a drag is in progress (press started, not yet released/cancelled).</summary>
    public bool IsDragging => (bool)(GetValue(IsDraggingProperty) ?? false);

    /// <summary>Drag-start notification (routed in real WPF; direct here).</summary>
    public event DragStartedEventHandler? DragStarted;

    /// <summary>Drag-step notification (routed in real WPF; direct here).</summary>
    public event DragDeltaEventHandler? DragDelta;

    /// <summary>Drag-end notification (routed in real WPF; direct here).</summary>
    public event DragCompletedEventHandler? DragCompleted;

    /// <summary>Thumbs take focus (store-backed; the Control default is true).</summary>
    public override bool Focusable
    {
        get => (bool?)GetValue(FocusableProperty) ?? true;
        set => SetValue(FocusableProperty, value);
    }

    private WpfPoint _pressPoint;
    private WpfPoint _lastPoint;

    /// <summary>Ends the drag as cancelled (matches Thumb.CancelDrag).</summary>
    public void CancelDrag()
    {
        if (IsDragging) EndDrag(canceled: true);
    }

    /// <summary>Raises DragStarted (matches Thumb.OnDragStarted).</summary>
    protected virtual void OnDragStarted(DragStartedEventArgs e) => DragStarted?.Invoke(this, e);

    /// <summary>Raises DragDelta (matches Thumb.OnDragDelta).</summary>
    protected virtual void OnDragDelta(DragDeltaEventArgs e) => DragDelta?.Invoke(this, e);

    /// <summary>Raises DragCompleted (matches Thumb.OnDragCompleted).</summary>
    protected virtual void OnDragCompleted(DragCompletedEventArgs e) => DragCompleted?.Invoke(this, e);

    private void RaiseStarted(WpfPoint at)
    {
        OnDragStarted(new DragStartedEventArgs(at.X - RenderRect.X, at.Y - RenderRect.Y)
        {
            RoutedEvent = DragStartedEvent,
            Source = this,
            OriginalSource = this,
        });
    }

    private void RaiseDelta(WpfPoint step)
    {
        OnDragDelta(new DragDeltaEventArgs(step.X, step.Y)
        {
            RoutedEvent = DragDeltaEvent,
            Source = this,
            OriginalSource = this,
        });
    }

    private void EndDrag(bool canceled)
    {
        var total = new WpfPoint(_lastPoint.X - _pressPoint.X, _lastPoint.Y - _pressPoint.Y);
        SetValue(IsDraggingKey, false);
        Mouse.Capture(null);
        InvalidateVisual();
        OnDragCompleted(new DragCompletedEventArgs(total.X, total.Y, canceled)
        {
            RoutedEvent = DragCompletedEvent,
            Source = this,
            OriginalSource = this,
        });
    }

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        if (e.Source is not Core.WasmInputEvent.Pointer p) return;
        var at = new WpfPoint(p.Args.X, p.Args.Y);
        switch (p.Args.Kind)
        {
            case Core.WasmPointerKind.Down when e.IsLeftButtonDown:
                _pressPoint = at;
                _lastPoint = at;
                SetValue(IsDraggingKey, true);
                Mouse.Capture(this);
                InvalidateVisual();
                RaiseStarted(at);
                e.Handled = true;
                break;
            case Core.WasmPointerKind.Move when IsDragging:
                RaiseDelta(new WpfPoint(at.X - _lastPoint.X, at.Y - _lastPoint.Y));
                _lastPoint = at;
                e.Handled = true;
                break;
            case Core.WasmPointerKind.Up when IsDragging:
                _lastPoint = at;
                EndDrag(canceled: false);
                e.Handled = true;
                break;
            case Core.WasmPointerKind.Cancel when IsDragging:
                EndDrag(canceled: true);
                break;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        var enabled = IsEffectivelyEnabled;
        var edge = enabled ? 0x707070FFu : 0x909090FFu;
        var face = !enabled ? 0xCCCCCCFFu : IsDragging ? 0x707070FFu : 0xFFFFFFFFu;
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, edge);
        if (r.Width > 2 && r.Height > 2)
            dc.DrawRectangle((float)r.X + 1, (float)r.Y + 1, (float)r.Width - 2, (float)r.Height - 2, face);
    }
}

/// <summary>
/// Press-and-hold button (matches System.Windows.Controls.Primitives.RepeatButton):
/// press arms a dispatcher-timer repeat (first Click after Delay, then every
/// Interval), release disarms. A quick press+release fires exactly one Click
/// on release, matching the scrollbar-arrow behavior converted spinners rely
/// on; cancel fires nothing. Defaults (500/33ms) match the default keyboard
/// delay/speed the framework defaults resolve to. Paint and content come from
/// Button; only the press/click contract is replaced.
/// </summary>
public class RepeatButton : Button
{
    private bool _pressed;
    private bool _fired;
    private readonly DispatcherTimer _timer;

    public RepeatButton()
    {
        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) => OnTick();
    }

    /// <summary>Wait before the first repeat Click, in milliseconds (default 500).</summary>
    public int Delay { get; set; } = 500;

    /// <summary>Gap between repeat Clicks, in milliseconds (default 33).</summary>
    public int Interval { get; set; } = 33;

    private void OnTick()
    {
        if (!_pressed)
        {
            _timer.Stop();
            return;
        }
        _fired = true;
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, Interval));
        RaiseClick();
    }

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        if (e.Source is not Core.WasmInputEvent.Pointer p) return;
        switch (p.Args.Kind)
        {
            case Core.WasmPointerKind.Down when e.IsLeftButtonDown:
                _pressed = true;
                _fired = false;
                SetIsPressed(true);
                _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, Delay));
                _timer.Start();
                e.Handled = true;
                break;
            case Core.WasmPointerKind.Up when p.Args.Kind == Core.WasmPointerKind.Up:
                if (!_pressed) break;
                _pressed = false;
                SetIsPressed(false);
                _timer.Stop();
                var at = new WpfPoint(p.Args.X, p.Args.Y);
                // Quick press+release clicks once; a hold already fired
                // through the timer; release-outside never clicks.
                if (!_fired && HitTest(at))
                {
                    RaiseClick();
                    e.Handled = true;
                }
                break;
            case Core.WasmPointerKind.Cancel:
                _pressed = false;
                SetIsPressed(false);
                _timer.Stop();
                break;
        }
    }
}

/// <summary>Open animation (matches System.Windows.Controls.Primitives.PopupAnimation;
/// retained only: the engine paints popups inline with no animation
/// compositing, like AllowsTransparency. Member values verified against the
/// .NET 9 WindowsDesktop reference assemblies.)</summary>
public enum PopupAnimation
{
    None = 0,
    Fade = 1,
    Slide = 2,
    Scroll = 3,
}

/// <summary>
/// Floating child host (matches System.Windows.Controls.Primitives.Popup):
/// Child measures without affecting layout, arranges at the Placement-anchored
/// rect (PlacementTarget, else the parent, else the popup slot; Mouse uses the
/// last pointer position; Absolute uses the offsets as coordinates), paints
/// inline when open, and hit-tests the open child rect so presses route into
/// it. StaysOpen=false popups light-dismiss on outside presses through the
/// input manager; Escape closes; focus moves into the child on open and back
/// to the placement target on close. Retained limits: inline paint (covered
/// siblings can overlap, like ComboBox dropdowns), no fade animation, no
/// screen-edge clamping.
/// </summary>
public class Popup : FrameworkElement
{
    private UIElement? _child;
    private bool _isOpen;

    private static readonly HashSet<Popup> LightDismiss = new();
    private static readonly object LightDismissGate = new();

    /// <summary>
    /// Popup content identity (matches
    /// System.Windows.Controls.Primitives.Popup.ChildProperty; the CLR
    /// wrapper keeps the attach/detach behavior and dual-lands the DP store).
    /// </summary>
    public static readonly DependencyProperty ChildProperty =
        DependencyProperty.Register("Child", typeof(UIElement), typeof(Popup),
            new FrameworkPropertyMetadata((UIElement?)null));

    /// <summary>Floating content (null when empty).</summary>
    public UIElement? Child
    {
        get => _child;
        set
        {
            if (ReferenceEquals(_child, value)) return;
            if (_child is not null) DetachVisualChild(_child);
            _child = value;
            if (_child is not null) AttachVisualChild(_child);
            SetValue(ChildProperty, value);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Anchor-rule identity (matches
    /// System.Windows.Controls.Primitives.Popup.PlacementProperty; the CLR
    /// wrapper keeps the default and dual-lands the DP store).
    /// </summary>
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.Register("Placement", typeof(PlacementMode), typeof(Popup),
            new FrameworkPropertyMetadata(PlacementMode.Bottom));

    private PlacementMode _placement = PlacementMode.Bottom;

    /// <summary>Anchor rule (default Bottom, like WPF).</summary>
    public PlacementMode Placement
    {
        get => _placement;
        set
        {
            _placement = value;
            SetValue(PlacementProperty, value);
        }
    }

    /// <summary>Anchor element (null anchors to the parent, else the popup slot).</summary>
    public UIElement? PlacementTarget { get; set; }

    /// <summary>Anchor box override (non-empty replaces the target rect).</summary>
    public Rect PlacementRectangle { get; set; } = Rect.Empty;

    /// <summary>
    /// Horizontal-nudge identity (matches
    /// System.Windows.Controls.Primitives.Popup.HorizontalOffsetProperty;
    /// the CLR wrapper dual-lands the DP store).
    /// </summary>
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.Register("HorizontalOffset", typeof(double), typeof(Popup),
            new FrameworkPropertyMetadata(0.0));

    private double _horizontalOffset;

    /// <summary>Horizontal nudge in pixels (or X for Absolute).</summary>
    public double HorizontalOffset
    {
        get => _horizontalOffset;
        set
        {
            _horizontalOffset = value;
            SetValue(HorizontalOffsetProperty, value);
        }
    }

    /// <summary>
    /// Vertical-nudge identity (matches
    /// System.Windows.Controls.Primitives.Popup.VerticalOffsetProperty;
    /// the CLR wrapper dual-lands the DP store).
    /// </summary>
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register("VerticalOffset", typeof(double), typeof(Popup),
            new FrameworkPropertyMetadata(0.0));

    private double _verticalOffset;

    /// <summary>Vertical nudge in pixels (or Y for Absolute).</summary>
    public double VerticalOffset
    {
        get => _verticalOffset;
        set
        {
            _verticalOffset = value;
            SetValue(VerticalOffsetProperty, value);
        }
    }

    /// <summary>
    /// Light-dismiss identity (matches
    /// System.Windows.Controls.Primitives.Popup.StaysOpenProperty; the CLR
    /// wrapper keeps the default and dual-lands the DP store).
    /// </summary>
    public static readonly DependencyProperty StaysOpenProperty =
        DependencyProperty.Register("StaysOpen", typeof(bool), typeof(Popup),
            new FrameworkPropertyMetadata(true));

    private bool _staysOpen = true;

    /// <summary>Whether outside presses leave the popup open (default true, like WPF).</summary>
    public bool StaysOpen
    {
        get => _staysOpen;
        set
        {
            _staysOpen = value;
            SetValue(StaysOpenProperty, value);
        }
    }

    /// <summary>Transparency hint (parsed and retained; no layered-window compositing).</summary>
    public bool AllowsTransparency { get; set; }

    /// <summary>Drop-shadow hint (parsed and retained, default false like WPF; no compositor shadow).</summary>
    public bool HasDropShadow { get; set; }

    /// <summary>
    /// Open animation (matches System.Windows.Controls.Primitives.Popup.PopupAnimation):
    /// parsed and retained from XAML/templates; the engine paints popups
    /// inline with no animation compositing, like AllowsTransparency.
    /// The PC Space Map ComboBox template sets PopupAnimation="Slide".
    /// </summary>
    public PopupAnimation PopupAnimation { get; set; } = PopupAnimation.None;

    /// <summary>
    /// Custom-placement identity (matches
    /// System.Windows.Controls.Primitives.Popup.CustomPopupPlacementCallbackProperty;
    /// the CLR wrapper keeps the value and dual-lands the DP store so
    /// GetValue readers see CLR-applied callbacks too).
    /// </summary>
    public static readonly DependencyProperty CustomPopupPlacementCallbackProperty =
        DependencyProperty.Register("CustomPopupPlacementCallback", typeof(CustomPopupPlacementCallback), typeof(Popup),
            new FrameworkPropertyMetadata((CustomPopupPlacementCallback?)null));

    private CustomPopupPlacementCallback? _customPopupPlacementCallback;

    /// <summary>
    /// Custom-placement callback (default null, like WPF; retained but never
    /// invoked: engine popups place inline, so Placement=Custom keeps the
    /// standard anchor, like AllowsTransparency).
    /// </summary>
    public CustomPopupPlacementCallback? CustomPopupPlacementCallback
    {
        get => _customPopupPlacementCallback;
        set
        {
            if (ReferenceEquals(_customPopupPlacementCallback, value)) return;
            _customPopupPlacementCallback = value;
            SetValue(CustomPopupPlacementCallbackProperty, value);
        }
    }

    public override bool Focusable { get; set; } = true;

    /// <summary>Raised after IsOpen becomes true (matches Popup.Opened).</summary>
    public event EventHandler? Opened;

    /// <summary>Raised after IsOpen becomes false (matches Popup.Closed).</summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Open state: opening focuses the child when the popup takes focus,
    /// closing returns focus to the placement target when focus was inside.
    /// </summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value) return;
            _isOpen = value;
            if (value)
            {
                if (!StaysOpen) RegisterLightDismiss();
                Opened?.Invoke(this, EventArgs.Empty);
                if (Focusable) Child?.Focus();
            }
            else
            {
                UnregisterLightDismiss();
                ReturnFocus();
                Closed?.Invoke(this, EventArgs.Empty);
            }
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private void ReturnFocus()
    {
        var focused = WpfInputManager.Current?.Focused;
        if (focused is null || !IsInSubtree(focused)) return;
        if (PlacementTarget is FrameworkElement target && target.Focusable && target.IsEffectivelyEnabled)
            target.Focus();
    }

    private bool IsInSubtree(UIElement element)
    {
        for (var current = element.Parent; current is not null; current = current.Parent)
            if (ReferenceEquals(current, this)) return true;
        return ReferenceEquals(element, Child);
    }

    private void RegisterLightDismiss()
    {
        lock (LightDismissGate) LightDismiss.Add(this);
    }

    private void UnregisterLightDismiss()
    {
        lock (LightDismissGate) LightDismiss.Remove(this);
    }

    /// <summary>
    /// Input-manager hook: an outside left press closes every open
    /// StaysOpen=false popup whose child rect misses the point.
    /// </summary>
    internal static void LightDismissAt(WpfPoint point)
    {
        Popup[] open;
        lock (LightDismissGate) open = new Popup[LightDismiss.Count];
        lock (LightDismissGate) LightDismiss.CopyTo(open);
        foreach (var popup in open)
        {
            if (!popup.IsOpen || popup.StaysOpen) continue;
            if (popup.Child is not null && popup.ChildRectContains(point)) continue;
            popup.IsOpen = false;
        }
    }

    private bool ChildRectContains(WpfPoint point) =>
        Child is not null &&
        point.X >= Child.RenderRect.X && point.X < Child.RenderRect.X + Child.RenderRect.Width &&
        point.Y >= Child.RenderRect.Y && point.Y < Child.RenderRect.Y + Child.RenderRect.Height;

    private WpfRect AnchorRect()
    {
        if (PlacementRectangle.Width > 0 && PlacementRectangle.Height > 0)
            return new WpfRect(PlacementRectangle.X, PlacementRectangle.Y,
                PlacementRectangle.Width, PlacementRectangle.Height);
        if (PlacementTarget is not null)
            return PlacementTarget.RenderRect;
        if (Parent is FrameworkElement parent)
            return parent.RenderRect;
        return RenderRect;
    }

    /// <summary>Child rect in root coordinates for the current placement.</summary>
    internal WpfRect PlacedRect()
    {
        var childW = Math.Max(0, Child?.DesiredSize.Width ?? 0);
        var childH = Math.Max(0, Child?.DesiredSize.Height ?? 0);
        var anchor = AnchorRect();
        double x, y;
        switch (Placement)
        {
            case PlacementMode.Top:
                x = anchor.X + HorizontalOffset;
                y = anchor.Y - childH + VerticalOffset;
                break;
            case PlacementMode.Left:
                x = anchor.X - childW + HorizontalOffset;
                y = anchor.Y + VerticalOffset;
                break;
            case PlacementMode.Right:
                x = anchor.X + anchor.Width + HorizontalOffset;
                y = anchor.Y + VerticalOffset;
                break;
            case PlacementMode.Center:
                x = anchor.X + (anchor.Width - childW) / 2 + HorizontalOffset;
                y = anchor.Y + (anchor.Height - childH) / 2 + VerticalOffset;
                break;
            case PlacementMode.Mouse:
            case PlacementMode.MousePoint:
            {
                var mouse = WpfInputManager.Current?.LastPointerPosition ?? new WpfPoint(0, 0);
                x = mouse.X + HorizontalOffset;
                y = mouse.Y + VerticalOffset;
                break;
            }
            case PlacementMode.Absolute:
            case PlacementMode.AbsolutePoint:
                x = HorizontalOffset;
                y = VerticalOffset;
                break;
            case PlacementMode.Relative:
            case PlacementMode.RelativePoint:
                x = anchor.X + HorizontalOffset;
                y = anchor.Y + VerticalOffset;
                break;
            default: // Bottom (and Custom, which needs CustomPopupPlacementCallback)
                x = anchor.X + HorizontalOffset;
                y = anchor.Y + anchor.Height + VerticalOffset;
                break;
        }
        return new WpfRect(x, y, childW, childH);
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        // Overlay floats: the popup itself never takes layout space, but the
        // child still measures so placement has a size to anchor.
        Child?.Measure(available);
        return new WpfSize(0, 0);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (Child is not null) Child.Arrange(PlacedRect());
    }

    /// <summary>Hit-test covers the slot plus the open child rect.</summary>
    public override bool HitTest(WpfPoint point)
    {
        if (Visibility != Visibility.Visible) return false;
        return base.HitTest(point) || (IsOpen && ChildRectContains(point));
    }

    /// <summary>
    /// Overlay hit: the open floating child, so presses over it route into
    /// the popup even when later siblings overlap the child area.
    /// </summary>
    public override UIElement? HitTestOverlay(WpfPoint point)
    {
        if (!IsOpen || Child is null || Visibility != Visibility.Visible) return null;
        if (base.HitTest(point)) return null; // normal walk covers slot hits
        return ChildRectContains(point) ? Child : null;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (IsOpen && Child is not null) RenderVisible(dc, Child);
    }

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        if (e.Name == WasmRoutedEvent.KeyDownName && IsOpen &&
            e.Source is Core.WasmInputEvent.Key k && k.Args.IsDown && k.Args.Key == "Escape")
        {
            IsOpen = false;
            e.Handled = true;
        }
    }
}

/// <summary>
/// Status strip (matches System.Windows.Controls.Primitives.StatusBar, an
/// ItemsControl): compiled theme dictionaries key their separator style with
/// <see cref="SeparatorStyleKey"/> (HandyControl theme.baml entry #688).
/// Live-verified on net10: the WPF member is a SystemThemeKey singleton with
/// InternalKey StatusBarSeparatorStyle; the engine carries a
/// ComponentResourceKey singleton like the ToolBar style keys, so a
/// BAML-loaded entry and a later {x:Static StatusBar.SeparatorStyleKey}
/// lookup share identity through the static property. Item layout beyond the
/// inherited items panel stays a follow-up.
/// </summary>
public class StatusBar : ItemsControl
{
    /// <summary>Separator style key (matches StatusBar.SeparatorStyleKey).</summary>
    public static ResourceKey SeparatorStyleKey { get; } =
        new ComponentResourceKey(typeof(StatusBar), "SeparatorStyleKey");
}

/// <summary>Status-bar cell (matches System.Windows.Controls.Primitives.StatusBarItem:
/// a ContentControl; native adds no members beyond the item container contract).</summary>
public class StatusBarItem : ContentControl
{
}
