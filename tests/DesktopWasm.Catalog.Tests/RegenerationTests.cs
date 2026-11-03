using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.AppModel;
using DesktopWasm.Catalog;
using DesktopWasm.Catalog.Tool;

namespace DesktopWasm.Catalog.Tests;

/// <summary>RVW0900-CATALOG: pure-source import regeneration must not drop the
/// producer-owned joint record or digest-verified staging. Synthetic slugs and
/// temp directories only; never the live catalog or frozen originals.</summary>
public sealed class RegenerationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "regen-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static string ShaOfFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ShaOfString(string text) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    /// <summary>Synthetic producer package dir: manifest + matching assembly
    /// bytes + joint receipt with a typed bridge. Temp only.</summary>
    private string StageProducerPackage(string entry = "FixtureJoint", string? bridgeProvider = "Fixture.Resolver", string? bridgeContract = "Fixture.Contract")
    {
        var dir = Path.Combine(_dir, "testapp-package");
        Directory.CreateDirectory(dir);
        var payloadA = "assembly-bytes-a";
        var payloadB = "assembly-bytes-b";
        File.WriteAllText(Path.Combine(dir, entry + ".aaaaaaaa.bin"), payloadA);
        File.WriteAllText(Path.Combine(dir, "FixtureDock.bbbbbbbb.bin"), payloadB);
        var manifest = new
        {
            schemaVersion = 1,
            slug = "testapp-joint",
            mapVersion = "wpf-convert-v1",
            status = "converted",
            entryAssembly = entry,
            assemblies = new[]
            {
                new { name = entry, file = entry + ".aaaaaaaa.bin", sha256 = ShaOfString(payloadA), references = new[] { "System.Runtime" }, inputSha256 = ShaOfString("input-a") },
                new { name = "FixtureDock", file = "FixtureDock.bbbbbbbb.bin", sha256 = ShaOfString(payloadB), references = new[] { "System.Runtime" }, inputSha256 = ShaOfString("input-b") },
            },
            gaps = Array.Empty<object>(),
            sourceCommit = (string?)null,
        };
        File.WriteAllText(Path.Combine(dir, "app-manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        var receipt = new
        {
            slug = "testapp-joint",
            entryAssembly = entry,
            mapVersion = "wpf-convert-v1",
            builtAtUtc = "2026-10-31T00:00:00Z",
            status = "converted",
            bridgeProvider = bridgeProvider is null ? null : new
            {
                assembly = "FixtureBridge",
                resolver = bridgeProvider,
                hostType = bridgeContract,
            },
            gaps = Array.Empty<object>(),
        };
        File.WriteAllText(Path.Combine(dir, "testapp-joint.receipt.json"),
            JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        return dir;
    }

    private static AppDescriptor Missing(string id, AppCapability caps = AppCapability.Wpf) =>
        Fx.Descriptor(id, capabilities: caps);

    private static AppDescriptor AvailableWithFile(string id, string path, string sha, AppCapability caps = AppCapability.Wpf) =>
        Fx.Descriptor(id, capabilities: caps, availability: AppAvailability.Available,
            availabilityNote: "staged", gaps: []) with
        {
            Evidence = [$"local:{path}#{sha}"],
        };

    [Fact]
    public void ProducerJointRebuildsStagedWithBridge()
    {
        var dir = StageProducerPackage();

        var joint = Importer.TryBuildJointFromPackageDir(dir, dir);

        Assert.NotNull(joint);
        Assert.Equal("testapp-joint", joint.Id);
        Assert.Equal(AppCapability.Mixed, joint.Capabilities);
        Assert.Equal(AppAvailability.Staged, joint.Availability);
        Assert.NotEqual(AppAvailability.Available, joint.Availability);
        Assert.Empty(joint.Gaps);
        Assert.Equal("FixtureJoint", joint.Entry.EntryAssembly);
        Assert.Equal("testapp-joint", joint.Entry.ManifestSlug);
        Assert.NotNull(joint.Entry.Bridge);
        Assert.Equal("Fixture.Resolver", joint.Entry.Bridge.Provider);
        Assert.Equal("Fixture.Contract", joint.Entry.Bridge.Contract);
        Assert.NotNull(joint.Package);
        Assert.Equal("converted", joint.Package.Status);
        Assert.Empty(joint.Package.Gaps);
        Assert.All(joint.Package.Assemblies, a => Assert.Equal("mixed", a.Artifact));
        Assert.Contains(joint.Evidence, e => e.StartsWith("local:", StringComparison.Ordinal));
        Assert.Contains("unverified", joint.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
        CatalogValidator.ValidateDescriptor(joint);
        Assert.False(new AppRegistry([joint]).Menu().Single().IsLaunchable);
    }

    [Fact]
    public void ProducerJointWithBrokenClosureThrows()
    {
        var dir = StageProducerPackage();
        File.WriteAllText(Path.Combine(dir, "FixtureDock.bbbbbbbb.bin"), "tampered-bytes");

        Assert.Throws<InvalidDataException>(() => Importer.TryBuildJointFromPackageDir(dir, dir));
    }

    [Fact]
    public void ProducerJointWithoutBridgeThrows()
    {
        var dir = StageProducerPackage(bridgeProvider: null);

        Assert.Throws<InvalidDataException>(() => Importer.TryBuildJointFromPackageDir(dir, dir));
    }

    [Fact]
    public void MissingProducerYieldsNull()
    {
        Assert.Null(Importer.TryBuildJointFromPackageDir(
            Path.Combine(_dir, "no-such-package"), "inputs/mixed/testapp-package"));
    }

    [Fact]
    public void RegenPreservesVerifiedAvailableAndDemotesStale()
    {
        var staging = Path.Combine(_dir, "staging");
        Directory.CreateDirectory(staging);
        var kept = Path.Combine(staging, "kept.bin");
        File.WriteAllText(kept, "kept-bytes");
        var keptSha = ShaOfFile(kept);
        var stale = Path.Combine(staging, "stale.bin");
        File.WriteAllText(stale, "changed-bytes");
        var fresh = new List<AppDescriptor>
        {
            Missing("fixture-kept") with { Evidence = ["producer:generation pin fixture-kept"] },
            Missing("fixture-stale"),
        };
        var prior = new List<AppDescriptor>
        {
            AvailableWithFile("fixture-kept", kept, keptSha) with
            {
                Evidence = ["producer:generation pin fixture-kept", $"local:{kept}#{keptSha}"],
            },
            AvailableWithFile("fixture-stale", stale, new string('e', 64)),
        };

        var result = Importer.ReconcileWithPrior(
            fresh, prior, p => Directory.Exists(p) || File.Exists(p))
            .ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(AppAvailability.Available, result["fixture-kept"].Availability);
        Assert.Empty(result["fixture-kept"].Gaps);
        Assert.Contains(result["fixture-kept"].Evidence,
            e => e == "producer:generation pin fixture-kept");
        Assert.Equal(AppAvailability.MissingInput, result["fixture-stale"].Availability);
        Assert.NotEmpty(result["fixture-stale"].Gaps);
        Assert.DoesNotContain(result["fixture-stale"].Evidence,
            e => e.StartsWith("local:", StringComparison.Ordinal));
    }

    [Fact]
    public void RegenCarriesVerifiedStagedJointFromPriorWithoutProducer()
    {
        var staging = Path.Combine(_dir, "staging", "testapp-joint");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "app-manifest.json"), """{"assemblies": []}""");
        var manifestSha = ShaOfFile(Path.Combine(staging, "app-manifest.json"));
        var jointPackage = new PackageIdentity(1, "wpf-convert-v1", "converted",
            [new PackageAssembly("FixtureJoint", "FixtureJoint.aaaaaaaa.bin",
                ShaOfString("a"), ["System.Runtime"], Artifact: "mixed")],
            [], null);
        var priorJoint = Fx.Descriptor("testapp-joint",
            aliases: ["/app/testapp-joint", "/demo/testapp-joint"],
            capabilities: AppCapability.Mixed,
            availability: AppAvailability.Staged,
            availabilityNote: "Staged locally at 'inputs/mixed/testapp-package' (sha aabbccddeeff…); serving + behavior unverified.",
            gaps: [],
            entry: new AppEntry("converted-app", "FixtureJoint", "testapp-joint", null,
                new AppBridge("Fixture.Resolver", "Fixture.Contract")),
            package: jointPackage,
            transformVersion: "wpf-convert-v1") with
        {
            Evidence = [$"local:{staging}#{manifestSha}"],
        };

        var result = Importer.ReconcileWithPrior(
            [Fx.Descriptor("fixture-other")], [priorJoint, Fx.Descriptor("fixture-other")],
            p => Directory.Exists(p) || File.Exists(p));

        var joint = Assert.Single(result, a => a.Id == "testapp-joint");
        Assert.Equal(AppAvailability.Staged, joint.Availability);
        Assert.Empty(joint.Gaps);
        CatalogValidator.ValidateDescriptor(joint);
    }

    [Fact]
    public void RegenNeverPromotesPilotAndDropsUnknownPriorIds()
    {
        var staging = Path.Combine(_dir, "staging");
        Directory.CreateDirectory(staging);
        var added = Path.Combine(staging, "added");
        Directory.CreateDirectory(added);
        var fresh = new List<AppDescriptor> { LegacyAdapters.MixedPilot(), Missing("fixture-added") };
        var prior = new List<AppDescriptor>
        {
            LegacyAdapters.MixedPilot(),
            AvailableWithFile("fixture-ghost", added, new string('e', 64)),
        };
        var batch = Path.Combine(_dir, "receipts.json");
        File.WriteAllText(batch, JsonSerializer.Serialize(new[]
        {
            new { id = "mixed-session-pilot", localPath = added, sha256 = new string('f', 64), stagedUtc = "2026-10-31T00:00:00Z" },
        }));

        var result = Importer.ReconcileWithPrior(
            fresh, prior, p => Directory.Exists(p) || File.Exists(p));

        Assert.DoesNotContain(result, a => a.Id == "fixture-ghost");
        Assert.Equal(AppAvailability.Unavailable,
            Assert.Single(result, a => a.Id == "mixed-session-pilot").Availability);
    }
}
