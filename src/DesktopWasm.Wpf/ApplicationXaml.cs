using System.Linq.Expressions;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace DesktopWasm.Wpf;

public static partial class XamlLoader
{
    private static void ApplyImplicitStyle(UIElement element, List<FrameworkElement> scope, IXmlLineInfo line, List<XamlEventWire> wires)
    {
        if (element is FrameworkElement target &&
            TryFindInScope(element, element.GetType(), scope, out var value) && value is Style style)
        {
            var context = new StyleApplyContext { StringHandlers = new(), Scope = scope };
            style.ApplyTo(target, context);
            foreach (var (name, handler) in context.StringHandlers)
                wires.Add(new XamlEventWire(element, name, handler));
        }
    }

    /// <summary>Reads a <Style.Resources> block (single ResourceDictionary
    /// or bare entries, like element resources).</summary>
    private static void ParseStyleResources(Style style, XElement wrapper, IXmlLineInfo line)
    {
        using var fragment = System.Xml.XmlReader.Create(new System.IO.StringReader(wrapper.ToString()),
            new System.Xml.XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true });
        fragment.MoveToContent();
        if (fragment.NodeType != System.Xml.XmlNodeType.Element)
            throw Fail(line, "<Style.Resources> needs a <ResourceDictionary> or resource entries.");
        fragment.Read();
        SkipWhitespace(fragment);
        if (fragment.NodeType == System.Xml.XmlNodeType.Element && fragment.LocalName == "ResourceDictionary")
        {
            style.Resources.MergedDictionaries.Add(ReadResourceDictionaryRoot(fragment, line, 0));
            return;
        }
        while (fragment.NodeType == System.Xml.XmlNodeType.Element)
        {
            ReadResourceEntry(fragment, line, style.Resources);
            SkipWhitespace(fragment);
        }
        if (fragment.NodeType != System.Xml.XmlNodeType.EndElement)
            throw Fail(line, "Malformed <Style.Resources> (one dictionary or entries only).");
    }

    private static object ReadStyleKey(XmlReader reader, IXmlLineInfo line)
    {
        var key = reader.GetAttribute("Key", XamlNamespace);
        if (key is not null) return key;
        var target = reader.GetAttribute("TargetType") ?? throw Fail(line, "Style needs TargetType.");
        return ResolveStyleTargetType(target, line, prefix => reader.LookupNamespace(prefix));
    }

    /// <summary>Parses a Style resource entry (positioned on its start tag; consumes it).</summary>
    internal static Style ReadStyleElement(XmlReader reader, IXmlLineInfo line) =>
        ReadStyleNode(XElement.Parse(HoistTemplateNamespaces(reader.ReadOuterXml(), reader, line)), line);

    /// <summary>Parses a Style element (resource entries and nested Setter.Value styles share it).</summary>
    internal static Style ReadStyleNode(XElement style, IXmlLineInfo line)
    {
        foreach (var attribute in style.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            var name = attribute.Name.LocalName;
            if (name is "TargetType" or "Key") continue;
            if (name == "BasedOn") continue;
            throw Fail(line, $"Unsupported Style property '{attribute.Name}' (TargetType/x:Key/BasedOn only).");
        }
        // Keyed styles may omit TargetType (FocusVisual and other keyed theme
        // styles); only unkeyed styles need it for implicit application.
        var targetAttr = (string?)style.Attribute("TargetType");
        if (targetAttr is null && style.Attribute(XName.Get("Key", XamlNamespace)) is null)
            throw Fail(line, "Style needs TargetType.");
        Type? targetType = null;
        if (targetAttr is not null)
        {
            var targetName = targetAttr;
            if (targetName.StartsWith("{x:Type ", StringComparison.Ordinal) && targetName.EndsWith('}')) targetName = targetName[8..^1].Trim();
            targetType = ResolveStyleTargetType(targetName, line, prefix => style.GetNamespaceOfPrefix(prefix)?.NamespaceName);
        }
        var result = targetType is null ? new Style() : new Style(targetType);
        var basedOn = (string?)style.Attribute("BasedOn");
        if (basedOn is not null) result.BasedOnKey = ParseBasedOnKey(basedOn, line, prefix => style.GetNamespaceOfPrefix(prefix)?.NamespaceName);
        foreach (var setter in style.Elements())
        {
            if (IsStyleWrapper(setter, "Setters"))
            {
                foreach (var nested in setter.Elements())
                    result.Setters.Add(ReadAttributeStyleEntry(nested, targetType, line, result.Resources));
                continue;
            }
            if (IsStyleWrapper(setter, "Resources"))
            {
                ParseStyleResources(result, setter, line);
                continue;
            }
            if (IsStyleWrapper(setter, "Triggers"))
            {
                foreach (var trigger in setter.Elements())
                    result.Triggers.Add(ReadStyleTrigger(trigger, targetType, line, result.Resources));
                continue;
            }
            result.Setters.Add(ReadAttributeStyleEntry(setter, targetType, line, result.Resources));
        }
        return result;

        static bool IsStyleWrapper(XElement element, string wrapper) =>
            IsWrapper(element, "Style", wrapper) &&
            element.Name.NamespaceName is "" or "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    }

    /// <summary>Parses an attached-property style setter (values convert eagerly with line info).</summary>
    /// <summary>
    /// Resolves a "Type.Member" static command reference (converted
    /// MenuItem/ControlExample command attributes): engine types first,
    /// then converted-assembly types by short name, like XAML's static
    /// member lookup for command-typed properties.
    /// </summary>
    internal static ICommand ResolveStaticCommand(string value, IXmlLineInfo line)
    {
        if (!value.Contains('.') && !value.Contains(':'))
        {
            // Bare names (Command="Cut") resolve through the known command
            // libraries, like the XAML CommandConverter.
            foreach (var library in new[] { typeof(ApplicationCommands), typeof(NavigationCommands), typeof(MediaCommands), typeof(EditingCommands), typeof(ComponentCommands) })
            {
                var property = library.GetProperty(value, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (property?.CanRead == true && property.GetValue(null) is ICommand bare) return bare;
            }
        }
        var member = ResolveStaticMember(value, line);
        if (member is ICommand command) return command;
        throw Fail(line, $"'{value}' is not an ICommand (static commands resolve Type.Member).");
    }

    /// <summary>Resolves a "Type.Member" public static property or field (engine,
    /// then the converted entry, then a unique scan across the converted images;
    /// a missing member falls through to the next candidate, so vendor
    /// singletons extend engine type names like BooleanToVisibilityConverter).</summary>
    internal static object? ResolveStaticMember(string value, IXmlLineInfo line)
    {
        var dot = value.LastIndexOf('.');
        if (dot <= 0 || dot == value.Length - 1 || value.Contains(' ') || value.Contains('{'))
            throw Fail(line, $"'{value}' is not a static member reference (Type.Member).");
        var typeName = value[..dot];
        var memberName = value[(dot + 1)..];
        var shortName = typeName.Contains(':') ? typeName[(typeName.IndexOf(':') + 1)..] : typeName;
        var flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var seen = new HashSet<Type>();
        // Nested vendor singletons (RatingBar+TextBlockForegroundConverter):
        // the '+' names a nested type on the outer type, like XAML.
        var lookupName = shortName;
        string? nestedName = null;
        var plus = shortName.IndexOf('+');
        if (plus > 0 && plus < shortName.Length - 1)
        {
            lookupName = shortName[..plus];
            nestedName = shortName[(plus + 1)..];
        }
        foreach (var candidate in new[] { FindEngineType(lookupName), FindConvertedType(lookupName) })
        {
            if (candidate is null || !seen.Add(candidate)) continue;
            if (TryReadNestedStatic(candidate, nestedName, memberName, flags, out var member)) return member;
        }
        var matches = nestedName is null
            ? FindConvertedShortNameMatches(lookupName)
            : FindConvertedShortNameMatches(lookupName)
                .Select(m => m.GetNestedType(nestedName, BindingFlags.Public | BindingFlags.NonPublic))
                .OfType<Type>().ToList();
        if (matches.Count == 1 && seen.Add(matches[0]))
        {
            if (TryReadNestedStatic(matches[0], null, memberName, flags, out var single)) return single;
        }
        else if (matches.Count > 1)
        {
            var owner = ScanConvertedMemberOwner(matches, memberName, line, value);
            if (owner is not null)
            {
                var property = owner.GetProperty(memberName, flags);
                if (property?.CanRead == true) return property.GetValue(null);
                var field = owner.GetField(memberName, flags);
                if (field is not null) return field.GetValue(null);
            }
        }

        static bool TryReadNestedStatic(Type candidate, string? nestedName, string memberName, BindingFlags memberFlags, out object? member)
        {
            member = null;
            var owner = nestedName is null ? candidate
                : candidate.GetNestedType(nestedName, BindingFlags.Public | BindingFlags.NonPublic);
            if (owner is null) return false;
            var property = owner.GetProperty(memberName, memberFlags);
            if (property?.CanRead == true)
            {
                member = property.GetValue(null);
                return true;
            }
            var field = owner.GetField(memberName, memberFlags);
            if (field is null) return false;
            member = field.GetValue(null);
            return true;
        }
        if (seen.Count == 0 && matches.Count == 0)
            throw Fail(line, $"Static member '{value}' names no engine or converted type '{shortName}'.");
        throw Fail(line, $"Static member '{value}' is not a public static property or field of '{shortName}'.");
    }

    /// <summary>Reads a ContextMenu element subtree (style Setter.Value menu rows).</summary>
    internal static ContextMenu ReadContextMenuElement(XElement node, IXmlLineInfo line)
    {
        var menu = new ContextMenu();
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (attribute.Name.NamespaceName == XamlNamespace) continue;
            if (attribute.Name.LocalName == "FontFamily")
            {
                var fontRaw = attribute.Value.Trim();
                if (fontRaw.StartsWith("{Binding", StringComparison.Ordinal) && fontRaw.EndsWith('}'))
                    menu.SetBinding("FontFamily", ParseStandaloneBinding(fontRaw, "FontFamily", line));
                else
                    menu.FontFamily = new FontFamily(fontRaw);
                continue;
            }
            throw Fail(line, $"<ContextMenu> takes a FontFamily attribute and child <MenuItem>/<Separator> rows (got attribute '{attribute.Name.LocalName}').");
        }
        foreach (var child in node.Elements())
        {
            if (child.Name.LocalName == "Separator")
            {
                menu.AddChild(new Separator());
                continue;
            }
            if (child.Name.LocalName != "MenuItem")
                throw Fail(line, $"<ContextMenu> takes <MenuItem> rows (got <{child.Name.LocalName}>).");
            menu.AddItem(ReadMenuItemElement(child, line));
        }
        return menu;
    }

    private static MenuItem ReadMenuItemElement(XElement node, IXmlLineInfo line)
    {
        var item = new MenuItem();
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (attribute.Name.NamespaceName == XamlNamespace) continue;
            var local = attribute.Name.LocalName;
            if (local == "Header") item.Header = attribute.Value;
            else if (local == "Command") item.Command = ResolveStaticCommand(attribute.Value, line);
            else if (local == "CommandParameter") item.CommandParameter = attribute.Value;
            else throw Fail(line, $"<MenuItem> supports Header/Command/CommandParameter (got '{local}').");
        }
        foreach (var child in node.Elements())
        {
            if (child.Name.LocalName == "MenuItem.Icon")
            {
                var iconChild = child.Elements().FirstOrDefault()
                    ?? throw Fail(line, "<MenuItem.Icon> needs an icon element.");
                if (child.Elements().Skip(1).Any())
                    throw Fail(line, "<MenuItem.Icon> takes one icon element.");
                item.Icon = ReadMenuIconElement(iconChild, line);
                continue;
            }
            if (child.Name.LocalName != "MenuItem")
                throw Fail(line, $"<MenuItem> takes nested <MenuItem> rows (got <{child.Name.LocalName}>).");
            item.AddChild(ReadMenuItemElement(child, line));
        }
        return item;
    }

    /// <summary>Builds a menu icon element (converted PackIcon) with converted attribute values.</summary>
    private static object ReadMenuIconElement(XElement node, IXmlLineInfo line)
    {
        var icon = CreateElement(node.Name.LocalName, node.Name.NamespaceName, line);
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            var prop = icon.GetType().GetProperty(attribute.Name.LocalName,
                BindingFlags.Public | BindingFlags.Instance);
            if (prop is null || !prop.CanWrite || prop.SetMethod?.IsPublic != true)
                throw Fail(line, $"'{attribute.Name.LocalName}' is not a settable property of <{icon.GetType().Name}>.");
            object? converted;
            try { converted = StyleValueConverter.Convert(attribute.Value, prop.PropertyType); }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException or ArgumentException)
            { throw Fail(line, $"'{attribute.Value}' is not a {prop.PropertyType.Name} for <{icon.GetType().Name}.{prop.Name}>."); }
            if (converted is null)
                throw Fail(line, $"'{attribute.Value}' is not a {prop.PropertyType.Name} for <{icon.GetType().Name}.{prop.Name}>.");
            try { prop.SetValue(icon, converted); }
            catch (Exception ex) when (ex is TargetInvocationException or ArgumentException)
            { throw Fail(line, $"'{attribute.Value}' is not a {prop.PropertyType.Name} for <{icon.GetType().Name}.{prop.Name}>."); }
        }
        if (node.Elements().Any())
            throw Fail(line, $"<{node.Name.LocalName}> menu icons take attributes only.");
        return icon;
    }

    private static Setter ReadAttachedSetter(string property, string value, IXmlLineInfo line)
    {
        if (ParseStyleSetterBindingOrNull(value, line) is { } attachedBinding)
        {
            // Attribute-form {Binding} values bind at apply, like WPF.
            return new Setter { AttachedProperty = property, Value = attachedBinding };
        }
        if (value.TrimStart().StartsWith('{'))
        {
            // Resource references defer to apply time (style setters apply
            // after the whole dictionary is loaded, like WPF's load order).
            return new Setter { AttachedProperty = property, RawValue = value };
        }
        var dot = property.IndexOf('.');
        if (dot > 0)
        {
            // Owner-qualified DPs (Border.CornerRadius) apply through the DP
            // store on the styled element, like WPF's qualified setters.
            var owner = FindEngineType(property[..dot]);
            var dp = owner is not null ? DependencyProperty.FromName(owner, property[(dot + 1)..]) : null;
            if (dp is not null)
                return ParseStyleSetterBindingOrNull(value, line) is { } ownerBinding
                    ? new Setter(dp, ownerBinding)
                    : new Setter(dp, null) { RawValue = value };
            // Converted attached DPs (wpf:ButtonAssist.CornerRadius): vendor
            // libraries register them on converted owner types; unknown
            // owners keep the precise trigger-owner error below.
            var converted = ResolveTriggerOwner(property[..dot], line);
            var convertedDp = converted is not null ? DependencyProperty.FromName(converted, property[(dot + 1)..]) : null;
            if (convertedDp is not null) return new Setter(convertedDp, null) { RawValue = value };
        }
        return new Setter { AttachedProperty = property, Value = ParseAttachedValue(property, value, line) };
    }

    /// <summary>
    /// Parses one attached value shared by style setters and template visuals
    /// (Grid/Dock/Canvas/WindowChrome/KeyboardNavigation/FocusManager/
    /// ToolTipService/AutomationProperties/TextElement); the value lands in
    /// the same attached bag in both paths.
    /// </summary>
    private static object ParseAttachedValue(string property, string raw, IXmlLineInfo line)
    {
        if (property == "TextElement.Foreground")
        {
            // Brush parse mirrors the attribute path (same attached bag at apply).
            try { return new SolidColorBrush(StyleValueConverter.ColorFromRgba(ParseColorCore(raw))); }
            catch (ArgumentException ex) { throw Fail(line, ex.Message); }
        }
        return property switch
        {
            "Grid.Row" or "Row" or "Grid.Column" or "Column" or "Grid.RowSpan" or "RowSpan"
                or "Grid.ColumnSpan" or "ColumnSpan" => ParseIntValue(property, raw, line),
            "Canvas.Left" or "Canvas.Top" => ParseDoubleValue(property, raw, line),
            "Canvas.ZIndex" or "Panel.ZIndex" => ParseIntValue(property, raw, line),
            "DockPanel.Dock" => DockPanel.TryParseDock(raw, out var dock)
                ? dock
                : throw Fail(line, $"'{raw}' is not a Dock (Left/Top/Right/Bottom)."),
            "WindowChrome.IsHitTestVisibleInChrome" => ParseBoolValue(property, raw, line),
            "KeyboardNavigation.TabIndex" => ParseIntValue(property, raw, line),
            "KeyboardNavigation.IsTabStop" => ParseBoolValue(property, raw, line),
            "KeyboardNavigation.TabNavigation" or "KeyboardNavigation.DirectionalNavigation" => ParseKeyboardNavigationModeValue(property, raw, line),
            "ScrollViewer.CanContentScroll" => ParseBoolValue(property, raw, line),
            "ScrollViewer.HorizontalScrollBarVisibility" or "ScrollViewer.VerticalScrollBarVisibility" =>
                Enum.TryParse<ScrollBarVisibility>(raw.Trim(), ignoreCase: true, out var visibility)
                    ? visibility
                    : throw Fail(line, $"'{raw}' is not a ScrollBarVisibility (Disabled/Auto/Hidden/Visible)."),
            "ScrollViewer.IsDeferredScrollingEnabled" => ParseBoolValue(property, raw, line),
            "Stylus.IsFlicksEnabled" => ParseBoolValue(property, raw, line),
            "Stylus.IsPressAndHoldEnabled" => ParseBoolValue(property, raw, line),
            "ScrollViewer.PanningMode" => Enum.TryParse<PanningMode>(raw.Trim(), ignoreCase: true, out var panning)
                ? panning
                : throw Fail(line, $"'{raw}' is not a PanningMode (None/HorizontalOnly/VerticalOnly/Both/HorizontalFirst/VerticalFirst)."),
            "Grid.IsSharedSizeScope" => ParseBoolValue(property, raw, line),
            "SelectiveScrollingGrid.SelectiveScrollingOrientation" => Enum.TryParse<SelectiveScrollingOrientation>(raw.Trim(), ignoreCase: true, out var selective)
                ? selective
                : throw Fail(line, $"'{raw}' is not a SelectiveScrollingOrientation (None/Horizontal/Vertical/Both)."),
            "ToolTipService.InitialShowDelay" => ParseIntValue(property, raw, line),
            "FocusManager.IsFocusScope" => ParseBoolValue(property, raw, line),
            "ToolTipService.ToolTip" or "ToolTipService.Placement" => raw,
            "ToolTipService.HorizontalOffset" or "ToolTipService.VerticalOffset" => ParseDoubleValue(property, raw, line),
            "RenderOptions.EdgeMode" => Enum.TryParse<EdgeMode>(raw.Trim(), ignoreCase: true, out var edge)
                ? edge
                : throw Fail(line, $"'{raw}' is not an EdgeMode (Unspecified/Aliased)."),
            "RenderOptions.ClearTypeHint" => Enum.TryParse<ClearTypeHint>(raw.Trim(), ignoreCase: true, out var hint)
                ? hint
                : throw Fail(line, $"'{raw}' is not a ClearTypeHint (Auto/Enabled)."),
            "TextOptions.TextFormattingMode" => Enum.TryParse<TextFormattingMode>(raw.Trim(), ignoreCase: true, out var formatting)
                ? formatting
                : throw Fail(line, $"'{raw}' is not a TextFormattingMode (Ideal/Display)."),
            "TextOptions.TextRenderingMode" => Enum.TryParse<TextRenderingMode>(raw.Trim(), ignoreCase: true, out var rendering)
                ? rendering
                : throw Fail(line, $"'{raw}' is not a TextRenderingMode (Auto/Aliased/Grayscale/ClearType)."),
            "TextOptions.TextHintingMode" => Enum.TryParse<TextHintingMode>(raw.Trim(), ignoreCase: true, out var hinting)
                ? hinting
                : throw Fail(line, $"'{raw}' is not a TextHintingMode (Auto/Fixed/Animated)."),
            "AutomationProperties.Name" or "AutomationProperties.HelpText" or "AutomationProperties.AutomationId" => raw,
            "TextBlock.FontSize" => ParseDoubleValue(property, raw, line),
            "TextBlock.FontWeight" => XamlLoader.ParseFontWeight(raw, line),
            "TextElement.FontSize" => ParseDoubleValue(property, raw, line),
            "TextElement.FontWeight" => XamlLoader.ParseFontWeight(raw, line),
            "AutomationProperties.HeadingLevel" => Enum.TryParse<AutomationHeadingLevel>(raw.Trim(), ignoreCase: true, out var heading)
                ? heading
                : throw Fail(line, $"'{raw}' is not an AutomationHeadingLevel (None/Level1..Level9)."),
            _ => throw Fail(line, $"Unknown attached property '{property}' (Grid.Row/Grid.Column/Grid.RowSpan/Grid.ColumnSpan/Canvas.Left/Canvas.Top/Canvas.ZIndex/Panel.ZIndex/DockPanel.Dock/WindowChrome.IsHitTestVisibleInChrome/KeyboardNavigation.TabIndex/KeyboardNavigation.IsTabStop/KeyboardNavigation.TabNavigation/KeyboardNavigation.DirectionalNavigation/ScrollViewer.CanContentScroll/ScrollViewer.HorizontalScrollBarVisibility/ScrollViewer.VerticalScrollBarVisibility/ScrollViewer.IsDeferredScrollingEnabled/ScrollViewer.PanningMode/Stylus.IsFlicksEnabled/Stylus.IsPressAndHoldEnabled/ToolTipService.InitialShowDelay/FocusManager.IsFocusScope/ToolTipService.ToolTip/ToolTipService.Placement/ToolTipService.HorizontalOffset/ToolTipService.VerticalOffset/AutomationProperties.Name/AutomationProperties.HelpText/AutomationProperties.AutomationId/AutomationProperties.HeadingLevel/TextElement.Foreground/TextBlock.FontSize/TextBlock.FontWeight/TextElement.FontSize/TextElement.FontWeight/RenderOptions.EdgeMode/RenderOptions.ClearTypeHint/TextOptions.TextFormattingMode/TextOptions.TextRenderingMode/TextOptions.TextHintingMode/Grid.IsSharedSizeScope/SelectiveScrollingGrid.SelectiveScrollingOrientation)."),
        };
    }

    /// <summary>
    /// Finds a converted-app type by short name without letting unloadable
    /// framework references escape (the converted gallery carries a few).
    /// </summary>
    internal static Type? FindConvertedType(string shortName)
    {
        var source = CustomElementSource;
        if (source is null) return null;
        try
        {
            if (source.GetType(shortName) is { } direct) return direct;
            return WpfAppBoot.SafeGetTypes(source).FirstOrDefault(t => t.Name == shortName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Resolves a style TargetType (short name, prefix:Name or {x:Type ...}).</summary>
    internal static Type ResolveStyleTargetType(string targetName, IXmlLineInfo line, Func<string, string?>? xmlns = null)
    {
        var name = targetName.Trim();
        if (name.StartsWith("{x:Type ", StringComparison.Ordinal) && name.EndsWith('}'))
            name = name["{x:Type ".Length..^1].Trim();
        // Qualified names honor their xmlns (a vendor wpf:Clock beats the
        // engine animation Clock); unqualified names keep engine-first order.
        var colon = name.IndexOf(':');
        if (colon >= 0 && xmlns is not null && xmlns(name[..colon]) is { } uri &&
            (uri.StartsWith("clr-namespace:", StringComparison.Ordinal) || XmlNamespacesFor(uri) is not null))
            return ResolveConvertedTypeRef(name, line, xmlns, targetName);
        var shortName = colon >= 0 ? name[(colon + 1)..] : name;
        // Type-only resolution first (no instantiation side effects, so theme
        // dictionaries resolve converted targets without running ctors); the
        // element path below stays the fallback for anything else.
        if (FindEngineType(shortName) is { } engine) return engine;
        if (FindConvertedType(shortName) is { } converted) return converted;
        try { return CreateElement(shortName, string.Empty, line).GetType(); }
        catch (Exception ex) when (ex is InvalidOperationException || ex is XamlLoadException) { }
        throw Fail(line, $"Style TargetType '{targetName}' names no engine or converted type.");
    }

    /// <summary>Parses BasedOn="{StaticResource Key}" (or {StaticResource {x:Type ...}} or {x:Null} for no base, like WPF).</summary>
    internal static object? ParseBasedOnKey(string value, IXmlLineInfo line, Func<string, string?>? xmlns = null)
    {
        var text = value.Trim();
        if (text == "{x:Null}") return null;
        if (text.StartsWith("{StaticResource ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            var inner = text["{StaticResource ".Length..^1].Trim();
            if (inner.StartsWith("{x:Type ", StringComparison.Ordinal) && inner.EndsWith('}'))
                return ResolveStyleTargetType(inner, line, xmlns);
            if (inner.Length == 0 || inner.Contains('{') || inner.Contains('='))
                throw Fail(line, $"BasedOn '{value}' takes a plain key ({{StaticResource Key}}).");
            return inner;
        }
        throw Fail(line, $"BasedOn '{value}' needs markup ({{StaticResource Key}}), like WPF.");
    }

    /// <summary>Reads one bare Setter/EventSetter style entry (also used inside Style.Setters).</summary>
    private static SetterBase ReadAttributeStyleEntry(XElement setter, Type? targetType, IXmlLineInfo line, ResourceDictionary? styleResources = null)
    {
        if (setter.Name.LocalName == "Setter" && setter.HasElements &&
            setter.Elements().Count() == 1 && setter.Elements().First().Name.LocalName == "Setter.Value" &&
            (string?)setter.Attribute("Property") == "Template")
        {
            if (targetType is null)
                throw Fail(line, "Setter Property=\"Template\" needs a TargetType to resolve (qualify it as Owner.Template).");
            return ReadTemplateSetter(targetType, setter.Elements().First(), line, styleResources);
        }
        if (setter.Name.LocalName == "Setter" && setter.HasElements &&
            setter.Elements().Count() == 1 && setter.Elements().First().Name.LocalName == "Setter.Value" &&
            setter.Elements().First().Elements().FirstOrDefault() is { } templateNode &&
            templateNode.Name.LocalName == "ControlTemplate")
        {
            // Object-valued template setters (converted OxyPlot theme styles):
            // Setter.Value carries the ControlTemplate for a ControlTemplate DP.
            var property = (string?)setter.Attribute("Property") ?? throw Fail(line, "Setter needs Property.");
            var template = ReadControlTemplate(templateNode, targetType, line, styleResources);
            var dp = ResolveTemplateSetterProperty(property, targetType, line);
            if (!dp.PropertyType.IsAssignableFrom(typeof(ControlTemplate)))
                throw Fail(line, $"'{property}' takes {dp.PropertyType.Name}, not a <ControlTemplate>.");
            return new Setter(dp, template);
        }
        if (setter.Name.LocalName == "Setter" && setter.HasElements &&
            setter.Elements().Count() == 1 && setter.Elements().First().Name.LocalName == "Setter.Value" &&
            setter.Elements().First().Elements().FirstOrDefault() is { } panelNode &&
            panelNode.Name.LocalName == "ItemsPanelTemplate")
        {
            // Object-valued panel setters (the ComboBox VirtualizingStackPanel):
            // Setter.Value carries the ItemsPanelTemplate for an ItemsPanel DP.
            var property = (string?)setter.Attribute("Property") ?? throw Fail(line, "Setter needs Property.");
            var panelDp = targetType is not null ? DependencyProperty.FromName(targetType, property) : null;
            if (panelDp is null || !panelDp.PropertyType.IsAssignableFrom(typeof(ItemsPanelTemplate)))
                throw Fail(line, $"'{property}' takes {panelDp?.PropertyType.Name ?? "an ItemsPanelTemplate"}, not an <ItemsPanelTemplate>.");
            using var panelReader = panelNode.CreateReader();
            panelReader.Read();
            return new Setter(panelDp, ReadItemsPanelTemplateElement(panelReader, line));
        }
        if (setter.Name.LocalName == "Setter" && setter.HasElements &&
            setter.Elements().Count() == 1 && setter.Elements().First().Name.LocalName == "Setter.Value" &&
            setter.Elements().First().Elements().FirstOrDefault() is { } behaviorRoot &&
            behaviorRoot.Name.LocalName == "BehaviorCollection")
        {
            // Vendor behavior collections (MaterialDesign BehaviorsAssist):
            // the converted attached DP drives the attach, like WPF.
            return ReadBehaviorsAssistSetter(setter, behaviorRoot, line);
        }
        if (setter.Name.LocalName == "Setter" && setter.HasElements &&
            setter.Elements().Count() == 1 && setter.Elements().First().Name.LocalName == "Setter.Value" &&
            setter.Elements().First().Elements().FirstOrDefault() is { } bindingNode &&
            bindingNode.Name.LocalName is "Binding" or "MultiBinding" or "PriorityBinding" && targetType is not null)
        {
            // Bound style setters (the TextBox character counter): the value
            // parses now and live-binds per element at apply, like WPF.
            var property = (string?)setter.Attribute("Property") ?? throw Fail(line, "Setter needs Property.");
            var bindingDp = DependencyProperty.FromName(targetType, property)
                ?? (StylePropertyCache.Find(targetType, property) is { } bindingProp
                    ? DependencyProperty.Register(property, bindingProp.PropertyType, targetType)
                    : throw Fail(line, $"'{property}' is not a settable property of <{targetType.Name}>."));
            BindingBase bindingValue = bindingNode.Name.LocalName switch
            {
                "Binding" => ReadTemplateMultiChild(bindingNode, line),
                "MultiBinding" => ReadTemplateMultiBinding(bindingNode, line),
                _ => ReadTriggerPriorityBinding(bindingNode, line),
            };
            return new Setter(bindingDp, bindingValue);
        }
        if (setter.Name.LocalName == "Setter" && setter.HasElements &&
            setter.Elements().Count() == 1 && setter.Elements().First().Name.LocalName == "Setter.Value")
        {
            // Object-valued styles (the gallery's SelectionTextBox
            // ContextMenu): Setter.Value carries one element instance.
            var property = (string?)setter.Attribute("Property") ?? throw Fail(line, "Setter needs Property.");
            var content = setter.Elements().First().Elements().ToList();
            if (targetType is not null && content.Count == 1 && content[0].Name.LocalName == "ContextMenu" &&
                StylePropertyCache.Find(targetType, property)?.PropertyType == typeof(ContextMenu))
            {
                var dp = DependencyProperty.FromName(targetType, property)
                    ?? DependencyProperty.Register(property, typeof(ContextMenu), targetType);
                return new Setter(dp, ReadContextMenuElement(content[0], line));
            }
            if (targetType is not null && content.Count == 1 && content[0].Name.LocalName == "Style")
            {
                // Style-valued setters (the Clock ButtonStyle/LesserButtonStyle
                // nested styles): the value parses through the shared style
                // path with its own TargetType.
                var styleDp = DependencyProperty.FromName(targetType, property)
                    ?? (StylePropertyCache.Find(targetType, property) is { } styleProp
                        ? DependencyProperty.Register(property, styleProp.PropertyType, targetType)
                        : throw Fail(line, $"'{property}' is not a settable property of <{targetType.Name}>."));
                if (!styleDp.PropertyType.IsAssignableFrom(typeof(Style)))
                    throw Fail(line, $"'{property}' takes {styleDp.PropertyType.Name}, not a <Style>.");
                return new Setter(styleDp, ReadStyleNode(content[0], line));
            }
            if (targetType is not null && content.Count == 1 && content[0].Name.LocalName is "DataTemplate" or "HierarchicalDataTemplate")
            {
                // Template-valued setters (ScrollViewer ContentTemplate):
                // the DataTemplate markup inflates per use, like streamed
                // templates.
                var templateDp = DependencyProperty.FromName(targetType, property)
                    ?? (StylePropertyCache.Find(targetType, property) is { } templateProp
                        ? DependencyProperty.Register(property, templateProp.PropertyType, targetType)
                        : throw Fail(line, $"'{property}' is not a settable property of <{targetType.Name}>."));
                if (!templateDp.PropertyType.IsAssignableFrom(typeof(DataTemplate)))
                    throw Fail(line, $"'{property}' takes {templateDp.PropertyType.Name}, not a <DataTemplate>.");
                return new Setter(templateDp, ReadDataTemplateNode(content[0], line));
            }
            if (targetType is not null && content.Count == 1)
            {
                // Object-valued setters (ShowMeTheXAML XamlDisplay
                // Formatter): Setter.Value carries one CLR instance whose
                // attributes configure it, like a resource entry.
                var objectDp = DependencyProperty.FromName(targetType, property)
                    ?? (StylePropertyCache.Find(targetType, property) is { } objectProp
                        ? DependencyProperty.Register(property, objectProp.PropertyType, targetType)
                        : throw Fail(line, "Setter property " + property + " is not settable."));
                var value = ReadSetterValueObject(content[0], line);
                if (!objectDp.PropertyType.IsAssignableFrom(value.GetType()))
                    throw Fail(line, "Setter takes " + objectDp.PropertyType.Name);
                return new Setter(objectDp, value);
            }
        }
        if (setter.Name.LocalName == "Setter" && !setter.HasElements &&
            setter.Attributes().All(a => a.Name.LocalName is "Property" or "Value"))
        {
            var property = (string?)setter.Attribute("Property") ?? throw Fail(line, "Setter needs Property.");
            var value = (string?)setter.Attribute("Value") ?? throw Fail(line, "Setter needs Value.");
            if (property.Contains('.'))
            {
                var ownerText = property[..property.IndexOf('.')];
                if (ownerText.Contains(':') && !value.TrimStart().StartsWith('{'))
                {
                    // Prefixed owners resolve xmlns-aware: wpf:ButtonAssist is
                    // the vendor type even when the entry defines a same-named
                    // helper. Literals convert through the DP type at apply.
                    var owner = ResolveConvertedTypeRef(ownerText, line,
                        prefix => setter.GetNamespaceOfPrefix(prefix)?.NamespaceName, property);
                    var member = property[(property.IndexOf('.') + 1)..];
                    var attached = DependencyProperty.FromName(owner, member)
                        ?? throw Fail(line, $"'{property}' names no dependency property on <{owner.Name}>.");
                    return new Setter(attached, null) { RawValue = value };
                }
                return ReadAttachedSetter(property, value, line);
            }
            if (targetType is null)
                throw Fail(line, $"Setter Property=\"{property}\" needs a TargetType or an owner-qualified Property (Owner.Property).");
            var dp = DependencyProperty.FromName(targetType, property)
                ?? (StylePropertyCache.Find(targetType, property) is { } prop
                    ? DependencyProperty.Register(property, prop.PropertyType, targetType)
                    : throw Fail(line, $"'{property}' is not a settable property of <{targetType.Name}>."));
            return ParseStyleSetterBindingOrNull(value, line) is { } plainBinding
            ? new Setter(dp, plainBinding)
            : new Setter(dp, null) { RawValue = value };
        }
        if (setter.Name.LocalName == "EventSetter" && !setter.HasElements &&
            setter.Attributes().All(a => a.Name.LocalName is "Event" or "Handler"))
        {
            var name = (string?)setter.Attribute("Event") ?? throw Fail(line, "EventSetter needs Event.");
            var handler = (string?)setter.Attribute("Handler") ?? throw Fail(line, "EventSetter needs Handler.");
            if (targetType is null)
                throw Fail(line, $"EventSetter '{name}' needs a TargetType to resolve the routed event.");
            var routed = EventManager.Find(targetType, name)
                ?? throw Fail(line, $"'{name}' is not a routed event of <{targetType.Name}> (EventSetter needs a registered RoutedEvent).");
            return new EventSetter { Event = routed, HandlerName = handler };
        }
        throw Fail(line, $"This Style supports Setter Property/Value, Template/ItemsPanel/Binding Setter.Value and EventSetter Event/Handler entries (Trigger lives under <Style.Triggers>; got <{setter.Name.LocalName}> [{string.Join(",", setter.Attributes().Select(a => a.Name.LocalName + "=" + (a.Value.Length > 60 ? a.Value[..60] + "?" : a.Value)))}] children [{string.Join(",", setter.Elements().Select(e => e.Name.LocalName))}] in \"{setter.Name.NamespaceName}\").");
    }

    /// <summary>
    /// Matches a property-element wrapper in bare (v1) or Owner.Property (real
    /// XAML, e.g. Style.Setters) form. Setter/Trigger/Condition entries stay
    /// bare, like WPF.
    private static bool IsWrapper(XElement element, string owner, string property)
    {
        var local = element.Name.LocalName;
        return local == property || local == owner + "." + property;
    }

    /// <summary>
    /// Reads one Style.Triggers entry (Trigger/MultiTrigger/DataTrigger/
    /// MultiDataTrigger/EventTrigger share the template trigger readers;
    /// styles evaluate them live, templates at inflation).
    /// </summary>
    internal static TriggerBase ReadStyleTrigger(XElement node, Type? targetType, IXmlLineInfo line, ResourceDictionary? styleResources = null)
    {
        var trigger = ReadTrigger(node, targetType, line, null, styleResources);
        // Style triggers apply setters only (no activation tracking): action
        // storyboards stay template-only, like TargetName setters.
        if (trigger.EnterActions.Count > 0 || trigger.ExitActions.Count > 0)
            throw Fail(line, $"<{node.Name.LocalName}> Enter/ExitActions need a template trigger (style triggers apply setters only).");
        return trigger;
    }

    /// <summary>Reads Trigger.EnterActions/Trigger.ExitActions wrappers (BeginStoryboard actions).</summary>
    private static bool TryReadTriggerActions(XElement child, string owner, TriggerBase trigger, IXmlLineInfo line, Func<string, object?>? resolveResource = null)
    {
        if (IsWrapper(child, owner, "EnterActions"))
        {
            foreach (var action in child.Elements())
                trigger.EnterActions.Add(ReadTriggerAction(action, line, null, resolveResource));
            return true;
        }
        if (IsWrapper(child, owner, "ExitActions"))
        {
            foreach (var action in child.Elements())
                trigger.ExitActions.Add(ReadTriggerAction(action, line, null, resolveResource));
            return true;
        }
        return false;
    }

    /// <summary>Reads one trigger node (Trigger/MultiTrigger/DataTrigger/MultiDataTrigger/EventTrigger).</summary>
    /// <summary>Rewrites a prefixed attached trigger property
    /// (wpf:TransitionAssist.DisableTransitions) to its converted owner
    /// full name (mirrors setter owner resolution; triggers evaluate
    /// without XAML scope). Anything else keeps the original string so
    /// existing errors surface unchanged.</summary>
    private static string CanonicalizeTriggerProperty(string property, IXmlLineInfo line, Func<string, string?> xmlns)
    {
        var colon = property.IndexOf(':');
        if (colon <= 0) return property;
        var rest = property[(colon + 1)..];
        var dot = rest.IndexOf('.');
        if (dot <= 0) return property;
        var uri = xmlns(property[..colon]);
        if (uri is null) return property;
        var owner = XamlLoader.ResolveVendorOwnerType(uri, rest[..dot], line, property);
        if (owner?.FullName is null) return property;
        return owner.FullName + "." + rest[(dot + 1)..];
    }

    internal static TriggerBase ReadTrigger(XElement node, Type? targetType, IXmlLineInfo line, Func<string, object?>? resolveResource = null, ResourceDictionary? styleResources = null)
    {
        var local = node.Name.LocalName;
        if (local == "Trigger")
        {
            var sourceName = (string?)node.Attribute("SourceName");
            var trigger = new Trigger
            {
                Property = CanonicalizeTriggerProperty((string?)node.Attribute("Property") ?? throw Fail(line, "<Trigger> needs Property=."), line, prefix => node.GetNamespaceOfPrefix(prefix)?.NamespaceName),
                SourceName = sourceName,
                Value = ConvertTriggerValue(
                    (string?)node.Attribute("Property") ?? string.Empty,
                    (string?)node.Attribute("Value"), targetType, line, sourceName is not null,
                    prefix => node.GetNamespaceOfPrefix(prefix)?.NamespaceName),
            };
            foreach (var setter in node.Elements())
            {
                if (setter.Name.LocalName == "DataTrigger.Binding") continue;
                if (setter.Name.LocalName == "Setter")
                {
                    trigger.Setters.Add(ReadTriggerSetter(setter, line, targetType, styleResources));
                    continue;
                }
                if (TryReadTriggerActions(setter, "Trigger", trigger, line, resolveResource)) continue;
                throw Fail(line, $"<{setter.Name.LocalName}> is not valid in <Trigger> (Setter and Trigger.EnterActions/Trigger.ExitActions only).");
            }
            return trigger;
        }
        if (local == "MultiTrigger")
            return ReadMultiTrigger(node, targetType, line, resolveResource, styleResources);
        if (local == "DataTrigger")
        {
            var trigger = new DataTrigger
            {
                Binding = ReadTriggerBinding(node, line),
                Value = ReadTriggerConditionValue((string?)node.Attribute("Value"), line),
            };
            foreach (var setter in node.Elements())
            {
                if (setter.Name.LocalName == "DataTrigger.Binding") continue;
                if (setter.Name.LocalName == "Setter")
                {
                    trigger.Setters.Add(ReadTriggerSetter(setter, line, targetType, styleResources));
                    continue;
                }
                if (TryReadTriggerActions(setter, "DataTrigger", trigger, line, resolveResource)) continue;
                throw Fail(line, $"<{setter.Name.LocalName}> is not valid in <DataTrigger> (Setter and DataTrigger.EnterActions/ExitActions only).");
            }
            return trigger;
        }
        if (local == "MultiDataTrigger")
            return ReadMultiDataTrigger(node, targetType, line, resolveResource, styleResources);
        if (local == "EventTrigger")
            return ReadTemplateTrigger(node, targetType, line, null, TriggerScopeResolver(null));
        throw Fail(line, "This trigger collection supports Trigger/MultiTrigger/DataTrigger/MultiDataTrigger/EventTrigger entries.");
    }

    private static MultiTrigger ReadMultiTrigger(XElement node, Type? targetType, IXmlLineInfo line, Func<string, object?>? resolveResource = null, ResourceDictionary? styleResources = null)
    {
        var trigger = new MultiTrigger();
        foreach (var child in node.Elements())
        {
            if (IsWrapper(child, "MultiTrigger", "Conditions") || child.Name.LocalName == "Condition")
            {
                var conditions = IsWrapper(child, "MultiTrigger", "Conditions")
                    ? child.Elements()
                    : new[] { child };
                foreach (var condition in conditions)
                {
                    if (condition.Name.LocalName != "Condition")
                        throw Fail(line, "This MultiTrigger supports Condition entries only.");
                    var property = CanonicalizeTriggerProperty((string?)condition.Attribute("Property")
                        ?? throw Fail(line, "Condition needs Property=."), line, prefix => condition.GetNamespaceOfPrefix(prefix)?.NamespaceName);
                    var conditionSource = (string?)condition.Attribute("SourceName");
                    trigger.Conditions.Add(new Condition
                    {
                        Property = property,
                        SourceName = conditionSource,
                        Value = ConvertTriggerValue((string?)condition.Attribute("Property") ?? property, (string?)condition.Attribute("Value"), targetType, line, conditionSource is not null, prefix => condition.GetNamespaceOfPrefix(prefix)?.NamespaceName),
                    });
                }
                continue;
            }
            if (child.Name.LocalName == "Setter")
            {
                trigger.Setters.Add(ReadTriggerSetter(child, line, targetType, styleResources));
                continue;
            }
            if (IsWrapper(child, "MultiTrigger", "Setters"))
            {
                foreach (var setter in child.Elements())
                {
                    if (setter.Name.LocalName != "Setter")
                        throw Fail(line, "This MultiTrigger.Setters supports Setter entries only.");
                    trigger.Setters.Add(ReadTriggerSetter(setter, line, targetType, styleResources));
                }
                continue;
            }
            if (TryReadTriggerActions(child, "MultiTrigger", trigger, line, resolveResource)) continue;
            throw Fail(line, $"This MultiTrigger supports Conditions/Setter/MultiTrigger.Setters/EnterActions/ExitActions only (got <{child.Name.LocalName}>).");
        }
        if (trigger.Conditions.Count == 0)
            throw Fail(line, "MultiTrigger needs at least one Condition.");
        return trigger;
    }

    private static MultiDataTrigger ReadMultiDataTrigger(XElement node, Type? targetType, IXmlLineInfo line, Func<string, object?>? resolveResource = null, ResourceDictionary? styleResources = null)
    {
        var trigger = new MultiDataTrigger();
        foreach (var child in node.Elements())
        {
            if (IsWrapper(child, "MultiDataTrigger", "Conditions") || child.Name.LocalName == "Condition")
            {
                var conditions = IsWrapper(child, "MultiDataTrigger", "Conditions")
                    ? child.Elements()
                    : new[] { child };
                foreach (var condition in conditions)
                {
                    if (condition.Name.LocalName != "Condition")
                        throw Fail(line, "This MultiDataTrigger supports Condition entries only.");
                    trigger.Conditions.Add(new Condition
                    {
                        Binding = ReadTriggerBinding(condition, line),
                        Value = ReadTriggerConditionValue((string?)condition.Attribute("Value"), line),
                    });
                }
                continue;
            }
            if (child.Name.LocalName is "Setter")
            {
                trigger.Setters.Add(ReadTriggerSetter(child, line, targetType, styleResources));
                continue;
            }
            if (IsWrapper(child, "MultiDataTrigger", "Setters"))
            {
                foreach (var nested in child.Elements())
                {
                    if (nested.Name.LocalName != "Setter")
                        throw Fail(line, $"This MultiDataTrigger supports Setter only (got <{nested.Name.LocalName}>).");
                    trigger.Setters.Add(ReadTriggerSetter(nested, line, targetType, styleResources));
                }
                continue;
            }
            if (TryReadTriggerActions(child, "MultiDataTrigger", trigger, line, resolveResource)) continue;
            throw Fail(line, $"This MultiDataTrigger supports Conditions/Setters/EnterActions/ExitActions only (got <{child.Name.LocalName}>).");
        }
        if (trigger.Conditions.Count == 0)
            throw Fail(line, "MultiDataTrigger needs at least one Condition.");
        return trigger;
    }

    /// <summary>Reads the Binding= of a DataTrigger/Condition (attribute form, nested options intact).</summary>
    private static BindingBase ReadTriggerBinding(XElement node, IXmlLineInfo line)
    {
        var raw = (string?)node.Attribute("Binding");
        if (raw is null)
        {
            // Property-element form (<DataTrigger.Binding><Binding .../></DataTrigger.Binding> or
            // <MultiBinding .../>, like the MaterialDesign TextBox filled-state conditions): the nested
            // binding element parses through the shared template child readers.
            var wrapper = node.Elements().FirstOrDefault(e => e.Name.LocalName == node.Name.LocalName + ".Binding");
            var bindingNode = wrapper?.Elements().FirstOrDefault(e => e.Name.LocalName is "Binding" or "MultiBinding" or "PriorityBinding");
            if (bindingNode is null)
                throw Fail(line, $"DataTrigger/Condition needs Binding= (got <{node.Name.LocalName}> with {string.Join(",", node.Attributes().Select(a => a.Name.LocalName))} and [{string.Join(",", node.Elements().Select(e => e.Name.LocalName + "(" + string.Join(",", e.Elements().Select(g => g.Name.LocalName)) + ")"))}]).");
            if (wrapper!.Elements().Skip(1).Any())
                throw Fail(line, $"<{node.Name.LocalName}.Binding> holds one <Binding .../> (multiple bindings stay a precise boundary).");
            if (bindingNode.Name.LocalName == "PriorityBinding")
                return ReadTriggerPriorityBinding(bindingNode, line);
            if (bindingNode.Name.LocalName == "MultiBinding")
            {
                // Nested <Binding> children are the standard composite form
                // (theme slider conditions); the shared reader validates them.
                return ReadTemplateMultiBinding(bindingNode, line);
            }
            if (bindingNode.Elements().Any())
                throw Fail(line, $"<{bindingNode.Name.LocalName} .../> takes attributes only (got [{string.Join(",", bindingNode.Attributes().Select(a => a.Name.LocalName))}] and [{string.Join(",", bindingNode.Elements().Select(e => e.Name.LocalName))}]; nested binding content stays a precise boundary).");
            return ReadTemplateMultiChild(bindingNode, line);
        }
        var text = raw.Trim();
        if (!text.StartsWith('{') || !text.EndsWith('}'))
            throw Fail(line, $"DataTrigger Binding '{raw}' needs markup ({{Binding ...}}), like WPF.");
        var inner = text[1..^1].Trim();
        if (!inner.StartsWith("Binding", StringComparison.Ordinal))
            throw Fail(line, $"DataTrigger Binding '{raw}' needs {{Binding ...}} markup.");
        var options = BindingOptions(inner["Binding".Length..]);
        var relative = ExtractNestedOption(options, "RelativeSource");
        var converter = ExtractNestedOption(options, "Converter");
        var parameter = ExtractNestedOption(options, "ConverterParameter");
        var binding = ParseBinding(string.Join(',', options), raw, line);
        if (relative is not null) binding.RelativeSource = ParseRelativeSource(relative, line);
        if (converter is not null) ApplyBindingConverter(binding, converter, line);
        if (parameter is not null)
        {
            // Literal or {x:Static ...} converter parameters (theme opacity
            // scaling); other markup stays a precise error, like transforms.
            var parameterText = parameter.Trim();
            binding.ConverterParameter = parameterText.StartsWith("{x:Static ", StringComparison.Ordinal) && parameterText.EndsWith('}')
                ? ResolveStaticMember(parameterText["{x:Static ".Length..^1].Trim(), raw, line)
                : !parameterText.StartsWith('{')
                    ? parameter
                    : throw Fail(line, $"Trigger value '{raw}' takes a literal or {{x:Static ...}} ConverterParameter.");
        }
        return binding;
    }

    /// <summary>Reads a trigger-condition PriorityBinding (MaterialDesign
    /// character-counter DataTrigger): Binding children in priority order
    /// (first viable value wins); FallbackValue rides the composite like
    /// WPF. Nested composites and markup sources stay precise, mirroring
    /// the template child reader.</summary>
    private static PriorityBinding ReadTriggerPriorityBinding(XElement node, IXmlLineInfo line)
    {
        var priority = new PriorityBinding();
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (attribute.Name.NamespaceName == XamlNamespace) continue;
            var raw = attribute.Value.Trim();
            if (string.Equals(attribute.Name.LocalName, "FallbackValue", StringComparison.Ordinal))
            {
                priority.FallbackValue = ParseNestedFallback(raw, raw, line);
                continue;
            }
            throw Fail(line, $"PriorityBinding option '{attribute.Name.LocalName}' is not supported (Bindings/FallbackValue).");
        }
        foreach (var child in node.Elements())
        {
            if (child.Name.LocalName != "Binding")
                throw Fail(line, $"PriorityBinding takes <Binding> children (got <{child.Name.LocalName}>; nested composites stay a precise boundary).");
            if (child.Elements().Any())
                throw Fail(line, $"<Binding .../> takes attributes only (got [{string.Join(",", child.Attributes().Select(a => a.Name.LocalName))}] and [{string.Join(",", child.Elements().Select(e => e.Name.LocalName))}]; nested binding content stays a precise boundary).");
            priority.Bindings.Add(ReadTemplateMultiChild(child, line));
        }
        if (priority.Bindings.Count == 0)
            throw Fail(line, "PriorityBinding needs at least one <Binding> child.");
        return priority;
    }

    /// <summary>Applies one literal behavior attribute through the converted
    /// CLR property (shared by template and style behavior readers).</summary>
    private static void SetBehaviorLiteralAttribute(Type behaviorType, object behavior, string local, string raw, IXmlLineInfo line, string expectation)
    {
        var clr = behaviorType.GetProperty(local, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (clr is null || !clr.CanWrite || clr.SetMethod?.IsPublic != true)
            throw Fail(line, $"'{local}=' is not a settable property of <{behaviorType.Name}> ({expectation}).");
        object? literal;
        try
        {
            literal = StyleValueConverter.Convert(raw, clr.PropertyType)
                ?? throw new FormatException($"'{raw}' does not convert to {clr.PropertyType.Name}.");
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            throw Fail(line, ex.Message);
        }
        clr.SetValue(behavior, literal);
    }

    /// <summary>Reads a vendor behavior-collection style setter
    /// (MaterialDesign BehaviorsAssist.Behaviors): the converted attached DP
    /// carries the parsed collection, and the converted property-changed
    /// path drives the attach per styled element, like WPF.</summary>
    private static Setter ReadBehaviorsAssistSetter(XElement setter, XElement collectionNode, IXmlLineInfo line)
    {
        const string original = "<BehaviorsAssist.Behaviors>";
        var property = (string?)setter.Attribute("Property") ?? throw Fail(line, "Setter needs Property.");
        var dot = property.IndexOf('.');
        if (dot <= 0)
            throw Fail(line, $"'{property}' takes an owner-qualified behavior collection (Vendor.Behaviors).");
        var owner = ResolveConvertedTypeRef(property[..dot], line, prefix => setter.GetNamespaceOfPrefix(prefix)?.NamespaceName, property);
        var member = property[(dot + 1)..];
        if (!string.Equals(member, "Behaviors", StringComparison.Ordinal))
            throw Fail(line, $"'{property}' is not a behavior collection setter ({original}).");
        var dp = DependencyProperty.FromName(owner, member)
            ?? throw Fail(line, $"'{property}' names no dependency property on <{owner.Name}>.");
        if (!string.Equals(collectionNode.Name.LocalName, dp.PropertyType.Name, StringComparison.Ordinal))
            throw Fail(line, $"'{original}' holds <{collectionNode.Name.LocalName}>, not <{dp.PropertyType.Name}>.");
        if (!HasDefaultCtor(dp.PropertyType))
            throw Fail(line, $"'{original}' needs a default constructor on <{dp.PropertyType.FullName}> (converted shape changed).");
        object collection;
        try
        {
            collection = Activator.CreateInstance(dp.PropertyType, nonPublic: true)!;
        }
        catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or MemberAccessException)
        {
            throw Fail(line, $"'{original}' cannot construct <{collectionNode.Name.LocalName}> ({(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}).");
        }
        if (collection is not System.Collections.IList items)
            throw Fail(line, $"'{original}' needs a list collection (converted shape changed).");
        foreach (var behaviorNode in collectionNode.Elements())
        {
            var behaviorType = CheckTemplateBehaviorType(
                ResolveTemplateBehaviorType(behaviorNode, line, original),
                behaviorNode.Name.LocalName, line, original);
            object behavior;
            try
            {
                behavior = Activator.CreateInstance(behaviorType, nonPublic: true)!;
            }
            catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or MemberAccessException)
            {
                throw Fail(line, $"'{original}' cannot construct <{behaviorNode.Name.LocalName}> ({(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}).");
            }
            foreach (var attribute in behaviorNode.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                if (IsIgnorableAttribute(attribute)) continue;
                if (attribute.Name.NamespaceName == XamlNamespace)
                    throw Fail(line, $"'{attribute.Name.LocalName}=' is not supported on <{behaviorNode.Name.LocalName}> (style behavior attributes take literals; bindings stay a precise boundary).");
                var local = attribute.Name.LocalName;
                if (local.Contains('.'))
                    throw Fail(line, $"'{local}=' is not supported on <{behaviorNode.Name.LocalName}> (attached behavior attributes stay a precise boundary).");
                var raw = attribute.Value.Trim();
                if (raw.StartsWith('{'))
                    throw Fail(line, $"'{raw}' is not supported on style behavior '{local}' (literals only; bindings stay a precise boundary).");
                SetBehaviorLiteralAttribute(behaviorType, behavior, local, raw, line, "style behavior attributes take literals");
            }
            if (behaviorNode.Elements().Any())
                throw Fail(line, $"<{behaviorNode.Name.LocalName}> takes attributes only (nested behavior content stays a precise boundary).");
            try
            {
                items.Add(behavior);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                throw Fail(line, $"'{original}' cannot hold <{behaviorType.Name}> ({ex.GetType().Name}: {ex.Message}).");
            }
        }
        if (items.Count == 0)
            throw Fail(line, $"'{original}' needs at least one behavior.");
        return new Setter(dp, collection);
    }


    /// <summary>Reads a binding-condition value (literals stay raw for source-typed comparison; {x:Static} resolves eagerly).</summary>
    private static object? ReadTriggerConditionValue(string? raw, IXmlLineInfo line)
    {
        if (raw is null) return null;
        var text = raw.Trim();
        if (text == "{x:Null}") return null;
        if (text.StartsWith("{x:Static ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            var staticMember = text["{x:Static ".Length..^1].Trim();
            return ResolveStaticMember(staticMember, line);
        }
        if (text.StartsWith("{DynamicResource ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            var key = text["{DynamicResource ".Length..^1].Trim();
            if (key.Length == 0 || key.Contains('='))
                throw Fail(line, $"DataTrigger Value '{raw}' takes a plain key ({{DynamicResource Key}}).");
            if (key.Contains('{'))
            {
                if (!key.StartsWith("{x:Static ", StringComparison.Ordinal) || !key.EndsWith('}'))
                    throw Fail(line, $"DataTrigger Value '{raw}' takes a plain key ({{DynamicResource Key}}).");
                var nested = ResolveStaticMember(key[1..^1]["x:Static".Length..].Trim(), raw, line);
                if (nested is not ResourceKey staticKey)
                    throw Fail(line, $"DataTrigger Value '{raw}' needs a resource key (x:Static must name a ResourceKey).");
                var staticName = staticKey.ToString();
                if (string.IsNullOrEmpty(staticName))
                    throw Fail(line, $"DataTrigger Value '{raw}' needs a resource key (x:Static must name a ResourceKey).");
                key = staticName;
            }
            return new TriggerResourceReference(key, IsDynamic: true);
        }
        if (text.StartsWith("{StaticResource ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            var key = text["{StaticResource ".Length..^1].Trim();
            if (key.Length == 0 || key.Contains('='))
                throw Fail(line, $"DataTrigger Value '{raw}' takes a plain key ({{StaticResource Key}}).");
            if (key.Contains('{'))
            {
                if (!key.StartsWith("{x:Static ", StringComparison.Ordinal) || !key.EndsWith('}'))
                    throw Fail(line, $"DataTrigger Value '{raw}' takes a plain key ({{StaticResource Key}}).");
                var nested = ResolveStaticMember(key[1..^1]["x:Static".Length..].Trim(), raw, line);
                if (nested is not ResourceKey staticKey)
                    throw Fail(line, $"DataTrigger Value '{raw}' needs a resource key (x:Static must name a ResourceKey).");
                var staticName = staticKey.ToString();
                if (string.IsNullOrEmpty(staticName))
                    throw Fail(line, $"DataTrigger Value '{raw}' needs a resource key (x:Static must name a ResourceKey).");
                key = staticName;
            }
            return new TriggerResourceReference(key, IsDynamic: false);
        }
        return text;
    }

    private static int ParseIntValue(string property, string value, IXmlLineInfo line)
    {
        try { return int.Parse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentNullException)
        {
            throw Fail(line, $"'{value}' is not an integer for '{property}'.");
        }
    }

    private static double ParseDoubleValue(string property, string value, IXmlLineInfo line)
    {
        try { return double.Parse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentNullException)
        {
            throw Fail(line, $"'{value}' is not a number for '{property}'.");
        }
    }

    private static KeyboardNavigationMode ParseKeyboardNavigationModeValue(string property, string value, IXmlLineInfo line)
    {
        try { return KeyboardNavigation.ParseMode(value); }
        catch (FormatException ex) { throw Fail(line, $"'{value}' is not a KeyboardNavigationMode for '{property}'."); }
    }

    private static bool ParseBoolValue(string property, string value, IXmlLineInfo line)
    {
        if (bool.TryParse(value, out var parsed)) return parsed;
        throw Fail(line, $"'{value}' is not a boolean for '{property}' (True/False).");
    }

    /// <summary>Applies an explicit Style resource to a loaded element, collecting event wires.</summary>
    internal static void ApplyStyleResource(FrameworkElement target, Style style, IXmlLineInfo line, List<XamlEventWire> wires, IReadOnlyList<FrameworkElement>? scope = null)
    {
        var context = new StyleApplyContext { StringHandlers = new(), Scope = scope };
        try
        {
            style.ApplyTo(target, context);
        }
        catch (InvalidOperationException ex) { throw Fail(line, ex.Message); }
        catch (FormatException ex) { throw Fail(line, ex.Message); }
        foreach (var (name, handler) in context.StringHandlers)
            wires.Add(new XamlEventWire(target, name, handler));
    }

    /// <summary>Resolves a resource-reference attribute value ({StaticResource Key}
    /// or {DynamicResource Key}) on CLR/engine resource entries against the same
    /// scopes the streaming entry reader uses (hydration scopes innermost-first,
    /// then Application scope — the dictionary being built rides the hydration
    /// stack). SolidColorBrush values land on Color properties via .Color.
    /// Returns false when the value is not a resource reference.</summary>
    /// <summary>Presentation-namespace entry kinds with a dedicated streaming
    /// resource reader (brushes, geometries, effects, easings, timelines,
    /// images, colors). The generic CLR branch declines these so resource
    /// references and nested content resolve on the dedicated path.</summary>
    private static bool HasStreamingResourceReader(string localName) => localName is
        "DrawingBrush" or "GradientStopCollection" or "LinearGradientBrush" or "RadialGradientBrush" or
        "LineGeometry" or "RectangleGeometry" or "EllipseGeometry" or "GeometryGroup" or
        "PathGeometry" or "CombinedGeometry" or "Geometry" or "DropShadowEffect" or
        "ImageBrush" or "SineEase" or "CubicEase" or "PowerEase" or "Storyboard" or
        "DoubleAnimation" or "DoubleAnimationUsingKeyFrames" or "ObjectAnimationUsingKeyFrames" or
        "SolidColorBrush" or "Color" or "BitmapImage";

    private static bool TryResolveClrResourceReference(string raw, Type targetType, IXmlLineInfo line, out object? value)
    {
        value = null;
        var text = raw.Trim();
        if (!text.StartsWith('{') || !text.EndsWith('}')) return false;
        var inner = text[1..^1].Trim();
        bool isStatic;
        if (inner.StartsWith("StaticResource ", StringComparison.Ordinal)) isStatic = true;
        else if (inner.StartsWith("DynamicResource ", StringComparison.Ordinal)) isStatic = false;
        else return false;
        var key = inner[(isStatic ? "StaticResource ".Length : "DynamicResource ".Length)..].Trim();
        if (key.Length == 0) return false;
        object? resource = null;
        var found = TryFindHydrationResource(key, out resource) && resource is not null;
        if (!found && Application.Current?.Resources.TryGetValue(key, out resource) == true && resource is not null)
            found = true;
        if (!found)
        {
            if (isStatic) throw Fail(line, $"StaticResource '{key}' was not found.");
            if (targetType.IsValueType) throw Fail(line, $"DynamicResource '{key}' was not found.");
            value = null;
            return true;
        }
        if (targetType.IsInstanceOfType(resource)) { value = resource; return true; }
        if (targetType == typeof(Color) && resource is SolidColorBrush solid) { value = solid.Color; return true; }
        return false;
    }

    /// <summary>Tries an {x:Static ...} value against a CLR resource property
    /// (theme converter knobs like BooleanToDashStyleConverter.TrueValue):
    /// engine and converted statics resolve through the shared member path;
    /// mistyped statics fall through to the converter error below.</summary>
    private static bool TryResolveClrStaticMember(string raw, Type targetType, IXmlLineInfo line, out object? value)
    {
        value = null;
        var text = raw.Trim();
        if (!text.StartsWith("{x:Static ", StringComparison.Ordinal) || !text.EndsWith('}')) return false;
        var resolved = ResolveStaticMember(text["{x:Static".Length..^1].Trim(), raw, line);
        if (resolved is null || !targetType.IsInstanceOfType(resolved)) return false;
        value = resolved;
        return true;
    }

    /// <summary>Resolves an implicit data-template key (DataType={x:Type T})
    /// to the data type, like WPF's DataTemplateKey.</summary>
    private static Type ResolveImplicitTemplateKey(XmlReader reader, string raw, IXmlLineInfo line)
    {
        var text = raw.Trim();
        if (!text.StartsWith("{x:Type ", StringComparison.Ordinal) || !text.EndsWith('}'))
            throw Fail(line, $"DataType '{raw}' needs {{x:Type Name}} (implicit data templates).");
        var clean = text["{x:Type ".Length..^1].Trim();
        Type? type;
        if (clean.Contains(':'))
            type = ResolveConvertedTypeRef(clean, line, prefix => reader.LookupNamespace(prefix), raw);
        else
            type = FindEngineType(clean) ?? FindConvertedType(clean);
        return type ?? throw Fail(line, $"DataType '{{x:Type {clean}}}' names no loaded type.");
    }

    private static bool TryReadApplicationResource(XmlReader reader, IXmlLineInfo line, ResourceDictionary dictionary)
    {
        if (reader.LocalName == "Style")
        {
            dictionary.Add(ReadStyleKey(reader, line), ReadStyleElement(reader, line));
            return true;
        }
        if (reader.LocalName == "ContextMenu")
        {
            var menuKey = reader.GetAttribute("Key", XamlNamespace)
                ?? throw Fail(line, "<ContextMenu> resource entry needs x:Key.");
            var menuNode = XElement.Parse(HoistTemplateNamespaces(reader.ReadOuterXml(), reader, line));
            dictionary.Add(menuKey, ReadContextMenuElement(menuNode, line));
            return true;
        }
        if (reader.LocalName is "ItemsPanelTemplate" or "DataTemplate" or "HierarchicalDataTemplate" or "ControlTemplate")
        {
            var kind = reader.LocalName;
                var keyRaw = reader.GetAttribute("Key", XamlNamespace);
                // Implicit data templates (the validation error viewer): no x:Key,
                // keyed by the resolved data type like WPF's DataTemplateKey.
                if (keyRaw is null && kind is "DataTemplate" or "HierarchicalDataTemplate" &&
                    reader.GetAttribute("DataType") is { } implicitType)
                {
                    dictionary.Add(ResolveImplicitTemplateKey(reader, implicitType, line),
                        ReadDataTemplateElement(reader, line));
                    return true;
                }
                var keyRawChecked = keyRaw
                    ?? throw Fail(line, $"<{kind}> resource entry needs x:Key.");
                // Component-keyed templates (CalendarItem.DayTitleTemplateResourceKey):
                // the key resolves to the shared static instance, like WPF.
                object key = keyRawChecked;
            if (keyRawChecked.StartsWith("{x:Static ", StringComparison.Ordinal) && keyRaw.EndsWith('}'))
                key = ResolveStaticMember(keyRawChecked[1..^1]["x:Static".Length..].Trim(), keyRawChecked, line)
                    ?? throw Fail(line, $"<{kind}> key '{keyRawChecked}' resolved to null.");
            if (kind == "ControlTemplate")
            {
                // Keyed templates nested in template resources (the Calendar
                // Previous/Next/Header buttons) load like top-level entries.
                var templateNode = XElement.Parse(HoistTemplateNamespaces(reader.ReadOuterXml(), reader, line));
                Type? templateTarget = null;
                var declaredTarget = (string?)templateNode.Attribute("TargetType");
                if (declaredTarget is not null)
                {
                    var cleanTarget = declaredTarget.Trim();
                    if (cleanTarget.StartsWith("{x:Type ", StringComparison.Ordinal) && cleanTarget.EndsWith('}')) cleanTarget = cleanTarget[8..^1].Trim();
                    templateTarget = cleanTarget.Contains(':')
                        ? ResolveConvertedTypeRef(cleanTarget, line, prefix => templateNode.GetNamespaceOfPrefix(prefix)?.NamespaceName, declaredTarget)
                        : (FindEngineType(cleanTarget) ?? FindConvertedType(cleanTarget))
                        ?? throw Fail(line, $"<ControlTemplate> targets unknown type '{cleanTarget}'.");
                }
                dictionary.Add(key, ReadControlTemplate(templateNode, templateTarget, line));
                return true;
            }
            dictionary.Add(key, kind == "ItemsPanelTemplate"
                ? ReadItemsPanelTemplateElement(reader, line)
                : ReadDataTemplateElement(reader, line));
            return true;
        }
        Type? typeInfo;
        string typeName;
        if (reader.NamespaceURI.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            var ns = reader.NamespaceURI["clr-namespace:".Length..].Split(';');
            typeName = ns[0] + "." + reader.LocalName;
            typeInfo = ResolveClrResourceType(ns, typeName)
                ?? throw Fail(line, $"CLR resource '{typeName}' does not exist in the converted, engine or runtime assemblies.");
        }
        else if (XmlNamespacesFor(reader.NamespaceURI) is { } vendorNamespaces)
        {
            // Vendor map-URI resource entries (materialDesign:MathConverter
            // in app resources): XmlnsDefinition mappings resolve through
            // the carried tables, then the shared CLR construction below
            // runs, like the element path.
            typeInfo = ResolveVendorResourceType(vendorNamespaces, reader.LocalName, out typeName)
                ?? throw Fail(line, $"CLR resource '{reader.LocalName}' does not exist in the converted assemblies for '{reader.NamespaceURI}'.");
        }
        else if (reader.NamespaceURI == PresentationNamespace && FindEngineType(reader.LocalName) is { } engineType)
        {
            // Entries with a dedicated streaming reader stay on that path:
            // it resolves resource references and nested content (brushes,
            // effects, easings, timelines), which the generic CLR branch cannot.
            if (HasStreamingResourceReader(reader.LocalName)) return false;
            // Engine-namespace resource entries (theme easing functions):
            // same construction rules, resolved in the engine assembly.
            typeInfo = engineType;
            typeName = engineType.FullName ?? reader.LocalName;
        }
        else return false;
        var resource = XElement.Parse(HoistTemplateNamespaces(reader.ReadOuterXml(), reader, line));
        var resourceKey = (string?)resource.Attribute(XName.Get("Key", XamlNamespace)) ?? throw Fail(line, "CLR resource needs x:Key.");
        // Instance CLR resources (converters, thicknesses): attributes and
        // property elements apply below; text-only values convert directly.
        object instance;
        if (resource.HasElements)
        {
            var propInstance = Activator.CreateInstance(typeInfo, nonPublic: true) ?? throw Fail(line, $"Cannot create resource '{typeName}'.");
            // Property-element CLR resources (ListView template-selector
            // converter with ControlTemplate values): each Owner.Property
            // wrapper carries one value element or reference text, applied
            // before attributes like WPF.
            if (!string.IsNullOrWhiteSpace(resource.Value))
                throw Fail(line, $"<{resource.Name.LocalName}> mixes text with property elements.");
            foreach (var wrapper in resource.Elements())
            {
                var wrapperName = wrapper.Name.LocalName;
                var dot = wrapperName.IndexOf('.');
                if (dot <= 0)
                    throw Fail(line, $"<{wrapperName}> is not <{resource.Name.LocalName}.Property>.");
                var wrapperOwner = wrapperName[..dot];
                var ownerShort = wrapperOwner.Contains(':')
                    ? wrapperOwner[(wrapperOwner.IndexOf(':') + 1)..]
                    : wrapperOwner;
                if (!string.Equals(ownerShort, typeInfo.Name, StringComparison.Ordinal))
                    throw Fail(line, $"<{wrapperName}> does not belong to <{typeName}>.");
                var wrapperProp = wrapperName[(dot + 1)..];
                var elementProperty = typeInfo.GetProperty(wrapperProp)
                    ?? throw Fail(line, $"Unknown CLR resource property '{wrapperProp}'.");
                if (elementProperty.SetMethod?.IsPublic != true)
                    throw Fail(line, $"CLR resource property '{elementProperty.Name}' is not writable.");
                object? elementValue;
                var valueKids = wrapper.Elements().ToList();
                if (valueKids.Count == 1 && valueKids[0].Name.LocalName == "ControlTemplate")
                    elementValue = ReadControlTemplate(valueKids[0], null, line);
                else if (valueKids.Count == 0)
                {
                    var wrapperText = wrapper.Value.Trim();
                    if (TryResolveClrResourceReference(wrapperText, elementProperty.PropertyType, line, out var referenced))
                        elementValue = referenced;
                    else if (TryResolveClrStaticMember(wrapperText, elementProperty.PropertyType, line, out var staticMember))
                        elementValue = staticMember;
                    else try { elementValue = BindingExpression.ConvertValue(wrapperText, elementProperty.PropertyType); }
                    catch (Exception ex) when (ex is not XamlLoadException &&
                        ex is (InvalidCastException or FormatException or OverflowException or ArgumentException))
                    {
                        throw Fail(line, $"'{wrapperText}' does not convert to {elementProperty.PropertyType.Name} for resource '{typeName}' property '{elementProperty.Name}'.");
                    }
                }
                else throw Fail(line, $"<{wrapperName}> holds one ControlTemplate or reference text (got {valueKids.Count} elements).");
                elementProperty.SetValue(propInstance, elementValue);
            }
            instance = propInstance;
        }
        else if (!string.IsNullOrWhiteSpace(resource.Value))
        {
            // Value-type resources (the gallery's sys:Double theme sizes):
            // convert the text through a Parse(string) method or ChangeType.
            dictionary.Add(resourceKey, ConvertClrResourceText(typeInfo, typeName, resource.Value.Trim(), line));
            return true;
        }
        else instance = Activator.CreateInstance(typeInfo, nonPublic: true) ?? throw Fail(line, $"Cannot create resource '{typeName}'.");
        var freeze = false;
        foreach (var attribute in resource.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.NamespaceName != XamlNamespace))
        {
            // Designer freeze hint (po:Freeze on theme aliases): honored below,
            // like the BAML loader's PresentationOptions freeze markers.
            if (attribute.Name.NamespaceName.EndsWith("/presentation/options", StringComparison.Ordinal))
            {
                if (attribute.Name.LocalName == "Freeze" && bool.TryParse(attribute.Value, out var freezeParsed))
                {
                    freeze = freezeParsed;
                    continue;
                }
                continue;
            }
            var property = typeInfo.GetProperty(attribute.Name.LocalName) ?? throw Fail(line, $"Unknown CLR resource property '{attribute.Name.LocalName}'.");
            if (property.SetMethod?.IsPublic != true) throw Fail(line, $"CLR resource property '{property.Name}' is not writable.");
            object? converted;
            if (TryResolveClrResourceReference(attribute.Value, property.PropertyType, line, out var referenced))
                converted = referenced;
            else if (TryResolveClrStaticMember(attribute.Value, property.PropertyType, line, out var staticMember))
                converted = staticMember;
            else if (instance is FrameworkElement bindingHost && attribute.Value.TrimStart().StartsWith("{Binding", StringComparison.Ordinal))
            {
                var live = ParseStandaloneBinding(attribute.Value, property.Name, line, "resource");
                var bound = DependencyProperty.FromName(typeInfo, property.Name);
                if (bound is not null) BindingOperations.SetBinding(bindingHost, bound, live);
                else bindingHost.SetBinding(property.Name, live);
                continue;
            }
            else try { converted = BindingExpression.ConvertValue(attribute.Value, property.PropertyType); }
            catch (Exception ex) when (ex is not XamlLoadException &&
                ex is (InvalidCastException or FormatException or OverflowException or ArgumentException))
            {
                throw Fail(line, $"'{attribute.Value}' does not convert to {property.PropertyType.Name} for resource '{typeName}' property '{attribute.Name.LocalName}'.");
            }
            property.SetValue(instance, converted);
        }
        object resourceValue = instance;
        if (instance is MarkupExtension extension)
        {
            // Markup-extension entries (theme StaticResource aliases) resolve
            // now, like WPF load-time StaticResource: the dictionary being
            // built plus Application scope back the lookup.
            try { resourceValue = extension.ProvideValue(new XamlExtensionService(dictionary, null)) ?? throw new InvalidOperationException($"CLR resource '{typeName}' produced null."); }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                throw Fail(line, ex.Message);
            }
        }
        if (freeze && resourceValue is Freezable freezable && !freezable.IsFrozen) freezable.Freeze();
        dictionary.Add(resourceKey, resourceValue);
        return true;
    }

    /// <summary>
    /// Resolves a CLR resource type across the declared assembly (e.g.
    /// mscorlib for sys:Double), the converted application assembly, the
    /// engine assembly (theme converters) and the core library.
    /// </summary>
    /// <summary>Resolves a vendor map-URI resource type through the carried
    /// xmlns tables (first namespace wins, like the element path).</summary>
    private static Type? ResolveVendorResourceType(string[] namespaces, string localName, out string fullName)
    {
        foreach (var ns in namespaces)
        {
            fullName = ns + "." + localName;
            foreach (var assembly in ConvertedSearchAssemblies())
            {
                if (SafeGetConvertedType(assembly, fullName) is { } found) return found;
            }
        }
        fullName = namespaces.Length > 0 ? namespaces[0] + "." + localName : localName;
        return null;
    }

    private static Type? ResolveClrResourceType(string[] ns, string typeName)
    {
        var declared = ns.Skip(1).FirstOrDefault(p => p.StartsWith("assembly=", StringComparison.Ordinal));
        if (declared is not null)
        {
            try
            {
                var loaded = Assembly.Load(new AssemblyName(declared[9..]));
                if (loaded.GetType(typeName) is { } found) return found;
            }
            catch
            {
                // Unknown declared assemblies fall through to the app/engine/runtime below.
            }
        }
        foreach (var assembly in new[] { CustomElementSource, typeof(Window).Assembly, typeof(object).Assembly })
        {
            if (assembly is null) continue;
            try
            {
                if (assembly.GetType(typeName) is { } found) return found;
            }
            catch
            {
                // Unreadable assemblies are skipped; the caller reports absence.
            }
        }
        // Staged vendor images beyond the entry (MaterialDesignColors holds the
        // theme StaticResource extension): every loaded converted assembly.
        foreach (var assembly in ConvertedSearchAssemblies())
        {
            try
            {
                if (assembly.GetType(typeName) is { } found) return found;
            }
            catch
            {
                // Unreadable assemblies are skipped; the caller reports absence.
            }
        }
        // XAML Extension-suffix convention (<colors:StaticResource /> names
        // MaterialDesignColors.StaticResourceExtension): plain names win
        // everywhere above, so an accidental FooExtension never shadows Foo.
        var extensionName = typeName + "Extension";
        foreach (var assembly in ConvertedSearchAssemblies())
        {
            Type? candidate;
            try
            {
                candidate = assembly.GetType(extensionName);
            }
            catch
            {
                continue;
            }
            if (candidate is not null && typeof(MarkupExtension).IsAssignableFrom(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Converts CLR resource text (sys:Double sizes, enum names) to the target type.</summary>
    private static object ConvertClrResourceText(Type typeInfo, string typeName, string text, IXmlLineInfo line)
    {
        if (typeInfo == typeof(KeyTime))
        {
            // KeyTime resource entries (Expander animation keys): same
            // grammar as storyboard frames (Uniform/Paced/percent/TimeSpan).
            try { return KeyTime.ParseKeyTimeText(text); }
            catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
            { throw Fail(line, $"'{text}' is not a {typeName} ({ex.Message})."); }
        }
        if (typeInfo.IsEnum)
        {
            // Enum text entries (PackIconKind icon keys): member names parse
            // case-insensitively, like the other engine enum readers.
            try { return Enum.Parse(typeInfo, text.Trim(), ignoreCase: true); }
            catch (Exception ex) when (ex is ArgumentException or OverflowException)
            { throw Fail(line, $"'{text}' is not a {typeName} ({ex.Message})."); }
        }
        var parse = typeInfo.GetMethod("Parse",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            [typeof(string)]);
        if (parse is not null && parse.ReturnType == typeInfo)
        {
            try { return parse.Invoke(null, [text])!; }
            catch (TargetInvocationException ex) { throw Fail(line, $"'{text}' is not a {typeName} ({ex.InnerException?.Message ?? ex.Message})."); }
        }
        try { return System.Convert.ChangeType(text, typeInfo, System.Globalization.CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException or ArgumentException)
        {
            throw Fail(line, $"'{text}' is not a {typeName} (CLR resources take attributes or Parse-able text).");
        }
    }

    /// <summary>Reads a brush resource entry (positioned on its start tag; consumes it).</summary>
    private static void ReadBrushResourceEntry(XmlReader reader, IXmlLineInfo line, ResourceDictionary dictionary)
    {
        var node = XElement.Parse(HoistTemplateNamespaces(reader.ReadOuterXml(), reader, line));
        var key = (string?)node.Attribute(XName.Get("Key", XamlNamespace))
            ?? throw Fail(line, $"<{node.Name.LocalName}> resource entry needs x:Key.");
        object value = node.Name.LocalName switch
        {
            "DrawingBrush" => ReadDrawingBrushNode(node, line, scope: dictionary),
            "LinearGradientBrush" => ReadLinearBrush(node, line, scope: dictionary),
            "RadialGradientBrush" => ReadRadialBrush(node, line, scope: dictionary),
            "GradientStopCollection" => ReadStopCollection(node, line, scope: dictionary),
            _ => throw Fail(line, $"<{node.Name.LocalName}> is not a brush resource entry."),
        };
        try { dictionary.Add(key, value); }
        catch (InvalidOperationException ex) { throw Fail(line, ex.Message); }
    }

    private static GradientStopCollection ReadStopCollection(XElement node, IXmlLineInfo line, ResourceDictionary? scope = null)
    {
        var stops = new GradientStopCollection();
        foreach (var child in node.Elements())
            stops.Add(ReadGradientStop(child, line, scope));
        return stops;
    }

    /// <summary>
    /// Reads a brush/geometry/transform property element (positioned on the
    /// property start tag; consumes through its end tag). True when handled.
    /// </summary>
    private static bool TryReadMediaProperty(
        UIElement element, string property, XmlReader reader, IXmlLineInfo lineInfo, List<PendingStatic> pending)
    {
        var isBrush = element switch
        {
            Rectangle => property is "Fill" or "Stroke" or "OpacityMask",
            Path => property is "Fill" or "Stroke",
            Border => property is "Background" or "OpacityMask",
            Grid or Frame or ScrollViewer => property == "Background",
            _ => false,
        };
        var isGeometry = element switch
        {
            Path => property == "Data",
            Image => property == "Clip",
            _ => false,
        };
        if (!isBrush && !isGeometry) return false;
        if (reader.IsEmptyElement) { reader.Read(); return true; }
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
        {
            var text = reader.Value;
            reader.Read();
            SkipWhitespace(reader);
            if (reader.NodeType != XmlNodeType.EndElement)
                throw Fail(lineInfo, $"Malformed <{element.GetType().Name}.{property}>.");
            reader.Read(); // past the property end tag
            if (isGeometry && !text.TrimStart().StartsWith('{'))
            {
                AssignMediaProperty(element, property, ParseGeometryText(text, lineInfo), lineInfo);
                return true;
            }
            SetPropertyElement(element, property, text, lineInfo, pending);
            return true;
        }
        if (reader.NodeType == XmlNodeType.Element)
        {
            if (isGeometry)
            {
                var geometryNode = XElement.Parse(HoistTemplateNamespaces(reader.ReadOuterXml(), reader, lineInfo));
                AssignMediaProperty(element, property, ReadGeometryNode(geometryNode, lineInfo), lineInfo);
            }
            else
            {
                var brushNode = XElement.Parse(HoistTemplateNamespaces(reader.ReadOuterXml(), reader, lineInfo));
                AssignMediaProperty(element, property, ReadInlineBrush(brushNode, lineInfo), lineInfo);
            }
            SkipWhitespace(reader);
            if (reader.NodeType != XmlNodeType.EndElement)
                throw Fail(lineInfo, $"Malformed <{element.GetType().Name}.{property}>.");
            reader.Read(); // past the property end tag
            return true;
        }
        throw Fail(lineInfo, $"<{element.GetType().Name}.{property}> needs a value or a nested element.");
    }

    private static void AssignMediaProperty(UIElement element, string property, object value, IXmlLineInfo line)
    {
        switch (element, property)
        {
            case (Rectangle rectangle, "Fill"): rectangle.Fill = (Brush)value; return;
            case (Rectangle rectangle, "Stroke"): rectangle.Stroke = (Brush)value; return;
            case (Rectangle rectangle, "OpacityMask"): rectangle.OpacityMask = (Brush)value; return;
            case (Path path, "Data"): path.Data = (Geometry)value; return;
            case (Path path, "Fill"): path.Fill = (Brush)value; return;
            case (Path path, "Stroke"): path.Stroke = (Brush)value; return;
            case (Image image, "Clip"): image.Clip = (Geometry)value; return;
            case (Border border, "Background"): border.Background = (Brush)value; return;
            case (Border maskBorder, "OpacityMask"): maskBorder.OpacityMask = (Brush)value; return;
            case (Grid grid, "Background"): grid.Background = (Brush)value; return;
            case (Frame frame, "Background"): frame.Background = (Brush)value; return;
            case (ScrollViewer scroll, "Background"): scroll.Background = (Brush)value; return;
            default: throw Fail(line, $"<{element.GetType().Name}.{property}> is not a settable media property.");
        }
    }

    /// <summary>Reads an element Triggers property (positioned on the property start tag).</summary>
    private static void ReadElementTriggers(XmlReader reader, IXmlLineInfo line, FrameworkElement target, List<XamlEventWire>? wires = null)
    {
        if (reader.IsEmptyElement)
            throw Fail(line, $"<{target.GetType().Name}.Triggers/> is empty (Trigger/EventTrigger entries are required).");
        reader.Read(); // past the property start tag
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(line, "Unterminated triggers.");
            if (reader.NodeType != XmlNodeType.Element)
                throw Fail(line, "Expected a Trigger/EventTrigger entry inside Triggers.");
            var node = XElement.Parse(HoistTemplateNamespaces(reader.ReadOuterXml(), reader, line));
            var trigger = ReadTemplateTrigger(node, target.GetType(), line, wires, TriggerScopeResolver(null));
            if (trigger is EventTrigger eventTrigger) ValidateLiveTrigger(eventTrigger, target, line);
            target.Triggers.Add(trigger);
        }
        reader.Read(); // past the property end tag
    }

    /// <summary>Reads an inline ItemsControl.ItemTemplate property (one DataTemplate/HierarchicalDataTemplate).</summary>
    internal static void ReadElementItemTemplate(XmlReader reader, IXmlLineInfo line, ItemsControl target, List<FrameworkElement>? scope = null)
    {
        if (reader.IsEmptyElement)
            throw Fail(line, $"<{target.GetType().Name}.ItemTemplate/> is empty (a <DataTemplate> is required).");
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName is not ("DataTemplate" or "HierarchicalDataTemplate"))
            throw Fail(line, $"<{target.GetType().Name}.ItemTemplate> needs a nested <DataTemplate>.");
        target.ItemTemplate = ReadDataTemplateElement(reader, line, scope);
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(line, $"Malformed <{target.GetType().Name}.ItemTemplate> (one template only).");
        reader.Read(); // past the property end tag
    }

    /// <summary>Reads an inline ListBox.ItemTemplate property (one DataTemplate/HierarchicalDataTemplate).</summary>
    internal static void ReadListBoxItemTemplate(XmlReader reader, IXmlLineInfo line, ListBox target, List<FrameworkElement>? scope = null)
    {
        if (reader.IsEmptyElement)
            throw Fail(line, "<ListBox.ItemTemplate/> is empty (a <DataTemplate> is required).");
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName is not ("DataTemplate" or "HierarchicalDataTemplate"))
            throw Fail(line, "<ListBox.ItemTemplate> needs a nested <DataTemplate>.");
        target.ItemTemplate = ReadDataTemplateElement(reader, line, scope);
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(line, "Malformed <ListBox.ItemTemplate> (one template only).");
        reader.Read(); // past the property end tag
    }

    /// <summary>Reads an inline TabControl.ContentTemplate/SelectedContentTemplate property (one DataTemplate/HierarchicalDataTemplate).</summary>
    internal static void ReadTabContentTemplate(XmlReader reader, IXmlLineInfo line, TabControl target, string slot, List<FrameworkElement>? scope = null)
    {
        if (reader.IsEmptyElement)
            throw Fail(line, $"<{target.GetType().Name}.{slot}/> is empty (a <DataTemplate> is required).");
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName is not ("DataTemplate" or "HierarchicalDataTemplate"))
            throw Fail(line, $"<{target.GetType().Name}.{slot}> needs a nested <DataTemplate>.");
        var template = ReadDataTemplateElement(reader, line, scope);
        if (slot == "SelectedContentTemplate") target.SelectedContentTemplate = template;
        else target.ContentTemplate = template;
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(line, $"Malformed <{target.GetType().Name}.{slot}> (one template only).");
        reader.Read(); // past the property end tag
    }

    /// <summary>Reads an inline ItemsControl.ItemsPanel property (one ItemsPanelTemplate).</summary>
    internal static void ReadElementItemsPanel(XmlReader reader, IXmlLineInfo line, ItemsControl target)
    {
        if (reader.IsEmptyElement)
            throw Fail(line, $"<{target.GetType().Name}.ItemsPanel/> is empty (an <ItemsPanelTemplate> is required).");
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "ItemsPanelTemplate")
            throw Fail(line, $"<{target.GetType().Name}.ItemsPanel> needs a nested <ItemsPanelTemplate>.");
        target.ItemsPanel = ReadItemsPanelTemplateElement(reader, line);
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(line, $"Malformed <{target.GetType().Name}.ItemsPanel> (one template only).");
        reader.Read(); // past the property end tag
    }

    /// <summary>Reads an inline legacy ListBox.ItemsPanel property (one ItemsPanelTemplate).</summary>
    internal static void ReadLegacyListBoxItemsPanel(XmlReader reader, IXmlLineInfo line, ListBox target)
    {
        if (reader.IsEmptyElement)
            throw Fail(line, $"<{target.GetType().Name}.ItemsPanel/> is empty (an <ItemsPanelTemplate> is required).");
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "ItemsPanelTemplate")
            throw Fail(line, $"<{target.GetType().Name}.ItemsPanel> needs a nested <ItemsPanelTemplate>.");
        target.ItemsPanel = ReadItemsPanelTemplateElement(reader, line);
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(line, $"Malformed <{target.GetType().Name}.ItemsPanel> (one template only).");
        reader.Read(); // past the property end tag
    }

    /// <summary>Reads an inline Expander.Header/Content property (one visual or text).</summary>
    private static void ReadExpanderSlot(XmlReader reader, IXmlLineInfo line, Expander target, string slot, Dictionary<string, object> names, List<PendingStatic> pending, List<FrameworkElement> scope, List<XamlEventWire> wires, List<PendingVisual> visuals)
    {
        var isHeader = slot == "Header";
        void Assign(object value)
        {
            if (isHeader) target.Header = value;
            else target.Content = value;
        }
        if (reader.IsEmptyElement)
        {
            reader.Read();
            Assign(string.Empty);
            return;
        }
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType == XmlNodeType.Element)
        {
            Assign(ReadElement(reader, line, names, target, pending, scope, wires, visuals));
            SkipWhitespace(reader);
        }
        else
        {
            var value = string.Empty;
            if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
            {
                value = reader.Value;
                reader.Read();
                SkipWhitespace(reader);
            }
            Assign(value);
        }
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(line, $"Malformed <Expander.{slot}> (one value only).");
        reader.Read(); // past the property end tag
    }

    private static void ValidateLiveTrigger(EventTrigger trigger, FrameworkElement target, IXmlLineInfo line)
    {
        var dot = trigger.RoutedEvent.IndexOf('.');
        var owner = dot >= 0 ? FindEngineType(trigger.RoutedEvent[..dot]) : target.GetType();
        var eventName = dot >= 0 ? trigger.RoutedEvent[(dot + 1)..] : trigger.RoutedEvent;
        if (owner is null)
            throw Fail(line, $"'{trigger.RoutedEvent}' names an unknown type (EventTrigger needs [Owner.]Event).");
        var evt = owner.GetEvent(eventName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (evt is null && EventManager.Find(owner, eventName) is null)
            throw Fail(line, $"'{eventName}' is not an event of <{owner.Name}> (EventTrigger needs a CLR event or registered RoutedEvent).");
        foreach (var action in trigger.Actions)
            if (action is not (BeginStoryboard or RemoveStoryboard))
                throw Fail(line, $"<{action.GetType().Name}> is not a supported live trigger action (BeginStoryboard).");
    }

    /// <summary>Reads an element RenderTransform property (positioned on the property start tag).</summary>
    private static void ReadElementRenderTransform(XmlReader reader, IXmlLineInfo line, FrameworkElement target)
    {
        if (reader.IsEmptyElement)
            throw Fail(line, $"<{target.GetType().Name}.RenderTransform/> is empty (a transform element is required).");
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element)
            throw Fail(line, $"<{target.GetType().Name}.RenderTransform> needs a transform element.");
        var node = XElement.Parse(HoistTemplateNamespaces(reader.ReadOuterXml(), reader, line));
        target.RenderTransform = ReadInlineTransform(node, line);
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement)
            throw Fail(line, $"Malformed <{target.GetType().Name}.RenderTransform>.");
        reader.Read(); // past the property end tag
    }

    /// <summary>Subscribes live-tree EventTriggers to their matching CLR events.</summary>
    internal static void AttachLiveTriggers(UIElement root)
    {
        foreach (var scope in WalkTriggers(root))
        {
            foreach (var trigger in scope.Triggers.OfType<EventTrigger>())
                AttachEventTrigger(scope, trigger);
        }
    }

    private static IEnumerable<FrameworkElement> WalkTriggers(UIElement root)
    {
        if (root is FrameworkElement scope) yield return scope;
        if (root is FrameworkElement host)
            foreach (var child in host.LogicalChildren)
                foreach (var nested in WalkTriggers(child))
                    yield return nested;
    }

    private static void AttachEventTrigger(FrameworkElement scope, EventTrigger trigger)
    {
        var dot = trigger.RoutedEvent.IndexOf('.');
        var owner = dot >= 0 ? FindEngineType(trigger.RoutedEvent[..dot]) : scope.GetType();
        var eventName = dot >= 0 ? trigger.RoutedEvent[(dot + 1)..] : trigger.RoutedEvent;
        if (owner is null) return;
        var evt = owner.GetEvent(eventName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        var handlerType = evt?.EventHandlerType;
        if (handlerType is null) return;
        // CLR method binding needs exact signatures, so adapt (object, TArgs)
        // to OnFired(object, EventArgs) with an upcast instead of binding it
        // directly (a direct bind fails for RoutedEventArgs handlers).
        var invoke = handlerType.GetMethod("Invoke");
        var parameters = invoke?.GetParameters();
        if (invoke is null || invoke.ReturnType != typeof(void) || parameters is not { Length: 2 } ||
            parameters[0].ParameterType != typeof(object) || !typeof(EventArgs).IsAssignableFrom(parameters[1].ParameterType))
            return;
        var fire = new TriggerFire(scope, trigger);
        var sender = Expression.Parameter(typeof(object), "sender");
        var args = Expression.Parameter(parameters[1].ParameterType, "args");
        var call = Expression.Call(Expression.Constant(fire), TriggerFire.OnFiredMethod, sender,
            Expression.Convert(args, typeof(EventArgs)));
        var handler = Expression.Lambda(handlerType, call, sender, args).Compile();
        foreach (var target in WalkTriggers(scope).Where(e => owner.IsInstanceOfType(e)))
            evt!.AddEventHandler(target, handler);
    }

    /// <summary>Runs trigger actions (storyboards begin against the trigger element's namescope).</summary>
    internal sealed class TriggerFire(FrameworkElement Scope, EventTrigger Trigger)
    {
        internal static readonly MethodInfo OnFiredMethod =
            typeof(TriggerFire).GetMethod(nameof(OnFired), BindingFlags.Public | BindingFlags.Instance)!;

        public void OnFired(object? sender, EventArgs args)
        {
            foreach (var action in Trigger.Actions)
            {
                if (action is BeginStoryboard begin && begin.Storyboard is not null)
                    WpfAnimationClock.Start(begin.Storyboard, Scope, begin.Name);
                else if (action is RemoveStoryboard remove && remove.BeginStoryboardName is not null)
                    WpfAnimationClock.Remove(remove.BeginStoryboardName, Scope);
            }
        }
    }

    private static List<string> BindingOptions(string args)
    {
        var parts = new List<string>(); var depth = 0; var start = 0;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == '{') depth++; else if (args[i] == '}') depth--;
            else if (args[i] == ',' && depth == 0) { parts.Add(args[start..i].Trim()); start = i + 1; }
        }
        parts.Add(args[start..].Trim()); return parts;
    }

    private static void QueueApplicationBinding(FrameworkElement target, string property, string args,
        string original, IXmlLineInfo line, List<PendingStatic> pending,
        IReadOnlyList<ValidationRule>? rules = null)
    {
        QueueApplicationBinding(target, attribute: null, property, args, original, line, pending, rules);
    }

    private static void QueueApplicationBinding(FrameworkElement target, string? attribute, string? property, string args,
        string original, IXmlLineInfo line, List<PendingStatic> pending,
        IReadOnlyList<ValidationRule>? rules = null)
    {
        var options = BindingOptions(args);
        var source = ExtractNestedOption(options, "Source");
        var relative = ExtractNestedOption(options, "RelativeSource");
        var converter = ExtractNestedOption(options, "Converter");
        var parameter = ExtractNestedOption(options, "ConverterParameter");
        var binding = ParseBinding(string.Join(',', options), original, line);
        if (rules is not null)
            foreach (var rule in rules) binding.ValidationRules.Add(rule);
        if (relative is not null) binding.RelativeSource = ParseRelativeSource(relative, line);
        if (converter is not null) ApplyBindingConverter(binding, converter, line);
        if (parameter is not null)
            binding.ConverterParameter = ParseLiteralOrStaticParameter(parameter, original, line);
        var prop = property ?? attribute;
        if (prop is null) throw Fail(line, "Binding needs a target.");
        if (target is TextBox && prop == "Text" && !BindingUsesMode(args)) binding.Mode = BindingMode.TwoWay;
        if (source is null && converter is null) { BindApplicationTarget(target, attribute, prop, binding); return; }
        if (source is not null && converter is not null)
            throw Fail(line, "Binding Source= and Converter= need separate bindings (combined lookup stays a boundary).");
        if (source is not null)
        {
            var expression = source.Trim();
            if (!expression.StartsWith("{StaticResource ", StringComparison.Ordinal) || !expression.EndsWith('}'))
                throw Fail(line, "Binding Source must refer to {StaticResource Key}.");
            var key = ParseStaticResourceKey(expression[1..^1], expression, line);
            pending.Add(new PendingStatic(target, attribute, property, key) { Binding = binding });
            return;
        }
        if (binding.DeferredConverterKey is null)
        {
            // The converter resolved inline ({x:Static ...} singletons):
            // nothing defers, so the binding activates now instead of
            // queueing a null key that crashes the flush.
            BindApplicationTarget(target, attribute, prop, binding);
            return;
        }
        pending.Add(new PendingStatic(target, attribute, property, binding.DeferredConverterKey) { Binding = binding, KeepSource = true });
    }

    private static bool BindingUsesMode(string args) =>
        BindingOptions(args).Any(p => p.TrimStart().StartsWith("Mode", StringComparison.Ordinal));

    private static void BindApplicationTarget(FrameworkElement target, string? attribute, string? property, Binding binding)
    {
        var name = property ?? attribute ?? throw new InvalidOperationException("Binding needs a target.");
        if (name.Contains('.'))
        {
            target.SetAttachedBinding(name, binding);
            return;
        }
        target.SetBinding(name, binding);
    }

    /// <summary>Removes and returns the first top-level OptionName=... token (nested braces intact).</summary>
    internal static string? ExtractNestedOption(List<string> options, string name)
    {
        for (var i = 0; i < options.Count; i++)
        {
            var token = options[i].TrimStart();
            if (token.StartsWith(name + "=", StringComparison.Ordinal) ||
                token.StartsWith(name + " =", StringComparison.Ordinal))
            {
                options.RemoveAt(i);
                return token[(token.IndexOf('=') + 1)..].Trim();
            }
        }
        return null;
    }

    /// <summary>
    /// Parses {RelativeSource ...} (Mode=/AncestorType=/AncestorLevel= or a
    /// positional mode; a bare AncestorType implies FindAncestor, like WPF).
    /// Type names resolve by short name across engine and converted
    /// assemblies (prefixes are cosmetic single-assembly qualifiers).
    /// </summary>
    internal static RelativeSource ParseRelativeSource(string raw, IXmlLineInfo line)
    {
        var text = raw.Trim();
        if (!text.StartsWith("{RelativeSource", StringComparison.Ordinal) || !text.EndsWith('}'))
            throw Fail(line, $"RelativeSource '{raw}' needs markup ({{RelativeSource ...}}), like WPF.");
        var inner = text["{RelativeSource".Length..^1].Trim();
        var tokens = BindingOptions(inner);
        RelativeSourceMode? mode = null;
        Type? ancestorType = null;
        var level = 1;
        foreach (var token in tokens)
        {
            var part = token.Trim();
            if (part.Length == 0) continue;
            var eq = part.IndexOf('=');
            if (eq < 0)
            {
                if (mode is not null)
                    throw Fail(line, $"RelativeSource '{raw}' takes one positional mode only.");
                mode = ParseRelativeSourceMode(part, raw, line);
                continue;
            }
            var key = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            if (key == "Mode")
            {
                if (mode is not null)
                    throw Fail(line, $"RelativeSource '{raw}' sets Mode twice.");
                mode = ParseRelativeSourceMode(value, raw, line);
            }
            else if (key == "AncestorType")
            {
                ancestorType = ResolveXamlTypeName(value, line);
            }
            else if (key == "AncestorLevel")
            {
                if (!int.TryParse(value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out level) || level < 1)
                    throw Fail(line, $"RelativeSource '{raw}' needs AncestorLevel >= 1.");
            }
            else throw Fail(line, $"RelativeSource '{raw}' supports Mode/AncestorType/AncestorLevel (got '{key}').");
        }
        var resolved = mode ?? (ancestorType is not null ? RelativeSourceMode.FindAncestor
            : throw Fail(line, $"RelativeSource '{raw}' needs a mode (Self/TemplatedParent/FindAncestor)."));
        return new RelativeSource(resolved, ancestorType, level);
    }

    private static RelativeSourceMode ParseRelativeSourceMode(string value, string raw, IXmlLineInfo line) =>
        value.Trim() switch
        {
            "Self" => RelativeSourceMode.Self,
            "TemplatedParent" => RelativeSourceMode.TemplatedParent,
            "FindAncestor" => RelativeSourceMode.FindAncestor,
            "PreviousData" => RelativeSourceMode.PreviousData,
            _ => throw Fail(line, $"RelativeSource '{raw}' has unknown mode '{value}' (Self/TemplatedParent/FindAncestor)."),
        };

    /// <summary>
    /// Resolves {x:Type Name} / {x:Type prefix:Name} by short name across the
    /// engine and converted assemblies (prefixes are single-assembly
    /// qualifiers; the converted assembly is searched by short name).
    /// </summary>
    internal static Type ResolveXamlTypeName(string typeRef, IXmlLineInfo line)
    {
        var text = typeRef.Trim();
        var wrapped = text.StartsWith("{x:Type ", StringComparison.Ordinal) && text.EndsWith('}');
        var bare = text.Length > 0 && !text.Contains('{') && !text.Contains('}') && !text.Contains('=');
        if (!wrapped && !bare)
            throw Fail(line, $"AncestorType '{typeRef}' needs markup ({{x:Type ...}}), like WPF.");
        // Bare AncestorType=prefix:Name (what real XAML authors write): the
        // markup parser resolves the prefix to its assembly; here the short
        // name resolves the same way (prefixes are cosmetic single-assembly
        // qualifiers).
        var name = wrapped ? text["{x:Type ".Length..^1].Trim() : text;
        var shortName = name.Contains(':') ? name[(name.IndexOf(':') + 1)..] : name;
        if (string.IsNullOrEmpty(shortName))
            throw Fail(line, $"AncestorType '{typeRef}' names no type.");
        var engine = XamlLoader.FindEngineType(shortName);
        if (engine is not null) return engine;
        if (FindConvertedType(shortName) is { } converted) return converted;
        // Converted lib types (CardDemo footer/header AncestorType=hc:Card):
        // the entry-only lookup above misses lib-assembly controls, so fall
        // back to the unique converted-closure scan like element and static
        // resolution; zero or ambiguous matches keep the legacy error.
        if (ScanUniqueConvertedType(shortName, line, typeRef) is { } scanned) return scanned;
        throw Fail(line, $"AncestorType '{typeRef}' names no engine or converted type.");
    }

    /// <summary>Applies a Binding Converter (x:Static converter singletons bind
    /// immediately; {StaticResource Key} keeps deferring through resources).</summary>
    private static void ApplyBindingConverter(Binding binding, string converter, IXmlLineInfo line)
    {
        var text = converter.Trim();
        if (text.StartsWith("{x:Static ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            var member = ResolveStaticMember(text[1..^1]["x:Static".Length..].Trim(), converter, line);
            if (member is IValueConverter resolved)
            {
                binding.Converter = resolved;
                return;
            }
            throw Fail(line, $"Converter '{converter}' must name an IValueConverter singleton.");
        }
        binding.DeferredConverterKey = ParseConverterKey(converter, line);
    }

    /// <summary>Parses Converter={StaticResource Key} (only the static form defers through resources).</summary>
    internal static string ParseConverterKey(string raw, IXmlLineInfo line)
    {
        var text = raw.Trim();
        if (text.StartsWith("{StaticResource ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            var inner = text["{StaticResource ".Length..^1].Trim();
            if (inner.Length == 0 || inner.Contains('{') || inner.Contains('='))
                throw Fail(line, $"Converter '{raw}' takes a plain key (Converter={{StaticResource Key}}).");
            return inner;
        }
        throw Fail(line, $"Converter '{raw}' needs {{StaticResource Key}} (assign Binding.Converter in code otherwise).");
    }

    /// <summary>Resolves a deferred converter key against the target scope (fatal when absent or mistyped).</summary>
    internal static IValueConverter ResolveConverterKey(object key, FrameworkElement target, IXmlLineInfo line, IReadOnlyList<FrameworkElement>? scope = null)
    {
        if (target.TryFindResource(key, out var resource) && resource is IValueConverter converter)
            return converter;
        if (scope is not null)
        {
            if (target.Resources.TryGetValue(key, out var own) && own is IValueConverter ownConverter)
                return ownConverter;
            for (var i = scope.Count - 1; i >= 0; i--)
                if (scope[i].Resources.TryGetValue(key, out var ancestor) && ancestor is IValueConverter ancestorConverter)
                    return ancestorConverter;
        }
        throw Fail(line, $"Converter resource '{key}' is not an IValueConverter in element, ancestor or application resources.");
    }

    /// <summary>
    /// Hydrates application-scope resources (positioned on the
    /// &lt;Application.Resources&gt; start tag; consumes through its end tag).
    /// Entries land in <see cref="Application.Resources"/> like real WPF.
    /// </summary>
    internal static void HydrateApplicationResources(Application app, XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(reader);
        var line = (IXmlLineInfo)reader;
        if (reader.IsEmptyElement) { reader.Read(); return; }
        reader.Read(); // past the property start tag
        while (true)
        {
            while (reader.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace or XmlNodeType.Comment)
                reader.Read();
            if (reader.NodeType == XmlNodeType.EndElement) break;
            if (reader.EOF) throw Fail(line, "Unterminated <Application.Resources>.");
            if (reader.NodeType != XmlNodeType.Element)
                throw Fail(line, "Expected a resource entry inside <Application.Resources>.");
            if (reader.LocalName == "ResourceDictionary")
            {
                HashSet<string> before;
                lock (PendingTemplateStatics) { before = new HashSet<string>(PendingTemplateStatics, StringComparer.Ordinal); }
                var nested = ReadResourceDictionaryRoot(reader, line, depth: 0);
                // Merged children land as merges (structure preserved, like
                // WPF), not flattened: each arrival notifies, so deferred
                // template StaticResource values fill in dictionary order.
                foreach (var merged in nested.MergedDictionaries)
                    app.Resources.AddMerged(merged);
                foreach (var key in nested.Keys)
                {
                    if (!nested.TryGetValue(key, out var value))
                        throw Fail(line, $"Resource '{key}' disappeared while merging dictionaries.");
                    try { app.Resources.Add(key, value); }
                    catch (InvalidOperationException ex) { throw Fail(line, ex.Message); }
                }
                // Adding the merged entries fires the refresh that fills
                // deferred template StaticResource values; anything this
                // hydration queued that still misses fails loud, like WPF.
                string? missing;
                lock (PendingTemplateStatics)
                {
                    missing = PendingTemplateStatics.FirstOrDefault(k => !before.Contains(k));
                }
                if (missing is not null)
                    throw Fail(line, $"StaticResource '{missing}' was not found in application resources (template scope).");
                continue;
            }
            ReadResourceEntry(reader, line, app.Resources, wires: null);
        }
        reader.Read(); // past the property end tag
    }

    private static void ReadBindingProperty(XmlReader reader, IXmlLineInfo line, FrameworkElement target,
        string property, List<PendingStatic> pending)
    {
        var binding = XElement.Parse(HoistTemplateNamespaces(reader.ReadOuterXml(), reader, line));
        List<ValidationRule>? rules = null;
        foreach (var child in binding.Elements())
        {
            if (child.Name.LocalName == "Binding.ValidationRules")
            {
                if (rules is not null) throw Fail(line, "Duplicate <Binding.ValidationRules>.");
                rules = ReadValidationRules(child, line);
                continue;
            }
            throw Fail(line, $"Nested <{child.Name.LocalName}> in <Binding> is not supported (only <Binding.ValidationRules>).");
        }
        var options = string.Join(",", binding.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => a.Name.LocalName + "=" + a.Value));
        if (property.Contains('.'))
            QueueApplicationBinding(target, property, null, options, binding.ToString(), line, pending, rules);
        else
            QueueApplicationBinding(target, property, options, binding.ToString(), line, pending, rules);
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement) throw Fail(line, "Malformed binding property element.");
        reader.Read();
    }
    /// <summary>
    /// Reads a direct PriorityBinding property element (the DatePicker
    /// BorderThickness fallback chain): parses eagerly and live-binds via
    /// BindingOperations, like the style-setter composite path. Dotted
    /// names ride the attached transfer (TextFieldAssist thickness
    /// overrides); plain names resolve-or-register the DP like bound
    /// style setters.
    /// </summary>
    private static void ReadPriorityBindingProperty(XmlReader reader, IXmlLineInfo line, FrameworkElement target, string property)
    {
        var node = XElement.Parse(HoistTemplateNamespaces(reader.ReadOuterXml(), reader, line));
        var priority = ReadTriggerPriorityBinding(node, line);
        if (property.Contains('.'))
        {
            target.SetAttachedBinding(property, priority);
        }
        else
        {
            var bindingDp = DependencyProperty.FromName(target.GetType(), property)
                ?? (StylePropertyCache.Find(target.GetType(), property) is { } bindingProp
                    ? DependencyProperty.Register(property, bindingProp.PropertyType, target.GetType())
                    : throw Fail(line, $"'{property}' is not a settable property of <{target.GetType().Name}>."));
            BindingOperations.SetBinding(target, bindingDp, priority);
        }
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement) throw Fail(line, "Malformed binding property element.");
        reader.Read();
    }

    /// <summary>
    /// Reads an attached-owner PriorityBinding property element (the
    /// DatePicker TextFieldAssist thickness overrides): resolves the owner
    /// attached DP across engine and converted images and live-binds the
    /// attached transfer. False when the content is not a PriorityBinding,
    /// so unrelated owner mismatches keep the precise error.
    /// </summary>
    private static bool TryReadAttachedPriorityBinding(FrameworkElement target, string owner, string property, XmlReader reader, IXmlLineInfo line)
    {
        reader.Read(); // past the property start tag
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "PriorityBinding")
            return false;
        var node = XElement.Parse(HoistTemplateNamespaces(reader.ReadOuterXml(), reader, line));
        var priority = ReadTriggerPriorityBinding(node, line);
        var dp = FindAttachedDp(owner, property, line);
        target.SetAttachedBinding(owner + "." + property, priority);
        System.Diagnostics.Debug.Assert(dp.IsAttached);
        SkipWhitespace(reader);
        if (reader.NodeType != XmlNodeType.EndElement) throw Fail(line, $"<{owner}.{property}> takes one <PriorityBinding>.");
        reader.Read(); // past the property end tag
        return true;
    }

    /// <summary>Resolves an attached DP by owner short name (engine, then
    /// the converted entry, then a unique scan across converted images;
    /// missing or non-attached members fail precisely, like static member
    /// resolution).</summary>
    private static DependencyProperty FindAttachedDp(string owner, string property, IXmlLineInfo line)
    {
        var shortName = owner.Contains(':') ? owner[(owner.IndexOf(':') + 1)..] : owner;
        var fieldName = property + "Property";
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var seen = new HashSet<Type>();
        foreach (var candidate in new[] { FindEngineType(shortName), FindConvertedType(shortName) })
        {
            if (candidate is null || !seen.Add(candidate)) continue;
            if (candidate.GetField(fieldName, flags)?.GetValue(null) is DependencyProperty dp && dp.IsAttached)
                return dp;
        }
        var scanned = ScanUniqueConvertedType(shortName, line, owner + "." + property);
        if (scanned is not null && seen.Add(scanned)
            && scanned.GetField(fieldName, flags)?.GetValue(null) is DependencyProperty scannedDp && scannedDp.IsAttached)
            return scannedDp;
        throw Fail(line, $"'{owner}.{property}' is not a registered attached property (engine or converted '{shortName}.{fieldName}').");
    }

    /// <summary>
    /// Reads gallery-shaped &lt;Binding.ValidationRules&gt; (TextBoxPage's
    /// alphabetic rule): parameterless rule elements resolved by short name
    /// from the engine or the converted-app assembly.
    /// </summary>
    private static List<ValidationRule> ReadValidationRules(XElement parent, IXmlLineInfo line)
    {
        var rules = new List<ValidationRule>();
        foreach (var child in parent.Elements())
        {
            // Fragment re-parsing (ReadOuterXml) hoists in-scope namespace
            // declarations onto the point of use, so a prefixed gallery rule
            // like <helpers:AlphabeticValidationRule /> arrives carrying a
            // redundant xmlns:helpers declaration: namespace declarations are
            // not rule arguments, only real attributes and children are.
            if (child.Attributes().Any(a => !a.IsNamespaceDeclaration) || child.HasElements)
                throw Fail(line, $"<{child.Name.LocalName}> validation rules take no attributes or children (parameterless rules only).");
            var name = child.Name.LocalName;
            var engine = typeof(Window).Assembly.GetType($"DesktopWasm.Wpf.{name}", throwOnError: false);
            Type? type = engine is not null && typeof(ValidationRule).IsAssignableFrom(engine) && !engine.IsAbstract
                ? engine
                : CustomElementSource is { } source
                    ? WpfAppBoot.SafeGetTypes(source)
                        .Where(t => t.Name == name && typeof(ValidationRule).IsAssignableFrom(t) && !t.IsAbstract &&
                            t.GetConstructor(Type.EmptyTypes) is not null)
                        .FirstOrDefault()
                    : null;
            if (type is null) throw Fail(line, $"Unknown validation rule <{name}>.");
            try
            {
                rules.Add((ValidationRule)Activator.CreateInstance(type)!);
            }
            catch (Exception ex)
            {
                throw Fail(line, $"<{name}> needs a parameterless constructor: {ex.Message}");
            }
        }
        return rules;
    }

    /// <summary>
    /// Assigns a Brush-valued StaticResource to a Brush-typed CLR property
    /// (Background/Foreground/Fill/Stroke/BorderBrush). False when the element
    /// carries no such property, so the caller falls back to text conversion.
    /// </summary>
    internal static bool TrySetBrushResource(UIElement element, string? attribute, string? property, Brush brush)
    {
        var name = attribute ?? property;
        if (name is null) return false;
        var info = element.GetType().GetProperty(name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (info is null || !info.CanWrite || info.SetMethod?.IsPublic != true) return false;
        if (!info.PropertyType.IsInstanceOfType(brush)) return false;
        info.SetValue(element, brush);
        element.InvalidateVisual();
        return true;
    }

    /// <summary>Finds an engine type by short name (template/trigger owner validation).</summary>
    internal static Type? FindEngineType(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return typeof(Window).Assembly.GetTypes().FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.Ordinal) &&
            string.Equals(t.Namespace, "DesktopWasm.Wpf", StringComparison.Ordinal));
    }

    /// <summary>
    /// Finds an instantiable engine value-converter type by short name
    /// (theme converter instances like BooleanToVisibilityConverter). Only
    /// converter types with a public parameterless constructor qualify;
    /// brushes and everything else keep their dedicated resource entries,
    /// so this branch never shadows them.
    /// </summary>
    internal static Type? FindEngineResourceType(string name)
    {
        var candidate = FindEngineType(name);
        if (candidate is null || candidate.IsAbstract) return null;
        if (candidate.GetConstructor(Type.EmptyTypes) is null) return null;
        if (typeof(IValueConverter).IsAssignableFrom(candidate)) return candidate;
        return null;
    }

    /// <summary>Parses a Template Setter.Value wrapper into a Template-targeted Setter.</summary>
    /// <summary>Resolves a Template Setter.Value property (owner-qualified like
    /// Control.Template, or unqualified against the style target; typeless styles
    /// need the qualified form, like WPF.</summary>
    private static DependencyProperty ResolveTemplateSetterProperty(string property, Type? targetType, IXmlLineInfo line)
    {
        var dot = property.IndexOf('.');
        if (dot > 0)
        {
            var owner = FindEngineType(property[..dot]) ?? FindConvertedType(property[..dot])
                ?? ScanUniqueConvertedType(property[..dot], line, property);
            var owned = owner is not null ? DependencyProperty.FromName(owner, property[(dot + 1)..]) : null;
            if (owned is not null) return owned;
            throw Fail(line, $"'{property}' names no dependency property on <{owner?.Name ?? property[..dot]}>.");
        }
        if (targetType is null)
            throw Fail(line, $"Setter Property=\"{property}\" needs a TargetType or an owner-qualified Property (Owner.Property).");
        return DependencyProperty.FromName(targetType, property)
            ?? (StylePropertyCache.Find(targetType, property) is { } prop
                ? DependencyProperty.Register(property, prop.PropertyType, targetType)
                : throw Fail(line, $"'{property}' is not a settable property of <{targetType.Name}>."));
    }

    private static Setter ReadTemplateSetter(Type targetType, XElement wrapper, IXmlLineInfo line, ResourceDictionary? styleResources = null)
    {
        var templateNode = wrapper.Elements().FirstOrDefault()
            ?? throw Fail(line, "<Setter.Value> for Template needs a nested <ControlTemplate>.");
        if (templateNode.Name.LocalName != "ControlTemplate")
            throw Fail(line, $"<Setter.Value> for Template needs <ControlTemplate>, not <{templateNode.Name.LocalName}>.");
        var template = ReadControlTemplate(templateNode, targetType, line, styleResources);
        var dp = DependencyProperty.FromName(targetType, "Template")
            ?? (StylePropertyCache.Find(targetType, "Template") is { } prop
                ? DependencyProperty.Register("Template", prop.PropertyType, targetType)
                : throw Fail(line, $"'Template' is not a settable property of <{targetType.Name}>."));
        return new Setter(dp, template);
    }

    /// <summary>
    /// Parses a ControlTemplate subtree (XElement form, like styles): retains the
    /// visual root, template namescope, deferred template bindings and triggers.
    /// Elements are real engine instances; inflation (LoadContent) stays a boundary.
    /// </summary>
    /// <summary>Line info for template rebuilds (parse already validated positions).</summary>
    internal static readonly IXmlLineInfo NoLineInfo = new RebuildLineInfo();

    /// <summary>Position-less line info (rebuild failures report without a line).</summary>
    private sealed class RebuildLineInfo : IXmlLineInfo
    {
        public bool HasLineInfo() => false;
        public int LineNumber => 0;
        public int LinePosition => 0;
    }

    internal static ControlTemplate ReadControlTemplate(XElement node, Type? targetType, IXmlLineInfo line, ResourceDictionary? styleResources = null)
    {
        // Resource templates carry no style target; the declared markup type
        // scopes trigger conditions, like WPF.
        var effectiveTarget = targetType ?? ResolveDeclaredTemplateTarget(node, (string?)node.Attribute("TargetType"), line);
        var template = new ControlTemplate { TargetType = effectiveTarget };
        // Rebuilds (ApplyTemplate, LoadContent) re-enter this scope so converted
        // template parts resolve at inflation time, like load time.
        template.TemplateScopeAssembly = CustomElementSource;
        var built = BuildControlTemplate(node, effectiveTarget, line, styleResources: styleResources);
        template.VisualTree = built.Root;
        template.TemplateNames = built.Names;
        template.TemplateBindings = built.Legacy;
        foreach (var trigger in built.Triggers) template.Triggers.Add(trigger);
        template.TemplateSource = node;
        template.TemplateBuildTarget = effectiveTarget;
        return template;
    }

    /// <summary>Resolves a template declared TargetType for trigger parsing
    /// (resource templates carry no style target; the markup type scopes
    /// conditions like WPF).</summary>
    internal static Type? ResolveDeclaredTemplateTarget(XElement node, string? declared, IXmlLineInfo line)
    {
        if (string.IsNullOrWhiteSpace(declared)) return null;
        var clean = declared.Trim();
        if (clean.StartsWith("{x:Type ", StringComparison.Ordinal) && clean.EndsWith('}')) clean = clean[8..^1].Trim();
        if (clean.Contains(':'))
            return ResolveConvertedTypeRef(clean, line, prefix => node.GetNamespaceOfPrefix(prefix)?.NamespaceName, declared);
        return FindEngineType(clean) ?? FindConvertedType(clean);
    }

    /// <summary>Builds a fresh (root, namescope, legacy tuples, inflation
    /// entries, triggers) quintuple from template markup (parse and every inflation).</summary>
    internal static (UIElement Root, Dictionary<string, object> Names, List<(object Element, string Property)> Legacy, List<TemplateBindingEntry> Entries, TriggerCollection Triggers)
        BuildControlTemplate(XElement node, Type? targetType, IXmlLineInfo line, bool validateDeclaredTarget = true, ResourceDictionary? styleResources = null)
    {
        var template = new ControlTemplate { TargetType = targetType };
        var declared = (string?)node.Attribute("TargetType");
        // Parse validates the declared target once; rebuilds reuse the retained
        // markup without re-resolving (CustomElementSource is a load-time scope).
        if (declared is not null && targetType is not null && validateDeclaredTarget)
        {
            var clean = declared.Trim();
            if (clean.StartsWith("{x:Type ", StringComparison.Ordinal) && clean.EndsWith('}')) clean = clean[8..^1].Trim();
            Type? declaredType;
            if (clean.Contains(':'))
            {
                // Prefixed targets resolve xmlns-aware (wpf:Badged is the
                // vendor type even when the entry scope misses it); the
                // resolver throws its own precise unknown-prefix/type error.
                declaredType = ResolveConvertedTypeRef(clean, line,
                    prefix => node.GetNamespaceOfPrefix(prefix)?.NamespaceName, declared);
            }
            else declaredType = FindEngineType(clean) ?? FindConvertedType(clean);
            if (declaredType is null)
                throw Fail(line, $"<ControlTemplate> targets unknown type '{clean}'.");
            if (!declaredType.IsAssignableFrom(targetType) && !targetType.IsAssignableFrom(declaredType)
                && !(typeof(IRowTemplateTarget).IsAssignableFrom(declaredType) && typeof(IRowTemplateTarget).IsAssignableFrom(targetType)))
                throw Fail(line, $"<ControlTemplate> targets <{declaredType.Name}>, not <{targetType.Name}>.");
        }
        var names = new Dictionary<string, object>(StringComparer.Ordinal);
        var bindings = new List<(object Element, string Property)>();
        var entries = new List<TemplateBindingEntry>();
        var resources = new ResourceDictionary();
        var hasResources = false;
        UIElement? root = null;
        foreach (var child in node.Elements())
        {
            if (child.Name.LocalName == "ControlTemplate.Triggers")
            {
                foreach (var trigger in child.Elements())
                    template.Triggers.Add(ReadTemplateTrigger(trigger, targetType, line, null, TriggerScopeResolver(resources, styleResources)));
                continue;
            }
            if (IsWrapper(child, "ControlTemplate", "Resources"))
            {
                ReadTemplateResources(node, child, resources, line);
                hasResources = true;
                continue;
            }
            if (root is not null)
                throw Fail(line, "<ControlTemplate> holds one visual root plus <ControlTemplate.Triggers> and <ControlTemplate.Resources>.");
            root = ReadTemplateElement(child, names, bindings, entries, template, line, TriggerScopeResolver(resources, styleResources));
        }
        var visual = root
            ?? throw Fail(line, "<ControlTemplate> needs a visual root element.");
        template.VisualTree = visual;
        template.TemplateNames = names;
        template.TemplateBindings = bindings;
        if (hasResources && visual is FrameworkElement rootElement)
        {
            // Template-local resources resolve for the inflated subtree
            // through the root (element resources keep precedence, like WPF);
            // implicit application to template parts stays a follow-up.
            rootElement.Resources.AddMerged(resources);
        }
        return (visual, names, bindings, entries, template.Triggers);
    }

    /// <summary>Reads template-local resources (ControlTemplate.Resources):
    /// entries parse through the shared dictionary path; the template root
    /// carries the hoisted namespace declarations (retained at load and at
    /// inflation), so each entry re-roots under them before parsing.</summary>
    private static void ReadTemplateResources(XElement templateRoot, XElement wrapper, ResourceDictionary resources, IXmlLineInfo line)
    {
        var items = wrapper.Elements().ToList();
        if (items.Count == 1 && items[0].Name.LocalName == "ResourceDictionary" && (string?)items[0].Attribute("Source") is null)
            items = items[0].Elements().ToList();
        foreach (var entry in items)
        {
            using var fragment = System.Xml.XmlReader.Create(new System.IO.StringReader(NamespacedEntryXml(templateRoot, entry)),
                new System.Xml.XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true });
            fragment.MoveToContent();
            if (fragment.NodeType != System.Xml.XmlNodeType.Element)
                throw Fail(line, "<ControlTemplate.Resources> needs resource elements only.");
            // Template-local entries take the full dictionary path (styles and
            // templates delegate first, streaming arms handle brushes/easings).
            ReadResourceEntry(fragment, line, resources);
        }
    }

    /// <summary>Finds the template root owning a property wrapper
    /// (template subtrees stay attached to the retained markup, so the
    /// ancestor chain carries the hoisted declarations).</summary>
    private static XElement TemplateRootOf(XElement node) =>
        node.AncestorsAndSelf().LastOrDefault(a => a.Name.LocalName is
            "ControlTemplate" or "DataTemplate" or "HierarchicalDataTemplate" or "ItemsPanelTemplate") ?? node;

    /// <summary>Re-roots a template resource entry under the template root's
    /// namespace declarations (fragments lose ancestor declarations; the
    /// retained template root keeps its hoisted ones).</summary>
    private static string NamespacedEntryXml(XElement templateRoot, XElement entry)
    {
        var xml = entry.ToString();
        // The serialized entry re-declares the namespaces it uses (even
        // inherited ones), so re-insert only declarations the serialized
        // entry does not already carry. Declarations come from the whole
        // ancestor chain (theme prefixes live on the dictionary root, not
        // the template), nearest scope first like XML shadowing; template
        // subtrees stay attached to the retained markup, so the chain
        // survives inflation rebuilds too.
        string needed = string.Empty;
        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var scope in templateRoot.AncestorsAndSelf())
        {
            foreach (var declaration in scope.Attributes().Where(a => a.IsNamespaceDeclaration))
            {
                var marker = declaration.Name.LocalName == "xmlns"
                    ? "xmlns="
                    : "xmlns:" + declaration.Name.LocalName + "=";
                if (!seen.Add(marker)) continue;
                if (!xml.Contains(marker, System.StringComparison.Ordinal))
                    needed += " " + declaration.ToString();
            }
        }
        var declarations = needed;
        if (declarations.Length == 0) return xml;
        var start = xml.IndexOf('<') + 1;
        while (start < xml.Length && !char.IsWhiteSpace(xml[start]) && xml[start] != '>' && xml[start] != '/') start++;
        return xml.Insert(start, declarations);
    }

    /// <summary>Reads template visual states (VisualStateManager.VisualStateGroups):
    /// groups/states/transitions attach to the element's lazy group host;
    /// inline state storyboards share the trigger-action storyboard reader.
    /// States stay inert until something calls GoToState (a behavior
    /// follow-up); loading them keeps themed templates faithful.</summary>
    private static void ReadTemplateVisualStates(FrameworkElement host, XElement node, IXmlLineInfo line, Func<string, object?>? resolveResource = null)
    {
        var groups = VisualStateManager.GetVisualStateGroups(host);
        foreach (var groupNode in node.Elements())
        {
            if (groupNode.Name.LocalName != "VisualStateGroup")
                throw Fail(line, $"<{groupNode.Name.LocalName}> is not valid in <VisualStateManager.VisualStateGroups> (VisualStateGroup only).");
            var group = new VisualStateGroup
            {
                Name = (string?)groupNode.Attribute(XName.Get("Name", XamlNamespace)) ?? (string?)groupNode.Attribute("Name"),
            };
            foreach (var member in groupNode.Elements())
            {
                if (member.Name.LocalName == "VisualStateGroup.Transitions")
                {
                    foreach (var transitionNode in member.Elements())
                        group.Transitions.Add(ReadTemplateTransition(transitionNode, line, resolveResource));
                }
                else if (member.Name.LocalName == "VisualState")
                {
                    group.States.Add(ReadTemplateVisualState(member, line, resolveResource));
                }
                else throw Fail(line, $"<{member.Name.LocalName}> is not valid in <VisualStateGroup> (VisualState and VisualStateGroup.Transitions only).");
            }
            groups.Add(group);
        }
    }

    private static VisualState ReadTemplateVisualState(XElement node, IXmlLineInfo line, Func<string, object?>? resolveResource = null)
    {
        var state = new VisualState
        {
            Name = (string?)node.Attribute(XName.Get("Name", XamlNamespace)) ?? (string?)node.Attribute("Name"),
        };
        foreach (var child in node.Elements())
        {
            if (child.Name.LocalName == "Storyboard" && state.Storyboard is null)
            {
                state.Storyboard = ReadInlineStoryboard(child, line, null, resolveResource);
                continue;
            }
            throw Fail(line, $"<{child.Name.LocalName}> is not valid in <VisualState> (Storyboard only).");
        }
        return state;
    }

    private static VisualTransition ReadTemplateTransition(XElement node, IXmlLineInfo line, Func<string, object?>? resolveResource = null)
    {
        if (node.Name.LocalName != "VisualTransition")
            throw Fail(line, $"<{node.Name.LocalName}> is not valid in <VisualStateGroup.Transitions> (VisualTransition only).");
        var transition = new VisualTransition();
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            var local = attribute.Name.LocalName;
            if (local == "From") transition.From = attribute.Value;
            else if (local == "To") transition.To = attribute.Value;
            else if (local == "GeneratedDuration") transition.GeneratedDuration = Duration.Parse(attribute.Value);
            else throw Fail(line, $"'{attribute.Name}' is not valid on <VisualTransition> (From/To/GeneratedDuration).");
        }
        foreach (var child in node.Elements())
        {
            if (child.Name.LocalName == "Storyboard" && transition.Storyboard is null)
            {
                transition.Storyboard = ReadInlineStoryboard(child, line, null, resolveResource);
                continue;
            }
            if (child.Name.LocalName == "VisualTransition.GeneratedEasingFunction" && transition.GeneratedEasingFunction is null)
            {
                transition.GeneratedEasingFunction = ReadTransitionEasing(child, line);
                continue;
            }
            throw Fail(line, $"<{child.Name.LocalName}> is not valid in <VisualTransition> (Storyboard and VisualTransition.GeneratedEasingFunction only).");
        }
        return transition;
    }

    /// <summary>
    /// Reads a VisualTransition.GeneratedEasingFunction property element (one
    /// inline easing instance, like the theme switch transitions).
    /// </summary>
    private static EasingFunctionBase ReadTransitionEasing(XElement node, IXmlLineInfo line)
    {
        var members = node.Elements().ToList();
        if (members.Count != 1)
            throw Fail(line, "<VisualTransition.GeneratedEasingFunction> holds one easing element (SineEase/CubicEase/QuadraticEase/QuarticEase/PowerEase/CircleEase/BackEase).");
        var easingNode = members[0];
        EasingFunctionBase easing = easingNode.Name.LocalName switch
        {
            "SineEase" => new SineEase(),
            "CubicEase" => new CubicEase(),
            "QuadraticEase" => new QuadraticEase(),
            "QuarticEase" => new QuarticEase(),
            "PowerEase" => new PowerEase(),
            "CircleEase" => new CircleEase(),
            "BackEase" => new BackEase(),
            _ => throw Fail(line, $"<{easingNode.Name.LocalName}> is not a supported transition easing (SineEase/CubicEase/QuadraticEase/QuarticEase/PowerEase/CircleEase/BackEase)."),
        };
        foreach (var attribute in easingNode.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            var local = attribute.Name.LocalName;
            if (local == "EasingMode")
            {
                if (!Enum.TryParse<EasingMode>(attribute.Value.Trim(), true, out var mode))
                    throw Fail(line, $"'{attribute.Value}' is not an EasingMode (EaseIn/EaseOut/EaseInOut).");
                easing.EasingMode = mode;
                continue;
            }
            if (local == "Amplitude" && easing is BackEase back)
            {
                back.Amplitude = ParseDoubleValue("Amplitude", attribute.Value, line);
                continue;
            }
            if (local == "Power" && easing is PowerEase power)
            {
                power.Power = ParseDoubleValue("Power", attribute.Value, line);
                continue;
            }
            throw Fail(line, $"'{attribute.Name}' is not valid on <{easingNode.Name.LocalName}> (EasingMode plus Amplitude on BackEase, Power on PowerEase).");
        }
        return easing;
    }

    /// <summary>
    /// Reads a DataTemplate/HierarchicalDataTemplate element (positioned on its
    /// start tag; consumes it). Retains the single visual root markup; each
    /// LoadContent call inflates a fresh bound visual from it.
    /// </summary>
    internal static DataTemplate ReadDataTemplateElement(XmlReader reader, IXmlLineInfo line, List<FrameworkElement>? scope = null)
    {
        var kind = reader.LocalName;
        if (kind is not ("DataTemplate" or "HierarchicalDataTemplate"))
            throw Fail(line, $"<{kind}> is not a data template (DataTemplate/HierarchicalDataTemplate only).");
        var dataType = reader.GetAttribute("DataType");
        var itemsSourceRaw = reader.GetAttribute("ItemsSource");
        if (kind != "HierarchicalDataTemplate" && itemsSourceRaw is not null)
            throw Fail(line, $"<{kind}> takes no ItemsSource (HierarchicalDataTemplate only).");
        var (visualXml, triggers) = ReadSingleTemplateRoot(reader, line, kind);
        DataTemplate template = kind == "HierarchicalDataTemplate"
            ? new HierarchicalDataTemplate(visualXml)
            : new DataTemplate(visualXml);
        foreach (var trigger in triggers)
            template.Triggers.Add(trigger);
        template.DataType = dataType;
        template.LoadScope = scope is null ? null : new List<FrameworkElement>(scope);
        if (template is HierarchicalDataTemplate hierarchical && itemsSourceRaw is not null)
            hierarchical.ItemsSource = ParseTemplateItemsSource(itemsSourceRaw, line);
        return template;
    }

    /// <summary>Reads a DataTemplate node (style/trigger Setter.Value
    /// content): the retained markup inflates per use, like streamed
    /// templates; single visual root, like WPF.</summary>
    private static DataTemplate ReadDataTemplateNode(XElement node, IXmlLineInfo line)
    {
        if (node.Name.LocalName is not ("DataTemplate" or "HierarchicalDataTemplate"))
            throw Fail(line, $"<{node.Name.LocalName}> is not a data template (DataTemplate/HierarchicalDataTemplate only).");
        var triggersNode = node.Elements().FirstOrDefault(e => e.Name.LocalName == node.Name.LocalName + ".Triggers");
        var visualChildren = node.Elements().Where(e => e != triggersNode).ToList();
        if (visualChildren.Count != 1)
            throw Fail(line, $"<{node.Name.LocalName}> holds one visual root.");
        var visualXml = triggersNode is null
            ? string.Concat(node.Nodes())
            : visualChildren[0].ToString(SaveOptions.DisableFormatting);
        DataTemplate template = node.Name.LocalName == "HierarchicalDataTemplate"
            ? new HierarchicalDataTemplate(visualXml)
            : new DataTemplate(visualXml);
        template.DataType = (string?)node.Attribute("DataType");
        if (triggersNode is not null)
            foreach (var triggerNode in triggersNode.Elements())
            {
                var parsed = ReadTemplateTrigger(triggerNode, null, line);
                if (parsed is not (DataTrigger or MultiDataTrigger))
                    throw Fail(line, $"<{node.Name.LocalName}.Triggers> takes DataTrigger/MultiDataTrigger only (property/event triggers stay a precise boundary).");
                template.Triggers.Add(parsed);
            }
        if (template is HierarchicalDataTemplate hierarchical)
        {
            var itemsSourceRaw = (string?)node.Attribute("ItemsSource");
            if (itemsSourceRaw is not null)
                hierarchical.ItemsSource = ParseTemplateItemsSource(itemsSourceRaw, line);
        }
        return template;
    }

    /// <summary>
    /// Reads an ItemsPanelTemplate element (positioned on its start tag;
    /// consumes it). Panels take no DataType/ItemsSource; the retained root
    /// markup must inflate to a Panel (checked when the host is built).
    /// </summary>
    internal static ItemsPanelTemplate ReadItemsPanelTemplateElement(XmlReader reader, IXmlLineInfo line)
    {
        if (reader.LocalName != "ItemsPanelTemplate")
            throw Fail(line, $"<{reader.LocalName}> is not an items panel (ItemsPanelTemplate only).");
        if (reader.GetAttribute("DataType") is not null)
            throw Fail(line, "<ItemsPanelTemplate> takes no DataType.");
        if (reader.GetAttribute("ItemsSource") is not null)
            throw Fail(line, "<ItemsPanelTemplate> takes no ItemsSource.");
        return new ItemsPanelTemplate(ReadSingleTemplateRoot(reader, line, "ItemsPanelTemplate").VisualXml);
    }

    /// <summary>Consumes a template element through its single visual root child (the root markup is retained, not inflated).
    /// Data templates also accept a DataTemplate.Triggers sibling, parsed to
    /// data triggers like WPF (anything else beside the root stays loud).</summary>
    private static (string VisualXml, TriggerCollection Triggers) ReadSingleTemplateRoot(XmlReader reader, IXmlLineInfo line, string kind)
    {
        if (reader.IsEmptyElement)
            throw Fail(line, $"<{kind}> needs one visual root element.");
        reader.Read(); // past the template start tag
        string? visualXml = null;
        var triggers = new TriggerCollection();
        while (true)
        {
            SkipWhitespace(reader);
            if (reader.NodeType == XmlNodeType.EndElement)
                break;
            if (reader.NodeType != XmlNodeType.Element)
                throw Fail(line, $"<{kind}> needs one visual root element (no text).");
            if (reader.LocalName == kind + ".Triggers")
            {
                if (kind is not ("DataTemplate" or "HierarchicalDataTemplate"))
                    throw Fail(line, $"<{kind}> takes no triggers.");
                var triggersNode = (XElement)XNode.ReadFrom(reader)!;
                foreach (var triggerNode in triggersNode.Elements())
                {
                    var parsed = ReadTemplateTrigger(triggerNode, null, line);
                    if (parsed is not (DataTrigger or MultiDataTrigger))
                        throw Fail(line, $"<{kind}.Triggers> takes DataTrigger/MultiDataTrigger only (property/event triggers stay a precise boundary).");
                    triggers.Add(parsed);
                }
                continue;
            }
            if (visualXml is not null)
                throw Fail(line, $"<{kind}> holds one visual root element only.");
            visualXml = HoistTemplateNamespaces(reader.ReadOuterXml(), reader, line);
        }
        if (visualXml is null)
            throw Fail(line, $"<{kind}> needs one visual root element.");
        reader.Read(); // past the template end tag
        return (visualXml, triggers);
    }

    /// <summary>
    /// Re-declares value-only prefixes on a retained fragment root (template
    /// visuals and style fragments alike).
    /// ReadOuterXml repeats only namespaces used by element/attribute names,
    /// so prefixes living inside attribute VALUES ({ex:Lang ...},
    /// {x:Static langs:...}) lose their declarations and inflation-time
    /// LookupNamespace fails. Every such prefix still in scope at capture is
    /// re-declared on the root tag (same URI, so element resolution is
    /// unchanged); the reserved "xml" prefix and unresolvable prefixes stay
    /// untouched and keep their precise inflation errors.
    /// </summary>
    private static string HoistTemplateNamespaces(string visualXml, XmlReader reader, IXmlLineInfo line)
    {
        var close = visualXml.IndexOf('>');
        if (close <= 0) throw Fail(line, "Template root markup is malformed.");
        var insert = close;
        if (insert > 0 && visualXml[insert - 1] == '/') insert--;
        var rootTag = visualXml[..insert];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var extra = string.Empty;
        // Every namespace prefix used anywhere in the fragment, not just
        // inside {markup-extension} spans: plain attribute values (Setter
        // Property="wpf:ButtonAssist.CornerRadius") carry prefixes too, and
        // ReadOuterXml drops declarations used only there. Unresolvable
        // words (http:, pack:, prose) are skipped by TryHoistPrefix, and
        // same-URI redeclaration leaves element resolution unchanged.
        var span = visualXml;
        var j = 0;
        while (j < span.Length)
        {
            while (j < span.Length && !IsPrefixStart(span[j])) j++;
            var wordStart = j;
            while (j < span.Length && IsPrefixChar(span[j])) j++;
            if (j < span.Length && span[j] == ':' && j > wordStart)
            {
                if (seen.Add(span[wordStart..j]))
                    TryHoistPrefix(span[wordStart..j], rootTag, reader, ref extra);
            }
            if (j < span.Length && (span[j] == ':' || j == wordStart)) j++;
        }
        if (extra.Length == 0) return visualXml;
        return visualXml[..insert] + extra + visualXml[insert..];
    }

    private static bool IsPrefixStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsPrefixChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.';

    private static void TryHoistPrefix(string prefix, string rootTag, XmlReader reader, ref string extra)
    {
        if (rootTag.Contains("xmlns:" + prefix + "=", StringComparison.Ordinal)) return;
        if (string.Equals(prefix, "xml", StringComparison.Ordinal)) return;
        if (string.Equals(prefix, "xmlns", StringComparison.Ordinal)) return;
        string? uri = null;
        try { uri = reader.LookupNamespace(prefix); }
        catch { uri = null; }
        if (string.IsNullOrEmpty(uri)) return;
        extra += " xmlns:" + prefix + "=\"" + uri + "\"";
    }

    /// <summary>Parses a HierarchicalDataTemplate ItemsSource binding (same markup as element bindings).</summary>
    private static Binding ParseTemplateItemsSource(string raw, IXmlLineInfo line)
    {
        var text = raw.Trim();
        if (!text.StartsWith("{Binding", StringComparison.Ordinal) || !text.EndsWith('}'))
            throw Fail(line, $"HierarchicalDataTemplate ItemsSource '{raw}' needs {{Binding ...}} markup.");
        var inner = text[1..^1].Trim()["Binding".Length..];
        var options = BindingOptions(inner);
        var relative = ExtractNestedOption(options, "RelativeSource");
        var converter = ExtractNestedOption(options, "Converter");
        var binding = ParseBinding(string.Join(',', options), raw, line);
        if (relative is not null) binding.RelativeSource = ParseRelativeSource(relative, line);
        if (converter is not null) ApplyBindingConverter(binding, converter, line);
        return binding;
    }

    /// <summary>Parses one template visual element with attributes and property children.</summary>
    private static UIElement ReadTemplateElement(
        XElement node, Dictionary<string, object> names, List<(object Element, string Property)> bindings,
        List<TemplateBindingEntry> entries, ControlTemplate template, IXmlLineInfo line, Func<string, object?>? resolveResource = null)
    {
        var element = CreateElement(node.Name.LocalName, node.Name.NamespaceName, line);
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            var local = attribute.Name.LocalName;
            // x:Name registers into the template namescope before the
            // ignorable sweep (the XAML namespace is ignorable elsewhere).
            if (attribute.Name.NamespaceName == XamlNamespace && local == "Name")
            {
                RegisterTemplateName(names, element, attribute.Value, line);
                if (element is FrameworkElement named) named.Name = attribute.Value;
                continue;
            }
            if (IsIgnorableAttribute(attribute)) continue;
            if (local == "Name")
            {
                RegisterTemplateName(names, element, attribute.Value, line);
                if (element is FrameworkElement named) named.Name = attribute.Value;
                continue;
            }
            ApplyTemplateAttribute(element, local, attribute.Value, bindings, entries, line);
        }
        foreach (var child in node.Elements())
        {
            var parts = child.Name.LocalName.Split('.', 2);
            if (parts.Length == 2 && parts[0] == "VisualStateManager" && parts[1] == "VisualStateGroups" &&
                element is FrameworkElement stateHost)
            {
                ReadTemplateVisualStates(stateHost, child, line, resolveResource);
                continue;
            }
            if (parts.Length == 2 && string.Equals(parts[0], element.GetType().Name, StringComparison.Ordinal))
            {
                ReadTemplateProperty(element, parts[1], child, names, bindings, entries, template, line);
                continue;
            }
            if (parts.Length == 2)
            {
                // Attached or foreign-owned template property elements
                // (Grid.Row on non-panels): resolve owner-aware, like WPF.
                ReadTemplateProperty(element, child.Name.LocalName, child, names, bindings, entries, template, line);
                continue;
            }
            if (element is FrameworkElement host && host is Panel or Border)
            {
                host.AddChild(ReadTemplateElement(child, names, bindings, entries, template, line));
                continue;
            }
            if (element is Popup popupHost && popupHost.Child is null)
            {
                // Popup.Child hosts the single drop-down visual (matches
                // System.Windows.Controls.Primitives.Popup: the PC Space Map
                // ComboBox template nests its DropDownBorder here).
                popupHost.Child = ReadTemplateElement(child, names, bindings, entries, template, line);
                continue;
            }
            if (element is Decorator decoratorHost && decoratorHost.Child is null)
            {
                // Decorator.Child hosts the single adorned visual (matches
                // System.Windows.Controls.Decorator: theme templates nest
                // the content grid inside AdornerDecorator).
                decoratorHost.Child = ReadTemplateElement(child, names, bindings, entries, template, line);
                continue;
            }
            if (element is Viewbox boxHost && boxHost.Child is null)
            {
                // Viewbox.Child hosts the single scaled visual (matches
                // System.Windows.Controls.Viewbox: the Calendar arrows nest
                // their Path glyphs here).
                boxHost.Child = ReadTemplateElement(child, names, bindings, entries, template, line);
                continue;
            }
            if (element is ScrollViewer scrollerHost && scrollerHost.Content is null)
            {
                // ScrollViewer.Content hosts the single scrolled visual
                // (matches System.Windows.Controls.ScrollViewer: the PC Space
                // Map ComboBox template nests its ItemsPresenter here).
                scrollerHost.Content = ReadTemplateElement(child, names, bindings, entries, template, line);
                continue;
            }
            if (element is ContentControl contentHost &&
                (contentHost.Content is null || contentHost.Content is string contentText && contentText.Length == 0))
            {
                // Template scope retains one nested visual as content (only
                // controls that host visuals render it; XAML scope stays
                // strict because live trees need layout).
                contentHost.Content = ReadTemplateElement(child, names, bindings, entries, template, line);
                continue;
            }
            if (XamlLoader.TryAttachCustomContent(element, ReadTemplateElement(child, names, bindings, entries, template, line), line))
                continue;
            throw Fail(line, $"<{child.Name.LocalName}> cannot nest in template <{element.GetType().Name}> (use the <{element.GetType().Name}.Property> form).");
        }
        return element;
    }

    private static void RegisterTemplateName(Dictionary<string, object> names, object element, string name, IXmlLineInfo line)
    {
        if (string.IsNullOrWhiteSpace(name)) throw Fail(line, "Template x:Name must not be empty.");
        if (names.ContainsKey(name)) throw Fail(line, $"Duplicate template x:Name '{name}'.");
        names[name] = element;
    }

    private static bool IsIgnorableAttribute(XAttribute attribute)
    {
        if (attribute.IsNamespaceDeclaration) return true;
        var ns = attribute.Name.NamespaceName;
        // x: attributes (Key/Name/Class) are consumed by the entry readers;
        // sub-readers only take unqualified XAML attributes.
        if (ns == XamlNamespace) return true;
        return ns == "http://www.w3.org/XML/1998/namespace"
            || attribute.Name.LocalName == "Ignorable"
            || ns.EndsWith("/blend/2008", StringComparison.Ordinal)
            || ns.EndsWith("/markup-compatibility/2006", StringComparison.Ordinal)
            || ns.EndsWith("/presentation/options", StringComparison.Ordinal);
    }

    /// <summary>
    /// Template StaticResource keys deferred past hydration order (cleared on
    /// apply; keys added during one hydration that never land fail it loud).
    /// </summary>
    private static readonly HashSet<string> PendingTemplateStatics = new(StringComparer.Ordinal);

    /// <summary>Parses {ComponentResourceKey ResourceId=Id, TypeInTargetAssembly={x:Type T}}.</summary>
    private static ComponentResourceKey ParseComponentResourceKey(string inner, string original, IXmlLineInfo line)
    {
        string? resourceId = null;
        Type? assembly = null;
        foreach (var option in BindingOptions(inner))
        {
            var eq = option.IndexOf('=');
            if (eq <= 0)
                throw Fail(line, $"'{original}' needs ResourceId=/TypeInTargetAssembly= pairs (ComponentResourceKey).");
            var name = option[..eq].Trim();
            var text = option[(eq + 1)..].Trim();
            if (name == "ResourceId") resourceId = text;
            else if (name == "TypeInTargetAssembly")
            {
                if (!text.StartsWith("{x:Type ", StringComparison.Ordinal) || !text.EndsWith('}'))
                    throw Fail(line, $"'{original}' TypeInTargetAssembly needs {{x:Type Name}} (ComponentResourceKey).");
                var clean = text["{x:Type ".Length..^1].Trim();
                assembly = clean.Contains(':')
                    ? throw Fail(line, $"'{original}' TypeInTargetAssembly prefix '{clean}' stays a template-scope boundary (use an unqualified type).")
                    : (FindEngineType(clean) ?? FindConvertedType(clean))
                    ?? throw Fail(line, $"'{original}' TypeInTargetAssembly '{clean}' names no loaded type.");
            }
            else
                throw Fail(line, $"'{original}' has no ComponentResourceKey option '{name}' (ResourceId/TypeInTargetAssembly).");
        }
        if (resourceId is null)
            throw Fail(line, $"'{original}' needs ResourceId= (ComponentResourceKey).");
        if (assembly is null)
            throw Fail(line, $"'{original}' needs TypeInTargetAssembly= (ComponentResourceKey).");
        return new ComponentResourceKey(assembly, resourceId);
    }

    /// <summary>Applies one template attribute (TemplateBinding defers; StaticResource resolves now or after hydration).</summary>
    private static void ApplyTemplateAttribute(
        UIElement element, string local, string value, List<(object Element, string Property)> bindings,
        List<TemplateBindingEntry> entries, IXmlLineInfo line)
    {
        if (value.StartsWith("{Binding", StringComparison.Ordinal) && value.EndsWith('}'))
        {
            // Explicit bindings in templates (the gallery copies the
            // templated parent's attached Name): validated with the real
            // parser and recorded by source path; RelativeSource facets stay
            // an inflation boundary like the rest of template bindings.
            var body = value[1..^1].Trim()["Binding".Length..];
            if (body.Length == 0)
            {
                bindings.Add((element, local));
                entries.Add(new TemplateBindingEntry(element, local, string.Empty) { TemplateSource = new Binding() });
                return;
            }
            var options = BindingOptions(body);
            var relative = ExtractNestedOption(options, "RelativeSource");
            var converter = ExtractNestedOption(options, "Converter");
            var templateParameter = ExtractNestedOption(options, "ConverterParameter");
            Binding parsed;
            try { parsed = ParseBinding(string.Join(',', options), value, line); }
            catch (ArgumentException ex) { throw Fail(line, ex.Message); }
            if (templateParameter is not null)
                parsed.ConverterParameter = ParseLiteralOrStaticParameter(templateParameter, value, line);
            RelativeSource? resolvedRelative = null;
            if (relative is not null)
            {
                try { resolvedRelative = ParseRelativeSource(relative, line); }
                catch (ArgumentException ex) { throw Fail(line, ex.Message); }
            }
            string? bindingConverter = null;
            if (converter is not null)
            {
                var converterText = converter.Trim();
                if (converterText.StartsWith("{x:Static ", StringComparison.Ordinal) && converterText.EndsWith('}'))
                    ApplyBindingConverter(parsed, converter, line);
                else
                {
                    try { bindingConverter = ParseConverterKey(converter, line); }
                    catch (ArgumentException ex) { throw Fail(line, ex.Message); }
                }
            }
            string pathText = parsed.Path;
            if (string.IsNullOrEmpty(pathText)) pathText = local;
            parsed.RelativeSource = resolvedRelative;
            bindings.Add((element, pathText));
            if (bindingConverter is not null)
                bindings.Add((element, "Converter:" + bindingConverter));
            entries.Add(new TemplateBindingEntry(element, local, pathText)
                { TemplateSource = parsed, ConverterKey = bindingConverter });
            return;
        }
        if (value.StartsWith("{TemplateBinding ", StringComparison.Ordinal) && value.EndsWith('}'))
        {
            // TemplateBinding with a value converter (the gallery's header
            // visibility rule): the converter key is validated here and rides
            // with the property until inflation consumes it.
            var body = value["{TemplateBinding ".Length..^1].Trim();
            // Long form ({TemplateBinding Property=Background}, as the BAML
            // round-trip emits): strip the explicit Property= prefix, keeping
            // the original segment length for the options offset below.
            var segment = body.Split(',', 2)[0].Trim();
            var property = segment;
            if (property.StartsWith("Property=", StringComparison.Ordinal))
                property = property["Property=".Length..].Trim();
            if (property.Length == 0 || property.Contains('{') || property.Contains('='))
                throw Fail(line, $"'{value}' is not a TemplateBinding (needs a property name).");
            var rest = body[segment.Length..].Trim();
            if (rest.StartsWith(",", StringComparison.Ordinal)) rest = rest[1..].Trim();
            string? converterKey = null;
            IValueConverter? staticConverter = null;
            string? parameterKey = null;
            string? parameterLiteral = null;
            if (rest.Length > 0)
            {
                // Converter={StaticResource Key} or Converter={x:Static ...}
                // plus an optional ConverterParameter ({StaticResource Key}
                // or literal).
                foreach (var option in BindingOptions(rest))
                {
                    var opt = option.Trim();
                    const string convPrefix = "Converter={StaticResource ";
                    const string staticConvPrefix = "Converter={x:Static ";
                    const string paramPrefix = "ConverterParameter=";
                    if (opt.StartsWith(staticConvPrefix, StringComparison.Ordinal) && opt.EndsWith('}'))
                    {
                        if (converterKey is not null || staticConverter is not null)
                            throw Fail(line, $"'{value}' takes Converter once only.");
                        var member = opt[staticConvPrefix.Length..^1].Trim();
                        var resolved = ResolveStaticMember(member, value, line);
                        if (resolved is not IValueConverter converter)
                            throw Fail(line, $"'{value}' takes Converter as an IValueConverter singleton ({{x:Static ...}}).");
                        staticConverter = converter;
                    }
                    else if (opt.StartsWith(convPrefix, StringComparison.Ordinal) && opt.EndsWith('}'))
                    {
                        if (converterKey is not null || staticConverter is not null)
                            throw Fail(line, $"'{value}' takes Converter once only.");
                        converterKey = opt[convPrefix.Length..^1].Trim();
                        if (converterKey.Length == 0 || converterKey.Contains('{') || converterKey.Contains('='))
                            throw Fail(line, $"'{value}' takes a plain converter key ({{StaticResource Key}}).");
                    }
                    else if (opt.StartsWith(paramPrefix, StringComparison.Ordinal))
                    {
                        if ((parameterKey ?? parameterLiteral) is not null)
                            throw Fail(line, $"'{value}' takes ConverterParameter once only.");
                        var pval = opt[paramPrefix.Length..].Trim();
                        const string resPrefix = "{StaticResource ";
                        if (pval.StartsWith(resPrefix, StringComparison.Ordinal) && pval.EndsWith('}'))
                        {
                            parameterKey = pval[resPrefix.Length..^1].Trim();
                            if (parameterKey.Length == 0 || parameterKey.Contains('{') || parameterKey.Contains('='))
                                throw Fail(line, $"'{value}' takes a plain parameter key ({{StaticResource Key}}).");
                        }
                        else if (pval.Length == 0 || pval.Contains('{') || pval.Contains('}'))
                            throw Fail(line, $"'{value}' takes ConverterParameter as a literal or {{StaticResource Key}}.");
                        else parameterLiteral = pval;
                    }
                    else throw Fail(line, $"'{value}' takes a property name plus Converter/ConverterParameter only.");
                }
                if ((parameterKey ?? parameterLiteral) is not null && converterKey is null)
                    throw Fail(line, $"'{value}' takes ConverterParameter with a Converter.");
            }
            bindings.Add((element, property));
            if (converterKey is not null)
                bindings.Add((element, "Converter:" + converterKey));
            entries.Add(new TemplateBindingEntry(element, local, property)
            {
                ConverterKey = converterKey,
                Converter = staticConverter,
                ConverterParameterKey = parameterKey,
                ConverterParameter = parameterLiteral,
            });
            return;
        }
        if (value.StartsWith("{StaticResource ", StringComparison.Ordinal) && value.EndsWith('}'))
        {
            var key = value["{StaticResource ".Length..^1].Trim();
            object lookupKey = key.StartsWith("{ComponentResourceKey", StringComparison.Ordinal) && key.EndsWith('}')
                ? ParseComponentResourceKey(key["{ComponentResourceKey".Length..^1].Trim(), value, line)
                : key;
            if (lookupKey is string)
            {
            if (key.Length == 0 || (key.Contains('{') && !key.StartsWith("{x:Static ", StringComparison.Ordinal)) || key.Contains('='))
                throw Fail(line, $"'{value}' takes a plain key only ({{StaticResource Key}}).");
            if (key.StartsWith("{x:Static ", StringComparison.Ordinal) && key.EndsWith('}'))
            {
                var staticMember = ResolveStaticMember(key[1..^1]["x:Static".Length..].Trim(), value, line);
                if (staticMember is not ResourceKey staticKey)
                    throw Fail(line, $"'{value}' needs a resource key (x:Static must name a ResourceKey).");
                if (SystemTheme.TryFindSystemResource(staticKey, out var system) && system is not null)
                {
                    SetTemplateValue(element, local, system, line);
                    return;
                }
                lookupKey = staticKey;
            }
            }
            if (local == "Style" && element is FrameworkElement && lookupKey is string plainKey)
            {
                // Template StaticResource Style resolves at use, not in
                // dictionary order (WPF): sibling dictionaries may not have
                // landed yet, and the style's own setters must apply against
                // the linked tree (a theme template Style applies setters
                // naming later-sibling keys). Defer by key; inflation
                // resolves loud-or-bound like WPF.
                entries.Add(new TemplateBindingEntry(element, "Style", string.Empty) { DeferredStyleKey = plainKey });
                return;
            }
            // Defining-dictionary merges first (a theme file merges
            // its ValidationErrorTemplate locally): hydration scopes
            // resolve innermost-first like WPF, before app scope.
            if (TryFindHydrationResource(lookupKey, out var hydrated) && hydrated is not null)
            {
                SetTemplateValue(element, local, hydrated, line);
                return;
            }
            var app = Application.Current;
            if (app is null)
                throw Fail(line, $"StaticResource '{key}' was not found in application resources (template scope).");
            if (app.Resources.TryGetValue(lookupKey, out var resource) && resource is not null)
            {
                SetTemplateValue(element, local, resource, line);
                return;
            }
            if (lookupKey is not string)
                throw Fail(line, $"StaticResource '{key}' was not found in application resources (template scope).");
            // Apply-time lookup (WPF resolves template StaticResource when the
            // template is used, not in dictionary order): sibling dictionaries
            // may not have landed yet, so fill when the key appears. Hydration
            // fails loud for keys that never land.
            if (element is not FrameworkElement subscriber)
                throw Fail(line, $"StaticResource '{key}' was not found in application resources (template scope).");
            lock (PendingTemplateStatics) { PendingTemplateStatics.Add(key); }
            var capturedElement = element;
            var capturedLocal = local;
            Application.DynamicResourceManager.SubscribeProperty(subscriber, "template-static:" + capturedLocal + ":" + key, key,
                (_, resolved) =>
                {
                    if (resolved is null) return;
                    SetTemplateValue(capturedElement, capturedLocal, resolved, line);
                    lock (PendingTemplateStatics) { PendingTemplateStatics.Remove(key); }
                });
            return;
        }
        if (value.StartsWith("{DynamicResource ", StringComparison.Ordinal) && value.EndsWith('}'))
        {
            var key = value["{DynamicResource ".Length..^1].Trim();
            object lookupKey = key.StartsWith("{ComponentResourceKey", StringComparison.Ordinal) && key.EndsWith('}')
                ? ParseComponentResourceKey(key["{ComponentResourceKey".Length..^1].Trim(), value, line)
                : key;
            if (lookupKey is string)
            {
            if (key.Length == 0 || (key.Contains('{') && !key.StartsWith("{x:Static ", StringComparison.Ordinal)) || key.Contains('='))
                throw Fail(line, $"'{value}' takes a plain key only ({{DynamicResource Key}}).");
            if (key.StartsWith("{x:Static ", StringComparison.Ordinal) && key.EndsWith('}'))
            {
                var staticMember = ResolveStaticMember(key[1..^1]["x:Static".Length..].Trim(), value, line);
                if (staticMember is not ResourceKey staticKey)
                    throw Fail(line, $"'{value}' needs a resource key (x:Static must name a ResourceKey).");
                if (SystemTheme.TryFindSystemResource(staticKey, out var system) && system is not null)
                    SetTemplateValue(element, local, system, line);
                lookupKey = staticKey;
            }
            }
            // Missing keys stay unset and refresh when the key appears, like
            // style setters (the retained prototype re-applies on change).
            // Object keys (x:Static) refresh through the same application
            // scope, which carries the system values.
            if (Application.Current is { } app &&
                app.Resources.TryGetValue(lookupKey, out var resource) && resource is not null)
            {
                SetTemplateValue(element, local, resource, line);
            }
            if (element is FrameworkElement subscriber)
            {
                var capturedElement = element;
                var capturedLocal = local;
                Application.DynamicResourceManager.SubscribeProperty(subscriber, "template:" + capturedLocal, lookupKey,
                    (_, refreshed) =>
                    {
                        if (refreshed is null) return;
                        try { SetTemplateValue(capturedElement, capturedLocal, refreshed, line); }
                        catch
                        {
                            // Refresh keeps old values when a key mistypes;
                            // load time stays loud instead.
                        }
                    });
            }
            return;
        }
            if (value.Trim() == "{x:Null}")
            {
                // x:Null clears the property (theme templates null their fills
                // and strokes to inherit or hide).
                SetTemplateValue(element, local, null, line);
                return;
            }
        if (value.StartsWith("{x:Static ", StringComparison.Ordinal) && value.EndsWith('}'))
        {
            // Vendor constants on template parts (the ComboBox arrow Path
            // sizes from wpf:Constants): engine and converted statics
            // resolve through the shared member path and convert in place.
            var member = value["{x:Static".Length..^1].Trim();
            SetTemplateValue(element, local, ResolveStaticMember(member, value, line), line);
            return;
        }
        if (value.StartsWith('{'))
            throw Fail(line, $"'{value}' is not supported on template <{element.GetType().Name}> (only {{TemplateBinding ...}}, {{StaticResource ...}} and {{x:Static ...}}).");
        SetTemplateValue(element, local, value, line);
    }

    /// <summary>Assigns a template attribute value through the CLR property with XAML conversion.</summary>
    private static void SetTemplateValue(UIElement element, string local, object value, IXmlLineInfo line)
    {
        // LengthConverter parity (verified live: native maps "Auto" to NaN
        // for all six dimension properties): template dimensions take the
        // same Auto path the runtime loader's ParseLength uses.
        if (value is string lengthRaw && local is "Width" or "Height" or "MinWidth" or
            "MaxWidth" or "MinHeight" or "MaxHeight" &&
            string.Equals(lengthRaw.Trim(), "Auto", StringComparison.OrdinalIgnoreCase))
        {
            SetTemplateValue(element, local, double.NaN, line);
            return;
        }
        if (local.Contains('.'))
        {
            // Converted attached DPs (wpf:ButtonAssist.CornerRadius) set
            // through the DP store; engine attached names keep the bag
            // path below, which layout and chrome read.
            var ownerText = local[..local.IndexOf('.')];
            var memberText = local[(local.IndexOf('.') + 1)..];
            if (FindEngineType(ownerText) is null)
            {
                var converted = ResolveTriggerOwner(ownerText, line);
                var convertedDp = converted is not null ? DependencyProperty.FromName(converted, memberText) : null;
                if (convertedDp is null)
                    throw Fail(line, $"'{local}' names no dependency property on <{converted?.Name ?? ownerText}>.");
                object? convertedValue = value;
                if (value is string convertedRaw)
                {
                    try { convertedValue = StyleValueConverter.Convert(convertedRaw, convertedDp.PropertyType); }
                    catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
                    {
                        throw Fail(line, ex.Message);
                    }
                }
                if (convertedValue is null || !convertedDp.PropertyType.IsInstanceOfType(convertedValue))
                    throw Fail(line, $"'{value}' is not a {convertedDp.PropertyType.Name} for template '{local}'.");
                element.SetValue(convertedDp, convertedValue);
                return;
            }
            // Attached template values ride the same attached bag the loader
            // consults (shared parser); DynamicResource refresh may hand over
            // an already-typed value, which lands as-is.
            // TextBlock.* on elements carrying the CLR property (template text
            // sizing like FontSize/FontWeight): attached set on the defining
            // type equals the local set, like WPF inheritance for the common
            // case; the bag below keeps values for elements without one.
            if (local.StartsWith("TextBlock.", StringComparison.Ordinal))
            {
                var textProp = element.GetType().GetProperty(local["TextBlock.".Length..],
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (textProp is not null && textProp.CanWrite && textProp.SetMethod?.IsPublic == true)
                {
                    object? textValue = value;
                    if (value is string textRaw)
                    {
                        try { textValue = StyleValueConverter.Convert(textRaw, textProp.PropertyType); }
                        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
                        {
                            throw Fail(line, ex.Message);
                        }
                    }
                    if (textValue is not null && textProp.PropertyType.IsInstanceOfType(textValue))
                    {
                        textProp.SetValue(element, textValue);
                        return;
                    }
                }
            }
            // Legacy leniency (converted templates predate TextElement.Foreground):
            // TextBlock.Foreground rides the same attached bag as a parsed brush.
            if (local == "TextBlock.Foreground")
            {
                if (value is string legacy)
                {
                    try { element.SetAttached(local, new SolidColorBrush(NamedColor(legacy, line))); return; }
                    catch (ArgumentException ex) { throw Fail(line, ex.Message); }
                }
                if (value is Brush legacyBrush) { element.SetAttached(local, legacyBrush); return; }
            }
            // Resource-resolved TextElement.Foreground brushes (DynamicResource
            // handover): strings keep the ParseAttachedValue brush path, but
            // typed brushes land in the attached bag like TextBlock ones.
            if (local == "TextElement.Foreground" && value is Brush elementBrush)
            {
                element.SetAttached(local, elementBrush);
                return;
            }
            object parsed = value is string raw ? ParseAttachedValue(local, raw, line) : local switch
            {
                "DockPanel.Dock" when value is Dock => value,
                "Grid.Row" or "Row" or "Grid.Column" or "Column" or "Grid.RowSpan" or "RowSpan"
                    or "Grid.ColumnSpan" or "ColumnSpan" when value is int => value,
                "Canvas.Left" or "Canvas.Top" when value is double => value,
                "Canvas.ZIndex" or "Panel.ZIndex" when value is int => value,
                "WindowChrome.IsHitTestVisibleInChrome"
                    or "FocusManager.IsFocusScope" when value is bool => value,
                    "Grid.IsSharedSizeScope" when value is bool => value,
                "KeyboardNavigation.IsTabStop" when value is bool => value,
                "KeyboardNavigation.TabIndex" when value is int => value,
                "KeyboardNavigation.TabNavigation" or "KeyboardNavigation.DirectionalNavigation" when value is KeyboardNavigationMode => value,
                "ScrollViewer.CanContentScroll" when value is bool => value,
                "ScrollViewer.HorizontalScrollBarVisibility" or "ScrollViewer.VerticalScrollBarVisibility" when value is ScrollBarVisibility => value,
                "ScrollViewer.IsDeferredScrollingEnabled" when value is bool => value,
                "RenderOptions.EdgeMode" when value is EdgeMode => value,
                "ToolTipService.InitialShowDelay" when value is int => value,
                "AutomationProperties.HeadingLevel" when value is AutomationHeadingLevel => value,
                "AutomationProperties.AutomationId" when value is string => value,
                "TextBlock.FontSize" when value is double => value,
                "TextBlock.FontWeight" when value is FontWeight => value,
                "TextElement.FontSize" when value is double => value,
                "TextElement.FontWeight" when value is FontWeight => value,
                "Validation.ErrorTemplate" when value is ControlTemplate => value,
                _ => throw Fail(line, $"Unknown attached property '{local}' on template <{element.GetType().Name}>."),
            };
            // RenderOptions.EdgeMode lands in the DP store too (converted
            // stroke setup and template inflation read the DP; the bag keeps
            // legacy readers working, like the ScrollViewer knobs).
            if (local == "RenderOptions.EdgeMode" && parsed is EdgeMode edge)
                element.SetValue(RenderOptions.EdgeModeProperty, edge);
            // ClearType hint lands in the DP store too (converted code reads
            // RenderOptions.GetClearTypeHint; the bag keeps legacy readers).
            if (local == "RenderOptions.ClearTypeHint" && parsed is ClearTypeHint hint)
                element.SetValue(RenderOptions.ClearTypeHintProperty, hint);
            // TextElement font knobs land in the DP store too (attached
            // readers consult the DP, like WPF inheritance for the common
            // case; the bag above keeps legacy readers working).
            if (local == "TextElement.FontSize" && parsed is double fontSize && element is DependencyObject fontTarget)
                TextElement.SetFontSize(fontTarget, fontSize);
            if (local == "TextElement.FontWeight" && parsed is FontWeight fontWeight && element is DependencyObject weightTarget)
                TextElement.SetFontWeight(weightTarget, fontWeight);
            // The focus knobs land in the DP store too (converted code reads
            // the KeyboardNavigation statics; the bag keeps legacy readers).
            if (local == "KeyboardNavigation.IsTabStop" && parsed is bool tabStop)
                element.SetValue(KeyboardNavigation.IsTabStopProperty, tabStop);
            if (local == "KeyboardNavigation.TabNavigation" && parsed is KeyboardNavigationMode tabNavigation)
                element.SetValue(KeyboardNavigation.TabNavigationProperty, tabNavigation);
            // The scope knob lands in the DP store too (converted code reads
            // FocusManager.GetIsFocusScope; the bag keeps legacy readers).
            if (local == "FocusManager.IsFocusScope" && parsed is bool scope)
                element.SetValue(FocusManager.IsFocusScopeProperty, scope);
            // The shared-size knob lands in the DP store too (grid layout and
            // converted code read Grid.GetIsSharedSizeScope; the bag keeps readers).
            if (local == "Grid.IsSharedSizeScope" && parsed is bool sharedScope)
                element.SetValue(Grid.IsSharedSizeScopeProperty, sharedScope);
            // The validation error adorner lands in the DP store too
            // (converted code reads Validation.GetErrorTemplate; the bag
            // keeps template readers, like the focus knobs).
            if (local == "Validation.ErrorTemplate" && parsed is ControlTemplate errorTemplate)
                Validation.SetErrorTemplate(element, errorTemplate);
            // The paint-order knob lands in the DP store too (converted code
            // reads Panel.GetZIndex; the bag keeps template readers, like
            // the Canvas offsets).
            if ((local is "Canvas.ZIndex" or "Panel.ZIndex") && parsed is int zIndex)
                element.SetValue(Panel.ZIndexProperty, zIndex);
            element.SetAttached(local, parsed);
            return;
        }
        var property = element.GetType().GetProperty(local,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            // Unprefixed attached properties (Grid.IsSharedSizeScope on a Grid):
            // the owner's DP resolves by name and converts, like WPF.
            if (property is null)
            {
                var attachedDp = DependencyProperty.FromName(element.GetType(), local);
                if (attachedDp is not null)
                {
                    object? attachedValue = value;
                    if (value is string attachedRaw)
                    {
                        try { attachedValue = StyleValueConverter.Convert(attachedRaw, attachedDp.PropertyType); }
                        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
                        {
                            throw Fail(line, ex.Message);
                        }
                    }
                    if (attachedValue is not null && attachedDp.PropertyType.IsInstanceOfType(attachedValue))
                    {
                        element.SetValue(attachedDp, attachedValue);
                        element.SetAttached(element.GetType().Name + "." + local, attachedValue);
                        return;
                    }
                    throw Fail(line, $"'{value}' is not a {attachedDp.PropertyType.Name} for template '{local}'.");
                }
            }
        if (property is null || !property.CanWrite || property.SetMethod?.IsPublic != true)
            throw Fail(line, $"'{local}' is not a settable property of template <{element.GetType().Name}>.");
        try
        {
            object converted;
            if (value is string raw && property.PropertyType == typeof(ICommand))
                converted = ResolveStaticCommand(raw, line);
            else if (value is string text)
                converted = StyleValueConverter.Convert(text, property.PropertyType)
                    ?? throw Fail(line, $"'{value}' is not a {property.PropertyType.Name} for template '{local}'.");
            // Theme dictionaries store <SolidColorBrush> entries as packed
            // Color/uint values; template Brush targets take them as brushes
            // (mirrors the style-setter resource coercion).
            else if (value is Color color && property.PropertyType != typeof(object) &&
                property.PropertyType.IsAssignableFrom(typeof(SolidColorBrush)))
                converted = new SolidColorBrush(color);
            else if (value is uint rgba && property.PropertyType != typeof(object) &&
                property.PropertyType.IsAssignableFrom(typeof(SolidColorBrush)))
                converted = new SolidColorBrush(StyleValueConverter.ColorFromRgba(rgba));
            else if (value is not null && property.PropertyType.IsInstanceOfType(value))
                converted = value;
            else if (value is null && (Nullable.GetUnderlyingType(property.PropertyType) is not null ||
                !property.PropertyType.IsValueType))
                converted = null!; // x:Null clears reference targets (value targets fail below).
            else
                throw Fail(line, $"'{value}' is not a {property.PropertyType.Name} for template '{local}'.");
            property.SetValue(element, converted);
        }
        catch (FormatException ex) { throw Fail(line, ex.Message); }
        catch (ArgumentException ex) { throw Fail(line, ex.Message); }
        catch (InvalidOperationException ex) { throw Fail(line, ex.Message); }
    }

    /// <summary>Reads a template MultiBinding (ElementName/Path children plus Converter).</summary>
    private static MultiBinding ReadTemplateMultiBinding(XElement node, IXmlLineInfo line)
    {
        var multi = new MultiBinding();
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (attribute.Name.NamespaceName == XamlNamespace) continue;
            var raw = attribute.Value.Trim();
            switch (attribute.Name.LocalName)
            {
                case "Converter":
                    multi.Converter = ResolveTemplateMultiConverter(raw, line);
                    if (multi.Converter is null) multi.DeferredConverterKey = ResolveTemplateConverterKey(raw, line);
                    continue;
                case "ConverterParameter": multi.ConverterParameter = raw; continue;
                case "StringFormat": multi.StringFormat = raw; continue;
                case "Mode":
                    multi.Mode = raw switch
                    {
                        "OneWay" => BindingMode.OneWay,
                        "OneTime" => BindingMode.OneTime,
                        "TwoWay" => BindingMode.TwoWay,
                        "OneWayToSource" => BindingMode.OneWayToSource,
                        _ => throw Fail(line, $"'{raw}' is not a MultiBinding mode (OneWay/OneTime/TwoWay/OneWayToSource)."),
                    };
                    continue;
                case "NotifyOnValidationError": multi.NotifyOnValidationError = ParseBoolValue("NotifyOnValidationError", raw, line); continue;
                case "ValidatesOnDataErrors": multi.ValidatesOnDataErrors = ParseBoolValue("ValidatesOnDataErrors", raw, line); continue;
                case "TargetNullValue": multi.TargetNullValue = ParseNestedFallback(raw, raw, line); continue;
                default: throw Fail(line, $"MultiBinding option '{attribute.Name.LocalName}' is not supported (Converter/ConverterParameter/Mode/StringFormat/NotifyOnValidationError/ValidatesOnDataErrors/TargetNullValue).");
            }
        }
        foreach (var child in node.Elements())
        {
            if (child.Name.LocalName != "Binding")
                throw Fail(line, $"MultiBinding takes <Binding> children (got <{child.Name.LocalName}>).");
            multi.Bindings.Add(ReadTemplateMultiChild(child, line));
        }
        if (multi.Bindings.Count == 0)
            throw Fail(line, "MultiBinding needs at least one <Binding> child.");
        return multi;
    }

    /// <summary>Resolves a template MultiBinding Converter (x:Static instance now, StaticResource key deferred).</summary>
    private static IMultiValueConverter? ResolveTemplateMultiConverter(string raw, IXmlLineInfo line)
    {
        var text = raw.Trim();
        if (text.StartsWith("{x:Static ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            var member = ResolveStaticMember(text[1..^1]["x:Static".Length..].Trim(), raw, line);
            if (member is IMultiValueConverter resolved) return resolved;
            throw Fail(line, $"Converter '{raw}' must name an IMultiValueConverter singleton.");
        }
        return null;
    }

    /// <summary>Reads a StaticResource converter key for template bindings.</summary>
    private static string ResolveTemplateConverterKey(string raw, IXmlLineInfo line)
    {
        var text = raw.Trim();
        if (text.StartsWith("{StaticResource ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            var key = text["{StaticResource ".Length..^1].Trim();
            if (key.Length != 0 && !key.Contains('{') && !key.Contains('=')) return key;
        }
        throw Fail(line, $"Converter '{raw}' takes {{x:Static ...}} or {{StaticResource Key}}.");
    }

    /// <summary>Reads one template MultiBinding child (ElementName/Path plus the shared option subset).</summary>
    private static Binding ReadTemplateMultiChild(XElement child, IXmlLineInfo line)
    {
        var binding = new Binding();
        foreach (var attribute in child.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (attribute.Name.NamespaceName == XamlNamespace) continue;
            var raw = attribute.Value.Trim();
            switch (attribute.Name.LocalName)
            {
                case "ElementName": binding.ElementName = raw; continue;
                case "Path": binding.Path = raw; continue;
                case "Source":
                    if (raw.StartsWith("{x:Static ", StringComparison.Ordinal) && raw.EndsWith('}'))
                    {
                        // Static-source template children (DialogHost UnsetValue
                        // fallbacks): the member evaluates now, like Converter.
                        binding.Source = ResolveStaticMember(raw["{x:Static ".Length..^1].Trim(), raw, line);
                        continue;
                    }
                    if (raw.StartsWith("{StaticResource ", StringComparison.Ordinal) && raw.EndsWith('}'))
                    {
                        // Resource-source template children (PasswordBox reveal
                        // bindings): the key defers to evaluate time, when theme
                        // resources are complete, like deferred converters.
                        var sourceKey = raw["{StaticResource ".Length..^1].Trim();
                        if (sourceKey.Length != 0 && !sourceKey.Contains('{') && !sourceKey.Contains('='))
                        {
                            binding.DeferredSourceKey = sourceKey;
                            continue;
                        }
                    }
                    if (raw.StartsWith('{'))
                        throw Fail(line, $"Binding Source '{raw}' takes a literal, {{x:Static ...}} or {{StaticResource Key}} (markup sources stay a precise boundary).");
                    binding.Source = raw;
                    continue;
                case "RelativeSource": binding.RelativeSource = ParseRelativeSource(raw, line); continue;
                case "Converter":
                    if (raw.StartsWith("{x:Static ", StringComparison.Ordinal) && raw.EndsWith('}'))
                    {
                        var member = ResolveStaticMember(raw[1..^1]["x:Static".Length..].Trim(), raw, line);
                        if (member is IValueConverter resolved)
                        {
                            binding.Converter = resolved;
                            continue;
                        }
                        throw Fail(line, $"Converter '{raw}' must name an IValueConverter singleton.");
                    }
                    binding.DeferredConverterKey = ResolveTemplateConverterKey(raw, line);
                    continue;
                case "ConverterParameter": binding.ConverterParameter = raw; continue;
                case "FallbackValue": binding.FallbackValue = ParseNestedFallback(raw, raw, line); continue;
                case "TargetNullValue": binding.TargetNullValue = ParseNestedFallback(raw, raw, line); continue;
                case "StringFormat": binding.StringFormat = raw; continue;
                case "UpdateSourceTrigger":
                    binding.UpdateSourceTrigger = raw switch
                    {
                        "Default" => UpdateSourceTrigger.Default,
                        "PropertyChanged" => UpdateSourceTrigger.PropertyChanged,
                        "LostFocus" => UpdateSourceTrigger.LostFocus,
                        "Explicit" => UpdateSourceTrigger.Explicit,
                        _ => throw Fail(line, $"'{raw}' is not an UpdateSourceTrigger (Default/PropertyChanged/LostFocus/Explicit)."),
                    };
                    continue;
                case "NotifyOnValidationError": binding.NotifyOnValidationError = ParseBoolValue("NotifyOnValidationError", raw, line); continue;
                case "Mode":
                    binding.Mode = raw switch
                    {
                        "OneWay" => BindingMode.OneWay,
                        "OneTime" => BindingMode.OneTime,
                        "TwoWay" => BindingMode.TwoWay,
                        "Default" => BindingMode.Default,
                        "OneWayToSource" => BindingMode.OneWayToSource,
                        _ => throw Fail(line, $"'{raw}' is not a binding mode (OneWay/OneTime/TwoWay)."),
                    };
                    continue;
                default: throw Fail(line, $"Binding option '{attribute.Name.LocalName}' is not supported in template MultiBinding (ElementName/Path/RelativeSource/Converter/ConverterParameter/FallbackValue/Mode).");
            }
        }
        return binding;
    }

    /// <summary>
    /// Reads one template MouseBinding/KeyBinding (DialogHost click-away):
    /// x:Static commands resolve now, TemplateBinding command parameters
    /// defer their source name to inflation, MouseAction/Key/Modifiers parse
    /// as enums; anything else stays a precise boundary.
    /// </summary>
    private static TemplateInputBinding ReadTemplateInputBinding(XElement node, IXmlLineInfo line)
    {
        InputBinding binding = node.Name.LocalName switch
        {
            "MouseBinding" => new MouseBinding(),
            "KeyBinding" => new KeyBinding(),
            _ => throw Fail(line, $"<{node.Name.LocalName}> is not a supported template input binding (MouseBinding/KeyBinding)."),
        };
        string? parameterSource = null;
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            var raw = attribute.Value.Trim();
            switch (attribute.Name.LocalName)
            {
                case "Command":
                    if (!raw.StartsWith("{x:Static ", StringComparison.Ordinal) || !raw.EndsWith('}'))
                        throw Fail(line, $"InputBinding Command '{raw}' takes {{x:Static ...}} (resource commands stay a boundary).");
                    var command = ResolveStaticMember(raw["{x:Static ".Length..^1].Trim(), raw, line);
                    if (command is not ICommand inputCommand)
                        throw Fail(line, $"InputBinding Command '{raw}' is not an ICommand.");
                    binding.Command = inputCommand;
                    break;
                case "CommandParameter":
                    if (raw.StartsWith("{TemplateBinding ", StringComparison.Ordinal) && raw.EndsWith('}'))
                    {
                        var segment = raw["{TemplateBinding ".Length..^1].Trim().Split(',', 2)[0].Trim();
                        if (segment.StartsWith("Property=", StringComparison.Ordinal))
                            segment = segment["Property=".Length..].Trim();
                        if (segment.Length == 0 || segment.Contains('{') || segment.Contains('='))
                            throw Fail(line, $"'{raw}' is not a TemplateBinding (needs a property name).");
                        parameterSource = segment;
                    }
                    else
                        binding.CommandParameter = raw;
                    break;
                case "MouseAction" when binding is MouseBinding mouse:
                    if (!Enum.TryParse<MouseAction>(raw, ignoreCase: true, out var action))
                        throw Fail(line, $"MouseAction '{raw}' is not a MouseAction.");
                    mouse.MouseAction = action;
                    break;
                case "Key" when binding is KeyBinding keys:
                    if (!Enum.TryParse<Key>(raw, ignoreCase: true, out var key))
                        throw Fail(line, $"Key '{raw}' is not a Key.");
                    keys.Key = key;
                    break;
                case "Modifiers" when binding is KeyBinding modified:
                    if (!Enum.TryParse<ModifierKeys>(raw, ignoreCase: true, out var modifiers))
                        throw Fail(line, $"Modifiers '{raw}' is not a ModifierKeys combination.");
                    modified.Modifiers = modifiers;
                    break;
                default:
                    throw Fail(line, $"InputBinding option '{attribute.Name.LocalName}' is not supported (Command/CommandParameter/MouseAction/Key/Modifiers).");
            }
        }
        if (node.Elements().Any())
            throw Fail(line, $"<{node.Name.LocalName}> takes attributes only.");
        return new TemplateInputBinding { Binding = binding, CommandParameterSource = parameterSource };
    }

    /// <summary>Reads one template property element (brush, transform, clip, definitions or Triggers wrapper).</summary>
    /// <summary>Reads one template track part (typed RepeatButton/Thumb child).</summary>
    private static T ReadTrackPart<T>(XElement node, Dictionary<string, object> names, List<(object Element, string Property)> bindings, List<TemplateBindingEntry> entries, ControlTemplate template, IXmlLineInfo line)
        where T : UIElement
    {
        var part = ReadTemplateElement(node, names, bindings, entries, template, line);
        if (part is T typed) return typed;
        throw Fail(line, $"Track parts hold a <{typeof(T).Name}> (got <{part.GetType().Name}>).");
    }

    private static void ReadTemplateProperty(
        UIElement element, string property, XElement node,
        Dictionary<string, object> names, List<(object Element, string Property)> bindings,
        List<TemplateBindingEntry> entries, ControlTemplate template, IXmlLineInfo line)
    {
        var child = node.Elements().FirstOrDefault();
        if (property.Contains('.'))
        {
            // Owner-qualified template property elements (wpf:SmartHint.Hint
            // content on the hint host): the prefix selects the owner like
            // WPF, then one child element or literal text lands on the DP.
            ReadTemplateOwnerProperty(element, property, node, child, names, bindings, entries, template, line);
            return;
        }
        switch (property)
        {
            case "Stroke" when element is Rectangle rectangle:
                rectangle.Stroke = child is null ? ParseInlineBrushText(node, line) : ReadInlineBrush(child, line);
                return;
            case "Fill" when element is Rectangle fillRectangle:
                fillRectangle.Fill = child is null ? ParseInlineBrushText(node, line) : ReadInlineBrush(child, line);
                return;
            case "RenderTransform" when element is FrameworkElement transformed:
            {
                // Bound whole render transforms (SmartHint floating hint
                // composites produce the transform): a nested <Binding> or
                // <MultiBinding> defers like bound backgrounds; transform
                // elements parse inline as before.
                if (child is not null && child.Name.LocalName == "Binding")
                {
                    if (node.Elements().Skip(1).Any())
                        throw Fail(line, $"<{element.GetType().Name}.RenderTransform> takes one binding element.");
                    var single = ReadTemplateMultiChild(child, line);
                    string? singlePath = single.Path;
                    if (string.IsNullOrEmpty(singlePath)) singlePath = "RenderTransform";
                    bindings.Add((transformed, singlePath));
                    entries.Add(new TemplateBindingEntry(transformed, "RenderTransform", singlePath) { TemplateSource = single });
                    return;
                }
                if (child is not null && child.Name.LocalName == "MultiBinding")
                {
                    if (node.Elements().Skip(1).Any())
                        throw Fail(line, $"<{element.GetType().Name}.RenderTransform> takes one binding element.");
                    entries.Add(new TemplateBindingEntry(transformed, "RenderTransform", string.Empty) { MultiSource = ReadTemplateMultiBinding(child, line) });
                    return;
                }
                transformed.RenderTransform = child is null
                    ? throw Fail(line, $"<{element.GetType().Name}.RenderTransform> needs a transform element.")
                    : ReadInlineTransform(child, line, entries, transformed, names);
                return;
            }
            case "LayoutTransform" when element is FrameworkElement layoutHost:
                layoutHost.LayoutTransform = child is null
                    ? throw Fail(line, $"<{element.GetType().Name}.LayoutTransform> needs a transform element.")
                    : ReadInlineTransform(child, line, entries, layoutHost, names);
                return;
            case "Resources" when element is FrameworkElement resourced:
            {
                // Element-local template resources (badge PackIcon sizing):
                // parsed through the shared dictionary path under the
                // template root's declarations, merged under the element's
                // own resources (which keep precedence, like WPF).
                var scope = TemplateRootOf(node);
                var resources = new ResourceDictionary();
                ReadTemplateResources(scope, node, resources, line);
                resourced.Resources.AddMerged(resources);
                return;
            }
            case "OpacityMask" when element is Rectangle masked:
                masked.OpacityMask = child is null
                    ? throw Fail(line, $"<{element.GetType().Name}.OpacityMask> needs a brush element.")
                    : ReadInlineBrush(child, line);
                return;
            case "OpacityMask" when element is FrameworkElement opacityHost:
            {
                // Bound-mask template content (DialogHost cover grid): a
                // VisualBrush whose Visual is a MultiBinding resolves at
                // inflation; other shapes keep the precise error.
                if (child is null || child.Name.LocalName != "VisualBrush" || node.Elements().Skip(1).Any())
                {
                    if (child is not null && child.Name.LocalName == "MultiBinding" && !node.Elements().Skip(1).Any())
                    {
                        // Bound template masks (MaterialDesign Card/ElevationAssist shadow mask):
                        // the vendor converter produces the mask Brush at inflation, like bound effects.
                        entries.Add(new TemplateBindingEntry(opacityHost, "OpacityMask", string.Empty)
                            { MaskBinding = new TemplateMaskBinding { Multi = ReadTemplateMultiBinding(child, line) } });
                        return;
                    }
                    throw Fail(line, $"<{element.GetType().Name}.OpacityMask> takes one <VisualBrush>.");}
                var (maskBrush, maskVisualName) = ReadTemplateVisualBrush(child, line);
                if (maskVisualName is not null)
                    throw Fail(line, $"<{element.GetType().Name}.OpacityMask> takes a <VisualBrush.Visual> MultiBinding (ElementName visuals stay a boundary).");
                var visualNode = child.Elements().FirstOrDefault()
                    ?? throw Fail(line, $"<{element.GetType().Name}.OpacityMask> <VisualBrush> needs <VisualBrush.Visual>.");
                if (visualNode.Name.LocalName != "VisualBrush.Visual" || child.Elements().Skip(1).Any())
                    throw Fail(line, $"<{element.GetType().Name}.OpacityMask> <VisualBrush> takes one <VisualBrush.Visual>.");
                var visualChild = visualNode.Elements().FirstOrDefault();
                if (visualChild is null || visualChild.Name.LocalName != "MultiBinding" || visualNode.Elements().Skip(1).Any())
                    throw Fail(line, $"<{element.GetType().Name}.OpacityMask> <VisualBrush.Visual> takes one <MultiBinding>.");
                entries.Add(new TemplateBindingEntry(opacityHost, "OpacityMask", string.Empty)
                    { BrushVisualBinding = new TemplateBrushVisualBinding { Brush = maskBrush, Multi = ReadTemplateMultiBinding(visualChild, line) } });
                return;
            }
            case "RowDefinitions" when element is Grid rowGrid:
                ReadTemplateGridDefinitions(rowGrid.RowDefinitions, rowGrid, entries, node, "RowDefinition", "Height", "MinHeight", "MaxHeight", line);
                return;
            case "ColumnDefinitions" when element is Grid columnGrid:
                ReadTemplateGridDefinitions(columnGrid.ColumnDefinitions, columnGrid, entries, node, "ColumnDefinition", "Width", "MinWidth", "MaxWidth", line);
                return;
            case "Triggers":
                foreach (var trigger in node.Elements())
                    template.Triggers.Add(ReadTemplateTrigger(trigger, element.GetType(), line, null, TriggerScopeResolver(null)));
                return;
            case "Background":
            {
                var backgroundProp = element.GetType().GetProperty("Background",
                    BindingFlags.Public | BindingFlags.Instance);
                if (backgroundProp is null || !backgroundProp.CanWrite || backgroundProp.SetMethod?.IsPublic != true ||
                    !backgroundProp.PropertyType.IsAssignableFrom(typeof(Brush)))
                    break;
                var brushChild = node.Elements().FirstOrDefault();
                if (brushChild is null)
                {
                    backgroundProp.SetValue(element, ParseInlineBrushText(node, line));
                    return;
                }
                if (node.Elements().Skip(1).Any())
                    throw Fail(line, $"<{element.GetType().Name}.Background> takes one brush element.");
                if (brushChild.Name.LocalName == "Binding")
                {
                    // Bound template backgrounds (ComboBoxPopup.Background
                    // falls back across templated-parent and popup brushes):
                    // resolve live at inflation like other template bindings.
                    entries.Add(new TemplateBindingEntry(element, "Background", string.Empty) { TemplateSource = ReadTemplateMultiChild(brushChild, line) });
                    return;
                }
                if (brushChild.Name.LocalName == "MultiBinding")
                {
                    entries.Add(new TemplateBindingEntry(element, "Background", string.Empty) { MultiSource = ReadTemplateMultiBinding(brushChild, line) });
                    return;
                }
                if (brushChild.Name.LocalName == "VisualBrush")
                {
                    var (visualBrush, visualName) = ReadTemplateVisualBrush(brushChild, line);
                    backgroundProp.SetValue(element, visualBrush);
                    if (visualName is not null)
                        entries.Add(new TemplateBindingEntry(element, "Background", string.Empty) { BrushVisualName = visualName });
                    return;
                }
                backgroundProp.SetValue(element, ReadInlineBrush(brushChild, line));
                return;
            }
            case "Header" when element is Expander expanderHeader:
                var headerChild = node.Elements().FirstOrDefault();
                expanderHeader.Header = headerChild is null
                    ? node.Value.Trim()
                    : ReadTemplateElement(headerChild, names, bindings, entries, template, line);
                return;
            case "DecreaseRepeatButton" when element is Track trackDecrease:
            {
                // Template track parts (ScrollBar groove buttons): the
                // RepeatButton child parses through the shared template
                // path and parents like native visual children.
                var decreaseChild = node.Elements().FirstOrDefault()
                    ?? throw Fail(line, "<Track.DecreaseRepeatButton> needs a RepeatButton element.");
                if (node.Elements().Skip(1).Any())
                    throw Fail(line, "<Track.DecreaseRepeatButton> holds one element.");
                trackDecrease.DecreaseRepeatButton = ReadTrackPart<RepeatButton>(decreaseChild, names, bindings, entries, template, line);
                return;
            }
            case "IncreaseRepeatButton" when element is Track trackIncrease:
            {
                var increaseChild = node.Elements().FirstOrDefault()
                    ?? throw Fail(line, "<Track.IncreaseRepeatButton> needs a RepeatButton element.");
                if (node.Elements().Skip(1).Any())
                    throw Fail(line, "<Track.IncreaseRepeatButton> holds one element.");
                trackIncrease.IncreaseRepeatButton = ReadTrackPart<RepeatButton>(increaseChild, names, bindings, entries, template, line);
                return;
            }
            case "Thumb" when element is Track trackThumb:
            {
                var thumbChild = node.Elements().FirstOrDefault()
                    ?? throw Fail(line, "<Track.Thumb> needs a Thumb element.");
                if (node.Elements().Skip(1).Any())
                    throw Fail(line, "<Track.Thumb> holds one element.");
                trackThumb.Thumb = ReadTrackPart<Thumb>(thumbChild, names, bindings, entries, template, line);
                return;
            }
            case "Data" when element is Path pathElement:
            {
                // Template path data (the circular progress ring binds its
                // figure coordinates): static geometries parse inline, bound
                // ones defer through Freezable entries that re-measure.
                var dataChild = node.Elements().FirstOrDefault()
                    ?? throw Fail(line, "<Path.Data> needs a geometry element.");
                if (node.Elements().Skip(1).Any())
                    throw Fail(line, "<Path.Data> holds one geometry element.");
                if (HasTemplateGeometryBindings(dataChild))
                    ReadBoundTemplateGeometry(pathElement, dataChild, entries, line);
                else
                    pathElement.Data = ReadGeometryNode(dataChild, line);
                return;
            }
            case "Clip":
                var clipChild = node.Elements().FirstOrDefault()
                    ?? throw Fail(line, $"<{element.GetType().Name}.Clip> needs a geometry or MultiBinding element.");
                if (clipChild.Name.LocalName == "MultiBinding")
                {
                    entries.Add(new TemplateBindingEntry(element, "Clip", string.Empty) { MultiSource = ReadTemplateMultiBinding(clipChild, line) });
                    return;
                }
                element.Clip = ReadGeometryNode(clipChild, line);
                return;
            case "Template" when element is Control controlElement:
            {
                // Nested control templates (the Chip icon ContentControl
                // carries its own ControlTemplate): one ControlTemplate
                // child parses through the shared template path and lands
                // on Control.Template; inflation rebuilds it from the
                // retained markup, like outer templates.
                var templateChild = node.Elements().FirstOrDefault()
                    ?? throw Fail(line, $"<{element.GetType().Name}.Template> needs a ControlTemplate element.");
                if (templateChild.Name.LocalName != "ControlTemplate" || node.Elements().Skip(1).Any())
                    throw Fail(line, $"<{element.GetType().Name}.Template> takes one ControlTemplate element.");
                controlElement.Template = ReadControlTemplate(templateChild, element.GetType(), line);
                return;
            }
            case "Style" when element is FrameworkElement:
            {
                // Nested element styles (the Clock readout TextBlocks carry
                // their own opacity triggers): one Style child parses through
                // the shared style path and applies at inflation once the
                // tree links, so template-local BasedOn resolves, like WPF.
                var styleChild = node.Elements().FirstOrDefault()
                    ?? throw Fail(line, $"<{element.GetType().Name}.Style> needs a Style element.");
                if (styleChild.Name.LocalName != "Style" || node.Elements().Skip(1).Any())
                    throw Fail(line, $"<{element.GetType().Name}.Style> takes one Style element.");
                entries.Add(new TemplateBindingEntry(element, "Style", string.Empty) { DeferredStyle = ReadStyleNode(styleChild, line) });
                return;
            }
            case "Effect":
            {
                // Effects on template parts (the ComboBox drop-down Border
                // blur): one DropShadowEffect or BlurEffect child parses
                // through the shared literal / {Binding} attribute path and
                // lands on UIElement.Effect, like the element
                // property-element path.
                var effectChild = node.Elements().FirstOrDefault();
                if (effectChild is null)
                {
                    element.Effect = null;
                    return;
                }
                if (effectChild is not null && effectChild.Name.LocalName == "MultiBinding")
                {
                    // Bound template effects (DialogHost background blur):
                    // the vendor converter produces the Effect at inflation;
                    // only one value element, like the literal arms.
                    if (node.Elements().Skip(1).Any())
                        throw Fail(line, $"<{element.GetType().Name}.Effect> takes one MultiBinding.");
                    entries.Add(new TemplateBindingEntry(element, "Effect", string.Empty)
                        { EffectBinding = new TemplateEffectBinding { Multi = ReadTemplateMultiBinding(effectChild, line) } });
                    return;
                }
                if ((effectChild.Name.LocalName != "DropShadowEffect" && effectChild.Name.LocalName != "BlurEffect") || node.Elements().Skip(1).Any())
                    throw Fail(line, $"<{element.GetType().Name}.Effect> needs one DropShadowEffect, BlurEffect or MultiBinding element.");
                Effect effect = effectChild.Name.LocalName == "BlurEffect" ? new BlurEffect() : new DropShadowEffect();
                ApplyEffectAttributes(element, effect, effectChild, line);
                element.Effect = effect;
                return;
            }
            case "Tag" when element is FrameworkElement tagHost:
            {
                // Plain Tag template content (SmartHint scale hosts carry
                // a system:Double): Tag is CLR-only in the engine, so text
                // lands as a string and one system value converts through
                // its converter, like the attribute path.
                if (child is null)
                {
                    tagHost.Tag = node.Value.Trim();
                    return;
                }
                if (node.Elements().Skip(1).Any())
                    throw Fail(line, $"<{element.GetType().Name}.Tag> holds one value element (system values stay precise otherwise).");
                tagHost.Tag = ReadSystemValue(child, line);
                return;
            }
            case "Content" when element is ContentControl contentHost:
                // Converted template content (OxyPlot TrackerControl.Content):
                // one nested visual becomes the content, like XAML content.
                var contentChild = node.Elements().FirstOrDefault();
                contentHost.Content = contentChild is null
                    ? node.Value.Trim()
                    : ReadTemplateElement(contentChild, names, bindings, entries, template, line);
                return;
            case "InputBindings" when element is UIElement inputHost:
            {
                // Template click-away bindings (DialogHost MouseBinding):
                // each MouseBinding/KeyBinding parses now; TemplateBinding
                // command parameters resolve at inflation, like TemplateSource.
                var inputs = new List<TemplateInputBinding>();
                foreach (var bindingNode in node.Elements())
                    inputs.Add(ReadTemplateInputBinding(bindingNode, line));
                if (inputs.Count == 0)
                    throw Fail(line, $"<{element.GetType().Name}.InputBindings> needs at least one <MouseBinding> or <KeyBinding>.");
                entries.Add(new TemplateBindingEntry(inputHost, "InputBindings", string.Empty) { InputBindings = inputs });
                return;
            }
            case "ItemTemplate" when element is ItemsControl itemsHost:
            {
                // Template items views (the DataGrid validation summary lists
                // Validation.Errors through a DataTemplate): one DataTemplate
                // child parses through the shared data-template path, like
                // the runtime ItemsControl.ItemTemplate reader.
                var itemTemplateChild = node.Elements().FirstOrDefault()
                    ?? throw Fail(line, $"<{element.GetType().Name}.ItemTemplate/> is empty (a <DataTemplate> is required).");
                if (itemTemplateChild.Name.LocalName is not ("DataTemplate" or "HierarchicalDataTemplate") || node.Elements().Skip(1).Any())
                    throw Fail(line, $"<{element.GetType().Name}.ItemTemplate> needs one nested <DataTemplate>.");
                using var itemTemplateReader = itemTemplateChild.CreateReader();
                itemTemplateReader.Read();
                itemsHost.ItemTemplate = ReadDataTemplateElement(itemTemplateReader, line);
                return;
            }
            case "ItemsPanel" when element is ItemsControl itemsPanelHost:
            {
                // Template items panels (the RatingBar rows lay out through
                // an ItemsPanelTemplate): one ItemsPanelTemplate child parses
                // through the shared reader, like ItemTemplate above.
                var panelChild = node.Elements().FirstOrDefault()
                    ?? throw Fail(line, $"<{element.GetType().Name}.ItemsPanel/> is empty (an <ItemsPanelTemplate> is required).");
                if (panelChild.Name.LocalName != "ItemsPanelTemplate" || node.Elements().Skip(1).Any())
                    throw Fail(line, $"<{element.GetType().Name}.ItemsPanel> needs one nested <ItemsPanelTemplate>.");
                using var panelReader = panelChild.CreateReader();
                panelReader.Read();
                itemsPanelHost.ItemsPanel = ReadItemsPanelTemplateElement(panelReader, line);
                return;
            }
        }
        // Binding-valued template properties (the Calendar date display binds
        // DisplayDate through a nested MultiBinding): a nested <Binding> or
        // <MultiBinding> becomes a deferred template entry like the Clip arm.
        var bindingChild = node.Elements().FirstOrDefault();
        if (bindingChild is not null && bindingChild.Name.LocalName == "Binding")
        {
            var single = ReadTemplateMultiChild(bindingChild, line);
            string? singlePath = single.Path;
            if (string.IsNullOrEmpty(singlePath)) singlePath = property;
            bindings.Add((element, singlePath));
            entries.Add(new TemplateBindingEntry(element, property, singlePath) { TemplateSource = single });
            return;
        }
        if (bindingChild is not null && bindingChild.Name.LocalName == "MultiBinding")
        {
            entries.Add(new TemplateBindingEntry(element, property, string.Empty) { MultiSource = ReadTemplateMultiBinding(bindingChild, line) });
            return;
        }
        if (bindingChild is not null && bindingChild.Name.LocalName == "PriorityBinding")
        {
            // Priority fallback chains in templates (the DatePicker thickness
            // overrides): defer to inflation like other template bindings,
            // when the inflated parent chain resolves TemplatedParent.
            entries.Add(new TemplateBindingEntry(element, property, string.Empty) { PrioritySource = ReadTriggerPriorityBinding(bindingChild, line) });
            return;
        }
        // Same-type template content (SmartHint.Hint on the hint host):
        // one child element or literal text lands on the element's own DP.
        var ownDp = DependencyProperty.FromName(element.GetType(), property);
        if (ownDp is not null)
        {
            var valueChild = node.Elements().FirstOrDefault();
            object? ownValue;
            if (valueChild is null)
                ownValue = StyleValueConverter.Convert(node.Value.Trim(), ownDp.PropertyType)
                    ?? throw Fail(line, $"'{node.Value.Trim()}' does not convert to {ownDp.PropertyType.Name} for '{property}'.");
            else
            {
                if (node.Elements().Skip(1).Any())
                    throw Fail(line, $"<{element.GetType().Name}.{property}> holds one value element (collections stay a boundary).");
                ownValue = ReadTemplateElement(valueChild, names, bindings, entries, template, line);
            }
            element.SetValue(ownDp, ownValue);
            return;
        }
        throw Fail(line, $"<{element.GetType().Name}.{property}> is not a supported template property (Stroke/Fill/RenderTransform/LayoutTransform/OpacityMask/Resources/RowDefinitions/ColumnDefinitions/Triggers/Background/Header/Clip/Data/DecreaseRepeatButton/IncreaseRepeatButton/Thumb/Template/Style/Content/Effect/ItemTemplate/InputBindings).");
    }

    /// <summary>Reads one system primitive value element
    /// (&lt;system:Double&gt;/&lt;system:String&gt;/&lt;system:Int32&gt;/&lt;system:Boolean&gt;),
    /// like template Tag content carries.</summary>
    private static object ReadSystemValue(XElement node, IXmlLineInfo line)
    {
        var text = node.Value.Trim();
        return node.Name.LocalName switch
        {
            "Double" => ParseDoubleValue("system:Double", text, line),
            "String" => text,
            "Int32" => ParseIntValue("system:Int32", text, line),
            "Boolean" => ParseBoolValue("system:Boolean", text, line),
            _ => throw Fail(line, $"<{node.Name.LocalName}> is not a supported system value (Double/String/Int32/Boolean)."),
        };
    }

    private static void SetTemplateDefinitionLength(object definition, GridLength length)
    {
        if (definition is RowDefinition row) row.Height = length;
        else ((ColumnDefinition)definition).Width = length;
    }

    private static void SetTemplateDefinitionMin(object definition, double value)
    {
        if (definition is RowDefinition row) row.MinHeight = value;
        else ((ColumnDefinition)definition).MinWidth = value;
    }

    private static void SetTemplateDefinitionMax(object definition, double value)
    {
        if (definition is RowDefinition row) row.MaxHeight = value;
        else ((ColumnDefinition)definition).MaxWidth = value;
    }

    /// <summary>Assigns a shared-size group (mirrors the loader definition group).</summary>
    /// <summary>Extracts the property name from a {TemplateBinding ...}
    /// form (bare or Property=; converters stay a precise boundary because
    /// definition transfers evaluate a plain TemplatedParent binding).</summary>
    private static string ParseTemplateBindingProperty(string raw, IXmlLineInfo line)
    {
        var body = raw["{TemplateBinding ".Length..^1].Trim();
        var segment = body.Split(',', 2)[0].Trim();
        var property = segment;
        if (property.StartsWith("Property=", StringComparison.Ordinal))
            property = property["Property=".Length..].Trim();
        if (property.Length != 0 && !property.Contains('{') && !property.Contains('=') && !body.Contains("Converter"))
            return property;
        throw Fail(line, $"'{raw}' is not a plain TemplateBinding property (converters need {{Binding ...}}).");
    }

    /// <summary>Assigns a shared-size group (mirrors the loader definition group).</summary>
    private static void SetTemplateDefinitionGroup(object definition, string value)
    {
        if (definition is RowDefinition row) row.SharedSizeGroup = value;
        else ((ColumnDefinition)definition).SharedSizeGroup = value;
    }

    /// <summary>Reads grid Row/ColumnDefinition entries (mirrors the loader grid definitions).</summary>
    private static void ReadTemplateGridDefinitions<T>(IList<T> definitions, Grid grid, List<TemplateBindingEntry> entries, XElement node, string want, string attribute, string minAttr, string maxAttr, IXmlLineInfo line) where T : new()
    {
        foreach (var child in node.Elements())
        {
            if (child.Name.LocalName != want)
                throw Fail(line, $"Expected <{want}> inside grid definitions (got <{child.Name.LocalName}>).");
            object definition = new T();
            foreach (var candidate in child.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                var local = candidate.Name.LocalName;
                string? dimension = local == attribute ? "Length" : local == minAttr ? "Min" : local == maxAttr ? "Max" : null;
                if (dimension is not null)
                {
                    var raw = candidate.Value.Trim();
                    if (raw.StartsWith("{TemplateBinding ", StringComparison.Ordinal) && raw.EndsWith('}'))
                    {
                        // Template-bound definition dimensions (the Slider
                        // vertical track column): a TemplatedParent transfer
                        // evaluated at inflation/refresh, like bindings.
                        var templateProp = ParseTemplateBindingProperty(raw, line);
                        entries.Add(new TemplateBindingEntry(grid, local, string.Empty)
                        {
                            DefinitionBinding = new TemplateDefinitionBinding
                            {
                                Index = definitions.Count,
                                IsRow = typeof(T) == typeof(RowDefinition),
                                Dimension = dimension,
                                Binding = new Binding
                                {
                                    Path = templateProp,
                                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                                },
                            },
                        });
                        continue;
                    }
                    if (raw.StartsWith("{Binding", StringComparison.Ordinal) && raw.EndsWith('}'))
                    {
                        // Bound definition dimensions (the DataGrid frozen-column
                        // spacer): the binding defers to inflation/refresh, like
                        // Freezable bindings, and coerces through the shared
                        // definition path.
                        entries.Add(new TemplateBindingEntry(grid, local, string.Empty)
                        {
                            DefinitionBinding = new TemplateDefinitionBinding
                            {
                                Index = definitions.Count,
                                IsRow = typeof(T) == typeof(RowDefinition),
                                Dimension = dimension,
                                Binding = ParseStandaloneBinding(raw, local, line),
                            },
                        });
                        continue;
                    }
                }
                if (dimension is not null && IsDefinitionResource(candidate.Value, out var isStatic, out var resourceKey))
                {
                    // Resource-valued definition dimensions (the ComboBox popup
                    // ColumnDefinition tracking the scroll-bar width): known keys
                    // land now, missing dynamic keys defer to inflation/refresh.
                    SetTemplateDefinitionResource(definitions, definition, definitions.Count, grid, entries, local, dimension, candidate.Value, isStatic, resourceKey, line);
                    continue;
                }
                if (candidate.Name.LocalName == minAttr)
                    SetTemplateDefinitionMin(definition, ParseDoubleValue(minAttr, candidate.Value, line));
                else if (candidate.Name.LocalName == maxAttr)
                    SetTemplateDefinitionMax(definition, ParseDoubleValue(maxAttr, candidate.Value, line));
                else if (candidate.Name.LocalName == attribute)
                {
                    try { SetTemplateDefinitionLength(definition, GridLength.Parse(candidate.Value)); }
                    catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentNullException)
                    {
                        throw Fail(line, ex.Message);
                    }
                }
                else if (candidate.Name.LocalName == "SharedSizeGroup")
                    SetTemplateDefinitionGroup(definition, candidate.Value);
                else throw Fail(line, candidate.Name.LocalName + " is not valid on <" + want + "> (only " + attribute + "/" + minAttr + "/" + maxAttr + "/SharedSizeGroup).");
            }
            definitions.Add((T)definition);
        }
    }

    /// <summary>Detects a {StaticResource ...} / {DynamicResource ...} definition value.</summary>
    private static bool IsDefinitionResource(string raw, out bool isStatic, out string key)
    {
        var text = raw.Trim();
        isStatic = false;
        key = string.Empty;
        if (text.StartsWith("{StaticResource ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            isStatic = true;
            key = text["{StaticResource ".Length..^1].Trim();
            return key.Length > 0;
        }
        if (text.StartsWith("{DynamicResource ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            key = text["{DynamicResource ".Length..^1].Trim();
            return key.Length > 0;
        }
        return false;
    }

    /// <summary>Lands a resource-valued definition dimension now or defers it
    /// (missing dynamic keys resolve at inflation/refresh, like the
    /// other template resource arms).</summary>
    private static void SetTemplateDefinitionResource<T>(IList<T> definitions, object definition, int index, Grid grid, List<TemplateBindingEntry> entries, string display, string dimension, string raw, bool isStatic, string key, IXmlLineInfo line)
    {
        object lookupKey = key;
        if (key.StartsWith("{x:Static ", StringComparison.Ordinal) && key.EndsWith('}'))
        {
            var resolved = ResolveStaticMember(key["{x:Static".Length..^1].Trim(), raw, line);
            if (resolved is not ResourceKey staticKey)
                throw Fail(line, $"'{raw}' needs a resource key (x:Static must name a ResourceKey).");
            if (SystemTheme.TryFindSystemResource(staticKey, out var system) && system is not null)
            {
                try { TemplateApplier.ApplyDefinitionValue(definition, dimension, system, display); }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException or ArgumentNullException)
                { throw Fail(line, ex.Message); }
                return;
            }
            lookupKey = staticKey;
        }
        if (Application.Current is { } app && app.Resources.TryGetValue(lookupKey, out var resource) && resource is not null)
        {
            try { TemplateApplier.ApplyDefinitionValue(definition, dimension, resource, display); }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException or ArgumentNullException)
            { throw Fail(line, ex.Message); }
            return;
        }
        if (isStatic)
            throw Fail(line, $"StaticResource '{key}' was not found in application resources (template scope).");
        entries.Add(new TemplateBindingEntry(grid, display, string.Empty)
        {
            DefinitionResource = new TemplateDefinitionResource
            {
                Index = index,
                IsRow = typeof(T) == typeof(RowDefinition),
                Dimension = dimension,
                Key = lookupKey,
            },
        });
    }

    /// <summary>Reads an owner-qualified template property element
    /// (Owner.Property with one child element or literal text): the owner
    /// resolves namespace-aware (own type, vendor xmlns tables, engine and
    /// converted short names), then the value lands on the DP, like WPF.</summary>
    private static void ReadTemplateOwnerProperty(UIElement element, string property, XElement node, XElement? child,
        Dictionary<string, object> names, List<(object Element, string Property)> bindings,
        List<TemplateBindingEntry> entries, ControlTemplate template, IXmlLineInfo line)
    {
        var dot = property.IndexOf('.');
        var ownerText = property[..dot];
        var member = property[(dot + 1)..];
        if (member.Length == 0)
            throw Fail(line, $"<{property}> is not a supported template property element.");
        if (string.Equals(ownerText, "Interaction", StringComparison.Ordinal) &&
            string.Equals(member, "Behaviors", StringComparison.Ordinal))
        {
            // Blend behaviors on a template part (TextBox PART_ContentHost
            // scroll sync): the converted Interaction surface drives the
            // attach, like the Slice-99 converted-trigger path.
            ReadTemplateInteractionBehaviors(element, node, entries, line);
            return;
        }
        Type? owner = element.GetType().Name == ownerText ? element.GetType() : null;
        if (owner is null && XmlNamespacesFor(node.Name.NamespaceName) is { } namespaces)
        {
            foreach (var ns in namespaces)
            {
                foreach (var assembly in ConvertedSearchAssemblies())
                {
                    owner = SafeGetConvertedType(assembly, ns + "." + ownerText);
                    if (owner is not null) break;
                }
                if (owner is not null) break;
            }
        }
        owner ??= FindEngineType(ownerText) ?? FindConvertedType(ownerText)
            ?? ScanUniqueConvertedType(ownerText, line, property);
        if (owner is null)
            throw Fail(line, $"'{property}' names unknown owner type '{ownerText}'.");
        // Bound owner-qualified template content (RatingBar Ripple
        // TextBlock.Foreground, Snackbar DockPanel.Dock composites on a
        // DP-less attached owner): binding children defer to inflation
        // through the attached transfer, whether or not the owner
        // declares a DP; other shapes keep the precise errors below.
            if (child is not null && child.Name.LocalName == "PriorityBinding"
                && element is FrameworkElement priorityHost)
            {
                // Attached-owner PriorityBinding template content (DatePicker
                // TextFieldAssist thickness overrides): defer to inflation
                // like other template bindings; the dotted owner name rides
                // the attached transfer at apply time.
                entries.Add(new TemplateBindingEntry(priorityHost, property, string.Empty) { PrioritySource = ReadTriggerPriorityBinding(child, line) });
                return;
            }
            if (child is not null && child.Name.LocalName == "Binding"
                && element is FrameworkElement bindingHost)
            {
                // Bound owner-qualified template content (the RatingBar
                // Ripple TextBlock.Foreground single bindings): defer to
                // inflation like the generic Binding arm; the dotted owner
                // name rides the attached transfer at apply time.
                var single = ReadTemplateMultiChild(child, line);
                string? singlePath = single.Path;
                if (string.IsNullOrEmpty(singlePath)) singlePath = property;
                bindings.Add((bindingHost, singlePath));
                entries.Add(new TemplateBindingEntry(bindingHost, property, singlePath) { TemplateSource = single });
                return;
            }
            if (child is not null && child.Name.LocalName == "MultiBinding"
                && element is FrameworkElement multiHost)
            {
                // Bound owner-qualified template composites (the RatingBar
                // Ripple TextBlock.Foreground MultiBinding): defer to
                // inflation like the generic MultiBinding arm; the dotted
                // owner name rides the attached transfer at apply time.
                entries.Add(new TemplateBindingEntry(multiHost, property, string.Empty) { MultiSource = ReadTemplateMultiBinding(child, line) });
                return;
            }
        var dp = DependencyProperty.FromName(owner, member);
        if (dp is not null)
        {
            object? value;
            if (child is null)
                value = StyleValueConverter.Convert(node.Value.Trim(), dp.PropertyType)
                    ?? throw Fail(line, $"'{node.Value.Trim()}' does not convert to {dp.PropertyType.Name} for '{property}'.");
            else
            {
                if (node.Elements().Skip(1).Any())
                    throw Fail(line, $"<{property}> holds one value element (collections stay a boundary).");
                value = ReadTemplateElement(child, names, bindings, entries, template, line);
            }
            element.SetValue(dp, value);
            return;
        }
        // CLR-owned template content (no DP): text converts in place, like
        // the attribute path; element content needs a DP to land on.
        var clr = element.GetType().GetProperty(member,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (clr is not null && clr.CanWrite && clr.SetMethod?.IsPublic == true && child is null)
        {
            element.GetType().GetProperty(member,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)!
                .SetValue(element, StyleValueConverter.Convert(node.Value.Trim(), clr.PropertyType)
                    ?? throw Fail(line, $"'{node.Value.Trim()}' does not convert to {clr.PropertyType.Name} for '{property}'."));
            return;
        }
        throw Fail(line, $"'{property}' names no dependency property on <{owner.Name}>.");
    }

    /// <summary>Converted Blend Interaction.GetBehaviors driver cache
    /// (converted Microsoft.Xaml.Behaviors images resolve once per type).</summary>
    private static readonly Dictionary<Type, System.Reflection.MethodInfo> TemplateBehaviorGetters = new();

    /// <summary>Reads an &lt;Interaction.Behaviors&gt; template property
    /// element (MaterialDesign TextBox PART_ContentHost scroll sync): each
    /// child instantiates its converted behavior, {Binding} attributes defer
    /// through FreezableBinding entries (ElementName parts and
    /// TemplatedParent resolve at inflation, like transform bindings) and
    /// literals convert in place; the attach entry lands first so the
    /// converted collection attaches before the first bound value, like WPF.</summary>
    private static void ReadTemplateInteractionBehaviors(UIElement element, XElement node, List<TemplateBindingEntry> entries, IXmlLineInfo line)
    {
        const string original = "<Interaction.Behaviors>";
        if (element is not FrameworkElement host)
            throw Fail(line, $"'{original}' attaches to a FrameworkElement (got <{element.GetType().Name}>).");
        var getBehaviors = TemplateBehaviorGetter(node.Name.NamespaceName, line, original);
        var attach = new TemplateBehaviorAttach { GetBehaviors = getBehaviors, Behaviors = new List<object>() };
        var insertAt = entries.Count;
        foreach (var behaviorNode in node.Elements())
        {
            var behaviorType = ResolveTemplateBehaviorType(behaviorNode, line, original);
            object behavior;
            try
            {
                behavior = Activator.CreateInstance(behaviorType, nonPublic: true)!;
            }
            catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or MemberAccessException)
            {
                throw Fail(line, $"'{original}' cannot construct <{behaviorNode.Name.LocalName}> ({(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}).");
            }
            if (behavior is not DependencyObject)
                throw Fail(line, $"'{original}' cannot hold <{behaviorType.FullName}> (converted behaviors are DependencyObjects).");
            foreach (var attribute in behaviorNode.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                if (IsIgnorableAttribute(attribute)) continue;
                if (attribute.Name.NamespaceName == XamlNamespace)
                    throw Fail(line, $"'{attribute.Name.LocalName}=' is not supported on <{behaviorNode.Name.LocalName}> (behavior attributes take bindings or literals).");
                var local = attribute.Name.LocalName;
                if (local.Contains('.'))
                    throw Fail(line, $"'{local}=' is not supported on <{behaviorNode.Name.LocalName}> (attached behavior attributes stay a precise boundary).");
                var raw = attribute.Value.Trim();
                if (raw.StartsWith("{Binding", StringComparison.Ordinal) && raw.EndsWith('}'))
                {
                    entries.Add(new TemplateBindingEntry(host, local, string.Empty)
                    {
                        FreezableBinding = new TemplateFreezableBinding
                        {
                            Target = (DependencyObject)behavior,
                            Owner = host,
                            Property = local,
                            Binding = ParseBehaviorBinding(raw, local, line),
                        }
                    });
                    continue;
                }
                if (raw.StartsWith('{'))
                    throw Fail(line, $"'{raw}' is not supported on behavior '{local}' (only {{Binding ...}} and literals).");
                SetBehaviorLiteralAttribute(behaviorType, behavior, local, raw, line, "behavior attributes take bindings or literals");
            }
            if (behaviorNode.Elements().Any())
                throw Fail(line, $"<{behaviorNode.Name.LocalName}> takes attributes only (nested behavior content stays a precise boundary).");
            attach.Behaviors.Add(behavior);
        }
        if (attach.Behaviors.Count != 0)
            entries.Insert(insertAt, new TemplateBindingEntry(host, "Interaction.Behaviors", string.Empty) { BehaviorAttach = attach });
    }

    private static System.Reflection.MethodInfo TemplateBehaviorGetter(string namespaceUri, IXmlLineInfo line, string original)
    {
        Type? owner = null;
        if (XmlNamespacesFor(namespaceUri) is { } namespaces)
        {
            foreach (var ns in namespaces)
            {
                foreach (var assembly in ConvertedSearchAssemblies())
                {
                    var candidate = SafeGetConvertedType(assembly, ns + ".Interaction");
                    if (candidate is null) continue;
                    if (owner is not null && !ReferenceEquals(owner, candidate))
                        throw Fail(line, $"'{original}' matches '{owner.FullName}' and '{candidate.FullName}' (update the xmlns table).");
                    owner = candidate;
                }
            }
        }
        owner ??= FindEngineType("Interaction") ?? ScanUniqueConvertedType("Interaction", line, original);
        if (owner is null)
            throw Fail(line, $"'{original}' needs a converted Blend Interaction surface (no 'Interaction' under '{namespaceUri}').");
        lock (TemplateBehaviorGetters)
        {
            if (TemplateBehaviorGetters.TryGetValue(owner, out var cached)) return cached;
            var getter = owner.GetMethod("GetBehaviors", BindingFlags.Public | BindingFlags.Static);
            var parameters = getter?.GetParameters();
            if (getter is null || parameters is null || parameters.Length != 1 ||
                !typeof(DependencyObject).IsAssignableFrom(parameters[0].ParameterType) ||
                !typeof(System.Collections.IList).IsAssignableFrom(getter.ReturnType))
                throw Fail(line, $"'{owner.FullName}' has no GetBehaviors(DependencyObject) collection getter (converted shape changed).");
            TemplateBehaviorGetters[owner] = getter;
            return getter;
        }
    }

    // Reads one object-valued Setter.Value element (ShowMeTheXAML
    // XamlDisplay Formatter): resolves the CLR type through the same
    // clr-namespace / map-URI / engine chain as behaviors, constructs it,
    // and converts attributes through the CLR resource value path.
    // Builds one Setter.Value collection item: text converts to the
    // item type; structured content constructs and must be assignable.
    private static object CreateSetterCollectionItem(XElement node, Type itemType, IXmlLineInfo line)
    {
        var hasContent = node.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name.NamespaceName != XamlNamespace)
            || node.Elements().Any();
        if (!hasContent)
        {
            var text = node.Value.Trim();
            if (TryResolveClrResourceReference(text, itemType, line, out var referenced))
                return referenced!;
            if (TryResolveClrStaticMember(text, itemType, line, out var staticMember))
                return staticMember!;
            try { return BindingExpression.ConvertValue(text, itemType); }
            catch (Exception ex) when (ex is not XamlLoadException &&
                ex is (InvalidCastException or FormatException or OverflowException or ArgumentException))
            {
                throw Fail(line, "Setter.Value item text does not convert.");
            }
        }
        var child = ReadSetterValueObject(node, line);
        if (!itemType.IsInstanceOfType(child))
            throw Fail(line, "Setter.Value collection item has the wrong type.");
        return child;
    }

    private static object ReadSetterValueObject(XElement node, IXmlLineInfo line)
    {
        var name = node.Name.LocalName;
        var uri = node.Name.NamespaceName;
        Type? valueType = null;
        var target = ClrNamespaceTarget(uri, name);
        if (target is not null)
        {
            foreach (var assembly in ConvertedSearchAssemblies())
                if (SafeGetConvertedType(assembly, target) is { } candidate)
                    valueType ??= candidate;
        }
        else if (XmlNamespacesFor(uri) is { } namespaces)
        {
            foreach (var ns in namespaces)
                foreach (var assembly in ConvertedSearchAssemblies())
                    if (SafeGetConvertedType(assembly, ns + "." + name) is { } candidate)
                        valueType ??= candidate;
        }
        else if (uri == PresentationNamespace)
            valueType = FindEngineType(name);
        valueType ??= ScanUniqueConvertedType(name, line, name);
        if (valueType is null)
            throw Fail(line, "Setter.Value names no converted type.");
        if (valueType.IsAbstract)
            throw Fail(line, "Setter.Value type is abstract.");
        if (!HasDefaultCtor(valueType))
            throw Fail(line, "Setter.Value type has no default constructor.");
        var contentAttributes = node.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.NamespaceName != XamlNamespace).ToList();
        if (!node.Elements().Any() && contentAttributes.Count == 0)
        {
            // Text-only simple values (<system:String>uri</system:String>).
            try { return BindingExpression.ConvertValue(node.Value, valueType); }
            catch (Exception ex) when (ex is not XamlLoadException &&
                ex is (InvalidCastException or FormatException or OverflowException or ArgumentException))
            {
                throw Fail(line, "Setter.Value text does not convert.");
            }
        }
        var instance = Activator.CreateInstance(valueType, nonPublic: true)
            ?? throw Fail(line, "Cannot create Setter.Value content.");
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.NamespaceName != XamlNamespace))
        {
            var valueProperty = valueType.GetProperty(attribute.Name.LocalName)
                ?? throw Fail(line, "Unknown Setter.Value property.");
            if (valueProperty.SetMethod?.IsPublic != true)
                throw Fail(line, "Setter.Value property is not writable.");
            object? converted;
            if (TryResolveClrResourceReference(attribute.Value, valueProperty.PropertyType, line, out var referenced))
                converted = referenced;
            else if (TryResolveClrStaticMember(attribute.Value, valueProperty.PropertyType, line, out var staticMember))
                converted = staticMember;
            else try { converted = BindingExpression.ConvertValue(attribute.Value, valueProperty.PropertyType); }
            catch (Exception ex) when (ex is not XamlLoadException &&
                ex is (InvalidCastException or FormatException or OverflowException or ArgumentException))
            {
                throw Fail(line, "Setter.Value text does not convert.");
            }
            valueProperty.SetValue(instance, converted);
        }
        foreach (var wrapper in node.Elements())
        {
            // Property-element configuration
            // (<XamlFormatter.NamespacesToRemove><system:String/>...</>):
            // one assignable value sets a writable property, otherwise
            // children append to the live collection, like WPF.
            var wrapperName = wrapper.Name.LocalName;
            var dot = wrapperName.IndexOf(".");
            if (dot <= 0)
                throw Fail(line, "Setter.Value content needs Owner.Property wrappers.");
            var wrapperOwner = wrapperName[..dot];
            var ownerShort = wrapperOwner.Contains(":")
                ? wrapperOwner[(wrapperOwner.IndexOf(":") + 1)..]
                : wrapperOwner;
            if (!string.Equals(ownerShort, valueType.Name, StringComparison.Ordinal))
                throw Fail(line, "Setter.Value wrapper does not belong to the content type.");
            var wrapperProp = wrapperName[(dot + 1)..];
            var elementProperty = valueType.GetProperty(wrapperProp)
                ?? throw Fail(line, "Unknown Setter.Value property.");
            var valueKids = wrapper.Elements().ToList();
            if (valueKids.Count == 0)
            {
                var wrapperText = wrapper.Value.Trim();
                object? elementValue;
                if (TryResolveClrResourceReference(wrapperText, elementProperty.PropertyType, line, out var referenced))
                    elementValue = referenced;
                else if (TryResolveClrStaticMember(wrapperText, elementProperty.PropertyType, line, out var staticMember))
                    elementValue = staticMember;
                else try { elementValue = BindingExpression.ConvertValue(wrapperText, elementProperty.PropertyType); }
                catch (Exception ex) when (ex is not XamlLoadException &&
                    ex is (InvalidCastException or FormatException or OverflowException or ArgumentException))
                {
                    throw Fail(line, "Setter.Value text does not convert.");
                }
                if (elementProperty.SetMethod?.IsPublic != true)
                    throw Fail(line, "Setter.Value property is not writable.");
                elementProperty.SetValue(instance, elementValue);
                continue;
            }
            if (elementProperty.SetMethod?.IsPublic == true && valueKids.Count == 1)
            {
                var child = ReadSetterValueObject(valueKids[0], line);
                if (elementProperty.PropertyType.IsInstanceOfType(child))
                {
                    elementProperty.SetValue(instance, child);
                    continue;
                }
            }
            if (elementProperty.GetMethod?.IsPublic == true &&
                elementProperty.GetValue(instance) is System.Collections.IList list)
            {
                var itemType = elementProperty.PropertyType.IsArray
                    ? elementProperty.PropertyType.GetElementType()!
                    : elementProperty.PropertyType.IsGenericType
                        ? elementProperty.PropertyType.GetGenericArguments()[0]
                        : typeof(object);
                foreach (var kid in valueKids)
                    list.Add(CreateSetterCollectionItem(kid, itemType, line));
                continue;
            }
            throw Fail(line, "Setter.Value wrapper holds one assignable value or collection items.");
        }
        return instance;
    }

    private static Type ResolveTemplateBehaviorType(XElement node, IXmlLineInfo line, string original)
    {
        var name = node.Name.LocalName;
        var uri = node.Name.NamespaceName;
        var target = ClrNamespaceTarget(uri, name);
        if (target is not null)
        {
            Type? direct = null;
            foreach (var assembly in ConvertedSearchAssemblies())
            {
                var candidate = SafeGetConvertedType(assembly, target);
                if (candidate is null) continue;
                if (direct is not null && !ReferenceEquals(direct, candidate))
                    throw Fail(line, $"<{name}> matches '{direct.FullName}' and '{candidate.FullName}' (keep one converted behavior).");
                direct = candidate;
            }
            if (direct is null)
                throw Fail(line, $"'{original}' names no converted behavior '{target}'.");
            return CheckTemplateBehaviorType(direct, name, line, original);
        }
        if (XmlNamespacesFor(uri) is { } namespaces)
        {
            Type? found = null;
            foreach (var ns in namespaces)
            {
                foreach (var assembly in ConvertedSearchAssemblies())
                {
                    var candidate = SafeGetConvertedType(assembly, ns + "." + name);
                    if (candidate is null) continue;
                    if (found is not null && !ReferenceEquals(found, candidate))
                        throw Fail(line, $"'{original}' matches '{found.FullName}' and '{candidate.FullName}' (update the xmlns table).");
                    found = candidate;
                }
            }
            if (found is not null) return CheckTemplateBehaviorType(found, name, line, original);
            throw Fail(line, $"'{original}' names no converted behavior '{name}' under '{uri}'.");
        }
        var scanned = ScanUniqueConvertedType(name, line, original)
            ?? throw Fail(line, $"'{original}' names no converted behavior '{name}'.");
        return CheckTemplateBehaviorType(scanned, name, line, original);
    }

    private static Type CheckTemplateBehaviorType(Type type, string name, IXmlLineInfo line, string original)
    {
        if (!typeof(DependencyObject).IsAssignableFrom(type))
            throw Fail(line, $"'{original}' cannot hold <{name}> ('{type.FullName}' is not a DependencyObject behavior).");
        if (type.IsAbstract)
            throw Fail(line, $"'{original}' cannot hold <{name}> ('{type.FullName}' is abstract).");
        if (!HasDefaultCtor(type))
            throw Fail(line, $"'{original}' cannot hold <{name}> ('{type.FullName}' has no default constructor).");
        return type;
    }

    /// <summary>Parses a behavior {Binding} (RelativeSource/Converter/
    /// ConverterParameter like template transform bindings, but an empty
    /// Path binds the source itself, e.g. {Binding ElementName=Part}).</summary>
    // Parses an attribute-form style Setter {Binding} (theme buttons
    // bind Foreground to the ancestor TextElement.Foreground with a
    // literal fallback): RelativeSource/Converter/ConverterParameter
    // extract like element bindings, the rest converts through the
    // shared binding parser. Returns null for non-binding values.
    private static Binding? ParseStyleSetterBindingOrNull(string value, IXmlLineInfo line)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("{Binding", StringComparison.Ordinal) || !trimmed.EndsWith('}'))
            return null;
        var after = trimmed["{Binding".Length..];
        if (after.Length != 0 && after[0] != '}' && after[0] != ' ' && after[0] != ',')
            return null;
        var body = trimmed[1..^1].Trim()["Binding".Length..];
        var options = BindingOptions(body);
        var relative = ExtractNestedOption(options, "RelativeSource");
        var converter = ExtractNestedOption(options, "Converter");
        var parameter = ExtractNestedOption(options, "ConverterParameter");
        Binding parsed;
        try { parsed = ParseBinding(string.Join(',', options), value, line); }
        catch (ArgumentException ex) { throw Fail(line, ex.Message); }
        if (relative is not null)
        {
            try { parsed.RelativeSource = ParseRelativeSource(relative, line); }
            catch (ArgumentException ex) { throw Fail(line, ex.Message); }
        }
        if (converter is not null) ApplyBindingConverter(parsed, converter, line);
        if (parameter is not null)
            parsed.ConverterParameter = ParseLiteralOrStaticParameter(parameter, value, line);
        return parsed;
    }

    private static Binding ParseBehaviorBinding(string value, string property, IXmlLineInfo line)
    {
        var body = value[1..^1].Trim()["Binding".Length..];
        var options = BindingOptions(body);
        var relative = ExtractNestedOption(options, "RelativeSource");
        var converter = ExtractNestedOption(options, "Converter");
        var parameter = ExtractNestedOption(options, "ConverterParameter");
        Binding parsed;
        try { parsed = ParseBinding(string.Join(',', options), value, line); }
        catch (ArgumentException ex) { throw Fail(line, ex.Message); }
        if (relative is not null)
        {
            try { parsed.RelativeSource = ParseRelativeSource(relative, line); }
            catch (ArgumentException ex) { throw Fail(line, ex.Message); }
        }
        if (converter is not null) ApplyBindingConverter(parsed, converter, line);
        if (parameter is not null)
            parsed.ConverterParameter = ParseStandaloneParameter(parameter, property, line);
        if (parsed.Mode is BindingMode.TwoWay or BindingMode.OneWayToSource)
            throw Fail(line, $"'{value}' on behavior '{property}' takes OneWay/OneTime (two-way stays a precise boundary).");
        return parsed;
    }

    private static SolidColorBrush ParseInlineBrushText(XElement node, IXmlLineInfo line)
    {
        var text = node.Value.Trim();
        if (text.Length == 0) throw Fail(line, "Template brush properties need a brush element or color text.");
        try { return new SolidColorBrush(StyleValueConverter.ColorFromRgba(XamlLoader.ParseColorCore(text))); }
        catch (ArgumentException ex) { throw Fail(line, ex.Message); }
    }

    /// <summary>Reads an inline template VisualBrush (tile knobs plus an
    /// ElementName-bound Visual, like the runtime VisualBrush path).</summary>
    private static (VisualBrush Brush, string? VisualName) ReadTemplateVisualBrush(XElement node, IXmlLineInfo line)
    {
        var brush = new VisualBrush();
        string? visualName = null;
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            if (attribute.Name.NamespaceName == XamlNamespace) continue;
            var local = attribute.Name.LocalName;
            if (local == "Visual" && string.IsNullOrEmpty(attribute.Name.NamespaceName))
            {
                var text = attribute.Value.Trim();
                if (!text.StartsWith('{') || !text.EndsWith('}'))
                    throw Fail(line, "'Visual' needs {Binding ElementName=...} (inline visuals are not supported).");
                var inner = text[1..^1].Trim();
                if (!inner.StartsWith("Binding", StringComparison.Ordinal))
                    throw Fail(line, "'Visual' needs {Binding ElementName=...} (inline visuals are not supported).");
                Binding binding;
                try { binding = ParseBinding(inner["Binding".Length..], text, line); }
                catch (XamlLoadException) { throw; }
                if (binding.ElementName is null || binding.Path.Path.Length != 0)
                    throw Fail(line, "'Visual' needs {Binding ElementName=...} (path bindings are not supported).");
                visualName = binding.ElementName;
                continue;
            }
            if (local.StartsWith("RenderOptions.", StringComparison.Ordinal)) continue;
            ApplyTileBrushAttribute(brush, attribute, "VisualBrush", line);
        }
        return (brush, visualName);
    }

    internal static Brush ReadInlineBrush(XElement node, IXmlLineInfo line, ResourceDictionary? scope = null)
    {
        return node.Name.LocalName switch
        {
            "SolidColorBrush" => ReadSolidBrush(node, line, scope),
            "LinearGradientBrush" => ReadLinearBrush(node, line, scope),
            "RadialGradientBrush" => ReadRadialBrush(node, line, scope),
            "DrawingBrush" => ReadDrawingBrushNode(node, line, scope),
            "ImageBrush" => ReadImageBrushNode(node, line),
            "VisualBrush" => throw Fail(line, "<VisualBrush> needs an ElementName-bound Visual (inline visuals are not supported)."),
            _ => throw Fail(line, $"<{node.Name.LocalName}> is not a supported inline brush (SolidColorBrush/LinearGradientBrush/RadialGradientBrush/DrawingBrush/ImageBrush)."),
        };
    }

    private static SolidColorBrush ReadSolidBrush(XElement node, IXmlLineInfo line, ResourceDictionary? scope = null)
    {
        string? color = null;
        double opacity = 1.0;
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            if (attribute.Name.LocalName == "Color" && string.IsNullOrEmpty(attribute.Name.NamespaceName)) { color = attribute.Value; continue; }
            if (attribute.Name.LocalName == "Opacity" && string.IsNullOrEmpty(attribute.Name.NamespaceName)) { opacity = ParseDoubleValue("Opacity", attribute.Value, line); continue; }
            throw Fail(line, $"'{attribute.Name}' is not valid on <SolidColorBrush> (Color/Opacity).");
        }
        if (color is null) throw Fail(line, "<SolidColorBrush> needs Color=\"...\".");
        SolidColorBrush solid;
        try { solid = new SolidColorBrush(ParseBrushColorText(color, line, scope)); }
        catch (ArgumentException ex) { throw Fail(line, ex.Message); }
        solid.Opacity = opacity;
        return solid;
    }

    private static void ReadTileBrushAttributes(TileBrush brush, XElement node, IXmlLineInfo line)
    {
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            ApplyTileBrushAttribute(brush, attribute, node.Name.LocalName, line);
        }
    }

    /// <summary>Applies one TileBrush layout attribute (shared by inline and resource forms).</summary>
    internal static void ApplyTileBrushAttribute(TileBrush brush, XAttribute attribute, string owner, IXmlLineInfo line)
    {
        var local = attribute.Name.LocalName;
        var value = attribute.Value;
        switch (local)
        {
            case "Viewport": brush.Viewport = RectangleGeometry.ParseRect(value); break;
            case "ViewportUnits": brush.ViewportUnits = ParseBrushMapping(value, line); break;
            case "Viewbox": brush.Viewbox = RectangleGeometry.ParseRect(value); break;
            case "ViewboxUnits": brush.ViewboxUnits = ParseBrushMapping(value, line); break;
            case "TileMode":
                if (!Enum.TryParse<TileMode>(value, ignoreCase: true, out var tile))
                    throw Fail(line, $"'{value}' is not a TileMode (None/FlipX/FlipY/FlipXY/Tile).");
                brush.TileMode = tile; break;
            case "Stretch":
                if (!Enum.TryParse<Stretch>(value, ignoreCase: true, out var stretch))
                    throw Fail(line, $"'{value}' is not a Stretch (None/Fill/Uniform/UniformToFill).");
                brush.Stretch = stretch; break;
            case "AlignmentX":
                if (!Enum.TryParse<AlignmentX>(value, ignoreCase: true, out var alignX))
                    throw Fail(line, $"'{value}' is not an AlignmentX (Left/Center/Right).");
                brush.AlignmentX = alignX; break;
            case "AlignmentY":
                if (!Enum.TryParse<AlignmentY>(value, ignoreCase: true, out var alignY))
                    throw Fail(line, $"'{value}' is not an AlignmentY (Top/Center/Bottom).");
                brush.AlignmentY = alignY; break;
            case "Opacity":
                brush.Opacity = ParseDoubleValue("Opacity", value, line); break;
            default: throw Fail(line, $"'{attribute.Name}' is not valid on <{owner}> (Viewport/ViewportUnits/Viewbox/ViewboxUnits/TileMode/Stretch/AlignmentX/AlignmentY/Opacity).");
        }
    }

    /// <summary>Reads an ImageBrush element (resource and inline property-element forms).</summary>
    internal static ImageBrush ReadImageBrushNode(XElement node, IXmlLineInfo line)
    {
        var brush = new ImageBrush();
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            if (attribute.Name.LocalName == "ImageSource" && string.IsNullOrEmpty(attribute.Name.NamespaceName))
            {
                try { brush.ImageSource = new BitmapImage(new Uri(attribute.Value, UriKind.RelativeOrAbsolute)); }
                catch (UriFormatException ex) { throw Fail(line, ex.Message); }
                continue;
            }
            ApplyTileBrushAttribute(brush, attribute, node.Name.LocalName, line);
        }
        if (brush.ImageSource is null) throw Fail(line, "<ImageBrush> needs ImageSource=.");
        return brush;
    }

    private static BrushMappingMode ParseBrushMapping(string value, IXmlLineInfo line) =>
        value switch
        {
            "Absolute" => BrushMappingMode.Absolute,
            "RelativeToBoundingBox" => BrushMappingMode.RelativeToBoundingBox,
            _ => throw Fail(line, $"'{value}' is not a BrushMappingMode (Absolute/RelativeToBoundingBox)."),
        };

    private static LinearGradientBrush ReadLinearBrush(XElement node, IXmlLineInfo line, ResourceDictionary? scope = null)
    {
        var brush = new LinearGradientBrush();
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            var local = attribute.Name.LocalName;
            switch (local)
            {
                case "StartPoint": brush.StartPoint = ParsePointValue(attribute.Value, line); break;
                case "EndPoint": brush.EndPoint = ParsePointValue(attribute.Value, line); break;
                case "MappingMode": brush.MappingMode = ParseBrushMapping(attribute.Value, line); break;
                case "Opacity": brush.Opacity = ParseDoubleValue("Opacity", attribute.Value, line); break;
                case "GradientStops":
                    brush.GradientStops = ReadStaticGradientStops(attribute.Value, line, scope);
                    break;
                default: throw Fail(line, $"'{attribute.Name}' is not valid on <LinearGradientBrush> (StartPoint/EndPoint/MappingMode/Opacity/GradientStops).");
            }
        }
        foreach (var child in node.Elements())
        {
            if (child.Name.LocalName == "LinearGradientBrush.GradientStops")
            {
                foreach (var stop in child.Elements())
                    brush.GradientStops.Add(ReadGradientStop(stop, line, scope));
                continue;
            }
            // Content-property form: stops nest directly (real WPF collects them).
            if (child.Name.LocalName == "GradientStop")
            {
                brush.GradientStops.Add(ReadGradientStop(child, line, scope));
                continue;
            }
            throw Fail(line, $"<{child.Name.LocalName}> is not valid in <LinearGradientBrush> (<GradientStop> or <LinearGradientBrush.GradientStops> only).");
        }
        return brush;
    }

    private static RadialGradientBrush ReadRadialBrush(XElement node, IXmlLineInfo line, ResourceDictionary? scope = null)
    {
        var brush = new RadialGradientBrush();
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            var local = attribute.Name.LocalName;
            switch (local)
            {
                case "GradientOrigin": brush.GradientOrigin = ParsePointValue(attribute.Value, line); break;
                case "Center": brush.Center = ParsePointValue(attribute.Value, line); break;
                case "RadiusX": brush.RadiusX = ParseDoubleValue("RadiusX", attribute.Value, line); break;
                case "RadiusY": brush.RadiusY = ParseDoubleValue("RadiusY", attribute.Value, line); break;
                case "MappingMode": brush.MappingMode = ParseBrushMapping(attribute.Value, line); break;
                case "Opacity": brush.Opacity = ParseDoubleValue("Opacity", attribute.Value, line); break;
                case "GradientStops":
                    brush.GradientStops = ReadStaticGradientStops(attribute.Value, line, scope);
                    break;
                default: throw Fail(line, $"'{attribute.Name}' is not valid on <RadialGradientBrush> (GradientOrigin/Center/RadiusX/RadiusY/MappingMode/Opacity/GradientStops).");
            }
        }
        foreach (var child in node.Elements())
        {
            if (child.Name.LocalName == "RadialGradientBrush.GradientStops")
            {
                foreach (var stop in child.Elements())
                    brush.GradientStops.Add(ReadGradientStop(stop, line, scope));
                continue;
            }
            // Content-property form: stops nest directly (real WPF collects them).
            if (child.Name.LocalName == "GradientStop")
            {
                brush.GradientStops.Add(ReadGradientStop(child, line, scope));
                continue;
            }
            throw Fail(line, $"<{child.Name.LocalName}> is not valid in <RadialGradientBrush> (<GradientStop> or <RadialGradientBrush.GradientStops> only).");
        }
        return brush;
    }

    private static GradientStopCollection ReadStaticGradientStops(string value, IXmlLineInfo line, ResourceDictionary? scope = null)
    {
        var text = value.Trim();
        if (!text.StartsWith("{StaticResource ", StringComparison.Ordinal) || !text.EndsWith('}'))
            throw Fail(line, $"GradientStops='{value}' needs {{StaticResource Key}} (inline <GradientBrush.GradientStops> otherwise).");
        var key = text["{StaticResource ".Length..^1].Trim();
        // Lexical scope first (the dictionary being filled, like the loader's
        // element scopes), then application resources. Element-local scopes
        // inside nested brushes resolve through the pending path instead.
        if (scope is not null && scope.TryGetValue(key, out var entry) && entry is GradientStopCollection found)
            return found;
        var app = Application.Current;
        if (app is null || !app.Resources.TryGetValue(key, out var resource))
            throw Fail(line, $"StaticResource '{key}' was not found in element or application resources (GradientStops).");
        if (resource is not GradientStopCollection stops)
            throw Fail(line, $"StaticResource '{key}' is a {resource?.GetType().Name ?? "null"}, not a GradientStopCollection.");
        return stops;
    }

    private static GradientStop ReadGradientStop(XElement node, IXmlLineInfo line, ResourceDictionary? scope = null)
    {
        if (node.Name.LocalName != "GradientStop")
            throw Fail(line, $"<{node.Name.LocalName}> is not a <GradientStop>.");
        var stop = new GradientStop();
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            switch (attribute.Name.LocalName)
            {
                case "Color":
                    if (TryParseBrushColorResource(attribute.Value, line, scope, out var resourceColor))
                    {
                        if (resourceColor is not null) stop.Color = resourceColor.Value;
                        break;
                    }
                    try { stop.Color = Color.Parse(attribute.Value); }
                    catch (FormatException) { stop.Color = NamedColor(attribute.Value, line); }
                    break;
                case "Offset": stop.Offset = ParseDoubleValue("Offset", attribute.Value, line); break;
                default: throw Fail(line, $"'{attribute.Name}' is not valid on <GradientStop> (Color/Offset).");
            }
        }
        return stop;
    }

    private static Color NamedColor(string value, IXmlLineInfo line)
    {
        try { return StyleValueConverter.ColorFromRgba(XamlLoader.ParseColorCore(value)); }
        catch (ArgumentException ex) { throw Fail(line, ex.Message); }
    }

    private static Point ParsePointValue(string value, IXmlLineInfo line)
    {
        try { return Point.Parse(value); }
        catch (FormatException ex) { throw Fail(line, ex.Message); }
    }

    /// <summary>Reads a DrawingBrush element (properties plus a DrawingBrush.Drawing child).</summary>
    internal static DrawingBrush ReadDrawingBrushNode(XElement node, IXmlLineInfo line, ResourceDictionary? scope = null)
    {
        var brush = new DrawingBrush();
        ReadTileBrushAttributes(brush, node, line);
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            var local = attribute.Name.LocalName;
            if (local is "Viewport" or "ViewportUnits" or "Viewbox" or "ViewboxUnits" or "TileMode" or "Stretch" or "AlignmentX" or "AlignmentY" or "Opacity") continue;
            throw Fail(line, $"'{attribute.Name}' is not valid on <DrawingBrush> (Viewport/ViewportUnits/Viewbox/ViewboxUnits/TileMode/Stretch/AlignmentX/AlignmentY/Opacity plus DrawingBrush.Drawing).");
        }
        foreach (var child in node.Elements())
        {
            if (child.Name.LocalName == "DrawingBrush.Drawing")
            {
                var drawing = child.Elements().FirstOrDefault()
                    ?? throw Fail(line, "<DrawingBrush.Drawing> needs a drawing element.");
                brush.Drawing = ReadDrawingNode(drawing, line, scope);
                continue;
            }
            throw Fail(line, $"<{child.Name.LocalName}> is not valid in <DrawingBrush> (<DrawingBrush.Drawing> only).");
        }
        return brush;
    }

    /// <summary>Reads a drawing element (DrawingGroup, GeometryDrawing or ImageDrawing).</summary>
    internal static Drawing ReadDrawingNode(XElement node, IXmlLineInfo line, ResourceDictionary? scope = null)
    {
        return node.Name.LocalName switch
        {
            "DrawingGroup" => ReadDrawingGroupNode(node, line, scope),
            "GeometryDrawing" => ReadGeometryDrawingNode(node, line, scope),
            "ImageDrawing" => ReadImageDrawingNode(node, line),
            _ => throw Fail(line, $"<{node.Name.LocalName}> is not a supported drawing (DrawingGroup/GeometryDrawing/ImageDrawing)."),
        };
    }

    private static DrawingGroup ReadDrawingGroupNode(XElement node, IXmlLineInfo line, ResourceDictionary? scope)
    {
        var group = new DrawingGroup(new DrawingContext());
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            throw Fail(line, $"'{attribute.Name}' is not valid on <DrawingGroup> (children only).");
        }
        foreach (var child in node.Elements())
        {
            if (child.Name.LocalName == "DrawingGroup.Children")
            {
                foreach (var nested in child.Elements())
                    group.Children.Add(ReadDrawingNode(nested, line, scope));
                continue;
            }
            group.Children.Add(ReadDrawingNode(child, line, scope));
        }
        return group;
    }

    private static GeometryDrawing ReadGeometryDrawingNode(XElement node, IXmlLineInfo line, ResourceDictionary? scope)
    {
        var drawing = new GeometryDrawing();
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            var name = attribute.Name.LocalName;
            switch (name)
            {
                case "Brush": drawing.Brush = ParseBrushText(attribute.Value, line, scope); break;
                case "Geometry": drawing.Geometry = ParseGeometryText(attribute.Value, line); break;
                default: throw Fail(line, $"'{attribute.Name}' is not valid on <GeometryDrawing> (Brush/Geometry plus GeometryDrawing.Geometry/Pen).");
            }
        }
        foreach (var child in node.Elements())
        {
            var parts = child.Name.LocalName.Split('.', 2);
            if (parts.Length == 2 && parts[0] == "GeometryDrawing")
            {
                switch (parts[1])
                {
                    case "Geometry":
                        var geometryNode = child.Elements().FirstOrDefault()
                            ?? throw Fail(line, "<GeometryDrawing.Geometry> needs a geometry element.");
                        drawing.Geometry = ReadGeometryNode(geometryNode, line);
                        continue;
                    case "Pen":
                        var penNode = child.Elements().FirstOrDefault()
                            ?? throw Fail(line, "<GeometryDrawing.Pen> needs a <Pen> element.");
                        drawing.Pen = ReadPenNode(penNode, line, scope);
                        continue;
                }
            }
            throw Fail(line, $"<{child.Name.LocalName}> is not valid in <GeometryDrawing> (GeometryDrawing.Geometry/Pen only).");
        }
        return drawing;
    }

    private static ImageDrawing ReadImageDrawingNode(XElement node, IXmlLineInfo line)
    {
        var drawing = new ImageDrawing();
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            var name = attribute.Name.LocalName;
            switch (name)
            {
                case "ImageSource" when string.IsNullOrEmpty(attribute.Name.NamespaceName):
                    try { drawing.ImageSource = new BitmapImage(new Uri(attribute.Value, UriKind.RelativeOrAbsolute)); }
                    catch (UriFormatException ex) { throw Fail(line, ex.Message); }
                    break;
                case "Rect" when string.IsNullOrEmpty(attribute.Name.NamespaceName):
                    try { drawing.Rect = RectangleGeometry.ParseRect(attribute.Value); }
                    catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
                    { throw Fail(line, ex.Message); }
                    break;
                default: throw Fail(line, $"'{attribute.Name}' is not valid on <ImageDrawing> (ImageSource/Rect).");
            }
        }
        if (node.Elements().Any()) throw Fail(line, "<ImageDrawing> takes attributes only.");
        return drawing;
    }

    private static Pen ReadPenNode(XElement node, IXmlLineInfo line, ResourceDictionary? scope = null)
    {
        if (node.Name.LocalName != "Pen")
            throw Fail(line, $"<{node.Name.LocalName}> is not a <Pen>.");
        var pen = new Pen();
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            switch (attribute.Name.LocalName)
            {
                case "Brush": pen.Brush = ParseBrushText(attribute.Value, line, scope); break;
                case "Thickness": pen.Thickness = ParseDoubleValue("Thickness", attribute.Value, line); break;
                default: throw Fail(line, $"'{attribute.Name}' is not valid on <Pen> (Brush/Thickness).");
            }
        }
        return pen;
    }

    /// <summary>Parses a brush Color attribute that may hold a resource expression
    /// (theme tiles alias palette colors on SolidColorBrush/stop Colors; StaticResource
    /// stays strict like the entry readers, DynamicResource resolves now with missing
    /// keys transparent like the other dynamic entry colors).</summary>
    private static Color ParseBrushColorText(string text, IXmlLineInfo line, ResourceDictionary? scope)
    {
        if (TryParseBrushColorResource(text, line, scope, out var color))
            return color ?? Colors.Transparent;
        return StyleValueConverter.ColorFromRgba(XamlLoader.ParseColorCore(text));
    }

    /// <summary>Resolves a {StaticResource Key}/{DynamicResource Key} brush color.
    /// Returns false for plain colors; a null color means a missing DynamicResource
    /// key (callers keep the WPF default instead of failing the dictionary).</summary>
    private static bool TryParseBrushColorResource(string text, IXmlLineInfo line, ResourceDictionary? scope, out Color? color)
    {
        color = null;
        var value = text.Trim();
        var isStatic = value.StartsWith("{StaticResource ", StringComparison.Ordinal) && value.EndsWith('}');
        var isDynamic = !isStatic && value.StartsWith("{DynamicResource ", StringComparison.Ordinal) && value.EndsWith('}');
        if (!isStatic && !isDynamic) return false;
        var key = (isStatic ? value["{StaticResource ".Length..^1] : value["{DynamicResource ".Length..^1]).Trim();
        if (key.Length == 0 || key.Contains('{') || key.Contains('='))
            throw Fail(line, $"'{text}' takes a plain key ({{StaticResource Key}}/{{DynamicResource Key}}).");
        if (scope is not null && scope.TryGetValue(key, out var scoped) && scoped is not null)
        {
            color = isStatic ? CoerceBrushStaticColor(scoped, key, line) : CoerceEntryColor(scoped, key, line);
            return true;
        }
        if (isStatic)
        {
            color = StyleValueConverter.ColorFromRgba(ParseColor(ResolveEntryStaticColor(key, line), line));
            return true;
        }
        if (TryFindHydrationResource(key, out var hydrated) && hydrated is not null)
            color = CoerceEntryColor(hydrated, key, line);
        else
            color = CoerceEntryColor(ResolveEntryDynamicValue(key), key, line);
        return true;
    }

    private static Color CoerceBrushStaticColor(object resource, string key, IXmlLineInfo line) =>
        resource switch
        {
            uint rgba => StyleValueConverter.ColorFromRgba(rgba),
            Color found => found,
            SolidColorBrush solid => solid.Color,
            string s => StyleValueConverter.ColorFromRgba(XamlLoader.ParseColorCore(s)),
            _ => throw Fail(line, $"StaticResource '{key}' is a {resource.GetType().Name}, not a color."),
        };

    private static SolidColorBrush? ParseBrushText(string value, IXmlLineInfo line, ResourceDictionary? scope = null)
    {
        var text = value.Trim();
        if (text.StartsWith("{StaticResource ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            var key = text["{StaticResource ".Length..^1].Trim();
            if (scope is not null && scope.TryGetValue(key, out var entry) && entry is SolidColorBrush found)
                return found;
            var app = Application.Current;
            if (app is not null && app.Resources.TryGetValue(key, out var resource) && resource is SolidColorBrush appBrush)
                return appBrush;
            throw Fail(line, $"StaticResource '{key}' was not found for a solid brush.");
        }
        if (text.StartsWith("{DynamicResource ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            var key = text["{DynamicResource ".Length..^1].Trim();
            if (key.Length == 0 || key.Contains('{') || key.Contains('='))
                throw Fail(line, $"'{value}' takes a plain key ({{DynamicResource Key}}).");
            if (scope is not null && scope.TryGetValue(key, out var entry) && entry is SolidColorBrush found)
                return found;
            if (TryFindHydrationResource(key, out var hydrated) && hydrated is SolidColorBrush hydrating)
                return hydrating;
            var app = Application.Current;
            if (app is not null && app.Resources.TryGetValue(key, out var resource))
            {
                if (resource is SolidColorBrush appBrush) return appBrush;
                throw Fail(line, $"DynamicResource '{key}' is a {resource?.GetType().Name ?? "null"}, not a solid brush.");
            }
            return null;
        }
        try { return new SolidColorBrush(StyleValueConverter.ColorFromRgba(XamlLoader.ParseColorCore(text))); }
        catch (ArgumentException ex) { throw Fail(line, ex.Message); }
    }

    private static Geometry ParseGeometryText(string value, IXmlLineInfo line)
    {
        try { return Geometry.Parse(value.Trim()); }
        catch (FormatException ex) { throw Fail(line, ex.Message); }
        catch (ArgumentException ex) { throw Fail(line, ex.Message); }
    }

    /// <summary>Reads a geometry element (shape geometries, groups and path figures).</summary>
    internal static Geometry ReadGeometryNode(XElement node, IXmlLineInfo line)
    {
        switch (node.Name.LocalName)
        {
            case "LineGeometry":
            {
                var geometry = new LineGeometry();
                foreach (var attribute in GeometryAttributes(node, line))
                {
                    switch (attribute.Name.LocalName)
                    {
                        case "StartPoint": geometry.StartPoint = ParsePointValue(attribute.Value, line); break;
                        case "EndPoint": geometry.EndPoint = ParsePointValue(attribute.Value, line); break;
                        default: throw Fail(line, $"'{attribute.Name}' is not valid on <LineGeometry> (StartPoint/EndPoint).");
                    }
                }
                return geometry;
            }
            case "RectangleGeometry":
            {
                var geometry = new RectangleGeometry();
                foreach (var attribute in GeometryAttributes(node, line))
                {
                    if (attribute.Name.LocalName == "Rect") { geometry.Rect = RectangleGeometry.ParseRect(attribute.Value); continue; }
                    if (attribute.Name.LocalName == "RadiusX") { geometry.RadiusX = ParseDoubleValue("RadiusX", attribute.Value, line); continue; }
                    if (attribute.Name.LocalName == "RadiusY") { geometry.RadiusY = ParseDoubleValue("RadiusY", attribute.Value, line); continue; }
                    throw Fail(line, $"'{attribute.Name}' is not valid on <RectangleGeometry> (Rect/RadiusX/RadiusY).");
                }
                return geometry;
            }
            case "EllipseGeometry":
            {
                var geometry = new EllipseGeometry();
                foreach (var attribute in GeometryAttributes(node, line))
                {
                    switch (attribute.Name.LocalName)
                    {
                        case "Center": geometry.Center = ParsePointValue(attribute.Value, line); break;
                        case "RadiusX": geometry.RadiusX = ParseDoubleValue("RadiusX", attribute.Value, line); break;
                        case "RadiusY": geometry.RadiusY = ParseDoubleValue("RadiusY", attribute.Value, line); break;
                        default: throw Fail(line, $"'{attribute.Name}' is not valid on <EllipseGeometry> (Center/RadiusX/RadiusY).");
                    }
                }
                return geometry;
            }
            case "GeometryGroup":
            {
                var group = new GeometryGroup();
                foreach (var attribute in GeometryAttributes(node, line))
                {
                    if (attribute.Name.LocalName == "FillRule") { group.FillRule = ParseFillRule(attribute.Value, line); continue; }
                    throw Fail(line, $"'{attribute.Name}' is not valid on <GeometryGroup> (FillRule).");
                }
                foreach (var child in node.Elements())
                    group.Children.Add(ReadGeometryNode(child, line));
                return group;
            }
            case "PathGeometry":
            {
                var geometry = new PathGeometry();
                foreach (var attribute in GeometryAttributes(node, line))
                {
                    if (attribute.Name.LocalName == "FillRule") { geometry.FillRule = ParseFillRule(attribute.Value, line); continue; }
                    throw Fail(line, $"'{attribute.Name}' is not valid on <PathGeometry> (FillRule plus PathGeometry.Figures).");
                }
                foreach (var child in node.Elements())
                {
                    if (child.Name.LocalName == "PathGeometry.Figures")
                    {
                        foreach (var figures in child.Elements())
                        {
                            if (figures.Name.LocalName != "PathFigureCollection")
                                throw Fail(line, $"<{figures.Name.LocalName}> is not a <PathFigureCollection>.");
                            foreach (var figure in figures.Elements())
                                geometry.Figures.Add(ReadPathFigure(figure, line));
                        }
                        continue;
                    }
                    if (child.Name.LocalName == "PathFigure")
                    {
                        // Content-property form (theme rings nest figures
                        // directly, like native): same reader, no wrapper.
                        geometry.Figures.Add(ReadPathFigure(child, line));
                        continue;
                    }
                    throw Fail(line, $"<{child.Name.LocalName}> is not valid in <PathGeometry> (<PathFigure> or <PathGeometry.Figures> only).");
                }
                return geometry;
            }
            case "CombinedGeometry":
            {
                var geometry = new CombinedGeometry();
                foreach (var attribute in GeometryAttributes(node, line))
                {
                    switch (attribute.Name.LocalName)
                    {
                        case "GeometryCombineMode":
                            geometry.GeometryCombineMode = ParseCombineMode(attribute.Value, line);
                            break;
                        case "Geometry1": geometry.Geometry1 = ParseGeometryText(attribute.Value, line); break;
                        case "Geometry2": geometry.Geometry2 = ParseGeometryText(attribute.Value, line); break;
                        default: throw Fail(line, $"'{attribute.Name}' is not valid on <CombinedGeometry> (GeometryCombineMode/Geometry1/Geometry2).");
                    }
                }
                foreach (var child in node.Elements())
                {
                    var parts = child.Name.LocalName.Split('.', 2);
                    if (parts.Length == 2 && parts[0] == "CombinedGeometry")
                    {
                        var nested = child.Elements().FirstOrDefault()
                            ?? throw Fail(line, $"<{child.Name.LocalName}> needs a geometry element.");
                        switch (parts[1])
                        {
                            case "Geometry1": geometry.Geometry1 = ReadGeometryNode(nested, line); continue;
                            case "Geometry2": geometry.Geometry2 = ReadGeometryNode(nested, line); continue;
                        }
                    }
                    throw Fail(line, $"<{child.Name.LocalName}> is not valid in <CombinedGeometry> (<CombinedGeometry.Geometry1/Geometry2> only).");
                }
                return geometry;
            }
            default: throw Fail(line, $"<{node.Name.LocalName}> is not a supported geometry (LineGeometry/RectangleGeometry/EllipseGeometry/GeometryGroup/CombinedGeometry/PathGeometry).");
        }
    }

    private static IEnumerable<XAttribute> GeometryAttributes(XElement node, IXmlLineInfo line)
    {
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            yield return attribute;
        }
    }

    private static GeometryCombineMode ParseCombineMode(string value, IXmlLineInfo line)
    {
        foreach (var name in Enum.GetNames<GeometryCombineMode>())
            if (string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase))
                return Enum.Parse<GeometryCombineMode>(name);
        throw Fail(line, $"'{value}' is not a GeometryCombineMode (Union/Intersect/Xor/Exclude).");
    }

    private static FillRule ParseFillRule(string value, IXmlLineInfo line)
    {
        foreach (var name in Enum.GetNames<FillRule>())
            if (string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase))
                return Enum.Parse<FillRule>(name);
        throw Fail(line, $"'{value}' is not a FillRule (EvenOdd/Nonzero).");
    }

    private static PathFigure ReadPathFigure(XElement node, IXmlLineInfo line)
    {
        if (node.Name.LocalName != "PathFigure")
            throw Fail(line, $"<{node.Name.LocalName}> is not a <PathFigure>.");
        var figure = new PathFigure();
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            ApplyPathFigureAttribute(figure, attribute, line);
        }
        foreach (var child in node.Elements())
        {
            if (child.Name.LocalName == "PathFigure.Segments")
            {
                foreach (var segments in child.Elements())
                {
                    if (segments.Name.LocalName != "PathSegmentCollection")
                        throw Fail(line, $"<{segments.Name.LocalName}> is not a <PathSegmentCollection>.");
                    foreach (var segment in segments.Elements())
                        figure.Segments.Add(ReadPathSegment(segment, line));
                }
                continue;
            }
            if (IsPathSegmentName(child.Name.LocalName))
            {
                // Content-property form (theme arcs nest directly):
                // same reader, no wrapper.
                figure.Segments.Add(ReadPathSegment(child, line));
                continue;
            }
            throw Fail(line, $"<{child.Name.LocalName}> is not valid in <PathFigure> (segments or <PathFigure.Segments> only).");
        }
        return figure;
    }

    /// <summary>True for path-segment element names (content-property detection).</summary>
    private static bool IsPathSegmentName(string localName) => localName is
        "LineSegment" or "BezierSegment" or "QuadraticBezierSegment" or "ArcSegment";

    /// <summary>Applies one static PathFigure attribute (StartPoint/
    /// IsClosed/IsFilled mirror the native figure surface).</summary>
    private static void ApplyPathFigureAttribute(PathFigure figure, XAttribute attribute, IXmlLineInfo line)
    {
        switch (attribute.Name.LocalName)
        {
            case "StartPoint": figure.StartPoint = ParsePointValue(attribute.Value, line); break;
            case "IsClosed": figure.IsClosed = ParseBoolValue(attribute.Value, line); break;
            case "IsFilled": figure.IsFilled = ParseBoolValue(attribute.Value, line); break;
            default: throw Fail(line, $"'{attribute.Name}' is not valid on <PathFigure> (StartPoint/IsClosed/IsFilled plus PathFigure.Segments).");
        }
    }

    private static PathSegment ReadPathSegment(XElement node, IXmlLineInfo line)
    {
        switch (node.Name.LocalName)
        {
            case "LineSegment":
            {
                var segment = new LineSegment();
                foreach (var attribute in SegmentAttributes(node, line))
                    ApplyPathSegmentAttribute(segment, "LineSegment", attribute, line);
                return segment;
            }
            case "BezierSegment":
            {
                var segment = new BezierSegment();
                foreach (var attribute in SegmentAttributes(node, line))
                    ApplyPathSegmentAttribute(segment, "BezierSegment", attribute, line);
                return segment;
            }
            case "QuadraticBezierSegment":
            {
                var segment = new QuadraticBezierSegment();
                foreach (var attribute in SegmentAttributes(node, line))
                    ApplyPathSegmentAttribute(segment, "QuadraticBezierSegment", attribute, line);
                return segment;
            }
            case "ArcSegment":
            {
                var segment = new ArcSegment();
                foreach (var attribute in SegmentAttributes(node, line))
                    ApplyPathSegmentAttribute(segment, "ArcSegment", attribute, line);
                return segment;
            }
            default: throw Fail(line, $"<{node.Name.LocalName}> is not a supported path segment (LineSegment/BezierSegment/QuadraticBezierSegment/ArcSegment).");
        }
    }

    /// <summary>Applies one static path-segment attribute (shared by the
    /// runtime reader and bound template geometry).</summary>
    private static void ApplyPathSegmentAttribute(PathSegment segment, string element, XAttribute attribute, IXmlLineInfo line)
    {
        switch (segment)
        {
            case LineSegment lineSegment:
                if (attribute.Name.LocalName == "Point") { lineSegment.Point = ParsePointValue(attribute.Value, line); return; }
                throw Fail(line, $"'{attribute.Name}' is not valid on <{element}> (Point).");
            case BezierSegment bezier:
                switch (attribute.Name.LocalName)
                {
                    case "Point1": bezier.Point1 = ParsePointValue(attribute.Value, line); return;
                    case "Point2": bezier.Point2 = ParsePointValue(attribute.Value, line); return;
                    case "Point3": bezier.Point3 = ParsePointValue(attribute.Value, line); return;
                    default: throw Fail(line, $"'{attribute.Name}' is not valid on <{element}> (Point1/Point2/Point3).");
                }
            case QuadraticBezierSegment quadratic:
                switch (attribute.Name.LocalName)
                {
                    case "Point1": quadratic.Point1 = ParsePointValue(attribute.Value, line); return;
                    case "Point2": quadratic.Point2 = ParsePointValue(attribute.Value, line); return;
                    default: throw Fail(line, $"'{attribute.Name}' is not valid on <{element}> (Point1/Point2).");
                }
            case ArcSegment arc:
                switch (attribute.Name.LocalName)
                {
                    case "Point": arc.Point = ParsePointValue(attribute.Value, line); return;
                    case "Size": arc.Size = ParseSizeValue(attribute.Value, line); return;
                    case "RotationAngle": arc.RotationAngle = ParseDoubleValue("RotationAngle", attribute.Value, line); return;
                    case "IsLargeArc": arc.IsLargeArc = ParseBoolValue(attribute.Value, line); return;
                    case "SweepDirection":
                        if (string.Equals(attribute.Value, "Clockwise", StringComparison.OrdinalIgnoreCase))
                            arc.SweepDirection = SweepDirection.Clockwise;
                        else if (string.Equals(attribute.Value, "Counterclockwise", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(attribute.Value, "CounterClockwise", StringComparison.OrdinalIgnoreCase))
                            arc.SweepDirection = SweepDirection.Counterclockwise;
                        else throw Fail(line, $"'{attribute.Value}' is not a SweepDirection (Clockwise/Counterclockwise).");
                        return;
                    default: throw Fail(line, $"'{attribute.Name}' is not valid on <{element}> (Point/Size/RotationAngle/IsLargeArc/SweepDirection).");
                }
            default: throw Fail(line, $"<{element}> is not a supported path segment (LineSegment/BezierSegment/QuadraticBezierSegment/ArcSegment).");
        }
    }

    /// <summary>True when a template geometry subtree carries live
    /// values (markup attributes or composite bindings, like other template
    /// property content).</summary>
    private static bool HasTemplateGeometryBindings(XElement node) =>
        node.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Value.TrimStart().StartsWith('{')) ||
        node.Descendants().Any(e =>
            e.Name.LocalName is "MultiBinding" or "PriorityBinding" or "Binding" ||
            (e.Name.LocalName.Contains('.') && e.Name.LocalName is not "PathGeometry.Figures" and not "PathFigure.Segments"));

    /// <summary>Reads a template Path.Data geometry with live bindings
    /// (vendor progress rings bind figure/segment coordinates): static
    /// siblings parse through the shared appliers; {Binding ...} attributes
    /// defer through Freezable entries that re-measure the path;
    /// {x:Static ...} evaluates now; ArcSegment.Point holds one MultiBinding
    /// like other template composites.</summary>
    private static void ReadBoundTemplateGeometry(Path path, XElement node, List<TemplateBindingEntry> entries, IXmlLineInfo line)
    {
        if (node.Name.LocalName != "PathGeometry")
            throw Fail(line, $"<Path.Data> with bindings holds a <PathGeometry> (got <{node.Name.LocalName}>).");
        var geometry = new PathGeometry();
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            if (attribute.Name.LocalName == "FillRule") { geometry.FillRule = ParseFillRule(attribute.Value, line); continue; }
            throw Fail(line, $"'{attribute.Name}' is not valid on <PathGeometry> (FillRule plus PathGeometry.Figures).");
        }
        foreach (var child in node.Elements())
        {
            if (child.Name.LocalName == "PathGeometry.Figures")
            {
                foreach (var figures in child.Elements())
                {
                    if (figures.Name.LocalName != "PathFigureCollection")
                        throw Fail(line, $"<{figures.Name.LocalName}> is not a <PathFigureCollection>.");
                    foreach (var figure in figures.Elements())
                        geometry.Figures.Add(ReadBoundPathFigure(figure, path, entries, line));
                }
                continue;
            }
            geometry.Figures.Add(ReadBoundPathFigure(child, path, entries, line));
        }
        path.Data = geometry;
    }

    /// <summary>Reads one bound template figure (bare or wrapped segments;
    /// markup attributes defer, the rest parse statically).</summary>
    private static PathFigure ReadBoundPathFigure(XElement node, Path owner, List<TemplateBindingEntry> entries, IXmlLineInfo line)
    {
        if (node.Name.LocalName != "PathFigure")
            throw Fail(line, $"<{node.Name.LocalName}> is not a <PathFigure>.");
        var figure = new PathFigure();
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            ApplyBoundGeometryAttribute(figure, node.Name.LocalName, attribute, owner, entries, line);
        }
        foreach (var child in node.Elements())
        {
            if (child.Name.LocalName == "PathFigure.Segments")
            {
                foreach (var segments in child.Elements())
                {
                    if (segments.Name.LocalName != "PathSegmentCollection")
                        throw Fail(line, $"<{segments.Name.LocalName}> is not a <PathSegmentCollection>.");
                    foreach (var segment in segments.Elements())
                        figure.Segments.Add(ReadBoundPathSegment(segment, owner, entries, line));
                }
                continue;
            }
            figure.Segments.Add(ReadBoundPathSegment(child, owner, entries, line));
        }
        return figure;
    }

    /// <summary>Reads one bound template segment (markup attributes defer;
    /// ArcSegment.Point holds one MultiBinding, like template composites).</summary>
    private static PathSegment ReadBoundPathSegment(XElement node, Path owner, List<TemplateBindingEntry> entries, IXmlLineInfo line)
    {
        PathSegment segment = node.Name.LocalName switch
        {
            "LineSegment" => new LineSegment(),
            "BezierSegment" => new BezierSegment(),
            "QuadraticBezierSegment" => new QuadraticBezierSegment(),
            "ArcSegment" => new ArcSegment(),
            _ => throw Fail(line, $"<{node.Name.LocalName}> is not a supported path segment (LineSegment/BezierSegment/QuadraticBezierSegment/ArcSegment)."),
        };
        foreach (var attribute in SegmentAttributes(node, line))
            ApplyBoundGeometryAttribute(segment, node.Name.LocalName, attribute, owner, entries, line);
        foreach (var child in node.Elements())
        {
            if (segment is ArcSegment && child.Name.LocalName == "ArcSegment.Point" &&
                child.Elements().Count() == 1 && child.Elements().First().Name.LocalName == "MultiBinding")
            {
                var multi = ReadTemplateMultiBinding(child.Elements().First(), line);
                entries.Add(new TemplateBindingEntry(owner, "Point", string.Empty)
                {
                    FreezableBinding = new TemplateFreezableBinding
                    {
                        Target = segment,
                        Owner = owner,
                        Property = "Point",
                        Multi = multi,
                        RemeasuresOwner = true,
                    },
                });
                continue;
            }
            throw Fail(line, $"<{child.Name.LocalName}> is not valid in <{node.Name.LocalName}> (an <ArcSegment.Point> MultiBinding only).");
        }
        return segment;
    }

    /// <summary>Applies one template geometry attribute: {Binding ...}
    /// defers through a re-measuring Freezable entry, {x:Static ...}
    /// evaluates now, literals parse through the shared appliers.</summary>
    private static void ApplyBoundGeometryAttribute(object target, string element, XAttribute attribute, Path owner, List<TemplateBindingEntry> entries, IXmlLineInfo line)
    {
        var raw = attribute.Value.Trim();
        var local = attribute.Name.LocalName;
        if (raw.StartsWith("{x:Static ", StringComparison.Ordinal) && raw.EndsWith('}'))
        {
            SetGeometryProperty(target, local,
                ResolveStaticMember(raw["{x:Static".Length..^1].Trim(), raw, line), raw, line);
            return;
        }
        if (raw.StartsWith("{Binding", StringComparison.Ordinal) && raw.EndsWith('}'))
        {
            entries.Add(new TemplateBindingEntry(owner, local, string.Empty)
            {
                FreezableBinding = new TemplateFreezableBinding
                {
                    Target = target,
                    Owner = owner,
                    Property = local,
                    Binding = ParseStandaloneBinding(raw, local, line, "geometry"),
                    RemeasuresOwner = true,
                },
            });
            return;
        }
        if (raw.StartsWith('{'))
            throw Fail(line, $"'{raw}' is not supported on geometry '{local}' ({{Binding ...}} or {{x:Static ...}}).");
        if (target is PathFigure figure) { ApplyPathFigureAttribute(figure, attribute, line); return; }
        if (target is PathSegment segment) { ApplyPathSegmentAttribute(segment, element, attribute, line); return; }
        throw Fail(line, $"'{local}' is not a bound geometry target (<{target.GetType().Name}>).");
    }

    /// <summary>Sets one geometry CLR property from an evaluated static
    /// member (converter-aware, like other template literal landings).</summary>
    private static void SetGeometryProperty(object target, string property, object? value, string raw, IXmlLineInfo line)
    {
        var prop = target.GetType().GetProperty(property,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy)
            ?? throw Fail(line, $"'{property}' is not a property of <{target.GetType().Name}>.");
        if (!prop.CanWrite || prop.SetMethod?.IsPublic != true)
            throw Fail(line, $"'{property}' is read-only on <{target.GetType().Name}>.");
        object? final = value;
        if (final is string text)
            final = StyleValueConverter.Convert(text, prop.PropertyType)
                ?? throw Fail(line, $"'{raw}' does not convert to {prop.PropertyType.Name}.");
        else if (final is not null && !prop.PropertyType.IsInstanceOfType(final))
        {
            try { final = Convert.ChangeType(final, prop.PropertyType, System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
            {
                throw Fail(line, $"'{raw}' does not convert to {prop.PropertyType.Name}.");
            }
        }
        prop.SetValue(target, final);
    }

    private static IEnumerable<XAttribute> SegmentAttributes(XElement node, IXmlLineInfo line)
    {
        foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (IsIgnorableAttribute(attribute)) continue;
            yield return attribute;
        }
    }

    private static bool ParseBoolValue(string value, IXmlLineInfo line) =>
        bool.TryParse(value, out var parsed) ? parsed : throw Fail(line, $"'{value}' is not a boolean (True/False).");

    private static Size ParseSizeValue(string value, IXmlLineInfo line)
    {
        try { return Size.Parse(value); }
        catch (FormatException ex) { throw Fail(line, ex.Message); }
    }

    /// <summary>Reads an inline transform element (RenderTransform property forms).</summary>
    internal static Transform ReadInlineTransform(XElement node, IXmlLineInfo line, List<TemplateBindingEntry>? entries = null, FrameworkElement? owner = null, Dictionary<string, object>? names = null)
    {
        switch (node.Name.LocalName)
        {
            case "ScaleTransform":
            {
                var transform = new ScaleTransform();
                foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
                {
                    if (IsIgnorableAttribute(attribute)) continue;
                    if (names is not null && attribute.Name.LocalName == "Name" && attribute.Name.NamespaceName.Length == 0) continue;
                    switch (attribute.Name.LocalName)
                    {
                        case "ScaleX": if (!TryReadTransformBinding(transform, "ScaleX", attribute.Value, entries, owner, line)) transform.ScaleX = ParseDoubleValue("ScaleX", attribute.Value, line); break;
                        case "ScaleY": if (!TryReadTransformBinding(transform, "ScaleY", attribute.Value, entries, owner, line)) transform.ScaleY = ParseDoubleValue("ScaleY", attribute.Value, line); break;
                        case "CenterX": if (!TryReadTransformBinding(transform, "CenterX", attribute.Value, entries, owner, line)) transform.CenterX = ParseDoubleValue("CenterX", attribute.Value, line); break;
                        case "CenterY": if (!TryReadTransformBinding(transform, "CenterY", attribute.Value, entries, owner, line)) transform.CenterY = ParseDoubleValue("CenterY", attribute.Value, line); break;
                        default: throw Fail(line, $"'{attribute.Name}' is not valid on <ScaleTransform> (ScaleX/ScaleY/CenterX/CenterY).");
                    }
                }
                foreach (var _ in node.Elements())
                    throw Fail(line, "<ScaleTransform> takes attributes only.");
                return NameInlineTransform(transform, node, names, line);
            }
            case "TranslateTransform":
            {
                var transform = new TranslateTransform();
                foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
                {
                    if (IsIgnorableAttribute(attribute)) continue;
                    if (names is not null && attribute.Name.LocalName == "Name" && attribute.Name.NamespaceName.Length == 0) continue;
                    switch (attribute.Name.LocalName)
                    {
                        case "X": if (!TryReadTransformBinding(transform, "X", attribute.Value, entries, owner, line)) transform.X = ParseDoubleValue("X", attribute.Value, line); break;
                        case "Y": if (!TryReadTransformBinding(transform, "Y", attribute.Value, entries, owner, line)) transform.Y = ParseDoubleValue("Y", attribute.Value, line); break;
                        default: throw Fail(line, $"'{attribute.Name}' is not valid on <TranslateTransform> (X/Y).");
                    }
                }
                foreach (var child in node.Elements())
                    ReadTranslateTransformProperty(transform, child, entries, owner, line);
                return NameInlineTransform(transform, node, names, line);
            }
            case "RotateTransform":
            {
                var transform = new RotateTransform();
                foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
                {
                    if (IsIgnorableAttribute(attribute)) continue;
                    if (names is not null && attribute.Name.LocalName == "Name" && attribute.Name.NamespaceName.Length == 0) continue;
                    switch (attribute.Name.LocalName)
                    {
                        case "Angle": if (!TryReadTransformBinding(transform, "Angle", attribute.Value, entries, owner, line)) transform.Angle = ParseDoubleValue("Angle", attribute.Value, line); break;
                        case "CenterX": if (!TryReadTransformBinding(transform, "CenterX", attribute.Value, entries, owner, line)) transform.CenterX = ParseDoubleValue("CenterX", attribute.Value, line); break;
                        case "CenterY": if (!TryReadTransformBinding(transform, "CenterY", attribute.Value, entries, owner, line)) transform.CenterY = ParseDoubleValue("CenterY", attribute.Value, line); break;
                        default: throw Fail(line, $"'{attribute.Name}' is not valid on <RotateTransform> (Angle/CenterX/CenterY).");
                    }
                }
                foreach (var _ in node.Elements())
                    throw Fail(line, "<RotateTransform> takes attributes only.");
                return NameInlineTransform(transform, node, names, line);
            }
            case "SkewTransform":
            {
                var transform = new SkewTransform();
                foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
                {
                    if (IsIgnorableAttribute(attribute)) continue;
                    if (names is not null && attribute.Name.LocalName == "Name" && attribute.Name.NamespaceName.Length == 0) continue;
                    switch (attribute.Name.LocalName)
                    {
                        case "AngleX": if (!TryReadTransformBinding(transform, "AngleX", attribute.Value, entries, owner, line)) transform.AngleX = ParseDoubleValue("AngleX", attribute.Value, line); break;
                        case "AngleY": if (!TryReadTransformBinding(transform, "AngleY", attribute.Value, entries, owner, line)) transform.AngleY = ParseDoubleValue("AngleY", attribute.Value, line); break;
                        case "CenterX": if (!TryReadTransformBinding(transform, "CenterX", attribute.Value, entries, owner, line)) transform.CenterX = ParseDoubleValue("CenterX", attribute.Value, line); break;
                        case "CenterY": if (!TryReadTransformBinding(transform, "CenterY", attribute.Value, entries, owner, line)) transform.CenterY = ParseDoubleValue("CenterY", attribute.Value, line); break;
                        default: throw Fail(line, $"'{attribute.Name}' is not valid on <SkewTransform> (AngleX/AngleY/CenterX/CenterY).");
                    }
                }
                foreach (var _ in node.Elements())
                    throw Fail(line, "<SkewTransform> takes attributes only.");
                return NameInlineTransform(transform, node, names, line);
            }
            case "MatrixTransform":
            {
                // Named identity matrices (Transitions TransitioningContent
                // retargets): empty elements parse as identity; anything
                // else keeps the precise error.
                var transform = new MatrixTransform();
                foreach (var attribute in node.Attributes().Where(a => !a.IsNamespaceDeclaration))
                {
                    if (IsIgnorableAttribute(attribute)) continue;
                    if (names is not null && attribute.Name.LocalName == "Name" && attribute.Name.NamespaceName.Length == 0) continue;
                    throw Fail(line, $"'{attribute.Name}' is not valid on <MatrixTransform> (attribute-less identity).");
                }
                foreach (var _ in node.Elements())
                    throw Fail(line, "<MatrixTransform> takes attributes only.");
                return NameInlineTransform(transform, node, names, line);
            }
            default:
            { 
                if (node.Name.LocalName != "TransformGroup")
                    throw Fail(line, $"<{node.Name.LocalName}> is not a supported inline transform (TransformGroup/ScaleTransform/TranslateTransform/RotateTransform/SkewTransform/MatrixTransform).");
                // Grouped inline transforms (MaterialDesign progress/overlay
                // templates): children recurse through this same reader.
                var group = new TransformGroup();
                foreach (var child in node.Elements())
                    group.Children.Add(ReadInlineTransform(child, line, entries, owner, names));
                return NameInlineTransform(group, node, names, line);
            }
        }
    }

    /// <summary>Registers an x:Name'd inline transform in the template namescope
    /// (storyboard TargetName and ElementName bindings address named transforms,
    /// e.g. the circular ProgressBar FullyIndeterminateGridScaleTransform; WPF
    /// registers every template x:Name, not just visuals).</summary>
    /// <summary>Reads one TranslateTransform property element
    /// (MaterialDesign RatingBar preview-indicator offsets): a
    /// &lt;TranslateTransform.X&gt;/&lt;TranslateTransform.Y&gt; nesting one
    /// &lt;Binding&gt; or &lt;MultiBinding&gt; defers through a
    /// FreezableBinding entry like the attribute form; anything else keeps
    /// the precise error.</summary>
    private static void ReadTranslateTransformProperty(TranslateTransform transform, XElement child, List<TemplateBindingEntry>? entries, FrameworkElement? owner, IXmlLineInfo line)
    {
        var local = child.Name.LocalName;
        var dot = local.IndexOf('.');
        var property = dot >= 0 ? local[(dot + 1)..] : local;
        if (!string.Equals(local, "TranslateTransform." + property, StringComparison.Ordinal) || (property != "X" && property != "Y"))
            throw Fail(line, $"<TranslateTransform> takes X/Y attributes or <TranslateTransform.X>/<TranslateTransform.Y> (got <{local}>).");
        if (entries is null || owner is null)
            throw Fail(line, $"'<{local}>' binds TranslateTransform.{property} (bindings in runtime inline transforms stay a precise boundary).");
        var content = child.Elements().ToList();
        if (content.Count != 1 || (content[0].Name.LocalName != "Binding" && content[0].Name.LocalName != "MultiBinding"))
            throw Fail(line, $"'<{local}>' holds one <Binding> or <MultiBinding>.");
        var freezable = new TemplateFreezableBinding
        {
            Target = transform,
            Owner = owner,
            Property = property,
        };
        if (content[0].Name.LocalName == "Binding")
            freezable.Binding = ReadTemplateMultiChild(content[0], line);
        else
            freezable.Multi = ReadTemplateMultiBinding(content[0], line);
        entries.Add(new TemplateBindingEntry(owner, property, string.Empty) { FreezableBinding = freezable });
    }

    private static Transform NameInlineTransform(Transform transform, XElement node, Dictionary<string, object>? names, IXmlLineInfo line)
    {
        if (names is null) return transform;
        var transformName = (string?)node.Attribute(XName.Get("Name", XamlNamespace)) ?? (string?)node.Attribute("Name");
        if (transformName is not null) RegisterTemplateName(names, transform, transformName, line);
        return transform;
    }

    /// <summary>Reads one transform double attribute (bindings defer through entries).</summary>
    /// <summary>Reads one transform TemplateBinding attribute (Expander arrow
    /// rotation): the templated-parent path defers through a FreezableBinding
    /// entry; x:Static converters resolve now, StaticResource keys defer.</summary>
    private static bool TryReadTemplateTransformBinding(Transform transform, string property, string text, List<TemplateBindingEntry>? entries, FrameworkElement? owner, IXmlLineInfo line)
    {
        if (entries is null || owner is null)
            throw Fail(line, $"'{text}' binds {transform.GetType().Name}.{property} (bindings in runtime inline transforms stay a precise boundary).");
        var body = text["{TemplateBinding ".Length..^1].Trim();
        var segment = body.Split(',', 2)[0].Trim();
        var bound = segment;
        if (bound.StartsWith("Property=", StringComparison.Ordinal))
            bound = bound["Property=".Length..].Trim();
        if (bound.Length == 0 || bound.Contains('{') || bound.Contains('='))
            throw Fail(line, $"'{text}' is not a TemplateBinding (needs a property name).");
        var binding = new Binding
        {
            Path = bound,
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        };
        var rest = body[segment.Length..].Trim();
        if (rest.StartsWith(",", StringComparison.Ordinal)) rest = rest[1..].Trim();
        if (rest.Length > 0)
        {
            foreach (var option in BindingOptions(rest))
            {
                var opt = option.Trim();
                const string convPrefix = "Converter={StaticResource ";
                const string staticConvPrefix = "Converter={x:Static ";
                const string paramPrefix = "ConverterParameter=";
                if (opt.StartsWith(staticConvPrefix, StringComparison.Ordinal) && opt.EndsWith('}'))
                {
                    if (binding.Converter is not null || binding.DeferredConverterKey is not null)
                        throw Fail(line, $"'{text}' takes Converter once only.");
                    var member = ResolveStaticMember(opt[staticConvPrefix.Length..^1].Trim(), text, line);
                    if (member is not IValueConverter converter)
                        throw Fail(line, $"'{text}' takes Converter as an IValueConverter singleton ({{x:Static ...}}).");
                    binding.Converter = converter;
                }
                else if (opt.StartsWith(convPrefix, StringComparison.Ordinal) && opt.EndsWith('}'))
                {
                    if (binding.Converter is not null || binding.DeferredConverterKey is not null)
                        throw Fail(line, $"'{text}' takes Converter once only.");
                    binding.DeferredConverterKey = ResolveTemplateConverterKey(opt, line);
                }
                else if (opt.StartsWith(paramPrefix, StringComparison.Ordinal))
                {
                    if (binding.ConverterParameter is not null)
                        throw Fail(line, $"'{text}' takes ConverterParameter once only.");
                    var pval = opt[paramPrefix.Length..].Trim();
                    if (pval.Length == 0 || pval.StartsWith('{'))
                        throw Fail(line, $"'{text}' takes ConverterParameter as a literal (resource parameters stay a boundary).");
                    binding.ConverterParameter = pval;
                }
                else throw Fail(line, $"'{text}' takes a property name plus Converter/ConverterParameter.");
            }
        }
        entries.Add(new TemplateBindingEntry(owner, property, string.Empty)
        {
            FreezableBinding = new TemplateFreezableBinding
            {
                Target = transform,
                Owner = owner,
                Property = property,
                Binding = binding,
            }
        });
        return true;
    }

    private static bool TryReadTransformBinding(Transform transform, string property, string raw, List<TemplateBindingEntry>? entries, FrameworkElement? owner, IXmlLineInfo line)
    {
        var text = raw.Trim();
        if (text.StartsWith("{TemplateBinding ", StringComparison.Ordinal) && text.EndsWith('}'))
            return TryReadTemplateTransformBinding(transform, property, text, entries, owner, line);
        if (!text.StartsWith("{Binding", StringComparison.Ordinal) || !text.EndsWith('}')) return false;
        if (entries is null || owner is null)
            throw Fail(line, $"'{text}' binds {transform.GetType().Name}.{property} (bindings in runtime inline transforms stay a precise boundary).");
        entries.Add(new TemplateBindingEntry(owner, property, string.Empty)
        {
            FreezableBinding = new TemplateFreezableBinding
            {
                Target = transform,
                Owner = owner,
                Property = property,
                Binding = ParseStandaloneBinding(text, property, line),
            }
        });
        return true;
    }

    /// <summary>Parses a transform {Binding} (RelativeSource/Converter/ConverterParameter like template bindings).</summary>
    private static Binding ParseStandaloneBinding(string value, string property, IXmlLineInfo line, string context = "transform")
    {
        var body = value[1..^1].Trim()["Binding".Length..];
        var options = BindingOptions(body);
        var relative = ExtractNestedOption(options, "RelativeSource");
        var source = ExtractNestedOption(options, "Source");
        var converter = ExtractNestedOption(options, "Converter");
        var parameter = ExtractNestedOption(options, "ConverterParameter");
        Binding parsed;
        try { parsed = ParseBinding(string.Join(',', options), value, line); }
        catch (ArgumentException ex) { throw Fail(line, ex.Message); }
        if (source is not null)
        {
            // Explicit sources (theme geometry binds its proxy through
            // Source={StaticResource Key}): StaticResource keys defer to
            // inflation scope, x:Static members evaluate now, literals ride raw.
            var text = source.Trim();
            if (text.StartsWith("{StaticResource ", StringComparison.Ordinal) && text.EndsWith('}'))
            {
                var sourceKey = text["{StaticResource ".Length..^1].Trim();
                if (sourceKey.Length == 0 || sourceKey.Contains('{') || sourceKey.Contains('='))
                    throw Fail(line, $"'{value}' takes Source as {{StaticResource Key}}, {{x:Static ...}} or a literal.");
                parsed.DeferredSourceKey = sourceKey;
            }
            else if (text.StartsWith("{x:Static ", StringComparison.Ordinal) && text.EndsWith('}'))
                parsed.Source = ResolveStaticMember(text["{x:Static ".Length..^1].Trim(), value, line);
            else if (!text.StartsWith('{'))
                parsed.Source = text;
            else throw Fail(line, $"'{value}' takes Source as {{StaticResource Key}}, {{x:Static ...}} or a literal.");
        }
        if (relative is not null)
        {
            try { parsed.RelativeSource = ParseRelativeSource(relative, line); }
            catch (ArgumentException ex) { throw Fail(line, ex.Message); }
        }
        if (converter is not null) ApplyBindingConverter(parsed, converter, line);
        if (parameter is not null)
            parsed.ConverterParameter = ParseStandaloneParameter(parameter, property, line);
        if (parsed.Mode is BindingMode.TwoWay or BindingMode.OneWayToSource)
            throw Fail(line, $"'{value}' on {context} '{property}' takes OneWay/OneTime (two-way stays a precise boundary).");
        if (string.IsNullOrEmpty(parsed.Path.Path))
            throw Fail(line, $"'{value}' needs Path= for {context} '{property}'.");
        return parsed;
    }

    /// <summary>Resolves a ConverterParameter (literal or {x:Static ...}; other markup stays precise).</summary>
    private static object ParseLiteralOrStaticParameter(string raw, string original, IXmlLineInfo line)
    {
        var text = raw.Trim();
        if (text.StartsWith("{x:Static ", StringComparison.Ordinal) && text.EndsWith('}'))
            return ResolveStaticMember(text["{x:Static ".Length..^1].Trim(), original, line);
        if (!text.StartsWith('{'))
            return raw;
        throw Fail(line, $"'{original}' takes a literal or {{x:Static ...}} ConverterParameter.");
    }

    /// <summary>Parses a transform ConverterParameter (literal or {x:Static ...}).</summary>
    private static object ParseStandaloneParameter(string raw, string property, IXmlLineInfo line)
    {
        var text = raw.Trim();
        if (text.StartsWith("{x:Static ", StringComparison.Ordinal) && text.EndsWith('}'))
            return ResolveStaticMember(text["{x:Static ".Length..^1].Trim(), raw, line);
        if (!text.StartsWith('{'))
            return text;
        throw Fail(line, $"'{raw}' is not a ConverterParameter for transform '{property}' (literal or {{x:Static ...}}).");
    }

    /// <summary>Parses one template trigger (property Trigger or RoutedEvent EventTrigger).</summary>
    /// <summary>Builds an easing/brush scope lookup (template-local resources
    /// first, then hydration scopes, then application scope — the StaticResource
    /// order inline trigger storyboards share with keyed theme storyboards).</summary>
    private static Func<string, object?> TriggerScopeResolver(ResourceDictionary? local, ResourceDictionary? outer = null) => key =>
    {
        if (local is not null && local.TryGetValue(key, out var own) && own is not null) return own;
        // Style-level resources (SmartHint AnimationEasingFunction): the
        // style scope sits between template resources and hydration,
        // like the WPF lookup chain.
        if (outer is not null && outer.TryGetValue(key, out var styled) && styled is not null) return styled;
        if (TryFindHydrationResource(key, out var hydrated) && hydrated is not null) return hydrated;
        if (Application.Current?.Resources.TryGetValue(key, out var app) == true) return app;
        return null;
    };

    private static TriggerBase ReadTemplateTrigger(XElement node, Type? targetType, IXmlLineInfo line, List<XamlEventWire>? wires = null, Func<string, object?>? resolveResource = null)
    {
        if (node.Name.LocalName is "Trigger" or "MultiTrigger" or "DataTrigger" or "MultiDataTrigger")
            return ReadTrigger(node, targetType, line, resolveResource);
        if (node.Name.LocalName == "EventTrigger")
        {
            var routed = (string?)node.Attribute("RoutedEvent")
                ?? throw Fail(line, "<EventTrigger> needs RoutedEvent=\"[Owner.]Event\".");
            var trigger = new EventTrigger { RoutedEvent = routed };
            ValidateRoutedName(routed, line);
            foreach (var child in node.Elements())
            {
                if (child.Name.LocalName == "EventTrigger.Actions")
                {
                    foreach (var action in child.Elements())
                        trigger.Actions.Add(ReadTriggerAction(action, line, wires, resolveResource));
                    continue;
                }
                trigger.Actions.Add(ReadTriggerAction(child, line, wires, resolveResource));
            }
            return trigger;
        }
        throw Fail(line, $"<{node.Name.LocalName}> is not a supported trigger (Trigger/DataTrigger/MultiTrigger/MultiDataTrigger/EventTrigger).");
    }

    private static Setter ReadTriggerSetter(XElement setter, IXmlLineInfo line, Type? targetType, ResourceDictionary? styleResources = null)
    {
        var property = (string?)setter.Attribute("Property")
            ?? throw Fail(line, "Trigger Setter needs Property.");
        var valueAttr = (string?)setter.Attribute("Value");
        var valueNode = setter.Elements().FirstOrDefault(e => e.Name.LocalName == "Setter.Value");
        if (valueAttr is not null && valueNode is not null)
            throw Fail(line, "Trigger Setter takes Value= or <Setter.Value>, not both.");
        var target = (string?)setter.Attribute("TargetName");
        if (valueNode is not null)
        {
            // Property-element values (the switch track fill): bindings parse
            // like template bindings; brush elements read inline; text lands
            // as a literal for the part-type conversion at apply.
            var valueChildren = valueNode.Elements().ToList();
            if (valueChildren.Count > 1)
                throw Fail(line, "<Setter.Value> holds one value element.");
            var valueChild = valueChildren.FirstOrDefault();
            object? elementValue;
            if (valueChild is null) elementValue = valueNode.Value;
            else if (valueChild.Name.LocalName == "Binding") elementValue = ReadTemplateMultiChild(valueChild, line);
            else if (valueChild.Name.LocalName == "MultiBinding") elementValue = ReadTemplateMultiBinding(valueChild, line);
            else if (valueChild.Name.LocalName is "ScaleTransform" or "TranslateTransform" or "RotateTransform" or "SkewTransform" or "TransformGroup") elementValue = ReadInlineTransform(valueChild, line);
            else if (valueChild.Name.LocalName == "ControlTemplate") elementValue = ReadControlTemplate(valueChild, targetType, line, styleResources);
            else if (valueChild.Name.LocalName is "DataTemplate" or "HierarchicalDataTemplate") elementValue = ReadDataTemplateNode(valueChild, line);
            else if (valueChild.Name.LocalName == "ItemsPanelTemplate")
            {
                using var panelReader = valueChild.CreateReader();
                panelReader.Read();
                elementValue = ReadItemsPanelTemplateElement(panelReader, line);
            }
            else elementValue = ReadInlineBrush(valueChild, line);
            return new Setter { AttachedProperty = property, Value = elementValue, TargetName = target };
        }
        var value = valueAttr
            ?? throw Fail(line, "Trigger Setter needs Value.");
        // TargetName setters address the named part, not the trigger owner:
        // plain literals defer to apply time (the BAML path lands them the
        // same way); markup forms still convert eagerly (they are
        // type-independent).
        if (target is not null && !property.Contains('.') && !value.TrimStart().StartsWith('{'))
            return new Setter { AttachedProperty = property, Value = value, TargetName = target };
        var converted = ConvertTriggerValue(property, value, targetType, line, false, prefix => setter.GetNamespaceOfPrefix(prefix)?.NamespaceName);
        return new Setter { AttachedProperty = property, Value = converted, TargetName = target };
    }

    /// <summary>Converts a trigger property/value pair through the owner property type when known.</summary>
    private static object? ConvertTriggerValue(string property, string? raw, Type? targetType, IXmlLineInfo line, bool rawLiteral = false, Func<string, string?>? xmlns = null)
    {
        if (raw is null) return null;
        var text = raw.Trim();
        if (text == "{x:Null}") return null;
        if (text.StartsWith("{DynamicResource ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            var key = text["{DynamicResource ".Length..^1].Trim();
            if (key.Length == 0 || key.Contains('='))
                throw Fail(line, $"Trigger value '{raw}' takes a plain key ({{DynamicResource Key}}).");
            if (key.Contains('{'))
            {
                if (!key.StartsWith("{x:Static ", StringComparison.Ordinal) || !key.EndsWith('}'))
                    throw Fail(line, $"Trigger value '{raw}' takes a plain key ({{DynamicResource Key}}).");
                var nested = ResolveStaticMember(key[1..^1]["x:Static".Length..].Trim(), raw, line);
                if (nested is not ResourceKey staticKey)
                    throw Fail(line, $"Trigger value '{raw}' needs a resource key (x:Static must name a ResourceKey).");
                var staticName = staticKey.ToString();
                if (string.IsNullOrEmpty(staticName))
                    throw Fail(line, $"Trigger value '{raw}' needs a resource key (x:Static must name a ResourceKey).");
                key = staticName;
            }
            return new TriggerResourceReference(key, IsDynamic: true);
        }
        if (text.StartsWith("{StaticResource ", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            var key = text["{StaticResource ".Length..^1].Trim();
            if (key.Length == 0 || key.Contains('='))
                throw Fail(line, $"Trigger value '{raw}' takes a plain key ({{StaticResource Key}}).");
            if (key.Contains('{'))
            {
                if (!key.StartsWith("{x:Static ", StringComparison.Ordinal) || !key.EndsWith('}'))
                    throw Fail(line, $"Trigger value '{raw}' takes a plain key ({{StaticResource Key}}).");
                var nested = ResolveStaticMember(key[1..^1]["x:Static".Length..].Trim(), raw, line);
                if (nested is not ResourceKey staticKey)
                    throw Fail(line, $"Trigger value '{raw}' needs a resource key (x:Static must name a ResourceKey).");
                var staticName = staticKey.ToString();
                if (string.IsNullOrEmpty(staticName))
                    throw Fail(line, $"Trigger value '{raw}' needs a resource key (x:Static must name a ResourceKey).");
                key = staticName;
            }
            return new TriggerResourceReference(key, IsDynamic: false);
        }
        if (text.StartsWith("{Binding", StringComparison.Ordinal) && text.EndsWith('}'))
        {
            // Trigger-set {Binding} values (MaterialDesign hover overlays):
            // deferred live bindings, attached when the trigger activates
            // and cleared on revert, like WPF. Decomposed like trigger
            // condition bindings (nested RelativeSource/Converter split out
            // before the shared string-form parser runs).
            var inner = text[1..^1].Trim();
            if (!inner.StartsWith("Binding", StringComparison.Ordinal))
                throw Fail(line, $"Trigger value '{raw}' needs {{Binding ...}} markup.");
            var options = BindingOptions(inner["Binding".Length..]);
            var relative = ExtractNestedOption(options, "RelativeSource");
            var converter = ExtractNestedOption(options, "Converter");
            var parameter = ExtractNestedOption(options, "ConverterParameter");
            var binding = XamlLoader.ParseBinding(string.Join(',', options), raw, line);
            if (relative is not null) binding.RelativeSource = ParseRelativeSource(relative, line);
            if (converter is not null) ApplyBindingConverter(binding, converter, line);
            if (parameter is not null)
                binding.ConverterParameter = ParseLiteralOrStaticParameter(parameter, raw, line);
            return binding;
        }
            if (text.StartsWith("{x:Static ", StringComparison.Ordinal) && text.EndsWith('}'))
            {
                // Static member values (theme opacity constants): resolve now,
                // coerce at apply against the part or trigger type. Prefixed
                // statics (wpf:Constants) resolve through the declaring xmlns
                // when the blind scan stays ambiguous, like element attributes.
                var memberText = text[1..^1]["x:Static".Length..].Trim();
                try { return ResolveStaticMember(memberText, raw, line); }
                catch (XamlLoadException) when (xmlns is not null)
                {
                    return ResolveConvertedStatic(memberText, raw, line, xmlns);
                }
            }
        if (text.StartsWith('{'))
            throw Fail(line, $"Trigger value '{raw}' needs a literal, {{x:Null}} or resource reference.");
        // SourceName conditions address a template part whose type is known
        // only at inflation; literals stay raw and compare by coercion there.
        if (rawLiteral) return text;
        var dot = property.IndexOf('.');
        var ownerName = dot >= 0 ? property[..dot] : null;
        var shortName = dot >= 0 ? property[(dot + 1)..] : property;
        Type? owner = ownerName is null ? targetType : ResolveTriggerOwner(ownerName, line);
        if (owner is null) throw Fail(line, $"Trigger property '{property}' names unknown type '{ownerName}'.");
        var info = owner.GetProperty(shortName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (info is not null)
        {
            if (IsAutoLength(shortName, info.PropertyType, text)) return double.NaN;
            try { return StyleValueConverter.Convert(text, info.PropertyType) ?? throw Fail(line, $"Trigger value '{raw}' converted to null for '{property}'."); }
            catch (FormatException ex) { throw Fail(line, ex.Message); }
            catch (ArgumentException ex) { throw Fail(line, ex.Message); }
        }
        // Attached DPs (engine Grid.Row, vendor ShadowAssist.Darken):
        // convert through the dependency property type, like WPF.
        var attached = DependencyProperty.FromName(owner, shortName);
        if (attached is null)
        {
            // Bag-only attached properties (DockPanel.Dock): convert through
            // the Get<Property> accessor return type, like WPF resolution.
            var getter = owner.GetMethod("Get" + shortName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            var getterParams = getter?.GetParameters();
            if (getter is not null && getterParams is { Length: 1 })
            {
                if (IsAutoLength(shortName, getter.ReturnType, text)) return double.NaN;
                try { return StyleValueConverter.Convert(text, getter.ReturnType) ?? throw Fail(line, $"Trigger value '{raw}' converted to null for '{property}'."); }
                catch (FormatException ex) { throw Fail(line, ex.Message); }
                catch (ArgumentException ex) { throw Fail(line, ex.Message); }
            }
            throw Fail(line, $"Trigger property '{property}' is not a property of <{owner.Name}>.");
        }
        if (IsAutoLength(shortName, attached.PropertyType, text)) return double.NaN;
        try { return StyleValueConverter.Convert(text, attached.PropertyType) ?? throw Fail(line, $"Trigger value '{raw}' converted to null for '{property}'."); }
        catch (FormatException ex) { throw Fail(line, ex.Message); }
        catch (ArgumentException ex) { throw Fail(line, ex.Message); }
    }

    /// <summary>LengthConverter parity for trigger literals (native maps
    /// "Auto" to NaN for the six double dimension properties).</summary>
    private static bool IsAutoLength(string shortName, Type type, string text) =>
        type == typeof(double) && shortName is "Width" or "Height" or "MinWidth" or
        "MaxWidth" or "MinHeight" or "MaxHeight" &&
        string.Equals(text, "Auto", StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolves a trigger property owner (engine, converted entry,
    /// or unique converted scan; xmlns prefixes are cosmetic, like the
    /// static-member rule).</summary>
    private static Type? ResolveTriggerOwner(string ownerName, IXmlLineInfo line)
    {
        var shortName = ownerName.Contains(':') ? ownerName[(ownerName.IndexOf(':') + 1)..] : ownerName;
        return FindEngineType(ownerName) ?? FindEngineType(shortName) ?? FindConvertedType(shortName)
            ?? ScanUniqueConvertedType(shortName, line, ownerName);
    }

    private static void ValidateRoutedName(string routed, IXmlLineInfo line)
    {
        var dot = routed.IndexOf('.');
        var ownerName = dot >= 0 ? routed[..dot] : null;
        var eventName = dot >= 0 ? routed[(dot + 1)..] : routed;
        if (eventName.Length == 0) throw Fail(line, $"'{routed}' is not a routed event name ([Owner.]Event).");
        if (ownerName is not null && FindEngineType(ownerName) is null)
            throw Fail(line, $"'{routed}' names unknown type '{ownerName}'.");
    }

    private static TriggerAction ReadTriggerAction(XElement node, IXmlLineInfo line, List<XamlEventWire>? wires = null, Func<string, object?>? resolveResource = null)
    {
        if (node.Name.LocalName == "RemoveStoryboard")
        {
            // Named removal (the DataGrid header sort indicator show/hide):
            // references the BeginStoryboard action by name, like WPF.
            var beginName = ((string?)node.Attribute("BeginStoryboardName") ?? string.Empty).Trim();
            if (beginName.Length == 0)
                throw Fail(line, "<RemoveStoryboard> needs BeginStoryboardName=.");
            if (node.Elements().Any() || node.Attributes().Count(a => !a.IsNamespaceDeclaration) != 1)
                throw Fail(line, "<RemoveStoryboard> takes BeginStoryboardName= only.");
            return new RemoveStoryboard { BeginStoryboardName = beginName };
        }
        if (node.Name.LocalName != "BeginStoryboard")
            throw Fail(line, $"<{node.Name.LocalName}> is not a supported trigger action (BeginStoryboard/RemoveStoryboard).");
        var action = new BeginStoryboard { Name = (string?)node.Attribute("Name") };
        var storyboardAttr = (string?)node.Attribute("Storyboard");
        var storyboardNode = node.Elements().FirstOrDefault();
        if (storyboardNode is null && storyboardAttr is not null && resolveResource is not null)
        {
            // Referenced storyboards (the switch ripple show/hide boards):
            // resolve like keyed theme storyboards, then run.
            var boardText = storyboardAttr.Trim();
            if (!boardText.StartsWith("{StaticResource ", StringComparison.Ordinal) || !boardText.EndsWith('}'))
                throw Fail(line, "<BeginStoryboard> Storyboard= needs {StaticResource Key}.");
            var boardKey = boardText["{StaticResource ".Length..^1].Trim();
            action.Storyboard = resolveResource(boardKey) as Storyboard
                ?? throw Fail(line, $"<BeginStoryboard> Storyboard '{boardKey}' is not a storyboard.");
            return action;
        }
        var nestedBoard = storyboardNode
            ?? throw Fail(line, "<BeginStoryboard> needs a nested <Storyboard>.");
        action.Storyboard = ReadInlineStoryboard(nestedBoard, line, wires, resolveResource);
        return action;
    }

    /// <summary>Parses an inline storyboard fragment (trigger actions and
    /// visual states share it; namespaced fragments re-parse standalone).</summary>
    private static Storyboard ReadInlineStoryboard(XElement storyboardNode, IXmlLineInfo line, List<XamlEventWire>? wires = null, Func<string, object?>? resolveResource = null)
    {
        if (storyboardNode.Name.LocalName != "Storyboard")
            throw Fail(line, $"<{storyboardNode.Name.LocalName}> is not a <Storyboard>.");
        var attrs = storyboardNode.Attributes()
            .Where(a => !a.IsNamespaceDeclaration && !IsIgnorableAttribute(a))
            .Select(a => (a.Name.LocalName == "Completed" ? "Completed" : a.Name.ToString(), a.Value))
            .ToList();
        var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(storyboardNode.ToString()),
            new System.Xml.XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true });
        reader.MoveToContent();
        return ReadTimelineAsStoryboard(reader, line, attrs, wires, resolveResource);
    }

    private static Storyboard ReadTimelineAsStoryboard(
        System.Xml.XmlReader reader, IXmlLineInfo line, List<(string Name, string Value)> attrs, List<XamlEventWire>? wires = null, Func<string, object?>? resolveResource = null)
    {
        // Completed= without a loading component is a precise error inside ReadTimelineEntry.
        return (Storyboard)ReadTimelineEntry(reader, line, "Storyboard", attrs, wires, resolveResource);
    }
}
