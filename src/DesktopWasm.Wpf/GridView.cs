namespace DesktopWasm.Wpf;

// Grid view (PresentationFramework subset, live shapes verified against the
// 9.0.21 PresentationFramework ref pack plus a live net9.0-windows runtime
// probe): ViewBase, GridView with its column collection and header knobs,
// and GridViewColumn with store-backed dependency properties. The converted
// MaterialDesign TreeListView reads GridView.Columns and swaps a column's
// CellTemplate in code; ListView.View carries the active view.
//
// Retained limits: no column layout/measure (ActualWidth stays 0 until a
// GridView panel runs), no header visuals, no view switching beyond the
// stored View reference.

/// <summary>List-view view base (matches ViewBase : DependencyObject).</summary>
public abstract class ViewBase : DependencyObject
{
    /// <summary>Base constructor (converted GridView binds it).</summary>
    protected ViewBase()
    {
    }

    /// <summary>Default style key (null: no engine default-style lookup for views).</summary>
    public virtual object? DefaultStyleKey => null;

    /// <summary>Container default style key (null: no engine default-style lookup for views).</summary>
    public virtual object? ItemContainerDefaultStyleKey => null;
}

/// <summary>
/// Grid view (matches GridView : ViewBase): column collection plus header
/// knobs. Columns default empty, reordering allowed, matching live WPF.
/// </summary>
public class GridView : ViewBase
{
    /// <summary>ScrollViewer style key (matches GridView.GridViewScrollViewerStyleKey;
    /// a ComponentResourceKey singleton like the StatusBar style key, so
    /// x:Static lookups share identity; the default theme style stays unset in v1).</summary>
    public static ResourceKey GridViewScrollViewerStyleKey { get; } =
        new ComponentResourceKey(typeof(GridView), "GridViewScrollViewerStyle");

    /// <summary>Item container style key (matches GridView.GridViewItemContainerStyleKey;
    /// same singleton shape; the default container style stays unset in v1).</summary>
    public static ResourceKey GridViewItemContainerStyleKey { get; } =
        new ComponentResourceKey(typeof(GridView), "GridViewItemContainerStyle");

    private readonly GridViewColumnCollection _columns = new();

    /// <summary>Empty view (live-verified shape).</summary>
    public GridView()
    {
    }

    /// <summary>Columns shown by the view (empty by default, like WPF).</summary>
    public GridViewColumnCollection Columns => _columns;

    /// <summary>Whether columns reorder (default true, live-verified).</summary>
    public bool AllowsColumnReorder { get; set; } = true;

    /// <summary>Header container style (stored; no header visuals in v1).</summary>
    public Style? ColumnHeaderContainerStyle { get; set; }

    /// <summary>Header context menu (stored; no header visuals in v1).</summary>
    public ContextMenu? ColumnHeaderContextMenu { get; set; }

    /// <summary>Header string-format suffix (stored; no header visuals in v1).</summary>
    public string? ColumnHeaderStringFormat { get; set; }

    /// <summary>Header template (stored; no header visuals in v1).</summary>
    public DataTemplate? ColumnHeaderTemplate { get; set; }

    /// <summary>Header template selector (stored; no header visuals in v1).</summary>
    public DataTemplateSelector? ColumnHeaderTemplateSelector { get; set; }

    /// <summary>Header tooltip (stored; no header visuals in v1).</summary>
    public object? ColumnHeaderToolTip { get; set; }
}

/// <summary>Grid-view column list (matches GridViewColumnCollection).</summary>
public sealed class GridViewColumnCollection : System.Collections.ObjectModel.ObservableCollection<GridViewColumn>
{
}

/// <summary>
/// Grid-view column (matches GridViewColumn : DependencyObject): store-backed
/// dependency properties with the live WPF defaults (NaN width, zero actual
/// width, null header/template). Converted TreeListView code reads Columns
/// and swaps CellTemplate in code.
/// </summary>
public class GridViewColumn : DependencyObject
{
    /// <summary>Identifies the CellTemplate store.</summary>
    public static readonly DependencyProperty CellTemplateProperty =
        DependencyProperty.Register("CellTemplate", typeof(DataTemplate), typeof(GridViewColumn));

    /// <summary>Identifies the CellTemplateSelector store.</summary>
    public static readonly DependencyProperty CellTemplateSelectorProperty =
        DependencyProperty.Register("CellTemplateSelector", typeof(DataTemplateSelector), typeof(GridViewColumn));

    /// <summary>Identifies the DisplayMemberBinding store.</summary>
    public static readonly DependencyProperty DisplayMemberBindingProperty =
        DependencyProperty.Register("DisplayMemberBinding", typeof(BindingBase), typeof(GridViewColumn));

    /// <summary>Identifies the Header store.</summary>
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register("Header", typeof(object), typeof(GridViewColumn));

    /// <summary>Identifies the HeaderContainerStyle store.</summary>
    public static readonly DependencyProperty HeaderContainerStyleProperty =
        DependencyProperty.Register("HeaderContainerStyle", typeof(Style), typeof(GridViewColumn));

    /// <summary>Identifies the HeaderStringFormat store.</summary>
    public static readonly DependencyProperty HeaderStringFormatProperty =
        DependencyProperty.Register("HeaderStringFormat", typeof(string), typeof(GridViewColumn));

    /// <summary>Identifies the HeaderTemplate store.</summary>
    public static readonly DependencyProperty HeaderTemplateProperty =
        DependencyProperty.Register("HeaderTemplate", typeof(DataTemplate), typeof(GridViewColumn));

    /// <summary>Identifies the HeaderTemplateSelector store.</summary>
    public static readonly DependencyProperty HeaderTemplateSelectorProperty =
        DependencyProperty.Register("HeaderTemplateSelector", typeof(DataTemplateSelector), typeof(GridViewColumn));

    /// <summary>Identifies the Width store.</summary>
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register("Width", typeof(double), typeof(GridViewColumn), double.NaN);

    /// <summary>Empty column (live-verified shape).</summary>
    public GridViewColumn()
    {
    }

    /// <summary>Cell template (null by default).</summary>
    public DataTemplate? CellTemplate
    {
        get => (DataTemplate?)GetValue(CellTemplateProperty);
        set => SetValue(CellTemplateProperty, value);
    }

    /// <summary>Cell template selector (null by default).</summary>
    public DataTemplateSelector? CellTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(CellTemplateSelectorProperty);
        set => SetValue(CellTemplateSelectorProperty, value);
    }

    /// <summary>Display-member binding (null by default).</summary>
    public BindingBase? DisplayMemberBinding
    {
        get => (BindingBase?)GetValue(DisplayMemberBindingProperty);
        set => SetValue(DisplayMemberBindingProperty, value);
    }

    /// <summary>Column header (null by default).</summary>
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Header container style (null by default).</summary>
    public Style? HeaderContainerStyle
    {
        get => (Style?)GetValue(HeaderContainerStyleProperty);
        set => SetValue(HeaderContainerStyleProperty, value);
    }

    /// <summary>Header string-format suffix (null by default).</summary>
    public string? HeaderStringFormat
    {
        get => (string?)GetValue(HeaderStringFormatProperty);
        set => SetValue(HeaderStringFormatProperty, value);
    }

    /// <summary>Header template (null by default).</summary>
    public DataTemplate? HeaderTemplate
    {
        get => (DataTemplate?)GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    /// <summary>Header template selector (null by default).</summary>
    public DataTemplateSelector? HeaderTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(HeaderTemplateSelectorProperty);
        set => SetValue(HeaderTemplateSelectorProperty, value);
    }

    /// <summary>Column width in pixels (NaN/auto by default, live-verified).</summary>
    public double Width
    {
        get => (double)GetValue(WidthProperty)!;
        set => SetValue(WidthProperty, value);
    }

    /// <summary>Realized width (zero until a GridView panel measures, like a fresh column).</summary>
    public double ActualWidth => 0.0;
}

/// <summary>
/// Grid-view row presenter base (matches GridViewRowPresenterBase :
/// FrameworkElement): column host for row and header presenters; column
/// layout stays a boundary (no column measure in v1, like GridView
/// itself). Public here (native keeps it internal) because C# requires
/// the base to be at least as accessible as its public presenters.
/// </summary>
public abstract class GridViewRowPresenterBase : FrameworkElement
{
    private readonly GridViewColumnCollection _columns = new();

    /// <summary>Columns shown by the presenter (empty by default, like WPF).</summary>
    public GridViewColumnCollection Columns => _columns;
}

/// <summary>
/// Grid-view row presenter (matches GridViewRowPresenter : base):
/// content host for one row; no column visuals in v1.
/// </summary>
public class GridViewRowPresenter : GridViewRowPresenterBase
{
    /// <summary>Identifies the Content store.</summary>
    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.Register("Content", typeof(object), typeof(GridViewRowPresenter));

    /// <summary>Row content (null by default).</summary>
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }
}

/// <summary>
/// Grid-view header presenter (matches GridViewHeaderRowPresenter : base):
/// header knobs retained with the live WPF defaults; no header visuals
/// in v1, like GridView itself.
/// </summary>
public class GridViewHeaderRowPresenter : GridViewRowPresenterBase
{
    /// <summary>Identifies the AllowsColumnReorder store.</summary>
    public static readonly DependencyProperty AllowsColumnReorderProperty =
        DependencyProperty.Register("AllowsColumnReorder", typeof(bool), typeof(GridViewHeaderRowPresenter), true);

    /// <summary>Identifies the ColumnHeaderContainerStyle store.</summary>
    public static readonly DependencyProperty ColumnHeaderContainerStyleProperty =
        DependencyProperty.Register("ColumnHeaderContainerStyle", typeof(Style), typeof(GridViewHeaderRowPresenter));

    /// <summary>Identifies the ColumnHeaderContextMenu store.</summary>
    public static readonly DependencyProperty ColumnHeaderContextMenuProperty =
        DependencyProperty.Register("ColumnHeaderContextMenu", typeof(ContextMenu), typeof(GridViewHeaderRowPresenter));

    /// <summary>Identifies the ColumnHeaderStringFormat store.</summary>
    public static readonly DependencyProperty ColumnHeaderStringFormatProperty =
        DependencyProperty.Register("ColumnHeaderStringFormat", typeof(string), typeof(GridViewHeaderRowPresenter));

    /// <summary>Identifies the ColumnHeaderTemplate store.</summary>
    public static readonly DependencyProperty ColumnHeaderTemplateProperty =
        DependencyProperty.Register("ColumnHeaderTemplate", typeof(DataTemplate), typeof(GridViewHeaderRowPresenter));

    /// <summary>Identifies the ColumnHeaderTemplateSelector store.</summary>
    public static readonly DependencyProperty ColumnHeaderTemplateSelectorProperty =
        DependencyProperty.Register("ColumnHeaderTemplateSelector", typeof(DataTemplateSelector), typeof(GridViewHeaderRowPresenter));

    /// <summary>Identifies the ColumnHeaderToolTip store.</summary>
    public static readonly DependencyProperty ColumnHeaderToolTipProperty =
        DependencyProperty.Register("ColumnHeaderToolTip", typeof(object), typeof(GridViewHeaderRowPresenter));

    /// <summary>Whether columns reorder (default true, like WPF).</summary>
    public bool AllowsColumnReorder
    {
        get => (bool)GetValue(AllowsColumnReorderProperty)!;
        set => SetValue(AllowsColumnReorderProperty, value);
    }

    /// <summary>Header container style (null by default).</summary>
    public Style? ColumnHeaderContainerStyle
    {
        get => (Style?)GetValue(ColumnHeaderContainerStyleProperty);
        set => SetValue(ColumnHeaderContainerStyleProperty, value);
    }

    /// <summary>Header context menu (null by default).</summary>
    public ContextMenu? ColumnHeaderContextMenu
    {
        get => (ContextMenu?)GetValue(ColumnHeaderContextMenuProperty);
        set => SetValue(ColumnHeaderContextMenuProperty, value);
    }

    /// <summary>Header string-format suffix (null by default).</summary>
    public string? ColumnHeaderStringFormat
    {
        get => (string?)GetValue(ColumnHeaderStringFormatProperty);
        set => SetValue(ColumnHeaderStringFormatProperty, value);
    }

    /// <summary>Header template (null by default).</summary>
    public DataTemplate? ColumnHeaderTemplate
    {
        get => (DataTemplate?)GetValue(ColumnHeaderTemplateProperty);
        set => SetValue(ColumnHeaderTemplateProperty, value);
    }

    /// <summary>Header template selector (null by default).</summary>
    public DataTemplateSelector? ColumnHeaderTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ColumnHeaderTemplateSelectorProperty);
        set => SetValue(ColumnHeaderTemplateSelectorProperty, value);
    }

    /// <summary>Header tooltip (null by default).</summary>
    public object? ColumnHeaderToolTip
    {
        get => GetValue(ColumnHeaderToolTipProperty);
        set => SetValue(ColumnHeaderToolTipProperty, value);
    }
}

/// <summary>Grid-view column header role (matches GridViewColumnHeaderRole).</summary>
public enum GridViewColumnHeaderRole
{
    /// <summary>Normal header.</summary>
    Normal,
    /// <summary>Floating (drag) header.</summary>
    Floating,
    /// <summary>Padding header.</summary>
    Padding,
}

/// <summary>
/// Grid-view column header (matches GridViewColumnHeader : ButtonBase):
/// column/role retained; no header drag or visuals in v1.
/// </summary>
public class GridViewColumnHeader : ButtonBase
{
    /// <summary>Identifies the Column store.</summary>
    public static readonly DependencyProperty ColumnProperty =
        DependencyProperty.Register("Column", typeof(GridViewColumn), typeof(GridViewColumnHeader));

    /// <summary>Identifies the Role store.</summary>
    public static readonly DependencyProperty RoleProperty =
        DependencyProperty.Register("Role", typeof(GridViewColumnHeaderRole), typeof(GridViewColumnHeader),
            new FrameworkPropertyMetadata(GridViewColumnHeaderRole.Normal));

    /// <summary>The column this header represents (null by default).</summary>
    public GridViewColumn? Column
    {
        get => (GridViewColumn?)GetValue(ColumnProperty);
        set => SetValue(ColumnProperty, value);
    }

    /// <summary>Header role (Normal by default).</summary>
    public GridViewColumnHeaderRole Role
    {
        get => (GridViewColumnHeaderRole)(GetValue(RoleProperty) ?? GridViewColumnHeaderRole.Normal);
        set => SetValue(RoleProperty, value);
    }
}
