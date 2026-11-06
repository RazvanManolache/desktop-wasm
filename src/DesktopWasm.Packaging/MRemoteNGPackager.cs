using System.Runtime.InteropServices;
using System.Text.Json;
using DesktopWasm.Core;
using Mono.Cecil;

namespace DesktopWasm.Packaging;

/// <summary>
/// Real mRemoteNG closure packaging (dw-import owned, CREDIT1300-IMP3).
///
/// Builds the reconverted closure over the RDP-excluded staged package at
/// `.artifacts/benchmark-apps/benchmark-mremoteng` (see preparation.json
/// there): entry `mRemoteNG.exe` plus `Assemblies/*.dll`, lowered through
/// the shared joint policy (WinForms fork pass primary, WPF refs routed
/// through <see cref="JointMap"/> and existence-verified against the real
/// provider binaries), and staged into a NEW output directory.
///
/// Disposition mirrors <see cref="JointTestAppPackager"/>: packaged
/// (converted image staged as a hashed .bin), framework-provided (a
/// converted reference the net9 BrowserHost bundle already ships — staging
/// the staged net48 copy would shadow the framework copy). net48 BCL
/// facades (mscorlib/System/...) are never packaged.
///
/// WinForms-only entry: no joint-interface bridge gaps and no loose app.xaml
/// embedding apply (both are WPF-entry seams). Content files (Themes, Icons,
/// Schemas, configs) ride through verbatim under their staged relative
/// paths; the `dll/` resolver mirror is regenerated from the converted
/// payloads. Idempotent: a second run over converted bytes retargets nothing.
/// </summary>
public static class MRemoteNGPackager
{
    public const string Slug = "benchmark-mremoteng";
    public const string EntryAssembly = "mRemoteNG";

    /// <summary>
    /// Converter map version the manifest carries. Must equal
    /// DesktopWasm.Wpf.WpfAppBoot.MapVersion (loader-compatibility);
    /// parity is enforced by MRemoteNGPackageTests so this project stays
    /// decoupled from the Wpf engine.
    /// </summary>
    public const string MapVersion = JointTestAppPackager.MapVersion;

    public const string ForkAssembly = JointTestAppPackager.ForkAssembly;
    public const string EngineAssembly = JointTestAppPackager.EngineAssembly;
    public const string DrawingAssembly = JointTestAppPackager.DrawingAssembly;

    /// <summary>
    /// Scopes that stay bundle-side (CREDIT1300-IMP4 / RT3 §5): the net9
    /// BrowserHost bundle ships System.ObjectModel,
    /// System.Configuration.ConfigurationManager and
    /// System.Windows.Extensions, so references to them are never re-scoped
    /// to an engine. System.IO.Packaging is NOT bundle-shipped: it rides as
    /// a packaged private closure member (pack copy, pinned in the receipt),
    /// which is the same keep mechanism with packaged backing. The
    /// engine-declares alternative is declined (BCL identity fork risk).
    /// Microsoft.Win32.SystemEvents is DELIBERATELY absent: its
    /// type-collision needs the explicit bridge adapter (design handoff).
    /// </summary>
    public static IReadOnlySet<string> KeepScopes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System.ObjectModel",
        "System.Configuration.ConfigurationManager",
        "System.Windows.Extensions",
        "System.IO.Packaging",
        "System.CodeDom",
    };

    public static readonly JsonSerializerOptions JsonOptions = JointTestAppPackager.JsonOptions;

    /// <summary>
    /// Content (non-assembly) files that ride through verbatim. Skips the
    /// build-applicable set the staged preparation excludes from serving.
    /// </summary>
    private static readonly ISet<string> ContentSkipExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pdb", ".xml",
    };

    private static readonly ISet<string> ContentSkipDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Languages", "runtimes",
    };

    /// <summary>
    /// Builds the full mRemoteNG package: closure, lowering, staging,
    /// manifest, receipt and provenance. Never synthesizes success: any
    /// closure, routing or provider gap yields a conversion-failed
    /// manifest carrying the precise gaps.
    /// </summary>
    public static JointTestAppPackager.JointPackageResult Build(
        string stagedDirectory,
        string outputDirectory,
        IReadOnlyDictionary<string, string> providerAssemblyPaths,
        IEnumerable<string>? frameworkSearchDirectories = null,
        IReadOnlySet<string>? bundleProvidedAssemblyNames = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(stagedDirectory);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        ArgumentNullException.ThrowIfNull(providerAssemblyPaths);
        stagedDirectory = Path.GetFullPath(stagedDirectory);
        outputDirectory = Path.GetFullPath(outputDirectory);
        if (outputDirectory.Equals(stagedDirectory, StringComparison.OrdinalIgnoreCase) ||
            outputDirectory.StartsWith(stagedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Package output must not contain, or sit inside, the original input.");
        foreach (var name in new[] { ForkAssembly, EngineAssembly, DrawingAssembly })
            if (!providerAssemblyPaths.ContainsKey(name))
                throw new ArgumentException($"Provider binary path missing for '{name}'.");

        // UNSTALL1510: immutable provider snapshot. Foreign rebuilds
        // mid-run must not move the inputs: copy the three provider
        // binaries under the output first, then read ONLY the snapshots.
        // The receipt pins snapshot hashes = run-input hashes.
        var providerSnapshotDir = Path.Combine(outputDirectory, ".providers");
        if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, recursive: true);
        Directory.CreateDirectory(providerSnapshotDir);
        var snapProviders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in providerAssemblyPaths)
        {
            var snap = Path.Combine(providerSnapshotDir, Path.GetFileName(kv.Value));
            File.Copy(kv.Value, snap, overwrite: false);
            snapProviders[kv.Key] = snap;
        }

        var typeSets = JointTestAppPackager.LoadProviderTypeSets(snapProviders);

        var assembliesDir = Path.Combine(stagedDirectory, "Assemblies");
        var searchDirs = new List<string> { stagedDirectory };
        if (Directory.Exists(assembliesDir)) searchDirs.Add(assembliesDir);
        // Mapped shared-framework inputs (never a mutable old fallback):
        // e.g. the .NET 10 WindowsDesktop shared pack for framework-only
        // assemblies the staged tree does not carry. Pinned in the receipt.
        var frameworkDirs = (frameworkSearchDirectories ?? []).Select(Path.GetFullPath).ToArray();
        searchDirs.AddRange(frameworkDirs);

        // UNSTALL1510: precise-at-write resolution. Reads in the convert
        // branch resolve through the real provider snapshot dirs (including
        // the real fork), so a write failure is proven mixed-mode, never a
        // missing-resolver artifact.
        var resolver = new DefaultAssemblyResolver();
        foreach (var dir in searchDirs) resolver.AddSearchDirectory(dir);
        resolver.AddSearchDirectory(providerSnapshotDir);
        // The managed entry is mRemoteNG.dll: mRemoteNG.exe is a NATIVE
        // launcher stub (Cecil BadImageFormat), so it rides through as
        // verbatim content, never as a closure node.
        var entryPath = Path.Combine(stagedDirectory, EntryAssembly + ".dll");
        var closure = JointClosure.Build(entryPath, searchDirs, JointTestAppPackager.NetFrameworkProvided());

        Directory.CreateDirectory(outputDirectory);

        var converted = new List<JointTestAppPackager.ConvertedNode>();
        var frameworkRefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var directRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var transitiveRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gaps = new List<JointGap>(closure.Gaps);
        var assemblies = new List<PackageAssembly>();
        var stagedNameByNode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (closure.Ok)
        {
            var entry = closure.Nodes.FirstOrDefault(n => n.Name.Equals(EntryAssembly, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                gaps.Add(new JointGap(EntryAssembly, JointGap.MissingDependency, "Closure has no mRemoteNG entry node."));
            else if (!entry.Capabilities.HasFlag(FrameworkCapabilities.WinForms))
                gaps.Add(new JointGap(EntryAssembly, JointGap.MissingDependency,
                    $"mRemoteNG entry is '{entry.Capabilities}', not WinForms: not a WinForms application entry."));

            var entryOk = entry is not null && entry.Capabilities.HasFlag(FrameworkCapabilities.WinForms);
            foreach (var node in closure.Nodes)
            {
                if (!entryOk) break;

                // Bundle-shipped framework assemblies are never packaged:
                // staging converted copies would shadow the framework copy
                // the net9 BrowserHost bundle already ships. Recorded with
                // provider identity, matching the joint rule.
                if ((bundleProvidedAssemblyNames?.Contains(node.Name) == true) &&
                    !node.Name.Equals(EntryAssembly, StringComparison.OrdinalIgnoreCase))
                {
                    using var providedAsm = AssemblyDefinition.ReadAssembly(node.Path);
                    foreach (var r in providedAsm.MainModule.AssemblyReferences)
                    {
                        if (JointTestAppPackager.NetFrameworkProvided().Contains(r.Name) || JointTestAppPackager.TargetRuntimeProvided.Contains(r.Name))
                        {
                            frameworkRefs.TryAdd(r.Name, r.Version?.ToString() ?? "unknown");
                            if (!directRefs.Contains(r.Name)) transitiveRefs.Add(r.Name);
                        }
                    }
                    frameworkRefs.TryAdd(node.Name, node.Version);
                    directRefs.Add(node.Name);
                    converted.Add(new JointTestAppPackager.ConvertedNode(node.Name, node.Version, node.Sha256, node.Sha256, string.Empty, [], 0, 0, 0, null));
                    continue;
                }

                var inputSha = JointClosure.HashOf(node.Path);
                int fork, wpf, drawing;
                IReadOnlyList<JointGap> nodeGaps;
                string[] references;
                List<(string Name, string Version)> refVersions = [];
                var tempPath = Path.Combine(outputDirectory, ".converting-" + node.Name + ".dll");
                if (!JointTestAppPackager.HasRoutedRefs(node.Path))
                {
                    // No convertible reference anywhere in the graph: the
                    // pristine bytes pass through untouched (input hash ==
                    // output hash), still staged under a hashed name.
                    File.Copy(node.Path, tempPath);
                    using var pristine = AssemblyDefinition.ReadAssembly(tempPath);
                    references = pristine.MainModule.AssemblyReferences.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    refVersions = pristine.MainModule.AssemblyReferences
                        .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(g => (g.Key, g.First().Version?.ToString() ?? "unknown"))
                        .ToList();
                    (fork, wpf, drawing, nodeGaps) = (0, 0, 0, []);
                }
                else
                {
                    using var assembly = AssemblyDefinition.ReadAssembly(node.Path, new ReaderParameters { AssemblyResolver = resolver });
                    var module = assembly.MainModule;
                    // CREDIT1300-RT5: strip wasm-undecodable WPF metadata rows
                    // before routing (WebView2.Wpf carries its own ThemeInfo
                    // blob; the Mono loader aborts on it, assembly.c:1673).
                    int stripped = JointTestAppPackager.StripInertWpfAssemblyAttributes(module);
                    var capabilities = JointCapabilities.Detect(module);
                    var forkScope = JointTestAppPackager.EnsureScope(module, ForkAssembly, snapProviders);
                    var engineScope = JointTestAppPackager.EnsureScope(module, EngineAssembly, snapProviders);
                    var drawingScope = JointTestAppPackager.EnsureScope(module, DrawingAssembly, snapProviders);
                    (fork, wpf, drawing, nodeGaps) = JointTestAppPackager.RetargetModule(
                        module, capabilities, forkScope, engineScope, drawingScope, typeSets, KeepScopes);
                    wpf += stripped;
                    JointTestAppPackager.PruneUnusedProviderScopes(module, forkScope, engineScope, drawingScope);
                    JointTestAppPackager.PruneDrainedSourceScopes(module);
                    try { assembly.Write(tempPath); }
                    catch (Exception ex)
                    {
                        // Mixed-mode (C++/CLI) images read but never write:
                        // record the precise gap instead of crashing the run.
                        nodeGaps = nodeGaps.Append(new JointGap(node.Name, "rewrite-error",
                            $"Lowered image cannot be rewritten by Cecil ({ex.GetBaseException().Message}): mixed-mode or otherwise unloadable binary; decide exclusion vs runtime stub in the runtime lane.")).ToArray();
                    }
                    references = module.AssemblyReferences.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    refVersions = module.AssemblyReferences
                        .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(g => (g.Key, g.First().Version?.ToString() ?? "unknown"))
                        .ToList();
                }
                // UNSTALL1510: no break-on-first-gap. Every node lowers (or
                // records its precise gap) so the manifest carries the
                // deterministic COMPLETE gap list; a first-gap report is
                // not a different root cause per run.
                if (nodeGaps.Count != 0)
                {
                    gaps.AddRange(nodeGaps);
                    try { File.Delete(tempPath); } catch { }
                    continue;
                }
                var outputSha = JointClosure.HashOf(tempPath);
                var file = PackageAssembly.AssetFileName(node.Name, outputSha);
                File.Move(tempPath, Path.Combine(outputDirectory, file));
                stagedNameByNode[node.Name] = Path.GetFileName(node.Path);
                foreach (var (reference, version) in refVersions)
                {
                    if (converted.Any(c => c.Name.Equals(reference, StringComparison.OrdinalIgnoreCase))) continue;
                    if (JointTestAppPackager.NetFrameworkProvided().Contains(reference) || JointTestAppPackager.TargetRuntimeProvided.Contains(reference) ||
                        JointMap.RuntimeProvidedJointInterfaces.Contains(reference))
                    {
                        frameworkRefs.TryAdd(reference, version);
                        directRefs.Add(reference);
                    }
                }
                converted.Add(new JointTestAppPackager.ConvertedNode(node.Name, node.Version, inputSha, outputSha, file, references, fork, wpf, drawing, null));
                assemblies.Add(new PackageAssembly(node.Name, file, outputSha, references, inputSha));
            }
        }

        if (closure.Ok && gaps.Count == 0)
        {
            foreach (var dir in searchDirs)
            {
                foreach (var name in JointTestAppPackager.TargetRuntimeProvided)
                {
                    var stagedPath = Path.Combine(dir, name + ".dll");
                    if (!File.Exists(stagedPath)) continue;
                    using var providedAsm = AssemblyDefinition.ReadAssembly(stagedPath);
                    frameworkRefs.TryAdd(name, providedAsm.Name.Version?.ToString() ?? "unknown");
                    if (!directRefs.Contains(name)) transitiveRefs.Add(name);
                }
            }
        }

        var frameworkProvided = frameworkRefs
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new JointTestAppPackager.ProvidedReference(kv.Key, kv.Value, "browser-bundle",
                transitiveRefs.Contains(kv.Key)
                    ? "Transitive framework reference observed in a staged bundle-provided copy; resolved from the net9 BrowserHost bundle at runtime, never packaged."
                    : "Direct framework reference in a converted image; resolved from the net9 BrowserHost bundle at runtime, never packaged."))
            .ToList();

        var manifest = new ApplicationPackage(
            1, Slug, MapVersion,
            gaps.Count == 0 ? "converted" : "conversion-failed",
            EntryAssembly, assemblies.ToArray(),
            gaps.Distinct().Select(g => new PackageGap(g.Assembly, g.Kind, g.Detail)).ToArray(), null);
        manifest.Validate(Slug, MapVersion);
        WriteManifest(outputDirectory, manifest);
        WriteReceipt(outputDirectory, closure, manifest, converted, frameworkProvided, snapProviders, frameworkDirs);
        WriteProvenance(outputDirectory, stagedDirectory, manifest, converted);

        if (gaps.Count == 0)
        {
            RegenerateDllMirror(outputDirectory, converted, stagedNameByNode);
            CopyContentVerbatim(stagedDirectory, outputDirectory);
            CopyNativeEntryStub(stagedDirectory, outputDirectory);
        }

        return new JointTestAppPackager.JointPackageResult(manifest, converted, frameworkProvided, gaps.Distinct().ToArray(), outputDirectory);
    }

    /// <summary>
    /// Regenerates the flat `dll/` resolver mirror from the converted
    /// payloads under their original PE names (entry exe excluded: it
    /// stages at the package root mapping, not in the mirror).
    /// </summary>
    private static void RegenerateDllMirror(
        string outputDirectory,
        IReadOnlyList<JointTestAppPackager.ConvertedNode> converted,
        IReadOnlyDictionary<string, string> stagedNameByNode)
    {
        var mirror = Path.Combine(outputDirectory, "dll");
        Directory.CreateDirectory(mirror);
        foreach (var node in converted)
        {
            if (string.IsNullOrEmpty(node.File)) continue;
            if (!stagedNameByNode.TryGetValue(node.Name, out var stagedName)) continue;
            if (!stagedName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(
                Path.Combine(outputDirectory, node.File),
                Path.Combine(mirror, stagedName),
                overwrite: true);
        }
    }

    /// <summary>
    /// Copies content (non-assembly) files verbatim under their staged
    /// relative paths: themes, icons, schemas, configs. Never converts.
    /// </summary>
    private static void CopyContentVerbatim(string stagedDirectory, string outputDirectory)
    {
        foreach (var path in Directory.GetFiles(stagedDirectory, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(stagedDirectory, path);
            var parts = rel.Split(Path.DirectorySeparatorChar);
            if (parts.Any(p => ContentSkipDirectories.Contains(p))) continue;
            if (ContentSkipExtensions.Contains(Path.GetExtension(path))) continue;
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            var dest = Path.Combine(outputDirectory, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(path, dest, overwrite: true);
        }
    }

    /// <summary>
    /// Copies the native entry launcher stub verbatim (never converted:
    /// not a managed image). Serve mapping owns whether it is used.
    /// </summary>
    private static void CopyNativeEntryStub(string stagedDirectory, string outputDirectory)
    {
        var stub = Path.Combine(stagedDirectory, EntryAssembly + ".exe");
        if (File.Exists(stub))
            File.Copy(stub, Path.Combine(outputDirectory, EntryAssembly + ".exe"), overwrite: true);
    }

    private static void WriteManifest(string outputDirectory, ApplicationPackage manifest)
    {
        var pending = Path.Combine(outputDirectory, ".manifest-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(pending, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(pending, Path.Combine(outputDirectory, "app-manifest.json"), true);
    }

    private static void WriteReceipt(
        string outputDirectory, BuiltJointClosure closure, ApplicationPackage manifest,
        IReadOnlyList<JointTestAppPackager.ConvertedNode> converted,
        IReadOnlyList<JointTestAppPackager.ProvidedReference> frameworkProvided,
        IReadOnlyDictionary<string, string> providerAssemblyPaths,
        string[] frameworkDirs)
    {
        var receipt = new
        {
            slug = Slug,
            entryAssembly = EntryAssembly,
            mapVersion = MapVersion,
            builtAtUtc = DateTime.UtcNow.ToString("o"),
            hostFramework = RuntimeInformation.FrameworkDescription,
            status = manifest.Status,
            adaptation = "RDP-excluded browser build input: MSTSCLib COMReference removed (MSB4803); 7 COM-bound RDP classes excluded at source; RDP seam throws NotSupportedException at runtime (load-time closure unaffected); GenerateAssemblyInfo=false (legacy AssemblyInfo kept). Upstream tree untouched. dll/ resolver mirror regenerated from converted payloads.",
            frameworkInputs = frameworkDirs,
            closureNodes = closure.Nodes.Select(n => new
            {
                name = n.Name,
                version = n.Version,
                sha256 = n.Sha256,
                capabilities = n.Capabilities.ToString(),
                disposition = frameworkDirs.Any(d => n.Path.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    ? "framework-mapped"
                    : "packaged",
            }).ToArray(),
            converted = converted.Select(c => new
            {
                name = c.Name,
                inputSha256 = c.InputSha256,
                outputSha256 = c.OutputSha256,
                file = c.File,
                forkRetargets = c.ForkRetargets,
                wpfRetargets = c.WpfRetargets,
                drawingRetargets = c.DrawingRetargets,
            }).ToArray(),
            frameworkProvided = frameworkProvided.Select(p => new { name = p.Name, provider = p.Provider, detail = p.Detail }).ToArray(),
            drawingProvider = new
            {
                assembly = "WfWasm.Drawing",
                detail = "System.Drawing.* member references resolve here. Verified per member against the real provider binary.",
            },
            providersPinned = providerAssemblyPaths.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    path = kv.Value,
                    version = ((Func<string>)(() =>
                    {
                        using var a = AssemblyDefinition.ReadAssembly(kv.Value);
                        return a.Name.Version.ToString();
                    }))(),
                    sha256 = JointClosure.HashOf(kv.Value),
                }),
            gaps = manifest.Gaps.Select(g => new { assembly = g.Assembly, kind = g.Kind, detail = g.Detail }).ToArray(),
            notes = new[]
            {
                "Converted images preserve all manifest resources byte-for-byte: only type-reference scopes/namespaces move, plus stripped wasm-undecodable WPF metadata rows (ThemeInfoAttribute).",
                "WPF-routed references move into the engine namespace (DesktopWasm.Wpf.*). Existence-verified per member against the real engine binary; unresolvable members are precise gaps, never silent drops.",
                "Content files (Themes/Icons/Schemas/configs) ride through verbatim under staged relative paths; mRemoteNG.deps.json is NOT rewritten (shell regen owns serve-mapping).",
                "Method-body call compatibility is engine fidelity, proven by browser boot (verifier lane), not by this static closure.",
            },
        };
        File.WriteAllText(
            Path.Combine(outputDirectory, "benchmark-mremoteng.receipt.json"),
            JsonSerializer.Serialize(receipt, JsonOptions));
    }

    private static void WriteProvenance(
        string outputDirectory, string stagedDirectory, ApplicationPackage manifest,
        IReadOnlyList<JointTestAppPackager.ConvertedNode> converted)
    {
        var files = Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(outputDirectory, p))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new
            {
                name = f,
                bytes = new FileInfo(Path.Combine(outputDirectory, f)).Length,
                sha256 = JointClosure.HashOf(Path.Combine(outputDirectory, f)),
            }).ToArray();
        var provenance = new
        {
            id = Slug,
            builtUtc = DateTime.UtcNow.ToString("o"),
            sourceInputs = stagedDirectory,
            sourceProvenance = Path.Combine(stagedDirectory, "preparation.json"),
            manifestSha256 = JointClosure.HashOf(Path.Combine(outputDirectory, "app-manifest.json")),
            converted = converted.Select(c => new { name = c.Name, inputSha256 = c.InputSha256, outputSha256 = c.OutputSha256 }).ToArray(),
            status = manifest.Status,
            files,
        };
        File.WriteAllText(
            Path.Combine(outputDirectory, "PROVENANCE.json"),
            JsonSerializer.Serialize(provenance, JsonOptions));
    }
}
