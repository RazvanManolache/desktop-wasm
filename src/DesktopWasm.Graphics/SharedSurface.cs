using SkiaSharp;

namespace DesktopWasm.Graphics;

/// <summary>
/// Shared surface: WinForms immediate-mode painting and WPF retained
/// composition both target this SKCanvas. Zero-copy interop story:
/// a WinForms control renders into the same SKSurface as its parent
/// WPF visual (WindowsFormsHost) and vice versa (ElementHost).
/// </summary>
public interface ISharedGraphicsSurface : IDisposable
{
    SKCanvas Canvas { get; }
    int Width { get; }
    int Height { get; }
    void Replay(IReadOnlyList<DrawCommand> commands);
    SKImage Snapshot();
}

/// <summary>Raster SKSurface backing store with a DrawCommand replay path.</summary>
public sealed class SkiaSurface : ISharedGraphicsSurface
{
    private readonly SKSurface _surface;
    private readonly SkiaCommandRenderer _renderer = new();
    private bool _disposed;

    public SkiaSurface(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
    }

    public SKCanvas Canvas => _surface.Canvas;
    public int Width { get; }
    public int Height { get; }

    public static SKColor ToColor(uint rgba)
    {
        // Local convention (matches CanvasBridge Css #{rgba:x8}): 0xRRGGBBAA.
        var r = (byte)(rgba >> 24);
        var g = (byte)((rgba >> 16) & 0xFF);
        var b = (byte)((rgba >> 8) & 0xFF);
        var a = (byte)(rgba & 0xFF);
        return new SKColor(r, g, b, a);
    }

    public void Replay(IReadOnlyList<DrawCommand> commands) => _renderer.Replay(Canvas, commands);

    public SKImage Snapshot() => _surface.Snapshot();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _renderer.Dispose();
        _surface.Dispose();
    }
}
