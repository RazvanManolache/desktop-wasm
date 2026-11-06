using System;
using System.IO;
using WfWasm.Rewrite;
using Xunit;

// TMPMOVE rootfix: fixed five-parent climbs resolve the test-bin depth but
// escape to E:/AI/Code from a staged host depth
// (.artifacts/demo-deployments/<gen>/WfWasm.Host.Server needs four parents,
// not five). These pin the shared marker-walk resolver on both real nesting
// shapes, the explicit override, and the fail-closed errors. Fixture trees
// are owned tiny marker trees under the lane run root and removed on dispose.
public sealed class TmpWorkspacePathsTests : IDisposable
{
    // Test-bin layout only: five parents reach the repo from the test binary.
    // Used solely to place owned fixture trees, never as an expectation.
    private static string TestBinRepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string OldFiveParent(string baseDir) =>
        Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));

    private readonly string _jobRoot = Path.Combine(TestBinRepoRoot(), ".artifacts", "tmp", "runs", "dw-wpf", "rootfix");
    private readonly string _wsA;
    private readonly string _wsB;

    public TmpWorkspacePathsTests()
    {
        _wsA = Path.Combine(_jobRoot, "wsA");
        _wsB = Path.Combine(_jobRoot, "wsB");
        // Layout A: test-bin nesting depth (5 nested dirs).
        Directory.CreateDirectory(Path.Combine(_wsA, "t1", "t2", "t3", "t4", "t5"));
        Directory.CreateDirectory(Path.Combine(_wsA, "catalog"));
        File.WriteAllText(Path.Combine(_wsA, "catalog", "apps.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_wsA, "src"));
        // Layout B: published-generation staging depth.
        Directory.CreateDirectory(Path.Combine(_wsB, ".artifacts", "demo-deployments", "gen99", "WfWasm.Host.Server"));
        Directory.CreateDirectory(Path.Combine(_wsB, "catalog"));
        File.WriteAllText(Path.Combine(_wsB, "catalog", "apps.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_wsB, "scripts"));
        Directory.CreateDirectory(Path.Combine(_jobRoot, "empty"));
        Directory.CreateDirectory(Path.Combine(_jobRoot, "nocatalog", "src"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_jobRoot, recursive: true); }
        catch (IOException) { /* best effort; reruns recreate idempotently */ }
    }

    [Fact]
    public void TestBinDepth_ResolvesRealRepoRoot()
    {
        string root = WorkspacePaths.FindWorkspaceRoot(baseDirectory: AppContext.BaseDirectory);
        Assert.Equal(TestBinRepoRoot(), root);
        Assert.True(File.Exists(Path.Combine(root, "catalog", "apps.json")));
    }

    [Fact]
    public void DeployedDepth_ResolvesFakeRoot_WhereFiveParentEscapes()
    {
        string staged = Path.Combine(_wsB, ".artifacts", "demo-deployments", "gen99", "WfWasm.Host.Server");
        Assert.Equal(_wsB, WorkspacePaths.FindWorkspaceRoot(baseDirectory: staged, currentDirectory: staged));
        // The old fixed-depth formula escapes one level above the fake root here.
        Assert.NotEqual(_wsB, OldFiveParent(staged));
    }

    [Fact]
    public void ExplicitOverride_WinsOverDeepBase()
    {
        string deepA = Path.Combine(_wsA, "t1", "t2", "t3", "t4", "t5");
        Assert.Equal(_wsB, WorkspacePaths.FindWorkspaceRoot(explicitRoot: _wsB, baseDirectory: deepA));
    }

    [Fact]
    public void ExplicitOverride_InvalidRoot_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            WorkspacePaths.FindWorkspaceRoot(explicitRoot: Path.Combine(_jobRoot, "empty")));
        Assert.Contains("not a workspace root", ex.Message);
    }

    [Fact]
    public void ExplicitOverride_MissingPath_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            WorkspacePaths.FindWorkspaceRoot(explicitRoot: Path.Combine(_jobRoot, "nope", "deep")));
    }

    [Fact]
    public void NoMarkersAnywhere_Throws()
    {
        // E:\ carries no workspace markers, so the ancestor walk exhausts.
        string nowhere = @"E:\__dw_rootfix_nope__\deep\nest";
        var ex = Assert.Throws<InvalidOperationException>(() =>
            WorkspacePaths.FindWorkspaceRoot(baseDirectory: nowhere, currentDirectory: nowhere));
        Assert.Contains("No workspace root", ex.Message);
    }

    [Fact]
    public void DefaultNoArg_ResolvesRealRepoRoot()
    {
        string root = WorkspacePaths.FindWorkspaceRoot();
        Assert.EndsWith("DesktopWasm", root);
        Assert.True(File.Exists(Path.Combine(root, "catalog", "apps.json")));
    }

    [Fact]
    public void EnvOverride_WinsAndRestores()
    {
        string? prior = Environment.GetEnvironmentVariable(WorkspacePaths.WorkspaceRootEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(WorkspacePaths.WorkspaceRootEnvVar, _wsB);
            string deepA = Path.Combine(_wsA, "t1", "t2", "t3", "t4", "t5");
            Assert.Equal(_wsB, WorkspacePaths.FindWorkspaceRoot(baseDirectory: deepA));
        }
        finally
        {
            Environment.SetEnvironmentVariable(WorkspacePaths.WorkspaceRootEnvVar, prior);
        }
    }

    [Fact]
    public void IsWorkspaceRoot_MarkerRule()
    {
        Assert.True(WorkspacePaths.IsWorkspaceRoot(_wsA));
        Assert.True(WorkspacePaths.IsWorkspaceRoot(_wsB));
        Assert.False(WorkspacePaths.IsWorkspaceRoot(Path.Combine(_jobRoot, "empty")));
        Assert.False(WorkspacePaths.IsWorkspaceRoot(Path.Combine(_jobRoot, "nocatalog")));
    }
}
