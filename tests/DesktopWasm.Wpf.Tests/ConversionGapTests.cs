namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Real-world conversion boundary, pinned by the vendored MIT fixture
/// (Fixtures/CalculatorView.xaml, dariuszdbr/CalculatorWpfMVVM).
/// These tests lock the current precise rejection; when the engine learns
/// a construct, its coverage moves to a positive layout test.
/// </summary>
[ResetAmbientState]
public sealed class ConversionGapTests
{
    private static string FixturePath(string name)
    {
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var candidate = System.IO.Path.Combine(d.FullName, "Fixtures", name);
            if (File.Exists(candidate)) return candidate;
        }
        return System.IO.Path.Combine(dir, "Fixtures", name);
    }

    [Fact]
    public void Real_world_chrome_attributes_are_accepted_and_ignored()
    {
        const string xaml = """
            <Window x:Class="Calculator.MainWindow"
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                    mc:Ignorable="d" d:DesignHeight="100" d:DesignWidth="100"
                    Title="MainWindow" Height="655" Width="300" ResizeMode="CanMinimize">
              <StackPanel><Button Content="OK"/></StackPanel>
            </Window>
            """;
        var window = (Window)XamlLoader.Load(xaml).Root;
        Assert.Equal("MainWindow", window.Title);
    }

    [Fact]
    public void Calculator_fixture_fails_precisely_at_data_binding()
    {
        var xaml = File.ReadAllText(FixturePath("CalculatorView.xaml"));
        // Window chrome, x:Class, design namespaces, row/column definitions,
        // styles-free buttons, and the <Window.DataContext> property element
        // all parse; the first unsupported construct is the nested view-model
        // type itself (view-model instantiation is a later milestone).
        var ex = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(xaml));
        Assert.Contains("Unknown element <CalculatorViewModel>", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Calculator_keypad_subset_renders_end_to_end()
    {
        const string xaml = """
            <Window Title="Calc">
              <Grid>
                <Grid.RowDefinitions><RowDefinition Height="2*"/><RowDefinition Height="*"/></Grid.RowDefinitions>
                <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions>
                <TextBlock Text="0" Grid.Row="0" Grid.Column="0" Grid.ColumnSpan="3"/>
                <Button Content="1" Grid.Row="1" Grid.Column="0"/>
                <Button Content="2" Grid.Row="1" Grid.Column="1"/>
                <Button Content="=" Grid.Row="1" Grid.Column="2"/>
              </Grid>
            </Window>
            """;
        var window = (Window)XamlLoader.Load(xaml).Root;
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(300, 200));
        var json = WasmMilCore.ExportFrameJson(commands);
        Assert.Contains("\"1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"2\"", json, StringComparison.Ordinal);
        Assert.Contains("\"=\"", json, StringComparison.Ordinal);

        var grid = (Grid)window.Content!;
        var display = grid.LogicalChildren[0];
        var one = grid.LogicalChildren[1];
        Assert.Equal(300, display.ArrangedRect.Width);
        Assert.Equal(0, display.ArrangedRect.Y);
        Assert.Equal(2.0 / 3 * 200, display.ArrangedRect.Height, precision: 3);
        Assert.Equal(0, one.ArrangedRect.X);
        Assert.Equal(2.0 / 3 * 200, one.ArrangedRect.Y, precision: 3);
        Assert.Equal(100, one.ArrangedRect.Width);
    }
}
