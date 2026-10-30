using Microsoft.AspNetCore.Components;

namespace DesktopWasm.BrowserHost;

/// <summary>Selected-demo state shared by the menu, toolbar, footer, and stage (cf. winforms-wasm SceneState).</summary>
public sealed class DemoState
{
    private readonly NavigationManager _nav;
    private bool _navigated;
    private WpfDemo? _external;

    public DemoState(NavigationManager nav) => _nav = nav;

    public string CurrentSlug { get; private set; } = DemoCatalog.All[0].Slug;

    public WpfDemo Current => _external ?? DemoCatalog.Find(CurrentSlug);

    public void SyncExternal(string slug, string label, string description)
    {
        CurrentSlug = slug;
        _external = new WpfDemo(slug, label, description, "Real apps", "◇",
            () => throw new InvalidOperationException("External apps boot from their package manifest."));
    }

    public string Status { get; private set; } = "Ready";

    public event Action? Changed;

    public void Select(string slug)
    {
        var demo = DemoCatalog.Find(slug);
        if (demo.Slug == CurrentSlug && _navigated) return;
        CurrentSlug = demo.Slug;
        _navigated = true;
        Changed?.Invoke();
        _nav.NavigateTo(demo.Slug == DemoCatalog.All[0].Slug ? "/" : $"/demo/{demo.Slug}");
    }

    public void SyncFromRoute(string? slug)
    {
        _external = null;
        var demo = DemoCatalog.Find(string.IsNullOrEmpty(slug) ? DemoCatalog.All[0].Slug : slug);
        if (demo.Slug == CurrentSlug) return;
        CurrentSlug = demo.Slug;
        _navigated = true;
        Changed?.Invoke();
    }

    public void SetStatus(string status)
    {
        Status = status;
        Changed?.Invoke();
    }
}
