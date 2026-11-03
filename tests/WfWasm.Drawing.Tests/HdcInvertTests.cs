extern alias WfDraw;
using Bitmap = WfDraw::System.Drawing.Bitmap;
using Graphics = WfDraw::System.Drawing.Graphics;

public sealed class HdcInvertTests
{
    [Fact]
    public void InvertRect_Uses_device_clip_preserves_alpha_and_reverses_exactly()
    {
        var old = Graphics.HdcHook;
        Graphics.HdcHook = _ => new IntPtr(0x5f0101);
        try
        {
            using var bitmap = new Bitmap(8, 7);
            using var g = Graphics.FromImage(bitmap);
            var original = Color.FromArgb(173, 21, 82, 193);
            g.Clear(original);
            g.SetClip(new Rectangle(1, 1, 5, 4));
            g.ExcludeClip(new Rectangle(3, 2, 1, 2));
            g.TranslateTransform(100, 100); // HDC rectangle is already in device space.
            IntPtr hdc = g.GetHdc();
            int before = bitmap.PaintVersion;
            Assert.True(Graphics.InvertHdcRect(hdc, -100, 0, 5, 6));
            int writes = 0;
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 8; x++)
                {
                    bool inverted = x >= 1 && x < 5 && y >= 1 && y < 5 && !(x == 3 && y >= 2 && y < 4);
                    Assert.Equal(inverted ? original.ToArgb() ^ 0x00ffffff : original.ToArgb(), bitmap.GetPixel(x, y).ToArgb());
                    if (inverted) writes++;
                }
            Assert.Equal(before + writes, bitmap.PaintVersion);
            Assert.True(Graphics.InvertHdcRect(hdc, -100, 0, 5, 6));
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 8; x++) Assert.Equal(original.ToArgb(), bitmap.GetPixel(x, y).ToArgb());
            g.ReleaseHdc(hdc);
            Assert.False(Graphics.InvertHdcRect(hdc, 0, 0, 1, 1));
        }
        finally { Graphics.HdcHook = old; }
    }

    [Fact]
    public void Disposed_graphics_releases_its_invert_target_and_unknown_handles_fail()
    {
        var old = Graphics.HdcHook;
        Graphics.HdcHook = _ => new IntPtr(0x5f0102);
        try
        {
            using var bitmap = new Bitmap(3, 3);
            var g = Graphics.FromImage(bitmap);
            IntPtr hdc = g.GetHdc();
            Assert.True(Graphics.InvertHdcRect(hdc, 1, 1, 1, 1));
            g.Dispose();
            Assert.False(Graphics.InvertHdcRect(hdc, 0, 0, 3, 3));
            Assert.False(Graphics.InvertHdcRect(IntPtr.Zero, 0, 0, 1, 1));
        }
        finally { Graphics.HdcHook = old; }
    }
}
