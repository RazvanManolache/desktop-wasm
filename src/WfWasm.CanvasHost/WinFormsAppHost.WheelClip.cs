using System.Windows.Forms;

namespace WfWasm.CanvasHost;

// RVW3293 wheel-scroll scope: a successful host-driven ListView wheel
// scroll changes only that control's windowed rows (the host-drawn
// ListView canvas branch re-reads the managed top index every frame), so
// the scroll marks the scrolled subtree whole instead of dirtying the
// global frame. A global mark forces every visible self-painted control
// through the full-bitmap vendor OnPaint fallback (TryPaintRegion bails
// when the frame-global dirty rect is null); per-control retained layers
// carry no child pixels, so ancestors and siblings simply re-blit.
// A fault mid-walk falls back to the previous global mark: over-paint is
// always safe, a lost repaint is not.
internal sealed partial class WinFormsAppHostWheelClip
{
    internal static void MarkWheelScrolledSubtree(Control root)
    {
        try
        {
            var stack = new System.Collections.Generic.Stack<Control>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Control c = stack.Pop();
                WasmRepaintGate.MarkDirty(c);
                try
                {
                    foreach (Control child in c.Controls)
                        stack.Push(child);
                }
                catch
                {
                    WasmRepaintGate.MarkGlobalDirty();
                    return;
                }
            }
        }
        catch
        {
            WasmRepaintGate.MarkGlobalDirty();
        }
    }
}
