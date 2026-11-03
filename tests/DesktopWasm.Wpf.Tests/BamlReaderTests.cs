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
    private const short CBool = -46;

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

    private static byte[] RoundingShaped(string value, short conv)
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00); // ElementStart(MainWindow)
        b.Sized(0x1F, p => p.U16(0).I16(-694).Raw(0x00).Str("UseLayoutRounding"));
        b.Sized(0x24, p => p.I16(0).Str(value).I16(conv));
        b.Raw(0x04, 0x02);
        return b.ToArray();
    }

    [Fact]
    public void Baml_window_with_layout_rounding_lands()
    {
        var window = Load(RoundingShaped("True", CBool));
        Assert.True(window.UseLayoutRounding);
        var plain = Load(RoundingShaped("False", CBool));
        Assert.False(plain.UseLayoutRounding);
    }

    [Fact]
    public void Baml_window_rejects_bad_layout_rounding_value()
    {
        var error = Assert.Throws<XamlLoadException>(() => Load(RoundingShaped("Sometimes", CBool)));
        Assert.Contains("UseLayoutRounding", error.Message);
        Assert.Contains("boolean", error.Message);
    }

    [Fact]
    public void Baml_window_rejects_layout_rounding_wrong_converter()
    {
        var error = Assert.Throws<XamlLoadException>(() => Load(RoundingShaped("True", CString)));
        Assert.Contains("UseLayoutRounding", error.Message);
        Assert.Contains("unsupported BAML attribute", error.Message);
    }

    private static byte[] PerFileNamedShaped(string value, short conv)
    {
        // Per-file AttributeInfo Name row (TestApp layoutRoot/mainMenu shape:
        // owner id + StringConverter PWC), applied straight to the root.
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Sized(0x1F, p => p.U16(0).I16(-739).Raw(0x00).Str("Name"));
        b.Raw(0x03).I16(0).Raw(0x00); // ElementStart(MainWindow)
        b.Sized(0x24, p => p.I16(0).Str(value).I16(conv));
        b.Raw(0x04, 0x02);
        return b.ToArray();
    }

    [Fact]
    public void Baml_window_perfile_name_lands()
    {
        var window = Load(PerFileNamedShaped("Root", CString));
        Assert.Equal("Root", window.Name);
    }

    [Fact]
    public void Baml_window_rejects_name_wrong_converter()
    {
        var error = Assert.Throws<XamlLoadException>(() => Load(PerFileNamedShaped("Root", CBool)));
        Assert.Contains("Name", error.Message);
    }

    private static byte[] GridRowsShaped(string height)
    {
        // <Window><Grid><Grid.RowDefinitions><RowDefinition Height="..."/>
        // in real record shapes (TestApp layoutRoot at mainwindow.baml
        // 0x961: list 0x0B, definition -541, Height PWC/-256).
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Sized(0x1F, p => p.U16(3).I16(-254).Raw(0x00).Str("RowDefinitions"));
        b.Raw(0x03).I16(0).Raw(0x00); // ElementStart(MainWindow)
        b.Raw(0x2E).I16(-14); // SetContentMember(Content)
        b.Raw(0x03).I16(-254).Raw(0x00); // ElementStart(Grid)
        b.Raw(0x0B).U16(3); // PropertyIListStart(RowDefinitions)
        b.Raw(0x03).I16(-541).Raw(0x00); // ElementStart(RowDefinition)
        b.Sized(0x24, p => p.I16(-94).Str(height).I16(-256));
        b.Raw(0x04); // ElementEnd(RowDefinition)
        b.Raw(0x0C); // PropertyIListEnd
        b.Raw(0x04, 0x04, 0x02);
        return b.ToArray();
    }

    [Fact]
    public void Baml_grid_row_definitions_land()
    {
        var window = Load(GridRowsShaped("Auto"));
        var grid = Assert.IsType<Grid>(window.Content);
        var row = Assert.Single(grid.RowDefinitions);
        Assert.Equal(GridLength.Auto, row.Height);
    }

    [Fact]
    public void Baml_grid_rejects_bad_row_height()
    {
        var error = Assert.Throws<XamlLoadException>(() => Load(GridRowsShaped("tall")));
        Assert.Contains("RowDefinition.Height", error.Message);
    }

    private const short CInt = -315;

    private static byte[] GridRowShaped(string value)
    {
        // Attached Grid.Row PWC in real record shape (TestApp mainMenu at
        // mainwindow.baml 0x9BF: well-known member -63, Int32Converter).
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00); // ElementStart(MainWindow)
        b.Raw(0x2E).I16(-14); // SetContentMember(Content)
        b.Raw(0x03).I16(-638).Raw(0x00); // ElementStart(TextBlock)
        b.Sized(0x24, p => p.I16(-63).Str(value).I16(CInt));
        b.Raw(0x04, 0x04, 0x02);
        return b.ToArray();
    }

    [Fact]
    public void Baml_grid_row_attaches()
    {
        var window = Load(GridRowShaped("1"));
        var text = Assert.IsType<TextBlock>(window.Content);
        Assert.Equal(1, Grid.GetRow(text));
    }

    [Fact]
    public void Baml_grid_rejects_bad_row()
    {
        var error = Assert.Throws<XamlLoadException>(() => Load(GridRowShaped("one")));
        Assert.Contains("Grid.Row", error.Message);
    }

    private static byte[] BackgroundBindingShaped()
    {
        // <Window><Window.Background><Binding Path ctor "Background"
        // ElementName="dockManager" /></Window.Background></Window> in real
        // record shapes (TestApp mainwindow.baml at 0x8FB: complex member
        // -24, well-known Binding -20, ctor text, ElementName PWC).
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Sized(0x1F, p => p.U16(0).I16(-20).Raw(0x00).Str("ElementName"));
        b.Raw(0x03).I16(0).Raw(0x00); // ElementStart(MainWindow)
        b.Raw(0x07).I16(-24); // PropertyComplexStart(Background)
        b.Raw(0x03).I16(-20).Raw(0x00); // ElementStart(Binding)
        b.Raw(0x2A); // ConstructorParametersStart
        b.Sized(0x10, p => p.Str("Background")); // ctor path text
        b.Raw(0x2B); // ConstructorParametersEnd
        b.Sized(0x24, p => p.I16(0).Str("dockManager").I16(CString));
        b.Raw(0x04); // ElementEnd(Binding): attaches to Background
        b.Raw(0x08); // PropertyComplexEnd
        b.Raw(0x04, 0x02);
        return b.ToArray();
    }

    [Fact]
    public void Baml_window_background_binding_attaches()
    {
        var window = Load(BackgroundBindingShaped());
        var binding = Assert.IsType<Binding>(
            BindingOperations.GetBindingBase(window, Control.BackgroundProperty));
        Assert.Equal("Background", binding.Path.Path);
        Assert.Equal("dockManager", binding.ElementName);
    }

    [Fact]
    public void Baml_complex_end_without_start_rejected()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x08);
        var error = Assert.Throws<XamlLoadException>(() => Load(b.ToArray()));
        Assert.Contains("unbalanced PropertyComplexEnd", error.Message);
    }

    [Fact]
    public void Baml_element_end_closes_complex_rejected()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x07).I16(-24);
        b.Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(() => Load(b.ToArray()));
        Assert.Contains("closes a complex property", error.Message);
    }

    [Fact]
    public void Baml_complex_unsupported_member_rejected()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Sized(0x1F, p => p.U16(0).I16(-20).Raw(0x00).Str("ElementName"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x07).I16(-57); // Width: not a routed complex member
        b.Raw(0x03).I16(-20).Raw(0x00);
        b.Raw(0x2A);
        b.Sized(0x10, p => p.Str("Background"));
        b.Raw(0x2B);
        b.Sized(0x24, p => p.I16(0).Str("dockManager").I16(CString));
        b.Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(() => Load(b.ToArray()));
        Assert.Contains("complex property member -57", error.Message);
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

    private static byte[] InlineDictShaped(bool withEntry, ushort member = 0, bool secondBody = false, bool keyless = false)
    {
        // <Window><Grid><Grid.Resources><ResourceDictionary>
        //   <Deferable><key "pad"/><UInt32>7</UInt32></Deferable>
        // </ResourceDictionary></Grid.Resources></Grid></Window> in the real
        // TestApp mainwindow.baml 0xA02 framing (0x0D carries the member id,
        // entries ride an inline deferable index, the nested dictionary body
        // is transparent, 0x0E closes).
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Sized(0x1F, p => p.U16(0).I16(-226).Raw(0x00).Str("Resources"));
        b.Sized(0x1F, p => p.U16(1).I16(-226).Raw(0x00).Str("Width"));
        b.Sized(0x20, p => p.U16(0).Str("pad"));
        b.Raw(0x03).I16(0).Raw(0x00); // ElementStart(MainWindow)
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00); // ElementStart(Grid)
        b.Raw(0x0D).U16(member); // PropertyDictionaryStart
        if (withEntry || keyless || secondBody)
        {
            b.Raw(0x03).I16(-524).Raw(0x02); // nested ResourceDictionary
            if (secondBody)
            {
                b.Raw(0x03).I16(-524).Raw(0x02); // illegal second body
            }
            else
            {
                var inner = new B();
                if (!keyless)
                    inner.Sized(0x26, p => p.U16(0).U32(0).Raw(0x00, 0x00)); // key "pad"
                inner.Raw(0x03).I16(-697).Raw(0x00); // UInt32 value
                inner.Sized(0x11, p => p.Str("7").I16(-698));
                inner.Raw(0x04);
                var body = inner.ToArray();
                b.Raw(0x25).U32((uint)body.Length);
                b.Raw(body);
            }
            b.Raw(0x04); // nested ResourceDictionary end (deferable landing)
        }
        b.Raw(0x0E);
        b.Raw(0x04, 0x04, 0x02);
        return b.ToArray();
    }

    [Fact]
    public void Baml_inline_empty_dictionary_attaches()
    {
        var window = Load(InlineDictShaped(false));
        var grid = Assert.IsType<Grid>(window.Content);
        Assert.False(grid.Resources.Contains("pad"));
    }

    [Fact]
    public void Baml_inline_string_keyed_entry_lands()
    {
        var window = Load(InlineDictShaped(true));
        var grid = Assert.IsType<Grid>(window.Content);
        Assert.Equal(7u, grid.Resources["pad"]);
    }

    [Fact]
    public void Baml_inline_dictionary_rejects_non_resources_member()
    {
        var ex = Assert.Throws<XamlLoadException>(() => Load(InlineDictShaped(false, member: 1)));
        Assert.Contains("Width", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baml_inline_dictionary_rejects_keyless_value()
    {
        var ex = Assert.Throws<XamlLoadException>(() => Load(InlineDictShaped(false, keyless: true)));
        Assert.Contains("keyless", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Baml_inline_dictionary_rejects_truncated_start()
    {
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("Probe.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x0D).Raw(0x00); // dictionary start truncated mid-member
        var ex = Assert.Throws<XamlLoadException>(() => Load(b.ToArray()));
        Assert.Contains("truncated BAML dictionary start", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baml_inline_dictionary_rejects_second_body()
    {
        var ex = Assert.Throws<XamlLoadException>(() => Load(InlineDictShaped(false, secondBody: true)));
        Assert.Contains("second ResourceDictionary body", ex.Message, StringComparison.Ordinal);
    }

    private static byte[] InlineStyleShaped(bool withTrigger = false, bool shortForm = false)
    {
        // <Window><Grid><Grid.Resources><ResourceDictionary>
        //   <Deferable><key {x:Type MenuItem}/><Style TargetType="MenuItem"/>
        // </Deferable></ResourceDictionary></Grid.Resources></Grid></Window>:
        // the real TestApp mainwindow.baml 0xA61 implicit-style entry shape
        // (nested body, inline deferable index, DefAttributeKeyType MenuItem
        // key, TargetType PropertyTypeReference, immediate style close).
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Sized(0x1F, p => p.U16(0).I16(-226).Raw(0x00).Str("Resources"));
        b.Sized(0x1F, p => p.U16(1).I16(-620).Raw(0x00).Str("TargetType"));
        b.Sized(0x1F, p => p.U16(2).I16(-620).Raw(0x00).Str("Triggers"));
        b.Sized(0x1F, p => p.U16(3).I16(-688).Raw(0x00).Str("Property"));
        b.Sized(0x1F, p => p.U16(4).I16(-688).Raw(0x00).Str("Value"));
        b.Raw(0x03).I16(0).Raw(0x00); // ElementStart(MainWindow)
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00); // ElementStart(Grid)
        b.Raw(0x0D).U16(0); // PropertyDictionaryStart(Resources)
        b.Raw(0x03).I16(-524).Raw(0x02); // nested ResourceDictionary
        var inner = new B();
        inner.Raw(0x27).I16(-403).Raw(0x00).U32(0).Raw(0x00, 0x00); // key {x:Type MenuItem}
        inner.Raw(0x03).I16(-620).Raw(0x00); // ElementStart(Style)
        inner.Raw(0x22).I16(1).I16(-403); // TargetType = MenuItem
        if (withTrigger)
        {
            inner.Raw(0x0B).U16(2); // Style.Triggers
            inner.Raw(0x03).I16(-688).Raw(0x00); // Trigger
            if (shortForm)
                inner.Sized(0x06, p => p.I16(3).U16(137).I16(-32)); // Property = Foreground (short form, TestApp mainwindow.baml 0xAAC shape)
            else
                inner.Sized(0x06, p => p.I16(3).U16(16521).I16(-403).Str("Icon")); // Property = MenuItem.Icon
            inner.Sized(0x24, p => p.I16(4).Str("x").I16(-195)); // Value (kept raw)
            inner.Raw(0x04); // trigger end
            inner.Raw(0x0C); // triggers end
        }
        inner.Raw(0x04); // style end
        var body = inner.ToArray();
        b.Raw(0x25).U32((uint)body.Length);
        b.Raw(body);
        b.Raw(0x04); // nested ResourceDictionary end (deferable landing)
        b.Raw(0x0E);
        b.Raw(0x04, 0x04, 0x02);
        return b.ToArray();
    }

    [Fact]
    public void Baml_inline_implicit_menuitem_style_lands()
    {
        var window = Load(InlineStyleShaped());
        var grid = Assert.IsType<Grid>(window.Content);
        var style = Assert.IsType<Style>(grid.Resources[typeof(MenuItem)]);
        Assert.Equal(typeof(MenuItem), style.TargetType);
    }

    [Fact]
    public void Baml_inline_menuitem_trigger_property_lands()
    {
        var window = Load(InlineStyleShaped(withTrigger: true));
        var grid = Assert.IsType<Grid>(window.Content);
        var style = Assert.IsType<Style>(grid.Resources[typeof(MenuItem)]);
        var trigger = Assert.Single(style.Triggers);
        var t = Assert.IsType<Trigger>(trigger);
        Assert.Equal("MenuItem.Icon", t.Property);
    }

    private const short TMenu = -401;
    private const short TMenuItem = -403;
    private const short TSeparator = -554;
    private const short MItems = -192;
    private const short MHeader = -71;

    private static byte[] MenuRowShaped(bool nested, bool badConv, string command = "cmd:ApplicationCommands.Copy")
    {
        // <Window><Menu><MenuItem Header="Edit" Command="cmd:Copy"/>...
        // in the real TestApp mainwindow.baml row shape (well-known
        // Header member -71, per-file MenuItem.Command with the Command
        // converter, prefix:Type.Member text through xmlns tables).
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("Probe.App"));
        b.Sized(0x1C, p => p.U16(1).Str("DesktopWasm.Wpf"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("Probe.MainWindow"));
        b.Sized(0x1F, p => p.U16(0).I16(TMenuItem).Raw(0x00).Str("Command"));
        b.Sized(0x14, p => p.Str("cmd").Str("http://engine/cmd").U16(1).U16(1));
        b.Sized(0x1B, p => p.Str("http://engine/cmd").Str("DesktopWasm.Wpf").U16(1));
        b.Raw(0x03).I16(0).Raw(0x00); // ElementStart(MainWindow)
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TMenu).Raw(0x00); // ElementStart(Menu)
        if (nested)
        {
            b.Raw(0x03).I16(TMenuItem).Raw(0x00); // parent "File"
            b.Sized(0x24, p => p.I16(MHeader).Str("File").I16(CString));
            b.Raw(0x2E).I16(MItems); // MenuItem.Items
        }
        b.Raw(0x03).I16(TMenuItem).Raw(0x00); // row
        b.Sized(0x24, p => p.I16(MHeader).Str(nested ? "New" : "Edit").I16(CString));
        b.Sized(0x24, p => p.I16(0).Str(command).I16(badConv ? CString : (short)-94));
        b.Raw(0x04); // row end
        if (nested) b.Raw(0x04); // parent end
        b.Raw(0x04, 0x04, 0x02);
        return b.ToArray();
    }

    [Fact]
    public void Baml_menu_row_command_and_header_land()
    {
        var window = Load(MenuRowShaped(false, false));
        var menu = Assert.IsType<Menu>(window.Content);
        var row = Assert.Single(menu.Items);
        Assert.Equal("Edit", row.Header);
        Assert.Same(ApplicationCommands.Copy, row.Command);
    }

    [Fact]
    public void Baml_menu_row_nests_children()
    {
        var window = Load(MenuRowShaped(true, false));
        var menu = Assert.IsType<Menu>(window.Content);
        var parent = Assert.Single(menu.Items);
        Assert.Equal("File", parent.Header);
        Assert.Null(parent.Command);
        var child = Assert.Single(parent.Items);
        Assert.Equal("New", child.Header);
        Assert.Same(ApplicationCommands.Copy, child.Command);
    }

    [Fact]
    public void Baml_menu_row_bare_command_name_lands()
    {
        var window = Load(MenuRowShaped(false, false, "Copy"));
        var menu = Assert.IsType<Menu>(window.Content);
        var row = Assert.Single(menu.Items);
        Assert.Same(ApplicationCommands.Copy, row.Command);
    }

    [Fact]
    public void Baml_menu_row_rejects_ambiguous_command()
    {
        // "Delete" lives on ApplicationCommands and EditingCommands alike:
        // the row fails naming both holders instead of binding either.
        var ex = Assert.Throws<XamlLoadException>(() => Load(MenuRowShaped(false, false, "Delete")));
        Assert.Contains("menu row", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ambiguous", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baml_menu_row_rejects_unknown_command()
    {
        var ex = Assert.Throws<XamlLoadException>(() => Load(MenuRowShaped(false, false, "Nope")));
        Assert.Contains("menu row", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'Nope'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("names an unknown command", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baml_menu_separator_row_lands()
    {
        // <Menu><MenuItem Header="Edit"/><Separator/><MenuItem Header="Cut"/>
        // in the real TestApp mainwindow.baml 0xB48 shape (bare element,
        // no attributes, attached through the Items member).
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("Probe.App"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("Probe.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TMenu).Raw(0x00);
        b.Raw(0x2E).I16(MItems);
        b.Raw(0x03).I16(TMenuItem).Raw(0x00);
        b.Sized(0x24, p => p.I16(MHeader).Str("Edit").I16(CString));
        b.Raw(0x04);
        b.Raw(0x03).I16(TSeparator).Raw(0x00);
        b.Raw(0x04);
        b.Raw(0x03).I16(TMenuItem).Raw(0x00);
        b.Sized(0x24, p => p.I16(MHeader).Str("Cut").I16(CString));
        b.Raw(0x04);
        b.Raw(0x04, 0x04, 0x02);
        var window = Load(b.ToArray());
        var menu = Assert.IsType<Menu>(window.Content);
        Assert.Equal(["Edit", "Cut"], menu.Items.Select(i => i.Header));
        Assert.Equal(3, menu.LogicalChildren.Count);
        Assert.Equal("Edit", Assert.IsType<MenuItem>(menu.LogicalChildren[0]).Header);
        Assert.IsType<Separator>(menu.LogicalChildren[1]);
        Assert.Equal("Cut", Assert.IsType<MenuItem>(menu.LogicalChildren[2]).Header);
    }

    [Fact]
    public void Baml_menu_row_rejects_wrong_command_converter()
    {
        var ex = Assert.Throws<XamlLoadException>(() => Load(MenuRowShaped(false, true)));
        Assert.Contains("menu row", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Command converter", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Baml_inline_menuitem_shortform_foreground_trigger_lands()
    {
        var window = Load(InlineStyleShaped(withTrigger: true, shortForm: true));
        var grid = Assert.IsType<Grid>(window.Content);
        var style = Assert.IsType<Style>(grid.Resources[typeof(MenuItem)]);
        var trigger = Assert.Single(style.Triggers);
        var t = Assert.IsType<Trigger>(trigger);
        Assert.Equal("Foreground", t.Property);
    }
}
