using System.Runtime.InteropServices;
using WfWasm.PdnCpu;

namespace WfWasm.PdnCpu.Tests;

// Scratch ownership (WC1835): a stale/duplicate free of a raw scratch
// handle that is still owned by the ID table must not kill it. Pre-fix,
// FreeScratchTarget freed the live handle and left a dangling ID binding;
// under forced-GC composition the recycled slot aliased a live foreign
// object (fatal CLR aborts: ExecutionEngineException / AV in an unrelated
// Dictionary read). The finalizer/consume paths always free
// freshly-claimed raws, so skipping a still-owned raw is always correct.
public sealed unsafe class ScratchOwnershipTests
{
    private static long MakeWhiteBitmap(int w, int h, byte alpha)
    {
        var pixels = new uint[w * h];
        uint px = (uint)(0x00FFFFFF | (alpha << 24));
        Array.Fill(pixels, px);
        fixed (uint* p = pixels)
            return CpuPaint.CreateBitmap((IntPtr)p, w * 4, w, h);
    }

    [Fact]
    public void FreeScratchTarget_Skips_Raw_Still_Owned_By_Id_Table()
    {
        long id = CpuPaint.CreateScratchHandle(8, 8, 1f, 0f, 0f, 1f, 0f, 0f);
        Assert.True(id < 0);
        long raw = CpuPaint.PeekScratch(id);
        Assert.NotEqual(0, raw);
        try
        {
            // Paint the scratch opaque white through its ID.
            long bmp = MakeWhiteBitmap(8, 8, 255);
            try
            {
                CpuPaint.DrawBitmap(id, bmp, 0, 0, 8, 8, true, 0, 0, 8, 8, false, 1f, false);
            }
            finally
            {
                CpuPaint.FreeBitmap(bmp);
            }

            // Stale/duplicate free of the still-owned raw (simulates the
            // recycled claimer's late free landing on a live binding).
            CpuPaint.FreeScratchTarget(raw);

            // The ID-owned scratch must still resolve and blit.
            byte[] pixels = new byte[8 * 8 * 4];
            GCHandle pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                long dst = CpuPaint.CreateTarget(pin.AddrOfPinnedObject(), 8, 8, 8 * 4, 4);
                try
                {
                    CpuPaint.BlitScratchOver(dst, id);
                }
                finally
                {
                    CpuPaint.FreeContext(dst);
                }
            }
            finally
            {
                pin.Free();
            }
            Assert.All(pixels, b => Assert.Equal(255, b));
        }
        finally
        {
            // Normal consume-path cleanup (idempotent).
            long slot = id;
            long claimed = CpuPaint.ClaimScratch(ref slot);
            Assert.Equal(0, slot);
            CpuPaint.FreeScratchTarget(claimed);
        }
    }
}
