using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;

namespace DesktopWasm.Wpf.Tests;

/// <summary>CollectionViewSource/ICollectionView engine slice: default-view
/// caching, filter/sort/group/currency/defer semantics plus the exact OxyPlot
/// ExampleBrowser boot sequence (GetDefaultView + PropertyGroupDescription)
/// and live ItemsControl binding over a view.</summary>
[ResetAmbientState]
public sealed class CollectionViewSourceTests
{
    private sealed class Example
    {
        public Example(string category, string name, int order)
        {
            Category = category;
            Name = name;
            Order = order;
        }

        public string Category { get; }
        public string Name { get; }
        public int Order { get; }
    }

    private static List<Example> Sample() =>
    [
        new("Plot", "Line", 2),
        new("Axes", "Log", 1),
        new("Plot", "Bar", 1),
        new("Axes", "Linear", 3),
    ];

    [Fact]
    public void GetDefaultView_caches_one_view_per_source()
    {
        var source = Sample();
        var first = CollectionViewSource.GetDefaultView(source);
        var second = CollectionViewSource.GetDefaultView(source);
        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.IsType<ListCollectionView>(first);
        Assert.Same(source, first.SourceCollection);
        Assert.True(CollectionViewSource.IsDefaultView(first));
        Assert.False(CollectionViewSource.IsDefaultView(new ListCollectionView(source)));
        Assert.True(CollectionViewSource.IsDefaultView(null));
    }

    [Fact]
    public void GetDefaultView_covers_shapes()
    {
        Assert.Null(CollectionViewSource.GetDefaultView(null));
        Assert.Null(CollectionViewSource.GetDefaultView(42));
        var enumerable = Sample().Where(_ => true);
        var view = CollectionViewSource.GetDefaultView(enumerable);
        Assert.NotNull(view);
        Assert.IsType<CollectionView>(view);
        Assert.NotNull(CollectionViewSource.GetDefaultView(view));
        Assert.Same(view, CollectionViewSource.GetDefaultView(view));
    }

    [Fact]
    public void OxyPlot_entry_sequence_groups_by_category()
    {
        // Exact MainWindowViewModel..ctor call sequence from the staged
        // ExampleBrowser.WPF assembly: GetDefaultView(ordered) then
        // GroupDescriptions.Add(new PropertyGroupDescription("Category")).
        var examples = Sample().OrderBy(e => e.Category);
        var view = CollectionViewSource.GetDefaultView(examples);
        Assert.NotNull(view);
        view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        var groups = view.Groups;
        Assert.NotNull(groups);
        Assert.Equal(2, groups.Count);
        var names = groups.OfType<CollectionViewGroup>().Select(g => g.Name).ToList();
        Assert.Contains("Axes", names);
        Assert.Contains("Plot", names);
        var axes = groups.OfType<CollectionViewGroup>().Single(g => Equals(g.Name, "Axes"));
        Assert.Equal(2, axes.ItemCount);
        Assert.True(axes.IsBottomLevel);
        Assert.Equal(4, view.Cast<object>().Count());
        Assert.False(view.IsEmpty);
        Assert.Equal("Log", ((Example)view.CurrentItem!).Name);
    }

    [Fact]
    public void Filter_narrows_view_and_currency_follows()
    {
        var view = new ListCollectionView(Sample());
        Assert.Equal(4, view.Count);
        view.Filter = item => ((Example)item!).Category == "Plot";
        Assert.Equal(2, view.Count);
        Assert.All(view.Cast<object>(), item => Assert.Equal("Plot", ((Example)item!).Category));
        Assert.True(view.PassesFilter(view.CurrentItem));
        view.Filter = null;
        Assert.Equal(4, view.Count);
    }

    [Fact]
    public void SortDescriptions_order_view_stably()
    {
        var view = new ListCollectionView(Sample());
        view.SortDescriptions.Add(new SortDescription("Order", ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Descending));
        var names = view.Cast<Example>().Select(e => e.Name).ToList();
        Assert.Equal(["Log", "Bar", "Line", "Linear"], names);
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription("Order", ListSortDirection.Descending));
        Assert.Equal(3, ((Example)view.Cast<Example>().First()).Order);
    }

    [Fact]
    public void Sort_missing_property_sorts_null_first()
    {
        var view = new ListCollectionView(new object?[] { "bb", null, "a" });
        view.SortDescriptions.Add(new SortDescription("Length", ListSortDirection.Ascending));
        Assert.Equal([null, "a", "bb"], view.Cast<object?>().ToList());
    }

    [Fact]
    public void DeferRefresh_batches_single_reset_and_blocks_queries()
    {
        var view = new ListCollectionView(Sample());
        var resets = 0;
        view.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
        };
        using (view.DeferRefresh())
        {
            view.Filter = item => ((Example)item!).Order > 1;
            view.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
            Assert.Throws<InvalidOperationException>(() => view.Count);
            Assert.Throws<InvalidOperationException>(() => view.CurrentItem);
            Assert.Throws<InvalidOperationException>(() => view.GetEnumerator());
        }
        Assert.Equal(1, resets);
        Assert.Equal(2, view.Count);
        using (view.DeferRefresh())
        using (view.DeferRefresh())
        {
            view.Filter = null;
        }
        Assert.Equal(4, view.Count);
    }

    [Fact]
    public void Currency_moves_with_cancel_and_boundaries()
    {
        var view = new ListCollectionView(new object[] { "a", "b", "c" });
        Assert.Equal("a", view.CurrentItem);
        Assert.Equal(0, view.CurrentPosition);
        Assert.False(view.IsCurrentBeforeFirst);
        Assert.False(view.IsCurrentAfterLast);
        var changed = 0;
        view.CurrentChanged += (_, _) => changed++;
        Assert.True(view.MoveCurrentToNext());
        Assert.Equal("b", view.CurrentItem);
        Assert.True(view.MoveCurrentToPrevious());
        Assert.Equal("a", view.CurrentItem);
        Assert.True(view.MoveCurrentToPosition(2));
        Assert.False(view.MoveCurrentToNext());
        Assert.Null(view.CurrentItem);
        Assert.True(view.IsCurrentAfterLast);
        Assert.Equal(3, view.CurrentPosition);
        Assert.False(view.MoveCurrentToPosition(-1));
        Assert.True(view.IsCurrentBeforeFirst);
        Assert.False(view.MoveCurrentToPrevious());
        Assert.Throws<ArgumentOutOfRangeException>(() => view.MoveCurrentToPosition(-2));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.MoveCurrentToPosition(4));
        view.MoveCurrentToFirst();
        Assert.False(view.MoveCurrentTo("zzz"));
        Assert.True(view.IsCurrentBeforeFirst);
        Assert.True(view.MoveCurrentTo("b"));
        Assert.Equal("b", view.CurrentItem);
        view.CurrentChanging += (_, e) => e.Cancel = true;
        Assert.True(view.MoveCurrentToLast());
        Assert.Equal("b", view.CurrentItem);
        Assert.Equal(1, view.CurrentPosition);
        Assert.True(changed >= 5);
    }

    [Fact]
    public void Culture_rejects_null_and_recompares()
    {
        var view = new ListCollectionView(Sample());
        Assert.Throws<ArgumentNullException>(() => view.Culture = null!);
        view.Culture = CultureInfo.InvariantCulture;
        Assert.Same(CultureInfo.InvariantCulture, view.Culture);
    }

    [Fact]
    public void Empty_view_reports_both_edges()
    {
        var view = new ListCollectionView(new object[0]);
        Assert.True(view.IsEmpty);
        Assert.Equal(0, view.Count);
        Assert.Null(view.CurrentItem);
        Assert.Equal(-1, view.CurrentPosition);
        Assert.True(view.IsCurrentBeforeFirst);
        Assert.True(view.IsCurrentAfterLast);
        Assert.False(view.MoveCurrentToFirst());
        Assert.Null(view.Groups);
        Assert.True(view.CanFilter && view.CanSort && view.CanGroup);
    }

    [Fact]
    public void Class_helpers_cover_index_contains_detach()
    {
        var source = new ObservableCollection<object> { "a", "b" };
        var view = new ListCollectionView(source);
        Assert.True(view.Contains("a"));
        Assert.False(view.Contains("zzz"));
        Assert.Equal(1, view.IndexOf("b"));
        Assert.Equal("a", view.GetItemAt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.GetItemAt(-1));
        source.Add("c");
        Assert.Equal(3, view.Count);
        view.DetachFromSourceCollection();
        Assert.Equal(0, view.Count);
        Assert.True(view.IsEmpty);
        source.Add("d");
        Assert.Equal(0, view.Count);
    }

    [Fact]
    public void Nested_group_descriptions_build_two_levels()
    {
        var view = new ListCollectionView(Sample());
        view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        view.GroupDescriptions.Add(new PropertyGroupDescription("Order"));
        var groups = view.Groups;
        Assert.NotNull(groups);
        Assert.Equal(2, groups.Count);
        var plot = groups.OfType<CollectionViewGroup>().Single(g => Equals(g.Name, "Plot"));
        Assert.False(plot.IsBottomLevel);
        Assert.Equal(2, plot.ItemCount);
        Assert.Equal(2, plot.Items.Count);
    }

    [Fact]
    public void CollectionViewSource_forwards_sort_group_filter()
    {
        var cvs = new CollectionViewSource
        {
            Source = Sample(),
        };
        var view = cvs.View;
        Assert.NotNull(view);
        Assert.IsType<ListCollectionView>(view);
        cvs.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
        Assert.Equal("Bar", ((Example)view.Cast<Example>().First()).Name);
        cvs.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        Assert.NotNull(view.Groups);
        Assert.Equal(2, view.Groups!.Count);
        cvs.Filter += (_, e) => e.Accepted = ((Example)e.Item!).Order > 1;
        Assert.Equal(2, view.Cast<object>().Count());
        cvs.Filter += (_, e) => e.Accepted = ((Example)e.Item!).Category == "Plot";
        Assert.Single(view.Cast<Example>());
        cvs.Source = new List<Example> { new("Plot", "Only", 9) };
        var swapped = cvs.View;
        Assert.NotNull(swapped);
        Assert.NotSame(view, swapped);
        var only = Assert.Single(swapped.Cast<Example>());
        Assert.Equal("Only", only.Name);
    }

    [Fact]
    public void CollectionViewSource_validates_source_and_init_rules()
    {
        var cvs = new CollectionViewSource();
        Assert.Null(cvs.View);
        var view = new ListCollectionView(Sample());
        Assert.Throws<ArgumentException>(() => cvs.Source = view);
        Assert.Throws<InvalidOperationException>(() => cvs.CollectionViewType = typeof(ListCollectionView));
        cvs.BeginInit();
        cvs.CollectionViewType = typeof(ListCollectionView);
        cvs.Source = Sample();
        Assert.Null(cvs.View);
        cvs.EndInit();
        Assert.IsType<ListCollectionView>(cvs.View);
        cvs.BeginInit();
        Assert.Throws<ArgumentException>(() => cvs.CollectionViewType = typeof(string));
        cvs.EndInit();
    }

    [Fact]
    public void CollectionViewSource_defer_before_view_is_noop()
    {
        var cvs = new CollectionViewSource();
        using (cvs.DeferRefresh())
        {
        }
        Assert.Null(cvs.View);
    }

    [Fact]
    public void ItemsControl_binds_live_view_and_regenerates()
    {
        var control = new ItemsControl();
        var view = new ListCollectionView(new[] { "a", "b", "c" });
        control.ItemsSource = view;
        Assert.Equal(3, control.Items.Count);
        view.Filter = item => (string)item! != "b";
        Assert.Equal(2, control.Items.Count);
        Assert.DoesNotContain("b", control.Items.Cast<string>());
        view.Filter = null;
        Assert.Equal(3, control.Items.Count);
        control.ItemsSource = new[] { "z" };
        Assert.Single(control.Items);
        view.Filter = item => false;
        Assert.Single(control.Items);
    }

    [Fact]
    public void ItemCollection_answers_base_view_members()
    {
        // Converted WPFGallery MainWindow.ControlsList_Loaded calls
        // CollectionView.get_Count over TreeView.Items (real WPF
        // ItemCollection derives from CollectionView, so the C# compiler
        // binds the inherited getter): the engine matches the inheritance so
        // the call dispatches onto the real store.
        var control = new ItemsControl();
        control.AddItem("a");
        control.AddItem("b");
        CollectionView asBase = control.Items;
        Assert.Equal(2, asBase.Count);
        Assert.False(asBase.IsEmpty);
        Assert.Same(control.Items[0], asBase.GetItemAt(0));
        Assert.Equal(0, asBase.IndexOf("a"));
        Assert.True(asBase.Contains("b"));
        Assert.Equal("a", asBase.CurrentItem);
        IList asList = control.Items;
        Assert.Equal(2, asList.Count);
        Assert.Same(control.Items[1], asList[1]);
        Assert.True(asList.Contains("a"));
        Assert.Equal(1, asList.IndexOf("b"));
        Assert.False(asList.IsFixedSize || asList.IsReadOnly);
        // Rule-0 parity: the default view of an ItemCollection is itself.
        Assert.Same(control.Items, CollectionViewSource.GetDefaultView(control.Items));
        // IsDefaultView keys on SourceCollection (the inner store, whose own
        // default would be a separate ListCollectionView), so it is false
        // here, like WPF.
        Assert.False(CollectionViewSource.IsDefaultView(control.Items));
        asList.Remove("a");
        Assert.Single(control.Items.Cast<object>());
        Assert.Equal("b", asBase.CurrentItem);
        asList.Insert(0, "z");
        Assert.Equal("z", control.Items[0]);
        control.Items[0] = "y";
        Assert.Equal("y", control.Items[0]);
    }

    [Fact]
    public void GroupNameFromItem_honors_converter()
    {
        var view = new ListCollectionView(Sample());
        var upper = new UpperConverter();
        view.GroupDescriptions.Add(new PropertyGroupDescription("Category", upper));
        var groups = view.Groups;
        Assert.NotNull(groups);
        var names = groups.OfType<CollectionViewGroup>().Select(g => g.Name).ToList();
        Assert.Contains("PLOT", names);
        Assert.Contains("AXES", names);
    }

    private sealed class UpperConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string s ? s.ToUpperInvariant() : value;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string s ? s.ToLowerInvariant() : value;
    }
}
