using DesktopWasm.BrowserHost;
using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery chrome/pack slice: MainWindow sets a custom WindowChrome
/// (CaptionHeight 50, GlassFrameThickness -1, resize border, aero buttons,
/// NonClientFrameEdges) with IsHitTestVisibleInChrome caption buttons, and
/// ControlExample.LoadResource reads page XAML through
/// Application.GetResourceStream. Both must work through the existing
/// pack store/renderer instead of failing conversion.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ChromePackTests : IDisposable
{
    private readonly Application? _previousApp = Application.Current;
    private readonly WasmInputRouter _router = new();
    private readonly WpfInputManager _manager;

    public ChromePackTests()
    {
        WpfAnimationClock.ResetForTests();
        WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
        _manager = new WpfInputManager(_router);
        Application.Current = new Application();
    }

    public void Dispose()
    {
        Application.Current = _previousApp;
        WpfAnimationClock.ResetForTests();
    }

    private static void Layout(Window window, double w = 800, double h = 600)
    {
        window.Measure(new WpfSize(w, h));
        window.Arrange(new WpfRect(0, 0, w, h));
    }

    private (Window Window, Button Ping, TextBlock Client) ChromeHarness(double caption = 50)
    {
        var window = new Window { Title = "ChromeLab" };
        var panel = new StackPanel();
        var title = new TextBlock { Text = "Gallery-style custom chrome" };
        var ping = new Button { Content = "Ping" };
        WindowChrome.SetIsHitTestVisibleInChrome(ping, true);
        var client = new TextBlock { Text = "Client area" };
        panel.AddChild(title);
        panel.AddChild(ping);
        panel.AddChild(client);
        window.Content = panel;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = caption,
            CornerRadius = new CornerRadius(12),
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(4),
            UseAeroCaptionButtons = true,
            NonClientFrameEdges = NonClientFrameEdges.Left | NonClientFrameEdges.Right | NonClientFrameEdges.Bottom,
        });
        Layout(window);
        return (window, ping, client);
    }

    [Fact]
    public void WindowChrome_defaults_match_wpf()
    {
        var chrome = new WindowChrome();
        Assert.Equal(0, chrome.CaptionHeight);
        Assert.Equal(new CornerRadius(0), chrome.CornerRadius);
        Assert.Equal(new Thickness(0), chrome.GlassFrameThickness);
        Assert.Equal(new Thickness(0), chrome.ResizeBorderThickness);
        Assert.Equal(NonClientFrameEdges.None, chrome.NonClientFrameEdges);
        Assert.True(chrome.UseAeroCaptionButtons);
    }

    [Fact]
    public void NonClientFrameEdges_flag_values_match_wpf()
    {
        Assert.Equal(0, (int)NonClientFrameEdges.None);
        Assert.Equal(1, (int)NonClientFrameEdges.Left);
        Assert.Equal(2, (int)NonClientFrameEdges.Top);
        Assert.Equal(4, (int)NonClientFrameEdges.Right);
        Assert.Equal(8, (int)NonClientFrameEdges.Bottom);
        Assert.Equal(
            NonClientFrameEdges.Left | NonClientFrameEdges.Right | NonClientFrameEdges.Bottom,
            (NonClientFrameEdges)(1 | 4 | 8));
    }

    [Fact]
    public void Set_and_get_window_chrome_roundtrip()
    {
        var window = new Window();
        Assert.Null(WindowChrome.GetWindowChrome(window));
        var chrome = new WindowChrome { CaptionHeight = 50 };
        WindowChrome.SetWindowChrome(window, chrome);
        Assert.Same(chrome, WindowChrome.GetWindowChrome(window));
        WindowChrome.SetWindowChrome(window, null);
        Assert.Null(WindowChrome.GetWindowChrome(window));
    }

    [Fact]
    public void Chrome_mutation_repaints_attached_windows()
    {
        var (window, _, _) = ChromeHarness();
        window.ClearInvalidation();
        WindowChrome.GetWindowChrome(window)!.CaptionHeight = 60;
        Assert.True(window.IsRenderDirty);
    }

    [Fact]
    public void IsHitTestVisibleInChrome_defaults_false_and_roundtrips()
    {
        var button = new Button { Content = "Back" };
        Assert.False(WindowChrome.GetIsHitTestVisibleInChrome(button));
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(button));
    }

    [Fact]
    public void Caption_click_hits_only_chrome_marked_controls()
    {
        var (window, ping, client) = ChromeHarness();
        _manager.AddRoot(window);

        // The marked Ping button sits in the caption band: it stays hittable.
        var pingPoint = new WpfPoint(
            ping.RenderRect.X + ping.RenderRect.Width / 2,
            ping.RenderRect.Y + ping.RenderRect.Height / 2);
        Assert.True(pingPoint.Y < 50);
        Assert.Same(ping, _manager.HitTest(pingPoint.X, pingPoint.Y));

        // Unmarked client text in the caption band is chrome: the window absorbs it.
        var title = (TextBlock)((StackPanel)window.Content!).LogicalChildren[0];
        var titlePoint = new WpfPoint(
            title.RenderRect.X + title.RenderRect.Width / 2,
            title.RenderRect.Y + title.RenderRect.Height / 2);
        Assert.True(titlePoint.Y < 50);
        Assert.Same(window, _manager.HitTest(titlePoint.X, titlePoint.Y));

        // Below the caption the client text hits normally.
        Assert.True(client.RenderRect.Y >= 50);
        var clientPoint = new WpfPoint(
            client.RenderRect.X + client.RenderRect.Width / 2,
            client.RenderRect.Y + client.RenderRect.Height / 2);
        Assert.Same(client, _manager.HitTest(clientPoint.X, clientPoint.Y));
    }

    [Fact]
    public void Caption_click_does_not_reach_client_handlers()
    {
        var (window, _, _) = ChromeHarness();
        _manager.AddRoot(window);
        var fired = 0;
        var title = (TextBlock)((StackPanel)window.Content!).LogicalChildren[0];
        title.PreviewMouseDown += (_, _) => fired++;

        var x = title.RenderRect.X + title.RenderRect.Width / 2;
        var y = title.RenderRect.Y + title.RenderRect.Height / 2;
        _router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, WasmPointerButton.Left, true) { Kind = WasmPointerKind.Down });

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Chrome_paints_caption_band_glass_edges_and_content()
    {
        var (window, _, _) = ChromeHarness();
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        var rects = commands.OfType<DrawCommand.FillRect>().ToList();
        // Caption band: full width, exactly the CaptionHeight tall.
        Assert.Contains(rects, r =>
            r.X == 0 && r.Y == 0 && r.W == 800 && r.H == 50);
        // Glass edges on the gallery's Left|Right|Bottom frame, 4px insets
        // (side strips run below the 50px caption, above the bottom strip).
        Assert.Contains(rects, r => r.X == 0 && r.Y == 50 && r.W == 4 && r.H == 546);
        Assert.Contains(rects, r => r.X == 796 && r.Y == 50 && r.W == 4 && r.H == 546);
        Assert.Contains(rects, r => r.X == 0 && r.Y == 596 && r.W == 800 && r.H == 4);
        // Content still paints through the chrome.
        var json = WasmMilCore.ExportFrameJson(commands);
        Assert.Contains("Gallery-style custom chrome", json, StringComparison.Ordinal);
        Assert.Contains("Client area", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Chrome_attach_leaves_content_measure_untouched()
    {
        var window = new Window { Title = "MeasureLab" };
        var body = new TextBlock { Text = "Body" };
        window.Content = body;
        Layout(window);
        var before = window.DesiredSize;
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 50 });
        window.Measure(new WpfSize(800, 600));
        Assert.Equal(before, window.DesiredSize);
    }

    [Fact]
    public void Xaml_chrome_attached_attribute_parses()
    {
        const string xaml =
            "<Window Title=\"ChromeXaml\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<StackPanel><Button Content=\"Back\" WindowChrome.IsHitTestVisibleInChrome=\"True\"/></StackPanel>" +
            "</Window>";
        var window = (Window)XamlLoader.Load(xaml).Root;
        var button = Assert.IsType<Button>(((StackPanel)window.Content!).LogicalChildren[0]);
        Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(button));
    }

    [Fact]
    public void Xaml_chrome_style_setter_parses()
    {
        const string xaml =
            "<Window Title=\"ChromeStyle\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Window.Resources><ResourceDictionary>" +
            "<Style TargetType=\"Button\"><Setter Property=\"WindowChrome.IsHitTestVisibleInChrome\" Value=\"True\"/></Style>" +
            "</ResourceDictionary></Window.Resources>" +
            "<Button Content=\"Back\"/>" +
            "</Window>";
        var window = (Window)XamlLoader.Load(xaml).Root;
        var button = Assert.IsType<Button>(window.Content);
        Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(button));
    }

    [Fact]
    public void GetResourceStream_reads_registered_pack_xaml()
    {
        const string body = "<TextBlock>Hello pack</TextBlock>";
        Application.Current!.RegisterPackResource("pack://application:,,,/Docs/Help.xaml", body);
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Docs/Help.xaml"));
        Assert.NotNull(info);
        Assert.Equal("application/xaml+xml", info.ContentType);
        using var reader = new StreamReader(info.Stream!);
        Assert.Equal(body, reader.ReadToEnd());
    }

    [Fact]
    public void GetResourceStream_missing_returns_null_like_gallery_null_pattern()
    {
        Assert.Null(Application.GetResourceStream(new Uri("pack://application:,,,/Docs/Missing.xaml")));
    }

    [Fact]
    public void GetResourceStream_reads_converted_assembly_manifest_bytes()
    {
        var asm = typeof(ChromePackTests).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("ChromePackProbe.bin", StringComparison.Ordinal));
        Assert.NotNull(name);
        // Manifest names carry dotted prefixes (…Fixtures.Pack.ChromePackProbe.bin):
        // the assembly-relative "Pack/…" path matches through the suffix rule.
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Pack/ChromePackProbe.bin"));
        Assert.NotNull(info);
        Assert.Equal("application/octet-stream", info.ContentType);
        using var copy = new MemoryStream();
        info.Stream!.CopyTo(copy);
        Assert.Equal(new byte[] { 0x50, 0x41, 0x43, 0x4B }, copy.ToArray());
    }

    [Fact]
    public void FindPackText_reads_bom_loose_xaml()
    {
        // WPFGallery shape: most loose XAML ships with a UTF-8 BOM, which
        // the manifest sniff must not mistake for non-XAML bytes.
        var text = WpfAppBoot.FindPackText("Pack/Bom.xaml", null);
        Assert.NotNull(text);
        Assert.StartsWith("<", text.TrimStart(), StringComparison.Ordinal);
        var dict = XamlLoader.LoadResourceDictionary(text);
        Assert.Equal("bom", dict["BomMarker"]);
    }

    [Fact]
    public void GetContentStream_resolves_pack_store_and_reports_image_type()
    {
        Application.Current!.RegisterPackResource("pack://application:,,,/Assets/Shot.png", "fakepng");
        var info = Application.GetContentStream(new Uri("pack://application:,,,/Assets/Shot.png"));
        Assert.NotNull(info);
        Assert.Equal("image/png", info.ContentType);
    }

    [Fact]
    public void GetRemoteStream_is_a_precise_boundary()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Application.GetRemoteStream(new Uri("https://example.com/app.xaml")));
        Assert.Contains("remote", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Chrome_pack_demo_wires_gallery_chrome_and_pack_text()
    {
        var window = DemoCatalog.Find("chrome-pack").Build();
        var chrome = WindowChrome.GetWindowChrome(window);
        Assert.NotNull(chrome);
        Assert.Equal(50, chrome.CaptionHeight);
        Assert.True(chrome.GlassCoversAll);
        Assert.Equal(
            NonClientFrameEdges.Left | NonClientFrameEdges.Right | NonClientFrameEdges.Bottom,
            chrome.NonClientFrameEdges);
        var panel = Assert.IsType<StackPanel>(window.Content);
        var ping = Assert.IsType<Button>(panel.LogicalChildren[0]);
        Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(ping));
        var pack = Assert.IsType<TextBlock>(panel.LogicalChildren[2]);
        Assert.Contains("GetResourceStream", pack.Text, StringComparison.Ordinal);
        ping.RaiseClick();
        var pings = Assert.IsType<TextBlock>(panel.LogicalChildren[5]);
        Assert.Equal("Pings: 1", pings.Text);
        var poke = Assert.IsType<Button>(panel.LogicalChildren[4]);
        poke.RaiseClick();
        var pokes = Assert.IsType<TextBlock>(panel.LogicalChildren[6]);
        Assert.Equal("Pokes: 1", pokes.Text);
    }

    [Fact]
    public void Gallery_control_example_load_pattern_reads_pack_stream()
    {
        // Mirrors WPFGallery Controls/ControlExample.LoadResource: null when
        // absent, full text when the pack store holds the page XAML.
        static string LoadResource(Uri uri)
        {
            if (Application.GetResourceStream(uri) is not { } info) return string.Empty;
            using var reader = new StreamReader(info.Stream!, System.Text.Encoding.UTF8);
            return reader.ReadToEnd();
        }

        // A real Gallery package can already be loaded by another test.
        // Use an absent fixture resource rather than its actual embedded page.
        var uri = new Uri("pack://application:,,,/__fixture__/Views/BasicInput/ButtonPage.xaml");
        Assert.Equal(string.Empty, LoadResource(uri));
        Application.Current!.RegisterPackResource(uri.AbsoluteUri, "<Page>Buttons</Page>");
        Assert.Equal("<Page>Buttons</Page>", LoadResource(uri));
    }
}
