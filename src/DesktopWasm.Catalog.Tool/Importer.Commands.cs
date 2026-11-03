// Commands: import / verify / refresh.
using System.Text.Json;
using DesktopWasm.AppModel;

namespace DesktopWasm.Catalog.Tool;

internal static partial class Importer
{
    private static readonly JsonSerializerOptions SnapshotOpts = new() { WriteIndented = true };

    private static void WriteSnapshot(string dir, string name, string source, string sha, object data, string utc)
    {
        var payload = new { name, source, sourceSha256 = sha, extractedUtc = utc, data };
        File.WriteAllText(Path.Combine(dir, name + ".snapshot.json"),
            JsonSerializer.Serialize(payload, SnapshotOpts) + "\n");
    }

    public static int Import(string winformsRoot, string wpfRoot, string outDir)
    {
        foreach (var (key, dir) in new[] { ("winforms-root", winformsRoot), ("wpf-root", wpfRoot) })
            if (!Directory.Exists(dir)) throw new ArgumentException($"--{key} not found: '{dir}'.");
        var utc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var sourcesDir = Path.Combine(outDir, "sources");
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(sourcesDir);

        var s = ReadSources(winformsRoot, wpfRoot);
        var sceneStatePath = Path.Combine(winformsRoot, "src", "WfWasm.CanvasHost", "SceneState.cs");
        var appTransformPath = Path.Combine(winformsRoot, "src", "WfWasm.Host.Server", "AppTransform.cs");
        var syncfusionPath = Path.Combine(winformsRoot, "src", "WfWasm.CanvasHost", "SyncfusionDemos.json");
        var benchmarksPath = Path.Combine(winformsRoot, "src", "WfWasm.CanvasHost", "DesktopBenchmarks.json");
        var libraryPath = Path.Combine(wpfRoot, "src", "DesktopWasm.BrowserHost", "wwwroot", "app-library.json");
        var demosPath = Path.Combine(wpfRoot, "src", "DesktopWasm.BrowserHost", "Demos.cs");

        WriteSnapshot(sourcesDir, "winforms-scenestate", sceneStatePath, ShaOfFile(sceneStatePath),
            new
            {
                count = s.Fixed.Count,
                scenes = s.Fixed.Select(f => new
                {
                    scene = f.Scene, slug = f.Slug, label = f.Label,
                    sourceLabel = f.SourceLabel, sourceUrl = f.SourceUrl, appFolder = f.AppFolder,
                }).ToArray(),
            }, utc);
        WriteSnapshot(sourcesDir, "winforms-apptransform", appTransformPath, ShaOfFile(appTransformPath),
            new { staticKeys = s.TransformKeys, dynamicRule = "benchmarks where build != manual plus all syncfusion demos" }, utc);
        WriteSnapshot(sourcesDir, "winforms-syncfusion", syncfusionPath, ShaOfFile(syncfusionPath),
            new
            {
                count = s.Syncfusion.RootElement.GetArrayLength(),
                slugs = s.Syncfusion.RootElement.EnumerateArray().Select(SlugOf).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            }, utc);
        WriteSnapshot(sourcesDir, "winforms-benchmarks", benchmarksPath, ShaOfFile(benchmarksPath),
            new
            {
                count = s.Benchmarks.RootElement.GetArrayLength(),
                entries = s.Benchmarks.RootElement.EnumerateArray().Select(e => new
                {
                    slug = SlugOf(e),
                    build = e.TryGetProperty("build", out var b) ? b.GetString() : null,
                    manual = e.TryGetProperty("build", out var b2) && b2.GetString() == "manual",
                }).OrderBy(e => e.slug, StringComparer.Ordinal).ToArray(),
            }, utc);
        WriteSnapshot(sourcesDir, "wpf-applibrary", libraryPath, ShaOfFile(libraryPath),
            new
            {
                count = s.Library.Count,
                entries = s.Library.OrderBy(e => e.Slug, StringComparer.Ordinal)
                    .Select(e => new { slug = e.Slug, status = e.Status, kind = e.Kind }).ToArray(),
            }, utc);
        WriteSnapshot(sourcesDir, "wpf-demos", demosPath, ShaOfFile(demosPath),
            new
            {
                count = s.Demos.Count,
                slugs = s.Demos.Select(d => d.Slug).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                subsetOfLibrary = s.Demos.All(d => s.Library.Any(l => l.Slug.Equals(d.Slug, StringComparison.OrdinalIgnoreCase))),
            }, utc);
        WriteSnapshot(sourcesDir, "wpf-manifests",
            Path.Combine(wpfRoot, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps"), "",
            new
            {
                count = s.Manifests.Count,
                entries = s.Manifests.OrderBy(m => m.Slug, StringComparer.Ordinal).Select(m => new
                {
                    slug = m.Slug, manifestSha256 = s.ManifestShas[m.Slug],
                    status = m.Status, entryAssembly = m.EntryAssembly, assemblies = m.Assemblies.Length,
                }).ToArray(),
            }, utc);

        var apps = BuildRegistry(s);
        // Regeneration preservation (RVW0900-CATALOG): pure-source rebuilds
        // must not drop the producer-owned joint record or digest-verified
        // staging. The joint record rebuilds from the packaging producer's
        // staged bytes; other availability re-verifies from the prior
        // registry's local evidence (digest-trust: changed bytes demote
        // honestly, never restore stale flags).
        var repoRoot = ResolveRepoRoot(outDir);
        var joint = TryBuildJointFromPackageDir(
            Path.Combine(repoRoot, JointPackageDir), JointPackageDir);
        if (joint is not null)
        {
            apps.RemoveAll(a => string.Equals(a.Id, joint.Id, StringComparison.OrdinalIgnoreCase));
            apps.Add(joint);
        }
        var priorPath = Path.Combine(outDir, "apps.json");
        if (File.Exists(priorPath))
        {
            var prior = CatalogSerializer.Deserialize(File.ReadAllText(priorPath));
            static bool Exists(string p) => Directory.Exists(p) || File.Exists(p);
            apps = ReconcileWithPrior(apps, prior.Apps, Exists);
        }
        var conflicts = CatalogValidator.FindConflicts(apps);
        var registry = new AppRegistry(apps);
        File.WriteAllText(Path.Combine(outDir, "apps.json"), CatalogSerializer.Serialize(apps));
        File.WriteAllText(Path.Combine(outDir, "conflict-map.json"),
            JsonSerializer.Serialize(new
            {
                generatedUtc = utc,
                conflicts,
                notes = ConflictNotes(apps),
            }, SnapshotOpts) + "\n");

        var wf = apps.Count(a => a.Capabilities == AppCapability.WinForms);
        var wpf = apps.Count(a => a.Capabilities == AppCapability.Wpf);
        var mixed = apps.Count(a => a.Capabilities == AppCapability.Mixed);
        Console.WriteLine($"imported {registry.Count} entries (winforms={wf} wpf={wpf} mixed={mixed} conflicts={conflicts.Count})");
        Console.WriteLine($"winforms fixed={s.Fixed.Count} syncfusion={s.Syncfusion.RootElement.GetArrayLength()} benchmarks={s.Benchmarks.RootElement.GetArrayLength()}");
        Console.WriteLine($"wpf library={s.Library.Count} demos={s.Demos.Count} manifests={s.Manifests.Count}");
        if (conflicts.Count > 0) throw new InvalidDataException("Conflicts: " + string.Join("; ", conflicts));
        return 0;
    }

    private static string[] ConflictNotes(List<AppDescriptor> apps)
    {
        // Alias-only similarities: same stem after stripping family prefixes.
        static string Stem(string id)
        {
            foreach (var prefix in new[] { "syncfusion-", "telerik-", "benchmark-", "wpf-", "wf-" })
                if (id.StartsWith(prefix, StringComparison.Ordinal)) return id[prefix.Length..];
            return id;
        }
        var notes = new SortedSet<string>(StringComparer.Ordinal);
        var ids = apps.Select(a => a.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
        for (var i = 0; i < ids.Count; i++)
            for (var j = i + 1; j < ids.Count; j++)
            {
                var a = Stem(ids[i]);
                var b = Stem(ids[j]);
                if ((a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)) &&
                    Math.Abs(a.Length - b.Length) <= 12)
                    notes.Add($"'{ids[i]}' ~ '{ids[j]}': similar stems, distinct ids, no alias collision");
            }
        return notes.Take(40).ToArray();
    }

    public static int Verify(string winformsRoot, string wpfRoot, string catalogDir)
    {
        var failures = new List<string>();
        var sourcesDir = Path.Combine(catalogDir, "sources");
        var utc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        JsonElement ReadSnap(string name)
        {
            var path = Path.Combine(sourcesDir, name + ".snapshot.json");
            if (!File.Exists(path)) throw new ArgumentException($"Missing snapshot '{name}'.");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.Clone();
        }
        void CheckFile(string snapName, string livePath)
        {
            var snap = ReadSnap(snapName);
            var live = ShaOfFile(livePath);
            if (!snap.GetProperty("sourceSha256").GetString()!.Equals(live, StringComparison.OrdinalIgnoreCase))
                failures.Add($"drift: '{snapName}' source changed (snapshot {snap.GetProperty("sourceSha256").GetString()} != live {live})");
        }

        string Wf(params string[] parts) => Path.Combine([winformsRoot, ..parts]);
        string Wpf(params string[] parts) => Path.Combine([wpfRoot, ..parts]);
        foreach (var (snap, live) in new[]
        {
            ("winforms-scenestate", Wf("src", "WfWasm.CanvasHost", "SceneState.cs")),
            ("winforms-apptransform", Wf("src", "WfWasm.Host.Server", "AppTransform.cs")),
            ("winforms-syncfusion", Wf("src", "WfWasm.CanvasHost", "SyncfusionDemos.json")),
            ("winforms-benchmarks", Wf("src", "WfWasm.CanvasHost", "DesktopBenchmarks.json")),
            ("wpf-applibrary", Wpf("src", "DesktopWasm.BrowserHost", "wwwroot", "app-library.json")),
            ("wpf-demos", Wpf("src", "DesktopWasm.BrowserHost", "Demos.cs")),
        })
        {
            if (!File.Exists(live)) failures.Add($"missing live input '{live}'");
            else CheckFile(snap, live);
        }

        // Live recount must match snapshots and apps.json.
        Sources? liveSources = null;
        try { liveSources = ReadSources(winformsRoot, wpfRoot); }
        catch (Exception ex) { failures.Add("live recount failed: " + ex.Message); }
        var appsPath = Path.Combine(catalogDir, "apps.json");
        AppRegistry? registry = null;
        try { registry = CatalogSerializer.Deserialize(File.ReadAllText(appsPath)); }
        catch (Exception ex) { failures.Add("apps.json invalid: " + ex.Message); }
        if (liveSources is not null && registry is not null)
        {
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var scene in liveSources.Fixed) expected.Add(scene.Slug);
            foreach (var e in liveSources.Syncfusion.RootElement.EnumerateArray()) expected.Add(SlugOf(e));
            foreach (var e in liveSources.Benchmarks.RootElement.EnumerateArray()) expected.Add(SlugOf(e));
            foreach (var lib in liveSources.Library) expected.Add(lib.Slug);
            expected.Add("mixed-session-pilot");
            expected.Add("testapp-joint"); // permanent joint record (packaging producer + catalog receipt), like the pilot
            var actual = registry.Apps.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var missing in expected.OrderBy(x => x, StringComparer.Ordinal).Where(x => !actual.Contains(x)))
                failures.Add($"apps.json missing live entry '{missing}'");
            foreach (var extra in actual.OrderBy(x => x, StringComparer.Ordinal).Where(x => !expected.Contains(x)))
                failures.Add($"apps.json has entry '{extra}' with no live source");
            var conflicts = CatalogValidator.FindConflicts(registry.Apps);
            var mapPath = Path.Combine(catalogDir, "conflict-map.json");
            using var mapDoc = JsonDocument.Parse(File.ReadAllText(mapPath));
            var recorded = mapDoc.RootElement.GetProperty("conflicts").EnumerateArray()
                .Select(c => c.GetString()!).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (!conflicts.SequenceEqual(recorded))
                failures.Add("conflict-map.json does not match recomputed conflicts");
        }

        Console.WriteLine($"verify at {utc}: {(failures.Count == 0 ? "clean" : failures.Count + " problem(s)")}");
        foreach (var f in failures) Console.WriteLine("  - " + f);
        if (failures.Count > 0) throw new InvalidDataException("Verification failed.");
        return 0;
    }

    /// <summary>Cumulative refresh: the union of all --receipts files is the
    /// new batch, and still-present earlier staging is carried forward from
    /// its own local: evidence (bytes re-verified against the disk per
    /// <see cref="ReceiptVerifier"/>: files by content hash, manifest
    /// directories by manifest digest plus manifest-claimed closure,
    /// manifest-less directories as asserted existence-only provenance), so
    /// a partial batch never silently demotes earlier inputs and never
    /// silently carries stale verified claims for changed bytes. A receipt
    /// naming a missing path, a mismatching digest, or a vanished/changed
    /// staged path, fails or demotes honestly; nothing is written when any
    /// fresh receipt is invalid.</summary>
    public static int Refresh(string catalogDir, string receiptsPath) =>
        Refresh(catalogDir, new[] { receiptsPath });

    public static int Refresh(string catalogDir, IReadOnlyList<string> receiptsPaths)
    {
        var appsPath = Path.Combine(catalogDir, "apps.json");
        var registry = CatalogSerializer.Deserialize(File.ReadAllText(appsPath));
        var fresh = new Dictionary<string, LocalInputMapper.StagedReceipt>(StringComparer.OrdinalIgnoreCase);
        foreach (var rp in receiptsPaths)
        {
            if (!File.Exists(rp)) throw new ArgumentException($"Receipts file not found: '{rp}'.");
            using var doc = JsonDocument.Parse(File.ReadAllText(rp));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"Receipts file '{rp}' must be a JSON array.");
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var r = new LocalInputMapper.StagedReceipt(
                    e.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    e.TryGetProperty("localPath", out var lp) ? lp.GetString() ?? "" : "",
                    e.TryGetProperty("sha256", out var sh) ? sh.GetString() ?? "" : "",
                    e.TryGetProperty("stagedUtc", out var u) ? u.GetString()! : "");
                if (string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.LocalPath) ||
                    string.IsNullOrWhiteSpace(r.Sha256))
                    throw new InvalidDataException($"Invalid receipt entry in '{rp}': id/localPath/sha256 required.");
                if (!Directory.Exists(r.LocalPath) && !File.Exists(r.LocalPath))
                    throw new InvalidDataException($"Staged input missing for '{r.Id}': '{r.LocalPath}'.");
                ReceiptVerifier.ThrowIfDigestMismatch(r);
                fresh[r.Id] = r;
            }
        }
        static bool Exists(string p) => Directory.Exists(p) || File.Exists(p);
        var carried = LocalInputMapper.CarryForwardReceipts(registry.Apps, fresh, Exists);
        foreach (var (k, v) in carried) fresh[k] = v;
        var refreshed = registry.Apps.Select(a => LocalInputMapper.Refresh(a, fresh)).ToList();
        foreach (var app in refreshed)
            CatalogValidator.ValidateDescriptor(app);
        var next = new AppRegistry(refreshed);
        File.WriteAllText(appsPath, CatalogSerializer.Serialize(refreshed));
        var avail = refreshed.Count(a => a.Availability == AppAvailability.Available);
        Console.WriteLine($"refresh: {next.Count} entries, available={avail} (fresh={fresh.Count - carried.Count} carried={carried.Count})");
        return 0;
    }
}
