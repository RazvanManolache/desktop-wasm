using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-72 controls tests: weak events, item-generation notices,
/// bring-into-view routing, layout slots, spelling, parser context, the
/// DataGrid column remainder and the ToggleButton IsChecked DP re-route.</summary>
[ResetAmbientState]
public sealed class Slice72ControlsTests
{
    private sealed class Recorder : IWeakEventListener
    {
        public readonly List<EventArgs> Seen = new();
        public bool Answer = true;

        public bool ReceiveWeakEvent(Type managerType, object? sender, EventArgs e)
        {
            Seen.Add(e);
            return Answer;
        }
    }

    [Fact]
    public void CollectionChanged_add_handler_fires_and_removes()
    {
        var items = new ObservableCollection<string>();
        var seen = new List<NotifyCollectionChangedEventArgs>();
        NotifyCollectionChangedEventHandler handler = (_, e) => seen.Add(e);
        CollectionChangedEventManager.AddHandler(items, handler);
        items.Add("a");
        var delivered = Assert.Single(seen);
        Assert.Equal(NotifyCollectionChangedAction.Add, delivered.Action);
        CollectionChangedEventManager.RemoveHandler(items, handler);
        items.Add("b");
        Assert.Single(seen);
    }

    [Fact]
    public void CollectionChanged_listener_receives_and_false_unsubscribes()
    {
        var items = new ObservableCollection<string>();
        var recorder = new Recorder();
        CollectionChangedEventManager.AddListener(items, recorder);
        items.Add("a");
        Assert.Single(recorder.Seen);
        recorder.Answer = false;
        items.Add("b");
        Assert.Equal(2, recorder.Seen.Count);
        items.Add("c");
        Assert.Equal(2, recorder.Seen.Count);
        CollectionChangedEventManager.RemoveListener(items, recorder);
    }

    [Fact]
    public void GeneratorPosition_round_trips()
    {
        var position = new GeneratorPosition(3, 1);
        Assert.Equal(3, position.Index);
        Assert.Equal(1, position.Offset);
        Assert.Equal(position, new GeneratorPosition(3, 1));
        Assert.NotEqual(position, new GeneratorPosition(3, 0));
        Assert.Contains("3", position.ToString());
    }

    [Fact]
    public void ItemContainerGenerator_raises_items_changed()
    {
        var control = new ItemsControl();
        var events = new List<ItemsChangedEventArgs>();
        control.ItemContainerGenerator.ItemsChanged += (_, e) => events.Add(e);
        var container = control.ItemContainerGenerator.ContainerFromItem("a");
        Assert.NotNull(container);
        var generated = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Add, generated.Action);
        Assert.Equal(1, generated.ItemCount);
        Assert.Equal(1, generated.ItemUICount);
        control.ItemContainerGenerator.Refresh();
        var reset = Assert.Single(events, e => e.Action == NotifyCollectionChangedAction.Reset);
        Assert.Equal(0, reset.ItemCount);
    }

    [Fact]
    public void BringIntoView_raises_route_with_target()
    {
        var parent = new StackPanel();
        var child = new TextBlock();
        parent.Children.Add(child);
        RequestBringIntoViewEventArgs? bubbled = null;
        parent.AddHandler(FrameworkElement.RequestBringIntoViewEvent,
            new RequestBringIntoViewEventHandler((_, e) => bubbled = e));
        RequestBringIntoViewEventArgs? direct = null;
        child.RequestBringIntoView += (_, e) => direct = e;
        child.BringIntoView();
        Assert.NotNull(direct);
        Assert.Same(child, direct.TargetObject);
        Assert.NotNull(bubbled);
        Assert.Same(child, bubbled.TargetObject);
        Assert.Same(FrameworkElement.RequestBringIntoViewEvent, direct.RoutedEvent);
    }

    [Fact]
    public void BringIntoView_rect_overload_carries_rect_and_handled_skips_scroll()
    {
        var scroller = new ScrollViewer();
        var panel = new StackPanel();
        panel.Children.Add(new Border { Height = 200, Width = 100 });
        var child = new TextBlock();
        panel.Children.Add(child);
        scroller.Content = panel;
        scroller.Measure(new WpfSize(100, 100));
        scroller.Arrange(new WpfRect(0, 0, 100, 100));
        Assert.True(scroller.ScrollableHeight > 0);
        scroller.ScrollToVerticalOffset(40);
        Assert.Equal(40, scroller.VerticalOffset);
        var rect = new Rect(0, 0, 10, 10);
        RequestBringIntoViewEventArgs? seen = null;
        child.RequestBringIntoView += (_, e) => { seen = e; e.Handled = true; };
        child.BringIntoView(rect);
        Assert.NotNull(seen);
        Assert.Equal(rect, seen.TargetRect);
        Assert.Equal(40, scroller.VerticalOffset);
    }

    [Fact]
    public void BringIntoView_unhandled_scrolls_child_into_view()
    {
        var scroller = new ScrollViewer();
        var panel = new StackPanel();
        panel.Children.Add(new Border { Height = 200, Width = 100 });
        var child = new TextBlock();
        panel.Children.Add(child);
        scroller.Content = panel;
        scroller.Measure(new WpfSize(100, 100));
        scroller.Arrange(new WpfRect(0, 0, 100, 100));
        Assert.Equal(0, scroller.VerticalOffset);
        child.BringIntoView();
        Assert.True(scroller.VerticalOffset > 0);
    }

    [Fact]
    public void LayoutInformation_reports_arranged_slot()
    {
        var element = new TextBlock();
        element.Measure(new WpfSize(120, 30));
        element.Arrange(new WpfRect(5, 7, 120, 30));
        var slot = LayoutInformation.GetLayoutSlot(element);
        Assert.Equal(new Rect(5, 7, 120, 30), slot);
        Assert.Null(LayoutInformation.GetLayoutClip(element));
        Assert.Null(LayoutInformation.GetLayoutExceptionElement(Dispatcher.CurrentDispatcher));
        Assert.Throws<ArgumentNullException>(() => LayoutInformation.GetLayoutSlot(null!));
    }

    [Fact]
    public void SpellCheck_switch_round_trips_per_editor()
    {
        var first = new TextBox();
        var second = new TextBox();
        Assert.False(SpellCheck.GetIsEnabled(first));
        SpellCheck.SetIsEnabled(first, true);
        Assert.True(SpellCheck.GetIsEnabled(first));
        Assert.False(SpellCheck.GetIsEnabled(second));
        Assert.Empty(SpellCheck.GetCustomDictionaries(first));
        SpellCheck.SetSpellingReform(first, SpellingReform.Postreform);
        Assert.Equal(1, (int)SpellingReform.Prereform);
        Assert.Equal(2, (int)SpellingReform.Postreform);
        Assert.Throws<ArgumentNullException>(() => SpellCheck.GetIsEnabled(null!));
    }

    [Fact]
    public void SpellingError_correct_and_ignore()
    {
        var error = new SpellingError(new[] { "color", "colour" });
        Assert.Equal(new[] { "color", "colour" }, error.Suggestions);
        Assert.False(error.IsCorrected);
        error.Correct("color");
        Assert.True(error.IsCorrected);
        error.IgnoreAll();
        Assert.True(error.IsIgnored);
        Assert.Throws<ArgumentNullException>(() => error.Correct(null!));
    }

    [Fact]
    public void XmlnsDictionary_scopes_and_seals()
    {
        var map = new XmlnsDictionary();
        map.Add("p", "urn:demo");
        Assert.Equal("urn:demo", map.LookupNamespace("p"));
        Assert.Equal("urn:demo", map["p"]);
        Assert.Equal("p", map.LookupPrefix("urn:demo"));
        Assert.Equal("http://www.w3.org/XML/1998/namespace", map.LookupNamespace("xml"));
        map.PushScope();
        map.Add("p", "urn:inner");
        Assert.Equal("urn:inner", map.LookupNamespace("p"));
        map.PopScope();
        Assert.Equal("urn:demo", map.LookupNamespace("p"));
        var copy = new XmlnsDictionary(map);
        Assert.Equal("urn:demo", copy.LookupNamespace("p"));
        copy.Seal();
        Assert.True(copy.Sealed);
        Assert.Throws<InvalidOperationException>(() => copy.Add("q", "urn:q"));
        Assert.Equal(string.Empty, map.DefaultNamespace());
        map.Add(string.Empty, "urn:default");
        Assert.Equal("urn:default", map.DefaultNamespace());
        Assert.True(map.Contains("p"));
        Assert.False(map.Contains("missing"));
    }

    [Fact]
    public void ParserContext_round_trips_xml_context()
    {
        var context = new ParserContext
        {
            BaseUri = new Uri("https://example.test/app.xaml"),
            XmlLang = "en-us",
            XmlSpace = "preserve",
            XamlTypeMapper = new XamlTypeMapper(),
        };
        context.XmlnsDictionary.Add("p", "urn:demo");
        var xml = ParserContext.ToXmlParserContext(context);
        Assert.Equal("en-us", xml.XmlLang);
        Assert.Equal(System.Xml.XmlSpace.Preserve, xml.XmlSpace);
        Assert.Equal("urn:demo", xml.NamespaceManager.LookupNamespace("p"));
        var back = new ParserContext(xml);
        Assert.Equal("preserve", back.XmlSpace);
        Assert.NotNull(back.XmlnsDictionary);
        Assert.Throws<ArgumentNullException>(() => ParserContext.ToXmlParserContext(null!));
    }

    [Fact]
    public void ComboBoxColumn_generates_bound_combo()
    {
        var column = new DataGridComboBoxColumn
        {
            ItemsSource = new[] { "red", "green" },
            DisplayMemberPath = "Length",
            SelectedValuePath = "Length",
            SelectedItemBinding = new Binding("Color"),
        };
        var cell = new DataGridCell();
        var box = Assert.IsType<ComboBox>(column.GenerateElement(cell, "red"));
        Assert.Same(column.ItemsSource, box.ItemsSource);
        Assert.Equal("Length", box.DisplayMemberPath);
        Assert.NotNull(box.GetBindingExpression(Selector.SelectedItemProperty));
        var editing = Assert.IsType<ComboBox>(column.GenerateEditingElement(cell, "red"));
        Assert.True(column.CommitCellEdit(editing));
        column.CancelCellEdit(editing, "red");
        Assert.Equal(DataGridComboBoxColumn.DefaultElementStyle, column.ElementStyle);
        Assert.Null(DataGridComboBoxColumn.DefaultElementStyle);
        Assert.NotNull(DataGridComboBoxColumn.TextBlockComboBoxStyleKey);
        var style = new Style();
        column.ElementStyle = style;
        Assert.Same(style, column.ElementStyle);
        Assert.Same(style, column.GetValue(DataGridComboBoxColumn.ElementStyleProperty));
    }

    [Fact]
    public void CheckBoxColumn_generates_bound_check()
    {
        var column = new DataGridCheckBoxColumn { IsThreeState = true, Binding = new Binding("Done") };
        var cell = new DataGridCell();
        var check = Assert.IsType<CheckBox>(column.GenerateElement(cell, dataItem: null));
        Assert.True(check.IsThreeState);
        Assert.NotNull(check.GetBindingExpression(ToggleButton.IsCheckedProperty));
        Assert.True(column.CommitCellEdit(check));
        column.CancelCellEdit(check, false);
        Assert.Null(DataGridCheckBoxColumn.DefaultElementStyle);
    }

    [Fact]
    public void TemplateColumn_inflates_and_null_template_stays_null()
    {
        var empty = new DataGridTemplateColumn();
        Assert.Null(empty.GenerateElement(new DataGridCell(), dataItem: null));
        var column = new DataGridTemplateColumn
        {
            CellTemplate = new DataTemplate(
                "<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Text=\"hi\" />"),
        };
        var element = Assert.IsType<TextBlock>(column.GenerateElement(new DataGridCell(), "row"));
        Assert.Equal("hi", element.Text);
        Assert.Equal("row", element.DataContext);
    }

    [Fact]
    public void AutoGeneratingColumnEventArgs_carries_column()
    {
        var column = new DataGridCheckBoxColumn();
        var args = new DataGridAutoGeneratingColumnEventArgs("Done", typeof(bool), column);
        Assert.Equal("Done", args.PropertyName);
        Assert.Equal(typeof(bool), args.PropertyType);
        Assert.Same(column, args.Column);
        Assert.False(args.Cancel);
        args.Cancel = true;
        Assert.True(args.Cancel);
    }

    [Fact]
    public void ToggleButton_ischecked_is_store_backed_and_raises_once()
    {
        var toggle = new ToggleButton();
        var events = new List<string>();
        toggle.Checked += (_, _) => events.Add("checked");
        toggle.Unchecked += (_, _) => events.Add("unchecked");
        Assert.Equal(false, toggle.IsChecked);
        toggle.IsChecked = true;
        Assert.Equal(true, toggle.IsChecked);
        Assert.Equal(new[] { "checked" }, events);
        toggle.SetValue(ToggleButton.IsCheckedProperty, true);
        Assert.Equal(new[] { "checked" }, events);
        toggle.IsChecked = null;
        Assert.Null(toggle.IsChecked);
    }
}
