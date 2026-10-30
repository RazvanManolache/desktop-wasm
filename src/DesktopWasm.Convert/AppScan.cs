using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DesktopWasm.Convert;

/// <summary>Read-only conversion triage for one managed app binary.</summary>
public sealed record AppScanReport(
    string FileName,
    IReadOnlyList<string> AssemblyReferences,
    IReadOnlyList<string> PInvokeModules,
    IReadOnlyList<string> PInvokeMethods,
    IReadOnlyList<string> ManifestResources,
    IReadOnlyList<string> WpfMarkers);

/// <summary>
/// Inspects a candidate exe/dll without loading it (PEReader +
/// metadata, never Assembly.Load): framework references, native
/// P/Invoke surface, embedded resources, and WPF markers (framework
/// assembly refs, .g.resources BAML, x:Class-bearing Page items are
/// visible as TypeDefs whose base is a framework type).
/// </summary>
public static class AppScanner
{
    private static readonly string[] WpfAssemblies =
        ["PresentationFramework", "PresentationCore", "WindowsBase", "System.Xaml",
         "UIAutomationTypes", "UIAutomationProvider", "PresentationNative"];

    public static AppScanReport Scan(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"App assembly not found: {assemblyPath}", assemblyPath);
        using var stream = File.OpenRead(assemblyPath);
        return Scan(stream, Path.GetFileName(assemblyPath));
    }

    /// <summary>Scans an in-memory image (tests synthesize fixtures).</summary>
    public static AppScanReport Scan(Stream image, string fileName)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var pe = new PEReader(image);
        if (!pe.HasMetadata)
            throw new InvalidOperationException($"'{fileName}' is not a managed assembly.");
        var reader = pe.GetMetadataReader();

        var refs = new List<string>();
        foreach (var handle in reader.AssemblyReferences)
        {
            var reference = reader.GetAssemblyReference(handle);
            refs.Add(reader.GetString(reference.Name));
        }

        var pinvokeModules = new List<string>();
        var pinvokeMethods = new List<string>();
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            var import = method.GetImport();
            if (import.Module.IsNil) continue;
            var module = reader.GetModuleReference(import.Module);
            pinvokeModules.Add(reader.GetString(module.Name));
            pinvokeMethods.Add($"{reader.GetString(reader.GetTypeDefinition(method.GetDeclaringType()).Name)}::{reader.GetString(method.Name)}");
        }

        var resources = new List<string>();
        foreach (var handle in reader.ManifestResources)
            resources.Add(reader.GetString(reader.GetManifestResource(handle).Name));

        var markers = new List<string>();
        foreach (var wpf in WpfAssemblies)
        {
            if (refs.Any(r => r.Equals(wpf, StringComparison.OrdinalIgnoreCase)))
                markers.Add($"references {wpf}");
        }
        if (resources.Any(r => r.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase)))
            markers.Add("compiled XAML (.g.resources BAML)");
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            var ns = reader.GetString(type.Namespace);
            if (!ns.StartsWith("System.Windows", StringComparison.Ordinal)) continue;
            markers.Add($"framework type {ns}.{reader.GetString(type.Name)}");
        }

        return new AppScanReport(
            fileName,
            refs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r => r).ToList(),
            pinvokeModules.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(m => m).ToList(),
            pinvokeMethods.Distinct(StringComparer.Ordinal).OrderBy(m => m).ToList(),
            resources.OrderBy(r => r).ToList(),
            markers.Distinct(StringComparer.Ordinal).OrderBy(m => m).ToList());
    }
}
