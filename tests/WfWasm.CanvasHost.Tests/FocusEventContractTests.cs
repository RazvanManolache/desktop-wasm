using System.Reflection;
using WfWasm.CanvasHost;
using FormsControl = System.Windows.Forms.Control;

namespace WfWasm.CanvasHost.Tests;

// RVW3293-WPF5: headless has no WM_SETFOCUS loop, so the host focus router
// must drive the desktop focus-event contract itself: OnLostFocus on the
// previous focused control, then OnGotFocus on the new one. Without this,
// vendor redirect overrides (outer SfDateTimeEdit OnGotFocus ->
// inner.Focus()) never run and keystrokes land on the handler-less outer.
public sealed class FocusEventContractTests
{
    private sealed class EventLog
    {
        public readonly List<string> Calls = new();
    }

    private sealed class VendorOuter : FormsControl
    {
        public readonly EventLog Log;
        public Action? Redirect;
        public VendorOuter(EventLog log) => Log = log;
        protected override void OnGotFocus(System.EventArgs e)
        {
            Log.Calls.Add("outer.Got");
            base.OnGotFocus(e);
            Redirect?.Invoke(); // stands in for inner.Focus()
        }
        protected override void OnLostFocus(System.EventArgs e)
        {
            Log.Calls.Add("outer.Lost");
            base.OnLostFocus(e);
        }
    }

    private sealed class VendorInner : FormsControl
    {
        public readonly EventLog Log;
        public VendorInner(EventLog log) => Log = log;
        protected override void OnGotFocus(System.EventArgs e)
        {
            Log.Calls.Add("inner.Got");
            base.OnGotFocus(e);
        }
        protected override void OnLostFocus(System.EventArgs e)
        {
            Log.Calls.Add("inner.Lost");
            base.OnLostFocus(e);
        }
    }

    private sealed class StockControl : FormsControl
    {
    }

    private static void NoteFocus(WinFormsAppHost host, FormsControl? control)
    {
        typeof(WinFormsAppHost).GetMethod("NoteFocus",
            BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(host, new object?[] { control });
    }

    [Fact]
    public void Transfer_Fires_Lost_Then_Got_In_Order()
    {
        var log = new EventLog();
        using var host = new WinFormsAppHost();
        using var outer = new VendorOuter(log);
        using var inner = new VendorInner(log);
        NoteFocus(host, outer);
        Assert.Equal(new[] { "outer.Got" }, log.Calls);
        log.Calls.Clear();
        NoteFocus(host, inner);
        Assert.Equal(new[] { "outer.Lost", "inner.Got" }, log.Calls);
    }

    [Fact]
    public void Same_Control_Notes_Nothing()
    {
        var log = new EventLog();
        using var host = new WinFormsAppHost();
        using var outer = new VendorOuter(log);
        NoteFocus(host, outer);
        log.Calls.Clear();
        NoteFocus(host, outer);
        Assert.Empty(log.Calls);
    }

    [Fact]
    public void Null_Transfer_Fires_Lost_Only()
    {
        var log = new EventLog();
        using var host = new WinFormsAppHost();
        using var outer = new VendorOuter(log);
        NoteFocus(host, outer);
        log.Calls.Clear();
        NoteFocus(host, null);
        Assert.Equal(new[] { "outer.Lost" }, log.Calls);
    }

    [Fact]
    public void Vendor_Redirect_Runs_Synchronously_Inside_NoteFocus()
    {
        // The diagnosed break: outer.OnGotFocus -> inner.Focus() never ran,
        // so keystrokes landed on the handler-less outer. The host fix must
        // reach the redirect call site during NoteFocus.
        var log = new EventLog();
        bool redirected = false;
        using var host = new WinFormsAppHost();
        using var outer = new VendorOuter(log) { Redirect = () => redirected = true };
        NoteFocus(host, outer);
        Assert.True(redirected);
    }

    [Fact]
    public void Stock_Control_Fires_No_Vendor_Events()
    {
        // Deliberate scoping (mirrors TryArmVendorEditMode): only nearest
        // vendor overrides are invoked; stock controls keep today's behavior.
        var log = new EventLog();
        using var host = new WinFormsAppHost();
        using var stock = new StockControl();
        using var outer = new VendorOuter(log);
        NoteFocus(host, stock);
        NoteFocus(host, outer);
        Assert.Equal(new[] { "outer.Got" }, log.Calls);
    }
}
