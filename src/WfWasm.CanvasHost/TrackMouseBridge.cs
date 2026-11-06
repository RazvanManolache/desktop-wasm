// REBOOT1555: host-side TrackMouseEvent delivery bridge. Converted code
// registers hover/leave intent through the fork PInvoke stub into the
// WPF-owned TrackMouseTracker registry (fork file, never written from
// this lane); the host pump drains it here to deliver real managed
// OnMouseHover/OnMouseLeave. Read-only by construction: the bridge only
// ever queries and consumes (one-shot) tracker entries.
//
// Semantics (Win32-faithful, headless-noted):
// - Explicit registration directs delivery for that hwnd. HOVER fires on
//   the registration's HoverTime (HOVER_DEFAULT = system MouseHoverTime,
//   0 = immediate). LEAVE fires when the pointer leaves. Anything else
//   while an entry lives (LEAVE-only under CheckHover, HOVER-only under
//   SetHoverControl, pure NONCLIENT anywhere) suppresses: wrong-event dup
//   none. Delivery consumes the entry (one-shot; re-arm needs a fresh
//   TrackMouseEvent call). Leaving the control ends tracking either way.
// - QUERY never stores (tracker-side) and CANCEL removes (tracker-side);
//   the host tombstones a vanished entry it previously observed, so a
//   cancel between ticks suppresses instead of falling back to baseline.
// - No entry and never observed: framework auto-track equivalent
//   baseline (today's unconditional synthesis), unchanged.
// - NONCLIENT-only: headless has no non-client area, so it never fires.
//   Combined NONCLIENT|HOVER (or |LEAVE) still matches the client pointer:
//   the flag refines an area that does not exist headless, the intent is
//   what matters.
// - Fail-closed: unbound registry (older fork binary without the tracker),
//   missing handles (0), or any reflection error behaves as untracked.
using System.Reflection;

namespace WfWasm.CanvasHost;

internal static class TrackMouseBridge
{
    public const uint TME_HOVER = 0x00000001;
    public const uint TME_LEAVE = 0x00000002;
    public const uint TME_NONCLIENT = 0x00000010;
    public const uint TME_QUERY = 0x40000000;
    public const uint TME_CANCEL = 0x80000000;
    public const uint HOVER_DEFAULT = 0xFFFFFFFF;

    private const string TrackerTypeName = "System.Windows.Forms.Primitives.TrackMouseTracker";

    /// <summary>Minimal read surface the pump needs. Implemented by the
    /// reflection binder (real tracker) or a fake in tests.</summary>
    internal interface ITrackRegistry
    {
        bool TryGet(nint hwnd, out uint flags, out uint hoverTimeMs);
        void Remove(nint hwnd);
    }

    internal static ITrackRegistry Registry { get; set; } = new ReflectionRegistry();

    internal static bool TryGetTrack(nint hwnd, out uint flags, out uint hoverTimeMs)
    {
        flags = 0;
        hoverTimeMs = HOVER_DEFAULT;
        if (hwnd == IntPtr.Zero)
            return false;
        try
        {
            return Registry.TryGet(hwnd, out flags, out hoverTimeMs);
        }
        catch
        {
            return false;
        }
    }

    internal static void Consume(nint hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        try
        {
            Registry.Remove(hwnd);
        }
        catch
        {
        }
    }

    internal static nint HandleOf(System.Windows.Forms.Control control)
    {
        try
        {
            return control.Handle;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>Binds the WPF-owned fork tracker read-only via reflection
    /// (no fork edits, no visibility grants needed). Unbound when the
    /// loaded fork binary predates the tracker: every query misses.</summary>
    internal sealed class ReflectionRegistry : ITrackRegistry
    {
        private readonly object _gate = new();
        private MethodInfo? _query;
        private MethodInfo? _cancel;
        private bool _bound;
        private bool _tried;

        private void Ensure()
        {
            lock (_gate)
            {
                if (_tried)
                    return;
                _tried = true;
                try
                {
                    Assembly fork = typeof(System.Windows.Forms.Control).Assembly;
                    Type? tracker = fork.GetType(TrackerTypeName);
                    _query = tracker?.GetMethod("QueryTrack",
                        BindingFlags.NonPublic | BindingFlags.Static);
                    _cancel = tracker?.GetMethod("CancelTrack",
                        BindingFlags.NonPublic | BindingFlags.Static);
                    _bound = _query != null && _cancel != null;
                }
                catch
                {
                    _bound = false;
                }
            }
        }

        public bool TryGet(nint hwnd, out uint flags, out uint hoverTimeMs)
        {
            flags = 0;
            hoverTimeMs = HOVER_DEFAULT;
            Ensure();
            if (!_bound)
                return false;
            lock (_gate)
            {
                try
                {
                    object?[] args = new object?[] { hwnd, null };
                    object? hit = _query!.Invoke(null, args);
                    if (hit is not true || args[1] is null)
                        return false;
                    Type current = args[1].GetType();
                    flags = Convert.ToUInt32(current.GetField("dwFlags")?.GetValue(args[1]));
                    hoverTimeMs = Convert.ToUInt32(current.GetField("dwHoverTime")?.GetValue(args[1]));
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public void Remove(nint hwnd)
        {
            Ensure();
            if (!_bound)
                return;
            lock (_gate)
            {
                try
                {
                    _cancel!.Invoke(null, new object?[] { hwnd });
                }
                catch
                {
                }
            }
        }
    }
}
