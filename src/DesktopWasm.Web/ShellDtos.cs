namespace DesktopWasm.Web;

/// <summary>Client-side view of /api/catalog records (contract vocabulary
/// from CATALOG-CONTRACT.md, camelCase from the shell API).</summary>
public sealed class CatalogEntry
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Icon { get; set; } = "";
    public string[] Frameworks { get; set; } = [];
    public bool Mixed { get; set; }
    public string Kind { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Available { get; set; }
    public string AvailabilityNote { get; set; } = "";
    public string[] Gaps { get; set; } = [];
    public string[] Evidence { get; set; } = [];
    public ProvenanceInfo? Provenance { get; set; }
    public LaunchInfo Launch { get; set; } = new();

    public bool Supports(string framework) =>
        Frameworks.Any(f => string.Equals(f, framework, StringComparison.OrdinalIgnoreCase));
}

public sealed class ProvenanceInfo
{
    public string Family { get; set; } = "";
    public string Origin { get; set; } = "";
    public string? SourceUrl { get; set; }
    public string? Repository { get; set; }
    public string? Commit { get; set; }
    public string? Project { get; set; }
    public string? License { get; set; }
    public string? Boundary { get; set; }
    public bool HasRecipe { get; set; }
}

public sealed class LaunchInfo
{
    public string Route { get; set; } = "";
    public string[] LegacyRoutes { get; set; } = [];
}

public sealed class SessionInfo
{
    public string AppId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string LaunchUrl { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}
