using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace DesktopWasm.Wpf;

// Items controls v1 (PresentationFramework subset, shim, not port): the
// Selector/ItemsControl generator family converted gallery and ScreenToGif
// bind, with member-evidenced APIs (SelectedIndex/Item/Value, ItemsSource,
// ItemContainerGenerator, SelectedItems/SelectAll/UnselectAll/ScrollIntoView,
// CollectionView currency, HeaderedItemsControl.Header, ComboBox text/drop,
// IValueConverter, SystemParameters, Orientation/ResizeMode/WindowState).
// Generator containers default to Label (renders content text); the legacy
// ListBox keeps its row model and Action-typed SelectionChanged event while
// carrying the bindable ItemsSource half of the contract itself (container
// generation, live sync, parity guards), and TreeView/DataGrid keep theirs
// untouched (unifying them under Selector stays a later milestone once their
// subscribers migrate to the typed handler).

/// <summary>Stack orientation (matches System.Windows.Controls.Orientation).</summary>
public enum Orientation
{
    Horizontal,
    Vertical,
}

/// <summary>Window resize behavior (matches System.Windows.ResizeMode).</summary>
public enum ResizeMode
{
    NoResize,
    CanMinimize,
    CanResize,
    CanResizeWithGrip,
}

/// <summary>Window state (matches System.Windows.WindowState).</summary>
public enum WindowState
{
    Normal,
    Minimized,
    Maximized,
}

/// <summary>Window chrome style (matches System.Windows.WindowStyle; parsed and stored, chrome stays browser-owned).</summary>
public enum WindowStyle
{
    None,
    SingleBorderWindow,
    ThreeDBorderWindow,
    ToolWindow,
}

/// <summary>Initial window placement (matches System.Windows.WindowStartupLocation; parsed and stored).</summary>
public enum WindowStartupLocation
{
    Manual,
    CenterScreen,
    CenterOwner,
}

/// <summary>Selection behavior (matches System.Windows.Controls.SelectionMode).</summary>
public enum SelectionMode
{
    Single,
    Multiple,
    Extended,
}

/// <summary>System metrics (matches System.Windows.SystemParameters).</summary>
public static class SystemParameters
{
    /// <summary>
    /// Raised when a system metric changes (matches the WPF
    /// StaticPropertyChangedEventHandler shape converted code subscribes to;
    /// engine values are fixed WASM defaults, preference broadcasts raise it).
    /// </summary>
    public static event PropertyChangedEventHandler? StaticPropertyChanged;

    internal static void RaiseStaticPropertyChanged(string propertyName) =>
        StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));

    public static bool HighContrast => false;
    public static bool IsGlassEnabled => false;
    public static double MinimumHorizontalDragDistance => 4;
    public static double MinimumVerticalDragDistance => 4;
    public static double VirtualScreenLeft => 0;
    public static double VirtualScreenTop => 0;
    public static double VirtualScreenWidth => 1024;
    public static double VirtualScreenHeight => 768;

    /// <summary>
    /// Primary display width in DIPs (matches
    /// System.Windows.SystemParameters.PrimaryScreenWidth; converted
    /// floating-window clamp code (KeepInsideNearestMonitor) reads it; backed
    /// by the session primary screen, which equals the virtual screen here).
    /// </summary>
    public static double PrimaryScreenWidth => System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;

    /// <summary>
    /// Primary display height in DIPs (matches
    /// System.Windows.SystemParameters.PrimaryScreenHeight; same session
    /// backing as <see cref="PrimaryScreenWidth"/>).
    /// </summary>
    public static double PrimaryScreenHeight => System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;
    public static Thickness WindowResizeBorderThickness => new(4, 4, 4, 4);

    /// <summary>
    /// Scrollbar-button metric resource key (matches
    /// System.Windows.SystemParameters.VerticalScrollBarButtonHeightKey,
    /// live-verified on net10 as a SystemResourceKey singleton whose
    /// InternalKey is VerticalScrollBarButtonHeight; ToString is the metric
    /// name without the Key suffix). Compiled theme dictionaries key their
    /// scrollbar-button metric entry with it (HandyControl theme.baml entry
    /// #93 holds Double 30 under this key); lookups resolve through this
    /// same singleton, so Add and FindResource share identity.
    /// </summary>
    public static ResourceKey VerticalScrollBarButtonHeightKey { get; } =
        new SystemResourceKey("VerticalScrollBarButtonHeight");

    /// <summary>Vertical scrollbar width in DIPs (matches
    /// System.Windows.SystemParameters.VerticalScrollBarWidth; fixed WASM
    /// default, the 96dpi Win32 SM_CXVSCROLL metric).</summary>
    public static double VerticalScrollBarWidth => 17;

    /// <summary>Vertical scrollbar width key (matches
    /// System.Windows.SystemParameters.VerticalScrollBarWidthKey).</summary>
    public static ResourceKey VerticalScrollBarWidthKey { get; } =
        new SystemResourceKey("VerticalScrollBarWidth");

    /// <summary>Horizontal scrollbar height in DIPs (matches
    /// System.Windows.SystemParameters.HorizontalScrollBarHeight; fixed WASM
    /// default, the 96dpi Win32 SM_CYHSCROLL metric).</summary>
    public static double HorizontalScrollBarHeight => 17;

    /// <summary>Horizontal scrollbar height key (matches
    /// System.Windows.SystemParameters.HorizontalScrollBarHeightKey).</summary>
    public static ResourceKey HorizontalScrollBarHeightKey { get; } =
        new SystemResourceKey("HorizontalScrollBarHeight");

    /// <summary>Horizontal scrollbar button width in DIPs (matches
    /// System.Windows.SystemParameters.HorizontalScrollBarButtonWidth;
    /// fixed WASM default, the 96dpi arrow-button metric).</summary>
    public static double HorizontalScrollBarButtonWidth => 16;

    /// <summary>Horizontal scrollbar button width key (matches
    /// System.Windows.SystemParameters.HorizontalScrollBarButtonWidthKey).</summary>
    public static ResourceKey HorizontalScrollBarButtonWidthKey { get; } =
        new SystemResourceKey("HorizontalScrollBarButtonWidth");

    /// <summary>ComboBox popup animation (matches
    /// System.Windows.SystemParameters.ComboBoxPopupAnimation; popups slide
    /// like WPF by default).</summary>
    public static PopupAnimation ComboBoxPopupAnimation => PopupAnimation.Slide;

    /// <summary>ComboBox popup animation key (matches
    /// System.Windows.SystemParameters.ComboBoxPopupAnimationKey).</summary>
    public static ResourceKey ComboBoxPopupAnimationKey { get; } =
        new SystemResourceKey("ComboBoxPopupAnimation");
}

/// <summary>Selection change payload (matches System.Windows.Controls.SelectionChangedEventArgs).</summary>
public class SelectionChangedEventArgs : RoutedEventArgs
{
    public SelectionChangedEventArgs(RoutedEvent routedEvent, IList removedItems, IList addedItems)
    {
        RoutedEvent = routedEvent;
        RemovedItems = removedItems;
        AddedItems = addedItems;
    }

    public IList RemovedItems { get; }
    public IList AddedItems { get; }
}

/// <summary>Selection change handler (matches System.Windows.Controls.SelectionChangedEventHandler).</summary>
public delegate void SelectionChangedEventHandler(object sender, SelectionChangedEventArgs e);

/// <summary>Value converter contract (matches System.Windows.Data.IValueConverter).</summary>
public interface IValueConverter
{
    object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture);
    object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture);
}

/// <summary>
/// Converter type annotation (matches
/// System.Windows.Data.ValueConversionAttribute, verified against live .NET:
/// Class-only, AllowMultiple, public (Type, Type) ctor, SourceType/TargetType
/// getters, settable ParameterType). Converted OxyPlot annotates
/// OxyColorConverter with [ValueConversion(typeof(OxyColor), typeof(Color))];
/// the converter keeps attributes verbatim, so the engine carries the
/// contract for the gap check.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ValueConversionAttribute : Attribute
{
    public ValueConversionAttribute(Type sourceType, Type targetType)
    {
        SourceType = sourceType;
        TargetType = targetType;
    }

    public Type SourceType { get; }
    public Type TargetType { get; }
    public Type? ParameterType { get; set; }
}

/// <summary>
/// Boolean-to-visibility conversion (matches
/// System.Windows.Controls.BooleanToVisibilityConverter, which the Fluent
/// theme ships as a resource for gallery tile warnings): true maps to
/// Visible, anything else to Collapsed; back-conversion maps Visible to
/// true and everything else to false.
/// </summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>
/// Null-to-visibility conversion (matches the HandyControl lib
/// Tools/Converter/Object2VisibilityConverter the staged CardDemo footer and
/// header slots read through {TemplateBinding}: null maps to Collapsed,
/// anything else to Visible; back-conversion throws like upstream).
/// Carried in the engine because the lib converter ships BAML-only behind
/// the line-17 SkinDefault stop.
/// </summary>
public sealed class Object2VisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Details-scrolling conversion (matches
/// System.Windows.Controls.BooleanToSelectiveScrollingOrientationConverter,
/// exposed as DataGrid.RowDetailsScrollingConverter for the DataGrid
/// template): every input maps to Both (live-verified), and back-conversion
/// throws like upstream.
/// </summary>
public sealed class BooleanToSelectiveScrollingOrientationConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        SelectiveScrollingOrientation.Both;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>
/// Header-band visibility conversion (matches
/// System.Windows.Controls.DataGridHeadersVisibilityToVisibilityConverter,
/// exposed as DataGrid.HeadersVisibilityConverter for the DataGrid
/// template): the parameter names the band as a DataGridHeadersVisibility
/// enum (strings never match, live-verified); Visible when the value covers
/// the band, Collapsed otherwise; non-Visibility targets yield UnsetValue;
/// back-conversion throws like upstream.
/// </summary>
public sealed class DataGridHeadersVisibilityToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (targetType != typeof(Visibility))
            return DependencyProperty.UnsetValue;
        return value is DataGridHeadersVisibility visibility
            && parameter is DataGridHeadersVisibility band
            && (visibility & band) == band
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>
/// Menu repeat-button visibility (matches
/// System.Windows.Controls.MenuScrollingVisibilityConverter, the framework
/// multi-converter the HandyControl theme keys as
/// "MenuScrollingVisibilityConverter" for its scrolling menus): logic mirrors
/// dotnet/wpf MenuScrollingVisibilityConverter.Convert exactly (scrollbar
/// hidden forces Collapsed; a visible scrollbar hides only the button at the
/// scroll target percent; malformed inputs yield UnsetValue; ConvertBack
/// yields DoNothing). Carried in the engine because the framework type ships
/// only in the desktop PresentationFramework pack.
/// </summary>
public sealed class MenuScrollingVisibilityConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (parameter is null ||
            values is null ||
            values.Length != 4 ||
            values[0] is not Visibility ||
            values[1] is not double ||
            values[2] is not double ||
            values[3] is not double)
        {
            return DependencyProperty.UnsetValue;
        }
        if (parameter is not double && parameter is not string)
        {
            return DependencyProperty.UnsetValue;
        }
        var computedVerticalScrollBarVisibility = (Visibility)values[0];
        if (computedVerticalScrollBarVisibility == Visibility.Visible)
        {
            double target;
            if (parameter is string text)
                target = double.Parse(text, System.Globalization.NumberFormatInfo.InvariantInfo);
            else
                target = (double)parameter;
            var verticalOffset = (double)values[1];
            var extentHeight = (double)values[2];
            var viewportHeight = (double)values[3];
            if (extentHeight != viewportHeight) // Avoid divide by 0
            {
                var percent = Math.Min(100.0, Math.Max(0.0, verticalOffset * 100.0 / (extentHeight - viewportHeight)));
                if (AreClose(percent, target))
                    return Visibility.Collapsed;
            }
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture) =>
        new object[] { Binding.DoNothing };

    /// <summary>Matches MS.Internal.DoubleUtil.AreClose (relative epsilon).</summary>
    private static bool AreClose(double value1, double value2)
    {
        if (value1 == value2) return true;
        const double epsilon = 2.2204460492503131e-016;
        var eps = (Math.Abs(value1) + Math.Abs(value2) + 10.0) * epsilon;
        var delta = value1 - value2;
        return -eps < delta && eps > delta;
    }
}

/// <summary>Source-to-target push timing (matches System.Windows.Data.UpdateSourceTrigger).</summary>
public enum UpdateSourceTrigger
{
    Default,
    PropertyChanged,
    LostFocus,
    Explicit,
}

/// <summary>Group contract (matches System.ComponentModel.GroupDescription).</summary>
public abstract class GroupDescription
{
    public abstract object? GroupNameFromItem(object? item, int level, System.Globalization.CultureInfo culture);
}

/// <summary>Property group (matches System.Windows.Data.PropertyGroupDescription).</summary>
public sealed class PropertyGroupDescription : GroupDescription
{
    public PropertyGroupDescription()
    {
    }

    public PropertyGroupDescription(string propertyName) => PropertyName = propertyName;

    public PropertyGroupDescription(string propertyName, IValueConverter converter)
    {
        PropertyName = propertyName;
        Converter = converter;
    }

    public string? PropertyName { get; set; }
    public IValueConverter? Converter { get; set; }
    public StringComparison StringComparison { get; set; } = StringComparison.Ordinal;

    public override object? GroupNameFromItem(object? item, int level, System.Globalization.CultureInfo culture)
    {
        if (item is null || PropertyName is null) return null;
        if (!BindingExpression.TryRead(item, PropertyName, out var value) || value is null) return null;
        return Converter?.Convert(value, typeof(object), null, culture) ?? value;
    }
}

/// <summary>Item list (matches System.Windows.Controls.ItemCollection).</summary>
/// <remarks>
/// Real WPF ItemCollection derives from CollectionView, and converted member
/// references bind against that base (e.g. TreeView.Items.Count compiles to
/// CollectionView.get_Count). The engine matches the inheritance so those
/// calls dispatch onto the real store instead of misreading a foreign object.
/// Count/indexer/Contains/IndexOf read the inner list (unfiltered, like WPF);
/// the inherited view surface (filter/sort/group/currency) stays available.
/// </remarks>
public sealed class ItemCollection : CollectionView, IList
{
    private readonly List<object> _items;

    internal ItemCollection(Action changed)
        : base(NewStore(out var store))
    {
        _items = store;
        Changed = changed;
    }

    private static List<object> NewStore(out List<object> store) => store = new List<object>();

    internal Action Changed { get; set; }

    /// <summary>Inner-list count (matches ItemCollection.Count).</summary>
    public override int Count => _items.Count;

    public bool IsReadOnly => false;

    public bool IsFixedSize => false;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    /// <summary>Inner-list item (matches ItemCollection indexer).</summary>
    public object? this[int index]
    {
        get => _items[index];
        set
        {
            _items[index] = value!;
            AfterChange();
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = value;
    }

    public int Add(object? value)
    {
        _items.Add(value!);
        AfterChange();
        return _items.Count - 1;
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        AfterChange();
    }

    public bool Contains(object? value) => _items.Contains(value!);

    public int IndexOf(object? value) => _items.IndexOf(value!);

    public void Insert(int index, object? value)
    {
        _items.Insert(index, value!);
        AfterChange();
    }

    public void Remove(object? value)
    {
        if (_items.Remove(value!)) AfterChange();
    }

    public void RemoveAt(int index)
    {
        _items.RemoveAt(index);
        AfterChange();
    }

    public void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    private void AfterChange()
    {
        Changed();
        Refresh();
    }
}

/// <summary>
/// Container-owner contract behind <see cref="ItemContainerGenerator"/>:
/// ItemsControl implements it with its items/templates/virtuals, and the
/// DataGrid family implements it with row/cell containers, so converted
/// code can reach ContainerFromIndex/ContainerFromItem off both.
/// </summary>
internal interface IItemContainerOwner
{
    System.Collections.IList ContainerItems { get; }

    DataTemplate? ItemTemplate { get; }

    DataTemplateSelector? ItemTemplateSelector { get; }

    bool IsItemItsOwnContainer(object item);

    DependencyObject GetContainerForItem();

    void PrepareContainerForItem(DependencyObject container, object? item);

    void ClearContainerForItem(DependencyObject container, object item);

    void AddGeneratedChild(DependencyObject container);

    void RemoveGeneratedChild(DependencyObject container);
}

/// <summary>Generation state (matches System.Windows.Controls.Primitives.GeneratorStatus).</summary>
public enum GeneratorStatus
{
    NotStarted = 0,
    GeneratingContainers = 1,
    ContainersGenerated = 2,
    Error = 3,
}

/// <summary>Item-to-container map (matches System.Windows.Controls.ItemContainerGenerator).</summary>
public class ItemContainerGenerator
{
    private readonly IItemContainerOwner _owner;
    private readonly Dictionary<object, DependencyObject> _containers = new(ContainerIdentityComparer.Instance);
    private readonly Dictionary<DependencyObject, object> _items = new();
    private GeneratorStatus _status = GeneratorStatus.NotStarted;

    internal ItemContainerGenerator(ItemsControl owner) => _owner = owner;

    internal ItemContainerGenerator(IItemContainerOwner owner) => _owner = owner;

    /// <summary>Generation state (starts NotStarted; Error only on a generation throw).</summary>
    public GeneratorStatus Status => _status;

    /// <summary>Raised when Status actually changes (matches StatusChanged).</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Container-set change notice (matches ItemContainerGenerator.ItemsChanged).</summary>
    public event ItemsChangedEventHandler? ItemsChanged;

    private void SetStatus(GeneratorStatus status)
    {
        if (_status == status) return;
        _status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Mapped container without creating (hierarchical overrides probe children first).</summary>
    internal bool TryGetContainer(object? item, out DependencyObject? container)
    {
        container = null;
        return item is not null && _containers.TryGetValue(item, out container);
    }

    /// <summary>Mapped item without creating.</summary>
    internal bool TryGetItem(DependencyObject container, out object? item) =>
        _items.TryGetValue(container, out item);

    /// <summary>Container for a data item (created on demand via the owner overrides).</summary>
    public virtual DependencyObject? ContainerFromItem(object? item)
    {
        if (item is null) return null;
        if (TryGetContainer(item, out var existing)) return existing;
        // An already-realized container maps to itself (prevents double
        // parenting when callers pass containers back in, e.g. tree nodes).
        if (item is DependencyObject realized && _items.ContainsKey(realized)) return realized;
        SetStatus(GeneratorStatus.GeneratingContainers);
        try
        {
            var isOwnContainer = _owner.IsItemItsOwnContainer(item) && item is DependencyObject;
            DependencyObject container = isOwnContainer && item is DependencyObject own
                ? own
                : _owner.GetContainerForItem();
            if (!isOwnContainer && _owner.ItemTemplate is null &&
                _owner.ItemTemplateSelector?.SelectTemplate(item, container) is { } chosen)
                container = chosen.LoadContent();
            _owner.PrepareContainerForItem(container, item);
            _containers[item] = container;
            _items[container] = item;
            _owner.AddGeneratedChild(container);
            SetStatus(GeneratorStatus.ContainersGenerated);
            RaiseItemsChanged(System.Collections.Specialized.NotifyCollectionChangedAction.Add, _owner.ContainerItems.IndexOf(item), 1, 1);
            return container;
        }
        catch
        {
            SetStatus(GeneratorStatus.Error);
            throw;
        }
    }

    /// <summary>Container at an item index (null when out of range).</summary>
    public DependencyObject? ContainerFromIndex(int index)
    {
        if ((uint)index >= (uint)_owner.ContainerItems.Count) return null;
        return ContainerFromItem(_owner.ContainerItems[index]);
    }

    /// <summary>Data item behind a container (the container itself when it is its own item).</summary>
    public virtual object? ItemFromContainer(DependencyObject container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return TryGetItem(container, out var item) ? item : null;
    }

    /// <summary>Item index behind a container (-1 when unknown).</summary>
    public int IndexFromContainer(DependencyObject container)
    {
        ArgumentNullException.ThrowIfNull(container);
        var item = ItemFromContainer(container);
        return item is null ? -1 : _owner.ContainerItems.IndexOf(item);
    }

    internal void Refresh()
    {
        foreach (var container in _containers.Values)
        {
            _owner.ClearContainerForItem(container, _items[container]);
            _owner.RemoveGeneratedChild(container);
        }
        _containers.Clear();
        _items.Clear();
        SetStatus(GeneratorStatus.NotStarted);
        RaiseItemsChanged(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, -1, 0, 0);
    }

    /// <summary>
    /// External reset notice (matches a generator Refresh without owning the
    /// containers): the legacy ListBox owns its rows and calls this after
    /// every SyncGeneratedRows so converted ItemsChanged subscribers observe
    /// row-set changes. No status transition (generation stays legacy-side).
    /// </summary>
    internal void NotifyItemsReset() =>
        ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(
            System.Collections.Specialized.NotifyCollectionChangedAction.Reset,
            new GeneratorPosition(-1, 0), new GeneratorPosition(-1, 0), 0, 0));

    private void RaiseItemsChanged(System.Collections.Specialized.NotifyCollectionChangedAction action, int index, int itemCount, int itemUICount) =>
        ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(action, new GeneratorPosition(index, 0), new GeneratorPosition(-1, 0), itemCount, itemUICount));

    private sealed class ContainerIdentityComparer : IEqualityComparer<object>
    {
        public static ContainerIdentityComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

/// <summary>Items host (matches System.Windows.Controls.ItemsControl).</summary>
public class ItemsControl : Control, IItemContainerOwner
{
    /// <summary>
    /// Source identity (matches System.Windows.Controls.ItemsControl.ItemsSourceProperty;
    /// converted AvalonDock menus bind it in OnOpened; the CLR wrapper keeps
    /// the snapshot flow and dual-lands the DP store so GetValue readers see
    /// CLR-applied sources too).
    /// </summary>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register("ItemsSource", typeof(System.Collections.IEnumerable), typeof(ItemsControl),
            new FrameworkPropertyMetadata(null));

    private IEnumerable? _itemsSource;
    private bool _bulkUpdate;

    public ItemsControl()
    {
        Items = new ItemCollection(OnCollectionChanged);
        ItemContainerGenerator = new ItemContainerGenerator(this);
    }

    /// <summary>Data items (UIElement items render directly; others get generated Label containers).</summary>
    public ItemCollection Items { get; }

    private static readonly DependencyPropertyKey IsGroupingPropertyKey =
        DependencyProperty.RegisterReadOnly("IsGrouping", typeof(bool), typeof(ItemsControl),
            new FrameworkPropertyMetadata(false));

    /// <summary>Grouping flag (matches ItemsControl.IsGroupingProperty; read-only like WPF).</summary>
    public static readonly DependencyProperty IsGroupingProperty = IsGroupingPropertyKey.DependencyProperty;

    /// <summary>Whether items are grouped (always false; grouping stays a boundary).</summary>
    public bool IsGrouping => (bool)(GetValue(IsGroupingProperty) ?? false);

    private static readonly DependencyPropertyKey AlternationIndexPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly("AlternationIndex", typeof(int), typeof(ItemsControl),
            new FrameworkPropertyMetadata(0));

    /// <summary>Alternation index (matches ItemsControl.AlternationIndexProperty; read-only like WPF).</summary>
    public static readonly DependencyProperty AlternationIndexProperty = AlternationIndexPropertyKey.DependencyProperty;

    /// <summary>Reads the alternation index (assigned from AlternationCount in WPF; zero in v1).</summary>
    public static int GetAlternationIndex(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (int)(element.GetValue(AlternationIndexProperty) ?? 0);
    }

    /// <summary>Property path rendered for data items (stored; container text binding stays a boundary).</summary>
    public string? DisplayMemberPath { get; set; }

    /// <summary>
    /// Item-format identity (matches System.Windows.Controls.ItemsControl.ItemStringFormatProperty;
    /// the CLR wrapper keeps the value and dual-lands the DP store so GetValue readers see
    /// CLR-applied formats too, like the ItemsSource dual-backing).
    /// </summary>
    public static readonly DependencyProperty ItemStringFormatProperty =
        DependencyProperty.Register("ItemStringFormat", typeof(string), typeof(ItemsControl),
            new FrameworkPropertyMetadata((string?)null));

    private string? _itemStringFormat;

    /// <summary>Composite format for data items shown as strings (stored; container formatting stays a boundary, like DisplayMemberPath).</summary>
    public string? ItemStringFormat
    {
        get => _itemStringFormat;
        set
        {
            if (string.Equals(_itemStringFormat, value, StringComparison.Ordinal)) return;
            _itemStringFormat = value;
            SetValue(ItemStringFormatProperty, value);
        }
    }

    /// <summary>Alternating-row count (stored; AlternationIndex styling stays a boundary).</summary>
    public int AlternationCount { get; set; }

    /// <summary>
    /// Text-search gate (matches System.Windows.Controls.ItemsControl.IsTextSearchEnabled,
    /// verified declaring type against the .NET 9 WindowsDesktop reference
    /// assemblies; default true like real WPF). Parsed and retained: the PC
    /// Space Map MainWindow disables it on its path box. Type-ahead selection
    /// stays a boundary.
    /// </summary>
    public bool IsTextSearchEnabled { get; set; } = true;

    public IEnumerable? ItemsSource
    {
        get => _itemsSource;
        set
        {
            if (ReferenceEquals(_itemsSource, value)) return;
            DetachViewSource();
            _itemsSource = value;
            SetValue(ItemsSourceProperty, value);
            try
            {
                _bulkUpdate = true;
                using (Items.DeferRefresh())
                {
                    Items.Clear();
                    if (value is not null)
                        foreach (var item in value) Items.Add(item!);
                }
            }
            finally
            {
                _bulkUpdate = false;
            }
            AttachViewSource(value);
            Regenerate();
            InvalidateMeasure();
        }
    }

    private NotifyCollectionChangedEventHandler? _viewSourceHandler;

    /// <summary>
    /// Live view tracking (matches ItemsControl listening to its default
    /// collection view): a bound ICollectionView repaints containers when the
    /// view refreshes (filter/sort/group change) instead of freezing the
    /// snapshot taken at bind time. Plain enumerables keep snapshot semantics.
    /// </summary>
    private void AttachViewSource(IEnumerable? value)
    {
        if (value is ICollectionView view)
        {
            NotifyCollectionChangedEventHandler handler = (_, _) =>
            {
                try
                {
                    _bulkUpdate = true;
                    Items.Clear();
                    foreach (var item in view) Items.Add(item!);
                }
                finally
                {
                    _bulkUpdate = false;
                }
                Regenerate();
                InvalidateMeasure();
            };
            _viewSourceHandler = handler;
            view.CollectionChanged += handler;
        }
    }

    private void DetachViewSource()
    {
        if (_itemsSource is ICollectionView oldView && _viewSourceHandler is not null)
            oldView.CollectionChanged -= _viewSourceHandler;
        _viewSourceHandler = null;
    }

    private ItemContainerGenerator? _generator;

    /// <summary>
    /// Row-container map (matches ItemsControl.ItemContainerGenerator,
    /// constructed in the ctor). Legacy-ListBox bridge: converted code calls
    /// this getter on the legacy ListBox (framework ListBox IS an
    /// ItemsControl, so the call is well-typed upstream, but the engine maps
    /// &lt;ListBox&gt; to the legacy Control which is not an ItemsControl; a
    /// direct backing-field read would hand back whatever reference sits at
    /// that offset and corrupt it on subscribe). Dispatch to the legacy
    /// generator instead; real ItemsControl receivers are untouched.
    /// </summary>
    public ItemContainerGenerator ItemContainerGenerator
    {
        get => (object)this is ListBox legacy ? legacy.ItemContainerGenerator : _generator!;
        protected set => _generator = value;
    }

    System.Collections.IList IItemContainerOwner.ContainerItems => Items;

    DataTemplate? IItemContainerOwner.ItemTemplate => ItemTemplate;

    DataTemplateSelector? IItemContainerOwner.ItemTemplateSelector => ItemTemplateSelector;

    bool IItemContainerOwner.IsItemItsOwnContainer(object item) => IsItemItsOwnContainerOverride(item);

    DependencyObject IItemContainerOwner.GetContainerForItem() => GetContainerForItemOverride();

    void IItemContainerOwner.PrepareContainerForItem(DependencyObject container, object? item) =>
        PrepareContainerForItemOverride(container, item!);

    void IItemContainerOwner.ClearContainerForItem(DependencyObject container, object item) =>
        ClearContainerForItemOverride(container, item);

    void IItemContainerOwner.AddGeneratedChild(DependencyObject container) => AddGeneratedChild(container);

    void IItemContainerOwner.RemoveGeneratedChild(DependencyObject container) => RemoveGeneratedChild(container);

    public bool HasItems => Items.Count > 0;

    private DataTemplate? _itemTemplate;
    private Panel? _itemsHost;

    /// <summary>
    /// Per-item visual factory (matches ItemsControl.ItemTemplate): when set,
    /// non-UIElement items inflate a fresh templated root with the item as
    /// DataContext instead of a Label container. ItemTemplate wins over
    /// ItemTemplateSelector, like WPF's apply order.
    /// </summary>
    public DataTemplate? ItemTemplate
    {
        get => _itemTemplate;
        set
        {
            if (ReferenceEquals(_itemTemplate, value)) return;
            _itemTemplate = value;
            Regenerate();
        }
    }

    private DataTemplateSelector? _itemTemplateSelector;

    /// <summary>
    /// Per-item visual choice (matches ItemsControl.ItemTemplateSelector):
    /// when set (and ItemTemplate is null), each shell container asks the
    /// selector for its template, like the ScreenToGif
    /// ComboBoxItemTemplateSelector Selected/DropDown choice.
    /// </summary>
    public DataTemplateSelector? ItemTemplateSelector
    {
        get => _itemTemplateSelector;
        set
        {
            if (ReferenceEquals(_itemTemplateSelector, value)) return;
            _itemTemplateSelector = value;
            Regenerate();
        }
    }

    /// <summary>
    /// Items host factory (matches ItemsControl.ItemsPanelProperty): when set,
    /// generated containers parent into one inflated panel instead of the control
    /// directly. The DP store is the value authority (the staged HandyControl
    /// RibbonTab implicit style's ItemsPanel setter lands here through
    /// DependencyProperty.FromName like WPF, and direct SetValue paths regenerate
    /// through the metadata callback); the CLR wrapper keeps the converted
    /// ElementGroup/ButtonGroup fallback (see Slice111HandyPanels), so explicit
    /// panels always win and every other control keeps the null default.
    /// </summary>
    public static readonly DependencyProperty ItemsPanelProperty =
        DependencyProperty.Register("ItemsPanel", typeof(ItemsPanelTemplate), typeof(ItemsControl),
            new FrameworkPropertyMetadata((ItemsPanelTemplate?)null, (d, _) => ((ItemsControl)d).Regenerate()));

    public ItemsPanelTemplate? ItemsPanel
    {
        get => (ItemsPanelTemplate?)GetValue(ItemsPanelProperty) ?? Slice111HandyPanels.DefaultGroupItemsPanel(this);
        set
        {
            if (ReferenceEquals((ItemsPanelTemplate?)GetValue(ItemsPanelProperty), value)) return;
            SetValue(ItemsPanelProperty, value);
        }
    }

    /// <summary>
    /// Per-container style identity (matches ItemsControl.ItemContainerStyleProperty):
    /// the DP store is the value authority (theme ComboBox ItemContainerStyle
    /// setters land here through DependencyProperty.FromName like WPF);
    /// the CLR wrapper keeps the current value. Applying the style to
    /// generated containers follows container-type fidelity (engine shells
    /// are Labels, not ComboBoxItems) and stays a boundary.
    /// </summary>
    public static readonly DependencyProperty ItemContainerStyleProperty =
        DependencyProperty.Register("ItemContainerStyle", typeof(Style), typeof(ItemsControl),
            new FrameworkPropertyMetadata((Style?)null, (d, _) => ((ItemsControl)d).Regenerate()));

    private Style? _itemContainerStyle;

    /// <summary>Style applied to generated item containers (stored; container application stays a boundary).</summary>
    public Style? ItemContainerStyle
    {
        get => _itemContainerStyle ?? (Style?)GetValue(ItemContainerStyleProperty);
        set
        {
            if (ReferenceEquals(_itemContainerStyle, value)) return;
            _itemContainerStyle = value;
            SetValue(ItemContainerStyleProperty, value);
            Regenerate();
        }
    }

    /// <summary>
    /// Item-container style picker (matches ItemsControl.ItemContainerStyleSelector):
    /// converted ButtonGroup.OnRender re-selects each item style through it
    /// (null keeps the current style, like WPF). Plain auto-property; the
    /// picker itself is the engine StyleSelector converted AvalonDock already binds.
    /// </summary>
    public StyleSelector? ItemContainerStyleSelector { get; set; }

    /// <summary>
    /// Replays the items-child walk for converted ElementGroup/ButtonGroup
    /// (see Slice111HandyPanels): their converted OnRender runs
    /// corner-radius/style code without painting, so the engine replays the
    /// visual-tree composition real WPF performs. Engine instances and every
    /// other converted type keep the base no-op: their OnRender already
    /// paints (including StepBar, which paints through base.OnRender and
    /// must not paint twice).
    /// </summary>
    internal override void RenderConvertedChildrenIfNeeded(DrawingContext dc)
    {
        if (!Slice111HandyPanels.IsConvertedGroupPanel(GetType()))
            return;
        foreach (var child in Children) RenderVisible(dc, child);
    }

    /// <summary>Owning ItemsControl above a container (null outside item trees).</summary>
    public static ItemsControl? ItemsControlFromItemContainer(DependencyObject container)
    {
        for (var node = container; node is not null; node = (node as FrameworkElement)?.Parent)
            if (node is ItemsControl owner && !ReferenceEquals(node, container))
                return owner;
        return null;
    }

    protected internal virtual DependencyObject GetContainerForItemOverride()
    {
        if (ItemTemplate is not null) return ItemTemplate.LoadContent();
        return new Label();
    }

    protected internal virtual bool IsItemItsOwnContainerOverride(object item) => item is UIElement;

    protected internal virtual void PrepareContainerForItemOverride(DependencyObject container, object item)
    {
        if (ItemTemplate is not null || ItemTemplateSelector is not null)
        {
            if (container is FrameworkElement templated) templated.DataContext = item;
            return;
        }
        if (container is Label label && item is not UIElement)
            label.Content = item;
    }

    protected internal virtual void ClearContainerForItemOverride(DependencyObject container, object item)
    {
        if (ItemTemplate is not null) return;
        if (container is Label label && item is not UIElement)
            label.Content = string.Empty;
    }

    protected virtual void OnItemsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
    }

    private void OnCollectionChanged()
    {
        if (_bulkUpdate) return;
        Regenerate();
        InvalidateMeasure();
    }

    private void Regenerate()
    {
        ItemContainerGenerator.Refresh();
        if (_itemsHost is not null)
        {
            base.RemoveChild(_itemsHost);
            _itemsHost = null;
        }
        if (ItemsPanel is not null)
        {
            var host = ItemsPanel.LoadContent();
            if (host is not Panel panel)
                throw new InvalidOperationException(
                    $"ItemsPanel content must be a Panel (have <{host.GetType().Name}>).");
            _itemsHost = panel;
            base.AddChild(panel);
        }
        for (var i = 0; i < Items.Count; i++) ItemContainerGenerator.ContainerFromIndex(i);
        OnItemsChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
            System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }

    internal void AddGeneratedChild(DependencyObject container)
    {
        if (container is not UIElement element) return;
        if (_itemsHost is not null) _itemsHost.AddChild(element);
        else base.AddChild(element);
    }

    internal void RemoveGeneratedChild(DependencyObject container)
    {
        if (container is not UIElement element) return;
        if (_itemsHost is not null) _itemsHost.RemoveChild(element);
        else base.RemoveChild(element);
    }

    /// <summary>XAML/code children become items (generated containers render, like real WPF).</summary>
    public new void AddChild(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        AddItem(child);
    }

    /// <summary>Adds a data item (UIElement items are their own containers).</summary>
    public virtual void AddItem(object item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Items.Add(item);
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var w = double.IsNaN(Width) ? available.Width : Width;
        var h = double.IsNaN(Height) ? available.Height : Height;
        if (Children.Count == 0) return new(0, 0);
        var width = 0.0;
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(new WpfSize(w, h));
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
        }
        return new(Math.Min(w, width), Math.Min(h, height));
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        var y = finalRect.Y;
        foreach (var child in Children)
        {
            var h = child.DesiredSize.Height;
            child.Arrange(new WpfRect(finalRect.X, y, finalRect.Width, h));
            y += h;
        }
    }

    /// <summary>Items paint chromeless (Border/Panel chrome belongs to
    /// templates): generated containers — or the items-host panel when
    /// <see cref="ItemsPanel"/> is set — render directly, like real WPF.
    /// Without this the control measured and arranged children that never
    /// painted (blank gallery sample lists).</summary>
    protected override void OnRender(DrawingContext dc)
    {
        foreach (var child in Children) UIElement.RenderVisible(dc, child);
    }
}

/// <summary>
/// Visual factory for ItemsControl.ItemTemplate (matches System.Windows.DataTemplate):
/// retains the template root markup; LoadContent inflates a fresh bound visual
/// per item. Template event wires and x:Name scopes have no codebehind owner
/// and stay a boundary (content inflates without them).
/// </summary>
public class DataTemplate : FrameworkTemplate
{
    private readonly string _visualXml;

    public DataTemplate(string visualXml)
    {
        ArgumentException.ThrowIfNullOrEmpty(visualXml);
        _visualXml = visualXml;
    }

    /// <summary>Optional DataType filter (stored; type-based selection stays a boundary).</summary>
    public string? DataType { get; set; }

    /// <summary>Data triggers retargeting named parts (parsed from
    /// DataTemplate.Triggers; evaluated per inflation, like WPF).</summary>
    public TriggerCollection Triggers { get; } = new();

    /// <summary>
    /// Lexical resources visible where the template was declared (captured
    /// at parse so inflated visuals resolve window-level keys, like WPF's
    /// apply-site lookup).
    /// </summary>
    public IReadOnlyList<FrameworkElement>? LoadScope { get; set; }

    /// <summary>Inflates one fresh visual from the retained markup (new namescope per call).</summary>
    public override UIElement LoadContent()
    {
        var loaded = XamlLoader.Load(_visualXml, LoadScope);
        TemplateApplier.ApplyDataTemplateTriggers(loaded.Root, loaded.Names, Triggers);
        return loaded.Root;
    }
}

/// <summary>
/// Hierarchical visual factory for tree ItemTemplate slots (matches
/// System.Windows.HierarchicalDataTemplate): a DataTemplate plus the child
/// items binding the tree expands.
/// </summary>
public sealed class HierarchicalDataTemplate : DataTemplate
{
    public HierarchicalDataTemplate(string visualXml)
        : base(visualXml)
    {
    }

    /// <summary>Child-items binding (parsed from ItemsSource="{Binding ...}").</summary>
    public Binding? ItemsSource { get; set; }
}

/// <summary>
/// Panel factory for ItemsControl.ItemsPanel (matches
/// System.Windows.ItemsPanelTemplate): retains the panel markup; LoadContent
/// builds the host panel the control parents generated containers into.
/// </summary>
public sealed class ItemsPanelTemplate
{
    private readonly string _visualXml;
    private readonly Panel? _panelRoot;

    public ItemsPanelTemplate(string visualXml)
    {
        ArgumentException.ThrowIfNullOrEmpty(visualXml);
        _visualXml = visualXml;
    }

    /// <summary>BAML-retained panel root (the BAML reader lands keyed
    /// ItemsPanelTemplate entries as live panels, e.g. the staged HandyControl
    /// ButtonGroup UniformGrid/StackPanel entries; no inflation markup rides
    /// along, like the BAML-retained ControlTemplate roots).</summary>
    internal ItemsPanelTemplate(Panel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        _visualXml = string.Empty;
        _panelRoot = panel;
    }

    /// <summary>Retained panel root (null on markup-built templates).</summary>
    public Panel? VisualRoot => _panelRoot;

    /// <summary>Builds the host panel (callers check for Panel). BAML-retained
    /// roots throw a precise no-inflation boundary instead of sharing one
    /// instance across controls, like the BAML-retained ControlTemplate
    /// roots.</summary>
    public UIElement LoadContent() => _panelRoot is not null
        ? throw new InvalidOperationException("ItemsPanelTemplate has a retained panel but no inflation markup (BAML-tree inflation is a precise follow-up).")
        : XamlLoader.Load(_visualXml).Root;
}

/// <summary>Legacy row-model template contract: ListBoxItem and ListViewItem
/// are interchangeable template targets here (native pairs satisfy this
/// through ListViewItem : ListBoxItem inheritance; the engine fork keeps
/// them as sibling Controls with string- versus object-Content, so the
/// template target check consults this marker).</summary>
internal interface IRowTemplateTarget;

/// <summary>Single-selection items host (matches System.Windows.Controls.Primitives.Selector).</summary>
public abstract class Selector : ItemsControl
{
    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register("SelectedIndex", typeof(int), typeof(Selector),
            new FrameworkPropertyMetadata(-1));
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register("SelectedItem", typeof(object), typeof(Selector),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register("SelectedValue", typeof(object), typeof(Selector),
            new FrameworkPropertyMetadata(null));
    public static readonly RoutedEvent SelectionChangedEvent = EventManager.RegisterRoutedEvent(
        "SelectionChanged", RoutingStrategy.Bubble, typeof(SelectionChangedEventHandler), typeof(Selector));

    private static readonly DependencyPropertyKey IsSelectionActivePropertyKey =
        DependencyProperty.RegisterAttachedReadOnly("IsSelectionActive", typeof(bool), typeof(Selector),
            new FrameworkPropertyMetadata(false));

    /// <summary>Selection-active flag (matches Selector.IsSelectionActiveProperty; read-only like WPF).</summary>
    public static readonly DependencyProperty IsSelectionActiveProperty = IsSelectionActivePropertyKey.DependencyProperty;

    /// <summary>Reads the selection-active flag (focus-driven in WPF; false in v1).</summary>
    public static bool GetIsSelectionActive(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsSelectionActiveProperty) ?? false);
    }

    private int _selectedIndex = -1;

    /// <summary>Selection changes (AddedItems/RemovedItems carry the effective change).</summary>
    public event SelectionChangedEventHandler? SelectionChanged;

    /// <summary>Selection behavior knob (v1 implements Single; Multiple/Extended parse and store).</summary>
    public SelectionMode SelectionMode { get; set; } = SelectionMode.Single;

    public static readonly DependencyProperty IsSynchronizedWithCurrentItemProperty =
        DependencyProperty.Register("IsSynchronizedWithCurrentItem", typeof(bool?), typeof(Selector),
            new FrameworkPropertyMetadata(null));

    /// <summary>Currency sync knob (matches Selector.IsSynchronizedWithCurrentItemProperty;
    /// nullable, null default like WPF; CollectionView coupling stays a boundary).</summary>
    public bool? IsSynchronizedWithCurrentItem
    {
        get => (bool?)GetValue(IsSynchronizedWithCurrentItemProperty);
        set => SetValue(IsSynchronizedWithCurrentItemProperty, value);
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value) return;
            var oldItem = EffectiveSelectedItem();
            _selectedIndex = value;
            var newItem = EffectiveSelectedItem();
            if (!ReferenceEquals(oldItem, newItem) || oldItem is null != (newItem is null))
                OnSelectionChanged(new SelectionChangedEventArgs(SelectionChangedEvent, AddedRemoved(oldItem), AddedRemoved(newItem))
                {
                    Source = this,
                    OriginalSource = this,
                });
            InvalidateVisual();
            // Slice-52 renderer-switch parity: an effective selection change
            // pushes back into TwoWay SelectedItem/SelectedIndex bindings
            // (the OxyPlot renderer ComboBox drives VM.Renderer this way);
            // the push skips when the source already holds the value.
            UpdateSources();
        }
    }

    public object? SelectedItem
    {
        get => EffectiveSelectedItem();
        set
        {
            if (value is null)
            {
                SelectedIndex = -1;
                return;
            }
            SelectedIndex = Items.IndexOf(value);
        }
    }

    public string? SelectedValuePath { get; set; }

    public object? SelectedValue
    {
        get
        {
            var item = EffectiveSelectedItem();
            if (item is null) return null;
            if (SelectedValuePath is null) return item;
            return BindingExpression.TryRead(item, SelectedValuePath, out var value) ? value : null;
        }
        set
        {
            if (value is null)
            {
                SelectedIndex = -1;
                return;
            }
            for (var i = 0; i < Items.Count; i++)
            {
                var candidate = SelectedValuePath is null
                    ? Items[i]
                    : BindingExpression.TryRead(Items[i], SelectedValuePath, out var v) ? v : null;
                if (Equals(candidate, value))
                {
                    SelectedIndex = i;
                    return;
                }
            }
            SelectedIndex = -1;
        }
    }

    /// <summary>
    /// Selection-change entry (matches System.Windows.Controls.Primitives.Selector.OnSelectionChanged;
    /// converted TabControlEx overrides it to rebuild its content presenter):
    /// carries the routed payload through the list-based worker so tab flags,
    /// bindings and routed handlers all run.
    /// </summary>
    protected virtual void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        OnSelectionChanged(e.RemovedItems, e.AddedItems);
    }

    protected virtual void OnSelectionChanged(IList removedItems, IList addedItems)
    {
        var args = new SelectionChangedEventArgs(SelectionChangedEvent, removedItems, addedItems)
        {
            Source = this,
            OriginalSource = this,
        };
        SelectionChanged?.Invoke(this, args);
        InvokeRoutedHandlers(SelectionChangedEvent, args);
        for (var i = 0; i < Items.Count; i++) SetRowSelected(Items[i], i == SelectedIndex);
    }

    /// <summary>Honors a pre-selected row added after its IsSelected flag was set.</summary>
    public override void AddItem(object item)
    {
        base.AddItem(item);
        if (IsRowSelected(item))
            SelectedItem = item;
    }

    /// <summary>
    /// Re-seats the selection from a bound SelectedItem data value after the
    /// items rebuild (WPF target-follows-source; skipped without a resolvable
    /// SelectedItem binding so programmatic indexes survive bulk adds).
    /// </summary>
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        SyncBoundSelectedItem();
    }

    private void SyncBoundSelectedItem()
    {
        var expression = ActiveBindings.FirstOrDefault(b => b.TargetProperty == "SelectedItem");
        if (expression?.Source is null) return;
        if (!BindingExpression.TryRead(expression.Source, expression.Binding.Path, out var data)) return;
        if (data is null) { SelectedIndex = -1; return; }
        var index = Items.IndexOf(data);
        if (index >= 0) SelectedIndex = index;
    }

    private static IList AddedRemoved(object? item) =>
        item is null ? new object[0] : new object[] { item };

    private static bool IsRowSelected(object item) =>
        item is ComboBoxItem comboRow ? comboRow.IsSelected :
        item is ListViewItem listRow && listRow.IsSelected;

    private static void SetRowSelected(object item, bool selected)
    {
        if (item is ComboBoxItem comboRow) comboRow.IsSelected = selected;
        else if (item is ListViewItem listRow) listRow.IsSelected = selected;
    }

    private object? EffectiveSelectedItem() =>
        _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;
}

/// <summary>Headered items host (matches System.Windows.Controls.HeaderedItemsControl).</summary>
public class HeaderedItemsControl : ItemsControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register("Header", typeof(object), typeof(HeaderedItemsControl),
            new FrameworkPropertyMetadata(null));

    private object? _header;

    /// <summary>
    /// Header content (stored; template-driven header rendering stays a boundary).
    /// Converted C# binds header access through this declaring type (real WPF
    /// inheritance puts MenuItem.Header and TreeViewItem.Header here), while
    /// the engine keeps per-control header storage on those siblings, so the
    /// accessor forwards to the live value instead of this slot's default
    /// (gallery MenuPage reads submenu headers this way).
    /// </summary>
    public object? Header
    {
        get
        {
            // MenuItem keeps its own hierarchy (sealed here), so the check
            // goes through object; TreeViewItem derives this type directly.
            if ((object)this is MenuItem menuItem) return menuItem.Header;
            if (this is TreeViewItem treeItem) return treeItem.Header;
            return _header;
        }
        set
        {
            if ((object)this is MenuItem menuItem) { menuItem.Header = value; return; }
            if (this is TreeViewItem treeItem) { treeItem.Header = value; return; }
            _header = value;
        }
    }
}

/// <summary>Selectable drop-down row (matches System.Windows.Controls.ComboBoxItem).</summary>
public class ComboBoxItem : HeaderedItemsControl
{
    private string? _content;
    private bool _isSelected;

    /// <summary>Row text (renders in the drop-down list and the header).</summary>
    public string? Content
    {
        get => _content;
        set
        {
            if (_content == value) return;
            _content = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Selection flag (two-way with the owning ComboBox: setting true selects
    /// this row when already parented; the owner syncs it on selection change).
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            InvalidateVisual();
            if (value && Parent is Selector selector && !ReferenceEquals(selector.SelectedItem, this))
                selector.SelectedItem = this;
        }
    }

    private bool _isHighlighted;

    /// <summary>
    /// Hover highlight (matches System.Windows.Controls.ComboBoxItem.IsHighlighted:
    /// true while the pointer hovers the row; the owning ComboBox drives it
    /// on pointer moves and clears it when the drop-down closes; the PC Space
    /// Map ComboBoxItem style triggers its hover/selected paints off it).
    /// </summary>
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (_isHighlighted == value) return;
            _isHighlighted = value;
            InvalidateVisual();
        }
    }
}

/// <summary>Drop-down list (matches System.Windows.Controls.ComboBox).</summary>
public class ComboBox : Selector
{
    private const double HeaderHeight = 30;
    private const double ChevronWidth = 24;

    /// <summary>Drop-down panel cap in pixels (default 200).</summary>
    public double MaxDropDownHeight { get; set; } = 200;

    private string _text = string.Empty;

    /// <summary>Editable text (displayed when IsEditable; otherwise the selection text shows).</summary>
    public string Text
    {
        get => IsEditable ? _text : DisplayText();
        set
        {
            if (_text == value) return;
            _text = value ?? string.Empty;
            InvalidateMeasure();
            InvalidateVisual();
            UpdateSources();
        }
    }

    public bool IsEditable { get; set; }

    private bool _isDropDownOpen;

    public bool IsDropDownOpen
    {
        get => _isDropDownOpen;
        set
        {
            if (_isDropDownOpen == value) return;
            _isDropDownOpen = value;
            if (!value) ClearHighlight();
            InvalidateVisual();
        }
    }

    /// <summary>Highlights the hovered drop-down row (none when closed or outside).</summary>
    internal void TrackHighlight(WpfPoint point)
    {
        var index = IsDropDownOpen && Inside(DropRect, point)
            ? (int)((point.Y - DropRect.Y) / 24)
            : -1;
        if ((uint)index >= (uint)Items.Count) index = -1;
        for (var i = 0; i < Children.Count; i++)
            if (Children[i] is ComboBoxItem row)
                row.IsHighlighted = i == index;
    }

    private void ClearHighlight()
    {
        foreach (var child in Children)
            if (child is ComboBoxItem row && row.IsHighlighted)
                row.IsHighlighted = false;
    }

    public override bool Focusable => true;

    private string DisplayText()
    {
        var item = SelectedItem;
        if (item is ComboBoxItem ci) return ci.Content ?? ci.Header?.ToString() ?? string.Empty;
        if (item is ContentControl cc) return cc.Content?.ToString() ?? string.Empty;
        if (item is UIElement) return item.GetType().Name;
        return item?.ToString() ?? string.Empty;
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var w = double.IsNaN(Width) ? available.Width : Width;
        var label = DisplayText();
        var (textW, textH) = TryMeasure(label, "14px sans-serif", Math.Max(0, label.Length * 8), 16);
        var width = Math.Max(120, textW + ChevronWidth + 16);
        return new(Math.Min(w, width), HeaderHeight);
    }

    private WpfRect HeaderRect => new(RenderRect.X, RenderRect.Y, RenderRect.Width, HeaderHeight);
    private WpfRect DropRect => new(RenderRect.X, RenderRect.Y + HeaderHeight, RenderRect.Width,
        Math.Min(Math.Max(0, MaxDropDownHeight), Children.Count * 24));

    private static bool Inside(WpfRect rect, WpfPoint point) =>
        point.X >= rect.X && point.X < rect.X + rect.Width &&
        point.Y >= rect.Y && point.Y < rect.Y + rect.Height;

    /// <summary>Hit-test covers the header plus the open drop-down panel.</summary>
    public override bool HitTest(WpfPoint point) =>
        Visibility == Visibility.Visible && (base.HitTest(point) || (IsDropDownOpen && Inside(DropRect, point)));

    protected override void OnRender(DrawingContext dc)
    {
        var header = HeaderRect;
        if (header.Width <= 0 || header.Height <= 0) return;
        var enabled = IsEffectivelyEnabled;
        dc.DrawRectangle((float)header.X, (float)header.Y, (float)header.Width, (float)header.Height,
            enabled ? 0xFFFFFFFFu : 0xF0F0F0FFu);
        dc.DrawRectangle((float)header.X, (float)header.Y, (float)header.Width, (float)header.Height, 0x707070FFu);
        var label = DisplayText();
        if (!string.IsNullOrEmpty(label))
            dc.DrawText(label, (float)header.X + 6, (float)(header.Y + header.Height / 2),
                "14px sans-serif", enabled ? 0x000000FFu : 0x707070FFu, 0, 2, -1);
        var cx = (float)(header.X + header.Width - ChevronWidth / 2);
        var cy = (float)(header.Y + header.Height / 2);
        dc.DrawLine(cx - 5, cy - 3, cx, cy + 3, 2, 0x000000FFu);
        dc.DrawLine(cx, cy + 3, cx + 5, cy - 3, 2, 0x000000FFu);
        if (!IsDropDownOpen) return;
        var drop = DropRect;
        if (drop.Width <= 0 || drop.Height <= 0) return;
        dc.DrawRectangle((float)drop.X, (float)drop.Y, (float)drop.Width, (float)drop.Height, 0xFFFFFFFFu);
        dc.DrawRectangle((float)drop.X, (float)drop.Y, (float)drop.Width, (float)drop.Height, 0x707070FFu);
        var y = drop.Y;
        for (var i = 0; i < Children.Count && y < drop.Y + drop.Height; i++)
        {
            var child = Children[i];
            if (i == SelectedIndex)
                dc.DrawRectangle((float)drop.X + 1, (float)y, (float)drop.Width - 2, 24, 0x3390FFFFu);
            var text = ItemText(child);
            if (!string.IsNullOrEmpty(text))
                dc.DrawText(text, (float)drop.X + 6, (float)(y + 12), "14px sans-serif", 0x000000FFu, 0, 2, -1);
            y += 24;
        }
    }

    private static string ItemText(UIElement child) =>
        child is ComboBoxItem ci ? ci.Content ?? ci.Header?.ToString() ?? string.Empty :
        child is ContentControl cc ? cc.Content?.ToString() ?? string.Empty : child.GetType().Name;

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        if (e.Name == WasmRoutedEvent.KeyDownName && e.Source is Core.WasmInputEvent.Key k && k.Args.IsDown)
        {
            switch (k.Args.Key)
            {
                case "Escape" when IsDropDownOpen:
                    IsDropDownOpen = false;
                    e.Handled = true;
                    return;
                case "ArrowDown":
                    SelectedIndex = Items.Count == 0 ? -1 : (SelectedIndex + 1) % Items.Count;
                    e.Handled = true;
                    return;
                case "ArrowUp":
                    SelectedIndex = Items.Count == 0 ? -1 : (SelectedIndex - 1 + Items.Count) % Items.Count;
                    e.Handled = true;
                    return;
            }
            return;
        }
        if (e.Name == WasmRoutedEvent.PointerMoveName &&
            e.Source is Core.WasmInputEvent.Pointer hover && hover.Args.Kind == Core.WasmPointerKind.Move)
        {
            TrackHighlight(new WpfPoint(hover.Args.X, hover.Args.Y));
            return;
        }
        if (e.Name != WasmRoutedEvent.PointerDownName || !e.IsLeftButtonDown) return;
        if (e.Source is not Core.WasmInputEvent.Pointer p) return;
        var at = new WpfPoint(p.Args.X, p.Args.Y);
        if (Inside(HeaderRect, at))
        {
            IsDropDownOpen = !IsDropDownOpen;
            e.Handled = true;
            return;
        }
        if (IsDropDownOpen && Inside(DropRect, at))
        {
            var index = (int)((at.Y - DropRect.Y) / 24);
            if ((uint)index < (uint)Items.Count)
            {
                SelectedIndex = index;
                IsDropDownOpen = false;
                e.Handled = true;
            }
        }
    }

}

/// <summary>Grid-less list (matches System.Windows.Controls.ListView).</summary>
public class ListView : Selector
{
    /// <summary>Identifies the View store (matches ListView.ViewProperty).</summary>
    public static readonly DependencyProperty ViewProperty =
        DependencyProperty.Register("View", typeof(ViewBase), typeof(ListView));

    /// <summary>Active view (null by default; GridView lands here from converted code).</summary>
    public ViewBase? View
    {
        get => (ViewBase?)GetValue(ViewProperty);
        set => SetValue(ViewProperty, value);
    }
}

/// <summary>ListView row (same v1 surface as <see cref="ComboBoxItem"/>).</summary>
public class ListViewItem : HeaderedItemsControl, IRowTemplateTarget
{
    private string? _content;
    private bool _isSelected;

    /// <summary>Row text (renders in the list).</summary>
    public string? Content
    {
        get => _content;
        set
        {
            if (_content == value) return;
            _content = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Selection flag (two-way with the owning ListView: setting true selects
    /// this row when already parented; the owner syncs it on selection change).
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            InvalidateVisual();
            if (value && Parent is Selector selector && !ReferenceEquals(selector.SelectedItem, this))
                selector.SelectedItem = this;
        }
    }
}
