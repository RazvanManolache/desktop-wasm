using DesktopWasm.Graphics;

namespace DesktopWasm.Core.Tests;

public sealed class SharedApplicationFontTests
{
    [Fact]
    public void Embedded_family_alias_resolves_the_same_face_and_metrics()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DesktopWasm.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var bytes = File.ReadAllBytes(Path.Combine(root.FullName, "src/DesktopWasm.BrowserHost/wwwroot/fonts/NotoSans-Regular.ttf"));
        TextShaper.RegisterEmbeddedFont(bytes, ["studio-font-test"]);
        using var alias = TextShaper.ResolveTypeface("studio-font-test");
        using var named = TextShaper.ResolveTypeface("Noto Sans");
        Assert.Equal("Noto Sans", alias.FamilyName);
        Assert.Equal(named.FamilyName, alias.FamilyName);
        var actual = TextShaper.Measure("Application", "18px studio-font-test");
        var expected = TextShaper.Measure("Application", "18px Noto Sans");
        Assert.Equal(expected, actual);
        Assert.True(actual.Width > 20);
    }
}
