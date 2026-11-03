// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Unit tests for nested BAML inline resources, PropertyDictionaryStart scopes,
/// StaticResource resolution across nested visual trees, implicit style application,
/// and DataBinding DeferredConverterKey resolution from ancestor resources.
/// </summary>
[ResetAmbientState]
public sealed class BamlInlineResourcesTests
{
    private const short TWindow = -739;
    private const short TGrid = -254;
    private const short TBorder = -72;
    private const short TTextBlock = -638;
    private const short TStyle = -620;
    private const short TResourceDictionary = -524;
    private const short KnownUInt32 = -697;
    private const short ConvUInt32 = -698;
    private const short KnownColor = -81;
    private const short ConvColor = -87;

    private const short MContent = -14;
    private const short MChildren = -213;

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
        .Raw(0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00,
             0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00)
        .ToArray();

    private static Window Load(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return XamlLoader.LoadComponent<Window>(stream);
    }

    [Fact]
    public void Baml_nested_inline_resources_scopes_resolve_hierarchically()
    {
        // Construct BAML:
        // <Window>
        //   <Window.Resources>
        //     <ResourceDictionary>
        //       <Deferable>
        //         <key "KeyA"/><UInt32>10</UInt32>
        //         <key "SharedKey"/><UInt32>100</UInt32>
        //       </Deferable>
        //     </ResourceDictionary>
        //   </Window.Resources>
        //   <Grid>
        //     <Grid.Resources>
        //       <ResourceDictionary>
        //         <Deferable>
        //           <key "KeyB"/><UInt32>20</UInt32>
        //           <key "SharedKey"/><UInt32>200</UInt32>
        //         </Deferable>
        //       </ResourceDictionary>
        //     </Grid.Resources>
        //     <Grid.Children>
        //       <Border>
        //         <Border.Resources>
        //           <ResourceDictionary>
        //             <Deferable>
        //               <key "KeyC"/><UInt32>30</UInt32>
        //             </Deferable>
        //           </ResourceDictionary>
        //         </Border.Resources>
        //       </Border>
        //     </Grid.Children>
        //   </Grid>
        // </Window>
        var b = new B();
        b.Raw(Header()).Raw(0x01); // DocumentStart
        b.Sized(0x1C, p => p.U16(0).Str("TestAssembly, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestAssembly.MainWindow"));
        b.Sized(0x1F, p => p.U16(0).I16(TWindow).Raw(0x00).Str("Resources"));
        b.Sized(0x1F, p => p.U16(1).I16(TGrid).Raw(0x00).Str("Resources"));
        b.Sized(0x1F, p => p.U16(2).I16(TBorder).Raw(0x00).Str("Resources"));
        b.Sized(0x20, p => p.U16(0).Str("KeyA"));
        b.Sized(0x20, p => p.U16(1).Str("SharedKey"));
        b.Sized(0x20, p => p.U16(2).Str("KeyB"));
        b.Sized(0x20, p => p.U16(3).Str("KeyC"));

        // Window Start
        b.Raw(0x03).I16(0).Raw(0x00);
        // Window.Resources
        b.Raw(0x0D).U16(0); // PropertyDictionaryStart(Window.Resources)
        b.Raw(0x03).I16(TResourceDictionary).Raw(0x02);
        var winDict = new B();
        winDict.Sized(0x26, p => p.U16(0).U32(0).Raw(0x00, 0x00)); // key KeyA
        winDict.Raw(0x03).I16(KnownUInt32).Raw(0x00);
        winDict.Sized(0x11, p => p.Str("10").I16(ConvUInt32));
        winDict.Raw(0x04);
        winDict.Sized(0x26, p => p.U16(1).U32(0).Raw(0x00, 0x00)); // key SharedKey
        winDict.Raw(0x03).I16(KnownUInt32).Raw(0x00);
        winDict.Sized(0x11, p => p.Str("100").I16(ConvUInt32));
        winDict.Raw(0x04);
        var winBody = winDict.ToArray();
        b.Raw(0x25).U32((uint)winBody.Length);
        b.Raw(winBody);
        b.Raw(0x04); // ResourceDictionary End
        b.Raw(0x0E); // PropertyDictionaryEnd

        // Content = Grid
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        // Grid.Resources
        b.Raw(0x0D).U16(1); // PropertyDictionaryStart(Grid.Resources)
        b.Raw(0x03).I16(TResourceDictionary).Raw(0x02);
        var gridDict = new B();
        gridDict.Sized(0x26, p => p.U16(2).U32(0).Raw(0x00, 0x00)); // key KeyB
        gridDict.Raw(0x03).I16(KnownUInt32).Raw(0x00);
        gridDict.Sized(0x11, p => p.Str("20").I16(ConvUInt32));
        gridDict.Raw(0x04);
        gridDict.Sized(0x26, p => p.U16(1).U32(0).Raw(0x00, 0x00)); // key SharedKey
        gridDict.Raw(0x03).I16(KnownUInt32).Raw(0x00);
        gridDict.Sized(0x11, p => p.Str("200").I16(ConvUInt32));
        gridDict.Raw(0x04);
        var gridBody = gridDict.ToArray();
        b.Raw(0x25).U32((uint)gridBody.Length);
        b.Raw(gridBody);
        b.Raw(0x04); // ResourceDictionary End
        b.Raw(0x0E); // PropertyDictionaryEnd

        // Grid.Children -> Border
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(TBorder).Raw(0x00);
        // Border.Resources
        b.Raw(0x0D).U16(2); // PropertyDictionaryStart(Border.Resources)
        b.Raw(0x03).I16(TResourceDictionary).Raw(0x02);
        var borderDict = new B();
        borderDict.Sized(0x26, p => p.U16(3).U32(0).Raw(0x00, 0x00)); // key KeyC
        borderDict.Raw(0x03).I16(KnownUInt32).Raw(0x00);
        borderDict.Sized(0x11, p => p.Str("30").I16(ConvUInt32));
        borderDict.Raw(0x04);
        var borderBody = borderDict.ToArray();
        b.Raw(0x25).U32((uint)borderBody.Length);
        b.Raw(borderBody);
        b.Raw(0x04); // ResourceDictionary End
        b.Raw(0x0E); // PropertyDictionaryEnd

        b.Raw(0x04); // Border End
        b.Raw(0x04); // Grid End
        b.Raw(0x04); // Window End
        b.Raw(0x02); // DocumentEnd

        var window = Load(b.ToArray());
        var grid = Assert.IsType<Grid>(window.Content);
        var border = Assert.IsType<Border>(Assert.Single(grid.Children));

        // Direct dictionary checks
        Assert.Equal(10u, window.Resources["KeyA"]);
        Assert.Equal(100u, window.Resources["SharedKey"]);
        Assert.Equal(20u, grid.Resources["KeyB"]);
        Assert.Equal(200u, grid.Resources["SharedKey"]);
        Assert.Equal(30u, border.Resources["KeyC"]);

        // Hierarchical TryFindResource resolution on Border
        Assert.True(border.TryFindResource("KeyC", out var c));
        Assert.Equal(30u, c);

        Assert.True(border.TryFindResource("KeyB", out var resB));
        Assert.Equal(20u, resB);

        // Grid overrides Window for SharedKey
        Assert.True(border.TryFindResource("SharedKey", out var shared));
        Assert.Equal(200u, shared);

        // Resolved up to Window
        Assert.True(border.TryFindResource("KeyA", out var resA));
        Assert.Equal(10u, resA);
    }

    [Fact]
    public void Baml_element_property_with_static_resource_resolves_from_ancestor()
    {
        // <Window>
        //   <Window.Resources>
        //     <ResourceDictionary>
        //       <Deferable><key "BorderBrush"/><Color>#FF112233</Color></Deferable>
        //     </ResourceDictionary>
        //   </Window.Resources>
        //   <Grid>
        //     <Border BorderBrush="{StaticResource BorderBrush}"/>
        //   </Grid>
        // </Window>
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestAssembly, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestAssembly.MainWindow"));
        b.Sized(0x1F, p => p.U16(0).I16(TWindow).Raw(0x00).Str("Resources"));
        b.Sized(0x1F, p => p.U16(1).I16(TBorder).Raw(0x00).Str("BorderBrush"));
        b.Sized(0x20, p => p.U16(0).Str("BorderBrush"));

        b.Raw(0x03).I16(0).Raw(0x00); // Window Start
        // Window.Resources
        b.Raw(0x0D).U16(0);
        b.Raw(0x03).I16(TResourceDictionary).Raw(0x02);
        var winDict = new B();
        winDict.Sized(0x26, p => p.U16(0).U32(0).Raw(0x00, 0x00)); // key BorderBrush
        winDict.Raw(0x03).I16(KnownColor).Raw(0x00);
        winDict.Sized(0x11, p => p.Str("#FF112233").I16(ConvColor));
        winDict.Raw(0x04);
        var winBody = winDict.ToArray();
        b.Raw(0x25).U32((uint)winBody.Length);
        b.Raw(winBody);
        b.Raw(0x04); // ResourceDictionary End
        b.Raw(0x0E); // PropertyDictionaryEnd

        // Content = Grid
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(TBorder).Raw(0x00);
        // PropertyWithStaticResourceId(0x38): attrId 1 (BorderBrush), staticId 0 ("BorderBrush" in StringInfo)
        b.Raw(0x38).I16(1).I16(0);
        b.Raw(0x04); // Border End
        b.Raw(0x04); // Grid End
        b.Raw(0x04); // Window End
        b.Raw(0x02); // DocumentEnd

        var window = Load(b.ToArray());
        var grid = Assert.IsType<Grid>(window.Content);
        var border = Assert.IsType<Border>(Assert.Single(grid.Children));

        var solidBrush = Assert.IsType<SolidColorBrush>(border.BorderBrush);
        Assert.Equal(Color.FromArgb(0xFF, 0x11, 0x22, 0x33), solidBrush.Color);
    }

    [Fact]
    public void Baml_implicit_styles_apply_to_child_elements_with_inner_scope_override()
    {
        // Window defines implicit style for TextBlock
        // Grid defines implicit style for TextBlock
        // TextBlock inside Grid receives Grid's implicit style.
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestAssembly, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestAssembly.MainWindow"));
        b.Sized(0x1F, p => p.U16(0).I16(TWindow).Raw(0x00).Str("Resources"));
        b.Sized(0x1F, p => p.U16(1).I16(TGrid).Raw(0x00).Str("Resources"));
        b.Sized(0x1F, p => p.U16(2).I16(TStyle).Raw(0x00).Str("TargetType"));

        // Window Start
        b.Raw(0x03).I16(0).Raw(0x00);
        // Window.Resources
        b.Raw(0x0D).U16(0);
        b.Raw(0x03).I16(TResourceDictionary).Raw(0x02);
        var winDict = new B();
        // Implicit style key for TextBlock
        winDict.Raw(0x27).I16(TTextBlock).Raw(0x00).U32(0).Raw(0x00, 0x00);
        winDict.Raw(0x03).I16(TStyle).Raw(0x00);
        winDict.Raw(0x22).I16(2).I16(TTextBlock); // TargetType = TextBlock
        winDict.Raw(0x04); // Style End
        var winBody = winDict.ToArray();
        b.Raw(0x25).U32((uint)winBody.Length);
        b.Raw(winBody);
        b.Raw(0x04);
        b.Raw(0x0E); // PropertyDictionaryEnd

        // Grid Start
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TGrid).Raw(0x00);
        // Grid.Resources
        b.Raw(0x0D).U16(1);
        b.Raw(0x03).I16(TResourceDictionary).Raw(0x02);
        var gridDict = new B();
        gridDict.Raw(0x27).I16(TTextBlock).Raw(0x00).U32(0).Raw(0x00, 0x00);
        gridDict.Raw(0x03).I16(TStyle).Raw(0x00);
        gridDict.Raw(0x22).I16(2).I16(TTextBlock); // TargetType = TextBlock
        gridDict.Raw(0x04); // Style End
        var gridBody = gridDict.ToArray();
        b.Raw(0x25).U32((uint)gridBody.Length);
        b.Raw(gridBody);
        b.Raw(0x04);
        b.Raw(0x0E); // PropertyDictionaryEnd

        // Grid.Children -> TextBlock
        b.Raw(0x2E).I16(MChildren);
        b.Raw(0x03).I16(TTextBlock).Raw(0x00);
        b.Raw(0x04); // TextBlock End

        b.Raw(0x04); // Grid End
        b.Raw(0x04); // Window End
        b.Raw(0x02); // DocumentEnd

        var window = Load(b.ToArray());
        var grid = Assert.IsType<Grid>(window.Content);
        var tb = Assert.IsType<TextBlock>(Assert.Single(grid.Children));

        var gridStyle = Assert.IsType<Style>(grid.Resources[typeof(TextBlock)]);
        var winStyle = Assert.IsType<Style>(window.Resources[typeof(TextBlock)]);

        Assert.NotSame(winStyle, gridStyle);
        // Inner grid style must be applied to tb, overriding window style
        Assert.Same(gridStyle, tb.Style);
    }

    [Fact]
    public void Baml_databinding_deferred_converter_resolves_from_ancestor_resources()
    {
        // Test that a Binding Expression with DeferredConverterKey resolves the converter
        // from ancestor FrameworkElement resources (hierarchical TryFindResource).
        var window = new Window();
        var converter = new BooleanToVisibilityConverter();
        window.Resources.Add("B2V", converter);

        var grid = new Grid();
        window.Content = grid;

        var targetTb = new TextBlock();
        grid.Children.Add(targetTb);

        var binding = new Binding("IsActive")
        {
            Source = new ActiveContext { IsActive = false },
            DeferredConverterKey = "B2V"
        };

        targetTb.SetBinding(UIElement.VisibilityProperty, binding);

        // Binding must evaluate and use BooleanToVisibilityConverter resolving from window.Resources
        Assert.Equal(Visibility.Collapsed, targetTb.Visibility);
    }

    [Fact]
    public void Baml_property_dictionary_start_supports_wellknown_negative_member_ids()
    {
        // Test using well-known negative member id -55 (FrameworkElement.Resources)
        // without an explicit AttributeInfo entry.
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("TestAssembly, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("TestAssembly.MainWindow"));
        b.Sized(0x20, p => p.U16(0).Str("DirectKey"));

        b.Raw(0x03).I16(0).Raw(0x00); // Window Start
        // PropertyDictionaryStart with negative member id -55
        b.Raw(0x0D).I16(-55);
        b.Raw(0x03).I16(TResourceDictionary).Raw(0x02);
        var winDict = new B();
        winDict.Sized(0x26, p => p.U16(0).U32(0).Raw(0x00, 0x00)); // key DirectKey
        winDict.Raw(0x03).I16(KnownUInt32).Raw(0x00);
        winDict.Sized(0x11, p => p.Str("42").I16(ConvUInt32));
        winDict.Raw(0x04);
        var winBody = winDict.ToArray();
        b.Raw(0x25).U32((uint)winBody.Length);
        b.Raw(winBody);
        b.Raw(0x04);
        b.Raw(0x0E); // PropertyDictionaryEnd

        b.Raw(0x04); // Window End
        b.Raw(0x02); // DocumentEnd

        var window = Load(b.ToArray());
        Assert.Equal(42u, window.Resources["DirectKey"]);
    }

    private sealed class ActiveContext
    {
        public bool IsActive { get; set; }
    }
}
