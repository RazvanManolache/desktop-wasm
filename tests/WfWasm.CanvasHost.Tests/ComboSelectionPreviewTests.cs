// RT-ADGV-SEL-1326-j7x1: open-preview selection must survive a lost
// SelectedIndex (served wasm condition: open-state arrows visibly highlight
// but the native index snaps back; Enter then commits stale index 0).
// The hook must remember the previewed row per combo and commit it.
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;

public sealed class ComboSelectionPreviewTests
{
    private static System.Windows.Forms.ComboBox MakeCombo()
    {
        var combo = new System.Windows.Forms.ComboBox
        {
            Name = "selCombo",
            Location = new System.Drawing.Point(30, 200),
            Size = new System.Drawing.Size(195, 24),
            DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList,
            Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25f),
        };
        combo.Items.Add(" ");
        combo.Items.Add("FullForm");
        combo.Items.Add("X");
        combo.SelectedIndex = 0;
        return combo;
    }

    private static void OpenViaArrow(System.Windows.Forms.ComboBox combo)
    {
        bool consumed = ComboBoxBrowserHook.HandleForkMouseDown(combo, combo.Width - 5, 5);
        Assert.True(consumed);
        Assert.True(combo.DroppedDown);
    }

    [Fact]
    public void OpenPreview_LostSelection_Enter_Commits_PreviewIndex()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        using var combo = MakeCombo();
        form.Controls.Add(combo);
        try
        {
            OpenViaArrow(combo);
            var down = new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Down);
            Assert.True(ComboBoxBrowserHook.HandleForkBrowserKey(combo, down));
            // Served wasm condition: the open preview does not stick.
            combo.SelectedIndex = 0;
            var enter = new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Enter);
            Assert.True(ComboBoxBrowserHook.HandleForkBrowserKey(combo, enter));
            Assert.Equal(1, combo.SelectedIndex);
            Assert.False(combo.DroppedDown);
        }
        finally
        {
            form.Controls.Remove(combo);
        }
    }

    [Fact]
    public void OpenPreview_KeptSelection_Enter_Commits_PreviewIndex()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        using var combo = MakeCombo();
        form.Controls.Add(combo);
        try
        {
            OpenViaArrow(combo);
            var down = new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Down);
            Assert.True(ComboBoxBrowserHook.HandleForkBrowserKey(combo, down));
            var enter = new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Enter);
            Assert.True(ComboBoxBrowserHook.HandleForkBrowserKey(combo, enter));
            Assert.Equal(1, combo.SelectedIndex);
            Assert.False(combo.DroppedDown);
        }
        finally
        {
            form.Controls.Remove(combo);
        }
    }

    [Fact]
    public void OpenPreview_Escape_Restores_Snapshot_And_Clears_Preview()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        using var combo = MakeCombo();
        form.Controls.Add(combo);
        try
        {
            OpenViaArrow(combo);
            var down = new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Down);
            Assert.True(ComboBoxBrowserHook.HandleForkBrowserKey(combo, down));
            var esc = new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Escape);
            Assert.True(ComboBoxBrowserHook.HandleForkBrowserKey(combo, esc));
            Assert.False(combo.DroppedDown);
            // Reopen + Enter with no fresh preview commits the restored 0.
            OpenViaArrow(combo);
            var enter = new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Enter);
            Assert.True(ComboBoxBrowserHook.HandleForkBrowserKey(combo, enter));
            Assert.Equal(0, combo.SelectedIndex);
        }
        finally
        {
            form.Controls.Remove(combo);
        }
    }
}
