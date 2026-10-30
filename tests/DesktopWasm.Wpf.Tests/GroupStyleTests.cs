using System.Collections.ObjectModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-44: GroupStyle/GroupItem engine surface. The staged OxyPlot
/// ExampleBrowser builds its demo list as a CollectionView grouped by
/// Category (PropertyGroupDescription) and styles the groups through one
/// &lt;ListBox.GroupStyle&gt; (ContainerStyle with an Expander template
/// hosting an ItemsPresenter). Boots used to stop at MainWindow.xaml line
/// 77 with XamlLoadException "Unknown element &lt;GroupStyle&gt;".
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class GroupStyleTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    private sealed class Example
    {
        public Example(string category, string name)
        {
            Category = category;
            Name = name;
        }

        public string Category { get; }
        public string Name { get; }
        public override string ToString() => Name;
    }

    private static List<Example> Sample() =>
    [
        new("Axes", "Log"),
        new("Axes", "Linear"),
        new("Plot", "Line"),
        new("Plot", "Bar"),
    ];

    private sealed class EmptyGroup : CollectionViewGroup
    {
        public EmptyGroup()
            : base("Empty")
        {
        }

        public override ObservableCollection<object> Items { get; } = new();
        public override int ItemCount => 0;
    }

    private sealed class FullGroup : CollectionViewGroup
    {
        public FullGroup(object? name, params object[] items)
            : base(name)
        {
            foreach (var item in items) Items.Add(item);
        }

        public override ObservableCollection<object> Items { get; } = new();
        public override int ItemCount => Items.Count;
    }

    private sealed class StubView : CollectionView
    {
        public StubView(ReadOnlyObservableCollection<object> groups)
            : base(Enumerable.Empty<object>())
        {
            OverrideGroups = groups;
        }

        public ReadOnlyObservableCollection<object> OverrideGroups { get; }

        public override ReadOnlyObservableCollection<object>? Groups => OverrideGroups;
    }

    private sealed class PickViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private object? _pick;
        public object? Pick
        {
            get => _pick;
            set
            {
                _pick = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Pick)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    [Fact]
    public void GroupStyle_defaults_are_empty()
    {
        var style = new GroupStyle();
        Assert.Null(style.ContainerStyle);
        Assert.Null(style.HeaderTemplate);
        Assert.Null(style.Panel);
        Assert.False(style.HidesIfEmpty);
    }

    [Fact]
    public void ExpandDirection_defaults_down_and_stores()
    {
        var expander = new Expander();
        Assert.Equal(ExpandDirection.Down, expander.ExpandDirection);
        expander.ExpandDirection = ExpandDirection.Left;
        Assert.Equal(ExpandDirection.Left, expander.ExpandDirection);
    }

    [Fact]
    public void GroupItem_takes_group_as_datacontext_with_caption()
    {
        var examples = Sample();
        var view = CollectionViewSource.GetDefaultView(examples)!;
        view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        var group = view.Groups!.OfType<CollectionViewGroup>().First(g => Equals(g.Name, "Axes"));

        var item = new GroupItem { Group = group };
        Assert.Same(group, item.Group);
        Assert.Same(group, item.DataContext);

        var window = new Window { Content = item };
        window.Measure(new WpfSize(400, 1000));
        window.Arrange(new WpfRect(0, 0, 400, 1000));
        Assert.True(item.DesiredSize.Height > 10, $"group caption measured {item.DesiredSize}");
    }

    [Fact]
    public void ListBox_builds_group_containers_over_view_groups()
    {
        var examples = Sample();
        var view = CollectionViewSource.GetDefaultView(examples)!;
        view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));

        var list = new ListBox();
        list.GroupStyle.Add(new GroupStyle());
        list.ItemsSource = view;

        Assert.Equal(2, CountGroups(list));
        var rows = list.Items;
        Assert.Equal(4, rows.Count);
        // Group order follows the view; row text follows the data items.
        Assert.Equal(["Log", "Linear", "Line", "Bar"], rows.Select(r => r.Content).ToArray());
    }

    private static int CountGroups(ListBox list)
    {
        var count = 0;
        foreach (var child in list.LogicalChildren)
            if (child is GroupItem) count++;
        return count;
    }

    [Fact]
    public void GroupStyle_absent_keeps_flat_rows()
    {
        var examples = Sample();
        var view = CollectionViewSource.GetDefaultView(examples)!;
        view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));

        var list = new ListBox { ItemsSource = view };
        Assert.Equal(4, list.Items.Count);
        Assert.Equal(0, CountGroups(list));
    }

    [Fact]
    public void HidesIfEmpty_skips_empty_groups()
    {
        var full = new FullGroup("Full", new Example("Full", "a"), new Example("Full", "b"));
        var groups = new ReadOnlyObservableCollection<object>(
            new ObservableCollection<object> { new EmptyGroup(), full });

        var list = new ListBox();
        list.GroupStyle.Add(new GroupStyle { HidesIfEmpty = true });
        list.ItemsSource = new StubView(groups);

        Assert.Equal(1, CountGroups(list));
        Assert.Equal(["a", "b"], list.Items.Select(r => r.Content).ToArray());

        var kept = new ListBox();
        kept.GroupStyle.Add(new GroupStyle());
        kept.ItemsSource = new StubView(groups);
        Assert.Equal(2, CountGroups(kept));
    }

    [Fact]
    public void ItemContainerStyle_applies_to_grouped_rows()
    {
        var examples = Sample();
        var view = CollectionViewSource.GetDefaultView(examples)!;
        view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));

        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Colors.Red)));

        var list = new ListBox();
        list.GroupStyle.Add(new GroupStyle());
        list.ItemsSource = view;
        Assert.All(list.Items, row => Assert.Null(row.Background));

        list.ItemContainerStyle = style;
        Assert.All(list.Items, row => Assert.NotNull(row.Background));

        // Late rows (style first, source second) land styled too.
        var view2 = CollectionViewSource.GetDefaultView(Sample())!;
        view2.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        var late = new ListBox { ItemContainerStyle = style };
        late.GroupStyle.Add(new GroupStyle());
        late.ItemsSource = view2;
        Assert.All(late.Items, row => Assert.NotNull(row.Background));
    }

    [Fact]
    public void SelectedItem_binding_reseats_to_the_data_item()
    {
        var examples = Sample();
        var view = CollectionViewSource.GetDefaultView(examples)!;
        view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));

        var vm = new PickViewModel { Pick = examples[2] };
        var list = new ListBox();
        list.GroupStyle.Add(new GroupStyle());
        list.DataContext = vm;
        list.SetBinding("SelectedItem", new Binding("Pick"));
        list.ItemsSource = view;

        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal("Line", list.SelectedItem?.Content);

        // A null data value clears (a list bound before any pick stays clear).
        var cleared = new ListBox();
        cleared.GroupStyle.Add(new GroupStyle());
        cleared.DataContext = new PickViewModel { Pick = null };
        cleared.SetBinding("SelectedItem", new Binding("Pick"));
        cleared.ItemsSource = CollectionViewSource.GetDefaultView(Sample());
        Assert.Equal(-1, cleared.SelectedIndex);
        Assert.Null(cleared.SelectedItem);
    }

    [Fact]
    public void GroupStyle_xaml_block_parses_with_container_slots()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Window.Resources>" +
            "<Style x:Key=\"RowStyle\" TargetType=\"ListBoxItem\">" +
            "<Setter Property=\"Background\" Value=\"Red\"/>" +
            "</Style>" +
            "</Window.Resources>" +
            "<ListBox ItemContainerStyle=\"{DynamicResource RowStyle}\">" +
            "<ListBox.GroupStyle>" +
            "<GroupStyle HidesIfEmpty=\"True\">" +
            "<GroupStyle.ContainerStyle>" +
            "<Style TargetType=\"{x:Type GroupItem}\">" +
            "<Setter Property=\"Template\">" +
            "<Setter.Value>" +
            "<ControlTemplate>" +
            "<Expander IsExpanded=\"False\" ExpandDirection=\"Down\">" +
            "<Expander.Header>" +
            "<StackPanel Orientation=\"Horizontal\">" +
            "<TextBlock FontWeight=\"Bold\" Text=\"{Binding Name}\" Padding=\"0 0 8 0\"/>" +
            "<TextBlock Text=\"{Binding ItemCount, StringFormat='({0})'}\"/>" +
            "</StackPanel>" +
            "</Expander.Header>" +
            "<ItemsPresenter />" +
            "</Expander>" +
            "</ControlTemplate>" +
            "</Setter.Value>" +
            "</Setter>" +
            "</Style>" +
            "</GroupStyle.ContainerStyle>" +
            "<GroupStyle.HeaderTemplate>" +
            "<DataTemplate><TextBlock Text=\"{Binding Name}\"/></DataTemplate>" +
            "</GroupStyle.HeaderTemplate>" +
            "<GroupStyle.Panel>" +
            "<ItemsPanelTemplate><StackPanel Orientation=\"Vertical\"/></ItemsPanelTemplate>" +
            "</GroupStyle.Panel>" +
            "</GroupStyle>" +
            "</ListBox.GroupStyle>" +
            "</ListBox>" +
            "</Window>").Root);
        var list = Assert.IsType<ListBox>(window.Content);
        Assert.Single(list.GroupStyle);
        var group = list.GroupStyle[0];
        Assert.True(group.HidesIfEmpty);
        Assert.NotNull(group.ContainerStyle);
        Assert.Equal(typeof(GroupItem), group.ContainerStyle.TargetType);
        Assert.NotNull(group.HeaderTemplate);
        Assert.NotNull(group.Panel);
        Assert.NotNull(list.ItemContainerStyle);
    }

    [Fact]
    public void ContainerStyle_template_inflates_with_presenter_hosting_rows()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\">" +
            "<ListBox>" +
            "<ListBox.GroupStyle>" +
            "<GroupStyle>" +
            "<GroupStyle.ContainerStyle>" +
            "<Style TargetType=\"{x:Type GroupItem}\">" +
            "<Setter Property=\"Template\">" +
            "<Setter.Value>" +
            "<ControlTemplate>" +
            "<Expander IsExpanded=\"True\">" +
            "<Expander.Header>" +
            "<TextBlock Text=\"{Binding Name}\"/>" +
            "</Expander.Header>" +
            "<ItemsPresenter />" +
            "</Expander>" +
            "</ControlTemplate>" +
            "</Setter.Value>" +
            "</Setter>" +
            "</Style>" +
            "</GroupStyle.ContainerStyle>" +
            "</GroupStyle>" +
            "</ListBox.GroupStyle>" +
            "</ListBox>" +
            "</Window>").Root);
        var list = Assert.IsType<ListBox>(window.Content);

        var examples = Sample();
        var view = CollectionViewSource.GetDefaultView(examples)!;
        view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        list.ItemsSource = view;

        Assert.Equal(2, CountGroups(list));
        foreach (var container in list.LogicalChildren.OfType<GroupItem>())
        {
            Assert.NotNull(container.TemplateRoot);
            var presenter = FindDescendant<ItemsPresenter>(container.TemplateRoot!);
            Assert.NotNull(presenter);
        }
        window.Measure(new WpfSize(400, 1000));
        window.Arrange(new WpfRect(0, 0, 400, 1000));

        // Header bindings resolve against the group (Name lands in the header).
        var first = list.LogicalChildren.OfType<GroupItem>().First();
        var captions = FindDescendants<TextBlock>(first.TemplateRoot!).Select(t => t.Text).ToList();
        Assert.Contains("Axes", captions);
    }

    private static List<T> FindDescendants<T>(UIElement root)
        where T : UIElement
    {
        var found = new List<T>();
        if (root is T match) found.Add(match);
        if (root is FrameworkElement host)
            foreach (var child in host.LogicalChildren)
                found.AddRange(FindDescendants<T>(child));
        return found;
    }

    private static T? FindDescendant<T>(UIElement root)
        where T : UIElement
    {
        if (root is T match) return match;
        if (root is not FrameworkElement host) return null;
        foreach (var child in host.LogicalChildren)
        {
            var found = FindDescendant<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    [Fact]
    public void Collapsed_expander_content_stays_logical_and_expands_to_rows()
    {
        // Slice-51: a collapsed Expander used to drop its Content from the
        // logical tree, so the ItemsPresenter inside a GroupItem template never
        // got a TemplateParent and the rows panel stayed orphaned: expanding a
        // group showed nothing and rows never arranged. Content now stays
        // logical while only the visual slot is expansion-gated.
        var window = Assert.IsType<Window>(XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\">" +
            "<ListBox>" +
            "<ListBox.GroupStyle>" +
            "<GroupStyle>" +
            "<GroupStyle.ContainerStyle>" +
            "<Style TargetType=\"{x:Type GroupItem}\">" +
            "<Setter Property=\"Template\">" +
            "<Setter.Value>" +
            "<ControlTemplate>" +
            "<Expander IsExpanded=\"False\">" +
            "<Expander.Header>" +
            "<TextBlock Text=\"{Binding Name}\"/>" +
            "</Expander.Header>" +
            "<ItemsPresenter />" +
            "</Expander>" +
            "</ControlTemplate>" +
            "</Setter.Value>" +
            "</Setter>" +
            "</Style>" +
            "</GroupStyle.ContainerStyle>" +
            "</GroupStyle>" +
            "</ListBox.GroupStyle>" +
            "</ListBox>" +
            "</Window>").Root);
        var list = Assert.IsType<ListBox>(window.Content);
        var examples = Sample();
        var view = CollectionViewSource.GetDefaultView(examples)!;
        view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        list.ItemsSource = view;
        window.Measure(new WpfSize(400, 1000));
        window.Arrange(new WpfRect(0, 0, 400, 1000));

        var first = list.LogicalChildren.OfType<GroupItem>().First();
        var expander = FindDescendants<Expander>(first).Single();
        Assert.False(expander.IsExpanded);
        var presenter = FindDescendant<ItemsPresenter>(expander);
        Assert.NotNull(presenter);
        Assert.Same(first, TemplateParentOf(presenter));
        // Collapsed rows have no rects yet but are already generated.
        Assert.Equal(0, list.Items[0].RenderRect.Width);
        Assert.Equal("Log", list.Items[0].Content);

        expander.IsExpanded = true;
        window.Measure(new WpfSize(400, 1000));
        window.Arrange(new WpfRect(0, 0, 400, 1000));

        var row = list.Items[0].RenderRect;
        Assert.True(row.Width > 0 && row.Height > 0);
        var rowHost = Assert.IsAssignableFrom<Panel>(((UIElement)list.Items[0]).Parent);
        Assert.Same(presenter, rowHost.Parent);
    }

    private static FrameworkElement? TemplateParentOf(FrameworkElement element) =>
        (FrameworkElement?)typeof(FrameworkElement).GetProperty("TemplateParent",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .GetValue(element);

    [Fact]
    public void Rejects_unknown_group_slot()
    {
        var ex = Assert.Throws<XamlLoadException>(() =>
            XamlLoader.Load(
                $"<Window xmlns=\"{PresentationNs}\">" +
                "<ListBox><ListBox.GroupStyle><GroupStyle><GroupStyle.Foo/></GroupStyle></ListBox.GroupStyle></ListBox>" +
                "</Window>"));
        Assert.Contains("GroupStyle", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Grouped_list_renders_selection_in_skia_pixels()
    {
        Application.Current = new BootProbe.ProbeApp();
        var examples = Sample();
        var view = CollectionViewSource.GetDefaultView(examples)!;
        view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));

        var window = new Window();
        var list = new ListBox { Width = 400 };
        list.GroupStyle.Add(new GroupStyle());
        list.ItemsSource = view;
        list.SelectedIndex = 1;
        window.Content = list;
        using var surface = new DesktopWasm.Graphics.SkiaSurface(400, 600);
        var mil = new WasmMilCore(surface);
        var commands = mil.Render(window, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        mil.CommitChannel(commands);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        var selected = list.Items[1].RenderRect;
        var pixel = bitmap.GetPixel(
            Math.Clamp((int)(selected.X + 2), 0, 399),
            Math.Clamp((int)(selected.Y + selected.Height / 2), 0, 599));
        Assert.True(pixel.Blue > 150 && pixel.Red < 150,
            $"selected grouped row ({selected}) pixel {pixel} is not the selection highlight");
    }

    [Fact]
    public void Staged_MainWindow_xaml_clears_group_style_block()
    {
        var entry = LoadStagedOxyPlotEntry();
        var name = entry.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("MainWindow.xaml", StringComparison.Ordinal));
        Assert.NotNull(name);
        using var stream = entry.GetManifestResourceStream(name!);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = entry;
        try
        {
            try
            {
                XamlLoader.Load(reader.ReadToEnd());
            }
            catch (XamlLoadException ex)
            {
                // Tightened slice-43 oracle: the exact line-77 GroupStyle
                // failure is gone, so any later failure sits past the whole
                // <ListBox.GroupStyle> block (lines 76-98).
                Assert.DoesNotContain("Unknown element <GroupStyle>", ex.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("GroupStyle", ex.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("GroupItem", ex.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("ItemsPresenter", ex.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("ItemContainerStyle", ex.Message, StringComparison.Ordinal);
                Assert.True(ex.Line > 98, $"staged MainWindow.xaml must advance past the GroupStyle block, failed at line {ex.Line}: {ex.Message}");
            }
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    [Fact]
    public void Staged_boot_advances_past_group_style()
    {
        Application.Current = null;
        var entry = LoadStagedOxyPlotEntry();
        try
        {
            WpfAppBoot.Boot(entry);
        }
        catch (Exception ex)
        {
            var messages = new List<string>();
            for (var current = ex; current is not null; current = current.InnerException)
                messages.Add(current.GetType().FullName + ": " + current.Message.Split('\n')[0]);
            // Tightened slice-43 oracle: the exact line-77 GroupStyle failure
            // is gone from the whole boot chain.
            Assert.DoesNotContain(messages, m => m.Contains("Unknown element <GroupStyle>", StringComparison.Ordinal));
            var xamlFailure = messages.FirstOrDefault(m => m.Contains(nameof(XamlLoadException), StringComparison.Ordinal));
            if (xamlFailure is not null)
                Assert.DoesNotMatch(@"\(line 77,", xamlFailure);
        }
    }

    private static readonly object StagedGate = new();
    private static Assembly? _stagedEntry;

    private static Assembly LoadStagedOxyPlotEntry()
    {
        lock (StagedGate)
        {
            var hit = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "ExampleBrowser.WPF", StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                VerifyStagedHashes();
                return hit;
            }
            if (_stagedEntry is not null) return _stagedEntry;
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(
                directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "oxyplot", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var apps = System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "oxyplot");
            using var document = JsonDocument.Parse(
                File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
            var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var bytes = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
                var actual = Convert.ToHexString(SHA256.HashData(bytes));
                Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                    $"hash mismatch in staged '{file}': {actual} != {expected}");
                images[assembly.GetProperty("name").GetString()!] = bytes;
            }
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var requested = new AssemblyName(args.Name).Name;
                var loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, requested, StringComparison.OrdinalIgnoreCase));
                if (loaded is not null) return loaded;
                if (requested is not null && images.TryGetValue(requested, out var image))
                    return Assembly.Load(image);
                return null;
            };
            foreach (var image in images.Values)
                Assembly.Load(image);
            _stagedEntry = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => string.Equals(a.GetName().Name, "ExampleBrowser.WPF", StringComparison.OrdinalIgnoreCase));
            return _stagedEntry;
        }
    }

    private static void VerifyStagedHashes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "oxyplot", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var apps = System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "oxyplot");
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
        foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
        {
            var file = assembly.GetProperty("file").GetString()!;
            var expected = assembly.GetProperty("sha256").GetString()!;
            var actual = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(System.IO.Path.Combine(apps, file))));
            Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                $"hash mismatch in staged '{file}': {actual} != {expected}");
        }
    }
}
