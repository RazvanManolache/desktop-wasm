using System.Collections.Specialized;

namespace DesktopWasm.Wpf;

/// <summary>Drop result (matches System.Windows.DragDropEffects; numerics verified against the real WPF assemblies).</summary>
[Flags]
public enum DragDropEffects
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
    Scroll = -2147483648,
    All = -2147483645,
}

/// <summary>Modifier/pointer state during a drag (matches System.Windows.DragDropKeyStates).</summary>
[Flags]
public enum DragDropKeyStates
{
    None = 0,
    LeftMouseButton = 1,
    RightMouseButton = 2,
    ShiftKey = 4,
    ControlKey = 8,
    MiddleMouseButton = 16,
    AltKey = 32,
}

/// <summary>Drag-target handler (matches System.Windows.DragEventHandler).</summary>
public delegate void DragEventHandler(object sender, DragEventArgs e);

/// <summary>
/// Drag-target data (matches System.Windows.DragEventArgs): the payload is
/// an <see cref="IDataObject"/> so HTML5 file drops land as
/// <see cref="DataFormats.FileDrop"/> string arrays, exactly like the
/// editor's Control_DragEnter/Control_Drop handlers read them. Handlers set
/// <see cref="Effects"/>; the engine-native raiser and the browser file-drop
/// bridge both deliver through <see cref="UIElement.RaiseDragEvent"/>.
/// </summary>
public sealed class DragEventArgs : RoutedEventArgs
{
    public DragEventArgs(IDataObject data, DragDropKeyStates keyStates, DragDropEffects allowedEffects)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        KeyStates = keyStates;
        AllowedEffects = allowedEffects;
        Effects = DragDropEffects.None;
    }

    /// <summary>Payload under negotiation (never null).</summary>
    public IDataObject Data { get; }

    /// <summary>Modifier/pointer state when the event was raised.</summary>
    public DragDropKeyStates KeyStates { get; }

    /// <summary>Effects the source allows (set by the raiser, read by handlers).</summary>
    public DragDropEffects AllowedEffects { get; }

    /// <summary>Effect the handler commits to (None until a handler sets it).</summary>
    public DragDropEffects Effects { get; set; }
}

/// <summary>Standard clipboard/drag formats (matches System.Windows.DataFormats; names verified).</summary>
public static class DataFormats
{
    public const string Text = "Text";
    public const string UnicodeText = "UnicodeText";
    public const string Dib = "DeviceIndependentBitmap";
    public const string Bitmap = "Bitmap";
    public const string EnhancedMetafile = "EnhancedMetafile";
    public const string MetafilePicture = "MetaFilePict";
    public const string SymbolicLink = "SymbolicLink";
    public const string Dif = "DataInterchangeFormat";
    public const string Tiff = "TaggedImageFileFormat";
    public const string OemText = "OEMText";
    public const string Palette = "Palette";
    public const string PenData = "PenData";
    public const string Riff = "RiffAudio";
    public const string WaveAudio = "WaveAudio";
    public const string FileDrop = "FileDrop";
    public const string Locale = "Locale";
    public const string Html = "HTML Format";
    public const string Rtf = "Rich Text Format";
    public const string CommaSeparatedValue = "CSV";
    public const string StringFormat = "System.String";
    public const string Serializable = "PersistentObject";
    public const string Xaml = "Xaml";
    public const string XamlPackage = "XamlPackage";
}

/// <summary>Data-exchange contract (matches System.Windows.IDataObject).</summary>
public interface IDataObject
{
    object? GetData(string format);
    object? GetData(Type format);
    object? GetData(string format, bool autoConvert);
    bool GetDataPresent(string format);
    bool GetDataPresent(Type format);
    bool GetDataPresent(string format, bool autoConvert);
    string[] GetFormats();
    string[] GetFormats(bool autoConvert);
    void SetData(object? data);
    void SetData(string format, object? data);
    void SetData(Type format, object? data);
    void SetData(string format, object? data, bool autoConvert);
}

/// <summary>
/// Paste/copy veto data (matches System.Windows.DataObjectEventArgs):
/// handlers call <see cref="CancelCommand"/> to reject the operation,
/// which the numeric boxes use to keep non-numeric pastes out.
/// </summary>
public class DataObjectEventArgs : RoutedEventArgs
{
    public DataObjectEventArgs(IDataObject dataObject, bool isDragDrop)
    {
        SourceDataObject = dataObject ?? throw new ArgumentNullException(nameof(dataObject));
        IsDragDrop = isDragDrop;
    }

    /// <summary>Payload the command would apply (replaced via DataObject on pastes).</summary>
    public IDataObject SourceDataObject { get; }

    /// <summary>True when the command rides on a drag-drop instead of the clipboard.</summary>
    public bool IsDragDrop { get; }

    /// <summary>True after <see cref="CancelCommand"/> rejected the operation.</summary>
    public bool CommandCancelled { get; private set; }

    /// <summary>Rejects the pending paste/copy (the command must check this flag).</summary>
    public void CancelCommand() => CommandCancelled = true;
}

/// <summary>Paste data (matches System.Windows.DataObjectPastingEventArgs).</summary>
public sealed class DataObjectPastingEventArgs : DataObjectEventArgs
{
    public DataObjectPastingEventArgs(IDataObject dataObject, bool isDragDrop, string formatToApply)
        : base(dataObject, isDragDrop)
    {
        DataObject = dataObject;
        FormatToApply = formatToApply ?? throw new ArgumentNullException(nameof(formatToApply));
    }

    /// <summary>Payload the paste applies (handlers may substitute their own object).</summary>
    public IDataObject DataObject { get; set; }

    /// <summary>Format the paste reads (handlers may narrow it).</summary>
    public string FormatToApply { get; set; }
}

/// <summary>Paste handler (matches System.Windows.DataObjectPastingEventHandler).</summary>
public delegate void DataObjectPastingEventHandler(object sender, DataObjectPastingEventArgs e);

/// <summary>
/// Drag-source decision (matches System.Windows.DragAction; numerics
/// Continue=0/Drop=1/Cancel=2 verified against the 10.0.13 ref pack and a
/// live runtime probe). Read by AvalonEdit's drag-continue handler through
/// <see cref="QueryContinueDragEventArgs.Action"/>.
/// </summary>
public enum DragAction
{
    Continue = 0,
    Drop = 1,
    Cancel = 2,
}

/// <summary>
/// Drag-source cursor feedback (matches System.Windows.GiveFeedbackEventArgs):
/// sealed over <see cref="RoutedEventArgs"/> with an internal framework-only
/// constructor like WPF (converted code only reads <see cref="Effects"/> and
/// flips <see cref="UseDefaultCursors"/>, as AvalonEdit's text-area drag
/// handler does).
/// </summary>
public sealed class GiveFeedbackEventArgs : RoutedEventArgs
{
    internal GiveFeedbackEventArgs(DragDropEffects effects, bool useDefaultCursors)
    {
        Effects = effects;
        UseDefaultCursors = useDefaultCursors;
    }

    /// <summary>Effects the drop target currently allows.</summary>
    public DragDropEffects Effects { get; }

    /// <summary>Whether the drag loop shows the default cursors (WPF default true).</summary>
    public bool UseDefaultCursors { get; set; }
}

/// <summary>Drag-source feedback handler (matches System.Windows.GiveFeedbackEventHandler).</summary>
public delegate void GiveFeedbackEventHandler(object sender, GiveFeedbackEventArgs e);

/// <summary>
/// Drag-source continuation query (matches
/// System.Windows.QueryContinueDragEventArgs): sealed over
/// <see cref="RoutedEventArgs"/> with an internal framework-only constructor
/// like WPF (converted code reads <see cref="EscapePressed"/> and commits
/// <see cref="Action"/>, as AvalonEdit's text-area drag handler does). An
/// undefined <see cref="Action"/> value throws <see cref="ArgumentException"/>,
/// verified against the live 10.0.13 runtime.
/// </summary>
public sealed class QueryContinueDragEventArgs : RoutedEventArgs
{
    internal QueryContinueDragEventArgs(bool escapePressed, DragDropKeyStates keyStates)
    {
        EscapePressed = escapePressed;
        KeyStates = keyStates;
        Action = DragAction.Continue;
    }

    /// <summary>Whether Escape was pressed (the loop must cancel).</summary>
    public bool EscapePressed { get; }

    /// <summary>Modifier/pointer state when the event was raised.</summary>
    public DragDropKeyStates KeyStates { get; }

    /// <summary>
    /// What the drag loop should do next (Continue until a handler commits
    /// Drop or Cancel).
    /// </summary>
    public DragAction Action
    {
        get => _action;
        set
        {
            if (!Enum.IsDefined(typeof(DragAction), value))
                throw new ArgumentException($"Unknown DragAction value {(int)value}.", nameof(value));
            _action = value;
        }
    }

    private DragAction _action;
}

/// <summary>Drag-source continuation handler (matches System.Windows.QueryContinueDragEventHandler).</summary>
public delegate void QueryContinueDragEventHandler(object sender, QueryContinueDragEventArgs e);

/// <summary>Copy-veto handler (matches System.Windows.DataObjectCopyingEventHandler).</summary>
public delegate void DataObjectCopyingEventHandler(object sender, DataObjectCopyingEventArgs e);

/// <summary>Format-negotiation handler (matches System.Windows.DataObjectSettingDataEventHandler).</summary>
public delegate void DataObjectSettingDataEventHandler(object sender, DataObjectSettingDataEventArgs e);

/// <summary>
/// Copy veto data (matches System.Windows.DataObjectCopyingEventArgs):
/// handlers call <see cref="DataObjectEventArgs.CancelCommand"/> to reject
/// the copy, which AvalonEdit's copy path checks before touching the
/// clipboard. The constructor sets <see cref="RoutedEventArgs.RoutedEvent"/>
/// to <see cref="DataObject.CopyingEvent"/> like WPF, so the converted
/// raise-then-check sequence runs without a routing token.
/// </summary>
public sealed class DataObjectCopyingEventArgs : DataObjectEventArgs
{
    public DataObjectCopyingEventArgs(IDataObject dataObject, bool isDragDrop)
        : base(dataObject, isDragDrop)
    {
        DataObject = dataObject;
        RoutedEvent = DesktopWasm.Wpf.DataObject.CopyingEvent;
    }

    /// <summary>Payload the copy would apply (never null).</summary>
    public IDataObject DataObject { get; }
}

/// <summary>
/// Format-negotiation data (matches
/// System.Windows.DataObjectSettingDataEventArgs): raised while a data object
/// gains a format so handlers can veto or substitute. The constructor sets
/// <see cref="RoutedEventArgs.RoutedEvent"/> to
/// <see cref="DataObject.SettingDataEvent"/> like WPF (a drag-drop copy is
/// never in flight here, so <see cref="DataObjectEventArgs.IsDragDrop"/> is
/// false, verified against the live runtime).
/// </summary>
public sealed class DataObjectSettingDataEventArgs : DataObjectEventArgs
{
    public DataObjectSettingDataEventArgs(IDataObject dataObject, string format)
        : base(dataObject, false)
    {
        DataObject = dataObject;
        Format = format ?? throw new ArgumentNullException(nameof(format));
        RoutedEvent = DesktopWasm.Wpf.DataObject.SettingDataEvent;
    }

    /// <summary>Payload gaining the format (never null).</summary>
    public IDataObject DataObject { get; }

    /// <summary>Format being added (never null).</summary>
    public string Format { get; }
}

/// <summary>
/// Session data object (matches System.Windows.DataObject): a format-keyed
/// store with the text-family auto-conversion converted readers rely on
/// (Text/UnicodeText/System.String/OemText alias one payload, like desktop
/// OLE text). Image and file-drop payloads keep their exact shapes so
/// Clipboard bridging and FileDrop drops round-trip.
/// </summary>
public sealed class DataObject : IDataObject
{
    private static readonly string[] TextFamily =
        [DataFormats.StringFormat, DataFormats.UnicodeText, DataFormats.Text, DataFormats.OemText];

    private readonly Dictionary<string, object?> _store = new(StringComparer.Ordinal);

    public DataObject()
    {
    }

    public DataObject(object? data)
    {
        if (data is not null) SetData(data.GetType(), data);
    }

    public DataObject(string format, object? data) => SetData(format, data);

    public DataObject(Type format, object? data) => SetData(format, data);

    public DataObject(string format, object? data, bool autoConvert) => SetData(format, data, autoConvert);

    /// <summary>Paste token (matches DataObject.PastingEvent).</summary>
    public static readonly RoutedEvent PastingEvent = EventManager.RegisterRoutedEvent(
        "Pasting", RoutingStrategy.Bubble, typeof(DataObjectPastingEventHandler), typeof(DataObject));

    /// <summary>Attaches a paste veto (matches DataObject.AddPastingHandler).</summary>
    public static void AddPastingHandler(DependencyObject dependencyObject, DataObjectPastingEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        ArgumentNullException.ThrowIfNull(handler);
        if (dependencyObject is UIElement element) element.AddHandler(PastingEvent, handler);
        else throw new NotSupportedException("DataObject paste handlers attach to UIElement targets in the session.");
    }

    /// <summary>Detaches a paste veto (matches DataObject.RemovePastingHandler).</summary>
    public static void RemovePastingHandler(DependencyObject dependencyObject, DataObjectPastingEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(dependencyObject);
        ArgumentNullException.ThrowIfNull(handler);
        if (dependencyObject is UIElement element) element.RemoveHandler(PastingEvent, handler);
    }

    /// <summary>
    /// Copy token (matches DataObject.CopyingEvent): the
    /// <see cref="DataObjectCopyingEventArgs"/> constructor routes through
    /// this event like WPF, so the converted copy raise-then-check sequence
    /// carries a routing token.
    /// </summary>
    public static readonly RoutedEvent CopyingEvent = EventManager.RegisterRoutedEvent(
        "Copying", RoutingStrategy.Bubble, typeof(DataObjectCopyingEventHandler), typeof(DataObject));

    /// <summary>
    /// Format token (matches DataObject.SettingDataEvent): the
    /// <see cref="DataObjectSettingDataEventArgs"/> constructor routes through
    /// this event like WPF.
    /// </summary>
    public static readonly RoutedEvent SettingDataEvent = EventManager.RegisterRoutedEvent(
        "SettingData", RoutingStrategy.Bubble, typeof(DataObjectSettingDataEventHandler), typeof(DataObject));

    public object? GetData(string format) => GetData(format, true);

    public object? GetData(Type format)
    {
        ArgumentNullException.ThrowIfNull(format);
        foreach (var candidate in CandidatesFor(format))
            if (_store.TryGetValue(candidate, out var value)) return value;
        return null;
    }

    public object? GetData(string format, bool autoConvert)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (_store.TryGetValue(format, out var value)) return value;
        if (autoConvert)
            foreach (var candidate in AliasesOf(format))
                if (_store.TryGetValue(candidate, out var aliased)) return aliased;
        return null;
    }

    public bool GetDataPresent(string format) => GetDataPresent(format, true);

    public bool GetDataPresent(Type format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return CandidatesFor(format).Any(candidate => _store.ContainsKey(candidate));
    }

    public bool GetDataPresent(string format, bool autoConvert)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (_store.ContainsKey(format)) return true;
        return autoConvert && AliasesOf(format).Any(candidate => _store.ContainsKey(candidate));
    }

    public string[] GetFormats() => GetFormats(true);

    public string[] GetFormats(bool autoConvert)
    {
        if (!autoConvert) return _store.Keys.ToArray();
        var names = new HashSet<string>(_store.Keys, StringComparer.Ordinal);
        foreach (var key in _store.Keys)
            foreach (var alias in AliasesOf(key))
                names.Add(alias);
        return names.ToArray();
    }

    public void SetData(object? data)
    {
        if (data is null) return;
        SetData(data.GetType(), data);
    }

    public void SetData(string format, object? data) => SetData(format, data, true);

    public void SetData(Type format, object? data)
    {
        ArgumentNullException.ThrowIfNull(format);
        foreach (var candidate in CandidatesFor(format))
            _store[candidate] = data;
        _store[format.FullName ?? format.Name] = data;
    }

    public void SetData(string format, object? data, bool autoConvert)
    {
        ArgumentNullException.ThrowIfNull(format);
        _store[format] = data;
        if (autoConvert)
            foreach (var alias in AliasesOf(format))
                _store[alias] = data;
    }

    /// <summary>Whether a text payload is stored (matches DataObject.ContainsText).</summary>
    public bool ContainsText() => TextFamily.Any(family => _store.ContainsKey(family));

    /// <summary>Whether text in the given plain format is stored.</summary>
    public bool ContainsText(TextDataFormat format) =>
        format is TextDataFormat.Text or TextDataFormat.UnicodeText && ContainsText();

    /// <summary>Stored text (empty when absent, like WPF).</summary>
    public string GetText() => GetText(TextDataFormat.UnicodeText);

    /// <summary>Stored text in a plain format (rich formats are unsupported).</summary>
    public string GetText(TextDataFormat format)
    {
        if (format is not (TextDataFormat.Text or TextDataFormat.UnicodeText))
            throw new NotSupportedException(
                $"DataObject keeps plain text only; {format} payloads are not supported (pass Text or UnicodeText).");
        foreach (var family in TextFamily)
            if (_store.TryGetValue(family, out var value) && value is string text) return text;
        return string.Empty;
    }

    /// <summary>Stores text (matches DataObject.SetText(string)).</summary>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        SetData(DataFormats.UnicodeText, text, true);
    }

    /// <summary>Stores text for a plain format (matches DataObject.SetText(string, TextDataFormat)).</summary>
    public void SetText(string text, TextDataFormat format)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (format is not (TextDataFormat.Text or TextDataFormat.UnicodeText))
            throw new NotSupportedException(
                $"DataObject keeps plain text only; {format} payloads are not supported (pass Text or UnicodeText).");
        SetData(format == TextDataFormat.Text ? DataFormats.Text : DataFormats.UnicodeText, text, true);
    }

    /// <summary>Whether an image payload is stored (matches DataObject.ContainsImage).</summary>
    public bool ContainsImage() => _store.ContainsKey(DataFormats.Bitmap);

    /// <summary>Stores an image (matches DataObject.SetImage).</summary>
    public void SetImage(ImageSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _store[DataFormats.Bitmap] = image;
    }

    /// <summary>Stored image (null when absent, like WPF).</summary>
    public ImageSource? GetImage() =>
        _store.TryGetValue(DataFormats.Bitmap, out var value) ? value as ImageSource : null;

    /// <summary>Whether a file-drop list is stored (matches DataObject.ContainsFileDropList).</summary>
    public bool ContainsFileDropList() => _store.ContainsKey(DataFormats.FileDrop);

    /// <summary>Stores a file-drop list (matches DataObject.SetFileDropList).</summary>
    public void SetFileDropList(StringCollection fileDropList)
    {
        ArgumentNullException.ThrowIfNull(fileDropList);
        _store[DataFormats.FileDrop] = fileDropList;
    }

    /// <summary>Stored file-drop list (empty when absent, like WPF).</summary>
    public StringCollection GetFileDropList()
    {
        var copy = new StringCollection();
        if (_store.TryGetValue(DataFormats.FileDrop, out var value))
        {
            if (value is StringCollection stored) copy.AddRange(stored.Cast<string>().ToArray());
            else if (value is string[] paths) copy.AddRange(paths);
        }
        return copy;
    }

    /// <summary>Audio payloads are never stored: always false (no fabricated data).</summary>
    public bool ContainsAudio() => false;

    private static IEnumerable<string> AliasesOf(string format)
    {
        if (TextFamily.Contains(format, StringComparer.Ordinal))
            return TextFamily.Where(family => !family.Equals(format, StringComparison.Ordinal));
        return [];
    }

    private static IEnumerable<string> CandidatesFor(Type format)
    {
        if (format == typeof(string)) return TextFamily;
        if (format == typeof(string[]) || format == typeof(StringCollection))
            return [DataFormats.FileDrop];
        if (typeof(ImageSource).IsAssignableFrom(format)) return [DataFormats.Bitmap];
        return [format.FullName ?? format.Name];
    }
}

/// <summary>
/// Drag-drop exchange (matches System.Windows.DragDrop): the target-side
/// routed events plus handler attachers over <see cref="UIElement"/>. Drops
/// arrive from the browser HTML5 file-drop bridge and from engine-native
/// raisers through <see cref="UIElement.RaiseDragEvent"/>; the modal OLE
/// source loop has no browser equivalent, so <see cref="DoDragDrop"/> is a
/// precise boundary instead of a silent no-op.
/// </summary>
public static class DragDrop
{
    public static readonly RoutedEvent PreviewDragEnterEvent = EventManager.RegisterRoutedEvent(
        "PreviewDragEnter", RoutingStrategy.Tunnel, typeof(DragEventHandler), typeof(DragDrop));
    public static readonly RoutedEvent DragEnterEvent = EventManager.RegisterRoutedEvent(
        "DragEnter", RoutingStrategy.Bubble, typeof(DragEventHandler), typeof(DragDrop));
    public static readonly RoutedEvent PreviewDragOverEvent = EventManager.RegisterRoutedEvent(
        "PreviewDragOver", RoutingStrategy.Tunnel, typeof(DragEventHandler), typeof(DragDrop));
    public static readonly RoutedEvent DragOverEvent = EventManager.RegisterRoutedEvent(
        "DragOver", RoutingStrategy.Bubble, typeof(DragEventHandler), typeof(DragDrop));
    public static readonly RoutedEvent PreviewDragLeaveEvent = EventManager.RegisterRoutedEvent(
        "PreviewDragLeave", RoutingStrategy.Tunnel, typeof(DragEventHandler), typeof(DragDrop));
    public static readonly RoutedEvent DragLeaveEvent = EventManager.RegisterRoutedEvent(
        "DragLeave", RoutingStrategy.Bubble, typeof(DragEventHandler), typeof(DragDrop));
    public static readonly RoutedEvent PreviewDropEvent = EventManager.RegisterRoutedEvent(
        "PreviewDrop", RoutingStrategy.Tunnel, typeof(DragEventHandler), typeof(DragDrop));
    public static readonly RoutedEvent DropEvent = EventManager.RegisterRoutedEvent(
        "Drop", RoutingStrategy.Bubble, typeof(DragEventHandler), typeof(DragDrop));

    public static void AddPreviewDragEnterHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).AddHandler(PreviewDragEnterEvent, handler);
    public static void RemovePreviewDragEnterHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).RemoveHandler(PreviewDragEnterEvent, handler);
    public static void AddDragEnterHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).AddHandler(DragEnterEvent, handler);
    public static void RemoveDragEnterHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).RemoveHandler(DragEnterEvent, handler);
    public static void AddPreviewDragOverHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).AddHandler(PreviewDragOverEvent, handler);
    public static void RemovePreviewDragOverHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).RemoveHandler(PreviewDragOverEvent, handler);
    public static void AddDragOverHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).AddHandler(DragOverEvent, handler);
    public static void RemoveDragOverHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).RemoveHandler(DragOverEvent, handler);
    public static void AddPreviewDragLeaveHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).AddHandler(PreviewDragLeaveEvent, handler);
    public static void RemovePreviewDragLeaveHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).RemoveHandler(PreviewDragLeaveEvent, handler);
    public static void AddDragLeaveHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).AddHandler(DragLeaveEvent, handler);
    public static void RemoveDragLeaveHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).RemoveHandler(DragLeaveEvent, handler);
    public static void AddPreviewDropHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).AddHandler(PreviewDropEvent, handler);
    public static void RemovePreviewDropHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).RemoveHandler(PreviewDropEvent, handler);
    public static void AddDropHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).AddHandler(DropEvent, handler);
    public static void RemoveDropHandler(DependencyObject target, DragEventHandler handler) =>
        Target(target).RemoveHandler(DropEvent, handler);

    /// <summary>
    /// Starts a modal drag from a source element. The browser has no OLE
    /// modal loop and a session cannot drag payloads out to the OS, so this
    /// is a precise boundary: drop targets still receive HTML5 file drops.
    /// </summary>
    public static DragDropEffects DoDragDrop(DependencyObject dragSource, object data, DragDropEffects allowedEffects)
    {
        ArgumentNullException.ThrowIfNull(dragSource);
        ArgumentNullException.ThrowIfNull(data);
        throw new NotSupportedException(
            "DragDrop.DoDragDrop needs the desktop OLE modal loop, which the browser session has no equivalent for. " +
            "Drop targets still receive HTML5 file drops through UIElement.RaiseDragEvent.");
    }

    private static UIElement Target(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target is UIElement element) return element;
        throw new NotSupportedException("DragDrop handlers attach to UIElement targets in the session.");
    }
}
