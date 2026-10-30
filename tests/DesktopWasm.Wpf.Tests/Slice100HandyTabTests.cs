using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-100 HandyControl tab styles: the staged LeftMainContent
/// view reads {StaticResource TabControlInLine} (Style) and
/// {StaticResource TabItemTransparent} (ItemContainerStyle) on its TabControl
/// (line 14), but both keys live behind the line-17 lib SkinDefault
/// BAML-only stop. Member evidence: the staged HandyControl.g.resources
/// carries themes/skindefault.baml (585 bytes, merge root only) plus
/// themes/theme.baml (466350 bytes, the combined theme); TabControlInLine
/// sits in the theme BAML string table next to TabItemInLine (ANSI hit), and
/// the staged entry's loose Resources/Themes/Styles/Style.xaml defines
/// TabItemTransparent (TargetType TabItem, BasedOn the lib TabItemInLine
/// plus transparent Background) while the demo SkinDefault/Theme dictionaries
/// hold no TabControlInLine. The engine BAML reader covers element content
/// only (styles are a precise-error boundary), so the engine carries both
/// keys as honest chrome-less shims (empty styles, correct TargetTypes, the
/// FluentTheme.DefaultMenuItemStyle idiom; see HandyTheme), consulted last in
/// Application.TryFindResource so real styles always win. Real-parity engine
/// work in the same slice: TabControl.ItemContainerStyle (the WPF-inherited
/// ItemsControl slot the ItemContainerStyle attribute needs; ListBox already
/// owns it) applied to TabItem children existing and future with the tab's
/// own ancestry as the loader-order scope. Advance pinned here (re-pinned in
/// slice-101: EffectShadow4 now resolves through the same fallback and
/// Hydrate carries the root Effect onto the component, so the view that
/// stopped at EffectShadow4 now instantiates with the shadow landed):
/// LeftMainContent moves past both tab styles to StaticResource
/// 'EffectShadow4' (the root Border Effect, a lib-BAML key in the remaining
/// app-StaticResources chain); the 2 absent binaries stay declared with no
/// browser path.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice100HandyTabTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public Slice100HandyTabTests()
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
    public void Handy_fallback_carries_exactly_the_two_tab_keys()
    {
        var dict = XamlLoader.LoadResourceDictionary(HandyTheme.Xaml);
        var inline = Assert.IsType<Style>(dict["TabControlInLine"]);
        Assert.Same(typeof(TabControl), inline.TargetType);
        Assert.Empty(inline.Setters);
        Assert.Empty(inline.Triggers);
        var transparent = Assert.IsType<Style>(dict["TabItemTransparent"]);
        Assert.Same(typeof(TabItem), transparent.TargetType);
        Assert.Empty(transparent.Setters);
        Assert.Empty(transparent.Triggers);
    }

    [Fact]
    public void App_scope_resolves_tab_keys_with_local_entries_winning()
    {
        Application.Current = new Application();
        Assert.True(Application.Current.TryFindResource("TabControlInLine", out var inline));
        Assert.IsType<Style>(inline);
        Assert.True(Application.Current.TryFindResource("TabItemTransparent", out var transparent));
        Assert.IsType<Style>(transparent);
        // A real merged style always beats the fallback (last-resort order).
        var local = new Style(typeof(TabControl));
        Application.Current.Resources.Add("TabControlInLine", local);
        Assert.True(Application.Current.TryFindResource("TabControlInLine", out var winner));
        Assert.Same(local, winner);
    }

    [Fact]
    public void Unknown_keys_still_miss_like_wpf()
    {
        Application.Current = new Application();
        Assert.False(Application.Current.TryFindResource("TabControlNoSuchKey", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void TabControl_item_container_style_defaults_null()
    {
        var tabs = new TabControl();
        Assert.Null(tabs.ItemContainerStyle);
    }

    [Fact]
    public void TabControl_item_container_style_applies_to_existing_and_future_tab_items()
    {
        var tabs = new TabControl();
        var first = new TabItem { Header = "Alpha" };
        var second = new TabItem { Header = "Beta" };
        tabs.AddItem(first);
        var brush = new SolidColorBrush(Color.FromRgb(0x11, 0x22, 0x33));
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, brush));
        tabs.ItemContainerStyle = style;
        Assert.Same(style, tabs.ItemContainerStyle);
        Assert.Same(brush, first.Background);
        tabs.AddItem(second);
        Assert.Same(brush, second.Background);
        // Data items mint no TabItem in the engine (headers render from
        // data), so the style path leaves them alone.
        tabs.AddItem("Gamma");
        Assert.Equal(3, tabs.Items.Count);
    }

    [Fact]
    public void TabControl_style_slots_land_from_xaml_static_resources()
    {
        Application.Current = new Application();
        var tabs = Assert.IsType<TabControl>(XamlLoader.Load(
            "<TabControl xmlns=\"" + PresentationNs + "\" " +
            "Style=\"{StaticResource TabControlInLine}\" " +
            "ItemContainerStyle=\"{StaticResource TabItemTransparent}\" />").Root);
        Assert.True(Application.Current.TryFindResource("TabControlInLine", out var inline));
        Assert.True(Application.Current.TryFindResource("TabItemTransparent", out var transparent));
        // The Style slot applies immediately (no throw is the advance: this
        // load failed loud on TabControlInLine before the fallback); the
        // ItemContainerStyle slot stores the resolved instance.
        Assert.IsType<Style>(inline);
        Assert.Same(transparent, tabs.ItemContainerStyle);
    }

    [Fact]
    public void Local_tab_style_beats_the_fallback_like_wpf()
    {
        Application.Current = new Application();
        var tabs = Assert.IsType<TabControl>(XamlLoader.Load(
            "<TabControl xmlns=\"" + PresentationNs + "\" " +
            "Style=\"{StaticResource TabControlInLine}\">" +
            "<TabControl.Resources>" +
            "<Style x:Key=\"TabControlInLine\" TargetType=\"TabControl\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Setter Property=\"Background\" Value=\"#FF112233\" />" +
            "</Style>" +
            "</TabControl.Resources>" +
            "</TabControl>").Root);
        var fill = Assert.IsType<SolidColorBrush>(tabs.Background);
        Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), fill.Color);
    }

    [Fact]
    public void Mismatched_fallback_target_still_throws_like_wpf()
    {
        Application.Current = new Application();
        Assert.True(Application.Current.TryFindResource("TabControlInLine", out var inline));
        var button = new Button();
        var style = Assert.IsType<Style>(inline);
        var error = Assert.Throws<InvalidOperationException>(() => style.ApplyTo(button));
        Assert.Contains("cannot apply to <Button>", error.Message);
    }

    [Fact]
    public void Staged_manifest_keeps_two_absent_binary_gaps()
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
        foreach (var gap in gaps)
        {
            Assert.Equal("missing-dependency", gap.GetProperty("kind").GetString());
            Assert.Contains("absent from upstream build output", gap.GetProperty("detail").GetString()!);
            Assert.DoesNotContain("TabControlInLine", gap.GetProperty("detail").GetString()!);
            Assert.DoesNotContain("TabItemTransparent", gap.GetProperty("detail").GetString()!);
        }
        _ = entry;
    }

    [Fact]
    public void Staged_left_main_content_moves_past_tab_styles_to_effect_shadow()
    {
        var entry = LoadStagedHandyControlEntry();
        // A shown view always has an Application (WPF startup path); the
        // theme-scope fallback only runs at application scope, like WPF.
        // Re-pinned in slice-101: EffectShadow4 resolves through the same
        // fallback and Hydrate carries the root Effect onto the component,
        // so the view that stopped here now instantiates (the tab styles
        // still land: no tab fragment anywhere in the load path).
        Application.Current = new Application();
        var type = entry.GetType("HandyControlDemo.UserControl.LeftMainContent", throwOnError: true)!;
        Assert.True(typeof(Border).IsAssignableFrom(type));
        var view = (Border)Activator.CreateInstance(type)!;
        var shadow = Assert.IsType<DropShadowEffect>(view.Effect);
        Assert.Equal(25, shadow.BlurRadius);
        Assert.Equal(8, shadow.ShadowDepth);
    }

    [Fact]
    public void Staged_tab_slots_land_the_fallback_styles()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        XamlLoader.CustomElementSource = entry;
        var tabs = Assert.IsType<TabControl>(XamlLoader.Load(
            "<TabControl xmlns=\"" + PresentationNs + "\" " +
            "Style=\"{StaticResource TabControlInLine}\" " +
            "ItemContainerStyle=\"{StaticResource TabItemTransparent}\" />").Root);
        Assert.True(Application.Current.TryFindResource("TabControlInLine", out var inline));
        Assert.True(Application.Current.TryFindResource("TabItemTransparent", out var transparent));
        Assert.IsType<Style>(inline);
        Assert.Same(transparent, tabs.ItemContainerStyle);
        Assert.IsType<Style>(tabs.ItemContainerStyle);
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
}
