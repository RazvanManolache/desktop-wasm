namespace DesktopWasm.Wpf.Tests;

/// <summary>ListView.View: the gallery ListViewPage declares
/// &lt;ListView.View&gt;&lt;GridView&gt; with GridViewColumn children
/// (ListViewPage.xaml lines 144-160). The engine carries GridView,
/// GridViewColumn and the ListView.View DP, but the loader has no
/// View property-element branch, so the original page fails on
/// "Unknown element &lt;GridView&gt;". Parse-and-retain is the honest
/// minimum (column rendering stays a documented follow-up).</summary>
[ResetAmbientState]
public sealed class ListViewViewTests
{
    [Fact]
    public void View_property_element_parses_gridview_columns()
    {
        var loaded = XamlLoader.Load("""
            <UserControl><ListView Name="V"><ListView.View><GridView><GridViewColumn Header="First Name" Width="150" DisplayMemberBinding="{Binding FirstName}" /><GridViewColumn Header="Last Name" Width="150" DisplayMemberBinding="{Binding LastName}" /></GridView></ListView.View></ListView></UserControl>
            """);

        var view = Assert.IsType<ListView>(loaded.Names["V"]).View;
        var grid = Assert.IsType<GridView>(view);
        Assert.Equal(2, grid.Columns.Count);

        Assert.Equal("First Name", grid.Columns[0].Header);
        Assert.Equal(150, grid.Columns[0].Width);
        var first = Assert.IsType<Binding>(grid.Columns[0].DisplayMemberBinding);
        Assert.Equal("FirstName", first.Path.Path);

        Assert.Equal("Last Name", grid.Columns[1].Header);
        var last = Assert.IsType<Binding>(grid.Columns[1].DisplayMemberBinding);
        Assert.Equal("LastName", last.Path.Path);
    }

    [Fact]
    public void View_rejects_non_gridview_child()
    {
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("""
            <UserControl><ListView><ListView.View><StackPanel /></ListView.View></ListView></UserControl>
            """));
        Assert.Contains("GridView", ex.Message);
    }
}
