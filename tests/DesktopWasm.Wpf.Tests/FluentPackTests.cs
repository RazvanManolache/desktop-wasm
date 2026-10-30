using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Wpf;
using Microsoft.Win32;

namespace DesktopWasm.Wpf.Tests;

[Collection("ApplicationState")]

/// <summary>
/// Fluent theme pack slice (slice-25): the PresentationFramework.Fluent
/// pack never ships to the browser, so the engine stages its text in every
/// application's pack store and falls back to its keys at application
/// scope (like WPF's theme scope). Converted Fluent-era apps (the real
/// WPFGallery App.xaml merges that pack first) keep resolving theme keys
/// through DynamicResource/BasedOn/TemplateBinding chains, and stale
/// subscribers outliving their merges fail soft instead of throwing.
/// </summary>
[ResetAmbientState]
public sealed class FluentPackTests : IDisposable
{
    private const string FluentPackUri =
        "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml";

    private const string Ns =
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private readonly Application? _previous = Application.Current;

    public void Dispose() => Application.Current = _previous;

    [Fact]
    public void Fresh_application_resolves_fluent_theme_keys()
    {
        var app = new Application();
        Assert.NotNull(app.FindResource("AcrylicBackgroundFillColorDefault"));
        Assert.True(app.TryFindResource("SystemAccentColorPrimaryBrush", out var accent));
        Assert.NotNull(accent);
        Assert.True(app.TryFindResource("BooleanToVisibilityConverter", out var converter));
        Assert.IsAssignableFrom<IValueConverter>(converter);
        // Non-theme misses stay misses.
        Assert.False(app.TryFindResource("NoSuchThemeKey", out _));
        Assert.Throws<InvalidOperationException>(() => app.FindResource("NoSuchThemeKey"));
    }

    [Fact]
    public void Fresh_application_stages_fluent_pack_text()
    {
        var app = new Application();
        var xaml = app.GetPackResource(FluentPackUri);
        Assert.NotNull(xaml);
        Assert.Contains("AcrylicBackgroundFillColorDefault", xaml, StringComparison.Ordinal);
        var info = Application.GetResourceStream(new Uri(FluentPackUri));
        Assert.NotNull(info?.Stream);
        Assert.True(info.Stream.Length > 0);
    }

    [Fact]
    public void Fluent_source_merge_matches_gallery_app_boot()
    {
        var previous = Application.Current;
        try
        {
            var app = new Application();
            const string xaml =
                "<Application.Resources " + Ns + ">" +
                "<ResourceDictionary><ResourceDictionary.MergedDictionaries>" +
                "<ResourceDictionary Source=\"" + FluentPackUri + "\"/>" +
                "</ResourceDictionary.MergedDictionaries></ResourceDictionary>" +
                "</Application.Resources>";
            using var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(xaml));
            reader.MoveToContent();
            XamlLoader.HydrateApplicationResources(app, reader);
            // Implicit Button style lands through the merge (gallery tiles
            // restyle through it) plus the named accent chain.
            Assert.True(app.Resources.TryGetValue(typeof(Button), out var implicitStyle));
            Assert.IsType<Style>(implicitStyle);
            Assert.True(app.TryFindResource("AccentButtonStyle", out var accent));
            Assert.IsType<Style>(accent);
        }
        finally { Application.Current = previous; }
    }

    [Fact]
    public void BasedOn_chain_applies_fluent_button_styles()
    {
        var previous = Application.Current;
        try
        {
            var app = new Application();
            const string resources =
                "<Application.Resources " + Ns + ">" +
                "<ResourceDictionary><ResourceDictionary.MergedDictionaries>" +
                "<ResourceDictionary Source=\"" + FluentPackUri + "\"/>" +
                "</ResourceDictionary.MergedDictionaries></ResourceDictionary>" +
                "</Application.Resources>";
            using var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(resources));
            reader.MoveToContent();
            XamlLoader.HydrateApplicationResources(app, reader);
            // AccentButtonStyle BasedOn DefaultButtonStyle BasedOn {x:Type
            // Button}: the derived style wins with the accent fill.
            var window = (Window)XamlLoader.Load(
                "<Window " + Ns + "><Button Style=\"{StaticResource AccentButtonStyle}\" /></Window>").Root;
            var button = Assert.IsType<Button>(window.Content);
            Assert.Equal(0x0078D4FFu, button.BackgroundRgba);
        }
        finally { Application.Current = previous; }
    }

    [Fact]
    public void DynamicResource_fluent_key_resolves_and_local_override_wins()
    {
        var previous = Application.Current;
        try
        {
            var app = new Application();
            var window = (Window)XamlLoader.Load(
                "<Window " + Ns + "><TextBlock Text=\"t\" Foreground=\"{DynamicResource ControlFillColorDefaultBrush}\" /></Window>").Root;
            var text = Assert.IsType<TextBlock>(window.Content);
            Assert.Equal(0xF9F9F9FFu, text.ForegroundRgba);
            app.Resources.Add("ControlFillColorDefaultBrush", "#FF112233");
            Assert.Equal(0x112233FFu, text.ForegroundRgba);
        }
        finally { Application.Current = previous; }
    }

    [Fact]
    public void Stale_subscriber_on_fresh_app_resolves_theme_keys()
    {
        // Converted HeaderTile refresh pattern: a SystemEvents handler calls
        // Application.FindResource for a theme key. When the handler outlives
        // its merged dictionaries (a fresh Application), the lookup must
        // still resolve instead of throwing.
        var previous = Application.Current;
        void OnChanged(object sender, UserPreferenceChangedEventArgs e) =>
            Application.Current!.FindResource("AcrylicBackgroundFillColorDefault");
        SystemEvents.UserPreferenceChanged += OnChanged;
        try
        {
            _ = new Application();
            var raised = 0;
            void Count(object sender, UserPreferenceChangedEventArgs e) => raised++;
            SystemEvents.UserPreferenceChanged += Count;
            try
            {
                SystemEvents.RaiseUserPreferenceChanged(UserPreferenceCategory.General);
            }
            finally
            {
                SystemEvents.UserPreferenceChanged -= Count;
            }
            Assert.Equal(1, raised);
        }
        finally
        {
            SystemEvents.UserPreferenceChanged -= OnChanged;
            Application.Current = previous;
        }
    }

    [Fact]
    public async Task Gallery_boots_navigates_and_checkbox_flips_sample_button()
    {
        var previous = Application.Current;
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                baseDir, "..", "..", "..", "..", ".."));
            var dir = System.IO.Path.Combine(root, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "wpf-gallery");
            var json = await File.ReadAllTextAsync(System.IO.Path.Combine(dir, "app-manifest.json"));
            var package = JsonSerializer.Deserialize<ApplicationPackage>(json,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            Assert.NotNull(package);
            using var loader = await ApplicationPackageLoader.CreateAsync(package!, "wpf-gallery",
                file => Task.FromResult(File.ReadAllBytes(System.IO.Path.Combine(dir, file))));
            var window = loader.Boot();
            Assert.NotNull(window);

            window.Measure(new WpfSize(800, 600));
            window.Arrange(new WpfRect(0, 0, 800, 600));

            // Home: the session Frame shows the dashboard.
            var frame = FindVisual<Frame>(window.Content as FrameworkElement);
            Assert.NotNull(frame);
            Assert.Equal("WPFGallery.Views.DashboardPage", frame.Content?.GetType().FullName);

            // Section: pick "Basic Input" in the nav tree.
            var tree = FindVisual<TreeView>(window.Content as FrameworkElement);
            Assert.NotNull(tree);
            var basicData = ((ItemsControl)tree).Items.Cast<object?>()
                .FirstOrDefault(d => d?.ToString() == "Basic Input");
            Assert.NotNull(basicData);
            tree.SelectedItem = basicData;

            // Category page: the Button card command navigates the Frame.
            var categoryFrame = FindVisual<Frame>(window.Content as FrameworkElement);
            Assert.NotNull(categoryFrame);
            var card = FindVisuals<Button>(categoryFrame.Content as FrameworkElement)
                .FirstOrDefault(b => (b.CommandParameter?.ToString() ?? "").Contains("BasicInputPage"));
            Assert.NotNull(card);
            Assert.NotNull(card.Command);
            card.Command.Execute(card.CommandParameter);
            Assert.Equal("WPFGallery.Views.BasicInputPage", categoryFrame.Content?.GetType().FullName);

            // Detail page: the Button card command lands on ButtonPage.
            var detail = FindVisuals<Button>(categoryFrame.Content as FrameworkElement)
                .FirstOrDefault(b => (b.CommandParameter?.ToString() ?? "").Contains("ButtonPage"));
            Assert.NotNull(detail);
            Assert.NotNull(detail.Command);
            detail.Command.Execute(detail.CommandParameter);
            var page = categoryFrame.Content as FrameworkElement;
            Assert.NotNull(page);
            Assert.Equal("WPFGallery.Views.ButtonPage", page.GetType().FullName);

            // TemplateBinding chain: ControlExample templates inflate with
            // live sample content.
            var examples = FindVisualsByName(page, "WPFGallery.Controls.ControlExample");
            Assert.NotEmpty(examples);
            Assert.Contains("Simple Button", string.Join("|", CollectVisualText(page).Take(8)));

            // Interaction chain: the page checkbox command flips the sample
            // button through FindAncestor ViewModel bindings.
            var sample = FindVisuals<Button>(page)
                .FirstOrDefault(b => b.Content?.ToString() == "Standard WPF button");
            Assert.NotNull(sample);
            Assert.True(sample.IsEnabled);
            var check = FindVisuals<CheckBox>(page).FirstOrDefault();
            Assert.NotNull(check);
            Assert.True(check.IsChecked != true);
            // User press semantics: the toggle lands first, then Click runs
            // the bound checkbox command with the box as its parameter.
            check.IsChecked = true;
            check.RaiseClick();
            Assert.True(check.IsChecked == true);
            Assert.False(sample.IsEnabled);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    private static T? FindVisual<T>(FrameworkElement? root) where T : FrameworkElement
    {
        if (root is null) return null;
        if (root is T match) return match;
        foreach (var child in root.LogicalChildren)
        {
            var found = FindVisual<T>(child as FrameworkElement);
            if (found is not null) return found;
        }
        return null;
    }

    private static List<T> FindVisuals<T>(FrameworkElement? root) where T : FrameworkElement
    {
        var found = new List<T>();
        CollectVisuals(root, found);
        return found;
    }

    private static void CollectVisuals<T>(FrameworkElement? root, List<T> found) where T : FrameworkElement
    {
        if (root is null) return;
        if (root is T match) found.Add(match);
        foreach (var child in root.LogicalChildren) CollectVisuals(child as FrameworkElement, found);
    }

    private static List<FrameworkElement> FindVisualsByName(FrameworkElement? root, string fullName)
    {
        var found = new List<FrameworkElement>();
        CollectByName(root, fullName, found);
        return found;
    }

    private static void CollectByName(FrameworkElement? root, string fullName, List<FrameworkElement> found)
    {
        if (root is null) return;
        if (root.GetType().FullName == fullName) found.Add(root);
        foreach (var child in root.LogicalChildren) CollectByName(child as FrameworkElement, fullName, found);
    }

    private static List<string> CollectVisualText(FrameworkElement? root)
    {
        var found = new List<string>();
        CollectVisualTextInto(root, found);
        return found;
    }

    private static void CollectVisualTextInto(DependencyObject? node, List<string> found)
    {
        if (node is null) return;
        if (node is TextBlock tb && !string.IsNullOrEmpty(tb.Text)) found.Add(tb.Text);
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++) CollectVisualTextInto(VisualTreeHelper.GetChild(node, i), found);
    }
}
