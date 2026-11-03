using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WfWasm.CanvasHost;
using WfWasm.Renderer;

public sealed class ScrollWholeUploadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fresh_Raster_Uses_Pending_Scroll_Only_When_Overlap_Really_Matches(bool edited)
    {
        using var control = new Control { Size = new Size(8, 6) };
        using var before = new Bitmap(8, 6);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 8; x++) before.SetPixel(x, y, Color.FromArgb(255, x * 20, y * 30, 10));
        byte[] old = before.ToRgbaBytes();
        byte[] fresh = new byte[old.Length];
        Buffer.BlockCopy(old, 8 * 4, fresh, 0, 8 * 5 * 4);
        if (edited) fresh[0] ^= 255; // same-frame edit must force the full path
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        typeof(WinFormsAppHost).GetMethod("StoreRetainedPaint", flags)!
            .Invoke(null, new object[] { control, before });
        WasmRepaintGate.NoteScroll(control, 0, -1);
        var recorder = new RecordingRenderer();
        object?[] args = { recorder, control, fresh, 8, 6, 0, 0, 0L, null };
        bool used = (bool)typeof(WinFormsAppHost).GetMethod("TryShiftUploadFromFresh", flags)!.Invoke(null, args)!;
        Assert.Equal(!edited, used);
        Assert.False(WasmRepaintGate.TakeScrollShift(control, out _, out _));
        if (edited)
        {
            Assert.Empty(recorder.Commands);
            return;
        }
        var shift = Assert.Single(recorder.Commands.OfType<ShiftBitmapCommand>());
        Assert.Equal((0, -1), (shift.DX, shift.DY));
        var patch = Assert.Single(recorder.Commands.OfType<PatchBitmapCommand>());
        Assert.Equal((0, 5, 8, 1), (patch.X, patch.Y, patch.Width, patch.Height));
        Assert.Equal(8L, args[^2]);
        byte[] browser = new byte[old.Length];
        Buffer.BlockCopy(old, 8 * 4, browser, 0, 8 * 5 * 4);
        Buffer.BlockCopy(patch.Rgba, 0, browser, 8 * 5 * 4, patch.Rgba.Length);
        Assert.Equal(fresh, browser);
    }
}
