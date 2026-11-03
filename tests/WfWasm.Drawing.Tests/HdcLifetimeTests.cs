extern alias WfDraw;
using D = WfDraw::System.Drawing;

public sealed class HdcLifetimeTests
{
    [Fact]
    public void Parameterless_Release_And_Disposal_Unroot_Every_Leased_Surface()
    {
        var oldAcquire = D.Graphics.HdcHook;
        var oldRelease = D.Graphics.HdcReleaseHook;
        var rooted = new Dictionary<IntPtr, D.Graphics>();
        long serial = 0, baseline = D.Graphics.LiveHdcLeaseCount;
        D.Graphics.HdcHook = g => { var h = (IntPtr)(++serial); rooted.Add(h, g); return h; };
        D.Graphics.HdcReleaseHook = h => rooted.Remove(h);
        try
        {
            for (int i = 0; i < 100; i++)
            {
                using var bitmap = new D.Bitmap(256, 256);
                using (var g = D.Graphics.FromImage(bitmap))
                {
                    IntPtr first = g.GetHdc(), second = g.GetHdc();
                    Assert.NotEqual(first, second);
                    g.ReleaseHdc();
                    Assert.False(rooted.ContainsKey(second));
                    Assert.True(rooted.ContainsKey(first));
                    g.ReleaseHdcInternal(first);
                    Assert.Empty(rooted);
                    g.GetHdc(); // forgotten explicit release: owned by Graphics
                }
                Assert.Empty(rooted);
                Assert.Equal(baseline, D.Graphics.LiveHdcLeaseCount);
                bitmap.SetPixel(0, 0, D.Color.Red); // Graphics does not own the image
                Assert.Equal(D.Color.Red.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
            }
        }
        finally { D.Graphics.HdcHook = oldAcquire; D.Graphics.HdcReleaseHook = oldRelease; }
    }
}
