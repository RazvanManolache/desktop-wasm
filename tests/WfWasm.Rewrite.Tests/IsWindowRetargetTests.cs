using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

// MEMBERREF-CONSTRAIN (ISWINDOW-1735): the IsWindow branch must emit only a
// single call to the resolved CpuPaint.IsWindowIntPtr(IntPtr)->Int32
// forwarder (primitives-only signature, same kind as the proven helper
// family). The previous direct chain (HWND::op_Explicit /
// PInvoke::IsWindow / BOOL::op_Explicit with hand-built custom-valuetype
// member refs) threw TypeLoadException kind-17 ("Expected reference type
// but got type kind 17") at NativeMessageHandler.Assign on first
// execution in vivo. Companion CapMiniToolBarTopWindowLoop bounds the
// one IsWindow-guarded stub walk this truthfulness unblocks.
public sealed class IsWindowRetargetTests
{
    private static MethodDefinition AddPInvoke(TypeDefinition type, ModuleReference dll, string name, TypeReference ret, params TypeReference[] args)
    {
        var m = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PInvokeImpl, ret);
        foreach (var a in args)
            m.Parameters.Add(new ParameterDefinition(a));
        m.PInvokeInfo = new PInvokeInfo(PInvokeAttributes.CallConvStdCall, name, dll);
        m.IsPInvokeImpl = true;
        type.Methods.Add(m);
        return m;
    }

    private static string Disassemble(MethodDefinition m) =>
        string.Join(";", m.Body.Instructions.Select(i => i.OpCode.Name + (i.Operand is null ? "" : ":" + i.Operand)));

    private static string CpuPaintDll() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "WfWasm.PdnCpu", "bin", "Debug", "net9.0", "WfWasm.PdnCpu.dll"));

    private static void AssertForwarderCall(MethodDefinition m, bool expectConvI, bool expectConvU4)
    {
        var ins = m.Body.Instructions;
        Assert.Equal(OpCodes.Ldarg_0, ins[0].OpCode);
        int k = 1;
        if (expectConvI)
            Assert.Equal(OpCodes.Conv_I, ins[k++].OpCode);
        var fwd = Assert.IsType<MethodReference>(ins[k++].Operand);
        Assert.Equal("IsWindowIntPtr", fwd.Name);
        Assert.Equal("WfWasm.PdnCpu.CpuPaint", fwd.DeclaringType.FullName);
        Assert.Single(fwd.Parameters);
        Assert.Equal("System.IntPtr", fwd.Parameters[0].ParameterType.FullName);
        Assert.Equal("System.Int32", fwd.ReturnType.FullName);
        if (expectConvU4)
            Assert.Equal(OpCodes.Conv_U4, ins[k++].OpCode);
        Assert.Equal(OpCodes.Ret, ins[k].OpCode);
    }

    [Fact]
    public void StubModulePInvokes_IsWindow_Calls_Resolved_Forwarder_Others_FailClosed()
    {
        using var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("StubProbeIsWindow", new Version(1, 0)), "StubProbeIsWindow", ModuleKind.Dll);
        var module = asm.MainModule;
        var dll = new ModuleReference("user32");
        module.ModuleReferences.Add(dll);
        var pinv = new TypeDefinition("", "P", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(pinv);
        var isWindow = AddPInvoke(pinv, dll, "IsWindow", module.TypeSystem.Int32, module.TypeSystem.IntPtr);
        var isWindowU = AddPInvoke(pinv, dll, "IsWindow", module.TypeSystem.UInt32, module.TypeSystem.UIntPtr);
        var isWindowArity2 = AddPInvoke(pinv, dll, "IsWindow", module.TypeSystem.Int32, module.TypeSystem.IntPtr, module.TypeSystem.IntPtr);
        var isWindowPtr = AddPInvoke(pinv, dll, "IsWindow", module.TypeSystem.IntPtr, module.TypeSystem.IntPtr);
        var getTick = AddPInvoke(pinv, dll, "GetTickCount", module.TypeSystem.Int32);

        AssemblyRewriter.StubModulePInvokes(module, CpuPaintDll());

        Assert.False(isWindow.IsPInvokeImpl);
        AssertForwarderCall(isWindow, expectConvI: false, expectConvU4: false);
        AssertForwarderCall(isWindowU, expectConvI: true, expectConvU4: true);
        Assert.Equal("ldc.i4.0;ret", Disassemble(isWindowArity2));
        Assert.Equal("ldc.i4:4096;conv.i;ret", Disassemble(isWindowPtr));
        Assert.Equal("ldc.i4.0;ret", Disassemble(getTick));
        foreach (var mm in pinv.Methods)
        {
            foreach (var i in mm.Body.Instructions)
            {
                if (i.Operand is MethodReference mr
                    && mr.DeclaringType.FullName.StartsWith("Windows.Win32.", System.StringComparison.Ordinal))
                    Assert.Fail("custom-valuetype member ref escapes: " + mr.FullName);
            }
        }
    }

    [Fact]
    public void StubModulePInvokes_IsWindow_FailClosed_Without_Forwarder_Path()
    {
        using var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("StubProbeIsWindowNull", new Version(1, 0)), "StubProbeIsWindowNull", ModuleKind.Dll);
        var module = asm.MainModule;
        var dll = new ModuleReference("user32");
        module.ModuleReferences.Add(dll);
        var pinv = new TypeDefinition("", "P", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(pinv);
        var isWindow = AddPInvoke(pinv, dll, "IsWindow", module.TypeSystem.Int32, module.TypeSystem.IntPtr);

        AssemblyRewriter.StubModulePInvokes(module);

        Assert.Equal("ldc.i4.0;ret", Disassemble(isWindow));
    }

    [Fact]
    public void Rewrite_ToolsWindows_IsWindow_Forwarded_And_TopWindow_Walk_Bounded()
    {
        string input = @"E:/AI/Code/DesktopWasm/inputs/winforms/syncfusion-staging/syncfusion-word/Syncfusion.Tools.Windows.dll";
        Assert.True(File.Exists(input), "Word staging input missing: " + input);
        string tempOut = Path.Combine(Path.GetTempPath(), $"SfIsWindow_{Guid.NewGuid():N}.dll");
        try
        {
            WfWasm.Rewrite.AssemblyRewriter.Rewrite(
                input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                CpuPaintDll());
            using var module = AssemblyDefinition.ReadAssembly(tempOut).MainModule;
            var winApi = module.GetType("Syncfusion.Windows.Forms.Tools.Win32API.WindowsAPI");
            Assert.NotNull(winApi);
            var isWindow = winApi.Methods.FirstOrDefault(m =>
                m.Name == "IsWindow" && m.HasBody && m.Parameters.Count == 1);
            Assert.NotNull(isWindow);
            bool sawFwd = isWindow.Body.Instructions.Any(i =>
                i.Operand is MethodReference mr && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint" && mr.Name == "IsWindowIntPtr");
            Assert.True(sawFwd, "CpuPaint.IsWindowIntPtr dispatch lost in " + isWindow.FullName);
            foreach (var i in isWindow.Body.Instructions)
            {
                if (i.Operand is MethodReference mr
                    && mr.DeclaringType.FullName.StartsWith("Windows.Win32.", System.StringComparison.Ordinal))
                    Assert.Fail("custom-valuetype member ref escapes staged Tools.Windows: " + mr.FullName);
            }
            Assert.DoesNotContain(isWindow.Body.Instructions, i =>
                i.OpCode == OpCodes.Ldc_I4_0 && i.Next?.OpCode == OpCodes.Ret);

            var bar = module.GetType("Syncfusion.Windows.Forms.Tools.MiniToolBar");
            Assert.NotNull(bar);
            var target = bar.Methods.FirstOrDefault(m =>
                m.Name == "GetTopWindow" && !m.IsStatic && m.HasBody && m.Parameters.Count == 0);
            Assert.NotNull(target);
            bool sawCap = false;
            bool sawCapExit = false;
            int backwardBranches = 0;
            foreach (var ins in target.Body.Instructions)
            {
                if (ins.OpCode == OpCodes.Ldc_I4 && ins.Operand is int n && n == 256)
                    sawCap = true;
                if ((ins.OpCode == OpCodes.Bge || ins.OpCode == OpCodes.Bge_S)
                    && ins.Operand is Instruction)
                    sawCapExit = true;
                if (ins.OpCode.FlowControl == FlowControl.Cond_Branch
                    && ins.Operand is Instruction t
                    && target.Body.Instructions.IndexOf(t) < target.Body.Instructions.IndexOf(ins))
                    Assert.Fail("Unbounded loop back-edge survives in " + target.FullName);
                if (ins.OpCode.FlowControl == FlowControl.Branch
                    && ins.Operand is Instruction tb
                    && target.Body.Instructions.IndexOf(tb) < target.Body.Instructions.IndexOf(ins))
                    backwardBranches++;
            }
            Assert.True(sawCap, "256 step cap missing in " + target.FullName);
            Assert.True(sawCapExit, "cap exit branch missing in " + target.FullName);
            Assert.Equal(1, backwardBranches);
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
