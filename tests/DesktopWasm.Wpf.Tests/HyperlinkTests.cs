using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery HyperlinkPage/ClipboardPage slice: Hyperlink activation must raise
/// Click then RequestNavigate with the right route/event semantics, and an
/// in-session host (a Frame swap here) performs the navigation. Pointer and
/// keyboard dispatch run through process-wide input statics, so this joins
/// the serialized ApplicationState collection like the other interaction tests.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class HyperlinkTests
{
    private static readonly WasmWindowHandle Handle = new(1);

    private static (WasmInputRouter Router, WpfInputManager Manager, Window Window, TextBlock Text, Hyperlink Link) Harness()
    {
        var text = new TextBlock { Width = 400, Height = 60 };
        var link = new Hyperlink { NavigateUri = new Uri("https://www.microsoft.com"), TargetName = "self" };
        link.Text = "Microsoft";
        text.AddInline(link);
        var window = new Window { Content = text };
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        manager.AddRoot(window);
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));
        return (router, manager, window, text, link);
    }

    private static WpfPoint LinkCenter(TextBlock text, Hyperlink link)
    {
        var rect = text.GetLinkRect(link);
        Assert.NotNull(rect);
        return new WpfPoint(rect.Value.X + rect.Value.Width / 2, rect.Value.Y + rect.Value.Height / 2);
    }

    private static void Down(WasmInputRouter router, WpfPoint p) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, p.X, p.Y, WasmPointerButton.Left, true));

    private static void Up(WasmInputRouter router, WpfPoint p) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, p.X, p.Y, WasmPointerButton.Left, false));

    [Fact]
    public void DoClick_raises_click_then_request_navigate_in_order()
    {
        var (_, _, _, _, link) = Harness();
        var order = new List<string>();
        RequestNavigateEventArgs? navigate = null;
        object? clickSender = null;
        link.Click += (s, e) =>
        {
            order.Add("click");
            clickSender = s;
            Assert.Same(Hyperlink.ClickEvent, e.RoutedEvent);
            Assert.Same(link, e.Source);
        };
        link.RequestNavigate += (s, e) => { order.Add("navigate"); navigate = e; };

        link.DoClick();

        Assert.Equal(["click", "navigate"], order);
        Assert.Same(link, clickSender);
        Assert.NotNull(navigate);
        Assert.Same(link, navigate.Source);
        Assert.Same(Hyperlink.RequestNavigateEvent, navigate.RoutedEvent);
        Assert.Equal(new Uri("https://www.microsoft.com"), navigate.Uri);
        Assert.Equal("self", navigate.Target);
        Assert.False(navigate.Handled);
    }

    [Fact]
    public void DoClick_without_navigate_uri_raises_click_only()
    {
        var link = new Hyperlink { Text = "Plain" };
        var clicks = 0;
        var navigates = 0;
        link.Click += (_, _) => clicks++;
        link.RequestNavigate += (_, _) => navigates++;

        link.DoClick();

        Assert.Equal(1, clicks);
        Assert.Equal(0, navigates);
    }

    [Fact]
    public void Handled_request_stops_routed_delivery()
    {
        var (_, _, window, _, link) = Harness();
        var routed = 0;
        window.AddHandler(Hyperlink.RequestNavigateEvent,
            new RequestNavigateEventHandler((_, _) => routed++));
        link.RequestNavigate += (_, e) => e.Handled = true;

        link.DoClick();

        Assert.Equal(0, routed);
    }

    [Fact]
    public void RequestNavigate_bubbles_to_ancestor_handlers()
    {
        var (_, _, window, _, link) = Harness();
        var seen = new List<RequestNavigateEventArgs>();
        window.AddHandler(Hyperlink.RequestNavigateEvent,
            new RequestNavigateEventHandler((s, e) => seen.Add(e)));

        link.DoClick();

        var args = Assert.Single(seen);
        Assert.Equal(new Uri("https://www.microsoft.com"), args.Uri);
        Assert.Same(link, args.Source);
        Assert.Same(link, args.OriginalSource);
    }

    [Fact]
    public void Disabled_link_does_nothing()
    {
        var (_, _, _, _, link) = Harness();
        var fires = 0;
        link.Click += (_, _) => fires++;
        link.RequestNavigate += (_, _) => fires++;
        link.IsEnabled = false;

        link.DoClick();

        Assert.Equal(0, fires);
    }

    [Fact]
    public void Keyboard_enter_and_space_activate_only_when_focused()
    {
        var (router, manager, _, _, link) = Harness();
        var fires = 0;
        link.RequestNavigate += (_, _) => fires++;

        router.DispatchKeyboard(new WasmKeyArgs(Handle, "Enter", true, false, false, false));
        Assert.Equal(0, fires);

        Assert.True(manager.TryFocus(link));
        router.DispatchKeyboard(new WasmKeyArgs(Handle, "Enter", true, false, false, false));
        Assert.Equal(1, fires);
        router.DispatchKeyboard(new WasmKeyArgs(Handle, " ", true, false, false, false));
        Assert.Equal(2, fires);
        router.DispatchKeyboard(new WasmKeyArgs(Handle, "Enter", false, false, false, false));
        Assert.Equal(2, fires);
    }

    [Fact]
    public void Pointer_press_release_inside_activates_release_outside_does_not()
    {
        var (router, _, window, text, link) = Harness();
        var fires = 0;
        link.RequestNavigate += (_, _) => fires++;
        var center = LinkCenter(text, link);
        var outside = new WpfPoint(center.X + 150, center.Y + 20);

        Down(router, center);
        Up(router, center);
        Assert.Equal(1, fires);
        Assert.True(link.IsKeyboardFocused);

        Down(router, center);
        Up(router, outside);
        Assert.Equal(1, fires);

        Down(router, outside);
        Up(router, center);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void In_session_host_navigates_on_request_with_content_rendered()
    {
        var (_, _, _, _, link) = Harness();
        var frame = new Frame();
        var rendered = 0;
        frame.ContentRendered += (_, _) => rendered++;
        var page = new Page { Title = "Target" };
        // Gallery-shaped subscriber: handle the request and navigate the
        // session host instead of launching externally.
        link.RequestNavigate += (_, e) =>
        {
            frame.Navigate(page);
            e.Handled = true;
        };

        link.DoClick();

        Assert.Same(page, frame.Content);
        Assert.Equal(1, rendered);
    }

    [Fact]
    public void TextBlock_hosts_link_inline_with_rect_and_hit()
    {
        var (_, _, window, text, link) = Harness();

        Assert.Equal("Microsoft", text.Text);
        Assert.Same(link, Assert.Single(text.Inlines));
        Assert.Same(text, link.Parent);

        var rect = text.GetLinkRect(link);
        Assert.NotNull(rect);
        Assert.True(rect.Value.Width > 0);
        Assert.True(rect.Value.Height > 0);
        var center = LinkCenter(text, link);
        Assert.True(link.HitTest(center));
        Assert.False(link.HitTest(new WpfPoint(center.X + 200, center.Y)));
        Assert.False(link.HitTest(new WpfPoint(center.X, center.Y + 200)));

        link.Visibility = Visibility.Hidden;
        Assert.False(link.HitTest(center));
    }

    [Fact]
    public void Link_paints_blue_text_with_underline()
    {
        var (_, _, window, text, link) = Harness();
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));

        var linkTexts = commands.OfType<DrawCommand.DrawText>()
            .Where(t => t.Rgba == TextBlock.LinkRgba && t.Text == "Microsoft")
            .ToList();
        Assert.NotEmpty(linkTexts);
        var underlines = commands.OfType<DrawCommand.DrawLine>()
            .Where(l => l.Rgba == TextBlock.LinkRgba)
            .ToList();
        Assert.NotEmpty(underlines);
        var rect = text.GetLinkRect(link);
        Assert.NotNull(rect);
        var ink = linkTexts[0];
        Assert.Equal(rect.Value.X, ink.X, precision: 1);
    }

    [Fact]
    public void Focused_link_paints_a_focus_ring()
    {
        var (_, manager, window, _, link) = Harness();
        Assert.True(manager.TryFocus(link));
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));

        Assert.Contains(commands.OfType<DrawCommand.FillRect>(), r => r.Rgba == 0x285B45FF);
    }

    [Fact]
    public void Span_text_edits_refold_and_text_set_replaces_inlines()
    {
        var text = new TextBlock();
        var first = new Run { Text = "Hello " };
        var link = new Hyperlink();
        link.Text = "there";
        text.AddInline(first);
        text.AddInline(link);
        Assert.Equal("Hello there", text.Text);

        link.Text = "world";
        Assert.Equal("Hello world", text.Text);

        text.Text = "flat";
        Assert.Equal("flat", text.Text);
        Assert.Empty(text.Inlines);
    }

    [Fact]
    public void Loader_parses_gallery_hyperlink_shape_with_wires()
    {
        var result = XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <TextBlock Margin="20">
                <Hyperlink NavigateUri="https://www.microsoft.com" RequestNavigate="Hyperlink_RequestNavigate">
                  Microsoft
                </Hyperlink>
              </TextBlock>
            </Window>
            """);
        var text = Assert.IsType<TextBlock>(Assert.IsType<Window>(result.Root).Content);
        Assert.Equal("Microsoft", text.Text);
        var link = Assert.IsType<Hyperlink>(Assert.Single(text.Inlines));
        Assert.Equal(new Uri("https://www.microsoft.com"), link.NavigateUri);
        var wire = Assert.Single(result.EventWires);
        Assert.Same(link, wire.Element);
        Assert.Equal("RequestNavigate", wire.EventName);
        Assert.Equal("Hyperlink_RequestNavigate", wire.HandlerName);
    }

    [Fact]
    public void Loader_parses_hyperlink_click_and_rejects_bad_uri()
    {
        var result = XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <TextBlock>
                <Hyperlink Click="Open_Page">here</Hyperlink>
              </TextBlock>
            </Window>
            """);
        var link = Assert.IsType<Hyperlink>(
            Assert.Single(Assert.IsType<TextBlock>(Assert.IsType<Window>(result.Root).Content).Inlines));
        var wire = Assert.Single(result.EventWires);
        Assert.Equal("Click", wire.EventName);

        // An unclosed IPv6 literal parses as neither an absolute nor a relative URI.
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <TextBlock>
                <Hyperlink NavigateUri="http://[::1">here</Hyperlink>
              </TextBlock>
            </Window>
            """));
    }

    [Fact]
    public void Span_and_run_keep_folding_without_activation()
    {
        var result = XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <TextBlock><Run>One </Run><Span>two</Span><LineBreak />three</TextBlock>
            </Window>
            """);
        var text = Assert.IsType<TextBlock>(Assert.IsType<Window>(result.Root).Content);
        // Inline text is trimmed by the loader (legacy fold behavior).
        Assert.Equal("Onetwo\nthree", text.Text);
        Assert.Equal(3, text.Inlines.Count);
        Assert.DoesNotContain(text.Inlines, s => s is Hyperlink);
    }
}
