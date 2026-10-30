using System.Collections.ObjectModel;
using System.Text.Json;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-35: ListBox ItemsSource — collection binding, container
/// generation, selection sync (shim on the legacy row model, not a port).
/// The real staged Views/Collections/ListBoxPage.xaml binds
/// ItemsSource="{Binding ViewModel.ListBoxItems, Mode=TwoWay}" with
/// SelectedIndex="2" over the converted ObservableCollection of 5 fonts.
/// ApplicationState: the staged-XAML test loads the converted gallery
/// assembly into the shared AppDomain (pack lookup scans loaded
/// assemblies), so it serializes with the other staged/pack tests.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ListBoxItemsSourceTests
{
    private sealed class Person
    {
        public string Name { get; set; } = string.Empty;
        public override string ToString() => Name;
    }

    [Fact]
    public void ItemsSource_bulk_strings_generate_text_rows()
    {
        var list = new ListBox();
        list.ItemsSource = new[] { "Blue", "Green", "Red" };
        Assert.Equal(3, list.Items.Count);
        Assert.Equal(["Blue", "Green", "Red"], list.Items.Select(i => i.Content));
        Assert.Equal(-1, list.SelectedIndex);
        Assert.Null(list.SelectedItem);
    }

    [Fact]
    public void ItemsSource_preset_index_materializes_with_single_fire()
    {
        var list = new ListBox { SelectedIndex = 2 };
        Assert.Equal(-1, list.SelectedIndex); // pending: nothing to select yet
        var fires = 0;
        list.SelectionChanged += () => fires++;
        list.ItemsSource = new[] { "a", "b", "c" };
        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal("c", list.SelectedItem?.Content);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void ItemsSource_listboxitem_data_is_own_container()
    {
        var first = new ListBoxItem { Content = "x" };
        var second = new ListBoxItem { Content = "y" };
        var list = new ListBox();
        list.ItemsSource = new[] { first, second };
        Assert.Same(first, list.Items[0]);
        Assert.Same(second, list.Items[1]);
        list.SelectedIndex = 1;
        Assert.Same(second, list.SelectedItem);
    }

    [Fact]
    public void ItemsSource_display_member_path_resolves_text()
    {
        var list = new ListBox { DisplayMemberPath = "Name" };
        list.ItemsSource = new[] { new Person { Name = "Ada" }, new Person { Name = "Grace" } };
        Assert.Equal(["Ada", "Grace"], list.Items.Select(i => i.Content));
        list.DisplayMemberPath = null;
        Assert.Equal(["Ada", "Grace"], list.Items.Select(i => i.Content));
    }

    [Fact]
    public void ItemsSource_live_collection_tracks_selection_by_data()
    {
        var source = new ObservableCollection<string> { "a", "b", "c" };
        var list = new ListBox { ItemsSource = source };
        var fires = 0;
        list.SelectionChanged += () => fires++;
        list.SelectedIndex = 1;
        Assert.Equal("b", list.SelectedItem?.Content);
        Assert.Equal(1, fires);

        source.Insert(0, "z"); // selection follows its data, like WPF
        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal("b", list.SelectedItem?.Content);
        Assert.Equal(2, fires); // index shifted under the same item: fires, like removing a row above

        source.Remove("b"); // selected row leaves: slides onto "c"
        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal("c", list.SelectedItem?.Content);
        Assert.Equal(3, fires);

        source.Clear(); // emptied: effective selection clears, raw index kept
        Assert.Equal(-1, list.SelectedIndex);
        Assert.Null(list.SelectedItem);
        Assert.Equal(4, fires);
    }

    [Fact]
    public void ItemsSource_clear_keeps_pending_index_like_removing_rows()
    {
        var list = new ListBox { ItemsSource = new[] { "a", "b", "c" } };
        list.SelectedIndex = 1;
        list.ItemsSource = null;
        Assert.Empty(list.Items);
        Assert.Equal(-1, list.SelectedIndex);
        list.ItemsSource = new[] { "x" };
        Assert.Equal(-1, list.SelectedIndex); // raw 1 out of range: still pending
        list.ItemsSource = new[] { "x", "y" };
        Assert.Equal(1, list.SelectedIndex);
        Assert.Equal("y", list.SelectedItem?.Content);
    }

    [Fact]
    public void ItemsSource_guards_mirror_wpf()
    {
        var direct = new ListBox();
        direct.AddChild(new ListBoxItem { Content = "x" });
        var ex = Assert.Throws<InvalidOperationException>(() => direct.ItemsSource = new[] { "y" });
        Assert.Contains("Items collection must be empty", ex.Message);
        Assert.Null(direct.ItemsSource);

        var bound = new ListBox { ItemsSource = new[] { "a" } };
        Assert.Throws<InvalidOperationException>(() => bound.AddChild(new ListBoxItem()));
        Assert.Throws<InvalidOperationException>(() => bound.RemoveChild(bound.Items[0]));
        Assert.Single(bound.Items); // rejected mutations change nothing
    }

    [Fact]
    public void ItemsSource_binding_pushes_collection_like_the_gallery_page()
    {
        var vm = new TestViewModel();
        var page = new StackPanel();
        var list = new ListBox { SelectedIndex = 2 };
        page.AddChild(list);
        page.DataContext = vm;
        list.SetBinding("ItemsSource", new Binding("ViewModel.ListBoxItems") { Mode = BindingMode.TwoWay });
        Assert.Equal(5, list.Items.Count);
        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal("Courier New", list.SelectedItem?.Content);

        vm.ViewModel.ListBoxItems.Add("Consolas");
        Assert.Equal(6, list.Items.Count);
        Assert.Equal("Consolas", list.Items[^1].Content);
        Assert.Equal(2, list.SelectedIndex); // unrelated edit keeps selection
    }

    [Fact]
    public void Xaml_items_source_needs_a_binding_literal_rejected()
    {
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <ListBox ItemsSource="nope"/>
            </Window>
            """));
        Assert.Contains("ItemsSource", ex.Message);
    }

    [Fact]
    public void Xaml_fragment_mirrors_real_page_inline_plus_bound()
    {
        var vm = new TestViewModel();
        var result = XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <StackPanel>
                <ListBox SelectedIndex="0">
                  <ListBoxItem>Blue</ListBoxItem>
                  <ListBoxItem>Green</ListBoxItem>
                </ListBox>
                <ListBox Height="164" ItemsSource="{Binding ViewModel.ListBoxItems, Mode=TwoWay}" SelectedIndex="2"/>
              </StackPanel>
            </Window>
            """);
        var window = Assert.IsType<Window>(result.Root);
        window.DataContext = vm;
        var panel = Assert.IsType<StackPanel>(window.Content);
        var inline = Assert.IsType<ListBox>(panel.LogicalChildren[0]);
        Assert.Equal(["Blue", "Green"], inline.Items.Select(i => i.Content));
        Assert.Equal(0, inline.SelectedIndex);
        var bound = Assert.IsType<ListBox>(panel.LogicalChildren[1]);
        Assert.Equal(164, bound.Height);
        Assert.Equal(5, bound.Items.Count);
        Assert.Equal(2, bound.SelectedIndex);
        Assert.Equal("Courier New", bound.SelectedItem?.Content);
    }

    [Fact]
    public void Real_staged_listbox_page_inflates_with_bound_rows()
    {
        // Real staged Views/Collections/ListBoxPage.xaml from the converted
        // WPFGallery assembly (re-verified the pre-slice failure against this
        // exact resource: 'ListBox' has no bindable property 'ItemsSource').
        using var staged = LoadStagedGalleryAssembly();
        var asm = staged.Assembly;
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = asm;
        try
        {
            using var stream = asm.GetManifestResourceStream("Views/Collections/ListBoxPage.xaml")!;
            using var reader = new StreamReader(stream);
            var root = XamlLoader.Load(reader.ReadToEnd()).Root;
            var page = Assert.IsAssignableFrom<FrameworkElement>(root);
            var vm = Activator.CreateInstance(asm.GetType("WPFGallery.ViewModels.ListBoxPageViewModel")!)!;
            page.DataContext = new PageStub { ViewModel = vm };
            var lists = Descendants(page).OfType<ListBox>().ToList();
            Assert.Equal(2, lists.Count);
            Assert.Equal(["Blue", "Green", "Red", "Yellow"], lists[0].Items.Select(i => i.Content));
            Assert.Equal(0, lists[0].SelectedIndex);
            var bound = lists[1];
            Assert.Equal(164, bound.Height);
            Assert.Equal(
                ["Arial", "Comic Sans MS", "Courier New", "Segoe UI", "Times New Roman"],
                bound.Items.Select(i => i.Content));
            Assert.Equal(2, bound.SelectedIndex);
            Assert.Equal("Courier New", bound.SelectedItem?.Content);
        }
        finally { XamlLoader.CustomElementSource = previous; }
    }

    private sealed class PageStub
    {
        public object? ViewModel { get; set; }
    }

    private sealed class TestViewModel
    {
        public TestListViewModel ViewModel { get; } = new();
    }

    private sealed class TestListViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public ObservableCollection<string> ListBoxItems { get; } = new(
            ["Arial", "Comic Sans MS", "Courier New", "Segoe UI", "Times New Roman"]);

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class StagedGallery : IDisposable
    {
        public System.Reflection.Assembly Assembly { get; }

        private readonly System.ResolveEventHandler _resolve;

        public StagedGallery(Dictionary<string, byte[]> images)
        {
            _resolve = (_, args) =>
            {
                var name = new System.Reflection.AssemblyName(args.Name).Name!;
                if (images.TryGetValue(name, out var bytes))
                {
                    try { return System.Reflection.Assembly.Load(bytes); }
                    catch { return null; }
                }
                return null;
            };
            AppDomain.CurrentDomain.AssemblyResolve += _resolve;
            Assembly = System.Reflection.Assembly.Load(images["WPFGallery"]);
        }

        public void Dispose() => AppDomain.CurrentDomain.AssemblyResolve -= _resolve;
    }

    private static StagedGallery LoadStagedGalleryAssembly()
    {
        // Boot-free staging load: the WPFGallery image plus an
        // AssemblyResolve hook over the staged files (kept for the whole
        // test, since converted member bases resolve lazily), so converted
        // member types whose bases live in staged dependencies (e.g. the
        // CommunityToolkit.Mvvm ObservableObject behind the page
        // view-models) resolve without booting a window. Already-loaded
        // engine assemblies satisfy by name, so engine type identity is the
        // test's own (OfType/ListBox casts keep working).
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "DesktopWasm.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var apps = System.IO.Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "wpf-gallery");
        var package = JsonSerializer.Deserialize<ApplicationPackage>(
            File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return new StagedGallery(package.Assemblies.ToDictionary(
            a => a.Name,
            a => File.ReadAllBytes(System.IO.Path.Combine(apps, a.File)),
            StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<UIElement> Descendants(UIElement element)
    {
        yield return element;
        if (element is FrameworkElement framework)
            foreach (var child in framework.LogicalChildren)
                foreach (var item in Descendants(child)) yield return item;
    }
}
