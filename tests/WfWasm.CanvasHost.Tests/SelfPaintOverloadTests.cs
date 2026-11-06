// PUSH1106-runtime-maskededit-0430: Syncfusion AutoLabel declares multiple
// OnPaint overloads, so name-only GetMethod("OnPaint") throws
// AmbiguousMatchException. Served shape: EditorStateProbe.Dump dies on the
// first AutoLabel (masked-edit leg1: control_count 0, whole oracle lost).
// The self-paint gates must resolve the exact PaintEventArgs override and
// never throw on overloaded paint methods.
using WfWasm.CanvasHost;

public sealed class SelfPaintOverloadTests
{
    private sealed class PlainControl : System.Windows.Forms.Control { }

    private sealed class OverloadedPaintControl : System.Windows.Forms.Control
    {
        public void OnPaint() { }
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e) { base.OnPaint(e); }
    }

    private sealed class AmbiguousButton : System.Windows.Forms.Button
    {
        public void OnPaint() { }
    }

    [Fact]
    public void Overloaded_OnPaint_Does_Not_Throw_And_Self_Paints()
    {
        // RED pre-fix: AmbiguousMatchException out of TypeSelfPaints.
        Assert.True(WinFormsAppHost.TypeSelfPaints(typeof(OverloadedPaintControl)));
    }

    [Fact]
    public void Plain_Control_Does_Not_Self_Paint()
    {
        Assert.False(WinFormsAppHost.TypeSelfPaints(typeof(PlainControl)));
    }

    [Fact]
    public void Ambiguous_Button_Subclass_Does_Not_Throw()
    {
        // RED pre-fix: AmbiguousMatchException out of ButtonPaintsItself.
        Assert.False(WinFormsAppHost.ButtonPaintsItself(typeof(AmbiguousButton)));
    }
}
