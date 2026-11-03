using System.Reflection;

namespace DesktopWasm.Interop;

/// <summary>
/// Runtime side of the joint-interface gap: resolves the net48
/// <c>WindowsFormsIntegration</c> assembly reference (GAC-only upstream,
/// never packaged, never demanded as a file by <c>JointClosure</c>) to the
/// real interop bridge provider in this assembly
/// (<c>System.Windows.Forms.Integration.WindowsFormsHost</c> over a real
/// fork <c>System.Windows.Forms.Control</c>).
/// BrowserHost registers <c>Resolve</c> on
/// <c>AppDomain.AssemblyResolve</c>; engine projects never reference this
/// adapter (acyclic boundary preserved).
/// </summary>
public static class WindowsFormsHostResolver
{
    /// <summary>Joint-interface assembly the TestApp closure references.</summary>
    public const string JointInterfaceAssemblyName = "WindowsFormsIntegration";

    /// <summary>Full name of the real bridge type this resolver provides.</summary>
    public const string WindowsFormsHostFullName = "System.Windows.Forms.Integration.WindowsFormsHost";

    /// <summary>
    /// Returns the bridge provider assembly when <paramref name="displayName"/>
    /// names the joint interface (short name or full display name,
    /// case-insensitive); otherwise null so other resolvers keep running.
    /// </summary>
    public static Assembly? Resolve(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;
        string simpleName;
        try
        {
            simpleName = new AssemblyName(displayName).Name ?? string.Empty;
        }
        catch
        {
            return null;
        }
        if (!simpleName.Equals(JointInterfaceAssemblyName, StringComparison.OrdinalIgnoreCase))
            return null;
        return typeof(System.Windows.Forms.Integration.WindowsFormsHost).Assembly;
    }

    /// <summary>
    /// Returns the real bridge <see cref="Type"/> when the request names the
    /// joint interface assembly plus the host full name; otherwise null.
    /// </summary>
    public static Type? ResolveHostType(string? assemblyDisplayName, string? fullTypeName)
    {
        if (!string.Equals(fullTypeName, WindowsFormsHostFullName, StringComparison.Ordinal))
            return null;
        return Resolve(assemblyDisplayName) is null
            ? null
            : typeof(System.Windows.Forms.Integration.WindowsFormsHost);
    }
}
