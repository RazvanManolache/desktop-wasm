using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsForm = System.Windows.Forms.Form;

// PAINT-COLORS-COLLAPSE (2026-11-02): the Colors floating window shrinks on
// "Less" but its advanced children (Visible=true, positioned outside the
// shrunk client area) kept painting past the window frame onto the
// document. Native relies on client-area clipping, not on hiding every
// child. The Host renderer must skip children fully outside the ancestor
// client rectangle (generically, not a Paint-only blacklist).
// NOTE: plain Buttons stand in for the Colors children because they paint
// deterministically through the flat renderer on a direct RenderControls
// call (same pattern as ButtonPaintRoutingTests); the clip check lives at
// the loop head and is branch-independent.
public sealed class ClientClipPaintTests
{
    private static (WinFormsAppHost host, FormsForm form) NarrowDialog()
    {
        var host = new WinFormsAppHost();
        var form = new FormsForm { Text = "Clip", Width = 209, Height = 217, Visible = true, FormBorderStyle = System.Windows.Forms.FormBorderStyle.None };
        var inside = new FormsButton { Name = "inBtn", Text = "IN_CLIP", Left = 10, Top = 40, Width = 100, Height = 20 };
        var outside = new FormsButton { Name = "outBtn", Text = "OUT_CLIP", Left = 222, Top = 40, Width = 100, Height = 20 };
        form.Controls.Add(inside);
        form.Controls.Add(outside);
        return (host, form);
    }

    [Fact]
    public void CollapsedWindow_Skips_Children_Fully_Outside_Client_Rect()
    {
        var (host, form) = NarrowDialog();
        using (host)
        using (form)
        {
            Assert.Equal(2, form.Controls.Count);
            var recorder = new RecordingRenderer();
            // Client origin (50,60), client size 209x217: the 222-offset
            // child is fully outside, like Less-collapse.
            host.RenderControlsClipped(recorder, form.Controls, 50, 60,
                new System.Drawing.Rectangle(50, 60, 209, 217));
            Assert.Contains(recorder.Commands, c =>
                c is DrawTextCommand t && t.Text == "IN_CLIP");
            Assert.DoesNotContain(recorder.Commands, c =>
                c is DrawTextCommand t && t.Text == "OUT_CLIP");
        }
    }

    [Fact]
    public void CollapsedWindow_Keeps_Partially_Overlapping_Child()
    {
        // Bounded behavior contract: only fully-outside children are
        // skipped (ICanvas has no clip primitive, so partial overlap keeps
        // today's full paint). Documents the boundary so a future canvas
        // clip can tighten it without changing this test's name.
        using var host = new WinFormsAppHost();
        using var form = new FormsForm { Text = "ClipEdge", Width = 209, Height = 217, Visible = true, FormBorderStyle = System.Windows.Forms.FormBorderStyle.None };
        var edge = new FormsButton { Name = "edgeBtn", Text = "EDGE_CLIP", Left = 200, Top = 40, Width = 100, Height = 20 };
        form.Controls.Add(edge);
        var recorder = new RecordingRenderer();
        host.RenderControlsClipped(recorder, form.Controls, 50, 60,
            new System.Drawing.Rectangle(50, 60, 209, 217));
        Assert.Contains(recorder.Commands, c =>
            c is DrawTextCommand t && t.Text == "EDGE_CLIP");
    }
}
