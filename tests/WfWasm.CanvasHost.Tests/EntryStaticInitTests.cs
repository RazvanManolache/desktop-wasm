// CREDIT1300-RT2 (dw-runtime): BCL user-settings save derives its path per
// call from XDG_DATA_HOME/XDG_CONFIG_HOME/HOME; the served browser env
// leaves them unset, so ClientConfigurationHost.OpenStreamForWrite gets a
// null stream and throws before any host/file touch (minesweeper "Failed
// to save settings", G5/LOCALRUN + w29 live repro). EntryStaticInit seeds
// rooted MEMFS homes when absent, before the entry class ctor (and hence
// before ClientConfigPaths caches per process). These facts pin that
// contract against an isolated dictionary (no process-env mutation, so
// parallel tests can never observe it): fill-missing and
// preserve-existing. The BCL effect itself (rooted LocalConfigFilename,
// MEMFS roundtrip) rides the next regen; the live w29 failure is the
// failing-first proof, not a faked BCL.
public sealed class EntryStaticInitTests
{
    private static (System.Func<string, string?> get, System.Action<string, string?> set, System.Collections.Generic.Dictionary<string, string?> store) IsolatedEnv()
    {
        var store = new System.Collections.Generic.Dictionary<string, string?>(System.StringComparer.Ordinal);
        return (n => store.TryGetValue(n, out var v) ? v : null,
            (n, v) => store[n] = v, store);
    }

    [Fact]
    public void SeedWasmUserConfigHome_FillsMissingVars_WithRootedMemfsPaths()
    {
        var (get, set, store) = IsolatedEnv();
        WfWasm.CanvasHost.EntryStaticInit.SeedWasmUserConfigHome(get, set);
        Assert.Equal("/home/web_user/.local/share", store["XDG_DATA_HOME"]);
        Assert.Equal("/home/web_user/.config", store["XDG_CONFIG_HOME"]);
        Assert.Equal("/home/web_user", store["HOME"]);
    }

    [Fact]
    public void SeedWasmUserConfigHome_PreservesExistingVars()
    {
        var (get, set, store) = IsolatedEnv();
        store["XDG_DATA_HOME"] = "/custom/data";
        store["XDG_CONFIG_HOME"] = "/custom/config";
        store["HOME"] = "/custom/home";
        WfWasm.CanvasHost.EntryStaticInit.SeedWasmUserConfigHome(get, set);
        Assert.Equal("/custom/data", store["XDG_DATA_HOME"]);
        Assert.Equal("/custom/config", store["XDG_CONFIG_HOME"]);
        Assert.Equal("/custom/home", store["HOME"]);
    }
}
