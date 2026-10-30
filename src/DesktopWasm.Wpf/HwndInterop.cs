namespace DesktopWasm.Wpf;

/// <summary>
/// Window-message hook (matches System.Windows.Interop.HwndSourceHook):
/// retained so custom-chrome windows register their procedure. The browser
/// session has no Win32 pump, so hooks never fire from the host; they stay
/// listed until removed or disposed.
/// </summary>
public delegate IntPtr HwndSourceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled);

/// <summary>
/// Source construction (matches System.Windows.Interop.HwndSourceParameters):
/// plain construction data. The session ignores Win32 placement/style bits
/// and keeps the name/size the capture helpers read.
/// </summary>
public struct HwndSourceParameters
{
    public HwndSourceParameters(string name)
    {
        WindowName = name;
    }

    public HwndSourceParameters(string name, int width, int height)
    {
        WindowName = name;
        Width = width;
        Height = height;
    }

    public int WindowClassStyle { get; set; }
    public int WindowStyle { get; set; }
    public int ExtendedWindowStyle { get; set; }
    public int PositionX { get; set; }
    public int PositionY { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool HasAssignedSize => Width != 0 || Height != 0;
    public string WindowName { get; set; } = string.Empty;
    public IntPtr ParentWindow { get; set; }
    public HwndSourceHook? HwndSourceHook { get; set; }
    public bool AdjustSizingForNonClientArea { get; set; }
    public bool TreatAncestorsAsNonClientArea { get; set; }
    public bool UsesPerPixelOpacity { get; set; }
    public bool UsesPerPixelTransparency { get; set; }
    public bool TreatAsInputRoot { get; set; }

    public void SetPosition(int x, int y)
    {
        PositionX = x;
        PositionY = y;
    }

    public void SetSize(int width, int height)
    {
        Width = width;
        Height = height;
    }
}

/// <summary>
/// Composition result (matches System.Windows.Media.CompositionTarget):
/// the device transform over the session scale. Browser CSS pixels are
/// device-independent (96 dpi), so the default scale is 1.0; the host may
/// raise it to the real devicePixelRatio through
/// <see cref="HwndTarget.DeviceScale"/>.
/// </summary>
public class CompositionTarget : DispatcherObject
{
    public virtual Visual? RootVisual { get; set; }

    public virtual Matrix TransformToDevice => Matrix.Identity;

    public virtual Matrix TransformFromDevice => Matrix.Identity;
}

/// <summary>
/// Session composition target (matches System.Windows.Interop.HwndTarget):
/// carries <see cref="DeviceScale"/> into the device transform the DPI
/// helpers read (Other.Dpi/Scale shape: 96 * M11).
/// </summary>
public sealed class HwndTarget : CompositionTarget, IDisposable
{
    private static double _deviceScale = 1.0;

    /// <summary>
    /// Device pixels per CSS pixel for the session (1.0 default = 96 dpi;
    /// the host sets the real devicePixelRatio when known).
    /// </summary>
    public static double DeviceScale
    {
        get => _deviceScale;
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "DeviceScale must be a positive number.");
            _deviceScale = value;
        }
    }

    public override Matrix TransformToDevice => new(DeviceScale, 0, 0, DeviceScale, 0, 0);

    public override Matrix TransformFromDevice => new(1 / DeviceScale, 0, 0, 1 / DeviceScale, 0, 0);

    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;

    /// <summary>
    /// Glass-frame tint (matches System.Windows.Interop.HwndTarget.BackgroundColor,
    /// live-verified as a read/write System.Windows.Media.Color): retained only,
    /// the session paints no glass frame. Converted WindowChromeWorker glass
    /// helpers land here instead of failing to JIT.
    /// </summary>
    public Color BackgroundColor { get; set; } = Colors.Transparent;
}

/// <summary>
/// Session window source (matches System.Windows.Interop.HwndSource): a
/// <see cref="PresentationSource"/> whose composition target reports the
/// session scale, so DPI fallbacks (96 * M11) compute real session values.
/// There is no HWND behind it: <see cref="Handle"/> is a precise boundary,
/// and hooks are retained but never invoked by the host (no Win32 pump).
/// </summary>
public sealed class HwndSource : PresentationSource, IDisposable
{
    private readonly List<HwndSourceHook> _hooks = new();
    private readonly HwndTarget _target = new();
    private Visual? _root;

    public HwndSource(HwndSourceParameters parameters)
    {
        Parameters = parameters;
        if (parameters.HwndSourceHook is not null) _hooks.Add(parameters.HwndSourceHook);
    }

    /// <summary>
    /// Menu-mode focus acquisition (matches
    /// System.Windows.Interop.HwndSource.DefaultAcquireHwndFocusInMenuMode,
    /// live-verified as a static read/write bool defaulting to true):
    /// retained only, the session has no Win32 menu loop to acquire from.
    /// Converted AvalonDock sets this in the DockingManager static
    /// constructor, so the member must exist for the closure to JIT.
    /// </summary>
    public static bool DefaultAcquireHwndFocusInMenuMode { get; set; } = true;

    /// <summary>
    /// Autosize rule (matches System.Windows.Interop.HwndSource.SizeToContent,
    /// live-verified as a plain CLR property with no backing field):
    /// retained only, the session has no HWND to auto-size. Converted
    /// FloatingWindowContentHost mirrors the Window rule here.
    /// </summary>
    public SizeToContent SizeToContent { get; set; }

    /// <summary>
    /// Source for a window handle (matches
    /// System.Windows.Interop.HwndSource.FromHwnd): the session composes with
    /// no HWND, so this honestly reports null like WPF does for an unknown
    /// handle instead of fabricating a source.
    /// </summary>
    public static HwndSource? FromHwnd(IntPtr hwnd) => null;

    /// <summary>Construction values (name/size retained; Win32 bits ignored).</summary>
    public HwndSourceParameters Parameters { get; }

    public override Visual? RootVisual
    {
        get => _root;
        set => _root = value;
    }

    /// <summary>Session composition target (never null while undisposed).</summary>
    public override HwndTarget CompositionTarget => _target;

    /// <summary>
    /// No HWND exists in the browser session: reading the handle throws
    /// instead of returning a fabricated zero.
    /// </summary>
    public IntPtr Handle => throw new NotSupportedException(
        "HwndSource.Handle has no browser equivalent: the session composes with no HWND.");

    public bool IsDisposed { get; private set; }

    /// <summary>Retains a window-message hook (never invoked by the session host).</summary>
    public void AddHook(HwndSourceHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        lock (_hooks) _hooks.Add(hook);
    }

    /// <summary>Detaches a window-message hook (no-op when absent, like WPF).</summary>
    public void RemoveHook(HwndSourceHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        lock (_hooks) _hooks.Remove(hook);
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        lock (_hooks) _hooks.Clear();
        _target.Dispose();
    }
}

/// <summary>
/// Window handle helper (matches System.Windows.Interop.WindowInteropHelper):
/// construction is retained, but the handle has no browser equivalent.
/// </summary>
public sealed class WindowInteropHelper
{
    public WindowInteropHelper(Window window)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
    }

    /// <summary>Window this helper was built for.</summary>
    public Window Window { get; }

    /// <summary>Owner window handle (retained plain value; no Win32 ownership in the session).</summary>
    public IntPtr Owner { get; set; }

    /// <summary>No HWND exists in the browser session (precise boundary).</summary>
    public IntPtr Handle => throw new NotSupportedException(
        "WindowInteropHelper.Handle has no browser equivalent: the session composes with no HWND.");
}

/// <summary>
/// GDI imaging bridge (matches System.Windows.Interop.Imaging): every
/// entry needs a device handle or device context, so all three are precise
/// boundaries instead of fabricated bitmaps.
/// </summary>
public static class Imaging
{
    public static BitmapSource CreateBitmapSourceFromHBitmap(
        IntPtr bitmap, IntPtr palette, Int32Rect sourceRect, BitmapSizeOptions sizeOptions) =>
        throw new NotSupportedException(
            "Imaging.CreateBitmapSourceFromHBitmap needs a GDI bitmap handle, which the browser session has no equivalent for.");

    public static BitmapSource CreateBitmapSourceFromHIcon(
        IntPtr icon, Int32Rect sourceRect, BitmapSizeOptions sizeOptions) =>
        throw new NotSupportedException(
            "Imaging.CreateBitmapSourceFromHIcon needs a GDI icon handle, which the browser session has no equivalent for.");

    public static BitmapSource CreateBitmapSourceFromMemorySection(
        IntPtr section, int pixelWidth, int pixelHeight, PixelFormat format, int stride, int offset) =>
        throw new NotSupportedException(
            "Imaging.CreateBitmapSourceFromMemorySection needs a Win32 memory section, which the browser session has no equivalent for.");
}

/// <summary>Window message (matches System.Windows.Interop.MSG; property shape verified).</summary>
public struct MSG
{
    public IntPtr hwnd { get; set; }
    public int message { get; set; }
    public IntPtr wParam { get; set; }
    public IntPtr lParam { get; set; }
    public int time { get; set; }
    public int pt_x { get; set; }
    public int pt_y { get; set; }
}
