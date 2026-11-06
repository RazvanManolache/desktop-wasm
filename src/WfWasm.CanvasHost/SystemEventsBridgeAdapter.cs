// CREDIT1300-RT4-1215: SystemEvents bridge adapter. The mRemoteNG closure
// carries ONE residual conversion gap by design: the type
// `Microsoft.Win32.SystemEvents` is claimed by both the real broadcast
// assembly (bundle-shipped `Microsoft.Win32.SystemEvents`, source of truth
// for display/color changes) and the WPF engine adapter (host-driven
// `RaiseUserPreferenceChanged`/`RaiseDisplaySettingsChanged`/
// `RaisePowerModeChanged` inside `DesktopWasm.Wpf`). The packager binds
// non-overlapped member refs to the real assembly direct; this bridge
// forwards the genuinely dual-owned overlapping names into the engine so
// both subscriber sets observe one coherent broadcast domain.
//
// Overlap set (engine-raisable surface, nothing more): DisplaySettingsChanged,
// UserPreferenceChanged, PowerModeChanged. NOTE on the RT4 brief's
// parenthetical: staged-entry `add_SystemColorsChanged` is an APP-level
// event (mRemoteNG's own config objects), not a SystemEvents member — the
// engine declares no SystemColorsChanged and the real assembly owns that
// name, so it binds direct and this bridge claims nothing outside the
// three engine-raisable names.
//
// The real assembly is headless by construction (all events are no-op
// add/remove stubs that drop handlers), so the subscription half below is
// the contract half: the exact delegates the real broadcast would invoke,
// proven by unit test through the same forward path the host/test entry
// points drive. The host drives the live half from browser signals
// (media-query flip -> ForwardUserPreferenceChanged, display/DPI signal ->
// ForwardDisplaySettingsChanged, power signal -> ForwardPowerModeChanged).
// The engine is reached by reflection (no compile-time reference — bundle
// integrity, same precedent as MRemoteNGStartupAdapter); anything missing
// fails closed to false, never throws out of boot.
using System.Reflection;

namespace WfWasm.CanvasHost;

internal static class SystemEventsBridgeAdapter
{
    public const string EngineAssemblyName = "DesktopWasm.Wpf";

    private const string EngineSystemEventsFullName = "Microsoft.Win32.SystemEvents";

    private static readonly HashSet<string> ForwardedEvents = new(StringComparer.Ordinal)
    {
        "DisplaySettingsChanged",
        "UserPreferenceChanged",
        "PowerModeChanged",
    };

    /// <summary>
    /// Whether the bridge forwards this broadcast name into the engine.
    /// Everything else binds the real assembly direct (packager route) and
    /// passes through untouched — the bridge claims nothing outside the
    /// overlap set.
    /// </summary>
    public static bool IsForwarded(string? eventName) =>
        !string.IsNullOrEmpty(eventName) && ForwardedEvents.Contains(eventName);

    /// <summary>
    /// Engine-raise core. Default reflects into the bundle-shipped engine;
    /// tests swap a recorder (restored in finally — xunit runs one class
    /// sequentially, and no other suite touches this core).
    /// </summary>
    internal static Func<string, object?[], bool> RaiseEngine { get; set; } = DefaultRaiseEngine;

    internal static bool DefaultRaiseEngine(string methodName, object?[] args)
    {
        try
        {
            var engine = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(
                a => string.Equals(a.GetName().Name, EngineAssemblyName, StringComparison.Ordinal));
            if (engine is null)
                return false;
            var method = engine.GetType(EngineSystemEventsFullName)?.GetMethod(
                methodName, BindingFlags.Public | BindingFlags.Static);
            if (method is null)
                return false;
            var parameters = method.GetParameters();
            if (parameters.Length != args.Length)
                return false;
            var converted = new object?[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                var target = parameters[i].ParameterType;
                converted[i] = target.IsEnum && args[i] is int n
                    ? Enum.ToObject(target, n)
                    : args[i];
            }
            method.Invoke(null, converted);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool subscribed;

    public static bool IsSubscribed => subscribed;

    /// <summary>
    /// Registers the forward delegates on the real broadcast assembly.
    /// Idempotent. Headless no-op-safe: the real assembly drops handlers
    /// by construction, so this never fires today and never throws.
    /// </summary>
    public static void EnsureSubscribed()
    {
        if (subscribed)
            return;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnRealDisplaySettingsChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnRealUserPreferenceChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnRealPowerModeChanged;
        subscribed = true;
    }

    public static bool ForwardDisplaySettingsChanged() =>
        RaiseEngine("RaiseDisplaySettingsChanged", []);

    public static bool ForwardUserPreferenceChanged(int category) =>
        RaiseEngine("RaiseUserPreferenceChanged", [category]);

    public static bool ForwardPowerModeChanged(int mode) =>
        RaiseEngine("RaisePowerModeChanged", [mode]);

    internal static void OnRealDisplaySettingsChanged(object? sender, EventArgs e) =>
        ForwardDisplaySettingsChanged();

    internal static void OnRealUserPreferenceChanged(
        object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) =>
        ForwardUserPreferenceChanged((int)e.Category);

    internal static void OnRealPowerModeChanged(
        object sender, Microsoft.Win32.PowerModeChangedEventArgs e) =>
        ForwardPowerModeChanged((int)e.Mode);
}
