using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf;

/// <summary>
/// Transport frame for the shared browser host path. The host uploads each
/// entry in <see cref="Uploads"/> out-of-band (TexDef for defs, TexPatch for
/// patches), then paints with one call: wfwasm.paint(canvasId, Json).
/// </summary>
public sealed record WpfTransportFrame(string Json, IReadOnlyList<DrawCommand> Uploads);

/// <summary>
/// Single-threaded WPF host: owns the pump, surface, input manager, and
/// windows. One <see cref="Tick"/> renders every window (layout + record),
/// replays into the shared surface (zero-copy with WinForms), and exports the
/// transport frame for wfwasm.paint. The browser wires requestAnimationFrame
/// to Tick; tests drive it manually.
/// </summary>
public sealed class WasmWpfHost
{
    private readonly IWasmMessagePump _pump;
    private readonly ISharedGraphicsSurface? _surface;
    private readonly List<(Window Window, WpfSize Size)> _windows = new();
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private readonly ImageUploadCache _images = new();

    /// <summary>
    /// The display sink changed (renderer switch, context loss, new canvas):
    /// uploaded-layer tracking no longer matches what the consumer holds, so
    /// the next frame re-uploads every live layer. Cheap (switches are rare)
    /// and keeps the steady state upload-once.
    /// </summary>
    public void NoteSinkChanged()
    {
        _images.ResetPresented();
        _dirty = true;
    }
    private bool _dirty = true;
    private WpfTransportFrame? _lastFrame;
    private IReadOnlyList<DrawCommand> _commands = Array.Empty<DrawCommand>();
    private string _lastResult = string.Empty;
    private long _paints;
    private long _skipped;
    private long _lastJsonChars;
    private long _lastUploadBytes;
    private long _totalJsonChars;
    private long _totalUploadBytes;
    private long _heapHighWatermark;

    /// <summary>
    /// Generated revision: incremented every time <see cref="Tick"/> renders a
    /// new frame. A revision is only consumed when <see cref="PublishAsync"/>
    /// presents it successfully; a failed or cancelled publish leaves the
    /// latest generated frame pending so the next call retries it.
    /// </summary>
    private long _generatedRevision;

    /// <summary>Revision of the last successfully presented frame.</summary>
    private long _presentedRevision;

    /// <summary>Canvas the presented revision was painted to.</summary>
    private string _presentedCanvasId = string.Empty;

    /// <summary>
    /// Full host: renders into the shared Skia surface (desktop, tests,
    /// future WebGL) and exports the Canvas2D transport frame.
    /// </summary>
    public WasmWpfHost(IWasmMessagePump pump, ISharedGraphicsSurface surface, IWasmInputRouter router)
        : this(pump, router)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _surface = surface;
    }

    /// <summary>
    /// Browser host: no Skia surface. Frames render to draw commands and
    /// export straight to the Canvas2D transport (the shared winforms-wasm
    /// path); layout falls back to heuristics only when the shaper backend
    /// is unavailable. The browser can alternatively replay CurrentCommands
    /// through the same Skia command renderer on its live WebGL surface.
    /// </summary>
    public WasmWpfHost(IWasmMessagePump pump, IWasmInputRouter router)
    {
        ArgumentNullException.ThrowIfNull(pump);
        ArgumentNullException.ThrowIfNull(router);
        _pump = pump;
        Input = new WpfInputManager(router);
        // Any routed input potentially changes visuals; programmatic changes
        // (Timer, data binding) mark dirty explicitly via Invalidate().
        router.Routed += _ => _dirty = true;
    }

    public WpfInputManager Input { get; }

    /// <summary>Stable identity of this host session (one session per host instance).</summary>
    public Guid SessionId { get; } = Guid.NewGuid();

    /// <summary>Revision most recently rendered by <see cref="Tick"/>.</summary>
    public long GeneratedRevision => _generatedRevision;

    /// <summary>Revision most recently presented successfully by <see cref="PublishAsync"/>.</summary>
    public long PresentedRevision => _presentedRevision;

    /// <summary>Current application commands for in-process raster/GPU replay.
    /// Tick owns layout and invalidation; callers never rebuild a substitute scene.</summary>
    public IReadOnlyList<DrawCommand> CurrentCommands => _commands;

    /// <summary>True when the next Tick will re-render.</summary>
    public bool IsDirty => _dirty;

    /// <summary>
    /// Publish telemetry for session-memory acceptance: per-publish transient
    /// sizes (JSON chars, upload bytes), cumulative totals, paint counts, the
    /// session image-cache bounds, and the managed-heap high-watermark. CPU
    /// submission sizes only; GPU execution/presentation is not measured.
    /// </summary>
    public WpfPublishTelemetry GetTelemetry()
    {
        var heap = GC.GetTotalMemory(false);
        if (heap > _heapHighWatermark) _heapHighWatermark = heap;
        var memoryInfo = GC.GetGCMemoryInfo();
        return new WpfPublishTelemetry(
            _generatedRevision, _presentedRevision, _paints, _skipped,
            _lastJsonChars, _lastUploadBytes, _totalJsonChars, _totalUploadBytes,
            _images.Count, _images.Bytes, heap, _heapHighWatermark,
            (long)memoryInfo.HeapSizeBytes, GC.GetTotalAllocatedBytes(false));
    }

    /// <summary>Marks every window dirty (call after programmatic visual changes).</summary>
    public void Invalidate() => _dirty = true;

    /// <summary>Root visuals for menu/tree walking (windows in z-order).</summary>
    public IReadOnlyList<UIElement> Roots => _windows.Select(w => (UIElement)w.Window).ToList();

    /// <summary>
    /// Shows a window as a modal dialog at the given size: added like
    /// <see cref="AddWindow"/>, then gated modal by the input manager.
    /// </summary>
    public void ShowDialog(Window dialog, WpfSize size)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (!_windows.Any(w => ReferenceEquals(w.Window, dialog))) AddWindow(dialog, size);
        else dialog.IsVisible = true;
        Input.ShowDialog(dialog);
    }

    /// <summary>Closes a modal dialog with the given result (see input manager).</summary>
    public void CloseDialog(Window dialog, bool? result = null)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        Input.CloseDialog(dialog, result);
    }

    public void AddWindow(Window window, WpfSize size)
    {
        ArgumentNullException.ThrowIfNull(window);
        _windows.Add((window, size));
        Input.AddRoot(window);
        // Visual roots report property/tree mutations through Invalidated; the
        // host subscribes once per owned window so app code never calls
        // host Invalidate() after ordinary setters (the event source outlives
        // any frame: the window holds the host delegate, not vice versa, and
        // windows are never removed, so no unsubscribe path is needed).
        window.Invalidated += _ => _dirty = true;
        // A new window changes the scene even when the previous frame was
        // clean; without this the added content would never present.
        _dirty = true;
    }

    /// <summary>
    /// Pumps queued work, renders when dirty, then publishes the latest
    /// generated frame through the shared canvas host path (uploads first,
    /// then one wfwasm.paint call) — unless that exact revision was already
    /// presented to the same canvas, in which case the cached result returns
    /// without touching the sink (no interop).
    /// </summary>
    /// <remarks>
    /// The publish decision is made after pumping: queued work posted before
    /// this call may invalidate and generate a new revision inside
    /// <see cref="Tick"/>, and that revision must be presented, not skipped.
    /// Pumping happens on the caller (loop) thread before serializing because
    /// the pump is thread-affine; the serialized section below never pumps, so
    /// it is safe to resume on any thread. Overlapping frame-loop and
    /// event-driven calls present in gate-acquisition order from the latest
    /// rendered frame, so a late completion can never overwrite newer
    /// presentation state. A failed or cancelled publish leaves the generated
    /// revision unpresented, so the next call retries the latest frame instead
    /// of returning the previous success.
    /// </remarks>
    public async Task<string> PublishAsync(string canvasId, IFrameSink sink, FrameContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(canvasId);
        ArgumentNullException.ThrowIfNull(sink);
        ct.ThrowIfCancellationRequested();

        // A new canvas holds none of the cached layers: re-upload the whole
        // live set with the next frame instead of only first-sight layers.
        // Imageless scenes skip the forced re-render (the cached frame stays
        // byte-identical either way).
        if (!string.Equals(_presentedCanvasId, canvasId, StringComparison.Ordinal) && _images.Count > 0)
            NoteSinkChanged();

        Tick(ctx);

        await _publishGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();

            if (_generatedRevision == _presentedRevision
                && _lastResult.Length > 0
                && string.Equals(_presentedCanvasId, canvasId, StringComparison.Ordinal))
            {
                _skipped++;
                return _lastResult;
            }

            var frame = _lastFrame!;
            string result;
            try
            {
                result = await PublishFrameAsync(sink, canvasId, frame, ct).ConfigureAwait(false);
            }
            catch
            {
                // The failed frame's layers may never have arrived: the next
                // frame re-uploads the live set (retry sends the same cached
                // frame, or a re-render re-defs it).
                _images.ResetPresented();
                throw;
            }
            _presentedRevision = _generatedRevision;
            _presentedCanvasId = canvasId;
            return result;
        }
        finally
        {
            _publishGate.Release();
        }
    }

    /// <summary>
    /// Renders all windows and returns the transport frame; when nothing is
    /// dirty the cached frame returns without re-render or surface replay.
    /// </summary>
    public WpfTransportFrame Tick(FrameContext ctx)
    {
        _ = ctx;
        _pump.Pump();
        // Wall-time animation pump: storyboard steps dirty their targets
        // through the usual Invalidated path, so motion repaints by itself.
        WpfAnimationClock.PumpRealTime();
        DispatcherTimer.TickAll();
        if (!_dirty && _lastFrame is not null) return _lastFrame;

        var all = new List<DrawCommand>();
        foreach (var (window, size) in _windows)
        {
            // Hidden windows (closed dialogs) contribute nothing to the frame.
            if (!window.IsVisible) continue;
            var commands = WasmMilCore.RenderHeadless(window, size);
            all.AddRange(commands);
        }

        var (replay, json, uploads) = ShareImages(all);
        _surface?.Replay(replay);

        _commands = replay;
        _lastFrame = new WpfTransportFrame(DrawCommandJson.Serialize(json), uploads);
        _generatedRevision++;
        _dirty = false;
        // Layout and paint just re-executed for every root, so their pending
        // flags are consumed here (presentation itself is tracked by revisions).
        foreach (var (window, _) in _windows) window.ClearInvalidation();
        return _lastFrame;
    }

    /// <summary>
    /// Rewrites inline image blits into shared layer references. Every frame
    /// carries defs for its whole live set first (so a consumer that skipped
    /// an intermediate frame — GPU replay coalescing, canvas switches — can
    /// still build every layer it blits), then paints by blit, and closes
    /// with a retain naming the live set so consumers release evicted layers.
    /// Out-of-band uploads carry only layers the sink has not been sent yet
    /// (upload-once steady state). Scenes without images return the input
    /// list untouched (no per-frame allocation).
    /// </summary>
    private (List<DrawCommand> Replay, List<DrawCommand> Json, List<DrawCommand> Uploads) ShareImages(
        List<DrawCommand> commands)
    {
        var empty = new List<DrawCommand>();
        if (commands.FindIndex(c => c is DrawCommand.DrawImage) < 0) return (commands, commands, empty);
        var defs = new List<DrawCommand>();
        var replay = new List<DrawCommand>(commands.Count + 2);
        var json = new List<DrawCommand>(commands.Count + 1);
        var uploads = new List<DrawCommand>();
        List<string>? live = null;
        foreach (var command in commands)
        {
            if (command is DrawCommand.DrawImage image)
            {
                var (layerId, isNew) = _images.GetOrAdd(image.Rgba, image.Width, image.Height);
                var blit = new DrawCommand.BlitBitmap(layerId, image.X, image.Y, image.DW, image.DH);
                if (live is null || !live.Contains(layerId, StringComparer.Ordinal))
                {
                    (live ??= new List<string>()).Add(layerId);
                    var def = new DrawCommand.DefBitmap(layerId, image.Rgba, image.Width, image.Height);
                    defs.Add(def);
                    if (isNew) uploads.Add(def);
                }
                replay.Add(blit);
                json.Add(blit);
            }
            else
            {
                replay.Add(command);
                json.Add(command);
            }
        }
        live ??= new List<string>();
        _images.EndFrame(live, _generatedRevision + 1);
        var retain = new DrawCommand.RetainLayers(live);
        replay.Add(retain);
        json.Add(retain);
        replay.InsertRange(0, defs);
        return (replay, json, uploads);
    }

    private async Task<string> PublishFrameAsync(IFrameSink sink, string canvasId, WpfTransportFrame frame, CancellationToken ct)
    {
        _lastResult = await FramePublisher.PublishAsync(sink, canvasId, frame.Json, frame.Uploads, ct).ConfigureAwait(false);
        _paints++;
        _lastJsonChars = frame.Json.Length;
        _lastUploadBytes = 0;
        foreach (var upload in frame.Uploads)
            _lastUploadBytes += upload switch
            {
                DrawCommand.DefBitmap d => (long)d.Rgba.Length,
                DrawCommand.PatchBitmap p => (long)p.Rgba.Length,
                _ => 0,
            };
        _totalJsonChars += _lastJsonChars;
        _totalUploadBytes += _lastUploadBytes;
        return _lastResult;
    }
}

/// <summary>
/// Session publish/memory telemetry (see <see cref="WasmWpfHost.GetTelemetry"/>).
/// Sizes are CPU submission sizes; GPU execution/presentation is not measured.
/// </summary>
public sealed record WpfPublishTelemetry(
    long GeneratedRevision,
    long PresentedRevision,
    long Paints,
    long SkippedPublishes,
    long LastJsonChars,
    long LastUploadBytes,
    long TotalJsonChars,
    long TotalUploadBytes,
    int ImageCount,
    long ImageBytes,
    long ManagedHeapBytes,
    long ManagedHeapHighWatermark,
    long CommittedHeapBytes,
    long TotalAllocatedBytes);
