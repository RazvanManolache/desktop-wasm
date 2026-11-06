namespace WfWasm.Rewrite.Tests;

// PUSH1106-vendor-qc4arm: the Quantize OK flow builds the output
// palette via ImagingFactoryExtensions.CreatePalette(factory,
// colors[, start, length]) with the dead headless factory, which
// throws ArgumentNullException and buries the conversion as a
// WorkerThread + 418-repaint (live w23 evidence). The CPU arm must
// ignore the factory and construct a CPU palette instead: rewritten
// bodies keep no "factory" validation and newobj a nested type that
// implements IPalette.
public sealed class PaletteCreateArmTests
{
    private static string PaintDir()
    {
        string dir = @"E:\AI\Code\WindowsForms\targets\paint.net";
        Assert.True(Directory.Exists(dir), "pristine PDN inputs missing: " + dir);
        return dir;
    }

    private static string BinDll(string project, string tfm, string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", project, "bin", "Debug", tfm, name + ".dll"));
        Assert.True(File.Exists(dll), "dependency not built: " + dll);
        return dll;
    }

    private static string LaneTemp(string fileName, string lane = "ven-qc4-w23")
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        string laneDir = Path.Combine(dir.FullName, ".artifacts", "tmp", "runs",
            "dw-vendor", lane);
        Directory.CreateDirectory(laneDir);
        return Path.Combine(laneDir, fileName);
    }

    private static Mono.Cecil.ModuleDefinition Transform(
        string fileName, string tempOut)
    {
        string input = Path.Combine(PaintDir(), fileName);
        Assert.True(File.Exists(input), "pristine PDN input missing: " + input);
        string cpuDll = BinDll("WfWasm.PdnCpu", "net9.0", "WfWasm.PdnCpu");
        WfWasm.Rewrite.AssemblyRewriter.Rewrite(
            input, tempOut,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
            cpuDll);
        var resolver = new Mono.Cecil.DefaultAssemblyResolver();
        resolver.AddSearchDirectory(PaintDir());
        resolver.AddSearchDirectory(
            System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        return Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut,
            new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver }).MainModule;
    }

    [Fact]
    public void PaletteArm_MemberOperands_Resolve()
    {
        // w22 lesson: presence is not correctness. Every member token
        // the arm emits (including the !0 indexer mirror) must resolve
        // in the closure, or the method detonates at first live call.
        const string fileName = "PaintDotNet.Windows.Core.dll";
        string tempOut = LaneTemp($"PalResolve_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = Transform(fileName, tempOut);
            var ife = module.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
            Assert.NotNull(ife);
            var failures = new List<string>();
            failures.AddRange(CheckOperands(module));
            Assert.True(failures.Count == 0,
                "unresolvable arm tokens, got " + failures.Count + ": "
                + string.Join(" | ", failures.Take(6)));
        }
        finally
        {
            try { System.IO.File.Delete(tempOut); } catch { }
        }
    }

    private static List<string> CheckOperands(Mono.Cecil.ModuleDefinition module)
    {
        var failures = new List<string>();
        var ife = module.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
        Assert.NotNull(ife);
        var targets = new List<Mono.Cecil.MethodDefinition>();
        targets.AddRange(ife.Methods.Where(m => m.Name == "CreatePalette" && m.HasBody));
        var cpuPal = ife.NestedTypes.Concat(module.Types)
            .FirstOrDefault(t => t.Interfaces.Any(ii => ii.InterfaceType.FullName
                == "PaintDotNet.Imaging.IPalette"));
        if (cpuPal is not null)
            targets.AddRange(cpuPal.Methods.Where(m => m.HasBody));
        foreach (var m in targets)
        {
            foreach (var ins in m.Body.Instructions)
            {
                try
                {
                    if (ins.Operand is Mono.Cecil.MethodReference mr
                        && (ins.OpCode.Name == "call" || ins.OpCode.Name == "callvirt"
                            || ins.OpCode.Name == "newobj" || ins.OpCode.Name == "ldftn"))
                    {
                        if (mr.Resolve() is null)
                            failures.Add(m.FullName + " :: " + mr.FullName + " resolved null");
                    }
                    else if (ins.Operand is Mono.Cecil.FieldReference fr
                        && (ins.OpCode.Name.StartsWith("ldfld", System.StringComparison.Ordinal)
                            || ins.OpCode.Name.StartsWith("stfld", System.StringComparison.Ordinal)))
                    {
                        if (fr.Resolve() is null)
                            failures.Add(m.FullName + " :: field " + fr.FullName + " resolved null");
                    }
                }
                catch (System.Exception ex)
                {
                    failures.Add(m.FullName + " :: " + ins.Operand + " threw "
                        + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
                }
            }
        }
        return failures;
    }

    [Fact]
    public void CreatePalette_Arms_Ignore_Null_Factory_And_Build_Cpu_Palette()
    {
        const string fileName = "PaintDotNet.Windows.Core.dll";
        string tempOut = LaneTemp($"PalArm_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = Transform(fileName, tempOut);
            var ife = module.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
            Assert.NotNull(ife);
            var methods = ife.Methods
                .Where(m => m.Name == "CreatePalette" && m.HasBody).ToList();
            Assert.Equal(2, methods.Count);
            var failures = new List<string>();
            bool BuildsPalette(Mono.Cecil.MethodDefinition m) =>
                m.Body.Instructions.Any(ins =>
                    ins.OpCode.Name == "newobj" && ins.Operand is Mono.Cecil.MethodReference mr
                    && mr.DeclaringType.Resolve() is Mono.Cecil.TypeDefinition td
                    && td.Interfaces.Any(ii => ii.InterfaceType.FullName
                        == "PaintDotNet.Imaging.IPalette"));
            var builders = methods.Where(BuildsPalette).ToList();
            foreach (var m in methods)
            {
                foreach (var ins in m.Body.Instructions)
                {
                    if (ins.OpCode.Name == "ldstr" && ins.Operand is string s
                        && s == "factory")
                        failures.Add(m.FullName + " still validates factory");
                }
                if (BuildsPalette(m))
                    continue;
                // pc=2 mirrors pristine by delegating to the pc=4 arm over
                // the full range instead of constructing inline.
                bool delegates = m.Body.Instructions.Any(ins =>
                    (ins.OpCode.Name == "call" || ins.OpCode.Name == "callvirt")
                    && ins.Operand is Mono.Cecil.MethodReference mr
                    && mr.Name == "CreatePalette"
                    && builders.Any(b => b.FullName == mr.FullName
                        || mr.Resolve()?.FullName == b.FullName));
                if (!delegates)
                    failures.Add(m.FullName + " does not construct an IPalette");
            }
            var cpuPal = ife.NestedTypes.Concat(module.Types)
                .FirstOrDefault(t => t.Interfaces.Any(ii => ii.InterfaceType.FullName
                    == "PaintDotNet.Imaging.IPalette"));
            if (cpuPal is null)
            {
                failures.Add("no CPU IPalette implementation in module");
            }
            else
            {
                foreach (string need in new[]
                    { "get_Colors", "get_PaletteType", "get_HasAlpha",
                      "get_IsBlackWhite", "get_IsGrayscale" })
                {
                    if (!cpuPal.Methods.Any(m => m.Name == need
                        && m.Parameters.Count == 0))
                        failures.Add("CpuPalette missing " + need);
                }
            }
            Assert.True(failures.Count == 0,
                "palette CPU arm incomplete, got " + failures.Count + ": "
                + string.Join(" | ", failures.Take(6)));
        }
        finally
        {
            try { System.IO.File.Delete(tempOut); } catch { }
        }
    }

    private sealed class PalAlc : System.Runtime.Loader.AssemblyLoadContext
    {
        private readonly string _dir;
        public PalAlc(string dir) : base(isCollectible: true) { _dir = dir; }
        protected override System.Reflection.Assembly? Load(
            System.Reflection.AssemblyName assemblyName)
        {
            string cand = Path.Combine(_dir, assemblyName.Name + ".dll");
            return File.Exists(cand) ? LoadFromAssemblyPath(cand) : null;
        }
    }

    [Fact]
    public void CpuPalette_Ctor_Invokes_Without_FieldAccess()
    {
        // w25 lesson: Resolve()-green is not execution-green. The
        // synthesized CpuPalette .ctor read private ColorBgra32.bgra
        // and detonated live with FieldAccessException inside a worker.
        // This test INVOKES the ctor: RED means the live wall stands.
        const string fileName = "PaintDotNet.Windows.Core.dll";
        string tempOut = LaneTemp($"PalInvoke_{Guid.NewGuid():N}.dll", "fieldaccess-0306");
        PalAlc? alc = null;
        try
        {
            using (Transform(fileName, tempOut)) { }
            alc = new PalAlc(PaintDir());
            byte[] raw = File.ReadAllBytes(tempOut);
            using var ms = new MemoryStream(raw);
            var rw = alc.LoadFromStream(ms);
            var palType = rw.GetType("PaintDotNet.Imaging.CpuPalette")
                ?? rw.GetTypes().FirstOrDefault(t => t.GetInterfaces().Any(ii =>
                    ii.FullName == "PaintDotNet.Imaging.IPalette"));
            Assert.NotNull(palType);
            var primAsm = alc.Assemblies.FirstOrDefault(a =>
                a.GetType("PaintDotNet.Imaging.ColorBgra32") != null)
                ?? alc.LoadFromAssemblyName(new System.Reflection.AssemblyName(
                    "PaintDotNet.Primitives"));
            var colorType = primAsm.GetType("PaintDotNet.Imaging.ColorBgra32");
            Assert.NotNull(colorType);
            var colorCtor = colorType.GetConstructor(new[] {
                typeof(byte), typeof(byte), typeof(byte), typeof(byte) });
            Assert.NotNull(colorCtor);
            var listType = typeof(List<>).MakeGenericType(colorType);
            var colors = System.Activator.CreateInstance(listType);
            Assert.NotNull(colors);
            var add = listType.GetMethod("Add");
            Assert.NotNull(add);
            // Entry 0: opaque red (B=0,G=0,R=255,A=255). Entry 1:
            // half-alpha green with a non-extreme channel
            // (B=0,G=200,R=0,A=128) -> HasAlpha true, IsBlackWhite
            // false (200 is neither 0 nor 255), IsGrayscale false.
            add.Invoke(colors, new[] { colorCtor.Invoke(new object[] {
                (byte)0, (byte)0, (byte)255, (byte)255 }) });
            add.Invoke(colors, new[] { colorCtor.Invoke(new object[] {
                (byte)0, (byte)200, (byte)0, (byte)128 }) });
            object? pal = null;
            try
            {
                pal = System.Activator.CreateInstance(palType, new[] { colors });
            }
            catch (System.Reflection.TargetInvocationException tie)
            {
                Assert.Fail("CpuPalette .ctor threw "
                    + (tie.InnerException?.GetType().Name ?? "null") + ": "
                    + tie.InnerException?.Message);
            }
            Assert.NotNull(pal);
            // NOTE: the synthesized type emits getter METHODS without
            // PropertyDef grouping, so bind by method name, not property.
            var colorsBack = (System.Collections.IList)palType
                .GetMethod("get_Colors")!.Invoke(pal, null)!;
            Assert.Equal(2, colorsBack.Count);
            Assert.True((bool)palType.GetMethod("get_HasAlpha")!
                .Invoke(pal, null)!);
            Assert.False((bool)palType.GetMethod("get_IsGrayscale")!
                .Invoke(pal, null)!);
            Assert.False((bool)palType.GetMethod("get_IsBlackWhite")!
                .Invoke(pal, null)!);
        }
        finally
        {
            alc?.Unload();
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            try { System.IO.File.Delete(tempOut); } catch { }
        }
    }
}
