namespace DesktopWasm.Packaging;

/// <summary>One unresolvable or conflicting joint-packaging item (fails the build visibly).</summary>
public sealed record JointGap(string Assembly, string Kind, string Detail)
{
    public const string MissingDependency = "missing-dependency";
    public const string DependencyName = "dependency-name";
    public const string VersionConflict = "version-conflict";
    public const string HashMismatch = "hash-mismatch";
    public const string TypeCollision = "type-collision";
    public const string EngineUnavailable = "engine-unavailable";
    public const string OrderViolation = "order-violation";
}
