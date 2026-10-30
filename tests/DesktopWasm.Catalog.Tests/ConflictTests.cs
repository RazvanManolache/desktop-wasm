using DesktopWasm.AppModel;

namespace DesktopWasm.Catalog.Tests;

public sealed class ConflictTests
{
    [Fact]
    public void DuplicateIdThrows()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            new AppRegistry([Fx.Descriptor("fixture-dup"), Fx.Descriptor("fixture-dup")]));
        Assert.Contains("fixture-dup", ex.Message);
    }

    [Fact]
    public void AliasCollidingWithIdThrows()
    {
        var ex = Assert.Throws<InvalidDataException>(() => new AppRegistry(
            [Fx.Descriptor("fixture-a"), Fx.Descriptor("fixture-b", aliases: ["fixture-a"])]));
        Assert.Contains("fixture-a", ex.Message);
    }

    [Fact]
    public void AliasClaimedTwiceThrows()
    {
        var ex = Assert.Throws<InvalidDataException>(() => new AppRegistry(
            [Fx.Descriptor("fixture-a", aliases: ["fixture-shared"]),
             Fx.Descriptor("fixture-b", aliases: ["fixture-shared"])]));
        Assert.Contains("fixture-shared", ex.Message);
    }

    [Fact]
    public void RouteAliasCaseCollidesDeterministically()
    {
        var conflicts = CatalogValidator.FindConflicts(
            [Fx.Descriptor("fixture-a", aliases: ["/demo/fixture-x"]),
             Fx.Descriptor("fixture-b", aliases: ["/DEMO/fixture-x"])]);
        Assert.Single(conflicts);
        Assert.Contains("/demo/fixture-x", conflicts[0]);
    }

    [Fact]
    public void FindConflictsIsDeterministicAndSorted()
    {
        List<AppDescriptor> Apps() =>
        [
            Fx.Descriptor("fixture-b", aliases: ["fixture-clash"]),
            Fx.Descriptor("fixture-a", aliases: ["fixture-clash"]),
            Fx.Descriptor("fixture-c", aliases: ["fixture-clash"]),
        ];
        var first = CatalogValidator.FindConflicts(Apps());
        var second = CatalogValidator.FindConflicts(Apps().AsEnumerable().Reverse());
        Assert.Equal(first, second);
        Assert.Equal(first.OrderBy(x => x, StringComparer.Ordinal), first);
    }

    [Fact]
    public void RegistryResolvesIdAliasAndRoute()
    {
        var registry = new AppRegistry([Fx.Descriptor("fixture-app", aliases: ["fixture-legacy", "/app/fixture-app"])]);
        Assert.Equal("fixture-app", registry.Resolve("fixture-app").Id);
        Assert.Equal("fixture-app", registry.Resolve("FIXTURE-LEGACY").Id);
        Assert.Equal("fixture-app", registry.Resolve("/APP/fixture-app").Id);
        Assert.False(registry.TryResolve("../escape", out _));
        Assert.False(registry.TryResolve("", out _));
        Assert.Throws<KeyNotFoundException>(() => registry.Resolve("fixture-nope"));
    }
}
