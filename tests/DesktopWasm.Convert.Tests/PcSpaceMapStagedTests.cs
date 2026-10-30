using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Convert;

namespace DesktopWasm.Convert.Tests;

/// <summary>
/// Slice-56 staged oracle for PC Space Map: the last unmapped row was the
/// generic dispatcher operation (DispatcherOperation`1, consumed via
/// Dispatcher.InvokeAsync&lt;TResult&gt; + get_Task), so the package now sits
/// at its declared platform boundary. Every remaining gap is a
/// missing-dependency row naming an absent ASP.NET/Kestrel assembly: the app
/// embeds a web server, which has no browser-executable path. The entry
/// itself converts (zero unmapped/nested/rewrite rows) and stages alone.
/// </summary>
public sealed class PcSpaceMapStagedTests
{
    [Fact]
    public void Staged_pcspacemap_hashes_verify()
    {
        Assert.Empty(StagedPcSpaceMapLoader.VerifyHashes());
    }

    [Fact]
    public void Gaps_are_exactly_the_declared_web_server_boundary()
    {
        var manifest = StagedPcSpaceMapLoader.ReadManifest();
        Assert.Equal("conversion-failed", manifest.Status);
        Assert.Equal(14, manifest.Gaps.Count);
        // Positive boundary assertions: every gap is an ASP.NET/Kestrel
        // missing-dependency row, and the full referenced set is present
        // (not silently resolved).
        Assert.All(manifest.Gaps, g => Assert.Equal("missing-dependency", g.Kind));
        Assert.All(manifest.Gaps, g =>
            Assert.Contains("Microsoft.", g.Detail, StringComparison.Ordinal));
        foreach (var name in new[]
        {
            "Microsoft.AspNetCore",
            "Microsoft.AspNetCore.Server.Kestrel",
            "Microsoft.AspNetCore.Server.Kestrel.Core",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
        })
            Assert.Contains(manifest.Gaps, g => g.Detail.Contains(name, StringComparison.Ordinal));
        // Zero slice gaps left: no converter failure rows of any kind.
        Assert.DoesNotContain(manifest.Gaps, g =>
            g.Kind is "rewrite-error" or "stale-framework-reference" or "unmapped-type" or "nested-type");
        Assert.DoesNotContain(manifest.Gaps, g => g.Detail.Contains("DispatcherOperation", StringComparison.Ordinal));
    }

    [Fact]
    public void Entry_assembly_converts_and_stages_alone()
    {
        // The entry converts cleanly (nothing to rewrite fails) and stages
        // by itself; the absent server assemblies leave no further closure.
        var manifest = StagedPcSpaceMapLoader.ReadManifest();
        Assert.Equal(["PC Space Map"], manifest.Assemblies);
        Assert.Single(StagedPcSpaceMapLoader.StagedFiles());
    }

    [Fact]
    public void Staged_entry_binds_dispatchers_to_the_engine()
    {
        // The converted entry's exact upstream dispatcher calls (verified
        // with Mono.Cecil against the net9.0-windows binary) land on engine
        // scope: both BeginInvoke overloads, InvokeAsync(Func`1) returning
        // the generic operation, and DispatcherOperation`1.get_Task.
        var entry = StagedPcSpaceMapLoader.LoadAssembly("PC Space Map");
        var refs = entry.MainModule.GetMemberReferences()
            .Where(m => m.DeclaringType is not null
                && (m.DeclaringType.Name.StartsWith("Dispatcher", StringComparison.Ordinal)
                    || m.DeclaringType.FullName.Contains("DispatcherOperation", StringComparison.Ordinal)))
            .ToList();
        Assert.NotEmpty(refs);
        Assert.All(refs, m => Assert.Equal("DesktopWasm.Wpf",
            ((Mono.Cecil.AssemblyNameReference)m.DeclaringType.Scope).Name));
        Assert.Contains(refs, m => m.Name == "get_Task"
            && m.DeclaringType.FullName.StartsWith("DesktopWasm.Wpf.DispatcherOperation`1", StringComparison.Ordinal));
        Assert.Contains(refs, m => m.Name == "InvokeAsync"
            && ((Mono.Cecil.MethodReference)m).ReturnType.FullName.StartsWith("DesktopWasm.Wpf.DispatcherOperation`1", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, m =>
            m.DeclaringType.FullName.StartsWith("System.Windows.Threading", StringComparison.Ordinal));
    }

    private sealed record StagedGap(string Assembly, string Kind, string Detail);
    private sealed record StagedManifest(string Status, List<StagedGap> Gaps, List<string> Assemblies);

    private static class StagedPcSpaceMapLoader
    {
        public static List<string> VerifyHashes()
        {
            var failures = new List<string>();
            foreach (var (_, file, bytes, expected) in ReadStaged())
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
            foreach (var assembly in root.GetProperty("assemblies").EnumerateArray())
                assemblies.Add(assembly.GetProperty("name").GetString()!);
            return new StagedManifest(root.GetProperty("status").GetString()!, gaps, assemblies);
        }

        public static Mono.Cecil.AssemblyDefinition LoadAssembly(string name)
        {
            var images = ReadStaged().ToDictionary(e => e.name, e => e.bytes, StringComparer.OrdinalIgnoreCase);
            Assert.True(images.ContainsKey(name), $"staged image missing: {name}");
            return Mono.Cecil.AssemblyDefinition.ReadAssembly(new System.IO.MemoryStream(images[name]));
        }

        private static List<(string name, string file, byte[] bytes, string expected)> ReadStaged()
        {
            var apps = AppsDir();
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(apps, "app-manifest.json")));
            var entries = new List<(string, string, byte[], string)>();
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var bytes = File.ReadAllBytes(Path.Combine(apps, file));
                entries.Add((assembly.GetProperty("name").GetString()!, file, bytes, expected));
            }
            return entries;
        }

        private static string AppsDir()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "pc-space-map", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            return Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "pc-space-map");
        }
    }
}
