using System.Text.Json;
using WfWasm.CanvasHost;
using WfWasm.Renderer;

public sealed class CanvasClipBridgeTests
{
    [Fact]
    public void UploadSplit_PreservesClipAroundRetainedLayer_WithoutClippingUpload()
    {
        var renderer = new RecordingRenderer();
        using (renderer.PushClip(10, 20, 30, 40))
        {
            renderer.DefBitmap("edge", new byte[] { 255, 0, 0, 255 }, 1, 1);
            renderer.BlitBitmap("edge", 0, 0, 100, 100);
        }
        var (commands, uploads) = CanvasBridge.SplitUploads(renderer.Commands);
        Assert.Equal("edge", Assert.Single(uploads).LayerId);
        using var doc = JsonDocument.Parse(CanvasBridge.SerializeCommands(commands));
        Assert.Equal(new[] { 13, 9, 14 },
            doc.RootElement.EnumerateArray().Select(op => op[0].GetInt32()).ToArray());
    }
}
