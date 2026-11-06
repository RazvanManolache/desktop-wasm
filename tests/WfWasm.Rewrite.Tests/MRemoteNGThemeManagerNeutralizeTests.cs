using Mono.Cecil;
using Mono.Cecil.Cil;

// RVW3293-RT2 (dw-runtime): mRemoteNG w27/w28 wall — ThemeManager.getInstance()
// calls IsDesignTimeHost(), which calls Process.GetCurrentProcess() (PNSE
// headless), aborting FrmMain construction into a blank window (served:
// [CTOR-GUARD] PNSE after PresentationCore FNF, Controls=0 + follow-on NREs).
// The cure forces the vendor's own design-time stub (no file I/O, no Process,
// theming inactive): it exists precisely for contexts without native services.
// Served proof rides the next regen; this file pins the transform contract on
// a Cecil-synthesized fixture (hermetic: no input-DLL or lane-tmp coupling)
// plus a desktop behavioral proof (pre-transform false on testhost, true
// after the arm) and a full-pipeline dispatch proof for assembly mRemoteNG.
public sealed class MRemoteNGThemeManagerNeutralizeTests
{
    private const string TargetType = "mRemoteNG.Themes.ThemeManager";
    private const string TargetMethod = "IsDesignTimeHost";

    private static string BuildFixture(string path, bool withProcessProbe)
    {
        var asmName = new AssemblyNameDefinition("mRemoteNG", new Version(1, 0, 0, 0));
        var asm = AssemblyDefinition.CreateAssembly(asmName, "mRemoteNG", ModuleKind.Dll);
        var module = asm.MainModule;
        var getProcess = module.ImportReference(
            typeof(System.Diagnostics.Process).GetMethod("GetCurrentProcess",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null, Type.EmptyTypes, null)!);
        var getName = module.ImportReference(
            typeof(System.Diagnostics.Process).GetProperty("ProcessName")!.GetMethod!);
        var strEquals = module.ImportReference(
            typeof(string).GetMethod("Equals",
                new[] { typeof(string), typeof(string) })!);

        var type = new TypeDefinition("mRemoteNG.Themes", "ThemeManager",
            TypeAttributes.Public | TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(type);

        // Mirrors the served shape: private static bool, no params; probes
        // the current process name unless the fixture omits the probe
        // (wrong-shape negative).
        var method = new MethodDefinition(TargetMethod,
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.TypeSystem.Boolean);
        {
            var il = method.Body.GetILProcessor();
            method.Body.InitLocals = true;
            method.Body.MaxStackSize = 8;
            if (withProcessProbe)
            {
                il.Emit(OpCodes.Call, getProcess);
                il.Emit(OpCodes.Callvirt, getName);
                il.Emit(OpCodes.Ldstr, "devenv");
                il.Emit(OpCodes.Call, strEquals);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4_0);
            }
            il.Emit(OpCodes.Ret);
            type.Methods.Add(method);
        }

        asm.Write(path);
        return path;
    }

    private static void AssertBranchTargetsValid(MethodDefinition m)
    {
        var set = new HashSet<Instruction>(m.Body.Instructions);
        foreach (var ins in m.Body.Instructions)
        {
            if (ins.Operand is Instruction t)
                Assert.True(set.Contains(t), $"dangling branch in {m.Name}");
            else if (ins.Operand is Instruction[] arr)
                foreach (var x in arr) Assert.True(set.Contains(x), $"dangling switch in {m.Name}");
        }
    }

    [Fact]
    public void IsDesignTimeHost_Neutralized_To_True()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"mrntheme_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string input = Path.Combine(dir, "ThemeFixture.dll");
        string output = Path.Combine(dir, "ThemeFixture.out.dll");
        try
        {
            BuildFixture(input, withProcessProbe: true);
            // Fixture validity gate: untransformed returns false on desktop
            // (testhost process name), proving a later true comes from the
            // arm, not the synthesis.
            {
                var asmPre = System.Reflection.Assembly.Load(File.ReadAllBytes(input));
                var tPre = asmPre.GetType(TargetType)!;
                var mPre = tPre.GetMethod(TargetMethod,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                Assert.False((bool)mPre.Invoke(null, null)!);
            }
            using (var module = ModuleDefinition.ReadModule(input))
            {
                int sites = WfWasm.Rewrite.AssemblyRewriter
                    .NeutralizeMRemoteNGThemeManagerDesignTime(module);
                Assert.Equal(1, sites);
                var type = module.GetType(TargetType);
                Assert.NotNull(type);
                var method = type.Methods.First(m => m.Name == TargetMethod);
                Assert.Equal(2, method.Body.Instructions.Count);
                Assert.Equal(Code.Ldc_I4_1, method.Body.Instructions[0].OpCode.Code);
                Assert.Equal(Code.Ret, method.Body.Instructions[1].OpCode.Code);
                Assert.Empty(method.Body.ExceptionHandlers);
                AssertBranchTargetsValid(method);
                module.Write(output);
            }
            using (var re = ModuleDefinition.ReadModule(output))
            {
                var type = re.GetType(TargetType);
                Assert.NotNull(type);
                var method = type.Methods.First(m => m.Name == TargetMethod);
                Assert.Equal(2, method.Body.Instructions.Count);
                AssertBranchTargetsValid(method);
            }
            // Behavioral proof on desktop: neutralized body answers true.
            var asmLoaded = System.Reflection.Assembly.Load(File.ReadAllBytes(output));
            var tLoaded = asmLoaded.GetType(TargetType)!;
            Assert.NotNull(tLoaded);
            var mLoaded = tLoaded.GetMethod(TargetMethod,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            Assert.True((bool)mLoaded.Invoke(null, null)!);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void FullRewrite_MRemoteNG_Applies_ThemeManager_Seam()
    {
        // Dispatch proof: the arm must fire through the real Rewrite
        // pipeline for assembly name mRemoteNG (a typo'd gate would
        // silently skip in prod and the direct unit test would not catch
        // it). Fixture assembly is already named mRemoteNG.
        string dir = Path.Combine(Path.GetTempPath(), $"mrntheme_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string input = Path.Combine(dir, "mRemoteNG.dll");
        string output = Path.Combine(dir, "mRemoteNG.out.dll");
        try
        {
            BuildFixture(input, withProcessProbe: true);
            string cpuDll = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "WfWasm.PdnCpu", "bin", "Debug", "net9.0", "WfWasm.PdnCpu.dll"));
            WfWasm.Rewrite.AssemblyRewriter.Rewrite(
                input, output,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            using var module = ModuleDefinition.ReadModule(output);
            var type = module.GetType(TargetType);
            Assert.NotNull(type);
            var method = type.Methods.First(m => m.Name == TargetMethod);
            Assert.Equal(2, method.Body.Instructions.Count);
            Assert.Equal(Code.Ldc_I4_1, method.Body.Instructions[0].OpCode.Code);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Missing_ThemeManager_Fails_Loud()
    {
        var asmName = new AssemblyNameDefinition("mRemoteNG", new Version(1, 0, 0, 0));
        using var asm = AssemblyDefinition.CreateAssembly(asmName, "mRemoteNG", ModuleKind.Dll);
        Assert.Throws<InvalidOperationException>(() =>
            WfWasm.Rewrite.AssemblyRewriter.NeutralizeMRemoteNGThemeManagerDesignTime(asm.MainModule));
    }

    [Fact]
    public void Wrong_Shape_Without_Process_Probe_Fails_Loud()
    {
        // A same-named method that never touches Process must not be
        // silently stubbed: fail loud so the regen skips the rule instead
        // of rewriting the wrong member.
        string dir = Path.Combine(Path.GetTempPath(), $"mrntheme_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string input = Path.Combine(dir, "ThemeWrongShape.dll");
        try
        {
            BuildFixture(input, withProcessProbe: false);
            using var module = ModuleDefinition.ReadModule(input);
            Assert.Throws<InvalidOperationException>(() =>
                WfWasm.Rewrite.AssemblyRewriter.NeutralizeMRemoteNGThemeManagerDesignTime(module));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
