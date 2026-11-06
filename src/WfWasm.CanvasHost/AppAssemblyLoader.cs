using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Runtime.InteropServices.JavaScript;
using Microsoft.JSInterop;
using WfWasm.Drawing;

namespace WfWasm.CanvasHost;

public sealed class AppLaunchException : InvalidOperationException
{
    public string App { get; }
    public AppLaunchException(string app, string message, Exception innerException)
        : base(message, innerException)
    {
        App = app;
    }
}

public sealed record PrefetchTelemetrySnapshot(
    int TotalPrefetches,
    int SuccessfulPrefetches,
    int FailedPrefetches,
    int CacheHits,
    int CacheMisses,
    long TotalBytesPrefetched,
    double TotalLatencyMs,
    double AverageLatencyMs,
    double MinLatencyMs,
    double MaxLatencyMs,
    double CacheHitRate)
{
    public static PrefetchTelemetrySnapshot Empty => new(0, 0, 0, 0, 0, 0, 0.0, 0.0, 0.0, 0.0, 0.0);
}

public sealed class PrefetchTelemetryTracker
{
    private readonly object _lock = new();
    private int _totalPrefetches;
    private int _successfulPrefetches;
    private int _failedPrefetches;
    private int _cacheHits;
    private int _cacheMisses;
    private long _totalBytes;
    private double _totalLatencyMs;
    private double _minLatencyMs = double.MaxValue;
    private double _maxLatencyMs = 0.0;

    public void RecordTiming(double elapsedMs, long bytes, bool success)
    {
        lock (_lock)
        {
            _totalPrefetches++;
            if (success)
            {
                _successfulPrefetches++;
                _totalBytes += bytes;
                _totalLatencyMs += elapsedMs;
                if (elapsedMs < _minLatencyMs) _minLatencyMs = elapsedMs;
                if (elapsedMs > _maxLatencyMs) _maxLatencyMs = elapsedMs;
            }
            else
            {
                _failedPrefetches++;
            }
        }
    }

    public void RecordHit(bool hit)
    {
        lock (_lock)
        {
            if (hit) _cacheHits++;
            else _cacheMisses++;
        }
    }

    public PrefetchTelemetrySnapshot GetSnapshot()
    {
        lock (_lock)
        {
            int totalLookups = _cacheHits + _cacheMisses;
            double hitRate = totalLookups > 0 ? (double)_cacheHits / totalLookups : 0.0;
            double avgLatency = _successfulPrefetches > 0 ? _totalLatencyMs / _successfulPrefetches : 0.0;
            double min = _successfulPrefetches > 0 && _minLatencyMs != double.MaxValue ? _minLatencyMs : 0.0;
            return new PrefetchTelemetrySnapshot(
                _totalPrefetches,
                _successfulPrefetches,
                _failedPrefetches,
                _cacheHits,
                _cacheMisses,
                _totalBytes,
                Math.Round(_totalLatencyMs, 3),
                Math.Round(avgLatency, 3),
                Math.Round(min, 3),
                Math.Round(_maxLatencyMs, 3),
                Math.Round(hitRate, 4));
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _totalPrefetches = 0;
            _successfulPrefetches = 0;
            _failedPrefetches = 0;
            _cacheHits = 0;
            _cacheMisses = 0;
            _totalBytes = 0;
            _totalLatencyMs = 0.0;
            _minLatencyMs = double.MaxValue;
            _maxLatencyMs = 0.0;
        }
    }
}

public static partial class AppAssemblyLoader
{
    internal static bool StaticHosting { get; set; }
    private sealed record ManifestFile(string name, string sha256, string url);
    private sealed record ManifestContent(string name, string url, string? sha256 = null);
    private sealed record ManifestDefaultFont(string name, float size);
    private sealed record AppManifest(string entry, List<ManifestFile> files, List<ManifestContent>? content, ManifestDefaultFont? defaultFont = null, List<ManifestPrefetch>? prefetch = null);
    private sealed record ManifestPrefetch(string name, string url, long size, string sha256);
    private sealed record ContentItem(string Name, byte[]? Bytes, AppFileSystem.DecodedImage? Image, long SourceSize)
    {
        public long Size => Bytes?.LongLength ?? Image?.Rgba.LongLength ?? 0;
    }
    private sealed record ContentSet(List<ContentItem> Items, long Size);
    private static readonly Dictionary<string, byte[]> Bytes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Task<(Assembly Assembly, AppManifest Manifest)>> Apps = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> Fetches = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Assembly> LoadedByName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> LastAppError = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, List<string>> AppResolutionFailures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ContentSet> Content = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> ContentLru = new();
    private const long ContentBudget = 128 * 1024 * 1024;
    private static readonly object Gate = new();
    private static bool _hooked;
    private static string? _activeApp;
    private static readonly Dictionary<string, HashSet<string>> UsedByApp = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Resolving = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Missing = new(StringComparer.OrdinalIgnoreCase);
    [JSImport("globalThis.wfwasmFetchAssembly")]
    private static partial byte[] FetchRequestedAssembly(string app, string name);

    // RV2944-shell demand-storm seam: the synchronous demand fetch behind an
    // injectable delegate so the storm path is measurable off-browser.
    // Production always uses the JSImport above (see reset helper).
    internal static Func<string, string, byte[]?> DemandFetch = (app, name) => FetchRequestedAssembly(app, name);

    internal static void ResetDemandFetchForTest() =>
        DemandFetch = (app, name) => FetchRequestedAssembly(app, name);

    // The CLR probes "<base>.XmlSerializers" for every XmlSerializer use.
    // These pre-generated serializer assemblies are optional: absence falls
    // back to runtime serialization, and none of the shipped apps carry one.
    internal static bool IsKnownOptionalProbe(string name) =>
        name.EndsWith(".XmlSerializers", StringComparison.OrdinalIgnoreCase);

    internal static byte[]? ResolveDemandBytes(string app, string name)
    {
        // Short-circuit BEFORE any synchronous demand fetch: the outcome is
        // identical to the 204 miss the fetch would return (null, with the
        // caller recording Missing), minus the main-thread storm wait.
        if (IsKnownOptionalProbe(name)) return null;
        return DemandAssemblyPayload.Decode(DemandFetch(app, name));
    }

    // Names the runtime XmlSerializer probe chain will ask for once boot
    // reaches form code. Never present in PE references, so the parallel
    // prefetch closure must name them explicitly (miss-safe downstream).
    internal static List<string> OptionalProbeCandidates(string entry, IEnumerable<string> directRefs)
    {
        var result = new List<string> { entry + ".XmlSerializers" };
        foreach (string r in directRefs)
        {
            string candidate = r + ".XmlSerializers";
            if (!result.Contains(candidate, StringComparer.OrdinalIgnoreCase)) result.Add(candidate);
        }
        return result;
    }

    public static string? GetAppLoadError(string app) => LastAppError.TryGetValue(app, out string? err) ? err : null;

    public static IReadOnlyList<string> GetResolutionFailures(string app)
    {
        lock (Gate)
        {
            return AppResolutionFailures.TryGetValue(app, out var list) ? list.ToArray() : Array.Empty<string>();
        }
    }

    public static void ClearAppErrors(string app)
    {
        LastAppError.TryRemove(app, out _);
        lock (Gate)
        {
            AppResolutionFailures.TryRemove(app, out _);
        }
    }

    private static readonly PrefetchTelemetryTracker GlobalTelemetry = new();
    private static readonly ConcurrentDictionary<string, PrefetchTelemetryTracker> AppTelemetry = new(StringComparer.OrdinalIgnoreCase);

    public static PrefetchTelemetrySnapshot GetPrefetchTelemetry(string? app = null)
    {
        if (!string.IsNullOrEmpty(app) && AppTelemetry.TryGetValue(app, out var tracker))
            return tracker.GetSnapshot();
        return GlobalTelemetry.GetSnapshot();
    }

    public static void ResetPrefetchTelemetry(string? app = null)
    {
        if (string.IsNullOrEmpty(app))
        {
            GlobalTelemetry.Reset();
            foreach (var tracker in AppTelemetry.Values) tracker.Reset();
            AppTelemetry.Clear();
        }
        else if (AppTelemetry.TryGetValue(app, out var tracker))
        {
            tracker.Reset();
        }
    }

    internal static void RecordPrefetchTiming(string? app, double elapsedMs, long bytes, bool success)
    {
        GlobalTelemetry.RecordTiming(elapsedMs, bytes, success);
        if (!string.IsNullOrEmpty(app))
            AppTelemetry.GetOrAdd(app, _ => new PrefetchTelemetryTracker()).RecordTiming(elapsedMs, bytes, success);
    }

    internal static void RecordResolutionHit(string? app, bool hit)
    {
        GlobalTelemetry.RecordHit(hit);
        if (!string.IsNullOrEmpty(app))
            AppTelemetry.GetOrAdd(app, _ => new PrefetchTelemetryTracker()).RecordHit(hit);
    }

    internal static void RecordPrefetchTimingForTest(string app, double elapsedMs, long bytes, bool success)
        => RecordPrefetchTiming(app, elapsedMs, bytes, success);

    internal static void RecordResolutionHitForTest(string app, bool hit)
        => RecordResolutionHit(app, hit);

    internal static void RecordResolutionFailure(string? app, string name, Exception ex)
    {
        if (app is not null)
        {
            lock (Gate)
            {
                Missing.Add(app + "/" + name);
                if (!AppResolutionFailures.TryGetValue(app, out var list))
                    AppResolutionFailures[app] = list = new List<string>();
                list.Add(name);
            }
            LastAppError[app] = $"The application '{app}' is missing dependency '{name}'.";
            try { BrowserErrorReporter.Report("assembly-resolve", ex); } catch { }
        }
    }

    internal static void RecordResolutionFailureForTest(string app, string name, Exception ex)
        => RecordResolutionFailure(app, name, ex);

    public static string FormatUserFacingLaunchError(string app, Exception ex)
    {
        Exception root = ex.GetBaseException();
        string rootMsg = root.Message ?? "";

        if (root is HttpRequestException || rootMsg.Contains("404") || rootMsg.Contains("Response status code"))
            return $"The application assets for '{app}' could not be downloaded from the server.";

        if (root is InvalidDataException || rootMsg.Contains("integrity check failed"))
            return $"An integrity check failed for one of the application files in '{app}'.";

        if (root is PlatformNotSupportedException || rootMsg.Contains("PlatformNotSupportedException"))
            return "This app needs Windows-only features that aren't available in the browser.";

        if (root is FileNotFoundException || root is TypeLoadException || rootMsg.Contains("Could not load file or assembly"))
            return $"The application '{app}' is missing a required dependency.";

        if (root is InvalidOperationException && rootMsg.StartsWith("No manifest for app", StringComparison.OrdinalIgnoreCase))
            return $"The application manifest for '{app}' is not available.";

        return $"Could not start '{app}'. Technical detail is in App errors.";
    }

    internal static string Key(string name)
    {
        string leaf = Path.GetFileName(name.Replace('\\', '/'));
        return leaf.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || leaf.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? leaf[..^4] : leaf;
    }

    internal static string ExtractSimpleName(string fullName)
    {
        if (string.IsNullOrEmpty(fullName)) return fullName;
        int comma = fullName.IndexOf(',');
        return comma >= 0 ? fullName[..comma].Trim() : fullName.Trim();
    }

    internal static bool VerifySha256Hex(ReadOnlySpan<byte> data, string? expectedHex)
    {
        if (string.IsNullOrEmpty(expectedHex) || expectedHex.Length != 64) return false;
        Span<byte> actualSha = stackalloc byte[32];
        SHA256.HashData(data, actualSha);
        Span<byte> expectedSha = stackalloc byte[32];
        for (int i = 0; i < 32; i++)
        {
            int hi = HexVal(expectedHex[i * 2]);
            int lo = HexVal(expectedHex[i * 2 + 1]);
            if ((hi | lo) < 0) return false;
            expectedSha[i] = (byte)((hi << 4) | lo);
        }
        return CryptographicOperations.FixedTimeEquals(actualSha, expectedSha);

        static int HexVal(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1
        };
    }

    internal static List<string> ReadPeAssemblyReferences(byte[] peBytes)
    {
        var result = new List<string>();
        try
        {
            using var stream = new MemoryStream(peBytes, writable: false);
            using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
            if (!pe.HasMetadata) return result;
            var reader = pe.GetMetadataReader();
            foreach (var handle in reader.AssemblyReferences)
            {
                var ar = reader.GetAssemblyReference(handle);
                string refName = reader.GetString(ar.Name);
                if (!string.IsNullOrEmpty(refName))
                    result.Add(refName);
            }
        }
        catch { }
        return result;
    }

    private static Assembly? FindLoadedAssembly(string name)
    {
        if (LoadedByName.TryGetValue(name, out var asm)) return asm;
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            var a = assemblies[i];
            string? n = a.GetName().Name;
            if (n is not null)
            {
                LoadedByName.TryAdd(n, a);
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                    asm = a;
            }
        }
        return asm;
    }

    private static void HookResolving()
    {
        lock (Gate)
        {
            if (_hooked) return;
            _hooked = true;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveRequestedAssembly;
        }
    }

    // Rewrite-target scopes unify with the bundled fork: rewritten
    // assemblies reference the fork by name, but Verbatim assemblies
    // (System.Windows.Forms.Legacy, WPF) keep framework scopes. Binding
    // those to the already-loaded fork/drawing avoids a duplicate load
    // of the same types (split-brain) and a pointless multi-MB fetch
    // (a rewritten System.Windows.Forms is 7MB of desktop surface the
    // browser must never bind).
    private static string? WellKnownForkAssembly(string name) => name.ToLowerInvariant() switch
    {
        "system.windows.forms" => "WfWasm.WinForms",
        "system.windows.forms.primitives" => "WfWasm.WinForms",
        "system.drawing" => "WfWasm.Drawing",
        "system.drawing.common" => "WfWasm.Drawing",
        "system.drawing.primitives" => "WfWasm.Drawing",
        _ => null,
    };

    private static Assembly? ResolveRequestedAssembly(object? sender, ResolveEventArgs args)
    {
        string name = ExtractSimpleName(args.Name);
        /* resolve probe removed (misses still log "treating as missing") */
        if (string.IsNullOrEmpty(name)) return null;
        string? app;
        byte[]? bytes;
        lock (Gate)
        {
            app = _activeApp;
            if (!Resolving.Add(name)) return null;
            Bytes.TryGetValue(name, out bytes);
        }
        RecordResolutionHit(app, bytes is not null);
        try
        {
            if (bytes is null)
            {
                // One copy per name, version-agnostic: netfx v4 refs
                // (Telerik UIA -> WindowsBase v4) must bind the bundled
                // v9, never a second fetch. Without this, twin loads of
                // one framework assembly split type identity (Rect !=
                // Rect) and interface maps fail ("get_BoundingRectangle
                // ... does not have an implementation"). The hook only
                // fires when default binding failed, so returning a
                // loaded same-name assembly can only unify, and the
                // on-demand model is name-keyed anyway (no side-by-side).
                string localName = WellKnownForkAssembly(name) ?? name;
                var loaded = FindLoadedAssembly(localName);
                if (loaded is not null)
                {
                    if (app is not null) NoteUsed(app, name);
                    return loaded;
                }
                if (app is null || !OperatingSystem.IsBrowser() || Missing.Contains(app + "/" + name)) return null;
                // AssemblyResolve is synchronous. Fetch only the name the CLR
                // requested, then supply its verified bytes before returning.
                // Known-optional probes short-circuit inside (same miss as a
                // 204, without the main-thread storm wait).
                bytes = ResolveDemandBytes(app, name);
                if (bytes is null) { Missing.Add(app + "/" + name); return null; }
                lock (Gate) Bytes[name] = bytes;
                Console.WriteLine($"[AppAssemblyLoader] Requested {app}/{name} ({bytes.Length} bytes) at runtime.");
            }
            if (app is not null) NoteUsed(app, name);
            try
            {
                var loadedAsm = Assembly.Load(bytes);
                LoadedByName[name] = loadedAsm;
                try { Console.WriteLine($"[AppAssemblyLoader] Loaded {app}/{name}."); } catch { }
                /* load beacon removed (endpoint ignored the flag; saved a manifest regen per assembly) */
                return loadedAsm;
            }
            catch (Exception ex)
            {
                // An exception out of an AssemblyResolve handler kills the
                // Mono runtime (exit 1): PDN's WPF AssemblyHelper.OnLoaded
                // throws NIE on ReflectionOnly during Load. Convert the
                // throwing load into a catchable miss (null) instead of
                // propagating; the request site sees FileNotFound.
                RecordResolutionFailure(app, name, ex);
                Console.WriteLine($"[AppAssemblyLoader] Load of {app}/{name} threw {ex.GetType().Name}, treating as missing.");
                return null;
            }
        }
        catch (Exception ex)
        {
            // Fail closed for the whole handler, not just the load: the
            // synchronous demand fetch above runs outside the inner try,
            // and anything it throws would otherwise escape the handler
            // and kill the runtime (exit 1). The request site sees a miss.
            RecordResolutionFailure(app, name, ex);
            try { Console.WriteLine($"[AppAssemblyLoader] Resolve of {app}/{name} failed: {ex.GetType().Name}, treating as missing."); } catch { }
            return null;
        }
        finally { lock (Gate) Resolving.Remove(name); }
    }

    private static readonly HashSet<string> FontApplied = new(StringComparer.OrdinalIgnoreCase);

    private static void ApplyDefaultFont(string app, ManifestDefaultFont? font)
    {
        if (font is null) return;
        lock (Gate)
        {
            if (!FontApplied.Add(app)) return;
        }
        try
        {
            global::System.Windows.Forms.Application.SetDefaultFont(new global::System.Drawing.Font(font.name, font.size));
        }
        catch (InvalidOperationException)
        {
            // A window already exists (app switch): the first app's font sticks.
            Console.WriteLine($"[AppAssemblyLoader] Default font for {app} skipped (windows exist).");
        }
    }

    private static void NoteUsed(string app, string name)
    {
        lock (Gate)
        {
            if (!UsedByApp.TryGetValue(app, out var used)) UsedByApp[app] = used = new(StringComparer.OrdinalIgnoreCase);
            used.Add(name);
        }
    }

    public static async Task<Assembly> EnsureAppAsync(string app, HttpClient http, IJSRuntime? js = null)
    {
        HookResolving();
        ClearAppErrors(app);
        Task<(Assembly Assembly, AppManifest Manifest)> task;
        bool reused;
        lock (Gate)
        {
            _activeApp = app;
            reused = Apps.TryGetValue(app, out task!);
            if (!reused) Apps[app] = task = LoadAppAsync(app, http, js);
        }
        (Assembly Assembly, AppManifest Manifest) loaded;
        try { loaded = await task; }
        catch (Exception ex)
        {
            // Faults must not poison a session: the next selection can retry.
            lock (Gate)
                if (Apps.TryGetValue(app, out var current) && ReferenceEquals(current, task)) Apps.Remove(app);
            string userFacing = FormatUserFacingLaunchError(app, ex);
            LastAppError[app] = userFacing;
            BrowserErrorReporter.Report("app-load", ex);
            throw new AppLaunchException(app, userFacing, ex);
        }
        if (reused)
        {
            string[] used;
            lock (Gate) used = UsedByApp.TryGetValue(app, out var names) ? names.ToArray() : [];
            foreach (string name in used)
            {
                byte[]? bytes;
                lock (Gate) Bytes.TryGetValue(name, out bytes);
                if (bytes is not null) await ReportAsync(js, bytes.Length, true);
            }
        }
        await RestoreContentAsync(app, http, js, loaded.Manifest.content);
        ApplyDefaultFont(app, loaded.Manifest.defaultFont);
        System.Windows.Forms.WasmAppIdentity.EntryFileName = loaded.Manifest.entry + ".exe";
        return loaded.Assembly;
    }

    private static async Task<(Assembly, AppManifest)> LoadAppAsync(string app, HttpClient http, IJSRuntime? js)
    {
        string manifestUrl = StaticHosting
            ? "apps/" + Uri.EscapeDataString(app) + "/app-manifest.json"
            : "/api/appmanifest?app=" + Uri.EscapeDataString(app);
        var manifest = await http.GetFromJsonAsync<AppManifest>(manifestUrl)
            ?? throw new InvalidOperationException("No manifest for app '" + app + "'.");
        await PhaseAsync(js, "Downloading app entry assembly");
        // Even a legacy manifest containing a folder/catalog cannot cause a
        // preload: only the entry is fetched here. Everything else is requested
        // by ResolveRequestedAssembly when the CLR actually needs that name.
        var file = manifest.files.SingleOrDefault(f => Key(f.name).Equals(manifest.entry, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Manifest has no entry assembly for '" + app + "'.");
        var lazy = new Lazy<Task<byte[]>>(() => FetchVerifiedAsync(http, file));
        var fetch = Fetches.GetOrAdd(file.sha256, lazy);
        byte[] entryBytes;
        try { entryBytes = await fetch.Value; }
        catch { Fetches.TryRemove(new KeyValuePair<string, Lazy<Task<byte[]>>>(file.sha256, fetch)); throw; }
        lock (Gate) Bytes[Key(file.name)] = entryBytes;
        NoteUsed(app, Key(file.name));
        await ReportAsync(js, entryBytes.Length, !ReferenceEquals(fetch, lazy));
        if (StaticHosting)
        {
            // A static host has no assembly-resolution endpoint. Package the
            // complete closure and verify it before the entry can request it.
            foreach (var dependency in manifest.files.Where(f => !f.name.Equals(file.name, StringComparison.OrdinalIgnoreCase)))
            {
                byte[] bytes = await FetchVerifiedAsync(http, dependency);
                lock (Gate) Bytes[Key(dependency.name)] = bytes;
                await ReportAsync(js, bytes.Length, false);
            }
            var staticEntry = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                string.Equals(a.GetName().Name, manifest.entry, StringComparison.OrdinalIgnoreCase))
                ?? Assembly.Load(entryBytes);
            return (staticEntry, manifest);
        }
        // One async, verified bootstrap lane. The previous JS warmup and
        // managed warmup used different URLs for the same modules, then sync
        // resolution downloaded them again while the UI thread was blocked.
        // Store only bytes here; assembly activation order stays demand-driven.
        var loadedNames = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool paint = app.Equals("paintnet", StringComparison.OrdinalIgnoreCase);
        var bootstrap = (manifest.prefetch ?? []).Where(f =>
        {
            string name = Key(f.name);
            if (loadedNames.Contains(WellKnownForkAssembly(name) ?? name)) return false;
            if (f.name.Replace('\\', '/').Contains('/')) return false; // optional plugins stay on demand
            return paint || name.StartsWith("UIAutomation", StringComparison.OrdinalIgnoreCase)
                || name.Equals("System.ComponentModel.Composition", StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        using var bootstrapSlots = new SemaphoreSlim(6);
        await Task.WhenAll(bootstrap.Select(async f =>
        {
            await bootstrapSlots.WaitAsync();
            try { await PrefetchOneAsync(http, f); }
            catch { } // synchronous verified resolution remains the fallback
            finally { bootstrapSlots.Release(); }
        }));
        var entry = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
            string.Equals(a.GetName().Name, manifest.entry, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            entry = Assembly.Load(entryBytes);
        }
        // Direct entry references cover vendor UI libraries absent from legacy
        // manifests. Fetch them concurrently before any constructor can issue
        // synchronous demand calls. Loaded framework/fork assemblies are skipped.
        var direct = entry.GetReferencedAssemblies().Where(a => a.Name is not null)
            .Select(a => a.Name!).Where(n => !loadedNames.Contains(WellKnownForkAssembly(n) ?? n))
            .Where(n => !bootstrap.Any(f => Key(f.name).Equals(n, StringComparison.OrdinalIgnoreCase)))
            .Select(n => new ManifestPrefetch(n, "/api/appresolve?app=" + Uri.EscapeDataString(app)
                + "&name=" + Uri.EscapeDataString(n), 0, "")).ToArray();
        await Task.WhenAll(direct.Select(async f =>
        {
            await bootstrapSlots.WaitAsync();
            try { await PrefetchOneAsync(http, f); }
            catch { }
            finally { bootstrapSlots.Release(); }
        }));
        // Secondary / transitive prefetch: inspect PE references of fetched direct
        // assemblies to warm vendor UI dependencies (e.g. TelerikCommon, Documents)
        // before constructor execution hits synchronous demand calls.
        var secondaryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in direct)
        {
            byte[]? directBytes;
            lock (Gate) Bytes.TryGetValue(Key(d.name), out directBytes);
            if (directBytes is not null)
            {
                foreach (string r in ReadPeAssemblyReferences(directBytes))
                {
                    string k = Key(r);
                    if (!loadedNames.Contains(WellKnownForkAssembly(k) ?? k)
                        && !bootstrap.Any(f => Key(f.name).Equals(k, StringComparison.OrdinalIgnoreCase))
                        && !direct.Any(f => Key(f.name).Equals(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        secondaryNames.Add(k);
                    }
                }
            }
        }
        // RV2944-shell: warm the XmlSerializer probe chain in the existing
        // parallel lanes. The CLR probes "<base>.XmlSerializers" at runtime;
        // those names never appear in PE references, so without this they
        // always fall through to the synchronous demand hook. Miss-safe:
        // PrefetchOneAsync already swallows non-success per name.
        foreach (string c in OptionalProbeCandidates(manifest.entry, direct.Select(d => Key(d.name))))
        {
            if (!loadedNames.Contains(WellKnownForkAssembly(c) ?? c)
                && !bootstrap.Any(f => Key(f.name).Equals(c, StringComparison.OrdinalIgnoreCase))
                && !direct.Any(f => Key(f.name).Equals(c, StringComparison.OrdinalIgnoreCase)))
            {
                secondaryNames.Add(c);
            }
        }
        if (secondaryNames.Count > 0)
        {
            var secondary = secondaryNames.Select(n => new ManifestPrefetch(n,
                "/api/appresolve?app=" + Uri.EscapeDataString(app) + "&name=" + Uri.EscapeDataString(n), 0, "")).ToArray();
            await Task.WhenAll(secondary.Select(async f =>
            {
                await bootstrapSlots.WaitAsync();
                try { await PrefetchOneAsync(http, f); }
                catch { }
                finally { bootstrapSlots.Release(); }
            }));
        }
        return (entry, manifest);

        // One prefetch lane: raw transformed bytes, stored only when the
        // server's content hash header matches. Anything else (miss,
        // optional assembly, drift) is left for the demand hook.
        async Task<long> PrefetchOneAsync(HttpClient client, ManifestPrefetch f)
        {
            string key = Key(f.name);
            lock (Gate)
            {
                if (Bytes.ContainsKey(key))
                {
                    RecordResolutionHit(app, true);
                    return 0;
                }
            }
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                using var resp = await client.GetAsync(f.url);
                double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (!resp.IsSuccessStatusCode)
                {
                    RecordPrefetchTiming(app, elapsedMs, 0, false);
                    return 0;
                }
                byte[] body = await resp.Content.ReadAsByteArrayAsync();
                if (resp.Headers.TryGetValues("X-WfWasm-Sha256", out var values)
                    && values.FirstOrDefault() is string sha
                    && VerifySha256Hex(body, sha))
                {
                    lock (Gate) Bytes[key] = body;
                    await ReportAsync(js, body.Length, false);
                    RecordPrefetchTiming(app, elapsedMs, body.Length, true);
                    return body.Length;
                }
                RecordPrefetchTiming(app, elapsedMs, body.Length, false);
                return 0;
            }
            catch
            {
                double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                RecordPrefetchTiming(app, elapsedMs, 0, false);
                throw;
            }
        }
    }

    private static async Task<byte[]> FetchVerifiedAsync(HttpClient http, ManifestFile file)
    {
        byte[] bytes = await http.GetByteArrayAsync(file.url);
        if (!VerifySha256Hex(bytes, file.sha256))
            throw new InvalidDataException("Assembly integrity check failed: " + file.name);
        Console.WriteLine($"[AppAssemblyLoader] Fetched {file.name} ({bytes.Length} bytes).");
        return bytes;
    }

    private static async Task RestoreContentAsync(string app, HttpClient http, IJSRuntime? js, List<ManifestContent>? files)
    {
        // Always reset/restore the VFS, even for apps whose assembly task was cached.
        // Otherwise A → B → A leaves B's resources registered for A.
        ContentSet? cached;
        lock (Gate) Content.TryGetValue(app, out cached);
        if (cached is not null)
        {
            AppFileSystem.Clear();
            foreach (var item in cached.Items) { Register(item); await ReportAsync(js, item.SourceSize, true); }
            Touch(app);
            return;
        }
        if (files is null || files.Count == 0) { AppFileSystem.Clear(); return; }
        await PhaseAsync(js, "Downloading and decoding app content");
        using var slots = new SemaphoreSlim(4);
        var items = await Task.WhenAll(files.Select(async file =>
        {
            await slots.WaitAsync();
            try
            {
                byte[] bytes = await http.GetByteArrayAsync(file.url);
                if (file.sha256 is not null && !VerifySha256Hex(bytes, file.sha256))
                    throw new InvalidDataException("Content integrity check failed: " + file.name);
                await ReportAsync(js, bytes.Length, false);
                string ext = Path.GetExtension(file.name).ToLowerInvariant();
                if (js is not null && ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp")
                {
                    byte[] decoded = await js.InvokeAsync<byte[]>("wfwasm.decodeImageBytes", bytes);
                    if (decoded.Length < 8) throw new InvalidDataException("Invalid decoded image: " + file.name);
                    int w = BitConverter.ToInt32(decoded, 0), h = BitConverter.ToInt32(decoded, 4);
                    if (w <= 0 || h <= 0 || decoded.LongLength != 8L + (long)w * h * 4)
                        throw new InvalidDataException("Invalid decoded dimensions: " + file.name);
                    var rgba = decoded.AsSpan(8).ToArray();
                    return new ContentItem(file.name, null, new(w, h, rgba), bytes.Length);
                }
                return new ContentItem(file.name, bytes, null, bytes.Length);
            }
            finally { slots.Release(); }
        }));
        AppFileSystem.Clear();
        foreach (var item in items) Register(item);
        var set = new ContentSet(items.ToList(), items.Sum(i => i.Size));
        lock (Gate)
        {
            if (set.Size <= ContentBudget)
            {
                Content[app] = set;
                Touch(app);
                while (Content.Values.Sum(c => c.Size) > ContentBudget && ContentLru.First is not null)
                {
                    string oldest = ContentLru.First.Value;
                    ContentLru.RemoveFirst();
                    Content.Remove(oldest);
                }
            }
        }
    }

    private static void Touch(string app)
    {
        lock (Gate) { ContentLru.Remove(app); ContentLru.AddLast(app); }
    }
    private static void Register(ContentItem item)
    {
        if (item.Image is { } image) AppFileSystem.RegisterDecoded(item.Name, image.Width, image.Height, image.Rgba);
        else if (item.Bytes is { } bytes) AppFileSystem.Register(item.Name, bytes);
    }
    private static ValueTask PhaseAsync(IJSRuntime? js, string phase) => js is null ? ValueTask.CompletedTask : js.InvokeVoidAsync("preview.phase", phase);
    private static ValueTask ReportAsync(IJSRuntime? js, long bytes, bool reused) => js is null ? ValueTask.CompletedTask : js.InvokeVoidAsync("preview.asset", bytes, reused);
}
