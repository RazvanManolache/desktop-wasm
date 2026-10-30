using System.Collections;

namespace DesktopWasm.Wpf;

// Slice-93 AvalonEdit composition scope: TextCompositionEventHandler /
// InputManager / LogicalDirection / LanguageSpecificStringDictionary (the
// last 4 convertible gaps in the handycontrol package: 4 unmapped-type rows
// on ICSharpCode.AvalonEdit). Member evidence via Mono.Cecil on the upstream
// net10.0-windows ICSharpCode.AvalonEdit.dll, attributed to the exact
// converted call sites: TextArea.add/remove_TextEntering/TextEntered wire the
// handler type through UIElement-style add_/remove_ accessors and
// OnTextEntering/OnTextEntered Invoke it; CompletionWindow.AttachEvents/
// DetachEvents new up the handler and call UIElement.add/remove_PreviewTextInput
// while textArea_PreviewTextInput reads InputEventArgs.get_Device plus
// TextCompositionEventArgs.get_TextComposition and news up
// TextCompositionEventArgs..ctor(InputDevice, TextComposition); TextArea's two
// PerformTextInput overloads call InputManager.get_Current() then new up
// TextComposition..ctor(InputManager, IInputElement, string) (the second via
// Keyboard.get_PrimaryDevice) and read get_Text(); TextRangeProvider.MoveOffset
// plus TextUtilities.GetNextCaretPosition and the VisualLine* overrides take a
// LogicalDirection parameter. LanguageSpecificStringDictionary has no code
// user in the binary (no MemberRef rows module-wide): it survives only as an
// orphaned TypeRef, but the converter reports unmapped TypeRefs, so declaring
// the engine contract clears the row. Live shapes verified against the 10.0.13
// PresentationCore/PresentationFramework ref packs plus live net10.0-windows
// runtime probes (delegate (object, args):void; InputManager sealed over
// DispatcherObject with a private ctor and a per-thread Current; TextComposition
// (InputManager, IInputElement, string) tolerating null manager/source while
// null text throws ArgumentException; args (InputDevice, TextComposition)
// throwing ArgumentNullException on null composition while null devices flow;
// Text/SystemText/ControlText reading the stored composition; TextCompositionManager
// owning both routed tokens; dictionary null-value/null-key rejection, string-key
// conversion through XmlLanguage.GetLanguage, non-convertible keys reading null /
// containing false / removing silent / setting ArgumentException, and missing
// convertible keys throwing KeyNotFoundException on read).
// Boundary, never fabricated: the engine drives no IME composition loop (no
// TextCompositionManager staging pipeline, no ProcessInput/NotifyInput surface,
// no Complete dispatch), so InputManager.Current is only the thread-local
// composition identity converted construction binds against; TextComposition
// carries no AutoComplete/Stage/Source/Complete surface and the args carry no
// ControlText/SystemText variants (converted AvalonEdit reads Text only).

/// <summary>Text composition handler (matches System.Windows.Input.TextCompositionEventHandler).</summary>
public delegate void TextCompositionEventHandler(object sender, TextCompositionEventArgs e);

/// <summary>
/// Input manager (matches System.Windows.Input.InputManager, sealed over
/// DispatcherObject): the thread-local composition identity converted text
/// composition binds against. Live WPF returns the manager for the current
/// Dispatcher; the engine has no dispatcher-bound staging pipeline, so Current
/// auto-creates one manager per thread, like WPF auto-creates per Dispatcher.
/// </summary>
public sealed class InputManager : DispatcherObject
{
    private static readonly ThreadLocal<InputManager> _current =
        new(() => new InputManager(), trackAllValues: false);

    private InputManager()
    {
    }

    /// <summary>Composition identity for the calling thread (matches InputManager.Current).</summary>
    public static InputManager Current => _current.Value!;
}

/// <summary>Text-flow direction (matches System.Windows.Documents.LogicalDirection).</summary>
public enum LogicalDirection
{
    /// <summary>Toward the start of the content (matches Backward = 0).</summary>
    Backward = 0,
    /// <summary>Toward the end of the content (matches Forward = 1).</summary>
    Forward = 1,
}

/// <summary>
/// Language-keyed string map (matches
/// System.Windows.Media.LanguageSpecificStringDictionary, sealed): an
/// XmlLanguage-keyed dictionary with the live conversion and rejection
/// behavior. Converted AvalonEdit carries only an orphaned TypeRef (no code
/// user), so this contract exists to clear the conversion row with real
/// behavior rather than a name-only stub.
/// </summary>
public sealed class LanguageSpecificStringDictionary
    : IDictionary<XmlLanguage, string>, IDictionary
{
    private readonly IDictionary<XmlLanguage, string> _inner;

    internal LanguageSpecificStringDictionary(IDictionary<XmlLanguage, string> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Entry count (matches Count).</summary>
    public int Count => _inner.Count;

    /// <summary>Always false: entries add and remove freely (matches IsReadOnly).</summary>
    public bool IsReadOnly => false;

    /// <summary>Reads or replaces the string for a language (matches Item).</summary>
    public string this[XmlLanguage key]
    {
        get => _inner[key];
        set
        {
            ArgumentNullException.ThrowIfNull(key);
            if (value is null) throw new ArgumentNullException(nameof(value));
            _inner[key] = value;
        }
    }

    /// <summary>Adds one entry pair (matches Add).</summary>
    public void Add(KeyValuePair<XmlLanguage, string> item) => Add(item.Key, item.Value);

    /// <summary>Adds one entry (matches Add).</summary>
    public void Add(XmlLanguage key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (value is null) throw new ArgumentNullException(nameof(value));
        _inner.Add(key, value);
    }

    /// <summary>Clears every entry (matches Clear).</summary>
    public void Clear() => _inner.Clear();

    /// <summary>True when the exact pair is present (matches Contains).</summary>
    public bool Contains(KeyValuePair<XmlLanguage, string> item) => _inner.Contains(item);

    /// <summary>True when the language is present (matches ContainsKey).</summary>
    public bool ContainsKey(XmlLanguage key) => _inner.ContainsKey(key);

    /// <summary>Copies entries into an array (matches CopyTo).</summary>
    public void CopyTo(KeyValuePair<XmlLanguage, string>[] array, int arrayIndex) =>
        _inner.CopyTo(array, arrayIndex);

    /// <summary>Enumerates entries (matches GetEnumerator).</summary>
    public IEnumerator<KeyValuePair<XmlLanguage, string>> GetEnumerator() => _inner.GetEnumerator();

    /// <summary>Removes one language (matches Remove).</summary>
    public bool Remove(XmlLanguage key) => _inner.Remove(key);

    /// <summary>Removes the exact pair (matches Remove).</summary>
    public bool Remove(KeyValuePair<XmlLanguage, string> item) =>
        _inner is ICollection<KeyValuePair<XmlLanguage, string>> list && list.Remove(item);

    /// <summary>Reads one entry without throwing when absent (matches TryGetValue).</summary>
    public bool TryGetValue(XmlLanguage key, out string value) => _inner.TryGetValue(key, out value!);

    /// <summary>Languages present (matches Keys).</summary>
    public ICollection<XmlLanguage> Keys => _inner.Keys;

    /// <summary>Strings present (matches Values).</summary>
    public ICollection<string> Values => _inner.Values;

    bool ICollection<KeyValuePair<XmlLanguage, string>>.IsReadOnly => false;

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool IDictionary.IsFixedSize => false;

    bool IDictionary.IsReadOnly => false;

    ICollection IDictionary.Keys => (ICollection)Keys;

    ICollection IDictionary.Values => (ICollection)Values;

    object? IDictionary.this[object key]
    {
        get
        {
            if (!TryConvertKey(key, out var converted) || converted is null) return null;
            return _inner[converted];
        }
        set
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (!TryConvertKey(key, out var converted) || converted is null)
                throw new ArgumentException(
                    $"Cannot convert type '{key.GetType()}' to '{typeof(XmlLanguage)}'.", nameof(key));
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (value is not string text)
                throw new ArgumentException(
                    $"Cannot convert type '{value.GetType()}' to '{typeof(string)}'.", nameof(value));
            this[converted] = text;
        }
    }

    void IDictionary.Add(object key, object? value)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (!TryConvertKey(key, out var converted) || converted is null)
            throw new ArgumentException(
                $"Cannot convert type '{key.GetType()}' to '{typeof(XmlLanguage)}'.", nameof(key));
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (value is not string text)
            throw new ArgumentException(
                $"Cannot convert type '{value.GetType()}' to '{typeof(string)}'.", nameof(value));
        Add(converted, text);
    }

    bool IDictionary.Contains(object key) =>
        key is not null && TryConvertKey(key, out var converted) &&
        converted is not null && _inner.ContainsKey(converted);

    IDictionaryEnumerator IDictionary.GetEnumerator() =>
        ((IDictionary)_inner).GetEnumerator();

    void IDictionary.Remove(object key)
    {
        if (key is null) return;
        if (!TryConvertKey(key, out var converted) || converted is null) return;
        _inner.Remove(converted);
    }

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    void ICollection.CopyTo(Array array, int index) =>
        ((ICollection)_inner).CopyTo(array, index);

    private static bool TryConvertKey(object? key, out XmlLanguage? converted)
    {
        if (key is XmlLanguage language)
        {
            converted = language;
            return true;
        }
        if (key is string tag)
        {
            converted = XmlLanguage.GetLanguage(tag);
            return true;
        }
        converted = null;
        return false;
    }
}
