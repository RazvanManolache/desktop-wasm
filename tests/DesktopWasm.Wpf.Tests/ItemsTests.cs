using System.Collections.ObjectModel;
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
        // Native oracle (UNSTALL gallery oracle, combo mode): real WPF
        // clamps (DOWN-FROM-LAST=2 UP-FROM-FIRST=0), like the ListBox gate.
        combo.SelectedIndex = 2;
        Key(router, "ArrowDown"); // clamps on the last row
        Assert.Equal(2, combo.SelectedIndex);
        Key(router, "ArrowUp");
        Assert.Equal(1, combo.SelectedIndex);
        combo.SelectedIndex = 0;
        Key(router, "ArrowUp"); // clamps on the first row
        Assert.Equal(0, combo.SelectedIndex);
        Down(router, 200, 15);
        Assert.True(combo.IsDropDownOpen);
        Key(router, "Escape");
        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(0, combo.SelectedIndex);
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

    private sealed class ItemTestStyleSelector(Style styleA, Style styleB) : StyleSelector
    {
        public override Style? SelectStyle(object item, DependencyObject container)
        {
            var text = item?.ToString() ?? string.Empty;
            return text.StartsWith("A") ? styleA : styleB;
        }
    }

    private sealed class ItemTestDataTemplateSelector(DataTemplate templateA, DataTemplate templateB) : DataTemplateSelector
    {
        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            var text = item?.ToString() ?? string.Empty;
            return text.StartsWith("A") ? templateA : templateB;
        }
    }

    [Fact]
    public void ItemsControl_ItemContainerStyle_applies_style_to_generated_containers()
    {
        var style = new Style(typeof(Label));
        style.Setters.Add(new Setter(FrameworkElement.TagProperty, "Styled"));
        var control = new ItemsControl
        {
            ItemContainerStyle = style
        };
        control.AddItem("Original");
        var container = Assert.IsType<Label>(control.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Equal(style, container.Style);
        Assert.Equal("Styled", container.Tag);
        Assert.Equal("Original", container.Content);
    }

    [Fact]
    public void ItemsControl_ItemContainerStyleSelector_selects_style_per_item()
    {
        var styleA = new Style(typeof(Label));
        styleA.Setters.Add(new Setter(FrameworkElement.TagProperty, "AlphaStyle"));
        var styleB = new Style(typeof(Label));
        styleB.Setters.Add(new Setter(FrameworkElement.TagProperty, "BetaStyle"));
        var control = new ItemsControl
        {
            ItemContainerStyleSelector = new ItemTestStyleSelector(styleA, styleB)
        };
        control.AddItem("A1");
        control.AddItem("B1");
        var container0 = Assert.IsType<Label>(control.ItemContainerGenerator.ContainerFromIndex(0));
        var container1 = Assert.IsType<Label>(control.ItemContainerGenerator.ContainerFromIndex(1));
        Assert.Equal(styleA, container0.Style);
        Assert.Equal(styleB, container1.Style);
        Assert.Equal("AlphaStyle", container0.Tag);
        Assert.Equal("BetaStyle", container1.Tag);
        Assert.Equal("A1", container0.Content);
        Assert.Equal("B1", container1.Content);
    }

    [Fact]
    public void ItemsControl_ItemsSource_INotifyCollectionChanged_updates_containers_live()
    {
        var collection = new System.Collections.ObjectModel.ObservableCollection<string> { "One", "Two" };
        var control = new ItemsControl
        {
            ItemsSource = collection
        };
        Assert.Equal(2, control.Items.Count);
        Assert.NotNull(control.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.NotNull(control.ItemContainerGenerator.ContainerFromIndex(1));

        collection.Add("Three");
        Assert.Equal(3, control.Items.Count);
        var container2 = Assert.IsType<Label>(control.ItemContainerGenerator.ContainerFromIndex(2));
        Assert.Equal("Three", container2.Content);
    }

    [Fact]
    public void ItemsControl_DisplayMemberPath_formats_label_content()
    {
        var control = new ItemsControl
        {
            DisplayMemberPath = "Name"
        };
        control.AddItem(new { Name = "Alice", Age = 30 });
        var container = Assert.IsType<Label>(control.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Equal("Alice", container.Content);
    }

    [Fact]
    public void ListBox_ItemTemplateSelector_selects_per_item_data_template()
    {
        var tmplA = new DataTemplate("<TextBlock " + Ns + " Text=\"TypeA\" />");
        var tmplB = new DataTemplate("<TextBlock " + Ns + " Text=\"TypeB\" />");
        var list = new ListBox
        {
            ItemTemplateSelector = new ItemTestDataTemplateSelector(tmplA, tmplB),
            ItemsSource = new[] { "A-Item", "B-Item" }
        };
        Assert.Equal(2, list.Items.Count);
        var row0 = list.Items[0];
        var row1 = list.Items[1];
        Assert.Same(tmplA, row0.ContentTemplate);
        Assert.Same(tmplB, row1.ContentTemplate);
        var txt0 = Assert.IsType<TextBlock>(row0.TemplatedContent);
        var txt1 = Assert.IsType<TextBlock>(row1.TemplatedContent);
        Assert.Equal("TypeA", txt0.Text);
        Assert.Equal("TypeB", txt1.Text);
    }

    [Fact]
    public void ListBox_ItemContainerStyleSelector_applies_style_per_row()
    {
        var styleA = new Style(typeof(ListBoxItem));
        styleA.Setters.Add(new Setter(FrameworkElement.TagProperty, "TagA"));
        var styleB = new Style(typeof(ListBoxItem));
        styleB.Setters.Add(new Setter(FrameworkElement.TagProperty, "TagB"));
        var list = new ListBox
        {
            ItemContainerStyleSelector = new ItemTestStyleSelector(styleA, styleB),
            ItemsSource = new[] { "A-Row", "B-Row" }
        };
        Assert.Equal(2, list.Items.Count);
        Assert.Equal("TagA", list.Items[0].Tag);
        Assert.Equal("TagB", list.Items[1].Tag);
    }

    [Fact]
    public void VirtualizingStackPanel_realizes_only_visible_items_for_large_data_set()
    {
        var panel = new VirtualizingStackPanel();
        var control = new ItemsControl
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = Enumerable.Range(0, 10000).Select(i => $"Item {i}").ToList(),
            Height = 200,
            Width = 300
        };

        // Measure with 200 DIPs height
        control.Measure(new WpfSize(300, 200));
        control.Arrange(new WpfRect(0, 0, 300, 200));

        // Total dataset has 10,000 items, but only a small window should be realized!
        Assert.Equal(10000, control.Items.Count);
        Assert.True(control.ItemContainerGenerator.RealizedCount < 50);
        Assert.True(panel.RealizedCount < 50);
        Assert.True(control.ItemContainerGenerator.IsRealized(0));
        Assert.False(control.ItemContainerGenerator.IsRealized(9999));

        // Bounds cache accurately estimated/measured total extent
        Assert.True(panel.ExtentHeight >= 200000);
        Assert.Equal(200, panel.ViewportHeight);
    }

    [Fact]
    public void VirtualizingStackPanel_bounds_caching_and_scroll_realization()
    {
        var panel = new VirtualizingStackPanel();
        var control = new ItemsControl
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = Enumerable.Range(0, 1000).Select(i => $"Row {i}").ToList(),
            Height = 200,
            Width = 300
        };

        control.Measure(new WpfSize(300, 200));
        control.Arrange(new WpfRect(0, 0, 300, 200));

        var initialRealizedStart = panel.RealizedRange.StartIndex;
        Assert.Equal(0, initialRealizedStart);
        Assert.True(control.ItemContainerGenerator.IsRealized(0));

        // Scroll down by 500 DIPs
        panel.SetVerticalOffset(500);
        control.Measure(new WpfSize(300, 200));
        control.Arrange(new WpfRect(0, 0, 300, 200));

        // Item 0 should now be out of the realization window and de-realized
        Assert.False(control.ItemContainerGenerator.IsRealized(0));
        Assert.True(panel.RealizedRange.StartIndex > 10);
        Assert.True(control.ItemContainerGenerator.RealizedCount < 50);

        // Bounds cache cached measured bounds
        Assert.True(panel.GetItemBounds(panel.RealizedRange.StartIndex) > 0);
        Assert.True(panel.GetTotalExtent(1000) > 0);
    }

    [Fact]
    public void VirtualizingStackPanel_horizontal_virtualization()
    {
        var panel = new VirtualizingStackPanel { Orientation = Orientation.Horizontal };
        var control = new ItemsControl
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = Enumerable.Range(0, 2000).Select(i => $"Col {i}").ToList(),
            Height = 100,
            Width = 250
        };

        control.Measure(new WpfSize(250, 100));
        control.Arrange(new WpfRect(0, 0, 250, 100));

        Assert.Equal(2000, control.Items.Count);
        Assert.True(control.ItemContainerGenerator.RealizedCount < 30);
        Assert.True(panel.ExtentWidth >= 20000);
        Assert.Equal(250, panel.ViewportWidth);
    }

    [Fact]
    public void VirtualizingStackPanel_virtualization_toggle_realizes_all_when_disabled()
    {
        var panel = new VirtualizingStackPanel();
        var control = new ItemsControl
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = Enumerable.Range(0, 30).Select(i => $"Item {i}").ToList(),
            Height = 100,
            Width = 300
        };
        VirtualizingStackPanel.SetIsVirtualizing(control, false);

        control.Measure(new WpfSize(300, 100));
        control.Arrange(new WpfRect(0, 0, 300, 100));

        // When virtualization is disabled, all 30 items are realized
        Assert.Equal(30, control.ItemContainerGenerator.RealizedCount);
        for (var i = 0; i < 30; i++)
        {
            Assert.True(control.ItemContainerGenerator.IsRealized(i));
        }
    }

    [Fact]
    public void VirtualizingStackPanel_selection_preserved_across_scroll_and_derealization()
    {
        var panel = new VirtualizingStackPanel();
        var listView = new ListView
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = Enumerable.Range(0, 1000).Select(i => $"Item {i}").ToList(),
            Height = 200,
            Width = 300
        };

        listView.SelectedIndex = 500;
        Assert.Equal("Item 500", listView.SelectedItem);

        listView.Measure(new WpfSize(300, 200));
        listView.Arrange(new WpfRect(0, 0, 300, 200));

        Assert.True(listView.ItemContainerGenerator.IsRealized(0));
        Assert.False(listView.ItemContainerGenerator.IsRealized(500));

        panel.SetVerticalOffset(panel.GetTotalExtent(500));
        listView.Measure(new WpfSize(300, 200));
        listView.Arrange(new WpfRect(0, 0, 300, 200));

        Assert.True(listView.ItemContainerGenerator.IsRealized(500));
        var container500 = Assert.IsType<ListViewItem>(listView.ItemContainerGenerator.ContainerFromIndex(500));
        Assert.True(container500.IsSelected);
        Assert.True(Selector.GetIsSelected(container500));

        panel.SetVerticalOffset(0);
        listView.Measure(new WpfSize(300, 200));
        listView.Arrange(new WpfRect(0, 0, 300, 200));

        Assert.False(listView.ItemContainerGenerator.IsRealized(500));
        Assert.Equal(500, listView.SelectedIndex);
        Assert.Equal("Item 500", listView.SelectedItem);

        panel.SetVerticalOffset(panel.GetTotalExtent(500));
        listView.Measure(new WpfSize(300, 200));
        listView.Arrange(new WpfRect(0, 0, 300, 200));

        var container500Again = Assert.IsType<ListViewItem>(listView.ItemContainerGenerator.ContainerFromIndex(500));
        Assert.True(container500Again.IsSelected);
    }

    [Fact]
    public void VirtualizingStackPanel_collection_mutation_add_and_remove_incremental()
    {
        var data = new ObservableCollection<string>(Enumerable.Range(0, 500).Select(i => $"Item {i}"));
        var panel = new VirtualizingStackPanel();
        var control = new ItemsControl
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = data,
            Height = 200,
            Width = 300
        };

        control.Measure(new WpfSize(300, 200));
        control.Arrange(new WpfRect(0, 0, 300, 200));

        panel.SetVerticalOffset(300);
        control.Measure(new WpfSize(300, 200));
        control.Arrange(new WpfRect(0, 0, 300, 200));

        var offsetBefore = panel.VerticalOffset;
        Assert.Equal(300, offsetBefore);
        var realizedCountBefore = control.ItemContainerGenerator.RealizedCount;
        Assert.True(realizedCountBefore < 30);

        data.Add("Item 500");
        Assert.Equal(501, control.Items.Count);
        Assert.Equal(300, panel.VerticalOffset);
        Assert.True(control.ItemContainerGenerator.RealizedCount < 30);

        data.RemoveAt(500);
        Assert.Equal(500, control.Items.Count);
        Assert.Equal(300, panel.VerticalOffset);
    }

    [Fact]
    public void VirtualizingStackPanel_collection_mutation_insert_preserves_selected_item()
    {
        var data = new ObservableCollection<string>(Enumerable.Range(0, 200).Select(i => $"Item {i}"));
        var panel = new VirtualizingStackPanel();
        var selector = new ListView
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = data,
            Height = 200,
            Width = 300
        };

        selector.SelectedIndex = 10;
        Assert.Equal("Item 10", selector.SelectedItem);

        data.Insert(0, "Header Item");
        Assert.Equal(11, selector.SelectedIndex);
        Assert.Equal("Item 10", selector.SelectedItem);

        data.RemoveAt(0);
        Assert.Equal(10, selector.SelectedIndex);
        Assert.Equal("Item 10", selector.SelectedItem);

        data.RemoveAt(10);
        Assert.Equal(-1, selector.SelectedIndex);
        Assert.Null(selector.SelectedItem);
    }

    [Fact]
    public void ScrollViewer_virtualizing_items_control_realizes_bounded_containers_and_scrolls()
    {
        var scrollViewer = new ScrollViewer { CanContentScroll = true, Height = 200, Width = 300 };
        var control = new ItemsControl
        {
            ItemsPanel = new ItemsPanelTemplate(() => new VirtualizingStackPanel()),
            ItemsSource = Enumerable.Range(0, 10000).Select(i => $"Row {i}").ToList()
        };
        scrollViewer.Content = control;

        scrollViewer.Measure(new WpfSize(300, 200));
        scrollViewer.Arrange(new WpfRect(0, 0, 300, 200));

        Assert.Equal(10000, control.Items.Count);
        Assert.True(control.ItemContainerGenerator.RealizedCount < 50);
        Assert.True(scrollViewer.ExtentHeight >= 200000);

        scrollViewer.ScrollToVerticalOffset(1200);
        scrollViewer.Measure(new WpfSize(300, 200));
        scrollViewer.Arrange(new WpfRect(0, 0, 300, 200));

        Assert.True(control.ItemContainerGenerator.RealizedCount < 50);
        var panel = Assert.IsType<VirtualizingStackPanel>(control.ItemsHost);
        Assert.True(panel.RealizedRange.StartIndex > 20);
    }

    [Fact]
    public void ListBox_with_virtualizing_panel_realizes_bounded_containers_and_coordinates_selection()
    {
        var list = new ListBox
        {
            ItemsPanel = new ItemsPanelTemplate(() => new VirtualizingStackPanel()),
            ItemsSource = Enumerable.Range(0, 5000).Select(i => $"Entry {i}").ToList(),
            Height = 200,
            Width = 300
        };

        list.Measure(new WpfSize(300, 200));
        list.Arrange(new WpfRect(0, 0, 300, 200));

        Assert.True(list.ItemContainerGenerator.RealizedCount < 50);
        Assert.True(list.Items.Count < 50);

        list.SelectedIndex = 3000;
        Assert.Equal(3000, list.SelectedIndex);

        list.Measure(new WpfSize(300, 200));
        list.Arrange(new WpfRect(0, 0, 300, 200));

        var row3000 = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(3000));
        Assert.True(row3000.IsSelected);
    }

    [Fact]
    public void VirtualizingStackPanel_container_recycling_zero_heap_container_allocations_during_steady_state_scrolling()
    {
        var panel = new VirtualizingStackPanel
        {
            VirtualizationMode = VirtualizationMode.Recycling
        };
        var list = new ListBox
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = Enumerable.Range(0, 10000).Select(i => $"Entry {i:D5}").ToList(),
            Height = 200,
            Width = 300
        };

        // Initial layout pass
        list.Measure(new WpfSize(300, 200));
        list.Arrange(new WpfRect(0, 0, 300, 200));

        // Warm up scroll to reach full steady-state window capacity (11 containers)
        for (var i = 1; i <= 3; i++)
        {
            panel.SetVerticalOffset(i * 24.0);
            list.Measure(new WpfSize(300, 200));
            list.Arrange(new WpfRect(0, 0, 300, 200));
        }

        var steadyStateContainers = list.ItemContainerGenerator.ContainersCreatedCount;
        Assert.True(steadyStateContainers > 0, "Initial containers must be created.");
        Assert.True(steadyStateContainers <= 30, $"Expected bounded steady-state containers (<= 30), got {steadyStateContainers}.");

        // Continuous steady-state scrolling downward across 50 steps
        for (var step = 4; step <= 54; step++)
        {
            panel.SetVerticalOffset(step * 24.0);
            list.Measure(new WpfSize(300, 200));
            list.Arrange(new WpfRect(0, 0, 300, 200));

            // CRITICAL INVARIANT: Zero heap container allocations during continuous steady-state scrolling
            Assert.Equal(steadyStateContainers, list.ItemContainerGenerator.ContainersCreatedCount);
            Assert.True(list.ItemContainerGenerator.RealizedCount <= steadyStateContainers);

            // Verify the recycled container at the actual window start shows correct item data.
            // The window start derives from measured item bounds (~28.6px rows vs 24px scroll
            // steps), so `step` drifts outside the window; ContainerFromIndex realizes on
            // demand per the WPF contract, so only query inside the realized window.
            var visibleIndex = panel.RealizedRange.StartIndex;
            Assert.True(list.ItemContainerGenerator.IsRealized(visibleIndex));
            var container = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(visibleIndex));
            Assert.Equal($"Entry {visibleIndex:D5}", container.Content);
        }

        // Continuous steady-state scrolling upward back across 50 steps
        for (var step = 53; step >= 4; step--)
        {
            panel.SetVerticalOffset(step * 24.0);
            list.Measure(new WpfSize(300, 200));
            list.Arrange(new WpfRect(0, 0, 300, 200));

            // CRITICAL INVARIANT: Zero heap container allocations scrolling back
            Assert.Equal(steadyStateContainers, list.ItemContainerGenerator.ContainersCreatedCount);
        }

        Assert.Equal(steadyStateContainers, list.ItemContainerGenerator.ContainersCreatedCount);
    }

    [Fact]
    public void VirtualizingStackPanel_container_recycling_preserves_selection_and_cleans_recycled_containers()
    {
        var panel = new VirtualizingStackPanel
        {
            VirtualizationMode = VirtualizationMode.Recycling
        };
        var list = new ListBox
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = Enumerable.Range(0, 5000).Select(i => $"Item {i}").ToList(),
            Height = 200,
            Width = 300
        };

        // Pre-select item 42 (ListBox brings item into view by default)
        list.SelectedIndex = 42;
        Assert.Equal(42, list.SelectedIndex);

        // Layout at item 42: container 42 is realized and selected
        list.Measure(new WpfSize(300, 200));
        list.Arrange(new WpfRect(0, 0, 300, 200));

        Assert.True(list.ItemContainerGenerator.IsRealized(42));
        var container42 = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(42));
        Assert.True(container42.IsSelected, "Item 42 container must be selected.");

        // Scroll far away to item 2000: container 42 is recycled into the pool
        panel.SetVerticalOffset(panel.GetTotalExtent(2000));
        list.Measure(new WpfSize(300, 200));
        list.Arrange(new WpfRect(0, 0, 300, 200));

        Assert.False(list.ItemContainerGenerator.IsRealized(42), "Item 42 must be de-realized at offset 2000.");

        // Verify unselected items at 2000 reusing recycled containers are NOT marked selected
        var container2000 = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(2000));
        Assert.False(container2000.IsSelected, "Recycled container for unselected item 2000 must not inherit selection.");

        // Scroll back to item 42: container is recycled back and has selection restored
        panel.SetVerticalOffset(panel.GetTotalExtent(42));
        list.Measure(new WpfSize(300, 200));
        list.Arrange(new WpfRect(0, 0, 300, 200));

        Assert.True(list.ItemContainerGenerator.IsRealized(42));
        var container42Again = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(42));
        Assert.True(container42Again.IsSelected, "Selection must be restored when item 42 is re-realized.");
    }

    [Fact]
    public void ListView_container_recycling_zero_heap_container_allocations_during_steady_state_scrolling()
    {
        var panel = new VirtualizingStackPanel
        {
            VirtualizationMode = VirtualizationMode.Recycling
        };
        var listView = new ListView
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = Enumerable.Range(0, 8000).Select(i => $"Row {i:D4}").ToList(),
            Height = 200,
            Width = 300
        };

        // Initial layout pass
        listView.Measure(new WpfSize(300, 200));
        listView.Arrange(new WpfRect(0, 0, 300, 200));

        // Warm up scroll to reach full steady-state window capacity (11 containers)
        for (var i = 1; i <= 3; i++)
        {
            panel.SetVerticalOffset(i * 24.0);
            listView.Measure(new WpfSize(300, 200));
            listView.Arrange(new WpfRect(0, 0, 300, 200));
        }

        var steadyStateContainers = listView.ItemContainerGenerator.ContainersCreatedCount;
        Assert.True(steadyStateContainers > 0);

        for (var step = 4; step <= 34; step++)
        {
            panel.SetVerticalOffset(step * 24.0);
            listView.Measure(new WpfSize(300, 200));
            listView.Arrange(new WpfRect(0, 0, 300, 200));

            // CRITICAL INVARIANT: Zero heap container allocations during continuous steady-state scrolling
            Assert.Equal(steadyStateContainers, listView.ItemContainerGenerator.ContainersCreatedCount);
            // Same window-start discipline as the ListBox test above: `step` drifts outside
            // the measured window, and ContainerFromIndex realizes on demand.
            var visibleRow = panel.RealizedRange.StartIndex;
            Assert.True(listView.ItemContainerGenerator.IsRealized(visibleRow));
            var lvi = Assert.IsType<ListViewItem>(listView.ItemContainerGenerator.ContainerFromIndex(visibleRow));
            Assert.Equal($"Row {visibleRow:D4}", lvi.Content);
        }
    }
}
