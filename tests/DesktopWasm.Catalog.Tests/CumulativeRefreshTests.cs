using System.Text.Json;
using DesktopWasm.AppModel;
using DesktopWasm.Catalog;
using DesktopWasm.Catalog.Tool;

namespace DesktopWasm.Catalog.Tests;

/// <summary>RV1745-CATFIX: a partial receipts batch must never silently
/// demote still-present earlier staging, while truly removed inputs and
/// invalid receipts still fail or demote honestly. Synthetic slugs only;
/// temp directories only; never the live catalog or frozen originals.</summary>
public sealed class CumulativeRefreshTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "catfix-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
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

    private string StageDir(string id)
    {
        var path = Path.Combine(_dir, "staging", id);
        Directory.CreateDirectory(path);
        return path;
    }

    private AppRegistry ReadLive() =>
        CatalogSerializer.Deserialize(File.ReadAllText(Path.Combine(_dir, "apps.json")));

    [Fact]
    public void PartialBatchPreservesVerifiedEarlierStaging()
    {
        var kept = StageDir("fixture-kept");
        var added = StageDir("fixture-added");
        WriteRegistry(
            Staged("fixture-kept", kept, new string('e', 64)),
            Fx.Descriptor("fixture-added"));
        var batch = WriteReceipts(("fixture-added", added, new string('f', 64)));

        var rc = Importer.Refresh(_dir, new[] { batch });

        Assert.Equal(0, rc);
        var apps = ReadLive().Apps.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(AppAvailability.Available, apps["fixture-kept"].Availability);
        Assert.Equal(AppAvailability.Available, apps["fixture-added"].Availability);
        Assert.Single(apps["fixture-kept"].Evidence, e => e.StartsWith("local:", StringComparison.Ordinal));
    }

    [Fact]
    public void RemovedStagedPathDemotesHonestly()
    {
        var gone = Path.Combine(_dir, "staging", "fixture-gone"); // never created
        var added = StageDir("fixture-added");
        WriteRegistry(
            Staged("fixture-gone", gone, new string('e', 64)),
            Fx.Descriptor("fixture-added"));
        var batch = WriteReceipts(("fixture-added", added, new string('f', 64)));

        Importer.Refresh(_dir, new[] { batch });

        var apps = ReadLive().Apps.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(AppAvailability.MissingInput, apps["fixture-gone"].Availability);
        Assert.NotEmpty(apps["fixture-gone"].Gaps);
        Assert.Equal(AppAvailability.Available, apps["fixture-added"].Availability);
    }

    [Fact]
    public void InvalidReceiptAbortsWithoutWriting()
    {
        var appsPath = WriteRegistry(Fx.Descriptor("fixture-added"));
        var before = File.ReadAllText(appsPath);
        var bad = WriteReceipts(("fixture-added", "", "")); // empty path+sha

        var ex = Assert.Throws<InvalidDataException>(() => Importer.Refresh(_dir, new[] { bad }));
        Assert.Contains("id/localPath/sha256", ex.Message);
        Assert.Equal(before, File.ReadAllText(appsPath));
    }

    [Fact]
    public void ReRefreshIsIdempotentNoDupEvidence()
    {
        var kept = StageDir("fixture-kept");
        WriteRegistry(Staged("fixture-kept", kept, new string('e', 64)));
        var batch = WriteReceipts(("fixture-other-missing", StageDir("fixture-other-missing"), new string('f', 64)));

        Importer.Refresh(_dir, new[] { batch });
        var once = File.ReadAllText(Path.Combine(_dir, "apps.json"));
        Importer.Refresh(_dir, new[] { batch });
        var twice = File.ReadAllText(Path.Combine(_dir, "apps.json"));

        Assert.Equal(once, twice);
        var app = ReadLive().Apps.Single(a => a.Id == "fixture-kept");
        Assert.Equal(AppAvailability.Available, app.Availability);
        Assert.Single(app.Evidence, e => e.StartsWith("local:", StringComparison.Ordinal));
    }

    [Fact]
    public void PilotStaysUnavailableThroughCarry()
    {
        var added = StageDir("fixture-added");
        WriteRegistry(LegacyAdapters.MixedPilot(), Fx.Descriptor("fixture-added"));
        var pilotReceipt = WriteReceipts(
            ("mixed-session-pilot", added, new string('f', 64)),
            ("fixture-added", added, new string('f', 64)));

        Importer.Refresh(_dir, new[] { pilotReceipt });

        var apps = ReadLive().Apps.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(AppAvailability.Unavailable, apps["mixed-session-pilot"].Availability);
        Assert.NotEmpty(apps["mixed-session-pilot"].Gaps);
    }
}
