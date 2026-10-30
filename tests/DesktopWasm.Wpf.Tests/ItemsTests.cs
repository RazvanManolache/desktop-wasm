using System.ComponentModel;
using System.Text;
using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Step-43 items cluster: ItemsControl/Selector/ComboBox/ListView,
/// CollectionView, BindingBase surface, Window chrome and StackPanel orientation.</summary>
[ResetAmbientState]
public sealed class ItemsTests
{
    private static (WasmInputRouter Router, WpfInputManager Manager) Harness(UIElement content, double width = 400, double height = 600)
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        root.Content = content;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new SkiaSurface((int)width, (int)height));
        mil.Render(root, new WpfSize(width, height), new WpfRect(0, 0, width, height));
        return (router, manager);
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(Handle, key, true, false, false, false));

    [Fact]
    public void ItemsControl_add_strings_generates_label_containers()
    {
        var control = new ItemsControl();
        control.AddItem("a");
        control.AddItem("b");
        Assert.Equal(2, control.Items.Count);
        Assert.True(control.HasItems);
        var container = Assert.IsType<Label>(control.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Equal("a", container.Content);
        Assert.Same(control.Items[0], control.ItemContainerGenerator.ItemFromContainer(container));
        Assert.Equal(0, control.ItemContainerGenerator.IndexFromContainer(container));
        Assert.Null(control.ItemContainerGenerator.ContainerFromIndex(99));
        Assert.Same(control, ItemsControl.ItemsControlFromItemContainer(container));
    }

    [Fact]
    public void ItemsControl_ui_elements_are_their_own_containers()
    {
        var control = new ItemsControl();
        var button = new Button();
        control.AddChild(button);
        Assert.Same(button, control.ItemContainerGenerator.ContainerFromItem(button));
        Assert.Same(control.Items[0], button);
    }

    [Fact]
    public void ItemsSource_bulk_replaces_items()
    {
        var control = new ItemsControl();
        control.ItemsSource = new object[] { "x", "y", "z" };
        Assert.Equal(3, control.Items.Count);
        Assert.Equal("y", Assert.IsType<Label>(control.ItemContainerGenerator.ContainerFromIndex(1)).Content);
        control.ItemsSource = null;
        Assert.False(control.HasItems);
    }

    [Fact]
    public void Selector_selection_fires_typed_event_with_payload()
    {
        var combo = new ComboBox();
        combo.AddItem("a");
        combo.AddItem("b");
        SelectionChangedEventArgs? seen = null;
        object? sender = null;
        combo.SelectionChanged += (s, e) => { sender = s; seen = e; };
        combo.SelectedIndex = 1;
        Assert.Same(combo, sender);
        Assert.NotNull(seen);
        Assert.Equal(["b"], seen.AddedItems.Cast<object>());
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("b", combo.SelectedItem);
        seen = null;
        combo.SelectedIndex = 1; // same value: silent
        Assert.Null(seen);
    }

    [Fact]
    public void Selector_pending_index_materializes_when_items_arrive()
    {
        var combo = new ComboBox { SelectedIndex = 1 };
        Assert.Null(combo.SelectedItem);
        combo.AddItem("a");
        combo.AddItem("b");
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("b", combo.SelectedItem);
    }

    [Fact]
    public void Selector_selected_item_resolves_by_reference_and_value_path()
    {
        var combo = new ComboBox();
        var ada = new Person { Name = "Ada", Age = 36 };
        var grace = new Person { Name = "Grace", Age = 85 };
        combo.AddItem(ada);
        combo.AddItem(grace);
        combo.SelectedItem = grace;
        Assert.Equal(1, combo.SelectedIndex);
        combo.SelectedItem = new Person { Name = "Nobody", Age = 0 };
        Assert.Equal(-1, combo.SelectedIndex);
        combo.SelectedValuePath = "Name";
        combo.SelectedValue = "Ada";
        Assert.Equal(0, combo.SelectedIndex);
        Assert.Equal("Ada", combo.SelectedValue);
        combo.SelectedValue = "Nobody";
        Assert.Equal(-1, combo.SelectedIndex);
    }

    [Fact]
    public void ComboBox_preselected_row_is_honored_and_synced()
    {
        var combo = new ComboBox();
        var first = new ComboBoxItem { Content = "a" };
        var second = new ComboBoxItem { Content = "b", IsSelected = true };
        combo.AddItem(first);
        combo.AddItem(second); // pre-selected before parenting
        Assert.Same(second, combo.SelectedItem);
        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
        combo.SelectedIndex = 0;
        Assert.True(first.IsSelected);
        Assert.False(second.IsSelected);
        second.IsSelected = true; // setter pushes back into the owner
        Assert.Same(second, combo.SelectedItem);
    }

    [Fact]
    public void Selector_selection_mode_parses_and_stores()
    {
        var combo = new ComboBox { SelectionMode = SelectionMode.Multiple };
        Assert.Equal(SelectionMode.Multiple, combo.SelectionMode);
    }

    [Fact]
    public void CollectionView_tracks_currency()
    {
        var view = new ListCollectionView(new object[] { "a", "b", "c" });
        Assert.Equal(3, view.Count);
        Assert.False(view.IsEmpty);
        Assert.Equal("a", view.CurrentItem);
        Assert.Equal(0, view.CurrentPosition);
        var fired = 0;
        view.PropertyChanged += (_, _) => fired++;
        Assert.True(view.MoveCurrentTo("c"));
        Assert.Equal(2, view.CurrentPosition);
        Assert.False(view.MoveCurrentTo("zzz"));
        Assert.True(view.MoveCurrentToFirst());
        Assert.Equal("a", view.CurrentItem);
        Assert.True(view.MoveCurrentToLast());
        Assert.Equal("c", view.CurrentItem);
        Assert.True(fired >= 3);
        view.GroupDescriptions.Add(new PropertyGroupDescription("Name"));
        Assert.Single(view.GroupDescriptions);
        view.Refresh();
        Assert.Equal("c", view.CurrentItem); // currency survives refresh
    }

    private sealed class Person : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        public string Name { get => _name; set { if (_name == value) return; _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); } }
        public int Age { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public override string ToString() => $"{Name} ({Age})";
    }

    private sealed class UpperConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is string s ? s.ToUpperInvariant() : value;
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is string s ? s.ToLowerInvariant() : value;
    }

    private sealed class SkipConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }

    [Fact]
    public void Binding_converter_applies_both_directions()
    {
        var vm = new Person { Name = "Ada" };
        var text = new TextBox();
        text.SetBinding("Text", new Binding("Name") { Source = vm, Mode = BindingMode.TwoWay, Converter = new UpperConverter() });
        Assert.Equal("ADA", text.Text);
        text.Text = "GRACE";
        Assert.Equal("grace", vm.Name); // ConvertBack lowercases on source push
    }

    [Fact]
    public void Binding_do_nothing_keeps_old_value()
    {
        var text = new TextBlock { Text = "old" };
        text.SetBinding("Text", new Binding("Name") { Source = new Person { Name = "Ada" }, Converter = new SkipConverter() });
        Assert.Equal("old", text.Text);
    }

    [Fact]
    public void Binding_explicit_trigger_waits_for_update_source()
    {
        var vm = new Person { Name = "Ada" };
        var text = new TextBox();
        var expression = text.SetBinding("Text", new Binding("Name")
        {
            Source = vm,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.Explicit,
        });
        text.Text = "Grace";
        Assert.Equal("Ada", vm.Name); // Explicit: edit alone does not push
        expression.UpdateSource();
        Assert.Equal("Grace", vm.Name);
    }

    [Fact]
    public void Binding_fallback_target_null_and_format_apply()
    {
        var missing = new TextBlock();
        missing.SetBinding("Text", new Binding("Nope") { Source = new Person(), FallbackValue = "fb" });
        Assert.Equal("fb", missing.Text);

        var nullish = new TextBlock();
        nullish.SetBinding("Text", new Binding("Name") { Source = new Person { Name = null! }, TargetNullValue = "null!" });
        Assert.Equal("null!", nullish.Text);

        var formatted = new TextBlock();
        formatted.SetBinding("Text", new Binding("Age") { Source = new Person { Age = 36 }, StringFormat = "Age: {0}" });
        Assert.Equal("Age: 36", formatted.Text);
    }

    [Fact]
    public void Binding_element_name_resolves_through_find_name()
    {
        var panel = new StackPanel();
        var source = new TextBox { Name = "src" };
        source.Text = "hi";
        var target = new TextBlock();
        panel.AddChild(source);
        panel.AddChild(target);
        Assert.Same(source, panel.FindName("src"));
        Assert.Null(panel.FindName("missing"));
        var expression = target.SetBinding("Text", new Binding("Text") { ElementName = "src" });
        Assert.Equal("hi", target.Text);
        source.Text = "yo"; // TextBox has no INPC: pull explicitly after edits
        expression.UpdateTarget();
        Assert.Equal("yo", target.Text);
    }

    [Fact]
    public void FindName_searches_depth_first()
    {
        var root = new StackPanel { Name = "root" };
        var inner = new StackPanel { Name = "inner" };
        var leaf = new Button { Name = "leaf" };
        root.AddChild(inner);
        inner.AddChild(leaf);
        Assert.Same(leaf, root.FindName("leaf"));
        Assert.Same(inner, root.FindName("inner"));
        Assert.Same(root, root.FindName("root"));
    }

    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";

    [Fact]
    public void Xaml_items_control_strings_and_selection()
    {
        var result = XamlLoader.Load($"""
            <Window {Ns}>
              <ComboBox SelectedIndex="1" IsDropDownOpen="True" MaxDropDownHeight="100">
                <ComboBoxItem Content="a"/>
                <ComboBoxItem>b</ComboBoxItem>
              </ComboBox>
            </Window>
            """);
        var window = Assert.IsType<Window>(result.Root);
        var combo = Assert.IsType<ComboBox>(window.Content);
        Assert.Equal(2, combo.Items.Count);
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("b", Assert.IsType<ComboBoxItem>(combo.SelectedItem).Content);
        Assert.True(combo.IsDropDownOpen);
        Assert.Equal(100, combo.MaxDropDownHeight);
    }

    [Fact]
    public void Xaml_preselected_row_selects_on_load()
    {
        var result = XamlLoader.Load($"""
            <Window {Ns}>
              <ComboBox>
                <ComboBoxItem Content="a" IsSelected="True"/>
                <ComboBoxItem Content="b"/>
              </ComboBox>
            </Window>
            """);
        var combo = Assert.IsType<ComboBox>(Assert.IsType<Window>(result.Root).Content);
        Assert.Equal(0, combo.SelectedIndex);
        Assert.Equal("a", Assert.IsType<ComboBoxItem>(combo.SelectedItem).Content);
    }

    [Fact]
    public void Xaml_items_control_inner_text_and_header_property_element()
    {
        var result = XamlLoader.Load($"""
            <Window {Ns}>
              <ItemsControl DisplayMemberPath="Name" AlternationCount="2">
                <ComboBoxItem>
                  <ComboBoxItem.Header>h</ComboBoxItem.Header>
                </ComboBoxItem>
                plain
              </ItemsControl>
            </Window>
            """);
        var control = Assert.IsType<ItemsControl>(Assert.IsType<Window>(result.Root).Content);
        Assert.Equal("Name", control.DisplayMemberPath);
        Assert.Equal(2, control.AlternationCount);
        Assert.Equal(2, control.Items.Count);
        Assert.Equal("h", Assert.IsType<ComboBoxItem>(control.Items[0]).Header);
        Assert.Equal("plain", control.Items[1]);
    }

    [Fact]
    public void Xaml_list_view_and_orientation_and_window_chrome()
    {
        var result = XamlLoader.Load($"""
            <Window {Ns} Title="T" ResizeMode="CanMinimize" WindowStyle="ToolWindow" WindowState="Maximized" WindowStartupLocation="CenterScreen" Topmost="True" ShowInTaskbar="False">
              <StackPanel Orientation="Horizontal">
                <ListView SelectedIndex="0" SelectionMode="Single">
                  <ListViewItem Content="x"/>
                </ListView>
              </StackPanel>
            </Window>
            """);
        var window = Assert.IsType<Window>(result.Root);
        Assert.Equal(ResizeMode.CanMinimize, window.ResizeMode);
        Assert.Equal(WindowStyle.ToolWindow, window.WindowStyle);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
        Assert.True(window.Topmost);
        Assert.False(window.ShowInTaskbar);
        var panel = Assert.IsType<StackPanel>(window.Content);
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        var list = Assert.IsType<ListView>(panel.LogicalChildren[0]);
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal("x", Assert.IsType<ListViewItem>(list.SelectedItem).Content);
    }

    [Fact]
    public void Xaml_rejects_bad_chrome_orientation_and_abstract_selector()
    {
        Assert.Contains("ResizeMode", Assert.Throws<XamlLoadException>(() => XamlLoader.Load($"""
            <Window {Ns} ResizeMode="Bogus"/>
            """)).Message);
        Assert.Contains("Orientation", Assert.Throws<XamlLoadException>(() => XamlLoader.Load($"""
            <Window {Ns}><StackPanel Orientation="Diagonal"/></Window>
            """)).Message);
        Assert.Contains("Selector", Assert.Throws<XamlLoadException>(() => XamlLoader.Load($"""
            <Window {Ns}><Selector/></Window>
            """)).Message);
        Assert.Contains("ItemsSource", Assert.Throws<XamlLoadException>(() => XamlLoader.Load($"""
            <Window {Ns}><ComboBox ItemsSource="nope"/></Window>
            """)).Message);
    }

    [Fact]
    public void Xaml_binding_element_name_fallback_and_trigger_options()
    {
        var result = XamlLoader.Load($$"""
            <Window {{Ns}}>
              <StackPanel>
                <TextBox Name="src" Text="hi"/>
                <TextBlock Text="{Binding Text, ElementName=src}"/>
                <TextBlock Text="{Binding Nope, FallbackValue=fb}"/>
              </StackPanel>
            </Window>
            """);
        var panel = Assert.IsType<StackPanel>(Assert.IsType<Window>(result.Root).Content);
        Assert.Equal("hi", Assert.IsType<TextBlock>(panel.LogicalChildren[1]).Text);
        Assert.Equal("fb", Assert.IsType<TextBlock>(panel.LogicalChildren[2]).Text);

        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load($$"""
            <Window {{Ns}}><TextBlock Text="{Binding Text, Converter=wat}"/></Window>
            """));
        Assert.Contains("Converter", ex.Message);
    }

    [Fact]
    public void StackPanel_horizontal_stacks_side_by_side()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.AddChild(new Border { Width = 100, Height = 20 });
        panel.AddChild(new Border { Width = 100, Height = 20 });
        Harness(panel);
        Assert.Equal(200, panel.DesiredSize.Width);
        Assert.Equal(20, panel.DesiredSize.Height);
        var children = panel.LogicalChildren;
        Assert.Equal(0, children[0].ArrangedRect.X);
        Assert.Equal(100, children[1].ArrangedRect.X);
    }

    [Fact]
    public void SystemParameters_report_fixed_metrics()
    {
        Assert.Equal(1024, SystemParameters.VirtualScreenWidth);
        Assert.Equal(768, SystemParameters.VirtualScreenHeight);
        Assert.False(SystemParameters.HighContrast);
        Assert.Equal(4, SystemParameters.MinimumHorizontalDragDistance);
    }

    [Fact]
    public void ComboBox_press_toggles_selects_and_keyboard_cycles()
    {
        var combo = new ComboBox { Width = 400 };
        combo.AddItem(new ComboBoxItem { Content = "a" });
        combo.AddItem(new ComboBoxItem { Content = "b" });
        combo.AddItem(new ComboBoxItem { Content = "c" });
        var (router, manager) = Harness(combo);
        Assert.False(combo.IsDropDownOpen);
        Down(router, 200, 15); // header
        Assert.True(combo.IsDropDownOpen);
        Down(router, 200, 30 + 2 * 24 + 12); // third row
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(2, combo.SelectedIndex);

        Assert.True(manager.TryFocus(combo));
        Key(router, "ArrowDown"); // wraps to the first row
        Assert.Equal(0, combo.SelectedIndex);
        Key(router, "ArrowUp"); // wraps to the last row
        Assert.Equal(2, combo.SelectedIndex);
        Down(router, 200, 15);
        Assert.True(combo.IsDropDownOpen);
        Key(router, "Escape");
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(2, combo.SelectedIndex);
    }

    [Fact]
    public void ComboBox_press_ignored_when_disabled()
    {
        var combo = new ComboBox { Width = 400, IsEnabled = false };
        combo.AddItem(new ComboBoxItem { Content = "a" });
        var (router, _) = Harness(combo);
        Down(router, 200, 15);
        Assert.False(combo.IsDropDownOpen);
    }

    [Fact]
    public void ComboBox_text_binding_pushes_on_edit()
    {
        var vm = new Person { Name = "Ada" };
        var combo = new ComboBox { IsEditable = true };
        combo.SetBinding("Text", new Binding("Name") { Source = vm, Mode = BindingMode.TwoWay });
        combo.Text = "Grace";
        Assert.Equal("Grace", vm.Name);
    }

    // Minimal synthetic-MS-BAML builder (mirrors ListBoxTests framing).
    private sealed class B
    {
        private readonly List<byte> _b = new();
        public B Raw(params byte[] b) { _b.AddRange(b); return this; }
        public B U16(ushort v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B I16(short v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B Str(string s)
        {
            var ascii = Encoding.ASCII.GetBytes(s);
            Seven(ascii.Length);
            _b.AddRange(ascii);
            return this;
        }
        public B Seven(int v)
        {
            uint u = (uint)v;
            while (u >= 0x80) { _b.Add((byte)(u | 0x80)); u >>= 7; }
            _b.Add((byte)u);
            return this;
        }
        public B Sized(byte type, Action<B> payload)
        {
            var inner = new B();
            payload(inner);
            var p = inner._b;
            if (p.Count + 1 >= 128) throw new InvalidOperationException("test record too large");
            _b.Add(type);
            _b.Add((byte)(p.Count + 1));
            _b.AddRange(p);
            return this;
        }
        public byte[] ToArray() => _b.ToArray();
    }

    private static byte[] Header() => new B()
        .Raw(0x0C, 0x00, 0x00, 0x00)
        .Raw(0x4D, 0x00, 0x53, 0x00, 0x42, 0x00, 0x41, 0x00, 0x4D, 0x00, 0x4C, 0x00)
        .U16(0).U16(0x60)
        .Raw(new byte[] { 0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00, 0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 })
        .ToArray();

    [Fact]
    public void Baml_combobox_travels_by_type_name_with_items_and_selection()
    {
        const short tWindow = -739; // verified against a real Window BAML fixture
        const short mItems = -192; // ItemsControl.Items
        const short mContent = -14; // ContentControl.Content
        const short cString = -615;
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("PresentationFramework, Version=4.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("System.Windows.Controls.ComboBox"));
        b.Sized(0x1D, p => p.U16(1).U16(0).Str("System.Windows.Controls.ComboBoxItem"));
        b.Sized(0x1F, p => p.U16(0).I16(0).Raw(0x00).Str("SelectedIndex"));
        b.Raw(0x03).I16(tWindow).Raw(0x00);
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Sized(0x24, p => p.I16(0).Str("1").I16(0));
        b.Raw(0x2E).I16(mItems);
        foreach (var text in new[] { "a", "b" })
        {
            b.Raw(0x03).I16(1).Raw(0x00);
            b.Sized(0x24, p => p.I16(mContent).Str(text).I16(cString));
            b.Raw(0x04);
        }
        b.Raw(0x04).Raw(0x04).Raw(0x02);

        using var stream = new MemoryStream(b.ToArray());
        var window = XamlLoader.LoadComponent<Window>(stream);
        var combo = Assert.IsType<ComboBox>(window.Content);
        Assert.Equal(2, combo.Items.Count);
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("b", Assert.IsType<ComboBoxItem>(combo.SelectedItem).Content);
    }

    private const string TemplateNs =
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private sealed record CardItem(string Title);

    private static string CardDictionary() =>
        "<ResourceDictionary " + TemplateNs + ">" +
        "<ItemsPanelTemplate x:Key=\"Wrap\"><WrapPanel Orientation=\"Horizontal\" Margin=\"10\" /></ItemsPanelTemplate>" +
        "<DataTemplate x:Key=\"Card\"><StackPanel Orientation=\"Vertical\"><TextBlock Text=\"{Binding Title}\" /></StackPanel></DataTemplate>" +
        "<HierarchicalDataTemplate x:Key=\"Nav\" ItemsSource=\"{Binding Items}\"><Grid /></HierarchicalDataTemplate>" +
        "</ResourceDictionary>";

    [Fact]
    public void Resource_entries_parse_panel_and_data_templates()
    {
        var dict = XamlLoader.LoadResourceDictionary(CardDictionary());
        var panel = Assert.IsType<ItemsPanelTemplate>(dict["Wrap"]);
        var host = Assert.IsType<WrapPanel>(panel.LoadContent());
        Assert.Equal(Orientation.Horizontal, host.Orientation);
        Assert.Equal(new Thickness(10), host.Margin);
        var card = Assert.IsType<DataTemplate>(dict["Card"]);
        Assert.Equal(typeof(DataTemplate), card.GetType());
        var nav = Assert.IsType<HierarchicalDataTemplate>(dict["Nav"]);
        Assert.Equal("Items", nav.ItemsSource?.Path?.Path);
        var first = Assert.IsType<StackPanel>(card.LoadContent());
        var second = Assert.IsType<StackPanel>(card.LoadContent());
        Assert.NotSame(first, second);
        first.DataContext = new CardItem("Hi");
        Assert.Equal("Hi", Assert.IsType<TextBlock>(Assert.Single(first.LogicalChildren)).Text);
    }

    [Fact]
    public void ItemTemplate_generates_templated_containers_with_data_context()
    {
        var dict = XamlLoader.LoadResourceDictionary(CardDictionary());
        var control = new ItemsControl
        {
            ItemTemplate = Assert.IsType<DataTemplate>(dict["Card"]),
        };
        control.AddItem(new CardItem("A"));
        control.AddItem(new CardItem("B"));
        var first = Assert.IsType<StackPanel>(control.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Same(control.Items[0], first.DataContext);
        Assert.Equal("A", Assert.IsType<TextBlock>(Assert.Single(first.LogicalChildren)).Text);
        var second = Assert.IsType<StackPanel>(control.ItemContainerGenerator.ContainerFromItem(control.Items[1]));
        Assert.Equal("B", Assert.IsType<TextBlock>(Assert.Single(second.LogicalChildren)).Text);
        Assert.Same(control.Items[0], control.ItemContainerGenerator.ItemFromContainer(first));
    }

    [Fact]
    public void ItemsPanel_hosts_containers_in_inflated_panel()
    {
        var dict = XamlLoader.LoadResourceDictionary(CardDictionary());
        var control = new ItemsControl
        {
            ItemsPanel = Assert.IsType<ItemsPanelTemplate>(dict["Wrap"]),
        };
        control.AddItem("a");
        control.AddItem("b");
        var host = Assert.IsType<WrapPanel>(Assert.Single(control.LogicalChildren));
        Assert.Equal(2, host.LogicalChildren.Count);
        var container = Assert.IsType<Label>(control.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Contains(container, host.LogicalChildren);
    }

    [Fact]
    public void Non_panel_items_panel_content_is_precise()
    {
        var control = new ItemsControl();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            control.ItemsPanel = new ItemsPanelTemplate("<Button " + TemplateNs + " />"));
        Assert.Contains("Panel", ex.Message);
    }

    [Fact]
    public void Xaml_inline_item_template_and_items_panel()
    {
        var result = XamlLoader.Load($$$"""
            <Window {{{Ns}}}>
              <ItemsControl>
                <ItemsControl.ItemsPanel><ItemsPanelTemplate><StackPanel Orientation="Horizontal" /></ItemsPanelTemplate></ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate><DataTemplate><TextBlock Text="{Binding Title}" /></DataTemplate></ItemsControl.ItemTemplate>
              </ItemsControl>
            </Window>
            """);
        var control = Assert.IsType<ItemsControl>(Assert.IsType<Window>(result.Root).Content);
        Assert.IsType<StackPanel>(control.ItemsPanel?.LoadContent());
        Assert.NotNull(control.ItemTemplate);
        Assert.IsType<StackPanel>(Assert.Single(control.LogicalChildren));
    }

    [Fact]
    public void WrapPanel_flows_rows_and_wraps()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        panel.AddChild(new Border { Width = 60, Height = 20 });
        panel.AddChild(new Border { Width = 60, Height = 20 });
        panel.Measure(new WpfSize(100, 600));
        Assert.Equal(60, panel.DesiredSize.Width);
        Assert.Equal(40, panel.DesiredSize.Height);
        panel.Arrange(new WpfRect(0, 0, 100, 40));
        var children = panel.LogicalChildren;
        Assert.Equal(0, children[0].ArrangedRect.X);
        Assert.Equal(0, children[0].ArrangedRect.Y);
        Assert.Equal(0, children[1].ArrangedRect.X);
        Assert.Equal(20, children[1].ArrangedRect.Y);
    }
}
