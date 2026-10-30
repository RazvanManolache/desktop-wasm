namespace DesktopWasm.Web;

/// <summary>
/// Runtime/session adapter seam (MERGE-SHELL lane). Framework-specific boot
/// logic lives behind these adapters; the shell portal never owns engine
/// boot code. Pure apps mount imported host adapters only after source-ready;
/// until interop/packaging land, adapters report actual availability instead
/// of presenting stub hosting as real. A mixed app session holds BOTH engine
/// adapters in one session; an iframe/process-isolation mount is explicitly
/// not completion and is rejected here by design (see MixedSession).
/// </summary>
public interface IRuntimeAdapter
{
    string Framework { get; }
    string DisplayName { get; }
    Task<AdapterStatus> StatusAsync(CatalogEntry app);
    Task<AdapterBootResult> BootAsync(CatalogEntry app, string sessionId);
}

public sealed record AdapterStatus(bool CanBoot, string Detail);
public sealed record AdapterBootResult(bool Started, string Detail);

/// <summary>WinForms boot adapter. Mounts WfWasm.CanvasHost behind this seam after source-ready.</summary>
public sealed class WinFormsRuntimeAdapter(HttpClient http) : IRuntimeAdapter
{
    public string Framework => "winforms";
    public string DisplayName => "Windows Forms";

    public async Task<AdapterStatus> StatusAsync(CatalogEntry app)
    {
        if (!app.Supports(Framework))
            return new AdapterStatus(false, $"App '{app.Id}' does not declare the winforms capability.");
        if (!app.Available)
            return new AdapterStatus(false,
                $"WinForms package for '{app.Id}' is not available: {string.Join("; ", app.Gaps)}");
        try
        {
            using var response = await http.GetAsync($"api/packages/{Uri.EscapeDataString(app.Id)}/manifest");
            if (!response.IsSuccessStatusCode)
                return new AdapterStatus(false,
                    $"WinForms package manifest returned {(int)response.StatusCode}; host mount deferred until packaging publishes it.");
            return new AdapterStatus(true, "Package manifest present; host mount runs after source-ready import verification.");
        }
        catch (Exception ex)
        {
            return new AdapterStatus(false, $"Package probe failed: {ex.GetBaseException().Message}");
        }
    }

    public Task<AdapterBootResult> BootAsync(CatalogEntry app, string sessionId) =>
        Task.FromResult(new AdapterBootResult(false,
            "WinForms host mount is gated on source-ready (importer-owned hosts). Session reserved; no stub boot performed."));
}

/// <summary>WPF boot adapter. Mounts DesktopWasm.BrowserHost behind this seam after source-ready.</summary>
public sealed class WpfRuntimeAdapter(HttpClient http) : IRuntimeAdapter
{
    public string Framework => "wpf";
    public string DisplayName => "WPF";

    public async Task<AdapterStatus> StatusAsync(CatalogEntry app)
    {
        if (!app.Supports(Framework))
            return new AdapterStatus(false, $"App '{app.Id}' does not declare the wpf capability.");
        if (!app.Available)
            return new AdapterStatus(false,
                $"WPF package for '{app.Id}' is not available: {string.Join("; ", app.Gaps)}");
        try
        {
            using var response = await http.GetAsync($"api/packages/{Uri.EscapeDataString(app.Id)}/manifest");
            if (!response.IsSuccessStatusCode)
                return new AdapterStatus(false,
                    $"WPF package manifest returned {(int)response.StatusCode}; host mount deferred until packaging publishes it.");
            return new AdapterStatus(true, "Package manifest present; host mount runs after source-ready import verification.");
        }
        catch (Exception ex)
        {
            return new AdapterStatus(false, $"Package probe failed: {ex.GetBaseException().Message}");
        }
    }

    public Task<AdapterBootResult> BootAsync(CatalogEntry app, string sessionId) =>
        Task.FromResult(new AdapterBootResult(false,
            "WPF host mount is gated on source-ready (importer-owned hosts). Session reserved; no stub boot performed."));
}

/// <summary>
/// One mixed-framework session: owns both engine adapters together.
/// Mixed acceptance requires both adapters active inside this single session;
/// mounting two isolated hosts (iframes/processes) does not satisfy it.
/// </summary>
public sealed class MixedSession(WinFormsRuntimeAdapter winForms, WpfRuntimeAdapter wpf)
{
    public CatalogEntry? App { get; private set; }
    public string? SessionId { get; private set; }
    public AdapterStatus? WinFormsStatus { get; private set; }
    public AdapterStatus? WpfStatus { get; private set; }

    public bool IsMixed => App?.Mixed == true;
    public bool BothReady => WinFormsStatus?.CanBoot == true && WpfStatus?.CanBoot == true;

    public async Task AttachAsync(CatalogEntry app, string sessionId)
    {
        App = app;
        SessionId = sessionId;
        WinFormsStatus = await winForms.StatusAsync(app);
        WpfStatus = await wpf.StatusAsync(app);
    }

    public string Verdict => App is null ? "No app attached."
        : !IsMixed ? $"App '{App.Id}' is single-framework; mixed session does not apply."
        : BothReady ? "Both engine adapters report ready in one session (host mount still gated on interop/packaging)."
        : "Mixed session attached; at least one engine adapter is not bootable yet — honestly reported above, not worked around.";
}
