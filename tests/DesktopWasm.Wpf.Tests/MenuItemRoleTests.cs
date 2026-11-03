namespace DesktopWasm.Wpf.Tests;

/// <summary>MenuItem.RoleProperty tests: readonly registration mirroring real WPF
/// (owner MenuItem, gate input identical to the staged MainWindow trigger
/// Property="MenuItem.Role"), store mirroring the computed Role across all four
/// kinds, and the SubmenuHeader trigger case from the real bytes.</summary>
[ResetAmbientState]
public sealed class MenuItemRoleTests
{
    [Fact]
    public void RoleProperty_registers_readonly_with_menuitem_owner()
    {
        var dp = DependencyProperty.FromName(typeof(MenuItem), "Role");
        Assert.NotNull(dp);
        Assert.Same(MenuItem.RoleProperty, dp);
        Assert.Equal("Role", dp.Name);
        Assert.Equal(typeof(MenuItemRole), dp.PropertyType);
        Assert.Equal(typeof(MenuItem), dp.OwnerType);
        Assert.True(dp.IsReadOnly);
    }

    [Fact]
    public void RoleProperty_rejects_direct_set()
    {
        var item = new MenuItem { Header = "File" };
        var ex = Assert.Throws<InvalidOperationException>(
            () => item.SetValue(MenuItem.RoleProperty, MenuItemRole.SubmenuHeader));
        Assert.Contains("read-only", ex.Message);
    }

    [Fact]
    public void Role_store_mirrors_computed_role_for_all_kinds()
    {
        // Detached leaf: TopLevelItem.
        var solo = new MenuItem { Header = "Solo" };
        Assert.Equal(MenuItemRole.TopLevelItem, solo.GetValue(MenuItem.RoleProperty));
        Assert.Equal(MenuItemRole.TopLevelItem, solo.Role);

        // Detached parent: TopLevelHeader.
        var parent = new MenuItem { Header = "Parent" };
        parent.AddChild(new MenuItem { Header = "Kid" });
        Assert.Equal(MenuItemRole.TopLevelHeader, parent.GetValue(MenuItem.RoleProperty));

        // Attached tree: File=TopLevelHeader, Recent=SubmenuHeader, Doc=SubmenuItem.
        var menu = new Menu();
        var file = new MenuItem { Header = "File" };
        var recent = new MenuItem { Header = "Recent" };
        var doc = new MenuItem { Header = "Doc1" };
        menu.AddChild(file);
        file.AddChild(recent);
        recent.AddChild(doc);
        Assert.Equal(MenuItemRole.TopLevelHeader, file.GetValue(MenuItem.RoleProperty));
        Assert.Equal(MenuItemRole.TopLevelHeader, file.Role);
        Assert.Equal(MenuItemRole.SubmenuHeader, recent.GetValue(MenuItem.RoleProperty));
        Assert.Equal(MenuItemRole.SubmenuHeader, recent.Role);
        Assert.Equal(MenuItemRole.SubmenuItem, doc.GetValue(MenuItem.RoleProperty));
        Assert.Equal(MenuItemRole.SubmenuItem, doc.Role);

        // Removal flips back through the RemoveChild hook.
        recent.RemoveChild(doc);
        Assert.Equal(MenuItemRole.SubmenuItem, recent.GetValue(MenuItem.RoleProperty));
        Assert.Equal(MenuItemRole.SubmenuItem, recent.Role);
        recent.AddChild(doc);
        Assert.Equal(MenuItemRole.SubmenuHeader, recent.GetValue(MenuItem.RoleProperty));
    }

    [Fact]
    public void SubmenuHeader_trigger_applies_from_implicit_style()
    {
        const string xaml =
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Menu>" +
            "<Menu.Resources><ResourceDictionary>" +
            "<Style TargetType=\"{x:Type MenuItem}\">" +
            "<Style.Triggers>" +
            "<Trigger Property=\"MenuItem.Role\" Value=\"SubmenuHeader\">" +
            "<Setter Property=\"Background\" Value=\"Red\" />" +
            "</Trigger>" +
            "</Style.Triggers>" +
            "</Style>" +
            "</ResourceDictionary></Menu.Resources>" +
            "<MenuItem Header=\"File\"><MenuItem Header=\"Recent\"><MenuItem Header=\"Doc1\" /></MenuItem></MenuItem>" +
            "</Menu></Window>";
        var window = (Window)XamlLoader.Load(xaml).Root;
        var menu = Assert.IsType<Menu>(window.Content);
        var file = Assert.Single(menu.Items);
        var recent = Assert.Single(file.Items);
        var doc = Assert.Single(recent.Items);

        var red = new SolidColorBrush(Colors.Red).ToRgba();
        Assert.Equal(red, ((SolidColorBrush)recent.Background!).ToRgba());
        Assert.True(file.Background is not SolidColorBrush fileBrush || fileBrush.ToRgba() != red);
        Assert.True(doc.Background is not SolidColorBrush docBrush || docBrush.ToRgba() != red);
    }
}
