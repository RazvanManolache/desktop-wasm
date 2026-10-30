namespace DesktopWasm.Catalog.Tests;

public sealed class RouteAliasTests
{
    [Theory]
    [InlineData("/demo/hello-real")]
    [InlineData("/app/paint")]
    [InlineData("/DEMO/Calc-Keypad")]
    [InlineData("/APP/TIC-TAC-TOE")]
    public void ValidRoutesNormalize(string route)
    {
        Assert.True(CatalogValidator.IsRouteAlias(route));
        var normal = CatalogValidator.NormalizeRoute(route);
        Assert.Equal(route.ToLowerInvariant(), normal);
    }

    [Theory]
    [InlineData("/demo/../escape")]
    [InlineData("/app/%2e%2e/escape")]
    [InlineData("/demo/%2Fescape")]
    [InlineData("/demo/a%20b")]
    [InlineData("/demo/a/b")]
    [InlineData("/demo/a\\b")]
    [InlineData("/demo/a:b")]
    [InlineData("/demo/")]
    [InlineData("/demo")]
    [InlineData("/other/slug")]
    [InlineData("demo/slug")]
    [InlineData("/demo/./x")]
    [InlineData("/demo/-x-/../y")]
    public void HostileRoutesRejected(string route)
    {
        Assert.False(CatalogValidator.IsRouteAlias(route));
        Assert.Throws<InvalidDataException>(() => CatalogValidator.NormalizeRoute(route));
    }

    [Fact]
    public void RegistryRejectsTraversalLookup()
    {
        var registry = new AppRegistry([Fx.Descriptor("fixture-app", aliases: ["/app/fixture-app"])]);
        Assert.False(registry.TryResolve("/app/../fixture-app", out _));
        Assert.False(registry.TryResolve("/app/%2e%2e/fixture-app", out _));
        Assert.True(registry.TryResolve("/app/fixture-app", out _));
    }
}
