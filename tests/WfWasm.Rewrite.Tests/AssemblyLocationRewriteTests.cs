using WfWasm.Rewrite;

public sealed class AssemblyLocationRewriteTests
{
    private static string ShimDll(string project, string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", project, "bin", "Debug", "net9.0", name + ".dll"));
        Assert.True(File.Exists(dll), $"shim not built: {dll}");
        return dll;
    }

    [Fact]
    public void Rewrite_Assembly_Location_Redirects_To_Fork_Shim()
    {
        // Syncfusion SR LocalizationResourceAccessor.GetLocalizedResourceManager
        // probes Path.Combine(GetDirectoryName(asm.Location), culture, name).
        // Single-file/wasm Location is "", GetDirectoryName answers null and
        // Combine throws ArgumentNullException("path1") (SF06/SF10 ctor-guard
        // noise) before the embedded-resource fallback can run. The rewriter
        // retargets the get_Location call to the fork's empty-guarded
        // SafeLocation (desktop passthrough); the probe path then composes
        // and File.Exists skips it cleanly.
        string fixture = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "WfWasm.Rewrite.Fixture", "bin", "Debug", "net9.0-windows",
            "WfWasm.Rewrite.Fixture.dll"));
        Assert.True(File.Exists(fixture), $"fixture not built: {fixture}");
        string input = Path.Combine(Path.GetTempPath(), "asmlocation.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(fixture));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var calls = new Mono.Cecil.TypeDefinition(
                "Syncfusion.Windows.Forms", "LocalizationResourceAccessor",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(calls);
            var asmRef = new Mono.Cecil.TypeReference(
                "System.Reflection", "Assembly", module, module.TypeSystem.Object.Scope);
            var getLocation = new Mono.Cecil.MethodReference("get_Location", module.TypeSystem.String, asmRef)
            {
                HasThis = true,
            };
            var probe = new Mono.Cecil.MethodDefinition(
                "GetLocalizedResourceManager",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.String);
            probe.Parameters.Add(new Mono.Cecil.ParameterDefinition(asmRef));
            var il = probe.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
            il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, getLocation);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            calls.Methods.Add(probe);
            asm.Write(input);
        }

        string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
        string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["System.Windows.Forms"] = formsDll,
            ["System.Windows.Forms.Primitives"] = formsDll,
            ["System.Drawing"] = drawingDll,
            ["System.Drawing.Common"] = drawingDll,
            ["System.Drawing.Primitives"] = drawingDll,
        };
        string tempOut = Path.Combine(Path.GetTempPath(), $"AssemblyLocation_{Guid.NewGuid()}.dll");
        try
        {
            var typeMap = GapAnalyzer.AutoMap(input, map);
            AssemblyRewriter.Rewrite(input, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var probe = asm.MainModule.GetTypes()
                .First(t => t.FullName == "Syncfusion.Windows.Forms.LocalizationResourceAccessor").Methods
                .First(m => m.Name == "GetLocalizedResourceManager");
            var call = probe.Body.Instructions
                .First(i => i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "SafeLocation");
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Call, call.OpCode);
            var target = (Mono.Cecil.MethodReference)call.Operand;
            Assert.Equal("System.Windows.Forms.WasmAssemblyLocation", target.DeclaringType.FullName);
            Assert.Single(target.Parameters);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
            try { if (File.Exists(input)) File.Delete(input); } catch { }
        }
    }
}
