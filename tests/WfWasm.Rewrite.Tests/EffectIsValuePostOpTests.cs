using Mono.Cecil;
using Mono.Cecil.Cil;

// VEN-QC4-2057: get_IsValue returns bool, but the effect-setting reroute
// emitted the bridge post-op with the SETTING type (gi arg0) instead of
// the getter return type. Result<bool> sites passed by accident (bool
// == bool); Result<PaletteTable>.get_IsValue in QuantizeBitmapEffect.
// TryGetQuantizer became (PaletteTable)GetEffectSettingValue(..., 3, ...),
// throwing InvalidCastException at Quantize Colors=2 (worker-thread
// exception, zero content change) while default-256 short-circuits early
// and stays green. which==3 call sites must unbox System.Boolean.
public sealed class EffectIsValuePostOpTests
{
    private static string PaintDir()
    {
        string dir = @"E:\AI\Code\WindowsForms\targets\paint.net";
        Assert.True(Directory.Exists(dir), "pristine PDN inputs missing: " + dir);
        return dir;
    }

    private static string BinDll(string project, string tfm, string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", project, "bin", "Debug", tfm, name + ".dll"));
        Assert.True(File.Exists(dll), "dependency not built: " + dll);
        return dll;
    }

    private static ModuleDefinition Transform(string fileName, string tempOut)
    {
        string input = Path.Combine(PaintDir(), fileName);
        Assert.True(File.Exists(input), "pristine PDN input missing: " + input);
        string cpuDll = BinDll("WfWasm.PdnCpu", "net9.0", "WfWasm.PdnCpu");
        WfWasm.Rewrite.AssemblyRewriter.Rewrite(
            input, tempOut,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
            cpuDll);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(PaintDir());
        return AssemblyDefinition.ReadAssembly(tempOut,
            new ReaderParameters { AssemblyResolver = resolver }).MainModule;
    }

    private static int? LdcValue(Instruction ins)
    {
        if (ins.OpCode == OpCodes.Ldc_I4 && ins.Operand is int i) return i;
        if (ins.OpCode == OpCodes.Ldc_I4_S && ins.Operand is sbyte sb) return sb;
        if (ins.OpCode == OpCodes.Ldc_I4_M1) return -1;
        if (ins.OpCode == OpCodes.Ldc_I4_0) return 0;
        if (ins.OpCode == OpCodes.Ldc_I4_1) return 1;
        if (ins.OpCode == OpCodes.Ldc_I4_2) return 2;
        if (ins.OpCode == OpCodes.Ldc_I4_3) return 3;
        if (ins.OpCode == OpCodes.Ldc_I4_4) return 4;
        if (ins.OpCode == OpCodes.Ldc_I4_5) return 5;
        if (ins.OpCode == OpCodes.Ldc_I4_6) return 6;
        if (ins.OpCode == OpCodes.Ldc_I4_7) return 7;
        if (ins.OpCode == OpCodes.Ldc_I4_8) return 8;
        return null;
    }

    [Fact]
    public void Effect_IsValue_Reroute_Unboxes_Bool_Not_Setting_Type()
    {
        const string fileName = "PaintDotNet.Effects.dll";
        string tempOut = Path.Combine(Path.GetTempPath(), $"FxIsValue_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = Transform(fileName, tempOut);
            int which3Sites = 0;
            var bad = new List<string>();
            var stack = new Stack<TypeDefinition>(module.Types);
            while (stack.Count > 0)
            {
                var type = stack.Pop();
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    var insns = method.Body.Instructions;
                    for (int idx = 0; idx < insns.Count; idx++)
                    {
                        var ins = insns[idx];
                        if ((ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt)
                            || ins.Operand is not MethodReference mr
                            || mr.DeclaringType.FullName != "WfWasm.PdnCpu.CpuPaint"
                            || mr.Name != "GetEffectSettingValue")
                            continue;
                        int? which = null;
                        for (int back = idx - 1; back >= 0 && back >= idx - 4; back--)
                        {
                            which = LdcValue(insns[back]);
                            if (which.HasValue) break;
                        }
                        if (which != 3) continue;
                        which3Sites++;
                        var next = idx + 1 < insns.Count ? insns[idx + 1] : null;
                        bool ok = next is not null
                            && next.OpCode == OpCodes.Unbox_Any
                            && next.Operand is TypeReference tr
                            && tr.FullName == "System.Boolean";
                        if (!ok)
                            bad.Add(type.FullName + "::" + method.Name
                                + " which=3 post-op is "
                                + (next is null ? "<end>" : next.OpCode.Name + " "
                                    + (next.Operand?.ToString() ?? "?")));
                    }
                }
                foreach (var nested in type.NestedTypes)
                    stack.Push(nested);
            }
            Assert.True(which3Sites > 0,
                fileName + " must contain at least one get_IsValue reroute site");
            Assert.True(bad.Count == 0,
                fileName + " IsValue reroute must unbox bool, not the setting type: "
                + string.Join("; ", bad.Take(10)));
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
