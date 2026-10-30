using DesktopWasm.Core;
using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf.Tests;

// Regression tests for review finding P1-6: WPF XAML input uses #AARRGGBB
// while the engine/Canvas2D format is 0xRRGGBBAA, so the alpha byte must be
// rotated at the XAML boundary. Every expected value below is hand-computed
// from the WPF color spec (never by running the parser under test):
//   #AARRGGBB -> bytes RR GG BB AA, e.g. #800000FF -> 0x0000FF80.
// Named references: WPF Green is #FF008000, WPF Lime is #FF00FF00.
[ResetAmbientState]
public sealed class XamlAlphaColorTests
{
    [Theory]
    [InlineData("#800000FF", 0x0000FF80u)] // partial alpha: half-transparent blue
    [InlineData("#00000000", 0x00000000u)] // fully transparent
    [InlineData("#FFFFFFFF", 0xFFFFFFFFu)] // fully opaque white
    [InlineData("#FF0000FF", 0x0000FFFFu)] // opaque blue: alpha moves to the end
    [InlineData("#FFFF0000", 0xFF0000FFu)] // opaque red
    [InlineData("#FF0000", 0xFF0000FFu)] // 6-digit: opaque alpha implied
    [InlineData("Green", 0x008000FFu)] // WPF #FF008000 (dark)
    [InlineData("Lime", 0x00FF00FFu)] // WPF #FF00FF00 (bright)
    public void Eight_digit_xaml_colors_convert_alpha_to_rrggbbaa(string input, uint expected)
    {
        var button = Assert.IsType<Button>(XamlLoader.Load($"<Button Background=\"{input}\"/>").Root);
        Assert.Equal(expected, button.BackgroundRgba);
    }

    [Fact]
    public void Green_and_lime_are_distinct_wpf_colors()
    {
        var green = Assert.IsType<Button>(XamlLoader.Load("<Button Background=\"Green\"/>").Root);
        var lime = Assert.IsType<Button>(XamlLoader.Load("<Button Background=\"Lime\"/>").Root);
        Assert.Equal(0x008000FFu, green.BackgroundRgba);
        Assert.Equal(0x00FF00FFu, lime.BackgroundRgba);
        Assert.NotEqual(green.BackgroundRgba, lime.BackgroundRgba);
    }

    [Fact]
    public void Partial_alpha_reaches_canvas2d_transport_as_rrggbbaa()
    {
        var window = Assert.IsType<Window>(
            XamlLoader.Load("<Window><Border Background=\"#800000FF\"/></Window>").Root);
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(800, 600));
        var json = DrawCommandJson.Serialize(commands);
        // Hand-computed CSS payload Canvas2D will paint: RR=00 GG=00 BB=FF AA=80.
        Assert.Contains("#0000ff80", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Opaque_blue_border_renders_blue_pixels()
    {
        using var surface = new SkiaSurface(800, 600);
        var host = new WasmWpfHost(new WasmDispatcherPump(), surface, new WasmInputRouter());
        var window = Assert.IsType<Window>(
            XamlLoader.Load("<Window><Border Background=\"#FF0000FF\"/></Window>").Root);
        host.AddWindow(window, new WpfSize(800, 600));
        host.Tick(new FrameContext(1, 16.6));

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        // WPF #FF0000FF is opaque blue, independent of the parser.
        Assert.Equal(new SKColor(0, 0, 255, 255), bitmap.GetPixel(400, 300));
    }

    [Fact]
    public void Named_green_border_renders_dark_green_pixels()
    {
        using var surface = new SkiaSurface(800, 600);
        var host = new WasmWpfHost(new WasmDispatcherPump(), surface, new WasmInputRouter());
        var window = Assert.IsType<Window>(
            XamlLoader.Load("<Window><Border Background=\"Green\"/></Window>").Root);
        host.AddWindow(window, new WpfSize(800, 600));
        host.Tick(new FrameContext(1, 16.6));

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        // WPF Green is #FF008000, independent of the parser.
        Assert.Equal(new SKColor(0, 128, 0, 255), bitmap.GetPixel(400, 300));
    }
}
