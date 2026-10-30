using System.Text;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// BAML reader v1 tests. Synthetic streams use the exact MS-BAML framing
/// validated against real staged files (15-byte header trailer, sized records
/// with inclusive sizes, negative well-known ids); the real 1222-byte
/// artifact itself is verified locally, never vendored.
/// </summary>
[ResetAmbientState]
public sealed class BamlReaderTests
{
    // Well-known ids (negated framework known-table indices).
    private const short TWindow = -739;
    private const short TGrid = -254;
    private const short TTextBlock = -638;
    private const short MContent = -14;
    private const short MHeight = -47;
    private const short MHAlign = -48;
    private const short MVAlign = -56;
    private const short MWidth = -57;
    private const short MChildren = -213;
    private const short CString = -615;
    private const short CLength = -348;
    private const short CEnum = -195;
    private const short CImage = -286;

    private sealed class B
    {
        private readonly List<byte> _b = new();
        public B Raw(params byte[] b) { _b.AddRange(b); return this; }
        public B U16(ushort v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B I16(short v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B U32(uint v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
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
            // Size includes its own byte(s); all test records stay < 128.
            if (p.Count + 1 >= 128) throw new InvalidOperationException("test record too large");
            _b.Add(type);
            _b.Add((byte)(p.Count + 1));
            _b.AddRange(p);
            return this;
        }
        public byte[] ToArray() => _b.ToArray();
    }

    // The real 15-byte version trailer (byte-identical in every real staged
    // file read so far across 8 packages; the old 14-zero-byte filler never
    // matched a real file).
    private static byte[] Header() => new B()
        .Raw(0x0C, 0x00, 0x00, 0x00)
        .Raw(0x4D, 0x00, 0x53, 0x00, 0x42, 0x00, 0x41, 0x00, 0x4D, 0x00, 0x4C, 0x00)
        .U16(0).U16(0x60)
        .Raw(0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00,
             0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00)
        .ToArray();

    private static B Doc(B b) => b.Raw(0x01); // DocumentStart

    private static Window Load(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return XamlLoader.LoadComponent<Window>(stream);
    }

    private static byte[] HelloShaped(Action<B>? tweak = null)
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00); // ElementStart(MainWindow)
        b.Sized(0x1F, p => p.U16(0).I16(-739).Raw(0x00).Str("Title"));
        b.Sized(0x24, p => p.I16(0).Str("MainWindow").I16(CString));
        b.Raw(0x35).U32(8).U32(9);
        b.Sized(0x24, p => p.I16(MHeight).Str("350").I16(CLength));
        tweak?.Invoke(b);
        return b.ToArray();
    }

    [Fact]
    public void Baml_window_with_title_and_height_loads()
    {
        var bytes = HelloShaped();
        // Minimal well-formed doc: close root + end.
        var full = bytes.Concat(new byte[] { 0x04, 0x02 }).ToArray();
        var window = Load(full);
        Assert.Equal("MainWindow", window.Title);
        Assert.Equal(350, window.Height);
    }

    [Fact]
    public void Baml_grid_textblock_nesting_and_text_alignments_load()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Sized(0x24, p => p.I16(MWidth).Str("525").I16(CLength));
        b.Raw(0x2E).I16(MContent); // Content = Grid
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren); // Grid.Children
        b.Raw(0x03).I16(TTextBlock).Raw(0x00);
        b.Sized(0x24, p => p.I16(MVAlign).Str("Center").I16(CEnum));
        b.Sized(0x24, p => p.I16(MHAlign).Str("Center").I16(CEnum));
        b.Sized(0x10, p => p.Str("Hi"));
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);

        var window = Load(b.ToArray());
        var grid = Assert.IsType<Grid>(window.Content);
        var text = Assert.IsType<TextBlock>(Assert.Single(grid.LogicalChildren));
        Assert.Equal("Hi", text.Text);
        Assert.Equal(HorizontalAlignment.Center, text.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, text.VerticalAlignment);
        Assert.Equal(525, window.Width);
    }

    [Fact]
    public void Baml_icon_is_parsed_but_unrendered()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("Foo.Bar"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Sized(0x1F, p => p.U16(1).I16(TWindow).Raw(0x00).Str("Icon"));
        b.Sized(0x24, p => p.I16(1).Str("app.ico").I16(CImage));
        b.Raw(0x04).Raw(0x02);

        var window = Load(b.ToArray());
        Assert.Equal("app.ico", window.IconSource);
    }

    [Fact]
    public void Baml_root_custom_type_maps_to_window()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("Foo.Bar"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x04).Raw(0x02);

        Assert.IsType<Window>(Load(b.ToArray()));
    }

    [Fact]
    public void Baml_bad_signature_rejected()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("MZ not baml...."));
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("neither loose XAML nor MS BAML", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baml_unknown_record_rejected_with_offset()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x05, p => p.Raw(0x00)); // Property: not in v1
        b.Raw(0x02);
        using var stream = new MemoryStream(b.ToArray());
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("0x05", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baml_unknown_wellknown_type_rejected()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Raw(0x03).I16(-999).Raw(0x00);
        using var stream = new MemoryStream(b.ToArray());
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("-999", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baml_text_outside_textblock_rejected()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("Foo.Bar"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Sized(0x10, p => p.Str("oops"));
        using var stream = new MemoryStream(b.ToArray());
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("TextBlock", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baml_non_window_root_rejected()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x04).Raw(0x02);
        using var stream = new MemoryStream(b.ToArray());
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("Window", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baml_wrong_converter_rejected()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("Foo.Bar"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Sized(0x24, p => p.I16(MHeight).Str("350").I16(CString)); // String on a double
        using var stream = new MemoryStream(b.ToArray());
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("-47", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baml_nested_custom_type_rejected()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("Foo.Bar"));
        b.Sized(0x1D, p => p.U16(1).U16(0).Str("Foo.Baz"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(1).Raw(0x00);
        using var stream = new MemoryStream(b.ToArray());
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("Foo.Baz", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baml_wrong_root_type_request_rejected()
    {
        using var stream = new MemoryStream(HelloShaped().Concat(new byte[] { 0x04, 0x02 }).ToArray());
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Grid>(stream));
        Assert.Contains("Grid", ex.Message, StringComparison.Ordinal);
    }
}
