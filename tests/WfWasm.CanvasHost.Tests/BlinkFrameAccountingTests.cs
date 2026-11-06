using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;

namespace WfWasm.CanvasHost.Tests;

// CREDIT1300-WPF: blink-frame upload/blit accounting lock. A served
// drawImage hook counts every canvas draw — including full-layer blits —
// so per-frame blits of large clean layers read as "full Def" uploads.
// This pins the command-level truth for a real rect+other-marks frame:
// one rect-marked control plus one clean control emit exactly one Patch,
// full-size Blits, and ZERO Defs. Green-first accounting pin (the region
// mechanism pre-exists); its job is to keep future changes from turning
// blits into uploads or patches into Defs.
public sealed class BlinkFrameAccountingTests
{
    private sealed class CountingControl : FormsControl
    {
        public int Paints;
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e) => Paints++;
    }

    [Fact]
    public void RectFrame_Emits_One_Patch_Plus_Blits_Zero_Defs()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var edited = new CountingControl { Left = 4, Top = 200, Width = 60, Height = 40 };
        var clean = new CountingControl { Left = 120, Top = 200, Width = 60, Height = 40 };
        form.Controls.Add(edited);
        form.Controls.Add(clean);
        try
        {
            string editedLayer = WinFormsAppHost.LayerIdFor(edited);
            string cleanLayer = WinFormsAppHost.LayerIdFor(clean);
            host.Paint(new RecordingRenderer()); // prime: both Def+Blit
            Assert.Equal(1, edited.Paints);
            Assert.Equal(1, clean.Paints);

            // Caret-blink shape: small rect on one control, rest clean.
            System.Windows.Forms.WasmRepaintGate.MarkDirtyRect(
                edited, new System.Drawing.Rectangle(2, 2, 8, 8));
            var frame = new RecordingRenderer();
            host.Paint(frame);

            // Exactly one patch upload (the rect), for the edited layer.
            var patches = frame.Commands.OfType<PatchBitmapCommand>().ToList();
            Assert.Single(patches);
            Assert.Equal(editedLayer, patches[0].LayerId);

            // Full-size blits cover every live layer, patched or clean.
            Assert.Contains(frame.Commands,
                c => c is BlitBitmapCommand b && b.LayerId == editedLayer);
            Assert.Contains(frame.Commands,
                c => c is BlitBitmapCommand b && b.LayerId == cleanLayer);

            // No full re-upload anywhere in the frame.
            Assert.DoesNotContain(frame.Commands, c => c is DefBitmapCommand);

            // Vendor repaint ran once (the rect), clean control untouched.
            Assert.Equal(2, edited.Paints);
            Assert.Equal(1, clean.Paints);
        }
        finally
        {
            form.Controls.Remove(edited);
            form.Controls.Remove(clean);
            edited.Dispose();
            clean.Dispose();
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }
}
