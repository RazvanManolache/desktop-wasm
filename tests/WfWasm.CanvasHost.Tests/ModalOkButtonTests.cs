using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsDialogResult = System.Windows.Forms.DialogResult;
using FormsForm = System.Windows.Forms.Form;

/// <summary>
/// PUSH1106-MODAL: Paint Quantize OK is a PDN custom-painted push button
/// (PdnPushButton : PdnPushButtonBase : PdnButtonBase :
/// GdiBufferedAnimationControl : Control) — NOT a WinForms Button and not
/// reachable through IButtonControl. Served w27: the dialog opens 10/10 at
/// (335,372) 345x229 with okButton at (161,198)x(85x24); a real click on
/// its painted center must run the OK handler and dismiss the modal, like
/// the Button branch already does for plain buttons.
/// </summary>
public sealed class ModalOkButtonTests
{
    // PDN-faithful double: Control-derived custom-painted push button.
    // Name matches the served control so name-scoped host handling applies.
    private sealed class PdnPushButton : FormsControl
    {
    }

    private static (WinFormsAppHost host, FormsForm dlg, PdnPushButton ok, bool[] clicked) CreateQuantizeDialog()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var dlg = new FormsForm { Text = "Quantize", Width = 345, Height = 229 };
        var ok = new PdnPushButton
        {
            Name = "okButton",
            Text = "OK",
            Left = 161,
            Top = 198,
            Width = 85,
            Height = 24,
        };
        var cancel = new PdnPushButton
        {
            Name = "cancelButton",
            Text = "Cancel",
            Left = 253,
            Top = 198,
            Width = 85,
            Height = 24,
        };
        bool[] clicked = new bool[1];
        ok.Click += (_, _) => { clicked[0] = true; dlg.DialogResult = FormsDialogResult.OK; };
        dlg.Controls.Add(ok);
        dlg.Controls.Add(cancel);
        // NOTE: no AcceptButton assignment is possible here — a
        // Control-derived custom button cannot satisfy the IButtonControl
        // setter, exactly like the served PdnPushButton. Pointer dispatch
        // is the activation path under test.
        dlg.ShowDialog();
        return (host, dlg, ok, clicked);
    }

    [Fact]
    public void Custom_PushButton_OK_Center_Click_Applies_And_Dismisses()
    {
        var (host, dlg, ok, clicked) = CreateQuantizeDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            int cx = dlg.Location.X + ok.Left + (ok.Width / 2);
            int cy = dlg.Location.Y + 30 + ok.Top + (ok.Height / 2);
            host.OnMouseDown(cx, cy);
            host.OnMouseUp(cx, cy);
            Assert.True(clicked[0], "click on the painted OK center must run the OK handler");
            Assert.False(
                System.Windows.Forms.WasmDialogHost.TopModal(out _),
                "OK click must dismiss the modal synchronously, like the Button branch");
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
            host.Dispose();
            dlg.Dispose();
        }
    }

    [Fact]
    public void Custom_PushButton_OK_Direct_OnClick_Applies_And_Dismisses()
    {
        var (host, dlg, ok, clicked) = CreateQuantizeDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            int cx = dlg.Location.X + ok.Left + (ok.Width / 2);
            int cy = dlg.Location.Y + 30 + ok.Top + (ok.Height / 2);
            host.OnClick(cx, cy);
            Assert.True(clicked[0], "direct OnClick on the painted OK center must run the OK handler");
            Assert.False(
                System.Windows.Forms.WasmDialogHost.TopModal(out _),
                "direct OnClick on OK must dismiss the modal synchronously, like the Button branch");
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
            host.Dispose();
            dlg.Dispose();
        }
    }
}
