namespace DesktopWasm.Wpf;

/// <summary>
/// Honest host placeholder for OxyPlot plot views without a staged converted
/// image (oxySkia:PlotView: OxyPlot.SkiaSharp.Wpf is conversion-blocked) and
/// for engine-native XAML. Slice-49 instantiates the staged converted views
/// instead wherever they exist: oxyWpf:PlotView resolves the converted
/// OxyPlot.Wpf.PlotView and local:XamlPlotView the converted
/// ExampleBrowser.XamlPlotView (engine UIElement.OnRender is now protected
/// virtual like real WPF, plus the CommandBindingCollection.Add,
/// LayoutUpdated, 3-arg Register and DefaultStyleKey contracts the converted
/// constructors need). This shim preserves everything the converted
/// view-model IL drives for real: layout participation, DataContext
/// inheritance, and the Model/Controller/Visibility bindings against the
/// staged PlotModel and PlotController instances. It paints nothing: a
/// placeholder must never fabricate native plot pixels (the converted views
/// paint their own chrome; series ink awaits the converted default template
/// with PART_Grid, still unapplied).
/// </summary>
public class PlotView : Control
{
    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register("Model", typeof(object), typeof(PlotView),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ControllerProperty =
        DependencyProperty.Register("Controller", typeof(object), typeof(PlotView),
            new FrameworkPropertyMetadata(null));

    /// <summary>Bound plot model (real staged OxyPlot PlotModel; retained, never rendered).</summary>
    public object? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <summary>Bound plot controller (real staged OxyPlot PlotController; retained, never rendered).</summary>
    public object? Controller
    {
        get => GetValue(ControllerProperty);
        set => SetValue(ControllerProperty, value);
    }
}

/// <summary>
/// Mirrors the upstream ExampleBrowser.XamlPlotView : OxyPlot.Wpf.PlotView
/// shape for engine-native XAML: same Model / Controller / Visibility
/// contract, same honest-empty rendering. Converted entry XAML resolves the
/// real converted subclass through loader precedence instead.
/// </summary>
public sealed class XamlPlotView : PlotView
{
}
