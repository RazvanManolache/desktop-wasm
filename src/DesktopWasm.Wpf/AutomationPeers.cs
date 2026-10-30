namespace DesktopWasm.Wpf;

// Item/list automation peers (PresentationFramework subset, live shapes
// verified against the 9.0.21 PresentationFramework/PresentationCore ref
// packs plus a live net9.0-windows runtime probe): the pattern/control-type
// enumerations plus the peer hierarchy the MaterialDesign TreeListView peers
// derive (TreeListViewAutomationPeer : ListViewAutomationPeer,
// TreeListViewItemAutomationPeer : ListBoxItemAutomationPeer with
// GetPattern/GetAutomationControlTypeCore/GetChildrenCore overrides and
// CreateItemAutomationPeer/Owner/Item plumbing).
//
// GetPattern returns null here (no OS provider tree in the browser; the
// announcement channel still lives on AutomationPeer). GetChildrenCore
// returns null when there are no automation children, like WPF. The
// UIAutomationProvider text/value contracts (ITextProvider,
// ITextRangeProvider, IValueProvider, IRawElementProviderSimple) live in
// AutomationProviders.cs for the converted AvalonEdit text peers; the
// remaining selection/table patterns stay out of scope (no converted member
// reference names them).

/// <summary>Automation pattern under query (matches PatternInterface).</summary>
public enum PatternInterface
{
    /// <summary>No pattern (default).</summary>
    Invoke = 0,
    /// <summary>Selection container/item pattern.</summary>
    Selection = 1,
    /// <summary>Value pattern.</summary>
    Value = 2,
    /// <summary>Range-value pattern.</summary>
    RangeValue = 3,
    /// <summary>Scroll pattern.</summary>
    Scroll = 4,
    /// <summary>Scroll-item pattern.</summary>
    ScrollItem = 5,
    /// <summary>Expand/collapse pattern.</summary>
    ExpandCollapse = 6,
    /// <summary>Grid pattern.</summary>
    Grid = 7,
    /// <summary>Grid-item pattern.</summary>
    GridItem = 8,
    /// <summary>Multiple-view pattern.</summary>
    MultipleView = 9,
    /// <summary>Window pattern.</summary>
    Window = 10,
    /// <summary>Selection-item pattern.</summary>
    SelectionItem = 11,
    /// <summary>Dock pattern.</summary>
    Dock = 12,
    /// <summary>Table pattern.</summary>
    Table = 13,
    /// <summary>Table-item pattern.</summary>
    TableItem = 14,
    /// <summary>Toggle pattern.</summary>
    Toggle = 15,
    /// <summary>Transform pattern.</summary>
    Transform = 16,
    /// <summary>Text pattern.</summary>
    Text = 17,
    /// <summary>Item-container pattern.</summary>
    ItemContainer = 18,
    /// <summary>Virtualized-item pattern.</summary>
    VirtualizedItem = 19,
    /// <summary>Synchronized-input pattern.</summary>
    SynchronizedInput = 20,
}

/// <summary>Automation control kind (matches AutomationControlType).</summary>
public enum AutomationControlType
{
    /// <summary>Button control.</summary>
    Button = 0,
    /// <summary>Calendar control.</summary>
    Calendar = 1,
    /// <summary>Check box.</summary>
    CheckBox = 2,
    /// <summary>Combo box.</summary>
    ComboBox = 3,
    /// <summary>Edit field.</summary>
    Edit = 4,
    /// <summary>Hyperlink.</summary>
    Hyperlink = 5,
    /// <summary>Image.</summary>
    Image = 6,
    /// <summary>List item.</summary>
    ListItem = 7,
    /// <summary>List.</summary>
    List = 8,
    /// <summary>Menu bar.</summary>
    Menu = 9,
    /// <summary>Menu bar item.</summary>
    MenuBar = 10,
    /// <summary>Menu item.</summary>
    MenuItem = 11,
    /// <summary>Progress bar.</summary>
    ProgressBar = 12,
    /// <summary>Radio button.</summary>
    RadioButton = 13,
    /// <summary>Scroll bar.</summary>
    ScrollBar = 14,
    /// <summary>Slider.</summary>
    Slider = 15,
    /// <summary>Spinner.</summary>
    Spinner = 16,
    /// <summary>Status bar.</summary>
    StatusBar = 17,
    /// <summary>Tab control.</summary>
    Tab = 18,
    /// <summary>Tab item.</summary>
    TabItem = 19,
    /// <summary>Text block.</summary>
    Text = 20,
    /// <summary>Tool bar.</summary>
    ToolBar = 21,
    /// <summary>Tool tip.</summary>
    ToolTip = 22,
    /// <summary>Tree view.</summary>
    Tree = 23,
    /// <summary>Tree item.</summary>
    TreeItem = 24,
    /// <summary>Custom control.</summary>
    Custom = 25,
    /// <summary>Group.</summary>
    Group = 26,
    /// <summary>Thumb.</summary>
    Thumb = 27,
    /// <summary>Data grid.</summary>
    DataGrid = 28,
    /// <summary>Data item.</summary>
    DataItem = 29,
    /// <summary>Document.</summary>
    Document = 30,
    /// <summary>Split button.</summary>
    SplitButton = 31,
    /// <summary>Window.</summary>
    Window = 32,
    /// <summary>Pane.</summary>
    Pane = 33,
    /// <summary>Header.</summary>
    Header = 34,
    /// <summary>Header item.</summary>
    HeaderItem = 35,
    /// <summary>Table.</summary>
    Table = 36,
    /// <summary>Title bar.</summary>
    TitleBar = 37,
    /// <summary>Separator.</summary>
    Separator = 38,
}

/// <summary>
/// Framework-element peer (matches FrameworkElementAutomationPeer :
/// UIElementAutomationPeer). Owner is the element, so converted peers
/// built over converted controls bind their base.
/// </summary>
public class FrameworkElementAutomationPeer : UIElementAutomationPeer
{
    /// <summary>Peers the element (null owner rejected, like WPF).</summary>
    public FrameworkElementAutomationPeer(FrameworkElement owner) : base(owner)
    {
    }
}

/// <summary>
/// Item peer (matches ItemAutomationPeer : AutomationPeer): carries the
/// data item plus its owning items peer. The converted TreeListView item
/// peer reads both back in GetChildrenCore.
/// </summary>
public class ItemAutomationPeer : AutomationPeer
{
    /// <summary>Peers the item under its owning items peer.</summary>
    public ItemAutomationPeer(object item, ItemsControlAutomationPeer itemsControlAutomationPeer)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        ItemsControlAutomationPeer = itemsControlAutomationPeer
            ?? throw new ArgumentNullException(nameof(itemsControlAutomationPeer));
    }

    /// <summary>Data item this peer represents.</summary>
    public object Item { get; }

    /// <summary>Owning items peer.</summary>
    public ItemsControlAutomationPeer ItemsControlAutomationPeer { get; }
}

/// <summary>
/// Selector-item peer (matches SelectorItemAutomationPeer : ItemAutomationPeer).
/// </summary>
public class SelectorItemAutomationPeer : ItemAutomationPeer
{
    /// <summary>Peers the item under its owning selector peer.</summary>
    public SelectorItemAutomationPeer(object item, SelectorAutomationPeer parent) : base(item, parent)
    {
    }
}

/// <summary>
/// List-box-item peer (matches ListBoxItemAutomationPeer :
/// SelectorItemAutomationPeer with the (object, SelectorAutomationPeer)
/// constructor the converted TreeListView item peer calls).
/// </summary>
public class ListBoxItemAutomationPeer : SelectorItemAutomationPeer
{
    /// <summary>Peers the item under its owning selector peer.</summary>
    public ListBoxItemAutomationPeer(object item, SelectorAutomationPeer parent) : base(item, parent)
    {
    }
}

/// <summary>
/// Items peer (matches ItemsControlAutomationPeer :
/// FrameworkElementAutomationPeer). CreateItemAutomationPeer mints the
/// default item peer; the converted TreeListView peer overrides it.
/// </summary>
public class ItemsControlAutomationPeer : FrameworkElementAutomationPeer
{
    /// <summary>Peers the items control.</summary>
    public ItemsControlAutomationPeer(ItemsControl owner) : base(owner)
    {
    }

    /// <summary>Whether virtualization is active (retained: no virtualizing panels in v1).</summary>
    public bool IsVirtualized => false;

    /// <summary>Mints the default peer for an item (converted peers override this).</summary>
    protected virtual ItemAutomationPeer CreateItemAutomationPeer(object item) =>
        new(item, this);
}

/// <summary>
/// Selector peer (matches SelectorAutomationPeer : ItemsControlAutomationPeer).
/// </summary>
public class SelectorAutomationPeer : ItemsControlAutomationPeer
{
    /// <summary>Peers the selector.</summary>
    public SelectorAutomationPeer(Selector owner) : base(owner)
    {
    }
}

/// <summary>
/// List-box peer (matches ListBoxAutomationPeer : SelectorAutomationPeer).
/// The constructor stays protected: the engine legacy <see cref="ListBox"/>
/// is Control-based (not a Selector), so the real public (ListBox) shape
/// has no engine counterpart; the class exists so
/// <see cref="ListViewAutomationPeer"/> keeps the real chain.
/// </summary>
public class ListBoxAutomationPeer : SelectorAutomationPeer
{
    /// <summary>Chains the real intermediate (converted code never constructs this directly).</summary>
    protected ListBoxAutomationPeer(Selector owner) : base(owner)
    {
    }
}

/// <summary>
/// List-view peer (matches ListViewAutomationPeer : ListBoxAutomationPeer).
/// The converted TreeListView peer derives this with the ListView owner.
/// </summary>
public class ListViewAutomationPeer : ListBoxAutomationPeer
{
    /// <summary>Peers the list view.</summary>
    public ListViewAutomationPeer(ListView owner) : base(owner)
    {
    }
}
