// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-103 HandyControl dialog/notification views: the staged
/// GeometryItem, AppNotification, InteractiveDialog, TextDialog and
/// TextDialogWithTimer views stopped at application-scope keys the slice-102
/// fallback already carried (Path4GeometryItem, LogoGeometry,
/// TextBlockLargeBold - the slice-95/96/97 app-less pins, unreachable without
/// an Application). Member evidence via the staged images themselves
/// (engine-native staged-bin oracle, status gate bypassed in-probe only) plus
/// the upstream HandyControl sources (HandyControl_Shared, MIT, loose text):
/// GeometryItem's root reads BorderCircular, AppNotification's caption reads
/// PrimaryBrush, InteractiveDialog's input reads TextBoxExtend, and the
/// EffectShadow1/2/3/5 siblings back BorderDemo/Avatar/ChatBox/CardDemo.
/// BorderCircular (TargetType Border) and TextBoxExtend (TargetType TextBox)
/// land as empty chrome-less shims (their lib setters chain through BAML-only
/// base styles, the slice-100 idiom); PrimaryBrush carries the real upstream
/// structure (two-stop LinearGradientBrush, StartPoint 0,0 EndPoint 1,0) with
/// the default-skin PrimaryColor/DarkPrimaryColor #326cf3 value inlined for
/// both stops (resource entries take literals only, the slice-101 idiom);
/// EffectShadow1/2/3/5 carry real upstream values (BlurRadius 5/8/14/35,
/// ShadowDepth 1/1.5/4.5/13, Direction 270, Color #88000000 inlined,
/// Opacity .2, RenderingBias Performance). Advance pinned here: GeometryItem
/// and AppNotification instantiate with an Application, InteractiveDialog
/// moves to ButtonPrimary (line 15, ahead of the still-pending line-10
/// Locator flush), TextDialog/TextDialogWithTimer move to ErrorGeometry; the
/// line-17 SkinDefault BAML-only stop and the 2 absent binaries
/// (System.Security.Permissions / System.Windows.Extensions) stay declared
/// with no browser path.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice103HandyViewsTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public Slice103HandyViewsTests()
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
    public void Fallback_carries_border_and_textbox_shims_with_correct_targets()
    {
        var dict = XamlLoader.LoadResourceDictionary(HandyTheme.Xaml);
        var circular = Assert.IsType<Style>(dict["BorderCircular"]);
        Assert.Same(typeof(Border), circular.TargetType);
        Assert.Empty(circular.Setters);
        var extend = Assert.IsType<Style>(dict["TextBoxExtend"]);
        Assert.Same(typeof(TextBox), extend.TargetType);
        Assert.Empty(extend.Setters);
    }

    [Fact]
    public void Fallback_carries_primary_brush_with_default_skin_values()
    {
        var dict = XamlLoader.LoadResourceDictionary(HandyTheme.Xaml);
        var brush = Assert.IsType<LinearGradientBrush>(dict["PrimaryBrush"]);
        Assert.Equal(new Point(0, 0), brush.StartPoint);
        Assert.Equal(new Point(1, 0), brush.EndPoint);
        Assert.Equal(2, brush.GradientStops.Count);
        Assert.Equal(Color.FromArgb(0xFF, 0x32, 0x6C, 0xF3), brush.GradientStops[0].Color);
        Assert.Equal(0, brush.GradientStops[0].Offset);
        Assert.Equal(Color.FromArgb(0xFF, 0x32, 0x6C, 0xF3), brush.GradientStops[1].Color);
        Assert.Equal(1, brush.GradientStops[1].Offset);
    }

    public static IEnumerable<object[]> ShadowSiblings() => new[]
    {
        new object[] { "EffectShadow1", 5.0, 1.0 },
        new object[] { "EffectShadow2", 8.0, 1.5 },
        new object[] { "EffectShadow3", 14.0, 4.5 },
        new object[] { "EffectShadow5", 35.0, 13.0 },
    };

    [Theory]
    [MemberData(nameof(ShadowSiblings))]
    public void Fallback_carries_shadow_siblings_with_upstream_values(string key, double blur, double depth)
    {
        var dict = XamlLoader.LoadResourceDictionary(HandyTheme.Xaml);
        var shadow = Assert.IsType<DropShadowEffect>(dict[key]);
        Assert.Equal(blur, shadow.BlurRadius);
        Assert.Equal(depth, shadow.ShadowDepth);
        Assert.Equal(270, shadow.Direction);
        Assert.Equal(Color.FromArgb(0x88, 0, 0, 0), shadow.Color);
        Assert.Equal(0.2, shadow.Opacity);
        Assert.Equal(RenderingBias.Performance, shadow.RenderingBias);
    }

    [Fact]
    public void App_scope_resolves_new_keys_with_local_entries_winning()
    {
        Application.Current = new Application();
        foreach (var key in new[] { "BorderCircular", "TextBoxExtend", "PrimaryBrush", "EffectShadow1", "EffectShadow2", "EffectShadow3", "EffectShadow5" })
            Assert.True(Application.Current.TryFindResource(key, out _), key);
        Assert.True(Application.Current.TryFindResource("PrimaryBrush", out var brush));
        Assert.IsType<LinearGradientBrush>(brush);
        // A real merged brush always beats the fallback (last-resort order).
        var local = new SolidColorBrush(Color.FromArgb(0xFF, 0, 0, 0));
        Application.Current.Resources.Add("PrimaryBrush", local);
        Assert.True(Application.Current.TryFindResource("PrimaryBrush", out var winner));
        Assert.Same(local, winner);
        Assert.False(Application.Current.TryFindResource("NoSuchHandyKey103", out _));
    }

    [Fact]
    public void Primary_brush_and_shadow_sibling_land_from_xaml_static_resource()
    {
        Application.Current = new Application();
        var text = Assert.IsType<TextBlock>(XamlLoader.Load(
            "<TextBlock xmlns=\"" + PresentationNs + "\" " +
            "Foreground=\"{StaticResource PrimaryBrush}\" />").Root);
        Assert.True(Application.Current.TryFindResource("PrimaryBrush", out var shared));
        Assert.Same(shared, Assert.IsType<LinearGradientBrush>(text.Foreground));
        var border = Assert.IsType<Border>(XamlLoader.Load(
            "<Border xmlns=\"" + PresentationNs + "\" " +
            "Effect=\"{StaticResource EffectShadow2}\" />").Root);
        var shadow = Assert.IsType<DropShadowEffect>(border.Effect);
        Assert.Equal(8, shadow.BlurRadius);
        Assert.Equal(1.5, shadow.ShadowDepth);
        Assert.True(Application.Current.TryFindResource("EffectShadow2", out var sharedShadow));
        Assert.Same(sharedShadow, shadow);
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
        foreach (var name in new[] { "BorderCircular", "PrimaryBrush", "TextBoxExtend", "EffectShadow1", "EffectShadow2", "EffectShadow3", "EffectShadow5" })
            Assert.DoesNotContain(details, d => d.Contains(name));
        _ = entry;
    }

    [Fact]
    public void Staged_geometry_item_instantiates_past_path_style()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.GeometryItem");
        Assert.IsAssignableFrom<Border>(view);
        // The root BorderCircular flush and the Path4GeometryItem content
        // style both land: the single Path child survives with the fallback
        // key resolving at application scope.
        var path = Assert.IsType<Path>(((Border)view).Child);
        Assert.True(Application.Current!.TryFindResource("BorderCircular", out var circular));
        Assert.Same(typeof(Border), Assert.IsType<Style>(circular).TargetType);
        Assert.True(Application.Current.TryFindResource("Path4GeometryItem", out var item));
        Assert.Same(typeof(Path), Assert.IsType<Style>(item).TargetType);
        // The item Path carries no Data of its own (unlike AppNotification's
        // mark); the styles land through the setter-application path, so the
        // advance pin is the instantiation plus the key/TargetType pins above.
        Assert.Null(path.Data);
    }

    [Fact]
    public void Staged_app_notification_instantiates_past_logo_geometry()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.AppNotification");
        Assert.IsAssignableFrom<Border>(view);
        var nodes = Descendants(view);
        // LogoGeometry lands on the mark Path (read-time Data, line 11).
        var mark = Assert.Single(nodes.OfType<Path>());
        Assert.IsAssignableFrom<Geometry>(mark.Data);
        // PrimaryBrush lands on the caption Foreground (line 12) by
        // reference, like any shared resource entry.
        var caption = Assert.Single(nodes.OfType<TextBlock>());
        Assert.True(Application.Current!.TryFindResource("PrimaryBrush", out var shared));
        Assert.Same(shared, Assert.IsType<LinearGradientBrush>(caption.Foreground));
        Assert.Contains(nodes, n => n is Button);
    }

    [Fact]
    public void Staged_interactive_dialog_instantiates()
    {
        // Slice-104 landed the ButtonPrimary read (line 15) and the
        // line-10 Locator flush, so the dialog instantiates; the detailed
        // DataContext pins live in Slice104HandyDialogTests.
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.InteractiveDialog");
        Assert.IsAssignableFrom<Border>(view);
        var nodes = Descendants(view);
        Assert.Contains(nodes, n => n is TextBox);
        Assert.Contains(nodes, n => n is Button);
    }

    [Theory]
    [InlineData("HandyControlDemo.UserControl.TextDialog")]
    [InlineData("HandyControlDemo.UserControl.TextDialogWithTimer")]
    public void Staged_text_dialogs_instantiate(string typeName)
    {
        // Slice-104 landed the converted ErrorGeometry icon lookup (plus
        // the Locator flush and the DoubleAnimation timer ctor), so both
        // dialogs instantiate; the detailed STATE pins live in
        // Slice104HandyDialogTests.
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, typeName);
        Assert.IsAssignableFrom<Border>(view);
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
