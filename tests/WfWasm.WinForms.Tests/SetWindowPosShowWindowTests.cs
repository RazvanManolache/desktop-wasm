using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WfWasm.WinForms.Tests;

// Fix G (SF02 ghost): SetWindowPos ignored SWP_SHOWWINDOW/SWP_HIDEWINDOW and
// only moved bounds, so windows shown solely through that flag (vendor drag
// ghosts via DragHelper.ShowWindowTopMost) stayed invisible forever. The stub
// now mirrors ShowWindow's delivery on the outer path.
public class SetWindowPosShowWindowTests
{
    [Fact]
    public void SetWindowPos_With_ShowWindow_Flag_Shows_TopLevel()
    {
        using var form = new Form();
        nint handle = form.Handle; // force handle creation
        Assert.False(form.Visible);
        PInvoke.SetWindowPos(new HWND(handle), HWND.HWND_TOPMOST, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
            | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        Assert.True(form.Visible);
    }

    [Fact]
    public void SetWindowPos_With_HideWindow_Flag_Hides_TopLevel()
    {
        using var form = new Form();
        form.Show();
        Assert.True(form.Visible);
        PInvoke.SetWindowPos(new HWND(form.Handle), HWND.HWND_TOPMOST, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
            | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_HIDEWINDOW);
        Assert.False(form.Visible);
    }

    [Fact]
    public void SetWindowPos_Without_Visibility_Flags_Keeps_Visibility()
    {
        using var form = new Form();
        nint handle = form.Handle;
        Assert.False(form.Visible);
        PInvoke.SetWindowPos(new HWND(handle), HWND.HWND_TOPMOST, 10, 10, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER
            | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        Assert.False(form.Visible);
    }
}
