using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf;

/// <summary>Text content label with the ContentControl identity used by converted WPF code.</summary>
public class Label : ContentControl
{
    static Label()
    {
        // Display labels never take keyboard focus (live-verified WPF default).
        FocusableProperty.OverrideMetadata(typeof(Label), new FrameworkPropertyMetadata(false));
    }

    private readonly TextBlock _text = new();
    private Thickness _padding = new(5);
    private string _font = "14px sans-serif";
    private uint _foreground = 0x000000FF;
    public new Thickness Padding { get => _padding; set { _padding = value; InvalidateMeasure(); } }
    public string FontCss { get => _font; set { _font = value; InvalidateMeasure(); } }
    public uint ForegroundRgba { get => _foreground; set { _foreground = value; InvalidateVisual(); } }

    protected override void OnForegroundChanged(Brush? brush)
    {
        if (brush is SolidColorBrush solid) ForegroundRgba = solid.ToRgba();
    }

    protected override void OnFontChanged() => FontCss = ComposeFontCss();
    private void Sync() { _text.Text = FormatContentText(Content, ContentStringFormat); _text.Padding = Padding; _text.FontCss = FontCss; _text.ForegroundRgba = ForegroundRgba; }

    /// <summary>Visual content hosts live (gallery PageHeader template parts
    /// nest TextBlocks); text keeps the folded path.</summary>
    public override object Content
    {
        get => base.Content;
        set
        {
            if (Equals(base.Content, value)) return;
            var old = base.Content;
            base.Content = value;
            SyncContentVisual(old, value);
        }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (Content is UIElement visual)
        {
            visual.Measure(available);
            return visual.DesiredSize;
        }
        Sync(); _text.Measure(available); return _text.DesiredSize;
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (Content is UIElement visual)
        {
            visual.Arrange(finalRect);
            return;
        }
        _text.Arrange(finalRect);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Content is UIElement visual)
        {
            RenderVisible(dc, visual);
            return;
        }
        Sync(); _text.Render(dc);
    }
}
