// WC1241: host Label-branch rect census. The host paints Label text
// itself (RenderControlsClipped Label branch) via canvas DrawTextAligned,
// bypassing every DrawString/TextRenderer/GDI census (FP1235). This pin
// drives Welcome-text Labels in both boxes (frozen 316x56, served 344x45)
// through the real pump and asserts the [LABEL-RECT] emission (rect +
// fontCss). FAILS until the hunk lands; do not weaken.
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsLabel = System.Windows.Forms.Label;

public sealed class LabelRectCensusTests
{
    private const string Welcome =
        "The Wizard control allows developers to make wizard applications " +
        "quickly offering easy control over pages and wizard buttons";

    private static FormsLabel WelcomeLabel(int x, int y, int w, int h) =>
        new FormsLabel
        {
            AutoSize = false,
            Text = Welcome,
            Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f),
            Location = new System.Drawing.Point(x, y),
            Size = new System.Drawing.Size(w, h),
        };

    [Fact]
    public void LabelBranch_Emits_Rect_And_Font_For_Both_Boxes()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        using var frozen = WelcomeLabel(30, 180, 316, 56);
        using var served = WelcomeLabel(30, 250, 344, 45);
        form.Controls.Add(frozen);
        form.Controls.Add(served);

        var sb = new System.Text.StringBuilder();
        var old = System.Console.Out;
        System.Console.SetOut(new System.IO.StringWriter(sb));
        var recorder = new RecordingRenderer();
        try
        {
            host.Paint(recorder);
        }
        finally
        {
            System.Console.SetOut(old);
            form.Controls.Remove(frozen);
            form.Controls.Remove(served);
        }

        string log = sb.ToString();
        Assert.Contains("[LABEL-RECT]", log);
        Assert.Contains("The Wizard control allow", log);
        Assert.Contains("316x56", log);
        Assert.Contains("344x45", log);
        Assert.Contains("Microsoft Sans Serif", log);
    }
}
