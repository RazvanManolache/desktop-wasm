extern alias Fork;

using System.Runtime.InteropServices;
using System.Windows.Forms;
using ForkWin32 = Fork::Windows.Win32;

namespace WfWasm.WinForms.Tests;

// Syncfusion MaskedEditBox commits typed display text through its own
// NativeMethods.SendMessage(Handle, WM_SETTEXT, ...) (see
// MaskedEditBox.SetTextBoxText in Syncfusion.Shared.Base, read-only Cecil
// trace in .artifacts/tmp/runs/dw-vendor/rvw3293/probe/). The headless
// SendMessage emulation used to fall through on WM_SETTEXT/WM_GETTEXT/
// WM_GETTEXTLENGTH, so the vendor write landed nowhere: KeyDown/KeyPress
// arrived but no visible text committed (RVW3293-VENDOR).
public sealed class TextBoxWindowTextShimTests
{
    private static nint SendRawSetText(nint hwnd, string text)
    {
        nint textPtr = Marshal.StringToHGlobalUni(text);
        try
        {
            ForkWin32.Foundation.LRESULT result = ForkWin32.PInvokeCore.SendMessage(
                new ForkWin32.Foundation.HWND(hwnd),
                ForkWin32.PInvokeCore.WM_SETTEXT,
                default,
                textPtr);
            return result.Value;
        }
        finally
        {
            Marshal.FreeHGlobal(textPtr);
        }
    }

    [Fact]
    public void RawSetTextMessageStoresWindowText()
    {
        using var box = new TextBox();
        box.Text = "boot";
        nint hwnd = box.Handle; // force handle creation
        nint result = SendRawSetText(hwnd, "typed");
        Assert.Equal(1, result);
        Assert.Equal("typed", box.Text);
    }

    [Fact]
    public void RawSetTextMessageResetsSelection()
    {
        using var box = new TextBox();
        box.Text = "abcd";
        nint hwnd = box.Handle;
        box.Select(1, 2);
        SendRawSetText(hwnd, "typed");
        Assert.Equal(0, box.SelectionStart);
        Assert.Equal(0, box.SelectionLength);
    }

    [Fact]
    public void RawGetTextLengthReportsStoredText()
    {
        using var box = new TextBox();
        box.Text = "boot";
        nint hwnd = box.Handle;
        SendRawSetText(hwnd, "typed!");
        ForkWin32.Foundation.LRESULT length = ForkWin32.PInvokeCore.SendMessage(
            new ForkWin32.Foundation.HWND(hwnd),
            ForkWin32.PInvokeCore.WM_GETTEXTLENGTH,
            default,
            default);
        Assert.Equal(6, length.Value);
    }

    [Fact]
    public void RawSetTextToDeadHandleStaysSilent()
    {
        nint result = SendRawSetText(IntPtr.Zero, "typed");
        Assert.Equal(0, result);
    }
}
