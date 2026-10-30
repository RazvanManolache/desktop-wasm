using System.Reflection;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-76 HandyControl entry splash scope: SplashScreen ctor
/// retention, pack-store resolve plus Skia decode of the real image bytes,
/// single-show/close/fade/auto-close semantics. Member evidence via
/// Mono.Cecil on the upstream net10.0-windows HandyControlDemo.dll
/// (App.OpenSplashScreen calls .ctor(string)/Show(bool)); shapes
/// live-verified against the 9.0.21 WindowsBase ref pack
/// (.ctor(Assembly, string)/.ctor(string), Close(TimeSpan),
/// Show(bool)/Show(bool, bool), object base, no properties or events).
/// There is no native splash window in the session: visibility is retained
/// state the host overlay reads, and WpfAppBoot closes auto-close splashes
/// when the main window resolves.</summary>
[ResetAmbientState]
public sealed class SplashScreenTests : IDisposable
{
    public SplashScreenTests()
    {
        SplashScreen.ResetForTests();
    }

    public void Dispose()
    {
        SplashScreen.ResetForTests();
    }

    private static byte[] PngBytes(int width, int height)
    {
        var rgba = new byte[checked(width * height * 4)];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = 0x20;
            rgba[i + 1] = 0x60;
            rgba[i + 2] = 0xA0;
            rgba[i + 3] = 0xFF;
        }
        return ImagingInterop.EncodePng(rgba, width, height);
    }

    private static SplashScreen ShownSplash(byte[]? bytes = null, bool autoClose = false, bool topMost = false)
    {
        SplashScreen.ResourceOverride = (_, _) => bytes ?? PngBytes(12, 7);
        var splash = new SplashScreen("Resources/Img/Cover.png");
        splash.Show(autoClose, topMost);
        return splash;
    }

    [Fact]
    public void Ctors_retain_name_and_assembly()
    {
        var plain = new SplashScreen("Resources/Img/Cover.png");
        Assert.Equal("Resources/Img/Cover.png", plain.ResourceName);
        Assert.Null(plain.ResourceAssembly);
        Assert.False(plain.IsVisible);
        Assert.Null(plain.Image);
        var asm = typeof(SplashScreenTests).Assembly;
        var scoped = new SplashScreen(asm, "cover.png");
        Assert.Same(asm, scoped.ResourceAssembly);
        Assert.Equal("cover.png", scoped.ResourceName);
        Assert.Throws<ArgumentNullException>(() => new SplashScreen((string)null!));
        Assert.Throws<ArgumentNullException>(() => new SplashScreen(asm, null!));
    }

    [Fact]
    public void Show_missing_resource_throws_precise_and_stays_hidden()
    {
        SplashScreen.ResourceOverride = (_, _) => null;
        var splash = new SplashScreen("Resources/Img/Missing.png");
        var error = Assert.Throws<FileNotFoundException>(() => splash.Show(true));
        Assert.Contains("Resources/Img/Missing.png", error.Message);
        Assert.False(splash.IsVisible);
        Assert.Null(splash.Image);
    }

    [Fact]
    public void Show_decodes_real_bytes_and_lands_flags()
    {
        var splash = ShownSplash(autoClose: true, topMost: true);
        Assert.True(splash.IsVisible);
        Assert.True(splash.AutoClose);
        Assert.True(splash.TopMost);
        Assert.NotNull(splash.Image);
        Assert.Equal(12, splash.Image!.PixelWidth);
        Assert.Equal(7, splash.Image.PixelHeight);
    }

    [Fact]
    public void Show_garbage_bytes_throws_loud()
    {
        SplashScreen.ResourceOverride = (_, _) => [0x00, 0x01, 0x02, 0x03];
        var splash = new SplashScreen("cover.png");
        Assert.Throws<InvalidOperationException>(() => splash.Show(false));
        Assert.False(splash.IsVisible);
    }

    [Fact]
    public void Show_twice_throws()
    {
        var splash = ShownSplash();
        Assert.Throws<InvalidOperationException>(() => splash.Show(false));
        Assert.True(splash.IsVisible);
    }

    [Fact]
    public void Close_zero_hides_immediately()
    {
        var splash = ShownSplash();
        splash.Close(TimeSpan.Zero);
        Assert.False(splash.IsVisible);
    }

    [Fact]
    public void Close_fades_on_the_dispatcher_pump()
    {
        var splash = ShownSplash();
        splash.Close(TimeSpan.FromSeconds(2));
        Assert.True(splash.IsVisible);
        DispatcherTimer.TickAll(DateTime.UtcNow.AddSeconds(3));
        Assert.False(splash.IsVisible);
    }

    [Fact]
    public void Close_negative_throws_and_close_unshown_throws()
    {
        var splash = ShownSplash();
        Assert.Throws<ArgumentOutOfRangeException>(() => splash.Close(TimeSpan.FromSeconds(-1)));
        Assert.True(splash.IsVisible);
        var fresh = new SplashScreen("cover.png");
        Assert.Throws<InvalidOperationException>(() => fresh.Close(TimeSpan.Zero));
    }

    [Fact]
    public void CloseAutoShown_closes_only_auto_close()
    {
        var auto = ShownSplash(autoClose: true);
        var manual = ShownSplash(autoClose: false);
        SplashScreen.CloseAutoShown();
        Assert.False(auto.IsVisible);
        Assert.True(manual.IsVisible);
    }

    [Fact]
    public void SplashScreen_matches_wpf_shape()
    {
        Assert.False(typeof(SplashScreen).IsSealed);
        Assert.Equal(typeof(object), typeof(SplashScreen).BaseType);
        Assert.NotNull(typeof(SplashScreen).GetConstructor([typeof(string)]));
        Assert.NotNull(typeof(SplashScreen).GetConstructor([typeof(Assembly), typeof(string)]));
        Assert.NotNull(typeof(SplashScreen).GetMethod("Show", [typeof(bool)]));
        Assert.NotNull(typeof(SplashScreen).GetMethod("Show", [typeof(bool), typeof(bool)]));
        Assert.NotNull(typeof(SplashScreen).GetMethod("Close", [typeof(TimeSpan)]));
    }
}
