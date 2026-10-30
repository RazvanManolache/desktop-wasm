namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery TextBox validation slice (slice-33): the real converted WPFGallery
/// Text/TextBoxPage binds its ValidatedTextBox through
/// &lt;TextBox.Text&gt; with UpdateSourceTrigger=PropertyChanged and a prefixed
/// helpers:AlphabeticValidationRule. Fragment re-parsing (ReadOuterXml) hoists
/// the in-scope xmlns:helpers declaration onto the rule element itself, which
/// the loader used to reject as a rule argument, so the page could not inflate
/// from staging. Namespace declarations are not arguments: the loader resolves
/// the parameterless rule by short name and runs it.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class TextBoxValidationPageTests
{
    private sealed class PageVm
    {
        public InnerVm ViewModel { get; } = new();
    }

    private sealed class InnerVm
    {
        public string ValidatedText { get; set; } = string.Empty;
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

    private static TextBox LoadValidatedBox(out FrameworkElement root)
    {
        var asm = LoadConvertedGallery();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("TextBoxPage.xaml", StringComparison.Ordinal));
        Assert.NotNull(name);
        using var stream = asm.GetManifestResourceStream(name!);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = asm;
        try
        {
            root = (Assert.IsType<Page>(XamlLoader.Load(reader.ReadToEnd()).Root) as FrameworkElement)!;
            var box = FindVisuals<TextBox>(root).FirstOrDefault(b => b.Name == "ValidatedTextBox");
            Assert.NotNull(box);
            return box!;
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    [Fact]
    public void Real_TextBoxPage_xaml_inflates_with_prefixed_validation_rule()
    {
        var box = LoadValidatedBox(out _);
        var expression = Assert.Single(box.ActiveBindings);
        Assert.Equal("Text", expression.TargetProperty);
        var rule = Assert.Single(expression.Binding.ValidationRules);
        Assert.Equal("AlphabeticValidationRule", rule.GetType().Name);
    }

    [Fact]
    public void Real_ValidatedTextBox_blocks_non_alpha_and_pushes_alpha()
    {
        var box = LoadValidatedBox(out _);
        var vm = new PageVm();
        box.DataContext = vm;

        box.Text = "Ann3";
        Assert.Equal(string.Empty, vm.ViewModel.ValidatedText);
        Assert.True(Validation.GetHasError(box));

        box.Text = "Ada";
        Assert.Equal("Ada", vm.ViewModel.ValidatedText);
        Assert.False(Validation.GetHasError(box));
    }

    [Fact]
    public void Real_TextBoxPage_other_examples_parse()
    {
        LoadValidatedBox(out var root);
        var boxes = FindVisuals<TextBox>(root);
        Assert.Equal(3, boxes.Count);
        var multiline = boxes.FirstOrDefault(b => b.AcceptsReturn);
        Assert.NotNull(multiline);
        Assert.Equal(TextWrapping.Wrap, multiline!.TextWrapping);
    }
}
