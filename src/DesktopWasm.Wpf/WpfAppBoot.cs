using System.Collections;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml;

namespace DesktopWasm.Wpf;

/// <summary>
/// Converted real-WPF application boot (shim entry, Step 30).
///
/// The original Microsoft assemblies stay untouched: the converter
/// (DesktopWasm.Convert) rewrites the app's assembly/type references onto
/// this engine and embeds the app's loose .xaml (plus its real compiled
/// .g.resources BAML, which still ships inside). Generated
/// InitializeComponent calls land on
/// <see cref="Application.LoadComponent(object, Uri)"/>, which hydrates the
/// existing component instance; <see cref="Boot"/> replicates the framework
/// part of App startup (create Application, run InitializeComponent, resolve
/// StartupUri to the first window) without a blocking dispatcher loop.
/// </summary>
public static class WpfAppBoot
{
    /// <summary>
    /// Converter map version the boot path understands. The converter stamps
    /// this into its receipt; the host refuses receipts from other versions.
    /// </summary>
    public const string MapVersion = "wpf-convert-v1";

    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// WPF Application.LoadComponent equivalent for converted apps: resolves
    /// the pack/relative URI against loose XAML first, then real compiled
    /// BAML, both read from loaded-assembly manifest resources, and hydrates
    /// the existing component (Window, Page, UserControl, Border or
    /// Application) instead of building a detached tree.
    /// </summary>
    public static void LoadComponent(object component, Uri resourceLocator)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(resourceLocator);
        var parsed = PackUri.Parse(resourceLocator.ToString());
        var preferred = component.GetType().Assembly;
        if (parsed.Assembly is { } assemblyName && !string.Equals(preferred.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
            preferred = Assembly.Load(new AssemblyName(assemblyName));
        var source = ResolveSource(parsed.Path, preferred)
            ?? throw new XamlLoadException(
                $"No convertible component '{parsed.Path}': ship the app's loose .xaml or its real " +
                ".g.resources BAML in the converted assembly (see the converter receipt).");
        var previousSource = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = component.GetType().Assembly;
        try
        {
            Hydrate(component, source, parsed.Path);
        }
        finally
        {
            XamlLoader.CustomElementSource = previousSource;
        }
    }

    /// <summary>
    /// Boots a converted application image (the rewritten managed .dll bytes)
    /// and returns its main window for the host to show. Mirrors framework
    /// startup without running a dispatcher loop: the first Application
    /// subclass is created (becoming Current), its InitializeComponent runs
    /// (remapped LoadComponent calls hydrate), and StartupUri resolves to
    /// the first window when nothing registered MainWindow directly.
    /// </summary>
    public static Window Boot(byte[] assemblyImage, Action<string>? report = null)
    {
        ArgumentNullException.ThrowIfNull(assemblyImage);
        return Boot(Assembly.Load(assemblyImage), report);
    }

    public static Window Boot(Assembly asm, Action<string>? report = null)
    {
        ArgumentNullException.ThrowIfNull(asm);
        report?.Invoke("boot: entry image loaded");
        var previousSource = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = asm;
        // The engine installs its dispatcher context for converted
        // startup when the host left none (browser renderer thread);
        // restore whatever was ambient so host awaits after Boot keep
        // their routing instead of posting to an unpumped queue.
        var previousContext = SynchronizationContext.Current;
        SeedAppSettings(asm);
        try
        {
            return BootCore(asm, report);
        }
        finally
        {
            XamlLoader.CustomElementSource = previousSource;
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    /// <summary>
    /// Seeds the engine ConfigurationManager adapter from the converted image's
    /// "__AppSettings.json" resource (packager-written from the real upstream
    /// ".dll.config"); missing or unreadable seed leaves defaults, like a
    /// missing config section on desktop. Peripheral data never sinks boot.
    /// </summary>
    private static void SeedAppSettings(System.Reflection.Assembly asm)
    {
        try
        {
            using var stream = asm.GetManifestResourceStream("__AppSettings.json");
            if (stream is null) return;
            using var reader = new System.IO.StreamReader(stream);
            var pairs = JsonSerializer.Deserialize<System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string?>>>(reader.ReadToEnd());
            if (pairs is not null) System.Configuration.ConfigurationManager.SeedAppSettings(pairs);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Sentinel prefix thrown by Application.Run (the browser host owns the
    /// loop): a converted entry point is expected to end here. Matched by
    /// prefix so the help text after it can evolve.
    /// </summary>
    internal const string RunSentinelPrefix = "Application.Run has no browser equivalent";

    private static Window? TryBootFromEntryPoint(Assembly asm)
    {
        // Code-driven startup (DI containers, explicit MainWindow assignment):
        // the converted entry point builds its own App and ends in
        // Application.Run, which throws the sentinel the browser host owns.
        var entry = asm.EntryPoint;
        if (entry is null || !entry.IsStatic) return null;
        var parameters = entry.GetParameters();
        if (parameters.Length > 1) return null;
        if (parameters.Length == 1 && parameters[0].ParameterType != typeof(string[])) return null;
        object?[]? invokeArgs = parameters.Length == 0 ? null : new object?[] { Array.Empty<string>() };
        try
        {
            var result = entry.Invoke(null, invokeArgs);
            if (result is System.Threading.Tasks.Task task) task.GetAwaiter().GetResult();
        }
        catch (TargetInvocationException ex) when (IsRunSentinel(ex.InnerException))
        {
        }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException($"Converted app startup failed in entry point: {ex.InnerException?.Message}", ex.InnerException ?? ex);
        }
        catch (InvalidOperationException ex) when (IsRunSentinel(ex))
        {
        }
        return Application.Current?.MainWindow;
    }

    private static bool IsRunSentinel(Exception? ex) =>
        ex is InvalidOperationException invalid &&
        invalid.Message.StartsWith(RunSentinelPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Installs the UI-thread synchronization context when the host left
    /// none (converted ctors call
    /// TaskScheduler.FromCurrentSynchronizationContext, which needs one,
    /// like real-WPF UI threads; a host-provided context keeps winning).
    /// </summary>
    internal static void EnsureDispatcherSynchronizationContext()
    {
        if (SynchronizationContext.Current is null)
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
    }

    private static Window BootCore(Assembly asm, Action<string>? report)
    {
        EnsureDispatcherSynchronizationContext();
        report?.Invoke("boot: resolving application type");
        // Converted assemblies may declare types over pass-through references
        // (e.g. designer Settings over ConfigurationManager) that are never
        // touched at runtime: skip unloadable types instead of failing boot.
        Type? appType;
        string loadNotes = string.Empty;
        try
        {
            appType = asm.GetTypes().FirstOrDefault(IsBootApp);
        }
        catch (ReflectionTypeLoadException ex)
        {
            appType = ex.Types.Where(t => t is not null).FirstOrDefault(IsBootApp);
            loadNotes = string.Join("; ", ex.LoaderExceptions
                .Select(e => e.GetType().Name + ": " + e.Message).Distinct().Take(5));
        }
        if (appType is null)
            throw new InvalidOperationException(
                $"Converted assembly '{asm.GetName().Name}' has no convertible Application type " +
                "(a non-abstract Application subclass with a parameterless constructor)." +
                (loadNotes.Length == 0 ? string.Empty : " Type-load failures: " + loadNotes));

        // Code-driven startup first (DI containers, explicit MainWindow):
        // the converted entry point builds its own App and ends in
        // Application.Run, which throws the sentinel above.
        report?.Invoke("boot: trying entry point");
        var coded = TryBootFromEntryPoint(asm);
        report?.Invoke(coded is not null ? "boot: entry point produced a window" : "boot: no entry point window");
        if (coded is not null) { SplashScreen.CloseAutoShown(); return coded; }

        report?.Invoke("boot: constructing application");
        var app = (Application)Activator.CreateInstance(appType)!;
        report?.Invoke("boot: application constructed");
        var init = appType.GetMethod("InitializeComponent",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            Type.EmptyTypes);
        if (init is null)
            throw new InvalidOperationException(
                $"Converted type '{appType.FullName}' has no InitializeComponent: the app must be " +
                "converted with its generated code (keep codebehind; do not strip XAML wiring).");
        report?.Invoke("boot: running InitializeComponent");
        try
        {
            init.Invoke(app, null);
        }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException(
                $"Converted app startup failed in InitializeComponent: {ex.InnerException?.Message}", ex.InnerException ?? ex);
        }
        report?.Invoke("boot: initialized");

        report?.Invoke("boot: resolving main window");
        if (app.MainWindow is not null) { SplashScreen.CloseAutoShown(); report?.Invoke("boot: main window ready"); return app.MainWindow; }
        if (app.StartupUri is not null)
        {
            // Window construction is side-effect-free by design (no global
            // auto-registration), so boot assigns MainWindow explicitly.
            var window = CreateStartupWindow(asm, app.StartupUri);
            app.MainWindow ??= window;
            SplashScreen.CloseAutoShown();
            return app.MainWindow;
        }
        throw new InvalidOperationException(
            $"Converted app '{appType.FullName}' produced no window: neither a constructed window " +
            "registered MainWindow nor StartupUri resolved to one.");
    }

    private static bool IsBootApp(Type? t) =>
        t is not null && t.IsSubclassOf(typeof(Application)) && !t.IsAbstract &&
        t.GetConstructor(Type.EmptyTypes) is not null;

    private static Window CreateStartupWindow(Assembly appAssembly, Uri startupUri)
    {
        var parsed = PackUri.Parse(startupUri.ToString());
        var source = ResolveSource(parsed.Path, appAssembly)
            ?? throw new InvalidOperationException(
                $"StartupUri '{startupUri}' resolves to no embedded component '{parsed.Path}'.");
        if (source is LooseSource loose)
        {
            if (loose.RootClass is null)
                throw new InvalidOperationException(
                    $"StartupUri '{startupUri}' has no x:Class root: cannot pick a codebehind type.");
            var type = appAssembly.GetType(loose.RootClass, throwOnError: false)
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => SafeGetType(a, loose.RootClass))
                    .FirstOrDefault(t => t is not null);
            if (type is null)
                throw new InvalidOperationException(
                    $"StartupUri '{startupUri}' names unknown type '{loose.RootClass}'.");
            if (type.GetConstructor(Type.EmptyTypes) is null)
                throw new InvalidOperationException(
                    $"Startup type '{loose.RootClass}' has no parameterless constructor.");
            if (typeof(Page).IsAssignableFrom(type))
            {
                // Real WPF shows StartupUri pages inside a navigation window;
                // the engine hosts the page the same way so boot keeps
                // returning a Window for the host to show.
                var page = (Page)Activator.CreateInstance(type)!;
                var navigation = new NavigationWindow();
                navigation.Navigate(page);
                return navigation;
            }
            try
            {
                return (Window)Activator.CreateInstance(type)!;
            }
            catch (InvalidCastException)
            {
                throw new InvalidOperationException(
                    $"Startup type '{loose.RootClass}' is not a Window or Page.");
            }
        }
        // Real BAML carries no x:Class the reader surfaces: the assembly must
        // hold exactly one window candidate (true for HelloWorld-style apps).
        var candidates = SafeGetTypes(appAssembly)
            .Where(t => t.IsSubclassOf(typeof(Window)) && !t.IsAbstract &&
                        t.GetConstructor(Type.EmptyTypes) is not null)
            .ToList();
        if (candidates.Count == 1) return (Window)Activator.CreateInstance(candidates[0])!;
        var pages = SafeGetTypes(appAssembly)
            .Where(t => t.IsSubclassOf(typeof(Page)) && !t.IsAbstract &&
                        t.GetConstructor(Type.EmptyTypes) is not null)
            .ToList();
        if (candidates.Count == 0 && pages.Count == 1)
        {
            var navigation = new NavigationWindow();
            navigation.Navigate((Page)Activator.CreateInstance(pages[0])!);
            return navigation;
        }
        throw new InvalidOperationException(
            $"StartupUri '{startupUri}' is real BAML with {candidates.Count} window and {pages.Count} page candidates: " +
            "ship the loose .xaml alongside so boot can bind x:Class.");
    }

    private static void Hydrate(object component, ComponentSource source, string path)
    {
        if (component is Application app)
        {
            if (source is not LooseSource looseApp)
                throw new XamlLoadException(
                    $"Application root '{path}' must load from loose XAML (App.xaml BAML is not supported).");
            HydrateApplication(app, looseApp.Text, path);
            return;
        }
        if (component is Window window)
        {
            var loaded = ParseWindow(source, path);
            if (loaded.Root is not Window parsed)
                throw new XamlLoadException($"Root of '{path}' is {loaded.Root.GetType().Name}, not a Window.");
            if (loaded.RootClass is not null && !loaded.RootClass.Equals(component.GetType().FullName, StringComparison.Ordinal))
                throw new XamlLoadException(
                    $"x:Class '{loaded.RootClass}' does not match component '{component.GetType().FullName}'.");
            if (parsed.GetType() != component.GetType() &&
                !component.GetType().IsSubclassOf(parsed.GetType()))
                throw new XamlLoadException(
                    $"Component '{component.GetType().FullName}' is not a '{parsed.GetType().Name}' (from '{path}').");
            window.Title = parsed.Title;
            window.IconSource = parsed.IconSource;
            if (parsed.Icon is not null && window.Icon is null) window.Icon = parsed.Icon;
            window.Width = parsed.Width;
            window.Height = parsed.Height;
            window.Margin = parsed.Margin;
            CopyWindowChrome(window, parsed);
            window.SetResources(parsed.Resources);
            if (parsed.HasLocalDataContext) window.DataContext = parsed.DataContext;
            window.Content = parsed.Content;
            // Generated Connect equivalent: names become fields, Click=
            // attributes become handlers (loose XAML always ships this).
            WireFields(component, loaded.Names, path);
            WireEvents(component, loaded, path);
            RegisterNameScope(component, loaded.Names);
            // Triggers parsed on the loader tree resolve against the detached
            // parsed root, so it shares the component namescope (same
            // element instances, moved into the component above).
            RegisterNameScope(loaded.Root, loaded.Names);
            window.RaiseLoadedTree();
            return;
        }

        if (component is Page page)
        {
            var loaded = ParseWindow(source, path);
            if (loaded.Root is not Page parsed)
                throw new XamlLoadException($"Root of '{path}' is {loaded.Root.GetType().Name}, not a Page.");
            if (loaded.RootClass is not null && !loaded.RootClass.Equals(component.GetType().FullName, StringComparison.Ordinal))
                throw new XamlLoadException(
                    $"x:Class '{loaded.RootClass}' does not match component '{component.GetType().FullName}'.");
            if (parsed.GetType() != component.GetType() &&
                !component.GetType().IsSubclassOf(parsed.GetType()))
                throw new XamlLoadException(
                    $"Component '{component.GetType().FullName}' is not a '{parsed.GetType().Name}' (from '{path}').");
            page.WindowTitle = parsed.WindowTitle;
            // Root chrome belongs to the instance like windows and user
            // controls: the loader applied DynamicResource brushes to the
            // detached parsed root, so they transfer over default
            // component values with WPF local-beats-style precedence
            // (Page owns Background/Foreground only).
            if (parsed.Background is not null && page.Background is null)
                page.Background = parsed.Background;
            if (parsed.Foreground is not null && page.Foreground is null)
                page.Foreground = parsed.Foreground;
            page.SetResources(parsed.Resources);
            if (parsed.HasLocalDataContext) page.DataContext = parsed.DataContext;
            page.Content = parsed.Content;
            // Same generated-Connect equivalent as windows.
            WireFields(component, loaded.Names, path);
            WireEvents(component, loaded, path);
            RegisterNameScope(component, loaded.Names);
            // Triggers parsed on the loader tree resolve against the detached
            // parsed root, so it shares the component namescope (same
            // element instances, moved into the component above).
            RegisterNameScope(loaded.Root, loaded.Names);
            page.RaiseLoadedTree();
            return;
        }
        if (component is UserControl userControl)
        {
            var loaded = ParseWindow(source, path);
            if (loaded.Root is not UserControl parsed)
                throw new XamlLoadException($"Root of '{path}' is {loaded.Root.GetType().Name}, not a UserControl.");
            if (loaded.RootClass is not null && !loaded.RootClass.Equals(component.GetType().FullName, StringComparison.Ordinal))
                throw new XamlLoadException(
                    $"x:Class '{loaded.RootClass}' does not match component '{component.GetType().FullName}'.");
            if (parsed.GetType() != component.GetType() &&
                !component.GetType().IsSubclassOf(parsed.GetType()))
                throw new XamlLoadException(
                    $"Component '{component.GetType().FullName}' is not a '{parsed.GetType().Name}' (from '{path}').");
            userControl.SetResources(parsed.Resources);
            if (parsed.HasLocalDataContext) userControl.DataContext = parsed.DataContext;
            userControl.Content = parsed.Content;
            // Root attributes belong to the instance (the parsed root is
            // detached): without these the control sizes to content instead
            // of its declared footprint (gallery HeaderTile 198x220).
            userControl.Width = parsed.Width;
            userControl.Height = parsed.Height;
            userControl.MinWidth = parsed.MinWidth;
            userControl.MinHeight = parsed.MinHeight;
            userControl.MaxWidth = parsed.MaxWidth;
            userControl.MaxHeight = parsed.MaxHeight;
            userControl.Margin = parsed.Margin;
            userControl.HorizontalAlignment = parsed.HorizontalAlignment;
            userControl.VerticalAlignment = parsed.VerticalAlignment;
            // Root chrome belongs to the instance like windows (see
            // CopyWindowChrome): the loader applied DynamicResource brushes
            // to the detached parsed root, so they transfer over default
            // component values with WPF local-beats-style precedence
            // (staged BrushDemo/LabelDemo roots read RegionBrush).
            if (parsed.Background is not null && userControl.Background is null)
                userControl.Background = parsed.Background;
            if (parsed.Foreground is not null && userControl.Foreground is null)
                userControl.Foreground = parsed.Foreground;
            if (parsed.BorderBrush is not null && userControl.BorderBrush is null)
                userControl.BorderBrush = parsed.BorderBrush;
            if (parsed.BorderThickness != default && userControl.BorderThickness == default)
                userControl.BorderThickness = parsed.BorderThickness;
            if (parsed.Padding != default && userControl.Padding == default)
                userControl.Padding = parsed.Padding;
            // Same generated-Connect equivalent as windows and pages.
            WireFields(component, loaded.Names, path);
            WireEvents(component, loaded, path);
            RegisterNameScope(component, loaded.Names);
            // Triggers parsed on the loader tree resolve against the detached
            // parsed root, so it shares the component namescope (same
            // element instances, moved into the component above).
            RegisterNameScope(loaded.Root, loaded.Names);
            userControl.RaiseLoadedTree();
            return;
        }
        if (component is Border border)
        {
            var loaded = ParseWindow(source, path);
            if (loaded.Root is not Border parsed)
                throw new XamlLoadException($"Root of '{path}' is {loaded.Root.GetType().Name}, not a Border.");
            if (loaded.RootClass is not null && !loaded.RootClass.Equals(component.GetType().FullName, StringComparison.Ordinal))
                throw new XamlLoadException(
                    $"x:Class '{loaded.RootClass}' does not match component '{component.GetType().FullName}'.");
            if (parsed.GetType() != component.GetType() &&
                !component.GetType().IsSubclassOf(parsed.GetType()))
                throw new XamlLoadException(
                    $"Component '{component.GetType().FullName}' is not a '{parsed.GetType().Name}' (from '{path}').");
            border.SetResources(parsed.Resources);
            if (parsed.HasLocalDataContext) border.DataContext = parsed.DataContext;
            border.Child = parsed.Child;
            // Root attributes belong to the instance (the parsed root is
            // detached): without these the control sizes to content instead
            // of its declared footprint.
            border.Width = parsed.Width;
            border.Height = parsed.Height;
            border.MinWidth = parsed.MinWidth;
            border.MinHeight = parsed.MinHeight;
            border.MaxWidth = parsed.MaxWidth;
            border.MaxHeight = parsed.MaxHeight;
            border.Margin = parsed.Margin;
            border.HorizontalAlignment = parsed.HorizontalAlignment;
            border.VerticalAlignment = parsed.VerticalAlignment;
            if (parsed.Background is not null) border.Background = parsed.Background;
            if (parsed.BorderBrush is not null) border.BorderBrush = parsed.BorderBrush;
            border.BorderThickness = parsed.BorderThickness;
            border.CornerRadius = parsed.CornerRadius;
            border.Padding = parsed.Padding;
            // Root attributes belong to the instance (the parsed root is
            // detached): a root Effect="{StaticResource ...}" lands on the
            // parsed stand-in at flush time, so Hydrate carries it over like
            // the other chrome (the staged LeftMainContent root reads
            // EffectShadow4; without this the view instantiates with a null
            // Effect even though the key resolves).
            if (parsed.Effect is not null) border.Effect = parsed.Effect;
            // Same generated-Connect equivalent as windows, pages and user controls.
            WireFields(component, loaded.Names, path);
            WireEvents(component, loaded, path);
            RegisterNameScope(component, loaded.Names);
            // Triggers parsed on the loader tree resolve against the detached
            // parsed root, so it shares the component namescope (same
            // element instances, moved into the component above).
            RegisterNameScope(loaded.Root, loaded.Names);
            border.RaiseLoadedTree();
            return;
        }
        throw new XamlLoadException(
            $"LoadComponent for '{component.GetType().FullName}' is not supported (Window/Page/Application/UserControl/Border only).");
    }

    /// <summary>
    /// Copies the inherited Control chrome the loader applied to the parsed
    /// root onto the converted component (implicit Window styles land on the
    /// parsed tree, which Hydrate otherwise discards; the component is what
    /// the host shows). Only non-default parsed values transfer over default
    /// component values, so codebehind-local values set before
    /// InitializeComponent keep WPF local-beats-style precedence.
    /// </summary>
    private static void CopyWindowChrome(Window window, Window parsed)
    {
        if (parsed.Background is not null && window.Background is null)
            window.Background = parsed.Background;
        if (parsed.Foreground is not null && window.Foreground is null)
            window.Foreground = parsed.Foreground;
        if (parsed.BorderBrush is not null && window.BorderBrush is null)
            window.BorderBrush = parsed.BorderBrush;
        if (parsed.BorderThickness != default && window.BorderThickness == default)
            window.BorderThickness = parsed.BorderThickness;
        if (parsed.Padding != default && window.Padding == default)
            window.Padding = parsed.Padding;
        if (window.FontFamily.Equals(new FontFamily("sans-serif")) && !parsed.FontFamily.Equals(new FontFamily("sans-serif")))
            window.FontFamily = parsed.FontFamily;
        if (window.FontSize == 14.0 && parsed.FontSize != 14.0)
            window.FontSize = parsed.FontSize;
        if (window.FontWeight.Equals(FontWeights.Normal) && !parsed.FontWeight.Equals(FontWeights.Normal))
            window.FontWeight = parsed.FontWeight;
        if (window.FontStyle.Equals(FontStyles.Normal) && !parsed.FontStyle.Equals(FontStyles.Normal))
            window.FontStyle = parsed.FontStyle;
        if (parsed.Template is not null && window.Template is null)
            window.Template = parsed.Template;
        if (!window.AllowsTransparency && parsed.AllowsTransparency)
            window.AllowsTransparency = true;
        if (window.ShowInTaskbar && !parsed.ShowInTaskbar)
            window.ShowInTaskbar = false;
        if (window.ShowActivated && !parsed.ShowActivated)
            window.ShowActivated = false;
        if (window.WindowStyle == WindowStyle.SingleBorderWindow
            && parsed.WindowStyle != WindowStyle.SingleBorderWindow)
            window.WindowStyle = parsed.WindowStyle;
        if (double.IsNaN(window.Left) && !double.IsNaN(parsed.Left))
            window.Left = parsed.Left;
        if (double.IsNaN(window.Top) && !double.IsNaN(parsed.Top))
            window.Top = parsed.Top;
    }


    /// <summary>
    /// Assigns x:Name/Name elements to the matching codebehind fields
    /// (what generated Connect does). Missing or mistyped fields are precise
    /// errors: silent nulls would crash converted handlers far from the cause.
    /// </summary>
    private static void WireFields(object component, IReadOnlyDictionary<string, object> names, string path)
    {
        var type = component.GetType();
        foreach (var (name, element) in names)
        {
            var field = FindInstanceField(type, name)
                ?? throw new XamlLoadException(
                    $"x:Name '{name}' (from '{path}') has no instance field '{type.FullName}.{name}'.");
            if (!field.FieldType.IsAssignableFrom(element.GetType()))
                throw new XamlLoadException(
                    $"x:Name '{name}' is {element.GetType().Name}, not assignable to '{type.FullName}.{name}' ({field.FieldType.Name}).");
            field.SetValue(component, element);
        }
    }

    private static System.Reflection.FieldInfo? FindInstanceField(Type type, string name)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var field = t.GetField(name,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly);
            if (field is not null) return field;
        }
        return null;
    }

    /// <summary>
    /// Binds recorded Click="Handler" attributes to codebehind methods with
    /// the routed signature (what generated Connect does).
    /// </summary>
    /// <summary>
    /// Component namescopes for TargetName-style resolution: keyed by the
    /// hydrated component (weak keys die with it), covering visual and 3D
    /// names the logical-tree walk cannot reach.
    /// </summary>
    internal static readonly ConditionalWeakTable<object, IReadOnlyDictionary<string, object>> NameScopes = new();

    private static void RegisterNameScope(object component, IReadOnlyDictionary<string, object> names)
    {
        NameScopes.Remove(component);
        NameScopes.Add(component, names);
        if (component is DependencyObject dobj)
        {
            var scope = new NameScope();
            foreach (var (k, v) in names)
            {
                if (scope.FindName(k) is null)
                    scope.RegisterName(k, v);
            }
            NameScope.SetNameScope(dobj, scope);
        }
    }

    private static void WireEvents(object component, XamlLoadResult loaded, string path)
    {
        var type = component.GetType();
        foreach (var wire in loaded.EventWires)
        {
            // Wires recorded against the parsed root belong to the component:
            // the root itself is discarded (only Content/Resources move over),
            // so root-targeted handlers re-attach to the component instance.
            var target = ReferenceEquals(wire.Element, loaded.Root) && component is UIElement
                ? component
                : wire.Element;

            // Real converted handlers take (object, RoutedEventArgs), but
            // framework samples also use (object, EventArgs) (e.g. Geometry's
            // FrameContentRendered); the latter binds through a forwarder.
            // EventHandler-typed events (e.g. Storyboard.Completed) only
            // accept (object, EventArgs) methods; anything else is precise.
            var flags = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;
            var wireEvent = target.GetType().GetEvent(wire.EventName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public);
            // Exact delegate-signature binding first (converted handlers
            // declare the event's own args, e.g. (object, KeyEventArgs) for
            // PreviewKeyDown): binds directly with no forwarder.
            if (wireEvent?.EventHandlerType is not null &&
                TryBindExactSignature(wireEvent.EventHandlerType, type, component, wire, target))
                continue;
            var method = type.GetMethod(wire.HandlerName, flags,
                null, [typeof(object), typeof(RoutedEventArgs)], null);
            var takesPlainEventArgs = false;
            if (method is null)
            {
                method = type.GetMethod(wire.HandlerName, flags,
                    null, [typeof(object), typeof(EventArgs)], null);
                takesPlainEventArgs = method is not null;
            }
            if (method is null)
                throw new XamlLoadException(
                    $"'{wire.HandlerName}' (from '{path}') is not a (object, RoutedEventArgs) or (object, EventArgs) method on '{type.FullName}'.");
            var evt = target.GetType().GetEvent(wire.EventName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public)
                ?? throw new XamlLoadException(
                    $"'{wire.EventName}' is not an event on <{target.GetType().Name}> (from '{path}').");
            if (takesPlainEventArgs && evt.EventHandlerType == typeof(RoutedEventHandler))
                evt.AddEventHandler(target,
                    new RoutedEventHandler((sender, e) => method.Invoke(component, [sender, e])));
            else if (takesPlainEventArgs && evt.EventHandlerType == typeof(EventHandler))
                evt.AddEventHandler(target,
                    new EventHandler((sender, e) => method.Invoke(component, [sender, e])));
            else if (!takesPlainEventArgs && evt.EventHandlerType == typeof(RoutedEventHandler))
                evt.AddEventHandler(target,
                    System.Delegate.CreateDelegate(typeof(RoutedEventHandler), component, method));
            else if (evt.EventHandlerType is not null && TryBindTypedHandler(evt.EventHandlerType, component, wire, target))
            {
                // Bound above (TextChangedEventHandler, MouseButtonEventHandler, ...).
            }
            else
                throw new XamlLoadException(
                    $"'{wire.EventName}' on <{target.GetType().Name}> uses {evt.EventHandlerType?.Name ?? "an unknown delegate"}, which v1 cannot bind (needs a (object, RoutedEventArgs)/(object, EventArgs) or matching (object, TEventArgs) method).");
        }
    }

    /// <summary>
    /// Binds (object, TEventArgs) component methods to matching CLR events
    /// (TextChangedEventHandler, MouseButtonEventHandler, ...). Returns false
    /// when the shapes do not line up.
    /// </summary>
    /// <summary>
    /// Binds a (object, TEventArgs) component method to the event's own
    /// delegate type (KeyEventHandler, MouseButtonEventHandler, ...).
    /// Returns false when the component declares no exact match (legacy
    /// RoutedEventArgs/EventArgs forwarders apply instead).
    /// </summary>
    private static bool TryBindExactSignature(Type handlerType, Type componentType, object component, XamlEventWire wire, object target)
    {
        var parameters = handlerType.GetMethod("Invoke")?.GetParameters();
        // Exact (object, TArgs) match like generated BAML Connect: plain
        // System.EventHandler<T> is unconstrained, so converted payloads need not
        // derive EventArgs (the PC Space Map treemap reports ScanNode directly).
        // CreateDelegate still validates compatibility, so near-matches stay unbound.
        if (parameters?.Length != 2 ||
            parameters[0].ParameterType != typeof(object))
            return false;
        var flags = System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;
        var method = componentType.GetMethod(wire.HandlerName, flags,
            null, [typeof(object), parameters[1].ParameterType], null);
        if (method is null) return false;
        try
        {
            target.GetType().GetEvent(wire.EventName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public)!
                .AddEventHandler(target, System.Delegate.CreateDelegate(handlerType, component, method, throwOnBindFailure: false)!);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryBindTypedHandler(Type handlerType, object component, XamlEventWire wire, object target)
    {
        var invoke = handlerType.GetMethod("Invoke");
        var parameters = invoke?.GetParameters();
        // Exact (object, TArgs) match like generated BAML Connect: plain
        // System.EventHandler<T> is unconstrained, so converted payloads need not
        // derive EventArgs (the PC Space Map treemap reports ScanNode directly).
        // CreateDelegate still validates compatibility, so near-matches stay unbound.
        if (parameters?.Length != 2 ||
            parameters[0].ParameterType != typeof(object))
            return false;
        var eventArgsType = parameters[1].ParameterType;
        var flags = System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;
        var method = component.GetType().GetMethod(wire.HandlerName, flags,
            null, [typeof(object), eventArgsType], null);
        if (method is null) return false;
        try
        {
            target.GetType().GetEvent(wire.EventName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public)!
                .AddEventHandler(target, System.Delegate.CreateDelegate(handlerType, component, method, throwOnBindFailure: false)!);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static XamlLoadResult ParseWindow(ComponentSource source, string path) =>
        source switch
        {
            LooseSource loose => XamlLoader.Load(loose.Text),
            BamlSource baml => CreateBamlLoadResult(ParseBaml(baml, path)),
            _ => throw new XamlLoadException($"Internal error: unknown component source for '{path}'."),
        };

    private static XamlLoadResult CreateBamlLoadResult(UIElement root)
    {
        var names = new Dictionary<string, object>(StringComparer.Ordinal);
        if (NameScope.GetNameScope(root) is System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, object>> scope)
        {
            foreach (var kvp in scope)
                names[kvp.Key] = kvp.Value;
        }
        return new XamlLoadResult(root, names);
    }

    private static UIElement ParseBaml(BamlSource baml, string path)
    {
        try
        {
            return BamlReader.LoadRoot(baml.Bytes);
        }
        catch (Exception ex) when (ex is not XamlLoadException)
        {
            throw new XamlLoadException($"Real BAML '{path}' is outside the v1 surface: {ex.Message}");
        }
    }

    private static void HydrateApplication(Application app, string xaml, string path)
    {
        // Same reader contract as the loader paths below: comments and
        // insignificant whitespace never reach the resource readers.
        using var reader = XmlReader.Create(new StringReader(xaml), new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Prohibit,
        });
        MoveToRoot(reader, path);
        if (reader.LocalName != "Application")
            throw new XamlLoadException($"Root of '{path}' is {reader.LocalName}, not Application.");
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns" || reader.Name == "xmlns" ||
                    reader.Prefix is "d" or "mc" or "presentationOptions" || reader.Name.StartsWith("xml:", StringComparison.Ordinal) ||
                    IsDesignTimeNamespace(reader.NamespaceURI))
                    continue;
                if (reader.Prefix == "x" && reader.LocalName == "Class") continue;
                if (reader.Prefix == string.Empty && reader.LocalName == "StartupUri")
                {
                    app.StartupUri = new Uri(reader.Value, UriKind.RelativeOrAbsolute);
                    continue;
                }
                if (reader.Prefix == string.Empty && reader.LocalName == "ShutdownMode")
                {
                    if (!Enum.TryParse<ShutdownMode>(reader.Value, out var mode))
                        throw new XamlLoadException(
                            $"Application root '{path}' has unsupported ShutdownMode '{reader.Value}' " +
                            "(want OnLastWindowClose, OnMainWindowClose or OnExplicitShutdown).");
                    app.ShutdownMode = mode;
                    continue;
                }
                throw new XamlLoadException(
                    $"Application root attribute '{reader.Name}' is not supported (StartupUri and ShutdownMode only).");
            }
            reader.MoveToElement();
        }
        if (reader.IsEmptyElement) return;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.LocalName == "Application.Resources")
                {
                    XamlLoader.HydrateApplicationResources(app, reader);
                    continue;
                }
                throw new XamlLoadException(
                    $"Application root child '<{reader.LocalName}>' is not supported (Application.Resources only).");
            }
            if (reader.NodeType == XmlNodeType.EndElement) break;
        }
    }

    /// <summary>
    /// Whether a XAML namespace URI is design-time only (the markup-compatibility
    /// namespace Visual Studio binds to generated prefixes like d1P1, or the blend
    /// design namespace): real WPF honors mc:Ignorable by namespace, never by prefix.
    /// </summary>
    private static bool IsDesignTimeNamespace(string namespaceUri) =>
        string.Equals(namespaceUri, "http://schemas.openxmlformats.org/markup-compatibility/2006", StringComparison.Ordinal) ||
        string.Equals(namespaceUri, "http://schemas.microsoft.com/expression/blend/2008", StringComparison.Ordinal);

    private static void MoveToRoot(XmlReader reader, string path)
    {
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element) return;
            if (reader.NodeType is XmlNodeType.XmlDeclaration or XmlNodeType.Comment or XmlNodeType.Whitespace)
                continue;
            throw new XamlLoadException($"Unexpected node before the root of '{path}'.");
        }
        throw new XamlLoadException($"No root element in '{path}'.");
    }

    private static ComponentSource? ResolveSource(string path, Assembly? preferred)
    {
        foreach (var asm in CandidateAssemblies(preferred))
        {
            string[] names;
            try
            {
                names = asm.GetManifestResourceNames();
            }
            catch
            {
                continue;
            }
            foreach (var name in names)
            {
                if (!ResourceNameMatches(name, path)) continue;
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is null) continue;
                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd();
                return new LooseSource(text, PeekRootClass(text));
            }
            // Real compiled BAML inside .g.resources (original build output).
            foreach (var container in names.Where(n => n.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase)))
            {
                var bytes = ReadBamlEntry(asm, container, System.IO.Path.ChangeExtension(path, ".baml")!);
                if (bytes is not null) return new BamlSource(bytes);
            }
        }
        return null;
    }

    /// <summary>
    /// Finds image bytes for a BitmapImage UriSource: loose manifest resources
    /// plus image entries inside .g.resources (where real WPF packs them).
    /// Null when absent (the image stays retained-only).
    /// </summary>
    internal static byte[]? FindPackBytes(string path, string? assemblyName)
    {
        foreach (var asm in CandidateAssemblies(assemblyName is null ? null : PreferredAssembly(assemblyName)))
        {
            string[] names;
            try
            {
                names = asm.GetManifestResourceNames();
            }
            catch
            {
                continue;
            }
            foreach (var name in names)
            {
                if (!ResourceNameMatches(name, path)) continue;
                if (name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using var stream = asm.GetManifestResourceStream(name);
                    if (stream is null) continue;
                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    if (copy.Length > 0) return copy.ToArray();
                }
                catch
                {
                    // Unreadable entries are skipped; absence returns null below.
                }
            }
            foreach (var container in names.Where(n => n.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase)))
            {
                var bytes = ReadResourceBytes(asm, container, path);
                if (bytes is not null) return bytes;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds embedded loose-XAML text for a Source= dictionary path: manifest
    /// resources whose names match the pack path (what the converter embeds
    /// for every loose .xaml) decoded as UTF-8. Compiled BAML stays a precise
    /// boundary (see <see cref="XamlLoader.LoadComponent{T}(Stream)"/>).
    /// Null when absent. When <paramref name="assemblyName"/> is set, only
    /// that assembly is consulted (assembly-qualified pack URIs).
    /// </summary>
    internal static string? FindPackText(string path, string? assemblyName)
    {
        var preferred = assemblyName is null ? null : PreferredAssembly(assemblyName);
        if (assemblyName is not null && preferred is null) return null;
        foreach (var asm in CandidateAssemblies(preferred))
        {
            if (preferred is not null && !ReferenceEquals(asm, preferred)) continue;
            string[] names;
            try
            {
                names = asm.GetManifestResourceNames();
            }
            catch
            {
                continue;
            }
            foreach (var name in names)
            {
                if (name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase)) continue;
                if (!ResourceNameMatches(name, path)) continue;
                try
                {
                    using var stream = asm.GetManifestResourceStream(name);
                    if (stream is null) continue;
                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    if (copy.Length == 0 || copy.Length > 4 * 1024 * 1024) continue;
                    // Loose XAML files commonly carry a UTF-8 BOM (U+FEFF),
                    // which TrimStart leaves behind: strip it before sniffing.
                    var text = System.Text.Encoding.UTF8.GetString(copy.ToArray()).TrimStart('\uFEFF');
                    if (!text.TrimStart().StartsWith('<')) continue;
                    return text;
                }
                catch
                {
                    // Unreadable entries are skipped; absence returns null below.
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Loads a staged package image by assembly name for pack lookups
    /// (set by <see cref="ApplicationPackageLoader"/> while it owns the
    /// package; cleared on dispose). Pack Sources routinely name assemblies
    /// nothing has referenced yet (App.xaml merges a vendor theme before any
    /// of its types load), so without this the lookup reports staged images
    /// absent. Laziness is preserved: only the named image loads.
    /// </summary>
    internal static Func<string, Assembly?>? PackAssemblyResolver { get; set; }

    internal static Assembly? PreferredAssembly(string assemblyName)
    {
        try
        {
            // A package may carry a newer converted image with the same
            // simple name as an earlier session. Its verified bytes own its
            // pack resources; process load order must not choose the source.
            return PackAssemblyResolver?.Invoke(assemblyName)
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? ReadResourceBytes(Assembly asm, string container, string path)
    {
        try
        {
            using var stream = asm.GetManifestResourceStream(container);
            if (stream is null) return null;
            using var reader = new ResourceReader(stream);
            foreach (DictionaryEntry entry in reader)
            {
                if (entry.Key is not string key || !ResourceNameMatches(key, path)) continue;
                if (entry.Value is byte[] bytes) return bytes;
                if (entry.Value is Stream s)
                {
                    using var copy = new MemoryStream();
                    s.CopyTo(copy);
                    return copy.ToArray();
                }
                if (entry.Value is UnmanagedMemoryStream ums)
                {
                    using var copy = new MemoryStream();
                    ums.CopyTo(copy);
                    return copy.ToArray();
                }
            }
        }
        catch
        {
            // Unreadable containers are skipped; the caller keeps looking.
        }
        return null;
    }

    private static byte[]? ReadBamlEntry(Assembly asm, string container, string bamlPath)
    {
        try
        {
            using var stream = asm.GetManifestResourceStream(container);
            if (stream is null) return null;
            using var reader = new ResourceReader(stream);
            foreach (DictionaryEntry entry in reader)
            {
                if (entry.Key is not string key || !ResourceNameMatches(key, bamlPath)) continue;
                if (entry.Value is byte[] bytes) return bytes;
                if (entry.Value is Stream s)
                {
                    using var copy = new MemoryStream();
                    s.CopyTo(copy);
                    return copy.ToArray();
                }
            }
        }
        catch
        {
            // Unreadable containers are skipped; the final error names the path.
        }
        return null;
    }

    /// <summary>
    /// Manifest names use dots (DesktopWasm...MainWindow.xaml), pack paths
    /// use slashes (mainwindow.xaml): treat '.', '/' and '\' as equivalent
    /// separators so either spelling finds the other.
    /// </summary>
    private static bool ResourceNameMatches(string resourceName, string path)
    {
        var candidate = NormalizeKey(resourceName);
        var wanted = NormalizeKey(path);
        return candidate.Equals(wanted) || candidate.EndsWith("/" + wanted, StringComparison.Ordinal);
    }

    private static string NormalizeKey(string s)
    {
        var lower = s.ToLowerInvariant();
        var parts = lower.Split(['.', '/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join("/", parts);
    }

    private static IEnumerable<Assembly> CandidateAssemblies(Assembly? preferred)
    {
        var seen = new HashSet<Assembly>();
        var ordered = new List<Assembly?> { preferred, XamlLoader.CustomElementSource, Application.Current?.GetType().Assembly, GetCalling() };
        ordered.AddRange(AppDomain.CurrentDomain.GetAssemblies());
        foreach (var asm in ordered)
        {
            if (asm is null || asm.IsDynamic || !seen.Add(asm)) continue;
            yield return asm;
        }
    }

    private static Assembly? GetCalling()
    {
        try
        {
            return Assembly.GetCallingAssembly();
        }
        catch
        {
            return null;
        }
    }

    private static string? PeekRootClass(string xaml)
    {
        try
        {
            using var xml = XmlReader.Create(new StringReader(xaml), new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                DtdProcessing = DtdProcessing.Prohibit,
            });
            while (xml.Read())
            {
                if (xml.NodeType != XmlNodeType.Element) continue;
                return xml.GetAttribute("Class", XamlNamespace);
            }
        }
        catch
        {
            // Malformed roots surface from the real parse; no class here.
        }
        return null;
    }

    private static Type? SafeGetType(Assembly asm, string name)
    {
        try
        {
            return asm.GetType(name, throwOnError: false);
        }
        catch
        {
            return null;
        }
    }

    private static readonly ConditionalWeakTable<Assembly, Type[]> CompleteTypeLists = new();

    internal static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try
        {
            // Converted themes repeatedly resolve owners during inflation.
            // Complete immutable type lists can be reused. Partial load
            // failures and growing Reflection.Emit assemblies cannot.
            return asm.IsDynamic ? asm.GetTypes() : CompleteTypeLists.GetValue(asm, static image => image.GetTypes());
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
        catch
        {
            return Enumerable.Empty<Type>();
        }
    }
    /// <summary>
    /// Finds a converted type by full name across loaded assemblies (style
    /// triggers canonicalize vendor prefixes at parse; the owner dictionary
    /// is loaded by apply time). Null when absent; never throws.
    /// </summary>
    internal static Type? FindConvertedTypeByFullName(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? candidate;
            try { candidate = assembly.GetType(fullName); }
            catch { candidate = null; }
            if (candidate is not null) return candidate;
        }
        return null;
    }

    private abstract record ComponentSource;
    private sealed record LooseSource(string Text, string? RootClass) : ComponentSource;
    private sealed record BamlSource(byte[] Bytes) : ComponentSource;
}
