namespace DesktopWasm.Catalog.Tests;

public sealed class PathSafetyTests
{
    [Theory]
    [InlineData("HelloWorld.3fb9c9617cc6ed4d970529d9947c39bfa87e557cb3acbb88f39bacde8b413eb0.bin")]
    [InlineData("PC Space Map.709a8d3ca13ff1b5cf25ceced7169f87165a205300c03283baa03c42cce1c462.bin")]
    [InlineData("Calculator.dll")]
    [InlineData("Tic Tac Toe.exe")]
    public void SafeArtifactNamesAccepted(string file)
    {
        Assert.True(CatalogValidator.IsPathSafe(file));
    }

    [Theory]
    [InlineData("../escape.bin")]
    [InlineData("a/b.bin")]
    [InlineData("a\\b.bin")]
    [InlineData("C:/evil.bin")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a%2f.bin")]
    [InlineData("a%b.bin")]
    [InlineData("evil\0.bin")]
    public void HostileArtifactNamesRejected(string file)
    {
        Assert.False(CatalogValidator.IsPathSafe(file));
    }

    [Fact]
    public void OverlongNameRejected()
    {
        Assert.False(CatalogValidator.IsPathSafe(new string('a', 201)));
    }
}
