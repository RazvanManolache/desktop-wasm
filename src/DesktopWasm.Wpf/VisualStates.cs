using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace DesktopWasm.Wpf;

// Visual-state engine v1 (System.Windows subset, shim, not port): VisualStateManager
// with its groups/states/transitions plus the FreezableCollection<T> concrete
// list States/Transitions return. Reference semantics: dotnet/wpf
// PresentationFramework VisualStateManager.cs / VisualStateGroup.cs plus the
// 9.0.21 ref-pack shapes and a live net9.0-windows runtime probe (GoToState
// reads control.StateGroupsRoot, groups default to an empty per-element list,
// CurrentState starts null, unknown states return false with no events, the
// same state returns true with no events, VisualStateGroupsProperty is
// read-only, TemplateVisualStateAttribute is Class/AllowMultiple).
// Shim boundaries: no implicit interpolation for GeneratedDuration-only
// transitions (the state storyboard begins at once and Changing/Changed fire
// synchronously; WPF defers Changed until the generated transition completes),
// no PresentationSource/tree gating (ShouldRunStateStoryboard always runs),
// storyboards begin through the v1 pump so non-DoubleAnimation children fail
// exactly like a direct Storyboard.Begin, and XAML never populates the groups
// (no VisualStateGroups property-element support in the loader yet), so live
// converted templates cannot reach the timed path until that slice lands.

/// <summary>Mutable Freezable list (matches System.Windows.FreezableCollection&lt;T&gt;).</summary>
public class FreezableCollection<T> : Animatable, IList<T>, IList, INotifyCollectionChanged, INotifyPropertyChanged
    where T : DependencyObject
{
    private readonly List<T> _items;
    private int _version;

    public FreezableCollection() => _items = new List<T>();

    public FreezableCollection(IEnumerable<T> collection) =>
        _items = new List<T>(collection ?? throw new ArgumentNullException(nameof(collection)));

    public FreezableCollection(int capacity) => _items = new List<T>(capacity);

    public int Count => _items.Count;

    public T this[int index]
    {
        get => _items[index];
        set
        {
            ThrowIfFrozen();
            ArgumentNullException.ThrowIfNull(value);
            var old = _items[index];
            _items[index] = value;
            RaiseChanged(NotifyCollectionChangedAction.Replace, value, old, index);
        }
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged
    {
        add { _collectionChanged += value; }
        remove { _collectionChanged -= value; }
    }

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { _propertyChanged += value; }
        remove { _propertyChanged -= value; }
    }

    private event NotifyCollectionChangedEventHandler? _collectionChanged;
    private event PropertyChangedEventHandler? _propertyChanged;

    public void Add(T item)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        RaiseChanged(NotifyCollectionChangedAction.Add, item, _items.Count - 1);
    }

    public void Clear()
    {
        ThrowIfFrozen();
        if (_items.Count == 0) return;
        _items.Clear();
        RaiseReset();
    }

    public bool Contains(T item) => _items.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <summary>Value enumerator (matches System.Windows.FreezableCollection&lt;T&gt;/Enumerator).</summary>
    public Enumerator GetEnumerator() => new(this);

    /// <summary>Version-stamped enumerator (matches the live 10.0.13 PresentationCore shape:
    /// public Current/MoveNext/Reset with explicit IEnumerator/IEnumerator&lt;T&gt;/IDisposable members;
    /// mutation during enumeration throws like WPF).</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly FreezableCollection<T> _collection;
        private readonly int _version;
        private int _index;
        private T? _current;

        internal Enumerator(FreezableCollection<T> collection)
        {
            _collection = collection;
            _version = collection._version;
            _index = -1;
            _current = default;
        }

        /// <summary>Current item (throws before the first MoveNext or after the end, like WPF).</summary>
        public T Current => _current ?? throw new InvalidOperationException("Enumeration has either not started or has already finished.");

        /// <summary>Advances to the next item (throws when the collection changed, like WPF).</summary>
        public bool MoveNext()
        {
            ThrowIfChanged();
            if ((uint)++_index >= (uint)_collection._items.Count)
            {
                _current = default;
                return false;
            }
            _current = _collection._items[_index];
            return true;
        }

        /// <summary>Restarts enumeration (throws when the collection changed, like WPF).</summary>
        public void Reset()
        {
            ThrowIfChanged();
            _index = -1;
            _current = default;
        }

        /// <summary>No-op (no unmanaged state; matches the struct-enumerator convention).</summary>
        public void Dispose()
        {
        }

        object? IEnumerator.Current => Current;

        private void ThrowIfChanged()
        {
            if (_version != _collection._version)
                throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
        }
    }

    public int IndexOf(T item) => _items.IndexOf(item);

    public void Insert(int index, T item)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, item);
        RaiseChanged(NotifyCollectionChangedAction.Add, item, index);
    }

    public bool Remove(T item)
    {
        ThrowIfFrozen();
        var index = _items.IndexOf(item);
        if (index < 0) return false;
        _items.RemoveAt(index);
        RaiseChanged(NotifyCollectionChangedAction.Remove, item, index);
        return true;
    }

    public void RemoveAt(int index)
    {
        ThrowIfFrozen();
        var old = _items[index];
        _items.RemoveAt(index);
        RaiseChanged(NotifyCollectionChangedAction.Remove, old, index);
    }

    /// <summary>Modifiable copy with its own list (items are shared, like WPF's CloneCore item walk minus per-item cloning).</summary>
    public new FreezableCollection<T> Clone()
    {
        var clone = new FreezableCollection<T>(_items);
        clone.ForkLocalValues();
        return clone;
    }

    /// <summary>Identical to Clone here (no per-frame state to snapshot).</summary>
    public new FreezableCollection<T> CloneCurrentValue() => Clone();

    protected override Freezable CreateInstanceCore() => new FreezableCollection<T>();

    bool ICollection<T>.IsReadOnly => IsFrozen;

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => IsFrozen;

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = (T)value!;
    }

    int IList.Add(object? value)
    {
        if (value is not T item)
            throw new ArgumentException($"'{value?.GetType().Name}' is not a '{typeof(T).Name}'.", nameof(value));
        Add(item);
        return Count - 1;
    }

    bool IList.Contains(object? value) => value is T item && Contains(item);

    int IList.IndexOf(object? value) => value is T item ? IndexOf(item) : -1;

    void IList.Insert(int index, object? value)
    {
        if (value is not T item)
            throw new ArgumentException($"'{value?.GetType().Name}' is not a '{typeof(T).Name}'.", nameof(value));
        Insert(index, item);
    }

    void IList.Remove(object? value)
    {
        if (value is T item) Remove(item);
    }

    void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    private void RaiseChanged(NotifyCollectionChangedAction action, T item, int index) =>
        RaiseChanged(action, item, default, index);

    private void RaiseChanged(NotifyCollectionChangedAction action, T newItem, T? oldItem, int index)
    {
        _version++;
        var args = action == NotifyCollectionChangedAction.Replace
            ? new NotifyCollectionChangedEventArgs(action, newItem, oldItem, index)
            : new NotifyCollectionChangedEventArgs(action, newItem, index);
        _collectionChanged?.Invoke(this, args);
        _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        _propertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private void RaiseReset()
    {
        _version++;
        _collectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        _propertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}

/// <summary>Named state within a group (matches System.Windows.VisualState).</summary>
public class VisualState : DependencyObject
{
    /// <summary>State name (state lookup compares this).</summary>
    public string? Name { get; set; }

    /// <summary>Animations applied while the state is current (null runs none).</summary>
    public Storyboard? Storyboard { get; set; }
}

/// <summary>Mutually exclusive state set (matches System.Windows.VisualStateGroup).</summary>
[ContentProperty("States")]
public class VisualStateGroup : DependencyObject
{
    private FreezableCollection<VisualState>? _states;
    private FreezableCollection<VisualTransition>? _transitions;
    private Collection<Storyboard>? _currentStoryboards;

    /// <summary>Group name (transition From/To match against state names, not this).</summary>
    public string? Name { get; set; }

    /// <summary>States in the group (concrete FreezableCollection like WPF).</summary>
    public IList States => _states ??= new FreezableCollection<VisualState>();

    /// <summary>Transitions between the states (concrete FreezableCollection like WPF).</summary>
    public IList Transitions => _transitions ??= new FreezableCollection<VisualTransition>();

    /// <summary>Currently applied state (the manager sets it after firing events, like WPF).</summary>
    public VisualState? CurrentState { get; internal set; }

    /// <summary>Raised when the transition starts.</summary>
    public event EventHandler<VisualStateChangedEventArgs>? CurrentStateChanging;

    /// <summary>Raised when the new state storyboard begins.</summary>
    public event EventHandler<VisualStateChangedEventArgs>? CurrentStateChanged;

    internal VisualState? GetState(string stateName)
    {
        for (var i = 0; i < States.Count; i++)
        {
            var state = (VisualState)States[i]!;
            if (state.Name == stateName) return state;
        }

        return null;
    }

    internal Collection<Storyboard> CurrentStoryboards => _currentStoryboards ??= new Collection<Storyboard>();

    internal void StartNewThenStopOld(FrameworkElement element, params Storyboard?[] newStoryboards)
    {
        // Stopping first is the v1 pump's SnapshotAndReplace equivalent
        // (WPF delays the remove to the next tick; the handoff reads the same).
        foreach (var current in CurrentStoryboards)
        {
            if (current is not null) WpfAnimationClock.Stop(current);
        }

        CurrentStoryboards.Clear();

        foreach (var next in newStoryboards)
        {
            if (next is null) continue;
            next.Begin(element);
            CurrentStoryboards.Add(next);
        }
    }

    internal void RaiseCurrentStateChanging(FrameworkElement stateGroupsRoot, VisualState? oldState, VisualState newState, FrameworkElement? control)
    {
        CurrentStateChanging?.Invoke(stateGroupsRoot,
            new VisualStateChangedEventArgs(oldState, newState, control, stateGroupsRoot));
    }

    internal void RaiseCurrentStateChanged(FrameworkElement stateGroupsRoot, VisualState? oldState, VisualState newState, FrameworkElement? control)
    {
        CurrentStateChanged?.Invoke(stateGroupsRoot,
            new VisualStateChangedEventArgs(oldState, newState, control, stateGroupsRoot));
    }
}

/// <summary>Timed hop between two states (matches System.Windows.VisualTransition).</summary>
public class VisualTransition : DependencyObject
{
    /// <summary>Source state name (null matches any previous state).</summary>
    public string? From { get; set; }

    /// <summary>Target state name (null matches any new state).</summary>
    public string? To { get; set; }

    /// <summary>Explicit transition storyboard (null runs the state storyboard at once).</summary>
    public Storyboard? Storyboard { get; set; }

    /// <summary>Implicit interpolation length (zero by default, like WPF).</summary>
    public Duration GeneratedDuration { get; set; } = new Duration(TimeSpan.Zero);

    /// <summary>Easing for generated interpolations (carried, never synthesized here).</summary>
    public IEasingFunction? GeneratedEasingFunction { get; set; }

    internal bool IsDefault { get; set; }

    internal bool DynamicStoryboardCompleted { get; set; } = true;

    internal bool ExplicitStoryboardCompleted { get; set; } = true;
}

/// <summary>State-change notice (matches System.Windows.VisualStateChangedEventArgs).</summary>
public sealed class VisualStateChangedEventArgs : EventArgs
{
    internal VisualStateChangedEventArgs(VisualState? oldState, VisualState newState, FrameworkElement? control, FrameworkElement stateGroupsRoot)
    {
        OldState = oldState;
        NewState = newState;
        Control = control;
        StateGroupsRoot = stateGroupsRoot;
    }

    /// <summary>Control GoToState was called on (null for GoToElementState).</summary>
    public FrameworkElement? Control { get; }

    /// <summary>Element the groups were read from.</summary>
    public FrameworkElement StateGroupsRoot { get; }

    /// <summary>State being left (null for the first transition).</summary>
    public VisualState? OldState { get; }

    /// <summary>State being entered.</summary>
    public VisualState NewState { get; }
}

/// <summary>Declares the states a templated control supports (matches System.Windows.TemplateVisualStateAttribute).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class TemplateVisualStateAttribute : Attribute
{
    /// <summary>State name.</summary>
    public string? Name { get; set; }

    /// <summary>Group the state belongs to.</summary>
    public string? GroupName { get; set; }
}

/// <summary>Visual-state driver (matches System.Windows.VisualStateManager).</summary>
public class VisualStateManager : DependencyObject
{
    private static readonly Duration Zero = new(TimeSpan.Zero);

    private static readonly DependencyPropertyKey VisualStateGroupsPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly("VisualStateGroups", typeof(IList), typeof(VisualStateManager),
            new FrameworkPropertyMetadata((object?)null));

    /// <summary>Read-only group host (XAML fills the live list; there is no public setter, like WPF).</summary>
    public static readonly DependencyProperty VisualStateGroupsProperty =
        VisualStateGroupsPropertyKey.DependencyProperty;

    /// <summary>Custom driver slot (plain attached property, like WPF).</summary>
    public static readonly DependencyProperty CustomVisualStateManagerProperty =
        DependencyProperty.RegisterAttached("CustomVisualStateManager", typeof(VisualStateManager), typeof(VisualStateManager),
            new FrameworkPropertyMetadata((object?)null));

    /// <summary>Lazy group host for the loader path (mirrors the BAML writer's key access).</summary>
    internal static void SetVisualStateGroups(FrameworkElement obj, IList? value) =>
        obj.SetValue(VisualStateGroupsPropertyKey, value);

    /// <summary>Reads the custom driver (null unless SetCustomVisualStateManager ran).</summary>
    public static VisualStateManager? GetCustomVisualStateManager(FrameworkElement obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return (VisualStateManager?)obj.GetValue(CustomVisualStateManagerProperty);
    }

    /// <summary>Installs a custom driver (ExtendedVisualStateManager binds here).</summary>
    public static void SetCustomVisualStateManager(FrameworkElement obj, VisualStateManager? value)
    {
        ArgumentNullException.ThrowIfNull(obj);
        obj.SetValue(CustomVisualStateManagerProperty, value);
    }

    /// <summary>Live group list, attached on first read (never null, like WPF).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public static IList GetVisualStateGroups(FrameworkElement obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var groups = (IList?)obj.GetValue(VisualStateGroupsProperty);
        if (groups is null)
        {
            groups = new ObservableCollection<VisualStateGroup>();
            obj.SetValue(VisualStateGroupsPropertyKey, groups);
        }

        return groups;
    }

    internal static Collection<VisualStateGroup>? GetVisualStateGroupsInternal(FrameworkElement obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        // WPF reports Default-sourced values as absent; the lazy list above
        // is stored locally on attach, so untouched elements read null here.
        if (!obj.HasLocalValue(VisualStateGroupsProperty)) return null;
        return obj.GetValue(VisualStateGroupsProperty) as Collection<VisualStateGroup>;
    }

    /// <summary>Transitions the templated control (reads groups off its template root; false without one).</summary>
    public static bool GoToState(FrameworkElement control, string stateName, bool useTransitions)
    {
        ArgumentNullException.ThrowIfNull(control);
        return GoToStateCommon(control, control.StateGroupsRoot, stateName, useTransitions);
    }

    /// <summary>Transitions the given root itself (converted utilities bind here).</summary>
    public static bool GoToElementState(FrameworkElement stateGroupsRoot, string stateName, bool useTransitions)
    {
        ArgumentNullException.ThrowIfNull(stateGroupsRoot);
        return GoToStateCommon(null, stateGroupsRoot, stateName, useTransitions);
    }

    /// <summary>Override point for custom drivers (ExtendedVisualStateManager binds here).</summary>
    protected virtual bool GoToStateCore(FrameworkElement? control, FrameworkElement? stateGroupsRoot, string stateName, VisualStateGroup? group, VisualState? state, bool useTransitions) =>
        GoToStateInternal(control, stateGroupsRoot!, group!, state!, useTransitions);

    /// <summary>Forwards the changing notice (no-op without a template root, like WPF).</summary>
    protected void RaiseCurrentStateChanging(VisualStateGroup stateGroup, VisualState? oldState, VisualState newState, FrameworkElement? control, FrameworkElement? stateGroupsRoot)
    {
        ArgumentNullException.ThrowIfNull(stateGroup);
        ArgumentNullException.ThrowIfNull(newState);
        if (stateGroupsRoot is null) return;
        stateGroup.RaiseCurrentStateChanging(stateGroupsRoot, oldState, newState, control);
    }

    /// <summary>Forwards the changed notice (no-op without a template root, like WPF).</summary>
    protected void RaiseCurrentStateChanged(VisualStateGroup stateGroup, VisualState? oldState, VisualState newState, FrameworkElement? control, FrameworkElement? stateGroupsRoot)
    {
        ArgumentNullException.ThrowIfNull(stateGroup);
        ArgumentNullException.ThrowIfNull(newState);
        if (stateGroupsRoot is null) return;
        stateGroup.RaiseCurrentStateChanged(stateGroupsRoot, oldState, newState, control);
    }

    private static bool GoToStateCommon(FrameworkElement? control, FrameworkElement? stateGroupsRoot, string stateName, bool useTransitions)
    {
        ArgumentNullException.ThrowIfNull(stateName);
        if (stateGroupsRoot is null) return false;

        var groups = GetVisualStateGroupsInternal(stateGroupsRoot);
        if (groups is null) return false;
        if (!TryGetState(groups, stateName, out var group, out var state) || group is null)
        {
            group = null;
            state = null;
        }

        // A custom driver runs even for unknown states; the default needs one.
        var custom = GetCustomVisualStateManager(stateGroupsRoot);
        if (custom is not null)
            return custom.GoToStateCore(control, stateGroupsRoot, stateName, group, state, useTransitions);
        if (state is not null && group is not null)
            return GoToStateInternal(control, stateGroupsRoot, group, state, useTransitions);
        return false;
    }

    internal static bool TryGetState(IList<VisualStateGroup> groups, string stateName, out VisualStateGroup? group, out VisualState? state)
    {
        foreach (var candidate in groups)
        {
            var found = candidate.GetState(stateName);
            if (found is not null)
            {
                group = candidate;
                state = found;
                return true;
            }
        }

        group = null;
        state = null;
        return false;
    }

    private static bool GoToStateInternal(FrameworkElement? control, FrameworkElement stateGroupsRoot, VisualStateGroup group, VisualState state, bool useTransitions)
    {
        ArgumentNullException.ThrowIfNull(stateGroupsRoot);
        ArgumentNullException.ThrowIfNull(state);
        if (group is null) throw new InvalidOperationException();

        var lastState = group.CurrentState;
        if (ReferenceEquals(lastState, state)) return true;

        var transition = useTransitions ? GetTransition(stateGroupsRoot, group, lastState, state) : null;
        var snaps = transition is null ||
            (transition.GeneratedDuration == Zero &&
                (transition.Storyboard is null || transition.Storyboard.Duration == Zero));
        if (snaps)
        {
            // Zero-length hops start the state (or explicit transition)
            // storyboard at once, then fire both notices, like WPF.
            group.StartNewThenStopOld(stateGroupsRoot,
                transition?.Storyboard is not null
                    ? new Storyboard?[] { transition.Storyboard, state.Storyboard }
                    : new Storyboard?[] { state.Storyboard });
            group.RaiseCurrentStateChanging(stateGroupsRoot, lastState, state, control);
            group.RaiseCurrentStateChanged(stateGroupsRoot, lastState, state, control);
        }
        else
        {
            // Timed hops keep the v1 boundary: the explicit storyboards begin
            // at once and both notices fire synchronously. WPF instead builds
            // a generated interpolation storyboard and defers the state
            // storyboard plus Changed until the transition completes.
            group.StartNewThenStopOld(stateGroupsRoot, transition!.Storyboard, state.Storyboard);
            group.RaiseCurrentStateChanging(stateGroupsRoot, lastState, state, control);
            group.RaiseCurrentStateChanged(stateGroupsRoot, lastState, state, control);
        }

        group.CurrentState = state;
        return true;
    }

    internal static VisualTransition? GetTransition(FrameworkElement element, VisualStateGroup group, VisualState? from, VisualState to)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(to);

        VisualTransition? best = null;
        VisualTransition? otherwise = null;
        var bestScore = -1;
        foreach (VisualTransition transition in group.Transitions)
        {
            if (transition.IsDefault && otherwise is null)
            {
                otherwise = transition;
                continue;
            }

            var score = -1;
            var fromState = transition.From is not null ? group.GetState(transition.From) : null;
            var toState = transition.To is not null ? group.GetState(transition.To) : null;
            if (ReferenceEquals(from, fromState)) score += 1;
            else if (fromState is not null) continue;
            if (ReferenceEquals(to, toState)) score += 2;
            else if (toState is not null) continue;
            if (score > bestScore)
            {
                bestScore = score;
                best = transition;
            }
        }

        return best ?? otherwise;
    }
}
