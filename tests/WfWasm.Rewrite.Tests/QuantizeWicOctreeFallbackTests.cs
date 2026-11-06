using Mono.Cecil;
using WfWasm.Rewrite;
using Xunit;

namespace WfWasm.Rewrite.Tests;

// RVW3293-VEN5: MedianCutWicQuantizer.GeneratePalette dereferences
// ImagingFactory.Instance, which the WIC rule leaves null headless; the
// fault never settles WaitWithUI.doneSignal, so the modal progress dialog
// never closes (TRUE-OK 2/2 wedge, 0 CPU). OctreeQuantizer is pure managed
// math, so the rule routes the null-factory case to it (desktop factory
// present keeps the exact WIC path). Pinned directly against the pristine
// input, like the Redirect* rules prescribe.
public sealed class QuantizeWicOctreeFallbackTests
{
    private static string PaintDir()
    {
        string dir = @"E:\AI\Code\WindowsForms\targets\paint.net";
        Assert.True(Directory.Exists(dir), "pristine PDN inputs missing: " + dir);
        return dir;
    }

    private static ModuleDefinition LoadFramework(out string _)
    {
        string input = Path.Combine(PaintDir(), "PaintDotNet.Framework.dll");
        Assert.True(File.Exists(input), "pristine PDN input missing: " + input);
        _ = input;
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(PaintDir());
        return AssemblyDefinition.ReadAssembly(input,
            new ReaderParameters { AssemblyResolver = resolver }).MainModule;
    }

    private static MethodDefinition FindGeneratePalette(ModuleDefinition module)
    {
        var wic = module.GetType("PaintDotNet.Imaging.Quantization.MedianCutWicQuantizer");
        Assert.NotNull(wic);
        var gen = wic!.Methods.FirstOrDefault(m => m.Name == "GeneratePalette"
            && m.Parameters.Count == 4
            && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IBitmapSource"
            && m.Parameters[1].ParameterType.FullName == "System.Int32"
            && m.Parameters[2].ParameterType.FullName == "System.Boolean"
            && m.Parameters[3].ParameterType.FullName == "PaintDotNet.Threading.ICancellationToken");
        Assert.NotNull(gen);
        return gen!;
    }

    private static bool Calls(MethodDefinition m, string decl, string name) =>
        m.Body.Instructions.Any(i => i.Operand is MethodReference mr
            && mr.DeclaringType.FullName == decl && mr.Name == name);

    [Fact]
    public void Rewrite_Routes_Null_WicFactory_To_Octree()
    {
        ModuleDefinition module = LoadFramework(out _);
        MethodDefinition gen = FindGeneratePalette(module);
        // Pre-shape (the wedge as shipped): WIC path, no factory check,
        // no Octree route.
        Assert.True(Calls(gen, "PaintDotNet.Imaging.Quantization.MedianCutWicQuantizer", "GeneratePaletteWicImpl"));
        Assert.False(Calls(gen, "PaintDotNet.Imaging.ImagingFactory", "get_Instance"));
        Assert.False(Calls(gen, "PaintDotNet.Imaging.Quantization.OctreeQuantizer", "GeneratePalette"));

        AssemblyRewriter.EnsureQuantizerWicOctreeFallback(module);

        // Post-shape: WIC path preserved AND null-factory Octree fallback present.
        Assert.True(Calls(gen, "PaintDotNet.Imaging.Quantization.MedianCutWicQuantizer", "GeneratePaletteWicImpl"));
        Assert.True(Calls(gen, "PaintDotNet.Imaging.ImagingFactory", "get_Instance"));
        Assert.True(Calls(gen, "PaintDotNet.Imaging.Quantization.OctreeQuantizer", "GeneratePalette"));
        // Structural: guard leads (factory check first), branch lands on a Nop.
        var first = gen.Body.Instructions[0];
        Assert.True(first.OpCode == Mono.Cecil.Cil.OpCodes.Call
            && first.Operand is MethodReference fr
            && fr.Name == "get_Instance");
        Assert.Contains(gen.Body.Instructions, ins =>
            ins.OpCode == Mono.Cecil.Cil.OpCodes.Brtrue_S
            && ins.Operand is Mono.Cecil.Cil.Instruction tgt
            && tgt.OpCode == Mono.Cecil.Cil.OpCodes.Nop);
    }
}
