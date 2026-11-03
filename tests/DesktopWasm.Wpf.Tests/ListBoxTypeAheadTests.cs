using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Native ListBox parity: typing text jumps selection to the next row with
/// a matching prefix (WPF TextSearch; multi-keystroke prefixes accumulate
/// and wrap past the end).
/// </summary>
[ResetAmbientState]
public sealed class ListBoxTypeAheadTests
{
    private static (WasmInputRouter Router, ListBox Box) Harness(int count = 20)
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var box = new ListBox();
        for (var i = 0; i < count; i++) box.AddChild(new ListBoxItem { Content = $"R{i}" });
        root.Content = box;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(800, 600));
        mil.Render(root, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        Assert.True(manager.TryFocus(box));
        return (router, box);
    }

    private static void Type(WasmInputRouter router, string text)
    {
        foreach (var ch in text)
            router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), ch.ToString(), true, false, false, false));
    }

    [Fact]
    public void Typing_prefix_jumps_and_accumulates()
    {
        var (router, box) = Harness();
        Assert.Equal(-1, box.SelectedIndex);

        Type(router, "R1");

        Assert.Equal(1, box.SelectedIndex);

        Type(router, "5");

        Assert.Equal(15, box.SelectedIndex);
    }

    [Fact]
    public void Typing_wraps_past_the_end()
    {
        var (router, box) = Harness();
        box.SelectedIndex = 19;

        Type(router, "r");

        Assert.Equal(0, box.SelectedIndex);
    }

    [Fact]
    public void Quiet_second_resets_the_prefix()
    {
        var (router, box) = Harness();
        Type(router, "r");
        Assert.Equal(0, box.SelectedIndex);

        Thread.Sleep(1200);
        Type(router, "1");

        // Fresh prefix "1" matches nothing ("R1" starts with R), so the
        // R0 selection stands; without the reset "r1" would jump to R1.
        Assert.Equal(0, box.SelectedIndex);
    }
}
