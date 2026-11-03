// Combo row-commit tests (dw-runtime slice combo-rowcommit-dropdown):
// R1 (itemH=0 empty popup) + R5 (ChartType row click closes without commit)
// share one root cause: ForkItemHeight collapses headless when the live
// font metric is unavailable, so the overlay shrinks to a sliver and row
// clicks miss it. R2: saved combos are editable DropDown style — field
// clicks must not open (desktop-faithful), arrow clicks must.
using System.Windows.Forms;
using WfWasm.CanvasHost;

public sealed class ComboBoxRowCommitTests
{
    private static ComboBox MemoryCombo()
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 93, Height = 17 };
        combo.Items.Add(" ");
        combo.Items.Add("FullForm");
        combo.Items.Add("DataSource");
        return combo;
    }

    private static ComboBox SavedCombo()
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 141, Height = 17 };
        combo.Items.Add("1");
        combo.Items.Add("2");
        return combo;
    }

    [Fact]
    public void ForkItemHeight_Never_Collapses_Below_Legible_Stride()
    {
        using var combo = MemoryCombo();
        int raw = -1;
        string fontInfo;
        try
        {
            raw = combo.ItemHeight;
            fontInfo = "font=" + (combo.Font?.Name ?? "<null>");
        }
        catch (Exception ex) { fontInfo = "EX " + ex.GetType().Name; }
        int itemH = combo.ForkItemHeight();
        var (rows, height) = combo.ForkPopupGeometry();
        Assert.True(itemH >= 15 && height >= rows * 15,
            $"R1: rows={rows} height={height} itemH={itemH} rawItemHeight={raw} {fontInfo}");
    }

    [Fact]
    public void Open_Popup_Row_Center_Click_Commits_Row()
    {
        using var combo = MemoryCombo();
        combo.DroppedDown = true;
        var (bx, by, bw, _) = combo.ForkDropDownBounds();
        int itemH = combo.ForkItemHeight();
        bool consumed = combo.HandleForkMouseDown(bx + bw / 2, by + 1 + itemH + itemH / 2);
        Assert.True(consumed, "row click must be consumed");
        Assert.Equal(1, combo.SelectedIndex);
        Assert.False(combo.DroppedDown);
    }

    [Fact]
    public void DropDown_Field_Click_Does_Not_Open()
    {
        using var combo = SavedCombo();
        bool consumed = combo.HandleForkMouseDown(50, 8);
        Assert.False(consumed, "R2: editable-field click must not open (desktop-faithful)");
        Assert.False(combo.DroppedDown);
    }

    [Fact]
    public void DropDown_Arrow_Click_Opens_Popup()
    {
        using var combo = SavedCombo();
        bool consumed = combo.HandleForkMouseDown(combo.Width - 8, 8);
        Assert.True(consumed, "R2: arrow click must be consumed");
        Assert.True(combo.DroppedDown, "R2: arrow click must open the saved combo");
    }

    [Fact]
    public void DropDown_F4_Opens_Popup()
    {
        using var combo = SavedCombo();
        var e = new KeyEventArgs(Keys.F4);
        bool consumed = combo.HandleForkBrowserKey(e);
        Assert.True(consumed, "R2: F4 must be consumed");
        Assert.True(combo.DroppedDown, "R2: F4 must open the saved combo");
    }
}
