using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>ListView press-to-select (served u5 sanity HOLD: lists render
/// with default selections but 6+1 clicks never move selection).
/// Genuine WPF selects on press; the engine ListBox does, but ListView
/// (a separate Selector branch) has no press handling at all, so served
/// clicks land visually yet change nothing. Pre-fix: press leaves
/// SelectedIndex untouched despite a realized enabled row under the point.</summary>
[ResetAmbientState]
public sealed class ListViewPressSelectTests
{
    private sealed class Person
    {
        public string Name { get; set; } = string.Empty;
    }

    private static (WasmInputRouter Router, ListView View, List<Person> People) Harness()
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window();
        var list = new ListView { Height = 200, SelectionMode = SelectionMode.Single };
        var people = new List<Person>
        {
            new() { Name = "Winston" }, new() { Name = "Lynch" },
            new() { Name = "Arielle" }, new() { Name = "Grace" },
        };
        list.ItemsSource = people;
        var template = new DataTemplate();
        var tbFactory = new FrameworkElementFactory(typeof(TextBlock));
        tbFactory.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        template.VisualTree = tbFactory;
        list.ItemTemplate = template;
        var scroll = new ScrollViewer { Content = list };
        root.Content = scroll;
        manager.AddRoot(root);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(800, 600));
        mil.Render(root, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        return (router, list, people);
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static UIElement Container(ListView list, int index)
    {
        var container = Assert.IsAssignableFrom<UIElement>(list.ItemContainerGenerator.ContainerFromIndex(index));
        Assert.NotNull(container);
        return container;
    }

    [Fact]
    public void Press_selects_hit_row()
    {
        var (router, list, people) = Harness();
        Assert.Equal(-1, list.SelectedIndex);
        var r = Container(list, 3).RenderRect;
        Down(router, r.X + r.Width / 2, r.Y + r.Height / 2);
        Assert.Equal(3, list.SelectedIndex);
        Assert.Same(people[3], list.SelectedItem);
    }

    [Fact]
    public void Press_moves_selection_between_rows()
    {
        var (router, list, _) = Harness();
        var r0 = Container(list, 0).RenderRect;
        Down(router, r0.X + r0.Width / 2, r0.Y + r0.Height / 2);
        Assert.Equal(0, list.SelectedIndex);
        var r2 = Container(list, 2).RenderRect;
        Down(router, r2.X + r2.Width / 2, r2.Y + r2.Height / 2);
        Assert.Equal(2, list.SelectedIndex);
    }

    [Fact]
    public void Press_on_disabled_row_keeps_selection()
    {
        var (router, list, _) = Harness();
        Container(list, 1).IsEnabled = false;
        var r1 = Container(list, 1).RenderRect;
        Down(router, r1.X + r1.Width / 2, r1.Y + r1.Height / 2);
        Assert.Equal(-1, list.SelectedIndex);
        var r0 = Container(list, 0).RenderRect;
        Down(router, r0.X + r0.Width / 2, r0.Y + r0.Height / 2);
        Assert.Equal(0, list.SelectedIndex);
    }
}
