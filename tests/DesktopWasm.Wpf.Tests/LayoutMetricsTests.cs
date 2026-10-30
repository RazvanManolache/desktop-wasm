namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class LayoutMetricsTests
{
    private static WpfSize Measure(UIElement el, double availableWidth = 800)
    {
        el.Measure(new WpfSize(availableWidth, 600));
        return el.DesiredSize;
    }

    [Fact]
    public void Short_text_keeps_legacy_minimums()
    {
        Assert.Equal(new WpfSize(120, 32), Measure(new Button { Content = "OK" }));
        Assert.Equal(new WpfSize(40, 20), Measure(new TextBlock { Text = "Hi" }));
        Assert.Equal(new WpfSize(120, 32), Measure(new Button { Content = string.Empty }));
    }

    [Fact]
    public void Long_text_grows_beyond_minimums()
    {
        var wide = new string('W', 60);
        Assert.True(Measure(new TextBlock { Text = wide }).Width > 40);
        Assert.True(Measure(new Button { Content = wide }).Width > 120);
    }

    [Fact]
    public void Large_font_grows_height()
    {
        var tall = Measure(new TextBlock { Text = "Hi", FontCss = "48px sans-serif" });
        Assert.True(tall.Height > 20);
    }

    [Fact]
    public void Invalid_fontcss_falls_back_without_throwing()
    {
        var size = Measure(new TextBlock { Text = "Hi", FontCss = "not-a-font" });
        Assert.Equal(new WpfSize(40, 20), size);
    }

    [Fact]
    public void Default_colors_are_opaque_under_RRGGBBAA()
    {
        static byte Alpha(uint rgba) => (byte)(rgba & 0xFF);
        Assert.Equal(0xFF, Alpha(new Button().BackgroundRgba));
        Assert.Equal(0xFF, Alpha(new Button().ForegroundRgba));
        Assert.Equal(0xFF, Alpha(new TextBlock().ForegroundRgba));
        Assert.Equal(0xFF, Alpha(new Border().BackgroundRgba));
    }
}
