// CREDIT1300-WPF4: wizard-title overflow parity. The served title
// (label5 376x52, single line, over-wide) is cropped mid-glyph by the
// host Label-branch rect clip (served rel 654 == command clip at the
// rect right edge; A/B pixels in reports/CREDIT1300-dw-wpf.md S3).
// Gen3 painted it unclipped. One-line labels (rect fits a single text
// line) must therefore carry NO clip; taller labels keep the WC1307
// rect clip (WC1256: no inking past the form). FAILS until the branch
// relaxes the one-line path; the body pin below guards blanket-removal.
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsLabel = System.Windows.Forms.Label;

public sealed class TitleOverflowParityTests
{
    private const string Title = "Welcome to the Wizard demo application";

    private const string Body =
        "The Wizard control allows developers to make wizard applications " +
        "quickly offering easy control over pages and wizard buttons";

    // NOTE: X=120 origins keep the once-only [LABEL-RECT] log shapes
    // disjoint from LabelRectCensusTests (X=30) and ClipLabelRectTests
    // (X=60): the log dedups per shape process-wide, so sharing rects
    // would race under parallel runners.
    private static FormsLabel TitleLabel() =>
        new FormsLabel
        {
            AutoSize = false,
            Text = Title,
            Font = new System.Drawing.Font("Verdana", 24f, System.Drawing.FontStyle.Bold),
            Location = new System.Drawing.Point(120, 40),
            Size = new System.Drawing.Size(376, 52),
        };

    private static FormsLabel BodyLabel() =>
        new FormsLabel
        {
            AutoSize = false,
            Text = Body,
            Font = new System.Drawing.Font("Arial", 10f),
            Location = new System.Drawing.Point(120, 140),
            Size = new System.Drawing.Size(344, 45),
        };

    [Fact]
    public void TitleLabel_Paints_Unclipped_BodyLabel_Stays_Clipped()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        using var title = TitleLabel();
        using var body = BodyLabel();
        form.Controls.Add(title);
        form.Controls.Add(body);

        var recorder = new RecordingRenderer();
        try
        {
            host.Paint(recorder);
        }
        finally
        {
            form.Controls.Remove(title);
            form.Controls.Remove(body);
        }

        DrawTextCommand? titleCmd = null;
        DrawTextCommand? bodyCmd = null;
        foreach (var c in recorder.Commands)
        {
            if (c is DrawTextCommand dt && dt.Text == Title)
                titleCmd = dt;
            if (c is DrawTextCommand dt2 && dt2.Text == Body)
                bodyCmd = dt2;
        }
        Assert.NotNull(titleCmd);
        Assert.NotNull(bodyCmd);
        // Gen3 parity: the one-line over-wide title runs unclipped.
        Assert.Equal(0, titleCmd!.ClipWidth);
        Assert.Equal(0, titleCmd!.ClipHeight);
        // WC1307 intact: the taller body label keeps its rect clip.
        Assert.Equal(344, (int)bodyCmd!.ClipWidth);
        Assert.Equal(45, (int)bodyCmd!.ClipHeight);
    }
}
