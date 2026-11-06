using DesktopWasm.Core;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Gallery-tree header-press routing repro (WPF-D6B-2327).
/// Boots the real converted gallery package in-process (same pattern
/// as DataGridGalleryAutoColumnsTests), navigates the served card
/// path, lays out at the served viewport (1400x1000), and presses the
/// ProductName header band through the real pointer path. Served w20
/// shows 0.0px/order-unchanged with the press never reaching the grid
/// handler (pressCount proof); the managed data path is GREEN
/// (DataGridHeaderSortTests). Red here isolates the misroute to the
/// managed gallery tree; green bounds it to served-only delivery.
/// No browser needed: the served tree is reachable in-process.</summary>
[ResetAmbientState]
public sealed class DataGridGalleryHeaderRoutingTests
{
    private static readonly WasmWindowHandle Handle = new(1);

    private sealed record GalleryBoot(WasmMilCore Mil, FrameworkElement Window, DesktopWasm.Wpf.DataGrid Grid);

    private static GalleryBoot? s_boot;
    private static readonly object s_bootLock = new();

    // The gallery package boots once per process: a second boot in the same
    // process finds the navigation card's Command null (harness-ambient gap,
    // wpf-lane-owned follow-up, not press delivery), so every fact shares
    // one boot and resets grid state through public API instead.
    private static async Task<GalleryBoot> BootOnceAsync()
    {
        lock (s_bootLock)
        {
            if (s_boot is not null) return s_boot;
        }
        var dir = @"E:\AI\Code\WPF-wasm\src\DesktopWasm.BrowserHost\wwwroot\apps\wpf-gallery";
        var json = File.ReadAllText(System.IO.Path.Combine(dir, "app-manifest.json"));
        var package = JsonSerializer.Deserialize<ApplicationPackage>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.NotNull(package);
        using var loader = await ApplicationPackageLoader.CreateAsync(package!, "wpf-gallery",
            file => Task.FromResult(File.ReadAllBytes(System.IO.Path.Combine(dir, file))));
        var window = loader.Boot();
        Assert.NotNull(window);
        window.Width = 1400;
        window.Height = 1000;

        // Served card path: find the DataGrid navigation card and run it.
        // A converted entry point runs once per process (like real WPF:
        // Application.Current and app statics are process-wide), so a
        // second package boot in the same process reuses the first boot's
        // navigation state: its frame may already show the page (reuse it)
        // or its menu may no longer offer the card (loud fail with the
        // exact state; harness-ambient gap, wpf-lane-owned follow-up).
        var frame = FindVisual<Frame>(window.Content as FrameworkElement);
        Assert.NotNull(frame);
        FrameworkElement? page = frame.Content as FrameworkElement;
        if (page?.GetType().FullName != "WPFGallery.Views.DataGridPage")
        {
            var card = FindVisuals<Button>(frame.Content as FrameworkElement)
                .FirstOrDefault(b => (b.CommandParameter?.ToString() ?? "").Contains("DataGridPage"));
            Assert.NotNull(card);
            Assert.NotNull(card.Command);
            card.Command.Execute(card.CommandParameter);
            page = frame.Content as FrameworkElement;
        }
        Assert.Equal("WPFGallery.Views.DataGridPage", page?.GetType().FullName);
        var grid = Assert.IsType<DesktopWasm.Wpf.DataGrid>(page!.FindName("SampleDataGrid"));

        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(1400, 1000));
        mil.Render(window, new WpfSize(1400, 1000), new WpfRect(0, 0, 1400, 1000));
        Assert.True(grid.RowRects.Count > 1);
        lock (s_bootLock)
        {
            s_boot ??= new GalleryBoot(mil, window, grid);
            return s_boot;
        }
    }

    // Fresh press path per fact (router/manager carry per-press state) over
    // the shared boot, with grid sort/selection state reset so facts stay
    // order-independent. Geometry is re-rendered after reset: sort state
    // never moves slots, but the render keeps paint/hit in the same pass.
    private static async Task<(WasmInputRouter Router, DesktopWasm.Wpf.DataGrid Grid)> Harness()
    {
        var boot = await BootOnceAsync();
        boot.Grid.Items.SortDescriptions.Clear();
        foreach (var column in boot.Grid.Columns) column.SortDirection = null;
        boot.Grid.SelectedIndex = -1;
        boot.Mil.Render(boot.Window, new WpfSize(1400, 1000), new WpfRect(0, 0, 1400, 1000));
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        manager.AddRoot(boot.Window);
        return (router, boot.Grid);
    }

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static double ColumnCenterX(DesktopWasm.Wpf.DataGrid grid, string header)
    {
        var index = grid.Columns.ToList().FindIndex(c => Equals(c.Header, header));
        Assert.True(index >= 0);
        var cx = grid.RenderRect.X + 1;
        for (var i = 0; i < index; i++) cx += grid.ColumnWidths[i];
        return cx + grid.ColumnWidths[index] / 2;
    }

    private static T? FindVisual<T>(FrameworkElement? root) where T : FrameworkElement
    {
        if (root is null) return null;
        if (root is T match) return match;
        foreach (var child in root.LogicalChildren)
        {
            var found = FindVisual<T>(child as FrameworkElement);
            if (found is not null) return found;
        }
        return null;
    }

    private static List<T> FindVisuals<T>(FrameworkElement? root) where T : FrameworkElement
    {
        var found = new List<T>();
        CollectVisuals(root, found);
        return found;
    }

    private static void CollectVisuals<T>(FrameworkElement? root, List<T> found) where T : FrameworkElement
    {
        if (root is null) return;
        if (root is T match) found.Add(match);
        foreach (var child in root.LogicalChildren) CollectVisuals(child as FrameworkElement, found);
    }

    [Fact]
    public async Task Gallery_header_press_sorts_the_rendered_view()
    {
        var (router, grid) = await Harness();

        // Select the second row first: after an ascending ProductName
        // sort it must stay selected while sliding to its sorted slot.
        var rows = grid.RowRects;
        Down(router, grid.RenderRect.X + 60, rows[1].Y + rows[1].Height / 2);
        var selected = grid.SelectedItem;
        Assert.NotNull(selected);

        // Press the ProductName header band through the router.
        var headerY = rows[0].Y - 2;
        Assert.True(headerY > grid.RenderRect.Y + 1);
        Down(router, ColumnCenterX(grid, "ProductName"), headerY);

        // The press must reach the data path...
        var descriptions = grid.Items.SortDescriptions;
        Assert.Single(descriptions);
        Assert.Equal("ProductName", descriptions[0].PropertyName);
        Assert.Equal(ListSortDirection.Ascending, descriptions[0].Direction);

        // ...and the rendered view must follow with identity preserved.
        var ordered = grid.Items.Cast<object?>()
            .OrderBy(it => grid.CellText(it, grid.Columns
                .OfType<DataGridTextColumn>()
                .First(c => Equals(c.Header, "ProductName"))),
                StringComparer.Ordinal).ToList();
        var first = Assert.IsType<DataGridRow>(
            grid.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Same(ordered[0], first.Item);
        Assert.Same(selected, grid.SelectedItem);
        Assert.Equal(ordered.IndexOf(selected), grid.SelectedIndex);
    }

    // RT-ENGINE-2344 (engine spec item 2): the painted header band must
    // sort at its top, middle and bottom edges through the real pointer
    // path. Band edges are computed from the arranged tree (band top =
    // grid top + 1px chrome, band bottom = first row top), mirroring the
    // served y-ladder (D6B: sort applies at the band bottom edge).
    [Fact]
    public async Task Gallery_header_band_top_sorts()
    {
        var (router, grid) = await Harness();
        Down(router, ColumnCenterX(grid, "ProductName"), grid.RenderRect.Y + 2);
        var descriptions = grid.Items.SortDescriptions;
        Assert.Single(descriptions);
        Assert.Equal("ProductName", descriptions[0].PropertyName);
        Assert.Equal(ListSortDirection.Ascending, descriptions[0].Direction);
    }

    [Fact]
    public async Task Gallery_header_band_mid_sorts()
    {
        var (router, grid) = await Harness();
        var top = grid.RenderRect.Y + 1;
        var bottom = grid.RowRects[0].Y;
        Assert.True(bottom > top);
        Down(router, ColumnCenterX(grid, "ProductName"), (top + bottom) / 2);
        var descriptions = grid.Items.SortDescriptions;
        Assert.Single(descriptions);
        Assert.Equal("ProductName", descriptions[0].PropertyName);
        Assert.Equal(ListSortDirection.Ascending, descriptions[0].Direction);
    }

    [Fact]
    public async Task Gallery_header_band_bottom_sorts()
    {
        var (router, grid) = await Harness();
        Down(router, ColumnCenterX(grid, "ProductName"), grid.RowRects[0].Y - 1);
        var descriptions = grid.Items.SortDescriptions;
        Assert.Single(descriptions);
        Assert.Equal("ProductName", descriptions[0].PropertyName);
        Assert.Equal(ListSortDirection.Ascending, descriptions[0].Direction);
    }

    // Served dead-zone mirror (D6B map: presses above the grid reach no
    // handler). A press just above the grid top must neither sort nor
    // select through the real pointer path.
    [Fact]
    public async Task Gallery_dead_zone_above_grid_sorts_nothing()
    {
        var (router, grid) = await Harness();
        Down(router, ColumnCenterX(grid, "ProductName"), grid.RenderRect.Y - 4);
        Assert.Empty(grid.Items.SortDescriptions);
        Assert.Null(grid.SelectedItem);
    }

    // Row graze mirror (D6 W20 (952,285)->row-0 event): a press inside the
    // first row selects it without sorting.
    [Fact]
    public async Task Gallery_first_row_graze_selects_without_sorting()
    {
        var (router, grid) = await Harness();
        var rows = grid.RowRects;
        Down(router, ColumnCenterX(grid, "ProductName"), rows[0].Y + 2);
        Assert.Empty(grid.Items.SortDescriptions);
        Assert.NotNull(grid.SelectedItem);
    }
}
