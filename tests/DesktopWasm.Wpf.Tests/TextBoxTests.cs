using System.ComponentModel;
using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>TextBox v1: focus-gated keyboard editing, caret, TwoWay push, XAML + BAML loading.</summary>
[ResetAmbientState]
public sealed class TextBoxTests
{
    private sealed class Person : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); }
        }
    }

    private sealed class Harness
    {
        public WasmInputRouter Router { get; } = new();
        public WpfInputManager Manager { get; }
        public StackPanel Root { get; } = new();
        public TextBox Box { get; } = new();

        public Harness()
        {
            Manager = new WpfInputManager(Router);
            Root.AddChild(Box);
            Manager.AddRoot(Root);
            Root.Measure(new WpfSize(800, 600));
            Root.Arrange(new WpfRect(0, 0, 800, 600));
            Box.Arrange(new WpfRect(10, 10, 200, 30));
            Manager.TryFocus(Box);
        }

        public void Key(string key, bool ctrl = false) =>
            Router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, true, ctrl, false, false));

        public void Type(string text)
        {
            foreach (var ch in text) Key(ch.ToString());
        }
    }

    [Fact]
    public void Focus_hooks_track_manager_focus()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var tb = new TextBox();
        var button = new Button();
        Assert.False(tb.IsFocused);
        Assert.True(manager.TryFocus(tb));
        Assert.True(tb.IsFocused);
        Assert.True(manager.TryFocus(button)); // moves away
        Assert.False(tb.IsFocused);
        Assert.False(manager.TryFocus(new TextBlock())); // not focusable
    }

    [Fact]
    public void Press_focuses_box()
    {
        var h = new Harness();
        var other = new Button();
        h.Root.AddChild(other);
        other.Arrange(new WpfRect(10, 50, 200, 30));
        Assert.True(h.Manager.TryFocus(other));
        Assert.False(h.Box.IsFocused);
        h.Router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), 20, 20, WasmPointerButton.Left, true)
            { Kind = WasmPointerKind.Down, PointerId = 1 });
        Assert.Same(h.Box, h.Manager.Focused);
        Assert.True(h.Box.IsFocused);
    }

    [Fact]
    public void Typing_appends_and_steps_caret()
    {
        var h = new Harness();
        var changes = 0;
        h.Box.TextChanged += (s, e) => changes++;
        h.Type("Hi");
        Assert.Equal("Hi", h.Box.Text);
        Assert.Equal(2, h.Box.CaretIndex);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Backspace_delete_arrows_home_end_edit()
    {
        var h = new Harness();
        h.Type("abcd");
        h.Key("ArrowLeft");
        Assert.Equal(3, h.Box.CaretIndex);
        h.Key("Backspace");
        Assert.Equal("abd", h.Box.Text);
        Assert.Equal(2, h.Box.CaretIndex);
        h.Key("Delete");
        Assert.Equal("ab", h.Box.Text);
        Assert.Equal(2, h.Box.CaretIndex);
        h.Key("Home");
        Assert.Equal(0, h.Box.CaretIndex);
        h.Key("End");
        Assert.Equal(2, h.Box.CaretIndex);
    }

    [Fact]
    public void Backspace_at_start_and_delete_at_end_are_noops()
    {
        var h = new Harness();
        h.Type("ab");
        h.Key("Home");
        h.Key("Backspace");
        Assert.Equal("ab", h.Box.Text);
        h.Key("End");
        h.Key("Delete");
        Assert.Equal("ab", h.Box.Text);
    }

    [Fact]
    public void Ctrl_combos_and_enter_are_ignored()
    {
        var h = new Harness();
        h.Key("a", ctrl: true);
        h.Key("Enter");
        Assert.Equal(string.Empty, h.Box.Text);
        Assert.Equal(0, h.Box.CaretIndex);
    }

    [Fact]
    public void Unfocused_box_ignores_keys()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var panel = new StackPanel();
        var tb = new TextBox();
        panel.AddChild(tb);
        manager.AddRoot(panel);
        panel.Measure(new WpfSize(800, 600));
        panel.Arrange(new WpfRect(0, 0, 800, 600));
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), "x", true, false, false, false));
        Assert.Equal(string.Empty, tb.Text);
    }

    [Fact]
    public void TextChanged_fires_on_type_and_set_not_on_equal()
    {
        var tb = new TextBox { Text = "a" };
        var changes = 0;
        tb.TextChanged += (s, e) => changes++;
        tb.Text = "a";
        Assert.Equal(0, changes);
        tb.Text = "ab";
        Assert.Equal(1, changes);
    }

    [Fact]
    public void CaretIndex_clamps_to_text()
    {
        var tb = new TextBox { Text = "abc" };
        tb.CaretIndex = 99;
        Assert.Equal(3, tb.CaretIndex);
        tb.CaretIndex = -5;
        Assert.Equal(0, tb.CaretIndex);
    }

    [Fact]
    public void Null_text_coerces_to_empty_like_WPF()
    {
        // Gallery ControlExample template bindings resolve null before the
        // view-model lands; without coercion SelectionLength throws on
        // Text.Length and the whole page load dies.
        var tb = new TextBox { Text = "abc" };
        tb.Text = null!;
        Assert.Equal(string.Empty, tb.Text);
        Assert.Equal(0, tb.SelectionLength);
        tb.SelectionStart = 0;
        tb.CaretIndex = 0;

        var block = new TextBlock { Text = "abc" };
        block.Text = null!;
        Assert.Equal(string.Empty, block.Text);
        var window = new Window { Content = block };
        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));
    }

    [Fact]
    public void TwoWay_typing_pushes_to_source_and_back()
    {
        var vm = new Person { Name = "Ada" };
        var h = new Harness();
        h.Box.SetBinding("Text", new Binding("Name") { Source = vm, Mode = BindingMode.TwoWay });
        Assert.Equal("Ada", h.Box.Text);
        h.Box.CaretIndex = h.Box.Text.Length; // user presses End before typing
        h.Type("!");
        Assert.Equal("Ada!", vm.Name);
        vm.Name = "Grace";
        Assert.Equal("Grace", h.Box.Text);
    }

    [Fact]
    public void OneWay_typing_does_not_push()
    {
        var vm = new Person { Name = "Ada" };
        var h = new Harness();
        h.Box.SetBinding("Text", new Binding("Name") { Source = vm, Mode = BindingMode.OneWay });
        h.Box.CaretIndex = h.Box.Text.Length; // user presses End before typing
        h.Type("!");
        Assert.Equal("Ada!", h.Box.Text);
        Assert.Equal("Ada", vm.Name);
    }

    [Fact]
    public void Render_shows_caret_only_when_focused()
    {
        static int CaretRects(TextBox tb)
        {
            tb.Measure(new WpfSize(800, 600));
            tb.Arrange(new WpfRect(0, 0, 200, 30));
            return WasmMilCore.RenderHeadless(tb, new WpfSize(800, 600))
                .OfType<DrawCommand.FillRect>().Count(r => r.W == 1);
        }

        var tb = new TextBox { Text = "hi" };
        Assert.Equal(0, CaretRects(tb));
        tb.OnGotFocus();
        Assert.Equal(1, CaretRects(tb));
        tb.OnLostFocus();
        Assert.Equal(0, CaretRects(tb));
    }

    [Fact]
    public void Rendered_frame_reflects_typing()
    {
        var h = new Harness();
        static string Drawn(TextBox tb)
        {
            tb.Measure(new WpfSize(800, 600));
            tb.Arrange(new WpfRect(0, 0, 200, 30));
            return string.Concat(WasmMilCore.RenderHeadless(tb, new WpfSize(800, 600))
                .OfType<DrawCommand.DrawText>().Select(d => d.Text));
        }
        h.Type("yo");
        Assert.Equal("yo", Drawn(h.Box));
    }

    [Fact]
    public void Xaml_textbox_parses_attributes_content_and_binding()
    {
        var attr = (TextBox)((Window)XamlLoader.Load("""<Window><TextBox Text="hi" FontSize="20"/></Window>""").Root).Content!;
        Assert.Equal("hi", attr.Text);
        Assert.Equal("20px sans-serif", attr.FontCss);

        var inner = (TextBox)((Window)XamlLoader.Load("""<Window><TextBox>yo</TextBox></Window>""").Root).Content!;
        Assert.Equal("yo", inner.Text);

        var prop = (TextBox)((Window)XamlLoader.Load(
            """<Window><TextBox><TextBox.Text>q</TextBox.Text></TextBox></Window>""").Root).Content!;
        Assert.Equal("q", prop.Text);

        var window = (Window)XamlLoader.Load(
            """<Window><TextBox Text="{Binding Name, Mode=TwoWay}"/></Window>""").Root;
        var bound = (TextBox)window.Content!;
        var vm = new Person { Name = "Ada" };
        window.DataContext = vm;
        Assert.Equal("Ada", bound.Text);
        bound.Text = "Grace";
        Assert.Equal("Grace", vm.Name);
    }

    private static byte[] TextBoxBaml(short contentType = -639)
    {
        var b = new List<byte>();
        void Raw(params byte[] x) => b.AddRange(x);
        void U16(ushort v) => b.AddRange(BitConverter.GetBytes(v));
        void I16(short v) => b.AddRange(BitConverter.GetBytes(v));
        void U32(uint v) => b.AddRange(BitConverter.GetBytes(v));
        void Str(string s)
        {
            var ascii = System.Text.Encoding.ASCII.GetBytes(s);
            b.Add((byte)ascii.Length);
            b.AddRange(ascii);
        }
        void Sized(byte type, Action payload)
        {
            var start = b.Count;
            b.Add(type); b.Add(0);
            payload();
            // Size covers its own byte(s) + payload (not the type byte).
            b[start + 1] = (byte)(b.Count - start - 1);
        }
        Raw(0x0C, 0x00, 0x00, 0x00);
        Raw(0x4D, 0x00, 0x53, 0x00, 0x42, 0x00, 0x41, 0x00, 0x4D, 0x00, 0x4C, 0x00);
        U16(0); U16(0x60);
        Raw(new byte[] { 0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00, 0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 });
        Raw(0x01); // DocumentStart
        Sized(0x1C, () => { U16(0); Str("T"); });
        Sized(0x1D, () => { U16(0); U16(0); Str("T.Main"); });
        Raw(0x03); I16(0); Raw(0x00); // ElementStart(Main)
        Raw(0x2D); U32(1); // ConnectionId(1)
        Sized(0x24, () => { I16(-54); Str("T"); I16(-615); }); // Name
        Raw(0x2E); I16(-14); // Content
        Raw(0x03); I16(contentType); Raw(0x00); // ElementStart(content)
        Raw(0x2D); U32(2); // ConnectionId(2)
        Sized(0x24, () => { I16(-54); Str("T"); I16(-615); }); // Name
        Sized(0x24, () => { I16(-114); Str("hi"); I16(-615); }); // Text
        Sized(0x24, () => { I16(-57); Str("200"); I16(-348); }); // Width
        Raw(0x04, 0x04, 0x02);
        return b.ToArray();
    }

    [Fact]
    public void Baml_textbox_loads_with_text_width_and_name()
    {
        using var stream = new MemoryStream(TextBoxBaml());
        var window = XamlLoader.LoadComponent<Window>(stream);
        var tb = Assert.IsType<TextBox>(window.Content);
        Assert.Equal("hi", tb.Text);
        Assert.Equal(200, tb.Width);
    }

    [Fact]
    public void Baml_text_on_non_textbox_is_rejected()
    {
        // Same stream with a TextBlock (-638) where the TextBox (-639) was.
        using var stream = new MemoryStream(TextBoxBaml(contentType: -638));
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("-114", ex.Message);
    }

    [Fact]
    public void Baml_truncated_connection_id_is_rejected()
    {
        var bytes = TextBoxBaml();
        var cut = bytes[..(Array.LastIndexOf(bytes, (byte)0x2D) + 1)]; // ends right after 0x2D
        using var stream = new MemoryStream(cut);
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadComponent<Window>(stream));
        Assert.Contains("ConnectionId", ex.Message);
    }
}
