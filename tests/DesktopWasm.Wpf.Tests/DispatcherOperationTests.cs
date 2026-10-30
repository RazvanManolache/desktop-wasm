namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-56 regression: the typed dispatch unit. PC Space Map posts work
/// with Dispatcher.InvokeAsync&lt;TResult&gt;(Func&lt;TResult&gt;) and consumes
/// DispatcherOperation&lt;TResult&gt;.Task (verified member-by-member against
/// the upstream net9.0-windows binary: BeginInvoke(Delegate, object[]),
/// BeginInvoke(Delegate, DispatcherPriority, object[]),
/// InvokeAsync(Func&lt;T&gt;), DispatcherObject.get_Dispatcher,
/// DispatcherOperation`1.get_Task). The generic operation mirrors the
/// framework shape (sealed, derives the untyped operation, new typed
/// Result/Task plus an awaiter) over the same single-threaded pump, with one
/// shared unhandled-filter evaluation for both tasks.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class DispatcherOperationTests
{
    public DispatcherOperationTests()
    {
        // Rebind the loop pump to this thread: statics bind thread affinity
        // at first touch, and xunit does not pin test threads.
        WasmDispatcher.Current = new WasmDispatcher(new DesktopWasm.Core.WasmDispatcherPump());
    }

    [Fact]
    public void FromThread_resolves_the_current_thread_dispatcher()
    {
        // Demo boot shape: converted code calls
        // Dispatcher.FromThread(Thread.CurrentThread). The single pump owns
        // the current thread, so it resolves there and stays null
        // elsewhere, like WPF threads without a dispatcher.
        Assert.Same(Dispatcher.CurrentDispatcher, Dispatcher.FromThread(Thread.CurrentThread));
        var main = Thread.CurrentThread;
        Dispatcher? foreign = Dispatcher.CurrentDispatcher;
        var worker = new Thread(() => { foreign = Dispatcher.FromThread(main); });
        worker.Start();
        worker.Join();
        Assert.Null(foreign);
    }

    [Fact]
    public void Dispatcher_synchronization_context_posts_through_the_pump()
    {
        // Demo MainWindow shape: converted code calls
        // TaskScheduler.FromCurrentSynchronizationContext(). Posts ride the
        // dispatcher pump and sends run inline, like the UI thread.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            Assert.NotNull(TaskScheduler.FromCurrentSynchronizationContext());
            var ran = 0;
            SynchronizationContext.Current!.Post(_ => ran++, null);
            Dispatcher.CurrentDispatcher.PumpQueue(10);
            Assert.Equal(1, ran);
            SynchronizationContext.Current!.Send(_ => ran++, null);
            Assert.Equal(2, ran);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void Boot_installs_dispatcher_synchronization_context_when_missing()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            WpfAppBoot.EnsureDispatcherSynchronizationContext();
            Assert.IsType<DispatcherSynchronizationContext>(SynchronizationContext.Current);
            var custom = new SynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(custom);
            WpfAppBoot.EnsureDispatcherSynchronizationContext();
            Assert.Same(custom, SynchronizationContext.Current);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void Typed_operation_matches_the_framework_shape()
    {
        var operationType = typeof(DispatcherOperation<>);
        const System.Reflection.BindingFlags Declared =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.DeclaredOnly;
        Assert.Equal("DesktopWasm.Wpf", operationType.Namespace);
        Assert.Equal("DispatcherOperation`1", operationType.Name);
        Assert.True(operationType.IsSealed);
        Assert.Equal(typeof(DispatcherOperation), operationType.BaseType);
        // DeclaredOnly: like the framework, the typed members hide (new) the
        // untyped ones, so a flat name lookup is ambiguous by design.
        var closed = operationType.MakeGenericType(typeof(int));
        Assert.Equal(typeof(int), closed.GetProperty("Result", Declared)!.PropertyType);
        Assert.Equal(typeof(Task<int>), closed.GetProperty("Task", Declared)!.PropertyType);
        Assert.NotNull(closed.GetMethod("GetAwaiter", Declared));
    }

    [Fact]
    public async Task InvokeAsync_returns_a_typed_operation_with_the_value()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var completed = 0;
        var operation = dispatcher.InvokeAsync(() => 41 + 1);
        Assert.IsType<DispatcherOperation<int>>(operation);
        Assert.IsAssignableFrom<DispatcherOperation>(operation);
        Assert.Equal(DispatcherOperationStatus.Pending, operation.Status);
        operation.Completed += (_, _) => completed++;
        // Wait pumps the single-threaded queue; the awaits below only
        // observe the already-completed tasks.
        Assert.True(operation.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(42, await operation.Task);
        Assert.Equal(DispatcherOperationStatus.Completed, operation.Status);
        Assert.Equal(42, operation.Result);
        Assert.Equal(1, completed);
        // The untyped mirror completes with the same boxed value.
        await ((DispatcherOperation)operation).Task;
        Assert.Equal(42, ((DispatcherOperation)operation).Result);
    }

    [Fact]
    public async Task Typed_operation_supports_the_awaiter()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var operation = dispatcher.InvokeAsync(() => "pc-space-map");
        Assert.True(operation.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal("pc-space-map", await operation);
        Assert.Equal(DispatcherOperationStatus.Completed, operation.Status);
    }

    [Fact]
    public void BeginInvoke_delegate_priority_args_overload_posts_with_args()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var operation = dispatcher.BeginInvoke((Func<int, int>)(x => x * 2), DispatcherPriority.Background, 21);
        Assert.Equal(DispatcherPriority.Background, operation.Priority);
        Assert.True(operation.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(42, operation.Result);
    }

    [Fact]
    public async Task Faulted_typed_operation_faults_both_tasks()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var operation = dispatcher.InvokeAsync<int>(() => throw new InvalidOperationException("boom"));
        Assert.True(operation.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(operation.Task.IsFaulted);
        Assert.Throws<InvalidOperationException>(() => operation.Result);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await operation.Task);
        Assert.True(((DispatcherOperation)operation).Task.IsFaulted);
    }

    [Fact]
    public async Task Handled_fault_completes_typed_task_with_default_exactly_once()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var seen = 0;
        void Handler(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            seen++;
            Assert.Equal("swallowed", e.Exception.Message);
            e.Handled = true;
        }
        dispatcher.UnhandledException += Handler;
        try
        {
            var operation = dispatcher.InvokeAsync<int>(() => throw new InvalidOperationException("swallowed"));
            Assert.True(operation.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(operation.Task.IsFaulted);
            Assert.Equal(0, await operation.Task);
            Assert.Equal(0, operation.Result);
            Assert.Equal(1, seen);
        }
        finally { dispatcher.UnhandledException -= Handler; }
    }

    [Fact]
    public void Aborted_typed_operation_never_runs()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var operation = dispatcher.InvokeAsync(() => 7);
        Assert.True(operation.Abort());
        Assert.Equal(DispatcherOperationStatus.Aborted, operation.Status);
        Assert.False(operation.Wait(TimeSpan.FromMilliseconds(50)));
        Assert.False(operation.Task.IsCompleted);
    }
}
