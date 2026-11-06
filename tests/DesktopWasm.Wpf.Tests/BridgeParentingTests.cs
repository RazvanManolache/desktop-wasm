using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// WPF-SERVED-W10-1318-q6n4 task 2: bridge-parenting repro (importer probe
/// source: reports/IMP-MATERIAL-1258-bridge-probe.json +
/// .artifacts/tmp/runs/dw-import/bridge-probe/Program.cs).
/// The converted TestApp.MainWindow carries the winFormsHost as Content of
/// the LayoutAnchorable ContentId=WinFormsWindow (visible+selected); the
/// engine lane must materialize the anchorable pane control so the host
/// enters the visual/logical tree. RED-first: this test fails until that
/// materialization exists. Seam facts (one-shot diagnostic, see report):
/// manager Template/Style null, Theme=ArcDarkTheme, all pane/item styles and
/// templates null; LayoutAnchorablePaneControl present but needs ctor args;
/// GetLayoutItemFromModel works; no engine anchorable control types exist.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class BridgeParentingTests
{
    private static readonly object JointGate = new();
    private static bool _jointLoaded;

    private static void LoadJointPackage()
    {
        lock (JointGate)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(
                directory.FullName, "inputs", "mixed", "testapp-package", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var apps = System.IO.Path.Combine(directory!.FullName, "inputs", "mixed", "testapp-package");
            using var document = JsonDocument.Parse(
                File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
            var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var bytes = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
                var actual = Convert.ToHexString(SHA256.HashData(bytes));
                Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                    $"hash mismatch in staged '{file}': {actual} != {expected}");
                images[assembly.GetProperty("name").GetString()!] = bytes;
            }
            if (!_jointLoaded)
            {
                AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
                {
                    var requested = new AssemblyName(args.Name).Name;
                    var loaded = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, requested, StringComparison.OrdinalIgnoreCase));
                    if (loaded is not null) return loaded;
                    if (requested is not null && images.TryGetValue(requested, out var image))
                        return Assembly.Load(image);
                    if (string.Equals(requested, "WindowsFormsIntegration", StringComparison.OrdinalIgnoreCase))
                        return RealBridgeAssembly();
                    var bridgeDir = System.IO.Path.GetDirectoryName(RealBridgeAssembly().Location);
                    if (bridgeDir != null && requested != null)
                    {
                        var candidate = System.IO.Path.Combine(bridgeDir, requested + ".dll");
                        if (System.IO.File.Exists(candidate))
                            return Assembly.LoadFrom(candidate);
                    }
                    return null;
                };
                foreach (var image in images.Values)
                {
                    try { Assembly.Load(image); } catch { }
                }
                _jointLoaded = true;
            }
        }
    }

    private static Assembly RealBridgeAssembly()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.Interop", "DesktopWasm.Interop.csproj")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        foreach (var config in new[] { "Debug", "Release" })
        {
            var dll = System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.Interop", "bin", config, "net9.0", "DesktopWasm.Interop.dll");
            if (System.IO.File.Exists(dll)) return Assembly.LoadFrom(dll);
        }
        throw new InvalidOperationException("DesktopWasm.Interop.dll is not built");
    }

    private static Type RealBridgeType()
    {
        var resolver = RealBridgeAssembly().GetType("DesktopWasm.Interop.WindowsFormsHostResolver");
        Assert.NotNull(resolver);
        var fullName = (string?)resolver!.GetField("WindowsFormsHostFullName")!.GetRawConstantValue();
        var hostType = (Type?)resolver.GetMethod("ResolveHostType")!.Invoke(null, new object?[] { "WindowsFormsIntegration", fullName });
        Assert.NotNull(hostType);
        return hostType!;
    }

    private static object? FindAnchorable(object layoutRoot)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        object? Walk(object? node, int depth)
        {
            if (node is null || depth > 40 || !seen.Add(node)) return null;
            var t = node.GetType();
            if (t.FullName == "AvalonDock.Layout.LayoutAnchorable" &&
                string.Equals(t.GetProperty("ContentId")?.GetValue(node) as string, "WinFormsWindow", StringComparison.Ordinal))
                return node;
            if (node is FrameworkElement fe)
            {
                foreach (var c in fe.LogicalChildren)
                {
                    var hit = Walk(c, depth + 1);
                    if (hit is not null) return hit;
                }
                if (fe is ContentControl cc && cc.Content is not null)
                {
                    var hit = Walk(cc.Content, depth + 1);
                    if (hit is not null) return hit;
                }
                return null;
            }
            if (node is System.Collections.IEnumerable seq && node is not string)
            {
                foreach (var item in seq)
                {
                    var hit = Walk(item, depth + 1);
                    if (hit is not null) return hit;
                }
                return null;
            }
            if (t.Assembly.GetName().Name!.StartsWith("System", StringComparison.Ordinal)) return null;
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                if (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) || p.PropertyType == typeof(decimal)) continue;
                object? v;
                try { v = p.GetValue(node); } catch { continue; }
                var hit = Walk(v, depth + 1);
                if (hit is not null) return hit;
            }
            return null;
        }
        return Walk(layoutRoot, 0);
    }

    private static FrameworkElement? FindConvertedControl(DependencyObject root, string fullName)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        FrameworkElement? Walk(DependencyObject node, int depth)
        {
            if (depth > 14 || !seen.Add(node)) return null;
            if (node.GetType().FullName == fullName) return node as FrameworkElement;
            if (node is FrameworkElement fe)
            {
                foreach (var c in fe.LogicalChildren)
                {
                    var hit = Walk(c, depth + 1);
                    if (hit is not null) return hit;
                }
                var content = node.GetType().GetProperty("Content")?.GetValue(node) as DependencyObject;
                if (content is not null)
                {
                    var hit = Walk(content, depth + 1);
                    if (hit is not null) return hit;
                }
            }
            return null;
        }
        return Walk(root, 0);
    }

    [Fact]
    public void Converted_anchorable_host_is_parented_into_visual_tree()
    {
        LoadJointPackage();
        var hostType = RealBridgeType();
        WindowsFormsHostActivation.Register(() => (UIElement)Activator.CreateInstance(hostType)!);
        try
        {
            var testApp = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => string.Equals(a.GetName().Name, "TestApp", StringComparison.OrdinalIgnoreCase));
            var mainWinType = testApp.GetType("TestApp.MainWindow");
            Assert.NotNull(mainWinType);
            var window = (Window)Activator.CreateInstance(mainWinType)!;

            var hostField = mainWinType.GetField("winFormsHost", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(hostField);
            var host = Assert.IsAssignableFrom<FrameworkElement>(hostField!.GetValue(window));

            // Anchorable slot must exist in the converted layout model.
            var manager = FindConvertedControl(window, "AvalonDock.DockingManager");
            Assert.True(manager is not null, "no converted DockingManager in live tree");
            var layout = manager!.GetType().GetProperty("Layout")?.GetValue(manager);
            Assert.True(layout is not null, "DockingManager.Layout is null");
            var anchorable = FindAnchorable(layout!);
            Assert.True(anchorable is not null, "WinFormsWindow anchorable missing from layout model");
            var content = anchorable!.GetType().GetProperty("Content")?.GetValue(anchorable);
            Assert.Same(host, content);

            // The engine lane materializes the pane control for the visible
            // anchorable and parents the host with a real layout slot.
            var router = new WasmInputRouter();
            var mgr = new WpfInputManager(router);
            mgr.AddRoot(window);
            var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(800, 600));
            mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
            // AvalonDock creates these controls on the first SizeChanged,
            // after the host has performed layout, like desktop WPF.
            var pane = FindConvertedControl(window, "AvalonDock.Controls.LayoutAnchorablePaneControl");
            Assert.True(pane is not null, "no LayoutAnchorablePaneControl materialized for the visible anchorable after layout");
            Assert.True(host.Parent is not null, "winFormsHost still unparented after full layout");
            Assert.True(host.RenderRect.Width > 0 && host.RenderRect.Height > 0,
                $"winFormsHost has no layout slot: {host.RenderRect}");
        }
        finally
        {
            WindowsFormsHostActivation.Unregister();
        }
    }
}
