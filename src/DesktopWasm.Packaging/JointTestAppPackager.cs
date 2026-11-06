using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Core;
using Mono.Cecil;

namespace DesktopWasm.Packaging;

/// <summary>
/// Real TestApp joint packaging (dw-runtime owned, RECOVER-RUNTIME-01).
///
/// Builds the joint closure over the pristine staged20 inputs at
/// inputs/mixed/avalondock (see provenance.json there), lowers every
/// packaged node through the composed joint policy (WinForms fork pass
/// first, WPF pass second, joint interface untouched), and stages a
/// loader-compatible manifest plus the full converted assembly/resource
/// closure into a NEW output directory.
///
/// Disposition per closure node (recorded in the receipt, never silent):
/// packaged (converted image staged as a hashed .bin), framework-provided
/// (a converted reference the net9 BrowserHost bundle already ships —
/// staging the staged20 net48 copy would shadow the framework copy), or
/// bridge-provided (the WindowsFormsIntegration joint interface, served
/// at runtime by DesktopWasm.Interop's WindowsFormsHostResolver over a
/// real fork control). net48 BCL facades (mscorlib/System/...) are never
/// packaged.
///
/// Lowering vs the additive seams: <see cref="JointPrecedence"/> decides
/// the route (shared policy, untouched here); the lowering additionally
/// moves WPF-routed references into the engine namespace
/// (System.Windows.* does not exist in the engine — only
/// DesktopWasm.Wpf.* does, verified against engine binaries) and resolves
/// System.Drawing.* to the WfWasm.Drawing provider (the fork's own
/// binaries scope their drawing references there, and the served bundle
/// ships it). Every lowered reference is existence-verified against the
/// real provider binaries: a missing member is a precise
/// engine-unavailable gap, never a dangling scope. The walk covers member
/// references in method bodies, locals, exception handlers, custom
/// attributes and generic constraints in addition to type positions.
/// Idempotent: a second run over converted bytes retargets nothing.
/// </summary>
public static class JointTestAppPackager
{
    public const string Slug = "testapp-joint";
    public const string EntryAssembly = "TestApp";

    /// <summary>
    /// Converter map version the manifest carries. Must equal
    /// DesktopWasm.Wpf.WpfAppBoot.MapVersion (loader-compatibility);
    /// parity is enforced by TestAppJointPackageTests so this project
    /// stays decoupled from the Wpf engine.
    /// </summary>
    public const string MapVersion = "wpf-convert-v1";

    public const string ForkAssembly = JointMap.WinFormsEngineAssembly;
    public const string EngineAssembly = JointMap.WpfEngineAssembly;

    /// <summary>
    /// Concrete provider of System.Drawing.* member references. The joint
    /// policy routes System.Drawing to the WinForms engine; the fork's own
    /// binaries scope their drawing references to this assembly and the
    /// served bundle ships it, so the lowering resolves to the concrete
    /// provider and existence-verifies every member there.
    /// </summary>
    public const string DrawingAssembly = "WfWasm.Drawing";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// Closure members the net9 BrowserHost bundle already ships (verified
    /// in the BrowserHost build output): never packaged, never converted.
    /// Staging the staged20 net48 copies would shadow the framework copy
    /// the engines bind to. In practice these never even become closure
    /// nodes (<see cref="JointClosure"/> treats the runtime set as
    /// provided); they surface here as observed framework references in
    /// converted images, recorded with provider identities in the receipt.
    /// Microsoft.Bcl.AsyncInterfaces is deliberately absent: nothing in
    /// the bundle ships it, so if it ever enters a closure it is a
    /// genuine private member and is packaged.
    /// </summary>
    public static IReadOnlySet<string> TargetRuntimeProvided { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System.Buffers",
        "System.IO.Pipelines",
        "System.Memory",
        "System.Numerics.Vectors",
        "System.Runtime.CompilerServices.Unsafe",
        "System.Text.Encodings.Web",
        "System.Text.Json",
        "System.Threading.Tasks.Extensions",
    };

    public sealed record ConvertedNode(
        string Name, string Version, string InputSha256, string OutputSha256,
        string File, string[] References, int ForkRetargets, int WpfRetargets, int DrawingRetargets,
        string? LooseAppXaml);

    public sealed record ProvidedReference(string Name, string ReferencedVersion, string Provider, string Detail);

    public sealed record JointPackageResult(
        ApplicationPackage Manifest,
        IReadOnlyList<ConvertedNode> Converted,
        IReadOnlyList<ProvidedReference> FrameworkProvided,
        IReadOnlyList<JointGap> Gaps,
        string OutputDirectory);

    /// <summary>
    /// net48 framework names the DesktopWasm runtime already provides (or
    /// that the joint maps rewrite): never packaged from disk, never
    /// demanded as files. Mirrors the AvalonDockClosureTests set so the
    /// closure result matches the proven zero-gap baseline.
    /// </summary>
    public static IReadOnlySet<string> NetFrameworkProvided()
    {
        var provided = new HashSet<string>(
            JointClosure.ProvidedAssemblies(Path.GetDirectoryName(typeof(object).Assembly.Location)!),
            StringComparer.OrdinalIgnoreCase);
        provided.UnionWith([
            "mscorlib", "netstandard",
            "System", "System.Core", "System.Xml", "System.Xml.Linq",
            "System.Numerics", "System.ValueTuple",
        ]);
        return provided;
    }

    /// <summary>
    /// Loads the full type-name sets of the real provider binaries (engine,
    /// fork, drawing) for the existence audit. Paths come from the caller
    /// so this project keeps no engine reference.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> LoadProviderTypeSets(
        IReadOnlyDictionary<string, string> providerAssemblyPaths)
    {
        var sets = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in providerAssemblyPaths)
        {
            if (!File.Exists(kv.Value))
                throw new ArgumentException($"Provider binary absent: {kv.Key} at '{kv.Value}'.");
            using var assembly = AssemblyDefinition.ReadAssembly(kv.Value);
            sets[kv.Key] = assembly.MainModule.Types
                .SelectMany(Flatten)
                .Select(t => t.FullName)
                .ToHashSet(StringComparer.Ordinal);
        }
        return sets;
    }

    private static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes)
            foreach (var inner in Flatten(nested))
                yield return inner;
    }

    /// <summary>
    /// The joint packager's copy of the converter's inert-assembly-attribute
    /// rule (AssemblyConverter.AssemblyAttributeIsInert): XAML namespace
    /// markers (System.Windows.Markup XmlnsDefinition/XmlnsPrefix/
    /// XmlnsCompatibleWith), the loose-content marker
    /// (System.Windows.Resources AssemblyAssociatedContentFileAttribute) and
    /// the theme-location marker (System.Windows ThemeInfoAttribute). The
    /// engine never reads assembly attributes by any API, converted code
    /// cannot name these types (any live reference gaps), and the wasm Mono
    /// loader asserts on the undecodable rows instead of skipping them
    /// (served joint entry: custom-attrs.c:1980 + assembly.c:1673, exit 1).
    /// </summary>
    private static bool IsInertWpfAssemblyAttribute(string ns, string name) =>
        (ns.Equals("System.Windows.Markup", StringComparison.Ordinal) &&
            (name.Equals("XmlnsDefinitionAttribute", StringComparison.Ordinal) ||
                name.Equals("XmlnsPrefixAttribute", StringComparison.Ordinal) ||
                name.Equals("XmlnsCompatibleWithAttribute", StringComparison.Ordinal))) ||
        (ns.Equals("System.Windows.Resources", StringComparison.Ordinal) &&
            name.Equals("AssemblyAssociatedContentFileAttribute", StringComparison.Ordinal)) ||
        (ns.Equals("System.Windows", StringComparison.Ordinal) &&
            name.Equals("ThemeInfoAttribute", StringComparison.Ordinal));

    /// <summary>
    /// Removes inert WPF assembly/module attribute rows (see
    /// <see cref="IsInertWpfAssemblyAttribute"/>) whose type scope is a
    /// joint convertible source assembly. Only exact-named inert markers
    /// are removed; anything else keeps today's retarget/gap behavior.
    /// Returns stripped rows. Idempotent: a converted module has no such
    /// rows left, so a re-run strips nothing.
    /// </summary>
    public static int StripInertWpfAssemblyAttributes(Mono.Cecil.ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        int stripped = 0;
        foreach (var owner in new[] { module.Assembly.CustomAttributes, module.CustomAttributes })
        {
            foreach (var attr in owner.ToList())
            {
                var attributeType = attr.AttributeType;
                while (attributeType is Mono.Cecil.TypeSpecification spec)
                    attributeType = spec.ElementType;
                if (attributeType is null || attributeType.Scope is not Mono.Cecil.AssemblyNameReference scope)
                    continue;
                if (!JointMap.WpfSourceAssemblies.Contains(scope.Name) &&
                    !JointMap.WinFormsSourceAssemblies.Contains(scope.Name))
                    continue;
                if (!IsInertWpfAssemblyAttribute(attributeType.Namespace, attributeType.Name))
                    continue;
                owner.Remove(attr);
                stripped++;
            }
        }
        return stripped;
    }

    /// <summary>
    /// Lowers one module through the composed joint policy: WinForms fork
    /// pass first, WPF pass second (see <see cref="JointPipeline"/> order),
    /// the WindowsFormsIntegration joint interface untouched. WPF-routed
    /// references additionally move into the engine namespace; drawing
    /// references resolve to the concrete drawing provider. Returns per
    /// pass counts plus visible collision/gap items. Drained source scopes
    /// are pruned afterwards (the joint interface scope is always kept).
    /// </summary>
    public static (int Fork, int Wpf, int Drawing, IReadOnlyList<JointGap> Gaps) RetargetModule(
        ModuleDefinition module,
        FrameworkCapabilities capabilities,
        AssemblyNameReference forkScope,
        AssemblyNameReference engineScope,
        AssemblyNameReference drawingScope,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? providerTypeSets = null,
        IReadOnlySet<string>? keepScopes = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(forkScope);
        ArgumentNullException.ThrowIfNull(engineScope);
        ArgumentNullException.ThrowIfNull(drawingScope);
        var fork = 0;
        var wpf = 0;
        var drawing = 0;
        var gaps = new List<JointGap>();
        var seenGap = new HashSet<string>(StringComparer.Ordinal);
        foreach (var typeRef in CollectAllTypeRefs(module))
        {
            if (typeRef.Scope is not AssemblyNameReference scope) continue;
            if (!JointMap.WpfSourceAssemblies.Contains(scope.Name) &&
                !JointMap.WinFormsSourceAssemblies.Contains(scope.Name)) continue;
            // Bundle-side keep (CREDIT1300-IMP4): scopes the runtime bundle
            // (or a packaged private member) resolves stay untouched — never
            // re-scoped to an engine, never existence-checked against one.
            // Null keeps joint behavior byte-identical.
            if (keepScopes?.Contains(scope.Name) == true) continue;
            var fullName = typeRef.FullName;
            var dot = fullName.LastIndexOf('.');
            if (dot <= 0) continue;
            var route = JointPrecedence.ResolveType(
                scope.Name, fullName[..dot], fullName[(dot + 1)..], capabilities);
            switch (route.Kind)
            {
                case JointTargetKind.WinFormsEngine:
                    if (scope.Name.StartsWith("System.Drawing", StringComparison.OrdinalIgnoreCase))
                    {
                        typeRef.Scope = drawingScope;
                        drawing++;
                        CheckExists(gaps, seenGap, providerTypeSets, drawingScope.Name, scope.Name, fullName, typeRef.FullName);
                    }
                    else
                    {
                        typeRef.Scope = forkScope;
                        fork++;
                        CheckExists(gaps, seenGap, providerTypeSets, forkScope.Name, scope.Name, fullName, typeRef.FullName);
                    }
                    break;
                case JointTargetKind.WpfEngine:
                    // Candidate targets, in policy order: the joint
                    // policy's target namespace first (preserves shim and
                    // adapter namespaces such as System.Windows.Forms or
                    // Microsoft.Win32 inside the engine), then the engine
                    // namespace (the default home of converted WPF
                    // contracts, matching WpfTypeMap's default rule), then
                    // the original full name (the engine keeps some
                    // namespaces verbatim). The first candidate the real
                    // provider binary declares wins; none existing is a
                    // precise engine-unavailable gap.
                    var wpfHit = false;
                    if (typeRef.DeclaringType is null)
                    {
                        foreach (var candidateNs in new[] { route.TargetNamespace, JointMap.WpfEngineNamespace, typeRef.Namespace }.Distinct(StringComparer.Ordinal))
                        {
                            var candidate = candidateNs + "." + typeRef.Name;
                            if (TypeExists(providerTypeSets, engineScope.Name, candidate))
                            {
                                typeRef.Scope = engineScope;
                                typeRef.Namespace = candidateNs;
                                wpfHit = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        typeRef.Scope = engineScope;
                        wpfHit = TypeExists(providerTypeSets, engineScope.Name, typeRef.FullName);
                    }
                    wpf++;
                    if (!wpfHit)
                    {
                        var missKey = JointGap.EngineUnavailable + "|" + engineScope.Name + "|" + fullName;
                        if (seenGap.Add(missKey))
                            gaps.Add(new JointGap(scope.Name, JointGap.EngineUnavailable,
                                $"Lowered reference '{fullName}' (from {scope.Name}) names no type in provider '{engineScope.Name}' " +
                                $"(tried '{route.TargetNamespace}.{typeRef.Name}', '{JointMap.WpfEngineNamespace}.{typeRef.Name}')."));
                    }
                    break;
                case JointTargetKind.Collision or JointTargetKind.Gap:
                    // Bridge-aware collision resolution (CREDIT1300-IMP5):
                    // the SystemEventsBridgeAdapter subscribes the real
                    // broadcast and forwards the overlap surface into the
                    // engine adapter, so refs to the bridge surface bind the
                    // engine — but ONLY when the engine actually declares
                    // the exact type (fail closed: otherwise the collision
                    // gap stands, never a silent re-scope).
                    if (route.Kind == JointTargetKind.Collision &&
                        JointMap.WpfSystemEventsShimTypes.Contains(fullName) &&
                        TypeExists(providerTypeSets, engineScope.Name, fullName))
                    {
                        typeRef.Scope = engineScope;
                        wpf++;
                        break;
                    }
                    var key = route.Kind + "|" + scope.Name + "|" + fullName;
                    if (seenGap.Add(key))
                        gaps.Add(new JointGap(scope.Name,
                            route.Kind == JointTargetKind.Collision
                                ? JointGap.TypeCollision : JointGap.MissingDependency,
                            route.Reason));
                    break;
            }
        }
        PruneDrainedSourceScopes(module);
        return (fork, wpf, drawing, gaps.Distinct().ToArray());
    }

    private static bool TypeExists(
        IReadOnlyDictionary<string, IReadOnlySet<string>>? providerTypeSets,
        string provider, string fullName)
    {
        if (providerTypeSets is null) return true;
        return providerTypeSets.TryGetValue(provider, out var types) && types.Contains(fullName);
    }

    private static void CheckExists(
        List<JointGap> gaps, HashSet<string> seenGap,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? providerTypeSets,
        string provider, string sourceScope, string sourceName, string loweredName)
    {
        if (providerTypeSets is null) return;
        if (!providerTypeSets.TryGetValue(provider, out var types) || !types.Contains(loweredName))
        {
            var key = JointGap.EngineUnavailable + "|" + provider + "|" + loweredName;
            if (seenGap.Add(key))
                gaps.Add(new JointGap(sourceScope, JointGap.EngineUnavailable,
                    $"Lowered reference '{loweredName}' (from {sourceScope}:{sourceName}) names no type in provider '{provider}'."));
        }
    }

    internal static bool HasRoutedRefs(string path)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        foreach (var typeRef in CollectAllTypeRefs(assembly.MainModule))
        {
            if (typeRef.Scope is AssemblyNameReference scope &&
                (JointMap.WpfSourceAssemblies.Contains(scope.Name) ||
                 JointMap.WinFormsSourceAssemblies.Contains(scope.Name)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Removes provider scopes no type reference uses: a pure-WPF node
    /// must not carry an unused fork reference (and symmetrical cases).
    /// </summary>
    internal static void PruneUnusedProviderScopes(
        ModuleDefinition module,
        AssemblyNameReference forkScope,
        AssemblyNameReference engineScope,
        AssemblyNameReference drawingScope)
    {
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var typeRef in CollectAllTypeRefs(module))
        {
            if (typeRef.Scope is AssemblyNameReference scope)
                live.Add(scope.Name);
        }
        foreach (var scope in new[] { forkScope, engineScope, drawingScope })
        {
            if (!live.Contains(scope.Name))
                module.AssemblyReferences.Remove(scope);
        }
    }

    /// <summary>
    /// Removes convertible source scopes no type reference uses anymore.
    /// The joint-interface scope is always kept: the runtime bridge (not a
    /// packaged file) resolves it.
    /// </summary>
    internal static void PruneDrainedSourceScopes(ModuleDefinition module)
    {
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var typeRef in CollectAllTypeRefs(module))
        {
            if (typeRef.Scope is AssemblyNameReference scope)
                live.Add(scope.Name);
        }
        foreach (var reference in module.AssemblyReferences
            .Where(r => (JointMap.WpfSourceAssemblies.Contains(r.Name) ||
                         JointMap.WinFormsSourceAssemblies.Contains(r.Name)) &&
                        !JointMap.RuntimeProvidedJointInterfaces.Contains(r.Name) &&
                        !live.Contains(r.Name))
            .ToArray())
        {
            module.AssemblyReferences.Remove(reference);
        }
    }

    /// <summary>
    /// Deep scope audit over converted bytes: every type reference whose
    /// scope is still a convertible source assembly. Empty means the
    /// composed lowering owns the whole graph (the joint interface lives
    /// in neither map and is correctly absent here).
    /// </summary>
    public static IReadOnlyList<string> AuditConvertedScopes(ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var leftovers = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var typeRef in CollectAllTypeRefs(module))
        {
            if (typeRef.Scope is not AssemblyNameReference scope) continue;
            if (!JointMap.WpfSourceAssemblies.Contains(scope.Name) &&
                !JointMap.WinFormsSourceAssemblies.Contains(scope.Name)) continue;
            if (seen.Add(scope.Name + ":" + typeRef.FullName))
                leftovers.Add(scope.Name + ":" + typeRef.FullName);
        }
        return leftovers;
    }

    /// <summary>
    /// Builds the full joint package: closure, lowering, staging,
    /// manifest, receipt and provenance. Never synthesizes success: any
    /// closure, routing or provider gap yields a conversion-failed
    /// manifest carrying the precise gaps.
    /// </summary>
    public static JointPackageResult Build(
        string stagedDirectory,
        string outputDirectory,
        IReadOnlyDictionary<string, string> providerAssemblyPaths)
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

        var typeSets = LoadProviderTypeSets(providerAssemblyPaths);
        var entryPath = Path.Combine(stagedDirectory, EntryAssembly + ".exe");
        var closure = JointClosure.Build(entryPath, [stagedDirectory], NetFrameworkProvided());

        if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, recursive: true);
        Directory.CreateDirectory(outputDirectory);

        var converted = new List<ConvertedNode>();
        var frameworkRefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var directRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var transitiveRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gaps = new List<JointGap>(closure.Gaps);
        var assemblies = new List<PackageAssembly>();

        if (closure.Ok)
        {
            var entry = closure.Nodes.FirstOrDefault(n => n.Name.Equals(EntryAssembly, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                gaps.Add(new JointGap(EntryAssembly, JointGap.MissingDependency, "Closure has no TestApp entry node."));
            else if (entry.Capabilities != FrameworkCapabilities.Mixed)
                gaps.Add(new JointGap(EntryAssembly, JointGap.MissingDependency,
                    $"TestApp entry is '{entry.Capabilities}', not Mixed: not a joint WPF-containing-WinForms application."));

            foreach (var node in closure.Nodes)
            {
                if (gaps.Count != 0) break;

                var inputSha = JointClosure.HashOf(node.Path);
                int fork, wpf, drawing;
                IReadOnlyList<JointGap> nodeGaps;
                string[] references;
                List<(string Name, string Version)> refVersions = [];
                var tempPath = Path.Combine(outputDirectory, ".converting-" + node.Name + ".dll");
                if (!HasRoutedRefs(node.Path))
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
                    using var assembly = AssemblyDefinition.ReadAssembly(node.Path);
                    var module = assembly.MainModule;
                    // RVW3293-RT5: strip wasm-undecodable WPF metadata rows
                    // before routing (see StripInertWpfAssemblyAttributes):
                    // served joint entry dies in the Mono loader on these
                    // blobs (custom-attrs.c:1980 + assembly.c:1673, exit 1).
                    int stripped = StripInertWpfAssemblyAttributes(module);
                    var capabilities = JointCapabilities.Detect(module);
                    var forkScope = EnsureScope(module, ForkAssembly, providerAssemblyPaths);
                    var engineScope = EnsureScope(module, EngineAssembly, providerAssemblyPaths);
                    var drawingScope = EnsureScope(module, DrawingAssembly, providerAssemblyPaths);
                    (fork, wpf, drawing, nodeGaps) = RetargetModule(
                        module, capabilities, forkScope, engineScope, drawingScope, typeSets);
                    // Stripped inert WPF metadata rows count as WPF lowering
                    // work: a markers-only node (no code refs left) converts
                    // with changed bytes, and must never report a zero-work
                    // conversion (see the ConvertedBytes idempotency
                    // invariant). Re-runs strip nothing, so idempotency holds.
                    wpf += stripped;
                    PruneUnusedProviderScopes(module, forkScope, engineScope, drawingScope);
                    if (node.Name.Equals(EntryAssembly, StringComparison.OrdinalIgnoreCase))
                    {
                        nodeGaps = nodeGaps.Concat(CollectEntryBridgeGaps(module)).ToArray();
                        var appXamlGap = TryEmbedLooseAppXaml(module, node);
                        if (appXamlGap is not null)
                            nodeGaps = nodeGaps.Append(appXamlGap).ToArray();
                    }
                    assembly.Write(tempPath);
                    references = module.AssemblyReferences.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    refVersions = module.AssemblyReferences
                        .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(g => (g.Key, g.First().Version?.ToString() ?? "unknown"))
                        .ToList();
                }
                if (nodeGaps.Count != 0)
                {
                    gaps.AddRange(nodeGaps);
                    try { File.Delete(tempPath); } catch { }
                    break;
                }
                var outputSha = JointClosure.HashOf(tempPath);
                var file = PackageAssembly.AssetFileName(node.Name, outputSha);
                File.Move(tempPath, Path.Combine(outputDirectory, file));
                foreach (var (reference, version) in refVersions)
                {
                    if (converted.Any(c => c.Name.Equals(reference, StringComparison.OrdinalIgnoreCase))) continue;
                    if (NetFrameworkProvided().Contains(reference) || TargetRuntimeProvided.Contains(reference) ||
                        JointMap.RuntimeProvidedJointInterfaces.Contains(reference))
                    {
                        frameworkRefs.TryAdd(reference, version);
                        directRefs.Add(reference);
                    }
                }
                converted.Add(new ConvertedNode(node.Name, node.Version, inputSha, outputSha, file, references, fork, wpf, drawing,
                    node.Name.Equals(EntryAssembly, StringComparison.OrdinalIgnoreCase) ? LooseAppXamlName(node.Name, node.Version) : null));
                assemblies.Add(new PackageAssembly(node.Name, file, outputSha, references, inputSha));
            }
        }

        // Transitive framework references: the staged20 net48 copies of
        // the bundle-provided assemblies are real bytes on disk, so their
        // own references are observed (not assumed) and recorded with
        // provider identities. Nothing here is packaged.
        if (closure.Ok && gaps.Count == 0)
        {
            foreach (var name in TargetRuntimeProvided)
            {
                var stagedPath = Path.Combine(stagedDirectory, name + ".dll");
                if (!File.Exists(stagedPath)) continue;
                using var providedAsm = AssemblyDefinition.ReadAssembly(stagedPath);
                frameworkRefs.TryAdd(name, providedAsm.Name.Version?.ToString() ?? "unknown");
                if (!directRefs.Contains(name)) transitiveRefs.Add(name);
                foreach (var r in providedAsm.MainModule.AssemblyReferences)
                {
                    if (NetFrameworkProvided().Contains(r.Name) || TargetRuntimeProvided.Contains(r.Name))
                    {
                        frameworkRefs.TryAdd(r.Name, r.Version?.ToString() ?? "unknown");
                        if (!directRefs.Contains(r.Name)) transitiveRefs.Add(r.Name);
                    }
                }
            }
        }

        var frameworkProvided = frameworkRefs
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new ProvidedReference(kv.Key, kv.Value, ProviderOf(kv.Key),
                kv.Key == "WindowsFormsIntegration"
                    ? "Joint interface: GAC-only upstream, never packaged; runtime bridge DesktopWasm.Interop.WindowsFormsHostResolver serves System.Windows.Forms.Integration.WindowsFormsHost over a real fork control."
                    : transitiveRefs.Contains(kv.Key)
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
        WriteReceipt(outputDirectory, closure, manifest, converted, frameworkProvided, providerAssemblyPaths);
        WriteProvenance(outputDirectory, stagedDirectory, manifest, converted);
        return new JointPackageResult(manifest, converted, frameworkProvided, gaps.Distinct().ToArray(), outputDirectory);
    }

    private static string ProviderOf(string name) =>
        name == "WindowsFormsIntegration" ? "interop-bridge" : "browser-bundle";

    internal static AssemblyNameReference EnsureScope(
        ModuleDefinition module, string name, IReadOnlyDictionary<string, string> providerAssemblyPaths)
    {
        var existing = module.AssemblyReferences.FirstOrDefault(
            r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        using var provider = AssemblyDefinition.ReadAssembly(providerAssemblyPaths[name]);
        var scope = new AssemblyNameReference(name, provider.Name.Version);
        module.AssemblyReferences.Add(scope);
        return scope;
    }

    /// <summary>
    /// Entry-level bridge contract: TestApp must still reference the joint
    /// interface untouched (bridge-provided, never rebound), and its WPF
    /// window plus WinForms control must ride their canonical providers.
    /// </summary>
    private static IReadOnlyList<JointGap> CollectEntryBridgeGaps(ModuleDefinition module)
    {
        var gaps = new List<JointGap>();
        var refs = CollectAllTypeRefs(module)
            .Select(t => (t.FullName, Scope: (t.Scope as AssemblyNameReference)?.Name))
            .ToList();
        if (!refs.Any(s => s.FullName == "System.Windows.Forms.Integration.WindowsFormsHost" &&
                           s.Scope == "WindowsFormsIntegration"))
            gaps.Add(new JointGap(EntryAssembly, JointGap.MissingDependency,
                "Converted entry no longer references the joint interface 'WindowsFormsIntegration:System.Windows.Forms.Integration.WindowsFormsHost': the bridge contract is broken."));
        if (!module.Types.Any(t => t.FullName == "TestApp.MainWindow" &&
                (t.BaseType?.Scope as AssemblyNameReference)?.Name == EngineAssembly) &&
            !refs.Any(s => s.FullName == "TestApp.MainWindow" && s.Scope == EngineAssembly))
            gaps.Add(new JointGap(EntryAssembly, JointGap.MissingDependency,
                "Converted TestApp.MainWindow does not derive from the WPF engine: the WPF pass missed the entry visual."));
        return gaps;
    }

    /// <summary>
    /// Embedded loose-XAML resource name for the entry app root. Mirrors
    /// <c>WpfAppBoot.ResolveSource</c> exactly: pack URI
    /// "/{Name};V{Version};component/app.xaml" with '/' to '.', leading
    /// dots trimmed, lowercased. The loader prefers this loose form over
    /// the BAML app root.
    /// </summary>
    public static string LooseAppXamlName(string assemblyName, string assemblyVersion)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyName);
        ArgumentException.ThrowIfNullOrEmpty(assemblyVersion);
        return ("/" + assemblyName + ";V" + assemblyVersion + ";component/app.xaml")
            .Replace('/', '.').TrimStart('.').ToLowerInvariant();
    }

    /// <summary>
    /// Embeds a minimal loose app.xaml for the entry root, derived from the
    /// real app.baml bytes (ShutdownMode evidence), so the loader's
    /// app-root hydration has a loose form to prefer. The generated
    /// InitializeComponent sets StartupUri in code, so the loose form
    /// carries only identity (x:Class) plus the BAML-evidenced
    /// ShutdownMode. App-level Resources are NOT carried by this form
    /// (recorded receipt gap; BAML app-root engine support is the
    /// follow-up). Returns a precise gap instead of inventing content when
    /// the BAML evidence is absent or ambiguous.
    /// </summary>
    private static JointGap? TryEmbedLooseAppXaml(ModuleDefinition module, JointNode node)
    {
        var looseName = LooseAppXamlName(node.Name, node.Version);
        var resources = module.Resources
            .OfType<EmbeddedResource>()
            .FirstOrDefault(r => r.Name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase));
        if (resources is null)
            return new JointGap(node.Name, JointGap.MissingDependency,
                "Entry has no .g.resources: cannot derive loose app.xaml from app.baml evidence.");
        byte[]? appBaml = null;
        using (var reader = new System.Resources.ResourceReader(new MemoryStream(resources.GetResourceData())))
        {
            foreach (System.Collections.DictionaryEntry entry in reader)
            {
                if ((entry.Key as string)?.EndsWith("app.baml", StringComparison.OrdinalIgnoreCase) == true)
                {
                    appBaml = entry.Value switch
                    {
                        byte[] b => b,
                        Stream s => ReadAllBytes(s),
                        _ => null,
                    };
                    break;
                }
            }
        }
        if (appBaml is null)
            return new JointGap(node.Name, JointGap.MissingDependency,
                "Entry .g.resources has no app.baml: cannot derive loose app.xaml from BAML evidence.");
        var mode = ExtractShutdownMode(appBaml);
        if (mode is null)
            return new JointGap(node.Name, JointGap.MissingDependency,
                "app.baml carries no unambiguous ShutdownMode: refusing to invent the loose app.xaml policy.");
        var xaml =
            $"<Application xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            $"xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
            $"x:Class=\"{node.Name}.App\" ShutdownMode=\"{mode}\" />";
        module.Resources.Add(new EmbeddedResource(
            looseName, ManifestResourceAttributes.Public,
            System.Text.Encoding.UTF8.GetBytes(xaml)));
        return null;
    }

    /// <summary>
    /// Reads the ShutdownMode policy out of real app.baml bytes: the
    /// attribute name plus exactly one recognized policy value nearby.
    /// Anything else is null (ambiguous or absent: never guess).
    /// </summary>
    public static string? ExtractShutdownMode(byte[] appBaml)
    {
        ArgumentNullException.ThrowIfNull(appBaml);
        var text = System.Text.Encoding.ASCII.GetString(appBaml);
        var marker = text.IndexOf("ShutdownMode", StringComparison.Ordinal);
        if (marker < 0) return null;
        var window = text.Substring(marker, Math.Min(256, text.Length - marker));
        string? found = null;
        foreach (var candidate in new[] { "OnLastWindowClose", "OnMainWindowClose", "OnExplicitShutdown" })
        {
            if (window.Contains(candidate, StringComparison.Ordinal))
            {
                if (found is not null) return null;
                found = candidate;
            }
        }
        return found;
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static IEnumerable<TypeReference> CollectAllTypeRefs(ModuleDefinition module)
    {
        var roots = new List<TypeReference?>();
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            foreach (var nested in type.NestedTypes) stack.Push(nested);
            roots.Add(type.BaseType);
            foreach (var iface in type.Interfaces) roots.Add(iface.InterfaceType);
            foreach (var field in type.Fields) roots.Add(field.FieldType);
            foreach (var method in type.Methods)
            {
                roots.Add(method.ReturnType);
                foreach (var parameter in method.Parameters) roots.Add(parameter.ParameterType);
                foreach (var gp in method.GenericParameters)
                    foreach (var c in gp.Constraints) roots.Add(c.ConstraintType);
                if (method.Body is null) continue;
                foreach (var variable in method.Body.Variables) roots.Add(variable.VariableType);
                foreach (var handler in method.Body.ExceptionHandlers) roots.Add(handler.CatchType);
                foreach (var instruction in method.Body.Instructions)
                {
                    switch (instruction.Operand)
                    {
                        case TypeReference operandType:
                            roots.Add(operandType);
                            break;
                        case MemberReference member:
                            roots.Add(member.DeclaringType);
                            switch (member)
                            {
                                case MethodReference methodRef:
                                    roots.Add(methodRef.ReturnType);
                                    foreach (var p in methodRef.Parameters) roots.Add(p.ParameterType);
                                    break;
                                case FieldReference fieldRef:
                                    roots.Add(fieldRef.FieldType);
                                    break;
                            }
                            break;
                    }
                }
            }
            foreach (var property in type.Properties) roots.Add(property.PropertyType);
            foreach (var evt in type.Events) roots.Add(evt.EventType);
            foreach (var gp in type.GenericParameters)
                foreach (var c in gp.Constraints) roots.Add(c.ConstraintType);
            foreach (var attribute in type.CustomAttributes) roots.Add(attribute.AttributeType);
            foreach (var field in type.Fields)
                foreach (var attribute in field.CustomAttributes) roots.Add(attribute.AttributeType);
            foreach (var method in type.Methods)
                foreach (var attribute in method.CustomAttributes) roots.Add(attribute.AttributeType);
        }
        foreach (var attribute in module.CustomAttributes) roots.Add(attribute.AttributeType);
        // Assembly-level attributes participate in routing too: unwalked
        // rows used to slip through (the joint entry's ThemeInfo poison).
        // Inert WPF markers are stripped before routing (see
        // StripInertWpfAssemblyAttributes); anything left here routes or
        // gaps honestly instead of shipping unscoped.
        foreach (var attribute in module.Assembly.CustomAttributes) roots.Add(attribute.AttributeType);

        var seen = new HashSet<TypeReference>();
        foreach (var root in roots)
            foreach (var leaf in Leaves(root, seen))
                yield return leaf;
    }

    private static IEnumerable<TypeReference> Leaves(TypeReference? reference, HashSet<TypeReference> seen)
    {
        if (reference is null || !seen.Add(reference)) yield break;
        switch (reference)
        {
            case GenericInstanceType generic:
                foreach (var leaf in Leaves(generic.ElementType, seen)) yield return leaf;
                foreach (var argument in generic.GenericArguments)
                    foreach (var leaf in Leaves(argument, seen)) yield return leaf;
                break;
            case TypeSpecification specification:
                foreach (var leaf in Leaves(specification.ElementType, seen)) yield return leaf;
                break;
            default:
                yield return reference;
                break;
        }
    }

    private static void WriteManifest(string outputDirectory, ApplicationPackage manifest)
    {
        var pending = Path.Combine(outputDirectory, ".manifest-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(pending, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(pending, Path.Combine(outputDirectory, "app-manifest.json"), true);
    }

    private static void WriteReceipt(
        string outputDirectory, BuiltJointClosure closure, ApplicationPackage manifest,
        IReadOnlyList<ConvertedNode> converted, IReadOnlyList<ProvidedReference> frameworkProvided,
        IReadOnlyDictionary<string, string> providerAssemblyPaths)
    {
        var receipt = new
        {
            slug = Slug,
            entryAssembly = EntryAssembly,
            mapVersion = MapVersion,
            builtAtUtc = DateTime.UtcNow.ToString("o"),
            hostFramework = RuntimeInformation.FrameworkDescription,
            status = manifest.Status,
            closureNodes = closure.Nodes.Select(n => new
            {
                name = n.Name,
                version = n.Version,
                sha256 = n.Sha256,
                capabilities = n.Capabilities.ToString(),
                disposition = "packaged",
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
                looseAppXaml = c.LooseAppXaml,
            }).ToArray(),
            frameworkProvided = frameworkProvided.Select(p => new { name = p.Name, provider = p.Provider, detail = p.Detail }).ToArray(),
            bridgeProvider = new
            {
                assembly = "WindowsFormsIntegration",
                resolver = "DesktopWasm.Interop.WindowsFormsHostResolver",
                hostType = "System.Windows.Forms.Integration.WindowsFormsHost",
                childContract = "WfWasm.WinForms:System.Windows.Forms.Control (real fork control, not the engine IImmediatePaintable stub)",
                registration = "DesktopWasm.BrowserHost.InteropBridgeRegistration.EnsureRegistered (AppDomain.AssemblyResolve, joint-interface name only)",
            },
            drawingProvider = new
            {
                assembly = "WfWasm.Drawing",
                detail = "System.Drawing.* member references resolve here: the fork's own binaries scope their drawing references to this assembly and the served bundle ships it. Verified per member against the real provider binary.",
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
                "Converted images preserve all manifest resources (BAML .g.resources, theme assets) byte-for-byte: only type-reference scopes/namespaces move, plus the derived loose app.xaml embedded resource on the entry.",
                "The loose app.xaml carries only identity (x:Class) plus the BAML-evidenced ShutdownMode; the generated InitializeComponent sets StartupUri in code. App-level Resources (one AvalonDock floating-window style) are NOT carried by the loose form: BAML app-root engine support is the follow-up; floating-window chrome may differ.",
                "WPF-routed references additionally move into the engine namespace (DesktopWasm.Wpf.*): the engine declares no System.Windows.* Window/Application/Control types. Existence-verified per member against the real engine binary.",
                "Loose XAML is unavailable upstream, so boot resolves StartupUri through preserved BAML via WpfAppBoot (same treatment as prior avalondock conversion).",
                "Method-body call compatibility (signatures on renamed engine types) is engine fidelity, proven by browser boot (verifier lane), not by this static closure.",
            },
        };
        File.WriteAllText(
            Path.Combine(outputDirectory, "testapp-joint.receipt.json"),
            JsonSerializer.Serialize(receipt, JsonOptions));
    }

    private static void WriteProvenance(
        string outputDirectory, string stagedDirectory, ApplicationPackage manifest,
        IReadOnlyList<ConvertedNode> converted)
    {
        var files = Directory.GetFiles(outputDirectory)
            .Select(Path.GetFileName)
            .OfType<string>()
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
            sourceProvenance = Path.Combine(stagedDirectory, "provenance.json"),
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
