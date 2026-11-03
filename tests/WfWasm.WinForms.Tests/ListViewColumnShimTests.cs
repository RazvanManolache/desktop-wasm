using System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

// Syncfusion AutoComplete suggestion init mutates a ListView ColumnHeader
// after the handle exists (first keystroke into TextBoxExt 'textBox1'),
// which routes through ListView.SetColumnInfo -> LVM_SETCOLUMNW. Headless
// the fork SendMessage shim had no LVM_SETCOLUMNW arm, fell through to
// default (zero), and SetColumnInfo threw InvalidOperationException
// "ColumnInfo cannot be set." once per session (45 client-error recurrences,
// all app=syncfusion-auto-complete). Desktop answers TRUE here; the managed
// ColumnHeader already holds the new values, so success needs no mirror.
public class ListViewColumnShimTests
{
    private static ListView ColumnList()
    {
        var list = new ListView();
        list.Columns.Add("Name", 100);
        _ = list.Handle; // force handle creation: Text/Image sets go native
        return list;
    }

    [Fact]
    public void SetColumnTextAfterHandleCreationDoesNotThrow()
    {
        using ListView list = ColumnList();
        var ex = Record.Exception(() => list.Columns[0].Text = "Renamed");
        Assert.Null(ex);
        Assert.Equal("Renamed", list.Columns[0].Text);
    }

    [Fact]
    public void SetColumnTextAlignAfterHandleCreationDoesNotThrow()
    {
        // Column 0 is coerced to Left by the native contract; align column 1.
        using ListView list = ColumnList();
        list.Columns.Add("Second", 120);
        var ex = Record.Exception(() => list.Columns[1].TextAlign = HorizontalAlignment.Center);
        Assert.Null(ex);
        Assert.Equal(HorizontalAlignment.Center, list.Columns[1].TextAlign);
    }

    [Fact]
    public void AddThenRenameSecondColumnAfterHandleCreation()
    {
        using ListView list = ColumnList();
        list.Columns.Add("Second", 120);
        var ex = Record.Exception(() => list.Columns[1].Text = "Two");
        Assert.Null(ex);
        Assert.Equal("Two", list.Columns[1].Text);
    }

    private static ListView DetailsList()
    {
        var list = new ListView { View = View.Details };
        list.Columns.Add("N", 60);
        list.Items.Add("a much longer entry");
        _ = list.Handle; // force handle creation: widths read LVM_GETCOLUMNWIDTH
        return list;
    }

    [Fact]
    public void AutoResizeColumnContentFitsItemText()
    {
        using ListView list = DetailsList();
        var ex = Record.Exception(() => list.AutoResizeColumn(0, ColumnHeaderAutoResizeStyle.ColumnContent));
        Assert.Null(ex);
        Assert.True(list.Columns[0].Width > 60);
    }

    [Fact]
    public void AutoResizeColumnHeaderSizeFitsHeaderText()
    {
        using ListView list = DetailsList();
        list.Columns[0].Text = "A considerably longer header caption";
        list.AutoResizeColumn(0, ColumnHeaderAutoResizeStyle.HeaderSize);
        Assert.True(list.Columns[0].Width > 60);
    }

    // Headless row geometry is a documented approximation (see
    // PInvokeCore.Methods.cs): row = Font.Height + 2, Details header =
    // Font.Height + 8. These tests pin the wiring (struct write-back,
    // index math, empty guard), not the pixel constants.
    private static int RowH(ListView list) => list.Font.Height + 2;
    private static int HeaderH(ListView list) => list.Font.Height + 8;

    [Fact]
    public void HitTestOnEmptyListReturnsNullInsteadOfThrowing()
    {
        using ListView list = new() { View = View.Details };
        list.Columns.Add("N", 60);
        _ = list.Handle;
        var ex = Record.Exception(() => list.HitTest(10, 10));
        Assert.Null(ex);
        Assert.Null(list.HitTest(10, 10).Item);
    }

    [Fact]
    public void HitTestReturnsClickedRowNotAlwaysFirst()
    {
        using ListView list = new() { View = View.Details };
        list.Columns.Add("N", 60);
        list.Items.Add("zero");
        list.Items.Add("one");
        list.Items.Add("two");
        _ = list.Handle;
        int y = HeaderH(list) + RowH(list) + 1; // middle of second row
        Assert.Same(list.Items[1], list.HitTest(5, y).Item);
    }

    [Fact]
    public void HitTestOnHeaderReturnsNull()
    {
        using ListView list = new() { View = View.Details };
        list.Columns.Add("N", 60);
        list.Items.Add("zero");
        _ = list.Handle;
        Assert.Null(list.HitTest(5, 2).Item);
    }

    private sealed class TextItemComparer : System.Collections.IComparer
    {
        public int Compare(object? x, object? y) => string.Compare(
            ((ListViewItem?)x)?.Text, ((ListViewItem?)y)?.Text,
            StringComparison.OrdinalIgnoreCase);
    }

    private static ListView UnsortedList()
    {
        var list = new ListView { View = View.Details };
        list.Items.Add("delta");
        list.Items.Add("alpha");
        list.Items.Add("charlie");
        _ = list.Handle; // force handle creation: Sort goes native
        return list;
    }

    [Fact]
    public void SortWithCustomSorterReordersDisplayItems()
    {
        using ListView list = UnsortedList();
        list.ListViewItemSorter = new TextItemComparer();
        Assert.Equal("alpha", list.Items[0].Text);
        Assert.Equal("charlie", list.Items[1].Text);
        Assert.Equal("delta", list.Items[2].Text);
    }

    [Fact]
    public void SortingStyleInsertsInSortedPosition()
    {
        // LVS_SORTASCENDING (Sorting property, no custom sorter): desktop
        // inserts each item at its sorted position instead of appending.
        using ListView list = new() { View = View.Details };
        list.Columns.Add("N", 60);
        _ = list.Handle;
        list.Sorting = SortOrder.Ascending;
        list.Items.Add("delta");
        list.Items.Add("alpha");
        list.Items.Add("charlie");
        Assert.Equal("alpha", list.Items[0].Text);
        Assert.Equal("charlie", list.Items[1].Text);
        Assert.Equal("delta", list.Items[2].Text);
    }

    [Fact]
    public void SortingStyleDescendingInsertsInReverse()
    {
        using ListView list = new() { View = View.Details };
        list.Columns.Add("N", 60);
        _ = list.Handle;
        list.Sorting = SortOrder.Descending;
        list.Items.Add("alpha");
        list.Items.Add("charlie");
        list.Items.Add("delta");
        Assert.Equal("delta", list.Items[0].Text);
        Assert.Equal("charlie", list.Items[1].Text);
        Assert.Equal("alpha", list.Items[2].Text);
    }

    [Fact]
    public void SortKeepsSelectionOnItem()
    {
        using ListView list = UnsortedList();
        list.Items[1].Selected = true; // "alpha", moves to 0 after sort
        list.ListViewItemSorter = new TextItemComparer();
        Assert.True(list.Items[0].Selected);
        Assert.False(list.Items[1].Selected);
    }

    [Fact]
    public void GetItemRectReturnsRowBounds()
    {
        // LVM_GETITEMRECT fell through to default (0), which the upstream
        // wrapper turns into ArgumentOutOfRangeException for every valid
        // index. Desktop fills the row rect and returns nonzero.
        using ListView list = DetailsList();
        list.Items.Add("second entry");
        var r = list.GetItemRect(1);
        Assert.Equal(HeaderH(list) + RowH(list), r.Y);
        Assert.Equal(RowH(list), r.Height);
        Assert.Equal(0, r.X);
        Assert.True(r.Width > 0);
    }

    [Fact]
    public void GetItemRectInvalidIndexStillThrows()
    {
        using ListView list = DetailsList();
        Assert.Throws<ArgumentOutOfRangeException>(() => list.GetItemRect(9));
    }

    [Fact]
    public void SortingPropertySortsExistingItemsOnRecreate()
    {
        // Setting Sorting on an icon view recreates the handle; realization
        // re-inserts every row, which used to duplicate the Order mirror
        // and silently disable sorting afterwards. Desktop parity: the
        // recreated control comes up sorted.
        using ListView list = new() { View = View.LargeIcon };
        list.Items.Add("delta");
        list.Items.Add("alpha");
        list.Items.Add("charlie");
        _ = list.Handle;
        list.Sorting = SortOrder.Ascending;
        Assert.Equal("alpha", list.Items[0].Text);
        Assert.Equal("charlie", list.Items[1].Text);
        Assert.Equal("delta", list.Items[2].Text);
    }

    [Fact]
    public void SuggestionInitSequenceCompletesWithoutWedge()
    {
        // VERIFY-FIRSTKEY-1218: keys into the AutoComplete country box stall
        // the automation channel 3/3 while panel2 keys complete. This test
        // replays the full fork-level suggestion-init sequence the vendor
        // performs after SetColumnInfo starts succeeding (column mutation,
        // autosize, hit-test/commit selection, dropdown state, first-key
        // text entry) with a hard wall-clock bound: completion rules out
        // re-entrancy/looping inside the LVM_SETCOLUMNW TRUE arm and its
        // immediate downstream mirrors. A hang here reproduces the wedge.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using ListView list = new() { View = View.Details };
        list.Width = 300;
        list.Height = 400;
        list.Columns.Add("Country", 60);
        list.Columns.Add("Code", 40);
        for (int i = 0; i < 50; i++)
            list.Items.Add(new ListViewItem(new[] { "country-" + i, "c" + i }));
        _ = list.Handle;
        for (int round = 0; round < 5; round++)
        {
            list.Columns[0].Text = "Country-" + round;
            list.AutoResizeColumn(0, ColumnHeaderAutoResizeStyle.ColumnContent);
            list.AutoResizeColumn(1, ColumnHeaderAutoResizeStyle.HeaderSize);
            Assert.True(list.Columns[0].Width > 60);
            int y = HeaderH(list) + RowH(list) * 7 + 1;
            Assert.Same(list.Items[7], list.HitTest(5, y).Item);
            list.Items[7].Selected = true;
            Assert.Contains(list.Items[7], list.SelectedItems.Cast<ListViewItem>());
            list.Items[7].Selected = false;
        }
        using var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.Add("alpha");
        combo.Items.Add("beta");
        _ = combo.Handle;
        combo.DroppedDown = true;
        Assert.True(combo.DroppedDown);
        Assert.Equal(1, combo.FindString("bet"));
        combo.DroppedDown = false;
        using var box = new TextBox();
        _ = box.Handle;
        box.AppendText("u");
        Assert.Equal("u", box.Text);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30),
            $"suggestion-init sequence took {sw.Elapsed}");
    }
}
