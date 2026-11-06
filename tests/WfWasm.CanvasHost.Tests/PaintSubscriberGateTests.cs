// WC1746: event-painted controls (e.g. Syncfusion ProgressBarAdv, which has
// no OnPaint override and instead subscribes its private OnBarPaint to its
// own Paint event in InitializeComponent) must be admitted to self-paint.
// TypeSelfPaints is blind to them; without this the host renders the
// BackColor+Text fallback forever even at Value=100.
using WfWasm.CanvasHost;
using WfWasm.Shim;

public sealed class PaintSubscriberGateTests
{
    [Fact]
    public void Unsubscribed_Control_Has_No_Paint_Subscribers()
    {
        using var c = new System.Windows.Forms.Control();
        Assert.False(WinFormsAppHost.ControlHasPaintSubscribers(c));
    }

    [Fact]
    public void Paint_Subscribed_Control_Is_Detected()
    {
        using var c = new System.Windows.Forms.Control();
        System.Windows.Forms.PaintEventHandler h = static (_, _) => { };
        c.Paint += h;
        try
        {
            Assert.True(WinFormsAppHost.ControlHasPaintSubscribers(c));
        }
        finally
        {
            c.Paint -= h;
        }
    }

    [Fact]
    public void Event_Painted_Subclass_Fails_Type_Gate_But_Passes_Instance_Gate()
    {
        using var c = new EventPaintedControl();
        // No OnPaint/OnPaintBackground override: the type-level gate is blind.
        Assert.False(WinFormsAppHost.TypeSelfPaints(c.GetType()));
        // ...but the instance carries a Paint subscriber, so the host must
        // admit it to self-paint instead of the gray fallback.
        Assert.True(WinFormsAppHost.ControlHasPaintSubscribers(c));
    }

    private sealed class EventPaintedControl : System.Windows.Forms.Control
    {
        public EventPaintedControl()
        {
            Paint += static (_, _) => { };
        }
    }

    // WC1955: a redeclared (shadowed) Paint event carries its own key; the
    // subscription lands on it, not on the base key. A base-only lookup
    // misses it and falls back forever.
    private sealed class ShadowedPaintControl : System.Windows.Forms.Control
    {
        private static new readonly object s_paintEvent = new();
        public new event System.Windows.Forms.PaintEventHandler Paint
        {
            add => Events.AddHandler(s_paintEvent, value);
            remove => Events.RemoveHandler(s_paintEvent, value);
        }
        public ShadowedPaintControl()
        {
            Paint += static (_, _) => { };
        }
    }

    // WC1955: fork revisions that renamed the key (EventPaint) must still
    // admit a subscriber wired through that key.
    private sealed class RenamedKeyControl : System.Windows.Forms.Control
    {
        private static readonly object EventPaint = new();
        public event System.Windows.Forms.PaintEventHandler CustomPaint
        {
            add => Events.AddHandler(EventPaint, value);
            remove => Events.RemoveHandler(EventPaint, value);
        }
        public RenamedKeyControl()
        {
            CustomPaint += static (_, _) => { };
        }
    }

    [Fact]
    public void Shadowed_Paint_Key_Is_Detected()
    {
        using var c = new ShadowedPaintControl();
        Assert.False(WinFormsAppHost.TypeSelfPaints(c.GetType()));
        Assert.True(WinFormsAppHost.ControlHasPaintSubscribers(c));
    }

    [Fact]
    public void Renamed_EventPaint_Key_Is_Detected()
    {
        using var c = new RenamedKeyControl();
        Assert.True(WinFormsAppHost.ControlHasPaintSubscribers(c));
    }

}
