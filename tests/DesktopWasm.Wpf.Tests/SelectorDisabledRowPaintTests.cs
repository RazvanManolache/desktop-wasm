using DesktopWasm.Core;
using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native parity: disabled rows paint gray text (ListBox rows and
/// ComboBox drop rows alike), never the full-ink normal/selected text.
/// </summary>
[ResetAmbientState]
public sealed class SelectorDisabledRowPaintTests
{
    private static SKBitmap Render(UIElement root)
    {
        var surface = new SkiaSurface(800, 600);
        var mil = new WasmMilCore(surface);
        var window = root as Window ?? throw new InvalidOperationException("harness roots are windows");
        var commands = mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        mil.CommitChannel(commands);
        return SKBitmap.FromImage(surface.Snapshot());
    }

    private static bool HasDarkText(SKBitmap map, WpfRect row)
    {
        for (var y = (int)(row.Y + row.Height / 2) - 7; y <= row.Y + row.Height / 2 + 7; y++)
            for (var x = (int)(row.X + 6); x < row.X + 120; x++)
            {
                var px = map.GetPixel(x, y);
                if (px.Red < 80 && px.Green < 80 && px.Blue < 80) return true;
            }
        return false;
    }

    private static bool HasGrayText(SKBitmap map, WpfRect row)
    {
        for (var y = (int)(row.Y + row.Height / 2) - 7; y <= row.Y + row.Height / 2 + 7; y++)
            for (var x = (int)(row.X + 6); x < row.X + 120; x++)
            {
                var px = map.GetPixel(x, y);
                if (px.Red is >= 90 and <= 160 && px.Green is >= 90 and <= 160 && px.Blue is >= 90 and <= 160)
                    return true;
            }
        return false;
    }

    [Fact]
    public void ListBox_disabled_row_paints_gray_text()
    {
        var root = new Window();
        var box = new ListBox();
        var enabled = new ListBoxItem { Content = "R0-enabled" };
        var disabled = new ListBoxItem { Content = "R1-disabled", IsEnabled = false };
        box.AddChild(enabled);
        box.AddChild(disabled);
        root.Content = box;
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));

        using var map = Render(root);

        Assert.True(HasDarkText(map, enabled.RenderRect), "enabled row must keep full-ink text");
        Assert.False(HasDarkText(map, disabled.RenderRect), "disabled row must not paint full-ink text");
        Assert.True(HasGrayText(map, disabled.RenderRect), "disabled row must paint gray text");
    }

    [Fact]
    public void Drop_disabled_row_paints_gray_text()
    {
        var root = new Window();
        var combo = new ComboBox { Width = 400 };
        combo.AddItem(new ComboBoxItem { Content = "Blue-enabled" });
        var disabled = new ComboBoxItem { Content = "Green-disabled", IsEnabled = false };
        combo.AddItem(disabled);
        root.Content = combo;
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        combo.IsDropDownOpen = true;

        using var map = Render(root);

        var dropTop = combo.RenderRect.Y + 30;
        var row = new WpfRect(combo.RenderRect.X, dropTop + 24, combo.RenderRect.Width, 24);
        Assert.False(HasDarkText(map, row), "disabled drop row must not paint full-ink text");
        Assert.True(HasGrayText(map, row), "disabled drop row must paint gray text");
    }
}
