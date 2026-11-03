namespace DesktopWasm.Wpf;

// XAML namescopes (WindowsBase/System.Xaml subset, live shapes verified
// against the 9.0.21 WindowsBase/System.Xaml ref packs): NameScope as the
// real IDictionary-backed INameScope plus the attached Get/SetNameScope
// pair the converted MaterialDesign transitions use to root their
// Storyboard target names. Each scope owns its dictionary; lookup does not
// walk ancestors (like WPF: GetNameScope returns the scope set on the
// element, and FindName searches that scope only).

/// <summary>Name-scope contract (matches System.Windows.Markup.INameScope).</summary>
public interface INameScope
{
    /// <summary>Finds a registered name (null when absent).</summary>
    object? FindName(string name);

    /// <summary>Registers a name (duplicates rejected, like WPF).</summary>
    void RegisterName(string name, object scopedElement);

    /// <summary>Removes a registration (absent names are a no-op).</summary>
    void UnregisterName(string name);
}

/// <summary>
/// Name scope (matches System.Windows.NameScope : IDictionary plus
/// INameScope). Backed by a plain dictionary; enumeration snapshots the
/// live entries.
/// </summary>
public sealed class NameScope : IDictionary<string, object>, INameScope
{
    private readonly Dictionary<string, object> _names = new(StringComparer.Ordinal);
    private readonly List<WeakReference<DependencyObject>> _attachedElements = new();

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, INameScope> AttachedScopes = new();

    /// <summary>Empty scope (live-verified shape).</summary>
    public NameScope()
    {
    }

    /// <summary>
    /// Returns the scope set on the element (null when none was set, like
    /// WPF: lookup does not walk ancestors).
    /// </summary>
    public static INameScope? GetNameScope(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return AttachedScopes.TryGetValue(element, out var scope) ? scope : null;
    }

    /// <summary>
    /// Attaches a scope to the element (null clears it). The converted
    /// transitions root their Storyboard target names through this pair.
    /// </summary>
    public static void SetNameScope(DependencyObject element, INameScope? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        AttachedScopes.Remove(element);
        if (value is not null)
        {
            AttachedScopes.Add(element, value);
            if (value is NameScope ns)
            {
                lock (ns._attachedElements)
                {
                    ns._attachedElements.Add(new WeakReference<DependencyObject>(element));
                }
            }
        }
        if (element is FrameworkElement fe) fe.RefreshBindingsTree();
    }

    /// <inheritdoc />
    public object? FindName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _names.TryGetValue(name, out var value) ? value : null;
    }

    /// <inheritdoc />
    public void RegisterName(string name, object scopedElement)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(scopedElement);
        if (!_names.TryAdd(name, scopedElement))
            throw new ArgumentException($"Name '{name}' is already registered in this scope.", nameof(name));
        NotifyAttachedElements();
    }

    /// <inheritdoc />
    public void UnregisterName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_names.Remove(name))
            NotifyAttachedElements();
    }

    private void NotifyAttachedElements()
    {
        lock (_attachedElements)
        {
            for (var i = _attachedElements.Count - 1; i >= 0; i--)
            {
                if (_attachedElements[i].TryGetTarget(out var target))
                {
                    if (target is FrameworkElement fe) fe.RefreshBindingsTree();
                }
                else
                {
                    _attachedElements.RemoveAt(i);
                }
            }
        }
    }

    /// <inheritdoc />
    public object this[string key]
    {
        get => _names[key];
        set
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);
            _names[key] = value;
        }
    }

    /// <inheritdoc />
    public int Count => _names.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public ICollection<string> Keys => _names.Keys;

    /// <inheritdoc />
    public ICollection<object> Values => _names.Values;

    /// <inheritdoc />
    public void Add(string key, object value) => RegisterName(key, value);

    /// <inheritdoc />
    public void Add(KeyValuePair<string, object> item) => RegisterName(item.Key, item.Value);

    /// <inheritdoc />
    public void Clear() => _names.Clear();

    /// <inheritdoc />
    public bool Contains(KeyValuePair<string, object> item) =>
        item.Value is not null && _names.TryGetValue(item.Key, out var value) && Equals(value, item.Value);

    /// <inheritdoc />
    public bool ContainsKey(string key) => _names.ContainsKey(key);

    /// <inheritdoc />
    public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<string, object>>)_names).CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _names.GetEnumerator();

    /// <inheritdoc />
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public bool Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _names.Remove(key);
    }

    /// <inheritdoc />
    public bool Remove(KeyValuePair<string, object> item) =>
        Contains(item) && _names.Remove(item.Key);

    /// <inheritdoc />
    public bool TryGetValue(string key, out object value) => _names.TryGetValue(key, out value!);
}
