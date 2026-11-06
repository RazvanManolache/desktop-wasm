using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsTextBox = System.Windows.Forms.TextBox;

// RVW-9594 Job1: host glyph rendering for native-delegating vendor boxes.
// A Syncfusion numeric box overrides OnPaint for decoration only and leaves
// text glyphs to the native EDIT (served UserPaint=false whenever text is
// present). Headless there is no native EDIT, so the self-painted raster
// carried the watermark/border but never the digits (typed pixels 0 while
// the value committed). The host now draws the display text for exactly
// these boxes (UserPaint false + non-empty text); boxes that paint their
// own text (UserPaint true) stay untouched. Live oracle: gate_job2d/2e
// typed pixels on frozen bytes; these tests pin the routing contract
// in-process with vendor-shaped doubles.
public sealed class SfNumericTextBoxHostGlyphProbe : FormsTextBox
{
    public SfNumericTextBoxHostGlyphProbe(bool userPaint)
    {
        SetStyle(System.Windows.Forms.ControlStyles.UserPaint, userPaint);
    }

    // Vendor-like: decoration only, never text glyphs.
    protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
    {
        base.OnPaint(e);
    }
}

public sealed class VendorTextGlyphPaintTests
{
    private static List<DrawTextCommand> PaintBox(SfNumericTextBoxHostGlyphProbe box)
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var form = new System.Windows.Forms.Form();
        box.Font = new Font("Segoe UI", 9f);
        box.Bounds = new Rectangle(10, 20, 200, 24);
        form.Controls.Add(box);
        form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        form.Left = 0;
        form.Top = 0;
        form.Show();
        try
        {
            var recorder = new RecordingRenderer();
            recorder.BeginFrame(400, 400);
            MethodInfo render = typeof(WinFormsAppHost).GetMethod(
                "RenderControls",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.NotNull(render);
            render.Invoke(host, [recorder, form.Controls, 0, 0]);
            recorder.EndFrame();
            var draws = new List<DrawTextCommand>();
            foreach (DrawCommand cmd in recorder.Commands)
                if (cmd is DrawTextCommand t)
                    draws.Add(t);
            return draws;
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(form);
        }
    }

    [Fact]
    public void NativeDelegatingBox_Draws_Display_Text()
    {
        // Red without the host-glyph block: the vendor-like OnPaint draws
        // no text and the generic dispatch drew nothing either (served
        // empty-white boxes with committed values).
        using var box = new SfNumericTextBoxHostGlyphProbe(userPaint: false)
        {
            Text = "27",
        };
        var draws = PaintBox(box);
        var runs = draws.FindAll(d => d.Text == "27");
        Assert.Single(runs);
        Assert.Equal(TextHAlign.Left, runs[0].HAlign);
        Assert.Equal(TextVAlign.Middle, runs[0].VAlign);
    }

    [Fact]
    public void SelfPaintingBox_Draws_No_Host_Text()
    {
        // UserPaint true = the box paints its own text (served watermark /
        // disabled states): the host must not double-draw.
        using var box = new SfNumericTextBoxHostGlyphProbe(userPaint: true)
        {
            Text = "27",
        };
        var draws = PaintBox(box);
        Assert.DoesNotContain(draws, d => d.Text == "27");
    }

    [Fact]
    public void EmptyBox_Draws_No_Host_Text()
    {
        // Empty text = vendor watermark state: nothing to draw.
        using var box = new SfNumericTextBoxHostGlyphProbe(userPaint: false)
        {
            Text = "",
        };
        var draws = PaintBox(box);
        Assert.Empty(draws);
    }
}
