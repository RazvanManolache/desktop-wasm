using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

// Pins RepairWizardSelectedPageFallback (rev333 PSTALL): served,
// Wizard.set_SelectedPage builds the AfterPageSelect args from a
// get_SelectedPage() read-back that can miss while the requested page
// is visibly presented, so OnAfterPageSelect skips RaisePageLoad and
// the page timer never arms. When the read-back is null the args fall
// back to the requested page (BeforePageSelect already carries it).
public sealed class WizardSelectedPageFallbackTests
{
    // TMPMOVE canonical-only: fixtures under retained/.
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static string RealDll
    {
        get
        {
            var rel = Path.Combine("wfwasm-syncfusion-apps", "syncfusion-auto-complete", "Syncfusion.Tools.Windows.dll");
            var canon = Path.Combine(RepoRoot, ".artifacts", "tmp", "retained", rel);
            return canon;
        }
    }

    private static DefaultAssemblyResolver FxResolver()
    {
        var resolver = new DefaultAssemblyResolver();
        string shared = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet", "shared");
        foreach (string fw in new[] { "Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App" })
        {
            string root = Path.Combine(shared, fw);
            if (Directory.Exists(root))
                foreach (string dir in Directory.GetDirectories(root))
                    resolver.AddSearchDirectory(dir);
        }
        return resolver;
    }

    private static MethodDefinition PatchAndWrite(string dstOut)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"WizPSTALL_{Guid.NewGuid()}.dll");
        File.Copy(RealDll, tmp, overwrite: true);
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp,
                new ReaderParameters { AssemblyResolver = FxResolver() });
            int before = read.MainModule.GetType("Syncfusion.Windows.Forms.Tools.Wizard").Methods
                .First(m => m.Name == "set_SelectedPage" && m.Parameters.Count == 1)
                .Body.Instructions.Count;
            Assert.Equal(1, AssemblyRewriter.RepairWizardSelectedPageFallback(read.MainModule));
            var setter = read.MainModule.GetType("Syncfusion.Windows.Forms.Tools.Wizard").Methods
                .First(m => m.Name == "set_SelectedPage" && m.Parameters.Count == 1);
            var instrs = setter.Body.Instructions;
            Assert.Equal(before + 4, instrs.Count);
            // Fallback shape directly before the AfterPageSelect args newobj:
            // dup / brtrue(newobj) / pop / ldarg.1 / newobj.
            // Post-patch anchor: the args newobj preceded by the fallback
            // (dup / brtrue(newobj) / pop / ldarg.1) atop the read-back call.
            int ni = -1;
            for (int k = 5; k < instrs.Count; k++)
            {
                if (instrs[k].OpCode == OpCodes.Newobj
                    && instrs[k].Operand is MethodReference nmr
                    && nmr.DeclaringType.FullName == "Syncfusion.Windows.Forms.Tools.WizardPageSelectEventArgs"
                    && instrs[k - 1].OpCode == OpCodes.Ldarg_1
                    && instrs[k - 2].OpCode == OpCodes.Pop
                    && (instrs[k - 3].OpCode == OpCodes.Brtrue_S
                        || instrs[k - 3].OpCode == OpCodes.Brtrue)
                    && ReferenceEquals(instrs[k - 3].Operand, instrs[k])
                    && instrs[k - 4].OpCode == OpCodes.Dup
                    && instrs[k - 5].OpCode == OpCodes.Callvirt
                    && instrs[k - 5].Operand is MethodReference gsp
                    && gsp.Name == "get_SelectedPage")
                    ni = k;
            }
            Assert.True(ni > 5);
            read.Write(dstOut);
            return setter;
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public void Repair_Real_DLL_Patches_Once_Then_Noop()
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"WizPSTALL_{Guid.NewGuid()}.dll");
        try
        {
            File.Copy(RealDll, tmp, overwrite: true);
            using (var read = AssemblyDefinition.ReadAssembly(tmp,
                new ReaderParameters { AssemblyResolver = FxResolver() }))
            {
                var setter = read.MainModule.GetType("Syncfusion.Windows.Forms.Tools.Wizard").Methods
                    .First(m => m.Name == "set_SelectedPage" && m.Parameters.Count == 1);
                int before = setter.Body.Instructions.Count;
                Assert.Empty(setter.Body.ExceptionHandlers);
                Assert.Equal(1, AssemblyRewriter.RepairWizardSelectedPageFallback(read.MainModule));
                Assert.Equal(before + 4, setter.Body.Instructions.Count);
                // Fallback shape: dup / brtrue(args newobj) / pop / ldarg.1
                // atop the read-back call, directly before the args newobj.
                var instrs = setter.Body.Instructions;
                int ni = -1;
                for (int k = 5; k < instrs.Count; k++)
                {
                    if (instrs[k].OpCode == OpCodes.Newobj
                        && instrs[k].Operand is MethodReference nmr
                        && nmr.DeclaringType.FullName == "Syncfusion.Windows.Forms.Tools.WizardPageSelectEventArgs"
                        && instrs[k - 1].OpCode == OpCodes.Ldarg_1
                        && instrs[k - 2].OpCode == OpCodes.Pop
                        && (instrs[k - 3].OpCode == OpCodes.Brtrue_S
                            || instrs[k - 3].OpCode == OpCodes.Brtrue)
                        && ReferenceEquals(instrs[k - 3].Operand, instrs[k])
                        && instrs[k - 4].OpCode == OpCodes.Dup
                        && instrs[k - 5].OpCode == OpCodes.Callvirt
                        && instrs[k - 5].Operand is MethodReference gsp
                        && gsp.Name == "get_SelectedPage")
                        ni = k;
                }
                Assert.True(ni > 5);
                Assert.Equal(0, AssemblyRewriter.RepairWizardSelectedPageFallback(read.MainModule));
                Assert.Equal(before + 4, setter.Body.Instructions.Count);
            }
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public void Repair_Real_DLL_RoundTrips_Through_Write()
    {
        string outTmp = Path.Combine(Path.GetTempPath(), $"WizPSTALL_{Guid.NewGuid()}.out.dll");
        try
        {
            var setter = PatchAndWrite(outTmp);
            _ = setter;
            using var reread = AssemblyDefinition.ReadAssembly(outTmp,
                new ReaderParameters { AssemblyResolver = FxResolver() });
            var target = reread.MainModule.GetType("Syncfusion.Windows.Forms.Tools.Wizard").Methods
                .First(m => m.Name == "set_SelectedPage" && m.Parameters.Count == 1);
            Assert.EndsWith("ret", target.Body.Instructions[^1].OpCode.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(outTmp); } catch { }
        }
    }

    private static string BuildSynthetic(bool withType, bool withSetter)
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"WizSynth_{Guid.NewGuid()}.dll");
        var asmName = new AssemblyNameDefinition("WizSynth", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "WizSynth", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            if (withType)
            {
                var wizard = new TypeDefinition("Syncfusion.Windows.Forms.Tools", "Wizard",
                    Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                    module.TypeSystem.Object);
                module.Types.Add(wizard);
                if (withSetter)
                {
                    var page = new TypeDefinition("Syncfusion.Windows.Forms.Tools", "WizardPage",
                        Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                        module.TypeSystem.Object);
                    module.Types.Add(page);
                    var setter = new MethodDefinition("set_SelectedPage",
                        Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig,
                        module.TypeSystem.Void);
                    setter.Parameters.Add(new ParameterDefinition(page));
                    wizard.Methods.Add(setter);
                }
            }
            asm.Write(tempOut);
        }
        return tempOut;
    }

    [Fact]
    public void Repair_Missing_Wizard_Throws()
    {
        string tmp = BuildSynthetic(withType: false, withSetter: false);
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            Assert.Throws<InvalidOperationException>(
                () => AssemblyRewriter.RepairWizardSelectedPageFallback(read.MainModule));
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public void Repair_Missing_Setter_Throws()
    {
        string tmp = BuildSynthetic(withType: true, withSetter: false);
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            Assert.Throws<InvalidOperationException>(
                () => AssemblyRewriter.RepairWizardSelectedPageFallback(read.MainModule));
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
