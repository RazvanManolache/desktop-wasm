using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

// Pins RepairCardLayoutReverseLookup (rev332 PSTALL): served
// CardLayout.set_SelectedCard can complete the visible switch while the
// reverse controlsVsCardNames entry stays missing, so the next
// get_SelectedCard misses, Wizard.get_SelectedPage answers null,
// OnAfterPageSelect skips RaisePageLoad and the page timer never arms.
// The repair writes controlsVsCardNames[V_0] = value on the success
// path (V_0 non-null, right before MakeDirty).
public sealed class CardLayoutReverseLookupTests
{
    // TMPMOVE canonical-only: fixtures under retained/.
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static string RealDll
    {
        get
        {
            var rel = Path.Combine("wfwasm-syncfusion-apps", "syncfusion-calendar", "Syncfusion.Shared.Base.dll");
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

    private static void PatchAndWrite(string dstOut)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"CardPSTALL_{Guid.NewGuid()}.dll");
        File.Copy(RealDll, tmp, overwrite: true);
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp,
                new ReaderParameters { AssemblyResolver = FxResolver() });
            Assert.Equal(1, AssemblyRewriter.RepairCardLayoutReverseLookup(read.MainModule));
            var setter = read.MainModule.GetType("Syncfusion.Windows.Forms.Tools.CardLayout").Methods
                .First(m => m.Name == "set_SelectedCard" && m.Parameters.Count == 1);
            Assert.Equal(64 + 7, setter.Body.Instructions.Count);
            // Tail order: repair, then the original ldarg.0/call MakeDirty/ret.
            var instrs = setter.Body.Instructions;
            Assert.Equal(OpCodes.Ret, instrs[^1].OpCode);
            Assert.Equal(OpCodes.Call, instrs[^2].OpCode);
            Assert.Equal("MakeDirty", ((MethodReference)instrs[^2].Operand).Name);
            Assert.Equal(OpCodes.Ldarg_0, instrs[^3].OpCode);
            Assert.Equal(OpCodes.Callvirt, instrs[^4].OpCode);
            var setItem = (MethodReference)instrs[^4].Operand;
            Assert.Equal("set_Item", setItem.Name);
            Assert.Equal("System.Collections.Hashtable", setItem.DeclaringType.FullName);
            // The repair is guarded: V_0 null skips the write (no null-key throw).
            Assert.Contains(instrs, i =>
                (i.OpCode == OpCodes.Brfalse_S || i.OpCode == OpCodes.Brfalse)
                && ReferenceEquals(i.Operand, instrs[^3]));
            // Single MakeDirty call site preserved.
            Assert.Single(instrs, i => i.Operand is MethodReference mr && mr.Name == "MakeDirty");
            // Served-verified shape: every short branch fits its sbyte
            // window (the 0037 beq.s -> ret overflow died served as
            // VerificationException: invalid instruction target).
            var offsets = new System.Collections.Generic.Dictionary<Instruction, int>();
            int pos = 0;
            foreach (var ins in instrs)
            {
                offsets[ins] = pos;
                pos += ins.GetSize();
            }
            foreach (var i in instrs)
            {
                string name = i.OpCode.ToString();
                if (name.EndsWith(".s", StringComparison.Ordinal)
                    && i.Operand is Instruction target)
                {
                    int dist = offsets[target] - (offsets[i] + i.GetSize());
                    Assert.True(dist >= sbyte.MinValue && dist <= sbyte.MaxValue,
                        $"short branch {name} overflows its sbyte window");
                }
            }
            // The old ret-targeting branch is long form now.
            var retTargeter = instrs.First(
                i => i.Operand is Instruction t && instrs.IndexOf(t) == instrs.Count - 1);
            Assert.Equal(OpCodes.Beq, retTargeter.OpCode);
            read.Write(dstOut);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public void Repair_Real_DLL_Patches_Once_Then_Noop()
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"CardPSTALL_{Guid.NewGuid()}.dll");
        try
        {
            using (var read = AssemblyDefinition.ReadAssembly(RealDll))
            {
                // Sanity: pristine setter has the exact audited shape.
                var pristine = read.MainModule.GetType("Syncfusion.Windows.Forms.Tools.CardLayout").Methods
                    .First(m => m.Name == "set_SelectedCard" && m.Parameters.Count == 1);
                Assert.Equal(64, pristine.Body.Instructions.Count);
                Assert.Empty(pristine.Body.ExceptionHandlers);
            }
            File.Copy(RealDll, tmp, overwrite: true);
            using (var read = AssemblyDefinition.ReadAssembly(tmp))
            {
                Assert.Equal(1, AssemblyRewriter.RepairCardLayoutReverseLookup(read.MainModule));
                var setter = read.MainModule.GetType("Syncfusion.Windows.Forms.Tools.CardLayout").Methods
                    .First(m => m.Name == "set_SelectedCard" && m.Parameters.Count == 1);
                Assert.Equal(71, setter.Body.Instructions.Count);
                Assert.Equal(0, AssemblyRewriter.RepairCardLayoutReverseLookup(read.MainModule));
                Assert.Equal(71, setter.Body.Instructions.Count);
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
        string outTmp = Path.Combine(Path.GetTempPath(), $"CardPSTALL_{Guid.NewGuid()}.out.dll");
        try
        {
            PatchAndWrite(outTmp);
            using var reread = AssemblyDefinition.ReadAssembly(outTmp);
            var setter = reread.MainModule.GetType("Syncfusion.Windows.Forms.Tools.CardLayout").Methods
                .First(m => m.Name == "set_SelectedCard" && m.Parameters.Count == 1);
            Assert.Equal(71, setter.Body.Instructions.Count);
            Assert.Equal(OpCodes.Ret, setter.Body.Instructions[^1].OpCode);
        }
        finally
        {
            try { File.Delete(outTmp); } catch { }
        }
    }

    private static string BuildSynthetic(bool withType, bool withSetter)
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"CardSynth_{Guid.NewGuid()}.dll");
        var asmName = new AssemblyNameDefinition("CardSynth", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "CardSynth", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            if (withType)
            {
                var layout = new TypeDefinition("Syncfusion.Windows.Forms.Tools", "CardLayout",
                    Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                    module.TypeSystem.Object);
                module.Types.Add(layout);
                if (withSetter)
                {
                    var setter = new MethodDefinition("set_SelectedCard",
                        Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig,
                        module.TypeSystem.Void);
                    setter.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                    layout.Methods.Add(setter);
                }
            }
            asm.Write(tempOut);
        }
        return tempOut;
    }

    [Fact]
    public void Repair_Missing_CardLayout_Throws()
    {
        string tmp = BuildSynthetic(withType: false, withSetter: false);
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            Assert.Throws<InvalidOperationException>(
                () => AssemblyRewriter.RepairCardLayoutReverseLookup(read.MainModule));
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
                () => AssemblyRewriter.RepairCardLayoutReverseLookup(read.MainModule));
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
