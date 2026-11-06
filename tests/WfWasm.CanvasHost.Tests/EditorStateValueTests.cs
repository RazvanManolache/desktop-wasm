// WC1746: EditorStateDumpJson carries Value for valued controls (the
// paintbar separating probe reads the served bar Value from one dump
// line). Pins the reflection projection, not any paint behavior.
using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsNumericUpDown = System.Windows.Forms.NumericUpDown;

public sealed class EditorStateValueTests
{
    [Fact]
    public void Dump_Includes_Value_For_Valued_Controls()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        using var probe = new FormsNumericUpDown
        {
            Name = "valueProbe1746",
            Value = 42m,
        };
        form.Controls.Add(probe);
        try
        {
            string json = EditorStateProbe.Dump(host);
            Assert.Contains("\"valueProbe1746\"", json);
            Assert.Contains("\"value\":42", json);
        }
        finally
        {
            form.Controls.Remove(probe);
        }
    }

    [Fact]
    public void Dump_Includes_SelfPaint_Gate_Facts()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        using var probe = new FormsNumericUpDown
        {
            Name = "gateProbe1746",
        };
        form.Controls.Add(probe);
        try
        {
            string json = EditorStateProbe.Dump(host);
            Assert.Contains("\"gateProbe1746\"", json);
            Assert.Contains("\"selfPaint\":", json);
            Assert.Contains("\"bgImageNull\":", json);
        }
        finally
        {
            form.Controls.Remove(probe);
        }
    }
}
