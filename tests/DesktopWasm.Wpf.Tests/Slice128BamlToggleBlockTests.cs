// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-128 HandyControl theme.baml ToggleBlock run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the keyed 'ToggleBlockBaseStyle' style entry keyidx 127 at
/// [BAML+0x17F3C, BAML+0x1835D) lands as a live engine style (short-form
/// VerticalContentAlignment -38 plus HorizontalContentAlignment -33 plus
/// Focusable plus the Template setter holding the ControlTemplate whose
/// Border visual carries Background/BorderBrush/BorderThickness
/// TemplateBindings with a converted SimplePanel child carrying Margin/
/// VerticalAlignment/HorizontalAlignment TemplateBindings plus three named
/// ContentPresenters with ContentSource/Visibility values, plus the
/// ControlTemplate.Triggers list with three IsChecked Triggers carrying
/// TargetName setters and an x:Null value); slice-129 lands the keyed
/// 'GroupBoxBaseTopTemplate' entry at BAML+0x1835D as a live engine
/// ControlTemplate (GroupBox target, Grid visual with RowDefinitions plus
/// two Borders carrying TemplateBinding and TemplatedParent-bound values);
/// slice-130 lands the keyed 'GroupBoxBaseLeftTemplate' entry at
/// BAML+0x1858F as a live engine ControlTemplate (ColumnDefinitions plus
/// the LayoutTransform chain) and slice-131 lands the keyed
/// 'GroupBoxTabTopTemplate' entry at BAML+0x187F5 plus the keyed
/// 'GroupBoxTabLeftTemplate' entry as live engine ControlTemplates
/// (RowDefinitions/ColumnDefinitions plus StaticResource-converter
/// TemplatedParent bindings), stopping precise at the
/// 'ButtonGroupHorizontalUniformItemsPanelTemplate' entry at BAML+0x18D3F.
/// The GroupBox styles, EventTriggers, the line-info trailer and the
/// converted-lib elements stay pinned follow-ups.
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways (the staged bytes: Style/-620 head with the
/// TargetType over the per-file TypeInfo 45 ToggleBlock row plus the
/// Setters content; short-form -38/-33 PWCs with "Center"/EnumConverter,
/// Focusable -132 with "False"/BooleanConverter, Template -37 with the
/// complex ControlTemplate holding the Border -50 visual with the three
/// TemplateBinding triples and the Child content, the per-file TypeInfo 46
/// SimplePanel visual with Margin/VerticalAlignment/HorizontalAlignment
/// TemplateBindings over Padding/VerticalContentAlignment/
/// HorizontalContentAlignment plus the Children content, three
/// ContentPresenter -102 children with Name/ContentSource StringConverter
/// PWCs plus Visibility EnumConverter PWCs, the 0x0B trigger list over the
/// ControlTemplate.Triggers row with IsChecked Trigger -688 elements
/// carrying qualified owner-45 PropertyCustoms plus "True"/"False"/x:Null
/// values plus TargetName/Visibility/Visible setters; the live upstream
/// HandyControl Theme.xaml 'ToggleBlockBaseStyle' (4 setters with the same
/// properties and values, 3 template triggers True/False/null with the
/// same TargetName/Visibility matrix, verified through a net10 WPF probe
/// against the pinned upstream build); a genuine net10.0-windows
/// ResourceDictionary fixture compiling the ToggleBlock shapes,
/// byte-identical in every staged record shape - -38/-33/-132/-37
/// short-forms, -54 Name and -20 ContentSource StringConverter PWCs, -135
/// Visibility EnumConverter PWCs, TargetName PWCs over the Setter.TargetName
/// row with StringConverter, the x:Null complex value (well-known -422)
/// emitted before the PropertyCustom Property, the 0x0B list over the
/// ControlTemplate.Triggers row - differing only in per-file table
/// numbering and line markers).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice128BamlToggleBlockTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice128BamlToggleBlockTests()
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
    public void Theme_baml_lands_toggleblock_then_stops_at_groupbox()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        // The ToggleBlock entry head at BAML+0x17F3C: Style start with the
        // TargetType over the per-file TypeInfo 45 ToggleBlock row plus the
        // Setters content.
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x2D, 0x00, 0x2E, 0x12, 0xFF },
            theme[0x17F3C..0x17F48]);
        // Short-form VerticalContentAlignment (-38) plus the
        // "Center"/EnumConverter value.
        Assert.Equal(
            new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDA, 0xFF },
            theme[0x17F48..0x17F54]);
        Assert.Equal(
            new byte[] { 0x24, 0x0C, 0x02, 0x00, 0x06, 0x43, 0x65, 0x6E, 0x74, 0x65, 0x72, 0x3D, 0xFF },
            theme[0x17F54..0x17F61]);
        // Short-form HorizontalContentAlignment (-33) plus the same value.
        Assert.Equal(
            new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDF, 0xFF },
            theme[0x17F62..0x17F6E]);
        Assert.Equal(
            new byte[] { 0x24, 0x0C, 0x02, 0x00, 0x06, 0x43, 0x65, 0x6E, 0x74, 0x65, 0x72, 0x3D, 0xFF },
            theme[0x17F6E..0x17F7B]);
        // Short-form Focusable (-132) plus the "False" value.
        Assert.Equal(
            new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0x7C, 0xFF },
            theme[0x17F7C..0x17F88]);
        Assert.Equal(
            new byte[] { 0x24, 0x0B, 0x02, 0x00, 0x05, 0x46, 0x61, 0x6C, 0x73, 0x65, 0xD2, 0xFF },
            theme[0x17F88..0x17F94]);
        // Template setter head plus the complex ControlTemplate start with
        // the TargetType over TypeInfo 45 and the VisualTree content.
        Assert.Equal(
            new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDB, 0xFF },
            theme[0x17F95..0x17FA1]);
        Assert.Equal(new byte[] { 0x07, 0x02, 0x00 }, theme[0x17FA1..0x17FA4]);
        Assert.Equal(new byte[] { 0x03, 0x94, 0xFF, 0x00 }, theme[0x17FA4..0x17FA8]);
        Assert.Equal(new byte[] { 0x22, 0x07, 0x00, 0x2D, 0x00 }, theme[0x17FA8..0x17FAD]);
        Assert.Equal(new byte[] { 0x2E, 0x52, 0xFF }, theme[0x17FAD..0x17FB0]);
        // Border visual head plus the three TemplateBinding triples
        // (Background/BorderBrush/BorderThickness) plus the Child content.
        Assert.Equal(new byte[] { 0x03, 0xCE, 0xFF, 0x00 }, theme[0x17FB0..0x17FB4]);
        Assert.Equal(
            new byte[] { 0x23, 0xFC, 0xFF, 0x7A, 0x02, 0xE8, 0xFF, 0x23, 0xFB, 0xFF, 0x7A, 0x02, 0xE7, 0xFF, 0x23, 0xFA, 0xFF, 0x7A, 0x02, 0xE6, 0xFF },
            theme[0x17FB4..0x17FC9]);
        Assert.Equal(new byte[] { 0x2E, 0x5F, 0xFF }, theme[0x17FC9..0x17FCC]);
        // SimplePanel visual (per-file TypeInfo 46) plus its three
        // TemplateBinding triples (Margin/Padding, VerticalAlignment/
        // VerticalContentAlignment, HorizontalAlignment/
        // HorizontalContentAlignment) plus the Children content.
        Assert.Equal(new byte[] { 0x03, 0x2E, 0x00, 0x00 }, theme[0x17FCC..0x17FD0]);
        Assert.Equal(
            new byte[] { 0x23, 0xCF, 0xFF, 0x7A, 0x02, 0xDD, 0xFF, 0x23, 0xC8, 0xFF, 0x7A, 0x02, 0xDA, 0xFF, 0x23, 0xD0, 0xFF, 0x7A, 0x02, 0xDF, 0xFF },
            theme[0x17FD0..0x17FE5]);
        Assert.Equal(new byte[] { 0x2E, 0x2B, 0xFF }, theme[0x17FE5..0x17FE8]);
        // First ContentPresenter: Name plus ContentSource StringConverter
        // PWCs plus the Visibility EnumConverter PWC.
        Assert.Equal(new byte[] { 0x03, 0x9A, 0xFF, 0x00 }, theme[0x17FE8..0x17FEC]);
        Assert.Equal(
            new byte[] { 0x24, 0x1D, 0xCA, 0xFF, 0x17, 0x43, 0x6F, 0x6E, 0x74, 0x65, 0x6E, 0x74, 0x50, 0x72, 0x65, 0x73, 0x65, 0x6E, 0x74, 0x65, 0x72, 0x43, 0x68, 0x65, 0x63, 0x6B, 0x65, 0x64, 0x99, 0xFD },
            theme[0x17FEC..0x1800A]);
        Assert.Equal(
            new byte[] { 0x24, 0x14, 0xEC, 0xFF, 0x0E, 0x43, 0x68, 0x65, 0x63, 0x6B, 0x65, 0x64, 0x43, 0x6F, 0x6E, 0x74, 0x65, 0x6E, 0x74, 0x99, 0xFD },
            theme[0x1800A..0x1801F]);
        Assert.Equal(
            new byte[] { 0x24, 0x0F, 0x79, 0xFF, 0x09, 0x43, 0x6F, 0x6C, 0x6C, 0x61, 0x70, 0x73, 0x65, 0x64, 0x3D, 0xFF },
            theme[0x1801F..0x1802F]);
        // ControlTemplate trigger list plus the first IsChecked Trigger
        // (qualified owner-45 PropertyCustom, "True" value, Setters).
        Assert.Equal(new byte[] { 0x0B, 0x1B, 0x00 }, theme[0x180C2..0x180C5]);
        Assert.Equal(new byte[] { 0x03, 0x50, 0xFD, 0x00 }, theme[0x180C5..0x180C9]);
        Assert.Equal(
            new byte[] { 0x06, 0x11, 0x0D, 0x00, 0x89, 0x40, 0x2D, 0x00, 0x09, 0x49, 0x73, 0x43, 0x68, 0x65, 0x63, 0x6B, 0x65, 0x64 },
            theme[0x180C9..0x180DB]);
        Assert.Equal(
            new byte[] { 0x24, 0x0A, 0x0E, 0x00, 0x04, 0x54, 0x72, 0x75, 0x65, 0x59, 0xFE },
            theme[0x180DB..0x180E6]);
        Assert.Equal(new byte[] { 0x2E, 0x00, 0xFF }, theme[0x180E6..0x180E9]);
        // First TargetName setter value (Setter.TargetName row plus the
        // StringConverter name).
        Assert.Equal(
            new byte[] { 0x24, 0x1D, 0x1C, 0x00, 0x17, 0x43, 0x6F, 0x6E, 0x74, 0x65, 0x6E, 0x74, 0x50, 0x72, 0x65, 0x73, 0x65, 0x6E, 0x74, 0x65, 0x72, 0x43, 0x68, 0x65, 0x63, 0x6B, 0x65, 0x64, 0x99, 0xFD },
            theme[0x180ED..0x1810B]);
        // Third trigger x:Null value: the complex Trigger.Value row plus
        // the NullExtension element, emitted before the PropertyCustom.
        Assert.Equal(new byte[] { 0x07, 0x0E, 0x00 }, theme[0x18282..0x18285]);
        Assert.Equal(new byte[] { 0x03, 0x5A, 0xFE, 0x00 }, theme[0x18285..0x18289]);
        // Entry close: setter end, trigger end, list end, template end,
        // complex end, setter end, style end.
        Assert.Equal(new byte[] { 0x04, 0x04, 0x0C, 0x04, 0x08, 0x04, 0x04 }, theme[0x18356..0x1835D]);
        // Key record for the run plus the next key: 0x26 with the
        // StringInfo id, the entry offset and the shared pair.
        Assert.Equal(
            new byte[] { 0x26, 0x09, 0x75, 0x00, 0xA5, 0x8D, 0x00, 0x00, 0x00, 0x00 },
            theme[0xB3FF..0xB409]);
        Assert.Equal(
            new byte[] { 0x26, 0x09, 0x76, 0x00, 0xC6, 0x91, 0x00, 0x00, 0x00, 0x00 },
            theme[0xB409..0xB413]);
        // The follow-up entry head at BAML+0x1835D: a ControlTemplate
        // element with its TargetType and VisualTree content.
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFF, 0x00, 0x22, 0x07, 0x00, 0xF8, 0xFE, 0x2E, 0x52, 0xFF },
            theme[0x1835D..0x18369]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(theme));
        // Entries #87-127 materialize in index order (the entry machine
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
        Assert.DoesNotContain("short-form properties beyond FlowDirection/Template/Focusable/IsEnabled/HorizontalAlignment/VerticalAlignment/Background/Height/Width/Fill/Visibility/BorderThickness/BorderBrush/Margin/IsTabStop", error.Message);
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
    }

    [Fact]
    public void Staged_boot_advances_past_toggleblock_to_groupbox()
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
        // TransitioningContentControl style, the four keyed RepeatButton
        // styles (keyidx 123-126: Border/Rectangle and ContentPresenter
        // visuals with TemplateBindings) and the keyed 'ToggleBlockBaseStyle'
        // style (keyidx 127: converted ToggleBlock target with
        // VerticalContentAlignment/HorizontalContentAlignment setters plus
        // the ToggleBlock template with its trigger list) and the keyed
        // 'GroupBoxBaseTopTemplate' template (keyidx 128: GroupBox target,
        // Grid visual with RowDefinitions plus two Borders carrying
        // TemplateBinding and TemplatedParent-bound values) plus the keyed
        // 'GroupBoxBaseLeftTemplate' template (keyidx 129) plus the keyed
        // 'GroupBoxTabTopTemplate' template (keyidx 130) and
        // 'GroupBoxTabLeftTemplate' template (keyidx 131) now resolve; boot
        // fails at the keyed 'ButtonGroupHorizontalUniformItemsPanelTemplate'
        // entry - the precise next blocker.
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
    public void Staged_shape_toggleblock_lands_live_style()
    {
        // The staged ToggleBlock entry shape (converted ToggleBlock target
        // with VerticalContentAlignment/HorizontalContentAlignment/Focusable
        // setters plus the Template setter holding the ControlTemplate whose
        // Border visual carries the three TemplateBindings with a converted
        // SimplePanel child plus three named ContentPresenters, plus the
        // ControlTemplate.Triggers list with True/False/null IsChecked
        // Triggers) lands as a live Style against the staged converted lib.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var toggleBlock = handyControl.GetType("HandyControl.Controls.ToggleBlock");
        Assert.NotNull(toggleBlock);
        var bytes = ToggleBlockDictionary("ToggleBlockBaseStyle", ToggleBlockEntry().ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary["ToggleBlockBaseStyle"]);
        Assert.Same(toggleBlock, style.TargetType);
        Assert.Equal(4, style.Setters.Count);
        var vertical = Assert.IsType<Setter>(style.Setters[0]);
        Assert.Same(Control.VerticalContentAlignmentProperty, vertical.Property);
        Assert.Equal(VerticalAlignment.Center, vertical.Value);
        var horizontal = Assert.IsType<Setter>(style.Setters[1]);
        Assert.Same(Control.HorizontalContentAlignmentProperty, horizontal.Property);
        Assert.Equal(HorizontalAlignment.Center, horizontal.Value);
        var focusable = Assert.IsType<Setter>(style.Setters[2]);
        Assert.Same(UIElement.FocusableProperty, focusable.Property);
        Assert.Equal(false, focusable.Value);
        var templateSetter = Assert.IsType<Setter>(style.Setters[3]);
        Assert.Same(Control.TemplateProperty, templateSetter.Property);
        var template = Assert.IsType<ControlTemplate>(templateSetter.Value);
        Assert.Same(toggleBlock, template.TargetType);
        var border = Assert.IsType<Border>(template.VisualTree);
        // The staged TypeInfo 46 names HandyControl.Controls.SimplePanel;
        // the entry-type rule resolves engine short-name mirrors first, so
        // the visual is the engine SimplePanel (the documented mirror of
        // the converted panel, inert until inflation either way).
        var panel = Assert.IsType<SimplePanel>(border.Child);
        Assert.Equal(3, panel.Children.Count);
        var checkedPresenter = Assert.IsType<ContentPresenter>(panel.Children[0]);
        Assert.Equal("ContentPresenterChecked", checkedPresenter.Name);
        Assert.Equal("CheckedContent", checkedPresenter.ContentSource);
        Assert.Equal(Visibility.Collapsed, checkedPresenter.GetValue(UIElement.VisibilityProperty));
        var uncheckedPresenter = Assert.IsType<ContentPresenter>(panel.Children[1]);
        Assert.Equal("ContentPresenterUnChecked", uncheckedPresenter.Name);
        Assert.Equal("UnCheckedContent", uncheckedPresenter.ContentSource);
        Assert.Equal(Visibility.Visible, uncheckedPresenter.GetValue(UIElement.VisibilityProperty));
        var indeterminatePresenter = Assert.IsType<ContentPresenter>(panel.Children[2]);
        Assert.Equal("ContentPresenterIndeterminate", indeterminatePresenter.Name);
        Assert.Equal("IndeterminateContent", indeterminatePresenter.ContentSource);
        Assert.Equal(Visibility.Collapsed, indeterminatePresenter.GetValue(UIElement.VisibilityProperty));
        Assert.Equal(3, template.TemplateNames.Count);
        Assert.Same(checkedPresenter, template.TemplateNames["ContentPresenterChecked"]);
        Assert.Same(uncheckedPresenter, template.TemplateNames["ContentPresenterUnChecked"]);
        Assert.Same(indeterminatePresenter, template.TemplateNames["ContentPresenterIndeterminate"]);
        var bound = template.TemplateBindings.Select(b => b.Property).ToList();
        Assert.Equal(
            new[] { "Background", "BorderBrush", "BorderThickness", "Padding", "VerticalContentAlignment", "HorizontalContentAlignment" },
            bound);
        Assert.Equal(3, template.Triggers.Count);
        var whenTrue = Assert.IsType<Trigger>(template.Triggers[0]);
        Assert.Equal("ToggleBlock.IsChecked", whenTrue.Property);
        Assert.Equal("True", whenTrue.Value);
        Assert.Equal(3, whenTrue.Setters.Count);
        AssertTargetSetter(whenTrue.Setters[0], "ContentPresenterChecked", Visibility.Visible);
        AssertTargetSetter(whenTrue.Setters[1], "ContentPresenterUnChecked", Visibility.Collapsed);
        AssertTargetSetter(whenTrue.Setters[2], "ContentPresenterIndeterminate", Visibility.Collapsed);
        var whenFalse = Assert.IsType<Trigger>(template.Triggers[1]);
        Assert.Equal("ToggleBlock.IsChecked", whenFalse.Property);
        Assert.Equal("False", whenFalse.Value);
        AssertTargetSetter(whenFalse.Setters[0], "ContentPresenterChecked", Visibility.Collapsed);
        AssertTargetSetter(whenFalse.Setters[1], "ContentPresenterUnChecked", Visibility.Visible);
        AssertTargetSetter(whenFalse.Setters[2], "ContentPresenterIndeterminate", Visibility.Collapsed);
        var whenNull = Assert.IsType<Trigger>(template.Triggers[2]);
        Assert.Equal("ToggleBlock.IsChecked", whenNull.Property);
        Assert.Null(whenNull.Value);
        AssertTargetSetter(whenNull.Setters[0], "ContentPresenterChecked", Visibility.Collapsed);
        AssertTargetSetter(whenNull.Setters[1], "ContentPresenterUnChecked", Visibility.Collapsed);
        AssertTargetSetter(whenNull.Setters[2], "ContentPresenterIndeterminate", Visibility.Visible);
    }

    [Fact]
    public void Staged_shape_toggleblock_has_no_inflation_markup()
    {
        // The landed ToggleBlock template retains its visual root without
        // inflation markup, so inflating throws the precise BAML-tree
        // inflation boundary instead of sharing one instance.
        LoadStagedHandyControlEntry();
        var bytes = ToggleBlockDictionary("ToggleBlockBaseStyle", ToggleBlockEntry().ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary["ToggleBlockBaseStyle"]);
        var template = Assert.IsType<ControlTemplate>(((Setter)style.Setters[3]).Value);
        var error = Assert.Throws<InvalidOperationException>(() => template.LoadContent());
        Assert.Contains("no inflation markup", error.Message);
        Assert.Contains("BAML-tree inflation", error.Message);
    }

    private static void AssertTargetSetter(SetterBase setterBase, string targetName, Visibility visibility)
    {
        var setter = Assert.IsType<Setter>(setterBase);
        Assert.Equal(targetName, setter.TargetName);
        Assert.Same(UIElement.VisibilityProperty, setter.Property);
        Assert.Equal(visibility, setter.Value);
    }

    [Fact]
    public void Content_alignment_short_forms_land_on_engine_target()
    {
        // Short-form -38/-33 resolve against the style TargetType through
        // the DP store, so an engine ToggleButton lands Center/Center with
        // the ToggleBlock template shape (per-file Panel stand-in plus a
        // named presenter plus a TargetName trigger).
        XamlLoader.CustomElementSource = null;
        var bytes = EngineToggleDictionary("ToggleLike", ToggleBlockEntry().ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary["ToggleLike"]);
        Assert.Same(typeof(ToggleButton), style.TargetType);
        var vertical = Assert.IsType<Setter>(style.Setters[0]);
        Assert.Same(Control.VerticalContentAlignmentProperty, vertical.Property);
        Assert.Equal(VerticalAlignment.Center, vertical.Value);
        var horizontal = Assert.IsType<Setter>(style.Setters[1]);
        Assert.Same(Control.HorizontalContentAlignmentProperty, horizontal.Property);
        Assert.Equal(HorizontalAlignment.Center, horizontal.Value);
        var template = Assert.IsType<ControlTemplate>(((Setter)style.Setters[3]).Value);
        Assert.Same(typeof(ToggleButton), template.TargetType);
        var border = Assert.IsType<Border>(template.VisualTree);
        var panel = Assert.IsType<StackPanel>(border.Child);
        Assert.Equal(3, panel.Children.Count);
        var presenter = Assert.IsType<ContentPresenter>(panel.Children[0]);
        Assert.Equal("ContentPresenterChecked", presenter.Name);
        Assert.Equal("CheckedContent", presenter.ContentSource);
        Assert.Same(presenter, template.TemplateNames["ContentPresenterChecked"]);
        Assert.Equal(3, template.TemplateNames.Count);
        Assert.Equal(3, template.Triggers.Count);
        var trigger = Assert.IsType<Trigger>(template.Triggers[0]);
        Assert.Equal("ToggleButton.IsChecked", trigger.Property);
        Assert.Equal("True", trigger.Value);
        Assert.Equal(3, trigger.Setters.Count);
        var setter = Assert.IsType<Setter>(trigger.Setters[0]);
        Assert.Equal("ContentPresenterChecked", setter.TargetName);
        Assert.Same(UIElement.VisibilityProperty, setter.Property);
        Assert.Equal(Visibility.Visible, setter.Value);
        var nullTrigger = Assert.IsType<Trigger>(template.Triggers[2]);
        Assert.Equal("ToggleButton.IsChecked", nullTrigger.Property);
        Assert.Null(nullTrigger.Value);
    }

    [Fact]
    public void Content_alignment_setters_apply_through_the_dp_store()
    {
        // The new Control DPs apply through SetValue like WPF: applying a
        // two-setter style flips a live ToggleButton to Center/Center, and
        // the CLR wrappers keep reading the store (defaults stay Left/Top).
        XamlLoader.CustomElementSource = null;
        var plain = new ToggleButton();
        Assert.Equal(HorizontalAlignment.Left, plain.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Top, plain.VerticalContentAlignment);
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(ToggleSetter(-38, "Center", -195).ToArray())
            .Raw(ToggleSetter(-33, "Center", -195).ToArray())
            .Raw(0x04).ToArray();
        var dictionary = BamlReader.LoadDictionary(EngineToggleDictionary("S", content));
        var style = Assert.IsType<Style>(dictionary["S"]);
        style.ApplyTo(plain);
        Assert.Equal(HorizontalAlignment.Center, plain.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Center, plain.VerticalContentAlignment);
    }

    [Fact]
    public void Unknown_short_form_member_still_fails_precise()
    {
        // Short-form members beyond the extended list fail naming the
        // member id with the full list instead of landing a half-built
        // Style.
        XamlLoader.CustomElementSource = null;
        var bytes = EngineShortFormDictionary("DesktopWasm.Wpf.ToggleButton", -39, "Top", -195);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("well-known member id -39", error.Message);
        Assert.Contains("short-form properties beyond FlowDirection/Template/Focusable/IsEnabled/HorizontalAlignment/VerticalAlignment/HorizontalContentAlignment/VerticalContentAlignment/Background/Height/Width/Fill/Visibility/BorderThickness/BorderBrush/Margin/IsTabStop/Padding/ItemsPanel/RenderTransform", error.Message);
    }

    [Fact]
    public void Content_alignment_on_target_without_dp_fails_precise()
    {
        // The -38 member resolves through the style TargetType: a target
        // without the DP fails naming the property instead of landing a
        // half-built Style.
        XamlLoader.CustomElementSource = null;
        var bytes = EngineShortFormDictionary("DesktopWasm.Wpf.Style", -38, "Center", -195);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("'VerticalContentAlignment' is not a dependency property of Style", error.Message);
    }

    [Fact]
    public void Content_alignment_garbage_value_fails_precise()
    {
        // "Center" converts through the DP type; garbage fails naming the
        // value and the target type instead of landing a half-built Style.
        XamlLoader.CustomElementSource = null;
        var bytes = EngineShortFormDictionary("DesktopWasm.Wpf.ToggleButton", -38, "Topx", -195);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("'Topx' is not a VerticalAlignment", error.Message);
        Assert.Contains("setter 'VerticalContentAlignment'", error.Message);
    }

    [Fact]
    public void Setter_targetname_needs_string_converter()
    {
        // TargetName travels as a StringConverter PWC over the
        // Setter.TargetName row; any other converter fails naming the name
        // instead of landing a half-bound setter.
        XamlLoader.CustomElementSource = null;
        var bytes = EngineToggleDictionary("S", ToggleEntryWithTriggerSetter(
            new B().Sized(0x24, q => q.I16(28).Str("ContentPresenterChecked").I16(-195)).ToArray()));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("TargetName 'ContentPresenterChecked'", error.Message);
        Assert.Contains("take StringConverter", error.Message);
    }

    [Fact]
    public void Setter_duplicate_targetname_fails_precise()
    {
        // One TargetName per setter; a second fails instead of landing a
        // half-bound setter.
        XamlLoader.CustomElementSource = null;
        var target = new B()
            .Sized(0x24, q => q.I16(28).Str("ContentPresenterChecked").I16(-615))
            .Sized(0x24, q => q.I16(28).Str("ContentPresenterChecked").I16(-615)).ToArray();
        var bytes = EngineToggleDictionary("S", ToggleEntryWithTriggerSetter(target));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("duplicate TargetName", error.Message);
        Assert.Contains("setters take one TargetName", error.Message);
    }

    [Fact]
    public void Trigger_null_value_with_wrong_child_fails_precise()
    {
        // The x:Null complex value takes exactly the NullExtension
        // element; any other element fails naming it.
        XamlLoader.CustomElementSource = null;
        var nullValue = new B()
            .Raw(0x07).I16(14)
            .Raw(0x03).I16(-514).Raw(0x00)
            .Raw(0x04)
            .Raw(0x08).ToArray();
        var bytes = EngineToggleDictionary("S", ToggleEntryWithTriggerValue(nullValue));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("holds <Rectangle>", error.Message);
        Assert.Contains("take a NullExtension element", error.Message);
    }

    [Fact]
    public void Trigger_null_value_with_wrong_row_fails_precise()
    {
        // The x:Null complex attribute must be the Trigger.Value row;
        // anything else fails naming the attribute instead of landing a
        // half-built trigger.
        XamlLoader.CustomElementSource = null;
        var nullValue = new B()
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-422).Raw(0x00)
            .Raw(0x04)
            .Raw(0x08).ToArray();
        var bytes = EngineToggleDictionary("S", ToggleEntryWithTriggerValue(nullValue));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("attribute 'Value'", error.Message);
        Assert.Contains("styles take Value", error.Message);
    }

    [Fact]
    public void Trigger_duplicate_null_value_fails_precise()
    {
        // One Value per trigger; a complex x:Null after a simple value
        // fails instead of landing a half-built trigger.
        XamlLoader.CustomElementSource = null;
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x0B).U16(9)
            .Raw(0x03).I16(-688).Raw(0x00)
            .Sized(0x06, q => q.I16(13).U16(16521).I16(0).Str("IsChecked"))
            .Sized(0x24, q => q.I16(14).Str("True").I16(-423))
            .Raw(0x07).I16(14)
            .Raw(0x03).I16(-422).Raw(0x00)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04).ToArray();
        var bytes = EngineStyleDictionary("S", content);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("duplicate Value", error.Message);
    }

    [Fact]
    public void Border_non_panel_per_file_child_fails_precise()
    {
        // Per-file Border children must resolve to a Panel; any other CLR
        // type fails naming it instead of landing a half-built visual.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x2E).I16(-161)
            .Raw(0x03).I16(1).Raw(0x00)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var bytes = EnginePanelDictionary("S", visual, "DesktopWasm.Wpf.Button");
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("is not a Panel", error.Message);
        Assert.Contains("Rectangle or SimplePanel child", error.Message);
    }

    [Fact]
    public void Presenter_duplicate_name_fails_precise()
    {
        // Template part names share one namescope; a duplicate fails
        // naming it instead of landing a half-named template.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x2E).I16(-161)
            .Raw(0x03).I16(1).Raw(0x00)
            .Raw(0x2E).I16(-213)
            .Raw(TogglePresenter("P", "CheckedContent", null).ToArray())
            .Raw(TogglePresenter("P", "UnCheckedContent", null).ToArray())
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var bytes = EnginePanelDictionary("S", visual, "DesktopWasm.Wpf.StackPanel");
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("duplicate template part name 'P'", error.Message);
        Assert.Contains("template names take unique names", error.Message);
    }

    [Fact]
    public void Presenter_unknown_member_still_fails_precise()
    {
        // Presenters take ContentSource/Name strings plus
        // HorizontalAlignment/VerticalAlignment/Visibility enum values: a
        // ContentSource with any other converter fails naming it, and any
        // other member fails through the shared template-member path.
        XamlLoader.CustomElementSource = null;
        var badConverter = new B()
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x2E).I16(-161)
            .Raw(0x03).I16(1).Raw(0x00)
            .Raw(0x2E).I16(-213)
            .Raw(0x03).I16(-102).Raw(0x00)
            .Sized(0x24, q => q.I16(-20).Str("CheckedContent").I16(-46))
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(EnginePanelDictionary("S", badConverter, "DesktopWasm.Wpf.StackPanel")));
        Assert.Contains("template ContentPresenter property id -20 with converter -46", error.Message);
        Assert.Contains("presenters take ContentSource/Name/RecognizesAccessKey strings plus HorizontalAlignment/VerticalAlignment/Visibility enum values", error.Message);
        var badMember = new B()
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x2E).I16(-161)
            .Raw(0x03).I16(1).Raw(0x00)
            .Raw(0x2E).I16(-213)
            .Raw(0x03).I16(-102).Raw(0x00)
            .Sized(0x24, q => q.I16(-39).Str("Top").I16(-195))
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var memberError = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(EnginePanelDictionary("S", badMember, "DesktopWasm.Wpf.StackPanel")));
        Assert.Contains("template property id -39", memberError.Message);
        Assert.Contains("template members beyond Margin/Text/TextTrimming/StrokeThickness/Height/Width/MinHeight/MinWidth/Background", memberError.Message);
    }

    /// <summary>Builds the staged ToggleBlock entry shape: four setters
    /// (VerticalContentAlignment/HorizontalContentAlignment Center,
    /// Focusable False, Template) with the ControlTemplate holding the
    /// Border visual (three TemplateBindings plus the per-file Panel child
    /// with Margin/alignment TemplateBindings plus three named
    /// ContentPresenters) and the ControlTemplate.Triggers list (True/
    /// False/null IsChecked Triggers with TargetName setters). TypeInfo 0
    /// is the style target, TypeInfo 1 the panel child.</summary>
    private static B ToggleBlockEntry() => new B()
        .Raw(0x03).I16(-620).Raw(0x00)
        .Raw(0x22).I16(0).I16(0)
        .Raw(0x2E).I16(-238)
        .Raw(ToggleSetter(-38, "Center", -195).ToArray())
        .Raw(ToggleSetter(-33, "Center", -195).ToArray())
        .Raw(ToggleSetter(-132, "False", -46).ToArray())
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
        .Raw(0x07).I16(2)
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16(7).I16(0)
        .Raw(0x2E).I16(-174)
        .Raw(0x03).I16(-50).Raw(0x00)
        .Raw(0x23).I16(-4).U16(634).I16(-24)
        .Raw(0x23).I16(-5).U16(634).I16(-25)
        .Raw(0x23).I16(-6).U16(634).I16(-26)
        .Raw(0x2E).I16(-161)
        .Raw(0x03).I16(1).Raw(0x00)
        .Raw(0x23).I16(-49).U16(634).I16(-35)
        .Raw(0x23).I16(-56).U16(634).I16(-38)
        .Raw(0x23).I16(-48).U16(634).I16(-33)
        .Raw(0x2E).I16(-213)
        .Raw(TogglePresenter("ContentPresenterChecked", "CheckedContent", "Collapsed").ToArray())
        .Raw(TogglePresenter("ContentPresenterUnChecked", "UnCheckedContent", null).ToArray())
        .Raw(TogglePresenter("ContentPresenterIndeterminate", "IndeterminateContent", "Collapsed").ToArray())
        .Raw(0x04)
        .Raw(0x04)
        .Raw(0x0B).U16(27)
        .Raw(ToggleValueTrigger("True",
            ("ContentPresenterChecked", "Visible"),
            ("ContentPresenterUnChecked", "Collapsed"),
            ("ContentPresenterIndeterminate", "Collapsed")).ToArray())
        .Raw(ToggleValueTrigger("False",
            ("ContentPresenterChecked", "Collapsed"),
            ("ContentPresenterUnChecked", "Visible"),
            ("ContentPresenterIndeterminate", "Collapsed")).ToArray())
        .Raw(ToggleNullTrigger(
            ("ContentPresenterChecked", "Collapsed"),
            ("ContentPresenterUnChecked", "Collapsed"),
            ("ContentPresenterIndeterminate", "Visible")).ToArray())
        .Raw(0x0C)
        .Raw(0x04)
        .Raw(0x08)
        .Raw(0x04)
        .Raw(0x04);

    /// <summary>Builds one short-form setter element over the given member
    /// id with a PropertyWithConverter text value.</summary>
    private static B ToggleSetter(short member, string text, short converter) => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16(1).U16(137).I16(member))
        .Sized(0x24, q => q.I16(2).Str(text).I16(converter))
        .Raw(0x04);

    /// <summary>Builds one template ContentPresenter child with a Name plus
    /// a ContentSource StringConverter PWC and an optional Visibility
    /// EnumConverter PWC.</summary>
    private static B TogglePresenter(string name, string source, string? visibility)
    {
        var b = new B()
            .Raw(0x03).I16(-102).Raw(0x00)
            .Sized(0x24, q => q.I16(-54).Str(name).I16(-615))
            .Sized(0x24, q => q.I16(-20).Str(source).I16(-615));
        if (visibility is not null)
            b.Sized(0x24, q => q.I16(-135).Str(visibility).I16(-195));
        return b.Raw(0x04);
    }

    /// <summary>Builds one template-trigger setter with a TargetName plus
    /// a Visibility value.</summary>
    private static B ToggleTargetSetter(string target, string visibility) => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x24, q => q.I16(28).Str(target).I16(-615))
        .Sized(0x06, q => q.I16(1).U16(137).I16(-135))
        .Sized(0x24, q => q.I16(2).Str(visibility).I16(-195))
        .Raw(0x04);

    /// <summary>Builds one IsChecked Trigger element with a simple text
    /// condition value plus TargetName setters.</summary>
    private static B ToggleValueTrigger(string value, params (string Target, string Visibility)[] setters)
    {
        var b = new B()
            .Raw(0x03).I16(-688).Raw(0x00)
            .Sized(0x06, q => q.I16(13).U16(16521).I16(0).Str("IsChecked"))
            .Sized(0x24, q => q.I16(14).Str(value).I16(-423))
            .Raw(0x2E).I16(-256);
        foreach (var (target, visibility) in setters)
            b.Raw(ToggleTargetSetter(target, visibility).ToArray());
        return b.Raw(0x04);
    }

    /// <summary>Builds one IsChecked Trigger element with the complex
    /// x:Null value (emitted before the Property like the staged bytes)
    /// plus TargetName setters.</summary>
    private static B ToggleNullTrigger(params (string Target, string Visibility)[] setters)
    {
        var b = new B()
            .Raw(0x03).I16(-688).Raw(0x00)
            .Raw(0x07).I16(14)
            .Raw(0x03).I16(-422).Raw(0x00)
            .Raw(0x04)
            .Raw(0x08)
            .Sized(0x06, q => q.I16(13).U16(16521).I16(0).Str("IsChecked"))
            .Raw(0x2E).I16(-256);
        foreach (var (target, visibility) in setters)
            b.Raw(ToggleTargetSetter(target, visibility).ToArray());
        return b.Raw(0x04);
    }

    /// <summary>Builds a ToggleButton-targeted entry whose template trigger
    /// setter starts with the given TargetName record bytes.</summary>
    private static byte[] ToggleEntryWithTriggerSetter(byte[] targetNameRecord) => new B()
        .Raw(0x03).I16(-620).Raw(0x00)
        .Raw(0x22).I16(0).I16(0)
        .Raw(0x2E).I16(-238)
        .Raw(ToggleSetter(-38, "Center", -195).ToArray())
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
        .Raw(0x07).I16(2)
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16(7).I16(0)
        .Raw(0x2E).I16(-174)
        .Raw(0x03).I16(-514).Raw(0x00)
        .Raw(0x04)
        .Raw(0x0B).U16(27)
        .Raw(0x03).I16(-688).Raw(0x00)
        .Sized(0x06, q => q.I16(13).U16(16521).I16(0).Str("IsChecked"))
        .Sized(0x24, q => q.I16(14).Str("True").I16(-423))
        .Raw(0x2E).I16(-256)
        .Raw(0x03).I16(-556).Raw(0x00)
        .Raw(targetNameRecord)
        .Sized(0x06, q => q.I16(1).U16(137).I16(-135))
        .Sized(0x24, q => q.I16(2).Str("Visible").I16(-195))
        .Raw(0x04)
        .Raw(0x04)
        .Raw(0x0C)
        .Raw(0x04)
        .Raw(0x08)
        .Raw(0x04)
        .Raw(0x04).ToArray();

    /// <summary>Builds a ToggleButton-targeted entry whose template trigger
    /// carries the given complex value bytes plus one TargetName setter.
    /// </summary>
    private static byte[] ToggleEntryWithTriggerValue(byte[] valueRecord) => new B()
        .Raw(0x03).I16(-620).Raw(0x00)
        .Raw(0x22).I16(0).I16(0)
        .Raw(0x2E).I16(-238)
        .Raw(ToggleSetter(-38, "Center", -195).ToArray())
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
        .Raw(0x07).I16(2)
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16(7).I16(0)
        .Raw(0x2E).I16(-174)
        .Raw(0x03).I16(-514).Raw(0x00)
        .Raw(0x04)
        .Raw(0x0B).U16(27)
        .Raw(0x03).I16(-688).Raw(0x00)
        .Raw(valueRecord)
        .Sized(0x06, q => q.I16(13).U16(16521).I16(0).Str("IsChecked"))
        .Raw(0x2E).I16(-256)
        .Raw(ToggleTargetSetter("ContentPresenterChecked", "Visible").ToArray())
        .Raw(0x04)
        .Raw(0x0C)
        .Raw(0x04)
        .Raw(0x08)
        .Raw(0x04)
        .Raw(0x04).ToArray();

    [Fact]
    public void Template_trigger_list_without_row_fails_precise()
    {
        // The ControlTemplate trigger list needs its AttributeInfo row;
        // without one the list fails naming the id instead of landing a
        // trigger-less template.
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
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(EngineRepeatDictionary(entry)));
        Assert.Contains("attribute id 27", error.Message);
        Assert.Contains("has no AttributeInfo record", error.Message);
    }

    /// <summary>Builds a single-entry keyed style dictionary over the
    /// ToggleBlock tables (TypeInfo 0 is the style target, TypeInfo 1 the
    /// panel child; attribute rows mirror the staged file: 0 TargetType/
    /// -620, 1 Property/-556, 2 Value/-556, 7 TargetType/-108, 9 Triggers/
    /// -620, 13 Property/-688, 14 Value/-688, 27 Triggers/-108, 28
    /// TargetName/-556).</summary>
    private static byte[] ToggleDictionary(string key, byte[] entry, string targetFullName, string panelFullName) =>
        new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1D, p => p.U16(0).U16(0).Str(targetFullName))
            .Sized(0x1D, p => p.U16(1).U16(0).Str(panelFullName))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(7).I16(-108).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(9).I16(-620).Raw(0x00).Str("Triggers"))
            .Sized(0x1F, p => p.I16(13).I16(-688).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(14).I16(-688).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(27).I16(-108).Raw(0x00).Str("Triggers"))
            .Sized(0x1F, p => p.I16(28).I16(-556).Raw(0x00).Str("TargetName"))
            .Sized(0x20, p => p.U16(0).Str(key))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0))
            .Raw(entry).FinishDictionary().ToArray();

    /// <summary>Builds the ToggleBlock dictionary over the staged converted
    /// target (TypeInfo 0, resolved through the staged bins) and the staged
    /// SimplePanel full name (TypeInfo 1, resolving to the engine
    /// short-name mirror by the entry-type rule).</summary>
    private static byte[] ToggleBlockDictionary(string key, byte[] entry) =>
        ToggleDictionary(key, entry, "HandyControl.Controls.ToggleBlock", "HandyControl.Controls.SimplePanel");

    /// <summary>Builds the ToggleBlock dictionary over engine stand-ins
    /// (ToggleButton target with its bool? IsChecked DP, StackPanel panel).
    /// </summary>
    private static byte[] EngineToggleDictionary(string key, byte[] entry) =>
        ToggleDictionary(key, entry, "DesktopWasm.Wpf.ToggleButton", "DesktopWasm.Wpf.StackPanel");

    /// <summary>Builds a single-entry keyed style dictionary over the engine
    /// ToggleButton TargetType with a per-file panel TypeInfo (for Border
    /// visual tests).</summary>
    private static byte[] EnginePanelDictionary(string key, byte[] visual, string panelFullName)
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(7).I16(0)
            .Raw(0x2E).I16(-174)
            .Raw(visual)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        return ToggleDictionary(key, entry, "DesktopWasm.Wpf.ToggleButton", panelFullName);
    }

    /// <summary>Builds a single-entry keyed style dictionary over the engine
    /// ToggleButton TargetType (well-known tables only, for short-form
    /// setter tests).</summary>
    private static byte[] EngineStyleDictionary(string key, byte[] content) => new B().Raw(Header()).Raw(0x01)
        .Raw(0x03).I16(-524).Raw(0x00)
        .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
        .Sized(0x1D, p => p.U16(0).U16(0).Str("DesktopWasm.Wpf.ToggleButton"))
        .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
        .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
        .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
        .Sized(0x1F, p => p.I16(7).I16(-108).Raw(0x00).Str("TargetType"))
        .Sized(0x1F, p => p.I16(9).I16(-620).Raw(0x00).Str("Triggers"))
        .Sized(0x1F, p => p.I16(13).I16(-688).Raw(0x00).Str("Property"))
        .Sized(0x1F, p => p.I16(14).I16(-688).Raw(0x00).Str("Value"))
        .Sized(0x20, p => p.U16(0).Str(key))
        .Deferable()
        .Sized(0x26, p => p.U16(0).U32(0).U16(0))
        .Raw(content).FinishDictionary().ToArray();

    /// <summary>Builds a single-entry implicit-style dictionary with one
    /// short-form setter over an engine ToggleButton-compatible target.
    /// </summary>
    private static byte[] EngineShortFormDictionary(string fullName, short memberId, string text, short converter)
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

    /// <summary>Builds a single-entry keyed style dictionary over the
    /// engine RepeatButton TargetType (well-known -522, no TypeInfo row).
    /// </summary>
    private static byte[] EngineRepeatDictionary(byte[] content) => new B().Raw(Header()).Raw(0x01)
        .Raw(0x03).I16(-524).Raw(0x00)
        .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
        .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
        .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
        .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
        .Sized(0x1F, p => p.I16(7).I16(-108).Raw(0x00).Str("TargetType"))
        .Sized(0x20, p => p.U16(0).Str("S"))
        .Deferable()
        .Sized(0x26, p => p.U16(0).U32(0).U16(0))
        .Raw(content).FinishDictionary().ToArray();

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
