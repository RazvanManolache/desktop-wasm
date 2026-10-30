// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-105 HandyControl converted-lib views: BorderDemo
/// BorderRegion first, then Avatar/ChatBox/CardDemo paint and NotifyIconDemo,
/// behind the unchanged line-17 SkinDefault BAML-only stop. Member evidence
/// via the staged images themselves (hash-verified, probed in-process):
/// converted HandyControl.Controls.ScrollViewer carries IsInertiaEnabled over
/// an attached bool DP (false default); converted ImageAttach carries the
/// ImageSource-typed SourceFailed attached DP; converted ControlCommands live
/// at HandyControl.Interactivity.ControlCommands (OpenLink plus the
/// PushMainWindow2Top/ShutdownApp commands the staged views wire literally);
/// converted HandyControl.Interactivity.RoutedEventTrigger carries a settable
/// RoutedEvent with EventToCommand actions; converted ChatBubble.Selected
/// resolves to SelectableItem.SelectedEvent; converted NotifyIcon carries
/// Token (string), Icon (ImageSource), IsBlink plus Click/MouseDoubleClick
/// events and a Callback needing RoutedEventArgs(RoutedEvent); the demo
/// entry carries StringRepeatConverter (IValueConverter, default ctor),
/// MessageToken string constants, chat/card/notify view-models on the
/// Locator, and the loose Basic/Geometries.xaml values backing the new
/// KeyboardGeometry/ImageGeometry fallbacks. Engine work, never fabricated:
/// ScrollViewer instance IsInertiaEnabled CLR over the existing attached DP,
/// StyleValueConverter ImageSource branch (lazy BitmapImage, same shape as
/// the Image.Source path), HandyTheme style/geometry/ItemsPanel fallbacks,
/// code-resolved converted converters (Locator precedent), converted x:Static
/// fallback, RoutedEventTrigger loading, literal trigger commands, Lang
/// StaticResource converters, Image.ImageFailed plus SourceProperty DP,
/// legacy ListBox retained ItemsPanel, MenuItem static-command lib lookup,
/// Panel MeasureOverride/ArrangeOverride routing plus default child paint.
/// Advance pinned here: BorderDemo, Avatar, CardDemo and NotifyIconDemo
/// instantiate (BorderDemo and NotifyIconDemo paint real translated ink;
/// Avatar keeps honest-empty image with the fallback retained and the
/// missing-pack swap proven; CardDemo rows stay ToString text on the
/// retained-template boundary); ChatBox moves past the line-22 trigger to
/// the lib-only AudioGeometry key (no in-workspace source), the precise
/// next blocker with the 2 absent binaries still declared.
/// Slice-106 re-pin: CardDemo rows inflate converted Cards (Footer/Header
/// bindings land) and ChatBox instantiates past AudioGeometry (geometries
/// land on the converted IconSwitchElement DPs; a live message inflates a
/// bound ChatBubble row); depth lives in Slice106HandyChatCardTests.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice105HandyViewsTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string HandyNs = "https://handyorg.github.io/handycontrol";

    private readonly Application? _previous = Application.Current;

    public Slice105HandyViewsTests()
    {
        WpfAnimationClock.ResetForTests();
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
        WpfAnimationClock.ResetForTests();
    }

    [Fact]
    public void Inertia_clr_defaults_false_and_shares_attached_store()
    {
        var scroller = new ScrollViewer();
        Assert.False(scroller.IsInertiaEnabled);
        Assert.False(ScrollViewer.GetIsInertiaEnabled(scroller));
        scroller.IsInertiaEnabled = true;
        Assert.True(ScrollViewer.GetIsInertiaEnabled(scroller));
        Assert.True((bool)scroller.GetValue(ScrollViewer.IsInertiaEnabledProperty)!);
        ScrollViewer.SetIsInertiaEnabled(scroller, false);
        Assert.False(scroller.IsInertiaEnabled);
    }

    [Fact]
    public void Inertia_direct_attribute_lands_on_engine_scrollviewer()
    {
        var scroller = Assert.IsType<ScrollViewer>(XamlLoader.Load(
            "<ScrollViewer xmlns=\"" + PresentationNs + "\" IsInertiaEnabled=\"True\" />").Root);
        Assert.True(scroller.IsInertiaEnabled);
        var bad = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<ScrollViewer xmlns=\"" + PresentationNs + "\" IsInertiaEnabled=\"maybe\" />"));
        Assert.Contains("IsInertiaEnabled", bad.Message);
    }

    [Fact]
    public void Image_source_converts_pack_uri_lazily()
    {
        var image = StyleValueConverter.Convert("/Resources/Img/chrome_dragon.png", typeof(ImageSource));
        var bitmap = Assert.IsType<BitmapImage>(image);
        Assert.Equal("/Resources/Img/chrome_dragon.png", bitmap.UriSource!.OriginalString);
        var icon = StyleValueConverter.Convert(
            "/HandyControlDemo;component/Resources/Img/icon-white.ico", typeof(BitmapImage));
        Assert.IsType<BitmapImage>(icon);
    }

    [Fact]
    public void Image_source_keeps_relative_garbage_but_rejects_bad_absolute()
    {
        // Same contract as the <Image Source="..."> path (identical
        // expression): relative text stays a lazy retained BitmapImage that
        // simply resolves to no pack bytes, while a malformed absolute URI
        // fails at construction like the element path.
        var retained = Assert.IsType<BitmapImage>(
            StyleValueConverter.Convert(":://not a uri", typeof(ImageSource)));
        Assert.Equal(":://not a uri", retained.UriSource!.OriginalString);
        Assert.ThrowsAny<FormatException>(() =>
            StyleValueConverter.Convert("http://exa mple/bad pack.png", typeof(ImageSource)));
    }

    [Fact]
    public void Image_failed_event_matches_converted_shape()
    {
        var add = typeof(Image).GetMethod("add_ImageFailed",
            BindingFlags.Public | BindingFlags.Instance);
        var remove = typeof(Image).GetMethod("remove_ImageFailed",
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(add);
        Assert.NotNull(remove);
        Assert.Equal(typeof(EventHandler<ExceptionRoutedEventArgs>), add!.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(EventHandler<ExceptionRoutedEventArgs>), remove!.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void Image_source_property_is_wpf_shaped_and_store_backed()
    {
        var field = typeof(Image).GetField("SourceProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        var dp = Assert.IsAssignableFrom<DependencyProperty>(field?.GetValue(null));
        Assert.Equal("Source", dp.Name);
        Assert.Same(typeof(Image), dp.OwnerType);
        Assert.True(typeof(ImageSource).IsAssignableFrom(dp.PropertyType));
        var image = new Image();
        Assert.Null(image.Source);
        var bitmap = new BitmapImage(new Uri("/x.png", UriKind.RelativeOrAbsolute));
        image.SetValue(Image.SourceProperty, bitmap);
        Assert.Same(bitmap, image.Source);
    }

    [Fact]
    public void Image_failed_drives_converted_fallback_swap()
    {
        LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var attach = lib.GetType("HandyControl.Controls.ImageAttach", throwOnError: true)!;
        var dp = (DependencyProperty)attach.GetField("SourceFailedProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!.GetValue(null)!;
        var image = new Image();
        var fallback = new BitmapImage(new Uri("/Resources/Img/chrome_dragon.png", UriKind.RelativeOrAbsolute));
        image.SetValue(dp, fallback);
        Assert.Null(image.Source);
        var fired = 0;
        image.ImageFailed += (_, _) => fired++;
        image.Source = new BitmapImage(new Uri("/Resources/Img/nope-missing.png", UriKind.RelativeOrAbsolute));
        Assert.Equal(1, fired);
        Assert.Same(fallback, image.Source);
        image.Source = new BitmapImage(new Uri("/Resources/Img/chrome_dragon.png", UriKind.RelativeOrAbsolute));
        Assert.Equal(1, fired);
        Assert.NotSame(fallback, image.Source);
    }

    [Fact]
    public void Image_missing_pack_raises_but_present_pack_stays_silent()
    {
        LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var missing = new Image();
        var fired = 0;
        missing.ImageFailed += (_, _) => fired++;
        missing.Source = new BitmapImage(new Uri("/Resources/Img/nope-missing.png", UriKind.RelativeOrAbsolute));
        Assert.Equal(1, fired);
        var present = new Image();
        present.ImageFailed += (_, _) => fired++;
        present.Source = new BitmapImage(new Uri("/Resources/Img/chrome_dragon.png", UriKind.RelativeOrAbsolute));
        Assert.Equal(1, fired);
    }

    [Theory]
    [InlineData("BorderRegion", "Border")]
    [InlineData("BorderTipPrimary", "Border")]
    [InlineData("BorderTipDanger", "Border")]
    [InlineData("BorderTipWarning", "Border")]
    [InlineData("BorderTipInfo", "Border")]
    [InlineData("BorderTipSuccess", "Border")]
    [InlineData("ToggleButtonSwitch", "ToggleButton")]
    [InlineData("GroupBoxOriginal", "GroupBox")]
    [InlineData("ButtonCustom", "Button")]
    [InlineData("ToggleButtonIcon", "ToggleButton")]
    [InlineData("StackPanelVerticalListBox", "ListBox")]
    [InlineData("WrapPanelHorizontalListBox", "ListBox")]
    public void Fallback_styles_carry_correct_targets(string key, string targetName)
    {
        Application.Current = new Application();
        Assert.True(Application.Current.TryFindResource(key, out var value));
        var style = Assert.IsType<Style>(value);
        Assert.Equal(targetName, style.TargetType.Name);
        Assert.Empty(style.Setters);
    }

    [Fact]
    public void Fallback_items_panel_template_inflates_wrap_panel()
    {
        Application.Current = new Application();
        Assert.True(Application.Current.TryFindResource(
            "FluidMoveBehaviorWrapPanelItemsPanelTemplate", out var value));
        var template = Assert.IsType<ItemsPanelTemplate>(value);
        var host = template.LoadContent();
        Assert.IsType<WrapPanel>(host);
    }

    [Fact]
    public void Fallback_listbox_items_panel_lands_from_resources()
    {
        Application.Current = new Application();
        var list = Assert.IsType<ListBox>(XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\" " +
            "ItemsPanel=\"{StaticResource FluidMoveBehaviorWrapPanelItemsPanelTemplate}\" />").Root);
        Assert.True(Application.Current.TryFindResource(
            "FluidMoveBehaviorWrapPanelItemsPanelTemplate", out var shared));
        Assert.Same(shared, list.ItemsPanel);
    }

    [Fact]
    public void Fallback_geometries_carry_demo_path_data()
    {
        Application.Current = new Application();
        Assert.True(Application.Current.TryFindResource("KeyboardGeometry", out var keyboard));
        Assert.IsAssignableFrom<Geometry>(keyboard);
        Assert.True(Application.Current.TryFindResource("ImageGeometry", out var image));
        var data = Assert.IsAssignableFrom<Geometry>(image);
        Assert.StartsWith("M 736 224 L 288 224", data.ToPathData(), StringComparison.Ordinal);
    }

    [Fact]
    public void Converted_converters_resolve_cached_with_local_wins()
    {
        LoadStagedHandyControlEntry();
        Application.Current = new Application();
        Assert.True(Application.Current.TryFindResource("StringRepeatConverter", out var repeat));
        Assert.IsAssignableFrom<IValueConverter>(repeat);
        Assert.Equal("HandyControlDemo.Tools.Converter.StringRepeatConverter", repeat!.GetType().FullName);
        Assert.True(Application.Current.TryFindResource("Boolean2VisibilityReConverter", out var re));
        Assert.IsAssignableFrom<IValueConverter>(re);
        Assert.Equal("HandyControl.Tools.Converter.Boolean2VisibilityReConverter", re!.GetType().FullName);
        Assert.True(Application.Current.TryFindResource("StringRepeatConverter", out var again));
        Assert.Same(repeat, again);
        var local = new BooleanToVisibilityConverter();
        Application.Current.Resources.Add("StringRepeatConverter", local);
        Assert.True(Application.Current.TryFindResource("StringRepeatConverter", out var winner));
        Assert.Same(local, winner);
        Assert.False(Application.Current.TryFindResource("NoSuchConverter105", out _));
    }

    [Fact]
    public void Converted_converters_miss_without_element_source()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = new Application();
        Assert.False(Application.Current.TryFindResource("StringRepeatConverter", out _));
        Assert.False(Application.Current.TryFindResource("Boolean2VisibilityReConverter", out _));
    }

    [Fact]
    public void Lang_converter_missing_key_fails_precisely()
    {
        LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var error = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<TextBlock xmlns=\"" + PresentationNs + "\" " +
            "xmlns:ex=\"clr-namespace:HandyControlDemo.Tools.Extension;assembly=HandyControlDemo\" " +
            "Text=\"{ex:Lang Key=Title,Converter={StaticResource NoSuchConverter105}}\" />"));
        Assert.Contains("NoSuchConverter105", error.Message);
    }

    [Fact]
    public void Converted_xstatic_resolves_entry_string_constant()
    {
        LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var button = Assert.IsType<Button>(XamlLoader.Load(
            "<Button xmlns=\"" + PresentationNs + "\" " +
            "xmlns:data=\"clr-namespace:HandyControlDemo.Data;assembly=HandyControlDemo\" " +
            "Tag=\"{x:Static data:MessageToken.NotifyIconDemo}\" />").Root);
        Assert.Equal("NotifyIconDemo", button.Tag);
    }

    [Fact]
    public void Engine_xstatic_still_resolves_and_unknown_stays_loud()
    {
        LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var button = Assert.IsType<Button>(XamlLoader.Load(
            "<Button xmlns=\"" + PresentationNs + "\" Tag=\"{x:Static Colors.Red}\" />").Root);
        Assert.NotNull(button.Tag);
        var error = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<Button xmlns=\"" + PresentationNs + "\" " +
            "xmlns:data=\"clr-namespace:HandyControlDemo.Data;assembly=HandyControlDemo\" " +
            "Tag=\"{x:Static data:MessageToken.NoSuchToken105}\" />"));
        Assert.Contains("NoSuchToken105", error.Message);
    }

    [Fact]
    public void Routed_event_trigger_needs_routed_event_shape()
    {
        LoadStagedHandyControlEntry();
        XamlLoader.CustomElementSource = LoadStagedHandyControlEntry();
        var missing = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<TabControl xmlns=\"" + PresentationNs + "\" xmlns:hc=\"" + HandyNs + "\">" +
            "<hc:Interaction.Triggers><hc:RoutedEventTrigger /></hc:Interaction.Triggers>" +
            "</TabControl>"));
        Assert.Contains("needs RoutedEvent=", missing.Message);
        var shape = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<TabControl xmlns=\"" + PresentationNs + "\" xmlns:hc=\"" + HandyNs + "\">" +
            "<hc:Interaction.Triggers><hc:RoutedEventTrigger RoutedEvent=\"ChatBubble\" /></hc:Interaction.Triggers>" +
            "</TabControl>"));
        Assert.Contains("not a routed event", shape.Message);
        var owner = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<TabControl xmlns=\"" + PresentationNs + "\" xmlns:hc=\"" + HandyNs + "\">" +
            "<hc:Interaction.Triggers><hc:RoutedEventTrigger RoutedEvent=\"hc:Nope.Nope\" /></hc:Interaction.Triggers>" +
            "</TabControl>"));
        Assert.Contains("no converted owner", owner.Message);
        var evt = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<TabControl xmlns=\"" + PresentationNs + "\" xmlns:hc=\"" + HandyNs + "\">" +
            "<hc:Interaction.Triggers><hc:RoutedEventTrigger RoutedEvent=\"hc:ChatBubble.Nope\" /></hc:Interaction.Triggers>" +
            "</TabControl>"));
        Assert.Contains("no routed event", evt.Message);
    }

    [Fact]
    public void Literal_trigger_command_resolves_converted_command()
    {
        LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var tabs = Assert.IsType<TabControl>(XamlLoader.Load(
            "<TabControl xmlns=\"" + PresentationNs + "\" xmlns:hc=\"" + HandyNs + "\">" +
            "<hc:Interaction.Triggers><hc:EventTrigger EventName=\"SelectionChanged\">" +
            "<hc:EventToCommand Command=\"hc:ControlCommands.PushMainWindow2Top\" />" +
            "</hc:EventTrigger></hc:Interaction.Triggers>" +
            "</TabControl>").Root);
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var interaction = lib.GetType("HandyControl.Interactivity.Interaction", throwOnError: true)!;
        var triggers = (System.Collections.IList)interaction.GetMethod("GetTriggers")!.Invoke(null, [tabs])!;
        var trigger = Assert.Single(triggers);
        var actions = (System.Collections.IList)trigger.GetType().GetProperty("Actions")!.GetValue(trigger)!;
        var command = actions[0]!.GetType().GetProperty("Command")!.GetValue(actions[0]);
        var expected = lib.GetType("HandyControl.Interactivity.ControlCommands", throwOnError: true)!
            .GetProperty("PushMainWindow2Top")!.GetValue(null);
        Assert.Same(expected, command);
    }

    [Fact]
    public void Literal_trigger_command_keeps_legacy_miss()
    {
        LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var error = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<TabControl xmlns=\"" + PresentationNs + "\" xmlns:hc=\"" + HandyNs + "\">" +
            "<hc:Interaction.Triggers><hc:EventTrigger EventName=\"SelectionChanged\">" +
            "<hc:EventToCommand Command=\"hc:ControlCommands.NoSuchCommand105\" />" +
            "</hc:EventTrigger></hc:Interaction.Triggers>" +
            "</TabControl>"));
        Assert.Contains("is not a public static command", error.Message);
        var unknown = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<TabControl xmlns=\"" + PresentationNs + "\" xmlns:hc=\"" + HandyNs + "\">" +
            "<hc:Interaction.Triggers><hc:EventTrigger EventName=\"SelectionChanged\">" +
            "<hc:EventToCommand Command=\"Nope105.NoSuchCommand\" />" +
            "</hc:EventTrigger></hc:Interaction.Triggers>" +
            "</TabControl>"));
        Assert.Contains("needs a {Binding}", unknown.Message);
    }

    [Fact]
    public void Menu_item_static_command_reaches_converted_lib()
    {
        LoadStagedHandyControlEntry();
        var item = Assert.IsType<MenuItem>(XamlLoader.Load(
            "<MenuItem xmlns=\"" + PresentationNs + "\" Header=\"Exit\" " +
            "Command=\"hc:ControlCommands.ShutdownApp\" />").Root);
        var expected = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase))
            .GetType("HandyControl.Interactivity.ControlCommands", throwOnError: true)!
            .GetProperty("ShutdownApp")!.GetValue(null);
        Assert.Same(expected, item.Command);
    }

    [Fact]
    public void Routed_event_args_carry_routed_token_shapes()
    {
        var empty = new RoutedEventArgs();
        Assert.Null(empty.RoutedEvent);
        Assert.Null(empty.Source);
        var token = ScrollViewer.ScrollChangedEvent;
        var withToken = new RoutedEventArgs(token);
        Assert.Same(token, withToken.RoutedEvent);
        var withSource = new RoutedEventArgs(token, "host");
        Assert.Same(token, withSource.RoutedEvent);
        Assert.Equal("host", withSource.Source);
    }

    private sealed class OverridePanel : Panel
    {
        public int MeasureCalls;
        public int ArrangeCalls;

        protected override Size MeasureOverride(Size constraint)
        {
            MeasureCalls++;
            var width = 0.0;
            var height = 0.0;
            foreach (var child in Children)
            {
                if (child is FrameworkElement fe)
                {
                    fe.Measure((WpfSize)constraint);
                    width = Math.Max(width, fe.DesiredSize.Width);
                    height += fe.DesiredSize.Height;
                }
            }
            return new Size(Math.Min(width, constraint.Width), Math.Min(height, constraint.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            ArrangeCalls++;
            var y = 0.0;
            foreach (var child in Children)
            {
                if (child is FrameworkElement fe)
                {
                    fe.Arrange(new WpfRect(0, y, finalSize.Width, fe.DesiredSize.Height));
                    y += fe.DesiredSize.Height;
                }
            }
            return finalSize;
        }
    }

    [Fact]
    public void Panel_core_routes_through_measure_arrange_overrides()
    {
        var panel = new OverridePanel();
        var text = new TextBlock { Text = "Hello world" };
        panel.Children.Add(text);
        panel.Measure(new WpfSize(800, 600));
        Assert.Equal(1, panel.MeasureCalls);
        Assert.True(panel.DesiredSize.Width > 60 && panel.DesiredSize.Width < 80);
        Assert.Equal(text.DesiredSize, panel.DesiredSize);
        panel.Arrange(new WpfRect(0, 0, 800, 600));
        Assert.Equal(1, panel.ArrangeCalls);
        Assert.Equal(new WpfRect(0, 0, 800, text.DesiredSize.Height), text.RenderRect);
    }

    private sealed class BarePanel : Panel
    {
    }

    [Fact]
    public void Panel_default_paint_walks_children()
    {
        var panel = new BarePanel();
        panel.Children.Add(new TextBlock { Text = "Hello world" });
        panel.Measure(new WpfSize(800, 600));
        panel.Arrange(new WpfRect(0, 0, 800, 600));
        var frame = WasmMilCore.RenderHeadless(panel, new WpfSize(800, 600));
        var ink = Assert.Single(frame.OfType<DrawCommand.DrawText>());
        Assert.Equal("Hello world", ink.Text);
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
            "BorderRegion", "BorderTipPrimary", "BorderTipDanger", "BorderTipWarning",
            "BorderTipInfo", "BorderTipSuccess", "ToggleButtonSwitch", "GroupBoxOriginal",
            "ButtonCustom", "ToggleButtonIcon", "StackPanelVerticalListBox",
            "WrapPanelHorizontalListBox", "FluidMoveBehaviorWrapPanelItemsPanelTemplate",
            "KeyboardGeometry", "ImageGeometry", "StringRepeatConverter",
            "Boolean2VisibilityReConverter", "IsInertiaEnabled", "ImageFailed",
            "RoutedEventTrigger",
        })
            Assert.DoesNotContain(details, d => d.Contains(name));
        _ = entry;
    }

    [Fact]
    public void Staged_border_demo_instantiates_with_region_styles_and_shadow()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.BorderDemo");
        var nodes = Descendants(view);
        var borders = nodes.OfType<Border>().ToList();
        Assert.Equal(17, borders.Count);
        Assert.Equal(11, nodes.OfType<TextBlock>().Count());
        Assert.Equal(3, nodes.Count(n => n.GetType().Name == "UniformSpacingPanel"));
        var scroller = Assert.Single(nodes.OfType<ScrollViewer>());
        Assert.True(scroller.IsInertiaEnabled);
        Assert.True((bool)scroller.GetValue(ScrollViewer.IsInertiaEnabledProperty)!);
        var shadowed = borders.Where(b => b.Effect is not null).ToList();
        Assert.Equal(5, shadowed.Count);
        var first = Assert.IsType<DropShadowEffect>(borders[1].Effect);
        Assert.Equal(5, first.BlurRadius);
        Assert.Equal(1, first.ShadowDepth);
        var last = Assert.IsType<DropShadowEffect>(borders[5].Effect);
        Assert.Equal(35, last.BlurRadius);
        Assert.Equal(13, last.ShadowDepth);
    }

    [Fact]
    public void Staged_border_demo_paints_translated_text_ink()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.BorderDemo");
        var frame = WasmMilCore.RenderHeadless(view, new WpfSize(800, 600));
        var texts = frame.OfType<DrawCommand.DrawText>().ToList();
        Assert.Equal(11, texts.Count);
        Assert.All(texts, t => Assert.False(string.IsNullOrEmpty(t.Text)));
        Assert.Contains(texts, t => t.Text == "这是内容");
        Assert.Contains(texts, t => t.Text.StartsWith("标题标题", StringComparison.Ordinal));
    }

    [Fact]
    public void Staged_avatar_instantiates_with_retained_fallback_and_honest_empty_image()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.Avatar");
        var image = Assert.Single(Descendants(view).OfType<Image>());
        Assert.Null(image.Source);
        Assert.True(Application.Current.TryFindResource("ButtonCustom", out var style));
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var attach = lib.GetType("HandyControl.Controls.ImageAttach", throwOnError: true)!;
        var dp = (DependencyProperty)attach.GetField("SourceFailedProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!.GetValue(null)!;
        var fallback = Assert.IsType<BitmapImage>(image.GetValue(dp));
        Assert.Equal("/Resources/Img/chrome_dragon.png", fallback.UriSource!.OriginalString);
        var frame = WasmMilCore.RenderHeadless(view, new WpfSize(300, 300));
        Assert.Empty(frame.OfType<DrawCommand.DrawImage>());
        Assert.Empty(frame.OfType<DrawCommand.DrawText>());
        Assert.NotEmpty(frame.OfType<DrawCommand.FillRect>());
    }

    [Fact]
    public void Staged_card_demo_instantiates_with_ten_card_rows()
    {
        // Slice-106 re-pin: rows inflate converted Cards (was ToString text).
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.CardDemo");
        var list = Assert.Single(Descendants(view).OfType<ListBox>());
        Assert.Equal(10, list.Items.Count);
        Assert.NotNull(list.ItemTemplate);
        Assert.True(Application.Current.TryFindResource(
            "FluidMoveBehaviorWrapPanelItemsPanelTemplate", out var shared));
        Assert.Same(shared, list.ItemsPanel);
        Assert.Equal(10, Descendants(view).Count(n => n.GetType().Name == "Card"));
        Assert.All(list.Items, row => Assert.True(row.HasTemplatedContent));
    }

    [Fact]
    public void Staged_chat_box_instantiates_past_audio_geometry()
    {
        // Slice-106 re-pin: the AudioGeometry frontier is resolved, so the
        // view instantiates (was XamlLoadException naming AudioGeometry).
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.ChatBox");
        Assert.True(Application.Current.TryFindResource("AudioGeometry", out _));
        Assert.Single(Descendants(view).OfType<ToggleButton>());
        Assert.Single(Descendants(view).OfType<ListBox>());
    }

    [Fact]
    public void Staged_notify_icon_demo_instantiates_with_tokens_commands_and_chrome_ink()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.NotifyIconDemo");
        var nodes = Descendants(view);
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var icons = nodes.Where(n => n.GetType().FullName == "HandyControl.Controls.NotifyIcon").ToList();
        Assert.Equal(2, icons.Count);
        var tokenProp = icons[0].GetType().GetProperty("Token")!;
        Assert.Equal("NotifyIconDemo", tokenProp.GetValue(icons[0]));
        var iconProp = icons[1].GetType().GetProperty("Icon")!;
        var icon = Assert.IsType<BitmapImage>(iconProp.GetValue(icons[1]));
        Assert.EndsWith("icon-white.ico", icon.UriSource!.OriginalString, StringComparison.Ordinal);
        var menu = Assert.Single(nodes.OfType<ContextMenu>());
        Assert.Equal(3, menu.Items.Count);
        var interaction = lib.GetType("HandyControl.Interactivity.Interaction", throwOnError: true)!;
        var triggers = (System.Collections.IList)interaction.GetMethod("GetTriggers")!
            .Invoke(null, [icons[0]])!;
        Assert.Equal(2, triggers.Count);
        var push = lib.GetType("HandyControl.Interactivity.ControlCommands", throwOnError: true)!
            .GetProperty("PushMainWindow2Top")!.GetValue(null);
        var firstActions = (System.Collections.IList)triggers[0]!.GetType()
            .GetProperty("Actions")!.GetValue(triggers[0])!;
        Assert.Same(push, firstActions[0]!.GetType().GetProperty("Command")!.GetValue(firstActions[0]));
        var frame = WasmMilCore.RenderHeadless(view, new WpfSize(800, 600));
        var texts = frame.OfType<DrawCommand.DrawText>().ToList();
        Assert.NotEmpty(texts);
        Assert.All(texts, t => Assert.False(string.IsNullOrEmpty(t.Text)));
        Assert.NotEmpty(frame.OfType<DrawCommand.DrawLine>());
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
