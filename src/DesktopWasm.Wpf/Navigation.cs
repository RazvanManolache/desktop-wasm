namespace DesktopWasm.Wpf;

/// <summary>
/// Navigation request (matches System.Windows.Navigation.RequestNavigateEventArgs):
/// a Hyperlink activation carrying the link target. Converted gallery pages
/// (HyperlinkPage, ClipboardPage) read Uri and mark Handled after launching
/// or navigating. Raised through Hyperlink.RequestNavigateEvent so it bubbles
/// to in-session hosts; the engine performs no external launch itself.
/// </summary>
public sealed class RequestNavigateEventArgs : RoutedEventArgs
{
    public RequestNavigateEventArgs(Uri uri, string target)
    {
        ArgumentNullException.ThrowIfNull(uri);
        Uri = uri;
        Target = target;
        RoutedEvent = Hyperlink.RequestNavigateEvent;
    }

    /// <summary>Link destination (matches RequestNavigateEventArgs.Uri).</summary>
    public Uri Uri { get; }

    /// <summary>Frame target name (matches RequestNavigateEventArgs.Target).</summary>
    public string Target { get; }
}

/// <summary>Navigation-request handler shape (matches System.Windows.Navigation.RequestNavigateEventHandler).</summary>
public delegate void RequestNavigateEventHandler(object sender, RequestNavigateEventArgs e);

/// <summary>
/// Completed-navigation arguments (matches System.Windows.Navigation.NavigationEventArgs):
/// the navigated content plus the source Uri when navigation came from one.
/// Content navigation (Frame.Navigate(object), the Gallery pattern) carries
/// the element with a null Uri; Uri navigation stays a precise Frame error.
/// WebResponse is not carried: System.Net streaming has no session equivalent.
/// </summary>
public sealed class NavigationEventArgs : EventArgs
{
    public NavigationEventArgs(object? content, Uri? uri = null, object? extraData = null)
    {
        Content = content;
        Uri = uri;
        ExtraData = extraData;
    }

    /// <summary>Navigated content (matches NavigationEventArgs.Content).</summary>
    public object? Content { get; }

    /// <summary>Navigation source, null for content navigation (matches NavigationEventArgs.Uri).</summary>
    public Uri? Uri { get; }

    /// <summary>Caller-supplied navigation data (matches NavigationEventArgs.ExtraData).</summary>
    public object? ExtraData { get; }
}

/// <summary>Completed-navigation handler shape (matches System.Windows.Navigation.NavigatedEventHandler).</summary>
public delegate void NavigatedEventHandler(object sender, NavigationEventArgs e);

/// <summary>Frame chrome policy (real WPF shows navigation chrome unless Hidden; v1 never shows chrome).</summary>
public enum NavigationUIVisibility
{
    Automatic,
    Hidden,
}

/// <summary>
/// Page (WPF subset): a navigable content host. Converted apps bind
/// generated InitializeComponent/Connect wiring here (Geometry
/// SampleViewer); top-level page boot stays a precise boundary (the host
/// takes Windows), but pages construct, wire and render inside a
/// <see cref="Frame"/> or any panel.
/// </summary>
public class Page : FrameworkElement
{
    /// <summary>Title a navigation host would show (retained, not rendered).</summary>
    public string WindowTitle { get; set; } = string.Empty;

    /// <summary>Page title (matches Page.Title; retained like WindowTitle).</summary>
    public string Title { get; set; } = string.Empty;

    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register("Background", typeof(Brush), typeof(Page),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register("Foreground", typeof(Brush), typeof(Page),
            new FrameworkPropertyMetadata(null));

    private Brush? _background;
    private Brush? _foreground;

    /// <summary>Page fill (matches Page.Background; retained, repaints).</summary>
    public Brush? Background
    {
        get => _background;
        set { if (ReferenceEquals(_background, value)) return; _background = value; InvalidateVisual(); }
    }

    /// <summary>Page ink (matches Page.Foreground; retained, repaints).</summary>
    public Brush? Foreground
    {
        get => _foreground;
        set { if (ReferenceEquals(_foreground, value)) return; _foreground = value; InvalidateVisual(); }
    }

    public UIElement? Content
    {
        get => Children.Count > 0 ? Children[0] : null;
        set
        {
            if (ReferenceEquals(Content, value)) return;
            var old = Content;
            if (old is not null) RemoveChild(old);
            if (value is not null) AddChild(value);
        }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (Content is null) return new WpfSize(0, 0);
        var m = GetContentMargin();
        Content.Measure(new WpfSize(Math.Max(0, available.Width - m.Left - m.Right), Math.Max(0, available.Height - m.Top - m.Bottom)));
        var s = Content.DesiredSize;
        return new WpfSize(s.Width + m.Left + m.Right, s.Height + m.Top + m.Bottom);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (Content is null) return;
        var m = GetContentMargin();
        Content.Arrange(new WpfRect(
            finalRect.X + m.Left, finalRect.Y + m.Top,
            Math.Max(0, finalRect.Width - m.Left - m.Right),
            Math.Max(0, finalRect.Height - m.Top - m.Bottom)));
    }

    private Thickness GetContentMargin() =>
        Content is FrameworkElement fe ? fe.Margin : new Thickness(0);

    protected override void OnRender(DrawingContext dc) => RenderVisible(dc, Content);
}

/// <summary>
/// Frame (WPF subset): content host with page navigation. v1 keeps no
/// journal and shows no chrome: Navigate swaps the content, raises
/// <see cref="Navigated"/> (content args, null Uri), then raises
/// <see cref="ContentRendered"/>. Handlers are per-instance, so one
/// session Frame never observes another's navigation. Uri navigation
/// (pack/relative resolution plus journaling) is a precise error.
/// </summary>
/// <summary>Scrollbar visibility policy (matches System.Windows.Controls.ScrollBarVisibility).</summary>
public enum ScrollBarVisibility
{
    Disabled,
    Auto,
    Hidden,
    Visible,
}

/// <summary>Touch-panning axis policy (matches System.Windows.Controls.PanningMode; native order None/HorizontalOnly/VerticalOnly/Both/HorizontalFirst/VerticalFirst, verified on net10.0-windows).</summary>
public enum PanningMode
{
    None,
    HorizontalOnly,
    VerticalOnly,
    Both,
    HorizontalFirst,
    VerticalFirst,
}

public class Frame : FrameworkElement
{
    private Brush? _background;

    /// <summary>Fill brush (solid brushes paint exactly; others paint their resolved fallback).</summary>
    public Brush? Background
    {
        get => _background;
        set
        {
            if (ReferenceEquals(_background, value)) return;
            _background = value;
            InvalidateVisual();
        }
    }

    public UIElement? Content
    {
        get => Children.Count > 0 ? Children[0] : null;
        set
        {
            if (ReferenceEquals(Content, value)) return;
            var old = Content;
            if (old is not null) RemoveChild(old);
            if (value is not null) AddChild(value);
        }
    }

    /// <summary>Chrome policy (accepted; v1 never renders chrome).</summary>
    public NavigationUIVisibility NavigationUIVisibility { get; set; } = NavigationUIVisibility.Automatic;

    /// <summary>Raised when Navigate completes (matches Frame.Navigated).</summary>
    public event NavigatedEventHandler? Navigated;

    private NavigationService? _navigationService;

    /// <summary>
    /// Journal behind this frame (matches Frame.NavigationService;
    /// converted Behaviors code arrives here through
    /// NavigationService.GetNavigationService).
    /// </summary>
    public NavigationService NavigationService => _navigationService ??= new NavigationService(this);

    /// <summary>Swaps journal content without re-journaling (the bound service calls this).</summary>
    internal void NavigateToServiceContent(object? content)
    {
        if (content is UIElement element)
            Navigate(element);
    }

    /// <summary>Raised after Navigate swaps the content.</summary>
    public event RoutedEventHandler? ContentRendered;

    /// <summary>Hosts a page (or any element) and reports it rendered (matches Frame.Navigate returning bool).</summary>
    public bool Navigate(object? content)
    {
        if (content is not UIElement element)
            throw new InvalidOperationException(
                $"Frame.Navigate needs a UIElement, not '{content?.GetType().Name ?? "null"}' (Uri navigation is not supported).");
        Content = element;
        Navigated?.Invoke(this, new NavigationEventArgs(element));
        ContentRendered?.Invoke(this, new RoutedEventArgs { Source = this });
        return true;
    }

    /// <summary>Uri navigation needs pack resolution plus a journal: precise error.</summary>
    public void Navigate(Uri source) =>
        throw new InvalidOperationException(
            $"Frame.Navigate({source}) needs Uri pack resolution and a navigation journal, which v1 does not implement.");

    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (Content is null) return new WpfSize(0, 0);
        Content.Measure(available);
        return Content.DesiredSize;
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        Content?.Arrange(finalRect);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_background is not null)
        {
            var r = RenderRect;
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height,
                BrushRender.ResolveFill(_background, 0x00000000));
        }
        RenderVisible(dc, Content);
    }
}
