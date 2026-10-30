using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-101 HandyControl EffectShadow4: the staged LeftMainContent
/// and MainContent views read {StaticResource EffectShadow4} on their root
/// Borders, but the key lives behind the line-17 lib SkinDefault BAML-only
/// stop (themes/skindefault.baml is a 585-byte merge root into the 466350-byte
/// themes/theme.baml; the engine BAML reader covers element content only).
/// Member evidence: the upstream lib source Themes/Basic/Effects.xaml
/// (HandyControl_Shared, MIT) is loose text carrying five DropShadowEffect
/// entries plus the EffectShadowColor color; EffectShadow4 is BlurRadius 25,
/// ShadowDepth 8, Direction 270, Color #88000000 (the EffectShadowColor
/// value), Opacity .2, RenderingBias Performance with o:Freeze="True". The
/// engine resource-entry reader already parses DropShadowEffect entries with
/// literal attributes only (see XamlLoader.TryReadEffectResourceEntry), so
/// the HandyTheme fallback carries EffectShadow4 with the color inlined and
/// the freeze hint omitted (options-namespace attributes are rejected in
/// resource entries; the engine Freezable default applies). Only EffectShadow4
/// ships: the smallest genuinely-advancing scope (siblings stay follow-ups).
/// Advance pinned here: LeftMainContent fully instantiates (its remaining
/// reads sit in uninflated DataTemplates) and MainContent moves to the
/// converted-lib Watermark element (line 16, read-time, ahead of the still
/// pending BorderClip style flush); the 2 absent binaries stay declared with
/// no browser path.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice101EffectShadowTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public Slice101EffectShadowTests()
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
    public void Fallback_carries_effect_shadow_with_upstream_values()
    {
        var dict = XamlLoader.LoadResourceDictionary(HandyTheme.Xaml);
        var shadow = Assert.IsType<DropShadowEffect>(dict["EffectShadow4"]);
        Assert.Equal(25, shadow.BlurRadius);
        Assert.Equal(8, shadow.ShadowDepth);
        Assert.Equal(270, shadow.Direction);
        Assert.Equal(Color.FromArgb(0x88, 0, 0, 0), shadow.Color);
        Assert.Equal(0.2, shadow.Opacity);
        Assert.Equal(RenderingBias.Performance, shadow.RenderingBias);
    }

    [Fact]
    public void App_scope_resolves_shadow_with_local_entries_winning()
    {
        Application.Current = new Application();
        Assert.True(Application.Current.TryFindResource("EffectShadow4", out var fallback));
        Assert.IsType<DropShadowEffect>(fallback);
        // A real merged effect always beats the fallback (last-resort order).
        var local = new DropShadowEffect { BlurRadius = 3 };
        Application.Current.Resources.Add("EffectShadow4", local);
        Assert.True(Application.Current.TryFindResource("EffectShadow4", out var winner));
        Assert.Same(local, winner);
    }

    [Fact]
    public void Effect_shadow_lands_on_border_from_xaml_static_resource()
    {
        Application.Current = new Application();
        var border = Assert.IsType<Border>(XamlLoader.Load(
            "<Border xmlns=\"" + PresentationNs + "\" " +
            "Effect=\"{StaticResource EffectShadow4}\" />").Root);
        var shadow = Assert.IsType<DropShadowEffect>(border.Effect);
        Assert.Equal(25, shadow.BlurRadius);
        Assert.Equal(8, shadow.ShadowDepth);
        Assert.Equal(0.2, shadow.Opacity);
        // The fallback instance is shared like any other resource entry.
        Assert.True(Application.Current.TryFindResource("EffectShadow4", out var shared));
        Assert.Same(shared, shadow);
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
            Assert.DoesNotContain("EffectShadow4", gap.GetProperty("detail").GetString()!);
        }
        _ = entry;
    }

    [Fact]
    public void Staged_left_main_content_instantiates_with_effect_shadow()
    {
        var entry = LoadStagedHandyControlEntry();
        // A shown view always has an Application (WPF startup path); the
        // theme-scope fallback only runs at application scope, like WPF.
        Application.Current = new Application();
        var type = entry.GetType("HandyControlDemo.UserControl.LeftMainContent", throwOnError: true)!;
        Assert.True(typeof(Border).IsAssignableFrom(type));
        var view = (Border)Activator.CreateInstance(type)!;
        var shadow = Assert.IsType<DropShadowEffect>(view.Effect);
        Assert.Equal(25, shadow.BlurRadius);
        Assert.Equal(8, shadow.ShadowDepth);
        Assert.Equal(270, shadow.Direction);
        Assert.Equal(Color.FromArgb(0x88, 0, 0, 0), shadow.Color);
        Assert.Equal(0.2, shadow.Opacity);
    }

    [Fact]
    public void Staged_main_content_instantiates_past_effect_shadow_and_watermark()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var type = entry.GetType("HandyControlDemo.UserControl.MainContent", throwOnError: true)!;
        Assert.True(typeof(Border).IsAssignableFrom(type));
        // Slice-102: the converted-lib Watermark (line 16) now resolves and
        // the BorderClip/TextBlockLargeBold application-scope fallbacks
        // flush, so MainContent shows instead of throwing.
        var view = Activator.CreateInstance(type);
        Assert.IsAssignableFrom<Border>(view);
        Assert.True(Application.Current!.TryFindResource("TextBlockLargeBold", out var style));
        Assert.IsType<Style>(style);
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
