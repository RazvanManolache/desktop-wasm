namespace DesktopWasm.Wpf;

/// <summary>
/// Engine-facing activation contract for the fork-backed
/// WindowsFormsHost bridge (HOST-CALL-1425): BamlReader resolves the
/// exact joint-interface pair (WindowsFormsIntegration assembly +
/// System.Windows.Forms.Integration.WindowsFormsHost) through the
/// registered provider instead of an AppDomain-wide scan or arbitrary
/// fallback. The adapter (which references the engine) and BrowserHost
/// startup own registration; the engine never references the adapter,
/// so the boundary stays acyclic. Missing, mismatched-identity,
/// wrong-type and constructor-failure providers all fail precise —
/// never a substitute panel, swallowed error or success flag.
/// </summary>
public static class WindowsFormsHostActivation
{
    /// <summary>Joint-interface assembly the packaged closures reference
    /// (mirrors WindowsFormsHostResolver.JointInterfaceAssemblyName;
    /// a drift-guard test pins the equality).</summary>
    public const string AssemblySimpleName = "WindowsFormsIntegration";

    /// <summary>Canonical full name of the bridge host the provider must
    /// construct (mirrors WindowsFormsHostResolver.WindowsFormsHostFullName).</summary>
    public const string HostFullName = "System.Windows.Forms.Integration.WindowsFormsHost";

    private static readonly object Gate = new();
    private static Func<UIElement>? s_factory;

    /// <summary>Registers the bridge provider factory (last registration
    /// wins; BrowserHost startup owns the production call, tests own
    /// their scoped registrations and unregister in finally).</summary>
    public static void Register(Func<UIElement> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (Gate) s_factory = factory;
    }

    /// <summary>Clears the bridge provider registration.</summary>
    public static void Unregister()
    {
        lock (Gate) s_factory = null;
    }

    /// <summary>True while a bridge provider is registered.</summary>
    public static bool IsRegistered
    {
        get { lock (Gate) return s_factory is not null; }
    }

    /// <summary>Constructs the bridge host for the exact joint-interface
    /// pair. Identity mismatch, missing provider, wrong-type instance
    /// and constructor failure all fail precise with the reason.</summary>
    public static bool TryCreate(string? requestedAssembly, string? requestedType, out UIElement? element, out string? failure)
    {
        element = null;
        if (!string.Equals(requestedAssembly, AssemblySimpleName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(requestedType, HostFullName, StringComparison.Ordinal))
        {
            failure = $"cannot instantiate custom CLR type '{requestedType}' from '{requestedAssembly}' (bridge activation takes the exact joint-interface pair '{AssemblySimpleName}', '{HostFullName}' only)";
            return false;
        }
        Func<UIElement>? factory;
        lock (Gate) factory = s_factory;
        if (factory is null)
        {
            failure = $"cannot instantiate custom CLR type '{HostFullName}' (WindowsFormsHost bridge provider is not registered; register the joint-interface provider before loading BAML with WinForms hosts)";
            return false;
        }
        UIElement created;
        try
        {
            created = factory();
        }
        catch (Exception ex)
        {
            failure = $"cannot instantiate custom CLR type '{HostFullName}' (bridge provider constructor failed: {ex.GetBaseException().Message})";
            return false;
        }
        if (created is null || !string.Equals(created.GetType().FullName, HostFullName, StringComparison.Ordinal))
        {
            failure = $"cannot instantiate custom CLR type '{HostFullName}' (bridge provider returned <{created?.GetType().FullName ?? "null"}>; providers must construct the canonical host type)";
            return false;
        }
        element = created;
        failure = null;
        return true;
    }
}
