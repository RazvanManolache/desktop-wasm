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

    /// <summary>Decision RVW-20261030-2100-4467 §15.1: a valid converted manifest
    /// outranks stale upstream not-converted metadata for preparation
    /// eligibility only — MissingInput with a package-copy gap, stale
    /// upstream status preserved, never Available from metadata alone.</summary>
    [Fact]
    public void StaleNotConvertedWithValidManifestBecomesPreparationEligible()
    {
        var lib = new LegacyAdapters.WpfLibraryEntry("fixture-stale", "Stale", "Desc", "Control suites", "",
            "external", "not-converted", "https://example.com", "repo", "abc123", "P/P.csproj", "MIT", "Boundary.", true);
        var manifest = new LegacyAdapters.WpfManifestEntry("fixture-stale", 1, "wpf-convert-v1", "converted",
            "FixtureStale", [new PackageAssembly("FixtureStale", "FixtureStale.ff.bin", new string('f', 64), ["System.Runtime"])],
            [], "abc123");
        Assert.True(LegacyAdapters.IsPreparationEligible(lib, manifest));
        var app = LegacyAdapters.FromWpf(lib, nativeDemo: false, manifest);
        Assert.Equal(AppAvailability.MissingInput, app.Availability);
        Assert.Equal("not-converted", app.UpstreamStatus);
        Assert.NotNull(app.Package);
        Assert.Equal(["package copy for 'fixture-stale' hash-pinned from manifest"], app.Gaps);
        Assert.Contains("preparation eligibility only", app.AvailabilityNote, StringComparison.Ordinal);
        Assert.Contains("frozen-root manifest apps/fixture-stale", app.Evidence);
        CatalogValidator.ValidateDescriptor(app);
    }

    [Fact]
    public void ContradictoryManifestSlugStaysNotConverted()
    {
        var lib = new LegacyAdapters.WpfLibraryEntry("fixture-stale", "Stale", "Desc", "Control suites", "",
            "external", "not-converted", "https://example.com", "repo", "abc123", "P/P.csproj", "MIT", "Boundary.", true);
        var manifest = new LegacyAdapters.WpfManifestEntry("fixture-other", 1, "wpf-convert-v1", "converted",
            "FixtureOther", [new PackageAssembly("FixtureOther", "FixtureOther.ff.bin", new string('f', 64), ["System.Runtime"])],
            [], "abc123");
        Assert.False(LegacyAdapters.IsPreparationEligible(lib, manifest));
        var app = LegacyAdapters.FromWpf(lib, nativeDemo: false, manifest);
        Assert.Equal(AppAvailability.NotConverted, app.Availability);
        Assert.Equal("not-converted", app.UpstreamStatus);
        CatalogValidator.ValidateDescriptor(app);
    }

    [Fact]
    public void GappedManifestStaysNotConverted()
    {
        var lib = new LegacyAdapters.WpfLibraryEntry("fixture-stale", "Stale", "Desc", "Control suites", "",
            "external", "not-converted", "https://example.com", "repo", "abc123", "P/P.csproj", "MIT", "Boundary.", true);
        var manifest = new LegacyAdapters.WpfManifestEntry("fixture-stale", 1, "wpf-convert-v1", "converted",
            "FixtureStale", [new PackageAssembly("FixtureStale", "FixtureStale.ff.bin", new string('f', 64), ["System.Runtime"])],
            [new PackageGap("FixtureStale", "missing-dependency", "Something absent.")], "abc123");
        Assert.False(LegacyAdapters.IsPreparationEligible(lib, manifest));
        var app = LegacyAdapters.FromWpf(lib, nativeDemo: false, manifest);
        Assert.Equal(AppAvailability.NotConverted, app.Availability);
        // Self-contradictory (converted status with gaps): rejected loudly, never validated.
        Assert.Throws<InvalidDataException>(() => CatalogValidator.ValidateDescriptor(app));
    }

    [Fact]
    public void ManifestMissingEntryAssemblyStaysNotConverted()
    {
        var lib = new LegacyAdapters.WpfLibraryEntry("fixture-stale", "Stale", "Desc", "Control suites", "",
            "external", "not-converted", "https://example.com", "repo", "abc123", "P/P.csproj", "MIT", "Boundary.", true);
        var manifest = new LegacyAdapters.WpfManifestEntry("fixture-stale", 1, "wpf-convert-v1", "converted",
            "FixtureMissing", [new PackageAssembly("FixtureStale", "FixtureStale.ff.bin", new string('f', 64), ["System.Runtime"])],
            [], "abc123");
        Assert.False(LegacyAdapters.IsPreparationEligible(lib, manifest));
        var app = LegacyAdapters.FromWpf(lib, nativeDemo: false, manifest);
        Assert.Equal(AppAvailability.NotConverted, app.Availability);
        // Self-contradictory (converted status without the entry assembly): rejected loudly.
        Assert.Throws<InvalidDataException>(() => CatalogValidator.ValidateDescriptor(app));
    }

    [Fact]
    public void FailedManifestStaysNotConverted()
    {
        var lib = new LegacyAdapters.WpfLibraryEntry("fixture-stale", "Stale", "Desc", "Control suites", "",
            "external", "not-converted", "https://example.com", "repo", "abc123", "P/P.csproj", "MIT", "Boundary.", true);
        var manifest = new LegacyAdapters.WpfManifestEntry("fixture-stale", 1, "wpf-convert-v1", "conversion-failed",
            "FixtureStale", [],
            [new PackageGap("FixtureStale", "unmapped-type", "Nope.")], "abc123");
        Assert.False(LegacyAdapters.IsPreparationEligible(lib, manifest));
        var app = LegacyAdapters.FromWpf(lib, nativeDemo: false, manifest);
        Assert.Equal(AppAvailability.NotConverted, app.Availability);
        CatalogValidator.ValidateDescriptor(app);
    }

    [Fact]
    public void ReadyWithConvertedManifestKeepsExistingPreparationPath()
    {
        var lib = new LegacyAdapters.WpfLibraryEntry("fixture-real", "Real", "Desc", "Real apps", "",
            "external", "ready", "https://example.com", "repo", "abc123", "P/P.csproj", "MIT", "Boundary.", true);
        var manifest = new LegacyAdapters.WpfManifestEntry("fixture-real", 1, "wpf-convert-v1", "converted",
            "FixtureReal", [new PackageAssembly("FixtureReal", "FixtureReal.ab.bin", new string('f', 64), ["System.Runtime"])],
            [], "abc123");
        Assert.True(LegacyAdapters.IsPreparationEligible(lib, manifest));
        var app = LegacyAdapters.FromWpf(lib, nativeDemo: false, manifest);
        Assert.Equal(AppAvailability.MissingInput, app.Availability);
        Assert.Equal("ready", app.UpstreamStatus);
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
