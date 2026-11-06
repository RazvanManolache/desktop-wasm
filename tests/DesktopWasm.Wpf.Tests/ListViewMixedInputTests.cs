using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// WPF-JOB2-1227-x7k9: ListView input/focus/selection through the genuine
/// mixed TestApp window. The joint package boots the real converted
/// TestApp.MainWindow (WindowsFormsHost bridge + WinForms UserControl1);
/// a WPF ListView added to a live panel in that window must select on
/// press and move selection by arrow keys through the focused row, exactly
/// like the gallery harness. Pre-fix status unknown: this test proves the
/// mixed component-root/keyboard path instead of assuming it.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ListViewMixedInputTests
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

    private static Panel FindPanel(DependencyObject node, List<string> seen)
    {
        if (node is Panel panel) return panel;
        seen.Add(node.GetType().FullName ?? node.GetType().Name);
        if (node is FrameworkElement fe)
        {
            foreach (var child in fe.LogicalChildren)
            {
                var found = FindPanel(child, seen);
                if (found is not null) return found;
            }
        }
        return null!;
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
        throw new InvalidOperationException("DesktopWasm.Interop.dll is not built (build the Interop project before running host-activation tests)");
    }

    private static Type RealBridgeType()
    {
        var resolver = RealBridgeAssembly().GetType("DesktopWasm.Interop.WindowsFormsHostResolver");
        Assert.NotNull(resolver);
        var fullName = (string?)resolver!.GetField("WindowsFormsHostFullName")!.GetRawConstantValue();
        var hostType = (Type?)resolver.GetMethod("ResolveHostType")!.Invoke(null, new object?[] { "WindowsFormsIntegration", fullName });
        Assert.NotNull(hostType);
        Assert.Equal(WindowsFormsHostActivation.HostFullName, hostType!.FullName);
        return hostType;
    }

    private static FrameworkElement? FindFirst(DependencyObject node, Func<DependencyObject, bool> match, List<string> seen)
    {
        if (match(node)) return node as FrameworkElement;
        seen.Add(node.GetType().FullName ?? node.GetType().Name);
        if (node is FrameworkElement fe)
        {
            foreach (var child in fe.LogicalChildren)
            {
                var found = FindFirst(child, match, seen);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static readonly WasmWindowHandle Handle = new(1);

    [Fact]
    public void Mixed_window_listview_press_then_arrows_move_selection()
    {
        LoadJointPackage();
        var hostType = RealBridgeType();
        WindowsFormsHostActivation.Register(() => (UIElement)Activator.CreateInstance(hostType)!);
        Window window;
        try
        {
            var testApp = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => string.Equals(a.GetName().Name, "TestApp", StringComparison.OrdinalIgnoreCase));
            var mainWinType = testApp.GetType("TestApp.MainWindow");
            Assert.NotNull(mainWinType);
            window = (Window)Activator.CreateInstance(mainWinType)!;
            Assert.NotNull(window);
        }
        catch
        {
            WindowsFormsHostActivation.Unregister();
            throw;
        }

        try
        {
            var seen = new List<string>();
            var host = FindPanel(window, seen);
            Assert.True(host is not null,
                "no Panel in converted TestApp.MainWindow tree: " + string.Join(" > ", seen));

            var list = new ListView { Height = 200, SelectionMode = SelectionMode.Single };
            list.ItemsSource = new List<string> { "M0", "M1", "M2", "M3", "M4" };
            host!.Children.Add(list);

            var router = new WasmInputRouter();
            var manager = new WpfInputManager(router);
            manager.AddRoot(window);
            var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(800, 600));
            mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));

            var row = Assert.IsAssignableFrom<UIElement>(list.ItemContainerGenerator.ContainerFromIndex(1));
            var r = row.RenderRect;
            router.DispatchPointer(new WasmPointerArgs(Handle, r.X + r.Width / 2, r.Y + r.Height / 2, WasmPointerButton.Left, true));
            Assert.Equal(1, list.SelectedIndex);

            router.DispatchKeyboard(new WasmKeyArgs(Handle, "ArrowDown", true, false, false, false));
            Assert.Equal(2, list.SelectedIndex);
        }
        finally
        {
            WindowsFormsHostActivation.Unregister();
        }
    }

    [Fact]
    public void Mixed_window_arrows_follow_focus_not_broadcast()
    {
        // Keyboard-ownership boundary in the genuine mixed window: with
        // focus on a converted TextBox, ListView arrows must not move the
        // list selection; after pressing a row, arrows move it again.
        LoadJointPackage();
        var hostType = RealBridgeType();
        WindowsFormsHostActivation.Register(() => (UIElement)Activator.CreateInstance(hostType)!);
        Window window;
        try
        {
            var testApp = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => string.Equals(a.GetName().Name, "TestApp", StringComparison.OrdinalIgnoreCase));
            var mainWinType = testApp.GetType("TestApp.MainWindow");
            Assert.NotNull(mainWinType);
            window = (Window)Activator.CreateInstance(mainWinType)!;
            Assert.NotNull(window);
        }
        catch
        {
            WindowsFormsHostActivation.Unregister();
            throw;
        }

        try
        {
            var seen = new List<string>();
            var host = FindPanel(window, seen);
            Assert.True(host is not null,
                "no Panel in converted TestApp.MainWindow tree: " + string.Join(" > ", seen));
            var boxSeen = new List<string>();
            var box = FindFirst(window, n => n is TextBox, boxSeen);
            Assert.True(box is not null,
                "no TextBox in converted TestApp.MainWindow tree: " + string.Join(" > ", boxSeen));

            var list = new ListView { Height = 200, SelectionMode = SelectionMode.Single };
            list.ItemsSource = new List<string> { "M0", "M1", "M2", "M3", "M4" };
            host!.Children.Add(list);
            list.SelectedIndex = 1;

            var router = new WasmInputRouter();
            var manager = new WpfInputManager(router);
            manager.AddRoot(window);
            var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(800, 600));
            mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));

            Assert.True(manager.TryFocus(box!));
            router.DispatchKeyboard(new WasmKeyArgs(Handle, "ArrowDown", true, false, false, false));
            Assert.Equal(1, list.SelectedIndex);

            var row = Assert.IsAssignableFrom<UIElement>(list.ItemContainerGenerator.ContainerFromIndex(1));
            var r = row.RenderRect;
            router.DispatchPointer(new WasmPointerArgs(Handle, r.X + r.Width / 2, r.Y + r.Height / 2, WasmPointerButton.Left, true));
            Assert.Equal(1, list.SelectedIndex);
            router.DispatchKeyboard(new WasmKeyArgs(Handle, "ArrowDown", true, false, false, false));
            Assert.Equal(2, list.SelectedIndex);
        }
        finally
        {
            WindowsFormsHostActivation.Unregister();
        }
    }
}
