// WC1348(1): [COMBO-POPUP] once-per-open log. The fork combo overlay
// (WinFormsAppHost overlay paint) is log-silent, so a served open list
// is indistinguishable from a missing repaint. This pin drives the REAL
// open path (arrow click via HandleForkMouseDown) and asserts the open
// geometry log plus the list rect/row commands. FAILS until the log
// lands; do not weaken.
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;

public sealed class ComboPopupLogTests
{
    [Fact]
    public void OpenCombo_Emits_Popup_Geometry_Log_And_List_Commands()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        using var combo = new System.Windows.Forms.ComboBox
        {
            Name = "probeCombo",
            Location = new System.Drawing.Point(30, 200),
            Size = new System.Drawing.Size(195, 24),
            DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDown,
            Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f),
        };
        combo.Items.Add("Column");
        combo.Items.Add("Bar");
        combo.Items.Add("Line");
        form.Controls.Add(combo);

        // Real open path: arrow click, like production (drop=True served).
        bool consumed = ComboBoxBrowserHook.HandleForkMouseDown(combo, combo.Width - 5, 5);
        Assert.True(consumed);
        Assert.True(combo.DroppedDown);

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
            form.Controls.Remove(combo);
        }

        string log = sb.ToString();
        Assert.Contains("[COMBO-POPUP]", log);
        Assert.Contains("probeCombo", log);

        bool listRect = false;
        foreach (var c in recorder.Commands)
            if (c is FillRectCommand fr && fr.Rgba == 0xFFFFFFFFu && fr.Height >= 40 && fr.Width == 195)
                listRect = true;
        Assert.True(listRect);

        var rows = new System.Collections.Generic.List<string>();
        foreach (var c in recorder.Commands)
            if (c is DrawTextCommand dt && (dt.Text == "Column" || dt.Text == "Bar" || dt.Text == "Line"))
                rows.Add(dt.Text);
        Assert.Equal(3, rows.Count);
    }
}
