// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-141 HandyControl theme.baml CoverFlow run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the keyed 'CoverFlowBaseStyle' Style entry keyidx 155 at
/// [BAML+0x1943B, BAML+0x1956B) lands as a live engine Style (TargetType
/// the converted HandyControl.Controls.CoverFlow, Focusable False, the
/// Template setter holding the ControlTemplate whose Border visual carries
/// the three TemplateBindings with the PART_Viewport3D Viewport3D child:
/// PART_Camera PerspectiveCamera plus two DirectionalLight Content values
/// and PART_VisualParent), and the keyed 'CoverViewItemBaseStyle' Style
/// entry keyidx 156 at [BAML+0x1956C, BAML+0x195EC) lands with the existing
/// machinery (converted CoverViewItem target, Focusable False, the Template
/// setter holding the ControlTemplate whose Border visual carries the same
/// three TemplateBindings with the Header ContentPresenter child), so the
/// run stops precise at the keyed 'GridViewColumnHeaderGripper' Style entry
/// keyidx 157 at BAML+0x195ED (slice-142 routes its qualified Canvas.Right
/// setter plus Width, Binding Height, Padding and Background, stopping at
/// the Template setter whose Border Padding TemplateBinding the engine
/// Border does not declare: the precise next blocker).
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways: the staged bytes (the Style heads 03 94 FD
/// with the CoverFlow/CoverViewItem/CoverViewItem-family TargetTypes, the
/// Focusable/Template setters, the 03 27 FD Viewport3D child head with its
/// PART_Viewport3D Name PWC, the 07 42 00 Camera complex with the 03 3D FE
/// camera head, the 19 .. FFFF DefAttribute PART_Camera/PART_VisualParent
/// names, the Position/UpDirection/LookDirection/FieldOfView PWCs, the
/// 0B 78 FF Children list, the 03 67 FE ModelVisual3D heads with the
/// 07 47 00 Content complexes holding the 03 74 FF DirectionalLights, the
/// close runs, and the key records 0x26 key ids 0x91-0x94); the live
/// upstream HandyControl Theme.xaml CoverFlowBaseStyle/CoverViewItemBaseStyle/
/// GridViewColumnHeaderGripper blocks at pinned commit e9ad3d70 (the same
/// setters, the same Border TemplateBindings, the same Viewport3D/Camera/
/// Children/Light subtree, then Canvas.Right="-9" on the Thumb-targeted
/// Gripper); a genuine net10.0-windows fixture compiling the exact upstream
/// CoverFlow block with TargetType retargeted to Control (1502 bytes,
/// emitting the identical well-known ids -729/-490/-451/-409/-349/-140,
/// the identical converter ids -463/-715, the identical 0B 78 FF list and
/// 19 .. FFFF names modulo per-file table numbering plus line markers and
/// ConnectionIds the staged file omits; fixture retained at
/// E:/tmp/bamlfix141 outside the tree) plus the ILSpy BamlDecompiler
/// record layouts (DefAttributeRecord Value string plus NameId U16,
/// PropertyListStartRecord carrying the U16 id).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice141BamlCoverFlowTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice141BamlCoverFlowTests()
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
    public void Theme_baml_lands_coverflow_and_coverviewitem_then_stops_at_gripper()
    {
        // Staged framing pins for the keyed 'CoverFlowBaseStyle' Style
        // entry keyidx 155 spanning [0x1943B, 0x1956B): the Style head over
        // the CoverFlow TargetType with its Setters content, the
        // Focusable/Template setters, the 03 27 FD Viewport3D child head
        // with its PART_Viewport3D Name PWC, the Camera complex with the
        // PART_Camera DefAttribute plus the four camera PWCs, the Children
        // list with the two DirectionalLight Content values plus
        // PART_VisualParent, and the close run; then the keyed
        // 'CoverViewItemBaseStyle' Style entry keyidx 156 spanning
        // [0x1956C, 0x195EC) with the Header ContentPresenter; then the
        // keyed 'GridViewColumnHeaderGripper' Style entry keyidx 157 whose
        // Canvas.Right/Width/Height/Padding/Background setters land from
        // slice-142 on; the Template setter at 0x196A0 is the exact next
        // frontier.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");

        var coverFlowHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x34, 0x00, 0x2E, 0x12, 0xFF, 0x03, 0xD4, 0xFD };
        Assert.Equal(coverFlowHead, theme[0x1943B..0x1944A]);
        var focusableSetter = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0x7C, 0xFF, 0x24, 0x0B, 0x02, 0x00, 0x05, 0x46, 0x61, 0x6C, 0x73, 0x65, 0xD2, 0xFF, 0x04 };
        Assert.Equal(focusableSetter, theme[0x1944B..0x19460]);
        var templateHead = new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDB, 0xFF, 0x07, 0x02, 0x00, 0x03, 0x94, 0xFF, 0x00 };
        Assert.Equal(templateHead, theme[0x19460..0x19473]);
        var templateTarget = new byte[] { 0x22, 0x07, 0x00, 0x34, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0xCE, 0xFF, 0x00 };
        Assert.Equal(templateTarget, theme[0x19473..0x1947F]);
        var borderBindings = new byte[] { 0x23, 0xFB, 0xFF, 0x7A, 0x02, 0xE7, 0xFF, 0x23, 0xFA, 0xFF, 0x7A, 0x02, 0xE6, 0xFF, 0x23, 0xFC, 0xFF, 0x7A, 0x02, 0xE8, 0xFF, 0x2E, 0x5F, 0xFF };
        Assert.Equal(borderBindings, theme[0x1947F..0x19497]);
        var viewport3dHead = new byte[] { 0x03, 0x27, 0xFD, 0x00 };
        Assert.Equal(viewport3dHead, theme[0x19497..0x1949B]);
        var viewport3dName = new byte[] { 0x24, 0x15, 0x41, 0x00, 0x0F, 0x50, 0x41, 0x52, 0x54, 0x5F, 0x56, 0x69, 0x65, 0x77, 0x70, 0x6F, 0x72, 0x74, 0x33, 0x44, 0x99, 0xFD };
        Assert.Equal(viewport3dName, theme[0x1949B..0x194B1]);
        var cameraHead = new byte[] { 0x07, 0x42, 0x00, 0x03, 0x3D, 0xFE, 0x00, 0x19, 0x0F, 0x0B, 0x50, 0x41, 0x52, 0x54, 0x5F, 0x43, 0x61, 0x6D, 0x65, 0x72, 0x61, 0xFF, 0xFF };
        Assert.Equal(cameraHead, theme[0x194B1..0x194C8]);
        var position = new byte[] { 0x24, 0x0B, 0x43, 0x00, 0x05, 0x30, 0x2C, 0x30, 0x2C, 0x33, 0x31, 0xFE };
        Assert.Equal(position, theme[0x194C8..0x194D4]);
        var look = new byte[] { 0x24, 0x0C, 0x45, 0x00, 0x06, 0x30, 0x2C, 0x30, 0x2C, 0x2D, 0x31, 0x35, 0xFD };
        Assert.Equal(look, theme[0x194E0..0x194ED]);
        var childrenList = new byte[] { 0x04, 0x08, 0x0B, 0x78, 0xFF, 0x03, 0x67, 0xFE, 0x00 };
        Assert.Equal(childrenList, theme[0x194F6..0x194FF]);
        var visualParent = new byte[] { 0x19, 0x15, 0x11, 0x50, 0x41, 0x52, 0x54, 0x5F, 0x56, 0x69, 0x73, 0x75, 0x61, 0x6C, 0x50, 0x61, 0x72, 0x65, 0x6E, 0x74, 0xFF, 0xFF };
        Assert.Equal(visualParent, theme[0x1954E..0x19564]);
        var coverFlowClose = new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x08, 0x04, 0x04 };
        Assert.Equal(coverFlowClose, theme[0x19564..0x1956C]);
        var coverViewItemHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x35, 0x00, 0x2E, 0x12, 0xFF, 0x03, 0xD4, 0xFD };
        Assert.Equal(coverViewItemHead, theme[0x1956C..0x1957B]);
        var headerSource = new byte[] { 0x24, 0x0C, 0xEC, 0xFF, 0x06, 0x48, 0x65, 0x61, 0x64, 0x65, 0x72, 0x99, 0xFD };
        Assert.Equal(headerSource, theme[0x195CC..0x195D9]);
        var gripperHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x6E, 0xFD };
        Assert.Equal(gripperHead, theme[0x195ED..0x195F6]);
        var canvasRight = new byte[] { 0x06, 0x0D, 0x01, 0x00, 0x89, 0x40, 0xBE, 0xFF, 0x05, 0x52, 0x69, 0x67, 0x68, 0x74 };
        Assert.Equal(canvasRight, theme[0x195FD..0x1960B]);

        var key155 = new byte[] { 0x26, 0x09, 0x91, 0x00, 0xA4, 0xA2, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key155, theme[0xB537..0xB541]);
        var key156 = new byte[] { 0x26, 0x09, 0x92, 0x00, 0xD5, 0xA3, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key156, theme[0xB541..0xB54B]);
        var key157 = new byte[] { 0x26, 0x09, 0x93, 0x00, 0x56, 0xA4, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key157, theme[0xB54B..0xB555]);
        var key158 = new byte[] { 0x26, 0x09, 0x94, 0x00, 0x5F, 0xA5, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key158, theme[0xB555..0xB55F]);
    }

    [Fact]
    public void Staged_boot_advances_past_coverflow_to_gripper_style()
    {
        // The staged boot now materializes entries #155 (the
        // 'CoverFlowBaseStyle' Style with its staged template Border plus
        // the PART_Viewport3D scene) and #156 (the 'CoverViewItemBaseStyle'
        // Style with its Header ContentPresenter) and stops at the keyed
        // 'GridViewColumnHeaderGripper' Style entry keyidx 157 (slice-142
        // routes its Canvas.Right/Width/Height/Padding/Background setters;
        // the Template setter has no Border Padding routing: the precise
        // next blocker). Every earlier stop stays absent, and the new head
        // asserts the exact frontier.
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
    public void Staged_shape_coverflow_lands_live()
    {
        // The staged 'CoverFlowBaseStyle' entry shape (converted CoverFlow
        // target, Focusable False, the Template setter holding the
        // ControlTemplate whose Border visual carries the three
        // TemplateBindings with the named Viewport3D scene) lands as a live
        // engine Style with the camera, lights, directions and namescope
        // intact. Per-file ids mirror the staged rows 0/1/2/65-73.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var dictionary = BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", StagedCoverFlowEntry(n, null), n));
        var style = Assert.IsType<Style>(dictionary["CoverFlowBaseStyle"]);
        var coverFlowType = ResolveConvertedType("HandyControl.Controls.CoverFlow");
        Assert.Same(coverFlowType, style.TargetType);
        Assert.False((bool)style.Setters.OfType<Setter>().First(s => s.Property.Name == "Focusable").Value!);
        var template = Assert.IsType<ControlTemplate>(style.Setters.OfType<Setter>().First(s => s.Property.Name == "Template").Value);
        Assert.Same(coverFlowType, template.TargetType);
        var border = Assert.IsType<Border>(template.VisualTree);
        var viewport = Assert.IsType<Viewport3D>(border.Child);
        Assert.Equal("PART_Viewport3D", viewport.GetValue(FrameworkElement.NameProperty));
        var camera = Assert.IsType<PerspectiveCamera>(viewport.Camera);
        Assert.Equal(new Point3D(0, 0, 3), camera.Position);
        Assert.Equal(new Vector3D(0, 1, 0), camera.UpDirection);
        Assert.Equal(new Vector3D(0, 0, -1), camera.LookDirection);
        Assert.Equal(90.0, camera.FieldOfView);
        Assert.Equal(3, viewport.Children.Count);
        var first = Assert.IsType<ModelVisual3D>(viewport.Children[0]);
        var firstLight = Assert.IsType<DirectionalLight>(first.Content);
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), firstLight.Color);
        Assert.Equal(new Vector3D(1, 0, -4), firstLight.Direction);
        var second = Assert.IsType<ModelVisual3D>(viewport.Children[1]);
        Assert.Equal(new Vector3D(-1, 0, -4), Assert.IsType<DirectionalLight>(second.Content).Direction);
        Assert.IsType<ModelVisual3D>(viewport.Children[2]);
        Assert.Same(viewport, template.TemplateNames["PART_Viewport3D"]);
        Assert.Same(camera, template.TemplateNames["PART_Camera"]);
        Assert.Same(viewport.Children[2], template.TemplateNames["PART_VisualParent"]);
        Assert.Equal(3, template.TemplateBindings.Count);
    }

    [Fact]
    public void Staged_shape_coverviewitem_lands_live()
    {
        // The staged 'CoverViewItemBaseStyle' entry shape (converted
        // CoverViewItem target, Focusable False, the Template setter
        // holding the ControlTemplate whose Border visual carries the
        // three TemplateBindings with the Header ContentPresenter child)
        // lands as a live engine Style with the existing machinery.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var dictionary = BamlReader.LoadDictionary(CoverFlowDictionary("CoverViewItemBaseStyle", CoverViewItemEntry(n), n));
        var style = Assert.IsType<Style>(dictionary["CoverViewItemBaseStyle"]);
        var itemType = ResolveConvertedType("HandyControl.Controls.CoverViewItem");
        Assert.Same(itemType, style.TargetType);
        var template = Assert.IsType<ControlTemplate>(style.Setters.OfType<Setter>().First(s => s.Property.Name == "Template").Value);
        var border = Assert.IsType<Border>(template.VisualTree);
        var presenter = Assert.IsType<ContentPresenter>(border.Child);
        Assert.Equal("Header", presenter.ContentSource);
        Assert.Equal(5, template.TemplateBindings.Count);
    }

    [Fact]
    public void Fixture_numbered_coverflow_lands_live()
    {
        // The genuine net10.0-windows fixture numbers its tables from zero
        // (attrs 0-13 over the same owners); the same staged entry shape
        // lands identically, proving the routing never hardcodes an index.
        LoadStagedHandyControlEntry();
        var n = FixtureCoverFlowTables();
        var dictionary = BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", StagedCoverFlowEntry(n, null), n));
        var style = Assert.IsType<Style>(dictionary["CoverFlowBaseStyle"]);
        Assert.Same(ResolveConvertedType("HandyControl.Controls.CoverFlow"), style.TargetType);
        var template = Assert.IsType<ControlTemplate>(style.Setters.OfType<Setter>().First(s => s.Property.Name == "Template").Value);
        var viewport = Assert.IsType<Viewport3D>(Assert.IsType<Border>(template.VisualTree).Child);
        Assert.Equal(new Point3D(0, 0, 3), Assert.IsType<PerspectiveCamera>(viewport.Camera).Position);
        Assert.Equal(3, viewport.Children.Count);
    }

    [Fact]
    public void Viewport_without_camera_lands_bare()
    {
        // A Viewport3D child without the Camera complex or the Children
        // list still lands (both are optional like the RotateTransform
        // Angle): optionality is per-member, never a half-built scene.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), []))))).ToArray();
        var dictionary = BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n));
        var style = Assert.IsType<Style>(dictionary["CoverFlowBaseStyle"]);
        var template = Assert.IsType<ControlTemplate>(style.Setters.OfType<Setter>().First(s => s.Property.Name == "Template").Value);
        var viewport = Assert.IsType<Viewport3D>(Assert.IsType<Border>(template.VisualTree).Child);
        Assert.Null(viewport.Camera);
        Assert.Empty(viewport.Children);
    }

    private sealed record CoverFlowNumbering(
        (int Id, string Name, short Owner)[] Attrs,
        int TargetType, int Prop, int Value, int TemplateTarget,
        int Name, int Camera, int Position, int Up, int Look, int Fov,
        int Content, int Color, int Direction,
        int HeaderTemplate, int Padding);

    private static CoverFlowNumbering StagedCoverFlowTables() => new(
        [(0, "TargetType", (short)-620), (1, "Property", (short)-556), (2, "Value", (short)-556),
         (7, "TargetType", (short)-108),
         (65, "Name", (short)-729), (66, "Camera", (short)-729),
         (67, "Position", (short)-490), (68, "UpDirection", (short)-490), (69, "LookDirection", (short)-490),
         (70, "FieldOfView", (short)-451), (71, "Content", (short)-409),
         (72, "Color", (short)-349), (73, "Direction", (short)-140),
         (74, "HeaderTemplate", (short)1), (75, "Padding", (short)-50)],
        0, 1, 2, 7, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75);

    /// <summary>Fixture numbering standing in for the genuine net10
    /// fixture tables (attrs 0-12 plus the CoverViewItem rows over the
    /// same owners).</summary>
    private static CoverFlowNumbering FixtureCoverFlowTables() => new(
        [(0, "TargetType", (short)-620), (1, "Property", (short)-556), (2, "Value", (short)-556),
         (3, "TargetType", (short)-108),
         (4, "Name", (short)-729), (5, "Camera", (short)-729),
         (6, "Position", (short)-490), (7, "UpDirection", (short)-490), (8, "LookDirection", (short)-490),
         (9, "FieldOfView", (short)-451), (10, "Content", (short)-409),
         (11, "Color", (short)-349), (12, "Direction", (short)-140),
         (13, "HeaderTemplate", (short)1), (14, "Padding", (short)-50)],
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14);

    private static byte[] StagedCoverFlowEntry(CoverFlowNumbering n, string? positionOverride) => StyleShell(n, 0,
        FocusableSetter(n),
        TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
            ViewportShell(n, NamePwc(n, "PART_Viewport3D"), ViewportScene(n, positionOverride)))))).ToArray();

    private static B StyleShell(CoverFlowNumbering n, int typeId, params B[] setters)
    {
        var b = new B().Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16((short)n.TargetType).I16((short)typeId)
            .Raw(0x2E).I16(-238);
        foreach (var setter in setters) b.Raw(setter.ToArray());
        return b.Raw(0x04);
    }

    private static B FocusableSetter(CoverFlowNumbering n, string text = "False", short conv = -46) => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-132))
        .Raw(Pwc(n, n.Value, text, conv).ToArray())
        .Raw(0x04);

    private static B TemplateSetter(CoverFlowNumbering n, B template) => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-37))
        .Raw(0x07).I16((short)n.Value)
        .Raw(template.ToArray())
        .Raw(0x08)
        .Raw(0x04);

    private static B TemplateShell(CoverFlowNumbering n, int typeId, B visual) => new B()
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16((short)n.TemplateTarget).I16((short)typeId)
        .Raw(0x2E).I16(-174)
        .Raw(visual.ToArray())
        .Raw(0x04);

    private static B Extension(short target, short source) => new B()
        .Raw(0x23).I16(target).U16(634).I16(source);

    private static B CoverFlowBindings() => new B()
        .Raw(Extension(-5, -25).ToArray())
        .Raw(Extension(-6, -26).ToArray())
        .Raw(Extension(-4, -24).ToArray());

    private static B BorderShell(CoverFlowNumbering n, B bindings, B child) => new B()
        .Raw(0x03).I16(-50).Raw(0x00)
        .Raw(bindings.ToArray())
        .Raw(0x2E).I16(-161)
        .Raw(child.ToArray())
        .Raw(0x04);

    private static B Pwc(CoverFlowNumbering n, int attr, string text, short conv) => new B()
        .Sized(0x24, q => q.I16((short)attr).Str(text).I16(conv));

    private static B NamePwc(CoverFlowNumbering n, string name) => Pwc(n, n.Name, name, -615);

    private static B DefName(string name) => new B()
        .Sized(0x19, q => q.Str(name).U16(0xFFFF));

    private static B ViewportShell(CoverFlowNumbering n, B name, B[] inners)
    {
        var b = new B().Raw(0x03).I16(-729).Raw(0x00).Raw(name.ToArray());
        foreach (var inner in inners) b.Raw(inner.ToArray());
        return b.Raw(0x04);
    }

    private static B[] ViewportScene(CoverFlowNumbering n, string? positionOverride) =>
    [
        CameraComplex(n, positionOverride),
        ChildrenList(n, [LightNode(n, null, "1,0,-4"), LightNode(n, null, "-1,0,-4"), NamedNode("PART_VisualParent")]),
    ];

    private static B CameraComplex(CoverFlowNumbering n, string? positionOverride, string cameraName = "PART_Camera") => new B()
        .Raw(0x07).I16((short)n.Camera)
        .Raw(0x03).I16(-451).Raw(0x00)
        .Raw(DefName(cameraName).ToArray())
        .Raw(Pwc(n, n.Position, positionOverride ?? "0,0,3", -463).ToArray())
        .Raw(Pwc(n, n.Up, "0,1,0", -715).ToArray())
        .Raw(Pwc(n, n.Look, "0,0,-1", -715).ToArray())
        .Raw(Pwc(n, n.Fov, "90", -175).ToArray())
        .Raw(0x04)
        .Raw(0x08);

    private static B ChildrenList(CoverFlowNumbering n, B[] nodes)
    {
        _ = n;
        var b = new B().Raw(0x0B).I16(-136);
        foreach (var node in nodes) b.Raw(node.ToArray());
        return b.Raw(0x0C);
    }

    private static B LightNode(CoverFlowNumbering n, string? name, string direction) => new B()
        .Raw(0x03).I16(-409).Raw(0x00)
        .Raw(name is null ? [] : DefName(name).ToArray())
        .Raw(ContentComplex(n, Light(n, "White", direction)).ToArray())
        .Raw(0x04);

    private static B NamedNode(string name) => new B()
        .Raw(0x03).I16(-409).Raw(0x00)
        .Raw(DefName(name).ToArray())
        .Raw(0x04);

    private static B ContentComplex(CoverFlowNumbering n, B light) => new B()
        .Raw(0x07).I16((short)n.Content)
        .Raw(light.ToArray())
        .Raw(0x08);

    private static B Light(CoverFlowNumbering n, string color, string direction, short colorConv = -87, short dirConv = -715) => new B()
        .Raw(0x03).I16(-140).Raw(0x00)
        .Raw(Pwc(n, n.Color, color, colorConv).ToArray())
        .Raw(Pwc(n, n.Direction, direction, dirConv).ToArray())
        .Raw(0x04);

    private static byte[] CoverViewItemEntry(CoverFlowNumbering n) => StyleShell(n, 1,
        FocusableSetter(n),
        TemplateSetter(n, TemplateShell(n, 1, BorderShell(n, new B()
            .Raw(Extension(-6, -26).ToArray())
            .Raw(Extension(-4, -24).ToArray())
            .Raw(Extension(-5, -25).ToArray()),
            new B().Raw(0x03).I16(-102).Raw(0x00)
                .Raw(Pwc(n, -20, "Header", -615).ToArray())
                .Raw(Extension(-21, (short)n.HeaderTemplate).ToArray())
                .Raw(Extension(-49, (short)n.Padding).ToArray())
                .Raw(0x04))))).ToArray();

    [Fact]
    public void Camera_wrong_position_converter_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var camera = CameraComplexAltered(n, Pwc(n, n.Position, "0,0,3", -175));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            CoverFlowDictionary("CoverFlowBaseStyle", StyleShell(n, 0, FocusableSetter(n),
                TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                    ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [camera]))))).ToArray(), n)));
        Assert.Contains("positions take the Point3DConverter", error.Message);
    }

    private static B CameraComplexAltered(CoverFlowNumbering n, B firstPwc) => new B()
        .Raw(0x07).I16((short)n.Camera)
        .Raw(0x03).I16(-451).Raw(0x00)
        .Raw(DefName("PART_Camera").ToArray())
        .Raw(firstPwc.ToArray())
        .Raw(Pwc(n, n.Up, "0,1,0", -715).ToArray())
        .Raw(Pwc(n, n.Look, "0,0,-1", -715).ToArray())
        .Raw(Pwc(n, n.Fov, "90", -175).ToArray())
        .Raw(0x04)
        .Raw(0x08);

    [Fact]
    public void Camera_bad_position_text_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"),
                    [CameraComplex(n, "0,0"), ChildrenList(n, [NamedNode("PART_VisualParent")])]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("is not a Point3D for Position", error.Message);
    }

    [Fact]
    public void Camera_wrong_direction_converter_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var camera = new B()
            .Raw(0x07).I16((short)n.Camera)
            .Raw(0x03).I16(-451).Raw(0x00)
            .Raw(DefName("PART_Camera").ToArray())
            .Raw(Pwc(n, n.Position, "0,0,3", -463).ToArray())
            .Raw(Pwc(n, n.Up, "0,1,0", -175).ToArray())
            .Raw(Pwc(n, n.Look, "0,0,-1", -715).ToArray())
            .Raw(Pwc(n, n.Fov, "90", -175).ToArray())
            .Raw(0x04)
            .Raw(0x08);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [camera]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("directions take the Vector3DConverter", error.Message);
    }

    [Fact]
    public void Camera_bad_lookdirection_text_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var camera = new B()
            .Raw(0x07).I16((short)n.Camera)
            .Raw(0x03).I16(-451).Raw(0x00)
            .Raw(DefName("PART_Camera").ToArray())
            .Raw(Pwc(n, n.Position, "0,0,3", -463).ToArray())
            .Raw(Pwc(n, n.Up, "0,1,0", -715).ToArray())
            .Raw(Pwc(n, n.Look, "nope", -715).ToArray())
            .Raw(Pwc(n, n.Fov, "90", -175).ToArray())
            .Raw(0x04)
            .Raw(0x08);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [camera]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("is not a Vector3D for LookDirection", error.Message);
    }

    [Fact]
    public void Camera_wrong_fov_converter_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var camera = new B()
            .Raw(0x07).I16((short)n.Camera)
            .Raw(0x03).I16(-451).Raw(0x00)
            .Raw(DefName("PART_Camera").ToArray())
            .Raw(Pwc(n, n.Position, "0,0,3", -463).ToArray())
            .Raw(Pwc(n, n.Up, "0,1,0", -715).ToArray())
            .Raw(Pwc(n, n.Look, "0,0,-1", -715).ToArray())
            .Raw(Pwc(n, n.Fov, "90", -46).ToArray())
            .Raw(0x04)
            .Raw(0x08);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [camera]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("fields of view take the DoubleConverter", error.Message);
    }

    [Fact]
    public void Camera_bad_fov_text_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var camera = new B()
            .Raw(0x07).I16((short)n.Camera)
            .Raw(0x03).I16(-451).Raw(0x00)
            .Raw(DefName("PART_Camera").ToArray())
            .Raw(Pwc(n, n.Position, "0,0,3", -463).ToArray())
            .Raw(Pwc(n, n.Up, "0,1,0", -715).ToArray())
            .Raw(Pwc(n, n.Look, "0,0,-1", -715).ToArray())
            .Raw(Pwc(n, n.Fov, "wide", -175).ToArray())
            .Raw(0x04)
            .Raw(0x08);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [camera]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("is not a Double for FieldOfView", error.Message);
    }

    [Fact]
    public void Light_wrong_color_converter_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [CameraComplex(n, null),
                    ChildrenList(n, [ContentNode(n, Light(n, "White", "1,0,-4", -175)),
                        NamedNode("PART_VisualParent")])]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("colors take the ColorConverter", error.Message);
    }

    private static B ContentNode(CoverFlowNumbering n, B light) => new B()
        .Raw(0x03).I16(-409).Raw(0x00)
        .Raw(ContentComplex(n, light).ToArray())
        .Raw(0x04);

    [Fact]
    public void Light_bad_color_text_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [CameraComplex(n, null),
                    ChildrenList(n, [ContentNode(n, Light(n, "notacolor", "1,0,-4")),
                        NamedNode("PART_VisualParent")])]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("is not a color", error.Message);
    }

    [Fact]
    public void Light_wrong_direction_converter_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [CameraComplex(n, null),
                    ChildrenList(n, [ContentNode(n, Light(n, "White", "1,0,-4", -87, -175)),
                        NamedNode("PART_VisualParent")])]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("directions take the Vector3DConverter", error.Message);
    }

    [Fact]
    public void Light_bad_direction_text_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [CameraComplex(n, null),
                    ChildrenList(n, [ContentNode(n, Light(n, "White", "down")),
                        NamedNode("PART_VisualParent")])]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("is not a Vector3D for Direction", error.Message);
    }

    [Fact]
    public void Viewport_unknown_camera_element_fails_precise()
    {
        // A non-PerspectiveCamera element in the Camera complex fails
        // naming it instead of landing a half-built rig.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var camera = new B()
            .Raw(0x07).I16((short)n.Camera)
            .Raw(0x03).I16(-729).Raw(0x00)
            .Raw(0x04)
            .Raw(0x08);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [camera]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("cameras take a PerspectiveCamera", error.Message);
    }

    [Fact]
    public void Viewport_unknown_list_child_fails_precise()
    {
        // A non-ModelVisual3D element in the Children list fails naming it.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [CameraComplex(n, null),
                    ChildrenList(n, [new B().Raw(0x03).I16(-50).Raw(0x00).Raw(0x04)])]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("viewport children take ModelVisual3D nodes", error.Message);
    }

    [Fact]
    public void Viewport_element_outside_list_fails_precise()
    {
        // A ModelVisual3D element before the Children list opens fails: the
        // list framing carries the Children meaning, never the position.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [CameraComplex(n, null), NamedNode("PART_VisualParent")]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("outside the Children list", error.Message);
    }

    [Fact]
    public void Viewport_unclosed_list_fails_precise()
    {
        // A Children list without its PropertyIListEnd fails instead of
        // leaking an open list into the enclosing visual.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var viewport = new B()
            .Raw(0x03).I16(-729).Raw(0x00)
            .Raw(NamePwc(n, "PART_Viewport3D").ToArray())
            .Raw(0x0B).I16(-136)
            .Raw(NamedNode("PART_VisualParent").ToArray())
            .Raw(0x04);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(), viewport)))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("unclosed Children list", error.Message);
    }

    [Fact]
    public void Viewport_stray_list_end_fails_precise()
    {
        // A PropertyIListEnd without an open Children list fails.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var viewport = new B()
            .Raw(0x03).I16(-729).Raw(0x00)
            .Raw(NamePwc(n, "PART_Viewport3D").ToArray())
            .Raw(0x0C)
            .Raw(0x04);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(), viewport)))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("list end without a list", error.Message);
    }

    [Fact]
    public void Viewport_duplicate_camera_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"),
                    [CameraComplex(n, null), CameraComplex(n, null)]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("duplicate Camera on the template Viewport3D", error.Message);
    }

    [Fact]
    public void Viewport_duplicate_name_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"),
                    [CameraComplex(n, null, "PART_Viewport3D"),
                        ChildrenList(n, [NamedNode("PART_VisualParent")])]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("duplicate template part name 'PART_Viewport3D'", error.Message);
    }

    [Fact]
    public void Viewport_defname_with_name_id_fails_precise()
    {
        // A DefAttribute carrying an assigned name id fails: every staged
        // DefAttribute carries the unassigned 0xFFFF id.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var camera = new B()
            .Raw(0x07).I16((short)n.Camera)
            .Raw(0x03).I16(-451).Raw(0x00)
            .Sized(0x19, q => q.Str("PART_Camera").U16(0x0007))
            .Raw(Pwc(n, n.Position, "0,0,3", -463).ToArray())
            .Raw(Pwc(n, n.Up, "0,1,0", -715).ToArray())
            .Raw(Pwc(n, n.Look, "0,0,-1", -715).ToArray())
            .Raw(Pwc(n, n.Fov, "90", -175).ToArray())
            .Raw(0x04)
            .Raw(0x08);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [camera]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("carries name id 7", error.Message);
    }

    [Fact]
    public void Viewport_garbage_record_fails_precise()
    {
        // A markup-extension record directly on the template Viewport3D
        // fails naming the record with the viewport contract.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var viewport = new B()
            .Raw(0x03).I16(-729).Raw(0x00)
            .Raw(NamePwc(n, "PART_Viewport3D").ToArray())
            .Raw(0x23).I16(-4).U16(634).I16(-24)
            .Raw(0x04);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(), viewport)))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("template Viewport3D holds record 0x23", error.Message);
    }

    [Fact]
    public void Camera_garbage_record_fails_precise()
    {
        // A markup-extension record on the template camera fails naming the
        // record with the camera contract.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var camera = new B()
            .Raw(0x07).I16((short)n.Camera)
            .Raw(0x03).I16(-451).Raw(0x00)
            .Raw(DefName("PART_Camera").ToArray())
            .Raw(Pwc(n, n.Position, "0,0,3", -463).ToArray())
            .Raw(0x23).I16(-4).U16(634).I16(-24)
            .Raw(0x04)
            .Raw(0x08);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [camera]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("template camera holds record 0x23", error.Message);
    }

    [Fact]
    public void Light_garbage_record_fails_precise()
    {
        // A StaticResource value on the template light fails naming the
        // record with the light contract.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var light = new B()
            .Raw(0x03).I16(-140).Raw(0x00)
            .Raw(Pwc(n, n.Color, "White", -87).ToArray())
            .Raw(0x38).I16(0).U16(0)
            .Raw(0x04);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [CameraComplex(n, null),
                    ChildrenList(n, [ContentNode(n, light), NamedNode("PART_VisualParent")])]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("template light holds record 0x38", error.Message);
    }

    [Fact]
    public void Border_second_viewport_child_fails_precise()
    {
        // A template Border keeps its one-child contract: a second visual
        // (here a second Viewport3D) fails instead of replacing the first.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var border = new B()
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(CoverFlowBindings().ToArray())
            .Raw(0x2E).I16(-161)
            .Raw(ViewportShell(n, NamePwc(n, "PART_Viewport3D"), []).ToArray())
            .Raw(ViewportShell(n, NamePwc(n, "PART_Second"), []).ToArray())
            .Raw(0x04);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, border))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("second visual on the template Border", error.Message);
    }

    [Fact]
    public void Viewport_unknown_complex_fails_precise()
    {
        // A Content complex on the Viewport3D fails: viewports take the
        // Camera complex, never a Content one.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var viewport = new B()
            .Raw(0x03).I16(-729).Raw(0x00)
            .Raw(NamePwc(n, "PART_Viewport3D").ToArray())
            .Raw(0x07).I16((short)n.Content)
            .Raw(0x08)
            .Raw(0x04);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(), viewport)))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("viewports take the Camera complex", error.Message);
    }

    [Fact]
    public void Viewport_wrong_name_converter_fails_precise()
    {
        // The Viewport3D Name value must travel with the StringConverter.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, Pwc(n, n.Name, "PART_Viewport3D", -46), []))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("viewports take the Name string", error.Message);
    }

    [Fact]
    public void ModelVisual3D_duplicate_content_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var node = new B()
            .Raw(0x03).I16(-409).Raw(0x00)
            .Raw(ContentComplex(n, Light(n, "White", "1,0,-4")).ToArray())
            .Raw(ContentComplex(n, Light(n, "White", "1,0,-4")).ToArray())
            .Raw(0x04);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [CameraComplex(n, null),
                    ChildrenList(n, [node])]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("duplicate Content on the template ModelVisual3D", error.Message);
    }

    [Fact]
    public void Light_duplicate_color_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var light = new B()
            .Raw(0x03).I16(-140).Raw(0x00)
            .Raw(Pwc(n, n.Color, "White", -87).ToArray())
            .Raw(Pwc(n, n.Color, "Black", -87).ToArray())
            .Raw(Pwc(n, n.Direction, "1,0,-4", -715).ToArray())
            .Raw(0x04);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [CameraComplex(n, null),
                    ChildrenList(n, [ContentNode(n, light), NamedNode("PART_VisualParent")])]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("duplicate Color on the template light", error.Message);
    }

    [Fact]
    public void Camera_duplicate_position_fails_precise()
    {
        // The camera carries Position twice: the duplicate gate fires on
        // the second one instead of silently keeping the first.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var camera = new B()
            .Raw(0x07).I16((short)n.Camera)
            .Raw(0x03).I16(-451).Raw(0x00)
            .Raw(DefName("PART_Camera").ToArray())
            .Raw(Pwc(n, n.Position, "0,0,3", -463).ToArray())
            .Raw(Pwc(n, n.Position, "0,0,4", -463).ToArray())
            .Raw(Pwc(n, n.Up, "0,1,0", -715).ToArray())
            .Raw(Pwc(n, n.Look, "0,0,-1", -715).ToArray())
            .Raw(Pwc(n, n.Fov, "90", -175).ToArray())
            .Raw(0x04)
            .Raw(0x08);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [camera,
                    ChildrenList(n, [NamedNode("PART_VisualParent")])]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("duplicate Position on the template camera", error.Message);
    }

    [Fact]
    public void Viewport_unknown_list_fails_precise()
    {
        // A list that is not the Children list fails naming the attribute:
        // viewports take exactly the Children list.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var viewport = new B()
            .Raw(0x03).I16(-729).Raw(0x00)
            .Raw(NamePwc(n, "PART_Viewport3D").ToArray())
            .Raw(0x0B).I16((short)n.Camera)
            .Raw(0x04);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(), viewport)))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("viewports take the Children list", error.Message);
    }

    [Fact]
    public void Viewport_duplicate_list_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var viewport = new B()
            .Raw(0x03).I16(-729).Raw(0x00)
            .Raw(NamePwc(n, "PART_Viewport3D").ToArray())
            .Raw(0x0B).I16(-136)
            .Raw(0x0C)
            .Raw(0x0B).I16(-136)
            .Raw(0x0C)
            .Raw(0x04);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(), viewport)))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("duplicate Children list on the template Viewport3D", error.Message);
    }

    [Fact]
    public void Camera_attr_without_table_fails_precise()
    {
        // A camera simple over an AttributeInfo id with no record fails
        // instead of resolving a phantom attribute.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var camera = new B()
            .Raw(0x07).I16((short)n.Camera)
            .Raw(0x03).I16(-451).Raw(0x00)
            .Raw(DefName("PART_Camera").ToArray())
            .Raw(Pwc(n, 500, "0,0,3", -463).ToArray())
            .Raw(0x04)
            .Raw(0x08);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [camera]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("has no AttributeInfo record", error.Message);
    }

    [Fact]
    public void Viewport_direct_defname_fails_precise()
    {
        // A DefAttribute directly on the Viewport3D fails with the viewport
        // contract: viewport names arrive as the Name simple, DefAttributes
        // belong to the camera and the visual nodes.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var viewport = new B()
            .Raw(0x03).I16(-729).Raw(0x00)
            .Raw(DefName("PART_Viewport3D").ToArray())
            .Raw(0x04);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(), viewport)))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("template Viewport3D holds record 0x19", error.Message);
    }

    [Fact]
    public void ModelVisual3D_stray_simple_fails_precise()
    {
        // A PropertyWithConverter simple on a ModelVisual3D fails with the
        // node contract: nodes take a name plus the Content complex.
        LoadStagedHandyControlEntry();
        var n = StagedCoverFlowTables();
        var node = new B()
            .Raw(0x03).I16(-409).Raw(0x00)
            .Raw(Pwc(n, n.Content, "x", -615).ToArray())
            .Raw(0x04);
        var entry = StyleShell(n, 0, FocusableSetter(n),
            TemplateSetter(n, TemplateShell(n, 0, BorderShell(n, CoverFlowBindings(),
                ViewportShell(n, NamePwc(n, "PART_Viewport3D"), [CameraComplex(n, null),
                    ChildrenList(n, [node])]))))).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(CoverFlowDictionary("CoverFlowBaseStyle", entry, n)));
        Assert.Contains("template ModelVisual3D holds record 0x24", error.Message);
    }

    private static byte[] CoverFlowDictionary(string key, byte[] entry, CoverFlowNumbering n, bool shared = true)
    {
        var content = new B().Raw(entry);
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1D, p => p.U16(0).U16(0).Str("HandyControl.Controls.CoverFlow"))
            .Sized(0x1D, p => p.U16(1).U16(0).Str("HandyControl.Controls.CoverViewItem"));
        foreach (var (id, name, owner) in n.Attrs)
            head.Sized(0x1F, p => p.I16((short)id).I16(owner).Raw(0x00).Str(name));
        head.Sized(0x20, p => p.U16(0).Str(key));
        head.Deferable();
        if (shared)
            head.Raw(0x26).Seven(9).U16(0).U32(0).U16(0);
        else
            head.Raw(0x26).Seven(9).U16(0).U32(0).Raw(0x00, 0x01);
        return head.Raw(content.ToArray()).FinishDictionary().ToArray();
    }

    private static Type ResolveConvertedType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ReferenceEquals(assembly, typeof(UIElement).Assembly)) continue;
            var candidate = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
            if (candidate is not null) return candidate;
        }
        throw new InvalidOperationException("converted type not loaded: " + fullName);
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
