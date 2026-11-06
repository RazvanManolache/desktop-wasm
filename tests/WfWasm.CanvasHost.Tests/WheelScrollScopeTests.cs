using System.Windows.Forms;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsListView = System.Windows.Forms.ListView;

namespace WfWasm.CanvasHost.Tests;

// RVW3293: a successful host-driven ListView wheel scroll marks the whole
// frame globally dirty (OnMouseWheelRaw -> MarkGlobalDirty), so every
// visible self-painted control takes the full-bitmap vendor OnPaint
// fallback on the next frame — including controls the scroll never
// touched. The scrolled ListView is host-drawn from its managed top
// index; only its own layer needs a repaint, siblings just re-blit.
public sealed class WheelScrollScopeTests
{
    private sealed class CountingControl : FormsControl
    {
        public int Paints;
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e) => Paints++;
    }

    private static FormsListView SixRows()
    {
        var list = new FormsListView { Left = 4, Top = 4, Height = 116 };
        for (int i = 0; i < 6; i++)
            list.Items.Add($"row-{i}");
        return list;
    }

    [Fact]
    public void WheelScrolled_ListView_Does_Not_Repaint_Siblings()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        using FormsListView list = SixRows();
        var sibling = new CountingControl { Left = 200, Top = 4, Width = 40, Height = 40 };
        form.Controls.Add(list);
        form.Controls.Add(sibling);
        try
        {
            host.Paint(new RecordingRenderer());
            Assert.Equal(1, sibling.Paints);

            // Park the window at the bottom so a wheel-up notch scrolls.
            Assert.True(host.ScrollListView(list, 99));
            Assert.Equal(2, host.ResolveListViewTop(list));

            int x = form.Location.X + list.Left + 2;
            int y = form.Location.Y + WinFormsAppHost.ChromeHeight(form) + list.Top + 2;
            host.OnMouseWheelRaw(x, y, 0.0, -120.0, 0);

            // The scroll itself must have happened (proves the wheel path ran).
            Assert.Equal(0, host.ResolveListViewTop(list));

            host.Paint(new RecordingRenderer());
            Assert.Equal(1, sibling.Paints);
        }
        finally
        {
            form.Controls.Remove(list);
            form.Controls.Remove(sibling);
            sibling.Dispose();
            WasmRepaintGate.DrainDirty(out _);
        }
    }
}
