using System.Runtime.InteropServices;

namespace DesktopWasm.Wpf;

/// <summary>
/// Win32 content host (matches System.Windows.Interop.HwndHost): abstract
/// FrameworkElement subclass converted interop controls derive
/// (AvalonDock's LayoutAutoHideWindowControl overrides BuildWindowCore,
/// DestroyWindowCore, HasFocusWithinCore, MeasureOverride and ArrangeOverride
/// and calls Handle, UpdateWindowPos and Dispose).
/// The browser session has no HWND: Handle stays IntPtr.Zero, message hooks
/// are retained but never invoked (no Win32 pump, like HwndSource hooks),
/// UpdateWindowPos is a no-op (position is layout-driven), and Dispose only
/// marks disposal. Live-verified against PresentationFramework 9.0.21:
/// protected constructor, Handle/MessageHook/DpiChanged surface, abstract
/// BuildWindowCore/DestroyWindowCore, virtual WndProc/HasFocusWithinCore/
/// OnDpiChanged, DpiChangedEvent, UpdateWindowPos, Dispose.
/// </summary>
public abstract class HwndHost : FrameworkElement
{
    public static readonly RoutedEvent DpiChangedEvent = EventManager.RegisterRoutedEvent(
        "DpiChanged", RoutingStrategy.Bubble, typeof(DpiChangedEventHandler), typeof(HwndHost));

    private bool _disposed;

    protected HwndHost()
    {
    }

    /// <summary>No HWND exists in the browser session (always zero).</summary>
    public IntPtr Handle => IntPtr.Zero;

    /// <summary>Retained hooks (never invoked: no Win32 message pump).</summary>
    public event HwndSourceHook? MessageHook;

    /// <summary>DPI-change notification (raised by OnDpiChanged).</summary>
    public event DpiChangedEventHandler? DpiChanged;

    /// <summary>Whether Dispose has run (engine seam, like HwndSource).</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Creates the hosted window (upstream provides the real HWND path).</summary>
    protected abstract HandleRef BuildWindowCore(HandleRef hwndParent);

    /// <summary>Destroys the hosted window (upstream provides the real HWND path).</summary>
    protected abstract void DestroyWindowCore(HandleRef hwnd);

    /// <summary>Window procedure (default ignores; subclass to intercept).</summary>
    protected virtual IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        handled = false;
        return IntPtr.Zero;
    }

    /// <summary>Focus query for hosted content (default false).</summary>
    protected virtual bool HasFocusWithinCore() => false;

    /// <summary>No HWND to reposition (position is layout-driven).</summary>
    public void UpdateWindowPos()
    {
    }

    /// <summary>Raises DpiChanged (host DPI sync drives this).</summary>
    protected virtual void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) =>
        DpiChanged?.Invoke(this, new DpiChangedEventArgs(oldDpi, newDpi));

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) => _disposed = true;
}
