namespace DesktopWasm.Wpf;

using System.Collections.ObjectModel;

/// <summary>Touch phase (matches System.Windows.Input.TouchAction: Down=0,
/// Move=1, Up=2, verified against the 9.0.20 PresentationCore ref pack).</summary>
public enum TouchAction
{
    Down = 0,
    Move = 1,
    Up = 2,
}

/// <summary>
/// Single contact sample (matches System.Windows.Input.TouchPoint: public
/// (TouchDevice, Point, Rect, TouchAction) constructor with Action, Bounds,
/// Position, Size and TouchDevice readers). Size projects Bounds.Size, like
/// the framework value carried alongside the hardware bounds.
/// </summary>
public class TouchPoint
{
    public TouchPoint(TouchDevice touchDevice, Point position, Rect bounds, TouchAction action)
    {
        TouchDevice = touchDevice ?? throw new ArgumentNullException(nameof(touchDevice));
        Position = position;
        Bounds = bounds;
        Action = action;
    }

    public TouchDevice TouchDevice { get; }

    public Point Position { get; }

    public Rect Bounds { get; }

    public Size Size => Bounds.Size;

    public TouchAction Action { get; }
}

/// <summary>
/// Contact sample list (matches System.Windows.Input.TouchPointCollection:
/// a Collection-of-TouchPoint with a public parameterless constructor).
/// </summary>
public class TouchPointCollection : Collection<TouchPoint>
{
    public TouchPointCollection()
    {
    }
}

/// <summary>
/// Touch contact (matches System.Windows.Input.TouchDevice, verified
/// member-by-member against the 9.0.20 PresentationCore ref pack: abstract
/// with a protected (int) constructor, Id, Captured, CaptureMode,
/// DirectlyOver, Target, ActiveSource, Activated/Deactivated/Updated events,
/// Capture overloads, abstract GetTouchPoint/GetIntermediateTouchPoints,
/// protected OnCapture/OnManipulationStarted/OnManipulationEnded plus
/// Activate/Deactivate, and explicit IManipulator binding).
/// Two documented engine seams: the base is <see cref="DispatcherObject"/>
/// (like live TouchDevice, which is not an InputDevice; the engine roots
/// its mouse/keyboard/stylus devices under InputDevice since slice-92),
/// and the report path (ReportDown/Move/Up,
/// SetActiveSource) is internal so the same-assembly input stack can drive
/// it — converted code only ever binds the public surface above, which
/// keeps exact WPF shapes. The browser has no hardware touch feed; contacts
/// are synthesized from the real pointer path by
/// <see cref="WpfInputManager"/> (one contact per pointer id), so converted
/// touch handlers (HandyControlDemo TouchDragMoveWindow down/up counting
/// with CaptureTouch) run against live positions.
/// </summary>
public abstract class TouchDevice : DispatcherObject, IManipulator
{
    private Point _rootPosition;
    private readonly List<(Point Position, TouchAction Action)> _history = new();

    protected TouchDevice(int id) => Id = id;

    /// <summary>Contact id (the pointer id the manager synthesized this contact from).</summary>
    public int Id { get; }

    /// <summary>Whether the contact is down (set by the manager report path).</summary>
    public bool IsActive { get; private set; }

    /// <summary>Element holding this contact (null when none).</summary>
    public IInputElement? Captured { get; internal set; }

    /// <summary>Capture scope for the held contact.</summary>
    public CaptureMode CaptureMode { get; internal set; }

    /// <summary>Element directly under this contact (null when none).</summary>
    public IInputElement? DirectlyOver { get; internal set; }

    /// <summary>Capture target, else the element under the contact (matches Target).</summary>
    public virtual IInputElement? Target => Captured ?? DirectlyOver;

    /// <summary>Composition source (always null: the engine composes no per-device target).</summary>
    public virtual PresentationSource? ActiveSource => null;

    /// <summary>Contact went down (matches Activated).</summary>
    public event EventHandler? Activated;

    /// <summary>Contact went up (matches Deactivated).</summary>
    public event EventHandler? Deactivated;

    /// <summary>Contact moved (matches Updated).</summary>
    public event EventHandler? Updated;

    /// <summary>
    /// Owning manager (the input stack that minted this contact; null on
    /// explicitly constructed devices). Capture and queries follow the
    /// owner, never the ambient <see cref="WpfInputManager.Current"/>, so
    /// key/pointer traffic on another manager cannot veto or steal them
    /// (the slice-74 ambient cross-talk family).
    /// </summary>
    internal WpfInputManager? OwnerManager { get; set; }

    /// <summary>Captures this contact to an element (matches Capture(IInputElement)).</summary>
    public bool Capture(IInputElement element) => Capture(element, CaptureMode.Element);

    /// <summary>
    /// Captures this contact to an element with a scope (matches
    /// Capture(IInputElement, CaptureMode)). Non-UIElement hosts have no
    /// capturable surface and report false, like an uncapturable target.
    /// </summary>
    public bool Capture(IInputElement element, CaptureMode mode)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element is UIElement ui
            && (OwnerManager ?? WpfInputManager.Current)?.TryCaptureTouch(ui, this, mode) == true;
    }

    /// <summary>Sample at this contact relative to an element (matches GetTouchPoint).</summary>
    public virtual TouchPoint GetTouchPoint(IInputElement relativeTo)
    {
        ArgumentNullException.ThrowIfNull(relativeTo);
        var position = RelativePosition(relativeTo);
        return new TouchPoint(this, position, new Rect(position, new Size(1, 1)), LastAction);
    }

    /// <summary>
    /// Samples since contact start, oldest first (matches
    /// GetIntermediateTouchPoints). Boundary: the browser forwards one
    /// sample per pointer dispatch with no hardware coalescing, so the
    /// session history is exactly the intermediate set.
    /// </summary>
    public virtual TouchPointCollection GetIntermediateTouchPoints(IInputElement relativeTo)
    {
        ArgumentNullException.ThrowIfNull(relativeTo);
        var points = new TouchPointCollection();
        foreach (var (position, action) in _history)
        {
            var relative = RelativePosition(relativeTo, position);
            points.Add(new TouchPoint(this, relative, new Rect(relative, new Size(1, 1)), action));
        }
        return points;
    }

    /// <summary>
    /// Reasserts device state (matches Synchronize). Boundary: the session
    /// state is always current (no desktop message pump to catch up with),
    /// so this is a no-op; contacts update on every pointer dispatch.
    /// </summary>
    public void Synchronize()
    {
    }

    /// <summary>Capture change notice (matches OnCapture).</summary>
    protected virtual void OnCapture(IInputElement? oldCapture, CaptureMode mode)
    {
    }

    /// <summary>Touch-setup notice (matches OnManipulationStarted).</summary>
    protected virtual void OnManipulationStarted()
    {
    }

    /// <summary>Touch-end notice (matches OnManipulationEnded).</summary>
    protected virtual void OnManipulationEnded(bool cancel)
    {
    }

    /// <summary>Marks the contact down and fans out Activated (matches Activate).</summary>
    protected void Activate()
    {
        if (IsActive) return;
        IsActive = true;
        Activated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Marks the contact up and fans out Deactivated (matches Deactivate).</summary>
    protected void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        Deactivated?.Invoke(this, EventArgs.Empty);
    }

    internal TouchAction LastAction { get; private set; } = TouchAction.Down;

    internal void MoveTo(Point rootPosition) => _rootPosition = rootPosition;

    internal void ReportDown(Point rootPosition)
    {
        _rootPosition = rootPosition;
        LastAction = TouchAction.Down;
        _history.Add((rootPosition, TouchAction.Down));
        Activate();
        OnManipulationStarted();
    }

    internal void ReportMove(Point rootPosition)
    {
        _rootPosition = rootPosition;
        LastAction = TouchAction.Move;
        _history.Add((rootPosition, TouchAction.Move));
        Updated?.Invoke(this, EventArgs.Empty);
    }

    internal void ReportUp(Point rootPosition)
    {
        _rootPosition = rootPosition;
        LastAction = TouchAction.Up;
        _history.Add((rootPosition, TouchAction.Up));
        OnManipulationEnded(false);
        Deactivate();
    }

    internal void SetCapture(UIElement? element, CaptureMode mode)
    {
        var old = Captured as UIElement;
        Captured = element;
        CaptureMode = mode;
        OnCapture(old, mode);
    }

    private Point RelativePosition(IInputElement relativeTo) =>
        RelativePosition(relativeTo, _rootPosition);

    private static Point RelativePosition(IInputElement relativeTo, Point root)
    {
        if (relativeTo is UIElement element)
            return new Point(root.X - element.RenderRect.X, root.Y - element.RenderRect.Y);
        return root;
    }

    int IManipulator.Id => Id;

    Point IManipulator.GetPosition(IInputElement relativeTo) => GetTouchPoint(relativeTo).Position;

    void IManipulator.ManipulationEnded(bool cancel)
    {
        // Host-driven contact end (no engine manipulation pipeline calls
        // this): mirrors an up report so the contact deactivates cleanly.
        if (IsActive) ReportUp(_rootPosition);
    }
}

/// <summary>Engine touch contact (the manager mints one per pointer id).</summary>
internal sealed class WpfTouchDevice : TouchDevice
{
    internal WpfTouchDevice(int id)
        : base(id)
    {
    }
}

/// <summary>
/// Touch event data (matches System.Windows.Input.TouchEventArgs, verified
/// against the 9.0.20 PresentationCore ref pack: non-sealed, public
/// (TouchDevice, int) constructor, TouchDevice reader, GetTouchPoint and
/// GetIntermediateTouchPoints). The timestamp rides the InputEventArgs root
/// like the mouse/keyboard args; the manager stamps Source/OriginalSource
/// to the hit element like the mouse path.
/// </summary>
public class TouchEventArgs : InputEventArgs
{
    public TouchEventArgs(TouchDevice touchDevice, int timestamp)
        : base(timestamp) =>
        TouchDevice = touchDevice ?? throw new ArgumentNullException(nameof(touchDevice));

    /// <summary>Contact this event belongs to (matches TouchDevice).</summary>
    public TouchDevice TouchDevice { get; }

    /// <summary>Sample at this contact relative to an element (matches GetTouchPoint).</summary>
    public TouchPoint GetTouchPoint(IInputElement relativeTo) =>
        TouchDevice.GetTouchPoint(relativeTo);

    /// <summary>Samples since contact start (matches GetIntermediateTouchPoints).</summary>
    public TouchPointCollection GetIntermediateTouchPoints(IInputElement relativeTo) =>
        TouchDevice.GetIntermediateTouchPoints(relativeTo);
}
