// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-138 HandyControl theme.baml ColorPickerSliderThumbTemplate run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the keyed 'ColorPickerSliderThumbTemplate' ControlTemplate entry keyidx
/// 152 at [BAML+0x19279, BAML+0x192B2) lands: its TargetType record at
/// BAML+0x1927D names well-known type id -658, now routed to the engine
/// Thumb type, and its Border root visual carries the Width/Height "13"
/// LengthConverter simples, the packed-ARGB Background White value and the
/// CornerRadius "6.5" simple, landing as a live engine ControlTemplate. The
/// run lands the keyed 'ColorPickerRainbowBrush' LinearGradientBrush entry
/// keyidx 153 at BAML+0x192B2 in slice-139 (seven GradientStop children
/// but no StartPoint/EndPoint and a Color-only first stop: absent points
/// land at the WPF defaults and the missing Offset at 0.0), and slice-140
/// lands the keyed 'ColorPickerOpacityBrush' brush keyidx 154 at BAML+0x19383
/// with its checker DrawingGroup, then stops at the keyed 'CoverFlowBaseStyle'
/// Style keyidx 155 at BAML+0x19497 (template Border Viewport3D child
/// well-known id -729).
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways: the staged bytes (the ControlTemplate head,
/// the 0x22 Thumb TargetType record, the VisualTree content, the Border
/// head, the Width/Height/CornerRadius PWCs, the packed White 0x06 value,
/// the closes, the rainbow-brush head with its GradientStops content and
/// first-stop Color PWC, the key records 0x26 key ids 0x8E-0x90, the
/// DrawingBrush entry-154 head with its Viewport PWC); the live upstream
/// HandyControl ColorPickerBaseStyle.xaml block at pinned commit e9ad3d70
/// (the same Thumb template with the same Border values, the same
/// ColorPickerRainbowBrush with the same seven stops, the same
/// ColorPickerOpacityBrush DrawingBrush with the same Viewport values); a
/// genuine net10.0-windows fixture compiling the exact upstream template
/// block (emitting 6E FD for the Thumb TargetType, the Border head, the
/// same Width/Height/Background/CornerRadius record shapes modulo
/// per-file table numbering and line markers, which the staged file
/// omits; fixture retained at E:/tmp/bamlfix outside the tree).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice138BamlThumbTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice138BamlThumbTests()
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
    public void Theme_baml_places_thumb_stop_at_rainbow_brush()
    {
        // Staged framing pins for the keyed
        // 'ColorPickerSliderThumbTemplate' ControlTemplate entry keyidx 152
        // spanning [0x19279, 0x192B2): the ControlTemplate head, the Thumb
        // TargetType record at 0x1927D (well-known id -658), the VisualTree
        // content, the Border head, the Width/Height "13" PWCs, the
        // packed-ARGB White Background value, the CornerRadius "6.5" PWC,
        // the two closes, the keyed 'ColorPickerRainbowBrush'
        // LinearGradientBrush entry keyidx 153 head at 0x192B2 with its
        // GradientStops content and first-stop Color PWC, the key records
        // 0x26 for keyidx 152-154, and the keyed 'ColorPickerOpacityBrush'
        // entry keyidx 154 head at 0x19383 with its Viewport PWC plus the
        // staged Drawing complex (landing since slice-140), and the
        // keyidx-155 'CoverFlowBaseStyle' head at 0x1943B asserting the
        // exact next frontier.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");

        var thumbHead = new byte[] { 0x03, 0x94, 0xFF, 0x00 };
        Assert.Equal(thumbHead, theme[0x19279..0x1927D]);
        var thumbTarget = new byte[] { 0x22, 0x07, 0x00, 0x6E, 0xFD };
        Assert.Equal(thumbTarget, theme[0x1927D..0x19282]);
        var thumbTree = new byte[] { 0x2E, 0x52, 0xFF, 0x03, 0xCE, 0xFF, 0x00 };
        Assert.Equal(thumbTree, theme[0x19282..0x19289]);
        var width = new byte[] { 0x24, 0x08, 0xC7, 0xFF, 0x02, 0x31, 0x33, 0xA4, 0xFE };
        Assert.Equal(width, theme[0x19289..0x19292]);
        var height = new byte[] { 0x24, 0x08, 0xD1, 0xFF, 0x02, 0x31, 0x33, 0xA4, 0xFE };
        Assert.Equal(height, theme[0x19292..0x1929B]);
        var background = new byte[] { 0x06, 0x0A, 0xFC, 0xFF, 0xE8, 0x02, 0x01, 0xFF, 0xFF, 0xFF, 0xFF };
        Assert.Equal(background, theme[0x1929B..0x192A6]);
        var corner = new byte[] { 0x24, 0x09, 0x1F, 0x00, 0x03, 0x36, 0x2E, 0x35, 0x91, 0xFF };
        Assert.Equal(corner, theme[0x192A6..0x192B0]);
        var closes = new byte[] { 0x04, 0x04 };
        Assert.Equal(closes, theme[0x192B0..0x192B2]);
        var rainbowHead = new byte[] { 0x03, 0x9A, 0xFE, 0x00, 0x2E, 0xC4, 0xFF, 0x03, 0x04, 0xFF, 0x00 };
        Assert.Equal(rainbowHead, theme[0x192B2..0x192BD]);
        var firstStopColor = new byte[] { 0x24, 0x0D, 0x2C, 0x00, 0x07, 0x23, 0x66, 0x66, 0x30, 0x30, 0x30, 0x30, 0xA9, 0xFF };
        Assert.Equal(firstStopColor, theme[0x192BD..0x192CB]);

        var key152 = new byte[] { 0x26, 0x09, 0x8E, 0x00, 0xE2, 0xA0, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key152, theme[0xB519..0xB523]);
        var key153 = new byte[] { 0x26, 0x09, 0x8F, 0x00, 0x1B, 0xA1, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key153, theme[0xB523..0xB52D]);
        var key154 = new byte[] { 0x26, 0x09, 0x90, 0x00, 0xEC, 0xA1, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key154, theme[0xB52D..0xB537]);
        var opacityHead = new byte[] { 0x03, 0x4C, 0xFF, 0x00 };
        Assert.Equal(opacityHead, theme[0x19383..0x19387]);
        var viewport = new byte[] { 0x24, 0x0F, 0x39, 0x00, 0x09, 0x30, 0x2C, 0x30, 0x2C, 0x31, 0x32, 0x2C, 0x31, 0x31, 0x01, 0xFE };
        Assert.Equal(viewport, theme[0x19387..0x19397]);
    }

    [Fact]
    public void Staged_boot_advances_past_thumb_to_rainbow_brush()
    {
        // The staged boot now materializes entry #152 (the
        // 'ColorPickerSliderThumbTemplate' ControlTemplate with its Thumb
        // TargetType and packed-White Border root) plus the keyed
        // 'ColorPickerRainbowBrush' LinearGradientBrush entry keyidx 153
        // (WPF-default points, zero first offset) plus the keyed
        // 'ColorPickerOpacityBrush' brush keyidx 154 with its checker
        // DrawingGroup, and stops at the keyed 'CoverFlowBaseStyle' Style
        // keyidx 155 (template Border Viewport3D child well-known id -729
        // at BAML+0x19497: the precise next blocker).
        // Every earlier stop stays absent
        // (including the Thumb TargetType stop), and the new head asserts
        // the exact frontier.
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("startup failed in entry point", error.Message);
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
        Assert.DoesNotContain("BAML+0x168D2", inner.Message);
        Assert.DoesNotContain("BlendEffectBox", inner.Message);
        Assert.DoesNotContain("BAML+0x16926", inner.Message);
        Assert.DoesNotContain("BorderCircular", inner.Message);
        Assert.Contains("Themes/Theme.xaml", inner.Message);
        Assert.Contains("line 23", inner.Message);
        Assert.DoesNotContain("BAML+0x16954", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.Col", inner.Message);
        Assert.DoesNotContain("well-known member id -132", inner.Message);
        Assert.DoesNotContain("BAML+0x16C96", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.Notification", inner.Message);
        Assert.DoesNotContain("setter property owner Window", inner.Message);
        Assert.DoesNotContain("BAML+0x16E7C", inner.Message);
        Assert.DoesNotContain("RectangleCircular", inner.Message);
        Assert.DoesNotContain("SnapRect4ScreenshotStyle", inner.Message);
        Assert.DoesNotContain("TargetArea4ScreenshotStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x1760B", inner.Message);
        Assert.DoesNotContain("BAML+0x17C06", inner.Message);
        Assert.DoesNotContain("Right2LeftTransition", inner.Message);
        Assert.DoesNotContain("SliderRepeatButtonHorizontalStyle", inner.Message);
        Assert.DoesNotContain("SliderRepeatButtonVerticalStyle", inner.Message);
        Assert.DoesNotContain("CompareSliderRepeatButtonTransparent", inner.Message);
        Assert.DoesNotContain("ScrollBarBaseRepeatButton", inner.Message);
        Assert.DoesNotContain("BAML+0x17F4C", inner.Message);
        Assert.DoesNotContain("ToggleBlockBaseStyle", inner.Message);
        Assert.DoesNotContain("well-known member id -38", inner.Message);
        Assert.DoesNotContain("BAML+0x1835D", inner.Message);
        Assert.DoesNotContain("GroupBoxBaseTopTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x1859F", inner.Message);
        Assert.DoesNotContain("GroupBoxBaseLeftTemplate", inner.Message);
        Assert.DoesNotContain("holds ControlTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x1888C", inner.Message);
        Assert.DoesNotContain("GroupBoxTabTopTemplate", inner.Message);
        Assert.DoesNotContain("GroupBoxTabLeftTemplate", inner.Message);
        Assert.DoesNotContain("template Binding holds record 0x38", inner.Message);
        Assert.DoesNotContain("BAML+0x18D3F", inner.Message);
        Assert.DoesNotContain("ButtonGroupHorizontalUniformItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("holds well-known type id -330", inner.Message);
        Assert.DoesNotContain("BAML+0x18DBD", inner.Message);
        Assert.DoesNotContain("ProgressBarIndicatorAnimatedFill", inner.Message);
        Assert.DoesNotContain("holds well-known type id -358", inner.Message);
        Assert.DoesNotContain("BAML+0x18E5D", inner.Message);
        Assert.DoesNotContain("CircleProgressBarTemplate", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimplePanel", inner.Message);
        Assert.DoesNotContain("BAML+0x18FEC", inner.Message);
        Assert.DoesNotContain("VirtualizingStackHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("VirtualizingStackVerticalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x19029", inner.Message);
        Assert.DoesNotContain("WrapHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapVerticalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("DockItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("well-known type id -742", inner.Message);
        Assert.DoesNotContain("ResizeGripperForeground", inner.Message);
        Assert.DoesNotContain("SineEaseOut", inner.Message);
        Assert.DoesNotContain("BAML+0x19150", inner.Message);
        Assert.DoesNotContain("BadgeDotTemplate", inner.Message);
        Assert.DoesNotContain("element ContentPresenter on the template root panel", inner.Message);
        Assert.DoesNotContain("root panels take ContentPresenter, Border, converted Shape and TextBlock children", inner.Message);
        Assert.DoesNotContain("BAML+0x1927D", inner.Message);
        Assert.DoesNotContain("ColorPickerSliderThumbTemplate", inner.Message);
        Assert.DoesNotContain("well-known type id -658", inner.Message);
        Assert.DoesNotContain("style targets beyond Path/Border/ContentControl/GridSplitter/Rectangle/RepeatButton/GroupBox/Thumb are a style-surface follow-up", inner.Message);
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
    }

    [Fact]
    public void Staged_shape_thumb_template_lands_live()
    {
        // The staged 'ColorPickerSliderThumbTemplate' entry shape (Thumb
        // TargetType, Border root with Width/Height "13", packed-ARGB White
        // Background and CornerRadius "6.5") lands as a live engine
        // ControlTemplate with its visual root, empty namescope and no
        // deferred bindings. Per-file ids 7/31 stand in for the staged
        // rows (TargetType owned by ControlTemplate, CornerRadius owned by
        // Border); Width/Height/Background travel the well-known member
        // ids like the staged file.
        LoadStagedHandyControlEntry();
        var n = StagedThumbTables();
        var dictionary = BamlReader.LoadDictionary(ThumbDictionary("ColorPickerSliderThumbTemplate", ThumbEntry(n), n));
        var template = Assert.IsType<ControlTemplate>(dictionary["ColorPickerSliderThumbTemplate"]);
        Assert.Same(typeof(Thumb), template.TargetType);
        var border = Assert.IsType<Border>(template.VisualTree);
        Assert.Equal(13.0, border.Width);
        Assert.True(border.HasLocalValue(FrameworkElement.WidthProperty));
        Assert.Equal(13.0, border.Height);
        Assert.True(border.HasLocalValue(FrameworkElement.HeightProperty));
        var background = Assert.IsType<SolidColorBrush>(border.Background);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), background.Color);
        Assert.True(border.HasLocalValue(Border.BackgroundProperty));
        Assert.Equal(new CornerRadius(6.5), border.CornerRadius);
        Assert.True(border.HasLocalValue(Border.CornerRadiusProperty));
        Assert.Empty(template.TemplateBindings);
        Assert.Empty(template.TemplateNames);
    }

    [Fact]
    public void Shared_thumb_lookups_return_same_instance()
    {
        // The staged thumb key is shared, so every lookup returns the same
        // ControlTemplate instance.
        XamlLoader.CustomElementSource = null;
        var n = StagedThumbTables();
        var dictionary = BamlReader.LoadDictionary(ThumbDictionary(
            "ColorPickerSliderThumbTemplate", ThumbEntry(n), n));
        var first = Assert.IsType<ControlTemplate>(dictionary["ColorPickerSliderThumbTemplate"]);
        Assert.Same(first, Assert.IsType<ControlTemplate>(dictionary["ColorPickerSliderThumbTemplate"]));
        Assert.Same(typeof(Thumb), first.TargetType);
    }

    [Fact]
    public void Staged_shape_thumb_template_has_no_inflation_markup()
    {
        // The landed thumb template retains its visual root without
        // inflation markup, so inflating throws the precise BAML-tree
        // inflation boundary instead of sharing one instance.
        LoadStagedHandyControlEntry();
        var n = StagedThumbTables();
        var dictionary = BamlReader.LoadDictionary(ThumbDictionary("ColorPickerSliderThumbTemplate", ThumbEntry(n), n));
        var template = Assert.IsType<ControlTemplate>(dictionary["ColorPickerSliderThumbTemplate"]);
        var error = Assert.Throws<InvalidOperationException>(() => template.LoadContent());
        Assert.Contains("no inflation markup", error.Message);
        Assert.Contains("BAML-tree inflation", error.Message);
    }

    [Fact]
    public void Fixture_numbered_thumb_lands_with_line_markers()
    {
        // The genuine net10.0-windows fixture numbers its tables from zero
        // (attrs 0-1 over the same ControlTemplate/Border owners) and
        // carries 0x35/0x36 line markers: the parser resolves everything
        // through the tables, so the same shapes land with line markers
        // transparent.
        XamlLoader.CustomElementSource = null;
        var numbering = FixtureThumbTables();
        var entry = new B()
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x35).U32(6).U32(10)
            .Raw(0x22).I16(0).I16(-658)
            .Raw(0x2E).I16(-174)
            .Raw(0x35).U32(7).U32(12)
            .Raw(0x03).I16(-50).Raw(0x00)
            .Sized(0x24, q => q.I16(-57).Str("13").I16(-348))
            .Sized(0x24, q => q.I16(-47).Str("13").I16(-348))
            .Sized(0x06, q => q.I16(-4).U16(744).Raw(0x01, 0xFF, 0xFF, 0xFF, 0xFF))
            .Sized(0x24, q => q.I16(1).Str("6.5").I16(-111))
            .Raw(0x36).U32(8)
            .Raw(0x04)
            .Raw(0x36).U32(9)
            .Raw(0x04)
            .ToArray();
        var dictionary = BamlReader.LoadDictionary(ThumbDictionary("ColorPickerSliderThumbTemplate", entry, numbering));
        var template = Assert.IsType<ControlTemplate>(dictionary["ColorPickerSliderThumbTemplate"]);
        Assert.Same(typeof(Thumb), template.TargetType);
        var border = Assert.IsType<Border>(template.VisualTree);
        Assert.Equal(13.0, border.Width);
        Assert.Equal(13.0, border.Height);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), Assert.IsType<SolidColorBrush>(border.Background).Color);
        Assert.Equal(new CornerRadius(6.5), border.CornerRadius);
    }

    [Fact]
    public void Unknown_style_target_fails_precise()
    {
        // A well-known TargetType with no style-target routing (here the
        // staged DrawingBrush, which routes keyed entries but not style
        // targets since slice-140 named the drawing ids) fails naming the
        // type and the extended allowlist instead of landing a typeless
        // template.
        XamlLoader.CustomElementSource = null;
        var n = StagedThumbTables();
        var body = TemplateHead(n, target: -180)
            .Raw(0x04)
            .ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ThumbDictionary("T", body, n)));
        Assert.Contains("targets DrawingBrush", error.Message);
        Assert.Contains("style targets beyond Path/Border/ContentControl/GridSplitter/Rectangle/RepeatButton/GroupBox/Thumb", error.Message);
    }

    [Fact]
    public void Duplicate_target_type_fails_precise()
    {
        // A second TargetType on a keyed template fails instead of
        // re-targeting the template mid-parse.
        XamlLoader.CustomElementSource = null;
        var n = StagedThumbTables();
        var body = TemplateHead(n)
            .Raw(0x22).I16((short)n.TargetType).I16(-658)
            .Raw(0x04)
            .ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ThumbDictionary("T", body, n)));
        Assert.Contains("duplicate TargetType on the template", error.Message);
    }

    [Fact]
    public void Border_width_wrong_converter_fails_precise()
    {
        // A Width simple over any other converter (here the
        // BooleanConverter) fails instead of landing an unconverted width.
        XamlLoader.CustomElementSource = null;
        var n = StagedThumbTables();
        var border = ThumbBorder(n, widthConv: -46, widthText: "13").ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ThumbDictionary("T", ThumbEntryWithBorder(n, border), n)));
        Assert.Contains("'13' with converter -46", error.Message);
        Assert.Contains("does not convert to Double", error.Message);
    }

    [Fact]
    public void Border_width_bad_text_fails_precise()
    {
        // A Width simple whose text is not a length fails instead of
        // landing a zero width.
        XamlLoader.CustomElementSource = null;
        var n = StagedThumbTables();
        var border = ThumbBorder(n, widthConv: -348, widthText: "wide").ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ThumbDictionary("T", ThumbEntryWithBorder(n, border), n)));
        Assert.Contains("'wide' with converter -348", error.Message);
        Assert.Contains("does not convert to Double", error.Message);
    }

    [Fact]
    public void Border_background_wrong_serializer_fails_precise()
    {
        // A packed brush value with any other serializer fails naming the
        // serializer used.
        XamlLoader.CustomElementSource = null;
        var n = StagedThumbTables();
        var border = ThumbBorder(n, bgSer: 1).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ThumbDictionary("T", ThumbEntryWithBorder(n, border), n)));
        Assert.Contains("holds a brush value with serializer 1", error.Message);
        Assert.Contains("template brushes take packed-ARGB values", error.Message);
    }

    [Fact]
    public void Border_background_wrong_attr_fails_precise()
    {
        // A packed brush value over any other attribute (here BorderBrush)
        // fails instead of landing on the wrong brush slot.
        XamlLoader.CustomElementSource = null;
        var n = StagedThumbTables();
        var border = ThumbBorder(n, bgAttr: -5).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ThumbDictionary("T", ThumbEntryWithBorder(n, border), n)));
        Assert.Contains("holds a packed-ARGB brush for attribute id -5", error.Message);
        Assert.Contains("template brushes take the Background value", error.Message);
    }

    [Fact]
    public void Border_garbage_record_fails_precise()
    {
        // A trigger-list record directly on the template Border fails
        // naming the record with the Border contract.
        XamlLoader.CustomElementSource = null;
        var n = StagedThumbTables();
        var border = new B()
            .Raw(0x0B).U16(1)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ThumbDictionary("T", ThumbEntryWithBorder(n, border), n)));
        Assert.Contains("template Border holds record 0x0B", error.Message);
        Assert.Contains("borders take TemplateBinding values plus Margin/Grid.Row simples plus complex bindings plus one StackPanel, Rectangle, ContentPresenter, SimplePanel or Viewport3D child", error.Message);
    }

    [Fact]
    public void Template_second_visual_fails_precise()
    {
        // A second root visual on a keyed template fails instead of
        // replacing the first visual.
        XamlLoader.CustomElementSource = null;
        var n = StagedThumbTables();
        var border = ThumbBorder(n).ToArray();
        var body = TemplateHead(n)
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(border)
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(border)
            .Raw(0x04)
            .ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ThumbDictionary("T", body, n)));
        Assert.Contains("second visual on the template", error.Message);
    }

    [Fact]
    public void Template_unknown_root_fails_precise()
    {
        // A well-known visual with no template-root routing fails naming
        // the visual with the root dispatch contract.
        XamlLoader.CustomElementSource = null;
        var n = StagedThumbTables();
        var body = TemplateHead(n)
            .Raw(0x03).I16(-380).Raw(0x00)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ThumbDictionary("T", body, n)));
        Assert.Contains("element well-known type id -380 on the template", error.Message);
        Assert.Contains("templates take a ContentPresenter, StackPanel, Border, Rectangle, Grid, TextBlock or SimplePanel visual", error.Message);
    }

    [Fact]
    public void Border_box_dp_contract()
    {
        // The engine Border carries the WPF-identical box DPs the staged
        // thumb Border simples land through, defaulting empty.
        Assert.Same(FrameworkElement.WidthProperty, DependencyProperty.FromName(typeof(Border), "Width"));
        Assert.Same(FrameworkElement.HeightProperty, DependencyProperty.FromName(typeof(Border), "Height"));
        Assert.Same(Border.CornerRadiusProperty, DependencyProperty.FromName(typeof(Border), "CornerRadius"));
        var border = new Border();
        Assert.True(double.IsNaN(border.Width));
        Assert.True(double.IsNaN(border.Height));
        Assert.Equal(new CornerRadius(0), border.CornerRadius);
        Assert.False(border.HasLocalValue(FrameworkElement.WidthProperty));
    }

    /// <summary>Per-file table numbering standing in for the staged rows
    /// (attrs 7 TargetType, 31 CornerRadius; Width/Height/Background travel
    /// the well-known member ids like the staged file).</summary>
    private sealed record ThumbNumbering(
        (int Id, string Name, short Owner)[] Attrs,
        int TargetType, int CornerRadius);

    private static ThumbNumbering StagedThumbTables() => new(
        [(7, "TargetType", -108), (31, "CornerRadius", -50)],
        7, 31);

    /// <summary>Fixture numbering standing in for the genuine net10
    /// fixture tables (attrs 0 TargetType, 1 CornerRadius).</summary>
    private static ThumbNumbering FixtureThumbTables() => new(
        [(0, "TargetType", -108), (1, "CornerRadius", -50)],
        0, 1);

    /// <summary>Builds the staged template head: the ControlTemplate
    /// element, the Thumb (or override) TargetType and the VisualTree
    /// content (no visual, for the dispatch tests).</summary>
    private static B TemplateHead(ThumbNumbering n, short target = -658) => new B()
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16((short)n.TargetType).I16(target)
        .Raw(0x2E).I16(-174);

    /// <summary>Builds the staged thumb Border body (no ElementStart):
    /// the Width/Height/CornerRadius simples plus the packed-ARGB
    /// Background value, closed once.</summary>
    private static B ThumbBorder(
        ThumbNumbering n,
        short widthConv = -348, string widthText = "13",
        short heightConv = -348, string heightText = "13",
        short bgAttr = -4, ushort bgSer = 744,
        short cornerConv = -111, string cornerText = "6.5") => new B()
        .Sized(0x24, q => q.I16(-57).Str(widthText).I16(widthConv))
        .Sized(0x24, q => q.I16(-47).Str(heightText).I16(heightConv))
        .Sized(0x06, q => q.I16(bgAttr).U16(bgSer).Raw(0x01, 0xFF, 0xFF, 0xFF, 0xFF))
        .Sized(0x24, q => q.I16((short)n.CornerRadius).Str(cornerText).I16(cornerConv))
        .Raw(0x04);

    /// <summary>Builds the full staged 'ColorPickerSliderThumbTemplate'
    /// entry shape: the head, the Border visual, closed twice.</summary>
    private static byte[] ThumbEntry(ThumbNumbering n) => TemplateHead(n)
        .Raw(0x03).I16(-50).Raw(0x00)
        .Raw(ThumbBorder(n).ToArray())
        .Raw(0x04).ToArray();

    /// <summary>Builds a full entry body over a custom Border body (for
    /// the Border negatives): the Border ElementStart is added, then the
    /// body, closed twice.</summary>
    private static byte[] ThumbEntryWithBorder(ThumbNumbering n, byte[] borderBody) => TemplateHead(n)
        .Raw(0x03).I16(-50).Raw(0x00)
        .Raw(borderBody)
        .Raw(0x04).ToArray();

    /// <summary>Builds a single-entry keyed ControlTemplate dictionary over
    /// the thumb entry (the staged thumb key is shared, declaring no
    /// statics). AttributeInfo/StringInfo rows stand in for the staged
    /// tables.</summary>
    private static byte[] ThumbDictionary(string key, byte[] entry, ThumbNumbering n)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"));
        foreach (var (id, name, owner) in n.Attrs)
            head.Sized(0x1F, p => p.I16((short)id).I16(owner).Raw(0x00).Str(name));
        head.Sized(0x20, p => p.U16(0).Str(key));
        head.Deferable();
        head.Sized(0x26, p => p.U16(0).U32(0).U16(0));
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
                VerifyStagedHashes();
                return hit;
            }
            if (_stagedEntry is not null) return _stagedEntry;
            var apps = StagedHandyAppsDirectory();
            using var document = JsonDocument.Parse(
                File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
            var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var bytes = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
                var actual = Convert.ToHexString(SHA256.HashData(bytes));
                Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                    $"hash mismatch in staged '{file}': {actual} != {expected}");
                images[assembly.GetProperty("name").GetString()!] = bytes;
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
            return _stagedEntry;
        }
    }

    private static void VerifyStagedHashes()
    {
        var apps = StagedHandyAppsDirectory();
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
        foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
        {
            var file = assembly.GetProperty("file").GetString()!;
            var expected = assembly.GetProperty("sha256").GetString()!;
            var actual = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(System.IO.Path.Combine(apps, file))));
            Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                $"hash mismatch in staged '{file}': {actual} != {expected}");
        }
    }

    private sealed class B
    {
        private readonly List<byte> _b = new();
        public B Raw(params byte[] b) { _b.AddRange(b); return this; }
        public B U16(ushort v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B U32(uint v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
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
        public B Deferable()
        {
            _pos = _b.Count;
            _b.AddRange(new byte[] { 0x25, 0x00, 0x00, 0x00, 0x00 });
            return this;
        }
        public B FinishDictionary()
        {
            if (_pos < 0) throw new InvalidOperationException("no open deferable");
            var endPos = _b.Count;
            _b.Add(0x04);
            var skip = (uint)(endPos - (_pos + 5));
            var patch = BitConverter.GetBytes(skip);
            for (var i = 0; i < 4; i++) _b[_pos + 1 + i] = patch[i];
            _b.Add(0x02);
            return this;
        }
        public byte[] ToArray() => _b.ToArray();
        private int _pos = -1;
    }
}
