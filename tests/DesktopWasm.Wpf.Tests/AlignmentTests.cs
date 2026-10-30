using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class AlignmentTests
{
    private static List<DrawCommand> Render(UIElement content, double w = 800, double h = 600)
    {
        var window = new Window();
        window.Content = content;
        return WasmMilCore.RenderHeadless(window, new WpfSize(w, h)).ToList();
    }

    [Fact]
    public void TextBlock_anchors_text_to_top_left()
    {
        // Contract: DrawText VAlign 0 = baseline, 1 = top (Skia replay + Canvas2D
        // agree). A top-anchored run must use 1 or the first line clips above y=0.
        var text = Render(new TextBlock { Text = "Hi" })
            .OfType<DrawCommand.DrawText>().Single();
        Assert.Equal(0, text.HAlign);
        Assert.Equal(1, text.VAlign);
    }

    [Fact]
    public void TextBlock_offsets_text_inside_arranged_box_for_center_alignment()
    {
        var text = Render(new TextBlock
        {
            Text = "Hi",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        }).OfType<DrawCommand.DrawText>().Single();

        // Top-left anchored draw (HAlign 0 / VAlign 1) at the centered offset:
        // a short run in 800x600 lands well inside the box, not at the origin.
        Assert.Equal(0, text.HAlign);
        Assert.Equal(1, text.VAlign);
        Assert.True(text.X > 300, $"X={text.X}");
        Assert.True(text.Y > 200, $"Y={text.Y}");
        Assert.True(text.X < 500, $"X={text.X}");
        Assert.True(text.Y < 400, $"Y={text.Y}");
    }

    [Fact]
    public void TextBlock_uses_its_own_font()
    {
        var text = Render(new TextBlock { Text = "Hi", FontCss = "20px monospace" })
            .OfType<DrawCommand.DrawText>().Single();
        Assert.Equal("20px monospace", text.FontCss);
    }

    [Fact]
    public void Button_centers_label_and_draws_border_first()
    {
        var button = new Button { Content = "OK" };
        var commands = Render(button);

        var text = commands.OfType<DrawCommand.DrawText>().Single();
        Assert.Equal(1, text.HAlign);
        Assert.Equal(2, text.VAlign); // 2 = middle around the label center.
        Assert.Equal("14px sans-serif", text.FontCss);

        var rects = commands.OfType<DrawCommand.FillRect>().ToList();
        var borderAt = rects.FindIndex(r => r.Rgba == button.BorderRgba);
        Assert.True(borderAt >= 0); // 1px chrome underneath...
        Assert.Equal(button.BackgroundRgba, rects[borderAt + 1].Rgba); // ...the inset fill.
        Assert.Equal(button.ArrangedRect.Width - 2, rects[borderAt + 1].W, precision: 5);
    }

    [Fact]
    public void StackPanel_centers_fixed_width_child()
    {
        var panel = new StackPanel();
        var button = new Button { Content = "OK", Width = 200, HorizontalAlignment = HorizontalAlignment.Center };
        panel.AddChild(button);

        panel.Measure(new WpfSize(800, 600));
        panel.Arrange(new WpfRect(0, 0, 800, 600));

        Assert.Equal(200, button.ArrangedRect.Width);
        Assert.Equal(300, button.ArrangedRect.X); // (800 - 200) / 2
    }

    [Fact]
    public void StackPanel_offsets_children_by_margin()
    {
        var panel = new StackPanel();
        var first = new TextBlock { Text = "A", Margin = new Thickness(0, 4, 0, 4) };
        var second = new TextBlock { Text = "B" };
        panel.AddChild(first);
        panel.AddChild(second);

        panel.Measure(new WpfSize(800, 600));
        panel.Arrange(new WpfRect(0, 0, 800, 600));

        Assert.Equal(4, first.ArrangedRect.Y);
        Assert.Equal(first.DesiredSize.Height + 8, second.ArrangedRect.Y);
        Assert.Equal(first.DesiredSize.Height + second.DesiredSize.Height + 8, panel.DesiredSize.Height);
    }

    [Fact]
    public void Explicit_size_beats_content_size_on_any_element()
    {
        var border = new Border { Width = 300 };
        border.AddChild(new TextBlock { Text = "Hi" });
        border.Measure(new WpfSize(800, 600));
        Assert.Equal(300, border.DesiredSize.Width);

        var grid = new Grid { Width = 300, Height = 100 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star() });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star() });
        grid.AddChild(new Button { Content = "x" });
        grid.Measure(new WpfSize(800, 600));
        Assert.Equal(new Size(300, 100), grid.DesiredSize);
    }

    [Fact]
    public void Thickness_parses_all_three_forms()
    {
        Assert.Equal(new Thickness(8, 8, 8, 8), Thickness.Parse("8"));
        Assert.Equal(new Thickness(8, 4, 8, 4), Thickness.Parse("8,4"));
        Assert.Equal(new Thickness(1, 2, 3, 4), Thickness.Parse("1,2,3,4"));
        Assert.Throws<FormatException>(() => Thickness.Parse("1,2,3"));
        Assert.Throws<FormatException>(() => Thickness.Parse("wide"));
    }

    [Fact]
    public void Xaml_supports_alignment_margin_and_fontsize()
    {
        const string xaml = """
            <Window Title="T">
              <StackPanel>
                <Button Content="Hi" Width="120" HorizontalAlignment="Center" Margin="0,4,0,4"/>
                <TextBlock Text="Yo" FontSize="20"/>
              </StackPanel>
            </Window>
            """;
        var root = (Window)XamlLoader.Load(xaml).Root;
        var panel = (StackPanel)root.Content!;
        var button = (Button)panel.LogicalChildren[0];
        var text = (TextBlock)panel.LogicalChildren[1];

        Assert.Equal(HorizontalAlignment.Center, button.HorizontalAlignment);
        Assert.Equal(new Thickness(0, 4, 0, 4), button.Margin);
        Assert.Equal("20px sans-serif", text.FontCss);

        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        Assert.Equal(120, button.ArrangedRect.Width);
        Assert.Equal(340, button.ArrangedRect.X); // (800 - 120) / 2
        Assert.Equal(4, button.ArrangedRect.Y);
    }

    [Fact]
    public void TranslatePoint_maps_through_layout_offsets()
    {
        // Demo DrawerHost shape: converted code maps points between visuals
        // (drawer/content placement). Layout offsets compose through the
        // visual chain like WPF, both directions.
        var root = new StackPanel();
        var child = new Border { Width = 50, Height = 30, Margin = new Thickness(10, 20, 0, 0) };
        root.AddChild(child);
        root.Measure(new WpfSize(800, 600));
        root.Arrange(new WpfRect(0, 0, 800, 600));
        // The fixed-size border centers in its stretched slot
        // (RenderRect 380,20,50,30); the mapping follows the render rect.
        Assert.Equal(new Point(380, 20), child.TranslatePoint(new Point(0, 0), root));
        Assert.Equal(new Point(0, 0), root.TranslatePoint(new Point(380, 20), child));
    }

    [Fact]
    public void Xaml_supports_vertical_alignment_on_any_element()
    {
        // Parity pin: the real HelloWorld BAML carries VerticalAlignment on
        // its TextBlock plus Window size/Icon, so the loose-XAML path must
        // accept and retain them too.
        var root = (Window)XamlLoader.Load(
            """<Window Width="525" Height="350" Icon="app.ico"><TextBlock Text="Hi" VerticalAlignment="Center"/></Window>""").Root;
        var text = (TextBlock)root.Content!;
        Assert.Equal(VerticalAlignment.Center, text.VerticalAlignment);
        Assert.Equal(525, root.Width);
        Assert.Equal(350, root.Height);
        Assert.Equal("app.ico", root.IconSource);
    }

    [Fact]
    public void Xaml_rejects_unknown_alignment()
    {
        Assert.Throws<XamlLoadException>(() =>
            XamlLoader.Load("""<Window><StackPanel><Button Content="x" HorizontalAlignment="Middle"/></StackPanel></Window>"""));
    }

    [Fact]
    public void Window_insets_content_by_its_margin()
    {
        var window = new Window();
        var panel = new StackPanel { Margin = new Thickness(8) };
        var label = new TextBlock { Text = "Hi" };
        panel.AddChild(label);
        window.Content = panel;

        window.Measure(new WpfSize(800, 600));
        window.Arrange(new WpfRect(0, 0, 800, 600));

        Assert.Equal(new WpfRect(8, 8, 784, 584), panel.ArrangedRect);
        Assert.Equal(new WpfRect(8, 8, 784, 20), label.ArrangedRect);
    }
}
