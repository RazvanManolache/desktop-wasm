using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DesktopWasm.Wpf;

// Styling + control surface (PresentationCore/PresentationFramework subset,
// shim, not port): routed-event tokens, the Style/Setter/EventSetter runtime
// the XAML loader used to fake with string-only LiteralStyle, the Control /
// UserControl / TextBoxBase hierarchy converted gallery and ScreenToGif bind
// against, font value types, and the markup attributes + ThemeMode their
// generated code references. Namespace rule: every public type here keeps the
// WPF short name so the converter's name map binds it without an override.

/// <summary>Routing strategy (matches System.Windows.RoutingStrategy).</summary>
public enum RoutingStrategy
{
    Tunnel,
    Bubble,
    Direct,
}

/// <summary>
/// Routed-event token (matches System.Windows.RoutedEvent): identity for
/// AddHandler/RemoveHandler/RaiseEvent and EventSetter. Real WPF registers
/// these in a global table; the engine registers through EventManager.
/// </summary>
public sealed class RoutedEvent
{
    internal RoutedEvent(string name, RoutingStrategy routingStrategy, Type handlerType, Type ownerType)
    {
        Name = name;
        RoutingStrategy = routingStrategy;
        HandlerType = handlerType;
        OwnerType = ownerType;
    }

    public string Name { get; }
    public RoutingStrategy RoutingStrategy { get; }
    public Type HandlerType { get; }
    public Type OwnerType { get; }

    public override string ToString() => $"{OwnerType.Name}.{Name}";

    /// <summary>Registers another owner for this event (same instance, like WPF).</summary>
    public RoutedEvent AddOwner(Type ownerType)
    {
        EventManager.AliasOwner(ownerType, this);
        return this;
    }
}

/// <summary>
/// Routed-event registry (matches System.Windows.EventManager): converted
/// code registers class-level events here; UIElement routes them.
/// </summary>
public static class EventManager
{
    private static readonly Dictionary<(Type Owner, string Name), RoutedEvent> Registered = new();

    private static readonly List<(Type Owner, RoutedEvent Event, Delegate Handler, bool HandledToo)> ClassHandlers = new();

    public static RoutedEvent RegisterRoutedEvent(string name, RoutingStrategy routingStrategy, Type handlerType, Type ownerType)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(handlerType);
        ArgumentNullException.ThrowIfNull(ownerType);
        var key = (ownerType, name);
        lock (Registered)
        {
            if (Registered.TryGetValue(key, out var existing)) return existing;
            var routed = new RoutedEvent(name, routingStrategy, handlerType, ownerType);
            Registered[key] = routed;
            return routed;
        }
    }

    /// <summary>Registers an additional owner for a routed event (same instance, like WPF).</summary>
    internal static void AliasOwner(Type ownerType, RoutedEvent routedEvent)
    {
        ArgumentNullException.ThrowIfNull(ownerType);
        ArgumentNullException.ThrowIfNull(routedEvent);
        lock (Registered) { Registered[(ownerType, routedEvent.Name)] = routedEvent; }
    }

    /// <summary>
    /// Class-level handler (matches EventManager.RegisterClassHandler;
    /// converted AvalonDock drop-down areas register theirs in the static
    /// constructor). Stored per owner type and invoked from
    /// <see cref="UIElement.InvokeRoutedHandlers"/> ahead of the instance
    /// handlers at every route node, like WPF.
    /// </summary>
    public static void RegisterClassHandler(Type classType, RoutedEvent routedEvent, Delegate handler) =>
        RegisterClassHandler(classType, routedEvent, handler, false);

    /// <summary>
    /// Class-level handler with handled-events-too delivery (matches the
    /// four-argument RegisterClassHandler overload).
    /// </summary>
    public static void RegisterClassHandler(Type classType, RoutedEvent routedEvent, Delegate handler, bool handledEventsToo)
    {
        ArgumentNullException.ThrowIfNull(classType);
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);
        lock (Registered)
        {
            ClassHandlers.Add((classType, routedEvent, handler, handledEventsToo));
        }
    }

    /// <summary>
    /// Runs the class handlers registered for the target's type at one route
    /// node (base-type registrations apply to derived targets, in
    /// registration order). Same Handled/handledEventsToo gating and
    /// exception unwrap as the instance path.
    /// </summary>
    internal static void InvokeClassHandlers(UIElement target, RoutedEvent routed, RoutedEventArgs e)
    {
        List<(Delegate Handler, bool HandledToo)> snapshot;
        lock (Registered)
        {
            snapshot = ClassHandlers
                .Where(entry => ReferenceEquals(entry.Event, routed) && entry.Owner.IsInstanceOfType(target))
                .Select(entry => (entry.Handler, entry.HandledToo))
                .ToList();
        }
        foreach (var (handler, handledToo) in snapshot)
        {
            if (e.Handled && !handledToo) continue;
            try
            {
                handler.DynamicInvoke(target, e);
            }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }
    }

    internal static RoutedEvent? Find(Type ownerType, string name)
    {
        lock (Registered)
        {
            for (var t = ownerType; t is not null; t = t.BaseType)
            {
                // Statically-registered routed events enter the table from
                // the owner type's static initializer, which may not have run
                // yet when a Style is parsed before any instance exists
                // (gallery MenuPage resolves MenuItem.Click this way).
                try { RuntimeHelpers.RunClassConstructor(t.TypeHandle); }
                catch (TypeInitializationException) { }
                if (Registered.TryGetValue((t, name), out var routed)) return routed;
            }
            return null;
        }
    }
}

/// <summary>Undo action (matches System.Windows.Controls.UndoAction).</summary>
public enum UndoAction : byte
{
    None,
    Merge,
    Clear,
    Create,
}

/// <summary>Text-change payload (matches System.Windows.Controls.TextChangedEventArgs).</summary>
public class TextChangedEventArgs : RoutedEventArgs
{
    public TextChangedEventArgs(RoutedEvent routedEvent, UndoAction undoAction)
    {
        RoutedEvent = routedEvent;
        UndoAction = undoAction;
    }

    public UndoAction UndoAction { get; }

    /// <summary>Changed ranges (v1 reports none: the engine has no range tracking yet).</summary>
    public System.Collections.IList Changes { get; } = Array.Empty<object>();
}

/// <summary>Text-change handler (matches System.Windows.Controls.TextChangedEventHandler).</summary>
public delegate void TextChangedEventHandler(object sender, TextChangedEventArgs e);

/// <summary>Font family (matches System.Windows.Media.FontFamily subset).</summary>
public sealed class FontFamily : IEquatable<FontFamily>
{
    public FontFamily(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        Source = source;
    }

    /// <summary>Base-URI plus family name (matches FontFamily(Uri, string);
    /// relative names like "./#Roboto" resolve against the base, like WPF).</summary>
    public FontFamily(Uri? baseUri, string familyName)
        : this(CombineFamily(baseUri, familyName))
    {
    }

    private static string CombineFamily(Uri? baseUri, string familyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(familyName);
        if (baseUri is null) return familyName;
        return Uri.TryCreate(baseUri, familyName, out var combined) ? combined.ToString() : familyName;
    }

    /// <summary>Family name or comma list (e.g. "Segoe UI", "sans-serif").</summary>
    public string Source { get; }

    public static implicit operator FontFamily(string source) => new(source);

    /// <summary>Parses a family source (matches the FontFamilyConverter string path; pack URIs round-trip like WPF).</summary>
    public static FontFamily Parse(string source) => new(source);

    public static bool operator ==(FontFamily? left, FontFamily? right) => Equals(left, right);
    public static bool operator !=(FontFamily? left, FontFamily? right) => !Equals(left, right);

    public bool Equals(FontFamily? other) =>
        other is not null && string.Equals(Source, other.Source, StringComparison.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => obj is FontFamily other && Equals(other);
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Source);
    public override string ToString() => Source;
}

/// <summary>Font slant (matches System.Windows.FontStyle).</summary>
public readonly struct FontStyle : IEquatable<FontStyle>
{
    private readonly byte _style;

    private FontStyle(byte style) => _style = style;

    public static FontStyle Normal { get; } = new(0);
    public static FontStyle Oblique { get; } = new(1);
    public static FontStyle Italic { get; } = new(2);

    public static bool operator ==(FontStyle left, FontStyle right) => left._style == right._style;
    public static bool operator !=(FontStyle left, FontStyle right) => left._style != right._style;

    public bool Equals(FontStyle other) => _style == other._style;
    public override bool Equals(object? obj) => obj is FontStyle other && Equals(other);
    public override int GetHashCode() => _style;
    public override string ToString() => _style switch { 1 => "Oblique", 2 => "Italic", _ => "Normal" };
}

/// <summary>Font weight (matches System.Windows.FontWeight).</summary>
public readonly struct FontWeight : IEquatable<FontWeight>, IComparable<FontWeight>
{
    private readonly int _weight;

    private FontWeight(int weight) => _weight = weight;

    public static FontWeight Thin { get; } = new(100);
    public static FontWeight ExtraLight { get; } = new(200);
    public static FontWeight Light { get; } = new(300);
    public static FontWeight Normal { get; } = new(400);
    public static FontWeight Medium { get; } = new(500);
    public static FontWeight SemiBold { get; } = new(600);
    public static FontWeight Bold { get; } = new(700);
    public static FontWeight ExtraBold { get; } = new(800);
    public static FontWeight Black { get; } = new(900);

    /// <summary>Nearest weight for an OpenType value 1-999 (out of range clamps).</summary>
    public static FontWeight FromOpenTypeWeight(int weight) =>
        weight switch
        {
            <= 150 => Thin,
            <= 250 => ExtraLight,
            <= 350 => Light,
            <= 450 => Normal,
            <= 550 => Medium,
            <= 650 => SemiBold,
            <= 750 => Bold,
            <= 850 => ExtraBold,
            _ => Black,
        };

    /// <summary>
    /// Exact weight for a raw OpenType value (matches the FontWeightConverter
    /// numeric path, which preserves values like 450 instead of snapping to
    /// the nearest named weight; ToString renders unnamed values numerically
    /// like WPF). No range check here; callers validate.
    /// </summary>
    internal static FontWeight FromRawWeight(int weight) => new(weight);

    internal int OpenTypeWeight => _weight;

    public static bool operator ==(FontWeight left, FontWeight right) => left._weight == right._weight;
    public static bool operator !=(FontWeight left, FontWeight right) => left._weight != right._weight;
    public static bool operator <(FontWeight left, FontWeight right) => left._weight < right._weight;
    public static bool operator >(FontWeight left, FontWeight right) => left._weight > right._weight;

    public bool Equals(FontWeight other) => _weight == other._weight;
    public override bool Equals(object? obj) => obj is FontWeight other && Equals(other);
    public override int GetHashCode() => _weight;
    public int CompareTo(FontWeight other) => _weight.CompareTo(other._weight);
    public override string ToString() => _weight switch
    {
        100 => "Thin",
        200 => "ExtraLight",
        300 => "Light",
        400 => "Normal",
        500 => "Medium",
        600 => "SemiBold",
        700 => "Bold",
        800 => "ExtraBold",
        900 => "Black",
        // Raw converter weights (e.g. 450) render numerically like WPF
        // instead of collapsing to Normal; all previously constructible
        // values keep their exact strings.
        _ => _weight.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

/// <summary>Font slants (matches System.Windows.FontStyles).</summary>
public static class FontStyles
{
    public static FontStyle Normal => FontStyle.Normal;
    public static FontStyle Oblique => FontStyle.Oblique;
    public static FontStyle Italic => FontStyle.Italic;
}

/// <summary>Font weights (matches System.Windows.FontWeights).</summary>
public static class FontWeights
{
    public static FontWeight Thin => FontWeight.Thin;
    public static FontWeight ExtraLight => FontWeight.ExtraLight;
    public static FontWeight UltraLight => FontWeight.ExtraLight;
    public static FontWeight Light => FontWeight.Light;
    public static FontWeight Normal => FontWeight.Normal;
    public static FontWeight Regular => FontWeight.Normal;
    public static FontWeight Medium => FontWeight.Medium;
    public static FontWeight DemiBold => FontWeight.SemiBold;
    public static FontWeight SemiBold => FontWeight.SemiBold;
    public static FontWeight Bold => FontWeight.Bold;
    public static FontWeight ExtraBold => FontWeight.ExtraBold;
    public static FontWeight UltraBold => FontWeight.ExtraBold;
    public static FontWeight Black => FontWeight.Black;
    public static FontWeight Heavy => FontWeight.Black;
    public static FontWeight ExtraBlack => FontWeight.Black;
    public static FontWeight UltraBlack => FontWeight.Black;
}

/// <summary>Theme selector (matches the System.Windows.ThemeMode contract converted code binds).</summary>
public sealed class ThemeMode : IEquatable<ThemeMode>
{
    private readonly string _name;

    private ThemeMode(string name) => _name = name;

    public static ThemeMode System { get; } = new(nameof(System));
    public static ThemeMode Light { get; } = new(nameof(Light));
    public static ThemeMode Dark { get; } = new(nameof(Dark));

    public static bool operator ==(ThemeMode? left, ThemeMode? right) => Equals(left, right);
    public static bool operator !=(ThemeMode? left, ThemeMode? right) => !Equals(left, right);

    public bool Equals(ThemeMode? other) => other is not null && _name == other._name;
    public override bool Equals(object? obj) => obj is ThemeMode other && Equals(other);
    public override int GetHashCode() => _name.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => _name;
}

/// <summary>Resource-key base (matches System.Windows.ResourceKey).</summary>
public abstract class ResourceKey
{
}

/// <summary>Content-property marker (matches System.Windows.Markup.ContentPropertyAttribute).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ContentPropertyAttribute : Attribute
{
    public ContentPropertyAttribute() => Name = string.Empty;

    public ContentPropertyAttribute(string name) => Name = name;

    public string Name { get; }
}

/// <summary>Template-part contract (matches System.Windows.TemplatePartAttribute).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TemplatePartAttribute : Attribute
{
    /// <summary>Named-argument form ([TemplatePart(Name = ..., Type = ...)]).</summary>
    public TemplatePartAttribute()
    {
    }

    public TemplatePartAttribute(string name, Type type)
    {
        Name = name;
        Type = type;
    }

    public string? Name { get; set; }
    public Type? Type { get; set; }
}

/// <summary>BAML-generated connector (matches System.Windows.Markup.IStyleConnector).</summary>
public interface IStyleConnector
{
    void Connect(int connectionId, object target);
}

/// <summary>System colors (matches the System.Windows.SystemColors core converted code reads).</summary>
public static class SystemColors
{
    private static Color Rgba(uint rgba) => StyleValueConverter.ColorFromRgba(rgba);

    private static SolidColorBrush? _highlightBrush;
    private static SolidColorBrush? _highlightTextBrush;
    private static SolidColorBrush? _windowBrush;
    private static SolidColorBrush? _windowTextBrush;
    private static SolidColorBrush? _controlBrush;
    private static SolidColorBrush? _controlTextBrush;
    private static SolidColorBrush? _grayTextBrush;
    private static SolidColorBrush? _infoBrush;
    private static SolidColorBrush? _infoTextBrush;
    private static SolidColorBrush? _hotTrackBrush;

    public static Color HighlightColor => Rgba(0x3390FFFF);
    public static SolidColorBrush HighlightBrush => _highlightBrush ??= new SolidColorBrush(HighlightColor);
    public static Color HighlightTextColor => Rgba(0xFFFFFFFF);
    public static SolidColorBrush HighlightTextBrush => _highlightTextBrush ??= new SolidColorBrush(HighlightTextColor);
    public static Color WindowColor => Rgba(0xFFFFFFFF);
    public static SolidColorBrush WindowBrush => _windowBrush ??= new SolidColorBrush(WindowColor);
    public static Color WindowTextColor => Rgba(0x000000FF);
    public static SolidColorBrush WindowTextBrush => _windowTextBrush ??= new SolidColorBrush(WindowTextColor);
    public static Color ControlColor => Rgba(0xF0F0F0FF);
    public static SolidColorBrush ControlBrush => _controlBrush ??= new SolidColorBrush(ControlColor);
    public static Color ControlTextColor => Rgba(0x000000FF);
    public static SolidColorBrush ControlTextBrush => _controlTextBrush ??= new SolidColorBrush(ControlTextColor);
    public static Color GrayTextColor => Rgba(0x6D6D6DFF);
    public static SolidColorBrush GrayTextBrush => _grayTextBrush ??= new SolidColorBrush(GrayTextColor);
    public static Color InfoColor => Rgba(0xFFFFE1FF);
    public static SolidColorBrush InfoBrush => _infoBrush ??= new SolidColorBrush(InfoColor);
    public static Color InfoTextColor => Rgba(0x000000FF);
    public static SolidColorBrush InfoTextBrush => _infoTextBrush ??= new SolidColorBrush(InfoTextColor);
    public static Color HotTrackColor => Rgba(0x0066CCFF);
    public static SolidColorBrush HotTrackBrush => _hotTrackBrush ??= new SolidColorBrush(HotTrackColor);

    private static SolidColorBrush? _accentBrush;
    private static SolidColorBrush? _accentDark1Brush;
    private static SolidColorBrush? _accentDark2Brush;
    private static SolidColorBrush? _accentDark3Brush;
    private static SolidColorBrush? _accentLight1Brush;
    private static SolidColorBrush? _accentLight2Brush;
    private static SolidColorBrush? _accentLight3Brush;

    /// <summary>Default Windows accent (matches the Fluent theme default).</summary>
    public static Color AccentColor => Rgba(0x0078D4FF);
    /// <summary>Default Windows accent brush.</summary>
    public static SolidColorBrush AccentBrush => _accentBrush ??= new SolidColorBrush(AccentColor);
    public static Color AccentColorDark1 => Rgba(0x0067B8FF);
    public static SolidColorBrush AccentDark1Brush => _accentDark1Brush ??= new SolidColorBrush(AccentColorDark1);
    public static Color AccentColorDark2 => Rgba(0x005494FF);
    public static SolidColorBrush AccentDark2Brush => _accentDark2Brush ??= new SolidColorBrush(AccentColorDark2);
    public static Color AccentColorDark3 => Rgba(0x003966FF);
    public static SolidColorBrush AccentDark3Brush => _accentDark3Brush ??= new SolidColorBrush(AccentColorDark3);
    public static Color AccentColorLight1 => Rgba(0x1A86D9FF);
    public static SolidColorBrush AccentLight1Brush => _accentLight1Brush ??= new SolidColorBrush(AccentColorLight1);
    public static Color AccentColorLight2 => Rgba(0x4BA0E1FF);
    public static SolidColorBrush AccentLight2Brush => _accentLight2Brush ??= new SolidColorBrush(AccentColorLight2);
    public static Color AccentColorLight3 => Rgba(0x83BDEAFF);
    public static SolidColorBrush AccentLight3Brush => _accentLight3Brush ??= new SolidColorBrush(AccentColorLight3);

    /// <summary>Accent resource keys (match SystemColors.Accent*BrushKey; looked up by name).</summary>
    public static ResourceKey AccentColorBrushKey { get; } = new SystemColorResourceKey("AccentColorBrushKey");
    public static ResourceKey AccentColorDark1BrushKey { get; } = new SystemColorResourceKey("AccentColorDark1BrushKey");
    public static ResourceKey AccentColorDark2BrushKey { get; } = new SystemColorResourceKey("AccentColorDark2BrushKey");
    public static ResourceKey AccentColorDark3BrushKey { get; } = new SystemColorResourceKey("AccentColorDark3BrushKey");
    public static ResourceKey AccentColorLight1BrushKey { get; } = new SystemColorResourceKey("AccentColorLight1BrushKey");
    public static ResourceKey AccentColorLight2BrushKey { get; } = new SystemColorResourceKey("AccentColorLight2BrushKey");
    public static ResourceKey AccentColorLight3BrushKey { get; } = new SystemColorResourceKey("AccentColorLight3BrushKey");

    /// <summary>Active-caption resource key (matches SystemColors.ActiveCaptionBrushKey).</summary>
    public static ResourceKey ActiveCaptionBrushKey { get; } = new SystemColorResourceKey("ActiveCaption");

    /// <summary>Inactive-caption resource key (matches SystemColors.InactiveCaptionBrushKey).</summary>
    public static ResourceKey InactiveCaptionBrushKey { get; } = new SystemColorResourceKey("InactiveCaption");

    /// <summary>Control-face resource key (matches SystemColors.ControlBrushKey).</summary>
    public static ResourceKey ControlBrushKey { get; } = new SystemColorResourceKey("ControlBrush");

    /// <summary>Control-text resource key (matches SystemColors.ControlTextBrushKey).</summary>
    public static ResourceKey ControlTextBrushKey { get; } = new SystemColorResourceKey("ControlTextBrush");

    /// <summary>Highlight resource key (matches SystemColors.HighlightBrushKey).</summary>
    public static ResourceKey HighlightBrushKey { get; } = new SystemColorResourceKey("HighlightBrush");
}

/// <summary>System resource key (matches WPF's SystemResourceKey for SystemColors keys).</summary>
public sealed class SystemColorResourceKey : ResourceKey
{
    internal SystemColorResourceKey(string name) => Name = name;

    public string Name { get; }

    public override string ToString() => Name;
}

/// <summary>System-metric resource key (matches WPF's SystemResourceKey for
/// SystemParameters keys, live-verified on net10: InternalKey
/// VerticalScrollBarButtonHeight with a double Resource payload, singleton
/// per member with stable hash/Equals). Engine keys are singletons too, so a
/// BAML-loaded entry and a later {x:Static SystemParameters.*Key} lookup
/// share identity through the static property.</summary>
public sealed class SystemResourceKey : ResourceKey
{
    internal SystemResourceKey(string name) => Name = name;

    public string Name { get; }

    public override string ToString() => Name;
}

/// <summary>
/// System resource values (matches the WPF system scope): resolves the
/// engine SystemColors/SystemParameters singletons from their resource
/// keys (or the key names as strings, for the element-attribute path that
/// stringifies x:Static keys). Consulted last, so theme and app entries
/// always win; engine values are fixed WASM defaults.
/// </summary>
internal static class SystemTheme
{
    internal static bool TryFindSystemResource(object key, out object? value)
    {
        var name = key switch
        {
            SystemColorResourceKey colorKey => colorKey.Name,
            SystemResourceKey metricKey => metricKey.Name,
            string text => text,
            _ => null,
        };
        value = name switch
        {
            "ControlBrush" => SystemColors.ControlBrush,
            "ControlTextBrush" => SystemColors.ControlTextBrush,
            "HighlightBrush" => SystemColors.HighlightBrush,
            "HighlightTextBrush" => SystemColors.HighlightTextBrush,
            "AccentBrush" or "AccentColorBrush" => SystemColors.AccentBrush,
            "VerticalScrollBarWidth" => SystemParameters.VerticalScrollBarWidth,
            "HorizontalScrollBarHeight" => SystemParameters.HorizontalScrollBarHeight,
            "HorizontalScrollBarButtonWidth" => SystemParameters.HorizontalScrollBarButtonWidth,
            "VerticalScrollBarButtonHeight" => 17.0,
            "ComboBoxPopupAnimation" => SystemParameters.ComboBoxPopupAnimation,
            _ => null,
        };
        return value is not null;
    }
}

/// <summary>Style setter base (matches System.Windows.SetterBase).</summary>
public abstract class SetterBase
{
    internal abstract void ApplyTo(FrameworkElement target, StyleApplyContext context);
}

/// <summary>Style setter collection (matches System.Windows.SetterBaseCollection).</summary>
public sealed class SetterBaseCollection : Collection<SetterBase>
{
    internal bool Sealed { get; set; }

    protected override void InsertItem(int index, SetterBase item)
    {
        if (Sealed) throw new InvalidOperationException("This Style is sealed and cannot be modified.");
        ArgumentNullException.ThrowIfNull(item);
        base.InsertItem(index, item);
    }

    protected override void SetItem(int index, SetterBase item)
    {
        if (Sealed) throw new InvalidOperationException("This Style is sealed and cannot be modified.");
        ArgumentNullException.ThrowIfNull(item);
        base.SetItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        if (Sealed) throw new InvalidOperationException("This Style is sealed and cannot be modified.");
        base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        if (Sealed) throw new InvalidOperationException("This Style is sealed and cannot be modified.");
        base.ClearItems();
    }
}

/// <summary>Carries string-handler EventSetters out of Style.ApplyTo for load-time wiring.</summary>
internal sealed class StyleApplyContext
{
    public List<(string Event, string Handler)>? StringHandlers { get; set; }

    /// <summary>Style being applied (dynamic setters subscribe its re-application).</summary>
    internal Style? Owner { get; set; }

    /// <summary>
    /// Refresh passes skip event wiring (handlers were bound on first apply;
    /// re-adding them would double-invoke).
    /// </summary>
    internal bool SkipEventWires { get; set; }

    /// <summary>Lexical load scope for document-order lookups (null once the tree is linked).</summary>
    internal IReadOnlyList<FrameworkElement>? Scope { get; set; }

    /// <summary>
    /// Value provenance recorded by this apply pass (matches WPF precedence:
    /// plain style application reports Style, property/data triggers report
    /// StyleTrigger, template triggers TemplateTrigger, converted default
    /// styles DefaultStyle). DependencyPropertyHelper reads it back.
    /// </summary>
    internal BaseValueSource Source { get; set; } = BaseValueSource.Style;
}

/// <summary>Property setter (matches System.Windows.Setter).</summary>
public sealed class Setter : SetterBase
{
    public Setter()
    {
    }

    public Setter(DependencyProperty property, object? value)
        : this(property, value, null)
    {
    }

    public Setter(DependencyProperty property, object? value, string? targetName)
    {
        Property = property;
        Value = value;
        TargetName = targetName;
    }

    public DependencyProperty Property { get; set; } = null!;
    public object? Value { get; set; }
    public string? TargetName { get; set; }

    /// <summary>Unconverted XAML literal (the loader fills this; ApplyTo converts it).</summary>
    internal string? RawValue { get; set; }

    /// <summary>Attached setter name (Grid.Row/DockPanel.Dock style setters; Value is pre-parsed).</summary>
    internal string? AttachedProperty { get; set; }

    internal override void ApplyTo(FrameworkElement target, StyleApplyContext context)
    {
        if (TargetName is not null)
            throw new InvalidOperationException(
                $"Setter TargetName '{TargetName}' targets a template child (templates stay a precise boundary).");
        ApplyCore(target, context);
    }

    /// <summary>Applies a template trigger setter to its resolved part
    /// (the TargetName was resolved by the template applier).</summary>
    internal void ApplyToTemplatePart(FrameworkElement part, StyleApplyContext context) =>
        ApplyCore(part, context);

    /// <summary>Resolves the DP a trigger binding value addresses
    /// (plain names on the target type; dotted names strip the BAML-kept
    /// xmlns prefix and read engine, staged-entry then loaded converted
    /// owners, mirroring the template transfer).</summary>
    internal static DependencyProperty? ResolveTriggerBindingTarget(FrameworkElement target, string key)
    {
        var colon = key.IndexOf(':');
        var dotFirst = key.IndexOf('.');
        if (colon > 0 && colon < dotFirst)
            key = key[(colon + 1)..];
        var dot = key.IndexOf('.');
        if (dot > 0)
        {
            var owner = XamlLoader.FindEngineType(key[..dot])
                ?? XamlLoader.FindConvertedType(key[..dot])
                ?? TemplateApplier.FindLoadedConvertedType(key[..dot]);
            return owner is null ? null : DependencyProperty.FromName(owner, key[(dot + 1)..]);
        }
        return DependencyProperty.FromName(target.GetType(), key);
    }

    private void ApplyCore(FrameworkElement target, StyleApplyContext context)
    {
        if (AttachedProperty is string attached)
        {
            if (ResolveResourceValue(target, attached, context, attached) is not { } attachedValue)
                return;
            if (attachedValue is BindingBase triggerBinding)
            {
                // Trigger-set {Binding} values: live-bind the addressed DP
                // when the trigger activates (hover overlays); restore clears
                // the transfer so the base value or entry resumes, like WPF.
                var bindTarget = ResolveTriggerBindingTarget(target, attached);
                if (bindTarget is null)
                    throw new InvalidOperationException($"Trigger binding value for '{attached}' needs a dependency property on <{target.GetType().Name}>.");
                BindingOperations.SetBinding(target, bindTarget, triggerBinding);
                target.SetSourceTag(bindTarget, context.Source);
                return;
            }
            if (!attached.Contains('.'))
            {
                // Plain trigger-setter names address the CLR property first
                // (Background/Foreground/Visibility...), then a same-named
                // registered DP, then the attached store (matches reads).
                // The value coerces to the CLR type: theme dictionaries pack
                // brushes as Color/uint, which would otherwise land raw on a
                // Brush property (the PC Space Map ComboBox template triggers
                // drive BorderBrush off the Accent theme key).
                var clr = StylePropertyCache.Find(target.GetType(), attached);
                if (clr is not null && clr.CanWrite)
                {
                    clr.SetValue(target, CoerceResourceValue(attachedValue, clr.PropertyType, attached));
                    // The CLR mirror bypasses the DP store; tag the provenance
                    // anyway when the styled DP is registered (the style is
                    // the effective value's source either way). Missing local
                    // entries keep the default source (SetSourceTag no-ops).
                    var styled = DependencyProperty.FromName(target.GetType(), attached);
                    if (styled is not null) target.SetSourceTag(styled, context.Source);
                    return;
                }
                var dpDirect = DependencyProperty.FromName(target.GetType(), attached);
                if (dpDirect is not null)
                {
                    target.SetValueWithSource(dpDirect, CoerceResourceValue(attachedValue, dpDirect.PropertyType, attached), context.Source);
                    return;
                }
            }
            target.SetAttached(attached, attachedValue);
            return;
        }
        var dp = Property ?? throw new InvalidOperationException("Setter.Property must be set before the style is applied.");
        if (RawValue is null && AttachedProperty is null && Value is BindingBase styleBinding)
        {
            // BAML/text-landed Binding setter values (the staged Gripper
            // Height TemplatedParent transfer): activate per-element like WPF
            // instead of storing the Binding object as the local value.
            BindingOperations.SetBinding(target, dp, styleBinding);
            target.SetSourceTag(dp, context.Source);
            return;
        }
        if (dp.IsAttached && !dp.OwnerType.IsAssignableFrom(target.GetType()))
        {
            // Owner-qualified attached setter from another type
            // (ScrollViewer.* on a TextBox): addresses the DP store like
            // WPF, never a same-named CLR property on the styled target.
            // The attached bag lands too (legacy GetAttached readers keep
            // working, like the element/template dual landing).
            // Non-attached DPs (Control.FontSize on a TextBlock) keep the
            // CLR-first order their readers bind.
            if (ResolveResourceValue(target, dp.PropertyType, context, dp.Name) is { } qualified)
            {
                target.SetValueWithSource(dp, qualified, context.Source);
                target.SetAttached(dp.OwnerType.Name + "." + dp.Name, qualified);
            }
            return;
        }
        var prop = StylePropertyCache.Find(target.GetType(), dp.Name);
        if (prop is not null && prop.CanWrite)
        {
            try
            {
                if (ResolveResourceValue(target, prop.PropertyType, context, prop.Name) is { } value)
                    prop.SetValue(target, value);
                // CLR-first path (Control chrome setters bypass the store):
                // tag the provenance like the attached-name path above.
                target.SetSourceTag(dp, context.Source);
                return;
            }
            catch (TypeLoadException)
            {
                // Unloadable converted CLR signatures (staged-image property
                // blobs the converter never rewrote, e.g. OxyPlot
                // DefaultTrackerTemplate): the DP store is the value
                // authority, so land the converted value there exactly as
                // the CLR wrapper would. Narrow catch: only unloadable
                // signatures fall back; conversion and setter errors stay loud.
            }
        }
        if (ResolveResourceValue(target, dp.PropertyType, context, dp.Name) is { } dpValue)
            target.SetValueWithSource(dp, dpValue, context.Source);
    }

    /// <summary>
    /// Resolves a setter value: plain literals convert eagerly, StaticResource
    /// looks up the target scope now (missing is an error, like WPF), and
    /// DynamicResource resolves-or-skips plus subscribes the owning style
    /// for refresh (missing keys stay unset until they appear, like WPF).
    /// Null means "leave the current value" (missing dynamic key).
    /// </summary>
    private object? ResolveResourceValue(FrameworkElement target, object propertyOrType, StyleApplyContext context, string? propertyName = null)
    {
        var propertyType = propertyOrType as Type ?? typeof(object);
        var key = propertyOrType as string;
        if (RawValue is null)
        {
            // Trigger setter resource references resolve against the target
            // scope here (styles and inflated templates share this path):
            // missing dynamic keys skip, mistyped resources are an error.
            if (Value is TriggerResourceReference reference)
            {
                var resolved = TriggerEvaluation.ResolveExpected(target, reference);
                if (resolved is null) return null;
                return CoerceResourceValue(resolved, propertyType, "trigger:" + reference.Key);
            }
            return Value;
        }
        var raw = RawValue.Trim();
        // {x:Null} clears to the default (FocusVisualStyle on theme styles):
        // null means "leave the current value", which is already the default.
        if (string.Equals(raw, "{x:Null}", StringComparison.OrdinalIgnoreCase)) return null;
        if (raw.StartsWith("{x:Static ", StringComparison.Ordinal) && raw.EndsWith('}'))
        {
            // BAML-landed statics (Padding="{x:Static
            // wpf:Constants.TextBoxDefaultPadding}"): resolve through the
            // shared member path, then coerce to the setter type like WPF.
            var member = raw["{x:Static ".Length..^1].Trim();
            return CoerceResourceValue(ResolveApplyTimeStatic(member), propertyType, RawValue);
        }
        if (raw.StartsWith("{StaticResource ", StringComparison.Ordinal) && raw.EndsWith('}'))
        {
            var resourceKey = raw["{StaticResource ".Length..^1].Trim();
            if (string.IsNullOrEmpty(resourceKey) || resourceKey.Contains('{') || resourceKey.Contains('='))
                throw new InvalidOperationException($"'{RawValue}' takes a plain key only.");
            if (!target.TryFindResource(resourceKey, out var resource) || resource is null)
            {
                // In-load document order (sibling dictionaries above the
                // target land before its own subtree links into the live tree).
                resource = Style.TryFindValueInLoadScope(target, context.Scope, resourceKey);
                if (resource is null &&
                    !ThemeDictionaries.TryFindThemeValue(target, resourceKey, out resource))
                    throw new InvalidOperationException($"StaticResource '{resourceKey}' was not found in element, ancestor or application resources.");
            }
            return CoerceResourceValue(resource, propertyType, RawValue);
        }
        if (raw.StartsWith("{DynamicResource ", StringComparison.Ordinal) && raw.EndsWith('}'))
        {
            var resourceKey = raw["{DynamicResource ".Length..^1].Trim();
            if (string.IsNullOrEmpty(resourceKey) || resourceKey.Contains('{') || resourceKey.Contains('='))
                throw new InvalidOperationException($"'{RawValue}' takes a plain key only.");
            if (context.Owner is not null)
                Application.DynamicResourceManager.SubscribeStyle(target, context.Owner);
            if (!target.TryFindResource(resourceKey, out var resource) || resource is null)
            {
                // In-load document order resolves window-level keys at load;
                // the refresh subscription above still tracks later changes.
                if (Style.TryFindValueInLoadScope(target, context.Scope, resourceKey) is { } scoped)
                    return CoerceResourceValue(scoped, propertyType, RawValue);
                return key is null ? null : Value;
            }
            return CoerceResourceValue(resource, propertyType, RawValue);
        }
        // LengthConverter parity (native maps "Auto" to NaN for the six
        // dimension properties; other doubles keep the strict parse).
        if (propertyType == typeof(double) && propertyName is "Width" or "Height" or
            "MinWidth" or "MaxWidth" or "MinHeight" or "MaxHeight" &&
            string.Equals(raw, "Auto", StringComparison.OrdinalIgnoreCase))
            return double.NaN;
        if (key is not null) return Value;
        try { return StyleValueConverter.Convert(raw, propertyType); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException or ArgumentException)
        {
            throw new InvalidOperationException($"'{raw}' is not a {propertyType.Name} for '{propertyName}' (no string conversion).", ex);
        }
    }

    /// <summary>Resolves a "Type.Member" public static property or field at
    /// apply time (engine, then the staged entry, then a unique scan across
    /// the loaded converted images; the BAML-kept xmlns prefix strips like
    /// the template transfer). Failures stay InvalidOperationException with
    /// the raw text, since apply time carries no line info.</summary>
    private static object? ResolveApplyTimeStatic(string member)
    {
        var dot = member.LastIndexOf('.');
        if (dot <= 0 || dot == member.Length - 1 || member.Contains(' ') || member.Contains('{'))
            throw new InvalidOperationException($"'{member}' is not a static member reference (Type.Member).");
        var typeName = member[..dot];
        var memberName = member[(dot + 1)..];
        var shortName = typeName.Contains(':') ? typeName[(typeName.IndexOf(':') + 1)..] : typeName;
        var lookupName = shortName;
        string? nestedName = null;
        var plus = shortName.IndexOf('+');
        if (plus > 0 && plus < shortName.Length - 1)
        {
            lookupName = shortName[..plus];
            nestedName = shortName[(plus + 1)..];
        }
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy;
        foreach (var candidate in new[]
            {
                XamlLoader.FindEngineType(lookupName),
                XamlLoader.FindConvertedType(lookupName),
                TemplateApplier.FindLoadedConvertedType(lookupName, memberName),
            })
        {
            if (candidate is null) continue;
            var owner = nestedName is null ? candidate
                : candidate.GetNestedType(nestedName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (owner is null) continue;
            if (owner.GetProperty(memberName, flags)?.CanRead == true)
                return owner.GetProperty(memberName, flags)!.GetValue(null);
            if (owner.GetField(memberName, flags) is { } field)
                return field.GetValue(null);
        }
        throw new InvalidOperationException($"Static member '{member}' resolved to no public static property or field.");
    }

    /// <summary>Converts a resolved resource to the setter's property type.</summary>
    internal static object? CoerceResourceValue(object? resource, Type propertyType, string rawForError)
    {
        if (resource is null)
            throw new InvalidOperationException($"'{rawForError}' resolved to null, not {propertyType.Name}.");
        if (propertyType.IsInstanceOfType(resource)) return resource;
        // Theme dictionaries store SolidColorBrush/Color entries as packed
        // engine ARGB (uint); brush/color-typed setters take them as brushes.
        if (resource is uint rgba)
        {
            if (propertyType != typeof(object) && propertyType.IsAssignableFrom(typeof(SolidColorBrush)))
                return new SolidColorBrush(StyleValueConverter.ColorFromRgba(rgba));
            if (propertyType.IsAssignableFrom(typeof(Color)))
                return StyleValueConverter.ColorFromRgba(rgba);
        }
        if (resource is Color color && propertyType != typeof(object) &&
            propertyType.IsAssignableFrom(typeof(SolidColorBrush)))
            return new SolidColorBrush(color);
        if (resource is string text)
        {
            try { return StyleValueConverter.Convert(text, propertyType); }
            catch (FormatException ex) { throw new InvalidOperationException(ex.Message); }
        }
        throw new InvalidOperationException($"'{rawForError}' resolved to {resource.GetType().Name}, not {propertyType.Name}.");
    }
}

/// <summary>Event setter (matches System.Windows.EventSetter).</summary>
public sealed class EventSetter : SetterBase
{
    public EventSetter()
    {
    }

    public EventSetter(RoutedEvent routedEvent, Delegate handler)
    {
        Event = routedEvent;
        Handler = handler;
    }

    public RoutedEvent Event { get; set; } = null!;
    public Delegate? Handler { get; set; }

    /// <summary>XAML handler name (resolved against the component at load; ApplyTo collects it).</summary>
    internal string? HandlerName { get; set; }

    internal override void ApplyTo(FrameworkElement target, StyleApplyContext context)
    {
        // Refresh re-applications skip wiring (handlers were bound on first
        // apply; re-adding them would double-invoke).
        if (context.SkipEventWires) return;
        var routed = Event ?? throw new InvalidOperationException("EventSetter.Event must be set before the style is applied.");
        if (Handler is Delegate handler)
        {
            Attach(target, routed, handler);
            return;
        }
        if (HandlerName is string name)
        {
            if (context.StringHandlers is null)
                throw new InvalidOperationException(
                    $"EventSetter '{routed.Name}' names handler '{name}', which resolves only against the loading component (use a code EventSetter with a Delegate, or load through XAML).");
            context.StringHandlers.Add((routed.Name, name));
            return;
        }
        throw new InvalidOperationException($"EventSetter '{routed.Name}' has neither Handler nor HandlerName.");
    }

    private static void Attach(FrameworkElement target, RoutedEvent routed, Delegate handler)
    {
        var evt = target.GetType().GetEvent(routed.Name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
        if (evt is not null)
        {
            if (evt.EventHandlerType is not null && evt.EventHandlerType.IsInstanceOfType(handler))
            {
                evt.AddEventHandler(target, handler);
                return;
            }
            try
            {
                var compatible = Delegate.CreateDelegate(evt.EventHandlerType!, handler.Target, handler.Method, throwOnBindFailure: false);
                if (compatible is not null)
                {
                    evt.AddEventHandler(target, compatible);
                    return;
                }
            }
            catch (ArgumentException)
            {
                // Fall through to the handler table below.
            }
        }
        target.AddHandler(routed, handler);
    }
}

/// <summary>CLR-property lookup cache for style application.</summary>
internal static class StylePropertyCache
{
    private static readonly Dictionary<(Type, string), PropertyInfo?> Cache = new();

    internal static PropertyInfo? Find(Type targetType, string name)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue((targetType, name), out var cached)) return cached;
            for (var t = targetType; t is not null && t != typeof(object); t = t.BaseType)
            {
                var prop = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (prop is not null)
                {
                    Cache[(targetType, name)] = prop;
                    return prop;
                }
            }
            Cache[(targetType, name)] = null;
            return null;
        }
    }
}

/// <summary>Converts XAML setter literals to the target property type.</summary>
internal static class StyleValueConverter
{
    internal static object? Convert(string raw, Type targetType)
    {
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (type == typeof(string)) return raw;
        // BrushConverter parity: known color names (and #hex) convert for
        // every brush target, not just Brush (theme setters type some
        // brush properties as SolidColorBrush; resource-indirected strings
        // like DrawerHost's "Black" land here too).
        if (type == typeof(Brush) || type == typeof(SolidColorBrush))
            return new SolidColorBrush(ColorFromRgba(XamlLoader.ParseColorCore(raw)));
        // ColorConverter parity: the same names convert to Color.
        if (type == typeof(Color)) return ColorFromRgba(XamlLoader.ParseColorCore(raw));
        if (type == typeof(Point)) return Point.Parse(raw);
        if (type == typeof(Rect)) return RectangleGeometry.ParseRect(raw);
        if (type == typeof(Size)) return Size.Parse(raw);
        if (type == typeof(FontFamily)) return new FontFamily(raw);
        if (type == typeof(FontWeight)) return ParseFontWeight(raw);
        if (type == typeof(FontStyle)) return ParseFontStyle(raw);
        if (type == typeof(TextDecorationCollection)) return TextDecorations.Parse(raw);
        if (type == typeof(Thickness)) return Thickness.Parse(raw);
        if (type == typeof(CornerRadius)) return CornerRadius.Parse(raw);
        // Converted HandyControl attached DPs (TitleElement.TitleWidth) type
        // their widths as GridLength: route through the verified converter
        // like real-WPF XAML parsing (Auto, star weights, px/in/cm/pt).
        if (type == typeof(GridLength))
            return new GridLengthConverter().ConvertFrom(
                null, System.Globalization.CultureInfo.InvariantCulture, raw);
        if (type == typeof(DoubleCollection)) return DoubleCollection.Parse(raw);
        if (type == typeof(PointCollection)) return PointCollection.Parse(raw);
        if (typeof(Geometry).IsAssignableFrom(type))
        {
            var geometry = Geometry.Parse(raw);
            if (type.IsInstanceOfType(geometry)) return geometry;
            throw new InvalidOperationException($"'{raw}' parsed to {geometry.GetType().Name}, not {type.Name}.");
        }
        if (type == typeof(Cursor)) return ParseCursor(raw);
        // Pack-URI image sources (matches the <Image Source="...">, Window.Icon
        // and BitmapImage resource-entry paths: the BitmapImage stays lazy, so
        // staged views can name entry pack PNG/ICO files through converted
        // attached properties like hc:ImageAttach.SourceFailed without
        // eager decoding; garbage URIs stay loud like the element paths).
        if (typeof(ImageSource).IsAssignableFrom(type))
            return new BitmapImage(new Uri(raw, UriKind.RelativeOrAbsolute));
        if (type.IsEnum) return Enum.Parse(type, raw);
        if (type == typeof(double)) return double.Parse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
        if (type == typeof(int)) return int.Parse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture);
        if (type == typeof(bool)) return bool.Parse(raw);
        try { return System.Convert.ChangeType(raw, type, System.Globalization.CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            throw new InvalidOperationException($"'{raw}' cannot convert to {type.Name} (no string conversion).", ex);
        }
    }

    /// <summary>Splits a 0xRRGGBBAA engine value into a Color (R high, A low).</summary>
    internal static Color ColorFromRgba(uint rgba) =>
        Color.FromArgb((byte)rgba, (byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8));

    internal static FontWeight ParseFontWeight(string raw)
    {
        var prop = typeof(FontWeights).GetProperty(raw, BindingFlags.Static | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (prop?.GetValue(null) is FontWeight named) return named;
        if (int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n))
            return FontWeight.FromOpenTypeWeight(n);
        throw new FormatException($"'{raw}' is not a FontWeight (SemiBold/Bold/... or 100-900).");
    }

    internal static FontStyle ParseFontStyle(string raw) =>
        raw switch
        {
            "Normal" => FontStyles.Normal,
            "Italic" => FontStyles.Italic,
            "Oblique" => FontStyles.Oblique,
            _ => throw new FormatException($"'{raw}' is not a FontStyle (Normal/Italic/Oblique)."),
        };

    /// <summary>
    /// Parses a cursor name (matches System.Windows.Input.CursorConverter for
    /// the stock cursors: Arrow/Hand/Wait/...; custom .cur files stay a
    /// boundary like the rest of the cursor layer).
    /// </summary>
    internal static Cursor ParseCursor(string raw)
    {
        var prop = typeof(Cursors).GetProperty(raw.Trim(),
            BindingFlags.Static | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (prop?.GetValue(null) is Cursor named) return named;
        throw new FormatException($"'{raw}' is not a Cursor (Arrow/Cross/Hand/IBeam/Wait/...).");
    }
}

/// <summary>
/// Style picker (matches System.Windows.Controls.StyleSelector): converted
/// AvalonDock's DockingManager exposes a LayoutItemContainerStyleSelector
/// whose SelectStyle result (or the fixed LayoutItemContainerStyle) lands on
/// each layout item. The base returns null; selectors override per item.
/// </summary>
public class StyleSelector
{
    /// <summary>Chooses the item style (base returns null; selectors override).</summary>
    public virtual Style? SelectStyle(object item, DependencyObject container) => null;
}

/// <summary>Runtime style (matches System.Windows.Style).</summary>
public sealed class Style
{
    public Style()
    {
    }

    public Style(Type targetType)
        : this(targetType, null)
    {
    }

    public Style(Type targetType, Style? basedOn)
    {
        TargetType = targetType;
        BasedOn = basedOn;
    }

    public Type? TargetType { get; set; }
    public Style? BasedOn { get; set; }

    /// <summary>
    /// Deferred base-style reference from XAML BasedOn="{StaticResource ...}"
    /// (resolved against application resources at apply time, when the whole
    /// dictionary is loaded, like WPF's load-order handling).
    /// </summary>
    internal object? BasedOnKey { get; set; }
    public SetterBaseCollection Setters { get; } = new();

    /// <summary>Local resource scope (matches Style.Resources; implicit part
    /// styles land here, like WPF).</summary>
    public ResourceDictionary Resources { get; } = new();

    /// <summary>Style triggers (evaluated on apply and on their condition sources).</summary>
    public TriggerCollection Triggers { get; } = new();

    /// <summary>Sealed styles reject mutation (applying seals, like real WPF).</summary>
    public bool IsSealed => Setters.Sealed;

    /// <summary>Seals the style (further setter edits throw).</summary>
    public void Seal() => Setters.Sealed = true;

    /// <summary>Applies BasedOn first, then this style's setters (derived wins).</summary>
    public void ApplyTo(FrameworkElement target) => ApplyTo(target, new StyleApplyContext());

    internal void ApplyTo(FrameworkElement target, StyleApplyContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (TargetType is not null && !TargetType.IsInstanceOfType(target))
            throw new InvalidOperationException(
                $"Style targets {TargetType.Name} and cannot apply to <{target.GetType().Name}>.");
        Seal();
        context.Owner ??= this;
        var basedOn = BasedOn ?? (BasedOnKey is not null ? ResolveBasedOn(target, BasedOnKey, context) : null);
        basedOn?.ApplyTo(target, context);
        foreach (var setter in Setters) setter.ApplyTo(target, context);
        if (Triggers.Count > 0)
        {
            TriggerEvaluation.ApplyTriggers(target, this, context);
            TriggerEvaluation.SubscribeTriggers(target, this);
        }
    }

    private static Style ResolveBasedOn(FrameworkElement target, object key, StyleApplyContext context)
    {
        // In-load document order first (sibling dictionaries above the
        // target land before its own subtree links into the live tree),
        // then the linked tree, like WPF's apply-time lookup.
        if (TryFindValueInLoadScope(target, context.Scope, key) is { } scoped && scoped is Style scopedStyle)
            return scopedStyle;
        if (target.TryFindResource(key, out var resource) && resource is Style based)
            return based;
        // Named theme styles (MaterialDesignPopupBox): string-keyed vendor
        // generic.xaml entries resolve through the target's theme chain
        // like style StaticResource does.
        if (ThemeDictionaries.TryFindThemeValue(target, key, out var themed) && themed is Style themedStyle)
            return themedStyle;
        // BasedOn="{StaticResource {x:Type ...}}" names the implicit style
        // for that type: unkeyed theme styles are keyed by Type, so the
        // defining assembly's generic.xaml resolves them like WPF's theme
        // lookup (exact key only, like a resource lookup).
        if (key is Type basedType && TryFindImplicitBaseStyle(basedType) is { } implicitBase)
            return implicitBase;
        throw new InvalidOperationException(
            $"BasedOn refers to '{key}', which is not a Style in element, ancestor or application resources.");
    }

    /// <summary>Finds the implicit theme style for an exact type key.</summary>
    private static Style? TryFindImplicitBaseStyle(Type basedType)
    {
        var dict = ThemeDictionaries.EnsureForAssembly(basedType.Assembly);
        if (dict is not null && dict.TryGetValue(basedType, out var candidate) &&
            candidate is Style found && found.TargetType is not null)
            return found;
        return null;
    }

    /// <summary>
    /// Document-order lookup for in-load style resolution (mirrors the
    /// loader's lexical scope: the element, then innermost-first ancestors;
    /// partially populated ancestor dictionaries apply).
    /// </summary>
    internal static object? TryFindValueInLoadScope(FrameworkElement target, IReadOnlyList<FrameworkElement>? scope, object key)
    {
        if (target.Resources.TryGetValue(key, out var own) && own is not null) return own;
        if (scope is not null)
            for (var i = scope.Count - 1; i >= 0; i--)
                if (scope[i].Resources.TryGetValue(key, out var ancestor) && ancestor is not null) return ancestor;
        return null;
    }
}

/// <summary>Template trigger collection (matches System.Windows.TriggerCollection).</summary>
public sealed class TriggerCollection : Collection<TriggerBase>
{
}

/// <summary>
/// Style-trigger evaluation (matches WPF's trigger engine for the
/// property/data forms): conditions evaluate on apply; dependency-property
/// changes, mouse hover flips, bound-source pushes, static metric
/// broadcasts and resource refreshes re-apply the whole style (base, then
/// setters, then active trigger setters, so inactive triggers revert).
/// </summary>
internal static class TriggerEvaluation
{
    internal static void ApplyTriggers(FrameworkElement target, Style style, StyleApplyContext context)
    {
        var refresh = new StyleApplyContext { StringHandlers = context.StringHandlers, Owner = style, SkipEventWires = true, Scope = context.Scope, Source = BaseValueSource.StyleTrigger };
        foreach (var trigger in style.Triggers)
        {
            var active = trigger switch
            {
                Trigger property when property.SourceName is not null =>
                    throw new InvalidOperationException("Trigger SourceName is template-only (style triggers have no namescope)."),
                Trigger property => IsPropertyActive(target, property.Property, property.Value),
                MultiTrigger multi => multi.Conditions.All(c => IsPropertyConditionActive(target, c)),
                DataTrigger data => data.Binding is not null && IsBindingActive(target, data.Binding, data.Value),
                MultiDataTrigger multiData => multiData.Conditions.All(c =>
                    c.Binding is not null && IsBindingActive(target, c.Binding, c.Value)),
                EventTrigger => false,
                _ => false,
            };
            if (!active) continue;
            var setters = trigger switch
            {
                Trigger property => property.Setters,
                MultiTrigger multi => multi.Setters,
                DataTrigger data => data.Setters,
                MultiDataTrigger multiData => multiData.Setters,
                _ => null,
            };
            if (setters is null) continue;
            foreach (var setter in setters) setter.ApplyTo(target, refresh);
        }
    }

    internal static void SubscribeTriggers(FrameworkElement target, Style style)
    {
        Application.DynamicResourceManager.SubscribeStyle(target, style);
        target.PropertyChanged += (_, _, _) => Reapply(target, style);
        target.MouseEnter += (_, _) => Reapply(target, style);
        target.MouseLeave += (_, _) => Reapply(target, style);
        var needsStatic = false;
        var dataContext = target.DataContext as INotifyPropertyChanged;
        foreach (var trigger in style.Triggers)
        {
            foreach (var binding in TriggerBindings(trigger))
            {
                if (binding is null) continue;
                if (binding is Binding single)
                {
                    if (!needsStatic && ExposesStatic(single.Path)) needsStatic = true;
                }
                else if (binding is MultiBinding multi &&
                    !needsStatic && multi.Bindings.OfType<Binding>().Any(b => ExposesStatic(b.Path)))
                {
                    needsStatic = true;
                }
            }
        }
        if (needsStatic)
            SystemParameters.StaticPropertyChanged += (_, _) => Reapply(target, style);
        // The current data context pushes trigger re-evaluation (nested
        // view-model flips re-apply, like WPF's source subscriptions).
        if (dataContext is not null)
            dataContext.PropertyChanged += (_, _) => Reapply(target, style);
    }

    private static void Reapply(FrameworkElement target, Style style)
    {
        try
        {
            style.ApplyTo(target, new StyleApplyContext { SkipEventWires = true });
            if (target is Control control) TemplateApplier.RefreshTemplate(control);
        }
        catch
        {
            // Refresh keeps old values when a key disappears mid-life; load
            // time stays loud at the load site instead.
        }
    }

    private static IEnumerable<BindingBase?> TriggerBindings(TriggerBase trigger) => trigger switch
    {
        DataTrigger data => new[] { data.Binding },
        MultiDataTrigger multiData => multiData.Conditions.Select(c => c.Binding),
        _ => Enumerable.Empty<BindingBase?>(),
    };

    private static bool ExposesStatic(string path)
    {
        foreach (var segment in path.Split('.'))
            if (BindingExpression.IsStaticSegment(segment)) return true;
        return false;
    }

    private static bool IsPropertyConditionActive(FrameworkElement target, Condition condition)
    {
        if (condition.SourceName is not null)
            throw new InvalidOperationException("Condition SourceName is template-only (style triggers have no namescope).");
        if (condition.Property is null) return false;
        return IsPropertyActive(target, condition.Property, condition.Value);
    }

    internal static bool IsPropertyActive(FrameworkElement target, string property, object? expected)
    {
        var current = ReadTriggerProperty(target, property);
        return TriggerValuesEqual(current, ResolveExpected(target, expected));
    }

    internal static bool IsBindingActive(FrameworkElement target, Binding binding, object? expected)
    {
        var source = binding.EffectiveDirectSource(target)
            ?? (binding.ElementName is string name ? target.FindName(name) : null)
            ?? BindingExpression.ResolveRelativeSource(target, binding.RelativeSource)
            ?? target.DataContext;
        if (source is null || !BindingExpression.TryRead(source, binding.Path, out var current))
            return false;
        if (binding.Converter?.Convert(current, typeof(object), binding.ConverterParameter, binding.ConverterCulture)
            is { } converted && !ReferenceEquals(converted, Binding.DoNothing))
            current = converted;
        return TriggerValuesEqual(current, ResolveExpected(target, expected));
    }

    internal static bool IsBindingActive(FrameworkElement target, BindingBase binding, object? expected)
    {
        if (binding is Binding single)
            return IsBindingActive(target, single, expected);
        if (binding is MultiBinding multi)
            return TriggerValuesEqual(EvaluateTriggerMultiBinding(target, multi), ResolveExpected(target, expected));
        if (binding is PriorityBinding priority)
            return TriggerValuesEqual(EvaluateTriggerPriorityBinding(target, priority), ResolveExpected(target, expected));
        return false;
    }

    /// <summary>Evaluates a MultiBinding trigger condition (per-child source
    /// resolution like single bindings, vetoing on unresolvable children
    /// without fallbacks, then the composite converter/format, mirroring
    /// MultiBinding.Evaluate but resolving ElementName parts against the
    /// trigger target, like single-binding conditions).</summary>
    private static object? EvaluateTriggerMultiBinding(FrameworkElement target, MultiBinding multi)
    {
        var values = new object?[multi.Bindings.Count];
        for (var i = 0; i < multi.Bindings.Count; i++)
        {
            if (multi.Bindings[i] is not Binding child) return null;
            if (!TryEvaluateTriggerChild(target, child, out var value)) return null;
            values[i] = value;
        }
        var converter = multi.Converter;
        if (converter is null && multi.DeferredConverterKey is { } key
            && target.TryFindResource(key, out var resource) && resource is IMultiValueConverter found)
            converter = found;
        if (converter is not null)
            return converter.Convert(values, typeof(object), multi.ConverterParameter, multi.ConverterCulture);
        if (multi.StringFormat is { } format)
        {
            if (format.StartsWith("{}", StringComparison.Ordinal)) format = format[2..];
            return string.Format(multi.ConverterCulture, format, values);
        }
        return values.Length == 1 ? values[0] : string.Concat(values.Select(v => v?.ToString()));
    }

    /// <summary>Evaluates a PriorityBinding trigger condition (the first
    /// child yielding a value wins; nothing viable falls back to the
    /// composite FallbackValue, like WPF's UnsetValue-then-fallback).</summary>
    private static object? EvaluateTriggerPriorityBinding(FrameworkElement target, PriorityBinding priority)
    {
        foreach (var candidate in priority.Bindings)
        {
            if (candidate is not Binding child) continue;
            if (TryEvaluateTriggerChild(target, child, out var value)) return value;
        }
        return priority.FallbackValue;
    }

    /// <summary>Evaluates one trigger-condition child binding (source
    /// resolution like single bindings; false when the source is
    /// unresolvable and no FallbackValue applies).</summary>
    internal static bool TryEvaluateTriggerChild(FrameworkElement target, Binding child, out object? value)
    {
        var source = child.EffectiveDirectSource(target)
            ?? (child.ElementName is string name ? target.FindName(name) : null)
            ?? (child.RelativeSource is not null ? BindingExpression.ResolveRelativeSource(target, child.RelativeSource) : null)
            ?? target.DataContext;
        if (source is null || !BindingExpression.TryRead(source, child.Path, out value))
        {
            if (child.FallbackValue is not null)
            {
                value = child.FallbackValue;
                return true;
            }
            value = null;
            return false;
        }
        if (value is null)
        {
            if (child.Converter is not null)
                value = child.Converter.Convert(null, typeof(object), child.ConverterParameter, child.ConverterCulture);
            if (value is null && child.TargetNullValue is not null)
                value = child.TargetNullValue;
        }
        else if (child.Converter is not null)
            value = child.Converter.Convert(value, typeof(object), child.ConverterParameter, child.ConverterCulture);
        return true;
    }

    internal static object? ResolveExpected(FrameworkElement target, object? expected)
    {
        if (expected is TriggerResourceReference reference)
        {
            if (target.TryFindResource(reference.Key, out var resource) && resource is not null)
                return resource;
            if (!reference.IsDynamic)
                throw new InvalidOperationException(
                    $"StaticResource '{reference.Key}' was not found in element, ancestor or application resources.");
            return null;
        }
        return expected;
    }

    /// <summary>Trigger value comparison (converted, null and numeric/boolean text forms).</summary>
    internal static bool TriggerValuesEqual(object? current, object? expected)
    {
        if (Equals(current, expected)) return true;
        if (current is null || expected is null) return false;
        if (expected is string text)
        {
            if (current is bool boolean && bool.TryParse(text, out var parsedBool))
                return boolean == parsedBool;
            if (current is double number && double.TryParse(text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedDouble))
                return number == parsedDouble;
            if (current is int integer && int.TryParse(text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedInt))
                return integer == parsedInt;
            return string.Equals(Convert.ToString(current, System.Globalization.CultureInfo.InvariantCulture), text,
                StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>
    /// Reads a trigger property (mirrors setter write order: plain names go
    /// CLR, then a same-named DP, then the attached store; qualified names go
    /// owner DP, then owner CLR, then the attached store).
    /// </summary>
    internal static object? ReadTriggerProperty(FrameworkElement target, string property)
    {
        var dot = property.IndexOf('.');
        if (dot > 0)
        {
            var owner = XamlLoader.FindEngineType(property[..dot]);
            var dp = owner is not null ? DependencyProperty.FromName(owner, property[(dot + 1)..]) : null;
            if (dp is not null && target is DependencyObject dependency) return dependency.GetValue(dp);
            var qualified = owner?.GetProperty(property[(dot + 1)..],
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (qualified?.CanRead == true) return qualified.GetValue(target);
            // Canonical converted owners (style triggers canonicalize vendor
            // prefixes at parse; BAML carries full names): resolve by full
            // name across loaded assemblies (the owner dictionary is loaded
            // by apply time).
            var last = property.LastIndexOf('.');
            if (last > dot)
            {
                var convertedOwner = WpfAppBoot.FindConvertedTypeByFullName(property[..last]);
                var convertedDp = convertedOwner is not null ? DependencyProperty.FromName(convertedOwner, property[(last + 1)..]) : null;
                if (convertedDp is not null && target is DependencyObject convertedTarget) return convertedTarget.GetValue(convertedDp);
            }
            var attached = target.GetAttached(property);
            if (attached is not null) return attached;
            throw new InvalidOperationException($"Trigger property '{property}' is not a property of <{target.GetType().Name}>.");
        }
        var info = target.GetType().GetProperty(property,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (info?.CanRead == true) return info.GetValue(target);
        var dpDirect = DependencyProperty.FromName(target.GetType(), property);
        if (dpDirect is not null && target is DependencyObject direct) return direct.GetValue(dpDirect);
        var fallback = target.GetAttached(property);
        if (fallback is not null) return fallback;
        throw new InvalidOperationException($"Trigger property '{property}' is not a property of <{target.GetType().Name}>.");
    }
}

/// <summary>Trigger root (matches System.Windows.TriggerBase).</summary>
public abstract class TriggerBase
{
    /// <summary>Enter-actions (BeginStoryboard; template triggers run them on activation).</summary>
    public TriggerActionCollection EnterActions { get; } = new();

    /// <summary>Exit-actions (BeginStoryboard; template triggers run them on deactivation).</summary>
    public TriggerActionCollection ExitActions { get; } = new();
}

/// <summary>
/// Property trigger (matches System.Windows.Trigger): evaluated on apply and
/// re-evaluated when dependency properties, mouse hover, bound sources or
/// resources change. Setter TargetName stays template-only (style setters
/// with TargetName throw, like WPF).
/// </summary>
public sealed class Trigger : TriggerBase
{
    public string Property { get; set; } = string.Empty;
    public object? Value { get; set; }
    /// <summary>Named template part the condition reads (template triggers only, like WPF).</summary>
    public string? SourceName { get; set; }
    public SetterBaseCollection Setters { get; } = new();

    /// <summary>
    /// DependencyProperty-typed condition source (matches the real WPF
    /// Trigger.Property shape; converted code-built templates bind here).
    /// The engine keeps the XAML-friendly string name as the evaluation
    /// key: a plain DP records its name, an attached DP records the
    /// owner-qualified "Owner.Name" form, so the existing name-based
    /// evaluation (CLR, then same-named DP, then attached store) runs
    /// unchanged. Compiled C# callers keep using the string property;
    /// this overload exists so converted IL calling
    /// set_Property(DependencyProperty) binds.
    /// </summary>
    public void set_Property(DependencyProperty? dp) =>
        Property = dp is null ? string.Empty
            : dp.IsAttached ? dp.OwnerType.Name + "." + dp.Name
            : dp.Name;
}

/// <summary>Multi-property trigger (matches System.Windows.MultiTrigger): all conditions must hold.</summary>
public sealed class MultiTrigger : TriggerBase
{
    public List<Condition> Conditions { get; } = new();
    public SetterBaseCollection Setters { get; } = new();
}

/// <summary>
/// Trigger condition (matches System.Windows.Condition): either a plain
/// Property/Value pair (MultiTrigger) or a Binding/Value pair
/// (MultiDataTrigger). One of Property/Binding is set.
/// </summary>
public sealed class Condition
{
    public string? Property { get; set; }
    public BindingBase? Binding { get; set; }
    public object? Value { get; set; }
    /// <summary>Named template part the condition reads (template triggers only, like WPF).</summary>
    public string? SourceName { get; set; }
}

/// <summary>Data trigger (matches System.Windows.DataTrigger): binding-valued condition with setters.</summary>
public sealed class DataTrigger : TriggerBase
{
    public BindingBase? Binding { get; set; }
    public object? Value { get; set; }
    public SetterBaseCollection Setters { get; } = new();
}

/// <summary>Multi data trigger (matches System.Windows.MultiDataTrigger): all binding conditions must hold.</summary>
public sealed class MultiDataTrigger : TriggerBase
{
    public List<Condition> Conditions { get; } = new();
    public SetterBaseCollection Setters { get; } = new();
}

/// <summary>
/// Deferred resource reference inside a trigger value ({StaticResource K}
/// resolves at apply, {DynamicResource K} resolves-or-skips at apply and
/// refreshes with the owning style, like WPF's deferred references).
/// </summary>
internal sealed record TriggerResourceReference(string Key, bool IsDynamic);

/// <summary>Routed-event trigger action root (matches System.Windows.TriggerAction).</summary>
public abstract class TriggerAction
{
}

/// <summary>Trigger action list (matches System.Windows.TriggerActionCollection).</summary>
public sealed class TriggerActionCollection : Collection<TriggerAction>
{
}

/// <summary>
/// Event trigger (matches System.Windows.EventTrigger): "Owner.Event" plus
/// storyboard actions. Triggers attached to a live element subscribe to the
/// matching CLR events and run their storyboards; triggers retained on an
/// uninflated ControlTemplate fire when the template inflates (boundary).
/// </summary>
public sealed class EventTrigger : TriggerBase
{
    /// <summary>Routed event name ("Event" or "Owner.Event").</summary>
    public string RoutedEvent { get; set; } = string.Empty;

    public TriggerActionCollection Actions { get; } = new();
}

/// <summary>Storyboard action (matches System.Windows.Media.Animation.BeginStoryboard).</summary>
public sealed class BeginStoryboard : TriggerAction
{
    public string? Name { get; set; }
    public Storyboard? Storyboard { get; set; }
}

/// <summary>Storyboard removal action (matches
/// System.Windows.Media.Animation.RemoveStoryboard): stops the storyboards
/// begun by the named BeginStoryboard action in the same namescope, like
/// WPF's trigger EnterActions/ExitActions pairing.</summary>
public sealed class RemoveStoryboard : TriggerAction
{
    public string? BeginStoryboardName { get; set; }
}

/// <summary>Template binding marker (matches System.Windows.TemplateBindingExtension).</summary>
public sealed class TemplateBindingExtension
{
    public TemplateBindingExtension()
    {
    }

    public TemplateBindingExtension(string property) => Property = property;

    /// <summary>Templated-parent property the value flows from.</summary>
    public string Property { get; set; } = string.Empty;
}

/// <summary>Control template shell (matches System.Windows.Controls.ControlTemplate):
/// retains the parsed visual root, namescope, deferred bindings and triggers;
/// LoadContent/ApplyTemplate inflate a fresh bound visual tree per control.</summary>
public sealed class ControlTemplate : FrameworkTemplate
{
    public ControlTemplate()
    {
    }

    /// <summary>Targets the template at a control type (matches the converted
    /// show/hide button template construction).</summary>
    public ControlTemplate(Type targetType) => TargetType = targetType;

    public Type? TargetType { get; set; }

    /// <summary>
    /// Retained template visual root (parsed from XAML; each inflation rebuilds
    /// a fresh tree). Hides the factory-typed base VisualTree: the XAML path
    /// keeps the inflated UIElement root here while converted code-built
    /// templates install their FrameworkElementFactory through the base
    /// property (which also inflates this root, so both paths stay usable).
    /// </summary>
    public new UIElement? VisualTree { get; set; }

    /// <summary>
    /// Factory-tree hook: inflates the retained root from a code-built
    /// factory (converted AvalonDock installs its show/hide button template
    /// this way); a null factory clears the factory-inflated root.
    /// </summary>
    protected override void OnFactoryTreeSet(FrameworkElementFactory? factory) =>
        VisualTree = (UIElement?)factory?.CreateInstance();

    /// <summary>Retained template triggers (property and event triggers).</summary>
    public TriggerCollection Triggers { get; } = new();

    /// <summary>Template namescope (x:Name elements in the retained visual tree).</summary>
    public IReadOnlyDictionary<string, object> TemplateNames { get; internal set; } =
        new Dictionary<string, object>(StringComparer.Ordinal);

    /// <summary>Deferred template bindings (element, source property name) resolved at inflation.</summary>
    public IReadOnlyList<(object Element, string Property)> TemplateBindings { get; internal set; } = [];

    /// <summary>Retained template markup for fresh inflation (null on an empty template).</summary>
    internal System.Xml.Linq.XElement? TemplateSource { get; set; }

    /// <summary>Style target the markup was parsed for (rebuild validation).</summary>
    internal Type? TemplateBuildTarget { get; set; }

    /// <summary>
    /// Converted-assembly scope the markup was parsed in (its Themes/Generic.xaml
    /// or app XAML): rebuilds re-enter it so converted template parts
    /// (OxyPlot TrackerControl) resolve at inflation time, like load time.
    /// Null keeps the previous behavior (engine-only markup needs no scope).
    /// </summary>
    internal System.Reflection.Assembly? TemplateScopeAssembly { get; set; }

    /// <summary>Inflates one fresh visual tree from the retained template
    /// (factory-backed templates build from their factory; markup-less
    /// templates throw a precise boundary, never a null dereference).</summary>
    public override object LoadContent()
    {
        if (base.VisualTree is { } factory)
            return factory.CreateInstance();
        if (TemplateSource is null || TemplateBuildTarget is null)
            throw new InvalidOperationException(NoInflationMarkupError());
        return Rebuild().Root;
    }

    /// <summary>Precise boundary for markup-less templates (empty shells and
    /// BAML-retained visual roots alike): a retained root cannot inflate
    /// fresh trees without markup, and sharing one instance across controls
    /// would corrupt parenting, so applying throws loud until BAML-tree
    /// inflation lands.</summary>
    internal string NoInflationMarkupError() => VisualTree is not null
        ? "ControlTemplate has a retained visual tree but no inflation markup (BAML-tree inflation is a precise follow-up)."
        : "ControlTemplate.LoadContent has no template visual tree in v1 (templates stay a precise boundary).";

    /// <summary>Rebuilds a fresh (root, namescope, bindings, triggers) quintuple from the retained markup.</summary>
    internal (UIElement Root, Dictionary<string, object> Names, List<(object Element, string Property)> Legacy, List<TemplateBindingEntry> Entries, TriggerCollection Triggers) Rebuild()
    {
        if (TemplateSource is null || TemplateBuildTarget is null)
            throw new InvalidOperationException(NoInflationMarkupError());
        var scope = TemplateScopeAssembly;
        if (scope is null)
            return XamlLoader.BuildControlTemplate(TemplateSource!, TemplateBuildTarget!, XamlLoader.NoLineInfo, validateDeclaredTarget: false);
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = scope;
        try
        {
            return XamlLoader.BuildControlTemplate(TemplateSource!, TemplateBuildTarget!, XamlLoader.NoLineInfo, validateDeclaredTarget: false);
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    /// <summary>Finds a named part in the tree inflated for a templated parent
    /// (matches ControlTemplate.FindName; null when the parent carries no
    /// tree from this template).</summary>
    public override object? FindName(string name, FrameworkElement templatedParent)
    {
        if (string.IsNullOrEmpty(name) || templatedParent is not Control control) return null;
        if (!ReferenceEquals(control.ActiveTemplate, this) || control.ActiveTemplateNames is null) return null;
        return control.ActiveTemplateNames.TryGetValue(name, out var found) ? found : null;
    }
}

/// <summary>
/// Templatable control base (matches System.Windows.Controls.Control): real-typed
/// brush/font properties converted code binds, with solid-brush changes pushed
/// into the engine's uint render fields through overridable sync hooks so both
/// code-set brushes and XAML attributes render.
/// </summary>
public class Control : FrameworkElement
{
    static Control()
    {
        // Real WPF overrides the Focusable default to true on Control
        // (live-verified against the 9.0.21 runtime); the base keeps false.
        FocusableProperty.OverrideMetadata(typeof(Control), new FrameworkPropertyMetadata(true));
    }

    /// <summary>Tab-stop flag (matches Control.IsTabStopProperty; AddOwner'd from KeyboardNavigation like WPF).</summary>
    public static readonly DependencyProperty IsTabStopProperty =
        KeyboardNavigation.IsTabStopProperty.AddOwner(typeof(Control));

    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register("Background", typeof(Brush), typeof(Control),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register("Foreground", typeof(Brush), typeof(Control),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty BorderBrushProperty =
        DependencyProperty.Register("BorderBrush", typeof(Brush), typeof(Control),
            new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty BorderThicknessProperty =
        DependencyProperty.Register("BorderThickness", typeof(Thickness), typeof(Control),
            new FrameworkPropertyMetadata(new Thickness(0)) { AffectsMeasure = true });
    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register("Padding", typeof(Thickness), typeof(Control),
            new FrameworkPropertyMetadata(new Thickness(0)) { AffectsMeasure = true });
    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register("FontFamily", typeof(FontFamily), typeof(Control),
            new FrameworkPropertyMetadata(new FontFamily("sans-serif")) { AffectsMeasure = true });
    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register("FontSize", typeof(double), typeof(Control),
            new FrameworkPropertyMetadata(14.0) { AffectsMeasure = true });
    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register("FontWeight", typeof(FontWeight), typeof(Control),
            new FrameworkPropertyMetadata(FontWeights.Normal) { AffectsMeasure = true });
    public static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.Register("FontStyle", typeof(FontStyle), typeof(Control),
            new FrameworkPropertyMetadata(FontStyles.Normal) { AffectsMeasure = true });
    public static readonly DependencyProperty FontStretchProperty =
        TextElement.FontStretchProperty.AddOwner(typeof(Control));

    public static readonly RoutedEvent MouseDoubleClickEvent = EventManager.RegisterRoutedEvent(
        "MouseDoubleClick", RoutingStrategy.Bubble, typeof(MouseButtonEventHandler), typeof(Control));

    private Brush? _background;
    private Brush? _foreground;
    private Brush? _borderBrush;
    private Thickness _borderThickness;
    private Thickness _padding = new(0);
    private FontFamily _fontFamily = new("sans-serif");
    private double _fontSize = 14.0;
    private FontWeight _fontWeight = FontWeights.Normal;
    private FontStyle _fontStyle = FontStyles.Normal;
    private FontStretch _fontStretch = FontStretches.Normal;

    /// <summary>Fill brush (null keeps the control default; solid brushes repaint).</summary>
    public Brush? Background
    {
        get => _background;
        set { _background = value; OnBackgroundChanged(value); InvalidateVisual(); TemplateApplier.RefreshTemplate(this); }
    }

    /// <summary>Ink brush (null keeps the control default; solid brushes repaint).</summary>
    public Brush? Foreground
    {
        get => _foreground;
        set { _foreground = value; OnForegroundChanged(value); InvalidateVisual(); TemplateApplier.RefreshTemplate(this); }
    }

    /// <summary>Edge brush (null keeps the control default; solid brushes repaint).</summary>
    public Brush? BorderBrush
    {
        get => _borderBrush;
        set { _borderBrush = value; OnBorderBrushChanged(value); InvalidateVisual(); TemplateApplier.RefreshTemplate(this); }
    }

    /// <summary>Edge width (changes desired size).</summary>
    public Thickness BorderThickness
    {
        get => _borderThickness;
        set { if (_borderThickness == value) return; _borderThickness = value; InvalidateMeasure(); TemplateApplier.RefreshTemplate(this); }
    }

    /// <summary>Inner padding (changes desired size).</summary>
    public Thickness Padding
    {
        get => _padding;
        set { if (_padding == value) return; _padding = value; InvalidateMeasure(); TemplateApplier.RefreshTemplate(this); }
    }

    public FontFamily FontFamily
    {
        get => _fontFamily;
        set { if (value is null || _fontFamily.Equals(value)) return; _fontFamily = value; OnFontChanged(); InvalidateMeasure(); TemplateApplier.RefreshTemplate(this); }
    }

    public double FontSize
    {
        get => _fontSize;
        set { if (_fontSize == value) return; _fontSize = value; OnFontChanged(); InvalidateMeasure(); TemplateApplier.RefreshTemplate(this); }
    }

    public FontWeight FontWeight
    {
        get => _fontWeight;
        set { if (_fontWeight == value) return; _fontWeight = value; OnFontChanged(); InvalidateMeasure(); TemplateApplier.RefreshTemplate(this); }
    }

    public FontStyle FontStyle
    {
        get => _fontStyle;
        set { if (_fontStyle == value) return; _fontStyle = value; OnFontChanged(); InvalidateMeasure(); TemplateApplier.RefreshTemplate(this); }
    }

    public FontStretch FontStretch
    {
        get => _fontStretch;
        set { if (_fontStretch == value) return; _fontStretch = value; OnFontChanged(); InvalidateMeasure(); TemplateApplier.RefreshTemplate(this); }
    }

    /// <summary>Template shell (matches Control.TemplateProperty; setting it
    /// inflates a fresh visual tree, resolves bindings, applies triggers and
    /// calls OnApplyTemplate, like WPF).</summary>
    public static readonly DependencyProperty TemplateProperty =
        DependencyProperty.Register("Template", typeof(ControlTemplate), typeof(Control),
            new FrameworkPropertyMetadata(null, (d, _) => ((Control)d).OnTemplateChanged()));

    /// <summary>Template shell (null is the default; setting inflates and applies).</summary>
    public ControlTemplate? Template
    {
        get => (ControlTemplate?)GetValue(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }

    /// <summary>Inflated template root (null until a template applies).</summary>
    internal UIElement? TemplateRoot { get; set; }

    /// <summary>Visual-state root (matches FrameworkElement.StateGroupsRoot; the applied template root).</summary>
    internal override FrameworkElement? StateGroupsRoot => TemplateRoot as FrameworkElement;

    /// <summary>Template the live tree was inflated from (FindName scope owner).</summary>
    internal ControlTemplate? ActiveTemplate { get; set; }

    /// <summary>Namescope of the live inflated tree (per-control, like WPF).</summary>
    internal IReadOnlyDictionary<string, object>? ActiveTemplateNames { get; set; }

    /// <summary>Deferred values of the live inflated tree (re-resolved on refresh).</summary>
    internal List<TemplateBindingEntry>? ActiveEntries { get; set; }

    /// <summary>ContentPresenters snapshotting the templated content.</summary>
    internal List<ContentPresenter>? ActivePresenters { get; set; }

    /// <summary>Parent change subscription for template refresh (unhooked on detach).</summary>
    internal Action<DependencyProperty, object?, object?>? TemplateRefreshHandler { get; set; }

    /// <summary>Pre-trigger base values of the live tree (restored when triggers go inactive).</summary>
    internal List<TemplateApplier.TriggerDirtied>? TriggerDirtied { get; set; }

    /// <summary>Property triggers whose EnterActions already fired (exit runs on deactivation).</summary>
    internal HashSet<TriggerBase>? TriggerActionsFired { get; set; }

    /// <summary>Re-entrancy guard for template refresh (trigger writes announce).</summary>
    internal bool TemplateRefreshing { get; set; }

    /// <summary>
    /// Re-entrancy guard for converted default-style resolution (the style's
    /// Template setter re-enters the applier synchronously).
    /// </summary>
    internal bool TemplateDefaulting { get; set; }

    private void OnTemplateChanged() => TemplateApplier.Apply(this);

    /// <summary>Inflates (or refreshes) the template visuals (matches
    /// FrameworkElement.ApplyTemplate; true when a tree is present).</summary>
    public override bool ApplyTemplate()
    {
        if (TemplateRoot is null && Template is not null)
            TemplateApplier.Apply(this);
        else
            TemplateApplier.RefreshTemplate(this);
        return TemplateRoot is not null;
    }

    /// <summary>Runs the override after the live tree, names and bindings exist.</summary>
    internal void RaiseOnApplyTemplate() => OnApplyTemplate();

    /// <summary>Finds a named part of the applied template (matches
    /// FrameworkElement.GetTemplateChild).</summary>
    internal DependencyObject? GetTemplateChildCore(string childName) =>
        ActiveTemplateNames is not null && ActiveTemplateNames.TryGetValue(childName, out var found)
            ? found as DependencyObject
            : null;

    /// <summary>Base host paints nothing itself (subclasses render).</summary>
    protected override void OnRender(DrawingContext dc)
    {
        if (TemplateRoot is not null) RenderVisible(dc, TemplateRoot);
    }

    /// <summary>
    /// Templated custom controls (gallery ControlExample/PageHeader):
    /// the inflated template root sizes the control, like Button's
    /// template branch; template-less controls keep zero size.
    /// </summary>
    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (TemplateRoot is not null)
        {
            TemplateRoot.Measure(available);
            return TemplateRoot.DesiredSize;
        }
        return new WpfSize(0, 0);
    }

    /// <summary>Template root fills the arranged slot (matches Button).</summary>
    protected override void ArrangeCore(WpfRect finalRect)
    {
        TemplateRoot?.Arrange(finalRect);
    }

    /// <summary>Tab-stop participation (store-backed through KeyboardNavigation like WPF; traversal order stays a boundary).</summary>
    public bool IsTabStop
    {
        get => KeyboardNavigation.GetIsTabStop(this);
        set => KeyboardNavigation.SetIsTabStop(this, value);
    }
    public int TabIndex { get; set; } = int.MaxValue;

    /// <summary>Horizontal content alignment (matches
    /// Control.HorizontalContentAlignmentProperty; Left like WPF, verified
    /// against the live upstream default).</summary>
    public static readonly DependencyProperty HorizontalContentAlignmentProperty =
        DependencyProperty.Register("HorizontalContentAlignment", typeof(HorizontalAlignment), typeof(Control),
            new FrameworkPropertyMetadata(HorizontalAlignment.Left));

    /// <summary>Vertical content alignment (matches
    /// Control.VerticalContentAlignmentProperty; Top like WPF, verified
    /// against the live upstream default).</summary>
    public static readonly DependencyProperty VerticalContentAlignmentProperty =
        DependencyProperty.Register("VerticalContentAlignment", typeof(VerticalAlignment), typeof(Control),
            new FrameworkPropertyMetadata(VerticalAlignment.Top));

    /// <summary>Horizontal content alignment (Left like WPF).</summary>
    public HorizontalAlignment HorizontalContentAlignment
    {
        get => (HorizontalAlignment)(GetValue(HorizontalContentAlignmentProperty) ?? HorizontalAlignment.Left);
        set => SetValue(HorizontalContentAlignmentProperty, value);
    }

    /// <summary>Vertical content alignment (Top like WPF).</summary>
    public VerticalAlignment VerticalContentAlignment
    {
        get => (VerticalAlignment)(GetValue(VerticalContentAlignmentProperty) ?? VerticalAlignment.Top);
        set => SetValue(VerticalContentAlignmentProperty, value);
    }

    /// <summary>Double-press activation (real double-click detection lands in Button).</summary>
    public event MouseButtonEventHandler? MouseDoubleClick;

    protected virtual void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        e.RoutedEvent ??= MouseDoubleClickEvent;
        MouseDoubleClick?.Invoke(this, e);
    }

    internal void RaiseMouseDoubleClick(MouseButtonEventArgs e)
    {
        e.RoutedEvent ??= MouseDoubleClickEvent;
        OnMouseDoubleClick(e);
        InvokeRoutedHandlers(MouseDoubleClickEvent, e);
    }

    /// <summary>Composes the legacy CSS font string from the typed font properties.</summary>
    protected string ComposeFontCss()
    {
        var slant = FontStyle == FontStyles.Italic ? "italic " : string.Empty;
        var size = FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{slant}{size}px {FontFamily}";
    }
}

/// <summary>UserControl host (matches System.Windows.Controls.UserControl).</summary>
public class UserControl : ContentControl
{
    static UserControl()
    {
        // User-control roots never take keyboard focus (live-verified WPF default).
        FocusableProperty.OverrideMetadata(typeof(UserControl), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Hosts the content visual (logical parenting for bindings/namescope/Loaded).</summary>
    public override object Content
    {
        get => base.Content;
        set
        {
            if (Equals(base.Content, value)) return;
            if (base.Content is UIElement oldVisual) DetachVisualChild(oldVisual);
            base.Content = value;
            if (value is UIElement visual) AttachVisualChild(visual);
        }
    }

    protected override WpfSize MeasureCore(WpfSize available)
    {
        if (Content is UIElement visual)
        {
            visual.Measure(available);
            return visual.DesiredSize;
        }
        return new WpfSize(0, 0);
    }

    protected override void ArrangeCore(WpfRect finalRect)
    {
        if (Content is UIElement visual) visual.Arrange(finalRect);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Content is UIElement visual) RenderVisible(dc, visual);
    }
}

/// <summary>
/// TextBox base (matches System.Windows.Controls.Primitives.TextBoxBase):
/// the TextChanged event, read-only gate and selection contract converted
/// editors bind; editing lives in TextBox.
/// </summary>
public abstract class TextBoxBase : Control
{
    protected TextBoxBase()
    {
        // Per-instance default (never the shared SystemColors brush: Color is
        // settable, so sharing one default would let one box recolor every box).
        SelectionBrush = new SolidColorBrush(SystemColors.HighlightColor);
    }

    public static readonly RoutedEvent TextChangedEvent = EventManager.RegisterRoutedEvent(
        "TextChanged", RoutingStrategy.Bubble, typeof(TextChangedEventHandler), typeof(TextBoxBase));

    /// <summary>Raised after the text changes (typing, code, selection edits).</summary>
    public event TextChangedEventHandler? TextChanged;

    /// <summary>Read-only boxes keep focus and selection but reject edits.</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>Selection fill DP (converted HandyControl editors bind this field).</summary>
    public static readonly DependencyProperty SelectionBrushProperty =
        DependencyProperty.Register("SelectionBrush", typeof(Brush), typeof(TextBoxBase),
            new FrameworkPropertyMetadata(null));

    /// <summary>Caret paint DP (converted HandyControl editors bind this field).</summary>
    public static readonly DependencyProperty CaretBrushProperty =
        DependencyProperty.Register("CaretBrush", typeof(Brush), typeof(TextBoxBase),
            new FrameworkPropertyMetadata(null));

    /// <summary>Selection translucency DP (converted HandyControl editors bind this field).</summary>
    public static readonly DependencyProperty SelectionOpacityProperty =
        DependencyProperty.Register("SelectionOpacity", typeof(double), typeof(TextBoxBase),
            new FrameworkPropertyMetadata(0.4));

    /// <summary>
    /// Selection fill (matches System.Windows.Controls.Primitives.TextBoxBase.SelectionBrush,
    /// default SystemColors.HighlightBrush like real WPF; the PC Space Map
    /// TextBox style sets it from the SelectionBackground resource).
    /// </summary>
    public Brush SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty) ?? new SolidColorBrush(SystemColors.HighlightColor);
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <summary>
    /// Caret paint (matches System.Windows.Controls.Primitives.TextBoxBase.CaretBrush;
    /// null keeps the legacy black caret like real WPF's unset default).
    /// </summary>
    public Brush? CaretBrush
    {
        get => (Brush?)GetValue(CaretBrushProperty);
        set => SetValue(CaretBrushProperty, value);
    }

    /// <summary>
    /// Selection translucency (matches System.Windows.Controls.Primitives.TextBoxBase.SelectionOpacity;
    /// scales the SelectionBrush alpha at paint time; the PC Space Map
    /// TextBox style sets 0.85).
    /// </summary>
    public double SelectionOpacity
    {
        get => (double)(GetValue(SelectionOpacityProperty) ?? 0.4);
        set => SetValue(SelectionOpacityProperty, value);
    }

    /// <summary>
    /// Enter inserts a newline (parsed and retained; multiline editing
    /// stays a boundary, so single-line behavior is unchanged).
    /// </summary>
    public bool AcceptsReturn { get; set; }

    /// <summary>Vertical scrollbar policy (parsed and retained; TextBox stays single-line).</summary>
    public ScrollBarVisibility VerticalScrollBarVisibility { get; set; } = ScrollBarVisibility.Hidden;

    /// <summary>Horizontal scrollbar policy (parsed and retained; TextBox stays single-line).</summary>
    public ScrollBarVisibility HorizontalScrollBarVisibility { get; set; } = ScrollBarVisibility.Hidden;

    protected virtual void OnTextChanged(TextChangedEventArgs e)
    {
        e.RoutedEvent ??= TextChangedEvent;
        e.Source ??= this;
        e.OriginalSource ??= e.Source;
        TextChanged?.Invoke(this, e);
    }

    /// <summary>Selects the whole text (TextBox honors the range).</summary>
    public virtual void SelectAll()
    {
    }

    /// <summary>Selects a text range (TextBox honors the range).</summary>
    public virtual void Select(int start, int length)
    {
    }
}

