using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;

// Pins FixInputLanguageZeroHkl against the real fork binary
// (src build output, read-only: copied to temp first): with no keyboard
// layout headless, HKL is 0 and get_LanguageTag dies in
// CultureInfo.GetCultureInfo(0) (ArgumentOutOfRange 'culture ('0')'),
// which kills every printable-key KeyPress (Syncfusion editor probe).
// The fix prefixes a zero-handle branch answering CurrentUICulture;
// the nonzero path stays byte-identical. Red-first: the CurrentUICulture
// call is absent pre-fix, so the shape assert fails until the fix is cut.
public sealed class InputLanguageZeroHklTests
{
    private static string ForkBytes()
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "WfWasm.WinForms", "bin", "Debug", "net9.0",
            "WfWasm.WinForms.dll"));
        Assert.True(File.Exists(dll), $"fork not built: {dll}");
        string tmp = Path.Combine(Path.GetTempPath(), $"InputLang_{Guid.NewGuid()}.dll");
        File.Copy(dll, tmp);
        return tmp;
    }

    private static MethodDefinition LanguageTag(ModuleDefinition module)
    {
        var lang = module.GetType("System.Windows.Forms.InputLanguage");
        Assert.NotNull(lang);
        return Assert.Single(lang.Methods, m =>
            m.Name == "get_LanguageTag" && m.HasBody && m.Parameters.Count == 0);
    }

    [Fact]
    public void Fix_Reroutes_Zero_Handle_To_CurrentUICulture()
    {
        string tmp = ForkBytes();
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            AssemblyRewriter.FixInputLanguageZeroHkl(read.MainModule);

            var m = LanguageTag(read.MainModule);
            var instrs = m.Body.Instructions.ToList();
            Assert.True(instrs.Count >= 6);
            Assert.Equal(OpCodes.Ldarg_0, instrs[0].OpCode);
            Assert.Equal(OpCodes.Ldfld, instrs[1].OpCode);
            Assert.Equal(OpCodes.Brtrue_S, instrs[2].OpCode);
            var origFirst = instrs[2].Operand as Instruction;
            Assert.NotNull(origFirst);
            Assert.Equal(origFirst, instrs[6]);
            var calls = instrs
                .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                    && i.Operand is MethodReference)
                .Select(i => (MethodReference)i.Operand)
                .ToList();
            Assert.Equal(OpCodes.Call, instrs[3].OpCode);
            Assert.Equal("get_CurrentUICulture",
                ((MethodReference)instrs[3].Operand).Name);
            Assert.Equal("System.Globalization.CultureInfo",
                ((MethodReference)instrs[3].Operand).DeclaringType.FullName);
            Assert.Equal(OpCodes.Callvirt, instrs[4].OpCode);
            Assert.Equal("get_Name",
                ((MethodReference)instrs[4].Operand).Name);
            Assert.Equal(OpCodes.Ret, instrs[5].OpCode);
            // Nonzero path untouched: pristine GetCultureInfo(int) still present.
            Assert.Single(calls, r => r.Name == "GetCultureInfo"
                && r.Parameters.Count == 1
                && r.Parameters[0].ParameterType.FullName == "System.Int32");
            Assert.Equal(OpCodes.Ret, m.Body.Instructions.Last().OpCode);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public void Fix_Rejects_Double_Application()
    {
        string tmp = ForkBytes();
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            AssemblyRewriter.FixInputLanguageZeroHkl(read.MainModule);
            Assert.Throws<InvalidOperationException>(
                () => AssemblyRewriter.FixInputLanguageZeroHkl(read.MainModule));
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
