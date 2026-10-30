namespace DesktopWasm.Core;

/// <summary>Per-frame context supplied by the requestAnimationFrame tick.</summary>
public readonly record struct FrameContext(int FrameSeq, double TimestampMs);

/// <summary>
/// Unified non-blocking async pump shared by WinForms Application.Run and
/// WPF Dispatcher. Port of WfWasm.Shim.BrowserMessageLoop semantics:
/// the constructing thread owns the loop (STA-like).
/// </summary>
public interface IWasmMessagePump
{
    bool InvokeRequired { get; }
    int PendingCount { get; }
    void Post(Action action);
    Task PostAsync(Action action);
    Task<T> PostAsync<T>(Func<T> func);
    T Send<T>(Func<T> func);
    void Send(Action action);
    int Pump(int maxActions = int.MaxValue);

    /// <summary>
    /// Registers a per-frame callback. The browser host wires this to
    /// requestAnimationFrame; headless/tests invoke it manually.
    /// </summary>
    void RequestFrame(Action<FrameContext> onFrame);
}
