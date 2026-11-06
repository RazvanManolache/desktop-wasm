using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;

namespace WfWasm.CanvasHost.Tests;

// RVW3293-WPF4 attribution fixtures (NOT a fix): caret-blink-class marks
// arrive as whole-control null marks (vendor plain Invalidate, rewritten to
// RequestRepaint) and no host code can prove content-identical pre-paint,
// so the vendor full repaint always runs. These pin the exact cost split
// the vendor-lane fix (rect-invalidate or overlay caret) must improve:
// identical content burns repaints but never re-uploads; a 1px caret-class
// change uploads a FULL Def, never a patch.
public sealed class CaretBlinkAttributionTests
{
    private sealed class CountingControl : FormsControl
    {
        public int Paints;
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e) => Paints++;
    }

    private sealed class PixelToggleControl : FormsControl
    {
        public int Paints;
        public bool On;
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            Paints++;
            // 1px caret-class change in an otherwise identical surface.
            e.Graphics.FillRectangle(
                new System.Drawing.SolidBrush(On ? System.Drawing.Color.White : System.Drawing.Color.Black),
                new System.Drawing.Rectangle(2, 2, 1, 1));
        }
    }

    [Fact]
    public void Identical_WholeMarks_Repaint_Without_Reupload()
    {
        // Three consecutive whole-marks with zero content change (the
        // steady-state caret-blink shape when the caret is NOT vendor-
        // painted): every mark re-runs vendor OnPaint, none re-uploads.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var counter = new CountingControl { Left = 4, Top = 100, Width = 40, Height = 40 };
        form.Controls.Add(counter);
        try
        {
            string layer = WinFormsAppHost.LayerIdFor(counter);
            host.Paint(new RecordingRenderer());
            Assert.Equal(1, counter.Paints);
            for (int i = 0; i < 3; i++)
            {
                System.Windows.Forms.WasmRepaintGate.MarkDirty(counter);
                var frame = new RecordingRenderer();
                host.Paint(frame);
                Assert.DoesNotContain(frame.Commands,
                    c => c is DefBitmapCommand d && d.LayerId == layer);
            }
            Assert.Equal(4, counter.Paints);
        }
        finally
        {
            form.Controls.Remove(counter);
            counter.Dispose();
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void OnePixel_RectMark_Uploads_Patch_Never_Def()
    {
        // A 1px caret-class change under a caret-rect mark (the routed
        // RTE blink shape) uploads just the rect as Patch: the region
        // repaint path (`RecordBitmapPatch`) serves rect marks without a
        // full Def. This is the flip the vendor rect-invalidate fix needs.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var toggle = new PixelToggleControl { Left = 60, Top = 100, Width = 40, Height = 40 };
        form.Controls.Add(toggle);
        try
        {
            string layer = WinFormsAppHost.LayerIdFor(toggle);
            host.Paint(new RecordingRenderer());
            Assert.Equal(1, toggle.Paints);

            toggle.On = true;
            System.Windows.Forms.WasmRepaintGate.MarkDirtyRect(toggle, new System.Drawing.Rectangle(0, 0, 20, 20));
            var second = new RecordingRenderer();
            host.Paint(second);
            Assert.Equal(2, toggle.Paints);
            Assert.DoesNotContain(second.Commands, c => c is DefBitmapCommand d && d.LayerId == layer);
            var patch = Assert.Single(second.Commands.OfType<PatchBitmapCommand>(), p => p.LayerId == layer);
            Assert.Equal(0, patch.X);
            Assert.Equal(0, patch.Y);
            Assert.Equal(20, patch.Width);
            Assert.Equal(20, patch.Height);
        }
        finally
        {
            form.Controls.Remove(toggle);
            toggle.Dispose();
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }
}
