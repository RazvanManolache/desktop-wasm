using DesktopWasm.AppModel;

namespace DesktopWasm.Catalog;

/// <summary>Availability from mapped LOCAL inputs, never frozen originals.
/// Snapshots under catalog/sources are migration provenance only; after the
/// import lane stages content, shell/import feed this mapper a receipts map
/// (id -> staged evidence) and it recomputes availability. No file IO here:
/// callers resolve the local paths, this decides.
/// <para/>
/// Native demos need no upstream package when compiled locally: their only
/// required resource is the staged host, so their gap names the host, not a
/// package. Staged bytes alone never mean verified runtime (dw-verify owns
/// that; this mapper caps at <see cref="AppAvailability.Available"/>).
/// </summary>
public static class LocalInputMapper
{
    /// <summary>One staged input receipt, produced by the import lane.</summary>
    public sealed record StagedReceipt(string Id, string LocalPath, string Sha256, string StagedUtc);

    /// <summary>Parse a <c>local:{path}#{sha}</c> evidence entry back into
    /// its path and sha. Anything else is not staging provenance.</summary>
    public static bool TryParseLocalEvidence(string evidence, out string path, out string sha)
    {
        path = "";
        sha = "";
        const string prefix = "local:";
        if (!evidence.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var rest = evidence[prefix.Length..];
        var hash = rest.LastIndexOf('#');
        if (hash <= 0 || hash == rest.Length - 1) return false;
        path = rest[..hash];
        sha = rest[(hash + 1)..];
        return !string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(sha);
    }

    /// <summary>Cumulative staging: rebuild receipts for entries that are
    /// currently Available or Staged from their own <c>local:</c> evidence,
    /// but ONLY while the evidenced bytes still match the evidenced digest
    /// (RVW2100 digest-trust). Files and manifest directories (manifest
    /// digest plus manifest-claimed closure) are re-verified, never carried
    /// on mere path existence; manifest-less directories keep the
    /// asserted-provenance contract (existence only, see
    /// <see cref="ReceiptVerifier"/>). A partial new batch therefore never
    /// silently demotes still-present earlier inputs, while a removed
    /// input — or one whose bytes changed under a stale pin — carries
    /// nothing forward and demotes honestly through <see cref="Refresh"/>.
    /// Pure: the caller supplies path existence; byte hashing reads the
    /// evidenced paths.</summary>
    public static IReadOnlyDictionary<string, StagedReceipt> CarryForwardReceipts(
        IEnumerable<AppDescriptor> current,
        IReadOnlyDictionary<string, StagedReceipt> fresh,
        Func<string, bool> pathExists)
    {
        var carried = new Dictionary<string, StagedReceipt>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in current)
        {
            if (app.Availability != AppAvailability.Available && app.Availability != AppAvailability.Staged) continue;
            if (fresh.ContainsKey(app.Id)) continue;
            foreach (var e in app.Evidence)
            {
                if (TryParseLocalEvidence(e, out var path, out var sha) && pathExists(path) &&
                    ReceiptVerifier.DigestMatches(path, sha))
                    carried[app.Id] = new StagedReceipt(app.Id, path, sha, "");
            }
        }
        return carried;
    }

    public static AppDescriptor Refresh(AppDescriptor app, IReadOnlyDictionary<string, StagedReceipt> receipts)
    {
        if (app.Availability == AppAvailability.NotConverted)
            return app; // No package exists upstream; staging cannot change that.
        if (app.Availability == AppAvailability.Staged)
        {
            // Bytes-verified staging stays staging: a verified receipt keeps
            // it Staged (refreshing the local: evidence), a missing or
            // changed input demotes honestly to MissingInput. Refresh never
            // promotes Staged to Available — serving and behavior stay
            // unverified until another lane proves them.
            if (receipts.TryGetValue(app.Id, out var staged) &&
                !string.IsNullOrWhiteSpace(staged.LocalPath) && !string.IsNullOrWhiteSpace(staged.Sha256))
            {
                var kept = app.Evidence.Where(e => !e.StartsWith("local:", StringComparison.Ordinal))
                    .Append($"local:{staged.LocalPath}#{staged.Sha256}")
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(e => e, StringComparer.Ordinal).ToArray();
                return app with
                {
                    AvailabilityNote = $"Staged locally at '{staged.LocalPath}' (sha {staged.Sha256[..Math.Min(12, staged.Sha256.Length)]}…); serving + behavior unverified.",
                    Evidence = kept,
                    Gaps = [],
                };
            }
            var missing = RequiredResource(app) ?? $"staged inputs for '{app.Id}'";
            return app with
            {
                Availability = AppAvailability.MissingInput,
                AvailabilityNote = $"Missing local input: {missing}.",
                Gaps = [missing],
            };
        }
        if (app.Capabilities.HasFlag(AppCapability.Mixed) && app.Entry.Bridge is null)
            return app with
            {
                // Pilot-demotion: a receipt alone never promotes a mixed entry.
                // Availability requires the typed bridge identity; prose and
                // staged bytes are not readiness (see CatalogValidator).
                Availability = AppAvailability.Unavailable,
                AvailabilityNote = "No proven mixed session: entry carries no typed bridge identity; receipts cannot promote it.",
                Gaps = ["typed bridge identity (provider + contract) from the interop lane"],
            };
        var needs = RequiredResource(app);
        if (needs is null) return app;
        if (receipts.TryGetValue(app.Id, out var receipt) &&
            !string.IsNullOrWhiteSpace(receipt.LocalPath) && !string.IsNullOrWhiteSpace(receipt.Sha256))
        {
            var evidence = app.Evidence.Where(e => !e.StartsWith("local:", StringComparison.Ordinal))
                .Append($"local:{receipt.LocalPath}#{receipt.Sha256}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(e => e, StringComparer.Ordinal).ToArray();
            return app with
            {
                Availability = AppAvailability.Available,
                AvailabilityNote = $"Staged locally at '{receipt.LocalPath}' (sha {receipt.Sha256[..Math.Min(12, receipt.Sha256.Length)]}…); runtime unverified.",
                Evidence = evidence,
                Gaps = [],
            };
        }
        return app with
        {
            Availability = AppAvailability.MissingInput,
            AvailabilityNote = $"Missing local input: {needs}.",
            Gaps = [needs],
        };
    }

    /// <summary>Concrete required resource per entry kind. Native demos name
    /// the host build; converted apps name the package copy; scenes name the
    /// app folder plus transform inputs.</summary>
    public static string? RequiredResource(AppDescriptor app)
    {
        if (app.Availability == AppAvailability.NotConverted) return null;
        return app.Entry.Kind switch
        {
            "native-demo" when app.Provenance.Family == "wpf" =>
                "WPF BrowserHost import (engine-native demo, no upstream package needed)",
            "native-demo" =>
                $"app folder for '{app.Id}' plus WinForms transform inputs",
            "converted-app" when app.Package is not null =>
                $"package copy for '{app.Entry.ManifestSlug ?? app.Id}' hash-pinned from manifest",
            "converted-app" =>
                $"converted package for '{app.Id}' (no manifest staged)",
            "mixed-session" =>
                "proven mixed session from the interop lane",
            _ => $"staged inputs for '{app.Id}'",
        };
    }
}
