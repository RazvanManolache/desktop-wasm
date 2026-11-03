using DesktopWasm.Convert;
using Mono.Cecil;
using WfWasm.Rewrite;

namespace DesktopWasm.Packaging.Tests;

/// <summary>
/// Composed conversion through the real adapter seams: WinForms fork pass
/// first, WPF pass second, validation on the composed graph. Covers the
/// AvalonDock routing (real controls to the real fork, WindowsFormsHost a
/// visible gap) at policy level — pristine AvalonDock bytes are not in this
/// root (importer blocker), so the byte-level rerun waits on mapped inputs.
/// </summary>
public sealed class SeamCompositionTests
{
    [Fact]
    public void MixedModule_ComposedPasses_RouteEachFrameworkHome()
    {
        using var assembly = FixtureBuilder.CreateMixedModule();
        var module = assembly.MainModule;
        var capabilities = JointPolicyAdapter.DetectCapabilities(module);
        Assert.Equal(FrameworkCapabilities.Mixed, capabilities);

        var forkScope = new AssemblyNameReference("WfWasm.WinForms", new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(forkScope);
        var forkCount = JointForkAdapter.RewriteToForkScopes(module, capabilities, forkScope);
        Assert.True(forkCount > 0);

        var form = module.Types.First(t => t.Name == "MixedForm");
        Assert.Equal("WfWasm.WinForms", ((AssemblyNameReference)form.BaseType.Scope).Name);
        // Real fork wins the shim overlap inside a mixed assembly.
        var screenField = module.Types.First(t => t.Name == "MixedWindow").Fields.First(f => f.Name == "_screen");
        Assert.Equal("WfWasm.WinForms", ((AssemblyNameReference)screenField.FieldType.Scope).Name);

        var engineScope = new AssemblyNameReference("DesktopWasm.Wpf", new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(engineScope);
        var wpfCount = JointPolicyAdapter.ApplyWpfScopes(module, capabilities, engineScope);
        Assert.True(wpfCount > 0);

        var window = module.Types.First(t => t.Name == "MixedWindow");
        Assert.Equal("DesktopWasm.Wpf", ((AssemblyNameReference)window.BaseType.Scope).Name);

        // Collision is reported, never rewritten by either pass.
        var gaps = JointPolicyAdapter.CollectJointGaps(module);
        var collision = Assert.Single(gaps);
        Assert.Equal(JointGap.TypeCollision, collision.Kind);
        var handler = form.Events.First(e => e.Name == "PrefChanged");
        Assert.Equal("Microsoft.Win32.SystemEvents", ((AssemblyNameReference)handler.EventType.Scope).Name);
    }

    [Fact]
    public void WpfBodyOnlyReference_IsRetargeted_NotLeftDangling()
    {
        // A WPF reference living ONLY in a method body (constructed and
        // shown locally, never stored in a field) must still route to the
        // engine: leaving the convertible scope behind ships a dangling
        // PresentationFramework reference in converted bytes.
        var dir = FixtureBuilder.TempDir("wpfbody");
        var path = FixtureBuilder.WriteAssembly(dir, "BodyApp.dll", "1.0.0.0",
            [FixtureBuilder.Ref("System.Windows.Forms"), FixtureBuilder.Ref("PresentationFramework")],
            module =>
            {
                var window = FixtureBuilder.TypeRef(module, "PresentationFramework", "System.Windows", "Window");
                var type = new Mono.Cecil.TypeDefinition("Fixtures", "BodyOnly",
                    Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
                module.Types.Add(type);
                var show = new Mono.Cecil.MethodDefinition("Show",
                    Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
                type.Methods.Add(show);
                var il = show.Body.GetILProcessor();
                var ctor = new Mono.Cecil.MethodReference(".ctor", module.TypeSystem.Void, window) { HasThis = true };
                il.Emit(Mono.Cecil.Cil.OpCodes.Newobj, ctor);
                il.Emit(Mono.Cecil.Cil.OpCodes.Pop);
                il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            });

        using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
        var module = assembly.MainModule;
        Assert.Equal(FrameworkCapabilities.Mixed, JointPolicyAdapter.DetectCapabilities(module));
        var engineScope = new Mono.Cecil.AssemblyNameReference("DesktopWasm.Wpf", new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(engineScope);

        var count = JointPolicyAdapter.ApplyWpfScopes(module, JointCapabilities.Detect(module), engineScope);
        Assert.True(count > 0);

        var scopes = new List<string>();
        foreach (var type in module.Types)
            foreach (var method in type.Methods)
            {
                if (method.Body is null) continue;
                foreach (var instruction in method.Body.Instructions)
                    if (instruction.Operand is Mono.Cecil.MemberReference member &&
                        member.DeclaringType.Scope is Mono.Cecil.AssemblyNameReference scope)
                        scopes.Add(scope.Name);
            }
        Assert.NotEmpty(scopes);
        Assert.All(scopes, s => Assert.Equal("DesktopWasm.Wpf", s));
    }

    [Fact]
    public void ComposedPasses_AreIdempotent()
    {
        using var assembly = FixtureBuilder.CreateMixedModule();
        var module = assembly.MainModule;
        var capabilities = JointCapabilities.Detect(module);
        var forkScope = new AssemblyNameReference("WfWasm.WinForms", new Version(1, 0, 0, 0));
        var engineScope = new AssemblyNameReference("DesktopWasm.Wpf", new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(forkScope);
        module.AssemblyReferences.Add(engineScope);

        Assert.True(JointForkAdapter.RewriteToForkScopes(module, capabilities, forkScope) > 0);
        Assert.True(JointPolicyAdapter.ApplyWpfScopes(module, capabilities, engineScope) > 0);
        Assert.Equal(0, JointForkAdapter.RewriteToForkScopes(module, capabilities, forkScope));
        Assert.Equal(0, JointPolicyAdapter.ApplyWpfScopes(module, capabilities, engineScope));
    }

    [Fact]
    public void PureWpfModule_ForkPassLeavesShims_ForWpfPass()
    {
        using var assembly = FixtureBuilder.CreateMixedModule();
        var module = assembly.MainModule;
        // Pure-WPF view of the same bytes: shim names must survive the fork pass.
        var capabilities = FrameworkCapabilities.Wpf;
        var forkScope = new AssemblyNameReference("WfWasm.WinForms", new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(forkScope);

        var screenField = module.Types.First(t => t.Name == "MixedWindow").Fields.First(f => f.Name == "_screen");
        Assert.False(JointForkAdapter.ShouldRewriteToFork("System.Windows.Forms", "System.Windows.Forms.Screen", capabilities));

        var engineScope = new AssemblyNameReference("DesktopWasm.Wpf", new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(engineScope);
        Assert.True(JointPolicyAdapter.ApplyWpfScopes(module, capabilities, engineScope) > 0);
        Assert.Equal("DesktopWasm.Wpf", ((AssemblyNameReference)screenField.FieldType.Scope).Name);
    }

    [Fact]
    public void ComposedSlice_WritesStableConvertedBytes()
    {
        var dir = FixtureBuilder.TempDir("composed");
        string inputPath, firstPath, secondPath;
        using (var assembly = FixtureBuilder.CreateMixedModule())
        {
            inputPath = Path.Combine(dir, "MixedApp.dll");
            assembly.Write(inputPath);
        }
        var inputHash = JointClosure.HashOf(inputPath);

        (string Hash, int Fork, int Wpf) PassThrough(string source, string dest)
        {
            using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(source);
            var module = assembly.MainModule;
            var capabilities = JointPolicyAdapter.DetectCapabilities(module);
            Assert.Equal(FrameworkCapabilities.Mixed, capabilities);
            var order = JointPipeline.OrderedStages;
            Assert.Null(JointPipeline.CheckOrder(order));
            var forkScope = new Mono.Cecil.AssemblyNameReference("WfWasm.WinForms", new Version(1, 0, 0, 0));
            var engineScope = new Mono.Cecil.AssemblyNameReference("DesktopWasm.Wpf", new Version(1, 0, 0, 0));
            module.AssemblyReferences.Add(forkScope);
            module.AssemblyReferences.Add(engineScope);
            var fork = JointForkAdapter.RewriteToForkScopes(module, capabilities, forkScope);
            var wpf = JointPolicyAdapter.ApplyWpfScopes(module, capabilities, engineScope);
            assembly.Write(dest);
            return (JointClosure.HashOf(dest), fork, wpf);
        }

        firstPath = Path.Combine(dir, "MixedApp.joint1.dll");
        secondPath = Path.Combine(dir, "MixedApp.joint2.dll");
        var first = PassThrough(inputPath, firstPath);
        Assert.NotEqual(inputHash, first.Hash);
        Assert.True(first.Fork > 0 && first.Wpf > 0);

        using (var converted = Mono.Cecil.AssemblyDefinition.ReadAssembly(firstPath))
        {
            var module = converted.MainModule;
            var form = module.Types.First(t => t.Name == "MixedForm");
            Assert.Equal("WfWasm.WinForms", ((Mono.Cecil.AssemblyNameReference)form.BaseType.Scope).Name);
            var window = module.Types.First(t => t.Name == "MixedWindow");
            Assert.Equal("DesktopWasm.Wpf", ((Mono.Cecil.AssemblyNameReference)window.BaseType.Scope).Name);
            Assert.Single(JointPolicyAdapter.CollectJointGaps(module));
        }

        // Idempotency on real bytes: re-running the composed slice over the
        // converted output retargets nothing and leaves routing stable.
        var second = PassThrough(firstPath, secondPath);
        Assert.Equal(0, second.Fork);
        Assert.Equal(0, second.Wpf);
        using (var rerun = Mono.Cecil.AssemblyDefinition.ReadAssembly(secondPath))
        {
            var module = rerun.MainModule;
            Assert.Equal("WfWasm.WinForms",
                ((Mono.Cecil.AssemblyNameReference)module.Types.First(t => t.Name == "MixedForm").BaseType.Scope).Name);
            Assert.Equal("DesktopWasm.Wpf",
                ((Mono.Cecil.AssemblyNameReference)module.Types.First(t => t.Name == "MixedWindow").BaseType.Scope).Name);
        }
    }

    [Theory]
    [InlineData("System.Windows.Forms", "System.Windows.Forms", "ContainerControl")]
    [InlineData("System.Windows.Forms", "System.Windows.Forms", "Padding")]
    [InlineData("System.Windows.Forms", "System.Windows.Forms", "AutoScaleMode")]
    [InlineData("System.Windows.Forms", "System.Windows.Forms", "Control")]
    public void AvalonDock_WinFormsGaps_RouteToRealFork(string sourceAssembly, string ns, string name)
    {
        // Recorded AvalonDock app-manifest gaps, replayed against joint policy.
        var route = JointPrecedence.ResolveType(sourceAssembly, ns, name, FrameworkCapabilities.Mixed);

        Assert.Equal(JointTargetKind.WinFormsEngine, route.Kind);
        Assert.Equal("WfWasm.WinForms", route.TargetAssembly);
    }

    [Fact]
    public void AvalonDock_WindowsFormsHost_StaysVisibleGap_UntilRealHostBridge()
    {
        // WindowsFormsIntegration is owned by neither map: no stub, no silent
        // shim win. Closes only with the interop lane's real-host bridge.
        var route = JointPrecedence.ResolveType(
            "WindowsFormsIntegration", "System.Windows.Forms.Integration", "WindowsFormsHost",
            FrameworkCapabilities.Mixed);

        Assert.Equal(JointTargetKind.Gap, route.Kind);
    }
}
