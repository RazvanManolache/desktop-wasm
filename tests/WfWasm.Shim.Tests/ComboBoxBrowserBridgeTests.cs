using WfWasm.Shim;

// Generic stock-ComboBox browser bridge (ADGV-COMBO-20261102): the shim
// ComboBox paints and click-toggles, but data-bound combos stay blank
// (no DataSource fill), keyboard does nothing, and user commits never raise
// SelectionChangeCommitted. The helper is host-agnostic: pure state
// transitions the Host render/input hooks can wire without behavior drift.
public class ComboBoxBrowserBridgeTests
{
    private sealed class Row
    {
        public Row(string id, string name) { Id = id; Name = name; }
        public string Id { get; }
        public string Name { get; }
        public override string ToString() => Name;
    }

    private sealed class SourceHolder
    {
        public SourceHolder(object list) { List = list; }
        public object List { get; }
    }

    private static ComboBox List(params string[] items)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var item in items)
            combo.Items.Add(item);
        return combo;
    }

    [Fact]
    public void BindDataSource_fills_items_from_enumerable()
    {
        using ComboBox combo = List();
        combo.DisplayMember = "Name";
        combo.BindDataSource(new[] { new Row("a", "Alpha"), new Row("b", "Beta") });
        Assert.Equal(2, combo.Items.Count);
        Assert.Equal("Alpha", combo.GetItemText(combo.Items[0]));
        Assert.Equal("Beta", combo.GetItemText(combo.Items[1]));
    }

    [Fact]
    public void BindDataSource_null_clears_and_resets_selection()
    {
        using ComboBox combo = List("alpha", "beta");
        combo.SelectedIndex = 1;
        combo.BindDataSource(null);
        Assert.Empty(combo.Items);
        Assert.Equal(-1, combo.SelectedIndex);
    }

    [Fact]
    public void BindDataSource_unwraps_list_holder_like_binding_source()
    {
        using ComboBox combo = List();
        combo.BindDataSource(new SourceHolder(new[] { "x", "y", "z" }));
        Assert.Equal(3, combo.Items.Count);
        Assert.Equal("y", combo.GetItemText(combo.Items[1]));
    }

    [Fact]
    public void BindDataSource_preserves_selection_by_value()
    {
        using ComboBox combo = List();
        combo.DisplayMember = "Name";
        combo.ValueMember = "Id";
        combo.BindDataSource(new[] { new Row("a", "Alpha"), new Row("b", "Beta") });
        combo.SelectedIndex = 1;
        combo.BindDataSource(new[] { new Row("b", "Beta"), new Row("c", "Gamma") });
        Assert.Equal("b", combo.GetSelectedValue());
        Assert.Equal("Beta", combo.FieldText);
    }

    [Fact]
    public void SelectedValue_resolves_value_member()
    {
        using ComboBox combo = List();
        combo.DisplayMember = "Name";
        combo.ValueMember = "Id";
        combo.BindDataSource(new[] { new Row("a", "Alpha") });
        combo.SelectedIndex = 0;
        Assert.Equal("a", combo.GetSelectedValue());
    }

    [Fact]
    public void SelectedValue_without_value_member_is_selected_item()
    {
        using ComboBox combo = List("alpha", "beta");
        combo.SelectedIndex = 1;
        Assert.Equal("beta", combo.GetSelectedValue());
    }

    [Fact]
    public void Keyboard_down_when_closed_moves_and_commits()
    {
        using ComboBox combo = List("blank", "FullForm", "DataSource");
        int committed = 0;
        combo.SelectionChangeCommitted += (_, _) => committed++;
        combo.SelectedIndex = 0;
        Assert.True(combo.HandleBrowserKey(new KeyEventArgs(Keys.Down)));
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal(1, committed);
        Assert.False(combo.DroppedDown);
    }

    [Fact]
    public void Keyboard_F4_toggles_popup()
    {
        using ComboBox combo = List("blank", "FullForm", "DataSource");
        Assert.True(combo.HandleBrowserKey(new KeyEventArgs(Keys.F4)));
        Assert.True(combo.DroppedDown);
        Assert.True(combo.HandleBrowserKey(new KeyEventArgs(Keys.F4)));
        Assert.False(combo.DroppedDown);
    }

    [Fact]
    public void Keyboard_enter_commits_and_closes()
    {
        using ComboBox combo = List("blank", "FullForm", "DataSource");
        int committed = 0;
        combo.SelectionChangeCommitted += (_, _) => committed++;
        combo.DroppedDown = true;
        Assert.True(combo.HandleBrowserKey(new KeyEventArgs(Keys.Down)));
        Assert.True(combo.HandleBrowserKey(new KeyEventArgs(Keys.Enter)));
        Assert.False(combo.DroppedDown);
        Assert.Equal(1, committed);
    }

    [Fact]
    public void Keyboard_escape_restores_and_closes_without_commit()
    {
        using ComboBox combo = List("blank", "FullForm", "DataSource");
        int committed = 0;
        combo.SelectionChangeCommitted += (_, _) => committed++;
        combo.SelectedIndex = 0;
        combo.DroppedDown = true;
        Assert.True(combo.HandleBrowserKey(new KeyEventArgs(Keys.Down)));
        Assert.True(combo.HandleBrowserKey(new KeyEventArgs(Keys.Down)));
        Assert.True(combo.HandleBrowserKey(new KeyEventArgs(Keys.Escape)));
        Assert.False(combo.DroppedDown);
        Assert.Equal(0, combo.SelectedIndex);
        Assert.Equal(0, committed);
    }

    [Fact]
    public void Keyboard_home_end_jump()
    {
        using ComboBox combo = List("blank", "FullForm", "DataSource");
        Assert.True(combo.HandleBrowserKey(new KeyEventArgs(Keys.End)));
        Assert.Equal(2, combo.SelectedIndex);
        Assert.True(combo.HandleBrowserKey(new KeyEventArgs(Keys.Home)));
        Assert.Equal(0, combo.SelectedIndex);
    }

    [Fact]
    public void CommitSelection_raises_committed_once_and_closes()
    {
        using ComboBox combo = List("blank", "FullForm", "DataSource");
        int committed = 0;
        combo.SelectionChangeCommitted += (_, _) => committed++;
        combo.DroppedDown = true;
        combo.CommitSelection(2);
        Assert.Equal(2, combo.SelectedIndex);
        Assert.False(combo.DroppedDown);
        Assert.Equal(1, committed);
    }

    [Fact]
    public void PopupGeometry_respects_item_and_height_limits()
    {
        using ComboBox combo = List("a", "b", "c");
        combo.ItemHeight = 15;
        var (rows, height) = combo.PopupGeometry();
        Assert.Equal(3, rows);
        Assert.Equal(3 * 15 + 2, height);
    }
}
