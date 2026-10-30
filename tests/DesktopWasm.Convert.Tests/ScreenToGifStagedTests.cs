using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Convert;
using Mono.Cecil;

namespace DesktopWasm.Convert.Tests;

/// <summary>
/// Slice-55 staged oracle for ScreenToGif: the slice-50 converter rewrite
/// regressed the package (entry rewrite-error on PresentationCore enums,
/// Util stale-framework-reference on ValueSerializerAttribute), dropping the
/// staged closure 14 -&gt; 12. The shared-framework metadata fallback plus the
/// engine ValueSerializerAttribute redirect restore conversion, so the
/// closure returns to the declared GDI+ boundary: every remaining gap names
/// the absent System.Drawing.Common assembly, which has no
/// browser-executable path (LockBits/Scan0 pixel pipelines, HDC/HBITMAP
/// interop, codecs, metafiles). Never fabricated pixels.
/// </summary>
public sealed class ScreenToGifStagedTests
{
    [Fact]
    public void Staged_screentogif_hashes_verify()
    {
        Assert.Empty(StagedScreenToGifLoader.VerifyHashes());
    }

    [Fact]
    public void Gaps_are_exactly_the_declared_drawing_boundary()
    {
        var manifest = StagedScreenToGifLoader.ReadManifest();
        Assert.Equal("conversion-failed", manifest.Status);
        Assert.NotEmpty(manifest.Gaps);
        // Positive boundary assertions: every gap is a Drawing row, and the
        // Drawing rows are present (not silently resolved).
        Assert.All(manifest.Gaps, g =>
            Assert.Contains("System.Drawing.Common", g.Detail, StringComparison.Ordinal));
        Assert.Contains(manifest.Gaps, g => g.Kind == "missing-dependency" && g.Assembly == "ScreenToGif");
        Assert.Contains(manifest.Gaps, g => g.Kind == "missing-dependency" && g.Assembly == "ScreenToGif.Util");
        // Zero slice gaps left: no converter failure rows of any kind.
        Assert.DoesNotContain(manifest.Gaps, g =>
            g.Kind is "rewrite-error" or "stale-framework-reference" or "unmapped-type" or "nested-type");
    }

    [Fact]
    public void Staged_util_carries_engine_scoped_value_serializer()
    {
        // The converted SimpleKeyGesture keeps its annotation, rebound onto
        // the engine marker (DesktopWasm.Wpf.ValueSerializerAttribute) with
        // the framework serializer named by typeof, like the pre-regression
        // closure.
        var util = StagedScreenToGifLoader.LoadAssembly("ScreenToGif.Util");
        var gesture = util.MainModule.Types.Single(t => t.Name == "SimpleKeyGesture");
        var attr = Assert.Single(gesture.CustomAttributes,
            a => a.AttributeType.Name == "ValueSerializerAttribute");
        Assert.Equal("DesktopWasm.Wpf", ((AssemblyNameReference)attr.AttributeType.Scope).Name);
    }

    [Fact]
    public void Staged_entry_keeps_framework_enum_metadata()
    {
        // The converted entry keeps the TextPath Localizability rows and the
        // assembly-level ThemeInfo row readable (name/namespace/ctor shape);
        // scopes are pass-through, never engine-fabricated (see
        // ValueSerializerSliceTests for the writer re-pointing quirk).
        var entry = StagedScreenToGifLoader.LoadAssembly("ScreenToGif");
        var textPath = entry.MainModule.Types.Single(t => t.Name == "TextPath");
        foreach (var propName in new[] { "FontFamily", "FontSize" })
        {
            var prop = textPath.Properties.Single(p => p.Name == propName);
            var attr = Assert.Single(prop.CustomAttributes,
                a => a.AttributeType.Name == "LocalizabilityAttribute");
            Assert.Equal("System.Windows", attr.AttributeType.Namespace);
            Assert.NotEqual("DesktopWasm.Wpf", ((AssemblyNameReference)attr.AttributeType.Scope).Name);
        }
        var theme = Assert.Single(entry.CustomAttributes,
            a => a.AttributeType.Name == "ThemeInfoAttribute");
        Assert.Equal("System.Windows", theme.AttributeType.Namespace);
    }

    private sealed record StagedGap(string Assembly, string Kind, string Detail);
    private sealed record StagedManifest(string Status, List<StagedGap> Gaps);

    private static class StagedScreenToGifLoader
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
            return new StagedManifest(root.GetProperty("status").GetString()!, gaps);
        }

        public static AssemblyDefinition LoadAssembly(string name)
        {
            var images = ReadStaged().ToDictionary(e => e.name, e => e.bytes, StringComparer.OrdinalIgnoreCase);
            Assert.True(images.ContainsKey(name), $"staged image missing: {name}");
            return AssemblyDefinition.ReadAssembly(new System.IO.MemoryStream(images[name]));
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
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "screen-to-gif", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            return Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "screen-to-gif");
        }
    }
}
