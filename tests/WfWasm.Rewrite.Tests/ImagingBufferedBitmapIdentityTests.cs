using Mono.Cecil;
using Mono.Cecil.Cil;

// VEN-BRIDGE-2212: the WASM imaging factory (and its innermost ref) does
// not implement IImagingFactoryInternal, so the null-buffer
// CreateBufferedBitmap overloads throw InterfaceNotSupportedException
// (Quantize Colors-2 TryGetQuantizer wall on live w19). A buffer's
// contract is the same pixels, so the null-buffer overloads must answer
// the source itself (refcounted CreateRef), like the clipper/color cases.
// The explicit-buffer overloads keep their bodies.
public sealed class ImagingBufferedBitmapIdentityTests
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
    public void CreateBufferedBitmap_NullBuffer_Answers_Source_Identity()
    {
        const string fileName = "PaintDotNet.Windows.Core.dll";
        string tempOut = Path.Combine(Path.GetTempPath(), $"ImgBuf_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = Transform(fileName, tempOut);
            var ife = module.GetType("PaintDotNet.Imaging.ImagingFactoryExtensions");
            Assert.NotNull(ife);
            int identity = 0;
            int untouched = 0;
            var bad = new List<string>();
            foreach (var m in ife.Methods)
            {
                if (m.Name != "CreateBufferedBitmap" || !m.HasBody)
                    continue;
                bool log2 = m.Parameters.Count == 3
                    && m.Parameters[0].ParameterType.FullName == "PaintDotNet.Imaging.IImagingFactory"
                    && m.Parameters[2].ParameterType.FullName == "System.Int32";
                if (!log2)
                {
                    untouched++;
                    continue;
                }
                bool callsInternal = false;
                bool callsCreateRef = false;
                foreach (var ins in m.Body.Instructions)
                {
                    if (ins.Operand is MethodReference mr)
                    {
                        if (mr.Name == "GetImagingFactoryInternal")
                            callsInternal = true;
                        if (mr.Name == "CreateRef"
                            && mr.DeclaringType.FullName == "PaintDotNet.ComponentModel.ObjectRefExtensions")
                            callsCreateRef = true;
                    }
                }
                if (!callsInternal && callsCreateRef
                    && m.Body.ExceptionHandlers.Count == 0)
                    identity++;
                else
                    bad.Add(m.FullName);
            }
            Assert.True(identity == 2,
                "both null-buffer overloads (generic + nongeneric) must answer source identity, got "
                + identity + (bad.Count == 0 ? "" : " bad: " + string.Join("; ", bad.Take(5))));
            Assert.True(untouched >= 2,
                "explicit-buffer overloads must keep their bodies, found " + untouched);
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
