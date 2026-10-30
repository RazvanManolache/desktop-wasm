// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-130 HandyControl theme.baml GroupBox LeftTemplate run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the keyed 'GroupBoxBaseLeftTemplate' template entry keyidx 129 at
/// [BAML+0x1858F, BAML+0x187F5) lands as a live engine ControlTemplate
/// (GroupBox target, Grid visual with two ColumnDefinitions plus two Borders
/// carrying TemplateBinding and TemplatedParent-bound values plus a
/// LayoutTransform/TransformGroup/RotateTransform chain on the header
/// presenter); slice-131 then landed the keyed 'GroupBoxTabTopTemplate'
/// entry at BAML+0x187F5 (0x38 StaticResource Converter values landing as
/// deferred converter keys plus ConverterParameter simples) and the keyed
/// 'GroupBoxTabLeftTemplate' entry, and slice-132 landed the four keyed
/// ButtonGroup ItemsPanelTemplate entries keyidx 132-135 at
/// [BAML+0x18D3F, BAML+0x18DB8) plus the converted
/// 'ButtonGroupItemStyleSelector' entry keyidx 136, advancing the stop past
/// the slice-133 'ProgressBarIndicatorAnimatedFill' brush (keyidx 137) and
/// slice-135 landed the 'VirtualizingStackHorizontal/VerticalItemsPanelTemplate'
/// entries (keyidx 143-144), and slice-136 landed the keyed 'WrapHorizontal/WrapVertical/DockItemsPanelTemplate' entries (keyidx 145-147) plus the generic 'ResizeGripperForeground' brush (keyidx 148) and 'SineEaseOut' easing (keyidx 149), and slice-137 landed the keyed 'BadgeDotTemplate' entry (keyidx 150) plus the 'ColorPickerSliderRepeatButtonBaseStyle' Style (keyidx 151), and slice-138 landed the keyed 'ColorPickerSliderThumbTemplate' entry (keyidx 152) with its Thumb TargetType and packed-White Border root, and slice-139 landed the keyed 'ColorPickerRainbowBrush' brush (keyidx 153) at BAML+0x192B2 with WPF-default points and a zero first offset, and slice-140 landed the keyed 'ColorPickerOpacityBrush' brush (keyidx 154) at BAML+0x19383 with its checker DrawingGroup, so the run stops precise at the keyed 'CoverFlowBaseStyle' Style (keyidx 155) at BAML+0x19497 on its template Border's Viewport3D child (well-known type id -729)
/// The GroupBox styles (BasedOn,
/// DynamicResource setters, TitleElement Triggers), EventTriggers, the
/// line-info trailer, the per-file (Type,Name) x:Name shape and the
/// converted-lib elements stay pinned follow-ups.
///
/// Byte-exact decode of the 614-byte entry against the staged 466350-byte
/// theme.baml, triangulated three ways (the staged bytes: 21 framing spans -
/// entry head, Grid with the ColumnDefinitions list plus Width/Auto and
/// empty columns, header Border with BorderThickness/BorderBrush
/// TemplateBindings plus ClipToBounds False plus the '0,0,6,0' Margin plus
/// MinWidth/Background/CornerRadius TemplatedParent complexes, header
/// presenter with Foreground/VerticalContentAlignment/
/// HorizontalContentAlignment TemplateBindings plus ContentSource/
/// RecognizesAccessKey/SnapsToDevicePixels plus RenderTransformOrigin
/// '0.5,0.5' plus the Padding Margin complex plus the LayoutTransform
/// TransformGroup/RotateTransform Angle '-90' chain, content Border with
/// Grid.Column '1' plus Background/BorderThickness/BorderBrush TemplateBindings
/// plus the CornerRadius complex, content presenter with Margin/Padding plus
/// SnapsToDevicePixels TemplateBindings, key records 0x26 string 119 at
/// 0xB413 plus string 120 at 0xB41D with shared=0/sharedSet=0, next
/// ControlTemplate head at 0x187F5 with the RowDefinitions list; the live
/// upstream HandyControl GroupBoxBaseStyle.xaml 'GroupBoxBaseLeftTemplate'
/// block - Grid with the same two ColumnDefinitions, the same header Border
/// with its Margin plus the same complexes, the same header presenter with
/// its TemplateBindings plus RenderTransformOrigin plus LayoutTransform
/// chain, the same Grid.Column=1 content Border with its presenter; a
/// genuine net10.0-windows ResourceDictionary fixture compiling the same
/// LeftTemplate shapes, byte-identical in every staged record shape -
/// -90 ColumnDefinition, -13 Width, -61 Grid.Column, -131 ClipToBounds,
/// -682 TransformGroup, -130 Children, -528 RotateTransform, -474
/// PointConverter, -53 MinWidth, -256 GridLengthConverter, -315 Int32Converter,
/// -46 BooleanConverter, -175 DoubleConverter - differing only in per-file
/// table numbering and line markers: the fixture carries 0x35/0x36 records
/// while every staged file ends in 0x00 with no line markers).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice130BamlLeftTemplateTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice130BamlLeftTemplateTests()
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
    public void Theme_baml_lands_groupbox_left_then_stops_at_tab_top()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        // The GroupBoxBaseLeftTemplate entry head at BAML+0x1858F: a
        // ControlTemplate element with its TargetType over well-known
        // GroupBox (-264) plus the VisualTree content.
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFF, 0x00, 0x22, 0x07, 0x00, 0xF8, 0xFE, 0x2E, 0x52, 0xFF },
            theme[0x1858F..0x1859B]);
        // Grid visual with the ColumnDefinitions list (per-file row 33).
        Assert.Equal(
            new byte[] { 0x03, 0x02, 0xFF, 0x00, 0x0B, 0x21, 0x00 },
            theme[0x1859B..0x185A2]);
        // First ColumnDefinition with the Width/Auto GridLengthConverter value.
        Assert.Equal(
            new byte[] { 0x03, 0xA6, 0xFF, 0x00, 0x24, 0x0A, 0xF3, 0xFF, 0x04, 0x41, 0x75, 0x74, 0x6F, 0x00, 0xFF },
            theme[0x185A2..0x185B1]);
        // Empty second ColumnDefinition, list end and Children content.
        Assert.Equal(
            new byte[] { 0x04, 0x03, 0xA6, 0xFF, 0x00, 0x04, 0x0C, 0x2E, 0x2B, 0xFF },
            theme[0x185B1..0x185BB]);
        // Header Border head with the BorderThickness TemplateBinding and the
        // ClipToBounds False value.
        Assert.Equal(
            new byte[] { 0x03, 0xCE, 0xFF, 0x00, 0x23, 0xFA, 0xFF, 0x7A, 0x02, 0xE6, 0xFF, 0x24, 0x0B, 0x7D, 0xFF, 0x05, 0x46, 0x61, 0x6C, 0x73, 0x65, 0xD2, 0xFF },
            theme[0x185BB..0x185D2]);
        // BorderBrush TemplateBinding plus the '0,0,6,0' Margin value.
        Assert.Equal(
            new byte[] { 0x23, 0xFB, 0xFF, 0x7A, 0x02, 0xE7, 0xFF, 0x24, 0x0D, 0xCF, 0xFF, 0x07, 0x30, 0x2C, 0x30, 0x2C, 0x36, 0x2C, 0x30, 0x71, 0xFD },
            theme[0x185D2..0x185E7]);
        // MinWidth complex Binding: (hc:TitleElement.MinWidth) Path plus
        // the TemplatedParent RelativeSource child.
        Assert.Equal(
            new byte[] { 0x07, 0xCB, 0xFF, 0x03, 0xEC, 0xFF, 0x00, 0x24, 0x20, 0x1E, 0x00, 0x1A, 0x28, 0x68, 0x63, 0x3A, 0x54, 0x69, 0x74, 0x6C, 0x65, 0x45, 0x6C, 0x65, 0x6D, 0x65, 0x6E, 0x74, 0x2E, 0x4D, 0x69, 0x6E, 0x57, 0x69, 0x64, 0x74, 0x68, 0x29, 0x14, 0xFE, 0x07, 0x0A, 0x00, 0x03, 0xFC, 0xFD, 0x00, 0x2A, 0x10, 0x11, 0x0F, 0x54, 0x65, 0x6D, 0x70, 0x6C, 0x61, 0x74, 0x65, 0x64, 0x50, 0x61, 0x72, 0x65, 0x6E, 0x74, 0x2B, 0x04, 0x08, 0x04, 0x08 },
            theme[0x185E7..0x1862E]);
        // Background complex Binding over (hc:TitleElement.Background).
        Assert.Equal(
            new byte[] { 0x07, 0xFC, 0xFF, 0x03, 0xEC, 0xFF, 0x00, 0x24, 0x22, 0x1E, 0x00, 0x1C, 0x28, 0x68, 0x63, 0x3A, 0x54, 0x69, 0x74, 0x6C, 0x65, 0x45, 0x6C, 0x65, 0x6D, 0x65, 0x6E, 0x74, 0x2E, 0x42, 0x61, 0x63, 0x6B, 0x67, 0x72, 0x6F, 0x75, 0x6E, 0x64, 0x29, 0x14, 0xFE, 0x07, 0x0A, 0x00, 0x03, 0xFC, 0xFD, 0x00, 0x2A, 0x10, 0x11, 0x0F, 0x54, 0x65, 0x6D, 0x70, 0x6C, 0x61, 0x74, 0x65, 0x64, 0x50, 0x61, 0x72, 0x65, 0x6E, 0x74, 0x2B, 0x04, 0x08, 0x04, 0x08 },
            theme[0x1862E..0x18677]);
        // CornerRadius complex Binding over (hc:BorderElement.CornerRadius)
        // plus the Border Child content member.
        Assert.Equal(
            new byte[] { 0x07, 0x1F, 0x00, 0x03, 0xEC, 0xFF, 0x00, 0x24, 0x25, 0x1E, 0x00, 0x1F, 0x28, 0x68, 0x63, 0x3A, 0x42, 0x6F, 0x72, 0x64, 0x65, 0x72, 0x45, 0x6C, 0x65, 0x6D, 0x65, 0x6E, 0x74, 0x2E, 0x43, 0x6F, 0x72, 0x6E, 0x65, 0x72, 0x52, 0x61, 0x64, 0x69, 0x75, 0x73, 0x29, 0x14, 0xFE, 0x07, 0x0A, 0x00, 0x03, 0xFC, 0xFD, 0x00, 0x2A, 0x10, 0x11, 0x0F, 0x54, 0x65, 0x6D, 0x70, 0x6C, 0x61, 0x74, 0x65, 0x64, 0x50, 0x61, 0x72, 0x65, 0x6E, 0x74, 0x2B, 0x04, 0x08, 0x04, 0x08, 0x2E, 0x5F, 0xFF },
            theme[0x18677..0x186C6]);
        // Border Child content plus the header presenter head with its
        // TextElement.Foreground/VerticalAlignment/HorizontalAlignment
        // TemplateBindings.
        Assert.Equal(
            new byte[] { 0x03, 0x9A, 0xFF, 0x00, 0x23, 0x87, 0xFF, 0x7A, 0x02, 0xE0, 0xFF, 0x23, 0xC8, 0xFF, 0x7A, 0x02, 0xDA, 0xFF, 0x23, 0xD0, 0xFF, 0x7A, 0x02, 0xDF, 0xFF },
            theme[0x186C6..0x186DF]);
        // ContentSource/RecognizesAccessKey simples plus the
        // SnapsToDevicePixels TemplateBinding over the per-file row.
        Assert.Equal(
            new byte[] { 0x24, 0x0C, 0xEC, 0xFF, 0x06, 0x48, 0x65, 0x61, 0x64, 0x65, 0x72, 0x99, 0xFD, 0x24, 0x0A, 0xE9, 0xFF, 0x04, 0x54, 0x72, 0x75, 0x65, 0xD2, 0xFF, 0x23, 0x20, 0x00, 0x7A, 0x02, 0x20, 0x00 },
            theme[0x186DF..0x186FE]);
        // RenderTransformOrigin '0.5,0.5' through the PointConverter.
        Assert.Equal(
            new byte[] { 0x24, 0x0D, 0x22, 0x00, 0x07, 0x30, 0x2E, 0x35, 0x2C, 0x30, 0x2E, 0x35, 0x26, 0xFE },
            theme[0x186FE..0x1870C]);
        // Margin complex Binding over (hc:TitleElement.Padding), without its
        // closes (the next span carries them).
        Assert.Equal(
            new byte[] { 0x07, 0xCF, 0xFF, 0x03, 0xEC, 0xFF, 0x00, 0x24, 0x1F, 0x1E, 0x00, 0x19, 0x28, 0x68, 0x63, 0x3A, 0x54, 0x69, 0x74, 0x6C, 0x65, 0x45, 0x6C, 0x65, 0x6D, 0x65, 0x6E, 0x74, 0x2E, 0x50, 0x61, 0x64, 0x64, 0x69, 0x6E, 0x67, 0x29, 0x14, 0xFE, 0x07, 0x0A, 0x00, 0x03, 0xFC, 0xFD, 0x00, 0x2A, 0x10, 0x11, 0x0F, 0x54, 0x65, 0x6D, 0x70, 0x6C, 0x61, 0x74, 0x65, 0x64, 0x50, 0x61, 0x72, 0x65, 0x6E, 0x74, 0x2B, 0x04, 0x08 },
            theme[0x1870C..0x18750]);
        // Margin closes plus the LayoutTransform complex with the
        // TransformGroup Children content, the RotateTransform with Angle
        // '-90', and the presenter/Border closes.
        Assert.Equal(
            new byte[] { 0x04, 0x08, 0x07, 0x23, 0x00, 0x03, 0x56, 0xFD, 0x00, 0x2E, 0x7E, 0xFF, 0x03, 0xF0, 0xFD, 0x00, 0x24, 0x09, 0x24, 0x00, 0x03, 0x2D, 0x39, 0x30, 0x51, 0xFF, 0x04, 0x04, 0x08, 0x04, 0x04 },
            theme[0x18750..0x1876F]);
        // Content Border head with Grid.Column '1' plus the Background and
        // BorderThickness TemplateBindings.
        Assert.Equal(
            new byte[] { 0x03, 0xCE, 0xFF, 0x00, 0x24, 0x07, 0xC3, 0xFF, 0x01, 0x31, 0xC5, 0xFE, 0x23, 0xFC, 0xFF, 0x7A, 0x02, 0xE8, 0xFF, 0x23, 0xFA, 0xFF, 0x7A, 0x02, 0xE6, 0xFF },
            theme[0x1876F..0x18789]);
        // BorderBrush TemplateBinding.
        Assert.Equal(
            new byte[] { 0x23, 0xFB, 0xFF, 0x7A, 0x02, 0xE7, 0xFF },
            theme[0x18789..0x18790]);
        // CornerRadius complex Binding on the content Border, without its
        // closes (the next span carries them).
        Assert.Equal(
            new byte[] { 0x07, 0x1F, 0x00, 0x03, 0xEC, 0xFF, 0x00, 0x24, 0x25, 0x1E, 0x00, 0x1F, 0x28, 0x68, 0x63, 0x3A, 0x42, 0x6F, 0x72, 0x64, 0x65, 0x72, 0x45, 0x6C, 0x65, 0x6D, 0x65, 0x6E, 0x74, 0x2E, 0x43, 0x6F, 0x72, 0x6E, 0x65, 0x72, 0x52, 0x61, 0x64, 0x69, 0x75, 0x73, 0x29, 0x14, 0xFE, 0x07, 0x0A, 0x00, 0x03, 0xFC, 0xFD, 0x00, 0x2A, 0x10, 0x11, 0x0F, 0x54, 0x65, 0x6D, 0x70, 0x6C, 0x61, 0x74, 0x65, 0x64, 0x50, 0x61, 0x72, 0x65, 0x6E, 0x74, 0x2B, 0x04 },
            theme[0x18790..0x187D9]);
        // Complex closes, content presenter (Margin/Padding plus
        // SnapsToDevicePixels) and the Border/Grid/template closes.
        Assert.Equal(
            new byte[] { 0x08, 0x04, 0x08, 0x2E, 0x5F, 0xFF, 0x03, 0x9A, 0xFF, 0x00, 0x23, 0xCF, 0xFF, 0x7A, 0x02, 0xDD, 0xFF, 0x23, 0x20, 0x00, 0x7A, 0x02, 0x20, 0x00, 0x04, 0x04, 0x04, 0x04 },
            theme[0x187D9..0x187F5]);
        // Key record for the run plus the next key: 0x26 with the StringInfo
        // id, the entry offset and the shared pair.
        Assert.Equal(
            new byte[] { 0x26, 0x09, 0x77, 0x00, 0xF8, 0x93, 0x00, 0x00, 0x00, 0x00 },
            theme[0xB413..0xB41D]);
        Assert.Equal(
            new byte[] { 0x26, 0x09, 0x78, 0x00, 0x5E, 0x96, 0x00, 0x00, 0x00, 0x00 },
            theme[0xB41D..0xB427]);
        // The follow-up entry head at BAML+0x187F5: a ControlTemplate
        // element with its TargetType and VisualTree content, then the Grid
        // with the RowDefinitions list the new stop pins.
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFF, 0x00, 0x22, 0x07, 0x00, 0xF8, 0xFE, 0x2E, 0x52, 0xFF },
            theme[0x187F5..0x18801]);
        Assert.Equal(
            new byte[] { 0x03, 0x02, 0xFF, 0x00, 0x0B, 0x1D, 0x00 },
            theme[0x18801..0x18808]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(theme));
        // Entries #87-129 materialize in index order (the entry machine
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
        Assert.DoesNotContain("holds ControlTemplate", error.Message);
        Assert.DoesNotContain("BAML+0x1859F", error.Message);
        Assert.DoesNotContain("GroupBoxBaseLeftTemplate", error.Message);
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
    public void Staged_boot_advances_past_button_group_panels_to_gradient_brush()
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
        // visuals with TemplateBindings), the keyed 'ToggleBlockBaseStyle'
        // style (keyidx 127: converted ToggleBlock target with
        // VerticalContentAlignment/HorizontalContentAlignment setters plus
        // the ToggleBlock template with its trigger list), the keyed
        // 'GroupBoxBaseTopTemplate' template (keyidx 128: GroupBox target,
        // Grid visual with RowDefinitions plus two Borders carrying
        // TemplateBinding and TemplatedParent-bound values) and the keyed
        // 'GroupBoxBaseLeftTemplate' template (keyidx 129: GroupBox target,
        // Grid visual with ColumnDefinitions plus two Borders carrying
        // TemplateBinding and TemplatedParent-bound values plus the
        // LayoutTransform chain) plus the keyed 'GroupBoxTabTopTemplate'
        // template (keyidx 130) and 'GroupBoxTabLeftTemplate' template
        // (keyidx 131: StaticResource-converter TemplatedParent bindings),
        // the four keyed ButtonGroup ItemsPanelTemplates (keyidx 132-135:
        // UniformGrid/StackPanel visuals) and the converted
        // 'ButtonGroupItemStyleSelector' entry (keyidx 136) and the keyed
        // 'ProgressBarIndicatorAnimatedFill' brush (keyidx 137) plus the
        // 'ColorPickerSliderThumbTemplate' entry (keyidx 152) now resolve;
        // the 'ColorPickerRainbowBrush' brush (keyidx 153) now resolves;
        // boot fails at the 'CoverFlowBaseStyle' Style (keyidx 155)
        // - the precise next blocker (template Border Viewport3D child well-known id -729 at BAML+0x19497).
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
        Assert.DoesNotContain("holds ControlTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x1859F", inner.Message);
        Assert.DoesNotContain("GroupBoxBaseLeftTemplate", inner.Message);
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
    public void Staged_shape_left_lands_live_template()
    {
        // The staged GroupBoxBaseLeftTemplate entry shape (GroupBox target,
        // Grid visual with two ColumnDefinitions plus two Borders carrying
        // TemplateBinding and TemplatedParent-bound values plus the
        // LayoutTransform chain) lands as a live engine ControlTemplate with
        // its visual root, namescope and deferred bindings. Per-file ids 0-8
        // stand in for the staged rows (TargetType/ColumnDefinitions/Path/
        // RelativeSource/CornerRadius/SnapsToDevicePixels/RenderTransformOrigin/
        // LayoutTransform/Angle); the staged switch asserts pin the real numbers.
        var bytes = LeftDictionary("GroupBoxBaseLeftTemplate", LeftEntry().ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ControlTemplate>(dictionary["GroupBoxBaseLeftTemplate"]);
        Assert.Same(typeof(GroupBox), template.TargetType);
        Assert.Empty(template.Triggers);
        Assert.Empty(template.TemplateNames);
        var grid = Assert.IsType<Grid>(template.VisualTree);
        Assert.Equal(2, grid.ColumnDefinitions.Count);
        Assert.Equal(GridLength.Auto, grid.ColumnDefinitions[0].Width);
        Assert.Equal(new GridLength(1, GridUnitType.Star), grid.ColumnDefinitions[1].Width);
        Assert.Empty(grid.RowDefinitions);
        Assert.Equal(2, grid.Children.Count);
        var header = Assert.IsType<Border>(grid.Children[0]);
        Assert.Equal(0, Grid.GetColumn(header));
        Assert.False(header.ClipToBounds);
        Assert.Equal(new Thickness(0, 0, 6, 0), header.Margin);
        AssertMinWidthBinding(header, "(hc:TitleElement.MinWidth)");
        AssertBackgroundBinding(header, "(hc:TitleElement.Background)");
        AssertCornerRadiusBinding(header, "(hc:BorderElement.CornerRadius)");
        var headerPresenter = Assert.IsType<ContentPresenter>(header.Child);
        Assert.Equal("Header", headerPresenter.ContentSource);
        Assert.True(headerPresenter.RecognizesAccessKey);
        Assert.Equal(new Point(0.5, 0.5), headerPresenter.RenderTransformOrigin);
        var layout = Assert.IsType<TransformGroup>(headerPresenter.LayoutTransform);
        var rotation = Assert.Single(layout.Children);
        var rotate = Assert.IsType<RotateTransform>(rotation);
        Assert.Equal(-90.0, rotate.Angle);
        AssertMarginBinding(headerPresenter, "(hc:TitleElement.Padding)");
        var content = Assert.IsType<Border>(grid.Children[1]);
        Assert.Equal(1, Grid.GetColumn(content));
        AssertBackgroundBinding(content, null);
        AssertCornerRadiusBinding(content, "(hc:BorderElement.CornerRadius)");
        var contentPresenter = Assert.IsType<ContentPresenter>(content.Child);
        AssertMarginBinding(contentPresenter, null, "Padding");
        var bound = template.TemplateBindings.Select(b => (b.Element, b.Property)).ToList();
        Assert.Equal(
            new (object, string)[]
            {
                (header, "BorderThickness"),
                (header, "BorderBrush"),
                (headerPresenter, "Foreground"),
                (headerPresenter, "VerticalContentAlignment"),
                (headerPresenter, "HorizontalContentAlignment"),
                (headerPresenter, "SnapsToDevicePixels"),
                (content, "Background"),
                (content, "BorderThickness"),
                (content, "BorderBrush"),
                (contentPresenter, "Padding"),
                (contentPresenter, "SnapsToDevicePixels"),
            },
            bound);
    }

    [Fact]
    public void Staged_shape_left_has_no_inflation_markup()
    {
        // The landed LeftTemplate retains its visual root without
        // inflation markup, so inflating throws the precise BAML-tree
        // inflation boundary instead of sharing one instance.
        var bytes = LeftDictionary("GroupBoxBaseLeftTemplate", LeftEntry().ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ControlTemplate>(dictionary["GroupBoxBaseLeftTemplate"]);
        var error = Assert.Throws<InvalidOperationException>(() => template.LoadContent());
        Assert.Contains("no inflation markup", error.Message);
        Assert.Contains("BAML-tree inflation", error.Message);
    }

    [Fact]
    public void Min_width_backs_onto_the_dependency_store()
    {
        // FrameworkElement.MinWidth is a real AffectsMeasure DP like WPF
        // (the staged header Border binds it), so template transfers land
        // visibly and plain sets keep the zero default.
        var border = new Border();
        Assert.Equal(0.0, border.MinWidth);
        Assert.False(border.HasLocalValue(FrameworkElement.MinWidthProperty));
        border.MinWidth = 24;
        Assert.Equal(24.0, border.MinWidth);
        Assert.True(border.HasLocalValue(FrameworkElement.MinWidthProperty));
        Assert.Same(FrameworkElement.MinWidthProperty, DependencyProperty.FromName(typeof(Border), "MinWidth"));
    }

    [Fact]
    public void Rows_and_columns_together_land()
    {
        // A template Grid may carry one RowDefinitions list plus one
        // ColumnDefinitions list (BAML emits them in XAML order); both land.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-254).Raw(0x00)
            .Raw(0x0B).U16(9)
            .Raw(LeftRow("Auto").ToArray())
            .Raw(0x0C)
            .Raw(0x0B).U16(1)
            .Raw(LeftColumn("Auto").ToArray())
            .Raw(0x0C)
            .Raw(0x2E).I16(-213)
            .Raw(LeftHeaderBorder().ToArray())
            .Raw(0x04).ToArray();
        var dictionary = BamlReader.LoadDictionary(LeftTemplateDictionary("T", visual));
        var template = Assert.IsType<ControlTemplate>(dictionary["T"]);
        var grid = Assert.IsType<Grid>(template.VisualTree);
        Assert.Single(grid.RowDefinitions);
        Assert.Equal(GridLength.Auto, grid.RowDefinitions[0].Height);
        Assert.Single(grid.ColumnDefinitions);
        Assert.Equal(GridLength.Auto, grid.ColumnDefinitions[0].Width);
        Assert.Single(grid.Children);
    }

    [Fact]
    public void Column_width_wrong_member_fails_precise()
    {
        // ColumnDefinitions take Width (-13); any other member fails naming it.
        XamlLoader.CustomElementSource = null;
        var column = new B()
            .Raw(0x03).I16(-90).Raw(0x00)
            .Sized(0x24, q => q.I16(-47).Str("Auto").I16(-256))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftColumnGrid(column))));
        Assert.Contains("template ColumnDefinition property id -47", error.Message);
        Assert.Contains("column definitions take Width", error.Message);
    }

    [Fact]
    public void Column_width_wrong_converter_fails_precise()
    {
        // Column widths convert through the GridLengthConverter (-256).
        XamlLoader.CustomElementSource = null;
        var column = new B()
            .Raw(0x03).I16(-90).Raw(0x00)
            .Sized(0x24, q => q.I16(-13).Str("Auto").I16(-46))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftColumnGrid(column))));
        Assert.Contains("template ColumnDefinition Width 'Auto'", error.Message);
        Assert.Contains("uses converter -46", error.Message);
    }

    [Fact]
    public void Column_width_garbage_fails_precise()
    {
        // A GridLengthConverter value that does not parse fails loud.
        XamlLoader.CustomElementSource = null;
        var column = new B()
            .Raw(0x03).I16(-90).Raw(0x00)
            .Sized(0x24, q => q.I16(-13).Str("Autoo").I16(-256))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftColumnGrid(column))));
        Assert.Contains("'Autoo' is not a GridLength for Width", error.Message);
    }

    [Fact]
    public void Columns_non_columndefinition_fails_precise()
    {
        // The ColumnDefinitions list takes ColumnDefinition children only.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-254).Raw(0x00)
            .Raw(0x0B).U16(1)
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", visual)));
        Assert.Contains("Border", error.Message);
        Assert.Contains("take ColumnDefinition children", error.Message);
    }

    [Fact]
    public void Grid_duplicate_row_lists_fail_precise()
    {
        // A template Grid takes one RowDefinitions list; a second fails.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-254).Raw(0x00)
            .Raw(0x0B).U16(9)
            .Raw(0x0B).U16(9)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", visual)));
        Assert.Contains("duplicate RowDefinitions list", error.Message);
    }

    [Fact]
    public void Grid_duplicate_column_lists_fail_precise()
    {
        // A template Grid takes one ColumnDefinitions list; a second fails.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-254).Raw(0x00)
            .Raw(0x0B).U16(1)
            .Raw(0x0B).U16(1)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", visual)));
        Assert.Contains("duplicate ColumnDefinitions list", error.Message);
    }

    [Fact]
    public void Grid_unclosed_columns_fail_precise()
    {
        // A ColumnDefinitions list left open at the Grid end fails instead
        // of landing columns that never closed.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-254).Raw(0x00)
            .Raw(0x0B).U16(1)
            .Raw(LeftColumn(null).ToArray())
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", visual)));
        Assert.Contains("unclosed ColumnDefinitions list", error.Message);
    }

    [Fact]
    public void Border_grid_column_wrong_converter_fails_precise()
    {
        // Grid.Column attaches through the Int32Converter (-315); any other
        // converter fails naming it.
        XamlLoader.CustomElementSource = null;
        var border = new B()
            .Raw(0x03).I16(-50).Raw(0x00)
            .Sized(0x24, q => q.I16(-61).Str("1").I16(-46))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(border))));
        Assert.Contains("template Border Grid.Column '1'", error.Message);
        Assert.Contains("uses converter -46", error.Message);
    }

    [Fact]
    public void Border_grid_column_garbage_fails_precise()
    {
        // A Grid.Column value that is not an integer fails loud.
        XamlLoader.CustomElementSource = null;
        var border = new B()
            .Raw(0x03).I16(-50).Raw(0x00)
            .Sized(0x24, q => q.I16(-61).Str("one").I16(-315))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(border))));
        Assert.Contains("template Border Grid.Column 'one'", error.Message);
        Assert.Contains("is not an integer", error.Message);
    }

    [Fact]
    public void Clip_to_bounds_wrong_converter_fails_precise()
    {
        // ClipToBounds converts through the BooleanConverter (-46).
        XamlLoader.CustomElementSource = null;
        var border = new B()
            .Raw(0x03).I16(-50).Raw(0x00)
            .Sized(0x24, q => q.I16(-131).Str("False").I16(-615))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(border))));
        Assert.Contains("template Border ClipToBounds 'False'", error.Message);
        Assert.Contains("uses converter -615", error.Message);
    }

    [Fact]
    public void Clip_to_bounds_garbage_fails_precise()
    {
        // A ClipToBounds value that is not a boolean fails loud.
        XamlLoader.CustomElementSource = null;
        var border = new B()
            .Raw(0x03).I16(-50).Raw(0x00)
            .Sized(0x24, q => q.I16(-131).Str("yes").I16(-46))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(border))));
        Assert.Contains("template Border ClipToBounds 'yes'", error.Message);
        Assert.Contains("is not a boolean", error.Message);
    }

    [Fact]
    public void Render_origin_wrong_converter_fails_precise()
    {
        // RenderTransformOrigin converts through the PointConverter (-474).
        XamlLoader.CustomElementSource = null;
        var presenter = new B()
            .Raw(0x03).I16(-102).Raw(0x00)
            .Sized(0x24, q => q.I16(6).Str("0.5,0.5").I16(-46))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(LeftBorderWith(presenter)))));
        Assert.Contains("template ContentPresenter RenderTransformOrigin '0.5,0.5'", error.Message);
        Assert.Contains("uses converter -46", error.Message);
    }

    [Fact]
    public void Render_origin_garbage_fails_precise()
    {
        // A RenderTransformOrigin value that is not a point fails loud.
        XamlLoader.CustomElementSource = null;
        var presenter = new B()
            .Raw(0x03).I16(-102).Raw(0x00)
            .Sized(0x24, q => q.I16(6).Str("half").I16(-474))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(LeftBorderWith(presenter)))));
        Assert.Contains("'half' is not a Point for RenderTransformOrigin", error.Message);
    }

    [Fact]
    public void Layout_transform_non_group_fails_precise()
    {
        // A LayoutTransform complex child takes a TransformGroup; a
        // RotateTransform directly names its type.
        XamlLoader.CustomElementSource = null;
        var presenter = new B()
            .Raw(0x03).I16(-102).Raw(0x00)
            .Raw(0x07).I16(7)
            .Raw(0x03).I16(-528).Raw(0x00)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(LeftBorderWith(presenter)))));
        Assert.Contains("holds <RotateTransform>", error.Message);
        Assert.Contains("complex children take a Binding, RelativeSource, TransformGroup or TemplateBinding", error.Message);
    }

    [Fact]
    public void Layout_transform_binding_on_margin_fails_precise()
    {
        // A TransformGroup child on a Margin complex property fails naming
        // the value type instead of landing a transform on a Thickness.
        XamlLoader.CustomElementSource = null;
        var presenter = new B()
            .Raw(0x03).I16(-102).Raw(0x00)
            .Raw(0x07).I16(-49)
            .Raw(0x03).I16(-682).Raw(0x00)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(LeftBorderWith(presenter)))));
        Assert.Contains("holds a TransformGroup", error.Message);
        Assert.Contains("template complex values take bindings", error.Message);
    }

    [Fact]
    public void Transform_group_wrong_content_fails_precise()
    {
        // A TransformGroup takes the Children content member only.
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(LeftBorderWith(LeftPresenterWith(LeftTransformGroup(0x2E, -213)))))));
        Assert.Contains("content member -213 on the template TransformGroup", error.Message);
        Assert.Contains("groups take the Children content", error.Message);
    }

    [Fact]
    public void Transform_group_duplicate_children_fails_precise()
    {
        XamlLoader.CustomElementSource = null;
        var group = new B()
            .Raw(0x03).I16(-682).Raw(0x00)
            .Raw(0x2E).I16(-130)
            .Raw(0x2E).I16(-130)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(LeftBorderWith(LeftPresenterWith(group.ToArray()))))));
        Assert.Contains("duplicate Children content on the template TransformGroup", error.Message);
    }

    [Fact]
    public void Transform_group_child_before_children_fails_precise()
    {
        XamlLoader.CustomElementSource = null;
        var group = new B()
            .Raw(0x03).I16(-682).Raw(0x00)
            .Raw(0x03).I16(-528).Raw(0x00)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(LeftBorderWith(LeftPresenterWith(group.ToArray()))))));
        Assert.Contains("rotation child precedes the Children content", error.Message);
    }

    [Fact]
    public void Transform_group_non_rotate_fails_precise()
    {
        // A TransformGroup takes RotateTransform children only.
        XamlLoader.CustomElementSource = null;
        var group = new B()
            .Raw(0x03).I16(-682).Raw(0x00)
            .Raw(0x2E).I16(-130)
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(LeftBorderWith(LeftPresenterWith(group.ToArray()))))));
        Assert.Contains("Border", error.Message);
        Assert.Contains("groups take RotateTransform children", error.Message);
    }

    [Fact]
    public void Rotate_wrong_attribute_fails_precise()
    {
        // Rotates take Angle through the per-file Angle row; any other
        // attribute fails naming its id.
        XamlLoader.CustomElementSource = null;
        var group = new B()
            .Raw(0x03).I16(-682).Raw(0x00)
            .Raw(0x2E).I16(-130)
            .Raw(0x03).I16(-528).Raw(0x00)
            .Sized(0x24, q => q.I16(-47).Str("-90").I16(-175))
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(LeftBorderWith(LeftPresenterWith(group.ToArray()))))));
        Assert.Contains("template RotateTransform property id -47", error.Message);
        Assert.Contains("rotates take Angle", error.Message);
    }

    [Fact]
    public void Rotate_wrong_converter_fails_precise()
    {
        // Angles convert through the DoubleConverter (-175).
        XamlLoader.CustomElementSource = null;
        var group = new B()
            .Raw(0x03).I16(-682).Raw(0x00)
            .Raw(0x2E).I16(-130)
            .Raw(0x03).I16(-528).Raw(0x00)
            .Sized(0x24, q => q.I16(8).Str("-90").I16(-46))
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(LeftBorderWith(LeftPresenterWith(group.ToArray()))))));
        Assert.Contains("template RotateTransform Angle '-90'", error.Message);
        Assert.Contains("uses converter -46", error.Message);
    }

    [Fact]
    public void Rotate_duplicate_angle_fails_precise()
    {
        XamlLoader.CustomElementSource = null;
        var group = new B()
            .Raw(0x03).I16(-682).Raw(0x00)
            .Raw(0x2E).I16(-130)
            .Raw(0x03).I16(-528).Raw(0x00)
            .Sized(0x24, q => q.I16(8).Str("-90").I16(-175))
            .Sized(0x24, q => q.I16(8).Str("-45").I16(-175))
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(LeftBorderWith(LeftPresenterWith(group.ToArray()))))));
        Assert.Contains("duplicate Angle on the template RotateTransform", error.Message);
    }

    [Fact]
    public void Rotate_garbage_angle_fails_precise()
    {
        // An Angle value that is not a double fails loud.
        XamlLoader.CustomElementSource = null;
        var group = new B()
            .Raw(0x03).I16(-682).Raw(0x00)
            .Raw(0x2E).I16(-130)
            .Raw(0x03).I16(-528).Raw(0x00)
            .Sized(0x24, q => q.I16(8).Str("left").I16(-175))
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(LeftTemplateDictionary("T", LeftGridWith(LeftBorderWith(LeftPresenterWith(group.ToArray()))))));
        Assert.Contains("'left' is not a Double for Angle", error.Message);
    }

    private static void AssertMinWidthBinding(Border border, string path)
    {
        var binding = BindingOperations.GetBinding(border, FrameworkElement.MinWidthProperty);
        Assert.NotNull(binding);
        Assert.Equal(path, binding!.Path.Path);
        Assert.Equal(RelativeSourceMode.TemplatedParent, binding.RelativeSource!.Mode);
    }

    private static void AssertBackgroundBinding(Border border, string? path)
    {
        var binding = BindingOperations.GetBinding(border, Control.BackgroundProperty);
        if (path is null)
        {
            Assert.Null(binding);
            return;
        }
        Assert.NotNull(binding);
        Assert.Equal(path, binding!.Path.Path);
        Assert.Equal(RelativeSourceMode.TemplatedParent, binding.RelativeSource!.Mode);
    }

    private static void AssertCornerRadiusBinding(Border border, string path)
    {
        var binding = BindingOperations.GetBinding(border, Border.CornerRadiusProperty);
        Assert.NotNull(binding);
        Assert.Equal(path, binding!.Path.Path);
        Assert.Equal(RelativeSourceMode.TemplatedParent, binding.RelativeSource!.Mode);
    }

    private static void AssertMarginBinding(ContentPresenter presenter, string? path, string? templateSource = null)
    {
        if (templateSource is not null)
        {
            var binding = BindingOperations.GetBinding(presenter, FrameworkElement.MarginProperty);
            Assert.Null(binding);
            return;
        }
        var transfer = BindingOperations.GetBinding(presenter, FrameworkElement.MarginProperty);
        Assert.NotNull(transfer);
        Assert.Equal(path, transfer!.Path.Path);
        Assert.Equal(RelativeSourceMode.TemplatedParent, transfer.RelativeSource!.Mode);
    }

    /// <summary>Builds the staged GroupBoxBaseLeftTemplate entry shape: a
    /// GroupBox-targeted ControlTemplate holding the Grid visual with two
    /// ColumnDefinitions plus two Borders carrying TemplateBinding and
    /// TemplatedParent-bound values plus the LayoutTransform chain. Per-file
    /// ids 0-8 stand in for the staged rows (TargetType/ColumnDefinitions/
    /// Path/RelativeSource/CornerRadius/SnapsToDevicePixels/
    /// RenderTransformOrigin/LayoutTransform/Angle).</summary>
    private static B LeftEntry() => new B()
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16(0).I16(-264)
        .Raw(0x2E).I16(-174)
        .Raw(0x03).I16(-254).Raw(0x00)
        .Raw(0x0B).U16(1)
        .Raw(LeftColumn("Auto").ToArray())
        .Raw(LeftColumn(null).ToArray())
        .Raw(0x0C)
        .Raw(0x2E).I16(-213)
        .Raw(LeftHeaderBorder().ToArray())
        .Raw(LeftContentBorder().ToArray())
        .Raw(0x04)
        .Raw(0x04);

    /// <summary>Builds one template ColumnDefinition with an optional Width
    /// simple (null keeps the star default).</summary>
    private static B LeftColumn(string? width)
    {
        var column = new B().Raw(0x03).I16(-90).Raw(0x00);
        if (width is not null)
            column.Sized(0x24, q => q.I16(-13).Str(width).I16(-256));
        return column.Raw(0x04);
    }

    /// <summary>Builds one template RowDefinition with an optional Height
    /// simple (null keeps the star default).</summary>
    private static B LeftRow(string? height)
    {
        var row = new B().Raw(0x03).I16(-541).Raw(0x00);
        if (height is not null)
            row.Sized(0x24, q => q.I16(-94).Str(height).I16(-256));
        return row.Raw(0x04);
    }

    /// <summary>Builds the staged header Border: BorderThickness/BorderBrush
    /// TemplateBindings, the ClipToBounds False simple, the '0,0,6,0' Margin
    /// simple, MinWidth/Background/CornerRadius TemplatedParent bindings and
    /// the header presenter.</summary>
    private static B LeftHeaderBorder() => new B()
        .Raw(0x03).I16(-50).Raw(0x00)
        .Raw(0x23).I16(-6).U16(634).I16(-26)
        .Sized(0x24, q => q.I16(-131).Str("False").I16(-46))
        .Raw(0x23).I16(-5).U16(634).I16(-25)
        .Sized(0x24, q => q.I16(-49).Str("0,0,6,0").I16(-655))
        .Raw(0x07).I16(-53)
        .Raw(LeftBinding("(hc:TitleElement.MinWidth)").ToArray())
        .Raw(0x08)
        .Raw(0x07).I16(-4)
        .Raw(LeftBinding("(hc:TitleElement.Background)").ToArray())
        .Raw(0x08)
        .Raw(0x07).I16(4)
        .Raw(LeftBinding("(hc:BorderElement.CornerRadius)").ToArray())
        .Raw(0x08)
        .Raw(0x2E).I16(-161)
        .Raw(LeftHeaderPresenter().ToArray())
        .Raw(0x04);

    /// <summary>Builds the staged content Border: Grid.Column '1',
    /// Background/BorderThickness/BorderBrush TemplateBindings, the
    /// CornerRadius TemplatedParent binding and the content presenter.</summary>
    private static B LeftContentBorder() => new B()
        .Raw(0x03).I16(-50).Raw(0x00)
        .Sized(0x24, q => q.I16(-61).Str("1").I16(-315))
        .Raw(0x23).I16(-4).U16(634).I16(-24)
        .Raw(0x23).I16(-6).U16(634).I16(-26)
        .Raw(0x23).I16(-5).U16(634).I16(-25)
        .Raw(0x07).I16(4)
        .Raw(LeftBinding("(hc:BorderElement.CornerRadius)").ToArray())
        .Raw(0x08)
        .Raw(0x2E).I16(-161)
        .Raw(LeftContentPresenter().ToArray())
        .Raw(0x04);

    /// <summary>Builds one template Binding with a Path PWC plus the
    /// TemplatedParent RelativeSource child.</summary>
    private static B LeftBinding(string path) => new B()
        .Raw(0x03).I16(-20).Raw(0x00)
        .Sized(0x24, q => q.I16(2).Str(path).I16(-492))
        .Raw(0x07).I16(3)
        .Raw(0x03).I16(-516).Raw(0x00)
        .Raw(0x2A)
        .Sized(0x10, q => q.Str("TemplatedParent"))
        .Raw(0x2B)
        .Raw(0x04)
        .Raw(0x08)
        .Raw(0x04);

    /// <summary>Builds the staged header presenter (Foreground/
    /// VerticalContentAlignment/HorizontalContentAlignment/
    /// SnapsToDevicePixels TemplateBindings, ContentSource/
    /// RecognizesAccessKey/RenderTransformOrigin simples, the Padding Margin
    /// binding and the LayoutTransform chain).</summary>
    private static B LeftHeaderPresenter() => new B()
        .Raw(0x03).I16(-102).Raw(0x00)
        .Raw(0x23).I16(-121).U16(634).I16(-32)
        .Raw(0x23).I16(-56).U16(634).I16(-38)
        .Raw(0x23).I16(-48).U16(634).I16(-33)
        .Sized(0x24, q => q.I16(-20).Str("Header").I16(-615))
        .Sized(0x24, q => q.I16(-23).Str("True").I16(-46))
        .Raw(0x23).I16(5).U16(634).I16(5)
        .Sized(0x24, q => q.I16(6).Str("0.5,0.5").I16(-474))
        .Raw(0x07).I16(-49)
        .Raw(LeftBinding("(hc:TitleElement.Padding)").ToArray())
        .Raw(0x08)
        .Raw(0x07).I16(7)
        .Raw(0x03).I16(-682).Raw(0x00)
        .Raw(0x2E).I16(-130)
        .Raw(0x03).I16(-528).Raw(0x00)
        .Sized(0x24, q => q.I16(8).Str("-90").I16(-175))
        .Raw(0x04)
        .Raw(0x04)
        .Raw(0x08)
        .Raw(0x04);

    /// <summary>Builds the staged content presenter (Margin/Padding plus
    /// SnapsToDevicePixels TemplateBindings).</summary>
    private static B LeftContentPresenter() => new B()
        .Raw(0x03).I16(-102).Raw(0x00)
        .Raw(0x23).I16(-49).U16(634).I16(-35)
        .Raw(0x23).I16(5).U16(634).I16(5)
        .Raw(0x04);

    /// <summary>Wraps one template ColumnDefinition in a Grid with its
    /// ColumnDefinitions list (for the column dispatch tests).</summary>
    private static byte[] LeftColumnGrid(byte[] column) => new B()
        .Raw(0x03).I16(-254).Raw(0x00)
        .Raw(0x0B).U16(1)
        .Raw(column)
        .Raw(0x0C)
        .Raw(0x2E).I16(-213)
        .Raw(0x04).ToArray();

    /// <summary>Wraps one template Border in a Grid with its Children
    /// content (for the border dispatch tests).</summary>
    private static byte[] LeftGridWith(byte[] border) => new B()
        .Raw(0x03).I16(-254).Raw(0x00)
        .Raw(0x2E).I16(-213)
        .Raw(border)
        .Raw(0x04).ToArray();

    /// <summary>Wraps one template ContentPresenter in a Border with its
    /// Child content (for the presenter dispatch tests).</summary>
    private static byte[] LeftBorderWith(byte[] presenter) => new B()
        .Raw(0x03).I16(-50).Raw(0x00)
        .Raw(0x2E).I16(-161)
        .Raw(presenter)
        .Raw(0x04).ToArray();

    /// <summary>Wraps one template TransformGroup in a ContentPresenter
    /// LayoutTransform complex (for the transform dispatch tests).</summary>
    private static byte[] LeftPresenterWith(byte[] group) => new B()
        .Raw(0x03).I16(-102).Raw(0x00)
        .Raw(0x07).I16(7)
        .Raw(group)
        .Raw(0x08)
        .Raw(0x04).ToArray();

    /// <summary>Builds one TransformGroup element over a single content
    /// record and member (for the content-member dispatch test).</summary>
    private static byte[] LeftTransformGroup(byte record, short member) => new B()
        .Raw(0x03).I16(-682).Raw(0x00)
        .Raw(record).I16(member)
        .Raw(0x03).I16(-528).Raw(0x00)
        .Sized(0x24, q => q.I16(8).Str("-90").I16(-175))
        .Raw(0x04)
        .Raw(0x04).ToArray();

    /// <summary>Wraps one template visual in a keyed GroupBox-targeted
    /// ControlTemplate entry body (for the grid/column/child dispatch
    /// tests).</summary>
    private static byte[] LeftTemplateBody(byte[] visual) => new B()
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16(0).I16(-264)
        .Raw(0x2E).I16(-174)
        .Raw(visual)
        .Raw(0x04).ToArray();

    /// <summary>Builds a single-entry keyed template dictionary over one
    /// visual (for the grid/column/child dispatch tests).</summary>
    private static byte[] LeftTemplateDictionary(string key, byte[] visual, bool shared = true) =>
        LeftShapeDictionary(key, LeftTemplateBody(visual), shared);

    /// <summary>Builds a single-entry keyed template dictionary over the
    /// engine GroupBox TargetType (well-known tables only, no TypeInfo rows;
    /// per-file AttributeInfo ids 0-9 stand in for the staged rows).</summary>
    private static byte[] LeftDictionary(string key, byte[] entry) =>
        LeftShapeDictionary(key, entry, shared: true);

    private static B LeftKeyPair(B p, bool shared)
    {
        p.U16(0).U32(0);
        return shared ? p.U16(0) : p.Raw(0x00, 0x01);
    }

    private static byte[] LeftShapeDictionary(string key, byte[] entry, bool shared) => new B().Raw(Header()).Raw(0x01)
        .Raw(0x03).I16(-524).Raw(0x00)
        .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
        .Sized(0x1F, p => p.I16(0).I16(-108).Raw(0x00).Str("TargetType"))
        .Sized(0x1F, p => p.I16(1).I16(-254).Raw(0x00).Str("ColumnDefinitions"))
        .Sized(0x1F, p => p.I16(2).I16(-20).Raw(0x00).Str("Path"))
        .Sized(0x1F, p => p.I16(3).I16(-20).Raw(0x00).Str("RelativeSource"))
        .Sized(0x1F, p => p.I16(4).I16(-50).Raw(0x00).Str("CornerRadius"))
        .Sized(0x1F, p => p.I16(5).I16(-694).Raw(0x00).Str("SnapsToDevicePixels"))
        .Sized(0x1F, p => p.I16(6).I16(-694).Raw(0x00).Str("RenderTransformOrigin"))
        .Sized(0x1F, p => p.I16(7).I16(-226).Raw(0x00).Str("LayoutTransform"))
        .Sized(0x1F, p => p.I16(8).I16(-528).Raw(0x00).Str("Angle"))
        .Sized(0x1F, p => p.I16(9).I16(-254).Raw(0x00).Str("RowDefinitions"))
        .Sized(0x20, p => p.U16(0).Str(key))
        .Deferable()
        .Sized(0x26, p => LeftKeyPair(p, shared))
        .Raw(entry).FinishDictionary().ToArray();

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
