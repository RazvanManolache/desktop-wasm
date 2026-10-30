using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Core.Tests;

public sealed class TextShaperTests
{
    [Fact]
    public void Shape_uses_harfbuzz_with_ordered_widths()
    {
        var wide = TextShaper.Shape("Hello World", new FontSpec(14, null));
        var narrow = TextShaper.Shape("Hi", new FontSpec(14, null));

        Assert.Equal(TextShaper.HarfBuzzBackend, wide.Backend);
        Assert.True(wide.Width > narrow.Width && narrow.Width > 0);
        Assert.True(wide.Height > 0);
        Assert.Equal(11, wide.GlyphCount);
    }

    [Fact]
    public void Shape_empty_returns_zero_width()
    {
        var shaped = TextShaper.Shape(string.Empty, new FontSpec(14, null));
        Assert.Equal(0, shaped.Width);
        Assert.Equal(0, shaped.GlyphCount);
    }

    [Fact]
    public void Repeat_shape_reuses_cached_run()
    {
        const string text = "shape-cache-probe recently added";
        var spec = new FontSpec(14, "sans-serif");
        var first = TextShaper.Shape(text, spec);
        Assert.True(TextShaper.IsShapeCachedForTests(text, 14, "sans-serif"));
        var second = TextShaper.Shape(text, spec);
        Assert.Equal(first, second);
        Assert.True(TextShaper.IsShapeCachedForTests(text, 14, "sans-serif"));
        Assert.False(TextShaper.IsShapeCachedForTests(text, 15, "sans-serif"));
        Assert.False(TextShaper.IsShapeCachedForTests(text, 14, "monospace"));
    }

    [Fact]
    public void Shape_cache_stays_bounded_under_distinct_texts()
    {
        for (var i = 0; i < 5000; i++)
            TextShaper.Shape($"shape-cache-flood title {i}", new FontSpec(14, "sans-serif"));
        Assert.True(TextShaper.ShapedRunCacheCount <= 4096, $"cache holds {TextShaper.ShapedRunCacheCount} runs");
    }

    [Fact]
    public void Reregistration_drops_stale_shaped_runs()
    {
        const string text = "shape-cache-reregister probe";
        TextShaper.Shape(text, new FontSpec(14, "sans-serif"));
        Assert.True(TextShaper.IsShapeCachedForTests(text, 14, "sans-serif"));
        var path = SystemFontFile();
        if (path is null) return;
        TextShaper.RegisterEmbeddedFont(File.ReadAllBytes(path));
        Assert.False(TextShaper.IsShapeCachedForTests(text, 14, "sans-serif"));
    }

    [Fact]
    public void ParseFontCss_reads_size_and_family()
    {
        Assert.Equal(new FontSpec(14, "sans-serif"), TextShaper.ParseFontCss("14px sans-serif"));
        Assert.Equal(new FontSpec(16, "serif"), TextShaper.ParseFontCss("bold 16px serif"));
        Assert.Equal(new FontSpec(12, null), TextShaper.ParseFontCss("12px"));
        Assert.Throws<ArgumentException>(() => TextShaper.ParseFontCss("sans-serif"));
    }

    [Fact]
    public void RegisterEmbeddedFont_rejects_garbage_and_accepts_real_font()
    {
        Assert.Throws<ArgumentNullException>(() => TextShaper.RegisterEmbeddedFont(null!));
        Assert.Throws<ArgumentException>(() => TextShaper.RegisterEmbeddedFont(new byte[] { 1, 2, 3 }));

        var path = SystemFontFile();
        if (path is null) return; // No system font to embed: resolution falls back to SKTypeface.Default.
        using var probe = SKTypeface.FromFile(path);
        Assert.NotNull(probe); // else the file is not a usable font on this machine.

        TextShaper.RegisterEmbeddedFont(File.ReadAllBytes(path));
        using var resolved = TextShaper.ResolveTypeface(probe.FamilyName);
        Assert.Equal(probe.FamilyName, resolved.FamilyName);
    }

    private static string? SystemFontFile()
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf")]
            : ["/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"];
        return candidates.FirstOrDefault(File.Exists);
    }
}
