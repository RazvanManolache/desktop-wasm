using DesktopWasm.Core;

namespace DesktopWasm.Wpf;

// Threading facade (WindowsBase subset, shim, not port): DispatcherObject,
// DispatcherPriority (generated values, see Step 37 roadmap entry) and a
// Dispatcher over the shared single-threaded WasmDispatcher. Priorities are
// accepted and documented but not ordered (one pump queue); there is no
// shutdown (HasShutdownStarted is always false). BeginInvoke returns the
// untyped operation; the generic InvokeAsync&lt;TResult&gt; returns a typed
// DispatcherOperation&lt;TResult&gt; (the plain InvokeAsync(Action) still
// returns the Task directly).

/// <summary>Dispatch priorities (matches System.Windows.Threading.DispatcherPriority).</summary>

public enum DispatcherPriority
{
    Invalid = -1,
    Inactive = 0,
    SystemIdle = 1,
    ApplicationIdle = 2,
    ContextIdle = 3,
    Background = 4,
    Input = 5,
    Loaded = 6,
    Render = 7,
    DataBind = 8,
    Normal = 9,
    Send = 10,
}

/// <summary>Thread affinity root (matches System.Windows.Threading.DispatcherObject).</summary>
public class DispatcherObject
{
    public Dispatcher Dispatcher => Dispatcher.CurrentDispatcher;

    public bool CheckAccess() => WasmDispatcher.Current.CheckAccess();

    public void VerifyAccess() => WasmDispatcher.Current.VerifyAccess();
}

/// <summary>Single-threaded dispatcher (matches System.Windows.Threading.Dispatcher shape).</summary>
public sealed class Dispatcher : DispatcherObject
{
    private static Dispatcher? _current;

    private Dispatcher() { }

    public static Dispatcher CurrentDispatcher => _current ??= new Dispatcher();

    /// <summary>
    /// Thread dispatcher lookup (matches Dispatcher.FromThread; converted
    /// boot code resolves it for the current thread). The single pump owns
    /// the calling thread, so other threads resolve null like WPF threads
    /// without a dispatcher.
    /// </summary>
    public static Dispatcher? FromThread(Thread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        return ReferenceEquals(thread, Thread.CurrentThread) ? CurrentDispatcher : null;
    }

    private static WasmDispatcher Pump => WasmDispatcher.Current;

    public bool HasShutdownStarted => false;

    public event DispatcherUnhandledExceptionEventHandler? UnhandledException;

    /// <summary>
    /// Shutdown-started hook (matches Dispatcher.ShutdownStarted; converted
    /// shell parameter caches subscribe in their static constructor). The
    /// browser pump never shuts down, so it never fires, like
    /// HasShutdownStarted staying false.
    /// </summary>
    public event EventHandler? ShutdownStarted;

    public void Invoke(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        try { Pump.Invoke(callback); }
        catch (Exception ex) when (RaiseUnhandled(ex)) { }
    }

    /// <summary>
    /// Priority-tagged sync post (matches Dispatcher.Invoke(Action,
    /// DispatcherPriority); priorities ride the single pump queue unordered,
    /// like every other priority member here).
    /// </summary>
    public void Invoke(Action callback, DispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(callback);
        try { Pump.Invoke(callback); }
        catch (Exception ex) when (RaiseUnhandled(ex)) { }
    }

    public TResult Invoke<TResult>(Func<TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        try { return Pump.Invoke(callback); }
        catch (Exception ex) when (RaiseUnhandled(ex)) { return default!; }
    }

    /// <summary>
    /// Priority-tagged typed sync post (matches
    /// Dispatcher.Invoke(Func&lt;TResult&gt;, DispatcherPriority); converted
    /// AvalonDock floating windows bind it for activation).
    /// </summary>
    public TResult Invoke<TResult>(Func<TResult> callback, DispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(callback);
        try { return Pump.Invoke(callback); }
        catch (Exception ex) when (RaiseUnhandled(ex)) { return default!; }
    }

    public DispatcherOperation BeginInvoke(Delegate method, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(method);
        var operation = new DispatcherOperation(this, DispatcherPriority.Normal, method, args);
        Pump.BeginInvoke(operation.Execute);
        return operation;
    }

    public DispatcherOperation BeginInvoke(DispatcherPriority priority, Delegate method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var operation = new DispatcherOperation(this, priority, method, []);
        Pump.BeginInvoke(operation.Execute);
        return operation;
    }

    public DispatcherOperation BeginInvoke(Delegate method, DispatcherPriority priority, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(method);
        var operation = new DispatcherOperation(this, priority, method, args);
        Pump.BeginInvoke(operation.Execute);
        return operation;
    }

    public Task InvokeAsync(Action callback) => Pump.InvokeAsync(callback);

    /// <summary>
    /// Typed async post (matches Dispatcher.InvokeAsync&lt;TResult&gt;(Func&lt;TResult&gt;)):
    /// the returned operation carries a Task&lt;TResult&gt;, which is the single
    /// member PC Space Map consumes (DispatcherOperation`1.get_Task).
    /// </summary>
    public DispatcherOperation<TResult> InvokeAsync<TResult>(Func<TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var operation = new DispatcherOperation<TResult>(this, DispatcherPriority.Normal, callback);
        Pump.BeginInvoke(operation.Execute);
        return operation;
    }

    internal int PumpQueue(int maxActions) => Pump.Pump(maxActions);

    internal bool RaiseUnhandled(Exception exception)
    {
        var handler = UnhandledException;
        if (handler is null) return false;
        var args = new DispatcherUnhandledExceptionEventArgs(this, exception);
        handler(this, args);
        return args.Handled;
    }
}

/// <summary>
/// UI-thread context (matches
/// System.Windows.Threading.DispatcherSynchronizationContext): posts ride
/// the dispatcher pump, sends run inline on the single engine thread, and
/// copies mint a fresh instance, like WPF.
/// </summary>
public sealed class DispatcherSynchronizationContext : SynchronizationContext
{
    /// <inheritdoc/>
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => d(state)));
    }

    /// <inheritdoc/>
    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        d(state);
    }

    /// <inheritdoc/>
    public override SynchronizationContext CreateCopy() => new DispatcherSynchronizationContext();
}

/// <summary>Dispatch status (matches System.Windows.Threading.DispatcherOperationStatus).</summary>
public enum DispatcherOperationStatus
{
    Pending = 0,
    Aborted = 1,
    Completed = 2,
    Executing = 3,
}

/// <summary>Queued dispatch unit (matches System.Windows.Threading.DispatcherOperation shape).</summary>
public class DispatcherOperation
{
    private readonly TaskCompletionSource<object?> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private DispatcherOperationStatus _status = DispatcherOperationStatus.Pending;
    private bool _aborted;

    internal DispatcherOperation(Dispatcher dispatcher, DispatcherPriority priority, Delegate method, object?[] args)
    {
        Dispatcher = dispatcher;
        Priority = priority;
        Method = method;
        Args = args;
    }

    public Dispatcher Dispatcher { get; }
    public DispatcherPriority Priority { get; set; }
    public Delegate Method { get; }

    public DispatcherOperationStatus Status
    {
        get { lock (_gate) return _status; }
    }

    public Task Task => _done.Task;

    internal Task<object?> TaskCore => _done.Task;

    /// <summary>Operation result (rethrows a faulted operation).</summary>
    public object? Result => TaskCore.GetAwaiter().GetResult();

    /// <summary>Blocks the loop thread while pumping (matches Wait).</summary>
    public bool Wait()
    {
        while (!_done.Task.IsCompleted) Dispatcher.PumpQueue(1);
        return Status == DispatcherOperationStatus.Completed;
    }

    /// <summary>Bounded wait: pumps until done or the timeout elapses.</summary>
    public bool Wait(TimeSpan timeout)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!_done.Task.IsCompleted)
        {
            if (clock.Elapsed > timeout) return false;
            Dispatcher.PumpQueue(1);
        }
        return Status == DispatcherOperationStatus.Completed;
    }

    /// <summary>Abandons a pending operation (matches Abort).</summary>
    public bool Abort()
    {
        EventHandler? aborted;
        lock (_gate)
        {
            if (_status != DispatcherOperationStatus.Pending) return false;
            _aborted = true;
            _status = DispatcherOperationStatus.Aborted;
            aborted = Aborted;
        }
        aborted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public event EventHandler? Aborted;
    public event EventHandler? Completed;

    internal void Execute()
    {
        lock (_gate)
        {
            if (_aborted || _status != DispatcherOperationStatus.Pending) return;
            _status = DispatcherOperationStatus.Executing;
        }
        try
        {
            var result = Method.DynamicInvoke(Args);
            SetResult(result);
        }
        catch (Exception ex)
        {
            var fault = ex is System.Reflection.TargetInvocationException tie && tie.InnerException is not null
                ? tie.InnerException
                : ex;
            SetException(fault);
        }
        Completed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Completes the operation (typed subclasses mirror into their own task).</summary>
    protected virtual void SetResult(object? result)
    {
        lock (_gate) _status = DispatcherOperationStatus.Completed;
        _done.TrySetResult(result);
    }

    /// <summary>Faults the operation, honoring the dispatcher unhandled filter (typed subclasses mirror).</summary>
    protected virtual void SetException(Exception fault)
    {
        lock (_gate) _status = DispatcherOperationStatus.Completed;
        var handled = Dispatcher.RaiseUnhandled(fault);
        if (handled) _done.TrySetResult(null);
        else _done.TrySetException(fault);
        OnFaultSettled(handled, fault);
    }

    /// <summary>Settles a typed mirror after the single filter evaluation.</summary>
    protected virtual void OnFaultSettled(bool handled, Exception fault)
    {
    }

    private object?[] Args { get; }
}

/// <summary>
/// Typed dispatch unit (matches System.Windows.Threading.DispatcherOperation&lt;TResult&gt;,
/// verified against live .NET reflection: sealed, derives the untyped operation,
/// carries a Task&lt;TResult&gt; plus a typed Result and awaiter).
/// </summary>
public sealed class DispatcherOperation<TResult> : DispatcherOperation
{
    private readonly TaskCompletionSource<TResult> _typed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal DispatcherOperation(Dispatcher dispatcher, DispatcherPriority priority, Func<TResult> method)
        : base(dispatcher, priority, method, [])
    {
    }

    /// <summary>Typed operation result (rethrows a faulted operation).</summary>
    public new TResult Result => _typed.Task.GetAwaiter().GetResult();

    /// <summary>Typed completion task (the member converted PC Space Map awaits).</summary>
    public new Task<TResult> Task => _typed.Task;

    /// <summary>Enables await on the operation itself (matches the framework).</summary>
    public new System.Runtime.CompilerServices.TaskAwaiter<TResult> GetAwaiter() => _typed.Task.GetAwaiter();

    protected override void SetResult(object? result)
    {
        base.SetResult(result);
        _typed.TrySetResult((TResult)result!);
    }

    protected override void OnFaultSettled(bool handled, Exception fault)
    {
        // Mirror the single base filter evaluation: a handled fault completes
        // the base task with default(object), so the typed task completes
        // with default(TResult) rather than faulting.
        if (handled) _typed.TrySetResult(default!);
        else _typed.TrySetException(fault);
    }
}

/// <summary>
/// Dispatcher helpers (matches System.Windows.Threading.DispatcherExtensions).
/// Slice-69 MaterialDesignThemes.Wpf prerequisite (converted code calls
/// Invoke(Dispatcher, Action) through the System.Windows.Presentation
/// facade scope; the converter redirects that scope onto this type).
/// Every helper forwards to the matching Dispatcher member, so priorities
/// ride the single pump queue unordered and cancellation/timeout overloads
/// stay out (no timed invoke exists in the engine).
/// </summary>
public static class DispatcherExtensions
{
    /// <summary>Synchronous post (matches DispatcherExtensions.Invoke(Dispatcher, Action)).</summary>
    public static void Invoke(this Dispatcher dispatcher, Action callback)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        dispatcher.Invoke(callback);
    }

    /// <summary>Priority-tagged synchronous post (matches DispatcherExtensions.Invoke(Dispatcher, Action, DispatcherPriority)).</summary>
    public static void Invoke(this Dispatcher dispatcher, Action callback, DispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        dispatcher.Invoke(callback, priority);
    }

    /// <summary>Queued post (matches DispatcherExtensions.BeginInvoke(Dispatcher, Action)).</summary>
    public static DispatcherOperation BeginInvoke(this Dispatcher dispatcher, Action callback)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return dispatcher.BeginInvoke(callback);
    }

    /// <summary>Priority-tagged queued post (matches DispatcherExtensions.BeginInvoke(Dispatcher, Action, DispatcherPriority)).</summary>
    public static DispatcherOperation BeginInvoke(this Dispatcher dispatcher, Action callback, DispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return dispatcher.BeginInvoke(callback, priority);
    }

    /// <summary>Queued post with an operation handle (matches DispatcherExtensions.InvokeAsync(Dispatcher, Action)).</summary>
    public static DispatcherOperation InvokeAsync(this Dispatcher dispatcher, Action callback)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return dispatcher.BeginInvoke(callback);
    }

    /// <summary>Priority-tagged queued post with an operation handle (matches DispatcherExtensions.InvokeAsync(Dispatcher, Action, DispatcherPriority)).</summary>
    public static DispatcherOperation InvokeAsync(this Dispatcher dispatcher, Action callback, DispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return dispatcher.BeginInvoke(callback, priority);
    }
}

/// <summary>Dispatch event root (matches System.Windows.Threading.DispatcherEventArgs).</summary>
public class DispatcherEventArgs : EventArgs
{
    internal DispatcherEventArgs(Dispatcher dispatcher) => Dispatcher = dispatcher;

    public Dispatcher Dispatcher { get; }
}

/// <summary>Unhandled dispatch failure (matches DispatcherUnhandledExceptionEventArgs).</summary>
public sealed class DispatcherUnhandledExceptionEventArgs : DispatcherEventArgs
{
    internal DispatcherUnhandledExceptionEventArgs(Dispatcher dispatcher, Exception exception)
        : base(dispatcher) => Exception = exception;

    public Exception Exception { get; }

    public bool Handled { get; set; }
}

/// <summary>Unhandled dispatch handler (matches DispatcherUnhandledExceptionEventHandler).</summary>
public delegate void DispatcherUnhandledExceptionEventHandler(object sender, DispatcherUnhandledExceptionEventArgs e);

/// <summary>
/// Dispatcher work item (matches System.Windows.Threading.DispatcherOperationCallback):
/// the object-to-object delegate converted AvalonDock code posts through
/// Dispatcher.BeginInvoke(DispatcherPriority, Delegate) for window teardown.
/// The engine BeginInvoke(Delegate) path invokes it dynamically and completes
/// the untyped operation, like the real dispatcher.
/// </summary>
public delegate object DispatcherOperationCallback(object arg);

/// <summary>
/// Interval timer on the dispatcher queue (matches DispatcherTimer). Ticks
/// are raised from <see cref="WasmWpfHost.Tick"/> against wall time and from
/// <see cref="TickAll"/> in tests; never from a background thread.
/// </summary>
public class DispatcherTimer
{
    private static readonly HashSet<DispatcherTimer> Active = new();
    private static readonly object ActiveGate = new();

    private DateTime _last = DateTime.UtcNow;
    private TimeSpan _interval = TimeSpan.Zero;
    private bool _enabled;

    public DispatcherTimer() { }

    public DispatcherTimer(TimeSpan interval, EventHandler tick, DispatcherPriority priority, Dispatcher dispatcher)
    {
        Interval = interval;
        Tick += tick;
        Dispatcher = dispatcher;
    }

    public DispatcherTimer(DispatcherPriority priority) => Dispatcher = Dispatcher.CurrentDispatcher;

    public DispatcherTimer(DispatcherPriority priority, Dispatcher dispatcher) => Dispatcher = dispatcher;

    public Dispatcher Dispatcher { get; } = Dispatcher.CurrentDispatcher;

    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            if (value < TimeSpan.Zero && value != System.Threading.Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(value), "Interval cannot be negative (Timeout.Infinite disables).");
            _interval = value;
        }
    }

    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (value) Start();
            else Stop();
        }
    }

    public object? Tag { get; set; }

    public event EventHandler? Tick;

    public void Start()
    {
        lock (ActiveGate) Active.Add(this);
        _last = DateTime.UtcNow;
        _enabled = true;
    }

    public void Stop()
    {
        lock (ActiveGate) Active.Remove(this);
        _enabled = false;
    }

    /// <summary>Host entry point (called from WasmWpfHost.Tick).</summary>
    public static void TickAll() => TickAll(DateTime.UtcNow);

    internal static void TickAll(DateTime utcNow)
    {
        DispatcherTimer[] snapshot;
        lock (ActiveGate) snapshot = new DispatcherTimer[Active.Count];
        lock (ActiveGate) Active.CopyTo(snapshot);
        foreach (var timer in snapshot)
        {
            if (!timer._enabled) continue;
            if (timer._interval > TimeSpan.Zero && utcNow - timer._last < timer._interval) continue;
            timer._last = utcNow;
            timer.Tick?.Invoke(timer, EventArgs.Empty);
        }
    }
}

/// <summary>Interop message hook (matches System.Windows.Interop.ComponentDispatcher).</summary>
public static class ComponentDispatcher
{
    public static event ThreadMessageEventHandler? ThreadFilterMessage;
    public static event ThreadMessageEventHandler? ThreadPreprocessMessage;

    private static int _modalCount;

    public static bool IsThreadModal => _modalCount > 0;

    public static void PushModal() => _modalCount++;

    public static void PopModal()
    {
        if (_modalCount > 0) _modalCount--;
    }

    internal static bool RaiseFilter(ThreadMessageEventArgs args)
    {
        ThreadFilterMessage?.Invoke(args);
        return args.Handled;
    }

    internal static void RaisePreprocess(ThreadMessageEventArgs args) => ThreadPreprocessMessage?.Invoke(args);
}

/// <summary>Interop message data (matches ThreadMessageEventArgs; flat fields, no Win32 MSG).</summary>
public sealed class ThreadMessageEventArgs : EventArgs
{
    public ThreadMessageEventArgs(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam)
    {
        Hwnd = hwnd;
        Message = message;
        WParam = wParam;
        LParam = lParam;
    }

    public IntPtr Hwnd { get; }
    public int Message { get; }
    public IntPtr WParam { get; }
    public IntPtr LParam { get; }
    public bool Handled { get; set; }
}

/// <summary>Interop message handler (matches ThreadMessageEventHandler).</summary>
public delegate void ThreadMessageEventHandler(ThreadMessageEventArgs e);
