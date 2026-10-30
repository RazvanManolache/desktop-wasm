// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-140 HandyControl theme.baml ColorPickerOpacityBrush run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the keyed 'ColorPickerOpacityBrush' DrawingBrush entry keyidx 154 at
/// [BAML+0x19383, BAML+0x1943B) lands as a live engine DrawingBrush: the
/// Viewport "0,0,12,11" (Absolute units), Stretch None and TileMode Tile
/// PropertyWithConverter values plus the Drawing complex holding one
/// DrawingGroup with two GeometryDrawing children ("#d0cec7" over a
/// two-rect GeometryGroup "0,0,6,5"/"6,6,6,5" and "#e7e7e2" over a single
/// RectangleGeometry "0,5,12,1"). The run continues past the keyed
/// 'CoverFlowBaseStyle' Style entry keyidx 155 (slice-141 routes its
/// template Border Viewport3D child) and the keyed
/// 'CoverViewItemBaseStyle' Style entry keyidx 156, and past the keyed
/// 'GridViewColumnHeaderGripper' Style entry keyidx 157 scalars
/// (slice-142 routes the qualified Canvas.Right setter plus Width,
/// Binding Height, Padding and Background), stopping precise at the
/// Template setter at BAML+0x196A0 (its Border Padding TemplateBinding
/// at BAML+0x196C6 names a DP the engine Border does not declare).
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways: the staged bytes (the 03 4C FF brush head,
/// the four TileBrush PWCs over rows 0x39-0x3C, the 07 3D 00 Drawing
/// complex, the 03 49 FF DrawingGroup with its D6 FF Children content,
/// the two 03 0F FF GeometryDrawings with 06/E8 02 text-form brush
/// customs "#d0cec7"/"#e7e7e2", the 07 3F 00 Geometry complexes, the
/// 03 0E FF GeometryGroup with its C5 FF Children content, the three
/// 03 FD FD RectangleGeometries with 01 FE RectConverter PWCs over row
/// 0x40, the 04/08 close runs, the key records 0x26 key ids 0x90-0x92,
/// the keyidx-155 Style head 03 94 FD with its CoverFlow TargetType plus
/// Focusable/Template setters, and the 03 27 FD Viewport3D child head
/// with its PART_Viewport3D Name PWC); the live upstream HandyControl
/// ColorPickerBaseStyle.xaml ColorPickerOpacityBrush block at pinned
/// commit e9ad3d70 (the same Viewport/ViewportUnits/Stretch/TileMode
/// plus the DrawingGroup/GeometryDrawing/GeometryGroup content); a
/// genuine net10.0-windows fixture compiling the exact upstream block
/// into Opacity.baml (1182 bytes, emitting 03 4C FF with the same PWC
/// rows, the same 06/E8 02 text-form brush customs and the same
/// 03 0F FF/03 0E FF/03 FD FD element nesting, identical in every
/// staged record shape modulo per-file table numbering plus line
/// markers the staged file omits; fixture retained at
/// E:/tmp/bamlfix140 outside the tree) plus a live net10.0-windows
/// defaults probe (TileBrush Viewport 0,0,1,1, ViewportUnits
/// RelativeToBoundingBox, Stretch Fill, TileMode None, DrawingGroup
/// Children empty).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice140BamlOpacityTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice140BamlOpacityTests()
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
    public void Theme_baml_lands_opacity_brush_through_coverflow_head()
    {
        // Staged framing pins for the keyed 'ColorPickerOpacityBrush'
        // DrawingBrush entry keyidx 154 spanning [0x19383, 0x1943B) (slice-141
        // lands the keyidx-155 CoverFlow entry behind it and stops at the
        // keyidx-157 Gripper; this test keeps pinning the opacity bytes plus
        // the CoverFlow head as landed context): the
        // 03 4C FF head, the four TileBrush PWCs (Viewport "0,0,12,11"
        // with the RectConverter, ViewportUnits "Absolute", Stretch
        // "None" and TileMode "Tile" with the EnumConverter), the
        // 07 3D 00 Drawing complex, the 03 49 FF DrawingGroup with its
        // D6 FF Children content, the first 03 0F FF GeometryDrawing with
        // the 06/E8 02 "#d0cec7" text-form brush custom and the 07 3F 00
        // Geometry complex, the 03 0E FF GeometryGroup with its C5 FF
        // Children content, the two RectangleGeometries ("0,0,6,5" and
        // "6,6,6,5" with the RectConverter), the close runs, the second
        // GeometryDrawing ("#e7e7e2" over the single "0,5,12,1" rect),
        // the group/complex/brush closes, the 0x26 key records for
        // keyidx 154-156, and the keyidx-155 'CoverFlowBaseStyle' head at
        // 0x1943B (03 94 FD Style over the CoverFlow TargetType with its
        // Setters content) plus the 0x19497 Viewport3D child head with
        // its PART_Viewport3D Name PWC asserting the exact next frontier.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");

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
        var geometryDrawing = new byte[] { 0x03, 0x0F, 0xFF, 0x00 };
        Assert.Equal(geometryDrawing, theme[0x193C6..0x193CA]);
        var brushCustom0 = new byte[] { 0x06, 0x0E, 0x3E, 0x00, 0xE8, 0x02, 0x02, 0x07, 0x23, 0x64, 0x30, 0x63, 0x65, 0x63, 0x37 };
        Assert.Equal(brushCustom0, theme[0x193CA..0x193D9]);
        var geometryStart = new byte[] { 0x07, 0x3F, 0x00 };
        Assert.Equal(geometryStart, theme[0x193D9..0x193DC]);
        var geometryGroup = new byte[] { 0x03, 0x0E, 0xFF, 0x00 };
        Assert.Equal(geometryGroup, theme[0x193DC..0x193E0]);
        var geometryChildren = new byte[] { 0x2E, 0xC5, 0xFF };
        Assert.Equal(geometryChildren, theme[0x193E0..0x193E3]);
        var rectangleGeometry = new byte[] { 0x03, 0xFD, 0xFD, 0x00 };
        Assert.Equal(rectangleGeometry, theme[0x193E3..0x193E7]);
        var rect0 = new byte[] { 0x24, 0x0D, 0x40, 0x00, 0x07, 0x30, 0x2C, 0x30, 0x2C, 0x36, 0x2C, 0x35, 0x01, 0xFE };
        Assert.Equal(rect0, theme[0x193E7..0x193F5]);
        Assert.Equal(new byte[] { 0x04 }, theme[0x193F5..0x193F6]);
        Assert.Equal(rectangleGeometry, theme[0x193F6..0x193FA]);
        var rect1 = new byte[] { 0x24, 0x0D, 0x40, 0x00, 0x07, 0x36, 0x2C, 0x36, 0x2C, 0x36, 0x2C, 0x35, 0x01, 0xFE };
        Assert.Equal(rect1, theme[0x193FA..0x19408]);
        Assert.Equal(new byte[] { 0x04, 0x04, 0x08, 0x04 }, theme[0x19408..0x1940C]);
        Assert.Equal(geometryDrawing, theme[0x1940C..0x19410]);
        var brushCustom1 = new byte[] { 0x06, 0x0E, 0x3E, 0x00, 0xE8, 0x02, 0x02, 0x07, 0x23, 0x65, 0x37, 0x65, 0x37, 0x65, 0x32 };
        Assert.Equal(brushCustom1, theme[0x19410..0x1941F]);
        Assert.Equal(geometryStart, theme[0x1941F..0x19422]);
        Assert.Equal(rectangleGeometry, theme[0x19422..0x19426]);
        var rect2 = new byte[] { 0x24, 0x0E, 0x40, 0x00, 0x08, 0x30, 0x2C, 0x35, 0x2C, 0x31, 0x32, 0x2C, 0x31, 0x01, 0xFE };
        Assert.Equal(rect2, theme[0x19426..0x19435]);
        Assert.Equal(new byte[] { 0x04, 0x08, 0x04, 0x04, 0x08, 0x04 }, theme[0x19435..0x1943B]);

        var key154 = new byte[] { 0x26, 0x09, 0x90, 0x00, 0xEC, 0xA1, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key154, theme[0xB52D..0xB537]);
        var key155 = new byte[] { 0x26, 0x09, 0x91, 0x00, 0xA4, 0xA2, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key155, theme[0xB537..0xB541]);
        var key156 = new byte[] { 0x26, 0x09, 0x92, 0x00, 0xD5, 0xA3, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key156, theme[0xB541..0xB54B]);
        var coverFlowHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x34, 0x00, 0x2E, 0x12, 0xFF, 0x03, 0xD4, 0xFD };
        Assert.Equal(coverFlowHead, theme[0x1943B..0x1944A]);
        var viewport3dHead = new byte[] { 0x03, 0x27, 0xFD, 0x00 };
        Assert.Equal(viewport3dHead, theme[0x19497..0x1949B]);
        var viewport3dName = new byte[] { 0x24, 0x15, 0x41, 0x00, 0x0F, 0x50, 0x41, 0x52, 0x54, 0x5F, 0x56, 0x69, 0x65, 0x77, 0x70, 0x6F, 0x72, 0x74, 0x33, 0x44, 0x99, 0xFD };
        Assert.Equal(viewport3dName, theme[0x1949B..0x194B1]);
    }

    [Fact]
    public void Staged_boot_advances_past_opacity_to_gripper_style()
    {
        // The staged boot materializes entries #154 (the
        // 'ColorPickerOpacityBrush' DrawingBrush) through #156 (the
        // CoverFlow/CoverViewItem styles, landed by slice-141) and stops
        // at the keyed 'GridViewColumnHeaderGripper' Style entry keyidx
        // 157 (slice-142 routes its Canvas.Right/Width/Height/Padding/
        // Background setters; the Template setter has no Border Padding
        // routing: the precise next blocker). Every earlier stop stays
        // absent, and the new head asserts the exact frontier.
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
        Assert.DoesNotContain("template borders take", inner.Message);
    }

    [Fact]
    public void Staged_shape_opacity_lands_live()
    {
        // The staged 'ColorPickerOpacityBrush' entry shape (Viewport
        // "0,0,12,11" with Absolute units, Stretch None, TileMode Tile
        // plus the two-checker DrawingGroup) lands as a live engine
        // DrawingBrush. Per-file ids 0x39-0x40 stand in for the staged
        // TileBrush/Drawing/GeometryDrawing/RectangleGeometry rows.
        LoadStagedHandyControlEntry();
        var n = StagedOpacityTables();
        var dictionary = BamlReader.LoadDictionary(OpacityDictionary("ColorPickerOpacityBrush", StagedOpacityEntry(n), n));
        var brush = Assert.IsType<DrawingBrush>(dictionary["ColorPickerOpacityBrush"]);
        Assert.Equal(new Rect(0, 0, 12, 11), brush.Viewport);
        Assert.Equal(BrushMappingMode.Absolute, brush.ViewportUnits);
        Assert.Equal(Stretch.None, brush.Stretch);
        Assert.Equal(TileMode.Tile, brush.TileMode);
        var group = Assert.IsType<DrawingGroup>(brush.Drawing);
        Assert.Equal(2, group.Children.Count);
        var first = Assert.IsType<GeometryDrawing>(group.Children[0]);
        Assert.Equal(Color.FromArgb(0xFF, 0xD0, 0xCE, 0xC7), Assert.IsType<SolidColorBrush>(first.Brush).Color);
        var members = Assert.IsType<GeometryGroup>(first.Geometry);
        Assert.Equal(2, members.Children.Count);
        Assert.Equal(new Rect(0, 0, 6, 5), Assert.IsType<RectangleGeometry>(members.Children[0]).Rect);
        Assert.Equal(new Rect(6, 6, 6, 5), Assert.IsType<RectangleGeometry>(members.Children[1]).Rect);
        var second = Assert.IsType<GeometryDrawing>(group.Children[1]);
        Assert.Equal(Color.FromArgb(0xFF, 0xE7, 0xE7, 0xE2), Assert.IsType<SolidColorBrush>(second.Brush).Color);
        var solo = Assert.IsType<RectangleGeometry>(second.Geometry);
        Assert.Equal(new Rect(0, 5, 12, 1), solo.Rect);
    }

    [Fact]
    public void Shared_opacity_lookups_return_same_instance()
    {
        // The staged opacity key is shared, so every lookup returns the
        // same DrawingBrush instance.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var dictionary = BamlReader.LoadDictionary(OpacityDictionary(
            "ColorPickerOpacityBrush", StagedOpacityEntry(n), n));
        var first = Assert.IsType<DrawingBrush>(dictionary["ColorPickerOpacityBrush"]);
        Assert.Same(first, Assert.IsType<DrawingBrush>(dictionary["ColorPickerOpacityBrush"]));
        Assert.Equal(new Rect(0, 0, 12, 11), first.Viewport);
        Assert.Equal(TileMode.Tile, first.TileMode);
        Assert.Equal(2, Assert.IsType<DrawingGroup>(first.Drawing).Children.Count);
    }

    [Fact]
    public void Non_shared_opacity_lookups_materialize_fresh_instances()
    {
        // Non-shared keys replay the validated value bags through a
        // factory, so every lookup materializes a fresh brush with equal
        // values (tiling knobs, checker colors and rects), never a shared
        // singleton.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var dictionary = BamlReader.LoadDictionary(OpacityDictionary(
            "ColorPickerOpacityBrush", StagedOpacityEntry(n), n, shared: false));
        var first = Assert.IsType<DrawingBrush>(dictionary["ColorPickerOpacityBrush"]);
        var second = Assert.IsType<DrawingBrush>(dictionary["ColorPickerOpacityBrush"]);
        Assert.NotSame(first, second);
        Assert.Equal(first.Viewport, second.Viewport);
        Assert.Equal(first.ViewportUnits, second.ViewportUnits);
        Assert.Equal(first.Stretch, second.Stretch);
        Assert.Equal(first.TileMode, second.TileMode);
        var firstGroup = Assert.IsType<DrawingGroup>(first.Drawing);
        var secondGroup = Assert.IsType<DrawingGroup>(second.Drawing);
        Assert.NotSame(firstGroup, secondGroup);
        Assert.Equal(firstGroup.Children.Count, secondGroup.Children.Count);
        Assert.Equal(
            Assert.IsType<SolidColorBrush>(Assert.IsType<GeometryDrawing>(firstGroup.Children[0]).Brush).Color,
            Assert.IsType<SolidColorBrush>(Assert.IsType<GeometryDrawing>(secondGroup.Children[0]).Brush).Color);
        Assert.Equal(
            Assert.IsType<RectangleGeometry>(Assert.IsType<GeometryDrawing>(firstGroup.Children[1]).Geometry).Rect,
            Assert.IsType<RectangleGeometry>(Assert.IsType<GeometryDrawing>(secondGroup.Children[1]).Geometry).Rect);
    }

    [Fact]
    public void Fixture_numbered_opacity_lands_with_line_markers()
    {
        // The genuine net10.0-windows fixture numbers its tables from zero
        // (attrs 0-7 over the TileBrush/DrawingBrush/GeometryDrawing/
        // RectangleGeometry owners) and carries 0x35/0x36 line markers:
        // the parser resolves everything through the tables, so the same
        // drawing shape lands with line markers transparent (Viewport
        // plus checker rects and colors).
        XamlLoader.CustomElementSource = null;
        var numbering = FixtureOpacityTables();
        var entry = new B()
            .Raw(0x03).I16(-180).Raw(0x00)
            .Raw(0x35).U32(6).U32(10)
            .Raw(Pwc(numbering, 0, "0,0,12,11", -511).ToArray())
            .Raw(0x36).U32(12)
            .Raw(Pwc(numbering, 1, "Absolute", -195).ToArray())
            .Raw(Pwc(numbering, 2, "None", -195).ToArray())
            .Raw(Pwc(numbering, 3, "Tile", -195).ToArray())
            .Raw(0x07).I16(4)
            .Raw(0x03).I16(-183).Raw(0x00)
            .Raw(0x2E).I16(-42)
            .Raw(Drawing(numbering, "#d0cec7",
                new B().Raw(Group(numbering, "0,0,6,5", "6,6,6,5").ToArray()).ToArray()).ToArray())
            .Raw(Drawing(numbering, "#e7e7e2",
                new B().Raw(Solo(numbering, "0,5,12,1").ToArray()).ToArray()).ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .ToArray();
        var dictionary = BamlReader.LoadDictionary(OpacityDictionary("ColorPickerOpacityBrush", entry, numbering));
        var brush = Assert.IsType<DrawingBrush>(dictionary["ColorPickerOpacityBrush"]);
        Assert.Equal(new Rect(0, 0, 12, 11), brush.Viewport);
        Assert.Equal(BrushMappingMode.Absolute, brush.ViewportUnits);
        Assert.Equal(Stretch.None, brush.Stretch);
        Assert.Equal(TileMode.Tile, brush.TileMode);
        var group = Assert.IsType<DrawingGroup>(brush.Drawing);
        Assert.Equal(2, group.Children.Count);
        Assert.Equal(Color.FromArgb(0xFF, 0xD0, 0xCE, 0xC7),
            Assert.IsType<SolidColorBrush>(Assert.IsType<GeometryDrawing>(group.Children[0]).Brush).Color);
        Assert.Equal(new Rect(0, 5, 12, 1),
            Assert.IsType<RectangleGeometry>(Assert.IsType<GeometryDrawing>(group.Children[1]).Geometry).Rect);
    }

    [Fact]
    public void Brush_without_simples_lands_engine_defaults()
    {
        // A brush entry whose tiling simples were omitted (BAML drops
        // default values) lands the engine TileBrush defaults the XAML
        // loader lands for omitted attributes: Viewport (0,0,1,1),
        // RelativeToBoundingBox units, Fill stretch and Tile tiling, with
        // the checker drawing intact.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = new B()
            .Raw(0x03).I16(-180).Raw(0x00)
            .Raw(0x07).I16((short)n.Drawing)
            .Raw(0x03).I16(-183).Raw(0x00)
            .Raw(0x2E).I16(-42)
            .Raw(Drawing(n, "#d0cec7",
                new B().Raw(Solo(n, "0,0,6,5").ToArray()).ToArray()).ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .ToArray();
        var dictionary = BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n));
        var brush = Assert.IsType<DrawingBrush>(dictionary["T"]);
        Assert.Equal(new Rect(0, 0, 1, 1), brush.Viewport);
        Assert.Equal(BrushMappingMode.RelativeToBoundingBox, brush.ViewportUnits);
        Assert.Equal(Stretch.Fill, brush.Stretch);
        Assert.Equal(TileMode.Tile, brush.TileMode);
        Assert.Single(Assert.IsType<DrawingGroup>(brush.Drawing).Children);
    }

    [Fact]
    public void Engine_tilebrush_defaults_match_xaml_loader()
    {
        // The engine TileBrush CLR defaults are the values both the XAML
        // loader and the BAML drawing run land for omitted attributes
        // (Viewport (0,0,1,1), RelativeToBoundingBox, Fill, Tile). Note:
        // a live net10.0-windows probe shows real WPF defaults TileMode
        // to None, while the engine keeps Tile; an existing
        // ConvertedGeometryInteractionTests pin holds the engine default,
        // so the default stays and BAML matches the XAML loader.
        var brush = new DrawingBrush();
        Assert.Equal(new Rect(0, 0, 1, 1), brush.Viewport);
        Assert.Equal(BrushMappingMode.RelativeToBoundingBox, brush.ViewportUnits);
        Assert.Equal(Stretch.Fill, brush.Stretch);
        Assert.Equal(TileMode.Tile, brush.TileMode);
    }

    [Fact]
    public void Viewport_wrong_converter_fails_precise()
    {
        // A Viewport simple over any other converter (here the
        // EnumConverter) fails instead of landing an unconverted rect.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = new B()
            .Raw(0x03).I16(-180).Raw(0x00)
            .Raw(BadPwc(n, n.Viewport, "0,0,12,11", -195).ToArray())
            .Raw(Pwc(n, n.ViewportUnits, "Absolute", -195).ToArray())
            .Raw(Pwc(n, n.Stretch, "None", -195).ToArray())
            .Raw(Pwc(n, n.TileMode, "Tile", -195).ToArray())
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("Viewport value with converter -195", error.Message);
        Assert.Contains("RectConverter", error.Message);
    }

    [Fact]
    public void Viewport_garbage_text_fails_precise()
    {
        // A Viewport text that is not a rect stays loud instead of
        // landing a zero viewport.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n, viewport: "wide")
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("'wide' is not a Rect for Viewport", error.Message);
    }

    [Fact]
    public void Viewport_duplicate_fails_precise()
    {
        // A second Viewport simple fails instead of keeping the first
        // value.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n)
            .Raw(Pwc(n, n.Viewport, "0,0,12,11", -511).ToArray())
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("duplicate Viewport", error.Message);
    }

    [Fact]
    public void ViewportUnits_garbage_fails_precise()
    {
        // A ViewportUnits text outside BrushMappingMode stays loud.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n, units: "Sideways")
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("'Sideways' is not a ViewportUnits value", error.Message);
    }

    [Fact]
    public void Stretch_garbage_fails_precise()
    {
        // A Stretch text outside the Stretch enum stays loud.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n, stretch: "Sideways")
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("'Sideways' is not a Stretch value", error.Message);
    }

    [Fact]
    public void TileMode_garbage_fails_precise()
    {
        // A TileMode text outside the TileMode enum stays loud.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n, tile: "Mirror")
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("'Mirror' is not a TileMode value", error.Message);
    }

    [Fact]
    public void Tiling_knob_duplicate_fails_precise()
    {
        // A second Stretch simple fails instead of keeping the first
        // value (representative for the three enum knobs, which share
        // the duplicate gate).
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n)
            .Raw(Pwc(n, n.Stretch, "None", -195).ToArray())
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("duplicate Stretch", error.Message);
    }

    [Fact]
    public void Unknown_brush_attribute_fails_precise()
    {
        // A simple over a row the brush does not take (here the
        // GeometryDrawing.Brush row at brush level) fails naming the
        // attribute and the supported surface.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n)
            .Raw(Pwc(n, n.Brush, "#d0cec7", -87).ToArray())
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("attribute 'Brush' in drawing entry", error.Message);
        Assert.Contains("Viewport/ViewportUnits/Stretch/TileMode", error.Message);
    }

    [Fact]
    public void Missing_drawing_fails_precise()
    {
        // A brush entry with no Drawing complex fails instead of
        // landing a brush with a null drawing.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n).Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("no Drawing", error.Message);
    }

    [Fact]
    public void Empty_drawing_group_fails_precise()
    {
        // A DrawingGroup with its Children content but no
        // GeometryDrawing child fails instead of landing an empty
        // checker.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("no GeometryDrawing", error.Message);
    }

    [Fact]
    public void Duplicate_drawing_fails_precise()
    {
        // A second Drawing complex fails instead of replacing the
        // checker group.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(Drawing(n, "#d0cec7", Solo(n, "0,0,6,5").ToArray()).ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(Drawing(n, "#d0cec7", Solo(n, "0,0,6,5").ToArray()).ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("duplicate Drawing", error.Message);
    }

    [Fact]
    public void Drawing_child_not_group_fails_precise()
    {
        // A non-DrawingGroup element under the Drawing complex (here a
        // bare GeometryDrawing) fails naming the element and the
        // expected child.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n)
            .Raw(0x07).I16((short)n.Drawing)
            .Raw(Drawing(n, "#d0cec7", Solo(n, "0,0,6,5").ToArray()).ToArray())
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("GeometryDrawing", error.Message);
        Assert.Contains("takes one DrawingGroup", error.Message);
    }

    [Fact]
    public void Missing_brush_fails_precise()
    {
        // A GeometryDrawing with Geometry but no Brush fails instead of
        // landing an unpainted checker step.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var drawing = new B()
            .Raw(0x03).I16(-241).Raw(0x00)
            .Raw(GeometryOpen(n).ToArray())
            .Raw(Solo(n, "0,0,6,5").ToArray())
            .Raw(0x08)
            .Raw(0x04);
        var body = BrushHead(n)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(drawing.ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("has no Brush", error.Message);
    }

    [Fact]
    public void Missing_geometry_fails_precise()
    {
        // A GeometryDrawing with Brush but no Geometry fails instead of
        // landing a geometry-less checker step.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var drawing = new B()
            .Raw(0x03).I16(-241).Raw(0x00)
            .Raw(BrushCustom(n, "#d0cec7").ToArray())
            .Raw(0x04);
        var body = BrushHead(n)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(drawing.ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("has no Geometry", error.Message);
    }

    [Fact]
    public void Duplicate_brush_custom_fails_precise()
    {
        // A second Brush custom fails instead of keeping the first
        // checker color.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var drawing = new B()
            .Raw(0x03).I16(-241).Raw(0x00)
            .Raw(BrushCustom(n, "#d0cec7").ToArray())
            .Raw(BrushCustom(n, "#e7e7e2").ToArray())
            .Raw(0x04);
        var body = BrushHead(n)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(drawing.ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("duplicate Brush", error.Message);
    }

    [Fact]
    public void Brush_wrong_serializer_fails_precise()
    {
        // A brush custom with any other serializer (here the
        // DependencyProperty serializer) fails instead of landing an
        // unconverted brush.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var custom = new B()
            .Sized(0x06, q => q.I16((short)n.Brush).U16(137).Raw(0x02).Str("#d0cec7"));
        var drawing = new B()
            .Raw(0x03).I16(-241).Raw(0x00)
            .Raw(custom.ToArray())
            .Raw(0x04);
        var body = BrushHead(n)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(drawing.ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("brush custom value with serializer 137", error.Message);
    }

    [Fact]
    public void Brush_packed_marker_fails_precise()
    {
        // The packed-ARGB marker (0x01) the setter path lands stays a
        // precise follow-up on the drawing path: only the 0x02 text
        // form lands here.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var custom = new B()
            .Sized(0x06, q => q.I16((short)n.Brush).U16(744).Raw(0x01, 0xFF, 0xD0, 0xCE, 0xC7));
        var drawing = new B()
            .Raw(0x03).I16(-241).Raw(0x00)
            .Raw(custom.ToArray())
            .Raw(0x04);
        var body = BrushHead(n)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(drawing.ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("marked 0x01", error.Message);
    }

    [Fact]
    public void Brush_garbage_color_fails_precise()
    {
        // A brush custom whose text is not a color stays loud instead of
        // landing a transparent checker step.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(Drawing(n, "notacolor", Solo(n, "0,0,6,5").ToArray()).ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("'notacolor' is not a color", error.Message);
    }

    [Fact]
    public void Geometry_child_unsupported_fails_precise()
    {
        // A non-geometry element under the Geometry complex (here a
        // GradientStop) fails naming the element and the expected
        // children.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var drawing = new B()
            .Raw(0x03).I16(-241).Raw(0x00)
            .Raw(BrushCustom(n, "#d0cec7").ToArray())
            .Raw(GeometryOpen(n).ToArray())
            .Raw(0x03).I16(-252).Raw(0x00)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var body = BrushHead(n)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(drawing.ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("GradientStop", error.Message);
        Assert.Contains("one GeometryGroup or RectangleGeometry", error.Message);
    }

    [Fact]
    public void Group_child_not_rectangle_fails_precise()
    {
        // A non-RectangleGeometry child under the GeometryGroup (here a
        // nested GeometryDrawing) fails naming the element and the
        // expected children.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var nested = Drawing(n, "#d0cec7", Solo(n, "0,0,6,5").ToArray());
        var geometry = new B()
            .Raw(0x03).I16(-242).Raw(0x00)
            .Raw(0x2E).I16(-59)
            .Raw(nested.ToArray())
            .Raw(0x04);
        var drawing = new B()
            .Raw(0x03).I16(-241).Raw(0x00)
            .Raw(BrushCustom(n, "#d0cec7").ToArray())
            .Raw(GeometryOpen(n).ToArray())
            .Raw(geometry.ToArray())
            .Raw(0x08)
            .Raw(0x04);
        var body = BrushHead(n)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(drawing.ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("GeometryDrawing", error.Message);
        Assert.Contains("RectangleGeometry children", error.Message);
    }

    [Fact]
    public void Missing_rect_fails_precise()
    {
        // A RectangleGeometry with no Rect fails instead of landing a
        // zero-area checker tile.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(Drawing(n, "#d0cec7", new B().Raw(0x03).I16(-515).Raw(0x00).Raw(0x04).ToArray()).ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("has no Rect", error.Message);
    }

    [Fact]
    public void Duplicate_rect_fails_precise()
    {
        // A second Rect simple fails instead of keeping the first tile.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var rect = new B()
            .Raw(0x03).I16(-515).Raw(0x00)
            .Raw(Pwc(n, n.Rect, "0,0,6,5", -511).ToArray())
            .Raw(Pwc(n, n.Rect, "6,6,6,5", -511).ToArray())
            .Raw(0x04);
        var body = BrushHead(n)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(Drawing(n, "#d0cec7", rect.ToArray()).ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("duplicate Rect", error.Message);
    }

    [Fact]
    public void Rect_garbage_text_fails_precise()
    {
        // A Rect text that is not a rect stays loud instead of landing
        // a zero tile.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(Drawing(n, "#d0cec7", Solo(n, "tall").ToArray()).ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("'tall' is not a Rect", error.Message);
    }

    [Fact]
    public void Rect_wrong_converter_fails_precise()
    {
        // A Rect simple over any other converter (here the
        // EnumConverter) fails instead of landing an unconverted tile.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var rect = new B()
            .Raw(0x03).I16(-515).Raw(0x00)
            .Raw(BadPwc(n, n.Rect, "0,0,6,5", -195).ToArray())
            .Raw(0x04);
        var body = BrushHead(n)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(Drawing(n, "#d0cec7", rect.ToArray()).ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("Rect value with converter -195", error.Message);
        Assert.Contains("RectConverter", error.Message);
    }

    [Fact]
    public void StaticResource_on_brush_fails_precise()
    {
        // A StaticResource value on the brush fails instead of landing
        // a half-built resource: drawing static values are a
        // style-surface follow-up.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n)
            .Sized(0x38, q => q.U16(0))
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("StaticResource value on the DrawingBrush", error.Message);
    }

    [Fact]
    public void Markup_extension_on_group_fails_precise()
    {
        // A markup-extension value on the DrawingGroup fails instead of
        // landing a half-built checker: drawing markup values are a
        // style-surface follow-up.
        XamlLoader.CustomElementSource = null;
        var n = StagedOpacityTables();
        var body = BrushHead(n)
            .Raw(DrawingOpen(n).ToArray())
            .Raw(0x23).Raw(0x00, 0x00, 0x00, 0x00, 0x00, 0x00)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(OpacityDictionary("T", body.ToArray(), n)));
        Assert.Contains("markup-extension value on the DrawingGroup", error.Message);
    }

    /// <summary>Per-file table numbering standing in for the staged rows
    /// (Viewport 0x39, ViewportUnits 0x3A, Stretch 0x3B, TileMode 0x3C over
    /// TileBrush -662; Drawing 0x3D over DrawingBrush -180; Brush 0x3E and
    /// Geometry 0x3F over GeometryDrawing -241; Rect 0x40 over
    /// RectangleGeometry -515).</summary>
    private sealed record OpacityNumbering(
        (int Id, string Name, short Owner)[] Attrs,
        int Viewport, int ViewportUnits, int Stretch, int TileMode, int Drawing, int Brush, int Geometry, int Rect);

    private static OpacityNumbering StagedOpacityTables() => new(
        [(0x39, "Viewport", (short)-662), (0x3A, "ViewportUnits", (short)-662), (0x3B, "Stretch", (short)-662), (0x3C, "TileMode", (short)-662),
         (0x3D, "Drawing", (short)-180), (0x3E, "Brush", (short)-241), (0x3F, "Geometry", (short)-241), (0x40, "Rect", (short)-515)],
        0x39, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40);

    /// <summary>Fixture numbering standing in for the genuine net10
    /// fixture tables (attrs 0-7 over the same owners).</summary>
    private static OpacityNumbering FixtureOpacityTables() => new(
        [(0, "Viewport", (short)-662), (1, "ViewportUnits", (short)-662), (2, "Stretch", (short)-662), (3, "TileMode", (short)-662),
         (4, "Drawing", (short)-180), (5, "Brush", (short)-241), (6, "Geometry", (short)-241), (7, "Rect", (short)-515)],
        0, 1, 2, 3, 4, 5, 6, 7);

    /// <summary>Builds the staged 'ColorPickerOpacityBrush' entry shape:
    /// the head, the four staged tiling simples, the Drawing complex with
    /// a two-checker DrawingGroup, closed once.</summary>
    private static byte[] StagedOpacityEntry(OpacityNumbering n) => BrushHead(n)
        .Raw(DrawingOpen(n).ToArray())
        .Raw(Drawing(n, "#d0cec7", Group(n, "0,0,6,5", "6,6,6,5").ToArray()).ToArray())
        .Raw(Drawing(n, "#e7e7e2", Solo(n, "0,5,12,1").ToArray()).ToArray())
        .Raw(0x04)
        .Raw(0x08)
        .Raw(0x04)
        .ToArray();

    /// <summary>Builds one staged-shape DrawingBrush head (its ElementStart
    /// is included): the DrawingBrush element plus the four tiling
    /// simples. Null overrides fall back to the staged texts.</summary>
    private static B BrushHead(OpacityNumbering n, string? viewport = null, string? units = null, string? stretch = null, string? tile = null) => new B()
        .Raw(0x03).I16(-180).Raw(0x00)
        .Raw(Pwc(n, n.Viewport, viewport ?? "0,0,12,11", -511).ToArray())
        .Raw(Pwc(n, n.ViewportUnits, units ?? "Absolute", -195).ToArray())
        .Raw(Pwc(n, n.Stretch, stretch ?? "None", -195).ToArray())
        .Raw(Pwc(n, n.TileMode, tile ?? "Tile", -195).ToArray());

    /// <summary>Builds the Drawing complex opener plus an open DrawingGroup
    /// (its ElementStart is included): the Drawing complex start plus the
    /// DrawingGroup element with its Children content.</summary>
    private static B DrawingOpen(OpacityNumbering n) => new B()
        .Raw(0x07).I16((short)n.Drawing)
        .Raw(0x03).I16(-183).Raw(0x00)
        .Raw(0x2E).I16(-42);

    /// <summary>Builds the Geometry complex opener (the fixed 0x07 record
    /// with the per-file Geometry row).</summary>
    private static B GeometryOpen(OpacityNumbering n) => new B().Raw(0x07).I16((short)n.Geometry);

    /// <summary>Builds one staged-shape PropertyWithConverter simple (its
    /// record framing is included).</summary>
    private static B Pwc(OpacityNumbering n, int attr, string text, short conv) => new B()
        .Sized(0x24, q => q.I16((short)attr).Str(text).I16(conv));

    /// <summary>Builds one staged-shape PropertyWithConverter simple with
    /// an off-converter id (wrong-converter negatives).</summary>
    private static B BadPwc(OpacityNumbering n, int attr, string text, short conv) => Pwc(n, attr, text, conv);

    /// <summary>Builds one staged-shape text-form brush custom (its record
    /// framing is included): the per-file Brush row with the 744
    /// serializer, the 0x02 text marker and the color text.</summary>
    private static B BrushCustom(OpacityNumbering n, string color) => new B()
        .Sized(0x06, q => q.I16((short)n.Brush).U16(744).Raw(0x02).Str(color));

    /// <summary>Builds one staged-shape RectangleGeometry element (its
    /// ElementStart is included): the Rect simple, closed once.</summary>
    private static B Solo(OpacityNumbering n, string rect) => new B()
        .Raw(0x03).I16(-515).Raw(0x00)
        .Raw(Pwc(n, n.Rect, rect, -511).ToArray())
        .Raw(0x04);

    /// <summary>Builds one staged-shape GeometryGroup element (its
    /// ElementStart is included): the Children content plus one
    /// RectangleGeometry per rect, closed once.</summary>
    private static B Group(OpacityNumbering n, params string[] rects)
    {
        var b = new B()
            .Raw(0x03).I16(-242).Raw(0x00)
            .Raw(0x2E).I16(-59);
        foreach (var rect in rects) b.Raw(Solo(n, rect).ToArray());
        return b.Raw(0x04);
    }

    /// <summary>Builds one staged-shape GeometryDrawing element (its
    /// ElementStart is included): the Brush custom plus the Geometry
    /// complex around the given geometry bytes, closed once.</summary>
    private static B Drawing(OpacityNumbering n, string color, byte[] geometry) => new B()
        .Raw(0x03).I16(-241).Raw(0x00)
        .Raw(BrushCustom(n, color).ToArray())
        .Raw(GeometryOpen(n).ToArray())
        .Raw(geometry)
        .Raw(0x08)
        .Raw(0x04);

    /// <summary>Builds a single-entry keyed drawing dictionary over the
    /// engine brush ids (well-known tables only, no TypeInfo rows;
    /// per-file AttributeInfo ids stand in for the staged tiling rows).
    /// The staged opacity key is shared, so shared opts into the AddEntry
    /// singleton path.</summary>
    private static byte[] OpacityDictionary(string key, byte[] entry, OpacityNumbering n, bool shared = true)
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
