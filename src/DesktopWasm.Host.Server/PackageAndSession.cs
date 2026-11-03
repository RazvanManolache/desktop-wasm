using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DesktopWasm.Catalog;

namespace DesktopWasm.Host.Server;

/// <summary>Shell-side id normalization. Validation itself stays in the shared
/// <see cref="CatalogValidator"/> (catalog lane owns the token rules).</summary>
internal static class ShellIds
{
    public static string Normalize(string idOrAlias) => (idOrAlias ?? "").Trim().ToLowerInvariant();

    public static string RequireValidId(string appId)
    {
        string id = Normalize(appId);
        if (!CatalogValidator.IsValidId(id))
            throw new FileNotFoundException($"unknown app '{appId}'");
        return id;
    }
}

public sealed class PackageUnavailableException(string message) : Exception(message);
public sealed class ContentMismatchException(string message) : Exception(message);

/// <summary>
/// Hashed on-demand package/content delivery for one catalog app.
/// Mirrors the existing behavior of both imported hosts (content-hashed URLs
/// with ?v= pinning, 409 on stale versions, traversal-safe names) without
/// referencing importer-owned projects: the shell owns this seam and the
/// packaging lane plugs real transform output behind it after source-ready.
/// </summary>
public sealed class PackageService(string packagesRoot)
{
    private static readonly Regex SafeName = new("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);

    public string PackagesRoot { get; } = packagesRoot;

    public static void GuardContentName(string appId, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || !SafeName.IsMatch(name) ||
            name.Contains("..", StringComparison.Ordinal) || name.EndsWith('.'))
            throw new FileNotFoundException($"unknown content '{name}' for app '{appId}'");
    }

    public string AppDirectory(string appId)
    {
        string id = ShellIds.RequireValidId(appId);
        return Path.Combine(PackagesRoot, id);
    }

    public IReadOnlyList<PackageFile> Manifest(string appId)
    {
        string dir = AppDirectory(appId);
        if (!Directory.Exists(dir))
            throw new PackageUnavailableException(
                $"package-unavailable: no converted package staged for '{appId}' under '{dir}' (packaging lane has not published it yet)");
        return Directory.GetFiles(dir)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(path =>
            {
                string file = Path.GetFileName(path)!;
                string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                return new PackageFile(file, hash, new FileInfo(path).Length,
                    $"/api/packages/{Uri.EscapeDataString(ShellIds.Normalize(appId))}/content?name={Uri.EscapeDataString(file)}&v={hash}");
            })
            .ToList();
    }

    public (byte[] Bytes, string Mime, string Sha256) Content(string appId, string name, string? expectedVersion)
    {
        GuardContentName(appId, name);
        string dir = AppDirectory(appId);
        string path = Path.Combine(dir, name);
        if (!Directory.Exists(dir))
            throw new PackageUnavailableException($"package-unavailable: no converted package staged for '{appId}'");
        if (!File.Exists(path))
            throw new FileNotFoundException($"unknown content '{name}' for app '{appId}'");
        byte[] bytes = File.ReadAllBytes(path);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (expectedVersion is not null && !string.Equals(expectedVersion, hash, StringComparison.OrdinalIgnoreCase))
            throw new ContentMismatchException($"Asset changed for '{appId}/{name}'; reload the app manifest.");
        string mime = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".json" => "application/json",
            ".wasm" => "application/wasm",
            _ => "application/octet-stream",
        };
        return (bytes, mime, hash);
    }
}

public sealed record PackageFile(string Name, string Sha256, long Size, string Url);

/// <summary>App selection + session identity shared by both frameworks.</summary>
public sealed class SessionService
{
    private static readonly Regex SessionPattern = new("^[A-Za-z0-9_-]{1,128}$", RegexOptions.Compiled);

    public ShellSession Create(string appId, string? requestedSession = null)
    {
        string id = ShellIds.Normalize(appId);
        if (!CatalogValidator.IsValidId(id))
            throw new ArgumentException($"unknown app '{appId}'", nameof(appId));
        string session = string.IsNullOrWhiteSpace(requestedSession)
            ? Guid.NewGuid().ToString("N")
            : GuardSession(requestedSession);
        return new ShellSession(id, session, $"/launch/{id}?session={session}", DateTime.UtcNow);
    }

    public static string GuardSession(string session)
    {
        string s = session.Trim();
        if (!SessionPattern.IsMatch(s))
            throw new ArgumentException($"invalid session id (expected 1-128 chars of [A-Za-z0-9_-]): '{session}'");
        return s;
    }
}

public sealed record ShellSession(string AppId, string SessionId, string LaunchUrl, DateTime CreatedUtc);
