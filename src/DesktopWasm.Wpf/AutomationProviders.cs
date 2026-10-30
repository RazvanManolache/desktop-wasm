namespace DesktopWasm.Wpf;

// UIA text-provider contracts (matches the UIAutomationTypes /
// UIAutomationProvider / PresentationCore subset the converted AvalonEdit
// automation peers bind: TextAreaAutomationPeer derives
// FrameworkElementAutomationPeer and implements ITextProvider +
// IValueProvider with GetPattern/GetSelection/DocumentRange/
// SupportedTextSelection/Provider/RangeFromPoint/RangeFromChild plus
// OnSelectionChanged raising TextPatternOnTextSelectionChanged;
// TextEditorAutomationPeer implements IValueProvider and raises
// IsReadOnlyProperty through RaisePropertyChangedEvent;
// TextRangeProvider implements ITextRangeProvider with TextUnit /
// TextPatternRangeEndpoint / LogicalDirection parameters and
// IRawElementProviderSimple children/enclosing elements).
//
// Member shapes verified with Mono.Cecil against the upstream
// net10.0-windows ICSharpCode.AvalonEdit.dll call sites plus the 10.0.13
// UIAutomationTypes/UIAutomationProvider/PresentationCore ref packs
// (AutomationPeer.GetPattern is abstract there and ProviderFromPeer is
// protected-internal instance: the converted get_Provider calls it as an
// inherited instance member).
//
// There is no OS automation tree in the browser, so provider objects never
// back a live UIA fragment: peer raise methods record into the announcement
// channel (mirroring RaiseNotificationEvent) instead of dropping, and
// ProviderFromPeer/PeerFromProvider return null like GetPattern.

/// <summary>Text unit for range operations (matches TextUnit).</summary>
public enum TextUnit
{
    Character = 0,
    Format = 1,
    Word = 2,
    Line = 3,
    Paragraph = 4,
    Page = 5,
    Document = 6,
}

/// <summary>Range endpoint (matches TextPatternRangeEndpoint).</summary>
public enum TextPatternRangeEndpoint
{
    Start = 0,
    End = 1,
}

/// <summary>Selection model (matches SupportedTextSelection).</summary>
public enum SupportedTextSelection
{
    None = 0,
    Single = 1,
    Multiple = 2,
}

/// <summary>Automation event id (matches AutomationEvents).</summary>
public enum AutomationEvents
{
    ToolTipOpened = 0,
    ToolTipClosed = 1,
    MenuOpened = 2,
    MenuClosed = 3,
    AutomationFocusChanged = 4,
    InvokePatternOnInvoked = 5,
    SelectionItemPatternOnElementAddedToSelection = 6,
    SelectionItemPatternOnElementRemovedFromSelection = 7,
    SelectionItemPatternOnElementSelected = 8,
    SelectionPatternOnInvalidated = 9,
    TextPatternOnTextSelectionChanged = 10,
    TextPatternOnTextChanged = 11,
    AsyncContentLoaded = 12,
    PropertyChanged = 13,
    StructureChanged = 14,
    InputReachedTarget = 15,
    InputReachedOtherElement = 16,
    InputDiscarded = 17,
    LiveRegionChanged = 18,
    Notification = 19,
    ActiveTextPositionChanged = 20,
}

/// <summary>Provider hosting flags (matches ProviderOptions).</summary>
[Flags]
public enum ProviderOptions
{
    ClientSideProvider = 1,
    ServerSideProvider = 2,
    NonClientAreaProvider = 4,
    OverrideProvider = 8,
    ProviderOwnsSetFocus = 16,
    UseComThreading = 32,
}

/// <summary>
/// Automation identifier base (matches AutomationIdentifier: Id plus
/// ProgrammaticName with identity over Id, like WPF).
/// </summary>
public abstract class AutomationIdentifier : IComparable
{
    private static readonly Dictionary<int, AutomationIdentifier> Registry = new();

    protected AutomationIdentifier(int id, string programmaticName)
    {
        Id = id;
        ProgrammaticName = programmaticName ?? throw new ArgumentNullException(nameof(programmaticName));
        lock (Registry) Registry[id] = this;
    }

    /// <summary>Numeric UIA id (stable Win32 contract).</summary>
    public int Id { get; }

    /// <summary>Dotted programmatic name (Owner.Name).</summary>
    public string ProgrammaticName { get; }

    internal static T? FindById<T>(int id) where T : AutomationIdentifier
    {
        lock (Registry) return Registry.TryGetValue(id, out var found) ? found as T : null;
    }

    /// <inheritdoc/>
    public int CompareTo(object? obj) => obj is AutomationIdentifier other ? Id.CompareTo(other.Id) : 1;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is AutomationIdentifier other && other.Id == Id;

    /// <inheritdoc/>
    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>
/// Automation property id (matches AutomationProperty: internal
/// construction, LookupById over the declared identifiers).
/// </summary>
public class AutomationProperty : AutomationIdentifier
{
    internal AutomationProperty(int id, string programmaticName) : base(id, programmaticName)
    {
    }

    /// <summary>Returns the declared property with this id, or null.</summary>
    public static AutomationProperty? LookupById(int id) => FindById<AutomationProperty>(id);
}

/// <summary>
/// Automation pattern id (matches AutomationPattern: internal
/// construction, LookupById over the declared identifiers).
/// </summary>
public class AutomationPattern : AutomationIdentifier
{
    internal AutomationPattern(int id, string programmaticName) : base(id, programmaticName)
    {
    }

    /// <summary>Returns the declared pattern with this id, or null.</summary>
    public static AutomationPattern? LookupById(int id) => FindById<AutomationPattern>(id);
}

/// <summary>
/// Value-pattern identifiers (matches ValuePatternIdentifiers; ids are the
/// stable UIA registry values).
/// </summary>
public static class ValuePatternIdentifiers
{
    /// <summary>Value pattern id (10002).</summary>
    public static readonly AutomationPattern Pattern = new(10002, "ValuePatternIdentifiers.Pattern");

    /// <summary>Value property id (30045).</summary>
    public static readonly AutomationProperty ValueProperty = new(30045, "ValuePatternIdentifiers.ValueProperty");

    /// <summary>IsReadOnly property id (30046).</summary>
    public static readonly AutomationProperty IsReadOnlyProperty = new(30046, "ValuePatternIdentifiers.IsReadOnlyProperty");
}

/// <summary>
/// Value-pattern provider (matches IValueProvider: read-only flag, current
/// value, and the push that converted text editors route into the document).
/// </summary>
public interface IValueProvider
{
    /// <summary>Whether the control rejects SetValue (like WPF).</summary>
    bool IsReadOnly { get; }

    /// <summary>Current value (null when the control holds none).</summary>
    string? Value { get; }

    /// <summary>Pushes a value (converted editors write the document).</summary>
    void SetValue(string value);
}

/// <summary>
/// Text-pattern provider (matches ITextProvider: selection, visible ranges,
/// child/point resolution, document range, and the selection model).
/// </summary>
public interface ITextProvider
{
    /// <summary>Whole-document range (null when the document is unavailable).</summary>
    ITextRangeProvider? DocumentRange { get; }

    /// <summary>Selection model the provider supports.</summary>
    SupportedTextSelection SupportedTextSelection { get; }

    /// <summary>Currently selected ranges.</summary>
    ITextRangeProvider[] GetSelection();

    /// <summary>Ranges currently visible.</summary>
    ITextRangeProvider[] GetVisibleRanges();

    /// <summary>Range for a child element (null when it maps to no text).</summary>
    ITextRangeProvider? RangeFromChild(IRawElementProviderSimple childElement);

    /// <summary>Degenerate range at a screen point (null when outside text).</summary>
    ITextRangeProvider? RangeFromPoint(Point screenLocation);
}

/// <summary>
/// Text-range provider (matches ITextRangeProvider: clone/compare/endpoint
/// motion, attribute/text search, bounds/children/text extraction, and the
/// selection/scroll verbs the converted range provider implements).
/// </summary>
public interface ITextRangeProvider
{
    /// <summary>Duplicates this range.</summary>
    ITextRangeProvider Clone();

    /// <summary>Whether both ends match the other range.</summary>
    bool Compare(ITextRangeProvider range);

    /// <summary>Orders one endpoint against another range's endpoint.</summary>
    int CompareEndpoints(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint);

    /// <summary>Expands to the enclosing unit.</summary>
    void ExpandToEnclosingUnit(TextUnit unit);

    /// <summary>First matching-attribute range (null when none).</summary>
    ITextRangeProvider? FindAttribute(int attribute, object value, bool backward);

    /// <summary>First matching-text range (null when none).</summary>
    ITextRangeProvider? FindText(string text, bool backward, bool ignoreCase);

    /// <summary>Attribute value at the range (null when unset).</summary>
    object? GetAttributeValue(int attribute);

    /// <summary>Screen bounding rectangles of the range.</summary>
    double[] GetBoundingRectangles();

    /// <summary>Embedded child providers.</summary>
    IRawElementProviderSimple[] GetChildren();

    /// <summary>Enclosing element provider (null when none).</summary>
    IRawElementProviderSimple? GetEnclosingElement();

    /// <summary>Plain text of the range (up to maxLength characters).</summary>
    string GetText(int maxLength);

    /// <summary>Moves by whole units; returns the units actually moved.</summary>
    int Move(TextUnit unit, int count);

    /// <summary>Moves one endpoint onto another range's endpoint.</summary>
    void MoveEndpointByRange(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint);

    /// <summary>Moves one endpoint by whole units; returns the units moved.</summary>
    int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count);

    /// <summary>Adds the range to the selection.</summary>
    void AddToSelection();

    /// <summary>Removes the range from the selection.</summary>
    void RemoveFromSelection();

    /// <summary>Scrolls the range into view.</summary>
    void ScrollIntoView(bool alignToTop);

    /// <summary>Selects the range.</summary>
    void Select();
}

/// <summary>
/// Raw element provider (matches IRawElementProviderSimple: hosting flags,
/// pattern/property lookup, and the host provider link).
/// </summary>
public interface IRawElementProviderSimple
{
    /// <summary>Hosting flags.</summary>
    ProviderOptions ProviderOptions { get; }

    /// <summary>Pattern object for an id (null when unsupported).</summary>
    object? GetPatternProvider(int patternId);

    /// <summary>Property value for an id (null when unset).</summary>
    object? GetPropertyValue(int propertyId);

    /// <summary>Host provider (null for root providers).</summary>
    IRawElementProviderSimple? HostRawElementProvider { get; }
}
