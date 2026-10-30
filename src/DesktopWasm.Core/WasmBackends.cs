namespace DesktopWasm.Core;

/// <summary>In-memory virtual window manager (browser host replaces with DOM-backed impl).</summary>
public sealed class InMemoryWindowBackend : IWasmWindowBackend
{
    private int _nextId = 1;
    private readonly Dictionary<int, (WasmChromeStyle Chrome, WasmBounds Bounds, int Z)> _windows = new();
    private readonly HashSet<int> _modal = new();

    public WasmWindowHandle Create(WasmChromeStyle chrome, WasmBounds bounds)
    {
        var handle = new WasmWindowHandle(_nextId++);
        _windows[handle.Id] = (chrome, bounds, 0);
        return handle;
    }

    public void Close(WasmWindowHandle handle)
    {
        _windows.Remove(handle.Id);
        _modal.Remove(handle.Id);
    }

    public void SetBounds(WasmWindowHandle handle, WasmBounds bounds)
    {
        if (_windows.TryGetValue(handle.Id, out var w)) _windows[handle.Id] = (w.Chrome, bounds, w.Z);
    }

    public void SetZOrder(WasmWindowHandle handle, int z)
    {
        if (_windows.TryGetValue(handle.Id, out var w)) _windows[handle.Id] = (w.Chrome, w.Bounds, z);
    }

    public void ShowModal(WasmWindowHandle handle) => _modal.Add(handle.Id);

    public void EndModal(WasmWindowHandle handle) => _modal.Remove(handle.Id);

    public WasmBounds ClientToScreen(WasmWindowHandle handle, WasmBounds client) => client;

    public WasmBounds ScreenToClient(WasmWindowHandle handle, WasmBounds screen) => screen;
}

/// <summary>Fan-out input router: normalizes DOM events into <see cref="WasmInputEvent"/>.</summary>
public sealed class WasmInputRouter : IWasmInputRouter
{
    public event Action<WasmInputEvent>? Routed;

    public void DispatchPointer(WasmPointerArgs args) => Routed?.Invoke(new WasmInputEvent.Pointer(args));

    public void DispatchKeyboard(WasmKeyArgs args) => Routed?.Invoke(new WasmInputEvent.Key(args));

    public void DispatchWheel(WasmWheelArgs args) => Routed?.Invoke(new WasmInputEvent.Wheel(args));
}

/// <summary>In-memory clipboard (browser host replaces with navigator.clipboard impl).</summary>
public sealed class InMemoryClipboard : IWasmClipboard
{
    private string? _text;

    public Task<string?> GetTextAsync(CancellationToken ct = default) => Task.FromResult(_text);

    public Task SetTextAsync(string text, CancellationToken ct = default)
    {
        _text = text;
        return Task.CompletedTask;
    }
}

/// <summary>Recording native shim: virtual HDCs, diagnostics for unimplemented calls.</summary>
public sealed class WasmNativeShim : INativeShim
{
    private int _nextHdc = 0x1000;
    private readonly List<string> _unimplemented = new();

    public IReadOnlyList<string> UnimplementedCalls => _unimplemented;

    public nint GetDC(WasmWindowHandle window) => (nint)_nextHdc++;

    public void ReleaseDC(WasmWindowHandle window, nint hdc) { }

    public void InvalidateRect(WasmWindowHandle window, WasmBounds rect) { }

    public int GetSystemMetrics(int index)
    {
        _unimplemented.Add($"GetSystemMetrics({index})");
        return 0;
    }
}
