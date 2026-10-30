using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf;

/// <summary>Fetches the selected package's reference closure; resolves assemblies on first use.</summary>
public sealed class ApplicationPackageLoader : IDisposable
{
    private readonly ApplicationPackage _package;
    private readonly Dictionary<string, byte[]> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Assembly> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly ResolveEventHandler _resolve;
    private readonly Func<string, Assembly?> _packResolver;
    private bool _disposed;

    private ApplicationPackageLoader(ApplicationPackage package)
    {
        _package = package;
        _resolve = (_, args) => Load(new AssemblyName(args.Name).Name!);
        _packResolver = Load;
    }

    public static async Task<ApplicationPackageLoader> CreateAsync(ApplicationPackage package, string slug,
        Func<string, Task<byte[]>> fetch, Func<string, Task>? report = null)
    {
        package.Validate(slug, WpfAppBoot.MapVersion);
        if (package.Status != "converted")
            throw new InvalidOperationException($"{slug}: {package.Status}; {package.Gaps.Length} concrete preparation/conversion gaps. " +
                string.Join(" | ", package.Gaps.Take(20).Select(g => $"{g.Assembly} [{g.Kind}]: {g.Detail}")));
        var loader = new ApplicationPackageLoader(package);
        using var concurrency = new SemaphoreSlim(4);
        var images = await Task.WhenAll(package.Assemblies.Select(async assembly =>
        {
            await concurrency.WaitAsync();
            try
            {
                var bytes = await fetch(assembly.File);
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), assembly.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{slug}: hash mismatch in '{assembly.Name}'.");
                if (report is not null)
                    await report($"{assembly.Name} ({bytes.Length / 1024} KB fetched, hash verified)");
                return (assembly.Name, Bytes: bytes);
            }
            finally { concurrency.Release(); }
        }));
        foreach (var item in images) loader._images.Add(item.Name, item.Bytes);
        AppDomain.CurrentDomain.AssemblyResolve += loader._resolve;
        WpfAppBoot.PackAssemblyResolver = loader._packResolver;
        return loader;
    }

    public Window Boot(Action<string>? report = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        report?.Invoke("boot: loading entry image");
        var entry = Load(_package.EntryAssembly)
            ?? throw new InvalidDataException("Package entry image is missing.");
        return WpfAppBoot.Boot(entry, report);
    }

    // Identities shared across loaders in one process (the test AppDomain
    // boots several staged packages): byte-identical images resolve to one
    // Assembly so converted-type lookup never sees phantom duplicates.
    // Different bytes keep separate identities (a real conflict still throws
    // at resolution). Assemblies cannot unload, so entries live as long as
    // the process, like the images themselves.
    private static readonly ConcurrentDictionary<string, (string Sha256, Assembly Assembly)> _sharedImages = new();

    private Assembly? Load(string name)
    {
        if (_loaded.TryGetValue(name, out var assembly)) return assembly;
        if (!_images.TryGetValue(name, out var image)) return null;
        var sha = _package.Assemblies.FirstOrDefault(a => a.Name == name)?.Sha256;
        if (sha is not null && _sharedImages.TryGetValue(name, out var shared) && shared.Sha256 == sha)
        {
            _loaded.Add(name, shared.Assembly);
            return shared.Assembly;
        }
        assembly = Assembly.Load(image);
        if (assembly.GetName().Name != name) throw new InvalidDataException($"Package image identity does not match '{name}'.");
        if (sha is not null) _sharedImages.TryAdd(name, (sha, assembly));
        _loaded.Add(name, assembly);
        return assembly;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AppDomain.CurrentDomain.AssemblyResolve -= _resolve;
        if (ReferenceEquals(WpfAppBoot.PackAssemblyResolver, _packResolver))
            WpfAppBoot.PackAssemblyResolver = null;
        _images.Clear(); _loaded.Clear();
    }
}
