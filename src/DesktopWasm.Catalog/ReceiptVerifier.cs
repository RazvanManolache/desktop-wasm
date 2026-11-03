using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopWasm.Catalog;

/// <summary>Explicit receipt-digest verification (RVW2100 digest-trust).
/// <para/>
/// Receipt digest contracts observed from real producer/provenance (2026-10-30,
/// never redefined by this verifier):
/// <list type="bullet">
/// <item>A receipt naming a FILE pins that file's bytes: the declared
/// <c>sha256</c> must equal the file's SHA-256 (case-insensitive).</item>
/// <item>A receipt naming a DIRECTORY that contains <c>app-manifest.json</c>
/// pins the MANIFEST digest (e.g. <c>hello-real</c> receipt
/// <c>E51A..FDC</c> equals the SHA-256 of
/// <c>inputs/wpf/catalog-staging/hello-real/app-manifest.json</c>), plus the
/// closure resources the manifest claims: every assembly file the manifest
/// lists must exist beside it with matching bytes (e.g.
/// <c>HelloWorld.&lt;sha&gt;.bin</c> hash equals the manifest assembly
/// <c>sha256</c>).</item>
/// <item>A receipt naming a DIRECTORY with no manifest (e.g. <c>paint</c>
/// <c>targets/paint.net</c> pin <c>56BC..1310</c>,
/// <c>benchmark-fiddler-classic</c> pin <c>2310..FAC8</c>) carries ASSERTED
/// import-lane provenance, not a verifiable file/tree digest: it is verified
/// by existence only, and its <c>sha256</c> must never be presented as a
/// byte-verification fact. Do not invent a tree hash and compare it against
/// these pins; that would silently redefine old receipts.</item>
/// </list>
/// <para/>
/// Non-64-hex or mismatching digests on verifiable paths are stale/invalid:
/// fresh receipts with such digests are rejected, and carried evidence whose
/// bytes changed is not carried forward (the entry demotes honestly through
/// <see cref="LocalInputMapper.Refresh"/>).</summary>
public static class ReceiptVerifier
{
    public static bool IsHex64(string? value) =>
        value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);

    public static string Sha256OfFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>Classify a staged path: a file, a manifest directory
    /// (contains <c>app-manifest.json</c>), an asserted directory (no
    /// manifest; existence-only provenance), or missing.</summary>
    public static string Classify(string localPath)
    {
        if (File.Exists(localPath)) return "file";
        if (Directory.Exists(localPath))
            return File.Exists(Path.Combine(localPath, "app-manifest.json"))
                ? "manifest-directory"
                : "asserted-directory";
        return "missing";
    }

    /// <summary>True when the current bytes at <paramref name="localPath"/>
    /// match <paramref name="sha256"/> under the contract above. Asserted
    /// directories (no manifest) return true on existence: their pin is
    /// provenance, not a verifiable digest.</summary>
    public static bool DigestMatches(string localPath, string sha256)
    {
        if (!IsHex64(sha256)) return false;
        if (File.Exists(localPath))
        {
            try { return Sha256OfFile(localPath).Equals(sha256, StringComparison.OrdinalIgnoreCase); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        if (Directory.Exists(localPath))
        {
            var manifestPath = Path.Combine(localPath, "app-manifest.json");
            if (!File.Exists(manifestPath)) return true; // asserted provenance
            try
            {
                if (!Sha256OfFile(manifestPath).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                    return false;
                return ClosureMatches(localPath, manifestPath);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        return false;
    }

    /// <summary>Every assembly file the staged manifest lists must exist
    /// beside it with bytes matching the manifest's declared
    /// <c>sha256</c>. Parse problems fail closed.</summary>
    public static bool ClosureMatches(string stagedDir, string manifestPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!doc.RootElement.TryGetProperty("assemblies", out var assemblies) ||
                assemblies.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var a in assemblies.EnumerateArray())
            {
                if (!a.TryGetProperty("file", out var f) || !a.TryGetProperty("sha256", out var s))
                    return false;
                var file = f.GetString() ?? "";
                var sha = s.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(file) || !IsHex64(sha)) return false;
                if (file.IndexOfAny(['/', '\\', ':']) >= 0 || file.Contains("..", StringComparison.Ordinal))
                    return false;
                var staged = Path.Combine(stagedDir, file);
                if (!File.Exists(staged)) return false;
                if (!Sha256OfFile(staged).Equals(sha, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
        catch (JsonException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>Throw <see cref="InvalidDataException"/> describing a fresh
    /// receipt whose verifiable bytes do not match its declared digest.
    /// Asserted directories pass on existence (checked by the caller).</summary>
    public static void ThrowIfDigestMismatch(LocalInputMapper.StagedReceipt receipt)
    {
        if (!IsHex64(receipt.Sha256))
            throw new InvalidDataException(
                $"Invalid receipt digest for '{receipt.Id}': sha256 must be 64 hex characters.");
        var kind = Classify(receipt.LocalPath);
        if (kind is "missing" or "asserted-directory") return;
        if (!DigestMatches(receipt.LocalPath, receipt.Sha256))
            throw new InvalidDataException(kind == "file"
                ? $"Staged file digest mismatch for '{receipt.Id}': '{receipt.LocalPath}' bytes do not match declared sha256 {receipt.Sha256}."
                : $"Staged manifest digest mismatch for '{receipt.Id}': '{receipt.LocalPath}' manifest/closure bytes do not match declared sha256 {receipt.Sha256}.");
    }
}
