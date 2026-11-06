// RVW3293-VEN9: public SendMessage forward seam for rewritten vendor modules.
// user32.SendMessage P/Invokes cannot bind headless, so the rewriter stubs
// them; the stub drops vendor display-text pushes (Syncfusion mask
// SetTextBoxText: StringToHGlobalAuto + SendMessage + FreeHGlobal). This seam
// routes the exact vendor shape back into the internal fork emulation (VEN1
// WM_SETTEXT arm and friends). One method, exact vendor signature; the
// emulation itself stays internal.
namespace Windows.Win32;

public static class ForkSendMessage
{
    public static nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam)
        => PInvokeCore.SendMessage(new Foundation.HWND(hWnd), unchecked((uint)msg), new Foundation.WPARAM(unchecked((nuint)wParam)), new Foundation.LPARAM(lParam));
}
