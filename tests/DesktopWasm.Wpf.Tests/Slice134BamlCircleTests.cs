// Slice-134: HandyControl theme.baml CircleProgressBarTemplate run.
//
// The keyed 'CircleProgressBarTemplate' ControlTemplate entry (keyidx 138)
// at [BAML+0x18E51, BAML+0x18F6C) lands: its ControlTemplate targets the
// converted HandyControl.Controls.CircleProgressBar, its root visual is the
// converted HandyControl.Controls.SimplePanel (routed through the engine
// SimplePanel mirror like the staged ToggleBlock child), its children are
// two converted HandyControl.Expression.Shapes.Arc visuals (ArcThickness /
// Width / Height / Stroke / Fill TemplateBindings plus Stretch / EndAngle /
// StrokeThickness simples, the second named PART_Indicator) plus a TextBlock
// (Horizontal/VerticalAlignment Center, Foreground/Text TemplateBindings,
// Visibility bound to the templated parent's ShowText through the
// Boolean2VisibilityConverter static, which rides the key-block 0x37 into
// the entry statics and the in-Binding 0x38). The four following
// ItemsPanelTemplate siblings (keyidx 139-142, Uniform/Stack) land through
// the slice-132 shapes, slice-135 landed the keyed
// 'VirtualizingStackHorizontal/VerticalItemsPanelTemplate' entries (keyidx
// 143-144), and slice-136 landed the keyed 'WrapHorizontal/WrapVertical/DockItemsPanelTemplate' entries (keyidx 145-147) plus the generic 'ResizeGripperForeground' brush (keyidx 148) and 'SineEaseOut' easing (keyidx 149), and slice-137 landed the keyed 'BadgeDotTemplate' entry (keyidx 150) plus the 'ColorPickerSliderRepeatButtonBaseStyle' Style (keyidx 151), and slice-138 landed the keyed 'ColorPickerSliderThumbTemplate' entry (keyidx 152) with its Thumb TargetType and packed-White Border root, and slice-139 landed the keyed 'ColorPickerRainbowBrush' brush (keyidx 153) at BAML+0x192B2 with WPF-default points and a zero first offset, and slice-140 landed the keyed 'ColorPickerOpacityBrush' brush (keyidx 154) at BAML+0x19383 with its checker DrawingGroup, so the run stops precise at the keyed 'CoverFlowBaseStyle' Style (keyidx 155) at BAML+0x19497 on its template Border's Viewport3D child (well-known type id -729)
// Triangulation: staged bytes byte-exact below; the upstream HandyControl
// ProgressBarBaseStyle.xaml block at sourceCommit e9ad3d70 carries the same
// template verbatim (arc order/values/TemplateBindings/names match); a
// genuine net10.0-windows fixture (E:/tmp/bamlfix/Circle.xaml) compiling the
// same TemplateBinding/RelativeSource/StaticResource shapes emits the
// framework member ids 9B FF (Shape.Stroke) and 93 FF
// (TextBlock.Foreground) plus the 24 FD VirtualizingStackPanel visual head,
// byte-identical in every staged record shape (differing only in per-file
// table numbering and the fixture's line markers, which the staged file
// omits). Real behavior changes: engine Shape gains the public virtual
// RenderedGeometry slot (dotnet/wpf parity; the converted IShape interface
// map names get_RenderedGeometry), Shape.Stroke and TextBlock.Foreground/
// Text gain WPF-identical DependencyProperty identities, and
// FrameworkPropertyMetadata is unsealed (real WPF; converted
// DrawingPropertyMetadata derives from it).
using System.Reflection;
using System.Security.Cryptography;
using System.Resources;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice134BamlCircleTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice134BamlCircleTests()
    {
        XamlLoader.CustomElementSource = null;
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Staged_manifest_keeps_six_staged_two_absent()
    {
        // Live staged-oracle gate: HandyControl stays conversion-failed
        // with exactly the same two absent upstream binaries, so the run
        // below decodes the same staged bytes the earlier slices pinned.
        // The 466350-byte theme.baml length plus the sha256 map below pin
        // the input. Loading the staged entry first guarantees the
        // HandyControl assembly is present however the runner orders the
        // tests.
        LoadStagedHandyControlEntry();
        var apps = StagedHandyAppsDirectory();
        using var document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
        var root = document.RootElement;
        Assert.Equal("conversion-failed", root.GetProperty("status").GetString());
        var assemblies = root.GetProperty("assemblies").EnumerateArray().ToArray();
        Assert.Equal(6, assemblies.Length);
        var names = assemblies.Select(a => a.GetProperty("name").GetString()).ToArray();
        Assert.Equal(
            new[] { "HandyControlDemo", "HandyControl", "GalaSoft.MvvmLight", "ICSharpCode.AvalonEdit", "HandyControlDemoCode", "Newtonsoft.Json" },
            names);
        foreach (var assembly in assemblies)
        {
            var file = assembly.GetProperty("file").GetString()!;
            var expected = assembly.GetProperty("sha256").GetString()!;
            var actual = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(System.IO.Path.Combine(apps, file))));
            Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                $"hash mismatch in staged '{file}': {actual} != {expected}");
        }
        var gaps = root.GetProperty("gaps").EnumerateArray()
            .Select(g => g.GetProperty("detail").GetString()).ToArray();
        Assert.Equal(2, gaps.Length);
        Assert.Contains(gaps, g => g!.Contains("System.Security.Permissions", StringComparison.Ordinal));
        Assert.Contains(gaps, g => g!.Contains("System.Windows.Extensions", StringComparison.Ordinal));
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
    }

    [Fact]
    public void Theme_baml_lands_circle_template_then_stops_at_virt_stack()
    {
        // Staged framing pins for the keyed 'CircleProgressBarTemplate'
        // ControlTemplate entry keyidx 138 spanning [0x18E51, 0x18F6C):
        // the 03 94 FF head, the TargetType PWC, the VisualTree content, the
        // converted SimplePanel root with its TemplateRoot Name, the two
        // converted Arc children (ArcThickness TemplateBindings plus
        // Stretch/EndAngle/StrokeThickness simples plus the PART_Indicator
        // name), the TextBlock child (alignment simples, Foreground/Text
        // TemplateBindings, the TemplatedParent Visibility Binding with its
        // 0x38 Converter static), the key record plus its single 0x37
        // static, the four Uniform/Stack ItemsPanelTemplate siblings, and
        // the two VirtualizingStackPanel heads (slice-135 advances the stop
        // past both to the 'WrapHorizontalItemsPanelTemplate' entry).
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");

        var head = new byte[] { 0x03, 0x94, 0xFF, 0x00, 0x22, 0x07, 0x00, 0x30, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0x2E, 0x00, 0x00 };
        Assert.Equal(head, theme[0x18E51..0x18E61]);
        var rootName = new byte[] { 0x24, 0x12, 0x2E, 0x00, 0x0C, 0x54, 0x65, 0x6D, 0x70, 0x6C, 0x61, 0x74, 0x65, 0x52, 0x6F, 0x6F, 0x74, 0x99, 0xFD, 0x2E, 0x2B, 0xFF };
        Assert.Equal(rootName, theme[0x18E61..0x18E77]);
        var arc1Head = new byte[] { 0x03, 0x31, 0x00, 0x00, 0x23, 0x30, 0x00, 0x7A, 0x02, 0x2F, 0x00 };
        Assert.Equal(arc1Head, theme[0x18E77..0x18E82]);
        var arc1Simples = new byte[] { 0x24, 0x0A, 0x31, 0x00, 0x04, 0x4E, 0x6F, 0x6E, 0x65, 0x3D, 0xFF, 0x24, 0x09, 0x32, 0x00, 0x03, 0x33, 0x36, 0x30, 0x51, 0xFF };
        Assert.Equal(arc1Simples, theme[0x18E82..0x18E97]);
        var arc1Bindings = new byte[] { 0x23, 0xC7, 0xFF, 0x7A, 0x02, 0x1A, 0x00, 0x23, 0xD1, 0xFF, 0x7A, 0x02, 0x19, 0x00, 0x23, 0x9B, 0xFF, 0x7A, 0x02, 0xE7, 0xFF, 0x23, 0x9C, 0xFF, 0x7A, 0x02, 0xE8, 0xFF };
        Assert.Equal(arc1Bindings, theme[0x18E97..0x18EB3]);
        var arc1Tail = new byte[] { 0x24, 0x07, 0x9A, 0xFF, 0x01, 0x30, 0xA4, 0xFE, 0x04 };
        Assert.Equal(arc1Tail, theme[0x18EB3..0x18EBC]);
        var arc2Head = new byte[] { 0x03, 0x31, 0x00, 0x00, 0x24, 0x14, 0x33, 0x00, 0x0E, 0x50, 0x41, 0x52, 0x54, 0x5F, 0x49, 0x6E, 0x64, 0x69, 0x63, 0x61, 0x74, 0x6F, 0x72, 0x99, 0xFD };
        Assert.Equal(arc2Head, theme[0x18EBC..0x18ED5]);
        var arc2Mid = new byte[] { 0x23, 0x30, 0x00, 0x7A, 0x02, 0x2F, 0x00, 0x24, 0x0A, 0x31, 0x00, 0x04, 0x4E, 0x6F, 0x6E, 0x65, 0x3D, 0xFF, 0x23, 0xC7, 0xFF, 0x7A, 0x02, 0x1A, 0x00 };
        Assert.Equal(arc2Mid, theme[0x18ED5..0x18EEE]);
        var arc2Tail = new byte[] { 0x24, 0x07, 0x9A, 0xFF, 0x01, 0x30, 0xA4, 0xFE, 0x23, 0xD1, 0xFF, 0x7A, 0x02, 0x19, 0x00, 0x23, 0x9C, 0xFF, 0x7A, 0x02, 0xE0, 0xFF, 0x04 };
        Assert.Equal(arc2Tail, theme[0x18EEE..0x18F05]);
        var textHead = new byte[] { 0x03, 0x82, 0xFD, 0x00, 0x24, 0x0C, 0xD0, 0xFF, 0x06, 0x43, 0x65, 0x6E, 0x74, 0x65, 0x72, 0x3D, 0xFF, 0x24, 0x0C, 0xC8, 0xFF, 0x06, 0x43, 0x65, 0x6E, 0x74, 0x65, 0x72, 0x3D, 0xFF };
        Assert.Equal(textHead, theme[0x18F05..0x18F23]);
        var textBindings = new byte[] { 0x23, 0x93, 0xFF, 0x7A, 0x02, 0xE0, 0xFF, 0x23, 0x92, 0xFF, 0x7A, 0x02, 0x34, 0x00, 0x07, 0x79, 0xFF, 0x03, 0xEC, 0xFF, 0x00, 0x2A };
        Assert.Equal(textBindings, theme[0x18F23..0x18F39]);
        var textPath = new byte[] { 0x10, 0x0A, 0x08, 0x53, 0x68, 0x6F, 0x77, 0x54, 0x65, 0x78, 0x74, 0x2B, 0x07, 0x0A, 0x00, 0x03, 0xFC, 0xFD, 0x00, 0x2A };
        Assert.Equal(textPath, theme[0x18F39..0x18F4D]);
        var textSource = new byte[] { 0x10, 0x11, 0x0F, 0x54, 0x65, 0x6D, 0x70, 0x6C, 0x61, 0x74, 0x65, 0x64, 0x50, 0x61, 0x72, 0x65, 0x6E, 0x74, 0x2B, 0x04, 0x08, 0x38, 0x25, 0x00, 0x00, 0x00, 0x04, 0x08, 0x04, 0x04, 0x04 };
        Assert.Equal(textSource, theme[0x18F4D..0x18F6C]);

        var key138 = new byte[] { 0x26, 0x09, 0x80, 0x00, 0xBA, 0x9C, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key138, theme[0xB485..0xB48F]);
        var singleStatic = new byte[] { 0x37, 0x00, 0x08, 0x00 };
        Assert.Equal(singleStatic, theme[0xB48F..0xB493]);
        var key139 = new byte[] { 0x26, 0x09, 0x81, 0x00, 0xD5, 0x9D, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key139, theme[0xB493..0xB49D]);

        var uniformH = new byte[] { 0x03, 0xB6, 0xFE, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0x41, 0xFD, 0x00, 0x24, 0x07, 0x27, 0x00, 0x01, 0x31, 0xC5, 0xFE, 0x07, 0x28, 0x00, 0x03, 0x5A, 0xFE, 0x00, 0x04, 0x08, 0x04, 0x04 };
        Assert.Equal(uniformH, theme[0x18F6C..0x18F8A]);
        var uniformV = new byte[] { 0x03, 0xB6, 0xFE, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0x41, 0xFD, 0x00, 0x24, 0x07, 0x29, 0x00, 0x01, 0x31, 0xC5, 0xFE, 0x07, 0x28, 0x00, 0x03, 0x5A, 0xFE, 0x00, 0x04, 0x08, 0x04, 0x04 };
        Assert.Equal(uniformV, theme[0x18F8A..0x18FA8]);
        var stackH = new byte[] { 0x03, 0xB6, 0xFE, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0xA7, 0xFD, 0x00, 0x24, 0x10, 0x08, 0x00, 0x0A, 0x48, 0x6F, 0x72, 0x69, 0x7A, 0x6F, 0x6E, 0x74, 0x61, 0x6C, 0x3D, 0xFF, 0x07, 0x28, 0x00, 0x03, 0x5A, 0xFE, 0x00, 0x04, 0x08, 0x04, 0x04 };
        Assert.Equal(stackH, theme[0x18FA8..0x18FCF]);
        var stackV = new byte[] { 0x03, 0xB6, 0xFE, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0xA7, 0xFD, 0x00, 0x07, 0x28, 0x00, 0x03, 0x5A, 0xFE, 0x00, 0x04, 0x08, 0x04, 0x04 };
        Assert.Equal(stackV, theme[0x18FCF..0x18FE5]);

        var virtHead = new byte[] { 0x03, 0xB6, 0xFE, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0x24, 0xFD, 0x00, 0x24, 0x10, 0x35, 0x00, 0x0A, 0x48, 0x6F, 0x72, 0x69, 0x7A, 0x6F, 0x6E, 0x74, 0x61, 0x6C, 0x3D, 0xFF };
        Assert.Equal(virtHead, theme[0x18FE5..0x19001]);
        var nextHead = new byte[] { 0x03, 0xB6, 0xFE, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0x24, 0xFD, 0x00 };
        Assert.Equal(nextHead, theme[0x1900C..0x19017]);
    }

    [Fact]
    public void Staged_boot_advances_past_circle_to_virt_stack()
    {
        // The staged boot now materializes entry #138 (the
        // 'CircleProgressBarTemplate' ControlTemplate with its converted
        // SimplePanel/Arc/TextBlock visuals) plus the four Uniform/Stack
        // ItemsPanelTemplate siblings #139-142 in index order plus the
        // VirtualizingStackPanel siblings #143-144, and stops at
        // the 'WrapHorizontalItemsPanelTemplate' entry: the
        // precise next blocker. Every earlier stop stays absent (including
        // the converted-SimplePanel root stop and the -358 entry-type
        // stop), and the new visual offset asserts the exact frontier.
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("line 23", error.Message);
        Assert.Contains("position 26", error.Message);
        Assert.DoesNotContain("SkinDefault", error.Message);
        var inner = Assert.IsType<XamlLoadException>(error.InnerException);
        Assert.DoesNotContain("PathBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x166FF", inner.Message);
        Assert.DoesNotContain("BAML+0x167B8", inner.Message);
        Assert.DoesNotContain("x:Static", inner.Message);
        Assert.DoesNotContain("VerticalScrollBarButtonHeightKey", inner.Message);
        Assert.DoesNotContain("BAML+0x167C8", inner.Message);
        Assert.DoesNotContain("BehaviorXY200", inner.Message);
        Assert.DoesNotContain("FluidMoveBehavior", inner.Message);
        Assert.DoesNotContain("PathButtonBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x13231", inner.Message);
        Assert.DoesNotContain("BAML+0x1345C", inner.Message);
        Assert.DoesNotContain("BAML+0x13612", inner.Message);
        Assert.DoesNotContain("BAML+0x137BB", inner.Message);
        Assert.DoesNotContain("FluidMoveBehaviorTag", inner.Message);
        Assert.DoesNotContain("Unknown element", inner.Message);
        Assert.DoesNotContain("BAML+0x139C3", inner.Message);
        Assert.DoesNotContain("BAML+0x13A35", inner.Message);
        Assert.DoesNotContain("BAML+0x167D3", inner.Message);
        Assert.DoesNotContain("holds ControlTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x1859F", inner.Message);
        Assert.DoesNotContain("GroupBoxBaseLeftTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x1888C", inner.Message);
        Assert.DoesNotContain("GroupBoxTabTopTemplate", inner.Message);
        Assert.DoesNotContain("GroupBoxTabLeftTemplate", inner.Message);
        Assert.DoesNotContain("template Binding holds record 0x38", inner.Message);
        Assert.DoesNotContain("BAML+0x18913", inner.Message);
        Assert.DoesNotContain("template property id -6", inner.Message);
        Assert.DoesNotContain("BAML+0x18D3F", inner.Message);
        Assert.DoesNotContain("ButtonGroupHorizontalUniformItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("ButtonGroupVerticalUniformItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("ButtonGroupHorizontalStackItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("ButtonGroupVerticalStackItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("ButtonGroupItemStyleSelector", inner.Message);
        Assert.DoesNotContain("holds ItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("holds well-known type id -330", inner.Message);
        Assert.DoesNotContain("BAML+0x18DB8", inner.Message);
        Assert.DoesNotContain("ProgressBarIndicatorAnimatedFill", inner.Message);
        Assert.DoesNotContain("holds a LinearGradientBrush", inner.Message);
        Assert.DoesNotContain("holds well-known type id -358", inner.Message);
        Assert.DoesNotContain("holds LinearGradientBrush", inner.Message);
        Assert.DoesNotContain("BAML+0x18DBD", inner.Message);
        Assert.DoesNotContain("GradientStop in gradient entry 'ColorPickerRainbowBrush'", inner.Message);
        Assert.DoesNotContain("BAML+0x18E5D", inner.Message);
        Assert.DoesNotContain("CircleProgressBarTemplate", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimplePanel", inner.Message);
        Assert.DoesNotContain("templates take a ContentPresenter", inner.Message);
        Assert.DoesNotContain("BAML+0x19029", inner.Message);
        Assert.DoesNotContain("VirtualizingStackHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapVerticalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("DockItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("ResizeGripperForeground", inner.Message);
        Assert.DoesNotContain("SineEaseOut", inner.Message);
        Assert.DoesNotContain("BAML+0x19150", inner.Message);
        Assert.DoesNotContain("BadgeDotTemplate", inner.Message);
        Assert.DoesNotContain("element ContentPresenter on the template root panel", inner.Message);
        Assert.DoesNotContain("root panels take ContentPresenter, Border, ScrollViewer, converted Shape and TextBlock children", inner.Message);
        Assert.DoesNotContain("BAML+0x1927D", inner.Message);
        Assert.DoesNotContain("ColorPickerSliderThumbTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x192B9", inner.Message);
        Assert.DoesNotContain("ColorPickerRainbowBrush", inner.Message);
        Assert.DoesNotContain("has no Offset", inner.Message);
        Assert.DoesNotContain("BAML+0x19383", inner.Message);
        Assert.DoesNotContain("ColorPickerOpacityBrush", inner.Message);
        Assert.DoesNotContain("well-known type id -180", inner.Message);
        Assert.DoesNotContain("BAML+0x19497", inner.Message);
        Assert.DoesNotContain("CoverFlowBaseStyle", inner.Message);
        Assert.DoesNotContain("well-known type id -729", inner.Message);
        Assert.DoesNotContain("BAML+0x195FD", inner.Message);
        Assert.DoesNotContain("GridViewColumnHeaderGripper", inner.Message);
        Assert.DoesNotContain("well-known type id -66", inner.Message);
        Assert.DoesNotContain("Canvas.Right", inner.Message);
        Assert.DoesNotContain("BAML+0x196C6", inner.Message);
        Assert.DoesNotContain("'Padding' is not a dependency property of Border", inner.Message);
        Assert.DoesNotContain("SortByCategoryDrawingBrush", inner.Message);
        Assert.DoesNotContain("BAML+0x196F6", inner.Message);
        Assert.DoesNotContain("SortByNameDrawingBrush", inner.Message);
        Assert.DoesNotContain("TabItemMenu", inner.Message);
        Assert.DoesNotContain("BAML+0x19D99", inner.Message);
        Assert.DoesNotContain("BAML+0x19DCE", inner.Message);
        Assert.DoesNotContain("well-known type id -105", inner.Message);
        Assert.DoesNotContain("Lang.Close", inner.Message);
        Assert.DoesNotContain("throws reading the static property", inner.Message);
        Assert.DoesNotContain("XmlnsDefinitionAttribute", inner.Message);
        Assert.DoesNotContain("ToolBarTrayBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x19E4F", inner.Message);
        Assert.DoesNotContain("well-known type id -672", inner.Message);
        Assert.DoesNotContain("TreeArrow", inner.Message);
        Assert.DoesNotContain("BAML+0x19E7B", inner.Message);
        Assert.DoesNotContain("well-known type id -446", inner.Message);
        Assert.DoesNotContain("RowHeaderGripperStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x1A034", inner.Message);
        Assert.DoesNotContain("'Cursor' is not a dependency property of FrameworkElement", inner.Message);
        Assert.DoesNotContain("DataGridComboBoxColumnStyle", inner.Message);
        Assert.DoesNotContain("DataGridTextColumnStyle", inner.Message);        
        Assert.DoesNotContain("CarouselItemBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x1A113", inner.Message);
        Assert.DoesNotContain("BAML+0x1A186", inner.Message);        
        Assert.DoesNotContain("BAML+0x1A21B", inner.Message);
        Assert.DoesNotContain("well-known type id -92", inner.Message);
        Assert.DoesNotContain("targets TextBlock", inner.Message);        
        Assert.DoesNotContain("template property id -21", inner.Message);
        Assert.DoesNotContain("GrowlWindow", inner.Message);
        Assert.DoesNotContain("BAML+0x1A2CA", inner.Message);
        Assert.DoesNotContain("well-known member id -51", inner.Message);
        Assert.DoesNotContain("ListBoxItemCustom", inner.Message);
        Assert.DoesNotContain("BAML+0x1A368", inner.Message);
        Assert.DoesNotContain("well-known type id -374", inner.Message);
        Assert.DoesNotContain("BAML+0x1A92D", inner.Message);
        Assert.DoesNotContain("well-known member id -81", inner.Message);
        Assert.DoesNotContain("RibbonTab", inner.Message);
        Assert.DoesNotContain("BAML+0x1A990", inner.Message);
        Assert.DoesNotContain("template root panel property 'Visibility' with converter -195", inner.Message);
        Assert.DoesNotContain("BAML+0x1A9DD", inner.Message);
        Assert.DoesNotContain("element well-known type id -550 on the template root panel", inner.Message);
        Assert.DoesNotContain("BAML+0x1AE91", inner.Message);
        Assert.DoesNotContain("'EffectShadow1'", inner.Message);
        Assert.DoesNotContain("record 0x34 (converted-element values", inner.Message);
        Assert.DoesNotContain("BAML+0x1B001", inner.Message);
        Assert.DoesNotContain("BAML+0x1B22E", inner.Message);
        Assert.DoesNotContain("'SearchPathStyle'", inner.Message);
        Assert.DoesNotContain("'RightPathStyle'", inner.Message);
        Assert.DoesNotContain("record 0x38 in a Style", inner.Message);
        Assert.DoesNotContain("'RenderTransformOrigin' is not a dependency property of UIElement", inner.Message);
        Assert.DoesNotContain("BAML+0x1B260", inner.Message);
        Assert.DoesNotContain("well-known member id -134", inner.Message);
        Assert.Contains("'BorderRegion'", inner.Message);
        Assert.Contains("BAML+0x1B2F9", inner.Message);
        Assert.Contains("setter property owner Border in style entry 'BorderRegion'", inner.Message);
        Assert.DoesNotContain("BAML+0x1AA35", inner.Message);
        Assert.DoesNotContain("'PrimaryBrush'", inner.Message);
        Assert.DoesNotContain("record 0x34 in a LinearGradientBrush", inner.Message);
        Assert.DoesNotContain("BAML+0x1AA1C", inner.Message);
        Assert.DoesNotContain("LightPrimaryBrush", inner.Message);
        Assert.DoesNotContain("well-known type id -578", inner.Message);
        Assert.DoesNotContain("on setter 'Template'", inner.Message);
        Assert.DoesNotContain("well-known type id -658", inner.Message);
        Assert.DoesNotContain("VirtualizingStackPanel", inner.Message);
        Assert.DoesNotContain("items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", inner.Message);
    }

    [Fact]
    public void Staged_shape_circle_lands_live_template()
    {
        // The staged 'CircleProgressBarTemplate' entry shape lands as a
        // live ControlTemplate: converted CircleProgressBar target, engine
        // SimplePanel root named TemplateRoot, two converted Arcs (the
        // second named PART_Indicator) plus a centered TextBlock, eleven
        // (element, source) template bindings, and the ShowText Visibility
        // binding carrying the Boolean2VisibilityConverter deferred key.
        // Per-file ids stand in for the staged rows (7 TargetType, 46/47/
        // 48/49/50/51/52 names, 37 Converter, 10 RelativeSource, TypeInfos
        // 46/48/49). The converted HandyControl assembly must be present
        // for the target/visual types.
        LoadStagedHandyControlEntry();
        var n = StagedCircleTables();
        var dictionary = BamlReader.LoadDictionary(CircleDictionary("CircleTest", StagedCircleEntry(n).ToArray(), n));
        var template = Assert.IsType<ControlTemplate>(dictionary["CircleTest"]);
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        Assert.Same(handyControl.GetType("HandyControl.Controls.CircleProgressBar"), template.TargetType);
        var root = Assert.IsType<SimplePanel>(template.VisualTree);
        Assert.Same(root, template.TemplateNames["TemplateRoot"]);
        Assert.Equal(3, root.Children.Count);
        var arcType = handyControl.GetType("HandyControl.Expression.Shapes.Arc", throwOnError: true)!;
        var arc1 = root.Children[0];
        var arc2 = root.Children[1];
        Assert.Equal(arcType, arc1.GetType());
        Assert.Equal(arcType, arc2.GetType());
        Assert.Same(arc2, template.TemplateNames["PART_Indicator"]);
        Assert.Equal(Stretch.None, arc1.GetValue(DependencyProperty.FromName(arcType, "Stretch")));
        Assert.Equal(360.0, arc1.GetValue(DependencyProperty.FromName(arcType, "EndAngle")));
        Assert.Equal(0.0, arc1.GetValue(DependencyProperty.FromName(arcType, "StrokeThickness")));
        Assert.Equal(0.0, arc2.GetValue(DependencyProperty.FromName(arcType, "StrokeThickness")));
        Assert.Equal("PART_Indicator", arc2.GetValue(DependencyProperty.FromName(arcType, "Name")));
        var text = Assert.IsType<TextBlock>(root.Children[2]);
        Assert.Equal(HorizontalAlignment.Center, text.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, text.VerticalAlignment);
        var sources = template.TemplateBindings.Select(b => b.Property).ToArray();
        Assert.Equal(
            new[] { "ArcThickness", "ActualWidth", "ActualHeight", "BorderBrush", "Background", "ArcThickness", "ActualWidth", "ActualHeight", "Foreground", "Foreground", "Text" },
            sources);
        var visibility = BindingOperations.GetBinding(text, UIElement.VisibilityProperty);
        Assert.NotNull(visibility);
        Assert.Equal("ShowText", visibility!.Path.Path);
        Assert.Equal(RelativeSourceMode.TemplatedParent, visibility.RelativeSource!.Mode);
        Assert.Equal("Boolean2VisibilityConverter", visibility.DeferredConverterKey);
    }

    [Fact]
    public void Shared_circle_lookups_return_same_instance()
    {
        // The staged Circle key is shared (shared=0/sharedSet=0), so every
        // lookup returns the same ControlTemplate instance.
        LoadStagedHandyControlEntry();
        var n = StagedCircleTables();
        var dictionary = BamlReader.LoadDictionary(CircleDictionary(
            "CircleTest", StagedCircleEntry(n).ToArray(), n));
        var first = Assert.IsType<ControlTemplate>(dictionary["CircleTest"]);
        Assert.Same(first, Assert.IsType<ControlTemplate>(dictionary["CircleTest"]));
    }

    [Fact]
    public void Fixture_numbered_circle_lands_framework_shape()
    {
        // The genuine net10.0-windows fixture numbers its tables from zero
        // (attrs 0-8 over the well-known owners, like Circle.baml) and
        // carries 0x35/0x36 line markers: the parser resolves everything
        // through the tables, so the same shapes land with line markers
        // transparent. Framework stand-ins (StackPanel for SimplePanel,
        // Rectangle for Arc, Tag for the per-file Text source) verify the
        // framework member ids the staged entry relies on: Shape.Stroke
        // (-101) and TextBlock.Foreground (-109).
        XamlLoader.CustomElementSource = null;
        var n = FixtureCircleTables();
        var dictionary = BamlReader.LoadDictionary(CircleDictionary("CircleTest", FixtureCircleEntry(n).ToArray(), n));
        var template = Assert.IsType<ControlTemplate>(dictionary["CircleTest"]);
        Assert.Same(typeof(Rectangle), template.TargetType);
        var root = Assert.IsType<StackPanel>(template.VisualTree);
        Assert.Equal(3, root.Children.Count);
        var shape1 = Assert.IsType<Rectangle>(root.Children[0]);
        var shape2 = Assert.IsType<Rectangle>(root.Children[1]);
        Assert.Equal(Stretch.None, shape1.Stretch);
        Assert.Equal(0.0, shape1.StrokeThickness);
        Assert.Equal(0.0, shape2.StrokeThickness);
        var text = Assert.IsType<TextBlock>(root.Children[2]);
        Assert.Equal(HorizontalAlignment.Center, text.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, text.VerticalAlignment);
        var sources = template.TemplateBindings.Select(b => b.Property).ToArray();
        Assert.Equal(
            new[] { "ActualWidth", "ActualHeight", "BorderBrush", "Background", "ActualWidth", "ActualHeight", "Foreground", "Foreground", "Tag" },
            sources);
        var visibility = BindingOperations.GetBinding(text, UIElement.VisibilityProperty);
        Assert.NotNull(visibility);
        Assert.Equal("ShowText", visibility!.Path.Path);
        Assert.Equal(RelativeSourceMode.TemplatedParent, visibility.RelativeSource!.Mode);
        Assert.Equal("Boolean2VisibilityConverter", visibility.DeferredConverterKey);
    }

    [Fact]
    public void Circle_root_non_panel_fails_precise()
    {
        // A converted template root that is not a Panel fails instead of
        // landing a non-layout visual as a template root.
        LoadStagedHandyControlEntry();
        var n = StagedCircleTables();
        var body = CircleHead(n, 48)
            .Raw(CircleName(n).ToArray())
            .Raw(0x2E).I16(-213)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(CircleDictionary("CircleTest", body.ToArray(), n)));
        Assert.Contains("is not a Panel", error.Message);
        Assert.Contains("template roots take a SimplePanel visual", error.Message);
    }

    [Fact]
    public void Circle_root_duplicate_name_fails_precise()
    {
        // Two root Name simples fail instead of overwriting the template
        // namescope join.
        LoadStagedHandyControlEntry();
        var n = StagedCircleTables();
        var body = CircleHead(n, 46)
            .Raw(CircleName(n).ToArray())
            .Raw(CircleName(n).ToArray())
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(CircleDictionary("CircleTest", body.ToArray(), n)));
        Assert.Contains("duplicate template part name 'TemplateRoot'", error.Message);
    }

    [Fact]
    public void Circle_root_garbage_record_fails_precise()
    {
        // A list record directly on the converted root fails: roots take
        // the Name string plus the Children content with Shape/TextBlock
        // children.
        LoadStagedHandyControlEntry();
        var n = StagedCircleTables();
        var body = CircleHead(n, 46)
            .Raw(CircleName(n).ToArray())
            .Raw(0x2E).I16(-213)
            .Raw(0x0B).U16(1)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(CircleDictionary("CircleTest", body.ToArray(), n)));
        Assert.Contains("template root panel holds record 0x0B", error.Message);
    }

    [Fact]
    public void Circle_root_grid_child_fails_precise()
    {
        // A well-known Grid child on the converted root fails: root panels
        // take converted Shape and TextBlock children.
        LoadStagedHandyControlEntry();
        var n = StagedCircleTables();
        var body = CircleHead(n, 46)
            .Raw(CircleName(n).ToArray())
            .Raw(0x2E).I16(-213)
            .Raw(0x03).I16(-254).Raw(0x00)
            .Raw(0x04)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(CircleDictionary("CircleTest", body.ToArray(), n)));
        Assert.Contains("root panels take ContentPresenter, Border, ScrollViewer, converted Shape and TextBlock children", error.Message);
    }

    [Fact]
    public void Circle_arc_unknown_simple_fails_precise()
    {
        // A simple naming a per-file AttributeInfo id the file does not
        // have fails instead of landing an unbound value.
        LoadStagedHandyControlEntry();
        var n = StagedCircleTables();
        var arc = new B()
            .Raw(0x03).I16(49).Raw(0x00)
            .Sized(0x24, q => q.I16(99).Str("x").I16(-615))
            .Raw(0x04);
        var body = CircleHead(n, 46)
            .Raw(CircleName(n).ToArray())
            .Raw(0x2E).I16(-213)
            .Raw(arc.ToArray())
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(CircleDictionary("CircleTest", body.ToArray(), n)));
        Assert.Contains("has no AttributeInfo record", error.Message);
    }

    [Fact]
    public void Circle_arc_bad_target_fails_precise()
    {
        // HorizontalAlignment is not a Shape TemplateBinding target: the
        // Arc binds Stroke/Fill/Width/Height plus per-file rows.
        LoadStagedHandyControlEntry();
        var n = StagedCircleTables();
        var arc = new B()
            .Raw(0x03).I16(49).Raw(0x00)
            .Raw(0x23).I16(-48).U16(634).I16(49)
            .Raw(0x04);
        var body = CircleHead(n, 46)
            .Raw(CircleName(n).ToArray())
            .Raw(0x2E).I16(-213)
            .Raw(arc.ToArray())
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(CircleDictionary("CircleTest", body.ToArray(), n)));
        Assert.Contains("template extension target id -48", error.Message);
    }

    [Fact]
    public void Circle_arc_duplicate_name_fails_precise()
    {
        // Two PART_Indicator joins fail instead of overwriting the template
        // namescope.
        LoadStagedHandyControlEntry();
        var n = StagedCircleTables();
        var arc = CircleArc(n, "PART_Indicator", "0", -32, bindStroke: false).ToArray();
        var body = CircleHead(n, 46)
            .Raw(CircleName(n).ToArray())
            .Raw(0x2E).I16(-213)
            .Raw(arc)
            .Raw(arc)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(CircleDictionary("CircleTest", body.ToArray(), n)));
        Assert.Contains("duplicate template part name 'PART_Indicator'", error.Message);
    }

    [Fact]
    public void Circle_textblock_bad_simple_fails_precise()
    {
        // A Name simple on the TextBlock fails: staged text blocks carry
        // HorizontalAlignment/VerticalAlignment (optionally Visibility)
        // enum values.
        LoadStagedHandyControlEntry();
        var n = StagedCircleTables();
        var text = new B()
            .Raw(0x03).I16(-638).Raw(0x00)
            .Sized(0x24, q => q.I16((short)n.ArcName).Str("Tip").I16(-615))
            .Raw(0x04);
        var body = CircleHead(n, 46)
            .Raw(CircleName(n).ToArray())
            .Raw(0x2E).I16(-213)
            .Raw(text.ToArray())
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(CircleDictionary("CircleTest", body.ToArray(), n)));
        Assert.Contains("text blocks take HorizontalAlignment/VerticalAlignment/Visibility/TextTrimming enum values plus IsHitTestVisible bools", error.Message);
    }

    [Fact]
    public void Circle_textblock_bad_target_fails_precise()
    {
        // Width is not a TextBlock TemplateBinding target: the staged text
        // block binds Foreground/Text.
        LoadStagedHandyControlEntry();
        var n = StagedCircleTables();
        var text = new B()
            .Raw(0x03).I16(-638).Raw(0x00)
            .Raw(0x23).I16(-57).U16(634).I16(26)
            .Raw(0x04);
        var body = CircleHead(n, 46)
            .Raw(CircleName(n).ToArray())
            .Raw(0x2E).I16(-213)
            .Raw(text.ToArray())
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(CircleDictionary("CircleTest", body.ToArray(), n)));
        Assert.Contains("template extension target id -57", error.Message);
    }

    [Fact]
    public void Circle_binding_bad_converter_attr_fails_precise()
    {
        // A 0x38 over a non-Converter member fails naming the member: only
        // Binding.Converter rides the StaticResource form.
        LoadStagedHandyControlEntry();
        var n = StagedCircleTables();
        var visual = CircleVisualWithBinding(n, 0, 7);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(CircleDictionary("CircleTest", visual, n)));
        Assert.Contains("holds a StaticResource value for 'TargetType'", error.Message);
    }

    [Fact]
    public void Circle_binding_unknown_static_fails_precise()
    {
        // A 0x38 naming an entry-static id the key block never declared
        // fails instead of landing a dangling converter.
        LoadStagedHandyControlEntry();
        var n = StagedCircleTables();
        var visual = CircleVisualWithBinding(n, 5, 37);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(CircleDictionary("CircleTest", visual, n)));
        Assert.Contains("names static resource 5 (only 1 indexed static values", error.Message);
    }

    [Fact]
    public void Circle_binding_duplicate_converter_fails_precise()
    {
        // Two 0x38 Converter values on one template Binding fail instead
        // of overwriting the deferred key.
        LoadStagedHandyControlEntry();
        var n = StagedCircleTables();
        var binding = CircleBinding(n, 0, 37)
            .Raw(0x38).U16(37).U16(0)
            .Raw(0x04);
        var visual = CircleBodyWith(n, CircleArc(n, "PART_Indicator", "0", -32, bindStroke: false).ToArray(), CircleTextWith(binding.ToArray(), n).ToArray());
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(CircleDictionary("CircleTest", visual, n)));
        Assert.Contains("duplicate Converter on the template Binding", error.Message);
    }

    [Fact]
    public void Circle_binding_direct_static_record_fails_precise()
    {
        // A 0x37 OptimizedStaticResource record directly inside a template
        // Binding fails: bindings take the 0x38 PropertyWithStaticResourceId
        // Converter form.
        LoadStagedHandyControlEntry();
        var n = StagedCircleTables();
        var binding = CircleBinding(n, 0, 37)
            .Raw(0x37, 0x00).U16(1)
            .Raw(0x04);
        var visual = CircleBodyWith(n, CircleArc(n, "PART_Indicator", "0", -32, bindStroke: false).ToArray(), CircleTextWith(binding.ToArray(), n).ToArray());
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(CircleDictionary("CircleTest", visual, n)));
        Assert.Contains("holds record 0x37", error.Message);
        Assert.Contains("bindings take a path plus Converter plus ConverterParameter plus RelativeSource", error.Message);
    }

    [Fact]
    public void Shape_stroke_and_rendered_geometry_engine_units()
    {
        // Engine units for the slice-134 surface: Shape.Stroke defaults to
        // null and round-trips through the DP store, and RenderedGeometry
        // returns the defining geometry (empty when null), which is the
        // slot the converted Arc's IShape map inherits.
        XamlLoader.CustomElementSource = null;
        var rect = new Rectangle();
        Assert.Null(rect.Stroke);
        rect.Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x00));
        Assert.Same(rect.Stroke, rect.GetValue(Shape.StrokeProperty));
        var empty = new Path();
        Assert.Same(Geometry.Empty, empty.RenderedGeometry);
        var path = new Path { Data = new RectangleGeometry() };
        Assert.Same(path.Data, path.RenderedGeometry);
    }

    [Fact]
    public void Textblock_foreground_and_text_engine_units()
    {
        // Engine units for the slice-134 surface: TextBlock.Foreground and
        // Text resolve through the DP store, a solid Foreground syncs the
        // paint Rgba, and a direct store write routes back through the CLR
        // text folding.
        XamlLoader.CustomElementSource = null;
        Assert.NotNull(DependencyProperty.FromName(typeof(TextBlock), "Foreground"));
        Assert.NotNull(DependencyProperty.FromName(typeof(TextBlock), "Text"));
        var text = new TextBlock();
        Assert.Equal(string.Empty, text.Text);
        text.Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x22, 0x33));
        Assert.Same(text.Foreground, text.GetValue(TextBlock.ForegroundProperty));
        text.SetValue(TextBlock.TextProperty, "hi");
        Assert.Equal("hi", text.Text);
    }

    [Fact]
    public void Framework_metadata_unsealed_for_converted_shapes()
    {
        // The converted HandyControl Expression shapes derive
        // DrawingPropertyMetadata from FrameworkPropertyMetadata; a sealed
        // engine base fails their load with "parent type is sealed", so the
        // engine class stays unsealed like real WPF.
        LoadStagedHandyControlEntry();
        Assert.False(typeof(FrameworkPropertyMetadata).IsSealed);
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var metadata = handyControl.GetType("HandyControl.Expression.Media.DrawingPropertyMetadata", throwOnError: true)!;
        Assert.True(typeof(FrameworkPropertyMetadata).IsAssignableFrom(metadata));
    }

    /// <summary>Per-file table numbering standing in for the staged rows
    /// (attrs 7/10/25/26/37/46/47/48/49/50/51/52 over the staged owners,
    /// TypeInfos 46/48/49 over the converted HandyControl names).</summary>
    private sealed record CircleNumbering(
        (int Id, string Name)[] Types,
        (int Id, string Name, short Owner)[] Attrs,
        int TargetType, int RootName, int CircleArcThickness, int ArcArcThickness,
        int Stretch, int EndAngle, int ArcName, int CircleText,
        int ActualWidth, int ActualHeight, int Converter, int RelativeSource);

    private static CircleNumbering StagedCircleTables() => new(
        [(46, "HandyControl.Controls.SimplePanel"), (48, "HandyControl.Controls.CircleProgressBar"), (49, "HandyControl.Expression.Shapes.Arc")],
        [(7, "TargetType", -108), (46, "Name", 46), (47, "ArcThickness", 48), (48, "ArcThickness", 49), (49, "Stretch", -558), (50, "EndAngle", 49), (51, "Name", 49), (52, "Text", 48), (25, "ActualHeight", -226), (26, "ActualWidth", -226), (37, "Converter", -20), (10, "RelativeSource", -20)],
        7, 46, 47, 48, 49, 50, 51, 52, 26, 25, 37, 10);

    /// <summary>Per-file table numbering matching the genuine net10.0-windows
    /// fixture (attrs 0-8 over the well-known owners, like Circle.baml;
    /// TypeInfos 0/1 stand in for SimplePanel/Arc with framework targets).</summary>
    private static CircleNumbering FixtureCircleTables() => new(
        [(0, "System.Windows.Controls.StackPanel"), (1, "System.Windows.Shapes.Rectangle")],
        [(0, "TargetType", -108), (1, "Name", -601), (2, "Stretch", -558), (3, "ActualWidth", -226), (4, "ActualHeight", -226), (5, "Name", -514), (6, "Tag", -226), (7, "RelativeSource", -20), (8, "Converter", -20)],
        0, 1, 0, 0, 2, 0, 5, 6, 3, 4, 8, 7);

    /// <summary>Builds the staged 'CircleProgressBarTemplate' entry shape:
    /// the ControlTemplate head with its TargetType, the converted root,
    /// two converted Arcs, the TextBlock, closed once.</summary>
    private static B StagedCircleEntry(CircleNumbering n) => CircleHead(n, 46)
        .Raw(CircleName(n).ToArray())
        .Raw(0x2E).I16(-213)
        .Raw(CircleArc(n, null, "360", -24, bindStroke: true).ToArray())
        .Raw(CircleArc(n, "PART_Indicator", "0", -32, bindStroke: false).ToArray())
        .Raw(CircleTextWith(CircleBinding(n, 0, 37).Raw(0x04).ToArray(), n).ToArray())
        .Raw(0x04)
        .Raw(0x04);

    /// <summary>Builds the fixture-numbered Circle entry shape: the
    /// framework stand-in root (StackPanel for SimplePanel), two Rectangles
    /// (for the Arcs), the TextBlock, line markers transparent.</summary>
    private static B FixtureCircleEntry(CircleNumbering n) => new B()
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x35).U32(6).U32(10)
        .Raw(0x22).I16(0).I16(-514)
        .Raw(0x2E).I16(-174)
        .Raw(0x03).I16(0).Raw(0x00)
        .Sized(0x24, q => q.I16(1).Str("TemplateRoot").I16(-615))
        .Raw(0x2E).I16(-213)
        .Raw(0x35).U32(7).U32(12)
        .Raw(FixtureRect(n, null, bindStroke: true).ToArray())
        .Raw(FixtureRect(n, "PART_Indicator", bindStroke: false).ToArray())
        .Raw(FixtureTextWith(FixtureBinding(n).Raw(0x04).ToArray(), n).ToArray())
        .Raw(0x36).U32(8)
        .Raw(0x04)
        .Raw(0x04);

    /// <summary>Builds one ControlTemplate head over an explicit root
    /// TypeInfo id (for the root dispatch tests).</summary>
    private static B CircleHead(CircleNumbering n, int rootType) => new B()
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16((short)n.TargetType).I16(48)
        .Raw(0x2E).I16(-174)
        .Raw(0x03).I16((short)rootType).Raw(0x00);

    private static B CircleName(CircleNumbering n) => new B()
        .Sized(0x24, q => q.I16((short)n.RootName).Str("TemplateRoot").I16(-615));

    /// <summary>Builds one staged-shape Arc child (its ElementStart is
    /// included): the ArcThickness/Width/Height/optional-Stroke/Fill TemplateBindings plus the Stretch/
    /// EndAngle simples, closed once. A null name skips the namescope join
    /// (the staged first Arc carries no x:Name).</summary>
    private static B CircleArc(CircleNumbering n, string? name, string endAngle, short fillSource, bool bindStroke)
    {
        var arc = new B().Raw(0x03).I16(49).Raw(0x00);
        if (name is not null)
            arc.Sized(0x24, q => q.I16((short)n.ArcName).Str(name).I16(-615));
        arc = arc
            .Raw(0x23).I16((short)n.ArcArcThickness).U16(634).I16((short)n.CircleArcThickness)
            .Sized(0x24, q => q.I16((short)n.Stretch).Str("None").I16(-195))
            .Sized(0x24, q => q.I16((short)n.EndAngle).Str(endAngle).I16(-175))
            .Raw(0x23).I16(-57).U16(634).I16((short)n.ActualWidth)
            .Raw(0x23).I16(-47).U16(634).I16((short)n.ActualHeight);
        if (bindStroke)
            arc = arc.Raw(0x23).I16(-101).U16(634).I16(-25);
        return arc
            .Raw(0x23).I16(-100).U16(634).I16(fillSource)
            .Sized(0x24, q => q.I16(-102).Str("0").I16(-348))
            .Raw(0x04);
    }

    /// <summary>Builds one staged-shape TextBlock child over explicit
    /// Binding bytes (its ElementStart is included).</summary>
    private static B CircleTextWith(byte[] binding, CircleNumbering n) => new B()
        .Raw(0x03).I16(-638).Raw(0x00)
        .Sized(0x24, q => q.I16(-48).Str("Center").I16(-195))
        .Sized(0x24, q => q.I16(-56).Str("Center").I16(-195))
        .Raw(0x23).I16(-109).U16(634).I16(-32)
        .Raw(0x23).I16(-110).U16(634).I16((short)n.CircleText)
        .Raw(0x07).I16(-135)
        .Raw(binding)
        .Raw(0x08)
        .Raw(0x04);

    /// <summary>Builds one staged-shape template Binding: the ShowText path
    /// plus the TemplatedParent RelativeSource (the 0x38 Converter static
    /// is appended by the caller when the shape under test has one).</summary>
    private static B CircleBinding(CircleNumbering n, int staticId, int converterAttr) => new B()
        .Raw(0x03).I16(-20).Raw(0x00)
        .Raw(0x2A)
        .Sized(0x10, q => q.Str("ShowText"))
        .Raw(0x2B)
        .Raw(0x07).I16((short)n.RelativeSource)
        .Raw(0x03).I16(-516).Raw(0x00)
        .Raw(0x2A)
        .Sized(0x10, q => q.Str("TemplatedParent"))
        .Raw(0x2B)
        .Raw(0x04)
        .Raw(0x08)
        .Raw(0x38).U16((ushort)converterAttr).U16((ushort)staticId);

    /// <summary>Builds a full staged-shape ControlTemplate body over an Arc
    /// plus a TextBlock holding one Binding (for the Binding negative
    /// tests).</summary>
    private static byte[] CircleBodyWith(CircleNumbering n, byte[] arc, byte[] text) => CircleHead(n, 46)
        .Raw(CircleName(n).ToArray())
        .Raw(0x2E).I16(-213)
        .Raw(arc)
        .Raw(text)
        .Raw(0x04)
        .Raw(0x04)
        .ToArray();

    /// <summary>Builds a full staged-shape ControlTemplate body over one
    /// custom Binding (for the Converter-attribute/static-id tests).</summary>
    private static byte[] CircleVisualWithBinding(CircleNumbering n, int staticId, int converterAttr) =>
        CircleBodyWith(n, CircleArc(n, "PART_Indicator", "0", -32, bindStroke: false).ToArray(), CircleTextWith(CircleBinding(n, staticId, converterAttr).Raw(0x04).ToArray(), n).ToArray());

    private static B FixtureRect(CircleNumbering n, string? name, bool bindStroke)
    {
        var rect = new B().Raw(0x03).I16(1).Raw(0x00);
        if (name is not null)
            rect.Sized(0x24, q => q.I16((short)n.ArcName).Str(name).I16(-615));
        rect = rect
            .Sized(0x24, q => q.I16((short)n.Stretch).Str("None").I16(-195))
            .Raw(0x23).I16(-57).U16(634).I16((short)n.ActualWidth)
            .Raw(0x23).I16(-47).U16(634).I16((short)n.ActualHeight);
        if (bindStroke)
            rect = rect.Raw(0x23).I16(-101).U16(634).I16(-25);
        return rect
            .Raw(0x23).I16(-100).U16(634).I16(name is null ? (short)-24 : (short)-32)
            .Sized(0x24, q => q.I16(-102).Str("0").I16(-348))
            .Raw(0x04);
    }

    private static B FixtureTextWith(byte[] binding, CircleNumbering n) => new B()
        .Raw(0x03).I16(-638).Raw(0x00)
        .Sized(0x24, q => q.I16(-48).Str("Center").I16(-195))
        .Sized(0x24, q => q.I16(-56).Str("Center").I16(-195))
        .Raw(0x23).I16(-109).U16(634).I16(-32)
        .Raw(0x23).I16(-110).U16(634).I16((short)n.CircleText)
        .Raw(0x07).I16(-135)
        .Raw(binding)
        .Raw(0x08)
        .Raw(0x04);

    private static B FixtureBinding(CircleNumbering n) => new B()
        .Raw(0x03).I16(-20).Raw(0x00)
        .Raw(0x2A)
        .Sized(0x10, q => q.Str("ShowText"))
        .Raw(0x2B)
        .Raw(0x07).I16((short)n.RelativeSource)
        .Raw(0x03).I16(-516).Raw(0x00)
        .Raw(0x2A)
        .Sized(0x10, q => q.Str("TemplatedParent"))
        .Raw(0x2B)
        .Raw(0x04)
        .Raw(0x08)
        .Raw(0x38).U16((ushort)n.Converter).U16(0);

    /// <summary>Builds a single-entry keyed ControlTemplate dictionary over
    /// the circle entry plus the key-block statics (the staged Circle key
    /// is shared). TypeInfo/AttributeInfo/StringInfo rows stand in for the
    /// staged tables; the fixture numbering overload below stands in for
    /// Circle.baml.</summary>
    private static byte[] CircleDictionary(string key, byte[] entry, CircleNumbering n, bool shared = true)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"));
        foreach (var (id, name) in n.Types)
            head.Sized(0x1D, p => p.U16((ushort)id).U16(0).Str(name));
        foreach (var (id, name, owner) in n.Attrs)
            head.Sized(0x1F, p => p.I16((short)id).I16(owner).Raw(0x00).Str(name));
        head.Sized(0x20, p => p.U16(0).Str(key));
        head.Sized(0x20, p => p.U16(1).Str("Boolean2VisibilityConverter"));
        head.Deferable();
        if (shared)
            head.Sized(0x26, p => p.U16(0).U32(0).U16(0));
        else
            head.Sized(0x26, p => p.U16(0).U32(0).Raw(0x00, 0x01));
        head.Raw(0x37, 0x00).U16(1);
        return head.Raw(entry).FinishDictionary().ToArray();
    }

    private static byte[] Header() => new B()
        .Raw(0x0C, 0x00, 0x00, 0x00)
        .Raw(0x4D, 0x00, 0x53, 0x00, 0x42, 0x00, 0x41, 0x00, 0x4D, 0x00, 0x4C, 0x00)
        .U16(0).U16(0x60)
        .Raw(0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00,
             0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00)
        .ToArray();

    private static byte[] ReadStagedGResource(string assemblyShortName, string key)
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, assemblyShortName, StringComparison.OrdinalIgnoreCase));
        foreach (var name in asm.GetManifestResourceNames().Where(name => name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new ResourceReader(stream);
            foreach (System.Collections.DictionaryEntry entry in reader)
            {
                if (entry.Key is string k && k.Replace('\\', '/').Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    if (entry.Value is byte[] raw) return raw;
                    if (entry.Value is Stream s) { using var copy = new MemoryStream(); s.CopyTo(copy); return copy.ToArray(); }
                }
            }
        }
        throw new InvalidOperationException("resource not found: " + key);
    }

    private static string StagedHandyAppsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol");
    }

    private static readonly object StagedGate = new();
    private static Assembly? _stagedEntry;

    private static Assembly LoadStagedHandyControlEntry()
    {
        lock (StagedGate)
        {
            var hit = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "HandyControlDemo", StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                XamlLoader.CustomElementSource = hit;
                return hit;
            }
            if (_stagedEntry is not null)
            {
                XamlLoader.CustomElementSource = _stagedEntry;
                return _stagedEntry;
            }
            var apps = StagedHandyAppsDirectory();
            using var document = JsonDocument.Parse(
                File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
            var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                images[assembly.GetProperty("name").GetString()!] = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
            }
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var requested = new AssemblyName(args.Name).Name;
                var loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, requested, StringComparison.OrdinalIgnoreCase));
                if (loaded is not null) return loaded;
                if (requested is not null && images.TryGetValue(requested, out var image))
                    return Assembly.Load(image);
                return null;
            };
            foreach (var image in images.Values)
                Assembly.Load(image);
            _stagedEntry = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => string.Equals(a.GetName().Name, "HandyControlDemo", StringComparison.OrdinalIgnoreCase));
            XamlLoader.CustomElementSource = _stagedEntry;
            return _stagedEntry;
        }
    }

    private sealed class B
    {
        private readonly List<byte> _b = new();
        private int _deferPos = -1;
        public B Raw(params byte[] b) { _b.AddRange(b); return this; }
        public B Raw(B other) { _b.AddRange(other._b); return this; }
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
        public B Deferable()
        {
            _b.Add(0x25);
            _deferPos = _b.Count;
            _b.AddRange(new byte[4]);
            return this;
        }
        public B FinishDictionary()
        {
            if (_deferPos < 0) throw new InvalidOperationException("no open deferable");
            var endPos = _b.Count;
            _b.Add(0x04);
            var skip = (uint)(endPos - (_deferPos + 4));
            var patch = BitConverter.GetBytes(skip);
            for (var i = 0; i < 4; i++) _b[_deferPos + i] = patch[i];
            _b.Add(0x02);
            _deferPos = -1;
            return this;
        }
        public byte[] ToArray() => _b.ToArray();
    }
}
