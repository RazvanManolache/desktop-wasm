using System.Reflection;

namespace DesktopWasm.Core.Tests;

/// <summary>
/// Browser-safety invariants for the Unified merge: the DesktopWasm engine
/// must stay P/Invoke-free (no rewriter passes needed, unlike winforms-wasm),
/// and the staged WindowsBase full copy must carry DependencyObject when the
/// host build produced one.
/// </summary>
public sealed class BrowserSafetyTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DesktopWasm.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (DesktopWasm.slnx) not found.");
    }

    private static Assembly? TryLoadEngine(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name + ".dll");
        if (!File.Exists(path)) return null;
        return Assembly.LoadFrom(path);
    }

    [Fact]
    public void Engine_assemblies_contain_no_pinvokes()
    {
        var checkedAssemblies = 0;
        var offenders = new List<string>();
        foreach (var name in new[] { "DesktopWasm.Core", "DesktopWasm.Graphics", "DesktopWasm.Wpf" })
        {
            var assembly = TryLoadEngine(name);
            if (assembly is null) continue; // Test project references a subset; skip what is not there.
            checkedAssemblies++;
            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                        offenders.Add($"{type.FullName}.{method.Name}");
                }
            }
        }

        Assert.True(checkedAssemblies > 0, "No engine assemblies found next to the test run.");
        Assert.Empty(offenders);
    }

    [Fact]
    public void Staged_WindowsBase_carries_DependencyObject_when_present()
    {
        var root = RepoRoot();
        var candidates = new[]
        {
            Path.Combine(root, "src", "DesktopWasm.BrowserHost", "obj", "Debug", "net9.0", "full-assembly", "WindowsBase.dll"),
            Path.Combine(root, "src", "DesktopWasm.BrowserHost", "obj", "Release", "net9.0", "full-assembly", "WindowsBase.dll"),
        };
        var staged = candidates.FirstOrDefault(File.Exists);
        if (staged is null) return; // Host never built here; the build gate covers this path.

        // Same invariant as the bundle gate: full-sized copy carrying the type
        // (namespaces and type names live in separate metadata heaps, so match
        // the short name exactly like scripts/BundleCheck.ps1 does).
        Assert.True(new FileInfo(staged).Length >= 100 * 1024, "Staged copy is facade-sized.");
        var text = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(staged));
        Assert.Contains("DependencyObject", text, StringComparison.Ordinal);
    }
}
