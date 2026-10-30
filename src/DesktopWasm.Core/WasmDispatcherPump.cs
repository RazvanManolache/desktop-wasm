namespace DesktopWasm.Core;

/// <summary>
/// Single-threaded UI queue (cf. WfWasm.Shim.BrowserMessageLoop).
/// The constructing thread owns the loop (STA-like): only it may <see cref="Pump"/>.
/// </summary>
public sealed class WasmDispatcherPump : IWasmMessagePump
{
    private readonly int _ownerThreadId;
    private readonly Queue<WorkItem> _queue = new();
    private readonly object _gate = new();
    private readonly List<Action<FrameContext>> _frameCallbacks = new();

    public WasmDispatcherPump() => _ownerThreadId = Environment.CurrentManagedThreadId;

    public bool InvokeRequired => Environment.CurrentManagedThreadId != _ownerThreadId;

    public int PendingCount
    {
        get { lock (_gate) return _queue.Count; }
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Enqueue(new WorkItem(action, null));
    }

    public Task PostAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var done = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new WorkItem(action, done));
        return done.Task;
    }

    public Task<T> PostAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new WorkItem(() =>
        {
            try { done.TrySetResult(func()); }
            catch (Exception ex) { done.TrySetException(ex); }
        }, null));
        return done.Task;
    }

    public T Send<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (!InvokeRequired)
            return func();
        return PostAsync(func).GetAwaiter().GetResult();
    }

    public void Send(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Send<object?>(() => { action(); return null; });
    }

    public int Pump(int maxActions = int.MaxValue)
    {
        if (InvokeRequired)
            throw new InvalidOperationException("Pump must run on the loop thread.");
        if (maxActions < 0)
            throw new ArgumentOutOfRangeException(nameof(maxActions));

        var ran = 0;
        while (ran < maxActions)
        {
            WorkItem next;
            lock (_gate)
            {
                if (_queue.Count == 0)
                    break;
                next = _queue.Dequeue();
            }

            next.Execute();
            ran++;
        }

        return ran;
    }

    public void RequestFrame(Action<FrameContext> onFrame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        lock (_gate) _frameCallbacks.Add(onFrame);
    }

    /// <summary>
    /// Headless/test driver: runs queued work then invokes frame callbacks.
    /// The browser host calls this from requestAnimationFrame instead.
    /// </summary>
    public void Tick(FrameContext ctx)
    {
        Pump();
        List<Action<FrameContext>> callbacks;
        lock (_gate) callbacks = new List<Action<FrameContext>>(_frameCallbacks);
        foreach (var cb in callbacks) cb(ctx);
    }

    private void Enqueue(WorkItem item)
    {
        lock (_gate) _queue.Enqueue(item);
    }

    private sealed record WorkItem(Action Action, TaskCompletionSource<object?>? Completion)
    {
        public void Execute()
        {
            try
            {
                Action();
                Completion?.TrySetResult(null);
            }
            catch (Exception ex)
            {
                if (Completion is null) throw;
                Completion.TrySetException(ex);
            }
        }
    }
}
