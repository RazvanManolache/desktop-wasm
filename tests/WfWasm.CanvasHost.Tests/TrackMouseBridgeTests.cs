using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsControl = System.Windows.Forms.Control;

/// <summary>
/// REBOOT1555: hover/leave delivery against TrackMouseEvent registrations.
/// Real registration -> managed event; canceled/consumed -> none;
/// wrong-event dup none. Baseline (never-registered) synthesis is
/// preserved: the framework auto-tracks every control on desktop, so the
/// host keeps delivering for untracked controls.
/// </summary>
public sealed class TrackMouseBridgeTests
{
    private sealed class FakeRegistry : TrackMouseBridge.ITrackRegistry
    {
        public readonly Dictionary<nint, (uint Flags, uint HoverMs)> Entries = new();
        public bool TryGet(nint hwnd, out uint flags, out uint hoverTimeMs)
        {
            if (Entries.TryGetValue(hwnd, out var e))
            {
                flags = e.Flags;
                hoverTimeMs = e.HoverMs;
                return true;
            }
            flags = 0;
            hoverTimeMs = TrackMouseBridge.HOVER_DEFAULT;
            return false;
        }
        public void Remove(nint hwnd) => Entries.Remove(hwnd);
    }

    private static readonly MethodInfo SetHoverControlMethod =
        typeof(WinFormsAppHost).GetMethod("SetHoverControl",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static nint KeyOf(FormsControl control)
    {
        try { control.CreateControl(); } catch { }
        return TrackMouseBridge.HandleOf(control);
    }

    private static void SetHover(WinFormsAppHost host, FormsControl? control) =>
        SetHoverControlMethod.Invoke(host, new object?[] { control });

    private static (WinFormsAppHost host, FormsButton control, FakeRegistry fake) Arrange()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var control = new FormsButton { Name = "tracked", Text = "Tracked" };
        var fake = new FakeRegistry();
        TrackMouseBridge.Registry = fake;
        return (host, control, fake);
    }

    private static void Restore()
    {
        TrackMouseBridge.Registry = new TrackMouseBridge.ReflectionRegistry();
    }

    [Fact]
    public void Hover_Registered_Timeout_Is_Honored()
    {
        // Registration with a far-future HoverTime must NOT fire yet even
        // though the system hover timeout is long past (pre-wiring pump
        // ignores the registration and fires on system time).
        var (host, control, fake) = Arrange();
        try
        {
            nint hwnd = KeyOf(control);
            fake.Entries[hwnd] = (TrackMouseBridge.TME_HOVER, 1_000_000_000);
            int hovered = 0;
            control.MouseHover += (_, _) => hovered++;
            SetHover(host, control);
            host.CheckHover(Environment.TickCount64);
            Assert.Equal(0, hovered);
            fake.Entries[hwnd] = (TrackMouseBridge.TME_HOVER, 0);
            host.CheckHover(Environment.TickCount64);
            Assert.Equal(1, hovered);
        }
        finally
        {
            Restore();
            host.Dispose();
            control.Dispose();
        }
    }

    [Fact]
    public void Hover_Canceled_Between_Ticks_Fires_None()
    {
        // Seen live on one tick, canceled before the next: tombstoned, no
        // baseline fallback. Pre-wiring pump fires on system time anyway.
        var (host, control, fake) = Arrange();
        try
        {
            nint hwnd = KeyOf(control);
            fake.Entries[hwnd] = (TrackMouseBridge.TME_HOVER, 1_000_000_000);
            int hovered = 0;
            control.MouseHover += (_, _) => hovered++;
            SetHover(host, control);
            host.CheckHover(Environment.TickCount64);
            Assert.Equal(0, hovered);
            fake.Entries.Remove(hwnd);
            host.CheckHover(Environment.TickCount64);
            host.CheckHover(Environment.TickCount64 + 1_000_000);
            Assert.Equal(0, hovered);
        }
        finally
        {
            Restore();
            host.Dispose();
            control.Dispose();
        }
    }

    [Fact]
    public void HoverOnly_Registration_Suppresses_Leave_And_Consumes()
    {
        var (host, control, fake) = Arrange();
        try
        {
            nint hwnd = KeyOf(control);
            fake.Entries[hwnd] = (TrackMouseBridge.TME_HOVER, 0);
            int left = 0;
            control.MouseLeave += (_, _) => left++;
            SetHover(host, control);
            SetHover(host, null);
            Assert.Equal(0, left);
            Assert.Empty(fake.Entries);
        }
        finally
        {
            Restore();
            host.Dispose();
            control.Dispose();
        }
    }

    [Fact]
    public void LeaveOnly_Registration_Suppresses_Hover()
    {
        var (host, control, fake) = Arrange();
        try
        {
            nint hwnd = KeyOf(control);
            fake.Entries[hwnd] = (TrackMouseBridge.TME_LEAVE, TrackMouseBridge.HOVER_DEFAULT);
            int hovered = 0;
            control.MouseHover += (_, _) => hovered++;
            SetHover(host, control);
            host.CheckHover(Environment.TickCount64);
            host.CheckHover(Environment.TickCount64 + 1_000_000);
            Assert.Equal(0, hovered);
            Assert.True(fake.Entries.ContainsKey(hwnd));
        }
        finally
        {
            Restore();
            host.Dispose();
            control.Dispose();
        }
    }

    [Fact]
    public void Leave_Registered_Fires_Once_And_Consumes()
    {
        var (host, control, fake) = Arrange();
        try
        {
            nint hwnd = KeyOf(control);
            fake.Entries[hwnd] = (TrackMouseBridge.TME_LEAVE, TrackMouseBridge.HOVER_DEFAULT);
            int left = 0;
            control.MouseLeave += (_, _) => left++;
            SetHover(host, control);
            SetHover(host, null);
            Assert.Equal(1, left);
            Assert.Empty(fake.Entries);
        }
        finally
        {
            Restore();
            host.Dispose();
            control.Dispose();
        }
    }

    [Fact]
    public void Leave_Untracked_Baseline_Preserved()
    {
        // Never registered: today's unconditional synthesis keeps working
        // (framework auto-track equivalent).
        var (host, control, fake) = Arrange();
        try
        {
            int left = 0;
            control.MouseLeave += (_, _) => left++;
            SetHover(host, control);
            SetHover(host, null);
            Assert.Equal(1, left);
        }
        finally
        {
            Restore();
            host.Dispose();
            control.Dispose();
        }
    }
}
