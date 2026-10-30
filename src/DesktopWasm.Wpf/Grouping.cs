using System.Collections.ObjectModel;
using System.Xml;

namespace DesktopWasm.Wpf;

// Grouping surface (PresentationFramework subset, shim, not port): the
// OxyPlot ExampleBrowser groups its demo list by Category through
// CollectionView GroupDescriptions plus one <ListBox.GroupStyle> carrying a
// GroupItem ContainerStyle (Expander chrome with an ItemsPresenter for the
// rows). Real WPF names: ItemsControl.GroupStyle
// (ObservableCollection<GroupStyle>), System.Windows.Controls.GroupStyle
// (ContainerStyle/HeaderTemplate/Panel/HidesIfEmpty),
// System.Windows.Controls.GroupItem (the per-group container) and
// System.Windows.Controls.ItemsPresenter (the template slot hosting the
// group's items). The legacy ListBox below keeps its row model and gains
// the GroupStyle collection plus ItemContainerStyle; grouping only engages
// when ItemsSource is an ICollectionView exposing Groups and at least one
// GroupStyle exists, otherwise layout/paint stay pixel-identical.

// Namespace rule: every public type here keeps the WPF short name so the
// converter's name map (and style TargetType resolution) binds it without
// an override.

/// <summary>Expander open direction (matches System.Windows.Controls.ExpandDirection).</summary>
public enum ExpandDirection
{
    Down,
    Up,
    Left,
    Right,
}

/// <summary>Per-level group visuals (matches System.Windows.Controls.GroupStyle).</summary>
public sealed class GroupStyle
{
    /// <summary>Style applied to the generated GroupItem (TargetType GroupItem).</summary>
    public Style? ContainerStyle { get; set; }

    /// <summary>Header visual factory (inflated with the group as DataContext; default paints "Name (count)").</summary>
    public DataTemplate? HeaderTemplate { get; set; }

    /// <summary>Items host factory (must inflate a Panel; default stacks vertically).</summary>
    public ItemsPanelTemplate? Panel { get; set; }

    /// <summary>Empty groups are skipped (matches HidesIfEmpty).</summary>
    public bool HidesIfEmpty { get; set; }
}

/// <summary>
/// Per-group container (matches System.Windows.Controls.GroupItem): hosts
/// one CollectionViewGroup with its item rows. The ContainerStyle template
/// inflates around it (an ItemsPresenter slot inside the template hosts the
/// item panel); without a template it stacks a header caption over the rows.
/// DataContext is the group, so template {Binding Name}/{Binding ItemCount}
/// resolve like WPF.
/// </summary>
public sealed class GroupItem : Control
{
    static GroupItem()
    {
        // Group headers never take keyboard focus (live-verified WPF default).
        FocusableProperty.OverrideMetadata(typeof(GroupItem), new FrameworkPropertyMetadata(false));
    }

    private CollectionViewGroup? _group;
    private UIElement? _headerVisual;
    private bool _templateHeader;
    private Panel? _itemsPanel;

    /// <summary>Grouped rows host (created from the style Panel or a vertical stack).</summary>
    internal Panel ItemsPanel => _itemsPanel ??= new StackPanel();

    /// <summary>Whether the template hosts rows through an ItemsPresenter slot.</summary>
    internal bool HasPresenterSlot =>
        TemplateRoot is not null && FindPresenter(TemplateRoot) is not null;

    /// <summary>
    /// Grouped node (sets DataContext so template bindings resolve; rebuilds
    /// the default header when no HeaderTemplate supplies one).
    /// </summary>
    public CollectionViewGroup? Group
    {
        get => _group;
        set
        {
            if (ReferenceEquals(_group, value)) return;
            _group = value;
            DataContext = value;
            SyncHeader();
            InvalidateMeasure();
        }
    }

    /// <summary>Applies one GroupStyle level (panel/header/container style).</summary>
    /// <remarks>
    /// Generated containers are not parented yet when this runs, so a
    /// caller-supplied <see cref="StyleApplyContext"/> carries the owning
    /// list's ancestry for StaticResource lookups (window-level keys, like
    /// the loader scope); without it only attached-tree keys resolve.
    /// </remarks>
    internal void ApplyGroupStyle(GroupStyle style, StyleApplyContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(style);
        context ??= new StyleApplyContext();
        if (style.Panel is not null)
        {
            var host = style.Panel.LoadContent();
            if (host is not Panel panel)
                throw new InvalidOperationException(
                    $"GroupStyle.Panel content must be a Panel (have <{host.GetType().Name}>).");
            if (_itemsPanel is not null)
                throw new InvalidOperationException("GroupItem already has an items host.");
            _itemsPanel = panel;
        }
        if (style.HeaderTemplate is not null)
        {
            var header = style.HeaderTemplate.LoadContent();
            if (header is not UIElement visual)
                throw new InvalidOperationException(
                    $"GroupStyle.HeaderTemplate content must be a visual (have <{header.GetType().Name}>).");
            SetHeaderVisual(visual);
        }
        SyncHeader();
        style.ContainerStyle?.ApplyTo(this, context);
        EnsurePanelHomed();
        InvalidateMeasure();
    }

    /// <summary>
    /// Appends one item row to the group's host panel. Homes the lazily
    /// created fallback panel right away (ApplyGroupStyle only homes a
    /// style-supplied panel, while the default StackPanel is minted here):
    /// rows parented into an orphan panel break the ancestor walk converted
    /// code relies on (ListBoxItem owner lookup, ItemsControlFromItemContainer).
    /// </summary>
    internal void AddGroupRow(UIElement row)
    {
        ArgumentNullException.ThrowIfNull(row);
        ItemsPanel.AddChild(row);
        EnsurePanelHomed();
    }

    private void SetHeaderVisual(UIElement visual)
    {
        if (_headerVisual is not null) base.RemoveChild(_headerVisual);
        _headerVisual = visual;
        _templateHeader = true;
        base.AddChild(visual);
    }

    private void SyncHeader()
    {
        if (_templateHeader) return; // A HeaderTemplate visual stays put.
        if (_headerVisual is not null) base.RemoveChild(_headerVisual);
        _headerVisual = null;
        if (_group is null) return;
        var caption = new TextBlock
        {
            Text = _group.Name is null
                ? $"({_group.ItemCount})"
                : $"{_group.Name} ({_group.ItemCount})",
        };
        _headerVisual = caption;
        base.AddChild(caption);
    }

    /// <summary>
    /// Keeps the item panel parented exactly once: the ItemsPresenter pulls
    /// it into the template tree when the style supplies that slot,
    /// otherwise it stays a direct child for the fallback stack.
    /// </summary>
    internal void EnsurePanelHomed()
    {
        var panel = _itemsPanel;
        if (panel is null) return;
        if (TemplateRoot is not null && FindPresenter(TemplateRoot) is { } presenter)
        {
            // Templated groups host rows through the presenter slot (the
            // staged OxyPlot Expander shape): adopt eagerly at generation
            // time, not first measure, so selection triggers resolve
            // ancestor resources before any layout ever runs.
            if (ReferenceEquals(panel.Parent, this)) base.RemoveChild(panel);
            presenter.EnsureHosts(panel);
            return;
        }
        if (!ReferenceEquals(panel.Parent, this)) base.AddChild(panel);
    }

    private static ItemsPresenter? FindPresenter(UIElement root)
    {
        if (root is ItemsPresenter presenter) return presenter;
        if (root is not FrameworkElement host) return null;
        foreach (var child in host.LogicalChildren)
        {
            var found = FindPresenter(child);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>Runs after the container template inflates (re-homes the rows host).</summary>
    protected override void OnApplyTemplate() => EnsurePanelHomed();

    protected override WpfSize MeasureCore(WpfSize available)
    {
        EnsurePanelHomed();
        if (TemplateRoot is not null)
        {
            TemplateRoot.Measure(available);
            return TemplateRoot.DesiredSize;
        }
        var width = 0.0;
        var height = 0.0;
        if (_headerVisual is not null)
        {
            _headerVisual.Measure(available);
            width = Math.Max(width, _headerVisual.DesiredSize.Width);
            height += _headerVisual.DesiredSize.Height;
        }
        ItemsPanel.Measure(new WpfSize(available.Width, available.Height));
        return new WpfSize(
            Math.Max(width, ItemsPanel.DesiredSize.Width),
            height + ItemsPanel.DesiredSize.Height);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        EnsurePanelHomed();
        if (TemplateRoot is not null)
        {
            TemplateRoot.Arrange(finalRect);
            return;
        }
        var y = finalRect.Y;
        if (_headerVisual is not null)
        {
            var headerH = _headerVisual.DesiredSize.Height;
            _headerVisual.Arrange(new WpfRect(finalRect.X, y, finalRect.Width, headerH));
            y += headerH;
        }
        ItemsPanel.Arrange(new WpfRect(finalRect.X, y, finalRect.Width,
            Math.Max(0, finalRect.Height - (y - finalRect.Y))));
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (TemplateRoot is not null)
        {
            RenderVisible(dc, TemplateRoot);
            return;
        }
        if (_headerVisual is not null) RenderVisible(dc, _headerVisual);
        RenderVisible(dc, ItemsPanel);
    }
}

/// <summary>
/// Items host slot (matches System.Windows.Controls.ItemsPresenter): the
/// template placeholder that hosts the templated GroupItem's item rows. The
/// owning GroupItem is found through the template-parent chain, so each
/// inflated tree hosts exactly its own group's panel.
/// </summary>
public sealed class ItemsPresenter : FrameworkElement
{
    private Panel? _hosted;

    private GroupItem? Owner => TemplateParent as GroupItem;

    /// <summary>Adopts the owner's item panel outside layout (generation-time
    /// selection triggers resolve ancestor resources before first measure).</summary>
    internal void EnsureHosts(Panel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (ReferenceEquals(_hosted, panel) && ReferenceEquals(panel.Parent, this)) return;
        if (_hosted is not null)
        {
            RemoveChild(_hosted);
            _hosted = null;
        }
        AddChild(panel);
        _hosted = panel;
    }

    private void SyncHost()
    {
        var panel = Owner?.TemplateRoot is null ? null : Owner.ItemsPanel;
        if (panel is null)
        {
            if (_hosted is not null)
            {
                RemoveChild(_hosted);
                _hosted = null;
            }
            return;
        }
        EnsureHosts(panel);
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        SyncHost();
        if (_hosted is null) return new WpfSize(0, 0);
        _hosted.Measure(available);
        return _hosted.DesiredSize;
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        SyncHost();
        _hosted?.Arrange(finalRect);
    }

    protected override void OnRender(DrawingContext dc)
    {
        SyncHost();
        if (_hosted is not null) RenderVisible(dc, _hosted);
    }
}

/// <summary>GroupStyle XAML readers (the <see cref="XamlLoader"/> partial).</summary>
public static partial class XamlLoader
{
    /// <summary>
    /// Reads one &lt;ListBox.GroupStyle&gt; property element (positioned on
    /// its start tag; consumes it): each nested &lt;GroupStyle&gt; carries
    /// HidesIfEmpty plus ContainerStyle/HeaderTemplate/Panel slots.
    /// </summary>
    internal static void ReadListBoxGroupStyle(XmlReader reader, IXmlLineInfo lineInfo, ListBox target)
    {
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past <ListBox.GroupStyle>
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, "Unterminated <ListBox.GroupStyle>.");
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "GroupStyle")
                throw Fail(lineInfo, $"Expected <GroupStyle> inside <ListBox.GroupStyle> (got <{reader.LocalName}>).");
            target.GroupStyle.Add(ReadGroupStyle(reader, lineInfo));
        }
        reader.Read(); // past </ListBox.GroupStyle>
    }

    private static GroupStyle ApplyGroupStyleSlot(GroupStyle style, string slot, string raw, IXmlLineInfo line)
    {
        var text = raw.Trim();
        if (!text.StartsWith("{StaticResource ", StringComparison.Ordinal) || !text.EndsWith('}'))
            throw Fail(line, $"'{raw}' takes {slot}={{StaticResource Key}} (inline slot content stays a boundary).");
        var key = text["{StaticResource ".Length..^1].Trim();
        if (key.Length == 0 || key.Contains('{') || key.Contains('='))
            throw Fail(line, $"'{raw}' takes a plain key only ({{{slot} Key}}).");
        var app = Application.Current;
        if (app is null || !app.TryFindResource(key, out var resource) || resource is null)
            throw Fail(line, $"StaticResource '{key}' was not found in application resources (GroupStyle.{slot}).");
        switch (slot)
        {
            case "ContainerStyle" when resource is Style container:
                style.ContainerStyle = container;
                break;
            case "HeaderTemplate" when resource is DataTemplate header:
                style.HeaderTemplate = header;
                break;
            case "Panel" when resource is ItemsPanelTemplate panel:
                style.Panel = panel;
                break;
            default:
                throw Fail(line, $"StaticResource '{key}' is a {resource.GetType().Name}, not a {slot}.");
        }
        return style;
    }

    private static GroupStyle ReadGroupStyle(XmlReader reader, IXmlLineInfo lineInfo)
    {
        var style = new GroupStyle();
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns") continue;
                if (reader.Name == "xmlns") continue;
                if (reader.LocalName == "HidesIfEmpty")
                {
                    style.HidesIfEmpty = ParseBool(reader.Value, lineInfo);
                    continue;
                }
                if (reader.LocalName is "ContainerStyle" or "HeaderTemplate" or "Panel")
                {
                    // Attribute form (the staged demo writes
                    // ContainerStyle="{StaticResource DemoGroupItemStyle}"):
                    // resolves through application scope now, like template
                    // StaticResource values; misses and mistypes fail loud.
                    style = ApplyGroupStyleSlot(style, reader.LocalName, reader.Value, lineInfo);
                    continue;
                }
                throw Fail(lineInfo, $"'{reader.LocalName}' is not valid on <GroupStyle> (HidesIfEmpty plus GroupStyle.ContainerStyle/HeaderTemplate/Panel).");
            }
            reader.MoveToElement();
        }
        if (reader.IsEmptyElement) { reader.Read(); return style; }
        reader.Read(); // past <GroupStyle>
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, "Unterminated <GroupStyle>.");
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }
            var local = reader.LocalName;
            var slot = local.Contains('.') ? local[(local.IndexOf('.') + 1)..] : local;
            if (slot is not ("ContainerStyle" or "HeaderTemplate" or "Panel"))
                throw Fail(lineInfo, $"<{local}> is not a <GroupStyle> slot (GroupStyle.ContainerStyle/HeaderTemplate/Panel).");
            var owner = local.Contains('.') ? local[..local.IndexOf('.')] : "GroupStyle";
            if (owner != "GroupStyle")
                throw Fail(lineInfo, $"<{local}> does not belong to <GroupStyle>.");
            if (reader.IsEmptyElement)
                throw Fail(lineInfo, $"<{local}> needs one <{(slot == "ContainerStyle" ? "Style" : slot == "HeaderTemplate" ? "DataTemplate" : "ItemsPanelTemplate")}>.");
            reader.Read(); // past the slot start tag
            SkipWhitespace(reader);
            if (reader.NodeType != XmlNodeType.Element)
                throw Fail(lineInfo, $"<{local}> needs one <{(slot == "ContainerStyle" ? "Style" : slot == "HeaderTemplate" ? "DataTemplate" : "ItemsPanelTemplate")} (no text).");
            switch (slot)
            {
                case "ContainerStyle":
                    if (reader.LocalName != "Style")
                        throw Fail(lineInfo, $"<GroupStyle.ContainerStyle> takes <Style> (got <{reader.LocalName}>).");
                    if (style.ContainerStyle is not null)
                        throw Fail(lineInfo, "<GroupStyle.ContainerStyle> is set twice.");
                    style.ContainerStyle = ReadStyleElement(reader, lineInfo);
                    break;
                case "HeaderTemplate":
                    if (reader.LocalName != "DataTemplate")
                        throw Fail(lineInfo, $"<GroupStyle.HeaderTemplate> takes <DataTemplate> (got <{reader.LocalName}>).");
                    if (style.HeaderTemplate is not null)
                        throw Fail(lineInfo, "<GroupStyle.HeaderTemplate> is set twice.");
                    style.HeaderTemplate = ReadDataTemplateElement(reader, lineInfo);
                    break;
                default:
                    if (reader.LocalName != "ItemsPanelTemplate")
                        throw Fail(lineInfo, $"<GroupStyle.Panel> takes <ItemsPanelTemplate> (got <{reader.LocalName}>).");
                    if (style.Panel is not null)
                        throw Fail(lineInfo, "<GroupStyle.Panel> is set twice.");
                    style.Panel = ReadItemsPanelTemplateElement(reader, lineInfo);
                    break;
            }
            SkipWhitespace(reader);
            if (reader.NodeType != XmlNodeType.EndElement)
                throw Fail(lineInfo, $"Malformed <{local}>.");
            reader.Read(); // past the slot end tag
        }
        reader.Read(); // past </GroupStyle>
        return style;
    }
}
