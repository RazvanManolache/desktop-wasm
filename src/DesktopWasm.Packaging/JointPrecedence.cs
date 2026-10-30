namespace DesktopWasm.Packaging;

/// <summary>Where one source reference routes in the joint package.</summary>
public enum JointTargetKind
{
    /// <summary>Routes to the real WinForms engine (WfWasm.WinForms).</summary>
    WinFormsEngine,
    /// <summary>Routes to the WPF engine (DesktopWasm.Wpf), shims included.</summary>
    WpfEngine,
    /// <summary>Routes to the standalone SystemEvents assembly.</summary>
    SystemEventsAssembly,
    /// <summary>No engine owns this reference: conversion gap.</summary>
    Gap,
    /// <summary>Both engines claim this type: visible conflict, never silent.</summary>
    Collision,
}

/// <summary>One routing decision plus the evidence behind it.</summary>
public sealed record JointRoute(JointTargetKind Kind, string TargetAssembly, string TargetNamespace, string TargetName, string Reason);

/// <summary>
/// Composed mapping policy over both transforms. A single assembly may
/// reference both frameworks; each referenced type routes independently,
/// and the same source bytes are never rewritten twice blindly: the
/// caller applies WinForms positions first, then WPF positions, then
/// validates (see <see cref="JointPipeline"/>).
/// </summary>
public static class JointPrecedence
{
    /// <summary>
    /// Routes one (source assembly, namespace, name) reference given the
    /// capabilities of the assembly that carries it.
    /// </summary>
    public static JointRoute ResolveType(
        string sourceAssembly, string sourceNamespace, string sourceName, FrameworkCapabilities capabilities)
    {
        var fullName = string.IsNullOrEmpty(sourceNamespace) ? sourceName : sourceNamespace + "." + sourceName;

        // Overlap 1: System.Windows.Forms. Real controls always route to
        // the real WinForms engine. The five WPF shim names route to the
        // WPF engine ONLY in a pure-WPF closure (ScreenToGif prerequisites);
        // in a WinForms-capable assembly the real fork owns those names.
        if (sourceAssembly.Equals("System.Windows.Forms", StringComparison.OrdinalIgnoreCase))
        {
            if (JointMap.WpfWinFormsShimTypes.Contains(fullName) && capabilities is FrameworkCapabilities.Wpf)
                return new JointRoute(JointTargetKind.WpfEngine,
                    JointMap.WpfEngineAssembly, sourceNamespace, sourceName,
                    "Pure-WPF closure: WPF platform shim owns this member-verified name.");
            if (capabilities.HasFlag(FrameworkCapabilities.WinForms))
                return new JointRoute(JointTargetKind.WinFormsEngine,
                    JointMap.WinFormsEngineAssembly, sourceNamespace, sourceName,
                    "WinForms-capable closure: real controls route to the real WinForms fork.");
            if (JointMap.WpfWinFormsShimTypes.Contains(fullName))
                return new JointRoute(JointTargetKind.WpfEngine,
                    JointMap.WpfEngineAssembly, sourceNamespace, sourceName,
                    "Framework-neutral closure: only the WPF shim claims this name.");
            return new JointRoute(JointTargetKind.Gap, "", "", "",
                $"System.Windows.Forms type '{fullName}' has no WinForms capability and no WPF shim: conversion gap.");
        }

        // Overlap 2: Microsoft.Win32.SystemEvents. Pure-WPF closures bind
        // the engine broadcast adapter; WinForms-capable closures bind the
        // real SystemEvents assembly. A mixed assembly referencing broadcast
        // members beyond the adapter surface is a visible collision naming
        // both claimants, not a silent shim win.
        if (sourceAssembly.Equals("Microsoft.Win32.SystemEvents", StringComparison.OrdinalIgnoreCase) ||
            sourceAssembly.Equals(JointMap.SystemEventsAssembly, StringComparison.OrdinalIgnoreCase))
        {
            var wpfOwns = JointMap.WpfSystemEventsShimTypes.Contains(fullName);
            var winForms = capabilities.HasFlag(FrameworkCapabilities.WinForms);
            var wpf = capabilities.HasFlag(FrameworkCapabilities.Wpf);
            if (winForms && wpf)
            {
                if (!wpfOwns)
                    return new JointRoute(JointTargetKind.SystemEventsAssembly,
                        JointMap.SystemEventsAssembly, sourceNamespace, sourceName,
                        "Mixed closure: only the SystemEvents assembly claims this broadcast member.");
                return new JointRoute(JointTargetKind.Collision, "", "", "",
                    $"Type '{fullName}' is claimed by both the SystemEvents assembly (WinForms broadcast contract) " +
                    $"and the WPF engine adapter (host-driven RaiseUserPreferenceChanged): register an explicit " +
                    $"bridge adapter instead of letting either map win silently.");
            }
            if (winForms)
                return new JointRoute(JointTargetKind.SystemEventsAssembly,
                    JointMap.SystemEventsAssembly, sourceNamespace, sourceName,
                    "WinForms-only closure: full broadcast contract lives in the SystemEvents assembly; the WPF engine is not dragged in.");
            if (wpfOwns)
                return new JointRoute(JointTargetKind.WpfEngine,
                    JointMap.WpfEngineAssembly, sourceNamespace, sourceName,
                    "WPF closure: engine broadcast adapter owns this member-verified name.");
            return new JointRoute(JointTargetKind.SystemEventsAssembly,
                JointMap.SystemEventsAssembly, sourceNamespace, sourceName,
                "Only the SystemEvents assembly claims this broadcast member.");
        }

        // Non-overlapped WPF sources route to the WPF engine by namespace
        // rule (System.Windows.*) or explicit override membership; anything
        // outside both is a gap, never a pass-through.
        if (JointMap.WpfSourceAssemblies.Contains(sourceAssembly))
            return new JointRoute(JointTargetKind.WpfEngine,
                JointMap.WpfEngineAssembly, WpfTargetNamespace(sourceNamespace, sourceName), sourceName,
                $"WPF source assembly '{sourceAssembly}': engine owns the converted contract.");
        if (JointMap.WinFormsSourceAssemblies.Contains(sourceAssembly))
            return new JointRoute(JointTargetKind.WinFormsEngine,
                JointMap.WinFormsEngineAssembly, sourceNamespace, sourceName,
                $"WinForms source assembly '{sourceAssembly}': real fork owns the converted contract.");
        return new JointRoute(JointTargetKind.Gap, "", "", "",
            $"Assembly '{sourceAssembly}' is owned by neither map: missing-dependency gap.");
    }

    private static string WpfTargetNamespace(string sourceNamespace, string sourceName)
    {
        // Mirrors WpfTypeMap: System.Windows.* collapses onto the engine
        // namespace; outer namespaces keep their shape unless an explicit
        // override moves them (overrides live in Convert until the seam
        // lands; this policy reports the default-rule target).
        if (sourceNamespace.Equals("System.Windows", StringComparison.Ordinal) ||
            sourceNamespace.StartsWith("System.Windows.", StringComparison.Ordinal))
            return JointMap.WpfEngineNamespace;
        return sourceNamespace;
    }
}
