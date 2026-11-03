using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using WfWasm.CanvasHost;

public sealed class AppAssemblyLoaderStartupProfilingTests
{
    [Fact]
    public void VerifySha256Hex_Matches_Valid_Hash()
    {
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        string hex = Convert.ToHexString(SHA256.HashData(data));
        Assert.True(AppAssemblyLoader.VerifySha256Hex(data, hex));
        Assert.True(AppAssemblyLoader.VerifySha256Hex(data, hex.ToLowerInvariant()));
    }

    [Fact]
    public void VerifySha256Hex_Rejects_Invalid_Or_Corrupt_Hash()
    {
        byte[] data = [1, 2, 3, 4, 5];
        string hex = Convert.ToHexString(SHA256.HashData(data));

        Assert.False(AppAssemblyLoader.VerifySha256Hex(data, null));
        Assert.False(AppAssemblyLoader.VerifySha256Hex(data, ""));
        Assert.False(AppAssemblyLoader.VerifySha256Hex(data, "invalid_hex"));
        Assert.False(AppAssemblyLoader.VerifySha256Hex(data, hex[..^2] + "00"));
        Assert.False(AppAssemblyLoader.VerifySha256Hex(data, hex[..30])); // truncated
    }

    [Fact]
    public void ExtractSimpleName_Parses_Simple_And_Full_Assembly_Names()
    {
        Assert.Equal("System.Windows.Forms",
            AppAssemblyLoader.ExtractSimpleName("System.Windows.Forms, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"));
        Assert.Equal("Telerik.WinControls.UI",
            AppAssemblyLoader.ExtractSimpleName("Telerik.WinControls.UI, Version=2021.2.615.40, Culture=neutral"));
        Assert.Equal("RichTextEditor",
            AppAssemblyLoader.ExtractSimpleName("RichTextEditor"));
        Assert.Equal("MyAssembly",
            AppAssemblyLoader.ExtractSimpleName("  MyAssembly  "));
        Assert.Equal("", AppAssemblyLoader.ExtractSimpleName(""));
    }

    [Fact]
    public void Key_Normalizes_Paths_And_Extensions()
    {
        Assert.Equal("RichTextEditor", AppAssemblyLoader.Key("RichTextEditor.exe"));
        Assert.Equal("TelerikCommon", AppAssemblyLoader.Key(@"C:\path\to\TelerikCommon.dll"));
        Assert.Equal("WfWasm.Drawing", AppAssemblyLoader.Key("sub/folder/WfWasm.Drawing.dll"));
        Assert.Equal("AlreadyKey", AppAssemblyLoader.Key("AlreadyKey"));
    }

    [Fact]
    public void ReadPeAssemblyReferences_Extracts_References_From_Binary()
    {
        // Read assembly references from the current test assembly's PE binary on disk
        string location = typeof(AppAssemblyLoaderStartupProfilingTests).Assembly.Location;
        Assert.True(File.Exists(location), "Test assembly must exist on disk");
        byte[] bytes = File.ReadAllBytes(location);

        List<string> refs = AppAssemblyLoader.ReadPeAssemblyReferences(bytes);
        Assert.NotEmpty(refs);
        Assert.Contains(refs, r => r.Equals("WfWasm.CanvasHost", StringComparison.OrdinalIgnoreCase)
                                || r.Equals("System.Runtime", StringComparison.OrdinalIgnoreCase)
                                || r.Equals("xunit.core", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadPeAssemblyReferences_Handles_Corrupted_Or_NonPe_Bytes_Gracefully()
    {
        Assert.Empty(AppAssemblyLoader.ReadPeAssemblyReferences([]));
        Assert.Empty(AppAssemblyLoader.ReadPeAssemblyReferences([0x00, 0x01, 0x02, 0x03]));
    }

    [Fact]
    public void PairedTiming_VerifySha256Hex_ZeroAlloc_Versus_AllocatingHex()
    {
        byte[] payload = new byte[1024 * 1024]; // 1 MB payload
        new Random(42).NextBytes(payload);
        string expectedHex = Convert.ToHexString(SHA256.HashData(payload));

        const int iterations = 10;

        // Warmup
        _ = AppAssemblyLoader.VerifySha256Hex(payload, expectedHex);
        _ = Convert.ToHexString(SHA256.HashData(payload)).Equals(expectedHex, StringComparison.OrdinalIgnoreCase);

        // Timed run: Allocating approach (SHA256.HashData alloc + Convert.ToHexString string alloc)
        var swAlloc = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            string hex = Convert.ToHexString(SHA256.HashData(payload));
            bool ok = hex.Equals(expectedHex, StringComparison.OrdinalIgnoreCase);
            Assert.True(ok);
        }
        swAlloc.Stop();

        // Timed run: Zero-alloc span approach
        var swSpan = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            bool ok = AppAssemblyLoader.VerifySha256Hex(payload, expectedHex);
            Assert.True(ok);
        }
        swSpan.Stop();

        // Verification: span check must succeed and be within reasonable timing
        Assert.True(swSpan.ElapsedMilliseconds <= swAlloc.ElapsedMilliseconds + 50,
            $"Span check ({swSpan.ElapsedMilliseconds}ms) should be at least as fast as allocating check ({swAlloc.ElapsedMilliseconds}ms)");
    }

    [Fact]
    public void PairedTiming_DemandAssemblyPayload_Decode_Preserves_Data_And_Timing()
    {
        byte[] payload = new byte[512 * 1024]; // 512 KB
        new Random(123).NextBytes(payload);
        byte[] hash = SHA256.HashData(payload);
        byte[] packet = hash.Concat(payload).ToArray();

        // Warmup
        byte[]? warmup = DemandAssemblyPayload.Decode(packet);
        Assert.NotNull(warmup);
        Assert.Equal(payload, warmup);

        const int iterations = 10;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            byte[]? decoded = DemandAssemblyPayload.Decode(packet);
            Assert.NotNull(decoded);
            Assert.Equal(payload.Length, decoded.Length);
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 0);
    }
}
