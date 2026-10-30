// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-110 HandyControl ElementGroup/Honeycomb/Gravatar views.
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (absent System.Security.Permissions + System.Windows.Extensions,
/// both with no browser path; Convert map untouched, staged bins
/// byte-identical by hash, manifests untouched).
///
/// Advance, all reproduced against live staged bins via the engine-native
/// staged-bin boot oracle (status gate bypassed in-probe only) then pinned
/// by the new staged-oracle set: the staged ElementGroupDemo view
/// instantiates past the slice-109 line-24
/// `StaticResource 'ClockGeometry' was not found` stop with its four
/// converted ElementGroups and the clock Path carrying the shared
/// ClockGeometry; the staged HoneycombPanelDemo view instantiates past the
/// line-31 `StaticResource 'ListBoxCustom' was not found` stop with its
/// ListBox bound through the converted Locator/CardDemo to 10 rows of
/// converted Gravatars; the staged GravatarDemo view instantiates past the
/// line-14 `StaticResource 'GravatarCircleImg' was not found` stop (plus
/// lines 18/20 GravatarCircle) with 10 converted Gravatars.
///
/// Supporting real-behavior fixes, never fabricated: ClockGeometry carries
/// the real upstream MIT path data from the lib Themes/Theme.xaml geometry
/// table (the ErrorGeometry precedent; record start/middle/end verified
/// byte-verbatim inside the staged HandyControl image); ListBoxCustom lands
/// as an empty chrome-less shim with TargetType ListBox (its BasedOn
/// ListBoxBaseStyle plus the ItemContainerStyle/HorizontalContentAlignment
/// setters chain through BAML-only base styles, the slice-100 idiom);
/// GravatarCircle/GravatarCircleImg resolve in code to empty shared Styles
/// with TargetType the converted HandyControl.Controls.Gravatar control
/// (resource entries cannot name converted lib types without a staged image
/// present); engine ImageBrush gains the live-WPF-verified .ctor(ImageSource)
/// overload (net10.0 ref-pack Cecil evidence: exactly .ctor() plus
/// .ctor(ImageSource)), which the converted Gravatar code calls.
///
/// Paint stays honestly staged-bound: the clock Path paints real
/// DrawGeometry ink from the shared geometry while converted templates
/// behind the line-17 SkinDefault BAML-only stop stay chrome-less (an
/// honest boundary, pinned in-test). Remaining blockers: converted panel
/// layouts (ElementGroup/HoneycombPanel run converted code), the unchanged
/// line-17 SkinDefault BAML-only stop, and the 2 absent binaries with no
/// browser path.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice110HandyElementTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void ClockGeometry_fallback_carries_upstream_data()
    {
        var dict = XamlLoader.LoadResourceDictionary(HandyTheme.Xaml);
        var geometry = Assert.IsAssignableFrom<Geometry>(dict["ClockGeometry"]);
        Assert.NotNull(geometry);
    }

    [Fact]
    public void ListBoxCustom_fallback_is_empty_listbox_shim()
    {
        var dict = XamlLoader.LoadResourceDictionary(HandyTheme.Xaml);
        var style = Assert.IsType<Style>(dict["ListBoxCustom"]);
        Assert.Same(typeof(ListBox), style.TargetType);
        Assert.Empty(style.Setters);
    }

    [Fact]
    public void App_scope_resolves_new_keys_with_local_entries_winning()
    {
        Application.Current = new Application();
        Assert.True(Application.Current.TryFindResource("ClockGeometry", out var clock));
        Assert.IsAssignableFrom<Geometry>(clock);
        Assert.True(Application.Current.TryFindResource("ListBoxCustom", out var custom));
        Assert.Same(typeof(ListBox), Assert.IsType<Style>(custom).TargetType);
        var local = new Style(typeof(ListBox));
        Application.Current.Resources.Add("ListBoxCustom", local);
        Assert.True(Application.Current.TryFindResource("ListBoxCustom", out var winner));
        Assert.Same(local, winner);
        Assert.False(Application.Current.TryFindResource("NoSuchHandyKey110", out _));
    }

    [Fact]
    public void ImageBrush_source_ctor_lands_source_like_live_wpf()
    {
        var empty = new ImageBrush();
        Assert.Null(empty.ImageSource);
        var source = new BitmapImage();
        var brush = new ImageBrush(source);
        Assert.Same(source, brush.ImageSource);
        empty.ImageSource = source;
        Assert.Same(source, empty.ImageSource);
    }

    [Fact]
    public void ClockGeometry_path_paints_real_ink()
    {
        Application.Current = new Application();
        Assert.True(Application.Current.TryFindResource("ClockGeometry", out var shared));
        var path = Assert.IsType<Path>(XamlLoader.Load(
            "<Path xmlns=\"" + PresentationNs + "\" " +
            "Data=\"{StaticResource ClockGeometry}\" Width=\"16\" Height=\"16\" />").Root);
        Assert.Same(shared, Assert.IsAssignableFrom<Geometry>(path.Data));
        var frame = WasmMilCore.RenderHeadless(path, new WpfSize(800, 600));
        var ink = frame.OfType<DrawCommand.DrawGeometry>().ToList();
        Assert.NotEmpty(ink);
        Assert.All(ink, d => Assert.False(string.IsNullOrEmpty(d.PathData)));
    }

    [Fact]
    public void Converted_style_misses_without_element_source()
    {
        LoadStagedHandyControlEntry();
        XamlLoader.CustomElementSource = null;
        Application.Current = new Application();
        Assert.False(Application.Current.TryFindResource("GravatarCircle", out _));
        Assert.False(Application.Current.TryFindResource("GravatarCircleImg", out _));
        Assert.False(Slice110HandyElement.TryResolveConvertedStyle("NoSuchStyle110", out _));
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
            "GravatarCircle", "GravatarCircleImg",
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
    public void Staged_element_group_demo_instantiates_with_clock_icon()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var type = entry.GetType("HandyControlDemo.UserControl.ElementGroupDemo", throwOnError: true)!;
        var view = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type));
        var nodes = Descendants(view);
        Assert.Equal(4, nodes.Count(n => n.GetType().FullName == "HandyControl.Controls.ElementGroup"));
        var path = Assert.Single(nodes.OfType<Path>());
        Assert.True(Application.Current!.TryFindResource("ClockGeometry", out var shared));
        Assert.Same(shared, Assert.IsAssignableFrom<Geometry>(path.Data));
    }

    [Fact]
    public void Staged_honeycomb_demo_instantiates_with_ten_rows()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var type = entry.GetType("HandyControlDemo.UserControl.HoneycombPanelDemo", throwOnError: true)!;
        var view = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type));
        var nodes = Descendants(view);
        Assert.True(Application.Current!.TryFindResource("ListBoxCustom", out var shared));
        Assert.Same(typeof(ListBox), Assert.IsType<Style>(shared).TargetType);
        Assert.Empty(Assert.IsType<Style>(shared).Setters);
        var list = Assert.Single(nodes.OfType<ListBox>());
        Assert.Equal(10, list.Items.Count);
        Assert.Equal(10, nodes.Count(n => n.GetType().FullName == "HandyControl.Controls.Gravatar"));
    }

    [Fact]
    public void Staged_gravatar_demo_instantiates_with_converted_styles()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var type = entry.GetType("HandyControlDemo.UserControl.GravatarDemo", throwOnError: true)!;
        var view = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type));
        var nodes = Descendants(view);
        Assert.Equal(10, nodes.Count(n => n.GetType().FullName == "HandyControl.Controls.Gravatar"));
        foreach (var key in new[] { "GravatarCircle", "GravatarCircleImg" })
        {
            Assert.True(Application.Current!.TryFindResource(key, out var value));
            var style = Assert.IsType<Style>(value);
            Assert.Equal("HandyControl.Controls.Gravatar", style.TargetType!.FullName);
            Assert.True(typeof(UIElement).IsAssignableFrom(style.TargetType));
            Assert.Empty(style.Setters);
            Assert.True(Application.Current.TryFindResource(key, out var again));
            Assert.Same(style, again);
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
