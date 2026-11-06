namespace DesktopWasm.Core;

public sealed record PackageAssembly(string Name, string File, string Sha256, string[] References, string? InputSha256 = null)
{
    /// <summary>A URI- and MSBuild-safe disk leaf; percent escapes in physical
    /// filenames are unescaped by MSBuild's item handling during publish.</summary>
    public static string AssetFileName(string name, string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(sha256);
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected a SHA-256 digest.", nameof(sha256));
        var stem = Uri.EscapeDataString(name).Replace("~", "%7E", StringComparison.Ordinal).Replace('%', '~');
        return stem + "." + sha256.ToLowerInvariant() + ".bin";
    }
}
public sealed record PackageGap(string Assembly, string Kind, string Detail);

/// <summary>Selected application's referenced dependency closure, not a folder inventory.</summary>
public sealed record ApplicationPackage(int SchemaVersion, string Slug, string MapVersion,
    string Status, string EntryAssembly, PackageAssembly[] Assemblies, PackageGap[] Gaps,
    string? SourceCommit)
{
    public void Validate(string expectedSlug, string expectedMap)
    {
        if (SchemaVersion != 1 || Slug != expectedSlug || MapVersion != expectedMap)
            throw new InvalidDataException("Application package identity/version does not match the requested application.");
        if (Status is not ("converted" or "conversion-failed" or "build-failed"))
            throw new InvalidDataException("Invalid application preparation result.");
        if (Assemblies is null || Gaps is null) throw new InvalidDataException("Package assembly/gap lists are missing.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in Assemblies)
        {
            if (assembly is null || string.IsNullOrWhiteSpace(assembly.File) || assembly.Sha256 is null || assembly.References is null ||
                !names.Add(assembly.Name) || string.IsNullOrWhiteSpace(assembly.Name) ||
                assembly.File != Path.GetFileName(assembly.File) || assembly.File.Contains('\\') ||
                !assembly.File.EndsWith(".bin", StringComparison.Ordinal) ||
                assembly.Sha256.Length != 64 || !assembly.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid or duplicate packaged assembly.");
            if (assembly.InputSha256 is { } inputHash && (inputHash.Length != 64 || !inputHash.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Invalid original assembly hash.");
        }
        if (Status == "converted" && (Gaps.Length != 0 || !names.Contains(EntryAssembly)))
            throw new InvalidDataException("Successful package has gaps or no entry assembly.");
        if (Status != "converted" && Gaps.Length == 0)
            throw new InvalidDataException("Failed preparation must include its concrete cause.");
    }
}
