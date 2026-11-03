using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ComboBox DisplayMemberPath parity (mirrors the ListBox resolver): data
/// items display through the path in the header, the drop rows and the
/// type-ahead search, instead of falling back to ToString.
/// </summary>
[ResetAmbientState]
public sealed class ComboBoxDisplayMemberPathTests
{
    private sealed record Person(string Name, int Age);

    private static (WasmInputRouter Router, ComboBox Box) Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        var combo = new ComboBox();
        combo.AddItem(new Person("Alice", 30));
        combo.AddItem(new Person("Bob", 40));
        combo.DisplayMemberPath = "Name";
        root.AddChild(combo);
        manager.AddRoot(root);
        Assert.True(manager.TryFocus(combo));
        return (router, combo);
    }

    [Fact]
    public void Header_shows_path_value()
    {
        var (_, combo) = Harness();
        combo.SelectedIndex = 0;

        Assert.Equal("Alice", combo.Text);
    }

    [Fact]
    public void Type_ahead_searches_path_values()
    {
        var (router, combo) = Harness();

        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), "b", true, false, false, false));

        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("Bob", combo.Text);
    }
}
