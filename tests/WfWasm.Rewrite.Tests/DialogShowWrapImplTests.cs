using Mono.Cecil;
using Mono.Cecil.Cil;

// Dialog-show HWND backend: the New-image dialog measure path
// (PdnRadioButton.MeasureAndDraw.b__0 ->
// DrawingContextUtil.BeginDraw(IDeviceContext,Rect,Bool) -> factory and
// IntPtr overloads -> DCDeviceContextPool/BindDC ->
// DrawingContext.WrapImpl(IDeviceContext)) dies because converted
// WrapImpl is neutered to ldnull/ret, so null flows into b__1 and
// GetCachedOrCreateResource<ITextLayout> NREs (served runJ frames:
// ToolStripItem-click NRE at ResourceCacheExtensions, no dialog).
// WrapImpl's only live callers are dialog/control-show paths
// (DrawingContextUtil.BeginDraw + control OnRender/CallbackDrawer via
// Wrap; BeginDrawImpl is itself neutered) — the effect worker
// pipeline (scan0/IBitmap/surface) never calls it — so rerouting
// WrapImpl to `new CpuDrawingContext(0)` with TrySetResourceCache
// provisioning is dialog-show-scoped. The .ctor(Int64) stores only
// (zero-safe) and the holder slot carries the text-layout backend
// for measure. Red-first: the two WrapImpl pins fail on current
// emission; the boundary pin passes and must keep passing.
public sealed class DialogShowWrapImplTests
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
            new Dictionary<string, (string Assembly, string Type)>(System.StringComparer.Ordinal),
            cpuDll);
        return tempOut;
    }

    private static MethodDefinition FindWrapImpl(ModuleDefinition module)
    {
        TypeDefinition? dc = module.GetType("PaintDotNet.Direct2D1.DrawingContext");
        Assert.True(dc is not null, "DrawingContext missing from rewritten Windows.Core");
        MethodDefinition? wrap = dc!.Methods.FirstOrDefault(m => m.Name == "WrapImpl"
            && m.IsStatic && m.Parameters.Count == 1
            && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Direct2D1.IDeviceContext"
            && m.ReturnType.FullName == "PaintDotNet.Direct2D1.DrawingContext");
        Assert.True(wrap is not null, "WrapImpl(IDeviceContext) shape drifted or missing");
        Assert.True(wrap!.HasBody, "WrapImpl has no body");
        return wrap;
    }

    private static bool BodyNewsCpuContext(MethodDefinition m)
    {
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
        foreach (Instruction i in m.Body.Instructions)
        {
            if ((i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference mr && mr.Name == "TrySetResourceCache")
                return true;
        }
        return false;
    }

    [Fact]
    public void DialogShow_WrapImpl_Routes_To_CpuDrawingContext()
    {
        // The HWND dialog-show terminal must construct a CPU context
        // instead of returning null into the measure lambdas.
        string tempOut = Path.Combine(Path.GetTempPath(), $"DcWrap_{Guid.NewGuid():N}.dll");
        try
        {
            Transform("PaintDotNet.Windows.Core.dll", tempOut);
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(PaintDir());
            using var asm = AssemblyDefinition.ReadAssembly(tempOut,
                new ReaderParameters { AssemblyResolver = resolver });
            MethodDefinition wrap = FindWrapImpl(asm.MainModule);
            Assert.True(BodyNewsCpuContext(wrap),
                "WrapImpl does not construct CpuDrawingContext (dialog HWND path still null)");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void DialogShow_WrapImpl_Provisions_Holder()
    {
        // Holder core: the WrapImpl backend must TrySet its own cache so
        // the measure chain resolves the text-layout backend.
        string tempOut = Path.Combine(Path.GetTempPath(), $"DcWrap_{Guid.NewGuid():N}.dll");
        try
        {
            Transform("PaintDotNet.Windows.Core.dll", tempOut);
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(PaintDir());
            using var asm = AssemblyDefinition.ReadAssembly(tempOut,
                new ReaderParameters { AssemblyResolver = resolver });
            MethodDefinition wrap = FindWrapImpl(asm.MainModule);
            Assert.True(BodyCallsTrySet(wrap),
                "WrapImpl without TrySetResourceCache provisioning (holder unresolved)");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void DialogShow_Other_Overloads_Unchanged()
    {
        // Grant boundary: every non-WrapImpl BeginDraw shape keeps its
        // current emission — null terminals stay null, non-CPU overloads
        // stay non-CPU, and the NEXT7 scan0 backend stays CPU.
        string tempOut = Path.Combine(Path.GetTempPath(), $"DcWrap_{Guid.NewGuid():N}.dll");
        try
        {
            Transform("PaintDotNet.Windows.Core.dll", tempOut);
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(PaintDir());
            using var asm = AssemblyDefinition.ReadAssembly(tempOut,
                new ReaderParameters { AssemblyResolver = resolver });
            TypeDefinition? dc = asm.MainModule.GetType("PaintDotNet.Direct2D1.DrawingContext");
            Assert.True(dc is not null, "DrawingContext missing from rewritten Windows.Core");
            var bad = new List<string>();
            foreach (MethodDefinition m in dc!.Methods.Where(m => m.HasBody && m.Name.Contains("BeginDraw")))
            {
                string pars = string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName));
                bool isScan0 = m.Parameters.Any(p => p.ParameterType.FullName == "System.Void*");
                bool isWrapImpl = m.Name == "WrapImpl";
                if (isWrapImpl)
                    continue;
                if (m.Name == "BeginDrawNull" || m.Name == "BeginDrawImpl" || m.Name == "BeginDrawAndSubsume")
                {
                    var ins = m.Body.Instructions;
                    if (!(ins.Count == 2 && ins[0].OpCode == OpCodes.Ldnull && ins[1].OpCode == OpCodes.Ret))
                        bad.Add(m.Name + "(" + pars + ") left null-terminal shape");
                }
                else if (isScan0)
                {
                    if (!BodyNewsCpuContext(m))
                        bad.Add(m.Name + "(" + pars + ") lost NEXT7 CPU backend");
                }
                else if (m.IsStatic && m.ReturnType.FullName == "PaintDotNet.Direct2D1.IDrawingContext")
                {
                    if (BodyNewsCpuContext(m))
                        bad.Add(m.Name + "(" + pars + ") gained CPU backend outside grant");
                }
            }
            Assert.True(bad.Count == 0, "overload boundary drift: " + string.Join("; ", bad.Take(8)));
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
