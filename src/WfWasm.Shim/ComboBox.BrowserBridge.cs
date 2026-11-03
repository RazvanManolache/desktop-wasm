namespace WfWasm.Shim;

// Generic stock-ComboBox browser bridge (ADGV-COMBO-20261102), cohesive
// companion to the ComboBox in Containers.cs. The shim control already
// paints the closed field (border/arrow/text) and the open list and routes
// pointer open/commit/dismiss through HandleClickLocal; what it lacks for a
// real browser control is: data-source fill (data-bound combos stay blank),
// keyboard open/navigate/commit/dismiss, ValueMember-aware selection reads,
// and a commit signal (SelectionChangeCommitted never fires). Everything
// here is host-agnostic state transitions over the public control surface,
// so Host render/input hooks can wire it without behavior drift:
//
//   render branch: paint closed field + PopupGeometry() rows for the overlay
//   pointer: existing HandleClickLocal opens/commits; route user commits
//     through CommitSelection(index) so the committed event fires
//   keyboard: route KeyDown to HandleBrowserKey(e) and honor the bool
//
// No Host file is touched by this companion. ToolStripComboBox and vendor
// popups are explicitly out of scope.
public partial class ComboBox
{
    private int _browserCommittedIndex = -1;
    private int _browserOpenSnapshot = -1;
    private bool _browserSnapshotValid;

    /// <summary>
    /// Fills <see cref="ComboBox.Items"/> from a data source (cf.
    /// ComboBox.DataSource): any <see cref="System.Collections.IEnumerable"/>
    /// (strings are treated as one scalar, not a char row set), or a
    /// BindingSource-shaped holder exposing a readable <c>List</c> property
    /// (unwrapped up to 3 hops). Null or a non-list source clears.
    /// Preserves selection by selected value when still present, else keeps
    /// the old index when in range, else -1.
    /// </summary>
    public void BindDataSource(object? source)
    {
        object? remembered = GetSelectedValue();
        int oldIndex = SelectedIndex;
        BeginUpdate();
        try
        {
            Items.Clear();
            foreach (object? item in EnumerateSource(source))
                Items.Add(item);
        }
        finally
        {
            EndUpdate();
        }

        int restored = -1;
        if (Items.Count > 0)
        {
            if (remembered is not null)
            {
                for (int i = 0; i < Items.Count; i++)
                {
                    if (Equals(GetItemValue(Items[i]), remembered))
                    {
                        restored = i;
                        break;
                    }
                }
            }
            if (restored < 0 && oldIndex >= 0 && oldIndex < Items.Count)
                restored = oldIndex;
        }
        SelectedIndex = restored;
        _browserCommittedIndex = restored;
        _browserSnapshotValid = false;
    }

    /// <summary>ValueMember-aware selection read (cf. SelectedValue).</summary>
    public object? GetSelectedValue()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Items.Count)
            return null;
        return GetItemValue(Items[SelectedIndex]);
    }

    /// <summary>
    /// User commit: selects, closes, and raises
    /// <see cref="ComboBox.SelectionChangeCommitted"/> exactly once.
    /// Pointer and keyboard commit paths must funnel through here.
    /// </summary>
    public void CommitSelection(int index)
    {
        if (!Enabled)
            return;
        SelectedIndex = index;
        DroppedDown = false;
        _browserCommittedIndex = SelectedIndex;
        _browserSnapshotValid = false;
        OnSelectionChangeCommitted(EventArgs.Empty);
    }

    /// <summary>
    /// Keyboard driver for the browser host. Returns true when the key was
    /// consumed (host should mark Handled). Arrows/Home/End move the
    /// selection (committing immediately when closed); F4 and Alt+Down/Up
    /// toggle the list; Enter commits and closes; Escape restores the last
    /// committed selection and closes without committing.
    /// </summary>
    public bool HandleBrowserKey(KeyEventArgs e)
    {
        if (e.Handled || !Enabled)
            return false;
        Keys code = e.KeyCode;
        if (code == Keys.F4 && !e.Control)
        {
            if (DropDownStyle != ComboBoxStyle.Simple)
                DroppedDown = !DroppedDown;
            _browserSnapshotValid = false;
            e.Handled = true;
            return true;
        }
        if (e.Alt && code == Keys.Down && DropDownStyle != ComboBoxStyle.Simple)
        {
            DroppedDown = true;
            _browserSnapshotValid = false;
            e.Handled = true;
            return true;
        }
        if (e.Alt && code == Keys.Up && DropDownStyle != ComboBoxStyle.Simple)
        {
            DroppedDown = false;
            _browserSnapshotValid = false;
            e.Handled = true;
            return true;
        }
        if (code == Keys.Escape && DroppedDown && DropDownStyle != ComboBoxStyle.Simple)
        {
            if (_browserSnapshotValid)
                SelectedIndex = _browserOpenSnapshot;
            _browserSnapshotValid = false;
            DroppedDown = false;
            e.Handled = true;
            return true;
        }
        if (code == Keys.Enter && DroppedDown && DropDownStyle != ComboBoxStyle.Simple)
        {
            CommitSelection(SelectedIndex);
            e.Handled = true;
            return true;
        }
        int target = SelectedIndex;
        switch (code)
        {
            case Keys.Up: target = SelectedIndex - 1; break;
            case Keys.Down: target = SelectedIndex + 1; break;
            case Keys.Home: target = 0; break;
            case Keys.End: target = Items.Count - 1; break;
            default: return false;
        }
        if (Items.Count == 0)
            return false;
        target = System.Math.Clamp(target, 0, Items.Count - 1);
        e.Handled = true;
        if (DroppedDown && DropDownStyle != ComboBoxStyle.Simple)
        {
            if (!_browserSnapshotValid)
            {
                _browserOpenSnapshot = SelectedIndex;
                _browserSnapshotValid = true;
            }
            SelectedIndex = target;
            return true;
        }
        SelectedIndex = target;
        _browserCommittedIndex = SelectedIndex;
        OnSelectionChangeCommitted(EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Popup list geometry for the host overlay: visible rows capped by
    /// <see cref="ComboBox.MaxDropDownItems"/> and <see cref="ComboBox.DropDownHeight"/>;
    /// width comes from <see cref="ComboBox.DropDownWidth"/> (or the field).
    /// </summary>
    public (int Rows, int Height) PopupGeometry()
    {
        int rows = System.Math.Min(MaxDropDownItems, System.Math.Max(1, Items.Count));
        int height = System.Math.Min(DropDownHeight, (rows * ItemHeight) + 2);
        return (rows, height);
    }

    private static System.Collections.IEnumerable EnumerateSource(object? source)
    {
        object? current = source;
        for (int hop = 0; hop < 3 && current is not null; hop++)
        {
            if (current is string)
                break;
            if (current is System.Collections.IEnumerable enumerable)
                return enumerable;
            var listProp = current.GetType().GetProperty("List");
            object? next = listProp?.GetValue(current);
            if (next is null || ReferenceEquals(next, current))
                break;
            current = next;
        }
        if (current is string text)
            return new object?[] { text };
        if (current is System.Collections.IEnumerable rest)
            return rest;
        return System.Array.Empty<object?>();
    }

    private object? GetItemValue(object? item)
    {
        if (item is null)
            return null;
        if (ValueMember is not null)
        {
            var prop = item.GetType().GetProperty(ValueMember);
            object? value = prop?.GetValue(item);
            if (value is not null)
                return value;
        }
        return item;
    }
}
