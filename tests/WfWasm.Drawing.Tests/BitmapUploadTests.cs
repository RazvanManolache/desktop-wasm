extern alias WfDraw;
using Bitmap = WfDraw::System.Drawing.Bitmap;

public sealed class BitmapUploadTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(17)]
    public void Full_and_cropped_upload_preserve_channels_alpha_and_vector_tails(int width)
    {
        using var bitmap = new Bitmap(width, 5);
        var expected = new List<byte>();
        var cropExpected = new List<byte>();
        for (int y = 0; y < 5; y++)
        for (int x = 0; x < width; x++)
        {
            var c = Color.FromArgb((x * 71 + y * 37) % 256, (x * 43 + y * 3) % 256, (x * 13 + y * 19) % 256, (x * 7 + y * 61) % 256);
            bitmap.SetPixel(x, y, c);
            expected.AddRange(new[] { c.R, c.G, c.B, c.A });
            if (y >= 1 && y < 4 && x < width - 1) cropExpected.AddRange(new[] { c.R, c.G, c.B, c.A });
        }
        Assert.Equal(expected, bitmap.ToRgbaBytes());
        Assert.Equal(cropExpected, bitmap.ToRgbaBytes(new Rectangle(0, 1, width - 1, 3)));
    }
}
