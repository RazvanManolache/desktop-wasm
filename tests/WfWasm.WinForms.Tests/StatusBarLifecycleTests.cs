using System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

// RV2944: legacy StatusBar/StatusBarPanel must construct and run its
// text/panel lifecycle headless (Reflector ApplicationWindow news its own
// StatusBar subclass; the fork base ctor currently throws PNSE, killing
// form-create). This pins the semantically useful surface, not just a
// silent ctor: Text round-trip, panel add/count/text, ShowPanels and
// SizingGrip settable.
public class StatusBarLifecycleTests
{
    [Fact]
    public void Ctor_Succeeds_Headless()
    {
        using var sb = new StatusBar();
        Assert.NotNull(sb);
    }

    [Fact]
    public void Text_Panels_Lifecycle()
    {
        using var sb = new StatusBar();
        sb.Text = "Ready";
        Assert.Equal("Ready", sb.Text);
        sb.ShowPanels = true;
        sb.SizingGrip = false;
        var panel = new StatusBarPanel { Text = "Ln 1" };
        sb.Panels.Add(panel);
        Assert.Equal(1, sb.Panels.Count);
        Assert.Equal("Ln 1", ((StatusBarPanel)sb.Panels[0]).Text);
    }
}
