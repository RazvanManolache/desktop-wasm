namespace DesktopWasm.Wpf;

// Keyboard input devices and key events (WindowsBase PresentationCore
// subset, shim, not port): InputEventArgs, KeyboardEventArgs, KeyEventArgs,
// KeyEventHandler, KeyboardDevice, Keyboard, IInputElement,
// PresentationSource and TextComposition. Converted KeyDown/PreviewKeyDown
// handlers bind here; WpfInputManager raises them from DOM keyboard input
// (see OnRouted). Mouse/stylus members arrive with the Step 39 mouse slice.

/// <summary>
/// Input event root (matches System.Windows.Input.InputEventArgs): every
/// event carries the device that raised it (null when the engine raises the
/// event itself with no device behind it, like the real-args shapes the
/// converted AvalonEdit hover/completion handlers read through get_Device).
/// </summary>
public class InputEventArgs : RoutedEventArgs
{
    internal InputEventArgs(int timestamp) : this(null, timestamp) { }

    internal InputEventArgs(InputDevice? device, int timestamp)
    {
        Device = device;
        Timestamp = timestamp;
    }

    /// <summary>Raising device (matches Device; null on engine-built args).</summary>
    public InputDevice? Device { get; }

    /// <summary>Milliseconds since boot (matches Timestamp).</summary>
    public int Timestamp { get; protected set; }
}

/// <summary>Keyboard event root (matches System.Windows.Input.KeyboardEventArgs).</summary>
public class KeyboardEventArgs : InputEventArgs
{
    internal KeyboardEventArgs(KeyboardDevice? keyboardDevice, int timestamp)
        : base(keyboardDevice, timestamp) => KeyboardDevice = keyboardDevice;

    public KeyboardDevice? KeyboardDevice { get; protected set; }
}

/// <summary>Key handler (matches System.Windows.Input.KeyEventHandler).</summary>
public delegate void KeyEventHandler(object sender, KeyEventArgs e);

/// <summary>Key press data (matches System.Windows.Input.KeyEventArgs).</summary>
public class KeyEventArgs : KeyboardEventArgs
{
    internal KeyEventArgs(
        KeyboardDevice? keyboardDevice,
        int timestamp,
        Key key,
        Key systemKey,
        bool isDown,
        bool isRepeat)
        : base(keyboardDevice, timestamp)
    {
        Key = key;
        SystemKey = systemKey;
        IsDown = isDown;
        IsUp = !isDown;
        IsRepeat = isRepeat;
        IsToggled = false;
        ImeProcessedKey = Key.None;
    }

    public Key Key { get; }
    public Key SystemKey { get; }
    public Key ImeProcessedKey { get; protected set; }
    public bool IsDown { get; }
    public bool IsUp { get; }
    public bool IsRepeat { get; }
    public bool IsToggled { get; protected set; }

    /// <summary>App source (always null: the engine has no composition target).</summary>
    public PresentationSource? InputSource => null;

    /// <summary>Maps a DOM key name to a Key (tolerant: unknown names become None).</summary>
    public static Key KeyFromDomName(string? domKey) => WpfInputManager.CanonicalizeKey(domKey);
}

/// <summary>
/// Keyboard device (matches System.Windows.Input.KeyboardDevice, sealed over
/// the abstract InputDevice like the live type: Target carries the focused
/// element, ActiveSource stays null because the engine composes no Win32
/// target).
/// </summary>
public sealed class KeyboardDevice : InputDevice
{
    internal KeyboardDevice() { }

    private IInputElement? _target;

    /// <summary>Element holding keyboard focus (null when none does).</summary>
    public override IInputElement? Target => _target;

    /// <summary>Focus-manager write path (the override stays get-only like live).</summary>
    internal void SetTarget(IInputElement? target) => _target = target;

    /// <summary>Always null: the engine composes no Win32/target source.</summary>
    public override PresentationSource? ActiveSource => null;

    public UIElement? FocusedElement => Target as UIElement;

    public ModifierKeys Modifiers { get; internal set; }

    public bool IsKeyDown(Key key) =>
        WpfInputManager.Current?.IsKeyHeld(key) ?? false;
}

/// <summary>Keyboard access (matches System.Windows.Input.Keyboard).</summary>
public static class Keyboard
{
    private static readonly KeyboardDevice PrimaryDeviceValue = new();

    public static KeyboardDevice PrimaryDevice => PrimaryDeviceValue;

    public static UIElement? FocusedElement => WpfInputManager.Current?.Focused;

    public static ModifierKeys Modifiers => WpfInputManager.Current?.HeldModifiers ?? ModifierKeys.None;

    public static bool IsKeyDown(Key key) =>
        WpfInputManager.Current?.IsKeyHeld(key) ?? false;

    /// <summary>Moves focus (matches Keyboard.Focus).</summary>
    public static UIElement? Focus(UIElement? element)
    {
        if (element is null) return null;
        var manager = WpfInputManager.Current;
        if (manager is null) return null;
        return manager.TryFocus(element) ? manager.Focused : null;
    }

    /// <summary>
    /// Moves focus to an input element (matches Keyboard.Focus(IInputElement);
    /// converted AvalonDock focus management binds it). UIElements route
    /// through the focus manager; other hosts have no focusable surface in
    /// the browser and report null, like an unfocusable WPF element.
    /// </summary>
    public static IInputElement? Focus(IInputElement? element)
    {
        if (element is UIElement ui) return Focus(ui);
        return null;
    }
}

/// <summary>Focus/input contract (matches System.Windows.IInputElement, keyboard subset).</summary>
public interface IInputElement
{
    bool Focus();
    bool Focusable { get; }
    bool IsEnabled { get; }
    bool IsKeyboardFocused { get; }
    bool IsKeyboardFocusWithin { get; }
    bool IsMouseOver { get; }
    event KeyboardFocusChangedEventHandler GotKeyboardFocus;
    event KeyboardFocusChangedEventHandler LostKeyboardFocus;
    event RoutedEventHandler GotFocus;
    event RoutedEventHandler LostFocus;
}

/// <summary>Focus change handler (matches KeyboardFocusChangedEventHandler).</summary>
public delegate void KeyboardFocusChangedEventHandler(object sender, KeyboardFocusChangedEventArgs e);

/// <summary>Composition root (matches System.Windows.PresentationSource).</summary>
public abstract class PresentationSource : DispatcherObject
{
    public abstract Visual? RootVisual { get; set; }

    /// <summary>Session composition target (null when the source composes none).</summary>
    public virtual CompositionTarget? CompositionTarget => null;

    /// <summary>Always null: the engine composes no Win32/visual target.</summary>
    public static PresentationSource? FromVisual(UIElement? visual) => null;

    /// <summary>
    /// Visual overload (matches PresentationSource.FromVisual(Visual)): null
    /// like the element overload, so converted DPI fallbacks keep working.
    /// </summary>
    public static PresentationSource? FromVisual(Visual? visual) => null;

    /// <summary>
    /// Source lookup (matches PresentationSource.FromDependencyObject, which
    /// converted AvalonDock floating windows bind on load): null, like the
    /// FromVisual overloads, so converted DPI fallbacks keep working.
    /// </summary>
    public static PresentationSource? FromDependencyObject(DependencyObject? dependencyObject) => null;
}

/// <summary>
/// Composition result (matches System.Windows.Input.TextComposition, non-sealed
/// over DispatcherObject like the live type): the text a key press composes.
/// Converted AvalonEdit text input news these up through the live
/// (InputManager, IInputElement, string) shape with the ambient manager; the
/// engine's own text shape keeps the (string, string) convenience.
/// </summary>
public class TextComposition : DispatcherObject
{
    private readonly InputManager? _manager;
    private readonly IInputElement? _source;

    public TextComposition(string text, string compositionText = "")
        : this(InputManager.Current, null, text)
    {
        CompositionText = compositionText;
    }

    /// <summary>
    /// Live composition shape (matches the public (InputManager,
    /// IInputElement, string) constructor): the manager and source ride along
    /// without validation (live tolerates nulls there) while a null result
    /// text throws ArgumentException like live.
    /// </summary>
    public TextComposition(InputManager? manager, IInputElement? source, string text)
    {
        _manager = manager;
        _source = source;
        Text = text ?? throw new ArgumentException("Result text cannot be null.", nameof(text));
        CompositionText = string.Empty;
    }

    public string Text { get; }
    public string CompositionText { get; private set; }
}

/// <summary>
/// Touch delta (matches System.Windows.Input.ManipulationDelta, verified
/// against live .NET: a reference type with a public
/// (Vector, double, Vector, Vector) ctor and read-only
/// Translation/Rotation/Scale/Expansion). Converted OxyPlot touch code reads
/// Translation/Scale off the delta events below; the engine raises no touch
/// input, so values only ever flow from explicit construction.
/// </summary>
public class ManipulationDelta
{
    public ManipulationDelta(Vector translation, double rotation, Vector scale, Vector expansion)
    {
        Translation = translation;
        Rotation = rotation;
        Scale = scale;
        Expansion = expansion;
    }

    public Vector Translation { get; }
    public double Rotation { get; }
    public Vector Scale { get; }
    public Vector Expansion { get; }
}

/// <summary>
/// Touch start data (matches System.Windows.Input.ManipulationStartedEventArgs:
/// sealed, InputEventArgs root, ManipulationContainer/ManipulationOrigin).
/// The engine raises no touch input; converted overrides (OxyPlot
/// PlotViewBase) receive these only through explicit construction.
/// </summary>
public sealed class ManipulationStartedEventArgs : InputEventArgs
{
    public ManipulationStartedEventArgs(IInputElement manipulationContainer, Point manipulationOrigin)
        : base(Environment.TickCount)
    {
        ManipulationContainer = manipulationContainer;
        ManipulationOrigin = manipulationOrigin;
    }

    public IInputElement ManipulationContainer { get; }
    public Point ManipulationOrigin { get; }
}

/// <summary>
/// Touch move data (matches System.Windows.Input.ManipulationDeltaEventArgs:
/// sealed, InputEventArgs root, ManipulationContainer/ManipulationOrigin plus
/// DeltaManipulation/CumulativeManipulation). Engine-raised touch does not
/// exist; converted overrides receive these through explicit construction.
/// </summary>
public sealed class ManipulationDeltaEventArgs : InputEventArgs
{
    public ManipulationDeltaEventArgs(
        IInputElement manipulationContainer,
        Point manipulationOrigin,
        ManipulationDelta deltaManipulation,
        ManipulationDelta cumulativeManipulation)
        : base(Environment.TickCount)
    {
        ManipulationContainer = manipulationContainer;
        ManipulationOrigin = manipulationOrigin;
        DeltaManipulation = deltaManipulation;
        CumulativeManipulation = cumulativeManipulation;
    }

    public IInputElement ManipulationContainer { get; }
    public Point ManipulationOrigin { get; }
    public ManipulationDelta DeltaManipulation { get; }
    public ManipulationDelta CumulativeManipulation { get; }
}

/// <summary>
/// Touch end data (matches
/// System.Windows.Input.ManipulationCompletedEventArgs: sealed,
/// InputEventArgs root, ManipulationContainer/ManipulationOrigin plus
/// TotalManipulation). Engine-raised touch does not exist; converted
/// overrides receive these through explicit construction.
/// </summary>
public sealed class ManipulationCompletedEventArgs : InputEventArgs
{
    public ManipulationCompletedEventArgs(
        IInputElement manipulationContainer,
        Point manipulationOrigin,
        ManipulationDelta totalManipulation)
        : base(Environment.TickCount)
    {
        ManipulationContainer = manipulationContainer;
        ManipulationOrigin = manipulationOrigin;
        TotalManipulation = totalManipulation;
    }

    public IInputElement ManipulationContainer { get; }
    public Point ManipulationOrigin { get; }
    public ManipulationDelta TotalManipulation { get; }
}

/// <summary>Mouse handler (matches System.Windows.Input.MouseEventHandler).</summary>
public delegate void MouseEventHandler(object sender, MouseEventArgs e);

/// <summary>Mouse button handler (matches MouseButtonEventHandler).</summary>
public delegate void MouseButtonEventHandler(object sender, MouseButtonEventArgs e);

/// <summary>Mouse wheel handler (matches MouseWheelEventHandler).</summary>
public delegate void MouseWheelEventHandler(object sender, MouseWheelEventArgs e);

/// <summary>
/// Pointer event root (matches System.Windows.Input.MouseEventArgs): the
/// converted AvalonEdit TextView.RaiseHoverEventPair reads get_StylusDevice
/// and constructs args through the public (MouseDevice, int, StylusDevice)
/// shape, so both live public constructors are carried (the engine raiser
/// keeps the internal button-state path with a null stylus).
/// </summary>
public class MouseEventArgs : InputEventArgs
{
    /// <summary>Live (MouseDevice, int) shape (matches the converted hover path).</summary>
    public MouseEventArgs(MouseDevice? mouseDevice, int timestamp)
        : this(mouseDevice, timestamp,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released)
    {
    }

    /// <summary>
    /// Live (MouseDevice, int, StylusDevice) shape (matches the converted
    /// TextView.RaiseHoverEventPair construction; null stylus means mouse).
    /// </summary>
    public MouseEventArgs(MouseDevice? mouseDevice, int timestamp, StylusDevice? stylusDevice)
        : this(mouseDevice, timestamp,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, null, stylusDevice)
    {
    }

    internal MouseEventArgs(
        MouseDevice? mouseDevice,
        int timestamp,
        MouseButtonState left,
        MouseButtonState middle,
        MouseButtonState right,
        MouseButtonState xButton1,
        MouseButtonState xButton2,
        ModifierKeys? activeModifiers = null,
        StylusDevice? stylusDevice = null)
        : base(mouseDevice, timestamp)
    {
        MouseDevice = mouseDevice;
        LeftButton = left;
        MiddleButton = middle;
        RightButton = right;
        XButton1 = xButton1;
        XButton2 = xButton2;
        ActiveModifiers = activeModifiers;
        StylusDevice = stylusDevice;
    }

    /// <summary>
    /// Modifier state the dispatching manager held when the event was built
    /// (engine channel for gesture matching; null on explicitly constructed
    /// args, which read the ambient device instead).
    /// </summary>
    internal ModifierKeys? ActiveModifiers { get; }

    public MouseDevice? MouseDevice { get; protected set; }

    /// <summary>
    /// Stylus raising this event (matches StylusDevice; null for mouse input
    /// and for every engine-raised event: the engine drives no pen hardware).
    /// </summary>
    public StylusDevice? StylusDevice { get; }
    public MouseButtonState LeftButton { get; protected set; }
    public MouseButtonState MiddleButton { get; protected set; }
    public MouseButtonState RightButton { get; protected set; }
    public MouseButtonState XButton1 { get; protected set; }
    public MouseButtonState XButton2 { get; protected set; }

    /// <summary>Position relative to an element (screen position for null).</summary>
    public Point GetPosition(IInputElement? relativeTo) =>
        WpfInputManager.Current?.PositionRelativeTo(relativeTo as UIElement) ?? new Point(0, 0);
}

/// <summary>Button press data (matches System.Windows.Input.MouseButtonEventArgs).</summary>
public class MouseButtonEventArgs : MouseEventArgs
{
    /// <summary>
    /// Live (MouseDevice, int, MouseButton) shape (matches the converted
    /// ListBoxAssist re-raise construction and the native public overload).
    /// Down-shaped like native construction: the changed button reads
    /// Pressed with ClickCount 1, all other buttons Released.
    /// </summary>
    public MouseButtonEventArgs(MouseDevice? mouseDevice, int timestamp, MouseButton button)
        : this(mouseDevice, timestamp,
            button == MouseButton.Left ? MouseButtonState.Pressed : MouseButtonState.Released,
            button == MouseButton.Middle ? MouseButtonState.Pressed : MouseButtonState.Released,
            button == MouseButton.Right ? MouseButtonState.Pressed : MouseButtonState.Released,
            button == MouseButton.XButton1 ? MouseButtonState.Pressed : MouseButtonState.Released,
            button == MouseButton.XButton2 ? MouseButtonState.Pressed : MouseButtonState.Released,
            button, MouseButtonState.Pressed, 1)
    {
    }

    internal MouseButtonEventArgs(
        MouseDevice? mouseDevice,
        int timestamp,
        MouseButtonState left,
        MouseButtonState middle,
        MouseButtonState right,
        MouseButtonState xButton1,
        MouseButtonState xButton2,
        MouseButton changedButton,
        MouseButtonState buttonState,
        int clickCount,
        ModifierKeys? activeModifiers = null)
        : base(mouseDevice, timestamp, left, middle, right, xButton1, xButton2, activeModifiers)
    {
        ChangedButton = changedButton;
        ButtonState = buttonState;
        ClickCount = clickCount;
    }

    public MouseButton ChangedButton { get; }
    public MouseButtonState ButtonState { get; }
    public int ClickCount { get; }
}

/// <summary>Wheel data (matches System.Windows.Input.MouseWheelEventArgs).</summary>
public class MouseWheelEventArgs : MouseEventArgs
{
    internal MouseWheelEventArgs(
        MouseDevice? mouseDevice,
        int timestamp,
        MouseButtonState left,
        MouseButtonState middle,
        MouseButtonState right,
        MouseButtonState xButton1,
        MouseButtonState xButton2,
        int delta,
        ModifierKeys? activeModifiers = null)
        : base(mouseDevice, timestamp, left, middle, right, xButton1, xButton2, activeModifiers) => Delta = delta;

    public int Delta { get; }
}

/// <summary>
/// Mouse device (matches System.Windows.Input.MouseDevice, sealed over the
/// abstract InputDevice like the live type: Target tracks the element under
/// the pointer, ActiveSource stays null because the engine composes no
/// Win32 target).
/// </summary>
public sealed class MouseDevice : InputDevice
{
    internal MouseDevice() { }

    /// <summary>Element under the pointer (matches Target).</summary>
    public override IInputElement? Target => DirectlyOver;

    /// <summary>Always null: the engine composes no Win32/target source.</summary>
    public override PresentationSource? ActiveSource => null;

    /// <summary>Element holding explicit mouse capture (null when none).</summary>
    public UIElement? Captured { get; internal set; }

    public UIElement? DirectlyOver { get; internal set; }

    public Cursor? OverrideCursor { get; set; }

    /// <summary>Position relative to an element (screen position for null).</summary>
    public Point GetPosition(IInputElement? relativeTo) =>
        WpfInputManager.Current?.PositionRelativeTo(relativeTo as UIElement) ?? new Point(0, 0);
}

/// <summary>Mouse access (matches System.Windows.Input.Mouse).</summary>
public static class Mouse
{
    private static readonly MouseDevice PrimaryDeviceValue = new();

    public static MouseDevice PrimaryDevice => PrimaryDeviceValue;

    public static UIElement? Captured => PrimaryDeviceValue.Captured;

    public static UIElement? DirectlyOver => WpfInputManager.Current?.DirectlyOverElement;

    public static MouseButtonState LeftButton => ButtonState(MouseButton.Left);
    public static MouseButtonState MiddleButton => ButtonState(MouseButton.Middle);
    public static MouseButtonState RightButton => ButtonState(MouseButton.Right);
    public static MouseButtonState XButton1 => ButtonState(MouseButton.XButton1);
    public static MouseButtonState XButton2 => ButtonState(MouseButton.XButton2);

    public static Cursor? OverrideCursor
    {
        get => PrimaryDeviceValue.OverrideCursor;
        set => PrimaryDeviceValue.OverrideCursor = value;
    }

    /// <summary>Captures all pointer input (matches Mouse.Capture).</summary>
    public static bool Capture(IInputElement? element)
    {
        var manager = WpfInputManager.Current;
        if (manager is null) return false;
        return manager.SetExplicitCapture(element as UIElement);
    }

    public static Point GetPosition(IInputElement? relativeTo) =>
        WpfInputManager.Current?.PositionRelativeTo(relativeTo as UIElement) ?? new Point(0, 0);

    /// <summary>Tunneling button-release token (matches Mouse.PreviewMouseUpEvent;
    /// vendor ripples register class handlers on it).</summary>
    public static readonly RoutedEvent PreviewMouseUpEvent = EventManager.RegisterRoutedEvent(
        "PreviewMouseUp", RoutingStrategy.Tunnel, typeof(MouseButtonEventHandler), typeof(Mouse));

    /// <summary>Bubbling pointer-move token (matches Mouse.MouseMoveEvent;
    /// vendor ripples track the pointer through class handlers on it).</summary>
    public static readonly RoutedEvent MouseMoveEvent = EventManager.RegisterRoutedEvent(
        "MouseMove", RoutingStrategy.Bubble, typeof(MouseEventHandler), typeof(Mouse));

    /// <summary>Tunneling pointer-move token (matches Mouse.PreviewMouseMoveEvent).</summary>
    public static readonly RoutedEvent PreviewMouseMoveEvent = EventManager.RegisterRoutedEvent(
        "PreviewMouseMove", RoutingStrategy.Tunnel, typeof(MouseEventHandler), typeof(Mouse));

    /// <summary>Tunneling button-press token (matches Mouse.PreviewMouseDownEvent).</summary>
    public static readonly RoutedEvent PreviewMouseDownEvent = EventManager.RegisterRoutedEvent(
        "PreviewMouseDown", RoutingStrategy.Tunnel, typeof(MouseButtonEventHandler), typeof(Mouse));

    /// <summary>Bubbling button-press token (matches Mouse.MouseDownEvent).</summary>
    public static readonly RoutedEvent MouseDownEvent = EventManager.RegisterRoutedEvent(
        "MouseDown", RoutingStrategy.Bubble, typeof(MouseButtonEventHandler), typeof(Mouse));

    /// <summary>Bubbling button-release token (matches Mouse.MouseUpEvent).</summary>
    public static readonly RoutedEvent MouseUpEvent = EventManager.RegisterRoutedEvent(
        "MouseUp", RoutingStrategy.Bubble, typeof(MouseButtonEventHandler), typeof(Mouse));

    /// <summary>Tunneling press-outside-capture token (matches Mouse.PreviewMouseDownOutsideCapturedElementEvent).</summary>
    public static readonly RoutedEvent PreviewMouseDownOutsideCapturedElementEvent = EventManager.RegisterRoutedEvent(
        "PreviewMouseDownOutsideCapturedElement", RoutingStrategy.Tunnel, typeof(MouseButtonEventHandler), typeof(Mouse));

    /// <summary>Tunneling release-outside-capture token (matches Mouse.PreviewMouseUpOutsideCapturedElementEvent).</summary>
    public static readonly RoutedEvent PreviewMouseUpOutsideCapturedElementEvent = EventManager.RegisterRoutedEvent(
        "PreviewMouseUpOutsideCapturedElement", RoutingStrategy.Tunnel, typeof(MouseButtonEventHandler), typeof(Mouse));

    /// <summary>Tunneling wheel token (matches Mouse.PreviewMouseWheelEvent).</summary>
    public static readonly RoutedEvent PreviewMouseWheelEvent = EventManager.RegisterRoutedEvent(
        "PreviewMouseWheel", RoutingStrategy.Tunnel, typeof(MouseWheelEventHandler), typeof(Mouse));

    /// <summary>Bubbling wheel token (matches Mouse.MouseWheelEvent).</summary>
    public static readonly RoutedEvent MouseWheelEvent = EventManager.RegisterRoutedEvent(
        "MouseWheel", RoutingStrategy.Bubble, typeof(MouseWheelEventHandler), typeof(Mouse));

    /// <summary>Direct pointer-enter token (matches Mouse.MouseEnterEvent).</summary>
    public static readonly RoutedEvent MouseEnterEvent = EventManager.RegisterRoutedEvent(
        "MouseEnter", RoutingStrategy.Direct, typeof(MouseEventHandler), typeof(Mouse));

    /// <summary>Direct pointer-leave token (matches Mouse.MouseLeaveEvent).</summary>
    public static readonly RoutedEvent MouseLeaveEvent = EventManager.RegisterRoutedEvent(
        "MouseLeave", RoutingStrategy.Direct, typeof(MouseEventHandler), typeof(Mouse));

    /// <summary>Bubbling capture-taken token (matches Mouse.GotMouseCaptureEvent).</summary>
    public static readonly RoutedEvent GotMouseCaptureEvent = EventManager.RegisterRoutedEvent(
        "GotMouseCapture", RoutingStrategy.Bubble, typeof(MouseEventHandler), typeof(Mouse));

    /// <summary>Bubbling capture-released token (matches Mouse.LostMouseCaptureEvent).</summary>
    public static readonly RoutedEvent LostMouseCaptureEvent = EventManager.RegisterRoutedEvent(
        "LostMouseCapture", RoutingStrategy.Bubble, typeof(MouseEventHandler), typeof(Mouse));

    /// <summary>Bubbling cursor-query token (matches Mouse.QueryCursorEvent).</summary>
    public static readonly RoutedEvent QueryCursorEvent = EventManager.RegisterRoutedEvent(
        "QueryCursor", RoutingStrategy.Bubble, typeof(QueryCursorEventHandler), typeof(Mouse));

    internal static MouseButtonState ButtonState(MouseButton button) =>
        WpfInputManager.Current?.GetButtonState(button) ?? MouseButtonState.Released;
}

/// <summary>System cursor (matches System.Windows.Input.Cursor).</summary>
public sealed class Cursor
{
    internal Cursor(string displayName) => DisplayName = displayName;

    internal string DisplayName { get; }

    public override string ToString() => DisplayName;
}

/// <summary>Shared cursors (matches System.Windows.Input.Cursors).</summary>
public static class Cursors
{
    public static Cursor AppStarting { get; } = new("AppStarting");
    public static Cursor Arrow { get; } = new("Arrow");
    public static Cursor Cross { get; } = new("Cross");
    public static Cursor Hand { get; } = new("Hand");
    public static Cursor Help { get; } = new("Help");
    public static Cursor IBeam { get; } = new("IBeam");
    public static Cursor No { get; } = new("No");
    public static Cursor None { get; } = new("None");
    public static Cursor Pen { get; } = new("Pen");
    public static Cursor ScrollAll { get; } = new("ScrollAll");
    public static Cursor ScrollWE { get; } = new("ScrollWE");
    public static Cursor ScrollNS { get; } = new("ScrollNS");
    public static Cursor SizeAll { get; } = new("SizeAll");
    public static Cursor SizeNESW { get; } = new("SizeNESW");
    public static Cursor SizeNS { get; } = new("SizeNS");
    public static Cursor SizeNWSE { get; } = new("SizeNWSE");
    public static Cursor SizeWE { get; } = new("SizeWE");
    public static Cursor UpArrow { get; } = new("UpArrow");
    public static Cursor Wait { get; } = new("Wait");
}
