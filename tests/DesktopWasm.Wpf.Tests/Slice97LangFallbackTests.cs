using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-97: converted HandyControl language markup ({ex:Lang}/{hc:Lang}),
/// literal static commands (ControlCommands) and converted attached
/// properties (IconElement.Geometry). The loader resolves converted Lang
/// heads to the engine LangExtension mirror, whose values come from the
/// staged images' own neutral .resources (ResourceReader, never
/// ResourceManager: the staged attribute TypeRefs dangle and poison every
/// runtime attribute enumeration, proven with a gated probe).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice97LangFallbackTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
        WpfAnimationClock.ResetForTests();
    }

    public static IEnumerable<object[]> StagedLangViews() => new[]
    {
        // Each row: converted view, the precise content gap it reaches now
        // that ex:Lang resolves, and the slice-96 gap fragment that is gone.
        // Per-subtree attach flushes pendings in document order, so a view
        // stops at the first missing resource behind the line-17 SkinDefault
        // stop (or the first unmapped element) even when later lines hold
        // Command/IconElement usage (LeftMainContent clears
        // Interaction.Triggers since slice-99 and stops at the
        // TabControlInLine app style instead)
        // (those land through the synthetic pins below, not through these views).
        new object[] { "HandyControlDemo.UserControl.TextDialog", "StaticResource 'TextBlockLargeBold' was not found", "'{ex:Lang}' is not supported" },
        new object[] { "HandyControlDemo.UserControl.TextDialogWithTimer", "StaticResource 'TextBlockLargeBold' was not found", "'{ex:Lang}' is not supported" },
        new object[] { "HandyControlDemo.UserControl.LeftMainContent", "StaticResource 'TabControlInLine' was not found", "<Interaction.Triggers> does not belong to <TabControl>" },
        new object[] { "HandyControlDemo.UserControl.AppNotification", "StaticResource 'LogoGeometry' was not found", "SimplePanel" },
        new object[] { "HandyControlDemo.UserControl.MainWindowContent", "StaticResource 'ButtonIconCircular' was not found", "SimplePanel" },
        new object[] { "HandyControlDemo.UserControl.MainContent", "StaticResource 'EffectShadow4' was not found", "SimplePanel" },
        new object[] { "HandyControlDemo.UserControl.UnderConstruction", "StaticResource 'TextBlockLargeBold' was not found", "SimplePanel" },
        new object[] { "HandyControlDemo.UserControl.GeometryItem", "StaticResource 'Path4GeometryItem' was not found", "SimplePanel" },
        new object[] { "HandyControlDemo.UserControl.InteractiveDialog", "StaticResource 'TextBlockLargeBold' was not found", "SimplePanel" },
    };

    [Theory]
    [MemberData(nameof(StagedLangViews))]
    public void Staged_views_advance_past_lang_gaps(string typeName, string expectedFragment, string goneFragment)
    {
        var entry = LoadStagedHandyControlEntry();
        Assert.True(typeof(Border).IsAssignableFrom(entry.GetType(typeName, throwOnError: true)!), typeName);
        var error = Assert.Throws<TargetInvocationException>(() =>
            Activator.CreateInstance(entry.GetType(typeName, throwOnError: true)!));
        var load = Assert.IsType<XamlLoadException>(error.InnerException);
        Assert.Contains(expectedFragment, load.Message);
        Assert.DoesNotContain(goneFragment, load.Message);
        for (var inner = error.InnerException; inner is not null; inner = inner.InnerException)
            Assert.IsNotType<MissingFieldException>(inner);
    }

    [Fact]
    public void Staged_lang_tables_carry_the_real_translations()
    {
        var entry = LoadStagedHandyControlEntry();
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.Ordinal));
        var demoTable = LangTable.ForAssembly(entry);
        var libTable = LangTable.ForAssembly(lib);
        Assert.Equal("请稍后...", demoTable.GetString("PleaseWait"));
        Assert.Equal("实用例子", demoTable.GetString("PracticalDemos"));
        Assert.Equal("关闭", libTable.GetString("Close"));
        Assert.Null(demoTable.GetString("Close"));
        Assert.Null(libTable.GetString("PleaseWait"));
        Assert.Null(demoTable.GetString("NoSuchKey"));
    }

    [Fact]
    public void Staged_lang_extension_shapes_match_the_engine_mirror()
    {
        var entry = LoadStagedHandyControlEntry();
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.Ordinal));
        var libExtension = lib.GetType("HandyControl.Tools.Extension.LangExtension", throwOnError: true)!;
        Assert.True(typeof(MarkupExtension).IsAssignableFrom(libExtension));
        Assert.True(libExtension.IsSubclassOf(typeof(MarkupExtension)));
        foreach (var name in new[] { "Key", "Mode", "Converter", "ConverterParameter", "Source" })
            Assert.NotNull(libExtension.GetProperty(name));
        Assert.NotNull(libExtension.GetMethod("ProvideValue"));
        Assert.IsType<DependencyProperty>(libExtension.GetField(
            "KeyProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!.GetValue(null));
        var demoExtension = entry.GetType("HandyControlDemo.Tools.Extension.LangExtension", throwOnError: true)!;
        Assert.True(demoExtension.IsSubclassOf(libExtension));
        // The engine mirror carries the same public contract.
        Assert.True(typeof(MarkupExtension).IsAssignableFrom(typeof(LangExtension)));
        Assert.NotNull(typeof(LangExtension).GetProperty("Key"));
        Assert.NotNull(typeof(LangExtension).GetProperty("Source"));
        Assert.NotNull(typeof(LangExtension).GetMethod("ProvideValue"));
        Assert.IsType<DependencyProperty>(typeof(LangExtension).GetField(
            "KeyProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!.GetValue(null));
    }

    [Fact]
    public void Demo_lang_head_binds_the_real_translation_live()
    {
        LoadStagedHandyControlEntry();
        var load = XamlLoader.Load(
            "<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:ex=\"clr-namespace:HandyControlDemo.Tools.Extension\" " +
            "xmlns:langs=\"clr-namespace:HandyControlDemo.Properties.Langs\" " +
            "Text=\"{ex:Lang Key={x:Static langs:LangKeys.PleaseWait}}\" />");
        var text = Assert.IsType<TextBlock>(load.Root);
        Assert.Equal("请稍后...", text.Text);
        // The live adapter behind the binding carries the same string.
        var entry = LoadStagedHandyControlEntry();
        Assert.Equal("请稍后...", LangTable.ForAssembly(entry).GetOrAddValue("PleaseWait").Value);
    }

    [Fact]
    public void Lib_lang_head_binds_the_real_translation_live()
    {
        LoadStagedHandyControlEntry();
        var load = XamlLoader.Load(
            "<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:hc=\"https://handyorg.github.io/handycontrol\" " +
            "Text=\"{hc:Lang Key={x:Static hc:LangKeys.Close}}\" />");
        var text = Assert.IsType<TextBlock>(load.Root);
        Assert.Equal("关闭", text.Text);
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.Ordinal));
        Assert.Equal("关闭", LangTable.ForAssembly(lib).GetOrAddValue("Close").Value);
    }

    private sealed class TitleHolder
    {
        public TitleHolder(string title) => Title = title;
        public string Title { get; }
    }

    [Fact]
    public void Binding_key_rebinds_on_first_data_context()
    {
        LoadStagedHandyControlEntry();
        var load = XamlLoader.Load(
            "<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:ex=\"clr-namespace:HandyControlDemo.Tools.Extension\" " +
            "Text=\"{ex:Lang Key={Binding Title}}\" />");
        var text = Assert.IsType<TextBlock>(load.Root);
        Assert.Equal(string.Empty, text.Text);
        text.DataContext = new TitleHolder("PleaseWait");
        Assert.Equal("请稍后...", text.Text);
        var entry = LoadStagedHandyControlEntry();
        Assert.Equal("请稍后...", LangTable.ForAssembly(entry).GetOrAddValue("PleaseWait").Value);
    }

    [Fact]
    public void Culture_set_repushes_live_keys()
    {
        var entry = LoadStagedHandyControlEntry();
        var table = LangTable.ForAssembly(entry);
        var load = XamlLoader.Load(
            "<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:ex=\"clr-namespace:HandyControlDemo.Tools.Extension\" " +
            "xmlns:langs=\"clr-namespace:HandyControlDemo.Properties.Langs\" " +
            "Text=\"{ex:Lang Key={x:Static langs:LangKeys.PracticalDemos}}\" />");
        var text = Assert.IsType<TextBlock>(load.Root);
        Assert.Equal("实用例子", text.Text);
        // Staged sets ship neutral resources only, so the re-push keeps
        // values while proving the live path runs (no satellites staged).
        table.Culture = new System.Globalization.CultureInfo("en");
        Assert.Equal("实用例子", text.Text);
        table.Culture = System.Globalization.CultureInfo.CurrentCulture;
    }

    [Fact]
    public void Button_literal_command_binds_the_converted_routed_command()
    {
        var entry = LoadStagedHandyControlEntry();
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.Ordinal));
        var load = XamlLoader.Load(
            "<Button xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:hc=\"https://handyorg.github.io/handycontrol\" " +
            "Command=\"hc:ControlCommands.Close\" />");
        var button = Assert.IsType<Button>(load.Root);
        var expected = lib.GetType("HandyControl.Interactivity.ControlCommands", throwOnError: true)!
            .GetProperty("Close", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!
            .GetValue(null);
        Assert.IsAssignableFrom<ICommand>(button.Command);
        Assert.Same(expected, button.Command);
    }

    [Fact]
    public void Button_literal_command_keeps_legacy_and_precise_errors()
    {
        LoadStagedHandyControlEntry();
        var unknown = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<Button xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Command=\"Nope\" />"));
        Assert.Contains("needs a {Binding} or code assignment", unknown.Message);
        var missing = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<Button xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:hc=\"https://handyorg.github.io/handycontrol\" " +
            "Command=\"hc:ControlCommands.NoSuchCommand\" />"));
        Assert.Contains("not a public static command", missing.Message);
    }

    [Fact]
    public void Converted_attached_geometry_applies_through_pending_static()
    {
        var entry = LoadStagedHandyControlEntry();
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.Ordinal));
        var load = XamlLoader.Load(
            "<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
            "xmlns:hc=\"https://handyorg.github.io/handycontrol\">" +
            "<Border.Resources><Geometry x:Key=\"G\">M0,0 L10,0 L10,10 Z</Geometry></Border.Resources>" +
            "<Button x:Name=\"Target\" hc:IconElement.Geometry=\"{StaticResource G}\" />" +
            "</Border>");
        var button = Assert.IsType<Button>(load.Names["Target"]);
        var dp = (DependencyProperty)lib.GetType("HandyControl.Controls.IconElement", throwOnError: true)!
            .GetField("GeometryProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!
            .GetValue(null)!;
        Assert.IsAssignableFrom<Geometry>(button.GetValue(dp));
    }

    [Fact]
    public void Converted_attached_keeps_legacy_and_precise_errors()
    {
        LoadStagedHandyControlEntry();
        var unknown = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
            "xmlns:hc=\"https://handyorg.github.io/handycontrol\">" +
            "<Button hc:Nope.Prop=\"x\" />" +
            "</Border>"));
        Assert.Contains("Unknown attached property 'Nope.Prop'", unknown.Message);
        var missing = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
            "xmlns:hc=\"https://handyorg.github.io/handycontrol\">" +
            "<Border.Resources><Geometry x:Key=\"G\">M0,0 L1,1</Geometry></Border.Resources>" +
            "<Button hc:IconElement.Nope=\"{StaticResource G}\" />" +
            "</Border>"));
        Assert.Contains("names no attached property", missing.Message);
    }

    [Theory]
    [InlineData("{ex:Lang Key=K}", "'{ex:Lang}' is not supported")]
    public void Unmapped_prefix_keeps_the_legacy_error(string extension, string expectedFragment)
    {
        LoadStagedHandyControlEntry();
        var error = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Text=\"" + extension + "\" />"));
        Assert.Contains(expectedFragment, error.Message);
    }

    [Fact]
    public void Converted_lang_rejects_bad_shapes_precisely()
    {
        LoadStagedHandyControlEntry();
        const string head =
            "<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:ex=\"clr-namespace:HandyControlDemo.Tools.Extension\" " +
            "xmlns:langs=\"clr-namespace:HandyControlDemo.Properties.Langs\" Text=\"";
        var missing = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(head + "{ex:Lang}\" />"));
        Assert.Contains("needs a key", missing.Message);
        var badProp = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(head + "{ex:Lang Bogus=X}\" />"));
        Assert.Contains("not a settable Lang property", badProp.Message);
        var badStatic = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(head + "{ex:Lang Key={x:Static langs:Nope.Missing}}\" />"));
        Assert.Contains("names no converted type", badStatic.Message);
        var nested = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(head + "{ex:Lang Key={Foo Bar}}\" />"));
        Assert.Contains("not supported in converted-extension args", nested.Message);
        var missingTable = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\" Text=\"{t:Lang Key=K}\" />"));
        Assert.Contains("names no converted LangExtension", missingTable.Message);
    }

    [Fact]
    public void Staged_manifest_keeps_six_images_and_two_absent_binaries()
    {
        // No re-package this slice (Convert map untouched): the staged set
        // and its absent-binary gaps must be byte-identical to slice-96.
        LoadStagedHandyControlEntry();
        var apps = StagedAppsDirectory();
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
        Assert.Equal("conversion-failed", document.RootElement.GetProperty("status").GetString());
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
        Assert.All(gaps, gap => Assert.Equal("missing-dependency",
            gap.GetProperty("kind").GetString()));
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
            var apps = StagedAppsDirectory();
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

    private static string StagedAppsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol");
    }

    private static void VerifyStagedHashes()
    {
        var apps = StagedAppsDirectory();
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
