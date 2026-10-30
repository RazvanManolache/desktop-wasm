namespace DesktopWasm.Wpf;

// UI Automation notification surface (matches the PresentationCore /
// UIAutomationTypes subset the gallery copy buttons touch:
// ColorTile/ControlExample/IconDataField copy handlers plus
// MainWindow.SettingsButton_Click). Notifications are live UI Automation
// events on Windows; in the browser there is no OS automation tree, so
// RaiseNotificationEvent records the announcement on the peer and raises
// NotificationRaised for the host (the browser lab mirrors it into a
// DOM aria-live region) instead of dropping it. CreatePeerForElement
// returns null for a null element, matching the gallery's defensive
// peer?.RaiseNotificationEvent use.

// Notification kind (matches System.Windows.Automation.AutomationNotificationKind).
public enum AutomationNotificationKind
{
    ItemAdded = 0,
    ItemRemoved = 1,
    ActionCompleted = 2,
    ActionAborted = 3,
    Other = 4,
}

// Notification priority (matches System.Windows.Automation.AutomationNotificationProcessing).
public enum AutomationNotificationProcessing
{
    ImportantAll = 0,
    ImportantMostRecent = 1,
    All = 2,
    MostRecent = 3,
    CurrentThenMostRecent = 4,
}

// Heading level (matches System.Windows.Automation.AutomationHeadingLevel);
// the gallery PageHeader style sets Level1/Level2 in XAML.
public enum AutomationHeadingLevel
{
    None = 0,
    Level1 = 1,
    Level2 = 2,
    Level3 = 3,
    Level4 = 4,
    Level5 = 5,
    Level6 = 6,
    Level7 = 7,
    Level8 = 8,
    Level9 = 9,
}

/// <summary>Event payload raised by <see cref="AutomationPeer.RaiseAutomationEvent"/>.</summary>
public sealed class AutomationEventEventArgs : EventArgs
{
    public AutomationEventEventArgs(AutomationPeer peer, AutomationEvents eventId)
    {
        Peer = peer;
        EventId = eventId;
    }

    public AutomationPeer Peer { get; }

    public AutomationEvents EventId { get; }
}

/// <summary>Property-change payload raised by <see cref="AutomationPeer.RaisePropertyChangedEvent"/>.</summary>
public sealed class AutomationPropertyChangedEventArgs : EventArgs
{
    public AutomationPropertyChangedEventArgs(
        AutomationPeer peer,
        AutomationProperty property,
        object? oldValue,
        object? newValue)
    {
        Peer = peer;
        Property = property;
        OldValue = oldValue;
        NewValue = newValue;
    }

    public AutomationPeer Peer { get; }

    public AutomationProperty Property { get; }

    public object? OldValue { get; }

    public object? NewValue { get; }
}

/// <summary>Announcement payload raised by <see cref="AutomationPeer.RaiseNotificationEvent"/>.</summary>
public sealed class AutomationNotificationEventArgs : EventArgs
{
    public AutomationNotificationEventArgs(
        AutomationPeer peer,
        AutomationNotificationKind kind,
        AutomationNotificationProcessing processing,
        string? text,
        string? activityId)
    {
        Peer = peer;
        Kind = kind;
        Processing = processing;
        Text = text ?? string.Empty;
        ActivityId = activityId ?? string.Empty;
    }

    public AutomationPeer Peer { get; }

    public AutomationNotificationKind Kind { get; }

    public AutomationNotificationProcessing Processing { get; }

    public string Text { get; }

    public string ActivityId { get; }
}

/// <summary>
/// Automation peer (matches System.Windows.Automation.Peers.AutomationPeer
/// for the members the gallery touches). The peer tree itself has no OS
/// backing in the browser; the observable behavior is the announcement
/// channel: every RaiseNotificationEvent records <see cref="LastRaised"/>
/// and raises <see cref="NotificationRaised"/> for host mediation.
/// </summary>
public abstract class AutomationPeer : DispatcherObject
{
    /// <summary>
    /// Latest announcement (null until the first RaiseNotificationEvent;
    /// a test/host hook, not a screen-reader queue).
    /// </summary>
    public static AutomationNotificationEventArgs? LastRaised { get; private set; }

    /// <summary>
    /// Fires for every announcement (the browser host mirrors the text
    /// into a DOM aria-live region; headless sessions observe LastRaised).
    /// </summary>
    public static event EventHandler<AutomationNotificationEventArgs>? NotificationRaised;

    /// <summary>Clears the recorded raises (test isolation).</summary>
    public static void ClearRaisedForTests()
    {
        LastRaised = null;
        LastAutomationEvent = null;
        LastPropertyChanged = null;
    }

    /// <summary>
    /// Latest automation event (null until the first RaiseAutomationEvent;
    /// a test/host hook, not an OS event queue).
    /// </summary>
    public static AutomationEventEventArgs? LastAutomationEvent { get; private set; }

    /// <summary>Fires for every automation event (no OS tree in the browser).</summary>
    public static event EventHandler<AutomationEventEventArgs>? AutomationEventRaised;

    /// <summary>
    /// Latest property change (null until the first RaisePropertyChangedEvent;
    /// a test/host hook, not an OS event queue).
    /// </summary>
    public static AutomationPropertyChangedEventArgs? LastPropertyChanged { get; private set; }

    /// <summary>Fires for every property change (no OS tree in the browser).</summary>
    public static event EventHandler<AutomationPropertyChangedEventArgs>? AutomationPropertyChanged;

    /// <summary>Accessible name (null by default, like WPF).</summary>
    public virtual string? GetName() => null;

    /// <summary>
    /// Returns the pattern provider for an interface (null here: no OS
    /// provider tree in the browser; converted item peers override it and
    /// the override slot is what their GetPattern calls bind).
    /// </summary>
    public virtual object? GetPattern(PatternInterface patternInterface) => null;

    /// <summary>
    /// Control kind for this peer (Custom by default; converted list peers
    /// override it with List/TreeItem and friends).
    /// </summary>
    protected virtual AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;

    /// <summary>
    /// Automation children (null when there are none, like WPF; converted
    /// tree peers override it to enumerate realized item peers).
    /// </summary>
    protected virtual List<AutomationPeer>? GetChildrenCore() => null;

    /// <summary>
    /// Raises an announcement (matches AutomationPeer.RaiseNotificationEvent).
    /// Never dropped: recorded and forwarded to <see cref="NotificationRaised"/>.
    /// </summary>
    public void RaiseNotificationEvent(
        AutomationNotificationKind kind,
        AutomationNotificationProcessing processing,
        string? notificationText,
        string? activityId)
    {
        var raised = new AutomationNotificationEventArgs(this, kind, processing, notificationText, activityId);
        LastRaised = raised;
        NotificationRaised?.Invoke(this, raised);
    }

    /// <summary>
    /// Raises an automation event (matches AutomationPeer.RaiseAutomationEvent,
    /// the converted AvalonEdit selection path). Never dropped: recorded and
    /// forwarded to <see cref="AutomationEventRaised"/> for host mediation.
    /// </summary>
    public void RaiseAutomationEvent(AutomationEvents eventId)
    {
        var raised = new AutomationEventEventArgs(this, eventId);
        LastAutomationEvent = raised;
        AutomationEventRaised?.Invoke(this, raised);
    }

    /// <summary>
    /// Raises a property change (matches
    /// AutomationPeer.RaisePropertyChangedEvent, the converted AvalonEdit
    /// read-only path through ValuePatternIdentifiers.IsReadOnlyProperty).
    /// Never dropped: recorded and forwarded to
    /// <see cref="AutomationPropertyChanged"/>.
    /// </summary>
    public void RaisePropertyChangedEvent(AutomationProperty property, object? oldValue, object? newValue)
    {
        ArgumentNullException.ThrowIfNull(property);
        var raised = new AutomationPropertyChangedEventArgs(this, property, oldValue, newValue);
        LastPropertyChanged = raised;
        AutomationPropertyChanged?.Invoke(this, raised);
    }

    /// <summary>
    /// Returns the raw provider for a peer (matches the protected-internal
    /// AutomationPeer.ProviderFromPeer the converted get_Provider calls as an
    /// inherited instance member). Null here: no OS provider tree backs peers
    /// in the browser; GetPattern stays the observable contract.
    /// </summary>
    protected internal IRawElementProviderSimple? ProviderFromPeer(AutomationPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        return null;
    }

    /// <summary>
    /// Returns the peer for a provider (matches the protected
    /// AutomationPeer.PeerFromProvider). Null here: no peer table exists in
    /// v1, so nothing resolves back.
    /// </summary>
    protected AutomationPeer? PeerFromProvider(IRawElementProviderSimple provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return null;
    }
}

/// <summary>
/// Element peer (matches System.Windows.Automation.Peers.UIElementAutomationPeer
/// for the gallery's CreatePeerForElement + Owner use). GetName reads the
/// element's AutomationProperties.Name, so XAML-set names surface.
/// </summary>
public class UIElementAutomationPeer : AutomationPeer
{
    public UIElementAutomationPeer(UIElement owner)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public UIElement Owner { get; }

    /// <summary>
    /// Returns a peer for the element (null in, null out, matching the
    /// gallery's defensive peer?.RaiseNotificationEvent call shape).
    /// Converted elements that override OnCreateAutomationPeer (like the
    /// AvalonEdit text peers) mint their own peer; everything else keeps
    /// the legacy direct peer.
    /// </summary>
    public static AutomationPeer? CreatePeerForElement(UIElement? element) =>
        element is null ? null : (element.CreateAutomationPeer() ?? new UIElementAutomationPeer(element));

    /// <summary>
    /// Returns the peer for an element (matches
    /// UIElementAutomationPeer.FromElement, the converted AvalonEdit
    /// GetPattern path). Null in, null out; no peer table exists in v1, so
    /// every call mints through <see cref="CreatePeerForElement"/>.
    /// </summary>
    public static AutomationPeer? FromElement(UIElement? element) =>
        element is null ? null : CreatePeerForElement(element);

    /// <inheritdoc/>
    /// Returns null when no accessible name is set (WPF default).
    public override string? GetName()
    {
        var name = AutomationProperties.GetName(Owner);
        return string.IsNullOrEmpty(name) ? null : name;
    }
}

/// <summary>
/// Automation attached properties (matches System.Windows.Automation.AutomationProperties
/// for the Name/HelpText/HeadingLevel members the gallery XAML sets).
/// Values live in the element's attached store, so code Set/Get and XAML
/// attributes/property elements share one backing.
/// </summary>
public static class AutomationProperties
{
    /// <summary>
    /// Accessible-name contract (matches AutomationProperties.NameProperty):
    /// attached, default empty like WPF. The DP store plus attached bag pair
    /// like the Canvas offsets, so trigger {Binding} transfers and XAML/code
    /// sets share one readable value.
    /// </summary>
    public static readonly DependencyProperty NameProperty =
        DependencyProperty.RegisterAttached("Name", typeof(string), typeof(AutomationProperties),
            new FrameworkPropertyMetadata(string.Empty));

    /// <summary>Help-text contract (matches AutomationProperties.HelpTextProperty).</summary>
    public static readonly DependencyProperty HelpTextProperty =
        DependencyProperty.RegisterAttached("HelpText", typeof(string), typeof(AutomationProperties),
            new FrameworkPropertyMetadata(string.Empty));

    /// <summary>Automation-id contract (matches AutomationProperties.AutomationIdProperty).</summary>
    public static readonly DependencyProperty AutomationIdProperty =
        DependencyProperty.RegisterAttached("AutomationId", typeof(string), typeof(AutomationProperties),
            new FrameworkPropertyMetadata(string.Empty));

    /// <summary>Heading-level contract (matches AutomationProperties.HeadingLevelProperty).</summary>
    public static readonly DependencyProperty HeadingLevelProperty =
        DependencyProperty.RegisterAttached("HeadingLevel", typeof(AutomationHeadingLevel), typeof(AutomationProperties),
            new FrameworkPropertyMetadata(AutomationHeadingLevel.None));

    /// <summary>Reads the accessible name (empty when unset).</summary>
    public static string GetName(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement visual && visual.GetAttached("AutomationProperties.Name") is string fromBag) return fromBag;
        if (element.HasLocalValue(NameProperty) &&
            element.GetValue(NameProperty) is string fromStore) return fromStore;
        return string.Empty;
    }

    /// <summary>Sets the accessible name (DP store plus attached bag).</summary>
    public static void SetName(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is not UIElement target)
            throw new NotSupportedException("AutomationProperties.Name needs a UIElement host in v1.");
        target.SetAttached("AutomationProperties.Name", value ?? string.Empty);
        element.SetValue(NameProperty, value ?? string.Empty);
    }

    /// <summary>Reads the help text (empty when unset).</summary>
    public static string GetHelpText(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement visual && visual.GetAttached("AutomationProperties.HelpText") is string fromBag) return fromBag;
        if (element.HasLocalValue(HelpTextProperty) &&
            element.GetValue(HelpTextProperty) is string fromStore) return fromStore;
        return string.Empty;
    }

    /// <summary>Sets the help text (DP store plus attached bag).</summary>
    public static void SetHelpText(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is not UIElement target)
            throw new NotSupportedException("AutomationProperties.HelpText needs a UIElement host in v1.");
        target.SetAttached("AutomationProperties.HelpText", value ?? string.Empty);
        element.SetValue(HelpTextProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Reads the automation id (empty when unset; matches
    /// AutomationProperties.GetAutomationId, which converted docking managers
    /// stamp onto their toggle buttons).
    /// </summary>
    public static string GetAutomationId(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement visual && visual.GetAttached("AutomationProperties.AutomationId") is string fromBag) return fromBag;
        if (element.HasLocalValue(AutomationIdProperty) &&
            element.GetValue(AutomationIdProperty) is string fromStore) return fromStore;
        return string.Empty;
    }

    /// <summary>Sets the automation id (DP store plus attached bag).</summary>
    public static void SetAutomationId(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is not UIElement target)
            throw new NotSupportedException("AutomationProperties.AutomationId needs a UIElement host in v1.");
        target.SetAttached("AutomationProperties.AutomationId", value ?? string.Empty);
        element.SetValue(AutomationIdProperty, value ?? string.Empty);
    }

    /// <summary>Reads the heading level (None when unset).</summary>
    public static AutomationHeadingLevel GetHeadingLevel(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is UIElement visual && visual.GetAttached("AutomationProperties.HeadingLevel") is AutomationHeadingLevel fromBag) return fromBag;
        if (element.HasLocalValue(HeadingLevelProperty) &&
            element.GetValue(HeadingLevelProperty) is AutomationHeadingLevel fromStore) return fromStore;
        return AutomationHeadingLevel.None;
    }

    /// <summary>Sets the heading level (DP store plus attached bag).</summary>
    public static void SetHeadingLevel(DependencyObject element, AutomationHeadingLevel value)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is not UIElement target)
            throw new NotSupportedException("AutomationProperties.HeadingLevel needs a UIElement host in v1.");
        target.SetAttached("AutomationProperties.HeadingLevel", value);
        element.SetValue(HeadingLevelProperty, value);
    }
}
