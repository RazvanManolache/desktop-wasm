using DesktopWasm.Wpf;

namespace DesktopWasm.BrowserHost;

// Gallery PageHeader boot-chain shape (mirrors WPFGallery PageHeader
// codebehind): DP Title/Description/ShowDescription on a Control whose
// template parts resolve through ControlTemplate.FindName.
public sealed class TemplateHeaderControl : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(TemplateHeaderControl), new PropertyMetadata(null));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(TemplateHeaderControl), new PropertyMetadata(null));

    public static readonly DependencyProperty ShowDescriptionProperty = DependencyProperty.Register(
        nameof(ShowDescription), typeof(bool), typeof(TemplateHeaderControl), new PropertyMetadata(true));

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
        foreach (var child in Children) RenderChild(dc, child);
    }
}

// ScreenToGif SelectControl shape: template parts behind
// Template.FindName("StatusControlGrid", this) plus TemplateBinding values.
public sealed class StatusControl : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(StatusControl), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string?)GetValue(TitleProperty) ?? string.Empty;
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty ShowStatusProperty = DependencyProperty.Register(
        nameof(ShowStatus), typeof(bool), typeof(StatusControl), new PropertyMetadata(true));

    public bool ShowStatus
    {
        get => (bool)(GetValue(ShowStatusProperty) ?? true);
        set => SetValue(ShowStatusProperty, value);
    }

    public UniformGrid? StatusGrid { get; private set; }

    protected override void OnApplyTemplate() =>
        StatusGrid = Template?.FindName("StatusControlGrid", this) as UniformGrid;

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
        foreach (var child in Children) RenderChild(dc, child);
    }
}

// Gallery EmptyToVisibilityConverter shape (empty description collapses).
public sealed class TemplateVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        string.IsNullOrEmpty(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

// Template cluster slice as an engine-native lab: a gallery PageHeader-shape
// control inflates Title/Description bindings with a TargetName trigger, a
// ScreenToGif StatusControl-shape control resolves its UniformGrid part
// through Template.FindName, and a Button paints BorderlessButton-shape
// chrome through a BasedOn style chain. Pure engine surface; the browser
// proof asserts mirrored readout text and layout rects after real input.
public sealed class TemplatesLab
{
    public static TemplatesLab? Current { get; private set; }

    public Window Window { get; } = new() { Title = "Templates lab" };

    public TemplateHeaderControl Header { get; } = new()
    {
        Title = "Samples",
        Description = "Browse",
        ShowDescription = false,
    };

    public StatusControl Status { get; } = new()
    {
        Title = "Rec",
        ShowStatus = true,
        Width = 300,
        Height = 80,
        Background = new SolidColorBrush(Colors.Red),
    };

    public Button ThemedButton { get; } = new() { Content = "Go", Width = 200 };

    public TextBlock HeaderReadout { get; } = new() { Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock StatusReadout { get; } = new() { Margin = new Thickness(8, 4, 8, 4) };

    public TextBlock ButtonReadout { get; } = new() { Margin = new Thickness(8, 4, 8, 4) };

    public Button Toggle { get; } = new() { Content = "Toggle description", Width = 200, Margin = new Thickness(8, 4, 8, 4) };

    public Action<string, string>? Noter { get; set; }

    public IReadOnlyDictionary<string, string> Notes => _notes;

    private readonly Dictionary<string, string> _notes = new();
    private WasmWpfHost? _host;

    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    private const string ThemeXaml =
        "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\" " +
        "xmlns:lab=\"clr-namespace:DesktopWasm.BrowserHost\">" +
        "<lab:TemplateVisibilityConverter x:Key=\"EmptyToVisibilityConverter\" />" +
        "<Style x:Key=\"TitleTextBlockStyle\" TargetType=\"TextBlock\">" +
        "<Setter Property=\"FontSize\" Value=\"20\" />" +
        "</Style>" +
        "<Style x:Key=\"BodyTextBlockStyle\" TargetType=\"TextBlock\">" +
        "<Setter Property=\"FontSize\" Value=\"12\" />" +
        "</Style>" +
        "</ResourceDictionary>";

    private const string TemplatesXaml =
        "<ResourceDictionary xmlns=\"" + PresentationNs + "\" xmlns:x=\"" + XamlNs + "\" " +
        "xmlns:lab=\"clr-namespace:DesktopWasm.BrowserHost\">" +
        "<Style x:Key=\"PageHeaderLabStyle\" TargetType=\"{x:Type lab:TemplateHeaderControl}\">" +
        "<Setter Property=\"Focusable\" Value=\"False\" />" +
        "<Setter Property=\"Template\"><Setter.Value>" +
        "<ControlTemplate TargetType=\"{x:Type lab:TemplateHeaderControl}\">" +
        "<StackPanel VerticalAlignment=\"Center\">" +
        "<Label x:Name=\"TitleTextBlock\" Focusable=\"True\">" +
        "<TextBlock Style=\"{StaticResource TitleTextBlockStyle}\" Text=\"{TemplateBinding Title}\" />" +
        "</Label>" +
        "<Label Visibility=\"{TemplateBinding Description, Converter={StaticResource EmptyToVisibilityConverter}}\" Focusable=\"True\">" +
        "<TextBlock x:Name=\"DescriptionTextBlock\" Text=\"{TemplateBinding Description}\" Style=\"{StaticResource BodyTextBlockStyle}\" />" +
        "</Label>" +
        "</StackPanel>" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property=\"ShowDescription\" Value=\"False\">" +
        "<Setter TargetName=\"DescriptionTextBlock\" Property=\"Visibility\" Value=\"Hidden\" />" +
        "</Trigger>" +
        "</ControlTemplate.Triggers>" +
        "</ControlTemplate></Setter.Value></Setter>" +
        "</Style>" +
        "<Style x:Key=\"StatusLabStyle\" TargetType=\"{x:Type lab:StatusControl}\">" +
        "<Setter Property=\"Template\"><Setter.Value>" +
        "<ControlTemplate TargetType=\"{x:Type lab:StatusControl}\">" +
        "<Canvas x:Name=\"MainCanvas\" Width=\"{TemplateBinding Width}\" Height=\"{TemplateBinding Height}\">" +
        "<UniformGrid x:Name=\"StatusControlGrid\" Columns=\"3\" />" +
        "<Rectangle x:Name=\"FillRect\" Fill=\"{TemplateBinding Background}\" Width=\"10\" Height=\"10\" />" +
        "</Canvas>" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property=\"IsEnabled\" Value=\"False\">" +
        "<Setter TargetName=\"FillRect\" Property=\"Opacity\" Value=\"0.5\" />" +
        "</Trigger>" +
        "</ControlTemplate.Triggers>" +
        "</ControlTemplate></Setter.Value></Setter>" +
        "</Style>" +
        "<Style x:Key=\"BorderlessLabStyle\" TargetType=\"Button\">" +
        "<Setter Property=\"Background\" Value=\"Transparent\" />" +
        "<Setter Property=\"Border.CornerRadius\" Value=\"4\" />" +
        "<Setter Property=\"Template\"><Setter.Value>" +
        "<ControlTemplate TargetType=\"Button\">" +
        "<Border x:Name=\"EnclosingBorder\" Background=\"{TemplateBinding Background}\" " +
        "BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"{TemplateBinding BorderThickness}\" " +
        "CornerRadius=\"{TemplateBinding Border.CornerRadius}\" SnapsToDevicePixels=\"True\">" +
        "<ContentPresenter x:Name=\"Presenter\" RecognizesAccessKey=\"True\" />" +
        "</Border>" +
        "</ControlTemplate></Setter.Value></Setter>" +
        "</Style>" +
        "<Style x:Key=\"TitleBarLabStyle\" BasedOn=\"{StaticResource BorderlessLabStyle}\" TargetType=\"Button\">" +
        "<Setter Property=\"Border.CornerRadius\" Value=\"0\" />" +
        "</Style>" +
        "</ResourceDictionary>";

    public static Window Build()
    {
        var lab = new TemplatesLab();
        Current = lab;
        lab.Compose();
        return lab.Window;
    }

    public void Compose()
    {
        // Fresh lab application (mirrors gallery App.xaml hydration): theme
        // resources land first so template StaticResources resolve, exactly
        // the PageStyles-before-PageHeader order of the real boot chain.
        var app = new Application();
        Application.Current = app;
        var previousSource = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = typeof(TemplatesLab).Assembly;
        ResourceDictionary resources;
        try
        {
            var theme = XamlLoader.LoadResourceDictionary(ThemeXaml);
            foreach (var key in new object[] { "EmptyToVisibilityConverter", "TitleTextBlockStyle", "BodyTextBlockStyle" })
                if (theme.TryGetValue(key, out var value))
                    app.Resources.Add(key, value);
            resources = XamlLoader.LoadResourceDictionary(TemplatesXaml);
            // Lab styles join application scope (like merged app dictionaries)
            // so cross-style BasedOn chains resolve at apply time.
            foreach (var key in new object[] { "PageHeaderLabStyle", "StatusLabStyle", "BorderlessLabStyle", "TitleBarLabStyle" })
                if (resources.TryGetValue(key, out var style))
                    app.Resources.Add(key, style);
        }
        finally { XamlLoader.CustomElementSource = previousSource; }
        Header.Style = (Style)resources["PageHeaderLabStyle"]!;
        Status.Style = (Style)resources["StatusLabStyle"]!;
        ThemedButton.Style = (Style)resources["TitleBarLabStyle"]!;

        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.AddChild(new TextBlock
        {
            Text = "Template inflation: gallery header, gif status grid, themed button.",
            Margin = new Thickness(8, 8, 8, 4),
        });
        panel.AddChild(Header);
        panel.AddChild(HeaderReadout);
        panel.AddChild(Status);
        panel.AddChild(StatusReadout);
        panel.AddChild(ThemedButton);
        panel.AddChild(ButtonReadout);
        Toggle.Click += (_, _) =>
        {
            Header.ShowDescription = !Header.ShowDescription;
            RefreshNotes();
        };
        panel.AddChild(Toggle);

        Window.Content = panel;
        RefreshNotes();
    }

    public void Wire(WasmWpfHost host)
    {
        _host = host;
        WasmMilCore.RenderHeadless(Window, new WpfSize(800, 600));
        PublishLayoutNotes();
    }

    public void PublishNotes()
    {
        foreach (var (key, value) in _notes)
            Note(key, value);
        PublishLayoutNotes();
    }

    internal void RefreshNotes()
    {
        var titleText = (Header.Template?.FindName("TitleTextBlock", Header) as Label)?.Content as TextBlock;
        SetStatus("header.title", titleText?.Text ?? "?");
        var descriptionText = Header.Template?.FindName("DescriptionTextBlock", Header) as TextBlock;
        SetStatus("header.desc", descriptionText?.Text ?? "?");
        SetStatus("header.descvis", (descriptionText?.Visibility ?? Visibility.Visible).ToString());
        SetStatus("header.toggle", Header.ShowDescription ? "shown" : "hidden");
        SetStatus("status.findname", Status.StatusGrid is not null ? "found" : "missing");
        SetStatus("status.columns", (Status.StatusGrid?.Columns ?? -1).ToString());
        var border = ThemedButton.Template?.FindName("EnclosingBorder", ThemedButton) as Border;
        SetStatus("button.corner", border?.CornerRadius.TopLeft.ToString("0") ?? "?");
        var presenter = ThemedButton.Template?.FindName("Presenter", ThemedButton) as ContentPresenter;
        SetStatus("button.content", presenter?.Content?.ToString() ?? "?");
        HeaderReadout.Text = $"Header: {Notes["header.title"]} / {Notes["header.desc"]} ({Notes["header.descvis"]})";
        StatusReadout.Text = $"Status grid: {Notes["status.findname"]} ({Notes["status.columns"]} cols)";
        ButtonReadout.Text = $"Button: {Notes["button.content"]} corner {Notes["button.corner"]}";
        _host?.Invalidate();
    }

    private void SetStatus(string key, string text)
    {
        _notes[key] = text;
        Note(key, text);
    }

    private void Note(string key, string value)
    {
        try { Noter?.Invoke(key, value); }
        catch { }
    }

    private void PublishLayoutNotes()
    {
        var rects = new Dictionary<string, string>
        {
            ["header"] = Rect(Header),
            ["status"] = Rect(Status),
            ["button"] = Rect(ThemedButton),
            ["toggle"] = Rect(Toggle),
        };
        var json = "{" + string.Join(",", rects.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\"")) + "}";
        _notes["layout"] = json;
        Note("layout", json);
    }

    private static string Rect(UIElement? element)
    {
        if (element is null) return "0,0,0,0";
        var r = element.RenderRect;
        return $"{r.X:0},{r.Y:0},{r.Width:0},{r.Height:0}";
    }
}
