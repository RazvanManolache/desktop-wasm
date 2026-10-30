// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-108 HandyControl theme brushes + BrushDemo/LabelDemo views.
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (absent System.Security.Permissions + System.Windows.Extensions,
/// both with no browser path; Convert map untouched, staged bins
/// byte-identical by hash, manifests untouched).
///
/// Advance, all reproduced against live staged bins via the engine-native
/// staged-bin boot oracle (status gate bypassed in-probe only) then pinned
/// by the new staged-oracle set: the remaining default-skin theme brushes
/// (RegionBrush et al.) resolve at application scope with the real upstream
/// values (live-verified against the real WPF theme: a net10.0-windows
/// probe merged the upstream HandyControl Themes/SkinDefault.xaml +
/// Themes/Theme.xaml), so the staged BrushDemo view (27 theme rows behind
/// 5 lib Label* StaticResources) and the staged LabelDemo view (12 rows
/// behind 5 lib Label* plus 6 lib Label*.Small StaticResources) instantiate
/// and paint real translated ink: BrushDemo 28/28 content texts with 23
/// rows carrying the exact upstream Background/Foreground brushes,
/// LabelDemo 12/12 rows carrying the 10-fold repeated Title text.
///
/// Supporting real-behavior fixes, never fabricated: element-attribute
/// DynamicResource/StaticResource resolution coerces the theme-dictionary
/// Color/uint store to real brushes on brush targets (the
/// Setter.CoerceResourceValue rule; genuine mismatches still skip and
/// non-brush targets keep the stringify path), so {DynamicResource
/// DarkPrimaryBrush} lands SolidColorBrush #FF326CF3 on Background instead
/// of staying unset; the DynamicResourceExtension provision path carries
/// the same rule. The 11 Label* styles land as empty chrome-less shims
/// (TargetType Label, the slice-100 idiom): styled rows keep default label
/// chrome (no lib setter content is replicated - an honest boundary).
/// Remaining blockers: the unchanged line-17 SkinDefault BAML-only stop
/// (entry boot), PreviewSliderDemo's Label ContentStringFormat (line 10 -
/// re-pinned to instantiation by slice-109), non-WrapPanel ItemsPanel hosts,
/// and the 2 absent binaries.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice108HandyBrushesTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Theme_brushes_carry_live_wpf_default_skin_values()
    {
        Application.Current = new Application();
        foreach (var (key, a, r, g, b) in new (string, byte, byte, byte, byte)[]
        {
            ("RegionBrush", 0xFF, 0xFF, 0xFF, 0xFF),
            ("SecondaryRegionBrush", 0xFF, 0xEE, 0xEE, 0xEE),
            ("ThirdlyRegionBrush", 0xFF, 0xFF, 0xFF, 0xFF),
            ("BackgroundBrush", 0xFF, 0xEE, 0xEE, 0xEE),
            ("BorderBrush", 0xFF, 0xE0, 0xE0, 0xE0),
            ("SecondaryBorderBrush", 0xFF, 0x75, 0x75, 0x75),
            ("DefaultBrush", 0xFF, 0xFF, 0xFF, 0xFF),
            ("PrimaryTextBrush", 0xFF, 0x21, 0x21, 0x21),
            ("SecondaryTextBrush", 0xFF, 0x75, 0x75, 0x75),
            ("ThirdlyTextBrush", 0xFF, 0xBD, 0xBD, 0xBD),
            ("ReverseTextBrush", 0xFF, 0x21, 0x21, 0x21),
            ("TextIconBrush", 0xFF, 0xFF, 0xFF, 0xFF),
            ("AccentBrush", 0xFF, 0xF8, 0x49, 0x1E),
            ("DarkAccentBrush", 0xFF, 0xF8, 0x49, 0x1E),
            ("DarkPrimaryBrush", 0xFF, 0x32, 0x6C, 0xF3),
            ("DarkSuccessBrush", 0xFF, 0x2D, 0xB8, 0x4D),
            ("DarkInfoBrush", 0xFF, 0x00, 0xBC, 0xD4),
            ("DarkDangerBrush", 0xFF, 0xDB, 0x33, 0x40),
            ("DarkWarningBrush", 0xFF, 0xE9, 0xAF, 0x20),
            ("DarkDefaultBrush", 0xFF, 0xF5, 0xF5, 0xF5),
            ("DarkMaskBrush", 0x20, 0x00, 0x00, 0x00),
            ("DarkOpacityBrush", 0x40, 0x00, 0x00, 0x00),
        })
        {
            Assert.True(Application.Current.TryFindResource(key, out var value), key);
            Assert.Equal(Color.FromArgb(a, r, g, b), Assert.IsType<Color>(value));
        }
        foreach (var (key, a, r, g, b) in new (string, byte, byte, byte, byte)[]
        {
            ("TitleBrush", 0xFF, 0x32, 0x6C, 0xF3),
            ("SuccessBrush", 0xFF, 0x2D, 0xB8, 0x4D),
            ("InfoBrush", 0xFF, 0x00, 0xBC, 0xD4),
            ("DangerBrush", 0xFF, 0xDB, 0x33, 0x40),
            ("WarningBrush", 0xFF, 0xE9, 0xAF, 0x20),
        })
        {
            Assert.True(Application.Current.TryFindResource(key, out var value), key);
            var gradient = Assert.IsType<LinearGradientBrush>(value);
            Assert.Equal(new Point(0, 0), gradient.StartPoint);
            Assert.Equal(new Point(1, 0), gradient.EndPoint);
            Assert.Equal(2, gradient.GradientStops.Count);
            Assert.Equal(Color.FromArgb(a, r, g, b), gradient.GradientStops[0].Color);
            Assert.Equal(0, gradient.GradientStops[0].Offset);
            Assert.Equal(Color.FromArgb(a, r, g, b), gradient.GradientStops[1].Color);
            Assert.Equal(1, gradient.GradientStops[1].Offset);
        }
    }

    [Fact]
    public void Label_row_styles_resolve_as_empty_targeted_shims()
    {
        Application.Current = new Application();
        foreach (var key in new[]
        {
            "LabelPrimary", "LabelDanger", "LabelWarning", "LabelInfo", "LabelSuccess",
            "LabelDefault.Small", "LabelDanger.Small", "LabelWarning.Small",
            "LabelInfo.Small", "LabelPrimary.Small", "LabelSuccess.Small",
        })
        {
            Assert.True(Application.Current.TryFindResource(key, out var value), key);
            var style = Assert.IsType<Style>(value);
            Assert.Same(typeof(Label), style.TargetType);
            Assert.Empty(style.Setters);
        }
        Assert.False(Application.Current.TryFindResource("LabelNoSuch", out _));
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
        foreach (var name in new[]
        {
            "RegionBrush", "SecondaryRegionBrush", "ThirdlyRegionBrush", "BackgroundBrush",
            "BorderBrush", "SecondaryBorderBrush", "TitleBrush", "DefaultBrush",
            "PrimaryTextBrush", "SecondaryTextBrush", "ThirdlyTextBrush",
            "ReverseTextBrush", "TextIconBrush", "SuccessBrush", "InfoBrush",
            "DangerBrush", "WarningBrush", "AccentBrush", "DarkPrimaryBrush",
            "LabelPrimary", "LabelDanger", "LabelWarning", "LabelInfo", "LabelSuccess",
            "LabelDefault.Small", "ContentStringFormat",
        })
            Assert.DoesNotContain(details, d => d.Contains(name));
        _ = entry;
    }

    [Fact]
    public void Staged_entry_boot_advances_past_skin_default_to_theme_stop()
    {
        // Slice-120 re-pin: entries #102 (implicit Col style, short-form
        // Focusable setter) and #103 (implicit ContentControl style, same
        // shape) now resolve too; boot advances past FloatingBlock (#105) and
        // GridSplitter (#106) past Notification (#107) past the SnapRect4ScreenshotStyle Trigger list and the TargetArea4ScreenshotStyle entry past the implicit SimpleItemsControl/Sprite styles, the nine transition Storyboards (keyidx 113-121) and the implicit TransitioningContentControl style through the keyed RepeatButton styles (keyidx 123-126) through the keyed 'ToggleBlockBaseStyle' style (keyidx 127) to the 'GroupBoxBaseTopTemplate' entry at BAML+0x1835D
        // (see Slice122BamlAlignmentTests).
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("startup failed in entry point", error.Message);
        var inner = Assert.IsType<XamlLoadException>(error.InnerException);
        Assert.DoesNotContain("SkinDefault", inner.Message);
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
        Assert.Contains("HandyControl;component/Themes/Theme.xaml", inner.Message);
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
        Assert.DoesNotContain("holds a Trigger list", inner.Message);
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

        Assert.DoesNotContain("well-known type id -608", inner.Message);
        Assert.DoesNotContain("well-known type id -522", inner.Message);
        Assert.DoesNotContain("BAML+0x1744C", inner.Message);
        Assert.DoesNotContain("element Border on the template", inner.Message);
        Assert.DoesNotContain("SimpleItemsControl", inner.Message);
        Assert.DoesNotContain("BAML+0x17483", inner.Message);
        Assert.DoesNotContain("Sprite", inner.Message);
        Assert.DoesNotContain("IsDragElement", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimpleItemsControl", inner.Message);
    }

    [Fact]
    public void Staged_brush_demo_instantiates_with_28_rows()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.BrushDemo");
        var labels = Descendants(view).OfType<Label>().ToList();
        Assert.Equal(28, labels.Count);
        var frame = WasmMilCore.RenderHeadless(view, new WpfSize(800, 600));
        var texts = frame.OfType<DrawCommand.DrawText>().Select(d => d.Text).ToList();
        Assert.Equal(new[]
        {
            "PrimaryBrush", "DarkPrimaryBrush", "DangerBrush", "DarkDangerBrush",
            "WarningBrush", "DarkWarningBrush", "InfoBrush", "DarkInfoBrush",
            "SuccessBrush", "DarkSuccessBrush", "AccentBrush", "DarkAccentBrush",
            "PrimaryTextBrush", "SecondaryTextBrush", "ThirdlyTextBrush", "ReverseTextBrush",
            "TextIconBrush", "BorderBrush", "SecondaryBorderBrush", "BackgroundBrush",
            "RegionBrush", "SecondaryRegionBrush", "ThirdlyRegionBrush", "TitleBrush",
            "DefaultBrush", "DarkDefaultBrush", "DarkMaskBrush", "DarkOpacityBrush",
        }, texts);
        var rootFill = Assert.IsType<SolidColorBrush>(((Control)view).Background);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), rootFill.Color);
    }

    [Fact]
    public void Staged_brush_demo_rows_carry_upstream_brushes()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.BrushDemo");
        var rows = Descendants(view).OfType<Label>().ToDictionary(l => (string)l.Content!);
        void PinSolid(string content, byte ba, byte br, byte bg, byte bb, byte fa, byte fr, byte fg, byte fb)
        {
            var row = rows[content];
            var fill = Assert.IsType<SolidColorBrush>(((Control)row).Background);
            Assert.Equal(Color.FromArgb(ba, br, bg, bb), fill.Color);
            var ink = Assert.IsType<SolidColorBrush>(((Control)row).Foreground);
            Assert.Equal(Color.FromArgb(fa, fr, fg, fb), ink.Color);
        }
        PinSolid("DarkPrimaryBrush", 0xFF, 0x32, 0x6C, 0xF3, 0xFF, 0xFF, 0xFF, 0xFF);
        PinSolid("DarkDangerBrush", 0xFF, 0xDB, 0x33, 0x40, 0xFF, 0xFF, 0xFF, 0xFF);
        PinSolid("RegionBrush", 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x21, 0x21, 0x21);
        PinSolid("BorderBrush", 0xFF, 0xE0, 0xE0, 0xE0, 0xFF, 0x21, 0x21, 0x21);
        PinSolid("PrimaryTextBrush", 0xFF, 0x21, 0x21, 0x21, 0xFF, 0xBD, 0xBD, 0xBD);
        PinSolid("DarkMaskBrush", 0x20, 0x00, 0x00, 0x00, 0xFF, 0x21, 0x21, 0x21);
        var title = rows["TitleBrush"];
        var titleFill = Assert.IsType<LinearGradientBrush>(((Control)title).Background);
        Assert.Equal(2, titleFill.GradientStops.Count);
        Assert.All(titleFill.GradientStops,
            stop => Assert.Equal(Color.FromArgb(0xFF, 0x32, 0x6C, 0xF3), stop.Color));
        Assert.Equal(0, titleFill.GradientStops[0].Offset);
        Assert.Equal(1, titleFill.GradientStops[1].Offset);
        // The five lib-styled rows keep default label chrome: the empty
        // fallback styles carry no setters (no replicated lib content).
        foreach (var content in new[] { "PrimaryBrush", "DangerBrush", "WarningBrush", "InfoBrush", "SuccessBrush" })
        {
            var row = rows[content];
            Assert.Null(((Control)row).Background);
            Assert.Null(((Control)row).Foreground);
        }
    }

    [Fact]
    public void Staged_label_demo_instantiates_with_translated_rows()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.LabelDemo");
        var labels = Descendants(view).OfType<Label>().ToList();
        Assert.Equal(12, labels.Count);
        var frame = WasmMilCore.RenderHeadless(view, new WpfSize(800, 600));
        var texts = frame.OfType<DrawCommand.DrawText>().Select(d => d.Text).ToList();
        var expected = string.Concat(Enumerable.Repeat("\u6807\u9898", 10));
        Assert.Equal(12, texts.Count);
        Assert.All(texts, text => Assert.Equal(expected, text));
    }

    [Fact]
    public void Staged_preview_slider_demo_instantiates_past_label_format()
    {
        // Re-pinned by slice-109: the line-10 ContentStringFormat stop is
        // gone, the view instantiates with both formatted labels. Full
        // STATE pins live in Slice109HandyLabelFormatTests.
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var type = entry.GetType("HandyControlDemo.UserControl.PreviewSliderDemo", throwOnError: true)!;
        var view = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type));
        Assert.Equal(2, Descendants(view).OfType<Label>().Count());
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
            XamlLoader.CustomElementSource = _stagedEntry;
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

    private static FrameworkElement InstantiateStagedView(Assembly entry, string fullName)
    {
        var type = entry.GetType(fullName, throwOnError: true)!;
        return Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type));
    }

    private static List<DependencyObject> Descendants(DependencyObject root)
    {
        var found = new List<DependencyObject> { root };
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is FrameworkElement element)
                foreach (var child in element.LogicalChildren)
                {
                    found.Add(child);
                    queue.Enqueue(child);
                }
        }
        return found;
    }
}
