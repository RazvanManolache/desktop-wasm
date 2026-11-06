using DesktopWasm.Core;

namespace DesktopWasm.Wpf;

/// <summary>Point in layout (CSS pixel) space.</summary>
public readonly record struct WpfPoint(double X, double Y);

/// <summary>Routed event phases (tunneling Preview* down, bubbling * up).</summary>
public enum WasmRoutedPhase { Tunneling, Bubbling }

/// <summary>
/// Routed input event delivered along the root-to-target route.
/// Mirrors WPF's RoutedEventArgs: tunneling Preview* runs root-to-target,
/// then the unprefixed event bubbles target-to-root. Setting
/// <see cref="Handled"/> stops further delivery.
/// </summary>
public sealed class WasmRoutedEvent
{
    public const string PreviewPointerDownName = "PreviewPointerDown";
    public const string PointerDownName = "PointerDown";
    public const string PreviewPointerUpName = "PreviewPointerUp";
    public const string PointerUpName = "PointerUp";
    public const string PreviewPointerMoveName = "PreviewPointerMove";
    public const string PointerMoveName = "PointerMove";
    public const string PreviewPointerCancelName = "PreviewPointerCancel";
    public const string PointerCancelName = "PointerCancel";
    public const string PreviewWheelName = "PreviewWheel";
    public const string WheelName = "Wheel";
    public const string PreviewKeyDownName = "PreviewKeyDown";
    public const string KeyDownName = "KeyDown";
    public const string PreviewKeyUpName = "PreviewKeyUp";
    public const string KeyUpName = "KeyUp";

    public WasmRoutedEvent(string name, WasmInputEvent source, WasmRoutedPhase phase)
    {
        Name = name;
        Source = source;
        Phase = phase;
    }

    public string Name { get; }
    public WasmInputEvent Source { get; }
    public WasmRoutedPhase Phase { get; }
    public bool Handled { get; set; }

    /// <summary>True for a left-button press pointer event.</summary>
    public bool IsLeftButtonDown =>
        Source is WasmInputEvent.Pointer p &&
        p.Args.Kind == WasmPointerKind.Down &&
        p.Args.Button == WasmPointerButton.Left;
}

/// <summary>WPF InputManager over the shared <see cref="IWasmInputRouter"/>.</summary>
public sealed class WpfInputManager
{
    private readonly IWasmInputRouter _router;
    private readonly List<UIElement> _roots = new();

    /// <summary>Last constructed manager (Keyboard statics read through it).</summary>
    internal static WpfInputManager? Current { get; private set; }

    /// <summary>Clears held-key/modifier state on the current manager
    /// (test hermeticity seam: key traffic in one test must not leak
    /// ambient modifiers into the next).</summary>
    internal static void ResetForTests()
    {
        if (Current is { } current)
        {
            current._heldKeys.Clear();
            current._modifiers = ModifierKeys.None;
        }
    }

    public WpfInputManager(IWasmInputRouter router)
    {
        _router = router;
        _router.Routed += OnRouted;
        Current = this;
    }

    private readonly HashSet<string> _heldKeys = new(StringComparer.Ordinal);
    private ModifierKeys _modifiers;

    /// <summary>Modifiers from the most recent keyboard event.</summary>
    internal ModifierKeys HeldModifiers => _modifiers;

    internal bool IsKeyHeld(Key key) => _heldKeys.Contains(CanonicalKeyName(key));

    /// <summary>Canonical held-key name (letters uppercased; null when unmappable).</summary>
    internal static string CanonicalKeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        >= Key.A and <= Key.Z => key.ToString(),
        Key.Space => " ",
        Key.Enter or Key.Return => "Enter",
        Key.Tab => "Tab",
        Key.Escape => "Escape",
        Key.Back => "Backspace",
        Key.Left => "ArrowLeft",
        Key.Right => "ArrowRight",
        Key.Up => "ArrowUp",
        Key.Down => "ArrowDown",
        Key.LeftShift or Key.RightShift => "Shift",
        Key.LeftCtrl or Key.RightCtrl => "Control",
        Key.LeftAlt or Key.RightAlt => "Alt",
        Key.Capital => "CapsLock",
        _ => key.ToString(),
    };

    /// <summary>Maps a DOM key name to a Key (tolerant: unknown names become None).</summary>
    internal static Key CanonicalizeKey(string? domKey)
    {
        if (string.IsNullOrEmpty(domKey)) return Key.None;
        if (domKey.Length == 1)
        {
            var upper = char.ToUpperInvariant(domKey[0]);
            if (upper is >= 'A' and <= 'Z') return (Key)((int)Key.A + upper - 'A');
            if (domKey[0] is >= '0' and <= '9') return (Key)((int)Key.D0 + domKey[0] - '0');
            if (domKey == " ") return Key.Space;
            return Key.None;
        }
        if (domKey.StartsWith("F", StringComparison.Ordinal) && int.TryParse(domKey[1..], out var f) && f is >= 1 and <= 24)
            return (Key)((int)Key.F1 + f - 1);
        return domKey switch
        {
            "Enter" => Key.Enter,
            "Tab" => Key.Tab,
            "Escape" => Key.Escape,
            "Backspace" => Key.Back,
            "ArrowLeft" => Key.Left,
            "ArrowRight" => Key.Right,
            "ArrowUp" => Key.Up,
            "ArrowDown" => Key.Down,
            "Shift" => Key.LeftShift,
            "Control" => Key.LeftCtrl,
            "Alt" => Key.LeftAlt,
            "CapsLock" => Key.Capital,
            "Delete" => Key.Delete,
            "Insert" => Key.Insert,
            "Home" => Key.Home,
            "End" => Key.End,
            "PageUp" => Key.PageUp,
            "PageDown" => Key.PageDown,
            _ when Enum.TryParse<Key>(domKey, ignoreCase: true, out var named) => named,
            _ => Key.None,
        };
    }

    /// <summary>Compat hook: fired per route node alongside OnRoutedEvent.</summary>
    public event Action<UIElement, WasmInputEvent, WasmRoutedPhase>? Routed;

    /// <summary>Currently focused element (keyboard route target).</summary>
    public UIElement? Focused { get; private set; }

    public event Action<UIElement?>? FocusChanged;

    private WpfPoint? _lastPoint;
    private readonly HashSet<MouseButton> _heldButtons = new();
    private readonly HashSet<UIElement> _hoverRoute = new();
    private UIElement? _explicitCapture;
    private MouseButton? _lastDownButton;
    private DateTime _lastDownTime = DateTime.MinValue;
    private WpfPoint _lastDownPoint;
    private int _lastClickCount;

    /// <summary>Topmost element under the pointer (null when none).</summary>
    internal UIElement? DirectlyOverElement { get; private set; }

    /// <summary>Last pointer position in root coordinates (Mouse-placement popups anchor here).</summary>
    internal WpfPoint LastPointerPosition => _lastPoint ?? new WpfPoint(0, 0);

    internal bool IsOverElement(UIElement element) => _hoverRoute.Contains(element);

    internal MouseButtonState GetButtonState(MouseButton button) =>
        _heldButtons.Contains(button) ? MouseButtonState.Pressed : MouseButtonState.Released;

    internal Point PositionRelativeTo(UIElement? relativeTo)
    {
        var point = _lastPoint ?? new WpfPoint(0, 0);
        // RenderRect is root-absolute, so one subtraction lands in the
        // element's local space; walking ancestors would subtract
        // their absolute origins again (nested layouts like a pad in a
        // border in a panel exposed this).
        if (relativeTo is not null)
            point = new WpfPoint(point.X - relativeTo.RenderRect.X, point.Y - relativeTo.RenderRect.Y);
        return new Point(point.X, point.Y);
    }

    /// <summary>
    /// Explicit mouse capture (matches Mouse.Capture): the previous holder
    /// raises LostMouseCapture and the new holder GotMouseCapture, like the
    /// WPF capture path. Re-capturing the current holder is a silent no-op.
    /// </summary>
    internal bool SetExplicitCapture(UIElement? element)
    {
        if (element is null)
        {
            var released = _explicitCapture;
            _explicitCapture = null;
            Mouse.PrimaryDevice.Captured = null;
            released?.RaiseLostMouseCapture(BuildCaptureArgs());
            return true;
        }
        foreach (var root in _roots)
        {
            if (ReferenceEquals(root, element) || IsInSubtree(root, element))
            {
                var previous = _explicitCapture;
                if (ReferenceEquals(previous, element)) return true;
                _explicitCapture = element;
                Mouse.PrimaryDevice.Captured = element;
                previous?.RaiseLostMouseCapture(BuildCaptureArgs());
                element.RaiseGotMouseCapture(BuildCaptureArgs());
                return true;
            }
        }
        return false;
    }

    private MouseEventArgs BuildCaptureArgs() =>
        new(Mouse.PrimaryDevice, Environment.TickCount,
            GetButtonState(MouseButton.Left), GetButtonState(MouseButton.Middle),
            GetButtonState(MouseButton.Right), GetButtonState(MouseButton.XButton1),
            GetButtonState(MouseButton.XButton2));

    public void AddRoot(UIElement root) => _roots.Add(root);

    /// <summary>
    /// Modal dialog root: while set, pointer/wheel/keyboard routes consult
    /// only this window (real modality blocks the other windows; the dialog
    /// window itself covers the scene so dead-background presses cannot leak
    /// through). Null when no dialog is shown.
    /// </summary>
    public UIElement? ModalRoot { get; private set; }

    /// <summary>
    /// Shows a window as a modal dialog: registers it as a root (if needed),
    /// makes it visible, gates all input to it and focuses it.
    /// </summary>
    public void ShowDialog(Window dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (!_roots.Contains(dialog)) _roots.Add(dialog);
        dialog.IsVisible = true;
        dialog.DialogOwner = this;
        ModalRoot = dialog;
        TryFocus(dialog);
    }

    /// <summary>
    /// Closes a modal dialog: records the result, releases the input gate,
    /// hides the window (it stays rooted so re-show is cheap) and drops
    /// focus parked inside it. Harmless when the dialog is not modal.
    /// </summary>
    public void CloseDialog(Window dialog, bool? result = null)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        dialog.DialogResult = result ?? dialog.DialogResult;
        if (ReferenceEquals(ModalRoot, dialog)) ModalRoot = null;
        if (Focused is not null && IsInSubtree(dialog, Focused)) SetFocused(null);
        dialog.IsVisible = false;
        dialog.DialogOwner = null;
        dialog.RaiseClosed();
    }

    /// <summary>Whether <paramref name="element"/> is <paramref name="root"/> or descends from it.</summary>
    public static bool IsInSubtree(UIElement root, UIElement? element)
    {
        for (var p = element; p is not null; p = p.Parent)
            if (ReferenceEquals(p, root)) return true;
        return false;
    }

    /// <summary>False when an ancestor window is hidden (closed dialogs keep focus/routing out).</summary>
    public static bool IsShown(UIElement element)
    {
        for (var p = (UIElement?)element; p is not null; p = p.Parent)
            if (p is Window w && !w.IsVisible) return false;
        return true;
    }

    /// <summary>Moves keyboard focus (only to <see cref="UIElement.Focusable"/> elements).</summary>
    public bool TryFocus(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!element.Focusable || !element.IsEffectivelyEnabled || !IsShown(element)) return false;
        if (ModalRoot is not null && !IsInSubtree(ModalRoot, element)) return false;
        SetFocused(element);
        // The preview hook can veto the move (read-only displays swallow
        // focus): report whether focus actually landed.
        return ReferenceEquals(Focused, element);
    }

    /// <summary>Single focus gate: per-element hooks fire alongside the event.</summary>
    private void SetFocused(UIElement? next)
    {
        if (ReferenceEquals(Focused, next)) return;
        // Preview hooks run before the move on both sides; either Handled
        // keeps old focus (converted read-only displays swallow focus this
        // way). The default hooks raise PreviewLost/PreviewGotKeyboardFocus.
        var incoming = new KeyboardFocusChangedEventArgs(Focused, next);
        if (Focused is not null)
        {
            Focused.OnPreviewLostKeyboardFocus(incoming);
            if (incoming.Handled) return;
        }
        if (next is not null)
        {
            next.OnPreviewGotKeyboardFocus(incoming);
            if (incoming.Handled) return;
        }
        var old = Focused;
        Focused = next;
        var passed = new KeyboardFocusChangedEventArgs(old, next);
        if (old is not null)
        {
            old.OnLostFocus();
            old.RaiseLostFocus(passed);
            old.RaiseIsKeyboardFocusedChanged(true, false);
        }
        if (next is not null)
        {
            next.OnGotFocus();
            next.RaiseGotFocus(passed);
            next.RaiseIsKeyboardFocusedChanged(false, true);
        }
        Keyboard.PrimaryDevice.SetTarget(next);
        FocusChanged?.Invoke(next);
    }

    /// <summary>
    /// Whether the window paints a caption band over the point: only
    /// chrome-marked subtrees stay interactive there, everything else is
    /// window chrome (the route ends at the Window itself).
    /// </summary>
    private static bool CaptionGate(UIElement root, WpfPoint point) =>
        root is Window window && window.Chrome is { } chrome &&
        WindowChrome.IsCaptionPoint(window, chrome, point);

    /// <summary>Topmost element under the point (last root / last child wins).</summary>
    public UIElement? HitTest(double x, double y)
    {
        var point = new WpfPoint(x, y);
        for (var i = _roots.Count - 1; i >= 0; i--)
        {
            if (ModalRoot is not null && !ReferenceEquals(_roots[i], ModalRoot)) continue;
            if (_roots[i] is Window w && !w.IsVisible) continue;
            if (HitTestRecursive(_roots[i], point, CaptionGate(_roots[i], point), chromeActive: false) is { } hit) return hit;
        }
        return null;
    }

    /// <summary>Full root-to-target route for the point (empty when nothing is hit).</summary>
    public IReadOnlyList<UIElement> GetRoute(double x, double y)
    {
        var route = new List<UIElement>();
        var point = new WpfPoint(x, y);
        for (var i = _roots.Count - 1; i >= 0; i--)
        {
            if (ModalRoot is not null && !ReferenceEquals(_roots[i], ModalRoot)) continue;
            if (_roots[i] is Window w && !w.IsVisible) continue;
            if (!_roots[i].HitTest(point)) continue;
            if (!BuildRoute(_roots[i], point, route, CaptionGate(_roots[i], point), chromeActive: false)) route.Clear();
            break;
        }

        return route;
    }

    /// <summary>Implicit pointer capture: pointerId -> pressed element.</summary>
    private readonly Dictionary<int, UIElement> _captures = new();

    /// <summary>Live touch contact plus its route/over state (one per pointer id).</summary>
    private sealed class TouchSession
    {
        public TouchSession(WpfTouchDevice device) => Device = device;

        public WpfTouchDevice Device { get; }

        public readonly HashSet<UIElement> OverSet = new();

        public List<UIElement> OverOrder = new();
    }

    /// <summary>Active touch contacts by pointer id (synthesized from the pointer path).</summary>
    private readonly Dictionary<int, TouchSession> _touches = new();

    private void OnRouted(WasmInputEvent e)
    {
        // Converted CLR key events (PreviewKeyDown/KeyDown/...) fire around
        // the engine Wasm route: a handled preview suppresses everything,
        // and handled keys skip the default Tab/Enter/Space actions.
        KeyEventArgs? keyArgs = null;
        if (e is WasmInputEvent.Key k)
        {
            var repeat = TrackKeyboard(k.Args);
            var key = CanonicalizeKey(k.Args.Key);
            var system = _modifiers.HasFlag(ModifierKeys.Alt) && key != Key.None ? key : Key.None;
            keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, key, system, k.Args.IsDown, repeat);
        }

        // Pointer/wheel: hit-tested route. Keys: focused element's route, else broadcast roots.
        // A captured pointer routes to its pressed element (root-to-capture path)
        // instead of re-hit-testing, so drag-out and release-outside still reach it.
        var route = e switch
        {
            WasmInputEvent.Pointer p => new List<UIElement>(ResolvePointerRoute(p.Args)),
            WasmInputEvent.Wheel w => new List<UIElement>(GetRoute(w.Args.X, w.Args.Y)),
            WasmInputEvent.Key => FocusRoute(),
            _ => new List<UIElement>(),
        };
        if (keyArgs is not null)
        {
            foreach (var el in route) { el.RaiseKey(keyArgs, preview: true); if (keyArgs.Handled) return; }
        }

        MouseEventArgs? mouseArgs = null;
        if (e is WasmInputEvent.Pointer mp)
        {
            mouseArgs = TrackPointer(mp.Args);
            MarkOrigin(mouseArgs, route);
            UpdateHover(route, mouseArgs);
            foreach (var el in route) { el.RaiseMouse(mouseArgs, preview: true); if (mouseArgs.Handled) return; }
        }
        else if (e is WasmInputEvent.Wheel mw)
        {
            _lastPoint = new WpfPoint(mw.Args.X, mw.Args.Y);
            mouseArgs = BuildWheelArgs(mw.Args);
            MarkOrigin(mouseArgs, route);
            UpdateHover(route, mouseArgs);
            foreach (var el in route) { el.RaiseMouse(mouseArgs, preview: true); if (mouseArgs.Handled) return; }
        }

        if (e is WasmInputEvent.Pointer pd && pd.Args.Kind == WasmPointerKind.Down && pd.Args.Button == WasmPointerButton.Left)
        {
            // Light-dismiss popups close before focus moves, so the press
            // still reaches the element underneath like real WPF.
            Popup.LightDismissAt(new WpfPoint(pd.Args.X, pd.Args.Y));
            // Implicit focus on left press, mirroring desktop behavior.
            for (var i = route.Count - 1; i >= 0; i--)
            {
                if (!route[i].Focusable || !route[i].IsEffectivelyEnabled) continue;
                SetFocused(route[i]);
                break;
            }
            if (route.Count > 0) _captures[pd.Args.PointerId] = route[^1];
        }

        var (tunnel, bubble) = EventNames(e);
        var tunnelEvent = new WasmRoutedEvent(tunnel, e, WasmRoutedPhase.Tunneling);
        Deliver(route, tunnelEvent, reverse: false);
        // Shared handled state: a handled preview suppresses the paired bubble.
        if (tunnelEvent.Handled) return;
        Deliver(route, new WasmRoutedEvent(bubble, e, WasmRoutedPhase.Bubbling), reverse: true);
        if (keyArgs is not null)
        {
            for (var i = route.Count - 1; i >= 0; i--)
            {
                route[i].RaiseKey(keyArgs, preview: false);
                if (keyArgs.Handled) break;
            }
            if (e is WasmInputEvent.Key kk && kk.Args.IsDown && !keyArgs.Handled && TryExecuteInputBindings(route, keyArgs)) return;
            if (e is WasmInputEvent.Key kk2 && kk2.Args.IsDown && !keyArgs.Handled && HandleKeyShortcut(kk2.Args)) return;
        }

        if (mouseArgs is not null)
        {
            for (var i = route.Count - 1; i >= 0; i--)
            {
                route[i].RaiseMouse(mouseArgs, preview: false);
                if (mouseArgs.Handled) break;
            }
            // Mouse bindings run after an unhandled mouse bubble, mirroring
            // the key path below: a pressed button (clicks and double-clicks
            // alike) or a wheel tick scans the focused-first route for the
            // first matching MouseGesture and runs its command.
            if (!mouseArgs.Handled && mouseArgs is MouseButtonEventArgs pressed && pressed.ButtonState == MouseButtonState.Pressed)
                TryExecuteInputBindings(route, pressed);
            else if (!mouseArgs.Handled && mouseArgs is MouseWheelEventArgs wheel)
                TryExecuteInputBindings(route, wheel);
            // Touch travels the same pointer dispatch after the mouse bubble
            // (WPF touch-then-promotion order); it carries its own contact
            // args with independent handled state.
            if (e is WasmInputEvent.Pointer touchPointer)
                RaiseTouchForPointer(touchPointer.Args);
        }

        // Pen barrel adapter: a non-left button press/release on the real
        // pointer path also travels the stylus-button route (tunneling
        // PreviewStylusButtonDown/Up, then bubble), mirroring desktop
        // stylus-to-mouse promotion for converted barrel handlers.
        if (e is WasmInputEvent.Pointer stylus &&
            (stylus.Args.Kind == WasmPointerKind.Down || stylus.Args.Kind == WasmPointerKind.Up) &&
            stylus.Args.Button == WasmPointerButton.Right)
        {
            var button = new StylusButton(
                Guid.Empty,
                stylus.Args.Kind == WasmPointerKind.Down ? StylusButtonState.Down : StylusButtonState.Up);
            var stylusArgs = new StylusButtonEventArgs(button, Environment.TickCount);
            if (route.Count > 0)
            {
                stylusArgs.Source = route[^1];
                stylusArgs.OriginalSource = route[^1];
            }
            foreach (var el in route) { el.RaiseStylusButton(stylusArgs, preview: true); if (stylusArgs.Handled) return; }
            for (var i = route.Count - 1; i >= 0; i--)
            {
                route[i].RaiseStylusButton(stylusArgs, preview: false);
                if (stylusArgs.Handled) break;
            }
        }

        if (e is WasmInputEvent.Pointer up &&
            (up.Args.Kind == WasmPointerKind.Up || up.Args.Kind == WasmPointerKind.Cancel))
            _captures.Remove(up.Args.PointerId);
    }

    private List<UIElement> _hoverOrder = new();

    /// <summary>
    /// Marks the innermost hit as the input origin (matches WPF's
    /// OriginalSource/Source on routed mouse input; converted navigators
    /// branch on OriginalSource to skip chevron toggles).
    /// </summary>
    private static void MarkOrigin(MouseEventArgs args, List<UIElement> route)
    {
        if (route.Count == 0) return;
        args.Source = route[^1];
        args.OriginalSource = route[^1];
    }

    /// <summary>Pointer move/press/release accounting; builds the matching CLR args.</summary>
    private MouseEventArgs TrackPointer(WasmPointerArgs args)
    {
        _lastPoint = new WpfPoint(args.X, args.Y);
        var button = MapButton(args.Button);
        var clicks = 1;
        if (args.Kind == WasmPointerKind.Down)
        {
            var now = DateTime.UtcNow;
            var dx = args.X - _lastDownPoint.X;
            var dy = args.Y - _lastDownPoint.Y;
            clicks = button == _lastDownButton && (now - _lastDownTime).TotalMilliseconds <= 500 &&
                dx * dx + dy * dy <= 16.0 ? _lastClickCount + 1 : 1;
            _lastDownButton = button;
            _lastDownTime = now;
            _lastDownPoint = _lastPoint.Value;
            _lastClickCount = clicks;
            if (button.HasValue) _heldButtons.Add(button.Value);
        }
        else if (args.Kind is WasmPointerKind.Up or WasmPointerKind.Cancel)
        {
            if (button.HasValue) _heldButtons.Remove(button.Value);
            clicks = Nullable.Equals(button, _lastDownButton) ? _lastClickCount : 1;
        }
        var left = StateOf(MouseButton.Left);
        var middle = StateOf(MouseButton.Middle);
        var right = StateOf(MouseButton.Right);
        if (args.Kind == WasmPointerKind.Move)
            return new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount, left, middle, right,
                StateOf(MouseButton.XButton1), StateOf(MouseButton.XButton2), _modifiers);
        return new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, left, middle, right,
            StateOf(MouseButton.XButton1), StateOf(MouseButton.XButton2),
            button ?? MouseButton.Left,
            args.Kind == WasmPointerKind.Down ? MouseButtonState.Pressed : MouseButtonState.Released,
            clicks, _modifiers);
    }

    private MouseWheelEventArgs BuildWheelArgs(WasmWheelArgs args) =>
        new(Mouse.PrimaryDevice, Environment.TickCount, StateOf(MouseButton.Left), StateOf(MouseButton.Middle),
            StateOf(MouseButton.Right), StateOf(MouseButton.XButton1), StateOf(MouseButton.XButton2), (int)args.DeltaY, _modifiers);

    /// <summary>
    /// Touch synthesis over the real pointer path: every pointer dispatch
    /// carries one touch contact (the browser forwards touchscreens as
    /// pointer events with no separate touch feed). Down mints and activates
    /// the contact, Move updates it, Up/Cancel reports it up and retires it;
    /// each phase tunnels the PreviewTouch* event then bubbles Touch*
    /// through the capture-aware route with enter/leave transitions, like
    /// the WPF touch stack over real contact reports. Moves and stray ups
    /// with no active contact raise nothing (no contact, no touch event).
    /// </summary>
    private void RaiseTouchForPointer(WasmPointerArgs args)
    {
        var action = args.Kind switch
        {
            WasmPointerKind.Down => TouchAction.Down,
            WasmPointerKind.Move => TouchAction.Move,
            _ => TouchAction.Up,
        };
        TouchSession session;
        if (action == TouchAction.Down)
        {
            session = new TouchSession(new WpfTouchDevice(args.PointerId));
            session.Device.OwnerManager = this;
            _touches[args.PointerId] = session;
            session.Device.ReportDown(new Point(args.X, args.Y));
        }
        else if (!_touches.TryGetValue(args.PointerId, out session!))
        {
            return;
        }
        else if (action == TouchAction.Move)
        {
            session.Device.ReportMove(new Point(args.X, args.Y));
        }
        else
        {
            session.Device.MoveTo(new Point(args.X, args.Y));
        }

        var route = ResolveTouchRoute(session, args);
        foreach (var el in route) el.TouchManager = this;
        UpdateTouchOver(session, route);
        var touchArgs = new TouchEventArgs(session.Device, Environment.TickCount);
        if (route.Count > 0)
        {
            touchArgs.Source = route[^1];
            touchArgs.OriginalSource = route[^1];
        }
        session.Device.DirectlyOver = route.Count > 0 ? route[^1] : null;
        foreach (var el in route)
        {
            el.RaiseTouch(touchArgs, action, preview: true);
            if (touchArgs.Handled) break;
        }
        if (!touchArgs.Handled)
        {
            for (var i = route.Count - 1; i >= 0; i--)
            {
                route[i].RaiseTouch(touchArgs, action, preview: false);
                if (touchArgs.Handled) break;
            }
        }

        if (action != TouchAction.Up) return;
        for (var i = session.OverOrder.Count - 1; i >= 0; i--)
            session.OverOrder[i].RaiseTouchLeave(TouchArgsFor(session, session.OverOrder[i]));
        session.OverSet.Clear();
        session.OverOrder.Clear();
        session.Device.ReportUp(new Point(args.X, args.Y));
        if (session.Device.Captured is UIElement holder)
        {
            session.Device.SetCapture(null, CaptureMode.None);
            holder.TouchManager = this;
            holder.RaiseLostTouchCapture(TouchArgsFor(session, holder));
        }
        session.Device.DirectlyOver = null;
        _touches.Remove(args.PointerId);
    }

    private static TouchEventArgs TouchArgsFor(TouchSession session, UIElement element)
    {
        var args = new TouchEventArgs(session.Device, Environment.TickCount)
        {
            Source = element,
            OriginalSource = element,
        };
        return args;
    }

    /// <summary>Touch route: an intact touch capture wins, else fresh hit testing (never the mouse route, which may ride mouse capture).</summary>
    private List<UIElement> ResolveTouchRoute(TouchSession session, WasmPointerArgs args)
    {
        if (session.Device.Captured is UIElement captured)
        {
            foreach (var root in _roots)
            {
                var captureRoute = new List<UIElement>();
                if (BuildRouteTo(root, captured, captureRoute)) return captureRoute;
            }
            session.Device.SetCapture(null, CaptureMode.None);
        }
        return new List<UIElement>(GetRoute(args.X, args.Y));
    }

    /// <summary>Enter/leave transitions for touch-route changes (leaves innermost first, like hover).</summary>
    private static void UpdateTouchOver(TouchSession session, List<UIElement> route)
    {
        var next = new HashSet<UIElement>(route);
        for (var i = session.OverOrder.Count - 1; i >= 0; i--)
        {
            if (!next.Contains(session.OverOrder[i]))
                session.OverOrder[i].RaiseTouchLeave(TouchArgsFor(session, session.OverOrder[i]));
        }
        foreach (var el in route)
        {
            if (session.OverSet.Add(el)) el.RaiseTouchEnter(TouchArgsFor(session, el));
        }
        session.OverSet.RemoveWhere(el => !next.Contains(el));
        session.OverOrder = new List<UIElement>(route);
    }

    /// <summary>
    /// Touch capture (matches UIElement.CaptureTouch): the device must be an
    /// active contact of this manager (pointer-id match, never ambient
    /// cross-talk) and the element must sit in a live tree of this manager.
    /// </summary>
    internal bool TryCaptureTouch(UIElement element, TouchDevice device, CaptureMode mode)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(device);
        if (!device.IsActive) return false;
        if (!_touches.TryGetValue(device.Id, out var session) || !ReferenceEquals(session.Device, device)) return false;
        foreach (var root in _roots)
        {
            if (ReferenceEquals(root, element) || IsInSubtree(root, element))
            {
                device.SetCapture(element, mode);
                element.TouchManager = this;
                element.RaiseGotTouchCapture(TouchArgsFor(session, element));
                return true;
            }
        }
        return false;
    }

    /// <summary>Touch capture release (matches UIElement.ReleaseTouchCapture).</summary>
    internal bool ReleaseTouchCapture(UIElement element, TouchDevice device)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(device);
        if (!ReferenceEquals(device.Captured, element)) return false;
        device.SetCapture(null, CaptureMode.None);
        element.TouchManager = this;
        if (_touches.TryGetValue(device.Id, out var session) && ReferenceEquals(session.Device, device))
            element.RaiseLostTouchCapture(TouchArgsFor(session, element));
        else
            element.RaiseLostTouchCapture(new TouchEventArgs(device, Environment.TickCount)
            {
                Source = element,
                OriginalSource = element,
            });
        return true;
    }

    /// <summary>Releases every contact held by an element (matches UIElement.ReleaseAllTouchCaptures).</summary>
    internal void ReleaseAllTouchCaptures(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        foreach (var session in _touches.Values)
        {
            if (!ReferenceEquals(session.Device.Captured, element)) continue;
            session.Device.SetCapture(null, CaptureMode.None);
            element.TouchManager = this;
            element.RaiseLostTouchCapture(TouchArgsFor(session, element));
        }
    }

    /// <summary>Contacts captured to an element (matches UIElement.TouchesCaptured).</summary>
    internal IEnumerable<TouchDevice> CapturedTouches(UIElement element) =>
        _touches.Values
            .Where(s => ReferenceEquals(s.Device.Captured, element))
            .Select(s => (TouchDevice)s.Device)
            .ToList();

    /// <summary>Contacts captured into a subtree (matches UIElement.TouchesCapturedWithin).</summary>
    internal IEnumerable<TouchDevice> CapturesWithin(UIElement element) =>
        _touches.Values
            .Where(s => s.Device.Captured is UIElement cap &&
                (ReferenceEquals(cap, element) || IsInSubtree(element, cap)))
            .Select(s => (TouchDevice)s.Device)
            .ToList();

    /// <summary>Contacts directly over an element (matches UIElement.TouchesDirectlyOver).</summary>
    internal IEnumerable<TouchDevice> DirectlyOverTouches(UIElement element) =>
        _touches.Values
            .Where(s => ReferenceEquals(s.Device.DirectlyOver, element))
            .Select(s => (TouchDevice)s.Device)
            .ToList();

    /// <summary>Contacts over an element or a descendant (matches UIElement.TouchesOver).</summary>
    internal IEnumerable<TouchDevice> OverTouches(UIElement element) =>
        _touches.Values
            .Where(s => s.OverSet.Contains(element))
            .Select(s => (TouchDevice)s.Device)
            .ToList();

    private MouseButtonState StateOf(MouseButton button) =>
        _heldButtons.Contains(button) ? MouseButtonState.Pressed : MouseButtonState.Released;

    private static MouseButton? MapButton(WasmPointerButton button) => button switch
    {
        WasmPointerButton.Left => MouseButton.Left,
        WasmPointerButton.Middle => MouseButton.Middle,
        WasmPointerButton.Right => MouseButton.Right,
        _ => null,
    };

    /// <summary>Enter/leave transitions for hover-route changes (innermost first out).</summary>
    private void UpdateHover(List<UIElement> route, MouseEventArgs args)
    {
        var next = new HashSet<UIElement>(route);
        for (var i = _hoverOrder.Count - 1; i >= 0; i--)
            if (!next.Contains(_hoverOrder[i])) _hoverOrder[i].RaiseMouseLeave(args);
        foreach (var el in route)
            if (_hoverRoute.Add(el)) el.RaiseMouseEnter(args);
        _hoverRoute.RemoveWhere(el => !next.Contains(el));
        _hoverOrder = new List<UIElement>(route);
        DirectlyOverElement = route.Count > 0 ? route[^1] : null;
        Mouse.PrimaryDevice.DirectlyOver = DirectlyOverElement;
    }

    /// <summary>Updates held keys + modifiers; returns true for auto-repeat.</summary>
    private bool TrackKeyboard(WasmKeyArgs args)
    {
        _modifiers = (args.Ctrl ? ModifierKeys.Control : ModifierKeys.None)
            | (args.Shift ? ModifierKeys.Shift : ModifierKeys.None)
            | (args.Alt ? ModifierKeys.Alt : ModifierKeys.None);
        Keyboard.PrimaryDevice.Modifiers = _modifiers;
        var name = CanonicalDomHeldName(args.Key);
        if (name is null) return false;
        var repeat = args.IsDown && _heldKeys.Contains(name);
        if (args.IsDown) _heldKeys.Add(name);
        else _heldKeys.Remove(name);
        return repeat;
    }

    private static string? CanonicalDomHeldName(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (key.Length == 1) return key.ToUpperInvariant();
        return key;
    }

    /// <summary>
    /// Input-binding dispatch (matches CommandManager.TranslateInput): after
    /// an unhandled key or mouse bubble, the focused-first route is scanned
    /// for the first binding whose gesture matches; the command runs with
    /// its parameter when CanExecute holds and the input is marked handled.
    /// Key bindings run before the Tab/Enter/space shortcuts below, like the
    /// explicit gestures they are.
    /// </summary>
    private static bool TryExecuteInputBindings(List<UIElement> route, InputEventArgs inputArgs)
    {
        for (var i = route.Count - 1; i >= 0; i--)
        {
            var bindings = route[i].InputBindings;
            if (bindings.Count == 0) continue;
            var match = bindings.FindMatch(route[i], inputArgs);
            if (match?.Command is null) continue;
            if (!match.Command.CanExecute(match.CommandParameter)) continue;
            match.Command.Execute(match.CommandParameter);
            inputArgs.Handled = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Cheap keyboard activation: Tab moves focus, Enter/Space click the
    /// focused button (both on key-down as a documented simplification).
    /// The target is ButtonBase like native activation, so toggle buttons
    /// (CheckBox/RadioButton, which toggle through OnClick) activate too —
    /// a Button-only gate silently drops Space/Enter on focused checkboxes.
    /// Returns true when the key was consumed (no further routing).
    /// </summary>
    private bool HandleKeyShortcut(WasmKeyArgs args)
    {
        if (string.Equals(args.Key, "Tab", StringComparison.Ordinal))
        {
            MoveFocus(args.Shift ? -1 : 1);
            return true;
        }
        if ((string.Equals(args.Key, "Enter", StringComparison.Ordinal) || args.Key == " ") &&
            Focused is ButtonBase focused)
        {
            focused.ActivateByKeyboard();
            return true;
        }
        // Closed drop-downs open on Space like native ComboBox keyboard
        // handling (non-editable simplification: the engine renders no
        // in-place editor, so Space always opens; Escape still closes).
        if (args.Key == " " && Focused is ComboBox closed && !closed.IsDropDownOpen)
        {
            closed.IsDropDownOpen = true;
            return true;
        }
        return false;
    }

    /// <summary>Focuses the next/previous focusable element in route order (wraps).</summary>
    private void MoveFocus(int direction)
    {
        var order = TabOrder();
        if (order.Count == 0) return;
        var index = Focused is null ? (direction >= 0 ? -1 : 0) : order.IndexOf(Focused);
        if (index < 0) index = direction >= 0 ? -1 : 0;
        var next = order[(index + direction + order.Count) % order.Count];
        SetFocused(next);
    }

    /// <summary>Moves focus per request (directional requests return false: no spatial nav).</summary>
    internal bool MoveFocus(TraversalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var order = TabOrder();
        if (order.Count == 0) return false;
        switch (request.FocusNavigationDirection)
        {
            case FocusNavigationDirection.Next:
                MoveFocus(1);
                return true;
            case FocusNavigationDirection.Previous:
                MoveFocus(-1);
                return true;
            case FocusNavigationDirection.First:
                SetFocused(order[0]);
                return true;
            case FocusNavigationDirection.Last:
                SetFocused(order[^1]);
                return true;
            default:
                return false;
        }
    }

    private List<UIElement> TabOrder()
    {
        var order = new List<UIElement>();
        foreach (var root in _roots)
        {
            if (root is Window w && !w.IsVisible) continue;
            if (ModalRoot is not null && !ReferenceEquals(root, ModalRoot)) continue;
            CollectFocusable(root, order);
        }
        return order;
    }

    private static void CollectFocusable(UIElement el, List<UIElement> order)
    {
        // Tab walks tab stops (WPF parity): focusable, enabled and IsTabStop.
        // Programmatic Focus()/press-focus still land on non-tab-stop
        // focusable elements; only the Tab order filters.
        if (el.Focusable && el.IsEffectivelyEnabled && KeyboardNavigation.GetIsTabStop(el)) order.Add(el);
        if (el is FrameworkElement fe)
            foreach (var child in fe.LogicalChildren) CollectFocusable(child, order);
    }

    private IReadOnlyList<UIElement> ResolvePointerRoute(WasmPointerArgs args)
    {
        // Explicit capture (Mouse.Capture) wins over hit testing.
        if (_explicitCapture is not null)
        {
            foreach (var root in _roots)
            {
                var explicitRoute = new List<UIElement>();
                if (BuildRouteTo(root, _explicitCapture, explicitRoute)) return explicitRoute;
            }
            _explicitCapture = null;
            Mouse.PrimaryDevice.Captured = null;
        }
        if (args.Kind != WasmPointerKind.Down && _captures.TryGetValue(args.PointerId, out var captured))
        {
            // Capture-loss handling: a removed element falls back to hit testing.
            foreach (var root in _roots)
            {
                var route = new List<UIElement>();
                if (BuildRouteTo(root, captured, route)) return route;
            }
            _captures.Remove(args.PointerId);
        }
        return GetRoute(args.X, args.Y);
    }

    private List<UIElement> FocusRoute()
    {
        var route = new List<UIElement>();
        if (ModalRoot is not null)
        {
            // Keyboard stays inside the dialog; stale outer focus falls back
            // to the dialog root itself.
            if (Focused is not null && IsInSubtree(ModalRoot, Focused))
            {
                if (BuildRouteTo(ModalRoot, Focused, route)) return route;
            }
            route.Clear();
            route.Add(ModalRoot);
            return route;
        }
        if (Focused is null)
        {
            foreach (var r in _roots) if (IsShown(r)) route.Add(r);
            return route;
        }

        foreach (var root in _roots)
        {
            route.Clear();
            if (BuildRouteTo(root, Focused, route)) return route;
        }

        // Parent-parented-but-not-logical content (e.g. a DataGrid editing
        // element, which parents back to its cell without joining the
        // logical collection): the focused element must still own its keys,
        // so route the live Parent chain instead of broadcasting to roots
        // (which silently drops typing/commit on the focused editor).
        if (Focused is not null)
        {
            route.Clear();
            for (var p = Focused; p is not null; p = p.Parent) route.Add(p);
            route.Reverse();
            if (route.Count > 0) return route;
        }

        route.Clear();
        foreach (var r in _roots) if (IsShown(r)) route.Add(r);
        return route;
    }

    private void Deliver(List<UIElement> route, WasmRoutedEvent e, bool reverse)
    {
        if (reverse)
        {
            for (var i = route.Count - 1; i >= 0; i--)
            {
                if (e.Handled) return;
                Routed?.Invoke(route[i], e.Source, e.Phase);
                route[i].OnRoutedEvent(e);
            }
        }
        else
        {
            foreach (var node in route)
            {
                if (e.Handled) return;
                Routed?.Invoke(node, e.Source, e.Phase);
                node.OnRoutedEvent(e);
            }
        }
    }

    private static (string Tunnel, string Bubble) EventNames(WasmInputEvent e) => e switch
    {
        WasmInputEvent.Pointer p when p.Args.Kind == WasmPointerKind.Down => (WasmRoutedEvent.PreviewPointerDownName, WasmRoutedEvent.PointerDownName),
        WasmInputEvent.Pointer p when p.Args.Kind == WasmPointerKind.Move => (WasmRoutedEvent.PreviewPointerMoveName, WasmRoutedEvent.PointerMoveName),
        WasmInputEvent.Pointer p when p.Args.Kind == WasmPointerKind.Cancel => (WasmRoutedEvent.PreviewPointerCancelName, WasmRoutedEvent.PointerCancelName),
        WasmInputEvent.Pointer => (WasmRoutedEvent.PreviewPointerUpName, WasmRoutedEvent.PointerUpName),
        WasmInputEvent.Wheel => (WasmRoutedEvent.PreviewWheelName, WasmRoutedEvent.WheelName),
        WasmInputEvent.Key k when k.Args.IsDown => (WasmRoutedEvent.PreviewKeyDownName, WasmRoutedEvent.KeyDownName),
        WasmInputEvent.Key => (WasmRoutedEvent.PreviewKeyUpName, WasmRoutedEvent.KeyUpName),
        _ => ("PreviewUnknown", "Unknown"),
    };

    /// <summary>
    /// Whether a caption-gated descent may enter the child: outside the
    /// caption (or under an already chrome-active ancestor) plain geometry
    /// decides; inside the band the child must be chrome-marked or lead to
    /// marked content, otherwise the point belongs to the window chrome.
    /// </summary>
    private static bool CaptionAllows(UIElement child, WpfPoint point, bool gate, bool chromeActive)
    {
        if (!child.HitTest(point)) return false;
        if (!gate) return true;
        if (chromeActive || WindowChrome.GetIsHitTestVisibleInChrome(child)) return true;
        return WindowChrome.SubtreeHasChromeHit(child);
    }

    /// <summary>
    /// Topmost overlay owner in the subtree (menus, popups): owned overlays
    /// paint above later siblings across branches (the post-pass paints open
    /// menu panels above everything beneath them), so input matches paint
    /// and a panel row wins over content arranged beneath it, like the
    /// gallery MenuPage drop-down over the Source-code section. Later
    /// siblings win, mirroring sibling paint order. Only containing owners
    /// return, so a miss never shadows the regular walk.
    /// </summary>
    private static UIElement? HitTestOverlayDeep(UIElement el, WpfPoint point, bool gate, bool chromeActive)
    {
        if (el is FrameworkElement fe)
        {
            var active = chromeActive || !gate || WindowChrome.GetIsHitTestVisibleInChrome(el);
            for (var i = fe.LogicalChildren.Count - 1; i >= 0; i--)
                if (HitTestOverlayDeep(fe.LogicalChildren[i], point, gate && !active, active) is { } deep)
                    return deep;
        }
        if (el.HitTestOverlay(point) is { } overlay &&
            overlay.HitTest(point) &&
            CaptionAllows(overlay, point, gate, chromeActive))
            return overlay;
        return null;
    }

    private static UIElement? HitTestRecursive(UIElement el, WpfPoint point, bool gate, bool chromeActive)
    {
        if (!el.HitTest(point)) return null;
        var active = chromeActive || !gate || WindowChrome.GetIsHitTestVisibleInChrome(el);
        if (el is FrameworkElement fe)
        {
            // Owned overlays (menu drop-downs) paint above later siblings
            // across branches, so a child's panel wins over content arranged
            // beneath it anywhere in the subtree.
            for (var i = fe.LogicalChildren.Count - 1; i >= 0; i--)
                if (HitTestOverlayDeep(fe.LogicalChildren[i], point, gate && !active, active) is { } overlay &&
                    HitTestRecursive(overlay, point, gate && !active, active) is { } overlayHit) return overlayHit;
            for (var i = fe.LogicalChildren.Count - 1; i >= 0; i--)
            {
                var child = fe.LogicalChildren[i];
                if (!CaptionAllows(child, point, gate && !active, active)) continue;
                if (HitTestRecursive(child, point, gate && !active, active) is { } hit) return hit;
            }
            // A gated, chrome-inactive container is transparent: only the
            // window root itself absorbs the caption point (drag region).
            if (gate && !active) return el is Window ? el : null;
        }
        else if (gate && !active)
        {
            return el is Window ? el : null;
        }

        return el;
    }

    private static bool BuildRoute(UIElement el, WpfPoint point, List<UIElement> route, bool gate, bool chromeActive)
    {
        route.Add(el);
        var active = chromeActive || !gate || WindowChrome.GetIsHitTestVisibleInChrome(el);
        var childGate = gate && !active;
        if (el is FrameworkElement fe)
        {
            // Owned overlays (menu drop-downs) paint above later siblings
            // across branches, so a panel wins over content arranged beneath
            // it anywhere in the subtree; the owner stays in the route,
            // keeping the full root-to-entry chain for tunneling/bubbling
            // and implicit focus.
            for (var i = fe.LogicalChildren.Count - 1; i >= 0; i--)
            {
                var owner = fe.LogicalChildren[i];
                if (HitTestOverlayDeep(owner, point, childGate, active) is { } overlay)
                {
                    // The overlay may sit deeper than the direct child, so
                    // splice the structural owner-to-overlay chain in (the
                    // menu between a panel and its host must stay routed).
                    var middle = new List<UIElement>();
                    if (!BuildRouteTo(owner, overlay, middle) || middle.Count == 0) continue;
                    route.AddRange(middle);
                    return BuildRoute(overlay, point, route, childGate, active);
                }
            }
            for (var i = fe.LogicalChildren.Count - 1; i >= 0; i--)
            {
                var child = fe.LogicalChildren[i];
                if (!CaptionAllows(child, point, childGate, active)) continue;
                return BuildRoute(child, point, route, childGate, active);
            }
            if (gate && !active) return el is Window;
        }
        else if (gate && !active)
        {
            return el is Window;
        }

        return true;
    }

    private static bool BuildRouteTo(UIElement el, UIElement target, List<UIElement> route)
    {
        route.Add(el);
        if (ReferenceEquals(el, target)) return true;
        if (el is FrameworkElement fe)
        {
            foreach (var child in fe.LogicalChildren)
            {
                if (!BuildRouteTo(child, target, route)) continue;
                return true;
            }
        }

        route.RemoveAt(route.Count - 1);
        return false;
    }

}

/// <summary>
/// Single viewport transform shared by drawing, hit testing, pointer capture
/// and wheel coordinates. The canvas keeps a fixed logical size (800x600) and
/// is CSS-stretched with aspect preserved (object-fit: contain); JS sizes the
/// backing pixels by devicePixelRatio and letterboxes uniformly, so this
/// contain-fit math inverts the display mapping for input. Drawing applies the
/// same uniform scale via the canvas transform (see wasm-host.js fitCanvas).
/// </summary>
public static class WpfViewport
{
    /// <summary>Uniform contain-fit scale and letterbox offsets for an element box.</summary>
    public static (double Scale, double OffsetX, double OffsetY) ContainFit(
        double rectWidth, double rectHeight, double logicalWidth = 800, double logicalHeight = 600)
    {
        if (rectWidth <= 0 || rectHeight <= 0 || logicalWidth <= 0 || logicalHeight <= 0)
            return (1, 0, 0);
        var scale = Math.Min(rectWidth / logicalWidth, rectHeight / logicalHeight);
        if (!(scale > 0)) return (1, 0, 0);
        return (scale, (rectWidth - logicalWidth * scale) / 2, (rectHeight - logicalHeight * scale) / 2);
    }

    /// <summary>Maps element-box offsets (e.g. PointerEvent OffsetX/Y) to logical coordinates.</summary>
    public static (double X, double Y) MapToLogical(
        double offsetX, double offsetY, double rectWidth, double rectHeight,
        double logicalWidth = 800, double logicalHeight = 600)
    {
        var (scale, offX, offY) = ContainFit(rectWidth, rectHeight, logicalWidth, logicalHeight);
        return ((offsetX - offX) / scale, (offsetY - offY) / scale);
    }

    /// <summary>Inverse mapping: logical coordinates to element-box offsets.</summary>
    public static (double OffsetX, double OffsetY) MapToClient(
        double x, double y, double rectWidth, double rectHeight,
        double logicalWidth = 800, double logicalHeight = 600)
    {
        var (scale, offX, offY) = ContainFit(rectWidth, rectHeight, logicalWidth, logicalHeight);
        return (x * scale + offX, y * scale + offY);
    }
}
