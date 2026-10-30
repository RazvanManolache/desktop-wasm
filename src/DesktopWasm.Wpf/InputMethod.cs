namespace DesktopWasm.Wpf;

// Slice-90 AvalonEdit IME suspension: InputMethod attached contract
// (the convertible IME row in the handycontrol package: 1 unmapped-type gap
// on ICSharpCode.AvalonEdit). Member evidence via Mono.Cecil on the upstream
// net10.0-windows ICSharpCode.AvalonEdit.dll, attributed to the exact
// converted call sites: Editing.ImeSupport..ctor and .TextAreaOptionChanged
// both call InputMethod.SetIsInputMethodSuspended(textArea, bool) driven by
// TextEditorOptions.EnableImeSupport (suspend WPF IME while AvalonEdit's own
// Win32 IMM ImeSupport owns the context; resume otherwise). Live shapes
// verified against the 10.0.13 PresentationCore runtime pack (non-sealed class
// over DispatcherObject with static IsInputMethodEnabled/IsInputMethodSuspended
// attached DPs plus get/set pairs) plus a live net10.0-windows runtime probe
// (Enabled defaults true, Suspended defaults false, set round-trips, DP names
// IsInputMethodEnabled/IsInputMethodSuspended). The contract rides the default
// System.Windows.* rule once the engine declares it, so no Overrides entry is
// needed.
// Real behavior, never fabricated: the attached flags are retained per
// element through the real DependencyProperty store, so converted ImeSupport
// reads back exactly what it suspended. Boundary: there is no OS IME context
// behind these flags (no text-services loop, no composition window); Current,
// PreferredIme*/ImeState/SpeechMode, StateChanged, and InputScope stay
// follow-ups requiring OS text services, and the engine keeps no Current
// instance rather than shipping a dead one.

/// <summary>
/// IME suspension flags (matches System.Windows.Input.InputMethod,
/// PresentationCore): converted AvalonEdit ImeSupport suspends WPF IME on
/// its TextArea while its own Win32 IMM handling owns the context.
/// </summary>
public class InputMethod : DispatcherObject
{
    private InputMethod()
    {
    }

    /// <summary>Attached IME-enabled flag (matches InputMethod.IsInputMethodEnabledProperty).</summary>
    public static readonly DependencyProperty IsInputMethodEnabledProperty =
        DependencyProperty.RegisterAttached("IsInputMethodEnabled", typeof(bool), typeof(InputMethod),
            new PropertyMetadata(true));

    /// <summary>Attached IME-suspended flag (matches InputMethod.IsInputMethodSuspendedProperty).</summary>
    public static readonly DependencyProperty IsInputMethodSuspendedProperty =
        DependencyProperty.RegisterAttached("IsInputMethodSuspended", typeof(bool), typeof(InputMethod),
            new PropertyMetadata(false));

    /// <summary>Reads the attached IME-enabled flag (matches InputMethod.GetIsInputMethodEnabled).</summary>
    /// <param name="element">Target element.</param>
    /// <returns>True unless explicitly disabled (live default).</returns>
    public static bool GetIsInputMethodEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool?)element.GetValue(IsInputMethodEnabledProperty) ?? true;
    }

    /// <summary>Writes the attached IME-enabled flag (matches InputMethod.SetIsInputMethodEnabled).</summary>
    /// <param name="element">Target element.</param>
    /// <param name="value">Enabled value.</param>
    public static void SetIsInputMethodEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsInputMethodEnabledProperty, value);
    }

    /// <summary>Reads the attached IME-suspended flag (matches InputMethod.GetIsInputMethodSuspended).</summary>
    /// <param name="element">Target element.</param>
    /// <returns>True once suspended (live default false).</returns>
    public static bool GetIsInputMethodSuspended(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool?)element.GetValue(IsInputMethodSuspendedProperty) ?? false;
    }

    /// <summary>
    /// Writes the attached IME-suspended flag (matches
    /// InputMethod.SetIsInputMethodSuspended): the exact converted
    /// ImeSupport call shape.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <param name="value">Suspended value.</param>
    public static void SetIsInputMethodSuspended(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsInputMethodSuspendedProperty, value);
    }
}
