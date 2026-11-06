using DesktopWasm.Core;

namespace DesktopWasm.Core.Tests;

public sealed class PackageAssetNameTests
{
    [Theory]
    [InlineData("PC Space Map")]
    [InlineData("literal%20name")]
    [InlineData("name~20other")]
    [InlineData("工具")]
    [InlineData("module;with$chars")]
    public void AssetNamesSurviveUriAndMsbuildInterpretation(string name)
    {
        var hash = new string('A', 64);
        var leaf = PackageAssembly.AssetFileName(name, hash);
        Assert.Equal(leaf, Uri.UnescapeDataString(leaf));
        Assert.Equal(leaf, Path.GetFileName(leaf));
        Assert.DoesNotContain('%', leaf);
        Assert.DoesNotContain(';', leaf);
        Assert.DoesNotContain('$', leaf);
        Assert.EndsWith("." + hash.ToLowerInvariant() + ".bin", leaf);
        // Literal escape-like names retain a distinct physical identity.
        Assert.NotEqual(PackageAssembly.AssetFileName("PC%20Space%20Map", hash),
            PackageAssembly.AssetFileName("PC Space Map", hash));
    }

    [Fact]
    public void ExistingSimpleAssemblyNamesKeepTheirFileNames()
    {
        var hash = new string('b', 64);
        Assert.Equal("My.Library-1_2." + hash + ".bin", PackageAssembly.AssetFileName("My.Library-1_2", hash));
    }
}
