using DesktopWasm.Core;
using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ComboBox parity: the open drop scrolls the selection into view
/// (a 20-row drop shows ~8 rows; moving selection past the bottom must
/// scroll so the highlight stays visible, like the list BringIntoView).
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropScrollTests
{
    private static (ComboBox Box, Window Root) Harness(int count = 20)
    {
        var root = new Window();
        var combo = new ComboBox { Width = 400 };
        for (var i = 0; i < count; i++) combo.AddItem(new ComboBoxItem { Content = $"C{i}" });
        root.Content = combo;
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        return (combo, root);
    }

    private static int HighlightTop(SKBitmap bitmap, double x, double dropTop, double dropHeight)
    {
        var xi = Math.Clamp((int)x, 0, bitmap.Width - 1);
        var top = (int)dropTop;
        var bottom = Math.Min(bitmap.Height - 1, (int)(dropTop + dropHeight));
        for (var y = top; y <= bottom; y++)
        {
            var pixel = bitmap.GetPixel(xi, y);
            if (pixel.Blue > 150 && pixel.Red < 150) return y;
        }
        return -1;
    }

    [Fact]
    public void Paging_past_the_visible_drop_scrolls_the_highlight()
    {
        var (combo, root) = Harness();
        combo.IsDropDownOpen = true;
        combo.SelectedIndex = 12;

        using var surface = new SkiaSurface(800, 600);
        var mil = new WasmMilCore(surface);
        var commands = mil.Render(root, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        mil.CommitChannel(commands);
        using var bitmap = SKBitmap.FromImage(surface.Snapshot());

        // Drop geometry mirrors the control (header 30px, rows 24px): with
        // 20 rows the 200px drop shows 8, so index 12 scrolls first-visible
        // to 5 and the highlight sits 7 rows down the drop.
        var dropTop = combo.RenderRect.Y + 30;
        var top = HighlightTop(bitmap, combo.RenderRect.X + 5, dropTop, 200);
        Assert.True(top >= 0, "open drop must paint the selection highlight");
        Assert.Equal((int)dropTop + 7 * 24, top);
    }
}
