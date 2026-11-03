using System.Collections.Generic;
using System.Windows.Forms;
using ICanvas = WfWasm.Shim.ICanvas;
using TextHAlign = WfWasm.Shim.TextHAlign;
using TextVAlign = WfWasm.Shim.TextVAlign;
using WfWasm.PdnCpu;
namespace WfWasm.CanvasHost;
public sealed partial class WinFormsAppHost
{
    private static bool TryRenderPortablePropertyControl(ICanvas canvas, Control c, int x, int y, int w, int h)
    {
        string? type = c.GetType().FullName;
        if (type == "PaintDotNet.Controls.PdnCheckBox")
        {
            uint ink = c.Enabled ? 0x222222ffu : 0x999999ffu;
            int top = y + Math.Max(0, (h - 13) / 2);
            canvas.FillRectangle(x, top, 13, 13, 0xffffffff);
            canvas.DrawLine(x, top, x + 13, top, 1, ink); canvas.DrawLine(x, top + 13, x + 13, top + 13, 1, ink);
            canvas.DrawLine(x, top, x, top + 13, 1, ink); canvas.DrawLine(x + 13, top, x + 13, top + 13, 1, ink);
            if (c.GetType().GetProperty("IsChecked")?.GetValue(c) is true)
            { canvas.DrawLine(x + 2, top + 6, x + 5, top + 10, 2, ink); canvas.DrawLine(x + 5, top + 10, x + 11, top + 3, 2, ink); }
            canvas.DrawTextAligned(c.Text.Replace("&", ""), x + 18, y + h / 2f, GetFontCss(c.Font), ink, TextHAlign.Left, TextVAlign.Middle);
            return true;
        }
        if (type is not ("PaintDotNet.Effects.Gpu.CurveControlOneChannel" or "PaintDotNet.Effects.Gpu.CurveControlRgb")) return false;
        var tables = CpuPaint.CurveTables(c); if (tables is null) return false;
        canvas.FillRectangle(x, y, w, h, 0xffffffff);
        for (int i = 0; i <= 4; i++)
        { canvas.DrawLine(x + i * (w - 1) / 4f, y, x + i * (w - 1) / 4f, y + h - 1, 1, 0xbbbbbbff); canvas.DrawLine(x, y + i * (h - 1) / 4f, x + w - 1, y + i * (h - 1) / 4f, 1, 0xbbbbbbff); }
        var points = c.GetType().GetProperty("ControlPoints")?.GetValue(c) as SortedList<int, int>[];
        for (int channel = 0; channel < tables.Length; channel++)
        {
            uint color = tables.Length == 1 ? 0x222222ffu : channel == 0 ? 0xcc0000ffu : channel == 1 ? 0x008000ffu : 0x0000ccffu;
            for (int i = 1; i < 256; i++) canvas.DrawLine(x + (i - 1) * (w - 1) / 255f, y + (255 - tables[channel][i - 1]) * (h - 1) / 255f, x + i * (w - 1) / 255f, y + (255 - tables[channel][i]) * (h - 1) / 255f, 2, color);
            if (points is not null)
                foreach (var point in points[channel])
                    canvas.DrawEllipse(x + point.Key * (w - 1) / 255f - 3, y + (255 - point.Value) * (h - 1) / 255f - 3, 6, 6, 1, color, 0xffffffff);
        }
        return true;
    }
}
