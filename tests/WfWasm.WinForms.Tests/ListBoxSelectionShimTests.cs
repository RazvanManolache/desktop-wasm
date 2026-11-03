using System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

// The headless SendMessage stub has no native list box behind a handle, so
// LB_GETCURSEL / LB_GETSELCOUNT / LB_GETTEXT(LEN) are answered from managed
// state. Desktop returns LB_ERR (-1) when nothing is selected; the stub used
// to fall through to 0, which made SelectedIndex lie (0) on handle-created
// but unselected boxes: SelectedItems.Count read 1 while the managed
// ItemArray held zero Selected bits, and the indexer threw
// IndexOutOfRangeException out of Control.OnHandleDestroyed's text stash
// (Word ListBox teardown wall).
public class ListBoxSelectionShimTests
{
    [Fact]
    public void UnselectedBoxWithHandleReportsNoSelection()
    {
        using var box = new ListBox();
        box.Items.AddRange(["a", "b", "c"]);
        _ = box.Handle; // force handle creation: SelectedIndex reads LB_GETCURSEL
        Assert.Equal(-1, box.SelectedIndex);
        Assert.Empty(box.SelectedItems);
        Assert.Null(box.SelectedItem);
    }

    [Fact]
    public void UnselectedBoxTextReadDoesNotThrow()
    {
        using var box = new ListBox();
        box.Items.AddRange(["a", "b", "c"]);
        _ = box.Handle;
        // This is what Control.OnHandleDestroyed stashes (_text = Text).
        Assert.Equal(string.Empty, box.Text);
    }

    [Fact]
    public void SelectionRoundTripsThroughShim()
    {
        using var box = new ListBox();
        box.Items.AddRange(["a", "b", "c"]);
        _ = box.Handle;
        box.SelectedIndex = 1;
        Assert.Equal(1, box.SelectedIndex);
        Assert.Equal("b", box.SelectedItem);
        Assert.Single(box.SelectedItems);
        box.SelectedIndex = -1;
        Assert.Equal(-1, box.SelectedIndex);
        Assert.Empty(box.SelectedItems);
    }

    [Fact]
    public void MultiSelectCountRoundTripsThroughShim()
    {
        using var box = new ListBox { SelectionMode = SelectionMode.MultiSimple };
        box.Items.AddRange(["a", "b", "c", "d"]);
        _ = box.Handle;
        box.SetSelected(0, true);
        box.SetSelected(2, true);
        Assert.Equal(2, box.SelectedItems.Count);
        Assert.True(box.GetSelected(0));
        Assert.False(box.GetSelected(1));
        box.SetSelected(0, false);
        Assert.Single(box.SelectedItems);
    }

    [Fact]
    public void ClearResetsSelection()
    {
        using var box = new ListBox();
        box.Items.AddRange(["a", "b", "c"]);
        _ = box.Handle;
        box.SelectedIndex = 0;
        box.Items.Clear();
        Assert.Equal(-1, box.SelectedIndex);
        Assert.Empty(box.SelectedItems);
    }
}
