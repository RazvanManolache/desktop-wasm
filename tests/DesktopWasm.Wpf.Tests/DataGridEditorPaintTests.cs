using DesktopWasm.Core;
using SkiaSharp;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// WPF-HARNESS-1252-h7k2 task 2: managed editor-paint transient repro.
/// Served h2 shows F2-open/type/Escape-close changing zero canvas pixels
/// while edit-state counters advance correctly and commit/discard end-states
/// repaint. This test decides the scope in-process: F2 + typed text must
/// change the rendered pixels via WasmMilCore/SkiaSurface. Red (identical
/// pixels) reproduces a WPF invalidation gap; green (changed pixels) proves
/// the engine repaints and the served gap is host/upload-side.
/// </summary>
[ResetAmbientState]
public sealed class DataGridEditorPaintTests
{
    private sealed class Product
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
    }

    private static List<Product> Products(int count)
    {
        var rows = new List<Product>(count);
        for (var i = 0; i < count; i++)
            rows.Add(new Product { ProductId = i, ProductName = $"Red Marmalade {i}" });
        return rows;
    }

    private static byte[] Png(DesktopWasm.Graphics.SkiaSurface surface)
    {
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static readonly WasmWindowHandle Handle = new(1);

    [Fact]
    public void Typing_in_open_editor_repaints_cell_pixels()
    {
        var products = Products(10);
        var grid = new DataGrid { Width = 400 };
        var name = new DataGridTextColumn { Header = "ProductName", BindingPath = "ProductName", Width = GridLength.Pixel(200) };
        grid.AddColumn(name);
        grid.ItemsSource = products;

        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window { Content = grid };
        manager.AddRoot(root);
        using var surface = new DesktopWasm.Graphics.SkiaSurface(400, 600);
        var mil = new WasmMilCore(surface);
        var size = new WpfSize(400, 600);
        var frame = new WpfRect(0, 0, 400, 600);
        void Render() => mil.CommitChannel(mil.Render(root, size, frame));
        Render();

        // Press the row0 name cell through the real router (served path).
        // The grid paints rows itself from row rects (cell containers stay
        // unarranged holders), so aim by the row0 rect inside the 200px name
        // column, exactly like the pointer contract SelectAt/CellFromPoint.
        Assert.True(grid.RowRects.Count > 0, "grid laid out no rows");
        var rr = grid.RowRects[0];
        router.DispatchPointer(new WasmPointerArgs(Handle, rr.X + 60, rr.Y + rr.Height / 2, WasmPointerButton.Left, true));
        Render();
        Assert.True(grid.CurrentCell.IsValid,
            $"press missed: rowRect0={rr} sel={grid.SelectedIndex}");
        var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
        var cell = Assert.IsType<DataGridCell>(row.CellsPresenter.ContainerForColumn(name));

        // F2 opens the editor; snapshot the open-editor frame.
        router.DispatchKeyboard(new WasmKeyArgs(Handle, "F2", true, false, false, false));
        var editor = Assert.IsType<TextBox>(cell.Content);
        Render();
        var openBytes = Png(surface);

        // Keystrokes must reach the editor model (served parity: W9Red committed).
        router.DispatchKeyboard(new WasmKeyArgs(Handle, "Q", true, false, false, false));
        router.DispatchKeyboard(new WasmKeyArgs(Handle, "Q", true, false, false, false));
        Assert.Contains("QQ", editor.Text);
        Render();
        var typedBytes = Png(surface);

        // The typed text must repaint: identical pixels reproduces the served gap.
        Assert.NotEqual(openBytes, typedBytes);
    }
}
