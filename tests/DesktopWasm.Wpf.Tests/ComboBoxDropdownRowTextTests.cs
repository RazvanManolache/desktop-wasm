using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>ComboBox drop-down row text (served gallery pin): the FontFamilies
/// sample inflates templated TextBlock rows bound to the item itself. The
/// drop painter must draw the row's own text, not the container type name.</summary>
[ResetAmbientState]
public sealed class ComboBoxDropdownRowTextTests
{
    private sealed class Store
    {
        public FontFamily[] Fams { get; } =
            new[] { new FontFamily("Arial"), new FontFamily("Segoe UI") };
    }

    [Fact]
    public void Dropdown_renders_item_text_not_container_type_name()
    {
        var root = (Window)XamlLoader.Load("""
            <Window><ComboBox ItemsSource="{Binding Fams}"><ComboBox.ItemTemplate><DataTemplate><TextBlock Text="{Binding}"/></DataTemplate></ComboBox.ItemTemplate></ComboBox></Window>
            """).Root;
        var box = Assert.IsType<ComboBox>(root.Content);
        root.DataContext = new Store();
        box.IsDropDownOpen = true;
        var texts = WasmMilCore.RenderHeadless(root, new WpfSize(400, 300))
            .OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();
        Assert.Contains("Arial", texts);
        Assert.DoesNotContain(texts, t => t == "TextBlock");
    }
}
