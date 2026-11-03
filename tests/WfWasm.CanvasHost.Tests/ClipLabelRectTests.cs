// WC1307: host Label-branch clip. The branch paints Label text via canvas
// DrawTextAligned with no bounds, so a 724px line inks past its 344 box
// and past the form (WC1256). This pin drives Welcome-text Labels in both
// boxes (316x56, 344x45) through the real pump and asserts the recorded
// text commands carry clip bounds == the label rects. FAILS until the
// branch passes its rect; other callers stay unbounded (defaults).
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsLabel = System.Windows.Forms.Label;

public sealed class ClipLabelRectTests
{
    private const string Welcome =
        "The Wizard control allows developers to make wizard applications " +
        "quickly offering easy control over pages and wizard buttons";

    // NOTE: X=60 origins keep the once-only [LABEL-RECT] log shapes disjoint
    // from LabelRectCensusTests (X=30): the log dedups per text+rect
    // process-wide, so sharing rects would race under parallel runners.
    // Clip assertions use WxH + anchor math, never absolute positions.
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
    public void LabelBranch_Clips_Text_To_Label_Rect()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        using var frozen = WelcomeLabel(60, 180, 316, 56);
        using var served = WelcomeLabel(60, 250, 344, 45);
        form.Controls.Add(frozen);
        form.Controls.Add(served);

        var recorder = new RecordingRenderer();
        try
        {
            host.Paint(recorder);
        }
        finally
        {
            form.Controls.Remove(frozen);
            form.Controls.Remove(served);
        }

        var cmds = new System.Collections.Generic.List<DrawTextCommand>();
        foreach (var c in recorder.Commands)
            if (c is DrawTextCommand dt && dt.Text == Welcome)
                cmds.Add(dt);
        Assert.Equal(2, cmds.Count);
        var byWidth = new System.Collections.Generic.Dictionary<int, DrawTextCommand>();
        foreach (var c in cmds)
            byWidth[(int)c.ClipWidth] = c;
        Assert.True(byWidth.ContainsKey(316), "316 box must carry clip bounds");
        Assert.True(byWidth.ContainsKey(344), "344 box must carry clip bounds");
        foreach (var c in cmds)
        {
            Assert.True(c.ClipWidth > 0 && c.ClipHeight > 0);
            Assert.Equal(c.X - 2, c.ClipX);
            Assert.InRange(c.Y, c.ClipY, c.ClipY + c.ClipHeight);
            Assert.Equal(c.Text, Welcome);
        }
        Assert.Equal(56, (int)byWidth[316].ClipHeight);
        Assert.Equal(45, (int)byWidth[344].ClipHeight);
    }
}
