namespace DesktopWasm.Interop;

/// <summary>
/// Session DPI: device pixels per layout (CSS) pixel per axis.
/// Engine-neutral: both adapters scale device input down to layout and
/// report frames back up in device px with this scale attached.
/// </summary>
public readonly record struct InteropDpi(double ScaleX, double ScaleY)
{
    /// <summary>96-DPI identity: device px == layout px.</summary>
    public static InteropDpi Identity => new(1, 1);

    /// <summary>Uniform scale (throws when not finite positive).</summary>
    public static InteropDpi ForScale(double scale) => ForScale(scale, scale);

    /// <summary>Per-axis scale (throws when either axis is not finite positive).</summary>
    public static InteropDpi ForScale(double scaleX, double scaleY)
    {
        if (!double.IsFinite(scaleX) || scaleX <= 0)
            throw new ArgumentOutOfRangeException(nameof(scaleX), "DPI scale must be finite and positive.");
        if (!double.IsFinite(scaleY) || scaleY <= 0)
            throw new ArgumentOutOfRangeException(nameof(scaleY), "DPI scale must be finite and positive.");
        return new InteropDpi(scaleX, scaleY);
    }

    /// <summary>Layout px -> device px on X (throws on non-finite input).</summary>
    public double ToDeviceX(double layout) => Scale(layout, ScaleX, nameof(layout));

    /// <summary>Layout px -> device px on Y (throws on non-finite input).</summary>
    public double ToDeviceY(double layout) => Scale(layout, ScaleY, nameof(layout));

    /// <summary>Device px -> layout px on X (throws on non-finite input).</summary>
    public double ToLayoutX(double device) => Scale(device, 1 / ScaleX, nameof(device));

    /// <summary>Device px -> layout px on Y (throws on non-finite input).</summary>
    public double ToLayoutY(double device) => Scale(device, 1 / ScaleY, nameof(device));

    private static double Scale(double value, double factor, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("Coordinate must be finite.", name);
        return value * factor;
    }
}
