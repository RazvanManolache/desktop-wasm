using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery Text/RichTextEditPage slice (slice-39): the real staged
/// RichTextEditPage.xaml hosts a bare &lt;RichTextBox/&gt;, which the XAML
/// loader rejected as an unknown element. Before the slice the engine had
/// no RichTextBox type at all; the remaining gallery boundary after this
/// slice is Interaction.Triggers and FrameWindow Source.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class RichTextBoxTests
{
    private sealed class Harness
    {
        public Harness(UIElement content)
        {
            Root = new Window { Content = content };
            Manager = new WpfInputManager(Router);
            Manager.AddRoot(Root);
            Rerender();
        }

        public Window Root { get; }
        public WasmInputRouter Router { get; } = new();
        public WpfInputManager Manager { get; }

        public void Rerender()
        {
            var mil = new WasmMilCore(new SkiaSurface(800, 600));
            mil.Render(Root, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        }
    }

    private static WpfPoint Center(UIElement e)
    {
        var r = e.ArrangedRect;
        return new WpfPoint(r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, WasmPointerButton.Left, true));

    private static void Up(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, WasmPointerButton.Left, false));

    private static void Key(WasmInputRouter router, string key, bool shift = false, bool ctrl = false) =>
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, true, ctrl, shift, false));

    private static void Press(Harness h, UIElement element)
    {
        h.Rerender();
        var p = Center(element);
        Down(h.Router, p.X, p.Y);
        h.Rerender();
        p = Center(element);
        Up(h.Router, p.X, p.Y);
    }

    private static void Type(Harness h, string text)
    {
        foreach (var ch in text) Key(h.Router, ch.ToString());
    }

    private static T? FindVisual<T>(FrameworkElement? root) where T : FrameworkElement
    {
        if (root is null) return null;
        if (root is T match) return match;
        foreach (var child in root.LogicalChildren)
        {
            var found = FindVisual<T>(child as FrameworkElement);
            if (found is not null) return found;
        }
        return null;
    }

    [Fact]
    public void Defaults_match_wpf_shape()
    {
        var box = new RichTextBox();
        Assert.NotNull(box.Document);
        Assert.Single(box.Document.Blocks);
        Assert.Equal(string.Empty, box.PlainText);
        Assert.Equal(0, box.CaretIndex);
        Assert.Equal(0, box.SelectionStart);
        Assert.Equal(0, box.SelectionLength);
        Assert.True(box.AcceptsReturn);
        Assert.False(box.AcceptsTab);
        Assert.False(box.IsReadOnly);
        Assert.True(box.Focusable);
        Assert.False(box.IsFocused);
    }

    [Fact]
    public void Document_rejects_null_parents_replacement()
    {
        var box = new RichTextBox();
        Assert.Throws<ArgumentNullException>(() => box.Document = null!);
        var first = box.Document;
        var replacement = new FlowDocument(new Paragraph(new Run("hi")));
        box.Document = replacement;
        Assert.Same(replacement, box.Document);
        Assert.Same(box, replacement.Parent);
        Assert.Equal("hi", box.PlainText);
        Assert.Null(first.Parent);
    }

    [Fact]
    public void Typing_appends_text_moves_caret_and_raises_text_changed()
    {
        var box = new RichTextBox();
        var h = new Harness(box);
        var changes = 0;
        box.TextChanged += (_, _) => changes++;
        Press(h, box);
        Assert.True(box.IsFocused);
        Type(h, "Ada");
        Assert.Equal("Ada", box.PlainText);
        Assert.Equal(3, box.CaretIndex);
        Assert.True(changes >= 1);
    }

    [Fact]
    public void Backspace_delete_and_arrows_edit_like_textbox()
    {
        var box = new RichTextBox();
        var h = new Harness(box);
        Press(h, box);
        Type(h, "Ada");
        Key(h.Router, "ArrowLeft");
        Assert.Equal(2, box.CaretIndex);
        Key(h.Router, "Backspace");
        Assert.Equal("Aa", box.PlainText);
        Key(h.Router, "Delete");
        Assert.Equal("A", box.PlainText);
        Key(h.Router, "Home");
        Assert.Equal(0, box.CaretIndex);
        Key(h.Router, "End");
        Assert.Equal(1, box.CaretIndex);
    }

    [Fact]
    public void Enter_splits_paragraph_when_accepts_return()
    {
        var box = new RichTextBox();
        var h = new Harness(box);
        Press(h, box);
        Type(h, "ab");
        Key(h.Router, "ArrowLeft");
        Key(h.Router, "Enter");
        Assert.Equal(2, box.Document.Blocks.Count);
        Assert.Equal("a\nb", box.PlainText);
    }

    [Fact]
    public void Enter_ignored_when_accepts_return_false()
    {
        var box = new RichTextBox { AcceptsReturn = false };
        var h = new Harness(box);
        Press(h, box);
        Type(h, "ab");
        Key(h.Router, "Enter");
        Assert.Single(box.Document.Blocks);
        Assert.Equal("ab", box.PlainText);
    }

    [Fact]
    public void Backspace_at_paragraph_start_merges_up()
    {
        var box = new RichTextBox();
        var h = new Harness(box);
        Press(h, box);
        Type(h, "ab");
        Key(h.Router, "Enter");
        Type(h, "cd");
        Assert.Equal("ab\ncd", box.PlainText);
        Key(h.Router, "ArrowLeft");
        Key(h.Router, "ArrowLeft");
        Assert.Equal(3, box.CaretIndex);
        Key(h.Router, "Backspace");
        Assert.Single(box.Document.Blocks);
        Assert.Equal("abcd", box.PlainText);
        Assert.Equal(2, box.CaretIndex);
    }

    [Fact]
    public void Select_all_then_type_replaces_and_selection_round_trips()
    {
        var box = new RichTextBox();
        var h = new Harness(box);
        Press(h, box);
        Type(h, "Ada");
        box.SelectAll();
        Assert.Equal(3, box.SelectionLength);
        Assert.Equal("Ada", box.SelectedText);
        Type(h, "Bo");
        Assert.Equal("Bo", box.PlainText);
        box.SelectAll();
        box.SelectedText = "Cy";
        Assert.Equal("Cy", box.PlainText);
    }

    [Fact]
    public void Read_only_blocks_edits_but_keeps_select_all()
    {
        var box = new RichTextBox { IsReadOnly = true };
        box.Document = new FlowDocument(new Paragraph(new Run("Ada")));
        var h = new Harness(box);
        Press(h, box);
        Type(h, "X");
        Key(h.Router, "Backspace");
        Key(h.Router, "Enter");
        Assert.Equal("Ada", box.PlainText);
        box.SelectAll();
        Assert.Equal(3, box.SelectionLength);
    }

    [Fact]
    public void Atomic_hyperlink_survives_edits()
    {
        var box = new RichTextBox();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run("go "));
        var link = new Hyperlink(new Run("here"));
        paragraph.Inlines.Add(link);
        box.Document = new FlowDocument(paragraph);
        Assert.Equal("go here", box.PlainText);
        box.CaretIndex = 5;
        var h = new Harness(box);
        Press(h, box);
        box.CaretIndex = 5;
        Type(h, "!");
        Assert.Equal("go here!", box.PlainText);
        Assert.Contains(link, paragraph.Inlines);
        box.SelectionStart = 0;
        box.SelectionLength = box.PlainText.Length;
        Key(h.Router, "Backspace");
        Assert.Equal(string.Empty, box.PlainText);
        Assert.DoesNotContain(link, paragraph.Inlines);
    }

    [Fact]
    public void Click_moves_caret_to_text_start()
    {
        var box = new RichTextBox();
        var h = new Harness(box);
        Press(h, box);
        Type(h, "hello");
        h.Rerender();
        var r = box.ArrangedRect;
        Down(h.Router, r.X + 6, r.Y + 10);
        Up(h.Router, r.X + 6, r.Y + 10);
        Assert.Equal(0, box.CaretIndex);
        Assert.True(box.IsFocused);
    }

    [Fact]
    public void Box_parses_from_xaml_and_rejects_precisely()
    {
        const string xaml = """
            <RichTextBox IsReadOnly="True" AcceptsReturn="False" AcceptsTab="True" VerticalScrollBarVisibility="Auto" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """;
        var root = XamlLoader.Load(xaml).Root as RichTextBox;
        Assert.NotNull(root);
        Assert.True(root!.IsReadOnly);
        Assert.False(root.AcceptsReturn);
        Assert.True(root.AcceptsTab);
        Assert.Equal(ScrollBarVisibility.Auto, root.VerticalScrollBarVisibility);
        const string documentAttribute = """
            <RichTextBox Document="x" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """;
        var documentError = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(documentAttribute));
        Assert.Contains("RichTextBox.Document", documentError.Message);
        const string innerText = """
            <RichTextBox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">hi</RichTextBox>
            """;
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(innerText));
        const string badVisibility = """
            <RichTextBox VerticalScrollBarVisibility="Everywhere" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />
            """;
        var visibilityError = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(badVisibility));
        Assert.Contains("ScrollBarVisibility", visibilityError.Message);
    }

    [Fact]
    public void Document_property_element_inflates_flow_document()
    {
        const string xaml = """
            <RichTextBox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <RichTextBox.Document>
                <FlowDocument>
                  <Paragraph><Run Text="Hello" /></Paragraph>
                </FlowDocument>
              </RichTextBox.Document>
            </RichTextBox>
            """;
        var root = XamlLoader.Load(xaml).Root as RichTextBox;
        Assert.NotNull(root);
        Assert.Equal("Hello", root!.PlainText);
    }

    [Fact]
    public void Direct_flow_document_child_becomes_document()
    {
        const string xaml = """
            <RichTextBox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <FlowDocument>
                <Paragraph><Run Text="Direct" /></Paragraph>
              </FlowDocument>
            </RichTextBox>
            """;
        var root = XamlLoader.Load(xaml).Root as RichTextBox;
        Assert.NotNull(root);
        Assert.Equal("Direct", root!.PlainText);
    }

    [Fact]
    public void Render_paints_text_and_runs_without_errors()
    {
        var box = new RichTextBox { Width = 200, Height = 60 };
        box.Document = new FlowDocument(new Paragraph(new Run("Ada")));
        var h = new Harness(box);
        h.Rerender();
        var r = box.RenderRect;
        Down(h.Router, r.X + r.Width / 2, r.Y + r.Height / 2);
        Up(h.Router, r.X + r.Width / 2, r.Y + r.Height / 2);
        Assert.True(box.IsFocused);
        Type(h, "!");
        Assert.Equal("Ada!", box.PlainText);
        box.SelectAll();
        h.Rerender();
        Assert.True(box.ArrangedRect.Width > 0 && box.ArrangedRect.Height > 0);
    }
}
