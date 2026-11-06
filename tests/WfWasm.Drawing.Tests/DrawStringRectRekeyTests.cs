// WC1541: DS-RECT rekey. The [DS-RECT] dedup keyed on text preview+rect
// with a 64-cap: dynamic texts (counters, clocks) mint a fresh key per
// distinct preview and exhaust the cap mid-session, silencing the log
// process-wide (same failure the LABEL-RECT key fixed via
// type+name+rect+font in WC1307). These pins drive 70 same-shape draws
// with distinct previews, then a new shape: pre-rekey the new shape is
// dropped (cap exhausted); post-rekey the shape key collapses and the
// new shape still logs. FAILS until the rekey lands; do not weaken.
extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfFont = WfDraw::System.Drawing.Font;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfSolidBrush = WfDraw::System.Drawing.SolidBrush;
using WfBrowserText = WfDraw::System.Drawing.BrowserText;

namespace WfWasm.Drawing.Tests;

public sealed class DrawStringRectRekeyTests
{
    private static WfBrowserText.Raster Solid10x8()
    {
        byte[] rgba = new byte[10 * 8 * 4];
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        return new WfBrowserText.Raster(10, 8, rgba);
    }

    private static void ResetRectDedup()
    {
        var t = typeof(WfGraphics);
        var set = t.GetField("s_dsRectLogged",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var count = t.GetField("s_dsRectLoggedCount",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(set);
        Assert.NotNull(count);
        ((System.Collections.Generic.HashSet<string>)set!.GetValue(null)!).Clear();
        count!.SetValue(null, 0);
    }

    private static string DriveShapes(int sameShapeCount)
    {
        var sb = new System.Text.StringBuilder();
        var old = System.Console.Out;
        System.Console.SetOut(new System.IO.StringWriter(sb));
        WfBrowserText.RasterOverride = (_, _, _, _) => Solid10x8();
        try
        {
            ResetRectDedup();
            using var bmp = new WfBitmap(800, 60);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using var brush = new WfSolidBrush(WfColor.Red);
            using var font = new WfFont("Segoe UI", 9f);
            var shapeA = new WfRectangleF(10, 10, 80, 20);
            for (int i = 0; i < sameShapeCount; i++)
                g.DrawString($"rekey-probe-{i:000}", font, brush, shapeA, null);
            g.DrawString("rekey-new-shape", font, brush, new WfRectangleF(600, 10, 80, 20), null);
            return sb.ToString();
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
            System.Console.SetOut(old);
            ResetRectDedup();
        }
    }

    private static int CountLines(string log, string needle)
    {
        int n = 0;
        foreach (string line in log.Split('\n'))
            if (line.Contains("[DS-RECT]") && line.Contains(needle))
                n++;
        return n;
    }

    [Fact]
    public void Same_Shape_Distinct_Previews_Collapse_To_One_Line()
    {
        string log = DriveShapes(70);
        Assert.Equal(1, CountLines(log, "10,10 80x20"));
    }

    [Fact]
    public void New_Shape_Still_Logs_After_Many_Same_Shape_Draws()
    {
        string log = DriveShapes(70);
        Assert.Equal(1, CountLines(log, "600,10 80x20"));
    }
}
