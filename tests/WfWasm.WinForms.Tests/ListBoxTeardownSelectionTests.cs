using System.Windows.Forms;

// Word ctor wall 3 (SF1120-CRASHQA-SF0809): ListBox teardown read
// `get_Text -> get_SelectedItem -> get_Item` threw IndexOutOfRange on
// the served bundle when the managed selection mirror disagreed with
// the native count during WmDestroy. These pin the current fork:
// selection round-trips through handle creation, and teardown reads
// never throw.
public sealed class ListBoxTeardownSelectionTests
{
    private sealed class TeardownListBox : ListBox
    {
        public void TearDown() => DestroyHandle();
    }

    [Fact]
    public void Select_With_Handle_Reads_Back()
    {
        using var lb = new TeardownListBox();
        lb.Items.AddRange(["a", "b", "c"]);
        lb.SelectedIndex = 0;
        _ = lb.Handle;
        Assert.Equal(0, lb.SelectedIndex);
        Assert.Equal("a", lb.SelectedItem);
        Assert.Single(lb.SelectedItems);
    }

    [Fact]
    public void DestroyHandle_Teardown_Reads_Do_Not_Throw()
    {
        using var lb = new TeardownListBox();
        lb.Items.AddRange(["a", "b", "c"]);
        lb.SelectedIndex = 0;
        _ = lb.Handle;
        lb.TearDown();
        Assert.False(lb.IsHandleCreated);
        Assert.Equal("a", lb.SelectedItem);
        Assert.Equal("a", lb.Text);
    }
}
