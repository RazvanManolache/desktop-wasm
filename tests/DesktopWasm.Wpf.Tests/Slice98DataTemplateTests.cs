using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-98 TabControl content-template support: the LeftMainContent
/// staged view reaches TabControl.ContentTemplate (line 20-21) with a
/// DataTemplate the loader rejected ("Unknown element &lt;DataTemplate&gt;").
/// Member evidence via a live net9.0-windows WPF probe (see /tmp/wpfprobe):
/// TabControl declares settable ContentTemplate/ContentTemplateSelector DPs
/// defaulting to null plus read-only computed
/// SelectedContentTemplate (TabItem.ContentTemplate, else the TabControl
/// fallback); the engine keeps its settable SelectedContentTemplate
/// divergence (pinned by Slice67ChromeContentTests) and adds the
/// Content-level pair with Selected-beats-Content, template-beats-selector
/// resolution plus live inflation (fresh visual per selection, item as
/// DataContext, mirroring ItemsControl.PrepareContainerForItemOverride).
/// Advance pinned here: the loader accepts both template slots, the engine
/// renders templated page content, and the staged view moves to its next
/// precise blocker (Interaction.Triggers, a Blend-style trigger surface that
/// stays out of scope); the 2 absent binaries stay declared.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice98DataTemplateTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public Slice98DataTemplateTests()
    {
        WpfAnimationClock.ResetForTests();
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
        WpfAnimationClock.ResetForTests();
    }

    private static DataTemplate XamlTemplate(string inner) =>
        new("<TextBlock xmlns=\"" + PresentationNs + "\" Text=\"" + inner + "\" />");

    private sealed class FixedSelector(DataTemplate template) : DataTemplateSelector
    {
        public override DataTemplate? SelectTemplate(object item, DependencyObject container) => template;
    }

    private static TabControl DataTabs(params object[] items)
    {
        var tabs = new TabControl { Width = 400, Height = 200 };
        foreach (var item in items) tabs.AddItem(item);
        return tabs;
    }

    [Fact]
    public void Content_template_dp_identity_matches_wpf()
    {
        Assert.Same(typeof(DataTemplate), TabControl.ContentTemplateProperty.PropertyType);
        Assert.Same(typeof(TabControl), TabControl.ContentTemplateProperty.OwnerType);
        Assert.Null(TabControl.ContentTemplateProperty.DefaultValue);
        Assert.Same(typeof(DataTemplateSelector), TabControl.ContentTemplateSelectorProperty.PropertyType);
        Assert.Same(typeof(TabControl), TabControl.ContentTemplateSelectorProperty.OwnerType);
        Assert.Null(TabControl.ContentTemplateSelectorProperty.DefaultValue);
        Assert.Same(TabControl.ContentTemplateProperty,
            DependencyProperty.FromName(typeof(TabControl), "ContentTemplate"));
        Assert.Same(TabControl.ContentTemplateSelectorProperty,
            DependencyProperty.FromName(typeof(TabControl), "ContentTemplateSelector"));

        var tabs = new TabControl();
        Assert.Null(tabs.ContentTemplate);
        Assert.Null(tabs.ContentTemplateSelector);
    }

    [Fact]
    public void Content_template_parses_from_xaml_property_element()
    {
        const string xaml =
            "<TabControl xmlns=\"" + PresentationNs + "\">" +
            "<TabControl.ItemTemplate><DataTemplate><TextBlock Text=\"{Binding}\" /></DataTemplate></TabControl.ItemTemplate>" +
            "<TabControl.ContentTemplate><DataTemplate><TextBlock Text=\"page\" /></DataTemplate></TabControl.ContentTemplate>" +
            "</TabControl>";
        var tabs = Assert.IsType<TabControl>(XamlLoader.Load(xaml).Root);
        Assert.NotNull(tabs.ItemTemplate);
        Assert.NotNull(tabs.ContentTemplate);
        Assert.Null(tabs.SelectedContentTemplate);
        tabs.AddItem("Alpha");
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Equal("Alpha", tabs.SelectedContent);
    }

    [Fact]
    public void Selected_content_template_slot_parses_from_xaml()
    {
        const string xaml =
            "<TabControl xmlns=\"" + PresentationNs + "\">" +
            "<TabControl.SelectedContentTemplate><DataTemplate><TextBlock Text=\"sel\" /></DataTemplate></TabControl.SelectedContentTemplate>" +
            "</TabControl>";
        var tabs = Assert.IsType<TabControl>(XamlLoader.Load(xaml).Root);
        Assert.NotNull(tabs.SelectedContentTemplate);
        Assert.Null(tabs.ContentTemplate);
    }

    [Fact]
    public void Content_template_slots_reject_bad_shapes()
    {
        const string empty =
            "<TabControl xmlns=\"" + PresentationNs + "\">" +
            "<TabControl.ContentTemplate />" +
            "</TabControl>";
        var emptyError = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(empty));
        Assert.Contains("<TabControl.ContentTemplate/> is empty", emptyError.Message);

        const string nonTemplate =
            "<TabControl xmlns=\"" + PresentationNs + "\">" +
            "<TabControl.ContentTemplate><TextBlock Text=\"x\" /></TabControl.ContentTemplate>" +
            "</TabControl>";
        var nestedError = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(nonTemplate));
        Assert.Contains("<TabControl.ContentTemplate> needs a nested <DataTemplate>", nestedError.Message);

        const string doubled =
            "<TabControl xmlns=\"" + PresentationNs + "\">" +
            "<TabControl.SelectedContentTemplate><DataTemplate><TextBlock Text=\"a\" /></DataTemplate>" +
            "<DataTemplate><TextBlock Text=\"b\" /></DataTemplate></TabControl.SelectedContentTemplate>" +
            "</TabControl>";
        var doubledError = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(doubled));
        Assert.Contains("Malformed <TabControl.SelectedContentTemplate>", doubledError.Message);
    }

    [Fact]
    public void Content_template_inflates_data_content_with_datacontext()
    {
        var tabs = DataTabs("Alpha");
        tabs.ContentTemplate = XamlTemplate("picked");
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Equal("Alpha", tabs.SelectedContent);

        tabs.Measure(new WpfSize(400, 200));
        tabs.Arrange(new WpfRect(0, 0, 400, 200));
        // Data items also mint Label shells (ItemsControl generation); the
        // templated visual is the TextBlock carrying the template text.
        var hosted = tabs.LogicalChildren.OfType<TextBlock>().Single(t => t.Text == "picked");
        Assert.Equal("Alpha", hosted.DataContext);

        var commands = WasmMilCore.RenderHeadless(new Window { Content = tabs }, new WpfSize(400, 200));
        Assert.Contains(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "picked");
    }

    [Fact]
    public void Selected_content_template_wins_over_content_template()
    {
        var tabs = DataTabs("Alpha");
        tabs.ContentTemplate = XamlTemplate("content");
        tabs.SelectedContentTemplate = XamlTemplate("selected");

        var commands = WasmMilCore.RenderHeadless(new Window { Content = tabs }, new WpfSize(400, 200));
        Assert.Contains(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "selected");
        Assert.DoesNotContain(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "content");
    }

    [Fact]
    public void Content_template_selector_used_when_no_fixed_template()
    {
        var tabs = DataTabs(7);
        tabs.ContentTemplateSelector = new FixedSelector(XamlTemplate("seven"));

        var commands = WasmMilCore.RenderHeadless(new Window { Content = tabs }, new WpfSize(400, 200));
        Assert.Contains(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "seven");
    }

    [Fact]
    public void Fixed_content_template_beats_selector()
    {
        var tabs = DataTabs(7);
        tabs.ContentTemplate = XamlTemplate("fixed");
        tabs.ContentTemplateSelector = new FixedSelector(XamlTemplate("picked"));

        var commands = WasmMilCore.RenderHeadless(new Window { Content = tabs }, new WpfSize(400, 200));
        Assert.Contains(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "fixed");
        Assert.DoesNotContain(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "picked");
    }

    [Fact]
    public void Selection_change_reinflates_with_new_datacontext()
    {
        var tabs = DataTabs("Alpha", "Beta");
        tabs.ContentTemplate = XamlTemplate("page");
        tabs.Measure(new WpfSize(400, 200));
        tabs.Arrange(new WpfRect(0, 0, 400, 200));
        var first = tabs.LogicalChildren.OfType<TextBlock>().Single(t => t.Text == "page");
        Assert.Equal("Alpha", first.DataContext);

        tabs.SelectedIndex = 1;
        tabs.Measure(new WpfSize(400, 200));
        tabs.Arrange(new WpfRect(0, 0, 400, 200));
        var second = tabs.LogicalChildren.OfType<TextBlock>().Single(t => t.Text == "page");
        Assert.NotSame(first, second);
        Assert.Equal("Beta", second.DataContext);
    }

    [Fact]
    public void Visual_content_hosts_directly_ignoring_template()
    {
        var page = new TextBlock { Text = "live" };
        var tabs = DataTabs(page);
        tabs.ContentTemplate = XamlTemplate("picked");

        var commands = WasmMilCore.RenderHeadless(new Window { Content = tabs }, new WpfSize(400, 200));
        Assert.Contains(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "live");
        Assert.DoesNotContain(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "picked");
        Assert.Contains(tabs.LogicalChildren.OfType<TextBlock>(), t => ReferenceEquals(t, page));
    }

    [Fact]
    public void Repeat_sync_keeps_live_templated_visual()
    {
        var tabs = DataTabs("Alpha");
        tabs.ContentTemplate = XamlTemplate("page");
        tabs.Measure(new WpfSize(400, 200));
        tabs.Arrange(new WpfRect(0, 0, 400, 200));
        var first = tabs.LogicalChildren.OfType<TextBlock>().Single(t => t.Text == "page");

        tabs.AddItem("Gamma");
        var kept = tabs.LogicalChildren.OfType<TextBlock>().Single(t => t.Text == "page");
        Assert.Same(first, kept);
        Assert.Equal("Alpha", kept.DataContext);
    }

    [Fact]
    public void Staged_left_main_content_moves_past_data_template()
    {
        var entry = LoadStagedHandyControlEntry();
        var type = entry.GetType("HandyControlDemo.UserControl.LeftMainContent", throwOnError: true)!;
        Assert.True(typeof(Border).IsAssignableFrom(type));
        var error = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(type));
        var load = Assert.IsType<XamlLoadException>(error.InnerException);
        Assert.Contains("StaticResource 'TabControlInLine' was not found", load.Message);
        Assert.DoesNotContain("<Interaction.Triggers> does not belong to <TabControl>", load.Message);
        Assert.DoesNotContain("Unknown element <DataTemplate>", load.Message);
        Assert.DoesNotContain("is not supported (Window/Page/Application/UserControl/Border only)", load.Message);
        for (var inner = error.InnerException; inner is not null; inner = inner.InnerException)
            Assert.IsNotType<MissingFieldException>(inner);
    }

    [Fact]
    public void Staged_manifest_keeps_two_absent_binary_gaps()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var apps = System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol");
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
        var assemblies = document.RootElement.GetProperty("assemblies").EnumerateArray().ToList();
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
        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().ToList();
        Assert.Equal(2, gaps.Count);
        foreach (var gap in gaps)
        {
            Assert.Equal("missing-dependency", gap.GetProperty("kind").GetString());
            Assert.Contains("absent from upstream build output", gap.GetProperty("detail").GetString());
        }
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
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(
                directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var apps = System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol");
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
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var apps = System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol");
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
