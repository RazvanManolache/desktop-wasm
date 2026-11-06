using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace DesktopWasm.Wpf;

/// <summary>
/// Scroll-unit knob (matches System.Windows.Controls.ScrollUnit;
/// member values live-verified against the .NET 9 WindowsDesktop runtime:
/// Pixel = 0, Item = 1). Converted HandyControl PropertyItemsControl code
/// passes one of these into
/// <see cref="VirtualizingPanel.SetScrollUnit(DependencyObject, ScrollUnit)"/>.
/// </summary>
public enum ScrollUnit
{
    Pixel = 0,
    Item = 1,
}

/// <summary>
/// Virtualizing host contract (matches
/// System.Windows.Controls.VirtualizingPanel, abstract like real WPF):
/// the attached knobs converted HandyControl code sets on its property
/// grid (<c>SetIsVirtualizingWhenGrouping</c> /
/// <c>SetScrollUnit</c>, member-evidenced via Mono.Cecil on the upstream
/// net10.0-windows HandyControl.dll). The engine renders all rows, so the
/// knobs are retained state with real WPF defaults (Item / false,
/// live-verified against the .NET 9 runtime); grouping virtualization
/// stays a boundary like the grid's own EnableRowVirtualization hint.
/// </summary>
public abstract class VirtualizingPanel : Panel
{
    /// <summary>Owning container generator (matches VirtualizingPanel.ItemContainerGenerator).</summary>
    public ItemContainerGenerator? ItemContainerGenerator
    {
        get
        {
            if (ItemsControl.GetItemsOwner(this) is { } owner) return owner.ItemContainerGenerator;
            for (var node = (DependencyObject?)this; node is not null; node = (node as FrameworkElement)?.Parent)
                if (node is ListBox lb) return lb.ItemContainerGenerator;
            return null;
        }
    }

    /// <summary>
    /// Scroll-unit knob (matches
    /// System.Windows.Controls.VirtualizingPanel.ScrollUnitProperty;
    /// attached, default <see cref="ScrollUnit.Item"/> like WPF).
    /// </summary>
    public static readonly DependencyProperty ScrollUnitProperty =
        DependencyProperty.RegisterAttached("ScrollUnit", typeof(ScrollUnit), typeof(VirtualizingPanel),
            new FrameworkPropertyMetadata(ScrollUnit.Item));

    /// <summary>
    /// Grouping-virtualization knob (matches
    /// System.Windows.Controls.VirtualizingPanel.IsVirtualizingWhenGroupingProperty;
    /// attached, default false like WPF).
    /// </summary>
    public static readonly DependencyProperty IsVirtualizingWhenGroupingProperty =
        DependencyProperty.RegisterAttached("IsVirtualizingWhenGrouping", typeof(bool), typeof(VirtualizingPanel),
            new FrameworkPropertyMetadata(false));

    /// <summary>
    /// Virtualization gate (matches
    /// System.Windows.Controls.VirtualizingPanel.IsVirtualizingProperty;
    /// attached, default true like WPF — the engine renders all rows, so the
    /// knob is retained state theme triggers read).
    /// </summary>
    public static readonly DependencyProperty IsVirtualizingProperty =
        DependencyProperty.RegisterAttached("IsVirtualizing", typeof(bool), typeof(VirtualizingPanel),
            new FrameworkPropertyMetadata(true));

    /// <summary>Reads the virtualization gate (attached bag plus DP store, true when unset like WPF).</summary>
    public static bool GetIsVirtualizing(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement visual && visual.GetAttached("VirtualizingPanel.IsVirtualizing") is bool fromBag) return fromBag;
        if (element.HasLocalValue(IsVirtualizingProperty) &&
            element.GetValue(IsVirtualizingProperty) is bool fromStore) return fromStore;
        return true;
    }

    /// <summary>Sets the virtualization gate (DP store plus attached bag, like the Canvas offsets).</summary>
    public static void SetIsVirtualizing(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement visual) visual.SetAttached("VirtualizingPanel.IsVirtualizing", value);
        element.SetValue(IsVirtualizingProperty, value);
    }

    /// <summary>Reads the ScrollUnit knob (attached bag plus DP store, Item when unset like WPF).</summary>
    public static ScrollUnit GetScrollUnit(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement visual && visual.GetAttached("VirtualizingPanel.ScrollUnit") is ScrollUnit fromBag) return fromBag;
        if (element.HasLocalValue(ScrollUnitProperty) &&
            element.GetValue(ScrollUnitProperty) is ScrollUnit fromStore) return fromStore;
        return ScrollUnit.Item;
    }

    /// <summary>Sets the ScrollUnit knob (DP store plus attached bag, like the Canvas offsets).</summary>
    public static void SetScrollUnit(DependencyObject element, ScrollUnit value)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement visual) visual.SetAttached("VirtualizingPanel.ScrollUnit", value);
        element.SetValue(ScrollUnitProperty, value);
    }

    /// <summary>Reads the grouping-virtualization knob (attached bag plus DP store, false when unset like WPF).</summary>
    public static bool GetIsVirtualizingWhenGrouping(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement visual && visual.GetAttached("VirtualizingPanel.IsVirtualizingWhenGrouping") is bool fromBag) return fromBag;
        if (element.HasLocalValue(IsVirtualizingWhenGroupingProperty) &&
            element.GetValue(IsVirtualizingWhenGroupingProperty) is bool fromStore) return fromStore;
        return false;
    }

    /// <summary>Sets the grouping-virtualization knob (DP store plus attached bag, like the Canvas offsets).</summary>
    public static void SetIsVirtualizingWhenGrouping(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement visual) visual.SetAttached("VirtualizingPanel.IsVirtualizingWhenGrouping", value);
        element.SetValue(IsVirtualizingWhenGroupingProperty, value);
    }

    /// <summary>Virtualization mode knob (matches System.Windows.Controls.VirtualizingPanel.VirtualizationModeProperty).</summary>
    public static readonly DependencyProperty VirtualizationModeProperty =
        VirtualizingStackPanel.VirtualizationModeProperty;

    /// <summary>Reads the virtualization mode (attached bag plus DP store, Standard default like WPF).</summary>
    public static VirtualizationMode GetVirtualizationMode(DependencyObject element) =>
        VirtualizingStackPanel.GetVirtualizationMode(element);

    /// <summary>Writes the virtualization mode (matches SetVirtualizationMode).</summary>
    public static void SetVirtualizationMode(DependencyObject element, VirtualizationMode value) =>
        VirtualizingStackPanel.SetVirtualizationMode(element, value);
}

/// <summary>
/// Multi-selection items host (matches
/// System.Windows.Controls.Primitives.MultiSelector, abstract like real
/// WPF): the <see cref="Selector"/> single selection plus a live
/// <see cref="SelectedItems"/> set with real select-all/clear behavior.
/// Converted HandyControl SelectAllButtonAttach code probes selectors with
/// <c>isinst MultiSelector</c> before calling
/// <c>SelectAll/UnselectAll/get_SelectedItems</c> (member-evidenced via
/// Mono.Cecil on the upstream net10.0-windows HandyControl.dll).
/// </summary>
public abstract class MultiSelector : Selector
{
    private readonly ObservableCollection<object?> _selectedItems = new();
    private bool _syncing;

    protected MultiSelector()
    {
        _selectedItems.CollectionChanged += OnSelectedItemsChanged;
    }

    /// <summary>
    /// Selected data items (matches
    /// System.Windows.Controls.Primitives.MultiSelector.SelectedItems):
    /// live set, mirroring the single selection until
    /// <see cref="SelectAll"/> fills it. External edits re-seat
    /// <see cref="Selector.SelectedIndex"/> on the first listed item.
    /// </summary>
    public IList SelectedItems => _selectedItems;

    /// <summary>
    /// Multiple-selection gate (matches
    /// System.Windows.Controls.Primitives.MultiSelector.CanSelectMultipleItems;
    /// protected like real WPF, default true).
    /// </summary>
    protected virtual bool CanSelectMultipleItems => true;

    /// <summary>
    /// Selects every item (matches
    /// System.Windows.Controls.Primitives.MultiSelector.SelectAll;
    /// non-virtual like real WPF): the single selection moves to the first
    /// item and the set fills with the full items list.
    /// </summary>
    public void SelectAll()
    {
        if (Items.Count == 0)
        {
            SelectedIndex = -1;
            return;
        }
        SelectedIndex = 0;
        _syncing = true;
        try
        {
            _selectedItems.Clear();
            foreach (var item in Items) _selectedItems.Add(item);
        }
        finally
        {
            _syncing = false;
        }
        InvalidateVisual();
    }

    /// <summary>
    /// Clears the selection (matches
    /// System.Windows.Controls.Primitives.MultiSelector.UnselectAll;
    /// non-virtual like real WPF).
    /// </summary>
    public void UnselectAll()
    {
        _syncing = true;
        try
        {
            _selectedItems.Clear();
        }
        finally
        {
            _syncing = false;
        }
        SelectedIndex = -1;
        InvalidateVisual();
    }

    /// <summary>Keeps the set behind the single selection (base still fires the typed event).</summary>
    protected override void OnSelectionChanged(IList removedItems, IList addedItems)
    {
        base.OnSelectionChanged(removedItems, addedItems);
        if (_syncing) return;
        _syncing = true;
        try
        {
            _selectedItems.Clear();
            if (SelectedItem is not null) _selectedItems.Add(SelectedItem);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnSelectedItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            SelectedIndex = _selectedItems.Count == 0 ? -1 : Items.IndexOf(_selectedItems[0]);
        }
        finally
        {
            _syncing = false;
        }
    }
}

/// <summary>
/// Row-container payload (matches
/// System.Windows.Controls.DataGridRowEventArgs): converted HandyControl
/// DataGridAttach code reads <see cref="Row"/> in its LoadingRow handler
/// (member-evidenced via Mono.Cecil on the upstream net10.0-windows
/// HandyControl.dll). The engine raises it from
/// <see cref="DataGrid.LoadingRow"/> when a row container is prepared.
/// </summary>
public class DataGridRowEventArgs : EventArgs
{
    public DataGridRowEventArgs(DataGridRow row)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
    }

    /// <summary>Realized row container.</summary>
    public DataGridRow Row { get; }
}

/// <summary>
/// Column payload (matches
/// System.Windows.Controls.DataGridColumnEventArgs): converted HandyControl
/// DataGridAttach code reads <see cref="Column"/> in its Sorting handler
/// (member-evidenced via Mono.Cecil on the upstream net10.0-windows
/// HandyControl.dll).
/// </summary>
public class DataGridColumnEventArgs : EventArgs
{
    public DataGridColumnEventArgs(DataGridColumn column)
    {
        Column = column ?? throw new ArgumentNullException(nameof(column));
    }

    /// <summary>Column the event applies to.</summary>
    public DataGridColumn Column { get; }
}

/// <summary>
/// Sort-request payload (matches
/// System.Windows.Controls.DataGridSortingEventArgs): converted HandyControl
/// DataGridAttach code sets <see cref="Handled"/> in its Sorting handler
/// (member-evidenced via Mono.Cecil on the upstream net10.0-windows
/// HandyControl.dll). An unhandled request falls back to the engine default
/// sort (toggle the column glyph plus its sort description, like WPF).
/// </summary>
public class DataGridSortingEventArgs : DataGridColumnEventArgs
{
    public DataGridSortingEventArgs(DataGridColumn column)
        : base(column)
    {
    }

    /// <summary>
    /// Custom handling marker (default false like WPF): a handler that sorts
    /// itself sets this, which skips the engine default sort.
    /// </summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Sort-request handler shape (matches
/// System.Windows.Controls.DataGridSortingEventHandler): converted
/// HandyControl DataGridAttach code constructs it over
/// DataGridOnSorting and adds/removes it on <see cref="DataGrid.Sorting"/>
/// (member-evidenced via Mono.Cecil on the upstream net10.0-windows
/// HandyControl.dll).
/// </summary>
public delegate void DataGridSortingEventHandler(object sender, DataGridSortingEventArgs e);

/// <summary>Edit scope (matches System.Windows.Controls.DataGridEditingUnit;
/// member values verified against the .NET 9 WindowsDesktop reference
/// assemblies: Cell = 0, Row = 1).</summary>
public enum DataGridEditingUnit
{
    Cell = 0,
    Row = 1,
}

/// <summary>Edit outcome (matches System.Windows.Controls.DataGridEditAction;
/// member values verified against the .NET 9 WindowsDesktop reference
/// assemblies: Commit = 0, Cancel = 1).</summary>
public enum DataGridEditAction
{
    Commit = 0,
    Cancel = 1,
}

/// <summary>
/// Edit-start payload (matches
/// System.Windows.Controls.DataGridBeginningEditEventArgs): cancellable.
/// A handler that sets <see cref="CancelEventArgs.Cancel"/> keeps the cell
/// in display mode (the grid reports false from <c>BeginEdit</c>).
/// </summary>
public class DataGridBeginningEditEventArgs : CancelEventArgs
{
    public DataGridBeginningEditEventArgs(DataGridColumn column, DataGridRow row, FrameworkElement editingElement)
    {
        Column = column ?? throw new ArgumentNullException(nameof(column));
        Row = row ?? throw new ArgumentNullException(nameof(row));
        EditingElement = editingElement ?? throw new ArgumentNullException(nameof(editingElement));
    }

    /// <summary>Column entering edit mode.</summary>
    public DataGridColumn Column { get; }

    /// <summary>Row entering edit mode.</summary>
    public DataGridRow Row { get; }

    /// <summary>Editor that would replace the display element.</summary>
    public FrameworkElement EditingElement { get; }
}

/// <summary>
/// Cell edit-end payload (matches
/// System.Windows.Controls.DataGridCellEditEndingEventArgs): cancellable.
/// Cancelling a commit aborts the model push and keeps the editor open;
/// cancelling a cancel keeps the editor open with the typed text intact.
/// </summary>
public class DataGridCellEditEndingEventArgs : CancelEventArgs
{
    public DataGridCellEditEndingEventArgs(DataGridColumn column, DataGridRow row, FrameworkElement editingElement, DataGridEditAction editAction)
    {
        Column = column ?? throw new ArgumentNullException(nameof(column));
        Row = row ?? throw new ArgumentNullException(nameof(row));
        EditingElement = editingElement ?? throw new ArgumentNullException(nameof(editingElement));
        EditAction = editAction;
    }

    /// <summary>Column leaving edit mode.</summary>
    public DataGridColumn Column { get; }

    /// <summary>Row leaving edit mode.</summary>
    public DataGridRow Row { get; }

    /// <summary>Editor being committed or discarded.</summary>
    public FrameworkElement EditingElement { get; }

    /// <summary>Whether the edit is committed or discarded.</summary>
    public DataGridEditAction EditAction { get; }
}

/// <summary>
/// Row edit-end payload (matches
/// System.Windows.Controls.DataGridRowEditEndingEventArgs): cancellable.
/// Raised after the cell push on commit and after the restore on cancel.
/// </summary>
public class DataGridRowEditEndingEventArgs : CancelEventArgs
{
    public DataGridRowEditEndingEventArgs(DataGridRow row, DataGridEditAction editAction)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        EditAction = editAction;
    }

    /// <summary>Row leaving edit mode.</summary>
    public DataGridRow Row { get; }

    /// <summary>Whether the edit is committed or discarded.</summary>
    public DataGridEditAction EditAction { get; }
}

/// <summary>Edit-start handler shape (matches
/// System.Windows.Controls.DataGridBeginningEditEventHandler).</summary>
public delegate void DataGridBeginningEditEventHandler(object sender, DataGridBeginningEditEventArgs e);

/// <summary>Cell edit-end handler shape (matches
/// System.Windows.Controls.DataGridCellEditEndingEventHandler).</summary>
public delegate void DataGridCellEditEndingEventHandler(object sender, DataGridCellEditEndingEventArgs e);

/// <summary>Row edit-end handler shape (matches
/// System.Windows.Controls.DataGridRowEditEndingEventHandler).</summary>
public delegate void DataGridRowEditEndingEventHandler(object sender, DataGridRowEditEndingEventArgs e);
