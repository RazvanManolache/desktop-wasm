using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Interop;

namespace DesktopWasm.Packaging.Tests;

/// <summary>
/// The converted joint entry references the bridge chain by scope
/// (DesktopWasm.Wpf, WfWasm.WinForms, WfWasm.Drawing) and the joint
/// interface (WindowsFormsIntegration, runtime-provided). Type-level
/// resolution in a desktop test cannot prove those scopes are actually
/// rooted in the published browser bundle, so this pins the real
/// BrowserHost boot manifest: every required provider present with
/// matching content hash, the joint interface never packaged, and the
/// resolved host the real bridge rather than the WPF engine shim.
/// </summary>
public sealed class BrowserBundleRootingTests
{
    private static readonly string[] RequiredProviders =
    [
        "DesktopWasm.Interop",
        "DesktopWasm.Wpf",
        "WfWasm.WinForms",
        "WfWasm.Drawing",
        "WindowsBase",
        "DesktopWasm.BrowserHost",
    ];

    private static string BundleFrameworkDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DesktopWasm.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repository root (DesktopWasm.slnx) not found above the test binaries.");
        var fw = Path.Combine(dir.FullName, "src", "DesktopWasm.BrowserHost", "bin", "Debug", "net9.0", "wwwroot", "_framework");
        Assert.True(Directory.Exists(fw),
            "BrowserHost bundle not published (build/publish DesktopWasm.BrowserHost Debug first): " + fw);
        return fw;
    }

    private static Dictionary<string, string> BootAssemblyHashes(string frameworkDirectory)
    {
        var manifestPath = Path.Combine(frameworkDirectory, "blazor.boot.json");
        Assert.True(File.Exists(manifestPath), "blazor.boot.json missing from BrowserHost bundle: " + manifestPath);
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.True(document.RootElement.TryGetProperty("resources", out var resources),
            "blazor.boot.json has no resources table.");
        Assert.True(resources.TryGetProperty("assembly", out var assemblyTable),
            "blazor.boot.json has no resources.assembly table.");
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in assemblyTable.EnumerateObject())
            hashes[property.Name] = property.Value.GetString() ?? string.Empty;
        return hashes;
    }

    [Fact]
    public void BridgeChain_RootedInPublishedBrowserBundle_WithMatchingHashes()
    {
        var framework = BundleFrameworkDirectory();
        var hashes = BootAssemblyHashes(framework);

        foreach (var provider in RequiredProviders)
        {
            var resource = hashes.Keys.FirstOrDefault(k =>
                k.StartsWith(provider + ".", StringComparison.OrdinalIgnoreCase) &&
                k.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase));
            Assert.True(resource is not null,
                $"Published BrowserHost bundle does not root '{provider}': converted entries reference this scope at load.");
            var expected = hashes[resource];
            Assert.StartsWith("sha256-", expected);
            var path = Path.Combine(framework, resource);
            Assert.True(File.Exists(path), $"Bundled payload missing on disk: {resource}");
            var actual = "sha256-" + System.Convert.ToBase64String(SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(expected, actual);
        }

        // The joint interface is runtime-provided: it must never appear as
        // a packaged bundle payload (that would shadow the bridge).
        Assert.DoesNotContain(hashes.Keys, k =>
            k.StartsWith("WindowsFormsIntegration.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BridgeChildSetter_AcceptsRealForkControl()
    {
        // The staged original calls set_Child (the only WFI member ref
        // besides the type itself): the bridge setter must accept the real
        // fork Control the converted call site passes — not the engine
        // paintable, not an incompatible base.
        var provider = WindowsFormsHostResolver.ResolveHostType(
            "WindowsFormsIntegration", WindowsFormsHostResolver.WindowsFormsHostFullName);
        Assert.NotNull(provider);
        var setter = provider.GetMethod("set_Child");
        Assert.NotNull(setter);
        var parameter = Assert.Single(setter.GetParameters());
        Assert.Equal("System.Windows.Forms.Control", parameter.ParameterType.FullName);
        Assert.Equal("WfWasm.WinForms", parameter.ParameterType.Assembly.GetName().Name);
    }

    [Fact]
    public void JointInterface_ResolvesToInteropBridge_NotWpfShim()
    {
        // One canonical bridge: the resolver serves the real adapter type
        // from the Interop provider assembly, never the engine shim
        // (DesktopWasm.Wpf.WindowsFormsHost hosts IImmediatePaintable).
        var provider = WindowsFormsHostResolver.ResolveHostType(
            "WindowsFormsIntegration", WindowsFormsHostResolver.WindowsFormsHostFullName);
        Assert.NotNull(provider);
        Assert.Same(typeof(System.Windows.Forms.Integration.WindowsFormsHost), provider);
        Assert.Equal("DesktopWasm.Interop", provider.Assembly.GetName().Name);

        var shim = typeof(DesktopWasm.Wpf.WindowsFormsHost);
        Assert.NotSame(provider, shim);
        Assert.NotEqual(provider.Assembly.GetName().Name, shim.Assembly.GetName().Name);
        var shimChild = shim.GetProperty("Child")!.PropertyType;
        Assert.Equal("DesktopWasm.Wpf.IImmediatePaintable", shimChild.FullName);
        var bridgeChild = provider.GetProperty("Child")!.PropertyType;
        Assert.Equal("System.Windows.Forms.Control", bridgeChild.FullName);
        Assert.Equal("WfWasm.WinForms", bridgeChild.Assembly.GetName().Name);
    }
}
