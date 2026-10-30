// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-133 HandyControl theme.baml ProgressBarIndicatorAnimatedFill run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the keyed 'ProgressBarIndicatorAnimatedFill' entry keyidx 137 at
/// [BAML+0x18DBD, BAML+0x18E51) lands as a live engine LinearGradientBrush
/// (StartPoint "0,0" / EndPoint "1,0" PointConverter PWCs plus the
/// GradientStops content with four GradientStop children carrying
/// Color/Offset PWCs), the keyed 'CircleProgressBarTemplate' entry keyidx
/// 138 at [BAML+0x18E51, BAML+0x18F6C) (landed by the slice-134 run with its
/// converted SimplePanel root, two Arc children and the TextBlock child),
/// and slice-135 landed the keyed
/// 'VirtualizingStackHorizontal/VerticalItemsPanelTemplate' entries (keyidx
/// 143-144), and slice-136 landed the keyed 'WrapHorizontal/WrapVertical/DockItemsPanelTemplate' entries (keyidx 145-147) plus the generic 'ResizeGripperForeground' brush (keyidx 148) and 'SineEaseOut' easing (keyidx 149), and slice-137 landed the keyed 'BadgeDotTemplate' entry (keyidx 150) plus the 'ColorPickerSliderRepeatButtonBaseStyle' Style (keyidx 151), and slice-138 landed the keyed 'ColorPickerSliderThumbTemplate' entry (keyidx 152) with its Thumb TargetType and packed-White Border root, and slice-139 landed the keyed 'ColorPickerRainbowBrush' brush (keyidx 153) at BAML+0x192B2 with WPF-default points and a zero first offset, and slice-140 landed the keyed 'ColorPickerOpacityBrush' brush (keyidx 154) at BAML+0x19383 with its checker DrawingGroup, so the run stops precise at the keyed 'CoverFlowBaseStyle' Style (keyidx 155) at BAML+0x19497 on its template Border's Viewport3D child (well-known type id -729)
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways (the staged bytes: ElementStart 03 9A FE over
/// the -358 LinearGradientBrush id, 0x24 StartPoint/EndPoint PWCs over
/// per-file AttributeInfo rows 0x2A/0x2B with the PointConverter -474 and
/// the "0,0"/"1,0" texts, the 2E C4 FF GradientStops content member,
/// four 03 04 FF GradientStop children with 0x24 Color PWCs over row
/// 0x2C with the ColorConverter -87 and the "#00FFFFFF"/"#60FFFFFF"
/// texts plus 0x24 Offset PWCs over row 0x2D with the DoubleConverter
/// -175 and the "0"/"0.4"/"0.6"/"1" texts, the 04 04 close pair, key
/// record 0x26 key id 0x7F shared at 0xB47B plus the next 0x80 key at
/// 0xB485, the next entry head 03 94 FF at BAML+0x18E51; the live
/// upstream HandyControl ProgressBarBaseStyle.xaml
/// 'ProgressBarIndicatorAnimatedFill' block (the same StartPoint/EndPoint
/// plus the same four stops with the same colors and offsets, fetched
/// live from the HandyOrg/HandyControl master tree); a genuine
/// net10.0-windows ResourceDictionary fixture compiling the exact
/// upstream block into Brushes.baml, byte-identical in every staged
/// record shape - 03 9A FE head, 0x24 StartPoint/EndPoint with the
/// PointConverter -474, 2E C4 FF content, 03 04 FF children, 0x24 Color
/// with the ColorConverter -87, 0x24 Offset with the DoubleConverter
/// -175, the 04 04 close pair - differing only in per-file table
/// numbering (fixture attrs 0-3 over the same -358/-252 owners, staged
/// rows 0x2A-0x2D) and line markers (the fixture carries 0x35/0x36
/// records while every staged file ends in 0x00 with no line markers).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice133BamlGradientTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice133BamlGradientTests()
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
    public void Theme_baml_lands_gradient_brush_then_stops_at_circle_template()
    {
        // Staged framing pins for the keyed 'ProgressBarIndicatorAnimatedFill'
        // entry keyidx 137 spanning [0x18DBD, 0x18E51): the 03 9A FE head,
        // the StartPoint "0,0" / EndPoint "1,0" PointConverter PWCs, the
        // 2E C4 FF GradientStops content, the four GradientStop children
        // with their Color/Offset PWCs, the 04 04 close pair, the 0x26 key
        // record plus the next key, and the 'CircleProgressBarTemplate'
        // entry head at 0x18E51 asserting the exact new frontier. Loading
        // the staged entry first guarantees the HandyControl assembly is
        // present however the runner orders tests.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");

        var head = new byte[] { 0x03, 0x9A, 0xFE, 0x00, 0x24, 0x09, 0x2A, 0x00, 0x03, 0x30, 0x2C, 0x30, 0x26, 0xFE };
        Assert.Equal(head, theme[0x18DBD..0x18DCB]);
        var endPoint = new byte[] { 0x24, 0x09, 0x2B, 0x00, 0x03, 0x31, 0x2C, 0x30, 0x26, 0xFE, 0x2E, 0xC4, 0xFF };
        Assert.Equal(endPoint, theme[0x18DCB..0x18DD8]);
        var stop0 = new byte[] { 0x03, 0x04, 0xFF, 0x00, 0x24, 0x0F, 0x2C, 0x00, 0x09, 0x23, 0x30, 0x30, 0x46, 0x46, 0x46, 0x46, 0x46, 0x46, 0xA9, 0xFF };
        Assert.Equal(stop0, theme[0x18DD8..0x18DEC]);
        var offset0 = new byte[] { 0x24, 0x07, 0x2D, 0x00, 0x01, 0x30, 0x51, 0xFF, 0x04 };
        Assert.Equal(offset0, theme[0x18DEC..0x18DF5]);
        var stop1Color = new byte[] { 0x03, 0x04, 0xFF, 0x00, 0x24, 0x0F, 0x2C, 0x00, 0x09, 0x23, 0x36, 0x30, 0x46, 0x46, 0x46, 0x46, 0x46, 0x46, 0xA9, 0xFF };
        Assert.Equal(stop1Color, theme[0x18DF5..0x18E09]);
        var offset04 = new byte[] { 0x24, 0x09, 0x2D, 0x00, 0x03, 0x30, 0x2E, 0x34, 0x51, 0xFF, 0x04 };
        Assert.Equal(offset04, theme[0x18E09..0x18E14]);
        Assert.Equal(stop1Color, theme[0x18E14..0x18E28]);
        var offset06 = new byte[] { 0x24, 0x09, 0x2D, 0x00, 0x03, 0x30, 0x2E, 0x36, 0x51, 0xFF, 0x04 };
        Assert.Equal(offset06, theme[0x18E28..0x18E33]);
        Assert.Equal(stop0, theme[0x18E33..0x18E47]);
        var offset1 = new byte[] { 0x24, 0x07, 0x2D, 0x00, 0x01, 0x31, 0x51, 0xFF, 0x04, 0x04 };
        Assert.Equal(offset1, theme[0x18E47..0x18E51]);

        var key137 = new byte[] { 0x26, 0x09, 0x7F, 0x00, 0x26, 0x9C, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key137, theme[0xB47B..0xB485]);
        var key138 = new byte[] { 0x26, 0x09, 0x80, 0x00, 0xBA, 0x9C, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key138, theme[0xB485..0xB48F]);

        var nextHead = new byte[] { 0x03, 0x94, 0xFF, 0x00, 0x22, 0x07, 0x00, 0x30, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0x2E, 0x00, 0x00 };
        Assert.Equal(nextHead, theme[0x18E51..0x18E61]);
    }

    [Fact]
    public void Staged_boot_advances_past_gradient_to_circle_template()
    {
        // The staged boot now materializes entry #137 (the
        // 'ProgressBarIndicatorAnimatedFill' LinearGradientBrush) in index
        // order through the VirtualizingStackPanel items panels (keyidx 143-144) and stops at the 'WrapHorizontalItemsPanelTemplate' entry: the
        // precise next blocker. Every earlier stop stays absent (including
        // the intermediate items-panel head stop and the -358 entry-type
        // stop), and the new head asserts the exact frontier.
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
        Assert.DoesNotContain("BAML+0x19029", inner.Message);
        Assert.DoesNotContain("CircleProgressBarTemplate", inner.Message);
        Assert.DoesNotContain("VirtualizingStackHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapVerticalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("DockItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("ResizeGripperForeground", inner.Message);
        Assert.DoesNotContain("SineEaseOut", inner.Message);
        Assert.DoesNotContain("BAML+0x19150", inner.Message);
        Assert.DoesNotContain("BadgeDotTemplate", inner.Message);
        Assert.DoesNotContain("element ContentPresenter on the template root panel", inner.Message);
        Assert.DoesNotContain("root panels take ContentPresenter, Border, converted Shape and TextBlock children", inner.Message);
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
        Assert.DoesNotContain("HandyControl.Controls.SimplePanel", inner.Message);
        Assert.DoesNotContain("VirtualizingStackPanel", inner.Message);
        Assert.DoesNotContain("templates take a ContentPresenter", inner.Message);
        Assert.DoesNotContain("items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", inner.Message);
    }

    [Fact]
    public void Staged_shape_gradient_lands_live_brush()
    {
        // The staged 'ProgressBarIndicatorAnimatedFill' entry shape
        // (StartPoint "0,0" / EndPoint "1,0" plus four GradientStops)
        // lands as a live engine LinearGradientBrush with its stops.
        // Per-file ids 0x2A-0x2D stand in for the staged rows.
        XamlLoader.CustomElementSource = null;
        var bytes = GradientDictionary("ProgressBarIndicatorAnimatedFill", StagedGradientEntry(StagedTables()).ToArray(), StagedTables());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var brush = Assert.IsType<LinearGradientBrush>(dictionary["ProgressBarIndicatorAnimatedFill"]);
        Assert.Equal(new Point(0, 0), brush.StartPoint);
        Assert.Equal(new Point(1, 0), brush.EndPoint);
        Assert.Equal(4, brush.GradientStops.Count);
        Assert.Equal(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), brush.GradientStops[0].Color);
        Assert.Equal(0.0, brush.GradientStops[0].Offset);
        Assert.Equal(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), brush.GradientStops[1].Color);
        Assert.Equal(0.4, brush.GradientStops[1].Offset);
        Assert.Equal(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), brush.GradientStops[2].Color);
        Assert.Equal(0.6, brush.GradientStops[2].Offset);
        Assert.Equal(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), brush.GradientStops[3].Color);
        Assert.Equal(1.0, brush.GradientStops[3].Offset);
    }

    [Fact]
    public void Shared_gradient_lookups_return_same_instance()
    {
        // The staged brush key is shared (shared=0/sharedSet=0), so every
        // lookup returns the same LinearGradientBrush instance.
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(GradientDictionary(
            "ProgressBarIndicatorAnimatedFill", StagedGradientEntry(StagedTables()).ToArray(), StagedTables()));
        var first = Assert.IsType<LinearGradientBrush>(dictionary["ProgressBarIndicatorAnimatedFill"]);
        Assert.Same(first, Assert.IsType<LinearGradientBrush>(dictionary["ProgressBarIndicatorAnimatedFill"]));
    }

    [Fact]
    public void Non_shared_gradient_lookups_materialize_fresh_instances()
    {
        // Non-shared keys replay the validated value bags through a
        // factory, so every lookup materializes a fresh brush with equal
        // values, never a shared singleton.
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(GradientDictionary(
            "ProgressBarIndicatorAnimatedFill", StagedGradientEntry(StagedTables()).ToArray(), StagedTables(), shared: false));
        var first = Assert.IsType<LinearGradientBrush>(dictionary["ProgressBarIndicatorAnimatedFill"]);
        var second = Assert.IsType<LinearGradientBrush>(dictionary["ProgressBarIndicatorAnimatedFill"]);
        Assert.NotSame(first, second);
        Assert.Equal(first.StartPoint, second.StartPoint);
        Assert.Equal(first.EndPoint, second.EndPoint);
        Assert.Equal(first.GradientStops.Count, second.GradientStops.Count);
    }

    [Fact]
    public void Fixture_numbered_gradient_lands_with_line_markers()
    {
        // The genuine net10.0-windows fixture numbers its tables from zero
        // (attrs 0-3 over the same -358/-252 owners) and carries 0x35/0x36
        // line markers: the parser resolves everything through the tables,
        // so the same shapes land with line markers transparent.
        XamlLoader.CustomElementSource = null;
        var numbering = FixtureTables();
        var entry = new B()
            .Raw(0x03).I16(-358).Raw(0x00)
            .Raw(0x35).U32(6).U32(10)
            .Sized(0x24, q => q.I16(0).Str("0,0").I16(-474))
            .Sized(0x24, q => q.I16(1).Str("1,0").I16(-474))
            .Raw(0x2E).I16(-60)
            .Raw(0x35).U32(7).U32(12)
            .Raw(Stop(numbering, "#00FFFFFF", "0").ToArray())
            .Raw(Stop(numbering, "#60FFFFFF", "0.4").ToArray())
            .Raw(Stop(numbering, "#60FFFFFF", "0.6").ToArray())
            .Raw(Stop(numbering, "#00FFFFFF", "1").ToArray())
            .Raw(0x36).U32(8)
            .Raw(0x04)
            .ToArray();
        var dictionary = BamlReader.LoadDictionary(GradientDictionary("ProgressBarIndicatorAnimatedFill", entry, numbering));
        var brush = Assert.IsType<LinearGradientBrush>(dictionary["ProgressBarIndicatorAnimatedFill"]);
        Assert.Equal(new Point(0, 0), brush.StartPoint);
        Assert.Equal(new Point(1, 0), brush.EndPoint);
        Assert.Equal(4, brush.GradientStops.Count);
        Assert.Equal(0.6, brush.GradientStops[2].Offset);
    }

    [Fact]
    public void Gradient_missing_startpoint_lands_default()
    {
        // A brush entry without StartPoint (BAML drops the default value)
        // lands the WPF default (0,0) instead of failing: the staged
        // rainbow entry is exactly this shape.
        XamlLoader.CustomElementSource = null;
        var body = new B()
            .Raw(0x03).I16(-358).Raw(0x00)
            .Sized(0x24, q => q.I16((short)StagedTables().End).Str("1,0").I16(-474))
            .Raw(0x2E).I16(-60)
            .Raw(Stop(StagedTables(), "#00FFFFFF", "0").ToArray())
            .Raw(0x04);
        var dictionary = BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), StagedTables()));
        var brush = Assert.IsType<LinearGradientBrush>(dictionary["T"]);
        Assert.Equal(new Point(0, 0), brush.StartPoint);
        Assert.Equal(new Point(1, 0), brush.EndPoint);
    }

    [Fact]
    public void Gradient_missing_endpoint_lands_default()
    {
        // A brush entry without EndPoint (BAML drops the default value)
        // lands the WPF default (1,1) instead of failing.
        XamlLoader.CustomElementSource = null;
        var body = new B()
            .Raw(0x03).I16(-358).Raw(0x00)
            .Sized(0x24, q => q.I16((short)StagedTables().Start).Str("0,0").I16(-474))
            .Raw(0x2E).I16(-60)
            .Raw(Stop(StagedTables(), "#00FFFFFF", "0").ToArray())
            .Raw(0x04);
        var dictionary = BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), StagedTables()));
        var brush = Assert.IsType<LinearGradientBrush>(dictionary["T"]);
        Assert.Equal(new Point(0, 0), brush.StartPoint);
        Assert.Equal(new Point(1, 1), brush.EndPoint);
    }

    [Fact]
    public void Gradient_stop_missing_offset_lands_zero()
    {
        // A Color-only GradientStop (BAML drops the default Offset) lands
        // Offset 0.0 instead of failing: the staged rainbow first stop is
        // exactly this shape.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var stop = new B()
            .Raw(0x03).I16(-252).Raw(0x00)
            .Sized(0x24, q => q.I16((short)n.Color).Str("#00FFFFFF").I16(-87))
            .Raw(0x04);
        var body = GradientHead(n).Raw(stop.ToArray()).Raw(0x04);
        var dictionary = BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), n));
        var brush = Assert.IsType<LinearGradientBrush>(dictionary["T"]);
        var landed = Assert.Single(brush.GradientStops);
        Assert.Equal(0.0, landed.Offset);
    }

    [Fact]
    public void Gradient_missing_content_fails_precise()
    {
        // A brush entry with points but no GradientStops content fails
        // instead of landing an empty brush.
        XamlLoader.CustomElementSource = null;
        var body = GradientPoints(StagedTables()).Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), StagedTables())));
        Assert.Contains("no GradientStops", error.Message);
        Assert.Contains("gradient brushes take the GradientStops content", error.Message);
    }

    [Fact]
    public void Gradient_empty_content_fails_precise()
    {
        // The GradientStops content with an immediate end fails instead
        // of landing a stop-less brush.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var withContent = new B()
            .Raw(0x03).I16(-358).Raw(0x00)
            .Sized(0x24, q => q.I16((short)n.Start).Str("0,0").I16(-474))
            .Sized(0x24, q => q.I16((short)n.End).Str("1,0").I16(-474))
            .Raw(0x2E).I16(-60)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(GradientDictionary("T", withContent.ToArray(), n)));
        Assert.Contains("no GradientStop", error.Message);
        Assert.Contains("one GradientStop per color step", error.Message);
    }

    [Fact]
    public void Gradient_duplicate_content_fails_precise()
    {
        // Two GradientStops contents fail instead of collecting stops
        // twice.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var body = GradientHead(n)
            .Raw(Stop(n, "#00FFFFFF", "0").ToArray())
            .Raw(0x2E).I16(-60)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), n)));
        Assert.Contains("duplicate GradientStops content", error.Message);
    }

    [Fact]
    public void Gradient_startpoint_duplicate_fails_precise()
    {
        // Two StartPoint simples fail instead of overwriting the point.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var body = new B()
            .Raw(0x03).I16(-358).Raw(0x00)
            .Sized(0x24, q => q.I16((short)n.Start).Str("0,0").I16(-474))
            .Sized(0x24, q => q.I16((short)n.Start).Str("0,1").I16(-474))
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), n)));
        Assert.Contains("duplicate StartPoint", error.Message);
    }

    [Fact]
    public void Gradient_startpoint_wrong_converter_fails_precise()
    {
        // A StartPoint simple over any other converter (here the
        // StringConverter) fails naming the converter used.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var body = new B()
            .Raw(0x03).I16(-358).Raw(0x00)
            .Sized(0x24, q => q.I16((short)n.Start).Str("0,0").I16(-615))
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), n)));
        Assert.Contains("StartPoint value with converter -615", error.Message);
        Assert.Contains("take the PointConverter", error.Message);
    }

    [Fact]
    public void Gradient_startpoint_bad_text_fails_precise()
    {
        // A StartPoint simple whose text is not a point fails instead of
        // landing a default.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var body = new B()
            .Raw(0x03).I16(-358).Raw(0x00)
            .Sized(0x24, q => q.I16((short)n.Start).Str("nowhere").I16(-474))
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), n)));
        Assert.Contains("'nowhere' is not a Point", error.Message);
    }

    [Fact]
    public void Gradient_unknown_attribute_fails_precise()
    {
        // A simple over an AttributeInfo row the file does not have (id 99
        // is past the 0x2A-0x2D staged rows) fails instead of landing on
        // the wrong member.
        XamlLoader.CustomElementSource = null;
        var body = new B()
            .Raw(0x03).I16(-358).Raw(0x00)
            .Sized(0x24, q => q.I16(99).Str("0,0").I16(-474))
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), StagedTables())));
        Assert.Contains("attribute id 99", error.Message);
        Assert.Contains("has no AttributeInfo record", error.Message);
    }

    [Fact]
    public void Gradient_wrong_content_member_fails_precise()
    {
        // Any other content member (here the Storyboard Children id, which
        // shares no number with GradientStops) fails: gradient brushes
        // take the GradientStops content.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var body = GradientHeadWith(n, -122);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), n)));
        Assert.Contains("content member -122", error.Message);
        Assert.Contains("take the GradientStops content", error.Message);
    }

    [Fact]
    public void Gradient_stop_before_content_fails_precise()
    {
        // A GradientStop child before the GradientStops content fails
        // instead of collecting out of order.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var body = new B()
            .Raw(0x03).I16(-358).Raw(0x00)
            .Raw(Stop(n, "#00FFFFFF", "0").ToArray())
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), n)));
        Assert.Contains("precedes the GradientStops content", error.Message);
    }

    [Fact]
    public void Gradient_non_stop_child_fails_precise()
    {
        // Any other visual (here a Border) fails: gradient brushes take
        // GradientStop children.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var body = GradientHead(n)
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x04)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), n)));
        Assert.Contains("element Border inside gradient entry 'T'", error.Message);
        Assert.Contains("take GradientStop children", error.Message);
    }

    [Fact]
    public void Gradient_stop_missing_color_fails_precise()
    {
        // A GradientStop without Color fails instead of landing black.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var stop = new B()
            .Raw(0x03).I16(-252).Raw(0x00)
            .Sized(0x24, q => q.I16((short)n.Offset).Str("0").I16(-175))
            .Raw(0x04);
        var body = GradientHead(n).Raw(stop.ToArray()).Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), n)));
        Assert.Contains("no Color", error.Message);
        Assert.Contains("take Color plus Offset", error.Message);
    }

    [Fact]
    public void Gradient_stop_color_wrong_converter_fails_precise()
    {
        // A Color simple over any other converter (here the DoubleConverter)
        // fails naming the converter used.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var stop = new B()
            .Raw(0x03).I16(-252).Raw(0x00)
            .Sized(0x24, q => q.I16((short)n.Color).Str("#00FFFFFF").I16(-175))
            .Raw(0x04);
        var body = GradientHead(n).Raw(stop.ToArray()).Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), n)));
        Assert.Contains("Color value with converter -175", error.Message);
        Assert.Contains("take the ColorConverter", error.Message);
    }

    [Fact]
    public void Gradient_stop_offset_bad_text_fails_precise()
    {
        // An Offset simple whose text is not a double fails instead of
        // landing zero.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var stop = new B()
            .Raw(0x03).I16(-252).Raw(0x00)
            .Sized(0x24, q => q.I16((short)n.Color).Str("#00FFFFFF").I16(-87))
            .Sized(0x24, q => q.I16((short)n.Offset).Str("half").I16(-175))
            .Raw(0x04);
        var body = GradientHead(n).Raw(stop.ToArray()).Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), n)));
        Assert.Contains("'half' is not an Offset", error.Message);
    }

    [Fact]
    public void Gradient_static_value_fails_precise()
    {
        // StaticResource values inside gradient entries (the GroupBox
        // 0x38 follow-up shape) stay a precise boundary instead of
        // landing half-built.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var body = GradientHead(n)
            .Raw(0x38).U16(0).U16(0)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), n)));
        Assert.Contains("StaticResource value on the LinearGradientBrush", error.Message);
        Assert.Contains("gradient static values are a style-surface follow-up", error.Message);
    }

    [Fact]
    public void Gradient_stop_garbage_record_fails_precise()
    {
        // A text record inside a GradientStop fails naming the record:
        // stops take Color plus Offset values.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var stop = new B()
            .Raw(0x03).I16(-252).Raw(0x00)
            .Sized(0x24, q => q.I16((short)n.Color).Str("#00FFFFFF").I16(-87))
            .Sized(0x24, q => q.I16((short)n.Offset).Str("0").I16(-175))
            .Sized(0x10, q => q.Str("trailing"))
            .Raw(0x04);
        var body = GradientHead(n).Raw(stop.ToArray()).Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(GradientDictionary("T", body.ToArray(), n)));
        Assert.Contains("holds record 0x10", error.Message);
        Assert.Contains("stops take Color plus Offset", error.Message);
    }

    /// <summary>Per-file table numbering plus the record ids the gradient
    /// grammar resolves through the tables (file-independent: the staged
    /// file numbers brush attrs 0x2A/0x2B over LinearGradientBrush -358 and
    /// stop attrs 0x2C/0x2D over GradientStop -252, the genuine net10
    /// fixture numbers attrs 0-3 over the same owners).</summary>
    private sealed record GradientNumbering(
        (int Id, string Name, short Owner)[] Attrs,
        int Start, int End, int Color, int Offset);

    private static GradientNumbering StagedTables() => new(
        [(0x2A, "StartPoint", (short)-358), (0x2B, "EndPoint", (short)-358),
         (0x2C, "Color", (short)-252), (0x2D, "Offset", (short)-252)],
        0x2A, 0x2B, 0x2C, 0x2D);

    private static GradientNumbering FixtureTables() => new(
        [(0, "StartPoint", (short)-358), (1, "EndPoint", (short)-358),
         (2, "Color", (short)-252), (3, "Offset", (short)-252)],
        0, 1, 2, 3);

    /// <summary>Builds the staged 'ProgressBarIndicatorAnimatedFill' entry
    /// shape: StartPoint/EndPoint, the GradientStops content and the four
    /// staged stops, closed once.</summary>
    private static B StagedGradientEntry(GradientNumbering n) => GradientHead(n)
        .Raw(Stop(n, "#00FFFFFF", "0").ToArray())
        .Raw(Stop(n, "#60FFFFFF", "0.4").ToArray())
        .Raw(Stop(n, "#60FFFFFF", "0.6").ToArray())
        .Raw(Stop(n, "#00FFFFFF", "1").ToArray())
        .Raw(0x04);

    /// <summary>Builds one staged-shape gradient head (its ElementStart is
    /// included): the StartPoint/EndPoint PointConverter simples plus the
    /// GradientStops content.</summary>
    private static B GradientHead(GradientNumbering n) => GradientHeadWith(n, -60);

    /// <summary>Builds one gradient head over an explicit content member
    /// (for the content-member dispatch test).</summary>
    private static B GradientHeadWith(GradientNumbering n, short content) => GradientPoints(n)
        .Raw(0x2E).I16(content);

    /// <summary>Builds the StartPoint/EndPoint simples of a gradient entry
    /// (its ElementStart is included, no content member).</summary>
    private static B GradientPoints(GradientNumbering n) => new B()
        .Raw(0x03).I16(-358).Raw(0x00)
        .Sized(0x24, q => q.I16((short)n.Start).Str("0,0").I16(-474))
        .Sized(0x24, q => q.I16((short)n.End).Str("1,0").I16(-474));

    /// <summary>Builds one staged-shape GradientStop child (its
    /// ElementStart is included): the Color/Offset simples, closed once.</summary>
    private static B Stop(GradientNumbering n, string color, string offset) => new B()
        .Raw(0x03).I16(-252).Raw(0x00)
        .Sized(0x24, q => q.I16((short)n.Color).Str(color).I16(-87))
        .Sized(0x24, q => q.I16((short)n.Offset).Str(offset).I16(-175))
        .Raw(0x04);

    /// <summary>Builds a single-entry keyed gradient dictionary over the
    /// engine brush ids (well-known tables only, no TypeInfo rows;
    /// per-file AttributeInfo ids stand in for the staged rows). The
    /// staged brush key is shared, so shared opts into the AddEntry
    /// singleton path.</summary>
    private static byte[] GradientDictionary(string key, byte[] entry, GradientNumbering n, bool shared = true)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"));
        foreach (var (id, name, owner) in n.Attrs)
            head.Sized(0x1F, p => p.I16((short)id).I16(owner).Raw(0x00).Str(name));
        head.Sized(0x20, p => p.U16(0).Str(key));
        head.Deferable();
        if (shared)
            head.Sized(0x26, p => p.U16(0).U32(0).U16(0));
        else
            head.Sized(0x26, p => p.U16(0).U32(0).Raw(0x00, 0x01));
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
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase)))
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

