using Mono.Cecil;
using Mono.Cecil.Cil;

// VEN-SPLICE-2308: the rewritten CreateFormatConvertedBitmap must route an
// indexed source (quantized buffer) to Bgra32 through the source bitmap's
// own palette (exact per-pixel lookup) instead of fail-louding. Unknown
// non-indexed pairs keep the loud throw. Serves carry this via regen.
public sealed class IndexedToBgraShapeTests
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

    private static string LaneTemp(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        string lane = Path.Combine(dir.FullName, ".artifacts", "tmp", "runs",
            "dw-vendor", "ven-splice-2308");
        Directory.CreateDirectory(lane);
        return Path.Combine(lane, fileName);
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
        resolver.AddSearchDirectory(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        return AssemblyDefinition.ReadAssembly(tempOut,
            new ReaderParameters { AssemblyResolver = resolver }).MainModule;
    }

    [Fact]
    public void CreateFormatConvertedBitmap_RoutesIndexedThroughPaletteLookup()
    {
        const string fileName = "PaintDotNet.Windows.Core.dll";
        string tempOut = LaneTemp($"IdxBgra_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = Transform(fileName, tempOut);
            var ife = module.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
            Assert.NotNull(ife);
            int withPalette = 0;
            int withThrow = 0;
            var bad = new List<string>();
            foreach (var m in ife.Methods)
            {
                if (m.Name != "CreateFormatConvertedBitmap" || !m.HasBody)
                    continue;
                bool hasSource = false;
                bool hasFormat = false;
                foreach (var p in m.Parameters)
                {
                    if (p.ParameterType.FullName == "PaintDotNet.Imaging.IBitmapSource")
                        hasSource = true;
                    if (p.ParameterType is ByReferenceType br
                        && br.ElementType.FullName == "PaintDotNet.Imaging.PixelFormat")
                        hasFormat = true;
                }
                if (!hasSource || !hasFormat)
                    continue;
                bool palettePath = false;
                bool loudThrow = false;
                foreach (var ins in m.Body.Instructions)
                {
                    if (ins.Operand is MethodReference mr)
                    {
                        if (mr.Name == "GetPalette" || mr.Name == "get_Colors")
                            palettePath = true;
                    }
                    if (ins.OpCode == OpCodes.Ldstr
                        && ins.Operand is string s
                        && s.Contains("WIC conversion unavailable headless"))
                        loudThrow = true;
                }
                if (palettePath)
                    withPalette++;
                else
                    bad.Add(m.FullName);
                if (loudThrow)
                    withThrow++;
            }
            Assert.True(withPalette > 0,
                "indexed sources must route through a palette-lookup CPU path, got "
                + withPalette + (bad.Count == 0 ? "" : " missing: " + string.Join("; ", bad.Take(5))));
            Assert.True(withThrow > 0,
                "unknown non-indexed pairs must still fail loud, got " + withThrow);
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void PaletteLookup_CallvirtOperands_Resolve()
    {
        // VEN-QC4-W22: the palette-lookup splice synthesizes callvirt
        // operands (GetPalette/get_Colors/get_Item). A hand-built operand
        // that does not resolve detonates live at first worker call
        // (w22: MissingMethodException IReadOnlyList`1.get_Item on every
        // render). Presence alone is not enough — each operand must
        // resolve in the closure (PaintDir + shared framework for BCL).
        const string fileName = "PaintDotNet.Windows.Core.dll";
        string tempOut = LaneTemp($"IdxBgraResolve_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = Transform(fileName, tempOut);
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(PaintDir());
            resolver.AddSearchDirectory(
                Path.GetDirectoryName(typeof(object).Assembly.Location)!);
            var failures = new List<string>();
            var ife = module.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
            Assert.NotNull(ife);
            foreach (var m in ife.Methods.Where(m =>
                m.Name == "CreateFormatConvertedBitmap" && m.HasBody))
            {
                foreach (var ins in m.Body.Instructions)
                {
                    if (ins.Operand is MethodReference mr
                        && (mr.Name == "GetPalette" || mr.Name == "get_Colors"
                            || mr.Name == "get_Item"))
                    {
                        try
                        {
                            var resolved = mr.Resolve();
                            if (resolved is null)
                                failures.Add(m.FullName + " :: " + mr.FullName
                                    + " resolved null");
                        }
                        catch (Exception ex)
                        {
                            failures.Add(m.FullName + " :: " + mr.FullName
                                + " threw " + ex.GetType().Name + ": "
                                + ex.Message.Split('\n')[0]);
                        }
                    }
                }
            }
            Assert.True(failures.Count == 0,
                "palette-lookup operands must resolve, got "
                + failures.Count + ": " + string.Join(" | ", failures.Take(6)));
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void GenericDitherWorker_RoutesIndexedThroughPaletteLookup()
    {
        // The generic CreateFormatConverter<TPixelDst>(source) path (used by
        // Quantize TryGetQuantizer) forwards to the 6-param dither worker,
        // never touching the non-generic overloads. That worker needs the
        // same Indexed8->Bgra32 palette-lookup arm or QC4 still fail-louds
        // (w21 live residual: 263x NotSupportedException).
        const string fileName = "PaintDotNet.Windows.Core.dll";
        string tempOut = LaneTemp($"IdxBgraGen_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = Transform(fileName, tempOut);
            var ife = module.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
            Assert.NotNull(ife);
            var workers = ife.Methods.Where(m => m.Name == "CreateFormatConvertedBitmap"
                && m.HasBody && m.HasGenericParameters
                && m.GenericParameters.Count == 1 && m.Parameters.Count == 6
                && m.Parameters[1].ParameterType.FullName == "PaintDotNet.Imaging.IBitmapSource").ToList();
            Assert.NotEmpty(workers);
            int withPalette = 0;
            foreach (var m in workers)
            {
                bool palettePath = false;
                foreach (var ins in m.Body.Instructions)
                {
                    if (ins.Operand is MethodReference mr
                        && (mr.Name == "GetPalette" || mr.Name == "get_Colors"))
                        palettePath = true;
                }
                if (palettePath)
                    withPalette++;
            }
            Assert.True(withPalette == workers.Count,
                "generic dither worker must route indexed through the palette path, got "
                + withPalette + "/" + workers.Count);
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
