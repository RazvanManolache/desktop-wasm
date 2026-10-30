using System.Text;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>ListBox v1 tests: row layout, single selection (programmatic,
/// press, keyboard, pending/late), disabled gating, XAML and synthetic BAML.</summary>
[ResetAmbientState]
public sealed class ListBoxTests
{
    private static (WasmInputRouter Router, WpfInputManager Manager, ListBox List) Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var list = new ListBox { Width = 400 };
        for (var i = 0; i < 5; i++) list.AddChild(new ListBoxItem { Content = $"R{i}" });
        root.Content = list;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 600));
        mil.Render(root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        return (router, manager, list);
    }

    [Fact]
    public void Generated_rows_paint_item_text()
    {
        // Drawer shape: rows generated from ItemsSource must reach the frame
        // through the list's own choreography, or converted item lists stay
        // blank even with bound data and laid-out rows.
        var root = new Window();
        var list = new ListBox { Width = 400, Height = 200 };
        list.ItemsSource = new[] { "Alpha", "Beta" };
        root.Content = list;
        var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
        host.AddWindow(root, new WpfSize(800, 600));
        host.Tick(new FrameContext(1, 0));
        Assert.Contains(host.CurrentCommands,
            c => c is DesktopWasm.Graphics.DrawCommand.DrawText t && t.Text.Contains("Alpha"));
    }

    [Fact]
    public void Styled_rows_paint_item_text()
    {
        // Same generation, but with an item container style carrying a
        // template (the MaterialDesign drawer shape): the style chrome must
        // not swallow the row content.
        Application.Current = new BootProbe.ProbeApp();
        try
        {
            var dict = XamlLoader.LoadResourceDictionary(
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<Style TargetType=\"ListBoxItem\">" +
                "<Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"ListBoxItem\">" +
                "<Border><ContentPresenter Content=\"{TemplateBinding Content}\" /></Border>" +
                "</ControlTemplate></Setter.Value></Setter></Style>" +
                "</ResourceDictionary>");
            var root = new Window();
            var list = new ListBox { Width = 400, Height = 200 };
            list.ItemContainerStyle = Assert.IsType<Style>(dict[typeof(ListBoxItem)]);
            list.ItemsSource = new[] { "Alpha", "Beta" };
            root.Content = list;
            var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
            host.AddWindow(root, new WpfSize(800, 600));
            host.Tick(new FrameContext(1, 0));
            Assert.Contains(host.CurrentCommands,
                c => c is DesktopWasm.Graphics.DrawCommand.DrawText t && t.Text.Contains("Alpha"));
        }
        finally
        {
            Application.Current = null;
        }
    }

    [Fact]
    public void Templated_list_paints_rows_through_chrome()
    {
        // The converted drawer shape: the list itself carries a template
        // (Border/ScrollViewer chrome). The template owns the fill/frame,
        // but the generated rows parent to the list, not into the template,
        // so they must still paint through the row loop.
        Application.Current = new BootProbe.ProbeApp();
        try
        {
            var dict = XamlLoader.LoadResourceDictionary(
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<Style TargetType=\"ListBox\">" +
                "<Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"ListBox\">" +
                "<Border><ScrollViewer /></Border>" +
                "</ControlTemplate></Setter.Value></Setter></Style>" +
                "</ResourceDictionary>");
            var root = new Window();
            var list = new ListBox { Width = 400, Height = 200 };
            list.Style = Assert.IsType<Style>(dict[typeof(ListBox)]);
            list.ItemsSource = new[] { "Alpha", "Beta" };
            root.Content = list;
            var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
            host.AddWindow(root, new WpfSize(800, 600));
            host.Tick(new FrameContext(1, 0));
            Assert.Contains(host.CurrentCommands,
                c => c is DesktopWasm.Graphics.DrawCommand.DrawText t && t.Text.Contains("Alpha"));
        }
        finally
        {
            Application.Current = null;
        }
    }

    [Fact]
    public void IsSelected_dp_registered_for_converted_handlers()
    {
        // Converted MaterialDesign row-click handlers reference
        // ListBoxItem.IsSelectedProperty; without the registration they die
        // with MissingFieldException instead of selecting.
        Assert.NotNull(DependencyProperty.FromName(typeof(ListBoxItem), "IsSelected"));
    }

    [Fact]
    public void IsSelected_syncs_with_owner_selection()
    {
        var (_, _, list) = Harness();
        var dp = DependencyProperty.FromName(typeof(ListBoxItem), "IsSelected");
        Assert.NotNull(dp);
        list.SelectedIndex = 1;
        Assert.True((bool)list.Items[1].GetValue(dp));
        Assert.False((bool)list.Items[0].GetValue(dp));
        list.Items[0].SetValue(dp, true);
        Assert.Equal(0, list.SelectedIndex);
        Assert.True((bool)list.Items[0].GetValue(dp));
    }

    private const string RowTriggerStyleXaml =
        "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
        "<Style TargetType=\"ListBoxItem\">" +
        "<Setter Property=\"Template\"><Setter.Value>" +
        "<ControlTemplate TargetType=\"ListBoxItem\">" +
        "<Border x:Name=\"Bd\" />" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property=\"IsSelected\" Value=\"True\">" +
        "<Setter TargetName=\"Bd\" Property=\"Background\" Value=\"{StaticResource RowBrush}\" />" +
        "</Trigger></ControlTemplate.Triggers>" +
        "</ControlTemplate></Setter.Value></Setter></Style>" +
        "</ResourceDictionary>";

    private static Border SelectedRowPart(ListBox list, int index)
    {
        var row = list.Items[index];
        foreach (var child in row.LogicalChildren)
            if (child is Border border)
                return border;
        throw new InvalidOperationException("Row template part 'Bd' was not inflated.");
    }

    [Fact]
    public void Selected_flat_row_template_trigger_resolves_ancestor_resource()
    {
        // The OxyPlot ListboxItemStyle shape on a plain list: a template
        // trigger setter resolving {StaticResource} walks from the part up
        // through the row to the window. Attached rows must resolve.
        Application.Current = new BootProbe.ProbeApp();
        try
        {
            var brush = new SolidColorBrush(Color.FromArgb(255, 203, 230, 239));
            var dict = XamlLoader.LoadResourceDictionary(RowTriggerStyleXaml);
            var root = new Window();
            root.Resources.Add("RowBrush", brush);
            var list = new ListBox { Width = 400, Height = 200 };
            list.ItemContainerStyle = Assert.IsType<Style>(dict[typeof(ListBoxItem)]);
            list.ItemsSource = new[] { "Alpha", "Beta" };
            root.Content = list;
            list.SelectedIndex = 0;
            Assert.Same(brush, SelectedRowPart(list, 0).Background);
        }
        finally
        {
            Application.Current = null;
        }
    }

    [Fact]
    public void Selected_grouped_row_template_trigger_resolves_ancestor_resource()
    {
        // Same trigger shape over grouped rows (the staged OxyPlot examples
        // list: 754 rows across 71 category groups): the row's part walk
        // must reach the window through the group container, or selecting
        // any example throws StaticResource instead of highlighting.
        Application.Current = new BootProbe.ProbeApp();
        try
        {
            var brush = new SolidColorBrush(Color.FromArgb(255, 203, 230, 239));
            var dict = XamlLoader.LoadResourceDictionary(RowTriggerStyleXaml);
            var root = new Window();
            root.Resources.Add("RowBrush", brush);
            var items = new[]
            {
                new { Category = "Axes", Name = "a1" },
                new { Category = "Axes", Name = "a2" },
                new { Category = "Plot", Name = "p1" },
            };
            var view = CollectionViewSource.GetDefaultView(items);
            Assert.NotNull(view);
            view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
            // The staged OxyPlot GroupStyle templates each GroupItem around
            // an Expander + ItemsPresenter slot (not the fallback stack).
            var groupDict = XamlLoader.LoadResourceDictionary(
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Style TargetType=\"GroupItem\">" +
                "<Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"GroupItem\">" +
                "<Expander><ItemsPresenter /></Expander>" +
                "</ControlTemplate></Setter.Value></Setter></Style>" +
                "</ResourceDictionary>");
            var list = new ListBox { Width = 400, Height = 200 };
            list.GroupStyle.Add(new GroupStyle
            {
                ContainerStyle = Assert.IsType<Style>(groupDict[typeof(GroupItem)]),
            });
            list.ItemContainerStyle = Assert.IsType<Style>(dict[typeof(ListBoxItem)]);
            list.ItemsSource = view;
            root.Content = list;
            Assert.Equal(3, list.Items.Count);
            list.SelectedIndex = 0;
            Assert.Same(brush, SelectedRowPart(list, 0).Background);
        }
        finally
        {
            Application.Current = null;
        }
    }

    [Fact]
    public void Tab_skips_rows_but_programmatic_focus_still_lands()
    {
        // Rows stay focusable (arrows/pointer/programmatic) but leave the
        // Tab order: Tab must walk past a generated list to the next
        // control instead of sinking into every row.
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window { Width = 400, Height = 300 };
        var panel = new StackPanel();
        var before = new Button { Content = "Before", Width = 100, Height = 30 };
        var list = new ListBox { Width = 400, Height = 100 };
        list.ItemsSource = new[] { "Alpha", "Beta", "Gamma" };
        var after = new Button { Content = "After", Width = 100, Height = 30 };
        panel.AddChild(before);
        panel.AddChild(list);
        panel.AddChild(after);
        root.Content = panel;
        manager.AddRoot(root);
        Assert.True(manager.TryFocus(before));
        router.DispatchKeyboard(new WasmKeyArgs(Handle, "Tab", true, false, false, false));
        Assert.Same(list, manager.Focused);
        router.DispatchKeyboard(new WasmKeyArgs(Handle, "Tab", true, false, false, false));
        Assert.Same(after, manager.Focused);
        Assert.True(manager.TryFocus(list.Items[0]));
        Assert.Same(list.Items[0], manager.Focused);
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(Handle, key, true, false, false, false));

    [Fact]
    public void Layout_stacks_rows_inside_border()
    {
        var (_, _, list) = Harness();
        var rowH = list.Items[0].DesiredSize.Height;
        Assert.Equal(5 * rowH, list.DesiredSize.Height);
        for (var i = 0; i < 5; i++)
        {
            var r = list.Items[i].ArrangedRect;
            Assert.Equal(1 + i * rowH, r.Y);
            Assert.Equal(398, r.Width); // stretched inside the 1px border
        }
    }

    [Fact]
    public void Programmatic_selection_sets_item_and_fires_once()
    {
        var (_, _, list) = Harness();
        var fires = 0;
        list.SelectionChanged += () => fires++;

        list.SelectedIndex = 2;
        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal("R2", list.SelectedItem?.Content);
        Assert.True(list.Items[2].IsSelected);
        Assert.False(list.Items[1].IsSelected);
        Assert.Equal(1, fires);

        list.SelectedIndex = 2; // same value: silent
        Assert.Equal(1, fires);

        list.SelectedIndex = -1;
        Assert.Null(list.SelectedItem);
        Assert.Equal(2, fires);
    }

    [Fact]
    public void Out_of_range_selection_coerces_to_none()
    {
        var (_, _, list) = Harness();
        var fires = 0;
        list.SelectionChanged += () => fires++;

        list.SelectedIndex = 99;
        Assert.Equal(-1, list.SelectedIndex);
        Assert.Null(list.SelectedItem);
        Assert.Equal(0, fires); // never effective: no event

        list.SelectedIndex = -5;
        Assert.Equal(-1, list.SelectedIndex);
        Assert.Equal(0, fires);
    }

    [Fact]
    public void Selection_materializes_when_rows_arrive_late()
    {
        // XAML/BAML attributes arrive before child rows (as in real WPF):
        // the raw index is kept and takes effect once rows exist.
        var list = new ListBox();
        var fires = 0;
        list.SelectionChanged += () => fires++;
        list.SelectedIndex = 1;
        Assert.Equal(-1, list.SelectedIndex); // nothing to select yet
        Assert.Equal(0, fires);

        list.AddChild(new ListBoxItem { Content = "a" });
        Assert.Equal(-1, list.SelectedIndex);
        Assert.Equal(0, fires);
        list.AddChild(new ListBoxItem { Content = "b" });
        Assert.Equal(1, list.SelectedIndex);
        Assert.Equal("b", list.SelectedItem?.Content);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void Removing_rows_tracks_the_selected_item()
    {
        var (_, _, list) = Harness();
        var fires = 0;
        list.SelectedIndex = 1; // R1
        list.SelectionChanged += () => fires++;

        // Removing a row above shifts the index; R1 stays selected.
        Assert.True(list.RemoveChild(list.Items[0]));
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal("R1", list.SelectedItem?.Content);
        Assert.Equal(1, fires);

        // Removing the selected row hands selection to the row sliding in.
        Assert.True(list.RemoveChild(list.Items[0]));
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal("R2", list.SelectedItem?.Content);
        Assert.Equal(2, fires);

        // Removing the last selected row clears the selection.
        list.SelectedIndex = list.Items.Count - 1;
        Assert.True(list.RemoveChild(list.Items[^1]));
        Assert.Equal(-1, list.SelectedIndex);
        Assert.Null(list.SelectedItem);
    }

    [Fact]
    public void Click_selects_row_under_pointer()
    {
        var (router, _, list) = Harness();
        var rowH = list.Items[0].DesiredSize.Height;
        var fires = 0;
        list.SelectionChanged += () => fires++;

        Down(router, 200, 1 + 3 * rowH + rowH / 2); // press on R3
        Assert.Equal(3, list.SelectedIndex);
        Assert.Equal(1, fires);

        Down(router, 200, 1 + 5 * rowH + 20); // press below the rows
        Assert.Equal(3, list.SelectedIndex);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void Click_ignored_when_disabled()
    {
        var (router, _, list) = Harness();
        var rowH = list.Items[0].DesiredSize.Height;
        list.IsEnabled = false;
        Down(router, 200, 1 + 2 * rowH + rowH / 2);
        Assert.Equal(-1, list.SelectedIndex);
    }

    [Fact]
    public void Keyboard_moves_selection_only_when_focused()
    {
        var (router, manager, list) = Harness();
        Key(router, "ArrowDown");
        Assert.Equal(-1, list.SelectedIndex); // no focus yet

        Assert.True(manager.TryFocus(list));
        Key(router, "ArrowDown");
        Assert.Equal(0, list.SelectedIndex);
        Key(router, "ArrowDown");
        Assert.Equal(1, list.SelectedIndex);
        Key(router, "ArrowUp");
        Assert.Equal(0, list.SelectedIndex);
        Key(router, "ArrowUp"); // clamped at the top
        Assert.Equal(0, list.SelectedIndex);
        Key(router, "End");
        Assert.Equal(4, list.SelectedIndex);
        Key(router, "Home");
        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void SelectedItem_setter_resolves_by_reference()
    {
        var (_, _, list) = Harness();
        list.SelectedItem = list.Items[3];
        Assert.Equal(3, list.SelectedIndex);
        Assert.True(list.Items[3].IsSelected);
        list.SelectedItem = new ListBoxItem { Content = "foreign" };
        Assert.Equal(-1, list.SelectedIndex);
        list.SelectedItem = null;
        Assert.Equal(-1, list.SelectedIndex);
    }

    [Fact]
    public void Programmatic_add_of_wrong_type_throws()
    {
        var (_, _, list) = Harness();
        var ex = Assert.Throws<InvalidOperationException>(() => list.AddChild(new Button()));
        Assert.Contains("ListBoxItem", ex.Message);
    }

    [Fact]
    public void Xaml_parses_items_inner_text_and_selection()
    {
        var result = XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <ListBox SelectedIndex="0">
                <ListBoxItem Content="x"/>
                <ListBoxItem>y</ListBoxItem>
              </ListBox>
            </Window>
            """);
        var window = Assert.IsType<Window>(result.Root);
        var list = Assert.IsType<ListBox>(window.Content);
        Assert.Equal(["x", "y"], list.Items.Select(i => i.Content));
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal("x", list.SelectedItem?.Content);
    }

    [Fact]
    public void Xaml_rejects_non_item_child()
    {
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <ListBox>
                <Button Content="nope"/>
              </ListBox>
            </Window>
            """));
        Assert.Contains("ListBoxItem", ex.Message);
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
    public void Baml_listbox_loads_items_and_pending_selection()
    {
        const short tListBox = -373; // verified against a real ListBox BAML fixture
        const short tListBoxItem = -374;
        const short mItems = -192; // ItemsControl.Items
        const short mContent = -14; // ContentControl.Content
        const short cString = -615;
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(mContent);
        b.Raw(0x03).I16(tListBox).Raw(0x00);
        b.Sized(0x1F, p => p.U16(0).I16(tListBox).Raw(0x00).Str("SelectedIndex"));
        // SelectedIndex takes any int32 converter; the value is self-validating.
        b.Sized(0x24, p => p.I16(0).Str("1").I16(0));
        b.Raw(0x2E).I16(mItems);
        foreach (var text in new[] { "a", "b" })
        {
            b.Raw(0x03).I16(tListBoxItem).Raw(0x00);
            b.Sized(0x24, p => p.I16(mContent).Str(text).I16(cString));
            b.Raw(0x04);
        }
        b.Raw(0x04).Raw(0x04).Raw(0x02);

        using var stream = new MemoryStream(b.ToArray());
        var window = XamlLoader.LoadComponent<Window>(stream);
        var list = Assert.IsType<ListBox>(window.Content);
        Assert.Equal(["a", "b"], list.Items.Select(i => i.Content));
        Assert.Equal(1, list.SelectedIndex);
        Assert.Equal("b", list.SelectedItem?.Content);
    }

    [Fact]
    public void Baml_rejects_non_item_row()
    {
        const short tListBox = -373; // verified against a real ListBox BAML fixture
        const short tTextBlock = -638;
        const short mContent = -14; // ContentControl.Content
        const short mItems = -192;
        var b = new B();
        b.Raw(Header()).Raw(0x01);
        b.Sized(0x1C, p => p.U16(0).Str("HelloWorld, Version=1.0.0.0"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("HelloWorld.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00);
        b.Raw(0x2E).I16(mContent);
        b.Raw(0x03).I16(tListBox).Raw(0x00);
        b.Raw(0x2E).I16(mItems);
        b.Raw(0x03).I16(tTextBlock).Raw(0x00);
        b.Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x02);

        using var stream = new MemoryStream(b.ToArray());
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("ListBoxItem", ex.Message);
    }
}
