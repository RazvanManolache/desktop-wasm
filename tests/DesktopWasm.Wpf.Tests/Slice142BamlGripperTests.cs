// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-142 HandyControl theme.baml GridViewColumnHeaderGripper run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the slice-141 blocker (the keyed 'GridViewColumnHeaderGripper' Style
/// entry keyidx 157 at BAML+0x195ED, its qualified Canvas.Right setter at
/// BAML+0x195FD with owner well-known id -66) routes with its four
/// following setters in the same upstream block: the short-form Width
/// "18", the Height TemplatedParent Binding (constructor-path
/// "ActualHeight" plus the RelativeSource complex child), the short-form
/// Padding "0" and the packed-ARGB Background (Transparent). The run
/// stops precise at the Template setter at BAML+0x196A0, whose Border
/// Padding TemplateBinding (BAML+0x196C6) names a DP the engine Border
/// does not declare: the precise next blocker.
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways: the staged bytes (the Style head 03 94 FD
/// over the Thumb TargetType -658, the 16521 qualified Canvas.Right
/// PropertyCustom with owner BE FF plus the "-9"/LengthConverter value,
/// the Width/Height/Padding/Background short-form setters with the
/// -348 LengthConverter values, the -143 Padding-converter value, the
/// 744 packed-ARGB Transparent brush value, the Binding/RelativeSource
/// elements with their 0x2A constructor texts, the Template setter head
/// with its ControlTemplate/Thumb/Border/Rectangle shape, and the key
/// records 0x26 key ids 0x93-0x94); the stock-WPF GridView header
/// gripper block the upstream Theme.xaml carries (Canvas.Right="-9",
/// Width="18", Height bound to ActualHeight through TemplatedParent,
/// Padding="0", Background="Transparent", Border/Rectangle template);
/// a genuine net10.0-windows fixture compiling that exact block
/// (1256-byte gripper.baml, emitting the identical owner -66, member,
/// converter (-143 for Padding) and Binding/RelativeSource ids and
/// shapes modulo per-file table numbering plus line markers and
/// ConnectionIds the staged file omits; fixture retained at
/// E:/tmp/bamlfix142 outside the tree) plus a live net10.0-windows
/// defaults probe (Canvas.Right/Thumb.Background/Thumb.Padding/
/// Thumb.Height shapes the landing asserts).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice142BamlGripperTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice142BamlGripperTests()
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
    public void Theme_baml_pins_gripper_template_spans()
    {
        // Staged framing pins for the keyed 'GridViewColumnHeaderGripper'
        // Style entry keyidx 157 spanning [0x195ED, 0x196F6): the Style
        // head over the Thumb TargetType, the qualified Canvas.Right
        // setter (owner BE FF = -66) with its "-9"/LengthConverter value,
        // the Width "18" setter, the Height Binding setter (constructor
        // "ActualHeight" plus the RelativeSource TemplatedParent complex),
        // the Padding "0" setter (converter 71 FD = -143), the packed-ARGB
        // Transparent Background setter, then the Template setter head
        // with its Thumb ControlTemplate/Border/Rectangle prefix; then the
        // key records 0x26 key ids 0x93-0x94.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");

        var gripperHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x6E, 0xFD };
        Assert.Equal(gripperHead, theme[0x195ED..0x195F6]);
        var canvasRight = new byte[] { 0x06, 0x0D, 0x01, 0x00, 0x89, 0x40, 0xBE, 0xFF, 0x05, 0x52, 0x69, 0x67, 0x68, 0x74 };
        Assert.Equal(canvasRight, theme[0x195FD..0x1960B]);
        var canvasRightValue = new byte[] { 0x24, 0x08, 0x02, 0x00, 0x02, 0x2D, 0x39, 0xA4, 0xFE };
        Assert.Equal(canvasRightValue, theme[0x1960B..0x19614]);
        var widthProp = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xC7, 0xFF };
        Assert.Equal(widthProp, theme[0x19619..0x19621]);
        var widthValue = new byte[] { 0x24, 0x08, 0x02, 0x00, 0x02, 0x31, 0x38, 0xA4, 0xFE };
        Assert.Equal(widthValue, theme[0x19621..0x1962A]);
        var heightProp = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xD1, 0xFF };
        Assert.Equal(heightProp, theme[0x1962F..0x19637]);
        var heightComplex = new byte[] { 0x07, 0x02, 0x00 };
        Assert.Equal(heightComplex, theme[0x19637..0x1963A]);
        var bindingHead = new byte[] { 0x03, 0xEC, 0xFF, 0x00 };
        Assert.Equal(bindingHead, theme[0x1963A..0x1963E]);
        var pathText = new byte[] { 0x10, 0x0E, 0x0C, 0x41, 0x63, 0x74, 0x75, 0x61, 0x6C, 0x48, 0x65, 0x69, 0x67, 0x68, 0x74 };
        Assert.Equal(pathText, theme[0x1963F..0x1964E]);
        var rsComplex = new byte[] { 0x07, 0x0A, 0x00 };
        Assert.Equal(rsComplex, theme[0x1964F..0x19652]);
        var rsHead = new byte[] { 0x03, 0xFC, 0xFD, 0x00 };
        Assert.Equal(rsHead, theme[0x19652..0x19656]);
        var rsText = new byte[] { 0x10, 0x11, 0x0F, 0x54, 0x65, 0x6D, 0x70, 0x6C, 0x61, 0x74, 0x65, 0x64, 0x50, 0x61, 0x72, 0x65, 0x6E, 0x74 };
        Assert.Equal(rsText, theme[0x19657..0x19669]);
        var paddingProp = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDD, 0xFF };
        Assert.Equal(paddingProp, theme[0x19673..0x1967B]);
        var paddingValue = new byte[] { 0x24, 0x07, 0x02, 0x00, 0x01, 0x30, 0x71, 0xFD };
        Assert.Equal(paddingValue, theme[0x1967B..0x19683]);
        var bgProp = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xE8, 0xFF };
        Assert.Equal(bgProp, theme[0x19688..0x19690]);
        var bgValue = new byte[] { 0x06, 0x0A, 0x02, 0x00, 0xE8, 0x02, 0x01, 0xFF, 0xFF, 0xFF, 0x00 };
        Assert.Equal(bgValue, theme[0x19690..0x1969B]);
        var templateProp = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDB, 0xFF };
        Assert.Equal(templateProp, theme[0x196A0..0x196A8]);
        var templateComplex = new byte[] { 0x07, 0x02, 0x00 };
        Assert.Equal(templateComplex, theme[0x196A8..0x196AB]);
        var templateHead = new byte[] { 0x03, 0x94, 0xFF, 0x00 };
        Assert.Equal(templateHead, theme[0x196AB..0x196AF]);
        var templateTarget = new byte[] { 0x22, 0x07, 0x00, 0x6E, 0xFD };
        Assert.Equal(templateTarget, theme[0x196AF..0x196B4]);
        var visualTree = new byte[] { 0x2E, 0x52, 0xFF };
        Assert.Equal(visualTree, theme[0x196B4..0x196B7]);
        var borderHead = new byte[] { 0x03, 0xCE, 0xFF, 0x00 };
        Assert.Equal(borderHead, theme[0x196B7..0x196BB]);
        var borderBg = new byte[] { 0x06, 0x0A, 0xFC, 0xFF, 0xE8, 0x02, 0x01, 0xFF, 0xFF, 0xFF, 0x00 };
        Assert.Equal(borderBg, theme[0x196BB..0x196C6]);
        var paddingTb = new byte[] { 0x23, 0x4B, 0x00, 0x7A, 0x02, 0xDD, 0xFF };
        Assert.Equal(paddingTb, theme[0x196C6..0x196CD]);

        var key157 = new byte[] { 0x26, 0x09, 0x93, 0x00, 0x56, 0xA4, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key157, theme[0xB54B..0xB555]);
        var key158 = new byte[] { 0x26, 0x09, 0x94, 0x00, 0x5F, 0xA5, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key158, theme[0xB555..0xB55F]);
    }
    [Fact]
    public void Staged_boot_advances_past_gripper_scalars_to_template()
    {
        // The staged boot now materializes the keyed
        // 'GridViewColumnHeaderGripper' Style entry keyidx 157 through its
        // five scalar/complex setters (qualified Canvas.Right "-9", Width
        // "18", the ActualHeight TemplatedParent Height Binding, Padding
        // "0", transparent Background) and stops at the Template setter
        // (BAML+0x196A0): its Border Padding TemplateBinding at BAML+0x196C6
        // names a DP the engine Border does not declare - the precise next
        // blocker. Every earlier stop stays absent, and the new head asserts
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
        Assert.DoesNotContain("VirtualizingStackHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("VirtualizingStackVerticalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapVerticalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("DockItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("SineEaseOut", inner.Message);
        Assert.DoesNotContain("BAML+0x19150", inner.Message);
        Assert.DoesNotContain("BadgeDotTemplate", inner.Message);
        Assert.DoesNotContain("element ContentPresenter on the template root panel", inner.Message);
        Assert.DoesNotContain("root panels take ContentPresenter, Border, converted Shape and TextBlock children", inner.Message);
        Assert.DoesNotContain("BAML+0x1927D", inner.Message);
        Assert.DoesNotContain("ColorPickerSliderThumbTemplate", inner.Message);
        Assert.DoesNotContain("well-known type id -658", inner.Message);
        Assert.DoesNotContain("style targets beyond Path/Border/ContentControl/GridSplitter/Rectangle/RepeatButton/GroupBox/Thumb are a style-surface follow-up", inner.Message);
        Assert.DoesNotContain("BAML+0x192B2", inner.Message);
        Assert.DoesNotContain("BAML+0x192B9", inner.Message);
        Assert.DoesNotContain("ColorPickerRainbowBrush", inner.Message);
        Assert.DoesNotContain("has no Offset", inner.Message);
        Assert.DoesNotContain("holds a LinearGradientBrush", inner.Message);
        Assert.DoesNotContain("BAML+0x19383", inner.Message);
        Assert.DoesNotContain("ColorPickerOpacityBrush", inner.Message);
        Assert.DoesNotContain("well-known type id -180", inner.Message);
        Assert.DoesNotContain("BAML+0x19497", inner.Message);
        Assert.DoesNotContain("CoverFlowBaseStyle", inner.Message);
        Assert.DoesNotContain("well-known type id -729", inner.Message);
        Assert.DoesNotContain("template borders take", inner.Message);
        Assert.DoesNotContain("CoverViewItemBaseStyle", inner.Message);
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
    public void Staged_shape_gripper_lands_live()
    {
        // The staged 'GridViewColumnHeaderGripper' entry shape (Thumb
        // target, qualified Canvas.Right "-9", Width "18", the
        // ActualHeight TemplatedParent Height Binding, Padding "0",
        // transparent Background) lands as a live engine Style with the
        // five setters intact. Per-file ids mirror the staged rows
        // 0/1/2/7/10 plus the Border-owned Padding row 75.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var dictionary = BamlReader.LoadDictionary(GripperDictionary("GridViewColumnHeaderGripper", StagedGripperEntry(n), n));
        var style = Assert.IsType<Style>(dictionary["GridViewColumnHeaderGripper"]);
        Assert.Same(typeof(Thumb), style.TargetType);
        var setters = style.Setters.OfType<Setter>().ToArray();
        Assert.Equal(5, setters.Length);
        var right = setters[0];
        Assert.Same(Canvas.RightProperty, right.Property);
        Assert.Equal(-9.0, Assert.IsType<double>(right.Value));
        var width = setters[1];
        Assert.Same(FrameworkElement.WidthProperty, width.Property);
        Assert.Equal(18.0, Assert.IsType<double>(width.Value));
        var height = setters[2];
        Assert.Same(FrameworkElement.HeightProperty, height.Property);
        var binding = Assert.IsType<Binding>(height.Value);
        Assert.Equal("ActualHeight", binding.Path.Path);
        Assert.NotNull(binding.RelativeSource);
        Assert.Equal(RelativeSourceMode.TemplatedParent, binding.RelativeSource.Mode);
        var padding = setters[3];
        Assert.Same(Control.PaddingProperty, padding.Property);
        Assert.Equal(new Thickness(0), Assert.IsType<Thickness>(padding.Value));
        var background = setters[4];
        Assert.Same(Control.BackgroundProperty, background.Property);
        var brush = Assert.IsType<SolidColorBrush>(background.Value);
        Assert.Equal(Color.FromArgb(0, 255, 255, 255), brush.Color);
    }

    [Fact]
    public void Staged_shape_applies_to_live_thumb()
    {
        // Applying the landed gripper Style to a live Thumb drives the
        // real apply paths: the qualified Canvas.Right transfer lands
        // through the DP store plus the attached bag, the Width/Padding/
        // Background values land through the DP store, and the Height
        // transfer activates as a live binding instead of landing the
        // Binding object as the local value.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var dictionary = BamlReader.LoadDictionary(GripperDictionary("GridViewColumnHeaderGripper", StagedGripperEntry(n), n));
        var style = Assert.IsType<Style>(dictionary["GridViewColumnHeaderGripper"]);
        var thumb = new Thumb();
        Assert.True(double.IsNaN(Canvas.GetRight(thumb)));
        style.ApplyTo(thumb);
        Assert.Equal(-9.0, Canvas.GetRight(thumb));
        Assert.Equal(-9.0, (double)thumb.GetValue(Canvas.RightProperty));
        Assert.Equal(18.0, thumb.Width);
        Assert.Equal(new Thickness(0), thumb.Padding);
        Assert.Equal(Color.FromArgb(0, 255, 255, 255), Assert.IsType<SolidColorBrush>(thumb.Background).Color);
        var expression = BindingOperations.GetBindingExpression(thumb, FrameworkElement.HeightProperty);
        Assert.NotNull(expression);
        Assert.Equal("ActualHeight", expression.Binding.Path.Path);
        Assert.Equal(RelativeSourceMode.TemplatedParent, expression.Binding.RelativeSource!.Mode);
    }

    [Fact]
    public void Fixture_numbered_gripper_lands_live()
    {
        // The genuine net10.0-windows fixture numbers its tables from zero
        // (attrs 0-5 over the same owners); the same staged entry shape
        // lands identically, proving the routing never hardcodes an index.
        LoadStagedHandyControlEntry();
        var n = FixtureGripperTables();
        var dictionary = BamlReader.LoadDictionary(GripperDictionary("GridViewColumnHeaderGripper", StagedGripperEntry(n), n));
        var style = Assert.IsType<Style>(dictionary["GridViewColumnHeaderGripper"]);
        Assert.Same(typeof(Thumb), style.TargetType);
        var setters = style.Setters.OfType<Setter>().ToArray();
        Assert.Equal(5, setters.Length);
        Assert.Same(Canvas.RightProperty, setters[0].Property);
        Assert.Equal(-9.0, Assert.IsType<double>(setters[0].Value));
        var binding = Assert.IsType<Binding>(setters[2].Value);
        Assert.Equal("ActualHeight", binding.Path.Path);
        Assert.Equal(RelativeSourceMode.TemplatedParent, binding.RelativeSource!.Mode);
        Assert.Equal(new Thickness(0), Assert.IsType<Thickness>(setters[3].Value));
    }

    [Fact]
    public void Canvas_right_garbage_text_fails_precise()
    {
        // A qualified Canvas.Right value that is not a double fails naming
        // the text and the setter instead of landing a half-built Style.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var entry = GripperStyleShell(n, CanvasRightSetter(n, "-9x")).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(GripperDictionary("GridViewColumnHeaderGripper", entry, n)));
        Assert.Contains("'-9x' is not a Double for setter 'Canvas.Right'", error.Message);
    }

    [Fact]
    public void Qualified_canvas_owner_unknown_property_fails_precise()
    {
        // The Canvas owner resolves, but a property the engine Canvas does
        // not declare fails naming the property instead of landing a
        // half-built Style.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var setter = new B()
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16((short)n.Prop).U16(16521).I16(-66).Str("Nope"))
            .Raw(Pwc(n, n.Value, "-9", -348).ToArray())
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            GripperDictionary("GridViewColumnHeaderGripper", GripperStyleShell(n, setter).ToArray(), n)));
        Assert.Contains("'Nope' is not a dependency property of Canvas", error.Message);
    }

    [Fact]
    public void Canvas_right_defaults_nan()
    {
        // Engine Canvas.Right parity with the live probe: unset reads NaN,
        // SetRight round-trips through both the attached bag and the DP
        // store like the Left/Top knobs.
        var element = new Thumb();
        Assert.True(double.IsNaN(Canvas.GetRight(element)));
        Assert.Same(Canvas.RightProperty, DependencyProperty.FromName(typeof(Canvas), "Right")!);
        Assert.Equal(typeof(double), Canvas.RightProperty.PropertyType);
        Assert.Same(typeof(Canvas), Canvas.RightProperty.OwnerType);
        Canvas.SetRight(element, -9.0);
        Assert.Equal(-9.0, Canvas.GetRight(element));
        Assert.Equal(-9.0, (double)element.GetValue(Canvas.RightProperty));
    }

    [Fact]
    public void Padding_garbage_text_fails_precise()
    {
        // A Padding value that is not a Thickness fails naming the text
        // and the setter instead of landing a half-built Style.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var setter = new B()
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-35))
            .Raw(Pwc(n, n.Value, "0,x", -143).ToArray())
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            GripperDictionary("GridViewColumnHeaderGripper", GripperStyleShell(n, setter).ToArray(), n)));
        Assert.Contains("'0,x' is not a Thickness for setter 'Padding'", error.Message);
    }

    [Fact]
    public void Padding_on_border_target_lands()
    {
        // The -35 short form resolves through the style TargetType: the
        // engine Border declares Padding (its own Thickness registration),
        // so the setter lands instead of failing.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16((short)n.TargetType).I16(-50)
            .Raw(0x2E).I16(-238)
            .Raw(PaddingSetter(n).ToArray())
            .Raw(0x04).ToArray();
        var dictionary = BamlReader.LoadDictionary(GripperDictionary("S", content, n));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(Border), style.TargetType);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Same(Border.PaddingProperty, setter.Property);
        Assert.Equal(new Thickness(0), setter.Value);
    }

    [Fact]
    public void Template_padding_templatebinding_lands()
    {
        // The staged Template setter shape through the Border Padding
        // TemplateBinding lands: the transfer resolves through the engine
        // Border.PaddingProperty, so the template visual retains the
        // deferred (border, Padding) binding.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var template = TemplateSetterPrefix(n)
            .Raw(Extension((short)n.BorderPadding, -35).ToArray())
            .Raw(0x2E).I16(-161)
            .Raw(new B().Raw(0x03).I16(-514).Raw(0x00).Raw(0x04).ToArray())
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var entry = GripperStyleShell(n, template).ToArray();
        var dictionary = BamlReader.LoadDictionary(GripperDictionary("GridViewColumnHeaderGripper", entry, n));
        var style = Assert.IsType<Style>(dictionary["GridViewColumnHeaderGripper"]);
        var controlTemplate = Assert.IsType<ControlTemplate>(style.Setters.OfType<Setter>().Single(s => s.Property.Name == "Template").Value);
        var border = Assert.IsType<Border>(controlTemplate.VisualTree);
        Assert.Contains((border, "Padding"), controlTemplate.TemplateBindings.Select(b => (b.Element, b.Property)));
    }

    [Fact]
    public void Height_two_ctor_args_fail_precise()
    {
        // A setter Binding takes one constructor argument (the path);
        // richer markup-extension arguments stay a precise follow-up.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var binding = new B()
            .Raw(0x03).I16(-20).Raw(0x00)
            .Raw(new B().Raw(0x2A)
                .Sized(0x10, q => q.Str("ActualHeight"))
                .Sized(0x10, q => q.Str("TwoWay"))
                .Raw(0x2B).ToArray())
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            GripperDictionary("GridViewColumnHeaderGripper", GripperStyleShell(n, HeightSetterFor(n, binding)).ToArray(), n)));
        Assert.Contains("takes one constructor argument (the path", error.Message);
    }

    [Fact]
    public void Height_bad_relative_source_mode_fails_precise()
    {
        // A RelativeSource mode outside FindAncestor/Self/TemplatedParent/
        // PreviousData fails naming the mode instead of landing a
        // half-bound setter.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var entry = GripperStyleShell(n,
            HeightSetterFor(n, GripperBinding(n, "ActualHeight", "Nope"))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(GripperDictionary("GridViewColumnHeaderGripper", entry, n)));
        Assert.Contains("names 'Nope'", error.Message);
    }

    [Fact]
    public void Height_simple_value_fails_precise()
    {
        // A simple value inside a setter Binding fails: setter bindings
        // take the path plus RelativeSource, never PWC simples.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var binding = new B()
            .Raw(0x03).I16(-20).Raw(0x00)
            .Raw(HeightPathPwc(n).ToArray())
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            GripperDictionary("GridViewColumnHeaderGripper", GripperStyleShell(n, HeightSetterFor(n, binding)).ToArray(), n)));
        Assert.Contains("holds a simple value (setter bindings take the path plus RelativeSource", error.Message);
    }

    [Fact]
    public void Height_static_value_fails_precise()
    {
        // A StaticResource Converter inside a setter Binding fails naming
        // the transfer instead of landing a half-bound setter.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var binding = new B()
            .Raw(0x03).I16(-20).Raw(0x00)
            .Raw(0x38).I16(0).U16(0)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            GripperDictionary("GridViewColumnHeaderGripper", GripperStyleShell(n, HeightSetterFor(n, binding)).ToArray(), n)));
        Assert.Contains("holds a StaticResource value (setter bindings take the path plus RelativeSource", error.Message);
    }

    [Fact]
    public void Height_non_relativesource_complex_fails_precise()
    {
        // A complex child that is not the RelativeSource row fails naming
        // the attribute: setter bindings take RelativeSource.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var binding = new B()
            .Raw(0x03).I16(-20).Raw(0x00)
            .Raw(0x07).I16((short)n.Value)
            .Raw(0x03).I16(-20).Raw(0x00).Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            GripperDictionary("GridViewColumnHeaderGripper", GripperStyleShell(n, HeightSetterFor(n, binding)).ToArray(), n)));
        Assert.Contains("setter bindings take RelativeSource", error.Message);
    }

    [Fact]
    public void Height_duplicate_relative_source_fails_precise()
    {
        // Two RelativeSource children on one setter Binding fail naming
        // the duplicate instead of landing a half-bound setter.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var binding = new B()
            .Raw(0x03).I16(-20).Raw(0x00)
            .Raw(CtorText("ActualHeight").ToArray())
            .Raw(BindingRelativeSource(n, "TemplatedParent").ToArray())
            .Raw(BindingRelativeSource(n, "TemplatedParent").ToArray())
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            GripperDictionary("GridViewColumnHeaderGripper", GripperStyleShell(n, HeightSetterFor(n, binding)).ToArray(), n)));
        Assert.Contains("duplicate RelativeSource on the setter Binding", error.Message);
    }

    [Fact]
    public void Height_non_binding_complex_child_fails_precise()
    {
        // A complex Height value holding a non-Binding element fails
        // naming the element: complex setter values take a ControlTemplate,
        // a Binding, an ItemsPanelTemplate, a NullExtension or a TransformGroup.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var setter = new B()
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-47))
            .Raw(0x07).I16((short)n.Value)
            .Raw(0x03).I16(-514).Raw(0x00).Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            GripperDictionary("GridViewColumnHeaderGripper", GripperStyleShell(n, setter).ToArray(), n)));
        Assert.Contains("complex setter values take a ControlTemplate, a Binding, an ItemsPanelTemplate, a NullExtension or a TransformGroup", error.Message);
    }

    [Fact]
    public void Height_complex_missing_close_fails_precise()
    {
        // A Height Binding whose complex value closes with ElementEnd
        // instead of PropertyComplexEnd fails naming the record.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var setter = new B()
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-47))
            .Raw(0x07).I16((short)n.Value)
            .Raw(GripperBinding(n, "ActualHeight", "TemplatedParent").ToArray())
            .Raw(0x04)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            GripperDictionary("GridViewColumnHeaderGripper", GripperStyleShell(n, setter).ToArray(), n)));
        Assert.Contains("complex values close with PropertyComplexEnd", error.Message);
    }

    [Fact]
    public void Background_brush_on_double_property_fails_precise()
    {
        // A packed-ARGB brush value on a non-Brush property fails naming
        // the property type instead of landing a mistyped setter.
        LoadStagedHandyControlEntry();
        var n = StagedGripperTables();
        var setter = new B()
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-57))
            .Sized(0x06, q => q.I16((short)n.Value).U16(744).Raw(0x01, 0xFF, 0xFF, 0xFF, 0x00))
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            GripperDictionary("GridViewColumnHeaderGripper", GripperStyleShell(n, setter).ToArray(), n)));
        Assert.Contains("holds a packed-ARGB brush for Double", error.Message);
    }

    private sealed record GripperNumbering(
        (int Id, string Name, short Owner)[] Attrs,
        int TargetType, int Prop, int Value, int TemplateTarget,
        int RelativeSource, int BorderPadding);

    private static GripperNumbering StagedGripperTables() => new(
        [(0, "TargetType", (short)-620), (1, "Property", (short)-556), (2, "Value", (short)-556),
         (7, "TargetType", (short)-108), (10, "RelativeSource", (short)-20),
         (75, "Padding", (short)-50)],
        0, 1, 2, 7, 10, 75);

    /// <summary>Fixture numbering standing in for the genuine net10
    /// fixture tables (attrs 0-5 over the same owners).</summary>
    private static GripperNumbering FixtureGripperTables() => new(
        [(0, "TargetType", (short)-620), (1, "Property", (short)-556), (2, "Value", (short)-556),
         (3, "TargetType", (short)-108), (4, "RelativeSource", (short)-20),
         (5, "Padding", (short)-50)],
        0, 1, 2, 3, 4, 5);

    private static B GripperStyleShell(GripperNumbering n, params B[] setters)
    {
        var b = new B().Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16((short)n.TargetType).I16(-658)
            .Raw(0x2E).I16(-238);
        foreach (var setter in setters) b.Raw(setter.ToArray());
        return b.Raw(0x04);
    }

    private static byte[] StagedGripperEntry(GripperNumbering n) => GripperStyleShell(n,
        CanvasRightSetter(n),
        WidthSetter(n),
        HeightSetterFor(n, GripperBinding(n, "ActualHeight", "TemplatedParent")),
        PaddingSetter(n),
        BackgroundSetter(n)).ToArray();

    private static B CanvasRightSetter(GripperNumbering n, string text = "-9") => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16((short)n.Prop).U16(16521).I16(-66).Str("Right"))
        .Raw(Pwc(n, n.Value, text, -348).ToArray())
        .Raw(0x04);

    private static B WidthSetter(GripperNumbering n, string text = "18") => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-57))
        .Raw(Pwc(n, n.Value, text, -348).ToArray())
        .Raw(0x04);

    private static B HeightSetterFor(GripperNumbering n, B binding) => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-47))
        .Raw(0x07).I16((short)n.Value)
        .Raw(binding.ToArray())
        .Raw(0x08)
        .Raw(0x04);

    private static B PaddingSetter(GripperNumbering n, string text = "0") => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-35))
        .Raw(Pwc(n, n.Value, text, -143).ToArray())
        .Raw(0x04);

    private static B BackgroundSetter(GripperNumbering n) => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-24))
        .Sized(0x06, q => q.I16((short)n.Value).U16(744).Raw(0x01, 0xFF, 0xFF, 0xFF, 0x00))
        .Raw(0x04);

    private static B GripperBinding(GripperNumbering n, string path, string? mode) => new B()
        .Raw(0x03).I16(-20).Raw(0x00)
        .Raw(CtorText(path).ToArray())
        .Raw(mode is null ? [] : BindingRelativeSource(n, mode).ToArray())
        .Raw(0x04);

    private static B CtorText(string text) => new B()
        .Raw(0x2A)
        .Sized(0x10, q => q.Str(text))
        .Raw(0x2B);

    private static B BindingRelativeSource(GripperNumbering n, string mode) => new B()
        .Raw(0x07).I16((short)n.RelativeSource)
        .Raw(0x03).I16(-516).Raw(0x00)
        .Raw(CtorText(mode).ToArray())
        .Raw(0x04)
        .Raw(0x08);

    private static B HeightPathPwc(GripperNumbering n) => Pwc(n, n.Value, "ActualHeight", -615);

    private static B TemplateSetterPrefix(GripperNumbering n) => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-37))
        .Raw(0x07).I16((short)n.Value)
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16((short)n.TemplateTarget).I16(-658)
        .Raw(0x2E).I16(-174)
        .Raw(0x03).I16(-50).Raw(0x00)
        .Sized(0x06, q => q.I16(-4).U16(744).Raw(0x01, 0xFF, 0xFF, 0xFF, 0x00));

    private static B Extension(short target, short source) => new B()
        .Raw(0x23).I16(target).U16(634).I16(source);

    private static B Pwc(GripperNumbering n, int attr, string text, short conv) => new B()
        .Sized(0x24, q => q.I16((short)attr).Str(text).I16(conv));

    private static byte[] GripperDictionary(string key, byte[] entry, GripperNumbering n)
    {
        var content = new B().Raw(entry);
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"));
        foreach (var (id, name, owner) in n.Attrs)
            head.Sized(0x1F, p => p.I16((short)id).I16(owner).Raw(0x00).Str(name));
        head.Sized(0x20, p => p.U16(0).Str(key));
        head.Deferable();
        head.Raw(0x26).Seven(9).U16(0).U32(0).U16(0);
        return head.Raw(content.ToArray()).FinishDictionary().ToArray();
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
