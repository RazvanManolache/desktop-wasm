using System.Collections;
using System.Reflection;
using System.Xml;

namespace DesktopWasm.Wpf;

/// <summary>Slice-99 Blend-style Interaction.Triggers loader surface.
///
/// The HandyControl demo's LeftMainContent wires its tab switch through
/// Blend interactivity instead of only the code-behind handler:
/// &lt;hc:Interaction.Triggers&gt; (EventTrigger EventName="SelectionChanged"
/// driving EventToCommand Command="{Binding SwitchDemoCmd}"
/// PassEventArgsToCommand="True"). The engine carries no interactivity
/// mirror; the converted HandyControl.Interactivity types already stage in
/// the HandyControl lib image, so the loader drives THEM: it resolves the
/// converted TriggerCollection/EventTrigger/EventToCommand by full name
/// (exactly-one across the converted images), applies the XAML attributes
/// (string DPs, CLR bools, {Binding} onto converted DPs), nests the actions,
/// and attaches the collection through the converted
/// Interaction.GetTriggers (which runs the converted attach machinery:
/// TriggerCollection/TriggerBase/AttachableCollection over the engine
/// Freezable preamble, NameResolver deferral to Loaded, reflection
/// subscription to the engine Selector.SelectionChanged CLR event).
///
/// Member evidence via the upstream Shared sources compiled into the staged
/// lib (System.Windows.Interactivity/Interaction.cs GetTriggers with the
/// ShadowTriggers/ShadowBehaviors attached DPs; EventTrigger.cs with the
/// EventName DP defaulting to "Loaded"; EventTriggerBase.cs with reflection
/// RegisterEvent/UnregisterEvent plus the Loaded deferral;
/// TriggerBase.cs [ContentProperty("Actions")] with Attach/Detach/InvokeActions;
/// TriggerAction.cs with IsEnabled plus CallInvoke gating; TriggerCollection
/// (internal ctor) attaching each trigger on Add-when-attached;
/// AttachableCollection`1 over FreezableCollection with the
/// DesignerProperties.IsInDesignMode gate; HandyControl EventToCommand.cs
/// with the Command/CommandParameter/MustToggleIsEnabled/
/// EventArgsConverterParameter/AlwaysInvokeCommand DPs, PassEventArgsToCommand
/// plus MustToggleIsEnabledValue/CommandParameterValue/EventArgsConverter
/// CLR state, and Invoke running CanExecute/Execute with the event args when
/// PassEventArgsToCommand is set). The command binding resolves against the
/// host's DataContext (Source=host, Path="DataContext.X", refreshed on
/// DataContextChanged like the Slice-97 Lang rebind): upstream trigger
/// bindings inherit the trigger site's data context, which is exactly this.
/// Anything outside the covered shapes (unknown trigger/action types,
/// SourceName wiring, EventArgsConverter) fails precisely. Slice-105 adds
/// the ChatBox shape: HandyControl.Interactivity.RoutedEventTrigger with a
/// single RoutedEvent="prefix:Owner.Event" attribute (resolved against the
/// converted "<Owner>Event" routed-event field, e.g. hc:ChatBubble.Selected
/// on SelectableItem.SelectedEvent), driving the same converted
/// EventToCommand actions through the same converted attach machinery.
/// </summary>
public static partial class XamlLoader
{
    private static void ReadInteractionTriggers(UIElement element, XmlReader reader, IXmlLineInfo lineInfo)
    {
        const string original = "<Interaction.Triggers>";
        var host = (DependencyObject)element;
        var interactionType = FindConvertedInteractionType("Interaction", lineInfo, original);
        var triggerType = FindConvertedInteractionType("EventTrigger", lineInfo, original);
        var routedType = FindConvertedInteractionType("RoutedEventTrigger", lineInfo, original);
        var actionType = FindConvertedInteractionType("EventToCommand", lineInfo, original);
        var getTriggers = interactionType.GetMethod("GetTriggers", BindingFlags.Public | BindingFlags.Static);
        if (getTriggers is null)
            throw Fail(lineInfo, "'HandyControl.Interactivity.Interaction' has no GetTriggers (converted shape changed).");
        IList triggers;
        try
        {
            triggers = (IList)getTriggers.Invoke(null, [host])!;
        }
        catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidCastException)
        {
            throw Fail(lineInfo, $"Interaction.GetTriggers failed ({(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}).");
        }
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }
        reader.Read(); // past the property start tag
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, "Unterminated <Interaction.Triggers>.");
            var routed = string.Equals(reader.LocalName, "RoutedEventTrigger", StringComparison.Ordinal);
            if (reader.NodeType != XmlNodeType.Element ||
                (!string.Equals(reader.LocalName, "EventTrigger", StringComparison.Ordinal) && !routed))
                throw Fail(lineInfo, $"<Interaction.Triggers> needs <EventTrigger>/<RoutedEventTrigger> entries (got <{reader.LocalName}>).");
            object trigger;
            try
            {
                trigger = ReadInteractionEventTrigger(reader, lineInfo, routed ? routedType : triggerType, actionType, host, routed);
            }
            catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidOperationException or MissingMethodException)
            {
                throw Fail(lineInfo, $"<EventTrigger> failed ({(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}).");
            }
            triggers.Add(trigger);
        }
        reader.Read(); // past the property end tag
    }

    private static Type FindConvertedInteractionType(string shortName, IXmlLineInfo lineInfo, string original)
    {
        var fullName = "HandyControl.Interactivity." + shortName;
        Type? found = null;
        foreach (var assembly in ConvertedSearchAssemblies())
        {
            var candidate = SafeGetConvertedType(assembly, fullName);
            if (candidate is null) continue;
            if (found is not null && !ReferenceEquals(found, candidate))
                throw Fail(lineInfo, $"'{original}' matches '{found.FullName}' and '{candidate.FullName}' (keep one converted interactivity surface).");
            found = candidate;
        }
        if (found is null)
            throw Fail(lineInfo, $"'{original}' needs the converted HandyControl interactivity surface ('{fullName}' is in no converted image).");
        return found;
    }

    private static object ReadInteractionEventTrigger(XmlReader reader, IXmlLineInfo lineInfo, Type triggerType, Type actionType, DependencyObject host, bool routed = false)
    {
        var tag = routed ? "<RoutedEventTrigger>" : "<EventTrigger>";
        var trigger = Activator.CreateInstance(triggerType, nonPublic: true)!;
        if (trigger is not DependencyObject triggerHost)
            throw Fail(lineInfo, $"'{triggerType.FullName}' is not a DependencyObject (converted shape changed).");
        var sawRoutedEvent = false;
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns" || reader.Name == "xmlns") continue;
                if (reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace") continue;
                var local = reader.LocalName;
                var value = reader.Value;
                if (routed)
                {
                    if (string.Equals(local, "RoutedEvent", StringComparison.Ordinal))
                    {
                        var routedEvent = ResolveConvertedRoutedEvent(value, lineInfo, tag);
                        var setter = triggerType.GetProperty("RoutedEvent", BindingFlags.Public | BindingFlags.Instance);
                        if (setter is null || !setter.CanWrite || setter.SetMethod?.IsPublic != true ||
                            setter.PropertyType != typeof(RoutedEvent))
                            throw Fail(lineInfo, $"'{triggerType.FullName}' has no settable RoutedEvent (converted shape changed).");
                        setter.SetValue(trigger, routedEvent);
                        sawRoutedEvent = true;
                    }
                    else
                        throw Fail(lineInfo, $"'{local}=' is not supported on {tag} (RoutedEvent only).");
                    continue;
                }
                if (string.Equals(local, "EventName", StringComparison.Ordinal) ||
                    string.Equals(local, "SourceName", StringComparison.Ordinal))
                    SetConvertedStringDp(triggerHost, triggerType, local, value, lineInfo, "<EventTrigger>");
                else
                    throw Fail(lineInfo, $"'{local}=' is not supported on <EventTrigger> (EventName/SourceName only).");
            }
            reader.MoveToElement();
        }
        if (routed && !sawRoutedEvent)
            throw Fail(lineInfo, $"'{tag}' needs RoutedEvent='prefix:Owner.Event' (like 'hc:ChatBubble.Selected').");
        var actionsProperty = triggerType.GetProperty("Actions", BindingFlags.Public | BindingFlags.Instance);
        var actions = actionsProperty?.GetValue(trigger) as IList;
        if (actions is null)
            throw Fail(lineInfo, $"'{triggerType.FullName}' has no Actions collection (converted shape changed).");
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return trigger;
        }
        reader.Read(); // past the trigger start tag
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(lineInfo, $"Unterminated {tag}.");
            if (reader.NodeType != XmlNodeType.Element ||
                !string.Equals(reader.LocalName, "EventToCommand", StringComparison.Ordinal))
                throw Fail(lineInfo, $"{tag} needs <EventToCommand> actions (got <{reader.LocalName}>).");
            actions.Add(ReadInteractionEventToCommand(reader, lineInfo, actionType, host));
        }
        reader.Read(); // past the trigger end tag
        return trigger;
    }

    /// <summary>
    /// Resolves a "prefix:Owner.Event" routed-event reference against the
    /// converted images (prefixes are single-assembly qualifiers, stripped
    /// like the ancestor-type lookup). The owner resolves by short name with
    /// the exactly-one rule and the event from its public static
    /// "&lt;Name&gt;Event" field, matching the converted registration shape
    /// (SelectableItem.SelectedEvent for "hc:ChatBubble.Selected").
    /// </summary>
    private static RoutedEvent ResolveConvertedRoutedEvent(string raw, IXmlLineInfo lineInfo, string tag)
    {
        var stripped = raw.Trim();
        var colon = stripped.IndexOf(':');
        if (colon >= 0) stripped = stripped[(colon + 1)..];
        var dot = stripped.LastIndexOf('.');
        if (dot <= 0 || dot == stripped.Length - 1)
            throw Fail(lineInfo, $"'{raw}' is not a routed event ({tag} takes RoutedEvent='prefix:Owner.Event').");
        var ownerName = stripped[..dot];
        var eventName = stripped[(dot + 1)..];
        Type? owner = null;
        foreach (var assembly in ConvertedSearchAssemblies())
        {
            var candidate = SafeGetConvertedType(assembly, "HandyControl.Controls." + ownerName)
                ?? SafeGetConvertedType(assembly, "HandyControl.Interactivity." + ownerName);
            if (candidate is null) continue;
            if (owner is not null && !ReferenceEquals(owner, candidate))
                throw Fail(lineInfo, $"'{raw}' matches '{owner.FullName}' and '{candidate.FullName}' (keep one converted owner).");
            owner = candidate;
        }
        owner ??= ScanUniqueConvertedType(ownerName, lineInfo, raw);
        if (owner is null)
            throw Fail(lineInfo, $"'{raw}' names no converted owner type '{ownerName}'.");
        var field = owner.GetField(eventName + "Event",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (field?.GetValue(null) is RoutedEvent routedEvent)
            return routedEvent;
        throw Fail(lineInfo, $"'{raw}' names no routed event on <{owner.Name}> (no public static '{eventName}Event').");
    }

    private static object ReadInteractionEventToCommand(XmlReader reader, IXmlLineInfo lineInfo, Type actionType, DependencyObject host)
    {
        var action = Activator.CreateInstance(actionType, nonPublic: true)!;
        if (action is not DependencyObject actionHost)
            throw Fail(lineInfo, "'HandyControl.Interactivity.EventToCommand' is not a DependencyObject (converted shape changed).");
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns" || reader.Name == "xmlns") continue;
                if (reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace") continue;
                var local = reader.LocalName;
                var value = reader.Value;
                switch (local)
                {
                    case "Command":
                    case "CommandParameter":
                        if (!IsActionBindingValue(value))
                        {
                            if (!string.Equals(local, "CommandParameter", StringComparison.Ordinal))
                            {
                                // Literal converted commands (the staged
                                // NotifyIconDemo wires
                                // hc:ControlCommands.PushMainWindow2Top and
                                // ShutdownApp straight at its triggers):
                                // resolve like Button.Command, or keep the
                                // legacy miss when no command resolves.
                                if (TryResolveLiteralCommand(value.Trim(), lineInfo, out var literal) &&
                                    literal is not null)
                                {
                                    var commandDp = DependencyProperty.FromName(actionType, "Command");
                                    if (commandDp is null)
                                        throw Fail(lineInfo, $"'<EventToCommand>' has no 'Command' (converted shape changed).");
                                    actionHost.SetValue(commandDp, literal);
                                    break;
                                }
                                throw Fail(lineInfo, $"'{local}=' needs a {{Binding}} (a literal cannot name a command).");
                            }
                            SetConvertedObjectDp(actionHost, actionType, local, value, lineInfo, "<EventToCommand>");
                            break;
                        }
                        ApplyConvertedActionBinding(actionHost, actionType, local, value, host, lineInfo, "<EventToCommand>");
                        break;
                    case "PassEventArgsToCommand":
                        SetConvertedClrBool(action, actionType, local, value, lineInfo, "<EventToCommand>");
                        break;
                    case "MustToggleIsEnabled":
                    case "IsEnabled":
                        SetConvertedBoolDp(actionHost, actionType, local, value, lineInfo, "<EventToCommand>");
                        break;
                    default:
                        throw Fail(lineInfo, $"'{local}=' is not supported on <EventToCommand> (Command/CommandParameter/PassEventArgsToCommand/MustToggleIsEnabled/IsEnabled only).");
                }
            }
            reader.MoveToElement();
        }
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return action;
        }
        reader.Read(); // past the action start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(lineInfo, "<EventToCommand> takes no nested content.");
        reader.Read(); // past the action end tag
        return action;
    }

    private static bool IsActionBindingValue(string value)
    {
        var text = value.Trim();
        return text.StartsWith("{Binding", StringComparison.Ordinal) && text.EndsWith('}');
    }

    private static void SetConvertedStringDp(DependencyObject target, Type owner, string property, string value, IXmlLineInfo lineInfo, string tag)
    {
        var dp = DependencyProperty.FromName(owner, property);
        if (dp is null || dp.PropertyType != typeof(string))
            throw Fail(lineInfo, $"'{tag}' has no string '{property}' (converted shape changed).");
        target.SetValue(dp, value);
    }

    private static void SetConvertedObjectDp(DependencyObject target, Type owner, string property, string value, IXmlLineInfo lineInfo, string tag)
    {
        var dp = DependencyProperty.FromName(owner, property);
        if (dp is null)
            throw Fail(lineInfo, $"'{tag}' has no '{property}' (converted shape changed).");
        try
        {
            target.SetValue(dp, value);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw Fail(lineInfo, $"'{value}' does not fit '{property}' on '{tag}' ({ex.GetType().Name}).");
        }
    }

    private static void SetConvertedBoolDp(DependencyObject target, Type owner, string property, string value, IXmlLineInfo lineInfo, string tag)
    {
        var dp = DependencyProperty.FromName(owner, property);
        if (dp is null || dp.PropertyType != typeof(bool))
            throw Fail(lineInfo, $"'{tag}' has no boolean '{property}' (converted shape changed).");
        target.SetValue(dp, ParseBool(value, lineInfo));
    }

    private static void SetConvertedClrBool(object target, Type owner, string property, string value, IXmlLineInfo lineInfo, string tag)
    {
        var clr = owner.GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
        if (clr is null || clr.PropertyType != typeof(bool) || !clr.CanWrite)
            throw Fail(lineInfo, $"'{tag}' has no settable boolean '{property}' (converted shape changed).");
        clr.SetValue(target, ParseBool(value, lineInfo));
    }

    /// <summary>
    /// Binds a trigger-action DP against the trigger host's DataContext
    /// (Source=host, Path="DataContext.X", re-seated on DataContextChanged
    /// like the Slice-97 Lang rebind): upstream action bindings inherit the
    /// trigger site's data context, which is exactly this path.
    /// </summary>
    private static void ApplyConvertedActionBinding(DependencyObject action, Type actionType, string property, string value, DependencyObject host, IXmlLineInfo lineInfo, string tag)
    {
        var text = value.Trim();
        var binding = ParseBinding(text["{Binding".Length..^1], value, lineInfo);
        binding.Source = host;
        binding.Path = string.IsNullOrEmpty(binding.Path)
            ? "DataContext"
            : "DataContext." + binding.Path;
        var dp = DependencyProperty.FromName(actionType, property);
        if (dp is null)
            throw Fail(lineInfo, $"'{tag}' has no '{property}' (converted shape changed).");
        BindingOperations.SetBinding(action, dp, binding);
        if (host is FrameworkElement site)
            site.DataContextChanged += (_, _) => BindingOperations.SetBinding(action, dp, binding);
    }
}
