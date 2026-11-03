using System.Collections.ObjectModel;
using System.ComponentModel;

namespace DesktopWasm.Wpf;

/// <summary>Sort direction (matches System.ComponentModel.ListSortDirection: Ascending = 0, Descending = 1).</summary>
public enum ListSortDirection
{
    Ascending,
    Descending,
}

/// <summary>Visible gridlines (matches System.Windows.Controls.DataGridGridLinesVisibility;
/// member values verified against the .NET 9 WindowsDesktop reference assemblies).</summary>
public enum DataGridGridLinesVisibility
{
    All = 0,
    Horizontal = 1,
    None = 2,
    Vertical = 3,
}

/// <summary>Visible headers (matches System.Windows.Controls.DataGridHeadersVisibility;
/// member values verified against the .NET 9 WindowsDesktop reference assemblies.
/// The row-header column itself stays unpainted, like the engine's header band.)</summary>
public enum DataGridHeadersVisibility
{
    None = 0,
    Column = 1,
    Row = 2,
    All = 3,
}

/// <summary>Selection cardinality (matches System.Windows.Controls.DataGridSelectionMode;
/// member values verified against the .NET 9 WindowsDesktop reference assemblies.
/// The engine selects single rows only; Extended stays a retained boundary.)</summary>
public enum DataGridSelectionMode
{
    Single = 0,
    Extended = 1,
}

/// <summary>Details visibility mode (matches
/// System.Windows.Controls.DataGridRowDetailsVisibilityMode; member order
/// verified against live WPF: Collapsed/Visible/VisibleWhenSelected).</summary>
public enum DataGridRowDetailsVisibilityMode
{
    Collapsed = 0,
    Visible = 1,
    VisibleWhenSelected = 2,
}

/// <summary>Selection granularity (matches System.Windows.Controls.DataGridSelectionUnit;
/// member values verified against the .NET 9 WindowsDesktop reference assemblies.
/// The engine selects full rows only; Cell/CellOrRowHeader stay retained boundaries.)</summary>
public enum DataGridSelectionUnit
{
    Cell = 0,
    FullRow = 1,
    CellOrRowHeader = 2,
}

/// <summary>
/// Sort rule (matches System.ComponentModel.SortDescription): a property
/// name plus a direction. Converted ScreenToGif code builds these against
/// DataGrid item collections; the grid applies them to its rendered view.
/// </summary>
public readonly struct SortDescription : IEquatable<SortDescription>
{
    public SortDescription(string? propertyName, ListSortDirection direction)
    {
        PropertyName = propertyName ?? string.Empty;
        Direction = direction;
    }

    public string PropertyName { get; }

    public ListSortDirection Direction { get; }

    public static SortDescription Empty { get; } = new(string.Empty, ListSortDirection.Ascending);

    public bool Equals(SortDescription other) =>
        Direction == other.Direction && string.Equals(PropertyName, other.PropertyName, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SortDescription other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(PropertyName, (int)Direction);

    public override string ToString() => $"{PropertyName} {Direction}";

    public static bool operator ==(SortDescription left, SortDescription right) => left.Equals(right);

    public static bool operator !=(SortDescription left, SortDescription right) => !left.Equals(right);
}

/// <summary>Live sort rules (matches System.ComponentModel.SortDescriptionCollection).</summary>
public sealed class SortDescriptionCollection : ObservableCollection<SortDescription>
{
}

/// <summary>
/// Item list with WPF sorting/grouping state (matches
/// System.Windows.Controls.ItemCollection): converted ScreenToGif code
/// reaches <see cref="SortDescriptions"/> through
/// <c>DataGrid.Items.SortDescriptions</c>. Group rules are stored like
/// <see cref="CollectionView.GroupDescriptions"/>; sorting is applied by
/// the owning grid view, grouping stays a stored boundary.
/// </summary>
public sealed class DataGridItemCollection : ObservableCollection<object?>
{
    public DataGridItemCollection()
    {
    }

    /// <summary>Direct-edit gate (the owning grid forbids direct edits while
    /// ItemsSource is set, like WPF; null allows everything).</summary>
    internal Func<bool>? IsDirectEditAllowed { get; set; }

    private void RequireDirectEdit()
    {
        if (IsDirectEditAllowed is not null && !IsDirectEditAllowed())
            throw new InvalidOperationException(
                "Operation is not valid while ItemsSource is in use. Modify the source collection instead.");
    }

    protected override void InsertItem(int index, object? item)
    {
        RequireDirectEdit();
        base.InsertItem(index, item);
    }

    protected override void SetItem(int index, object? item)
    {
        RequireDirectEdit();
        base.SetItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        RequireDirectEdit();
        base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        RequireDirectEdit();
        base.ClearItems();
    }

    protected override void MoveItem(int oldIndex, int newIndex)
    {
        RequireDirectEdit();
        base.MoveItem(oldIndex, newIndex);
    }

    /// <summary>Active sort rules (a change re-sorts the owning grid view).</summary>
    public SortDescriptionCollection SortDescriptions { get; } = new();

    /// <summary>Group rules (stored; flat rendering keeps them as state).</summary>
    public ObservableCollection<GroupDescription> GroupDescriptions { get; } = new();
}

/// <summary>
/// Column contract (matches System.Windows.Controls.DataGridColumn):
/// columns are DependencyObjects, not visuals. The engine keeps the v1
/// GridLength width domain (pinned by DataGrid/XAML/BAML tests) instead of
/// the WPF DataGridLength struct; sort/display/cell-content members follow
/// the real shapes converted ScreenToGif code binds against.
/// </summary>
public abstract class DataGridColumn : DependencyObject
{
    private DataGrid? _owner;

    internal DataGrid? OwnerGrid => _owner;

    internal void SetOwner(DataGrid? owner)
    {
        if (ReferenceEquals(_owner, owner)) return;
        _owner = owner;
        OnColumnChanged();
    }

    private string _header = string.Empty;

    /// <summary>Column header text (paint-only change).</summary>
    public string Header
    {
        get => _header;
        set
        {
            if (_header == value) return;
            _header = value;
            OnColumnChanged();
        }
    }

    private GridLength _width = GridLength.Auto;

    /// <summary>Column width: pixel, star-weighted, or auto (changes layout).</summary>
    public GridLength Width
    {
        get => _width;
        set
        {
            if (_width == value) return;
            _width = value;
            _owner?.InvalidateMeasure();
        }
    }

    private ListSortDirection? _sortDirection;

    /// <summary>
    /// Active sort glyph direction (null = unsorted). Setting it marks the
    /// grid for repaint; it does not add a <see cref="SortDescription"/>
    /// by itself, like real WPF (callers add the description, as
    /// ScreenToGif's DataGridHelper.Sort does).
    /// </summary>
    public ListSortDirection? SortDirection
    {
        get => _sortDirection;
        set
        {
            if (_sortDirection == value) return;
            _sortDirection = value;
            _owner?.InvalidateVisual();
        }
    }

    private string _sortMemberPath = string.Empty;

    /// <summary>Property path the column sorts by (stored; used with SortDirection).</summary>
    public string SortMemberPath
    {
        get => _sortMemberPath;
        set
        {
            if (_sortMemberPath == value) return;
            _sortMemberPath = value ?? string.Empty;
            OnColumnChanged();
        }
    }

    /// <summary>Position in the owning grid's Columns (set by the grid).</summary>
    public int DisplayIndex { get; internal set; } = -1;

    /// <summary>Notifies the owner that layout-affecting column state changed.</summary>
    protected void OnColumnChanged() => _owner?.InvalidateMeasure();

    /// <summary>
    /// Cell content element for a row item (matches
    /// DataGridColumn.GetCellContent): the realized cell's content, whose
    /// <see cref="UIElement.Parent"/> is the cell, so helpers like
    /// <c>cellContent?.Parent as DataGridCell</c> resolve. Null with no
    /// owning grid or unknown item.
    /// </summary>
    public virtual FrameworkElement? GetCellContent(object? item)
    {
        var owner = _owner;
        if (owner is null || item is null) return null;
        // Unknown items have no cell (the generator would mint containers
        // on demand, but real GetCellContent answers null off-grid).
        if (owner.ViewIndexOf(item) < 0) return null;
        var row = owner.ContainerForItem(item) as DataGridRow;
        var cell = row?.CellsPresenter.ContainerForColumn(this) as DataGridCell;
        return cell?.EnsureContent(item);
    }

    /// <summary>Builds the display element for a cell (matches the protected GenerateElement).</summary>
    protected internal abstract FrameworkElement? GenerateElement(DataGridCell cell, object? dataItem);

    /// <summary>Builds the editing element for a cell (matches the protected GenerateEditingElement).</summary>
    protected internal abstract FrameworkElement? GenerateEditingElement(DataGridCell cell, object? dataItem);

    /// <summary>
    /// Abandons an edit (matches DataGridColumn.CancelCellEdit; the default
    /// restores nothing because the base holds no editor state).
    /// </summary>
    protected internal virtual void CancelCellEdit(FrameworkElement editingElement, object uneditedValue)
    {
    }

    /// <summary>
    /// Commits an edit (matches DataGridColumn.CommitCellEdit; the default
    /// accepts because the base holds no editor state to validate).
    /// </summary>
    protected internal virtual bool CommitCellEdit(FrameworkElement editingElement) => true;
}

/// <summary>
/// Bound column (matches System.Windows.Controls.DataGridBoundColumn):
/// carries the <see cref="Binding"/> converted code reads back (Sort and
/// ReSort match columns by <c>Binding.Path.Path</c>).
/// </summary>
public abstract class DataGridBoundColumn : DataGridColumn
{
    private Binding? _binding;

    /// <summary>Value binding (null until set; XAML BindingPath stays an engine extension).</summary>
    public Binding? Binding
    {
        get => _binding;
        set
        {
            if (ReferenceEquals(_binding, value)) return;
            _binding = value;
            OnColumnChanged();
        }
    }
}

/// <summary>
/// Row container (matches System.Windows.Controls.DataGridRow): hosts the
/// cells presenter plus the details presenter as logical children, so
/// <c>VisualHelper.GetVisualChild&lt;DataGridCellsPresenter&gt;(row)</c> and
/// the details-presenter lookup in converted ScreenToGif code resolve.
/// Rows are created by the grid's item-container generator and stay
/// unparented from the grid's own visual tree (the grid paints itself).
/// </summary>
public sealed class DataGridRow : ContentControl
{
    static DataGridRow()
    {
        // Grid rows never take keyboard focus (live-verified WPF default).
        FocusableProperty.OverrideMetadata(typeof(DataGridRow), new FrameworkPropertyMetadata(false));
    }

    private readonly DataGrid? _owner;

    internal DataGridRow(DataGrid owner)
        : this()
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        // Position-preserving replace: generated rows keep the prototype
        // order (cells presenter before details presenter, like native).
        var template = TemplateCellsPresenter;
        var index = template is null ? -1 : ChildList.IndexOf(template);
        if (template is not null) RemoveChild(template);
        CellsPresenter = new DataGridCellsPresenter(owner, this);
        if (index < 0 || index > ChildList.Count) AddChild(CellsPresenter);
        else InsertChildAt(index, CellsPresenter);
    }

    /// <summary>Template-part row (ownerless prototype for theme templates;
    /// generated rows always carry the owning grid).</summary>
    internal DataGridRow()
    {
        TemplateCellsPresenter = new DataGridCellsPresenter();
        CellsPresenter = TemplateCellsPresenter;
        DetailsPresenter = new DataGridDetailsPresenter();
        AddChild(CellsPresenter);
        AddChild(DetailsPresenter);
    }

    private DataGridCellsPresenter? TemplateCellsPresenter { get; set; }

    /// <summary>Owning grid (null for template-part prototypes, never null
    /// for generated rows).</summary>
    public DataGrid? OwnerGrid => _owner;

    private object? _item;

    /// <summary>Row data item (set by the grid generator).</summary>
    public object? Item
    {
        get => _item;
        internal set
        {
            if (ReferenceEquals(_item, value)) return;
            _item = value;
            SyncSelection();
        }
    }

    private bool _isSelected;

    /// <summary>Selection flag (two-way with the owning grid).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            if (_owner is null) return;
            if (value && !ReferenceEquals(_owner.SelectedItem, _item)) _owner.SelectedItem = _item;
            else if (!value && ReferenceEquals(_owner.SelectedItem, _item)) _owner.SelectedItem = null;
        }
    }

    internal void SyncSelection() => _isSelected = _owner is not null && _owner.SelectedItem is not null && ReferenceEquals(_owner.SelectedItem, _item);

    private Visibility _detailsVisibility = Visibility.Collapsed;

    /// <summary>Details-pane visibility (stored; details render through the presenter content).</summary>
    public Visibility DetailsVisibility
    {
        get => _detailsVisibility;
        set
        {
            if (_detailsVisibility == value) return;
            _detailsVisibility = value;
            InvalidateMeasure();
        }
    }

    private object? _header;

    /// <summary>Row header content (stored).</summary>
    public object? Header
    {
        get => _header;
        set
        {
            if (Equals(_header, value)) return;
            _header = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Cells host (per-row, synced to the grid columns).</summary>
    public DataGridCellsPresenter CellsPresenter { get; }

    /// <summary>Details host (per-row).</summary>
    public DataGridDetailsPresenter DetailsPresenter { get; }

    /// <summary>View index of this row in the owning grid (-1 when unknown).</summary>
    public int GetIndex() => _owner?.ViewIndexOf(_item) ?? -1;

    /// <summary>Row validation template token (matches
    /// DataGridRow.ValidationErrorTemplateProperty; null default, like WPF).</summary>
    public static readonly DependencyProperty ValidationErrorTemplateProperty =
        DependencyProperty.Register("ValidationErrorTemplate", typeof(ControlTemplate), typeof(DataGridRow),
            new FrameworkPropertyMetadata(null));

    /// <summary>Row validation template (null default, like WPF).</summary>
    public ControlTemplate? ValidationErrorTemplate
    {
        get => (ControlTemplate?)GetValue(ValidationErrorTemplateProperty);
        set => SetValue(ValidationErrorTemplateProperty, value);
    }

    /// <summary>New-item placeholder token (matches
    /// DataGridRow.IsNewItemProperty; false default, like WPF).</summary>
    public static readonly DependencyProperty IsNewItemProperty =
        DependencyProperty.Register("IsNewItem", typeof(bool), typeof(DataGridRow),
            new FrameworkPropertyMetadata(false));

    /// <summary>Whether this row is the new-item placeholder (false until
    /// placeholder generation lands; the DataGrid theme keys its
    /// placeholder visuals off this, like WPF).</summary>
    public bool IsNewItem
    {
        get => (bool)(GetValue(IsNewItemProperty) ?? false);
        set => SetValue(IsNewItemProperty, value);
    }
}

/// <summary>
/// Per-row cells host (matches
/// System.Windows.Controls.Primitives.DataGridCellsPresenter): an items
/// control over the grid columns whose generator hands out
/// <see cref="DataGridCell"/> containers, so
/// <c>presenter.ItemContainerGenerator.ContainerFromIndex(column)</c> in
/// converted ScreenToGif code resolves.
/// </summary>
public sealed class DataGridCellsPresenter : ItemsControl
{
    private readonly DataGrid? _owner;
    private readonly DataGridRow? _row;

    internal DataGridCellsPresenter(DataGrid owner, DataGridRow row)
        : this()
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _row = row ?? throw new ArgumentNullException(nameof(row));
    }

    /// <summary>Template-part presenter (ownerless prototype for theme
    /// templates; generated presenters always carry owner and row).</summary>
    internal DataGridCellsPresenter()
    {
    }

    /// <summary>Owning grid (null for template-part prototypes).</summary>
    public DataGrid? OwnerGrid => _owner;

    /// <summary>Row item these cells render (null for template-part prototypes).</summary>
    public object? RowItem => _row?.Item;

    internal void SyncColumns(IReadOnlyList<DataGridTextColumn> columns)
    {
        Items.Clear();
        foreach (var column in columns) Items.Add(column);
    }

    /// <summary>Realized cell for a grid column (null when out of range).</summary>
    internal DataGridCell? ContainerForColumn(DataGridColumn column)
    {
        var index = Items.IndexOf(column);
        return index < 0 ? null : ItemContainerGenerator.ContainerFromIndex(index) as DataGridCell;
    }

    protected internal override DependencyObject GetContainerForItemOverride() => new DataGridCell();

    protected internal override bool IsItemItsOwnContainerOverride(object item) => item is DataGridCell;

    protected internal override void PrepareContainerForItemOverride(DependencyObject container, object item)
    {
        if (container is DataGridCell cell)
        {
            cell.Column = item as DataGridColumn;
            cell.SyncSelection(_row?.IsSelected == true);
        }
    }
}

/// <summary>
/// Row details host (matches
/// System.Windows.Controls.Primitives.DataGridDetailsPresenter): a plain
/// content host converted ScreenToGif code looks up per row.
/// </summary>
/// <summary>
/// Column-headers host (matches
/// System.Windows.Controls.Primitives.DataGridColumnHeadersPresenter): an
/// items host for the header band, like WPF (the MaterialDesign DataGrid
/// template carries one).
/// </summary>
public class DataGridColumnHeadersPresenter : ItemsControl
{
}

/// <summary>
/// Rows host (matches
/// System.Windows.Controls.Primitives.DataGridRowsPresenter): the row band
/// panel, like WPF (the MaterialDesign DataGrid template carries one; the
/// engine panel does not virtualize, like VirtualizingStackPanel).
/// </summary>
public class DataGridRowsPresenter : VirtualizingStackPanel
{
}

public class DataGridDetailsPresenter : ContentControl
{
}

/// <summary>
/// Grid cell (matches System.Windows.Controls.DataGridCell): a content
/// control carrying its column. Content parents back to the cell, so
/// <c>cellContent?.Parent as DataGridCell</c> in converted ScreenToGif
/// code resolves.
/// </summary>
public class DataGridCell : ContentControl
{
    private DataGridColumn? _column;

    /// <summary>Owning column (set by the row's cells presenter).</summary>
    public DataGridColumn? Column
    {
        get => _column;
        internal set
        {
            if (ReferenceEquals(_column, value)) return;
            _column = value;
            InvalidateMeasure();
        }
    }

    private bool _isSelected;

    /// <summary>Cell selection flag (synced from the owning row).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            InvalidateVisual();
        }
    }

    private bool _isEditing;

    /// <summary>Editing flag (stored; in-place editors stay a boundary).</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value) return;
            _isEditing = value;
            InvalidateVisual();
        }
    }

    internal void SyncSelection(bool rowSelected) => IsSelected = rowSelected;

    /// <summary>
    /// Realized content for a row item: the column's generated element,
    /// parented back to this cell. Reuses existing content when already a
    /// FrameworkElement.
    /// </summary>
    internal FrameworkElement? EnsureContent(object? item)
    {
        if (Content is FrameworkElement existing)
        {
            ParentToCell(existing);
            return existing;
        }
        var generated = _column?.GenerateElement(this, item);
        if (generated is null) return null;
        Content = generated;
        ParentToCell(generated);
        InvalidateMeasure();
        return generated;
    }

    private void ParentToCell(FrameworkElement element)
    {
        // FrameworkElement.Parent is a get-only real-WPF-shaped view over
        // the stored UIElement.Parent link; the internal setter lives
        // there and is reachable from inside the engine assembly.
        if (!ReferenceEquals(element.Parent, this) && element is UIElement visual) visual.Parent = this;
    }
}

/// <summary>
/// Column header (matches System.Windows.Controls.Primitives.DataGridColumnHeader):
/// a button-base chrome host for the header band. The PC Space Map DataGrid
/// style lands its header brushes/padding/font on it. A press on the header
/// band raises DataGrid.Sorting for the covering column (an unhandled
/// request runs the engine default sort through SortDescriptions).
/// </summary>
public class DataGridColumnHeader : ButtonBase
{
    static DataGridColumnHeader()
    {
        // Column headers never take keyboard focus (live-verified WPF default).
        FocusableProperty.OverrideMetadata(typeof(DataGridColumnHeader), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Sort-direction token (matches
    /// DataGridColumnHeader.SortDirectionProperty; null = unsorted, like WPF).</summary>
    public static readonly DependencyProperty SortDirectionProperty =
        DependencyProperty.Register("SortDirection", typeof(ListSortDirection?), typeof(DataGridColumnHeader),
            new FrameworkPropertyMetadata(null));

    /// <summary>Active sort glyph direction (null = unsorted, like the column).</summary>
    public ListSortDirection? SortDirection
    {
        get => (ListSortDirection?)GetValue(SortDirectionProperty);
        set => SetValue(SortDirectionProperty, value);
    }
}

/// <summary>
/// Row header (matches System.Windows.Controls.Primitives.DataGridRowHeader):
/// a button-base chrome host for the row-header band, like the column
/// header (the MaterialDesign DataGrid theme styles both bands).
/// </summary>
public class DataGridRowHeader : ButtonBase
{
}

/// <summary>Selective scrolling axes (matches
/// System.Windows.Controls.Primitives.SelectiveScrollingOrientation:
/// None/Horizontal/Vertical/Both).</summary>
public enum SelectiveScrollingOrientation
{
    None,
    Horizontal,
    Vertical,
    Both,
}

/// <summary>
/// Selective scrolling grid (matches
/// System.Windows.Controls.Primitives.SelectiveScrollingGrid): a Grid whose
/// selective children scroll with the content while headers stay put. The
/// orientation rides the attached store (Both default, like WPF); layout
/// follows the Grid engine, like other theme panels.
/// </summary>
public class SelectiveScrollingGrid : Grid
{
    /// <summary>Scrolling-axis token (matches
    /// SelectiveScrollingGrid.SelectiveScrollingOrientationProperty; Both
    /// default, like WPF).</summary>
    public static readonly DependencyProperty SelectiveScrollingOrientationProperty =
        DependencyProperty.RegisterAttached("SelectiveScrollingOrientation", typeof(SelectiveScrollingOrientation), typeof(SelectiveScrollingGrid),
            new FrameworkPropertyMetadata(SelectiveScrollingOrientation.Both));

    /// <summary>Scrolling-axis lookup (matches GetSelectiveScrollingOrientation).</summary>
    public static SelectiveScrollingOrientation GetSelectiveScrollingOrientation(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (SelectiveScrollingOrientation)(element.GetValue(SelectiveScrollingOrientationProperty) ?? SelectiveScrollingOrientation.Both);
    }

    /// <summary>Scrolling-axis assignment (matches SetSelectiveScrollingOrientation).</summary>
    public static void SetSelectiveScrollingOrientation(DependencyObject element, SelectiveScrollingOrientation value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(SelectiveScrollingOrientationProperty, value);
    }
}

/// <summary>
/// Cell address (matches System.Windows.Controls.DataGridCellInfo): an
/// item plus a column. <see cref="Unset"/> (null item and column) reports
/// <see cref="IsValid"/> false, like real WPF.
/// </summary>
public readonly struct DataGridCellInfo : IEquatable<DataGridCellInfo>
{
    public DataGridCellInfo(object? item, DataGridColumn? column)
    {
        Item = item;
        Column = column;
    }

    public object? Item { get; }

    public DataGridColumn? Column { get; }

    public bool IsValid => Item is not null && Column is not null;

    public static DataGridCellInfo Unset { get; } = new(null, null);

    public bool Equals(DataGridCellInfo other) =>
        ReferenceEquals(Item, other.Item) && ReferenceEquals(Column, other.Column);

    public override bool Equals(object? obj) => obj is DataGridCellInfo other && Equals(other);

    public override int GetHashCode()
    {
        var item = Item is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Item);
        var column = Column is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Column);
        return HashCode.Combine(item, column);
    }

    public static bool operator ==(DataGridCellInfo left, DataGridCellInfo right) => left.Equals(right);

    public static bool operator !=(DataGridCellInfo left, DataGridCellInfo right) => !left.Equals(right);
}

/// <summary>
/// Logical-tree helpers (matches System.Windows.LogicalTreeHelper):
/// converted ScreenToGif code walks from a visual child up to its
/// DataGridCell through the logical parent chain.
/// </summary>
public static class LogicalTreeHelper
{
    /// <summary>Logical parent (null at the root; visual-only children report their host).</summary>
    public static DependencyObject? GetParent(DependencyObject reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return (reference as UIElement)?.Parent;
    }

    /// <summary>Logical children of a framework element (empty for bare visuals).</summary>
    public static IEnumerable<DependencyObject> GetChildren(DependencyObject reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference is FrameworkElement fe ? fe.LogicalChildren : [];
    }

    /// <summary>First logical descendant with a matching name (null when absent).</summary>
    public static DependencyObject? FindLogicalNode(DependencyObject reference, string name)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(name);
        if (reference is FrameworkElement named && named.Name == name) return reference;
        foreach (var child in GetChildren(reference))
            if (FindLogicalNode(child, name) is { } found) return found;
        return null;
    }
}
