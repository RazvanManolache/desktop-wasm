namespace DesktopWasm.Wpf;

/// <summary>
/// Immediate-mode paintable: the contract winforms-wasm controls implement so
/// a WPF visual tree can host them. No WinForms assembly reference — both
/// sides share the command list, so hosting is zero-copy.
/// </summary>
public interface IImmediatePaintable
{
    WpfSize Measure(WpfSize available);
    void Paint(DrawingContext dc, WpfRect bounds);
}

/// <summary>
/// WPF-side host for immediate-mode (WinForms) content (cf. System.Windows.Forms.Integration.WindowsFormsHost).
/// Delegates measuring to the child, clips to its arranged bounds on render,
/// and participates in hit-testing and routed input like any element.
/// </summary>
public sealed class WindowsFormsHost : FrameworkElement
{
    public IImmediatePaintable? Child { get; set; }

    protected override WpfSize MeasureCore(WpfSize available) =>
        Child?.Measure(available) ?? new WpfSize(0, 0);

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        dc.PushClip((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);
        Child?.Paint(dc, r);
        dc.PopClip();
    }
}

/// <summary>
/// WinForms-side host for retained WPF content (cf. System.Windows.Forms.Integration.ElementHost).
/// Holds a WPF visual tree at a fixed size and renders it to draw commands the
/// WinForms immediate pipeline replays into the same shared surface.
/// </summary>
public sealed class ElementHost
{
    public UIElement? Content { get; set; }

    public WpfSize Size { get; set; } = new WpfSize(800, 600);

    /// <summary>Renders the hosted content to commands (throws when empty).</summary>
    public IReadOnlyList<Graphics.DrawCommand> Render() =>
        WasmMilCore.RenderHeadless(
            Content ?? throw new InvalidOperationException("ElementHost.Content is not set."),
            Size);

    /// <summary>Transport JSON for the shared canvas host path.</summary>
    public string ExportFrameJson() => WasmMilCore.ExportFrameJson(Render());
}
