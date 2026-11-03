using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Grouped-list parity for type-ahead (jobs 15/51 pinned scroll, click
/// and wheel under GroupItem containers): the prefix jump searches the
/// same text the rows display — record ToString without a path, the
/// DisplayMemberPath value with one.
/// </summary>
[ResetAmbientState]
public sealed class ListBoxGroupedTypeAheadTests
{
    private sealed record Item(string Category, string Name);

    private static WasmInputRouter Harness(out ListBox box)
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        box = new ListBox();
        var data = new List<Item>();
        for (var i = 0; i < 10; i++) data.Add(new Item("A", $"A{i}"));
        for (var i = 0; i < 10; i++) data.Add(new Item("B", $"B{i}"));
        var source = new ListCollectionView(data);
        source.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
        box.ItemsSource = source;
        box.GroupStyle.Add(new GroupStyle());
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
    public void Typing_single_char_jumps_to_first_grouped_row()
    {
        var router = Harness(out var box);
        Assert.StartsWith("Item {", box.Items[0].Content, StringComparison.Ordinal);

        Type(router, "i");

        Assert.Equal(0, box.SelectedIndex);
    }

    [Fact]
    public void Typing_full_tostring_prefix_disambiguates_grouped_row()
    {
        var router = Harness(out var box);

        Type(router, "item { category = b, name = b3");

        Assert.Equal(13, box.SelectedIndex);
    }

    [Fact]
    public void Typing_path_prefix_jumps_to_grouped_match()
    {
        var router = Harness(out var box);
        box.DisplayMemberPath = "Name";
        Assert.Equal("B3", box.Items[13].Content);

        Type(router, "b3");

        Assert.Equal(13, box.SelectedIndex);
    }
}
