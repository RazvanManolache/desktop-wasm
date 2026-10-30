using System.Configuration;
using System.IO.Packaging;
using System.Printing;
using System.Reflection;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-48: engine counterparts for the OxyPlot view-assembly conversion
/// prerequisites (8 unmapped view types + document/print/settings shims for
/// the 4 missing upstream dependencies), plus the null-source IValueConverter
/// parity fix. Member scope is verified against the staged net8.0-windows
/// OxyPlot binaries and live .NET 10 reflection; device/packaging calls stay
/// precise NotSupported boundaries (EventLog-slice contract).
/// </summary>
[ResetAmbientState]
public sealed class ViewPrereqTests
{
    private sealed class NullRecordingConverter : IValueConverter
    {
        public bool SawNullInput;
        public object? Result = "converted";

        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            SawNullInput = value is null;
            return Result;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class JoinConverter : IMultiValueConverter
    {
        public object? Convert(object?[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            string.Join("+", values.Select(v => v?.ToString() ?? "<null>"));

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class TouchProbeControl : Control
    {
        public bool StartedReached;
        public bool DeltaReached;
        public bool CompletedReached;

        protected override void OnManipulationStarted(ManipulationStartedEventArgs e)
        {
            StartedReached = true;
            base.OnManipulationStarted(e);
        }

        protected override void OnManipulationDelta(ManipulationDeltaEventArgs e)
        {
            DeltaReached = true;
            base.OnManipulationDelta(e);
        }

        protected override void OnManipulationCompleted(ManipulationCompletedEventArgs e)
        {
            CompletedReached = true;
            base.OnManipulationCompleted(e);
        }

        public void RaiseStarted(ManipulationStartedEventArgs e) => OnManipulationStarted(e);
        public void RaiseDelta(ManipulationDeltaEventArgs e) => OnManipulationDelta(e);
        public void RaiseCompleted(ManipulationCompletedEventArgs e) => OnManipulationCompleted(e);
    }

    private sealed class TestPackage : Package
    {
    }

    private sealed class TestSettings : ApplicationSettingsBase
    {
        public TestSettings()
        {
        }
    }

    private sealed class NamedSource
    {
        public string? Name { get; set; }
    }

    [Fact]
    public void TextOptions_round_trips_formatting_mode_with_dp_identity()
    {
        Assert.Same(TextOptions.TextFormattingModeProperty,
            DependencyProperty.FromName(typeof(TextOptions), "TextFormattingMode"));
        Assert.Equal(typeof(TextFormattingMode), TextOptions.TextFormattingModeProperty.PropertyType);
        var block = new TextBlock();
        Assert.Equal(TextFormattingMode.Ideal, TextOptions.GetTextFormattingMode(block));
        TextOptions.SetTextFormattingMode(block, TextFormattingMode.Display);
        Assert.Equal(TextFormattingMode.Display, TextOptions.GetTextFormattingMode(block));
    }

    [Fact]
    public void ClearTypeHint_values_match_live_wpf()
    {
        Assert.Equal(0, (int)ClearTypeHint.Auto);
        Assert.Equal(1, (int)ClearTypeHint.Enabled);
        Assert.Same(RenderOptions.ClearTypeHintProperty,
            DependencyProperty.FromName(typeof(RenderOptions), "ClearTypeHint"));
        Assert.Equal(typeof(ClearTypeHint), RenderOptions.ClearTypeHintProperty.PropertyType);
        var block = new TextBlock();
        Assert.Equal(ClearTypeHint.Auto, RenderOptions.GetClearTypeHint(block));
        RenderOptions.SetClearTypeHint(block, ClearTypeHint.Enabled);
        Assert.Equal(ClearTypeHint.Enabled, RenderOptions.GetClearTypeHint(block));
    }

    [Fact]
    public void ManipulationDelta_carries_translation_and_scale()
    {
        var delta = new ManipulationDelta(new Vector(3, 4), 0, new Vector(2, 2), new Vector(0, 0));
        Assert.Equal(3, delta.Translation.X);
        Assert.Equal(4, delta.Translation.Y);
        Assert.Equal(2, delta.Scale.X);
        Assert.False(delta.GetType().IsValueType);
        var started = new ManipulationStartedEventArgs(new TouchProbeControl(), new Point(5, 6));
        Assert.Equal(5, started.ManipulationOrigin.X);
        Assert.NotNull(started.ManipulationContainer);
        Assert.IsAssignableFrom<InputEventArgs>(started);
        var deltaArgs = new ManipulationDeltaEventArgs(new TouchProbeControl(), new Point(1, 2), delta, delta);
        Assert.Same(delta, deltaArgs.DeltaManipulation);
        Assert.Same(delta, deltaArgs.CumulativeManipulation);
        var completed = new ManipulationCompletedEventArgs(new TouchProbeControl(), new Point(7, 8), delta);
        Assert.Same(delta, completed.TotalManipulation);
        Assert.False(completed.Handled);
        completed.Handled = true;
        Assert.True(completed.Handled);
    }

    [Fact]
    public void Manipulation_overrides_dispatch_through_base()
    {
        var control = new TouchProbeControl();
        Assert.False(control.IsManipulationEnabled);
        control.IsManipulationEnabled = true;
        Assert.True(control.IsManipulationEnabled);
        var origin = new Point(1, 1);
        var delta = new ManipulationDelta(new Vector(1, 0), 0, new Vector(1, 1), new Vector(0, 0));
        control.RaiseStarted(new ManipulationStartedEventArgs(control, origin));
        control.RaiseDelta(new ManipulationDeltaEventArgs(control, origin, delta, delta));
        control.RaiseCompleted(new ManipulationCompletedEventArgs(control, origin, delta));
        Assert.True(control.StartedReached);
        Assert.True(control.DeltaReached);
        Assert.True(control.CompletedReached);
    }

    [Fact]
    public void ValueConversionAttribute_matches_live_wpf_shape()
    {
        var usage = typeof(ValueConversionAttribute).GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
        var attribute = new ValueConversionAttribute(typeof(string), typeof(int));
        Assert.Equal(typeof(string), attribute.SourceType);
        Assert.Equal(typeof(int), attribute.TargetType);
        Assert.Null(attribute.ParameterType);
        attribute.ParameterType = typeof(string);
        Assert.Equal(typeof(string), attribute.ParameterType);
    }

    [Fact]
    public void Packaging_and_xps_types_convert_with_device_boundaries()
    {
        Assert.True(typeof(Package).IsAbstract);
        Assert.True(typeof(SerializerWriter).IsAbstract);
        Assert.True(typeof(XpsDocumentWriter).IsSubclassOf(typeof(SerializerWriter)));
        using var stream = new MemoryStream();
        var open = Assert.Throws<NotSupportedException>(() => Package.Open(stream, FileMode.Create, FileAccess.ReadWrite));
        Assert.Contains("packaging", open.Message, StringComparison.OrdinalIgnoreCase);
        var writer = XpsDocument.CreateXpsDocumentWriter(new XpsDocument(new TestPackage()));
        var canvas = new Canvas();
        var write = Assert.Throws<NotSupportedException>(() => writer.Write(canvas));
        Assert.Contains("XPS serialization", write.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
    }

    [Fact]
    public void Print_spooler_call_is_a_precise_boundary_with_area_assignment()
    {
        PrintDocumentImageableArea? area = null;
        var print = Assert.Throws<NotSupportedException>(() => PrintQueue.CreateXpsDocumentWriter(ref area!));
        Assert.Contains("Printing", print.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(area);
        Assert.Equal(0, area.ExtentWidth);
        Assert.Equal(0, area.ExtentHeight);
        Assert.Equal(0, new PrintDocumentImageableArea().MediaSizeWidth);
    }

    [Fact]
    public void Settings_contract_retains_construction_without_roaming_sync()
    {
        var settings = new TestSettings();
        Assert.Same(settings, SettingsBase.Synchronized(settings));
    }

    [Fact]
    public void Null_source_runs_the_converter_like_wpf()
    {
        // Genuine OxyPlot shape: Visibility bound to a null model through a
        // null-mapping converter with no TargetNullValue (MainWindow.xaml
        // NotNullVisibilityConverter on the not-yet-selected Skia/Canvas models).
        var converter = new NullRecordingConverter { Result = Visibility.Collapsed };
        var host = new TextBlock();
        host.SetBinding("Visibility", new Binding("Name") { Source = new NamedSource { Name = null }, Converter = converter });
        Assert.True(converter.SawNullInput);
        Assert.Equal(Visibility.Collapsed, host.Visibility);
    }

    [Fact]
    public void Null_source_applies_target_null_after_conversion()
    {
        var converter = new NullRecordingConverter { Result = null };
        var host = new TextBlock { Text = "old" };
        host.SetBinding("Text", new Binding("Name")
        {
            Source = new NamedSource { Name = null },
            Converter = converter,
            TargetNullValue = "fallback",
        });
        Assert.True(converter.SawNullInput);
        Assert.Equal("fallback", host.Text);
    }

    [Fact]
    public void Null_source_without_converter_keeps_target_null_value_behavior()
    {
        var host = new TextBlock();
        host.SetBinding("Text", new Binding("Name")
        {
            Source = new NamedSource { Name = null },
            TargetNullValue = "null!",
        });
        Assert.Equal("null!", host.Text);
        var sticky = new TextBlock { Text = "old" };
        sticky.SetBinding("Text", new Binding("Name") { Source = new NamedSource { Name = null } });
        Assert.Equal("old", sticky.Text);
    }

    [Fact]
    public void Null_converter_result_without_target_null_keeps_old_value()
    {
        var converter = new NullRecordingConverter { Result = null };
        var host = new TextBlock { Text = "old" };
        host.SetBinding("Text", new Binding("Name")
        {
            Source = new NamedSource { Name = null },
            Converter = converter,
        });
        Assert.True(converter.SawNullInput);
        Assert.Equal("old", host.Text);
    }

    [Fact]
    public void Multi_child_converter_runs_on_null_like_wpf()
    {
        var childConverter = new NullRecordingConverter { Result = null };
        var multi = new MultiBinding
        {
            Converter = new JoinConverter(),
        };
        multi.Bindings.Add(new Binding("Name")
        {
            Source = new NamedSource { Name = null },
            Converter = childConverter,
            TargetNullValue = "tn",
        });
        var target = new TextBlock();
        Assert.Equal("tn", multi.Evaluate(target));
        Assert.True(childConverter.SawNullInput);
    }
}
