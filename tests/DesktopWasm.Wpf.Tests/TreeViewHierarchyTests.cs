using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

// Regression cover for the real WPF Gallery navigation protocol
// (MainWindow.xaml.cs OnNavigating / ControlsList_SelectedItemChanged):
// nested ControlInfoDataItem entries must resolve through the root tree
// generator in both directions, and selecting a nested data item must
// round-trip back to the same data item.
[ResetAmbientState]
public sealed class TreeViewHierarchyTests
{
    private sealed class NavItem
    {
        public string Title { get; set; } = "";
        public List<NavItem> Items { get; } = new();
        public override string ToString() => Title;
    }

    private const string TemplateNs =
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private static TreeView BuildTree(out NavItem basic, out NavItem button)
    {
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary " + TemplateNs + ">" +
            "<HierarchicalDataTemplate x:Key=\"Nav\" ItemsSource=\"{Binding Items}\"><Grid /></HierarchicalDataTemplate>" +
            "</ResourceDictionary>");
        basic = new NavItem { Title = "Basic Input" };
        button = new NavItem { Title = "Button" };
        basic.Items.Add(button);
        var other = new NavItem { Title = "Layout" };
        var tree = new TreeView
        {
            ItemTemplate = Assert.IsType<HierarchicalDataTemplate>(dict["Nav"]),
        };
        tree.ItemsSource = new List<NavItem> { basic, other };
        return tree;
    }

    [Fact]
    public void Nested_data_resolves_to_realized_node_through_root_generator()
    {
        var tree = BuildTree(out var basic, out var button);
        var gen = tree.ItemContainerGenerator;
        var basicNode = Assert.IsType<TreeViewItem>(gen.ContainerFromItem(basic));
        var buttonNode = Assert.IsType<TreeViewItem>(gen.ContainerFromItem(button));
        Assert.Same(buttonNode, Assert.IsType<TreeViewItem>(
            basicNode.ItemContainerGenerator.ContainerFromItem(button)));
        Assert.Contains(buttonNode, basicNode.Items);
    }

    [Fact]
    public void Nested_node_resolves_back_to_data_through_root_generator()
    {
        var tree = BuildTree(out _, out var button);
        var gen = tree.ItemContainerGenerator;
        var buttonNode = Assert.IsType<TreeViewItem>(gen.ContainerFromItem(button));
        Assert.Same(button, gen.ItemFromContainer(buttonNode));
    }

    [Fact]
    public void Nested_data_selection_round_trips_to_same_data()
    {
        var tree = BuildTree(out _, out var button);
        tree.SelectedItem = button;
        Assert.Same(button, tree.SelectedItem);
    }

    [Fact]
    public void Navigating_walk_expands_each_level_without_strays()
    {
        var tree = BuildTree(out var basic, out var button);
        var rootsBefore = tree.Items.Count;
        ItemsControl level = tree;
        TreeViewItem? selected = null;
        foreach (var data in new[] { basic, button })
        {
            var tvi = Assert.IsType<TreeViewItem>(level.ItemContainerGenerator.ContainerFromItem(data));
            tvi.IsExpanded = true;
            tvi.UpdateLayout();
            level = tvi;
            selected = tvi;
        }
        Assert.NotNull(selected);
        selected!.IsSelected = true;
        Assert.Same(button, tree.SelectedItem);
        Assert.Equal(rootsBefore, tree.Items.Count);
    }
}
