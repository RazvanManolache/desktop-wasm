using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Xml;

namespace DesktopWasm.Wpf;

// XAML + app-lifecycle cluster (PresentationFramework subset, shim, not
// port): the ScreenToGif App wiring (Startup/Exit args + handlers,
// WindowCollection, Row/ColumnDefinitionCollections, HeaderedContentControl)
// and the XAML runtime services its Util layer clones through
// (XamlReader/XamlWriter/XamlParseException). PasswordBox stays an explicit
// non-goal: the loader keeps rejecting it as the unknown-element canary.

/// <summary>App-startup data (matches System.Windows.StartupEventArgs).</summary>
public sealed class StartupEventArgs : EventArgs
{
    public StartupEventArgs() : this([]) { }

    public StartupEventArgs(string[] args)
    {
        Args = args ?? [];
    }

    /// <summary>Command-line arguments (empty when the host supplies none).</summary>
    public string[] Args { get; set; }
}

/// <summary>App-startup handler (matches System.Windows.StartupEventHandler).</summary>
public delegate void StartupEventHandler(object sender, StartupEventArgs e);

/// <summary>App-exit data (matches System.Windows.ExitEventArgs).</summary>
public sealed class ExitEventArgs : EventArgs
{
    public ExitEventArgs() : this(0) { }

    public ExitEventArgs(int applicationExitCode)
    {
        ApplicationExitCode = applicationExitCode;
    }

    /// <summary>Process exit code (retained; the browser host owns the loop).</summary>
    public int ApplicationExitCode { get; set; }
}

/// <summary>App-exit handler (matches System.Windows.ExitEventHandler).</summary>
public delegate void ExitEventHandler(object sender, ExitEventArgs e);

/// <summary>App shutdown policy (matches System.Windows.ShutdownMode).</summary>
public enum ShutdownMode
{
    OnLastWindowClose = 0,
    OnMainWindowClose = 1,
    OnExplicitShutdown = 2,
}

/// <summary>
/// Live-window set (matches System.Windows.WindowCollection): the owning
/// <see cref="Application"/> tracks constructed windows and drops them on
/// close, like WPF. Read-only to callers (mutation methods throw, like WPF).
/// </summary>
public sealed class WindowCollection : IList<Window>, IList
{
    private readonly List<Window> _windows = new();
    private readonly object _sync = new();

    internal void AddInternal(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_sync)
        {
            if (!_windows.Contains(window)) _windows.Add(window);
        }
    }

    internal bool RemoveInternal(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_sync) return _windows.Remove(window);
    }

    public int Count { get { lock (_sync) return _windows.Count; } }

    public bool IsReadOnly => true;

    public Window this[int index]
    {
        get { lock (_sync) return _windows[index]; }
        set => throw new NotSupportedException("WindowCollection is read-only; windows register on construction and leave on close.");
    }

    public bool Contains(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_sync) return _windows.Contains(window);
    }

    public int IndexOf(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_sync) return _windows.IndexOf(window);
    }

    public void CopyTo(Window[] array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        lock (_sync) _windows.CopyTo(array, index);
    }

    public IEnumerator<Window> GetEnumerator()
    {
        List<Window> snapshot;
        lock (_sync) snapshot = new List<Window>(_windows);
        return snapshot.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => _sync;

    bool IList.IsFixedSize => true;

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException("WindowCollection is read-only; windows register on construction and leave on close.");
    }

    void IList.Clear() => throw new NotSupportedException("WindowCollection is read-only; windows register on construction and leave on close.");

    void IList.RemoveAt(int index) => throw new NotSupportedException("WindowCollection is read-only; windows register on construction and leave on close.");

    int IList.Add(object? value) => throw new NotSupportedException("WindowCollection is read-only; windows register on construction and leave on close.");

    bool IList.Contains(object? value) => value is Window w && Contains(w);

    int IList.IndexOf(object? value) => value is Window w ? IndexOf(w) : -1;

    void IList.Insert(int index, object? value) => throw new NotSupportedException("WindowCollection is read-only; windows register on construction and leave on close.");

    void IList.Remove(object? value) => throw new NotSupportedException("WindowCollection is read-only; windows register on construction and leave on close.");

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        lock (_sync) ((ICollection)_windows).CopyTo(array, index);
    }

    void ICollection<Window>.Add(Window window) => throw new NotSupportedException("WindowCollection is read-only; windows register on construction and leave on close.");

    void ICollection<Window>.Clear() => throw new NotSupportedException("WindowCollection is read-only; windows register on construction and leave on close.");

    bool ICollection<Window>.Remove(Window window) => throw new NotSupportedException("WindowCollection is read-only; windows register on construction and leave on close.");

    void IList<Window>.Insert(int index, Window window) => throw new NotSupportedException("WindowCollection is read-only; windows register on construction and leave on close.");

    void IList<Window>.RemoveAt(int index) => throw new NotSupportedException("WindowCollection is read-only; windows register on construction and leave on close.");
}

/// <summary>
/// Row definitions (matches System.Windows.Controls.RowDefinitionCollection).
/// </summary>
public sealed class RowDefinitionCollection : Collection<RowDefinition>
{
    /// <summary>Removes a range (matches the WPF definition-collection API).</summary>
    public void RemoveRange(int index, int count)
    {
        if (index < 0 || count < 0 || index + count > Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        for (var i = 0; i < count; i++) RemoveAt(index);
    }

    /// <summary>Always false (plain in-memory list, like the WPF collection).</summary>
    public bool IsSynchronized => false;

    /// <summary>Sync root (this, like the WPF collection).</summary>
    public object SyncRoot => this;
}

/// <summary>
/// Column definitions (matches System.Windows.Controls.ColumnDefinitionCollection).
/// </summary>
public sealed class ColumnDefinitionCollection : Collection<ColumnDefinition>
{
    /// <summary>Removes a range (matches the WPF definition-collection API).</summary>
    public void RemoveRange(int index, int count)
    {
        if (index < 0 || count < 0 || index + count > Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        for (var i = 0; i < count; i++) RemoveAt(index);
    }

    /// <summary>Always false (plain in-memory list, like the WPF collection).</summary>
    public bool IsSynchronized => false;

    /// <summary>Sync root (this, like the WPF collection).</summary>
    public object SyncRoot => this;
}

/// <summary>
/// Labeled content host (matches System.Windows.Controls.HeaderedContentControl):
/// a header row above content. Header visuals host live; text headers render
/// through an internal caption. Templates are retained (HeaderTemplate/
/// Selector) for converted lookup code; visual template inflation stays a
/// later milestone, like ContentControl.
/// </summary>
[ContentProperty("Content")]
public class HeaderedContentControl : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register("Header", typeof(object), typeof(HeaderedContentControl),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty HeaderTemplateProperty =
        DependencyProperty.Register("HeaderTemplate", typeof(DataTemplate), typeof(HeaderedContentControl),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty HeaderTemplateSelectorProperty =
        DependencyProperty.Register("HeaderTemplateSelector", typeof(DataTemplateSelector), typeof(HeaderedContentControl),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty HeaderStringFormatProperty =
        DependencyProperty.Register("HeaderStringFormat", typeof(string), typeof(HeaderedContentControl),
            new FrameworkPropertyMetadata(null));

    private static readonly DependencyPropertyKey HasHeaderKey =
        DependencyProperty.RegisterReadOnly("HasHeader", typeof(bool), typeof(HeaderedContentControl),
            new FrameworkPropertyMetadata(false));

    /// <summary>Header presence token (matches HasHeaderProperty; read-only like WPF).</summary>
    public static readonly DependencyProperty HasHeaderProperty = HasHeaderKey.DependencyProperty;

    private readonly TextBlock _caption = new();
    private UIElement? _headerVisual;

    /// <summary>Header content (string renders as a caption; visuals host live).</summary>
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set
        {
            if (Equals(GetValue(HeaderProperty), value)) return;
            SetValue(HeaderProperty, value);
            SetValue(HasHeaderKey, value is not null && value.ToString()?.Length > 0);
            SyncHeader();
            InvalidateMeasure();
        }
    }

    /// <summary>Header template (retained for converted lookup code).</summary>
    public DataTemplate? HeaderTemplate
    {
        get => (DataTemplate?)GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    /// <summary>Header template selector (retained for converted lookup code).</summary>
    public DataTemplateSelector? HeaderTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(HeaderTemplateSelectorProperty);
        set => SetValue(HeaderTemplateSelectorProperty, value);
    }

    /// <summary>Header format string (retained; composite formatting stays a boundary).</summary>
    public string? HeaderStringFormat
    {
        get => (string?)GetValue(HeaderStringFormatProperty);
        set => SetValue(HeaderStringFormatProperty, value);
    }

    /// <summary>Whether a header is present (matches HasHeader).</summary>
    public bool HasHeader => (bool)(GetValue(HasHeaderProperty) ?? false);

    /// <summary>
    /// Visual content hosts live (Label parity); the header also hosts live
    /// visuals instead of going through the generic content slot.
    /// </summary>
    public override object Content
    {
        get => base.Content;
        set
        {
            if (Equals(base.Content, value)) return;
            var old = base.Content;
            base.Content = value;
            SyncContentVisual(old, value);
        }
    }

    private void SyncHeader()
    {
        if (_headerVisual is not null)
        {
            RemoveChild(_headerVisual);
            _headerVisual = null;
        }
        if (Header is UIElement visual)
        {
            _headerVisual = visual;
            AddChild(visual);
        }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var header = MeasureHeader(available);
        var content = MeasureContent(available, header.Height);
        return new WpfSize(Math.Max(header.Width, content.Width), header.Height + content.Height);
    }

    private WpfSize MeasureHeader(WpfSize available)
    {
        if (!HasHeader) return new WpfSize(0, 0);
        if (_headerVisual is not null)
        {
            _headerVisual.Measure(available);
            return _headerVisual.DesiredSize;
        }
        _caption.Text = Header?.ToString() ?? string.Empty;
        _caption.Measure(available);
        return _caption.DesiredSize;
    }

    private WpfSize MeasureContent(WpfSize available, double headerHeight)
    {
        var rest = new WpfSize(available.Width, Math.Max(0, available.Height - headerHeight));
        if (Content is UIElement visual)
        {
            visual.Measure(rest);
            return visual.DesiredSize;
        }
        return new WpfSize(0, 0);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        var headerHeight = 0.0;
        if (HasHeader)
        {
            if (_headerVisual is not null)
            {
                _headerVisual.Arrange(new WpfRect(finalRect.X, finalRect.Y, finalRect.Width, _headerVisual.DesiredSize.Height));
                headerHeight = _headerVisual.DesiredSize.Height;
            }
            else
            {
                _caption.Arrange(new WpfRect(finalRect.X, finalRect.Y, finalRect.Width, _caption.DesiredSize.Height));
                headerHeight = _caption.DesiredSize.Height;
            }
        }
        var body = new WpfRect(finalRect.X, finalRect.Y + headerHeight,
            finalRect.Width, Math.Max(0, finalRect.Height - headerHeight));
        if (Content is UIElement visual) visual.Arrange(body);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (HasHeader)
        {
            if (_headerVisual is not null) RenderVisible(dc, _headerVisual);
            else { _caption.Text = Header?.ToString() ?? string.Empty; _caption.Render(dc); }
        }
        if (Content is UIElement visual) RenderVisible(dc, visual);
    }
}

/// <summary>
/// XAML load failure (matches System.Windows.Markup.XamlParseException):
/// the engine loader's <see cref="XamlLoadException"/> is translated here
/// so converted catch sites (ScreenToGif App error reporting) bind.
/// </summary>
public sealed class XamlParseException : SystemException
{
    public XamlParseException() : this("XAML parsing failed.") { }

    public XamlParseException(string? message) : base(message) { }

    public XamlParseException(string? message, Exception? innerException) : base(message, innerException) { }

    public XamlParseException(string message, int lineNumber, int linePosition)
        : base(FormattableString.Invariant($"{message} (line {lineNumber}, position {linePosition})"))
    {
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    public XamlParseException(string message, int lineNumber, int linePosition, Exception? innerException)
        : base(FormattableString.Invariant($"{message} (line {lineNumber}, position {linePosition})"), innerException)
    {
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    /// <summary>One-based XAML line (0 when unknown).</summary>
    public int LineNumber { get; }

    /// <summary>One-based XAML position (0 when unknown).</summary>
    public int LinePosition { get; }

    /// <summary>Source URI (null for in-memory text, like WPF without a parser context).</summary>
    public Uri? BaseUri { get; set; }

    /// <summary>Key context (parser-context scenarios stay a boundary).</summary>
    public object? KeyContext { get; set; }

    /// <summary>Name context (parser-context scenarios stay a boundary).</summary>
    public string? NameContext { get; set; }

    /// <summary>Uid context (parser-context scenarios stay a boundary).</summary>
    public string? UidContext { get; set; }
}

/// <summary>
/// XAML object reader (matches System.Windows.Markup.XamlReader): loose-XAML
/// streams, XmlReader content and Parse text hydrate through the engine
/// loader, so converted ResourceDictionary round-trips (ScreenToGif
/// localization) and element clones resolve. Compiled MS BAML routes through
/// <see cref="BamlReader"/>; data-binding and parser contexts stay precise
/// boundaries, like the underlying loader.
/// </summary>
public static class XamlReader
{
    /// <summary>Reads an object graph from XAML text (ResourceDictionary or element).</summary>
    public static object Parse(string xamlText)
    {
        ArgumentNullException.ThrowIfNull(xamlText);
        return LoadText(xamlText.TrimStart('﻿').TrimStart());
    }

    /// <summary>Reads an object graph from a stream (loose XAML text or BAML).</summary>
    public static object Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var probe = new MemoryStream();
        stream.CopyTo(probe);
        var bytes = probe.ToArray();
        if (IsBaml(bytes))
        {
            try { return BamlReader.LoadRoot(bytes); }
            catch (Exception ex) when (ex is not XamlParseException)
            {
                throw new XamlParseException($"BAML load failed: {ex.GetBaseException().Message}", ex);
            }
        }
        var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿').TrimStart();
        if (text.Length == 0) throw new XamlParseException("XAML stream is empty.");
        return LoadText(text);
    }

    /// <summary>
    /// Reads an object graph from an XML reader (fresh or positioned on the
    /// root, like the VisualHelper DeepCopy call shape).
    /// </summary>
    public static object Load(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        try
        {
            reader.MoveToContent();
            return LoadText(reader.ReadOuterXml());
        }
        catch (XamlParseException) { throw; }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            throw new XamlParseException($"XAML reader failed: {ex.GetBaseException().Message}", ex);
        }
    }

    /// <summary>
    /// Reads an object graph from a stream with a parser context (matches
    /// XamlReader.Load(Stream, ParserContext); relative pack URIs resolve
    /// through the registered pack store, like the context-free load).
    /// </summary>
    public static object Load(Stream stream, ParserContext parserContext)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(parserContext);
        return Load(stream);
    }

    /// <summary>
    /// Reads an object graph from an XML reader with a parser context (matches
    /// XamlReader.Load(XmlReader, ParserContext); same store-based resolution
    /// as the context-free load).
    /// </summary>
    public static object Load(XmlReader reader, ParserContext parserContext)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(parserContext);
        return Load(reader);
    }

    private static object LoadText(string xaml)
    {
        string root;
        try
        {
            using var peek = XmlReader.Create(new StringReader(xaml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            peek.MoveToContent();
            if (peek.EOF || peek.NodeType != XmlNodeType.Element)
                throw new XamlParseException("Empty XAML document.");
            root = peek.LocalName;
        }
        catch (XamlParseException) { throw; }
        catch (Exception ex) when (ex is XmlException)
        {
            throw new XamlParseException($"Malformed XAML: {ex.Message}", ex);
        }
        try
        {
            if (string.Equals(root, "ResourceDictionary", StringComparison.Ordinal))
                return XamlLoader.LoadResourceDictionary(xaml);
            if (string.Equals(root, "DataTemplate", StringComparison.Ordinal) ||
                string.Equals(root, "HierarchicalDataTemplate", StringComparison.Ordinal))
                return XamlLoader.LoadDataTemplate(xaml);
            return XamlLoader.Load(xaml).Root;
        }
        catch (XamlLoadException ex)
        {
            throw new XamlParseException(ex.Message, ex.Line, ex.Position, ex);
        }
    }

    private static bool IsBaml(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == (byte)'M' && bytes[1] == (byte)'S' &&
        bytes[2] == (byte)'B' && bytes[3] == (byte)'A';
}

/// <summary>
/// XAML object writer (matches System.Windows.Markup.XamlWriter): serializes
/// engine resource dictionaries and plain element trees back to loose XAML
/// the reader (and <see cref="XamlReader.Load(Stream)"/>) accepts, so
/// converted save/clone paths (ScreenToGif localization export,
/// XamlClone/DeepCopy) round-trip. Serialized surface: resource entries the
/// loader reads back (String/Double/Int32/Boolean/Color/SolidColorBrush/
/// FontFamily/Thickness/CornerRadius) plus Source-only merged dictionaries;
/// element tag with Name/Width/Height/Text/Content/Header strings, grid
/// definitions and Grid/Canvas attached positions with nested children.
/// Brushes beyond solid color, templates, styles, bindings and resource
/// references on elements stay precise boundaries (loader parity).
/// </summary>
public static class XamlWriter
{
    /// <summary>Serializes an object graph to XAML text.</summary>
    public static string Save(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var settings = new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = true,
            ConformanceLevel = ConformanceLevel.Fragment,
        };
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        using (var writer = XmlWriter.Create(text, settings)) WriteNode(writer, obj, key: null);
        return text.ToString();
    }

    /// <summary>Serializes an object graph into a stream (UTF-8, no BOM).</summary>
    public static void Save(object obj, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(stream);
        var bytes = Encoding.UTF8.GetBytes(Save(obj));
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Serializes an object graph into a text writer.</summary>
    public static void Save(object obj, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(Save(obj));
    }

    /// <summary>Serializes an object graph into an XML writer.</summary>
    public static void Save(object obj, XmlWriter xmlWriter)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(xmlWriter);
        xmlWriter.WriteRaw(Save(obj));
        xmlWriter.Flush();
    }

    private static void WriteNode(XmlWriter writer, object obj, string? key)
    {
        switch (obj)
        {
            case ResourceDictionary dictionary:
                if (key is not null)
                {
                    throw new InvalidOperationException(
                        "XamlWriter.Save cannot nest a ResourceDictionary as a resource entry (dictionaries serialize as roots only).");
                }
                WriteDictionary(writer, dictionary, key);
                return;
            case UIElement element:
                WriteElement(writer, element);
                return;
            case string text:
                writer.WriteStartElement("String");
                WriteKey(writer, key);
                writer.WriteString(text);
                writer.WriteEndElement();
                return;
            case double number:
                writer.WriteStartElement("Double");
                WriteKey(writer, key);
                writer.WriteString(number.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
                return;
            case int integer:
                writer.WriteStartElement("Int32");
                WriteKey(writer, key);
                writer.WriteString(integer.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
                return;
            case bool flag:
                writer.WriteStartElement("Boolean");
                WriteKey(writer, key);
                writer.WriteString(flag ? "True" : "False");
                writer.WriteEndElement();
                return;
            case Color color:
                writer.WriteStartElement("Color");
                WriteKey(writer, key);
                writer.WriteString(color.ToString());
                writer.WriteEndElement();
                return;
            case SolidColorBrush solid:
                writer.WriteStartElement("SolidColorBrush");
                WriteKey(writer, key);
                writer.WriteAttributeString("Color", solid.Color.ToString());
                writer.WriteEndElement();
                return;
            case FontFamily family:
                writer.WriteStartElement("FontFamily");
                WriteKey(writer, key);
                writer.WriteString(family.Source);
                writer.WriteEndElement();
                return;
            case Thickness thickness:
                writer.WriteStartElement("Thickness");
                WriteKey(writer, key);
                writer.WriteString(FormattableString.Invariant($"{thickness.Left},{thickness.Top},{thickness.Right},{thickness.Bottom}"));
                writer.WriteEndElement();
                return;
            case CornerRadius corner:
                writer.WriteStartElement("CornerRadius");
                WriteKey(writer, key);
                writer.WriteString(FormattableString.Invariant($"{corner.TopLeft},{corner.TopRight},{corner.BottomRight},{corner.BottomLeft}"));
                writer.WriteEndElement();
                return;
            default:
                throw new InvalidOperationException(
                    $"XamlWriter.Save cannot serialize '{obj.GetType().FullName}' " +
                    "(resource entries: String/Double/Int32/Boolean/Color/SolidColorBrush/FontFamily/Thickness/CornerRadius; elements: plain UIElement trees).");
        }
    }

    private static void WriteKey(XmlWriter writer, string? key)
    {
        if (key is not null)
            writer.WriteAttributeString("x", "Key", "http://schemas.microsoft.com/winfx/2006/xaml", key);
    }

    private static void WriteDictionary(XmlWriter writer, ResourceDictionary dictionary, string? key)
    {
        writer.WriteStartElement("ResourceDictionary", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        writer.WriteAttributeString("xmlns", "x", null, "http://schemas.microsoft.com/winfx/2006/xaml");
        WriteKey(writer, key);
        if (dictionary.Source is not null && dictionary.EntryCount() > 0)
        {
            throw new InvalidOperationException(
                "XamlWriter.Save cannot serialize a Source dictionary that also holds inline entries (Source dictionaries serialize as references only).");
        }
        foreach (var entry in dictionary.LocalEntries())
            WriteNode(writer, entry.Value ?? string.Empty, entry.Key?.ToString());
        if (dictionary.MergedDictionaries.Count > 0)
        {
            writer.WriteStartElement("ResourceDictionary.MergedDictionaries");
            foreach (var merged in dictionary.MergedDictionaries)
            {
                if (merged.Source is null)
                {
                    throw new InvalidOperationException(
                        "XamlWriter.Save serializes merged dictionaries as Source references (inline merged dictionaries stay a boundary).");
                }
                writer.WriteStartElement("ResourceDictionary");
                writer.WriteAttributeString("Source", merged.Source.OriginalString);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteElement(XmlWriter writer, UIElement element)
    {
        writer.WriteStartElement(element.GetType().Name);
        WriteElementAttributes(writer, element);
        switch (element)
        {
            case Window window:
                if (window.Title.Length > 0) writer.WriteAttributeString("Title", window.Title);
                break;
            case TextBlock text:
                if (text.Text.Length > 0) writer.WriteAttributeString("Text", text.Text);
                break;
            case TextBox box:
                if (box.Text.Length > 0) writer.WriteAttributeString("Text", box.Text);
                break;
            case HeaderedContentControl headered:
                if (headered.Header is string headerText && headerText.Length > 0)
                    writer.WriteAttributeString("Header", headerText);
                if (headered.Content is string headeredText && headeredText.Length > 0)
                    writer.WriteAttributeString("Content", headeredText);
                break;
            case ContentControl content when content.Content is string contentText && contentText.Length > 0:
                writer.WriteAttributeString("Content", contentText);
                break;
        }
        if (element is Grid grid && (grid.RowDefinitions.Count > 0 || grid.ColumnDefinitions.Count > 0))
            WriteGridDefinitions(writer, grid);
        WriteChildContent(writer, element);
        writer.WriteEndElement();
    }

    private static void WriteGridDefinitions(XmlWriter writer, Grid grid)
    {
        if (grid.RowDefinitions.Count > 0)
        {
            writer.WriteStartElement("Grid.RowDefinitions");
            foreach (var row in grid.RowDefinitions)
            {
                writer.WriteStartElement("RowDefinition");
                writer.WriteAttributeString("Height", GridLengthToString(row.Height));
                if (row.MinHeight != 0) writer.WriteAttributeString("MinHeight", row.MinHeight.ToString(CultureInfo.InvariantCulture));
                if (!double.IsPositiveInfinity(row.MaxHeight)) writer.WriteAttributeString("MaxHeight", row.MaxHeight.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        if (grid.ColumnDefinitions.Count > 0)
        {
            writer.WriteStartElement("Grid.ColumnDefinitions");
            foreach (var column in grid.ColumnDefinitions)
            {
                writer.WriteStartElement("ColumnDefinition");
                writer.WriteAttributeString("Width", GridLengthToString(column.Width));
                if (column.MinWidth != 0) writer.WriteAttributeString("MinWidth", column.MinWidth.ToString(CultureInfo.InvariantCulture));
                if (!double.IsPositiveInfinity(column.MaxWidth)) writer.WriteAttributeString("MaxWidth", column.MaxWidth.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
    }

    private static string GridLengthToString(GridLength length) => length.Unit switch
    {
        GridUnitType.Auto => "Auto",
        GridUnitType.Star when length.Value == 1 => "*",
        GridUnitType.Star => length.Value.ToString(CultureInfo.InvariantCulture) + "*",
        _ => length.Value.ToString(CultureInfo.InvariantCulture),
    };

    private static void WriteContentValue(XmlWriter writer, object? content)
    {
        if (content is null || content is string) return;
        if (content is UIElement visual) { WriteElement(writer, visual); return; }
        throw new InvalidOperationException(
            $"XamlWriter.Save cannot serialize a '{content.GetType().FullName}' Content value (strings and UIElement visuals only).");
    }

    private static void WriteChildContent(XmlWriter writer, UIElement element)
    {
        switch (element)
        {
            case HeaderedContentControl headered:
                if (headered.Header is UIElement headerVisual)
                {
                    writer.WriteStartElement("HeaderedContentControl.Header");
                    WriteElement(writer, headerVisual);
                    writer.WriteEndElement();
                }
                else if (headered.Header is not null && headered.Header is not string)
                {
                    throw new InvalidOperationException(
                        $"XamlWriter.Save cannot serialize a '{headered.Header.GetType().FullName}' Header (strings and UIElement visuals only).");
                }
                WriteContentValue(writer, headered.Content);
                return;
            case ContentControl content:
                WriteContentValue(writer, content.Content);
                return;
            case Decorator decorator when decorator.Child is not null:
                WriteElement(writer, decorator.Child);
                return;
            case Popup popup when popup.Child is not null:
                WriteElement(writer, popup.Child);
                return;
            case FrameworkElement host:
                foreach (var child in host.LogicalChildren)
                    WriteElement(writer, child);
                return;
            default:
                return;
        }
    }

    private static void WriteElementAttributes(XmlWriter writer, UIElement element)
    {
        if (element is FrameworkElement fe)
        {
            if (fe.Name.Length > 0) writer.WriteAttributeString("Name", fe.Name);
            if (!double.IsNaN(fe.Width)) writer.WriteAttributeString("Width", fe.Width.ToString(CultureInfo.InvariantCulture));
            if (!double.IsNaN(fe.Height)) writer.WriteAttributeString("Height", fe.Height.ToString(CultureInfo.InvariantCulture));
        }
        WriteAttached(writer, element, "Grid.Row");
        WriteAttached(writer, element, "Grid.Column");
        WriteAttached(writer, element, "Grid.RowSpan");
        WriteAttached(writer, element, "Grid.ColumnSpan");
        WriteAttached(writer, element, "Canvas.Left");
        WriteAttached(writer, element, "Canvas.Top");
    }

    private static void WriteAttached(XmlWriter writer, UIElement element, string name)
    {
        var value = element.GetAttached(name);
        if (value is null) return;
        // Loader defaults: Row/Column 0 and RowSpan/ColumnSpan 1 are
        // implicit, so only non-default positions serialize.
        if (name is "Grid.Row" or "Grid.Column" && value is int i && i == 0) return;
        if (name is "Grid.RowSpan" or "Grid.ColumnSpan" && value is int span && span <= 1) return;
        var text = value is double d ? d.ToString(CultureInfo.InvariantCulture) : value.ToString();
        if (text is not null) writer.WriteAttributeString(name, text);
    }
}
