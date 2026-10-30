using DesktopWasm.AppModel;

namespace DesktopWasm.Catalog;

/// <summary>All catalog validation in one place, scoped by field:
/// ids are strict tokens, route aliases are structural legacy routes,
/// package files are bare artifact names. Traversal is rejected in every
/// field, including percent-encoded forms in routes.</summary>
public static class CatalogValidator
{
    public static bool IsValidId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 80) return false;
        if (!char.IsAsciiLetterOrDigit(id[0])) return false;
        return id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }

    /// <summary>Legacy deep-link routes only: /demo/&lt;slug&gt; (WPF menu)
    /// or /app/&lt;slug&gt; (WinForms scenes). Anything else is not a route.
    /// Matching is case-insensitive; storage is lowercase (see NormalizeRoute).</summary>
    public static bool IsRouteAlias(string alias)
    {
        if (string.IsNullOrEmpty(alias) || alias.Length > 90) return false;
        if (alias.Any(char.IsControl)) return false;
        var lower = alias.ToLowerInvariant();
        if (!(lower.StartsWith("/demo/", StringComparison.Ordinal) ||
              lower.StartsWith("/app/", StringComparison.Ordinal)))
            return false;
        var slug = lower[(lower.IndexOf('/', 1) + 1)..];
        if (slug.Contains('/') || !IsValidId(slug)) return false;
        // Encoded traversal must never normalize into a route.
        if (alias.Contains('%') || alias.Contains('\\') || alias.Contains(':')) return false;
        if (slug.StartsWith('.') || slug.Contains("..", StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>Deterministic route form: lowercase, exactly one prefix.
    /// Throws on anything that is not a legacy route (no silent fix-ups).</summary>
    public static string NormalizeRoute(string alias)
    {
        if (!IsRouteAlias(alias))
            throw new InvalidDataException($"Not a legacy route alias: '{alias}'.");
        return alias.ToLowerInvariant();
    }

    public static bool IsValidAlias(string alias) => IsValidId(alias) || IsRouteAlias(alias);

    public static string NormalizeAlias(string alias) =>
        IsRouteAlias(alias) ? NormalizeRoute(alias) : IsValidId(alias) ? alias.ToLowerInvariant() : throw new InvalidDataException($"Bad alias '{alias}'.");

    public static bool IsPathSafe(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) return false;
        if (name is "." or "..") return false;
        if (name.IndexOfAny(['/', '\\', ':', '\0']) >= 0) return false;
        if (name.Any(char.IsControl)) return false;
        if (name.Contains("..", StringComparison.Ordinal)) return false;
        if (name.Contains('%')) return false;
        return true;
    }

    private static bool IsHex64(string? value) =>
        value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);

    private static readonly HashSet<string> Artifacts =
        new(StringComparer.Ordinal) { "wpf-bin", "wf-asm", "mixed" };

    private static bool IsArtifactFile(string file, string artifact) =>
        artifact switch
        {
            "wpf-bin" => file.EndsWith(".bin", StringComparison.Ordinal),
            "wf-asm" => file.EndsWith(".dll", StringComparison.Ordinal) ||
                        file.EndsWith(".exe", StringComparison.Ordinal),
            "mixed" => file.EndsWith(".bin", StringComparison.Ordinal) ||
                       file.EndsWith(".dll", StringComparison.Ordinal) ||
                       file.EndsWith(".exe", StringComparison.Ordinal),
            _ => false,
        };

    /// <summary>Package rules preserved from DesktopWasm.Core.ApplicationPackage
    /// (schema/slug/map match, 64-hex shas, entry in closure when converted,
    /// gaps exactly when not converted), extended through explicit adapters:
    /// wpf-bin artifacts are bare *.bin names, wf-asm artifacts bare
    /// *.dll/*.exe names. A valid imported app is never rejected merely
    /// because its legacy endpoint format differs.</summary>
    public static void ValidatePackage(string id, string mapVersion, string entryAssembly, PackageIdentity package)
    {
        if (package.SchemaVersion != 1 || !string.Equals(package.MapVersion, mapVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Package identity/version does not match '{id}'.");
        if (package.Status is not ("converted" or "conversion-failed" or "build-failed"))
            throw new InvalidDataException($"Invalid preparation result for '{id}'.");
        if (package.Assemblies is null || package.Gaps is null)
            throw new InvalidDataException($"Package assembly/gap lists are missing for '{id}'.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in package.Assemblies)
        {
            if (assembly is null || string.IsNullOrWhiteSpace(assembly.Name) || !IsPathSafe(assembly.File) ||
                !Artifacts.Contains(assembly.Artifact) || !IsArtifactFile(assembly.File, assembly.Artifact) ||
                !IsHex64(assembly.Sha256) || assembly.References is null ||
                !names.Add(assembly.Name) ||
                (assembly.InputSha256 is not null && !IsHex64(assembly.InputSha256)))
                throw new InvalidDataException($"Invalid or duplicate packaged assembly for '{id}'.");
        }
        if (package.Status == "converted" && (package.Gaps.Length != 0 || !names.Contains(entryAssembly)))
            throw new InvalidDataException($"Successful package for '{id}' has gaps or no entry assembly.");
        if (package.Status != "converted" && package.Gaps.Length == 0)
            throw new InvalidDataException($"Failed preparation for '{id}' must include its concrete cause.");
    }

    public static void ValidateDescriptor(AppDescriptor app)
    {
        if (!IsValidId(app.Id))
            throw new InvalidDataException($"Bad id '{app.Id}'.");
        foreach (var alias in app.Aliases)
            if (!IsValidAlias(alias))
                throw new InvalidDataException($"Bad alias '{alias}' on '{app.Id}'.");
        if (string.IsNullOrWhiteSpace(app.Label) || string.IsNullOrWhiteSpace(app.Category))
            throw new InvalidDataException($"Missing display metadata for '{app.Id}'.");
        if (app.Capabilities == AppCapability.None || (app.Capabilities & ~AppCapability.Mixed) != 0)
            throw new InvalidDataException($"Unknown capability flags for '{app.Id}'.");
        if (app.Capabilities == AppCapability.Mixed && app.Availability == AppAvailability.Available &&
            (app.Entry.Bridge is null || string.IsNullOrWhiteSpace(app.Entry.Bridge.Provider) ||
             string.IsNullOrWhiteSpace(app.Entry.Bridge.Contract)))
            throw new InvalidDataException($"Mixed entry '{app.Id}' needs a typed bridge identity (provider + contract); prose is not readiness.");
        if (app.Package is not null)
            ValidatePackage(app.Id, app.TransformVersion, app.Entry.EntryAssembly, app.Package);
        if (app.Availability == AppAvailability.Available && app.Gaps.Length != 0)
            throw new InvalidDataException($"Available entry '{app.Id}' must not carry gaps.");
        if ((app.Availability is AppAvailability.MissingInput or AppAvailability.Unavailable) && string.IsNullOrWhiteSpace(app.AvailabilityNote))
            throw new InvalidDataException($"Blocked entry '{app.Id}' must name its concrete gap.");
    }

    /// <summary>Deterministic duplicate/alias conflict map over raw descriptors
    /// (runs before the registry throws, so the report names every clash).
    /// Aliases compare in normalized form, so '/DEMO/X' and '/demo/x' collide.</summary>
    public static IReadOnlyList<string> FindConflicts(IEnumerable<AppDescriptor> apps)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var app in apps.OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            foreach (var key in app.Aliases.Select(NormalizeAlias).Prepend(app.Id.ToLowerInvariant()))
            {
                if (seen.TryGetValue(key, out var owner) && !owner.Equals(app.Id, StringComparison.OrdinalIgnoreCase))
                    conflicts.Add($"'{key}' claimed by '{owner}' and '{app.Id}'");
                else
                    seen.TryAdd(key, app.Id);
            }
        }
        return conflicts.ToList();
    }
}
