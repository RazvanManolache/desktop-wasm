// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-104 HandyControl dialog ButtonPrimary/Locator/ErrorGeometry:
/// the staged InteractiveDialog view stopped at the ButtonPrimary read
/// (line 15, ahead of the pending line-10 Locator flush) and both staged
/// TextDialog views at the ErrorGeometry icon lookup. Member evidence via
/// the upstream HandyControl sources (HandyControl_Shared, MIT, loose text):
/// Themes/Theme.xaml carries ButtonPrimary (TargetType Button, BasedOn the
/// BAML-only ButtonBaseStyle, Background/BorderBrush over DynamicResource
/// PrimaryBrush) and the ErrorGeometry path table entry; the demo App.xaml
/// declares the ViewModelLocator under key "Locator", which never hydrates
/// behind the line-17 SkinDefault BAML-only stop. ButtonPrimary lands as an
/// empty chrome-less shim with TargetType Button (the slice-100 idiom);
/// ErrorGeometry carries the real upstream path data (the LogoGeometry
/// precedent); "Locator" instantiates the real converted
/// HandyControlDemo.ViewModel.ViewModelLocator from the staged entry with
/// its real SimpleIoc registrations (the slice-41 converted view-model
/// precedent). Advance pinned here: InteractiveDialog, TextDialog and
/// TextDialogWithTimer instantiate with an Application; the line-17
/// SkinDefault BAML-only stop and the 2 absent binaries
/// (System.Security.Permissions / System.Windows.Extensions) stay declared
/// with no browser path.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice104HandyDialogTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public Slice104HandyDialogTests()
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
    public void Fallback_carries_button_primary_shim_with_correct_target()
    {
        var dict = XamlLoader.LoadResourceDictionary(HandyTheme.Xaml);
        var primary = Assert.IsType<Style>(dict["ButtonPrimary"]);
        Assert.Same(typeof(Button), primary.TargetType);
        Assert.Empty(primary.Setters);
    }

    [Fact]
    public void Fallback_carries_error_geometry_with_upstream_data()
    {
        var dict = XamlLoader.LoadResourceDictionary(HandyTheme.Xaml);
        var geometry = Assert.IsAssignableFrom<Geometry>(dict["ErrorGeometry"]);
        Assert.NotNull(geometry);
    }

    [Fact]
    public void App_scope_resolves_new_keys_with_local_entries_winning()
    {
        Application.Current = new Application();
        Assert.True(Application.Current.TryFindResource("ButtonPrimary", out var primary));
        Assert.IsType<Style>(primary);
        Assert.True(Application.Current.TryFindResource("ErrorGeometry", out var error));
        Assert.IsAssignableFrom<Geometry>(error);
        var local = new SolidColorBrush(Color.FromArgb(0xFF, 0, 0, 0));
        Application.Current.Resources.Add("ButtonPrimary", local);
        Assert.True(Application.Current.TryFindResource("ButtonPrimary", out var winner));
        Assert.Same(local, winner);
        Assert.False(Application.Current.TryFindResource("NoSuchHandyKey104", out _));
    }

    [Fact]
    public void Button_primary_and_error_geometry_land_from_xaml()
    {
        Application.Current = new Application();
        var button = Assert.IsType<Button>(XamlLoader.Load(
            "<Button xmlns=\"" + PresentationNs + "\" " +
            "Style=\"{StaticResource ButtonPrimary}\" />").Root);
        Assert.True(Application.Current.TryFindResource("ButtonPrimary", out var shared));
        Assert.Same(typeof(Button), Assert.IsType<Style>(shared).TargetType);
        var path = Assert.IsType<Path>(XamlLoader.Load(
            "<Path xmlns=\"" + PresentationNs + "\" " +
            "Data=\"{StaticResource ErrorGeometry}\" />").Root);
        Assert.True(Application.Current.TryFindResource("ErrorGeometry", out var sharedGeometry));
        Assert.Same(sharedGeometry, Assert.IsAssignableFrom<Geometry>(path.Data));
    }

    [Fact]
    public void Locator_resolves_to_converted_view_model_locator()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        Assert.True(Application.Current.TryFindResource("Locator", out var locator));
        Assert.NotNull(locator);
        Assert.Equal("HandyControlDemo.ViewModel.ViewModelLocator", locator.GetType().FullName);
        Assert.Same(entry, locator.GetType().Assembly);
        Assert.True(Application.Current.TryFindResource("Locator", out var again));
        Assert.Same(locator, again);
        Assert.False(Application.Current.TryFindResource("locator", out _));
        Assert.False(Application.Current.TryFindResource("NoSuchLocator104", out _));
    }

    [Fact]
    public void Locator_misses_without_element_source()
    {
        LoadStagedHandyControlEntry();
        XamlLoader.CustomElementSource = null;
        Application.Current = new Application();
        Assert.False(Application.Current.TryFindResource("Locator", out _));
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
        foreach (var name in new[] { "ButtonPrimary", "ErrorGeometry", "Locator" })
            Assert.DoesNotContain(details, d => d.Contains(name));
        _ = entry;
    }

    [Fact]
    public void Staged_interactive_dialog_instantiates_with_locator_data_context()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.InteractiveDialog");
        Assert.IsAssignableFrom<Border>(view);
        var context = view.DataContext;
        Assert.NotNull(context);
        Assert.Equal("HandyControlDemo.ViewModel.InteractiveDialogViewModel", context.GetType().FullName);
        var nodes = Descendants(view);
        Assert.Contains(nodes, n => n is TextBox);
        var button = Assert.Single(nodes.OfType<Button>());
        Assert.True(Application.Current!.TryFindResource("ButtonPrimary", out var shared));
        Assert.Same(typeof(Button), Assert.IsType<Style>(shared).TargetType);
    }

    [Fact]
    public void Staged_text_dialog_instantiates_with_error_icon()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.TextDialog");
        Assert.IsAssignableFrom<Border>(view);
        var button = Assert.Single(Descendants(view).OfType<Button>());
        var owner = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("HandyControl.Controls.IconElement"))
            .FirstOrDefault(t => t is not null);
        Assert.NotNull(owner);
        var dp = Assert.IsAssignableFrom<DependencyProperty>(owner.GetField("GeometryProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null));
        Assert.True(Application.Current!.TryFindResource("ErrorGeometry", out var shared));
        Assert.Same(shared, button.GetValue(dp));
    }

    [Fact]
    public void Double_animation_to_value_ctors_carry_wpf_shape()
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(5000));
        var animation = new DoubleAnimation(100, duration);
        Assert.Equal(100, animation.To);
        Assert.Equal(duration, animation.Duration);
        Assert.Null(animation.From);
        var filled = new DoubleAnimation(100, duration, FillBehavior.Stop);
        Assert.Equal(100, filled.To);
        Assert.Equal(duration, filled.Duration);
        Assert.Equal(FillBehavior.Stop, filled.FillBehavior);
    }

    [Fact]
    public void Staged_text_dialog_with_timer_instantiates_with_locator_data_context()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.TextDialogWithTimer");
        Assert.IsAssignableFrom<Border>(view);
        var context = view.DataContext;
        Assert.NotNull(context);
        Assert.Equal("HandyControlDemo.ViewModel.InteractiveDialogViewModel", context.GetType().FullName);
        var nodes = Descendants(view);
        Assert.Contains(nodes, n => n is Button);
        Assert.Contains(nodes, n => n.GetType().Name == "CircleProgressBar");
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
