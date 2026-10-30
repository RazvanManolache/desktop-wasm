namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class FixedRenderBoundsTests
{
    [Fact]
    public void Fixed_size_preserves_parent_slot_but_paints_and_hits_only_actual_bounds()
    {
        var editor = new TextBox { Width = 100, Height = 25 };
        editor.Measure(new WpfSize(400, 200));
        editor.Arrange(new WpfRect(0, 0, 400, 200));
        Assert.Equal(new WpfRect(0, 0, 400, 200), editor.ArrangedRect);
        Assert.Equal(new WpfRect(150, 0, 100, 25), editor.RenderRect);
        Assert.True(editor.HitTest(new WpfPoint(200, 12)));
        Assert.False(editor.HitTest(new WpfPoint(10, 12)));
        Assert.False(editor.HitTest(new WpfPoint(200, 30)));
    }

    [Fact]
    public void Stretch_overflow_keeps_the_leading_edge_instead_of_centering_outside_slot()
    {
        var editor = new TextBox { Width = 500, Height = 25 };
        editor.Measure(new WpfSize(400, 200));
        editor.Arrange(new WpfRect(10, 20, 400, 200));
        Assert.Equal(new WpfRect(10, 20, 500, 25), editor.RenderRect);
    }
}
