using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Host.Server;

namespace WfWasm.Host.Server.Tests;

public sealed class DirectApplyIntegrationTests
{
    [Fact]
    public void Paint_Uses_The_Deployed_Helper_And_Map()
    {
        Assert.True(AppTransform.TryGetSpec("paintnet", out var spec));
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "WfWasm.PdnCpu.dll"), spec!.PdnCpu);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "handmap.paint.net.txt"), spec.Handmap);
        Assert.True(File.Exists(spec.Handmap));
    }

    [Fact]
    public void Real_Paint_Direct_Effect_Stashes_Source_Before_Rendering()
    {
        // Exercise the production conversion path, not just a synthetic Cecil
        // method. A stale helper path used to silently omit this rule.
        byte[] bytes = AppTransform.TransformOrCached("paintnet", "paintdotnet.dll");
        using var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes));
        var method = asm.MainModule.GetType("PaintDotNet.Menus.EffectMenuBase")
            .Methods.Single(m => m.Name == "DoEffect" && m.Parameters.Count == 6);
        var entry = method.Body.Instructions.Take(3).ToArray();
        Assert.Equal(OpCodes.Ldarg_0, entry[0].OpCode);
        Assert.Equal(OpCodes.Ldarg_1, entry[1].OpCode);
        Assert.Equal(OpCodes.Call, entry[2].OpCode);
        var target = Assert.IsAssignableFrom<MethodReference>(entry[2].Operand);
        Assert.Equal("NoteDirectApplySource", target.Name);
        Assert.Equal("WfWasm.PdnCpu", target.DeclaringType.Scope.Name);
        Assert.Single(method.Body.Instructions.Where(i => i.Operand is MethodReference mr
            && mr.Name == "SaveEffectHistoryInline"));
        Assert.DoesNotContain(method.Body.Instructions, i => i.Operand is MethodReference mr
            && mr.DeclaringType.FullName == "PaintDotNet.Threading.Work" && mr.Name == "QueueWorkItem");
    }

    [Fact]
    public void Real_Cpu_Context_Releases_Cache_On_Last_Reference_Teardown()
    {
        byte[] bytes = AppTransform.TransformOrCached("paintnet", "PaintDotNet.Windows.Core.dll");
        using var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes));
        var context = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
        var dispose = context.Methods.Single(m => m.Name == "Dispose" && m.Parameters.Count == 1);
        var calls = dispose.Body.Instructions.Where(i => i.Operand is MethodReference)
            .Select(i => ((MethodReference)i.Operand).Name).ToArray();
        int cleanup = Array.IndexOf(calls, "ReleaseCpuCache");
        Assert.True(cleanup >= 0 && cleanup < Array.IndexOf(calls, "FreeContext"));
        var cache = context.Methods.Single(m => m.Name == "GetCachedOrCreateResource" && m.IsVirtual);
        Assert.Contains(cache.Body.Instructions, i => i.Operand is MethodReference m && m.Name == "SetCpuCacheEntry");
        var effect = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDeviceEffect");
        var finalizer = effect.Methods.Single(m => m.Name == "Finalize");
        Assert.Contains(finalizer.Body.Instructions, i => i.Operand is MethodReference m && m.Name == "ReleaseCpuRetainedInputs");
    }
}
