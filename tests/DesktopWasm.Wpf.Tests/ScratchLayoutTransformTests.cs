using System.IO;
using System.Text;
using Xunit;

namespace DesktopWasm.Wpf.Tests;

public sealed class ScratchLayoutTransformTests
{
    private static Window Load(string inner)
    {
        var xaml = @"
<Window xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Window.Resources>
    <ControlTemplate x:Key=""T"" TargetType=""{x:Type Button}"">
      <Border>" + inner + @"</Border>
    </ControlTemplate>
  </Window.Resources>
</Window>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
        return XamlLoader.LoadComponent<Window>(stream);
    }

    [Fact]
    public void NamedScaleTransformParses()
    {
        var window = Load(@"
        <Border.LayoutTransform>
          <TransformGroup>
            <ScaleTransform x:Name=""ContentSiteScaleTransform"" />
          </TransformGroup>
        </Border.LayoutTransform>");
        Assert.NotNull(window);
    }

    [Fact]
    public void TemplateBoundRotateTransformParses()
    {
        var window = Load(@"
        <Border.LayoutTransform>
          <TransformGroup>
            <RotateTransform Angle=""{TemplateBinding Tag}"" />
          </TransformGroup>
        </Border.LayoutTransform>");
        Assert.NotNull(window);
    }
}
