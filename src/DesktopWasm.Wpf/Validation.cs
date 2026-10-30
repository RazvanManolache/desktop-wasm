using System.Collections.ObjectModel;

namespace DesktopWasm.Wpf;

// Validation (matches System.Windows.Controls.ValidationRule/ValidationResult
// plus the System.Windows.Controls.Validation error-state surface): the
// gallery TextBoxPage binds <Binding.ValidationRules> with an alphabetic
// rule, so rules must really run on source push, block bad updates, track
// per-element error state and notify subscribers.

/// <summary>When a validation rule runs (matches System.Windows.Controls.ValidationStep).</summary>
public enum ValidationStep
{
    RawProposedValue = 0,
    ConvertedProposedValue = 1,
    UpdatedValue = 2,
    CommittedValue = 3,
}

/// <summary>Rule outcome (matches System.Windows.Controls.ValidationResult).</summary>
public sealed class ValidationResult
{
    public ValidationResult(bool isValid, object? errorContent)
    {
        IsValid = isValid;
        ErrorContent = errorContent;
    }

    /// <summary>Whether the value passed.</summary>
    public bool IsValid { get; }

    /// <summary>Message or payload describing the failure (null when valid).</summary>
    public object? ErrorContent { get; }

    /// <summary>Shared valid outcome (matches ValidationResult.ValidResult).</summary>
    /// <remarks>Property (not field): converted assemblies compiled against
    /// the engine call the get_ValidResult getter, so the shape must stay a
    /// property for staged binaries to keep running.</remarks>
    public static ValidationResult ValidResult { get; } = new(true, null);
}

/// <summary>
/// Value rule (matches System.Windows.Controls.ValidationRule, abstract like
/// the original). Converted gallery rules (AlphabeticValidationRule) derive
/// from this; the converter rebinds the base. Plain class, not a
/// DependencyObject: engine rules carry no bindable properties.
/// </summary>
public abstract class ValidationRule
{
    /// <summary>Which transfer stage this rule checks (default RawProposedValue, like WPF).</summary>
    public ValidationStep ValidationStep { get; set; } = ValidationStep.RawProposedValue;

    /// <summary>Run once when the target first updates from the source (default false, like WPF).</summary>
    public bool ValidatesOnTargetUpdated { get; set; }

    /// <summary>Checks the proposed value in the binding's converter culture.</summary>
    public abstract ValidationResult Validate(object? value, System.Globalization.CultureInfo cultureInfo);
}

/// <summary>
/// Exception marker rule (matches System.Windows.Controls.ExceptionValidationRule,
/// sealed like the original). Slice-69 MaterialDesignDemo entry prerequisite.
/// WPF's Validate always returns ValidResult (verified live: plain values and
/// exception instances alike); the rule's meaning lives in the transfer
/// path, which turns a thrown transfer into a validation failure when the
/// rule is present. The engine binding pipeline already records failures
/// for rules whose Validate throws (see DataBinding.RunRules); converter
/// transfer exceptions keep the old value through the existing DoNothing
/// path, so this rule ports the marker contract, not a new engine catch.
/// </summary>
public sealed class ExceptionValidationRule : ValidationRule
{
    /// <summary>Always valid (matches the original: the engine, not the rule, reports transfer exceptions).</summary>
    public override ValidationResult Validate(object? value, System.Globalization.CultureInfo cultureInfo) =>
        ValidationResult.ValidResult;
}

/// <summary>One active failure (matches System.Windows.Controls.ValidationError).</summary>
public sealed class ValidationError
{
    public ValidationError(ValidationRule ruleInError, object bindingInError, object? errorContent, Exception? exception)
    {
        RuleInError = ruleInError;
        BindingInError = bindingInError;
        ErrorContent = errorContent;
        Exception = exception;
    }

    /// <summary>Rule that reported the failure.</summary>
    public ValidationRule RuleInError { get; }

    /// <summary>Binding transfer that failed (the engine BindingExpression).</summary>
    public object BindingInError { get; }

    /// <summary>Message or payload from the rule.</summary>
    public object? ErrorContent { get; }

    /// <summary>Exception the rule threw (null for rule-reported failures).</summary>
    public Exception? Exception { get; }
}

/// <summary>Error-list delta (matches System.Windows.Controls.ValidationErrorEventAction).</summary>
public enum ValidationErrorEventAction
{
    Added = 0,
    Removed = 1,
}

/// <summary>Error notification (matches System.Windows.Controls.ValidationErrorEventArgs).</summary>
public sealed class ValidationErrorEventArgs : RoutedEventArgs
{
    public ValidationErrorEventArgs(ValidationError error, ValidationErrorEventAction action)
    {
        Error = error;
        Action = action;
        RoutedEvent = Validation.ErrorEvent;
    }

    /// <summary>Error added or removed.</summary>
    public ValidationError Error { get; }

    /// <summary>Which way the list moved.</summary>
    public ValidationErrorEventAction Action { get; }
}

/// <summary>
/// Attached error state (matches System.Windows.Controls.Validation): error
/// lists live here per element; only bindings that opt into
/// <see cref="Binding.NotifyOnValidationError"/> raise <see cref="ErrorEvent"/>
/// (like WPF). State always updates; events are the opt-in part.
/// </summary>
public static class Validation
{
    /// <summary>Error token (matches Validation.ErrorEvent; bubbles like WPF).</summary>
    public static readonly RoutedEvent ErrorEvent = EventManager.RegisterRoutedEvent(
        "Error", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Validation));

    private static readonly DependencyPropertyKey HasErrorPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly("HasError", typeof(bool), typeof(Validation),
            new FrameworkPropertyMetadata(false));

    /// <summary>Error-presence token (matches Validation.HasErrorProperty; read-only attached like real WPF).</summary>
    public static readonly DependencyProperty HasErrorProperty = HasErrorPropertyKey.DependencyProperty;

    /// <summary>Error-template token (matches Validation.ErrorTemplateProperty;
    /// read-write attached ControlTemplate, null by default like WPF).</summary>
    public static readonly DependencyProperty ErrorTemplateProperty =
        DependencyProperty.RegisterAttached("ErrorTemplate", typeof(ControlTemplate), typeof(Validation),
            new FrameworkPropertyMetadata(null));

    /// <summary>Error-template lookup (matches Validation.GetErrorTemplate).</summary>
    public static ControlTemplate? GetErrorTemplate(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (ControlTemplate?)element.GetValue(ErrorTemplateProperty);
    }

    /// <summary>Error-template assignment (matches Validation.SetErrorTemplate).</summary>
    public static void SetErrorTemplate(DependencyObject element, ControlTemplate? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ErrorTemplateProperty, value);
    }

    private sealed class Entry
    {
        public readonly ObservableCollection<ValidationError> Live = new();
        public readonly Dictionary<Binding, List<ValidationError>> ByBinding = new();
    }

    private static readonly Dictionary<DependencyObject, Entry> Errors = new();

    /// <summary>Subscribes to error add/remove on an element (matches Validation.AddErrorHandler).</summary>
    public static void AddErrorHandler(DependencyObject element, RoutedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is not UIElement target)
            throw new InvalidOperationException(
                $"Validation.AddErrorHandler needs a UIElement, not '{element.GetType().Name}'.");
        target.AddHandler(ErrorEvent, handler);
    }

    /// <summary>Unsubscribes (matches Validation.RemoveErrorHandler).</summary>
    public static void RemoveErrorHandler(DependencyObject element, RoutedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(handler);
        if (element is not UIElement target)
            throw new InvalidOperationException(
                $"Validation.RemoveErrorHandler needs a UIElement, not '{element.GetType().Name}'.");
        target.RemoveHandler(ErrorEvent, handler);
    }

    /// <summary>Whether the element currently holds any error (matches Validation.GetHasError).</summary>
    public static bool GetHasError(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        lock (Errors)
            return Errors.TryGetValue(element, out var entry) && entry.Live.Count > 0;
    }

    /// <summary>Live error list (matches Validation.GetErrors; empty, never null).</summary>
    public static ReadOnlyObservableCollection<ValidationError> GetErrors(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        lock (Errors)
        {
            if (!Errors.TryGetValue(element, out var entry))
            {
                entry = new Entry();
                Errors[element] = entry;
            }
            return new ReadOnlyObservableCollection<ValidationError>(entry.Live);
        }
    }

    /// <summary>
    /// Replaces one binding's errors on an element: stale owned errors leave
    /// (Removed), fresh ones join (Added). Only raises when <paramref name="notify"/>
    /// (binding opted into NotifyOnValidationError); state always updates and
    /// repaints.
    /// </summary>
    internal static void SetBindingErrors(DependencyObject element, Binding binding, IReadOnlyList<ValidationError> fresh, bool notify)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(fresh);
        List<ValidationError> removed;
        List<ValidationError> added;
        lock (Errors)
        {
            if (!Errors.TryGetValue(element, out var entry))
            {
                entry = new Entry();
                Errors[element] = entry;
            }
            entry.ByBinding.TryGetValue(binding, out var owned);
            owned ??= new List<ValidationError>();
            removed = owned.Where(e => !fresh.Contains(e)).ToList();
            added = fresh.Where(e => !owned.Contains(e)).ToList();
            foreach (var error in removed)
            {
                entry.Live.Remove(error);
            }
            foreach (var error in added)
            {
                entry.Live.Add(error);
            }
            entry.ByBinding[binding] = fresh.ToList();
        }
        var hasError = false;
        lock (Errors)
        {
            hasError = Errors.TryGetValue(element, out var current) && current.Live.Count > 0;
        }
        if (!Equals(element.GetValue(HasErrorProperty), hasError))
            element.SetValue(HasErrorPropertyKey, hasError);
        if (notify && element is UIElement target)
        {
            foreach (var error in removed)
                target.RaiseEvent(new ValidationErrorEventArgs(error, ValidationErrorEventAction.Removed) { Source = element });
            foreach (var error in added)
                target.RaiseEvent(new ValidationErrorEventArgs(error, ValidationErrorEventAction.Added) { Source = element });
        }
        if ((removed.Count > 0 || added.Count > 0) && element is UIElement visual)
            visual.InvalidateVisual();
    }
}
