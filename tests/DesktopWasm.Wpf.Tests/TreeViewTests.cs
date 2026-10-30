using System.Text;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>TreeView v1 tests: hierarchy/flattening, expand/collapse,
/// reference selection (incl. hidden), press/chevron/keyboard input,
/// disabled gating, XAML and synthetic BAML.</summary>
[ResetAmbientState]
public sealed class TreeViewTests
{
    private static (WasmInputRouter Router, WpfInputManager Manager, TreeView Tree,
        TreeViewItem RootA, TreeViewItem RootB, TreeViewItem RootC) Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var tree = new TreeView { Width = 400 };
        var rootA = new TreeViewItem { Header = "A", IsExpanded = true };
        rootA.AddChild(new TreeViewItem { Header = "a1" });
        rootA.AddChild(new TreeViewItem { Header = "a2" });
        var rootB = new TreeViewItem { Header = "B" };
        rootB.AddChild(new TreeViewItem { Header = "b1" });
        var rootC = new TreeViewItem { Header = "C" };
        tree.AddChild(rootA);
        tree.AddChild(rootB);
        tree.AddChild(rootC);
        root.Content = tree;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 600));
        mil.Render(root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        return (router, manager, tree, rootA, rootB, rootC);
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(Handle, key, true, false, false, false));

    [Fact]
    public void Layout_flattens_visible_rows_with_indent()
    {
        var (_, _, tree, _, _, _) = Harness();
        var rows = tree.VisibleRows;
        Assert.Equal(["A", "a1", "a2", "B", "C"], rows.Select(r => r.Header));
        var rowH = rows[0].DesiredSize.Height;
        Assert.Equal(5 * rowH, tree.DesiredSize.Height);
        for (var i = 0; i < 5; i++)
        {
            var r = rows[i].ArrangedRect;
            Assert.Equal(1 + i * rowH, r.Y);
            Assert.Equal(398, r.Width); // stretched inside the 1px border
        }
        // Depth is derived from TreeViewItem ancestors.
        Assert.Equal([0, 1, 1, 0, 0], rows.Select(r => r.Depth));
    }

    [Fact]
    public void Expand_collapse_changes_rows_and_fires_events()
    {
        var (_, _, tree, rootA, rootB, _) = Harness();
        var expanded = 0; var collapsed = 0;
        rootA.Expanded += () => expanded++;
        rootA.Collapsed += () => collapsed++;

        rootB.IsExpanded = true;
        Assert.Equal(["A", "a1", "a2", "B", "b1", "C"], tree.VisibleRows.Select(r => r.Header));

        rootA.IsExpanded = false;
        Assert.Equal(["A", "B", "b1", "C"], tree.VisibleRows.Select(r => r.Header));
        Assert.Equal(1, collapsed);
        rootA.IsExpanded = false; // same value: silent
        Assert.Equal(1, collapsed);

        rootA.IsExpanded = true;
        Assert.Equal(6, tree.VisibleRows.Count);
        Assert.Equal(1, expanded);
    }

    [Fact]
    public void Programmatic_selection_sets_reference_and_fires_once()
    {
        var (_, _, tree, _, _, _) = Harness();
        var fires = 0;
        tree.SelectionChanged += () => fires++;
        var a2 = tree.VisibleRows[2];

        tree.SelectedItem = a2;
        Assert.Same(a2, tree.SelectedItem);
        Assert.True(a2.IsSelected);
        Assert.Equal(1, fires);

        tree.SelectedItem = a2; // same reference: silent
        Assert.Equal(1, fires);

        tree.SelectedItem = null;
        Assert.Null(tree.SelectedItem);
        Assert.Equal(2, fires);
    }

    [Fact]
    public void SelectedItem_keeps_hidden_and_rejects_foreign_nodes()
    {
        var (_, _, tree, _, rootB, _) = Harness();
        var hidden = rootB.Items[0]; // b1 under collapsed B
        tree.SelectedItem = hidden;
        Assert.Same(hidden, tree.SelectedItem); // kept, as in WPF
        Assert.DoesNotContain(hidden, tree.VisibleRows);

        tree.SelectedItem = new TreeViewItem { Header = "foreign" };
        Assert.Null(tree.SelectedItem);
    }

    [Fact]
    public void Collapse_keeps_hidden_selection_until_reexpanded()
    {
        var (_, _, tree, rootA, _, _) = Harness();
        var a1 = rootA.Items[0];
        tree.SelectedItem = a1;
        rootA.IsExpanded = false;
        Assert.Same(a1, tree.SelectedItem); // still selected, just hidden
        Assert.DoesNotContain(a1, tree.VisibleRows);
        rootA.IsExpanded = true;
        Assert.Same(a1, tree.SelectedItem);
        Assert.Contains(a1, tree.VisibleRows);
    }

    [Fact]
    public void Removing_detached_selection_clears_and_fires()
    {
        var (_, _, tree, rootA, _, rootC) = Harness();
        var fires = 0;
        tree.SelectedItem = rootA.Items[1]; // a2
        tree.SelectionChanged += () => fires++;

        Assert.True(tree.RemoveChild(rootC)); // unrelated root: silent
        Assert.Equal("a2", ((TreeViewItem)tree.SelectedItem!).Header);
        Assert.Equal(0, fires);

        Assert.True(rootA.RemoveChild(rootA.Items[1])); // nested remove of a2
        Assert.Null(tree.SelectedItem);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void Click_selects_row_and_chevron_toggles()
    {
        var (router, _, tree, _, rootB, _) = Harness();
        var rowH = tree.VisibleRows[0].DesiredSize.Height;
        var fires = 0;
        tree.SelectionChanged += () => fires++;

        // a1 is visible row 1: press on its text area selects it.
        Down(router, 200, 1 + 1 * rowH + rowH / 2);
        Assert.Equal("a1", ((TreeViewItem)tree.SelectedItem!).Header);
        Assert.Equal(1, fires);

        // Root B is visible row 3 and collapsed: press its expander gutter.
        Down(router, 8, 1 + 3 * rowH + rowH / 2);
        Assert.True(rootB.IsExpanded);
        Assert.Equal(6, tree.VisibleRows.Count);
        // Selection untouched by the toggle.
        Assert.Equal("a1", ((TreeViewItem)tree.SelectedItem!).Header);

        Down(router, 8, 1 + 3 * rowH + rowH / 2);
        Assert.False(rootB.IsExpanded);
        Assert.Equal(5, tree.VisibleRows.Count);
    }

    [Fact]
    public void Click_ignored_when_disabled()
    {
        var (router, _, tree, _, rootB, _) = Harness();
        var rowH = tree.VisibleRows[0].DesiredSize.Height;
        tree.IsEnabled = false;
        Down(router, 200, 1 + 1 * rowH + rowH / 2);
        Assert.Null(tree.SelectedItem);
        Down(router, 8, 1 + 3 * rowH + rowH / 2);
        Assert.False(rootB.IsExpanded);
    }

    [Fact]
    public void Keyboard_navigates_expands_and_collapses_when_focused()
    {
        var (router, manager, tree, rootA, rootB, _) = Harness();
        Key(router, "ArrowDown");
        Assert.Null(tree.SelectedItem); // no focus yet

        Assert.True(manager.TryFocus(tree));
        Key(router, "ArrowDown");
        Assert.Equal("A", ((TreeViewItem)tree.SelectedItem!).Header);
        Key(router, "ArrowDown");
        Assert.Equal("a1", ((TreeViewItem)tree.SelectedItem!).Header);
        Key(router, "ArrowUp");
        Assert.Equal("A", ((TreeViewItem)tree.SelectedItem!).Header);
        Key(router, "End");
        Assert.Equal("C", ((TreeViewItem)tree.SelectedItem!).Header);
        Key(router, "Home");
        Assert.Equal("A", ((TreeViewItem)tree.SelectedItem!).Header);

        // Right on collapsed B expands; Right again moves to first child;
        // Left moves back to the parent; Left collapses.
        tree.SelectedItem = rootB;
        Key(router, "ArrowRight");
        Assert.True(rootB.IsExpanded);
        Assert.Same(rootB, tree.SelectedItem);
        Key(router, "ArrowRight");
        Assert.Equal("b1", ((TreeViewItem)tree.SelectedItem!).Header);
        Key(router, "ArrowLeft");
        Assert.Same(rootB, tree.SelectedItem);
        Key(router, "ArrowLeft");
        Assert.False(rootB.IsExpanded);
        Assert.Same(rootB, tree.SelectedItem);

        // Collapsing a selected expanded node keeps the selection on it.
        tree.SelectedItem = rootA;
        Key(router, "ArrowLeft");
        Assert.False(rootA.IsExpanded);
        Assert.Same(rootA, tree.SelectedItem);
    }

    [Fact]
    public void Programmatic_add_of_wrong_type_throws()
    {
        var (_, _, tree, rootA, _, _) = Harness();
        var ex = Assert.Throws<InvalidOperationException>(() => tree.AddChild(new Button()));
        Assert.Contains("TreeViewItem", ex.Message);
        var ex2 = Assert.Throws<InvalidOperationException>(() => rootA.AddChild(new Button()));
        Assert.Contains("TreeViewItem", ex2.Message);
    }

    [Fact]
    public void Xaml_parses_nested_tree_with_headers_and_expansion()
    {
        var result = XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <TreeView>
                <TreeViewItem Header="a" IsExpanded="True">
                  <TreeViewItem>a1</TreeViewItem>
                </TreeViewItem>
                <TreeViewItem Header="b"/>
              </TreeView>
            </Window>
            """);
        var window = Assert.IsType<Window>(result.Root);
        var tree = Assert.IsType<TreeView>(window.Content);
        Assert.Equal(["a", "a1", "b"], tree.VisibleRows.Select(r => r.Header));
        Assert.True(tree.Items[0].IsExpanded);
        Assert.False(tree.Items[1].IsExpanded);
    }

    [Fact]
    public void Xaml_rejects_SelectedIndex_like_real_wpf()
    {
        // Real WPF fails SelectedIndex on TreeView at compile time (MC3072:
        // TreeView derives from ItemsControl, not Selector).
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <TreeView SelectedIndex="1"/>
            </Window>
            """));
        Assert.Contains("SelectedIndex", ex.Message);
    }

    [Fact]
    public void Xaml_rejects_non_item_children()
    {
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <TreeView><Button Content="nope"/></TreeView>
            </Window>
            """));
        Assert.Contains("TreeViewItem", ex.Message);
        var ex2 = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <TreeView><TreeViewItem Header="a"><Button Content="nope"/></TreeViewItem></TreeView>
            </Window>
            """));
        Assert.Contains("TreeViewItem", ex2.Message);
    }

    // Minimal synthetic-MS-BAML builder (same framing as the other suites).
    private sealed class B
    {
        private readonly List<byte> _b = new();
        public B Raw(params byte[] b) { _b.AddRange(b); return this; }
        public B U16(ushort v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B I16(short v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B Str(string s)
        {
            var ascii = Encoding.ASCII.GetBytes(s);
            Seven(ascii.Length);
            _b.AddRange(ascii);
            return this;
        }
        public B Seven(int v)
        {
            uint u = (uint)v;
            while (u >= 0x80) { _b.Add((byte)(u | 0x80)); u >>= 7; }
            _b.Add((byte)u);
            return this;
        }
        public B Sized(byte type, Action<B> payload)
        {
            var inner = new B();
            payload(inner);
            var p = inner._b;
            if (p.Count + 1 >= 128) throw new InvalidOperationException("test record too large");
            _b.Add(type);
            _b.Add((byte)(p.Count + 1));
            _b.AddRange(p);
            return this;
        }
        public byte[] ToArray() => _b.ToArray();
    }

    private static byte[] Header() => new B()
        .Raw(0x0C, 0x00, 0x00, 0x00)
        .Raw(0x4D, 0x00, 0x53, 0x00, 0x42, 0x00, 0x41, 0x00, 0x4D, 0x00, 0x4C, 0x00)
        .U16(0).U16(0x60)
        .Raw(new byte[] { 0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00, 0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 })
        .ToArray();

    [Fact]
    public void Baml_treeview_loads_hierarchy_and_expansion()
    {
        const short tTreeView = -686; // verified against a real TreeView BAML fixture
        const short tTreeViewItem = -687;
        const short mItems = -192; // ItemsControl.Items
        const short mContent = -14; // ContentControl.Content
        const short mHeader = -71; // HeaderedContentControl.Header
        const short cString = -615;
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(mContent);
        b.Raw(0x03).I16(tTreeView).Raw(0x00);
        b.Raw(0x2E).I16(mItems);
        // Node a, expanded via per-file attribute (any bool converter).
        b.Raw(0x03).I16(tTreeViewItem).Raw(0x00);
        b.Sized(0x1F, p => p.U16(0).I16(tTreeViewItem).Raw(0x00).Str("IsExpanded"));
        b.Sized(0x24, p => p.I16(0).Str("True").I16(0));
        b.Sized(0x24, p => p.I16(mHeader).Str("a").I16(cString));
        b.Raw(0x2E).I16(mItems);
        b.Raw(0x03).I16(tTreeViewItem).Raw(0x00);
        b.Sized(0x24, p => p.I16(mHeader).Str("a1").I16(cString));
        b.Raw(0x04).Raw(0x04);
        // Node b, collapsed.
        b.Raw(0x03).I16(tTreeViewItem).Raw(0x00);
        b.Sized(0x24, p => p.I16(mHeader).Str("b").I16(cString));
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);

        using var stream = new MemoryStream(b.ToArray());
        var window = XamlLoader.LoadComponent<Window>(stream);
        var tree = Assert.IsType<TreeView>(window.Content);
        Assert.Equal(["a", "a1", "b"], tree.VisibleRows.Select(r => r.Header));
        Assert.True(tree.Items[0].IsExpanded);
        Assert.False(tree.Items[1].IsExpanded);
    }

    [Fact]
    public void Baml_rejects_non_item_node()
    {
        const short tTreeView = -686;
        const short tTextBlock = -638;
        const short mItems = -192;
        const short mContent = -14;
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(mContent);
        b.Raw(0x03).I16(tTreeView).Raw(0x00);
        b.Raw(0x2E).I16(mItems);
        b.Raw(0x03).I16(tTextBlock).Raw(0x00);
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);

        using var stream = new MemoryStream(b.ToArray());
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("TreeViewItem", ex.Message);
    }
}
