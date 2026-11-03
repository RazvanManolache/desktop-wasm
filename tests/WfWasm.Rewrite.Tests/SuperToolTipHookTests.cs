using Mono.Cecil;
using Mono.Cecil.Cil;

// SF08 wall 2 (SF1120-CRASHQA-SF0809): Word boot dies in
// SuperToolTip.OnToolTipControlHandleCreated at
// Process.GetCurrentProcess() (PNSE headless) while installing a Win32
// mouse hook; the aborted WmCreate cascades to the terminal Invalid
// window handle. The managed AddMessageFilter path already delivers
// tooltips, so the rule keeps it and drops only the native hook
// install (nhook = IntPtr.Zero). This pins the shipped Word input:
// no SetWindowsHookEx/GetCurrentProcess call survives in the method,
// and the AddMessageFilter prologue is intact.
public sealed class SuperToolTipHookTests
{
    [Fact]
    public void Hook_Install_Dropped_Filter_Kept()
    {
        string input = @"E:\AI\Code\DesktopWasm\inputs\winforms\syncfusion-staging\syncfusion-word\Syncfusion.Tools.Windows.dll";
        Assert.True(File.Exists(input), "Word staging input missing: " + input);
        string cpuDll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "WfWasm.PdnCpu", "bin", "Debug", "net9.0", "WfWasm.PdnCpu.dll"));
        string tempOut = Path.Combine(Path.GetTempPath(), $"SfHook_{Guid.NewGuid():N}.dll");
        try
        {
            WfWasm.Rewrite.AssemblyRewriter.Rewrite(
                input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            using var module = AssemblyDefinition.ReadAssembly(tempOut).MainModule;
            var tip = module.GetType("Syncfusion.Windows.Forms.Tools.SuperToolTip");
            Assert.NotNull(tip);
            var target = tip.Methods.FirstOrDefault(m =>
                m.Name == "OnToolTipControlHandleCreated" && m.HasBody && m.Parameters.Count == 2);
            Assert.NotNull(target);
            bool sawFilter = false;
            foreach (var ins in target.Body.Instructions)
            {
                if (ins.Operand is MethodReference mr)
                {
                    Assert.False(mr.Name == "SetWindowsHookEx",
                        "SetWindowsHookEx survives in " + target.FullName);
                    Assert.False(mr.DeclaringType.FullName == "System.Diagnostics.Process"
                        && mr.Name == "GetCurrentProcess",
                        "GetCurrentProcess survives in " + target.FullName);
                    if (mr.Name == "AddMessageFilter")
                        sawFilter = true;
                }
            }
            Assert.True(sawFilter, "AddMessageFilter prologue lost in " + target.FullName);
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
