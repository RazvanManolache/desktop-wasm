extern alias Fork;
extern alias WasmDraw;

using System.Windows.Forms;
using ForkGate = Fork::System.Windows.Forms.WasmRepaintGate;
using WfRect = WasmDraw::System.Drawing.Rectangle;
using WfRegion = WasmDraw::System.Drawing.Region;

namespace WfWasm.WinForms.Tests;

// RVW3293-VEN3: Telerik RTE caret blink (`Caret.Timer_Tick` in
// Telerik.WinControls.RichTextEditor, pristine Bin/Release trace in
// .artifacts/tmp/runs/dw-vendor/rvw3293/probe/rte-tick-il.txt) already
// invalidates the caret rect: `Invalidate(new Region(caretRect), false)`.
// The rewriter preserves the args into `RequestRepaint(control, region,
// invalidateChildren)`. The gate must record the caret rect — a whole
// (null) mark here is what forces the full repaint + full Def upload
// every ~500ms idle.
[Collection("WasmRepaintGate")]
public sealed class CaretRectMarkTests : IDisposable
{
    // Other control tests can leave a global layout wake queued. A caret
    // tick is measured after that initial frame has drained, as in the host.
    public CaretRectMarkTests() => ForkGate.DrainDirty(out _);

    public void Dispose() => ForkGate.DrainDirty(out _);

    [Fact]
    public void CaretRegionMarkRecordsCaretRect()
    {
        using var box = new TextBox();
        _ = box.Handle;
        var caret = new WfRect(359, 468, 2, 19);
        using var region = new WfRegion(caret);
        ForkGate.RequestRepaint(box, region, false);
        try
        {
            bool global = ForkGate.DrainDirty(out var dirty);
            Assert.False(global);
            Assert.True(dirty.TryGetValue(box, out WfRect? mark));
            Assert.NotNull(mark);
            Assert.Equal(caret, mark.Value);
        }
        finally
        {
            ForkGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void CaretRegionMarkSurvivesDrainCycle()
    {
        // A second consecutive blink tick must still scope to the caret
        // rect (no decay to whole-null across frames).
        using var box = new TextBox();
        _ = box.Handle;
        var caret = new WfRect(359, 468, 2, 19);
        for (int i = 0; i < 2; i++)
        {
            using var region = new WfRegion(caret);
            ForkGate.RequestRepaint(box, region, false);
            bool global = ForkGate.DrainDirty(out var dirty);
            Assert.False(global);
            Assert.True(dirty.TryGetValue(box, out WfRect? mark));
            Assert.Equal(caret, mark);
        }
    }
}
