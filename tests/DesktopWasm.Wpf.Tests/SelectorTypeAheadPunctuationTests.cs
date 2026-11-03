using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native TextSearch parity: type-ahead prefix accepts spaces and
/// punctuation, so multi-word items are reachable by typing ("new " must
/// pass "Newark" through to "New York"). Letters/digits keep working.
/// </summary>
[ResetAmbientState]
public sealed class SelectorTypeAheadPunctuationTests
{
    private static WasmInputRouter Harness(out ListBox box)
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        box = new ListBox();
        box.AddChild(new ListBoxItem { Content = "Newark" });
        box.AddChild(new ListBoxItem { Content = "New York" });
        box.AddChild(new ListBoxItem { Content = "Xylophone" });
        box.AddChild(new ListBoxItem { Content = "X-Ray" });
        box.AddChild(new ListBoxItem { Content = "Boston" });
        root.AddChild(box);
        manager.AddRoot(root);
        Assert.True(manager.TryFocus(box));
        return router;
    }

    private static void Type(WasmInputRouter router, string text)
    {
        var handle = new WasmWindowHandle(1);
        foreach (var ch in text)
            router.DispatchKeyboard(new WasmKeyArgs(handle, ch.ToString(), true, false, false, false));
    }

    [Fact]
    public void Space_extends_prefix_to_multiword_match()
    {
        var router = Harness(out var box);
        Type(router, "new");
        Assert.Equal(0, box.SelectedIndex);

        Type(router, " ");

        Assert.Equal(1, box.SelectedIndex);
    }

    [Fact]
    public void Hyphen_extends_prefix()
    {
        var router = Harness(out var box);
        Type(router, "x");
        Assert.Equal(2, box.SelectedIndex);

        Type(router, "-");

        Assert.Equal(3, box.SelectedIndex);
    }

    [Fact]
    public void ComboBox_space_extends_prefix()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var combo = new ComboBox();
        combo.AddItem(new ComboBoxItem { Content = "Newark" });
        combo.AddItem(new ComboBoxItem { Content = "New York" });
        root.AddChild(combo);
        manager.AddRoot(root);
        Assert.True(manager.TryFocus(combo));

        Type(router, "new ");
        Assert.Equal(1, combo.SelectedIndex);
    }
}
