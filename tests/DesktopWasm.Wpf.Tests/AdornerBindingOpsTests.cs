using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ScreenToGif adorner/binding-ops slice repro (fails before the slice).
/// Original call sites:
/// - ScreenToGif/Controls/AdornedControl.cs: AdornerLayer.GetAdornerLayer,
///   layer.Add/_adorner, layer.Remove/_adorner.DisconnectChild, plus the
///   FrameworkElementAdorner measure/arrange overrides and DataContext push.
/// - ScreenToGif/Controls/FrameworkElementAdorner.cs: Adorner subclass with
///   MeasureOverride/ArrangeOverride/VisualChildrenCount/GetVisualChild/
///   LogicalChildren and a positioned child over AdornedElement bounds.
/// - ScreenToGif/Windows/Editor.xaml.cs: BindingOperations.SetBinding with
///   `Path = new PropertyPath("Items.Count")` / `new PropertyPath(nameof(..))`.
/// - ScreenToGif.Util/DynamicResourceBinding.cs: `new PropertyPath(dp)`.
/// - ScreenToGif/Controls/TimeBox.cs: GetBindingExpression(TextProperty)
///   followed by UpdateSource.
/// - ScreenToGif.Util/VisualHelper.cs: GetBindingExpression(DataContextProperty).
/// - ScreenToGif Themes/Generic.xaml + Windows/Editor.xaml: AdornerDecorator.
/// </summary>
[ResetAmbientState]
public sealed class AdornerBindingOpsTests
{
    private sealed class Named : FrameworkElement
    {
        public Named(string name) => Name = name;
        public List<string> Items { get; } = new() { "a", "b" };
        protected override WpfSize MeasureCore(WpfSize available) => new(10, 10);
        protected override void OnRender(DrawingContext dc) { }
    }

    private sealed class PaintBox : FrameworkElement
    {
        private readonly uint _rgba;
        public PaintBox(uint rgba, double w, double h)
        {
            _rgba = rgba;
            Width = w;
            Height = h;
        }

        protected override WpfSize MeasureCore(WpfSize available) => new(Width, Height);
        protected override void OnRender(DrawingContext dc)
        {
            var r = RenderRect;
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, _rgba);
        }
    }

    private sealed class BlueAdorner : Adorner
    {
        public BlueAdorner(UIElement adorned) : base(adorned) { }

        protected override Size MeasureOverride(Size constraint) =>
            new(Math.Min(60, constraint.Width), Math.Min(40, constraint.Height));

        protected override Size ArrangeOverride(Size finalSize) => finalSize;

        protected override void OnRender(DrawingContext dc)
        {
            var r = AdornedElement.RenderRect;
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, 0xFF0000FF);
        }
    }

    private sealed class Counter : FrameworkElement
    {
        public static readonly DependencyProperty CountProperty =
            DependencyProperty.Register("Count", typeof(int), typeof(Counter), new PropertyMetadata(0));

        public int Count
        {
            get => (int)(GetValue(CountProperty) ?? 0);
            set => SetValue(CountProperty, value);
        }

        protected override WpfSize MeasureCore(WpfSize available) => new(0, 0);
        protected override void OnRender(DrawingContext dc) { }
    }

    private sealed class DpBox : FrameworkElement
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(DpBox), new PropertyMetadata(string.Empty));

        public string Text
        {
            get => (string)(GetValue(TextProperty) ?? string.Empty);
            set => SetValue(TextProperty, value);
        }

        protected override WpfSize MeasureCore(WpfSize available) => new(10, 10);
        protected override void OnRender(DrawingContext dc) { }
    }

    [Fact]
    public void PropertyPath_from_string_round_trips()
    {
        var path = new PropertyPath("Items.Count");
        Assert.Equal("Items.Count", path.Path);
        Assert.Equal("Items.Count", path.ToString());
    }

    [Fact]
    public void PropertyPath_from_dependency_property_names_it()
    {
        var path = new PropertyPath(Named.WidthProperty);
        Assert.Equal("Width", path.Path);
    }

    [Fact]
    public void Binding_Path_accepts_PropertyPath_Editor_shape()
    {
        var binding = new Binding { Path = new PropertyPath("Items.Count") };
        Assert.Equal("Items.Count", binding.Path.Path);
    }

    [Fact]
    public void BindingOperations_SetBinding_pushes_source_to_clr_property()
    {
        var grid = new Counter();
        var source = new Person { Name = "Ada" };
        var binding = new Binding { Path = new PropertyPath("Name"), Source = source };
        var expression = BindingOperations.SetBinding(grid, Counter.CountProperty, binding);
        Assert.NotNull(expression);
        Assert.Same(binding, expression.BindingBase);
    }

    [Fact]
    public void BindingOperations_SetBinding_onto_dp_name_resolves_clr_value()
    {
        // Editor.xaml.cs shape: SetBinding(grid, ExportPanel.FrameCountProperty,
        // frameCountBinding) with Path = new PropertyPath("Items.Count").
        var target = new DpBox();
        var binding = new Binding
        {
            ElementName = "list",
            Path = new PropertyPath("Items.Count"),
            Mode = BindingMode.OneWay,
        };
        var root = new StackPanel();
        var list = new Named("list");
        root.AddChild(list);
        root.AddChild(target);
        root.Measure(new WpfSize(200, 100));
        root.Arrange(new WpfRect(0, 0, 200, 100));

        BindingOperations.SetBinding(target, DpBox.TextProperty, binding);

        Assert.Equal("2", target.Text);
    }

    [Fact]
    public void GetBindingExpression_UpdateSource_pushes_back_TimeBox_shape()
    {
        // TimeBox shape: GetBindingExpression(TextProperty)?.UpdateSource().
        var box = new DpBox { Text = "7" };
        var vm = new Person { Name = "Ada" };
        box.SetBinding("Text", new Binding("Name") { Source = vm, Mode = BindingMode.TwoWay });

        box.Text = "Grace";
        box.GetBindingExpression(DpBox.TextProperty)?.UpdateSource();

        Assert.Equal("Grace", vm.Name);
    }

    [Fact]
    public void DataContext_bound_reports_expression_VisualHelper_shape()
    {
        // VisualHelper.IsDataContextDataBound shape.
        var element = new TextBox();
        Assert.Null(element.GetBindingExpression(FrameworkElement.DataContextProperty));

        element.SetBinding("DataContext", new Binding("Name") { Source = new Person { Name = "Ada" } });

        Assert.NotNull(element.GetBindingExpression(FrameworkElement.DataContextProperty));
    }

    [Fact]
    public void Decorator_hosts_child_measure_arrange_paint()
    {
        var decorator = new Decorator();
        var child = new PaintBox(0xFFFF0000, 50, 30);
        decorator.Child = child;

        var commands = WasmMilCore.RenderHeadless(decorator, new WpfSize(200, 100));

        Assert.Equal(new Size(50, 30), decorator.DesiredSize);
        Assert.Same(child, decorator.LogicalChildren[0]);
        var fill = Assert.IsType<DrawCommand.FillRect>(Assert.Single(commands));
        Assert.Equal(0xFFFF0000u, fill.Rgba);
        Assert.Equal(50, fill.W, precision: 3);
        Assert.Equal(30, fill.H, precision: 3);
    }

    [Fact]
    public void AdornerLayer_Add_paints_above_adorned_element()
    {
        // Crop-overlay shape: content inside an AdornerDecorator, whose
        // owned layer GetAdornerLayer resolves from the content.
        var host = new StackPanel();
        var decorator = new AdornerDecorator();
        var adorned = new PaintBox(0xFFFF0000, 100, 60);
        decorator.Child = adorned;
        host.AddChild(decorator);
        host.Measure(new WpfSize(200, 200));
        host.Arrange(new WpfRect(0, 0, 200, 200));

        var layer = AdornerLayer.GetAdornerLayer(adorned);
        Assert.Same(decorator.AdornerLayer, layer);

        var adorner = new BlueAdorner(adorned);
        layer.Add(adorner);
        host.Measure(new WpfSize(200, 200));
        host.Arrange(new WpfRect(0, 0, 200, 200));

        var commands = WasmMilCore.RenderHeadless(host, new WpfSize(200, 200));
        var fills = commands.OfType<DrawCommand.FillRect>().ToList();
        var red = fills.FindIndex(f => f.Rgba == 0xFFFF0000u);
        var blue = fills.FindIndex(f => f.Rgba == 0xFF0000FFu);
        Assert.True(red >= 0, "adorned element paints");
        Assert.True(blue >= 0, "adorner paints");
        Assert.True(blue > red, "adorner paints above the adorned element");
        Assert.Equal(fills[red].X, fills[blue].X, precision: 3);
        Assert.Equal(fills[red].Y, fills[blue].Y, precision: 3);
        Assert.Equal(fills[red].W, fills[blue].W, precision: 3);
        Assert.Equal(fills[red].H, fills[blue].H, precision: 3);
    }

    [Fact]
    public void AdornerLayer_Remove_stops_painting()
    {
        var host = new StackPanel();
        var adorned = new PaintBox(0xFFFF0000, 100, 60);
        host.AddChild(adorned);
        var layer = new AdornerLayer();
        host.AddChild(layer);
        var adorner = new BlueAdorner(adorned);
        layer.Add(adorner);
        Assert.Single(layer.Adorners);

        layer.Remove(adorner);

        Assert.Empty(layer.Adorners);
        var commands = WasmMilCore.RenderHeadless(host, new WpfSize(200, 200));
        Assert.DoesNotContain(commands.OfType<DrawCommand.FillRect>(), f => f.Rgba == 0xFF0000FFu);
    }

    [Fact]
    public void AdornerDecorator_hosts_child_and_layer()
    {
        var decorator = new AdornerDecorator();
        var child = new PaintBox(0xFFFF0000, 50, 30);
        decorator.Child = child;

        Assert.Same(AdornerLayer.GetAdornerLayer(child), decorator.AdornerLayer);

        var commands = WasmMilCore.RenderHeadless(decorator, new WpfSize(200, 100));
        Assert.Contains(commands.OfType<DrawCommand.FillRect>(), f => f.Rgba == 0xFFFF0000u);
    }

    [Fact]
    public void Xaml_AdornerDecorator_hosts_child()
    {
        var root = XamlLoader.Load("""
            <AdornerDecorator>
              <Border Thickness="2"/>
            </AdornerDecorator>
            """).Root;

        var decorator = Assert.IsType<AdornerDecorator>(root);
        var border = Assert.IsType<Border>(Assert.Single(decorator.LogicalChildren, c => c is Border));
        Assert.Same(border, decorator.Child);
        Assert.Same(decorator.AdornerLayer, AdornerLayer.GetAdornerLayer(border));
    }

    private sealed class Person
    {
        public string Name { get; set; } = string.Empty;
    }
}
