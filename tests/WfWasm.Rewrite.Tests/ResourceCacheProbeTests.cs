using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;

// Pins the resource-cache/rewriter bridge diagnostics: the CPU
// GetCachedOrCreateResource contract must record a probe note on every
// miss and every unknown-provider throw (stage + provider-key, fail
// closed), so served first-chance logs carry WHO/WHAT/WHERE instead of a
// bare NotSupportedException. Red-first: no NoteMiss calls are emitted
// yet, so this fails until the contract wires the probe.
public sealed class ResourceCacheProbeTests
{
    private static string ShimBinDll(string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", name, "bin", "Debug", "net9.0", name + ".dll"));
        Assert.True(File.Exists(dll), $"shim bin not built: {dll}");
        return dll;
    }

    [Fact]
    public void Rewrite_ResourceCache_Miss_And_Unknown_Record_Probe_Notes()
    {
        string corePath = @"E:\AI\Code\WindowsForms\targets\paint.net\PaintDotNet.Windows.Core.dll";
        if (!File.Exists(corePath)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"WindowsCore_ResProbe_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(
                corePath, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);

            var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(corePath)!);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
                new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            var ctx = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var resCache = Assert.Single(ctx.Methods,
                m => m.Name == "GetCachedOrCreateResource" && m.IsVirtual);
            var probeCalls = resCache.Body.Instructions
                .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                    && i.Operand is MethodReference mr
                    && mr.DeclaringType.FullName == "WfWasm.PdnCpu.EffectConstructionProbe"
                    && mr.Name == "NoteMiss")
                .ToList();
            // Exactly two: the cache-miss arm and the unknown-provider arm.
            // The hit fast path stays silent (hot paint path, no probe).
            Assert.Equal(2, probeCalls.Count);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
}
