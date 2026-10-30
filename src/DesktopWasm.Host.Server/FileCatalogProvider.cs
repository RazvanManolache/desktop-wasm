using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Host.Server;

/// <summary>Thrown when catalog/apps.json has duplicate ids or alias collisions.</summary>
public sealed class CatalogConflictException(string message, IReadOnlyDictionary<string, string[]> conflicts)
    : Exception(message)
{
    public IReadOnlyDictionary<string, string[]> Conflicts { get; } = conflicts;
}

/// <summary>
/// Read-model provider over the canonical catalog/apps.json registry.
/// The future DesktopWasm.Catalog project implements <see cref="ICatalogProvider"/>;
/// this file-based provider is the shell's consumption seam so the API/UI read
/// one registry and never a hand-maintained list.
/// </summary>
public interface ICatalogProvider
{
    string? CatalogPath { get; }
    bool Loaded { get; }
    string? LoadError { get; }
    string? CatalogSha256 { get; }
    IReadOnlyList<ShellAppRecord> All { get; }
    ShellAppRecord? Find(string idOrAlias);
    IReadOnlyList<ShellAppRecord> Filter(string? framework, string? query, string? status);
}

public sealed class FileCatalogProvider : ICatalogProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private List<ShellAppRecord> _apps = [];
    private string? _catalogSha256;

    public FileCatalogProvider(string? catalogPath)
    {
        CatalogPath = catalogPath;
        if (catalogPath is null)
        {
            LoadError = "catalog-unavailable: no catalog/apps.json path resolved (MERGE-CATALOG registry not published yet)";
            return;
        }
        if (!File.Exists(catalogPath))
        {
            LoadError = $"catalog-unavailable: registry file not found at '{catalogPath}' (MERGE-CATALOG registry not published yet)";
            return;
        }
        try
        {
            byte[] bytes = File.ReadAllBytes(catalogPath);
            _catalogSha256 = Convert.ToHexString(SHA256.HashData(bytes));
            var records = JsonSerializer.Deserialize<List<ShellAppRecord>>(Encoding.UTF8.GetString(bytes), Json)
                ?? throw new InvalidDataException("catalog registry deserialized to null");
            Validate(records);
            _apps = records.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
            Loaded = true;
        }
        catch (CatalogConflictException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoadError = $"catalog-invalid: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
        }
    }

    public string? CatalogPath { get; }
    public bool Loaded { get; }
    public string? LoadError { get; }
    public string? CatalogSha256 => _catalogSha256;
    public IReadOnlyList<ShellAppRecord> All => _apps;

    public ShellAppRecord? Find(string idOrAlias)
    {
        string key = CatalogVocabulary.Normalize(idOrAlias);
        return _apps.FirstOrDefault(a =>
            string.Equals(a.Id, key, StringComparison.Ordinal) ||
            a.Aliases.Any(al => string.Equals(al, key, StringComparison.OrdinalIgnoreCase)));
    }

    public IReadOnlyList<ShellAppRecord> Filter(string? framework, string? query, string? status)
    {
        IEnumerable<ShellAppRecord> result = _apps;
        if (!string.IsNullOrWhiteSpace(framework))
        {
            string f = framework.Trim().ToLowerInvariant();
            result = f == "mixed"
                ? result.Where(a => a.IsMixed)
                : result.Where(a => a.Supports(f));
        }
        if (!string.IsNullOrWhiteSpace(status))
            result = result.Where(a => string.Equals(a.Status, status.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query))
        {
            string q = query.Trim();
            result = result.Where(a =>
                a.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        return result.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    internal static void Validate(IReadOnlyList<ShellAppRecord> records)
    {
        var conflicts = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var byId = new Dictionary<string, ShellAppRecord>(StringComparer.Ordinal);
        var aliasOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            if (!CatalogVocabulary.IsValidId(record.Id))
                throw new InvalidDataException($"invalid app id '{record.Id}': must match [a-z0-9]+(-[a-z0-9]+)*, max 64 chars");
            if (byId.ContainsKey(record.Id))
            {
                conflicts[$"duplicate-id:{record.Id}"] = [byId[record.Id].Label, record.Label];
                continue;
            }
            byId[record.Id] = record;
            if (record.Frameworks is null || record.Frameworks.Length == 0 ||
                record.Frameworks.Any(f => !CatalogVocabulary.Frameworks.Contains(f, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidDataException($"app '{record.Id}' has no valid frameworks: expected subset of [winforms, wpf]");
            if (!CatalogVocabulary.Kinds.Contains(record.Kind, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"app '{record.Id}' has unknown kind '{record.Kind}'");
            if (!CatalogVocabulary.Statuses.Contains(record.Status, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"app '{record.Id}' has unknown status '{record.Status}'");
            foreach (string alias in record.Aliases ?? [])
            {
                string key = CatalogVocabulary.Normalize(alias);
                if (!CatalogVocabulary.IsValidId(key))
                    throw new InvalidDataException($"app '{record.Id}' has invalid alias '{alias}'");
                if (byId.ContainsKey(key) || aliasOwners.TryGetValue(key, out _))
                {
                    string owner = byId.ContainsKey(key) ? $"id:{key}" : $"alias-of:{aliasOwners[key]}";
                    conflicts[$"alias-collision:{key}"] = [$"app:{record.Id}", owner];
                    continue;
                }
                aliasOwners[key] = record.Id;
            }
        }

        if (conflicts.Count > 0)
            throw new CatalogConflictException(
                "catalog registry has id/alias conflicts: " + string.Join("; ", conflicts.Keys.OrderBy(k => k)),
                conflicts);
    }
}
