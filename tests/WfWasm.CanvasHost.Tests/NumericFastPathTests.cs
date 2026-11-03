// WC1036 triage: SF06 trigger never fires served-side (shell48, old+new
// gens identical). Root cause from source (no vendor execution):
// RenderControlsClipped paints fork-TextBox subclasses via the canvas
// TextBox fast path (WinFormsAppHost.cs:4907) WITHOUT invoking OnPaint,
// so SfNumericTextBox.OnPaint (watermark) + LogFirstPaint never run — the
// boxes paint as bordered empties, no [WM]/[INK] ever. SfComboBox/SfButton
// first-paint because they match NO fast path and fall to
// TryRenderSelfPainted. The trigger cannot fire without a routing change
// (exempt SfNumericTextBox-likes from the fast path); no harness can force
// it. Stand-in names mirror the served types so a name-gated fix flips the
// failing pin; fork TextBox fast-path perf behavior is deliberately kept.
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsTextBox = System.Windows.Forms.TextBox;
using FormsPaintEventArgs = System.Windows.Forms.PaintEventArgs;
using FormsPoint = System.Drawing.Point;
using FormsSize = System.Drawing.Size;

public sealed class NumericFastPathTests
{
    private sealed class PlainPainter : FormsControl
    {
        public bool Painted;
        protected override void OnPaint(FormsPaintEventArgs e) => Painted = true;
    }

    private sealed class PlainTextBox : FormsTextBox
    {
        public bool Painted;
        protected override void OnPaint(FormsPaintEventArgs e) => Painted = true;
    }

    // Type name must contain "SfNumericTextBox" (the served vendor type).
    private sealed class SfNumericTextBoxStandin : FormsTextBox
    {
        public bool Painted;
        protected override void OnPaint(FormsPaintEventArgs e) => Painted = true;
    }

    private static (WinFormsAppHost host, T control) Mount<T>(T control) where T : FormsControl
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        control.Location = new FormsPoint(4, 100);
        control.Size = new FormsSize(200, 24);
        form.Controls.Add(control);
        return (host, control);
    }

    [Fact]
    public void Plain_Control_OnPaint_Dispatched_Through_Pump()
    {
        // Control case: unmatched types reach TryRenderSelfPainted and run
        // OnPaint (this is how SfComboBox/SfButton first-paint served-side).
        var (host, control) = Mount(new PlainPainter());
        try
        {
            host.Paint(new RecordingRenderer());
            Assert.True(control.Painted, "plain control OnPaint must run through the pump");
        }
        finally
        {
            host.Dispose();
        }
    }

    [Fact]
    public void TextBox_Subclass_OnPaint_Skipped_By_Fast_Path()
    {
        // Fork TextBox subclasses take the canvas fast path: they paint
        // (recorder gets the box fill) but OnPaint never runs. This is the
        // perf fast path — intentionally kept for fork TextBoxes.
        var (host, control) = Mount(new PlainTextBox());
        try
        {
            var recorder = new RecordingRenderer();
            host.Paint(recorder);
            Assert.True(recorder.Commands.Count > 0, "fast path must still paint the box");
            Assert.False(control.Painted, "TextBox fast path must not invoke OnPaint");
        }
        finally
        {
            host.Dispose();
        }
    }

    [Fact]
    public void SfNumericTextBox_Shape_Routes_To_Dispatch()
    {
        // TRIAGE PIN, FAILS until SfNumericTextBox-likes are exempted from
        // the TextBox fast path (WinFormsAppHost.cs:4907) and routed to
        // TryRenderSelfPainted like every other custom control. Served
        // SfNumericTextBox never first-paints on any generation because of
        // this branch — no harness can force the trigger without the fix.
        // Do not weaken this test; fix the routing.
        var (host, control) = Mount(new SfNumericTextBoxStandin());
        try
        {
            host.Paint(new RecordingRenderer());
            Assert.True(control.Painted, "SfNumericTextBox-shaped control must reach OnPaint dispatch");
        }
        finally
        {
            host.Dispose();
        }
    }
}
