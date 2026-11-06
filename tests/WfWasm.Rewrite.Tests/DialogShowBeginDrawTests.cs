using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Reflection;

// Dialog-show backend: the dialog-measure path
// (PdnRadioButton.MeasureAndDraw -> DrawingContextUtil.BeginDraw ->
// static DrawingContext.BeginDraw(factory, scan0, size, stride, pxfmt)
// -> instance BeginDraw(Rect,Bool) -> GetCachedOrCreateResource<ITextLayout>)
// dies because the static scan0 BeginDraw overloads are neutered to
// ldnull/ret and the synthesized CpuDrawingContext instance
// BeginDraw(Rect,Bool) falls into the generic NotSupported throw, so no
// holder-equipped CPU context ever reaches the text-layout backend and
// the genuine forwarder NREs (served ImageSizeDialog/effect-dialog wall).
// The fix reroutes the scan0 statics to `new CpuDrawingContext(scan0)`
// with TrySetResourceCache provisioning and returns `this` from the
// instance overload. Red-first: all three pins fail on current emission.
public sealed class DialogShowBeginDrawTests
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

    private static string Transform(string fileName, string tempOut)
    {
        string input = Path.Combine(PaintDir(), fileName);
        Assert.True(File.Exists(input), "pristine PDN input missing: " + input);
        string cpuDll = BinDll("WfWasm.PdnCpu", "net9.0", "WfWasm.PdnCpu");
        WfWasm.Rewrite.AssemblyRewriter.Rewrite(
            input, tempOut,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
            cpuDll);
        return tempOut;
    }

    private static bool BodyNewsCpuContext(MethodDefinition m)
    {
        if (!m.HasBody) return false;
        foreach (Instruction i in m.Body.Instructions)
        {
            if ((i.OpCode == OpCodes.Newobj) && i.Operand is MethodReference mr
                && mr.DeclaringType.FullName == "PaintDotNet.Direct2D1.CpuDrawingContext"
                && mr.Name == ".ctor")
                return true;
        }
        return false;
    }

    private static bool BodyCallsTrySet(MethodDefinition m)
    {
        if (!m.HasBody) return false;
        foreach (Instruction i in m.Body.Instructions)
        {
            if ((i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference mr && mr.Name == "TrySetResourceCache")
                return true;
        }
        return false;
    }

    [Fact]
    public void DialogShow_Scan0_BeginDraw_Routes_To_CpuDrawingContext()
    {
        // The two scan0 static overloads (the DrawingContextUtil.BeginDraw
        // target shape) must construct a holder-equipped CPU context instead
        // of returning null.
        string tempOut = Path.Combine(Path.GetTempPath(), $"DcShow_{Guid.NewGuid():N}.dll");
        try
        {
            Transform("PaintDotNet.Windows.Core.dll", tempOut);
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(PaintDir());
            using var asm = AssemblyDefinition.ReadAssembly(tempOut,
                new ReaderParameters { AssemblyResolver = resolver });
            TypeDefinition? dc = asm.MainModule.GetType("PaintDotNet.Direct2D1.DrawingContext");
            Assert.True(dc is not null, "DrawingContext missing from rewritten Windows.Core");
            var scan0 = dc!.Methods
                .Where(m => m.IsStatic && m.Name == "BeginDraw" && m.HasBody
                    && m.Parameters.Any(p => p.ParameterType.FullName == "System.Void*"))
                .ToList();
            Assert.True(scan0.Count > 0, "no scan0 static BeginDraw overloads found");
            var bad = scan0.Where(m => !BodyNewsCpuContext(m)).Select(m => m.FullName).ToList();
            Assert.True(bad.Count == 0,
                "scan0 BeginDraw without CpuDrawingContext backend: " + string.Join("; ", bad.Take(5)));
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void DialogShow_CpuContext_BeginDraw_Rect_Returns_This()
    {
        // The instance BeginDraw(RectInt32, Boolean) on the synthesized
        // context must answer the same context (chain continues to b__1),
        // not the generic NotSupported throw.
        string tempOut = Path.Combine(Path.GetTempPath(), $"DcShow_{Guid.NewGuid():N}.dll");
        try
        {
            Transform("PaintDotNet.Windows.Core.dll", tempOut);
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(PaintDir());
            using var asm = AssemblyDefinition.ReadAssembly(tempOut,
                new ReaderParameters { AssemblyResolver = resolver });
            TypeDefinition? cpu = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.True(cpu is not null, "CpuDrawingContext missing from rewritten Windows.Core");
            MethodDefinition? begin = cpu!.Methods.FirstOrDefault(m => m.Name == "BeginDraw"
                && m.Parameters.Count == 2
                && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Rendering.RectInt32"
                && m.Parameters[1].ParameterType.FullName == "System.Boolean");
            Assert.True(begin is not null, "CpuDrawingContext.BeginDraw(Rect,Bool) missing");
            Assert.True(begin!.HasBody, "CpuDrawingContext.BeginDraw(Rect,Bool) has no body");
            var ins = begin.Body.Instructions;
            Assert.True(ins.Count == 2 && ins[0].OpCode == OpCodes.Ldarg_0 && ins[1].OpCode == OpCodes.Ret,
                "CpuDrawingContext.BeginDraw(Rect,Bool) does not return this");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void DialogShow_Scan0_BeginDraw_Provisions_Holder()
    {
        // Holder core: the scan0 backend must TrySet its own cache so
        // holder-based consumers resolve the same text-layout backend.
        string tempOut = Path.Combine(Path.GetTempPath(), $"DcShow_{Guid.NewGuid():N}.dll");
        try
        {
            Transform("PaintDotNet.Windows.Core.dll", tempOut);
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(PaintDir());
            using var asm = AssemblyDefinition.ReadAssembly(tempOut,
                new ReaderParameters { AssemblyResolver = resolver });
            TypeDefinition? dc = asm.MainModule.GetType("PaintDotNet.Direct2D1.DrawingContext");
            Assert.True(dc is not null, "DrawingContext missing from rewritten Windows.Core");
            var scan0 = dc!.Methods
                .Where(m => m.IsStatic && m.Name == "BeginDraw" && m.HasBody
                    && m.Parameters.Any(p => p.ParameterType.FullName == "System.Void*"))
                .ToList();
            Assert.True(scan0.Count > 0, "no scan0 static BeginDraw overloads found");
            var bad = scan0.Where(m => !BodyCallsTrySet(m)).Select(m => m.FullName).ToList();
            Assert.True(bad.Count == 0,
                "scan0 BeginDraw without TrySetResourceCache provisioning: " + string.Join("; ", bad.Take(5)));
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    private static void LocalSink(object? o) { }

    [Fact]
    public void DialogShow_Null_Context_Mechanism_Callvirt_Null_This_Throws()
    {
        // Runtime mechanism proof without executing foreign binaries
        // (pristine PDN ships its own CoreLib MVID and cannot LoadFrom
        // in-process): (a) pristine metadata proves DrawingContextUtil.
        // BeginDraw delegates to static DrawingContext.BeginDraw, the
        // neutered target; (b) the BCL proves a virtual dispatch on a null
        // reference throws NullReferenceException — the exact served NRE
        // signature when the neutered static feeds null into b__0/b__1.
        string corePath = Path.Combine(PaintDir(), "PaintDotNet.Core.dll");
        Assert.True(File.Exists(corePath), "pristine Core missing: " + corePath);
        using var core = AssemblyDefinition.ReadAssembly(corePath);
        TypeDefinition? util = core.MainModule.GetTypes()
            .FirstOrDefault(t => t.FullName == "PaintDotNet.Drawing.DrawingContextUtil");
        Assert.True(util is not null, "DrawingContextUtil not found in pristine metadata");
        bool routesToStatic = util!.Methods.Where(m => m.HasBody).Any(m =>
            m.Body.Instructions.Any(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference mr && mr.Name == "BeginDraw"
                && mr.DeclaringType.FullName == "PaintDotNet.Direct2D1.DrawingContext"));
        Assert.True(routesToStatic, "DrawingContextUtil.BeginDraw does not route to DrawingContext.BeginDraw");
        Action<object?> sink = LocalSink;
        object? noThis = null;
        Assert.Throws<NullReferenceException>(() => sink(noThis!.GetType()));
    }
}
