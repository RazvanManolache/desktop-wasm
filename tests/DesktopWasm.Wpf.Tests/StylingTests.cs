using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Step 41: Control hierarchy, Style runtime, fonts, routed tokens.</summary>
[ResetAmbientState]
public sealed class StylingTests
{
    private static Button StyledButton()
    {
        var button = new Button();
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Colors.Red)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 20.0));
        style.ApplyTo(button);
        return button;
    }

    [Fact]
    public void Style_applies_brush_and_font_to_control()
    {
        var button = StyledButton();
        Assert.Equal(new SolidColorBrush(Colors.Red).ToRgba(), button.BackgroundRgba);
        Assert.Equal(20.0, button.FontSize);
        Assert.Equal("20px sans-serif", button.FontCss);
        Assert.True(button.Style is null); // ApplyTo alone does not adopt the style.
    }

    [Fact]
    public void Explicit_style_property_applies_and_replaces()
    {
        var button = new Button();
        var first = new Style(typeof(Button));
        first.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Colors.Red)));
        button.Style = first;
        Assert.Equal(new SolidColorBrush(Colors.Red).ToRgba(), button.BackgroundRgba);
        var second = new Style(typeof(Button));
        second.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Colors.Blue)));
        button.Style = second;
        Assert.Same(second, button.Style);
        Assert.Equal(new SolidColorBrush(Colors.Blue).ToRgba(), button.BackgroundRgba);
    }

    [Fact]
    public void Style_basedon_chain_derived_wins()
    {
        var button = new Button();
        var baseStyle = new Style(typeof(Button));
        baseStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Colors.Red)));
        baseStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        var derived = new Style(typeof(Button), baseStyle);
        derived.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Colors.Blue)));
        derived.ApplyTo(button);
        Assert.Equal(new SolidColorBrush(Colors.Blue).ToRgba(), button.BackgroundRgba);
        Assert.Equal(12.0, button.FontSize);
    }

    [Fact]
    public void Style_wrong_target_throws()
    {
        var style = new Style(typeof(Button));
        var ex = Assert.Throws<InvalidOperationException>(() => style.ApplyTo(new TextBox()));
        Assert.Contains("Button", ex.Message);
    }

    [Fact]
    public void Sealed_style_rejects_edits()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Colors.Red)));
        Assert.False(style.IsSealed);
        style.Seal();
        Assert.True(style.IsSealed);
        Assert.Throws<InvalidOperationException>(() =>
            style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Colors.Blue))));
        Assert.Throws<InvalidOperationException>(() => style.ApplyTo(new TextBox()));
    }

    [Fact]
    public void Applying_seals_the_style()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Colors.Red)));
        style.ApplyTo(new Button());
        Assert.True(style.IsSealed);
    }

    [Fact]
    public void Setter_targetname_is_a_precise_boundary()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Colors.Red), "Part"));
        var ex = Assert.Throws<InvalidOperationException>(() => style.ApplyTo(new Button()));
        Assert.Contains("TargetName", ex.Message);
    }

    [Fact]
    public void Implicit_style_from_xaml_resources_applies()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Window.Resources><ResourceDictionary>" +
            "<Style TargetType=\"Button\"><Setter Property=\"Background\" Value=\"Red\"/>" +
            "<Setter Property=\"FontSize\" Value=\"20\"/></Style>" +
            "</ResourceDictionary></Window.Resources>" +
            "<Button Content=\"Hi\"/></Window>").Root);
        var button = Assert.IsType<Button>(window.Content);
        Assert.Equal(new SolidColorBrush(Colors.Red).ToRgba(), button.BackgroundRgba);
        Assert.Equal("20px sans-serif", button.FontCss);
    }

    [Fact]
    public void Explicit_style_from_staticresource_applies()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Window.Resources><ResourceDictionary>" +
            "<Style x:Key=\"Big\" TargetType=\"Button\"><Setter Property=\"FontSize\" Value=\"24\"/></Style>" +
            "</ResourceDictionary></Window.Resources>" +
            "<Button Content=\"Hi\" Style=\"{StaticResource Big}\"/></Window>").Root);
        var button = Assert.IsType<Button>(window.Content);
        Assert.Equal("24px sans-serif", button.FontCss);
    }

    [Fact]
    public void Property_element_style_applies()
    {
        var button = Assert.IsType<Button>(XamlLoader.Load(
            "<Button xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Content=\"Hi\">" +
            "<Button.Style><Style TargetType=\"Button\">" +
            "<Setter Property=\"Background\" Value=\"#FF0000\"/>" +
            "</Style></Button.Style></Button>").Root);
        Assert.Equal(0xFF0000FFu, button.BackgroundRgba);
    }

    [Fact]
    public void Unknown_style_property_is_precise()
    {
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Window.Resources><ResourceDictionary>" +
            "<Style TargetType=\"Button\"><Setter Property=\"Nope\" Value=\"1\"/></Style>" +
            "</ResourceDictionary></Window.Resources></Window>"));
        Assert.Contains("Nope", ex.Message);
    }

    [Fact]
    public void Attached_setter_applies_dockpanel_dock()
    {
        var label = new Label();
        var style = new Style(typeof(Label));
        style.Setters.Add(new Setter { AttachedProperty = "DockPanel.Dock", Value = Dock.Left });
        style.ApplyTo(label);
        Assert.Equal(Dock.Left, label.GetAttached("DockPanel.Dock"));
    }

    [Fact]
    public void Trigger_binding_setter_binds_automation_name()
    {
        // Demo search-box shape: a trigger setter drives
        // AutomationProperties.Name from a {Binding}. The attached owner
        // exposes a real DP (like WPF's AutomationProperties.NameProperty),
        // so the trigger transfer binds instead of failing the lookup.
        var box = new TextBox { Tag = "prompt" };
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter
        {
            AttachedProperty = "AutomationProperties.Name",
            Value = new Binding("Tag") { Source = box },
        });
        style.ApplyTo(box);
        Assert.Equal("prompt", AutomationProperties.GetName(box));
    }

    [Fact]
    public void Trigger_binding_setter_resolves_prefixed_converted_owner()
    {
        // Demo TextFieldAssist shape: a trigger setter drives
        // wpf:TextFieldAssist.IncludeSpellingSuggestions from a {Binding},
        // where wpf: is the BAML-kept xmlns prefix and the owner lives in a
        // converted assembly. The resolver strips the prefix and reads the
        // staged-entry owner like the template transfer.
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = typeof(StylingTests).Assembly;
        try
        {
            var box = new TextBox { Tag = "spell" };
            var style = new Style(typeof(TextBox));
            style.Setters.Add(new Setter
            {
                AttachedProperty = "wpf:TriggerOwnerStub.Echo",
                Value = new Binding("Tag") { Source = box },
            });
            style.ApplyTo(box);
            Assert.Equal("spell", box.GetAttached("TriggerOwnerStub.Echo"));
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    private sealed class TriggerOwnerStub
    {
        public static readonly DependencyProperty EchoProperty =
            DependencyProperty.RegisterAttached("Echo", typeof(string), typeof(TriggerOwnerStub), string.Empty);

        public static Thickness BoxPadding => new Thickness(1, 2, 3, 4);

        public static IValueConverter Upper { get; } = new UpperConverter();
    }

    [Fact]
    public void Style_setter_xstatic_member_resolves_thickness()
    {
        // Demo TextBox shape: Padding="{x:Static
        // wpf:Constants.TextBoxDefaultPadding}". Statics land at apply time
        // through the shared member path (prefix stripped, converted
        // owners scanned), then coerce to the setter type like WPF.
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = typeof(StylingTests).Assembly;
        try
        {
            var box = new TextBox();
            var style = new Style(typeof(TextBox));
            style.Setters.Add(new Setter
            {
                Property = Control.PaddingProperty,
                RawValue = "{x:Static t:TriggerOwnerStub.BoxPadding}",
            });
            style.ApplyTo(box);
            Assert.Equal(new Thickness(1, 2, 3, 4), box.Padding);
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    [Fact]
    public void Xaml_attached_setter_parses()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Window.Resources><ResourceDictionary>" +
            "<Style TargetType=\"Label\"><Setter Property=\"DockPanel.Dock\" Value=\"Top\"/></Style>" +
            "</ResourceDictionary></Window.Resources>" +
            "<Label Content=\"Hi\"/></Window>").Root);
        var label = Assert.IsType<Label>(window.Content);
        Assert.Equal(Dock.Top, label.GetAttached("DockPanel.Dock"));
    }

    [Fact]
    public void EventSetter_binds_code_delegate_to_clr_event()
    {
        var button = new Button();
        var fired = 0;
        var style = new Style(typeof(Button));
        style.Setters.Add(new EventSetter(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) => fired++)));
        style.ApplyTo(button);
        button.RaiseClick();
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Xaml_eventsetter_collects_wire()
    {
        var loaded = XamlLoader.Load(
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Window.Resources><ResourceDictionary>" +
            "<Style TargetType=\"Button\"><EventSetter Event=\"Click\" Handler=\"OnSave\"/></Style>" +
            "</ResourceDictionary></Window.Resources>" +
            "<Button Content=\"Hi\"/></Window>");
        Assert.Contains(loaded.EventWires, w => w.EventName == "Click" && w.HandlerName == "OnSave");
    }

    [Fact]
    public void Control_dependency_properties_registered()
    {
        Assert.Equal("Background", Control.BackgroundProperty.Name);
        Assert.Same(typeof(Control), Control.BackgroundProperty.OwnerType);
        Assert.Same(Control.BackgroundProperty, DependencyProperty.FromName(typeof(Button), "Background"));
        Assert.Same(Control.FontSizeProperty, DependencyProperty.FromName(typeof(Button), "FontSize"));
        Assert.Null(DependencyProperty.FromName(typeof(Button), "Nope"));
    }

    [Fact]
    public void Routed_event_bubbles_target_first()
    {
        var root = new StackPanel();
        var mid = new StackPanel();
        var leaf = new Button();
        root.AddChild(mid);
        mid.AddChild(leaf);
        var token = EventManager.RegisterRoutedEvent("StylingBubble", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Button));
        var order = new List<string>();
        root.AddHandler(token, new RoutedEventHandler((s, e) => order.Add("root")));
        mid.AddHandler(token, new RoutedEventHandler((s, e) => order.Add("mid")));
        leaf.AddHandler(token, new RoutedEventHandler((s, e) => order.Add("leaf")));
        leaf.RaiseEvent(new RoutedEventArgs { RoutedEvent = token });
        Assert.Equal(["leaf", "mid", "root"], order);
    }

    [Fact]
    public void Routed_event_tunnels_root_first_and_direct_stays()
    {
        var root = new StackPanel();
        var leaf = new Button();
        root.AddChild(leaf);
        var tunnel = EventManager.RegisterRoutedEvent("StylingTunnel", RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(Button));
        var direct = EventManager.RegisterRoutedEvent("StylingDirect", RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(Button));
        var order = new List<string>();
        root.AddHandler(tunnel, new RoutedEventHandler((s, e) => order.Add("root")));
        leaf.AddHandler(tunnel, new RoutedEventHandler((s, e) => order.Add("leaf")));
        leaf.RaiseEvent(new RoutedEventArgs { RoutedEvent = tunnel });
        Assert.Equal(["root", "leaf"], order);
        order.Clear();
        root.AddHandler(direct, new RoutedEventHandler((s, e) => order.Add("root")));
        leaf.AddHandler(direct, new RoutedEventHandler((s, e) => order.Add("leaf")));
        leaf.RaiseEvent(new RoutedEventArgs { RoutedEvent = direct });
        Assert.Equal(["leaf"], order);
    }

    [Fact]
    public void Routed_event_handled_skips_plain_but_not_handledtoo()
    {
        var root = new StackPanel();
        var leaf = new Button();
        root.AddChild(leaf);
        var token = EventManager.RegisterRoutedEvent("StylingHandled", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Button));
        var seen = new List<string>();
        leaf.AddHandler(token, new RoutedEventHandler((s, e) => { seen.Add("leaf"); e.Handled = true; }));
        root.AddHandler(token, new RoutedEventHandler((s, e) => seen.Add("root")));
        root.AddHandler(token, new RoutedEventHandler((s, e) => seen.Add("root-too")), true);
        RemoveHandlerCheck(leaf, token, seen);
        leaf.RaiseEvent(new RoutedEventArgs { RoutedEvent = token });
        Assert.Equal(["leaf", "root-too"], seen);
    }

    private static void RemoveHandlerCheck(Button leaf, RoutedEvent token, List<string> seen)
    {
        RoutedEventHandler temp = (s, e) => seen.Add("temp");
        leaf.AddHandler(token, temp);
        leaf.RemoveHandler(token, temp);
    }

    [Fact]
    public void RaiseEvent_requires_token_and_defaults_source()
    {
        var button = new Button();
        Assert.Throws<InvalidOperationException>(() => button.RaiseEvent(new RoutedEventArgs()));
        var token = EventManager.RegisterRoutedEvent("StylingSource", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Button));
        var args = new RoutedEventArgs { RoutedEvent = token };
        button.RaiseEvent(args);
        Assert.Same(button, args.Source);
        Assert.Same(button, args.OriginalSource);
    }

    [Fact]
    public void ThemeMode_identity()
    {
        Assert.Same(ThemeMode.Dark, ThemeMode.Dark);
        Assert.True(ThemeMode.Dark == ThemeMode.Dark);
        Assert.True(ThemeMode.Dark != ThemeMode.Light);
        Assert.True(ThemeMode.System != ThemeMode.Light);
        Assert.Equal("Dark", ThemeMode.Dark.ToString());
    }

    [Fact]
    public void Font_values_parse_and_compare()
    {
        Assert.Equal(FontWeights.SemiBold, StyleValueConverter.ParseFontWeight("SemiBold"));
        Assert.Equal(FontWeights.Bold, StyleValueConverter.ParseFontWeight("700"));
        Assert.Equal(FontWeights.Normal, FontWeight.FromOpenTypeWeight(400));
        Assert.True(FontWeights.Bold > FontWeights.Normal);
        Assert.Equal(FontStyles.Italic, StyleValueConverter.ParseFontStyle("Italic"));
        Assert.Throws<FormatException>(() => StyleValueConverter.ParseFontWeight("Nope"));
        FontFamily family = "Segoe UI";
        Assert.Equal("Segoe UI", family.ToString());
        Assert.Equal(new FontFamily("Segoe UI"), family);
    }

    [Fact]
    public void CornerRadius_parses_and_compares()
    {
        Assert.Equal(new CornerRadius(4), CornerRadius.Parse("4"));
        Assert.Equal(new CornerRadius(1, 2, 3, 4), CornerRadius.Parse("1,2,3,4"));
        Assert.True(new CornerRadius(4) == new CornerRadius(4, 4, 4, 4));
        Assert.True(new CornerRadius(4) != new CornerRadius(5));
        Assert.Throws<FormatException>(() => CornerRadius.Parse("nope"));
    }

    [Fact]
    public void Border_corner_radius_from_xaml()
    {
        var border = Assert.IsType<Border>(XamlLoader.Load(
            "<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" CornerRadius=\"1,2,3,4\"/>").Root);
        Assert.Equal(new CornerRadius(1, 2, 3, 4), border.CornerRadius);
    }

    [Fact]
    public void TextBox_is_TextBoxBase_with_typed_text_changed()
    {
        var box = new TextBox();
        Assert.IsAssignableFrom<TextBoxBase>(box);
        var seen = new List<TextChangedEventArgs>();
        ((TextBoxBase)box).TextChanged += (s, e) => seen.Add(e);
        box.Text = "hi";
        Assert.Single(seen);
        Assert.Equal(UndoAction.None, seen[0].UndoAction);
        Assert.Same(box, seen[0].Source);
    }

    [Fact]
    public void TextBox_selectall_and_replace_selection()
    {
        var box = new TextBox { Text = "hello" };
        box.SelectAll();
        Assert.Equal("hello", box.SelectedText);
        box.SelectedText = "bye";
        Assert.Equal("bye", box.Text);
        Assert.Equal(3, box.CaretIndex);
    }

    [Fact]
    public void TextBox_readonly_blocks_edits_but_selects()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var box = new TextBox { Text = "hello", IsReadOnly = true };
        root.AddChild(box);
        manager.AddRoot(root);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        Assert.True(manager.TryFocus(box));
        box.SelectAll();
        Assert.Equal("hello", box.SelectedText);
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), "Backspace", true, false, false, false));
        Assert.Equal("hello", box.Text);
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), "x", true, false, false, false));
        Assert.Equal("hello", box.Text);
    }

    [Fact]
    public void TextBox_ctrl_a_selects_all()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var box = new TextBox { Text = "hello" };
        root.AddChild(box);
        manager.AddRoot(root);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        Assert.True(manager.TryFocus(box));
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), "a", true, true, false, false));
        Assert.Equal("hello", box.SelectedText);
    }

    private sealed class ClickHarness
    {
        public WasmInputRouter Router { get; } = new();
        public WpfInputManager Manager { get; }
        public StackPanel Root { get; } = new();
        public Button Button { get; } = new();

        public ClickHarness()
        {
            Manager = new WpfInputManager(Router);
            Root.AddChild(Button);
            Manager.AddRoot(Root);
            Root.Measure(new WpfSize(800, 600));
            Root.Arrange(new WpfRect(0, 0, 800, 600));
            Button.Arrange(new WpfRect(10, 10, 200, 30));
        }

        public void Press(float x = 100, float y = 20)
        {
            var handle = new WasmWindowHandle(1);
            Router.DispatchPointer(new WasmPointerArgs(handle, x, y, WasmPointerButton.Left, true)
                { Kind = WasmPointerKind.Down, PointerId = 1 });
            Router.DispatchPointer(new WasmPointerArgs(handle, x, y, WasmPointerButton.Left, false)
                { Kind = WasmPointerKind.Up, PointerId = 1 });
        }
    }

    [Fact]
    public void Button_double_click_raises_mouse_double_click()
    {
        var harness = new ClickHarness();
        var doubles = 0;
        harness.Button.MouseDoubleClick += (s, e) =>
        {
            doubles++;
            Assert.Equal(2, e.ClickCount);
            Assert.Same(Control.MouseDoubleClickEvent, e.RoutedEvent);
        };
        var now = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        Button.Clock = () => now;
        try
        {
            harness.Press();
            now = now.AddMilliseconds(100);
            harness.Press();
            Assert.Equal(1, doubles);
        }
        finally
        {
            Button.Clock = static () => DateTime.UtcNow;
        }
    }

    [Fact]
    public void Button_single_clicks_do_not_double_click()
    {
        var harness = new ClickHarness();
        var doubles = 0;
        harness.Button.MouseDoubleClick += (s, e) => doubles++;
        var now = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        Button.Clock = () => now;
        try
        {
            harness.Press();
            now = now.AddSeconds(5);
            harness.Press();
            Assert.Equal(0, doubles);
        }
        finally
        {
            Button.Clock = static () => DateTime.UtcNow;
        }
    }

    [Fact]
    public void UserControl_hosts_content_and_template_defaults_null()
    {
        var page = new UserControl { Content = new TextBlock { Text = "Hi" } };
        Assert.IsType<TextBlock>(page.Content);
        Assert.Null(page.Template);
        Assert.Throws<InvalidOperationException>(() => new ControlTemplate().LoadContent());
    }

    public sealed class EchoExtension : MarkupExtension
    {
        public string? Text { get; set; }

        public override object? ProvideValue(IServiceProvider serviceProvider) => Text;
    }

    [Fact]
    public void Converted_markup_extension_instantiates_with_props()
    {
        // Demo MainWindow shape: Content="{materialDesign:PackIcon
        // Kind=Menu}". Vendor heads resolve to a converted MarkupExtension
        // (abbreviated head included), take named props, and provide the
        // value onto the target like WPF.
        const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = typeof(StylingTests).Assembly;
        try
        {
            var root = Assert.IsType<TextBlock>(XamlLoader.Load(
                "<TextBlock " + ns + " xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\" " +
                "Text=\"{t:EchoExtension Text=hi}\" />").Root);
            Assert.Equal("hi", root.Text);
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    public sealed class UpperConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value?.ToString()?.ToUpperInvariant();

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    [Fact]
    public void Binding_xstatic_converter_binds_immediately()
    {
        // Demo shape: Text="{Binding Tag, RelativeSource={RelativeSource
        // Self}, Converter={x:Static ...}}". The static converter resolves
        // inline, so there is no deferred key to flush — the binding
        // activates at load instead of crashing the pending queue.
        const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = typeof(StylingTests).Assembly;
        try
        {
            var root = Assert.IsType<TextBlock>(XamlLoader.Load(
                "<TextBlock " + ns + " xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\" Tag=\"abc\" " +
                "Text=\"{Binding Tag, RelativeSource={RelativeSource Self}, Converter={x:Static t:TriggerOwnerStub.Upper}}\" />").Root);
            Assert.Equal("ABC", root.Text);
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    [Fact]
    public void Markup_attributes_round_trip()
    {
        var content = new ContentPropertyAttribute("Content");
        Assert.Equal("Content", content.Name);
        var part = new TemplatePartAttribute("PART_Box", typeof(TextBox));
        Assert.Equal("PART_Box", part.Name);
        Assert.Same(typeof(TextBox), part.Type);
        // Converted controls declare [TemplatePart(Name = ..., Type = ...)],
        // which needs the parameterless form like WPF.
        var named = new TemplatePartAttribute { Name = "PART_Box", Type = typeof(TextBox) };
        Assert.Equal("PART_Box", named.Name);
        Assert.Same(typeof(TextBox), named.Type);
    }

    [Fact]
    public void SystemColors_core_values()
    {
        Assert.Equal(0x3390FFFFu, SystemColors.HighlightBrush.ToRgba());
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), SystemColors.HighlightTextColor);
        Assert.NotEqual(SystemColors.WindowColor, SystemColors.WindowTextColor);
    }

    [Fact]
    public void Qualified_property_element_wrappers_parse_like_real_xaml()
    {
        // WPFGallery ColorTilesPanelStyle shape: real XAML qualifies wrappers
        // (<Style.Setters>, <Style.Triggers>, <MultiTrigger.Conditions>).
        const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary " + ns + ">" +
            "<Style x:Key=\"Tiles\" TargetType=\"{x:Type Border}\">" +
            "<Style.Setters>" +
            "<Setter Property=\"BorderThickness\" Value=\"1\" />" +
            "<Setter Property=\"BorderBrush\" Value=\"{DynamicResource CardStroke}\" />" +
            "<Setter Property=\"CornerRadius\" Value=\"8\" />" +
            "</Style.Setters>" +
            "<Style.Triggers>" +
            "<MultiTrigger><MultiTrigger.Conditions>" +
            "<Condition Property=\"IsEnabled\" Value=\"False\" />" +
            "</MultiTrigger.Conditions>" +
            "<Setter Property=\"Opacity\" Value=\"0.5\" />" +
            "</MultiTrigger></Style.Triggers>" +
            "</Style></ResourceDictionary>");
        Assert.True(dict.TryGetValue("Tiles", out var found));
        var style = Assert.IsType<Style>(found);
        Assert.Equal(typeof(Border), style.TargetType);
        Assert.Equal(3, style.Setters.Count);
        var trigger = Assert.IsType<MultiTrigger>(Assert.Single(style.Triggers));
        Assert.Single(trigger.Conditions);
        var border = new Border();
        style.ApplyTo(border);
        Assert.Equal(new Thickness(1), border.BorderThickness);
        Assert.Equal(new CornerRadius(8), border.CornerRadius);
        Assert.Null(border.BorderBrush); // DynamicResource defers past missing keys.
    }

    [Fact]
    public void Style_trigger_with_vendor_attached_property_evaluates()
    {
        // ProgressBar theme shape: Trigger Property="wpf:TransitionAssist.DisableTransitions"
        // reads the converted assist DP (canonicalized at parse; evaluated
        // without XAML scope). The default (false) activates the False trigger.
        var directory = FindStagedAppDirectory("material-design");
        var themes = Directory.GetFiles(directory, "MaterialDesignThemes.Wpf.*.bin");
        System.Reflection.Assembly.Load(File.ReadAllBytes(Assert.Single(themes)));
        WasmDispatcher.Current = new WasmDispatcher(new DesktopWasm.Core.WasmDispatcherPump());
        var window = (Window)XamlLoader.Load(
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
            "xmlns:wpf=\"clr-namespace:MaterialDesignThemes.Wpf\">" +
            "<Window.Resources><ResourceDictionary>" +
            "<Style TargetType=\"{x:Type ProgressBar}\">" +
            "<Setter Property=\"Opacity\" Value=\"0.5\" />" +
            "<Style.Triggers>" +
            "<Trigger Property=\"wpf:TransitionAssist.DisableTransitions\" Value=\"False\">" +
            "<Setter Property=\"Opacity\" Value=\"1\" />" +
            "</Trigger>" +
            "</Style.Triggers>" +
            "</Style>" +
            "</ResourceDictionary></Window.Resources>" +
            "<ProgressBar />" +
            "</Window>").Root;
        var bar = Assert.IsType<ProgressBar>(window.Content);
        Assert.Equal(1.0, bar.Opacity);
    }

    private static string FindStagedAppDirectory(string slug)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "DesktopWasm.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return System.IO.Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", slug);
    }
}
