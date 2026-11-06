using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf;

/// <summary>Fetches the selected package's reference closure; resolves assemblies on first use.</summary>
public sealed class ApplicationPackageLoader : IDisposable
{
    private readonly ApplicationPackage _package;
    private readonly Dictionary<string, byte[]> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Assembly> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly PackageLoadContext _context;
    private readonly Func<string, Assembly?> _packResolver;
    private bool _disposed;

    private ApplicationPackageLoader(ApplicationPackage package)
    {
        _package = package;
        _context = new PackageLoadContext(this);
        _packResolver = Load;
    }

    public static async Task<ApplicationPackageLoader> CreateAsync(ApplicationPackage package, string slug,
        Func<string, Task<byte[]>> fetch, Func<string, Task>? report = null, Func<string, Task>? reportStart = null)
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
            byte[] bytes;
            try
            {
                // Fetch-start phase (tiered-timer contract): emitted after
                // the semaphore acquire, before the fetch, so no inter-file
                // gap is silent even when a fetch stalls.
                if (reportStart is not null)
                    await reportStart($"fetch-start {assembly.Name}");
                bytes = await fetch(assembly.File);
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), assembly.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{slug}: hash mismatch in '{assembly.Name}'.");
            }
            finally { concurrency.Release(); }
            // Released before the verify report: a stalled report callback
            // (JS interop round-trip) must never pin a fetch slot and
            // serialize the tail. Fetch concurrency stays capped at 4.
            if (report is not null)
                await report($"{assembly.Name} ({bytes.Length / 1024} KB fetched, hash verified)");
            return (assembly.Name, Bytes: bytes);
        }));
        foreach (var item in images) loader._images.Add(item.Name, item.Bytes);
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

    // Resolve the verified private closure before process-wide handlers or
    // another session's images. Engine/framework assemblies fall through to
    // the default context so every converted control shares the host contracts.
    private sealed class PackageLoadContext(ApplicationPackageLoader owner)
        : AssemblyLoadContext(isCollectible: false)
    {
        protected override Assembly? Load(AssemblyName name) =>
            name.Name is { } simple ? owner.Load(simple) : null;
    }

    private Assembly? Load(string name)
    {
        if (_loaded.TryGetValue(name, out var assembly)) return assembly;
        if (!_images.TryGetValue(name, out var image)) return null;
        using var stream = new MemoryStream(image, writable: false);
        assembly = _context.LoadFromStream(stream);
        if (assembly.GetName().Name != name) throw new InvalidDataException($"Package image identity does not match '{name}'.");
        _loaded.Add(name, assembly);
        return assembly;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (ReferenceEquals(WpfAppBoot.PackAssemblyResolver, _packResolver))
            WpfAppBoot.PackAssemblyResolver = null;
        _images.Clear(); _loaded.Clear();
    }
}
