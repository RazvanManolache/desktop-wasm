// ADGV-COMBO-20261102 Host hookup: stock-ComboBox browser bridge for the
// FORK control (System.Windows.Forms.ComboBox as served via the
// rewritten-small fork reference). The dw-wpf helper
// (WfWasm.Shim/ComboBox.BrowserBridge.cs) implements the same transitions
// against the SHIM control used by built-in demos; rewritten apps like the
// ADGV sample bind the fork's pristine ComboBox instead (Cecil-verified:
// AdvancedDataGridViewSample references ComboBox only in scope
// WfWasm.WinForms v1.0.0.0), which has no BindDataSource/HandleBrowserKey.
// Fork edits need a narrow vendor transfer, so this file ports the tested
// transitions 1:1 onto the fork's public surface as extension methods —
// no fork, rewriter, or PInvoke-mirror changes. Branch structure and
// snapshot semantics mirror the shim helper; deviations are commented.
// Verified by browser acceptance (ADGV gate), not unit tests: the fork
// control only exists meaningfully in the served browser closure.
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace WfWasm.CanvasHost;

public static class ComboBoxBrowserHook
{
    private sealed class KeyState
    {
        public int OpenSnapshot = -1;
        public bool SnapshotValid;
        public int CommittedIndex = -1;
        // RT-ADGV-SEL-1326-j7x1: the open-state arrow preview must survive
        // a lost SelectedIndex (served wasm condition: highlight moves but
        // the native index snaps back, so Enter commits stale state).
        // PreviewIndex remembers the last previewed row per combo; Enter
        // commits it, and every close/commit/refill path clears it.
        public int PreviewIndex = -1;
        public bool PreviewValid;
    }

    private static readonly ConditionalWeakTable<ComboBox, KeyState> s_keyState = new();

    private static KeyState StateOf(ComboBox combo) => s_keyState.GetValue(combo, _ => new KeyState());

    private const int ButtonWidth = 17;
    private const int FieldHeight = 23;

    /// <summary>
    /// Absolute legibility floor (combo-rowcommit-dropdown R1/R5): served
    /// sessions log raw fork ItemHeight=0 with an unusable live font metric,
    /// which collapsed the stride to 1px (5px popup sliver, rows unhittable,
    /// clicks closing without commit). Matches the shim ComboBox's own
    /// ItemHeight default (15); owner-set taller strides still win via Max.
    /// </summary>
    private const int MinHeadlessItemHeight = 15;

    /// <summary>
    /// Lazy DataSource fill at Host use sites. The Host cannot intercept
    /// the pristine setter; when the app assigned a source but Items stayed
    /// empty (headless CurrencyManager gap), fill from the same duck-typed
    /// unwrap the shim helper uses. No-op when Items are already filled.
    /// </summary>
    public static void EnsureDataSourceItems(this ComboBox combo)
    {
        object? ds;
        try { ds = combo.DataSource; }
        catch { return; }
        if (ds is null || combo.Items.Count > 0)
            return;
        object? remembered = null;
        try { remembered = combo.SelectedValue; } catch { }
        int oldIndex = -1;
        try { oldIndex = combo.SelectedIndex; } catch { }
        try
        {
            combo.BeginUpdate();
            try
            {
                combo.Items.Clear();
                foreach (object? item in EnumerateSource(ds))
                    combo.Items.Add(item);
            }
            finally
            {
                combo.EndUpdate();
            }
        }
        catch { return; }
        int restored = -1;
        if (combo.Items.Count > 0)
        {
            if (remembered is not null)
            {
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    if (Equals(GetItemValue(combo, combo.Items[i]), remembered))
                    {
                        restored = i;
                        break;
                    }
                }
            }
            if (restored < 0 && oldIndex >= 0 && oldIndex < combo.Items.Count)
                restored = oldIndex;
        }
        try { combo.SelectedIndex = restored; } catch { }
        StateOf(combo).CommittedIndex = restored;
        StateOf(combo).SnapshotValid = false;
        StateOf(combo).PreviewValid = false;
    }

    /// <summary>
    /// User commit: selects, closes, raises SelectionChangeCommitted once.
    /// The protected raiser is invoked by reflection (graceful skip when
    /// absent); everything else uses the public surface.
    /// </summary>
    public static void CommitForkSelection(this ComboBox combo, int index)
    {
        if (!combo.Enabled)
            return;
        if (index < 0 || index >= combo.Items.Count)
        {
            try { combo.DroppedDown = false; } catch { }
            StateOf(combo).SnapshotValid = false;
            StateOf(combo).PreviewValid = false;
            return;
        }
        try { combo.SelectedIndex = index; } catch { return; }
        try { combo.DroppedDown = false; } catch { }
        StateOf(combo).CommittedIndex = combo.SelectedIndex;
        StateOf(combo).SnapshotValid = false;
        StateOf(combo).PreviewValid = false;
        RaiseCommitEvent(combo);
        try
        {
            string committed = combo.GetItemText(combo.Items[combo.SelectedIndex]);
            System.Console.WriteLine("[Host][combo] commit " + combo.Name + " index=" + combo.SelectedIndex + " text=" + committed);
        }
        catch { }
    }

    /// <summary>
    /// Keyboard driver for fork combos. Ports
    /// WfWasm.Shim.ComboBox.HandleBrowserKey 1:1 (F4 toggle, Alt+Down/Up,
    /// Escape restore+close, Enter commit+close, arrows/Home/End with
    /// commit-when-closed and preview-when-open). Returns true when the key
    /// was consumed (host must mark Handled and skip default dispatch).
    /// </summary>
    public static bool HandleForkBrowserKey(this ComboBox combo, KeyEventArgs e)
    {
        if (e.Handled || !combo.Enabled)
            return false;
        bool simple = combo.DropDownStyle == ComboBoxStyle.Simple;
        Keys code = e.KeyCode;
        if (code == Keys.F4 && !e.Control)
        {
            if (!simple)
                try { combo.DroppedDown = !combo.DroppedDown; } catch { }
            StateOf(combo).SnapshotValid = false;
            StateOf(combo).PreviewValid = false;
            e.Handled = true;
            return true;
        }
        if (e.Alt && code == Keys.Down && !simple)
        {
            try { combo.DroppedDown = true; } catch { }
            StateOf(combo).SnapshotValid = false;
            StateOf(combo).PreviewValid = false;
            e.Handled = true;
            return true;
        }
        if (e.Alt && code == Keys.Up && !simple)
        {
            try { combo.DroppedDown = false; } catch { }
            StateOf(combo).SnapshotValid = false;
            StateOf(combo).PreviewValid = false;
            e.Handled = true;
            return true;
        }
        if (code == Keys.Escape && combo.DroppedDown && !simple)
        {
            KeyState st = StateOf(combo);
            if (st.SnapshotValid)
                try { combo.SelectedIndex = st.OpenSnapshot; } catch { }
            st.SnapshotValid = false;
            st.PreviewValid = false;
            try { combo.DroppedDown = false; } catch { }
            e.Handled = true;
            return true;
        }
        if (code == Keys.Enter && combo.DroppedDown && !simple)
        {
            KeyState stEnter = StateOf(combo);
            int commitIndex = combo.SelectedIndex;
            if (stEnter.PreviewValid && stEnter.PreviewIndex >= 0 && stEnter.PreviewIndex < combo.Items.Count)
                commitIndex = stEnter.PreviewIndex;
            combo.CommitForkSelection(commitIndex);
            e.Handled = true;
            return true;
        }
        int target = combo.SelectedIndex;
        switch (code)
        {
            case Keys.Up: target = combo.SelectedIndex - 1; break;
            case Keys.Down: target = combo.SelectedIndex + 1; break;
            case Keys.Home: target = 0; break;
            case Keys.End: target = combo.Items.Count - 1; break;
            default: return false;
        }
        if (combo.Items.Count == 0)
            return false;
        target = Math.Clamp(target, 0, combo.Items.Count - 1);
        e.Handled = true;
        if (combo.DroppedDown && !simple)
        {
            KeyState st = StateOf(combo);
            if (!st.SnapshotValid)
            {
                st.OpenSnapshot = combo.SelectedIndex;
                st.SnapshotValid = true;
            }
            try { combo.SelectedIndex = target; } catch { }
            st.PreviewIndex = target;
            st.PreviewValid = true;
            return true;
        }
        try { combo.SelectedIndex = target; } catch { return true; }
        StateOf(combo).CommittedIndex = combo.SelectedIndex;
        RaiseCommitEvent(combo);
        return true;
    }

    /// <summary>
    /// Pointer driver for fork combos. Ports
    /// WfWasm.Shim.ComboBox.HandleClickLocal 1:1 (row commit, button
    /// toggle, DropDownList tap-to-open). Local coords relative to the
    /// combo field origin; the open list overflows below FieldHeight.
    /// Returns true when consumed (host must skip default dispatch).
    /// </summary>
    public static bool HandleForkMouseDown(this ComboBox combo, int localX, int localY)
    {
        if (!combo.Enabled)
            return false;
        bool simple = combo.DropDownStyle == ComboBoxStyle.Simple;
        if (combo.DroppedDown && !simple)
        {
            (int bx, int by, int bw, int bh) = ForkDropDownBounds(combo);
            if (localX >= bx && localX < bx + bw && localY >= by && localY < by + bh)
            {
                int index = (localY - by - 1) / combo.ForkItemHeight();
                if (index >= 0 && index < combo.Items.Count)
                    combo.CommitForkSelection(index);
                return true;
            }
            if (localX >= combo.Width - ButtonWidth && localY < combo.Height)
            {
                try { combo.DroppedDown = false; } catch { }
                StateOf(combo).SnapshotValid = false;
                StateOf(combo).PreviewValid = false;
                return true;
            }
            try { combo.DroppedDown = false; } catch { }
            StateOf(combo).SnapshotValid = false;
            StateOf(combo).PreviewValid = false;
            return true;
        }
        if (localX >= combo.Width - ButtonWidth && localY < combo.Height)
        {
            if (!simple)
                try { combo.DroppedDown = true; } catch { }
            StateOf(combo).SnapshotValid = false;
            StateOf(combo).PreviewValid = false;
            return true;
        }
        if (combo.DropDownStyle == ComboBoxStyle.DropDownList)
        {
            try { combo.DroppedDown = true; } catch { }
            StateOf(combo).SnapshotValid = false;
            StateOf(combo).PreviewValid = false;
            return true;
        }
        return false;
    }

    private static bool s_geometryLogged;

    /// <summary>
    /// Row height headless: the fork's ItemHeight derives from font
    /// metrics that collapse to 0/1px without a native device (served
    /// sessions log raw itemH=0), which shrank the overlay to a sliver.
    /// Floor at the managed font height + padding when available
    /// (desktop draws rows at font height + margin), then at the
    /// absolute legibility floor so paint, hit-test, and commit share
    /// one usable stride whatever the font state.
    /// </summary>
    public static int ForkItemHeight(this ComboBox combo)
    {
        int itemH = 1;
        try { itemH = Math.Max(1, combo.ItemHeight); } catch { }
        try
        {
            int fontH = (int)Math.Ceiling(combo.Font.GetHeight()) + 4;
            if (fontH > itemH)
                itemH = fontH;
        }
        catch { }
        if (itemH < MinHeadlessItemHeight)
            itemH = MinHeadlessItemHeight;
        return itemH;
    }

    /// <summary>Popup list geometry (mirrors the shim helper's math).</summary>
    public static (int Rows, int Height) ForkPopupGeometry(this ComboBox combo)
    {
        int rows = Math.Min(combo.MaxDropDownItems, Math.Max(1, combo.Items.Count));
        int height = Math.Min(combo.DropDownHeight, (rows * combo.ForkItemHeight()) + 2);
        if (!s_geometryLogged)
        {
            s_geometryLogged = true;
            try
            {
                System.Console.WriteLine("[Host][combo] geometry " + combo.Name + " rows=" + rows + " itemH=" + combo.ItemHeight + " maxDrop=" + combo.MaxDropDownItems + " dropH=" + combo.DropDownHeight);
            }
            catch { }
        }
        return (rows, height);
    }

    /// <summary>Drop-down list bounds in control-local space (below field).
    /// Origin is the live field height (fork combos are Height-tall, not
    /// the shim's 23px constant) so paint and hit-test share one origin.
    /// </summary>
    public static (int X, int Y, int Width, int Height) ForkDropDownBounds(this ComboBox combo)
    {
        (_, int height) = combo.ForkPopupGeometry();
        return (0, combo.Height, combo.DropDownWidth > 0 ? combo.DropDownWidth : combo.Width, height);
    }

    /// <summary>Closed-field text (GetItemText over SelectedItem, else Text).</summary>
    public static string ForkFieldText(this ComboBox combo)
    {
        try
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < combo.Items.Count)
            {
                string t = combo.GetItemText(combo.Items[combo.SelectedIndex]);
                if (!string.IsNullOrEmpty(t))
                    return t;
            }
        }
        catch { }
        try { return combo.Text ?? string.Empty; } catch { return string.Empty; }
    }

    private static void RaiseCommitEvent(ComboBox combo)
    {
        try
        {
            MethodInfo? m = combo.GetType().GetMethod(
                "OnSelectionChangeCommitted",
                BindingFlags.NonPublic | BindingFlags.Instance);
            m?.Invoke(combo, new object[] { EventArgs.Empty });
        }
        catch { }
    }

    private static object? GetItemValue(ComboBox combo, object? item)
    {
        if (item is null)
            return null;
        try
        {
            string? vm = combo.ValueMember;
            if (!string.IsNullOrEmpty(vm))
            {
                object? value = item.GetType().GetProperty(vm)?.GetValue(item);
                if (value is not null)
                    return value;
            }
        }
        catch { }
        return item;
    }

    private static IEnumerable EnumerateSource(object? source)
    {
        object? current = source;
        for (int hop = 0; hop < 3 && current is not null; hop++)
        {
            if (current is string)
                break;
            if (current is IEnumerable enumerable)
                return enumerable;
            PropertyInfo? listProp = current.GetType().GetProperty("List");
            object? next = null;
            try { next = listProp?.GetValue(current); } catch { }
            if (next is null || ReferenceEquals(next, current))
                break;
            current = next;
        }
        if (current is string text)
            return new object?[] { text };
        if (current is IEnumerable rest)
            return rest;
        return Array.Empty<object?>();
    }
}
