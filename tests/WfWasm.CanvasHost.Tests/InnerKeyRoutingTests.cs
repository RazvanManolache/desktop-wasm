using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsKeys = System.Windows.Forms.Keys;

namespace WfWasm.CanvasHost.Tests;

// RVW3293-WPF9: host key dispatch follows _focusedControl (the outer), but
// vendor composite editors (SfDateTimeEdit) own keys on the inner editor
// (EditingTextBox) reached via the vendor's own redirect property. Keys
// starve at the handler-less outer. When the focused control exposes a
// mapped inner editor, Down/Press (+Enter) must go inward; plain controls
// keep today's routing byte-identical.
public sealed class InnerKeyRoutingTests
{
    // Models the vendor outer: exposes the redirect property the real
    // OnGotFocus override uses (get_EditingTextBox -> inner.Focus()).
    private sealed class OuterLike : FormsControl
    {
        public FormsControl? EditingTextBox { get; set; }
    }

    private static (int X, int Y) CenterOn(FormsForm form, FormsControl control)
    {
        int absX = form.Location.X;
        int absY = form.Location.Y + 30;
        for (FormsControl? current = control;
            current is not null && current is not FormsForm;
            current = current.Parent)
        {
            absX += current.Left;
            absY += current.Top;
        }
        return (absX + (control.Width / 2), absY + (control.Height / 2));
    }

    private static WinFormsAppHost FocusOuter(out OuterLike outer, out FormsControl inner,
        out List<string> outerCalls, out List<string> innerCalls)
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var o = new OuterLike();
        o.SetBounds(4, 60, 200, 30);
        var n = new FormsControl();
        // Served inner is hidden until the vendor OnMouseDown shows it;
        // dispatch must not gate on Visible (desktop delivers to focus).
        n.Visible = false;
        o.EditingTextBox = n;
        form.Controls.Add(o);
        var oc = new List<string>();
        var nc = new List<string>();
        o.KeyDown += (_, e) => oc.Add("down:" + e.KeyCode);
        o.KeyPress += (_, e) => oc.Add("press:" + e.KeyChar);
        n.KeyDown += (_, e) => nc.Add("down:" + e.KeyCode);
        n.KeyPress += (_, e) => nc.Add("press:" + e.KeyChar);
        var (x, y) = CenterOn(form, o);
        host.OnMouseDown(x, y);
        outer = o;
        inner = n;
        outerCalls = oc;
        innerCalls = nc;
        return host;
    }

    [Fact]
    public void CharKey_Routes_Down_And_Press_To_Inner_Editor()
    {
        using WinFormsAppHost host = FocusOuter(out OuterLike outer, out FormsControl inner,
            out List<string> outerCalls, out List<string> innerCalls);
        try
        {
            host.OnKeyDown("a");
            Assert.Equal(new[] { "down:A", "press:a" }, innerCalls);
            Assert.Empty(outerCalls);
        }
        finally
        {
            outer.Parent?.Controls.Remove(outer);
            outer.Dispose();
            inner.Dispose();
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void Enter_Routes_Down_To_Inner_Editor()
    {
        using WinFormsAppHost host = FocusOuter(out OuterLike outer, out FormsControl inner,
            out List<string> outerCalls, out List<string> innerCalls);
        try
        {
            host.OnKeyDown("Enter");
            Assert.Equal(new[] { "down:Enter" }, innerCalls);
            // Preserved (not new): the Return leg still fires outer
            // SUBSCRIBERS directly via FireOuterKeyDown; only the override
            // raise moved inward. Served outers have zero key subscribers.
            Assert.Equal(new[] { "down:Enter" }, outerCalls);
        }
        finally
        {
            outer.Parent?.Controls.Remove(outer);
            outer.Dispose();
            inner.Dispose();
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void Plain_Control_Keeps_Direct_Routing()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var outer = new OuterLike();
        outer.SetBounds(4, 100, 200, 30);
        outer.EditingTextBox = null; // no mapped inner: direct routing
        form.Controls.Add(outer);
        try
        {
            var calls = new List<string>();
            outer.KeyDown += (_, e) => calls.Add("down:" + e.KeyCode);
            outer.KeyPress += (_, e) => calls.Add("press:" + e.KeyChar);
            var (x, y) = CenterOn(form, outer);
            host.OnMouseDown(x, y);
            host.OnKeyDown("a");
            Assert.Equal(new[] { "down:A", "press:a" }, calls);
        }
        finally
        {
            form.Controls.Remove(outer);
            outer.Dispose();
        }
    }
}
