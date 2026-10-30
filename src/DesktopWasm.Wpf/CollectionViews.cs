using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace DesktopWasm.Wpf;

// Collection-view engine v1 (PresentationFramework/WindowsBase subset, shim,
// not port): ICollectionView currency/filter/sort/group over an IEnumerable
// snapshot plus CollectionViewSource default-view caching and forwarding.
// Reference semantics: System.ComponentModel.ICollectionView,
// System.Windows.Data.CollectionView/ListCollectionView/CollectionViewSource,
// System.Windows.FilterEventArgs/Handler, CurrentChangingEventArgs/Handler.
// Shim boundaries: no dispatcher affinity, no cross-thread or weak-event
// machinery, no IListSource/IBindingList/ICollectionViewFactory
// views (a source already implementing ICollectionView is its own default
// view), no ICollectionViewLiveShaping, and Refresh raises one coarse Reset
// instead of per-index deltas. The engine unifies EnumerableCollectionView
// into CollectionView: CanSort/CanGroup are true with live collections on
// both CollectionView and ListCollectionView, so code like the OxyPlot
// ExampleBrowser (GetDefaultView(orderBy) + GroupDescriptions.Add) runs.

/// <summary>Cancelable currency notice (matches System.ComponentModel.CurrentChangingEventArgs).</summary>
public sealed class CurrentChangingEventArgs : EventArgs
{
    public CurrentChangingEventArgs()
        : this(true)
    {
    }

    public CurrentChangingEventArgs(bool isCancelable) => IsCancelable = isCancelable;

    public bool IsCancelable { get; }

    private bool _cancel;

    public bool Cancel
    {
        get => _cancel;
        set
        {
            if (value && !IsCancelable)
                throw new InvalidOperationException("This CurrentChanging event cannot be canceled.");
            _cancel = value;
        }
    }
}

/// <summary>Currency notice handler (matches System.ComponentModel.CurrentChangingEventHandler).</summary>
public delegate void CurrentChangingEventHandler(object? sender, CurrentChangingEventArgs e);

/// <summary>Filter query payload (matches System.Windows.FilterEventArgs).</summary>
public sealed class FilterEventArgs : EventArgs
{
    public FilterEventArgs(object? item) => Item = item;

    public object? Item { get; }

    public bool Accepted { get; set; } = true;
}

/// <summary>Filter query handler (matches System.Windows.FilterEventHandler).</summary>
public delegate void FilterEventHandler(object sender, FilterEventArgs e);

/// <summary>Group node (matches System.Windows.Data.CollectionViewGroup).</summary>
public abstract class CollectionViewGroup : INotifyPropertyChanged
{
    protected CollectionViewGroup(object? name) => Name = name;

    public object? Name { get; }

    public abstract ObservableCollection<object> Items { get; }

    public abstract int ItemCount { get; }

    public virtual bool IsBottomLevel => true;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected void RaiseItemsReset()
    {
        RaisePropertyChanged(nameof(Items));
        RaisePropertyChanged(nameof(ItemCount));
    }
}

/// <summary>Runtime group node (single implementation behind CollectionViewGroup).</summary>
public sealed class RuntimeViewGroup : CollectionViewGroup
{
    private readonly ObservableCollection<object> _items = new();

    internal RuntimeViewGroup(object? name, bool isBottomLevel)
        : base(name) => IsBottomLevel = isBottomLevel;

    public override ObservableCollection<object> Items => _items;

    public override int ItemCount
    {
        get
        {
            if (IsBottomLevel) return _items.Count;
            var total = 0;
            foreach (var child in _items)
                total += child is CollectionViewGroup group ? group.ItemCount : 1;
            return total;
        }
    }

    public override bool IsBottomLevel { get; }

    internal void Add(object item) => _items.Add(item);

    internal void RaiseReset() => RaiseItemsReset();
}

/// <summary>Collection-view contract (matches System.ComponentModel.ICollectionView).</summary>
public interface ICollectionView : IEnumerable, INotifyCollectionChanged
{
    bool CanFilter { get; }

    bool CanGroup { get; }

    bool CanSort { get; }

    CultureInfo Culture { get; set; }

    object? CurrentItem { get; }

    int CurrentPosition { get; }

    Predicate<object?>? Filter { get; set; }

    ObservableCollection<GroupDescription> GroupDescriptions { get; }

    /// <summary>Top-level groups; null while no GroupDescriptions are set (like WPF).</summary>
    ReadOnlyObservableCollection<object>? Groups { get; }

    bool IsCurrentAfterLast { get; }

    bool IsCurrentBeforeFirst { get; }

    bool IsEmpty { get; }

    SortDescriptionCollection SortDescriptions { get; }

    IEnumerable SourceCollection { get; }

    event CurrentChangingEventHandler? CurrentChanging;

    event EventHandler? CurrentChanged;

    bool Contains(object? item);

    IDisposable DeferRefresh();

    bool MoveCurrentTo(object? item);

    bool MoveCurrentToFirst();

    bool MoveCurrentToLast();

    bool MoveCurrentToNext();

    bool MoveCurrentToPrevious();

    bool MoveCurrentToPosition(int position);

    void Refresh();
}

/// <summary>Default view over a collection (matches System.Windows.Data.CollectionView).</summary>
public class CollectionView : ICollectionView, INotifyPropertyChanged
{
    private List<object?> _view = [];
    private ReadOnlyObservableCollection<object>? _groups;
    private object? _currentItem;
    private int _currentPosition = -1;
    private bool _beforeFirst = true;
    private bool _afterLast = true;
    private Predicate<object?>? _filter;
    private CultureInfo _culture = CultureInfo.CurrentCulture;
    private int _deferLevel;
    private bool _needsRefresh;
    private NotifyCollectionChangedEventHandler? _sourceHandler;
    private bool _detached;

    public CollectionView(IEnumerable source)
    {
        ArgumentNullException.ThrowIfNull(source);
        SourceCollection = source;
        SortDescriptions = new SortDescriptionCollection();
        SortDescriptions.CollectionChanged += (_, _) => RefreshOrDefer();
        GroupDescriptions = new ObservableCollection<GroupDescription>();
        GroupDescriptions.CollectionChanged += (_, _) => RefreshOrDefer();
        if (source is INotifyCollectionChanged incc)
        {
            NotifyCollectionChangedEventHandler handler = (_, _) => RefreshOrDefer();
            _sourceHandler = handler;
            incc.CollectionChanged += handler;
        }
        Rebuild();
    }

    public IEnumerable SourceCollection { get; private set; }

    public virtual int Count
    {
        get
        {
            VerifyRefreshNotDeferred();
            return _view.Count;
        }
    }

    public virtual bool IsEmpty
    {
        get
        {
            VerifyRefreshNotDeferred();
            return _view.Count == 0;
        }
    }

    public virtual bool CanFilter => true;

    public virtual bool CanSort => true;

    public virtual bool CanGroup => true;

    public virtual CultureInfo Culture
    {
        get => _culture;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_culture, value) || _culture.Equals(value)) return;
            _culture = value;
            OnPropertyChanged(nameof(Culture));
            RefreshOrDefer();
        }
    }

    public virtual Predicate<object?>? Filter
    {
        get => _filter;
        set
        {
            if (!CanFilter && value is not null) throw new NotSupportedException("This view does not support filtering.");
            _filter = value;
            RefreshOrDefer();
        }
    }

    public virtual SortDescriptionCollection SortDescriptions { get; }

    public virtual ObservableCollection<GroupDescription> GroupDescriptions { get; }

    public virtual ReadOnlyObservableCollection<object>? Groups
    {
        get
        {
            VerifyRefreshNotDeferred();
            return _groups;
        }
    }

    public virtual object? CurrentItem
    {
        get
        {
            VerifyRefreshNotDeferred();
            return _currentItem;
        }
    }

    public virtual int CurrentPosition
    {
        get
        {
            VerifyRefreshNotDeferred();
            return _currentPosition;
        }
    }

    public virtual bool IsCurrentAfterLast
    {
        get
        {
            VerifyRefreshNotDeferred();
            return _afterLast;
        }
    }

    public virtual bool IsCurrentBeforeFirst
    {
        get
        {
            VerifyRefreshNotDeferred();
            return _beforeFirst;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public virtual event CurrentChangingEventHandler? CurrentChanging;

    public virtual event EventHandler? CurrentChanged;

    public virtual void Refresh()
    {
        if (_deferLevel > 0)
        {
            _needsRefresh = true;
            return;
        }
        Rebuild();
    }

    public virtual IDisposable DeferRefresh()
    {
        _deferLevel++;
        return new DeferScope(this);
    }

    public virtual bool Contains(object? item)
    {
        VerifyRefreshNotDeferred();
        return IndexOf(item) >= 0;
    }

    /// <summary>Filter probe without a view lookup (matches CollectionView.PassesFilter).</summary>
    public virtual bool PassesFilter(object? item) =>
        !CanFilter || _filter is null || _filter(item);

    /// <summary>View index of an item, or -1 (matches CollectionView.IndexOf).</summary>
    public virtual int IndexOf(object? item)
    {
        VerifyRefreshNotDeferred();
        for (var i = 0; i < _view.Count; i++)
            if (Equals(_view[i], item)) return i;
        return -1;
    }

    /// <summary>Item at a view index (matches CollectionView.GetItemAt).</summary>
    public virtual object? GetItemAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        VerifyRefreshNotDeferred();
        return _view[index];
    }

    /// <summary>Stops listening to the source collection (matches CollectionView.DetachFromSourceCollection).</summary>
    public virtual void DetachFromSourceCollection()
    {
        if (SourceCollection is INotifyCollectionChanged incc && _sourceHandler is not null)
            incc.CollectionChanged -= _sourceHandler;
        _sourceHandler = null;
        _detached = true;
        SourceCollection = Array.Empty<object>();
        RefreshOrDefer();
    }

    public virtual bool MoveCurrentTo(object? item)
    {
        VerifyRefreshNotDeferred();
        if (Equals(CurrentItem, item))
        {
            if (item is not null || IsCurrentInView) return IsCurrentInView;
        }
        var index = -1;
        if (PassesFilter(item)) index = IndexOf(item);
        return MoveCurrentToPosition(index);
    }

    public virtual bool MoveCurrentToFirst()
    {
        VerifyRefreshNotDeferred();
        return MoveCurrentToPosition(0);
    }

    public virtual bool MoveCurrentToLast()
    {
        VerifyRefreshNotDeferred();
        return MoveCurrentToPosition(Count - 1);
    }

    public virtual bool MoveCurrentToNext()
    {
        VerifyRefreshNotDeferred();
        var index = CurrentPosition + 1;
        return index <= Count ? MoveCurrentToPosition(index) : false;
    }

    public virtual bool MoveCurrentToPrevious()
    {
        VerifyRefreshNotDeferred();
        var index = CurrentPosition - 1;
        return index >= -1 ? MoveCurrentToPosition(index) : false;
    }

    public virtual bool MoveCurrentToPosition(int position)
    {
        VerifyRefreshNotDeferred();
        ArgumentOutOfRangeException.ThrowIfLessThan(position, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, Count);
        if ((position != CurrentPosition || !IsCurrentInSync) && OkToChangeCurrent())
        {
            var before = _beforeFirst;
            var after = _afterLast;
            MoveCurrentCore(position);
            OnCurrentChanged();
            if (_afterLast != after) OnPropertyChanged(nameof(IsCurrentAfterLast));
            if (_beforeFirst != before) OnPropertyChanged(nameof(IsCurrentBeforeFirst));
            OnPropertyChanged(nameof(CurrentPosition));
            OnPropertyChanged(nameof(CurrentItem));
        }
        return IsCurrentInView;
    }

    public IEnumerator GetEnumerator()
    {
        VerifyRefreshNotDeferred();
        return _view.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private bool IsCurrentInView => CurrentPosition >= 0 && CurrentPosition < _view.Count;

    private bool IsCurrentInSync =>
        (CurrentPosition < 0 || CurrentPosition >= _view.Count) ? _currentItem is null : Equals(_view[CurrentPosition], _currentItem);

    private bool OkToChangeCurrent()
    {
        var args = new CurrentChangingEventArgs();
        CurrentChanging?.Invoke(this, args);
        return !args.Cancel;
    }

    private void MoveCurrentCore(int position)
    {
        if (position < 0)
        {
            _currentItem = null;
            _currentPosition = -1;
            _beforeFirst = true;
            _afterLast = false;
        }
        else if (position >= _view.Count)
        {
            _currentItem = null;
            _currentPosition = position;
            _beforeFirst = false;
            _afterLast = true;
        }
        else
        {
            _currentItem = _view[position];
            _currentPosition = position;
            _beforeFirst = false;
            _afterLast = false;
        }
    }

    private void OnCurrentChanged() => CurrentChanged?.Invoke(this, EventArgs.Empty);

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void RefreshOrDefer()
    {
        if (_deferLevel > 0) _needsRefresh = true;
        else Rebuild();
    }

    private void VerifyRefreshNotDeferred()
    {
        if (_deferLevel > 0)
            throw new InvalidOperationException("The view cannot be queried while a DeferRefresh cycle is active.");
    }

    private void Rebuild()
    {
        var culture = _culture;
        IEnumerable<object?> query = _detached ? Array.Empty<object?>() : SourceCollection.Cast<object?>();
        if (_filter is not null) query = query.Where(item => _filter(item));
        var sorts = SortDescriptions.ToArray();
        if (sorts.Length > 0)
        {
            var ordered = ApplySortLevel(query, sorts[0], culture, first: true);
            for (var i = 1; i < sorts.Length; i++)
                ordered = ApplySortLevel(ordered, sorts[i], culture, first: false);
            query = ordered;
        }
        _view = query.ToList();
        RebuildGroups(culture);
        if (_currentItem is null || !_view.Contains(_currentItem))
        {
            if (_view.Count == 0)
            {
                _currentItem = null;
                _currentPosition = -1;
                _beforeFirst = true;
                _afterLast = true;
            }
            else
            {
                _currentItem = _view[0];
                _currentPosition = 0;
                _beforeFirst = false;
                _afterLast = false;
            }
        }
        else
        {
            _currentPosition = _view.IndexOf(_currentItem);
            _beforeFirst = false;
            _afterLast = false;
        }
        OnPropertyChanged(string.Empty);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private static readonly object NullGroupKey = new();

    private void RebuildGroups(CultureInfo culture)
    {
        if (GroupDescriptions.Count == 0)
        {
            _groups = null;
            return;
        }
        var descs = GroupDescriptions.ToArray();
        var roots = new List<object>();
        // Dictionary keys cannot be null: fold them onto a sentinel while
        // the group keeps the real (null) name, like WPF's null group.
        var lookup = new Dictionary<object, RuntimeViewGroup>(new GroupKeyComparer());
        foreach (var item in _view)
        {
            var key = descs[0].GroupNameFromItem(item, 0, culture) ?? NullGroupKey;
            if (!lookup.TryGetValue(key, out var group))
            {
                group = new RuntimeViewGroup(ReferenceEquals(key, NullGroupKey) ? null : key, descs.Length == 1);
                lookup[key] = group;
                roots.Add(group);
            }
            AddGrouped(group, item, descs, 1, culture);
        }
        foreach (var root in roots.OfType<RuntimeViewGroup>()) root.RaiseReset();
        _groups = new ReadOnlyObservableCollection<object>(new ObservableCollection<object>(roots));
    }

    private static void AddGrouped(RuntimeViewGroup group, object? item, GroupDescription[] descs, int level, CultureInfo culture)
    {
        if (level >= descs.Length)
        {
            group.Add(item!);
            return;
        }
        var key = descs[level].GroupNameFromItem(item, level, culture);
        foreach (var child in group.Items.OfType<RuntimeViewGroup>())
        {
            if (new GroupKeyComparer().Equals(child.Name, key))
            {
                AddGrouped(child, item, descs, level + 1, culture);
                return;
            }
        }
        var nested = new RuntimeViewGroup(key, level == descs.Length - 1);
        group.Add(nested);
        AddGrouped(nested, item, descs, level + 1, culture);
    }

    private static object? ReadSortKey(object? item, string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName)) return item;
        return BindingExpression.TryRead(item, propertyName, out var value) ? value : null;
    }

    private static IOrderedEnumerable<object?> ApplySortLevel(
        IEnumerable<object?> query, SortDescription description, CultureInfo culture, bool first)
    {
        // Stable chained ordering (OrderBy/ThenBy keep earlier levels); each
        // level reads its property through the binding path reader.
        Func<object?, object?> key = item => ReadSortKey(item, description.PropertyName);
        var comparer = new ViewComparer(culture);
        if (first)
            return description.Direction == ListSortDirection.Descending
                ? query.OrderByDescending(key, comparer)
                : query.OrderBy(key, comparer);
        var ordered = (IOrderedEnumerable<object?>)query;
        return description.Direction == ListSortDirection.Descending
            ? ordered.ThenByDescending(key, comparer)
            : ordered.ThenBy(key, comparer);
    }

    private sealed class ViewComparer(CultureInfo culture) : IComparer<object?>
    {
        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            if (x is string sx && y is string sy)
                return string.Compare(sx, sy, culture, CompareOptions.None);
            if (x.GetType() == y.GetType() && x is IComparable comparable)
            {
                try { return comparable.CompareTo(y); }
                catch (ArgumentException) { }
            }
            return string.Compare(x.ToString(), y.ToString(), culture, CompareOptions.None);
        }
    }

    private sealed class GroupKeyComparer : IEqualityComparer<object>
    {
        public new bool Equals(object? x, object? y) => object.Equals(x, y);

        public int GetHashCode(object obj) => obj?.GetHashCode() ?? 0;
    }

    private sealed class DeferScope(CollectionView owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner._deferLevel--;
            if (owner._deferLevel == 0 && owner._needsRefresh)
            {
                owner._needsRefresh = false;
                owner.Rebuild();
            }
        }
    }
}

/// <summary>List view (matches System.Windows.Data.ListCollectionView).</summary>
public sealed class ListCollectionView : CollectionView
{
    public ListCollectionView(IList list)
        : base(list)
    {
    }
}

/// <summary>
/// Markup-accessible view factory (matches System.Windows.Data.CollectionViewSource):
/// Source plus forwarded SortDescriptions/GroupDescriptions/Filter materialize
/// View on demand; GetDefaultView caches one shared live view per source.
/// </summary>
public class CollectionViewSource : DependencyObject
{
    private static readonly ConditionalWeakTable<object, ICollectionView> DefaultViews = new();

    private object? _source;
    private CollectionView? _view;
    private Type? _collectionViewType;
    private CultureInfo? _culture;
    private int _initializing;

    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(
            "Source",
            typeof(object),
            typeof(CollectionViewSource),
            new PropertyMetadata(null, OnSourceChanged),
            value => value is null || (value is IEnumerable && value is not ICollectionView));

    public static readonly DependencyPropertyKey ViewPropertyKey =
        DependencyProperty.RegisterReadOnly(
            "View",
            typeof(ICollectionView),
            typeof(CollectionViewSource),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ViewProperty = ViewPropertyKey.DependencyProperty;

    public static readonly DependencyProperty CollectionViewTypeProperty =
        DependencyProperty.Register(
            "CollectionViewType",
            typeof(Type),
            typeof(CollectionViewSource),
            new PropertyMetadata(null, OnCollectionViewTypeChanged),
            value => value is null || typeof(ICollectionView).IsAssignableFrom((Type)value));

    public CollectionViewSource()
    {
        SortDescriptions = new SortDescriptionCollection();
        SortDescriptions.CollectionChanged += (_, _) => RefreshView();
        GroupDescriptions = new ObservableCollection<GroupDescription>();
        GroupDescriptions.CollectionChanged += (_, _) => RefreshView();
    }

    /// <summary>Underlying collection (validated: enumerable, never a view).</summary>
    public object? Source
    {
        get => _source;
        set
        {
            var validator = DependencyProperty.TryGetValidator(typeof(CollectionViewSource), "Source");
            if (validator is not null && !validator(value!))
                throw new ArgumentException("Source must be an enumerable collection and not an ICollectionView.");
            var old = _source;
            _source = value;
            OnSourceChanged(old, value);
            EnsureView();
        }
    }

    /// <summary>Materialized view over Source (null until Source is set).</summary>
    public ICollectionView? View => _view;

    /// <summary>View implementation created for Source (init-only, null selects by source shape).</summary>
    public Type? CollectionViewType
    {
        get => _collectionViewType;
        set
        {
            if (_initializing == 0)
                throw new InvalidOperationException("CollectionViewType may only be set during initialization.");
            if (value is not null && !typeof(ICollectionView).IsAssignableFrom(value))
                throw new ArgumentException("CollectionViewType must implement ICollectionView.");
            _collectionViewType = value;
            OnCollectionViewTypeChanged();
            EnsureView();
        }
    }

    /// <summary>Culture forwarded to the materialized view.</summary>
    public CultureInfo? Culture
    {
        get => _culture;
        set
        {
            _culture = value;
            if (_view is not null && value is not null) _view.Culture = value;
        }
    }

    public SortDescriptionCollection SortDescriptions { get; }

    public ObservableCollection<GroupDescription> GroupDescriptions { get; }

    private FilterEventHandler? _filter;

    public event FilterEventHandler? Filter
    {
        add
        {
            _filter += value;
            RefreshView();
        }
        remove
        {
            _filter -= value;
            RefreshView();
        }
    }

    /// <summary>Default view for a source, creating and caching it (matches GetDefaultView).</summary>
    public static ICollectionView? GetDefaultView(object? source)
    {
        if (source is null) return null;
        // A foreign view implementation is already a view (the engine has no
        // proxy layer, so it is returned as-is, like the unwrapped original).
        if (source is ICollectionView && source is not CollectionView) return (ICollectionView)source;
        if (source is IEnumerable enumerable)
            return DefaultViews.GetValue(enumerable, static key => CreateDefaultView((IEnumerable)key));
        return null;
    }

    private static ICollectionView CreateDefaultView(IEnumerable source) =>
        // Rule 0: a view is its own default (ItemCollection included).
        source is CollectionView own ? own :
        source is IList list ? new ListCollectionView(list) :
        new CollectionView(source);

    /// <summary>Whether the view is its source's cached default (matches IsDefaultView).</summary>
    public static bool IsDefaultView(ICollectionView? view)
    {
        if (view is null) return true;
        return DefaultViews.TryGetValue(view.SourceCollection, out var current) && ReferenceEquals(current, view);
    }

    /// <summary>Defers the materialized view's refresh (matches DeferRefresh).</summary>
    public IDisposable DeferRefresh() => (_view as CollectionView)?.DeferRefresh() ?? new NoopDefer();

    /// <summary>Signals initialization start (matches ISupportInitialize.BeginInit).</summary>
    public void BeginInit() => _initializing++;

    /// <summary>Signals initialization end and materializes the view (matches ISupportInitialize.EndInit).</summary>
    public void EndInit()
    {
        if (_initializing > 0) _initializing--;
        EnsureView();
    }

    protected virtual void OnSourceChanged(object? oldSource, object? newSource)
    {
    }

    protected virtual void OnCollectionViewTypeChanged()
    {
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var source = (CollectionViewSource)d;
        source.OnSourceChanged(e.OldValue, e.NewValue);
        source.EnsureView();
    }

    private static void OnCollectionViewTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CollectionViewSource)d).OnCollectionViewTypeChanged();

    private void RefreshView() => EnsureView(refreshOnly: true);

    private IEnumerable? _viewSource;

    private void EnsureView(bool refreshOnly = false)
    {
        if (_initializing > 0 && !refreshOnly) return;
        if (_source is null)
        {
            _view = null;
            _viewSource = null;
            SetViewProperty(null);
            return;
        }
        if (_source is not IEnumerable enumerable) return;
        if (_view is null || !ReferenceEquals(_viewSource, enumerable))
        {
            var created = CreateView(enumerable);
            _view = created;
            _viewSource = enumerable;
            SetViewProperty(created);
            ApplyForwardedState(created);
        }
        else if (refreshOnly)
        {
            ApplyForwardedState(_view);
        }
    }

    private CollectionView CreateView(IEnumerable enumerable)
    {
        if (_collectionViewType is not null)
        {
            try
            {
                if (Activator.CreateInstance(_collectionViewType, enumerable) is CollectionView typed)
                    return typed;
            }
            catch (MissingMethodException ex)
            {
                throw new ArgumentException($"CollectionViewType {_collectionViewType.Name} needs a constructor taking the source.", ex);
            }
        }
        return enumerable is IList list ? new ListCollectionView(list) : new CollectionView(enumerable);
    }

    private void ApplyForwardedState(CollectionView view)
    {
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            foreach (var sort in SortDescriptions) view.SortDescriptions.Add(sort);
            view.GroupDescriptions.Clear();
            foreach (var group in GroupDescriptions) view.GroupDescriptions.Add(group);
            if (_culture is not null) view.Culture = _culture;
            view.Filter = _filter is null ? null : item => PassesSourceFilter(item);
        }
    }

    private bool PassesSourceFilter(object? item)
    {
        var handlers = _filter;
        if (handlers is null) return true;
        foreach (FilterEventHandler handler in handlers.GetInvocationList())
        {
            var args = new FilterEventArgs(item);
            handler(this, args);
            if (!args.Accepted) return false;
        }
        return true;
    }

    private void SetViewProperty(ICollectionView? view) =>
        SetValue(ViewPropertyKey, view);

    private sealed class NoopDefer : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
