using System.ComponentModel;
using System.Reflection;

namespace DesktopWasm.Wpf;

// Slice-91: AvalonEdit weak-event subscriptions (verified member-by-member
// against the upstream net10.0-windows ICSharpCode.AvalonEdit.dll with
// Mono.Cecil plus the 10.0.13 WindowsBase runtime pack and a live
// net10.0-windows runtime probe).
//
// Converted call sites: TextEditor.OnDocumentChanged and
// LineNumberMargin.OnDocumentChanged call AddListener/RemoveListener with
// (INotifyPropertyChanged, IWeakEventListener, string); the lib's own
// PropertyChangedWeakEventManager routes through the engine WeakEventManager
// base. Live probe facts honored below: null source/listener/name throw
// ArgumentNullException on the listener statics; RemoveListener/RemoveHandler
// stay silent on a null source; AddHandler throws ArgumentNullException on a
// null handler/name but NullReferenceException on a null source (the attach
// casts without a guard); duplicate adds deliver per entry with single
// removes; empty-name subscriptions and null/empty raised names broadcast;
// handlers receive the real sender and args; attach is a single shared
// subscription dropped when the last entry leaves. One deliberate
// divergence: live WPF FailFasts the process when a listener answers false
// (observed 2026-10-27 probe); the engine honors the base contract and
// unsubscribes instead.

/// <summary>Weak per-property router (matches System.ComponentModel.PropertyChangedEventManager).</summary>
public class PropertyChangedEventManager : WeakEventManager
{
    private readonly Dictionary<object, List<Entry>> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly object _entriesLock = new();

    /// <summary>Manager singleton (matches the protected-internal CurrentManager).</summary>
    protected internal static PropertyChangedEventManager CurrentManager
    {
        get
        {
            var current = (PropertyChangedEventManager?)GetCurrentManager(typeof(PropertyChangedEventManager));
            if (current is null)
            {
                current = new PropertyChangedEventManager();
                SetCurrentManager(typeof(PropertyChangedEventManager), current);
            }
            return current;
        }
    }

    /// <summary>Subscribes a weak listener to one property (matches AddListener).</summary>
    public static void AddListener(INotifyPropertyChanged? source, IWeakEventListener? listener, string? propertyName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(propertyName);
        CurrentManager.PrivateAdd(source, propertyName, listener, null);
    }

    /// <summary>Unsubscribes one weak-listener entry (matches RemoveListener).</summary>
    public static void RemoveListener(INotifyPropertyChanged? source, IWeakEventListener? listener, string? propertyName)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(propertyName);
        if (source is null) return;
        CurrentManager.PrivateRemove(source, propertyName, listener, null);
    }

    /// <summary>Subscribes a handler weakly to one property (matches AddHandler).</summary>
    public static void AddHandler(INotifyPropertyChanged? source, EventHandler<PropertyChangedEventArgs>? handler, string? propertyName)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(propertyName);
        CurrentManager.PrivateAdd(source!, propertyName, null, handler);
    }

    /// <summary>Unsubscribes one handler entry (matches RemoveHandler).</summary>
    public static void RemoveHandler(INotifyPropertyChanged? source, EventHandler<PropertyChangedEventArgs>? handler, string? propertyName)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(propertyName);
        if (source is null) return;
        CurrentManager.PrivateRemove(source, propertyName, null, handler);
    }

    /// <summary>Attaches the delivery trampoline (matches StartListening).</summary>
    protected override void StartListening(object source)
    {
        ((INotifyPropertyChanged)source).PropertyChanged += OnPropertyChanged;
    }

    /// <summary>Detaches the delivery trampoline (matches StopListening).</summary>
    protected override void StopListening(object source)
    {
        ((INotifyPropertyChanged)source).PropertyChanged -= OnPropertyChanged;
    }

    private void PrivateAdd(INotifyPropertyChanged source, string propertyName, IWeakEventListener? listener, EventHandler<PropertyChangedEventArgs>? handler)
    {
        // The attach casts without a null guard: a null source reaches
        // StartListening and fails with NullReferenceException, like live
        // WPF (only the AddHandler static can pass null this far; the other
        // statics reject or return on null up front).
        var entry = Entry.For(listener, handler, propertyName);
        bool attach;
        lock (_entriesLock)
        {
            attach = source is null || !_entries.ContainsKey(source);
            if (source is not null)
            {
                if (!_entries.TryGetValue(source, out var list))
                {
                    list = new List<Entry>();
                    _entries[source] = list;
                }
                list.Add(entry);
                PurgeDead(list);
            }
        }
        if (attach) StartListening(source!);
        ScheduleCleanup();
    }

    private void PrivateRemove(INotifyPropertyChanged source, string propertyName, IWeakEventListener? listener, EventHandler<PropertyChangedEventArgs>? handler)
    {
        bool detach = false;
        lock (_entriesLock)
        {
            if (!_entries.TryGetValue(source, out var list)) return;
            PurgeDead(list);
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Matches(propertyName, listener, handler))
                {
                    list.RemoveAt(i);
                    break;
                }
            }
            PurgeDead(list);
            if (list.Count == 0)
            {
                _entries.Remove(source);
                detach = true;
            }
        }
        if (detach) StopListening(source);
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is null) return;
        Entry[] snapshot;
        lock (_entriesLock)
        {
            if (!_entries.TryGetValue(sender, out var list)) return;
            snapshot = list.ToArray();
        }
        var raised = args.PropertyName;
        bool broadcast = string.IsNullOrEmpty(raised);
        var dead = new List<Entry>();
        foreach (var entry in snapshot)
        {
            if (!broadcast && entry.PropertyName.Length != 0 &&
                !string.Equals(entry.PropertyName, raised, StringComparison.Ordinal))
                continue;
            if (!entry.Deliver(GetType(), sender, args))
                dead.Add(entry);
        }
        if (dead.Count == 0) return;
        bool detach = false;
        lock (_entriesLock)
        {
            if (!_entries.TryGetValue(sender, out var list)) return;
            foreach (var entry in dead)
                list.Remove(entry);
            PurgeDead(list);
            if (list.Count == 0)
            {
                _entries.Remove(sender);
                detach = true;
            }
        }
        if (detach) StopListening(sender);
    }

    /// <summary>Purges collected entries (matches ScheduleCleanup).</summary>
    protected override bool ScheduleCleanup()
    {
        bool detachSource = false;
        object? emptySource = null;
        lock (_entriesLock)
        {
            base.ScheduleCleanup();
            foreach (var kv in _entries)
            {
                PurgeDead(kv.Value);
                if (kv.Value.Count == 0)
                {
                    emptySource = kv.Key;
                    detachSource = true;
                    break;
                }
            }
            if (detachSource) _entries.Remove(emptySource!);
        }
        if (detachSource) StopListening(emptySource!);
        return true;
    }

    private static void PurgeDead(List<Entry> list)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (!list[i].IsAlive)
                list.RemoveAt(i);
        }
    }

    private sealed class Entry
    {
        public string PropertyName = "";
        public WeakReference<IWeakEventListener>? Listener;
        public WeakReference? HandlerTarget;
        public MethodInfo? HandlerMethod;
        public bool HandlerIsStatic;

        public static Entry For(IWeakEventListener? listener, EventHandler<PropertyChangedEventArgs>? handler, string propertyName)
        {
            var entry = new Entry { PropertyName = propertyName };
            if (listener is not null)
            {
                entry.Listener = new WeakReference<IWeakEventListener>(listener);
            }
            else
            {
                entry.HandlerMethod = handler!.Method;
                entry.HandlerIsStatic = handler.Method.IsStatic;
                if (!entry.HandlerIsStatic)
                    entry.HandlerTarget = new WeakReference(handler.Target);
            }
            return entry;
        }

        public bool IsAlive
        {
            get
            {
                if (Listener is not null) return Listener.TryGetTarget(out _);
                if (HandlerIsStatic) return true;
                return HandlerTarget!.Target is not null;
            }
        }

        public bool Matches(string propertyName, IWeakEventListener? listener, EventHandler<PropertyChangedEventArgs>? handler)
        {
            if (!string.Equals(PropertyName, propertyName, StringComparison.Ordinal)) return false;
            if (listener is not null)
                return Listener!.TryGetTarget(out var target) && ReferenceEquals(target, listener);
            if (handler!.Method.IsStatic != HandlerIsStatic) return false;
            if (HandlerIsStatic) return HandlerMethod!.Equals(handler.Method);
            var current = HandlerTarget!.Target;
            return current is not null && ReferenceEquals(current, handler.Target) && HandlerMethod!.Equals(handler.Method);
        }

        public bool Deliver(Type managerType, object sender, PropertyChangedEventArgs args)
        {
            if (Listener is not null)
            {
                if (!Listener.TryGetTarget(out var target)) return false;
                return target.ReceiveWeakEvent(managerType, sender, args);
            }
            if (HandlerIsStatic)
            {
                HandlerMethod!.Invoke(null, [sender, args]);
                return true;
            }
            var handlerTarget = HandlerTarget!.Target;
            if (handlerTarget is null) return false;
            HandlerMethod!.Invoke(handlerTarget, [sender, args]);
            return true;
        }
    }
}
