using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;

// Pins the Syncfusion press-chain culture-0 fix (RT-REGATE-W15 c1 FAIL):
// headless answers HKL 0, so InputLanguage.get_Culture() dies in
// GetCultureInfo(0) inside StreamEditControl.OnKeyPress (it-IT IME branch)
// and ControlStateStore (ar RTL branches), killing every printable-key
// KeyPress with a TIE. The Syncfusion assemblies pass through the
// per-request rewrite, so the call sites are answered with
// CurrentUICulture (consumed only by the name test that follows).
// Pristine shape first: exactly 3 get_Culture name-test sites.
public sealed class SyncfusionInputCultureTests
{
    private static string EditBytes()
    {
        string src = @"E:\AI\Code\DesktopWasm\.artifacts\tmp\retained\wfwasm-syncfusion-apps\syncfusion-docking\Syncfusion.Edit.Windows.dll";
        string tmp = Path.Combine(Path.GetTempPath(), $"SyncInputCulture_{Guid.NewGuid():N}.dll");
        File.Copy(src, tmp);
        return tmp;
    }

    private static int CountCalls(ModuleDefinition module, string type, string name)
    {
        int n = 0;
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            foreach (var m in t.Methods)
            {
                if (!m.HasBody) continue;
                foreach (var i in m.Body.Instructions)
                {
                    if ((i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                        && i.Operand is MethodReference mr
                        && mr.Name == name
                        && mr.DeclaringType.FullName == type)
                        n++;
                }
            }
            foreach (var nt in t.NestedTypes)
                stack.Push(nt);
        }
        return n;
    }

    [Fact]
    public void Pristine_Carries_Three_InputCulture_NameTest_Sites()
    {
        string tmp = EditBytes();
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            Assert.Equal(3, CountCalls(read.MainModule,
                "System.Windows.Forms.InputLanguage", "get_Culture"));
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public void Reroute_Answers_CurrentUICulture_At_NameTest_Sites()
    {
        string tmp = EditBytes();
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            int preName = CountCalls(read.MainModule,
                "System.Globalization.CultureInfo", "get_Name");
            int prePrologue = CountCalls(read.MainModule,
                "System.Windows.Forms.InputLanguage", "get_CurrentInputLanguage");
            int count = AssemblyRewriter.RetargetSyncfusionInputCulture(read.MainModule);
            Assert.Equal(3, count);
            // The throwing get_Culture dispatch is gone; the name-test
            // consumers and the prologue calls stay byte-identical.
            Assert.Equal(0, CountCalls(read.MainModule,
                "System.Windows.Forms.InputLanguage", "get_Culture"));
            Assert.Equal(3, CountCalls(read.MainModule,
                "System.Globalization.CultureInfo", "get_CurrentUICulture"));
            Assert.Equal(preName, CountCalls(read.MainModule,
                "System.Globalization.CultureInfo", "get_Name"));
            Assert.Equal(prePrologue, CountCalls(read.MainModule,
                "System.Windows.Forms.InputLanguage", "get_CurrentInputLanguage"));
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public void Reroute_Rejects_Double_Application()
    {
        string tmp = EditBytes();
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            AssemblyRewriter.RetargetSyncfusionInputCulture(read.MainModule);
            Assert.Throws<InvalidOperationException>(
                () => AssemblyRewriter.RetargetSyncfusionInputCulture(read.MainModule));
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
