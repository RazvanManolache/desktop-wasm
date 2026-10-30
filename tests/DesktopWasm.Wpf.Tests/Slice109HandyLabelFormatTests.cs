// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-109 HandyControl Label ContentStringFormat + StackPanel host + Page chrome.
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (absent System.Security.Permissions + System.Windows.Extensions,
/// both with no browser path; Convert map untouched, staged bins
/// byte-identical by hash, manifests untouched).
///
/// Advance, all reproduced against live staged bins via the engine-native
/// staged-bin boot oracle (status gate bypassed in-probe only) then pinned
/// by the new staged-oracle set: the staged PreviewSliderDemo view
/// (HandyControlDemo.UserControl.PreviewSliderDemo) instantiates past the
/// slice-108 line-10 `'ContentStringFormat' is not valid on &lt;Label&gt;`
/// stop. Both demo labels carry the converted PreviewPosition default
/// (0.0, read off the converted HandyControl PreviewSlider.PreviewPosition
/// attached DP through the binding's RelativeSource Self path) with the
/// upstream "#0.00"/"#0.0" formats, rendering "0.00"/"0.0" like live WPF.
///
/// Supporting real-behavior fixes, never fabricated: ContentControl gains
/// the WPF-shaped ContentStringFormat DP (owner ContentControl, string,
/// null default, AffectsMeasure like the live-parity chrome DPs) shared by
/// every content host including Label; the content-to-text rule is
/// live-verified against real WPF on net10.0-windows (numeric formats apply
/// as "{0:format}", brace-carrying formats run composite, strings ignore
/// the format, null renders empty, all invariant - even de-DE renders
/// "500.00") and shared with ContentPresenter; XAML attributes land on
/// Label and ContentPresenter; binding paths split on top-level dots only
/// so parenthesized owner-qualified steps survive; binding reads resolve
/// converted attached DPs by unique owner short name (engine wins, unknown
/// or ambiguous owners keep the legacy attached-bag behavior); the legacy
/// ListBox arranges rows left-to-right through a horizontal StackPanel
/// ItemsPanel host (vertical StackPanel matches the legacy stack exactly);
/// WpfAppBoot.Hydrate copies parsed Page Background/Foreground onto the
/// component with local-beats-style precedence (Page owns no other chrome).
///
/// Paint stays honestly empty in-view: the converted PreviewSlider shows
/// its PreviewContent through the BAML-only lib theme (behind the
/// line-17 SkinDefault stop), so the labels carry values without template
/// ink - the same honest-empty boundary as every prior converted-control
/// slice. Remaining blockers: ElementGroupDemo ClockGeometry (line 24),
/// HoneycombPanelDemo ListBoxCustom (line 31), GravatarDemo
/// GravatarCircleImg (line 14), custom converted panel layouts, and the
/// 2 absent binaries with no browser path.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice109HandyLabelFormatTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void ContentStringFormat_dp_matches_live_wpf_shape()
    {
        var dp = ContentControl.ContentStringFormatProperty;
        Assert.Equal("ContentStringFormat", dp.Name);
        Assert.Equal(typeof(string), dp.PropertyType);
        Assert.Equal(typeof(ContentControl), dp.OwnerType);
        Assert.Null(dp.DefaultMetadata.DefaultValue);
        Assert.Same(dp, DependencyProperty.FromName(typeof(Label), "ContentStringFormat"));
    }

    [Theory]
    [InlineData(500.0, "#0.00", "500.00")]
    [InlineData(500.0, "#0.0", "500.0")]
    [InlineData(500.0, "{0:0.00}", "500.00")]
    [InlineData(500.0, "Value: {0}", "Value: 500")]
    [InlineData(500, "#0.00", "500.00")]
    [InlineData("hi", "#0.00", "hi")]
    [InlineData(null, "#0.00", "")]
    [InlineData(500.0, null, "500")]
    public void Format_rule_matches_live_wpf(object? content, string? format, string expected)
    {
        Assert.Equal(expected, ContentControl.FormatContentText(content, format));
    }

    [Fact]
    public void Label_xaml_format_attribute_lands_with_string_bypass()
    {
        Application.Current = new Application();
        var label = Assert.IsType<Label>(XamlLoader.Load(
            "<Label xmlns=\"" + PresentationNs + "\" Content=\"500\" ContentStringFormat=\"#0.00\" />").Root);
        Assert.Equal("#0.00", label.ContentStringFormat);
        // XAML literal content is a string: live WPF ignores the format.
        var frame = WasmMilCore.RenderHeadless(label, new WpfSize(800, 600));
        Assert.Equal(new[] { "500" }, frame.OfType<DrawCommand.DrawText>().Select(d => d.Text));
    }

    [Fact]
    public void Label_numeric_content_renders_formatted_like_live_wpf()
    {
        Application.Current = new Application();
        var label = new Label { Content = 500.0, ContentStringFormat = "#0.00" };
        var frame = WasmMilCore.RenderHeadless(label, new WpfSize(800, 600));
        Assert.Equal(new[] { "500.00" }, frame.OfType<DrawCommand.DrawText>().Select(d => d.Text));
    }

    [Fact]
    public void ContentPresenter_xaml_format_attribute_lands()
    {
        Application.Current = new Application();
        var presenter = Assert.IsType<ContentPresenter>(XamlLoader.Load(
            "<ContentPresenter xmlns=\"" + PresentationNs + "\" Content=\"500\" ContentStringFormat=\"#0.00\" />").Root);
        Assert.Equal("#0.00", presenter.ContentStringFormat);
        var frame = WasmMilCore.RenderHeadless(presenter, new WpfSize(800, 600));
        Assert.Equal(new[] { "500" }, frame.OfType<DrawCommand.DrawText>().Select(d => d.Text));
        presenter.ContentStringFormat = "{0:0.00}";
        presenter.Content = 500.0;
        frame = WasmMilCore.RenderHeadless(presenter, new WpfSize(800, 600));
        Assert.Equal(new[] { "500.00" }, frame.OfType<DrawCommand.DrawText>().Select(d => d.Text));
    }

    [Fact]
    public void SplitPath_keeps_parenthesized_owner_qualified_step()
    {
        Assert.Equal(
            new[] { "(hc:PreviewSlider.PreviewPosition)" },
            BindingExpression.SplitPath("(hc:PreviewSlider.PreviewPosition)"));
        Assert.Equal(new[] { "A", "B", "C" }, BindingExpression.SplitPath("A.B.C"));
        Assert.Equal(
            new[] { "(A.B)", "C" },
            BindingExpression.SplitPath("(A.B).C"));
    }

    [Fact]
    public void Legacy_listbox_horizontal_stack_panel_host_arranges_side_by_side()
    {
        Application.Current = new Application();
        var list = Assert.IsType<ListBox>(XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\" Width=\"400\" Height=\"100\">" +
            "<ListBox.ItemTemplate><DataTemplate><Border Width=\"120\" Height=\"40\" />" +
            "</DataTemplate></ListBox.ItemTemplate>" +
            "<ListBox.ItemsPanel><ItemsPanelTemplate><StackPanel Orientation=\"Horizontal\" />" +
            "</ItemsPanelTemplate></ListBox.ItemsPanel></ListBox>").Root);
        Assert.True(list.ItemsPanel is not null);
        list.ItemsSource = new[] { "a", "b", "c" };
        Assert.Equal(3, list.Items.Count);
        WasmMilCore.RenderHeadless(list, new WpfSize(400, 100));
        var rects = list.Items.Select(r => r.RenderRect).ToList();
        Assert.True(rects[1].X > rects[0].X);
        Assert.True(rects[2].X > rects[1].X);
        Assert.Equal(rects[0].Y, rects[1].Y);
        Assert.Equal(rects[1].Y, rects[2].Y);
        Assert.All(rects, r => Assert.Equal(120, r.Width));
    }

    [Fact]
    public void Legacy_listbox_vertical_stack_panel_host_keeps_legacy_stack()
    {
        Application.Current = new Application();
        var list = Assert.IsType<ListBox>(XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\" Width=\"300\">" +
            "<ListBox.ItemTemplate><DataTemplate><Border Width=\"120\" Height=\"40\" />" +
            "</DataTemplate></ListBox.ItemTemplate>" +
            "<ListBox.ItemsPanel><ItemsPanelTemplate><StackPanel Orientation=\"Vertical\" />" +
            "</ItemsPanelTemplate></ListBox.ItemsPanel></ListBox>").Root);
        list.ItemsSource = new[] { "a", "b" };
        WasmMilCore.RenderHeadless(list, new WpfSize(300, 600));
        var rects = list.Items.Select(r => r.RenderRect).ToList();
        Assert.Equal(rects[0].X, rects[1].X);
        Assert.True(rects[1].Y > rects[0].Y);
    }

    /// <summary>Stand-in for a converted Page codebehind (chrome copy target).</summary>
    public sealed class PageChromeProbe : Page
    {
        public PageChromeProbe() =>
            Application.LoadComponent(this, new Uri("Boot/PageChrome.xaml", UriKind.Relative));
    }

    /// <summary>Stand-in proving parsed chrome never beats a local value.</summary>
    public sealed class PageChromeLocalProbe : Page
    {
        public PageChromeLocalProbe()
        {
            Background = new SolidColorBrush(Colors.Blue);
            Application.LoadComponent(this, new Uri("Boot/PageChromeLocal.xaml", UriKind.Relative));
        }
    }

    [Fact]
    public void Page_hydrate_carries_parsed_root_chrome()
    {
        Application.Current = new Application();
        var page = new PageChromeProbe();
        Assert.Equal(Colors.Red, ((SolidColorBrush)page.Background!).Color);
        Assert.Equal(Colors.White, ((SolidColorBrush)page.Foreground!).Color);
        Assert.NotNull(page.Content);
    }

    [Fact]
    public void Page_hydrate_keeps_local_chrome_over_parsed()
    {
        Application.Current = new Application();
        var page = new PageChromeLocalProbe();
        Assert.Equal(Colors.Blue, ((SolidColorBrush)page.Background!).Color);
        Assert.Equal(Colors.White, ((SolidColorBrush)page.Foreground!).Color);
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
            "RegionBrush", "LabelPrimary", "ContentStringFormat", "PreviewSlider",
            "PreviewPosition", "ClockGeometry", "ListBoxCustom",
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
    public void Staged_preview_slider_demo_instantiates_with_formatted_labels()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var type = entry.GetType("HandyControlDemo.UserControl.PreviewSliderDemo", throwOnError: true)!;
        var view = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type));
        var labels = Descendants(view).OfType<Label>().ToList();
        Assert.Equal(2, labels.Count);
        // Both labels read the converted PreviewPosition attached-DP default
        // through their RelativeSource Self binding; the upstream formats land.
        Assert.Equal("#0.00", labels[0].ContentStringFormat);
        Assert.Equal("#0.0", labels[1].ContentStringFormat);
        Assert.Equal(0.0, Assert.IsType<double>(labels[0].Content));
        Assert.Equal(0.0, Assert.IsType<double>(labels[1].Content));
        // The format path renders end to end on the staged values (live-WPF
        // values); in-view ink stays honestly empty behind the converted
        // PreviewSlider's BAML-only template stop.
        foreach (var (label, expected) in new[] { (labels[0], "0.00"), (labels[1], "0.0") })
        {
            var frame = WasmMilCore.RenderHeadless(label, new WpfSize(800, 600));
            Assert.Equal(new[] { expected }, frame.OfType<DrawCommand.DrawText>().Select(d => d.Text));
        }
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
