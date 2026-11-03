using System.Text.Json;
using DesktopWasm.Convert;
using Mono.Cecil;
using WfWasm.Rewrite;

namespace DesktopWasm.Packaging.Tests;

/// <summary>
/// Real joint-app validation over the pristine AvalonDock TestApp closure
/// staged at inputs/mixed/avalondock (see provenance.json there): a genuine
/// WPF-containing-WinForms application. TestApp.MainWindow derives from
/// System.Windows.Window, embeds real System.Windows.Forms controls, and
/// hosts one through System.Windows.Forms.Integration.WindowsFormsHost.
/// No synthetic fixture counts as this conversion.
/// </summary>
public sealed class AvalonDockClosureTests
{
    private static string StagedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DesktopWasm.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repository root (DesktopWasm.slnx) not found above the test binaries.");
        var staged = Path.Combine(dir.FullName, "inputs", "mixed", "avalondock");
        Assert.True(Directory.Exists(staged),
            $"Staged AvalonDock closure absent at '{staged}': stage the pristine TestApp closure there first (see provenance.json).");
        return staged;
    }

    /// <summary>
    /// net48 framework names the DesktopWasm runtime already provides (or
    /// that the joint maps rewrite): never packaged from disk, never
    /// demanded as files. Only private closure members can produce gaps.
    /// </summary>
    private static IReadOnlySet<string> NetFrameworkProvided()
    {
        var provided = new HashSet<string>(
            JointClosure.ProvidedAssemblies(Path.GetDirectoryName(typeof(object).Assembly.Location)!),
            StringComparer.OrdinalIgnoreCase);
        provided.UnionWith([
            "mscorlib", "netstandard",
            "System", "System.Core", "System.Xml", "System.Xml.Linq",
            "System.Numerics", "System.ValueTuple",
        ]);
        return provided;
    }

    [Fact]
    public void RealClosure_MixedEntry_NoConflicts_ZeroGaps_BridgeProvided()
    {
        var staged = StagedDirectory();
        var closure = JointClosure.Build(Path.Combine(staged, "TestApp.exe"), [staged], NetFrameworkProvided());

        var entry = closure.Nodes.FirstOrDefault(n => n.Name == "TestApp");
        Assert.True(entry is not null,
            "Closure has no TestApp node: " + string.Join(", ", closure.Nodes.Select(n => n.Name)));
        Assert.Equal(FrameworkCapabilities.Mixed, entry.Capabilities);
        Assert.Contains(closure.Nodes, n => n.Name == "AvalonDock");
        Assert.Contains(closure.Nodes, n => n.Name == "AvalonDock.Core");

        // The joint interface is runtime-provided by the real interop bridge
        // (JointMap.RuntimeProvidedJointInterfaces, GAC-only on net48 and
        // verified absent under the frozen WPF-wasm root): zero gaps and
        // zero version/hash conflicts.
        Assert.DoesNotContain(closure.Gaps, g => g.Kind is JointGap.VersionConflict or JointGap.HashMismatch);
        Assert.Empty(closure.Gaps);
        Assert.True(closure.Ok);

        // Resolver/packager composition on real TestApp bytes — not a
        // fabricated zero from the registration alone: the converted app
        // keeps referencing the joint interface untouched, and that exact
        // reference resolves to the real bridge provider.
        var work = FixtureBuilder.TempDir("avalondock-bridge-resolve");
        var copy = Path.Combine(work, "TestApp.exe");
        File.Copy(Path.Combine(staged, "TestApp.exe"), copy);
        using (var assembly = AssemblyDefinition.ReadAssembly(copy))
        {
            var module = assembly.MainModule;
            var windowsFormsHost = FindTypeRef(module, "WindowsFormsHost");
            Assert.True(windowsFormsHost is not null,
                "Real TestApp no longer references the joint interface.");
            Assert.Equal("WindowsFormsIntegration", ScopeOf(windowsFormsHost));
            Assert.Equal("System.Windows.Forms.Integration.WindowsFormsHost", windowsFormsHost.FullName);
        }
        var provider = typeof(System.Windows.Forms.Integration.WindowsFormsHost);
        Assert.True(typeof(DesktopWasm.Wpf.FrameworkElement).IsAssignableFrom(provider));
        Assert.Equal("System.Windows.Forms.Control",
            provider.GetProperty("Child")!.PropertyType.FullName);
    }

    [Fact]
    public void RealConversion_ComposedPasses_RouteBothFrameworks_WindowsFormsHostUntouched()
    {
        var staged = StagedDirectory();
        var work = FixtureBuilder.TempDir("avalondock-joint");
        var copy = Path.Combine(work, "TestApp.exe");
        File.Copy(Path.Combine(staged, "TestApp.exe"), copy);

        string convertedPath;
        int fork, wpf;
        using (var assembly = AssemblyDefinition.ReadAssembly(copy))
        {
            var module = assembly.MainModule;
            Assert.Equal(FrameworkCapabilities.Mixed, JointPolicyAdapter.DetectCapabilities(module));
            Assert.Null(JointPipeline.CheckOrder(JointPipeline.OrderedStages));

            var forkScope = new AssemblyNameReference("WfWasm.WinForms", new Version(1, 0, 0, 0));
            var engineScope = new AssemblyNameReference("DesktopWasm.Wpf", new Version(1, 0, 0, 0));
            module.AssemblyReferences.Add(forkScope);
            module.AssemblyReferences.Add(engineScope);
            fork = JointForkAdapter.RewriteToForkScopes(module, FrameworkCapabilities.Mixed, forkScope);
            wpf = JointPolicyAdapter.ApplyWpfScopes(module, FrameworkCapabilities.Mixed, engineScope);
            Assert.True(fork > 0, "WinForms fork pass retargeted nothing on real mixed bytes.");
            Assert.True(wpf > 0, "WPF pass retargeted nothing on real mixed bytes.");

            // Real WinForms controls ride the real fork ...
            Assert.Equal("WfWasm.WinForms", ScopeOf(FindTypeRef(module, "Label")));
            // ... WPF visuals ride the WPF engine ...
            Assert.Equal("DesktopWasm.Wpf",
                ScopeOf(module.Types.First(t => t.Name == "MainWindow").BaseType));
            // ... and the joint interface is never rebound by either pass.
            Assert.Equal("WindowsFormsIntegration", ScopeOf(FindTypeRef(module, "WindowsFormsHost")));

            convertedPath = Path.Combine(work, "TestApp.joint.dll");
            assembly.Write(convertedPath);
        }
        Assert.NotEqual(JointClosure.HashOf(copy), JointClosure.HashOf(convertedPath));

        // Idempotency on real converted bytes: re-running the composed slice
        // retargets nothing and leaves routing stable.
        using (var rerun = AssemblyDefinition.ReadAssembly(convertedPath))
        {
            var module = rerun.MainModule;
            var forkScope = module.AssemblyReferences.First(r => r.Name == "WfWasm.WinForms");
            var engineScope = module.AssemblyReferences.First(r => r.Name == "DesktopWasm.Wpf");
            var capabilities = JointCapabilities.Detect(module);
            Assert.Equal(0, JointForkAdapter.RewriteToForkScopes(module, capabilities, (AssemblyNameReference)forkScope));
            Assert.Equal(0, JointPolicyAdapter.ApplyWpfScopes(module, capabilities, (AssemblyNameReference)engineScope));
            Assert.Equal("WfWasm.WinForms", ScopeOf(FindTypeRef(module, "Label")));
            Assert.Equal("DesktopWasm.Wpf",
                ScopeOf(module.Types.First(t => t.Name == "MainWindow").BaseType));
            Assert.Equal("WindowsFormsIntegration", ScopeOf(FindTypeRef(module, "WindowsFormsHost")));
        }
    }

    [Fact]
    public void StagedPayload_MatchesProvenance_And_VerifiesAgainstClosure()
    {
        var staged = StagedDirectory();
        using (var provenance = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(staged, "provenance.json"))))
        {
            foreach (var file in provenance.RootElement.GetProperty("files").EnumerateArray())
            {
                var name = file.GetProperty("name").GetString()!;
                var expected = file.GetProperty("sha256").GetString()!;
                Assert.True(expected.Equals(JointClosure.HashOf(Path.Combine(staged, name)),
                    StringComparison.OrdinalIgnoreCase),
                    $"Staged '{name}' no longer matches its provenance hash.");
            }
        }

        var closure = JointClosure.Build(Path.Combine(staged, "TestApp.exe"), [staged], NetFrameworkProvided());

        // Stage exactly the closure nodes (never the provenance sidecar),
        // then validate assembly identity: same name, version and bytes.
        var stage = FixtureBuilder.TempDir("avalondock-stage");
        foreach (var node in closure.Nodes)
        {
            var source = Directory.GetFiles(staged)
                .First(f => Path.GetFileNameWithoutExtension(f).Equals(node.Name, StringComparison.OrdinalIgnoreCase));
            File.Copy(source, Path.Combine(stage, Path.GetFileName(source)));
        }
        Assert.Empty(JointClosure.VerifyPayload(closure, stage));
    }

    private static TypeReference? FindTypeRef(ModuleDefinition module, string name)
    {
        foreach (var type in WalkTypes(module))
        {
            foreach (var reference in Positions(type))
            {
                var unwrapped = Unwrap(reference);
                if (unwrapped is not null && unwrapped.Name == name &&
                    unwrapped.Scope is AssemblyNameReference)
                    return unwrapped;
            }
        }
        return null;
    }

    private static string? ScopeOf(TypeReference? reference)
    {
        var unwrapped = Unwrap(reference);
        return (unwrapped?.Scope as AssemblyNameReference)?.Name;
    }

    private static IEnumerable<TypeDefinition> WalkTypes(ModuleDefinition module)
    {
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            yield return type;
            foreach (var nested in type.NestedTypes) stack.Push(nested);
        }
    }

    private static IEnumerable<TypeReference?> Positions(TypeDefinition type)
    {
        yield return type.BaseType;
        foreach (var iface in type.Interfaces) yield return iface.InterfaceType;
        foreach (var field in type.Fields) yield return field.FieldType;
        foreach (var method in type.Methods)
        {
            yield return method.ReturnType;
            foreach (var parameter in method.Parameters) yield return parameter.ParameterType;
        }
        foreach (var property in type.Properties) yield return property.PropertyType;
        foreach (var evt in type.Events) yield return evt.EventType;
    }

    private static TypeReference? Unwrap(TypeReference? reference)
    {
        while (reference is TypeSpecification specification)
            reference = specification.ElementType;
        return reference;
    }
}
