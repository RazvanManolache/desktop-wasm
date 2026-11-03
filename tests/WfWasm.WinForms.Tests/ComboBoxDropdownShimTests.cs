using System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

// No native combo box exists headless: CB_SHOWDROPDOWN opens nothing and
// CB_GETDROPPEDSTATE fell through to default (0), so DroppedDown read back
// false even right after being set true — breaking dropdown open/close
// tracking (and AutoComplete commit-on-close flows) on handle-created
// combos. Desktop parity: the set state reads back until closed.
public class ComboBoxDropdownShimTests
{
    private static ComboBox DropList()
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.Add("alpha");
        combo.Items.Add("beta");
        combo.Items.Add("gamma");
        _ = combo.Handle; // force handle creation: DroppedDown goes native
        return combo;
    }

    [Fact]
    public void DroppedDownDefaultsFalse()
    {
        using ComboBox combo = DropList();
        Assert.False(combo.DroppedDown);
    }

    [Fact]
    public void DroppedDownSetTrueReadsBackTrue()
    {
        using ComboBox combo = DropList();
        combo.DroppedDown = true;
        Assert.True(combo.DroppedDown);
    }

    [Fact]
    public void DroppedDownSetFalseAfterTrueReadsBackFalse()
    {
        using ComboBox combo = DropList();
        combo.DroppedDown = true;
        combo.DroppedDown = false;
        Assert.False(combo.DroppedDown);
    }
}
