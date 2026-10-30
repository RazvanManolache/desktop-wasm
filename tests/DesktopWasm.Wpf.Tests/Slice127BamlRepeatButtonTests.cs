// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-127 HandyControl theme.baml RepeatButton run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the four keyed RepeatButton style entries keyidx 123-126 at
/// [BAML+0x17C02, BAML+0x17F3C) land as live engine styles (qualified
/// FrameworkElement OverridesDefaultStyle plus short-form Background/
/// Focusable/IsTabStop setters plus the Template setter holding the
/// ControlTemplate whose Border visual carries a packed-ARGB Background
/// plus Height/Width TemplateBindings with a Rectangle child, or a bare
/// ContentPresenter/Rectangle visual with Content/Fill/Height/Width
/// TemplateBindings); the run stopped precise at the keyed
/// 'ToggleBlockBaseStyle' style entry at BAML+0x17F3C (slice-128 lands it
/// as a live style - converted ToggleBlock target with short-form
/// VerticalContentAlignment/HorizontalContentAlignment setters plus the
/// ToggleBlock template with its trigger list - slice-129 lands the keyed
/// 'GroupBoxBaseTopTemplate' template entry at BAML+0x1835D as a live
/// engine ControlTemplate, slice-130 lands the keyed
/// 'GroupBoxBaseLeftTemplate' template entry at BAML+0x1858F as a live
/// engine ControlTemplate and slice-131 lands the keyed
/// 'GroupBoxTabTopTemplate' template entry at BAML+0x187F5 plus the keyed
/// 'GroupBoxTabLeftTemplate' entry as live engine ControlTemplates,
/// stopping at the 'ButtonGroupHorizontalUniformItemsPanelTemplate'
/// entry at BAML+0x18D3F).
///
/// Parser-traced entry map (key consumption order, offsets validated):
/// 'SliderRepeatButtonHorizontalStyle' at BAML+0x17C02 (Rectangle child
/// with Height "2"), 'SliderRepeatButtonVerticalStyle' at BAML+0x17CE2
/// (same bytes except the Rectangle PWC targets Width), 'CompareSlider-
/// RepeatButtonTransparent' at BAML+0x17DC2 (bare ContentPresenter with
/// Content/ActualHeight/ActualWidth TemplateBindings),
/// 'ScrollBarBaseRepeatButton' at BAML+0x17E7F (bare Rectangle with
/// Fill/Height/Width TemplateBindings), 'ToggleBlockBaseStyle' at
/// BAML+0x17F3C (landed by slice-128).
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways (the staged bytes: Style/-620 heads with the
/// TargetType over the -522 RepeatButton id plus the Setters content;
/// qualified OverridesDefaultStyle over owner -226 with "true"/
/// BooleanConverter, short-form Background -24 with a 0x06/744
/// packed-ARGB Transparent value, short-form Focusable -132 and IsTabStop
/// -34 with "false"/BooleanConverter, short-form Template -37 with the
/// complex ControlTemplate holding Border -50/Child -161/Rectangle -514
/// visuals, 0x23 TemplateBinding triples and LengthConverter -348 PWCs;
/// the upstream HandyControl Theme.xaml Slider blocks naming every setter
/// and template value; a genuine net10.0-windows ResourceDictionary
/// fixture compiling the Horizontal/Transparent/ScrollBarBase styles,
/// byte-identical in every staged record shape - -522 target, -226
/// qualified owner, -34 IsTabStop member, -102 StrokeThickness member,
/// -19 ContentPresenter.Content target, 744 brush, -348/-46 converters,
/// 634 TemplateBinding triples, 0x26 keys with shared=0/sharedSet=0 -
/// differing only in per-file table numbering and line markers).
/// One mid-slice correction: the slice-126 report names the
/// OverridesDefaultStyle qualified owner -225, but the staged bytes carry
/// 1E FF (-226, FrameworkElement, confirmed by the fixture compiling
/// Property="OverridesDefaultStyle"), so -226 is implemented.
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice127BamlRepeatButtonTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice127BamlRepeatButtonTests()
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
        var entry = LoadStagedHandyControlEntry();
        var apps = StagedHandyAppsDirectory();
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
        var root = document.RootElement;
        Assert.Equal("conversion-failed", root.GetProperty("status").GetString());
        var assemblies = root.GetProperty("assemblies").EnumerateArray().ToList();
        Assert.Equal(6, assemblies.Count);
        foreach (var assembly in assemblies)
        {
            var file = assembly.GetProperty("file").GetString()!;
            var expected = assembly.GetProperty("sha256").GetString()!;
            var actual = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(System.IO.Path.Combine(apps, file))));
            Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                $"hash mismatch in staged '{file}': {actual} != {expected}");
        }
        var gaps = root.GetProperty("gaps").EnumerateArray().ToList();
        Assert.Equal(2, gaps.Count);
        var details = gaps.Select(g => g.GetProperty("detail").GetString()!).ToList();
        Assert.Contains(details, d => d.Contains("System.Security.Permissions"));
        Assert.Contains(details, d => d.Contains("System.Windows.Extensions"));
        _ = entry;
    }

    [Fact]
    public void Theme_baml_lands_repeatbutton_styles_then_stops_at_groupbox()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        // The Horizontal entry head at BAML+0x17C02: Style start with the
        // TargetType over the -522 RepeatButton id plus Setters content.
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0xF6, 0xFD, 0x2E, 0x12, 0xFF },
            theme[0x17C02..0x17C0E]);
        // Qualified OverridesDefaultStyle PropertyCustom (owner -226,
        // FrameworkElement) plus the "true"/BooleanConverter value.
        Assert.Equal(
            new byte[] { 0x06, 0x1D, 0x01, 0x00, 0x89, 0x40, 0x1E, 0xFF, 0x15, 0x4F, 0x76, 0x65, 0x72, 0x72, 0x69, 0x64, 0x65, 0x73, 0x44, 0x65, 0x66, 0x61, 0x75, 0x6C, 0x74, 0x53, 0x74, 0x79, 0x6C, 0x65 },
            theme[0x17C12..0x17C30]);
        Assert.Equal(
            new byte[] { 0x24, 0x0A, 0x02, 0x00, 0x04, 0x74, 0x72, 0x75, 0x65, 0xD2, 0xFF },
            theme[0x17C30..0x17C3B]);
        // Short-form Background (-24) plus the 0x06/744 packed-ARGB
        // Transparent brush value.
        Assert.Equal(
            new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xE8, 0xFF },
            theme[0x17C40..0x17C48]);
        Assert.Equal(
            new byte[] { 0x06, 0x0A, 0x02, 0x00, 0xE8, 0x02, 0x01, 0xFF, 0xFF, 0xFF, 0x00 },
            theme[0x17C48..0x17C53]);
        // Short-form Focusable (-132) plus the "false" value.
        Assert.Equal(
            new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0x7C, 0xFF },
            theme[0x17C58..0x17C60]);
        Assert.Equal(
            new byte[] { 0x24, 0x0B, 0x02, 0x00, 0x05, 0x66, 0x61, 0x6C, 0x73, 0x65, 0xD2, 0xFF },
            theme[0x17C60..0x17C6C]);
        // Short-form IsTabStop (-34) plus the "false" value.
        Assert.Equal(
            new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDE, 0xFF },
            theme[0x17C71..0x17C79]);
        Assert.Equal(
            new byte[] { 0x24, 0x0B, 0x02, 0x00, 0x05, 0x66, 0x61, 0x6C, 0x73, 0x65, 0xD2, 0xFF },
            theme[0x17C79..0x17C85]);
        // Template setter head plus the complex ControlTemplate start with
        // the TargetType over -522 and the VisualTree content.
        Assert.Equal(
            new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDB, 0xFF },
            theme[0x17C86..0x17C92]);
        Assert.Equal(
            new byte[] { 0x07, 0x02, 0x00, 0x03, 0x94, 0xFF, 0x00, 0x22, 0x07, 0x00, 0xF6, 0xFD, 0x2E, 0x52, 0xFF },
            theme[0x17C92..0x17CA1]);
        // Border visual head plus the packed-ARGB brush value.
        Assert.Equal(
            new byte[] { 0x03, 0xCE, 0xFF, 0x00, 0x06, 0x0A, 0xFC, 0xFF, 0xE8 },
            theme[0x17CA1..0x17CAA]);
        Assert.Equal(
            new byte[] { 0x02, 0x01, 0xFF, 0xFF, 0xFF, 0x00, 0x23, 0xD1, 0xFF, 0x7A, 0x02, 0xD1, 0xFF },
            theme[0x17CAA..0x17CB7]);
        Assert.Equal(
            new byte[] { 0x23, 0xC7, 0xFF, 0x7A, 0x02, 0xC7, 0xFF, 0x2E, 0x5F, 0xFF, 0x03, 0xFE, 0xFD, 0x00 },
            theme[0x17CB7..0x17CC5]);
        // Rectangle StrokeThickness "0" (member -102, LengthConverter)
        // plus Fill-from-Background plus Height "2".
        Assert.Equal(
            new byte[] { 0x24, 0x07, 0x9A, 0xFF, 0x01, 0x30, 0xA4, 0xFE, 0x23, 0x9C, 0xFF, 0x7A, 0x02, 0xE8, 0xFF },
            theme[0x17CC5..0x17CD4]);
        Assert.Equal(
            new byte[] { 0x24, 0x07, 0xD1, 0xFF, 0x01, 0x32, 0xA4, 0xFE, 0x04, 0x04, 0x04, 0x08, 0x04, 0x04 },
            theme[0x17CD4..0x17CE2]);
        // The Vertical entry head plus its Rectangle Width "2" PWC (target
        // -57); the entry is otherwise byte-identical to Horizontal.
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0xF6, 0xFD, 0x2E, 0x12, 0xFF },
            theme[0x17CE2..0x17CEE]);
        Assert.Equal(
            new byte[] { 0x24, 0x07, 0xC7, 0xFF, 0x01, 0x32, 0xA4, 0xFE },
            theme[0x17DB4..0x17DBC]);
        Assert.Equal(
            new byte[] { 0x04, 0x04, 0x04, 0x08, 0x04, 0x04 },
            theme[0x17DBC..0x17DC2]);
        Assert.Equal(0xD1, theme[0x17C02 + 0xD4]);
        Assert.Equal(0xC7, theme[0x17CE2 + 0xD4]);
        Assert.Equal(
            theme[0x17C02..0x17CE2].ToArray().Select((b, i) => i == 0xD4 ? (byte)0xC7 : b).ToArray(),
            theme[0x17CE2..0x17DC2]);
        // The Transparent entry head plus its bare ContentPresenter visual
        // (Content/Height/Width TemplateBindings, no simples).
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0xF6, 0xFD, 0x2E, 0x12, 0xFF },
            theme[0x17DC2..0x17DCE]);
        Assert.Equal(
            new byte[] { 0x03, 0x9A, 0xFF, 0x00, 0x23, 0xED, 0xFF, 0x7A, 0x02, 0xF2, 0xFF },
            theme[0x17E61..0x17E6C]);
        Assert.Equal(
            new byte[] { 0x23, 0xD1, 0xFF, 0x7A, 0x02, 0x19, 0x00, 0x23, 0xC7, 0xFF, 0x7A, 0x02, 0x1A, 0x00 },
            theme[0x17E6C..0x17E7A]);
        Assert.Equal(
            new byte[] { 0x04, 0x04, 0x08, 0x04, 0x04 },
            theme[0x17E7A..0x17E7F]);
        // The ScrollBarBase entry head plus its bare Rectangle visual
        // (Fill/Height/Width TemplateBindings, no simples).
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0xF6, 0xFD, 0x2E, 0x12, 0xFF },
            theme[0x17E7F..0x17E8B]);
        Assert.Equal(
            new byte[] { 0x03, 0xFE, 0xFD, 0x00, 0x23, 0x9C, 0xFF, 0x7A, 0x02, 0xE8, 0xFF },
            theme[0x17F1E..0x17F29]);
        Assert.Equal(
            new byte[] { 0x23, 0xD1, 0xFF, 0x7A, 0x02, 0xD1, 0xFF, 0x23, 0xC7, 0xFF, 0x7A, 0x02, 0xC7, 0xFF },
            theme[0x17F29..0x17F37]);
        Assert.Equal(
            new byte[] { 0x04, 0x04, 0x08, 0x04, 0x04 },
            theme[0x17F37..0x17F3C]);
        // Key records for the run: 0x26 with the StringInfo id, the entry
        // offset and the shared pair (0/0: shared styles land once).
        Assert.Equal(
            new byte[] { 0x26, 0x09, 0x71, 0x00, 0x6B, 0x8A, 0x00, 0x00, 0x00, 0x00 },
            theme[0xB3D7..0xB3E1]);
        Assert.Equal(
            new byte[] { 0x26, 0x09, 0x72, 0x00, 0x4B, 0x8B, 0x00, 0x00, 0x00, 0x00 },
            theme[0xB3E1..0xB3EB]);
        Assert.Equal(
            new byte[] { 0x26, 0x09, 0x73, 0x00, 0x2B, 0x8C, 0x00, 0x00, 0x00, 0x00 },
            theme[0xB3EB..0xB3F5]);
        Assert.Equal(
            new byte[] { 0x26, 0x09, 0x74, 0x00, 0xE8, 0x8C, 0x00, 0x00, 0x00, 0x00 },
            theme[0xB3F5..0xB3FF]);
        Assert.Equal(
            new byte[] { 0x26, 0x09, 0x75, 0x00, 0xA5, 0x8D, 0x00, 0x00, 0x00, 0x00 },
            theme[0xB3FF..0xB409]);
        // The follow-up style head at BAML+0x17F3C: Style start with the
        // TargetType over the TypeInfo 45 ToggleBlock row plus Setters, then
        // the first setter with the short-form -38 PropertyCustom and the
        // "Center"/EnumConverter value.
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x2D, 0x00, 0x2E, 0x12, 0xFF },
            theme[0x17F3C..0x17F48]);
        Assert.Equal(
            new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDA, 0xFF },
            theme[0x17F48..0x17F54]);
        Assert.Equal(
            new byte[] { 0x24, 0x0C, 0x02, 0x00, 0x06, 0x43, 0x65, 0x6E, 0x74, 0x65, 0x72, 0x3D, 0xFF },
            theme[0x17F54..0x17F61]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(theme));
        // Entries #87-126 materialize in index order (the entry machine
        // consumes keys strictly in order, so any earlier failure would
        // throw its own offset instead).
        Assert.DoesNotContain("BAML+0x166FF", error.Message);
        Assert.DoesNotContain("PathBaseStyle", error.Message);
        Assert.DoesNotContain("BAML+0x167B8", error.Message);
        Assert.DoesNotContain("x:Static", error.Message);
        Assert.DoesNotContain("VerticalScrollBarButtonHeightKey", error.Message);
        Assert.DoesNotContain("BAML+0x167C8", error.Message);
        Assert.DoesNotContain("BehaviorXY200", error.Message);
        Assert.DoesNotContain("FluidMoveBehavior", error.Message);
        Assert.DoesNotContain("BAML+0x168D2", error.Message);
        Assert.DoesNotContain("BlendEffectBox", error.Message);
        Assert.DoesNotContain("BAML+0x16926", error.Message);
        Assert.DoesNotContain("BorderCircular", error.Message);
        Assert.DoesNotContain("converted type", error.Message);
        Assert.DoesNotContain("BAML+0x16954", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.Col", error.Message);
        Assert.DoesNotContain("well-known member id -132", error.Message);
        Assert.DoesNotContain("BAML+0x16990", error.Message);
        Assert.DoesNotContain("BAML+0x169D0", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.FlipClock", error.Message);
        Assert.DoesNotContain("templates take a ContentPresenter or StackPanel visual", error.Message);
        Assert.DoesNotContain("BAML+0x16BBB", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.FloatingBlock", error.Message);
        Assert.DoesNotContain("(implicit style key 'GridSplitter')", error.Message);
        Assert.DoesNotContain("BAML+0x16C96", error.Message);
        Assert.DoesNotContain("setter property owner Window", error.Message);
        Assert.DoesNotContain("well-known member id -24", error.Message);
        Assert.DoesNotContain("BAML+0x16E7C", error.Message);
        Assert.DoesNotContain("RectangleCircular", error.Message);
        Assert.DoesNotContain("SnapRect4ScreenshotStyle", error.Message);
        Assert.DoesNotContain("TargetArea4ScreenshotStyle", error.Message);
        Assert.DoesNotContain("holds a Trigger list", error.Message);
        Assert.DoesNotContain("BAML+0x1744C", error.Message);
        Assert.DoesNotContain("element Border on the template", error.Message);
        Assert.DoesNotContain("SimpleItemsControl", error.Message);
        Assert.DoesNotContain("BAML+0x17483", error.Message);
        Assert.DoesNotContain("Sprite", error.Message);
        Assert.DoesNotContain("IsDragElement", error.Message);
        Assert.DoesNotContain("BAML+0x1760B", error.Message);
        Assert.DoesNotContain("Right2LeftTransition", error.Message);
        Assert.DoesNotContain("Left2RightTransition", error.Message);
        Assert.DoesNotContain("Bottom2TopTransition", error.Message);
        Assert.DoesNotContain("Top2BottomTransition", error.Message);
        Assert.DoesNotContain("WithFadeTransition", error.Message);
        Assert.DoesNotContain("FadeTransition", error.Message);
        Assert.DoesNotContain("well-known type id -608", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.TransitioningContentControl", error.Message);
        Assert.DoesNotContain("BAML+0x17C06", error.Message);
        Assert.DoesNotContain("BAML+0x17C02", error.Message);
        Assert.DoesNotContain("SliderRepeatButtonHorizontalStyle", error.Message);
        Assert.DoesNotContain("SliderRepeatButtonVerticalStyle", error.Message);
        Assert.DoesNotContain("CompareSliderRepeatButtonTransparent", error.Message);
        Assert.DoesNotContain("ScrollBarBaseRepeatButton", error.Message);
        Assert.DoesNotContain("well-known type id -522", error.Message);
        Assert.DoesNotContain("BAML+0x17F4C", error.Message);
        Assert.DoesNotContain("ToggleBlockBaseStyle", error.Message);
        Assert.DoesNotContain("well-known member id -38", error.Message);
        Assert.DoesNotContain("BAML+0x1835D", error.Message);
        Assert.DoesNotContain("GroupBoxBaseTopTemplate", error.Message);
        Assert.DoesNotContain("BAML+0x1859F", error.Message);
        Assert.DoesNotContain("GroupBoxBaseLeftTemplate", error.Message);
        Assert.DoesNotContain("holds ControlTemplate", error.Message);
        Assert.DoesNotContain("BAML+0x1888C", error.Message);
        Assert.DoesNotContain("GroupBoxTabTopTemplate", error.Message);
        Assert.DoesNotContain("GroupBoxTabLeftTemplate", error.Message);
        Assert.DoesNotContain("template Binding holds record 0x38", error.Message);
        Assert.DoesNotContain("BAML+0x18D3F", error.Message);
        Assert.DoesNotContain("ButtonGroupHorizontalUniformItemsPanelTemplate", error.Message);
        Assert.DoesNotContain("holds well-known type id -330", error.Message);
        Assert.DoesNotContain("BAML+0x18DBD", error.Message);
        Assert.DoesNotContain("ProgressBarIndicatorAnimatedFill", error.Message);
        Assert.DoesNotContain("holds well-known type id -358", error.Message);
        Assert.DoesNotContain("BAML+0x18E5D", error.Message);
        Assert.DoesNotContain("BAML+0x19029", error.Message);
        Assert.DoesNotContain("CircleProgressBarTemplate", error.Message);
        Assert.DoesNotContain("VirtualizingStackHorizontalItemsPanelTemplate", error.Message);
        Assert.DoesNotContain("WrapHorizontalItemsPanelTemplate", error.Message);
        Assert.DoesNotContain("WrapVerticalItemsPanelTemplate", error.Message);
        Assert.DoesNotContain("DockItemsPanelTemplate", error.Message);
        Assert.DoesNotContain("ResizeGripperForeground", error.Message);
        Assert.DoesNotContain("SineEaseOut", error.Message);
        Assert.DoesNotContain("BAML+0x19150", error.Message);
        Assert.DoesNotContain("BadgeDotTemplate", error.Message);
        Assert.DoesNotContain("element ContentPresenter on the template root panel", error.Message);
        Assert.DoesNotContain("root panels take ContentPresenter, Border, converted Shape and TextBlock children", error.Message);
        Assert.DoesNotContain("BAML+0x1927D", error.Message);
        Assert.DoesNotContain("ColorPickerSliderThumbTemplate", error.Message);
        Assert.DoesNotContain("BAML+0x192B9", error.Message);
        Assert.DoesNotContain("ColorPickerRainbowBrush", error.Message);
        Assert.DoesNotContain("has no Offset", error.Message);
        Assert.DoesNotContain("BAML+0x19383", error.Message);
        Assert.DoesNotContain("ColorPickerOpacityBrush", error.Message);
        Assert.DoesNotContain("well-known type id -180", error.Message);
        Assert.DoesNotContain("BAML+0x19497", error.Message);
        Assert.DoesNotContain("CoverFlowBaseStyle", error.Message);
        Assert.DoesNotContain("well-known type id -729", error.Message);
        Assert.DoesNotContain("BAML+0x195FD", error.Message);
        Assert.DoesNotContain("GridViewColumnHeaderGripper", error.Message);
        Assert.DoesNotContain("well-known type id -66", error.Message);
        Assert.DoesNotContain("Canvas.Right", error.Message);
        Assert.DoesNotContain("BAML+0x196C6", error.Message);
        Assert.DoesNotContain("'Padding' is not a dependency property of Border", error.Message);
        Assert.DoesNotContain("SortByCategoryDrawingBrush", error.Message);
        Assert.DoesNotContain("BAML+0x196F6", error.Message);
        Assert.DoesNotContain("SortByNameDrawingBrush", error.Message);
        Assert.DoesNotContain("TabItemMenu", error.Message);
        Assert.DoesNotContain("BAML+0x19D99", error.Message);
        Assert.DoesNotContain("BAML+0x19DCE", error.Message);
        Assert.DoesNotContain("well-known type id -105", error.Message);
        Assert.DoesNotContain("Lang.Close", error.Message);
        Assert.DoesNotContain("throws reading the static property", error.Message);
        Assert.DoesNotContain("XmlnsDefinitionAttribute", error.Message);
        Assert.DoesNotContain("ToolBarTrayBaseStyle", error.Message);
        Assert.DoesNotContain("BAML+0x19E4F", error.Message);
        Assert.DoesNotContain("well-known type id -672", error.Message);
        Assert.DoesNotContain("TreeArrow", error.Message);
        Assert.DoesNotContain("BAML+0x19E7B", error.Message);
        Assert.DoesNotContain("well-known type id -446", error.Message);
        Assert.DoesNotContain("RowHeaderGripperStyle", error.Message);
        Assert.DoesNotContain("BAML+0x1A034", error.Message);
        Assert.DoesNotContain("'Cursor' is not a dependency property of FrameworkElement", error.Message);
        Assert.DoesNotContain("DataGridComboBoxColumnStyle", error.Message);
        Assert.DoesNotContain("DataGridTextColumnStyle", error.Message);        
        Assert.DoesNotContain("CarouselItemBaseStyle", error.Message);
        Assert.DoesNotContain("BAML+0x1A113", error.Message);
        Assert.DoesNotContain("BAML+0x1A186", error.Message);        
        Assert.DoesNotContain("BAML+0x1A21B", error.Message);
        Assert.DoesNotContain("well-known type id -92", error.Message);
        Assert.DoesNotContain("targets TextBlock", error.Message);        
        Assert.DoesNotContain("template property id -21", error.Message);
        Assert.DoesNotContain("GrowlWindow", error.Message);
        Assert.DoesNotContain("BAML+0x1A2CA", error.Message);
        Assert.DoesNotContain("well-known member id -51", error.Message);
        Assert.DoesNotContain("ListBoxItemCustom", error.Message);
        Assert.DoesNotContain("BAML+0x1A368", error.Message);
        Assert.DoesNotContain("well-known type id -374", error.Message);
        Assert.DoesNotContain("BAML+0x1A92D", error.Message);
        Assert.DoesNotContain("well-known member id -81", error.Message);
        Assert.DoesNotContain("RibbonTab", error.Message);
        Assert.DoesNotContain("BAML+0x1A990", error.Message);
        Assert.DoesNotContain("template root panel property 'Visibility' with converter -195", error.Message);
        Assert.DoesNotContain("BAML+0x1A9DD", error.Message);
        Assert.DoesNotContain("element well-known type id -550 on the template root panel", error.Message);
        Assert.DoesNotContain("BAML+0x1AE91", error.Message);
        Assert.DoesNotContain("'EffectShadow1'", error.Message);
        Assert.DoesNotContain("record 0x34 (converted-element values", error.Message);
        Assert.DoesNotContain("BAML+0x1B001", error.Message);
        Assert.DoesNotContain("BAML+0x1B22E", error.Message);
        Assert.DoesNotContain("'SearchPathStyle'", error.Message);
        Assert.DoesNotContain("'RightPathStyle'", error.Message);
        Assert.DoesNotContain("record 0x38 in a Style", error.Message);
        Assert.DoesNotContain("'RenderTransformOrigin' is not a dependency property of UIElement", error.Message);
        Assert.DoesNotContain("BAML+0x1B260", error.Message);
        Assert.DoesNotContain("well-known member id -134", error.Message);
        Assert.Contains("'BorderRegion'", error.Message);
        Assert.Contains("BAML+0x1B2F9", error.Message);
        Assert.Contains("setter property owner Border in style entry 'BorderRegion'", error.Message);
        Assert.DoesNotContain("BAML+0x1AA35", error.Message);
        Assert.DoesNotContain("'PrimaryBrush'", error.Message);
        Assert.DoesNotContain("record 0x34 in a LinearGradientBrush", error.Message);
        Assert.DoesNotContain("BAML+0x1AA1C", error.Message);
        Assert.DoesNotContain("LightPrimaryBrush", error.Message);
        Assert.DoesNotContain("well-known type id -578", error.Message);
        Assert.DoesNotContain("on setter 'Template'", error.Message);
        Assert.DoesNotContain("well-known type id -658", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimplePanel", error.Message);
        Assert.DoesNotContain("VirtualizingStackPanel", error.Message);
        Assert.DoesNotContain("templates take a ContentPresenter", error.Message);
        Assert.DoesNotContain("items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", error.Message);
        Assert.DoesNotContain("short-form properties beyond FlowDirection/Template/Focusable/IsEnabled/HorizontalAlignment/VerticalAlignment/Background/Height/Width/Fill/Visibility/BorderThickness/BorderBrush/Margin/IsTabStop", error.Message);
    }

    [Fact]
    public void Staged_boot_advances_past_repeatbutton_to_groupbox()
    {
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("startup failed in entry point", error.Message);
        // The line-17 SkinDefault stop, the line-23 Theme key/index plus
        // scalar run, PathBaseStyle, the x:Static-keyed Double, the six
        // converted Behavior entries, the implicit BlendEffectBox style, the
        // 'BorderCircular' entry, the implicit Col/ContentControl styles,
        // the implicit FlipClock style, the implicit FloatingBlock style,
        // the implicit GridSplitter style, the implicit Notification style,
        // the 'RectangleCircular' entry, the 'SnapRect4ScreenshotStyle' flat
        // run plus its Trigger list, the 'TargetArea4ScreenshotStyle' entry,
        // the implicit SimpleItemsControl style (Template/Border visual with
        // TemplateBindings plus PART_Panel) and the implicit Sprite style
        // (attached/window setters plus Template/ContentPresenter visual),
        // the nine transition Storyboards (keyidx 113-121), the implicit
        // TransitioningContentControl style and the four keyed RepeatButton
        // styles (keyidx 123-126: Border/Rectangle and ContentPresenter
        // visuals with TemplateBindings), the keyed 'ToggleBlockBaseStyle'
        // style (keyidx 127) and the keyed 'GroupBoxBaseTopTemplate' template
        // (keyidx 128) plus the keyed 'GroupBoxBaseLeftTemplate' template
        // (keyidx 129) plus the keyed 'GroupBoxTabTopTemplate' template
        // (keyidx 130) and 'GroupBoxTabLeftTemplate' template (keyidx 131)
        // now resolve; boot fails at the keyed
        // 'ButtonGroupHorizontalUniformItemsPanelTemplate' entry - the
        // precise next blocker.
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
        Assert.DoesNotContain("BAML+0x16BBB", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.FloatingBlock", inner.Message);
        Assert.DoesNotContain("(implicit style key 'GridSplitter')", inner.Message);
        Assert.DoesNotContain("BAML+0x16C96", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.Notification", inner.Message);
        Assert.DoesNotContain("setter property owner Window", inner.Message);
        Assert.DoesNotContain("BAML+0x16E7C", inner.Message);
        Assert.DoesNotContain("RectangleCircular", inner.Message);
        Assert.DoesNotContain("SnapRect4ScreenshotStyle", inner.Message);
        Assert.DoesNotContain("TargetArea4ScreenshotStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x1744C", inner.Message);
        Assert.DoesNotContain("element Border on the template", inner.Message);
        Assert.DoesNotContain("SimpleItemsControl", inner.Message);
        Assert.DoesNotContain("BAML+0x17483", inner.Message);
        Assert.DoesNotContain("Sprite", inner.Message);
        Assert.DoesNotContain("IsDragElement", inner.Message);
        Assert.DoesNotContain("BAML+0x1760B", inner.Message);
        Assert.DoesNotContain("Right2LeftTransition", inner.Message);
        Assert.DoesNotContain("WithFadeTransition", inner.Message);
        Assert.DoesNotContain("FadeTransition", inner.Message);
        Assert.DoesNotContain("well-known type id -608", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.TransitioningContentControl", inner.Message);
        Assert.DoesNotContain("BAML+0x17C06", inner.Message);
        Assert.DoesNotContain("BAML+0x17C02", inner.Message);
        Assert.DoesNotContain("SliderRepeatButtonHorizontalStyle", inner.Message);
        Assert.DoesNotContain("SliderRepeatButtonVerticalStyle", inner.Message);
        Assert.DoesNotContain("CompareSliderRepeatButtonTransparent", inner.Message);
        Assert.DoesNotContain("ScrollBarBaseRepeatButton", inner.Message);
        Assert.DoesNotContain("well-known type id -522", inner.Message);
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
    public void Staged_horizontal_entry_bytes_land_as_live_style()
    {
        // The staged 'SliderRepeatButtonHorizontalStyle' entry shape (five
        // setters plus a Border/Rectangle template) lands as a live engine
        // style: the synthetic entry below is byte-identical to the staged
        // [BAML+0x17C02, BAML+0x17CE2) span, so the landing proves the staged
        // bytes materialize, while the staged-run tests above prove the real
        // parser consumes them.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var entry = HorizontalEntry().ToArray();
        Assert.Equal(theme[0x17C02..0x17CE2], entry);
        var dictionary = BamlReader.LoadDictionary(KeyedStyleDictionary("SliderRepeatButtonHorizontalStyle", entry));
        var style = Assert.IsType<Style>(dictionary["SliderRepeatButtonHorizontalStyle"]);
        Assert.Same(typeof(RepeatButton), style.TargetType);
        Assert.Equal(5, style.Setters.Count);
        var overrides = Assert.IsType<Setter>(style.Setters[0]);
        Assert.Same(FrameworkElement.OverridesDefaultStyleProperty, overrides.Property);
        Assert.Equal(true, overrides.Value);
        var background = Assert.IsType<Setter>(style.Setters[1]);
        Assert.Same(Control.BackgroundProperty, background.Property);
        Assert.Equal(Color.FromArgb(0, 255, 255, 255), Assert.IsType<SolidColorBrush>(background.Value).Color);
        var focusable = Assert.IsType<Setter>(style.Setters[2]);
        Assert.Same(UIElement.FocusableProperty, focusable.Property);
        Assert.Equal(false, focusable.Value);
        var tabStop = Assert.IsType<Setter>(style.Setters[3]);
        Assert.Same(Control.IsTabStopProperty, tabStop.Property);
        Assert.Equal(false, tabStop.Value);
        var templateSetter = Assert.IsType<Setter>(style.Setters[4]);
        Assert.Same(Control.TemplateProperty, templateSetter.Property);
        var template = Assert.IsType<ControlTemplate>(templateSetter.Value);
        Assert.Same(typeof(RepeatButton), template.TargetType);
        var border = Assert.IsType<Border>(template.VisualTree);
        Assert.Equal(Color.FromArgb(0, 255, 255, 255), Assert.IsType<SolidColorBrush>(border.Background).Color);
        var rectangle = Assert.IsType<Rectangle>(border.Child);
        Assert.Equal(0.0, rectangle.StrokeThickness);
        Assert.Equal(0.0, rectangle.GetValue(Shape.StrokeThicknessProperty));
        Assert.Equal(2.0, rectangle.Height);
        Assert.Equal(3, template.TemplateBindings.Count);
        Assert.Same(border, Assert.IsType<Border>(template.TemplateBindings[0].Element));
        Assert.Equal("Height", template.TemplateBindings[0].Property);
        Assert.Same(border, Assert.IsType<Border>(template.TemplateBindings[1].Element));
        Assert.Equal("Width", template.TemplateBindings[1].Property);
        Assert.Same(rectangle, Assert.IsType<Rectangle>(template.TemplateBindings[2].Element));
        Assert.Equal("Background", template.TemplateBindings[2].Property);
    }

    [Fact]
    public void Staged_vertical_entry_bytes_land_with_width_two()
    {
        // The staged 'SliderRepeatButtonVerticalStyle' entry shape lands the
        // same five setters with the Rectangle Width "2" PWC: the synthetic
        // entry is byte-identical to the staged [BAML+0x17CE2, BAML+0x17DC2)
        // span and differs from the horizontal span in exactly the Height/
        // Width target byte.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var entry = VerticalEntry().ToArray();
        Assert.Equal(theme[0x17CE2..0x17DC2], entry);
        var horizontal = HorizontalEntry().ToArray();
        Assert.Equal(horizontal.Length, entry.Length);
        for (var i = 0; i < entry.Length; i++)
            Assert.True(entry[i] == horizontal[i] || i == 0xD4, $"vertical differs from horizontal outside +0xD4 (at +0x{i:X})");
        Assert.Equal(0xD1, horizontal[0xD4]);
        Assert.Equal(0xC7, entry[0xD4]);
        var dictionary = BamlReader.LoadDictionary(KeyedStyleDictionary("SliderRepeatButtonVerticalStyle", entry));
        var style = Assert.IsType<Style>(dictionary["SliderRepeatButtonVerticalStyle"]);
        Assert.Same(typeof(RepeatButton), style.TargetType);
        var template = Assert.IsType<ControlTemplate>(Assert.IsType<Setter>(style.Setters[4]).Value);
        var border = Assert.IsType<Border>(template.VisualTree);
        var rectangle = Assert.IsType<Rectangle>(border.Child);
        Assert.Equal(2.0, rectangle.Width);
        Assert.Equal(3, template.TemplateBindings.Count);
    }

    [Fact]
    public void Staged_transparent_entry_bytes_land_presenter_bindings()
    {
        // The staged 'CompareSliderRepeatButtonTransparent' entry shape (a
        // bare ContentPresenter visual with Content/Height/Width
        // TemplateBindings over the per-file ActualHeight/ActualWidth rows)
        // lands with three recorded bindings: the synthetic entry is
        // byte-identical to the staged [BAML+0x17DC2, BAML+0x17E7F) span.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var entry = TransparentEntry().ToArray();
        Assert.Equal(theme[0x17DC2..0x17E7F], entry);
        var dictionary = BamlReader.LoadDictionary(KeyedStyleDictionary("CompareSliderRepeatButtonTransparent", entry));
        var style = Assert.IsType<Style>(dictionary["CompareSliderRepeatButtonTransparent"]);
        Assert.Same(typeof(RepeatButton), style.TargetType);
        Assert.Equal(5, style.Setters.Count);
        var template = Assert.IsType<ControlTemplate>(Assert.IsType<Setter>(style.Setters[4]).Value);
        var presenter = Assert.IsType<ContentPresenter>(template.VisualTree);
        Assert.Equal(3, template.TemplateBindings.Count);
        Assert.Same(presenter, template.TemplateBindings[0].Element);
        Assert.Equal("Content", template.TemplateBindings[0].Property);
        Assert.Same(presenter, template.TemplateBindings[1].Element);
        Assert.Equal("ActualHeight", template.TemplateBindings[1].Property);
        Assert.Same(presenter, template.TemplateBindings[2].Element);
        Assert.Equal("ActualWidth", template.TemplateBindings[2].Property);
    }

    [Fact]
    public void Staged_scrollbar_entry_bytes_land_rectangle_root()
    {
        // The staged 'ScrollBarBaseRepeatButton' entry shape (a bare
        // Rectangle visual with Fill/Height/Width TemplateBindings) lands
        // with three recorded bindings: the synthetic entry is byte-identical
        // to the staged [BAML+0x17E7F, BAML+0x17F3C) span.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var entry = ScrollBarEntry().ToArray();
        Assert.Equal(theme[0x17E7F..0x17F3C], entry);
        var dictionary = BamlReader.LoadDictionary(KeyedStyleDictionary("ScrollBarBaseRepeatButton", entry));
        var style = Assert.IsType<Style>(dictionary["ScrollBarBaseRepeatButton"]);
        Assert.Same(typeof(RepeatButton), style.TargetType);
        var template = Assert.IsType<ControlTemplate>(Assert.IsType<Setter>(style.Setters[4]).Value);
        var rectangle = Assert.IsType<Rectangle>(template.VisualTree);
        Assert.Equal(3, template.TemplateBindings.Count);
        Assert.Same(rectangle, template.TemplateBindings[0].Element);
        Assert.Equal("Background", template.TemplateBindings[0].Property);
        Assert.Same(rectangle, template.TemplateBindings[1].Element);
        Assert.Equal("Height", template.TemplateBindings[1].Property);
        Assert.Same(rectangle, template.TemplateBindings[2].Element);
        Assert.Equal("Width", template.TemplateBindings[2].Property);
    }

    [Fact]
    public void RepeatButton_template_has_no_inflation_markup()
    {
        // The landed RepeatButton templates retain their visual roots
        // without inflation markup, so inflating throws the precise
        // BAML-tree inflation boundary instead of sharing one instance.
        LoadStagedHandyControlEntry();
        var dictionary = BamlReader.LoadDictionary(KeyedStyleDictionary("SliderRepeatButtonHorizontalStyle", HorizontalEntry().ToArray()));
        var style = Assert.IsType<Style>(dictionary["SliderRepeatButtonHorizontalStyle"]);
        var template = Assert.IsType<ControlTemplate>(Assert.IsType<Setter>(style.Setters[4]).Value);
        var error = Assert.Throws<InvalidOperationException>(() => template.LoadContent());
        Assert.Contains("no inflation markup", error.Message);
        Assert.Contains("BAML-tree inflation", error.Message);
    }

    [Fact]
    public void OverridesDefaultStyle_is_a_framework_element_dp()
    {
        // WPF parity: OverridesDefaultStyle exists as a DP identity owned by
        // FrameworkElement (the staged RepeatButton setter resolves the -226
        // qualified owner to it); the CLR wrapper stays store-backed so
        // style setters and direct sets agree, and non-framework hosts keep
        // a plain bool through the UIElement bridge.
        Assert.Same(typeof(FrameworkElement), FrameworkElement.OverridesDefaultStyleProperty.OwnerType);
        Assert.Equal("OverridesDefaultStyle", FrameworkElement.OverridesDefaultStyleProperty.Name);
        Assert.Equal(typeof(bool), FrameworkElement.OverridesDefaultStyleProperty.PropertyType);
        var button = new RepeatButton();
        Assert.False(button.OverridesDefaultStyle);
        Assert.Equal(false, button.GetValue(FrameworkElement.OverridesDefaultStyleProperty));
        button.OverridesDefaultStyle = true;
        Assert.Equal(true, button.GetValue(FrameworkElement.OverridesDefaultStyleProperty));
        button.SetValue(FrameworkElement.OverridesDefaultStyleProperty, false);
        Assert.False(button.OverridesDefaultStyle);
    }

    [Fact]
    public void IsTabStop_short_form_lands_false_on_engine_repeatbutton()
    {
        // The staged IsTabStop shape (short-form -34 PropertyCustom plus a
        // False/BooleanConverter value) lands as a live Setter carrying the
        // Control IsTabStop DP; applying it to a real RepeatButton flips the
        // default true through the DP store.
        XamlLoader.CustomElementSource = null;
        var bytes = ImplicitShortFormDictionary("DesktopWasm.Wpf.RepeatButton", -34, "False", -46);
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(RepeatButton)]);
        Assert.Same(typeof(RepeatButton), style.TargetType);
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
        Assert.Same(Control.IsTabStopProperty, setter.Property);
        Assert.Equal(false, setter.Value);
        var target = new RepeatButton();
        Assert.True(KeyboardNavigation.GetIsTabStop(target));
        style.ApplyTo(target);
        Assert.False(KeyboardNavigation.GetIsTabStop(target));
    }

    [Fact]
    public void IsTabStop_on_target_without_dp_fails_precise()
    {
        // The short-form member resolves against the style TargetType: a
        // target with no IsTabStop DP anywhere in its hierarchy fails
        // precise naming the target instead of landing a half-built setter.
        XamlLoader.CustomElementSource = null;
        var bytes = ImplicitShortFormDictionary(typeof(Slice127PlainStub).FullName!, -34, "False", -46);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("'IsTabStop' is not a dependency property of Slice127PlainStub", error.Message);
    }

    [Fact]
    public void StrokeThickness_is_a_store_backed_shape_dp()
    {
        // WPF parity: StrokeThickness exists as a DP identity owned by Shape
        // (the staged Rectangle PWC resolves member -102 to it); the CLR
        // wrapper stays store-backed with the 1.0 default so binding pushes
        // and direct sets agree.
        Assert.Same(typeof(Shape), Shape.StrokeThicknessProperty.OwnerType);
        Assert.Equal("StrokeThickness", Shape.StrokeThicknessProperty.Name);
        var rectangle = new Rectangle();
        Assert.Equal(1.0, rectangle.StrokeThickness);
        Assert.Equal(1.0, rectangle.GetValue(Shape.StrokeThicknessProperty));
        rectangle.StrokeThickness = 0;
        Assert.Equal(0.0, rectangle.GetValue(Shape.StrokeThicknessProperty));
        rectangle.SetValue(Shape.StrokeThicknessProperty, 2.5);
        Assert.Equal(2.5, rectangle.StrokeThickness);
    }

    [Fact]
    public void LengthConverter_template_value_lands_and_garbage_fails_precise()
    {
        // Template LengthConverter doubles land (the staged StrokeThickness
        // "0" and Height/Width "2"); non-double text with the LengthConverter
        // fails precise naming the converter instead of landing NaN.
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(RepeatButtonBorderDictionary(
            RectangleChild(new B().Sized(0x24, q => q.I16(-102).Str("0").I16(-348)).Raw(0x04))));
        var style = Assert.IsType<Style>(dictionary[typeof(RepeatButton)]);
        var template = Assert.IsType<ControlTemplate>(Assert.IsType<Setter>(Assert.Single(style.Setters)).Value);
        var border = Assert.IsType<Border>(template.VisualTree);
        var rectangle = Assert.IsType<Rectangle>(border.Child);
        Assert.Equal(0.0, rectangle.StrokeThickness);
        var garbage = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(RepeatButtonBorderDictionary(
            RectangleChild(new B().Sized(0x24, q => q.I16(-102).Str("abc").I16(-348)).Raw(0x04)))));
        Assert.Contains("'abc' with converter -348 does not convert to Double", garbage.Message);
        Assert.Contains("template simple values take", garbage.Message);
    }

    [Fact]
    public void Template_brush_wrong_serializer_fails_precise()
    {
        // Template Background brushes travel as packed-ARGB (744); any other
        // serializer fails naming it instead of landing a raw value.
        XamlLoader.CustomElementSource = null;
        var bytes = RepeatButtonBorderDictionary(new B()
            .Sized(0x06, q => q.I16(-4).U16(137).Raw(0x01, 0xFF, 0xFF, 0xFF, 0x00))
            .Raw(0x04));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("holds a brush value with serializer 137", error.Message);
        Assert.Contains("template brushes take packed-ARGB values", error.Message);
    }

    [Fact]
    public void Template_brush_non_background_attr_fails_precise()
    {
        // Template brushes land the Background value only; a packed-ARGB
        // value for any other attribute fails naming it.
        XamlLoader.CustomElementSource = null;
        var bytes = RepeatButtonBorderDictionary(new B()
            .Sized(0x06, q => q.I16(-5).U16(744).Raw(0x01, 0xFF, 0xFF, 0xFF, 0x00))
            .Raw(0x04));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("holds a packed-ARGB brush for attribute id -5", error.Message);
        Assert.Contains("template brushes take the Background value", error.Message);
    }

    [Fact]
    public void Template_rectangle_unknown_simple_fails_precise()
    {
        // Rectangle template simples take the staged StrokeThickness/Height/
        // Width members; any other member id fails naming it.
        XamlLoader.CustomElementSource = null;
        var bytes = RepeatButtonBorderDictionary(RectangleChild(new B()
            .Sized(0x24, q => q.I16(-111).Str("0").I16(-348))
            .Raw(0x04)));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("template property id -111", error.Message);
        Assert.Contains("template members beyond Margin/Text/TextTrimming/StrokeThickness/Height/Width/MinHeight/MinWidth/Background", error.Message);
    }

    [Fact]
    public void Template_rectangle_unknown_target_fails_precise()
    {
        // Rectangle TemplateBinding targets are Fill/Height/Width; anything
        // else fails naming the id.
        XamlLoader.CustomElementSource = null;
        var bytes = RepeatButtonBorderDictionary(RectangleChild(new B()
            .Raw(0x23).I16(-110).U16(634).I16(-24)
            .Raw(0x04)));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("template extension target id -110", error.Message);
        Assert.Contains("Fill/Height/Width/Stroke on a Rectangle", error.Message);
    }

    [Fact]
    public void Template_rectangle_unknown_source_fails_precise()
    {
        // Rectangle TemplateBinding sources are the framework-known rows
        // (slice-129 adds Foreground for the GroupBox header); an unknown
        // well-known id fails naming it.
        XamlLoader.CustomElementSource = null;
        var bytes = RepeatButtonBorderDictionary(RectangleChild(new B()
            .Raw(0x23).I16(-100).U16(634).I16(-999)
            .Raw(0x04)));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("template extension source id -999", error.Message);
        Assert.Contains("Content/Height/Width/HorizontalContentAlignment/VerticalContentAlignment/Foreground or an AttributeInfo row", error.Message);
    }

    [Fact]
    public void Template_rectangle_wrong_record_fails_precise()
    {
        // Rectangles take simples plus TemplateBindings; any other record
        // fails naming it.
        XamlLoader.CustomElementSource = null;
        var bytes = RepeatButtonBorderDictionary(RectangleChild(new B()
            .Raw(0x2E).I16(-161)
            .Raw(0x04)));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("template Rectangle holds record 0x2E", error.Message);
        Assert.Contains("rectangles take simple values plus TemplateBinding values", error.Message);
    }

    [Fact]
    public void Border_grid_child_still_fails_precise()
    {
        // Border children are a StackPanel, a Rectangle, a ContentPresenter
        // (slice-129 lands the GroupBox presenters) or a converted panel; a
        // Grid visual still fails naming it.
        XamlLoader.CustomElementSource = null;
        var bytes = RepeatButtonBorderDictionary(new B()
            .Raw(0x2E).I16(-161)
            .Raw(0x03).I16(-254).Raw(0x00).Raw(0x04)
            .Raw(0x04));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("element Grid on the template Border", error.Message);
        Assert.Contains("template borders take a StackPanel, Rectangle, ContentPresenter, SimplePanel or Viewport3D child", error.Message);
    }

    [Fact]
    public void ToggleBlock_short_form_lands_vertical_content_alignment()
    {
        // The staged 'ToggleBlockBaseStyle' shape (short-form -38
        // VerticalContentAlignment with a "Center"/EnumConverter value)
        // lands on the engine RepeatButton through the new Control DP;
        // applying it flips the alignment through the DP store.
        XamlLoader.CustomElementSource = null;
        var bytes = ImplicitShortFormDictionary("DesktopWasm.Wpf.RepeatButton", -38, "Center", -195);
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(RepeatButton)]);
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
        Assert.Same(Control.VerticalContentAlignmentProperty, setter.Property);
        Assert.Equal(VerticalAlignment.Center, setter.Value);
        var target = new RepeatButton();
        Assert.Equal(VerticalAlignment.Top, target.VerticalContentAlignment);
        style.ApplyTo(target);
        Assert.Equal(VerticalAlignment.Center, target.VerticalContentAlignment);
    }

    [Fact]
    public void Qualified_framework_element_owner_lands_overrides_default_style()
    {
        // The staged OverridesDefaultStyle shape (qualified -226 owner plus
        // a "true"/BooleanConverter value) lands on the engine RepeatButton
        // through the new FrameworkElement DP; applying it flips the style
        // opt-out through the DP store.
        XamlLoader.CustomElementSource = null;
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-522)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(16521).I16(-226).Str("OverridesDefaultStyle"))
            .Sized(0x24, q => q.I16(2).Str("true").I16(-46))
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var dictionary = BamlReader.LoadDictionary(KeyedStyleDictionary("S", content));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(RepeatButton), style.TargetType);
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
        Assert.Same(FrameworkElement.OverridesDefaultStyleProperty, setter.Property);
        Assert.Equal(true, setter.Value);
        var target = new RepeatButton();
        Assert.False(target.OverridesDefaultStyle);
        style.ApplyTo(target);
        Assert.True(target.OverridesDefaultStyle);
    }

    [Fact]
    public void Qualified_unknown_owner_still_fails_precise()
    {
        // Qualified owners beyond Shape/UIElement/Window/FrameworkElement/Canvas
        // stay a precise follow-up with the extended owner list named.
        XamlLoader.CustomElementSource = null;
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-522)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(16521).I16(-999).Str("Foo"))
            .Sized(0x24, q => q.I16(2).Str("true").I16(-46))
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(KeyedStyleDictionary("S", content)));
        Assert.Contains("setter property owner well-known type id -999", error.Message);
        Assert.Contains("owners beyond Shape/UIElement/Window/FrameworkElement/Canvas", error.Message);
    }

    [Fact]
    public void RepeatButton_target_lands_but_unknown_target_stays_precise()
    {
        // The -522 RepeatButton target resolves to the engine type; an
        // unknown well-known target still fails with the extended list.
        XamlLoader.CustomElementSource = null;
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-999)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(KeyedStyleDictionary("S", content)));
        Assert.Contains("targets well-known type id -999", error.Message);
        Assert.Contains("style targets beyond Path/Border/ContentControl/GridSplitter/Rectangle/RepeatButton/GroupBox/Thumb", error.Message);
    }

    [Fact]
    public void StaticResource_setter_value_bad_attribute_stays_precise()
    {
        // Setter StaticResource values (0x38) route through the engine
        // deferred RawValue path since slice-160; a static over an attribute
        // id with no AttributeInfo record (here the staged GroupBox
        // numbering 37, absent from this dictionary) stays a precise
        // follow-up naming the id.
        XamlLoader.CustomElementSource = null;
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-522)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-132))
            .Raw(0x38).U16(37).U16(0)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(KeyedStyleDictionary("S", content)));
        Assert.Contains("attribute id 37", error.Message);
        Assert.Contains("has no AttributeInfo record", error.Message);
    }

    /// <summary>Builds the staged 'SliderRepeatButtonHorizontalStyle' entry
    /// shape: five setters (qualified OverridesDefaultStyle, Background
    /// brush, Focusable, IsTabStop, Template with a Border visual carrying
    /// a packed-ARGB Background, Height/Width TemplateBindings and a
    /// Rectangle child with StrokeThickness "0", Fill-from-Background and
    /// Height "2"). Byte-identical to the staged
    /// [BAML+0x17C02, BAML+0x17CE2) span (asserted in the landing test).
    /// </summary>
    private static B HorizontalEntry() => BorderTemplateEntry(
        new B().Sized(0x24, q => q.I16(-102).Str("0").I16(-348))
            .Raw(0x23).I16(-100).U16(634).I16(-24)
            .Sized(0x24, q => q.I16(-47).Str("2").I16(-348))
            .Raw(0x04));

    private static B VerticalEntry() => BorderTemplateEntry(
        new B().Sized(0x24, q => q.I16(-102).Str("0").I16(-348))
            .Raw(0x23).I16(-100).U16(634).I16(-24)
            .Sized(0x24, q => q.I16(-57).Str("2").I16(-348))
            .Raw(0x04));

    /// <summary>Builds the staged 'CompareSliderRepeatButtonTransparent'
    /// entry shape: the same five setters with a bare ContentPresenter
    /// visual carrying Content/Height/Width TemplateBindings (Height/Width
    /// over the per-file ActualHeight/ActualWidth rows 25/26).</summary>
    private static B TransparentEntry() => RepeatButtonTemplateEntry(new B()
        .Raw(0x03).I16(-102).Raw(0x00)
        .Raw(0x23).I16(-19).U16(634).I16(-14)
        .Raw(0x23).I16(-47).U16(634).I16(25)
        .Raw(0x23).I16(-57).U16(634).I16(26)
        .Raw(0x04));

    /// <summary>Builds the staged 'ScrollBarBaseRepeatButton' entry shape:
    /// the same five setters with a bare Rectangle visual carrying
    /// Fill/Height/Width TemplateBindings.</summary>
    private static B ScrollBarEntry() => RepeatButtonTemplateEntry(new B()
        .Raw(0x03).I16(-514).Raw(0x00)
        .Raw(0x23).I16(-100).U16(634).I16(-24)
        .Raw(0x23).I16(-47).U16(634).I16(-47)
        .Raw(0x23).I16(-57).U16(634).I16(-57)
        .Raw(0x04));

    [Fact]
    public void Template_trigger_list_lands_on_template()
    {
        // ControlTemplate trigger lists land on the template (the staged
        // ToggleBlock template carries three IsChecked Triggers with
        // TargetName setters); EventTriggers behind the ToggleBlock stop
        // stay a precise follow-up.
        XamlLoader.CustomElementSource = null;
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-522)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(7).I16(-522)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(-514).Raw(0x00)
            .Raw(0x04)
            .Raw(0x0B).U16(27)
            .Raw(0x03).I16(-688).Raw(0x00)
            .Sized(0x06, q => q.I16(13).U16(137).I16(-132))
            .Sized(0x24, q => q.I16(14).Str("True").I16(-46))
            .Raw(0x2E).I16(-256)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-135))
            .Sized(0x24, q => q.I16(2).Str("Collapsed").I16(-195))
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x0C)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var bytes = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(7).I16(-108).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(27).I16(-108).Raw(0x00).Str("Triggers"))
            .Sized(0x1F, p => p.I16(13).I16(-688).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(14).I16(-688).Raw(0x00).Str("Value"))
            .Sized(0x20, p => p.U16(0).Str("S"))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0))
            .Raw(entry).FinishDictionary().ToArray();
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary["S"]);
        var template = Assert.IsType<ControlTemplate>(((Setter)style.Setters[0]).Value);
        Assert.IsType<Rectangle>(template.VisualTree);
        var trigger = Assert.IsType<Trigger>(Assert.Single(template.Triggers));
        Assert.Equal("Focusable", trigger.Property);
        Assert.Equal("True", trigger.Value);
        var setter = Assert.IsType<Setter>(Assert.Single(trigger.Setters));
        Assert.Null(setter.TargetName);
        Assert.Same(UIElement.VisibilityProperty, setter.Property);
        Assert.Equal(Visibility.Collapsed, setter.Value);
    }

    /// <summary>Builds the staged five-setter prefix shared by all four
    /// RepeatButton entries (qualified OverridesDefaultStyle, Background
    /// brush, Focusable, IsTabStop) around the given template visual.
    /// Attribute rows mirror the staged file (0 TargetType/-620, 1
    /// Property/-556, 2 Value/-556, 7 TargetType/-108, 25/26
    /// ActualHeight/ActualWidth/-226).</summary>
    private static B RepeatButtonTemplateEntry(B visual) => new B()
        .Raw(0x03).I16(-620).Raw(0x00)
        .Raw(0x22).I16(0).I16(-522)
        .Raw(0x2E).I16(-238)
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16(1).U16(16521).I16(-226).Str("OverridesDefaultStyle"))
        .Sized(0x24, q => q.I16(2).Str("true").I16(-46))
        .Raw(0x04)
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16(1).U16(137).I16(-24))
        .Sized(0x06, q => q.I16(2).U16(744).Raw(0x01, 0xFF, 0xFF, 0xFF, 0x00))
        .Raw(0x04)
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16(1).U16(137).I16(-132))
        .Sized(0x24, q => q.I16(2).Str("false").I16(-46))
        .Raw(0x04)
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16(1).U16(137).I16(-34))
        .Sized(0x24, q => q.I16(2).Str("false").I16(-46))
        .Raw(0x04)
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
        .Raw(0x07).I16(2)
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16(7).I16(-522)
        .Raw(0x2E).I16(-174)
        .Raw(visual.ToArray())
        .Raw(0x04)
        .Raw(0x08)
        .Raw(0x04)
        .Raw(0x04);

    /// <summary>Wraps rectangle content in the staged Border visual shape
    /// (packed-ARGB Background plus Height/Width self TemplateBindings plus
    /// the Child content member) around the five-setter prefix.</summary>
    private static B BorderTemplateEntry(B rectangleContent) => RepeatButtonTemplateEntry(new B()
        .Raw(0x03).I16(-50).Raw(0x00)
        .Sized(0x06, q => q.I16(-4).U16(744).Raw(0x01, 0xFF, 0xFF, 0xFF, 0x00))
        .Raw(0x23).I16(-47).U16(634).I16(-47)
        .Raw(0x23).I16(-57).U16(634).I16(-57)
        .Raw(0x2E).I16(-161)
        .Raw(0x03).I16(-514).Raw(0x00)
        .Raw(rectangleContent.ToArray())
        .Raw(0x04));

    /// <summary>Wraps rectangle records in the Child content plus the
    /// Rectangle element start (the caller closes the rectangle).</summary>
    private static B RectangleChild(B rectContent) => new B()
        .Raw(0x2E).I16(-161)
        .Raw(0x03).I16(-514).Raw(0x00)
        .Raw(rectContent.ToArray());

    /// <summary>Builds a single-entry keyed shared style dictionary over the
    /// staged-like RepeatButton tables.</summary>
    private static byte[] KeyedStyleDictionary(string key, byte[] entry)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(7).I16(-108).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(25).I16(-226).Raw(0x00).Str("ActualHeight"))
            .Sized(0x1F, p => p.I16(26).I16(-226).Raw(0x00).Str("ActualWidth"))
            .Sized(0x20, p => p.U16(0).Str(key))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0));
        return head.Raw(entry).FinishDictionary().ToArray();
    }

    /// <summary>Builds a single-entry implicit-style dictionary with one
    /// short-form setter over an engine RepeatButton-compatible target.
    /// </summary>
    private static byte[] ImplicitShortFormDictionary(string fullName, short memberId, string text, short converter)
    {
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(memberId))
            .Sized(0x24, q => q.I16(2).Str(text).I16(converter))
            .Raw(0x04)
            .Raw(0x04).ToArray();
        return new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1D, p => p.U16(0).U16(0).Str(fullName))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(7).I16(-108).Raw(0x00).Str("TargetType"))
            .Raw(0x25).U32((uint)(content.Length + 10))
            .Raw(0x27).I16(0).Raw(0x00).U32(0).Raw(0x00, 0x00)
            .Raw(content)
            .Raw(0x04, 0x02).ToArray();
    }

    /// <summary>Builds a single-entry implicit-style dictionary over the
    /// engine RepeatButton TargetType (well-known -522, no TypeInfo row).
    /// </summary>
    private static byte[] ImplicitRepeatButtonDictionary(byte[] content) => new B().Raw(Header()).Raw(0x01)
        .Raw(0x03).I16(-524).Raw(0x00)
        .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
        .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
        .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
        .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
        .Sized(0x1F, p => p.I16(7).I16(-108).Raw(0x00).Str("TargetType"))
        .Raw(0x25).U32((uint)(content.Length + 10))
        .Raw(0x27).I16(-522).Raw(0x00).U32(0).Raw(0x00, 0x00)
        .Raw(content)
        .Raw(0x04, 0x02).ToArray();

    /// <summary>Builds a single-entry implicit-style dictionary with a
    /// Template setter holding a Border template visual over the given
    /// border content (the caller closes the visual).</summary>
    private static byte[] RepeatButtonBorderDictionary(B borderContent)
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-522)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(7).I16(-522)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(borderContent.ToArray())
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        return ImplicitRepeatButtonDictionary(entry);
    }

    private sealed class Slice127PlainStub
    {
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
}
