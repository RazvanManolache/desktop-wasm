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
        // Hermetic against suite order: earlier tests may have exhausted
        // the process-wide 64-shape census cap; reset first so this pin
        // observes its own paint, not the suite's history.
        ResetLabelRectLog();
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

    // WC2049: the census log dedupes process-wide with a 64-shape cap, so a
    // full parallel run can exhaust the cap before this test paints and the
    // asserts below go quiet (order-flake). The census resets the table
    // first; these pins document the mechanism and the reset contract.
    internal static void ResetLabelRectLog()
    {
        var field = typeof(WinFormsAppHost).GetField("s_labelRectLogged",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var table = (System.Collections.Concurrent.ConcurrentDictionary<string, byte>)field!.GetValue(null)!;
        table.Clear();
    }

    internal static void ExhaustLabelRectLog()
    {
        var field = typeof(WinFormsAppHost).GetField("s_labelRectLogged",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var table = (System.Collections.Concurrent.ConcurrentDictionary<string, byte>)field!.GetValue(null)!;
        for (int i = 0; i < 70; i++)
            table.TryAdd("pin|exhaust|" + i, 0);
    }

    [Fact]
    public void Exhausted_Cap_Suppresses_Label_Lines()
    {
        // Mechanism doc: with the cap exhausted, fresh labels stay silent.
        // This is intended served-side log bounding, not a product defect.
        ExhaustLabelRectLog();
        try
        {
            using var host = new WinFormsAppHost();
            host.SelectScene(AppScene.Smoke);
            var form = host.GetOrCreateForm(AppScene.Smoke);
            Assert.NotNull(form);
            using var probe = WelcomeLabel(30, 400, 200, 40);
            form.Controls.Add(probe);
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
                form.Controls.Remove(probe);
            }
            Assert.DoesNotContain("[LABEL-RECT]", sb.ToString());
        }
        finally
        {
            ResetLabelRectLog();
        }
    }

    [Fact]
    public void Census_Passes_After_Reset_Under_Exhausted_Cap()
    {
        // Regression pin for the hermetic reset: even with the cap
        // exhausted by earlier tests, resetting first restores the lines.
        ExhaustLabelRectLog();
        ResetLabelRectLog();
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
        Assert.Contains("316x56", log);
        Assert.Contains("344x45", log);
    }
}
