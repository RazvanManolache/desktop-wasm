using System.Drawing;
using System.Windows.Forms;
using WfWasm.CanvasHost;
using WfWasm.Shim;
using Control = System.Windows.Forms.Control;

public sealed class WheelDirectionTests
{
    [Fact]
    public void Reversing_A_Full_Notch_Does_Not_Wait_For_Another_Event()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        foreach (Control child in form.Controls.Cast<Control>().ToArray()) child.Dispose();
        using var target = new Control { Location = new Point(20, 20), Size = new Size(80, 80) };
        form.Controls.Add(target);
        var deltas = new List<int>();
        target.MouseWheel += (_, e) => deltas.Add(e.Delta);
        int x = form.Left + 30, y = form.Top + 60;
        for (int i = 0; i < 4; i++) host.OnMouseWheelRaw(x, y, 0, 120, 0);
        Assert.Equal(4, deltas.Count);
        host.OnMouseWheelRaw(x, y, 0, -120, 0);
        Assert.Equal(5, deltas.Count);
        Assert.Equal(120, deltas[^1]);
    }
}
