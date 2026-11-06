using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;

// Pins BypassCloudsServicesFactory against the real Effects.Gpu binary
// (targets/ read-only: copied to temp first): OnCreateConfigUI must no
// longer touch the lazy Services acquisition and instead activate the
// real factory with a synthesized MixMode-only predicate; everything
// downstream (Create, GetLocalizedEnumValues, enumerator loop, NotNull
// rails) stays byte-identical. Red-first: the service chain is present
// pre-fix, so the zero-Services asserts fail until the bypass is cut.
public sealed class CloudsFactoryBypassTests
{
    private static string GpuBytes()
    {
        string src = @"E:\AI\Code\WindowsForms\targets\paint.net\PaintDotNet.Effects.Gpu.dll";
        string tmp = Path.Combine(Path.GetTempPath(), $"CloudsFactory_{Guid.NewGuid()}.dll");
        File.Copy(src, tmp);
        return tmp;
    }

    private static MethodDefinition CloudsConfigUI(ModuleDefinition module)
    {
        var clouds = module.GetType("PaintDotNet.Effects.Gpu.CloudsGpuEffect");
        Assert.NotNull(clouds);
        return Assert.Single(clouds.Methods, m =>
            m.Name == "OnCreateConfigUI" && m.HasBody && m.Parameters.Count == 1);
    }

    [Fact]
    public void Bypass_Removes_Services_Acquisition_And_Activates_Factory()
    {
        string tmp = GpuBytes();
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            AssemblyRewriter.BypassCloudsServicesFactory(read.MainModule);

            var clouds = read.MainModule.GetType("PaintDotNet.Effects.Gpu.CloudsGpuEffect");
            Assert.NotNull(clouds);
            var allow = Assert.Single(clouds.Methods, m => m.Name == "CloudsMixModeAllowlist");
            Assert.True(allow.IsStatic);
            Assert.Single(allow.Parameters);
            Assert.Equal("System.Boolean", allow.ReturnType.FullName);
            var m = CloudsConfigUI(read.MainModule);
            var instrs = m.Body.Instructions.ToList();
            var calls = instrs
                .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                    && i.Operand is MethodReference)
                .Select(i => (MethodReference)i.Operand)
                .ToList();
            Assert.DoesNotContain(calls, r => r.Name == "get_Services");
            Assert.DoesNotContain(calls, r => r.Name == "GetRequiredService");
            Assert.Single(instrs, i => i.OpCode == OpCodes.Ldftn
                && i.Operand is MethodDefinition md && md == allow);
            Assert.Single(m.Body.Instructions, i => i.OpCode == OpCodes.Newobj
                && i.Operand is MethodReference nr && nr.Name == ".ctor"
                && nr.DeclaringType.FullName.Contains("Func")
                && nr.Parameters.Count == 2);
            Assert.Single(instrs, i => i.OpCode == OpCodes.Ldstr
                && (string)i.Operand == "PaintDotNet.AppModel.EnumLocalizerFactory, paintdotnet");
            Assert.Single(calls, r => r.Name == "GetType"
                && r.DeclaringType.FullName == "System.Type");
            Assert.Single(calls, r => r.Name == "CreateInstance"
                && r.DeclaringType.FullName == "System.Activator");
            Assert.Single(instrs, i => i.OpCode == OpCodes.Castclass
                && i.Operand is TypeReference tr
                && tr.FullName == "PaintDotNet.AppModel.IEnumLocalizerFactory");
            // Downstream untouched: interface factory Create, interface
            // GetLocalizedEnumValues, enumerator loop, NotNull rails.
            Assert.Single(calls, r => r.Name == "Create"
                && r.DeclaringType.FullName == "PaintDotNet.AppModel.IEnumLocalizerFactory");
            Assert.Single(calls, r => r.Name == "GetLocalizedEnumValues"
                && r.DeclaringType.FullName == "PaintDotNet.AppModel.IEnumLocalizer");
            Assert.Single(calls, r => r.Name == "GetEnumerator");
            Assert.Equal(4, calls.Count(r => r.Name == "NotNull"));
            Assert.Equal(OpCodes.Ret, m.Body.Instructions.Last().OpCode);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public void Bypass_Survives_Full_Rewrite_Pipeline_Order()
    {
        // w14 regen missed the bypass in converts: the pipeline runs
        // RetargetEffectSettingGetters before BypassCloudsServicesFactory,
        // and the getters insert extra ldtokens into
        // CloudsGpuEffect.OnCreateConfigUI, tripping the bypass's
        // "ldtoken not unique" guard (swallowed by the pipeline catch).
        // Pin the order: full Rewrite must land the bypass.
        string input = @"E:\AI\Code\WindowsForms\targets\paint.net\PaintDotNet.Effects.Gpu.dll";
        string tempOut = Path.Combine(Path.GetTempPath(), $"CloudsPipeline_{Guid.NewGuid():N}.dll");
        string cpuDll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "WfWasm.PdnCpu", "bin", "Debug", "net9.0", "WfWasm.PdnCpu.dll"));
        Assert.True(File.Exists(cpuDll), "dependency not built: " + cpuDll);
        try
        {
            WfWasm.Rewrite.AssemblyRewriter.Rewrite(
                input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(@"E:\AI\Code\WindowsForms\targets\paint.net");
            using var read = AssemblyDefinition.ReadAssembly(tempOut,
                new ReaderParameters { AssemblyResolver = resolver });
            var clouds = read.MainModule.GetType("PaintDotNet.Effects.Gpu.CloudsGpuEffect");
            Assert.NotNull(clouds);
            Assert.Contains(clouds.Methods, m => m.Name == "CloudsMixModeAllowlist");
            var ui = Assert.Single(clouds.Methods, m =>
                m.Name == "OnCreateConfigUI" && m.HasBody && m.Parameters.Count == 1);
            var calls = ui.Body.Instructions
                .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                    && i.Operand is MethodReference)
                .Select(i => (MethodReference)i.Operand)
                .ToList();
            Assert.DoesNotContain(calls, r => r.Name == "get_Services");
            Assert.DoesNotContain(calls, r => r.Name == "GetRequiredService");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void Bypass_Rejects_Double_Application()
    {
        string tmp = GpuBytes();
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            AssemblyRewriter.BypassCloudsServicesFactory(read.MainModule);
            Assert.Throws<InvalidOperationException>(
                () => AssemblyRewriter.BypassCloudsServicesFactory(read.MainModule));
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
