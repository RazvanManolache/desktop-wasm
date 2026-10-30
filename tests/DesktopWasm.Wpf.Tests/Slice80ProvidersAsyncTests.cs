using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

// Slice-80 async seam: ObjectDataProvider.IsAsynchronous posts the method
// query through the engine dispatcher instead of running it inline.
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice80ProvidersAsyncTests
{
    private sealed class Stringy
    {
        public string Make(object? arg) => "made:" + arg;
    }

    public Slice80ProvidersAsyncTests()
    {
        // Rebind the loop pump to this thread: statics bind thread affinity
        // at first touch, and xunit does not pin test threads.
        WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
    }

    [Fact]
    public void Asynchronous_query_posts_and_completes_on_pump()
    {
        var pump = new WasmDispatcherPump();
        WasmDispatcher.Current = new WasmDispatcher(pump);
        var provider = new ObjectDataProvider { IsAsynchronous = true };
        var changed = 0;
        provider.DataChanged += (_, _) => changed++;
        provider.ObjectInstance = new Stringy();
        provider.MethodName = "Make";
        provider.MethodParameters.Add(7);
        // Posted, not run: no data yet and no failure yet.
        Assert.Null(provider.Data);
        Assert.Null(provider.Error);
        Assert.Equal(0, changed);
        pump.Pump();
        // Every posted setter ran its own query, like the framework: the
        // last one wins and each completion raised DataChanged.
        Assert.Equal("made:7", provider.Data);
        Assert.Null(provider.Error);
        Assert.Equal(3, changed);
    }
}
