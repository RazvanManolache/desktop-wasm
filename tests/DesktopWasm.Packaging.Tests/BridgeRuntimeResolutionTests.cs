using DesktopWasm.Interop;
using Mono.Cecil;

namespace DesktopWasm.Packaging.Tests;

/// <summary>
/// End-to-end bridge chain on real TestApp bytes: packaged closure reference
/// (WindowsFormsIntegration scope) -&gt; runtime resolver -&gt; real bridge
/// provider -&gt; real fork control contract. The packaging half registers
/// the joint interface as runtime-provided (never a file gap); the Interop
/// half serves it at load. No facade/stub, no suppressed package/type/hash
/// gaps, WPF shim identity policy untouched.
/// </summary>
public sealed class BridgeRuntimeResolutionTests
{
    private static string StagedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DesktopWasm.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repository root (DesktopWasm.slnx) not found above the test binaries.");
        var staged = Path.Combine(dir.FullName, "inputs", "mixed", "avalondock");
        Assert.True(Directory.Exists(staged),
            $"Staged AvalonDock closure absent at '{staged}'.");
        return staged;
    }

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
    public void RealTestApp_joint_reference_resolves_to_real_bridge_and_fork_control()
    {
        var staged = StagedDirectory();

        // 1. Actual bytes: real TestApp references the joint interface scope.
        string scope;
        string fullName;
        using (var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(staged, "TestApp.exe")))
        {
            var hostRef = WalkTypeRefs(assembly.MainModule)
                .FirstOrDefault(r => r.Name == "WindowsFormsHost" && r.Scope is AssemblyNameReference);
            Assert.True(hostRef is not null, "Real TestApp no longer references the joint interface.");
            scope = ((AssemblyNameReference)hostRef.Scope).Name;
            fullName = hostRef.FullName;
        }
        Assert.Equal("WindowsFormsIntegration", scope);
        Assert.Equal("System.Windows.Forms.Integration.WindowsFormsHost", fullName);

        // 2. Packaging registers that scope as runtime-provided, never a file.
        Assert.Contains("WindowsFormsIntegration", JointMap.RuntimeProvidedJointInterfaces);

        // 3. Closure introduces no conflicts and no non-bridge gaps.
        var closure = JointClosure.Build(Path.Combine(staged, "TestApp.exe"), [staged], NetFrameworkProvided());
        Assert.DoesNotContain(closure.Gaps, g => g.Kind is JointGap.VersionConflict or JointGap.HashMismatch);
        Assert.All(closure.Gaps, g =>
            Assert.Contains("WindowsFormsIntegration", g.Detail, StringComparison.Ordinal));

        // 4. Runtime resolution: the joint reference loads the real bridge.
        var provider = WindowsFormsHostResolver.ResolveHostType(scope, fullName);
        Assert.NotNull(provider);
        Assert.Same(typeof(System.Windows.Forms.Integration.WindowsFormsHost), provider);

        // 5. The bridge hosts a real fork control, not the engine stub
        // (stub Child is IImmediatePaintable in DesktopWasm.Wpf).
        var child = provider.GetProperty("Child")!.PropertyType;
        Assert.Equal("System.Windows.Forms.Control", child.FullName);
        Assert.Equal("WfWasm.WinForms", child.Assembly.GetName().Name);
    }

    private static IEnumerable<TypeReference> WalkTypeRefs(ModuleDefinition module)
    {
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            foreach (var nested in type.NestedTypes) stack.Push(nested);
            foreach (var reference in Positions(type))
            {
                var unwrapped = Unwrap(reference);
                if (unwrapped is not null) yield return unwrapped;
            }
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
