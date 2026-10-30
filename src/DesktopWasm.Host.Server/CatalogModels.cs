using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DesktopWasm.Host.Server;

/// <summary>
/// Shell-side view of one catalog entry (MERGE-SHELL lane).
/// This is intentionally a thin read-model over the canonical
/// catalog/apps.json registry owned by the MERGE-CATALOG lane:
/// it carries only what listing, launching and availability need.
/// Field vocabulary (id/aliases/capabilities/provenance/availability/gaps)
/// follows docs/architecture/UNIFICATION.md; the exact shared contract is
/// negotiated via Ops once docs/architecture/CATALOG-CONTRACT.md lands.
/// The shell never maintains its own app list.
/// </summary>
public sealed record ShellAppRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("aliases")] string[] Aliases,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("frameworks")] string[] Frameworks,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("gaps")] string[] Gaps,
    [property: JsonPropertyName("provenance")] ShellProvenance? Provenance,
    [property: JsonPropertyName("launch")] ShellLaunch Launch)
{
    public bool Supports(string framework) =>
        Frameworks.Contains(framework, StringComparer.OrdinalIgnoreCase);

    public bool IsMixed =>
        Supports(CatalogVocabulary.WinForms) && Supports(CatalogVocabulary.Wpf);
}

public sealed record ShellProvenance(
    [property: JsonPropertyName("sourceUrl")] string? SourceUrl,
    [property: JsonPropertyName("repository")] string? Repository,
    [property: JsonPropertyName("commit")] string? Commit,
    [property: JsonPropertyName("project")] string? Project,
    [property: JsonPropertyName("license")] string? License,
    [property: JsonPropertyName("boundary")] string? Boundary);

public sealed record ShellLaunch(
    [property: JsonPropertyName("route")] string Route,
    [property: JsonPropertyName("legacyRoutes")] string[] LegacyRoutes);

/// <summary>Accepted vocabulary for the shell view. Mirrors UNIFICATION.md.</summary>
public static class CatalogVocabulary
{
    public const string WinForms = "winforms";
    public const string Wpf = "wpf";

    public static readonly string[] Frameworks = [WinForms, Wpf];
    public static readonly string[] Kinds = ["demo", "original"];
    public static readonly string[] Statuses = ["ready", "not-converted", "unavailable"];

    private static readonly Regex IdPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    public static bool IsValidId(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 64 && IdPattern.IsMatch(id);

    public static string Normalize(string idOrAlias) => (idOrAlias ?? "").Trim().ToLowerInvariant();
}
