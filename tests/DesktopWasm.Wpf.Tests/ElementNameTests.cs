namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class ElementNameTests
{
    [Fact]
    public void Markup_names_are_readable_by_original_application_event_handlers()
    {
        var loaded = XamlLoader.Load("""
            <StackPanel xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Button x:Name="BPlus" Content="+"/>
              <Button Name="BEquals" Content="="/>
            </StackPanel>
            """);
        Assert.Equal("BPlus", Assert.IsType<Button>(loaded.Names["BPlus"]).Name);
        Assert.Equal("BEquals", Assert.IsType<Button>(loaded.Names["BEquals"]).Name);
        var button = new Button();
        Assert.Equal(string.Empty, button.Name);
        button.SetValue(FrameworkElement.NameProperty, "Direct");
        Assert.Equal("Direct", button.Name);
    }
}
