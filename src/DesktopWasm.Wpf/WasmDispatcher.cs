using DesktopWasm.Core;

namespace DesktopWasm.Wpf;

/// <summary>
/// WPF Dispatcher over the shared IWasmMessagePump (single-threaded WASM:
/// no background render thread; layout + composition run on the UI thread).
/// </summary>
public sealed class WasmDispatcher
{
    private readonly IWasmMessagePump _pump;

    public WasmDispatcher(IWasmMessagePump pump) => _pump = pump;

    public static WasmDispatcher Current { get; set; } = new(new WasmDispatcherPump());

    public bool CheckAccess() => !_pump.InvokeRequired;

    public void VerifyAccess()
    {
        if (_pump.InvokeRequired) throw new InvalidOperationException("Must run on the UI thread.");
    }

    public void Invoke(Action action) => _pump.Send(action);

    public T Invoke<T>(Func<T> func) => _pump.Send(func);

    public Task InvokeAsync(Action action) => _pump.PostAsync(action);

    public void BeginInvoke(Action action) => _pump.Post(action);

    internal int Pump(int maxActions) => _pump.Pump(maxActions);
}
