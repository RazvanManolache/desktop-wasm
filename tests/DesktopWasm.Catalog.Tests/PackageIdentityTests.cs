using DesktopWasm.AppModel;

namespace DesktopWasm.Catalog.Tests;

public sealed class PackageIdentityTests
{
    [Fact]
    public void WpfBinPackageValidates()
    {
        var package = Fx.BinPackage("HelloWorld");
        CatalogValidator.ValidatePackage("fixture-wpf", "wpf-convert-v1", "HelloWorld", package);
    }

    [Fact]
    public void WinFormsAsmAdapterValidates()
    {
        // Legacy .dll endpoint lands as a canonical package (review item 5).
        var package = Fx.DllPackage("Calculator");
        Assert.Equal("wf-asm", package.Assemblies[0].Artifact);
        CatalogValidator.ValidatePackage("fixture-wf", "wf-rewrite-v1", "Calculator", package);
    }

    [Fact]
    public void FailedPreparationNeedsGaps()
    {
        var failed = new PackageIdentity(1, "wpf-convert-v1", "conversion-failed",
            [new PackageAssembly("PC Space Map", "PC Space Map.ab.bin", new string('c', 64), ["System.Runtime"])],
            [new PackageGap("PC Space Map", "missing-dependency", "Absent from upstream build output.")], null);
        CatalogValidator.ValidatePackage("fixture-pc", "wpf-convert-v1", "PC Space Map", failed);
    }

    [Theory]
    [InlineData("nothex", "converted")]
    [InlineData("aabb", "converted")]
    public void BadShaRejected(string sha, string status)
    {
        var package = new PackageIdentity(1, "wpf-convert-v1", status,
            [new PackageAssembly("E", "E.ab.bin", sha, [])], status == "converted" ? [] : [new PackageGap("E", "k", "d")], null);
        Assert.Throws<InvalidDataException>(() =>
            CatalogValidator.ValidatePackage("fixture-x", "wpf-convert-v1", "E", package));
    }

    [Fact]
    public void WrongSuffixRejected()
    {
        var package = new PackageIdentity(1, "wpf-convert-v1", "converted",
            [new PackageAssembly("E", "E.txt", new string('d', 64), [])], [], null);
        Assert.Throws<InvalidDataException>(() =>
            CatalogValidator.ValidatePackage("fixture-x", "wpf-convert-v1", "E", package));
    }

    [Fact]
    public void TraversalFileRejected()
    {
        var package = new PackageIdentity(1, "wpf-convert-v1", "converted",
            [new PackageAssembly("E", "../E.ab.bin", new string('d', 64), [])], [], null);
        Assert.Throws<InvalidDataException>(() =>
            CatalogValidator.ValidatePackage("fixture-x", "wpf-convert-v1", "E", package));
    }

    [Fact]
    public void ConvertedWithGapsRejected()
    {
        var package = Fx.BinPackage("E") with { Gaps = [new PackageGap("E", "k", "d")] };
        Assert.Throws<InvalidDataException>(() =>
            CatalogValidator.ValidatePackage("fixture-x", "wpf-convert-v1", "E", package));
    }

    [Fact]
    public void ConvertedWithoutEntryRejected()
    {
        var package = Fx.BinPackage("Other");
        Assert.Throws<InvalidDataException>(() =>
            CatalogValidator.ValidatePackage("fixture-x", "wpf-convert-v1", "E", package));
    }

    [Fact]
    public void FailedWithoutGapsRejected()
    {
        var package = new PackageIdentity(1, "wpf-convert-v1", "conversion-failed",
            [new PackageAssembly("E", "E.ab.bin", new string('d', 64), [])], [], null);
        Assert.Throws<InvalidDataException>(() =>
            CatalogValidator.ValidatePackage("fixture-x", "wpf-convert-v1", "E", package));
    }

    [Fact]
    public void MapMismatchRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            CatalogValidator.ValidatePackage("fixture-x", "other-map", "E", Fx.BinPackage("E")));
    }

    [Fact]
    public void DuplicateAssemblyRejected()
    {
        var package = new PackageIdentity(1, "wpf-convert-v1", "converted",
            [new PackageAssembly("E", "E.ab.bin", new string('d', 64), []),
             new PackageAssembly("e", "e.ab.bin", new string('d', 64), [])], [], null);
        Assert.Throws<InvalidDataException>(() =>
            CatalogValidator.ValidatePackage("fixture-x", "wpf-convert-v1", "E", package));
    }

    [Fact]
    public void UnknownArtifactRejected()
    {
        var package = new PackageIdentity(1, "wpf-convert-v1", "converted",
            [new PackageAssembly("E", "E.ab.bin", new string('d', 64), [], Artifact: "legacy-zip")], [], null);
        Assert.Throws<InvalidDataException>(() =>
            CatalogValidator.ValidatePackage("fixture-x", "wpf-convert-v1", "E", package));
    }

    [Fact]
    public void ArtifactOnlySpaceUnescaped()
    {
        // Only %20 is restored; any other escape fails the import, not the shell.
        Assert.Equal("PC Space Map.ab.bin",
            LegacyAdapters.NormalizeArtifactFile("PC%20Space%20Map.ab.bin", "fixture-x"));
        Assert.Throws<InvalidDataException>(() =>
            LegacyAdapters.NormalizeArtifactFile("E%2f.ab.bin", "fixture-x"));
    }
}
