using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Convert;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("DesktopWasm.Convert: real-WPF-app triage and conversion (shim, not port).");
    Console.WriteLine("  scan <app.dll>                                   list refs, pinvokes, resources, WPF markers");
    Console.WriteLine("  convert <app.dll> --source <dir> --out <dir> [--commit <sha>]");
    Console.WriteLine("  package <app.dll> --source <project-dir> --root <checkout> --out <app-dir> --slug <id> [--commit <sha>]");
    Console.WriteLine("      rewrite framework refs onto the engine, embed loose .xaml, write receipt");
    Console.WriteLine("  boot <converted.dll>                             desktop oracle: boot and describe the main window");
    Console.WriteLine("  press <converted.dll> <label...>                 desktop oracle: RaiseClick buttons by label, print TextBox texts");
    return 2;
}

try
{
    switch (args[0])
    {
        case "package" when args.Length >= 2:
        {
            var source = Option("--source"); var output = Option("--out"); var slug = Option("--slug");
            if (source is null || output is null || slug is null) return 2;
            var package = ApplicationPackager.Prepare(args[1], source, Option("--root", required: false) ?? source,
                output, slug, Option("--commit", required: false));
            Console.WriteLine($"{slug}: {package.Status}; {package.Assemblies.Length} referenced assemblies; {package.Gaps.Length} gaps.");
            foreach (var gap in package.Gaps.Take(25)) Console.WriteLine($"[{gap.Assembly}] {gap.Kind}: {gap.Detail}");
            return package.Status == "converted" ? 0 : 1;
        }
        case "scan" when args.Length == 2:
        {
            var report = AppScanner.Scan(args[1]);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        case "convert" when args.Length >= 2:
        {
            var source = Option("--source");
            var output = Option("--out");
            var commit = Option("--commit", required: false);
            if (source is null || output is null)
            {
                Console.Error.WriteLine("convert needs --source <loose-xaml dir> and --out <dir>.");
                return 2;
            }
            var (ok, gaps, receipt) = AssemblyConverter.TryConvert(args[1], source, output, commit);
            if (!ok)
            {
                Console.Error.WriteLine($"CONVERT GAPS ({gaps.Count}): the engine has no counterpart for:");
                foreach (var gap in gaps)
                    Console.Error.WriteLine($"  [{gap.Kind}] {gap.Detail}");
                return 1;
            }
            Console.WriteLine($"Converted {receipt!.App} -> {output} (map {receipt.MapVersion}).");
            foreach (var adaptation in receipt.Adaptations)
                Console.WriteLine($"  adaptation: {adaptation}");
            Console.WriteLine($"  sha256 {receipt.InputSha256}");
            Console.WriteLine($"  embedded: {string.Join(", ", receipt.EmbeddedXaml)}");
            foreach (var warning in receipt.Warnings)
                Console.WriteLine($"  warning: {warning}");
            return 0;
        }
        case "boot" when args.Length == 2:
        {
            PreloadStagedSiblings(args[1]);
            var window = DesktopWasm.Wpf.WpfAppBoot.Boot(File.ReadAllBytes(args[1]));
            Console.WriteLine($"Booted {window.GetType().FullName}: Title='{window.Title}' {window.Width}x{window.Height}");
            Console.WriteLine($"  Content={window.Content?.GetType().Name ?? "<none>"}");
            if (window.Content is DesktopWasm.Wpf.FrameworkElement fe)
            {
                foreach (var child in fe.LogicalChildren)
                    Console.WriteLine($"    - {child.GetType().Name}");
            }
            return 0;
        }
        case "press" when args.Length >= 3:
        {
            PreloadStagedSiblings(args[1]);
            var window = DesktopWasm.Wpf.WpfAppBoot.Boot(File.ReadAllBytes(args[1]));
            foreach (var label in args[2..])
            {
                var button = FindButton(window, label)
                    ?? throw new InvalidOperationException($"No button labeled '{label}'.");
                button.RaiseClick();
            }
            foreach (var text in FindTexts(window))
                Console.WriteLine($"TEXT {text}");
            return 0;
        }
        default:
            Console.Error.WriteLine("Unknown command. Use scan, convert, boot, or press.");
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.GetBaseException()}");
    return 2;
}

void PreloadStagedSiblings(string entryPath)
{
    // Desktop oracle parity with the staged boot tests: a staged app dir
    // (app-manifest.json next to the entry image) preloads every converted
    // sibling and resolves simple-name references onto the staged bytes,
    // so multi-assembly entries boot exactly as the tests load them.
    var directory = Path.GetDirectoryName(Path.GetFullPath(entryPath));
    if (directory is null) return;
    var manifestPath = Path.Combine(directory, "app-manifest.json");
    if (!File.Exists(manifestPath)) return;
    using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
    var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
    {
        var file = assembly.GetProperty("file").GetString()!;
        var expected = assembly.GetProperty("sha256").GetString()!;
        var bytes = File.ReadAllBytes(Path.Combine(directory, file));
        var actual = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"staged hash mismatch in '{file}': {actual} != {expected}");
        images[assembly.GetProperty("name").GetString()!] = bytes;
    }
    AppDomain.CurrentDomain.AssemblyResolve += (_, resolvedArgs) =>
    {
        var requested = new AssemblyName(resolvedArgs.Name).Name;
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, requested, StringComparison.OrdinalIgnoreCase));
        if (loaded is not null) return loaded;
        if (requested is not null && images.TryGetValue(requested, out var image))
            return Assembly.Load(image);
        return null;
    };
    foreach (var image in images.Values)
        Assembly.Load(image);
    Console.WriteLine($"staged preload: {images.Count} assemblies from {manifestPath}");
}
string? Option(string name, bool required = true)
{
    for (var i = 2; i + 1 < args.Length; i++)
    {
        if (args[i] == name) return args[i + 1];
    }
    if (required) Console.Error.WriteLine($"Missing {name} <value>.");
    return null;
}

DesktopWasm.Wpf.ButtonBase? FindButton(DesktopWasm.Wpf.UIElement root, string label)
{
    if (root is DesktopWasm.Wpf.ButtonBase button && Equals(button.Content?.ToString(), label))
        return button;
    if (root is DesktopWasm.Wpf.FrameworkElement fe)
    {
        foreach (var child in fe.LogicalChildren)
        {
            var found = FindButton(child, label);
            if (found is not null) return found;
        }
    }
    return null;
}

IEnumerable<string> FindTexts(DesktopWasm.Wpf.UIElement root)
{
    if (root is DesktopWasm.Wpf.TextBox box)
        yield return box.Text;
    if (root is DesktopWasm.Wpf.FrameworkElement fe)
    {
        foreach (var child in fe.LogicalChildren)
        {
            foreach (var text in FindTexts(child))
                yield return text;
        }
    }
}
