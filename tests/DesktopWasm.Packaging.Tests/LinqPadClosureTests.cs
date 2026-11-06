using DesktopWasm.Packaging;

namespace DesktopWasm.Packaging.Tests;

/// <summary>
/// LINQPad5 staged-closure completeness (FACTORY1615-IMPORT, dw-import).
/// The RDP-style preparation gap: the staged set shipped only the app
/// folder, but LINQPad 5 is net4x and its MainForm pulls 38 framework
/// assemblies (chart, MEF, WF/WCF stacks). The preparation now stages
/// them from the local GAC into dll/; this gate pins the closure whole:
/// zero missing-dependency gaps, entry present and WinForms-capable.
/// </summary>
public sealed class LinqPadClosureTests
{
    private static string StagedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DesktopWasm.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Repository root (DesktopWasm.slnx) not found above the test binaries.");
        var staged = Path.Combine(dir.FullName, ".artifacts", "benchmark-apps", "benchmark-linqpad5");
        Assert.True(Directory.Exists(staged),
            $"Staged LINQPad package absent at '{staged}': stage the RDP-excluded LINQPad closure there first (see preparation.json).");
        return staged;
    }

    [Fact]
    public void RealClosure_WinFormsEntry_ZeroMissingDependencies()
    {
        var staged = StagedDirectory();
        var dll = Path.Combine(staged, "dll");
        var closure = JointClosure.Build(
            Path.Combine(staged, "LINQPad.exe"),
            [staged, dll],
            JointTestAppPackager.NetFrameworkProvided());

        Assert.True(closure.Ok, "LINQPad closure must resolve: " +
            string.Join(" | ", closure.Gaps.Select(g => $"{g.Assembly} [{g.Kind}]: {g.Detail}")));
        Assert.Empty(closure.Gaps);
        var entry = closure.Nodes.FirstOrDefault(n => n.Name == "LINQPad");
        Assert.True(entry is not null, "Closure has no LINQPad entry node.");
        Assert.True(entry.Capabilities.HasFlag(FrameworkCapabilities.WinForms),
            $"LINQPad entry is '{entry.Capabilities}', not WinForms.");
        Assert.Contains(closure.Nodes, n => n.Name == "System.Windows.Forms.DataVisualization");
    }
}
