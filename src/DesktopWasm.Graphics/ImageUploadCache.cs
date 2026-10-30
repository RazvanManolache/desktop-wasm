namespace DesktopWasm.Graphics;

/// <summary>
/// Session image-upload cache for the frame transport. Identical pixel
/// buffers are uploaded once (out-of-band def) and repainted by layer
/// reference (blit), so scroll/animation repaints carry small reference ops
/// instead of re-encoding every bitmap to base64 on every publish. Without
/// this, each dirty frame re-sends the full image payload as managed strings
/// (base64 + JSON), which grows the WASM heap toward its fixed cap on long
/// gallery sessions while the JS heap stays flat.
/// </summary>
/// <remarks>
/// Layer ids are content hashes (FNV-1a over dimensions + pixels), so they
/// are deterministic across sessions: a stale browser-side layer is simply
/// overwritten by the next def with the same id, and every dirty frame ends
/// with a retain op naming the live set so both consumers (Canvas2D layers,
/// Skia layers) drop what the session no longer shows. Entries hold the
/// application's own pixel buffers by reference (no copy); total retained
/// bytes and entry count are bounded, with least-recently-used eviction.
/// In-place pixel mutation is safe: changed content hashes to a new id and
/// the stale id leaves the live set (re-upload on next use if revived).
/// Single-threaded like the host that owns it.
/// </remarks>
public sealed class ImageUploadCache
{
    /// <summary>Maximum distinct images retained per session.</summary>
    public const int MaxImages = 512;

    /// <summary>Maximum retained image bytes per session (256 MiB).</summary>
    public const long MaxBytes = 256L * 1024 * 1024;

    private sealed class Entry
    {
        public required string LayerId;
        public required byte[] Rgba;
        public int Width;
        public int Height;
        public long LastFrame;
        public bool Presented;
    }

    private readonly Dictionary<ulong, Entry> _entries = new();
    private long _bytes;
    private long _frames;

    /// <summary>Distinct images currently retained.</summary>
    public int Count => _entries.Count;

    /// <summary>Retained image bytes (references the app's own buffers).</summary>
    public long Bytes => _bytes;

    /// <summary>
    /// Forgets what the sink holds (canvas switch, renderer change, failed
    /// publish): the next frame re-uploads the live set. Entries stay cached
    /// so bytes are never re-hashed or copied.
    /// </summary>
    public void ResetPresented()
    {
        foreach (var entry in _entries.Values) entry.Presented = false;
    }

    /// <summary>
    /// Resolves the layer id for a pixel buffer. <see cref="IsNew"/> is true
    /// when the caller must (re-)upload the bytes: first sight, evicted id,
    /// or an id the consumer dropped via a previous retain.
    /// </summary>
    public (string LayerId, bool IsNew) GetOrAdd(byte[] rgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var hash = Hash(rgba, width, height);
        while (true)
        {
            if (!_entries.TryGetValue(hash, out var existing))
            {
                var id = "img-" + hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
                var entry = new Entry { LayerId = id, Rgba = rgba, Width = width, Height = height, LastFrame = _frames, Presented = true };
                _entries[hash] = entry;
                _bytes += rgba.Length;
                return (id, true);
            }
            if (ReferenceEquals(existing.Rgba, rgba) || PixelsEqual(existing.Rgba, rgba))
            {
                existing.LastFrame = _frames;
                if (existing.Presented) return (existing.LayerId, false);
                existing.Presented = true;
                return (existing.LayerId, true);
            }
            hash ^= 0x9E3779B97F4A7C15UL; // collision: probe the next slot
        }
    }

    /// <summary>
    /// Closes a frame: marks the live set presented, evicts beyond budget
    /// (non-live least-recently-used first, then live least-recently-used),
    /// and un-presents dropped ids so their next use re-uploads.
    /// </summary>
    public void EndFrame(IReadOnlyList<string> liveIds, long frameSequence)
    {
        ArgumentNullException.ThrowIfNull(liveIds);
        _frames = Math.Max(_frames, frameSequence);
        var live = new HashSet<string>(liveIds, StringComparer.Ordinal);
        foreach (var entry in _entries.Values)
            entry.Presented = live.Contains(entry.LayerId);
        while ((_entries.Count > MaxImages || _bytes > MaxBytes) && TryEvict(live))
        {
        }
    }

    private bool TryEvict(HashSet<string> live)
    {
        Entry? victim = null;
        ulong victimKey = 0;
        foreach (var (key, entry) in _entries)
        {
            if (live.Contains(entry.LayerId)) continue;
            if (victim is null || entry.LastFrame < victim.LastFrame) { victim = entry; victimKey = key; }
        }
        if (victim is null)
        {
            foreach (var (key, entry) in _entries)
                if (victim is null || entry.LastFrame < victim.LastFrame) { victim = entry; victimKey = key; }
        }
        if (victim is null) return false;
        _entries.Remove(victimKey);
        _bytes -= victim.Rgba.Length;
        return true;
    }

    private static ulong Hash(byte[] rgba, int width, int height)
    {
        unchecked
        {
            var hash = 14695981039346656037UL;
            hash ^= (ulong)width; hash *= 1099511628211UL;
            hash ^= (ulong)height; hash *= 1099511628211UL;
            hash ^= (ulong)rgba.Length; hash *= 1099511628211UL;
            for (var i = 0; i < rgba.Length; i++) { hash ^= rgba[i]; hash *= 1099511628211UL; }
            return hash;
        }
    }

    private static bool PixelsEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
            if (left[i] != right[i]) return false;
        return true;
    }
}
