extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;

namespace WfWasm.Drawing.Tests;

// FACTORY1615-WPF: Adam7 interlaced decode. Filtered rows below are
// per-pass (filter byte + pass-row bytes); pixel content follows a
// closed-form pattern R=(x*37)%256 G=(y*53)%256 B=((x+y)*29)%256 A=255
// recomputed in the assertions, so the oracle is the pattern itself,
// not the codec. FAIL until PngCodec routes interlace to PngAdam7.
public sealed class PngAdam7Tests
{
    // (xStart, yStart, xStep, yStep) per PNG spec.
    private static readonly (int, int, int, int)[] Passes =
    [
        (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4),
        (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2),
    ];

    private static int PatR(int x, int y) => (x * 37) % 256;
    private static int PatG(int x, int y) => (y * 53) % 256;
    private static int PatB(int x, int y) => ((x + y) * 29) % 256;

    [Fact]
    public void Interlaced1x1_SkipsEmptyPasses_Decodes()
    {
        // 1x1: only pass 0 carries a row; passes 1-6 are empty.
        byte[][] rows = [[0, 10, 20, 30, 40]];
        using var bitmap = DecodeInterlaced(BuildInterlaced(1, 1, 8, 6, rows));
        Assert.Equal(new WfDraw::System.Drawing.Size(1, 1), bitmap.Size);
        Assert.Equal(WfColor.FromArgb(40, 10, 20, 30).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Interlaced2x2_ScattersThreePasses()
    {
        // Pass 0 -> (0,0); pass 5 -> (1,0); pass 6 -> (0,1),(1,1).
        byte[][] rows =
        [
            [0, 11, 21, 31, 41],
            [0, 12, 22, 32, 42],
            [0, 13, 23, 33, 43, 14, 24, 34, 44],
        ];
        using var bitmap = DecodeInterlaced(BuildInterlaced(2, 2, 8, 6, rows));
        Assert.Equal(WfColor.FromArgb(41, 11, 21, 31).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(42, 12, 22, 32).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(43, 13, 23, 33).ToArgb(), bitmap.GetPixel(0, 1).ToArgb());
        Assert.Equal(WfColor.FromArgb(44, 14, 24, 34).ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void Interlaced8x8_Pattern_DecodesExact()
    {
        const int W = 8;
        const int H = 8;
        var rows = new List<byte[]>();
        foreach (var (xs, ys, xst, yst) in Passes)
        {
            int pw = xs < W ? (W - xs + xst - 1) / xst : 0;
            int ph = ys < H ? (H - ys + yst - 1) / yst : 0;
            for (int py = 0; py < ph; py++)
            {
                var row = new byte[1 + pw * 4];
                row[0] = 0;
                for (int px = 0; px < pw; px++)
                {
                    int x = xs + px * xst;
                    int y = ys + py * yst;
                    row[1 + px * 4] = (byte)PatR(x, y);
                    row[1 + px * 4 + 1] = (byte)PatG(x, y);
                    row[1 + px * 4 + 2] = (byte)PatB(x, y);
                    row[1 + px * 4 + 3] = 255;
                }
                rows.Add(row);
            }
        }
        using var bitmap = DecodeInterlaced(BuildInterlaced(W, H, 8, 6, rows.ToArray()));
        Assert.Equal(new WfDraw::System.Drawing.Size(W, H), bitmap.Size);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                Assert.Equal(WfColor.FromArgb(255, PatR(x, y), PatG(x, y), PatB(x, y)).ToArgb(),
                    bitmap.GetPixel(x, y).ToArgb());
    }

    [Fact]
    public void Interlaced_Palette1Bit_Decodes()
    {
        // 4x1 indexed 1-bit: pass 0 -> x0; pass 3 -> x2; pass 5 -> x1,x3.
        byte[] palette = [255, 0, 0, 0, 255, 0];
        byte[][] rows = [[0, 0x00], [0, 0x80], [0, 0x00]];
        using var bitmap = DecodeInterlaced(BuildInterlaced(4, 1, 1, 3, rows, palette));
        Assert.Equal(WfColor.FromArgb(255, 255, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 255, 0, 0).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 0, 255, 0).ToArgb(), bitmap.GetPixel(2, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 255, 0, 0).ToArgb(), bitmap.GetPixel(3, 0).ToArgb());
    }

    [Fact]
    public void Interlaced_SubFilter_UsesPassLocalHistory()
    {
        // Pass 5 row with Sub filter: history must restart at zero for
        // the pass, not continue from pass 0's row.
        byte[][] rows =
        [
            [0, 100, 0, 0, 255],
            [1, 10, 0, 0, 5],
        ];
        using var bitmap = DecodeInterlaced(BuildInterlaced(2, 1, 8, 6, rows));
        Assert.Equal(WfColor.FromArgb(255, 100, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(5, 10, 0, 0).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
    }

    [Fact]
    public void Interlaced16_TakesHighBytes()
    {
        byte[][] rows = [[0, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66]];
        using var bitmap = DecodeInterlaced(BuildInterlaced(1, 1, 16, 2, rows));
        Assert.Equal(WfColor.FromArgb(255, 0x11, 0x33, 0x55).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Interlaced_Truncated_ThrowsOutOfMemory()
    {
        byte[][] rows = [[0, 11, 21, 31, 41], [0, 12, 22, 32, 42]];
        byte[] full = BuildInterlaced(2, 1, 8, 6, rows);
        Assert.Throws<OutOfMemoryException>(() => DecodeInterlaced(full[..(full.Length / 2)]));
    }

    [Fact]
    public void InterlaceMethod2_ThrowsOutOfMemory()
    {
        byte[] png = BuildInterlaced(1, 1, 8, 6, [[0, 1, 2, 3, 4]], interlaceByte: 2);
        Assert.Throws<OutOfMemoryException>(() => DecodeInterlaced(png));
    }

    private static WfBitmap DecodeInterlaced(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new WfBitmap(stream);
    }

    private static byte[] BuildInterlaced(
        int width, int height, int bitDepth, int colorType, byte[][] passRows,
        byte[]? palette = null, byte interlaceByte = 1)
    {
        using var plain = new MemoryStream();
        foreach (byte[] row in passRows)
            plain.Write(row, 0, row.Length);
        plain.Position = 0;
        using var compressed = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            plain.CopyTo(deflate);
        byte[] idat = WrapZlib(compressed.ToArray(), passRows);

        var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10], 0, 8);
        byte[] ihdr =
        [
            (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
            (byte)bitDepth, (byte)colorType, 0, 0, interlaceByte,
        ];
        WriteChunk(png, "IHDR", ihdr);
        if (palette is not null)
            WriteChunk(png, "PLTE", palette);
        WriteChunk(png, "IDAT", idat);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] WrapZlib(byte[] deflate, byte[][] rows)
    {
        uint s1 = 1;
        uint s2 = 0;
        foreach (byte[] row in rows)
            foreach (byte b in row)
            {
                s1 = (s1 + b) % 65521;
                s2 = (s2 + s1) % 65521;
            }
        uint adler = (s2 << 16) | s1;
        var zlib = new byte[2 + deflate.Length + 4];
        zlib[0] = 0x78;
        zlib[1] = 0x01;
        Buffer.BlockCopy(deflate, 0, zlib, 2, deflate.Length);
        zlib[^4] = (byte)(adler >> 24);
        zlib[^3] = (byte)(adler >> 16);
        zlib[^2] = (byte)(adler >> 8);
        zlib[^1] = (byte)adler;
        return zlib;
    }

    private static void WriteChunk(MemoryStream png, string type, byte[] data)
    {
        byte[] name = System.Text.Encoding.ASCII.GetBytes(type);
        png.Write([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length], 0, 4);
        png.Write(name, 0, 4);
        png.Write(data, 0, data.Length);
        uint c = uint.MaxValue;
        foreach (byte b in name)
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (byte b in data)
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        c ^= uint.MaxValue;
        png.Write([(byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c], 0, 4);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
