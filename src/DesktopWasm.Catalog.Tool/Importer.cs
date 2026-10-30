// Readers: frozen-root metadata parsing + snapshot receipts.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopWasm.AppModel;

namespace DesktopWasm.Catalog.Tool;

internal static partial class Importer
{
    internal sealed record Snapshot(string Name, string Source, string SourceSha256, string ExtractedUtc, JsonElement Data);

    internal static string ShaOfFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static string ShaOfString(string text) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // ---- WinForms: SceneState.cs fixed entries ----

    internal sealed record FixedScene(string Scene, string Slug, string Label, string? SourceLabel, string? SourceUrl, string AppFolder);

    [GeneratedRegex(@"new\s+SceneEntry\(AppScene\.(\w+),\s*(""(?:[^""\\]|\\.)*""|null)\s*,\s*(""(?:[^""\\]|\\.)*""|null)\s*,\s*(""(?:[^""\\]|\\.)*""|null)\s*,\s*(""(?:[^""\\]|\\.)*""|null)\s*,\s*(""(?:[^""\\]|\\.)*""|null)\s*\)")]
    private static partial Regex SceneEntryRegex();

    [GeneratedRegex(@"AppScene\.(\w+)\s*=>\s*""([^""]+)""")]
    private static partial Regex SlugMapRegex();

    [GeneratedRegex(@"AppScene\.(\w+)\s*=>\s*(null|""([^""]+)"")")]
    private static partial Regex FolderMapRegex();

    private static string? Unquote(string token) =>
        token == "null" ? null : token[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");

    internal static List<FixedScene> ReadFixedScenes(string sceneStatePath)
    {
        var text = File.ReadAllText(sceneStatePath);
        // The file holds two switches over AppScene: ToSlug (slug per scene)
        // and AppFolderOf (folder override or null). Parse each in its own
        // region so the folder pattern never matches slug arms.
        var slugAt = text.IndexOf("ToSlug(AppScene", StringComparison.Ordinal);
        var folderAt = text.IndexOf("AppFolderOf(AppScene", StringComparison.Ordinal);
        if (slugAt < 0 || folderAt <= slugAt)
            throw new InvalidDataException("SceneState.cs switch layout changed.");
        var slugRegion = text[slugAt..folderAt];
        var folderRegion = text[folderAt..];
        var slugs = SlugMapRegex().Matches(slugRegion).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);
        var folders = FolderMapRegex().Matches(folderRegion).ToDictionary(m => m.Groups[1].Value, m => m.Groups[3].Success ? m.Groups[3].Value : null, StringComparer.Ordinal);
        var scenes = new List<FixedScene>();
        foreach (Match m in SceneEntryRegex().Matches(text))
        {
            var scene = m.Groups[1].Value;
            if (!slugs.TryGetValue(scene, out var slug))
                throw new InvalidDataException($"Scene '{scene}' has no slug mapping.");
            var folder = folders.TryGetValue(scene, out var f) ? f ?? slug : slug;
            scenes.Add(new FixedScene(scene, slug, Unquote(m.Groups[2].Value) ?? scene,
                Unquote(m.Groups[3].Value), Unquote(m.Groups[4].Value), folder));
        }
        return scenes.OrderBy(s => s.Slug, StringComparer.Ordinal).ToList();
    }

    // ---- WinForms: benchmark + syncfusion JSON ----

    internal static JsonDocument ReadJsonFile(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonDocument.Parse(stream);
    }

    internal static string SlugOf(JsonElement e) =>
        e.TryGetProperty("slug", out var s) ? s.GetString() ?? throw new InvalidDataException("Null slug.") :
        e.TryGetProperty("Slug", out var s2) ? s2.GetString() ?? throw new InvalidDataException("Null Slug.") :
        throw new InvalidDataException("Entry without slug.");

    // ---- WinForms: AppTransform.cs static keys ----

    [GeneratedRegex(@"^\s*\[\""([^""]+)\""\]\s*=\s*new\(", RegexOptions.Multiline)]
    private static partial Regex AppKeyRegex();

    internal static List<string> ReadAppTransformKeys(string appTransformPath) =>
        AppKeyRegex().Matches(File.ReadAllText(appTransformPath))
            .Select(m => m.Groups[1].Value)
            .OrderBy(k => k, StringComparer.Ordinal).ToList();

    // ---- WPF: app-library.json ----

    internal static List<LegacyAdapters.WpfLibraryEntry> ReadAppLibrary(string path)
    {
        using var doc = ReadJsonFile(path);
        return doc.RootElement.EnumerateArray().Select(e => new LegacyAdapters.WpfLibraryEntry(
            e.GetProperty("slug").GetString()!,
            e.GetProperty("label").GetString()!,
            e.GetProperty("description").GetString()!,
            e.GetProperty("category").GetString()!,
            e.GetProperty("icon").GetString()!,
            e.GetProperty("kind").GetString()!,
            e.GetProperty("status").GetString()!,
            e.TryGetProperty("source", out var v) ? v.GetString() : null,
            e.TryGetProperty("repository", out var r) ? r.GetString() : null,
            e.TryGetProperty("commit", out var c) ? c.GetString() : null,
            e.TryGetProperty("project", out var p) ? p.GetString() : null,
            e.TryGetProperty("license", out var l) ? l.GetString() : null,
            e.TryGetProperty("boundary", out var b) ? b.GetString() : null,
            e.TryGetProperty("recipe", out var rec) && rec.GetBoolean())).ToList();
    }

    // ---- WPF: Demos.cs All slugs ----

    [GeneratedRegex(@"new\(""(?<slug>[^""]+)"",\s*""(?<label>[^""]*)"",\s*""(?<desc>[^""]*)"",\s*""(?<group>[^""]*)""")]
    private static partial Regex DemoRegex();

    internal static List<(string Slug, string Label, string Group)> ReadDemoSlugs(string demosPath) =>
        DemoRegex().Matches(File.ReadAllText(demosPath))
            .Select(m => (m.Groups["slug"].Value, m.Groups["label"].Value, m.Groups["group"].Value))
            .ToList();

    // ---- WPF: app manifests ----

    internal static LegacyAdapters.WpfManifestEntry ReadManifest(string path)
    {
        using var doc = ReadJsonFile(path);
        var root = doc.RootElement;
        var slug = root.GetProperty("slug").GetString()!;
        return new LegacyAdapters.WpfManifestEntry(
            slug,
            root.GetProperty("schemaVersion").GetInt32(),
            root.GetProperty("mapVersion").GetString()!,
            root.GetProperty("status").GetString()!,
            root.GetProperty("entryAssembly").GetString()!,
            root.GetProperty("assemblies").EnumerateArray().Select(a => new PackageAssembly(
                a.GetProperty("name").GetString()!,
                LegacyAdapters.NormalizeArtifactFile(a.GetProperty("file").GetString()!, slug),
                a.GetProperty("sha256").GetString()!,
                a.GetProperty("references").EnumerateArray().Select(r => r.GetString()!).ToArray(),
                a.TryGetProperty("inputSha256", out var h) ? h.GetString() : null)).ToArray(),
            root.GetProperty("gaps").EnumerateArray().Select(g => new PackageGap(
                g.GetProperty("assembly").GetString()!,
                g.GetProperty("kind").GetString()!,
                g.GetProperty("detail").GetString()!)).ToArray(),
            root.TryGetProperty("sourceCommit", out var c) ? c.GetString() : null);
    }

    // ---- Build ----

    internal sealed record Sources(
        List<FixedScene> Fixed, List<string> TransformKeys,
        JsonDocument Syncfusion, JsonDocument Benchmarks,
        List<LegacyAdapters.WpfLibraryEntry> Library,
        List<(string Slug, string Label, string Group)> Demos,
        List<LegacyAdapters.WpfManifestEntry> Manifests,
        Dictionary<string, string> ManifestShas);

    internal static Sources ReadSources(string winformsRoot, string wpfRoot)
    {
        string Wf(params string[] parts) => Path.Combine([winformsRoot, ..parts]);
        string Wpf(params string[] parts) => Path.Combine([wpfRoot, ..parts]);
        var fixedScenes = ReadFixedScenes(Wf("src", "WfWasm.CanvasHost", "SceneState.cs"));
        var transformKeys = ReadAppTransformKeys(Wf("src", "WfWasm.Host.Server", "AppTransform.cs"));
        var syncfusion = ReadJsonFile(Wf("src", "WfWasm.CanvasHost", "SyncfusionDemos.json"));
        var benchmarks = ReadJsonFile(Wf("src", "WfWasm.CanvasHost", "DesktopBenchmarks.json"));
        var library = ReadAppLibrary(Wpf("src", "DesktopWasm.BrowserHost", "wwwroot", "app-library.json"));
        var demos = ReadDemoSlugs(Wpf("src", "DesktopWasm.BrowserHost", "Demos.cs"));
        var appsDir = Wpf("src", "DesktopWasm.BrowserHost", "wwwroot", "apps");
        var manifests = new List<LegacyAdapters.WpfManifestEntry>();
        var manifestShas = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var dir in Directory.GetDirectories(appsDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(dir, "app-manifest.json");
            if (!File.Exists(manifestPath)) continue;
            var manifest = ReadManifest(manifestPath);
            manifests.Add(manifest);
            manifestShas[manifest.Slug] = ShaOfFile(manifestPath);
        }
        return new Sources(fixedScenes, transformKeys, syncfusion, benchmarks, library, demos, manifests, manifestShas);
    }

    internal static List<AppDescriptor> BuildRegistry(Sources s)
    {
        var apps = new List<AppDescriptor>();
        foreach (var scene in s.Fixed)
            apps.Add(LegacyAdapters.FromWinFormsScene(
                new LegacyAdapters.WinFormsSceneEntry(scene.Scene, scene.Slug, scene.Label, scene.SourceLabel, scene.SourceUrl, scene.AppFolder, null, null),
                scene.AppFolder, staged: false,
                "Import lane has not staged this app folder in the new root."));
        foreach (var demo in s.Syncfusion.RootElement.EnumerateArray())
        {
            var slug = SlugOf(demo);
            apps.Add(LegacyAdapters.FromWinFormsScene(
                new LegacyAdapters.WinFormsSceneEntry(slug, slug,
                    demo.TryGetProperty("label", out var l) ? l.GetString()! : slug,
                    "Syncfusion WinForms demos", "https://github.com/syncfusion/winforms-demos",
                    slug, null, null),
                slug, staged: false, "Import lane has not staged this Syncfusion input in the new root."));
        }
        foreach (var bench in s.Benchmarks.RootElement.EnumerateArray())
        {
            var slug = SlugOf(bench);
            apps.Add(LegacyAdapters.FromWinFormsScene(
                new LegacyAdapters.WinFormsSceneEntry(slug, slug,
                    bench.TryGetProperty("label", out var l) ? l.GetString()! : slug,
                    bench.TryGetProperty("family", out var f) ? f.GetString()! + " benchmark" : "benchmark",
                    bench.TryGetProperty("sourceUrl", out var u) ? u.GetString() : null,
                    slug,
                    bench.TryGetProperty("family", out var f2) ? f2.GetString() : null,
                    bench.TryGetProperty("build", out var b) ? b.GetString() : null),
                slug, staged: false, "Import lane has not staged this benchmark input in the new root."));
        }
        var manifests = s.Manifests.ToDictionary(m => m.Slug, StringComparer.OrdinalIgnoreCase);
        var demoSlugs = new HashSet<string>(s.Demos.Select(d => d.Slug), StringComparer.OrdinalIgnoreCase);
        foreach (var lib in s.Library)
        {
            manifests.TryGetValue(lib.Slug, out var manifest);
            apps.Add(LegacyAdapters.FromWpf(lib, nativeDemo: demoSlugs.Contains(lib.Slug) && manifest is null, manifest));
        }
        foreach (var demo in s.Demos.Where(d => !s.Library.Any(l => l.Slug.Equals(d.Slug, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException($"DemoCatalog entry '{demo.Slug}' missing from app-library.json.");
        apps.Add(LegacyAdapters.MixedPilot());
        foreach (var app in apps)
            CatalogValidator.ValidateDescriptor(app);
        return apps.OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
    }
}
