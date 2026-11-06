// RT-OVERLAY-1451-a3k9: an open fork-combo list floats above later
// siblings; HitTestDeep must not let an opaque later sibling shadow list
// rows. Served shape: memory combo (form 284,13 93x17) overlapped by
// textBox_sort (form 234,60 210x92) — row clicks at form y>=60 hit the
// textbox, no commit, dropdown stays open. Minimal stand-in below mirrors
// that geometry (combo first, overlapping textbox later in Controls[]).
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;

public sealed class ComboOverlayShadowTests
{
    private static (WinFormsAppHost host, System.Windows.Forms.Form form,
        System.Windows.Forms.ComboBox combo, int rowAbsX, int rowAbsY) OpenShadowedCombo()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);

        var combo = new System.Windows.Forms.ComboBox
        {
            Name = "selCombo",
            Location = new System.Drawing.Point(10, 10),
            Size = new System.Drawing.Size(100, 21),
            DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList,
        };
        combo.Items.Add(" ");
        combo.Items.Add("FullForm");
        combo.Items.Add("X");
        combo.SelectedIndex = -1;
        form.Controls.Add(combo);

        // Later sibling overlapping the drop-down list rows (served
        // textBox_sort shape, trimmed to the overlap band).
        var shadow = new System.Windows.Forms.TextBox
        {
            Name = "shadowBox",
            Location = new System.Drawing.Point(10, 10 + combo.Height + 2),
            Size = new System.Drawing.Size(100, 44),
        };
        form.Controls.Add(shadow);

        bool opened = ComboBoxBrowserHook.HandleForkMouseDown(combo, combo.Width - 5, 5);
        Assert.True(opened && combo.DroppedDown);

        // Row 0 center in canvas coords (list opens below the field).
        // Client origin sits below the ~30px title chrome (MouseRoutingTests).
        int rowAbsX = form.Location.X + combo.Left + combo.Width / 2;
        int rowAbsY = form.Location.Y + 30 + combo.Top + combo.Height + 8;
        return (host, form, combo, rowAbsX, rowAbsY);
    }

    [Fact]
    public void Shadowed_List_Row_Press_Commits_Row_Selection()
    {
        // RED pre-fix: the press hits shadowBox (later sibling), no commit,
        // dropdown stays open. GREEN: overlay pre-pass routes to the combo.
        var (host, form, combo, rowAbsX, rowAbsY) = OpenShadowedCombo();
        using (combo)
        try
        {
            Assert.Equal(-1, combo.SelectedIndex);
            host.OnMouseDown(rowAbsX, rowAbsY);
            Assert.Equal(0, combo.SelectedIndex);
            Assert.False(combo.DroppedDown);
        }
        finally
        {
            foreach (System.Windows.Forms.Control c in new System.Windows.Forms.Control[] { combo, (System.Windows.Forms.Control)form.Controls["shadowBox"]! })
                form.Controls.Remove(c);
            host.Dispose();
        }
    }

    [Fact]
    public void Unshadowed_List_Row_Press_Still_Commits()
    {
        // Guard: the pre-pass must not break the normal (unshadowed) path.
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var combo = new System.Windows.Forms.ComboBox
        {
            Name = "plainCombo",
            Location = new System.Drawing.Point(300, 10),
            Size = new System.Drawing.Size(100, 21),
            DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList,
        };
        combo.Items.Add(" ");
        combo.Items.Add("FullForm");
        combo.Items.Add("X");
        combo.SelectedIndex = -1;
        form.Controls.Add(combo);
        using (combo)
        try
        {
            bool opened = ComboBoxBrowserHook.HandleForkMouseDown(combo, combo.Width - 5, 5);
            Assert.True(opened && combo.DroppedDown);
            int rowAbsX = form.Location.X + combo.Left + combo.Width / 2;
            int rowAbsY = form.Location.Y + 30 + combo.Top + combo.Height + 8;
            host.OnMouseDown(rowAbsX, rowAbsY);
            Assert.Equal(0, combo.SelectedIndex);
            Assert.False(combo.DroppedDown);
        }
        finally
        {
            form.Controls.Remove(combo);
            host.Dispose();
        }
    }
}
