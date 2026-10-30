namespace DesktopWasm.Wpf;

// Slice-72 controls/XAML slice, part 1 (live shapes verified 2026-10-26
// with Mono.Cecil against the 9.0.20 PresentationFramework/PresentationCore/
// WindowsBase ref packs): the weak-event manager contract underneath
// CollectionChangedEventManager, the item-generation change notices, the
// bring-into-view route, and the layout-slot inspector.
//
// Converted callers pin every member: TreeListViewItem/TreeListViewItemsCollection
// drive AddHandler/RemoveHandler, TabControlHeaderScrollBehavior subscribes
// ItemContainerGenerator.ItemsChanged, PaddedBringIntoViewStackPanel registers
// against FrameworkElement.RequestBringIntoViewEvent, and
// ExtendedVisualStateManager reads GetLayoutSlot.

/// <summary>Weak listener (matches System.Windows.IWeakEventListener).</summary>
public interface IWeakEventListener
{
    /// <summary>Delivers an event; false unsubscribes the listener.</summary>
    bool ReceiveWeakEvent(Type managerType, object? sender, EventArgs e);
}

/// <summary>
/// Weak-event base (matches System.Windows.WeakEventManager): managers hold
/// listeners weakly so sources never leak subscribers. Listener lists are
/// per source; delivery purges collected listeners and drops the ones that
/// answer false, like WPF.
/// </summary>
public class WeakEventManager : DispatcherObject
{
    private static readonly Dictionary<Type, WeakEventManager> Managers = new();
    private static readonly object ManagerLock = new();
    private readonly Dictionary<object, ListenerList> _lists = new(ReferenceEqualityComparer.Instance);
    private readonly object _listLock = new();

    /// <summary>Creates a manager (matches the protected WeakEventManager ctor).</summary>
    protected WeakEventManager()
    {
    }

    /// <summary>Manager registered for a manager type (matches GetCurrentManager).</summary>
    public static WeakEventManager? GetCurrentManager(Type managerType)
    {
        ArgumentNullException.ThrowIfNull(managerType);
        lock (ManagerLock)
            return Managers.TryGetValue(managerType, out var manager) ? manager : null;
    }

    /// <summary>Registers a manager for a manager type (matches SetCurrentManager).</summary>
    public static void SetCurrentManager(Type managerType, WeakEventManager manager)
    {
        ArgumentNullException.ThrowIfNull(managerType);
        ArgumentNullException.ThrowIfNull(manager);
        lock (ManagerLock)
            Managers[managerType] = manager;
    }

    /// <summary>Finds the listener list for a source, if any.</summary>
    protected bool TryGetList(object source, out ListenerList? list)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_listLock)
            return _lists.TryGetValue(source, out list);
    }

    /// <summary>Drops a source's list after stopping its subscription.</summary>
    protected void DropSource(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_listLock)
        {
            if (_lists.Remove(source))
                StopListening(source);
        }
    }

    /// <summary>Subscribes a weak listener on a source (matches ProtectedAddListener).</summary>
    protected void ProtectedAddListener(object source, IWeakEventListener listener) =>
        AddListener(source, listener);

    /// <summary>Subscribes a weak listener on a source (matches AddListener).</summary>
    protected void AddListener(object source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        lock (_listLock)
        {
            if (!_lists.TryGetValue(source, out var list))
            {
                list = NewListenerList();
                _lists[source] = list;
                StartListening(source);
            }
            else if (ContainsListener(list, listener))
            {
                OnListenerExists(source, listener);
                return;
            }
            list.Add(listener);
        }
        ScheduleCleanup();
    }

    /// <summary>Unsubscribes a weak listener (matches ProtectedRemoveListener).</summary>
    protected void ProtectedRemoveListener(object source, IWeakEventListener listener) =>
        RemoveListener(source, listener);

    /// <summary>Unsubscribes a weak listener (matches RemoveListener).</summary>
    protected void RemoveListener(object source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        lock (_listLock)
        {
            if (!_lists.TryGetValue(source, out var list)) return;
            list.Remove(listener);
            if (list.IsEmpty)
            {
                _lists.Remove(source);
                StopListening(source);
            }
        }
    }

    /// <summary>Subscribes to the source event (matches StartListening).</summary>
    protected virtual void StartListening(object source)
    {
    }

    /// <summary>Unsubscribes from the source event (matches StopListening).</summary>
    protected virtual void StopListening(object source)
    {
    }

    /// <summary>Delivers an event to every list of this manager (matches DeliverEvent).</summary>
    protected void DeliverEvent(object? sender, EventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        List<ListenerList> snapshot;
        lock (_listLock)
            snapshot = new List<ListenerList>(_lists.Values);
        foreach (var list in snapshot)
            DeliverEventToList(sender, args, list);
    }

    /// <summary>Delivers an event to one list, purging dead listeners (matches DeliverEventToList).</summary>
    protected void DeliverEventToList(object? sender, EventArgs args, ListenerList list)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(list);
        foreach (var listener in list.BeginUse())
        {
            if (!listener.TryGetTarget(out var target))
                continue;
            if (!target.ReceiveWeakEvent(GetType(), sender, args))
                list.QueueRemove(target);
        }
        list.EndUse();
    }

    /// <summary>Creates a listener list (matches NewListenerList).</summary>
    protected ListenerList NewListenerList() => new();

    /// <summary>Runs a purge pass (matches ScheduleCleanup).</summary>
    protected virtual bool ScheduleCleanup()
    {
        lock (_listLock)
        {
            foreach (var list in _lists.Values)
                list.Purge();
        }
        return true;
    }

    /// <summary>Ran when a listener subscribes twice (matches OnListenerExists).</summary>
    protected virtual void OnListenerExists(object source, IWeakEventListener listener)
    {
    }

    private static bool ContainsListener(ListenerList list, IWeakEventListener listener)
    {
        var snapshot = list.BeginUse();
        try
        {
            foreach (var entry in snapshot)
            {
                if (entry.TryGetTarget(out var target) && ReferenceEquals(target, listener))
                    return true;
            }
            return false;
        }
        finally
        {
            list.EndUse();
        }
    }

    /// <summary>
    /// Weak listener list (matches WeakEventManager.ListenerList): entries
    /// are weak so collected listeners stop receiving without unsubscribing.
    /// </summary>
    protected class ListenerList
    {
        private readonly List<WeakReference<IWeakEventListener>> _entries = new();
        private readonly List<IWeakEventListener> _queuedRemovals = new();
        private int _users;

        /// <summary>Live entry count (dead entries purge on next delivery).</summary>
        public int Count
        {
            get
            {
                Purge();
                return _entries.Count;
            }
        }

        /// <summary>True when no live listener remains.</summary>
        public bool IsEmpty
        {
            get
            {
                Purge();
                return _entries.Count == 0;
            }
        }

        /// <summary>Appends a listener (matches Add).</summary>
        public void Add(IWeakEventListener listener)
        {
            ArgumentNullException.ThrowIfNull(listener);
            _entries.Add(new WeakReference<IWeakEventListener>(listener));
        }

        /// <summary>Removes a listener (matches Remove).</summary>
        public void Remove(IWeakEventListener listener)
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].TryGetTarget(out var target) && ReferenceEquals(target, listener))
                    _entries.RemoveAt(i);
            }
            Purge();
        }

        /// <summary>Drops collected listeners (matches Purge).</summary>
        public void Purge()
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (!_entries[i].TryGetTarget(out _))
                    _entries.RemoveAt(i);
            }
        }

        /// <summary>Begins a delivery walk (matches BeginUse).</summary>
        public IReadOnlyList<WeakReference<IWeakEventListener>> BeginUse()
        {
            _users++;
            return _entries.ToArray();
        }

        /// <summary>Ends a delivery walk, applying queued removals (matches EndUse).</summary>
        public void EndUse()
        {
            _users--;
            if (_users == 0 && _queuedRemovals.Count > 0)
            {
                foreach (var listener in _queuedRemovals)
                    Remove(listener);
                _queuedRemovals.Clear();
            }
            Purge();
        }

        /// <summary>Defers a removal until the walk ends (listener answered false).</summary>
        public void QueueRemove(IWeakEventListener listener)
        {
            ArgumentNullException.ThrowIfNull(listener);
            if (_users > 0) _queuedRemovals.Add(listener);
            else Remove(listener);
        }
    }
}

/// <summary>
/// Collection-change weak router (matches
/// System.Collections.Specialized.CollectionChangedEventManager): converted
/// tree controls subscribe through AddHandler/RemoveHandler or AddListener/
/// RemoveListener without rooting the subscriber in the source.
/// </summary>
public class CollectionChangedEventManager : WeakEventManager
{
    private CollectionChangedEventManager()
    {
    }

    private static CollectionChangedEventManager Current
    {
        get
        {
            var current = (CollectionChangedEventManager?)GetCurrentManager(typeof(CollectionChangedEventManager));
            if (current is null)
            {
                current = new CollectionChangedEventManager();
                SetCurrentManager(typeof(CollectionChangedEventManager), current);
            }
            return current;
        }
    }

    /// <summary>Subscribes a handler weakly (matches AddHandler).</summary>
    public static void AddHandler(System.Collections.Specialized.INotifyCollectionChanged source, System.Collections.Specialized.NotifyCollectionChangedEventHandler handler) =>
        Current.ProtectedAddListener(source, handler);

    /// <summary>Unsubscribes a handler (matches RemoveHandler).</summary>
    public static void RemoveHandler(System.Collections.Specialized.INotifyCollectionChanged source, System.Collections.Specialized.NotifyCollectionChangedEventHandler handler) =>
        Current.ProtectedRemoveListener(source, handler);

    /// <summary>Subscribes a weak listener (matches AddListener).</summary>
    public static void AddListener(System.Collections.Specialized.INotifyCollectionChanged source, IWeakEventListener listener) =>
        Current.AddListenerCore(source, listener);

    /// <summary>Unsubscribes a weak listener (matches RemoveListener).</summary>
    public static void RemoveListener(System.Collections.Specialized.INotifyCollectionChanged source, IWeakEventListener listener) =>
        Current.RemoveListenerCore(source, listener);

    private void AddListenerCore(System.Collections.Specialized.INotifyCollectionChanged source, IWeakEventListener listener) =>
        base.AddListener(source, listener);

    private void RemoveListenerCore(System.Collections.Specialized.INotifyCollectionChanged source, IWeakEventListener listener) =>
        base.RemoveListener(source, listener);

    private void ProtectedAddListener(System.Collections.Specialized.INotifyCollectionChanged source, System.Collections.Specialized.NotifyCollectionChangedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        AddListener(source, new HandlerListener(handler));
    }

    private void ProtectedRemoveListener(System.Collections.Specialized.INotifyCollectionChanged source, System.Collections.Specialized.NotifyCollectionChangedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        if (!TryGetList(source, out var list) || list is null) return;
        foreach (var entry in list.BeginUse())
        {
            if (entry.TryGetTarget(out var target) && target is HandlerListener listener && listener.Matches(handler))
                list.QueueRemove(target);
        }
        list.EndUse();
        if (list.IsEmpty) RemoveSource(source);
    }

    private void RemoveSource(object source) => DropSource(source);

    /// <inheritdoc />
    protected override void StartListening(object source)
    {
        if (source is System.Collections.Specialized.INotifyCollectionChanged changes)
            changes.CollectionChanged += OnCollectionChanged;
    }

    /// <inheritdoc />
    protected override void StopListening(object source)
    {
        if (source is System.Collections.Specialized.INotifyCollectionChanged changes)
            changes.CollectionChanged -= OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        DeliverEvent(sender, e);

    private sealed class HandlerListener : IWeakEventListener
    {
        private readonly WeakReference<System.Collections.Specialized.NotifyCollectionChangedEventHandler> _handler;

        public HandlerListener(System.Collections.Specialized.NotifyCollectionChangedEventHandler handler) =>
            _handler = new WeakReference<System.Collections.Specialized.NotifyCollectionChangedEventHandler>(handler);

        public bool Matches(System.Collections.Specialized.NotifyCollectionChangedEventHandler handler) =>
            _handler.TryGetTarget(out var target) && target == handler;

        public bool ReceiveWeakEvent(Type managerType, object? sender, EventArgs e)
        {
            if (!_handler.TryGetTarget(out var handler)) return false;
            if (e is System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
                handler(sender, args);
            return true;
        }
    }
}

/// <summary>Generation position (matches System.Windows.Controls.Primitives.GeneratorPosition).</summary>
public struct GeneratorPosition : IEquatable<GeneratorPosition>
{
    /// <summary>Creates a position (matches GeneratorPosition(index, offset)).</summary>
    public GeneratorPosition(int index, int offset)
    {
        Index = index;
        Offset = offset;
    }

    /// <summary>Item index (-1 with no item).</summary>
    public int Index { get; set; }

    /// <summary>Offset from the index.</summary>
    public int Offset { get; set; }

    /// <inheritdoc />
    public bool Equals(GeneratorPosition other) => Index == other.Index && Offset == other.Offset;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GeneratorPosition other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Index, Offset);

    /// <inheritdoc />
    public override string ToString() => $"GeneratorPosition Index:'{Index}' Offset:'{Offset}'";
}

/// <summary>Item-container change notice (matches System.Windows.Controls.Primitives.ItemsChangedEventHandler).</summary>
public delegate void ItemsChangedEventHandler(object sender, ItemsChangedEventArgs e);

/// <summary>Item-container change data (matches System.Windows.Controls.Primitives.ItemsChangedEventArgs).</summary>
public class ItemsChangedEventArgs : EventArgs
{
    internal ItemsChangedEventArgs(
        System.Collections.Specialized.NotifyCollectionChangedAction action,
        GeneratorPosition position,
        GeneratorPosition oldPosition,
        int itemCount,
        int itemUICount)
    {
        Action = action;
        Position = position;
        OldPosition = oldPosition;
        ItemCount = itemCount;
        ItemUICount = itemUICount;
    }

    /// <summary>What changed.</summary>
    public System.Collections.Specialized.NotifyCollectionChangedAction Action { get; }

    /// <summary>Affected item count.</summary>
    public int ItemCount { get; }

    /// <summary>Affected container count.</summary>
    public int ItemUICount { get; }

    /// <summary>New position.</summary>
    public GeneratorPosition Position { get; }

    /// <summary>Old position.</summary>
    public GeneratorPosition OldPosition { get; }
}

/// <summary>Bring-into-view notice (matches System.Windows.RequestBringIntoViewEventArgs).</summary>
public class RequestBringIntoViewEventArgs : RoutedEventArgs
{
    internal RequestBringIntoViewEventArgs(DependencyObject targetObject)
    {
        TargetObject = targetObject ?? throw new ArgumentNullException(nameof(targetObject));
        TargetRect = Rect.Empty;
        RoutedEvent = FrameworkElement.RequestBringIntoViewEvent;
    }

    internal RequestBringIntoViewEventArgs(DependencyObject targetObject, Rect targetRect)
        : this(targetObject) => TargetRect = targetRect;

    /// <summary>Element to show (matches TargetObject).</summary>
    public DependencyObject TargetObject { get; internal set; }

    /// <summary>Sub-rectangle to show (matches TargetRect).</summary>
    public Rect TargetRect { get; internal set; }
}

/// <summary>Bring-into-view route (matches System.Windows.RequestBringIntoViewEventHandler).</summary>
public delegate void RequestBringIntoViewEventHandler(object sender, RequestBringIntoViewEventArgs e);

/// <summary>
/// Layout inspector (matches System.Windows.Controls.Primitives.LayoutInformation):
/// converted ExtendedVisualStateManager reads the layout slot of templated
/// elements.
/// </summary>
public static class LayoutInformation
{
    /// <summary>Layout clip of an element (matches GetLayoutClip).</summary>
    public static Geometry? GetLayoutClip(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.Clip;
    }

    /// <summary>Final arrange slot of an element (matches GetLayoutSlot).</summary>
    public static Rect GetLayoutSlot(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var slot = element.RenderRect;
        return new Rect(slot.X, slot.Y, slot.Width, slot.Height);
    }

    /// <summary>Last layout exception element for a dispatcher (matches GetLayoutExceptionElement).</summary>
    public static UIElement? GetLayoutExceptionElement(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return null;
    }
}

/// <summary>Spelling reform rule (matches System.Windows.Controls.SpellingReform).</summary>
public enum SpellingReform
{
    /// <summary>Both pre- and post-reform spellings accepted.</summary>
    PreAndPostreform = 0,

    /// <summary>Pre-reform spellings only.</summary>
    Prereform = 1,

    /// <summary>Post-reform spellings only.</summary>
    Postreform = 2,
}

/// <summary>
/// Spell-check attached surface (matches System.Windows.Controls.SpellCheck):
/// converted TextFieldAssist reads GetIsEnabled and SpellingError suggestions
/// off context menus. The switch is retained per editor; the proofing engine
/// itself has no browser path, so no suggestions are ever produced here.
/// </summary>
public sealed class SpellCheck
{
    private SpellCheck(TextBoxBase owner) => Owner = owner;

    private TextBoxBase Owner { get; }

    /// <summary>Switch store (matches SpellCheck.IsEnabledProperty).</summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(SpellCheck), new FrameworkPropertyMetadata(false));

    /// <summary>Reads the switch (matches GetIsEnabled).</summary>
    public static bool GetIsEnabled(TextBoxBase textBoxBase)
    {
        ArgumentNullException.ThrowIfNull(textBoxBase);
        return textBoxBase.GetValue(IsEnabledProperty) is true;
    }

    /// <summary>Writes the switch (matches SetIsEnabled).</summary>
    public static void SetIsEnabled(TextBoxBase textBoxBase, bool value)
    {
        ArgumentNullException.ThrowIfNull(textBoxBase);
        textBoxBase.SetValue(IsEnabledProperty, value);
    }

    /// <summary>Custom dictionaries for an editor (matches GetCustomDictionaries).</summary>
    public static System.Collections.IList GetCustomDictionaries(TextBoxBase textBoxBase)
    {
        ArgumentNullException.ThrowIfNull(textBoxBase);
        return textBoxBase.GetValue(CustomDictionariesProperty) as System.Collections.IList ?? new System.Collections.ArrayList();
    }

    private static readonly DependencyProperty CustomDictionariesProperty =
        DependencyProperty.RegisterAttached("CustomDictionaries", typeof(System.Collections.IList), typeof(SpellCheck),
            new FrameworkPropertyMetadata(null));

    /// <summary>Writes the reform rule (matches SetSpellingReform).</summary>
    public static void SetSpellingReform(TextBoxBase textBoxBase, SpellingReform value)
    {
        ArgumentNullException.ThrowIfNull(textBoxBase);
        textBoxBase.SetValue(SpellingReformProperty, value);
    }

    private static readonly DependencyProperty SpellingReformProperty =
        DependencyProperty.RegisterAttached("SpellingReform", typeof(SpellingReform), typeof(SpellCheck),
            new FrameworkPropertyMetadata(SpellingReform.PreAndPostreform));

    /// <summary>Custom dictionaries (matches CustomDictionaries).</summary>
    public System.Collections.IList CustomDictionaries => GetCustomDictionaries(Owner);

    /// <summary>Switch value (matches IsEnabled).</summary>
    public bool IsEnabled
    {
        get => GetIsEnabled(Owner);
        set => SetIsEnabled(Owner, value);
    }

    /// <summary>Reform rule (matches SpellingReform).</summary>
    public SpellingReform SpellingReform
    {
        get => Owner.GetValue(SpellingReformProperty) is SpellingReform reform ? reform : SpellingReform.PreAndPostreform;
        set => SetSpellingReform(Owner, value);
    }
}

/// <summary>
/// Spelling error (matches System.Windows.Controls.SpellingError): converted
/// TextFieldAssist reads Suggestions off the context-menu error. The engine
/// never produces errors (no proofing pipeline), so instances only arise in
/// tests and through the internal factory below.
/// </summary>
public class SpellingError
{
    private readonly List<string> _suggestions;
    private bool _corrected;
    private bool _ignored;

    internal SpellingError(IEnumerable<string>? suggestions = null) =>
        _suggestions = new List<string>(suggestions ?? Array.Empty<string>());

    /// <summary>Suggested replacements (matches Suggestions).</summary>
    public IEnumerable<string> Suggestions => _suggestions.AsReadOnly();

    /// <summary>Whether Correct ran.</summary>
    public bool IsCorrected => _corrected;

    /// <summary>Whether IgnoreAll ran.</summary>
    public bool IsIgnored => _ignored;

    /// <summary>Applies a correction (matches Correct).</summary>
    public void Correct(string correctedText)
    {
        ArgumentNullException.ThrowIfNull(correctedText);
        _corrected = true;
    }

    /// <summary>Ignores the error (matches IgnoreAll).</summary>
    public void IgnoreAll() => _ignored = true;
}

/// <summary>
/// XAML type-mapper marker (matches System.Windows.Markup.XamlTypeMapper):
/// full design-time type resolution has no browser path, so the engine
/// carries the slot the parser context owns without resolving through it.
/// </summary>
public sealed class XamlTypeMapper
{
    /// <summary>Empty mapper.</summary>
    public XamlTypeMapper()
    {
    }
}

/// <summary>Parser context (matches System.Windows.Markup.ParserContext).</summary>
public class ParserContext
{
    private Uri? _baseUri;
    private XamlTypeMapper? _mapper;
    private string? _xmlLang;
    private string? _xmlSpace;
    private XmlnsDictionary? _xmlnsDictionary;

    /// <summary>Empty context.</summary>
    public ParserContext()
    {
    }

    /// <summary>Context over an XML parser context (matches ParserContext(XmlParserContext)).</summary>
    public ParserContext(System.Xml.XmlParserContext xmlParserContext)
    {
        ArgumentNullException.ThrowIfNull(xmlParserContext);
        _baseUri = xmlParserContext.BaseURI.Length == 0 ? null : new Uri(xmlParserContext.BaseURI);
        _xmlLang = xmlParserContext.XmlLang.Length == 0 ? null : xmlParserContext.XmlLang;
        _xmlSpace = xmlParserContext.XmlSpace switch
        {
            System.Xml.XmlSpace.Preserve => "preserve",
            System.Xml.XmlSpace.Default => "default",
            _ => null,
        };
    }

    /// <summary>Base URI (matches BaseUri).</summary>
    public Uri? BaseUri
    {
        get => _baseUri;
        set => _baseUri = value;
    }

    /// <summary>Type mapper (matches XamlTypeMapper).</summary>
    public XamlTypeMapper? XamlTypeMapper
    {
        get => _mapper;
        set => _mapper = value;
    }

    /// <summary>Language (matches XmlLang).</summary>
    public string? XmlLang
    {
        get => _xmlLang;
        set => _xmlLang = value;
    }

    /// <summary>Whitespace handling (matches XmlSpace).</summary>
    public string? XmlSpace
    {
        get => _xmlSpace;
        set => _xmlSpace = value;
    }

    /// <summary>Namespace map (matches XmlnsDictionary).</summary>
    public XmlnsDictionary XmlnsDictionary => _xmlnsDictionary ??= new XmlnsDictionary();

    /// <summary>Converts back to an XML parser context (matches ToXmlParserContext).</summary>
    public static System.Xml.XmlParserContext ToXmlParserContext(ParserContext parserContext)
    {
        ArgumentNullException.ThrowIfNull(parserContext);
        var table = new System.Xml.XmlNamespaceManager(new System.Xml.NameTable());
        foreach (var prefix in parserContext.XmlnsDictionary.GetKnownPrefixes())
            table.AddNamespace(prefix, parserContext.XmlnsDictionary.GetNamespace(prefix) ?? string.Empty);
        var xml = new System.Xml.XmlParserContext(
            null,
            table,
            parserContext.XmlLang,
            parserContext.XmlSpace == "preserve" ? System.Xml.XmlSpace.Preserve : System.Xml.XmlSpace.Default);
        xml.BaseURI = parserContext.BaseUri?.OriginalString;
        return xml;
    }
}

/// <summary>
/// Namespace map (matches System.Windows.Markup.XmlnsDictionary): scoped
/// prefix table with the reserved xml/xmlns bindings, seal support, and
/// push/pop scope parity.
/// </summary>
public class XmlnsDictionary
{
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

    private readonly List<Dictionary<string, string>> _scopes = new() { new(StringComparer.Ordinal) };
    private bool _sealed;

    /// <summary>Empty map.</summary>
    public XmlnsDictionary()
    {
    }

    /// <summary>Copy (matches XmlnsDictionary(XmlnsDictionary)).</summary>
    public XmlnsDictionary(XmlnsDictionary xmlnsDictionary)
    {
        ArgumentNullException.ThrowIfNull(xmlnsDictionary);
        _scopes.Clear();
        foreach (var scope in xmlnsDictionary._scopes)
            _scopes.Add(new Dictionary<string, string>(scope, StringComparer.Ordinal));
    }

    /// <summary>Declared prefix count across open scopes.</summary>
    public int Count
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var scope in _scopes)
                foreach (var key in scope.Keys)
                    seen.Add(key);
            return seen.Count;
        }
    }

    /// <summary>Never fixed-size.</summary>
    public bool IsFixedSize => false;

    /// <summary>Sealed maps reject writes.</summary>
    public bool IsReadOnly => _sealed;

    /// <summary>Never thread-safe (single-threaded WASM).</summary>
    public bool IsSynchronized => false;

    /// <summary>Map identity lock.</summary>
    public object SyncRoot => _scopes;

    /// <summary>Sealed maps reject writes (matches Sealed).</summary>
    public bool Sealed => _sealed;

    /// <summary>All declared prefixes.</summary>
    public System.Collections.ICollection Keys
    {
        get
        {
            var seen = new List<string>();
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var scope in _scopes)
                foreach (var key in scope.Keys)
                    if (known.Add(key)) seen.Add(key);
            return seen;
        }
    }

    /// <summary>All declared namespaces in prefix order.</summary>
    public System.Collections.ICollection Values
    {
        get
        {
            var values = new List<string>();
            foreach (string prefix in Keys)
                values.Add(LookupNamespace(prefix) ?? string.Empty);
            return values;
        }
    }

    /// <summary>Namespace for a prefix, or null (matches Item[object]/Item[string]).</summary>
    public object? this[object prefix]
    {
        get => prefix is string name ? LookupNamespace(name) : null;
        set => Add(prefix, value);
    }

    /// <summary>Namespace for a prefix, or null (matches Item[string]).</summary>
    public string? this[string prefix]
    {
        get => LookupNamespace(prefix);
        set => Add(prefix, value);
    }

    /// <summary>Adds a mapping (matches Add(object, object)).</summary>
    public void Add(object prefix, object? xmlNamespace)
    {
        if (prefix is not string name) throw new ArgumentException("Prefix must be a string.", nameof(prefix));
        if (xmlNamespace is not string ns) throw new ArgumentException("Namespace must be a string.", nameof(xmlNamespace));
        Add(name, ns);
    }

    /// <summary>Adds a mapping (matches Add(string, string)).</summary>
    public void Add(string prefix, string xmlNamespace)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(xmlNamespace);
        ThrowIfSealed();
        _scopes[^1][prefix] = xmlNamespace;
    }

    /// <summary>Clears every open scope.</summary>
    public void Clear()
    {
        ThrowIfSealed();
        _scopes.Clear();
        _scopes.Add(new Dictionary<string, string>(StringComparer.Ordinal));
    }

    /// <summary>Whether a prefix is declared (matches Contains(object)).</summary>
    public bool Contains(object key) => key is string name && LookupNamespace(name) is not null;

    /// <summary>Copies entries (matches CopyTo(Array, int)).</summary>
    public void CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        foreach (string prefix in Keys)
            array.SetValue(new System.Collections.DictionaryEntry(prefix, LookupNamespace(prefix)), index++);
    }

    /// <summary>Copies entries (matches CopyTo(DictionaryEntry[], int)).</summary>
    public void CopyTo(System.Collections.DictionaryEntry[] array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        foreach (string prefix in Keys)
            array[index++] = new System.Collections.DictionaryEntry(prefix, LookupNamespace(prefix));
    }

    /// <summary>Default-namespace lookup (matches DefaultNamespace).</summary>
    public string DefaultNamespace() => LookupNamespace(string.Empty) ?? string.Empty;

    /// <summary>Namespace for a prefix, or null (matches GetNamespace).</summary>
    public string? GetNamespace(string prefix) => LookupNamespace(prefix);

    /// <summary>Namespace for a prefix, or null (matches LookupNamespace).</summary>
    public string? LookupNamespace(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix == "xml") return XmlNamespace;
        if (prefix == "xmlns") return XmlnsNamespace;
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].TryGetValue(prefix, out var ns)) return ns;
        }
        return null;
    }

    /// <summary>First prefix bound to a namespace, or null (matches LookupPrefix).</summary>
    public string? LookupPrefix(string xmlNamespace)
    {
        ArgumentNullException.ThrowIfNull(xmlNamespace);
        if (xmlNamespace == XmlNamespace) return "xml";
        if (xmlNamespace == XmlnsNamespace) return "xmlns";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
            foreach (var (prefix, ns) in _scopes[i])
            {
                if (ns == xmlNamespace && seen.Add(prefix)) return prefix;
            }
        }
        return null;
    }

    /// <summary>Declared prefixes (supports ToXmlParserContext).</summary>
    public IEnumerable<string> GetKnownPrefixes()
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in _scopes)
            foreach (var key in scope.Keys)
                if (known.Add(key)) yield return key;
    }

    // GetNamespacePrefixes is intentionally omitted: it returns
    // System.Xaml.NamespaceDeclaration, and System.Xaml ships in the
    // desktop-only WindowsDesktop pack (not the portable runtime the
    // engine builds against), so there is no portable declaration to
    // return. GetKnownPrefixes above covers the conversion-time need.

    /// <summary>Opens a scope (matches PushScope).</summary>
    public void PushScope()
    {
        ThrowIfSealed();
        _scopes.Add(new Dictionary<string, string>(StringComparer.Ordinal));
    }

    /// <summary>Closes the innermost scope (matches PopScope).</summary>
    public void PopScope()
    {
        ThrowIfSealed();
        if (_scopes.Count > 1)
            _scopes.RemoveAt(_scopes.Count - 1);
    }

    /// <summary>Removes a mapping (matches Remove(object)).</summary>
    public void Remove(object prefix)
    {
        if (prefix is not string name) throw new ArgumentException("Prefix must be a string.", nameof(prefix));
        Remove(name);
    }

    /// <summary>Removes a mapping (matches Remove(string)).</summary>
    public void Remove(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ThrowIfSealed();
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].Remove(prefix)) return;
        }
    }

    /// <summary>Seals the map (matches Seal).</summary>
    public void Seal() => _sealed = true;

    private void ThrowIfSealed()
    {
        if (_sealed) throw new InvalidOperationException("The dictionary is sealed and cannot be modified.");
    }
}

/// <summary>
/// Combo-box column (matches System.Windows.Controls.DataGridComboBoxColumn):
/// a bound column whose cells edit through a drop-down. Styles land on the
/// generated ComboBox; bindings flow through the engine binding engine.
/// </summary>
public class DataGridComboBoxColumn : DataGridBoundColumn
{
    /// <summary>Display style store (matches ElementStyleProperty).</summary>
    public static readonly DependencyProperty ElementStyleProperty =
        DependencyProperty.Register("ElementStyle", typeof(Style), typeof(DataGridComboBoxColumn), (object?)null);

    /// <summary>Editing style store (matches EditingElementStyleProperty).</summary>
    public static readonly DependencyProperty EditingElementStyleProperty =
        DependencyProperty.Register("EditingElementStyle", typeof(Style), typeof(DataGridComboBoxColumn), (object?)null);

    private static readonly ComponentResourceKey TextBlockComboBoxStyleKeyValue =
        new(typeof(DataGridComboBoxColumn), "TextBlockComboBoxStyleKey");

    /// <summary>Display style.</summary>
    public Style? ElementStyle
    {
        get => GetValue(ElementStyleProperty) as Style;
        set => SetValue(ElementStyleProperty, value);
    }

    /// <summary>Editing style.</summary>
    public Style? EditingElementStyle
    {
        get => GetValue(EditingElementStyleProperty) as Style;
        set => SetValue(EditingElementStyleProperty, value);
    }

    /// <summary>Default display style (matches DefaultElementStyle; null with no theme dictionaries).</summary>
    public static Style? DefaultElementStyle => null;

    /// <summary>Default editing style (matches DefaultEditingElementStyle; null with no theme dictionaries).</summary>
    public static Style? DefaultEditingElementStyle => null;

    /// <summary>Theme style key (matches TextBlockComboBoxStyleKey).</summary>
    public static ComponentResourceKey TextBlockComboBoxStyleKey => TextBlockComboBoxStyleKeyValue;

    /// <summary>Path displayed in a read-only cell (matches DisplayMemberPath).</summary>
    public string? DisplayMemberPath { get; set; }

    /// <summary>Items offered by the drop-down (matches ItemsSource).</summary>
    public System.Collections.IEnumerable? ItemsSource { get; set; }

    /// <summary>Selection binding (matches SelectedItemBinding).</summary>
    public BindingBase? SelectedItemBinding { get; set; }

    /// <summary>Value binding (matches SelectedValueBinding).</summary>
    public BindingBase? SelectedValueBinding { get; set; }

    /// <summary>Value path (matches SelectedValuePath).</summary>
    public string? SelectedValuePath { get; set; }

    /// <summary>Text binding (matches TextBinding).</summary>
    public BindingBase? TextBinding { get; set; }

    /// <summary>Clipboard binding (matches ClipboardContentBinding).</summary>
    public BindingBase? ClipboardContentBinding { get; set; }

    /// <inheritdoc />
    protected internal override FrameworkElement? GenerateElement(DataGridCell cell, object? dataItem) =>
        BuildComboBox(ElementStyle ?? DefaultElementStyle, dataItem);

    /// <inheritdoc />
    protected internal override FrameworkElement? GenerateEditingElement(DataGridCell cell, object? dataItem) =>
        BuildComboBox(EditingElementStyle ?? ElementStyle ?? DefaultEditingElementStyle, dataItem);

    private ComboBox BuildComboBox(Style? style, object? dataItem)
    {
        var box = new ComboBox
        {
            Style = style,
            DisplayMemberPath = DisplayMemberPath,
            SelectedValuePath = SelectedValuePath,
            IsDropDownOpen = false,
        };
        if (ItemsSource is not null) box.ItemsSource = ItemsSource;
        if (SelectedItemBinding is not null) box.SetBinding(Selector.SelectedItemProperty, SelectedItemBinding);
        if (SelectedValueBinding is not null) box.SetBinding(Selector.SelectedValueProperty, SelectedValueBinding);
        if (dataItem is not null) box.DataContext = dataItem;
        return box;
    }

    /// <inheritdoc />
    protected internal override void CancelCellEdit(FrameworkElement editingElement, object uneditedValue)
    {
        if (editingElement is ComboBox box)
        {
            box.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateTarget();
            box.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateTarget();
        }
    }

    /// <inheritdoc />
    protected internal override bool CommitCellEdit(FrameworkElement editingElement)
    {
        if (editingElement is ComboBox box)
        {
            box.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateSource();
            box.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateSource();
        }
        return true;
    }
}

/// <summary>
/// Check-box column (matches System.Windows.Controls.DataGridCheckBoxColumn):
/// a bound boolean column. Like WPF it carries no per-column ElementStyle
/// (only the theme defaults, which stay null with no theme dictionaries).
/// </summary>
public class DataGridCheckBoxColumn : DataGridBoundColumn
{
    private bool _isThreeState;

    /// <summary>Whether null is a third state (matches IsThreeState).</summary>
    public bool IsThreeState
    {
        get => _isThreeState;
        set
        {
            if (_isThreeState == value) return;
            _isThreeState = value;
            OnColumnChanged();
        }
    }

    /// <summary>Default display style (matches DefaultElementStyle; null with no theme dictionaries).</summary>
    public static Style? DefaultElementStyle => null;

    /// <summary>Default editing style (matches DefaultEditingElementStyle; null with no theme dictionaries).</summary>
    public static Style? DefaultEditingElementStyle => null;

    /// <inheritdoc />
    protected internal override FrameworkElement? GenerateElement(DataGridCell cell, object? dataItem)
    {
        var check = new CheckBox { IsThreeState = IsThreeState };
        if (Binding is not null) check.SetBinding(ToggleButton.IsCheckedProperty, Binding);
        return check;
    }

    /// <inheritdoc />
    protected internal override FrameworkElement? GenerateEditingElement(DataGridCell cell, object? dataItem) =>
        GenerateElement(cell, dataItem);

    /// <inheritdoc />
    protected internal override void CancelCellEdit(FrameworkElement editingElement, object uneditedValue)
    {
        if (editingElement is CheckBox check)
            check.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
    }

    /// <inheritdoc />
    protected internal override bool CommitCellEdit(FrameworkElement editingElement)
    {
        if (editingElement is CheckBox check)
            check.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource();
        return true;
    }
}

/// <summary>
/// Template column (matches System.Windows.Controls.DataGridTemplateColumn):
/// cells inflate their visuals from data templates.
/// </summary>
public class DataGridTemplateColumn : DataGridColumn
{
    private DataTemplate? _cellTemplate;
    private DataTemplateSelector? _cellTemplateSelector;
    private DataTemplate? _cellEditingTemplate;
    private DataTemplateSelector? _cellEditingTemplateSelector;

    /// <summary>Display template (matches CellTemplate).</summary>
    public DataTemplate? CellTemplate
    {
        get => _cellTemplate;
        set
        {
            _cellTemplate = value;
            OnColumnChanged();
        }
    }

    /// <summary>Display template selector (matches CellTemplateSelector).</summary>
    public DataTemplateSelector? CellTemplateSelector
    {
        get => _cellTemplateSelector;
        set
        {
            _cellTemplateSelector = value;
            OnColumnChanged();
        }
    }

    /// <summary>Editing template (matches CellEditingTemplate).</summary>
    public DataTemplate? CellEditingTemplate
    {
        get => _cellEditingTemplate;
        set
        {
            _cellEditingTemplate = value;
            OnColumnChanged();
        }
    }

    /// <summary>Editing template selector (matches CellEditingTemplateSelector).</summary>
    public DataTemplateSelector? CellEditingTemplateSelector
    {
        get => _cellEditingTemplateSelector;
        set
        {
            _cellEditingTemplateSelector = value;
            OnColumnChanged();
        }
    }

    /// <inheritdoc />
    protected internal override FrameworkElement? GenerateElement(DataGridCell cell, object? dataItem) =>
        Inflate(CellTemplate, CellTemplateSelector, cell, dataItem);

    /// <inheritdoc />
    protected internal override FrameworkElement? GenerateEditingElement(DataGridCell cell, object? dataItem) =>
        Inflate(CellEditingTemplate, CellEditingTemplateSelector, cell, dataItem);

    private static FrameworkElement? Inflate(DataTemplate? template, DataTemplateSelector? selector, DataGridCell cell, object? dataItem)
    {
        var chosen = template ?? selector?.SelectTemplate(dataItem, cell);
        if (chosen?.LoadContent() is not FrameworkElement element) return null;
        if (dataItem is not null && !element.HasLocalValue(FrameworkElement.DataContextProperty))
            element.DataContext = dataItem;
        return element;
    }
}

/// <summary>Auto-column notice (matches System.Windows.Controls.DataGridAutoGeneratingColumnEventArgs).</summary>
public class DataGridAutoGeneratingColumnEventArgs : EventArgs
{
    /// <summary>Creates the notice (matches DataGridAutoGeneratingColumnEventArgs(propertyName, propertyType, column)).</summary>
    public DataGridAutoGeneratingColumnEventArgs(string propertyName, Type propertyType, DataGridColumn column)
    {
        PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        PropertyType = propertyType ?? throw new ArgumentNullException(nameof(propertyType));
        Column = column ?? throw new ArgumentNullException(nameof(column));
    }

    /// <summary>Whether to drop the generated column (matches Cancel).</summary>
    public bool Cancel { get; set; }

    /// <summary>Generated column (matches Column).</summary>
    public DataGridColumn Column { get; set; }

    /// <summary>Property descriptor, when known (matches PropertyDescriptor).</summary>
    public object? PropertyDescriptor { get; set; }

    /// <summary>Property name (matches PropertyName).</summary>
    public string PropertyName { get; }

    /// <summary>Property type (matches PropertyType).</summary>
    public Type PropertyType { get; }
}