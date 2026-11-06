// PUSH-1106-runtime RTE font-dropdown: editable drop-downs whose hosted
// editor covers the arrow zone must still open on arrow press (served:
// Telerik DropDownListFont arrow click resolves to HostedTextBoxBase,
// focuses, never opens). (1) plain fork stand-in: editable combo + a
// covering edit child, owner climb runs the owner arrow logic; (2) fake
// vendor owner + element tree whose arrow element carries ShowPopup
// (element fallback, reflection-only, no vendor reference); (3) guard:
// editable field press still does not open (desktop-faithful R2 rule).
using System.Drawing;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;

public sealed class DropDownArrowOwnerTests
{
    private sealed class FakeElementTree
    {
        public Func<Point, object?>? Resolver { get; set; }
        public object? GetElementAtPoint(Point p) => Resolver != null ? Resolver(p) : null;
    }

    private sealed class FakeArrowElement
    {
        public bool Opened { get; private set; }
        public object? Parent { get; set; }
        public void ShowPopup() { Opened = true; }
    }

    private sealed class FakeVendorOwner : System.Windows.Forms.Control
    {
        public FakeElementTree? ElementTree { get; set; }
    }

    private sealed class HostedFakeBox : System.Windows.Forms.Control
    {
    }

    private sealed class FakePopupFlagElement
    {
        public bool IsPopupOpen { get; set; }
        public object? Parent { get; set; }
    }

    private static (WinFormsAppHost host, System.Windows.Forms.Form form) OpenHost()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        return (host, form);
    }

    [Fact]
    public void EditableCombo_CoveringEditChild_ArrowPress_Opens_Owner()
    {
        // Served Telerik shape, plain-fork stand-in: the hosted editor
        // covers the arrow zone, so HitTestDeep resolves the child, not
        // the owner. RED pre-fix: hook skipped, list stays closed.
        var (host, form) = OpenHost();
        var combo = new System.Windows.Forms.ComboBox
        {
            Name = "covCombo",
            Location = new Point(10, 10),
            Size = new Size(141, 21),
            DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDown,
        };
        combo.Items.Add("1");
        combo.Items.Add("2");
        var cover = new System.Windows.Forms.TextBox
        {
            Name = "coverEdit",
            Location = new Point(combo.Width - 20, 0),
            Size = new Size(20, combo.Height),
        };
        combo.Controls.Add(cover);
        form.Controls.Add(combo);
        using (combo)
        try
        {
            int ax = form.Location.X + combo.Left + combo.Width - 8;
            int ay = form.Location.Y + 30 + combo.Top + 8;
            host.OnMouseDown(ax, ay);
            Assert.True(combo.DroppedDown, "arrow press through covering edit child must open the owner combo");
        }
        finally
        {
            form.Controls.Remove(combo);
            host.Dispose();
        }
    }

    [Fact]
    public void EditableCombo_FieldPress_Does_Not_Open()
    {
        // Guard: the owner climb must not open on field presses
        // (desktop-faithful R2 rule, now at host-dispatch level).
        var (host, form) = OpenHost();
        var combo = new System.Windows.Forms.ComboBox
        {
            Name = "fldCombo",
            Location = new Point(10, 40),
            Size = new Size(141, 21),
            DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDown,
        };
        combo.Items.Add("1");
        combo.Items.Add("2");
        form.Controls.Add(combo);
        using (combo)
        try
        {
            int fx = form.Location.X + combo.Left + 50;
            int fy = form.Location.Y + 30 + combo.Top + 8;
            host.OnMouseDown(fx, fy);
            Assert.False(combo.DroppedDown, "editable field press must not open");
        }
        finally
        {
            form.Controls.Remove(combo);
            host.Dispose();
        }
    }

    [Fact]
    public void VendorHostedEditor_ArrowElement_Opens_Via_ElementTree()
    {
        // Fake vendor owner (no vendor reference): hosted-editor hit +
        // element tree resolving the arrow element with ShowPopup.
        // RED pre-fix: default dispatch only, element never driven.
        var (host, form) = OpenHost();
        var arrow = new FakeArrowElement();
        var owner = new FakeVendorOwner
        {
            Name = "fakeOwner",
            Location = new Point(10, 70),
            Size = new Size(200, 60),
            ElementTree = new FakeElementTree { Resolver = _ => arrow },
        };
        var box = new HostedFakeBox
        {
            Name = "fakeBox",
            Location = new Point(5, 5),
            Size = new Size(100, 20),
        };
        owner.Controls.Add(box);
        form.Controls.Add(owner);
        using (owner)
        try
        {
            int bx = form.Location.X + owner.Left + box.Left + box.Width / 2;
            int by = form.Location.Y + 30 + owner.Top + box.Top + box.Height / 2;
            host.OnMouseDown(bx, by);
            Assert.True(arrow.Opened, "arrow element behind hosted editor must be driven open");
        }
        finally
        {
            form.Controls.Remove(owner);
            host.Dispose();
        }
    }

    [Fact]
    public void VendorHostedEditor_FlagProperty_Opens_Via_ElementTree()
    {
        // Same gate, property-shape affordance (IsPopupOpen) instead of a
        // ShowPopup method: documents the fallback order boundary.
        var (host, form) = OpenHost();
        var flag = new FakePopupFlagElement();
        var owner = new FakeVendorOwner
        {
            Name = "flagOwner",
            Location = new Point(220, 70),
            Size = new Size(200, 60),
            ElementTree = new FakeElementTree { Resolver = _ => flag },
        };
        var box = new HostedFakeBox
        {
            Name = "flagBox",
            Location = new Point(5, 5),
            Size = new Size(100, 20),
        };
        owner.Controls.Add(box);
        form.Controls.Add(owner);
        using (owner)
        try
        {
            int bx = form.Location.X + owner.Left + box.Left + box.Width / 2;
            int by = form.Location.Y + 30 + owner.Top + box.Top + box.Height / 2;
            host.OnMouseDown(bx, by);
            Assert.True(flag.IsPopupOpen, "flag-shaped open affordance must be set");
        }
        finally
        {
            form.Controls.Remove(owner);
            host.Dispose();
        }
    }

    [Fact]
    public void VendorHostedEditor_AltDown_Opens_Via_Focus()
    {
        // Keyboard route: focus the hosted editor with a real click, then
        // Alt+Down opens the owner's list through the element tree. No
        // coordinates cross the test boundary: the query point is
        // synthesized from the focused box's own Bounds (arrow zone).
        // RED pre-fix: no focus-chord branch exists, element never driven.
        var (host, form) = OpenHost();
        var arrow = new FakeArrowElement();
        var owner = new FakeVendorOwner
        {
            Name = "kbdOwner",
            Location = new Point(10, 130),
            Size = new Size(200, 60),
            ElementTree = new FakeElementTree { Resolver = p => (p.X >= 85 ? arrow : null) },
        };
        var box = new HostedFakeBox
        {
            Name = "kbdBox",
            Location = new Point(5, 5),
            Size = new Size(100, 20),
        };
        owner.Controls.Add(box);
        form.Controls.Add(owner);
        using (owner)
        try
        {
            int cx = form.Location.X + owner.Left + box.Left + box.Width / 2;
            int cy = form.Location.Y + 30 + owner.Top + box.Top + box.Height / 2;
            host.OnMouseDown(cx, cy);
            host.OnKeyDown("ArrowDown", alt: true);
            Assert.True(arrow.Opened, "Alt+Down on focused hosted editor must open the owner's list");
        }
        finally
        {
            form.Controls.Remove(owner);
            host.Dispose();
        }
    }

    [Fact]
    public void VendorHostedEditor_PlainKey_Reaches_Box()
    {
        // Guard: ordinary typing keys are never consumed by the chord
        // branch; the focused editor still receives them.
        var (host, form) = OpenHost();
        var arrow = new FakeArrowElement();
        var owner = new FakeVendorOwner
        {
            Name = "typeOwner",
            Location = new Point(230, 130),
            Size = new Size(200, 60),
            ElementTree = new FakeElementTree { Resolver = p => (p.X >= 85 ? arrow : null) },
        };
        var box = new HostedFakeBox
        {
            Name = "typeBox",
            Location = new Point(5, 5),
            Size = new Size(100, 20),
        };
        owner.Controls.Add(box);
        form.Controls.Add(owner);
        using (owner)
        try
        {
            bool gotKey = false;
            box.KeyDown += (_, _) => gotKey = true;
            int cx = form.Location.X + owner.Left + box.Left + box.Width / 2;
            int cy = form.Location.Y + 30 + owner.Top + box.Top + box.Height / 2;
            host.OnMouseDown(cx, cy);
            host.OnKeyDown("a");
            Assert.False(arrow.Opened, "plain typing key must not open the list");
            Assert.True(gotKey, "plain typing key must still reach the focused editor");
        }
        finally
        {
            form.Controls.Remove(owner);
            host.Dispose();
        }
    }
}
