using Mono.Cecil;
using Mono.Cecil.Cil;

// SF01/SF05 root (SF05-DISMISS-20261102-dw-runtime): an OPEN vendor popup
// plus Escape wedged the main thread in
// PopupUtils.GetADotNetParentControl when GetParent was a constant stub
// (served 0x1000): while (handle != Zero) { handle = GetParent(handle);
// ... } never progressed, no exception, dead CDP. The rule bounds the
// walk at 256 iterations so no stub regression can re-wedge the pump;
// true-parent walks terminate in a handful of steps and never touch the
// bound. This pins the shipped Word input: the unbounded back-edge is
// gone, the cap is present, and GetParent/FromHandle still dispatch.
public sealed class PopupParentWalkGuardTests
{
    [Fact]
    public void Parent_Walk_Bounded_And_Function_Preserved()
    {
        string input = @"E:\AI\Code\DesktopWasm\inputs\winforms\syncfusion-staging\syncfusion-word\Syncfusion.Shared.Base.dll";
        Assert.True(File.Exists(input), "Word staging input missing: " + input);
        string cpuDll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "WfWasm.PdnCpu", "bin", "Debug", "net9.0", "WfWasm.PdnCpu.dll"));
        string tempOut = Path.Combine(Path.GetTempPath(), $"SfWalk_{Guid.NewGuid():N}.dll");
        try
        {
            WfWasm.Rewrite.AssemblyRewriter.Rewrite(
                input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            using var module = AssemblyDefinition.ReadAssembly(tempOut).MainModule;
            var utils = module.GetType("Syncfusion.Windows.Forms.PopupUtils");
            Assert.NotNull(utils);
            var target = utils.Methods.FirstOrDefault(m =>
                m.Name == "GetADotNetParentControl" && m.IsStatic && m.HasBody && m.Parameters.Count == 1);
            Assert.NotNull(target);
            bool sawGetParent = false;
            bool sawFromHandle = false;
            bool sawCap = false;
            bool sawCapExit = false;
            foreach (var ins in target.Body.Instructions)
            {
                if (ins.Operand is MethodReference mr)
                {
                    if (mr.Name == "GetParent" && mr.Parameters.Count == 1)
                        sawGetParent = true;
                    if (mr.Name == "FromHandle" && mr.Parameters.Count == 1)
                        sawFromHandle = true;
                }
                if (ins.OpCode == OpCodes.Ldc_I4 && ins.Operand is int n && n == 256)
                    sawCap = true;
                if ((ins.OpCode == OpCodes.Bge || ins.OpCode == OpCodes.Bge_S)
                    && ins.Operand is Instruction)
                    sawCapExit = true;
                if (ins.OpCode.FlowControl == FlowControl.Cond_Branch
                    && ins.Operand is Instruction t
                    && target.Body.Instructions.IndexOf(t) < target.Body.Instructions.IndexOf(ins)
                    && (ins.OpCode == OpCodes.Bne_Un || ins.OpCode == OpCodes.Bne_Un_S))
                    Assert.Fail("Unbounded loop back-edge survives in " + target.FullName);
            }
            Assert.True(sawGetParent, "GetParent dispatch lost in " + target.FullName);
            Assert.True(sawFromHandle, "FromHandle dispatch lost in " + target.FullName);
            Assert.True(sawCap, "256 step cap missing in " + target.FullName);
            Assert.True(sawCapExit, "cap exit branch missing in " + target.FullName);
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
