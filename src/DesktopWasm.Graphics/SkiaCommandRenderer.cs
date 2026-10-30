using System.Runtime.InteropServices;
using SkiaSharp;

namespace DesktopWasm.Graphics;

/// <summary>Replays application commands onto a caller-owned raster or GPU canvas.
/// Resources belong to this renderer/session, never to a throwaway demo surface.</summary>
public sealed class SkiaCommandRenderer : IDisposable
{
    private readonly Dictionary<uint, SKPaint> _fills = new();
    private readonly Dictionary<string, (SKTypeface Face, SKFont Font)> _fonts = new();
    private readonly Dictionary<string, SKBitmap> _layers = new();
    private readonly Dictionary<DrawCommand.FillGradient, SKPaint> _gradients = new();
    private readonly Dictionary<string, SKPath> _paths = new();
    private bool _disposed;

    public void Replay(SKCanvas canvas, IReadOnlyList<DrawCommand> commands)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(commands);
        var saved = canvas.Save();
        var depth = 0;
        try
        {
            foreach (var command in commands)
                switch (command)
                {
                    case DrawCommand.FillRect r: canvas.DrawRect(r.X, r.Y, r.W, r.H, Fill(r.Rgba)); break;
                    case DrawCommand.FillRoundedRect r: canvas.DrawRoundRect(r.X, r.Y, r.W, r.H, r.Radius, r.Radius, Fill(r.Rgba)); break;
                    case DrawCommand.FillGradient g:
                        if (g.W > 0 && g.H > 0) canvas.DrawRect(g.X, g.Y, g.W, g.H, Gradient(g));
                        break;
                    case DrawCommand.DrawLine l:
                        using (var paint = new SKPaint { Color = SkiaSurface.ToColor(l.Rgba), StrokeWidth = l.Width, StrokeCap = SKStrokeCap.Round, IsAntialias = true })
                            canvas.DrawLine(l.X1, l.Y1, l.X2, l.Y2, paint);
                        break;
                    case DrawCommand.DrawEllipse e:
                        var oval = new SKRect(e.X, e.Y, e.X + e.W, e.Y + e.H);
                        if (e.FillRgba is { } color) canvas.DrawOval(oval, Fill(color));
                        if (e.LineWidth > 0)
                            using (var paint = new SKPaint { Color = SkiaSurface.ToColor(e.OutlineRgba), StrokeWidth = e.LineWidth, Style = SKPaintStyle.Stroke, IsAntialias = true }) canvas.DrawOval(oval, paint);
                        break;
                    case DrawCommand.DrawText t: DrawText(canvas, t); break;
                    case DrawCommand.DrawGeometry g:
                        var path = Path(g.PathData, g.FillRule);
                        {
                            canvas.DrawPath(path, Fill(g.FillRgba));
                            if (g.StrokeRgba is { } stroke)
                                using (var paint = new SKPaint { Color = SkiaSurface.ToColor(stroke), StrokeWidth = g.StrokeWidth, Style = SKPaintStyle.Stroke, IsAntialias = true }) canvas.DrawPath(path, paint);
                        }
                        break;
                    case DrawCommand.PushClip c:
                        canvas.Save(); depth++; canvas.ClipRect(new SKRect(c.X, c.Y, c.X + c.W, c.Y + c.H)); break;
                    case DrawCommand.PushTransform t:
                        canvas.Save(); depth++; canvas.Concat(new SKMatrix(t.M11, t.M21, t.Dx, t.M12, t.M22, t.Dy, 0, 0, 1)); break;
                    case DrawCommand.PopClip or DrawCommand.PopTransform:
                        if (depth-- <= 0) throw new InvalidOperationException("Unbalanced render-state pop.");
                        canvas.Restore(); break;
                    case DrawCommand.DefBitmap b:
                        var bitmap = Bitmap(b.Rgba, b.Width, b.Height);
                        if (_layers.Remove(b.LayerId, out var old)) old.Dispose();
                        _layers[b.LayerId] = bitmap; break;
                    case DrawCommand.PatchBitmap p:
                        if (!_layers.TryGetValue(p.LayerId, out var target)) throw new InvalidOperationException($"Missing bitmap layer '{p.LayerId}'.");
                        if (p.X < 0 || p.Y < 0 || p.X + p.Width > target.Width || p.Y + p.Height > target.Height) throw new ArgumentOutOfRangeException(nameof(commands), "Bitmap patch is outside its layer.");
                        using (var patch = Bitmap(p.Rgba, p.Width, p.Height))
                        using (var painter = new SKCanvas(target))
                        using (var paint = new SKPaint { BlendMode = SKBlendMode.Src }) painter.DrawBitmap(patch, p.X, p.Y, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), paint);
                        break;
                    case DrawCommand.BlitBitmap b:
                        if (!_layers.TryGetValue(b.LayerId, out var layer)) throw new InvalidOperationException($"Missing bitmap layer '{b.LayerId}'.");
                        canvas.DrawBitmap(layer, new SKRect(b.X, b.Y, b.X + b.DW, b.Y + b.DH), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)); break;
                    case DrawCommand.RetainLayers r:
                        foreach (var id in _layers.Keys.Where(id => !r.LiveIds.Contains(id)).ToArray()) { _layers[id].Dispose(); _layers.Remove(id); }
                        break;
                    case DrawCommand.DrawImage image:
                        using (var source = Bitmap(image.Rgba, image.Width, image.Height)) canvas.DrawBitmap(source, new SKRect(image.X, image.Y, image.X + image.DW, image.Y + image.DH), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
                        break;
                    default: throw new NotSupportedException($"Unsupported drawing command {command.GetType().Name}.");
                }
            if (depth != 0) throw new InvalidOperationException("Unbalanced render-state push.");
        }
        finally { canvas.RestoreToCount(saved); }
    }

    private SKPaint Fill(uint color)
    {
        if (_fills.TryGetValue(color, out var paint)) return paint;
        if (_fills.Count >= 256) { foreach (var old in _fills.Values) old.Dispose(); _fills.Clear(); }
        return _fills[color] = new SKPaint { Color = SkiaSurface.ToColor(color), IsAntialias = true };
    }

    private void DrawText(SKCanvas canvas, DrawCommand.DrawText text)
    {
        if (string.IsNullOrEmpty(text.Text)) return;
        if (!_fonts.TryGetValue(text.FontCss, out var resource))
        {
            if (_fonts.Count >= 64) { foreach (var old in _fonts.Values) { old.Font.Dispose(); old.Face.Dispose(); } _fonts.Clear(); }
            FontSpec spec;
            try { spec = TextShaper.ParseFontCss(text.FontCss); }
            catch (ArgumentException) { spec = new FontSpec(14, null); }
            var face = TextShaper.ResolveTypeface(spec.Family) ?? throw new InvalidOperationException("No font is available for the application renderer.");
            resource = (face, new SKFont(face, spec.SizePx)); _fonts[text.FontCss] = resource;
        }
        var metrics = resource.Font.Metrics;
        var y = text.Y - (text.VAlign switch { 1 => metrics.Ascent, 2 => (metrics.Ascent + metrics.Descent) / 2, 3 => metrics.Descent, _ => 0 });
        canvas.DrawText(text.Text, text.X, y, text.HAlign switch { 1 => SKTextAlign.Center, 2 => SKTextAlign.Right, _ => SKTextAlign.Left }, resource.Font, Fill(text.Rgba));
    }

    private static SKBitmap Bitmap(byte[] rgba, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgba.Length != checked(width * height * 4)) throw new ArgumentException("Bitmap data must contain width × height RGBA pixels.");
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        return bitmap;
    }

    private SKPaint Gradient(DrawCommand.FillGradient gradient)
    {
        if (_gradients.TryGetValue(gradient, out var paint)) return paint;
        if (_gradients.Count >= 128) { foreach (var old in _gradients.Values) old.Dispose(); _gradients.Clear(); }
        using var shader = SKShader.CreateLinearGradient(new SKPoint(gradient.X, gradient.Y), new SKPoint(gradient.X, gradient.Y + gradient.H),
            [SkiaSurface.ToColor(gradient.TopRgba), SkiaSurface.ToColor(gradient.BottomRgba)], SKShaderTileMode.Clamp);
        return _gradients[gradient] = new SKPaint { Shader = shader };
    }

    private SKPath Path(string data, string fillRule)
    {
        var key = fillRule == "evenodd" ? data + "\u0001evenodd" : data;
        if (_paths.TryGetValue(key, out var path)) return path;
        if (_paths.Count >= 128) { foreach (var old in _paths.Values) old.Dispose(); _paths.Clear(); }
        var parsed = SKPath.ParseSvgPathData(data) ?? throw new InvalidOperationException("Invalid geometry path.");
        if (fillRule == "evenodd") parsed.FillType = SKPathFillType.EvenOdd;
        return _paths[key] = parsed;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var paint in _fills.Values) paint.Dispose();
        foreach (var (face, font) in _fonts.Values) { font.Dispose(); face.Dispose(); }
        foreach (var bitmap in _layers.Values) bitmap.Dispose();
        foreach (var paint in _gradients.Values) paint.Dispose();
        foreach (var path in _paths.Values) path.Dispose();
        _fills.Clear(); _fonts.Clear(); _layers.Clear(); _gradients.Clear(); _paths.Clear();
    }
}
