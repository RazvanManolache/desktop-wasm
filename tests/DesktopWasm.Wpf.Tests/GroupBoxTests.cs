using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery GroupBox slice (slice-32): the real converted WPFGallery
/// Layout/GroupBoxPage needs System.Windows.Controls.GroupBox (a bordered
/// header + content host). Before the slice, both the converter map and the
/// XAML loader reject it, so the page cannot inflate from staging.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class GroupBoxTests
{
    private static (WasmInputRouter Router, WpfInputManager Manager) Harness(UIElement content)
    {
        var window = new Window { Content = content };
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        manager.AddRoot(window);
        var mil = new WasmMilCore(new SkiaSurface(800, 600));
        mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        return (router, manager);
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
        Collect(root, found);
        return found;
    }

    private static void Collect<T>(FrameworkElement? root, List<T> found) where T : FrameworkElement
    {
        if (root is null) return;
        if (root is T match) found.Add(match);
        foreach (var child in root.LogicalChildren) Collect(child as FrameworkElement, found);
    }

    private static System.Reflection.Assembly LoadConvertedGallery()
    {
        var dir = FindGalleryDir();
        var manifest = System.IO.Path.Combine(dir, "app-manifest.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
        string? file = null;
        foreach (var asm in doc.RootElement.GetProperty("assemblies").EnumerateArray())
        {
            if (asm.GetProperty("name").GetString() == "WPFGallery")
                file = asm.GetProperty("file").GetString();
        }
        Assert.NotNull(file);
        var bytes = File.ReadAllBytes(System.IO.Path.Combine(dir, file!));
        return System.Reflection.Assembly.Load(bytes);
    }

    private static string FindGalleryDir()
    {
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var candidate = System.IO.Path.Combine(d.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "wpf-gallery");
            if (File.Exists(System.IO.Path.Combine(candidate, "app-manifest.json"))) return candidate;
        }
        throw new InvalidOperationException("staged wpf-gallery package not found");
    }

    [Fact]
    public void Real_GroupBoxPage_xaml_inflates_header_and_form()
    {
        var asm = LoadConvertedGallery();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("GroupBoxPage.xaml", StringComparison.Ordinal));
        Assert.NotNull(name);
        using var stream = asm.GetManifestResourceStream(name!);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = asm;
        try
        {
            var root = XamlLoader.Load(reader.ReadToEnd()).Root as FrameworkElement;
            var box = FindVisual<GroupBox>(root);
            Assert.NotNull(box);
            Assert.Equal("User Information", box!.Header?.ToString());
            Assert.True(box.HasHeader);
            var boxes = FindVisuals<TextBox>(box);
            Assert.Equal(2, boxes.Count);
            var submit = FindVisuals<Button>(box).FirstOrDefault(b => b.Content?.ToString() == "Submit");
            Assert.NotNull(submit);
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    [Fact]
    public void GroupBox_hosts_header_and_content_with_frame_layout()
    {
        var box = new GroupBox
        {
            Header = "User Information",
            Content = new TextBox { Text = "Ada" },
        };
        var window = new Window { Content = box };
        window.Measure(new WpfSize(400, 300));
        window.Arrange(new WpfRect(0, 0, 400, 300));
        Assert.True(box.HasHeader);
        Assert.True(box.DesiredSize.Width > 0);
        Assert.True(box.DesiredSize.Height > 0);
        var content = (TextBox)box.Content!;
        Assert.True(content.ArrangedRect.Width > 0);
        Assert.True(content.ArrangedRect.Height > 0);
        // Content sits inside the border, below the header caption.
        Assert.True(content.ArrangedRect.Y >= box.ArrangedRect.Y);
        Assert.True(content.ArrangedRect.X >= box.ArrangedRect.X);
    }

    [Fact]
    public void GroupBox_header_replacement_and_clear_follow_wpf()
    {
        var box = new GroupBox { Header = "First" };
        Assert.True(box.HasHeader);
        box.Header = "Second";
        Assert.Equal("Second", box.Header?.ToString());
        Assert.True(box.HasHeader);
        box.Header = null;
        Assert.False(box.HasHeader);
    }

    [Fact]
    public void GroupBox_renders_border_and_header_text()
    {
        var box = new GroupBox
        {
            Header = "User Information",
            Content = new TextBox { Text = "Ada" },
        };
        var window = new Window { Content = box };
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(400, 300));
        var json = WasmMilCore.ExportFrameJson(commands);
        Assert.Contains("User Information", json, StringComparison.Ordinal);
        Assert.Contains("Ada", json, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupBox_parses_from_xaml_attributes_and_nested_content()
    {
        const string xaml = """
            <GroupBox Header="User Information" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <StackPanel>
                <TextBox Text="Ada" />
                <Button Content="Submit" />
              </StackPanel>
            </GroupBox>
            """;
        var root = XamlLoader.Load(xaml).Root as GroupBox;
        Assert.NotNull(root);
        Assert.Equal("User Information", root!.Header?.ToString());
        var panel = root.Content as StackPanel;
        Assert.NotNull(panel);
        Assert.Single(FindVisuals<TextBox>(root));
        Assert.Single(FindVisuals<Button>(root));
    }
}
