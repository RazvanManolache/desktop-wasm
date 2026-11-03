using System.Reflection;
using System.Windows.Forms;
using WfWasm.CanvasHost;
using AppScene = WfWasm.Shim.AppScene;
public sealed class NumericDialogInputTests
{
    private static readonly MethodInfo Focus = typeof(WinFormsAppHost).GetMethod("NoteFocus", BindingFlags.Instance | BindingFlags.NonPublic)!;
    [Fact]
    public void Leaving_numeric_editor_commits_value_before_OK_and_SelectAll_stays_local()
    {
        using var host = new WinFormsAppHost(); host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        using var numeric = new NumericUpDown { Minimum = -100, Maximum = 100, Width = 100 };
        form.Controls.Add(numeric); numeric.CreateControl();
        var edit = numeric.Controls.OfType<TextBox>().Single();
        Focus.Invoke(host, new object?[] { edit });
        edit.Text = "12"; edit.Select(1, 0);
        host.OnKeyDown("a", ctrl: true);
        Assert.Equal(0, edit.SelectionStart); Assert.Equal(2, edit.SelectionLength);
        edit.Text = "30";
        decimal committed = -1; numeric.ValueChanged += (_, _) => committed = numeric.Value;
        using var ok = new Button(); form.Controls.Add(ok);
        Focus.Invoke(host, new object?[] { ok });
        Assert.Equal(30m, committed); Assert.Equal(30m, numeric.Value);
    }
}

