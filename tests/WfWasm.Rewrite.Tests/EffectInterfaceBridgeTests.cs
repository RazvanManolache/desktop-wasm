using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

// TRIO-NEXTWALL: the browser interpreter rejects callvirt whose declaring
// type is a generic interface instantiation (Quantize OnSetToken foreach:
// IEnumerable<T>.GetEnumerator / IEnumerator<T>.get_Current; render
// lambda b__4: IReadOnlyCollection<T>.get_Count). The rewriter bridges
// every 0-parameter shape in that family through
// CpuPaint.GetInterfaceMemberValue (ldtoken declaring interface +
// GetTypeFromHandle + ldstr member, resolved via the interface map),
// and every GetEffectSettingValue site now carries its declaring type
// (ldtoken + GetTypeFromHandle, 3-operand helper).
public sealed class EffectInterfaceBridgeTests
{
    private static string RewriteEffects(string input)
    {
        Assert.True(File.Exists(input), "Effects input missing: " + input);
        string cpuDll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "WfWasm.PdnCpu", "bin", "Debug", "net9.0", "WfWasm.PdnCpu.dll"));
        string tempOut = Path.Combine(Path.GetTempPath(), $"SfIfaceBridge_{Guid.NewGuid():N}.dll");
        try
        {
            WfWasm.Rewrite.AssemblyRewriter.Rewrite(
                input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            return tempOut;
        }
        catch
        {
            try { File.Delete(tempOut); } catch { }
            throw;
        }
    }

    private static bool IsGenericInterfaceCall(MethodReference mr, out GenericInstanceType? gi)
    {
        gi = null;
        if (mr.DeclaringType is not GenericInstanceType egi)
            return false;
        if (mr.Parameters.Count != 0 || mr is GenericInstanceMethod)
            return false;
        if (egi.ContainsGenericParameter)
            return false;
        TypeDefinition? ed = null;
        try { ed = egi.ElementType.Resolve(); } catch { }
        if (ed is null || !ed.IsInterface)
            return false;
        gi = egi;
        return true;
    }

    [Theory]
    [InlineData("PaintDotNet.Effects.dll")]
    [InlineData("PaintDotNet.Effects.Gpu.dll")]
    [InlineData("PaintDotNet.Effects.Legacy.dll")]
    public void Rewrite_Effects_Bridges_Generic_Interface_Dispatch_And_Carries_SiteTypes(string file)
    {
        string tempOut = RewriteEffects(@"E:\AI\Code\WindowsForms\targets\paint.net\" + file);
        try
        {
            using var module = AssemblyDefinition.ReadAssembly(tempOut).MainModule;
            int bridge = 0;
            int valueSites = 0;
            int valueSitesWithToken = 0;
            var survivors = new List<string>();
            foreach (var type in module.Types.SelectMany(AllTypes))
            {
                foreach (var m in type.Methods)
                {
                    if (!m.HasBody) continue;
                    var insns = m.Body.Instructions;
                    for (int k = 0; k < insns.Count; k++)
                    {
                        var ins = insns[k];
                        if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt)
                            continue;
                        if (ins.Operand is not MethodReference mr)
                            continue;
                        if (mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                            && mr.Name == "GetInterfaceMemberValue")
                        {
                            bridge++;
                            continue;
                        }
                        if (mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                            && mr.Name == "GetEffectSettingValue")
                        {
                            valueSites++;
                            bool hasToken = false, hasFromHandle = false;
                            for (int j = System.Math.Max(0, k - 4); j < k; j++)
                            {
                                if (insns[j].OpCode == OpCodes.Ldtoken) hasToken = true;
                                if (insns[j].Operand is MethodReference pm
                                    && pm.Name == "GetTypeFromHandle") hasFromHandle = true;
                            }
                            if (hasToken && hasFromHandle) valueSitesWithToken++;
                            continue;
                        }
                        if (ins.OpCode == OpCodes.Callvirt && IsGenericInterfaceCall(mr, out _))
                            survivors.Add(type.FullName + "::" + m.Name + " -> " + mr.FullName);
                    }
                }
            }
            if (file != "PaintDotNet.Effects.Legacy.dll")
                Assert.True(bridge > 0, "no GetInterfaceMemberValue bridge calls emitted in " + file);
            Assert.True(valueSites > 0, "no GetEffectSettingValue sites found in " + file);
            Assert.Equal(valueSites, valueSitesWithToken);
            Assert.True(survivors.Count == 0,
                "generic-interface 0-param callvirt survives:\n" + string.Join("\n", survivors.Take(8)));
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition t)
    {
        yield return t;
        foreach (var n in t.NestedTypes)
            foreach (var x in AllTypes(n))
                yield return x;
    }
}
