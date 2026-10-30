using System.Collections.Specialized;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf;

// Clipboard (matches System.Windows.Clipboard, PresentationCore subset):
// the gallery ClipboardPage calls Clear/Contains*/Get*/Set* for text,
// images and file-drop lists. Sync WPF-shaped methods operate on the
// session store; the device clipboard behind IWasmClipboard.Provider is
// bridged explicitly through FlushAsync/RefreshAsync so denial and
// cancellation surface as real states instead of silent no-ops. The sync
// methods never block on the provider: without an explicit flush, device
// text stays out of the session store (a session boundary, not a miss).

/// <summary>Clipboard text format (matches System.Windows.TextDataFormat).</summary>
public enum TextDataFormat
{
    Text = 0,
    UnicodeText = 1,
    Rtf = 2,
    Html = 3,
    CommaSeparatedValue = 4,
    Xaml = 5,
}

/// <summary>Device clipboard refused or failed the transfer (denial is loud).</summary>
public sealed class ClipboardException : InvalidOperationException
{
    public ClipboardException(string message) : base(message) { }

    public ClipboardException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Session clipboard (matches System.Windows.Clipboard for the text, image
/// and file-drop members the gallery uses). The engine keeps plain text
/// only: rich formats (Rtf/Html/Xaml/CSV) are a precise NotSupported boundary
/// on set, and read as absent. Audio payloads are never stored, so
/// ContainsAudio is always false. Data-object exchange bridges the text,
/// image and file-drop payloads a <see cref="DataObject"/> carries; richer
/// payloads have no session representation and stay out of the store.
/// </summary>
public static class Clipboard
{
    private static readonly object Gate = new();
    private static string? _text;
    private static ImageSource? _image;
    private static StringCollection? _files;

    /// <summary>
    /// Device bridge (the browser host installs its navigator.clipboard
    /// adapter; tests install fakes). Defaults to an in-memory device so
    /// headless sessions round-trip without a browser.
    /// </summary>
    public static IWasmClipboard Provider { get; set; } = new InMemoryClipboard();

    /// <summary>Empties the session store (device push needs FlushAsync).</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            _text = null;
            _image = null;
            _files = null;
        }
    }

    /// <summary>Whether the session store holds text.</summary>
    public static bool ContainsText() => ContainsText(TextDataFormat.UnicodeText);

    /// <summary>Whether the session store holds text in the given plain format.</summary>
    public static bool ContainsText(TextDataFormat format) =>
        format is TextDataFormat.Text or TextDataFormat.UnicodeText && TextOrNull() is not null;

    /// <summary>Session text (empty when nothing is stored, like WPF).</summary>
    public static string GetText() => GetText(TextDataFormat.UnicodeText);

    /// <summary>Session text in a plain format (rich formats are unsupported).</summary>
    public static string GetText(TextDataFormat format)
    {
        RequirePlainText(format, nameof(format));
        return TextOrNull() ?? string.Empty;
    }

    /// <summary>Stores text in the session (device push needs FlushAsync).</summary>
    public static void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (Gate) _text = text;
    }

    /// <summary>Stores text for a plain format (rich formats are unsupported).</summary>
    public static void SetText(string text, TextDataFormat format)
    {
        RequirePlainText(format, nameof(format));
        SetText(text);
    }

    /// <summary>Whether the session store holds an image.</summary>
    public static bool ContainsImage()
    {
        lock (Gate) return _image is not null;
    }

    /// <summary>Stores an image in the session.</summary>
    public static void SetImage(ImageSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        lock (Gate) _image = image;
    }

    /// <summary>Session image (null when absent, like WPF).</summary>
    public static ImageSource? GetImage()
    {
        lock (Gate) return _image;
    }

    /// <summary>Whether the session store holds a file-drop list.</summary>
    public static bool ContainsFileDropList()
    {
        lock (Gate) return _files is not null;
    }

    /// <summary>Stores virtual-mount file paths in the session.</summary>
    public static void SetFileDropList(StringCollection fileDropList)
    {
        ArgumentNullException.ThrowIfNull(fileDropList);
        var copy = new StringCollection();
        copy.AddRange(fileDropList.Cast<string>().ToArray());
        lock (Gate) _files = copy;
    }

    /// <summary>Session file-drop list (empty when absent, like WPF).</summary>
    public static StringCollection GetFileDropList()
    {
        lock (Gate)
        {
            var copy = new StringCollection();
            if (_files is not null) copy.AddRange(_files.Cast<string>().ToArray());
            return copy;
        }
    }

    /// <summary>Audio payloads are never stored: always false (no fabricated data).</summary>
    public static bool ContainsAudio() => false;

    /// <summary>
    /// Stores the text, image and file-drop payloads a data object carries
    /// (matches Clipboard.SetDataObject; richer payloads have no session
    /// representation and stay out of the store).
    /// </summary>
    public static void SetDataObject(object data) => SetDataObject(data, false);

    /// <summary>
    /// Stores the text, image and file-drop payloads a data object carries
    /// (matches Clipboard.SetDataObject(object, bool); the copy flag has no
    /// OLE ownership behind it, so session storage is always a copy).
    /// </summary>
    public static void SetDataObject(object data, bool copy)
    {
        ArgumentNullException.ThrowIfNull(data);
        _ = copy;
        var source = data as IDataObject ?? new DataObject(data);
        if (source is DataObject typed)
        {
            if (typed.ContainsText()) SetText(typed.GetText());
            if (typed.GetImage() is { } image) SetImage(image);
            if (typed.ContainsFileDropList()) SetFileDropList(typed.GetFileDropList());
            return;
        }
        if (source.GetDataPresent(DataFormats.UnicodeText) &&
            source.GetData(DataFormats.UnicodeText) is string text) SetText(text);
        if (source.GetDataPresent(DataFormats.Bitmap) &&
            source.GetData(DataFormats.Bitmap) is ImageSource bitmap) SetImage(bitmap);
        if (source.GetDataPresent(DataFormats.FileDrop))
        {
            var files = new StringCollection();
            if (source.GetData(DataFormats.FileDrop) is StringCollection stored)
                files.AddRange(stored.Cast<string>().ToArray());
            else if (source.GetData(DataFormats.FileDrop) is string[] paths)
                files.AddRange(paths);
            if (files.Count > 0) SetFileDropList(files);
        }
    }

    /// <summary>
    /// Returns the session store as a data object (matches
    /// Clipboard.GetDataObject; formats absent from the store read absent).
    /// </summary>
    public static IDataObject GetDataObject()
    {
        var view = new DataObject();
        var text = TextOrNull();
        if (text is not null) view.SetText(text);
        lock (Gate)
        {
            if (_image is not null) view.SetImage(_image);
            if (_files is not null) view.SetFileDropList(_files);
        }
        return view;
    }

    /// <summary>
    /// Pushes session text to the device bridge (real navigator.clipboard in
    /// the browser). Denial throws <see cref="ClipboardException"/>; a
    /// cancelled token throws <see cref="OperationCanceledException"/>.
    /// Empty session text pushes empty, mirroring Clear.
    /// </summary>
    public static async Task FlushAsync(CancellationToken ct = default)
    {
        var text = TextOrNull() ?? string.Empty;
        ct.ThrowIfCancellationRequested();
        try
        {
            await Provider.SetTextAsync(text, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new ClipboardException($"Clipboard device denied the text ({error.GetType().Name}: {error.Message}).", error);
        }
    }

    /// <summary>
    /// Pulls device text into the session store. A null device read (denied
    /// or unavailable) throws <see cref="ClipboardException"/> instead of
    /// inventing content; cancellation throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task RefreshAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string? text;
        try
        {
            text = await Provider.GetTextAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new ClipboardException($"Clipboard device denied the read ({error.GetType().Name}: {error.Message}).", error);
        }
        if (text is null)
            throw new ClipboardException("Clipboard device returned no text (denied or unavailable).");
        lock (Gate) _text = text;
    }

    private static string? TextOrNull()
    {
        lock (Gate) return _text;
    }

    private static void RequirePlainText(TextDataFormat format, string param)
    {
        if (format is not (TextDataFormat.Text or TextDataFormat.UnicodeText))
            throw new NotSupportedException(
                $"Clipboard keeps plain text only; {format} payloads are not supported (pass Text or UnicodeText).");
    }
}
