namespace DesktopWasm.Wpf;

// Gallery Text/RichTextEditPage slice (slice-39): the detail page hosts a
// bare <RichTextBox/>, which the loader rejected as an unknown element. This
// is a shim, not a port: a TextBoxBase editor over a real hosted
// FlowDocument (the slice-37 document engine), so paragraphs, runs, links
// and breaks keep their layout and paint while typing, caret, selection and
// Enter-splitting are real.
//
// Retained boundaries (documented, precise): the RichTextBox.Selection
// property (caret and selection stay int offsets over the flattened text;
// TextPointer/TextRange/TextSelection themselves now exist in Documents.cs),
// TextChanged is the TextBoxBase event, undo/redo, spellcheck, drag
// selection, IME composition, templates and scrolling chrome. A hyperlink
// with hosted children counts as one atomic unit for edits (inserts land
// outside it, covering deletes take it whole) instead of splitting its fold.
public sealed class RichTextBox : TextBoxBase
{
    private const double Pad = 4;
    private const uint BorderRgba = 0x707070FF;
    private const uint FillRgba = 0xFFFFFFFF;
    private const uint CaretRgba = 0x000000FF;

    /// <summary>Document slot (matches RichTextBox.DocumentProperty).</summary>
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register("Document", typeof(FlowDocument), typeof(RichTextBox),
            new FrameworkPropertyMetadata(null));

    private FlowDocument _document;
    private bool _focused;
    private int _selectionStart;
    private int _selectionLength;

    /// <summary>Builds the editor around an empty document (one blank paragraph like WPF).</summary>
    public RichTextBox()
    {
        Focusable = true;
        AcceptsReturn = true;
        _document = new FlowDocument(new Paragraph());
        AddChild(_document);
    }

    /// <summary>Hosted document (setting parents it like WPF; null throws).</summary>
    public FlowDocument Document
    {
        get => _document;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_document, value)) return;
            RemoveChild(_document);
            _document = value;
            AddChild(_document);
            CollapseTo(0);
            InvalidateMeasure();
        }
    }

    /// <summary>Tab inserts a tab (retained; parsed and stored like WPF).</summary>
    public bool AcceptsTab { get; set; }

    /// <summary>Plain-text projection (paragraphs joined with \n; the STATE/testing seam).</summary>
    public string PlainText
    {
        get
        {
            var paragraphs = LiveParagraphs();
            return string.Join("\n", paragraphs.Select(static p => p.Text));
        }
    }

    /// <summary>Insertion point as a flattened-text offset (collapses the selection like WPF).</summary>
    public int CaretIndex
    {
        get => SelectionStart + SelectionLength;
        set => CollapseTo(value);
    }

    /// <summary>Selection anchor (clamped to the text).</summary>
    public int SelectionStart
    {
        get => _selectionStart;
        set
        {
            var clamped = Math.Clamp(value, 0, FullLength());
            if (_selectionStart == clamped) return;
            _selectionStart = clamped;
            SelectionLength = Math.Min(SelectionLength, FullLength() - _selectionStart);
            InvalidateVisual();
        }
    }

    /// <summary>Selected length from the anchor (clamped to the text).</summary>
    public int SelectionLength
    {
        get => _selectionLength;
        set
        {
            var clamped = Math.Clamp(value, 0, FullLength() - SelectionStart);
            if (_selectionLength == clamped) return;
            _selectionLength = clamped;
            InvalidateVisual();
        }
    }

    /// <summary>Selected text (setting it replaces the selection).</summary>
    public string SelectedText
    {
        get => SelectionLength == 0 ? string.Empty : PlainText.Substring(SelectionStart, SelectionLength);
        set
        {
            if (IsReadOnly) return;
            var start = SelectionStart;
            DeleteSelection();
            InsertTextAt(start, value ?? string.Empty);
            CollapseTo(start + (value?.Length ?? 0));
        }
    }

    /// <summary>Selects the whole document (allowed on read-only boxes, like real WPF).</summary>
    public override void SelectAll()
    {
        SelectionStart = 0;
        SelectionLength = FullLength();
    }

    /// <summary>Whether the caret is currently shown (mirrors the focus gate).</summary>
    public override bool IsFocused => _focused;

    public override void OnGotFocus()
    {
        _focused = true;
        InvalidateVisual();
    }

    public override void OnLostFocus()
    {
        _focused = false;
        InvalidateVisual();
    }

    private void CollapseTo(int offset)
    {
        var clamped = Math.Clamp(offset, 0, FullLength());
        _selectionStart = clamped;
        _selectionLength = 0;
        InvalidateVisual();
    }

    private List<Paragraph> LiveParagraphs()
    {
        var found = new List<Paragraph>();
        foreach (var block in _document.Blocks)
        {
            if (block is Paragraph paragraph) found.Add(paragraph);
        }
        if (found.Count == 0)
        {
            var fresh = new Paragraph();
            _document.Blocks.Add(fresh);
            found.Add(fresh);
        }
        return found;
    }

    private int FullLength()
    {
        var paragraphs = LiveParagraphs();
        var length = 0;
        for (var i = 0; i < paragraphs.Count; i++)
        {
            if (i > 0) length += 1;
            length += paragraphs[i].Text.Length;
        }
        return length;
    }

    private (int Para, int Inner) Locate(int global)
    {
        var paragraphs = LiveParagraphs();
        global = Math.Clamp(global, 0, FullLength());
        var pos = 0;
        for (var i = 0; i < paragraphs.Count; i++)
        {
            if (i > 0)
            {
                if (global == pos) return (i - 1, paragraphs[i - 1].Text.Length);
                pos += 1;
            }
            var text = paragraphs[i].Text;
            if (global <= pos + text.Length) return (i, global - pos);
            pos += text.Length;
        }
        var last = paragraphs.Count - 1;
        return (last, paragraphs[last].Text.Length);
    }

    private sealed record TextHolder(Inline Inline, int Start, int Length, bool Atomic);

    private static List<TextHolder> Holders(Paragraph paragraph)
    {
        var holders = new List<TextHolder>();
        var offset = 0;
        foreach (var inline in paragraph.Inlines)
        {
            var atomic = inline is LineBreak || (inline is Hyperlink link && link.Inlines.Count > 0);
            var length = inline switch
            {
                LineBreak => 1,
                Hyperlink hosted when hosted.Inlines.Count > 0 => hosted.Text.Length,
                Span span => span.Text.Length,
                _ => 0,
            };
            holders.Add(new TextHolder(inline, offset, length, atomic));
            offset += length;
        }
        return holders;
    }

    private static void SetHolderText(TextHolder holder, string text)
    {
        if (holder.Inline is Span span) span.Text = text;
    }

    private static string HolderText(TextHolder holder) =>
        holder.Inline is Span span ? span.Text : string.Empty;

    private static void MoveInline(Paragraph from, Paragraph to, Inline inline)
    {
        from.Inlines.Remove(inline);
        to.Inlines.Add(inline);
    }

    private void InsertTextAt(int global, string text)
    {
        if (text.Length == 0) return;
        var paragraphs = LiveParagraphs();
        var (para, inner) = Locate(global);
        InsertInto(paragraphs[para], inner, text);
        OnTextChanged(new TextChangedEventArgs(TextChangedEvent, UndoAction.None));
    }

    private void InsertInto(Paragraph paragraph, int inner, string text)
    {
        var holders = Holders(paragraph);
        if (holders.Count == 0)
        {
            paragraph.Inlines.Add(new Run(text));
            InvalidateMeasure();
            return;
        }
        var total = holders[^1].Start + holders[^1].Length;
        if (inner >= total)
        {
            var last = holders[^1];
            if (!last.Atomic) SetHolderText(last, HolderText(last) + text);
            else paragraph.Inlines.Add(new Run(text));
            InvalidateMeasure();
            return;
        }
        foreach (var holder in holders)
        {
            if (inner < holder.Start || inner > holder.Start + holder.Length) continue;
            if (!holder.Atomic)
            {
                var current = HolderText(holder);
                var at = Math.Clamp(inner - holder.Start, 0, current.Length);
                SetHolderText(holder, current.Insert(at, text));
                InvalidateMeasure();
                return;
            }
            InsertInto(paragraph, holder.Inline is LineBreak ? holder.Start : holder.Start + holder.Length, text);
            return;
        }
        paragraph.Inlines.Add(new Run(text));
        InvalidateMeasure();
    }

    private void DeleteInnerRange(Paragraph paragraph, int start, int length)
    {
        if (length <= 0) return;
        var end = start + length;
        foreach (var holder in Holders(paragraph))
        {
            var holderEnd = holder.Start + holder.Length;
            if (holderEnd <= start || holder.Start >= end) continue;
            if (holder.Atomic)
            {
                paragraph.Inlines.Remove(holder.Inline);
                continue;
            }
            var current = HolderText(holder);
            var from = Math.Clamp(start - holder.Start, 0, current.Length);
            var to = Math.Clamp(end - holder.Start, 0, current.Length);
            if (to > from) SetHolderText(holder, current.Remove(from, to - from));
        }
        InvalidateMeasure();
    }

    private void MergeNextInto(Paragraph first, Paragraph next)
    {
        foreach (var inline in next.Inlines.ToList()) MoveInline(next, first, inline);
        _document.Blocks.Remove(next);
        InvalidateMeasure();
    }

    private void SplitParagraph(int paraIndex, int inner)
    {
        var paragraphs = LiveParagraphs();
        if (paraIndex < 0 || paraIndex >= paragraphs.Count) return;
        var current = paragraphs[paraIndex];
        var holders = Holders(current);
        var split = new Paragraph();
        var boundary = holders.FindIndex(h => inner > h.Start && inner < h.Start + h.Length);
        if (boundary >= 0 && !holders[boundary].Atomic)
        {
            var holder = holders[boundary];
            var text = HolderText(holder);
            var at = Math.Clamp(inner - holder.Start, 0, text.Length);
            SetHolderText(holder, text[..at]);
            var moved = new Run(text[at..]);
            CopyFont(holder.Inline, moved);
            split.Inlines.Add(moved);
            foreach (var rest in holders.Skip(boundary + 1).ToList()) MoveInline(current, split, rest.Inline);
        }
        else
        {
            foreach (var rest in holders.Where(h => h.Start >= inner).ToList()) MoveInline(current, split, rest.Inline);
        }
        _document.Blocks.InsertAfter(current, split);
        InvalidateMeasure();
    }

    private static void CopyFont(Inline source, Span target)
    {
        target.FontFamily = source.FontFamily;
        target.FontSize = source.FontSize;
        target.FontStyle = source.FontStyle;
        target.FontWeight = source.FontWeight;
        target.FontStretch = source.FontStretch;
        target.Foreground = source.Foreground;
        target.Background = source.Background;
    }

    /// <summary>Deletes the selection, collapsing the caret to its anchor.</summary>
    private bool DeleteSelection()
    {
        if (SelectionLength == 0) return false;
        var paragraphs = LiveParagraphs();
        var (firstPara, firstInner) = Locate(SelectionStart);
        var (lastPara, lastInner) = Locate(SelectionStart + SelectionLength);
        var first = paragraphs[firstPara];
        var last = paragraphs[lastPara];
        if (firstPara == lastPara)
        {
            DeleteInnerRange(first, firstInner, SelectionLength);
        }
        else
        {
            DeleteInnerRange(first, firstInner, first.Text.Length - firstInner);
            for (var i = lastPara - 1; i > firstPara; i--) _document.Blocks.Remove(paragraphs[i]);
            DeleteInnerRange(last, 0, lastInner);
            MergeNextInto(first, last);
        }
        CollapseTo(SelectionStart);
        OnTextChanged(new TextChangedEventArgs(TextChangedEvent, UndoAction.None));
        return true;
    }

    private void Backspace()
    {
        if (IsReadOnly) return;
        if (DeleteSelection()) return;
        var at = CaretIndex;
        if (at == 0) return;
        var paragraphs = LiveParagraphs();
        var (para, inner) = Locate(at);
        if (inner > 0)
        {
            DeleteInnerRange(paragraphs[para], inner - 1, 1);
            CollapseTo(at - 1);
        }
        else
        {
            MergeNextInto(paragraphs[para - 1], paragraphs[para]);
            CollapseTo(at - 1);
        }
        OnTextChanged(new TextChangedEventArgs(TextChangedEvent, UndoAction.None));
    }

    private void DeleteForward()
    {
        if (IsReadOnly) return;
        if (DeleteSelection()) return;
        var paragraphs = LiveParagraphs();
        var (para, inner) = Locate(CaretIndex);
        if (inner < paragraphs[para].Text.Length)
        {
            DeleteInnerRange(paragraphs[para], inner, 1);
            OnTextChanged(new TextChangedEventArgs(TextChangedEvent, UndoAction.None));
        }
        else if (para + 1 < paragraphs.Count)
        {
            MergeNextInto(paragraphs[para], paragraphs[para + 1]);
            OnTextChanged(new TextChangedEventArgs(TextChangedEvent, UndoAction.None));
        }
    }

    private void SplitAtCaret()
    {
        if (IsReadOnly || !AcceptsReturn) return;
        DeleteSelection();
        var at = CaretIndex;
        var paragraphs = LiveParagraphs();
        var (para, inner) = Locate(at);
        SplitParagraph(para, inner);
        var caret = 0;
        var fresh = LiveParagraphs();
        for (var i = 0; i <= para && i < fresh.Count; i++) caret += fresh[i].Text.Length + 1;
        CollapseTo(caret);
        OnTextChanged(new TextChangedEventArgs(TextChangedEvent, UndoAction.None));
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var contentWidth = Math.Max(0, available.Width - Pad * 2);
        _document.Measure(new WpfSize(contentWidth, available.Height));
        return new(
            double.IsNaN(Width) ? Math.Min(available.Width, Math.Max(40, _document.DesiredSize.Width + Pad * 2)) : Width,
            double.IsNaN(Height) ? Math.Max(28, _document.DesiredSize.Height + Pad * 2) : Height);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        _document.Arrange(new WpfRect(
            finalRect.X + Pad, finalRect.Y + Pad,
            Math.Max(0, finalRect.Width - Pad * 2), Math.Max(0, finalRect.Height - Pad * 2)));
    }

    private sealed record LineHit(
        IReadOnlyList<Paragraph.RunLayout> Runs, double Y, double Height, double BaseX, List<int> Breaks);

    private List<(Paragraph Paragraph, int GlobalStart, List<LineHit> Lines)> LaidOutLines()
    {
        var result = new List<(Paragraph, int, List<LineHit>)>();
        var global = 0;
        foreach (var paragraph in LiveParagraphs())
        {
            var r = paragraph.RenderRect;
            var contentWidth = Math.Max(0, r.Width - paragraph.Margin.Left - paragraph.Margin.Right);
            var lines = paragraph.ComputeLines(contentWidth);
            var breaks = LineBreakOffsets(paragraph);
            var hits = new List<LineHit>();
            var y = r.Y + paragraph.Margin.Top;
            foreach (var line in lines)
            {
                var align = paragraph.TextAlignment switch
                {
                    TextAlignment.Center => Math.Max(0, contentWidth - line.Width) / 2,
                    TextAlignment.Right => Math.Max(0, contentWidth - line.Width),
                    _ => 0.0,
                };
                hits.Add(new LineHit(line.Runs, y, line.Height,
                    r.X + paragraph.Margin.Left + align, breaks));
                y += line.Height;
            }
            result.Add((paragraph, global, hits));
            global += paragraph.Text.Length + 1;
        }
        return result;
    }

    private static List<int> LineBreakOffsets(Paragraph paragraph)
    {
        var breaks = new List<int>();
        var offset = 0;
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is LineBreak) breaks.Add(offset);
            offset += inline switch
            {
                LineBreak => 1,
                Hyperlink hosted when hosted.Inlines.Count > 0 => hosted.Text.Length,
                Span span => span.Text.Length,
                _ => 0,
            };
        }
        return breaks;
    }

    private static int MapOffset(int noBreakOffset, List<int> breaks)
    {
        var mapped = noBreakOffset;
        foreach (var b in breaks)
        {
            if (b <= mapped) mapped++;
        }
        return mapped;
    }

    private (double X, double Y, double Height) CaretPosition(int global)
    {
        foreach (var (paragraph, globalStart, lines) in LaidOutLines())
        {
            var inner = global - globalStart;
            if (inner < 0 || inner > paragraph.Text.Length) continue;
            foreach (var line in lines)
            {
                var noBreak = 0;
                foreach (var piece in line.Runs)
                {
                    var mappedStart = MapOffset(noBreak, line.Breaks);
                    var mappedEnd = mappedStart + piece.Text.Length;
                    if (inner >= mappedStart && inner <= mappedEnd)
                    {
                        var prefix = piece.Text[..Math.Clamp(inner - mappedStart, 0, piece.Text.Length)];
                        var (prefixW, _) = TryMeasure(prefix, piece.FontCss, prefix.Length * 8, 16);
                        return (line.BaseX + piece.X + prefixW, line.Y, piece.Height);
                    }
                    noBreak += piece.Text.Length;
                }
            }
            var last = lines[^1];
            return (last.BaseX, last.Y, last.Height);
        }
        return (RenderRect.X + Pad, RenderRect.Y + Pad, 16);
    }

    private int HitToCaret(WpfPoint point)
    {
        var bestGlobal = -1;
        var bestInner = 0;
        var bestDistance = double.PositiveInfinity;
        foreach (var (paragraph, globalStart, lines) in LaidOutLines())
        {
            foreach (var line in lines)
            {
                var dy = point.Y < line.Y ? line.Y - point.Y
                    : point.Y > line.Y + line.Height ? point.Y - (line.Y + line.Height) : 0;
                var noBreak = 0;
                for (var i = 0; i < line.Runs.Count; i++)
                {
                    var piece = line.Runs[i];
                    var mappedStart = MapOffset(noBreak, line.Breaks);
                    var left = line.BaseX + piece.X;
                    var right = left + piece.Width;
                    double dx;
                    int chars;
                    if (point.X <= left) { dx = left - point.X; chars = 0; }
                    else if (point.X >= right) { dx = point.X - right; chars = piece.Text.Length; }
                    else
                    {
                        dx = 0;
                        chars = SnapChars(piece, point.X - left);
                    }
                    var distance = dy * 4 + dx;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestGlobal = globalStart;
                        bestInner = mappedStart + chars;
                    }
                    noBreak += piece.Text.Length;
                }
                if (line.Runs.Count == 0 && dy < bestDistance)
                {
                    bestDistance = dy;
                    bestGlobal = globalStart;
                    bestInner = MapOffset(noBreak, line.Breaks);
                }
            }
        }
        return bestGlobal < 0 ? FullLength() : Math.Clamp(bestGlobal + bestInner, 0, FullLength());
    }

    private static int SnapChars(Paragraph.RunLayout piece, double dx)
    {
        var low = 0;
        var high = piece.Text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            var (w, _) = TryMeasure(piece.Text[..mid], piece.FontCss, mid * 8, 16);
            if (w <= dx) low = mid;
            else high = mid - 1;
        }
        if (low < piece.Text.Length)
        {
            var (w0, _) = TryMeasure(piece.Text[..(low + 1)], piece.FontCss, (low + 1) * 8, 16);
            var (w1, _) = TryMeasure(piece.Text[..low], piece.FontCss, low * 8, 16);
            if (dx - w1 > w0 - dx) low++;
        }
        return low;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height, BorderRgba);
        if (r.Width > 2 && r.Height > 2)
            dc.DrawRectangle((float)r.X + 1, (float)r.Y + 1, (float)r.Width - 2, (float)r.Height - 2, FillRgba);
        if (r.Width <= 2 || r.Height <= 2) return;
        PaintSelection(dc);
        RenderVisible(dc, _document);
        if (_focused && SelectionLength == 0)
        {
            var (x, y, height) = CaretPosition(CaretIndex);
            dc.DrawRectangle((float)x, (float)y, 1, (float)height, CaretRgba);
        }
    }

    private void PaintSelection(DrawingContext dc)
    {
        if (SelectionLength == 0) return;
        var start = SelectionStart;
        var end = SelectionStart + SelectionLength;
        var highlight = SystemColors.HighlightBrush.ToRgba();
        foreach (var (paragraph, globalStart, lines) in LaidOutLines())
        {
            var paraStart = globalStart;
            var paraEnd = globalStart + paragraph.Text.Length;
            if (paraEnd <= start || paraStart >= end) continue;
            foreach (var line in lines)
            {
                var noBreak = 0;
                double? fromX = null;
                var toX = 0.0;
                foreach (var piece in line.Runs)
                {
                    var mappedStart = MapOffset(noBreak, line.Breaks) + globalStart;
                    var mappedEnd = mappedStart + piece.Text.Length;
                    var overlapStart = Math.Max(mappedStart, start);
                    var overlapEnd = Math.Min(mappedEnd, end);
                    if (overlapEnd > overlapStart)
                    {
                        var headLength = Math.Clamp(overlapStart - mappedStart, 0, piece.Text.Length);
                        var coverLength = Math.Clamp(overlapEnd - overlapStart, 0, piece.Text.Length - headLength);
                        var (headW, _) = TryMeasure(piece.Text[..headLength], piece.FontCss, headLength * 8, 16);
                        var (coverW, _) = TryMeasure(piece.Text.Substring(headLength, coverLength), piece.FontCss, Math.Max(1, coverLength * 8), 16);
                        var x = line.BaseX + piece.X + headW;
                        fromX ??= x;
                        toX = x + Math.Max(1, coverW);
                    }
                    noBreak += piece.Text.Length;
                }
                if (fromX is { } fx)
                    dc.DrawRectangle((float)fx, (float)line.Y, (float)Math.Max(1, toX - fx), (float)line.Height, highlight);
            }
        }
    }

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        if (e.Name == WasmRoutedEvent.PointerDownName && e.IsLeftButtonDown &&
            e.Source is Core.WasmInputEvent.Pointer down)
        {
            Focus();
            CollapseTo(HitToCaret(new WpfPoint(down.Args.X, down.Args.Y)));
            return;
        }
        if (e.Name != WasmRoutedEvent.KeyDownName || !_focused) return;
        if (e.Source is not Core.WasmInputEvent.Key k || !k.Args.IsDown) return;
        switch (k.Args.Key)
        {
            case "Backspace":
                Backspace();
                e.Handled = true;
                break;
            case "Delete":
                DeleteForward();
                e.Handled = true;
                break;
            case "Enter":
                if (AcceptsReturn) { SplitAtCaret(); e.Handled = true; }
                break;
            case "Tab":
                if (AcceptsTab)
                {
                    DeleteSelection();
                    var tabAt = CaretIndex;
                    InsertTextAt(tabAt, "\t");
                    CollapseTo(tabAt + 1);
                    e.Handled = true;
                }
                break;
            case "ArrowLeft":
                CollapseTo(SelectionLength > 0 ? SelectionStart : CaretIndex - 1);
                e.Handled = true;
                break;
            case "ArrowRight":
                CollapseTo(SelectionLength > 0 ? SelectionStart + SelectionLength : CaretIndex + 1);
                e.Handled = true;
                break;
            case "ArrowUp":
                MoveVertical(-1);
                e.Handled = true;
                break;
            case "ArrowDown":
                MoveVertical(1);
                e.Handled = true;
                break;
            case "Home":
                MoveToBoundary(start: true);
                e.Handled = true;
                break;
            case "End":
                MoveToBoundary(start: false);
                e.Handled = true;
                break;
            default:
                var key = k.Args.Key;
                if (key.Length == 1 && (key == "a" || key == "A") && k.Args.Ctrl && !k.Args.Alt)
                {
                    SelectAll();
                    e.Handled = true;
                }
                else if (key.Length == 1 && !k.Args.Ctrl && !k.Args.Alt && !IsReadOnly)
                {
                    DeleteSelection();
                    var at = CaretIndex;
                    InsertTextAt(at, key);
                    CollapseTo(at + key.Length);
                    e.Handled = true;
                }
                break;
        }
    }

    private void MoveVertical(int direction)
    {
        var paragraphs = LiveParagraphs();
        var (para, _) = Locate(CaretIndex);
        var next = Math.Clamp(para + direction, 0, paragraphs.Count - 1);
        if (next == para)
        {
            CollapseTo(direction < 0 ? 0 : FullLength());
            return;
        }
        var caret = 0;
        for (var i = 0; i < next; i++) caret += paragraphs[i].Text.Length + 1;
        if (direction > 0) caret += paragraphs[next].Text.Length;
        CollapseTo(caret);
    }

    private void MoveToBoundary(bool start)
    {
        var paragraphs = LiveParagraphs();
        var (para, _) = Locate(CaretIndex);
        var caret = 0;
        for (var i = 0; i < para; i++) caret += paragraphs[i].Text.Length + 1;
        CollapseTo(start ? caret : caret + paragraphs[para].Text.Length);
    }
}
