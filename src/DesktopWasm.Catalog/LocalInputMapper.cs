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

    public static AppDescriptor Refresh(AppDescriptor app, IReadOnlyDictionary<string, StagedReceipt> receipts)
    {
        if (app.Availability == AppAvailability.NotConverted)
            return app; // No package exists upstream; staging cannot change that.
        var needs = RequiredResource(app);
        if (needs is null) return app;
        if (receipts.TryGetValue(app.Id, out var receipt) &&
            !string.IsNullOrWhiteSpace(receipt.LocalPath) && !string.IsNullOrWhiteSpace(receipt.Sha256))
        {
            return app with
            {
                Availability = AppAvailability.Available,
                AvailabilityNote = $"Staged locally at '{receipt.LocalPath}' (sha {receipt.Sha256[..Math.Min(12, receipt.Sha256.Length)]}…); runtime unverified.",
                Evidence = app.Evidence.Append($"local:{receipt.LocalPath}#{receipt.Sha256}").OrderBy(e => e, StringComparer.Ordinal).ToArray(),
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
