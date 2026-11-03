extern alias WfiBridge;

using System.Reflection;

namespace DesktopWasm.BrowserHost;

/// <summary>
/// dw-runtime owned BrowserHost bridge-registration region (excludes shell
/// entry.mjs/gateway and importer Fiddler work).
///
/// The packaged TestApp closure references the net48 joint interface
/// <c>WindowsFormsIntegration</c>, which is runtime-provided: never packaged
/// from disk and never demanded as a file (see
/// <c>JointMap.RuntimeProvidedJointInterfaces</c>). This registration
/// supplies that reference at runtime from the real interop bridge
/// (<c>System.Windows.Forms.Integration.WindowsFormsHost</c> over a real
/// fork control). It answers ONLY the joint-interface name and returns null
/// for everything else, so the package image resolver
/// (<c>ApplicationPackageLoader</c>'s AssemblyResolve handler, added later
/// per package) keeps owning all packaged assemblies. Registered once;
/// safe to call again.
/// </summary>
public static class InteropBridgeRegistration
{
    private static readonly ResolveEventHandler s_handler = (_, args) => Resolve(args?.Name);
    private static int s_registered;

    /// <summary>Installs the joint-interface AssemblyResolve handler once.</summary>
    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref s_registered, 1) == 0)
        {
            AppDomain.CurrentDomain.AssemblyResolve += s_handler;
            // Activation wiring for the same provider (HOST-CALL-1425):
            // BamlReader resolves the exact joint-interface pair through
            // this factory — the real fork-backed bridge, constructed
            // genuine on first host element. Engine never references this
            // adapter; the factory closure is the only coupling.
            DesktopWasm.Wpf.WindowsFormsHostActivation.Register(
                () => new WfiBridge::System.Windows.Forms.Integration.WindowsFormsHost());
        }
    }

    /// <summary>
    /// Resolves the joint-interface assembly to the real bridge provider;
    /// null for any other name (package resolver owns those).
    /// </summary>
    public static Assembly? Resolve(string? assemblyDisplayName) =>
        WfiBridge::DesktopWasm.Interop.WindowsFormsHostResolver.Resolve(assemblyDisplayName);
}
