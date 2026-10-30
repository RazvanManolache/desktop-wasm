// Slice-86: text-formatting line-pipeline contract
// (matches System.Windows.Media.TextFormatting plus the System.Windows
// typography/layout enums AvalonEdit binds).
//
// Member evidence via Mono.Cecil on the upstream net10.0-windows
// ICSharpCode.AvalonEdit.dll: the editor subclasses TextSource (Simple +
// VisualLine sources), TextRunProperties (Global + VisualLineElement),
// TextParagraphProperties (VisualLine), TextRunTypographyProperties
// (Default) and TextEmbeddedObject (Formatted + InlineObject runs), drives
// lines through TextFormatter.Create(TextFormattingMode).FormatLine with a
// VisualLineTextSource, and consumes TextLine metrics/draw/hit-testing plus
// TextCharacters/TextEndOfParagraph/TextSpan/CultureSpecificCharacterBuffer
// run plumbing. Shapes below mirror the 10.0.13 PresentationCore reference
// member-for-member (abstract/virtual/static verified); virtual defaults
// (BaselineAlignment.Baseline, null NumberSubstitution/TypographyProperties,
// DefaultIncrementalTab 48, PixelsPerDip 1) verified against a live
// net10.0-windows runtime probe. TextFormatter.FormatLine and
// FormatMinMaxParagraphWidth run on a managed backend (per-code-unit advances
// off the shared Skia shaper): converted AvalonEdit line consumers get real
// metrics, bounds, hit-testing and paint. Kerning/ligatures, combining marks,
// right-to-left order, justification and collapsing stay boundaries, like the
// modal DoDragDrop one.
using System.Collections;
using System.Globalization;
using System.Text;

namespace DesktopWasm.Wpf;

/// <summary>Vertical alignment of a run relative to the baseline (matches System.Windows.BaselineAlignment).</summary>
public enum BaselineAlignment
{
    Top = 0,
    Center = 1,
    Bottom = 2,
    Baseline = 3,
    TextTop = 4,
    TextBottom = 5,
    Subscript = 6,
    Superscript = 7,
}

/// <summary>Line-break opportunity next to an embedded object (matches System.Windows.LineBreakCondition).</summary>
public enum LineBreakCondition
{
    BreakDesired = 0,
    BreakPossible = 1,
    BreakRestrained = 2,
    BreakAlways = 3,
}

/// <summary>OpenType capitals variant (matches System.Windows.FontCapitals).</summary>
public enum FontCapitals
{
    Normal = 0,
    AllSmallCaps = 1,
    SmallCaps = 2,
    AllPetiteCaps = 3,
    PetiteCaps = 4,
    Unicase = 5,
    Titling = 6,
}

/// <summary>East Asian language OpenType variant (matches System.Windows.FontEastAsianLanguage).</summary>
public enum FontEastAsianLanguage
{
    Normal = 0,
    Jis78 = 1,
    Jis83 = 2,
    Jis90 = 3,
    Jis04 = 4,
    HojoKanji = 5,
    NlcKanji = 6,
    Simplified = 7,
    Traditional = 8,
    TraditionalNames = 9,
}

/// <summary>East Asian width OpenType variant (matches System.Windows.FontEastAsianWidths).</summary>
public enum FontEastAsianWidths
{
    Normal = 0,
    Proportional = 1,
    Full = 2,
    Half = 3,
    Third = 4,
    Quarter = 5,
}

/// <summary>Fraction OpenType variant (matches System.Windows.FontFraction).</summary>
public enum FontFraction
{
    Normal = 0,
    Slashed = 1,
    Stacked = 2,
}

/// <summary>Numeral alignment OpenType variant (matches System.Windows.FontNumeralAlignment).</summary>
public enum FontNumeralAlignment
{
    Normal = 0,
    Proportional = 1,
    Tabular = 2,
}

/// <summary>Numeral style OpenType variant (matches System.Windows.FontNumeralStyle).</summary>
public enum FontNumeralStyle
{
    Normal = 0,
    Lining = 1,
    OldStyle = 2,
}

/// <summary>Variants OpenType substitution (matches System.Windows.FontVariants).</summary>
public enum FontVariants
{
    Normal = 0,
    Superscript = 1,
    Subscript = 2,
    Ordinal = 3,
    Inferior = 4,
    Ruby = 5,
}

/// <summary>Axis selector for line drawing (matches System.Windows.Media.TextFormatting.InvertAxes).</summary>
public enum InvertAxes
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
    Both = 3,
}

/// <summary>Collapsing style for overlong lines (matches System.Windows.Media.TextFormatting.TextCollapsingStyle).</summary>
public enum TextCollapsingStyle
{
    TrailingCharacter = 0,
    TrailingWord = 1,
}

/// <summary>Tab stop alignment (matches System.Windows.Media.TextFormatting.TextTabAlignment).</summary>
public enum TextTabAlignment
{
    Left = 0,
    Center = 1,
    Right = 2,
    Character = 3,
}

/// <summary>
/// Buffer handle behind character ranges (matches
/// System.Windows.Media.TextFormatting.CharacterBufferReference). Pointer
/// buffers keep offset zero with the pointer length, like WPF.
/// </summary>
public readonly struct CharacterBufferReference : IEquatable<CharacterBufferReference>
{
    private readonly object? _buffer;
    private readonly int _offset;

    public unsafe CharacterBufferReference(char* unsafeCharacterString, int characterLength)
    {
        if (characterLength < 0) throw new ArgumentOutOfRangeException(nameof(characterLength));
        _buffer = (IntPtr)unsafeCharacterString;
        _offset = 0;
    }

    public CharacterBufferReference(char[] characterArray, int offsetToFirstChar)
    {
        ArgumentNullException.ThrowIfNull(characterArray);
        if (offsetToFirstChar < 0 || offsetToFirstChar > characterArray.Length)
            throw new ArgumentOutOfRangeException(nameof(offsetToFirstChar));
        _buffer = characterArray;
        _offset = offsetToFirstChar;
    }

    public CharacterBufferReference(string characterString, int offsetToFirstChar)
    {
        ArgumentNullException.ThrowIfNull(characterString);
        if (offsetToFirstChar < 0 || offsetToFirstChar > characterString.Length)
            throw new ArgumentOutOfRangeException(nameof(offsetToFirstChar));
        _buffer = characterString;
        _offset = offsetToFirstChar;
    }

    public bool Equals(CharacterBufferReference other) =>
        _offset == other._offset && Equals(_buffer, other._buffer);

    public override bool Equals(object? obj) => obj is CharacterBufferReference other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_buffer, _offset);

    public static bool operator ==(CharacterBufferReference left, CharacterBufferReference right) => left.Equals(right);

    public static bool operator !=(CharacterBufferReference left, CharacterBufferReference right) => !left.Equals(right);
}

/// <summary>
/// Character slice over a string/array/pointer buffer (matches
/// System.Windows.Media.TextFormatting.CharacterBufferRange).
/// </summary>
public readonly struct CharacterBufferRange : IEquatable<CharacterBufferRange>
{
    private readonly CharacterBufferReference _reference;
    private readonly int _length;

    public unsafe CharacterBufferRange(char* unsafeCharacterString, int characterLength)
    {
        if (characterLength < 0) throw new ArgumentOutOfRangeException(nameof(characterLength));
        _reference = new CharacterBufferReference(unsafeCharacterString, characterLength);
        _length = characterLength;
    }

    public CharacterBufferRange(char[] characterArray, int offsetToFirstChar, int characterLength)
    {
        ArgumentNullException.ThrowIfNull(characterArray);
        if (offsetToFirstChar < 0 || characterLength < 0 || offsetToFirstChar + characterLength > characterArray.Length)
            throw new ArgumentOutOfRangeException(nameof(offsetToFirstChar));
        _reference = new CharacterBufferReference(characterArray, offsetToFirstChar);
        _length = characterLength;
    }

    public CharacterBufferRange(string characterString, int offsetToFirstChar, int characterLength)
    {
        ArgumentNullException.ThrowIfNull(characterString);
        if (offsetToFirstChar < 0 || characterLength < 0 || offsetToFirstChar + characterLength > characterString.Length)
            throw new ArgumentOutOfRangeException(nameof(offsetToFirstChar));
        _reference = new CharacterBufferReference(characterString, offsetToFirstChar);
        _length = characterLength;
    }

    /// <summary>Empty range (matches Empty).</summary>
    public static CharacterBufferRange Empty => default;

    public CharacterBufferReference CharacterBufferReference => _reference;

    public int Length => _length;

    public bool Equals(CharacterBufferRange other) =>
        _length == other._length && _reference.Equals(other._reference);

    public override bool Equals(object? obj) => obj is CharacterBufferRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_reference, _length);

    public static bool operator ==(CharacterBufferRange left, CharacterBufferRange right) => left.Equals(right);

    public static bool operator !=(CharacterBufferRange left, CharacterBufferRange right) => !left.Equals(right);
}

/// <summary>Hit-test result over a formatted line (matches System.Windows.Media.TextFormatting.CharacterHit).</summary>
public readonly struct CharacterHit : IEquatable<CharacterHit>
{
    public CharacterHit(int firstCharacterIndex, int trailingLength)
    {
        FirstCharacterIndex = firstCharacterIndex;
        TrailingLength = trailingLength;
    }

    public int FirstCharacterIndex { get; }

    public int TrailingLength { get; }

    public bool Equals(CharacterHit other) =>
        FirstCharacterIndex == other.FirstCharacterIndex && TrailingLength == other.TrailingLength;

    public override bool Equals(object? obj) => obj is CharacterHit other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(FirstCharacterIndex, TrailingLength);

    public static bool operator ==(CharacterHit left, CharacterHit right) => left.Equals(right);

    public static bool operator !=(CharacterHit left, CharacterHit right) => !left.Equals(right);
}

/// <summary>
/// Length plus value pair the text source hands back (matches
/// System.Windows.Media.TextFormatting.TextSpan{T}).
/// </summary>
public sealed class TextSpan<T>
{
    public TextSpan(int length, T value)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        Length = length;
        Value = value;
    }

    public int Length { get; }

    public T Value { get; }
}

/// <summary>
/// Culture-tagged character slice (matches
/// System.Windows.Media.TextFormatting.CultureSpecificCharacterBufferRange).
/// </summary>
public sealed class CultureSpecificCharacterBufferRange
{
    public CultureSpecificCharacterBufferRange(CultureInfo culture, CharacterBufferRange characterBufferRange)
    {
        ArgumentNullException.ThrowIfNull(culture);
        CultureInfo = culture;
        CharacterBufferRange = characterBufferRange;
    }

    public CultureInfo CultureInfo { get; }

    public CharacterBufferRange CharacterBufferRange { get; }
}

/// <summary>
/// Break state between formatted lines (matches
/// System.Windows.Media.TextFormatting.TextLineBreak). Instances only come
/// from TextLine.GetTextLineBreak; there is no public constructor, like WPF.
/// </summary>
public sealed class TextLineBreak
{
    internal TextLineBreak()
    {
    }

    /// <summary>Independent copy (matches Clone).</summary>
    public TextLineBreak Clone() => new();

    /// <summary>Releases break state (matches Dispose; no unmanaged state here).</summary>
    public void Dispose()
    {
    }
}

/// <summary>
/// Measured size of an embedded object (matches
/// System.Windows.Media.TextFormatting.TextEmbeddedObjectMetrics).
/// </summary>
public sealed class TextEmbeddedObjectMetrics
{
    public TextEmbeddedObjectMetrics(double width, double height, double baseline)
    {
        Width = width;
        Height = height;
        Baseline = baseline;
    }

    public double Width { get; }

    public double Height { get; }

    public double Baseline { get; }
}

/// <summary>
/// One formatting run from a text source (matches
/// System.Windows.Media.TextFormatting.TextRun).
/// </summary>
public abstract class TextRun
{
    /// <summary>Allows subclassing (matches the WPF family constructor).</summary>
    protected TextRun()
    {
    }

    public abstract CharacterBufferReference CharacterBufferReference { get; }

    public abstract int Length { get; }

    public abstract TextRunProperties Properties { get; }
}

/// <summary>
/// Character run over a string/array/pointer buffer (matches
/// System.Windows.Media.TextFormatting.TextCharacters).
/// </summary>
public class TextCharacters : TextRun
{
    private readonly object? _buffer;
    private readonly int _offset;
    private readonly int _length;
    private readonly TextRunProperties _properties;

    public unsafe TextCharacters(char* unsafeCharacterString, int length, TextRunProperties textRunProperties)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        ArgumentNullException.ThrowIfNull(textRunProperties);
        _buffer = (IntPtr)unsafeCharacterString;
        _offset = 0;
        _length = length;
        _properties = textRunProperties;
    }

    public TextCharacters(char[] characterArray, int offsetToFirstChar, int length, TextRunProperties textRunProperties)
    {
        ArgumentNullException.ThrowIfNull(characterArray);
        if (offsetToFirstChar < 0 || length < 0 || offsetToFirstChar + length > characterArray.Length)
            throw new ArgumentOutOfRangeException(nameof(offsetToFirstChar));
        ArgumentNullException.ThrowIfNull(textRunProperties);
        _buffer = characterArray;
        _offset = offsetToFirstChar;
        _length = length;
        _properties = textRunProperties;
    }

    public TextCharacters(string characterString, int offsetToFirstChar, int length, TextRunProperties textRunProperties)
    {
        ArgumentNullException.ThrowIfNull(characterString);
        if (offsetToFirstChar < 0 || length < 0 || offsetToFirstChar + length > characterString.Length)
            throw new ArgumentOutOfRangeException(nameof(offsetToFirstChar));
        ArgumentNullException.ThrowIfNull(textRunProperties);
        _buffer = characterString;
        _offset = offsetToFirstChar;
        _length = length;
        _properties = textRunProperties;
    }

    public TextCharacters(string characterString, TextRunProperties textRunProperties)
    {
        ArgumentNullException.ThrowIfNull(characterString);
        ArgumentNullException.ThrowIfNull(textRunProperties);
        _buffer = characterString;
        _offset = 0;
        _length = characterString.Length;
        _properties = textRunProperties;
    }

    public override CharacterBufferReference CharacterBufferReference => _buffer switch
    {
        string s => new CharacterBufferReference(s, _offset),
        char[] a => new CharacterBufferReference(a, _offset),
        _ => default,
    };

    /// <summary>
    /// Reads the run characters for the managed line formatter (string, array
    /// and pointer buffers; the formatter cannot shape what it cannot read).
    /// </summary>
    internal unsafe string GetText() => _buffer switch
    {
        string s => s.Substring(_offset, _length),
        char[] a => new string(a, _offset, _length),
        IntPtr p => new string((char*)p + _offset, 0, _length),
        _ => throw new NotSupportedException($"TextFormatter cannot read {(_buffer?.GetType().FullName ?? "null")} character buffers."),
    };

    public override int Length => _length;

    public override TextRunProperties Properties => _properties;
}

/// <summary>
/// End-of-line run (matches System.Windows.Media.TextFormatting.TextEndOfLine).
/// </summary>
public class TextEndOfLine : TextRun
{
    public TextEndOfLine(int length)
        : this(length, null!)
    {
    }

    public TextEndOfLine(int length, TextRunProperties textRunProperties)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        Length = length;
        Properties = textRunProperties;
    }

    public override CharacterBufferReference CharacterBufferReference => default;

    public override int Length { get; }

    public override TextRunProperties Properties { get; }
}

/// <summary>
/// End-of-paragraph run (matches
/// System.Windows.Media.TextFormatting.TextEndOfParagraph).
/// </summary>
public class TextEndOfParagraph : TextEndOfLine
{
    public TextEndOfParagraph(int length)
        : base(length)
    {
    }

    public TextEndOfParagraph(int length, TextRunProperties? textRunProperties)
        : base(length, textRunProperties)
    {
    }
}

/// <summary>
/// Inline object run (matches
/// System.Windows.Media.TextFormatting.TextEmbeddedObject). AvalonEdit's
/// FormattedText and inline-UI runs subclass this shape.
/// </summary>
public abstract class TextEmbeddedObject : TextRun
{
    /// <summary>Allows subclassing (matches the WPF family constructor).</summary>
    protected TextEmbeddedObject()
    {
    }

    public abstract LineBreakCondition BreakBefore { get; }

    public abstract LineBreakCondition BreakAfter { get; }

    public abstract bool HasFixedSize { get; }

    public abstract Rect ComputeBoundingBox(bool rightToLeft, bool sideways);

    public abstract void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways);

    public abstract TextEmbeddedObjectMetrics Format(double remainingParagraphWidth);
}

/// <summary>
/// Per-run rendering properties (matches
/// System.Windows.Media.TextFormatting.TextRunProperties). AvalonEdit's
/// Global and VisualLineElement properties classes subclass this shape.
/// Virtual defaults verified against a live net10.0-windows probe.
/// </summary>
public abstract class TextRunProperties
{
    /// <summary>Allows subclassing (matches the WPF family constructor).</summary>
    protected TextRunProperties()
    {
    }

    public abstract Brush BackgroundBrush { get; }

    public abstract CultureInfo CultureInfo { get; }

    public abstract double FontHintingEmSize { get; }

    public abstract double FontRenderingEmSize { get; }

    public abstract Brush ForegroundBrush { get; }

    public abstract TextDecorationCollection TextDecorations { get; }

    public abstract TextEffectCollection TextEffects { get; }

    public abstract Typeface Typeface { get; }

    /// <summary>Baseline alignment (virtual default Baseline, like WPF).</summary>
    public virtual BaselineAlignment BaselineAlignment => BaselineAlignment.Baseline;

    /// <summary>Number substitution (virtual default null, like WPF).</summary>
    public virtual NumberSubstitution? NumberSubstitution => null;

    /// <summary>Typography properties (virtual default null, like WPF).</summary>
    public virtual TextRunTypographyProperties? TypographyProperties => null;

    /// <summary>Resolution for text rendering (matches PixelsPerDip; default 1, like WPF).</summary>
    public double PixelsPerDip { get; set; } = 1.0;
}

/// <summary>
/// Per-paragraph formatting properties (matches
/// System.Windows.Media.TextFormatting.TextParagraphProperties).
/// AvalonEdit's VisualLineTextParagraphProperties subclasses this shape.
/// Virtual defaults verified against a live net10.0-windows probe.
/// </summary>
public abstract class TextParagraphProperties
{
    /// <summary>Allows subclassing (matches the WPF family constructor).</summary>
    protected TextParagraphProperties()
    {
    }

    public abstract TextRunProperties DefaultTextRunProperties { get; }

    public abstract bool FirstLineInParagraph { get; }

    public abstract FlowDirection FlowDirection { get; }

    public abstract double Indent { get; }

    public abstract double LineHeight { get; }

    public abstract TextAlignment TextAlignment { get; }

    public abstract TextMarkerProperties TextMarkerProperties { get; }

    public abstract TextWrapping TextWrapping { get; }

    /// <summary>Whether the paragraph stays collapsible (virtual default false, like WPF).</summary>
    public virtual bool AlwaysCollapsible => false;

    /// <summary>Incremental tab width (virtual default 48, like WPF).</summary>
    public virtual double DefaultIncrementalTab => 48.0;

    /// <summary>First-line indent delta (virtual default 0, like WPF).</summary>
    public virtual double ParagraphIndent => 0.0;

    /// <summary>Tab stops (virtual default null, like WPF).</summary>
    public virtual IList<TextTabProperties>? Tabs => null;

    /// <summary>Paragraph decorations (virtual default null, like WPF).</summary>
    public virtual TextDecorationCollection? TextDecorations => null;
}

/// <summary>
/// Marker properties for list paragraphs (matches
/// System.Windows.Media.TextFormatting.TextMarkerProperties).
/// </summary>
public abstract class TextMarkerProperties
{
    /// <summary>Allows subclassing (matches the WPF family constructor).</summary>
    protected TextMarkerProperties()
    {
    }

    public abstract double Offset { get; }

    public abstract TextSource TextSource { get; }
}

/// <summary>
/// OpenType typography properties (matches
/// System.Windows.Media.TextFormatting.TextRunTypographyProperties).
/// AvalonEdit's DefaultTextRunTypographyProperties overrides every member.
/// </summary>
public abstract class TextRunTypographyProperties
{
    /// <summary>Allows subclassing (matches the WPF family constructor).</summary>
    protected TextRunTypographyProperties()
    {
    }

    public abstract int AnnotationAlternates { get; }

    public abstract FontCapitals Capitals { get; }

    public abstract bool CapitalSpacing { get; }

    public abstract bool CaseSensitiveForms { get; }

    public abstract bool ContextualAlternates { get; }

    public abstract bool ContextualLigatures { get; }

    public abstract int ContextualSwashes { get; }

    public abstract bool DiscretionaryLigatures { get; }

    public abstract bool EastAsianExpertForms { get; }

    public abstract FontEastAsianLanguage EastAsianLanguage { get; }

    public abstract FontEastAsianWidths EastAsianWidths { get; }

    public abstract FontFraction Fraction { get; }

    public abstract bool HistoricalForms { get; }

    public abstract bool HistoricalLigatures { get; }

    public abstract bool Kerning { get; }

    public abstract bool MathematicalGreek { get; }

    public abstract FontNumeralAlignment NumeralAlignment { get; }

    public abstract FontNumeralStyle NumeralStyle { get; }

    public abstract bool SlashedZero { get; }

    public abstract bool StandardLigatures { get; }

    public abstract int StandardSwashes { get; }

    public abstract int StylisticAlternates { get; }

    public abstract bool StylisticSet1 { get; }

    public abstract bool StylisticSet2 { get; }

    public abstract bool StylisticSet3 { get; }

    public abstract bool StylisticSet4 { get; }

    public abstract bool StylisticSet5 { get; }

    public abstract bool StylisticSet6 { get; }

    public abstract bool StylisticSet7 { get; }

    public abstract bool StylisticSet8 { get; }

    public abstract bool StylisticSet9 { get; }

    public abstract bool StylisticSet10 { get; }

    public abstract bool StylisticSet11 { get; }

    public abstract bool StylisticSet12 { get; }

    public abstract bool StylisticSet13 { get; }

    public abstract bool StylisticSet14 { get; }

    public abstract bool StylisticSet15 { get; }

    public abstract bool StylisticSet16 { get; }

    public abstract bool StylisticSet17 { get; }

    public abstract bool StylisticSet18 { get; }

    public abstract bool StylisticSet19 { get; }

    public abstract bool StylisticSet20 { get; }

    public abstract FontVariants Variants { get; }
}

/// <summary>
/// Run provider behind formatted lines (matches
/// System.Windows.Media.TextFormatting.TextSource). AvalonEdit's Simple and
/// VisualLine sources subclass this shape.
/// </summary>
public abstract class TextSource
{
    /// <summary>Allows subclassing (matches the WPF family constructor).</summary>
    protected TextSource()
    {
    }

    /// <summary>Resolution callers see (matches PixelsPerDip; default 1, like WPF).</summary>
    public double PixelsPerDip { get; set; } = 1.0;

    public abstract TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndex);

    public abstract int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex);

    public abstract TextRun GetTextRun(int textSourceCharacterIndex);
}

/// <summary>
/// Line formatter entry point (matches
/// System.Windows.Media.TextFormatting.TextFormatter). Creation and
/// disposal are real, and line layout runs on a managed backend off the
/// shared Skia shaper (see the formatter class below for the exact limits).
/// </summary>
public abstract class TextFormatter
{
    /// <summary>Allows subclassing (matches the WPF family constructor).</summary>
    protected TextFormatter()
    {
    }

    /// <summary>Creates a formatter with ideal metrics (matches Create).</summary>
    public static TextFormatter Create() => new ManagedTextFormatter(TextFormattingMode.Ideal);

    /// <summary>Creates a formatter with the requested mode (matches Create).</summary>
    public static TextFormatter Create(TextFormattingMode textFormattingMode) => new ManagedTextFormatter(textFormattingMode);

    /// <summary>Releases formatter state (matches Dispose).</summary>
    public virtual void Dispose()
    {
    }

    public abstract TextLine FormatLine(
        TextSource textSource,
        int firstCharIndex,
        double paragraphWidth,
        TextParagraphProperties paragraphProperties,
        TextLineBreak? previousLineBreak);

    public abstract TextLine FormatLine(
        TextSource textSource,
        int firstCharIndex,
        double paragraphWidth,
        TextParagraphProperties paragraphProperties,
        TextLineBreak? previousLineBreak,
        TextRunCache? textRunCache);

    public abstract MinMaxParagraphWidth FormatMinMaxParagraphWidth(
        TextSource textSource,
        int firstCharIndex,
        TextParagraphProperties paragraphProperties);

    public abstract MinMaxParagraphWidth FormatMinMaxParagraphWidth(
        TextSource textSource,
        int firstCharIndex,
        TextParagraphProperties paragraphProperties,
        TextRunCache? textRunCache);

    /// <summary>
    /// Managed line formatter (matches TextFormatter.Create/Dispose): layout runs
    /// on a managed backend off the shared Skia shaper — per-code-unit advances
    /// measured off the resolved native face, so metrics and paint agree with
    /// FormattedText. Latin text measures exactly (the AvalonEdit consumer is
    /// monospace); proportional runs skip kerning/ligature shaping, tabs honor
    /// incremental and left stops, first-line and paragraph indents apply,
    /// right-to-left runs lay out left-to-right, and decorations/effects stay
    /// unpainted. Unknown run shapes fail loud instead of shaping silently.
    /// </summary>
    private sealed class ManagedTextFormatter : TextFormatter
    {
        internal ManagedTextFormatter(TextFormattingMode mode)
        {
            Mode = mode;
        }

        internal TextFormattingMode Mode { get; }

        public override TextLine FormatLine(
            TextSource textSource,
            int firstCharIndex,
            double paragraphWidth,
            TextParagraphProperties paragraphProperties,
            TextLineBreak? previousLineBreak) =>
            ManagedTextLine.Format(textSource, firstCharIndex, paragraphWidth, paragraphProperties, Mode);

        public override TextLine FormatLine(
            TextSource textSource,
            int firstCharIndex,
            double paragraphWidth,
            TextParagraphProperties paragraphProperties,
            TextLineBreak? previousLineBreak,
            TextRunCache? textRunCache) =>
            ManagedTextLine.Format(textSource, firstCharIndex, paragraphWidth, paragraphProperties, Mode);

        public override MinMaxParagraphWidth FormatMinMaxParagraphWidth(
            TextSource textSource,
            int firstCharIndex,
            TextParagraphProperties paragraphProperties) =>
            ManagedTextLine.MeasureParagraph(textSource, firstCharIndex, paragraphProperties, Mode);

        public override MinMaxParagraphWidth FormatMinMaxParagraphWidth(
            TextSource textSource,
            int firstCharIndex,
            TextParagraphProperties paragraphProperties,
            TextRunCache? textRunCache) =>
            ManagedTextLine.MeasureParagraph(textSource, firstCharIndex, paragraphProperties, Mode);
    }

    /// <summary>One laid-out source run: absolute x, per-character advances and font box.</summary>
    private sealed class RunSlice
    {
        internal RunSlice(TextRun run, int absoluteStart, int sliceLength, string text, double x, double[] advances, double emSize, double height, double baseline)
        {
            Run = run;
            AbsoluteStart = absoluteStart;
            SliceLength = sliceLength;
            Text = text;
            X = x;
            Advances = advances;
            EmSize = emSize;
            Height = height;
            Baseline = baseline;
        }

        internal TextRun Run { get; }

        internal int AbsoluteStart { get; }

        internal int SliceLength { get; }

        internal string Text { get; }

        internal double X { get; }

        internal double[] Advances { get; }

        internal double EmSize { get; }

        internal double Height { get; }

        internal double Baseline { get; }

        internal double Width
        {
            get
            {
                var width = 0.0;
                foreach (var advance in Advances) width += advance;
                return width;
            }
        }
    }
    /// <summary>
    /// Managed formatted line: greedy word-wrap at spaces/tabs against the
    /// paragraph width, per-code-unit advances off the shared shaper, font boxes
    /// from the resolved native face, indent/start accounting, trailing-space
    /// tracking, and replay paint through FormattedText plus embedded-object
    /// runs. Latin and monospace text lays out exactly; kerning, ligatures,
    /// combining marks, right-to-left order, justification, collapsing and
    /// decorations stay boundaries (documented on the formatter).
    /// </summary>
    private sealed class ManagedTextLine : TextLine
    {
        private static readonly Typeface DefaultTypeface = new("Segoe UI");

        private readonly List<RunSlice> _runs;
        private readonly List<double> _charX;
        private readonly string _fullText;
        private readonly int _contentLength;
        private readonly FlowDirection _flow;
        private readonly double _paragraphWidth;
        private readonly TextAlignment _alignment;
        private readonly bool _paragraphEnd;
        private readonly double _longestWord;

        private ManagedTextLine(
            List<RunSlice> runs, List<double> charX, string fullText, int contentLength,
            int length, int newlineLength, int trailingWhitespaceLength,
            double width, double widthIncludingTrailingWhitespace,
            double height, double textHeight, double baseline, double start,
            bool hasOverflowed, bool paragraphEnd, double longestWord,
            FlowDirection flow, double paragraphWidth, TextAlignment alignment)
        {
            _runs = runs;
            _charX = charX;
            _fullText = fullText;
            _contentLength = contentLength;
            Length = length;
            NewlineLength = newlineLength;
            TrailingWhitespaceLength = trailingWhitespaceLength;
            Width = width;
            WidthIncludingTrailingWhitespace = widthIncludingTrailingWhitespace;
            Height = height;
            TextHeight = textHeight;
            Baseline = baseline;
            TextBaseline = baseline;
            Start = start;
            HasOverflowed = hasOverflowed;
            _paragraphEnd = paragraphEnd;
            _longestWord = longestWord;
            _flow = flow;
            _paragraphWidth = paragraphWidth;
            _alignment = alignment;
        }

        internal static ManagedTextLine Format(
            TextSource textSource,
            int firstCharIndex,
            double paragraphWidth,
            TextParagraphProperties paragraphProperties,
            TextFormattingMode mode)
        {
            ArgumentNullException.ThrowIfNull(textSource);
            ArgumentNullException.ThrowIfNull(paragraphProperties);
            if (firstCharIndex < 0) throw new ArgumentOutOfRangeException(nameof(firstCharIndex));
            var dip = textSource.PixelsPerDip > 0 ? textSource.PixelsPerDip : 1.0;
            var indent = paragraphProperties.Indent +
                (paragraphProperties.FirstLineInParagraph ? paragraphProperties.ParagraphIndent : 0);
            if (double.IsNaN(indent) || double.IsInfinity(indent) || indent < 0) indent = 0;
            var budget = double.IsNaN(paragraphWidth) || double.IsInfinity(paragraphWidth)
                ? double.PositiveInfinity
                : Math.Max(0, paragraphWidth);
            var wrap = paragraphProperties.TextWrapping;
            var defaultProps = paragraphProperties.DefaultTextRunProperties;
            SeedMetrics(defaultProps, dip, out var height, out var baseline);
            var runs = new List<RunSlice>();
            var charX = new List<double>();
            var full = new StringBuilder();
            var index = firstCharIndex;
            var x = indent;
            var consumed = 0;
            var newlineLength = 0;
            var paragraphEnd = false;
            var overflowed = false;
            var longestWord = 0.0;
            var wordWidth = 0.0;
            var breakOpportunities = new List<int>();
            var guard = 0;
            var done = false;
            while (!done)
            {
                if (guard++ > 100000) break;
                var run = textSource.GetTextRun(index);
                if (run is null || run.Length <= 0) { paragraphEnd = true; break; }
                switch (run)
                {
                    case TextCharacters chars:
                        ConsumeCharacters(chars, ref index, ref x, ref consumed, full, charX, runs,
                            breakOpportunities, budget, wrap, paragraphProperties, dip, firstCharIndex,
                            ref newlineLength, ref done, ref height, ref baseline,
                            ref wordWidth, ref longestWord);
                        break;
                    case TextEmbeddedObject embedded:
                        ConsumeEmbedded(embedded, run.Length, ref index, ref x, ref consumed, full, charX, runs,
                            breakOpportunities, budget, wrap, paragraphProperties, dip, firstCharIndex,
                            ref done, ref height, ref baseline, ref wordWidth, ref longestWord);
                        if (embedded.BreakAfter == LineBreakCondition.BreakAlways && !done) { done = true; }
                        break;
                    case TextEndOfLine eol:
                        full.Append('\n', run.Length);
                        for (var pad = 0; pad < run.Length; pad++) charX.Add(x);
                        index += run.Length;
                        consumed += run.Length;
                        newlineLength = run.Length;
                        paragraphEnd = run is TextEndOfParagraph;
                        done = true;
                        break;
                    default:
                        throw new NotSupportedException($"TextFormatter cannot lay out {run.GetType().FullName} runs.");
                }
                if (done) break;
            }
            return Finish(firstCharIndex, runs, charX, full.ToString(), consumed, newlineLength,
                x, indent, height, baseline, paragraphProperties, overflowed || x > budget,
                paragraphEnd, longestWord, budget, mode);
        }
        private static void ConsumeCharacters(
            TextCharacters chars, ref int index, ref double x, ref int consumed,
            StringBuilder full, List<double> charX, List<RunSlice> runs,
            List<int> breakOpportunities, double budget, TextWrapping wrap,
            TextParagraphProperties paragraphProperties, double dip, int firstCharIndex,
            ref int newlineLength, ref bool done, ref double height, ref double baseline,
            ref double wordWidth, ref double longestWord)
        {
            var text = chars.GetText();
            var props = chars.Properties;
            var emSize = EmSizeOf(props);
            MeasureRun(props, emSize, dip, out var runHeight, out var runBaseline);
            height = Math.Max(height, runHeight);
            baseline = Math.Max(baseline, runBaseline);
            var advances = new List<double>(text.Length);
            var runStart = consumed;
            var runX = x;
            var i = 0;
            while (i < text.Length)
            {
                var ch = text[i];
                if (ch == '\r' || ch == '\n')
                {
                    var nl = ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                    FlushSlice(chars, firstCharIndex + runStart, consumed - runStart,
                        text.Substring(0, i), runs, advances, runX, emSize, runHeight, runBaseline);
                    full.Append(text, i, nl);
                    for (var pad = 0; pad < nl; pad++) charX.Add(x);
                    index += nl;
                    consumed += nl;
                    newlineLength = nl;
                    done = true;
                    if (wordWidth > longestWord) longestWord = wordWidth;
                    return;
                }
                var w = ch == '\t' ? TabWidth(x, paragraphProperties) : CharWidth(ch, props, emSize, dip);
                if (ch != '\t' && wrap != TextWrapping.NoWrap && x + w > budget && consumed > 0)
                {
                    var opportunity = -1;
                    for (var k = breakOpportunities.Count - 1; k >= 0; k--)
                    {
                        if (breakOpportunities[k] < consumed) { opportunity = breakOpportunities[k]; break; }
                    }
                    FlushSlice(chars, firstCharIndex + runStart, consumed - runStart,
                        text.Substring(0, i), runs, advances, runX, emSize, runHeight, runBaseline);
                    if (opportunity > 0)
                        RewindTo(opportunity, runs, charX, full, breakOpportunities, firstCharIndex, ref index, ref x, ref consumed);
                    if (wordWidth > longestWord) longestWord = wordWidth;
                    wordWidth = 0;
                    newlineLength = 0;
                    done = true;
                    return;
                }
                charX.Add(x);
                advances.Add(w);
                full.Append(ch);
                x += w;
                index++;
                consumed++;
                i++;
                if (ch == ' ' || ch == '\t')
                {
                    breakOpportunities.Add(consumed);
                    if (wordWidth > longestWord) longestWord = wordWidth;
                    wordWidth = 0;
                }
                else
                {
                    wordWidth += w;
                }
            }
            FlushSlice(chars, firstCharIndex + runStart, consumed - runStart,
                text, runs, advances, runX, emSize, runHeight, runBaseline);
            if (wordWidth > longestWord) longestWord = wordWidth;
        }

        private static void ConsumeEmbedded(
            TextEmbeddedObject embedded, int runLength, ref int index, ref double x, ref int consumed,
            StringBuilder full, List<double> charX, List<RunSlice> runs,
            List<int> breakOpportunities, double budget, TextWrapping wrap,
            TextParagraphProperties paragraphProperties, double dip, int firstCharIndex,
            ref bool done, ref double height, ref double baseline,
            ref double wordWidth, ref double longestWord)
        {
            if (embedded.BreakBefore == LineBreakCondition.BreakAlways && consumed > 0)
            {
                if (wordWidth > longestWord) longestWord = wordWidth;
                wordWidth = 0;
                return;
            }
            var remaining = double.IsPositiveInfinity(budget - x) ? double.PositiveInfinity : Math.Max(0, budget - x);
            var metrics = embedded.Format(remaining);
            var w = Math.Max(0, metrics.Width);
            if (wrap != TextWrapping.NoWrap && x + w > budget && consumed > 0 &&
                embedded.BreakBefore != LineBreakCondition.BreakDesired &&
                embedded.BreakBefore != LineBreakCondition.BreakPossible)
            {
                var opportunity = -1;
                for (var k = breakOpportunities.Count - 1; k >= 0; k--)
                {
                    if (breakOpportunities[k] < consumed) { opportunity = breakOpportunities[k]; break; }
                }
                if (opportunity > 0)
                    RewindTo(opportunity, runs, charX, full, breakOpportunities, firstCharIndex, ref index, ref x, ref consumed);
                if (wordWidth > longestWord) longestWord = wordWidth;
                wordWidth = 0;
                done = true;
                return;
            }
            runs.Add(new RunSlice(embedded, firstCharIndex + consumed, runLength, string.Empty,
                x, new[] { w }, 0, metrics.Height, metrics.Baseline));
            for (var pad = 0; pad < runLength; pad++) { charX.Add(x); full.Append('\0'); }
            height = Math.Max(height, metrics.Height);
            baseline = Math.Max(baseline, metrics.Baseline);
            x += w;
            index += runLength;
            consumed += runLength;
            wordWidth += w;
            if (embedded.BreakAfter == LineBreakCondition.BreakDesired ||
                embedded.BreakAfter == LineBreakCondition.BreakPossible)
                breakOpportunities.Add(consumed);
        }

        private static void FlushSlice(
            TextRun run, int absoluteStart, int sliceLength, string text,
            List<RunSlice> runs, List<double> advances, double runX,
            double emSize, double runHeight, double runBaseline)
        {
            if (sliceLength <= 0) return;
            runs.Add(new RunSlice(run, absoluteStart, sliceLength, text, runX,
                advances.ToArray(), emSize, runHeight, runBaseline));
        }
        private static void RewindTo(
            int opportunity, List<RunSlice> runs, List<double> charX, StringBuilder full,
            List<int> breakOpportunities, int firstCharIndex,
            ref int index, ref double x, ref int consumed)
        {
            while (runs.Count > 0)
            {
                var last = runs[runs.Count - 1];
                var lastEnd = last.AbsoluteStart + last.SliceLength - firstCharIndex;
                if (lastEnd <= opportunity) break;
                runs.RemoveAt(runs.Count - 1);
                var keep = opportunity - (last.AbsoluteStart - firstCharIndex);
                if (keep > 0)
                {
                    var kept = new double[keep];
                    Array.Copy(last.Advances, kept, keep);
                    runs.Add(new RunSlice(last.Run, last.AbsoluteStart, keep,
                        last.Text.Substring(0, Math.Min(keep, last.Text.Length)),
                        last.X, kept, last.EmSize, last.Height, last.Baseline));
                }
            }
            while (charX.Count > opportunity) charX.RemoveAt(charX.Count - 1);
            full.Length = opportunity;
            while (breakOpportunities.Count > 0 && breakOpportunities[breakOpportunities.Count - 1] > opportunity)
                breakOpportunities.RemoveAt(breakOpportunities.Count - 1);
            consumed = opportunity;
            index = firstCharIndex + opportunity;
            x = 0;
            foreach (var slice in runs) x += slice.Width;
        }

        private static double TabWidth(double x, TextParagraphProperties paragraphProperties)
        {
            var stops = paragraphProperties.Tabs;
            if (stops is not null)
            {
                foreach (var tab in stops)
                {
                    if (tab is null) continue;
                    if (tab.Alignment == TextTabAlignment.Left && tab.Location > x)
                        return tab.Location - x;
                }
            }
            var incremental = paragraphProperties.DefaultIncrementalTab;
            if (!(incremental > 0)) incremental = 48.0;
            var offset = x % incremental;
            var width = incremental - offset;
            return width <= 0 ? incremental : width;
        }

        private static double EmSizeOf(TextRunProperties props)
        {
            var emSize = props.FontRenderingEmSize;
            return emSize > 0 ? emSize : 12.0;
        }

        private static double CharWidth(char ch, TextRunProperties props, double emSize, double dip)
        {
            var face = props.Typeface ?? DefaultTypeface;
            var spec = new DesktopWasm.Graphics.FontSpec((float)(emSize * dip), face.PrimaryFamilyName);
            return DesktopWasm.Graphics.TextShaper.Shape(ch.ToString(), spec).Width / dip;
        }

        private static void MeasureRun(TextRunProperties props, double emSize, double dip, out double height, out double baseline)
        {
            height = 0;
            baseline = 0;
            var face = props.Typeface;
            if (face is not null && face.TryGetGlyphTypeface(out var glyphs) && glyphs is not null)
            {
                height = emSize * glyphs.Height;
                baseline = emSize * glyphs.Baseline;
            }
            if (!(height > 0))
            {
                height = emSize * 1.2;
                baseline = emSize * 0.9;
            }
        }

        private static void SeedMetrics(TextRunProperties? props, double dip, out double height, out double baseline)
        {
            if (props is null)
            {
                height = 12.0 * 1.2;
                baseline = 12.0 * 0.9;
                return;
            }
            MeasureRun(props, EmSizeOf(props), dip, out height, out baseline);
        }

        private static ManagedTextLine Finish(
            int firstCharIndex, List<RunSlice> runs, List<double> charX, string fullText,
            int consumed, int newlineLength, double endX, double indent,
            double height, double baseline, TextParagraphProperties paragraphProperties,
            bool overflowed, bool paragraphEnd, double longestWord, double budget, TextFormattingMode mode)
        {
            var contentEnd = consumed - newlineLength;
            var trailing = 0;
            var width = endX;
            for (var k = contentEnd - 1; k >= 0; k--)
            {
                var ch = k < fullText.Length ? fullText[k] : '\0';
                if (ch != ' ' && ch != '\t') break;
                trailing++;
            }
            if (trailing > 0 && trailing < contentEnd)
            {
                width = charX[contentEnd - trailing];
            }
            else if (trailing >= contentEnd && contentEnd > 0)
            {
                width = indent;
            }
            if (!double.IsNaN(paragraphProperties.LineHeight) && paragraphProperties.LineHeight > 0)
                height = Math.Max(height, paragraphProperties.LineHeight);
            return new ManagedTextLine(runs, charX, fullText, contentEnd,
                consumed, newlineLength, trailing, width, endX,
                height, height, baseline, indent, overflowed, paragraphEnd, longestWord,
                paragraphProperties.FlowDirection, budget, paragraphProperties.TextAlignment);
        }
        internal static MinMaxParagraphWidth MeasureParagraph(
            TextSource textSource,
            int firstCharIndex,
            TextParagraphProperties paragraphProperties,
            TextFormattingMode mode)
        {
            ArgumentNullException.ThrowIfNull(textSource);
            ArgumentNullException.ThrowIfNull(paragraphProperties);
            if (firstCharIndex < 0) throw new ArgumentOutOfRangeException(nameof(firstCharIndex));
            var minWidth = 0.0;
            var maxWidth = 0.0;
            var index = firstCharIndex;
            var guard = 0;
            while (guard++ < 100000)
            {
                var line = Format(textSource, index, double.PositiveInfinity, paragraphProperties, mode);
                if (line._longestWord > minWidth) minWidth = line._longestWord;
                if (line.WidthIncludingTrailingWhitespace > maxWidth) maxWidth = line.WidthIncludingTrailingWhitespace;
                if (line.Length <= 0 || line._paragraphEnd) break;
                index += line.Length;
            }
            return new MinMaxParagraphWidth(minWidth, maxWidth);
        }

        internal bool ParagraphEnd => _paragraphEnd;

        internal double LongestWord => _longestWord;

        public override double Baseline { get; }

        public override int DependentLength => 0;

        public override double Extent => Width;

        public override bool HasCollapsed => false;

        public override bool HasOverflowed { get; }

        public override double Height { get; }

        public override int Length { get; }

        public override double MarkerBaseline => 0;

        public override double MarkerHeight => 0;

        public override int NewlineLength { get; }

        public override double OverhangAfter => 0;

        public override double OverhangLeading => 0;

        public override double OverhangTrailing => 0;

        public override double Start { get; }

        public override double TextBaseline { get; }

        public override double TextHeight { get; }

        public override int TrailingWhitespaceLength { get; }

        public override double Width { get; }

        public override double WidthIncludingTrailingWhitespace { get; }

        public override TextLine Collapse(TextCollapsingProperties[] collapsingPropertiesList) =>
            throw new NotSupportedException("TextLine.Collapse needs a collapsing backend.");

        public override void Dispose()
        {
        }

        public override void Draw(DrawingContext drawingContext, Point origin, InvertAxes inversion)
        {
            ArgumentNullException.ThrowIfNull(drawingContext);
            if (inversion != InvertAxes.None)
                throw new NotSupportedException($"TextLine.Draw with {inversion} inversion needs an axis-flip backend.");
            var dx = 0.0;
            if (!double.IsPositiveInfinity(_paragraphWidth) && !double.IsNaN(_paragraphWidth))
            {
                dx = _alignment switch
                {
                    TextAlignment.Center => Math.Max(0, (_paragraphWidth - Width) / 2),
                    TextAlignment.Right => Math.Max(0, _paragraphWidth - Width),
                    _ => 0.0,
                };
            }
            foreach (var slice in _runs)
            {
                if (slice.Run is TextCharacters && slice.Text.Length > 0)
                {
                    var props = slice.Run.Properties;
                    var face = props.Typeface ?? DefaultTypeface;
                    var foreground = props.ForegroundBrush ?? Brushes.Black;
                    var culture = props.CultureInfo ?? CultureInfo.InvariantCulture;
                    var formatted = new FormattedText(slice.Text, culture, _flow, face,
                        slice.EmSize, foreground, props.NumberSubstitution);
                    var runX = origin.X + dx + slice.X;
                    if (props.BackgroundBrush is Brush background)
                        drawingContext.DrawRectangle(background, null, new Rect(runX, origin.Y, slice.Width, Height));
                    drawingContext.DrawText(formatted, new Point(runX, origin.Y + (Baseline - slice.Baseline)));
                }
                else if (slice.Run is TextEmbeddedObject embedded)
                {
                    embedded.Draw(drawingContext,
                        new Point(origin.X + dx + slice.X, origin.Y + (Baseline - slice.Baseline)), false, false);
                }
            }
        }
        public override CharacterHit GetBackspaceCaretCharacterHit(CharacterHit characterHit)
        {
            var caret = Math.Max(0, characterHit.FirstCharacterIndex + characterHit.TrailingLength);
            if (caret > _contentLength) caret = _contentLength;
            if (caret == 0) return new CharacterHit(0, 0);
            var start = caret - 1;
            var length = 1;
            if (start < _fullText.Length && _fullText[start] == '\n' && start > 0 &&
                _fullText[start - 1] == '\r')
            {
                start--;
                length = 2;
            }
            else if (start < _fullText.Length && char.IsLowSurrogate(_fullText[start]) && start > 0 &&
                char.IsHighSurrogate(_fullText[start - 1]))
            {
                start--;
                length = 2;
            }
            return new CharacterHit(start, length);
        }

        public override CharacterHit GetCharacterHitFromDistance(double distance)
        {
            if (_contentLength == 0) return new CharacterHit(0, 0);
            if (distance <= 0) return new CharacterHit(0, 0);
            for (var i = 0; i < _contentLength && i < _charX.Count; i++)
            {
                var w = i + 1 < _charX.Count ? _charX[i + 1] - _charX[i]
                    : Math.Max(0, WidthIncludingTrailingWhitespace - _charX[i]);
                if (distance < _charX[i] + w / 2) return new CharacterHit(i, 0);
                if (distance < _charX[i] + w) return new CharacterHit(i, 1);
            }
            return new CharacterHit(_contentLength, 0);
        }

        public override double GetDistanceFromCharacterHit(CharacterHit characterHit)
        {
            var index = Math.Max(0, characterHit.FirstCharacterIndex);
            if (index > _contentLength) index = _contentLength;
            var x = index < _charX.Count ? _charX[index] : WidthIncludingTrailingWhitespace;
            if (characterHit.TrailingLength > 0 && index < _contentLength)
            {
                var w = index + 1 < _charX.Count ? _charX[index + 1] - _charX[index]
                    : Math.Max(0, WidthIncludingTrailingWhitespace - x);
                x += Math.Max(0, w);
            }
            return x;
        }

        public override IEnumerable<IndexedGlyphRun> GetIndexedGlyphRuns()
        {
            var runs = new List<IndexedGlyphRun>();
            foreach (var slice in _runs)
            {
                if (slice.Run is not TextCharacters || slice.Text.Length == 0) continue;
                var props = slice.Run.Properties;
                var face = props.Typeface ?? DefaultTypeface;
                var fontCss = $"{slice.EmSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}px {face.PrimaryFamilyName}";
                runs.Add(new IndexedGlyphRun(
                    new GlyphRun(slice.Text, fontCss, new WpfPoint(slice.X, 0)),
                    slice.AbsoluteStart, slice.SliceLength));
            }
            return runs;
        }

        public override CharacterHit GetNextCaretCharacterHit(CharacterHit characterHit)
        {
            var caret = characterHit.FirstCharacterIndex + characterHit.TrailingLength;
            var next = Math.Min(Math.Max(0, caret) + 1, _contentLength);
            return new CharacterHit(next, 0);
        }

        public override CharacterHit GetPreviousCaretCharacterHit(CharacterHit characterHit)
        {
            var caret = characterHit.FirstCharacterIndex + characterHit.TrailingLength;
            var previous = Math.Max(0, Math.Min(caret, _contentLength) - 1);
            return new CharacterHit(previous, 0);
        }

        public override IList<TextBounds> GetTextBounds(int firstTextSourceCharacterIndex, int textLength)
        {
            var bounds = new List<TextBounds>();
            if (textLength <= 0) return bounds;
            var lineStart = _runs.Count > 0 ? _runs[0].AbsoluteStart : 0;
            var rangeStart = Math.Max(firstTextSourceCharacterIndex, lineStart);
            var rangeEnd = Math.Min(firstTextSourceCharacterIndex + textLength, lineStart + Length);
            if (rangeEnd <= rangeStart) return bounds;
            var runBounds = new List<TextRunBounds>();
            var left = double.PositiveInfinity;
            var right = double.NegativeInfinity;
            foreach (var slice in _runs)
            {
                var sliceStart = slice.AbsoluteStart;
                var sliceEnd = sliceStart + slice.SliceLength;
                var overlapStart = Math.Max(rangeStart, sliceStart);
                var overlapEnd = Math.Min(rangeEnd, sliceEnd);
                if (overlapEnd <= overlapStart || slice.Advances.Length == 0) continue;
                var x = slice.X;
                for (var k = 0; k < slice.SliceLength && k < slice.Advances.Length; k++)
                {
                    var absolute = sliceStart + k;
                    if (absolute >= overlapStart && absolute < overlapEnd)
                    {
                        var w = Math.Max(0, slice.Advances[k]);
                        runBounds.Add(new TextRunBounds(absolute, 1, slice.Run, new Rect(x, 0, w, Height)));
                        if (x < left) left = x;
                        if (x + w > right) right = x + w;
                    }
                    x += Math.Max(0, slice.Advances[k]);
                }
            }
            if (runBounds.Count == 0) return bounds;
            bounds.Add(new TextBounds(_flow, new Rect(left, 0, Math.Max(0, right - left), Height), runBounds));
            return bounds;
        }

        public override IList<TextCollapsedRange> GetTextCollapsedRanges() => Array.Empty<TextCollapsedRange>();

        public override TextLineBreak GetTextLineBreak() => new();

        public override IList<TextSpan<TextRun>> GetTextRunSpans()
        {
            var spans = new List<TextSpan<TextRun>>(_runs.Count);
            foreach (var slice in _runs) spans.Add(new TextSpan<TextRun>(slice.SliceLength, slice.Run));
            return spans;
        }
    }
}

/// <summary>
/// One formatted line (matches System.Windows.Media.TextFormatting.TextLine).
/// Concrete lines come from the managed backend below; the contract
/// here is what converted AvalonEdit line consumers bind.
/// </summary>
public abstract class TextLine : IDisposable
{
    /// <summary>Allows subclassing (matches the WPF family constructor).</summary>
    protected TextLine()
    {
    }

    /// <summary>Allows subclassing with a resolution (matches the WPF family constructor).</summary>
    protected TextLine(double pixelsPerDip)
    {
        PixelsPerDip = pixelsPerDip;
    }

    public abstract double Baseline { get; }

    public abstract int DependentLength { get; }

    public abstract double Extent { get; }

    public abstract bool HasCollapsed { get; }

    public abstract bool HasOverflowed { get; }

    public abstract double Height { get; }

    public abstract int Length { get; }

    public abstract double MarkerBaseline { get; }

    public abstract double MarkerHeight { get; }

    public abstract int NewlineLength { get; }

    public abstract double OverhangAfter { get; }

    public abstract double OverhangLeading { get; }

    public abstract double OverhangTrailing { get; }

    public abstract double Start { get; }

    public abstract double TextBaseline { get; }

    public abstract double TextHeight { get; }

    public abstract int TrailingWhitespaceLength { get; }

    public abstract double Width { get; }

    public abstract double WidthIncludingTrailingWhitespace { get; }

    /// <summary>Whether the line was truncated (virtual default false, like WPF).</summary>
    public virtual bool IsTruncated => false;

    /// <summary>Resolution the line was formatted at (default 1, like WPF).</summary>
    public double PixelsPerDip { get; set; } = 1.0;

    public abstract TextLine Collapse(TextCollapsingProperties[] collapsingPropertiesList);

    public abstract void Dispose();

    public abstract void Draw(DrawingContext drawingContext, Point origin, InvertAxes inversion);

    public abstract CharacterHit GetBackspaceCaretCharacterHit(CharacterHit characterHit);

    public abstract CharacterHit GetCharacterHitFromDistance(double distance);

    public abstract double GetDistanceFromCharacterHit(CharacterHit characterHit);

    public abstract IEnumerable<IndexedGlyphRun> GetIndexedGlyphRuns();

    public abstract CharacterHit GetNextCaretCharacterHit(CharacterHit characterHit);

    public abstract CharacterHit GetPreviousCaretCharacterHit(CharacterHit characterHit);

    public abstract IList<TextBounds> GetTextBounds(int firstTextSourceCharacterIndex, int textLength);

    public abstract IList<TextCollapsedRange> GetTextCollapsedRanges();

    public abstract TextLineBreak GetTextLineBreak();

    public abstract IList<TextSpan<TextRun>> GetTextRunSpans();
}

/// <summary>
/// Formatted bounds of a character range (matches
/// System.Windows.Media.TextFormatting.TextBounds). Instances come from
/// TextLine.GetTextBounds; there is no public constructor, like WPF.
/// </summary>
public sealed class TextBounds
{
    internal TextBounds(FlowDirection flowDirection, Rect rectangle, IList<TextRunBounds> textRunBounds)
    {
        FlowDirection = flowDirection;
        Rectangle = rectangle;
        TextRunBounds = textRunBounds;
    }

    public FlowDirection FlowDirection { get; }

    public Rect Rectangle { get; }

    public IList<TextRunBounds> TextRunBounds { get; }
}

/// <summary>
/// Per-run formatted bounds (matches
/// System.Windows.Media.TextFormatting.TextRunBounds).
/// </summary>
public sealed class TextRunBounds
{
    internal TextRunBounds(int textSourceCharacterIndex, int length, TextRun textRun, Rect rectangle)
    {
        TextSourceCharacterIndex = textSourceCharacterIndex;
        Length = length;
        TextRun = textRun;
        Rectangle = rectangle;
    }

    public int TextSourceCharacterIndex { get; }

    public int Length { get; }

    public TextRun TextRun { get; }

    public Rect Rectangle { get; }
}

/// <summary>
/// Glyph run with its source position (matches
/// System.Windows.Media.TextFormatting.IndexedGlyphRun).
/// </summary>
public sealed class IndexedGlyphRun
{
    internal IndexedGlyphRun(GlyphRun glyphRun, int textSourceCharacterIndex, int textSourceLength)
    {
        GlyphRun = glyphRun;
        TextSourceCharacterIndex = textSourceCharacterIndex;
        TextSourceLength = textSourceLength;
    }

    public GlyphRun GlyphRun { get; }

    public int TextSourceCharacterIndex { get; }

    public int TextSourceLength { get; }
}

/// <summary>
/// Collapsed range of a collapsed line (matches
/// System.Windows.Media.TextFormatting.TextCollapsedRange).
/// </summary>
public sealed class TextCollapsedRange
{
    internal TextCollapsedRange(int textSourceCharacterIndex, int length, double width)
    {
        TextSourceCharacterIndex = textSourceCharacterIndex;
        Length = length;
        Width = width;
    }

    public int TextSourceCharacterIndex { get; }

    public int Length { get; }

    public double Width { get; }
}

/// <summary>
/// Collapsing rule for overlong lines (matches
/// System.Windows.Media.TextFormatting.TextCollapsingProperties).
/// </summary>
public abstract class TextCollapsingProperties
{
    /// <summary>Allows subclassing (matches the WPF family constructor).</summary>
    protected TextCollapsingProperties()
    {
    }

    public abstract TextCollapsingStyle Style { get; }

    public abstract TextRun Symbol { get; }

    public abstract double Width { get; }
}

/// <summary>
/// Tab stop definition (matches
/// System.Windows.Media.TextFormatting.TextTabProperties).
/// </summary>
public sealed class TextTabProperties
{
    public TextTabProperties(TextTabAlignment alignment, double location, int tabLeader, int aligningChar)
    {
        Alignment = alignment;
        Location = location;
        TabLeader = tabLeader;
        AligningCharacter = aligningChar;
    }

    public int AligningCharacter { get; }

    public TextTabAlignment Alignment { get; }

    public double Location { get; }

    public int TabLeader { get; }
}

/// <summary>
/// Line-layout cache token (matches
/// System.Windows.Media.TextFormatting.TextRunCache).
/// </summary>
public sealed class TextRunCache
{
    public TextRunCache()
    {
    }

    /// <summary>Notes a source change (matches Change; no backend to invalidate here).</summary>
    public void Change(int textSourceCharacterIndex, int addition, int removal)
    {
    }

    /// <summary>Clears cached state (matches Invalidate; no backend state here).</summary>
    public void Invalidate()
    {
    }
}

/// <summary>
/// Paragraph width extremes (matches
/// System.Windows.Media.TextFormatting.MinMaxParagraphWidth).
/// </summary>
public readonly struct MinMaxParagraphWidth : IEquatable<MinMaxParagraphWidth>
{
    internal MinMaxParagraphWidth(double minWidth, double maxWidth)
    {
        MinWidth = minWidth;
        MaxWidth = maxWidth;
    }

    public double MinWidth { get; }

    public double MaxWidth { get; }

    public bool Equals(MinMaxParagraphWidth other) =>
        MinWidth == other.MinWidth && MaxWidth == other.MaxWidth;

    public override bool Equals(object? obj) => obj is MinMaxParagraphWidth other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(MinWidth, MaxWidth);

    public static bool operator ==(MinMaxParagraphWidth left, MinMaxParagraphWidth right) => left.Equals(right);

    public static bool operator !=(MinMaxParagraphWidth left, MinMaxParagraphWidth right) => !left.Equals(right);
}

/// <summary>
/// Per-character effect (matches System.Windows.Media.TextEffect).
/// </summary>
public sealed class TextEffect : Animatable
{
    public TextEffect()
    {
    }

    public TextEffect(Transform transform, Brush foreground, Geometry clip, int positionStart, int positionCount)
    {
        Transform = transform;
        Foreground = foreground;
        Clip = clip;
        PositionStart = positionStart;
        PositionCount = positionCount;
    }

    public Geometry? Clip { get; set; }

    public Brush? Foreground { get; set; }

    public int PositionCount { get; set; }

    public int PositionStart { get; set; }

    public Transform? Transform { get; set; }

    /// <summary>Modifiable copy (matches Clone).</summary>
    public new TextEffect Clone() => (TextEffect)MemberwiseClone();

    /// <summary>Identical to Clone here (matches CloneCurrentValue).</summary>
    public new TextEffect CloneCurrentValue() => Clone();
}

/// <summary>
/// Effect list over runs (matches System.Windows.Media.TextEffectCollection).
/// </summary>
public sealed class TextEffectCollection : Animatable, IList<TextEffect>, IList
{
    private readonly List<TextEffect> _effects = new();
    private int _version;

    public TextEffectCollection()
    {
    }

    public TextEffectCollection(IEnumerable<TextEffect> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _effects.AddRange(collection);
    }

    public TextEffectCollection(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _effects.Capacity = capacity;
    }

    public TextEffect this[int index]
    {
        get => _effects[index];
        set
        {
            ThrowIfFrozen();
            _effects[index] = value;
            _version++;
        }
    }

    public int Count => _effects.Count;

    public bool IsReadOnly => false;

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => false;

    int ICollection.Count => _effects.Count;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    public void Add(TextEffect value)
    {
        ThrowIfFrozen();
        _effects.Add(value);
        _version++;
    }

    /// <summary>Adds a range (matches Add(IEnumerable)).</summary>
    public void Add(IEnumerable<TextEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ThrowIfFrozen();
        _effects.AddRange(effects);
        _version++;
    }

    public void Clear()
    {
        ThrowIfFrozen();
        _effects.Clear();
        _version++;
    }

    /// <summary>Deep copy (matches Clone).</summary>
    public new TextEffectCollection Clone()
    {
        var clone = new TextEffectCollection();
        foreach (var effect in _effects) clone._effects.Add(effect.Clone());
        return clone;
    }

    /// <summary>Identical to Clone here (matches CloneCurrentValue).</summary>
    public new TextEffectCollection CloneCurrentValue() => Clone();

    public bool Contains(TextEffect value) => _effects.Contains(value);

    public void CopyTo(TextEffect[] array, int index) => _effects.CopyTo(array, index);

    public Enumerator GetEnumerator() => new(this);

    public int IndexOf(TextEffect value) => _effects.IndexOf(value);

    public void Insert(int index, TextEffect value)
    {
        ThrowIfFrozen();
        _effects.Insert(index, value);
        _version++;
    }

    public bool Remove(TextEffect value)
    {
        ThrowIfFrozen();
        var removed = _effects.Remove(value);
        if (removed) _version++;
        return removed;
    }

    public void RemoveAt(int index)
    {
        ThrowIfFrozen();
        _effects.RemoveAt(index);
        _version++;
    }

    int IList.Add(object? value)
    {
        Add((TextEffect)value!);
        return Count - 1;
    }

    bool IList.Contains(object? value) => value is TextEffect effect && Contains(effect);

    int IList.IndexOf(object? value) => value is TextEffect effect ? IndexOf(effect) : -1;

    void IList.Insert(int index, object? value) => Insert(index, (TextEffect)value!);

    void IList.Remove(object? value)
    {
        if (value is TextEffect effect) Remove(effect);
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = (TextEffect)value!;
    }

    void ICollection.CopyTo(Array array, int index) => ((ICollection)_effects).CopyTo(array, index);

    IEnumerator<TextEffect> IEnumerable<TextEffect>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Version-stamped enumerator (matches the nested Enumerator shape;
    /// mutation during enumeration throws, like WPF).
    /// </summary>
    public struct Enumerator : IEnumerator<TextEffect>
    {
        private readonly TextEffectCollection _collection;
        private readonly int _version;
        private int _index;
        private TextEffect? _current;

        internal Enumerator(TextEffectCollection collection)
        {
            _collection = collection;
            _version = collection._version;
            _index = -1;
            _current = null;
        }

        public readonly TextEffect Current => _current!;

        readonly object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_version != _collection._version)
                throw new InvalidOperationException("Collection was modified during enumeration.");
            if (_index + 1 >= _collection._effects.Count) return false;
            _index++;
            _current = _collection._effects[_index];
            return true;
        }

        public void Reset()
        {
            if (_version != _collection._version)
                throw new InvalidOperationException("Collection was modified during enumeration.");
            _index = -1;
            _current = null;
        }

        public void Dispose()
        {
        }
    }
}
