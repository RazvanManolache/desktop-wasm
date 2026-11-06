using WfWasm.Host.Server;

namespace WfWasm.Host.Server.Tests;

public sealed class BenchmarkStartupCacheIsolationTests
{
    [Fact]
    public void ExperimentalStartupFlag_IsolatedInTransformCache()
    {
        // ADAPTER-shell-1050: staging enables the LINQPad startup adapter via
        // WFWASM_EXPERIMENTAL_BENCHMARK_STARTUP=1. The flag must be part of the
        // transform cache key so experimental bytes can never leak into normal
        // sessions sharing the transform cache. Order is the leak direction:
        // experimental first, then normal must still serve non-adapted bytes.
        // Real retained original input, no synthetic assemblies.
        const string app = "benchmark-linqpad5";
        Assert.True(AppTransform.TryResolveAssembly(app, "LINQPad.exe", out string? name));
        Assert.True(name is not null);
        const string flag = "WFWASM_EXPERIMENTAL_BENCHMARK_STARTUP";
        string? previous = Environment.GetEnvironmentVariable(flag);
        try
        {
            Environment.SetEnvironmentVariable(flag, "1");
            byte[] adapted = AppTransform.TransformOrCached(app, name!);
            Assert.True(HasPublicParameterlessMainFormCtor(adapted), "adapter ctor missing with flag=1");

            Environment.SetEnvironmentVariable(flag, null);
            byte[] normal = AppTransform.TransformOrCached(app, name!);
            Assert.False(HasPublicParameterlessMainFormCtor(normal), "adapter bytes leaked into flag-off cache entry");
            Assert.NotEqual(adapted, normal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(flag, previous);
        }
    }

    private static bool HasPublicParameterlessMainFormCtor(byte[] bytes)
    {
        using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(bytes));
        var mainForm = asm.MainModule.GetType("LINQPad.UI.MainForm");
        Assert.True(mainForm is not null);
        return mainForm.Methods.Any(m => m.IsConstructor && m.IsPublic && !m.IsStatic && m.Parameters.Count == 0);
    }
}
