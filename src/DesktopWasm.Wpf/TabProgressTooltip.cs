namespace DesktopWasm.Wpf;

// ScreenToGif tab/progress/tooltip slice: the editor ribbon is a
// HideableTabControl (TabControl subclass) of AwareTabItem pages with a
// TabPanel items host, the encoder list paints ProgressBar values
// (TemplateBinding Percentage/IsIndeterminate plus Circular/Extended
// ProgressBar subclasses over ValueChanged), and nearly every toolbar
// button carries a ToolTip or ToolTipService-attached hint (Placement,
// offsets, ShowDuration override). All six map by engine type name;
// behavior below is real selection/content/layout/paint/hover, not an
// alias. Retained limits: no ControlTemplate inflation (HideableTabControl
// template parts stay dormant; animation clocks live in AnimationClocks.cs),
// no visual Header content (headers paint
// as text; UIElement headers measure but do not paint), no tooltip show
// delays (hover opens immediately) and no screen-edge clamping.

/// <summary>
/// Tab page (matches System.Windows.Controls.TabItem): a ContentControl
/// with a Header plus a two-way IsSelected flag. Setting IsSelected on a
/// parented item selects it in the owning TabControl (through a TabPanel
/// host when one is used); the owner syncs the flag on selection change.
/// </summary>
public class TabItem : ContentControl
{
    private object? _header;
    private bool _isSelected;

    /// <summary>Tab strip label (string paints; visuals measure only).</summary>
    public object? Header
    {
        get => _header;
        set
        {
            if (Equals(_header, value)) return;
            _header = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Whether this page is selected. Setting true on a parented item
    /// selects it in the owning TabControl; setting false deselects.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            InvalidateVisual();
            var owner = OwnerControl;
            if (owner is null) return;
            if (value)
            {
                if (!ReferenceEquals(owner.SelectedItem, this))
                    owner.SelectedItem = this;
            }
            else if (ReferenceEquals(owner.SelectedItem, this))
            {
                owner.SelectedIndex = -1;
            }
        }
    }

    /// <summary>Owning tab strip, walking up through a TabPanel host.</summary>
    private TabControl? OwnerControl
    {
        get
        {
            for (var p = Parent as FrameworkElement; p is not null; p = p.Parent as FrameworkElement)
                if (p is TabControl tabs)
                    return tabs;
            return null;
        }
    }

    /// <summary>Owner-side flag sync (bypasses the back-select above).</summary>
    internal void SetSelectedInternal(bool value)
    {
        if (_isSelected == value) return;
        _isSelected = value;
        InvalidateVisual();
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var text = Header?.ToString() ?? string.Empty;
        var (shapedW, shapedH) = TryMeasure(text, ComposeFontCss(), text.Length * 8, 16);
        return new WpfSize(
            Math.Min(available.Width, shapedW + 20),
            Math.Max(28, shapedH + 12));
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Direct-host paint (a TabPanel of TabItems paints each header);
        // inside a TabControl the strip below paints every header instead.
        PaintHeader(dc, RenderRect, _isSelected, Header, ComposeFontCss());
    }

    internal static void PaintHeader(DrawingContext dc, WpfRect slot, bool selected, object? header, string fontCss, uint dimEdge = 0x707070FFu)
    {
        if (slot.Width <= 0 || slot.Height <= 0) return;
        dc.DrawRectangle((float)slot.X, (float)slot.Y, (float)slot.Width, (float)slot.Height, dimEdge);
        if (slot.Width > 2 && slot.Height > 2)
            dc.DrawRectangle((float)slot.X + 1, (float)slot.Y + 1, (float)slot.Width - 2, (float)slot.Height - 2,
                selected ? 0xFFFFFFFFu : 0xF0F0F0FFu);
        var text = header?.ToString() ?? string.Empty;
        if (!string.IsNullOrEmpty(text))
            dc.DrawText(text, (float)(slot.X + slot.Width / 2), (float)(slot.Y + slot.Height / 2),
                fontCss, 0x000000FFu, 1, 2, -1);
    }
}

/// <summary>
/// Tab strip (matches System.Windows.Controls.TabControl): a Selector
/// whose header row selects pages and whose content area hosts the
/// selected page content. Selection, SelectedContent and the
/// SelectionChanged payload come from Selector; header presses (pointer
/// or converted PreviewMouseDown handlers on the arranged TabItems) and
/// arrow keys switch pages; disabled pages are skipped.
/// </summary>
public class TabControl : Selector
{
    private const double HeaderExtent = 28;

    private UIElement? _contentVisual;
    private readonly List<WpfRect> _headerRects = new();

    /// <summary>Tabs take focus (store-backed; the Control default is true).</summary>
    public override bool Focusable
    {
        get => (bool?)GetValue(FocusableProperty) ?? true;
        set => SetValue(FocusableProperty, value);
    }

    /// <summary>Header strip side (default Top, like WPF).</summary>
    public Dock TabStripPlacement { get; set; } = Dock.Top;

    /// <summary>
    /// Selected page content (the TabItem Content when the selection is a
    /// TabItem, else the selected item itself; null when nothing selects).
    /// </summary>
    public object? SelectedContent =>
        SelectedItem is TabItem tab ? tab.Content : SelectedItem;

    /// <summary>Selected-content template token (matches System.Windows.Controls.TabControl.SelectedContentTemplateProperty).</summary>
    public static readonly DependencyProperty SelectedContentTemplateProperty =
        DependencyProperty.Register("SelectedContentTemplate", typeof(DataTemplate), typeof(TabControl),
            new FrameworkPropertyMetadata((DataTemplate?)null));

    /// <summary>Selected-content selector token (matches System.Windows.Controls.TabControl.SelectedContentTemplateSelectorProperty).</summary>
    public static readonly DependencyProperty SelectedContentTemplateSelectorProperty =
        DependencyProperty.Register("SelectedContentTemplateSelector", typeof(DataTemplateSelector), typeof(TabControl),
            new FrameworkPropertyMetadata((DataTemplateSelector?)null));

    /// <summary>Selected-content format token (matches System.Windows.Controls.TabControl.SelectedContentStringFormatProperty).</summary>
    public static readonly DependencyProperty SelectedContentStringFormatProperty =
        DependencyProperty.Register("SelectedContentStringFormat", typeof(string), typeof(TabControl),
            new FrameworkPropertyMetadata((string?)null));

    private DataTemplate? _selectedContentTemplate;

    /// <summary>
    /// Fixed visual factory for the selected page (matches
    /// System.Windows.Controls.TabControl.SelectedContentTemplate; converted
    /// TabControlEx.CreateChildContentPresenter reads it onto the presenter).
    /// Kept settable (real WPF registers it read-only and computes it, but
    /// the converted consumer and Slice67ChromeContentTests pin the setter).
    /// </summary>
    public DataTemplate? SelectedContentTemplate
    {
        get => _selectedContentTemplate;
        set
        {
            if (ReferenceEquals(_selectedContentTemplate, value)) return;
            _selectedContentTemplate = value;
            SyncSelectedContent();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private DataTemplateSelector? _selectedContentTemplateSelector;

    /// <summary>
    /// Per-page visual choice (matches
    /// System.Windows.Controls.TabControl.SelectedContentTemplateSelector;
    /// converted TabControlEx.CreateChildContentPresenter reads it onto the
    /// presenter when no fixed template is set).
    /// </summary>
    public DataTemplateSelector? SelectedContentTemplateSelector
    {
        get => _selectedContentTemplateSelector;
        set
        {
            if (ReferenceEquals(_selectedContentTemplateSelector, value)) return;
            _selectedContentTemplateSelector = value;
            SyncSelectedContent();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private string? _selectedContentStringFormat;

    /// <summary>
    /// Format for the selected page text (matches
    /// System.Windows.Controls.TabControl.SelectedContentStringFormat;
    /// converted TabControlEx.CreateChildContentPresenter reads it onto the
    /// presenter).
    /// </summary>
    public string? SelectedContentStringFormat
    {
        get => _selectedContentStringFormat;
        set
        {
            if (_selectedContentStringFormat == value) return;
            _selectedContentStringFormat = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>Content template token (matches System.Windows.Controls.TabControl.ContentTemplateProperty).</summary>
    public static readonly DependencyProperty ContentTemplateProperty =
        DependencyProperty.Register("ContentTemplate", typeof(DataTemplate), typeof(TabControl),
            new FrameworkPropertyMetadata((DataTemplate?)null));

    /// <summary>Content selector token (matches System.Windows.Controls.TabControl.ContentTemplateSelectorProperty).</summary>
    public static readonly DependencyProperty ContentTemplateSelectorProperty =
        DependencyProperty.Register("ContentTemplateSelector", typeof(DataTemplateSelector), typeof(TabControl),
            new FrameworkPropertyMetadata((DataTemplateSelector?)null));

    private DataTemplate? _contentTemplate;

    /// <summary>
    /// Default visual factory for the selected page (matches
    /// System.Windows.Controls.TabControl.ContentTemplate; live-verified on
    /// the 9.0.21 pack as a settable DP defaulting to null). Consulted when
    /// the Selected-level pair resolves nothing, mirroring the computed
    /// SelectedContentTemplate fallback (TabItem content falls back to it).
    /// </summary>
    public DataTemplate? ContentTemplate
    {
        get => _contentTemplate;
        set
        {
            if (ReferenceEquals(_contentTemplate, value)) return;
            _contentTemplate = value;
            SyncSelectedContent();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private DataTemplateSelector? _contentTemplateSelector;

    /// <summary>
    /// Default per-page visual choice (matches
    /// System.Windows.Controls.TabControl.ContentTemplateSelector;
    /// live-verified settable DP defaulting to null). Consulted after the
    /// Selected-level pair; template beats selector within each level.
    /// </summary>
    public DataTemplateSelector? ContentTemplateSelector
    {
        get => _contentTemplateSelector;
        set
        {
            if (ReferenceEquals(_contentTemplateSelector, value)) return;
            _contentTemplateSelector = value;
            SyncSelectedContent();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private Style? _itemContainerStyle;

    /// <summary>
    /// Header-container style (matches ItemsControl.ItemContainerStyle, which
    /// TabControl inherits in WPF): applied to TabItem children, existing and
    /// future, with the tab's own ancestry as the loader-order scope (the
    /// ListBox generated-row idiom). Data items mint no TabItem in the engine
    /// (headers render from data), so the style lands on direct TabItem
    /// visuals; container generation for data items stays a declared
    /// boundary. Null default, like WPF.
    /// </summary>
    public Style? ItemContainerStyle
    {
        get => _itemContainerStyle;
        set
        {
            if (ReferenceEquals(_itemContainerStyle, value)) return;
            _itemContainerStyle = value;
            if (value is null) return;
            foreach (var item in Items)
                if (item is TabItem tab) value.ApplyTo(tab, TabStyleContext());
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Style scope for TabItem children (the ListBox generated-row idiom):
    /// the tab's own ancestry stands in for the loader scope, letting
    /// StaticResource setters resolve app-level keys while the tree lookup
    /// still runs first, like WPF's apply-time lookup.
    /// </summary>
    private StyleApplyContext TabStyleContext()
    {
        var ancestry = new List<FrameworkElement>();
        for (var node = (FrameworkElement?)this; node is not null; node = node.Parent as FrameworkElement)
            ancestry.Add(node);
        ancestry.Reverse();
        return new StyleApplyContext { Scope = ancestry };
    }

    /// <summary>
    /// Effective visual factory for the selected page (Selected-level pair
    /// first, then the Content-level pair; template beats selector within
    /// each level). This is the control-level half of the live-verified WPF
    /// computed SelectedContentTemplate (TabItem.ContentTemplate, else the
    /// TabControl fallback); the engine keeps its settable
    /// SelectedContentTemplate divergence, so the Selected-level pair wins.
    /// </summary>
    private DataTemplate? EffectiveContentTemplate()
    {
        if (SelectedContentTemplate is not null) return SelectedContentTemplate;
        var data = SelectedContent;
        if (data is not null)
        {
            var selected = SelectedContentTemplateSelector?.SelectTemplate(data, this);
            if (selected is not null) return selected;
        }
        if (ContentTemplate is not null) return ContentTemplate;
        if (data is not null) return ContentTemplateSelector?.SelectTemplate(data, this);
        return null;
    }

    /// <summary>Honors a pre-selected page added after its IsSelected flag was set.</summary>
    public override void AddItem(object item)
    {
        base.AddItem(item);
        if (item is TabItem tab)
        {
            ItemContainerStyle?.ApplyTo(tab, TabStyleContext());
            if (tab.IsSelected)
                SelectedItem = tab;
        }
    }

    protected override void OnItemsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        if (SelectedIndex < 0)
        {
            for (var i = 0; i < Items.Count; i++)
            {
                if (IsItemEnabled(Items[i]))
                {
                    SelectedIndex = i;
                    break;
                }
            }
        }
        SyncSelectedContent();
    }

    protected override void OnSelectionChanged(System.Collections.IList removedItems, System.Collections.IList addedItems)
    {
        base.OnSelectionChanged(removedItems, addedItems);
        for (var i = 0; i < Items.Count; i++)
            if (Items[i] is TabItem tab)
                tab.SetSelectedInternal(i == SelectedIndex);
        SyncSelectedContent();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private static bool IsItemEnabled(object item) =>
        item is UIElement element ? element.IsEnabled : true;

    private bool _contentTemplated;
    private object? _contentData;
    private DataTemplate? _contentTemplateUsed;

    private void SyncSelectedContent()
    {
        var direct = SelectedContent as UIElement;
        if (direct is not null)
        {
            // Live visuals host directly (templates never wrap visuals).
            if (ReferenceEquals(_contentVisual, direct) && !_contentTemplated) return;
            DetachContentVisual();
            AttachVisualChild(direct);
            _contentVisual = direct;
            return;
        }
        var data = SelectedContent;
        var template = data is null ? null : EffectiveContentTemplate();
        if (template is null)
        {
            DetachContentVisual();
            return;
        }
        // Fresh visual per data/template pair with the item as DataContext
        // (mirrors ItemsControl.PrepareContainerForItemOverride); a repeat
        // sync for the same pair keeps the live visual.
        if (_contentTemplated && ReferenceEquals(_contentData, data) &&
            ReferenceEquals(_contentTemplateUsed, template))
            return;
        DetachContentVisual();
        var visual = template.LoadContent();
        if (visual is FrameworkElement templated) templated.DataContext = data;
        AttachVisualChild(visual);
        _contentVisual = visual;
        _contentTemplated = true;
        _contentData = data;
        _contentTemplateUsed = template;
    }

    private void DetachContentVisual()
    {
        if (_contentVisual is not null)
        {
            RemoveChild(_contentVisual);
            _contentVisual = null;
        }
        _contentTemplated = false;
        _contentData = null;
        _contentTemplateUsed = null;
    }

    private bool IsVertical => TabStripPlacement is Dock.Left or Dock.Right;

    private static string HeaderText(object item) =>
        item is TabItem tab ? tab.Header?.ToString() ?? string.Empty :
        item is Label label ? label.Content?.ToString() ?? string.Empty :
        item?.ToString() ?? string.Empty;

    /// <summary>Header slot for an item (hosts and tests share it for hit-testing).</summary>
    public WpfRect HeaderRect(int index) =>
        (uint)index < (uint)_headerRects.Count ? _headerRects[index] : default;

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var w = double.IsNaN(Width) ? available.Width : Width;
        var h = double.IsNaN(Height) ? available.Height : Height;
        var fontCss = ComposeFontCss();
        var headerWant = 0.0;
        if (IsVertical)
        {
            foreach (var item in Items)
            {
                var text = HeaderText(item);
                var (tw, _) = TryMeasure(text, fontCss, text.Length * 8, 16);
                headerWant = Math.Max(headerWant, tw + 20);
            }
        }
        else
        {
            foreach (var item in Items)
            {
                var text = HeaderText(item);
                var (tw, _) = TryMeasure(text, fontCss, text.Length * 8, 16);
                headerWant += Math.Max(40, tw + 20);
            }
        }
        var (contentW, contentH) = _contentTemplated && _contentVisual is not null
            ? MeasureVisual(_contentVisual, w, h)
            : SelectedContent is UIElement visual
                ? MeasureVisual(visual, w, h)
                : MeasureText(SelectedContent?.ToString() ?? string.Empty, fontCss);
        var wantW = IsVertical ? headerWant + contentW : Math.Max(headerWant, contentW);
        var wantH = IsVertical ? Math.Max(Items.Count * HeaderExtent, contentH) : HeaderExtent + contentH;
        return new WpfSize(Math.Min(w, wantW), Math.Min(h, wantH));
    }

    private static (double W, double H) MeasureVisual(UIElement visual, double w, double h)
    {
        visual.Measure(new WpfSize(Math.Max(0, w), Math.Max(0, h)));
        return (visual.DesiredSize.Width, visual.DesiredSize.Height);
    }

    private static (double W, double H) MeasureText(string text, string fontCss) =>
        string.IsNullOrEmpty(text)
            ? (0, 0)
            : TryMeasure(text, fontCss, text.Length * 8, 16);

    protected override void ArrangeCore(WpfRect finalRect)
    {
        _headerRects.Clear();
        var fontCss = ComposeFontCss();
        if (IsVertical)
        {
            var headerW = 0.0;
            foreach (var item in Items)
            {
                var (tw, _) = TryMeasure(HeaderText(item), fontCss, HeaderText(item).Length * 8, 16);
                headerW = Math.Max(headerW, tw + 20);
            }
            headerW = Math.Min(headerW, finalRect.Width);
            var contentX = TabStripPlacement == Dock.Left ? finalRect.X + headerW : finalRect.X;
            var headerX = TabStripPlacement == Dock.Left ? finalRect.X : finalRect.X + finalRect.Width - headerW;
            var y = finalRect.Y;
            for (var i = 0; i < Items.Count; i++)
            {
                var slot = new WpfRect(headerX, y, headerW, HeaderExtent);
                _headerRects.Add(slot);
                if (Items[i] is UIElement visual) visual.Arrange(slot);
                y += HeaderExtent;
            }
            ContentRect = new WpfRect(contentX, finalRect.Y,
                Math.Max(0, finalRect.Width - headerW), finalRect.Height);
        }
        else
        {
            var contentY = TabStripPlacement == Dock.Top ? finalRect.Y + HeaderExtent : finalRect.Y;
            var headerY = TabStripPlacement == Dock.Top ? finalRect.Y : finalRect.Y + finalRect.Height - HeaderExtent;
            var x = finalRect.X;
            for (var i = 0; i < Items.Count; i++)
            {
                var text = HeaderText(Items[i]);
                var (tw, _) = TryMeasure(text, fontCss, text.Length * 8, 16);
                var slot = new WpfRect(x, headerY, Math.Max(40, tw + 20), HeaderExtent);
                _headerRects.Add(slot);
                if (Items[i] is UIElement visual) visual.Arrange(slot);
                x += slot.Width;
            }
            ContentRect = new WpfRect(finalRect.X, contentY,
                finalRect.Width, Math.Max(0, finalRect.Height - HeaderExtent));
        }
        if (_contentVisual is not null) _contentVisual.Arrange(ContentRect);
        foreach (UIElement child in Children)
            if (child is ToolTip tip)
                tip.ArrangeTip();
    }

    /// <summary>Selected-content slot from the last arrange (tests share it).</summary>
    internal WpfRect ContentRect { get; private set; }

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        var fontCss = ComposeFontCss();
        var enabled = IsEffectivelyEnabled;
        for (var i = 0; i < Items.Count && i < _headerRects.Count; i++)
        {
            var dimmed = !IsItemEnabled(Items[i]) || !enabled;
            var slot = _headerRects[i];
            TabItem.PaintHeader(dc, slot, i == SelectedIndex, HeaderText(Items[i]), fontCss,
                dimmed ? 0x909090FFu : 0x707070FFu);
            if (dimmed)
            {
                var text = HeaderText(Items[i]);
                if (!string.IsNullOrEmpty(text))
                    dc.DrawText(text, (float)(slot.X + slot.Width / 2), (float)(slot.Y + slot.Height / 2),
                        fontCss, 0x707070FFu, 1, 2, -1);
            }
        }
        var content = ContentRect;
        if (content.Width > 0 && content.Height > 0)
        {
            dc.DrawRectangle((float)content.X, (float)content.Y, (float)content.Width, (float)content.Height, 0x707070FFu);
            if (content.Width > 2 && content.Height > 2)
                dc.DrawRectangle((float)content.X + 1, (float)content.Y + 1, (float)content.Width - 2, (float)content.Height - 2, 0xFFFFFFFFu);
        }
        if (_contentVisual is not null) RenderVisible(dc, _contentVisual);
        else if (SelectedContent is not null)
        {
            var text = SelectedContent.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(text))
                dc.DrawText(text, (float)(content.X + 6), (float)(content.Y + content.Height / 2),
                    fontCss, enabled ? 0x000000FFu : 0x707070FFu, 0, 2, -1);
        }
    }

    public override void OnRoutedEvent(WasmRoutedEvent e)
    {
        if (e.Handled || e.Phase != WasmRoutedPhase.Bubbling || !IsEffectivelyEnabled) return;
        if (e.Source is not Core.WasmInputEvent ev) return;
        switch (ev)
        {
            case Core.WasmInputEvent.Pointer pd when pd.Args.Kind == Core.WasmPointerKind.Down && e.IsLeftButtonDown:
            {
                var at = new WpfPoint(pd.Args.X, pd.Args.Y);
                for (var i = 0; i < _headerRects.Count && i < Items.Count; i++)
                {
                    var slot = _headerRects[i];
                    if (at.X >= slot.X && at.X < slot.X + slot.Width &&
                        at.Y >= slot.Y && at.Y < slot.Y + slot.Height)
                    {
                        if (IsItemEnabled(Items[i]) && SelectedIndex != i)
                            SelectedIndex = i;
                        Focus();
                        e.Handled = true;
                        return;
                    }
                }
                break;
            }
            case Core.WasmInputEvent.Key k when k.Args.IsDown:
            {
                var next = k.Args.Key switch
                {
                    "ArrowRight" or "ArrowDown" => MoveSelection(1),
                    "ArrowLeft" or "ArrowUp" => MoveSelection(-1),
                    _ => false,
                };
                if (next) e.Handled = true;
                break;
            }
        }
    }

    private bool MoveSelection(int delta)
    {
        if (Items.Count == 0) return false;
        var index = SelectedIndex;
        for (var step = 0; step < Items.Count; step++)
        {
            index = (index + delta + Items.Count) % Items.Count;
            if (IsItemEnabled(Items[index]))
            {
                SelectedIndex = index;
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Tab header host (matches System.Windows.Controls.Primitives.TabPanel):
/// a horizontal (or vertical) strip that stacks its children. The engine
/// TabControl paints its own strip, so this panel mainly serves direct
/// XAML use and converted member references (Children, IsItemsHost).
/// </summary>
public class TabPanel : Panel
{
    /// <summary>Stack direction (default Horizontal, like WPF).</summary>
    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (Orientation == Orientation.Vertical)
        {
            var h = 0.0; var w = 0.0;
            foreach (UIElement c in Children)
            {
                c.Measure(new WpfSize(available.Width, available.Height));
                w = Math.Max(w, c.DesiredSize.Width);
                h += c.DesiredSize.Height;
            }
            return new WpfSize(w, h);
        }
        var width = 0.0; var height = 0.0;
        foreach (UIElement c in Children)
        {
            c.Measure(new WpfSize(available.Width, available.Height));
            width += c.DesiredSize.Width;
            height = Math.Max(height, c.DesiredSize.Height);
        }
        return new WpfSize(width, height);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (Orientation == Orientation.Vertical)
        {
            var y = finalRect.Y;
            foreach (UIElement c in Children)
            {
                var h = c.DesiredSize.Height;
                c.Arrange(new WpfRect(finalRect.X, y, finalRect.Width, h));
                y += h;
            }
            return;
        }
        var x = finalRect.X;
        foreach (UIElement c in Children)
        {
            var w = c.DesiredSize.Width;
            c.Arrange(new WpfRect(x, finalRect.Y, w, finalRect.Height));
            x += w;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        foreach (UIElement c in Children) RenderVisible(dc, c);
    }
}

/// <summary>
/// Progress indicator (matches System.Windows.Controls.ProgressBar): a
/// RangeBase with a painted fill fraction. Converted Circular/Extended
/// ProgressBar subclasses bind ValueChanged (inherited) and their own
/// Percentage/State DPs; the encoder list template binds Minimum, Maximum,
/// Value and IsIndeterminate. Defaults match WPF (0..100).
/// </summary>
public class ProgressBar : RangeBase
{
    static ProgressBar()
    {
        // Progress indicators never take keyboard focus (live-verified WPF default).
        FocusableProperty.OverrideMetadata(typeof(ProgressBar), new FrameworkPropertyMetadata(false));
    }

    public ProgressBar()
    {
        Maximum = 100;
    }

    /// <summary>Fill direction (default Horizontal, like WPF).</summary>
    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    /// <summary>Marquee identifier (matches System.Windows.Controls.ProgressBar.IsIndeterminateProperty).</summary>
    public static readonly DependencyProperty IsIndeterminateProperty =
        DependencyProperty.Register("IsIndeterminate", typeof(bool), typeof(ProgressBar), false);

    /// <summary>Marquee block instead of a value fill (static block; no clock).</summary>
    public bool IsIndeterminate
    {
        get => (bool?)GetValue(IsIndeterminateProperty) ?? false;
        set => SetValue(IsIndeterminateProperty, value);
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        var w = double.IsNaN(Width) ? available.Width : Width;
        var h = double.IsNaN(Height) ? available.Height : Height;
        return Orientation == Orientation.Horizontal
            ? new WpfSize(Math.Min(w, Math.Max(100, w)), double.IsNaN(Height) ? Math.Min(h, 14) : h)
            : new WpfSize(double.IsNaN(Width) ? Math.Min(w, 14) : w, Math.Min(h, Math.Max(100, h)));
    }

    /// <summary>Fill rect inside the arranged slot for the current value.</summary>
    internal WpfRect FillRect() => FillRect(TrackRect());

    /// <summary>Track rect (one pixel inside the border).</summary>
    internal WpfRect TrackRect()
    {
        var r = RenderRect;
        return new WpfRect(r.X + 1, r.Y + 1, Math.Max(0, r.Width - 2), Math.Max(0, r.Height - 2));
    }

    private WpfRect FillRect(WpfRect track)
    {
        var t = Maximum > Minimum ? (Value - Minimum) / (Maximum - Minimum) : 0;
        t = Math.Clamp(t, 0, 1);
        return Orientation == Orientation.Horizontal
            ? new WpfRect(track.X, track.Y, track.Width * t, track.Height)
            : new WpfRect(track.X, track.Y + track.Height * (1 - t), track.Width, track.Height * t);
    }

    /// <summary>Marquee block rect (centered quarter of the track).</summary>
    internal WpfRect MarqueeRect() => MarqueeRect(TrackRect());

    private WpfRect MarqueeRect(WpfRect track) =>
        Orientation == Orientation.Horizontal
            ? new WpfRect(track.X + track.Width * 3 / 8, track.Y, track.Width / 4, track.Height)
            : new WpfRect(track.X, track.Y + track.Height * 3 / 8, track.Width, track.Height / 4);

    protected override void OnRender(DrawingContext dc)
    {
        var r = RenderRect;
        if (r.Width <= 0 || r.Height <= 0) return;
        var enabled = IsEffectivelyEnabled;
        dc.DrawRectangle((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height,
            enabled ? 0x707070FFu : 0x909090FFu);
        var track = new WpfRect(r.X + 1, r.Y + 1, Math.Max(0, r.Width - 2), Math.Max(0, r.Height - 2));
        if (track.Width > 0 && track.Height > 0)
        {
            dc.DrawRectangle((float)track.X, (float)track.Y, (float)track.Width, (float)track.Height,
                enabled ? 0xE5E5E5FFu : 0xCCCCCCFFu);
            var fill = IsIndeterminate ? MarqueeRect(track) : FillRect(track);
            if (fill.Width > 0 && fill.Height > 0)
                dc.DrawRectangle((float)fill.X, (float)fill.Y, (float)fill.Width, (float)fill.Height,
                    enabled ? 0x0078D7FFu : 0x909090FFu);
        }
    }
}

/// <summary>
/// Hover card (matches System.Windows.Controls.ToolTip): a ContentControl
/// that paints above the tree when open. String content paints as text;
/// visual content hosts live. Closed tips take no layout space, never
/// hit-test (clicks pass through, like real tooltips) and paint only in
/// the overlay post-pass, so owners never reflow around them.
/// </summary>
public class ToolTip : ContentControl
{
    public override object Content
    {
        get => base.Content;
        set
        {
            if (Equals(base.Content, value)) return;
            if (base.Content is UIElement oldVisual) DetachVisualChild(oldVisual);
            base.Content = value;
            if (value is UIElement visual) AttachVisualChild(visual);
            InvalidateMeasure();
        }
    }

    /// <summary>Anchor rule (default Bottom, like Popup).</summary>
    public PlacementMode Placement { get; set; } = PlacementMode.Bottom;

    /// <summary>
    /// Custom placement hook (matches ToolTip.CustomPopupPlacementCallbackProperty;
    /// the WPF-identical shared Popup instance, so qualified setters bind; the
    /// CLR wrapper stays store-backed).
    /// </summary>
    public static readonly DependencyProperty CustomPopupPlacementCallbackProperty =
        Popup.CustomPopupPlacementCallbackProperty.AddOwner(typeof(ToolTip));

    /// <summary>Custom placement hook (store-backed; null keeps the anchor rule).</summary>
    public CustomPopupPlacementCallback? CustomPopupPlacementCallback
    {
        get => (CustomPopupPlacementCallback?)GetValue(CustomPopupPlacementCallbackProperty);
        set => SetValue(CustomPopupPlacementCallbackProperty, value);
    }

    /// <summary>Anchor element (null anchors to the placement target, else the parent).</summary>
    public UIElement? PlacementTarget { get; set; }

    /// <summary>Anchor box override (non-empty replaces the target rect).</summary>
    public Rect PlacementRectangle { get; set; } = Rect.Empty;

    /// <summary>Horizontal nudge in pixels.</summary>
    public double HorizontalOffset { get; set; }

    /// <summary>Vertical nudge in pixels.</summary>
    public double VerticalOffset { get; set; }

    /// <summary>Whether outside presses leave the tip open (default true, like WPF).</summary>
    public bool StaysOpen { get; set; } = true;

    private bool _isOpen;

    /// <summary>Raised after IsOpen becomes true (matches ToolTip.Opened).</summary>
    public event EventHandler? Opened;

    /// <summary>Raised after IsOpen becomes false (matches ToolTip.Closed).</summary>
    public event EventHandler? Closed;

    /// <summary>Open state (inline paint stays off; the overlay pass paints open tips).</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value) return;
            _isOpen = value;
            if (value) Opened?.Invoke(this, EventArgs.Empty);
            else Closed?.Invoke(this, EventArgs.Empty);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public override bool Focusable { get; set; }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        // Overlay floats: the tip itself never takes layout space, but the
        // content still measures so placement has a size to anchor.
        if (Content is UIElement visual) visual.Measure(available);
        return new WpfSize(0, 0);
    }

    private WpfSize DesiredTextSize(WpfSize available)
    {
        var text = Content?.ToString() ?? string.Empty;
        var (w, h) = TryMeasure(text, ComposeFontCss(), text.Length * 8, 16);
        return new WpfSize(Math.Min(available.Width, w + 16), h + 12);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        ArrangeTip();
    }

    /// <summary>Places the content at the anchored tip rect.</summary>
    internal void ArrangeTip()
    {
        if (Content is UIElement visual) visual.Arrange(TipRect());
    }

    public override bool HitTest(WpfPoint point) => false;

    protected override void OnRender(DrawingContext dc)
    {
    }

    /// <summary>Overlay paint: open tips render above the whole tree.</summary>
    internal void RenderOverlay(DrawingContext dc)
    {
        if (!IsOpen || Visibility != Visibility.Visible) return;
        var rect = TipRect();
        if (rect.Width <= 0 || rect.Height <= 0) return;
        dc.DrawRectangle((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, 0x767676FFu);
        if (rect.Width <= 2 || rect.Height <= 2) return;
        dc.DrawRectangle((float)rect.X + 1, (float)rect.Y + 1, (float)rect.Width - 2, (float)rect.Height - 2, 0xFFFFE1FFu);
        var fontCss = ComposeFontCss();
        if (Content is UIElement visual) RenderVisible(dc, visual);
        else if (Content is not null)
        {
            var text = Content.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(text))
                dc.DrawText(text, (float)rect.X + 8, (float)(rect.Y + rect.Height / 2),
                    fontCss, 0x000000FFu, 0, 2, -1);
        }
    }

    /// <summary>Tip rect in root coordinates for the current placement.</summary>
    internal WpfRect TipRect()
    {
        var contentW = 0.0; var contentH = 0.0;
        if (Content is UIElement visual)
        {
            contentW = Math.Max(0, visual.DesiredSize.Width);
            contentH = Math.Max(0, visual.DesiredSize.Height);
        }
        else if (Content is not null)
        {
            var size = DesiredTextSize(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
            contentW = size.Width; contentH = size.Height;
        }
        var w = contentW; var h = contentH;
        double anchorX, anchorY, anchorW, anchorH;
        if (PlacementRectangle.Width > 0 && PlacementRectangle.Height > 0)
        {
            anchorX = PlacementRectangle.X; anchorY = PlacementRectangle.Y;
            anchorW = PlacementRectangle.Width; anchorH = PlacementRectangle.Height;
        }
        else
        {
            var anchor = PlacementTarget?.RenderRect ?? (Parent as FrameworkElement)?.RenderRect ?? RenderRect;
            anchorX = anchor.X; anchorY = anchor.Y; anchorW = anchor.Width; anchorH = anchor.Height;
        }
        double x, y;
        switch (Placement)
        {
            case PlacementMode.Top:
                x = anchorX + HorizontalOffset;
                y = anchorY - h + VerticalOffset;
                break;
            case PlacementMode.Left:
                x = anchorX - w + HorizontalOffset;
                y = anchorY + VerticalOffset;
                break;
            case PlacementMode.Right:
                x = anchorX + anchorW + HorizontalOffset;
                y = anchorY + VerticalOffset;
                break;
            case PlacementMode.Center:
                x = anchorX + (anchorW - w) / 2 + HorizontalOffset;
                y = anchorY + (anchorH - h) / 2 + VerticalOffset;
                break;
            case PlacementMode.Mouse:
            case PlacementMode.MousePoint:
            {
                var mouse = WpfInputManager.Current?.LastPointerPosition ?? new WpfPoint(0, 0);
                x = mouse.X + HorizontalOffset;
                y = mouse.Y + VerticalOffset;
                break;
            }
            case PlacementMode.Absolute:
            case PlacementMode.AbsolutePoint:
                x = HorizontalOffset;
                y = VerticalOffset;
                break;
            case PlacementMode.Relative:
            case PlacementMode.RelativePoint:
                x = anchorX + HorizontalOffset;
                y = anchorY + VerticalOffset;
                break;
            default: // Bottom (and Custom, which needs a callback)
                x = anchorX + HorizontalOffset;
                y = anchorY + anchorH + VerticalOffset;
                break;
        }
        return new WpfRect(x, y, Math.Max(0, w), Math.Max(0, h));
    }
}

/// <summary>
/// Hover-hint service (matches System.Windows.Controls.ToolTipService):
/// attached ToolTip/Placement/offsets plus the ShowDuration knob the
/// original App startup overrides. Hovering an owner with a hint opens its
/// tip; leaving closes it. XAML attributes land in the shared attached
/// bag, so code Get/Set reads the dependency store first and falls back
/// to the parsed bag value.
/// </summary>
public static class ToolTipService
{
    public static readonly DependencyProperty ToolTipProperty =
        DependencyProperty.RegisterAttached("ToolTip", typeof(object), typeof(ToolTipService),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.RegisterAttached("Placement", typeof(PlacementMode), typeof(ToolTipService),
            new PropertyMetadata(PlacementMode.Bottom));

    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.RegisterAttached("HorizontalOffset", typeof(double), typeof(ToolTipService),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.RegisterAttached("VerticalOffset", typeof(double), typeof(ToolTipService),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty ShowDurationProperty =
        DependencyProperty.RegisterAttached("ShowDuration", typeof(int), typeof(ToolTipService),
            new PropertyMetadata(5000));

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(ToolTipService),
            new PropertyMetadata(true));

    private static readonly HashSet<FrameworkElement> Wired = new();
    private static readonly object WiredGate = new();

    public static object? GetToolTip(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.GetValue(ToolTipProperty) is { } tip) return tip;
        if (element is UIElement ui && ui.GetAttached("ToolTipService.ToolTip") is { } raw) return raw;
        if (element is FrameworkElement fe && fe.ToolTip is { } own) return own;
        return null;
    }

    public static void SetToolTip(DependencyObject element, object? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ToolTipProperty, value);
        if (element is FrameworkElement fe)
        {
            fe.ToolTip = value;
            // A ToolTip element parents like the XAML property-element path
            // so the overlay post-pass can paint it.
            if (value is UIElement visual && visual.Parent is null) fe.AttachVisualChild(visual);
            EnsureWired(fe);
        }
    }

    public static PlacementMode GetPlacement(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        // The XAML loader and Set* calls both land in the shared attached
        // bag; value-type DPs cannot tell unset from default, so the bag
        // (explicit intent) wins over the store.
        if (element is UIElement ui && ui.GetAttached("ToolTipService.Placement") is { } raw)
        {
            if (raw is PlacementMode parsed) return parsed;
            if (raw is string text && Enum.TryParse<PlacementMode>(text, ignoreCase: true, out var named))
                return named;
        }
        if (element.GetValue(PlacementProperty) is PlacementMode mode) return mode;
        return PlacementMode.Bottom;
    }

    public static void SetPlacement(DependencyObject element, PlacementMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PlacementProperty, value);
        if (element is UIElement ui) ui.SetAttached("ToolTipService.Placement", value);
        if (element is FrameworkElement fe) EnsureWired(fe);
    }

    public static double GetHorizontalOffset(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement ui && AttachedDouble(ui, "ToolTipService.HorizontalOffset") is { } parsed)
            return parsed;
        if (element.GetValue(HorizontalOffsetProperty) is double d) return d;
        return 0;
    }

    public static void SetHorizontalOffset(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(HorizontalOffsetProperty, value);
        if (element is UIElement ui) ui.SetAttached("ToolTipService.HorizontalOffset", value);
        if (element is FrameworkElement fe) EnsureWired(fe);
    }

    public static double GetVerticalOffset(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement ui && AttachedDouble(ui, "ToolTipService.VerticalOffset") is { } parsed)
            return parsed;
        if (element.GetValue(VerticalOffsetProperty) is double d) return d;
        return 0;
    }

    public static void SetVerticalOffset(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(VerticalOffsetProperty, value);
        if (element is UIElement ui) ui.SetAttached("ToolTipService.VerticalOffset", value);
        if (element is FrameworkElement fe) EnsureWired(fe);
    }

    public static int GetShowDuration(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.GetValue(ShowDurationProperty) is int ms) return ms;
        return 5000;
    }

    public static void SetShowDuration(DependencyObject element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ShowDurationProperty, value);
    }

    public static bool GetIsEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.GetValue(IsEnabledProperty) is bool enabled) return enabled;
        return true;
    }

    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsEnabledProperty, value);
    }

    private static double? AttachedDouble(UIElement element, string name) =>
        element.GetAttached(name) switch
        {
            double d => d,
            int i => (double)i,
            string text when double.TryParse(text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };

    /// <summary>Hooks hover open/close for an owner (loader calls this for XAML hints).</summary>
    internal static void EnsureWired(FrameworkElement owner)
    {
        lock (WiredGate)
        {
            if (!Wired.Add(owner)) return;
        }
        owner.MouseEnter += (_, _) => Show(owner);
        owner.MouseLeave += (_, _) => Hide(owner);
    }

    /// <summary>Opens the owner's tip (string hints inflate a shared card).</summary>
    public static void Show(FrameworkElement owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        EnsureWired(owner);
        if (!GetIsEnabled(owner)) return;
        var hint = GetToolTip(owner);
        if (hint is null) return;
        var tip = hint as ToolTip ?? FindSharedTip(owner) ?? AttachSharedTip(owner);
        if (hint is not ToolTip) tip.Content = hint;
        // Service-attached values win when the owner carries them (XAML or
        // Set* calls land in the shared bag); otherwise a configured
        // ToolTip element keeps its own placement and offsets.
        if (HasAttached(owner, "ToolTipService.Placement")) tip.Placement = GetPlacement(owner);
        if (HasAttached(owner, "ToolTipService.HorizontalOffset")) tip.HorizontalOffset = GetHorizontalOffset(owner);
        if (HasAttached(owner, "ToolTipService.VerticalOffset")) tip.VerticalOffset = GetVerticalOffset(owner);
        tip.PlacementTarget = owner;
        // Owners never measure the floating tip, so size and place it here
        // against the already-arranged anchor.
        tip.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        tip.ArrangeTip();
        tip.IsOpen = true;
    }

    private static bool HasAttached(UIElement element, string name) =>
        element.GetAttached(name) is not null;

    /// <summary>Closes the owner's tip.</summary>
    public static void Hide(FrameworkElement owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (GetToolTip(owner) is ToolTip tip) tip.IsOpen = false;
        else if (FindSharedTip(owner) is { } shared) shared.IsOpen = false;
    }

    private static ToolTip? FindSharedTip(FrameworkElement owner)
    {
        foreach (var child in owner.LogicalChildren)
            if (child is ToolTip tip && ReferenceEquals(tip.PlacementTarget, owner))
                return tip;
        return null;
    }

    private static ToolTip AttachSharedTip(FrameworkElement owner)
    {
        var tip = new ToolTip { PlacementTarget = owner };
        owner.AttachVisualChild(tip);
        return tip;
    }
}
