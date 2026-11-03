using DesktopWasm.AppModel;

namespace DesktopWasm.Catalog;

/// <summary>Legacy import adapters: frozen-root metadata in, canonical
/// descriptors out. Adapters read data files (never engine assemblies);
/// the import tool feeds them and records snapshot receipts.</summary>
public static class LegacyAdapters
{
    public sealed record WinFormsSceneEntry(
        string Scene, string Slug, string Label, string? SourceLabel, string? SourceUrl,
        string? AppFolder, string? BenchmarkFamily, string? BenchmarkBuild);

    public sealed record SyncfusionEntry(
        string Slug, string Label, string Project, string Assembly, string Family, string Purpose);

    public sealed record BenchmarkEntry(
        string Slug, string Label, string Family, string SourceUrl, string Revision,
        string Project, string Assembly, string Build, string License, string Notes);

    public sealed record WpfLibraryEntry(
        string Slug, string Label, string Description, string Category, string Icon, string Kind,
        string Status, string? Source, string? Repository, string? Commit, string? Project,
        string? License, string? Boundary, bool Recipe);

    public sealed record WpfManifestEntry(
        string Slug, int SchemaVersion, string MapVersion, string Status, string EntryAssembly,
        PackageAssembly[] Assemblies, PackageGap[] Gaps, string? SourceCommit);

    private static string Flat(string slug) => slug.Replace("-", string.Empty, StringComparison.Ordinal);

    public static AppDescriptor FromWinFormsScene(
        WinFormsSceneEntry scene, string appFolder, bool staged, string stagedNote)
    {
        var aliases = new List<string> { "/app/" + scene.Slug };
        if (!string.Equals(appFolder, scene.Slug, StringComparison.OrdinalIgnoreCase))
            aliases.Add(appFolder);
        var flat = Flat(scene.Slug);
        if (!flat.Equals(scene.Slug, StringComparison.OrdinalIgnoreCase)) aliases.Add(flat);
        if (!appFolder.Equals(scene.Slug, StringComparison.OrdinalIgnoreCase))
        {
            var folderFlat = Flat(appFolder);
            if (!folderFlat.Equals(scene.Slug, StringComparison.OrdinalIgnoreCase) &&
                !folderFlat.Equals(flat, StringComparison.OrdinalIgnoreCase))
                aliases.Add(folderFlat);
        }
        return new AppDescriptor(
            scene.Slug, aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            scene.Label, scene.SourceLabel ?? "WinForms demo scene",
            scene.BenchmarkFamily is not null ? "Benchmarks" : scene.Slug.StartsWith("syncfusion-", StringComparison.Ordinal) ? "Syncfusion" :
            scene.Slug.StartsWith("telerik-", StringComparison.Ordinal) ? "Telerik" : "WinForms",
            "", new AppProvenance("winforms",
                scene.BenchmarkFamily is not null ? "benchmark" : scene.Slug.StartsWith("syncfusion-", StringComparison.Ordinal) ? "vendor-demo" : "original",
                scene.SourceUrl, null, null, null, null, null, false),
            new AppEntry("native-demo", scene.Slug, null, null),
            AppCapability.WinForms, "wf-rewrite-v1", null,
            staged ? AppAvailability.Available : AppAvailability.MissingInput,
            "ready", stagedNote, staged ? [stagedNote] : [],
            staged ? [] : [$"app folder '{appFolder}' not staged in this root"]);
    }

    public static AppDescriptor FromWpf(
        WpfLibraryEntry lib, bool nativeDemo, WpfManifestEntry? manifest)
    {
        var capabilities = AppCapability.Wpf;
        AppEntry entry;
        PackageIdentity? package = null;
        if (manifest is not null)
        {
            package = new PackageIdentity(
                manifest.SchemaVersion, manifest.MapVersion, manifest.Status,
                manifest.Assemblies, manifest.Gaps, manifest.SourceCommit);
            entry = new AppEntry("converted-app", manifest.EntryAssembly, manifest.Slug,
                manifest.Status == "converted" ? "Converted app boots via package loader." : "Preparation failed; see gaps.");
        }
        else
        {
            entry = nativeDemo
                ? new AppEntry("native-demo", lib.Slug, null, null)
                : new AppEntry("converted-app", lib.Slug, null, "No package staged.");
        }
        var (availability, note, gaps) = (lib.Status, manifest) switch
        {
            ("ready", not null) when manifest.Status == "converted" =>
                (AppAvailability.MissingInput,
                 $"Package staged in frozen WPF-wasm root under apps/{manifest.Slug}; copy to this root pending (import lane).",
                 (string[])[$"package for '{manifest.Slug}' not copied to this root"]),
            ("ready", _) =>
                (AppAvailability.MissingInput, "Engine-native demo; runs only after WPF host import (import lane).", (string[])[]),
            (_, not null) when IsPreparationEligible(lib, manifest) =>
                // Decision RVW-20261030-2100-4467 §15.1: a VALID converted
                // manifest outranks stale upstream not-converted metadata for
                // PREPARATION eligibility only. UpstreamStatus keeps the stale
                // library claim; availability stays MissingInput until staged
                // receipts verify the bytes (never Available here).
                (AppAvailability.MissingInput,
                 $"Converted manifest apps/{manifest.Slug} ({manifest.Assemblies.Length} assemblies, 0 gaps) outranks stale upstream status '{lib.Status}' for preparation eligibility only; package copy to this root pending (import lane).",
                 (string[])[$"package copy for '{manifest.Slug}' hash-pinned from manifest"]),
            _ =>
                (AppAvailability.NotConverted, lib.Boundary ?? "No converted package yet.", new[] { lib.Boundary ?? "not-converted" }),
        };
        return new AppDescriptor(
            lib.Slug, ["/demo/" + lib.Slug],
            lib.Label, lib.Description, lib.Category, lib.Icon,
            new AppProvenance("wpf", lib.Kind == "toolkit" ? "native-demo" : "original",
                lib.Source, lib.Repository, lib.Commit, lib.Project, lib.License, lib.Boundary, lib.Recipe),
            entry, capabilities, "wpf-convert-v1", package,
            availability, lib.Status, note, manifest is not null ? [$"frozen-root manifest apps/{manifest.Slug}"] : [],
            gaps);
    }

    /// <summary>Preparation eligibility (decision RVW-20261030-2100-4467 §15.1):
    /// a manifest outranks stale upstream not-converted metadata ONLY when it
    /// is coherent and fully converted: same slug as the library entry,
    /// schema 1, matching map version, status converted, a non-empty closure
    /// whose 64-hex pins include the entry assembly, and zero gaps.
    /// Contradictory (slug/entry mismatch), invalid, gapped, or non-converted
    /// manifests are never promoted by this rule.</summary>
    public static bool IsPreparationEligible(WpfLibraryEntry lib, WpfManifestEntry manifest)
    {
        if (!string.Equals(manifest.Slug, lib.Slug, StringComparison.OrdinalIgnoreCase)) return false;
        if (manifest.SchemaVersion != 1 || manifest.MapVersion != "wpf-convert-v1") return false;
        if (manifest.Status != "converted" || manifest.Gaps.Length != 0) return false;
        if (manifest.Assemblies.Length == 0 || string.IsNullOrWhiteSpace(manifest.EntryAssembly)) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in manifest.Assemblies)
        {
            if (a is null || string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.File)) return false;
            if (a.Sha256 is null || a.Sha256.Length != 64 || !a.Sha256.All(Uri.IsHexDigit)) return false;
            names.Add(a.Name);
        }
        return names.Contains(manifest.EntryAssembly);
    }

    /// <summary>Legacy manifests percent-encode spaces in artifact file names
    /// (pc-space-map ships "PC%20Space%20Map.&lt;sha&gt;.bin"). The adapter
    /// restores the literal space; any other % escape is rejected, never
    /// decoded, so encoded traversal (%2e/%2f/...) cannot become a path.</summary>
    public static string NormalizeArtifactFile(string file, string slug)
    {
        if (!file.Contains('%')) return file;
        var unescaped = file.Replace("%20", " ", StringComparison.OrdinalIgnoreCase);
        if (unescaped.Contains('%'))
            throw new InvalidDataException($"Manifest '{slug}' has unsupported escape in artifact file '{file}'.");
        return unescaped;
    }

    /// <summary>WinForms on-demand transform adapter: the legacy endpoint serves
    /// rewritten .dll/.exe bytes (content-hash URLs, prefetch closure) rather
    /// than WPF .bin manifests. Both shapes land as canonical
    /// <see cref="PackageIdentity"/> with an explicit artifact tag, so a valid
    /// imported app is never rejected merely for its legacy format.</summary>
    public static PackageIdentity FromWinFormsTransform(
        string entryAssembly,
        PackageAssembly[] assemblies,
        string mapVersion = "wf-rewrite-v1",
        string? sourceCommit = null)
    {
        var tagged = assemblies
            .Select(a => a with { Artifact = "wf-asm" })
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new PackageIdentity(1, mapVersion, "converted", tagged, [], sourceCommit);
    }

    public static AppDescriptor MixedPilot() => new(
        "mixed-session-pilot", ["mixed-pilot"],
        "Mixed session pilot", "One session hosting WinForms and WPF together (pilot).",
        "Mixed", "",
        new AppProvenance("mixed", "pilot", null, null, null, null, null,
            "No mixed session proven yet; interop lane owns the bridge.", false),
        new AppEntry("mixed-session", "MixedPilot", null,
            "Unblocked only when the interop lane names the bridge contract and proves the session."),
        AppCapability.Mixed, "mixed-v1", null,
        AppAvailability.Unavailable, "pilot",
        "No mixed WinForms+WPF session proven; owned by the interop lane.",
        [], ["mixed session not proven"]);
}
