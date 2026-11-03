namespace DesktopWasm.Packaging.Tests;

public sealed class ClosureTests
{
    private static readonly IReadOnlySet<string> Provided =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "System.Runtime", "System.Private.CoreLib" };

    [Fact]
    public void WinFormsOnly_Closure_Ok_WithWinFormsCapabilities()
    {
        var dir = FixtureBuilder.TempDir("wfclosure");
        var entry = FixtureBuilder.WriteAssembly(dir, "WfApp.dll", "1.0.0.0",
            [FixtureBuilder.Ref("System.Runtime"), FixtureBuilder.Ref("System.Windows.Forms")],
            FixtureBuilder.DefineWinFormsControl);

        var closure = JointClosure.Build(entry, [], Provided);

        Assert.True(closure.Ok);
        var node = Assert.Single(closure.Nodes);
        Assert.Equal(FrameworkCapabilities.WinForms, node.Capabilities);
        Assert.Equal(64, node.Sha256.Length);
    }

    [Fact]
    public void WpfOnly_Closure_Ok_WithWpfCapabilities()
    {
        var dir = FixtureBuilder.TempDir("wpfclosure");
        var entry = FixtureBuilder.WriteAssembly(dir, "WpfApp.dll", "1.0.0.0",
            [FixtureBuilder.Ref("System.Runtime"), FixtureBuilder.Ref("PresentationFramework")],
            module =>
            {
                var window = FixtureBuilder.TypeRef(module, "PresentationFramework", "System.Windows", "Window");
                module.Types.Add(new Mono.Cecil.TypeDefinition(
                    "Fixtures", "PureWindow", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, window));
            });

        var closure = JointClosure.Build(entry, [], Provided);

        Assert.True(closure.Ok);
        var node = Assert.Single(closure.Nodes);
        Assert.Equal(FrameworkCapabilities.Wpf, node.Capabilities);
    }

    [Fact]
    public void MissingPrivateDependency_IsVisibleGap()
    {
        var dir = FixtureBuilder.TempDir("missing");
        var entry = FixtureBuilder.WriteAssembly(dir, "App.dll", "1.0.0.0",
            [FixtureBuilder.Ref("System.Runtime"), FixtureBuilder.Ref("Contoso.Private", "2.0.0.0")]);

        var closure = JointClosure.Build(entry, [], Provided);

        Assert.False(closure.Ok);
        var gap = Assert.Single(closure.Gaps);
        Assert.Equal(JointGap.MissingDependency, gap.Kind);
        Assert.Contains("Contoso.Private", gap.Detail);
        Assert.Contains("mapped/pinned input", gap.Detail);
    }

    [Fact]
    public void SameName_DifferentVersions_IsVersionConflict()
    {
        var root = FixtureBuilder.TempDir("verconflict");
        var dirA = Path.Combine(root, "a");
        var dirB = Path.Combine(root, "b");
        FixtureBuilder.WriteAssembly(dirA, "LibA.dll", "1.0.0.0",
            [FixtureBuilder.Ref("Dep", "1.0.0.0")]);
        FixtureBuilder.WriteAssembly(dirB, "LibB.dll", "1.0.0.0",
            [FixtureBuilder.Ref("Dep", "2.0.0.0")]);
        FixtureBuilder.WriteAssembly(dirA, "Dep.dll", "1.0.0.0", []);
        FixtureBuilder.WriteAssembly(dirB, "Dep.dll", "2.0.0.0", []);
        var entry = FixtureBuilder.WriteAssembly(root, "Entry.dll", "1.0.0.0",
            [FixtureBuilder.Ref("LibA"), FixtureBuilder.Ref("LibB")]);
        // LibA/LibB themselves arrive through the shared search roots.
        var closure = JointClosure.Build(entry, [dirA, dirB], Provided);

        Assert.False(closure.Ok);
        Assert.Contains(closure.Gaps, g => g.Kind == JointGap.VersionConflict && g.Assembly == "Dep");
    }

    [Fact]
    public void SameNameVersion_DifferentBytes_IsHashMismatch()
    {
        var root = FixtureBuilder.TempDir("hashconflict");
        var dirA = Path.Combine(root, "a");
        var dirB = Path.Combine(root, "b");
        FixtureBuilder.WriteAssembly(dirA, "LibA.dll", "1.0.0.0",
            [FixtureBuilder.Ref("Dep", "1.0.0.0")]);
        FixtureBuilder.WriteAssembly(dirB, "LibB.dll", "1.0.0.0",
            [FixtureBuilder.Ref("Dep", "1.0.0.0")]);
        FixtureBuilder.WriteAssembly(dirA, "Dep.dll", "1.0.0.0", []);
        FixtureBuilder.WriteAssembly(dirB, "Dep.dll", "1.0.0.0", [],
            module => module.Types.Add(new Mono.Cecil.TypeDefinition(
                "X", "Extra", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class)));
        var entry = FixtureBuilder.WriteAssembly(root, "Entry.dll", "1.0.0.0",
            [FixtureBuilder.Ref("LibA"), FixtureBuilder.Ref("LibB")]);
        var closure = JointClosure.Build(entry, [dirA, dirB], Provided);

        Assert.False(closure.Ok);
        Assert.Contains(closure.Gaps, g => g.Kind == JointGap.HashMismatch && g.Assembly == "Dep");
    }

    [Fact]
    public void MixedEntry_Closure_Ok_WithMixedCapabilities_AndSharedIdentity()
    {
        // One original assembly referencing BOTH stacks: the closure must
        // carry it as a single node with canonical (name, version, content
        // hash) identity and Mixed capabilities, never split or dropped.
        var dir = FixtureBuilder.TempDir("mixedclosure");
        var entry = FixtureBuilder.WriteAssembly(dir, "MixedApp.dll", "1.0.0.0",
            [FixtureBuilder.Ref("System.Runtime"), FixtureBuilder.Ref("System.Windows.Forms"), FixtureBuilder.Ref("PresentationFramework"), FixtureBuilder.Ref("WindowsBase")],
            module =>
            {
                FixtureBuilder.DefineWinFormsControl(module);
                FixtureBuilder.DefineWpfVisual(module);
            });

        var closure = JointClosure.Build(entry, [], Provided);

        Assert.True(closure.Ok);
        var node = Assert.Single(closure.Nodes);
        Assert.Equal("MixedApp", node.Name);
        Assert.Equal("1.0.0.0", node.Version);
        Assert.Equal(64, node.Sha256.Length);
        Assert.Equal(JointClosure.HashOf(entry), node.Sha256);
        Assert.Equal(FrameworkCapabilities.Mixed, node.Capabilities);

        // The staged mixed payload verifies against the closure identity:
        // same name, same bytes, one payload per package.
        var stage = Path.Combine(dir, "stage");
        Directory.CreateDirectory(stage);
        File.Copy(entry, Path.Combine(stage, "MixedApp.dll"));
        Assert.Empty(JointClosure.VerifyPayload(closure, stage));
    }

    [Fact]
    public void VerifyPayload_CatchesTamperedStage()
    {
        var dir = FixtureBuilder.TempDir("stage");
        var entry = FixtureBuilder.WriteAssembly(dir, "WfApp.dll", "1.0.0.0",
            [FixtureBuilder.Ref("System.Runtime"), FixtureBuilder.Ref("System.Windows.Forms")],
            FixtureBuilder.DefineWinFormsControl);
        var closure = JointClosure.Build(entry, [], Provided);
        Assert.True(closure.Ok);

        var stage = Path.Combine(dir, "stage");
        Directory.CreateDirectory(stage);
        File.Copy(entry, Path.Combine(stage, "WfApp.dll"));
        Assert.Empty(JointClosure.VerifyPayload(closure, stage));

        File.AppendAllText(Path.Combine(stage, "WfApp.dll"), "tamper");
        var gaps = JointClosure.VerifyPayload(closure, stage);
        Assert.Contains(gaps, g => g.Kind == JointGap.HashMismatch && g.Assembly == "WfApp");
    }

    [Fact]
    public void ManifestValidation_StillApplies_ViaCoreContract()
    {
        // Reuse, not replacement: the joint manifest obeys the existing
        // ApplicationPackage validation (hash format, converted/gaps rule).
        var package = new DesktopWasm.Core.ApplicationPackage(
            1, "mixed-fixture", DesktopWasm.Wpf.WpfAppBoot.MapVersion,
            "converted", "Entry",
            [new DesktopWasm.Core.PackageAssembly("Entry", "Entry." + new string('a', 64) + ".bin", new string('a', 64), [], new string('b', 64))],
            [],
            null);
        package.Validate("mixed-fixture", DesktopWasm.Wpf.WpfAppBoot.MapVersion);
    }
}
