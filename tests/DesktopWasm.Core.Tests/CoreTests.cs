using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Core.Tests;

public sealed class PumpTests
{
    [Fact]
    public void Post_then_Pump_runs_action()
    {
        var pump = new WasmDispatcherPump();
        var ran = 0;
        pump.Post(() => ran++);
        Assert.Equal(1, pump.Pump());
        Assert.Equal(1, ran);
    }

    [Fact]
    public void Send_on_loop_thread_inlines()
    {
        var pump = new WasmDispatcherPump();
        Assert.Equal(42, pump.Send(() => 42));
    }

    [Fact]
    public void Tick_runs_frames_after_queue()
    {
        var pump = new WasmDispatcherPump();
        var order = new List<string>();
        pump.Post(() => order.Add("work"));
        pump.RequestFrame(_ => order.Add("frame"));
        pump.Tick(new FrameContext(1, 16.6));
        Assert.Equal(new[] { "work", "frame" }, order);
    }
}

public sealed class InputRouterTests
{
    [Fact]
    public void Pointer_dispatch_fans_out()
    {
        var router = new WasmInputRouter();
        WasmInputEvent? seen = null;
        router.Routed += e => seen = e;
        var handle = new WasmWindowHandle(1);
        router.DispatchPointer(new WasmPointerArgs(handle, 10, 20, WasmPointerButton.Left, true));
        Assert.IsType<WasmInputEvent.Pointer>(seen);
    }
}

public sealed class BridgeCodecTests
{
    [Fact]
    public void Serialize_contains_opcodes()
    {
        var cmds = new DrawCommand[]
        {
            new DrawCommand.FillRect(1, 2, 3, 4, 0xFF0000FF),
            new DrawCommand.DrawText("hi", 5, 6, "14px sans-serif", 0xFF000000, 0, 0, -1),
            new DrawCommand.PushClip(0, 0, 100, 100),
            new DrawCommand.PopClip(),
        };
        var json = DrawCommandJson.Serialize(cmds);
        Assert.Contains("hi", json, StringComparison.Ordinal);
        var (js, uploads) = DrawCommandJson.SplitUploads(cmds);
        Assert.Equal(4, js.Count);
        Assert.Empty(uploads);
    }

    [Fact]
    public void SplitUploads_separates_bitmaps()
    {
        var cmds = new DrawCommand[]
        {
            new DrawCommand.FillRect(0, 0, 1, 1, 0xFFFFFFFF),
            new DrawCommand.DefBitmap("l1", new byte[16], 2, 2),
        };
        var (js, uploads) = DrawCommandJson.SplitUploads(cmds);
        Assert.Single(js);
        Assert.Single(uploads);
    }
}
