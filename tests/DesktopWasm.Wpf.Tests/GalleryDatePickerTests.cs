namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery DatePickerPage staged proof (slice-38). The real staged
/// DateAndTime/DatePickerPage.xaml hosts a bare &lt;DatePicker/&gt;, which the
/// XAML loader rejects as an unknown element. Before the slice the engine has
/// no DatePicker type at all; the remaining gallery boundary after this slice
/// is Interaction.Triggers, FrameWindow Source and RichTextBox.
/// Ordering note: this class deliberately sorts after ChromePackTests.
/// Assembly.Load of the converted gallery is AppDomain-persistent, and
/// Application.GetResourceStream intentionally resolves converted-assembly
/// manifest bytes as a fallback store; loading the gallery before
/// ChromePackTests.Gallery_control_example_load_pattern_reads_pack_stream
/// runs would make its absent-pack assertion see the real ButtonPage.xaml.
/// (Test discovery follows the SDK's alphabetical Compile glob, so the
/// Gallery* name keeps this class after Chrome*.)
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class GalleryDatePickerTests
{
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

    private static System.Reflection.Assembly LoadConvertedGallery()
    {
        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (loaded.GetName().Name == "WPFGallery") return loaded;
        }
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var candidate = System.IO.Path.Combine(d.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "wpf-gallery");
            if (File.Exists(System.IO.Path.Combine(candidate, "app-manifest.json")))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(
                    File.ReadAllText(System.IO.Path.Combine(candidate, "app-manifest.json")));
                string? file = null;
                foreach (var asm in doc.RootElement.GetProperty("assemblies").EnumerateArray())
                {
                    if (asm.GetProperty("name").GetString() == "WPFGallery")
                        file = asm.GetProperty("file").GetString();
                }
                Assert.NotNull(file);
                return System.Reflection.Assembly.Load(
                    File.ReadAllBytes(System.IO.Path.Combine(candidate, file!)));
            }
        }
        throw new InvalidOperationException("staged wpf-gallery package not found");
    }

    [Fact]
    public void Real_DatePickerPage_xaml_inflates_single_picker()
    {
        var asm = LoadConvertedGallery();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("DatePickerPage.xaml", StringComparison.Ordinal));
        Assert.NotNull(name);
        using var stream = asm.GetManifestResourceStream(name!);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = asm;
        try
        {
            var root = XamlLoader.Load(reader.ReadToEnd()).Root as FrameworkElement;
            var picker = FindVisual<DatePicker>(root);
            Assert.NotNull(picker);
            Assert.Equal("Pick a date", AutomationProperties.GetName(picker!));
            Assert.Null(picker!.SelectedDate);
            Assert.False(picker.IsDropDownOpen);
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }
}
