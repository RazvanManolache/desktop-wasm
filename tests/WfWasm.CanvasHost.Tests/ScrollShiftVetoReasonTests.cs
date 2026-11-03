using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using ShimCanvas = WfWasm.Shim.ICanvas;
using TextHAlign = WfWasm.Shim.TextHAlign;
using TextVAlign = WfWasm.Shim.TextVAlign;

public sealed class ScrollShiftVetoReasonTests
{
    private static readonly MethodInfo ShiftUpload = typeof(WinFormsAppHost)
        .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .Single(m => m.Name == "TryShiftUploadFromFresh" && m.GetParameters().Length == 9);

    private sealed class PlainCanvas : ShimCanvas
    {
        public void FillRectangle(float x, float y, float width, float height, uint rgba) { }
        public void FillRoundedRectangle(float x, float y, float width, float height, float radius, uint rgba) { }
        public void FillGradientVertical(float x, float y, float width, float height, uint topRgba, uint bottomRgba) { }
        public void DrawLine(float x1, float y1, float x2, float y2, float width, uint rgba) { }
        public void DrawEllipse(float x, float y, float width, float height, float lineWidth, uint outlineRgba, uint? fillRgba = null) { }
        public void DrawText(string text, float x, float y, string fontCss, uint rgba) { }
        public void DrawImage(byte[] rgba, int width, int height, float x, float y, float dw, float dh) { }
        public void DrawTextAligned(string text, float x, float y, string fontCss, uint rgba, TextHAlign hAlign, TextVAlign vAlign, int mnemonicIndex = -1, float clipX = 0, float clipY = 0, float clipWidth = 0, float clipHeight = 0) { }
    }

    private static string? InvokeShift(object canvas, System.Windows.Forms.Control c, byte[] fresh, int w, int h, out bool used, out long stripPx)
    {
        object?[] args = { canvas, c, fresh, w, h, 0, 0, 0L, null };
        used = (bool)ShiftUpload.Invoke(null, args)!;
        stripPx = (long)args[7]!;
        return (string?)args[8];
    }

    private static byte[] GradientFrame(int w, int h)
    {
        using var bmp = new Bitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) bmp.SetPixel(x, y, Color.FromArgb(255, x * 20, y * 30, 10));
        return bmp.ToRgbaBytes();
    }

    [Fact]
    public void Disabled_Switch_Reports_Disabled()
    {
        using var control = new Control { Size = new Size(8, 6) };
        AppContext.SetSwitch("WfWasm.DisableScrollShift", true);
        try
        {
            string? veto = InvokeShift(new RecordingRenderer(), control, new byte[8 * 6 * 4], 8, 6, out bool used, out _);
            Assert.False(used);
            Assert.Equal("disabled", veto);
        }
        finally
        {
            AppContext.SetSwitch("WfWasm.DisableScrollShift", false);
        }
    }

    [Fact]
    public void Missing_Shift_Reports_NoShiftTaken()
    {
        using var control = new Control { Size = new Size(8, 6) };
        string? veto = InvokeShift(new RecordingRenderer(), control, new byte[8 * 6 * 4], 8, 6, out bool used, out _);
        Assert.False(used);
        Assert.Equal("no-shift-taken", veto);
    }

    [Fact]
    public void Plain_Canvas_Reports_NoBitmapLayers()
    {
        using var control = new Control { Size = new Size(8, 6) };
        WasmRepaintGate.NoteScroll(control, 0, -1);
        string? veto = InvokeShift(new PlainCanvas(), control, new byte[8 * 6 * 4], 8, 6, out bool used, out _);
        Assert.False(used);
        Assert.Equal("no-bitmap-layers", veto);
    }

    [Fact]
    public void Missing_Retained_Reports_RetainedMiss()
    {
        using var control = new Control { Size = new Size(8, 6) };
        WasmRepaintGate.NoteScroll(control, 0, -1);
        string? veto = InvokeShift(new RecordingRenderer(), control, new byte[8 * 6 * 4], 8, 6, out bool used, out _);
        Assert.False(used);
        Assert.StartsWith("retained-miss", veto);
    }

    [Fact]
    public void Edited_Frame_Reports_OverlapMismatch()
    {
        using var control = new Control { Size = new Size(8, 6) };
        byte[] old = GradientFrame(8, 6);
        using var before = new Bitmap(8, 6);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 8; x++) before.SetPixel(x, y, Color.FromArgb(255, x * 20, y * 30, 10));
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        typeof(WinFormsAppHost).GetMethod("StoreRetainedPaint", flags)!
            .Invoke(null, new object[] { control, before });
        WasmRepaintGate.NoteScroll(control, 0, -1);
        byte[] fresh = new byte[old.Length];
        Buffer.BlockCopy(old, 8 * 4, fresh, 0, 8 * 5 * 4);
        fresh[0] ^= 255; // same-frame edit must force the full path
        string? veto = InvokeShift(new RecordingRenderer(), control, fresh, 8, 6, out bool used, out _);
        Assert.False(used);
        Assert.StartsWith("overlap-mismatch", veto);
    }

    [Fact]
    public void Clean_Shift_Reports_Ok()
    {
        using var control = new Control { Size = new Size(8, 6) };
        byte[] old = GradientFrame(8, 6);
        using var before = new Bitmap(8, 6);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 8; x++) before.SetPixel(x, y, Color.FromArgb(255, x * 20, y * 30, 10));
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        typeof(WinFormsAppHost).GetMethod("StoreRetainedPaint", flags)!
            .Invoke(null, new object[] { control, before });
        WasmRepaintGate.NoteScroll(control, 0, -1);
        byte[] fresh = new byte[old.Length];
        Buffer.BlockCopy(old, 8 * 4, fresh, 0, 8 * 5 * 4);
        string? veto = InvokeShift(new RecordingRenderer(), control, fresh, 8, 6, out bool used, out long stripPx);
        Assert.True(used);
        Assert.Equal("ok", veto);
        Assert.Equal(8L, stripPx);
    }
}
