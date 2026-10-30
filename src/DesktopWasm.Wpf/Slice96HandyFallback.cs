namespace DesktopWasm.Wpf;

// Slice-96 HandyControl view-content fallback: SimplePanel and
// TransitioningContentControl plus the ScrollViewer.IsInertiaEnabled attached
// knob the staged HandyControlDemo views need to hydrate. Member evidence via
// Mono.Cecil on the staged converted HandyControl binary (SimplePanel :
// DesktopWasm.Wpf.Panel with no extra members; TransitioningContentControl :
// DesktopWasm.Wpf.ContentControl with TransitionMode/TransitionStoryboard DPs;
// ScrollViewer : DesktopWasm.Wpf.ScrollViewer with instance + static
// IsInertiaEnabled accessors over a RegisterAttached bool defaulting to
// ValueBoxes.FalseBox) and the upstream HandyControl sources (SimplePanel is a
// pure fill panel: measure takes the max child desired size, arrange gives
// every child the full rect; TransitioningContentControl swaps content like a
// ContentControl and restarts its transition storyboard on mode/content
// change). Live default verified: IsInertiaEnabled registers attached with a
// false default and no inherit flag, so the engine copy is retained-only with
// no touch-inertia physics behind it.
// Boundary, never fabricated: the built-in TransitionMode storyboards resolve
// through HandyControl app resources (behind the line-17 SkinDefault stop), so
// mode changes alone animate nothing; only an explicitly set
// TransitionStoryboard runs, through the engine Storyboard pump.

/// <summary>
/// Transition selector (mirrors HandyControl.Data.TransitionMode values
/// Right2Left=0 through Fade=8 plus Custom=9; the engine owns the names so
/// converted XAML keeps parsing if the converted lib ever stages them).
/// </summary>
public enum TransitionMode
{
    Right2Left = 0,
    Left2Right = 1,
    Bottom2Top = 2,
    Top2Bottom = 3,
    Right2LeftWithFade = 4,
    Left2RightWithFade = 5,
    Bottom2TopWithFade = 6,
    Top2BottomWithFade = 7,
    Fade = 8,
    Custom = 9,
}

/// <summary>
/// Fill panel (mirrors HandyControl.Controls.SimplePanel, the Grid
/// replacement for content without row/column needs): measure reports the max
/// child desired size, arrange gives every child the full rect. Paints
/// nothing itself, like the upstream panel (no OnRender override there
/// either); Panel.Background stays available for converted setters.
/// </summary>
public class SimplePanel : Panel
{
    protected override WpfSize MeasureCore(WpfSize available)
    {
        var w = 0.0;
        var h = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            if (child is null) continue;
            child.Measure(available);
            w = Math.Max(w, child.DesiredSize.Width);
            h = Math.Max(h, child.DesiredSize.Height);
        }
        return new WpfSize(w, h);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        var fill = new WpfRect(finalRect.X, finalRect.Y, finalRect.Width, finalRect.Height);
        foreach (UIElement child in InternalChildren)
            child?.Arrange(fill);
    }
}

/// <summary>
/// Content host with a transition selector (mirrors
/// HandyControl.Controls.TransitioningContentControl): content swaps like a
/// plain ContentControl with the visual hosted full-bleed; an explicitly set
/// TransitionStoryboard restarts on content change, mode change and load
/// through the engine Storyboard pump. Built-in mode storyboards stay a
/// declared boundary (they resolve through HandyControl app resources).
/// </summary>
public class TransitioningContentControl : ContentControl
{
    /// <summary>Transition selector (default Right2Left, the upstream default).</summary>
    public static readonly DependencyProperty TransitionModeProperty =
        DependencyProperty.Register("TransitionMode", typeof(TransitionMode), typeof(TransitioningContentControl),
            new PropertyMetadata(TransitionMode.Right2Left));

    /// <summary>Explicit transition storyboard (null default: content swaps with no animation).</summary>
    public static readonly DependencyProperty TransitionStoryboardProperty =
        DependencyProperty.Register("TransitionStoryboard", typeof(Storyboard), typeof(TransitioningContentControl),
            new PropertyMetadata(null));

    /// <summary>Transition selector (stored; built-in modes animate nothing until their resources stage).</summary>
    public TransitionMode TransitionMode
    {
        get => (TransitionMode)GetValue(TransitionModeProperty)!;
        set
        {
            SetValue(TransitionModeProperty, value);
            StartTransition();
        }
    }

    /// <summary>Explicit transition storyboard (restarted on content/mode/load while set).</summary>
    public Storyboard? TransitionStoryboard
    {
        get => (Storyboard?)GetValue(TransitionStoryboardProperty);
        set
        {
            SetValue(TransitionStoryboardProperty, value);
            StartTransition();
        }
    }

    public TransitioningContentControl()
    {
        Loaded += OnFirstLoaded;
    }

    public override object Content
    {
        get => base.Content;
        set
        {
            if (Equals(base.Content, value)) return;
            var old = base.Content;
            base.Content = value;
            SyncContentVisual(old, value);
            StartTransition();
        }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (Content is UIElement visual)
        {
            visual.Measure(available);
            return visual.DesiredSize;
        }
        return new WpfSize(0, 0);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (Content is UIElement visual)
            visual.Arrange(new WpfRect(finalRect.X, finalRect.Y, finalRect.Width, finalRect.Height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Content is UIElement visual)
            RenderVisible(dc, visual);
    }

    private void OnFirstLoaded(object sender, RoutedEventArgs e) => StartTransition();

    private void StartTransition()
    {
        var board = TransitionStoryboard;
        if (board is null) return;
        if (Content is not UIElement) return;
        if (!IsVisible) return;
        board.Begin(this);
    }
}
