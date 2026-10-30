// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Collections.ObjectModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-106 HandyControl ChatBox/CardDemo row templating.
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (absent System.Security.Permissions + System.Windows.Extensions,
/// both with no browser path; Convert map untouched, staged bins
/// sha256-verified, manifests untouched).
/// (a) ChatBox line-39 AudioGeometry: lib-only geometry key with no
/// in-workspace source. Strategy is geometry-data with staged-verbatim
/// evidence: the exact upstream MIT Themes/Theme.xaml record also appears
/// byte-verbatim inside the staged HandyControl image (closing the slice-105
/// BAML string-table attribution gap by contiguous-bytes evidence), and the
/// key name matches the converted lib's own
/// HandyControl.Data.ResourceToken.AudioGeometry string constant (the
/// lib-surface key evidence). The fallback carries the real path data like
/// the LogoGeometry/ErrorGeometry/KeyboardGeometry/ImageGeometry precedent.
/// (b) CardDemo legacy-ListBox row templating: generated rows inflate the
/// retained ItemTemplate per data item (converted hc:Card + Footer/Header
/// bindings; rows rendered ToString text on the documented boundary).
/// Root causes fixed (real WPF parity, shim-always-shim, never fabricated):
/// legacy ListBoxItem hosts templated content (measure/arrange inside the
/// ring, paints instead of ToString text, chrome and selection unchanged);
/// legacy ListBox inflates ItemTemplate per generated row (flat + grouped)
/// with the item as DataContext and resyncs when the template arrives after
/// the source; &lt;DataTemplate&gt; nests in property elements on any target
/// with a settable DataTemplate slot (the &lt;hc:Card.FooterTemplate&gt;
/// path); converted ContentControl subclasses parent XAML children through
/// [ContentProperty] (real WPF parity: the Card body Border/Image land in
/// the logical tree; paint stays honest-empty without the BAML-only theme);
/// legacy ListBox exposes its ItemContainerGenerator over the legacy sync
/// (Reset after every rebuild) and ItemsControl.get_ItemContainerGenerator
/// dispatches to it on legacy receivers (the converted ChatBox subscription
/// is well-typed upstream since framework ListBox IS an ItemsControl, but a
/// direct backing-field read would hand back whatever reference sits at that
/// offset and corrupt it on subscribe).
/// Advance pinned here, all reproduced against live staged bins via the
/// engine-native staged-bin boot oracle (status gate bypassed in-probe only)
/// then pinned by the new staged-oracle set: ChatBox instantiates (line-39
/// geometries land on the converted IconSwitchElement DPs by reference; a
/// live ChatInfoModel message inflates one bound ChatBubble row); CardDemo
/// instantiates with 10 data rows hosting 10 converted Cards (DataContext is
/// the CardModel, Footer/Header bindings land, Content Border parents with
/// its BitmapImage, FooterTemplate retained; rows paint no text ink on the
/// unstyled-control boundary). Remaining blockers: the line-17 SkinDefault
/// BAML-only stop and the 2 absent binaries (no browser path); the WrapPanel
/// ItemsPanelTemplate stays retained-but-unarranged (legacy vertical stack).
/// Slice-107 re-pin: the converted Card default template now applies (middle
/// presenter hosts the content Border, footer/header templates inflate and
/// paint) and WrapPanel hosts arrange in wrap flow (see
/// Slice107HandyCardWrapTests).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice106HandyChatCardTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    private sealed class NamedItem
    {
        public NamedItem(string name) => Name = name;
        public string Name { get; }
        public override string ToString() => "ToString:" + Name;
    }

    [Fact]
    public void AudioGeometry_fallback_resolves_parseable_geometry_with_ink()
    {
        Application.Current = new Application();
        Assert.True(Application.Current.TryFindResource("AudioGeometry", out var value));
        var geometry = Assert.IsAssignableFrom<Geometry>(value);
        var path = new Path { Data = geometry };
        var panel = new StackPanel();
        panel.AddChild(path);
        var frame = WasmMilCore.RenderHeadless(panel, new WpfSize(800, 600));
        Assert.NotEmpty(frame.OfType<DrawCommand.DrawGeometry>());
    }

    [Fact]
    public void Legacy_listbox_inflates_item_template_per_row()
    {
        Application.Current = new Application();
        var list = Assert.IsType<ListBox>(XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\">" +
            "<ListBox.ItemTemplate><DataTemplate><Border><TextBlock Text=\"{Binding Name}\" /></Border>" +
            "</DataTemplate></ListBox.ItemTemplate></ListBox>").Root);
        list.ItemsSource = new object[] { new NamedItem("Alpha"), new NamedItem("Beta") };
        Assert.Equal(2, list.Items.Count);
        foreach (var row in list.Items)
        {
            Assert.True(row.HasTemplatedContent);
            Assert.Equal(string.Empty, row.Content);
            var visual = Assert.IsAssignableFrom<FrameworkElement>(row.TemplatedContent);
            var item = Assert.IsType<NamedItem>(visual.DataContext);
            var text = Assert.Single(Descendants(visual).OfType<TextBlock>());
            Assert.Equal(item.Name, text.Text);
        }
        var frame = WasmMilCore.RenderHeadless(list, new WpfSize(800, 600));
        var texts = frame.OfType<DrawCommand.DrawText>().Select(d => d.Text).ToList();
        Assert.Contains("Alpha", texts);
        Assert.Contains("Beta", texts);
        Assert.DoesNotContain(texts, t => t.Contains("ToString:"));
    }

    [Fact]
    public void Legacy_listbox_without_template_keeps_to_string_rows()
    {
        Application.Current = new Application();
        var list = Assert.IsType<ListBox>(XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\" />").Root);
        list.ItemsSource = new object[] { new NamedItem("Alpha") };
        var row = Assert.Single(list.Items);
        Assert.False(row.HasTemplatedContent);
        Assert.Null(row.TemplatedContent);
        Assert.Equal("ToString:Alpha", row.Content);
        var frame = WasmMilCore.RenderHeadless(list, new WpfSize(800, 600));
        Assert.Contains(frame.OfType<DrawCommand.DrawText>().Select(d => d.Text), t => t == "ToString:Alpha");
    }

    [Fact]
    public void ItemTemplate_set_after_source_regenerates_rows()
    {
        Application.Current = new Application();
        var list = new ListBox { ItemsSource = new object[] { new NamedItem("Alpha") } };
        Assert.Equal("ToString:Alpha", Assert.Single(list.Items).Content);
        list.ItemTemplate = new DataTemplate(
            "<Border xmlns=\"" + PresentationNs + "\" />");
        var row = Assert.Single(list.Items);
        Assert.True(row.HasTemplatedContent);
        Assert.Equal(string.Empty, row.Content);
    }

    private sealed class FullGroup : CollectionViewGroup
    {
        public FullGroup(object? name, params object[] items)
            : base(name)
        {
            foreach (var item in items) Items.Add(item);
        }

        public override ObservableCollection<object> Items { get; } = new();
        public override int ItemCount => Items.Count;
    }

    private sealed class StubView : CollectionView
    {
        public StubView(ReadOnlyObservableCollection<object> groups)
            : base(Enumerable.Empty<object>())
        {
            OverrideGroups = groups;
        }

        public ReadOnlyObservableCollection<object> OverrideGroups { get; }

        public override ReadOnlyObservableCollection<object>? Groups => OverrideGroups;
    }

    [Fact]
    public void Grouped_rows_inflate_template()
    {
        Application.Current = new Application();
        var list = Assert.IsType<ListBox>(XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\">" +
            "<ListBox.ItemTemplate><DataTemplate><Border /></DataTemplate></ListBox.ItemTemplate>" +
            "</ListBox>").Root);
        list.GroupStyle.Add(new GroupStyle());
        var groups = new ReadOnlyObservableCollection<object>(new ObservableCollection<object>
        {
            new FullGroup("G", new NamedItem("Alpha")),
        });
        list.ItemsSource = new StubView(groups);
        var row = Assert.Single(list.Items);
        Assert.True(row.HasTemplatedContent);
        Assert.IsType<NamedItem>(((FrameworkElement)row.TemplatedContent!).DataContext);
    }

    [Fact]
    public void ContentPresenter_content_template_slot_accepts_data_template_element()
    {
        Application.Current = new Application();
        var presenter = Assert.IsType<ContentPresenter>(XamlLoader.Load(
            "<ContentPresenter xmlns=\"" + PresentationNs + "\" Content=\"hi\">" +
            "<ContentPresenter.ContentTemplate><DataTemplate><TextBlock Text=\"{Binding}\" />" +
            "</DataTemplate></ContentPresenter.ContentTemplate></ContentPresenter>").Root);
        Assert.NotNull(presenter.ContentTemplate);
    }

    [Fact]
    public void Legacy_generator_notifies_reset_on_sync()
    {
        Application.Current = new Application();
        var list = new ListBox();
        var seen = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        list.ItemContainerGenerator.ItemsChanged += (_, e) => seen.Add(e.Action);
        var source = new ObservableCollection<object> { new NamedItem("Alpha") };
        list.ItemsSource = source;
        Assert.Contains(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, seen);
        seen.Clear();
        source.Add(new NamedItem("Beta"));
        Assert.Contains(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, seen);
        Assert.Equal(2, list.Items.Count);
    }

    [Fact]
    public void ItemsControl_getter_dispatches_legacy_generator()
    {
        // Mirrors the converted ChatBox call shape exactly: callvirt against
        // ItemsControl.get_ItemContainerGenerator with a legacy ListBox
        // receiver (framework ListBox IS an ItemsControl, so the C# call is
        // well-typed upstream). A direct backing-field read would hand back
        // whatever reference sits at that offset and corrupt it on subscribe.
        Application.Current = new Application();
        var list = new ListBox();
        var probe = new System.Reflection.Emit.DynamicMethod(
            "Slice106GeneratorProbe", typeof(ItemContainerGenerator), [typeof(object)], typeof(Slice106HandyChatCardTests));
        var il = probe.GetILGenerator();
        il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
        il.Emit(System.Reflection.Emit.OpCodes.Callvirt,
            typeof(ItemsControl).GetProperty(nameof(ItemsControl.ItemContainerGenerator))!.GetMethod!);
        il.Emit(System.Reflection.Emit.OpCodes.Ret);
        var getter = (Func<object, ItemContainerGenerator>)probe.CreateDelegate(typeof(Func<object, ItemContainerGenerator>));
        Assert.Same(list.ItemContainerGenerator, getter(list));
        var real = new ItemsControl();
        Assert.Same(real.ItemContainerGenerator, getter(real));
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
            "KeyboardGeometry", "ImageGeometry", "AudioGeometry", "StringRepeatConverter",
            "Boolean2VisibilityReConverter", "IsInertiaEnabled", "ImageFailed",
            "RoutedEventTrigger", "ChatBubble", "Card", "ItemTemplate",
        })
            Assert.DoesNotContain(details, d => d.Contains(name));
        _ = entry;
    }

    [Fact]
    public void Staged_audio_geometry_matches_lib_surface_and_staged_bytes()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var token = lib.GetType("HandyControl.Data.ResourceToken", throwOnError: true)!;
        Assert.Equal("AudioGeometry", token.GetField("AudioGeometry")!.GetValue(null));
        Assert.True(Application.Current.TryFindResource("AudioGeometry", out var value));
        Assert.IsAssignableFrom<Geometry>(value);
        var marker = "<Geometry x:Key=\"AudioGeometry\">";
        var xaml = HandyTheme.Xaml;
        var start = xaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = xaml.IndexOf("</Geometry>", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var data = xaml[(start + marker.Length)..end];
        Assert.Equal(790, data.Length);
        Assert.StartsWith("M627.52666696 962", data, StringComparison.Ordinal);
        Assert.EndsWith("75.83999942z", data, StringComparison.Ordinal);
        var apps = StagedHandyAppsDirectory();
        var libFile = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")))
            .RootElement.GetProperty("assemblies").EnumerateArray()
            .First(a => a.GetProperty("name").GetString() == "HandyControl")
            .GetProperty("file").GetString()!;
        var libBytes = File.ReadAllBytes(System.IO.Path.Combine(apps, libFile));
        Assert.True(ContainsBytes(libBytes, System.Text.Encoding.UTF8.GetBytes(data)));
        _ = entry;
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length) return false;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return true;
        }
        return false;
    }

    [Fact]
    public void Staged_chat_box_instantiates_with_audio_geometries_and_bound_rows()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.ChatBox");
        Assert.True(Application.Current.TryFindResource("AudioGeometry", out var audio));
        Assert.True(Application.Current.TryFindResource("KeyboardGeometry", out var keyboard));
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var switchType = lib.GetType("HandyControl.Controls.IconSwitchElement", throwOnError: true)!;
        var geometryDp = (DependencyProperty)switchType.GetField(
            "GeometryProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!.GetValue(null)!;
        var selectedDp = (DependencyProperty)switchType.GetField(
            "GeometrySelectedProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!.GetValue(null)!;
        var toggle = Assert.Single(Descendants(view).OfType<ToggleButton>());
        Assert.Same(audio, toggle.GetValue(geometryDp));
        Assert.Same(keyboard, toggle.GetValue(selectedDp));
        var list = Assert.Single(Descendants(view).OfType<ListBox>());
        Assert.Equal(0, list.Items.Count);
        Assert.NotNull(list.ItemTemplate);
        Assert.NotNull(list.ItemContainerGenerator);
        var infoType = entry.GetType("HandyControlDemo.Data.ChatInfoModel", throwOnError: true)!;
        var info = Activator.CreateInstance(infoType)!;
        infoType.GetProperty("Message")!.SetValue(info, "hello slice-106");
        infoType.GetProperty("Role")!.SetValue(info, Enum.Parse(infoType.GetProperty("Role")!.PropertyType, "Sender"));
        infoType.GetProperty("Type")!.SetValue(info, Enum.Parse(infoType.GetProperty("Type")!.PropertyType, "String"));
        var infos = (System.Collections.IList)view.DataContext!.GetType()
            .GetProperty("ChatInfos")!.GetValue(view.DataContext)!;
        infos.Add(info);
        var row = Assert.Single(list.Items);
        Assert.True(row.HasTemplatedContent);
        Assert.Equal(string.Empty, row.Content);
        var bubble = Assert.Single(Descendants(view).Where(n => n.GetType().Name == "ChatBubble"));
        var bubbleData = ((FrameworkElement)bubble).DataContext;
        Assert.Equal(infoType.FullName, bubbleData?.GetType().FullName);
        foreach (var name in new[] { "Message", "Role", "Type" })
            Assert.Equal(
                infoType.GetProperty(name)!.GetValue(info)?.ToString(),
                bubbleData?.GetType().GetProperty(name)!.GetValue(bubbleData)?.ToString());
        Assert.Equal("Sender", bubble.GetType().GetProperty("Role")!.GetValue(bubble)!.ToString());
        Assert.Equal("String", bubble.GetType().GetProperty("Type")!.GetValue(bubble)!.ToString());
        Assert.Equal("hello slice-106", bubble.GetType().GetProperty("Content")!.GetValue(bubble));
        list.SelectedIndex = 0;
        Assert.Same(row, list.SelectedItem);
    }

    [Fact]
    public void Staged_card_demo_inflates_cards_with_footer_bindings()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.CardDemo");
        var list = Assert.Single(Descendants(view).OfType<ListBox>());
        Assert.Equal(10, list.Items.Count);
        Assert.NotNull(list.ItemTemplate);
        Assert.True(Application.Current.TryFindResource(
            "FluidMoveBehaviorWrapPanelItemsPanelTemplate", out var shared));
        Assert.Same(shared, list.ItemsPanel);
        var cards = Descendants(view).Where(n => n.GetType().Name == "Card").ToList();
        Assert.Equal(10, cards.Count);
        foreach (var card in cards)
        {
            var model = ((FrameworkElement)card).DataContext;
            Assert.Equal("HandyControlDemo.Data.CardModel", model?.GetType().FullName);
            var footer = (string?)model?.GetType().GetProperty("Footer")!.GetValue(model);
            Assert.False(string.IsNullOrEmpty(footer));
            Assert.Equal(footer, card.GetType().GetProperty("Footer")!.GetValue(card));
            Assert.IsType<DataTemplate>(card.GetType().GetProperty("FooterTemplate")!.GetValue(card));
            var border = Assert.IsType<Border>(card.GetType().GetProperty("Content")!.GetValue(card));
            // Slice-107 re-pin: the converted default template hosts the
            // content Border in the middle presenter, so the template root
            // (not the content Border) is the single logical child.
            var root = Assert.Single(((FrameworkElement)card).LogicalChildren);
            Assert.IsType<Border>(root);
            Assert.Contains(border, Descendants(card));
            var image = Assert.Single(Descendants(border).OfType<Image>());
            Assert.IsType<BitmapImage>(image.Source);
        }
        foreach (var row in list.Items)
        {
            Assert.True(row.HasTemplatedContent);
            Assert.Equal(string.Empty, row.Content);
        }
        var frame = WasmMilCore.RenderHeadless(view, new WpfSize(800, 600));
        Assert.DoesNotContain(frame.OfType<DrawCommand.DrawText>(), d => d.Text.Contains("CardModel"));
        list.SelectedIndex = 2;
        Assert.Same(list.Items[2], list.SelectedItem);
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
