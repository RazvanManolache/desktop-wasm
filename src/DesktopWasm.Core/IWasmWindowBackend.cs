namespace DesktopWasm.Core;

/// <summary>Opaque virtual window handle (cf. Win32 HWND).</summary>
public readonly record struct WasmWindowHandle(int Id);

/// <summary>Window chrome style for the virtual window manager.</summary>
public enum WasmChromeStyle
{
    Overlapped,
    Borderless,
    Dialog,
}

/// <summary>Integer bounds in CSS pixels.</summary>
public readonly record struct WasmBounds(int X, int Y, int Width, int Height);

/// <summary>
/// Virtual window manager shared by WinForms and WPF.
/// Owns virtual HWNDs, Z-order, modal overlays, window chrome, and coordinate spaces.
/// </summary>
public interface IWasmWindowBackend
{
    WasmWindowHandle Create(WasmChromeStyle chrome, WasmBounds bounds);
    void Close(WasmWindowHandle handle);
    void SetBounds(WasmWindowHandle handle, WasmBounds bounds);
    void SetZOrder(WasmWindowHandle handle, int z);
    void ShowModal(WasmWindowHandle handle);
    void EndModal(WasmWindowHandle handle);
    WasmBounds ClientToScreen(WasmWindowHandle handle, WasmBounds client);
    WasmBounds ScreenToClient(WasmWindowHandle handle, WasmBounds screen);
}
