using System.Reflection;
using System.Windows.Forms;
using WfWasm.CanvasHost.Pages;

namespace WfWasm.CanvasHost.Tests;

public sealed class WheelPaintTransactionTests
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VendorInvalidations_WaitForWholeWheelDispatch_EvenOnFailure(bool fail)
    {
        var home = new Home();
        // Exercise real invalidation and pump scheduling, without creating a
        // browser renderer. A disposed component makes RepaintAsync a no-op.
        typeof(Home).GetField("_disposed", Private)!.SetValue(home, true);
        typeof(Home).GetProperty("State", Private | BindingFlags.Public)!.SetValue(home, new SceneState());
        using var control = new Panel();
        Action request = () => typeof(Home).GetMethod("RequestPaint", Private)!.Invoke(home, null);
        WasmRepaintGate.RepaintRequested += request;
        try
        {
            Action invalidate = () =>
            {
                WasmRepaintGate.RequestRepaint(control);
                WasmRepaintGate.RequestRepaint(control);
                Assert.False((bool)typeof(Home).GetField("_repaintRunning", Private)!.GetValue(home)!,
                    "Painting must not re-enter a partially completed vendor handler.");
                Assert.True((bool)typeof(Home).GetField("_repaintDirty", Private)!.GetValue(home)!);
            };
            Action wheel = () =>
            {
                invalidate();
                // Nested dispatch must not release the outer transaction.
                Dispatch(home, invalidate);
                Assert.False((bool)typeof(Home).GetField("_repaintRunning", Private)!.GetValue(home)!);
                if (fail) throw new InvalidOperationException("wheel-fault");
            };
            var error = Record.Exception(() => Dispatch(home, wheel));
            if (fail) Assert.Equal("wheel-fault", error?.GetBaseException().Message);
            else Assert.Null(error);
            Assert.Equal(0, (int)typeof(Home).GetField("_updateDispatchDepth", Private)!.GetValue(home)!);
            Assert.True((bool)typeof(Home).GetField("_repaintRunning", Private)!.GetValue(home)!,
                "The final dirty state must be scheduled even when the handler throws.");
            await Task.Delay(40);
            Assert.False((bool)typeof(Home).GetField("_repaintRunning", Private)!.GetValue(home)!);
        }
        finally
        {
            WasmRepaintGate.RepaintRequested -= request;
            WasmRepaintGate.DrainDirty(out _);
            home.Dispose();
            ((WinFormsAppHost)typeof(Home).GetField("_winFormsHost", Private)!.GetValue(home)!).Dispose();
        }
    }

    private static void Dispatch(Home home, Action action) =>
        typeof(Home).GetMethod("DispatchUpdate", Private)!.Invoke(home, new object[] { action });
}
