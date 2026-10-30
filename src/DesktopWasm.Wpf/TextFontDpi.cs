using System.Globalization;
using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf;

// Text/font/DPI cluster (ScreenToGif text rendering): Typeface,
// GlyphTypeface, DpiScale (+DpiChangedEventArgs/Handler), NumberSubstitution,
// TextFormattingMode, the TextDecoration family, DesignerProperties and
// SystemFonts. Every measurement flows through Graphics.TextShaper
// (HarfBuzz shaping, Steps 6/13 pipeline); nothing here fabricates metrics.

/// <summary>Style simulation flags (matches System.Windows.Media.StyleSimulations).</summary>
[Flags]
public enum StyleSimulations
{
    None = 0,
    BoldSimulation = 1,
    ItalicSimulation = 2,
    BoldItalicSimulation = BoldSimulation | ItalicSimulation,
}

/// <summary>Number culture source (matches System.Windows.Media.NumberCultureSource).</summary>
public enum NumberCultureSource
{
    Text = 0,
    User = 1,
    Override = 2,
}

/// <summary>Digit substitution method (matches System.Windows.Media.NumberSubstitutionMethod).</summary>
public enum NumberSubstitutionMethod
{
    AsCulture = 0,
    Context = 1,
    European = 2,
    NativeNational = 3,
    Traditional = 4,
}

/// <summary>
/// Digit substitution (matches System.Windows.Media.NumberSubstitution):
/// selects which culture's native digits replace ASCII 0-9. European (and a
/// null substitution on FormattedText) leaves digits untouched; the other
/// methods map through the override (or user/text) culture's NativeDigits,
/// which is what the converted editor passes for localized counters.
/// </summary>
public sealed class NumberSubstitution
{
    public NumberSubstitution()
        : this(NumberCultureSource.Text, null, NumberSubstitutionMethod.European)
    {
    }

    public NumberSubstitution(NumberCultureSource source, CultureInfo? cultureOverride, NumberSubstitutionMethod substitution)
    {
        CultureSource = source;
        CultureOverride = cultureOverride;
        Substitution = substitution;
    }

    public NumberCultureSource CultureSource { get; set; }

    public CultureInfo? CultureOverride { get; set; }

    public NumberSubstitutionMethod Substitution { get; set; }

    public static readonly DependencyProperty CultureSourceProperty =
        DependencyProperty.RegisterAttached("CultureSource", typeof(NumberCultureSource), typeof(NumberSubstitution),
            new PropertyMetadata(NumberCultureSource.Text));

    public static readonly DependencyProperty CultureOverrideProperty =
        DependencyProperty.RegisterAttached("CultureOverride", typeof(CultureInfo), typeof(NumberSubstitution),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SubstitutionProperty =
        DependencyProperty.RegisterAttached("Substitution", typeof(NumberSubstitutionMethod), typeof(NumberSubstitution),
            new PropertyMetadata(NumberSubstitutionMethod.European));

    public static void SetCultureSource(DependencyObject target, NumberCultureSource value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(CultureSourceProperty, value);
    }

    public static NumberCultureSource GetCultureSource(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (NumberCultureSource)(target.GetValue(CultureSourceProperty) ?? NumberCultureSource.Text);
    }

    public static void SetCultureOverride(DependencyObject target, CultureInfo? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(CultureOverrideProperty, value);
    }

    public static CultureInfo? GetCultureOverride(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (CultureInfo?)target.GetValue(CultureOverrideProperty);
    }

    public static void SetSubstitution(DependencyObject target, NumberSubstitutionMethod value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(SubstitutionProperty, value);
    }

    public static NumberSubstitutionMethod GetSubstitution(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (NumberSubstitutionMethod)(target.GetValue(SubstitutionProperty) ?? NumberSubstitutionMethod.European);
    }

    /// <summary>
    /// Maps ASCII digits to the substitution culture's native digits.
    /// European (or null culture data) is the identity; the result feeds
    /// shaping/measurement so substituted text measures with real advances.
    /// </summary>
    internal string ApplyDigits(string text, CultureInfo textCulture)
    {
        if (Substitution == NumberSubstitutionMethod.European) return text;
        var culture = CultureOverride
            ?? (CultureSource == NumberCultureSource.User ? CultureInfo.CurrentUICulture : textCulture);
        string[] native;
        try
        {
            native = culture.NumberFormat.NativeDigits;
        }
        catch (Exception)
        {
            return text;
        }
        if (native.Length < 10 || native.All(d => d.Length == 1 && d[0] is >= '0' and <= '9')) return text;
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
            builder.Append(ch is >= '0' and <= '9' ? native[ch - '0'] : ch.ToString());
        return builder.ToString();
    }
}

/// <summary>Text shaping mode (matches System.Windows.Media.TextFormattingMode).</summary>
public enum TextFormattingMode
{
    Ideal = 0,
    Display = 1,
}

/// <summary>
/// Text hinting mode (matches System.Windows.Media.TextHintingMode, verified
/// against the 9.0.21 PresentationCore reference: Auto=0, Fixed=1,
/// Animated=2). Converted AvalonDock floating windows inherit it.
/// </summary>
public enum TextHintingMode
{
    Auto = 0,
    Fixed = 1,
    Animated = 2,
}

/// <summary>
/// Text rendering mode (matches System.Windows.Media.TextRenderingMode,
/// verified against the 9.0.21 PresentationCore reference: Auto=0,
/// Aliased=1, Grayscale=2, ClearType=3). Converted AvalonDock floating
/// windows inherit it.
/// </summary>
public enum TextRenderingMode
{
    Auto = 0,
    Aliased = 1,
    Grayscale = 2,
    ClearType = 3,
}

/// <summary>
/// Text rendering options (matches System.Windows.Media.TextOptions): the
/// attached text-formatting-mode contract OxyPlot's CanvasRenderContext
/// drives (SetTextFormattingMode over its TextFormattingMode). Values are
/// retained in the dependency-property store; the engine shapes text in
/// Ideal mode and does not re-hint per element (no per-glyph ClearType
/// retuning in the browser compositor).
/// </summary>
public static class TextOptions
{
    public static readonly DependencyProperty TextFormattingModeProperty =
        DependencyProperty.RegisterAttached("TextFormattingMode", typeof(TextFormattingMode), typeof(TextOptions), TextFormattingMode.Ideal);

    public static TextFormattingMode GetTextFormattingMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(TextFormattingModeProperty) is TextFormattingMode mode
            ? mode
            : TextFormattingMode.Ideal;
    }

    public static void SetTextFormattingMode(DependencyObject element, TextFormattingMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextFormattingModeProperty, value);
    }

    /// <summary>
    /// Hinting identity (matches TextOptions.TextHintingModeProperty; converted
    /// AvalonDock floating windows inherit it through GetInheritableProperties).
    /// </summary>
    public static readonly DependencyProperty TextHintingModeProperty =
        DependencyProperty.RegisterAttached("TextHintingMode", typeof(TextHintingMode), typeof(TextOptions), TextHintingMode.Auto);

    /// <summary>Reads the hinting mode (unset means Auto).</summary>
    public static TextHintingMode GetTextHintingMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(TextHintingModeProperty) is TextHintingMode mode
            ? mode
            : TextHintingMode.Auto;
    }

    /// <summary>Writes the hinting mode.</summary>
    public static void SetTextHintingMode(DependencyObject element, TextHintingMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextHintingModeProperty, value);
    }

    /// <summary>
    /// Rendering-mode identity (matches TextOptions.TextRenderingModeProperty;
    /// converted AvalonDock floating windows inherit it through
    /// GetInheritableProperties).
    /// </summary>
    public static readonly DependencyProperty TextRenderingModeProperty =
        DependencyProperty.RegisterAttached("TextRenderingMode", typeof(TextRenderingMode), typeof(TextOptions), TextRenderingMode.Auto);

    /// <summary>Reads the rendering mode (unset means Auto).</summary>
    public static TextRenderingMode GetTextRenderingMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(TextRenderingModeProperty) is TextRenderingMode mode
            ? mode
            : TextRenderingMode.Auto;
    }

    /// <summary>Writes the rendering mode.</summary>
    public static void SetTextRenderingMode(DependencyObject element, TextRenderingMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextRenderingModeProperty, value);
    }
}

/// <summary>Decoration position (matches System.Windows.TextDecorationLocation).</summary>
public enum TextDecorationLocation
{
    Underline = 0,
    OverLine = 1,
    Strikethrough = 2,
    Baseline = 3,
}

/// <summary>Decoration unit (matches System.Windows.TextDecorationUnit).</summary>
public enum TextDecorationUnit
{
    FontRecommended = 0,
    FontRenderingEmSize = 1,
    Pixel = 2,
}

/// <summary>
/// Text decoration (matches System.Windows.TextDecoration): pen, offset and
/// location. Converted settings code builds Strikethrough decorations with
/// FontRecommended units; <see cref="TextBlock"/> resolves those against the
/// resolved typeface metrics when painting.
/// </summary>
public sealed class TextDecoration : Freezable
{
    public TextDecoration()
    {
    }

    public TextDecoration(TextDecorationLocation location, Pen? pen, double penOffset,
        TextDecorationUnit penOffsetUnit, TextDecorationUnit penThicknessUnit)
    {
        Location = location;
        Pen = pen;
        PenOffset = penOffset;
        PenOffsetUnit = penOffsetUnit;
        PenThicknessUnit = penThicknessUnit;
    }

    public Pen? Pen { get; set; }

    public double PenOffset { get; set; }

    public TextDecorationUnit PenOffsetUnit { get; set; } = TextDecorationUnit.FontRecommended;

    public TextDecorationUnit PenThicknessUnit { get; set; } = TextDecorationUnit.FontRecommended;

    public TextDecorationLocation Location { get; set; } = TextDecorationLocation.Underline;

    public static readonly DependencyProperty PenProperty =
        DependencyProperty.Register("Pen", typeof(Pen), typeof(TextDecoration), new PropertyMetadata(null));

    public static readonly DependencyProperty PenOffsetProperty =
        DependencyProperty.Register("PenOffset", typeof(double), typeof(TextDecoration), new PropertyMetadata(0.0));

    public static readonly DependencyProperty PenOffsetUnitProperty =
        DependencyProperty.Register("PenOffsetUnit", typeof(TextDecorationUnit), typeof(TextDecoration),
            new PropertyMetadata(TextDecorationUnit.FontRecommended));

    public static readonly DependencyProperty PenThicknessUnitProperty =
        DependencyProperty.Register("PenThicknessUnit", typeof(TextDecorationUnit), typeof(TextDecoration),
            new PropertyMetadata(TextDecorationUnit.FontRecommended));

    public static readonly DependencyProperty LocationProperty =
        DependencyProperty.Register("Location", typeof(TextDecorationLocation), typeof(TextDecoration),
            new PropertyMetadata(TextDecorationLocation.Underline));

    /// <summary>Typed clone (matches TextDecoration.Clone).</summary>
    public new TextDecoration Clone() => (TextDecoration)base.Clone();
}

/// <summary>Decoration list (matches System.Windows.TextDecorationCollection).</summary>
public sealed class TextDecorationCollection : List<TextDecoration>
{
    public TextDecorationCollection()
    {
    }

    public TextDecorationCollection(int capacity) : base(capacity)
    {
    }

    public TextDecorationCollection(IEnumerable<TextDecoration> collection) : base(collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
    }

    /// <summary>Copies the list (matches Clone).</summary>
    public TextDecorationCollection Clone() => new(this);

    /// <summary>Adds a range (matches Add(IEnumerable)).</summary>
    public void Add(IEnumerable<TextDecoration> textDecorations)
    {
        ArgumentNullException.ThrowIfNull(textDecorations);
        AddRange(textDecorations);
    }

    /// <summary>Removes a range (matches TryRemove).</summary>
    public bool TryRemove(IEnumerable<TextDecoration> textDecorations, out TextDecorationCollection result)
    {
        ArgumentNullException.ThrowIfNull(textDecorations);
        result = new TextDecorationCollection(this);
        var removed = false;
        foreach (var decoration in textDecorations)
            removed |= result.Remove(decoration);
        return removed;
    }
}

/// <summary>
/// Stock decorations (matches System.Windows.TextDecorations): shared frozen
/// underline/strikethrough/overline/baseline collections plus the
/// comma-separated XAML form ("Underline", "Strikethrough, OverLine").
/// </summary>
public static class TextDecorations
{
    public static TextDecorationCollection Underline { get; } = Freeze(new TextDecorationCollection
    {
        new TextDecoration(TextDecorationLocation.Underline, null, 0,
            TextDecorationUnit.FontRecommended, TextDecorationUnit.FontRecommended),
    });

    public static TextDecorationCollection Strikethrough { get; } = Freeze(new TextDecorationCollection
    {
        new TextDecoration(TextDecorationLocation.Strikethrough, null, 0,
            TextDecorationUnit.FontRecommended, TextDecorationUnit.FontRecommended),
    });

    public static TextDecorationCollection OverLine { get; } = Freeze(new TextDecorationCollection
    {
        new TextDecoration(TextDecorationLocation.OverLine, null, 0,
            TextDecorationUnit.FontRecommended, TextDecorationUnit.FontRecommended),
    });

    public static TextDecorationCollection Baseline { get; } = Freeze(new TextDecorationCollection
    {
        new TextDecoration(TextDecorationLocation.Baseline, null, 0,
            TextDecorationUnit.FontRecommended, TextDecorationUnit.FontRecommended),
    });

    private static TextDecorationCollection Freeze(TextDecorationCollection decorations)
    {
        foreach (var decoration in decorations) decoration.Freeze();
        return decorations;
    }

    /// <summary>Parses the XAML decoration form (matches TextDecorationCollectionConverter).</summary>
    public static TextDecorationCollection Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var parsed = new TextDecorationCollection();
        foreach (var token in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            parsed.Add(token.ToLowerInvariant() switch
            {
                "underline" => CloneOf(Underline),
                "strikethrough" => CloneOf(Strikethrough),
                "overline" => CloneOf(OverLine),
                "baseline" => CloneOf(Baseline),
                "none" or "" => null!,
                _ => throw new FormatException($"'{token}' is not a TextDecoration (Underline/Strikethrough/OverLine/Baseline/None)."),
            });
        }
        parsed.RemoveAll(d => d is null);
        return parsed;
    }

    private static TextDecoration CloneOf(TextDecorationCollection source) => source[0].Clone();
}

/// <summary>
/// DPI scale (matches System.Windows.DpiScale): per-axis device-independent
/// scaling plus the raw pixels-per-inch/dip factors converted editor math
/// reads (DpiScaleX, PixelsPerInchX). The session scale starts at 1.0 and
/// follows <see cref="VisualTreeHelper.SetRootDpi"/> host sync.
/// </summary>
public readonly struct DpiScale : IEquatable<DpiScale>, IFormattable
{
    public DpiScale(double dpiScaleX, double dpiScaleY)
    {
        if (dpiScaleX <= 0) throw new ArgumentOutOfRangeException(nameof(dpiScaleX));
        if (dpiScaleY <= 0) throw new ArgumentOutOfRangeException(nameof(dpiScaleY));
        DpiScaleX = dpiScaleX;
        DpiScaleY = dpiScaleY;
    }

    public double DpiScaleX { get; }

    public double DpiScaleY { get; }

    /// <summary>Pixels per device-independent pixel (matches PixelsPerDip).</summary>
    public double PixelsPerDip => DpiScaleX;

    public double PixelsPerInchX => DpiScaleX * 96.0;

    public double PixelsPerInchY => DpiScaleY * 96.0;

    public bool Equals(DpiScale other) => DpiScaleX == other.DpiScaleX && DpiScaleY == other.DpiScaleY;

    public override bool Equals(object? obj) => obj is DpiScale other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(DpiScaleX, DpiScaleY);

    public static bool operator ==(DpiScale left, DpiScale right) => left.Equals(right);

    public static bool operator !=(DpiScale left, DpiScale right) => !left.Equals(right);

    public override string ToString() => $"{DpiScaleX},{DpiScaleY}";

    public string ToString(string? format, IFormatProvider? provider) =>
        string.Format(provider, "{0},{1}", DpiScaleX.ToString(format, provider), DpiScaleY.ToString(format, provider));
}

/// <summary>DPI-change handler (matches System.Windows.DpiChangedEventHandler).</summary>
public delegate void DpiChangedEventHandler(object sender, DpiChangedEventArgs e);

/// <summary>
/// DPI-change payload (matches System.Windows.DpiChangedEventArgs): old and
/// new session scales. Raised by Window.RaiseDpiChanged (host DPI sync);
/// converted recorder/editor/region windows read
/// NewDpi.DpiScaleX/PixelsPerInchX off this.
/// </summary>
public sealed class DpiChangedEventArgs : RoutedEventArgs
{
    internal DpiChangedEventArgs(DpiScale oldDpi, DpiScale newDpi)
    {
        OldDpi = oldDpi;
        NewDpi = newDpi;
    }

    public DpiScale OldDpi { get; }

    public DpiScale NewDpi { get; }
}

/// <summary>
/// Font face selector (matches System.Windows.Media.Typeface): family plus
/// style/weight/stretch resolved through the shared TextShaper face cache
/// (embedded fonts first, then system lookup, then well-known files, then
/// the Skia default — resolution never returns null). Metrics come from the
/// resolved native face, so advances and heights are measured, not stubbed.
/// </summary>
public sealed class Typeface : IEquatable<Typeface>
{
    public Typeface(string typefaceName)
        : this(new FontFamily(typefaceName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)
    {
    }

    public Typeface(FontFamily fontFamily, FontStyle style, FontWeight weight, FontStretch stretch)
        : this(fontFamily, style, weight, stretch, fallbackFontFamily: null)
    {
    }

    public Typeface(FontFamily fontFamily, FontStyle style, FontWeight weight, FontStretch stretch, FontFamily? fallbackFontFamily)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);
        FontFamily = fontFamily;
        Style = style;
        Weight = weight;
        Stretch = stretch;
        FallbackFontFamily = fallbackFontFamily;
    }

    public FontFamily FontFamily { get; }

    public FontWeight Weight { get; }

    public FontStyle Style { get; }

    public FontStretch Stretch { get; }

    internal FontFamily? FallbackFontFamily { get; }

    /// <summary>First family token used for native resolution (WPF takes the primary name).</summary>
    internal string PrimaryFamilyName
    {
        get
        {
            var source = FontFamily.Source;
            var comma = source.IndexOf(',');
            var first = (comma < 0 ? source : source[..comma]).Trim().Trim('\'', '"');
            return string.IsNullOrEmpty(first) ? source : first;
        }
    }

    /// <summary>Resolved native face (shared TextShaper cache; never null).</summary>
    internal SKTypeface ResolveFace() => TextShaper.ResolveTypeface(PrimaryFamilyName);

    private static SKFontMetrics FaceMetrics(SKTypeface face)
    {
        using var font = new SKFont(face, 1000);
        return font.Metrics;
    }

    /// <summary>Oblique simulation (style requested, face not slanted).</summary>
    public bool IsObliqueSimulated
    {
        get
        {
            using var face = ResolveFace();
            return Style.ToString() != FontStyles.Normal.ToString() && face.FontSlant == SKFontStyleSlant.Upright;
        }
    }

    /// <summary>Bold simulation (heavy weight requested, face not bold).</summary>
    public bool IsBoldSimulated
    {
        get
        {
            using var face = ResolveFace();
            return Weight.OpenTypeWeight >= 600 && !face.IsBold;
        }
    }

    /// <summary>x-height in em units (measured off the resolved face).</summary>
    public double XHeight
    {
        get
        {
            using var face = ResolveFace();
            return FaceMetrics(face).XHeight / 1000.0;
        }
    }

    /// <summary>Cap height in em units (measured off the resolved face).</summary>
    public double CapsHeight
    {
        get
        {
            using var face = ResolveFace();
            return FaceMetrics(face).CapHeight / 1000.0;
        }
    }

    /// <summary>Underline offset in em units (measured off the resolved face).</summary>
    public double UnderlinePosition
    {
        get
        {
            using var face = ResolveFace();
            return (FaceMetrics(face).UnderlinePosition ?? 0) / 1000.0;
        }
    }

    /// <summary>Underline thickness in em units (measured off the resolved face).</summary>
    public double UnderlineThickness
    {
        get
        {
            using var face = ResolveFace();
            return Math.Max(0.01, (FaceMetrics(face).UnderlineThickness ?? 0) / 1000.0);
        }
    }

    /// <summary>Strikethrough offset in em units (measured off the resolved face).</summary>
    public double StrikethroughPosition
    {
        get
        {
            using var face = ResolveFace();
            return (FaceMetrics(face).StrikeoutPosition ?? 0) / 1000.0;
        }
    }

    /// <summary>Strikethrough thickness in em units (measured off the resolved face).</summary>
    public double StrikethroughThickness
    {
        get
        {
            using var face = ResolveFace();
            return Math.Max(0.01, (FaceMetrics(face).StrikeoutThickness ?? 0) / 1000.0);
        }
    }

    /// <summary>
    /// Builds the glyph face (matches TryGetGlyphTypeface): succeeds whenever
    /// the family resolves, which the shared cache guarantees; the map and
    /// advances are then measured lazily off the same native face.
    /// </summary>
    public bool TryGetGlyphTypeface(out GlyphTypeface? glyphTypeface)
    {
        glyphTypeface = GlyphTypeface.FromFace(ResolveFace(), Style, Weight, Stretch);
        return glyphTypeface is not null;
    }

    public bool Equals(Typeface? other) =>
        other is not null && FontFamily.Equals(other.FontFamily) &&
        Style.Equals(other.Style) && Weight.Equals(other.Weight) && Stretch.Equals(other.Stretch);

    public override bool Equals(object? obj) => obj is Typeface other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(FontFamily, Style, Weight, Stretch);

    public override string ToString() => $"{FontFamily} {Style} {Weight}";
}

/// <summary>
/// Glyph face (matches System.Windows.Media.GlyphTypeface): character map
/// and advances measured off the resolved native face. Vertical metrics,
/// symbol flags and embedding rights have no Skia source and stay unmapped
/// (no converted screen-to-gif member reads them).
/// </summary>
public sealed class GlyphTypeface
{
    private readonly SKTypeface? _ownedFace;

    private GlyphTypeface(SKTypeface face, FontStyle style, FontWeight weight, FontStretch stretch, Uri? fontUri, bool owned)
    {
        Face = face;
        Style = style;
        Weight = weight;
        Stretch = stretch;
        FontUri = fontUri;
        _ownedFace = owned ? face : null;
        var metrics = MetricsOf(face);
        Height = (metrics.Descent - metrics.Ascent + metrics.Leading) / 1000.0;
        Baseline = -metrics.Ascent / 1000.0;
        CapsHeight = metrics.CapHeight / 1000.0;
        XHeight = metrics.XHeight / 1000.0;
        UnderlinePosition = (metrics.UnderlinePosition ?? 0) / 1000.0;
        UnderlineThickness = Math.Max(0.01, (metrics.UnderlineThickness ?? 0) / 1000.0);
        StrikethroughPosition = (metrics.StrikeoutPosition ?? 0) / 1000.0;
        StrikethroughThickness = Math.Max(0.01, (metrics.StrikeoutThickness ?? 0) / 1000.0);
        StyleSimulations = (weight.OpenTypeWeight >= 600 && !face.IsBold ? StyleSimulations.BoldSimulation : 0)
            | (style.ToString() != FontStyles.Normal.ToString() && face.FontSlant == SKFontStyleSlant.Upright
                ? StyleSimulations.ItalicSimulation : 0);
    }

    public GlyphTypeface()
        : this(TextShaper.ResolveTypeface(null), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal, null, owned: false)
    {
    }

    public GlyphTypeface(Uri typefaceSource)
        : this(LoadFace(typefaceSource), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal, typefaceSource, owned: true)
    {
    }

    public GlyphTypeface(Uri typefaceSource, StyleSimulations styleSimulations)
        : this(LoadFace(typefaceSource),
              styleSimulations.HasFlag(StyleSimulations.ItalicSimulation) ? FontStyles.Italic : FontStyles.Normal,
              styleSimulations.HasFlag(StyleSimulations.BoldSimulation) ? FontWeights.Bold : FontWeights.Normal,
              FontStretches.Normal, typefaceSource, owned: true)
    {
    }

    /// <summary>Native face behind this glyph face (shared cache; do not dispose).</summary>
    internal SKTypeface Face { get; }

    public Uri? FontUri { get; }

    /// <summary>Family/face names (measured: the resolved native family name).</summary>
    public IDictionary<string, string> FamilyNames { get; } = new Dictionary<string, string>();

    /// <summary>Face names (measured: the resolved native family name).</summary>
    public IDictionary<string, string> FaceNames { get; } = new Dictionary<string, string>();

    public FontStyle Style { get; }

    public FontWeight Weight { get; }

    public FontStretch Stretch { get; }

    /// <summary>Line height in em units (ascent-to-descent plus leading).</summary>
    public double Height { get; }

    /// <summary>Baseline offset from the em-box top, in em units.</summary>
    public double Baseline { get; }

    public double CapsHeight { get; }

    public double XHeight { get; }

    public double UnderlinePosition { get; }

    public double UnderlineThickness { get; }

    public double StrikethroughPosition { get; }

    public double StrikethroughThickness { get; }

    public StyleSimulations StyleSimulations { get; }

    private GlyphCharacterMap? _characterToGlyphMap;

    /// <summary>
    /// Supported character map (measured: one native lookup per BMP code
    /// point on first access, then cached; astral planes resolve on demand
    /// through TryGetValue and join the cache).
    /// </summary>
    public IDictionary<int, ushort> CharacterToGlyphMap => _characterToGlyphMap ??= new GlyphCharacterMap(Face);

    private Dictionary<ushort, double>? _advanceWidths;

    /// <summary>
    /// Advance widths in em units (measured: single-glyph shaping per mapped
    /// glyph on first access, then cached).
    /// </summary>
    public IDictionary<ushort, double> AdvanceWidths => _advanceWidths ??= BuildAdvanceWidths(Face);

    internal static GlyphTypeface? FromFace(SKTypeface face, FontStyle style, FontWeight weight, FontStretch stretch)
    {
        if (face is null) return null;
        var glyphs = new GlyphTypeface(face, style, weight, stretch, null, owned: false);
        glyphs.FamilyNames["en-US"] = face.FamilyName;
        glyphs.FaceNames["en-US"] = face.FamilyName;
        return glyphs;
    }

    private static SKFontMetrics MetricsOf(SKTypeface face)
    {
        using var font = new SKFont(face, 1000);
        return font.Metrics;
    }

    private static SKTypeface LoadFace(Uri typefaceSource)
    {
        ArgumentNullException.ThrowIfNull(typefaceSource);
        try
        {
            var path = typefaceSource.IsFile ? typefaceSource.LocalPath : typefaceSource.OriginalString;
            if (File.Exists(path))
            {
                var face = SKTypeface.FromFile(path);
                if (face is not null) return face;
            }
        }
        catch (Exception)
        {
            // Fall through to the shared default below.
        }
        return TextShaper.ResolveTypeface(null);
    }

    internal static Dictionary<int, ushort> BuildCharacterMap(SKTypeface face)
    {
        var map = new Dictionary<int, ushort>();
        using var font = new SKFont(face, 12);
        for (var code = 0; code <= 0xFFFF; code++)
        {
            if (code is >= 0xD800 and <= 0xDFFF) continue;
            var glyph = font.GetGlyph(code);
            if (glyph != 0) map[code] = glyph;
        }
        return map;
    }

    private static Dictionary<ushort, double> BuildAdvanceWidths(SKTypeface face)
    {
        // One representative per glyph id: single-glyph shaping has no
        // kerning or ligature context, so the measured width is the advance.
        var firstChar = new Dictionary<ushort, char>();
        foreach (var pair in BuildCharacterMap(face))
        {
            if (pair.Key <= char.MaxValue && !firstChar.ContainsKey(pair.Value))
                firstChar[pair.Value] = (char)pair.Key;
        }
        var widths = new Dictionary<ushort, double>(firstChar.Count);
        using var font = new SKFont(face, 1000);
        foreach (var pair in firstChar)
            widths[pair.Key] = Math.Max(0, font.MeasureText(new string(pair.Value, 1)) / 1000.0);
        return widths;
    }
}

/// <summary>
/// Measured character map (backs GlyphTypeface.CharacterToGlyphMap): the BMP
/// fills eagerly off native lookups; astral code points resolve through the
/// same native lookup on demand and join the cache.
/// </summary>
internal sealed class GlyphCharacterMap : IDictionary<int, ushort>
{
    private readonly SKTypeface _face;
    private Dictionary<int, ushort>? _bmp;

    internal GlyphCharacterMap(SKTypeface face) => _face = face;

    private Dictionary<int, ushort> Bmp => _bmp ??= GlyphTypeface.BuildCharacterMap(_face);

    public ushort this[int key]
    {
        get
        {
            if (TryGetValue(key, out var glyph)) return glyph;
            throw new KeyNotFoundException($"U+{key:X4} has no glyph in '{_face.FamilyName}'.");
        }
        set => Bmp[key] = value;
    }

    public ICollection<int> Keys => Bmp.Keys;

    public ICollection<ushort> Values => Bmp.Values;

    public int Count => Bmp.Count;

    public bool IsReadOnly => false;

    public void Add(int key, ushort value) => Bmp.Add(key, value);

    public void Add(KeyValuePair<int, ushort> item) => Bmp.Add(item.Key, item.Value);

    public void Clear() => Bmp.Clear();

    public bool Contains(KeyValuePair<int, ushort> item) =>
        TryGetValue(item.Key, out var glyph) && glyph == item.Value;

    public bool ContainsKey(int key) => Bmp.ContainsKey(key) || ResolveAstral(key) != 0;

    public void CopyTo(KeyValuePair<int, ushort>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<int, ushort>>)Bmp).CopyTo(array, arrayIndex);

    public IEnumerator<KeyValuePair<int, ushort>> GetEnumerator() => Bmp.GetEnumerator();

    public bool Remove(int key) => Bmp.Remove(key);

    public bool Remove(KeyValuePair<int, ushort> item) =>
        Contains(item) && Bmp.Remove(item.Key);

    public bool TryGetValue(int key, out ushort value)
    {
        if (Bmp.TryGetValue(key, out value)) return true;
        var glyph = ResolveAstral(key);
        if (glyph == 0) { value = 0; return false; }
        Bmp[key] = value = glyph;
        return true;
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => Bmp.GetEnumerator();

    private ushort ResolveAstral(int key)
    {
        if (key < 0x10000 || key > 0x10FFFF) return 0;
        try
        {
            using var font = new SKFont(_face, 12);
            return font.GetGlyph(key);
        }
        catch (Exception)
        {
            return 0;
        }
    }
}

/// <summary>
/// Formatted text (matches System.Windows.Media.FormattedText): the converted
/// editor/TextPath/Util call shapes (6 ctor overloads, BuildGeometry,
/// MaxTextWidth/Height, number substitution, formatting mode, pixels-per-dip)
/// measured through the shared HarfBuzz shaper. Single-line layout with
/// CharacterEllipsis truncation at MaxTextWidth (the legacy engine
/// behavior); multi-line wrapping stays a documented later step.
/// Origin is the baseline-left text origin (Skia convention): highlight and
/// geometry rects share it, and TextPath normalizes through Bounds.
/// </summary>
public sealed class FormattedText
{
    public FormattedText(string textToFormat, CultureInfo culture, FlowDirection flowDirection,
        Typeface typeface, double emSize, Brush foreground)
        : this(textToFormat, culture, flowDirection, typeface, emSize, foreground, pixelsPerDip: 1.0)
    {
    }

    public FormattedText(string textToFormat, CultureInfo culture, FlowDirection flowDirection,
        Typeface typeface, double emSize, Brush foreground, double pixelsPerDip)
        : this(textToFormat, culture, flowDirection, typeface, emSize, foreground,
              numberSubstitution: null, pixelsPerDip: pixelsPerDip, formattingMode: TextFormattingMode.Ideal)
    {
    }

    public FormattedText(string textToFormat, CultureInfo culture, FlowDirection flowDirection,
        Typeface typeface, double emSize, Brush foreground, NumberSubstitution? numberSubstitution)
        : this(textToFormat, culture, flowDirection, typeface, emSize, foreground,
              numberSubstitution, pixelsPerDip: 1.0, formattingMode: TextFormattingMode.Ideal)
    {
    }

    public FormattedText(string textToFormat, CultureInfo culture, FlowDirection flowDirection,
        Typeface typeface, double emSize, Brush foreground, NumberSubstitution? numberSubstitution, double pixelsPerDip)
        : this(textToFormat, culture, flowDirection, typeface, emSize, foreground,
              numberSubstitution, pixelsPerDip, TextFormattingMode.Ideal)
    {
    }

    public FormattedText(string textToFormat, CultureInfo culture, FlowDirection flowDirection,
        Typeface typeface, double emSize, Brush foreground, NumberSubstitution? numberSubstitution, TextFormattingMode textFormattingMode)
        : this(textToFormat, culture, flowDirection, typeface, emSize, foreground,
              numberSubstitution, pixelsPerDip: 1.0, formattingMode: textFormattingMode)
    {
    }

    public FormattedText(string textToFormat, CultureInfo culture, FlowDirection flowDirection,
        Typeface typeface, double emSize, Brush foreground, NumberSubstitution? numberSubstitution,
        TextFormattingMode textFormattingMode, double pixelsPerDip)
        : this(textToFormat, culture, flowDirection, typeface, emSize, foreground,
              numberSubstitution, pixelsPerDip, textFormattingMode)
    {
    }

    private FormattedText(string textToFormat, CultureInfo culture, FlowDirection flowDirection,
        Typeface typeface, double emSize, Brush foreground, NumberSubstitution? numberSubstitution,
        double pixelsPerDip, TextFormattingMode formattingMode)
    {
        ArgumentNullException.ThrowIfNull(textToFormat);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(typeface);
        ArgumentNullException.ThrowIfNull(foreground);
        if (emSize <= 0) throw new ArgumentOutOfRangeException(nameof(emSize));
        if (pixelsPerDip <= 0) throw new ArgumentOutOfRangeException(nameof(pixelsPerDip));
        SourceText = textToFormat;
        Culture = culture;
        FlowDirection = flowDirection;
        Typeface = typeface;
        EmSize = emSize;
        Foreground = foreground;
        Substitution = numberSubstitution;
        PixelsPerDip = pixelsPerDip;
        FormattingMode = formattingMode;
    }

    /// <summary>Original text (digits substituted per <see cref="Text"/>).</summary>
    public string SourceText { get; }

    public CultureInfo Culture { get; }

    public FlowDirection FlowDirection { get; }

    public Typeface Typeface { get; }

    public double EmSize { get; }

    public Brush Foreground { get; private set; }

    public NumberSubstitution? Substitution { get; }

    public double PixelsPerDip { get; }

    public TextFormattingMode FormattingMode { get; }

    /// <summary>Text after digit substitution (measured and painted).</summary>
    public string Text => Substitution?.ApplyDigits(SourceText, Culture) ?? SourceText;

    /// <summary>Displayed text after ellipsis truncation at MaxTextWidth.</summary>
    public string DisplayText
    {
        get
        {
            var text = Text;
            if (MaxTextWidth <= 0 || string.IsNullOrEmpty(text)) return text;
            if (Snap(TypefaceShape(text).Width) <= MaxTextWidth) return text;
            if (Trimming == TextTrimming.None) return text;
            var lo = 0;
            var hi = text.Length;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                var candidate = text[..mid] + "…";
                if (Snap(TypefaceShape(candidate).Width) <= MaxTextWidth) lo = mid;
                else hi = mid - 1;
            }
            return lo == 0 ? string.Empty : text[..lo] + "…";
        }
    }

    public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;

    /// <summary>Line height override (NaN follows the shaped height).</summary>
    public double LineHeight { get; set; } = double.NaN;

    /// <summary>Wrap width (retained; layout stays single-line, see class note).</summary>
    public double MaxTextWidth { get; set; }

    /// <summary>Wrap height (retained; layout stays single-line, see class note).</summary>
    public double MaxTextHeight { get; set; }

    /// <summary>Line cap (retained; layout stays single-line, see class note).</summary>
    public int MaxLineCount { get; set; }

    /// <summary>Overflow rule (default truncates with an ellipsis at MaxTextWidth).</summary>
    public TextTrimming Trimming { get; set; } = TextTrimming.CharacterEllipsis;

    private double Snap(double dips) =>
        FormattingMode == TextFormattingMode.Display ? Math.Ceiling(dips) : dips;

    private ShapedText TypefaceShape(string text) =>
        TextShaper.Shape(text, new FontSpec((float)(EmSize * PixelsPerDip), Typeface.PrimaryFamilyName));

    private double ShapedWidthDips(string text) => TypefaceShape(text).Width / PixelsPerDip;

    private double ShapedHeightDips(string text) => TypefaceShape(text).Height / PixelsPerDip;

    /// <summary>Measured width in device-independent pixels.</summary>
    public double Width => Snap(ShapedWidthDips(DisplayText));

    /// <summary>Measured width including trailing whitespace (same run here).</summary>
    public double WidthIncludingTrailingWhitespace => Width;

    /// <summary>Measured height in device-independent pixels (LineHeight wins when set).</summary>
    public double Height
    {
        get
        {
            var shaped = ShapedHeightDips(DisplayText);
            var height = double.IsNaN(LineHeight) ? shaped : Math.Max(shaped, LineHeight);
            return Snap(height);
        }
    }

    /// <summary>Line-box height (single-line layout: same as Height).</summary>
    public double Extent => Height;

    /// <summary>Baseline offset from the text top, in device-independent pixels.</summary>
    public double Baseline
    {
        get
        {
            using var font = new SKFont(Typeface.ResolveFace(), (float)(EmSize * PixelsPerDip));
            return -font.Metrics.Ascent / (float)PixelsPerDip;
        }
    }

    /// <summary>Draws the run into a drawing context (retained op path).</summary>
    public void DrawInto(DrawingContext dc, double x, double y, uint rgba, int hAlign = 0, int vAlign = 0)
    {
        ArgumentNullException.ThrowIfNull(dc);
        var css = $"{EmSize.ToString(CultureInfo.InvariantCulture)}px {Typeface.PrimaryFamilyName}";
        dc.DrawText(DisplayText, (float)x, (float)y, css, rgba, hAlign, vAlign);
    }

    /// <summary>
    /// Builds the text outline geometry (matches BuildGeometry): real glyph
    /// outlines from the shaped run via the native text path, scaled back to
    /// device-independent pixels. Falls back to per-advance body rects only
    /// when the native path cannot be parsed.
    /// </summary>
    public Geometry BuildGeometry(Point origin)
    {
        var text = DisplayText;
        if (string.IsNullOrEmpty(text)) return new PathGeometry();
        var pixels = (float)(EmSize * PixelsPerDip);
        using var font = new SKFont(Typeface.ResolveFace(), pixels);
        var scale = 1.0 / PixelsPerDip;
        try
        {
            using var path = font.GetTextPath(text.AsSpan(), new SKPoint((float)(origin.X * PixelsPerDip), (float)(origin.Y * PixelsPerDip)));
            if (!path.IsEmpty)
            {
                path.Transform(SKMatrix.CreateScale((float)scale, (float)scale));
                return Geometry.Parse(path.ToSvgPathData());
            }
        }
        catch (Exception)
        {
            // Fall through to the measured-rect fallback below.
        }
        return AdvanceRectGeometry(text, origin, pixels);
    }

    private Geometry AdvanceRectGeometry(string text, Point origin, float pixels)
    {
        var height = Height;
        var figures = new PathGeometry();
        using var measure = new SKFont(Typeface.ResolveFace(), pixels);
        var x = origin.X;
        foreach (var ch in text)
        {
            var advance = Math.Max(0, measure.MeasureText(ch.ToString()) / PixelsPerDip);
            if (advance > 0)
            {
                var figure = new PathFigure { StartPoint = new Point(x, origin.Y - height), IsClosed = true };
                figure.Segments.Add(new LineSegment { Point = new Point(x + advance, origin.Y - height) });
                figure.Segments.Add(new LineSegment { Point = new Point(x + advance, origin.Y) });
                figure.Segments.Add(new LineSegment { Point = new Point(x, origin.Y) });
                figures.Figures.Add(figure);
            }
            x += advance;
        }
        return figures;
    }

    /// <summary>Highlight box over the whole run (matches BuildHighlightGeometry).</summary>
    public Geometry BuildHighlightGeometry(Point origin) =>
        BuildHighlightGeometry(origin, 0, Text.Length);

    /// <summary>Highlight box over a character range (measured prefix/run widths).</summary>
    public Geometry BuildHighlightGeometry(Point origin, int startIndex, int count)
    {
        var text = Text;
        if (startIndex < 0 || startIndex > text.Length) throw new ArgumentOutOfRangeException(nameof(startIndex));
        if (count < 0 || startIndex + count > text.Length) throw new ArgumentOutOfRangeException(nameof(count));
        var before = startIndex == 0 ? 0 : ShapedWidthDips(text[..startIndex]);
        var run = count == 0 ? 0 : ShapedWidthDips(text.Substring(startIndex, count));
        return new RectangleGeometry(new Rect(origin.X + before, origin.Y - Baseline, Math.Max(0, run), Height));
    }

    /// <summary>Sets the foreground brush (matches SetForegroundBrush).</summary>
    public void SetForegroundBrush(Brush foregroundBrush)
    {
        ArgumentNullException.ThrowIfNull(foregroundBrush);
        Foreground = foregroundBrush;
    }
}

/// <summary>
/// Design-time marker (matches System.ComponentModel.DesignerProperties):
/// converted WebcamControl/UserSettings query IsInDesignMode off converted
/// elements (false at runtime; settable for design-time hosts).
/// </summary>
public static class DesignerProperties
{
    public static readonly DependencyProperty IsInDesignModeProperty =
        DependencyProperty.RegisterAttached("IsInDesignMode", typeof(bool), typeof(DesignerProperties),
            new PropertyMetadata(false));

    /// <summary>Whether the element is in a designer (always false in live sessions).</summary>
    public static bool GetIsInDesignMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsInDesignModeProperty) ?? false);
    }

    public static void SetIsInDesignMode(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsInDesignModeProperty, value);
    }
}

/// <summary>System font key (matches WPF's internal SystemResourceKey for SystemFonts keys).</summary>
public sealed class SystemFontResourceKey : ResourceKey
{
    internal SystemFontResourceKey(string name) => Name = name;

    public string Name { get; }

    public override string ToString() => Name;
}

/// <summary>
/// System fonts (matches System.Windows.SystemFonts): session defaults for
/// the message/caption/menu/status/icon families converted TextPath
/// registrations default from. Real WPF reads OS metrics; the engine pins
/// the Segoe UI 9pt (12px) message defaults and reports them as the live
/// values (browser sessions have no OS font-metric source).
/// </summary>
public static class SystemFonts
{
    private const string DefaultFamily = "Segoe UI";
    private const double DefaultSize = 12.0;

    private static FontFamily Family(string? name) => new(string.IsNullOrEmpty(name) ? DefaultFamily : name);

    private static TextDecorationCollection Decorations(TextDecorationCollection source) => source.Clone();

    public static double IconFontSize => DefaultSize;

    public static FontFamily IconFontFamily => Family(null);

    public static FontStyle IconFontStyle => FontStyles.Normal;

    public static FontWeight IconFontWeight => FontWeights.Normal;

    public static TextDecorationCollection IconFontTextDecorations => Decorations(TextDecorations.Underline);

    public static double CaptionFontSize => DefaultSize;

    public static FontFamily CaptionFontFamily => Family(null);

    public static FontStyle CaptionFontStyle => FontStyles.Normal;

    public static FontWeight CaptionFontWeight => FontWeights.Bold;

    public static TextDecorationCollection CaptionFontTextDecorations => new();

    public static double SmallCaptionFontSize => DefaultSize;

    public static FontFamily SmallCaptionFontFamily => Family(null);

    public static FontStyle SmallCaptionFontStyle => FontStyles.Normal;

    public static FontWeight SmallCaptionFontWeight => FontWeights.Bold;

    public static TextDecorationCollection SmallCaptionFontTextDecorations => new();

    public static double MenuFontSize => DefaultSize;

    public static FontFamily MenuFontFamily => Family(null);

    public static FontStyle MenuFontStyle => FontStyles.Normal;

    public static FontWeight MenuFontWeight => FontWeights.Normal;

    public static TextDecorationCollection MenuFontTextDecorations => new();

    public static double StatusFontSize => DefaultSize;

    public static FontFamily StatusFontFamily => Family(null);

    public static FontStyle StatusFontStyle => FontStyles.Normal;

    public static FontWeight StatusFontWeight => FontWeights.Normal;

    public static TextDecorationCollection StatusFontTextDecorations => new();

    public static double MessageFontSize => DefaultSize;

    public static FontFamily MessageFontFamily => Family(null);

    public static FontStyle MessageFontStyle => FontStyles.Normal;

    public static FontWeight MessageFontWeight => FontWeights.Normal;

    public static TextDecorationCollection MessageFontTextDecorations => new();

    private static ResourceKey Key(string name) => new SystemFontResourceKey(name);

    public static ResourceKey IconFontSizeKey => Key("IconFontSize");

    public static ResourceKey IconFontFamilyKey => Key("IconFontFamily");

    public static ResourceKey IconFontStyleKey => Key("IconFontStyle");

    public static ResourceKey IconFontWeightKey => Key("IconFontWeight");

    public static ResourceKey IconFontTextDecorationsKey => Key("IconFontTextDecorations");

    public static ResourceKey CaptionFontSizeKey => Key("CaptionFontSize");

    public static ResourceKey CaptionFontFamilyKey => Key("CaptionFontFamily");

    public static ResourceKey CaptionFontStyleKey => Key("CaptionFontStyle");

    public static ResourceKey CaptionFontWeightKey => Key("CaptionFontWeight");

    public static ResourceKey CaptionFontTextDecorationsKey => Key("CaptionFontTextDecorations");

    public static ResourceKey SmallCaptionFontSizeKey => Key("SmallCaptionFontSize");

    public static ResourceKey SmallCaptionFontFamilyKey => Key("SmallCaptionFontFamily");

    public static ResourceKey SmallCaptionFontStyleKey => Key("SmallCaptionFontStyle");

    public static ResourceKey SmallCaptionFontWeightKey => Key("SmallCaptionFontWeight");

    public static ResourceKey SmallCaptionFontTextDecorationsKey => Key("SmallCaptionFontTextDecorations");

    public static ResourceKey MenuFontSizeKey => Key("MenuFontSize");

    public static ResourceKey MenuFontFamilyKey => Key("MenuFontFamily");

    public static ResourceKey MenuFontStyleKey => Key("MenuFontStyle");

    public static ResourceKey MenuFontWeightKey => Key("MenuFontWeight");

    public static ResourceKey MenuFontTextDecorationsKey => Key("MenuFontTextDecorations");

    public static ResourceKey StatusFontSizeKey => Key("StatusFontSize");

    public static ResourceKey StatusFontFamilyKey => Key("StatusFontFamily");

    public static ResourceKey StatusFontStyleKey => Key("StatusFontStyle");

    public static ResourceKey StatusFontWeightKey => Key("StatusFontWeight");

    public static ResourceKey StatusFontTextDecorationsKey => Key("StatusFontTextDecorations");

    public static ResourceKey MessageFontSizeKey => Key("MessageFontSize");

    public static ResourceKey MessageFontFamilyKey => Key("MessageFontFamily");

    public static ResourceKey MessageFontStyleKey => Key("MessageFontStyle");

    public static ResourceKey MessageFontWeightKey => Key("MessageFontWeight");

    public static ResourceKey MessageFontTextDecorationsKey => Key("MessageFontTextDecorations");
}

/// <summary>
/// Property descriptor (matches System.ComponentModel.DependencyPropertyDescriptor):
/// exposes the registration behind a DependencyProperty (default value,
/// owner, type) for converted helpers like VisualHelper.IsInDesignMode,
/// which reads Metadata.DefaultValue off DesignerProperties.
/// Source namespace System.ComponentModel maps through the WpfTypeMap
/// override (the namespace rule only covers System.Windows.*).
/// </summary>
public sealed class DependencyPropertyDescriptor : System.ComponentModel.PropertyDescriptor
{
    private DependencyPropertyDescriptor(DependencyProperty property, Type componentType)
        : base(property.Name, null)
    {
        DependencyProperty = property;
        ComponentType = componentType;
    }

    public DependencyProperty DependencyProperty { get; }

    public override Type ComponentType { get; }

    public override bool IsReadOnly => DependencyProperty.IsReadOnly;

    public override Type PropertyType => DependencyProperty.PropertyType;

    /// <summary>Registration metadata (default value plus change/coerce hooks).</summary>
    public PropertyMetadata Metadata => DependencyProperty.Metadata;

    public override bool CanResetValue(object component) => false;

    public override object? GetValue(object? component) =>
        component is DependencyObject target ? target.GetValue(DependencyProperty) : null;

    public override void ResetValue(object component)
    {
    }

    public override void SetValue(object? component, object? value)
    {
        if (component is DependencyObject target)
            target.SetValue(DependencyProperty, value);
    }

    public override bool ShouldSerializeValue(object component) => false;

    /// <summary>Wraps a DependencyProperty (matches FromProperty).</summary>
    public static DependencyPropertyDescriptor? FromProperty(DependencyProperty dependencyProperty, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(dependencyProperty);
        ArgumentNullException.ThrowIfNull(targetType);
        return new DependencyPropertyDescriptor(dependencyProperty, targetType);
    }

    /// <summary>PropertyDescriptor-based lookup has no engine source (precise null).</summary>
    public static DependencyPropertyDescriptor? FromProperty(System.ComponentModel.PropertyDescriptor property) =>
        property is DependencyPropertyDescriptor descriptor ? descriptor : null;

    /// <summary>Finds a "nameProperty" field up the owner hierarchy (matches FromName).</summary>
    public static DependencyPropertyDescriptor? FromName(string name, Type ownerType, Type targetType)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(ownerType);
        ArgumentNullException.ThrowIfNull(targetType);
        var property = DependencyProperty.FromName(ownerType, name);
        return property is null ? null : new DependencyPropertyDescriptor(property, targetType);
    }

    /// <summary>Finds a "nameProperty" field (matches FromName with ignorePropertyType).</summary>
    public static DependencyPropertyDescriptor? FromName(string name, Type ownerType, Type targetType, bool ignorePropertyType) =>
        FromName(name, ownerType, targetType);
}
