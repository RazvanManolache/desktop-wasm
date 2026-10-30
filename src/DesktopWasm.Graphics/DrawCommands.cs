namespace DesktopWasm.Graphics;

/// <summary>
/// Shared retained/immediate draw records. Superset of the WinForms
/// CanvasBridge op-codes (fillRect, roundRect, fillText, gradient,
/// strokeLine, ellipse, drawImage/defBitmap/blitBitmap/retainLayers/patchBitmap),
/// extended with transform and clip records so WPF DrawingContext output
/// replays without loss.
/// </summary>
public abstract record DrawCommand
{
    public sealed record FillRect(float X, float Y, float W, float H, uint Rgba) : DrawCommand;
    public sealed record FillRoundedRect(float X, float Y, float W, float H, float Radius, uint Rgba) : DrawCommand;
    public sealed record DrawText(string Text, float X, float Y, string FontCss, uint Rgba, int HAlign, int VAlign, int MnemonicIndex) : DrawCommand;
    public sealed record FillGradient(float X, float Y, float W, float H, uint TopRgba, uint BottomRgba) : DrawCommand;
    public sealed record DrawLine(float X1, float Y1, float X2, float Y2, float Width, uint Rgba) : DrawCommand;
    public sealed record DrawEllipse(float X, float Y, float W, float H, float LineWidth, uint OutlineRgba, uint? FillRgba) : DrawCommand;
    public sealed record DrawImage(byte[] Rgba, int Width, int Height, float X, float Y, float DW, float DH) : DrawCommand;
    public sealed record DefBitmap(string LayerId, byte[] Rgba, int Width, int Height) : DrawCommand;
    public sealed record BlitBitmap(string LayerId, float X, float Y, float DW, float DH) : DrawCommand;
    public sealed record RetainLayers(IReadOnlyList<string> LiveIds) : DrawCommand;
    public sealed record PatchBitmap(string LayerId, byte[] Rgba, int X, int Y, int Width, int Height) : DrawCommand;

    // WPF composition additions (no CanvasBridge equivalent; JSON fallback encodes them).
    public sealed record PushClip(float X, float Y, float W, float H) : DrawCommand;
    public sealed record PopClip : DrawCommand;
    public sealed record PushTransform(float M11, float M12, float M21, float M22, float Dx, float Dy) : DrawCommand;
    public sealed record PopTransform : DrawCommand;
    public sealed record DrawGeometry(string PathData, uint FillRgba, uint? StrokeRgba, float StrokeWidth, string FillRule = "nonzero") : DrawCommand;
}
