namespace DesktopWasm.Catalog.Tests;

/// <summary>The shell renders one menu from this projection: no second list.</summary>
public sealed class MenuTests
{
    [Fact]
    public void MenuIsSingleSortedListWithLaunchFlags()
    {
        var registry = new AppRegistry(
            [Fx.Available("fixture-b"), Fx.Descriptor("fixture-a"), Fx.Available("fixture-c")]);
        var menu = registry.Menu();
        Assert.Equal(3, menu.Count);
        Assert.Equal(["fixture-a", "fixture-b", "fixture-c"], menu.Select(m => m.Id));
        Assert.False(menu[0].IsLaunchable);
        Assert.True(menu[1].IsLaunchable);
        foreach (var item in menu)
            Assert.Equal(item.Id, registry.Resolve(item.Id).Id);
    }

    [Fact]
    public void GeneratedMenuCoversBothFamiliesOnce()
    {
        var registry = CatalogSerializer.Deserialize(
            File.ReadAllText(Path.Combine(Fx.CatalogDir(), "apps.json")));
        var menu = registry.Menu();
        Assert.Equal(registry.Count, menu.Count);
        Assert.Equal(menu.Count, menu.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(menu, m => m.Capabilities == DesktopWasm.AppModel.AppCapability.WinForms);
        Assert.Contains(menu, m => m.Capabilities == DesktopWasm.AppModel.AppCapability.Wpf);
        Assert.Contains(menu, m => m.Capabilities == DesktopWasm.AppModel.AppCapability.Mixed);
    }
}
