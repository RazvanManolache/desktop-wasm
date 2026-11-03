using WfWasm.Rewrite;

public sealed class AssemblyLocationCctorRewriteTests
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
    public void Rewrite_Assembly_Location_Redirects_Inside_Static_Initializer()
    {
        // Fourth accessor (Syncfusion.WinForms.ListView.
        // ListViewLocalizationResourceAccessor): its static initializer runs
        // `Instance = new ...()` -> base .ctor ->
        // GetLocalizedResourceManager -> Combine(null,...) and the ANE
        // escapes as TypeInitializationException, where the instance
        // ctor-guard cannot reach (SF06/SF10 twins stay caught; the ListView
        // one kills form-create). Locks that the get_Location retarget also
        // fires inside .cctor bodies.
        string fixture = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "WfWasm.Rewrite.Fixture", "bin", "Debug", "net9.0-windows",
            "WfWasm.Rewrite.Fixture.dll"));
        Assert.True(File.Exists(fixture), $"fixture not built: {fixture}");
        string input = Path.Combine(Path.GetTempPath(), "asmlocation4.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(fixture));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var calls = new Mono.Cecil.TypeDefinition(
                "Syncfusion.WinForms.ListView", "ListViewLocalizationResourceAccessor",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(calls);
            var asmRef = new Mono.Cecil.TypeReference(
                "System.Reflection", "Assembly", module, module.TypeSystem.Object.Scope);
            var getExecuting = new Mono.Cecil.MethodReference(
                "GetExecutingAssembly", asmRef, asmRef) { HasThis = false };
            var getLocation = new Mono.Cecil.MethodReference("get_Location", module.TypeSystem.String, asmRef)
            {
                HasThis = true,
            };
            var cctor = new Mono.Cecil.MethodDefinition(
                ".cctor",
                Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static
                    | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            var il = cctor.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Call, getExecuting);
            il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, getLocation);
            il.Emit(Mono.Cecil.Cil.OpCodes.Pop);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            calls.Methods.Add(cctor);
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
        string tempOut = Path.Combine(Path.GetTempPath(), $"AssemblyLocation4_{Guid.NewGuid()}.dll");
        try
        {
            var typeMap = GapAnalyzer.AutoMap(input, map);
            AssemblyRewriter.Rewrite(input, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var cctor = asm.MainModule.GetTypes()
                .First(t => t.FullName == "Syncfusion.WinForms.ListView.ListViewLocalizationResourceAccessor").Methods
                .First(m => m.Name == ".cctor");
            var call = cctor.Body.Instructions
                .First(i => i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "SafeLocation");
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Call, call.OpCode);
            var target = (Mono.Cecil.MethodReference)call.Operand;
            Assert.Equal("System.Windows.Forms.WasmAssemblyLocation", target.DeclaringType.FullName);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
            try { if (File.Exists(input)) File.Delete(input); } catch { }
        }
    }
}
