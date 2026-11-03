using DesktopWasm.Core;
using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ComboBox parity: hovering a drop row paints a hover highlight
/// (distinct from the selection highlight), so pointer position is visible
/// even with no selection.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDropHoverPaintTests
{
    private static (WasmInputRouter Router, ComboBox Box, Window Root) Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var combo = new ComboBox { Width = 400 };
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        combo.AddItem(new ComboBoxItem { Content = "Red" });
        root.Content = combo;
        manager.AddRoot(root);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        return (router, combo, root);
    }

    private static bool IsHoverPixel(SKColor pixel) =>
        pixel.Red >= 180 && pixel.Red <= 230 && pixel.Green >= 200 && pixel.Blue >= 250;

    [Fact]
    public void Hovering_row_paints_hover_highlight()
    {
        var (router, combo, root) = Harness();
        combo.IsDropDownOpen = true;
        var handle = new WasmWindowHandle(1);
        var dropTop = combo.RenderRect.Y + 30;

        router.DispatchPointer(new WasmPointerArgs(handle, combo.RenderRect.X + 20, dropTop + 1 * 24 + 12, WasmPointerButton.Left, false)
            { Kind = WasmPointerKind.Move });

        using var surface = new SkiaSurface(800, 600);
        var mil = new WasmMilCore(surface);
        var commands = mil.Render(root, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        mil.CommitChannel(commands);
        using var bitmap = SKBitmap.FromImage(surface.Snapshot());

        var xi = (int)(combo.RenderRect.X + 5);
        var found = -1;
        for (var y = (int)dropTop; y < dropTop + 3 * 24 && found < 0; y++)
            if (IsHoverPixel(bitmap.GetPixel(xi, y))) found = y;
        Assert.True(found >= 0, "hovered drop row must paint a hover highlight");
        Assert.Equal((int)dropTop + 24, found);
    }
}
