// RVW-5795 Job1 containment pin: the SF06 branch-(d) analysis rests on the
// paint pump containing per-control OnPaint throws (PaintRegionPixels catch,
// TryRenderSelfPainted catch + one-time log + interop-only poisoning).
// This test drives the real public pump (host.Paint) against a control
// whose OnPaint always throws and asserts: (1) nothing escapes, (2) the
// walk still reaches the throwing control (BackColor fallback fill at its
// bounds proves it was attempted, not skipped), (3) the pump keeps
// producing output past the failure. Durable regardless of the pending
// grant fix (logging-only): any refactor that lets paint throws kill the
// pump or silently drop the walk fails here.
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsPaintEventArgs = System.Windows.Forms.PaintEventArgs;

public sealed class ThrowingSelfPaintTests
{
    private sealed class BoomControl : FormsControl
    {
        protected override void OnPaint(FormsPaintEventArgs e) =>
            throw new InvalidOperationException("boom");
    }

    [Fact]
    public void Throwing_OnPaint_Is_Contained_And_Walk_Continues()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        using var boom = new BoomControl
        {
            Location = new System.Drawing.Point(4, 4),
            Size = new System.Drawing.Size(80, 28),
            BackColor = System.Drawing.Color.Red,
        };
        form.Controls.Add(boom);
        int absX = form.Location.X + 4;
        int absY = form.Location.Y + 30 + 4;

        var recorder = new RecordingRenderer();
        var ex = Record.Exception(() => host.Paint(recorder));
        Assert.Null(ex);

        Assert.Contains(recorder.Commands, c =>
            c is FillRectCommand f &&
            f.X == absX && f.Y == absY &&
            f.Width == 80 && f.Height == 28);
        Assert.True(recorder.Commands.Count > 1, "pump must keep painting past the throwing control");
    }
}
