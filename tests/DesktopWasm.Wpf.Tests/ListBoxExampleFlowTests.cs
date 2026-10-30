using System.ComponentModel;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-47: the staged OxyPlot boot completed through MainWindow.xaml line
/// 133 but the legacy ListBox rendered 0 rows while ItemsSource carried the
/// live 754-example CollectionView (754 in, 0 rows out). Two engine defects
/// combined: (1) generated rows are styled before they are parented, so
/// StaticResource setters in ItemContainerStyle (e.g. NormalItemBackground
/// from Window.Resources) resolved against nothing and threw; the binding
/// then swallowed the throw (WPF keeps-old-value semantics) leaving
/// ItemsSource set with no rows and no retry. (2) TemplateBinding transfers
/// with unconvertible runtime types (Thickness BorderThickness onto a double
/// StrokeThickness) failed the whole generation instead of keeping the
/// target default like WPF's binding-error semantics. Selection never flowed
/// either: SelectedItem stayed OneWay and nothing pushed the picked data
/// item back, while the upstream MainWindow relies purely on the
/// SelectedItem binding (no SelectionChanged handler in codebehind).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ListBoxExampleFlowTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

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

    private sealed class FlowViewModel : INotifyPropertyChanged
    {
        private object? _pick;

        public object? Source { get; set; }

        public object? Pick
        {
            get => _pick;
            set
            {
                _pick = value;
                Model = value is null ? null : new object();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Pick)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Model)));
            }
        }

        public object? Model { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    [Fact]
    public void Grouped_rows_style_with_window_staticresource()
    {
        var examples = new List<Example>
        {
            new("Axes", "Log"),
            new("Axes", "Linear"),
            new("Plot", "Line"),
            new("Plot", "Bar"),
        };
        var view = CollectionViewSource.GetDefaultView(examples)!;
        view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        var vm = new FlowViewModel { Source = view };

        var window = (Window)XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" xmlns:x=\"{XamlNs}\">" +
            "<Window.Resources>" +
            "<SolidColorBrush x:Key=\"RowFill\" Color=\"#FF0000\" />" +
            "<Style x:Key=\"RowStyle\" TargetType=\"ListBoxItem\">" +
            "<Setter Property=\"Background\" Value=\"{StaticResource RowFill}\" />" +
            "</Style>" +
            "</Window.Resources>" +
            "<ListBox ItemsSource=\"{Binding Source}\" ItemContainerStyle=\"{StaticResource RowStyle}\">" +
            "<ListBox.GroupStyle><GroupStyle /></ListBox.GroupStyle>" +
            "</ListBox></Window>").Root;
        window.DataContext = vm;

        var list = Assert.IsType<ListBox>(window.Content);
        // Pre-fix this stayed 0: the StaticResource throw was swallowed by
        // the ItemsSource binding, leaving the view set with no rows.
        Assert.Equal(4, list.Items.Count);
        Assert.All(list.Items, row => Assert.NotNull(row.Background));
    }

    [Fact]
    public void Flat_rows_style_with_window_staticresource()
    {
        var vm = new FlowViewModel { Source = new[] { "a", "b", "c" } };

        var window = (Window)XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" xmlns:x=\"{XamlNs}\">" +
            "<Window.Resources>" +
            "<SolidColorBrush x:Key=\"RowFill\" Color=\"#00FF00\" />" +
            "<Style x:Key=\"RowStyle\" TargetType=\"ListBoxItem\">" +
            "<Setter Property=\"Background\" Value=\"{StaticResource RowFill}\" />" +
            "</Style>" +
            "</Window.Resources>" +
            "<ListBox ItemsSource=\"{Binding Source}\" ItemContainerStyle=\"{StaticResource RowStyle}\" />" +
            "</Window>").Root;
        window.DataContext = vm;

        var list = Assert.IsType<ListBox>(window.Content);
        Assert.Equal(3, list.Items.Count);
        Assert.Equal(["a", "b", "c"], list.Items.Select(r => r.Content).ToArray());
        Assert.All(list.Items, row => Assert.NotNull(row.Background));
    }

    [Fact]
    public void TemplateBinding_thickness_to_double_keeps_default()
    {
        var window = (Window)XamlLoader.Load(
            $"<Window xmlns=\"{PresentationNs}\" xmlns:x=\"{XamlNs}\">" +
            "<Window.Resources>" +
            "<Style x:Key=\"ThickStyle\" TargetType=\"ListBoxItem\">" +
            "<Setter Property=\"BorderThickness\" Value=\"2\" />" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"ListBoxItem\">" +
            "<Rectangle x:Name=\"Frame\" Fill=\"{TemplateBinding Background}\" StrokeThickness=\"{TemplateBinding BorderThickness}\" />" +
            "</ControlTemplate>" +
            "</Setter.Value></Setter>" +
            "</Style>" +
            "</Window.Resources>" +
            "<ListBox ItemsSource=\"{Binding Source}\" ItemContainerStyle=\"{StaticResource ThickStyle}\" />" +
            "</Window>").Root;
        window.DataContext = new FlowViewModel { Source = new[] { "a", "b" } };

        var list = Assert.IsType<ListBox>(window.Content);
        // Pre-fix the Thickness->double transfer threw out of generation.
        Assert.Equal(2, list.Items.Count);
        window.Measure(new WpfSize(400, 200));
        window.Arrange(new WpfRect(0, 0, 400, 200));
        foreach (var row in list.Items)
        {
            var frame = Assert.IsType<Rectangle>(
                row.Template?.FindName("Frame", row));
            Assert.Equal(1.0, frame.StrokeThickness);
        }
    }

    [Fact]
    public void Selecting_a_row_pushes_the_data_item_to_the_source()
    {
        var examples = new List<Example>
        {
            new("Axes", "Log"),
            new("Axes", "Linear"),
            new("Plot", "Line"),
        };
        var view = CollectionViewSource.GetDefaultView(examples)!;
        view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));

        var vm = new FlowViewModel();
        var list = new ListBox { DataContext = vm };
        list.GroupStyle.Add(new GroupStyle());
        list.SetBinding("SelectedItem", new Binding("Pick"));
        list.ItemsSource = view;

        Assert.Equal(-1, list.SelectedIndex);
        Assert.Null(vm.Pick);

        list.SelectedIndex = 1;

        // The container contract stays (a ListBoxItem), but the bound
        // source carries the data item, like WPF.
        Assert.IsType<ListBoxItem>(list.SelectedItem);
        Assert.Same(examples[1], vm.Pick);
        Assert.NotNull(vm.Model);
    }

    [Fact]
    public void Reseat_from_source_does_not_push_back()
    {
        var examples = new List<Example>
        {
            new("Axes", "Log"),
            new("Axes", "Linear"),
        };

        var vm = new FlowViewModel { Pick = examples[1] };
        var list = new ListBox { DataContext = vm };
        list.SetBinding("SelectedItem", new Binding("Pick"));
        list.ItemsSource = CollectionViewSource.GetDefaultView(examples);

        // Source-to-target re-seat lands the row without echoing a push.
        Assert.Equal(1, list.SelectedIndex);
        Assert.Same(examples[1], list.SelectedItem is ListBoxItem row
            ? GetGeneratedData(list, row) : null);
        Assert.Same(examples[1], vm.Pick);
    }

    [Fact]
    public void Explicit_oneway_selecteditem_does_not_push()
    {
        var vm = new FlowViewModel();
        var list = new ListBox { DataContext = vm };
        list.SetBinding("SelectedItem", new Binding("Pick") { Mode = BindingMode.OneWay });
        list.ItemsSource = new[] { "a", "b" };

        list.SelectedIndex = 0;

        Assert.Null(vm.Pick);
    }

    [Fact]
    public void Clearing_selection_pushes_null()
    {
        var vm = new FlowViewModel();
        var list = new ListBox { DataContext = vm };
        list.SetBinding("SelectedItem", new Binding("Pick"));
        list.ItemsSource = new[] { "a", "b" };
        list.SelectedIndex = 0;
        Assert.Same("a", vm.Pick);

        list.SelectedIndex = -1;

        Assert.Null(vm.Pick);
    }

    private static object? GetGeneratedData(ListBox list, ListBoxItem row)
    {
        var field = typeof(ListBox).GetField("_generatedData",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var map = (System.Collections.IDictionary)field!.GetValue(list)!;
        return map[row];
    }
}
