using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.AppModel;
using DesktopWasm.Catalog;

namespace DesktopWasm.Host.Server;

/// <summary>
/// Shell consumption point for the canonical registry (MERGE-SHELL-2).
/// Loads <c>catalog/apps.json</c> through the shared contract
/// (<see cref="CatalogSerializer"/> + <see cref="AppRegistry"/>, owned by the
/// MERGE-CATALOG lane); the shell keeps no registry model of its own.
/// A missing or invalid registry is an honest 503, never a fallback list.
/// </summary>
public sealed class CatalogSource
{
    public CatalogSource(string catalogPath)
    {
        CatalogPath = catalogPath;
        if (!File.Exists(catalogPath))
        {
            LoadError = $"catalog-unavailable: registry file not found at '{catalogPath}' (MERGE-CATALOG registry not published yet)";
            return;
        }
        try
        {
            byte[] bytes = File.ReadAllBytes(catalogPath);
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
            Registry = CatalogSerializer.Deserialize(System.Text.Encoding.UTF8.GetString(bytes));
            Loaded = true;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            LoadError = $"catalog-invalid: {ex.Message.Split('\n')[0]}";
        }
    }

    public string CatalogPath { get; }
    public bool Loaded { get; }
    public string? LoadError { get; }
    public string? Sha256 { get; }
    public AppRegistry? Registry { get; }

    public AppDescriptor? Find(string idOrAlias) =>
        Registry is not null && Registry.TryResolve(idOrAlias, out var app) ? app : null;

    /// <summary>Legacy deep-link resolution: bare id first, then the
    /// contract route-alias form for the legacy prefix (/app/ and /demo/
    /// only; /runtime/ has no registry route and resolves id-only).</summary>
    public AppDescriptor? FindLegacy(string prefix, string slug)
    {
        var direct = Find(slug);
        if (direct is not null || (prefix != "/app" && prefix != "/demo"))
            return direct;
        return Find(prefix + "/" + slug.Trim().ToLowerInvariant());
    }

    public IReadOnlyList<AppDescriptor> Filter(string? framework, string? query, string? status)
    {
        if (Registry is null)
            return [];
        IEnumerable<AppDescriptor> result = Registry.Apps;
        if (!string.IsNullOrWhiteSpace(framework))
        {
            string f = framework.Trim().ToLowerInvariant();
            result = f switch
            {
                "winforms" => result.Where(a => a.Capabilities.HasFlag(AppCapability.WinForms)),
                "wpf" => result.Where(a => a.Capabilities.HasFlag(AppCapability.Wpf)),
                "mixed" => result.Where(a => a.Capabilities == AppCapability.Mixed),
                _ => result,
            };
        }
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<AppAvailability>(status.Trim(), ignoreCase: true, out var availability))
            result = result.Where(a => a.Availability == availability);
        if (!string.IsNullOrWhiteSpace(query))
        {
            string q = query.Trim();
            result = result.Where(a =>
                a.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        return result.OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
    }

    public static string[] FrameworksOf(AppDescriptor app)
    {
        var list = new List<string>(2);
        if (app.Capabilities.HasFlag(AppCapability.WinForms))
            list.Add("winforms");
        if (app.Capabilities.HasFlag(AppCapability.Wpf))
            list.Add("wpf");
        return list.ToArray();
    }

    public static string StatusOf(AppDescriptor app) =>
        JsonNamingPolicy.CamelCase.ConvertName(app.Availability.ToString());
}
