using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace DesktopWasm.Wpf.Tests;

public sealed class TmpProbeBg
{
    private readonly ITestOutputHelper _out;
    public TmpProbeBg(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Print_bg_facts()
    {
        var builder = typeof(DockingManagerIntegrationTests).GetMethod("GridWithTransparentBackground", BindingFlags.NonPublic | BindingFlags.Static)!;
        var bytes = (byte[])builder.Invoke(null, null)!;
        using var ms = new MemoryStream(bytes);
        var w = XamlLoader.LoadComponent<Window>(ms);
        var g = (Grid)((Window)w).Content!;
        var dp = DependencyProperty.FromName(g.GetType(), "Background");
        _out.WriteLine($"PROBE gridType={g.GetType().FullName} dp={(dp is null ? "null" : dp.OwnerType.FullName + "/" + dp.PropertyType.FullName)} bgNull={g.Background is null} store={(g.GetValue(Panel.BackgroundProperty) is null ? "null" : "SET")}");
        var g2 = new Grid();
        var bg2 = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
        g2.SetValue(Panel.BackgroundProperty, bg2);
        _out.WriteLine($"PROBE direct bgNull={g2.Background is null} same={ReferenceEquals(bg2, g2.Background)}");
    }
}
