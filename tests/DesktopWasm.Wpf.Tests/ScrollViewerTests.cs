using System.Text;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>ScrollViewer v1 tests: layout/extent, wheel, keyboard, thumb drag,
/// disabled gating, offset-shifted hit-testing, XAML and synthetic BAML.</summary>
[ResetAmbientState]
public sealed class ScrollViewerTests
{
    private static (WasmInputRouter Router, WpfInputManager Manager, ScrollViewer View) Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var view = new ScrollViewer { Width = 400, Height = 100 };
        var panel = new StackPanel();
        for (var i = 0; i < 10; i++) panel.AddChild(new Button { Content = $"B{i}" });
        view.Content = panel;
        root.Content = view;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 100));
        mil.Render(root, new WpfSize(400, 100), new WpfRect(0, 0, 400, 100));
        return (router, manager, view);
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static void Wheel(WasmInputRouter router, double x, double y, double deltaY) =>
        router.DispatchWheel(new WasmWheelArgs(Handle, x, y, deltaY));

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(Handle, key, true, false, false, false));

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static void Move(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true)
        { Kind = WasmPointerKind.Move });

    private static void Up(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, false));

    [Fact]
    public void Layout_reports_extent_viewport_and_clamps_offset()
    {
        var (_, _, view) = Harness();
        // 10 buttons x 32px; viewport is 100 - 2px border.
        Assert.Equal(320, view.ExtentHeight);
        Assert.Equal(98, view.ViewportHeight);
        Assert.Equal(222, view.ScrollableHeight);

        view.VerticalOffset = 500;
        Assert.Equal(222, view.VerticalOffset);
        view.VerticalOffset = -5;
        Assert.Equal(0, view.VerticalOffset);
    }

    [Fact]
    public void Wheel_scrolls_content_under_pointer()
    {
        var (router, _, view) = Harness();
        Wheel(router, 50, 50, 60);
        Assert.Equal(60, view.VerticalOffset);
        Wheel(router, 50, 50, 1000);
        Assert.Equal(222, view.VerticalOffset);
    }

    [Fact]
    public void Wheel_ignored_when_disabled()
    {
        var (router, _, view) = Harness();
        view.IsEnabled = false;
        Wheel(router, 50, 50, 60);
        Assert.Equal(0, view.VerticalOffset);
    }

    [Fact]
    public void Keyboard_scrolls_only_when_focused()
    {
        var (router, manager, view) = Harness();
        Key(router, "ArrowDown");
        Assert.Equal(0, view.VerticalOffset); // no focus yet

        Assert.True(manager.TryFocus(view));
        Key(router, "ArrowDown");
        Assert.Equal(16, view.VerticalOffset);
        Key(router, "ArrowUp");
        Assert.Equal(0, view.VerticalOffset);
        Key(router, "PageDown");
        Assert.Equal(98, view.VerticalOffset);
        Key(router, "PageUp");
        Assert.Equal(0, view.VerticalOffset);
        Key(router, "End");
        Assert.Equal(222, view.VerticalOffset);
        Key(router, "Home");
        Assert.Equal(0, view.VerticalOffset);
    }

    [Fact]
    public void Thumb_drag_moves_offset_and_release_stops_tracking()
    {
        var (router, _, view) = Harness();
        var changes = 0;
        view.ScrollChanged += (_, _) => changes++;

        // Thumb spans x 387..399, y 1..33 while offset is 0.
        Down(router, 390, 5);
        Move(router, 390, 50);
        // Grab 4px into the thumb; viewport 98, extent 320 (10 x 32px buttons).
        var thumbH = 98 * 98.0 / 320;
        Assert.Equal(45.0 / (98 - thumbH) * 222, view.VerticalOffset, 6);
        Assert.True(changes > 0);

        Up(router, 390, 50);
        var frozen = view.VerticalOffset;
        Move(router, 390, 90);
        Assert.Equal(frozen, view.VerticalOffset);
    }

    [Fact]
    public void Hit_testing_follows_scrolled_content()
    {
        var (_, manager, view) = Harness();
        Assert.Equal("B0", ((Button)manager.HitTest(50, 10)!).Content);
        view.VerticalOffset = 100; // B3 (96..128) now sits at viewport y -3..29
        Assert.Equal("B3", ((Button)manager.HitTest(50, 10)!).Content);
    }

    [Fact]
    public void Xaml_parses_child_and_initial_offset()
    {
        var result = XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <ScrollViewer VerticalOffset="0">
                <StackPanel>
                  <TextBlock Text="Hi"/>
                </StackPanel>
              </ScrollViewer>
            </Window>
            """);
        var window = Assert.IsType<Window>(result.Root);
        var view = Assert.IsType<ScrollViewer>(window.Content);
        Assert.IsType<StackPanel>(view.Content);
    }

    [Fact]
    public void Xaml_parses_padding_and_insets_content()
    {
        // WPFGallery sample-page shape: Padding on ScrollViewer (a
        // ContentControl property in WPF) used to fail the whole page load.
        var result = XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <ScrollViewer Padding="0,0,24,0">
                <StackPanel>
                  <TextBlock Text="Hi"/>
                </StackPanel>
              </ScrollViewer>
            </Window>
            """);
        var view = Assert.IsType<ScrollViewer>(Assert.IsType<Window>(result.Root).Content);
        Assert.Equal(new Thickness(0, 0, 24, 0), view.Padding);

        var window = new Window { Width = 400, Height = 100 };
        window.Content = view;
        window.Measure(new WpfSize(400, 100));
        window.Arrange(new WpfRect(0, 0, 400, 100));
        var content = Assert.IsType<StackPanel>(view.Content);
        // 1px border + 24px right padding narrow the content band.
        Assert.Equal(400 - 2 - 12 - 24, content.ArrangedRect.Width, precision: 5);
        Assert.Equal(1, content.ArrangedRect.X, precision: 5);
    }

    [Fact]
    public void Xaml_second_child_is_rejected()
    {
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <ScrollViewer>
                <TextBlock Text="A"/>
                <TextBlock Text="B"/>
              </ScrollViewer>
            </Window>
            """));
    }

    // Minimal synthetic-MS-BAML builder (same framing as BamlReaderTests).
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
    public void Baml_scrollviewer_with_textblock_child_loads()
    {
        const short tScrollViewer = -550; // verified against a real ScrollViewer BAML fixture
        const short tTextBlock = -638;
        const short mContent = -14;
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(mContent);
        b.Raw(0x03).I16(tScrollViewer).Raw(0x00);
        b.Raw(0x2E).I16(mContent);
        b.Raw(0x03).I16(tTextBlock).Raw(0x00);
        b.Sized(0x10, p => p.Str("Hi"));
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);

        using var stream = new MemoryStream(b.ToArray());
        var window = XamlLoader.LoadComponent<Window>(stream);
        var view = Assert.IsType<ScrollViewer>(window.Content);
        Assert.Equal("Hi", Assert.IsType<TextBlock>(view.Content).Text);
    }

    [Fact]
    public void Baml_textblock_text_member_on_textbox_errors_with_id()
    {
        const short tTextBox = -639;
        const short mTextBlockText = -110;
        const short cString = -615;
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x03).I16(tTextBox).Raw(0x00);
        b.Sized(0x24, p => p.I16(mTextBlockText).Str("Hi").I16(cString));
        b.Raw(0x04).Raw(0x04).Raw(0x02);

        using var stream = new MemoryStream(b.ToArray());
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("-110", ex.Message);
    }
}
