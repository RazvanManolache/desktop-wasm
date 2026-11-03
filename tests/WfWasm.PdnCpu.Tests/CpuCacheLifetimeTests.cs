using System.Collections;
using WfWasm.PdnCpu;

public sealed class CpuCacheLifetimeTests
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference Create_And_Free_Pinned_Context()
    {
        long handle = CpuPaint.CreateScratchTarget(100, 100);
        object target = System.Runtime.InteropServices.GCHandle.FromIntPtr((IntPtr)handle).Target!;
        var pin = (System.Runtime.InteropServices.GCHandle)target.GetType().GetField("Pin")!.GetValue(target)!;
        var pixels = new WeakReference(pin.Target);
        CpuPaint.FreeContext(handle);
        return pixels;
    }

    [Fact]
    public void Context_Teardown_Does_Not_Leave_Its_Own_Pixel_Buffer_Pinned()
    {
        var pixels = Create_And_Free_Pinned_Context();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(pixels.IsAlive);
    }

    private sealed class Resource : IDisposable
    {
        public int References = 1;
        public int Releases;
        public long Bitmap = CpuPaint.CreateBitmapEmpty(16, 16);
        public void Dispose()
        {
            Releases++;
            if (--References == 0) { CpuPaint.FreeBitmap(Bitmap); Bitmap = 0; }
        }
    }

    [Fact]
    public void Replacing_And_Closing_A_Cache_Releases_Only_Its_Own_Reference()
    {
        var cache = new Hashtable();
        var previous = new Resource { References = 2 }; // one external device ref
        var current = new Resource();
        try
        {
            CpuPaint.SetCpuCacheEntry(cache, "provider", new object[] { 1L, previous });
            CpuPaint.SetCpuCacheEntry(cache, "provider", new object[] { 2L, current });
            Assert.Equal(1, previous.Releases);
            Assert.NotEqual(0, previous.Bitmap);
            Assert.Equal(16, CpuPaint.BitmapWidth(previous.Bitmap));
            CpuPaint.ReleaseCpuCache(cache);
            Assert.Empty(cache);
            Assert.Equal(1, current.Releases);
            Assert.Equal(0, current.Bitmap);
            CpuPaint.ReleaseCpuCache(cache);
            Assert.Equal(1, current.Releases);
        }
        finally
        {
            if (previous.Bitmap != 0) previous.Dispose();
            if (current.Bitmap != 0) current.Dispose();
        }
    }

    [Fact]
    public void Aliases_And_Writeback_Entries_Do_Not_Double_Release_Resources()
    {
        var cache = new Hashtable();
        var shared = new Resource();
        var replacement = new Resource();
        object source = new();
        CpuPaint.SetCpuCacheEntry(cache, "one", new object[] { 1L, shared });
        CpuPaint.SetCpuCacheEntry(cache, "two", new object[] { 1L, shared });
        cache[123L] = source; // borrowed FromBitmap source, not a cache resource
        CpuPaint.SetCpuCacheEntry(cache, "one", new object[] { 2L, replacement });
        Assert.Equal(0, shared.Releases);
        CpuPaint.ReleaseCpuCache(cache);
        Assert.Equal(1, shared.Releases);
        Assert.Equal(1, replacement.Releases);
        Assert.Equal(0, shared.Bitmap);
        Assert.Equal(0, replacement.Bitmap);
    }

    [Fact]
    public void Effect_Finalization_Releases_Each_Counted_Input_Slot()
    {
        var bitmap = new Resource { References = 2 };
        var inputs = new Hashtable { [0] = bitmap, [1] = bitmap };
        CpuPaint.ReleaseCpuRetainedInputs(inputs);
        Assert.Empty(inputs);
        Assert.Equal(2, bitmap.Releases);
        Assert.Equal(0, bitmap.Bitmap);
        CpuPaint.ReleaseCpuRetainedInputs(inputs);
        Assert.Equal(2, bitmap.Releases);
    }
}
