using System.Text.Json;

namespace DesktopWasm.Graphics;

/// <summary>
/// Pure JSON codec for the Canvas2D fallback transport.
/// Op numbers match WfWasm.CanvasHost.CanvasBridge: 1 fillRect, 2 roundRect,
/// 3 fillText, 4 gradient, 5 line, 6 ellipse, 7 drawImage, 8 defBitmap,
/// 9 blitBitmap, 10 retainLayers, 11 patchBitmap. WPF-only ops use 12+.
/// Unit-testable without a JS runtime.
/// </summary>
public static class DrawCommandJson
{
    public static string Css(uint rgba) => $"#{rgba:x8}";

    public static string Serialize(IReadOnlyList<DrawCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var ops = new List<object?[]>(commands.Count);
        foreach (var c in commands)
        {
            switch (c)
            {
                case DrawCommand.FillRect r: ops.Add([1, r.X, r.Y, r.W, r.H, Css(r.Rgba)]); break;
                case DrawCommand.FillRoundedRect r: ops.Add([2, r.X, r.Y, r.W, r.H, r.Radius, Css(r.Rgba)]); break;
                case DrawCommand.DrawText t: ops.Add([3, t.Text, t.X, t.Y, t.FontCss, Css(t.Rgba), t.HAlign, t.VAlign, t.MnemonicIndex]); break;
                case DrawCommand.FillGradient g: ops.Add([4, g.X, g.Y, g.W, g.H, Css(g.TopRgba), Css(g.BottomRgba)]); break;
                case DrawCommand.DrawLine l: ops.Add([5, l.X1, l.Y1, l.X2, l.Y2, l.Width, Css(l.Rgba)]); break;
                case DrawCommand.DrawEllipse e: ops.Add([6, e.X, e.Y, e.W, e.H, e.LineWidth, Css(e.OutlineRgba), e.FillRgba.HasValue ? Css(e.FillRgba.Value) : null]); break;
                case DrawCommand.DrawImage i: ops.Add([7, i.X, i.Y, i.DW, i.DH, i.Width, i.Height, Convert.ToBase64String(i.Rgba)]); break;
                case DrawCommand.DefBitmap d: ops.Add([8, d.LayerId, d.Width, d.Height]); break;
                case DrawCommand.BlitBitmap b: ops.Add([9, b.LayerId, b.X, b.Y, b.DW, b.DH]); break;
                case DrawCommand.RetainLayers r: ops.Add([10, r.LiveIds]); break;
                case DrawCommand.PatchBitmap p: ops.Add([11, p.LayerId, p.X, p.Y, p.Width, p.Height]); break;
                case DrawCommand.PushClip cl: ops.Add([12, cl.X, cl.Y, cl.W, cl.H]); break;
                case DrawCommand.PopClip: ops.Add([13]); break;
                case DrawCommand.PushTransform t: ops.Add([14, t.M11, t.M12, t.M21, t.M22, t.Dx, t.Dy]); break;
                case DrawCommand.PopTransform: ops.Add([15]); break;
                case DrawCommand.DrawGeometry g: ops.Add([16, g.PathData, Css(g.FillRgba), g.StrokeRgba.HasValue ? Css(g.StrokeRgba.Value) : null, g.StrokeWidth, g.FillRule]); break;
            }
        }

        // Canvas and Skia ignore non-finite operands, but JSON cannot
        // represent them: sanitize poisoned geometry (unmeasured subtrees)
        // to texture-safe bounds. Bounds must stay below real allocation
        // sizes: extreme sentinels (1e30) become multi-GB canvas buffers
        // and OOM the runtime on scroll repaints.
        foreach (var op in ops)
            for (var i = 0; i < op.Length; i++)
            {
                if (op[i] is float f && !float.IsFinite(f))
                    op[i] = f > 0 ? 16383f : f < 0 ? -16383f : 0f;
                else if (op[i] is double d && !double.IsFinite(d))
                    op[i] = d > 0 ? 16383.0 : d < 0 ? -16383.0 : 0.0;
            }

        return JsonSerializer.Serialize(ops);
    }

    /// <summary>Splits binary uploads out of a frame (defs/patches travel out-of-band).</summary>
    public static (List<DrawCommand> Json, List<DrawCommand> Uploads) SplitUploads(IReadOnlyList<DrawCommand> commands)
    {
        var json = new List<DrawCommand>(commands.Count);
        var uploads = new List<DrawCommand>();
        foreach (var c in commands)
        {
            if (c is DrawCommand.DefBitmap or DrawCommand.PatchBitmap) uploads.Add(c);
            else json.Add(c);
        }

        return (json, uploads);
    }
}
