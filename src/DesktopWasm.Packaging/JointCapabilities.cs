using Mono.Cecil;

namespace DesktopWasm.Packaging;

/// <summary>Which framework families one assembly actually uses.</summary>
[Flags]
public enum FrameworkCapabilities
{
    None = 0,
    WinForms = 1,
    Wpf = 2,
    Mixed = WinForms | Wpf,
}

/// <summary>
/// Detects WinForms/WPF capability from assembly references. A single
/// assembly can reference both frameworks; that is a mixed assembly, not
/// an exclusive choice. Engine assemblies themselves (already converted
/// output) report <see cref="FrameworkCapabilities.None"/> so the
/// pipeline treats them as converted, not as source.
/// </summary>
public static class JointCapabilities
{
    private static readonly ISet<string> WinFormsMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System.Windows.Forms",
        "System.Windows.Forms.Primitives",
    };

    private static readonly ISet<string> WpfMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
        "System.Xaml",
    };

    public static FrameworkCapabilities Detect(IEnumerable<string> assemblyReferences)
    {
        var capabilities = FrameworkCapabilities.None;
        foreach (var reference in assemblyReferences)
        {
            if (WinFormsMarkers.Contains(reference)) capabilities |= FrameworkCapabilities.WinForms;
            if (WpfMarkers.Contains(reference)) capabilities |= FrameworkCapabilities.Wpf;
        }
        return capabilities;
    }

    public static FrameworkCapabilities Detect(ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return Detect(module.AssemblyReferences.Select(r => r.Name));
    }

    public static FrameworkCapabilities Detect(AssemblyDefinition assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Detect(assembly.MainModule);
    }
}
