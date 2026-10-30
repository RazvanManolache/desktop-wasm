namespace DesktopWasm.Wpf;

/// <summary>
/// WPF routed-event plumbing (PresentationCore subset): plain CLR event data
/// for the managed input path. Real WPF carries RoutedEvent/Source/OriginalSource
/// routing metadata; the engine routes through <see cref="WpfInputManager"/>
/// instead and keeps only what converted handlers read: Handled + sender.
/// </summary>
public class RoutedEventArgs : EventArgs
{
    public RoutedEventArgs() { }

    /// <summary>
    /// Routed-token constructor (matches the live (RoutedEvent) overload;
    /// converted HandyControl NotifyIcon.Callback news these up to report
    /// tray callbacks through the converted Click/MouseDoubleClick events).
    /// </summary>
    public RoutedEventArgs(RoutedEvent? routedEvent) => RoutedEvent = routedEvent;

    /// <summary>Routed-token-plus-source constructor (matches the live (RoutedEvent, object) overload).</summary>
    public RoutedEventArgs(RoutedEvent? routedEvent, object? source)
    {
        RoutedEvent = routedEvent;
        Source = source;
    }

    /// <summary>Handler marks the event handled (stops further routing).</summary>
    public bool Handled { get; set; }

    /// <summary>Object the event is presented as coming from.</summary>
    public object? Source { get; set; }

    /// <summary>Routed token (set by raisers; required by UIElement.RaiseEvent).</summary>
    public RoutedEvent? RoutedEvent { get; set; }

    /// <summary>Originator before any Source retargeting (defaults to Source at raise).</summary>
    public object? OriginalSource { get; set; }
}

/// <summary>WPF routed-event handler shape (converted Click/Loaded wiring binds here).</summary>
public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);

/// <summary>
/// Text composition data (PresentationCore System.Windows.Input subset, sealed
/// over InputEventArgs like the live type): the text a key press composes.
/// Converted Window KeyDown/TextInput handlers read <see cref="Text"/>;
/// converted AvalonEdit text input news these up through the live
/// (InputDevice, TextComposition) shape and reads the stored composition
/// back; the engine raises these from keyboard input only where a composition
/// source exists (TextBox editing today).
/// </summary>
public sealed class TextCompositionEventArgs : InputEventArgs
{
    private readonly TextComposition _composition;

    public TextCompositionEventArgs(string text)
        : base(Environment.TickCount) => _composition = new TextComposition(text);

    /// <summary>
    /// Live composition shape (matches the public (InputDevice,
    /// TextComposition) constructor): the device rides InputEventArgs.Device
    /// (null on engine-raised args, like the other device-less engine args)
    /// while a null composition throws ArgumentNullException like live.
    /// </summary>
    public TextCompositionEventArgs(InputDevice? device, TextComposition composition)
        : base(device, Environment.TickCount) =>
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));

    /// <summary>Composed text ("" when the key produces none).</summary>
    public string Text => _composition.Text;

    public TextComposition TextComposition => _composition;
}

/// <summary>
/// Layout size-change data (PresentationFramework System.Windows subset):
/// gallery TileGallery/UserDashboard responsive layouts read
/// <see cref="PreviousSize"/> and <see cref="NewSize"/> to reflow tiles and
/// avatars when an arrange pass actually changes the size. Converted
/// handlers read PreviousSize/NewSize/WidthChanged/HeightChanged.
/// </summary>
public sealed class SizeChangedEventArgs : RoutedEventArgs
{
    public SizeChangedEventArgs(Size previousSize, Size newSize)
    {
        PreviousSize = previousSize;
        NewSize = newSize;
    }

    /// <summary>Size from the previous arrange pass.</summary>
    public Size PreviousSize { get; }

    /// <summary>Size from the arrange pass that raised this event.</summary>
    public Size NewSize { get; }

    /// <summary>True when the width differs between passes.</summary>
    public bool WidthChanged => PreviousSize.Width != NewSize.Width;

    /// <summary>True when the height differs between passes.</summary>
    public bool HeightChanged => PreviousSize.Height != NewSize.Height;
}

/// <summary>Layout size-change handler shape (matches System.Windows.SizeChangedEventHandler).</summary>
public delegate void SizeChangedEventHandler(object sender, SizeChangedEventArgs e);

/// <summary>
/// Scroll-state change data (PresentationFramework System.Windows.Controls
/// subset): ScreenToGif's ZoomBox recenters from ExtentWidth/ExtentHeight on
/// extent changes, ImageViewer re-anchors the zoom origin the same way, and
/// Util.ScrollSynchronizer gates on HorizontalChange/VerticalChange. Real
/// WPF constructs these (internal ctor); converted code only reads them.
/// </summary>
public sealed class ScrollChangedEventArgs : RoutedEventArgs
{
    private readonly Vector _offset;
    private readonly Vector _offsetChange;
    private readonly Size _extent;
    private readonly Vector _extentChange;
    private readonly Size _viewport;
    private readonly Vector _viewportChange;

    internal ScrollChangedEventArgs(Vector offset, Vector offsetChange, Size extent, Vector extentChange, Size viewport, Vector viewportChange)
    {
        _offset = offset;
        _offsetChange = offsetChange;
        _extent = extent;
        _extentChange = extentChange;
        _viewport = viewport;
        _viewportChange = viewportChange;
    }

    /// <summary>Updated horizontal offset (matches ScrollViewer.HorizontalOffset).</summary>
    public double HorizontalOffset => _offset.X;

    /// <summary>Updated vertical offset (matches ScrollViewer.VerticalOffset).</summary>
    public double VerticalOffset => _offset.Y;

    /// <summary>Horizontal offset delta since the previous state.</summary>
    public double HorizontalChange => _offsetChange.X;

    /// <summary>Vertical offset delta since the previous state.</summary>
    public double VerticalChange => _offsetChange.Y;

    /// <summary>Updated content extent width (matches ScrollViewer.ExtentWidth).</summary>
    public double ExtentWidth => _extent.Width;

    /// <summary>Updated content extent height (matches ScrollViewer.ExtentHeight).</summary>
    public double ExtentHeight => _extent.Height;

    /// <summary>Extent width delta since the previous state.</summary>
    public double ExtentWidthChange => _extentChange.X;

    /// <summary>Extent height delta since the previous state.</summary>
    public double ExtentHeightChange => _extentChange.Y;

    /// <summary>Updated viewport width (matches ScrollViewer.ViewportWidth).</summary>
    public double ViewportWidth => _viewport.Width;

    /// <summary>Updated viewport height (matches ScrollViewer.ViewportHeight).</summary>
    public double ViewportHeight => _viewport.Height;

    /// <summary>Viewport width delta since the previous state.</summary>
    public double ViewportWidthChange => _viewportChange.X;

    /// <summary>Viewport height delta since the previous state.</summary>
    public double ViewportHeightChange => _viewportChange.Y;
}

/// <summary>Scroll-state change handler shape (matches System.Windows.Controls.ScrollChangedEventHandler).</summary>
public delegate void ScrollChangedEventHandler(object sender, ScrollChangedEventArgs e);

/// <summary>
/// Keyboard focus transfer data (PresentationCore System.Windows.Input
/// subset): the focus preview hook consults Handled to veto the move
/// (MyTextBox-style read-only displays swallow focus this way).
/// </summary>
public sealed class KeyboardFocusChangedEventArgs : RoutedEventArgs
{
    public KeyboardFocusChangedEventArgs(UIElement? oldFocus, UIElement? newFocus)
    {
        OldFocus = oldFocus;
        NewFocus = newFocus;
    }

    /// <summary>Element losing focus (null when none had it).</summary>
    public UIElement? OldFocus { get; }

    /// <summary>Element about to gain focus.</summary>
    public UIElement? NewFocus { get; }
}
