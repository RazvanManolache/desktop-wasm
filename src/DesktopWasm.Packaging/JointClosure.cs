using System.Security.Cryptography;
using Mono.Cecil;

namespace DesktopWasm.Packaging;

/// <summary>One closure node: logical identity plus content evidence.</summary>
public sealed record JointNode(string Name, string Version, string Sha256, string Path, FrameworkCapabilities Capabilities);

/// <summary>Built joint closure: nodes plus the visible gaps that fail the build.</summary>
public sealed record BuiltJointClosure(IReadOnlyList<JointNode> Nodes, IReadOnlyList<JointGap> Gaps)
{
    public bool Ok => Gaps.Count == 0;
}

/// <summary>
/// Builds the unified selected-app closure over one entry assembly: both
/// frameworks compose over a single dependency graph with one shared
/// engine/native payload. Identity is (name + version + content hash);
/// conflicts reject visibly, never by "first file wins".
/// </summary>
public static class JointClosure
{
    /// <summary>
    /// Assemblies the runtime already provides: never packaged, never
    /// rewritten. Union of the BCL set with both engine payloads.
    /// </summary>
    public static IReadOnlySet<string> ProvidedAssemblies(string runtimeDirectory)
    {
        var provided = Directory.GetFiles(runtimeDirectory, "*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
        provided.UnionWith([
            "DesktopWasm.Core", "DesktopWasm.Graphics", "DesktopWasm.Wpf",
            "WfWasm.WinForms", "WfWasm.Shim", "WfWasm.Drawing", "WfWasm.Rendering",
            "Microsoft.Win32.SystemEvents",
            "SkiaSharp", "SkiaSharp.HarfBuzz", "HarfBuzzSharp",
        ]);
        return provided;
    }

    public static BuiltJointClosure Build(string entryPath, IEnumerable<string> searchDirectories, IReadOnlySet<string>? provided = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryPath);
        entryPath = Path.GetFullPath(entryPath);
        if (!File.Exists(entryPath))
            return new BuiltJointClosure([], [new JointGap(Path.GetFileName(entryPath), JointGap.MissingDependency, $"Entry assembly not found: {entryPath}")]);

        var searches = searchDirectories.Select(Path.GetFullPath).ToArray();
        provided ??= ProvidedAssemblies(Path.GetDirectoryName(typeof(object).Assembly.Location)!);

        var nodes = new List<JointNode>();
        var gaps = new List<JointGap>();
        // Logical identity -> node: same name twice must agree on version
        // AND content hash, or the closure fails instead of picking one.
        var seen = new Dictionary<string, JointNode>(StringComparer.OrdinalIgnoreCase);
        // Referrer-relative probing first (mirrors CLR layout: a dependency
        // beside its referrer wins over the shared search roots), so genuine
        // version/content splits between sibling folders surface instead of
        // collapsing onto whichever file a global lookup finds first.
        var queue = new Queue<(string Path, string ReferrerDir)>();
        queue.Enqueue((entryPath, Path.GetDirectoryName(entryPath)!));

        var resolver = new DefaultAssemblyResolver();
        foreach (var dir in searches) resolver.AddSearchDirectory(dir);
        resolver.AddSearchDirectory(Path.GetDirectoryName(entryPath)!);

        while (queue.TryDequeue(out var item))
        {
            var (current, referrerDir) = item;
            AssemblyDefinition assembly;
            try
            {
                assembly = AssemblyDefinition.ReadAssembly(current, new ReaderParameters { AssemblyResolver = resolver });
            }
            catch (Exception error)
            {
                gaps.Add(new JointGap(Path.GetFileName(current), "rewrite-error", error.GetBaseException().Message));
                continue;
            }
            using (assembly)
            {
                var name = assembly.Name.Name;
                var version = assembly.Name.Version?.ToString() ?? "0.0.0.0";
                var hash = HashOf(current);
                if (seen.TryGetValue(name, out var existing))
                {
                    if (!existing.Version.Equals(version, StringComparison.Ordinal))
                        gaps.Add(new JointGap(name, JointGap.VersionConflict,
                            $"Version split: '{existing.Path}' is {existing.Version}, '{current}' is {version}. Pin one version."));
                    else if (!existing.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase))
                        gaps.Add(new JointGap(name, JointGap.HashMismatch,
                            $"Content split: '{existing.Path}' and '{current}' share version {version} with different bytes. Pin one file."));
                    continue;
                }
                var node = new JointNode(name, version, hash, current, JointCapabilities.Detect(assembly));
                seen[name] = node;
                nodes.Add(node);

                foreach (var reference in assembly.MainModule.AssemblyReferences)
                {
                    // Transform-owned framework references are rewritten by
                    // the composed passes, never packaged from disk and
                    // never demanded as files: only private closure members
                    // below can produce missing-dependency gaps.
                    if (provided.Contains(reference.Name) || seen.ContainsKey(reference.Name) ||
                        JointMap.WpfSourceAssemblies.Contains(reference.Name) ||
                        JointMap.WinFormsSourceAssemblies.Contains(reference.Name) ||
                        JointMap.RuntimeProvidedJointInterfaces.Contains(reference.Name)) continue;
                    if (reference.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || reference.Name.Contains('\\'))
                    { gaps.Add(new JointGap(name, JointGap.DependencyName, reference.Name)); continue; }
                    var candidate = new[] { referrerDir }.Concat(searches)
                        .Select(dir => Path.Combine(dir, reference.Name + ".dll"))
                        .FirstOrDefault(File.Exists);
                    if (candidate is not null) queue.Enqueue((candidate, Path.GetDirectoryName(candidate)!));
                    else gaps.Add(new JointGap(name, JointGap.MissingDependency,
                        $"Referenced assembly '{reference.Name}' (v{reference.Version}) is absent from mapped inputs: add a new mapped/pinned input, never a mutable old fallback."));
                }
            }
        }
        resolver.Dispose();
        return new BuiltJointClosure(nodes, gaps.Distinct().ToArray());
    }

    /// <summary>
    /// Verifies staged payload bytes against a built closure: same
    /// (name, version, hash), shared payload present exactly once.
    /// </summary>
    public static IReadOnlyList<JointGap> VerifyPayload(BuiltJointClosure closure, string stageDirectory)
    {
        var gaps = new List<JointGap>();
        // Exact assembly-name match (file name without extension): a prefix
        // match confuses 'AvalonDock' with 'AvalonDock.Core' and its other
        // sibling assemblies in a real multi-assembly closure.
        static bool IsPayloadFor(string? file, string name) =>
            file is not null && Path.GetFileNameWithoutExtension(file)
                .Equals(name, StringComparison.OrdinalIgnoreCase);
        var files = Directory.Exists(stageDirectory)
            ? Directory.GetFiles(stageDirectory)
            : [];
        foreach (var node in closure.Nodes)
        {
            var matches = files.Where(f => IsPayloadFor(Path.GetFileName(f), node.Name)).ToList();
            if (matches.Count == 0)
            {
                gaps.Add(new JointGap(node.Name, JointGap.MissingDependency, $"Staged payload missing for '{node.Name}'."));
                continue;
            }
            if (matches.Count > 1)
            {
                gaps.Add(new JointGap(node.Name, JointGap.HashMismatch,
                    $"Assembly '{node.Name}' staged {matches.Count} times ({string.Join(", ", matches.Select(Path.GetFileName))}): one payload per package."));
                continue;
            }
            var hash = HashOf(matches[0]);
            if (!hash.Equals(node.Sha256, StringComparison.OrdinalIgnoreCase))
                gaps.Add(new JointGap(node.Name, JointGap.HashMismatch, $"Staged '{Path.GetFileName(matches[0])}' does not match closure hash {node.Sha256}."));
        }
        // One shared payload per package: a shared-payload node staged under
        // more than one file name is a duplication defect, not a second copy.
        var sharedNames = closure.Nodes
            .Where(n => JointMap.SharedPayloadAssemblies.Contains(n.Name))
            .Select(n => n.Name)
            .ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in sharedNames)
        {
            var copies = files.Where(f => IsPayloadFor(Path.GetFileName(f), name)).ToList();
            if (copies.Count > 1)
                gaps.Add(new JointGap(name, JointGap.HashMismatch,
                    $"Shared payload '{name}' staged {copies.Count} times ({string.Join(", ", copies.Select(Path.GetFileName))}): one payload per package."));
        }
        return gaps;
    }

    public static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

