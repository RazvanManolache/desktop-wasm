// Preservation: pure-source regeneration must not drop producer-owned or
// digest-verified records. BuildRegistry covers only frozen-root snapshots;
// the joint record comes from the packaging producer's staged bytes and
// Available/Staged states come from hash-pinned local staging. Both are
// rebuilt/re-verified here, never restored as stale flags.
using System.Text.Json;
using DesktopWasm.AppModel;

namespace DesktopWasm.Catalog.Tool;

internal static partial class Importer
{
    internal const string JointId = "testapp-joint";
    private const string JointPackageDir = "inputs/mixed/testapp-package";
    private const string JointMapVersion = "wpf-convert-v1";

    /// <summary>Repo root for producer inputs: the parent of outDir when it
    /// carries inputs/, else the process working directory (the tool runs
    /// from the repo root, like refresh).</summary>
    internal static string ResolveRepoRoot(string outDir)
    {
        var parent = Directory.GetParent(Path.GetFullPath(outDir))?.FullName;
        if (parent is not null && Directory.Exists(Path.Combine(parent, "inputs", "mixed", "testapp-package")))
            return parent;
        return Directory.GetCurrentDirectory();
    }

    /// <summary>Rebuild the permanent joint record from the packaging
    /// producer's staged bytes (manifest + closure + joint receipt bridge).
    /// Returns null only when no producer staging is present. A present but
    /// invalid producer fails closed: throwing beats silently dropping or
    /// carrying a stale record. The record is Staged (never Available):
    /// bytes-verified staging only, serving + behavior unverified.</summary>
    internal static AppDescriptor? TryBuildJointFromPackageDir(string packageDir, string evidencePath)
    {
        if (!Directory.Exists(packageDir))
            return null;
        var manifestPath = Path.Combine(packageDir, "app-manifest.json");
        var receiptPath = Path.Combine(packageDir, "testapp-joint.receipt.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"Joint producer staging '{packageDir}' has no app-manifest.json.");
        if (!File.Exists(receiptPath))
            throw new InvalidDataException($"Joint producer staging '{packageDir}' has no testapp-joint.receipt.json.");
        // Lowercase hex, matching the committed receipts and the live record.
        var manifestSha = ShaOfFile(manifestPath).ToLowerInvariant();
        using var manifest = ReadJsonFile(manifestPath);
        var root = manifest.RootElement;
        var slug = root.GetProperty("slug").GetString()!;
        if (!string.Equals(slug, JointId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Joint manifest slug '{slug}' is not '{JointId}'.");
        var mapVersion = root.GetProperty("mapVersion").GetString()!;
        var status = root.GetProperty("status").GetString()!;
        var entryAssembly = root.GetProperty("entryAssembly").GetString()!;
        if (!string.Equals(mapVersion, JointMapVersion, StringComparison.Ordinal) || status != "converted")
            throw new InvalidDataException($"Joint manifest is not a converted '{JointMapVersion}' package.");
        if (!ReceiptVerifier.ClosureMatches(packageDir, manifestPath))
            throw new InvalidDataException($"Joint producer closure mismatch in '{packageDir}': manifest/closure bytes do not verify.");
        using var receipt = ReadJsonFile(receiptPath);
        var receiptRoot = receipt.RootElement;
        if (!string.Equals(receiptRoot.GetProperty("slug").GetString(), JointId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Joint receipt slug does not match the manifest.");
        if (!receiptRoot.TryGetProperty("bridgeProvider", out var bridge) ||
            bridge.ValueKind != JsonValueKind.Object ||
            !bridge.TryGetProperty("resolver", out var provider) ||
            !bridge.TryGetProperty("hostType", out var contract) ||
            string.IsNullOrWhiteSpace(provider.GetString()) ||
            string.IsNullOrWhiteSpace(contract.GetString()))
            throw new InvalidDataException("Joint receipt carries no typed bridge identity (resolver + hostType).");
        var assemblies = root.GetProperty("assemblies").EnumerateArray().Select(a =>
            new PackageAssembly(
                a.GetProperty("name").GetString()!,
                a.GetProperty("file").GetString()!,
                a.GetProperty("sha256").GetString()!,
                a.GetProperty("references").EnumerateArray().Select(r => r.GetString()!).ToArray(),
                a.TryGetProperty("inputSha256", out var h) ? h.GetString() : null,
                "mixed")).ToArray();
        var package = new PackageIdentity(
            root.GetProperty("schemaVersion").GetInt32(), mapVersion, status,
            assemblies,
            root.TryGetProperty("gaps", out var gaps) && gaps.ValueKind == JsonValueKind.Array
                ? gaps.EnumerateArray().Select(g => new PackageGap(
                    g.GetProperty("assembly").GetString()!,
                    g.GetProperty("kind").GetString()!,
                    g.GetProperty("detail").GetString()!)).ToArray()
                : [],
            root.TryGetProperty("sourceCommit", out var c) ? c.GetString() : null);
        CatalogValidator.ValidatePackage(JointId, JointMapVersion, entryAssembly, package);
        var app = new AppDescriptor(
            JointId, ["/app/testapp-joint", "/demo/testapp-joint"],
            "AvalonDock TestApp (joint)",
            "AvalonDock TestApp converted to a joint WinForms+WPF package (staged, serving + behavior unverified).",
            "Mixed", "",
            // Static upstream metadata for this pinned conversion (the
            // AvalonDock TestApp at the committed revision). Dynamic truth
            // (package, digest, bridge) comes from the producer bytes above.
            new AppProvenance("mixed", "original",
                "https://github.com/Dirkster99/AvalonDock/blob/10d1cc6fb3ef45284b31bf2372a35d83f3b3ca02/source/TestApp/TestApp.csproj",
                "https://github.com/Dirkster99/AvalonDock",
                "10d1cc6fb3ef45284b31bf2372a35d83f3b3ca02",
                "source/TestApp/TestApp.csproj", "MS-PL",
                "Serving + behavior unverified: staged bytes only. WPF-routed refs moved into the engine namespace; method-body call compatibility proven only by browser boot (verifier lane).",
                true),
            new AppEntry("converted-app", entryAssembly, JointId,
                "Joint converted app boots via package loader once served; serving + behavior unverified.",
                new AppBridge(provider.GetString()!, contract.GetString()!)),
            AppCapability.Mixed, mapVersion, package,
            AppAvailability.Staged, "converted",
            $"Staged locally at '{evidencePath}' (sha {manifestSha[..12]}…); serving + behavior unverified.",
            [$"local:{evidencePath}#{manifestSha}",
             "producer:inputs/mixed/testapp-package/PROVENANCE.json",
             "producer:inputs/mixed/testapp-package/testapp-joint.receipt.json"],
            []);
        CatalogValidator.ValidateDescriptor(app);
        return app;
    }

    /// <summary>Merge a prior registry's digest-verified staging into a fresh
    /// source-built registry. Fresh metadata wins; availability is recomputed
    /// by refresh semantics over carried receipts (bytes re-verified on disk,
    /// never restored as stale flags). Prior-only ids are dropped, except
    /// the permanent joint record, which is carried when its bytes still
    /// verify (producer rebuild is preferred and passed in via fresh).</summary>
    internal static List<AppDescriptor> ReconcileWithPrior(
        List<AppDescriptor> fresh,
        IReadOnlyList<AppDescriptor> prior,
        Func<string, bool> pathExists)
    {
        var priorById = prior.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        var carried = LocalInputMapper.CarryForwardReceipts(prior, new Dictionary<string, LocalInputMapper.StagedReceipt>(), pathExists);
        var mutable = new Dictionary<string, LocalInputMapper.StagedReceipt>(carried, StringComparer.OrdinalIgnoreCase);
        foreach (var app in fresh)
        {
            if (app.Availability is not (AppAvailability.Available or AppAvailability.Staged)) continue;
            foreach (var e in app.Evidence)
            {
                if (LocalInputMapper.TryParseLocalEvidence(e, out var path, out var sha) &&
                    pathExists(path) && ReceiptVerifier.DigestMatches(path, sha))
                    mutable[app.Id] = new LocalInputMapper.StagedReceipt(app.Id, path, sha, "");
            }
        }
        var freshIds = new HashSet<string>(fresh.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
        var result = new List<AppDescriptor>(fresh.Count + 1);
        foreach (var app in fresh)
        {
            var merged = app;
            if (priorById.TryGetValue(app.Id, out var old))
            {
                var evidence = app.Evidence.Concat(old.Evidence.Where(e => !e.StartsWith("local:", StringComparison.Ordinal)))
                    .Distinct(StringComparer.Ordinal).OrderBy(e => e, StringComparer.Ordinal).ToArray();
                merged = app with { Evidence = evidence };
            }
            result.Add(LocalInputMapper.Refresh(merged, mutable));
        }
        foreach (var (id, _) in carried)
        {
            if (freshIds.Contains(id)) continue;
            if (!string.Equals(id, JointId, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(LocalInputMapper.Refresh(priorById[id], mutable));
        }
        foreach (var app in result)
            CatalogValidator.ValidateDescriptor(app);
        return result.OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
    }
}
