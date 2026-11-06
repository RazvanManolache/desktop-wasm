// CREDIT1300-WPF6: deep-button bounds oracle. DescribeKids stops
// expanding at depth 2, so buttons nested deeper (wizard Next inside
// page/panel stacks) never appear in kids= dumps and no bounds oracle
// exists for them. The dump path must emit [Host] deepbuttons= with
// identity + bounds for depth>=3 buttons only; depth<=2 buttons stay
// out of that line (listed controls unchanged). FAILS until the
// walker lands (no such line); zero behavior change to hit/dispatch.
using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsPanel = System.Windows.Forms.Panel;

public sealed class DeepButtonOracleTests
{
    [Fact]
    public void ClickDump_Lists_Deep_Button_Only()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        using var shallow = new FormsButton { Name = "ShallowOk", Text = "Ok", Left = 10, Top = 10, Width = 60, Height = 23 };
        using var p1 = new FormsPanel { Left = 10, Top = 60, Width = 400, Height = 200 };
        using var p2 = new FormsPanel { Left = 5, Top = 5, Width = 390, Height = 190 };
        using var p3 = new FormsPanel { Left = 5, Top = 5, Width = 380, Height = 180 };
        using var deep = new FormsButton { Name = "DeepNext", Text = "Next >>", Left = 331, Top = 351 - 70, Width = 75, Height = 19 };
        p3.Controls.Add(deep);
        p2.Controls.Add(p3);
        p1.Controls.Add(p2);
        form!.Controls.Add(p1);
        form.Controls.Add(shallow);

        var sb = new System.Text.StringBuilder();
        var old = System.Console.Out;
        System.Console.SetOut(new System.IO.StringWriter(sb));
        try
        {
            // Far-outside point: no target, but the client dump path
            // (form loc / kids= / deepbuttons=) still fires.
            host.OnMouseDown(-1000, -1000);
        }
        finally
        {
            System.Console.SetOut(old);
            form.Controls.Remove(p1);
            form.Controls.Remove(shallow);
        }

        string log = sb.ToString();
        Assert.Contains("[Host] kids=", log);
        Assert.Contains("[Host] deepbuttons=", log);
        string deepLine = "";
        foreach (var line in log.Split('\n'))
            if (line.Contains("[Host] deepbuttons="))
                deepLine = line;
        Assert.Contains("DeepNext", deepLine);
        Assert.Contains("75x19", deepLine);
        Assert.DoesNotContain("ShallowOk", deepLine);
    }
}
