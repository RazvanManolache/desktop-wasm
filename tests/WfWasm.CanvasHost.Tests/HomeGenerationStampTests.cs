using Microsoft.JSInterop;
using WfWasm.CanvasHost;
using WfWasm.CanvasHost.Pages;

// GAP-genprov-0836-k7d2: Home must assign GenerationProvider at init
// (mirroring BackendProvider) and refresh the cached stamp from
// window.wfwasmRuntime.generation via JS interop. FAILS until the
// refresh seam lands.
public sealed class HomeGenerationStampTests
{
    private sealed class GenerationJs : IJSRuntime
    {
        private readonly string? _generation;

        public GenerationJs(string? generation) => _generation = generation;

        public List<string> Calls { get; } = [];

        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            if (identifier == "wfwasm.readGeneration" && typeof(T) == typeof(string))
                return ValueTask.FromResult((T)(object)_generation!);
            return ValueTask.FromResult(default(T)!);
        }

        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<T>(identifier, args);
    }

    [Fact]
    public async Task RefreshReportGeneration_Assigns_Stamped_Provider_Value()
    {
        var js = new GenerationJs("20261105T0000Zw9");
        BrowserErrorReporter.GenerationProvider = null;
        BrowserErrorReporter.NoteGeneration(null);
        try
        {
            await Home.RefreshReportGenerationAsync(js);
            Assert.Contains("wfwasm.readGeneration", js.Calls);
            Assert.Equal("20261105T0000Zw9", BrowserErrorReporter.GenerationProvider?.Invoke());
        }
        finally
        {
            BrowserErrorReporter.GenerationProvider = null;
            BrowserErrorReporter.NoteGeneration(null);
        }
    }

    [Fact]
    public async Task RefreshReportGeneration_Absent_Stamp_Means_Unknown()
    {
        var js = new GenerationJs(null);
        BrowserErrorReporter.GenerationProvider = BrowserErrorReporter.CachedGeneration;
        try
        {
            await Home.RefreshReportGenerationAsync(js);
            Assert.Null(BrowserErrorReporter.GenerationProvider?.Invoke());
        }
        finally
        {
            BrowserErrorReporter.GenerationProvider = null;
            BrowserErrorReporter.NoteGeneration(null);
        }
    }
}
