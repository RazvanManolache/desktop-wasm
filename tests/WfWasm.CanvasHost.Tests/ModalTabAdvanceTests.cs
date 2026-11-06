using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Shim;

// RVW-1105-0900-runtime Job2b (Twist Tab focus-trap, vendor FP-0735 routing):
// Tab/Shift+Tab inside a modal dialog must advance focus through the real
// Control.ProcessDialogKey chain (desktop parity: TabIndex/TabStop order
// with Form wrap), instead of dying in focused-control default handling
// (served symptom: Tabs 2-10 swallowed, OK/Cancel unreachable). FAILS until
// the host pump emulates dialog Tab advance for modals; scope pins below
// must keep passing (no hijack of Ctrl+Tab, no change outside modals).
public sealed class ModalTabAdvanceTests
{
    private static readonly MethodInfo Focus =
        typeof(WinFormsAppHost).GetMethod("NoteFocus", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static System.Windows.Forms.Form ModalWithThreeFields(
        out System.Windows.Forms.TextBox b0,
        out System.Windows.Forms.TextBox b1,
        out System.Windows.Forms.TextBox b2)
    {
        var dlg = new System.Windows.Forms.Form { Text = "TabDlg", Width = 360, Height = 240 };
        b0 = new System.Windows.Forms.TextBox { Name = "f0", TabIndex = 0, Left = 10, Top = 10, Width = 100 };
        b1 = new System.Windows.Forms.TextBox { Name = "f1", TabIndex = 1, Left = 10, Top = 40, Width = 100 };
        b2 = new System.Windows.Forms.TextBox { Name = "f2", TabIndex = 2, Left = 10, Top = 70, Width = 100 };
        dlg.Controls.Add(b0);
        dlg.Controls.Add(b1);
        dlg.Controls.Add(b2);
        dlg.ShowDialog();
        return dlg;
    }

    [Fact]
    public void Tab_In_Modal_Advances_To_Next_TabStop_And_Wraps()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = ModalWithThreeFields(out var b0, out var b1, out var b2);
        try
        {
            Focus.Invoke(host, new object?[] { b0 });
            host.OnKeyDown("Tab");
            Assert.Same(b1, dlg.ActiveControl);
            host.OnKeyDown("Tab");
            Assert.Same(b2, dlg.ActiveControl);
            host.OnKeyDown("Tab");
            Assert.Same(b0, dlg.ActiveControl);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void ShiftTab_In_Modal_Moves_Backward_With_Wrap()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = ModalWithThreeFields(out var b0, out var b1, out var b2);
        try
        {
            Focus.Invoke(host, new object?[] { b1 });
            host.OnKeyDown("Tab", shift: true);
            Assert.Same(b0, dlg.ActiveControl);
            host.OnKeyDown("Tab", shift: true);
            Assert.Same(b2, dlg.ActiveControl);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void CtrlTab_In_Modal_Is_Not_Hijacked()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = ModalWithThreeFields(out var b0, out _, out _);
        try
        {
            Focus.Invoke(host, new object?[] { b0 });
            host.OnKeyDown("Tab", ctrl: true);
            Assert.Same(b0, dlg.ActiveControl);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Tab_Without_Modal_Keeps_Existing_Routing()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        using var a = new System.Windows.Forms.TextBox { Name = "scopeA", TabIndex = 0 };
        using var b = new System.Windows.Forms.TextBox { Name = "scopeB", TabIndex = 1 };
        form.Controls.Add(a);
        form.Controls.Add(b);
        try
        {
            Focus.Invoke(host, new object?[] { a });
            host.OnKeyDown("Tab");
            Assert.Same(a, form.ActiveControl);
        }
        finally
        {
            form.Controls.Remove(a);
            form.Controls.Remove(b);
        }
    }
}
