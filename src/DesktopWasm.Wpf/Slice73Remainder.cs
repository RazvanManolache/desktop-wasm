using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;

namespace DesktopWasm.Wpf;

// Slice-73 material-design remainder (live shapes verified 2026-10-27
// against the 9.0.21 PresentationFramework/PresentationCore/WindowsBase
// runtime plus Mono.Cecil scans of the upstream net8.0-windows
// MaterialDesignThemes.Wpf.dll, MaterialDesignColors.dll and
// Microsoft.Xaml.Behaviors.dll): the date-picker pair, the context-menu
// event pair, the adorner placeholder, the cache hint, the color
// converter, the focus manager, the size-change carrier, the static
// resource extension, the generic weak-event manager, the annotation
// store and the navigation service. Converted callers pin every member:
// PopupEx.HostWindow_StateChanged reads AdornedElementPlaceholder.
// AdornedElement, TextFieldAssist subscribes ContextMenuEventHandler,
// DialogHost reads FocusManager.GetFocusedElement, InheritSystemColor-
// TypeConverter subclasses ColorConverter, the Colors assembly calls
// ColorConverter.ConvertFromString plus both StaticResourceExtension
// ctors, ResourceDictionaryExtensions reads get_ResourceKey,
// InteractionContext drives NavigationService, and the Behaviors layout
// code enumerates TimelineCollection/TransformCollection (nested
// Enumerator structs, added on those collections).
//
// The remaining material-design rows stay declared boundaries: the
// FreezableCollection`1/Enumerator nested types (the converter maps
// nested framework types only off non-generic outers), the absent
// System.Windows.Presentation facade binary (no browser path), and the
// static System.Configuration.ConfigurationManager entry row.

/// <summary>Date display style (matches System.Windows.Controls.DatePickerFormat).</summary>
public enum DatePickerFormat
{
    Long = 0,
    Short = 1,
}

/// <summary>
/// Editable date text field (matches
/// System.Windows.Controls.Primitives.DatePickerTextBox): the text part
/// of <see cref="DatePicker"/>, carrying the watermark slot. Converted
/// themes reference the type; the picker hosts it as its text field.
/// </summary>
public class DatePickerTextBox : TextBox
{
    /// <summary>Watermark slot (matches DatePickerTextBox.WatermarkProperty; retained, not painted).</summary>
    public static readonly DependencyProperty WatermarkProperty =
        DependencyProperty.Register("Watermark", typeof(object), typeof(DatePickerTextBox),
            new FrameworkPropertyMetadata(null));

    /// <summary>Watermark content (retained like the picker's other chrome text).</summary>
    public object? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }
}

/// <summary>Context-menu open/close handler shape (matches System.Windows.Controls.ContextMenuEventHandler).</summary>
public delegate void ContextMenuEventHandler(object sender, ContextMenuEventArgs e);

/// <summary>
/// Context-menu open/close payload (matches
/// System.Windows.Controls.ContextMenuEventArgs): cursor position plus
/// the menu target. Constructed by the menu host (internal ctors like
/// WPF); converted handlers only read the payload.
/// </summary>
public sealed class ContextMenuEventArgs : RoutedEventArgs
{
    internal ContextMenuEventArgs(object source, bool opening)
        : this(source, opening, 0, 0)
    {
    }

    internal ContextMenuEventArgs(object source, bool opening, double left, double top)
    {
        Source = source;
        Opening = opening;
        CursorLeft = left;
        CursorTop = top;
    }

    /// <summary>True for the opening notification, false for closing.</summary>
    public bool Opening { get; }

    /// <summary>Cursor X at open time (matches ContextMenuEventArgs.CursorLeft).</summary>
    public double CursorLeft { get; }

    /// <summary>Cursor Y at open time (matches ContextMenuEventArgs.CursorTop).</summary>
    public double CursorTop { get; }

    /// <summary>Element the menu targets (matches ContextMenuEventArgs.TargetElement).</summary>
    public DependencyObject? TargetElement { get; set; }
}

/// <summary>
/// Validation-template slot (matches
/// System.Windows.Controls.AdornedElementPlaceholder): reserves the
/// adorned element's space inside an error template. The adorner host
/// assigns <see cref="AdornedElement"/>; measure follows it, paint is a
/// no-op because the adorned element paints itself.
/// </summary>
public class AdornedElementPlaceholder : FrameworkElement
{
    private UIElement? _adornedElement;

    /// <summary>Element this placeholder reserves space for.</summary>
    public UIElement? AdornedElement => _adornedElement;

    /// <summary>Assigns the adorned element (the adorner host calls this, like WPF's template wiring).</summary>
    internal void SetAdornedElement(UIElement? element)
    {
        if (ReferenceEquals(_adornedElement, element)) return;
        _adornedElement = element;
        InvalidateMeasure();
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (AdornedElement is FrameworkElement fe)
        {
            var desired = fe.DesiredSize;
            return new WpfSize(desired.Width, desired.Height);
        }
        return new WpfSize(0, 0);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
    }

    protected override void OnRender(DrawingContext dc)
    {
    }
}

/// <summary>Caching hint (matches System.Windows.Media.CachingHint).</summary>
public enum CachingHint
{
    Unspecified = 0,
    Cache = 1,
}

/// <summary>
/// String color converter (matches System.Windows.Media.ColorConverter):
/// hex (#RRGGBB/#AARRGGBB) plus the engine <see cref="Colors"/> names,
/// case-insensitive like WPF. Converted themes subclass it
/// (InheritSystemColorTypeConverter) and the Colors assembly drives
/// <see cref="ConvertFromString"/>. scRGB/context colors have no engine
/// color-space equivalent and stay a precise FormatException.
/// </summary>
public class ColorConverter : TypeConverter
{
    /// <summary>Creates the converter (converted subclasses chain here).</summary>
    public ColorConverter()
    {
    }

    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text ? ParseColor(text) : throw new NotSupportedException(
            $"ColorConverter cannot convert from {value?.GetType().Name ?? "null"}.");

    /// <summary>Parses a color string (matches ColorConverter.ConvertFromString).</summary>
    public static object ConvertFromString(string text) =>
        new ColorConverter().ConvertFrom(null, CultureInfo.InvariantCulture, text)!;

    /// <inheritdoc />
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        value is Color color && destinationType == typeof(string) ? color.ToString() : throw new NotSupportedException(
            $"ColorConverter cannot convert {value?.GetType().Name ?? "null"} to {destinationType.Name}.");

    private static Color ParseColor(string text)
    {
        var trimmed = text.Trim();
        try
        {
            return Color.Parse(trimmed);
        }
        catch (FormatException)
        {
            // Fall through to the named-color lookup below.
        }
        var named = typeof(Colors).GetProperty(trimmed,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
        if (named is not null && named.PropertyType == typeof(Color) && named.GetValue(null) is Color color)
            return color;
        throw new FormatException($"'{text}' is not a valid color (#RRGGBB/#AARRGGBB or a known color name).");
    }
}

/// <summary>Localizability mutability (matches System.Windows.Modifiability).</summary>
public enum Modifiability
{
    Unmodifiable = 0,
    Modifiable = 1,
    Inherit = 2,
}

/// <summary>Localizability readability (matches System.Windows.Readability).</summary>
public enum Readability
{
    Unreadable = 0,
    Readable = 1,
    Inherit = 2,
}

/// <summary>Localizability category (matches System.Windows.LocalizationCategory).</summary>
public enum LocalizationCategory
{
    None = 0,
    Text = 1,
    Title = 2,
    Label = 3,
    Button = 4,
    CheckBox = 5,
    ComboBox = 6,
    ListBox = 7,
    Menu = 8,
    RadioButton = 9,
    ToolTip = 10,
    Hyperlink = 11,
    TextFlow = 12,
    XmlData = 13,
    Font = 14,
    Inherit = 15,
    Ignore = 16,
    NeverLocalize = 17,
}

/// <summary>
/// Localization annotation (matches System.Windows.LocalizabilityAttribute,
/// verified sealed with Class/Struct/Enum/Property/Field usage against
/// the 9.0.21 runtime): converted Colors code carries these assembly- and
/// member-level rows; the engine keeps the annotation so the rows bind.
/// No localization pipeline consumes them in the browser.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class LocalizabilityAttribute : Attribute
{
    /// <summary>Annotates a category (defaults match WPF: modifiable, readable).</summary>
    public LocalizabilityAttribute(LocalizationCategory category)
    {
        Category = category;
        Modifiability = Modifiability.Modifiable;
        Readability = Readability.Readable;
    }

    /// <summary>Annotated category.</summary>
    public LocalizationCategory Category { get; }

    /// <summary>Mutability hint (defaults to Modifiable like WPF).</summary>
    public Modifiability Modifiability { get; set; }

    /// <summary>Readability hint (defaults to Readable like WPF).</summary>
    public Readability Readability { get; set; }
}

/// <summary>
/// Focus-scope manager (matches System.Windows.Input.FocusManager):
/// attached focused-element/scope slots plus the static accessors
/// converted dialogs drive (DialogHost reads GetFocusedElement to decide
/// whether focus sits inside the dialog before closing it). The focused
/// element is manager-authoritative through <see cref="Keyboard"/>; the
/// attached slot only overrides it when locally set, like WPF's
/// FocusedElement cache.
/// </summary>
public static class FocusManager
{
    /// <summary>Focused-element slot (matches FocusManager.FocusedElementProperty).</summary>
    public static readonly DependencyProperty FocusedElementProperty =
        DependencyProperty.RegisterAttached("FocusedElement", typeof(IInputElement), typeof(FocusManager),
            new FrameworkPropertyMetadata(null));

    /// <summary>Focus-scope slot (matches FocusManager.IsFocusScopeProperty; default false like WPF).</summary>
    public static readonly DependencyProperty IsFocusScopeProperty =
        DependencyProperty.RegisterAttached("IsFocusScope", typeof(bool), typeof(FocusManager),
            new FrameworkPropertyMetadata(false));

    /// <summary>Focus-arrival token (matches FocusManager.GotFocusEvent).</summary>
    public static readonly RoutedEvent GotFocusEvent = EventManager.RegisterRoutedEvent(
        "GotFocus", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(FocusManager));

    /// <summary>Focus-loss token (matches FocusManager.LostFocusEvent).</summary>
    public static readonly RoutedEvent LostFocusEvent = EventManager.RegisterRoutedEvent(
        "LostFocus", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(FocusManager));

    /// <summary>Reads the locally-set focused element (matches GetFocusedElement).</summary>
    public static IInputElement? GetFocusedElement(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.GetValue(FocusedElementProperty) is IInputElement local)
            return local;
        var focused = Keyboard.FocusedElement;
        if (focused is UIElement ui && IsInScopeSubtree(element, ui))
            return focused;
        return null;
    }

    /// <summary>
    /// Sets the focused element (matches SetFocusedElement): stores the
    /// slot and moves keyboard focus there through <see cref="Keyboard"/>
    /// when it is a focusable element, like WPF's logical-focus set.
    /// </summary>
    public static void SetFocusedElement(DependencyObject element, IInputElement? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FocusedElementProperty, value);
        if (value is not null)
            Keyboard.Focus(value);
    }

    /// <summary>Reads the scope flag (matches GetIsFocusScope; unset means false).</summary>
    public static bool GetIsFocusScope(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(IsFocusScopeProperty) is true;
    }

    /// <summary>Writes the scope flag (matches SetIsFocusScope).</summary>
    public static void SetIsFocusScope(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsFocusScopeProperty, value);
    }

    /// <summary>
    /// Nearest self-or-ancestor focus scope (matches GetFocusScope; null
    /// when no scope is set, like an unscoped WPF tree).
    /// </summary>
    public static DependencyObject? GetFocusScope(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node.GetValue(IsFocusScopeProperty) is true)
                return node;
        }
        return null;
    }

    /// <summary>Subscribes a focus-arrival handler (matches AddGotFocusHandler).</summary>
    public static void AddGotFocusHandler(DependencyObject element, RoutedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is UIElement ui) ui.GotFocus += handler;
        else throw new ArgumentException($"'{element.GetType().Name}' cannot take focus handlers.", nameof(element));
    }

    /// <summary>Unsubscribes a focus-arrival handler (matches RemoveGotFocusHandler).</summary>
    public static void RemoveGotFocusHandler(DependencyObject element, RoutedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is UIElement ui) ui.GotFocus -= handler;
    }

    /// <summary>Subscribes a focus-loss handler (matches AddLostFocusHandler).</summary>
    public static void AddLostFocusHandler(DependencyObject element, RoutedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is UIElement ui) ui.LostFocus += handler;
        else throw new ArgumentException($"'{element.GetType().Name}' cannot take focus handlers.", nameof(element));
    }

    /// <summary>Unsubscribes a focus-loss handler (matches RemoveLostFocusHandler).</summary>
    public static void RemoveLostFocusHandler(DependencyObject element, RoutedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is UIElement ui) ui.LostFocus -= handler;
    }

    private static bool IsInScopeSubtree(DependencyObject scope, UIElement focused)
    {
        for (var node = (DependencyObject)focused; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, scope))
                return true;
        }
        return false;
    }
}

/// <summary>
/// Layout size-change carrier (matches System.Windows.SizeChangedInfo):
/// previous size plus dimensions deltas, with the new size read live
/// off the element like WPF (NewSize returns RenderSize).
/// </summary>
public class SizeChangedInfo
{
    private readonly UIElement _element;
    private bool _widthChanged;
    private bool _heightChanged;

    /// <summary>Captures the pre-arrange size (matches the WPF public ctor shape).</summary>
    public SizeChangedInfo(UIElement element, Size previousSize, bool widthChanged, bool heightChanged)
    {
        _element = element ?? throw new ArgumentNullException(nameof(element));
        PreviousSize = previousSize;
        _widthChanged = widthChanged;
        _heightChanged = heightChanged;
    }

    /// <summary>Element that changed size.</summary>
    public UIElement Element => _element;

    /// <summary>Size from the previous arrange pass.</summary>
    public Size PreviousSize { get; }

    /// <summary>Size after the arrange pass (live off the element, like WPF).</summary>
    public Size NewSize => _element.RenderSize;

    /// <summary>True when the width differs between passes.</summary>
    public bool WidthChanged => _widthChanged;

    /// <summary>True when the height differs between passes.</summary>
    public bool HeightChanged => _heightChanged;

    /// <summary>Refreshes the dimension deltas (matches SizeChangedInfo.Update).</summary>
    public void Update(bool widthChanged, bool heightChanged)
    {
        _widthChanged = widthChanged;
        _heightChanged = heightChanged;
    }
}

/// <summary>
/// One-shot static resource reference (matches
/// System.Windows.StaticResourceExtension): resolves the key once
/// through element/ancestor/application scopes and throws when absent,
/// like WPF (unlike DynamicResourceExtension, which stays null and
/// re-applies). Converted Colors code constructs both overloads and
/// converted theme code reads ResourceKey back.
/// </summary>
public class StaticResourceExtension : MarkupExtension
{
    /// <summary>Creates an extension with no key (set ResourceKey before use).</summary>
    public StaticResourceExtension()
    {
    }

    /// <summary>Creates an extension for a resource key.</summary>
    public StaticResourceExtension(object resourceKey) => ResourceKey = resourceKey;

    /// <summary>Resource key to resolve (string keys, like XAML).</summary>
    public object? ResourceKey { get; set; }

    /// <inheritdoc />
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        var key = ResourceKey ?? throw new InvalidOperationException(
            "StaticResourceExtension.ResourceKey is null.");
        var target = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        if (target?.TargetObject is FrameworkElement element && element.TryFindResource(key, out var local))
            return local;
        // Load-time entries in merged Source dictionaries resolve against
        // the dictionaries hydrating around them (theme aliases over the
        // just-merged BundledTheme), before Application scope exists.
        if (XamlLoader.TryFindHydrationResource(key, out var hydrating) && hydrating is not null)
            return hydrating;
        if (Application.Current?.TryFindResource(key, out var appValue) == true)
            return appValue;
        throw new InvalidOperationException($"StaticResource '{key}' was not found.");
    }
}

/// <summary>
/// Typed weak-event manager (matches System.Windows.WeakEventManager`2,
/// verified with no generic constraints against the 9.0.21 runtime):
/// subscribes <see cref="EventHandler{TEventArgs}"/> handlers without
/// rooting their targets, delivering through the shared
/// <see cref="WeakEventManager"/> listener lists so collected targets
/// stop receiving without unsubscribing. The event is resolved by name
/// on first listen (missing events fail loud, like WPF).
/// </summary>
public class WeakEventManager<TEventSource, TEventArgs> : WeakEventManager
{
    private readonly string _eventName;
    private readonly Dictionary<object, System.Delegate> _trampolines = new(ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<object, List<HandlerListener>> _liveWrappers = new();

    /// <summary>Creates a manager for a named source event (matches the protected WPF ctor).</summary>
    protected WeakEventManager(string eventName)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventName);
        _eventName = eventName;
    }

    /// <summary>Subscribes a handler weakly (matches AddHandler).</summary>
    public static void AddHandler(TEventSource source, string eventName, EventHandler<TEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(eventName);
        ArgumentNullException.ThrowIfNull(handler);
        var manager = CurrentManager(eventName);
        var wrapper = new HandlerListener(handler);
        var live = manager._liveWrappers.GetOrCreateValue(source);
        lock (live)
        {
            if (live.Contains(wrapper))
                return;
            manager.AddListener(source, wrapper);
            live.Add(wrapper);
        }
    }

    /// <summary>Unsubscribes a handler (matches RemoveHandler).</summary>
    public static void RemoveHandler(TEventSource source, string eventName, EventHandler<TEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(eventName);
        ArgumentNullException.ThrowIfNull(handler);
        var manager = CurrentManager(eventName);
        if (!manager._liveWrappers.TryGetValue(source, out var live))
            return;
        var probe = new HandlerListener(handler);
        lock (live)
        {
            var index = live.IndexOf(probe);
            if (index < 0)
                return;
            var existing = live[index];
            live.RemoveAt(index);
            if (live.Count == 0)
                manager._liveWrappers.Remove(source);
            manager.RemoveListener(source, existing);
        }
    }

    /// <summary>Manager for an event name, created on first use (matches CurrentManager).</summary>
    protected static WeakEventManager<TEventSource, TEventArgs> CurrentManager(string eventName)
    {
        var managerType = typeof(WeakEventManager<TEventSource, TEventArgs>);
        var current = (WeakEventManager<TEventSource, TEventArgs>?)GetCurrentManager(managerType);
        if (current is null)
        {
            current = new ConcreteManager(eventName);
            SetCurrentManager(managerType, current);
        }
        return current;
    }

    /// <summary>Subscribes the delivery trampoline to the source event (matches StartListening).</summary>
    protected override void StartListening(object source)
    {
        // Native WeakEventManager binds its delivery method to the event's
        // own handler type (Loaded is RoutedEventHandler, not
        // EventHandler<RoutedEventArgs>); creating the delegate from the
        // handler instance fails that AddEventHandler type check.
        var info = Event(source);
        System.Delegate trampoline;
        try
        {
            trampoline = System.Delegate.CreateDelegate(info.EventHandlerType!, this, new EventHandler<TEventArgs>(OnSourceEvent).Method);
        }
        catch (Exception ex) when (ex is ArgumentException or MemberAccessException)
        {
            trampoline = new EventHandler<TEventArgs>(OnSourceEvent);
        }
        info.AddEventHandler(source, trampoline);
        lock (_trampolines)
            _trampolines[source] = trampoline;
    }

    /// <summary>Unsubscribes the delivery trampoline (matches StopListening).</summary>
    protected override void StopListening(object source)
    {
        System.Delegate? trampoline;
        lock (_trampolines)
        {
            if (!_trampolines.Remove(source, out trampoline))
                return;
        }
        Event(source).RemoveEventHandler(source, trampoline);
    }

    /// <summary>Delivers a typed event through the manager (matches DeliverEvent).</summary>
    protected void DeliverEvent(object? sender, TEventArgs args) =>
        DeliverEvent(sender, (EventArgs)(object)args!);

    private void OnSourceEvent(object? sender, TEventArgs args)
    {
        DeliverEvent(sender, args);
        if (sender is not null && _liveWrappers.TryGetValue(sender, out var live))
        {
            lock (live)
                live.RemoveAll(wrapper => !wrapper.IsAlive);
        }
    }

    private EventInfo Event(object source) =>
        source.GetType().GetEvent(_eventName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
        ?? throw new InvalidOperationException(
            $"'{source.GetType().Name}' has no event '{_eventName}'.");

    private sealed class ConcreteManager(string eventName) : WeakEventManager<TEventSource, TEventArgs>(eventName)
    {
    }

    private sealed class HandlerListener(EventHandler<TEventArgs> handler) : IWeakEventListener
    {
        private readonly WeakReference _target = new(handler.Target);
        private readonly MethodInfo _method = handler.Method;

        /// <summary>True while a static handler or a live target backs this subscription.</summary>
        public bool IsAlive => _method.IsStatic || _target.IsAlive;

        public bool Equals(HandlerListener? other) =>
            other is not null && ReferenceEquals(_target.Target, other._target.Target) && _method.Equals(other._method);

        public override bool Equals(object? obj) => obj is HandlerListener other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_target.Target, _method);

        public bool ReceiveWeakEvent(Type managerType, object? sender, EventArgs e)
        {
            if (e is not TEventArgs args)
                return true;
            if (_method.IsStatic)
            {
                _method.Invoke(null, [sender, args]);
                return true;
            }
            var target = _target.Target;
            if (target is null)
                return false;
            _method.Invoke(target, [sender, args]);
            return true;
        }
    }
}

/// <summary>Annotation change kind (matches System.Windows.Annotations.AnnotationAction).</summary>
public enum AnnotationAction
{
    Added = 0,
    Removed = 1,
    Modified = 2,
}

/// <summary>
/// Annotation resource-change payload (matches
/// System.Windows.Annotations.AnnotationResourceChangedEventArgs).
/// </summary>
public sealed class AnnotationResourceChangedEventArgs : EventArgs
{
    public AnnotationResourceChangedEventArgs(Annotation annotation, AnnotationAction action, AnnotationResource resource)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Action = action;
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
    }

    /// <summary>Annotation that owns the resource.</summary>
    public Annotation Annotation { get; }

    /// <summary>What changed.</summary>
    public AnnotationAction Action { get; }

    /// <summary>Resource that changed.</summary>
    public AnnotationResource Resource { get; }
}

/// <summary>
/// Annotation author-change payload (matches
/// System.Windows.Annotations.AnnotationAuthorChangedEventArgs).
/// </summary>
public sealed class AnnotationAuthorChangedEventArgs : EventArgs
{
    public AnnotationAuthorChangedEventArgs(Annotation annotation, AnnotationAction action, object author)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Action = action;
        Author = author ?? throw new ArgumentNullException(nameof(author));
    }

    /// <summary>Annotation that owns the author.</summary>
    public Annotation Annotation { get; }

    /// <summary>What changed.</summary>
    public AnnotationAction Action { get; }

    /// <summary>Author that changed.</summary>
    public object Author { get; }
}

/// <summary>Annotation resource-change handler shape (matches AnnotationResourceChangedEventHandler).</summary>
public delegate void AnnotationResourceChangedEventHandler(object sender, AnnotationResourceChangedEventArgs e);

/// <summary>Annotation author-change handler shape (matches AnnotationAuthorChangedEventHandler).</summary>
public delegate void AnnotationAuthorChangedEventHandler(object sender, AnnotationAuthorChangedEventArgs e);

/// <summary>
/// Annotation resource (matches System.Windows.Annotations.AnnotationResource,
/// verified sealed): a named content anchor or cargo with a stable id.
/// XML content locators have no session equivalent and are not carried.
/// </summary>
public sealed class AnnotationResource
{
    /// <summary>Creates an unnamed resource with a fresh id.</summary>
    public AnnotationResource()
        : this(Guid.NewGuid())
    {
    }

    /// <summary>Creates a named resource with a fresh id.</summary>
    public AnnotationResource(string name)
        : this(Guid.NewGuid())
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>Creates a resource with a fixed id.</summary>
    public AnnotationResource(Guid id)
    {
        Id = id;
    }

    /// <summary>Resource name (anchor or cargo slot).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Stable resource id.</summary>
    public Guid Id { get; }
}

/// <summary>
/// User annotation (matches System.Windows.Annotations.Annotation,
/// verified sealed over System.Object): anchor/author/cargo stores with
/// change notices and freshness timestamps. Converted Behaviors code
/// references the type; the stores behave like WPF's observable
/// collections (add/remove fires the matching event and refreshes
/// <see cref="LastModificationTime"/>).
/// </summary>
public sealed class Annotation
{
    private readonly ObservableCollection<AnnotationResource> _anchors = new();
    private readonly ObservableCollection<string> _authors = new();
    private readonly ObservableCollection<AnnotationResource> _cargos = new();

    /// <summary>Creates an untyped annotation with fresh stamps.</summary>
    public Annotation()
        : this(null)
    {
    }

    /// <summary>Creates a typed annotation with fresh stamps.</summary>
    public Annotation(XmlQualifiedName? annotationType)
        : this(annotationType, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow)
    {
    }

    /// <summary>Creates a fully-stamped annotation (matches the WPF full ctor).</summary>
    public Annotation(XmlQualifiedName? annotationType, Guid id, DateTime creationTime, DateTime lastModificationTime)
    {
        AnnotationType = annotationType;
        Id = id;
        CreationTime = creationTime;
        LastModificationTime = lastModificationTime;
        _anchors.CollectionChanged += (_, e) => OnAnchorsChanged(e);
        _authors.CollectionChanged += (_, e) => OnAuthorsChanged(e);
        _cargos.CollectionChanged += (_, e) => OnCargosChanged(e);
    }

    /// <summary>Anchor resources.</summary>
    public Collection<AnnotationResource> Anchors => _anchors;

    /// <summary>Author names.</summary>
    public Collection<string> Authors => _authors;

    /// <summary>Cargo resources.</summary>
    public Collection<AnnotationResource> Cargos => _cargos;

    /// <summary>Annotation type name.</summary>
    public XmlQualifiedName? AnnotationType { get; }

    /// <summary>Stable annotation id.</summary>
    public Guid Id { get; }

    /// <summary>Creation stamp.</summary>
    public DateTime CreationTime { get; }

    /// <summary>Last-change stamp (refreshed by every store mutation).</summary>
    public DateTime LastModificationTime { get; private set; }

    /// <summary>Anchor change notice (matches Annotation.AnchorChanged).</summary>
    public event AnnotationResourceChangedEventHandler? AnchorChanged;

    /// <summary>Author change notice (matches Annotation.AuthorChanged).</summary>
    public event AnnotationAuthorChangedEventHandler? AuthorChanged;

    /// <summary>Cargo change notice (matches Annotation.CargoChanged).</summary>
    public event AnnotationResourceChangedEventHandler? CargoChanged;

    private static AnnotationAction ActionOf(System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        e.Action switch
        {
            System.Collections.Specialized.NotifyCollectionChangedAction.Add => AnnotationAction.Added,
            System.Collections.Specialized.NotifyCollectionChangedAction.Remove => AnnotationAction.Removed,
            _ => AnnotationAction.Modified,
        };

    private void OnAnchorsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        LastModificationTime = DateTime.UtcNow;
        var action = ActionOf(e);
        var items = (e.NewItems ?? e.OldItems)?.Cast<AnnotationResource>() ?? [];
        foreach (var resource in items)
            AnchorChanged?.Invoke(this, new AnnotationResourceChangedEventArgs(this, action, resource));
    }

    private void OnAuthorsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        LastModificationTime = DateTime.UtcNow;
        var action = ActionOf(e);
        var items = (e.NewItems ?? e.OldItems)?.Cast<object>() ?? [];
        foreach (var author in items)
            AuthorChanged?.Invoke(this, new AnnotationAuthorChangedEventArgs(this, action, author));
    }

    private void OnCargosChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        LastModificationTime = DateTime.UtcNow;
        var action = ActionOf(e);
        var items = (e.NewItems ?? e.OldItems)?.Cast<AnnotationResource>() ?? [];
        foreach (var resource in items)
            CargoChanged?.Invoke(this, new AnnotationResourceChangedEventArgs(this, action, resource));
    }
}

/// <summary>
/// Frame navigation service (matches System.Windows.Navigation.NavigationService,
/// verified over System.Object): a content journal bound to one
/// <see cref="Frame"/>. Converted Behaviors code (InteractionContext)
/// reads the host's service and drives Navigate/GoBack/GoForward with
/// the CanGoBack/CanGoForward guards. Content navigation journals real
/// entries and swaps the host; Uri navigation needs pack resolution
/// plus the desktop journal and stays a precise error, like
/// <see cref="Frame"/>.
/// </summary>
public class NavigationService
{
    private readonly Frame? _host;
    private readonly List<object?> _back = new();
    private readonly List<object?> _forward = new();
    private object? _current;

    /// <summary>Binds a service to a host frame (converted code arrives via Frame.NavigationService).</summary>
    internal NavigationService(Frame host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Current content (matches NavigationService.Content).</summary>
    public object? Content => _current;

    /// <summary>Whether the back journal is non-empty (matches CanGoBack).</summary>
    public bool CanGoBack => _back.Count > 0;

    /// <summary>Whether the forward journal is non-empty (matches CanGoForward).</summary>
    public bool CanGoForward => _forward.Count > 0;

    /// <summary>
    /// Navigates to content (matches Navigate(object)): journals the
    /// entry, drops the forward stack, swaps the host and reports true.
    /// </summary>
    public bool Navigate(object? content)
    {
        if (_current is not null)
            _back.Add(_current);
        _forward.Clear();
        _current = content;
        _host?.NavigateToServiceContent(content);
        return true;
    }

    /// <summary>Uri navigation needs pack resolution plus the desktop journal: precise error.</summary>
    public bool Navigate(Uri source) => throw new InvalidOperationException(
        $"NavigationService.Navigate({source}) needs Uri pack resolution and a navigation journal, which v1 does not implement.");

    /// <summary>Steps back through the journal (matches GoBack; throws when empty like WPF).</summary>
    public void GoBack()
    {
        if (_back.Count == 0)
            throw new InvalidOperationException("NavigationService.GoBack needs a back entry (CanGoBack is false).");
        if (_current is not null)
            _forward.Add(_current);
        _current = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        _host?.NavigateToServiceContent(_current);
    }

    /// <summary>Steps forward through the journal (matches GoForward; throws when empty like WPF).</summary>
    public void GoForward()
    {
        if (_forward.Count == 0)
            throw new InvalidOperationException("NavigationService.GoForward needs a forward entry (CanGoForward is false).");
        if (_current is not null)
            _back.Add(_current);
        _current = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        _host?.NavigateToServiceContent(_current);
    }

    /// <summary>
    /// Service for an element's host frame (matches
    /// GetNavigationService): walks to the containing <see cref="Frame"/>
    /// and returns its service, or null outside one.
    /// </summary>
    public static NavigationService? GetNavigationService(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is Frame frame)
                return frame.NavigationService;
        }
        return null;
    }
}
