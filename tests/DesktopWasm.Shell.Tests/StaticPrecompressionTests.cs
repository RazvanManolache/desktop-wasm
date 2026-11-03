using System.IO.Compression;
using System.Net;
using System.Text;
using DesktopWasm.Host.Server;
using Xunit;

namespace DesktopWasm.Shell.Tests;

public sealed class StaticPrecompressionTests
{
    private static string CreateWpfTree(out string wpfRoot, out string catalogPath)
    {
        string dir = Path.Combine(Path.GetTempPath(), "dwprecompress-" + Guid.NewGuid().ToString("N"));
        wpfRoot = Path.Combine(dir, "wpfroot");
        Directory.CreateDirectory(wpfRoot);
        Directory.CreateDirectory(Path.Combine(wpfRoot, "_framework"));
        Directory.CreateDirectory(Path.Combine(wpfRoot, "apps", "testapp"));

        File.WriteAllText(Path.Combine(wpfRoot, "index.html"), "<!doctype html><html></html>");
        File.WriteAllText(Path.Combine(wpfRoot, "_framework", "blazor.boot.json"), """{"resources":{}}""");
        File.WriteAllText(Path.Combine(wpfRoot, "_framework", "blazor.webassembly.js"), "// loader");
        File.WriteAllText(Path.Combine(wpfRoot, "entry.mjs"), "export default {};");

        // Staged wasm payload
        string raw = "WASMPAYLOAD_1234567890_UNCOMPRESSED_DATA_FOR_PRECOMPRESSION_TESTING";
        byte[] rawBytes = Encoding.UTF8.GetBytes(raw);
        string wasmPath = Path.Combine(wpfRoot, "_framework", "sample.wasm");
        File.WriteAllBytes(wasmPath, rawBytes);

        // Pre-compressed .br sibling
        using (var brStream = new FileStream(wasmPath + ".br", FileMode.Create, FileAccess.Write))
        using (var compressor = new BrotliStream(brStream, CompressionLevel.Optimal))
        {
            compressor.Write(rawBytes, 0, rawBytes.Length);
        }

        // Pre-compressed .gz sibling
        using (var gzStream = new FileStream(wasmPath + ".gz", FileMode.Create, FileAccess.Write))
        using (var compressor = new GZipStream(gzStream, CompressionLevel.Optimal))
        {
            compressor.Write(rawBytes, 0, rawBytes.Length);
        }

        catalogPath = Path.Combine(dir, "apps.json");
        File.WriteAllText(catalogPath, ShellHost.FixtureCatalog());
        return dir;
    }

    [Fact]
    public async Task Wpf_precompressed_br_asset_served_when_client_accepts_br()
    {
        string dir = CreateWpfTree(out string wpf, out string catalog);
        var shell = ShellApp.Create(new ShellOptions(
            CatalogPath: catalog,
            PackagesRoot: Path.Combine(dir, "packages"),
            LogDir: Path.Combine(dir, "logs"),
            ContentRootPath: dir,
            WebRootPath: wpf,
            WinFormsBackendUrl: "http://127.0.0.1:1",
            WpfWebRootPath: wpf));
        shell.Urls.Clear();
        shell.Urls.Add("http://127.0.0.1:0");
        await shell.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(shell.Urls.First()) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "wpf/_framework/sample.wasm");
            request.Headers.AcceptEncoding.ParseAdd("br, gzip");

            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/wasm", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("br", response.Content.Headers.ContentEncoding);
            Assert.Contains("max-age=31536000", response.Headers.CacheControl?.ToString() ?? "");
            Assert.Contains("immutable", response.Headers.CacheControl?.ToString() ?? "");

            // Verify content decodes back to original bytes
            byte[] compressedBytes = await response.Content.ReadAsByteArrayAsync();
            using var decompressor = new BrotliStream(new MemoryStream(compressedBytes), CompressionMode.Decompress);
            using var reader = new StreamReader(decompressor, Encoding.UTF8);
            string decompressed = await reader.ReadToEndAsync();
            Assert.Equal("WASMPAYLOAD_1234567890_UNCOMPRESSED_DATA_FOR_PRECOMPRESSION_TESTING", decompressed);
        }
        finally
        {
            await shell.DisposeAsync();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Wpf_precompressed_gz_asset_served_when_client_accepts_gzip_only()
    {
        string dir = CreateWpfTree(out string wpf, out string catalog);
        var shell = ShellApp.Create(new ShellOptions(
            CatalogPath: catalog,
            PackagesRoot: Path.Combine(dir, "packages"),
            LogDir: Path.Combine(dir, "logs"),
            ContentRootPath: dir,
            WebRootPath: wpf,
            WinFormsBackendUrl: "http://127.0.0.1:1",
            WpfWebRootPath: wpf));
        shell.Urls.Clear();
        shell.Urls.Add("http://127.0.0.1:0");
        await shell.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(shell.Urls.First()) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "wpf/_framework/sample.wasm");
            request.Headers.AcceptEncoding.ParseAdd("gzip");

            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/wasm", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("gzip", response.Content.Headers.ContentEncoding);

            // Verify content decodes back to original bytes
            byte[] compressedBytes = await response.Content.ReadAsByteArrayAsync();
            using var decompressor = new GZipStream(new MemoryStream(compressedBytes), CompressionMode.Decompress);
            using var reader = new StreamReader(decompressor, Encoding.UTF8);
            string decompressed = await reader.ReadToEndAsync();
            Assert.Equal("WASMPAYLOAD_1234567890_UNCOMPRESSED_DATA_FOR_PRECOMPRESSION_TESTING", decompressed);
        }
        finally
        {
            await shell.DisposeAsync();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Wpf_uncompressed_asset_served_when_no_accept_encoding_matches()
    {
        string dir = CreateWpfTree(out string wpf, out string catalog);
        var shell = ShellApp.Create(new ShellOptions(
            CatalogPath: catalog,
            PackagesRoot: Path.Combine(dir, "packages"),
            LogDir: Path.Combine(dir, "logs"),
            ContentRootPath: dir,
            WebRootPath: wpf,
            WinFormsBackendUrl: "http://127.0.0.1:1",
            WpfWebRootPath: wpf));
        shell.Urls.Clear();
        shell.Urls.Add("http://127.0.0.1:0");
        await shell.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(shell.Urls.First()) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "wpf/_framework/sample.wasm");
            // No Accept-Encoding header

            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/wasm", response.Content.Headers.ContentType?.MediaType);
            Assert.Empty(response.Content.Headers.ContentEncoding);

            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal("WASMPAYLOAD_1234567890_UNCOMPRESSED_DATA_FOR_PRECOMPRESSION_TESTING", body);
        }
        finally
        {
            await shell.DisposeAsync();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
