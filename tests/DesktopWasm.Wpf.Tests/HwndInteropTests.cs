namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ScreenToGif HwndSource interop slice: custom-chrome windows hook the
/// source (ExWindow), capture helpers read the DPI/scale session values
/// (Other.Dpi/Scale/DpiOfSystem), and capture/screen helpers touch window
/// handles (ScreenHelper, Recorder, TestField). The browser has no HWND or
/// Win32 pump: handles and GDI imaging are precise boundaries, while DPI,
/// the hook list and the message struct are real session behavior.
/// </summary>
[ResetAmbientState]
public sealed class HwndInteropTests
{
    [Fact]
    public void Source_parameters_carry_the_construction_values()
    {
        var defaults = new HwndSourceParameters();
        Assert.Equal(0, defaults.WindowStyle);
        Assert.Equal(0, defaults.Width);
        Assert.Equal(0, defaults.Height);

        var named = new HwndSourceParameters("capture", 800, 600);
        Assert.Equal("capture", named.WindowName);
        Assert.Equal(800, named.Width);
        Assert.Equal(600, named.Height);

        named.SetPosition(10, 20);
        Assert.Equal(10, named.PositionX);
        Assert.Equal(20, named.PositionY);
        named.SetSize(640, 480);
        Assert.Equal(640, named.Width);
        Assert.Equal(480, named.Height);
    }

    [Fact]
    public void Session_source_reports_scale_and_lifetime()
    {
        using var source = new HwndSource(new HwndSourceParameters("session", 800, 600));
        Assert.False(source.IsDisposed);
        Assert.NotNull(source.CompositionTarget);
        Assert.Equal(1.0, source.CompositionTarget.TransformToDevice.M11);
        Assert.Equal(1.0, source.CompositionTarget.TransformFromDevice.M11);

        // The capture helpers' DPI math over a session source (Other.DpiOfSystem shape).
        Assert.Equal(96.0, 96d * (source.CompositionTarget?.TransformToDevice.M11 ?? 1D));

        var root = new Border();
        source.RootVisual = root;
        Assert.Same(root, source.RootVisual);

        source.Dispose();
        Assert.True(source.IsDisposed);
    }

    [Fact]
    public void Hooks_register_and_detach_without_a_win32_pump()
    {
        using var source = new HwndSource(new HwndSourceParameters());
        var calls = 0;
        HwndSourceHook hook = (IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) =>
        {
            calls++;
            return IntPtr.Zero;
        };
        source.AddHook(hook);
        source.RemoveHook(hook);

        // No Win32 pump exists in the session, so registration alone delivers nothing.
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Window_handles_are_a_precise_browser_boundary()
    {
        using var source = new HwndSource(new HwndSourceParameters());
        Assert.Throws<NotSupportedException>(() => source.Handle);

        var window = new Window();
        var helper = new WindowInteropHelper(window);
        Assert.Same(window, helper.Window);
        Assert.Throws<NotSupportedException>(() => helper.Handle);
    }

    [Fact]
    public void Gdi_imaging_has_no_device_context_in_the_browser()
    {
        Assert.Throws<NotSupportedException>(() =>
            Imaging.CreateBitmapSourceFromHBitmap(IntPtr.Zero, IntPtr.Zero, new Int32Rect(), BitmapSizeOptions.FromEmptyOptions()));
        Assert.Throws<NotSupportedException>(() =>
            Imaging.CreateBitmapSourceFromHIcon(IntPtr.Zero, new Int32Rect(), BitmapSizeOptions.FromEmptyOptions()));
        Assert.Throws<NotSupportedException>(() =>
            Imaging.CreateBitmapSourceFromMemorySection(IntPtr.Zero, 0, 0, PixelFormats.Bgra32, 0, 0));
    }

    [Fact]
    public void Message_struct_carries_hook_payloads()
    {
        var message = new MSG
        {
            hwnd = new IntPtr(7),
            message = 0x84,
            wParam = IntPtr.Zero,
            lParam = new IntPtr(0x00100010),
            time = 123,
            pt_x = 16,
            pt_y = 16,
        };
        Assert.Equal(new IntPtr(7), message.hwnd);
        Assert.Equal(0x84, message.message);
        Assert.Equal(16, message.pt_x);
        Assert.Equal(16, message.pt_y);
    }

    [Fact]
    public void Presentation_source_still_has_no_session_visual()
    {
        Assert.Null(PresentationSource.FromVisual(new Border()));
        Assert.Null(PresentationSource.FromVisual(null));
    }
}
