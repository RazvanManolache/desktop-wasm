using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

// Pins GuardPdnDropDownListWndProc against the real Framework binary
// (targets/ read-only: copied to temp first). Dispose(bool) frees the
// animation handler before base.Dispose destroys the window, so the
// destroy message NREs on the null field (served w8 File>New teardown:
// one caught PdnDropDownList.WndProc NRE per dropdown destroyed). The
// guard inserts a null-check prologue that falls through to base.WndProc
// when freed; the relay/fast path is otherwise untouched.
public sealed class PdnDropDownListWndProcGuardTests
{
    [Fact]
    public void Guard_Real_DropDownListWndProc_NullChecks_Handler()
    {
        string src = @"E:\AI\Code\WindowsForms\targets\paint.net\PaintDotNet.Framework.dll";
        string tmp = Path.Combine(Path.GetTempPath(), $"PdnDropDown_{Guid.NewGuid()}.dll");
        File.Copy(src, tmp);
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            AssemblyRewriter.GuardPdnDropDownListWndProc(read.MainModule);

            var m = read.MainModule.GetType("PaintDotNet.Controls.PdnDropDownList").Methods
                .First(x => x.Name == "WndProc");
            var instrs = m.Body.Instructions;
            Assert.True(instrs.Count >= 11, $"unexpected length {instrs.Count}");
            Assert.Equal(
                new[]
                {
                    OpCodes.Ldarg_0, OpCodes.Ldfld, OpCodes.Brtrue_S,
                    OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Call, OpCodes.Ret,
                    OpCodes.Ldarg_0,
                },
                instrs.Take(8).Select(i => i.OpCode).ToList());
            // The guard branches back to the original prologue.
            Assert.Same(instrs[7], instrs[2].Operand);
            // The null path dispatches the base WndProc (not a self call).
            var baseCall = (MethodReference)instrs[5].Operand;
            Assert.Equal("WndProc", baseCall.Name);
            Assert.NotEqual("PaintDotNet.Controls.PdnDropDownList", baseCall.DeclaringType.FullName);
            // The guarded field is the freed animation handler.
            var fld = (FieldReference)instrs[1].Operand;
            Assert.Equal("animationHandler", fld.Name);
            // The relay call survives on the non-null path.
            var calls = instrs
                .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                .Select(i => (MethodReference)i.Operand)
                .ToList();
            Assert.Single(calls, c => c.Name == "RelayWndProc");
            // Two base dispatches: the inserted null-path call plus the
            // original relay-false call; neither is a self call.
            var baseCalls = calls.Where(c => c.Name == "WndProc").ToList();
            Assert.Equal(2, baseCalls.Count);
            Assert.All(baseCalls, c => Assert.NotEqual(
                "PaintDotNet.Controls.PdnDropDownList", c.DeclaringType.FullName));
            // Every instance call observes a pushed object reference.
            for (int i = 0; i < instrs.Count; i++)
            {
                if ((instrs[i].OpCode == OpCodes.Call || instrs[i].OpCode == OpCodes.Callvirt)
                    && instrs[i].Operand is MethodReference mr && mr.HasThis && mr.Name != ".ctor")
                {
                    bool pushed = false;
                    for (int j = Math.Max(0, i - 4); j < i; j++)
                    {
                        var o = instrs[j].OpCode;
                        if (o == OpCodes.Ldarg_0 || o == OpCodes.Ldarg_1
                            || o == OpCodes.Call || o == OpCodes.Callvirt || o == OpCodes.Newobj
                            || o == OpCodes.Ldnull || o == OpCodes.Dup
                            || o == OpCodes.Ldfld || o == OpCodes.Ldsfld)
                        {
                            pushed = true;
                            break;
                        }
                    }
                    Assert.True(pushed, $"no instance for {mr.DeclaringType.Name}::{mr.Name} at {i}");
                }
            }
            Assert.Equal(OpCodes.Ret, instrs.Last().OpCode);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public void Guard_Rejects_Double_Application()
    {
        string src = @"E:\AI\Code\WindowsForms\targets\paint.net\PaintDotNet.Framework.dll";
        string tmp = Path.Combine(Path.GetTempPath(), $"PdnDropDown_{Guid.NewGuid()}.dll");
        File.Copy(src, tmp);
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            AssemblyRewriter.GuardPdnDropDownListWndProc(read.MainModule);
            Assert.Throws<InvalidOperationException>(
                () => AssemblyRewriter.GuardPdnDropDownListWndProc(read.MainModule));
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
