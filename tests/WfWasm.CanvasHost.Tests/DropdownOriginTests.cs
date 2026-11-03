using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsForm = System.Windows.Forms.Form;

public sealed class DropdownOriginTests
{
    // USERFIX-shell menu displacement (browser-proven ~38px gap + ~10px
    // shift): ToolStripDropDown.Bounds is already virtual-screen
    // (canvas-absolute) space. The host must NOT add the owner origin again
    // when rendering or hit-testing; the tracked origin must equal
    // drop.Location, and a point inside Location must hit.
    [Fact]
    public void Tracked_Dropdown_Origin_Equals_Screen_Location()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 500, Height = 400 };
        var strip = new System.Windows.Forms.MenuStrip();
        var mi = new System.Windows.Forms.ToolStripMenuItem("M");
        var leaf = new System.Windows.Forms.ToolStripMenuItem("Leaf");
        mi.DropDownItems.Add(leaf);
        strip.Items.Add(mi);
        dlg.Controls.Add(strip);
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 100; dlg.Top = 100; dlg.Show();
        try
        {
            strip.PerformLayout();
            WaitForLayout(() => mi.Bounds.Width, "menu item");
            int mx = dlg.Left + strip.Left + mi.Bounds.X + (mi.Bounds.Width / 2);
            int my = dlg.Top + 30 + strip.Top + mi.Bounds.Y + (mi.Bounds.Height / 2);
            host.OnClick(mx, my);
            var drop = mi.DropDown;
            Assert.True(drop.Visible, "drop opened");
            var entries = GetOpenDropdowns(host);
            (int ox, int oy) = FindOrigin(entries, drop);
            Assert.Equal(drop.Location.X, ox);
            Assert.Equal(drop.Location.Y, oy);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Dropdown_Hit_Uses_Screen_Location_Without_Double_Origin()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 500, Height = 400 };
        var strip = new System.Windows.Forms.MenuStrip();
        var mi = new System.Windows.Forms.ToolStripMenuItem("M");
        var leaf = new System.Windows.Forms.ToolStripMenuItem("Leaf");
        mi.DropDownItems.Add(leaf);
        strip.Items.Add(mi);
        dlg.Controls.Add(strip);
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 100; dlg.Top = 100; dlg.Show();
        try
        {
            strip.PerformLayout();
            WaitForLayout(() => mi.Bounds.Width, "menu item");
            int mx = dlg.Left + strip.Left + mi.Bounds.X + (mi.Bounds.Width / 2);
            int my = dlg.Top + 30 + strip.Top + mi.Bounds.Y + (mi.Bounds.Height / 2);
            host.OnClick(mx, my);
            var drop = mi.DropDown;
            Assert.True(drop.Visible, "drop opened");
            drop.Width = 200;
            drop.Height = 100;
            drop.PerformLayout();
            WaitForLayout(() => leaf.Bounds.Width, "leaf item");
            // Inside the screen-space drop rect, near its top-left: must hit
            // under the single-origin contract. Under the old double-origin
            // contract this point misses (needs owner origin added again).
            int px = drop.Location.X + 5;
            int py = drop.Location.Y + 5;
            Assert.True(TryHit(host, px, py, out int dx, out int dy), $"point ({px},{py}) inside drop at {drop.Location} must hit");
            Assert.Equal(drop.Location.X, dx);
            Assert.Equal(drop.Location.Y, dy);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    private static void WaitForLayout(System.Func<int> width, string what)
    {
        for (int i = 0; i < 100 && width() <= 0; i++)
        {
            try { System.Windows.Forms.Application.DoEvents(); }
            catch { }
            System.Threading.Thread.Sleep(20);
        }
        Assert.True(width() > 0, what + " never laid out");
    }

    private static System.Collections.IEnumerable GetOpenDropdowns(WinFormsAppHost host)
    {
        System.Reflection.MethodInfo m = typeof(WinFormsAppHost).GetMethod("GetOpenDropdowns",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (System.Collections.IEnumerable)m.Invoke(host, null)!;
    }

    private static (int ox, int oy) FindOrigin(System.Collections.IEnumerable entries, object drop)
    {
        foreach (object e in entries)
        {
            var t = e.GetType();
            object? d = t.GetField("Item1")!.GetValue(e) ?? t.GetField("Drop")!.GetValue(e);
            if (ReferenceEquals(d, drop))
            {
                int ox = (int)(t.GetField("Item2")!.GetValue(e) ?? t.GetField("Ox")!.GetValue(e))!;
                int oy = (int)(t.GetField("Item3")!.GetValue(e) ?? t.GetField("Oy")!.GetValue(e))!;
                return (ox, oy);
            }
        }
        Assert.Fail("drop not tracked");
        return (0, 0);
    }

    [Fact]
    public void Dropdown_Render_Uses_Screen_Location_Without_Double_Origin()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 500, Height = 400 };
        var strip = new System.Windows.Forms.MenuStrip();
        var mi = new System.Windows.Forms.ToolStripMenuItem("M");
        var leaf = new System.Windows.Forms.ToolStripMenuItem("Leaf");
        mi.DropDownItems.Add(leaf);
        strip.Items.Add(mi);
        dlg.Controls.Add(strip);
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 100; dlg.Top = 100; dlg.Show();
        try
        {
            strip.PerformLayout();
            WaitForLayout(() => mi.Bounds.Width, "menu item");
            int mx = dlg.Left + strip.Left + mi.Bounds.X + (mi.Bounds.Width / 2);
            int my = dlg.Top + 30 + strip.Top + mi.Bounds.Y + (mi.Bounds.Height / 2);
            host.OnClick(mx, my);
            var drop = mi.DropDown;
            Assert.True(drop.Visible, "drop opened");
            var canvas = new RecordingCanvas();
            System.Reflection.MethodInfo render = typeof(WinFormsAppHost).GetMethod("RenderOpenDropdowns",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            render.Invoke(host, new object[] { canvas });
            Assert.Contains(canvas.Fills, f =>
                f.x == drop.Location.X && f.y == drop.Location.Y &&
                f.w == drop.Width && f.h == drop.Height);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Submenu_Owner_Click_Keeps_Chain_Open()
    {
        // USERFIX-shell submenu usability (same tracked-origin contract):
        // hover opens the submenu; clicking its owner item must keep the
        // chain open, not toggle the submenu shut and cascade-close all.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 500, Height = 400 };
        var strip = new System.Windows.Forms.MenuStrip();
        var mi = new System.Windows.Forms.ToolStripMenuItem("M");
        var leaf = new System.Windows.Forms.ToolStripMenuItem("Leaf");
        var sub = new System.Windows.Forms.ToolStripMenuItem("Sub");
        leaf.DropDownItems.Add(sub);
        mi.DropDownItems.Add(leaf);
        strip.Items.Add(mi);
        dlg.Controls.Add(strip);
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 100; dlg.Top = 100; dlg.Show();
        try
        {
            strip.PerformLayout();
            WaitForLayout(() => mi.Bounds.Width, "menu item");
            int mx = dlg.Left + strip.Left + mi.Bounds.X + (mi.Bounds.Width / 2);
            int my = dlg.Top + 30 + strip.Top + mi.Bounds.Y + (mi.Bounds.Height / 2);
            host.OnClick(mx, my);
            var drop = mi.DropDown;
            Assert.True(drop.Visible, "drop opened");
            drop.Width = 200;
            drop.Height = 100;
            drop.PerformLayout();
            WaitForLayout(() => leaf.Bounds.Width, "leaf item");
            int lx = drop.Location.X + leaf.Bounds.X + (leaf.Bounds.Width / 2);
            int ly = drop.Location.Y + leaf.Bounds.Y + (leaf.Bounds.Height / 2);
            host.OnMouseMove(lx, ly);
            Assert.True(leaf.DropDown.Visible, "submenu opened on hover");
            string s0 = Snapshot(host, drop, leaf.DropDown);
            host.OnMouseDown(lx, ly);
            string s1 = Snapshot(host, drop, leaf.DropDown);
            host.OnMouseUp(lx, ly);
            string s2 = Snapshot(host, drop, leaf.DropDown);
            host.OnClick(lx, ly);
            string snap = Snapshot(host, drop, leaf.DropDown);
            string trace = $"hover=[{s0}] down=[{s1}] up=[{s2}] click=[{snap}]";
            Assert.True(drop.Visible, "parent stays open after owner click; " + trace);
            Assert.True(leaf.DropDown.Visible, "submenu stays open after owner click; " + trace);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    private sealed class RecordingCanvas : WfWasm.Shim.ICanvas
    {
        public readonly System.Collections.Generic.List<(float x, float y, float w, float h)> Fills = new();
        public void FillRectangle(float x, float y, float width, float height, uint rgba) => Fills.Add((x, y, width, height));
        public void FillRoundedRectangle(float x, float y, float width, float height, float radius, uint rgba) { }
        public void FillGradientVertical(float x, float y, float width, float height, uint topRgba, uint bottomRgba) { }
        public void DrawLine(float x1, float y1, float x2, float y2, float width, uint rgba) { }
        public void DrawEllipse(float x, float y, float width, float height, float lineWidth, uint outlineRgba, uint? fillRgba = null) { }
        public void DrawText(string text, float x, float y, string fontCss, uint rgba) { }
        public void DrawImage(byte[] rgba, int width, int height, float x, float y, float dw, float dh) { }
        public void DrawTextAligned(string text, float x, float y, string fontCss, uint rgba, WfWasm.Shim.TextHAlign hAlign, WfWasm.Shim.TextVAlign vAlign, int mnemonicIndex = -1, float clipX = 0, float clipY = 0, float clipWidth = 0, float clipHeight = 0) { }
    }

    private static string Snapshot(WinFormsAppHost host, object fileDrop, object subDrop)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (object e in GetOpenDropdowns(host))
        {
            var t = e.GetType();
            object? d = t.GetField("Item1")!.GetValue(e);
            int ox = (int)t.GetField("Item2")!.GetValue(e)!;
            int oy = (int)t.GetField("Item3")!.GetValue(e)!;
            string name = "unknown";
            bool vis = false;
            int lx = 0, ly = 0;
            try
            {
                var dd = (System.Windows.Forms.ToolStripDropDown)d!;
                name = dd.Name;
                vis = dd.Visible;
                lx = dd.Location.X;
                ly = dd.Location.Y;
            }
            catch { }
            string mark = ReferenceEquals(d, fileDrop) ? "[file]" : (ReferenceEquals(d, subDrop) ? "[sub]" : "[?]");
            parts.Add($"{mark}{name} vis={vis} loc=({lx},{ly}) trk=({ox},{oy})");
        }
        return string.Join(" ", parts);
    }

    private static bool TryHit(WinFormsAppHost host, int x, int y, out int dx, out int dy)
    {
        System.Reflection.MethodInfo m = typeof(WinFormsAppHost).GetMethod("TryHitDropdown",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        object?[] args = new object?[] { x, y, null, null, 0, 0 };
        bool hit = (bool)m.Invoke(host, args)!;
        dx = (int)args[4]!;
        dy = (int)args[5]!;
        return hit;
    }
}
