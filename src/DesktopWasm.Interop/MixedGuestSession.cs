using DesktopWasm.Core;
using DesktopWasm.Wpf;

namespace DesktopWasm.Interop;

/// <summary>
/// Mixed fixture session: owns BOTH a live WPF guest and a live WinForms
/// guest in one session. Cross-engine event wiring belongs to the app (the
/// tests attach a WPF Click that mutates WinForms state and vice versa);
/// this session provides the joint dispatch surface, joint frames, and
/// joint deterministic lifetime. (Name avoids the shell lane's
/// <c>DesktopWasm.Web.MixedSession</c> boot/availability session: different
/// layer, different ownership.)
/// </summary>
public sealed class MixedGuestSession : IDisposable
{
    private bool _disposed;

    public MixedGuestSession(WpfGuestSession wpf, WinFormsGuestSession winForms)
    {
        ArgumentNullException.ThrowIfNull(wpf);
        ArgumentNullException.ThrowIfNull(winForms);
        Wpf = wpf;
        WinForms = winForms;
    }

    public WpfGuestSession Wpf { get; }

    public WinFormsGuestSession WinForms { get; }

    public bool IsDisposed => _disposed;

    public bool DispatchPointerToWpf(HostPointerEvent pointer)
    {
        ThrowIfDisposed();
        return Wpf.DispatchPointer(pointer);
    }

    public bool DispatchPointerToWinForms(HostPointerEvent pointer)
    {
        ThrowIfDisposed();
        return WinForms.DispatchPointer(pointer);
    }

    public void DispatchKeyToWpf(HostKeyEvent key)
    {
        ThrowIfDisposed();
        Wpf.DispatchKey(key);
    }

    public void DispatchKeyToWinForms(HostKeyEvent key)
    {
        ThrowIfDisposed();
        WinForms.DispatchKey(key);
    }

    /// <summary>Renders both halves (throws after disposal).</summary>
    public (HostedFrame Wpf, HostedFrame WinForms) RenderBoth()
    {
        ThrowIfDisposed();
        return (Wpf.RenderFrame(), WinForms.RenderFrame());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WinForms.Dispose();
        Wpf.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
