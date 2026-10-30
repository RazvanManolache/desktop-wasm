using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;

namespace DesktopWasm.Wpf;

// Data-provider engine v1 (PresentationFramework/WindowsBase subset, real
// behavior, not port): DataSourceProvider (deferred/initial-load/refresh
// machinery, DataChanged + INotifyPropertyChanged) plus ObjectDataProvider
// (ObjectInstance/ObjectType + MethodName/MethodParameters method invocation
// producing Data, ConstructorParameters instantiation, IsAsynchronous
// dispatch) plus TextSearch (attached Text/TextPath contract). Member
// evidence via Mono.Cecil on the upstream net10.0-windows HandyControl.dll:
// HandyControl.Data.EnumDataProvider derives ObjectDataProvider (ctor sets
// MethodName to GetValues; set_Type appends the enum Type to
// MethodParameters and sets ObjectType to Enum; set_UseAttributes switches
// ObjectType between EnumDataProvider and Enum) and
// HandyControl.Tools.Helper.BindingHelper.GetString binds
// TextSearch.TextProperty on a fresh DependencyObject (TwoWay path binding
// with the value as Source, then reads and clears it). Every shape below is
// live-verified against the .NET 10 WindowsDesktop reference assemblies
// (DataSourceProvider abstract with get-only Data/Error, protected
// Dispatcher/IsRefreshDeferred, public IsInitialLoadEnabled/DataChanged/
// DeferRefresh/InitialLoad/Refresh plus INotifyPropertyChanged and
// ISupportInitialize; ObjectDataProvider concrete with get-only
// ConstructorParameters/MethodParameters and settable
// IsAsynchronous/MethodName/ObjectInstance/ObjectType plus the four
// ShouldSerialize* predicates; TextSearch : DependencyObject with a private
// ctor and attached Text/TextPath defaulting to String.Empty) and against a
// real-WPF STA probe (defaults, setter re-query, deferral, error, event and
// journal semantics; see Slice80ProvidersTests).
// Reference semantics: System.Windows.Data.DataSourceProvider,
// System.Windows.Data.ObjectDataProvider, System.Windows.Controls.TextSearch.
// Shim-always-shim, never fabricated: the query path uses the same
// Type.InvokeMember late-binding as WPF, so overload quirks match by
// construction (e.g. invoking the open generic Enum.GetValues<TEnum> with
// zero parameters raises InvalidOperationException out of the setter, while
// a missing method lands in Error as MissingMethodException). Declared
// boundaries: IsAsynchronous posts through the engine dispatcher (no
// cross-thread pumping); unresolved-method queries beyond
// MissingMethod/TargetInvocation propagate like the framework;
// TextSearch type-ahead selection (FindNextTextMatch behavior) stays a
// boundary as declared on ItemsControl.IsTextSearchEnabled; XAML attached
// TextSearch.Text stays a precise loud loader rejection until a staged
// template needs it.

/// <summary>Data-source host (matches System.Windows.Data.DataSourceProvider).</summary>
public abstract class DataSourceProvider : INotifyPropertyChanged, ISupportInitialize
{
    private object? _data;
    private Exception? _error;
    private bool _isInitialLoadEnabled = true;
    private int _deferCount;
    private int _initDepth;
    private bool _needsRefresh;
    private Dispatcher? _dispatcher;

    /// <summary>Initializes the provider (matches the protected framework ctor).</summary>
    protected DataSourceProvider()
    {
    }

    /// <summary>Current query result (matches DataSourceProvider.Data).</summary>
    public object? Data => _data;

    /// <summary>Affinity for asynchronous queries (matches the protected framework slot).</summary>
    protected Dispatcher Dispatcher
    {
        get => _dispatcher ??= Dispatcher.CurrentDispatcher;
        set => _dispatcher = value;
    }

    /// <summary>Query failure, if the last query faulted (matches DataSourceProvider.Error).</summary>
    public Exception? Error => _error;

    /// <summary>Whether InitialLoad runs a query (matches DataSourceProvider.IsInitialLoadEnabled).</summary>
    public bool IsInitialLoadEnabled
    {
        get => _isInitialLoadEnabled;
        set => _isInitialLoadEnabled = value;
    }

    /// <summary>Whether queries are currently deferred (matches the protected framework slot).</summary>
    protected bool IsRefreshDeferred => _deferCount > 0 || _initDepth > 0;

    /// <summary>Raised after every completed query, success or failure (matches DataSourceProvider.DataChanged).</summary>
    public event EventHandler? DataChanged;

    /// <summary>Property notice (matches the protected framework event).</summary>
    protected virtual event PropertyChangedEventHandler? PropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => PropertyChanged += value;
        remove => PropertyChanged -= value;
    }

    /// <summary>Raises PropertyChanged (matches the protected framework hook).</summary>
    /// <param name="e">Notice payload.</param>
    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, e);

    /// <summary>Defers queries until disposal, then runs one refresh when needed (matches DeferRefresh).</summary>
    /// <returns>Scope ending the deferral.</returns>
    public IDisposable DeferRefresh()
    {
        _deferCount++;
        return new RefreshDeferral(this);
    }

    /// <summary>Signals initialization start (matches ISupportInitialize.BeginInit).</summary>
    protected virtual void BeginInit() => _initDepth++;

    /// <summary>Signals initialization end and runs one refresh when needed (matches ISupportInitialize.EndInit).</summary>
    protected virtual void EndInit()
    {
        if (_initDepth > 0) _initDepth--;
        if (_initDepth == 0 && _needsRefresh)
        {
            _needsRefresh = false;
            Refresh();
        }
    }

    void ISupportInitialize.BeginInit() => BeginInit();

    void ISupportInitialize.EndInit() => EndInit();

    /// <summary>Runs the initial query unless disabled (matches DataSourceProvider.InitialLoad).</summary>
    public void InitialLoad()
    {
        if (IsInitialLoadEnabled) Refresh();
    }

    /// <summary>Re-runs the query now, or marks one pending while deferred (matches DataSourceProvider.Refresh).</summary>
    public void Refresh()
    {
        if (IsRefreshDeferred)
        {
            _needsRefresh = true;
            return;
        }
        BeginQuery();
    }

    /// <summary>Starts a query (matches the protected framework hook; the base is a no-op).</summary>
    protected virtual void BeginQuery()
    {
    }

    /// <summary>Publishes a query result (matches OnQueryFinished(data)).</summary>
    /// <param name="resultData">New Data value.</param>
    protected void OnQueryFinished(object? resultData) => OnQueryFinished(resultData, null, null, null);

    /// <summary>Publishes a query result or failure (matches OnQueryFinished(data, error, callback, args)).</summary>
    /// <param name="resultData">New Data value.</param>
    /// <param name="error">New Error value.</param>
    /// <param name="callback">Async completion hook (accepted, unused: the engine has no async completion path).</param>
    /// <param name="callbackArgs">Async completion state (accepted, unused).</param>
    protected void OnQueryFinished(object? resultData, Exception? error, DispatcherOperationCallback? callback, object? callbackArgs)
    {
        _data = resultData;
        _error = error;
        DataChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Data)));
    }

    private sealed class RefreshDeferral : IDisposable
    {
        private DataSourceProvider? _owner;

        public RefreshDeferral(DataSourceProvider owner) => _owner = owner;

        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            if (owner is null) return;
            owner._deferCount--;
            if (owner._deferCount == 0 && owner._needsRefresh)
            {
                owner._needsRefresh = false;
                owner.Refresh();
            }
        }
    }
}

/// <summary>Method-invoking provider (matches System.Windows.Data.ObjectDataProvider).</summary>
public class ObjectDataProvider : DataSourceProvider
{
    private const BindingFlags InvokeFlags =
        BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;

    private readonly ObservableCollection<object?> _constructorParameters = new();
    private readonly ObservableCollection<object?> _methodParameters = new();
    private bool _isAsynchronous;
    private string? _methodName;
    private object? _objectInstance;
    private Type? _objectType;

    /// <summary>Initializes the provider with live parameter lists (matches the framework ctor).</summary>
    public ObjectDataProvider()
    {
        _constructorParameters.CollectionChanged += (_, _) => Refresh();
        _methodParameters.CollectionChanged += (_, _) => Refresh();
    }

    /// <summary>Constructor arguments for ObjectType instantiation (matches ConstructorParameters).</summary>
    public IList ConstructorParameters => _constructorParameters;

    /// <summary>Method arguments for the MethodName call (matches MethodParameters).</summary>
    public IList MethodParameters => _methodParameters;

    /// <summary>Whether queries post through the dispatcher (matches IsAsynchronous).</summary>
    public bool IsAsynchronous
    {
        get => _isAsynchronous;
        set => _isAsynchronous = value;
    }

    /// <summary>Method invoked on the target (matches MethodName).</summary>
    public string? MethodName
    {
        get => _methodName;
        set
        {
            _methodName = value;
            Refresh();
        }
    }

    /// <summary>Direct target object, winning over ObjectType (matches ObjectInstance).</summary>
    public object? ObjectInstance
    {
        get => _objectInstance;
        set
        {
            _objectInstance = value;
            Refresh();
        }
    }

    /// <summary>Target type, instantiated or used statically (matches ObjectType).</summary>
    public Type? ObjectType
    {
        get => _objectType;
        set
        {
            _objectType = value;
            Refresh();
        }
    }

    /// <summary>Serialization guard (matches ShouldSerializeConstructorParameters).</summary>
    /// <returns>True once a constructor parameter exists.</returns>
    public bool ShouldSerializeConstructorParameters() => _constructorParameters.Count > 0;

    /// <summary>Serialization guard (matches ShouldSerializeMethodParameters).</summary>
    /// <returns>True once a method parameter exists.</returns>
    public bool ShouldSerializeMethodParameters() => _methodParameters.Count > 0;

    /// <summary>Serialization guard (matches ShouldSerializeObjectInstance).</summary>
    /// <returns>True once an instance is set.</returns>
    public bool ShouldSerializeObjectInstance() => _objectInstance is not null;

    /// <summary>Serialization guard (matches ShouldSerializeObjectType).</summary>
    /// <returns>True once a type is set.</returns>
    public bool ShouldSerializeObjectType() => _objectType is not null;

    /// <summary>Runs the method query, synchronously or posted (matches the framework override).</summary>
    protected override void BeginQuery()
    {
        if (IsAsynchronous)
        {
            Dispatcher.BeginInvoke(new Action(RunQuery));
            return;
        }
        RunQuery();
    }

    private void RunQuery()
    {
        if (ObjectInstance is null && ObjectType is null)
        {
            OnQueryFinished(null, null, null, null);
            return;
        }
        object? data = null;
        Exception? error = null;
        try
        {
            data = Invoke();
        }
        catch (TargetInvocationException tie)
        {
            error = tie.InnerException ?? tie;
        }
        catch (MissingMethodException mme)
        {
            error = mme;
        }
        OnQueryFinished(data, error, null, null);
    }

    private object? Invoke()
    {
        var args = _methodParameters.Cast<object?>().ToArray();
        if (string.IsNullOrEmpty(MethodName))
            return ObjectInstance ?? CreateInstance();
        // Single late-bound call on the instance or the type (matches the
        // framework invoking through InvokeMember, so overload quirks match
        // by construction: statics run with a null target and abstract types
        // never reach the activator; a missing method reports MissingMethod
        // ("Method 'FullType.Name' not found."); an open generic method
        // late-binds and its InvalidOperationException escapes like the
        // framework). An instance method with an ObjectType-only target
        // reports TargetException, which funds creation from
        // ConstructorParameters before the retry below.
        var type = ObjectInstance?.GetType() ?? ObjectType!;
        var target = ObjectInstance;
        try
        {
            return type.InvokeMember(MethodName, InvokeFlags, binder: null, target, args);
        }
        catch (TargetException)
        {
            target = CreateInstance();
            return target.GetType().InvokeMember(MethodName, InvokeFlags, binder: null, target, args);
        }
    }

    private object CreateInstance()
    {
        var args = _constructorParameters.Cast<object?>().ToArray();
        return Activator.CreateInstance(ObjectType!, args)!;
    }
}

/// <summary>Attached text-search contract (matches System.Windows.Controls.TextSearch).</summary>
public class TextSearch : DependencyObject
{
    private TextSearch()
    {
    }

    /// <summary>Attached search text (matches TextSearch.TextProperty).</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(TextSearch),
            new PropertyMetadata(string.Empty));

    /// <summary>Attached search-text path (matches TextSearch.TextPathProperty).</summary>
    public static readonly DependencyProperty TextPathProperty =
        DependencyProperty.RegisterAttached("TextPath", typeof(string), typeof(TextSearch),
            new PropertyMetadata(string.Empty));

    /// <summary>Reads the attached search text (matches TextSearch.GetText).</summary>
    /// <param name="element">Target element.</param>
    /// <returns>The attached text, String.Empty when unset.</returns>
    public static string GetText(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(TextProperty) ?? string.Empty;
    }

    /// <summary>Writes the attached search text (matches TextSearch.SetText).</summary>
    /// <param name="element">Target element.</param>
    /// <param name="value">Text value.</param>
    public static void SetText(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextProperty, value);
    }

    /// <summary>Reads the attached search-text path (matches TextSearch.GetTextPath).</summary>
    /// <param name="element">Target element.</param>
    /// <returns>The attached path, String.Empty when unset.</returns>
    public static string GetTextPath(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(TextPathProperty) ?? string.Empty;
    }

    /// <summary>Writes the attached search-text path (matches TextSearch.SetTextPath).</summary>
    /// <param name="element">Target element.</param>
    /// <param name="value">Path value.</param>
    public static void SetTextPath(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextPathProperty, value);
    }
}
