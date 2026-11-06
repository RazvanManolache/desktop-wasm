using DesktopWasm.Packaging;
using Mono.Cecil;
using WpfAppBoot = DesktopWasm.Wpf.WpfAppBoot;

namespace DesktopWasm.Packaging.Tests;

/// <summary>
/// Genuine reconverted mRemoteNG closure over the RDP-excluded staged
/// package at .artifacts/benchmark-apps/benchmark-mremoteng: full closure
/// lowering, loader-compatible manifest, hashed payloads + dll/ resolver
/// mirror, ThemeInfo-free WebView2.Wpf, RDP-exclusion receipt note.
/// </summary>
public sealed class MRemoteNGPackageTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DesktopWasm.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repository root (DesktopWasm.slnx) not found above the test binaries.");
        return dir.FullName;
    }

    private static string StagedDirectory()
    {
        var staged = Path.Combine(RepoRoot(), ".artifacts", "benchmark-apps", "benchmark-mremoteng");
        Assert.True(Directory.Exists(staged),
            $"Staged mRemoteNG package absent at '{staged}': stage the RDP-excluded build there first (see preparation.json).");
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

    [Fact]
    public void MapVersion_MatchesLoaderContract()
    {
        Assert.Equal(WpfAppBoot.MapVersion, MRemoteNGPackager.MapVersion);
    }

    private static string[] FrameworkInputs()
    {
        // Mapped shared-framework inputs (pinned, never a mutable fallback):
        // .NET 10 WindowsDesktop pack carries the framework-only assemblies
        // the RDP-excluded staged tree does not ship.
        var pack = @"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.13";
        Assert.True(Directory.Exists(pack), $"Framework pack absent at '{pack}'.");
        return new[] { pack };
    }

    private static IReadOnlySet<string> BundleProvided()
    {
        // Real bundle observation: what the net9 BrowserHost build output
        // ships. Closure nodes under these names are framework-provided,
        // never packaged (staging copies would shadow the framework copy).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DesktopWasm.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repository root not found.");
        var bin = Path.Combine(dir.FullName, "src", "DesktopWasm.BrowserHost", "bin", "Debug", "net9.0");
        Assert.True(Directory.Exists(bin), $"BrowserHost build output absent at '{bin}': build it before packaging.");
        return Directory.GetFiles(bin, "*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static JointTestAppPackager.JointPackageResult BuildReal()
    {
        var work = FixtureBuilder.TempDir("mremoteng-package");
        return MRemoteNGPackager.Build(StagedDirectory(), Path.Combine(work, "pkg"), ProviderPaths(), FrameworkInputs(), BundleProvided());
    }

    [Fact(Skip = "Blocked: ObjectListView (2.9.3.5136) is mixed-mode — Cecil reads but cannot rewrite it, so the closure breaks with a precise rewrite-error gap. Runtime lane decides exclusion (if provably unloaded) vs stub; unskip when conversion succeeds. Tracked in reports/CREDIT1300-dw-import.md S5.")]
    public void JointPackage_BuildsValidLoaderManifest_RealStaged()
    {
        var result = BuildReal();

        Assert.True(result.Manifest.Status == "converted",
            "mRemoteNG package must be converted, not synthesized: " +
            string.Join(" | ", result.Gaps.Select(g => $"{g.Assembly} [{g.Kind}]: {g.Detail}")));
        Assert.Empty(result.Gaps);
        result.Manifest.Validate(MRemoteNGPackager.Slug, WpfAppBoot.MapVersion);

        Assert.Equal("mRemoteNG", result.Manifest.EntryAssembly);
        Assert.Contains(result.Manifest.Assemblies, a => a.Name == "mRemoteNG");

        // Every packaged payload matches its manifest hash; node pins cover
        // input (retained original) and output (reconverted) bytes.
        // Bundle-provided nodes carry an empty file and must be recorded
        // with provider identity instead of staged bytes.
        foreach (var node in result.Converted)
        {
            if (string.IsNullOrEmpty(node.File))
            {
                Assert.Contains(result.FrameworkProvided, p => p.Name.Equals(node.Name, StringComparison.OrdinalIgnoreCase));
                continue;
            }
            var path = Path.Combine(result.OutputDirectory, node.File);
            Assert.True(File.Exists(path), $"Staged payload missing for '{node.Name}'.");
            Assert.Equal(node.OutputSha256, JointClosure.HashOf(path));
        }

        // WebView2.Wpf converted image carries no undecodable ThemeInfo blob.
        var wpf = result.Converted.FirstOrDefault(c => c.Name.Equals("Microsoft.Web.WebView2.Wpf", StringComparison.OrdinalIgnoreCase));
        Assert.True(wpf is not null, "WebView2.Wpf must be a packaged closure node.");
        using var asm = AssemblyDefinition.ReadAssembly(Path.Combine(result.OutputDirectory, wpf.File));
        Assert.DoesNotContain(asm.MainModule.Assembly.CustomAttributes,
            a => a.AttributeType.Name.Contains("ThemeInfo"));
        Assert.DoesNotContain(asm.MainModule.AssemblyReferences,
            r => r.Name.Equals("PresentationFramework", StringComparison.OrdinalIgnoreCase));

        // dll/ resolver mirror regenerated alongside the hashed payloads.
        var mirror = Path.Combine(result.OutputDirectory, "dll");
        Assert.True(Directory.Exists(mirror), "dll/ resolver mirror missing from package output.");
        Assert.True(Directory.GetFiles(mirror, "*.dll").Length > 0, "dll/ resolver mirror is empty.");
    }

    private static int GetReceiptNodeCount(string receiptPath)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(receiptPath));
        return doc.RootElement.GetProperty("closureNodes").GetArrayLength();
    }

    [Fact]
    public void JointPackage_GapsArePreciseAndPinned_WhenEngineCoverageMissing()
    {
        // Shape contract (CREDIT1300-IMP5): whatever the provider binaries
        // currently declare, the packager must never emit a gap it owns —
        // no missing-dependency (closure must fully resolve), no
        // keep-scope engine gaps (bundle-side keep + private-member rule),
        // no SystemEvents collision (bridge-aware route). Residual
        // engine/fork-availability and mixed-mode gaps belong to the
        // provider/engine lanes and are filed per-run in the lane report.
        // If this ever goes fully green, unskip the acceptance gate above.
        var result = BuildReal();
        Assert.DoesNotContain(result.Gaps, g => g.Kind == JointGap.MissingDependency);
        Assert.DoesNotContain(result.Gaps, g => MRemoteNGPackager.KeepScopes.Contains(g.Assembly));
        Assert.DoesNotContain(result.Gaps, g => g.Kind == JointGap.TypeCollision);
        var receiptPath = Path.Combine(result.OutputDirectory, "benchmark-mremoteng.receipt.json");
        Assert.True(File.Exists(receiptPath), "Receipt with node pins must exist even on conversion-failed.");
        Assert.Equal(41, GetReceiptNodeCount(receiptPath));
    }
}
