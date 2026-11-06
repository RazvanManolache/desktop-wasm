using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using DesktopWasm.Core;
using Mono.Cecil;

namespace DesktopWasm.Convert;

/// <summary>Converts and stages only the entry's recursively referenced local assemblies.</summary>
public static class ApplicationPackager
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static ApplicationPackage Prepare(string entryPath, string projectDirectory, string sourceRoot,
        string outputDirectory, string slug, string? commit = null)
    {
        if (slug.Length == 0 || slug.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new ArgumentException("Invalid app slug.", nameof(slug));
        entryPath = Path.GetFullPath(entryPath);
        outputDirectory = Path.GetFullPath(outputDirectory);
        if (entryPath.StartsWith(outputDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Package output must not contain the original input.");
        Directory.CreateDirectory(outputDirectory);
        var work = Path.Combine(outputDirectory, ".conversion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var assemblies = new List<PackageAssembly>();
        var gaps = new List<PackageGap>();
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var provided = Directory.GetFiles(runtimeDirectory, "*.dll").Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        provided.UnionWith(["DesktopWasm.Core", "DesktopWasm.Graphics", "DesktopWasm.Wpf", "SkiaSharp", "SkiaSharp.HarfBuzz", "HarfBuzzSharp"]);
        var queue = new Queue<(string Path, bool Entry)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceProjects = FindProjects(sourceRoot);
        queue.Enqueue((entryPath, true));
        string entryName;
        using (var entry = AssemblyDefinition.ReadAssembly(entryPath)) entryName = entry.Name.Name;
        try
        {
            while (queue.TryDequeue(out var input))
            {
                using var original = AssemblyDefinition.ReadAssembly(input.Path);
                var name = original.Name.Name;
                if (name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidDataException("Assembly identity is not a safe file name.");
                if (!visited.Add(name)) continue;
                var source = input.Entry ? projectDirectory : sourceProjects.GetValueOrDefault(name);
                source ??= Path.Combine(work, "empty-source");
                Directory.CreateDirectory(source);
                var convertedDirectory = Path.Combine(work, name);
                (bool Ok, IReadOnlyList<ConvertGap> Gaps, ConvertReceipt? Receipt) result;
                try { result = AssemblyConverter.TryConvert(input.Path, source, convertedDirectory, commit, requireLooseXaml: input.Entry); }
                catch (Exception error) { result = (false, [new ConvertGap("rewrite-error", error.GetBaseException().Message)], null); }
                var references = original.MainModule.AssemblyReferences.Select(r => r.Name).ToArray();
                if (!result.Ok)
                    gaps.AddRange(result.Gaps.Select(g => new PackageGap(name, g.Kind, g.Detail)));
                else
                {
                    var convertedPath = Path.Combine(convertedDirectory, Path.GetFileName(input.Path));
                    using var converted = AssemblyDefinition.ReadAssembly(convertedPath);
                    references = converted.MainModule.AssemblyReferences.Select(r => r.Name).ToArray();
                    var bytes = File.ReadAllBytes(convertedPath);
                    var hash = System.Convert.ToHexStringLower(SHA256.HashData(bytes));
                    var file = PackageAssembly.AssetFileName(name, hash);
                    File.WriteAllBytes(Path.Combine(outputDirectory, file), bytes);
                    assemblies.Add(new PackageAssembly(name, file, hash, references, result.Receipt!.InputSha256));
                }
                foreach (var reference in references)
                {
                    if (provided.Contains(reference) || WpfTypeMap.SourceAssemblies.Contains(reference) || visited.Contains(reference)) continue;
                    if (reference != Path.GetFileName(reference) || reference.Contains('\\'))
                    { gaps.Add(new(name, "dependency-name", reference)); continue; }
                    var dependency = Path.Combine(Path.GetDirectoryName(entryPath)!, reference + ".dll");
                    if (File.Exists(dependency)) queue.Enqueue((dependency, false));
                    else gaps.Add(new(name, "missing-dependency", $"Referenced assembly '{reference}' is absent from upstream build output."));
                }
            }
            var manifest = new ApplicationPackage(1, slug, Wpf.WpfAppBoot.MapVersion,
                gaps.Count == 0 ? "converted" : "conversion-failed", entryName, assemblies.ToArray(), gaps.Distinct().ToArray(), commit);
            manifest.Validate(slug, Wpf.WpfAppBoot.MapVersion);
            WriteManifest(outputDirectory, manifest);
            return manifest;
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    public static void WriteManifest(string outputDirectory, ApplicationPackage manifest)
    {
        Directory.CreateDirectory(outputDirectory);
        var pending = Path.Combine(outputDirectory, ".manifest-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(pending, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(pending, Path.Combine(outputDirectory, "app-manifest.json"), true);
    }

    private static Dictionary<string, string> FindProjects(string root)
    {
        var projects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            if (Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj" or ".git")) continue;
            var xml = XDocument.Load(file);
            var name = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "AssemblyName" && !e.Value.Contains('$'))?.Value;
            projects.TryAdd(name ?? Path.GetFileNameWithoutExtension(file), Path.GetDirectoryName(file)!);
        }
        return projects;
    }
}
