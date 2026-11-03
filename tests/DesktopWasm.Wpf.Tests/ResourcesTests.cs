namespace DesktopWasm.Wpf.Tests;

[Collection("ApplicationState")]

/// <summary>Resources tests: pack URI parsing, the in-memory pack store,
/// dictionary lookup order, FindResource scoping, XAML resources
/// (Window.Resources, entries, Source=, merged dictionaries, StaticResource
/// attribute and property-element forms), DynamicResource resolve/refresh/
/// missing-key tolerance and precise boundaries (BAML complex properties).</summary>
[ResetAmbientState]
public sealed class ResourcesTests
{
    private const string Ns =
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private static Application UseApp()
    {
        // The constructor already makes the app current (WPF parity);
        // callers save the real previous app before calling UseApp.
        return new Application();
    }

    private static void RestoreApp(Application? previous) => Application.Current = previous;

    [Fact]
    public void PackUri_parses_application_paths()
    {
        var p = PackUri.Parse("pack://application:,,,/Resources/Brushes.xaml");
        Assert.Null(p.Assembly);
        Assert.Equal("Resources/Brushes.xaml", p.Path);
    }

    [Fact]
    public void PackUri_parses_assembly_component()
    {
        var p = PackUri.Parse("pack://application:,,,/MyApp;component/Themes/Dark.xaml");
        Assert.Equal("MyApp", p.Assembly);
        Assert.Equal("Themes/Dark.xaml", p.Path);
    }

    [Fact]
    public void PackUri_parses_generated_component_shorthand()
    {
        // Real InitializeComponent calls pass "/App;component/page.xaml".
        var p = PackUri.Parse("/HelloWorld;component/mainwindow.xaml");
        Assert.Equal("HelloWorld", p.Assembly);
        Assert.Equal("mainwindow.xaml", p.Path);
    }

    [Fact]
    public void PackUri_strips_generated_version_stamp_shorthand()
    {
        // Strong-named generated InitializeComponent stamps the version
        // (HandyControlDemo App.g.cs: "/HandyControlDemo;V3.6.0.0;component/app.xaml").
        var p = PackUri.Parse("/HandyControlDemo;V3.6.0.0;component/app.xaml");
        Assert.Equal("HandyControlDemo", p.Assembly);
        Assert.Equal("app.xaml", p.Path);
    }

    [Fact]
    public void PackUri_strips_generated_version_stamp_pack_form()
    {
        var p = PackUri.Parse("pack://application:,,,/HandyControlDemo;V3.6.0.0;component/app.xaml");
        Assert.Equal("HandyControlDemo", p.Assembly);
        Assert.Equal("app.xaml", p.Path);
    }

    [Fact]
    public void PackUri_version_stamp_key_matches_unqualified()
    {
        Assert.Equal(PackUri.KeyFor("/HandyControlDemo;component/app.xaml"),
            PackUri.KeyFor("/HandyControlDemo;V3.6.0.0;component/app.xaml"));
    }

    [Fact]
    public void PackUri_rejects_unknown_assembly_qualifiers()
    {
        Assert.Throws<FormatException>(() => PackUri.Parse("/Asm;Beta;component/x.xaml"));
        Assert.Throws<FormatException>(() => PackUri.Parse("/Asm;V;component/x.xaml"));
        Assert.Throws<FormatException>(() => PackUri.Parse("/Asm;Vab;component/x.xaml"));
        Assert.Throws<FormatException>(() => PackUri.Parse("pack://application:,,,/Asm;Beta;component/x.xaml"));
        Assert.Throws<FormatException>(() => PackUri.Parse("pack://application:,,,/;V1.0.0.0;component/x.xaml"));
    }

    [Fact]
    public void PackUri_treats_siteoforigin_like_application()
    {
        var p = PackUri.Parse("pack://siteoforigin:,,,/Data.xaml");
        Assert.Null(p.Assembly);
        Assert.Equal("Data.xaml", p.Path);
    }

    [Fact]
    public void PackUri_accepts_relative_paths_and_normalizes()
    {
        var p = PackUri.Parse(@"Resources\Brushes.xaml");
        Assert.Null(p.Assembly);
        Assert.Equal("Resources/Brushes.xaml", p.Path);
        Assert.Equal(PackUri.KeyFor("PACK://application:,,,/resources/brushes.xaml"),
            PackUri.KeyFor("Resources\\Brushes.xaml"));
    }

    [Fact]
    public void PackUri_rejects_garbage()
    {
        Assert.Throws<FormatException>(() => PackUri.Parse("pack://unknown:,,,/x.xaml"));
        Assert.Throws<FormatException>(() => PackUri.Parse("pack://application:,,,"));
        Assert.Throws<FormatException>(() => PackUri.Parse("http://example.com/x.xaml"));
        Assert.Throws<FormatException>(() => PackUri.Parse("pack://application:,,,/Asm;part/x.xaml"));
        Assert.Throws<FormatException>(() => PackUri.Parse("   "));
    }

    [Fact]
    public void MergedDictionaries_matches_framework_collection_shape()
    {
        // Demo MainWindow shape: converted code calls
        // get_MergedDictionaries(), compiled against Collection<...>.
        // A List-typed property misses at JIT like a missing member.
        var dict = new ResourceDictionary();
        Assert.IsType<System.Collections.ObjectModel.Collection<ResourceDictionary>>(dict.MergedDictionaries);
        dict.MergedDictionaries.Add(new ResourceDictionary());
        Assert.Equal(1, dict.MergedDictionaries.Count);
    }

    [Fact]
    public void Dictionary_lookup_prefers_local_then_last_merged()
    {
        var dict = new ResourceDictionary();
        var first = new ResourceDictionary();
        var second = new ResourceDictionary();
        dict.MergedDictionaries.Add(first);
        dict.MergedDictionaries.Add(second);
        first.Add("K", "first");
        second.Add("K", "second");
        second.Add("Only", 1);
        Assert.True(dict.TryGetValue("K", out var v));
        Assert.Equal("second", v);
        Assert.True(dict.TryGetValue("Only", out v));
        Assert.Equal(1, v);
        Assert.False(dict.TryGetValue("Missing", out _));
        dict.Add("K", "local");
        Assert.True(dict.TryGetValue("K", out v));
        Assert.Equal("local", v);
        Assert.True(dict.Contains("K"));
        Assert.False(dict.Contains("Missing"));
        Assert.Throws<InvalidOperationException>(() => dict.Add("K", "dup"));
    }

    [Fact]
    public void ResourceToString_converts_supported_values()
    {
        Assert.Equal("hi", Application.ResourceToString("hi", "K"));
        Assert.Equal("True", Application.ResourceToString(true, "K"));
        Assert.Equal("20", Application.ResourceToString(20, "K"));
        Assert.Equal("1.5", Application.ResourceToString(1.5, "K"));
        // Internal 0xRRGGBBAA round-trips through the #AARRGGBB XAML form.
        Assert.Equal("#8000FF00", Application.ResourceToString(0x00FF0080u, "K"));
        Assert.Throws<InvalidOperationException>(() => Application.ResourceToString(null, "K"));
        Assert.Throws<InvalidOperationException>(() => Application.ResourceToString(new object(), "K"));
    }

    [Fact]
    public void FindResource_walks_element_parent_application()
    {
        var previous = Application.Current;
        try
        {
            var app = UseApp();
            app.Resources.Add("App", "app");
            var window = new Window();
            window.Resources.Add("Win", "win");
            var panel = new StackPanel();
            panel.Resources.Add("Panel", "panel");
            window.Content = panel;
            var text = new TextBlock();
            panel.AddChild(text);

            Assert.True(text.TryFindResource("Panel", out var v));
            Assert.Equal("panel", v);
            Assert.True(text.TryFindResource("Win", out v));
            Assert.Equal("win", v);
            Assert.True(text.TryFindResource("App", out v));
            Assert.Equal("app", v);
            Assert.False(text.TryFindResource("Missing", out _));
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void Xaml_window_resources_resolve_static_resources()
    {
        var previous = Application.Current;
        try
        {
            UseApp();
            const string xaml =
                "<Window " + Ns + ">" +
                "<Window.Resources><ResourceDictionary>" +
                "<SolidColorBrush x:Key=\"Brand\" Color=\"#FF0066CC\"/>" +
                "<String x:Key=\"Greet\" xmlns:sys=\"http://example\">Hello</String>" +
                "<Double x:Key=\"TitleSize\">20</Double>" +
                "</ResourceDictionary></Window.Resources>" +
                "<TextBlock Text=\"{StaticResource Greet}\" Foreground=\"{StaticResource Brand}\" FontSize=\"{StaticResource TitleSize}\"/>" +
                "</Window>";
            var window = (Window)XamlLoader.Load(xaml).Root;
            var text = Assert.IsType<TextBlock>(window.Content);
            Assert.Equal("Hello", text.Text);
            Assert.Equal(0x0066CCFFu, text.ForegroundRgba);
            Assert.Equal("20px sans-serif", text.FontCss);
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void Xaml_static_resource_reads_ancestor_scope()
    {
        var previous = Application.Current;
        try
        {
            UseApp();
            const string xaml =
                "<Window " + Ns + ">" +
                "<Window.Resources><ResourceDictionary>" +
                "<String x:Key=\"Name\">Ada</String>" +
                "</ResourceDictionary></Window.Resources>" +
                "<StackPanel><TextBlock Text=\"{StaticResource Name}\"/></StackPanel>" +
                "</Window>";
            var window = (Window)XamlLoader.Load(xaml).Root;
            var panel = Assert.IsType<StackPanel>(window.Content);
            var text = Assert.IsType<TextBlock>(Assert.Single(panel.LogicalChildren));
            Assert.Equal("Ada", text.Text);
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void Xaml_static_resource_applies_to_attached_properties()
    {
        var previous = Application.Current;
        try
        {
            UseApp();
            const string xaml =
                "<Window " + Ns + ">" +
                "<Window.Resources><ResourceDictionary>" +
                "<Int32 x:Key=\"Row\">1</Int32>" +
                "</ResourceDictionary></Window.Resources>" +
                "<Grid><TextBlock Grid.Row=\"{StaticResource Row}\" Text=\"x\"/></Grid>" +
                "</Window>";
            var window = (Window)XamlLoader.Load(xaml).Root;
            var grid = Assert.IsType<Grid>(window.Content);
            var text = Assert.IsType<TextBlock>(Assert.Single(grid.LogicalChildren));
            Assert.Equal(1, text.GetAttached("Grid.Row"));
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void Xaml_static_resource_property_element_form()
    {
        var previous = Application.Current;
        try
        {
            UseApp();
            const string xaml =
                "<Window " + Ns + ">" +
                "<Window.Resources><ResourceDictionary>" +
                "<String x:Key=\"Hi\">Hey</String>" +
                "</ResourceDictionary></Window.Resources>" +
                "<TextBlock><TextBlock.Text>{StaticResource Hi}</TextBlock.Text></TextBlock>" +
                "</Window>";
            var window = (Window)XamlLoader.Load(xaml).Root;
            Assert.Equal("Hey", Assert.IsType<TextBlock>(window.Content).Text);
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void Xaml_source_dictionary_loads_from_pack_store()
    {
        var previous = Application.Current;
        try
        {
            var app = UseApp();
            app.RegisterPackResource("pack://application:,,,/Theme.xaml",
                "<ResourceDictionary " + Ns + ">" +
                "<SolidColorBrush x:Key=\"Theme\" Color=\"#FF112233\"/>" +
                "</ResourceDictionary>");
            const string xaml =
                "<Window " + Ns + ">" +
                "<Window.Resources><ResourceDictionary>" +
                "<ResourceDictionary.MergedDictionaries>" +
                "<ResourceDictionary Source=\"pack://application:,,,/Theme.xaml\"/>" +
                "</ResourceDictionary.MergedDictionaries>" +
                "</ResourceDictionary></Window.Resources>" +
                "<TextBlock Text=\"t\" Foreground=\"{StaticResource Theme}\"/>" +
                "</Window>";
            var window = (Window)XamlLoader.Load(xaml).Root;
            Assert.Equal(0x112233FFu, Assert.IsType<TextBlock>(window.Content).ForegroundRgba);
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void Template_static_resource_defers_past_hydration_order()
    {
        // Native parity (B-defer): a template-internal StaticResource
        // resolves in its defining-dictionary scope, so a key from a LATER
        // sibling dictionary is invisible even though the live application
        // scope carries it. The application load succeeds (native
        // B-DEFER-LOAD), but applying the template fails naming the key
        // (native B-DEFER-LOADCONTENT / hosted apply).
        var previous = Application.Current;
        try
        {
            var app = UseApp();
            app.RegisterPackResource("pack://application:,,,/Early.xaml",
                "<ResourceDictionary " + Ns + ">" +
                "<Style x:Key=\"WithTemplate\" TargetType=\"Button\">" +
                "<Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"{x:Type Button}\">" +
                "<TextBlock Style=\"{StaticResource LateStyle}\" />" +
                "</ControlTemplate></Setter.Value></Setter></Style>" +
                "</ResourceDictionary>");
            app.RegisterPackResource("pack://application:,,,/Late.xaml",
                "<ResourceDictionary " + Ns + ">" +
                "<Style x:Key=\"LateStyle\" TargetType=\"TextBlock\">" +
                "<Setter Property=\"FontSize\" Value=\"20\" />" +
                "</Style></ResourceDictionary>");
            const string xaml =
                "<Application.Resources " + Ns + ">" +
                "<ResourceDictionary><ResourceDictionary.MergedDictionaries>" +
                "<ResourceDictionary Source=\"pack://application:,,,/Early.xaml\"/>" +
                "<ResourceDictionary Source=\"pack://application:,,,/Late.xaml\"/>" +
                "</ResourceDictionary.MergedDictionaries></ResourceDictionary>" +
                "</Application.Resources>";
            using var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(xaml));
            reader.MoveToContent();
            var loadEx = Record.Exception(() => XamlLoader.HydrateApplicationResources(app, reader));
            Assert.Null(loadEx);
            var style = Assert.IsType<Style>(app.Resources["WithTemplate"]);
            var template = Assert.IsType<ControlTemplate>(Assert.IsType<Setter>(style.Setters[0]).Value);
            Assert.IsType<TextBlock>(template.VisualTree);
            var late = Assert.IsType<Style>(app.Resources["LateStyle"]);
            Assert.Single(late.Setters);
            var host = new Button();
            var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            {
                host.Style = style;
                host.ApplyTemplate();
            });
            var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("LateStyle", inner.Message, StringComparison.Ordinal);
            Assert.Contains("template scope", inner.Message, StringComparison.Ordinal);
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void Template_static_resource_resolves_earlier_sibling_style()
    {
        // Native parity (UNSTALL gallery adjudication): a template-internal
        // StaticResource Style resolves when its key lands in the defining
        // dictionary or an EARLIER sibling merge (the gallery's PageStyles
        // before PageHeader), even though the live application scope carries
        // it either way. Only LATER-sibling keys fail at apply (B-defer).
        // Verified against the native WPF oracle in reports/UNSTALL-wpf.md
        // (gallery order applies with FontSize 28; reversed order fails).
        var previous = Application.Current;
        try
        {
            var app = UseApp();
            app.RegisterPackResource("pack://application:,,,/EarlyStyle.xaml",
                "<ResourceDictionary " + Ns + ">" +
                "<Style x:Key=\"EarlyStyle\" TargetType=\"TextBlock\">" +
                "<Setter Property=\"FontSize\" Value=\"20\" />" +
                "</Style></ResourceDictionary>");
            app.RegisterPackResource("pack://application:,,,/LateTemplate.xaml",
                "<ResourceDictionary " + Ns + ">" +
                "<Style x:Key=\"WithEarlierTemplate\" TargetType=\"Button\">" +
                "<Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"{x:Type Button}\">" +
                "<TextBlock x:Name=\"Inner\" Style=\"{StaticResource EarlyStyle}\" />" +
                "</ControlTemplate></Setter.Value></Setter></Style>" +
                "</ResourceDictionary>");
            const string xaml =
                "<Application.Resources " + Ns + ">" +
                "<ResourceDictionary><ResourceDictionary.MergedDictionaries>" +
                "<ResourceDictionary Source=\"pack://application:,,,/EarlyStyle.xaml\"/>" +
                "<ResourceDictionary Source=\"pack://application:,,,/LateTemplate.xaml\"/>" +
                "</ResourceDictionary.MergedDictionaries></ResourceDictionary>" +
                "</Application.Resources>";
            using var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(xaml));
            reader.MoveToContent();
            var loadEx = Record.Exception(() => XamlLoader.HydrateApplicationResources(app, reader));
            Assert.Null(loadEx);
            var style = Assert.IsType<Style>(app.Resources["WithEarlierTemplate"]);
            var host = new Button();
            var applyEx = Record.Exception(() =>
            {
                host.Style = style;
                host.ApplyTemplate();
            });
            Assert.Null(applyEx);
            var inner = Assert.IsType<TextBlock>(host.Template?.FindName("Inner", host));
            Assert.Equal(20, inner.FontSize);
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void Template_static_resource_missing_key_fails_hydration()
    {
        var previous = Application.Current;
        try
        {
            var app = UseApp();
            app.RegisterPackResource("pack://application:,,,/Early.xaml",
                "<ResourceDictionary " + Ns + ">" +
                "<Style x:Key=\"WithTemplate\" TargetType=\"Button\">" +
                "<Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"{x:Type Button}\">" +
                "<TextBlock Style=\"{StaticResource Nope}\" />" +
                "</ControlTemplate></Setter.Value></Setter></Style>" +
                "</ResourceDictionary>");
            const string xaml =
                "<Application.Resources " + Ns + ">" +
                "<ResourceDictionary><ResourceDictionary.MergedDictionaries>" +
                "<ResourceDictionary Source=\"pack://application:,,,/Early.xaml\"/>" +
                "</ResourceDictionary.MergedDictionaries></ResourceDictionary>" +
                "</Application.Resources>";
            using var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(xaml));
            reader.MoveToContent();
            var ex = Assert.Throws<XamlLoadException>(() =>
                XamlLoader.HydrateApplicationResources(app, reader));
            Assert.Contains("template scope", ex.Message, StringComparison.Ordinal);
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void Xaml_missing_static_resource_is_precise()
    {
        var previous = Application.Current;
        try
        {
            UseApp();
            const string xaml = "<Window " + Ns + "><TextBlock Text=\"{StaticResource Nope}\"/></Window>";
            var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(xaml));
            Assert.Contains("StaticResource 'Nope' was not found", ex.Message, StringComparison.Ordinal);
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void Xaml_dynamic_resource_resolves_and_refreshes()
    {
        var previous = Application.Current;
        try
        {
            var app = UseApp();
            app.Resources.Add("Greet", "Hi");
            const string xaml = "<Window " + Ns + "><TextBlock Text=\"{DynamicResource Greet}\"/></Window>";
            var window = (Window)XamlLoader.Load(xaml).Root;
            var text = Assert.IsType<TextBlock>(window.Content);
            Assert.Equal("Hi", text.Text);
            app.Resources["Greet"] = "Yo";
            Assert.Equal("Yo", text.Text);
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void Xaml_dynamic_resource_missing_key_stays_unset()
    {
        var previous = Application.Current;
        try
        {
            var app = UseApp();
            const string xaml = "<Window " + Ns + "><TextBlock Text=\"late\" Foreground=\"{DynamicResource Later}\"/></Window>";
            var window = (Window)XamlLoader.Load(xaml).Root;
            var text = Assert.IsType<TextBlock>(window.Content);
            Assert.Equal("late", text.Text);
            app.Resources.Add("Later", "#FF112233");
            Assert.Equal(0x112233FFu, text.ForegroundRgba);
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void Automation_id_setter_applies_and_reads_back()
    {
        // MaterialDesign drawer item shape: the item container style stamps
        // AutomationProperties.AutomationId, which must resolve like its
        // Name/HelpText siblings instead of failing item generation.
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary " + Ns + ">" +
            "<Style x:Key=\"Ids\" TargetType=\"Button\">" +
            "<Setter Property=\"AutomationProperties.AutomationId\" Value=\"DemoButton\" />" +
            "</Style></ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Ids"]);
        var button = new Button();
        style.ApplyTo(button);
        Assert.Equal("DemoButton", AutomationProperties.GetAutomationId(button));
        Assert.Equal("DemoButton", button.GetAttached("AutomationProperties.AutomationId"));
        var parsed = XamlLoader.Load(
            "<Window " + Ns + "><Button AutomationProperties.AutomationId=\"Direct\" /></Window>");
        Assert.Equal("Direct", AutomationProperties.GetAutomationId(
            Assert.IsType<Button>(Assert.IsType<Window>(parsed.Root).Content)));
    }

    [Fact]
    public void TextBox_style_wrapping_and_keyboard_navigation_attach()
    {
        // WPFGallery SelectionTextBox shape: TextWrapping + attached
        // KeyboardNavigation setter on a TextBox style.
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary " + Ns + ">" +
            "<Style x:Key=\"Selection\" TargetType=\"TextBox\">" +
            "<Setter Property=\"TextWrapping\" Value=\"Wrap\" />" +
            "<Setter Property=\"KeyboardNavigation.IsTabStop\" Value=\"False\" />" +
            "</Style></ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Selection"]);
        var box = new TextBox();
        style.ApplyTo(box);
        Assert.Equal(TextWrapping.Wrap, box.TextWrapping);
        Assert.Equal(false, box.GetAttached("KeyboardNavigation.IsTabStop"));
        var parsed = XamlLoader.Load(
            "<Window " + Ns + "><TextBox TextWrapping=\"Wrap\" /></Window>");
        Assert.Equal(TextWrapping.Wrap, Assert.IsType<TextBox>(Assert.IsType<Window>(parsed.Root).Content).TextWrapping);
    }

    [Fact]
    public void Control_style_retained_knobs_parse_and_apply()
    {
        // WPFGallery ControlExample shape: pixel snapping + theme opt-out
        // style setters plus Focusable attribute parsing.
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary " + Ns + ">" +
            "<Style x:Key=\"Plain\" TargetType=\"Button\">" +
            "<Setter Property=\"SnapsToDevicePixels\" Value=\"True\" />" +
            "<Setter Property=\"OverridesDefaultStyle\" Value=\"True\" />" +
            "</Style></ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Plain"]);
        var button = new Button();
        style.ApplyTo(button);
        Assert.True(button.SnapsToDevicePixels);
        Assert.True(button.OverridesDefaultStyle);
        var parsed = XamlLoader.Load(
            "<Window " + Ns + "><Button Focusable=\"False\" /></Window>");
        Assert.False(Assert.IsType<Button>(Assert.IsType<Window>(parsed.Root).Content).Focusable);
    }

    [Fact]
    public void Xaml_resource_entries_validate()
    {
        var previous = Application.Current;
        try
        {
            UseApp();
            // Missing x:Key.
            var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
                "<Window " + Ns + "><Window.Resources><ResourceDictionary>" +
                "<String>NoKey</String></ResourceDictionary></Window.Resources></Window>"));
            Assert.Contains("needs x:Key", ex.Message, StringComparison.Ordinal);
            // Empty keyed Style is valid (native parity: B-VALIDATE-B accepts
            // <Style x:Key="S"/> with no TargetType and no setters).
            var accepted = (Window)XamlLoader.Load(
                "<Window " + Ns + "><Window.Resources><ResourceDictionary>" +
                "<Style x:Key=\"S\"/></ResourceDictionary></Window.Resources></Window>").Root;
            var emptyStyle = Assert.IsType<Style>(accepted.Resources["S"]);
            Assert.Empty(emptyStyle.Setters);
            Assert.Empty(emptyStyle.Triggers);
            // Duplicate key.
            ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
                "<Window " + Ns + "><Window.Resources><ResourceDictionary>" +
                "<String x:Key=\"K\">a</String><String x:Key=\"K\">b</String>" +
                "</ResourceDictionary></Window.Resources></Window>"));
            Assert.Contains("Duplicate resource key", ex.Message, StringComparison.Ordinal);
            // Source with no Application.Current.
            Application.Current = null;
            ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
                "<Window " + Ns + "><Window.Resources><ResourceDictionary>" +
                "<ResourceDictionary.MergedDictionaries>" +
                "<ResourceDictionary Source=\"Theme.xaml\"/>" +
                "</ResourceDictionary.MergedDictionaries>" +
                "</ResourceDictionary></Window.Resources></Window>"));
            Assert.Contains("no Application.Current", ex.Message, StringComparison.Ordinal);
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void Clr_resource_binding_attribute_attaches_live()
    {
        // MaterialDesign PopupBox shape: a PackIcon resource binds
        // Foreground to FindAncestor PopupBox.Foreground. Resource
        // attributes accept live bindings (DP and CLR targets); they
        // evaluate under engine refresh semantics.
        var previous = Application.Current;
        try
        {
            UseApp();
            var dict = XamlLoader.LoadResourceDictionary(
                "<ResourceDictionary " + Ns + ">" +
                "<Border x:Key=\"B\" Width=\"24\" " +
                "Tag=\"{Binding RelativeSource={RelativeSource Self}, Path=Width}\" " +
                "MinWidth=\"{Binding RelativeSource={RelativeSource Self}, Path=Width}\" />" +
                "</ResourceDictionary>");
            var border = Assert.IsType<Border>(dict["B"]);
            Assert.Equal(24.0, border.Tag);
            Assert.Equal(24.0, border.MinWidth);
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void Clr_resource_thickness_attribute_converts()
    {
        // MaterialDesign PopupBox shape: a PackIcon resource carries
        // Margin="4,0,4,0". CLR resource attributes convert through the same
        // text path as template attributes.
        var previous = Application.Current;
        try
        {
            UseApp();
            var dict = XamlLoader.LoadResourceDictionary(
                "<ResourceDictionary " + Ns + ">" +
                "<Border x:Key=\"B\" Margin=\"4,0,4,0\" />" +
                "</ResourceDictionary>");
            Assert.Equal(new Thickness(4, 0, 4, 0), Assert.IsType<Border>(dict["B"]).Margin);
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void Xaml_source_without_registration_is_precise()
    {
        var previous = Application.Current;
        try
        {
            UseApp();
            var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
                "<Window " + Ns + "><Window.Resources><ResourceDictionary>" +
                "<ResourceDictionary.MergedDictionaries>" +
                "<ResourceDictionary Source=\"Missing.xaml\"/>" +
                "</ResourceDictionary.MergedDictionaries>" +
                "</ResourceDictionary></Window.Resources></Window>"));
            Assert.Contains("No pack resource registered for 'Missing.xaml'", ex.Message, StringComparison.Ordinal);
        }
        finally { RestoreApp(previous); }
    }

    [Fact]
    public void LoadResourceDictionary_parses_standalone_documents()
    {
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary " + Ns + ">" +
            "<Double x:Key=\"W\">150</Double>" +
            "<Boolean x:Key=\"On\">True</Boolean>" +
            "</ResourceDictionary>");
        Assert.True(dict.TryGetValue("W", out var w));
        Assert.Equal(150.0, w);
        Assert.True(dict.TryGetValue("On", out var on));
        Assert.Equal(true, on);
        var ex = Assert.Throws<XamlLoadException>(() =>
            XamlLoader.LoadResourceDictionary("<Window " + Ns + "/>"));
        Assert.Contains("Resource root must be <ResourceDictionary>", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baml_complex_properties_stay_precise_boundary()
    {
        // PropertyComplexStart (0x07) parses; a malformed complex frame
        // desyncs at the first undecodable byte, which stays a precise
        // error naming the record (never a silent skip or raw crash).
        var bytes = new BamlBuilder()
            .Header().Doc()
            .TypeInfo(0, 0, "Probe.MainWindow")
            .ElementStart(0).ContentProperty(-14)
            .Raw(0x07).Sized((byte)0x24, p => p.Raw(0x00))
            .End().DocEnd().ToArray();
        using var stream = new MemoryStream(bytes);
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("unsupported BAML record 0x00", ex.Message, StringComparison.Ordinal);
    }

    private sealed class BamlBuilder
    {
        private readonly List<byte> _b = new();
        public BamlBuilder Raw(params byte[] b) { _b.AddRange(b); return this; }
        public BamlBuilder U16(ushort v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public BamlBuilder I16(short v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public BamlBuilder Str(string s)
        {
            var ascii = System.Text.Encoding.ASCII.GetBytes(s);
            Seven(ascii.Length);
            _b.AddRange(ascii);
            return this;
        }
        public BamlBuilder Seven(int v)
        {
            uint u = (uint)v;
            while (u >= 0x80) { _b.Add((byte)(u | 0x80)); u >>= 7; }
            _b.Add((byte)u);
            return this;
        }
        public BamlBuilder Sized(byte type, Action<BamlBuilder> payload)
        {
            var inner = new BamlBuilder();
            payload(inner);
            var p = inner._b;
            _b.Add(type);
            _b.Add((byte)(p.Count + 1));
            _b.AddRange(p);
            return this;
        }
        public BamlBuilder Header() => Raw(0x0C, 0x00, 0x00, 0x00)
            .Raw(0x4D, 0x00, 0x53, 0x00, 0x42, 0x00, 0x41, 0x00, 0x4D, 0x00, 0x4C, 0x00)
            .U16(0).U16(0x60).Raw(new byte[] { 0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00, 0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 });
        public BamlBuilder Doc() => Raw(0x01);
        public BamlBuilder End() => Raw(0x04);
        public BamlBuilder DocEnd() => Raw(0x02);
        public BamlBuilder TypeInfo(ushort id, ushort asm, string name) =>
            Sized(0x1D, p => p.U16(id).U16(asm).Str(name));
        public BamlBuilder ElementStart(short type) => Raw(0x03).I16(type).Raw(0x00);
        public BamlBuilder ContentProperty(short member) => Raw(0x2E).I16(member);
        public byte[] ToArray() => _b.ToArray();
    }
}
