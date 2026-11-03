// Fork-owned: headless RichEdit module seeding. Native RichTextBox creation
// demands the RichEdit module (LoadLibrary + version probe) in CreateParams
// and throws Win32Exception ("Cannot load MsftEdit.DLL") before any
// RichTextBox -- ours or derived (e.g. Syncfusion RichTextBoxExt) -- can
// exist. There is no native RichEdit in the browser and the version probe
// cannot survive headless (FileVersionInfo throws for the missing file), so
// on browser only the cached handle is pre-seeded: the load block is
// skipped, the version stays at its default (conservative feature paths),
// and all RichEdit messaging stays on the existing stubs. Desktop behavior
// is untouched (real module loads there as before).

using System.Reflection;
using System.Text;
using Windows.Win32;
using Windows.Win32.UI.Controls.RichEdit;

namespace System.Windows.Forms;

public partial class RichTextBox
{
    static RichTextBox()
    {
        if (!OperatingSystem.IsBrowser())
        {
            return;
        }

        try
        {
            typeof(RichTextBox).GetField("s_moduleHandle",
                BindingFlags.Static | BindingFlags.NonPublic)?
                .SetValue(null, (nint)0x70000000);
        }
        catch
        {
        }
    }

    // Headless char insertion (gen17: KeyDown delivered, 0px inserted).
    // Native RichEdit inserts on WM_CHAR; headless there is none and the
    // host's insertion emulation is TextBox-typed (a RichTextBox never
    // matches), so the fork splices the char itself. Browser-only:
    // desktop keeps native insertion (no double-type). Respects Handled
    // (vendor suppression) and ReadOnly; control chars (Enter/Backspace)
    // keep their existing host paths.
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (!OperatingSystem.IsBrowser() || e.Handled)
        {
            return;
        }

        char c = e.KeyChar;
        if (c < ' ' || c == 0x7F || ReadOnly)
        {
            return;
        }

        try
        {
            string current = WasmRtf.ReadInsertBaseText(this);
            int start = Math.Min(Math.Max(0, SelectionStart), current.Length);
            int len = Math.Min(Math.Max(0, SelectionLength), current.Length - start);
            ForceWindowText(WasmRtf.SpliceInsertion(current, start, len, c));
            SelectionStart = start + 1;
            e.Handled = true;
            // Storage writes do not invalidate (SetWindowText stub only
            // stores); without this the inserted char never repaints and
            // the gate still reads 0px. Whole-box mark: caret position
            // also moved.
            WasmRepaintGate.RequestRepaint(this);
        }
        catch
        {
        }
    }

    // Headless EDITSTREAM replacement. The desktop StreamIn/StreamOut drive
    // the native RichEdit control through Marshal.GetFunctionPointerForDelegate
    // + EM_STREAMIN/EM_STREAMOUT; the function-pointer thunk traps the
    // wasm interpreter with "memory access out of bounds" and no managed
    // frame (Syncfusion diagram RichTextNode boot), and the stubbed
    // SendMessage could never deliver the bytes anyway. These managed
    // equivalents keep the desktop contract surface: same ArgumentException
    // on a missing RTF tag, same MaxLength truncation, text lands in the
    // same WindowText storage the Text getter reads. Formatting beyond plain
    // text is not preserved headless (no RichEdit); content is.
    internal static class WasmRtf
    {
        private static readonly Encoding AnsiEncoding =
            CodePagesEncodingProvider.Instance.GetEncoding(0) ?? Encoding.UTF8;

        internal static void StreamInManaged(RichTextBox box, Stream data, uint flags)
        {
            ArgumentNullException.ThrowIfNull(box);
            ArgumentNullException.ThrowIfNull(data);
            byte[] bytes;
            using (var copy = new MemoryStream())
            {
                data.CopyTo(copy);
                bytes = copy.ToArray();
            }

            string text = ComputeStreamText(bytes, flags);
            int maxLength = box.MaxLength;
            if (text.Length > maxLength)
            {
                text = text.Substring(0, maxLength);
            }

            // Never assign box.Text here: RichTextBox.Text's setter streams
            // back into StreamIn on a created handle (infinite recursion,
            // diagram boot SO). ForceWindowText writes the same WindowText
            // storage the getters read, with no re-entrant streaming.
            if ((flags & PInvoke.SFF_SELECTION) != 0)
            {
                try
                {
                    if (box.SelectionLength > 0)
                    {
                        string current = ReadInsertBaseText(box);
                        int start = Math.Min(box.SelectionStart, current.Length);
                        int len = Math.Min(box.SelectionLength, current.Length - start);
                        box.ForceWindowText(current.Substring(0, start) + text + current.Substring(start + len));
                        return;
                    }
                }
                catch
                {
                }
            }

            box.ForceWindowText(text);
        }

        // Managed EM_GETTEXTEX: the stubbed SendMessage would report zero
        // length and empty text, leaving every RichTextBox (and
        // RichTextNode clear-text) blank headless. Selection slices
        // best-effort from the managed selection; unreadable selection
        // falls back to the full text.
        // Word doc-click SO (gen16): SelectionLength -> SelectedText ->
        // GetTextEx(GT_SELECTION) -> GetTextManaged -> SelectionLength ...
        // never unwinds: each pass re-enters GetTextManaged before the
        // outer call returns (the SelectionStart/EM_GETSEL/TextLength
        // detour terminates with 0, but its frames never unwind either).
        // Same-box nested entry therefore skips the slice: full-text
        // fallback, as for unreadable selection below. Single-threaded
        // browser re-enters on the same thread; ThreadStatic keeps
        // test-runner threads independent. Nesting is always same-box
        // (the slice only touches `box`), so cross-box entry stays allowed.
        [ThreadStatic]
        private static RichTextBox? t_activeGetTextBox;

        internal static bool TryEnterGetText(RichTextBox box)
        {
            if (ReferenceEquals(t_activeGetTextBox, box))
            {
                return false;
            }

            t_activeGetTextBox = box;
            return true;
        }

        internal static void ExitGetText(RichTextBox box)
        {
            if (ReferenceEquals(t_activeGetTextBox, box))
            {
                t_activeGetTextBox = null;
            }
        }

        internal static string GetTextManaged(RichTextBox box, GETTEXTEX_FLAGS flags)
        {
            ArgumentNullException.ThrowIfNull(box);
            string text = PInvokeCore.GetWindowText(box);
            if ((flags & GETTEXTEX_FLAGS.GT_SELECTION) != 0 && TryEnterGetText(box))
            {
                try
                {
                    try
                    {
                        text = SliceSelection(text, box.SelectionStart, box.SelectionLength);
                    }
                    catch
                    {
                    }
                }
                finally
                {
                    ExitGetText(box);
                }
            }

            if ((flags & GETTEXTEX_FLAGS.GT_USECRLF) != 0)
            {
                text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            }

            return text;
        }

        internal static string SliceSelection(string text, int start, int length)
        {
            if (length <= 0)
            {
                return string.Empty;
            }

            int s = Math.Max(0, Math.Min(start, text.Length));
            int len = Math.Max(0, Math.Min(length, text.Length - s));
            return text.Substring(s, len);
        }

        // Base text behind headless insertion (gen18: every keystroke
        // replaced the doc with its own char, ending in a lone "d").
        // ForceWindowText lands in managed _text while the handle is
        // uncreated (browser: always) and in WindowText storage once
        // created; Control.WindowText reads the same side in both cases,
        // while PInvokeCore.GetWindowText reads storage only. The seam is
        // TextBoxBase (shared by TextBox and RichTextBox) so the pin can
        // construct a plain TextBox in-harness (a live RichTextBox is
        // unavailable: desktop demands MsftEdit, headless has none).
        // Pinned by InsertBaseText_MatchesForceWindowText_Handleless.
        internal static string ReadInsertBaseText(TextBoxBase box)
        {
            ArgumentNullException.ThrowIfNull(box);
            return box.WindowText;
        }

        // Headless char insertion (gen17: KeyDown delivered, 0px inserted).
        // The host's key pipeline only emulates insertion for plain TextBox
        // (_focusedTextBox is TextBox-typed; a RichTextBox never matches),
        // and there is no native RichEdit to insert for us, so the fork
        // splices the char itself: replace the clamped selection (empty =
        // caret insert), mirroring the host's TextBox math. Pure for pins.
        internal static string SpliceInsertion(string text, int start, int length, char c)
        {
            int s = Math.Max(0, Math.Min(start, text.Length));
            int len = Math.Max(0, Math.Min(length, text.Length - s));
            return text.Substring(0, s) + c + text.Substring(s + len);
        }

        internal static void StreamOutManaged(RichTextBox box, Stream data, uint flags, bool includeCrLfs)
        {
            ArgumentNullException.ThrowIfNull(box);
            ArgumentNullException.ThrowIfNull(data);
            byte[] bytes = ComputeStreamPayload(box.Text, flags, includeCrLfs);
            data.Write(bytes, 0, bytes.Length);
        }

        // Pure byte/text mapping behind the managed stream methods: pinned
        // directly by unit tests (a live RichTextBox cannot be constructed
        // in every harness: desktop demands MsftEdit, headless has none).
        internal static string ComputeStreamText(byte[] bytes, uint flags)
        {
            bool unicode = (flags & PInvoke.SF_UNICODE) != 0;
            // SF_RTFNOOBJS (0x3) always carries the SF_RTF bit; testing SF_RTF
            // alone matches the desktop branch exactly (SF_TEXT is 0x1, which
            // a combined mask would misread as RTF).
            if ((flags & PInvoke.SF_RTF) != 0)
            {
                string rtf = (unicode ? Encoding.Unicode : AnsiEncoding).GetString(bytes);
                if (!rtf.StartsWith("{\\rtf", StringComparison.Ordinal))
                {
                    throw new ArgumentException(SR.InvalidFileFormat);
                }

                return RtfToPlainText(rtf);
            }

            return (unicode ? Encoding.Unicode : AnsiEncoding).GetString(bytes);
        }

        internal static byte[] ComputeStreamPayload(string text, uint flags, bool includeCrLfs)
        {
            string payload;
            if ((flags & PInvoke.SF_RTF) != 0)
            {
                payload = PlainToRtf(text);
            }
            else if (includeCrLfs)
            {
                payload = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            }
            else
            {
                payload = text.Replace("\r\n", "\n");
            }

            return ((flags & PInvoke.SF_UNICODE) != 0 ? Encoding.Unicode : AnsiEncoding).GetBytes(payload);
        }

        // Minimal RTF reader: plain-text extraction only. Skips destination
        // groups (fonttbl/colortbl/...) so metadata never leaks into text.
        internal static string RtfToPlainText(string rtf)
        {
            var sb = new StringBuilder(rtf.Length);
            int skipDepth = -1;
            int depth = 0;
            int ucSkip = 1;
            int pendingUnicodeSkip = 0;
            int i = 0;
            int n = rtf.Length;
            while (i < n)
            {
                char c = rtf[i];
                if (c == '{')
                {
                    depth++;
                    // Lookahead: a destination group ({\*, \fonttbl, ...) contributes no text.
                    int j = i + 1;
                    if (j < n && rtf[j] == '\\')
                    {
                        j++;
                        int k = j;
                        while (k < n && char.IsAsciiLetter(rtf[k]))
                        {
                            k++;
                        }

                        string word = rtf.Substring(j, k - j);
                        if (word.Length == 0 && k < n && rtf[k] == '*')
                        {
                            if (skipDepth < 0)
                            {
                                skipDepth = depth;
                            }
                        }
                        else if (word is "fonttbl" or "colortbl" or "stylesheet" or "info"
                            or "pict" or "object" or "header" or "footer" or "footnote"
                            or "annotation" or "author" or "operator" or "title" or "subject")
                        {
                            if (skipDepth < 0)
                            {
                                skipDepth = depth;
                            }
                        }
                    }

                    i++;
                    continue;
                }

                if (c == '}')
                {
                    if (skipDepth == depth)
                    {
                        skipDepth = -1;
                    }

                    depth = Math.Max(0, depth - 1);
                    i++;
                    continue;
                }

                bool skipping = skipDepth >= 0;
                if (c == '\\' && i + 1 < n)
                {
                    char d = rtf[i + 1];
                    if (d == '\r' || d == '\n')
                    {
                        // Escaped hard line break in the source: ignored.
                        i += 2;
                        if (d == '\r' && i < n && rtf[i] == '\n')
                        {
                            i++;
                        }

                        continue;
                    }

                    if (d == ' ' || d == '_' || d == '-' || d == '~' || d == ':')
                    {
                        if (!skipping)
                        {
                            sb.Append(d == '~' ? (char)0xA0 : d == '_' ? (char)0x2011 : d == '-' ? (char)0x00AD : d);
                            pendingUnicodeSkip = 0;
                        }

                        i += 2;
                        continue;
                    }

                    if (d == '\\' || d == '{' || d == '}')
                    {
                        if (!skipping)
                        {
                            sb.Append(d);
                            pendingUnicodeSkip = 0;
                        }

                        i += 2;
                        continue;
                    }

                    if (d == '\'')
                    {
                        if (i + 3 < n)
                        {
                            int hi = HexValue(rtf[i + 2]);
                            int lo = HexValue(rtf[i + 3]);
                            if (hi >= 0 && lo >= 0)
                            {
                                if (!skipping)
                                {
                                    sb.Append(AnsiEncoding.GetString([(byte)((hi << 4) | lo)]));
                                    if (pendingUnicodeSkip > 0)
                                    {
                                        pendingUnicodeSkip--;
                                    }
                                }

                                i += 4;
                                continue;
                            }
                        }

                        i += 2;
                        continue;
                    }

                    if (d == '*')
                    {
                        // Destination selector: the enclosing group is metadata.
                        if (skipDepth < 0)
                        {
                            skipDepth = depth;
                        }

                        i += 2;
                        continue;
                    }

                    if (char.IsAsciiLetter(d))
                    {
                        int k = i + 1;
                        while (k < n && char.IsAsciiLetter(rtf[k]))
                        {
                            k++;
                        }

                        string word = rtf.Substring(i + 1, k - i - 1);
                        bool neg = false;
                        if (k < n && (rtf[k] == '-' || char.IsAsciiDigit(rtf[k])))
                        {
                            neg = rtf[k] == '-';
                            if (neg)
                            {
                                k++;
                            }

                            while (k < n && char.IsAsciiDigit(rtf[k]))
                            {
                                k++;
                            }
                        }

                        int param = 0;
                        int ps = i + 1 + word.Length + (neg ? 1 : 0);
                        if (ps < k)
                        {
                            int.TryParse(rtf.AsSpan(ps, k - ps), out param);
                            if (neg)
                            {
                                param = -param;
                            }
                        }

                        // A single space after a control word is its delimiter.
                        if (k < n && rtf[k] == ' ')
                        {
                            k++;
                        }

                        i = k;
                        if (skipping)
                        {
                            continue;
                        }

                        pendingUnicodeSkip = 0;
                        switch (word)
                        {
                            case "par":
                            case "line":
                                sb.Append('\n');
                                break;
                            case "tab":
                                sb.Append('\t');
                                break;
                            case "uc":
                                ucSkip = Math.Max(0, param);
                                break;
                            case "u":
                                sb.Append((char)(short)param);
                                pendingUnicodeSkip = ucSkip;
                                break;
                        }

                        continue;
                    }

                    i += 2;
                    continue;
                }

                if (!skipping)
                {
                    if (c == '\r' || c == '\n')
                    {
                        // Bare source line breaks carry no content.
                    }
                    else if (pendingUnicodeSkip > 0)
                    {
                        pendingUnicodeSkip--;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }

                i++;
            }

            return sb.ToString();
        }

        internal static string PlainToRtf(string text)
        {
            var sb = new StringBuilder(text.Length + 128);
            sb.Append("{\\rtf1\\ansi\\ansicpg1252\\deff0\\deflang1033{\\fonttbl{\\f0\\fnil\\fcharset0 Segoe UI;}}");
            sb.Append("\\viewkind4\\uc1\\pard\\f0\\fs17 ");
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '{':
                        sb.Append("\\{");
                        break;
                    case '}':
                        sb.Append("\\}");
                        break;
                    case '\r':
                        break;
                    case '\n':
                        sb.Append("\\par\r\n");
                        break;
                    default:
                        if (c < 0x80)
                        {
                            sb.Append(c);
                        }
                        else
                        {
                            byte[] enc = AnsiEncoding.GetBytes([c]);
                            if (enc.Length == 1 && enc[0] >= 0x80)
                            {
                                sb.Append("\\'");
                                sb.Append(enc[0].ToString("x2"));
                            }
                            else
                            {
                                sb.Append("\\u");
                                sb.Append(((short)c).ToString());
                                sb.Append('?');
                            }
                        }

                        break;
                }
            }

            sb.Append("\\par\r\n}");
            return sb.ToString();
        }

        private static int HexValue(char c) =>
            c >= '0' && c <= '9' ? c - '0'
            : c >= 'a' && c <= 'f' ? c - 'a' + 10
            : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
    }
}
