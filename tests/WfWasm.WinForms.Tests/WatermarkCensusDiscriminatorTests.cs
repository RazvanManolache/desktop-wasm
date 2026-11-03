// RVW-5795 Job1 census discriminator: the served SF06 watermark gate needs to
// tell "OnPaint never ran / no target" apart from "ran with no target".
// IL-verified vendor contract (verification/rvw5923-wpf/VendorProbe):
//   SfNumericTextBoxDemo.GettingStarted sets WatermarkText "Enter your age"
//   and "Enter mobile number"; SfNumericTextBox.OnPaint (Text empty) calls
//   TextRenderer.DrawText(Graphics, Watermark, Font, ClientRectangle,
//   Style.WatermarkForeColor, flags 4/5/6).
// Bridge mechanics (read-only, src/WfWasm.WinForms/Interop/PInvoke.Methods.cs):
//   TryDrawText calls NoteGdiPaint FIRST (attempt recorded in the ring),
//   then returns false when WasmFillBridge.TryGetTarget fails for the HDC.
//   Graphics.GetHdc() yields a null HDC in this fork, so an unscoped paint
//   leaves zero registered targets: attempts>0 with the cue in the ring
//   while nothing paints. That census shape == branch (b) "no bound paint
//   target", distinct from no-attempt (branch a/d) and from Transparent-fore
//   silence (already pinned by WatermarkCausalTests).
extern alias Fork;

using ForkGdiText = Fork::System.Windows.Forms.WasmGdiText;
using ForkTextBridge = Fork::Windows.Win32.PInvoke.WasmTextBridge;

namespace WfWasm.WinForms.Tests;

public sealed class WatermarkCensusDiscriminatorTests
{
    [Fact]
    public void UnregisteredHdc_AgeCue_Attempt_Recorded_In_Census_Ring()
    {
        // Vendor-identical cue, Left|VerticalCenter (flags 4).
        var hdc = (IntPtr)0x4795;
        int height = ForkGdiText.DrawText(hdc, "Enter your age", -1, 0, 0, 200, 30, 4, out _, out _);
        Assert.Equal(0, height);
        Assert.Contains("Enter your age", ForkTextBridge.DumpGdiPaintCensus());
    }

    [Fact]
    public void UnregisteredHdc_MobileCue_Attempt_Recorded_In_Census_Ring()
    {
        // Vendor-identical cue, Right|VerticalCenter (flags 6).
        var hdc = (IntPtr)0x4796;
        int height = ForkGdiText.DrawText(hdc, "Enter mobile number", -1, 0, 0, 200, 30, 6, out _, out _);
        Assert.Equal(0, height);
        Assert.Contains("Enter mobile number", ForkTextBridge.DumpGdiPaintCensus());
    }
}
