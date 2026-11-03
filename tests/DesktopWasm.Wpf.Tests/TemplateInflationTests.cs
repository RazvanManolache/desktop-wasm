using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Template cluster: ControlTemplate/DataTemplate inflation for the
/// ScreenToGif control pattern (Template.FindName parts, TemplateBinding,
/// triggers) and the Gallery boot-chain templates (PageHeader, BorderlessButton),
/// plus the DataTemplateSelector/UniformGrid/StretchDirection/FrameworkTemplate
/// surface the gif manifest gaps name. Mirrors original call sites:
/// SelectControl.Template.FindName("StatusControlGrid"), PageHeader XAML,
/// MainWindow BorderlessButtonStyle, ComboBoxItemTemplateSelector.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class TemplateInflationTests : IDisposable
{
    private readonly Application? _previousApp = Application.Current;

    public void Dispose()
    {
        Application.Current = _previousApp;
        XamlLoader.CustomElementSource = null;
    }

    private sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is true ? Visibility.Visible : Visibility.Hidden;

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is Visibility.Visible;
    }

    private sealed class EmptyToVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            string.IsNullOrEmpty(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static void UseTestAssembly() =>
        XamlLoader.CustomElementSource = typeof(TemplateInflationTests).Assembly;

    private static Application SeedApp()
    {
        var app = new Application();
        Application.Current = app;
        return app;
    }

    [Fact]
    public void Control_template_applies_namescope_bindings_and_trigger()
    {
        var app = SeedApp();
        app.Resources.Add("BoolToVisibility", new BoolToVisibilityConverter());
        UseTestAssembly();
        var root = (StackPanel)XamlLoader.Load(
            "<StackPanel xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\" " +
            "xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<StackPanel.Resources><ResourceDictionary>" +
            "<Style TargetType=\"{x:Type t:SelectControlStub}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type t:SelectControlStub}\">" +
            "<Canvas x:Name=\"MainCanvas\" Width=\"{TemplateBinding Width}\" Height=\"{TemplateBinding Height}\">" +
            "<UniformGrid x:Name=\"StatusControlGrid\" Columns=\"3\" " +
            "Visibility=\"{TemplateBinding ShowStatus, Converter={StaticResource BoolToVisibility}}\" />" +
            "<Rectangle x:Name=\"FillRect\" Fill=\"{TemplateBinding Background}\" Width=\"10\" Height=\"10\" />" +
            "</Canvas>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property=\"IsEnabled\" Value=\"False\">" +
            "<Setter TargetName=\"FillRect\" Property=\"Opacity\" Value=\"0.5\" />" +
            "</Trigger>" +
            "</ControlTemplate.Triggers>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary></StackPanel.Resources>" +
            "<t:SelectControlStub Title=\"Rec\" Width=\"200\" Height=\"100\" Background=\"Red\" />" +
            "</StackPanel>").Root;
        var control = Assert.IsType<SelectControlStub>(Assert.Single(root.LogicalChildren));
        Assert.True(control.Applied, "OnApplyTemplate must run when the style Template applies.");
        var grid = Assert.IsType<UniformGrid>(control.Template?.FindName("StatusControlGrid", control));
        Assert.Same(grid, control.StatusGrid);
        Assert.Equal(3, grid.Columns);
        Assert.Equal(Visibility.Visible, grid.Visibility);
        var canvas = Assert.IsType<Canvas>(control.Template?.FindName("MainCanvas", control));
        Assert.Equal(200.0, canvas.Width);
        Assert.Equal(100.0, canvas.Height);
        var fill = Assert.IsType<Rectangle>(control.Template?.FindName("FillRect", control));
        Assert.Equal(new SolidColorBrush(Colors.Red).ToRgba(), ((SolidColorBrush)fill.Fill!).ToRgba());
        var commands = WasmMilCore.RenderHeadless(root, new WpfSize(400, 200));
        Assert.Contains(commands.OfType<DrawCommand.FillRect>(), r => r.Rgba == new SolidColorBrush(Colors.Red).ToRgba());
        control.IsEnabled = false;
        Assert.Equal(0.5, fill.Opacity);
        control.ShowStatus = false;
        Assert.Equal(Visibility.Hidden, grid.Visibility);
    }

    [Fact]
    public void Template_inflates_per_control_instance()
    {
        var app = SeedApp();
        app.Resources.Add("BoolToVisibility", new BoolToVisibilityConverter());
        UseTestAssembly();
        var root = (StackPanel)XamlLoader.Load(
            "<StackPanel xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\" " +
            "xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<StackPanel.Resources><ResourceDictionary>" +
            "<Style TargetType=\"{x:Type t:SelectControlStub}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type t:SelectControlStub}\">" +
            "<Canvas><UniformGrid x:Name=\"StatusControlGrid\" Columns=\"3\" /></Canvas>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary></StackPanel.Resources>" +
            "<t:SelectControlStub Title=\"One\" />" +
            "<t:SelectControlStub Title=\"Two\" />" +
            "</StackPanel>").Root;
        var controls = root.LogicalChildren.OfType<SelectControlStub>().ToList();
        Assert.Equal(2, controls.Count);
        Assert.All(controls, c => Assert.True(c.Applied));
        Assert.NotSame(controls[0].StatusGrid, controls[1].StatusGrid);
        Assert.Null(controls[0].Template?.FindName("NoSuchPart", controls[0]));
        Assert.Same(controls[1].StatusGrid, controls[1].Template?.FindName("StatusControlGrid", controls[1]));
    }

    [Fact]
    public void LoadContent_returns_fresh_roots_and_empty_template_throws()
    {
        UseTestAssembly();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\" " +
            "xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type t:SelectControlStub}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type t:SelectControlStub}\">" +
            "<Canvas><UniformGrid x:Name=\"StatusControlGrid\" Columns=\"3\" /></Canvas>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var template = Assert.IsType<ControlTemplate>(
            Assert.IsType<Setter>(Assert.IsType<Style>(dict["Probe"]).Setters[0]).Value);
        var first = Assert.IsType<Canvas>(template.LoadContent());
        var second = Assert.IsType<Canvas>(template.LoadContent());
        Assert.NotSame(first, second);
        Assert.NotSame(
            Assert.IsType<UniformGrid>(first.LogicalChildren[0]),
            Assert.IsType<UniformGrid>(second.LogicalChildren[0]));
        Assert.Throws<InvalidOperationException>(() => new ControlTemplate().LoadContent());
    }

    [Fact]
    public void GetTemplateChild_resolves_named_part_or_null()
    {
        SeedApp();
        UseTestAssembly();
        var root = (StackPanel)XamlLoader.Load(
            "<StackPanel xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\" " +
            "xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<StackPanel.Resources><ResourceDictionary>" +
            "<Style TargetType=\"{x:Type t:SelectControlStub}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type t:SelectControlStub}\">" +
            "<Canvas><UniformGrid x:Name=\"StatusControlGrid\" Columns=\"3\" /></Canvas>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary></StackPanel.Resources>" +
            "<t:SelectControlStub />" +
            "</StackPanel>").Root;
        var control = Assert.IsType<SelectControlStub>(Assert.Single(root.LogicalChildren));
        Assert.IsType<UniformGrid>(control.StatusGrid);
        Assert.Null(control.Template?.FindName("Missing", control));
    }

    [Fact]
    public void UniformGrid_cells_layout_row_major()
    {
        var grid = new UniformGrid { Columns = 2, Rows = 2 };
        var first = new TextBlock { Text = "A" };
        var second = new TextBlock { Text = "B" };
        grid.AddChild(first);
        grid.AddChild(second);
        grid.Measure(new WpfSize(200, 100));
        Assert.Equal(first.DesiredSize.Width * 2, grid.DesiredSize.Width);
        Assert.Equal(first.DesiredSize.Height * 2, grid.DesiredSize.Height);
        grid.Arrange(new WpfRect(0, 0, 200, 100));
        Assert.Equal(100.0, first.ArrangedRect.Width);
        Assert.Equal(50.0, first.ArrangedRect.Height);
        Assert.Equal(100.0, second.ArrangedRect.X);
        Assert.Equal(0.0, second.ArrangedRect.Y);
    }

    [Fact]
    public void Viewbox_stretchdirection_clamps_uniform_scale()
    {
        static Viewbox Sized(StretchDirection direction)
        {
            var viewbox = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = direction };
            viewbox.Child = new TextBlock { Text = "Hi" };
            viewbox.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
            return viewbox;
        }
        var natural = Sized(StretchDirection.Both);
        Assert.True(natural.DesiredSize.Width > 0);
        foreach (var direction in new[] { StretchDirection.Both, StretchDirection.UpOnly, StretchDirection.DownOnly })
        {
            var viewbox = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = direction };
            viewbox.Child = new TextBlock { Text = "Hi" };
            viewbox.Measure(new WpfSize(natural.DesiredSize.Width / 2, natural.DesiredSize.Height / 2));
            viewbox.Arrange(new WpfRect(0, 0, natural.DesiredSize.Width / 2, natural.DesiredSize.Height / 2));
            var rendered = viewbox.Child!.ArrangedRect;
            if (direction == StretchDirection.UpOnly)
                Assert.Equal(natural.DesiredSize.Width, rendered.Width, precision: 6);
            else
                Assert.True(rendered.Width < natural.DesiredSize.Width, direction + " must shrink the child.");
        }
        Assert.Equal(StretchDirection.Both, new Viewbox().StretchDirection);
    }

    [Fact]
    public void Pageheader_template_resolves_bindings_and_targetname_trigger()
    {
        var app = SeedApp();
        var titleStyle = new Style(typeof(TextBlock));
        titleStyle.Setters.Add(new Setter(Control.FontSizeProperty, 20.0));
        app.Resources.Add("TitleTextBlockStyle", titleStyle);
        var bodyStyle = new Style(typeof(TextBlock));
        bodyStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        app.Resources.Add("BodyTextBlockStyle", bodyStyle);
        app.Resources.Add("EmptyToVisibilityConverter", new EmptyToVisibilityConverter());
        UseTestAssembly();
        var root = (StackPanel)XamlLoader.Load(
            "<StackPanel xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\" " +
            "xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<StackPanel.Resources><ResourceDictionary>" +
            "<Style TargetType=\"{x:Type t:PageHeaderStub}\">" +
            "<Setter Property=\"Focusable\" Value=\"False\" />" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type t:PageHeaderStub}\">" +
            "<StackPanel VerticalAlignment=\"Center\">" +
            "<Label x:Name=\"TitleTextBlock\" " +
            "AutomationProperties.Name=\"{Binding Title, StringFormat='{}{0} Page', RelativeSource={RelativeSource Mode=TemplatedParent}}\" " +
            "Focusable=\"True\">" +
            "<TextBlock Style=\"{StaticResource TitleTextBlockStyle}\" Text=\"{TemplateBinding Title}\" />" +
            "</Label>" +
            "<Label " +
            "AutomationProperties.Name=\"{Binding Description, RelativeSource={RelativeSource Mode=TemplatedParent}}\" " +
            "Visibility=\"{TemplateBinding Description, Converter={StaticResource EmptyToVisibilityConverter}}\" " +
            "Focusable=\"True\">" +
            "<TextBlock x:Name=\"DescriptionTextBlock\" Text=\"{TemplateBinding Description}\" " +
            "Style=\"{StaticResource BodyTextBlockStyle}\" />" +
            "</Label>" +
            "</StackPanel>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property=\"ShowDescription\" Value=\"False\">" +
            "<Setter TargetName=\"DescriptionTextBlock\" Property=\"Visibility\" Value=\"Hidden\" />" +
            "</Trigger>" +
            "</ControlTemplate.Triggers>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary></StackPanel.Resources>" +
            "<t:PageHeaderStub Title=\"Samples\" Description=\"Browse\" ShowDescription=\"False\" />" +
            "</StackPanel>").Root;
        var control = Assert.IsType<PageHeaderStub>(Assert.Single(root.LogicalChildren));
        var titleLabel = Assert.IsType<Label>(control.Template?.FindName("TitleTextBlock", control));
        var titleText = Assert.IsType<TextBlock>(titleLabel.Content);
        Assert.Equal("Samples", titleText.Text);
        Assert.Equal(20.0, titleText.FontSize);
        Assert.Equal("Samples Page", titleLabel.GetAttached("AutomationProperties.Name"));
        var descriptionText = Assert.IsType<TextBlock>(control.Template?.FindName("DescriptionTextBlock", control));
        Assert.Equal("Browse", descriptionText.Text);
        Assert.Equal(Visibility.Hidden, descriptionText.Visibility);
        var descriptionLabel = Assert.IsType<Label>(descriptionText.Parent);
        Assert.Equal(Visibility.Visible, descriptionLabel.Visibility);
        var commands = WasmMilCore.RenderHeadless(root, new WpfSize(400, 200));
        var texts = commands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();
        Assert.Contains("Samples", texts);
        Assert.DoesNotContain("Browse", texts);
        control.ShowDescription = true;
        control.Description = "Browse all";
        Assert.Equal(Visibility.Visible, descriptionText.Visibility);
        Assert.Equal(Visibility.Visible, descriptionLabel.Visibility);
        Assert.Equal("Browse all", descriptionText.Text);
        var repainted = WasmMilCore.RenderHeadless(root, new WpfSize(400, 200));
        var repaintedTexts = repainted.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();
        Assert.Contains("Samples", repaintedTexts);
        Assert.Contains("Browse all", repaintedTexts);
    }

    [Fact]
    public void Control_template_presenter_presents_explicit_sample_and_paints()
    {
        // Gallery ControlExample shape: a bare Control subclass (no layout
        // of its own) whose template binds a ContentPresenter to a
        // non-Content slot. The control must size to its template root, the
        // presenter must present the sample child (not reset to the absent
        // parent Content), and the sample must paint.
        var app = SeedApp();
        UseTestAssembly();
        var root = (StackPanel)XamlLoader.Load(
            "<StackPanel xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\" " +
            "xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<StackPanel.Resources><ResourceDictionary>" +
            "<Style TargetType=\"{x:Type t:SampleHostStub}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type t:SampleHostStub}\">" +
            "<StackPanel><TextBlock Text=\"Sample\" />" +
            "<ContentPresenter x:Name=\"Part\" Content=\"{TemplateBinding Sample}\" />" +
            "</StackPanel>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary></StackPanel.Resources>" +
            "<t:SampleHostStub><Button Content=\"Hi\" /></t:SampleHostStub>" +
            "</StackPanel>").Root;
        var control = Assert.IsType<SampleHostStub>(Assert.Single(root.LogicalChildren));
        root.Measure(new WpfSize(400, 200));
        root.Arrange(new WpfRect(0, 0, 400, 200));
        Assert.True(control.DesiredSize.Width > 0);
        Assert.True(control.DesiredSize.Height > 0);
        var presenter = Assert.IsType<ContentPresenter>(control.Template?.FindName("Part", control));
        Assert.Same(control.Sample, presenter.Content);
        Assert.IsType<Button>(control.Sample);
        var commands = WasmMilCore.RenderHeadless(root, new WpfSize(400, 200));
        var texts = commands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();
        Assert.Contains("Sample", texts);
        Assert.Contains("Hi", texts);
    }

    [Fact]
    public void Template_dynamic_solidbrush_entry_coerces_to_brush()
    {
        // Gallery ControlExample/Templates shape: <SolidColorBrush> entries
        // are stored as Color values, but a template Foreground target must
        // still land a real brush (used to fail the whole page load with
        // "'#E4000000' is not a Brush for template 'Foreground'").
        var app = SeedApp();
        // Gallery shape: the Fluent <SolidColorBrush> entry lives in
        // application resources (stored as a Color by the resource reader).
        var theme = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<SolidColorBrush x:Key=\"ThemeInk\" Color=\"#E4000000\" /></ResourceDictionary>");
        app.Resources.MergedDictionaries.Add(theme);
        var root = (StackPanel)XamlLoader.Load(
            "<StackPanel xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<StackPanel.Resources><ResourceDictionary>" +
            "<Style TargetType=\"Button\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"Button\">" +
            "<Button x:Name=\"Part\" Foreground=\"{DynamicResource ThemeInk}\" Content=\"Hi\" />" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary></StackPanel.Resources>" +
            "<Button Content=\"Go\" /></StackPanel>").Root;
        var outer = Assert.IsType<Button>(Assert.Single(root.LogicalChildren));
        var part = Assert.IsType<Button>(outer.Template?.FindName("Part", outer));
        var brush = Assert.IsType<SolidColorBrush>(part.Foreground);
        Assert.Equal(0xE4, brush.Color.A);
        Assert.Equal(0, brush.Color.R);
        Assert.Equal(0, brush.Color.G);
        Assert.Equal(0, brush.Color.B);
    }

    [Fact]
    public void Button_template_paints_border_chrome_with_templatebindings_and_content()
    {
        var app = SeedApp();
        app.Resources.Add("ControlCornerRadius", new CornerRadius(4));
        var root = (Window)XamlLoader.Load(
            "<Window xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Window.Resources><ResourceDictionary>" +
            "<Style x:Key=\"BorderlessButtonStyle\" TargetType=\"Button\">" +
            "<Setter Property=\"Background\" Value=\"Transparent\" />" +
            "<Setter Property=\"Border.CornerRadius\" Value=\"{StaticResource ControlCornerRadius}\" />" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"Button\">" +
            "<Border x:Name=\"EnclosingBorder\" " +
            "Background=\"{TemplateBinding Background}\" " +
            "BorderBrush=\"{TemplateBinding BorderBrush}\" " +
            "BorderThickness=\"{TemplateBinding BorderThickness}\" " +
            "CornerRadius=\"{TemplateBinding Border.CornerRadius}\" " +
            "SnapsToDevicePixels=\"True\">" +
            "<ContentPresenter x:Name=\"Presenter\" " +
            "HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\" " +
            "VerticalAlignment=\"{TemplateBinding VerticalContentAlignment}\" " +
            "RecognizesAccessKey=\"True\" />" +
            "</Border>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "<Style x:Key=\"TitleBarButtonStyle\" BasedOn=\"{StaticResource BorderlessButtonStyle}\" TargetType=\"Button\">" +
            "<Setter Property=\"Border.CornerRadius\" Value=\"0\" />" +
            "</Style>" +
            "</ResourceDictionary></Window.Resources>" +
            "<Button Content=\"Go\" Style=\"{StaticResource TitleBarButtonStyle}\" />" +
            "</Window>").Root;
        var button = Assert.IsType<Button>(root.Content);
        var border = Assert.IsType<Border>(button.Template?.FindName("EnclosingBorder", button));
        Assert.Equal(((SolidColorBrush)button.Background!).ToRgba(), border.BackgroundRgba);
        Assert.Equal(new CornerRadius(0), border.CornerRadius);
        var presenter = Assert.IsType<ContentPresenter>(button.Template?.FindName("Presenter", button));
        Assert.Equal("Go", presenter.Content);
        var commands = WasmMilCore.RenderHeadless(root, new WpfSize(400, 200));
        Assert.Contains(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "Go");
        var derived = Assert.IsType<Style>(root.Resources["TitleBarButtonStyle"]);
        Assert.Equal("BorderlessButtonStyle", derived.BasedOnKey);
        Assert.Equal(new CornerRadius(0), Assert.IsType<CornerRadius>(button.GetValue(Border.CornerRadiusProperty)));
    }

    private sealed class PrefixSelector : DataTemplateSelector
    {
        public DataTemplate? SelectedTemplate { get; set; }

        public DataTemplate? DropDownTemplate { get; set; }

        public DependencyObject? SeenContainer { get; private set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            SeenContainer = container;
            var text = item?.ToString() ?? string.Empty;
            if (text.StartsWith("Drop:", StringComparison.Ordinal))
                return DropDownTemplate ?? SelectedTemplate;
            return SelectedTemplate;
        }
    }

    [Fact]
    public void ItemTemplateSelector_selects_per_item_template()
    {
        SeedApp();
        var selector = new PrefixSelector
        {
            SelectedTemplate = new DataTemplate(
                "<TextBlock xmlns=\"" + PresentationNs + "\" Text=\"{Binding}\" />"),
            DropDownTemplate = new DataTemplate(
                "<TextBlock xmlns=\"" + PresentationNs + "\" Text=\"{Binding}\" />"),
        };
        var control = new ItemsControl { ItemTemplateSelector = selector };
        control.AddItem("Drop:One");
        control.AddItem("Two");
        Assert.Same(selector, control.ItemTemplateSelector);
        Assert.NotNull(selector.SeenContainer);
        var commands = WasmMilCore.RenderHeadless(control, new WpfSize(400, 200));
        var texts = commands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();
        Assert.Contains("Drop:One", texts);
        Assert.Contains("Two", texts);
        var singled = new ItemsControl
        {
            ItemTemplate = new DataTemplate("<TextBlock xmlns=\"" + PresentationNs + "\" Text=\"{Binding}\" />"),
            ItemTemplateSelector = selector,
        };
        singled.AddItem("Drop:Three");
        var singleCommands = WasmMilCore.RenderHeadless(singled, new WpfSize(400, 200));
        Assert.Contains(singleCommands.OfType<DrawCommand.DrawText>(), t => t.Text == "Drop:Three");
    }

    [Fact]
    public void Templates_derive_from_FrameworkTemplate()
    {
        var data = new DataTemplate("<TextBlock xmlns=\"" + PresentationNs + "\" />");
        Assert.IsAssignableFrom<FrameworkTemplate>(data);
        var itemsPanel = new ItemsPanelTemplate("<StackPanel xmlns=\"" + PresentationNs + "\" />");
        Assert.IsType<StackPanel>(itemsPanel.LoadContent());
        var control = new ControlTemplate();
        Assert.IsAssignableFrom<FrameworkTemplate>(control);
    }

    [Fact]
    public void Template_style_attribute_defers_across_sibling_merge_order()
    {
        // MaterialDesign boot shape: an earlier sibling dictionary holds a
        // ControlTemplate whose child sets Style to a style whose own setter
        // names a key defined only in a LATER sibling dictionary (theme
        // templates reference MaterialDesignDefaultContextMenu from Menu.xaml
        // while loading before it). WPF resolves template StaticResource at
        // use, not in merge order, so the application load must succeed and
        // inflation must bind the later key's value against the live tree.
        var app = SeedApp();
        const string dictB =
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<SolidColorBrush x:Key=\"LaterBrush\" Color=\"#FF112233\" />" +
            "</ResourceDictionary>";
        const string dictA =
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"EarlyStyle\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Background\" Value=\"{StaticResource LaterBrush}\" />" +
            "</Style>" +
            "<ControlTemplate x:Key=\"EarlyTemplate\" TargetType=\"{x:Type Button}\">" +
            "<Button x:Name=\"Inner\" Style=\"{StaticResource EarlyStyle}\" />" +
            "</ControlTemplate>" +
            "</ResourceDictionary>";
        app.RegisterPackResource("pack://application:,,,/Probe;component/A.xaml", dictA);
        app.RegisterPackResource("pack://application:,,,/Probe;component/B.xaml", dictB);
        using var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(
            "<Application.Resources xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<ResourceDictionary><ResourceDictionary.MergedDictionaries>" +
            "<ResourceDictionary Source=\"pack://application:,,,/Probe;component/A.xaml\"/>" +
            "<ResourceDictionary Source=\"pack://application:,,,/Probe;component/B.xaml\"/>" +
            "</ResourceDictionary.MergedDictionaries></ResourceDictionary>" +
            "</Application.Resources>"));
        reader.MoveToContent();
        XamlLoader.HydrateApplicationResources(app, reader);
        var window = (Window)XamlLoader.Load(
            "<Window xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Button Template=\"{StaticResource EarlyTemplate}\" />" +
            "</Window>").Root;
        var host = Assert.IsType<Button>(window.Content);
        var inner = Assert.IsType<Button>(host.Template?.FindName("Inner", host));
        Assert.Equal(0x112233FFu, inner.BackgroundRgba);
    }

    [Fact]
    public void Template_attached_error_template_binds_control_template()
    {
        // MaterialDesign DataGrid shape: a template child carries
        // Validation.ErrorTemplate naming a ControlTemplate resource (WPF
        // shows it as the validation adorner). The template attached bag
        // must accept the owner and land the value in the DP store.
        var app = SeedApp();
        const string dictB =
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<ControlTemplate x:Key=\"LaterError\" TargetType=\"{x:Type Control}\">" +
            "<Border />" +
            "</ControlTemplate>" +
            "</ResourceDictionary>";
        const string dictA =
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<ControlTemplate x:Key=\"EarlyTemplate\" TargetType=\"{x:Type Button}\">" +
            "<Grid x:Name=\"ErrGrid\" Validation.ErrorTemplate=\"{StaticResource LaterError}\" />" +
            "</ControlTemplate>" +
            "</ResourceDictionary>";
        app.RegisterPackResource("pack://application:,,,/Probe;component/A.xaml", dictA);
        app.RegisterPackResource("pack://application:,,,/Probe;component/B.xaml", dictB);
        using var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(
            "<Application.Resources xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<ResourceDictionary><ResourceDictionary.MergedDictionaries>" +
            "<ResourceDictionary Source=\"pack://application:,,,/Probe;component/A.xaml\"/>" +
            "<ResourceDictionary Source=\"pack://application:,,,/Probe;component/B.xaml\"/>" +
            "</ResourceDictionary.MergedDictionaries></ResourceDictionary>" +
            "</Application.Resources>"));
        reader.MoveToContent();
        XamlLoader.HydrateApplicationResources(app, reader);
        var window = (Window)XamlLoader.Load(
            "<Window xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Button Template=\"{StaticResource EarlyTemplate}\" />" +
            "</Window>").Root;
        var host = Assert.IsType<Button>(window.Content);
        var grid = Assert.IsType<Grid>(host.Template?.FindName("ErrGrid", host));
        var error = Assert.IsType<ControlTemplate>(
            app.TryFindResource("LaterError", out var found) ? found : null);
        Assert.Same(error, Validation.GetErrorTemplate(grid));
    }

    [Fact]
    public void Template_named_transform_resolves_elementname_freezable_binding()
    {
        // MaterialDesign circular ProgressBar shape: a ScaleTransform named
        // inside a part's RenderTransform property element is addressable by
        // ElementName bindings on Freezables (the ArcSegment.Point
        // MultiBinding reads FullyIndeterminateGridScaleTransform.ScaleX).
        // WPF registers every x:Name in the template namescope, not just
        // visuals. (Engine Button target: the test-assembly x:Type path is
        // a preserved baseline gap, so the shape uses engine types only.)
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Grid x:Name=\"Root\">" +
            "<Grid.RenderTransform><ScaleTransform x:Name=\"zoom\" ScaleX=\"2\" /></Grid.RenderTransform>" +
            "<Rectangle x:Name=\"Box\" Width=\"10\" Height=\"10\">" +
            "<Rectangle.RenderTransform><ScaleTransform ScaleX=\"{Binding ElementName=zoom, Path=ScaleX}\" /></Rectangle.RenderTransform>" +
            "</Rectangle>" +
            "</Grid>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new Button();
        style.ApplyTo(control);
        var zoom = Assert.IsType<ScaleTransform>(control.Template?.FindName("zoom", control));
        Assert.Equal(2.0, zoom.ScaleX);
        var box = Assert.IsType<Rectangle>(control.Template?.FindName("Box", control));
        Assert.Equal(2.0, Assert.IsType<ScaleTransform>(box.RenderTransform).ScaleX);
    }

    [Fact]
    public void Template_named_transform_resolves_storyboard_targetname()
    {
        // MaterialDesign circular ProgressBar shape: trigger storyboards
        // address named template transforms (IsIndeterminateStoryboard
        // targets RotateTransform, IsFullyIndeterminateStoryboard targets
        // FullyIndeterminateGridScaleTransform). The TargetName lookup
        // covers the template namescope like WPF.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Grid x:Name=\"Root\">" +
            "<Grid.RenderTransform><ScaleTransform x:Name=\"zoom\" ScaleX=\"0\" /></Grid.RenderTransform>" +
            "</Grid>" +
            "<ControlTemplate.Triggers>" +
            "<Trigger Property=\"IsEnabled\" Value=\"True\">" +
            "<Trigger.EnterActions><BeginStoryboard><Storyboard>" +
            "<DoubleAnimation Storyboard.TargetName=\"zoom\" Storyboard.TargetProperty=\"(ScaleTransform.ScaleX)\" From=\"0\" To=\"1\" Duration=\"0:0:1\" />" +
            "</Storyboard></BeginStoryboard></Trigger.EnterActions>" +
            "</Trigger>" +
            "</ControlTemplate.Triggers>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new Button();
        style.ApplyTo(control);
        var zoom = Assert.IsType<ScaleTransform>(control.Template?.FindName("zoom", control));
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1.0, zoom.ScaleX, precision: 9);
    }

    [Fact]
    public void Template_visual_transition_resolves_template_resource_easing()
    {
        // MaterialDesign FlipperClassic shape: ControlTemplate.Resources
        // holds a SineEase addressed by EasingFunction={StaticResource
        // FlipEase} on transition key frames. The template resource scope
        // covers visual state storyboards like trigger storyboards.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<ControlTemplate.Resources>" +
            "<SineEase x:Key=\"FlipEase\" EasingMode=\"EaseInOut\" />" +
            "</ControlTemplate.Resources>" +
            "<Grid x:Name=\"Root\">" +
            "<VisualStateManager.VisualStateGroups>" +
            "<VisualStateGroup x:Name=\"FlipStates\">" +
            "<VisualStateGroup.Transitions>" +
            "<VisualTransition From=\"*\" To=\"Checked\">" +
            "<Storyboard>" +
            "<DoubleAnimationUsingKeyFrames Storyboard.TargetName=\"Root\" Storyboard.TargetProperty=\"(UIElement.Opacity)\">" +
            "<EasingDoubleKeyFrame KeyTime=\"0:0:1\" Value=\"1\" EasingFunction=\"{StaticResource FlipEase}\" />" +
            "</DoubleAnimationUsingKeyFrames>" +
            "</Storyboard>" +
            "</VisualTransition>" +
            "</VisualStateGroup.Transitions>" +
            "</VisualStateGroup>" +
            "</VisualStateManager.VisualStateGroups>" +
            "</Grid>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var template = Assert.IsType<ControlTemplate>(
            Assert.IsType<Setter>(Assert.IsType<Style>(dict["Probe"]).Setters[0]).Value);
        var root = Assert.IsType<Grid>(template.VisualTree);
        var group = Assert.IsType<VisualStateGroup>(Assert.Single(VisualStateManager.GetVisualStateGroups(root)));
        var transition = Assert.IsType<VisualTransition>(Assert.Single(group.Transitions));
        var keyed = Assert.IsType<DoubleAnimationUsingKeyFrames>(Assert.Single(transition.Storyboard!.Children));
        Assert.IsType<SineEase>(Assert.IsType<EasingDoubleKeyFrame>(Assert.Single(keyed.KeyFrames)).EasingFunction);
    }

    [Fact]
    public void Template_element_resources_unwrap_explicit_dictionary()
    {
        // MaterialDesign PopupBox shape: a template part carries
        // <Part.Resources><ResourceDictionary> with keyless implicit
        // styles. The explicit wrapper unwraps like page XAML instead of
        // parsing as a keyless CLR resource.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<ContentControl x:Name=\"CC\">" +
            "<ContentControl.Resources>" +
            "<ResourceDictionary>" +
            "<Style TargetType=\"{x:Type ToolTip}\"><Setter Property=\"Margin\" Value=\"4\" /></Style>" +
            "</ResourceDictionary>" +
            "</ContentControl.Resources>" +
            "</ContentControl>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new Button();
        style.ApplyTo(control);
        var host = Assert.IsType<ContentControl>(control.Template?.FindName("CC", control));
        Assert.IsType<Style>(host.Resources[typeof(ToolTip)]);
    }

    [Fact]
    public void Template_items_panel_parses_nested_template()
    {
        // MaterialDesign RatingBar shape: <ItemsControl.ItemsPanel>
        // carries one <ItemsPanelTemplate>. The template property parses
        // through the shared reader like ItemTemplate.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<ItemsControl x:Name=\"Host\">" +
            "<ItemsControl.ItemsPanel>" +
            "<ItemsPanelTemplate><StackPanel /></ItemsPanelTemplate>" +
            "</ItemsControl.ItemsPanel>" +
            "</ItemsControl>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new Button();
        style.ApplyTo(control);
        var host = Assert.IsType<ItemsControl>(control.Template?.FindName("Host", control));
        Assert.IsType<ItemsPanelTemplate>(host.GetValue(ItemsControl.ItemsPanelProperty));
    }

    [Fact]
    public void Template_owner_qualified_multibinding_defers_attached_transfer()
    {
        // MaterialDesign RatingBar shape: <TextBlock.Foreground> on the
        // Ripple carries a <MultiBinding> (ElementName sources plus the
        // vendor converter). Owner-qualified bindings defer like the
        // PriorityBinding arm; the dotted name rides the attached transfer
        // at inflation instead of failing as an unknown element.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Grid x:Name=\"Root\">" +
            "<TextBlock x:Name=\"Tint\" Foreground=\"Red\" />" +
            "<Border x:Name=\"Box\">" +
            "<TextBlock.Foreground><MultiBinding>" +
            "<Binding ElementName=\"Tint\" Path=\"Foreground\" />" +
            "</MultiBinding></TextBlock.Foreground>" +
            "</Border>" +
            "</Grid>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new Button();
        style.ApplyTo(control);
        var tint = Assert.IsType<TextBlock>(control.Template?.FindName("Tint", control));
        var box = Assert.IsType<Border>(control.Template?.FindName("Box", control));
        Assert.Same(tint.Foreground, box.GetAttached("TextBlock.Foreground"));
    }

    [Fact]
    public void Template_multibinding_converter_resolves_nested_singleton()
    {
        // MaterialDesign RatingBar shape: the Ripple TextBlock.Foreground
        // MultiBinding names Converter={x:Static
        // wpf:RatingBar+TextBlockForegroundConverter.Instance}. Nested
        // vendor singletons resolve through the outer converted type,
        // like XAML's Outer+Nested member lookup.
        SeedApp();
        UseTestAssembly();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\" " +
            "xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Grid x:Name=\"Root\">" +
            "<TextBlock x:Name=\"Tint\" Foreground=\"Red\" />" +
            "<Border x:Name=\"Box\">" +
            "<TextBlock.Foreground><MultiBinding Converter=\"{x:Static t:StaticHostStub+NestedStub.Instance}\">" +
            "<Binding ElementName=\"Tint\" Path=\"Foreground\" />" +
            "</MultiBinding></TextBlock.Foreground>" +
            "</Border>" +
            "</Grid>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new Button();
        style.ApplyTo(control);
        var tint = Assert.IsType<TextBlock>(control.Template?.FindName("Tint", control));
        var box = Assert.IsType<Border>(control.Template?.FindName("Box", control));
        Assert.Same(tint.Foreground, box.GetAttached("TextBlock.Foreground"));
    }

    [Fact]
    public void Template_transform_property_multibinding_binds_translate()
    {
        // MaterialDesign RatingBar shape: <TranslateTransform.X> carries a
        // <MultiBinding> (ElementName and TemplatedParent children, the
        // latter with an owner-qualified path like RatingBar.Orientation).
        // Bound transform property elements defer through FreezableBinding
        // entries like the attribute form.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Grid x:Name=\"Root\">" +
            "<TextBlock x:Name=\"Peer\" Width=\"7\" />" +
            "<Rectangle x:Name=\"Box\" Width=\"10\" Height=\"10\">" +
            "<Rectangle.RenderTransform><TranslateTransform x:Name=\"Mover\">" +
            "<TranslateTransform.X><MultiBinding>" +
            "<Binding ElementName=\"Peer\" Path=\"Width\" />" +
            "</MultiBinding></TranslateTransform.X>" +
            "<TranslateTransform.Y><MultiBinding>" +
            "<Binding Path=\"Button.Height\" RelativeSource=\"{RelativeSource TemplatedParent}\" />" +
            "</MultiBinding></TranslateTransform.Y>" +
            "</TranslateTransform></Rectangle.RenderTransform>" +
            "</Rectangle>" +
            "</Grid>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new Button { Height = 33 };
        style.ApplyTo(control);
        var mover = Assert.IsType<TranslateTransform>(control.Template?.FindName("Mover", control));
        Assert.Equal(7.0, mover.X);
        Assert.Equal(33.0, mover.Y);
    }

    [Fact]
    public void Template_visual_transition_resolves_style_resource_easing()
    {
        // MaterialDesign SmartHint shape: Style.Resources holds the
        // AnimationEasingFunction CubicEase addressed by
        // EasingFunction={StaticResource ...} on transition animations.
        // The style resource scope covers template visual state
        // storyboards like template resources do.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Style.Resources>" +
            "<CubicEase x:Key=\"ProbeEase\" EasingMode=\"EaseInOut\" />" +
            "</Style.Resources>" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Grid x:Name=\"Root\">" +
            "<VisualStateManager.VisualStateGroups>" +
            "<VisualStateGroup x:Name=\"ProbeStates\">" +
            "<VisualStateGroup.Transitions>" +
            "<VisualTransition From=\"*\" To=\"There\">" +
            "<Storyboard>" +
            "<DoubleAnimation Storyboard.TargetName=\"Root\" Storyboard.TargetProperty=\"Opacity\" To=\"0\" Duration=\"0:0:1\" EasingFunction=\"{StaticResource ProbeEase}\" />" +
            "</Storyboard>" +
            "</VisualTransition>" +
            "</VisualStateGroup.Transitions>" +
            "</VisualStateGroup>" +
            "</VisualStateManager.VisualStateGroups>" +
            "</Grid>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var template = Assert.IsType<ControlTemplate>(
            Assert.IsType<Setter>(Assert.IsType<Style>(dict["Probe"]).Setters[0]).Value);
        var root = Assert.IsType<Grid>(template.VisualTree);
        var group = Assert.IsType<VisualStateGroup>(Assert.Single(VisualStateManager.GetVisualStateGroups(root)));
        var transition = Assert.IsType<VisualTransition>(Assert.Single(group.Transitions));
        var animation = Assert.IsType<DoubleAnimation>(Assert.Single(transition.Storyboard!.Children));
        Assert.IsType<CubicEase>(animation.EasingFunction);
    }

    [Fact]
    public void Template_transition_animation_binds_templatebinding_endpoint()
    {
        // MaterialDesign SmartHint shape: transition DoubleAnimations carry
        // To="{TemplateBinding HintOpacity}". Endpoints defer like durations
        // and resolve against the templated parent at storyboard run.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Grid x:Name=\"Root\">" +
            "<VisualStateManager.VisualStateGroups>" +
            "<VisualStateGroup x:Name=\"ProbeStates\">" +
            "<VisualState x:Name=\"There\">" +
            "<Storyboard>" +
            "<DoubleAnimation Storyboard.TargetName=\"Root\" Storyboard.TargetProperty=\"Opacity\" To=\"{TemplateBinding Width}\" Duration=\"0:0:1\" />" +
            "</Storyboard>" +
            "</VisualState>" +
            "</VisualStateGroup>" +
            "</VisualStateManager.VisualStateGroups>" +
            "</Grid>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new Button { Width = 42 };
        style.ApplyTo(control);
        Assert.True(VisualStateManager.GoToState(control, "There", false));
        var template = Assert.IsType<ControlTemplate>(
            Assert.IsType<Setter>(style.Setters[0]).Value);
        var root = Assert.IsType<Grid>(template.VisualTree);
        var group = Assert.IsType<VisualStateGroup>(Assert.Single(VisualStateManager.GetVisualStateGroups(root)));
        var state = Assert.IsType<VisualState>(Assert.Single(group.States));
        var animation = Assert.IsType<DoubleAnimation>(Assert.Single(state.Storyboard!.Children));
        Assert.Equal("Width", animation.DeferredToBinding);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(1));
        var box = Assert.IsType<Grid>(control.Template?.FindName("Root", control));
        Assert.Equal(42.0, box.Opacity);
    }

    [Fact]
    public void Template_rendertransform_multibinding_produces_transform()
    {
        // MaterialDesign SmartHint shape: <Grid.RenderTransform> carries a
        // bare <MultiBinding> whose converter produces the whole transform
        // (ElementName plus self-path children). The composite defers like
        // bound backgrounds and lands through the RenderTransform CLR
        // property at inflation.
        SeedApp();
        UseTestAssembly();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\" " +
            "xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Grid x:Name=\"Root\">" +
            "<TextBlock x:Name=\"Peer\" Width=\"7\" />" +
            "<Rectangle x:Name=\"Box\" Width=\"10\" Height=\"10\">" +
            "<Rectangle.RenderTransform>" +
            "<MultiBinding Converter=\"{x:Static t:StaticHostStub+TransformStub.Instance}\">" +
            "<Binding ElementName=\"Peer\" Path=\"Width\" />" +
            "<Binding Path=\".\" RelativeSource=\"{RelativeSource TemplatedParent}\" />" +
            "</MultiBinding>" +
            "</Rectangle.RenderTransform>" +
            "</Rectangle>" +
            "</Grid>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new Button();
        style.ApplyTo(control);
        var box = Assert.IsType<Rectangle>(control.Template?.FindName("Box", control));
        Assert.Equal(7.0, Assert.IsType<TranslateTransform>(box.RenderTransform).X);
    }

    [Fact]
    public void Template_tag_holds_system_value()
    {
        // MaterialDesign SmartHint shape: a part carries
        // <ContentControl.Tag><system:Double>0.0</system:Double></>. Tag is
        // CLR-only in the engine (matches FrameworkElement.Tag), so the
        // value lands on the CLR property like the attribute path.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\" " +
            "xmlns:system=\"clr-namespace:System;assembly=mscorlib\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Rectangle x:Name=\"Box\" Width=\"10\" Height=\"10\">" +
            "<Rectangle.Tag><system:Double>0.5</system:Double></Rectangle.Tag>" +
            "</Rectangle>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new Button();
        style.ApplyTo(control);
        var box = Assert.IsType<Rectangle>(control.Template?.FindName("Box", control));
        Assert.Equal(0.5, box.Tag);
    }

    [Fact]
    public void Template_keyframe_name_parses()
    {
        // MaterialDesign Generic.xaml shape: keyed-animation frames carry
        // x:Name (MousePressedToNormalScaleXKeyFrame) with no referencing
        // target. Names parse like WPF registration; frame targeting stays
        // a boundary until a corpus references one.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Grid x:Name=\"Root\">" +
            "<VisualStateManager.VisualStateGroups>" +
            "<VisualStateGroup x:Name=\"ProbeStates\">" +
            "<VisualState x:Name=\"There\">" +
            "<Storyboard>" +
            "<DoubleAnimationUsingKeyFrames Storyboard.TargetName=\"Root\" Storyboard.TargetProperty=\"Opacity\">" +
            "<EasingDoubleKeyFrame x:Name=\"ProbeKeyFrame\" KeyTime=\"0:0:1\" Value=\"1\" />" +
            "</DoubleAnimationUsingKeyFrames>" +
            "</Storyboard>" +
            "</VisualState>" +
            "</VisualStateGroup>" +
            "</VisualStateManager.VisualStateGroups>" +
            "</Grid>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var template = Assert.IsType<ControlTemplate>(
            Assert.IsType<Setter>(Assert.IsType<Style>(dict["Probe"]).Setters[0]).Value);
        var root = Assert.IsType<Grid>(template.VisualTree);
        var group = Assert.IsType<VisualStateGroup>(Assert.Single(VisualStateManager.GetVisualStateGroups(root)));
        var state = Assert.IsType<VisualState>(Assert.Single(group.States));
        var keyed = Assert.IsType<DoubleAnimationUsingKeyFrames>(Assert.Single(state.Storyboard!.Children));
        Assert.Equal(1.0, Assert.IsType<EasingDoubleKeyFrame>(Assert.Single(keyed.KeyFrames)).Value);
    }

    [Fact]
    public void Template_thickness_keyframes_parse()
    {
        // MaterialDesign DrawerHost shape: transitions animate Margin
        // through ThicknessAnimationUsingKeyFrames with nested-easing
        // EasingThicknessKeyFrame children. The keyed thickness family
        // parses like the double family.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Grid x:Name=\"Root\">" +
            "<VisualStateManager.VisualStateGroups>" +
            "<VisualStateGroup x:Name=\"ProbeStates\">" +
            "<VisualState x:Name=\"There\">" +
            "<Storyboard>" +
            "<ThicknessAnimationUsingKeyFrames Storyboard.TargetName=\"Root\" Storyboard.TargetProperty=\"Margin\">" +
            "<EasingThicknessKeyFrame KeyTime=\"0:0:1\" Value=\"0\">" +
            "<EasingThicknessKeyFrame.EasingFunction><SineEase EasingMode=\"EaseOut\" /></EasingThicknessKeyFrame.EasingFunction>" +
            "</EasingThicknessKeyFrame>" +
            "</ThicknessAnimationUsingKeyFrames>" +
            "</Storyboard>" +
            "</VisualState>" +
            "</VisualStateGroup>" +
            "</VisualStateManager.VisualStateGroups>" +
            "</Grid>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var template = Assert.IsType<ControlTemplate>(
            Assert.IsType<Setter>(Assert.IsType<Style>(dict["Probe"]).Setters[0]).Value);
        var root = Assert.IsType<Grid>(template.VisualTree);
        var group = Assert.IsType<VisualStateGroup>(Assert.Single(VisualStateManager.GetVisualStateGroups(root)));
        var state = Assert.IsType<VisualState>(Assert.Single(group.States));
        var keyed = Assert.IsType<ThicknessAnimationUsingKeyFrames>(Assert.Single(state.Storyboard!.Children));
        var frame = Assert.IsType<EasingThicknessKeyFrame>(Assert.Single(keyed.KeyFrames));
        Assert.Equal(new Thickness(0, 0, 0, 0), frame.Value);
        Assert.IsType<SineEase>(frame.EasingFunction);
    }

    [Fact]
    public void Template_matrix_transform_parses_identity()
    {
        // Transitions TransitioningContent shape: the RenderTransform group
        // starts with an attribute-less named MatrixTransform (code-behind
        // retargets it at run). Empty matrix transforms parse as identity
        // and register their name like sibling transforms.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Grid x:Name=\"Root\">" +
            "<Grid.RenderTransform>" +
            "<TransformGroup>" +
            "<MatrixTransform x:Name=\"ProbeMatrix\" />" +
            "<TranslateTransform X=\"2\" Y=\"3\" />" +
            "</TransformGroup>" +
            "</Grid.RenderTransform>" +
            "</Grid>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new Button();
        style.ApplyTo(control);
        var matrix = Assert.IsType<MatrixTransform>(control.Template?.FindName("ProbeMatrix", control));
        Assert.Equal(Matrix.Identity, matrix.Value);
    }

    [Fact]
    public void Template_textelement_foreground_takes_resolved_brush()
    {
        // Theme shape: TextElement.Foreground="{DynamicResource ...}" hands
        // an already-typed Brush to the template applier. Resolved brushes
        // land in the attached bag like TextBlock.Foreground ones; strings
        // keep the ParseAttachedValue brush path.
        var app = SeedApp();
        var brush = new SolidColorBrush(Colors.Red);
        app.Resources.Add("ProbeBrush", brush);
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Grid x:Name=\"Root\" TextElement.Foreground=\"{DynamicResource ProbeBrush}\" />" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new Button();
        style.ApplyTo(control);
        var root = Assert.IsType<Grid>(control.Template?.FindName("Root", control));
        Assert.Same(brush, root.GetAttached("TextElement.Foreground"));
    }

    [Fact]
    public void Style_basedon_type_resolves_theme_implicit_style()
    {
        // Demo Card shape: BasedOn="{StaticResource {x:Type ...}}" names the
        // implicit theme style. Unkeyed theme styles are keyed by Type, so
        // the defining assembly's generic.xaml resolves them like WPF.
        SeedApp();
        UseTestAssembly();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\" " +
            "xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<Style x:Key=\"Derived\" TargetType=\"{x:Type t:BasedOnBaseStub}\" BasedOn=\"{StaticResource {x:Type t:BasedOnBaseStub}}\">" +
            "</Style>" +
            "</ResourceDictionary>");
        var derived = Assert.IsType<Style>(dict["Derived"]);
        var control = new BasedOnBaseStub();
        derived.ApplyTo(control);
        Assert.Equal("based", control.Tag);
    }

    [Fact]
    public void Style_setter_staticresource_resolves_theme_dictionary()
    {
        // DrawerHost shape: OverlayBackground="{StaticResource
        // BlackBackground}" where the key lives in the style's own theme
        // dictionary. Style application consults the target's theme chain
        // like WPF before failing the lookup.
        SeedApp();
        var dict = ThemeDictionaries.EnsureForAssembly(typeof(BasedOnThemedStub).Assembly);
        Assert.NotNull(dict);
        var style = Assert.IsType<Style>(dict[typeof(BasedOnThemedStub)]);
        var control = new BasedOnThemedStub();
        style.ApplyTo(control);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(control.Background).Color);
    }

    [Fact]
    public void Style_setter_named_color_converts_to_brush()
    {
        // Theme shape (DrawerHost resource indirection): a StaticResource
        // resolving to the "Black" string against a SolidColorBrush-typed
        // setter. WPF's BrushConverter/ColorConverter resolve known color
        // names for every brush/color target, not just Brush.
        SeedApp();
        Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(
            StyleValueConverter.Convert("Black", typeof(SolidColorBrush))).Color);
        Assert.Equal(Colors.Black, StyleValueConverter.Convert("Black", typeof(Color)));
        Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(
            StyleValueConverter.Convert("Black", typeof(Brush))).Color);
    }

    [Fact]
    public void Datatemplate_triggers_apply_to_named_parts()
    {
        // Demo DocumentationLinks shape: <DataTemplate> carries a visual
        // root plus <DataTemplate.Triggers> whose DataTriggers retarget
        // named parts once the inflated content gets its item.
        SeedApp();
        var window = Assert.IsType<Window>(XamlLoader.Load(
            "<Window xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Window.Resources><ResourceDictionary>" +
            "<DataTemplate x:Key=\"Probe\">" +
            "<StackPanel><TextBlock x:Name=\"Part\" Text=\"{Binding Text}\" /></StackPanel>" +
            "<DataTemplate.Triggers>" +
            "<DataTrigger Binding=\"{Binding Tag}\" Value=\"on\">" +
            "<Setter TargetName=\"Part\" Property=\"FontWeight\" Value=\"Bold\" />" +
            "</DataTrigger>" +
            "</DataTemplate.Triggers>" +
            "</DataTemplate>" +
            "</ResourceDictionary></Window.Resources>" +
            "</Window>").Root);
        var template = Assert.IsType<DataTemplate>(window.Resources["Probe"]);
        var on = Assert.IsType<StackPanel>(template.LoadContent());
        on.DataContext = new TextBox { Text = "hi", Tag = "on" };
        var part = Assert.IsType<TextBlock>(on.Children[0]);
        Assert.Equal("hi", part.Text);
        Assert.Equal(FontWeights.Bold, part.FontWeight);
        var off = Assert.IsType<StackPanel>(template.LoadContent());
        off.DataContext = new TextBox { Text = "lo", Tag = "off" };
        var calm = Assert.IsType<TextBlock>(off.Children[0]);
        Assert.Equal(FontWeights.Normal, calm.FontWeight);
    }

    [Fact]
    public void Itemscontrol_itemcontainerstyle_parses()
    {
        // Demo DocumentationLinks shape:
        // <ItemsControl.ItemContainerStyle><Style .../> nests the container
        // style like WPF instead of falling to the unknown-element error.
        SeedApp();
        var control = Assert.IsType<ItemsControl>(XamlLoader.Load(
            "<ItemsControl xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<ItemsControl.ItemContainerStyle>" +
            "<Style TargetType=\"{x:Type ContentPresenter}\">" +
            "<Setter Property=\"Margin\" Value=\"0,0,8,0\" />" +
            "</Style>" +
            "</ItemsControl.ItemContainerStyle>" +
            "</ItemsControl>").Root);
        Assert.NotNull(control.ItemContainerStyle);
        Assert.Equal(typeof(ContentPresenter), control.ItemContainerStyle.TargetType);
    }

    [Fact]
    public void Style_basedon_key_resolves_theme_dictionary()
    {
        // Demo PopupBox shape: BasedOn="MaterialDesignPopupBox" names a
        // string-keyed style in the theme dictionary, not the live tree.
        // Apply-time lookup consults the target's theme chain like WPF.
        SeedApp();
        var derived = new Style(typeof(BasedOnThemedStub)) { BasedOnKey = "ProbeBase" };
        var control = new BasedOnThemedStub();
        derived.ApplyTo(control);
        Assert.Equal("based", control.Tag);
    }

    [Fact]
    public void Template_contentcontrol_contenttemplate_transfers()
    {
        // MaterialDesign Card shape: parts bind ContentTemplate through
        // {TemplateBinding ContentControl.ContentTemplate}. The token and
        // CLR exist on ContentControl like ContentPresenter, so the transfer
        // reads the templated parent.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<ContentPresenter x:Name=\"Host\" ContentTemplate=\"{TemplateBinding ContentControl.ContentTemplate}\" />" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var template = new DataTemplate(
            "<Grid xmlns=\"" + PresentationNs + "\" />");
        var control = new Button { ContentTemplate = template };
        style.ApplyTo(control);
        var host = Assert.IsType<ContentPresenter>(control.Template?.FindName("Host", control));
        Assert.Same(template, host.ContentTemplate);
    }

    [Fact]
    public void Templatebinding_prefixed_attached_source_reads_owner()
    {
        // MaterialDesign TextBox shape: parts bind
        // {TemplateBinding wpf:TextFieldAssist.TextFieldCornerRadius},
        // where wpf: is the BAML-kept xmlns prefix and the owner lives in
        // the converted assembly (or the engine). The transfer strips the
        // prefix and reads the owner DP/CLR/attached value like WPF.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type TextBox}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type TextBox}\">" +
            "<TextBlock x:Name=\"Part\" Text=\"{TemplateBinding wpf:AutomationProperties.HelpText}\" />" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new TextBox();
        AutomationProperties.SetHelpText(control, "hint");
        style.ApplyTo(control);
        var part = Assert.IsType<TextBlock>(control.Template?.FindName("Part", control));
        Assert.Equal("hint", part.Text);
    }

    [Fact]
    public void Template_scrollviewer_contenttemplate_transfers()
    {
        // MaterialDesign ScrollViewer shape: the theme template binds
        // ContentTemplate through {TemplateBinding ContentTemplate}. Real
        // ScrollViewer inherits the ContentControl template DPs, so the
        // transfer reads the templated parent like the Card host does.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type ScrollViewer}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type ScrollViewer}\">" +
            "<ContentPresenter x:Name=\"Host\" ContentTemplate=\"{TemplateBinding ContentTemplate}\" />" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var template = new DataTemplate(
            "<Grid xmlns=\"" + PresentationNs + "\" />");
        var control = new ScrollViewer { ContentTemplate = template };
        style.ApplyTo(control);
        var host = Assert.IsType<ContentPresenter>(control.Template?.FindName("Host", control));
        Assert.Same(template, host.ContentTemplate);
    }

    [Fact]
    public void Contentcontrol_clr_and_dp_share_one_store()
    {
        // Demo DialogHost shape: the theme template binds
        // {TemplateBinding ContentControl.Content}, which reads the DP
        // store. The CLR wrapper shares that one store like WPF, so XAML
        // content and direct SetValue read back identically either way.
        SeedApp();
        var host = new ContentControl();
        var child = new TextBlock { Text = "hi" };
        host.Content = child;
        Assert.Same(child, host.GetValue(ContentControl.ContentProperty));
        host.SetValue(ContentControl.ContentProperty, "text");
        Assert.Equal("text", host.Content);
        Assert.True(host.HasContent);
        Assert.NotNull(host.GetValue(ContentControl.ContentProperty));
    }

    [Fact]
    public void Frameworktemplate_findname_dispatches_to_control_namescope()
    {
        // Converted code-behind calls Template.FindName(name, this) through
        // the FrameworkTemplate slot (ControlTemplate overrides it; data
        // templates keep no per-parent namescope and stay null).
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Grid x:Name=\"Root\" />" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new Button();
        style.ApplyTo(control);
        var template = Assert.IsType<ControlTemplate>(
            Assert.IsType<Setter>(style.Setters[0]).Value);
        Assert.IsType<Grid>(template.FindName("Root", control));
        Assert.IsType<Grid>(((FrameworkTemplate)template).FindName("Root", control));
        var data = new DataTemplate("<Grid xmlns=\"" + PresentationNs + "\" />");
        Assert.Null(data.FindName("Root", control));
    }

    [Fact]
    public void Template_attached_owner_multibinding_defers_without_dp()
    {
        // MaterialDesign Snackbar shape: <DockPanel.Dock> on a Button
        // carries a <MultiBinding>, but DockPanel declares no Dock DP (the
        // edge lives in the attached bag). Binding children of DP-less
        // owners defer through the attached transfer like PriorityBinding.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<DockPanel x:Name=\"Host\">" +
            "<TextBlock x:Name=\"Peer\" Tag=\"Right\" />" +
            "<Button x:Name=\"Btn\">" +
            "<DockPanel.Dock><MultiBinding>" +
            "<Binding ElementName=\"Peer\" Path=\"Tag\" />" +
            "</MultiBinding></DockPanel.Dock>" +
            "</Button>" +
            "</DockPanel>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new Button();
        style.ApplyTo(control);
        var btn = Assert.IsType<Button>(control.Template?.FindName("Btn", control));
        Assert.Equal(Dock.Right, DockPanel.GetDock(btn));
    }

    [Fact]
    public void Template_trigger_template_resolves_style_resource_easing()
    {
        // MaterialDesign SmartHint shape: the UseFloating trigger swaps in
        // a second ControlTemplate whose transition animations address the
        // same Style.Resources easing key. Trigger Setter.Value templates
        // parse under the style scope like direct Template setters.
        SeedApp();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Style.Resources>" +
            "<CubicEase x:Key=\"ProbeEase\" EasingMode=\"EaseInOut\" />" +
            "</Style.Resources>" +
            "<Style.Triggers>" +
            "<Trigger Property=\"IsEnabled\" Value=\"True\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<Grid x:Name=\"Root\">" +
            "<VisualStateManager.VisualStateGroups>" +
            "<VisualStateGroup x:Name=\"ProbeStates\">" +
            "<VisualStateGroup.Transitions>" +
            "<VisualTransition From=\"*\" To=\"There\">" +
            "<Storyboard>" +
            "<DoubleAnimation Storyboard.TargetName=\"Root\" Storyboard.TargetProperty=\"Opacity\" To=\"0\" Duration=\"0:0:1\" EasingFunction=\"{StaticResource ProbeEase}\" />" +
            "</Storyboard>" +
            "</VisualTransition>" +
            "</VisualStateGroup.Transitions>" +
            "</VisualStateGroup>" +
            "</VisualStateManager.VisualStateGroups>" +
            "</Grid>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Trigger>" +
            "</Style.Triggers>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var trigger = Assert.IsType<Trigger>(Assert.Single(style.Triggers));
        var template = Assert.IsType<ControlTemplate>(
            Assert.IsType<Setter>(Assert.Single(trigger.Setters)).Value);
        var root = Assert.IsType<Grid>(template.VisualTree);
        var group = Assert.IsType<VisualStateGroup>(Assert.Single(VisualStateManager.GetVisualStateGroups(root)));
        var transition = Assert.IsType<VisualTransition>(Assert.Single(group.Transitions));
        var animation = Assert.IsType<DoubleAnimation>(Assert.Single(transition.Storyboard!.Children));
        Assert.IsType<CubicEase>(animation.EasingFunction);
    }

    [Fact]
    public void Template_contentproperty_host_parents_nested_visual()
    {
        // MaterialDesign FlipperClassic shape: <wpf:Plane3D> carries
        // [ContentProperty("Child")] and nests its content grid directly.
        // Converted single-visual hosts parent through [ContentProperty] in
        // templates like page XAML does.
        SeedApp();
        UseTestAssembly();
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\" " +
            "xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\">" +
            "<Style x:Key=\"Probe\" TargetType=\"{x:Type Button}\">" +
            "<Setter Property=\"Template\"><Setter.Value>" +
            "<ControlTemplate TargetType=\"{x:Type Button}\">" +
            "<t:SingleChildStub x:Name=\"Host\">" +
            "<Grid x:Name=\"Inner\" />" +
            "</t:SingleChildStub>" +
            "</ControlTemplate></Setter.Value></Setter>" +
            "</Style>" +
            "</ResourceDictionary>");
        var style = Assert.IsType<Style>(dict["Probe"]);
        var control = new Button();
        style.ApplyTo(control);
        var host = Assert.IsType<SingleChildStub>(control.Template?.FindName("Host", control));
        var inner = Assert.IsType<Grid>(host.Child);
        Assert.Same(inner, control.Template?.FindName("Inner", control));
    }

    [Fact]
    public void Removing_absent_visual_and_logical_children_is_a_no_op()
    {
        // MaterialDesign Plane3D shape: the Child setter removes its
        // (null) visual/logical fields on first set. Real WPF tolerates
        // this and the engine documents absent removal as a no-op.
        var stub = new VisualDetachStub();
        stub.DetachVisual(null);
        stub.DetachLogical(null);
        Assert.Equal(0, stub.VisualChildCount);
    }

    [Fact]
    public void Uielement_cache_mode_defaults_null_and_parses()
    {
        // MaterialDesign DataGrid shape: template adorner decorators carry a
        // CacheMode binding (WPF bitmap-cache hint). The engine retains the
        // UIElement-level value without rasterizing it.
        Assert.Null(new Button().CacheMode);
        var root = (Grid)XamlLoader.Load(
            "<Grid xmlns=\"" + PresentationNs + "\">" +
            "<AdornerDecorator CacheMode=\"{x:Null}\" />" +
            "</Grid>").Root;
        var decorator = Assert.IsType<AdornerDecorator>(Assert.Single(root.LogicalChildren));
        Assert.Null(decorator.CacheMode);
    }

    [Fact]
    public void ControlTemplate_element_expansion_validates_hierarchical_nesting_and_templatebindings()
    {
        SeedApp();
        UseTestAssembly();
        var xaml = @"
<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
                    xmlns:t=""clr-namespace:DesktopWasm.Wpf.Tests"">
  <ControlTemplate x:Key=""ComplexButtonTemplate"" TargetType=""{x:Type t:SelectControlStub}"">
    <Border x:Name=""OuterBorder"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"">
      <Grid x:Name=""LayoutGrid"">
        <Grid.RowDefinitions>
          <RowDefinition Height=""Auto"" />
          <RowDefinition Height=""*"" />
        </Grid.RowDefinitions>
        <StackPanel Grid.Row=""0"" x:Name=""HeaderPanel"">
          <TextBlock x:Name=""HeaderPart"" Text=""{TemplateBinding Title}"" FontWeight=""Bold"" />
        </StackPanel>
        <ContentPresenter Grid.Row=""1"" x:Name=""PART_Content"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"" />
      </Grid>
    </Border>
  </ControlTemplate>
</ResourceDictionary>";

        var dict = XamlLoader.LoadResourceDictionary(xaml);
        var template = Assert.IsType<ControlTemplate>(dict["ComplexButtonTemplate"]);

        // 1. Verify LoadContent generates a fresh independent hierarchical visual tree
        var first = Assert.IsType<Border>(template.LoadContent());
        var second = Assert.IsType<Border>(template.LoadContent());
        Assert.NotSame(first, second);

        var firstGrid = Assert.IsType<Grid>(first.Child);
        var secondGrid = Assert.IsType<Grid>(second.Child);
        Assert.NotSame(firstGrid, secondGrid);
        Assert.Equal(2, firstGrid.RowDefinitions.Count);

        var stackPanel = Assert.IsType<StackPanel>(firstGrid.Children[0]);
        var header = Assert.IsType<TextBlock>(stackPanel.Children[0]);
        Assert.Equal("HeaderPart", header.Name);

        var presenter = Assert.IsType<ContentPresenter>(firstGrid.Children[1]);
        Assert.Equal("PART_Content", presenter.Name);

        // 2. Verify ApplyTemplate attaches visual tree and resolves template parts and bindings
        var control = new SelectControlStub
        {
            Title = "Template Header",
            Background = Brushes.Navy,
            BorderBrush = Brushes.Silver,
            BorderThickness = new Thickness(2),
            Template = template,
        };

        Assert.True(control.ApplyTemplate());
        Assert.True(control.Applied);

        var appliedBorder = Assert.IsType<Border>(control.TemplateRoot);
        Assert.Same(appliedBorder, control.Template?.FindName("OuterBorder", control));
        Assert.Same(appliedBorder, control.GetTemplateChild("OuterBorder"));

        var appliedGrid = Assert.IsType<Grid>(control.GetTemplateChild("LayoutGrid"));
        Assert.NotNull(appliedGrid);

        var appliedHeader = Assert.IsType<TextBlock>(control.GetTemplateChild("HeaderPart"));
        Assert.NotNull(appliedHeader);
        Assert.Equal("Template Header", appliedHeader.Text);

        var appliedPresenter = Assert.IsType<ContentPresenter>(control.GetTemplateChild("PART_Content"));
        Assert.NotNull(appliedPresenter);

        Assert.Equal(new SolidColorBrush(Colors.Navy).ToRgba(), ((SolidColorBrush)appliedBorder.Background!).ToRgba());
        Assert.Equal(new SolidColorBrush(Colors.Silver).ToRgba(), ((SolidColorBrush)appliedBorder.BorderBrush!).ToRgba());
        Assert.Equal(new Thickness(2), appliedBorder.BorderThickness);

        // Layout pass verification
        control.Measure(new WpfSize(300, 200));
        control.Arrange(new WpfRect(0, 0, 300, 200));
        Assert.True(control.DesiredSize.Width > 0);
        Assert.True(control.DesiredSize.Height > 0);
    }

    [Fact]
    public void DataTemplate_element_expansion_validates_hierarchical_nesting_and_binding_evaluation()
    {
        SeedApp();
        var xaml = @"
<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <DataTemplate x:Key=""ComplexCardTemplate"">
    <Border x:Name=""CardBorder"" Background=""White"" BorderThickness=""1"" CornerRadius=""4"">
      <Grid x:Name=""CardGrid"">
        <StackPanel x:Name=""ContentStack"" Orientation=""Vertical"">
          <TextBlock x:Name=""TitleText"" Text=""{Binding Title}"" FontWeight=""Bold"" />
          <TextBlock x:Name=""DetailsText"" Text=""{Binding Description}"" Margin=""0,4,0,0"" />
        </StackPanel>
      </Grid>
    </Border>
    <DataTemplate.Triggers>
      <DataTrigger Binding=""{Binding IsHighlighted}"" Value=""True"">
        <Setter TargetName=""CardBorder"" Property=""Background"" Value=""LightGreen"" />
      </DataTrigger>
    </DataTemplate.Triggers>
  </DataTemplate>
</ResourceDictionary>";

        var dict = XamlLoader.LoadResourceDictionary(xaml);
        var template = Assert.IsType<DataTemplate>(dict["ComplexCardTemplate"]);

        // 1. Verify LoadContent creates fresh independent hierarchical instances
        var first = Assert.IsType<Border>(template.LoadContent());
        var second = Assert.IsType<Border>(template.LoadContent());
        Assert.NotSame(first, second);

        var firstGrid = Assert.IsType<Grid>(first.Child);
        var secondGrid = Assert.IsType<Grid>(second.Child);
        Assert.NotSame(firstGrid, secondGrid);

        var firstStack = Assert.IsType<StackPanel>(firstGrid.Children[0]);
        Assert.Equal(2, firstStack.Children.Count);

        var title = Assert.IsType<TextBlock>(firstStack.Children[0]);
        var details = Assert.IsType<TextBlock>(firstStack.Children[1]);
        Assert.Equal("TitleText", title.Name);
        Assert.Equal("DetailsText", details.Name);

        // 2. Verify DataContext evaluates bindings and DataTemplate triggers
        first.DataContext = new ItemCardModel { Title = "Card 1", Description = "Active Item", IsHighlighted = true };

        Assert.Equal("Card 1", title.Text);
        Assert.Equal("Active Item", details.Text);
        Assert.Equal(new SolidColorBrush(Colors.LightGreen).ToRgba(), ((SolidColorBrush)first.Background!).ToRgba());

        // Toggle trigger condition off
        first.DataContext = new ItemCardModel { Title = "Card 1", Description = "Inactive Item", IsHighlighted = false };
        Assert.Equal("Inactive Item", details.Text);
        Assert.Equal(new SolidColorBrush(Colors.White).ToRgba(), ((SolidColorBrush)first.Background!).ToRgba());

        // Layout pass verification
        first.Measure(new WpfSize(200, 100));
        first.Arrange(new WpfRect(0, 0, 200, 100));
        Assert.True(first.DesiredSize.Width > 0);
        Assert.True(first.DesiredSize.Height > 0);
    }
}

public sealed class ItemCardModel
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsHighlighted { get; set; }
}

/// <summary>ScreenToGif SelectControl shape: Control with DP-backed props,
/// OnApplyTemplate resolving a named template part (like
/// SelectControl.cs Template.FindName("StatusControlGrid", this)).</summary>
public sealed class SelectControlStub : Control
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SelectControlStub),
            new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string?)GetValue(TitleProperty) ?? string.Empty;
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty ShowStatusProperty =
        DependencyProperty.Register(nameof(ShowStatus), typeof(bool), typeof(SelectControlStub),
            new PropertyMetadata(true));

    public bool ShowStatus
    {
        get => (bool)(GetValue(ShowStatusProperty) ?? true);
        set => SetValue(ShowStatusProperty, value);
    }

    public UniformGrid? StatusGrid { get; private set; }

    public bool Applied { get; private set; }

    protected override void OnApplyTemplate()
    {
        Applied = true;
        StatusGrid = Template?.FindName("StatusControlGrid", this) as UniformGrid;
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var width = 0.0;
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(available);
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
        }
        return new WpfSize(width, height);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        var y = finalRect.Y;
        foreach (var child in Children)
        {
            child.Arrange(new WpfRect(finalRect.X, y, finalRect.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        foreach (var child in Children) RenderVisible(dc, child);
    }
}

/// <summary>Gallery ControlExample shape: bare Control with a
/// content-property sample slot (no measure override of its own).</summary>
[ContentProperty("Sample")]
public sealed class SampleHostStub : Control
{
    public static readonly DependencyProperty SampleProperty = DependencyProperty.Register(
        nameof(Sample), typeof(object), typeof(SampleHostStub), new PropertyMetadata(null));

    public object? Sample
    {
        get => GetValue(SampleProperty);
        set => SetValue(SampleProperty, value);
    }
}

/// <summary>Gallery PageHeader shape: DP Title/Description/ShowDescription
/// (mirrors WPFGallery PageHeader codebehind exactly).</summary>
public sealed class PageHeaderStub : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageHeaderStub), new PropertyMetadata(null));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(PageHeaderStub), new PropertyMetadata(null));

    public static readonly DependencyProperty ShowDescriptionProperty = DependencyProperty.Register(
        nameof(ShowDescription), typeof(bool), typeof(PageHeaderStub), new PropertyMetadata(true));

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool ShowDescription
    {
        get => (bool)(GetValue(ShowDescriptionProperty) ?? true);
        set => SetValue(ShowDescriptionProperty, value);
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var width = 0.0;
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(available);
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
        }
        return new WpfSize(width, height);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        var y = finalRect.Y;
        foreach (var child in Children)
        {
            child.Arrange(new WpfRect(finalRect.X, y, finalRect.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        foreach (var child in Children) RenderVisible(dc, child);
    }
}


/// <summary>Converted single-visual host (matches wpf:Plane3D
/// [ContentProperty("Child")]): nests one visual directly.</summary>
[ContentProperty("Child")]
internal sealed class SingleChildStub : Control
{
    public FrameworkElement? Child { get; set; }
}

/// <summary>Visual/logical detach probe (matches the converted Plane3D
/// Child setter, which removes null visual/logical fields on first set).</summary>
internal sealed class VisualDetachStub : Control
{
    public void DetachVisual(Visual? child) => RemoveVisualChild(child!);

    public void DetachLogical(object? child) => RemoveLogicalChild(child!);
}

/// <summary>Implicit-style host (matches vendor Card defaults): the test
/// generic.xaml carries its unkeyed style, resolved by Type through the
/// defining assembly's theme dictionary.</summary>
internal sealed class BasedOnBaseStub : Control
{
}

/// <summary>Theme-scoped style host (matches DrawerHost): the test
/// generic.xaml carries its unkeyed style plus the theme-internal brush
/// its setters reference.</summary>
internal sealed class BasedOnThemedStub : Control
{
}

/// <summary>Nested singleton host (matches the converted RatingBar
/// TextBlockForegroundConverter nested singleton): x:Static
/// Outer+Nested.Instance resolves through the outer type.</summary>
internal sealed class StaticHostStub
{
    public sealed class NestedStub : IMultiValueConverter
    {
        public static readonly NestedStub Instance = new();

        public object? Convert(object?[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            values.Length == 1 ? values[0] : values;

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Transform-producing composite (matches the SmartHint
    /// FloatingHint converters): the first child value becomes the
    /// TranslateTransform X offset.</summary>
    public sealed class TransformStub : IMultiValueConverter
    {
        public static readonly TransformStub Instance = new();

        public object? Convert(object?[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            new TranslateTransform
            {
                X = values.Length > 0 && values[0] is double d ? d
                    : System.Convert.ToDouble(values[0], System.Globalization.CultureInfo.InvariantCulture),
            };

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
