extern alias WfDraw;

using System.Reflection;
using Xunit;

// REGRESSION (tile-layout Form1 ctor NRE): imageStreamerN.Images entries
// are BinaryFormatter-serialized List<System.Drawing.Image> under an
// UnknownType slot (Syncfusion component model, NOT WinForms MSFt).
// SafeGetObject recovers the healthy empty list; the 15 live entries
// are identical 367-byte placeholders (no real bitmap fits).
public sealed class TempTileLayoutResolve
{
    // TMPMOVE canonical-only: fixture under retained/.
    private static string RepoRoot =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static string TileLayoutFixture
    {
        get
        {
            var rel = System.IO.Path.Combine("wfwasm-inputs", "syncfusion-winforms-demos", "layout", "Tile layout", "TileLayout", "CS", "obj", "Debug", "net9.0-windows", "TileLayoutDemo_2008.dll");
            var canon = System.IO.Path.Combine(RepoRoot, ".artifacts", "tmp", "retained", rel);
            return canon;
        }
    }
    private static string RunsDir =>
        System.IO.Path.Combine(RepoRoot, ".artifacts", "tmp", "runs", "drawing-temp");

    [Fact]
    public void Temp_TileLayout_ImageStreamers_ResolveToEmptyLists()
    {
        var asm = Assembly.LoadFrom(TileLayoutFixture);
        var rm = new System.Resources.ResourceManager("TileLayoutDemo_2008.Form1", asm);
        var lines = new System.Collections.Generic.List<string>();
        foreach (string key in new[] { "imageStreamer1.Images", "imageStreamer2.Images", "imageStreamer9.Images" })
        {
            object? value = WfDraw::WfWasm.Drawing.ResourceHelper.SafeGetObject(rm, key);
            var list = value as System.Collections.Generic.List<WfDraw::System.Drawing.Image>;
            lines.Add($"{key} type={(value is null ? "null" : value.GetType().FullName)} count={(list is null ? -1 : list.Count)}");
            Assert.NotNull(value);
            Assert.NotNull(list);
            Assert.Empty(list);
        }
        System.IO.Directory.CreateDirectory(RunsDir);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(RunsDir, "tilelayout-resolve.txt"), lines);
    }
}
