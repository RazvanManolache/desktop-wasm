using DesktopWasm.Core;

namespace DesktopWasm.Interop;

/// <summary>
/// Host-side pointer event in device pixels. <see cref="ToLayout"/> is a
/// pure DPI scale; <see cref="IsInside"/> is the explicit clip test against
/// the host bounds in layout px. Sessions drop outside presses and still
/// route outside moves/releases so implicit capture keeps working.
/// </summary>
public readonly record struct HostPointerEvent(
    double DeviceX,
    double DeviceY,
    WasmPointerButton Button,
    WasmPointerKind Kind,
    int PointerId = 0)
{
    /// <summary>Device px -> layout px (throws on non-finite coordinates).</summary>
    public (double X, double Y) ToLayout(InteropDpi dpi)
    {
        RequireFinite(DeviceX, nameof(DeviceX));
        RequireFinite(DeviceY, nameof(DeviceY));
        return (dpi.ToLayoutX(DeviceX), dpi.ToLayoutY(DeviceY));
    }

    /// <summary>
    /// True when the scaled point lands inside the host bounds (edges
    /// inclusive). Throws on non-finite coordinates.
    /// </summary>
    public bool IsInside(InteropDpi dpi, double hostWidth, double hostHeight)
    {
        var (x, y) = ToLayout(dpi);
        return x >= 0 && y >= 0 && x <= hostWidth && y <= hostHeight;
    }

    private static void RequireFinite(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("Coordinate must be finite.", name);
    }
}

/// <summary>
/// Host-side key event (DOM key name). Converts to the shared
/// <see cref="WasmKeyArgs"/> the engine input managers consume.
/// </summary>
public readonly record struct HostKeyEvent(
    string Key,
    bool IsDown,
    bool Ctrl = false,
    bool Shift = false,
    bool Alt = false)
{
    /// <summary>Normalized args for the session window.</summary>
    public WasmKeyArgs ToWasmArgs(WasmWindowHandle window)
    {
        ArgumentNullException.ThrowIfNull(Key);
        return new WasmKeyArgs(window, Key, IsDown, Ctrl, Shift, Alt);
    }
}
