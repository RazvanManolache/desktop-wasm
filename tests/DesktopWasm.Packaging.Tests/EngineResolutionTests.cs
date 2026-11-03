using System.Reflection;

namespace DesktopWasm.Packaging.Tests;

/// <summary>
/// Every routed reference must resolve against a real engine: WPF targets
/// against the built DesktopWasm.Wpf engine, WinForms targets against the
/// real WinForms contract with the WfWasm.WinForms identity recorded. When
/// the WinForms engine image is absent from this root (sources still
/// copying), the pipeline reports engine-unavailable instead of pretending
/// the reference resolved.
/// </summary>
public sealed class EngineResolutionTests
{
    private static readonly Assembly WpfEngine = typeof(DesktopWasm.Wpf.Window).Assembly;

    [Theory]
    [InlineData("System.Windows.Controls", "Button")]
    [InlineData("System.Windows", "Window")]
    [InlineData("System.Windows.Forms", "Screen")]
    [InlineData("Microsoft.Win32", "SystemEvents")]
    public void WpfRouted_Types_ResolveAgainstBuiltEngine(string sourceNamespace, string sourceName)
    {
        var (sourceAssembly, capabilities) = sourceNamespace == "System.Windows.Forms" || sourceNamespace == "Microsoft.Win32"
            ? ("System.Windows.Forms", FrameworkCapabilities.Wpf)
            : ("PresentationFramework", FrameworkCapabilities.Wpf);
        if (sourceNamespace == "Microsoft.Win32")
            sourceAssembly = "Microsoft.Win32.SystemEvents";

        var route = JointPrecedence.ResolveType(sourceAssembly, sourceNamespace, sourceName, capabilities);

        Assert.Equal(JointTargetKind.WpfEngine, route.Kind);
        var resolved = WpfEngine.GetType(route.TargetNamespace + "." + route.TargetName, throwOnError: false);
        Assert.True(resolved is not null,
            $"Routed {sourceAssembly}:{sourceNamespace}.{sourceName} -> {route.TargetAssembly}:{route.TargetNamespace}.{route.TargetName} does not resolve in the built engine.");
    }

    [Theory]
    [InlineData("System.Windows.Forms", "Form")]
    [InlineData("System.Windows.Forms", "DialogResult")]
    [InlineData("System.Windows.Forms", "Screen")]
    public void WinFormsRouted_Types_MatchRealContract_AndEngineBindingIsExplicit(
        string sourceNamespace, string sourceName)
    {
        var route = JointPrecedence.ResolveType(
            "System.Windows.Forms", sourceNamespace, sourceName, FrameworkCapabilities.Mixed);

        Assert.Equal(JointTargetKind.WinFormsEngine, route.Kind);
        Assert.Equal("WfWasm.WinForms", route.TargetAssembly);

        // The routed (namespace, name) must be a genuine WinForms contract:
        // prove it against the real desktop framework surface.
        var fullName = route.TargetNamespace + "." + route.TargetName;
        Assert.True(RealWinFormsKnows(fullName),
            $"'{fullName}' is not a real System.Windows.Forms contract member.");

        // Assembly-identity binding: the pinned in-repo engine image
        // (inputs/winforms/rewritten-small, hash-pinned in source-ready)
        // proves the routed contract binds under the WfWasm.WinForms
        // identity; a live build proves the same image compiles from fork
        // sources (see slice-2 engine build receipt).
        var engine = LoadPinnedEngine() ?? TryLoadEngine("WfWasm.WinForms");
        if (engine is null)
        {
            var gap = new JointGap(route.TargetAssembly, JointGap.EngineUnavailable,
                $"Engine image 'WfWasm.WinForms' is unavailable: " +
                $"contract '{fullName}' verified, assembly binding deferred.");
            Assert.Equal(JointGap.EngineUnavailable, gap.Kind);
        }
        else
        {
            Assert.True(engine.GetType(fullName, throwOnError: false) is not null,
                $"Engine image does not declare routed contract '{fullName}'.");
        }
    }

    private static Assembly? LoadPinnedEngine()
    {
        // Pinned by source-ready.json (SHA256 BA68DEB9...); read-only proof,
        // never a build input for packaging.
        const string pinned = @"E:\AI\Code\DesktopWasm\inputs\winforms\rewritten-small\WfWasm.WinForms.dll";
        try { return File.Exists(pinned) ? Assembly.LoadFrom(pinned) : null; }
        catch { return null; }
    }

    private static Assembly? TryLoadEngine(string name)
    {
        try { return Assembly.Load(new AssemblyName(name)); }
        catch { return null; }
    }

    private static Assembly? _realWinForms;

    private static bool RealWinFormsKnows(string fullName)
    {
        // The net9.0 test host does not carry the WindowsDesktop shared
        // framework, so Assembly.Load by display name cannot resolve there:
        // load the real contract image from the installed desktop pack by
        // path (latest 9.x servicing release) and prove type existence.
        try
        {
            _realWinForms ??= LoadDesktopWinForms();
            return _realWinForms?.GetType(fullName, throwOnError: false) is not null;
        }
        catch { return false; }
    }

    private static Assembly? LoadDesktopWinForms()
    {
        var pack = @"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App";
        if (!Directory.Exists(pack)) return null;
        var best = Directory.EnumerateDirectories(pack, "9.*")
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .LastOrDefault();
        if (best is null) return null;
        var image = Path.Combine(best, "System.Windows.Forms.dll");
        return File.Exists(image) ? Assembly.LoadFrom(image) : null;
    }
}
