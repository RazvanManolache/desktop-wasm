extern alias Fork;

using DesktopWasm.Core;
using DesktopWasm.Wpf;
using ForkForms = Fork::System.Windows.Forms;

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

    private enum ActiveHalf { None, Wpf, WinForms }

    private ActiveHalf _activeHalf = ActiveHalf.None;

    /// <summary>
    /// True while the WPF half is the session's focused half and still
    /// holds engine focus. The engines do not share focus state, so the
    /// mixed session is the cross-boundary focus authority.
    /// </summary>
    public bool WpfHasFocus => _activeHalf == ActiveHalf.Wpf && Wpf.Manager.Focused is not null;

    /// <summary>
    /// True while the WinForms half is the session's focused half and a
    /// real control still holds focus.
    /// </summary>
    public bool WinFormsHasFocus => _activeHalf == ActiveHalf.WinForms && HasFocusedControl(WinForms.Content);

    /// <summary>
    /// Focuses the first WPF tab stop (pre-order, same eligibility the
    /// engine's own Tab order uses). Returns whether focus landed.
    /// </summary>
    public bool FocusFirstInWpf()
    {
        ThrowIfDisposed();
        var landed = FocusDeepWpf(Wpf.Content);
        if (landed)
            _activeHalf = ActiveHalf.Wpf;
        return landed;
    }

    /// <summary>
    /// Focuses the first WinForms tab stop (container roots start at
    /// their children, mirroring tab order). Returns whether focus landed.
    /// </summary>
    public bool FocusFirstInWinForms()
    {
        ThrowIfDisposed();
        bool landed;
        if (WinForms.Content is ForkForms.ContainerControl)
        {
            landed = false;
            foreach (ForkForms.Control child in WinForms.Content.Controls)
                if (FocusDeepWinForms(child))
                {
                    landed = true;
                    break;
                }
        }
        else
        {
            landed = FocusDeepWinForms(WinForms.Content);
        }
        if (landed)
            _activeHalf = ActiveHalf.WinForms;
        return landed;
    }

    /// <summary>
    /// Cross-engine boundary transfer forward: focus leaves whichever half
    /// holds it and lands on the first stop of the other half, through the
    /// real focus APIs on both sides. Both halves wrap internally, so the
    /// boundary is the only way across.
    /// </summary>
    public void FocusNextHalf()
    {
        ThrowIfDisposed();
        if (WpfHasFocus)
            FocusFirstInWinForms();
        else
            FocusFirstInWpf();
    }

    /// <summary>Mirror of <see cref="FocusNextHalf"/> in the reverse direction.</summary>
    public void FocusPreviousHalf()
    {
        ThrowIfDisposed();
        if (WinFormsHasFocus)
            FocusFirstInWpf();
        else
            FocusFirstInWinForms();
    }

    private static bool FocusDeepWpf(UIElement element)
    {
        if (element.Focusable && element.IsEffectivelyEnabled &&
            KeyboardNavigation.GetIsTabStop(element) && element.Focus())
            return true;
        if (element is FrameworkElement framework)
            foreach (var child in framework.LogicalChildren)
                if (FocusDeepWpf(child))
                    return true;
        return false;
    }

    private static bool FocusDeepWinForms(ForkForms.Control control)
    {
        if (control.TabStop && control.CanFocus && control.Focus())
            return true;
        foreach (ForkForms.Control child in control.Controls)
            if (FocusDeepWinForms(child))
                return true;
        return false;
    }

    private static bool HasFocusedControl(ForkForms.Control control)
    {
        if (control.Focused)
            return true;
        foreach (ForkForms.Control child in control.Controls)
            if (HasFocusedControl(child))
                return true;
        return false;
    }

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
