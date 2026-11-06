// PNG Adam7 interlace: pass-scatter decode for PngCodec (cf. PNG
// specification, Adam7 interlacing). Each of the seven passes is an
// independent sub-image: filter history restarts per pass, row bytes
// follow the pass width, and pixels scatter to (xStart + px * xStep,
// yStart + py * yStep). Empty passes consume no bytes. Sampling reuses
// PngCodec.Sample so color/depth/palette/tRNS behavior is identical to
// the non-interlaced path.
namespace System.Drawing;

internal static class PngAdam7
{
    /// <summary>(xStart, yStart, xStep, yStep) per pass, PNG order.</summary>
    internal static readonly (int XStart, int YStart, int XStep, int YStep)[] Passes =
    [
        (0, 0, 8, 8),
        (4, 0, 8, 8),
        (0, 4, 4, 8),
        (2, 0, 4, 4),
        (0, 2, 2, 4),
        (1, 0, 2, 2),
        (0, 1, 1, 2),
    ];

    /// <summary>
    /// Expected filtered-byte count (filter byte + row bytes per
    /// non-empty pass row). Short or long input reports corrupt data.
    /// </summary>
    internal static int ExpectedLength(int width, int height, int bitDepth, int channels)
    {
        int total = 0;
        foreach (var (xs, ys, xst, yst) in Passes)
        {
            int passWidth = PassWidth(width, xs, xst);
            int passHeight = PassHeight(height, ys, yst);
            if (passWidth == 0 || passHeight == 0)
                continue;
            int stride = (passWidth * bitDepth * channels + 7) / 8;
            total += (stride + 1) * passHeight;
        }
        return total;
    }

    internal static int PassWidth(int width, int xStart, int xStep) =>
        xStart < width ? (width - xStart + xStep - 1) / xStep : 0;

    internal static int PassHeight(int height, int yStart, int yStep) =>
        yStart < height ? (height - yStart + yStep - 1) / yStep : 0;

    internal static void Decode(
        byte[] raw, int width, int height, int bitDepth, int colorType, int channels,
        byte[] palette, byte[] transparency, int[] dest)
    {
        int unit = Math.Max(1, (bitDepth * channels + 7) / 8);
        int offset = 0;
        foreach (var (xs, ys, xst, yst) in Passes)
        {
            int passWidth = PassWidth(width, xs, xst);
            int passHeight = PassHeight(height, ys, yst);
            if (passWidth == 0 || passHeight == 0)
                continue;
            int stride = (passWidth * bitDepth * channels + 7) / 8;
            byte[] previous = new byte[stride];
            byte[] current = new byte[stride];
            for (int py = 0; py < passHeight; py++)
            {
                int filter = raw[offset++];
                Buffer.BlockCopy(raw, offset, current, 0, stride);
                offset += stride;
                PngCodec.Unfilter(filter, current, previous, unit);
                Buffer.BlockCopy(current, 0, previous, 0, stride);
                int y = ys + py * yst;
                for (int px = 0; px < passWidth; px++)
                    dest[y * width + xs + px * xst] = PngCodec.Sample(
                        current, px, passWidth, bitDepth, colorType, palette, transparency).ToArgb();
            }
        }
    }
}
