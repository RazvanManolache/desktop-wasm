using System.ComponentModel;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-91 AvalonEdit weak-event regression tests: the
/// PropertyChangedEventManager contract the converted TextEditor and
/// LineNumberMargin subscribe through (AddListener/RemoveListener with an
/// INotifyPropertyChanged source, an IWeakEventListener, and a property
/// name). Member shapes verified with Mono.Cecil against the 10.0.13
/// WindowsBase runtime pack plus a live net10.0-windows runtime probe (null
/// guards, NRE-on-null-source handler attach, per-entry duplicate delivery
/// with single removes, empty-name and null/empty-raised broadcast, shared
/// single subscription, sender/args identity). No Application state is
/// touched, so these stay outside the serialized ApplicationState collection.
/// </summary>
[ResetAmbientState]
public sealed class Slice91PropertyChangedTests
{
    [Fact]
    public void Listener_null_guards_match_live_probe()
    {
        var source = new Npc();
        var listener = new RecordingListener();
        Assert.Throws<ArgumentNullException>(() => PropertyChangedEventManager.AddListener(null!, listener, "P"));
        Assert.Throws<ArgumentNullException>(() => PropertyChangedEventManager.AddListener(source, null!, "P"));
        Assert.Throws<ArgumentNullException>(() => PropertyChangedEventManager.AddListener(source, listener, null!));
    }

    [Fact]
    public void RemoveListener_null_guards_match_live_probe()
    {
        var source = new Npc();
        var listener = new RecordingListener();
        PropertyChangedEventManager.RemoveListener(null!, listener, "P");
        Assert.Throws<ArgumentNullException>(() => PropertyChangedEventManager.RemoveListener(source, null!, "P"));
        Assert.Throws<ArgumentNullException>(() => PropertyChangedEventManager.RemoveListener(source, listener, null!));
    }

    [Fact]
    public void Handler_null_guards_match_live_probe()
    {
        var source = new Npc();
        EventHandler<PropertyChangedEventArgs> handler = (_, _) => { };
        Assert.Throws<NullReferenceException>(() => PropertyChangedEventManager.AddHandler(null!, handler, "P"));
        Assert.Throws<ArgumentNullException>(() => PropertyChangedEventManager.AddHandler(source, null!, "P"));
        Assert.Throws<ArgumentNullException>(() => PropertyChangedEventManager.AddHandler(source, handler, null!));
    }

    [Fact]
    public void RemoveHandler_null_guards_match_live_probe()
    {
        var source = new Npc();
        EventHandler<PropertyChangedEventArgs> handler = (_, _) => { };
        PropertyChangedEventManager.RemoveHandler(null!, handler, "P");
        Assert.Throws<ArgumentNullException>(() => PropertyChangedEventManager.RemoveHandler(source, null!, "P"));
        Assert.Throws<ArgumentNullException>(() => PropertyChangedEventManager.RemoveHandler(source, handler, null!));
    }

    [Fact]
    public void Delivery_filters_by_property_name()
    {
        var source = new Npc();
        var specific = new RecordingListener();
        var all = new RecordingListener();
        PropertyChangedEventManager.AddListener(source, specific, "P");
        PropertyChangedEventManager.AddListener(source, all, "");
        try
        {
            source.Raise("P");
            Assert.Equal(1, specific.Count);
            Assert.Equal(1, all.Count);
            source.Raise("Q");
            Assert.Equal(1, specific.Count);
            Assert.Equal(2, all.Count);
        }
        finally
        {
            PropertyChangedEventManager.RemoveListener(source, specific, "P");
            PropertyChangedEventManager.RemoveListener(source, all, "");
        }
    }

    [Fact]
    public void Null_and_empty_raised_names_broadcast()
    {
        var source = new Npc();
        var specific = new RecordingListener();
        var all = new RecordingListener();
        PropertyChangedEventManager.AddListener(source, specific, "P");
        PropertyChangedEventManager.AddListener(source, all, "");
        try
        {
            source.RaiseNull();
            Assert.Equal(1, specific.Count);
            Assert.Equal(1, all.Count);
            source.Raise("");
            Assert.Equal(2, specific.Count);
            Assert.Equal(2, all.Count);
        }
        finally
        {
            PropertyChangedEventManager.RemoveListener(source, specific, "P");
            PropertyChangedEventManager.RemoveListener(source, all, "");
        }
    }

    [Fact]
    public void Listener_receives_real_sender_and_manager_type()
    {
        var source = new Npc();
        var listener = new RecordingListener();
        PropertyChangedEventManager.AddListener(source, listener, "P");
        try
        {
            source.Raise("P");
            Assert.Same(source, listener.LastSender);
            Assert.Equal(typeof(PropertyChangedEventManager), listener.LastManager);
        }
        finally
        {
            PropertyChangedEventManager.RemoveListener(source, listener, "P");
        }
    }

    [Fact]
    public void Duplicate_adds_deliver_per_entry_and_single_removes()
    {
        var source = new Npc();
        var listener = new RecordingListener();
        PropertyChangedEventManager.AddListener(source, listener, "P");
        PropertyChangedEventManager.AddListener(source, listener, "P");
        source.Raise("P");
        Assert.Equal(2, listener.Count);
        PropertyChangedEventManager.RemoveListener(source, listener, "P");
        listener.Count = 0;
        source.Raise("P");
        Assert.Equal(1, listener.Count);
        PropertyChangedEventManager.RemoveListener(source, listener, "P");
        listener.Count = 0;
        source.Raise("P");
        Assert.Equal(0, listener.Count);
    }

    [Fact]
    public void Remove_for_never_added_is_silent()
    {
        var source = new Npc();
        PropertyChangedEventManager.RemoveListener(source, new RecordingListener(), "Nope");
        PropertyChangedEventManager.RemoveHandler(source, (_, _) => { }, "Nope");
    }

    [Fact]
    public void Handler_delivery_filters_and_removes()
    {
        var source = new Npc();
        int count = 0;
        object? sender = null;
        PropertyChangedEventArgs? args = null;
        EventHandler<PropertyChangedEventArgs> handler = (s, e) => { count++; sender = s; args = e; };
        PropertyChangedEventManager.AddHandler(source, handler, "P");
        try
        {
            source.Raise("Q");
            Assert.Equal(0, count);
            source.Raise("P");
            Assert.Equal(1, count);
            Assert.Same(source, sender);
            Assert.Equal("P", args!.PropertyName);
        }
        finally
        {
            PropertyChangedEventManager.RemoveHandler(source, handler, "P");
        }
        source.Raise("P");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Handler_empty_name_gets_everything_and_duplicates_remove_one_each()
    {
        var source = new Npc();
        int count = 0;
        EventHandler<PropertyChangedEventArgs> handler = (_, _) => count++;
        PropertyChangedEventManager.AddHandler(source, handler, "");
        PropertyChangedEventManager.AddHandler(source, handler, "");
        source.Raise("Q");
        Assert.Equal(2, count);
        PropertyChangedEventManager.RemoveHandler(source, handler, "");
        count = 0;
        source.Raise("Q");
        Assert.Equal(1, count);
        PropertyChangedEventManager.RemoveHandler(source, handler, "");
        count = 0;
        source.Raise("Q");
        Assert.Equal(0, count);
    }

    [Fact]
    public void Subscription_attaches_once_and_detaches_on_last_remove()
    {
        var source = new Npc();
        Assert.Equal(0, source.SubscriberCount);
        var first = new RecordingListener();
        var second = new RecordingListener();
        PropertyChangedEventManager.AddListener(source, first, "P");
        Assert.Equal(1, source.SubscriberCount);
        PropertyChangedEventManager.AddListener(source, second, "Q");
        Assert.Equal(1, source.SubscriberCount);
        PropertyChangedEventManager.RemoveListener(source, first, "P");
        Assert.Equal(1, source.SubscriberCount);
        PropertyChangedEventManager.RemoveListener(source, second, "Q");
        Assert.Equal(0, source.SubscriberCount);
    }

    [Fact]
    public void False_answer_unsubscribes_per_engine_contract()
    {
        // Live WPF FailFasts the process when a listener answers false; the
        // engine honors the base WeakEventManager contract (unsubscribe) and
        // pins that instead.
        var source = new Npc();
        var oneShot = new OneShotListener();
        PropertyChangedEventManager.AddListener(source, oneShot, "P");
        source.Raise("P");
        source.Raise("P");
        Assert.Equal(1, oneShot.Count);
        Assert.Equal(0, source.SubscriberCount);
    }

    [Fact]
    public void Collected_listener_stops_receiving()
    {
        var source = new Npc();
        var weak = TrackCollectedListener(source);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var probe = new RecordingListener();
        PropertyChangedEventManager.AddListener(source, probe, "P");
        try
        {
            source.Raise("P");
            Assert.False(weak.IsAlive);
            Assert.Equal(1, probe.Count);
        }
        finally
        {
            PropertyChangedEventManager.RemoveListener(source, probe, "P");
        }
    }

    [Fact]
    public void Current_manager_is_a_registered_singleton()
    {
        var first = WeakEventManager.GetCurrentManager(typeof(PropertyChangedEventManager));
        var second = WeakEventManager.GetCurrentManager(typeof(PropertyChangedEventManager));
        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.IsType<PropertyChangedEventManager>(first);
    }

    private static WeakReference TrackCollectedListener(Npc source)
    {
        var listener = new RecordingListener();
        PropertyChangedEventManager.AddListener(source, listener, "P");
        var weak = new WeakReference(listener);
        listener = null!;
        return weak;
    }

    private sealed class Npc : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public int SubscriberCount => PropertyChanged?.GetInvocationList().Length ?? 0;
        public void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public void RaiseNull() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private sealed class RecordingListener : IWeakEventListener
    {
        public int Count;
        public object? LastSender;
        public Type? LastManager;
        public bool ReceiveWeakEvent(Type managerType, object? sender, EventArgs e)
        {
            Count++;
            LastSender = sender;
            LastManager = managerType;
            return true;
        }
    }

    private sealed class OneShotListener : IWeakEventListener
    {
        public int Count;
        public bool ReceiveWeakEvent(Type managerType, object? sender, EventArgs e)
        {
            Count++;
            return false;
        }
    }
}
