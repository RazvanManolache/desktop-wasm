using Mono.Cecil;
using Mono.Cecil.Cil;

// SF09 (gen16 wordassign/sf08 reboot): VisualStudioDemo.Form1..ctor loads
// its own demo sources into two EditControls via baked backslash-relative
// paths (GetDirectoryName(ExecutablePath) + "\..\..\..\Form1.cs" /
// "\..\..\..\Program.cs" -> EditControl.LoadFile). Headless the files do
// not exist and LoadFile throws FileNotFoundException, stashed at
// form-create. The sources are demo chrome (a source viewer), not docking
// function, so the rule skips the two loads: pop instance+path, push
// false. This pins the shipped docking demo input: no LoadFile callvirt
// remains in Form1..ctor and each former site is pop/pop/ldc.i4.0.
public sealed class DemoSourceViewerNeutralizeTests
{
    [Fact]
    public void Form1Ctor_SourceViewer_Loads_Skipped()
    {
        string input = @"E:\AI\Code\DesktopWasm\inputs\winforms\syncfusion-staging\syncfusion-docking\VisualStudioDemo.dll";
        Assert.True(File.Exists(input), "Docking demo input missing: " + input);
        string cpuDll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "WfWasm.PdnCpu", "bin", "Debug", "net9.0", "WfWasm.PdnCpu.dll"));
        string tempOut = Path.Combine(Path.GetTempPath(), $"SfSrc_{Guid.NewGuid():N}.dll");
        try
        {
            WfWasm.Rewrite.AssemblyRewriter.Rewrite(
                input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            using var module = AssemblyDefinition.ReadAssembly(tempOut).MainModule;
            var form = module.GetType("VisualStudioDemo.Form1");
            Assert.NotNull(form);
            var ctor = form.Methods.FirstOrDefault(m =>
                m.Name == ".ctor" && m.Parameters.Count == 0 && m.HasBody);
            Assert.NotNull(ctor);
            foreach (var ins in ctor.Body.Instructions)
            {
                if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                    && ins.Operand is MethodReference mr && mr.Name == "LoadFile"
                    && mr.DeclaringType.FullName == "Syncfusion.Windows.Forms.Edit.EditControl")
                {
                    Assert.Fail("Unneutralized EditControl.LoadFile at " + ins.Offset.ToString("X4"));
                }
            }
            int skips = 0;
            var list = ctor.Body.Instructions;
            for (int i = 0; i + 2 < list.Count; i++)
            {
                if (list[i].OpCode == OpCodes.Pop
                    && list[i + 1].OpCode == OpCodes.Pop
                    && list[i + 2].OpCode == OpCodes.Ldc_I4_0)
                {
                    skips++;
                }
            }
            Assert.True(skips >= 2, "Expected both source-viewer skips, found " + skips);
            foreach (var ins in ctor.Body.Instructions)
            {
                if (ins.Operand is Mono.Cecil.Cil.Instruction)
                    Assert.NotNull(ins.Operand);
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
