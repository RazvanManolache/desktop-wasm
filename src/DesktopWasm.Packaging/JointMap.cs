namespace DesktopWasm.Packaging;

/// <summary>
/// Shared assembly/type registries for the joint closure. These are data
/// only (assembly/type names, never engine references): the composed
/// <see cref="JointPrecedence"/> policy reads them, and the future
/// Convert/Rewrite adapter seams will apply them to the live Cecil graph.
/// </summary>
public static class JointMap
{
    public const string WpfEngineAssembly = "DesktopWasm.Wpf";
    public const string WpfEngineNamespace = "DesktopWasm.Wpf";
    public const string WinFormsEngineAssembly = "WfWasm.WinForms";
    public const string SystemEventsAssembly = "Microsoft.Win32.SystemEvents";

    /// <summary>
    /// WPF framework assemblies whose references the WPF converter rewrites
    /// (mirrors WpfTypeMap.SourceAssemblies; kept as data here so the joint
    /// policy can classify without referencing the Convert project).
    /// </summary>
    public static IReadOnlySet<string> WpfSourceAssemblies { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
        "System.Xaml",
        "UIAutomationTypes",
        "UIAutomationProvider",
        "Microsoft.Win32.SystemEvents",
        "System.Diagnostics.EventLog",
        "System.Windows.Forms",
        "System.Threading.AccessControl",
        "System.Security.Cryptography.ProtectedData",
        "System.CodeDom",
        "System.IO.Packaging",
        "ReachFramework",
        "System.Printing",
        "System.Configuration.ConfigurationManager",
        "System.ObjectModel",
        "System.Windows.Presentation",
        "System.Windows.Extensions",
    };

    /// <summary>
    /// WinForms framework assemblies the WinForms rewriter consumes. Only
    /// assemblies here route to the real WinForms engine; anything else in
    /// a WinForms-capable closure is a missing-dependency gap, never a
    /// silent pass-through.
    /// </summary>
    public static IReadOnlySet<string> WinFormsSourceAssemblies { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System.Windows.Forms",
        "System.Windows.Forms.Primitives",
        "Microsoft.Win32.SystemEvents",
        "System.Drawing",
        "System.Drawing.Common",
        "System.Drawing.Primitives",
        "Accessibility",
        "UIAutomationTypes",
        "UIAutomationProvider",
    };

    /// <summary>
    /// Assemblies claimed by BOTH maps. A reference to one of these never
    /// resolves by assembly name alone: <see cref="JointPrecedence"/>
    /// decides per referenced type, and any genuinely dual-owned type is a
    /// <see cref="JointGap.TypeCollision"/> naming both claimants.
    /// </summary>
    public static IReadOnlySet<string> OverlappedAssemblies { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System.Windows.Forms",
        "Microsoft.Win32.SystemEvents",
        "UIAutomationTypes",
        "UIAutomationProvider",
    };

    /// <summary>
    /// The five System.Windows.Forms names the WPF engine carries as
    /// platform shims (PlatformShims.cs), verified member-by-member against
    /// staged ScreenToGif binaries. Every other System.Windows.Forms type
    /// is real WinForms surface and routes to the WinForms engine.
    /// </summary>
    public static IReadOnlySet<string> WpfWinFormsShimTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "System.Windows.Forms.IWin32Window",
        "System.Windows.Forms.Keys",
        "System.Windows.Forms.Screen",
        "System.Windows.Forms.SystemInformation",
        "System.Windows.Forms.Timer",
    };

    /// <summary>
    /// The Microsoft.Win32 names the WPF engine carries as its broadcast
    /// adapter surface (SystemEvents.cs / FileDialogs-adjacent). The real
    /// SystemEvents assembly owns the full broadcast contract; the engine
    /// names below are the only ones a pure-WPF closure may bind there.
    /// </summary>
    public static IReadOnlySet<string> WpfSystemEventsShimTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Microsoft.Win32.SystemEvents",
        "Microsoft.Win32.UserPreferenceChangedEventHandler",
        "Microsoft.Win32.UserPreferenceChangedEventArgs",
        "Microsoft.Win32.UserPreferenceCategory",
        "Microsoft.Win32.PowerModeChangedEventArgs",
        "Microsoft.Win32.PowerModeChangedEventHandler",
        "Microsoft.Win32.PowerModes",
    };

    /// <summary>
    /// Single shared engine/native payload: these resolve once per package
    /// (by logical identity + version + content hash) and are never
    /// duplicated, substituted by a facade, or won by "first file wins".
    /// </summary>
    public static IReadOnlySet<string> SharedPayloadAssemblies { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsBase",
        "SkiaSharp",
        "SkiaSharp.HarfBuzz",
        "HarfBuzzSharp",
    };
}
