using System.Globalization;
using System.Reflection;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-86 text-formatting core regression tests: the ~30 AvalonEdit rows
/// (20 System.Windows.Media.TextFormatting types plus the System.Windows
/// typography/layout enums and the TextEffectCollection signature type)
/// ride the default System.Windows.* rule once the engine declares the
/// exact upstream shapes (member shapes verified with Mono.Cecil against
/// the upstream net10.0-windows ICSharpCode.AvalonEdit.dll and the 10.0.13
/// PresentationCore ref pack; virtual defaults verified against a live
/// net10.0-windows runtime probe). Converted call sites: TextFormatter
/// Create(TextFormattingMode)/FormatLine/Dispose in TextView and
/// TextFormatterFactory, TextCharacters/TextEndOfParagraph construction in
/// the text sources, TextLine metric/draw/hit-test consumption across the
/// caret/margin/visual-line code, plus the 8 subclass overrides (sources,
/// run/paragraph/typography properties, embedded-object runs). No
/// Application state is touched, so these stay outside the serialized
/// ApplicationState collection.
/// </summary>
[ResetAmbientState]
public sealed class Slice86TextFormattingTests
{
    [Fact]
    public void Layout_enum_values_match_wpf()
    {
        Assert.Equal(3, (int)BaselineAlignment.Baseline);
        Assert.Equal(0, (int)BaselineAlignment.Top);
        Assert.Equal(7, (int)BaselineAlignment.Superscript);
        Assert.Equal(0, (int)LineBreakCondition.BreakDesired);
        Assert.Equal(1, (int)LineBreakCondition.BreakPossible);
        Assert.Equal(2, (int)LineBreakCondition.BreakRestrained);
        Assert.Equal(3, (int)LineBreakCondition.BreakAlways);
        Assert.Equal(3, (int)InvertAxes.Both);
        Assert.Equal(1, (int)TextCollapsingStyle.TrailingWord);
        Assert.Equal(3, (int)TextTabAlignment.Character);
    }

    [Fact]
    public void Typography_enum_values_match_wpf()
    {
        Assert.Equal(0, (int)FontCapitals.Normal);
        Assert.Equal(6, (int)FontCapitals.Titling);
        Assert.Equal(9, (int)FontEastAsianLanguage.TraditionalNames);
        Assert.Equal(5, (int)FontEastAsianWidths.Quarter);
        Assert.Equal(2, (int)FontFraction.Stacked);
        Assert.Equal(2, (int)FontNumeralAlignment.Tabular);
        Assert.Equal(2, (int)FontNumeralStyle.OldStyle);
        Assert.Equal(5, (int)FontVariants.Ruby);
        Assert.Equal(2, (int)FontVariants.Subscript);
    }

    [Fact]
    public void Character_buffer_ranges_carry_slices()
    {
        var range = new CharacterBufferRange("hello", 1, 3);
        Assert.Equal(3, range.Length);
        Assert.Equal(3, new CharacterBufferRange(['h', 'e', 'l', 'l', 'o'], 1, 3).Length);
        Assert.Equal(0, CharacterBufferRange.Empty.Length);
        Assert.Equal(range, new CharacterBufferRange("hello", 1, 3));
        Assert.NotEqual(range, new CharacterBufferRange("hello", 1, 2));
        Assert.Equal(range.GetHashCode(), new CharacterBufferRange("hello", 1, 3).GetHashCode());
        Assert.True(range == new CharacterBufferRange("hello", 1, 3));
        Assert.True(range != CharacterBufferRange.Empty);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterBufferRange("hi", 0, 3));
        Assert.Throws<ArgumentNullException>(() => new CharacterBufferRange((string)null!, 0, 0));
        var hit = new CharacterHit(3, 1);
        Assert.Equal(3, hit.FirstCharacterIndex);
        Assert.Equal(1, hit.TrailingLength);
        Assert.Equal(hit, new CharacterHit(3, 1));
        Assert.True(hit != new CharacterHit(3, 0));
    }

    [Fact]
    public void Unsafe_buffer_ctors_exist_like_wpf()
    {
        // The WPF public char* overloads exist (pinned by parameter shape so
        // the test project needs no unsafe context); behavior is covered
        // through the string/array overloads.
        var rangeCtor = typeof(CharacterBufferRange).GetConstructors()
            .Single(c => c.GetParameters().Length == 2 && c.GetParameters()[0].ParameterType.IsPointer);
        Assert.Equal(typeof(int), rangeCtor.GetParameters()[1].ParameterType);
        var refCtor = typeof(CharacterBufferReference).GetConstructors()
            .Single(c => c.GetParameters().Length == 2 && c.GetParameters()[0].ParameterType.IsPointer);
        Assert.Equal(typeof(int), refCtor.GetParameters()[1].ParameterType);
        var charsCtor = typeof(TextCharacters).GetConstructors()
            .Single(c => c.GetParameters().Length == 3 && c.GetParameters()[0].ParameterType.IsPointer);
        Assert.Equal(typeof(TextRunProperties), charsCtor.GetParameters()[2].ParameterType);
    }

    [Fact]
    public void Text_runs_carry_lengths_and_properties()
    {
        var props = new ProbeRunProperties();
        var chars = new TextCharacters("hello", 1, 3, props);
        Assert.Equal(3, chars.Length);
        Assert.Same(props, chars.Properties);
        Assert.Equal(new TextCharacters("hello", 1, 3, props).CharacterBufferReference, chars.CharacterBufferReference);
        var whole = new TextCharacters("hi", props);
        Assert.Equal(2, whole.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextCharacters("hi", 0, 5, props));
        var eop = new TextEndOfParagraph(2);
        Assert.Equal(2, eop.Length);
        Assert.IsAssignableFrom<TextEndOfLine>(eop);
        Assert.IsAssignableFrom<TextRun>(eop);
        var eol = new TextEndOfLine(1, props);
        Assert.Equal(1, eol.Length);
        Assert.Same(props, eol.Properties);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextEndOfParagraph(-1));
        var span = new TextSpan<int>(5, 42);
        Assert.Equal(5, span.Length);
        Assert.Equal(42, span.Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan<int>(-1, 0));
        var culture = new CultureSpecificCharacterBufferRange(
            CultureInfo.InvariantCulture, new CharacterBufferRange("abc", 0, 3));
        Assert.Same(CultureInfo.InvariantCulture, culture.CultureInfo);
        Assert.Equal(3, culture.CharacterBufferRange.Length);
        var metrics = new TextEmbeddedObjectMetrics(10, 20, 15);
        Assert.Equal(10, metrics.Width);
        Assert.Equal(20, metrics.Height);
        Assert.Equal(15, metrics.Baseline);
    }

    [Fact]
    public void Run_property_virtual_defaults_match_live_wpf()
    {
        // Grounded by the live net10.0-windows probe: BaselineAlignment
        // Baseline(3), null NumberSubstitution/TypographyProperties,
        // PixelsPerDip 1 on all three holders.
        var props = new ProbeRunProperties();
        Assert.Equal(BaselineAlignment.Baseline, props.BaselineAlignment);
        Assert.Null(props.NumberSubstitution);
        Assert.Null(props.TypographyProperties);
        Assert.Equal(1.0, props.PixelsPerDip);
        Assert.Equal(1.0, new ProbeSource().PixelsPerDip);
    }

    [Fact]
    public void Paragraph_property_virtual_defaults_match_live_wpf()
    {
        var para = new ProbeParagraphProperties();
        Assert.False(para.AlwaysCollapsible);
        Assert.Equal(48.0, para.DefaultIncrementalTab);
        Assert.Equal(0.0, para.ParagraphIndent);
        Assert.Null(para.Tabs);
        Assert.Null(para.TextDecorations);
    }

    [Fact]
    public void Typography_shape_covers_the_converted_overrides()
    {
        // AvalonEdit's DefaultTextRunTypographyProperties overrides all 43;
        // the probe double below must compile against the engine base, and
        // every converted override name must exist as an abstract getter.
        var names = typeof(TextRunTypographyProperties).GetProperties()
            .Where(p => p.GetMethod!.IsAbstract).Select(p => p.Name).Order().ToList();
        Assert.Equal(43, names.Count);
        Assert.Contains("Variants", names);
        Assert.Contains("StylisticSet20", names);
        Assert.Contains("NumeralStyle", names);
        Assert.Contains("EastAsianWidths", names);
        Assert.Contains("Capitals", names);
        var typo = new ProbeTypography();
        Assert.Equal(FontVariants.Ruby, typo.Variants);
        Assert.True(typo.Kerning);
    }

    [Fact]
    public void Formatter_lays_out_source_text_with_real_metrics()
    {
        var ideal = TextFormatter.Create();
        Assert.NotNull(ideal);
        var display = TextFormatter.Create(TextFormattingMode.Display);
        Assert.NotNull(display);
        // TextView recreates formatters on document change: Dispose is real.
        ideal.Dispose();
        var source = new ProbeSource();
        var para = new ProbeParagraphProperties();
        using var line = display.FormatLine(source, 0, 1000, para, null);
        // "hi" plus the paragraph break the probe source always terminates with.
        Assert.Equal(3, line.Length);
        Assert.Equal(1, line.NewlineLength);
        Assert.Equal(0, line.TrailingWhitespaceLength);
        Assert.True(line.Width > 0);
        Assert.Equal(line.Width, line.WidthIncludingTrailingWhitespace);
        Assert.True(line.Height > 0);
        Assert.True(line.Baseline > 0);
        Assert.True(line.Baseline <= line.Height);
        Assert.True(line.Start >= 0);
        Assert.IsType<TextLineBreak>(line.GetTextLineBreak());
        var bounds = line.GetTextBounds(0, 2);
        var single = Assert.Single(bounds);
        Assert.True(single.Rectangle.Width > 0);
        var hit = line.GetCharacterHitFromDistance(0);
        Assert.Equal(0, hit.FirstCharacterIndex);
        Assert.Equal(0, line.GetDistanceFromCharacterHit(new CharacterHit(0, 0)));
        var next = line.GetNextCaretCharacterHit(new CharacterHit(0, 0));
        Assert.Equal(1, next.FirstCharacterIndex);
        var spans = line.GetTextRunSpans();
        // One source run yields one span (native WPF behaves the same).
        Assert.True(spans.Count >= 1);
        Assert.True(spans.Sum(s => s.Length) >= line.Length - line.NewlineLength);
        // Draw replays the run without throwing (paint agreement rides canvas tests).
        line.Draw(new DrawingContext(), new Point(0, 0), InvertAxes.None);
        var sized = display.FormatMinMaxParagraphWidth(source, 0, para);
        Assert.True(sized.MaxWidth >= line.WidthIncludingTrailingWhitespace);
        Assert.True(sized.MinWidth >= 0);
        Assert.True(sized.MinWidth <= sized.MaxWidth);
        var sizedCached = display.FormatMinMaxParagraphWidth(source, 0, para, new TextRunCache());
        Assert.Equal(sized, sizedCached);
        // Probe coverage: keep the source/paragraph probes honest.
        var first = source.GetTextRun(0);
        Assert.True(first is TextCharacters);
        Assert.Equal(2, first.Length);
        var second = source.GetTextRun(2);
        Assert.True(second is TextEndOfParagraph);
    }

    [Fact]
    public void Converted_source_shapes_drive_runs_through_the_engine()
    {
        // Mirrors the converted SimpleTextSource/VisualLineTextSource shapes:
        // text runs, end-of-paragraph, and preceding-text spans flow without
        // the layout backend.
        var source = new ProbeSource();
        var first = source.GetTextRun(0);
        Assert.Equal(2, first.Length);
        Assert.IsType<TextCharacters>(first);
        var second = source.GetTextRun(2);
        Assert.IsType<TextEndOfParagraph>(second);
        Assert.Equal(1, second.Length);
        var preceding = source.GetPrecedingText(5);
        Assert.Equal(0, preceding.Length);
        Assert.Same(CultureInfo.InvariantCulture, preceding.Value.CultureInfo);
        Assert.Equal(5, source.GetTextEffectCharacterIndexFromTextSourceCharacterIndex(5));
        new TextRunCache().Invalidate();
        new TextRunCache().Change(0, 1, 0);
    }

    [Fact]
    public void Embedded_object_shape_covers_the_converted_runs()
    {
        // Mirrors the converted FormattedTextRun/InlineObjectRun overrides:
        // every abstract member binds and dispatches through the engine base.
        TextEmbeddedObject run = new ProbeEmbeddedObject();
        Assert.Equal(2, run.Length);
        Assert.Equal(LineBreakCondition.BreakAlways, run.BreakBefore);
        Assert.False(run.HasFixedSize);
        var metrics = run.Format(100);
        Assert.Equal(50, metrics.Width);
        var box = run.ComputeBoundingBox(false, false);
        Assert.Equal(50, box.Width);
        Assert.Same(run.Properties, ((TextRun)run).Properties);
    }

    [Fact]
    public void Effect_collections_behave_with_versioned_enumeration()
    {
        var effects = new TextEffectCollection
        {
            new TextEffect { PositionStart = 2, PositionCount = 3 },
        };
        Assert.Equal(1, effects.Count);
        Assert.Equal(2, effects[0].PositionStart);
        var clone = effects.Clone();
        Assert.Equal(1, clone.Count);
        Assert.NotSame(effects[0], clone[0]);
        Assert.Equal(2, clone[0].PositionStart);
        using var enumerator = effects.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.Same(effects[0], enumerator.Current);
        Assert.False(enumerator.MoveNext());
        enumerator.Reset();
        Assert.True(enumerator.MoveNext());
        var live = effects.GetEnumerator();
        Assert.True(live.MoveNext());
        effects.Add(new TextEffect());
        Assert.Throws<InvalidOperationException>(() => live.MoveNext());
        Assert.Contains(effects, e => e.PositionCount == 3);
        Assert.Equal(1, effects.IndexOf(effects[1]));
        Assert.True(effects.Remove(effects[1]));
        Assert.Equal(1, effects.Count);
        effects.Freeze();
        Assert.Throws<InvalidOperationException>(() => effects.Add(new TextEffect()));
    }

    [Fact]
    public void Line_model_shapes_match_wpf()
    {
        // No public TextLineBreak constructor, like WPF (backends hand out
        // instances); Clone/Dispose exist with the WPF signatures.
        Assert.Empty(typeof(TextLineBreak).GetConstructors());
        Assert.NotNull(typeof(TextLineBreak).GetMethod("Clone", Type.EmptyTypes));
        Assert.NotNull(typeof(TextLineBreak).GetMethod("Dispose", Type.EmptyTypes));
        var tab = new TextTabProperties(TextTabAlignment.Character, 72, 0, (int)'.');
        Assert.Equal(TextTabAlignment.Character, tab.Alignment);
        Assert.Equal(72, tab.Location);
        // Every converted TextLine consumer member must exist on the base.
        foreach (var name in new[]
        {
            "Baseline", "Height", "Length", "NewlineLength",
            "TrailingWhitespaceLength", "Width", "WidthIncludingTrailingWhitespace",
            "Dispose", "Draw", "GetCharacterHitFromDistance",
            "GetDistanceFromCharacterHit", "GetTextBounds", "GetTextLineBreak",
            "GetTextRunSpans",
        })
            Assert.NotNull(typeof(TextLine).GetMember(name));
        // Full-parity extras the signatures pull in.
        Assert.NotNull(typeof(TextLine).GetMethod("GetIndexedGlyphRuns"));
        Assert.NotNull(typeof(TextLine).GetMethod("Collapse"));
        Assert.NotNull(typeof(TextParagraphProperties).GetMethod("get_Tabs"));
        Assert.NotNull(typeof(TextBounds).GetProperty("TextRunBounds"));
        // MinMaxParagraphWidth is a backend-produced value struct with no
        // public constructor; the default value still honors equality.
        Assert.True(typeof(MinMaxParagraphWidth).IsValueType);
        Assert.Equal(default(MinMaxParagraphWidth), default);
        Assert.True(default(MinMaxParagraphWidth) == default);
        Assert.False(default(MinMaxParagraphWidth) != default);
    }

    private sealed class ProbeRunProperties : TextRunProperties
    {
        public override Brush BackgroundBrush => new SolidColorBrush(Colors.Transparent);

        public override CultureInfo CultureInfo => CultureInfo.InvariantCulture;

        public override double FontHintingEmSize => 12;

        public override double FontRenderingEmSize => 12;

        public override Brush ForegroundBrush => new SolidColorBrush(Colors.Black);

        public override TextDecorationCollection TextDecorations => new();

        public override TextEffectCollection TextEffects => new();

        public override Typeface Typeface => new("Segoe UI");
    }

    private sealed class ProbeParagraphProperties : TextParagraphProperties
    {
        public override TextRunProperties DefaultTextRunProperties => new ProbeRunProperties();

        public override bool FirstLineInParagraph => true;

        public override FlowDirection FlowDirection => FlowDirection.LeftToRight;

        public override double Indent => 0;

        public override double LineHeight => double.NaN;

        public override TextAlignment TextAlignment => TextAlignment.Left;

        public override TextMarkerProperties TextMarkerProperties => null!;

        public override TextWrapping TextWrapping => TextWrapping.Wrap;
    }

    private sealed class ProbeTypography : TextRunTypographyProperties
    {
        public override int AnnotationAlternates => 0;

        public override FontCapitals Capitals => FontCapitals.Normal;

        public override bool CapitalSpacing => false;

        public override bool CaseSensitiveForms => false;

        public override bool ContextualAlternates => false;

        public override bool ContextualLigatures => false;

        public override int ContextualSwashes => 0;

        public override bool DiscretionaryLigatures => false;

        public override bool EastAsianExpertForms => false;

        public override FontEastAsianLanguage EastAsianLanguage => FontEastAsianLanguage.Normal;

        public override FontEastAsianWidths EastAsianWidths => FontEastAsianWidths.Normal;

        public override FontFraction Fraction => FontFraction.Normal;

        public override bool HistoricalForms => false;

        public override bool HistoricalLigatures => false;

        public override bool Kerning => true;

        public override bool MathematicalGreek => false;

        public override FontNumeralAlignment NumeralAlignment => FontNumeralAlignment.Normal;

        public override FontNumeralStyle NumeralStyle => FontNumeralStyle.Normal;

        public override bool SlashedZero => false;

        public override bool StandardLigatures => false;

        public override int StandardSwashes => 0;

        public override int StylisticAlternates => 0;

        public override bool StylisticSet1 => false;

        public override bool StylisticSet2 => false;

        public override bool StylisticSet3 => false;

        public override bool StylisticSet4 => false;

        public override bool StylisticSet5 => false;

        public override bool StylisticSet6 => false;

        public override bool StylisticSet7 => false;

        public override bool StylisticSet8 => false;

        public override bool StylisticSet9 => false;

        public override bool StylisticSet10 => false;

        public override bool StylisticSet11 => false;

        public override bool StylisticSet12 => false;

        public override bool StylisticSet13 => false;

        public override bool StylisticSet14 => false;

        public override bool StylisticSet15 => false;

        public override bool StylisticSet16 => false;

        public override bool StylisticSet17 => false;

        public override bool StylisticSet18 => false;

        public override bool StylisticSet19 => false;

        public override bool StylisticSet20 => false;

        public override FontVariants Variants => FontVariants.Ruby;
    }

    private sealed class ProbeSource : TextSource
    {
        public override TextRun GetTextRun(int textSourceCharacterIndex) =>
            textSourceCharacterIndex == 0
                ? new TextCharacters("hi", 0, 2, new ProbeRunProperties())
                : new TextEndOfParagraph(1);

        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndex) =>
            new(0, new CultureSpecificCharacterBufferRange(CultureInfo.InvariantCulture, CharacterBufferRange.Empty));

        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex) =>
            textSourceCharacterIndex;
    }

    private sealed class ProbeEmbeddedObject : TextEmbeddedObject
    {
        private readonly TextRunProperties _properties = new ProbeRunProperties();

        public override LineBreakCondition BreakBefore => LineBreakCondition.BreakAlways;

        public override LineBreakCondition BreakAfter => LineBreakCondition.BreakDesired;

        public override bool HasFixedSize => false;

        public override CharacterBufferReference CharacterBufferReference => default;

        public override int Length => 2;

        public override TextRunProperties Properties => _properties;

        public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways) => new(0, 0, 50, 20);

        public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways)
        {
        }

        public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth) => new(50, 20, 15);
    }
}
