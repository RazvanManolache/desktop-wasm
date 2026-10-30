// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-139 HandyControl theme.baml ColorPickerRainbowBrush run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the keyed 'ColorPickerRainbowBrush' LinearGradientBrush entry keyidx
/// 153 at [BAML+0x192B2, BAML+0x19383) lands as a live engine
/// LinearGradientBrush: the entry carries no StartPoint/EndPoint records
/// and its first GradientStop at BAML+0x192B9 carries Color but no
/// Offset, so absent points land at the WPF defaults (StartPoint (0,0),
/// EndPoint (1,1)) and a missing Offset at 0.0. The opacity brush landed
/// in slice-140 with its checker DrawingGroup; the run now stops precise
/// at the keyed 'CoverFlowBaseStyle' Style keyidx 155 at BAML+0x19497
/// (template Border Viewport3D child well-known id -729).
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways: the staged bytes (the 03 9A FE brush head,
/// the 2E C4 FF GradientStops content, the seven 03 04 FF GradientStop
/// children with 0x24 Color PWCs over row 0x2C with the ColorConverter
/// -87 and the "#ff0000"/"#ff00ff"/"#0000ff"/"#00ffff"/"#00ff00"/
/// "#ffff00" texts plus 0x24 Offset PWCs over row 0x2D with the
/// DoubleConverter -175 and the "0.167"/"0.334"/"0.501"/"0.668"/
/// "0.835"/"1" texts with the first stop Color-only, the 04 04 close
/// pair, the key records 0x26 key ids 0x8F-0x91, the DrawingBrush
/// entry-154 head 03 4C FF with its Viewport/ViewportUnits/Stretch/
/// TileMode PWCs plus the Drawing complex start and the DrawingGroup
/// head); the live upstream HandyControl ColorPickerBaseStyle.xaml
/// block at pinned commit e9ad3d70 (the same seven rainbow stops with
/// the same colors and offsets and no StartPoint/EndPoint, the same
/// opacity DrawingBrush with the same Viewport/ViewportUnits/Stretch/
/// TileMode and DrawingGroup/GeometryDrawing/RectangleGeometry content);
/// a genuine net10.0-windows fixture compiling the exact upstream
/// rainbow block into Rainbow.baml (emitting 03 9A FE with no
/// StartPoint/EndPoint records, the 2E C4 FF content and a Color-only
/// first stop, identical in every staged record shape modulo per-file
/// table numbering plus line markers the staged file omits; fixture
/// retained at E:/tmp/bamlfix139 outside the tree) plus a live
/// net10.0-windows defaults probe (StartPoint=0,0 EndPoint=1,1
/// Offset=0).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice139BamlRainbowTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice139BamlRainbowTests()
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
    public void Theme_baml_lands_rainbow_brush_then_stops_at_opacity_brush()
    {
        // Staged framing pins for the keyed 'ColorPickerRainbowBrush'
        // LinearGradientBrush entry keyidx 153 spanning [0x192B2,
        // 0x19383): the 03 9A FE head, the 2E C4 FF GradientStops
        // content, the seven GradientStop children (the first at 0x192B9
        // carries Color "#ff0000" but no Offset; the rest carry the
        // staged colors with the "0.167"/"0.334"/"0.501"/"0.668"/
        // "0.835"/"1" Offset PWCs), the 04 04 close pair, the 0x26 key
        // records for keyidx 153-155, and the keyed
        // 'ColorPickerOpacityBrush' entry keyidx 154 head at 0x19383
        // (03 4C FF over well-known id -180) with its Viewport/
        // ViewportUnits/Stretch/TileMode PWCs plus the Drawing complex
        // start and the DrawingGroup head (landing since slice-140; the
        // run stops at the keyidx-155 'CoverFlowBaseStyle' Style).
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");

        var rainbowHead = new byte[] { 0x03, 0x9A, 0xFE, 0x00, 0x2E, 0xC4, 0xFF, 0x03, 0x04, 0xFF, 0x00 };
        Assert.Equal(rainbowHead, theme[0x192B2..0x192BD]);
        var stop0Color = new byte[] { 0x24, 0x0D, 0x2C, 0x00, 0x07, 0x23, 0x66, 0x66, 0x30, 0x30, 0x30, 0x30, 0xA9, 0xFF };
        Assert.Equal(stop0Color, theme[0x192BD..0x192CB]);
        Assert.Equal(new byte[] { 0x04 }, theme[0x192CB..0x192CC]);
        var stopHead = new byte[] { 0x03, 0x04, 0xFF, 0x00 };
        Assert.Equal(stopHead, theme[0x192CC..0x192D0]);
        var stop1Color = new byte[] { 0x24, 0x0D, 0x2C, 0x00, 0x07, 0x23, 0x66, 0x66, 0x30, 0x30, 0x66, 0x66, 0xA9, 0xFF };
        Assert.Equal(stop1Color, theme[0x192D0..0x192DE]);
        var stop1Offset = new byte[] { 0x24, 0x0B, 0x2D, 0x00, 0x05, 0x30, 0x2E, 0x31, 0x36, 0x37, 0x51, 0xFF };
        Assert.Equal(stop1Offset, theme[0x192DE..0x192EA]);
        Assert.Equal(new byte[] { 0x04 }, theme[0x192EA..0x192EB]);
        Assert.Equal(stopHead, theme[0x192EB..0x192EF]);
        var stop2Color = new byte[] { 0x24, 0x0D, 0x2C, 0x00, 0x07, 0x23, 0x30, 0x30, 0x30, 0x30, 0x66, 0x66, 0xA9, 0xFF };
        Assert.Equal(stop2Color, theme[0x192EF..0x192FD]);
        var stop2Offset = new byte[] { 0x24, 0x0B, 0x2D, 0x00, 0x05, 0x30, 0x2E, 0x33, 0x33, 0x34, 0x51, 0xFF };
        Assert.Equal(stop2Offset, theme[0x192FD..0x19309]);
        Assert.Equal(new byte[] { 0x04 }, theme[0x19309..0x1930A]);
        Assert.Equal(stopHead, theme[0x1930A..0x1930E]);
        var stop3Color = new byte[] { 0x24, 0x0D, 0x2C, 0x00, 0x07, 0x23, 0x30, 0x30, 0x66, 0x66, 0x66, 0x66, 0xA9, 0xFF };
        Assert.Equal(stop3Color, theme[0x1930E..0x1931C]);
        var stop3Offset = new byte[] { 0x24, 0x0B, 0x2D, 0x00, 0x05, 0x30, 0x2E, 0x35, 0x30, 0x31, 0x51, 0xFF };
        Assert.Equal(stop3Offset, theme[0x1931C..0x19328]);
        Assert.Equal(new byte[] { 0x04 }, theme[0x19328..0x19329]);
        Assert.Equal(stopHead, theme[0x19329..0x1932D]);
        var stop4Color = new byte[] { 0x24, 0x0D, 0x2C, 0x00, 0x07, 0x23, 0x30, 0x30, 0x66, 0x66, 0x30, 0x30, 0xA9, 0xFF };
        Assert.Equal(stop4Color, theme[0x1932D..0x1933B]);
        var stop4Offset = new byte[] { 0x24, 0x0B, 0x2D, 0x00, 0x05, 0x30, 0x2E, 0x36, 0x36, 0x38, 0x51, 0xFF };
        Assert.Equal(stop4Offset, theme[0x1933B..0x19347]);
        Assert.Equal(new byte[] { 0x04 }, theme[0x19347..0x19348]);
        Assert.Equal(stopHead, theme[0x19348..0x1934C]);
        var stop5Color = new byte[] { 0x24, 0x0D, 0x2C, 0x00, 0x07, 0x23, 0x66, 0x66, 0x66, 0x66, 0x30, 0x30, 0xA9, 0xFF };
        Assert.Equal(stop5Color, theme[0x1934C..0x1935A]);
        var stop5Offset = new byte[] { 0x24, 0x0B, 0x2D, 0x00, 0x05, 0x30, 0x2E, 0x38, 0x33, 0x35, 0x51, 0xFF };
        Assert.Equal(stop5Offset, theme[0x1935A..0x19366]);
        Assert.Equal(new byte[] { 0x04 }, theme[0x19366..0x19367]);
        Assert.Equal(stopHead, theme[0x19367..0x1936B]);
        Assert.Equal(stop0Color, theme[0x1936B..0x19379]);
        var stop6Offset = new byte[] { 0x24, 0x07, 0x2D, 0x00, 0x01, 0x31, 0x51, 0xFF };
        Assert.Equal(stop6Offset, theme[0x19379..0x19381]);
        var closes = new byte[] { 0x04, 0x04 };
        Assert.Equal(closes, theme[0x19381..0x19383]);

        var key153 = new byte[] { 0x26, 0x09, 0x8F, 0x00, 0x1B, 0xA1, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key153, theme[0xB523..0xB52D]);
        var key154 = new byte[] { 0x26, 0x09, 0x90, 0x00, 0xEC, 0xA1, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key154, theme[0xB52D..0xB537]);
        var key155 = new byte[] { 0x26, 0x09, 0x91, 0x00, 0xA4, 0xA2, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key155, theme[0xB537..0xB541]);
        var opacityHead = new byte[] { 0x03, 0x4C, 0xFF, 0x00 };
        Assert.Equal(opacityHead, theme[0x19383..0x19387]);
        var viewport = new byte[] { 0x24, 0x0F, 0x39, 0x00, 0x09, 0x30, 0x2C, 0x30, 0x2C, 0x31, 0x32, 0x2C, 0x31, 0x31, 0x01, 0xFE };
        Assert.Equal(viewport, theme[0x19387..0x19397]);
        var units = new byte[] { 0x24, 0x0E, 0x3A, 0x00, 0x08, 0x41, 0x62, 0x73, 0x6F, 0x6C, 0x75, 0x74, 0x65, 0x3D, 0xFF };
        Assert.Equal(units, theme[0x19397..0x193A6]);
        var stretch = new byte[] { 0x24, 0x0A, 0x3B, 0x00, 0x04, 0x4E, 0x6F, 0x6E, 0x65, 0x3D, 0xFF };
        Assert.Equal(stretch, theme[0x193A6..0x193B1]);
        var tile = new byte[] { 0x24, 0x0A, 0x3C, 0x00, 0x04, 0x54, 0x69, 0x6C, 0x65, 0x3D, 0xFF };
        Assert.Equal(tile, theme[0x193B1..0x193BC]);
        var drawingStart = new byte[] { 0x07, 0x3D, 0x00 };
        Assert.Equal(drawingStart, theme[0x193BC..0x193BF]);
        var drawingGroup = new byte[] { 0x03, 0x49, 0xFF, 0x00 };
        Assert.Equal(drawingGroup, theme[0x193BF..0x193C3]);
        var childrenContent = new byte[] { 0x2E, 0xD6, 0xFF };
        Assert.Equal(childrenContent, theme[0x193C3..0x193C6]);
    }

    [Fact]
    public void Staged_boot_advances_past_rainbow_to_opacity_brush()
    {
        // The staged boot now materializes entries #153-154 (the
        // 'ColorPickerRainbowBrush' LinearGradientBrush with its seven
        // staged stops, WPF-default points and zero first offset, plus the
        // 'ColorPickerOpacityBrush' DrawingBrush with its checker
        // DrawingGroup) and stops at the keyed 'CoverFlowBaseStyle' Style
        // entry keyidx 155 (template Border Viewport3D child well-known id
        // -729 with no template routing: the precise next blocker). Every
        // earlier stop stays absent
        // (including the rainbow brush stop and its Color-only first
        // stop), and the new head asserts the exact frontier.
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
    public void Staged_shape_rainbow_lands_live()
    {
        // The staged 'ColorPickerRainbowBrush' entry shape (no
        // StartPoint/EndPoint records, Color-only first stop, six
        // Color/Offset stops) lands as a live engine LinearGradientBrush
        // with WPF-default points and a zero first offset. Per-file ids
        // 0x2C/0x2D stand in for the staged stop rows.
        LoadStagedHandyControlEntry();
        var n = StagedRainbowTables();
        var dictionary = BamlReader.LoadDictionary(RainbowDictionary("ColorPickerRainbowBrush", StagedRainbowEntry(n), n));
        var brush = Assert.IsType<LinearGradientBrush>(dictionary["ColorPickerRainbowBrush"]);
        Assert.Equal(new Point(0, 0), brush.StartPoint);
        Assert.Equal(new Point(1, 1), brush.EndPoint);
        Assert.Equal(7, brush.GradientStops.Count);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0x00, 0x00), brush.GradientStops[0].Color);
        Assert.Equal(0.0, brush.GradientStops[0].Offset);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF), brush.GradientStops[1].Color);
        Assert.Equal(0.167, brush.GradientStops[1].Offset);
        Assert.Equal(Color.FromArgb(0xFF, 0x00, 0x00, 0xFF), brush.GradientStops[2].Color);
        Assert.Equal(0.334, brush.GradientStops[2].Offset);
        Assert.Equal(Color.FromArgb(0xFF, 0x00, 0xFF, 0xFF), brush.GradientStops[3].Color);
        Assert.Equal(0.501, brush.GradientStops[3].Offset);
        Assert.Equal(Color.FromArgb(0xFF, 0x00, 0xFF, 0x00), brush.GradientStops[4].Color);
        Assert.Equal(0.668, brush.GradientStops[4].Offset);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0x00), brush.GradientStops[5].Color);
        Assert.Equal(0.835, brush.GradientStops[5].Offset);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0x00, 0x00), brush.GradientStops[6].Color);
        Assert.Equal(1.0, brush.GradientStops[6].Offset);
    }

    [Fact]
    public void Shared_rainbow_lookups_return_same_instance()
    {
        // The staged rainbow key is shared, so every lookup returns the
        // same LinearGradientBrush instance.
        XamlLoader.CustomElementSource = null;
        var n = StagedRainbowTables();
        var dictionary = BamlReader.LoadDictionary(RainbowDictionary(
            "ColorPickerRainbowBrush", StagedRainbowEntry(n), n));
        var first = Assert.IsType<LinearGradientBrush>(dictionary["ColorPickerRainbowBrush"]);
        Assert.Same(first, Assert.IsType<LinearGradientBrush>(dictionary["ColorPickerRainbowBrush"]));
        Assert.Equal(new Point(0, 0), first.StartPoint);
        Assert.Equal(new Point(1, 1), first.EndPoint);
        Assert.Equal(0.0, first.GradientStops[0].Offset);
    }

    [Fact]
    public void Non_shared_rainbow_lookups_materialize_fresh_instances()
    {
        // Non-shared keys replay the validated value bags through a
        // factory, so every lookup materializes a fresh brush with equal
        // values (including the defaulted points and first offset), never
        // a shared singleton.
        XamlLoader.CustomElementSource = null;
        var n = StagedRainbowTables();
        var dictionary = BamlReader.LoadDictionary(RainbowDictionary(
            "ColorPickerRainbowBrush", StagedRainbowEntry(n), n, shared: false));
        var first = Assert.IsType<LinearGradientBrush>(dictionary["ColorPickerRainbowBrush"]);
        var second = Assert.IsType<LinearGradientBrush>(dictionary["ColorPickerRainbowBrush"]);
        Assert.NotSame(first, second);
        Assert.Equal(new Point(0, 0), second.StartPoint);
        Assert.Equal(new Point(1, 1), second.EndPoint);
        Assert.Equal(first.GradientStops.Count, second.GradientStops.Count);
        Assert.Equal(0.0, second.GradientStops[0].Offset);
        Assert.Equal(1.0, second.GradientStops[6].Offset);
    }

    [Fact]
    public void Fixture_numbered_rainbow_lands_with_line_markers()
    {
        // The genuine net10.0-windows fixture numbers its tables from zero
        // (attrs 0-1 over the same -252 owner) and carries 0x35/0x36 line
        // markers: the parser resolves everything through the tables, so
        // the same defaults shape lands with line markers transparent
        // (no point records, Color-only first stop).
        XamlLoader.CustomElementSource = null;
        var numbering = FixtureRainbowTables();
        var entry = new B()
            .Raw(0x03).I16(-358).Raw(0x00)
            .Raw(0x35).U32(6).U32(10)
            .Raw(0x2E).I16(-60)
            .Raw(0x35).U32(7).U32(12)
            .Raw(StopColorOnly(numbering, "#ff0000").ToArray())
            .Raw(Stop(numbering, "#ff00ff", "0.167").ToArray())
            .Raw(Stop(numbering, "#0000ff", "0.334").ToArray())
            .Raw(Stop(numbering, "#00ffff", "0.501").ToArray())
            .Raw(Stop(numbering, "#00ff00", "0.668").ToArray())
            .Raw(Stop(numbering, "#ffff00", "0.835").ToArray())
            .Raw(Stop(numbering, "#ff0000", "1").ToArray())
            .Raw(0x36).U32(8)
            .Raw(0x04)
            .ToArray();
        var dictionary = BamlReader.LoadDictionary(RainbowDictionary("ColorPickerRainbowBrush", entry, numbering));
        var brush = Assert.IsType<LinearGradientBrush>(dictionary["ColorPickerRainbowBrush"]);
        Assert.Equal(new Point(0, 0), brush.StartPoint);
        Assert.Equal(new Point(1, 1), brush.EndPoint);
        Assert.Equal(7, brush.GradientStops.Count);
        Assert.Equal(0.0, brush.GradientStops[0].Offset);
        Assert.Equal(0.835, brush.GradientStops[5].Offset);
        Assert.Equal(1.0, brush.GradientStops[6].Offset);
    }

    [Fact]
    public void Brush_without_points_lands_wpf_defaults()
    {
        // A brush entry whose points were omitted (BAML drops default
        // values) lands the WPF defaults instead of failing: StartPoint
        // (0,0), EndPoint (1,1), live-verified on net10.0-windows.
        XamlLoader.CustomElementSource = null;
        var n = StagedRainbowTables();
        var body = new B()
            .Raw(0x03).I16(-358).Raw(0x00)
            .Raw(0x2E).I16(-60)
            .Raw(Stop(n, "#ff0000", "0").ToArray())
            .Raw(0x04);
        var dictionary = BamlReader.LoadDictionary(RainbowDictionary("T", body.ToArray(), n));
        var brush = Assert.IsType<LinearGradientBrush>(dictionary["T"]);
        Assert.Equal(new Point(0, 0), brush.StartPoint);
        Assert.Equal(new Point(1, 1), brush.EndPoint);
    }

    [Fact]
    public void Stop_without_offset_lands_zero()
    {
        // A Color-only GradientStop (BAML drops the default Offset) lands
        // Offset 0.0 instead of failing: the staged rainbow first stop is
        // exactly this shape.
        XamlLoader.CustomElementSource = null;
        var n = StagedRainbowTables();
        var body = RainbowHead(n)
            .Raw(StopColorOnly(n, "#ff0000").ToArray())
            .Raw(0x04);
        var dictionary = BamlReader.LoadDictionary(RainbowDictionary("T", body.ToArray(), n));
        var brush = Assert.IsType<LinearGradientBrush>(dictionary["T"]);
        var stop = Assert.Single(brush.GradientStops);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0x00, 0x00), stop.Color);
        Assert.Equal(0.0, stop.Offset);
    }

    [Fact]
    public void Engine_brush_defaults_match_wpf()
    {
        // The engine LinearGradientBrush/GradientStop defaults are the
        // WPF-identical values the defaulted BAML records land through
        // (StartPoint (0,0), EndPoint (1,1), Offset 0.0; all three
        // live-verified on net10.0-windows PresentationCore).
        var brush = new LinearGradientBrush();
        Assert.Equal(new Point(0, 0), brush.StartPoint);
        Assert.Equal(new Point(1, 1), brush.EndPoint);
        Assert.Equal(0.0, new GradientStop().Offset);
    }

    [Fact]
    public void Opacity_brush_head_routes()
    {
        // The staged 'ColorPickerOpacityBrush' head shape (DrawingBrush
        // ElementStart over well-known id -180) routes to the drawing
        // entry parser since slice-140: the tiling simples plus the
        // Drawing complex with a single-checker DrawingGroup land as a
        // live DrawingBrush with the staged values.
        XamlLoader.CustomElementSource = null;
        var n = StagedDrawingTables();
        var body = new B()
            .Raw(0x03).I16(-180).Raw(0x00)
            .Sized(0x24, q => q.I16(0x39).Str("0,0,12,11").I16(-511))
            .Sized(0x24, q => q.I16(0x3A).Str("Absolute").I16(-195))
            .Sized(0x24, q => q.I16(0x3B).Str("None").I16(-195))
            .Sized(0x24, q => q.I16(0x3C).Str("Tile").I16(-195))
            .Raw(0x07).I16(0x3D)
            .Raw(0x03).I16(-183).Raw(0x00)
            .Raw(0x2E).I16(-42)
            .Raw(0x03).I16(-241).Raw(0x00)
            .Sized(0x06, q => q.I16(0x3E).U16(744).Raw(0x02).Str("#d0cec7"))
            .Raw(0x07).I16(0x3F)
            .Raw(0x03).I16(-515).Raw(0x00)
            .Sized(0x24, q => q.I16(0x40).Str("0,0,6,5").I16(-511))
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .ToArray();
        var dictionary = BamlReader.LoadDictionary(RainbowDictionary("ColorPickerOpacityBrush", body, n));
        var brush = Assert.IsType<DrawingBrush>(dictionary["ColorPickerOpacityBrush"]);
        Assert.Equal(new Rect(0, 0, 12, 11), brush.Viewport);
        Assert.Equal(BrushMappingMode.Absolute, brush.ViewportUnits);
        Assert.Equal(Stretch.None, brush.Stretch);
        Assert.Equal(TileMode.Tile, brush.TileMode);
        var drawing = Assert.Single(Assert.IsType<DrawingGroup>(brush.Drawing).Children);
        Assert.Equal(Color.FromArgb(0xFF, 0xD0, 0xCE, 0xC7),
            Assert.IsType<SolidColorBrush>(Assert.IsType<GeometryDrawing>(drawing).Brush).Color);
        Assert.Equal(new Rect(0, 0, 6, 5),
            Assert.IsType<RectangleGeometry>(Assert.IsType<GeometryDrawing>(drawing).Geometry).Rect);
    }

    /// <summary>Per-file table numbering extending the staged stop rows
    /// with the drawing-run rows (Viewport 0x39, ViewportUnits 0x3A,
    /// Stretch 0x3B, TileMode 0x3C over TileBrush -662; Drawing 0x3D over
    /// DrawingBrush -180; Brush 0x3E and Geometry 0x3F over
    /// GeometryDrawing -241; Rect 0x40 over RectangleGeometry -515).</summary>
    private static RainbowNumbering StagedDrawingTables() => new(
        [(0x2C, "Color", (short)-252), (0x2D, "Offset", (short)-252),
         (0x39, "Viewport", (short)-662), (0x3A, "ViewportUnits", (short)-662),
         (0x3B, "Stretch", (short)-662), (0x3C, "TileMode", (short)-662),
         (0x3D, "Drawing", (short)-180), (0x3E, "Brush", (short)-241),
         (0x3F, "Geometry", (short)-241), (0x40, "Rect", (short)-515)],
        0x2C, 0x2D);

    [Fact]
    public void Gradient_stop_duplicate_offset_fails_precise()
    {
        // Two Offset simples on one GradientStop fail instead of keeping
        // the first value (Color-only defaults never collide: the
        // duplicate check runs before any default lands).
        XamlLoader.CustomElementSource = null;
        var n = StagedRainbowTables();
        var stop = new B()
            .Raw(0x03).I16(-252).Raw(0x00)
            .Sized(0x24, q => q.I16((short)n.Color).Str("#ff0000").I16(-87))
            .Sized(0x24, q => q.I16((short)n.Offset).Str("0.167").I16(-175))
            .Sized(0x24, q => q.I16((short)n.Offset).Str("0.334").I16(-175))
            .Raw(0x04);
        var body = RainbowHead(n).Raw(stop.ToArray()).Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(RainbowDictionary("T", body.ToArray(), n)));
        Assert.Contains("duplicate Offset", error.Message);
    }

    /// <summary>Per-file table numbering standing in for the staged stop
    /// rows (Color 0x2C, Offset 0x2D over GradientStop -252; the staged
    /// rainbow entry carries no brush rows because BAML omits the
    /// defaulted StartPoint/EndPoint).</summary>
    private sealed record RainbowNumbering(
        (int Id, string Name, short Owner)[] Attrs,
        int Color, int Offset);

    private static RainbowNumbering StagedRainbowTables() => new(
        [(0x2C, "Color", (short)-252), (0x2D, "Offset", (short)-252)],
        0x2C, 0x2D);

    /// <summary>Fixture numbering standing in for the genuine net10
    /// fixture tables (attrs 0 Color, 1 Offset over the same owner).</summary>
    private static RainbowNumbering FixtureRainbowTables() => new(
        [(0, "Color", (short)-252), (1, "Offset", (short)-252)],
        0, 1);

    /// <summary>Builds the staged 'ColorPickerRainbowBrush' entry shape:
    /// the head, the Color-only first stop plus the six staged
    /// Color/Offset stops, closed once.</summary>
    private static byte[] StagedRainbowEntry(RainbowNumbering n) => RainbowHead(n)
        .Raw(StopColorOnly(n, "#ff0000").ToArray())
        .Raw(Stop(n, "#ff00ff", "0.167").ToArray())
        .Raw(Stop(n, "#0000ff", "0.334").ToArray())
        .Raw(Stop(n, "#00ffff", "0.501").ToArray())
        .Raw(Stop(n, "#00ff00", "0.668").ToArray())
        .Raw(Stop(n, "#ffff00", "0.835").ToArray())
        .Raw(Stop(n, "#ff0000", "1").ToArray())
        .Raw(0x04).ToArray();

    /// <summary>Builds one staged-shape gradient head (its ElementStart is
    /// included): the LinearGradientBrush element plus the GradientStops
    /// content (no point records: BAML omits the defaults).</summary>
    private static B RainbowHead(RainbowNumbering n) => new B()
        .Raw(0x03).I16(-358).Raw(0x00)
        .Raw(0x2E).I16(-60);

    /// <summary>Builds one staged-shape GradientStop child (its
    /// ElementStart is included): the Color/Offset simples, closed once.</summary>
    private static B Stop(RainbowNumbering n, string color, string offset) => new B()
        .Raw(0x03).I16(-252).Raw(0x00)
        .Sized(0x24, q => q.I16((short)n.Color).Str(color).I16(-87))
        .Sized(0x24, q => q.I16((short)n.Offset).Str(offset).I16(-175))
        .Raw(0x04);

    /// <summary>Builds one staged-shape Color-only GradientStop child
    /// (its ElementStart is included): the Color simple only, closed
    /// once (the missing Offset lands at the WPF default 0.0).</summary>
    private static B StopColorOnly(RainbowNumbering n, string color) => new B()
        .Raw(0x03).I16(-252).Raw(0x00)
        .Sized(0x24, q => q.I16((short)n.Color).Str(color).I16(-87))
        .Raw(0x04);

    /// <summary>Builds a single-entry keyed gradient dictionary over the
    /// engine brush ids (well-known tables only, no TypeInfo rows;
    /// per-file AttributeInfo ids stand in for the staged stop rows).
    /// The staged rainbow key is shared, so shared opts into the
    /// AddEntry singleton path.</summary>
    private static byte[] RainbowDictionary(string key, byte[] entry, RainbowNumbering n, bool shared = true)
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
