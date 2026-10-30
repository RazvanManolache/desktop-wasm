namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-95 RangeBase dependency-property family: live WPF declares
/// Minimum/Maximum/Value/SmallChange/LargeChange DPs on RangeBase (plus
/// IsIndeterminate on ProgressBar), and converted HandyControl code loads
/// them (TextDialogWithTimer..ctor ldsfld ValueProperty for BeginAnimation;
/// Circle/WaveProgressBar..cctor OverrideMetadata MaximumProperty to 100 and
/// AddOwner IsIndeterminateProperty). Live shapes verified against the
/// 10.0.13 PresentationFramework pack plus a live net10.0-windows runtime
/// probe (names/owners/types/defaults 0/1/0/0.1/1 and IsIndeterminate
/// false; CLR Value is double). The engine rebases the CLR properties onto
/// the DP store with the pinned setter-side coercion unchanged, so
/// converted field loads, AddOwner/OverrideMetadata and
/// BeginAnimation(ValueProperty) bind while the legacy observable contract
/// (clamp-down/up, value clamp, ValueChanged, snapping) holds.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice95RangeBaseDpTests : IDisposable
{
    public Slice95RangeBaseDpTests()
    {
        WpfAnimationClock.ResetForTests();
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        WpfAnimationClock.ResetForTests();
    }

    [Fact]
    public void Dp_identities_match_live()
    {
        Assert.Equal("Minimum", RangeBase.MinimumProperty.Name);
        Assert.Equal(typeof(RangeBase), RangeBase.MinimumProperty.OwnerType);
        Assert.Equal(typeof(double), RangeBase.MinimumProperty.PropertyType);
        Assert.Equal(0.0, RangeBase.MinimumProperty.DefaultMetadata.DefaultValue);
        Assert.Equal("Maximum", RangeBase.MaximumProperty.Name);
        Assert.Equal(typeof(RangeBase), RangeBase.MaximumProperty.OwnerType);
        Assert.Equal(typeof(double), RangeBase.MaximumProperty.PropertyType);
        Assert.Equal(1.0, RangeBase.MaximumProperty.DefaultMetadata.DefaultValue);
        Assert.Equal("Value", RangeBase.ValueProperty.Name);
        Assert.Equal(typeof(RangeBase), RangeBase.ValueProperty.OwnerType);
        Assert.Equal(typeof(double), RangeBase.ValueProperty.PropertyType);
        Assert.Equal(0.0, RangeBase.ValueProperty.DefaultMetadata.DefaultValue);
        Assert.Equal("SmallChange", RangeBase.SmallChangeProperty.Name);
        Assert.Equal(typeof(RangeBase), RangeBase.SmallChangeProperty.OwnerType);
        Assert.Equal(typeof(double), RangeBase.SmallChangeProperty.PropertyType);
        Assert.Equal(0.1, RangeBase.SmallChangeProperty.DefaultMetadata.DefaultValue);
        Assert.Equal("LargeChange", RangeBase.LargeChangeProperty.Name);
        Assert.Equal(typeof(RangeBase), RangeBase.LargeChangeProperty.OwnerType);
        Assert.Equal(typeof(double), RangeBase.LargeChangeProperty.PropertyType);
        Assert.Equal(1.0, RangeBase.LargeChangeProperty.DefaultMetadata.DefaultValue);
        Assert.Equal("IsIndeterminate", ProgressBar.IsIndeterminateProperty.Name);
        Assert.Equal(typeof(ProgressBar), ProgressBar.IsIndeterminateProperty.OwnerType);
        Assert.Equal(typeof(bool), ProgressBar.IsIndeterminateProperty.PropertyType);
        Assert.Equal(false, ProgressBar.IsIndeterminateProperty.DefaultMetadata.DefaultValue);
    }

    [Fact]
    public void Clr_and_store_stay_coherent()
    {
        var slider = new Slider { Minimum = 0, Maximum = 100 };
        slider.Value = 25;
        Assert.Equal(25.0, slider.GetValue(RangeBase.ValueProperty));
        slider.SetValue(RangeBase.ValueProperty, 40.0);
        Assert.Equal(40.0, slider.Value);
        slider.SmallChange = 2;
        Assert.Equal(2.0, slider.GetValue(RangeBase.SmallChangeProperty));
        slider.SetValue(RangeBase.LargeChangeProperty, 20.0);
        Assert.Equal(20.0, slider.LargeChange);
        var bar = new ProgressBar();
        Assert.False(bar.IsIndeterminate);
        bar.SetValue(ProgressBar.IsIndeterminateProperty, true);
        Assert.True(bar.IsIndeterminate);
    }

    [Fact]
    public void Setter_coercion_semantics_preserved()
    {
        // Legacy pins: Minimum coerces down to Maximum, Maximum coerces up
        // to Minimum, Value clamps into the range.
        var slider = new Slider();
        slider.Minimum = 10;
        Assert.Equal(1.0, slider.Minimum);
        slider.Maximum = -3;
        Assert.Equal(1.0, slider.Maximum);
        slider.Minimum = 0;
        slider.Maximum = 100;
        slider.Value = 250;
        Assert.Equal(100.0, slider.Value);
        slider.Value = -5;
        Assert.Equal(0.0, slider.Value);
        Assert.Equal(0.0, slider.GetValue(RangeBase.ValueProperty));
    }

    [Fact]
    public void Value_changed_fires_on_coerced_edits()
    {
        var slider = new Slider { Minimum = 0, Maximum = 100 };
        var events = new List<(double Old, double New)>();
        slider.ValueChanged += (_, e) => events.Add((e.OldValue, e.NewValue));
        slider.Value = 30;
        Assert.Single(events);
        Assert.Equal((0.0, 30.0), events[0]);
        slider.Value = 30;
        Assert.Single(events);
        slider.Value = 500;
        Assert.Equal(2, events.Count);
        Assert.Equal((30.0, 100.0), events[1]);
    }

    [Fact]
    public void Begin_animation_on_value_property_drives_value()
    {
        // Converted TextDialogWithTimer..ctor shape:
        // ProgressBarTimer.BeginAnimation(RangeBase.ValueProperty, animation).
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0 };
        var animation = new DoubleAnimation(0, 100, new Duration(TimeSpan.FromSeconds(5)));
        bar.BeginAnimation(RangeBase.ValueProperty, animation);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(2.5));
        Assert.Equal(50.0, bar.Value, precision: 6);
        WpfAnimationClock.Advance(TimeSpan.FromSeconds(2.5));
        Assert.Equal(100.0, bar.Value, precision: 6);
    }

    [Fact]
    public void Override_metadata_maximum_default_mirror()
    {
        // Converted Circle/WaveProgressBar..cctor shape:
        // RangeBase.MaximumProperty.OverrideMetadata(type, default 100).
        var bar = new OverrideMaximumBar();
        Assert.Equal(100.0, bar.Maximum);
        Assert.Equal(100.0, bar.GetValue(RangeBase.MaximumProperty));
        bar.Value = 250;
        Assert.Equal(100.0, bar.Value);
    }

    private sealed class OverrideMaximumBar : ProgressBar
    {
        static OverrideMaximumBar()
        {
            RangeBase.MaximumProperty.OverrideMetadata(
                typeof(OverrideMaximumBar), new FrameworkPropertyMetadata(100.0));
        }
    }
}
