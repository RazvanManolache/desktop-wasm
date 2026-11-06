using WfWasm.Rewrite;
using Xunit;

namespace WfWasm.Rewrite.Tests;

// RVW3293-VEN3: the Telerik RTE caret blink already invalidates the caret
// rect at its source (`Caret.Timer_Tick` in Telerik.WinControls.
// RichTextEditor: `Invalidate(new Region(caretRect), false)` — pristine IL
// trace in .artifacts/tmp/runs/dw-vendor/rvw3293/probe/rte-tick-il.txt).
// The rewrite must preserve that rect into the fork gate twin
// `RequestRepaint(control, region, invalidateChildren)`; dropping the
// region (or the bool) would widen every ~500ms blink to a whole repaint
// + full Def upload. Uses the frozen pristine input read-only (same as
// RepaintGateRewriteTests); converted output goes to the lane-local
// project temp dir, never E:/tmp.
public sealed class RteCaretBlinkRewriteTests
{
    private const string RteInput = @"E:\AI\Code\WindowsForms\telerik\Telerik UI for WinForms demos\Bin\Release\Telerik.WinControls.RichTextEditor.dll";

    private static string ShimDll(string project, string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", project, "bin", "Debug", "net9.0", name + ".dll"));
        Assert.True(File.Exists(dll), $"shim not built: {dll}");
        return dll;
    }

    private static System.Collections.Generic.IEnumerable<Mono.Cecil.MethodDefinition> AllMethods(
        System.Collections.Generic.IEnumerable<Mono.Cecil.TypeDefinition> types)
    {
        foreach (var t in types)
        {
            foreach (var m in t.Methods)
                yield return m;
            foreach (var m in AllMethods(t.NestedTypes))
                yield return m;
        }
    }

    [Fact]
    public void Rewrite_RteCaretTick_Preserves_CaretRect_Invalidate()
    {
        Assert.True(File.Exists(RteInput), $"rte input missing: {RteInput}");
        string laneDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            ".artifacts", "tmp", "runs", "dw-vendor", "rvw3293"));
        Directory.CreateDirectory(laneDir);
        string tempOut = Path.Combine(laneDir, $"RteCaretTick_{Guid.NewGuid():N}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(RteInput, map);
            AssemblyRewriter.Rewrite(RteInput, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var caret = AllMethods(asm.MainModule.Types)
                .FirstOrDefault(m => m.DeclaringType.FullName ==
                    "Telerik.WinControls.RichTextEditor.UI.Caret"
                    && m.Name == "Timer_Tick");
            Assert.NotNull(caret);
            Assert.True(caret!.HasBody);
            var calls = caret.Body.Instructions
                .Select(i => i.Operand as Mono.Cecil.MethodReference)
                .Where(mr => mr is not null
                    && (mr.Name == "Invalidate" || mr.Name == "RequestRepaint"))
                .ToList();
            // The whole-Control.Invalidate must be gone from the blink.
            Assert.DoesNotContain(calls, mr =>
                mr!.Name == "Invalidate"
                && mr.DeclaringType.FullName == "System.Windows.Forms.Control");
            // The caret rect + children flag must survive intact.
            Assert.Contains(calls, mr =>
                mr!.Name == "RequestRepaint"
                && mr.DeclaringType.FullName == "System.Windows.Forms.WasmRepaintGate"
                && mr.Parameters.Count == 3
                && mr.Parameters[1].ParameterType.FullName == "System.Drawing.Region"
                && mr.Parameters[2].ParameterType.FullName == "System.Boolean");
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
}
