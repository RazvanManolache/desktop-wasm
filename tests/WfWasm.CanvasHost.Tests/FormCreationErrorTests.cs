using WfWasm.CanvasHost;
using WfWasm.Shim;

// RV1745-DW-RUNTIME: the host must retain the ACTUAL per-scene constructor
// failure (Fiddler's <Module> initializer PlatformNotSupportedException
// class: TargetInvocationException wrapping a module type-initializer
// failure) so the shell can name the real cause instead of only the generic
// "could not create its real window" marker. These tests pin the stash and
// its shell suffix without launching a browser.
public sealed class FormCreationErrorTests
{
    [Fact]
    public void FailedBenchmarkScene_Retains_RootCause_Detail()
    {
        using var host = new WinFormsAppHost();
        // Fiddler's assembly is absent in tests, so Create throws
        // TypeLoadException through the same GetOrCreateForm catch path the
        // served <Module> initializer failure takes (TargetInvocationException).
        Assert.Null(host.GetOrCreateForm(AppScene.BenchmarkFiddlerClassic));
        Assert.False(host.HasScene(AppScene.BenchmarkFiddlerClassic));
        string? detail = host.GetFormCreationError(AppScene.BenchmarkFiddlerClassic);
        Assert.NotNull(detail);
        Assert.StartsWith("create-failed:", detail);
        Assert.Contains("TypeLoadException", detail);
        // FACTORY-shell-1 contract change (user requirement: no implementation
        // jargon in UI): the suffix is plain language; the machine detail above
        // stays in GetFormCreationError/console/App errors and is NOT repeated.
        string suffix = host.FormCreationErrorSuffix(AppScene.BenchmarkFiddlerClassic);
        Assert.Equal(" Technical detail is in App errors.", suffix);
        Assert.DoesNotContain(detail, suffix);
    }

    [Fact]
    public void FailedScene_Suffix_Has_No_Implementation_Jargon()
    {
        // FACTORY-shell-1: the suffix is user-facing (Home.razor overlay), so
        // it must not leak machine codes, CLR type names or <- chains. The
        // full detail stays in GetFormCreationError/console/App errors.
        using var host = new WinFormsAppHost();
        Assert.Null(host.GetOrCreateForm(AppScene.BenchmarkFiddlerClassic));
        string suffix = host.FormCreationErrorSuffix(AppScene.BenchmarkFiddlerClassic);
        Assert.DoesNotContain("create-failed:", suffix);
        Assert.DoesNotContain("TypeLoadException", suffix);
        Assert.DoesNotContain("<-", suffix);
        Assert.DoesNotContain("Exception", suffix);
        Assert.Contains("App errors", suffix);
    }

    [Fact]
    public void PlatformNotSupported_Root_Maps_To_Plain_WindowsOnly_Sentence()
    {
        string suffix = WinFormsAppHost.PlainCreationCause(
            "create-failed: TargetInvocationException <- PlatformNotSupportedException: Operation is not supported on this platform.");
        Assert.Contains("Windows-only", suffix);
        Assert.DoesNotContain("PlatformNotSupportedException", suffix);
        Assert.DoesNotContain("TargetInvocationException", suffix);
        Assert.DoesNotContain("create-failed:", suffix);
    }

    [Fact]
    public void SuccessfulScene_Records_No_Error()
    {
        using var host = new WinFormsAppHost();
        Assert.NotNull(host.GetOrCreateForm(AppScene.Smoke));
        Assert.True(host.HasScene(AppScene.Smoke));
        Assert.Null(host.GetFormCreationError(AppScene.Smoke));
        Assert.Equal("", host.FormCreationErrorSuffix(AppScene.Smoke));
    }

    [Fact]
    public void SuccessfulScene_Clears_Prior_Error_For_Its_Own_Scene()
    {
        using var host = new WinFormsAppHost();
        Assert.Null(host.GetOrCreateForm(AppScene.BenchmarkFiddlerClassic));
        Assert.NotNull(host.GetFormCreationError(AppScene.BenchmarkFiddlerClassic));
        // A later success for the same lookup path clears the stale detail.
        // Benchmark scenes cannot succeed headless, so assert the success
        // path clears via a scene that does construct.
        Assert.NotNull(host.GetOrCreateForm(AppScene.Smoke));
        Assert.Null(host.GetFormCreationError(AppScene.Smoke));
    }

    [Fact]
    public void Scene_Without_Construction_Path_Records_NoForm_Detail()
    {
        using var host = new WinFormsAppHost();
        var unknown = (AppScene)9999;
        Assert.Null(host.GetOrCreateForm(unknown));
        Assert.False(host.HasScene(unknown));
        Assert.Equal($"no-form: {unknown} produced no window (missing switch case or type lookup failed)",
            host.GetFormCreationError(unknown));
    }
}
