extern alias Fork;

using ForkRtf = Fork::System.Windows.Forms.RichTextBox.WasmRtf;

namespace WfWasm.WinForms.Tests;

// Pins the headless EDITSTREAM replacement (rev77): the desktop
// StreamIn/StreamOut drive native RichEdit through
// Marshal.GetFunctionPointerForDelegate, whose thunk traps the wasm
// interpreter with "memory access out of bounds" and no managed frame
// (Syncfusion diagram RichTextNode boot). The byte/text mapping below is
// pure so it pins without constructing a RichTextBox (desktop demands
// MsftEdit for one; headless has none); the thin box glue
// (StreamInManaged/StreamOutManaged) is covered by the rewrite-rule test
// plus the browser run.
public sealed class WasmRtfTests
{
    private const uint SF_RTF = 0x0002;
    private const uint SF_TEXT = 0x0001;
    private const uint SF_UNICODE = 0x0010;

    // The exact NodesDemo RichTextNode payload (ldstr from the demo).
    private const string DemoRtf =
        "{\\rtf1\\ansi\\ansicpg1252\\deff0\\deflang1033{\\fonttbl{\\f0\\fnil\\fcharset0 Meiryo UI;}{\\f1\\fnil\\fcharset0 Microsoft Sans Serif;}}\r\n" +
        "{\\colortbl ;\\red255\\green0\\blue0;\\red0\\green64\\blue128;}\r\n" +
        "\\viewkind4\\uc1\\pard\\b\\f0\\fs17 This is a text symbol\\f1  \\b0\\f0 designed using the \\cf1\\b\\i Essential Diagram\\i0  \\cf2\\ul RichText\\ulnone  node\\cf0\\b0 .\\par\r\n" +
        "\\i\\f1\\fs20\\par\r\n}";

    // Single spaces throughout: each control word eats exactly one delimiter
    // space ({\i0␣␣\cf2} and {\ulnone␣␣node} keep one text space each).
    private const string DemoPlain =
        "This is a text symbol designed using the Essential Diagram RichText node.\n\n";

    [Fact]
    public void DemoRtf_ExtractsPlainText()
    {
        Assert.Equal(DemoPlain, ForkRtf.RtfToPlainText(DemoRtf));
    }

    [Fact]
    public void DemoRtf_SkipsMetadataTables()
    {
        string plain = ForkRtf.RtfToPlainText(DemoRtf);
        Assert.DoesNotContain("Meiryo", plain);
        Assert.DoesNotContain("Sans Serif", plain);
        Assert.DoesNotContain("red255", plain);
        Assert.DoesNotContain("fonttbl", plain);
        Assert.Contains("Essential Diagram", plain);
        Assert.Contains("RichText", plain);
    }

    [Fact]
    public void ControlWords_MapToWhitespace()
    {
        Assert.Equal("a\nb\nc\td", ForkRtf.RtfToPlainText("{\\rtf1 a\\par b\\line c\\tab d}"));
    }

    [Fact]
    public void HexEscape_DecodesByte()
    {
        Assert.Equal("A", ForkRtf.RtfToPlainText("{\\rtf1\\ansi \\'41}"));
    }

    [Fact]
    public void UnicodeEscape_SkipsFallbackChars()
    {
        Assert.Equal("\u03B1x", ForkRtf.RtfToPlainText("{\\rtf1\\ansi\\uc1\\u945?x}"));
    }

    [Fact]
    public void Literals_Unescape()
    {
        Assert.Equal(@"A{B}C\D", ForkRtf.RtfToPlainText(@"{\rtf1 A\{B\}C\\D}"));
    }

    [Fact]
    public void EscapedSourceBreaks_CarryNoContent()
    {
        Assert.Equal("ab", ForkRtf.RtfToPlainText("{\\rtf1 a\\\r\nb}"));
    }

    [Fact]
    public void ComputeStreamText_Rtf_YieldsDemoPlain()
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(DemoRtf);
        Assert.Equal(DemoPlain, ForkRtf.ComputeStreamText(bytes, SF_RTF));
    }

    [Fact]
    public void ComputeStreamText_Plain_PassesThrough()
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("hello");
        Assert.Equal("hello", ForkRtf.ComputeStreamText(bytes, SF_TEXT));
    }

    [Fact]
    public void ComputeStreamText_Unicode_DecodesUtf16()
    {
        byte[] bytes = System.Text.Encoding.Unicode.GetBytes("hi");
        Assert.Equal("hi", ForkRtf.ComputeStreamText(bytes, SF_TEXT | SF_UNICODE));
    }

    [Fact]
    public void ComputeStreamText_Rtf_RejectsMissingTag()
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes("not rtf");
        Assert.Throws<ArgumentException>(() => ForkRtf.ComputeStreamText(bytes, SF_RTF));
    }

    [Fact]
    public void ComputeStreamPayload_Text_WritesBytes()
    {
        byte[] bytes = ForkRtf.ComputeStreamPayload("hi", SF_TEXT, includeCrLfs: false);
        Assert.Equal("hi", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void ComputeStreamPayload_Crlf_NormalizesEndings()
    {
        byte[] bytes = ForkRtf.ComputeStreamPayload("a\nb", SF_TEXT, includeCrLfs: true);
        Assert.Equal("a\r\nb", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void BuildFormat_Wrap_TogglesNoWrap()
    {
        var wrapped = Fork::System.Windows.Forms.WasmRtfDraw.BuildFormat(wordWrap: true);
        Assert.Equal(0, (int)(wrapped.FormatFlags & global::System.Drawing.StringFormatFlags.NoWrap));
        var nowrap = Fork::System.Windows.Forms.WasmRtfDraw.BuildFormat(wordWrap: false);
        Assert.NotEqual(0, (int)(nowrap.FormatFlags & global::System.Drawing.StringFormatFlags.NoWrap));
    }

    // Word doc-click SO (gen16): GetTextManaged(GT_SELECTION) reads
    // SelectionLength, whose override calls SelectedText, whose
    // GetTextEx(GT_SELECTION) trampolines back into GetTextManaged --
    // unbounded same-box re-entrancy. The guard trips nested entry so
    // the inner call falls back to full text instead of recursing.
    // A live cycle needs rewritten bytes (raw GetTextEx dead-ends at
    // the stubbed SendMessage), so this pins the guard contract
    // directly; the browser re-gate proves the cycle is broken.
    // The guard only compares box identity (ReferenceEquals): it never
    // touches members, so uninitialized instances are adequate here and
    // keep the desktop MsftEdit demand from the file header intact.
    private static Fork::System.Windows.Forms.RichTextBox UninitBox() =>
        (Fork::System.Windows.Forms.RichTextBox)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            typeof(Fork::System.Windows.Forms.RichTextBox));

    [Fact]
    public void GetTextGuard_BlocksSameBoxReentry()
    {
        var box = UninitBox();
        Assert.True(ForkRtf.TryEnterGetText(box));
        Assert.False(ForkRtf.TryEnterGetText(box));
        ForkRtf.ExitGetText(box);
        Assert.True(ForkRtf.TryEnterGetText(box));
        ForkRtf.ExitGetText(box);
    }

    [Fact]
    public void GetTextGuard_AllowsCrossBoxEntry()
    {
        var first = UninitBox();
        var second = UninitBox();
        Assert.True(ForkRtf.TryEnterGetText(first));
        Assert.True(ForkRtf.TryEnterGetText(second));
        ForkRtf.ExitGetText(second);
        ForkRtf.ExitGetText(first);
        Assert.True(ForkRtf.TryEnterGetText(first));
        ForkRtf.ExitGetText(first);
    }

    // Headless RichTextBox char insertion (gen17: KeyDown delivered, 0px
    // inserted): the host emulates insertion for plain TextBox only, so
    // the fork splices the char itself. The pure splice math is pinned
    // below; the glue's base-text read is pinned handleless (a live box
    // with no created handle, as in the browser) by
    // InsertBaseText_MatchesForceWindowText_Handleless; the browser
    // re-gate proves end-to-end insertion.
    [Fact]
    public void SpliceInsertion_InsertsAtCaret()
    {
        Assert.Equal("hXello", ForkRtf.SpliceInsertion("hello", 1, 0, 'X'));
    }

    // Gen18: every keystroke replaced the doc with its own char (typed
    // "Hello World" + " Bold", doc showed a lone "d"). ForceWindowText
    // on a handleless box lands in managed _text, but the insert glue
    // read PInvokeCore.GetWindowText (WindowTextStorage only, always ""
    // handleless), so each splice started from "". The read must see the
    // same side the write lands on. Plain TextBox exercises the shared
    // TextBoxBase seam (a live RichTextBox cannot be constructed
    // in-harness). No handle is created here (browser: never); the write
    // itself must not create one either.
    // The in-vivo selection stays (0,0)+fallback while the mirror
    // dispatch exists: Select(1,0) on a created handle must stick and
    // read back. Minted handles are pure managed (fork CreateWindowEx),
    // so this exercises the same FromHandle/mirror path as the browser.
    // Gen19: RichTextBox.TextLength sends EM_GETTEXTLENGTHEX
    // unconditionally (no handle check); the fork left it at the
    // fall-through zero, so Select() clamped every selection to (0,0),
    // the caret never advanced, and each keystroke replaced the doc
    // ("Hello World" + " Bold" left a lone "d"). The dispatch now
    // answers from WindowText storage. A live RichTextBox cannot be
    // constructed in-harness, but the branch is TextBoxBase-typed, so a
    // plain TextBox with a created handle pins it.
    [Fact]
    public void GetTextLengthEx_AnswersStorageLength()
    {
        var box = new Fork::System.Windows.Forms.TextBox();
        _ = box.Handle;
        Assert.True(box.IsHandleCreated);
        box.ForceWindowText("Hi");
        Assert.Equal(2, box.TextLength);
        int len = (int)Fork::Windows.Win32.PInvokeCore.SendMessage(
            box,
            Fork::Windows.Win32.PInvokeCore.EM_GETTEXTLENGTHEX,
            default,
            (Fork::Windows.Win32.Foundation.LPARAM)0);
        Assert.Equal(2, len);
    }

    [Fact]
    public void SelectionMirror_RoundTrips_OnCreatedHandle()
    {
        var box = new Fork::System.Windows.Forms.TextBox();
        box.Text = "Hi";
        _ = box.Handle;
        Assert.True(box.IsHandleCreated);
        box.Select(1, 0);
        Assert.Equal(1, box.SelectionStart);
        Assert.Equal(0, box.SelectionLength);
    }

    [Fact]
    public void InsertBaseText_MatchesForceWindowText_Handleless()
    {
        var box = new Fork::System.Windows.Forms.TextBox();
        Assert.False(box.IsHandleCreated);
        box.ForceWindowText("H");
        Assert.False(box.IsHandleCreated);
        Assert.Equal("H", ForkRtf.ReadInsertBaseText(box));
        // The read must work handlelessly too. The old storage-only read
        // touched box.Handle, which materializes a real handle on desktop
        // (and the creation pushes _text into storage, masking the bug
        // in-harness); in the browser no handle ever exists, so every
        // keystroke read "" and replaced the doc with its own char.
        Assert.False(box.IsHandleCreated);
    }

    [Fact]
    public void SpliceInsertion_ReplacesSelection()
    {
        Assert.Equal("hXo", ForkRtf.SpliceInsertion("hello", 1, 3, 'X'));
    }

    [Fact]
    public void SpliceInsertion_ClampsToText()
    {
        Assert.Equal("hello!", ForkRtf.SpliceInsertion("hello", 9, 4, '!'));
        Assert.Equal("!hello", ForkRtf.SpliceInsertion("hello", -4, 0, '!'));
        Assert.Equal("!", ForkRtf.SpliceInsertion("hello", -4, 99, '!'));
        Assert.Equal("!", ForkRtf.SpliceInsertion(string.Empty, 0, 0, '!'));
    }

    [Fact]
    public void SliceSelection_ClampsToText()
    {
        Assert.Equal("ell", ForkRtf.SliceSelection("hello", 1, 3));
        Assert.Equal(string.Empty, ForkRtf.SliceSelection("hello", 2, 0));
        Assert.Equal(string.Empty, ForkRtf.SliceSelection("hello", 9, 4));
        Assert.Equal("lo", ForkRtf.SliceSelection("hello", 3, 99));
        Assert.Equal("hello", ForkRtf.SliceSelection("hello", -4, 99));
    }

    [Fact]
    public void ComputeStreamPayload_Rtf_RoundTripsText()
    {
        byte[] bytes = ForkRtf.ComputeStreamPayload("hi\nyou", SF_RTF, includeCrLfs: false);
        string payload = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.StartsWith("{\\rtf", payload);
        string back = ForkRtf.RtfToPlainText(payload);
        Assert.Contains("hi", back);
        Assert.Contains("you", back);
    }
}
