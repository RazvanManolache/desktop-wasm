using System.Globalization;
using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Text/font/DPI cluster (ScreenToGif text rendering): Typeface,
/// GlyphTypeface, DpiScale, NumberSubstitution, TextFormattingMode, the
/// TextDecoration family, DesignerProperties and SystemFonts, wired into the
/// HarfBuzz/TextShaper/DPI pipeline with measured shaping/layout output.
/// </summary>
[ResetAmbientState]
public sealed class TextFontDpiTests
{
    private static Typeface SansTypeface(double size = 16) =>
        new(new FontFamily("sans-serif"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    [Fact]
    public void Typeface_resolves_through_harfbuzz_with_measured_advances()
    {
        var face = SansTypeface();
        Assert.True(face.TryGetGlyphTypeface(out var glyphs));
        Assert.NotNull(glyphs);
        Assert.True(glyphs!.CharacterToGlyphMap.TryGetValue('A', out var gid), "map covers 'A'");
        Assert.NotEqual(0, gid);
        Assert.True(glyphs.AdvanceWidths.TryGetValue(gid, out var advance), "advance for 'A'");
        Assert.True(advance > 0, $"advance {advance} is measured, not stubbed");

        var shaped = TextShaper.Shape("Hello World", new FontSpec(16, "sans-serif"));
        Assert.Equal(TextShaper.HarfBuzzBackend, shaped.Backend);
        Assert.True(shaped.Width > 0 && shaped.GlyphCount == "Hello World".Length);
    }

    [Fact]
    public void Typeface_string_ctor_and_value_equality()
    {
        var a = new Typeface("Segoe UI");
        Assert.Equal("Segoe UI", a.FontFamily.Source);
        Assert.Equal(FontStyles.Normal, a.Style);
        Assert.Equal(FontWeights.Normal, a.Weight);
        var b = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void FormattedText_measures_through_the_same_shaper()
    {
        var ft = new FormattedText("Hello", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            SansTypeface(), 16, Brushes.Black);
        var shaped = TextShaper.Shape("Hello", new FontSpec(16, "sans-serif"));
        Assert.Equal(shaped.Width, ft.Width, precision: 3);
        Assert.Equal(shaped.Height, ft.Height, precision: 3);
        Assert.True(ft.Baseline > 0, $"baseline {ft.Baseline} is measured");
        Assert.Equal(ft.Height, ft.Extent, precision: 3);
    }

    [Fact]
    public void FormattedText_applies_dpi_scale()
    {
        var face = SansTypeface();
        var at96 = new FormattedText("Hello", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            face, 16, Brushes.Black, pixelsPerDip: 1.0);
        var at192 = new FormattedText("Hello", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            face, 16, Brushes.Black, pixelsPerDip: 2.0);
        // Device-independent pixels are scale-invariant; the underlying
        // raster doubles.
        Assert.Equal(at96.Width, at192.Width, precision: 3);
        Assert.Equal(at96.Height, at192.Height, precision: 3);
        Assert.Equal(2.0, at192.PixelsPerDip);
        var px96 = TextShaper.Shape("Hello", new FontSpec(16, "sans-serif")).Width;
        var px192 = TextShaper.Shape("Hello", new FontSpec(32, "sans-serif")).Width;
        Assert.Equal(px96, at96.Width, precision: 3);
        Assert.Equal(px96 * 2, px192, precision: 0);
    }

    [Fact]
    public void FormattedText_display_mode_snaps_to_whole_pixels()
    {
        var ft = new FormattedText("Hello", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            SansTypeface(), 16, Brushes.Black, numberSubstitution: null, TextFormattingMode.Display);
        Assert.Equal(Math.Ceiling(ft.Width), ft.Width, precision: 6);
        Assert.Equal(Math.Ceiling(ft.Height), ft.Height, precision: 6);
    }

    [Fact]
    public void FormattedText_truncates_at_max_text_width()
    {
        const string text = "This is a very long line of text";
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            SansTypeface(), 16, Brushes.Black) { MaxTextWidth = 60 };
        Assert.True(ft.Width <= 60, $"ellipsized width {ft.Width} exceeds 60");
        Assert.True(ft.Height > 0);
    }

    [Fact]
    public void FormattedText_applies_native_national_digit_substitution()
    {
        var substitution = new NumberSubstitution(NumberCultureSource.Override,
            new CultureInfo("ar-SA"), NumberSubstitutionMethod.NativeNational);
        var ft = new FormattedText("Room 123", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            SansTypeface(), 16, Brushes.Black, substitution);
        Assert.Contains("١٢٣", ft.Text, StringComparison.Ordinal);
        Assert.True(ft.Width > 0);
        var european = new FormattedText("Room 123", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            SansTypeface(), 16, Brushes.Black,
            new NumberSubstitution(NumberCultureSource.Text, null, NumberSubstitutionMethod.European));
        Assert.Equal("Room 123", european.Text);
    }

    [Fact]
    public void FormattedText_build_geometry_returns_outlined_bounds()
    {
        var ft = new FormattedText("Hi", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            SansTypeface(), 32, Brushes.Black);
        var geometry = ft.BuildGeometry(new Point(10, 40));
        var bounds = geometry.Bounds;
        Assert.True(bounds.Width > 0 && bounds.Height > 0, "glyph outlines occupy area");
        Assert.True(Math.Abs(bounds.Width - ft.Width) < ft.Width * 0.35 + 2,
            $"outline width {bounds.Width} tracks measured {ft.Width}");
        Assert.Contains("M", geometry.ToPathData(), StringComparison.Ordinal);
    }

    [Fact]
    public void Geometry_bounds_transform_and_render_bounds_behave()
    {
        Assert.True(Geometry.Empty.Bounds.IsEmpty);
        var ft = new FormattedText("Hi", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            SansTypeface(), 32, Brushes.Black);
        var geometry = ft.BuildGeometry(new Point(0, 40));
        var before = geometry.Bounds;
        geometry.Transform = new TranslateTransform(5, 7);
        var after = geometry.Bounds;
        Assert.Equal(before.X + 5, after.X, precision: 3);
        Assert.Equal(before.Y + 7, after.Y, precision: 3);
        var pen = new Pen(Brushes.Black, 4);
        var stroked = geometry.GetRenderBounds(pen);
        Assert.True(stroked.Width >= after.Width + 3.5, "stroke inflates the render bounds");
    }

    [Fact]
    public void FormattedText_build_highlight_geometry_matches_text_width()
    {
        var ft = new FormattedText("Hello", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            SansTypeface(), 16, Brushes.Black);
        var highlight = ft.BuildHighlightGeometry(new Point(4, 40)).Bounds;
        Assert.True(Math.Abs(highlight.Width - ft.Width) < 1.5,
            $"highlight {highlight.Width} tracks text {ft.Width}");
        Assert.Equal(4, highlight.X, precision: 3);
        var range = ft.BuildHighlightGeometry(new Point(4, 40), 1, 2).Bounds;
        Assert.True(range.Width > 0 && range.Width < highlight.Width, "sub-range is narrower");
    }

    [Fact]
    public void TextDecoration_strikethrough_paints_a_line()
    {
        var block = new TextBlock { Text = "Local path", FontCss = "14px sans-serif" };
        block.TextDecorations.Add(new TextDecoration(TextDecorationLocation.Strikethrough,
            new Pen(Brushes.DarkSlateGray, 1), 0,
            TextDecorationUnit.FontRecommended, TextDecorationUnit.FontRecommended));
        var commands = WasmMilCore.RenderHeadless(block, new WpfSize(300, 60));
        Assert.Contains(commands, c => c is DrawCommand.DrawText);
        var line = Assert.IsType<DrawCommand.DrawLine>(Assert.Single(commands, c => c is DrawCommand.DrawLine));
        Assert.True(line.X2 > line.X1, "decoration spans the text width");
    }

    [Fact]
    public void TextDecorations_parse_underline_from_xaml()
    {
        var window = (Window)XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <TextBlock Text="x" TextDecorations="Underline"/>
            </Window>
            """).Root;
        var block = (TextBlock)((Window)window).Content!;
        Assert.Single(block.TextDecorations!);
        Assert.Equal(TextDecorationLocation.Underline, block.TextDecorations![0].Location);
    }

    [Fact]
    public void SystemFonts_message_defaults_back_text_registrations()
    {
        Assert.Equal("Segoe UI", SystemFonts.MessageFontFamily.Source);
        Assert.Equal(12.0, SystemFonts.MessageFontSize);
        Assert.Equal(FontStyles.Normal, SystemFonts.MessageFontStyle);
        Assert.Equal(FontWeights.Normal, SystemFonts.MessageFontWeight);
        Assert.NotNull(SystemFonts.MessageFontSizeKey);
    }

    [Fact]
    public void DesignerProperties_design_mode_defaults_false_and_settable()
    {
        var element = new TextBlock();
        Assert.False(DesignerProperties.GetIsInDesignMode(element));
        DesignerProperties.SetIsInDesignMode(element, true);
        Assert.True(DesignerProperties.GetIsInDesignMode(element));
    }

    [Fact]
    public void DependencyPropertyDescriptor_reports_registration_metadata()
    {
        var descriptor = DependencyPropertyDescriptor.FromProperty(
            DesignerProperties.IsInDesignModeProperty, typeof(FrameworkElement));
        Assert.NotNull(descriptor);
        Assert.False((bool)descriptor.Metadata.DefaultValue!);
        Assert.Equal(typeof(FrameworkElement), descriptor.ComponentType);
    }

    [Fact]
    public void DpiScale_carries_session_factors()
    {
        var dpi = new DpiScale(1.25, 1.5);
        Assert.Equal(1.25, dpi.DpiScaleX);
        Assert.Equal(1.5, dpi.DpiScaleY);
        Assert.Equal(1.25, dpi.PixelsPerDip);
        Assert.Equal(120.0, dpi.PixelsPerInchX);
        Assert.Equal(144.0, dpi.PixelsPerInchY);
    }

    [Fact]
    public void VisualTreeHelper_get_dpi_follows_the_session_scale()
    {
        var text = new TextBlock { Text = "Hi" };
        Assert.Equal(1.0, VisualTreeHelper.GetDpi(text).PixelsPerDip);
        VisualTreeHelper.SetRootDpi(new DpiScale(2.0, 2.0));
        try
        {
            Assert.Equal(192.0, VisualTreeHelper.GetDpi(text).PixelsPerInchX);
        }
        finally
        {
            VisualTreeHelper.SetRootDpi(new DpiScale(1.0, 1.0));
        }
    }

    [Fact]
    public void Window_dpi_changed_event_roundtrips_scales()
    {
        var window = new Window();
        DpiChangedEventArgs? seen = null;
        window.DpiChanged += (_, e) => seen = e;
        window.RaiseDpiChanged(new DpiScale(1.0, 1.0), new DpiScale(1.5, 1.5));
        Assert.NotNull(seen);
        Assert.Equal(1.5, seen.NewDpi.DpiScaleX);
        Assert.Equal(96.0, seen.OldDpi.PixelsPerInchX);
        Assert.Same(window, seen.Source);
    }

    [Fact]
    public void Text_lab_cycles_decoration_outline_and_dpi()
    {
        var window = DesktopWasm.BrowserHost.DemoCatalog.Find("text").Build();
        var lab = DesktopWasm.BrowserHost.TextLab.Current;
        Assert.NotNull(lab);
        var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
        host.AddWindow(window, new WpfSize(800, 600));
        lab.Wire(host);

        Assert.Equal("Decoration: Underline", lab.DecorationReadout.Text);
        Assert.Contains("Dpi: 1 (96 ppi)", lab.DpiReadout.Text);
        Assert.StartsWith("Outline: ", lab.OutlineReadout.Text);
        Assert.True(lab.Outline.Data is not null && !lab.Outline.Data.Bounds.IsEmpty);

        lab.Cycle.RaiseClick();
        Assert.Equal("Decoration: Strikethrough", lab.DecorationReadout.Text);
        lab.Cycle.RaiseClick();
        Assert.Equal("Decoration: None", lab.DecorationReadout.Text);
        Assert.Empty(lab.Caption.TextDecorations!);

        var frame = host.Tick(new FrameContext(1, 0));
        Assert.True(frame.Json.Length > 500);
        VisualTreeHelper.SetRootDpi(new DpiScale(1.0, 1.0));
    }

    [Fact]
    public void TextPath_call_sequence_builds_paintable_geometry()
    {
        // Mirrors ScreenToGif TextPath.OnPropertyChanged/OnRender/MeasureOverride
        // against the engine text/DPI surface (SystemFonts defaults, session DPI,
        // outline geometry, pen paint, stroke-inflated measure).
        var dpi = VisualTreeHelper.GetDpi(new TextBlock()).PixelsPerDip;
        var formatted = new FormattedText("Gif", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(SystemFonts.MessageFontFamily, SystemFonts.MessageFontStyle,
                SystemFonts.MessageFontWeight, FontStretches.Normal),
            SystemFonts.MessageFontSize, Brushes.Black, dpi);
        var geometry = formatted.BuildGeometry(new Point(0.5, 0.5));
        geometry.Transform = new TranslateTransform(-geometry.Bounds.X, -geometry.Bounds.Y + 1);
        var pen = new Pen(Brushes.Black, 1)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        var dc = new DrawingContext();
        dc.DrawGeometry(null, pen, geometry);
        var paint = Assert.IsType<DrawCommand.DrawGeometry>(Assert.Single(dc.Commands.OfType<DrawCommand.DrawGeometry>()));
        Assert.Contains(dc.Commands, c => c is DrawCommand.PushTransform);
        Assert.False(string.IsNullOrWhiteSpace(paint.PathData), "text outline path reaches the paint op");
        var renderBounds = geometry.GetRenderBounds(pen);
        Assert.True(renderBounds.Width > 0 && renderBounds.Height > 0);
        Assert.True(formatted.Width > 0 && formatted.Height > 0, "measured, not stubbed");
    }
}
