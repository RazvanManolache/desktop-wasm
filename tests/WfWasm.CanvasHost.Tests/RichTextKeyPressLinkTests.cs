// sf08-keypress-link (dw-runtime): prove the host KeyDown-to-KeyPress link
// for RichTextBox. Served gen18 types 0px: KeyDown is delivered per
// keystroke but no KeyPress activity reaches the fork's browser-only
// OnKeyPress splice. Either the WM_CHAR PumpKeyFilters gate swallows the
// char for rich boxes, or the raise path misses. The fork override no-ops
// off-browser (OperatingSystem.IsBrowser), but base.OnKeyPress still raises
// the event, so this test observes the host link in-process.
using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsKeys = System.Windows.Forms.Keys;
using FormsRichTextBox = System.Windows.Forms.RichTextBox;

public sealed class RichTextKeyPressLinkTests
{
    private static (WinFormsAppHost Host, FormsRichTextBox Rich) FocusedRich()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var rich = NewRichTextBoxAllowingFirstTouchThrow();
        rich.SetBounds(20, 20, 220, 60);
        form.Controls.Add(rich);
        int absX = form.Location.X;
        int absY = form.Location.Y + 30;
        for (FormsControl? current = rich; current is not null && current is not FormsForm; current = current.Parent)
        {
            absX += current.Left;
            absY += current.Top;
        }
        host.OnMouseDown(absX + (rich.Width / 2), absY + (rich.Height / 2));
        return (host, rich);
    }

    private static FormsRichTextBox NewRichTextBoxAllowingFirstTouchThrow()
    {
        // Fork-side first-touch hazard (vendor-owned, not worked around in
        // product code): the maiden RichTextBox construction in a process
        // throws Win32Exception on the MsftEdit probe and a retry succeeds.
        // Tolerate exactly one such throw; anything else fails loudly.
        try
        {
            return new FormsRichTextBox();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new FormsRichTextBox();
        }
    }

    [Fact]
    public void RichTextBox_KeyDown_Raises_KeyPress()
    {
        var (host, rich) = FocusedRich();
        using (host)
        using (rich)
        {
            var downs = new List<FormsKeys>();
            var presses = new List<char>();
            rich.KeyDown += (_, e) => downs.Add(e.KeyCode);
            rich.KeyPress += (_, e) => presses.Add(e.KeyChar);
            host.OnKeyDown("H");
            Assert.Contains(FormsKeys.H, downs);
            Assert.Equal(new[] { 'H' }, presses);
        }
    }

    [Fact]
    public void RichTextBox_Typing_Does_Not_Use_TextBox_Insertion()
    {
        // Host insertion emulation is TextBox-typed by design (fork owns the
        // rich splice). Lock it: typing must not land text via the host path
        // in-process, or desktop would double-type once the link is live.
        var (host, rich) = FocusedRich();
        using (host)
        using (rich)
        {
            host.OnKeyDown("H");
            Assert.Equal(string.Empty, rich.Text);
        }
    }
}
