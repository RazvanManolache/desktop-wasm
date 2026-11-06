using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Shim;

// FOREMAN KEYLOG-runtime-0856-c4e8: every Tab/Enter press in the modal key
// flow must log one concise line carrying the key, the pre-press target,
// the outcome (advance from->to / consumed / declined / accept-button),
// the post-press focused control and a UTC stamp for post-serve
// correlation. FAILS until the flow lines land; do not weaken to the
// entry-line-only or KeyTrace marks (gated off by default).
public sealed class ModalKeyFlowLogTests
{
    private static readonly MethodInfo Focus =
        typeof(WinFormsAppHost).GetMethod("NoteFocus", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private sealed class ConsumingBox : System.Windows.Forms.TextBox
    {
        protected override bool ProcessDialogKey(System.Windows.Forms.Keys keyData) => true;
    }

    private static string Capture(Action action)
    {
        var sb = new System.Text.StringBuilder();
        var old = System.Console.Out;
        System.Console.SetOut(new System.IO.StringWriter(sb));
        try
        {
            action();
        }
        finally
        {
            System.Console.SetOut(old);
        }
        return sb.ToString();
    }

    private static System.Windows.Forms.Form ModalWithThreeFields(
        out System.Windows.Forms.TextBox b0,
        out System.Windows.Forms.TextBox b1,
        out System.Windows.Forms.TextBox b2)
    {
        var dlg = new System.Windows.Forms.Form { Text = "KeyLogDlg", Width = 360, Height = 240 };
        b0 = new System.Windows.Forms.TextBox { Name = "k0", TabIndex = 0, Left = 10, Top = 10, Width = 100 };
        b1 = new System.Windows.Forms.TextBox { Name = "k1", TabIndex = 1, Left = 10, Top = 40, Width = 100 };
        b2 = new System.Windows.Forms.TextBox { Name = "k2", TabIndex = 2, Left = 10, Top = 70, Width = 100 };
        dlg.Controls.Add(b0);
        dlg.Controls.Add(b1);
        dlg.Controls.Add(b2);
        dlg.ShowDialog();
        return dlg;
    }

    private static System.Windows.Forms.Form ModalWithAccept(
        out System.Windows.Forms.TextBox field, out System.Windows.Forms.Button ok)
    {
        var dlg = new System.Windows.Forms.Form { Text = "KeyLogEnter", Width = 360, Height = 200 };
        field = new System.Windows.Forms.TextBox { Name = "efield", TabIndex = 0, Left = 10, Top = 10, Width = 100 };
        ok = new System.Windows.Forms.Button { Name = "okLogBtn", Text = "OK", TabIndex = 1, Left = 10, Top = 40, DialogResult = System.Windows.Forms.DialogResult.OK };
        dlg.Controls.Add(field);
        dlg.Controls.Add(ok);
        dlg.AcceptButton = ok;
        dlg.ShowDialog();
        return dlg;
    }

    [Fact]
    public void Tab_Advance_Logs_From_To_Focus_Per_Press()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = ModalWithThreeFields(out var b0, out _, out _);
        Focus.Invoke(host, new object?[] { b0 });
        string log;
        try
        {
            log = Capture(() => host.OnKeyDown("Tab"));
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }

        Assert.Contains("[Host] Tab", log);
        Assert.Contains("from=", log);
        Assert.Contains("k0", log);
        Assert.Contains("k1", log);
        Assert.Contains("utc=", log);
    }

    [Fact]
    public void Tab_Consumed_Logs_Target_When_Chain_Handles_Without_Moving()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var dlg = new System.Windows.Forms.Form { Text = "ConsumeDlg", Width = 360, Height = 200 };
        using var box = new ConsumingBox { Name = "consumeBox", TabIndex = 0, Left = 10, Top = 10, Width = 100 };
        dlg.Controls.Add(box);
        dlg.ShowDialog();
        Focus.Invoke(host, new object?[] { box });
        string log;
        try
        {
            log = Capture(() => host.OnKeyDown("Tab"));
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }

        Assert.Contains("consumed", log);
        Assert.Contains("consumeBox", log);
        Assert.Contains("utc=", log);
    }

    [Fact]
    public void Tab_Declined_Logs_Target_On_Empty_Modal()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new System.Windows.Forms.Form { Text = "EmptyDlg", Width = 360, Height = 200 };
        dlg.ShowDialog();
        string log;
        try
        {
            log = Capture(() => host.OnKeyDown("Tab"));
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }

        Assert.Contains("declined", log);
        Assert.Contains("utc=", log);
    }

    [Fact]
    public void Enter_Accept_Logs_Button_Path_Per_Press()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = ModalWithAccept(out var field, out _);
        Focus.Invoke(host, new object?[] { field });
        string log;
        try
        {
            log = Capture(() => host.OnKeyDown("Enter"));
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }

        Assert.Contains("[Host] Enter", log);
        Assert.Contains("okLogBtn", log);
        Assert.Contains("utc=", log);
    }

    [Fact]
    public void Enter_Without_AcceptButton_Logs_No_Button_Path()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = ModalWithThreeFields(out var b0, out _, out _);
        Focus.Invoke(host, new object?[] { b0 });
        string log;
        try
        {
            log = Capture(() => host.OnKeyDown("Enter"));
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }

        Assert.DoesNotContain("[Host] Enter accept", log);
        Assert.True(dlg.Modal);
    }
}
