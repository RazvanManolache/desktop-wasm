using System.Text.RegularExpressions;
using SkiaSharp;

namespace DesktopWasm.Graphics;

/// <summary>Parsed CSS font shorthand (e.g. "bold 14px sans-serif").</summary>
public sealed record FontSpec(float SizePx, string? Family);

/// <summary>Shaped text metrics plus which engine produced them.</summary>
public sealed record ShapedText(float Width, float Height, int GlyphCount, string Backend);

/// <summary>
/// Managed text shaping shared by WinForms (immediate) and WPF (retained).
/// Primary path is HarfBuzz (correct advances for complex scripts); if a
/// typeface cannot be shaped, measurement falls back to Skia advances and the
/// backend is reported as "skia-fallback" so callers can see it.
/// </summary>
public static class TextShaper
{
    public const string HarfBuzzBackend = "harfbuzz";
    public const string SkiaFallbackBackend = "skia-fallback";
    public const string UnavailableBackend = "unavailable";

    // Once set, Skia natives are absent for the life of the process.
    // Short-circuiting avoids repeat doomed P/Invokes and, more importantly,
    // undisposed native handles whose finalizers would otherwise throw on the
    // GC thread and kill the runtime. (Browser-wasm used to be the main case
    // here; since the GPU spike it ships working natives — see /gpu-spike.)
    private static volatile bool s_nativesMissing;

    private static readonly List<(string Family, byte[] Ttf)> Embedded = new();
    private static readonly object Gate = new();

    /// <summary>
    /// Session typeface reuse for shaping: one native face per family, kept
    /// for the process lifetime. Creating a native typeface per shaped run
    /// (hundreds per frame on gallery-sized trees) churns native font data on
    /// every dirty frame; the retained faces are small and bounded by family
    /// count. Abandoned on re-registration (boot-time only) without disposal
    /// so in-flight shaping can never touch a freed face.
    /// </summary>
    private static readonly Dictionary<string, SKTypeface> ShapeFaces =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Bounded shaped-run cache: shaping is a pure function of
    /// (text, size, family), and layout re-measures the same runs on every
    /// dirty frame. Hits avoid all native shaping work.
    /// </summary>
    private const int MaxShapedRuns = 4096;

    private static readonly Dictionary<ShapedRunKey, ShapedText> ShapedRuns = new();
    private static readonly Queue<ShapedRunKey> ShapedRunOrder = new();

    private readonly record struct ShapedRunKey(string Text, float SizePx, string? Family);

    private static readonly Regex FontCssPattern =
        new(@"(?<size>\d+(?:\.\d+)?)\s*px", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static FontSpec ParseFontCss(string fontCss)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontCss);
        var match = FontCssPattern.Match(fontCss);
        if (!match.Success)
            throw new ArgumentException($"'{fontCss}' has no '<n>px' size.", nameof(fontCss));
        var size = float.Parse(match.Groups["size"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var family = fontCss[(match.Index + match.Length)..].Trim().Trim('\'', '"');
        // A leading comma means the size was preceded by style tokens ("bold 14px ...").
        family = family.TrimStart(',', ' ').Trim();
        return new FontSpec(size, string.IsNullOrEmpty(family) ? null : family);
    }

    /// <summary>Registers an embedded .ttf (tried before system fonts).</summary>
    public static void RegisterEmbeddedFont(byte[] ttf) => RegisterEmbeddedFont(ttf, Array.Empty<string>());

    /// <summary>Registers the same font bytes for shared browser/Skia family aliases.</summary>
    public static void RegisterEmbeddedFont(byte[] ttf, IReadOnlyList<string> aliases)
    {
        ArgumentNullException.ThrowIfNull(ttf);
        ArgumentNullException.ThrowIfNull(aliases);
        // The probe stream must stay alive only for this call; the stored copy
        // backs future per-call faces (MemoryStream needs no disposal).
        using var probe = SKTypeface.FromStream(new MemoryStream(ttf, writable: false))
            ?? throw new ArgumentException("Bytes are not a loadable font.", nameof(ttf));
        var copy = (byte[])ttf.Clone();
        lock (Gate)
        {
            Embedded.Add((probe.FamilyName, copy));
            foreach (var alias in aliases) { ArgumentException.ThrowIfNullOrWhiteSpace(alias); Embedded.Add((alias, copy)); }
            // Family bytes may have changed: drop cached faces and shaped
            // runs (abandoned faces stay valid; shaping re-resolves fresh).
            ShapeFaces.Clear();
            ShapedRuns.Clear();
            ShapedRunOrder.Clear();
        }
    }

    /// <summary>Test hook: shaped-run cache size (proves repeat frames avoid native shaping).</summary>
    internal static int ShapedRunCacheCount
    {
        get { lock (Gate) return ShapedRuns.Count; }
    }

    /// <summary>Test hook: whether a run is cached (parallel-safe presence check).</summary>
    internal static bool IsShapeCachedForTests(string text, float sizePx, string? family)
    {
        lock (Gate) return ShapedRuns.ContainsKey(new ShapedRunKey(text, sizePx, family));
    }

    /// <summary>Test hook: clears caches without touching registrations.</summary>
    internal static void ClearShapeCacheForTests()
    {
        lock (Gate)
        {
            ShapeFaces.Clear();
            ShapedRuns.Clear();
            ShapedRunOrder.Clear();
        }
    }

    public static ShapedText Shape(string text, FontSpec spec)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(spec);

        // The GPU spike (/gpu-spike) proved Skia+HarfBuzz natives work in
        // browser-wasm (SkiaSharp.NativeAssets.WebAssembly), so shaping runs
        // everywhere: no IsBrowser carve-out. If natives are genuinely absent
        // the first touch throws below and latches s_nativesMissing; the
        // Canvas2D JSON frame still carries the text for the browser to draw.
        if (s_nativesMissing) return Heuristic(text, spec);

        // Repeat layout re-measures identical runs on every dirty frame; a
        // hit avoids all native shaping work (hundreds of native typefaces
        // per frame otherwise).
        var key = new ShapedRunKey(text, spec.SizePx, spec.Family);
        lock (Gate)
        {
            if (ShapedRuns.TryGetValue(key, out var cached)) return cached;
        }

        var shaped = ShapeUncached(text, spec);

        lock (Gate)
        {
            if (!ShapedRuns.ContainsKey(key))
            {
                if (ShapedRuns.Count >= MaxShapedRuns && ShapedRunOrder.TryDequeue(out var oldest))
                    ShapedRuns.Remove(oldest);
                ShapedRuns.Add(key, shaped);
                ShapedRunOrder.Enqueue(key);
            }
        }
        return shaped;
    }

    private static ShapedText ShapeUncached(string text, FontSpec spec)
    {
        SKTypeface? face;
        lock (Gate)
        {
            if (!ShapeFaces.TryGetValue(spec.Family ?? string.Empty, out face))
            {
                try
                {
                    face = ResolveTypeface(spec.Family);
                }
                catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException)
                {
                    // Runtimes genuinely without Skia natives. Browser-wasm is no
                    // longer one (see /gpu-spike); this latch now guards exotic
                    // runtimes only. The JSON frame still carries the text.
                    s_nativesMissing = true;
                    return Heuristic(text, spec);
                }
                if (face is null) return Heuristic(text, spec);
                ShapeFaces[spec.Family ?? string.Empty] = face;
            }
        }

        using var font = new SKFont(face, spec.SizePx);
        return ShapeCore(text, font, face);
    }

    private static ShapedText Heuristic(string text, FontSpec spec) =>
        new(text.Length * spec.SizePx * 0.6f, spec.SizePx * 1.2f, text.Length, UnavailableBackend);

    private static ShapedText ShapeCore(string text, SKFont font, SKTypeface typeface)
    {
        var metrics = font.Metrics;
        var height = metrics.Descent - metrics.Ascent + metrics.Leading;
        if (text.Length == 0) return new ShapedText(0, height, 0, HarfBuzzBackend);

        // HarfBuzz path: true shaped advances.
        try
        {
            using var shaper = new SkiaSharp.HarfBuzz.SKShaper(typeface);
            var result = shaper.Shape(text, font);
            return new ShapedText(result.Width, height, result.Codepoints.Length, HarfBuzzBackend);
        }
        catch (Exception)
        {
            // Fall through to Skia advances below.
        }

        var fallbackWidth = font.MeasureText(text);
        return new ShapedText(fallbackWidth, height, text.Length, SkiaFallbackBackend);
    }

    public static (float Width, float Height) Measure(string text, string fontCss)
    {
        var shaped = Shape(text, ParseFontCss(fontCss));
        return (shaped.Width, shaped.Height);
    }

    /// <summary>
    /// Typeface resolution: embedded fonts first, then system lookup, then
    /// well-known font files, then Skia's default (never null).
    /// Caller owns the returned typeface.
    /// </summary>
    public static SKTypeface ResolveTypeface(string? family)
    {
        lock (Gate)
        {
            if (family is not null)
                foreach (var (name, bytes) in Embedded)
                    if (string.Equals(name, family, StringComparison.OrdinalIgnoreCase))
                        return SKTypeface.FromStream(new MemoryStream(bytes, writable: false)) ?? SKTypeface.Default;
        }

        if (family is not null)
        {
            var named = SKTypeface.FromFamilyName(family);
            if (named is not null) return named;
        }

        foreach (var path in SystemFontFiles())
        {
            try
            {
                var fromFile = SKTypeface.FromFile(path);
                if (fromFile is not null) return fromFile;
            }
            catch (Exception) { /* keep looking */ }
        }

        return SKTypeface.Default;
    }

    private static IEnumerable<string> SystemFontFiles()
    {
        if (OperatingSystem.IsWindows())
        {
            var fonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            yield return Path.Combine(fonts, "arial.ttf");
            yield return Path.Combine(fonts, "segoeui.ttf");
        }
        else
        {
            yield return "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
            yield return "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf";
            yield return "/System/Library/Fonts/Helvetica.ttc";
        }
    }
}
