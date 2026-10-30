using DesktopWasm.AppModel;

namespace DesktopWasm.Catalog;

/// <summary>One validated registry: id + alias lookup, single menu projection.
/// Shell/API consume this; no hand-maintained second list.</summary>
public sealed class AppRegistry
{
    private readonly Dictionary<string, AppDescriptor> _byId;
    private readonly Dictionary<string, string> _aliasToId;

    public AppRegistry(IEnumerable<AppDescriptor> apps)
    {
        var list = apps.ToList();
        _byId = new Dictionary<string, AppDescriptor>(StringComparer.OrdinalIgnoreCase);
        _aliasToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        foreach (var app in list)
        {
            if (!CatalogValidator.IsValidId(app.Id))
                problems.Add($"bad id '{app.Id}'");
            else if (_byId.ContainsKey(app.Id))
                problems.Add($"duplicate id '{app.Id}'");
            else
                _byId[app.Id] = app;
            foreach (var alias in app.Aliases)
            {
                string key;
                try { key = CatalogValidator.NormalizeAlias(alias); }
                catch (InvalidDataException) { problems.Add($"bad alias '{alias}' on '{app.Id}'"); continue; }
                if (_byId.ContainsKey(key))
                    problems.Add($"alias '{alias}' on '{app.Id}' collides with an id");
                else if (_aliasToId.TryGetValue(key, out var owner) && !owner.Equals(app.Id, StringComparison.OrdinalIgnoreCase))
                    problems.Add($"alias '{alias}' claimed by '{owner}' and '{app.Id}'");
                else
                    _aliasToId.TryAdd(key, app.Id);
            }
        }
        if (problems.Count > 0)
            throw new InvalidDataException("Invalid registry: " + string.Join("; ", problems.OrderBy(p => p, StringComparer.Ordinal)));
    }

    public int Count => _byId.Count;

    public IReadOnlyList<AppDescriptor> Apps =>
        _byId.Values.OrderBy(a => a.Id, StringComparer.Ordinal).ToList();

    public bool TryResolve(string idOrAlias, out AppDescriptor? app)
    {
        app = null;
        if (string.IsNullOrWhiteSpace(idOrAlias)) return false;
        var key = idOrAlias;
        try { key = CatalogValidator.NormalizeAlias(idOrAlias.Trim()); }
        catch (InvalidDataException) { return false; }
        if (_byId.TryGetValue(key, out app)) return true;
        return _aliasToId.TryGetValue(key, out var id) && _byId.TryGetValue(id, out app);
    }

    public AppDescriptor Resolve(string idOrAlias) =>
        TryResolve(idOrAlias, out var app) && app is not null
            ? app
            : throw new KeyNotFoundException($"Unknown app '{idOrAlias}'.");

    /// <summary>The single menu shape the shell renders. Sorted by id.</summary>
    public IReadOnlyList<CatalogMenuEntry> Menu() =>
        Apps.Select(a => new CatalogMenuEntry(
            a.Id, a.Label, a.Description, a.Category, a.Icon,
            a.Capabilities, a.Availability, a.AvailabilityNote,
            IsLaunchable: a.Availability == AppAvailability.Available)).ToList();
}

public sealed record CatalogMenuEntry(
    string Id,
    string Label,
    string Description,
    string Category,
    string Icon,
    AppCapability Capabilities,
    AppAvailability Availability,
    string AvailabilityNote,
    bool IsLaunchable);
