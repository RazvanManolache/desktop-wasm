// CREDIT1300-WPF5: masked-field hit contract lock (green-first, honest).
// Served VFY5: click at the true field box center -> host hit=null while
// the 11-05 [Scene] census shows 8 VISIBLE+ENABLED+sized MaskedEditBox
// children (209x19, Children=0). This pins HitTestDeep on that tree
// SHAPE (fork TextBox proxies the editor: no fork MaskedEditBox type
// exists in-process): field centers must route focus; a gap point keeps
// focus where it was (WPF8 hit-null preserved, desktop focus retention).
// Passes pre-fix:
// it locks the filters-innocent verdict, not a behavior change. The w29
// fork-bounds-vs-click mapping needs served kids=/NULLHIT lines (named
// request in reports/CREDIT1300-dw-wpf.md S5); no source edit here.
using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsForm = System.Windows.Forms.Form;
using FormsTextBox = System.Windows.Forms.TextBox;

public sealed class MaskedFieldHitContractTests
{
    [Fact]
    public void FieldCenters_Route_Focus_GapPoint_Routes_Away()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "MaskedShape", Width = 500, Height = 350 };
        // Census-shaped editors: single-line, visible, enabled, sized.
        using var fieldA = new FormsTextBox { Left = 300, Top = 100, Width = 150, Height = 19 };
        using var fieldB = new FormsTextBox { Left = 50, Top = 200, Width = 150, Height = 19 };
        dlg.Controls.Add(fieldA);
        dlg.Controls.Add(fieldB);
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        dlg.Left = 60;
        dlg.Top = 60;
        dlg.Show();
        try
        {
            // +30 mirrors the dialog chrome allowance used by
            // DisabledHitFallthroughTests on the same construction.
            host.OnMouseDown(dlg.Left + 300 + 75, dlg.Top + 30 + 100 + 9);
            Assert.Same(fieldA, dlg.ActiveControl);
            host.OnMouseDown(dlg.Left + 50 + 75, dlg.Top + 30 + 200 + 9);
            Assert.Same(fieldB, dlg.ActiveControl);
            // Gap between/below the editors: no target (WPF8 hit-null
            // preserved — no RaiseMouseEvent fires), and desktop-correct
            // focus retention: a background click does not move focus.
            host.OnMouseDown(dlg.Left + 327, dlg.Top + 30 + 285);
            Assert.Same(fieldB, dlg.ActiveControl);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }
}
