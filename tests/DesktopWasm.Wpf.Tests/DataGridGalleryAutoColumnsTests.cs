using System.Reflection;
using System.Text.Json;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Gallery-package pin for auto-generated columns: the converted
/// gallery DataGridPage binds ItemsSource with no explicit columns (genuine
/// WPF auto-generates). Boots the real converted package in-process,
/// navigates to the sample page, and proves rows mint one text column per
/// readable property. Served empty-grid regressions point here first.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class DataGridGalleryAutoColumnsTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public void Dispose() => Application.Current = _previous;

    [Fact]
    public async Task GalleryDataGridPage_mints_columns_for_bound_products()
    {
        var dir = @"E:\AI\Code\WPF-wasm\src\DesktopWasm.BrowserHost\wwwroot\apps\wpf-gallery";
        var json = File.ReadAllText(System.IO.Path.Combine(dir, "app-manifest.json"));
        var package = JsonSerializer.Deserialize<ApplicationPackage>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.NotNull(package);
        using var loader = await ApplicationPackageLoader.CreateAsync(package!, "wpf-gallery",
            file => Task.FromResult(File.ReadAllBytes(System.IO.Path.Combine(dir, file))));
        var window = loader.Boot();
        Assert.NotNull(window);

        var frame = FindVisual<Frame>(window.Content as FrameworkElement);
        Assert.NotNull(frame);
        var card = FindVisuals<Button>(frame.Content as FrameworkElement)
            .FirstOrDefault(b => (b.CommandParameter?.ToString() ?? "").Contains("DataGridPage"));
        Assert.NotNull(card);
        Assert.NotNull(card.Command);
        card.Command.Execute(card.CommandParameter);

        var page = frame.Content as FrameworkElement;
        Assert.Equal("WPFGallery.Views.DataGridPage", page?.GetType().FullName);
        var grid = Assert.IsType<DataGrid>(page!.FindName("SampleDataGrid"));
        Assert.NotEmpty(grid.Items);
        var firstType = grid.Items[0]!.GetType();
        var expected = firstType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p => p.Name)
            .ToList();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, grid.Columns.Select(c => c.Header).ToList());
        Assert.Equal(expected, grid.Columns.Select(c => c.BindingPath).ToList());
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
}
