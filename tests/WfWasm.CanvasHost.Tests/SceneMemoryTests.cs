using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Shim;

public sealed class SceneMemoryTests
{
    // TMPMOVE canonical-only: rewritten fixtures under retained/.
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static string RewrittenSmall
    {
        get
        {
            var canon = Path.Combine(RepoRoot, ".artifacts", "tmp", "retained", "rewritten-small");
            return canon;
        }
    }
    private static void LoadApp(string file, string simpleName)
    {
        // Same regenerable-data coupling as the other host tests: the
        // rewritten app must exist in the retained tree; bridge the assembly name
        // like the browser loader does by file.
        string app = Path.Combine(RewrittenSmall, file);
        Assert.True(File.Exists(app), $"missing rewritten fixture (expected at retained/rewritten-small): {app}");
        var asm = Assembly.LoadFrom(app);
        ResolveEventHandler? resolver = (_, args) =>
            args.Name.Split(',')[0].Trim() == simpleName ? asm : null;
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
    }

    [Fact]
    public void Selecting_New_Scene_Disposes_Previous_Form()
    {
        // Only the selected app stays alive: switching must dispose the
        // old form so control trees cannot accumulate in memory.
        LoadApp("Calculator.dll", "Calculator");
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var smoke = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(smoke);
        Assert.False(smoke.IsDisposed);

        host.SelectScene(AppScene.Calculator);

        Assert.True(smoke.IsDisposed);
        var calculator = host.GetOrCreateForm(AppScene.Calculator);
        Assert.NotNull(calculator);
        Assert.False(calculator.IsDisposed);
    }

    [Fact]
    public void Reselecting_Same_Scene_Keeps_Form_Alive()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var first = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(first);

        host.SelectScene(AppScene.Smoke);

        Assert.False(first.IsDisposed);
        Assert.Same(first, host.GetOrCreateForm(AppScene.Smoke));
    }
}
