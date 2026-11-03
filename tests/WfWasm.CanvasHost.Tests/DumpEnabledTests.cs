// WC1348(2): tree dump carries Enabled. The ColTypeCombo hole (FP1340)
// needed app-vs-host attribution that Visible-only dumps cannot give.
// This pin adds a disabled control to the smoke form and asserts the
// SelectScene dump names its Enabled state. FAILS until the dump
// includes Enabled; do not weaken.
using WfWasm.CanvasHost;
using WfWasm.Shim;

public sealed class DumpEnabledTests
{
    [Fact]
    public void TreeDump_Includes_Enabled_State()
    {
        using var host = new WinFormsAppHost();
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        using var off = new System.Windows.Forms.Label
        {
            Name = "probeDisabled",
            Text = "off",
            Enabled = false,
            Location = new System.Drawing.Point(30, 300),
            Size = new System.Drawing.Size(80, 20),
        };
        form.Controls.Add(off);

        var sb = new System.Text.StringBuilder();
        var old = System.Console.Out;
        System.Console.SetOut(new System.IO.StringWriter(sb));
        try
        {
            host.SelectScene(AppScene.Smoke);
        }
        finally
        {
            System.Console.SetOut(old);
            form.Controls.Remove(off);
        }

        string? disabledLine = null;
        bool anyEnabledTrue = false;
        foreach (var line in sb.ToString().Split('\n'))
        {
            if (line.Contains("probeDisabled"))
                disabledLine = line;
            if (line.Contains("Enabled=True"))
                anyEnabledTrue = true;
        }

        Assert.NotNull(disabledLine);
        Assert.Contains("Enabled=False", disabledLine);
        Assert.True(anyEnabledTrue);
    }
}
