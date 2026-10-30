namespace DesktopWasm.Wpf;

/// <summary>
/// Edges that keep a native frame instead of glass (matches
/// System.Windows.Shell.NonClientFrameEdges: None 0, Left 1, Top 2,
/// Right 4, Bottom 8).
/// </summary>
[Flags]
public enum NonClientFrameEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}

/// <summary>
/// Custom window chrome (matches System.Windows.Shell.WindowChrome, the
/// Gallery MainWindow shape). Attached per Window: the top
/// <see cref="CaptionHeight"/> pixels become the drag/caption band where
/// only <see cref="IsHitTestVisibleInChrome"/> elements stay interactive,
/// and the renderer paints the glass base, caption band and native-frame
/// edge strips through the existing retained commands.
/// The browser host owns the outer OS frame, so there is no caption drag,
/// DWM blur, system-menu or per-monitor resize handling: the caption band
/// absorbs input (route ends at the Window) and the app draws its own
/// caption buttons exactly like the original Gallery does.
/// </summary>
public sealed class WindowChrome : Freezable
{
    /// <summary>Attached-property bag key for IsHitTestVisibleInChrome.</summary>
    internal const string HitTestVisibleKey = "WindowChrome.IsHitTestVisibleInChrome";

    /// <summary>Glass base painted when GlassFrameThickness is a sheet of glass.</summary>
    internal const uint GlassRgba = 0xEDF1F5FFu;

    /// <summary>Caption band tint (documented flat approximation of themed glass).</summary>
    internal const uint CaptionRgba = 0xDCE3EAFFu;

    /// <summary>Native-frame edge strip tint (documented flat approximation).</summary>
    internal const uint EdgeRgba = 0xB7C1CCFFu;

    private readonly HashSet<Window> _attached = new();

    private double _captionHeight;
    private CornerRadius _cornerRadius = new(0);
    private Thickness _glassFrameThickness = new(0);
    private Thickness _resizeBorderThickness = new(0);
    private NonClientFrameEdges _nonClientFrameEdges = NonClientFrameEdges.None;
    private bool _useAeroCaptionButtons = true;

    /// <summary>Caption band height in device-independent pixels (default 0: no caption).</summary>
    public double CaptionHeight
    {
        get => _captionHeight;
        set { ThrowIfFrozen(); _captionHeight = value; InvalidateAttached(); }
    }

    /// <summary>Window corner rounding (retained and queryable; clipping stays a boundary).</summary>
    public CornerRadius CornerRadius
    {
        get => _cornerRadius;
        set { ThrowIfFrozen(); _cornerRadius = value; InvalidateAttached(); }
    }

    /// <summary>
    /// Glass frame insets; all-negative (the Gallery's -1 uniform) is a sheet
    /// of glass covering the window (matches WPF's -1 convention).
    /// </summary>
    public Thickness GlassFrameThickness
    {
        get => _glassFrameThickness;
        set { ThrowIfFrozen(); _glassFrameThickness = value; InvalidateAttached(); }
    }

    /// <summary>Resize border insets painted on the <see cref="NonClientFrameEdges"/> edges.</summary>
    public Thickness ResizeBorderThickness
    {
        get => _resizeBorderThickness;
        set { ThrowIfFrozen(); _resizeBorderThickness = value; InvalidateAttached(); }
    }

    /// <summary>Edges that keep a native frame strip instead of glass (default none).</summary>
    public NonClientFrameEdges NonClientFrameEdges
    {
        get => _nonClientFrameEdges;
        set { ThrowIfFrozen(); _nonClientFrameEdges = value; InvalidateAttached(); }
    }

    /// <summary>Whether aero caption buttons show (default true, like WPF; the app draws its own).</summary>
    public bool UseAeroCaptionButtons
    {
        get => _useAeroCaptionButtons;
        set { ThrowIfFrozen(); _useAeroCaptionButtons = value; InvalidateAttached(); }
    }

    /// <summary>Whether every glass inset is negative (sheet-of-glass window).</summary>
    public bool GlassCoversAll =>
        GlassFrameThickness.Left < 0 && GlassFrameThickness.Top < 0 &&
        GlassFrameThickness.Right < 0 && GlassFrameThickness.Bottom < 0;

    /// <summary>Returns the chrome attached to the window (null when none).</summary>
    public static WindowChrome? GetWindowChrome(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.Chrome;
    }

    /// <summary>
    /// Attaches chrome to the window (null clears it). Repaints the window;
    /// reassignment detaches from the previous instance.
    /// </summary>
    public static void SetWindowChrome(Window window, WindowChrome? chrome)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (ReferenceEquals(window.Chrome, chrome)) return;
        if (window.Chrome is { } old) old._attached.Remove(window);
        window.Chrome = chrome;
        chrome?._attached.Add(window);
        window.InvalidateVisual();
    }

    /// <summary>Whether the element stays interactive inside the caption band (default false).</summary>
    public static bool GetIsHitTestVisibleInChrome(IInputElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element is UIElement ui && ui.GetAttached(HitTestVisibleKey) is true;
    }

    /// <summary>Marks the element interactive inside the caption band.</summary>
    public static void SetIsHitTestVisibleInChrome(IInputElement element, bool hitTestVisible)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is not UIElement ui)
            throw new ArgumentException("IsHitTestVisibleInChrome needs a UIElement.", nameof(element));
        ui.SetAttached(HitTestVisibleKey, hitTestVisible);
    }

    /// <summary>Whether the point falls in the window's caption band.</summary>
    internal static bool IsCaptionPoint(Window window, WindowChrome chrome, WpfPoint point)
    {
        if (chrome.CaptionHeight <= 0) return false;
        var r = window.RenderRect;
        return point.X >= r.X && point.X < r.X + r.Width &&
            point.Y >= r.Y && point.Y < r.Y + chrome.CaptionHeight;
    }

    /// <summary>
    /// Whether the visible subtree holds a chrome-marked element (used to
    /// decide if a caption-band point may descend past an unmarked container).
    /// </summary>
    internal static bool SubtreeHasChromeHit(UIElement el)
    {
        if (el.Visibility != Visibility.Visible) return false;
        if (GetIsHitTestVisibleInChrome(el)) return true;
        if (el is FrameworkElement fe)
            foreach (var child in fe.LogicalChildren)
                if (SubtreeHasChromeHit(child)) return true;
        return false;
    }

    /// <summary>Paints the caption band and native-frame edge strips (content paints after).</summary>
    internal void PaintChrome(DrawingContext dc, WpfRect r)
    {
        var cap = (float)Math.Min(Math.Max(0, CaptionHeight), r.Height);
        if (cap > 0)
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, cap, CaptionRgba);
        var left = HasEdge(NonClientFrameEdges.Left) ? EdgeWidth(ResizeBorderThickness.Left) : 0;
        var right = HasEdge(NonClientFrameEdges.Right) ? EdgeWidth(ResizeBorderThickness.Right) : 0;
        var top = HasEdge(NonClientFrameEdges.Top) ? EdgeWidth(ResizeBorderThickness.Top) : 0;
        var bottom = HasEdge(NonClientFrameEdges.Bottom) ? EdgeWidth(ResizeBorderThickness.Bottom) : 0;
        var bodyTop = (float)r.Y + cap;
        var bodyHeight = (float)Math.Max(0, r.Height - cap - bottom);
        if (left > 0) dc.DrawRectangle((float)r.X, bodyTop, left, bodyHeight, EdgeRgba);
        if (right > 0) dc.DrawRectangle((float)(r.X + r.Width - right), bodyTop, right, bodyHeight, EdgeRgba);
        if (bottom > 0) dc.DrawRectangle((float)r.X, (float)(r.Y + r.Height - bottom), (float)r.Width, bottom, EdgeRgba);
        if (top > 0) dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, top, EdgeRgba);
    }

    private bool HasEdge(NonClientFrameEdges edge) => (NonClientFrameEdges & edge) == edge;

    private static float EdgeWidth(double value) => (float)Math.Max(0, value);

    private void InvalidateAttached()
    {
        foreach (var window in _attached)
            window.InvalidateVisual();
    }
}
