namespace DesktopWasm.Core;

// Session-scoped virtual file store for browser-mediated file dialogs.
// Real WPF dialogs return host paths; the browser cannot expose them, so
// picked bytes land under declared mounts ("uploads/report.txt") and every
// dialog stream resolves through the active session instance. Only paths
// under a declared mount are visible: absolute, drive-rooted and ".."
// paths are precise errors, never silent remaps. Hosts replace Current per
// session; tests save/restore it around isolated instances.
public sealed class WasmVirtualFileSystem
{
    private readonly Dictionary<string, Dictionary<string, byte[]>> _mounts = new(StringComparer.OrdinalIgnoreCase);

    public static WasmVirtualFileSystem Current { get; set; } = new();

    public IReadOnlyCollection<string> Mounts => _mounts.Keys;

    // Declares an empty mount (idempotent: re-mounting keeps existing files).
    public void Mount(string name)
    {
        ValidateMountName(name);
        _mounts.TryAdd(name, new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase));
    }

    public bool Unmount(string name) => _mounts.Remove(name);

    public void Clear() => _mounts.Clear();

    public void WriteFile(string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var (mount, relative) = Split(path);
        Lookup(mount)[relative] = (byte[])bytes.Clone();
    }

    public void WriteText(string path, string text) =>
        WriteFile(path, System.Text.Encoding.UTF8.GetBytes(text));

    public bool Exists(string path)
    {
        var (mount, relative) = Split(path);
        return Lookup(mount).TryGetValue(relative, out _);
    }

    // Reads staged bytes (missing file is FileNotFoundException, undeclared
    // mount or escaping path is InvalidOperationException).
    public byte[] ReadFile(string path)
    {
        var (mount, relative) = Split(path);
        if (!Lookup(mount).TryGetValue(relative, out var bytes))
            throw new FileNotFoundException($"No staged file '{path}' in virtual mount '{mount}'.", path);
        return (byte[])bytes.Clone();
    }

    public Stream OpenRead(string path) => new MemoryStream(ReadFile(path), writable: false);

    // Writable stream that commits into the mount on Dispose (save-dialog
    // semantics); the parent mount must already be declared.
    public Stream OpenWrite(string path)
    {
        var (mount, relative) = Split(path);
        _ = Lookup(mount);
        return new CommitOnDisposeStream(bytes => Lookup(mount)[relative] = bytes);
    }

    public IReadOnlyList<string> ListFiles(string mount)
    {
        ValidateMountName(mount);
        if (!_mounts.TryGetValue(mount, out var files))
            throw new InvalidOperationException($"Virtual mount '{mount}' is not declared.");
        return files.Keys.Select(relative => mount + "/" + relative).ToList();
    }

    private Dictionary<string, byte[]> Lookup(string mount)
    {
        if (!_mounts.TryGetValue(mount, out var files))
            throw new InvalidOperationException(
                $"Virtual mount '{mount}' is not declared: stage picked bytes with WriteFile before opening.");
        return files;
    }

    private (string Mount, string Relative) Split(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new InvalidOperationException("Virtual file path is empty.");
        if (path.StartsWith('/') || path.StartsWith('\\') || path.Contains(':') || path.Contains('\\'))
            throw new InvalidOperationException(
                $"Virtual file path '{path}' must be mount-relative ('mount/relative/name'), not absolute.");
        var slash = path.IndexOf('/');
        if (slash <= 0)
            throw new InvalidOperationException(
                $"Virtual file path '{path}' must name a declared mount ('mount/relative/name').");
        var mount = path[..slash];
        var relative = path[(slash + 1)..];
        ValidateMountName(mount);
        if (relative.Length == 0 || relative.Split('/').Any(segment =>
                segment.Length == 0 || segment == "." || segment == ".." ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidOperationException($"Virtual file path '{path}' escapes its mount.");
        return (mount, relative);
    }

    private static void ValidateMountName(string mount)
    {
        if (string.IsNullOrEmpty(mount) || mount == "." || mount == ".." ||
            mount.Any(c => c == '/' || c == '\\' || c == ':' || char.IsWhiteSpace(c) ||
                           Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0))
            throw new ArgumentException($"Invalid virtual mount name '{mount}'.", nameof(mount));
    }

    private sealed class CommitOnDisposeStream : MemoryStream
    {
        private readonly Action<byte[]> _commit;
        private bool _committed;

        public CommitOnDisposeStream(Action<byte[]> commit) => _commit = commit;

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_committed)
            {
                _committed = true;
                _commit(ToArray());
            }
            base.Dispose(disposing);
        }
    }
}
