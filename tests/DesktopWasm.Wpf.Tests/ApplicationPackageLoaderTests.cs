using System.Security.Cryptography;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class ApplicationPackageLoaderTests
{
    private static ApplicationPackage Package(string status = "converted", PackageGap[]? gaps = null) =>
        new(1, "fixture", WpfAppBoot.MapVersion, status, "Entry",
            [new("Entry", "entry.bin", Convert.ToHexString(SHA256.HashData([1, 2, 3])), ["System.Runtime"])], gaps ?? [], null);

    [Fact]
    public async Task Fetches_only_declared_files_and_checks_hash_before_loading_any_code()
    {
        var requested = new List<string>();
        using var loader = await ApplicationPackageLoader.CreateAsync(Package(), "fixture", file =>
        { requested.Add(file); return Task.FromResult<byte[]>([1, 2, 3]); });
        Assert.Equal(new[] { "entry.bin" }, requested);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => ApplicationPackageLoader.CreateAsync(Package(), "fixture", _ => Task.FromResult<byte[]>([9])));
        Assert.Contains("hash mismatch", error.Message);
    }

    [Fact]
    public async Task Progress_reporter_names_each_verified_assembly()
    {
        var package = Package() with
        {
            Assemblies =
            [
                Package().Assemblies[0],
                Package().Assemblies[0] with { Name = "Second", File = "second.bin" },
            ]
        };
        var phases = new List<string>();
        using var loader = await ApplicationPackageLoader.CreateAsync(package, "fixture",
            file => Task.FromResult<byte[]>([1, 2, 3]),
            phase => { phases.Add(phase); return Task.CompletedTask; });
        Assert.Equal(2, phases.Count);
        Assert.Contains(phases, p => p.Contains("Entry", StringComparison.Ordinal));
        Assert.Contains(phases, p => p.Contains("Second", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Conversion_failure_reports_actual_assembly_and_gap_without_fetching_images()
    {
        var called = false;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ApplicationPackageLoader.CreateAsync(
            Package("conversion-failed", [new("Vendor.Controls", "unmapped-type", "System.Windows.Controls.ControlTemplate")]),
            "fixture", _ => { called = true; return Task.FromResult<byte[]>([]); }));
        Assert.False(called);
        Assert.Contains("Vendor.Controls [unmapped-type]", error.Message);
        Assert.Contains("ControlTemplate", error.Message);
    }

    [Fact]
    public async Task Pack_text_resolves_from_unloaded_staged_image()
    {
        // Browser shape: staged images are fetched and hash-verified but only
        // loaded on first use; App.xaml merges ShowMeTheXAML.AvalonEdit's
        // xamldisplayer dictionary before anything references that assembly,
        // so pack lookup must pull the staged image through the loader
        // instead of reporting it absent.
        var directory = FindAppDirectory("material-design");
        var manifest = System.Text.Json.JsonSerializer.Deserialize<ApplicationPackage>(
            File.ReadAllText(System.IO.Path.Combine(directory, "app-manifest.json")),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        using var loader = await ApplicationPackageLoader.CreateAsync(manifest, "material-design",
            file => File.ReadAllBytesAsync(System.IO.Path.Combine(directory, file)));
        var text = WpfAppBoot.FindPackText("Themes/xamldisplayer.xaml", "ShowMeTheXAML.AvalonEdit");
        Assert.NotNull(text);
        Assert.Contains("ResourceDictionary", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vendor_type_resolves_from_unloaded_staged_image()
    {
        // xamldisplayer shape: the theme dictionary sets Style TargetType to
        // avalonedit:TextEditor while ICSharpCode.AvalonEdit is fetched but
        // unloaded; the xmlns table plus the package resolver must bind the
        // converted type instead of reporting it absent.
        var directory = FindAppDirectory("material-design");
        var manifest = System.Text.Json.JsonSerializer.Deserialize<ApplicationPackage>(
            File.ReadAllText(System.IO.Path.Combine(directory, "app-manifest.json")),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        using var loader = await ApplicationPackageLoader.CreateAsync(manifest, "material-design",
            file => File.ReadAllBytesAsync(System.IO.Path.Combine(directory, file)));
        // Converted AvalonEdit controls verify UI-thread access on
        // construction (engine WasmDispatcher): capture this thread as the
        // owner like the staged boot oracles do (restored afterwards by
        // ResetAmbientState; the single-threaded browser owns its pump).
        WasmDispatcher.Current = new WasmDispatcher(new DesktopWasm.Core.WasmDispatcherPump());
        var text = WpfAppBoot.FindPackText("Themes/xamldisplayer.xaml", "ShowMeTheXAML.AvalonEdit");
        Assert.NotNull(text);
        var dict = XamlLoader.LoadResourceDictionary(text);
        var editorAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "ICSharpCode.AvalonEdit", StringComparison.OrdinalIgnoreCase));
        var editorType = editorAssembly.GetType("ICSharpCode.AvalonEdit.TextEditor");
        Assert.NotNull(editorType);
        Assert.True(dict.TryGetValue("AvalonTextEditorXamlDisplay", out var style));
        Assert.Equal(editorType, Assert.IsType<Style>(style).TargetType);
    }

    private static string FindAppDirectory(string slug)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "DesktopWasm.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return System.IO.Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", slug);
    }

    [Theory]
    [InlineData("../outside.bin")]
    [InlineData("..\\outside.bin")]
    [InlineData("/absolute.bin")]
    public async Task Invalid_manifest_paths_are_rejected_before_network_requests(string file)
    {
        var package = Package() with { Assemblies = [Package().Assemblies[0] with { File = file }] };
        var called = false;
        await Assert.ThrowsAsync<InvalidDataException>(() => ApplicationPackageLoader.CreateAsync(package, "fixture", _ =>
        { called = true; return Task.FromResult<byte[]>([]); }));
        Assert.False(called);
    }
}
