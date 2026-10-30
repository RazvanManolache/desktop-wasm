namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class GridDefinitionsTests
{
    private static Window ArrangeTree(UIElement content, double w = 800, double h = 600)
    {
        var window = new Window();
        window.Content = content;
        window.Measure(new WpfSize(w, h));
        window.Arrange(new WpfRect(0, 0, w, h));
        return window;
    }

    [Fact]
    public void GridLength_parses_all_forms()
    {
        Assert.Equal(new GridLength(4, GridUnitType.Star), GridLength.Parse("4*"));
        Assert.Equal(new GridLength(1, GridUnitType.Star), GridLength.Parse("*"));
        Assert.Equal(new GridLength(1, GridUnitType.Auto), GridLength.Parse("Auto"));
        Assert.Equal(new GridLength(120, GridUnitType.Pixel), GridLength.Parse("120"));
        Assert.Throws<FormatException>(() => GridLength.Parse("wide"));
    }

    [Fact]
    public void Star_columns_share_remaining_space_by_weight()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star() });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Parse("3*") });
        var left = new Button { Content = "L" };
        var right = new Button { Content = "R" };
        left.SetAttached("Grid.Column", 0);
        right.SetAttached("Grid.Column", 1);
        grid.AddChild(left);
        grid.AddChild(right);

        ArrangeTree(grid);

        Assert.Equal(new WpfRect(0, 0, 200, 600), left.ArrangedRect);
        Assert.Equal(new WpfRect(200, 0, 600, 600), right.ArrangedRect);
    }

    [Fact]
    public void Pixel_and_auto_tracks_size_to_content()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Pixel(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var button = new Button { Content = "OK" };
        var label = new TextBlock { Text = "Hi" };
        button.SetAttached("Grid.Column", 0);
        label.SetAttached("Grid.Column", 1);
        grid.AddChild(button);
        grid.AddChild(label);

        ArrangeTree(grid);

        Assert.Equal(120, button.ArrangedRect.Width);
        Assert.Equal(120, label.ArrangedRect.X);
        Assert.Equal(label.DesiredSize.Width, label.ArrangedRect.Width, precision: 5);
    }

    [Fact]
    public void MinHeight_clamps_star_rows()
    {
        // WPFGallery ColorTile shape: a star row that never drops below 30px.
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Parse("*"), MinHeight = 30 });
        grid.AddChild(new Button { Content = "x" });
        ArrangeTree(grid, 400, 20);
        Assert.Equal(30, grid.LogicalChildren[0].ArrangedRect.Height);
    }

    [Fact]
    public void Bogus_definition_attribute_is_precise()
    {
        var ex = Assert.Throws<XamlLoadException>(() =>
            XamlLoader.Load("<Window><Grid><Grid.RowDefinitions><RowDefinition Height=\"*\" Bogus=\"1\" /></Grid.RowDefinitions></Grid></Window>"));
        Assert.Contains("Bogus", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Row_shorthand_and_column_span_arrange_correctly()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star() });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star() });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star() });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star() });
        var wide = new Button { Content = "=" };
        wide.SetAttached("Row", 1); // wild-XAML shorthand for Grid.Row
        wide.SetAttached("Grid.ColumnSpan", 2);
        grid.AddChild(wide);

        ArrangeTree(grid, 400, 200);

        Assert.Equal(new WpfRect(0, 100, 400, 100), wide.ArrangedRect);
    }

    [Fact]
    public void Xaml_grid_definitions_drive_calculator_proportions()
    {
        const string xaml = """
            <Window Title="Calc">
              <Grid>
                <Grid.RowDefinitions><RowDefinition Height="4*"/><RowDefinition Height="1*"/><RowDefinition Height="6*"/></Grid.RowDefinitions>
                <Button Content="display" Grid.Row="0"/>
                <Button Content="back" Grid.Row="1"/>
                <Button Content="=" Grid.Row="2"/>
              </Grid>
            </Window>
            """;
        var window = (Window)XamlLoader.Load(xaml).Root;
        window.Measure(new WpfSize(300, 660));
        window.Arrange(new WpfRect(0, 0, 300, 660));

        var grid = (Grid)window.Content!;
        var display = (Button)grid.LogicalChildren[0];
        var equals = (Button)grid.LogicalChildren[2];
        Assert.Equal(0, display.ArrangedRect.Y);
        Assert.Equal(4.0 / 11 * 660, display.ArrangedRect.Height, precision: 3);
        Assert.Equal(5.0 / 11 * 660, equals.ArrangedRect.Y, precision: 3);
        Assert.Equal(6.0 / 11 * 660, equals.ArrangedRect.Height, precision: 3);
    }
}
