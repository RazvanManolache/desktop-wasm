namespace DesktopWasm.Wpf;

// FlowDocument cluster (PresentationFramework System.Windows.Documents +
// System.Windows.Controls FlowDocumentScrollViewer, PresentationCore
// System.Windows.FlowDirection/FontStretch): block/inline hosting with real
// layout/paint for the ScreenToGif workflows (History Content documents,
// DownloadDialog release-note paragraphs, InlineExtensions link builders).
// Enum numerics, DP defaults and member shapes were verified against the
// real WPF assemblies (net9.0-windows oracle probe, 2026-10-23).
//
// Engine merge note: real WPF roots content elements at DispatcherObject
// (ContentElement/FrameworkContentElement/TextElement), which cannot host
// layout, parenting or input. The engine unifies the branch under
// FrameworkElement so inlines keep logical parenting, measure/arrange and
// routed behavior; the WPF type names, DP fields and member shapes are
// preserved so converted references bind.

/// <summary>Bidirectional flow (matches System.Windows.FlowDirection; verified LeftToRight=0, RightToLeft=1).</summary>
public enum FlowDirection
{
    LeftToRight = 0,
    RightToLeft = 1,
}

/// <summary>
/// Stretch class (matches the System.Windows.FontStretch struct shape:
/// instanceless values from <see cref="FontStretches"/>, name-formatted).
/// </summary>
public readonly struct FontStretch : IEquatable<FontStretch>, IFormattable
{
    private readonly int _stretch;

    internal FontStretch(int stretch) => _stretch = stretch;

    /// <summary>OpenType stretch class (1-9, 5 is normal).</summary>
    public int ToOpenTypeStretch() => _stretch;

    public bool Equals(FontStretch other) => _stretch == other._stretch;
    public override bool Equals(object? obj) => obj is FontStretch other && Equals(other);
    public override int GetHashCode() => _stretch;
    public static bool operator ==(FontStretch left, FontStretch right) => left.Equals(right);
    public static bool operator !=(FontStretch left, FontStretch right) => !left.Equals(right);

    public override string ToString() => _stretch switch
    {
        1 => "UltraCondensed",
        2 => "ExtraCondensed",
        3 => "Condensed",
        4 => "SemiCondensed",
        5 => "Normal",
        6 => "SemiExpanded",
        7 => "Expanded",
        8 => "ExtraExpanded",
        9 => "UltraExpanded",
        _ => _stretch.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public string ToString(string? format, IFormatProvider? provider) => ToString();
}

/// <summary>Named stretch values (matches System.Windows.FontStretches; Medium aliases Normal, verified).</summary>
public static class FontStretches
{
    public static FontStretch UltraCondensed => new(1);
    public static FontStretch ExtraCondensed => new(2);
    public static FontStretch Condensed => new(3);
    public static FontStretch SemiCondensed => new(4);
    public static FontStretch Normal => new(5);
    public static FontStretch Medium => new(5);
    public static FontStretch SemiExpanded => new(6);
    public static FontStretch Expanded => new(7);
    public static FontStretch ExtraExpanded => new(8);
    public static FontStretch UltraExpanded => new(9);
}

/// <summary>
/// Content-element root (matches System.Windows.FrameworkContentElement):
/// the engine merge point giving content elements (TextElement,
/// FlowDocument) the FrameworkElement layout/parenting/binding surface.
/// </summary>
public class FrameworkContentElement : FrameworkElement
{
    private Cursor? _cursor;

    /// <summary>Pointer cursor (matches Cursor; retained, no cursor layer yet).</summary>
    public Cursor? Cursor
    {
        get => _cursor;
        set { _cursor = value; }
    }

    /// <summary>Cursor forcing (matches ForceCursor; retained).</summary>
    public bool ForceCursor { get; set; }

    protected override WpfSize MeasureCore(WpfSize available) => new(0, 0);
    protected override void OnRender(DrawingContext dc) { }
}

/// <summary>
/// Font/ink inheritance source (TextElement, FlowDocument, TextBlock):
/// resolved values, so grandchildren read through intermediate elements
/// that leave a value unset, like WPF property inheritance.
/// </summary>
internal interface IDocumentFontSource
{
    FontFamily InheritedFamily { get; }
    double InheritedSize { get; }
    FontStyle InheritedStyle { get; }
    FontWeight InheritedWeight { get; }
    FontStretch InheritedStretch { get; }
    Brush? InheritedForeground { get; }
    Brush? InheritedBackground { get; }
}

/// <summary>
/// Formatted-text root (matches System.Windows.Documents.TextElement):
/// font/ink DPs with parent-chain inheritance (WPF property inheritance),
/// so runs that leave a value unset read the paragraph/document value and
/// otherwise the WPF defaults (Segoe UI, 12px, normal, black; verified).
/// </summary>
public class TextElement : FrameworkContentElement, IDocumentFontSource
{
    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register("FontFamily", typeof(FontFamily), typeof(TextElement),
            new FrameworkPropertyMetadata(new FontFamily("Segoe UI")));
    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register("FontSize", typeof(double), typeof(TextElement),
            new FrameworkPropertyMetadata(12.0));
    public static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.Register("FontStyle", typeof(FontStyle), typeof(TextElement),
            new FrameworkPropertyMetadata(FontStyles.Normal));
    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register("FontWeight", typeof(FontWeight), typeof(TextElement),
            new FrameworkPropertyMetadata(FontWeights.Normal));
    public static readonly DependencyProperty FontStretchProperty =
        DependencyProperty.Register("FontStretch", typeof(FontStretch), typeof(TextElement),
            new FrameworkPropertyMetadata(FontStretches.Normal));
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register("Foreground", typeof(Brush), typeof(TextElement),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register("Background", typeof(Brush), typeof(TextElement),
            new FrameworkPropertyMetadata(null));

    private FontFamily? _fontFamily;
    private double? _fontSize;
    private FontStyle? _fontStyle;
    private FontWeight? _fontWeight;
    private FontStretch? _fontStretch;
    private Brush? _foreground;
    private Brush? _background;

    private IDocumentFontSource? InheritedSource
    {
        get
        {
            for (var current = ((UIElement)this).Parent; current is not null; current = current.Parent)
            {
                if (ReferenceEquals(current, this)) continue;
                if (current is IDocumentFontSource source) return source;
            }
            return null;
        }
    }

    FontFamily IDocumentFontSource.InheritedFamily => FontFamily;
    double IDocumentFontSource.InheritedSize => FontSize;
    FontStyle IDocumentFontSource.InheritedStyle => FontStyle;
    FontWeight IDocumentFontSource.InheritedWeight => FontWeight;
    FontStretch IDocumentFontSource.InheritedStretch => FontStretch;
    Brush? IDocumentFontSource.InheritedForeground => Foreground;
    Brush? IDocumentFontSource.InheritedBackground => Background;

    /// <summary>Font family (unset inherits, else Segoe UI).</summary>
    public FontFamily FontFamily
    {
        get => _fontFamily ?? InheritedSource?.InheritedFamily ?? new FontFamily("Segoe UI");
        set { _fontFamily = value; InvalidateMeasure(); }
    }

    /// <summary>Font size in pixels (unset inherits, else 12).</summary>
    public double FontSize
    {
        get => _fontSize ?? InheritedSource?.InheritedSize ?? 12.0;
        set { _fontSize = value; InvalidateMeasure(); }
    }

    /// <summary>Font slant (unset inherits, else normal).</summary>
    public FontStyle FontStyle
    {
        get => _fontStyle ?? InheritedSource?.InheritedStyle ?? FontStyles.Normal;
        set { _fontStyle = value; InvalidateMeasure(); }
    }

    /// <summary>Font weight (unset inherits, else normal).</summary>
    public FontWeight FontWeight
    {
        get => _fontWeight ?? InheritedSource?.InheritedWeight ?? FontWeights.Normal;
        set { _fontWeight = value; InvalidateMeasure(); }
    }

    /// <summary>Font stretch (unset inherits, else normal).</summary>
    public FontStretch FontStretch
    {
        get => _fontStretch ?? InheritedSource?.InheritedStretch ?? FontStretches.Normal;
        set { _fontStretch = value; InvalidateMeasure(); }
    }

    /// <summary>Ink brush (unset inherits; null keeps the default ink).</summary>
    public Brush? Foreground
    {
        get => _foreground ?? InheritedSource?.InheritedForeground;
        set { _foreground = value; InvalidateVisual(); }
    }

    /// <summary>Fill brush (unset inherits; stored, blocks paint their own).</summary>
    public Brush? Background
    {
        get => _background ?? InheritedSource?.InheritedBackground;
        set { _background = value; InvalidateVisual(); }
    }

    /// <summary>Resolved ink in 0xRRGGBBAA (solid brush, else the inherited ink, else the default).</summary>
    internal uint ResolveInk(uint fallback = 0x000000FF)
    {
        if (Foreground is SolidColorBrush solid) return solid.ToRgba();
        return fallback;
    }

    /// <summary>Legacy CSS font description for the shaper (matches the TextBlock composition).</summary>
    internal string ResolveFontCss()
    {
        var slant = FontStyle == FontStyles.Italic ? "italic " : string.Empty;
        var size = FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{slant}{size}px {FontFamily}";
    }

    public static void SetFontFamily(DependencyObject element, FontFamily value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FontFamilyProperty, value);
        if (element is TextElement textElement) textElement.FontFamily = value;
    }

    public static FontFamily GetFontFamily(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is TextElement textElement) return textElement.FontFamily;
        return (FontFamily)(element.GetValue(FontFamilyProperty) ?? new FontFamily("Segoe UI"));
    }

    public static void SetFontSize(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FontSizeProperty, value);
        if (element is TextElement textElement) textElement.FontSize = value;
    }

    public static double GetFontSize(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is TextElement textElement) return textElement.FontSize;
        return (double)(element.GetValue(FontSizeProperty) ?? 12.0);
    }

    public static void SetFontStyle(DependencyObject element, FontStyle value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FontStyleProperty, value);
        if (element is TextElement textElement) textElement.FontStyle = value;
    }

    public static FontStyle GetFontStyle(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is TextElement textElement) return textElement.FontStyle;
        return (FontStyle)(element.GetValue(FontStyleProperty) ?? FontStyles.Normal);
    }

    public static void SetFontWeight(DependencyObject element, FontWeight value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FontWeightProperty, value);
        if (element is TextElement textElement) textElement.FontWeight = value;
    }

    public static FontWeight GetFontWeight(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is TextElement textElement) return textElement.FontWeight;
        return (FontWeight)(element.GetValue(FontWeightProperty) ?? FontWeights.Normal);
    }

    public static void SetFontStretch(DependencyObject element, FontStretch value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FontStretchProperty, value);
        if (element is TextElement textElement) textElement.FontStretch = value;
    }

    public static FontStretch GetFontStretch(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is TextElement textElement) return textElement.FontStretch;
        return (FontStretch)(element.GetValue(FontStretchProperty) ?? FontStretches.Normal);
    }

    public static void SetForeground(DependencyObject element, Brush value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ForegroundProperty, value);
        if (element is TextElement textElement) textElement.Foreground = value;
    }

    public static Brush? GetForeground(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is TextElement textElement) return textElement.Foreground;
        return (Brush?)element.GetValue(ForegroundProperty);
    }
}

/// <summary>
/// Inline content (matches System.Windows.Documents.Inline): sibling
/// navigation plus the flow direction. The engine Span family derives here,
/// so Run/Hyperlink/LineBreak bind the converted Inline positions.
/// </summary>
public class Inline : TextElement
{
    public static readonly DependencyProperty FlowDirectionProperty =
        DependencyProperty.Register("FlowDirection", typeof(FlowDirection), typeof(Inline),
            new FrameworkPropertyMetadata(FlowDirection.LeftToRight));

    /// <summary>Decorations token (matches Inline.TextDecorationsProperty;
    /// Hyperlink inherits it, like WPF).</summary>
    public static readonly DependencyProperty TextDecorationsProperty =
        DependencyProperty.Register("TextDecorations", typeof(TextDecorationCollection), typeof(Inline),
            new FrameworkPropertyMetadata(null));

    private TextDecorationCollection _textDecorations = new();

    /// <summary>Underline/strikethrough/overline/baseline runs (null clears,
    /// like WPF; the Hyperlink theme styles this, like TextBlock).</summary>
    public TextDecorationCollection? TextDecorations
    {
        get => _textDecorations;
        set { _textDecorations = value ?? new TextDecorationCollection(); InvalidateVisual(); }
    }

    private FlowDirection _flowDirection = FlowDirection.LeftToRight;

    /// <summary>Inline flow (default left-to-right).</summary>
    public FlowDirection FlowDirection
    {
        get => _flowDirection;
        set { _flowDirection = value; }
    }

    /// <summary>Host collection (null when unhosted).</summary>
    public InlineCollection? SiblingInlines => Parent as Paragraph is { } paragraph ? paragraph.Inlines : null;

    /// <summary>Next inline in the host (null at the end).</summary>
    public Inline? NextInline
    {
        get
        {
            var siblings = SiblingInlines;
            if (siblings is null) return null;
            var index = siblings.IndexOf(this);
            return index >= 0 && index + 1 < siblings.Count ? siblings[index + 1] : null;
        }
    }

    /// <summary>Previous inline in the host (null at the start).</summary>
    public Inline? PreviousInline
    {
        get
        {
            var siblings = SiblingInlines;
            if (siblings is null) return null;
            var index = siblings.IndexOf(this);
            return index > 0 ? siblings[index - 1] : null;
        }
    }
}

/// <summary>
/// Block content (matches System.Windows.Documents.Block): sibling
/// navigation, alignment and flow. Paragraph derives here.
/// </summary>
public class Block : TextElement
{
    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register("TextAlignment", typeof(TextAlignment), typeof(Block),
            new FrameworkPropertyMetadata(TextAlignment.Left));
    public static readonly DependencyProperty FlowDirectionProperty =
        DependencyProperty.Register("FlowDirection", typeof(FlowDirection), typeof(Block),
            new FrameworkPropertyMetadata(FlowDirection.LeftToRight));

    private TextAlignment _textAlignment = TextAlignment.Left;
    private FlowDirection _flowDirection = FlowDirection.LeftToRight;

    /// <summary>Line alignment (default left).</summary>
    public TextAlignment TextAlignment
    {
        get => _textAlignment;
        set { if (_textAlignment == value) return; _textAlignment = value; InvalidateMeasure(); }
    }

    /// <summary>Block flow (default left-to-right).</summary>
    public FlowDirection FlowDirection
    {
        get => _flowDirection;
        set { _flowDirection = value; }
    }

    /// <summary>Host collection (null when unhosted).</summary>
    public BlockCollection? SiblingBlocks => Parent as FlowDocument is { } document ? document.Blocks : null;

    /// <summary>Next block in the host (null at the end).</summary>
    public Block? NextBlock
    {
        get
        {
            var siblings = SiblingBlocks;
            if (siblings is null) return null;
            var index = siblings.IndexOf(this);
            return index >= 0 && index + 1 < siblings.Count ? siblings[index + 1] : null;
        }
    }

    /// <summary>Previous block in the host (null at the start).</summary>
    public Block? PreviousBlock
    {
        get
        {
            var siblings = SiblingBlocks;
            if (siblings is null) return null;
            var index = siblings.IndexOf(this);
            return index > 0 ? siblings[index - 1] : null;
        }
    }
}

/// <summary>
/// Parent-linked content list (matches System.Windows.Documents.TextElementCollection{T}:
/// Add/Clear/Contains/Remove/InsertAfter/InsertBefore/AddRange with live
/// parenting, so hosted inlines/blocks join the logical tree).
/// </summary>
/// <typeparam name="T">Hosted content element.</typeparam>
public class TextElementCollection<T> : IEnumerable<T> where T : TextElement
{
    private readonly List<T> _items = new();
    private readonly Dictionary<T, Action> _textListeners = new();
    private readonly FrameworkElement _owner;

    /// <summary>Hosts content for an owner (mutations re-measure it).</summary>
    public TextElementCollection(FrameworkElement owner) => _owner = owner;

    /// <summary>Hosting element.</summary>
    public FrameworkElement Owner => _owner;

    /// <summary>Fires after any membership or hosted-text change (Hyperlink refold seam).</summary>
    internal event Action? ContentsChanged;

    private void RaiseContentsChanged() => ContentsChanged?.Invoke();

    /// <summary>Hosted count.</summary>
    public int Count => _items.Count;

    /// <summary>Always mutable (matches IsReadOnly=false).</summary>
    public bool IsReadOnly => false;

    /// <summary>Positional read (matches the live-list indexer).</summary>
    public T this[int index] => _items[index];

    private void Watch(T item)
    {
        if (item is Span span && !_textListeners.ContainsKey(item))
        {
            Action refold = OnHostedTextChanged;
            _textListeners[item] = refold;
            span.InlineTextChanged += refold;
        }
    }

    private void OnHostedTextChanged()
    {
        _owner.InvalidateMeasure();
        RaiseContentsChanged();
    }

    private void Unwatch(T item)
    {
        if (_textListeners.Remove(item, out var refold) && item is Span span)
            span.InlineTextChanged -= refold;
    }

    /// <summary>Hosts an element (matches Add).</summary>
    public void Add(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        _owner.AddChild(item);
        Watch(item);
        _owner.InvalidateMeasure();
        RaiseContentsChanged();
    }

    /// <summary>Unhosts every element (matches Clear).</summary>
    public void Clear()
    {
        if (_items.Count == 0) return;
        var removed = _items.ToArray();
        _items.Clear();
        foreach (var item in removed)
        {
            Unwatch(item);
            _owner.RemoveChild(item);
        }
        _owner.InvalidateMeasure();
        RaiseContentsChanged();
    }

    /// <summary>Membership (matches Contains).</summary>
    public bool Contains(T item) => item is not null && _items.Contains(item);

    /// <summary>Copies into an array (matches CopyTo).</summary>
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <summary>Unhosts an element (matches Remove).</summary>
    public bool Remove(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_items.Remove(item)) return false;
        Unwatch(item);
        _owner.RemoveChild(item);
        _owner.InvalidateMeasure();
        RaiseContentsChanged();
        return true;
    }

    /// <summary>Position lookup (matches IndexOf).</summary>
    public int IndexOf(T item) => item is null ? -1 : _items.IndexOf(item);

    /// <summary>Inserts after a hosted element (matches InsertAfter).</summary>
    public void InsertAfter(T pivot, T item)
    {
        ArgumentNullException.ThrowIfNull(pivot);
        ArgumentNullException.ThrowIfNull(item);
        var index = _items.IndexOf(pivot);
        if (index < 0) throw new ArgumentException("The pivot is not in the collection.", nameof(pivot));
        _items.Insert(index + 1, item);
        _owner.AddChild(item);
        _owner.InvalidateMeasure();
        RaiseContentsChanged();
    }

    /// <summary>Inserts before a hosted element (matches InsertBefore).</summary>
    public void InsertBefore(T pivot, T item)
    {
        ArgumentNullException.ThrowIfNull(pivot);
        ArgumentNullException.ThrowIfNull(item);
        var index = _items.IndexOf(pivot);
        if (index < 0) throw new ArgumentException("The pivot is not in the collection.", nameof(pivot));
        _items.Insert(index, item);
        _owner.AddChild(item);
        _owner.InvalidateMeasure();
        RaiseContentsChanged();
    }

    /// <summary>Hosts a range (matches AddRange).</summary>
    public void AddRange(System.Collections.IEnumerable items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            if (item is T element) Add(element);
        }
    }

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Inline host (matches System.Windows.Documents.InlineCollection): runs,
/// links and breaks with first/last navigation and the string shorthand
/// (Add(string) wraps a Run, the DownloadDialog code-behind shape).
/// </summary>
public sealed class InlineCollection : TextElementCollection<Inline>
{
    public InlineCollection(FrameworkElement owner)
        : base(owner)
    {
    }

    /// <summary>First inline (null when empty).</summary>
    public Inline? FirstInline => Count > 0 ? this[0] : null;

    /// <summary>Last inline (null when empty).</summary>
    public Inline? LastInline => Count > 0 ? this[Count - 1] : null;

    /// <summary>Wraps text in a Run (matches Add(string)).</summary>
    public void Add(string text) => Add(new Run(text ?? string.Empty));
}

/// <summary>
/// Block host (matches System.Windows.Documents.BlockCollection) with
/// first/last navigation.
/// </summary>
public sealed class BlockCollection : TextElementCollection<Block>
{
    public BlockCollection(FrameworkElement owner)
        : base(owner)
    {
    }

    /// <summary>First block (null when empty).</summary>
    public Block? FirstBlock => Count > 0 ? this[0] : null;

    /// <summary>Last block (null when empty).</summary>
    public Block? LastBlock => Count > 0 ? this[Count - 1] : null;
}

/// <summary>
/// Text paragraph (matches System.Windows.Documents.Paragraph): inline runs
/// laid out with word wrap and painted with per-run fonts/ink. Child text
/// edits re-fold the layout like the TextBlock path.
/// </summary>
public class Paragraph : Block
{
    public static readonly DependencyProperty KeepTogetherProperty =
        DependencyProperty.Register("KeepTogether", typeof(bool), typeof(Paragraph),
            new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty KeepWithNextProperty =
        DependencyProperty.Register("KeepWithNext", typeof(bool), typeof(Paragraph),
            new FrameworkPropertyMetadata(false));

    private readonly InlineCollection _inlines;
    private bool _keepTogether;
    private bool _keepWithNext;

    public Paragraph() => _inlines = new InlineCollection(this);

    public Paragraph(Inline inline)
        : this()
    {
        ArgumentNullException.ThrowIfNull(inline);
        _inlines.Add(inline);
    }

    /// <summary>Hosted runs/links/breaks.</summary>
    public InlineCollection Inlines => _inlines;

    /// <summary>Widow/orphan control (retained; single-column layout never splits).</summary>
    public bool KeepTogether
    {
        get => _keepTogether;
        set { _keepTogether = value; }
    }

    /// <summary>Keep-with-next (retained; layout never splits blocks).</summary>
    public bool KeepWithNext
    {
        get => _keepWithNext;
        set { _keepWithNext = value; }
    }

    /// <summary>Plain-text projection of the hosted inlines.</summary>
    public string Text => string.Concat(_inlines.Select(InlineText));

    private static string InlineText(Inline inline) => inline switch
    {
        LineBreak => "\n",
        Span span => span.Text,
        _ => string.Empty,
    };

    /// <summary>One laid-out line: runs with x offsets plus its height.</summary>
    internal sealed record LineLayout(IReadOnlyList<RunLayout> Runs, double Width, double Height);

    /// <summary>One laid-out run: source inline, text, font, ink and x offset.</summary>
    internal sealed record RunLayout(Inline Source, string Text, string FontCss, uint Ink, double X, double Width, double Height, bool IsLink);

    /// <summary>Lays out inlines into wrapped lines (single pass, shaped widths).</summary>
    internal List<LineLayout> ComputeLines(double maxWidth)
    {
        var lines = new List<LineLayout>();
        var runs = new List<RunLayout>();
        var width = 0.0;
        var height = 0.0;
        var wrap = double.IsNaN(maxWidth) || double.IsInfinity(maxWidth) ? double.PositiveInfinity : Math.Max(0, maxWidth);

        void Flush()
        {
            if (runs.Count == 0) return;
            lines.Add(new LineLayout(runs.ToArray(), width, height));
            runs = new List<RunLayout>();
            width = 0;
            height = 0;
        }

        foreach (var inline in _inlines)
        {
            if (inline is LineBreak)
            {
                Flush();
                continue;
            }
            var fontCss = inline.ResolveFontCss();
            var ink = inline.ResolveInk(ResolveInk());
            var isLink = inline is Hyperlink;
            var text = InlineText(inline);
            foreach (var piece in SplitWords(text))
            {
                var (pieceW, pieceH) = TryMeasure(piece, fontCss, piece.Length * 8, 16);
                if (runs.Count > 0 && width + pieceW > wrap && !string.IsNullOrWhiteSpace(piece))
                {
                    Flush();
                    if (string.IsNullOrWhiteSpace(piece)) continue;
                }
                if (piece.Length == 0) continue;
                runs.Add(new RunLayout(inline, piece, fontCss, isLink ? LinkInk(inline, ink) : ink, width, pieceW, pieceH, isLink));
                width += pieceW;
                height = Math.Max(height, pieceH);
            }
        }
        Flush();
        if (lines.Count == 0)
        {
            var (_, emptyH) = TryMeasure(" ", ResolveFontCss(), 8, 16);
            lines.Add(new LineLayout([], 0, emptyH));
        }
        return lines;
    }

    private static uint LinkInk(Inline inline, uint fallback) =>
        inline is TextElement element && element.Foreground is SolidColorBrush solid ? solid.ToRgba() : TextBlock.LinkRgba;

    private static IEnumerable<string> SplitWords(string text)
    {
        var current = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                yield return ch.ToString();
            }
            else
            {
                current.Append(ch);
            }
        }
        if (current.Length > 0) yield return current.ToString();
    }

    /// <summary>Link range in paragraph coordinates (union over wrapped words; null when absent).</summary>
    internal WpfRect? GetLinkRect(Hyperlink link)
    {
        var r = RenderRect;
        var contentWidth = Math.Max(0, r.Width - Margin.Left - Margin.Right);
        var lines = ComputeLines(contentWidth);
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;
        var y = r.Y + Margin.Top;
        foreach (var line in lines)
        {
            var x = r.X + Margin.Left + AlignOffset(line.Width, contentWidth);
            foreach (var run in line.Runs)
            {
                if (ReferenceEquals(run.Source, link) && run.Text.Length > 0)
                {
                    left = Math.Min(left, x + run.X);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x + run.X + run.Width);
                    bottom = Math.Max(bottom, y + run.Height);
                }
            }
            y += line.Height;
        }
        return double.IsNegativeInfinity(right)
            ? null
            : new WpfRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var inset = Margin.Left + Margin.Right;
        var lines = ComputeLines(Math.Max(0, available.Width - inset));
        var width = lines.Count == 0 ? 0 : lines.Max(l => l.Width);
        var height = lines.Sum(l => l.Height);
        return new WpfSize(
            Math.Min(available.Width, width + inset),
            height + Margin.Top + Margin.Bottom);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        var lines = ComputeLines(Math.Max(0, r.Width - Margin.Left - Margin.Right));
        var y = r.Y + Margin.Top;
        foreach (var line in lines)
        {
            var x = r.X + Margin.Left + AlignOffset(line.Width, Math.Max(0, r.Width - Margin.Left - Margin.Right));
            string? pendingText = null;
            string pendingFont = string.Empty;
            uint pendingInk = 0;
            double pendingX = 0;
            void FlushPending()
            {
                if (pendingText is null) return;
                dc.DrawText(pendingText, (float)(x + pendingX), (float)y, pendingFont, pendingInk, 0, 1, -1);
                pendingText = null;
            }
            foreach (var run in line.Runs)
            {
                if (run.Text.Length == 0) continue;
                if (run.IsLink)
                {
                    FlushPending();
                    dc.DrawText(run.Text, (float)(x + run.X), (float)y, run.FontCss, run.Ink, 0, 1, -1);
                    dc.DrawLine((float)(x + run.X), (float)(y + run.Height - 2),
                        (float)(x + run.X + run.Width), (float)(y + run.Height - 2), 1, run.Ink);
                    continue;
                }
                if (pendingText is not null && pendingFont == run.FontCss && pendingInk == run.Ink)
                {
                    pendingText += run.Text;
                    continue;
                }
                FlushPending();
                pendingText = run.Text;
                pendingFont = run.FontCss;
                pendingInk = run.Ink;
                pendingX = run.X;
            }
            FlushPending();
            y += line.Height;
        }
    }

    private double AlignOffset(double lineWidth, double contentWidth) => TextAlignment switch
    {
        TextAlignment.Center => Math.Max(0, (contentWidth - lineWidth) / 2),
        TextAlignment.Right => Math.Max(0, contentWidth - lineWidth),
        _ => 0.0,
    };
}

/// <summary>
/// Block document (matches System.Windows.Documents.FlowDocument): block
/// hosting with page padding, stacked single-column layout and paint.
/// Multi-column flow, pagination and text selection stay boundaries.
/// </summary>
public class FlowDocument : FrameworkContentElement, IDocumentFontSource
{
    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register("FontFamily", typeof(FontFamily), typeof(FlowDocument),
            new FrameworkPropertyMetadata(new FontFamily("Segoe UI")));
    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register("FontSize", typeof(double), typeof(FlowDocument),
            new FrameworkPropertyMetadata(12.0));
    public static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.Register("FontStyle", typeof(FontStyle), typeof(FlowDocument),
            new FrameworkPropertyMetadata(FontStyles.Normal));
    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register("FontWeight", typeof(FontWeight), typeof(FlowDocument),
            new FrameworkPropertyMetadata(FontWeights.Normal));
    public static readonly DependencyProperty FontStretchProperty =
        DependencyProperty.Register("FontStretch", typeof(FontStretch), typeof(FlowDocument),
            new FrameworkPropertyMetadata(FontStretches.Normal));
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register("Foreground", typeof(Brush), typeof(FlowDocument),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register("Background", typeof(Brush), typeof(FlowDocument),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register("TextAlignment", typeof(TextAlignment), typeof(FlowDocument),
            new FrameworkPropertyMetadata(TextAlignment.Left));
    public static readonly DependencyProperty FlowDirectionProperty =
        DependencyProperty.Register("FlowDirection", typeof(FlowDirection), typeof(FlowDocument),
            new FrameworkPropertyMetadata(FlowDirection.LeftToRight));
    public static readonly DependencyProperty PagePaddingProperty =
        DependencyProperty.Register("PagePadding", typeof(Thickness), typeof(FlowDocument),
            new FrameworkPropertyMetadata(new Thickness(double.NaN)));

    private readonly BlockCollection _blocks;
    private readonly DocumentFonts _fonts = new();
    private TextAlignment _textAlignment = TextAlignment.Left;
    private FlowDirection _flowDirection = FlowDirection.LeftToRight;
    private Thickness _pagePadding = new(double.NaN);

    public FlowDocument() => _blocks = new BlockCollection(this);

    public FlowDocument(Block block)
        : this()
    {
        ArgumentNullException.ThrowIfNull(block);
        _blocks.Add(block);
    }

    /// <summary>Hosted blocks.</summary>
    public BlockCollection Blocks => _blocks;

    /// <summary>Font family (unset reads Segoe UI).</summary>
    public FontFamily FontFamily
    {
        get => _fonts.Family ?? new FontFamily("Segoe UI");
        set { _fonts.Family = value; InvalidateMeasure(); }
    }

    /// <summary>Font size in pixels (unset reads 12).</summary>
    public double FontSize
    {
        get => _fonts.Size ?? 12.0;
        set { _fonts.Size = value; InvalidateMeasure(); }
    }

    /// <summary>Font slant (unset reads normal).</summary>
    public FontStyle FontStyle
    {
        get => _fonts.Style ?? FontStyles.Normal;
        set { _fonts.Style = value; InvalidateMeasure(); }
    }

    /// <summary>Font weight (unset reads normal).</summary>
    public FontWeight FontWeight
    {
        get => _fonts.Weight ?? FontWeights.Normal;
        set { _fonts.Weight = value; InvalidateMeasure(); }
    }

    /// <summary>Font stretch (unset reads normal).</summary>
    public FontStretch FontStretch
    {
        get => _fonts.Stretch ?? FontStretches.Normal;
        set { _fonts.Stretch = value; InvalidateMeasure(); }
    }

    /// <summary>Ink brush (null keeps the default ink).</summary>
    public Brush? Foreground
    {
        get => _fonts.Foreground;
        set { _fonts.Foreground = value; InvalidateVisual(); }
    }

    /// <summary>Page fill (null keeps transparent).</summary>
    public Brush? Background
    {
        get => _fonts.Background;
        set { _fonts.Background = value; InvalidateVisual(); }
    }

    /// <summary>Default block alignment (default left).</summary>
    public TextAlignment TextAlignment
    {
        get => _textAlignment;
        set { if (_textAlignment == value) return; _textAlignment = value; InvalidateMeasure(); }
    }

    /// <summary>Document flow (default left-to-right).</summary>
    public FlowDirection FlowDirection
    {
        get => _flowDirection;
        set { _flowDirection = value; }
    }

    /// <summary>
    /// Page insets (default Auto/NaN like WPF, which lays out edge to edge).
    /// </summary>
    public Thickness PagePadding
    {
        get => _pagePadding;
        set { if (_pagePadding == value) return; _pagePadding = value; InvalidateMeasure(); }
    }

    private double PadLeft => double.IsNaN(_pagePadding.Left) ? 0 : _pagePadding.Left;
    private double PadTop => double.IsNaN(_pagePadding.Top) ? 0 : _pagePadding.Top;
    private double PadRight => double.IsNaN(_pagePadding.Right) ? 0 : _pagePadding.Right;
    private double PadBottom => double.IsNaN(_pagePadding.Bottom) ? 0 : _pagePadding.Bottom;

    /// <summary>Plain-text projection of the hosted blocks.</summary>
    public string Text => string.Concat(_blocks.Select(b => b is Paragraph paragraph ? paragraph.Text + "\n" : string.Empty));

    /// <summary>Position before the first character (matches ContentStart).</summary>
    public TextPointer ContentStart => new(this, 0);

    /// <summary>Position after the last character (matches ContentEnd).</summary>
    public TextPointer ContentEnd => new(this, Text.Length);

    FontFamily IDocumentFontSource.InheritedFamily => FontFamily;
    double IDocumentFontSource.InheritedSize => FontSize;
    FontStyle IDocumentFontSource.InheritedStyle => FontStyle;
    FontWeight IDocumentFontSource.InheritedWeight => FontWeight;
    FontStretch IDocumentFontSource.InheritedStretch => FontStretch;
    Brush? IDocumentFontSource.InheritedForeground => Foreground;
    Brush? IDocumentFontSource.InheritedBackground => Background;

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var contentWidth = Math.Max(0, available.Width - PadLeft - PadRight);
        var width = 0.0;
        var height = 0.0;
        foreach (var block in _blocks)
        {
            block.Measure(new WpfSize(contentWidth, available.Height));
            width = Math.Max(width, block.DesiredSize.Width);
            height += block.DesiredSize.Height;
        }
        return new WpfSize(
            Math.Min(available.Width, width + PadLeft + PadRight),
            height + PadTop + PadBottom);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        var y = finalRect.Y + PadTop;
        var contentWidth = Math.Max(0, finalRect.Width - PadLeft - PadRight);
        foreach (var block in _blocks)
        {
            block.Arrange(new WpfRect(finalRect.X + PadLeft, y, contentWidth, block.DesiredSize.Height));
            y += block.DesiredSize.Height;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (Background is SolidColorBrush fill && r.Width > 0 && r.Height > 0)
            dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, fill.ToRgba());
        foreach (var block in _blocks) RenderVisible(dc, block);
    }

    private sealed class DocumentFonts
    {
        public FontFamily? Family;
        public double? Size;
        public FontStyle? Style;
        public FontWeight? Weight;
        public FontStretch? Stretch;
        public Brush? Foreground;
        public Brush? Background;
    }
}

/// <summary>
/// Scrollable document host (matches System.Windows.Controls.FlowDocumentScrollViewer):
/// hosts a <see cref="FlowDocument"/> with zoom arithmetic and scrollbar
/// visibility. Toolbars, find and selection stay boundaries.
/// </summary>
public class FlowDocumentScrollViewer : Control
{
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register("Document", typeof(FlowDocument), typeof(FlowDocumentScrollViewer),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register("Zoom", typeof(double), typeof(FlowDocumentScrollViewer),
            new FrameworkPropertyMetadata(100.0));
    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty =
        DependencyProperty.Register("HorizontalScrollBarVisibility", typeof(ScrollBarVisibility), typeof(FlowDocumentScrollViewer),
            new FrameworkPropertyMetadata(ScrollBarVisibility.Auto));
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty =
        DependencyProperty.Register("VerticalScrollBarVisibility", typeof(ScrollBarVisibility), typeof(FlowDocumentScrollViewer),
            new FrameworkPropertyMetadata(ScrollBarVisibility.Visible));

    private FlowDocument? _document;
    private double _zoom = 100.0;
    private ScrollBarVisibility _horizontalScrollBarVisibility = ScrollBarVisibility.Auto;
    private ScrollBarVisibility _verticalScrollBarVisibility = ScrollBarVisibility.Visible;

    /// <summary>Hosted document (setting parents it like WPF).</summary>
    public FlowDocument? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(_document, value)) return;
            _document = value;
            if (value is not null) AddChild(value);
            InvalidateMeasure();
        }
    }

    /// <summary>Zoom percentage (100 is actual size).</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            var clamped = Math.Min(MaxZoom, Math.Max(MinZoom, value));
            if (_zoom == clamped) return;
            _zoom = clamped;
            InvalidateMeasure();
        }
    }

    /// <summary>Zoom ceiling (verified 200).</summary>
    public double MaxZoom { get; set; } = 200.0;

    /// <summary>Zoom floor (verified 80).</summary>
    public double MinZoom { get; set; } = 80.0;

    /// <summary>Zoom step in points (verified 10; steps add like WPF).</summary>
    public double ZoomIncrement { get; set; } = 10.0;

    /// <summary>Whether zooming in stays possible.</summary>
    public bool CanIncreaseZoom => Zoom < MaxZoom;

    /// <summary>Whether zooming out stays possible.</summary>
    public bool CanDecreaseZoom => Zoom > MinZoom;

    /// <summary>Zooms in one step (matches IncreaseZoom).</summary>
    public void IncreaseZoom() => Zoom += ZoomIncrement;

    /// <summary>Zooms out one step (matches DecreaseZoom).</summary>
    public void DecreaseZoom() => Zoom -= ZoomIncrement;

    /// <summary>Selection support (retained; selection stays a boundary).</summary>
    public bool IsSelectionEnabled { get; set; } = true;

    /// <summary>Toolbar visibility (verified default hidden; toolbars stay a boundary).</summary>
    public bool IsToolBarVisible { get; set; }

    /// <summary>Horizontal scrollbar rule (retained; scrolling stays a boundary).</summary>
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => _horizontalScrollBarVisibility;
        set { _horizontalScrollBarVisibility = value; }
    }

    /// <summary>Vertical scrollbar rule (retained; scrolling stays a boundary).</summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => _verticalScrollBarVisibility;
        set { _verticalScrollBarVisibility = value; }
    }

    /// <summary>Selection ink (retained).</summary>
    public Brush? SelectionBrush { get; set; }

    /// <summary>Selection opacity (retained).</summary>
    public double SelectionOpacity { get; set; } = 0.4;

    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (_document is null) return new WpfSize(0, 0);
        _document.Measure(available);
        return _document.DesiredSize;
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        _document?.Arrange(finalRect);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_document is not null) RenderVisible(dc, _document);
    }
}

/// <summary>
/// Document position (matches System.Windows.Documents.TextPointer): a
/// (document, char-offset) pair over the engine's linear
/// <see cref="FlowDocument.Text"/> projection. Offsets clamp into range.
/// Retained limit: WPF symbol offsets (embedded elements count as one
/// symbol) are not modeled; here one char is one step.
/// </summary>
public sealed class TextPointer : IEquatable<TextPointer>
{
    /// <summary>Positions the pointer (offset clamps into the document text).</summary>
    internal TextPointer(FlowDocument document, int offset)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Offset = Math.Clamp(offset, 0, document.Text.Length);
    }

    /// <summary>Owning document.</summary>
    public FlowDocument Document { get; }

    /// <summary>Char offset into <see cref="FlowDocument.Text"/>.</summary>
    public int Offset { get; }

    /// <summary>Pointer moved by a relative offset (clamped).</summary>
    public TextPointer GetPositionAtOffset(int offset) => new(Document, Offset + offset);

    /// <summary>Offset of another pointer in the same document.</summary>
    public int GetOffsetToPosition(TextPointer position)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (!ReferenceEquals(Document, position.Document))
            throw new ArgumentException("TextPointers belong to different documents.", nameof(position));
        return position.Offset - Offset;
    }

    /// <inheritdoc />
    public bool Equals(TextPointer? other) =>
        other is not null && ReferenceEquals(Document, other.Document) && Offset == other.Offset;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TextPointer other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Document, Offset);

    /// <summary>Pointer equality (same document, same offset).</summary>
    public static bool operator ==(TextPointer? left, TextPointer? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Pointer inequality.</summary>
    public static bool operator !=(TextPointer? left, TextPointer? right) => !(left == right);
}

/// <summary>
/// Document range (matches System.Windows.Documents.TextRange): a
/// start/end pair over one document with sliced text. The text setter is
/// retained (no in-place document editing in v1); reading slices the
/// live <see cref="FlowDocument.Text"/>.
/// </summary>
public class TextRange
{
    private TextPointer _start;
    private TextPointer _end;

    /// <summary>Ranges two positions (nulls rejected; documents must match, like WPF).</summary>
    public TextRange(TextPointer start, TextPointer end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        if (!ReferenceEquals(start.Document, end.Document))
            throw new ArgumentException("TextRange endpoints belong to different documents.");
        _start = start;
        _end = end;
    }

    /// <summary>Range start (the earlier anchor after <see cref="TextSelection.Select"/>).</summary>
    public TextPointer Start => _start;

    /// <summary>Range end.</summary>
    public TextPointer End => _end;

    /// <summary>Whether the range covers no characters.</summary>
    public bool IsEmpty => _start.Offset == _end.Offset;

    /// <summary>
    /// Covered text (empty when reversed or empty; sliced from the live
    /// document text so document edits show through).
    /// </summary>
    public string Text
    {
        get
        {
            var text = _start.Document.Text;
            var from = Math.Min(_start.Offset, _end.Offset);
            var to = Math.Max(_start.Offset, _end.Offset);
            from = Math.Clamp(from, 0, text.Length);
            to = Math.Clamp(to, 0, text.Length);
            return text.Substring(from, to - from);
        }
    }

    /// <summary>Re-anchors both ends (the selection primitive).</summary>
    protected void SelectCore(TextPointer start, TextPointer end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        if (!ReferenceEquals(_start.Document, start.Document) || !ReferenceEquals(_start.Document, end.Document))
            throw new ArgumentException("TextRange endpoints belong to different documents.");
        _start = start;
        _end = end;
    }
}

/// <summary>
/// Document selection (matches System.Windows.Documents.TextSelection):
/// a live range whose Select re-anchors both ends. The converted
/// MaterialDesign reveal behavior carries selections as values and
/// reflects on the PasswordBox side, so the type plus the range contract
/// is what its closure binds.
/// </summary>
public class TextSelection : TextRange
{
    /// <summary>Selects a range (live-verified shape).</summary>
    public TextSelection(TextPointer start, TextPointer end) : base(start, end)
    {
    }

    /// <summary>Re-anchors the selection to a new range.</summary>
    public void Select(TextPointer start, TextPointer end) => SelectCore(start, end);
}
