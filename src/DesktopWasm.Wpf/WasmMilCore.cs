using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf;

/// <summary>
/// Managed composition engine (WasmMilCore): replaces DUCE.Channel / milcore P/Invokes.
/// Intercepts DrawingContext render instructions and replays them against the
/// shared ISharedGraphicsSurface SKCanvas (zero-copy with WinForms).
/// </summary>
public sealed class DrawingContext
{
    private readonly List<DrawCommand> _commands = new();

    public IReadOnlyList<DrawCommand> Commands => _commands;

    public void DrawRectangle(float x, float y, float w, float h, uint rgba) =>
        _commands.Add(new DrawCommand.FillRect(x, y, w, h, rgba));

    public void DrawRoundedRectangle(float x, float y, float w, float h, float radius, uint rgba) =>
        _commands.Add(new DrawCommand.FillRoundedRect(x, y, w, h, radius, rgba));

    public void DrawText(string text, float x, float y, string fontCss, uint rgba, int hAlign = 0, int vAlign = 0, int mnemonic = -1) =>
        _commands.Add(new DrawCommand.DrawText(text, x, y, fontCss, rgba, hAlign, vAlign, mnemonic));

    public void DrawLine(float x1, float y1, float x2, float y2, float width, uint rgba) =>
        _commands.Add(new DrawCommand.DrawLine(x1, y1, x2, y2, width, rgba));

    /// <summary>Axis-aligned ellipse op (JSON op 6; Canvas2D + Skia replay it).</summary>
    public void DrawEllipse(float x, float y, float w, float h, float lineWidth, uint outlineRgba, uint? fillRgba = null) =>
        _commands.Add(new DrawCommand.DrawEllipse(x, y, w, h, lineWidth, outlineRgba, fillRgba));

    public void DrawGeometry(string pathData, uint fillRgba, uint? strokeRgba = null, float strokeWidth = 1, string fillRule = "nonzero") =>
        _commands.Add(new DrawCommand.DrawGeometry(pathData, fillRgba, strokeRgba, strokeWidth, fillRule));

    /// <summary>
    /// Geometry paint (matches DrawingContext.DrawGeometry(Brush, Pen, Geometry)):
    /// converted shapes (ScreenToGif TextPath) land here; the path data,
    /// fill/stroke inks and width resolve exactly like Shape.PaintGeometry,
    /// and a geometry-level Transform rides the op stack.
    /// </summary>
    public void DrawGeometry(Brush? fill, Pen? pen, Geometry? geometry)
    {
        if (geometry is null) return;
        var data = geometry.ToPathData();
        if (string.IsNullOrWhiteSpace(data)) return;
        var transform = geometry.Transform;
        if (transform is not null)
        {
            var m = transform.Value;
            PushTransform((float)m.M11, (float)m.M12, (float)m.M21, (float)m.M22, (float)m.OffsetX, (float)m.OffsetY);
        }
        try
        {
            var fillRgba = fill is null ? 0x00000000u : BrushRender.ResolveFill(fill);
            var strokeRgba = pen?.Brush is null ? null : (uint?)BrushRender.ResolveFill(pen.Brush);
            var width = pen is null ? 1f : (float)Math.Max(0, pen.Thickness);
            var evenOdd = geometry switch
            {
                GeometryGroup group => group.FillRule == FillRule.EvenOdd,
                PathGeometry path => path.FillRule == FillRule.EvenOdd,
                StreamGeometry stream => stream.FillRule == FillRule.EvenOdd,
                _ => false,
            };
            DrawGeometry(data, fillRgba, strokeRgba, width, evenOdd ? "evenodd" : "nonzero");
        }
        finally
        {
            if (transform is not null) PopTransform();
        }
    }

    /// <summary>RGBA bitmap blit (op 7: RGBA bytes, source size, destination rect).</summary>
    public void DrawImage(byte[] rgba, int width, int height, float x, float y, float dw, float dh) =>
        _commands.Add(new DrawCommand.DrawImage(rgba, width, height, x, y, dw, dh));

    /// <summary>
    /// Retained close (matches System.Windows.Media.DrawingContext.Close):
    /// recording is immediate, so this is a no-op marker; converted
    /// Standard.Utility.GenerateHICON closes its render context here.
    /// </summary>
    public void Close()
    {
    }

    private static float StrokeWidth(Pen? pen) =>
        pen is null ? 0f : (float)Math.Max(0, pen.Thickness);

    /// <summary>
    /// Brush/pen ellipse (matches DrawingContext.DrawEllipse(Brush, Pen, Point,
    /// double, double)): lowers to the shared ellipse op exactly like the
    /// Ellipse shape (fill-only when the pen is null, like WPF).
    /// </summary>
    public void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
    {
        var x = (float)(center.X - radiusX);
        var y = (float)(center.Y - radiusY);
        var w = (float)Math.Max(0, radiusX * 2);
        var h = (float)(Math.Max(0, radiusY * 2));
        if (pen?.Brush is not null && pen.Thickness > 0)
            DrawEllipse(x, y, w, h, StrokeWidth(pen), BrushRender.ResolveFill(pen.Brush),
                brush is null ? null : BrushRender.ResolveFill(brush));
        else if (brush is not null)
            DrawEllipse(x, y, w, h, 0, 0x00000000u, BrushRender.ResolveFill(brush));
    }

    /// <summary>
    /// Image blit (matches DrawingContext.DrawImage(ImageSource, Rect)):
    /// lowers to the shared RGBA op when the source has decoded pixels
    /// (like the Image paint path); an undecodable source paints nothing,
    /// like WPF painting no frame.
    /// </summary>
    public void DrawImage(ImageSource? image, Rect rectangle)
    {
        ArgumentNullException.ThrowIfNull(image);
        var rgba = image.RgbaPixels;
        if (rgba is null) return;
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
        DrawImage(rgba.Value.Pixels, rgba.Value.Width, rgba.Value.Height,
            (float)rectangle.X, (float)rectangle.Y, (float)rectangle.Width, (float)rectangle.Height);
    }

    /// <summary>
    /// Pen line (matches DrawingContext.DrawLine(Pen, Point, Point)): lowers
    /// to the shared line op; a null pen throws like WPF.
    /// </summary>
    public void DrawLine(Pen? pen, Point point0, Point point1)
    {
        ArgumentNullException.ThrowIfNull(pen);
        if (pen.Brush is null || pen.Thickness <= 0) return;
        DrawLine((float)point0.X, (float)point0.Y, (float)point1.X, (float)point1.Y,
            StrokeWidth(pen), BrushRender.ResolveFill(pen.Brush));
    }

    /// <summary>
    /// Brush/pen rectangle (matches DrawingContext.DrawRectangle(Brush, Pen,
    /// Rect)): fill plus the house stroke ring (outer ink, inset fill), like
    /// the Rectangle shape; null brush paints no fill and null pen no ring.
    /// </summary>
    public void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
        var x = (float)rectangle.X;
        var y = (float)rectangle.Y;
        var w = (float)rectangle.Width;
        var h = (float)rectangle.Height;
        if (brush is not null)
            DrawRectangle(x, y, w, h, BrushRender.ResolveFill(brush));
        if (pen?.Brush is null || pen.Thickness <= 0) return;
        var t = (float)Math.Min(pen.Thickness, Math.Min(rectangle.Width, rectangle.Height) / 2);
        if (t <= 0) return;
        var ink = BrushRender.ResolveFill(pen.Brush);
        DrawRectangle(x, y, w, h, ink);
        DrawRectangle(x + t, y + t, Math.Max(0, w - 2 * t), Math.Max(0, h - 2 * t),
            brush is null ? 0x00000000u : BrushRender.ResolveFill(brush));
    }

    /// <summary>
    /// Brush/pen rounded rectangle (matches
    /// DrawingContext.DrawRoundedRectangle(Brush, Pen, Rect, double, double)):
    /// single-radius house convention (the larger radius wins) like the
    /// Rectangle shape, with the same fill plus stroke-ring lowering.
    /// </summary>
    public void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
        var x = (float)rectangle.X;
        var y = (float)rectangle.Y;
        var w = (float)rectangle.Width;
        var h = (float)rectangle.Height;
        var radius = (float)Math.Max(0, Math.Max(radiusX, radiusY));
        if (brush is not null)
            DrawRoundedRectangle(x, y, w, h, radius, BrushRender.ResolveFill(brush));
        if (pen?.Brush is null || pen.Thickness <= 0) return;
        var t = (float)Math.Min(pen.Thickness, Math.Min(rectangle.Width, rectangle.Height) / 2);
        if (t <= 0) return;
        var ink = BrushRender.ResolveFill(pen.Brush);
        DrawRoundedRectangle(x, y, w, h, radius, ink);
        DrawRoundedRectangle(x + t, y + t, Math.Max(0, w - 2 * t), Math.Max(0, h - 2 * t),
            Math.Max(0, radius - t), brush is null ? 0x00000000u : BrushRender.ResolveFill(brush));
    }

    /// <summary>
    /// Formatted-text run (matches DrawingContext.DrawText(FormattedText,
    /// Point)): lowers to the shared text op with the shaped font, ink and
    /// alignment; the point is the top-left origin (the baseline offset rides
    /// along, like the TextBlock decoration probe math).
    /// </summary>
    public void DrawText(FormattedText? formattedText, Point origin)
    {
        ArgumentNullException.ThrowIfNull(formattedText);
        var text = formattedText.DisplayText;
        if (string.IsNullOrEmpty(text)) return;
        var fontCss = $"{formattedText.EmSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}px {formattedText.Typeface.PrimaryFamilyName}";
        var ink = BrushRender.ResolveFill(formattedText.Foreground);
        var hAlign = formattedText.TextAlignment switch
        {
            TextAlignment.Center => 1,
            TextAlignment.Right => 2,
            _ => 0,
        };
        DrawText(text, (float)origin.X, (float)(origin.Y + formattedText.Baseline), fontCss, ink, hAlign, 0, -1);
    }

    /// <summary>
    /// Video frame blit (matches DrawingContext.DrawVideo(MediaPlayer, Rect)):
    /// the frame-export path (ScreenToGif transition rendering) lands here.
    /// The session has no decode pipeline, so there are no decoded pixels:
    /// an open player paints a deterministic dark video surface and a
    /// closed player paints nothing (like WPF painting no frame).
    /// </summary>
    public void DrawVideo(MediaPlayer player, Rect rectangle)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.Source is null) return;
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
        DrawRectangle((float)rectangle.X, (float)rectangle.Y, (float)rectangle.Width, (float)rectangle.Height, 0x000000FFu);
    }

    /// <summary>Re-emits one recorded command (container replay path).</summary>
    internal void Append(DrawCommand command) => _commands.Add(command);

    public void PushClip(float x, float y, float w, float h) =>
        _commands.Add(new DrawCommand.PushClip(x, y, w, h));

    public void PopClip() => _commands.Add(new DrawCommand.PopClip());

    public void PushTransform(float m11, float m12, float m21, float m22, float dx, float dy) =>
        _commands.Add(new DrawCommand.PushTransform(m11, m12, m21, m22, dx, dy));

    public void PopTransform() => _commands.Add(new DrawCommand.PopTransform());
}

/// <summary>
/// MediaContext: layout pass calls Render(root) then CommitChannel replays
/// on the next frame tick (single-threaded: no render thread).
/// </summary>
public sealed class WasmMilCore
{
    private readonly ISharedGraphicsSurface _surface;

    public WasmMilCore(ISharedGraphicsSurface surface) => _surface = surface;

    public IReadOnlyList<DrawCommand> Render(UIElement root, WpfSize available, WpfRect arranged)
    {
        ArgumentNullException.ThrowIfNull(root);
        root.Measure(available);
        root.Arrange(arranged);
        var dc = new DrawingContext();
        UIElement.RenderVisible(dc, root);
        RenderOverlays(root, dc);
        return dc.Commands;
    }

    /// <summary>
    /// Headless render for WinForms-side hosts (ElementHost): measure, arrange,
    /// and record without a surface. The caller commits or exports the commands.
    /// </summary>
    public static IReadOnlyList<DrawCommand> RenderHeadless(UIElement root, WpfSize size)
    {
        ArgumentNullException.ThrowIfNull(root);
        root.Measure(size);
        root.Arrange(new WpfRect(0, 0, size.Width, size.Height));
        var dc = new DrawingContext();
        UIElement.RenderVisible(dc, root);
        RenderOverlays(root, dc);
        return dc.Commands;
    }

    /// <summary>
    /// Overlay post-pass: owned overlays (open menu drop-downs) paint after
    /// the whole tree, above later siblings, unclipped by ancestors —
    /// approximating real popup z-order without a separate visual layer.
    /// </summary>
    private static void RenderOverlays(UIElement el, DrawingContext dc)
    {
        if (el.Visibility != Visibility.Visible) return;
        if (el is Menu menu) menu.RenderOpenPanels(dc);
        if (el is ToolTip tip) tip.RenderOverlay(dc);
        if (el is FrameworkElement fe)
            foreach (var child in fe.LogicalChildren)
                RenderOverlays(child, dc);
    }

    public void CommitChannel(IReadOnlyList<DrawCommand> commands) => _surface.Replay(commands);

    /// <summary>Frame export for the Canvas2D JSON fallback transport.</summary>
    public static string ExportFrameJson(IReadOnlyList<DrawCommand> commands) =>
        DrawCommandJson.Serialize(commands);
}

