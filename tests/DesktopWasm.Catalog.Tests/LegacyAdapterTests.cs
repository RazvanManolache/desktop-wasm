using DesktopWasm.AppModel;

namespace DesktopWasm.Catalog.Tests;

public sealed class LegacyAdapterTests
{
    [Fact]
    public void WinFormsSceneCarriesRouteAndFolderAliases()
    {
        var app = LegacyAdapters.FromWinFormsScene(
            new LegacyAdapters.WinFormsSceneEntry("TicTacToe", "tic-tac-toe", "Tic-tac-toe", null, null, "tictactoe", null, null),
            "tictactoe", staged: false, "not staged");
        Assert.Equal("tic-tac-toe", app.Id);
        Assert.Contains("/app/tic-tac-toe", app.Aliases);
        Assert.Contains("tictactoe", app.Aliases);
        Assert.Equal(AppCapability.WinForms, app.Capabilities);
        Assert.Equal(AppAvailability.MissingInput, app.Availability);
    }

    [Fact]
    public void WpfConvertedKeepsManifestIdentity()
    {
        var lib = new LegacyAdapters.WpfLibraryEntry("fixture-real", "Real", "Desc", "Real apps", "",
            "external", "ready", "https://example.com", "repo", "abc123", "P/P.csproj", "MIT", "Boundary.", true);
        var manifest = new LegacyAdapters.WpfManifestEntry("fixture-real", 1, "wpf-convert-v1", "converted",
            "FixtureReal", [new PackageAssembly("FixtureReal", "FixtureReal.ab.bin", new string('f', 64), ["System.Runtime"])],
            [], "abc123");
        var app = LegacyAdapters.FromWpf(lib, nativeDemo: false, manifest);
        Assert.Equal("converted-app", app.Entry.Kind);
        Assert.Equal("FixtureReal", app.Entry.EntryAssembly);
        Assert.NotNull(app.Package);
        CatalogValidator.ValidateDescriptor(app);
    }

    [Fact]
    public void WpfNativeDemoNeedsNoPackage()
    {
        var lib = new LegacyAdapters.WpfLibraryEntry("fixture-lab", "Lab", "Desc", "Interaction", "",
            "toolkit", "ready", null, null, null, null, null, null, false);
        var app = LegacyAdapters.FromWpf(lib, nativeDemo: true, null);
        Assert.Equal("native-demo", app.Entry.Kind);
        Assert.Null(app.Package);
        Assert.Contains("host", app.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
        CatalogValidator.ValidateDescriptor(app);
    }

    [Fact]
    public void WinFormsTransformAdapterTagsArtifacts()
    {
        var package = LegacyAdapters.FromWinFormsTransform("Calc",
            [new PackageAssembly("Calc", "Calc.dll", new string('a', 64), ["System.Runtime"])]);
        Assert.Equal("converted", package.Status);
        Assert.Equal("wf-asm", package.Assemblies[0].Artifact);
        CatalogValidator.ValidatePackage("fixture-calc", "wf-rewrite-v1", "Calc", package);
    }
}
