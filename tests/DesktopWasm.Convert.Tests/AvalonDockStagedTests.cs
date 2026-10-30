using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;

namespace DesktopWasm.Convert.Tests;

/// <summary>
/// Slice-61 staged oracle for AvalonDock: the engine now declares the last 8
/// AvalonDock rows (DependencyPropertyHelper/ValueSource/BaseValueSource,
/// GridLengthConverter, FrameworkElementFactory, StyleSelector,
/// IconBitmapDecoder, DispatcherOperationCallback) plus the VS-theme
/// MarkupExtensionReturnTypeAttribute row, so the package advances 16 -&gt; 6
/// gaps and 8 -&gt; 10 staged (AvalonDock itself plus AvalonDock.Themes.VS).
/// The TestApp entry stays WinForms-blocked (ContainerControl/AutoScaleMode,
/// nested ControlCollection, WindowsFormsIntegration missing dependency): out
/// of scope as declared, so no boot oracle runs here — conversion itself is
/// the advance, pinned by gap + member-ref evidence below. The 6th old gap
/// (a stale System.Windows.Forms.Primitives missing-dependency row) is gone
/// because the live TestApp.exe references no such assembly: the re-package
/// reports exactly what the upstream binary holds. Every staged image
/// rehashes on re-package (Cecil stamps a fresh MVID per write; content
/// bytes otherwise identical), so this oracle pins the post-slice hashes.
/// </summary>
public sealed class AvalonDockStagedTests
{
    [Fact]
    public void Staged_avalondock_hashes_verify()
    {
        Assert.Empty(StagedAvalonDockLoader.VerifyHashes());
    }

    [Fact]
    public void Entry_stays_winforms_blocked()
    {
        // The TestApp entry still needs WindowsFormsIntegration (declared
        // boundary): it converts nothing and stages nothing.
        var manifest = StagedAvalonDockLoader.ReadManifest();
        Assert.Equal("conversion-failed", manifest.Status);
        Assert.DoesNotContain(manifest.Assemblies, n => n.Contains("TestApp", StringComparison.Ordinal));
        Assert.Contains(manifest.Gaps, g => g.Kind == "missing-dependency" && g.Detail.Contains("WindowsFormsIntegration", StringComparison.Ordinal));
    }

    [Fact]
    public void Gaps_are_exactly_the_post_slice_set()
    {
        var manifest = StagedAvalonDockLoader.ReadManifest();
        Assert.Equal("conversion-failed", manifest.Status);
        Assert.Equal(6, manifest.Gaps.Count);
        // Zero slice-gap rows left: no converter failure of any kind names
        // the nine slice types.
        foreach (var name in new[]
        {
            "DependencyPropertyHelper", "BaseValueSource", "GridLengthConverter",
            "FrameworkElementFactory", "StyleSelector", "IconBitmapDecoder",
            "DispatcherOperationCallback", "MarkupExtensionReturnTypeAttribute",
        })
            Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains(name, StringComparison.Ordinal));
        Assert.DoesNotContain(manifest.Gaps, g =>
            g.Kind is "rewrite-error" or "stale-framework-reference" && g.Assembly != "TestApp"
            || g.Kind is "unmapped-type" && g.Assembly != "TestApp");
        Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains("ValueSource", StringComparison.Ordinal));
        // Positive remainder pins: all 6 rows are the TestApp WinForms
        // boundary (forms types plus the WindowsFormsIntegration dependency).
        Assert.All(manifest.Gaps, g => Assert.Equal("TestApp", g.Assembly));
        Assert.All(manifest.Gaps, g =>
            Assert.True(g.Detail.Contains("Forms", StringComparison.Ordinal) ||
                g.Detail.Contains("WindowsFormsIntegration", StringComparison.Ordinal),
                $"unexpected gap row: [{g.Kind}] {g.Detail}"));
    }

    [Fact]
    public void Slice_stages_the_main_assembly_and_the_VS_theme()
    {
        // The main assembly carried 8 unmapped rows and the VS theme 1; both
        // stage now (8 -> 10 staged). Every other assembly name is unchanged.
        var manifest = StagedAvalonDockLoader.ReadManifest();
        Assert.Equal(10, manifest.Assemblies.Count);
        Assert.Equal(10, StagedAvalonDockLoader.StagedFiles().Count);
        foreach (var name in new[]
        {
            "AvalonDock", "AvalonDock.Serializer.Xml", "AvalonDock.Core",
            "AvalonDock.Themes.VS2010", "AvalonDock.Themes.VS", "AvalonDock.Themes.Expression",
            "AvalonDock.Themes.Metro", "AvalonDock.Themes.Arc", "AvalonDock.Themes.VS2013",
            "AvalonDock.Themes.Aero",
        })
            Assert.Contains(name, manifest.Assemblies);
        Assert.DoesNotContain("TestApp", manifest.Assemblies);
    }

    [Fact]
    public void Converted_bins_bind_slice_members_to_the_engine()
    {
        // The converted main assembly resolves every analyzed slice call site
        // onto DesktopWasm.Wpf: the value-source read, the factory template
        // construction, the style-selector dispatch, the DTO grid-length
        // converter and the MessageWindow teardown callback.
        var assembly = StagedAvalonDockLoader.LoadAssembly("AvalonDock");
        var refs = assembly.MainModule.GetMemberReferences()
            .Where(m => m.DeclaringType is not null &&
                m.DeclaringType.Scope.Name == "DesktopWasm.Wpf" &&
                m.DeclaringType.Namespace == "DesktopWasm.Wpf")
            .ToList();
        Assert.Contains(refs, m => m.DeclaringType.Name == "DependencyPropertyHelper" && m.Name == "GetValueSource");
        Assert.Contains(refs, m => m.DeclaringType.Name == "ValueSource" && m.Name == "get_BaseValueSource");
        Assert.Contains(refs, m => m.DeclaringType.Name == "DependencyProperty" && m.Name == "get_ReadOnly");
        Assert.Contains(refs, m => m.DeclaringType.Name == "FrameworkElementFactory" && m.Name == ".ctor");
        Assert.Contains(refs, m => m.DeclaringType.Name == "FrameworkElementFactory" && m.Name == "AppendChild");
        Assert.Contains(refs, m => m.DeclaringType.Name == "FrameworkElementFactory" && m.Name == "SetBinding");
        Assert.Contains(refs, m => m.DeclaringType.Name == "FrameworkElementFactory" && m.Name == "SetValue");
        Assert.Contains(refs, m => m.DeclaringType.Name == "FrameworkTemplate" && m.Name == "set_VisualTree");
        Assert.Contains(refs, m => m.DeclaringType.Name == "StyleSelector" && m.Name == "SelectStyle");
        Assert.Contains(refs, m => m.DeclaringType.Name == "GridLengthConverter" && m.Name == ".ctor");
        Assert.Contains(refs, m => m.DeclaringType.Name == "DispatcherOperationCallback" && m.Name == ".ctor");
        Assert.Contains(refs, m => m.DeclaringType.Name == "FrameworkElement" && m.Name == "SetBinding");
        Assert.Contains(refs, m => m.DeclaringType.Name == "ControlTemplate" && m.Name == ".ctor");
        // No framework-assembly type or member reference survives in either
        // newly staged image (conversion strips them all).
        foreach (var name in new[] { "AvalonDock", "AvalonDock.Themes.VS" })
        {
            var image = StagedAvalonDockLoader.LoadAssembly(name);
            Assert.Empty(image.MainModule.GetTypeReferences().Where(t =>
                t.Scope.Name is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml"));
            Assert.Empty(image.MainModule.GetMemberReferences().Where(m =>
                m.DeclaringType is not null && m.DeclaringType.Scope.Name is
                    "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml"));
        }
        // The VS theme's annotation lands on the engine attribute with its
        // VsTheme typeof argument intact.
        var vs = StagedAvalonDockLoader.LoadAssembly("AvalonDock.Themes.VS");
        var themeExtension = vs.MainModule.Types.Single(t => t.Name == "VsThemeExtension");
        var annotation = Assert.Single(themeExtension.CustomAttributes,
            a => a.AttributeType.Name == "MarkupExtensionReturnTypeAttribute");
        Assert.Equal("DesktopWasm.Wpf", ((AssemblyNameReference)annotation.AttributeType.Scope).Name);
        // The icon-picker narrowing (isinst in Standard.Utility._GetBestMatch)
        // resolves onto the engine decoder in the main assembly.
        Assert.Contains(assembly.MainModule.GetTypeReferences(), t =>
            t.Name == "IconBitmapDecoder" && t.Scope.Name == "DesktopWasm.Wpf");
    }

    [Fact]
    public void Input_hashes_match_the_upstream_build()
    {
        // Receipt chain: every staged image records the sha256 of the exact
        // upstream binary it was converted from.
        var root = FindBuildDir();
        var manifest = StagedAvalonDockLoader.ReadManifest();
        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AvalonDock"] = Path.Combine(root, "TestApp", "release_net48", "AvalonDock.dll"),
            ["AvalonDock.Serializer.Xml"] = Path.Combine(root, "AvalonDock.Serializer.Xml", "release", "AvalonDock.Serializer.Xml.dll"),
            ["AvalonDock.Core"] = Path.Combine(root, "AvalonDock.Core", "release_netstandard2.0", "AvalonDock.Core.dll"),
            ["AvalonDock.Themes.VS"] = Path.Combine(root, "AvalonDock.Themes.VS", "release_net48", "AvalonDock.Themes.VS.dll"),
        };
        foreach (var (name, upstream) in inputs)
        {
            Assert.True(File.Exists(upstream), $"upstream binary missing: {upstream}");
            using var sha = System.Security.Cryptography.SHA256.Create();
            var actual = System.Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(upstream)));
            var recorded = manifest.RawAssemblies.Single(a => a.Name == name);
            Assert.Equal(recorded.InputSha256, actual);
        }
    }

    private static string FindBuildDir()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "artifacts", "app-builds", "avalondock", "bin")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "artifacts", "app-builds", "avalondock", "bin");
    }

    private sealed record StagedGap(string Assembly, string Kind, string Detail);
    private sealed record StagedAssembly(string Name, string Sha256, string InputSha256);
    private sealed record StagedManifest(string Status, List<StagedGap> Gaps, List<string> Assemblies, List<StagedAssembly> RawAssemblies);

    private static class StagedAvalonDockLoader
    {
        public static List<string> VerifyHashes()
        {
            var failures = new List<string>();
            foreach (var (_, file, bytes, expected, _) in ReadStaged())
            {
                var actual = System.Convert.ToHexString(SHA256.HashData(bytes));
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"hash mismatch in staged '{file}'");
            }
            return failures;
        }

        public static List<string> StagedFiles()
        {
            var apps = AppsDir();
            return Directory.GetFiles(apps, "*.bin").Select(Path.GetFileName)!.ToList();
        }

        public static StagedManifest ReadManifest()
        {
            var apps = AppsDir();
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(apps, "app-manifest.json")));
            var root = document.RootElement;
            var gaps = new List<StagedGap>();
            foreach (var gap in root.GetProperty("gaps").EnumerateArray())
                gaps.Add(new StagedGap(
                    gap.GetProperty("assembly").GetString()!,
                    gap.GetProperty("kind").GetString()!,
                    gap.GetProperty("detail").GetString()!));
            var assemblies = new List<string>();
            var raw = new List<StagedAssembly>();
            foreach (var assembly in root.GetProperty("assemblies").EnumerateArray())
            {
                assemblies.Add(assembly.GetProperty("name").GetString()!);
                raw.Add(new StagedAssembly(
                    assembly.GetProperty("name").GetString()!,
                    assembly.GetProperty("sha256").GetString()!,
                    assembly.GetProperty("inputSha256").GetString()!));
            }
            return new StagedManifest(root.GetProperty("status").GetString()!, gaps, assemblies, raw);
        }

        public static Mono.Cecil.AssemblyDefinition LoadAssembly(string name)
        {
            var images = ReadStaged().ToDictionary(e => e.name, e => e.bytes, StringComparer.OrdinalIgnoreCase);
            Assert.True(images.ContainsKey(name), $"staged image missing: {name}");
            return Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(images[name]));
        }

        private static List<(string name, string file, byte[] bytes, string expected, string input)> ReadStaged()
        {
            var apps = AppsDir();
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(apps, "app-manifest.json")));
            var entries = new List<(string, string, byte[], string, string)>();
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var input = assembly.GetProperty("inputSha256").GetString()!;
                var bytes = File.ReadAllBytes(Path.Combine(apps, file));
                entries.Add((assembly.GetProperty("name").GetString()!, file, bytes, expected, input));
            }
            return entries;
        }

        private static string AppsDir()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "avalondock", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            return Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "avalondock");
        }
    }
}
