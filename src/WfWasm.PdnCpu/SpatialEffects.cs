using System.Reflection;
using System.Runtime.InteropServices;
namespace WfWasm.PdnCpu;

public static unsafe partial class CpuPaint
{
    // Tokens expose both GetProperty(object) and GetProperty<T>(object).
    // Select the non-generic overload explicitly; GetMethod throws an
    // AmbiguousMatchException on real Paint.NET tokens.
    private static PropertyInfo? MostDerivedProperty(Type type, string name)
    {
        for (Type? t = type; t is not null; t = t.BaseType)
        {
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (p is not null) return p;
        }
        return null;
    }

    private static object? GetTokenValue(object token, string name)
    {
        object? value = MostDerivedProperty(token.GetType(), name)?.GetValue(token);
        if (value is null)
        {
            var get = token.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "GetProperty" && !m.IsGenericMethod && m.GetParameters() is var p && p.Length == 1 && (p[0].ParameterType == typeof(object) || p[0].ParameterType == typeof(string)));
            value = get?.Invoke(token, new object[] { name });
        }
        if (value is null)
        {
            var item = token.GetType().GetProperty("Item");
            if (item?.GetIndexParameters().Length == 1) value = item.GetValue(token, new object[] { name });
        }
        return value is null ? null : MostDerivedProperty(value.GetType(), "Value")?.GetValue(value) ?? value;
    }

    private static double TokenDouble(object token, string name, double fallback)
    {
        try { double v = Convert.ToDouble(GetTokenValue(token, name) ?? fallback); return double.IsFinite(v) ? v : fallback; }
        catch { return fallback; }
    }

    // Cache one rendered source per immutable stashed layer and parameter
    // set. Tiles sample the full image, including neighbors outside their
    // rectangle, so there are no seams at worker/tile boundaries.
    private sealed class SpatialResult { public readonly object Gate = new(); public string Key = ""; public byte[] Pixels = Array.Empty<byte>(); }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TileSource, SpatialResult> s_spatial = new();

    private static bool RenderSpatialTileCpu(object owner, object dst, object point)
    {
        if (!s_tileSources.TryGetValue(owner, out var source)) return false;
        object? token = GetEffectToken(owner);
        if (token is null) return false;
        bool blur = owner.GetType().Name.Contains("GaussianBlur", StringComparison.Ordinal);
        double amount = Math.Clamp(TokenDouble(token, blur ? "Radius" : "Amount", blur ? 2 : 2), 0, blur ? 750 : 10);
        double extra = Math.Clamp(TokenDouble(token, blur ? "GammaBoost" : "Threshold", 0), blur ? -0.99 : 0, blur ? 2 : 1);
        string key = FormattableString.Invariant($"{blur}:{amount:R}:{extra:R}");
        var cached = s_spatial.GetValue(source, _ => new SpatialResult());
        byte[] pixels;
        lock (cached.Gate)
        {
            if (cached.Key != key)
            {
                cached.Pixels = blur ? BlurSource(source, amount, extra) : SharpenSource(source, amount, extra);
                cached.Key = key;
            }
            pixels = cached.Pixels;
        }
        return CopyEffectTile(source, pixels, dst, point);
    }

    // Portable radial-bulge fallback for BulgeGpuEffect (Effects > Distort >
    // Bulge). Approximation, not native parity: inverse-map resample with
    // bilinear filtering and clamped edges (the D2D sample-map shader and
    // its edge mode are not reproduced). Genuine token: PdnBulgeEffect props
    // Strength (double [-3,1], default 0.45), Offset (Vector2, default 0,0);
    // Diameter derives from the render bounds (min dimension), matching
    // BulgeGpuEffect.OnCreateOutput. Center is bounds-center shifted by
    // Offset scaled by half-size; strength 0 (or empty bounds) is identity.
    private static bool RenderBulgeTileCpu(object owner, object dst, object point)
    {
        if (!s_tileSources.TryGetValue(owner, out var source)) return false;
        object? token = GetEffectToken(owner);
        if (token is null) return false;
        double strength = Math.Clamp(TokenDouble(token, "Strength", 0.45), -3, 1);
        var (ox, oy) = TokenVector(token, "Offset");
        ox = Math.Clamp(ox, -1, 1); oy = Math.Clamp(oy, -1, 1);
        string key = FormattableString.Invariant($"{strength:R}:{ox:R}:{oy:R}");
        var cached = s_spatial.GetValue(source, _ => new SpatialResult());
        byte[] pixels;
        lock (cached.Gate)
        {
            if (cached.Key != key)
            {
                cached.Pixels = BulgeSource(source, strength, ox, oy);
                cached.Key = key;
            }
            pixels = cached.Pixels;
        }
        return CopyEffectTile(source, pixels, dst, point);
    }

    private static (double x, double y) TokenVector(object token, string name)
    {
        try
        {
            object? v = GetTokenValue(token, name);
            if (v is null) return (0, 0);
            double x = Convert.ToDouble(v.GetType().GetProperty("X")?.GetValue(v) ?? 0);
            double y = Convert.ToDouble(v.GetType().GetProperty("Y")?.GetValue(v) ?? 0);
            return (double.IsFinite(x) ? x : 0, double.IsFinite(y) ? y : 0);
        }
        catch { return (0, 0); }
    }

    // Portable twist fallback for TwistGpuEffect (Effects > Distort >
    // Twist). Approximation, not native parity: inverse-map resample with
    // bilinear filtering and clamped edges. Genuine token: Amount (int,
    // default 30), Offset (Vector2, default 0,0); Scale/Quality derive
    // from the render bounds in TwistGpuEffect.OnUpdateOutput and are 1:1
    // for the full-surface tile path (bounds == document), so radius is
    // half the min dimension and center is bounds-center shifted by
    // Offset scaled by half-size — the same centering Bulge uses.
    // Angle falls off linearly to zero at the radius; amount 0 (or empty
    // bounds) is identity.
    private static bool RenderTwistTileCpu(object owner, object dst, object point)
    {
        if (!s_tileSources.TryGetValue(owner, out var source)) return false;
        object? token = GetEffectToken(owner);
        if (token is null) return false;
        int amount = Math.Clamp(TokenInt(token, "Amount", 30), -100, 100);
        var (ox, oy) = TokenVector(token, "Offset");
        ox = Math.Clamp(ox, -1, 1); oy = Math.Clamp(oy, -1, 1);
        string key = FormattableString.Invariant($"{amount}:{ox:R}:{oy:R}");
        var cached = s_spatial.GetValue(source, _ => new SpatialResult());
        byte[] pixels;
        lock (cached.Gate)
        {
            if (cached.Key != key)
            {
                cached.Pixels = TwistSource(source, amount, ox, oy);
                cached.Key = key;
            }
            pixels = cached.Pixels;
        }
        return CopyEffectTile(source, pixels, dst, point);
    }

    private static byte[] TwistSource(TileSource source, int amount, double ox, double oy)
    {
        byte[] input = TightPixels(source);
        int w = source.Width, h = source.Height;
        byte[] output = new byte[input.Length];
        double radius = Math.Min(w, h) / 2.0;
        if (radius <= 0 || amount == 0) { Buffer.BlockCopy(input, 0, output, 0, input.Length); return output; }
        double cx = w / 2.0 + ox * w / 2.0, cy = h / 2.0 + oy * h / 2.0;
        double maxAngle = amount / 100.0 * Math.PI;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            double dx = x - cx, dy = y - cy;
            double r = Math.Sqrt(dx * dx + dy * dy);
            double theta = Math.Atan2(dy, dx);
            double twist = r < radius ? maxAngle * (1.0 - r / radius) : 0.0;
            double sa = Math.Sin(theta - twist), ca = Math.Cos(theta - twist);
            double sx = cx + r * ca, sy = cy + r * sa;
            int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
            double fx = sx - x0, fy = sy - y0;
            int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            x0 = Math.Clamp(x0, 0, w - 1); y0 = Math.Clamp(y0, 0, h - 1);
            int o = (y * w + x) * 4;
            for (int c = 0; c < 4; c++)
            {
                double s00 = input[(y0 * w + x0) * 4 + c], s10 = input[(y0 * w + x1) * 4 + c];
                double s01 = input[(y1 * w + x0) * 4 + c], s11 = input[(y1 * w + x1) * 4 + c];
                double v = s00 * (1 - fx) * (1 - fy) + s10 * fx * (1 - fy) + s01 * (1 - fx) * fy + s11 * fx * fy;
                output[o + c] = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
            }
        }
        return output;
    }

    private static byte[] BulgeSource(TileSource source, double strength, double ox, double oy)
    {
        byte[] input = TightPixels(source);
        int w = source.Width, h = source.Height;
        byte[] output = new byte[input.Length];
        double maxrad = Math.Min(w, h) / 2.0;
        if (maxrad <= 0 || (strength == 0 && ox == 0 && oy == 0)) { Buffer.BlockCopy(input, 0, output, 0, input.Length); return output; }
        double cx = w / 2.0 + ox * w / 2.0, cy = h / 2.0 + oy * h / 2.0;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            double dx = x - cx, dy = y - cy;
            double d = Math.Sqrt(dx * dx + dy * dy) / maxrad;
            double f = d >= 1 ? 1.0 : 1.0 - strength * (1.0 - d * d);
            double sx = cx + dx * f, sy = cy + dy * f;
            int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
            double fx = sx - x0, fy = sy - y0;
            int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            x0 = Math.Clamp(x0, 0, w - 1); y0 = Math.Clamp(y0, 0, h - 1);
            int o = (y * w + x) * 4;
            for (int c = 0; c < 4; c++)
            {
                double s00 = input[(y0 * w + x0) * 4 + c], s10 = input[(y0 * w + x1) * 4 + c];
                double s01 = input[(y1 * w + x0) * 4 + c], s11 = input[(y1 * w + x1) * 4 + c];
                double v = s00 * (1 - fx) * (1 - fy) + s10 * fx * (1 - fy) + s01 * (1 - fx) * fy + s11 * fx * fy;
                output[o + c] = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
            }
        }
        return output;
    }
    // Portable clouds fallback for CloudsGpuEffect (Effects > Render >
    // Clouds). Approximation, not native parity: seeded value-noise fBm
    // blended between the two endpoint colors. Genuine token: PdnCloudsEffect
    // props Scale (int [2,1000], default 250), Power (double [0,1], default
    // 0.5), Seed (int [0,255], default 0), MixMode (ignored: Normal blend),
    // Color1/Color2 (ManagedColor endpoints, opaque replace). Scale maps to
    // feature size (sampled at Scale/8 px per noise cell, 4 octaves); Power
    // maps to a contrast exponent e = 2 - 2*Power (0.5 is identity).
    // PdnCpu-side managed-effect diagnostic for the Clouds original
    // failure (served dialog-never-opens with no captured stack): report,
    // per genuine token property (Scale/Power/Seed/MixMode/Color1/Color2),
    // whether it resolves and the exact sRGB bytes the CPU fallback would
    // render. Fail-closed by contract: hostile shapes (throwing getters,
    // missing token/owner) yield reason strings, never throws. A fully
    // resolved report with "" Problems proves the PdnCpu side can render
    // the token, routing any remaining dialog death to fork-side
    // construction; a non-empty Problems pinpoints the unreadable seam.
    public sealed class CloudsDiagnosis
    {
        public bool HasToken;
        public bool ScalePresent; public int Scale;
        public bool PowerPresent; public double Power;
        public bool SeedPresent; public int Seed;
        public bool MixModePresent; public string? MixMode;
        public bool Color1Present; public uint Color1Argb;
        public bool Color2Present; public uint Color2Argb;
        public string Problems = "";
    }

    public static CloudsDiagnosis DiagnoseCloudsEffect(object? owner)
    {
        var d = new CloudsDiagnosis();
        var problems = new System.Collections.Generic.List<string>();
        try
        {
            object? token = owner is null ? null : GetEffectToken(owner);
            d.HasToken = token is not null;
            if (token is null)
            {
                problems.Add(owner is null ? "null owner" : "null token");
            }
            else
            {
                if (TryProbeRaw(token, "Scale", problems, out _)) { d.ScalePresent = true; d.Scale = Math.Clamp(TokenInt(token, "Scale", 250), 2, 1000); }
                if (TryProbeRaw(token, "Power", problems, out _)) { d.PowerPresent = true; d.Power = Math.Clamp(TokenDouble(token, "Power", 0.5), 0, 1); }
                if (TryProbeRaw(token, "Seed", problems, out _)) { d.SeedPresent = true; d.Seed = Math.Clamp(TokenInt(token, "Seed", 0), 0, 255); }
                if (TryProbeRaw(token, "MixMode", problems, out object? mix)) { d.MixModePresent = true; try { d.MixMode = mix?.ToString(); } catch { d.MixMode = null; } }
                if (TryProbeRaw(token, "Color1", problems, out _)) { d.Color1Present = true; d.Color1Argb = TokenColor(token, "Color1", 255, 255, 255, 255); }
                if (TryProbeRaw(token, "Color2", problems, out _)) { d.Color2Present = true; d.Color2Argb = TokenColor(token, "Color2", 0, 0, 0, 255); }
            }
        }
        catch (Exception ex)
        {
            problems.Add("diagnostic throw: " + ex.GetType().Name);
        }
        d.Problems = string.Join("; ", problems);
        return d;
    }

    private static bool TryProbeRaw(object token, string name, System.Collections.Generic.List<string> problems, out object? raw)
    {
        try
        {
            raw = GetTokenValue(token, name);
        }
        catch (Exception ex)
        {
            raw = null;
            problems.Add(name + " unreadable: " + ex.GetType().Name);
            return false;
        }
        if (raw is null)
        {
            problems.Add(name + " missing");
            return false;
        }
        return true;
    }

    private static bool RenderCloudsTileCpu(object owner, object dst, object point)
    {
        if (!s_tileSources.TryGetValue(owner, out var source)) return false;
        object? token = GetEffectToken(owner);
        if (token is null) return false;
        int scale = Math.Clamp(TokenInt(token, "Scale", 250), 2, 1000);
        double power = Math.Clamp(TokenDouble(token, "Power", 0.5), 0, 1);
        int seed = Math.Clamp(TokenInt(token, "Seed", 0), 0, 255);
        var c1 = TokenColor(token, "Color1", 255, 255, 255, 255);
        var c2 = TokenColor(token, "Color2", 0, 0, 0, 255);
        string key = FormattableString.Invariant($"{scale}:{power:R}:{seed}:{c1:X8}:{c2:X8}");
        var cached = s_spatial.GetValue(source, _ => new SpatialResult());
        byte[] pixels;
        lock (cached.Gate)
        {
            if (cached.Key != key)
            {
                cached.Pixels = CloudsSource(source, scale, power, seed, c1, c2);
                cached.Key = key;
            }
            pixels = cached.Pixels;
        }
        return CopyEffectTile(source, pixels, dst, point);
    }

    private static int TokenInt(object token, string name, int fallback)
    {
        try
        {
            object? v = GetTokenValue(token, name);
            if (v is null) return fallback;
            double d = Convert.ToDouble(v);
            if (!double.IsFinite(d)) return fallback;
            return (int)Math.Round(d);
        }
        catch { return fallback; }
    }

    private static uint TokenColor(object token, string name, byte da, byte dr, byte dg, byte db)
    {
        try
        {
            object? v = GetTokenValue(token, name);
            if (v is null) return Pack(da, dr, dg, db);
            System.Type t = v.GetType();
            byte a = Chan(t.GetProperty("A")?.GetValue(v), da);
            byte r = Chan(t.GetProperty("R")?.GetValue(v), dr);
            byte g = Chan(t.GetProperty("G")?.GetValue(v), dg);
            byte b = Chan(t.GetProperty("B")?.GetValue(v), db);
            if (a == da && r == dr && g == dg && b == db)
            {
                if (TryReadManagedColor(v, out byte ma, out byte mr, out byte mg, out byte mb))
                    return Pack(ma, mr, mg, mb);
                object? c = t.GetMethod("ToColor", System.Type.EmptyTypes)?.Invoke(v, null);
                if (c is not null)
                {
                    System.Type ct = c.GetType();
                    a = Chan(ct.GetProperty("A")?.GetValue(c), a);
                    r = Chan(ct.GetProperty("R")?.GetValue(c), r);
                    g = Chan(ct.GetProperty("G")?.GetValue(c), g);
                    b = Chan(ct.GetProperty("B")?.GetValue(c), b);
            }
            }
            return Pack(a, r, g, b);
        }
        catch { return Pack(da, dr, dg, db); }
    }

    // Genuine Paint.NET 5 color properties carry ManagedColor values:
    // no A/R/G/B properties and no ToColor, so the property read above
    // always lands on the fallbacks. Convert through the genuine
    // ManagedColorExtensions.GetSrgb in the value's own assembly
    // (pristine and rewritten modules keep these member names) and
    // read the srgbColor field's float R/G/B/A channels. Any miss
    // fails closed (false) and the caller keeps its fallbacks.
    private static bool TryReadManagedColor(object v, out byte a, out byte r, out byte g, out byte b)
    {
        a = r = g = b = 0;
        try
        {
            System.Type? ext = v.GetType().Assembly.GetType("PaintDotNet.Imaging.ManagedColorExtensions");
            if (ext is null) return false;
            MethodInfo? getSrgb = null;
            foreach (var m in ext.GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.Name == "GetSrgb" && m.GetParameters().Length == 1) { getSrgb = m; break; }
            if (getSrgb is null) return false;
            object? srgb = getSrgb.Invoke(null, new object[] { v });
            if (srgb is null) return false;
            object? channels = srgb.GetType().GetField("srgbColor",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(srgb);
            if (channels is null) return false;
            System.Type ct = channels.GetType();
            float fa = ReadFloatChannel(ct.GetProperty("A")?.GetValue(channels));
            float fr = ReadFloatChannel(ct.GetProperty("R")?.GetValue(channels));
            float fg = ReadFloatChannel(ct.GetProperty("G")?.GetValue(channels));
            float fb = ReadFloatChannel(ct.GetProperty("B")?.GetValue(channels));
            if (!float.IsFinite(fa) || !float.IsFinite(fr) || !float.IsFinite(fg) || !float.IsFinite(fb)) return false;
            a = ToChannelByte(fa); r = ToChannelByte(fr); g = ToChannelByte(fg); b = ToChannelByte(fb);
            return true;
        }
        catch { return false; }
    }

    private static float ReadFloatChannel(object? v)
    {
        try { return v is null ? float.NaN : Convert.ToSingle(v); }
        catch { return float.NaN; }
    }

    private static byte ToChannelByte(float f) => (byte)Math.Clamp((int)Math.Round(f * 255f), 0, 255);
    private static byte Chan(object? v, byte fallback)
    {
        try { if (v is null) return fallback; double d = Convert.ToDouble(v); if (!double.IsFinite(d)) return fallback; return (byte)Math.Clamp((int)Math.Round(d), 0, 255); }
        catch { return fallback; }
    }

    private static uint Pack(byte a, byte r, byte g, byte b) => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    private static byte[] CloudsSource(TileSource source, int scale, double power, int seed, uint c1, uint c2)
    {
        int w = source.Width, h = source.Height;
        byte[] output = new byte[w * h * 4];
        if (w <= 0 || h <= 0) return output;
        double cell = Math.Max(scale / 8.0, 1.0);
        double e = 2.0 - 2.0 * power;
        byte a1 = (byte)(c1 >> 24), r1 = (byte)(c1 >> 16), g1 = (byte)(c1 >> 8), b1 = (byte)c1;
        byte a2 = (byte)(c2 >> 24), r2 = (byte)(c2 >> 16), g2 = (byte)(c2 >> 8), b2 = (byte)c2;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            double n = Fbm(x / cell, y / cell, seed);
            double t = e == 1 ? n : Math.Pow(Math.Clamp(n, 0, 1), e);
            int o = (y * w + x) * 4;
            output[o] = (byte)Math.Round(b1 + (b2 - b1) * t);
            output[o + 1] = (byte)Math.Round(g1 + (g2 - g1) * t);
            output[o + 2] = (byte)Math.Round(r1 + (r2 - r1) * t);
            output[o + 3] = (byte)Math.Round(a1 + (a2 - a1) * t);
        }
        return output;
    }

    private static double Fbm(double x, double y, int seed)
    {
        double v = 0, amp = 0.5, fx = x, fy = y;
        for (int o = 0; o < 4; o++) { v += amp * ValueNoise(fx, fy, seed + o * 101); amp *= 0.5; fx *= 2.03; fy *= 2.03; }
        return v;
    }

    private static double ValueNoise(double x, double y, int seed)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double xf = x - xi, yf = y - yi;
        double u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
        double a = Hash01(xi, yi, seed), b = Hash01(xi + 1, yi, seed);
        double c = Hash01(xi, yi + 1, seed), d = Hash01(xi + 1, yi + 1, seed);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    private static double Hash01(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 974634211 + 513071);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffffff) / (double)0x1000000;
    }


    private static bool CopyEffectTile(TileSource source, byte[] pixels, object dst, object point)
    {
        Type d = dst.GetType(), p = point.GetType();
        int w = Convert.ToInt32(d.GetProperty("Width")?.GetValue(dst)), h = Convert.ToInt32(d.GetProperty("Height")?.GetValue(dst));
        int stride = Convert.ToInt32(d.GetProperty("Stride")?.GetValue(dst));
        IntPtr ptr = UnboxPointer(d.GetProperty("Ptr")?.GetValue(dst));
        int x = Convert.ToInt32(p.GetProperty("X")?.GetValue(point)), y = Convert.ToInt32(p.GetProperty("Y")?.GetValue(point));
        if (w <= 0 || h <= 0 || stride < w * 4 || ptr == IntPtr.Zero || x < 0 || y < 0 || x + w > source.Width || y + h > source.Height) return false;
        for (int row = 0; row < h; row++)
            Marshal.Copy(pixels, ((y + row) * source.Width + x) * 4, ptr + row * stride, w * 4);
        return true;
    }

    private static byte[] TightPixels(TileSource source)
    {
        byte[] bytes = new byte[checked(source.Width * source.Height * 4)];
        for (int y = 0; y < source.Height; y++) Buffer.BlockCopy(source.Pixels, y * source.Stride, bytes, y * source.Width * 4, source.Width * 4);
        return bytes;
    }

    private static byte[] BlurSource(TileSource source, double radius, double boost)
    {
        byte[] input = TightPixels(source);
        if (radius == 0) return input;
        float exponent = (float)(1 + boost);
        float[] linear = new float[input.Length];
        for (int i = 0; i < input.Length; i += 4)
        {
            float a = input[i + 3] / 255f;
            for (int c = 0; c < 3; c++) linear[i + c] = MathF.Pow((float)SrgbByteToLinear(input[i + c]), exponent) * a;
            linear[i + 3] = a;
        }
        // Straight-alpha input is convolved in premultiplied space to avoid
        // transparent-color fringes. Radius is three standard deviations.
        GaussBlur(linear, source.Width, source.Height, radius / 3);
        byte[] output = new byte[input.Length];
        for (int i = 0; i < output.Length; i += 4)
        {
            float a = linear[i + 3];
            for (int c = 0; c < 3; c++) output[i + c] = a > 0 ? LinearToSrgbByte(MathF.Pow(Math.Clamp(linear[i + c] / a, 0, 1), 1 / exponent)) : (byte)0;
            output[i + 3] = (byte)Math.Clamp((int)MathF.Round(a * 255), 0, 255);
        }
        return output;
    }

    // Portable unsharp-mask fallback. It implements Amount and Threshold;
    // the native Direct2D kernel is not claimed to be bit-identical.
    private static byte[] SharpenSource(TileSource source, double amount, double threshold)
    {
        byte[] input = TightPixels(source), output = (byte[])input.Clone();
        if (amount == 0) return output;
        int w = source.Width, h = source.Height;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            for (int c = 0; c < 3; c++)
            {
                double low = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    low += input[(Math.Clamp(y + dy, 0, h - 1) * w + Math.Clamp(x + dx, 0, w - 1)) * 4 + c] * (dx == 0 ? 2 : 1) * (dy == 0 ? 2 : 1) / 16.0;
                double delta = input[i + c] - low;
                if (Math.Abs(delta) >= threshold * 255) output[i + c] = (byte)Math.Clamp((int)Math.Round(input[i + c] + amount * delta), 0, 255);
            }
        }
        return output;
    }
}
