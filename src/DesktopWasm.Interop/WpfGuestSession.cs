using DesktopWasm.Core;
using DesktopWasm.Wpf;

namespace DesktopWasm.Interop;

/// <summary>
/// Live WPF-guest host session: owns a real engine <see cref="UIElement"/>,
/// drives its measure/arrange through the real layout core, routes
/// DPI-and-clip translated input through a session-private
/// <see cref="WpfInputManager"/>, and renders frames of draw commands.
/// One direction of the mixed-runtime bridge (WPF content inside a host);
/// the WinForms-guest direction needs the real fork controls and lands once
/// source-ready delivers <c>runtimes/</c>.
/// </summary>
public sealed class WpfGuestSession : IHostedGuest
{
    private readonly WasmInputRouter _router = new();
    private readonly WpfInputManager _manager;
    private readonly WasmWindowHandle _window = new(1);
    private bool _disposed;

    /// <summary>
    /// Hosts <paramref name="content"/> at <paramref name="size"/> layout px
    /// (a zero <paramref name="dpi"/> means identity). UIElement is the
    /// engine root type; rendering goes through the public headless path so
    /// no internals access is needed.
    /// </summary>
    public WpfGuestSession(UIElement content, WpfSize size, InteropDpi dpi = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
        Size = RequireLayoutSize(size, nameof(size));
        Dpi = NormalizeDpi(dpi);
        _manager = new WpfInputManager(_router);
        _manager.AddRoot(content);
        Layout();
    }

    /// <summary>The real hosted visual (state mutates here).</summary>
    public UIElement Content { get; }

    /// <summary>Session size in layout px.</summary>
    public WpfSize Size { get; private set; }

    /// <summary>Session DPI (device px per layout px).</summary>
    public InteropDpi Dpi { get; private set; }

    /// <summary>Input manager owning this session (focus/capture evidence).</summary>
    public WpfInputManager Manager => _manager;

    public bool IsDisposed => _disposed;

    public event Action? Invalidated;

    /// <summary>Re-layouts the guest at a new size (guards empty sizes).</summary>
    public void Resize(WpfSize size)
    {
        ThrowIfDisposed();
        Size = RequireLayoutSize(size, nameof(size));
        Layout();
        Invalidated?.Invoke();
    }

    /// <summary>Re-layouts the guest at a new DPI.</summary>
    public void SetDpi(InteropDpi dpi)
    {
        ThrowIfDisposed();
        Dpi = NormalizeDpi(dpi);
        Layout();
        Invalidated?.Invoke();
    }

    /// <summary>
    /// Forwards a device-px pointer event: scales to layout, drops presses
    /// that start outside the clipped host bounds, and routes everything
    /// else through the real manager (implicit capture keeps working).
    /// Returns false only for a dropped outside press.
    /// </summary>
    public bool DispatchPointer(HostPointerEvent pointer)
    {
        ThrowIfDisposed();
        var (x, y) = pointer.ToLayout(Dpi);
        if (pointer.Kind == WasmPointerKind.Down &&
            !pointer.IsInside(Dpi, Size.Width, Size.Height))
            return false;
        var isDown = pointer.Kind == WasmPointerKind.Down;
        _router.DispatchPointer(new WasmPointerArgs(_window, x, y, pointer.Button, isDown)
        {
            Kind = pointer.Kind,
            PointerId = pointer.PointerId,
        });
        return true;
    }

    /// <summary>Forwards a key event to the focused guest through the real manager.</summary>
    public void DispatchKey(HostKeyEvent key)
    {
        ThrowIfDisposed();
        _router.DispatchKeyboard(key.ToWasmArgs(_window));
    }

    /// <summary>
    /// Re-runs layout and renders the guest to a device-px frame through
    /// the public headless path (the same entry ElementHost uses).
    /// </summary>
    public HostedFrame RenderFrame()
    {
        ThrowIfDisposed();
        var commands = WasmMilCore.RenderHeadless(Content, Size);
        var width = Math.Max(1, (int)Math.Round(Size.Width * Dpi.ScaleX));
        var height = Math.Max(1, (int)Math.Round(Size.Height * Dpi.ScaleY));
        return new HostedFrame(width, height, Dpi, commands);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Engine gap (recorded, not hacked): WpfInputManager has no
        // RemoveRoot/ClearFocus, so a parked focus stays readable through
        // Manager.Focused after dispose; the session only guarantees no
        // further dispatch or rendering. GC reclaims the private router.
    }

    private void Layout()
    {
        Content.Measure(Size);
        Content.Arrange(new WpfRect(0, 0, Size.Width, Size.Height));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static WpfSize RequireLayoutSize(WpfSize size, string name)
    {
        if (!double.IsFinite(size.Width) || size.Width <= 0 ||
            !double.IsFinite(size.Height) || size.Height <= 0)
            throw new ArgumentOutOfRangeException(name, "Session size must be finite and positive.");
        return size;
    }

    private static InteropDpi NormalizeDpi(InteropDpi dpi) =>
        dpi.Equals(default) ? InteropDpi.Identity : InteropDpi.ForScale(dpi.ScaleX, dpi.ScaleY);
}
