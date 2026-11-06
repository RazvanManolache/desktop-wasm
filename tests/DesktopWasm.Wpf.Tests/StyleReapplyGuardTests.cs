namespace DesktopWasm.Wpf.Tests;

/// <summary>Style reapply guard (tok-wpf-1941-q7): a trigger-carrying style
/// with a deferred brush base setter must terminate on post-style property
/// changes instead of recursing through PropertyChanged/Reapply until the
/// host overflows. The trigger only wires the subscription; the base
/// setter's per-pass fresh SolidColorBrush defeats Store's Equals guard
/// (see stackoverflow-attribution-20261104T1935Z).</summary>
[ResetAmbientState]
public sealed class StyleReapplyGuardTests
{
    [Fact]
    public void Trigger_style_with_deferred_brush_setter_terminates()
    {
        var border = new Border();
        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.BackgroundProperty, null) { RawValue = "{x:Static Colors.Red}" });
        style.Triggers.Add(new Trigger { Property = "Opacity", Value = 0.5 });
        style.ApplyTo(border);

        var droppedBefore = TriggerEvaluation.ReapplyNestedDroppedCount;
        border.Opacity = 0.9; // pre-guard: unbounded Reapply recursion, host StackOverflow

        Assert.True(TriggerEvaluation.ReapplyNestedDroppedCount > droppedBefore);
        var brush = Assert.IsType<SolidColorBrush>(border.Background);
        Assert.Equal(0xFF0000FFu, brush.ToRgba());
        Assert.Equal(0.9, border.Opacity);
    }
}
