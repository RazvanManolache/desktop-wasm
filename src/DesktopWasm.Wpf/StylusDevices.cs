namespace DesktopWasm.Wpf;

// Slice-92 AvalonEdit device pair: InputDevice + StylusDevice (the
// convertible device scope in the handycontrol package: 2 unmapped-type gaps
// on ICSharpCode.AvalonEdit). Member evidence via Mono.Cecil on the upstream
// net10.0-windows ICSharpCode.AvalonEdit.dll, attributed to the exact
// converted call sites: TextView.RaiseHoverEventPair reads
// MouseEventArgs.get_StylusDevice and news up MouseEventArgs through the
// (MouseDevice, int, StylusDevice) overload (stylus local included), while
// CompletionWindow.textArea_PreviewTextInput and TextArea.PerformTextInput
// call InputEventArgs.get_Device and pass the device into
// TextCompositionEventArgs..ctor (bound since slice-93 through the
// TextCompositionEventHandler/InputManager scope). Live shapes verified
// against the 10.0.13 PresentationCore ref pack: InputDevice is abstract over
// DispatcherObject with a protected ctor plus abstract get-only Target
// (IInputElement) and ActiveSource (PresentationSource); StylusDevice is
// sealed over InputDevice with an internal ctor plus non-abstract Target and
// ActiveSource overrides; MouseDevice and KeyboardDevice are sealed over
// InputDevice the same way. The pair rides the default System.Windows.*
// rule once the engine declares it, so no Overrides entry is needed.
// Boundary, never fabricated: the engine drives no pen hardware, so no
// StylusDevice instance is ever produced (the ctor stays internal with no
// factory) and Target/ActiveSource report null like an unloaded device; the
// TabletDevice/StylusButtonCollection/Capture/GetStylusPoints hardware
// surface stays a follow-up.

/// <summary>
/// Input device root (matches System.Windows.Input.InputDevice): the live
/// abstract contract over DispatcherObject. Engine devices route their real
/// state through it (mouse target, keyboard focus); composition sources
/// stay null because the engine composes no Win32 target.
/// </summary>
public abstract class InputDevice : DispatcherObject
{
    /// <summary>Live protected shape (verified against the ref pack).</summary>
    protected InputDevice() { }

    /// <summary>Device target (matches Target).</summary>
    public abstract IInputElement? Target { get; }

    /// <summary>Composition source (matches ActiveSource).</summary>
    public abstract PresentationSource? ActiveSource { get; }
}

/// <summary>
/// Pen flicks gate (matches System.Windows.Input.Stylus: attached, true
/// default like WPF, verified on net10.0-windows; retained-only, the
/// engine drives no pen hardware or flicks animation).
/// </summary>
public static class Stylus
{
    /// <summary>Flicks gate (matches Stylus.IsFlicksEnabledProperty).</summary>
    public static readonly DependencyProperty IsFlicksEnabledProperty =
        DependencyProperty.RegisterAttached("IsFlicksEnabled", typeof(bool), typeof(Stylus),
            new PropertyMetadata(true));

    /// <summary>Reads the flicks gate (matches Stylus.GetIsFlicksEnabled; true default).</summary>
    public static bool GetIsFlicksEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement ui && ui.GetAttached("Stylus.IsFlicksEnabled") is bool fromBag) return fromBag;
        if (element is UIElement uiBag && uiBag.GetAttached("Stylus.IsFlicksEnabled") is string bagText &&
            bool.TryParse(bagText, out var bagParsed)) return bagParsed;
        if (element.HasLocalValue(IsFlicksEnabledProperty) && element.GetValue(IsFlicksEnabledProperty) is bool local) return local;
        return true;
    }

    /// <summary>Writes the flicks gate (matches Stylus.SetIsFlicksEnabled; retained-only).</summary>
    public static void SetIsFlicksEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsFlicksEnabledProperty, value);
        if (element is UIElement ui) ui.SetAttached("Stylus.IsFlicksEnabled", value);
    }

    /// <summary>Press-and-hold gate (matches Stylus.IsPressAndHoldEnabledProperty; true default like WPF).</summary>
    public static readonly DependencyProperty IsPressAndHoldEnabledProperty =
        DependencyProperty.RegisterAttached("IsPressAndHoldEnabled", typeof(bool), typeof(Stylus),
            new PropertyMetadata(true));

    /// <summary>Reads the press-and-hold gate (matches Stylus.GetIsPressAndHoldEnabled; true default).</summary>
    public static bool GetIsPressAndHoldEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement ui && ui.GetAttached("Stylus.IsPressAndHoldEnabled") is bool fromBag) return fromBag;
        if (element is UIElement uiBag && uiBag.GetAttached("Stylus.IsPressAndHoldEnabled") is string bagText &&
            bool.TryParse(bagText, out var bagParsed)) return bagParsed;
        if (element.HasLocalValue(IsPressAndHoldEnabledProperty) && element.GetValue(IsPressAndHoldEnabledProperty) is bool local) return local;
        return true;
    }

    /// <summary>Writes the press-and-hold gate (matches Stylus.SetIsPressAndHoldEnabled; retained-only).</summary>
    public static void SetIsPressAndHoldEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsPressAndHoldEnabledProperty, value);
        if (element is UIElement ui) ui.SetAttached("Stylus.IsPressAndHoldEnabled", value);
    }
}

/// <summary>
/// Pen device (matches System.Windows.Input.StylusDevice, sealed like the
/// live type): the engine drives no pen hardware, so this carries the live
/// inheritance and override shape with null device state. Converted hover
/// code binds its MouseEventArgs plumbing against this contract.
/// </summary>
public sealed class StylusDevice : InputDevice
{
    internal StylusDevice() { }

    /// <summary>
    /// Always null: no stylus hardware is loaded (matches the Target
    /// override shape; a loaded live device would report its element).
    /// </summary>
    public override IInputElement? Target => null;

    /// <summary>
    /// Always null: the engine composes no Win32/target source (matches the
    /// ActiveSource override shape).
    /// </summary>
    public override PresentationSource? ActiveSource => null;
}
