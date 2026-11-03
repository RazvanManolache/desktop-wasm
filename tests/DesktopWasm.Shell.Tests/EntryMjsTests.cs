using System.Text.RegularExpressions;
using DesktopWasm.Host.Server;
using Xunit;

namespace DesktopWasm.Shell.Tests;

/// <summary>Guards the mount-prefix contract between the staged
/// BrowserHost entry module and the shell-owned WPF runtime shim.
/// entry.mjs must boot the runtime branch for /wpf/-prefixed routes with the
/// same asset set the shim serves; if the two drift, the shim is no longer a
/// proven equivalent and must not be removed.</summary>
public sealed class EntryMjsTests
{
    private static string EntrySource()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "DesktopWasm.slnx")))
                return File.ReadAllText(
                    Path.Combine(dir, "src", "DesktopWasm.BrowserHost", "wwwroot", "entry.mjs"));
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new InvalidOperationException("DesktopWasm.slnx not found above test binaries.");
    }

    private static HashSet<string> EntryAssets(string source)
    {
        var assets = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(source, @"(?:script|import)\('(\./)?([^']+\.(?:mjs|js))'\)"))
            assets.Add(m.Groups[2].Value);
        return assets;
    }

    private static IReadOnlySet<string> ShimAssets()
    {
        string shim = ShellApp.RuntimeShimHtml();
        var assets = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(shim, @"/wpf/([A-Za-z0-9_./-]+\.(?:mjs|js))"))
            assets.Add(m.Groups[1].Value);
        return assets;
    }

    [Fact]
    public void Entry_routes_prefixed_runtime_and_demo_to_runtime_branch()
    {
        string source = EntrySource();
        Assert.Contains("'/wpf'", source);
        Assert.Contains("/runtime/", source);
        Assert.Contains("/demo/", source);
        Assert.Contains("runtime-channel.mjs", source);
        Assert.Contains("studio.mjs", source);
    }

    [Fact]
    public void Entry_asset_set_matches_shell_runtime_shim()
    {
        // studio.mjs is the library branch, which the shim never serves;
        // every runtime asset the shim pins must load through entry.mjs.
        var entry = EntryAssets(EntrySource());
        entry.Remove("studio.mjs");
        var shim = ShimAssets();
        Assert.NotEmpty(entry);
        Assert.Equal(shim, entry);
    }

    [Fact]
    public void Entry_uses_relative_assets_so_base_href_scopes_them()
    {
        foreach (Match m in Regex.Matches(EntrySource(), @"(?:script|import)\('([^']+)'\)"))
            Assert.False(m.Groups[1].Value.StartsWith('/'),
                $"asset '{m.Groups[1].Value}' must stay relative so / and /wpf/ bases both work");
    }
}
