using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Guards the committed benchmark baseline (benchmarks/baselines/latest.json):
/// schema version, full demo coverage and sane metrics. Fails when a new demo
/// lands without a regenerated baseline (run benchmarks/run.ps1 -UpdateBaseline).
/// </summary>
[ResetAmbientState]
public sealed class BaselineTests
{
    private static readonly string[] ExpectedSlugs =
    [
        "hello-xaml", "controls", "typography", "layout", "input",
        "interop", "calc-keypad", "binding", "scroll", "listbox", "treeview",
        "datagrid", "menu", "dialogs", "resources",
    ];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "DesktopWasm.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Repo root (DesktopWasm.slnx) not found.");
        return dir.FullName;
    }

    private static JsonDocument LoadBaseline()
    {
        var path = System.IO.Path.Combine(RepoRoot(), "benchmarks", "baselines", "latest.json");
        if (!File.Exists(path))
            throw new InvalidOperationException($"Committed baseline missing: {path} (run benchmarks/run.ps1 -UpdateBaseline).");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void Baseline_covers_every_demo_with_sane_metrics()
    {
        using var doc = LoadBaseline();
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var demos = root.GetProperty("demos");
        foreach (var slug in ExpectedSlugs)
        {
            Assert.True(demos.TryGetProperty(slug, out var entry), $"Baseline missing demo: {slug}.");
            Assert.True(entry.GetProperty("loadMs").GetDouble() >= 0, slug);
            Assert.True(entry.GetProperty("fps").GetDouble() > 0, slug);
            Assert.True(entry.GetProperty("dataLen").GetInt64() > 20000, slug);
        }
        var interactions = root.GetProperty("interactions");
        foreach (var key in new[] { "menuOpen", "menuInvoke", "dialogOpen", "dialogAccept" })
            Assert.True(interactions.GetProperty(key).GetBoolean(), key);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("errors").ValueKind);
        Assert.Empty(root.GetProperty("errors").EnumerateArray());
        var env = root.GetProperty("environment");
        Assert.Equal(1280, env.GetProperty("viewport")[0].GetInt32());
        Assert.Equal(800, env.GetProperty("viewport")[1].GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(env.GetProperty("demoTransport").GetString()));
        var app = root.GetProperty("app");
        Assert.Matches("^[0-9a-f]{64}$", app.GetProperty("sha256").GetString()!);
        Assert.True(root.TryGetProperty("gpuSpike", out var spike));
        if (spike.ValueKind != JsonValueKind.Null)
            Assert.False(string.IsNullOrWhiteSpace(spike.GetProperty("backendBadge").GetString()));
    }
}
