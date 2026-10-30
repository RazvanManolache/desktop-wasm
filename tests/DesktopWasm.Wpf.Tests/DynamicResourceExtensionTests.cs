namespace DesktopWasm.Wpf.Tests;

[Collection("ApplicationState")]

/// <summary>DynamicResourceExtension live re-resolution (ScreenToGif.Util
/// DynamicResourceBinding derives from it, overrides ProvideValue, and
/// calls back into base.ProvideValue plus Binding.ProvideValue with an
/// IProvideValueTarget service).</summary>
[ResetAmbientState]
public sealed class DynamicResourceExtensionTests
{
    private sealed class Swatch : FrameworkElement
    {
        public static readonly DependencyProperty TintProperty =
            DependencyProperty.Register("Tint", typeof(object), typeof(Swatch),
                new FrameworkPropertyMetadata(null) { AffectsRender = true });

        public object? Tint
        {
            get => GetValue(TintProperty);
            set => SetValue(TintProperty, value);
        }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label", typeof(string), typeof(Swatch),
                new FrameworkPropertyMetadata(string.Empty));

        public string Label
        {
            get => (string)(GetValue(LabelProperty) ?? string.Empty);
            set => SetValue(LabelProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
        }
    }

    private sealed class ValueTarget(object targetObject, object targetProperty) : IProvideValueTarget
    {
        public object TargetObject => targetObject;
        public object TargetProperty => targetProperty;
    }

    private sealed class TargetProvider(IProvideValueTarget target) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IProvideValueTarget) ? target : null;
    }

    private static Application UseApp()
    {
        return new Application();
    }

    private static void RestoreApp(Application? previous) => Application.Current = previous;

    private static IServiceProvider ProviderFor(object target, object property) =>
        new TargetProvider(new ValueTarget(target, property));

    [Fact]
    public void Provide_value_resolves_and_lands_on_the_target_property()
    {
        var previous = Application.Current;
        try
        {
            var app = UseApp();
            app.Resources.Add("Accent", "red");
            var swatch = new Swatch();

            var extension = new DynamicResourceExtension("Accent");
            Assert.Equal("Accent", extension.ResourceKey);

            var value = extension.ProvideValue(ProviderFor(swatch, Swatch.TintProperty));
            Assert.Equal("red", value);
            Assert.Equal("red", swatch.Tint);
        }
        finally
        {
            RestoreApp(previous);
        }
    }

    [Fact]
    public void Missing_key_returns_null_without_throwing()
    {
        var previous = Application.Current;
        try
        {
            UseApp();
            var swatch = new Swatch();

            var extension = new DynamicResourceExtension("Absent");
            Assert.Null(extension.ProvideValue(ProviderFor(swatch, Swatch.TintProperty)));
            Assert.Null(swatch.Tint);
        }
        finally
        {
            RestoreApp(previous);
        }
    }

    [Fact]
    public void Resource_swap_reapplies_dependents_and_repaints()
    {
        var previous = Application.Current;
        try
        {
            var app = UseApp();
            app.Resources.Add("Accent", "red");
            var swatch = new Swatch();

            var extension = new DynamicResourceExtension("Accent");
            extension.ProvideValue(ProviderFor(swatch, Swatch.TintProperty));
            swatch.ClearInvalidation();
            Assert.False(swatch.IsRenderDirty);

            app.Resources["Accent"] = "blue";

            Assert.Equal("blue", swatch.Tint);
            Assert.True(swatch.IsRenderDirty);

            // A late key appearance resolves waiting subscribers too.
            var late = new Swatch();
            var lateExtension = new DynamicResourceExtension("Late");
            Assert.Null(lateExtension.ProvideValue(ProviderFor(late, Swatch.TintProperty)));
            app.Resources.Add("Late", "green");
            Assert.Equal("green", late.Tint);
        }
        finally
        {
            RestoreApp(previous);
        }
    }

    [Fact]
    public void Border_brush_dp_write_syncs_paint_state()
    {
        var brush = new SolidColorBrush(Colors.Red);
        var expected = new Border { BorderBrush = brush };
        var actual = new Border();
        actual.SetValue(Border.BorderBrushProperty, brush);
        Assert.Equal(expected.BorderRgba, actual.BorderRgba);
        Assert.NotNull(actual.BorderRgba);
        Assert.True(actual.IsRenderDirty);
    }

    [Fact]
    public void Provide_value_throws_on_null_key()
    {
        var previous = Application.Current;
        try
        {
            UseApp();
            var swatch = new Swatch();
            var extension = new DynamicResourceExtension();
            Assert.Throws<InvalidOperationException>(() =>
                extension.ProvideValue(ProviderFor(swatch, Swatch.TintProperty)));
        }
        finally
        {
            RestoreApp(previous);
        }
    }

    private sealed class PrefixConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            "!" + value?.ToString();

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class ResourceCell : Freezable
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(object), typeof(ResourceCell),
                new FrameworkPropertyMetadata(null));

        public object? Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        protected override Freezable CreateInstanceCore() => new ResourceCell();
    }

    private sealed class ConverterBinding : DynamicResourceExtension
    {
        public ConverterBinding(object key)
            : base(key)
        {
        }

        public IValueConverter? Converter { get; set; }

        // Mirrors ScreenToGif.Util DynamicResourceBinding.ProvideValue: the
        // base call resolves the resource, the Freezable cell carries it,
        // and the converter binding lands the converted value.
        public override object? ProvideValue(IServiceProvider serviceProvider)
        {
            var resourceReferenceExpression = base.ProvideValue(serviceProvider);
            if (Converter is null)
                return resourceReferenceExpression;
            var cell = new ResourceCell { Value = resourceReferenceExpression };
            var targetInfo = (IProvideValueTarget?)serviceProvider.GetService(typeof(IProvideValueTarget));
            var targetElement = targetInfo?.TargetObject as FrameworkElement;
            targetElement?.Resources.Add(cell, cell);
            var binding = new Binding
            {
                Path = new PropertyPath(ResourceCell.ValueProperty),
                Source = cell,
                Converter = Converter,
                Mode = BindingMode.OneWay,
            };
            return targetElement is not null ? binding.ProvideValue(serviceProvider) : binding;
        }
    }

    [Fact]
    public void Converter_binding_pattern_lands_converted_values()
    {
        var previous = Application.Current;
        try
        {
            var app = UseApp();
            app.Resources.Add("Accent", "red");
            var swatch = new Swatch { Label = "before" };

            var extension = new ConverterBinding("Accent") { Converter = new PrefixConverter() };
            var landed = extension.ProvideValue(ProviderFor(swatch, Swatch.LabelProperty));

            Assert.NotNull(landed);
            Assert.Equal("!red", swatch.Label);
        }
        finally
        {
            RestoreApp(previous);
        }
    }

    [Fact]
    public void Binding_provide_value_attaches_a_live_transfer()
    {
        var previous = Application.Current;
        try
        {
            UseApp();
            var swatch = new Swatch { Label = "before" };
            var binding = new Binding
            {
                Source = "hello",
                Converter = new PrefixConverter(),
            };

            var expression = binding.ProvideValue(
                ProviderFor(swatch, Swatch.LabelProperty)) as BindingExpressionBase;

            Assert.NotNull(expression);
            Assert.Equal("!hello", swatch.Label);
        }
        finally
        {
            RestoreApp(previous);
        }
    }
}
