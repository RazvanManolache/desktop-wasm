using System.Text;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Menu v1 tests: bar layout, open/toggle/switch, leaf invoke,
/// hover, keyboard nav, disabled/command gating, focus-loss close, loose
/// XAML, synthetic BAML (framing mirrors the real Menu fixture dump:
/// type ids -401/-403, Items -192, Header -71, IsEnabled -133/Boolean -46).</summary>
[ResetAmbientState]
public sealed class MenuTests
{
    private const short TMenu = -401;
    private const short TMenuItem = -403;
    private const short MContent = -14;
    private const short MItems = -192;
    private const short MHeader = -71;
    private const short MIsEnabled = -133;
    private const short CString = -615;
    private const short CBool = -46;

    private static readonly WasmWindowHandle Handle = new(1);

    private sealed class MenuHarness
    {
        public WasmInputRouter Router { get; } = new();
        public WpfInputManager Manager { get; }
        public Window Root { get; } = new();
        public Menu Menu { get; } = new();
        public MenuItem File { get; } = new() { Header = "File" };
        public MenuItem New { get; } = new() { Header = "New" };
        public MenuItem Recent { get; } = new() { Header = "Recent" };
        public MenuItem Doc1 { get; } = new() { Header = "Doc1" };
        public MenuItem Doc2 { get; } = new() { Header = "Doc2", IsEnabled = false };
        public MenuItem Edit { get; } = new() { Header = "Edit" };
        public MenuItem Undo { get; } = new() { Header = "Undo" };
        public TextBox FocusPeer { get; } = new();
        public List<string> Clicked { get; } = new();

        public MenuHarness()
        {
            New.Click += (_, _) => Clicked.Add("new");
            Doc1.Click += (_, _) => Clicked.Add("doc1");
            Doc2.Click += (_, _) => Clicked.Add("doc2");
            Undo.Click += (_, _) => Clicked.Add("undo");
            Recent.AddChild(Doc1);
            Recent.AddChild(Doc2);
            File.AddChild(New);
            File.AddChild(Recent);
            Edit.AddChild(Undo);
            Menu.AddChild(File);
            Menu.AddChild(Edit);
            var stack = new StackPanel();
            stack.AddChild(Menu);
            stack.AddChild(FocusPeer);
            Root.Content = stack;
            Manager = new WpfInputManager(Router);
            Manager.AddRoot(Root);
            var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 600));
            mil.Render(Root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        }

        public void Rerender()
        {
            var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(400, 600));
            mil.Render(Root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        }
    }

    private static WpfPoint Center(UIElement e)
    {
        var r = e.ArrangedRect;
        return new WpfPoint(r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static void Up(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, false));

    private static void Move(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true)
            { Kind = WasmPointerKind.Move });

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(Handle, key, true, false, false, false));

    private static void ClickAt(MenuHarness h, MenuItem item)
    {
        var p = Center(item);
        h.Rerender(); // open panels must be arranged before their centers are read
        p = Center(item);
        Down(h.Router, p.X, p.Y);
        h.Rerender();
        p = Center(item);
        Up(h.Router, p.X, p.Y);
    }

    [Fact]
    public void Layout_bar_rows_top_items_side_by_side()
    {
        var h = new MenuHarness();
        Assert.Equal(28, h.Menu.DesiredSize.Height);
        Assert.Equal(28, h.Menu.ArrangedRect.Height);
        Assert.Equal(h.File.ArrangedRect.Y, h.Edit.ArrangedRect.Y);
        Assert.Equal(h.File.ArrangedRect.X + h.File.ArrangedRect.Width, h.Edit.ArrangedRect.X);
        Assert.Equal(
            h.File.DesiredSize.Width + h.Edit.DesiredSize.Width,
            h.Menu.DesiredSize.Width);
    }

    [Fact]
    public void Items_reject_non_menuitem_children()
    {
        var menu = new Menu();
        var item = new MenuItem();
        var ex = Assert.Throws<InvalidOperationException>(() => menu.AddChild(new Button()));
        Assert.Contains("<Menu> entries must be <MenuItem>", ex.Message, StringComparison.Ordinal);
        var ex2 = Assert.Throws<InvalidOperationException>(() => item.AddChild(new TextBlock()));
        Assert.Contains("<MenuItem> entries must be <MenuItem>", ex2.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Press_toggles_submenu()
    {
        var h = new MenuHarness();
        var p = Center(h.File);
        Down(h.Router, p.X, p.Y);
        Assert.True(h.File.IsSubmenuOpen);
        Up(h.Router, p.X, p.Y);
        Assert.True(h.File.IsSubmenuOpen); // release over the pressed parent keeps the toggle
        p = Center(h.File);
        Down(h.Router, p.X, p.Y);
        Up(h.Router, p.X, p.Y);
        Assert.False(h.File.IsSubmenuOpen);
    }

    [Fact]
    public void Release_on_leaf_invokes_click_and_closes()
    {
        var h = new MenuHarness();
        ClickAt(h, h.File);
        Assert.True(h.File.IsSubmenuOpen);
        ClickAt(h, h.New);
        Assert.Equal(["new"], h.Clicked);
        Assert.False(h.File.IsSubmenuOpen);
        Assert.Null(h.Menu.Highlighted);
    }

    [Fact]
    public void Release_on_disabled_leaf_invokes_nothing_and_stays_open()
    {
        var h = new MenuHarness();
        ClickAt(h, h.File);
        ClickAt(h, h.Recent); // opens the Recent submenu (press toggles open)
        Assert.True(h.Recent.IsSubmenuOpen);
        ClickAt(h, h.Doc2);
        Assert.Empty(h.Clicked);
        Assert.True(h.File.IsSubmenuOpen);
        Assert.True(h.Recent.IsSubmenuOpen);
    }

    [Fact]
    public void Press_second_top_item_switches_open_menu()
    {
        var h = new MenuHarness();
        var pf = Center(h.File);
        Down(h.Router, pf.X, pf.Y);
        Up(h.Router, pf.X, pf.Y);
        Assert.True(h.File.IsSubmenuOpen);
        var pe = Center(h.Edit);
        Down(h.Router, pe.X, pe.Y);
        Up(h.Router, pe.X, pe.Y);
        Assert.False(h.File.IsSubmenuOpen);
        Assert.True(h.Edit.IsSubmenuOpen);
    }

    [Fact]
    public void Hover_switches_open_menu_and_highlights()
    {
        var h = new MenuHarness();
        ClickAt(h, h.File);
        Assert.True(h.File.IsSubmenuOpen);
        var pe = Center(h.Edit);
        Move(h.Router, pe.X, pe.Y);
        Assert.False(h.File.IsSubmenuOpen);
        Assert.True(h.Edit.IsSubmenuOpen);
        Assert.Same(h.Undo, h.Menu.Highlighted);
    }

    [Fact]
    public void Command_executes_on_leaf_click_and_canexecute_gates()
    {
        var h = new MenuHarness();
        var fired = new List<string>();
        var cmd = new RelayCommand(_ => fired.Add("cmd"));
        var leaf = new MenuItem { Header = "Cmd", Command = cmd };
        h.File.AddChild(leaf);
        h.Rerender();
        ClickAt(h, h.File);
        ClickAt(h, leaf);
        Assert.Equal(["cmd"], fired);

        var can = false;
        var gated = new RelayCommand(_ => fired.Add("gated"), _ => can);
        var leaf2 = new MenuItem { Header = "Gated", Command = gated };
        h.File.AddChild(leaf2);
        h.Rerender();
        Assert.False(leaf2.IsEffectivelyEnabled);
        ClickAt(h, h.File);
        ClickAt(h, leaf2);
        Assert.Equal(["cmd"], fired); // blocked: nothing else fired
        can = true;
        gated.RaiseCanExecuteChanged();
        Assert.True(leaf2.IsEffectivelyEnabled);
        ClickAt(h, leaf2);
        Assert.Equal(["cmd", "gated"], fired);
    }

    [Fact]
    public void Keyboard_opens_navigates_invokes_and_escapes()
    {
        var h = new MenuHarness();
        Assert.True(h.Manager.TryFocus(h.Menu));
        Key(h.Router, "ArrowDown");
        Assert.True(h.File.IsSubmenuOpen);
        Assert.Same(h.New, h.Menu.Highlighted);
        Key(h.Router, "ArrowDown");
        Assert.Same(h.Recent, h.Menu.Highlighted);
        Key(h.Router, "Enter"); // opens Recent, highlights first enabled leaf (Doc1, not Doc2)
        Assert.True(h.Recent.IsSubmenuOpen);
        Assert.Same(h.Doc1, h.Menu.Highlighted);
        Key(h.Router, "ArrowDown"); // skips disabled Doc2, wraps to Doc1
        Assert.Same(h.Doc1, h.Menu.Highlighted);
        Key(h.Router, "Enter");
        Assert.Equal(["doc1"], h.Clicked);
        Assert.False(h.File.IsSubmenuOpen);
    }

    [Fact]
    public void Keyboard_escape_closes_open_menus()
    {
        var h = new MenuHarness();
        Assert.True(h.Manager.TryFocus(h.Menu));
        Key(h.Router, "ArrowDown");
        Assert.True(h.File.IsSubmenuOpen);
        Key(h.Router, "Escape");
        Assert.False(h.File.IsSubmenuOpen);
        Assert.Null(h.Menu.OpenTopItem);
    }

    [Fact]
    public void Keyboard_left_right_move_across_top_level()
    {
        var h = new MenuHarness();
        Assert.True(h.Manager.TryFocus(h.Menu));
        Key(h.Router, "ArrowRight");
        Assert.Same(h.File, h.Menu.Highlighted);
        Assert.False(h.File.IsSubmenuOpen);
        Key(h.Router, "ArrowRight");
        Assert.Same(h.Edit, h.Menu.Highlighted);
        Key(h.Router, "ArrowLeft");
        Assert.Same(h.File, h.Menu.Highlighted);
    }

    [Fact]
    public void Focus_loss_closes_open_menus()
    {
        var h = new MenuHarness();
        ClickAt(h, h.File);
        Assert.True(h.File.IsSubmenuOpen);
        Assert.True(h.Manager.TryFocus(h.FocusPeer));
        Assert.False(h.File.IsSubmenuOpen);
    }

    [Fact]
    public void Disabled_menu_ignores_input()
    {
        var h = new MenuHarness();
        h.Menu.IsEnabled = false;
        var p = Center(h.File);
        Down(h.Router, p.X, p.Y);
        Up(h.Router, p.X, p.Y);
        Assert.False(h.File.IsSubmenuOpen);
        Assert.Null(h.Menu.Highlighted);
    }

    [Fact]
    public void Render_with_open_submenu_does_not_throw()
    {
        var h = new MenuHarness();
        ClickAt(h, h.File);
        h.Rerender(); // paints bar, panels, highlight in one pass
    }

    [Fact]
    public void Xaml_maps_menu_hierarchy_with_header_and_is_enabled()
    {
        const string xaml =
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Menu><MenuItem Header=\"File\"><MenuItem Header=\"New\"/>" +
            "<MenuItem Header=\"Old\" IsEnabled=\"False\"/></MenuItem>" +
            "<MenuItem Header=\"Edit\"/></Menu></Window>";
        var window = (Window)XamlLoader.Load(xaml).Root;
        var menu = Assert.IsType<Menu>(window.Content);
        Assert.Equal(2, menu.Items.Count);
        Assert.Equal("File", menu.Items[0].Header);
        Assert.Equal("Edit", menu.Items[1].Header);
        Assert.Equal(["New", "Old"], menu.Items[0].Items.Select(i => i.Header));
        Assert.False(menu.Items[0].Items[1].IsEnabled);
    }

    [Fact]
    public void Xaml_rejects_non_menuitem_child()
    {
        const string xaml =
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Menu><Button Content=\"x\"/></Menu></Window>";
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(xaml));
        Assert.Contains("<Menu> children must be <MenuItem>", ex.Message, StringComparison.Ordinal);
    }

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

    private static byte[] MenuBytes()
    {
        var b = new B();
        b.Raw(0x0C, 0x00, 0x00, 0x00)
         .Raw(0x4D, 0x00, 0x53, 0x00, 0x42, 0x00, 0x41, 0x00, 0x4D, 0x00, 0x4C, 0x00)
         .U16(0).U16(0x60).Raw(new byte[] { 0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00, 0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 });
        b.Raw(0x01); // DocumentStart
        b.Sized(0x1C, p => p.U16(0).Str("MenuProbe"));
        b.Sized(0x1D, p => p.U16(0).U16(0).Str("MenuProbe.MainWindow"));
        b.Raw(0x03).I16(0).Raw(0x00); // root custom type -> Window
        b.Raw(0x2E).I16(MContent);
        b.Raw(0x03).I16(TMenu).Raw(0x00);
        b.Raw(0x2E).I16(MItems);
        b.Raw(0x03).I16(TMenuItem).Raw(0x00);
        b.Sized(0x24, p => p.I16(MHeader).Str("File").I16(CString));
        b.Raw(0x2E).I16(MItems);
        b.Raw(0x03).I16(TMenuItem).Raw(0x00);
        b.Sized(0x24, p => p.I16(MHeader).Str("New").I16(CString));
        b.Raw(0x04);
        b.Raw(0x03).I16(TMenuItem).Raw(0x00);
        b.Sized(0x24, p => p.I16(MHeader).Str("Old").I16(CString));
        b.Sized(0x24, p => p.I16(MIsEnabled).Str("False").I16(CBool));
        b.Raw(0x04);
        b.Raw(0x04); // end File
        b.Raw(0x04); // end Menu
        b.Raw(0x04).Raw(0x02); // end Window, DocumentEnd
        return b.ToArray();
    }

    [Fact]
    public void Baml_menu_hierarchy_header_and_is_enabled_load()
    {
        using var stream = new MemoryStream(MenuBytes());
        var window = XamlLoader.LoadComponent<Window>(stream);
        var menu = Assert.IsType<Menu>(window.Content);
        var file = Assert.Single(menu.Items);
        Assert.Equal("File", file.Header);
        Assert.Equal(["New", "Old"], file.Items.Select(i => i.Header));
        Assert.False(file.Items[1].IsEnabled);
    }

    [Fact]
    public void Baml_menu_rejects_non_menuitem_child()
    {
        // Same doc but the File submenu holds a well-known Grid (-254).
        var bytes = MenuBytes();
        // Replace the "New" MenuItem ElementStart (type -403 = 0xFE6D LE)
        // with a Grid (-254 = 0xFF02 LE), patching the second MenuItem start.
        var patch = (byte[])bytes.Clone();
        var at = FindNth(patch, new byte[] { 0x03, 0x6D, 0xFE, 0x00 }, 2);
        Assert.True(at >= 0);
        patch[at + 1] = 0x02;
        patch[at + 2] = 0xFF;
        using var stream = new MemoryStream(patch);
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("v1 supports MenuItem entries only", ex.Message, StringComparison.Ordinal);
    }

    private static int FindNth(byte[] hay, byte[] needle, int n)
    {
        var found = 0;
        for (var i = 0; i + needle.Length <= hay.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
                if (hay[i + j] != needle[j]) { match = false; break; }
            if (match && ++found == n) return i;
        }
        return -1;
    }
}
