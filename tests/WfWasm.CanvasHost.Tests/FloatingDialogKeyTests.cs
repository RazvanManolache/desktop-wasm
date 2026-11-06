using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsForm = System.Windows.Forms.Form;
using FormsTextBox = System.Windows.Forms.TextBox;

namespace WfWasm.CanvasHost.Tests;

public sealed class FloatingDialogKeyTests
{
    [Fact]
    public void KeyDown_Reaches_TextBox_In_Floating_Dialog_After_Click()
    {
        // F1 (DIRECT-1105): clicking a text editor in a floating modeless
        // dialog (Paint Colors) never delivered keys — KeyTarget only
        // honored _focusedControl inside _activeForm, so routing fell
        // through to the document canvas (AppWorkspace).
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Floating", Width = 300, Height = 200 };
        using var editor = new FormsTextBox { Left = 50, Top = 50, Width = 120, Height = 30 };
        dlg.Controls.Add(editor);
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        dlg.Left = 100;
        dlg.Top = 100;
        dlg.Show();
        try
        {
            int mx = dlg.Left + editor.Left + (editor.Width / 2);
            int my = dlg.Top + 30 + editor.Top + (editor.Height / 2);
            host.OnMouseDown(mx, my);
            char? pressed = null;
            editor.KeyPress += (_, e) => pressed = e.KeyChar;
            host.OnKeyDown("a");
            Assert.Equal('a', pressed);
            Assert.Equal("a", editor.Text);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Canvas_Area_Click_Does_Not_Route_Keys_To_Stale_Dialog_Editor()
    {
        // Guard for the KeyTarget widening: once a floating editor was
        // clicked, clicking back on the active form must not keep sending
        // keys to the stale dialog editor.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        using var dlg = new FormsForm { Text = "Floating", Width = 300, Height = 200 };
        using var editor = new FormsTextBox { Left = 50, Top = 50, Width = 120, Height = 30 };
        dlg.Controls.Add(editor);
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        dlg.Left = 500;
        dlg.Top = 400;
        dlg.Show();
        try
        {
            int emx = dlg.Left + editor.Left + (editor.Width / 2);
            int emy = dlg.Top + 30 + editor.Top + (editor.Height / 2);
            host.OnMouseDown(emx, emy);
            char? pressed = null;
            editor.KeyPress += (_, e) => pressed = e.KeyChar;
            host.OnKeyDown("a");
            Assert.Equal('a', pressed);
            // Click empty client area of the active form (below its
            // controls), then keys must no longer reach the dialog editor.
            host.OnMouseDown(form.Location.X + 10, form.Location.Y + form.Height - 10);
            pressed = null;
            host.OnKeyDown("b");
            Assert.Null(pressed);
            Assert.Equal("a", editor.Text);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }
}
