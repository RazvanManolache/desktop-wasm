using System.Xml;
using DesktopWasm.Graphics;
using DesktopWasm.Wpf;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// XAML + app-lifecycle cluster (slice-27): Startup/Exit args + handlers,
/// WindowCollection, Row/ColumnDefinitionCollections, HeaderedContentControl
/// and the XamlReader/XamlWriter/XamlParseException runtime the real
/// ScreenToGif Util layer (localization export, XamlClone/DeepCopy) binds.
/// PasswordBox stays the XAML unknown-element canary.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class XamlAppLifecycleTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public void Dispose() => Application.Current = _previous;

    private sealed class RecordingApp : Application
    {
        public int StartupCalls;
        public int ExitCalls;
        protected override void OnStartup(StartupEventArgs e)
        {
            StartupCalls++;
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ExitCalls++;
            base.OnExit(e);
        }
    }

    [Fact]
    public void Startup_args_reach_handlers_in_order()
    {
        Application.Current = new RecordingApp();
        var app = (RecordingApp)Application.Current;
        var seen = new List<string[]>();
        app.Startup += (_, e) => seen.Add(e.Args);
        app.RaiseStartup(new StartupEventArgs(["--record", "--fps=30"]));
        Assert.Equal(1, app.StartupCalls);
        var args = Assert.Single(seen);
        Assert.Equal(["--record", "--fps=30"], args);
    }

    [Fact]
    public void Startup_handler_delegate_shape_matches_wpf()
    {
        var method = typeof(Application).GetEvent("Startup");
        Assert.NotNull(method);
        Assert.Equal(typeof(StartupEventHandler), method.EventHandlerType);
        var invoke = typeof(StartupEventHandler).GetMethod("Invoke");
        Assert.NotNull(invoke);
        Assert.Equal(typeof(void), invoke.ReturnType);
        Assert.Equal(typeof(object), invoke.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(StartupEventArgs), invoke.GetParameters()[1].ParameterType);
    }

    [Fact]
    public void Shutdown_raises_exit_with_code_and_closes_windows()
    {
        Application.Current = new RecordingApp();
        var app = (RecordingApp)Application.Current;
        var codes = new List<int>();
        app.Exit += (_, e) => codes.Add(e.ApplicationExitCode);
        var main = new Window { Title = "Main" };
        var second = new Window { Title = "Second" };
        app.MainWindow = main;
        Assert.Equal(2, app.Windows.Count);
        app.Shutdown(3);
        Assert.Equal(3, app.ExitCode);
        Assert.Equal([3], codes);
        Assert.Equal(1, app.ExitCalls);
        Assert.False(main.IsVisible);
        Assert.False(second.IsVisible);
        Assert.Empty(app.Windows);
    }

    [Fact]
    public void Windows_collection_tracks_construction_and_close()
    {
        // Read-only surface first, on a throwaway app (constructing the
        // probe window registers it, so it must not pollute the main app).
        Application.Current = new Application();
        var probeApp = Application.Current;
        Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.ICollection<Window>)probeApp.Windows).Add(new Window()));
        Assert.Throws<NotSupportedException>(() => ((System.Collections.IList)probeApp.Windows).Clear());
        Assert.Single(probeApp.Windows);

        Application.Current = new Application();
        var app = Application.Current;
        Assert.Empty(app.Windows);
        var first = new Window { Title = "First" };
        var second = new Window { Title = "Second" };
        Assert.Equal(2, app.Windows.Count);
        Assert.Same(first, app.Windows[0]);
        Assert.True(app.Windows.Contains(second));
        Assert.Equal(0, app.Windows.IndexOf(first));
        var array = new Window[2];
        app.Windows.CopyTo(array, 0);
        Assert.Equal([first, second], array);
        Assert.True(app.Windows.IsReadOnly);
        second.Close();
        Assert.Equal([first], app.Windows.ToArray());
        first.Close();
        Assert.Empty(app.Windows);
    }

    [Fact]
    public void Last_window_close_raises_exit_unless_explicit_shutdown()
    {
        Application.Current = new Application();
        var app = Application.Current;
        var exits = 0;
        app.Exit += (_, _) => exits++;
        var window = new Window();
        window.Close();
        Assert.Equal(1, exits);

        Application.Current = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var explicitApp = Application.Current;
        var explicitExits = 0;
        explicitApp.Exit += (_, _) => explicitExits++;
        var other = new Window();
        other.Close();
        Assert.Equal(0, explicitExits);
        Assert.Equal(ShutdownMode.OnExplicitShutdown, explicitApp.ShutdownMode);
        Assert.Equal(0, (int)ShutdownMode.OnLastWindowClose);
        Assert.Equal(1, (int)ShutdownMode.OnMainWindowClose);
        Assert.Equal(2, (int)ShutdownMode.OnExplicitShutdown);
    }

    [Fact]
    public void Definition_collections_carry_add_insert_remove_range()
    {
        var grid = new Grid();
        Assert.IsType<RowDefinitionCollection>(grid.RowDefinitions);
        Assert.IsType<ColumnDefinitionCollection>(grid.ColumnDefinitions);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star() });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Pixel(120) });
        grid.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = GridLength.Star() });
        Assert.Equal(2, grid.RowDefinitions.Count);
        Assert.Equal(2, grid.ColumnDefinitions.Count);
        Assert.Equal(GridUnitType.Star, grid.ColumnDefinitions[0].Width.Unit);
        Assert.True(grid.ColumnDefinitions.Contains(grid.ColumnDefinitions[1]));
        Assert.Equal(1, grid.ColumnDefinitions.IndexOf(grid.ColumnDefinitions[1]));
        grid.RowDefinitions.RemoveRange(0, 2);
        Assert.Empty(grid.RowDefinitions);
        Assert.False(grid.RowDefinitions.IsSynchronized);
        Assert.NotNull(grid.RowDefinitions.SyncRoot);
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.ColumnDefinitions.RemoveRange(0, 5));
    }

    [Fact]
    public void HeaderedContentControl_renders_header_above_content()
    {
        var control = new HeaderedContentControl
        {
            Header = "Encoder",
            Content = new TextBlock { Text = "Body" },
        };
        Assert.True(control.HasHeader);
        Assert.Equal("Encoder", control.Header);
        Assert.Equal("Encoder", control.GetValue(HeaderedContentControl.HeaderProperty));
        var commands = WasmMilCore.RenderHeadless(control, new WpfSize(400, 200));
        var texts = commands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();
        Assert.Contains("Encoder", texts);
        Assert.Contains("Body", texts);
        var headerTop = commands.OfType<DrawCommand.DrawText>().First(t => t.Text == "Encoder").Y;
        var bodyTop = commands.OfType<DrawCommand.DrawText>().First(t => t.Text == "Body").Y;
        Assert.True(bodyTop > headerTop);
    }

    [Fact]
    public void HeaderedContentControl_hosts_live_header_visuals()
    {
        var control = new HeaderedContentControl
        {
            Header = new TextBlock { Text = "Live" },
            Content = "plain",
        };
        Assert.True(control.HasHeader);
        var commands = WasmMilCore.RenderHeadless(control, new WpfSize(400, 200));
        Assert.Contains(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "Live");
    }

    [Fact]
    public void HeaderedContentControl_binds_header_resource_reference()
    {
        Application.Current = new Application();
        var app = Application.Current;
        app.Resources.Add("EncoderHeader", "Quantizer");
        var control = new HeaderedContentControl();
        control.SetResourceReference(HeaderedContentControl.HeaderProperty, "EncoderHeader");
        Assert.Equal("Quantizer", control.Header);
        Assert.True(control.HasHeader);
    }

    [Fact]
    public void HeaderedContentControl_parses_header_and_content_xaml()
    {
        const string xaml =
            "<HeaderedContentControl Header=\"Encoder\">" +
            "<HeaderedContentControl.Header><TextBlock>Live</TextBlock></HeaderedContentControl.Header>" +
            "<Button>Go</Button>" +
            "</HeaderedContentControl>";
        var control = Assert.IsType<HeaderedContentControl>(XamlLoader.Load(xaml).Root);
        // Property-element header wins over the attribute, like WPF.
        Assert.IsType<TextBlock>(control.Header);
        Assert.IsType<Button>(control.Content);
        var attributed = Assert.IsType<HeaderedContentControl>(
            XamlLoader.Load("<HeaderedContentControl Header=\"Encoder\"/>").Root);
        Assert.Equal("Encoder", attributed.Header);
        Assert.True(attributed.HasHeader);
        var empty = new HeaderedContentControl();
        Assert.False(empty.HasHeader);
    }

    [Fact]
    public void XamlWriter_round_trips_element_trees()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Parse("2*") });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 30 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Pixel(120) });
        var button = new Button { Name = "Go", Content = "Save" };
        button.SetAttached("Grid.Row", 1);
        grid.AddChild(button);
        grid.AddChild(new TextBlock { Text = "Hint" });
        grid.AddChild(new HeaderedContentControl { Header = "Section", Content = "Details" });

        var saved = XamlWriter.Save(grid);
        Assert.Contains("<Grid", saved, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"1\"", saved, StringComparison.Ordinal);
        var clone = Assert.IsType<Grid>(XamlReader.Parse(saved));
        Assert.Equal(2, clone.RowDefinitions.Count);
        Assert.Equal(GridUnitType.Star, clone.RowDefinitions[0].Height.Unit);
        Assert.Equal(2, clone.RowDefinitions[0].Height.Value);
        Assert.Equal(GridUnitType.Auto, clone.RowDefinitions[1].Height.Unit);
        Assert.Equal(30, clone.RowDefinitions[1].MinHeight);
        Assert.Equal(120, clone.ColumnDefinitions[0].Width.Value);
        var cloneButton = Assert.IsType<Button>(clone.LogicalChildren.OfType<Button>().Single());
        Assert.Equal("Go", cloneButton.Name);
        Assert.Equal("Save", cloneButton.Content);
        Assert.Equal(1, cloneButton.GetAttached("Grid.Row"));
        Assert.Contains("Hint", clone.LogicalChildren.OfType<TextBlock>().Select(t => t.Text));
        var cloneHeadered = Assert.IsType<HeaderedContentControl>(
            clone.LogicalChildren.OfType<HeaderedContentControl>().Single());
        Assert.Equal("Section", cloneHeadered.Header);
        Assert.Equal("Details", cloneHeadered.Content);
    }

    [Fact]
    public void XamlWriter_round_trips_resource_dictionaries_through_streams()
    {
        // Merged Source entries resolve through the pack store on load
        // (the localization flow registers downloaded files first).
        Application.Current = new Application();
        Application.Current.RegisterPackResource(
            "/Resources/StringResources.en.xaml",
            "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<String xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Key=\"Hello\">World</String>" +
            "</ResourceDictionary>");
        var dictionary = new ResourceDictionary();
        dictionary.Add("Greeting", "Hello");
        dictionary.Add("Scale", 1.5);
        dictionary.Add("Retries", 3);
        dictionary.Add("Enabled", true);
        dictionary.Add("Accent", Color.FromRgb(0x12, 0x34, 0x56));
        dictionary.Add("AccentBrush", new SolidColorBrush(Color.FromRgb(0x12, 0x34, 0x56)));
        dictionary.Add("Family", new FontFamily("Segoe UI"));
        dictionary.Add("Margin", new Thickness(8, 1, 8, 1));
        dictionary.Add("Radius", new CornerRadius(4));
        var merged = new ResourceDictionary { Source = new Uri("/Resources/StringResources.en.xaml", UriKind.RelativeOrAbsolute) };
        dictionary.MergedDictionaries.Add(merged);

        string saved;
        using (var text = new StringWriter()) { XamlWriter.Save(dictionary, text); saved = text.ToString(); }
        using var stream = new MemoryStream();
        XamlWriter.Save(dictionary, stream);
        Assert.True(stream.Length > 0);
        stream.Seek(0, SeekOrigin.Begin);
        // No BOM: the BAML sniff reads a text lead byte.
        Assert.NotEqual(0xEF, stream.ReadByte());
        stream.Seek(0, SeekOrigin.Begin);
        var loaded = Assert.IsType<ResourceDictionary>(XamlReader.Load(stream));
        Assert.Equal("Hello", loaded["Greeting"]);
        Assert.Equal(1.5, loaded["Scale"]);
        Assert.Equal(3, loaded["Retries"]);
        Assert.Equal(true, loaded["Enabled"]);
        // The loader stores <Color> entries as packed 0xRRGGBBAA uints
        // (XamlLoader.ParseColor); the writer emits the same hex back.
        Assert.Equal(0x123456FFu, loaded["Accent"]);
        // The loader stores full-opacity <SolidColorBrush Color=.../>
        // entries as their Color (ReadSolidColorBrushEntry); same color back.
        Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), loaded["AccentBrush"]);
        Assert.Equal(new FontFamily("Segoe UI"), loaded["Family"]);
        Assert.Equal(new Thickness(8, 1, 8, 1), loaded["Margin"]);
        Assert.Equal(new CornerRadius(4), loaded["Radius"]);
        Assert.Equal(new Uri("/Resources/StringResources.en.xaml", UriKind.RelativeOrAbsolute), loaded.MergedDictionaries.Single().Source);
        Assert.Contains("MergedDictionaries", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void XamlReader_loads_xml_reader_and_reports_line_info()
    {
        var source = new Button { Content = "Clone me" };
        var saved = XamlWriter.Save(source);
        using var reader = XmlReader.Create(new System.IO.StringReader(saved));
        var clone = Assert.IsType<Button>(XamlReader.Load(reader));
        Assert.Equal("Clone me", clone.Content);

        var failure = Assert.Throws<XamlParseException>(() =>
            XamlReader.Parse("<StackPanel><Button Nope=\"1\"/></StackPanel>"));
        Assert.True(failure.LineNumber > 0);
        Assert.Contains("not valid", failure.Message, StringComparison.Ordinal);

        var empty = Assert.Throws<XamlParseException>(() => XamlReader.Parse("   "));
        Assert.Equal(0, empty.LineNumber);
    }

    [Fact]
    public void XamlWriter_keeps_window_title()
    {
        var window = new Window { Title = "Recorder" };
        window.Content = new TextBlock { Text = "Hi" };
        var clone = Assert.IsType<Window>(XamlReader.Parse(XamlWriter.Save(window)));
        Assert.Equal("Recorder", clone.Title);
        Assert.Equal("Hi", Assert.IsType<TextBlock>(clone.Content).Text);
    }

    [Fact]
    public void XamlReader_strips_bom_streams()
    {
        var saved = "\uFEFF" + XamlWriter.Save(new TextBlock { Text = "BOM" });
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(saved));
        var clone = Assert.IsType<TextBlock>(XamlReader.Load(stream));
        Assert.Equal("BOM", clone.Text);
    }

    [Fact]
    public void MediaPlayer_stays_the_unknown_element_canary()
    {
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("<MediaPlayer/>"));
        Assert.Contains("Unknown element", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Exit_handler_delegate_shape_matches_wpf()
    {
        var method = typeof(Application).GetEvent("Exit");
        Assert.NotNull(method);
        Assert.Equal(typeof(ExitEventHandler), method.EventHandlerType);
        var args = new ExitEventArgs(7);
        Assert.Equal(7, args.ApplicationExitCode);
        Assert.Equal(0, new ExitEventArgs().ApplicationExitCode);
    }
}
