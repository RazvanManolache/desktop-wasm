using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Reflection;

// PROG1235-Job1: the GetSourceBitmap<TPixel> reroute emitted the substituted
// RETURN interface (IEffectInputBitmap<ColorBgra32>) as the pixelType argument
// to CpuPaint.GetEffectSourceBitmap, but the real definition is constrained
// `where TPixel : struct`. MakeGenericMethod(typeof(interface)) throws
// ArgumentException, which ResolveEffectStaticGeneric logs as MAKENO and the
// worker absorbs (served Quantize/Twist/Bulge bridge miss). The emitted
// pixelType must be the pixel struct (gim.GenericArguments[0]); the bridge
// post-op target stays the return interface (CpuPaint.Unwrap<T: class>).
public sealed class GetSourceBitmapPixelTypeTests
{
    private static readonly string[] EffectInputs =
    {
        "PaintDotNet.Effects.dll",
        "PaintDotNet.Effects.Gpu.dll",
        "PaintDotNet.Effects.Legacy.dll",
    };

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

    private static ModuleDefinition Transform(string fileName, string tempOut)
    {
        string input = Path.Combine(PaintDir(), fileName);
        Assert.True(File.Exists(input), "pristine PDN input missing: " + input);
        string cpuDll = BinDll("WfWasm.PdnCpu", "net9.0", "WfWasm.PdnCpu");
        WfWasm.Rewrite.AssemblyRewriter.Rewrite(
            input, tempOut,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
            cpuDll);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(PaintDir());
        return AssemblyDefinition.ReadAssembly(tempOut,
            new ReaderParameters { AssemblyResolver = resolver }).MainModule;
    }

    [Fact]
    public void GetSourceBitmap_Sites_Carry_Pixel_Struct_Not_Return_Interface()
    {
        // Emitted-IL pin: every rerouted GetSourceBitmap site must pass the
        // pixel STRUCT (ColorBgra32, a value type) as the ldtoken pixelType
        // argument to CpuPaint.GetEffectSourceBitmap — never the substituted
        // return interface IEffectInputBitmap<...>, which violates the
        // struct constraint and MAKENO-misses at runtime.
        var bad = new List<string>();
        int sites = 0;
        var tempFiles = new List<string>();
        try
        {
            foreach (string fileName in EffectInputs)
            {
                string tempOut = Path.Combine(Path.GetTempPath(), $"FxPixel_{Guid.NewGuid():N}.dll");
                tempFiles.Add(tempOut);
                using var module = Transform(fileName, tempOut);
                var stack = new Stack<TypeDefinition>(module.Types);
                while (stack.Count > 0)
                {
                    var type = stack.Pop();
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody) continue;
                        var insns = method.Body.Instructions;
                        for (int i = 0; i < insns.Count; i++)
                        {
                            var ins = insns[i];
                            if ((ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt)
                                || ins.Operand is not MethodReference mr
                                || mr.DeclaringType.FullName != "WfWasm.PdnCpu.CpuPaint"
                                || mr.Name != "GetEffectSourceBitmap")
                                continue;
                            sites++;
                            string loc = fileName + " " + type.FullName + "::" + method.Name;
                            if (i < 2
                                || insns[i - 1].OpCode != OpCodes.Call
                                || insns[i - 2].OpCode != OpCodes.Ldtoken
                                || insns[i - 2].Operand is not TypeReference pixelType)
                            {
                                bad.Add(loc + " unexpected pixelType sequence");
                                continue;
                            }
                            if (pixelType.FullName.Contains("IEffectInputBitmap"))
                                bad.Add(loc + " pixelType is return interface " + pixelType.FullName);
                            else if (!pixelType.IsValueType)
                            {
                                TypeDefinition? resolved = null;
                                try { resolved = pixelType.Resolve(); } catch { }
                                if (resolved is null || !resolved.IsValueType)
                                    bad.Add(loc + " pixelType not a struct: " + pixelType.FullName);
                            }
                        }
                    }
                    foreach (var nested in type.NestedTypes)
                        stack.Push(nested);
                }
            }
            Assert.True(sites > 0, "no GetEffectSourceBitmap helper landings found in effect inputs");
            Assert.True(bad.Count == 0,
                "GetSourceBitmap sites with interface pixelType: " + string.Join("; ", bad.Take(10)));
        }
        finally
        {
            foreach (string f in tempFiles)
            {
                try { File.Delete(f); } catch { }
            }
        }
    }

    private static T LocalStructId<T>(T value) where T : struct => value;

    [Fact]
    public void Struct_Constraint_Mechanism_Interface_Arg_Throws_Struct_Arg_Binds()
    {
        // Runtime mechanism proof without loading foreign binaries (pristine
        // PDN ships its own CoreLib MVID and cannot LoadFrom in-process):
        // (a) Cecil metadata on the pristine definition proves TPixel carries
        // the struct constraint; (b) the BCL proves a constraint-violating
        // interface type argument throws ArgumentException — the exact served
        // MAKENO signature — while the struct binds. Together with the
        // emission pin above, this closes the causal chain: pre-fix emission
        // feeds the interface into MakeGenericMethod, which must throw.
        string corePath = Path.Combine(PaintDir(), "PaintDotNet.Effects.Core.dll");
        Assert.True(File.Exists(corePath), "pristine Effects.Core missing: " + corePath);
        using var core = AssemblyDefinition.ReadAssembly(corePath);
        TypeDefinition? envExt = core.MainModule.Types
            .FirstOrDefault(t => t.Name == "EffectEnvironmentExtensions");
        Assert.True(envExt is not null, "EffectEnvironmentExtensions not found in pristine Effects.Core");
        MethodDefinition? def = envExt.Methods
            .FirstOrDefault(m => m.Name == "GetSourceBitmap" && m.HasGenericParameters
                && m.GenericParameters.Count == 1);
        Assert.True(def is not null, "GetSourceBitmap<TPixel> generic definition not found");
        var tp = def.GenericParameters[0];
        Assert.True((tp.Attributes & Mono.Cecil.GenericParameterAttributes.NotNullableValueTypeConstraint) != 0,
            "GetSourceBitmap<TPixel> lacks the struct constraint");
        TypeDefinition? pixelDef = core.MainModule.Types.FirstOrDefault(t => t.Name == "ColorBgra32")
            ?? core.MainModule.GetTypes().FirstOrDefault(t => t.Name == "ColorBgra32");
        TypeDefinition? ifaceDef = core.MainModule.GetTypes()
            .FirstOrDefault(t => t.Name == "IEffectInputBitmap`1");
        if (pixelDef is not null)
            Assert.True(pixelDef.IsValueType, "ColorBgra32 is not a value type in pristine metadata");
        if (ifaceDef is not null)
            Assert.True(ifaceDef.IsInterface, "IEffectInputBitmap<> is not an interface in pristine metadata");
        MethodInfo mirror = typeof(GetSourceBitmapPixelTypeTests).GetMethod(
            nameof(LocalStructId), BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Throws<ArgumentException>(() => mirror.MakeGenericMethod(typeof(IDisposable)));
        Assert.NotNull(mirror.MakeGenericMethod(typeof(int)));
    }
}
