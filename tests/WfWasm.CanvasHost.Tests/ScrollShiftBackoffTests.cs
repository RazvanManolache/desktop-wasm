using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WfWasm.CanvasHost;
using WfWasm.Renderer;

public sealed class ScrollShiftBackoffTests
{
    private static readonly MethodInfo ShiftUpload = typeof(WinFormsAppHost)
        .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .Single(m => m.Name == "TryShiftUploadFromFresh" && m.GetParameters().Length == 9);
    private static readonly MethodInfo StoreRetained = typeof(WinFormsAppHost)
        .GetMethod("StoreRetainedPaint", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo LayerIdFor = typeof(WinFormsAppHost)
        .GetMethod("LayerIdFor", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo StoreSuppressed = typeof(WinFormsAppHost)
        .GetMethod("OverlapStoreSuppressed", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo RetainedTable = typeof(WinFormsAppHost)
        .GetField("s_retainedPaint", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static string? InvokeShift(object canvas, System.Windows.Forms.Control c, byte[] fresh, int w, int h, out bool used, out long stripPx)
    {
        object?[] args = { canvas, c, fresh, w, h, 0, 0, 0L, null };
        used = (bool)ShiftUpload.Invoke(null, args)!;
        stripPx = (long)args[7]!;
        return (string?)args[8];
    }

    private static Bitmap GradientBitmap(int w, int h)
    {
        var bmp = new Bitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) bmp.SetPixel(x, y, Color.FromArgb(255, x * 20, y * 30, 10));
        return bmp;
    }

    private static byte[] RetainedBytes(System.Windows.Forms.Control c)
    {
        string id = (string)LayerIdFor.Invoke(null, new object[] { c })!;
        var table = (Dictionary<string, Bitmap>)RetainedTable.GetValue(null)!;
        lock (table)
        {
            return table[id].ToRgbaBytes();
        }
    }

    private static bool IsStoreSuppressed(System.Windows.Forms.Control c) =>
        (bool)StoreSuppressed.Invoke(null, new object[] { c })!;

    private static byte[] MismatchingFresh(byte[] old, int w)
    {
        byte[] fresh = new byte[old.Length];
        Buffer.BlockCopy(old, w * 4, fresh, 0, old.Length - w * 4);
        fresh[0] ^= 255; // same-frame edit must force the full path
        return fresh;
    }

    [Fact]
    public void Mismatch_Streak_Suppresses_Proof_And_Falls_Back_To_Full_Upload()
    {
        using var control = new Control { Size = new Size(8, 6) };
        using var before = GradientBitmap(8, 6);
        byte[] old = before.ToRgbaBytes();
        StoreRetained.Invoke(null, new object[] { control, before });
        var renderer = new RecordingRenderer();
        var vetoes = new List<string?>();
        for (int i = 0; i < 4; i++)
        {
            WasmRepaintGate.NoteScroll(control, 0, -1);
            string? veto = InvokeShift(renderer, control, MismatchingFresh(old, 8), 8, 6, out bool used, out long stripPx);
            vetoes.Add(veto);
            // Every exit reports fallback: the untouched call site uploads full.
            Assert.False(used);
            Assert.Equal(0L, stripPx);
        }
        Assert.StartsWith("overlap-mismatch", vetoes[0]);
        Assert.StartsWith("overlap-mismatch", vetoes[1]);
        Assert.StartsWith("overlap-mismatch", vetoes[2]);
        Assert.Equal("overlap-backoff", vetoes[3]);
        Assert.Empty(renderer.Commands.OfType<ShiftBitmapCommand>());
    }

    [Fact]
    public void Backoff_Consumes_Pending_Shift_So_Retry_Never_Sees_Stale_Displacement()
    {
        using var control = new Control { Size = new Size(8, 6) };
        using var before = GradientBitmap(8, 6);
        byte[] old = before.ToRgbaBytes();
        StoreRetained.Invoke(null, new object[] { control, before });
        var renderer = new RecordingRenderer();
        for (int i = 0; i < 4; i++)
        {
            WasmRepaintGate.NoteScroll(control, 0, -1);
            InvokeShift(renderer, control, MismatchingFresh(old, 8), 8, 6, out _, out _);
        }
        // The suppressed paint consumed the noted shift instead of letting it
        // accumulate: no stale displacement survives for the retry proof.
        Assert.False(WasmRepaintGate.TakeScrollShift(control, out _, out _));
    }

    [Fact]
    public void Backoff_Skips_Retained_Store_And_Retry_Reproves_Ok()
    {
        using var control = new Control { Size = new Size(8, 6) };
        using var before = GradientBitmap(8, 6);
        byte[] old = before.ToRgbaBytes();
        StoreRetained.Invoke(null, new object[] { control, before });
        var renderer = new RecordingRenderer();
        for (int i = 0; i < 3; i++)
        {
            WasmRepaintGate.NoteScroll(control, 0, -1);
            Assert.StartsWith("overlap-mismatch",
                InvokeShift(renderer, control, MismatchingFresh(old, 8), 8, 6, out _, out _));
        }
        // Proof ran on every mismatch paint, so the store gate stays open and
        // the retained surface tracks the last proof.
        Assert.False(IsStoreSuppressed(control));
        byte[] tracked = RetainedBytes(control);
        for (int i = 0; i < 5; i++)
        {
            WasmRepaintGate.NoteScroll(control, 0, -1);
            Assert.Equal("overlap-backoff",
                InvokeShift(renderer, control, MismatchingFresh(old, 8), 8, 6, out bool suppressedUsed, out _));
            Assert.False(suppressedUsed);
        }
        // Suppressed paints skip the retained clone: bytes are untouched.
        Assert.True(IsStoreSuppressed(control));
        Assert.Equal(tracked, RetainedBytes(control));
        // Finish the cooldown, then prove the retry re-runs the proof: a
        // clean shifted frame engages the shift path and resets the streak.
        for (int i = 0; i < 25; i++)
        {
            WasmRepaintGate.NoteScroll(control, 0, -1);
            InvokeShift(renderer, control, MismatchingFresh(old, 8), 8, 6, out _, out _);
        }
        StoreRetained.Invoke(null, new object[] { control, GradientBitmap(8, 6) });
        WasmRepaintGate.NoteScroll(control, 0, -1);
        byte[] fresh = new byte[old.Length];
        Buffer.BlockCopy(old, 8 * 4, fresh, 0, 8 * 5 * 4);
        string? veto = InvokeShift(renderer, control, fresh, 8, 6, out bool used, out long stripPx);
        Assert.True(used);
        Assert.Equal("ok", veto);
        Assert.Equal(8L, stripPx);
        var shift = Assert.Single(renderer.Commands.OfType<ShiftBitmapCommand>());
        Assert.Equal((0, -1), (shift.DX, shift.DY));
        // Streak reset: the next mismatch reports mismatch, not backoff.
        WasmRepaintGate.NoteScroll(control, 0, -1);
        Assert.StartsWith("overlap-mismatch",
            InvokeShift(renderer, control, MismatchingFresh(old, 8), 8, 6, out _, out _));
    }

    [Fact]
    public void Veto_Counters_Record_Families_And_Backoff()
    {
        using var control = new Control { Size = new Size(8, 6) };
        using var before = GradientBitmap(8, 6);
        byte[] old = before.ToRgbaBytes();
        StoreRetained.Invoke(null, new object[] { control, before });
        var renderer = new RecordingRenderer();
        long MismatchBefore = WinFormsAppHost.SnapshotShiftVetoCounts()
            .TryGetValue("overlap-mismatch", out long m) ? m : 0;
        long backoffBefore = WinFormsAppHost.SnapshotShiftVetoCounts()
            .TryGetValue("overlap-backoff", out long b) ? b : 0;
        for (int i = 0; i < 4; i++)
        {
            WasmRepaintGate.NoteScroll(control, 0, -1);
            InvokeShift(renderer, control, MismatchingFresh(old, 8), 8, 6, out _, out _);
        }
        var after = WinFormsAppHost.SnapshotShiftVetoCounts();
        Assert.True(after.TryGetValue("overlap-mismatch", out long mismatchAfter)
            && mismatchAfter - MismatchBefore >= 3);
        Assert.True(after.TryGetValue("overlap-backoff", out long backoffAfter)
            && backoffAfter - backoffBefore >= 1);
    }
}
