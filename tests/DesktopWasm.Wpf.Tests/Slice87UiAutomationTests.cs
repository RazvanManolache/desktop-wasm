using System.Reflection;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-87 UIA text-provider regression tests: the 10 AvalonEdit text rows
/// (ITextProvider/IValueProvider/ITextRangeProvider/IRawElementProviderSimple
/// plus TextUnit/TextPatternRangeEndpoint/SupportedTextSelection/
/// AutomationProperty/ValuePatternIdentifiers/AutomationEvents) ride the
/// default System.Windows.* rule once the engine declares the exact upstream
/// shapes (member shapes verified with Mono.Cecil against the upstream
/// net10.0-windows ICSharpCode.AvalonEdit.dll call sites plus the 10.0.13
/// UIAutomationTypes/UIAutomationProvider/PresentationCore ref packs:
/// GetPattern is abstract there, ProviderFromPeer is protected-internal
/// instance, OnCreateAutomationPeer is protected instance).
/// Converted call sites: TextAreaAutomationPeer (iface impls, GetPattern
/// Text/Value/Scroll switch, get_Provider via ProviderFromPeer,
/// OnSelectionChanged raising TextPatternOnTextSelectionChanged),
/// TextEditorAutomationPeer (explicit IValueProvider impl, GetPattern
/// Value branch via FromElement, RaiseIsReadOnlyChanged through
/// ValuePatternIdentifiers.IsReadOnlyProperty), and TextRangeProvider (the
/// full ITextRangeProvider surface with TextUnit/TextPatternRangeEndpoint/
/// LogicalDirection parameters and IRawElementProviderSimple children).
/// No Application state is touched, so these stay outside the serialized
/// ApplicationState collection.
/// </summary>
[ResetAmbientState]
public sealed class Slice87UiAutomationTests
{
    [Fact]
    public void Text_enum_values_match_wpf()
    {
        Assert.Equal(0, (int)TextUnit.Character);
        Assert.Equal(1, (int)TextUnit.Format);
        Assert.Equal(2, (int)TextUnit.Word);
        Assert.Equal(3, (int)TextUnit.Line);
        Assert.Equal(4, (int)TextUnit.Paragraph);
        Assert.Equal(5, (int)TextUnit.Page);
        Assert.Equal(6, (int)TextUnit.Document);
        Assert.Equal(0, (int)TextPatternRangeEndpoint.Start);
        Assert.Equal(1, (int)TextPatternRangeEndpoint.End);
        Assert.Equal(0, (int)SupportedTextSelection.None);
        Assert.Equal(1, (int)SupportedTextSelection.Single);
        Assert.Equal(2, (int)SupportedTextSelection.Multiple);
    }

    [Fact]
    public void AutomationEvents_values_match_wpf()
    {
        Assert.Equal(0, (int)AutomationEvents.ToolTipOpened);
        Assert.Equal(4, (int)AutomationEvents.AutomationFocusChanged);
        Assert.Equal(10, (int)AutomationEvents.TextPatternOnTextSelectionChanged);
        Assert.Equal(11, (int)AutomationEvents.TextPatternOnTextChanged);
        Assert.Equal(13, (int)AutomationEvents.PropertyChanged);
        Assert.Equal(20, (int)AutomationEvents.ActiveTextPositionChanged);
    }

    [Fact]
    public void ProviderOptions_flags_match_wpf()
    {
        Assert.True(typeof(ProviderOptions).IsDefined(typeof(FlagsAttribute), inherit: false));
        Assert.Equal(1, (int)ProviderOptions.ClientSideProvider);
        Assert.Equal(2, (int)ProviderOptions.ServerSideProvider);
        Assert.Equal(4, (int)ProviderOptions.NonClientAreaProvider);
        Assert.Equal(8, (int)ProviderOptions.OverrideProvider);
        Assert.Equal(16, (int)ProviderOptions.ProviderOwnsSetFocus);
        Assert.Equal(32, (int)ProviderOptions.UseComThreading);
    }

    [Fact]
    public void Identifiers_carry_stable_ids_and_lookup()
    {
        Assert.Equal(30046, ValuePatternIdentifiers.IsReadOnlyProperty.Id);
        Assert.Equal(30045, ValuePatternIdentifiers.ValueProperty.Id);
        Assert.Equal(10002, ValuePatternIdentifiers.Pattern.Id);
        Assert.IsType<AutomationProperty>(ValuePatternIdentifiers.IsReadOnlyProperty);
        Assert.IsType<AutomationProperty>(ValuePatternIdentifiers.ValueProperty);
        Assert.IsType<AutomationPattern>(ValuePatternIdentifiers.Pattern);
        Assert.Same(ValuePatternIdentifiers.IsReadOnlyProperty,
            AutomationProperty.LookupById(30046));
        Assert.Same(ValuePatternIdentifiers.ValueProperty,
            AutomationProperty.LookupById(30045));
        Assert.Same(ValuePatternIdentifiers.Pattern,
            AutomationPattern.LookupById(10002));
        Assert.Null(AutomationProperty.LookupById(999999));
        Assert.Null(AutomationPattern.LookupById(10002 - 1));
        // Identity is over the numeric id, like WPF.
        Assert.Equal(ValuePatternIdentifiers.IsReadOnlyProperty.GetHashCode(), 30046.GetHashCode());
        Assert.True(ValuePatternIdentifiers.IsReadOnlyProperty.Equals(AutomationProperty.LookupById(30046)));
    }

    [Fact]
    public void Identifier_ctors_stay_internal_like_wpf()
    {
        Assert.Null(typeof(AutomationProperty).GetConstructor(Type.EmptyTypes));
        Assert.Null(typeof(AutomationProperty).GetConstructor([typeof(int), typeof(string)]));
        var ctor = typeof(AutomationProperty).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(int), typeof(string)]);
        Assert.NotNull(ctor);
        Assert.True(ctor!.IsAssembly);
        Assert.Null(typeof(AutomationPattern).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(AutomationPattern).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(int), typeof(string)]));
    }

    [Fact]
    public void Provider_interface_shapes_match_converted_calls()
    {
        Assert.True(typeof(IValueProvider).IsInterface);
        Assert.Equal(typeof(bool), typeof(IValueProvider).GetProperty("IsReadOnly")!.PropertyType);
        Assert.Equal(typeof(string), typeof(IValueProvider).GetProperty("Value")!.PropertyType);
        Assert.NotNull(typeof(IValueProvider).GetMethod("SetValue", [typeof(string)]));
        Assert.True(typeof(ITextProvider).IsInterface);
        Assert.Equal(typeof(ITextRangeProvider), typeof(ITextProvider).GetProperty("DocumentRange")!.PropertyType);
        Assert.Equal(typeof(SupportedTextSelection), typeof(ITextProvider).GetProperty("SupportedTextSelection")!.PropertyType);
        Assert.Equal(typeof(ITextRangeProvider[]), typeof(ITextProvider).GetMethod("GetSelection", Type.EmptyTypes)!.ReturnType);
        Assert.Equal(typeof(ITextRangeProvider[]), typeof(ITextProvider).GetMethod("GetVisibleRanges", Type.EmptyTypes)!.ReturnType);
        Assert.NotNull(typeof(ITextProvider).GetMethod("RangeFromChild", [typeof(IRawElementProviderSimple)]));
        Assert.NotNull(typeof(ITextProvider).GetMethod("RangeFromPoint", [typeof(Point)]));
        Assert.True(typeof(IRawElementProviderSimple).IsInterface);
        Assert.Equal(typeof(ProviderOptions), typeof(IRawElementProviderSimple).GetProperty("ProviderOptions")!.PropertyType);
        Assert.NotNull(typeof(IRawElementProviderSimple).GetMethod("GetPatternProvider", [typeof(int)]));
        Assert.NotNull(typeof(IRawElementProviderSimple).GetMethod("GetPropertyValue", [typeof(int)]));
        Assert.Equal(typeof(IRawElementProviderSimple), typeof(IRawElementProviderSimple).GetProperty("HostRawElementProvider")!.PropertyType);
    }

    [Fact]
    public void TextRangeProvider_shape_matches_converted_range()
    {
        // The exact converted TextRangeProvider member shapes: endpoint/unit
        // enums ride the parameters, ranges and providers ride returns.
        Assert.True(typeof(ITextRangeProvider).IsInterface);
        Assert.NotNull(typeof(ITextRangeProvider).GetMethod("Clone", Type.EmptyTypes));
        Assert.NotNull(typeof(ITextRangeProvider).GetMethod("Compare", [typeof(ITextRangeProvider)]));
        Assert.NotNull(typeof(ITextRangeProvider).GetMethod("CompareEndpoints",
            [typeof(TextPatternRangeEndpoint), typeof(ITextRangeProvider), typeof(TextPatternRangeEndpoint)]));
        Assert.NotNull(typeof(ITextRangeProvider).GetMethod("ExpandToEnclosingUnit", [typeof(TextUnit)]));
        Assert.NotNull(typeof(ITextRangeProvider).GetMethod("FindAttribute",
            [typeof(int), typeof(object), typeof(bool)]));
        Assert.NotNull(typeof(ITextRangeProvider).GetMethod("FindText",
            [typeof(string), typeof(bool), typeof(bool)]));
        Assert.NotNull(typeof(ITextRangeProvider).GetMethod("GetAttributeValue", [typeof(int)]));
        Assert.Equal(typeof(double[]), typeof(ITextRangeProvider).GetMethod("GetBoundingRectangles", Type.EmptyTypes)!.ReturnType);
        Assert.Equal(typeof(IRawElementProviderSimple[]), typeof(ITextRangeProvider).GetMethod("GetChildren", Type.EmptyTypes)!.ReturnType);
        Assert.Equal(typeof(IRawElementProviderSimple), typeof(ITextRangeProvider).GetMethod("GetEnclosingElement", Type.EmptyTypes)!.ReturnType);
        Assert.NotNull(typeof(ITextRangeProvider).GetMethod("GetText", [typeof(int)]));
        Assert.NotNull(typeof(ITextRangeProvider).GetMethod("Move", [typeof(TextUnit), typeof(int)]));
        Assert.NotNull(typeof(ITextRangeProvider).GetMethod("MoveEndpointByRange",
            [typeof(TextPatternRangeEndpoint), typeof(ITextRangeProvider), typeof(TextPatternRangeEndpoint)]));
        Assert.NotNull(typeof(ITextRangeProvider).GetMethod("MoveEndpointByUnit",
            [typeof(TextPatternRangeEndpoint), typeof(TextUnit), typeof(int)]));
        foreach (var name in new[] { "AddToSelection", "RemoveFromSelection", "Select" })
            Assert.NotNull(typeof(ITextRangeProvider).GetMethod(name, Type.EmptyTypes));
        Assert.NotNull(typeof(ITextRangeProvider).GetMethod("ScrollIntoView", [typeof(bool)]));
    }

    [Fact]
    public void RaiseAutomationEvent_records_and_forwards()
    {
        var peer = new ProbePeer(new Button());
        AutomationEventEventArgs? forwarded = null;
        AutomationPeer.AutomationEventRaised += OnRaised;
        try
        {
            AutomationPeer.ClearRaisedForTests();
            Assert.Null(AutomationPeer.LastAutomationEvent);
            peer.RaiseAutomationEvent(AutomationEvents.TextPatternOnTextSelectionChanged);
            Assert.NotNull(forwarded);
            Assert.Same(peer, forwarded.Peer);
            Assert.Equal(AutomationEvents.TextPatternOnTextSelectionChanged, forwarded.EventId);
            Assert.Same(forwarded, AutomationPeer.LastAutomationEvent);
            AutomationPeer.ClearRaisedForTests();
            Assert.Null(AutomationPeer.LastAutomationEvent);
        }
        finally
        {
            AutomationPeer.AutomationEventRaised -= OnRaised;
        }

        void OnRaised(object? sender, AutomationEventEventArgs e) => forwarded = e;
    }

    [Fact]
    public void RaisePropertyChangedEvent_records_readonly_flip()
    {
        // Mirrors the converted RaiseIsReadOnlyChanged path: the
        // IsReadOnlyProperty identifier plus the old/new values land in the
        // recorded payload.
        var peer = new ProbePeer(new Button());
        AutomationPropertyChangedEventArgs? forwarded = null;
        AutomationPeer.AutomationPropertyChanged += OnChanged;
        try
        {
            AutomationPeer.ClearRaisedForTests();
            Assert.Null(AutomationPeer.LastPropertyChanged);
            peer.RaisePropertyChangedEvent(ValuePatternIdentifiers.IsReadOnlyProperty, false, true);
            Assert.NotNull(forwarded);
            Assert.Same(peer, forwarded.Peer);
            Assert.Same(ValuePatternIdentifiers.IsReadOnlyProperty, forwarded.Property);
            Assert.Equal(false, forwarded.OldValue);
            Assert.Equal(true, forwarded.NewValue);
            Assert.Same(forwarded, AutomationPeer.LastPropertyChanged);
            Assert.Throws<ArgumentNullException>(
                () => peer.RaisePropertyChangedEvent(null!, false, true));
        }
        finally
        {
            AutomationPeer.AutomationPropertyChanged -= OnChanged;
        }

        void OnChanged(object? sender, AutomationPropertyChangedEventArgs e) => forwarded = e;
    }

    [Fact]
    public void ProviderFromPeer_returns_null_without_os_tree()
    {
        // The exact converted get_Provider shape (inherited instance call)
        // binds and reports the honest no-provider boundary.
        var peer = new ProbePeer(new Button());
        Assert.Null(peer.ExposeProviderFromPeer(peer));
    }

    [Fact]
    public void FromElement_mints_through_the_element_slot()
    {
        Assert.Null(UIElementAutomationPeer.FromElement(null));
        var plain = new Button();
        var direct = UIElementAutomationPeer.FromElement(plain);
        Assert.IsType<UIElementAutomationPeer>(direct);
        Assert.Same(plain, ((UIElementAutomationPeer)direct!).Owner);
        var custom = new PeerMintingControl();
        var minted = UIElementAutomationPeer.FromElement(custom);
        Assert.IsType<ProbeTextPeer>(minted);
        // CreatePeerForElement keeps the legacy direct peer for elements
        // without an override (AutomationTests pins the exact type).
        Assert.IsType<UIElementAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(plain));
    }

    [Fact]
    public void Converted_peer_shapes_run_end_to_end()
    {
        // Mirrors the converted TextAreaAutomationPeer member shapes over a
        // live engine tree: GetPattern answers Text/Value, selection changes
        // raise the recorded automation event, and the value push lands.
        var control = new PeerMintingControl();
        var peer = (ProbeTextPeer)UIElementAutomationPeer.CreatePeerForElement(control)!;
        Assert.Same(peer, peer.GetPattern(PatternInterface.Text));
        Assert.Same(peer, peer.GetPattern(PatternInterface.Value));
        Assert.Null(peer.GetPattern(PatternInterface.Scroll));
        Assert.Equal(SupportedTextSelection.Single, peer.SupportedTextSelection);
        Assert.False(peer.IsReadOnly);
        Assert.Equal("hello", peer.Value);
        peer.SetValue("world");
        Assert.Equal("world", control.DocumentText);
        var only = Assert.Single(peer.GetSelection());
        Assert.Same(peer, ((ProbeTextPeer.ProbeRange)only).RangePeer);
        AutomationPeer.ClearRaisedForTests();
        peer.FireSelectionChanged();
        Assert.Equal(AutomationEvents.TextPatternOnTextSelectionChanged,
            AutomationPeer.LastAutomationEvent?.EventId);
        AutomationPeer.ClearRaisedForTests();
        peer.FireReadOnlyChanged(false, true);
        Assert.Same(ValuePatternIdentifiers.IsReadOnlyProperty,
            AutomationPeer.LastPropertyChanged?.Property);
    }

    private sealed class ProbePeer : UIElementAutomationPeer
    {
        public ProbePeer(UIElement owner) : base(owner)
        {
            ProbeOwner = owner;
        }

        public UIElement ProbeOwner { get; }

        public IRawElementProviderSimple? ExposeProviderFromPeer(AutomationPeer peer) =>
            ProviderFromPeer(peer);
    }

    private sealed class PeerMintingControl : Control
    {
        public string DocumentText { get; set; } = "hello";

        protected override AutomationPeer? OnCreateAutomationPeer() => new ProbeTextPeer(this);
    }

    private sealed class ProbeTextPeer : FrameworkElementAutomationPeer, ITextProvider, IValueProvider
    {
        public ProbeTextPeer(FrameworkElement owner) : base(owner)
        {
        }

        public bool IsReadOnly => false;

        public string? Value => ((PeerMintingControl)Owner).DocumentText;

        public void SetValue(string value) => ((PeerMintingControl)Owner).DocumentText = value;

        public SupportedTextSelection SupportedTextSelection => SupportedTextSelection.Single;

        public ITextRangeProvider? DocumentRange => new ProbeRange(this);

        public ITextRangeProvider[] GetSelection() => [new ProbeRange(this)];

        public ITextRangeProvider[] GetVisibleRanges() => [];

        public ITextRangeProvider? RangeFromChild(IRawElementProviderSimple childElement) => null;

        public ITextRangeProvider? RangeFromPoint(Point screenLocation) => null;

        public void FireSelectionChanged() =>
            RaiseAutomationEvent(AutomationEvents.TextPatternOnTextSelectionChanged);

        public void FireReadOnlyChanged(bool oldValue, bool newValue) =>
            RaisePropertyChangedEvent(ValuePatternIdentifiers.IsReadOnlyProperty, oldValue, newValue);

        public override object? GetPattern(PatternInterface patternInterface)
        {
            if (patternInterface is PatternInterface.Text or PatternInterface.Value)
                return this;
            return base.GetPattern(patternInterface);
        }

        internal sealed class ProbeRange : ITextRangeProvider
        {
            public ProbeRange(ProbeTextPeer peer)
            {
                RangePeer = peer;
            }

            public ProbeTextPeer RangePeer { get; }

            public ITextRangeProvider Clone() => new ProbeRange(RangePeer);

            public bool Compare(ITextRangeProvider range) => ReferenceEquals(this, range);

            public int CompareEndpoints(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint) => 0;

            public void ExpandToEnclosingUnit(TextUnit unit)
            {
            }

            public ITextRangeProvider? FindAttribute(int attribute, object value, bool backward) => null;

            public ITextRangeProvider? FindText(string text, bool backward, bool ignoreCase) => null;

            public object? GetAttributeValue(int attribute) => null;

            public double[] GetBoundingRectangles() => [];

            public IRawElementProviderSimple[] GetChildren() => [];

            public IRawElementProviderSimple? GetEnclosingElement() => null;

            public string GetText(int maxLength) => RangePeer.Value ?? string.Empty;

            public int Move(TextUnit unit, int count) => 0;

            public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint)
            {
            }

            public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count) => 0;

            public void AddToSelection()
            {
            }

            public void RemoveFromSelection()
            {
            }

            public void ScrollIntoView(bool alignToTop)
            {
            }

            public void Select()
            {
            }
        }
    }
}
