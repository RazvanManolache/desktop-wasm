// RVW3293-RT5 (dw-runtime): joint entry dies on undecodable WPF metadata
// blobs — served w27/w28 Mono assert (custom-attrs.c:1980 + assembly.c:1673,
// exit 1, zero managed frames) with all 11 .bins HTTP 200. Served decompile
// proves six `[assembly: ThemeInfo]` rows (AvalonDock + Themes.{Aero,Arc,
// Expression,VS,VS2013}) whose type (System.Windows, PresentationFramework)
// cannot resolve in wasm, plus unretargeted PresentationFramework refs in
// entry-referenced Metro/VS2010. The wpf-convert-v1 converter already drops
// these inert XAML markers (AssemblyConverter.AssemblyAttributeIsInert +
// FixAssemblyAttributeScopes); the joint packager's own lowering must do
// the same, or the served closure keeps shipping loader poison.
// Idempotency: a re-run over converted bytes must strip nothing.
using Mono.Cecil;
using DesktopWasm.Packaging;
using DesktopWasm.Packaging.Tests;

public sealed class TestAppJointThemeInfoTests
{
    private const string TargetNamespace = "System.Windows";
    private const string TargetName = "ThemeInfoAttribute";

    private static string StagedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DesktopWasm.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repository root (DesktopWasm.slnx) not found above the test binaries.");
        var staged = Path.Combine(dir.FullName, "inputs", "mixed", "avalondock");
        Assert.True(Directory.Exists(staged),
            $"Staged AvalonDock closure absent at '{staged}'.");
        return staged;
    }

    private static IReadOnlyDictionary<string, string> ProviderPaths()
    {
        var bin = AppContext.BaseDirectory;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [JointTestAppPackager.ForkAssembly] = Path.Combine(bin, "WfWasm.WinForms.dll"),
            [JointTestAppPackager.EngineAssembly] = Path.Combine(bin, "DesktopWasm.Wpf.dll"),
            [JointTestAppPackager.DrawingAssembly] = Path.Combine(bin, "WfWasm.Drawing.dll"),
        };
    }

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            foreach (var nested in current.NestedTypes)
                stack.Push(nested);
        }
    }

    private static bool HasThemeInfo(ModuleDefinition module)
    {
        bool Match(CustomAttribute attribute) =>
            attribute.AttributeType.Namespace == TargetNamespace &&
            attribute.AttributeType.Name == TargetName;
        if (module.Assembly.CustomAttributes.Any(Match)) return true;
        if (module.CustomAttributes.Any(Match)) return true;
        return AllTypes(module).Any(t => t.CustomAttributes.Any(Match));
    }

    private static bool HasWpfFrameworkRef(ModuleDefinition module) =>
        module.AssemblyReferences.Any(r =>
            r.Name == "PresentationFramework" ||
            r.Name == "PresentationCore" ||
            r.Name == "System.Xaml");

    [Fact]
    public void ConvertedClosure_CarriesNoThemeInfoOrWpfRefs()
    {
        var work = FixtureBuilder.TempDir("testapp-joint-themeinfo");
        var result = JointTestAppPackager.Build(StagedDirectory(), Path.Combine(work, "pkg"), ProviderPaths());
        Assert.Equal("converted", result.Manifest.Status);
        Assert.Empty(result.Manifest.Gaps);
        Assert.Equal(11, result.Converted.Count);
        foreach (var node in result.Converted)
        {
            var path = Path.Combine(result.OutputDirectory, node.File);
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            var module = assembly.MainModule;
            Assert.False(HasThemeInfo(module),
                $"{node.Name} still carries System.Windows.ThemeInfoAttribute (undecodable in wasm; Mono assert).");
            Assert.False(HasWpfFrameworkRef(module),
                $"{node.Name} still references a WPF framework assembly (absent in wasm).");
        }
    }

    [Fact]
    public void ThemeInfoStrip_IsIdempotent()
    {
        var work = FixtureBuilder.TempDir("testapp-joint-themeinfo-idem");
        var result = JointTestAppPackager.Build(StagedDirectory(), Path.Combine(work, "pkg"), ProviderPaths());
        Assert.Equal("converted", result.Manifest.Status);
        foreach (var node in result.Converted)
        {
            var path = Path.Combine(result.OutputDirectory, node.File);
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            Assert.False(HasThemeInfo(assembly.MainModule),
                $"{node.Name}: second lowering would still find ThemeInfo.");
            Assert.False(HasWpfFrameworkRef(assembly.MainModule),
                $"{node.Name}: second lowering would still find WPF framework refs.");
        }
    }
}
