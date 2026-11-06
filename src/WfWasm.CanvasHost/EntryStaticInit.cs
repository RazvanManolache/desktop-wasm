namespace WfWasm.CanvasHost;

// AE-0135 PKHeX form-create: the harness constructs the startup form
// directly, skipping Program.Main. App static initializers that are
// order-sensitive then run LATE and fail exactly as served: PKHeX
// Program..cctor calls Application.SetUnhandledExceptionMode, which
// throws InvalidOperationException once any Control exists on the
// thread — but the half-constructed base Form already exists, so the
// whole entry poisons to TypeInitializationException and the app
// stalls (form shows, "App ready" never signals). Desktop runs entry
// static init first, so mirror that: run the entry type's class ctor
// explicitly before any form construction. Best-effort: failures are
// logged to the served console (never swallowed) and construction
// continues down today's chain, never a new failure.
internal static class EntryStaticInit
{
    // CREDIT1300-RT2: BCL user-settings save derives its path per call
    // from the process environment (XDG_DATA_HOME/XDG_CONFIG_HOME/HOME on
    // Unix/wasm). The served browser env leaves all three unset, so
    // ClientConfigPaths.LocalConfigFilename comes back null and
    // ClientConfigurationHost.OpenStreamForWrite(null) throws before any
    // host/file touch (minesweeper "Failed to save settings", G5/LOCALRUN
    // evidence). Seed rooted MEMFS homes when (and only when) absent, so
    // the BCL derivation yields a writable path; the BCL itself creates
    // the user.config directory tree on write. Runs before the entry
    // class ctor (ClientConfigPaths caches per process on first settings
    // access). Fail-closed: never breaks boot. Does not touch
    // APP_CONFIG_FILE (the working exe-config read path).
    public static void EnsureInitialized(Type? entryType)
    {
        SeedWasmUserConfigHome();
        if (entryType is null) return;
        try { System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(entryType.TypeHandle); }
        catch (Exception ex) { try { Console.WriteLine("[Host][entry-init] " + (entryType.FullName ?? "?") + ": " + ex.GetType().Name); } catch { } }
    }

    internal static void SeedWasmUserConfigHome()
    {
        SeedWasmUserConfigHome(System.Environment.GetEnvironmentVariable, System.Environment.SetEnvironmentVariable);
    }

    // Injectable core: production passes the process environment; tests
    // pass an isolated dictionary, so no test ever mutates process-wide
    // state that parallel tests could observe.
    internal static void SeedWasmUserConfigHome(Func<string, string?> get, Action<string, string?> set)
    {
        try
        {
            SeedVar(get, set, "XDG_DATA_HOME", "/home/web_user/.local/share");
            SeedVar(get, set, "XDG_CONFIG_HOME", "/home/web_user/.config");
            SeedVar(get, set, "HOME", "/home/web_user");
        }
        catch { }
    }

    private static void SeedVar(Func<string, string?> get, Action<string, string?> set, string name, string memfsPath)
    {
        try
        {
            if (string.IsNullOrEmpty(get(name)))
                set(name, memfsPath);
        }
        catch { }
    }

    public static void EnsureInitialized(System.Reflection.Assembly? assembly)
    {
        try { EnsureInitialized(assembly?.EntryPoint?.DeclaringType); }
        catch (Exception ex) { try { Console.WriteLine("[Host][entry-init] assembly: " + ex.GetType().Name); } catch { } }
    }
}
