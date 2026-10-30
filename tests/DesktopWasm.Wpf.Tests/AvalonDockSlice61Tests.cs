namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-61 AvalonDock remainder: the last 8 AvalonDock unmapped rows
/// (DependencyPropertyHelper/ValueSource/BaseValueSource,
/// GridLengthConverter, FrameworkElementFactory, StyleSelector,
/// IconBitmapDecoder, DispatcherOperationCallback) plus the VS-theme
/// MarkupExtensionReturnTypeAttribute row. Every shape verified live against
/// the 9.0.21 WindowsDesktop ref (Cecil dumps) and runtime probes for
/// GetValueSource provenance and GridLengthConverter parsing; the TestApp
/// entry stays WinForms-blocked (out of scope), so no boot oracle runs here.
/// </summary>
[ResetAmbientState]
public sealed class AvalonDockSlice61Tests
{
    private sealed class InheritsHost : FrameworkElement
    {
        public static readonly DependencyProperty NoteProperty =
            DependencyProperty.Register("Note", typeof(string), typeof(InheritsHost),
                new FrameworkPropertyMetadata("dflt", FrameworkPropertyMetadataOptions.Inherits));
    }

    private sealed class CoercedHost : FrameworkElement
    {
        public static readonly DependencyProperty LevelProperty =
            DependencyProperty.Register("Level", typeof(double), typeof(CoercedHost),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.None,
                    null, (d, v) => Math.Min((double)v!, 10.0)));
    }

    private sealed class FixedStyleSelector : StyleSelector
    {
        private readonly Style _style;
        public FixedStyleSelector(Style style) => _style = style;
        public override Style? SelectStyle(object item, DependencyObject container) => _style;
    }

    private sealed class SampleExtension : MarkupExtension
    {
        public override object? ProvideValue(IServiceProvider serviceProvider) => "sample";
    }

    private sealed class StubTarget : IProvideValueTarget
    {
        public StubTarget(object target, object property) { TargetObject = target; TargetProperty = property; }
        public object TargetObject { get; }
        public object TargetProperty { get; }
    }

    private sealed class StubServices : IServiceProvider
    {
        private readonly object? _target;
        public StubServices(object? target) => _target = target;
        public object? GetService(Type serviceType) => _target;
    }

    [Fact]
    public void BaseValueSource_numerics_match_wpf()
    {
        Assert.Equal(0, (int)BaseValueSource.Unknown);
        Assert.Equal(1, (int)BaseValueSource.Default);
        Assert.Equal(2, (int)BaseValueSource.Inherited);
        Assert.Equal(3, (int)BaseValueSource.DefaultStyle);
        Assert.Equal(4, (int)BaseValueSource.DefaultStyleTrigger);
        Assert.Equal(5, (int)BaseValueSource.Style);
        Assert.Equal(6, (int)BaseValueSource.TemplateTrigger);
        Assert.Equal(7, (int)BaseValueSource.StyleTrigger);
        Assert.Equal(8, (int)BaseValueSource.ImplicitStyleReference);
        Assert.Equal(9, (int)BaseValueSource.ParentTemplate);
        Assert.Equal(10, (int)BaseValueSource.ParentTemplateTrigger);
        Assert.Equal(11, (int)BaseValueSource.Local);
    }

    [Fact]
    public void GetValueSource_default_local_cleared()
    {
        var button = new Button();
        var unset = DependencyPropertyHelper.GetValueSource(button, FrameworkElement.WidthProperty);
        Assert.Equal(BaseValueSource.Default, unset.BaseValueSource);
        Assert.False(unset.IsExpression || unset.IsAnimated || unset.IsCoerced || unset.IsCurrent);

        button.Width = 100;
        var local = DependencyPropertyHelper.GetValueSource(button, FrameworkElement.WidthProperty);
        Assert.Equal(BaseValueSource.Local, local.BaseValueSource);
        Assert.False(local.IsExpression || local.IsAnimated || local.IsCoerced || local.IsCurrent);

        button.ClearValue(FrameworkElement.WidthProperty);
        Assert.Equal(BaseValueSource.Default,
            DependencyPropertyHelper.GetValueSource(button, FrameworkElement.WidthProperty).BaseValueSource);
    }

    [Fact]
    public void GetValueSource_guards_null()
    {
        var button = new Button();
        Assert.Throws<ArgumentNullException>(() => DependencyPropertyHelper.GetValueSource(null!, FrameworkElement.WidthProperty));
        Assert.Throws<ArgumentNullException>(() => DependencyPropertyHelper.GetValueSource(button, null!));
        Assert.Throws<ArgumentNullException>(() => DependencyPropertyHelper.IsTemplatedValueDynamic(null!, FrameworkElement.WidthProperty));
    }

    [Fact]
    public void Styled_value_reports_Style()
    {
        var button = new Button();
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 77.0));
        style.ApplyTo(button);
        Assert.Equal(77.0, button.Width);
        Assert.Equal(BaseValueSource.Style,
            DependencyPropertyHelper.GetValueSource(button, FrameworkElement.WidthProperty).BaseValueSource);
        button.Width = 78;
        Assert.Equal(BaseValueSource.Local,
            DependencyPropertyHelper.GetValueSource(button, FrameworkElement.WidthProperty).BaseValueSource);
    }

    [Fact]
    public void Inactive_trigger_keeps_Default_active_reports_StyleTrigger()
    {
        var idle = new Button { IsEnabled = true };
        var idleStyle = new Style(typeof(Button));
        var idleTrigger = new Trigger { Property = "IsEnabled", Value = false };
        idleTrigger.Setters.Add(new Setter(FrameworkElement.WidthProperty, 33.0));
        idleStyle.Triggers.Add(idleTrigger);
        idleStyle.ApplyTo(idle);
        Assert.Equal(BaseValueSource.Default,
            DependencyPropertyHelper.GetValueSource(idle, FrameworkElement.WidthProperty).BaseValueSource);

        var disabled = new Button { IsEnabled = false };
        var style = new Style(typeof(Button));
        var trigger = new Trigger { Property = "IsEnabled", Value = false };
        trigger.Setters.Add(new Setter(FrameworkElement.WidthProperty, 33.0));
        style.Triggers.Add(trigger);
        style.ApplyTo(disabled);
        Assert.Equal(33.0, disabled.Width);
        Assert.Equal(BaseValueSource.StyleTrigger,
            DependencyPropertyHelper.GetValueSource(disabled, FrameworkElement.WidthProperty).BaseValueSource);
    }

    [Fact]
    public void TemplateTrigger_source_and_templated_guard()
    {
        var plain = new Button();
        plain.SetValueWithSource(FrameworkElement.WidthProperty, 44.0, BaseValueSource.TemplateTrigger);
        // Off-template the dynamic check keeps the WPF ArgumentException guard.
        var ex = Assert.Throws<ArgumentException>(() =>
            DependencyPropertyHelper.IsTemplatedValueDynamic(plain, FrameworkElement.WidthProperty));
        Assert.Contains("FrameworkTemplate", ex.Message);
        Assert.Equal(BaseValueSource.TemplateTrigger,
            DependencyPropertyHelper.GetValueSource(plain, FrameworkElement.WidthProperty).BaseValueSource);
    }

    [Fact]
    public void Inherited_reports_only_with_ancestor_local()
    {
        var parent = new InheritsHost();
        var child = new InheritsHost();
        parent.AddChild(child);
        Assert.Equal(BaseValueSource.Default,
            DependencyPropertyHelper.GetValueSource(child, InheritsHost.NoteProperty).BaseValueSource);
        parent.SetValue(InheritsHost.NoteProperty, "up");
        Assert.Equal(BaseValueSource.Inherited,
            DependencyPropertyHelper.GetValueSource(child, InheritsHost.NoteProperty).BaseValueSource);
    }

    [Fact]
    public void Bound_value_reports_Local_with_IsExpression()
    {
        var source = new Button { Width = 42 };
        var target = new Button();
        BindingOperations.SetBinding(target, FrameworkElement.WidthProperty,
            new Binding("Width") { Source = source });
        Assert.NotNull(BindingOperations.GetBindingExpression(target, FrameworkElement.WidthProperty));
        var viewed = DependencyPropertyHelper.GetValueSource(target, FrameworkElement.WidthProperty);
        Assert.Equal(BaseValueSource.Local, viewed.BaseValueSource);
        Assert.True(viewed.IsExpression);
        Assert.False(viewed.IsAnimated || viewed.IsCoerced || viewed.IsCurrent);
    }

    [Fact]
    public void DynamicResource_reports_IsExpression()
    {
        var element = new Button();
        var brush = new SolidColorBrush(Color.FromArgb(255, 21, 22, 23));
        element.Resources["accent"] = brush;
        var extension = new DynamicResourceExtension("accent");
        var provided = extension.ProvideValue(new StubServices(
            new StubTarget(element, Control.BackgroundProperty)));
        Assert.Same(brush, provided);
        Assert.True(Application.DynamicResourceManager.HasPropertySubscription(element, Control.BackgroundProperty));
        var viewed = DependencyPropertyHelper.GetValueSource(element, Control.BackgroundProperty);
        Assert.Equal(BaseValueSource.Local, viewed.BaseValueSource);
        Assert.True(viewed.IsExpression);
    }

    [Fact]
    public void SetCurrentValue_reports_Local_Coerced_Current()
    {
        var button = new Button { Width = 100 };
        button.SetCurrentValue(FrameworkElement.WidthProperty, 55.0);
        Assert.Equal(55.0, button.Width);
        var viewed = DependencyPropertyHelper.GetValueSource(button, FrameworkElement.WidthProperty);
        Assert.Equal(BaseValueSource.Local, viewed.BaseValueSource);
        Assert.True(viewed.IsCoerced);
        Assert.True(viewed.IsCurrent);
        Assert.False(viewed.IsExpression || viewed.IsAnimated);
        button.Width = 56;
        var reset = DependencyPropertyHelper.GetValueSource(button, FrameworkElement.WidthProperty);
        Assert.False(reset.IsCoerced || reset.IsCurrent);
    }

    [Fact]
    public void GridLengthConverter_matches_live_runtime()
    {
        // Every row verified live against the 9.0.21 runtime probe.
        var converter = new GridLengthConverter();
        Assert.True(converter.CanConvertFrom(typeof(string)));
        Assert.False(converter.CanConvertFrom(typeof(double)));
        Assert.True(converter.CanConvertTo(typeof(string)));
        Assert.False(converter.CanConvertTo(typeof(GridLength)));
        Assert.Equal(GridLength.Auto, converter.ConvertFrom(null, null, "Auto"));
        Assert.Equal(GridLength.Auto, converter.ConvertFrom(null, null, "auto"));
        Assert.Equal(GridLength.Star(), converter.ConvertFrom(null, null, "*"));
        Assert.Equal(GridLength.Star(2), converter.ConvertFrom(null, null, "2*"));
        Assert.Equal(GridLength.Star(2.5), converter.ConvertFrom(null, null, "2.5*"));
        Assert.Equal(GridLength.Pixel(100), converter.ConvertFrom(null, null, "100"));
        Assert.Equal(GridLength.Pixel(42), converter.ConvertFrom(null, null, " 42 "));
        Assert.Equal(GridLength.Pixel(96), converter.ConvertFrom(null, null, "1in"));
        Assert.Equal(GridLength.Pixel(-5), converter.ConvertFrom(null, null, "-5"));
        Assert.Throws<FormatException>(() => converter.ConvertFrom(null, null, "abc"));
        Assert.Throws<FormatException>(() => converter.ConvertFrom(null, null, string.Empty));
        Assert.Throws<ArgumentException>(() => converter.ConvertFrom(null, null, "Infinity"));
        Assert.Throws<NotSupportedException>(() => converter.ConvertFrom(null, null, 12.0));
        Assert.Equal("Auto", converter.ConvertTo(null, null, GridLength.Auto, typeof(string)));
        Assert.Equal("*", converter.ConvertTo(null, null, GridLength.Star(), typeof(string)));
        Assert.Equal("2*", converter.ConvertTo(null, null, GridLength.Star(2), typeof(string)));
        Assert.Equal("100", converter.ConvertTo(null, null, GridLength.Pixel(100), typeof(string)));
        Assert.Throws<NotSupportedException>(() => converter.ConvertTo(null, null, GridLength.Auto, typeof(GridLength)));
        var attribute = (System.ComponentModel.TypeConverterAttribute)Attribute.GetCustomAttribute(
            typeof(GridLength), typeof(System.ComponentModel.TypeConverterAttribute))!;
        Assert.Equal(typeof(GridLengthConverter).AssemblyQualifiedName, attribute.ConverterTypeName);
    }

    [Fact]
    public void FrameworkElementFactory_shape_and_chains()
    {
        var empty = new FrameworkElementFactory();
        Assert.Null(empty.Type);
        Assert.Null(empty.Name);
        Assert.Null(empty.Text);
        Assert.Null(empty.FirstChild);
        Assert.Null(empty.Parent);
        Assert.Null(empty.NextSibling);
        Assert.False(empty.IsSealed);

        var named = new FrameworkElementFactory(typeof(Button), "Bd");
        Assert.Same(typeof(Button), named.Type);
        Assert.Equal("Bd", named.Name);

        var root = new FrameworkElementFactory(typeof(Decorator));
        var first = new FrameworkElementFactory(typeof(TextBlock));
        var second = new FrameworkElementFactory(typeof(TextBlock));
        root.AppendChild(first);
        root.AppendChild(second);
        Assert.Same(first, root.FirstChild);
        Assert.Same(root, first.Parent);
        Assert.Same(second, first.NextSibling);
        Assert.Null(second.NextSibling);
        Assert.Null(root.NextSibling);
        Assert.Throws<InvalidOperationException>(() =>
            new FrameworkElementFactory(typeof(Decorator)).AppendChild(first));
        Assert.Throws<ArgumentNullException>(() => root.AppendChild(null!));

        root.Seal();
        Assert.True(root.IsSealed);
        Assert.True(first.IsSealed);
        Assert.Throws<InvalidOperationException>(() => root.SetValue(Control.BackgroundProperty, null));
        Assert.Throws<InvalidOperationException>(() => root.SetBinding(Control.BackgroundProperty, new Binding()));
        Assert.Throws<InvalidOperationException>(() => root.SetResourceReference(Control.BackgroundProperty, "k"));
        Assert.Throws<InvalidOperationException>(() => root.AddHandler(Control.MouseDoubleClickEvent, new Action(() => { })));
        Assert.Throws<InvalidOperationException>(() => root.AppendChild(new FrameworkElementFactory(typeof(TextBlock))));
    }

    [Fact]
    public void FrameworkElementFactory_creates_fresh_working_trees()
    {
        // Mirrors the converted AvalonDock show/hide button template: a named
        // Decorator root with a Text value child plus a RelativeSource binding.
        var root = new FrameworkElementFactory(typeof(Decorator), "Bd");
        root.SetValue(FrameworkElement.WidthProperty, 55.0);
        var child = new FrameworkElementFactory(typeof(TextBlock));
        child.Text = "hi";
        child.SetValue(FrameworkElement.HeightProperty, 12.0);
        var source = new Button { Width = 9 };
        child.SetBinding(FrameworkElement.WidthProperty, new Binding("Width") { Source = source });
        root.AppendChild(child);

        var first = (Decorator)root.CreateInstance();
        Assert.Equal("Bd", first.Name);
        Assert.Equal(55.0, first.Width);
        var text = Assert.IsType<TextBlock>(first.Child);
        Assert.Equal("hi", text.Text);
        Assert.Equal(12.0, text.Height);
        Assert.NotNull(BindingOperations.GetBindingExpression(text, FrameworkElement.WidthProperty));
        var second = (Decorator)root.CreateInstance();
        Assert.NotSame(first, second);
        Assert.NotSame(text, second.Child);

        var missing = new FrameworkElementFactory(typeof(TextBlock));
        missing.SetResourceReference(Control.BackgroundProperty, "no-such-key");
        Assert.Throws<InvalidOperationException>(() => missing.CreateInstance());

        Assert.Throws<InvalidOperationException>(() => new FrameworkElementFactory().CreateInstance());
        Assert.Throws<InvalidOperationException>(() => new FrameworkElementFactory(typeof(string)).CreateInstance());
    }

    [Fact]
    public void VisualTree_installation_inflates_and_seals()
    {
        var root = new FrameworkElementFactory(typeof(Decorator), "Bd");
        var child = new FrameworkElementFactory(typeof(TextBlock));
        child.Text = "part";
        root.AppendChild(child);

        var template = new ControlTemplate { TargetType = typeof(Button) };
        ((FrameworkTemplate)template).VisualTree = root;
        Assert.True(root.IsSealed);
        var retained = Assert.IsType<Decorator>(template.VisualTree);
        Assert.Equal("part", Assert.IsType<TextBlock>(retained.Child).Text);

        var fresh = Assert.IsType<Decorator>(template.LoadContent());
        Assert.NotSame(retained, fresh);
        Assert.Equal("part", Assert.IsType<TextBlock>(fresh.Child).Text);

        var empty = new ControlTemplate();
        Assert.Throws<InvalidOperationException>(() => empty.LoadContent());
    }

    [Fact]
    public void StyleSelector_base_returns_null_override_wins()
    {
        var plain = new StyleSelector();
        var container = new Button();
        Assert.Null(plain.SelectStyle("item", container));

        var style = new Style(typeof(Button));
        var fixed_ = new FixedStyleSelector(style);
        Assert.Same(style, fixed_.SelectStyle("item", container));
    }

    [Fact]
    public void IconBitmapDecoder_decodes_and_narrows()
    {
        Assert.True(typeof(IconBitmapDecoder).IsSealed);
        Assert.True(typeof(IconBitmapDecoder).IsSubclassOf(typeof(BitmapDecoder)));
        Assert.NotNull(typeof(IconBitmapDecoder).GetConstructor([typeof(Uri), typeof(BitmapCreateOptions), typeof(BitmapCacheOption)]));
        Assert.NotNull(typeof(IconBitmapDecoder).GetConstructor([typeof(Stream), typeof(BitmapCreateOptions), typeof(BitmapCacheOption)]));
        Assert.Throws<ArgumentNullException>(() => new IconBitmapDecoder((Uri)null!, BitmapCreateOptions.None, BitmapCacheOption.Default));
        Assert.Throws<ArgumentNullException>(() => new IconBitmapDecoder((Stream)null!, BitmapCreateOptions.None, BitmapCacheOption.Default));

        var bitmap = new WriteableBitmap(2, 1, 96, 96, PixelFormats.Bgra32, null);
        var rgba = new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 };
        bitmap.WritePixels(new Int32Rect(0, 0, 2, 1), rgba, 8, 0);
        byte[] png;
        using (var encoded = new MemoryStream())
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(encoded);
            png = encoded.ToArray();
        }
        using var stream = new MemoryStream(png);
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.Default);
        Assert.IsAssignableFrom<BitmapDecoder>(decoder);
        var frame = Assert.Single(decoder.Frames);
        Assert.Same(decoder, frame.Decoder);
        Assert.True(frame.Decoder is IconBitmapDecoder);
        Assert.Null(frame.Thumbnail);
        Assert.Equal(2, frame.PixelWidth);
        Assert.Equal(1, frame.PixelHeight);
        var bulk = new byte[8];
        frame.CopyPixels(bulk, 8, 0);
        Assert.Equal(rgba, bulk);
        Assert.Null(BitmapFrame.Create(bitmap).Decoder);
    }

    [Fact]
    public void DispatcherOperationCallback_matches_framework_shape()
    {
        Assert.Equal(typeof(object), typeof(DispatcherOperationCallback).GetMethod("Invoke")!.ReturnType);
        Assert.Equal([typeof(object)], typeof(DispatcherOperationCallback).GetMethod("Invoke")!.GetParameters().Select(p => p.ParameterType));
        var callback = new DispatcherOperationCallback(arg => arg ?? "null");
        Assert.Equal("seen", callback.DynamicInvoke("seen"));
    }

    [Fact]
    public void MarkupExtensionReturnTypeAttribute_matches_framework_shape()
    {
        var type = typeof(MarkupExtensionReturnTypeAttribute);
        Assert.True(type.IsSealed);
        Assert.True(type.IsSubclassOf(typeof(Attribute)));
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(type, typeof(AttributeUsageAttribute))!;
        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.NotNull(type.GetConstructor([typeof(Type)]));
        Assert.NotNull(type.GetConstructor([]));
        Assert.NotNull(type.GetConstructor([typeof(Type), typeof(Type)]));
        Assert.Equal(typeof(SampleExtension), new MarkupExtensionReturnTypeAttribute(typeof(SampleExtension)).ReturnType);
    }

    [Fact]
    public void ReadOnly_and_GetMetadata_match_wpf()
    {
        var plain = FrameworkElement.WidthProperty;
        Assert.False(plain.ReadOnly);
        Assert.False(plain.IsReadOnly);
        var key = DependencyProperty.RegisterReadOnly("Slice61Note", typeof(string),
            typeof(InheritsHost), new PropertyMetadata("x"));
        Assert.True(key.DependencyProperty.ReadOnly);
        Assert.True(key.DependencyProperty.IsReadOnly);
        Assert.Same(key.DependencyProperty.GetMetadata(typeof(InheritsHost)), key.DependencyProperty.Metadata);
        var host = new InheritsHost();
        Assert.Same(key.DependencyProperty.GetMetadata(host), key.DependencyProperty.Metadata);
        Assert.Same(plain.GetMetadata(typeof(Button)), plain.Metadata);
        Assert.Throws<ArgumentNullException>(() => plain.GetMetadata((Type)null!));
        Assert.Throws<ArgumentNullException>(() => plain.GetMetadata((DependencyObject)null!));
    }

    [Fact]
    public void FrameworkElement_SetBinding_dp_overload_binds()
    {
        var source = new Button { Width = 7 };
        var target = new Button();
        var expression = target.SetBinding(FrameworkElement.WidthProperty, new Binding("Width") { Source = source });
        Assert.NotNull(expression);
        Assert.NotNull(BindingOperations.GetBindingExpression(target, FrameworkElement.WidthProperty));
    }

    [Fact]
    public void ControlTemplate_type_ctor_targets()
    {
        var template = new ControlTemplate(typeof(Button)) { };
        Assert.Same(typeof(Button), template.TargetType);
    }

    [Fact]
    public void Coercion_flag_follows_changed_value_only()
    {
        var host = new CoercedHost();
        host.SetValue(CoercedHost.LevelProperty, 5.0);
        Assert.Equal(5.0, host.GetValue(CoercedHost.LevelProperty));
        Assert.False(DependencyPropertyHelper.GetValueSource(host, CoercedHost.LevelProperty).IsCoerced);
        host.SetValue(CoercedHost.LevelProperty, 999.0);
        Assert.Equal(10.0, host.GetValue(CoercedHost.LevelProperty));
        Assert.True(DependencyPropertyHelper.GetValueSource(host, CoercedHost.LevelProperty).IsCoerced);
    }
}
