using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Xml;

namespace DesktopWasm.Wpf;

// Slice-97 HandyControl language-markup fallback: an engine-owned
// LangExtension plus the LangTable/LangValue string source behind it.
//
// Why a mirror instead of the converted types: the converted HandyControl
// and HandyControlDemo images carry real LangExtension subclasses whose
// ProvideValue implements exactly the algorithm below (two-phase string /
// Binding keys, mode/explicit storyboard knobs, DataContextChanged rebind;
// verified instruction-by-instruction against the staged HandyControl
// binary with Mono.Cecil), but their value pipeline is unreachable at
// runtime. LangProvider.GetLang goes through ResourceManager, whose
// neutral-language check enumerates assembly-level attributes, and the
// converter left those TypeRefs dangling
// (System.Xaml Xmlns rows onto System.Collections,
// PresentationCore content-file rows onto System.Threading), so every
// such enumeration throws TypeLoadException. AppDomain.TypeResolve does
// not fire on this CLR path (proven with a gated probe: zero hits), and
// even System.Reflection.CustomAttributeData enumeration throws, so the
// runtime cannot repair or even read those rows. The mirror therefore
// replays the upstream algorithm with the strings read straight from the
// converted images' embedded .resources (the same manifest bytes the
// pack store already reads; ResourceReader never touches attributes),
// keyed per owning assembly: ex:Lang (HandyControlDemo.Tools.Extension,
// a pure marker subclass of the lib extension) defaults to the demo
// table (231 keys, e.g. PleaseWait="请稍后..."), hc:Lang (the lib
// extension) to the lib table (40 keys, e.g. Close="关闭"). Live
// language switching rides the engine binding engine through per-site
// LangValue adapters (Value path), because the engine path evaluator
// has no indexer support for a direct key path. Culture set/remove only
// re-pushes (staged sets ship neutral resources; no satellites), which
// the boundary comment on LangTable.Culture states.

/// <summary>
/// Language string table for one converted image (mirrors the lookup half
/// of HandyControl's LangProvider without its poisoned ResourceManager
/// path: the neutral .resources are read raw like every other pack
/// payload, so assembly-attribute TypeRefs never load).
/// </summary>
public sealed class LangTable
{
    private static readonly Dictionary<string, LangTable> Cache = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    private readonly Dictionary<string, LangValue> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _strings;

    private LangTable(Dictionary<string, string> strings) => _strings = strings;

    private CultureInfo _culture = CultureInfo.CurrentCulture;

    /// <summary>
    /// Culture knob (mirrors LangProvider.Culture). Setting it re-pushes
    /// every live key (staged sets ship neutral resources only, so values
    /// persist; satellites stay a boundary).
    /// </summary>
    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            _culture = value;
            RefreshAll();
        }
    }

    /// <summary>
    /// Table for the image owning the resolved Lang extension (one per
    /// assembly simple name). Missing tables fail loudly: a Lang head
    /// without its converted string table has no value source.
    /// </summary>
    public static LangTable ForAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var simple = assembly.GetName().Name ?? assembly.FullName ?? "unknown";
        lock (Gate)
        {
            if (Cache.TryGetValue(simple, out var hit)) return hit;
            var resource = simple + ".Properties.Langs.Lang.resources";
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException(
                    $"Converted image '{simple}' carries no language table '{resource}'.");
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            var reader = new System.Resources.ResourceReader(stream);
            foreach (System.Collections.DictionaryEntry entry in reader)
            {
                if (entry.Key is string key && entry.Value is string text)
                    strings[key] = text;
            }
            var table = new LangTable(strings);
            Cache[simple] = table;
            return table;
        }
    }

    /// <summary>Translated string for a key (null when the table lacks it, like ResourceManager).</summary>
    public string? GetString(string key) =>
        key is not null && _strings.TryGetValue(key, out var text) ? text : null;

    /// <summary>Live value holder for one key (one per site; the binding Source).</summary>
    internal LangValue GetOrAddValue(string key)
    {
        lock (Gate)
        {
            if (!_values.TryGetValue(key, out var value))
            {
                value = new LangValue(this, key);
                _values[key] = value;
            }
            return value;
        }
    }

    private void RefreshAll()
    {
        lock (Gate)
        {
            foreach (var value in _values.Values)
                value.Refresh();
        }
    }
}

/// <summary>Per-site live language value (binds Value; refreshes on Culture set).</summary>
internal sealed class LangValue : INotifyPropertyChanged
{
    private readonly LangTable _table;
    private readonly string _key;

    internal LangValue(LangTable table, string key)
    {
        _table = table;
        _key = key;
        Refresh();
    }

    /// <summary>Current translation (null when the table lacks the key).</summary>
    public string? Value { get; private set; }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Refresh()
    {
        var next = _table.GetString(_key);
        if (!Equals(Value, next))
        {
            Value = next;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }
}

/// <summary>
/// Language markup extension (mirrors HandyControl.Tools.Extension.
/// LangExtension: Key/Mode/Converter/ConverterParameter/Source plus the
/// public KeyProperty; ProvideValue replays the converted algorithm
/// against <see cref="LangTable"/> instead of the poisoned provider).
/// The loader assigns Source from the resolved extension's owning image
/// when XAML omits it (demo subclass Yen defaults to the demo table,
/// lib extension to the lib table, like the converted ctors).
/// </summary>
public class LangExtension : MarkupExtension
{
    /// <summary>Language key (string) or key binding (Binding two-phase).</summary>
    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached("Key", typeof(object), typeof(LangExtension), new PropertyMetadata());

    private static readonly DependencyProperty TargetPropertyProperty =
        DependencyProperty.RegisterAttached("TargetProperty", typeof(object), typeof(LangExtension), new PropertyMetadata());

    private readonly DependencyObject _proxy = new();

    /// <summary>Creates an extension with no key (set Key before use).</summary>
    public LangExtension()
    {
    }

    /// <summary>Creates an extension for a key.</summary>
    public LangExtension(object key) => Key = key;

    /// <summary>Language key (string) or key binding (stored on a private proxy, like upstream).</summary>
    public object? Key
    {
        get => _proxy.GetValue(KeyProperty);
        set => _proxy.SetValue(KeyProperty, value);
    }

    /// <summary>Binding mode knob (kept for parity; lang bindings stay OneWay like upstream).</summary>
    public BindingMode Mode { get; set; }

    /// <summary>Value converter applied to the translated string.</summary>
    public IValueConverter? Converter { get; set; }

    /// <summary>Converter parameter.</summary>
    public object? ConverterParameter { get; set; }

    /// <summary>Explicit source (null means the loader-assigned language table).</summary>
    public object? Source { get; set; }

    /// <summary>
    /// Produces the translated value (live binding when the target takes
    /// one). Upstream binds DependencyProperty targets only (WPF Text is
    /// a DP); the mirror additionally binds CLR-backed properties by name
    /// the engine way (the staged views' Lang targets, like TextBlock.Text,
    /// are CLR properties here, and {Binding} attributes bind them through
    /// FrameworkElement.SetBinding(name, binding)).
    /// </summary>
    public override object? ProvideValue(IServiceProvider serviceProvider)
    {
        var target = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        if (target?.TargetObject is not DependencyObject element) return this;
        if (target.TargetObject.GetType().FullName == "System.Windows.SharedDp") return this;
                var applicator = ResolveApplicator(target.TargetProperty, element);
        if (applicator is null) return this;
        var key = Key;
                if (key is string name)
        {
                var binding = CreateLangBinding(name);
                var expression = applicator.Set(binding);
            return expression ?? binding.ProvideValue(serviceProvider!);
        }
        if (key is Binding keyBinding)
        {
            if (element is not FrameworkElement host) return string.Empty;
            if (host.DataContext is not null)
            {
                var live = SetLangBinding(applicator, keyBinding.Path, host.DataContext);
                return live is null ? string.Empty : live.ProvideValue(serviceProvider!);
            }
            SetTargetProperty(host, applicator);
            host.DataContextChanged += OnDataContextChanged;
            return string.Empty;
        }
        return string.Empty;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement host) return;
        host.DataContextChanged -= OnDataContextChanged;
        if (Key is not Binding keyBinding) return;
        var applicator = GetTargetProperty(host);
        SetTargetProperty(host, null);
        if (applicator is not null)
            SetLangBinding(applicator, keyBinding.Path, host.DataContext!);
    }

    private void SetTargetProperty(DependencyObject target, LangApplicator? applicator) =>
        target.SetValue(TargetPropertyProperty, applicator);

    private static LangApplicator? GetTargetProperty(DependencyObject target) =>
        (LangApplicator?)target.GetValue(TargetPropertyProperty);

    private static LangApplicator? ResolveApplicator(object? targetProperty, DependencyObject element)
    {
        if (targetProperty is DependencyProperty dp)
            return new LangApplicator(element, dp, null);
        if (targetProperty is System.Reflection.PropertyInfo clr &&
            element is FrameworkElement &&
            clr.CanRead && clr.GetIndexParameters().Length == 0)
            return new LangApplicator(element, null, clr);
        return null;
    }

    private sealed class LangApplicator(DependencyObject Element, DependencyProperty? Dp, System.Reflection.PropertyInfo? Clr)
    {
                public object? Set(BindingBase binding)
        {
            if (Dp is not null)
                return BindingOperations.SetBinding(Element, Dp, (Binding)binding);
            if (Clr is not null && Element is FrameworkElement host)
                return host.SetBinding(Clr.Name, (Binding)binding);
            return null;
        }

        public object? Read()
        {
            if (Dp is not null) return Element.GetValue(Dp);
            return Clr?.GetValue(Element);
        }
    }

    private BindingBase? SetLangBinding(LangApplicator applicator, PropertyPath path, object dataContext)
    {
        var interim = new Binding { Path = path, Source = dataContext, Mode = BindingMode.OneWay };
        applicator.Set(interim);
        if (applicator.Read() is not string name || string.IsNullOrEmpty(name)) return null;
        var live = CreateLangBinding(name);
        applicator.Set(live);
        return live;
    }

    private Binding CreateLangBinding(string key)
    {
        // Default (loader-assigned table) binds Value off a per-key live
        // holder: same one-way live contract as the upstream provider-path
        // binding, without the unreachable provider getters. An explicit
        // user Source keeps the upstream shape ( Path off that object).
        if (Source is LangTable table)
        {
            return new Binding("Value")
            {
                Source = table.GetOrAddValue(key),
                Converter = Converter,
                ConverterParameter = ConverterParameter,
                UpdateSourceTrigger = UpdateSourceTrigger.Explicit,
                Mode = BindingMode.OneWay,
            };
        }
        return new Binding(key)
        {
            Source = Source,
            Converter = Converter,
            ConverterParameter = ConverterParameter,
            UpdateSourceTrigger = UpdateSourceTrigger.Explicit,
            Mode = BindingMode.OneWay,
        };
    }
}

/// <summary>Slice-97 converted-markup loader glue (partial XamlLoader).</summary>
public static partial class XamlLoader
{
    // HandyControl xmlns table (Cecil-verified against the staged
    // HandyControl image's assembly-level XmlnsDefinition rows: the
    // https://handyorg.github.io/handycontrol URI maps these lib
    // namespaces with the hc prefix hint). The runtime cannot read those
    // rows (dangling TypeRefs throw on any assembly-attribute
    // enumeration), so the loader carries the verified copy; the
    // staged-oracle test re-pins the resolution targets, so a vendor
    // change that breaks the exactly-one rule fails loudly instead of
    // binding somewhere unexpected.
    private static readonly Dictionary<string, string[]> HandyControlXmlNamespaces = new(StringComparer.Ordinal)
    {
        ["https://handyorg.github.io/handycontrol"] =
        [
            "HandyControl.Controls",
            "HandyControl.Tools",
            "HandyControl.Tools.Converter",
            "HandyControl.Tools.Extension",
            "HandyControl.Interactivity",
            "HandyControl.Expression.Shapes",
            "HandyControl.Expression.Media",
            "HandyControl.Media.Animation",
            "HandyControl.Media.Effects",
            "HandyControl.Data",
            "HandyControl.Properties.Langs",
            "HandyControl.Themes",
        ],
    };

    // MaterialDesignInXAML xmlns table (mirrors the upstream
    // MaterialDesignThemes.Wpf AssemblyInfo XmlnsDefinition rows for the
    // themes and themes-internal URIs; the converted images drop those
    // assembly rows, so the loader carries the verified copy like the
    // HandyControl table above).
    private static readonly Dictionary<string, string[]> MaterialDesignXmlNamespaces = new(StringComparer.Ordinal)
    {
        ["http://materialdesigninxaml.net/winfx/xaml/themes"] =
        [
            "MaterialDesignThemes.Wpf",
            "MaterialDesignThemes.Wpf.Transitions",
            "MaterialDesignThemes.Wpf.Converters",
        ],
        ["http://materialdesigninxaml.net/winfx/xaml/themes-internal"] =
        [
            "MaterialDesignThemes.Wpf.Internal",
            "MaterialDesignThemes.Wpf.Converters.Internal",
            "MaterialDesignThemes.Wpf.Behaviors.Internal",
        ],
    };

    // Blend SDK behaviors xmlns table (mirrors the upstream
    // Microsoft.Xaml.Behaviors AssemblyInfo XmlnsDefinition row for
    // http://schemas.microsoft.com/xaml/behaviors; the converted image
    // drops those assembly rows, so the loader carries the verified copy
    // like the HandyControl and MaterialDesign tables above).
    private static readonly Dictionary<string, string[]> BlendXmlNamespaces = new(StringComparer.Ordinal)
    {
        ["http://schemas.microsoft.com/xaml/behaviors"] =
        [
            "Microsoft.Xaml.Behaviors",
        ],
    };

    // AvalonEdit xmlns table (mirrors the upstream ICSharpCode.AvalonEdit
    // AssemblyInfo XmlnsDefinition row for
    // http://icsharpcode.net/sharpdevelop/avalonedit; the converted image
    // drops those assembly rows, so the loader carries the verified copy
    // like the tables above. MaterialDesign demo themes address TextEditor
    // through it).
    private static readonly Dictionary<string, string[]> AvalonEditXmlNamespaces = new(StringComparer.Ordinal)
    {
        ["http://icsharpcode.net/sharpdevelop/avalonedit"] =
        [
            "ICSharpCode.AvalonEdit",
        ],
    };

    /// <summary>
    /// Looks up a vendor xmlns URI in the carried tables (HandyControl,
    /// then MaterialDesignInXAML). Null when no table owns the URI and
    /// the caller falls back to the short-name scan.
    /// </summary>
    private static string[]? XmlNamespacesFor(string uri)
    {
        if (HandyControlXmlNamespaces.TryGetValue(uri, out var namespaces)) return namespaces;
        if (MaterialDesignXmlNamespaces.TryGetValue(uri, out namespaces)) return namespaces;
        if (BlendXmlNamespaces.TryGetValue(uri, out namespaces)) return namespaces;
        if (AvalonEditXmlNamespaces.TryGetValue(uri, out namespaces)) return namespaces;
        return null;
    }

    /// <summary>
    /// Applies a converted markup extension ({ex:Lang ...}, {hc:Lang ...}).
    /// True when handled; false when the head is not a converted Lang
    /// (the caller keeps its legacy error). Real gaps inside a converted
    /// head throw precisely.
    /// </summary>
    internal static bool TryApplyConvertedMarkupExtension(
        UIElement element, string local, string head, string value,
        Func<string, string?> xmlns, IXmlLineInfo lineInfo)
    {
        var colon = head.IndexOf(':');
        if (colon <= 0 || colon == head.Length - 1) return false;
        if (!string.Equals(head[(colon + 1)..], "Lang", StringComparison.Ordinal)) return false;
        var resolved = ResolveConvertedLangType(head[..colon], xmlns(head[..colon]), lineInfo);
        if (resolved is null) return false;
        var table = TableForProvenance(resolved.Value.Provenance, lineInfo);
                var extension = ReadLangExtension(value, head, lineInfo, xmlns, table, out var sourceSpecified);
                if (!sourceSpecified) extension.Source = table;
        if (local.Contains('.'))
        {
            // Attached target (hc:InfoElement.Placeholder on the converted
            // SearchBar): the converted attached DP lands through the DP
            // store, and the converted extension binds against it like a
            // plain target. Engine-owned attached names keep the legacy
            // error below.
            var dot = local.IndexOf('.');
            var attachedDp = ConvertedAttachedDp(local[..dot], local[(dot + 1)..]);
            if (attachedDp is null) return false;
            var attachedService = new XamlExtensionService(element, attachedDp);
            var attachedResult = extension.ProvideValue(attachedService);
            if (attachedResult is BindingExpressionBase) return true;
            if (attachedResult is string attachedText)
            {
                if (attachedDp.PropertyType == typeof(string) ||
                    attachedDp.PropertyType.IsInstanceOfType(attachedText))
                {
                    element.SetValue(attachedDp, attachedText);
                    return true;
                }
                throw Fail(lineInfo, $"'{value}' produced text that does not fit '{local}' on <{element.GetType().Name}>.");
            }
            if (attachedResult is MarkupExtension)
                throw Fail(lineInfo, $"'{value}' defers its value (shared template context); only live targets resolve it.");
            throw Fail(lineInfo, $"'{value}' needs a settable property '{local}' on <{element.GetType().Name}>.");
        }
        var targetProperty = TargetPropertyFor(element, local);
        var service = new XamlExtensionService(element, targetProperty);
                var result = extension.ProvideValue(service);
                if (result is BindingExpressionBase) return true;
        if (result is string text)
        {
            if (TrySetPropertyElementValue(element, local, text, lineInfo)) return true;
            throw Fail(lineInfo, $"'{value}' produced text that does not fit '{local}' on <{element.GetType().Name}>.");
        }
        if (result is MarkupExtension)
            throw Fail(lineInfo, $"'{value}' defers its value (shared template context); only live targets resolve it.");
        throw Fail(lineInfo, $"'{value}' needs a settable property '{local}' on <{element.GetType().Name}>.");
    }

    /// <summary>
    /// Instantiates a converted vendor markup extension
    /// ({materialDesign:PackIcon Kind=Menu}, abbreviated heads included).
    /// True when handled; false when the head names no converted
    /// MarkupExtension (the caller keeps its legacy error). The head short
    /// name resolves first, then the name plus "Extension"; named arguments
    /// convert onto public settable CLR properties through
    /// StyleValueConverter (enums/strings/brushes ride the literal path,
    /// {x:Static ...} through the shared member path); the provided value
    /// lands through the property setter. Real gaps inside a converted head
    /// throw precisely.
    /// </summary>
    internal static bool TryApplyConvertedExtensionObject(
        UIElement element, string local, string head, string value, IXmlLineInfo lineInfo)
    {
        var colon = head.IndexOf(':');
        if (colon <= 0 || colon == head.Length - 1) return false;
        var shortName = head[(colon + 1)..];
        if (shortName.Length == 0 || shortName.Contains('{') || shortName.Contains('=')) return false;
        Type? extensionType;
        try
        {
            extensionType = ScanUniqueConvertedType(shortName, lineInfo, value);
            if (extensionType is not null && !typeof(MarkupExtension).IsAssignableFrom(extensionType))
                extensionType = null;
            extensionType ??= ScanUniqueConvertedType(shortName + "Extension", lineInfo, value);
        }
        catch (XamlLoadException)
        {
            return false;
        }
        if (extensionType is null || !typeof(MarkupExtension).IsAssignableFrom(extensionType)) return false;
        var extension = Activator.CreateInstance(extensionType, nonPublic: false)
            ?? throw Fail(lineInfo, $"'{value}' needs a parameterless constructor on <{extensionType.FullName}>.");
        var inner = value[1..^1].Trim();
        var argsText = inner.Length > head.Length ? inner[head.Length..].Trim() : string.Empty;
        if (argsText.Length > 0)
        {
            foreach (var token in BindingOptions(argsText))
            {
                var option = token.Trim();
                if (option.Length == 0) continue;
                var eq = option.IndexOf('=');
                if (eq <= 0)
                    throw Fail(lineInfo, $"'{value}' takes named arguments only (Name=Value).");
                var name = option[..eq].Trim();
                var argRaw = option[(eq + 1)..].Trim();
                var prop = extensionType.GetProperty(name,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop is null || !prop.CanWrite || prop.SetMethod?.IsPublic != true)
                    throw Fail(lineInfo, $"'{value}' names no settable property '{name}' on <{extensionType.FullName}>.");
                object? argValue;
                if (argRaw.StartsWith("{x:Static ", StringComparison.Ordinal) && argRaw.EndsWith('}'))
                    argValue = ResolveStaticMember(argRaw["{x:Static ".Length..^1].Trim(), lineInfo);
                else
                    argValue = StyleValueConverter.Convert(argRaw, prop.PropertyType);
                if (argValue is not null && !prop.PropertyType.IsInstanceOfType(argValue))
                    throw Fail(lineInfo, $"'{value}' argument '{name}' produced <{argValue.GetType().Name}>, not {prop.PropertyType.Name}.");
                prop.SetValue(extension, argValue);
            }
        }
        var service = new XamlExtensionService(element, TargetPropertyFor(element, local));
        var result = ((MarkupExtension)extension).ProvideValue(service);
        if (result is BindingExpressionBase) return true;
        if (TrySetPropertyElementValue(element, local, result, lineInfo)) return true;
        throw Fail(lineInfo, $"'{value}' needs a settable property '{local}' on <{element.GetType().Name}>.");
    }

    private static object? TargetPropertyFor(UIElement element, string local)
    {
        if (local.Contains('.')) return null;
        return DependencyProperty.FromName(element.GetType(), local)
            ?? (object?)element.GetType().GetProperty(
                local, BindingFlags.Public | BindingFlags.Instance);
    }

    /// <summary>
    /// Resolves a converted attached DP by owner short name (InfoElement,
    /// IconElement, ...). Null when the owner is engine-owned, unknown or
    /// ambiguous, or names no DP; the caller keeps its legacy error then.
    /// </summary>
    private static DependencyProperty? ConvertedAttachedDp(string owner, string property)
    {
        if (property.Contains('.') || FindEngineType(owner) is not null) return null;
        var closure = ConvertedClosureAssemblies();
        Type? found = null;
        if (closure.Count > 0)
        {
            foreach (var assembly in closure)
            {
                List<Type> candidates;
                try { candidates = WpfAppBoot.SafeGetTypes(assembly).Where(t => t.Name == owner).ToList(); }
                catch { continue; }
                foreach (var candidate in candidates)
                {
                    if (found is not null && !ReferenceEquals(found, candidate) &&
                        !string.Equals(found.FullName, candidate.FullName, StringComparison.Ordinal)) return null;
                    found = candidate;
                }
            }
        }
        if (found is null)
        {
            var seenInClosure = new HashSet<Assembly>(closure);
            foreach (var assembly in ConvertedSearchAssemblies())
            {
                if (seenInClosure.Contains(assembly)) continue;
                List<Type> candidates;
                try { candidates = WpfAppBoot.SafeGetTypes(assembly).Where(t => t.Name == owner).ToList(); }
                catch { continue; }
                foreach (var candidate in candidates)
                {
                    if (found is not null && !ReferenceEquals(found, candidate) &&
                        !string.Equals(found.FullName, candidate.FullName, StringComparison.Ordinal)) return null;
                    found = candidate;
                }
            }
        }
        var field = found?.GetField(property + "Property",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        return field?.GetValue(null) as DependencyProperty;
    }

    /// <summary>
    /// Resolves a converted attached DP by owner short name without throwing
    /// (binding runtime reads carry no line info): engine-owned, unknown or
    /// ambiguous owners, missing DP fields, and unloadable images all yield
    /// null, and the caller keeps its legacy behavior then. Mirrors
    /// ConvertedAttachedDp, which keeps its precise load-time errors.
    /// </summary>
    internal static DependencyProperty? TryResolveConvertedAttachedDp(string owner, string property)
    {
        if (property.Contains('.')) return null;
        var shortOwner = owner.Contains(':') ? owner[(owner.IndexOf(':') + 1)..] : owner;
        if (string.IsNullOrEmpty(shortOwner) || FindEngineType(shortOwner) is not null) return null;
        var closure = ConvertedClosureAssemblies();
        Type? found = null;
        if (closure.Count > 0)
        {
            foreach (var assembly in closure)
            {
                List<Type> candidates;
                try { candidates = WpfAppBoot.SafeGetTypes(assembly).Where(t => t.Name == shortOwner).ToList(); }
                catch { continue; }
                foreach (var candidate in candidates)
                {
                    if (found is not null && !ReferenceEquals(found, candidate) &&
                        !string.Equals(found.FullName, candidate.FullName, StringComparison.Ordinal)) return null;
                    found = candidate;
                }
            }
        }
        if (found is null)
        {
            var seenInClosure = new HashSet<Assembly>(closure);
            foreach (var assembly in ConvertedSearchAssemblies())
            {
                if (seenInClosure.Contains(assembly)) continue;
                List<Type> candidates;
                try { candidates = WpfAppBoot.SafeGetTypes(assembly).Where(t => t.Name == shortOwner).ToList(); }
                catch { continue; }
                foreach (var candidate in candidates)
                {
                    if (found is not null && !ReferenceEquals(found, candidate) &&
                        !string.Equals(found.FullName, candidate.FullName, StringComparison.Ordinal)) return null;
                    found = candidate;
                }
            }
        }
        var field = found?.GetField(property + "Property",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        try { return field?.GetValue(null) as DependencyProperty; }
        catch { return null; }
    }

    private readonly record struct ConvertedLangTarget(Type ExtensionType, Assembly Provenance);

    private static ConvertedLangTarget? ResolveConvertedLangType(string prefix, string? uri, IXmlLineInfo lineInfo)
    {
        const string shortName = "LangExtension";
        if (string.IsNullOrEmpty(uri)) return null;
        if (uri.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            var rest = uri["clr-namespace:".Length..];
            string? assemblyName = null;
            var cut = rest.IndexOf(';');
            if (cut >= 0)
            {
                foreach (var part in rest[(cut + 1)..].Split(';'))
                {
                    var trimmed = part.Trim();
                    if (trimmed.StartsWith("assembly=", StringComparison.Ordinal))
                        assemblyName = trimmed["assembly=".Length..].Trim();
                }
                rest = rest[..cut];
            }
            var fullName = rest.Trim() + "." + shortName;
            Type? found = null;
            Assembly? owner = null;
            foreach (var assembly in ConvertedSearchAssemblies())
            {
                if (assemblyName is not null &&
                    !string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var candidate = SafeGetConvertedType(assembly, fullName);
                if (candidate is null || !typeof(MarkupExtension).IsAssignableFrom(candidate)) continue;
                if (found is not null && !ReferenceEquals(found, candidate) &&
                    (!string.Equals(found.FullName, candidate.FullName, StringComparison.Ordinal) ||
                     !string.Equals(owner?.GetName().Name, assembly.GetName().Name, StringComparison.OrdinalIgnoreCase)))
                    throw Fail(lineInfo, $"'{prefix}:Lang' matches converted types in '{owner!.GetName().Name}' and '{assembly.GetName().Name}' (qualify the xmlns assembly).");
                found = candidate;
                owner = assembly;
            }
            if (found is null)
                throw Fail(lineInfo, $"'{prefix}:Lang' names no converted LangExtension '{fullName}'.");
            return new ConvertedLangTarget(found, owner!);
        }
        if (HandyControlXmlNamespaces.TryGetValue(uri, out var namespaces))
        {
            Type? found = null;
            Assembly? owner = null;
            foreach (var ns in namespaces)
            {
                var fullName = ns + "." + shortName;
                foreach (var assembly in ConvertedSearchAssemblies())
                {
                    var candidate = SafeGetConvertedType(assembly, fullName);
                    if (candidate is null || !typeof(MarkupExtension).IsAssignableFrom(candidate)) continue;
                    if (found is not null && !ReferenceEquals(found, candidate) &&
                        !string.Equals(found.FullName, candidate.FullName, StringComparison.Ordinal))
                        throw Fail(lineInfo, $"'{prefix}:Lang' matches '{found.FullName}' and '{candidate.FullName}' (update the xmlns table).");
                    found = candidate;
                    owner = assembly;
                }
            }
            if (found is null)
                throw Fail(lineInfo, $"'{prefix}:Lang' names no converted LangExtension under '{uri}'.");
            return new ConvertedLangTarget(found, owner!);
        }
        return null;
    }

    private static LangTable TableForProvenance(Assembly provenance, IXmlLineInfo lineInfo)
    {
        try
        {
            return LangTable.ForAssembly(provenance);
        }
        catch (InvalidOperationException ex)
        {
            throw Fail(lineInfo, ex.Message);
        }
    }

    private static LangExtension ReadLangExtension(
        string value, string head, IXmlLineInfo lineInfo,
        Func<string, string?> xmlns, LangTable table, out bool sourceSpecified)
    {
        sourceSpecified = false;
        var inner = value[1..^1].Trim();
        var args = inner[head.Length..].Trim();
        var extension = new LangExtension();
        if (args.Length == 0)
            throw Fail(lineInfo, $"'{value}' needs a key ({{ex:Lang Key=...}}).");
        foreach (var token in BindingOptions(args))
        {
            var part = token.Trim();
            if (part.Length == 0) continue;
            var equals = IndexOfTopLevel(part, '=');
            if (equals <= 0)
                throw Fail(lineInfo, $"'{part}' is not a Name=Value pair ('{value}' takes Key/Mode/Converter/ConverterParameter/Source).");
            var name = part[..equals].Trim();
            var raw = part[(equals + 1)..].Trim();
            var property = typeof(LangExtension).GetProperty(
                name, BindingFlags.Public | BindingFlags.Instance);
            if (property is null || property.SetMethod?.IsPublic != true)
                throw Fail(lineInfo, $"'{name}' is not a settable Lang property (Key/Mode/Converter/ConverterParameter/Source).");
            if (string.Equals(name, "Source", StringComparison.Ordinal)) sourceSpecified = true;
            property.SetValue(extension, EvaluateLangArgument(raw, property.PropertyType, value, lineInfo, xmlns));
        }
        return extension;
    }

    private static int IndexOfTopLevel(string text, char separator)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') depth--;
            else if (text[i] == separator && depth == 0) return i;
        }
        return -1;
    }

    private static object? EvaluateLangArgument(
        string raw, Type propertyType, string original, IXmlLineInfo lineInfo, Func<string, string?> xmlns)
    {
        if (string.Equals(raw, "{x:Null}", StringComparison.Ordinal)) return null;
        if (raw.StartsWith("{x:Static ", StringComparison.Ordinal) && raw.EndsWith('}'))
            return ResolveConvertedStatic(raw[1..^1].Trim()["x:Static".Length..].Trim(), original, lineInfo, xmlns);
        if ((raw.StartsWith("{Binding ", StringComparison.Ordinal) || string.Equals(raw, "{Binding}", StringComparison.Ordinal)) && raw.EndsWith('}'))
            return ParseBinding(raw[1..^1].Trim()["Binding".Length..], original, lineInfo);
        if (raw.StartsWith("{StaticResource ", StringComparison.Ordinal) && raw.EndsWith('}'))
        {
            // BorderDemo's ex:Lang Converter on the BorderTip rows: the
            // converter key resolves at application scope (the HandyTheme
            // converted-converter fallback), like a resource lookup at
            // attach. Misses fail like WPF instead of landing null.
            if (!typeof(IValueConverter).IsAssignableFrom(propertyType))
                throw Fail(lineInfo, $"'{raw}' is a resource reference ('{original}' takes a converter here).");
            var key = raw["{StaticResource ".Length..^1].Trim();
            var app = Application.Current;
            if (app is not null && app.TryFindResource(key, out var res) && res is IValueConverter converter)
                return converter;
            throw Fail(lineInfo, $"'{raw}' names no application converter for '{original}'.");
        }
        if (raw.StartsWith('{'))
            throw Fail(lineInfo, $"Nested '{raw}' is not supported in converted-extension args ('{original}' takes {{x:Static ...}}, {{Binding ...}} or {{x:Null}}).");
        if (propertyType == typeof(object) || propertyType == typeof(string)) return raw;
        if (propertyType.IsEnum)
        {
            try
            {
                return Enum.Parse(propertyType, raw, ignoreCase: true);
            }
            catch (ArgumentException)
            {
                throw Fail(lineInfo, $"'{raw}' is not a {propertyType.Name} ({string.Join('/', Enum.GetNames(propertyType))}).");
            }
        }
        try
        {
            return StyleValueConverter.Convert(raw, propertyType)
                ?? throw Fail(lineInfo, $"'{raw}' does not convert to {propertyType.Name}.");
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
        {
            throw Fail(lineInfo, ex.Message);
        }
    }

    /// <summary>
    /// Resolves {x:Static prefix:Type.Member} (or Type.Member) against
    /// engine types first, then converted images (clr-namespace exact or
    /// the pinned vendor table, else a unique short-name scan).
    /// </summary>
    internal static object? ResolveConvertedStatic(
        string member, string original, IXmlLineInfo lineInfo, Func<string, string?> xmlns)
    {
        var dot = member.LastIndexOf('.');
        if (dot <= 0 || dot == member.Length - 1)
            throw Fail(lineInfo, $"'{original}' needs a static member ({{x:Static Type.Member}}).");
        var typeRef = member[..dot].Trim();
        var memberName = member[(dot + 1)..].Trim();
        var type = ResolveConvertedTypeRef(typeRef, lineInfo, xmlns, original);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var property = type.GetProperty(memberName, flags);
        if (property is not null && property.CanRead && property.GetIndexParameters().Length == 0)
            return property.GetValue(null)
                ?? throw Fail(lineInfo, $"'{original}' resolved to null.");
        var field = type.GetField(memberName, flags);
        if (field is not null)
            return field.GetValue(null)
                ?? throw Fail(lineInfo, $"'{original}' resolved to null.");
        throw Fail(lineInfo, $"'{original}' is not a public static property or field of <{type.Name}>.");
    }

    private static Type ResolveConvertedTypeRef(
        string typeRef, IXmlLineInfo lineInfo, Func<string, string?> xmlns, string original)
    {
        var prefix = string.Empty;
        var shortName = typeRef;
        var colon = typeRef.IndexOf(':');
        if (colon >= 0)
        {
            prefix = typeRef[..colon];
            shortName = typeRef[(colon + 1)..];
        }
        if (shortName.Length == 0)
            throw Fail(lineInfo, $"'{original}' names no type.");
        var engine = FindEngineType(shortName);
        if (engine is not null && prefix.Length == 0) return engine;
        string? uri = prefix.Length == 0 ? null : xmlns(prefix);
        if (prefix.Length > 0 && string.IsNullOrEmpty(uri))
            throw Fail(lineInfo, $"'{original}' uses unknown prefix '{prefix}'.");
        if (uri is not null && uri.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            var rest = uri["clr-namespace:".Length..];
            string? assemblyName = null;
            var cut = rest.IndexOf(';');
            if (cut >= 0)
            {
                foreach (var part in rest[(cut + 1)..].Split(';'))
                {
                    var trimmed = part.Trim();
                    if (trimmed.StartsWith("assembly=", StringComparison.Ordinal))
                        assemblyName = trimmed["assembly=".Length..].Trim();
                }
                rest = rest[..cut];
            }
            var fullName = rest.Trim() + "." + shortName;
            if (rest.Trim() == "System" &&
                (assemblyName is null or "mscorlib" or "System.Runtime" or "System.Private.CoreLib" or "netstandard") &&
                typeof(object).Assembly.GetType(fullName, throwOnError: false) is { IsVisible: true } frameworkType)
                return frameworkType;
            foreach (var assembly in ConvertedSearchAssemblies())
            {
                if (assemblyName is not null &&
                    !string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var candidate = SafeGetConvertedType(assembly, fullName);
                if (candidate is not null) return candidate;
            }
            if (assemblyName is not null)
            {
                // Staged images load on first use: pull the named image
                // through the package resolver (same lazy gap as pack
                // Sources; the hook is null outside a package boot).
                Assembly? staged = null;
                try { staged = WpfAppBoot.PackAssemblyResolver?.Invoke(assemblyName); }
                catch { staged = null; }
                if (staged is not null)
                {
                    var late = SafeGetConvertedType(staged, fullName);
                    if (late is not null) return late;
                }
            }
            throw Fail(lineInfo, $"'{original}' names no converted type '{fullName}'.");
        }
        if (uri is not null && XmlNamespacesFor(uri) is { } namespaces)
        {
            var closure = ConvertedClosureAssemblies();
            Type? found = null;
            if (closure.Count > 0)
            {
                foreach (var ns in namespaces)
                {
                    foreach (var assembly in closure)
                    {
                        var candidate = SafeGetConvertedType(assembly, ns + "." + shortName);
                        if (candidate is null) continue;
                        if (found is not null && !ReferenceEquals(found, candidate) &&
                            !string.Equals(found.FullName, candidate.FullName, StringComparison.Ordinal))
                            throw Fail(lineInfo, $"'{original}' matches '{found.FullName}' and '{candidate.FullName}' (update the xmlns table).");
                        found = candidate;
                    }
                }
                if (found is not null) return found;
            }

            var seenInClosure = new HashSet<Assembly>(closure);
            foreach (var ns in namespaces)
            {
                foreach (var assembly in ConvertedSearchAssemblies())
                {
                    if (seenInClosure.Contains(assembly)) continue;
                    var candidate = SafeGetConvertedType(assembly, ns + "." + shortName);
                    if (candidate is null) continue;
                    if (found is not null && !ReferenceEquals(found, candidate) &&
                        !string.Equals(found.FullName, candidate.FullName, StringComparison.Ordinal))
                        throw Fail(lineInfo, $"'{original}' matches '{found.FullName}' and '{candidate.FullName}' (update the xmlns table).");
                    found = candidate;
                }
            }
            // Staged images load on first use: pull the mapped assemblies
            // through the package resolver (same lazy gap as pack Sources).
            if (found is null)
                found = ResolveUnloadedConvertedType(namespaces, shortName, lineInfo, original);
            if (found is not null) return found;
            throw Fail(lineInfo, $"'{original}' names no converted type '{shortName}' under '{uri}'.");
        }
        var scanned = ScanUniqueConvertedType(shortName, lineInfo, original);
        if (scanned is not null) return scanned;
        throw Fail(lineInfo, $"'{original}' names no engine or converted type '{shortName}'.");
    }

    private static List<Assembly> ConvertedClosureAssemblies()
    {
        var engine = typeof(UIElement).Assembly;
        var assemblies = new List<Assembly>();
        var seen = new HashSet<Assembly>();
        var source = CustomElementSource;
        if (source is null) return assemblies;

        void Add(Assembly? asm)
        {
            if (asm is not null && !ReferenceEquals(asm, engine) && seen.Add(asm))
                assemblies.Add(asm);
        }

        Add(source);

        var loaded = AppDomain.CurrentDomain.GetAssemblies();
        var queue = new Queue<Assembly>();
        queue.Enqueue(source);
        var walked = new HashSet<Assembly> { source };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            AssemblyName[] refs;
            try
            {
                refs = current.GetReferencedAssemblies();
            }
            catch
            {
                continue;
            }

            foreach (var r in refs)
            {
                if (string.IsNullOrEmpty(r.Name)) continue;
                if (r.Name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                    r.Name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
                    r.Name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                    r.Name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
                    r.Name.Equals("WindowsBase", StringComparison.OrdinalIgnoreCase) ||
                    r.Name.Equals("PresentationCore", StringComparison.OrdinalIgnoreCase) ||
                    r.Name.Equals("PresentationFramework", StringComparison.OrdinalIgnoreCase))
                    continue;

                Assembly? resolved = null;
                if (WpfAppBoot.PackAssemblyResolver is { } resolver)
                {
                    try { resolved = resolver(r.Name); }
                    catch { }
                }
                resolved ??= loaded.FirstOrDefault(candidate =>
                    string.Equals(candidate.GetName().FullName, r.FullName, StringComparison.OrdinalIgnoreCase))
                    ?? loaded.FirstOrDefault(candidate =>
                        string.Equals(candidate.GetName().Name, r.Name, StringComparison.OrdinalIgnoreCase));
                if (resolved is not null)
                {
                    Add(resolved);
                    if (walked.Add(resolved)) queue.Enqueue(resolved);
                }
            }
        }

        return assemblies;
    }

    internal static List<Assembly> ConvertedSearchAssemblies()
    {
        var engine = typeof(UIElement).Assembly;
        var assemblies = new List<Assembly>();
        var seen = new HashSet<Assembly>();
        void Add(Assembly? assembly)
        {
            if (assembly is not null && !ReferenceEquals(assembly, engine) && seen.Add(assembly))
                assemblies.Add(assembly);
        }
        var closureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (CustomElementSource is not null)
        {
            foreach (var asm in ConvertedClosureAssemblies())
            {
                Add(asm);
                closureNames.Add(asm.GetName().Name!);
            }
        }
        else
        {
            Add(CustomElementSource);
        }
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name!;
            if (closureNames.Contains(name)) continue;
            // XAML-only dependencies need not appear in the entry's CLR
            // reference graph. Resolve their loaded simple name through the
            // active package too, before considering an older session image.
            var current = WpfAppBoot.PackAssemblyResolver?.Invoke(name);
            if (current is not null)
            {
                Add(current);
                closureNames.Add(name);
            }
            // With an active package, its declared closure is the boundary:
            // scanning every other application's/framework's types both
            // contaminates lookup and makes theme inflation quadratic in
            // the number of applications previously launched.
            else if (WpfAppBoot.PackAssemblyResolver is null) Add(assembly);
        }
        return assemblies;
    }

    private static Type? SafeGetConvertedType(Assembly assembly, string fullName)
    {
        try
        {
            return assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
        }
        catch
        {
            return null;
        }
    }

    private static Type? ScanUniqueConvertedType(string shortName, IXmlLineInfo lineInfo, string original)
    {
        var closure = ConvertedClosureAssemblies();
        if (closure.Count > 0)
        {
            Type? foundInClosure = null;
            foreach (var assembly in closure)
            {
                List<Type> candidates;
                try
                {
                    candidates = WpfAppBoot.SafeGetTypes(assembly).Where(t => t.Name == shortName).ToList();
                }
                catch
                {
                    continue;
                }
                foreach (var candidate in candidates)
                {
                    if (foundInClosure is not null && !ReferenceEquals(foundInClosure, candidate) &&
                        !string.Equals(foundInClosure.FullName, candidate.FullName, StringComparison.Ordinal))
                        throw Fail(lineInfo, $"'{original}' matches '{foundInClosure.FullName}' and '{candidate.FullName}' (qualify the xmlns).");
                    foundInClosure = candidate;
                }
            }
            if (foundInClosure is not null)
                return foundInClosure;
        }

        Type? found = null;
        var seenInClosure = new HashSet<Assembly>(closure);
        foreach (var assembly in ConvertedSearchAssemblies())
        {
            if (seenInClosure.Contains(assembly)) continue;
            List<Type> candidates;
            try
            {
                candidates = WpfAppBoot.SafeGetTypes(assembly).Where(t => t.Name == shortName).ToList();
            }
            catch
            {
                continue;
            }
            foreach (var candidate in candidates)
            {
                if (found is not null && !ReferenceEquals(found, candidate) &&
                    !string.Equals(found.FullName, candidate.FullName, StringComparison.Ordinal))
                    throw Fail(lineInfo, $"'{original}' matches '{found.FullName}' and '{candidate.FullName}' (qualify the xmlns).");
                found = candidate;
            }
        }
        return found;
    }

    /// <summary>
    /// Resolves a vendor attached-property owner (CLR short type name)
    /// through the carried xmlns tables, pulling staged images like element
    /// type refs (theme triggers name converted assist classes). A
    /// clr-namespace URI resolves its namespace directly (plus an explicit
    /// assembly suffix when present).
    /// </summary>
    internal static Type? ResolveVendorOwnerType(string uri, string ownerShort, IXmlLineInfo lineInfo, string display)
    {
        if (uri.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            var ns = uri["clr-namespace:".Length..];
            string? assemblyName = null;
            var semi = ns.IndexOf(';');
            if (semi >= 0)
            {
                foreach (var part in ns[(semi + 1)..].Split(';'))
                {
                    var eq = part.IndexOf('=');
                    if (eq > 0 && part[..eq].Trim() == "assembly")
                        assemblyName = part[(eq + 1)..].Trim();
                }
                ns = ns[..semi];
            }
            var full = ns.Trim() + "." + ownerShort;
            foreach (var assembly in ConvertedSearchAssemblies())
            {
                var direct = SafeGetConvertedType(assembly, full);
                if (direct is not null) return direct;
            }
            if (assemblyName is not null)
            {
                try
                {
                    var staged = WpfAppBoot.PackAssemblyResolver?.Invoke(assemblyName);
                    if (staged is not null)
                    {
                        var late = SafeGetConvertedType(staged, full);
                        if (late is not null) return late;
                    }
                }
                catch { }
            }
            return ResolveUnloadedConvertedType(new[] { ns.Trim() }, ownerShort, lineInfo, display);
        }
        var namespaces = XmlNamespacesFor(uri);
        if (namespaces is null) return null;
        var closure = ConvertedClosureAssemblies();
        Type? found = null;
        if (closure.Count > 0)
        {
            foreach (var ns in namespaces)
            {
                foreach (var assembly in closure)
                {
                    var candidate = SafeGetConvertedType(assembly, ns + "." + ownerShort);
                    if (candidate is null) continue;
                    if (found is not null && !ReferenceEquals(found, candidate) &&
                        !string.Equals(found.FullName, candidate.FullName, StringComparison.Ordinal))
                        throw Fail(lineInfo, $"'{display}' matches '{found.FullName}' and '{candidate.FullName}' (update the xmlns table).");
                    found = candidate;
                }
            }
            if (found is not null) return found;
        }

        var seenInClosure = new HashSet<Assembly>(closure);
        foreach (var ns in namespaces)
        {
            foreach (var assembly in ConvertedSearchAssemblies())
            {
                if (seenInClosure.Contains(assembly)) continue;
                var candidate = SafeGetConvertedType(assembly, ns + "." + ownerShort);
                if (candidate is null) continue;
                if (found is not null && !ReferenceEquals(found, candidate) &&
                    !string.Equals(found.FullName, candidate.FullName, StringComparison.Ordinal))
                    throw Fail(lineInfo, $"'{display}' matches '{found.FullName}' and '{candidate.FullName}' (update the xmlns table).");
                found = candidate;
            }
        }
        if (found is null)
            found = ResolveUnloadedConvertedType(namespaces, ownerShort, lineInfo, display);
        return found;
    }

    /// <summary>
    /// Pulls xmlns-mapped assemblies that nothing has loaded yet through the
    /// package resolver (staged images load on first use, like pack Sources)
    /// and re-checks the mapped full names. Probes try the longest namespace
    /// prefix first (MaterialDesignThemes.Wpf before MaterialDesignThemes);
    /// misses never load, and the full-name check keeps cross-assembly hits
    /// honest under the same exactly-one rule as the loaded scan above.
    /// </summary>
    private static Type? ResolveUnloadedConvertedType(string[] namespaces, string shortName, IXmlLineInfo lineInfo, string original)
    {
        var resolver = WpfAppBoot.PackAssemblyResolver;
        if (resolver is null) return null;
        Type? found = null;
        foreach (var ns in namespaces)
        {
            var rest = ns;
            while (rest.Length > 0)
            {
                Assembly? assembly;
                try { assembly = resolver(rest); }
                catch { assembly = null; }
                if (assembly is not null)
                {
                    var candidate = SafeGetConvertedType(assembly, ns + "." + shortName);
                    if (candidate is not null)
                    {
                        if (found is not null && !ReferenceEquals(found, candidate) &&
                            !string.Equals(found.FullName, candidate.FullName, StringComparison.Ordinal))
                            throw Fail(lineInfo, $"'{original}' matches '{found.FullName}' and '{candidate.FullName}' (update the xmlns table).");
                        found = candidate;
                    }
                }
                var cut = rest.LastIndexOf('.');
                if (cut <= 0) break;
                rest = rest[..cut];
            }
        }
        return found;
    }

    /// <summary>
    /// Lists every converted type with a short name, without failing on
    /// duplicates: x:Static resolution disambiguates same-named types by the
    /// requested member (an AvalonEdit Utils.Constants never defines theme
    /// members), like the xmlns prefix would. Callers keep the loud
    /// ambiguity error when several owners define the member.
    /// </summary>
    internal static List<Type> FindConvertedShortNameMatches(string shortName)
    {
        var matches = new List<Type>();
        foreach (var assembly in ConvertedSearchAssemblies())
        {
            List<Type> candidates;
            try
            {
                candidates = WpfAppBoot.SafeGetTypes(assembly).Where(t => t.Name == shortName).ToList();
            }
            catch
            {
                continue;
            }
            foreach (var candidate in candidates)
            {
                if (!matches.Any(m => ReferenceEquals(m, candidate) || string.Equals(m.FullName, candidate.FullName, StringComparison.Ordinal))) matches.Add(candidate);
            }
        }
        return matches;
    }

    /// <summary>
    /// Picks the single same-named converted type defining a static member
    /// (readable parameterless property or field, like the resolver reads):
    /// null when none defines it, loud when several do.
    /// </summary>
    internal static Type? ScanConvertedMemberOwner(List<Type> matches, string memberName, IXmlLineInfo lineInfo, string original)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        Type? owner = null;
        foreach (var match in matches)
        {
            var property = match.GetProperty(memberName, flags);
            var defines = (property is not null && property.CanRead && property.GetIndexParameters().Length == 0)
                || match.GetField(memberName, flags) is not null;
            if (!defines) continue;
            if (owner is not null && !ReferenceEquals(owner, match) &&
                !string.Equals(owner.FullName, match.FullName, StringComparison.Ordinal))
                throw Fail(lineInfo, $"'{original}' matches '{owner.FullName}' and '{match.FullName}' (qualify the xmlns).");
            owner = match;
        }
        return owner;
    }

    /// <summary>
    /// Resolves a literal Command="Type.Member" (engine first, then the
    /// converted images). True with the command when bound; false when the
    /// type is unknown (the caller keeps its legacy error). Real gaps
    /// inside a resolved type throw precisely.
    /// </summary>
    internal static bool TryResolveLiteralCommand(string value, IXmlLineInfo lineInfo, out ICommand? command)
    {
        command = null;
        var dot = value.LastIndexOf('.');
        if (dot <= 0 || dot == value.Length - 1 || value.Contains(' ') || value.Contains('{') || value.Contains('}'))
            return false;
        var shortName = value[..dot];
        var member = value[(dot + 1)..];
        if (shortName.Contains(':')) shortName = shortName[(shortName.IndexOf(':') + 1)..];
        if (shortName.Length == 0 || member.Length == 0) return false;
        var owner = FindEngineType(shortName) ?? FindConvertedType(shortName)
            ?? ScanUniqueConvertedType(shortName, lineInfo, value);
        if (owner is null) return false;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var property = owner.GetProperty(member, flags);
        if (property is not null && property.CanRead && property.GetIndexParameters().Length == 0)
        {
            if (property.GetValue(null) is ICommand propertyCommand)
            {
                command = propertyCommand;
                return true;
            }
            return false;
        }
        var field = owner.GetField(member, flags);
        if (field is not null)
        {
            if (field.GetValue(null) is ICommand fieldCommand)
            {
                command = fieldCommand;
                return true;
            }
            return false;
        }
        throw Fail(lineInfo, $"'{value}' is not a public static command of <{owner.Name}>.");
    }

    /// <summary>
    /// Applies a resolved resource to a converted attached property
    /// (hc:IconElement.Geometry and friends). True when applied; false
    /// when the owner is unknown (the caller keeps its legacy error).
    /// Real gaps inside a resolved owner throw precisely.
    /// </summary>
    internal static bool TryApplyConvertedAttachedObject(
        UIElement element, string name, object res, IXmlLineInfo lineInfo)
    {
        var dot = name.IndexOf('.');
        if (dot <= 0 || dot == name.Length - 1) return false;
        var ownerName = name[..dot];
        var propertyName = name[(dot + 1)..];
        if (propertyName.Contains('.')) return false;
        if (FindEngineType(ownerName) is not null) return false;
        var owner = ScanUniqueConvertedType(ownerName, lineInfo, name);
        if (owner is null) return false;
        var dp = ConvertedAttachedProperty(owner, propertyName, name, lineInfo);
        if (!dp.PropertyType.IsInstanceOfType(res))
            throw Fail(lineInfo, $"'{name}' takes {dp.PropertyType.Name}, not {res.GetType().Name}.");
        element.SetValue(dp, res);
        return true;
    }

    /// <summary>
    /// Applies a literal to a converted attached property. True when
    /// applied; false when the owner is unknown.
    /// </summary>
    internal static bool TryApplyConvertedAttachedLiteral(
        UIElement element, string name, string raw, IXmlLineInfo lineInfo)
    {
        var dot = name.IndexOf('.');
        if (dot <= 0 || dot == name.Length - 1) return false;
        var ownerName = name[..dot];
        var propertyName = name[(dot + 1)..];
        if (propertyName.Contains('.')) return false;
        if (FindEngineType(ownerName) is not null) return false;
        var owner = ScanUniqueConvertedType(ownerName, lineInfo, name);
        if (owner is null) return false;
        var dp = ConvertedAttachedProperty(owner, propertyName, name, lineInfo);
        object? converted;
        if (dp.PropertyType == typeof(string))
        {
            converted = raw;
        }
        else
        {
            try
            {
                converted = StyleValueConverter.Convert(raw, dp.PropertyType);
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
            {
                throw Fail(lineInfo, ex.Message);
            }
            if (converted is null || !dp.PropertyType.IsInstanceOfType(converted))
                throw Fail(lineInfo, $"'{raw}' does not convert to {dp.PropertyType.Name} for '{name}'.");
        }
        element.SetValue(dp, converted);
        return true;
    }

    private static DependencyProperty ConvertedAttachedProperty(
        Type owner, string propertyName, string original, IXmlLineInfo lineInfo)
    {
        var field = owner.GetField(propertyName + "Property",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (field is null)
            throw Fail(lineInfo, $"'{original}' names no attached property on <{owner.Name}>.");
        if (field.GetValue(null) is not DependencyProperty dp)
            throw Fail(lineInfo, $"'{original}' is not a dependency property on <{owner.Name}>.");
        return dp;
    }

    private sealed class XamlExtensionService(object element, object? targetProperty) : IServiceProvider
    {
        private readonly XamlProvideTarget _target = new(element, targetProperty);

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IProvideValueTarget) ? _target : null;
    }

    private sealed class XamlProvideTarget(object element, object? targetProperty) : IProvideValueTarget
    {
        public object TargetObject => element;

        public object TargetProperty => targetProperty
            ?? throw new InvalidOperationException("Converted extensions need a DependencyProperty target.");
    }
}
