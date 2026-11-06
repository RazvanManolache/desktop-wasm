using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

namespace WfWasm.Rewrite.Tests;

// RVW3293-VEN7: worker-side first-chance logging for the Quantize
// post-palette stall (RT7 handoff). The rule wraps the four handler-free
// worker-phase methods (QuantizeBitmapEffect.TryGetQuantizer /
// ReturnQuantizer and the bitmap-overload GeneratePalette of
// MedianCutWicQuantizer + OctreeQuantizer) with ENTER/EXIT
// TraceBridgeSite probes plus a log-then-rethrow fault wrapper capturing
// TargetSite, so the next served regen names the terminal site headless.
// EffectMenuBase.WaitWithUI owns the modal-loop finally and is SKIPPED
// (EH untouched). Pinned directly against the pristine inputs.
public sealed class QuantizePhaseOracleV7Tests
{
    private static string PaintDir()
    {
        string dir = @"E:\AI\Code\WindowsForms\targets\paint.net";
        Assert.True(Directory.Exists(dir), "pristine PDN inputs missing: " + dir);
        return dir;
    }

    private static string CpuPaintDll()
    {
        string root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        foreach (string cfg in new[] { "Debug", "Release" })
        {
            string p = Path.Combine(root, "src", "WfWasm.PdnCpu",
                "bin", cfg, "net9.0", "WfWasm.PdnCpu.dll");
            if (File.Exists(p))
                return p;
        }
        Assert.Fail("built WfWasm.PdnCpu.dll missing (build src/WfWasm.PdnCpu first)");
        throw new InvalidOperationException("unreachable");
    }

    private static ModuleDefinition Load(string file)
    {
        string input = Path.Combine(PaintDir(), file);
        Assert.True(File.Exists(input), "pristine PDN input missing: " + input);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(PaintDir());
        return AssemblyDefinition.ReadAssembly(input,
            new ReaderParameters { AssemblyResolver = resolver }).MainModule;
    }

    private static MethodDefinition Find(
        ModuleDefinition module, string type, string method, int argc, string? firstParam)
    {
        var t = module.GetType(type);
        Assert.NotNull(t);
        var m = t!.Methods.FirstOrDefault(m => m.Name == method && m.HasBody
            && m.Parameters.Count == argc
            && (firstParam is null || m.Parameters[0].ParameterType.FullName == firstParam));
        Assert.NotNull(t.FullName + "::" + method + "/" + argc);
        return m!;
    }

    private static string Tag(string type, string method) =>
        "ORACLE-V7 " + type + "::" + method;

    private static bool HasPhaseProbe(MethodDefinition m, string kind, string tag)
    {
        var ins = m.Body.Instructions;
        for (int i = 0; i + 1 < ins.Count; i++)
            if (ins[i].OpCode == OpCodes.Ldstr && (string?)ins[i].Operand == kind + " " + tag
                && ins[i + 1].OpCode == OpCodes.Call
                && ins[i + 1].Operand is MethodReference mr
                && mr.Name == "TraceBridgeSite")
                return true;
        return false;
    }

    private static bool HasFaultWrapper(MethodDefinition m) =>
        m.Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Catch
            && h.CatchType.FullName == "System.Exception"
            && h.HandlerStart.OpCode == OpCodes.Call
            && h.HandlerStart.Operand is MethodReference fmr
            && fmr.Name == "TraceWorkerFault")
        && m.Body.Instructions.Any(i => i.OpCode == OpCodes.Rethrow);

    private static HashSet<string> CallSet(MethodDefinition m) =>
        m.Body.Instructions
            .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference)
            .Select(i =>
            {
                var mr = (MethodReference)i.Operand;
                return mr.DeclaringType.FullName + "::" + mr.Name;
            })
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Oracle_Wraps_Worker_Phase_Methods()
    {
        string cpuPaint = CpuPaintDll();
        var effects = Load("PaintDotNet.Effects.dll");
        var framework = Load("PaintDotNet.Framework.dll");
        var onRender = Find(effects,
            "PaintDotNet.Effects.QuantizeBitmapEffect", "OnRender", 2, null);
        var tryGet = Find(effects,
            "PaintDotNet.Effects.QuantizeBitmapEffect", "TryGetQuantizer", 1, null);
        var ret = Find(effects,
            "PaintDotNet.Effects.QuantizeBitmapEffect", "ReturnQuantizer", 2, null);
        var wic = Find(framework,
            "PaintDotNet.Imaging.Quantization.MedianCutWicQuantizer", "GeneratePalette",
            4, "PaintDotNet.Imaging.IBitmapSource");
        var oct = Find(framework,
            "PaintDotNet.Imaging.Quantization.OctreeQuantizer", "GeneratePalette",
            4, "PaintDotNet.Imaging.IBitmapSource");
        var all = new[] { onRender, tryGet, ret, wic, oct };

        // TryGetQuantizer carries handlers (verified on pristine bytes), so
        // the oracle skips it and OnRender brackets its span instead.
        Assert.True(tryGet.Body.HasExceptionHandlers);

        // Pre-shape (pristine PDN never references CpuPaint or oracle tags).
        foreach (var m in all)
        {
            string tag = Tag(m.DeclaringType.FullName, m.Name);
            Assert.False(HasPhaseProbe(m, "ENTER", tag));
            Assert.False(HasPhaseProbe(m, "EXIT", tag));
            Assert.False(HasFaultWrapper(m));
            Assert.DoesNotContain(CallSet(m),
                c => c.StartsWith("WfWasm.PdnCpu.CpuPaint::", StringComparison.Ordinal));
        }
        var before = all.ToDictionary(m => m, m => CallSet(m));
        var handlersBefore = all.ToDictionary(m => m, m => m.Body.ExceptionHandlers.Count);
        // The rule skips exactly the handler-carrying methods: snapshot the
        // pre-rule status (wrapped methods gain one handler) so any
        // divergence between rule behavior and pristine shape fails.
        var skippedBefore = all.ToDictionary(m => m, m => m.Body.HasExceptionHandlers);
        bool Skipped(MethodDefinition m) => skippedBefore[m];

        int fx = AssemblyRewriter.EmitQuantizePhaseOracleV7(effects, cpuPaint);
        int fw = AssemblyRewriter.EmitQuantizePhaseOracleV7(framework, cpuPaint);
        Assert.Equal(all.Take(3).Count(m => !Skipped(m)), fx);
        Assert.Equal(all.Skip(3).Count(m => !Skipped(m)), fw);
        // Diagnostic floor: OnRender brackets the TryGet span and its fault
        // wrapper covers the whole body INCLUDING the inner TryGet /
        // GeneratePalette calls, so a fault there still logs with TargetSite
        // naming the exact inner thrower; ReturnQuantizer adds span
        // precision. OnRender ENTER + no Return ENTER + fault log localizes
        // the death to the TryGet/Gen span by elimination.
        Assert.True(fx + fw >= 2);

        // Post-shape: wrapped methods carry probes + fault wrapper with
        // original calls intact; skipped methods are byte-identical.
        foreach (var m in all)
        {
            string tag = Tag(m.DeclaringType.FullName, m.Name);
            if (Skipped(m))
            {
                Assert.False(HasPhaseProbe(m, "ENTER", tag));
                Assert.False(HasPhaseProbe(m, "EXIT", tag));
                Assert.False(HasFaultWrapper(m));
                Assert.Equal(handlersBefore[m], m.Body.ExceptionHandlers.Count);
                Assert.Equal(before[m], CallSet(m));
            }
            else
            {
                Assert.True(HasPhaseProbe(m, "ENTER", tag));
                Assert.True(HasPhaseProbe(m, "EXIT", tag));
                Assert.True(HasFaultWrapper(m));
                Assert.Equal(handlersBefore[m] + 1, m.Body.ExceptionHandlers.Count);
                var after = CallSet(m);
                after.ExceptWith(before[m]);
                Assert.Equal(
                    new HashSet<string>(StringComparer.Ordinal)
                    {
                        "WfWasm.PdnCpu.CpuPaint::TraceBridgeSite",
                        "WfWasm.PdnCpu.CpuPaint::TraceWorkerFault",
                    },
                    after);
            }
        }
    }

    [Fact]
    public void Oracle_Skips_Handler_Carrying_Closer()
    {
        string cpuPaint = CpuPaintDll();
        var app = Load("paintdotnet.dll");
        var waiter = Find(app,
            "PaintDotNet.Menus.EffectMenuBase", "WaitWithUI", 4, null);
        // The skip is EH-caused (modal-loop finally), not a match miss.
        Assert.True(waiter.Body.HasExceptionHandlers);
        int handlersBefore = waiter.Body.ExceptionHandlers.Count;
        var callsBefore = CallSet(waiter);

        Assert.Equal(0, AssemblyRewriter.EmitQuantizePhaseOracleV7(app, cpuPaint));

        string tag = Tag(waiter.DeclaringType.FullName, waiter.Name);
        Assert.False(HasPhaseProbe(waiter, "ENTER", tag));
        Assert.False(HasPhaseProbe(waiter, "EXIT", tag));
        Assert.False(HasFaultWrapper(waiter));
        Assert.Equal(handlersBefore, waiter.Body.ExceptionHandlers.Count);
        Assert.Equal(callsBefore, CallSet(waiter));
    }
}
