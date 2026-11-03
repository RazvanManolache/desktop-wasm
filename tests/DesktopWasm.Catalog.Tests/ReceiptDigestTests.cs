using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.AppModel;
using DesktopWasm.Catalog;
using DesktopWasm.Catalog.Tool;

namespace DesktopWasm.Catalog.Tests;

/// <summary>RVW2100 digest-trust: Refresh must compare current bytes to the
/// declared digest, not just check nonempty/path-exists. Files pin content
/// bytes, manifest directories pin the manifest digest plus manifest-claimed
/// closure, manifest-less directories keep asserted existence-only
/// provenance. Synthetic slugs only; temp directories only; never the live
/// catalog or frozen originals.</summary>
public sealed class ReceiptDigestTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "digest-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static string ShaOfString(string text) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    private static string ShaOfFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static AppDescriptor Staged(string id, string path, string sha) =>
        Fx.Available(id) with
        {
            Evidence = [$"local:{path}#{sha}"],
            AvailabilityNote = $"Staged locally at '{path}' (sha {sha[..12]}…); runtime unverified.",
        };

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
        File.WriteAllText(path, JsonSerializer.Serialize(receipts.Select(r =>
            new { id = r.Id, localPath = r.Path, sha256 = r.Sha, stagedUtc = "2026-10-30T00:00:00Z" })));
        return path;
    }

    private string StageFile(string id, string content)
    {
        var dir = Path.Combine(_dir, "staging");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, id + ".bin");
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A hello-real-like manifest directory: app-manifest.json plus
    /// its manifest-claimed closure file. Returns (dir, manifestSha).</summary>
    private (string Dir, string ManifestSha) StageManifestDir(string id, string payload = "closure-bytes")
    {
        var dir = Path.Combine(_dir, "staging", id);
        Directory.CreateDirectory(dir);
        var binName = id + ".bin";
        File.WriteAllText(Path.Combine(dir, binName), payload);
        var binSha = ShaOfFile(Path.Combine(dir, binName));
        File.WriteAllText(Path.Combine(dir, "app-manifest.json"),
            JsonSerializer.Serialize(new
            {
                slug = id,
                assemblies = new[] { new { file = binName, sha256 = binSha } },
            }));
        return (dir, ShaOfFile(Path.Combine(dir, "app-manifest.json")));
    }

    private AppRegistry ReadLive() =>
        CatalogSerializer.Deserialize(File.ReadAllText(Path.Combine(_dir, "apps.json")));

    [Fact]
    public void FreshFileReceiptWithWrongDigestAbortsWithoutWriting()
    {
        var staged = StageFile("fixture-file", "current-bytes");
        var appsPath = WriteRegistry(Fx.Descriptor("fixture-file"));
        var before = File.ReadAllText(appsPath);
        var bad = WriteReceipts(("fixture-file", staged, ShaOfString("other-bytes")));

        var ex = Assert.Throws<InvalidDataException>(() => Importer.Refresh(_dir, new[] { bad }));
        Assert.Contains("digest mismatch", ex.Message);
        Assert.Equal(before, File.ReadAllText(appsPath));
    }

    [Fact]
    public void FreshManifestDirectoryWithStaleDigestAbortsWithoutWriting()
    {
        var (dir, _) = StageManifestDir("fixture-manifest");
        var appsPath = WriteRegistry(Fx.Descriptor("fixture-manifest"));
        var before = File.ReadAllText(appsPath);
        var stale = WriteReceipts(("fixture-manifest", dir, new string('a', 64)));

        var ex = Assert.Throws<InvalidDataException>(() => Importer.Refresh(_dir, new[] { stale }));
        Assert.Contains("digest mismatch", ex.Message);
        Assert.Equal(before, File.ReadAllText(appsPath));
    }

    [Fact]
    public void NonHexDigestIsNotVerification()
    {
        var staged = StageFile("fixture-file", "current-bytes");
        var appsPath = WriteRegistry(Fx.Descriptor("fixture-file"));
        var before = File.ReadAllText(appsPath);
        var bad = WriteReceipts(("fixture-file", staged, "not-a-hash"));

        var ex = Assert.Throws<InvalidDataException>(() => Importer.Refresh(_dir, new[] { bad }));
        Assert.Contains("64 hex", ex.Message);
        Assert.Equal(before, File.ReadAllText(appsPath));
    }

    [Fact]
    public void ChangedFileBytesDemoteCarriedStagingHonestly()
    {
        var staged = StageFile("fixture-kept", "original-bytes");
        WriteRegistry(Staged("fixture-kept", staged, ShaOfFile(staged)));
        File.WriteAllText(staged, "changed-bytes");
        var other = StageFile("fixture-other", "other-bytes");
        var batch = WriteReceipts(("fixture-other", other, ShaOfFile(other)));

        Importer.Refresh(_dir, new[] { batch });

        var apps = ReadLive().Apps.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(AppAvailability.MissingInput, apps["fixture-kept"].Availability);
        Assert.NotEmpty(apps["fixture-kept"].Gaps);
    }

    [Fact]
    public void ChangedManifestBytesDemoteCarriedStagingHonestly()
    {
        var (dir, manifestSha) = StageManifestDir("fixture-kept");
        WriteRegistry(Staged("fixture-kept", dir, manifestSha));
        File.WriteAllText(Path.Combine(dir, "app-manifest.json"), """{"assemblies":[]}""");
        var (otherDir, otherSha) = StageManifestDir("fixture-other");
        var batch = WriteReceipts(("fixture-other", otherDir, otherSha));

        Importer.Refresh(_dir, new[] { batch });

        var apps = ReadLive().Apps.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(AppAvailability.MissingInput, apps["fixture-kept"].Availability);
        Assert.NotEmpty(apps["fixture-kept"].Gaps);
    }

    [Fact]
    public void ChangedClosureResourceDemotesCarriedStaging()
    {
        var (dir, manifestSha) = StageManifestDir("fixture-kept");
        WriteRegistry(Staged("fixture-kept", dir, manifestSha));
        File.WriteAllText(Path.Combine(dir, "fixture-kept.bin"), "tampered-closure");
        var (otherDir, otherSha) = StageManifestDir("fixture-other");
        var batch = WriteReceipts(("fixture-other", otherDir, otherSha));

        Importer.Refresh(_dir, new[] { batch });

        var apps = ReadLive().Apps.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(AppAvailability.MissingInput, apps["fixture-kept"].Availability);
        Assert.NotEmpty(apps["fixture-kept"].Gaps);
    }

    [Fact]
    public void ValidManifestDirectoryAndClosureStayAvailable()
    {
        var (dir, manifestSha) = StageManifestDir("fixture-manifest");
        WriteRegistry(Fx.Descriptor("fixture-manifest"));
        var batch = WriteReceipts(("fixture-manifest", dir, manifestSha));

        var rc = Importer.Refresh(_dir, new[] { batch });

        Assert.Equal(0, rc);
        var app = ReadLive().Apps.Single(a => a.Id == "fixture-manifest");
        Assert.Equal(AppAvailability.Available, app.Availability);
        Assert.Empty(app.Gaps);
    }

    [Fact]
    public void ValidFileReceiptPromotesAndCarriesWhileBytesMatch()
    {
        var staged = StageFile("fixture-file", "pinned-bytes");
        WriteRegistry(Fx.Descriptor("fixture-file"), Fx.Descriptor("fixture-other"));
        var batch = WriteReceipts(("fixture-file", staged, ShaOfFile(staged)));

        Importer.Refresh(_dir, new[] { batch });
        var other = StageFile("fixture-other", "other-bytes");
        Importer.Refresh(_dir, new[] { WriteReceipts(("fixture-other", other, ShaOfFile(other))) });

        var apps = ReadLive().Apps.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(AppAvailability.Available, apps["fixture-file"].Availability);
        Assert.Equal(AppAvailability.Available, apps["fixture-other"].Availability);
    }

    [Fact]
    public void AssertedDirectoryWithoutManifestCarriesOnExistence()
    {
        // paint/fiddler contract: a manifest-less staged directory carries
        // asserted import-lane provenance (existence only); its pin is not a
        // verifiable digest and must keep working without redefinition.
        var dir = Path.Combine(_dir, "staging", "fixture-tree");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "payload.dll"), "tree-bytes");
        WriteRegistry(Staged("fixture-tree", dir, new string('c', 64)));
        var other = StageFile("fixture-other", "other-bytes");
        var batch = WriteReceipts(("fixture-other", other, ShaOfFile(other)));

        Importer.Refresh(_dir, new[] { batch });

        var apps = ReadLive().Apps.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(AppAvailability.Available, apps["fixture-tree"].Availability);
    }
}
