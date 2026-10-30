namespace DesktopWasm.Core;

/// <summary>
/// Managed stub surface for user32/kernel32/gdi32/dwmapi.
/// Returns virtual handles instead of P/Invoking; unimplemented
/// members record a diagnostic instead of throwing, so ported
/// WinForms/WPF code keeps running in the browser.
/// </summary>
public interface INativeShim
{
    nint GetDC(WasmWindowHandle window);
    void ReleaseDC(WasmWindowHandle window, nint hdc);
    void InvalidateRect(WasmWindowHandle window, WasmBounds rect);
    int GetSystemMetrics(int index);
    IReadOnlyList<string> UnimplementedCalls { get; }
}
