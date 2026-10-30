using System.Globalization;
using System.Runtime.CompilerServices;

namespace DesktopWasm.Wpf;

/// <summary>
/// Registers the pack:// URI scheme the way WPF does at framework load
/// (opaque authority, so "application:,,," parses instead of failing port
/// validation). Converted original code constructs pack URIs with the plain
/// single-argument Uri constructor exactly like the Gallery does; without
/// this the BCL parser rejects them before the engine ever sees them.
/// </summary>
internal static class PackScheme
{
    [ModuleInitializer]
    internal static void Register()
    {
        if (UriParser.IsKnownScheme("pack")) return;
        try
        {
            UriParser.Register(new GenericUriParser(GenericUriParserOptions.GenericAuthority), "pack", -1);
        }
        catch (InvalidOperationException)
        {
            // Another loader already registered pack (or forbids it): the
            // TryParsePack null path keeps resource lookup fail-soft.
        }
    }
}

/// <summary>
/// Keyed resource store with merged dictionaries (WPF subset). Keys are
/// typically strings (x:Key); values are plain CLR values (string, double,
/// bool, uint color) produced by the XAML resource entries.
/// Lookup order: local entries, then merged dictionaries in order.
/// </summary>
// Unsealed like WPF: vendor ResourceDictionary subclasses (MaterialDesign
// BundledTheme/CustomColorTheme) merge theme-generated entries through it.
public class ResourceDictionary
{
    private readonly Dictionary<object, object?> _entries = new();

    /// <summary>
    /// Factories for x:Shared="False" entries (BAML sharedSet): every lookup
    /// materializes a fresh instance, matching WPF per-request semantics for
    /// non-shared resources. Populated only by the BAML loader; inline
    /// (loose-XAML) dictionaries never carry deferred entries.
    /// </summary>
    private readonly Dictionary<object, Func<object?>> _deferred = new();

    /// <summary>Dictionaries consulted after the local entries, in order
    /// (Collection like WPF: converted code calls get_MergedDictionaries
    /// compiled against that shape).</summary>
    public System.Collections.ObjectModel.Collection<ResourceDictionary> MergedDictionaries { get; } = new();

    /// <summary>
    /// Pack URI this dictionary was loaded from (matches
    /// ResourceDictionary.Source; set for Source-loaded dictionaries, null
    /// for inline ones. Converted AvalonDock docking managers assign it on
    /// theme change).
    /// </summary>
    public Uri? Source { get; set; }

    /// <summary>
    /// Raised after an entry is added or replaced (DynamicResource refresh
    /// subscribes; merged dictionaries forward through their owner).
    /// </summary>
    internal event Action? Changed;

    /// <summary>Adds a keyed value (duplicate keys are an error, like WPF).</summary>
    public void Add(object key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_entries.ContainsKey(key))
            throw new InvalidOperationException($"Duplicate resource key '{key}'.");
        _entries.Add(key, value);
        Changed?.Invoke();
    }

    /// <summary>Adds a merged child (structure preserved, like WPF) and notifies.</summary>
    internal void AddMerged(ResourceDictionary child)
    {
        ArgumentNullException.ThrowIfNull(child);
        MergedDictionaries.Add(child);
        Changed?.Invoke();
    }

    /// <summary>
    /// Adds an x:Shared="False" entry: the factory materializes one fresh
    /// instance per lookup (duplicate keys are an error, like <see cref="Add"/>).
    /// </summary>
    internal void AddDeferred(object key, Func<object?> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        if (_entries.ContainsKey(key) || _deferred.ContainsKey(key))
            throw new InvalidOperationException($"Duplicate resource key '{key}'.");
        _deferred.Add(key, factory);
        Changed?.Invoke();
    }

    /// <summary>Local entry keys (merge support; merged dictionaries stay opaque).</summary>
    internal IEnumerable<object> Keys => _entries.Keys.Concat(_deferred.Keys);

    /// <summary>
    /// Local entries (XamlWriter round-trips the inline ones). Deferred
    /// entries materialize once per enumeration; inline dictionaries never
    /// carry them, so the writer path is unaffected.
    /// </summary>
    internal IEnumerable<KeyValuePair<object, object?>> LocalEntries() =>
        _entries.Concat(_deferred.Select(pair => new KeyValuePair<object, object?>(pair.Key, pair.Value()))).ToArray();

    /// <summary>Local entry count (Source/inline disambiguation for the writer).</summary>
    internal int EntryCount() => _entries.Count + _deferred.Count;

    /// <summary>Local entries plus merged dictionaries in order.</summary>
    public bool TryGetValue(object key, out object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_entries.TryGetValue(key, out value)) return true;
        if (_deferred.TryGetValue(key, out var factory))
        {
            value = factory();
            return true;
        }
        // Native ResourceDictionary consults merged dictionaries last-added-first:
        // a later merge wins over an earlier one for the same key (verified
        // against the native WPF oracle in E:/tmp/wpf-review-20261029/ParityProbe).
        for (var i = MergedDictionaries.Count - 1; i >= 0; i--)
            if (MergedDictionaries[i].TryGetValue(key, out value)) return true;
        value = null;
        return false;
    }

    /// <summary>Whether the key resolves locally or through a merge.</summary>
    public bool Contains(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return TryGetValue(key, out _);
    }

    /// <summary>Keyed access (matches ResourceDictionary.this[]; missing keys return null).</summary>
    public object? this[object key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            return TryGetValue(key, out var value) ? value : null;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(key);
            _entries[key] = value;
            Changed?.Invoke();
        }
    }
}

/// <summary>
/// Resource stream handle (matches System.Windows.Resources.StreamResourceInfo).
/// Gallery ControlExample.LoadResource reads page XAML through
/// <see cref="Application.GetResourceStream"/> and treats a null return as
/// "resource absent"; the stream below resolves against the same in-memory
/// pack store and converted-assembly bytes as the rest of the engine.
/// </summary>
public sealed class StreamResourceInfo
{
    /// <summary>Creates an empty handle (null stream, generic content type).</summary>
    public StreamResourceInfo() : this(null, "application/octet-stream") { }

    /// <summary>Creates a handle over the given stream and content type.</summary>
    public StreamResourceInfo(Stream? stream, string? contentType)
    {
        Stream = stream;
        ContentType = string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType;
    }

    /// <summary>Resource bytes (null when the handle is empty).</summary>
    public Stream? Stream { get; }

    /// <summary>Content type inferred from the resource extension.</summary>
    public string ContentType { get; }
}

/// <summary>
/// pack:// URI parsing for application resources (WPF subset). Accepted:
/// <c>pack://application:,,,/path</c>,
/// <c>pack://application:,,,/Assembly;component/path</c> (strong-named
/// generated code stamps the version between the short name and
/// <c>;component/</c>, e.g. <c>/HandyControlDemo;V3.6.0.0;component/app.xaml</c>;
/// real WPF matches the short name and ignores the stamp for resolution),
/// <c>pack://siteoforigin:,,,/path</c> (treated like application: the engine
/// has no separate deployment location), and bare relative paths
/// (<c>Resources/Brushes.xaml</c>). Normal form is a case-insensitive
/// <c>assembly|path</c> key with forward slashes.
/// </summary>
public static class PackUri
{
    /// <summary>Parsed pack URI: optional assembly plus resource path.</summary>
    public sealed record ResolvedPackUri(string? Assembly, string Path);

    /// <summary>Parses a pack or relative resource URI.</summary>
    public static ResolvedPackUri Parse(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var s = uri.Trim().Replace('\\', '/');
        if (s.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
        {
            // pack://application:,,,/path (authorities carry no slash).
            var rest = s["pack://".Length..];
            var slash = rest.IndexOf('/');
            if (slash < 0) throw new FormatException($"'{uri}' is not a pack URI (missing path).");
            var authority = rest[..slash];
            var path = rest[slash..];
            if (!authority.Equals("application:,,,", StringComparison.OrdinalIgnoreCase) &&
                !authority.Equals("siteoforigin:,,,", StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"'{uri}' has an unknown pack authority (application/siteoforigin).");
            return SplitAssembly(path, uri);
        }
        if (s.Contains("://", StringComparison.Ordinal))
            throw new FormatException($"'{uri}' is not a pack URI (only pack:// or relative paths).");
        // Generated-code shorthand "/Assembly;component/path" (no pack://
        // prefix): real InitializeComponent calls pass exactly this form.
        // Strong-named builds stamp the version before ";component/"
        // ("/HandyControlDemo;V3.6.0.0;component/app.xaml"); the stamp is
        // stripped so the self-reference resolves to the loaded assembly.
        var shorthand = s.TrimStart('/');
        var semi = shorthand.IndexOf(';');
        if (semi > 0 && !shorthand[..semi].Contains('/'))
        {
            const string marker = ";component/";
            var idx = shorthand.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
                return new ResolvedPackUri(
                    StripAssemblyQualifiers(shorthand[..idx], uri),
                    NormalizePath(shorthand[(idx + marker.Length)..]));
        }
        return new ResolvedPackUri(null, NormalizePath(s));
    }

    private static ResolvedPackUri SplitAssembly(string path, string original)
    {
        var p = path.TrimStart('/');
        var semi = p.IndexOf(';');
        if (semi < 0) return new ResolvedPackUri(null, NormalizePath(p));
        var shortName = p[..semi].Trim();
        var rest = p[(semi + 1)..];
        const string component = "component/";
        // Strong-named generated code stamps the version between the short
        // name and ";component/": real WPF matches the short name and ignores
        // the stamp, so qualifier segments are skipped here.
        while (!rest.StartsWith(component, StringComparison.OrdinalIgnoreCase))
        {
            var next = rest.IndexOf(';');
            if (next < 0)
                throw new FormatException($"'{original}' is not a pack URI (expected Assembly;component/path).");
            if (!IsAssemblyQualifier(rest[..next]))
                throw new FormatException($"'{original}' is not a pack URI (expected Assembly;component/path).");
            rest = rest[(next + 1)..];
        }
        if (string.IsNullOrWhiteSpace(shortName))
            throw new FormatException($"'{original}' is not a pack URI (empty assembly).");
        return new ResolvedPackUri(shortName, NormalizePath(rest[component.Length..]));
    }

    /// <summary>
    /// Strips generated-code assembly qualifiers (";V3.6.0.0") from a pack-URI
    /// assembly segment, keeping the short name real WPF resolves. Unknown
    /// segments stay loud: only the version stamp real generators emit is
    /// accepted.
    /// </summary>
    private static string StripAssemblyQualifiers(string assemblyPart, string original)
    {
        var segments = assemblyPart.Split(';');
        var shortName = segments[0].Trim();
        if (string.IsNullOrWhiteSpace(shortName))
            throw new FormatException($"'{original}' is not a pack URI (empty assembly).");
        foreach (var qualifier in segments.Skip(1))
        {
            if (!IsAssemblyQualifier(qualifier))
                throw new FormatException($"'{original}' is not a pack URI (expected Assembly[;V<version>];component/path).");
        }
        return shortName;
    }

    /// <summary>
    /// Whether a pack-URI assembly segment is the generated version stamp
    /// ("V3.6.0.0", case-insensitive): real WPF ignores it for resolution.
    /// </summary>
    private static bool IsAssemblyQualifier(string segment)
    {
        var s = segment.Trim();
        return s.Length >= 2 && (s[0] == 'V' || s[0] == 'v') && char.IsAsciiDigit(s[1]) &&
            s.Skip(1).All(c => char.IsAsciiDigit(c) || c == '.');
    }

    private static string NormalizePath(string path)
    {
        var p = path.Trim().Replace('\\', '/').TrimStart('/');
        while (p.Contains("//", StringComparison.Ordinal)) p = p.Replace("//", "/", StringComparison.Ordinal);
        if (p.Length == 0) throw new FormatException("Empty resource path.");
        return p;
    }

    /// <summary>Registry key for a parsed pack URI (assembly-insensitive case).</summary>
    public static string KeyFor(ResolvedPackUri parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        return $"{parsed.Assembly ?? string.Empty}|{parsed.Path}".ToLowerInvariant();
    }

    /// <summary>Registry key for a raw URI string.</summary>
    public static string KeyFor(string uri) => KeyFor(Parse(uri));
}

/// <summary>
/// Minimal application object: the global resource scope behind element
/// resources, plus the in-memory pack store that <c>Source=</c> dictionaries
/// load from. Real WPF resolves pack URIs against assemblies; the browser
/// engine has no assembly store, so hosts/tests register XAML text by pack
/// URI (a future fetch layer can fill the same store).
/// </summary>
public class Application
{
    private readonly Dictionary<string, string> _packFiles = new(StringComparer.Ordinal);

    /// <summary>The running application (null until a host sets one).</summary>
    public static Application? Current { get; set; }

    /// <summary>Creates the application and makes it current (WPF parity).</summary>
    public Application()
    {
        Current = this;
        Resources.Changed += () => ResourcesChanged?.Invoke();
        // The Fluent theme pack never ships to the browser: the engine
        // stages its text in every application's pack store (like converted
        // packs), so pack-URI and stream lookups resolve the same Source
        // converted Fluent-era apps merge first.
        RegisterPackResource(FluentTheme.PackUri, FluentTheme.Xaml);
    }

    /// <summary>
    /// Framework-provided theme keys (the Fluent pack the browser never
    /// ships). Parsed once; explicit merges reuse the same text through the
    /// pack store instead of this copy.
    /// </summary>
    private static readonly Lazy<ResourceDictionary> FluentFallback = new(
        () => XamlLoader.LoadResourceDictionary(FluentTheme.Xaml),
        System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// HandyControl tab-style/shadow fallback (the lib theme keys converted
    /// demo views read at application scope, which never ship in convertible
    /// form; see HandyTheme). Parsed once, like the Fluent fallback.
    /// </summary>
    private static readonly Lazy<ResourceDictionary> HandyFallback = new(
        () => XamlLoader.LoadResourceDictionary(HandyTheme.Xaml),
        System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Raised after <see cref="Resources"/> gains or replaces an entry
    /// (DynamicResource subscribers and the de-emphasized-text coupling
    /// refresh through this, like WPF's resource invalidation).
    /// </summary>
    internal event Action? ResourcesChanged;

    /// <summary>
    /// Application theme preference (matches System.Windows.Application.ThemeMode;
    /// converted Fluent-era apps read it to pick light/dark resources).
    /// </summary>
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;

    /// <summary>
    /// App shutdown policy (matches Application.ShutdownMode; the browser
    /// host owns the loop, so window-close auto-shutdown is explicit: the
    /// last tracked window closing raises Exit under OnLastWindowClose).
    /// </summary>
    public ShutdownMode ShutdownMode { get; set; } = ShutdownMode.OnLastWindowClose;

    /// <summary>Live windows (matches Application.Windows).</summary>
    public WindowCollection Windows { get; } = new();

    /// <summary>Raised at startup (matches Application.Startup).</summary>
    public event StartupEventHandler? Startup;

    /// <summary>Raised at shutdown (matches Application.Exit).</summary>
    public event ExitEventHandler? Exit;

    /// <summary>
    /// Raises <see cref="Startup"/> (matches Application.OnStartup; the
    /// browser host does not pump a loop, so boot code calls this when the
    /// app is ready instead of relying on Run).
    /// </summary>
    protected virtual void OnStartup(StartupEventArgs e) => Startup?.Invoke(this, e);

    /// <summary>Raises <see cref="Exit"/> (matches Application.OnExit).</summary>
    protected virtual void OnExit(ExitEventArgs e) => Exit?.Invoke(this, e);

    /// <summary>Raises Startup with the given arguments (host/boot seam).</summary>
    public void RaiseStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        OnStartup(e);
    }

    /// <summary>Raises Exit with the given code (shutdown seam).</summary>
    public void RaiseExit(ExitEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        OnExit(e);
    }

    internal void RegisterWindow(Window window) => Windows.AddInternal(window);

    internal void UnregisterWindow(Window window)
    {
        Windows.RemoveInternal(window);
        if (!_shuttingDown && ShutdownMode == ShutdownMode.OnLastWindowClose && Windows.Count == 0)
            RaiseExit(new ExitEventArgs(ExitCode));
    }

    private bool _shuttingDown;

    /// <summary>
    /// Closes the main window (matches Application.Shutdown: the browser host
    /// owns the loop, so shutdown hides the app window instead of exiting).
    /// Exit fires exactly once (window-close auto-exit is suppressed while
    /// the explicit shutdown drains the live set, like WPF).
    /// </summary>
    public void Shutdown()
    {
        try
        {
            _shuttingDown = true;
            RaiseExit(new ExitEventArgs(ExitCode));
            foreach (var window in Windows.ToArray()) window.Close();
            MainWindow?.Close();
        }
        finally { _shuttingDown = false; }
    }

    /// <summary>Shutdown with an exit code (the code is retained, not acted on headless).</summary>
    public void Shutdown(int exitCode)
    {
        ExitCode = exitCode;
        Shutdown();
    }

    /// <summary>Exit code from <see cref="Shutdown(int)"/> (default zero).</summary>
    public int ExitCode { get; private set; }

    /// <summary>
    /// Startup window XAML, from App.xaml StartupUri (null until set, Uri
    /// like real WPF so rewritten generated assignments bind). Converted
    /// real apps resolve this at boot instead of running a loop.
    /// </summary>
    public Uri? StartupUri { get; set; }

    /// <summary>
    /// The window the host shows. Assigned explicitly by
    /// <see cref="WpfAppBoot.Boot"/> (construction stays side-effect-free,
    /// so parallel hosts and test runners never cross-register).
    /// </summary>
    public Window? MainWindow { get; set; }

    /// <summary>
    /// Converted-app entry point: rewritten generated InitializeComponent
    /// calls land here. Resolves the pack/relative URI against loose XAML or
    /// real compiled BAML embedded in loaded assemblies and hydrates the
    /// existing component instance (WPF LoadComponent semantics).
    /// </summary>
    public static void LoadComponent(object component, Uri resourceLocator) =>
        WpfAppBoot.LoadComponent(component, resourceLocator);

    /// <summary>
    /// Real WPF raises Startup here (converted OnStartup builds MainWindow),
    /// shows the main window and pumps a dispatcher loop. The browser host
    /// owns the loop (rAF pump + WasmWpfHost): converted entry points run
    /// their startup (DI, MainWindow assignment) and end here, which throws
    /// the browser-equivalent sentinel WpfAppBoot.Boot catches. Startup still
    /// fires first, so code-driven startup (like the PC Space Map
    /// OnStartup creating its MainWindow and starting its local API)
    /// executes under boot exactly once. Calling it outside boot is a
    /// precise error, never a silent hang.
    /// </summary>
    public int Run()
    {
        RaiseStartup(new StartupEventArgs());
        throw new InvalidOperationException(
            WpfAppBoot.RunSentinelPrefix + ": the host shows WpfAppBoot.Boot windows " +
            "and pumps the browser loop instead.");
    }

    /// <summary>Application-scope resources (last in the lookup chain).</summary>
    public ResourceDictionary Resources { get; } = new();

    /// <summary>
    /// Resolves a resource through application scope (matches
    /// Application.TryFindResource; converted code calls this directly).
    /// Local entries win, then merged dictionaries, then the
    /// framework-provided Fluent theme keys (the pack converted Fluent-era
    /// apps merge, which never ships to the browser), then the
    /// HandyControl tab-style/shadow keys (same unshippable-theme situation,
    /// see HandyTheme), then the converted demo ViewModelLocator under key
    /// "Locator" (same HandyTheme), then the converted demo/lib value
    /// converters (see Slice105HandyViews), the converted-target
    /// styles (see Slice110HandyElement) and the inert BehaviorXY200
    /// marker (see Slice111HandyPanels); misses stay false.
    /// </summary>
    public bool TryFindResource(object key, out object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (Resources.TryGetValue(key, out value)) return true;
        // Theme fallback: a stale subscriber (e.g. a converted control
        // outliving its merged dictionaries) still resolves theme keys,
        // like WPF's theme scope. A broken theme approximation must never
        // fail a lookup; the merge path stays loud instead.
        try
        {
            if (FluentFallback.Value.TryGetValue(key, out value) && value is not null) return true;
            if (HandyFallback.Value.TryGetValue(key, out value) && value is not null) return true;
            if (HandyTheme.TryResolveLocator(key, out value) && value is not null) return true;
            if (Slice105HandyViews.TryResolveConvertedConverter(key, out value) && value is not null) return true;
            if (Slice110HandyElement.TryResolveConvertedStyle(key, out value) && value is not null) return true;
            if (Slice111HandyPanels.TryResolveBehaviorXY200(key, out value) && value is not null) return true;
        }
        catch
        {
        }
        // System scope last (matches WPF: SystemColors/SystemParameters
        // resolve when no theme or app entry provides the key).
        if (SystemTheme.TryFindSystemResource(key, out value) && value is not null) return true;
        value = null;
        return false;
    }

    /// <summary>
    /// Resolves a resource or throws (matches Application.FindResource).
    /// </summary>
    public object FindResource(object key)
    {
        if (TryFindResource(key, out var value) && value is not null) return value;
        throw new InvalidOperationException($"Resource '{key}' was not found in application resources.");
    }

    /// <summary>
    /// Registers loose XAML text under a pack URI for
    /// <c>ResourceDictionary Source=</c> loading (overwrites).
    /// </summary>
    public void RegisterPackResource(string packUri, string xaml)
    {
        ArgumentNullException.ThrowIfNull(packUri);
        ArgumentNullException.ThrowIfNull(xaml);
        _packFiles[PackUri.KeyFor(packUri)] = xaml;
    }

    /// <summary>Looks up registered XAML text by pack URI (null when absent).</summary>
    public string? GetPackResource(string packUri)
    {
        ArgumentNullException.ThrowIfNull(packUri);
        return _packFiles.TryGetValue(PackUri.KeyFor(packUri), out var xaml) ? xaml : null;
    }

    /// <summary>
    /// Resource stream for a pack URI (matches
    /// System.Windows.Application.GetResourceStream). Resolves the same
    /// stores as Source dictionaries and BitmapImage: registered pack text
    /// first, then converted-assembly manifest and .g.resources bytes.
    /// Returns null when the resource is absent (the Gallery null pattern);
    /// malformed or non-pack URIs are absent too.
    /// </summary>
    public static StreamResourceInfo? GetResourceStream(Uri uriResource)
    {
        ArgumentNullException.ThrowIfNull(uriResource);
        if (!TryParsePack(uriResource, out var parsed) || parsed is null) return null;
        if (Current?._packFiles.TryGetValue(PackUri.KeyFor(parsed), out var text) == true && text is not null)
            return new StreamResourceInfo(
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)), ContentTypeFor(parsed.Path));
        var bytes = WpfAppBoot.FindPackBytes(parsed.Path, parsed.Assembly);
        return bytes is null ? null : new StreamResourceInfo(new MemoryStream(bytes), ContentTypeFor(parsed.Path));
    }

    /// <summary>
    /// Content-file stream for a pack URI (matches
    /// System.Windows.Application.GetContentStream). The browser engine has
    /// no separate loose content store, so content resolves through the
    /// same pack stores as resources; absent stays null.
    /// </summary>
    public static StreamResourceInfo? GetContentStream(Uri uriContent)
    {
        ArgumentNullException.ThrowIfNull(uriContent);
        return GetResourceStream(uriContent);
    }

    /// <summary>
    /// Remote stream fetch has no browser-engine equivalent (no network
    /// fetch in the resource path): always a precise error, never a silent
    /// null. Pack URIs are not remote; route them to
    /// <see cref="GetResourceStream"/> instead.
    /// </summary>
    public static StreamResourceInfo GetRemoteStream(Uri uriRemote)
    {
        ArgumentNullException.ThrowIfNull(uriRemote);
        throw new InvalidOperationException(
            $"Cannot fetch remote resource '{uriRemote}': the browser engine has no remote stream transport " +
            "(register the bytes with Application.RegisterPackResource instead).");
    }

    private static bool TryParsePack(Uri uri, out PackUri.ResolvedPackUri? parsed)
    {
        parsed = null;
        try
        {
            parsed = PackUri.Parse(uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ContentTypeFor(string path)
    {
        var dot = path.LastIndexOf('.');
        var extension = dot >= 0 ? path[dot..].ToLowerInvariant() : string.Empty;
        return extension switch
        {
            ".xaml" or ".xml" => "application/xaml+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            ".txt" or ".text" => "text/plain",
            ".json" => "application/json",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream",
        };
    }

    /// <summary>
    /// Deferred resource subscriptions (matches WPF DynamicResource):
    /// subscribers resolve once at load and re-apply when
    /// <see cref="Application.Resources"/> changes or
    /// <see cref="SystemParameters.StaticPropertyChanged"/> fires (theme
    /// broadcasts). Missing keys stay unresolved until they appear (WPF
    /// leaves them unset); one failing subscriber never blocks the rest.
    /// </summary>
    internal static class DynamicResourceManager
    {
        private sealed record Subscription(
            WeakReference<FrameworkElement> Target, object Owner, object Key, Action<FrameworkElement> Refresh);

        private static readonly List<Subscription> Subscriptions = new();
        private static WeakReference<Application>? _hookedApp;
        private static bool _staticHooked;

        /// <summary>Re-applies a whole style when its dynamic inputs change.</summary>
        internal static void SubscribeStyle(FrameworkElement target, Style style)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(style);
            Subscribe(target, style, style, refreshed => ReapplyStyle(refreshed, style));
        }

        /// <summary>
        /// Expression probe for DependencyPropertyHelper (internal): true
        /// while a live DynamicResource subscription owns (target, dp)
        /// (DynamicResourceExtension.ApplyTo subscribes the DP itself as the
        /// owner, so owner identity is the property).
        /// </summary>
        internal static bool HasPropertySubscription(DependencyObject target, DependencyProperty dp)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(dp);
            if (target is not FrameworkElement element) return false;
            lock (Subscriptions)
            {
                Prune();
                foreach (var subscription in Subscriptions)
                {
                    if (ReferenceEquals(subscription.Owner, dp) &&
                        subscription.Target.TryGetTarget(out var existing) &&
                        ReferenceEquals(existing, element))
                        return true;
                }
                return false;
            }
        }

        /// <summary>Re-applies one resolved value when the key changes.</summary>
        internal static void SubscribeProperty(FrameworkElement target, object owner, object key, Action<FrameworkElement, object?> apply)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(apply);
            Subscribe(target, owner, key, refreshed =>
            {
                if (refreshed.TryFindResource(key, out var value) && value is not null)
                    apply(refreshed, value);
            });
        }

        private static void Subscribe(FrameworkElement target, object owner, object key, Action<FrameworkElement> refresh)
        {
            lock (Subscriptions)
            {
                EnsureHooked();
                Prune();
                // Re-pointing the same owner replaces its subscription (WPF
                // keeps one live expression per property, not a stack).
                Subscriptions.RemoveAll(s => ReferenceEquals(s.Owner, owner) &&
                    s.Target.TryGetTarget(out var existing) && ReferenceEquals(existing, target));
                Subscriptions.Add(new Subscription(new WeakReference<FrameworkElement>(target), owner, key, refresh));
            }
        }

        private static void EnsureHooked()
        {
            if (!_staticHooked)
            {
                SystemParameters.StaticPropertyChanged += (_, _) => RefreshAll();
                _staticHooked = true;
            }
            var current = Application.Current;
            if (current is not null && !IsHooked(current))
            {
                current.ResourcesChanged += RefreshAll;
                _hookedApp = new WeakReference<Application>(current);
            }
        }

        private static bool IsHooked(Application current) =>
            _hookedApp is not null && _hookedApp.TryGetTarget(out var hooked) && ReferenceEquals(hooked, current);

        private static void Prune() =>
            Subscriptions.RemoveAll(s => !s.Target.TryGetTarget(out _));

        private static void ReapplyStyle(FrameworkElement target, Style style)
        {
            style.ApplyTo(target, new StyleApplyContext { SkipEventWires = true });
        }

        private static void RefreshAll()
        {
            List<Subscription> snapshot;
            lock (Subscriptions)
            {
                Prune();
                snapshot = new List<Subscription>(Subscriptions);
            }
            foreach (var subscription in snapshot)
            {
                if (!subscription.Target.TryGetTarget(out var target)) continue;
                try { subscription.Refresh(target); }
                catch
                {
                    // One failing subscriber keeps its old values; load-time
                    // errors stay loud at the load site instead.
                }
            }
        }

        /// <summary>Live subscription count (tests pin subscribe-once behavior).</summary>
        internal static int LiveSubscriptionCount
        {
            get
            {
                lock (Subscriptions)
                {
                    Prune();
                    return Subscriptions.Count;
                }
            }
        }
    }

    /// <summary>Converts a resource value to its canonical XAML string.</summary>
    public static string ResourceToString(object? value, string key)
    {
        return value switch
        {
            string s => s,
            bool b => b ? "True" : "False",
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            float f => ((double)f).ToString(CultureInfo.InvariantCulture),
            uint rgba => $"#{(rgba & 0xFF):X2}{(rgba >> 24) & 0xFF:X2}{(rgba >> 16) & 0xFF:X2}{(rgba >> 8) & 0xFF:X2}",
            FontFamily family => family.Source,
            SolidColorBrush solid => $"#{(byte)(solid.Color.A * solid.Opacity):X2}{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}",
            Color color => color.ToString(),
            Thickness thickness => string.Join(",", new[] { thickness.Left, thickness.Top, thickness.Right, thickness.Bottom }.Select(d => d.ToString(CultureInfo.InvariantCulture))),
            CornerRadius radius => radius.ToString(),
            null => throw new InvalidOperationException($"Resource '{key}' is null."),
            _ => throw new InvalidOperationException(
                $"Resource '{key}' is a {value.GetType().Name} (string/double/bool/color only)."),
        };
    }
}
