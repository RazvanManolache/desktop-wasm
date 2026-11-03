using System.Security.Cryptography;
using DesktopWasm.AppModel;
using DesktopWasm.Catalog;
using DesktopWasm.Catalog.Tool;

namespace DesktopWasm.Catalog.Tests;

/// <summary>RECOVER dw-catalog: joint descriptor/receipt/entry contract for the
/// real TestApp closure, with no false readiness. The joint package does not
/// exist yet (no manifest, runtime lane idle), so these tests pin the contract
/// against the real PROPOSED closure shape (entry TestApp, mixed .exe/.dll
/// artifacts, WindowsFormsIntegration runtime-provided and never packaged)
/// using synthetic fixture slugs and temp directories only; never the live
/// catalog, frozen originals, or real upstream identities. The HTML-fallback
/// case reproduces the live verifier finding: the static mount answers a
/// missing joint manifest with 200 text/html, which must fail closed, never
/// verify as a manifest.</summary>
public sealed class JointDescriptorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "joint-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static string ShaOfFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>Joint package shape modeled on the real proposed closure:
    /// entry assembly plus mixed .exe/.dll artifacts with 64-hex pins,
    /// converted and gapless. Fixture names/shas only.</summary>
    private static PackageIdentity JointPackage(string entry = "FixtureJoint", string map = "fixture-joint-v1") =>
        new(1, map, "converted",
            [new PackageAssembly(entry, entry + ".exe", new string('a', 64), ["System.Runtime"], Artifact: "mixed"),
             new PackageAssembly("FixtureDock", "FixtureDock.dll", new string('b', 64), ["System.Runtime"], Artifact: "mixed"),
             new PackageAssembly("FixtureJson", "FixtureJson.dll", new string('c', 64), ["System.Runtime"], Artifact: "mixed")],
            [], null);

    private static AppDescriptor JointDescriptor(string id = "fixture-joint", string map = "fixture-joint-v1") =>
        Fx.Descriptor(id,
            aliases: ["/demo/" + id, "/app/" + id],
            capabilities: AppCapability.Mixed,
            entry: new AppEntry("mixed-session", "FixtureJoint", id, null,
                new AppBridge("interop-host", "mixed-session-v1")),
            package: JointPackage("FixtureJoint", map),
            transformVersion: map);

    /// <summary>A staged directory whose app-manifest.json is HTML fallback
    /// bytes (the live joint failure: 200 text/html instead of a manifest).
    /// Returns (dir, htmlSha).</summary>
    private (string Dir, string HtmlSha) StageHtmlFallbackDir(string id)
    {
        var dir = Path.Combine(_dir, "staging", id);
        Directory.CreateDirectory(dir);
        const string html = "<!doctype html><html><body>studio index fallback</body></html>";
        File.WriteAllText(Path.Combine(dir, "app-manifest.json"), html);
        return (dir, ShaOfFile(Path.Combine(dir, "app-manifest.json")));
    }

    private string WriteRegistry(params AppDescriptor[] apps)
    {
        Directory.CreateDirectory(_dir);
        var appsPath = Path.Combine(_dir, "apps.json");
        File.WriteAllText(appsPath, CatalogSerializer.Serialize([.. apps]));
        return appsPath;
    }

    private string WriteReceipts(params (string Id, string Path, string Sha)[] receipts)
    {
        var path = Path.Combine(_dir, "receipts-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(receipts.Select(r =>
            new { id = r.Id, localPath = r.Path, sha256 = r.Sha, stagedUtc = "2026-10-31T00:00:00Z" })));
        return path;
    }

    [Fact]
    public void HtmlFallbackManifestFailsClosed()
    {
        // Even when the receipt pins the fallback file's own bytes, the
        // closure never verifies: HTML is not a manifest (parse fails
        // closed), so this is not a verifiable joint staging.
        var (dir, htmlSha) = StageHtmlFallbackDir("fixture-joint");
        Assert.Equal("manifest-directory", ReceiptVerifier.Classify(dir));
        Assert.False(ReceiptVerifier.ClosureMatches(dir, Path.Combine(dir, "app-manifest.json")));
        Assert.False(ReceiptVerifier.DigestMatches(dir, htmlSha));
    }

    [Fact]
    public void FreshJointReceiptOverHtmlFallbackAbortsWithoutWriting()
    {
        var (dir, htmlSha) = StageHtmlFallbackDir("fixture-joint");
        var appsPath = WriteRegistry(JointDescriptor());
        var before = File.ReadAllText(appsPath);
        var bad = WriteReceipts(("fixture-joint", dir, htmlSha));

        var ex = Assert.Throws<InvalidDataException>(() => Importer.Refresh(_dir, new[] { bad }));
        Assert.Contains("digest mismatch", ex.Message);
        Assert.Equal(before, File.ReadAllText(appsPath));
    }

    [Fact]
    public void JointMixedClosureShapeValidates()
    {
        CatalogValidator.ValidatePackage("fixture-joint", "fixture-joint-v1", "FixtureJoint",
            JointPackage());
    }

    [Fact]
    public void JointClosureWithoutEntryRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            CatalogValidator.ValidatePackage("fixture-joint", "fixture-joint-v1", "FixtureJoint",
                JointPackage() with
                {
                    Assemblies = [new PackageAssembly("FixtureDock", "FixtureDock.dll",
                        new string('b', 64), ["System.Runtime"], Artifact: "mixed")],
                }));
    }

    private static AppDescriptor StagedFixture(string id) =>
        Fx.Descriptor(id,
            capabilities: AppCapability.Mixed,
            availability: AppAvailability.Staged,
            availabilityNote: "Staged locally at 'inputs/mixed/fixture-joint' (sha aabbccddeeff…); serving + behavior unverified.",
            gaps: [],
            entry: new AppEntry("mixed-session", "FixtureJoint", id, null,
                new AppBridge("interop-host", "mixed-session-v1"))) with
        {
            Evidence = ["local:inputs/mixed/fixture-joint#aabbccddeeffaabbccddeeffaabbccddeeffaabbccddeeffaabbccddeeff0011"],
        };

    private static Dictionary<string, LocalInputMapper.StagedReceipt> StagedReceipts(params string[] ids) =>
        ids.ToDictionary(id => id,
            id => new LocalInputMapper.StagedReceipt(id, "inputs/mixed/" + id, new string('e', 64), "2026-10-31T00:00:00Z"),
            StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void StagedEntryWithReceiptStaysStagedNeverAvailable()
    {
        // Bytes-verified staging is not readiness: a verified receipt keeps
        // the entry Staged (never promotes to Available), refreshing the
        // local: evidence with empty gaps and an unverified note.
        var after = LocalInputMapper.Refresh(StagedFixture("fixture-joint"), StagedReceipts("fixture-joint"));
        Assert.Equal(AppAvailability.Staged, after.Availability);
        Assert.NotEqual(AppAvailability.Available, after.Availability);
        Assert.Empty(after.Gaps);
        Assert.Contains(after.Evidence, e => e.StartsWith("local:", StringComparison.Ordinal));
        Assert.Contains("unverified", after.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
        CatalogValidator.ValidateDescriptor(after);
        var menu = new AppRegistry([after]).Menu();
        Assert.False(Assert.Single(menu).IsLaunchable);
    }

    [Fact]
    public void StagedEntryWithoutReceiptDemotesHonestly()
    {
        var after = LocalInputMapper.Refresh(StagedFixture("fixture-joint"),
            StagedReceipts("fixture-other"));
        Assert.Equal(AppAvailability.MissingInput, after.Availability);
        Assert.NotEmpty(after.Gaps);
    }

    [Fact]
    public void StagedCarriesForwardOnVerifiedBytes()
    {
        // A partial batch naming another entry must not demote still-present
        // staged inputs (same cumulative guarantee as Available): carry
        // re-verifies real bytes, so this uses a real temp file.
        var dir = Path.Combine(_dir, "staging");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "fixture-joint.bin");
        File.WriteAllText(path, "staged-bytes");
        var app = StagedFixture("fixture-joint") with
        {
            Evidence = [$"local:{path}#{ShaOfFile(path)}"],
        };
        var kept = LocalInputMapper.CarryForwardReceipts(
            [app], StagedReceipts("fixture-other"), _ => true);
        Assert.True(kept.ContainsKey("fixture-joint"));
        var gone = LocalInputMapper.CarryForwardReceipts(
            [app], StagedReceipts("fixture-other"), _ => false);
        Assert.Empty(gone);
    }

    [Fact]
    public void StagedWithoutLocalEvidenceFails()
    {
        var app = StagedFixture("fixture-staged-noev") with { Evidence = [] };
        var ex = Assert.Throws<InvalidDataException>(() => CatalogValidator.ValidateDescriptor(app));
        Assert.Contains("local", ex.Message);
    }

    [Fact]
    public void StagedWithoutUnverifiedNoteFails()
    {
        var app = StagedFixture("fixture-staged-note") with { AvailabilityNote = "Staged locally, ready to serve." };
        var ex = Assert.Throws<InvalidDataException>(() => CatalogValidator.ValidateDescriptor(app));
        Assert.Contains("unverified", ex.Message);
    }

    [Fact]
    public void MixedStagedWithoutBridgeFails()
    {
        var app = StagedFixture("fixture-staged-nobridge") with
        {
            Entry = new AppEntry("mixed-session", "FixtureJoint", "fixture-staged-nobridge", null, null),
        };
        var ex = Assert.Throws<InvalidDataException>(() => CatalogValidator.ValidateDescriptor(app));
        Assert.Contains("typed bridge identity", ex.Message);
    }

    [Fact]
    public void LiveJointRecordIsStagedNotReady()
    {
        // RECOVER-CATALOG-2 acceptance on the live registry: the joint
        // record is bytes-verified staging only — staged with the real
        // manifest pin, gapless converted mixed package, typed bridge from
        // the producer receipt, explicitly unverified, never launchable.
        var registry = CatalogSerializer.Deserialize(
            File.ReadAllText(Path.Combine(Fx.CatalogDir(), "apps.json")));
        var app = registry.Resolve("testapp-joint");
        Assert.Equal(AppAvailability.Staged, app.Availability);
        Assert.NotEqual(AppAvailability.Available, app.Availability);
        Assert.Empty(app.Gaps);
        Assert.Equal("converted", app.UpstreamStatus);
        Assert.Equal("TestApp", app.Entry.EntryAssembly);
        Assert.Equal("testapp-joint", app.Entry.ManifestSlug);
        Assert.NotNull(app.Entry.Bridge);
        Assert.False(string.IsNullOrWhiteSpace(app.Entry.Bridge.Provider));
        Assert.False(string.IsNullOrWhiteSpace(app.Entry.Bridge.Contract));
        Assert.NotNull(app.Package);
        Assert.Equal("converted", app.Package.Status);
        Assert.Empty(app.Package.Gaps);
        Assert.Equal(11, app.Package.Assemblies.Length);
        Assert.Contains(app.Package.Assemblies, a => a.Name == "TestApp");
        Assert.All(app.Package.Assemblies, a => Assert.Equal("mixed", a.Artifact));
        Assert.Contains(app.Evidence, e => e.StartsWith("local:", StringComparison.Ordinal));
        Assert.Contains(app.Evidence, e => e.Contains("7b1c88186a0de72a7ca72282eec40f05ee37e1737a8b4784c99c92eb04938892"));
        Assert.Contains("unverified", app.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
        CatalogValidator.ValidateDescriptor(app);
        Assert.False(Assert.Single(registry.Menu(), m => m.Id == "testapp-joint").IsLaunchable);
        Assert.Equal(140, registry.Count);
    }

    [Fact]
    public void JointAvailableRecordRoundTripsUnverified()
    {
        // The exact acceptance shape for the future joint record: mixed
        // capabilities, typed bridge, gapless converted mixed package,
        // hash-pinned local evidence, empty gaps, runtime explicitly
        // unverified. Survives registry serialization and validation.
        var staged = JointDescriptor() with
        {
            Availability = AppAvailability.Available,
            AvailabilityNote = "Staged locally at 'inputs/mixed/fixture-joint' (sha aabbccddeeff…); runtime unverified.",
            Evidence = ["local:inputs/mixed/fixture-joint#aabbccddeeffaabbccddeeffaabbccddeeffaabbccddeeffaabbccddeeff0011"],
            Gaps = [],
        };
        var registry = CatalogSerializer.Deserialize(CatalogSerializer.Serialize([staged]));
        var app = Assert.Single(registry.Apps);
        Assert.Equal(AppCapability.Mixed, app.Capabilities);
        Assert.NotNull(app.Entry.Bridge);
        Assert.Equal(AppAvailability.Available, app.Availability);
        Assert.Empty(app.Gaps);
        Assert.Contains("unverified", app.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
        CatalogValidator.ValidateDescriptor(app);
    }
}
