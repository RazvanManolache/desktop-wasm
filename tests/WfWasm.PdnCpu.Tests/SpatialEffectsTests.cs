using System.Runtime.InteropServices;
using WfWasm.PdnCpu;
public sealed class SpatialEffectsTests
{
    public sealed class Token
    {
        public readonly Dictionary<string, double> Values = new();
        public readonly Dictionary<string, object> Objects = new();
        public object? GetProperty(object name) => Objects.TryGetValue(name.ToString()!, out var o) ? new PropertyObject { Value = o } : Values.TryGetValue(name.ToString()!, out var v) ? new PropertyValue { Value = v } : null;
        public T? GetProperty<T>(object name) where T : class => GetProperty(name) as T;
    }
    public class PropertyBase { public object? Value { get; set; } }
    public sealed class PropertyValue : PropertyBase { public new double Value { get; set; } }
    public sealed class PropertyObject : PropertyBase { public new object? Value { get; set; } }
    public sealed class Vec2 { public double X { get; set; } public double Y { get; set; } }
    public sealed class GaussianBlurGpuEffect { public Token Token { get; } = new(); }
    public sealed class SharpenGpuEffect { public Token Token { get; } = new(); }
    public sealed class BrightnessContrastGpuEffect { public Token Token { get; } = new(); }
    public sealed class CurvesToken { public SortedList<int,int>[] ControlPoints { get; set; } = new[] { new SortedList<int,int> { [0] = 0, [255] = 255 } }; }
    public sealed class CurvesGpuEffect { public CurvesToken Token { get; } = new(); }
    public sealed class PosterizeGpuEffect { public Token Token { get; } = new(); }
    [Fact]
    public void Curve_transfer_is_identity_or_inverted_with_alpha_preserved()
    {
        var effect = new CurvesGpuEffect(); byte[] src = { 0, 0, 0, 71, 128, 128, 128, 181, 255, 255, 255, 229 };
        CpuPaint.NoteTileSourceBytes(effect, src, 3, 1, 12);
        Assert.Equal(src, Render(effect, 3, 1));
        effect.Token.ControlPoints[0][0] = 255; effect.Token.ControlPoints[0][255] = 0;
        Assert.Equal(new byte[] {255,255,255,71,127,127,127,181,0,0,0,229}, Render(effect, 3, 1));
        effect.Token.ControlPoints[0][128] = 200;
        Assert.Equal(200, CpuPaint.CurveTables(effect.Token)![0][128]);
    }
    [Fact]
    public void Curves_Repeated_Render_StaysAllocationLean()
    {
        // CPU fallback census: a repeated curves tile with unchanged
        // control points must not rebuild the 256-entry tables and hex
        // key per tile — the key fingerprints the control-point inputs.
        var effect = new CurvesGpuEffect();
        byte[] src = new byte[8 * 8 * 4];
        for (int i = 0; i < src.Length; i++) src[i] = (byte)(i * 7);
        CpuPaint.NoteTileSourceBytes(effect, src, 8, 8, 32);
        Render(effect, 8, 8);
        GC.Collect(2, GCCollectionMode.Forced, true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        const int n = 200;
        for (int i = 0; i < n; i++)
            Render(effect, 8, 8);
        long perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / n;
        Assert.True(perCall <= 560, $"repeated curves tile allocated {perCall} B/call");
    }
    public sealed class LongTile { public long Width { get; set; } public long Height { get; set; } public long Stride { get; set; } public IntPtr Ptr { get; set; } }
    public sealed class LongPoint { public long X { get; set; } public long Y { get; set; } }
    [Fact]
    public void CopyEffectTile_Falls_Back_On_NonInt_Shapes()
    {
        // Exotic tile/point shapes (long instead of int) take the
        // reflective fallback and must render byte-identical pixels.
        var effect = new CurvesGpuEffect();
        byte[] src = new byte[8 * 8 * 4];
        for (int i = 0; i < src.Length; i++) src[i] = (byte)(i * 11);
        CpuPaint.NoteTileSourceBytes(effect, src, 8, 8, 32);
        byte[] expected = Render(effect, 8, 8);
        byte[] bytes = new byte[8 * 8 * 4];
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var tile = new LongTile { Ptr = pin.AddrOfPinnedObject(), Width = 8, Height = 8, Stride = 32 };
            var point = new LongPoint { X = 0, Y = 0 };
            Assert.True(CpuPaint.TryRenderGpuTileCpu(new FakeGpuEffect.Renderer(effect), tile, point));
        }
        finally { pin.Free(); }
        Assert.Equal(expected, bytes);
    }
    public sealed class ExposureGpuEffect { public Token Token { get; } = new(); }
    public sealed class SepiaGpuEffect { public Token Token { get; } = new(); }
    [Fact]
    public void Exposure_Zero_Is_Identity()
    {
        var effect = new ExposureGpuEffect(); effect.Token.Values["Exposure"] = 0;
        byte[] src = new byte[8 * 8 * 4];
        for (int i = 0; i < src.Length; i++) src[i] = (byte)(i * 7);
        CpuPaint.NoteTileSourceBytes(effect, src, 8, 8, 32);
        Assert.Equal(src, Render(effect, 8, 8));
    }
    [Fact]
    public void Exposure_Plus100_Doubles_And_Clamps()
    {
        var effect = new ExposureGpuEffect(); effect.Token.Values["Exposure"] = 100;
        byte[] src = { 64, 64, 64, 200, 200, 200, 200, 255 }; CpuPaint.NoteTileSourceBytes(effect, src, 2, 1, 8);
        Assert.Equal(new byte[] { 128, 128, 128, 200, 255, 255, 255, 255 }, Render(effect, 2, 1));
    }
    [Fact]
    public void Sepia_FullIntensity_UsesBgraOrderAndPreservesAlpha()
    {
        var effect = new SepiaGpuEffect();
        effect.Token.Values["Intensity"] = 100;
        byte[] src = { 190, 150, 100, 173 };
        CpuPaint.NoteTileSourceBytes(effect, src, 1, 1, 4);
        Assert.Equal(new byte[] { 132, 170, 191, 173 }, Render(effect, 1, 1));
    }
    [Fact]
    public void Exposure_Changed_Value_ReRenders()
    {
        var effect = new ExposureGpuEffect(); effect.Token.Values["Exposure"] = 0;
        byte[] src = new byte[8 * 8 * 4];
        for (int i = 0; i < src.Length; i++) src[i] = (byte)(i * 7);
        CpuPaint.NoteTileSourceBytes(effect, src, 8, 8, 32);
        byte[] plain = Render(effect, 8, 8);
        effect.Token.Values["Exposure"] = 50;
        byte[] graded = Render(effect, 8, 8);
        Assert.NotEqual(plain, graded);
    }
    public sealed class PosterizeRealToken
    {
        public int RedLevels { get; set; } = 2;
        public int GreenLevels { get; set; } = 3;
        public bool BlueEnable { get; set; }
        public bool AlphaEnable { get; set; }
        public int RedEnable { get; set; } = 1;
        public int GreenEnable { get; set; } = 1;
    }
    public sealed class CloudsEffectProxy
    {
        public object InnerRef { get; }
        public CloudsEffectProxy(object inner) => InnerRef = inner;
    }
    [Fact]
    public void ProxyWrapped_Effect_Routes_To_Same_Cpu_Tiles()
    {
        // The stash unwraps proxies to the inner effect; dispatch must
        // unwrap identically or the tile source misses and the GPU path
        // runs (headless NRE when its color context is null). Direct and
        // proxied owners must produce byte-identical tiles.
        var effect = new CloudsGpuEffect();
        effect.Token.Values["Scale"] = 4;
        byte[] src = new byte[8 * 8 * 4];
        for (int i = 0; i < src.Length; i++) src[i] = (byte)(i * 3 + 1);
        CpuPaint.NoteTileSourceBytes(effect, src, 8, 8, 32);
        byte[] expected = Render(effect, 8, 8);
        byte[] bytes = new byte[8 * 8 * 4];
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var tile = new Tile { Ptr = pin.AddrOfPinnedObject(), Width = 8, Height = 8, Stride = 32 };
            Assert.True(CpuPaint.TryRenderGpuTileCpu(new FakeGpuEffect.Renderer(new CloudsEffectProxy(effect)), tile, new Point { X = 0, Y = 0 }));
        }
        finally { pin.Free(); }
        Assert.Equal(expected, bytes);
    }
    public sealed class PosterizeGpuEffectReal { public PosterizeRealToken Token { get; } = new(); }
    [Fact]
    public void Posterize_Repeated_Render_StaysAllocationLean()
    {
        // CPU fallback census on the REAL token shape (typed CLR
        // properties, like production tokens): repeated posterize tiles
        // must not rebuild names/arrays/key per tile.
        var effect = new PosterizeGpuEffectReal();
        byte[] src = new byte[8 * 8 * 4];
        for (int i = 0; i < src.Length; i++) src[i] = (byte)(i * 5);
        CpuPaint.NoteTileSourceBytes(effect, src, 8, 8, 32);
        Render(effect, 8, 8);
        GC.Collect(2, GCCollectionMode.Forced, true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        const int n = 200;
        for (int i = 0; i < n; i++)
            Render(effect, 8, 8);
        long perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / n;
        Assert.True(perCall <= 1600, "posterize tile allocated " + perCall + " B/call"); // red pre-fix at 2084; post-fix 1336-1469
    }
    [Fact]
    public void Posterize_honors_level_counts_disabled_channels_and_alpha()
    {
        var effect = new PosterizeGpuEffect(); effect.Token.Values["RedLevels"] = 2; effect.Token.Values["GreenLevels"] = 3;
        effect.Token.Values["BlueEnable"] = 0; effect.Token.Values["AlphaEnable"] = 0;
        byte[] src = {73,140,200,173}; CpuPaint.NoteTileSourceBytes(effect,src,1,1,4);
        Assert.Equal(new byte[] {73,128,255,173}, Render(effect,1,1));
    }
    public sealed class FakeGpuEffect
    {
        public sealed class Renderer { public readonly object owner; public Renderer(object value) => owner = value; }
    }
    public sealed class Tile { public IntPtr Ptr { get; set; } public int Width { get; set; } public int Height { get; set; } public int Stride { get; set; } }
    public sealed class Point { public int X { get; set; } public int Y { get; set; } }
    static byte[] Render(object owner, int w, int h, int x = 0, int y = 0)
    {
        byte[] bytes = new byte[w * h * 4]; var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { Assert.True(CpuPaint.TryRenderGpuTileCpu(new FakeGpuEffect.Renderer(owner), new Tile { Ptr = pin.AddrOfPinnedObject(), Width = w, Height = h, Stride = w * 4 }, new Point { X = x, Y = y })); }
        finally { pin.Free(); }
        return bytes;
    }
    [Fact]
    public void Real_style_generic_and_non_generic_token_overloads_preserve_parameters()
    {
        var effect = new BrightnessContrastGpuEffect(); effect.Token.Values["Brightness"] = 20;
        CpuPaint.NoteTileSourceBytes(effect, new byte[] { 50, 100, 180, 173 }, 1, 1, 4);
        Assert.Equal(new byte[] { 101, 151, 231, 173 }, Render(effect, 1, 1));
    }
    [Fact]
    public void Gaussian_blur_spreads_impulse_without_tile_seams_and_refreshes_changed_parameters()
    {
        var effect = new GaussianBlurGpuEffect(); effect.Token.Values["Radius"] = 3;
        byte[] src = new byte[7 * 3 * 4];
        for (int i = 0; i < src.Length; i += 4) src[i + 3] = 255;
        for (int c = 0; c < 3; c++) src[(1 * 7 + 3) * 4 + c] = 255;
        CpuPaint.NoteTileSourceBytes(effect, src, 7, 3, 28);
        byte[] full = Render(effect, 7, 3), left = Render(effect, 3, 3), right = Render(effect, 4, 3, 3);
        Assert.InRange(full[(1 * 7 + 3) * 4], (byte)1, (byte)254);
        Assert.True(full[(1 * 7 + 2) * 4] > 0);
        for (int y = 0; y < 3; y++) { Assert.Equal(full.AsSpan(y * 28, 12).ToArray(), left.AsSpan(y * 12, 12).ToArray()); Assert.Equal(full.AsSpan(y * 28 + 12, 16).ToArray(), right.AsSpan(y * 16, 16).ToArray()); }
        effect.Token.Values["Radius"] = 0;
        Assert.Equal(src, Render(effect, 7, 3));
    }
    [Fact]
    public void Blur_avoids_hidden_color_fringe_and_sharpen_keeps_alpha_and_threshold()
    {
        var blur = new GaussianBlurGpuEffect(); blur.Token.Values["Radius"] = 3;
        byte[] src = { 255, 0, 0, 0, 0, 0, 255, 255, 255, 0, 0, 0 };
        CpuPaint.NoteTileSourceBytes(blur, src, 3, 1, 12);
        var blurred = Render(blur, 3, 1);
        Assert.Equal(0, blurred[0]); Assert.Equal(0, blurred[4]); Assert.True(blurred[2] > 200);
        var sharp = new SharpenGpuEffect(); sharp.Token.Values["Amount"] = 2;
        byte[] edge = { 70, 70, 70, 113, 130, 130, 130, 181, 190, 190, 190, 225 };
        CpuPaint.NoteTileSourceBytes(sharp, edge, 3, 1, 12);
        var output = Render(sharp, 3, 1);
        Assert.True(output[0] < edge[0]); Assert.True(output[8] > edge[8]);
        Assert.Equal(113, output[3]); Assert.Equal(181, output[7]); Assert.Equal(225, output[11]);
        sharp.Token.Values["Threshold"] = 1;
        Assert.Equal(edge, Render(sharp, 3, 1));
    }
    public sealed class BulgeGpuEffect { public Token Token { get; } = new(); }
    [Fact]
    public void Bulge_identity_at_zero_and_magnifies_center()
    {
        // Genuine defaults: Strength 0.45 in [-3,1], Offset (0,0); Diameter
        // derives from bounds (min dimension); center is bounds-center.
        var effect = new BulgeGpuEffect();
        byte[] src = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) { int i = (y * 8 + x) * 4; src[i] = src[i + 1] = src[i + 2] = (byte)(x * 28); src[i + 3] = 255; }
        CpuPaint.NoteTileSourceBytes(effect, src, 8, 8, 32);
        effect.Token.Values["Strength"] = 0;
        Assert.Equal(src, Render(effect, 8, 8));
        effect.Token.Values["Strength"] = 0.45;
        byte[] full = Render(effect, 8, 8);
        int ci = (4 * 8 + 4) * 4;
        Assert.Equal(src[ci], full[ci]); Assert.Equal(src[ci + 1], full[ci + 1]); Assert.Equal(src[ci + 2], full[ci + 2]);
        int changed = 0;
        for (int i = 0; i < full.Length; i += 4) if (full[i] != src[i] || full[i + 1] != src[i + 1] || full[i + 2] != src[i + 2]) changed++;
        Assert.True(changed > 0);
        for (int i = 3; i < full.Length; i += 4) Assert.Equal(255, full[i]);
        effect.Token.Objects["Offset"] = new Vec2 { X = 1, Y = 0 };
        byte[] shifted = Render(effect, 8, 8);
        int moved = 0;
        for (int i = 0; i < shifted.Length; i++) if (shifted[i] != full[i]) moved++;
        Assert.True(moved > 0);
        effect.Token.Objects.Remove("Offset");
        byte[] plane = Render(effect, 8, 8);
        Assert.Equal(full, plane);
        byte[] left = Render(effect, 4, 8), right = Render(effect, 4, 8, 4);
        for (int y = 0; y < 8; y++) { Assert.Equal(plane.AsSpan(y * 32, 16).ToArray(), left.AsSpan(y * 16, 16).ToArray()); Assert.Equal(plane.AsSpan(y * 32 + 16, 16).ToArray(), right.AsSpan(y * 16, 16).ToArray()); }
    }
    public sealed class TwistGpuEffect { public Token Token { get; } = new(); }
    [Fact]
    public void Twist_identity_at_zero_and_rotates_offcenter_content()
    {
        // Genuine defaults: Amount 30, Offset (0,0); radius is half the
        // min dimension, center is bounds-center. Angle falls off
        // linearly to zero at the radius; the center pixel maps to
        // itself; zero-amount is identity.
        var effect = new TwistGpuEffect();
        int w = 20, h = 14;
        byte[] src = new byte[w * h * 4];
        for (int i = 3; i < src.Length; i += 4) src[i] = 255;
        src[(7 * w + 16) * 4 + 2] = 255;
        CpuPaint.NoteTileSourceBytes(effect, src, w, h, w * 4);
        effect.Token.Values["Amount"] = 0;
        Assert.Equal(src, Render(effect, w, h));
        effect.Token.Values["Amount"] = 30;
        byte[] full = Render(effect, w, h);
        int ci = (7 * w + 10) * 4;
        Assert.Equal(src[ci], full[ci]); Assert.Equal(src[ci + 1], full[ci + 1]); Assert.Equal(src[ci + 2], full[ci + 2]);
        int changed = 0;
        for (int i = 0; i < full.Length; i += 4) if (full[i] != src[i] || full[i + 1] != src[i + 1] || full[i + 2] != src[i + 2]) changed++;
        Assert.True(changed > 0);
        for (int i = 3; i < full.Length; i += 4) Assert.Equal(255, full[i]);
        effect.Token.Objects["Offset"] = new Vec2 { X = 1, Y = 0 };
        byte[] shifted = Render(effect, w, h);
        int moved = 0;
        for (int i = 0; i < shifted.Length; i++) if (shifted[i] != full[i]) moved++;
        Assert.True(moved > 0);
        effect.Token.Objects.Remove("Offset");
        byte[] plane = Render(effect, w, h);
        Assert.Equal(full, plane);
        byte[] left = Render(effect, 10, h), right = Render(effect, 10, h, 10);
        for (int y = 0; y < h; y++) { Assert.Equal(plane.AsSpan(y * 80, 40).ToArray(), left.AsSpan(y * 40, 40).ToArray()); Assert.Equal(plane.AsSpan(y * 80 + 40, 40).ToArray(), right.AsSpan(y * 40, 40).ToArray()); }
    }
    public sealed class TileReflectionGpuEffect { public Token Token { get; } = new(); }
    [Fact]
    public void TileReflection_is_handled_by_the_cpu_tile_path()
    {
        // LOOP1109-M7-03: served TileReflectionGpuEffect OK-apply NREs in
        // the product tile path (TaskProgressDialog stuck, no
        // doeffect-applied). The owned CPU path must answer its tiles, as
        // it does for Twist/Bulge/Clouds. Neutral parameters (no rotation,
        // no curvature, single sample) are identity; the fail-closed rule
        // (no recognized geometry prop) still returns false.
        var effect = new TileReflectionGpuEffect();
        byte[] src = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) { int i = (y * 8 + x) * 4; src[i] = (byte)(x * 31); src[i + 1] = (byte)(y * 31); src[i + 2] = (byte)((x + y) * 16); src[i + 3] = 255; }
        CpuPaint.NoteTileSourceBytes(effect, src, 8, 8, 32);
        effect.Token.Values["Rotation"] = 0;
        effect.Token.Values["Curvature"] = 0;
        effect.Token.Values["Quality"] = 1;
        Assert.Equal(src, Render(effect, 8, 8));
    }
    [Fact]
    public void TileReflection_warp_moves_content_with_alpha_preserved()
    {
        var effect = new TileReflectionGpuEffect();
        int w = 8, h = 8;
        byte[] src = new byte[w * h * 4];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int i = (y * w + x) * 4; src[i] = (byte)(x * 31); src[i + 1] = (byte)(y * 17); src[i + 2] = 9; src[i + 3] = 255; }
        CpuPaint.NoteTileSourceBytes(effect, src, w, h, w * 4);
        // The classic transfer rotates into the warp frame, applies the tan
        // warp in that frame, and rotates back: with zero curvature any
        // rotation is identity, so the warp (curvature 8) carries the
        // change here.
        effect.Token.Values["Rotation"] = 30;
        effect.Token.Values["Curvature"] = 8;
        effect.Token.Values["Quality"] = 1;
        byte[] warped = Render(effect, w, h);
        int changed = 0;
        for (int i = 0; i < warped.Length; i += 4) if (warped[i] != src[i] || warped[i + 1] != src[i + 1] || warped[i + 2] != src[i + 2]) changed++;
        Assert.True(changed > w * h / 2);
        for (int i = 3; i < warped.Length; i += 4) Assert.Equal(255, warped[i]);
        byte[] left = Render(effect, 4, 8), right = Render(effect, 4, 8, 4);
        for (int y = 0; y < 8; y++) { Assert.Equal(warped.AsSpan(y * 32, 16).ToArray(), left.AsSpan(y * 16, 16).ToArray()); Assert.Equal(warped.AsSpan(y * 32 + 16, 16).ToArray(), right.AsSpan(y * 16, 16).ToArray()); }
    }
    [Fact]
    public void TileReflection_alias_token_names_resolve_and_unknown_tokens_fall_through()
    {
        // Angle/TileSize aliases must render byte-identical to the classic
        // Rotation/SquareSize names at equal values; a token with no
        // recognized geometry property falls through (false) so the
        // product path runs as before.
        byte[] src = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) { int i = (y * 8 + x) * 4; src[i] = (byte)(x * 31); src[i + 1] = (byte)(y * 17); src[i + 2] = 9; src[i + 3] = 255; }
        var effect = new TileReflectionGpuEffect();
        CpuPaint.NoteTileSourceBytes(effect, src, 8, 8, 32);
        effect.Token.Values["Rotation"] = 45;
        effect.Token.Values["SquareSize"] = 40;
        effect.Token.Values["Curvature"] = 8;
        effect.Token.Values["Quality"] = 1;
        byte[] classic = Render(effect, 8, 8);
        var aliased = new TileReflectionGpuEffect();
        CpuPaint.NoteTileSourceBytes(aliased, src, 8, 8, 32);
        aliased.Token.Values["Angle"] = 45;
        aliased.Token.Values["TileSize"] = 40;
        aliased.Token.Values["Curvature"] = 8;
        aliased.Token.Values["Quality"] = 1;
        Assert.Equal(classic, Render(aliased, 8, 8));
        var bare = new TileReflectionGpuEffect();
        CpuPaint.NoteTileSourceBytes(bare, src, 8, 8, 32);
        byte[] bytes = new byte[8 * 8 * 4]; var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { Assert.False(CpuPaint.TryRenderGpuTileCpu(new FakeGpuEffect.Renderer(bare), new Tile { Ptr = pin.AddrOfPinnedObject(), Width = 8, Height = 8, Stride = 32 }, new Point { X = 0, Y = 0 })); }
        finally { pin.Free(); }
    }
    public sealed class Argb { public byte A { get; set; } public byte R { get; set; } public byte G { get; set; } public byte B { get; set; } }
    public sealed class CloudsGpuEffect { public Token Token { get; } = new(); }
    [Fact]
    public void Clouds_seed_scale_power_vary_output()
    {
        // Genuine defaults: Scale 250 [2,1000], Power 0.5 [0,1], Seed 0,
        // MixMode Normal, Color1 = Primary, Color2 = Secondary.
        var effect = new CloudsGpuEffect();
        byte[] src = new byte[16 * 16 * 4];
        for (int i = 0; i < src.Length; i++) src[i] = 128;
        CpuPaint.NoteTileSourceBytes(effect, src, 16, 16, 64);
        effect.Token.Values["Scale"] = 250; effect.Token.Values["Power"] = 0.5; effect.Token.Values["Seed"] = 0;
        effect.Token.Objects["Color1"] = new Argb { A = 255, R = 10, G = 20, B = 30 };
        effect.Token.Objects["Color2"] = new Argb { A = 255, R = 200, G = 210, B = 220 };
        byte[] full = Render(effect, 16, 16);
        Assert.Equal(full, Render(effect, 16, 16));
        var distinct = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < full.Length; i += 4) { Assert.Equal(255, full[i + 3]); Assert.True(full[i] >= 10 && full[i] <= 200); distinct.Add(full[i] | (full[i + 1] << 8) | (full[i + 2] << 16)); }
        Assert.True(distinct.Count > 16);
        effect.Token.Values["Seed"] = 7;
        byte[] reseeded = Render(effect, 16, 16);
        int moved = 0; for (int i = 0; i < full.Length; i++) if (full[i] != reseeded[i]) moved++;
        Assert.True(moved > full.Length / 4);
        effect.Token.Values["Seed"] = 0; effect.Token.Values["Scale"] = 500;
        byte[] rescaled = Render(effect, 16, 16);
        moved = 0; for (int i = 0; i < full.Length; i++) if (full[i] != rescaled[i]) moved++;
        Assert.True(moved > 0);
        effect.Token.Values["Scale"] = 250; effect.Token.Values["Power"] = 1.0;
        byte[] powered = Render(effect, 16, 16);
        moved = 0; for (int i = 0; i < full.Length; i++) if (full[i] != powered[i]) moved++;
        Assert.True(moved > 0);
        effect.Token.Values["Power"] = 0.5;
        byte[] plane = Render(effect, 16, 16);
        Assert.Equal(full, plane);
        byte[] left = Render(effect, 8, 16), right = Render(effect, 8, 16, 8);
        for (int y = 0; y < 16; y++) { Assert.Equal(plane.AsSpan(y * 64, 32).ToArray(), left.AsSpan(y * 32, 32).ToArray()); Assert.Equal(plane.AsSpan(y * 64 + 32, 32).ToArray(), right.AsSpan(y * 32, 32).ToArray()); }
    }
    // Genuine Paint.NET 5 color properties carry ManagedColor values
    // (PaintDotNet.Imaging: no A/R/G/B properties, no ToColor; sRGB via
    // ManagedColorExtensions.GetSrgb). The CPU fallback must convert
    // those endpoints instead of silently rendering the white/black
    // fallbacks while the dialog shows user-picked colors.
    public sealed class GenuineManagedColor
    {
        private readonly byte _r, _g, _b, _a;
        public GenuineManagedColor(byte r, byte g, byte b, byte a = 255) { _r = r; _g = g; _b = b; _a = a; }
        public byte[] ToSrgbBytes() => new byte[] { _r, _g, _b, _a };
    }
    public sealed class GenuineRgba128Float
    {
        public float R { get; set; }
        public float G { get; set; }
        public float B { get; set; }
        public float A { get; set; }
    }
    public sealed class GenuineSrgbColorA
    {
        public GenuineRgba128Float srgbColor = new();
    }
    [Fact]
    public void Clouds_reads_genuine_ManagedColor_endpoints()
    {
        var effect = new CloudsGpuEffect();
        int w = 16, h = 12;
        byte[] src = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int o = (y * w + x) * 4;
            src[o] = (byte)((x * 13 + y * 29) & 0xFF);
            src[o + 1] = (byte)((x * 7 + y * 41) & 0xFF);
            src[o + 2] = (byte)((x * 31 + y * 3) & 0xFF);
            src[o + 3] = 255;
        }
        for (int y = 4; y < 8; y++)
        for (int x = 5; x < 11; x++)
        {
            int o = (y * w + x) * 4;
            src[o] = src[o + 1] = src[o + 2] = 255;
        }
        CpuPaint.NoteTileSourceBytes(effect, src, w, h, w * 4);
        effect.Token.Values["Scale"] = 300;
        effect.Token.Values["Power"] = 0.5;
        effect.Token.Values["Seed"] = 3;
        effect.Token.Objects["Color1"] = new GenuineManagedColor(200, 10, 20);
        effect.Token.Objects["Color2"] = new GenuineManagedColor(10, 20, 200);
        byte[] full = Render(effect, w, h);
        for (int i = 0; i < full.Length; i += 4)
        {
            Assert.Equal((byte)255, full[i + 3]);
            Assert.InRange(full[i], (byte)10, (byte)200);
            Assert.InRange(full[i + 1], (byte)10, (byte)20);
            Assert.InRange(full[i + 2], (byte)10, (byte)200);
        }
    }
    // PdnCpu-side managed-effect diagnostic: for a Clouds effect owner,
    // report per-property resolution (genuine scalar props + ManagedColor
    // endpoints) and the exact sRGB bytes the CPU fallback would render.
    // Fail-closed by contract: hostile shapes (throwing getters, missing
    // token/owner) yield reason strings, never throws. Lets a trace
    // distinguish "token unreadable here" from fork-side dialog death.
    public sealed class ThrowingScaleToken { public int Scale => throw new InvalidOperationException("boom"); }
    public sealed class ThrowingScaleEffect { public ThrowingScaleToken Token { get; } = new(); }
    public sealed class TokenlessEffect { }
    [Fact]
    public void Clouds_diagnosis_resolves_genuine_token_and_reports_hostile_shapes_fail_closed()
    {
        var effect = new CloudsGpuEffect();
        effect.Token.Values["Scale"] = 250;
        effect.Token.Values["Power"] = 0.5;
        effect.Token.Values["Seed"] = 3;
        effect.Token.Values["MixMode"] = 0;
        effect.Token.Objects["Color1"] = new GenuineManagedColor(200, 10, 20);
        effect.Token.Objects["Color2"] = new GenuineManagedColor(10, 20, 200);
        var full = CpuPaint.DiagnoseCloudsEffect(effect);
        Assert.True(full.HasToken);
        Assert.Equal("", full.Problems);
        Assert.True(full.ScalePresent && full.PowerPresent && full.SeedPresent && full.MixModePresent);
        Assert.True(full.Color1Present && full.Color2Present);
        Assert.Equal(250, full.Scale);
        Assert.Equal(0.5, full.Power);
        Assert.Equal(3, full.Seed);
        Assert.Equal(0xFFC80A14u, full.Color1Argb);
        Assert.Equal(0xFF0A14C8u, full.Color2Argb);
        var bad = CpuPaint.DiagnoseCloudsEffect(new ThrowingScaleEffect());
        Assert.True(bad.HasToken);
        Assert.Contains("Scale", bad.Problems);
        var noToken = CpuPaint.DiagnoseCloudsEffect(new TokenlessEffect());
        Assert.False(noToken.HasToken);
        Assert.Contains("null token", noToken.Problems);
        var noOwner = CpuPaint.DiagnoseCloudsEffect(null);
        Assert.False(noOwner.HasToken);
        Assert.Contains("null owner", noOwner.Problems);
    }
}
namespace PaintDotNet.Imaging
{
    // Shape-faithful stand-in for the genuine ManagedColorExtensions
    // (verified against the shipped PaintDotNet.Core): single-argument
    // GetSrgb returning an SrgbColorA whose srgbColor field carries
    // float R/G/B/A channels. Lives in the value's assembly so the
    // reflection lookup resolves exactly like the genuine one.
    public static class ManagedColorExtensions
    {
        public static SpatialEffectsTests.GenuineSrgbColorA GetSrgb(SpatialEffectsTests.GenuineManagedColor c)
        {
            byte[] b = c.ToSrgbBytes();
            return new SpatialEffectsTests.GenuineSrgbColorA
            {
                srgbColor = new SpatialEffectsTests.GenuineRgba128Float
                {
                    R = b[0] / 255f,
                    G = b[1] / 255f,
                    B = b[2] / 255f,
                    A = b[3] / 255f
                }
            };
        }
    }
}
