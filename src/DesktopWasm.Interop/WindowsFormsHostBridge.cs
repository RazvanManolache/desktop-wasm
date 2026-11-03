extern alias Fork;

using DesktopWasm.Core;
using DesktopWasm.Graphics;
using DesktopWasm.Interop;
using DesktopWasm.Wpf;
using ForkForms = Fork::System.Windows.Forms;

namespace System.Windows.Forms.Integration;

/// <summary>
/// Real WindowsFormsHost bridge (the joint interface the AvalonDock
/// closure names as missing assembly WindowsFormsIntegration): a WPF
/// <see cref="FrameworkElement"/> that hosts a REAL fork WinForms
/// <see cref="Fork::System.Windows.Forms.Control"/> through a
/// <see cref="WinFormsGuestSession"/>, translating measure/arrange/DPI,
/// paint and input across the boundary. Sizing follows the hosted
/// control's natural size clamped into the WPF slot; paint replays the
/// guest's texture frame offset to the arranged rect under clip; pointer
/// and keyboard input travel in element-layout px and reach the real
/// control surface (focus/capture/tab/dispose like the guest session).
/// This is the adapter-side canonical contract: the engine stub
/// (DesktopWasm.Wpf.WindowsFormsHost, immediate-paintable only) is
/// untouched, and neither engine references this adapter.
/// </summary>
public sealed class WindowsFormsHost : FrameworkElement, IDisposable, IHostedGuest
{
    private static readonly WpfSize FallbackSessionSize = new(300, 150);

    private ForkForms.Control? _child;
    private WinFormsGuestSession? _session;
    private InteropDpi _dpi = InteropDpi.Identity;
    private bool _disposed;

    public WindowsFormsHost()
    {
        // Hosts focusable content, so the bridge itself takes focus like the
        // desktop host (GetValue would read the UIElement false default).
        Focusable = true;
        // Leaving the live tree ends the guest lifetime, like closing the
        // host window in production (the engine host never removes windows
        // implicitly, so this only fires on real detach of a loaded tree).
        Unloaded += (_, _) => Dispose();
    }

    /// <summary>
    /// The real hosted WinForms control (null detaches). Setting a control
    /// (re)creates the guest session at the control's natural size; layout
    /// afterwards resizes it to the arranged slot.
    /// </summary>
    public ForkForms.Control? Child
    {
        get => _child;
        set
        {
            ThrowIfDisposed();
            if (ReferenceEquals(_child, value))
                return;
            _child = value;
            ResetSession();
        }
    }

    /// <summary>Session DPI (device px per layout px). Re-applied live.</summary>
    public InteropDpi Dpi
    {
        get => _dpi;
        set
        {
            ThrowIfDisposed();
            _dpi = value.Equals(default) ? InteropDpi.Identity : value;
            _session?.SetDpi(_dpi);
        }
    }

    /// <summary>True after <see cref="Dispose"/> runs once.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Raised after resize/DPI changes that require a re-render.</summary>
    public event Action? Invalidated;

    /// <summary>
    /// Measures the hosted control at its natural size clamped into the
    /// WPF slot (infinite axes take the natural size). An empty slot or a
    /// detached child measures zero, like the immediate-mode host.
    /// </summary>
    protected override WpfSize MeasureCore(WpfSize available)
    {
        var session = _session;
        if (session is null || Child is null)
            return new WpfSize(0, 0);
        var natural = NaturalLayoutSize();
        var width = ClampMeasure(available.Width, natural.Width);
        var height = ClampMeasure(available.Height, natural.Height);
        if (width > 0 && height > 0)
            session.Resize(new WpfSize(width, height));
        return new WpfSize(Math.Max(0, width), Math.Max(0, height));
    }

    /// <summary>
    /// The arrange slot is final (base records RenderRect): resize the
    /// guest to it so painted pixels align with the WPF slot.
    /// </summary>
    protected override void ArrangeCore(WpfRect finalRect)
    {
        base.ArrangeCore(finalRect);
        var session = _session;
        if (session is null)
            return;
        if (finalRect.Width > 0 && finalRect.Height > 0)
            session.Resize(new WpfSize(finalRect.Width, finalRect.Height));
    }

    /// <summary>
    /// Paints the real guest: replays its texture frame offset to the
    /// arranged rect under clip. An empty slot or detached child paints
    /// nothing.
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        var rect = RenderRect;
        if (rect.Width <= 0 || rect.Height <= 0)
            return;
        var session = _session;
        if (session is null)
            return;
        var frame = session.RenderFrame();
        dc.PushClip((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
        foreach (var command in frame.Commands)
        {
            // Guest sessions present exactly one texture; anything else is
            // not guest pixels and is never emitted here.
            if (command is DrawCommand.DrawImage image)
                dc.DrawImage(image.Rgba, image.Width, image.Height,
                    (float)rect.X + image.X, (float)rect.Y + image.Y, image.DW, image.DH);
        }
        dc.PopClip();
    }

    /// <summary>
    /// Routes an element-layout-px pointer event to the real control
    /// surface (origin at the arranged rect, scaled to device px).
    /// Outside presses drop like the guest session; returns false for a
    /// dropped outside press.
    /// </summary>
    public bool DispatchPointer(double layoutX, double layoutY, WasmPointerButton button, WasmPointerKind kind)
    {
        var session = ActiveSession();
        var deviceX = _dpi.ToDeviceX(layoutX - RenderRect.X);
        var deviceY = _dpi.ToDeviceY(layoutY - RenderRect.Y);
        return session.DispatchPointer(new HostPointerEvent(deviceX, deviceY, button, kind));
    }

    /// <summary>Routes a key event to the focused guest control.</summary>
    public void DispatchKey(HostKeyEvent key) => ActiveSession().DispatchKey(key);

    /// <summary>Renders the current guest state (session space).</summary>
    public HostedFrame RenderFrame() => ActiveSession().RenderFrame();

    /// <summary>
    /// Browser input path: WPF routed pointer down on the bridge focuses it
    /// and reaches the real control (click/press), like the desktop host.
    /// Post-dispose routing is a silent no-op; teardown input is meaningless.
    /// </summary>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (_disposed)
            return;
        Focus();
        ForwardPointer(e, WasmPointerKind.Down);
    }

    /// <summary>Browser input path: routed release completes the guest press (click).</summary>
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_disposed)
            return;
        ForwardPointer(e, WasmPointerKind.Up);
    }

    /// <summary>Browser input path: routed moves hover/drag the real control surface.</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_disposed || _session is null)
            return;
        var pos = e.GetPosition(this);
        var rect = RenderRect;
        DispatchPointer(rect.X + pos.X, rect.Y + pos.Y, WasmPointerButton.None, WasmPointerKind.Move);
    }

    /// <summary>Browser input path: focused key press reaches the real control.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_disposed)
            return;
        ForwardKey(e, isDown: true);
    }

    /// <summary>Browser input path: focused key release reaches the real control.</summary>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_disposed)
            return;
        ForwardKey(e, isDown: false);
    }

    /// <summary>
    /// Browser focus path: WPF focus landing on the bridge focuses the real
    /// hosted control, so subsequent keyboard routes at real WinForms focus.
    /// </summary>
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (_disposed)
            return;
        Child?.Focus();
    }

    /// <summary>
    /// Disposes the guest session (which disposes the hosted control, like
    /// the guest session contract). Second dispose is a no-op. Marks the
    /// visual tree dirty through the engine channel so a hosting
    /// <c>WasmWpfHost</c> re-renders without the guest on its next Tick
    /// instead of serving the cached frame.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DetachSession();
        _child = null;
        InvalidateVisual();
    }

    private void ForwardPointer(MouseButtonEventArgs e, WasmPointerKind kind)
    {
        WasmPointerButton? button = e.ChangedButton switch
        {
            MouseButton.Left => WasmPointerButton.Left,
            MouseButton.Middle => WasmPointerButton.Middle,
            MouseButton.Right => WasmPointerButton.Right,
            _ => null,
        };
        if (button is null || _session is null)
            return;
        // DispatchPointer takes root-absolute layout coords; GetPosition is
        // bridge-local, so re-anchor at the arranged rect.
        var pos = e.GetPosition(this);
        var rect = RenderRect;
        DispatchPointer(rect.X + pos.X, rect.Y + pos.Y, button.Value, kind);
    }

    private void ForwardKey(KeyEventArgs e, bool isDown)
    {
        if (_session is null)
            return;
        var name = GuestKeyName(e.Key);
        if (name is null)
            return;
        var modifiers = Keyboard.Modifiers;
        DispatchKey(new HostKeyEvent(name, isDown,
            (modifiers & ModifierKeys.Control) != 0,
            (modifiers & ModifierKeys.Shift) != 0,
            (modifiers & ModifierKeys.Alt) != 0));
    }

    /// <summary>
    /// WPF <see cref="Key"/> to the guest key-name vocabulary (the DOM names
    /// <see cref="WinFormsGuestSession"/> maps). Only names the guest owns
    /// are forwarded; anything else drops instead of synthesizing input.
    /// </summary>
    private static string? GuestKeyName(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
            return key.ToString();
        if (key is >= Key.D0 and <= Key.D9)
            return ((int)key - (int)Key.D0).ToString();
        if (key is >= Key.F1 and <= Key.F9)
            return key.ToString();
        return key switch
        {
            Key.Space => " ",
            Key.Back => "Backspace",
            Key.Enter or Key.Return => "Enter",
            Key.Tab => "Tab",
            Key.Escape => "Escape",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp or Key.Prior => "PageUp",
            Key.PageDown or Key.Next => "PageDown",
            Key.Left => "ArrowLeft",
            Key.Right => "ArrowRight",
            Key.Up => "ArrowUp",
            Key.Down => "ArrowDown",
            _ => null,
        };
    }

    private WinFormsGuestSession ActiveSession()
    {
        ThrowIfDisposed();
        return _session ?? throw new InvalidOperationException(
            "WindowsFormsHost.Child is not set: attach a real WinForms control first.");
    }

    private void ResetSession()
    {
        DetachSession();
        var child = _child;
        if (child is null)
            return;
        var natural = NaturalLayoutSize();
        var size = natural.Width > 0 && natural.Height > 0 ? natural : FallbackSessionSize;
        var session = new WinFormsGuestSession(child, size, _dpi);
        session.Invalidated += OnSessionInvalidated;
        _session = session;
    }

    private void DetachSession()
    {
        var session = _session;
        _session = null;
        if (session is null)
            return;
        session.Invalidated -= OnSessionInvalidated;
        session.Dispose();
    }

    private void OnSessionInvalidated() => Invalidated?.Invoke();

    private WpfSize NaturalLayoutSize()
    {
        var child = Child;
        if (child is null)
            return new WpfSize(0, 0);
        return new WpfSize(child.Width / _dpi.ScaleX, child.Height / _dpi.ScaleY);
    }

    private static double ClampMeasure(double available, double natural)
    {
        natural = double.IsFinite(natural) && natural > 0 ? natural : 0;
        if (!double.IsFinite(available) || available < 0)
            return natural;
        return Math.Min(available, natural);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
