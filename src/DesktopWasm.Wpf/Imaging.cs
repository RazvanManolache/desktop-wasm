using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf;

// Imaging v1 (PresentationCore subset, shim, not port): ImageSource bitmap
// root, the BitmapSource pixel pipeline (decode through SkiaSharp, managed
// RGBA buffers, write/format/crop/transform/render derivatives), palette and
// pixel-format catalogs, codec shells, and the Image control's data types.
// Buffer convention: engine pixel buffers are RGBA byte order, straight
// (unpremultiplied) alpha; Format reports the WPF format for compat while
// CopyPixels copies raw RGBA-ordered bytes. RenderTargetBitmap converts its
// premultiplied snapshot back to straight on read. Indexed formats stay a
// precise boundary (no palette quantization in v1).

/// <summary>Image stretch (matches System.Windows.Media.Stretch).</summary>
public enum Stretch
{
    None,
    Fill,
    Uniform,
    UniformToFill,
}

/// <summary>Bitmap caching hint (matches System.Windows.Media.Imaging.BitmapCacheOption).</summary>
public enum BitmapCacheOption
{
    Default,
    OnDemand,
    OnLoad,
    None,
}

/// <summary>Bitmap creation flags (matches System.Windows.Media.Imaging.BitmapCreateOptions).</summary>
[Flags]
public enum BitmapCreateOptions
{
    None = 0,
    PreservePixelFormat = 1,
    DelayCreation = 2,
    IgnoreColorProfile = 4,
    IgnoreImageCache = 8,
    Rotate0 = 0,
    Rotate90 = 16,
    Rotate180 = 32,
    Rotate270 = 48,
}

/// <summary>SIPS scaling quality (matches System.Windows.Media.BitmapScalingMode).</summary>
public enum BitmapScalingMode
{
    Unspecified,
    LowQuality,
    HighQuality,
    Linear,
    NearestNeighbor,
    Fant,
}

/// <summary>Edge rendering (matches System.Windows.Media.EdgeMode).</summary>
public enum EdgeMode
{
    Unspecified,
    Aliased,
}

/// <summary>Image metadata root (matches System.Windows.Media.ImageMetadata).</summary>
public abstract class ImageMetadata
{
}

/// <summary>Bitmap metadata store (matches System.Windows.Media.Imaging.BitmapMetadata).</summary>
public sealed class BitmapMetadata : ImageMetadata
{
    private readonly Dictionary<string, object?> _queries = new(StringComparer.OrdinalIgnoreCase);

    public bool ContainsQuery(string query)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        return _queries.ContainsKey(query);
    }

    public object? GetQuery(string query)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        return _queries.TryGetValue(query, out var value) ? value : null;
    }

    public void SetQuery(string query, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        _queries[query] = value;
    }
}

/// <summary>Pixel format token (matches System.Windows.Media.PixelFormat).</summary>
public readonly struct PixelFormat : IEquatable<PixelFormat>
{
    private readonly int _id;

    private PixelFormat(int id, int bitsPerPixel)
    {
        _id = id;
        BitsPerPixel = bitsPerPixel;
    }

    internal static PixelFormat Define(int id, int bitsPerPixel) => new(id, bitsPerPixel);

    /// <summary>Bits per pixel (matches PixelFormat.BitsPerPixel).</summary>
    public int BitsPerPixel { get; }

    public static bool operator ==(PixelFormat left, PixelFormat right) => left._id == right._id;
    public static bool operator !=(PixelFormat left, PixelFormat right) => left._id != right._id;

    public bool Equals(PixelFormat other) => _id == other._id;
    public override bool Equals(object? obj) => obj is PixelFormat other && Equals(other);
    public override int GetHashCode() => _id;
    public override string ToString() => PixelFormats.NameOf(this);
}

/// <summary>Pixel format catalog (matches System.Windows.Media.PixelFormats).</summary>
public static class PixelFormats
{
    private static readonly string[] Names =
    [
        nameof(Default), nameof(Bgr101010), nameof(Bgr24), nameof(Bgr32), nameof(Bgr555), nameof(Bgr565),
        nameof(Bgra32), nameof(BlackWhite), nameof(Cmyk32), nameof(Gray16), nameof(Gray2), nameof(Gray32Float),
        nameof(Gray4), nameof(Gray8), nameof(Indexed1), nameof(Indexed2), nameof(Indexed4), nameof(Indexed8),
        nameof(Pbgra32), nameof(Prgba128Float), nameof(Rgb128Float), nameof(Rgb24), nameof(Rgba128Float), nameof(Rgba64),
    ];

    private static readonly PixelFormat[] Formats =
    [
        PixelFormat.Define(0, 32), PixelFormat.Define(1, 30), PixelFormat.Define(2, 24), PixelFormat.Define(3, 32),
        PixelFormat.Define(4, 16), PixelFormat.Define(5, 16), PixelFormat.Define(6, 32), PixelFormat.Define(7, 1),
        PixelFormat.Define(8, 32), PixelFormat.Define(9, 16), PixelFormat.Define(10, 2), PixelFormat.Define(11, 96),
        PixelFormat.Define(12, 4), PixelFormat.Define(13, 8), PixelFormat.Define(14, 1), PixelFormat.Define(15, 2),
        PixelFormat.Define(16, 4), PixelFormat.Define(17, 8), PixelFormat.Define(18, 32), PixelFormat.Define(19, 128),
        PixelFormat.Define(20, 128), PixelFormat.Define(21, 24), PixelFormat.Define(22, 128), PixelFormat.Define(23, 64),
    ];

    internal static string NameOf(PixelFormat format)
    {
        for (var i = 0; i < Formats.Length; i++)
            if (Formats[i] == format) return Names[i];
        return "Custom";
    }

    public static PixelFormat Default => Bgra32;
    public static PixelFormat Bgr101010 => Formats[1];
    public static PixelFormat Bgr24 => Formats[2];
    public static PixelFormat Bgr32 => Formats[3];
    public static PixelFormat Bgr555 => Formats[4];
    public static PixelFormat Bgr565 => Formats[5];
    public static PixelFormat Bgra32 => Formats[6];
    public static PixelFormat BlackWhite => Formats[7];
    public static PixelFormat Cmyk32 => Formats[8];
    public static PixelFormat Gray16 => Formats[9];
    public static PixelFormat Gray2 => Formats[10];
    public static PixelFormat Gray32Float => Formats[11];
    public static PixelFormat Gray4 => Formats[12];
    public static PixelFormat Gray8 => Formats[13];
    public static PixelFormat Indexed1 => Formats[14];
    public static PixelFormat Indexed2 => Formats[15];
    public static PixelFormat Indexed4 => Formats[16];
    public static PixelFormat Indexed8 => Formats[17];
    public static PixelFormat Pbgra32 => Formats[18];
    public static PixelFormat Prgba128Float => Formats[19];
    public static PixelFormat Rgb128Float => Formats[20];
    public static PixelFormat Rgb24 => Formats[21];
    public static PixelFormat Rgba128Float => Formats[22];
    public static PixelFormat Rgba64 => Formats[23];
}

/// <summary>Palette (matches System.Windows.Media.Imaging.BitmapPalette).</summary>
public sealed class BitmapPalette : IList<Color>
{
    private readonly List<Color> _colors;

    public BitmapPalette(IList<Color> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        _colors = new List<Color>(colors);
    }

    public BitmapPalette(BitmapSource bitmapImage, int numberOfColors)
    {
        ArgumentNullException.ThrowIfNull(bitmapImage);
        if (numberOfColors <= 0 || numberOfColors > 256)
            throw new ArgumentOutOfRangeException(nameof(numberOfColors), "Palette size is 1-256.");
        _colors = new List<Color>(numberOfColors);
        for (var i = 0; i < numberOfColors; i++)
        {
            var v = (byte)(i * 255 / Math.Max(1, numberOfColors - 1));
            _colors.Add(Color.FromRgb(v, v, v));
        }
    }

    public int Count => _colors.Count;
    public bool IsReadOnly => false;
    public Color this[int index] { get => _colors[index]; set => _colors[index] = value; }
    public void Add(Color item) => _colors.Add(item);
    public void Clear() => _colors.Clear();
    public bool Contains(Color item) => _colors.Contains(item);
    public void CopyTo(Color[] array, int arrayIndex) => _colors.CopyTo(array, arrayIndex);
    public IEnumerator<Color> GetEnumerator() => _colors.GetEnumerator();
    public int IndexOf(Color item) => _colors.IndexOf(item);
    public void Insert(int index, Color item) => _colors.Insert(index, item);
    public bool Remove(Color item) => _colors.Remove(item);
    public void RemoveAt(int index) => _colors.RemoveAt(index);
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _colors.GetEnumerator();
}

/// <summary>Size options (matches System.Windows.Media.Imaging.BitmapSizeOptions).</summary>
public sealed class BitmapSizeOptions
{
    public BitmapSizeOptions(int width, int height)
    {
        if (width < 0 || height < 0) throw new ArgumentOutOfRangeException("Size is non-negative.");
        Width = width;
        Height = height;
    }

    public static BitmapSizeOptions FromEmptyPixels() => new(0, 0);

    /// <summary>Empty options (matches BitmapSizeOptions.FromEmptyOptions).</summary>
    public static BitmapSizeOptions FromEmptyOptions() => new(0, 0);

    public int Width { get; }
    public int Height { get; }
    public bool PreservesAspectRatio => true;
    public int Rotation => 0;
}

/// <summary>Image root (matches System.Windows.Media.ImageSource).</summary>
public abstract class ImageSource : DependencyObject
{
    /// <summary>Width in device-independent pixels (0 when unknown).</summary>
    public abstract double Width { get; }

    /// <summary>Height in device-independent pixels (0 when unknown).</summary>
    public abstract double Height { get; }

    /// <summary>Embedded metadata (null when none).</summary>
    public virtual ImageMetadata? Metadata => null;

    /// <summary>Internal RGBA pixel access (null when undecodable).</summary>
    internal virtual (byte[] Pixels, int Width, int Height)? RgbaPixels => null;
}

/// <summary>
/// Drawing-backed image (matches System.Windows.Media.DrawingImage): retains
/// the Drawing (null until set) and reports its bounds as size (0 when none,
/// like WPF before layout). Pixel rasterization stays a precise boundary;
/// Image hosts fall back to the retained vector paint.
/// </summary>
public sealed class DrawingImage : ImageSource
{
    private Drawing? _drawing;

    public DrawingImage()
    {
    }

    public DrawingImage(Drawing drawing) => Drawing = drawing;

    /// <summary>Painted drawing (matches DrawingImage.Drawing).</summary>
    public Drawing? Drawing
    {
        get => _drawing;
        set
        {
            if (ReferenceEquals(_drawing, value)) return;
            _drawing = value;
        }
    }

    public override double Width => DrawingBounds is { } bounds ? Math.Max(0, bounds.Width) : 0;

    public override double Height => DrawingBounds is { } bounds ? Math.Max(0, bounds.Height) : 0;

    private Rect? DrawingBounds => _drawing switch
    {
        GeometryDrawing geometry => geometry.Geometry?.GetBounds(),
        DrawingGroup => null,
        _ => null,
    };
}

/// <summary>Bitmap root (matches System.Windows.Media.Imaging.BitmapSource).</summary>
public abstract class BitmapSource : ImageSource
{
    public sealed override double Width => PixelWidth * 96.0 / DpiX;
    public sealed override double Height => PixelHeight * 96.0 / DpiY;

    public abstract PixelFormat Format { get; }
    public abstract int PixelWidth { get; }
    public abstract int PixelHeight { get; }
    public abstract double DpiX { get; }
    public abstract double DpiY { get; }
    public virtual BitmapPalette? Palette => null;

    /// <summary>Creates a bitmap from managed pixels (matches BitmapSource.Create).</summary>
    public static BitmapSource Create(int pixelWidth, int pixelHeight, double dpiX, double dpiY, PixelFormat pixelFormat, BitmapPalette? palette, Array pixels, int stride) =>
        new CachedBitmap(pixelWidth, pixelHeight, dpiX, dpiY, pixelFormat, palette, CopyArray(pixels), stride);

    /// <summary>Creates a bitmap from native pixels (matches BitmapSource.Create).</summary>
    public static BitmapSource Create(int pixelWidth, int pixelHeight, double dpiX, double dpiY, PixelFormat pixelFormat, BitmapPalette? palette, IntPtr buffer, int bufferSize, int stride)
    {
        var bytes = new byte[Math.Max(0, bufferSize)];
        if (bytes.Length > 0) System.Runtime.InteropServices.Marshal.Copy(buffer, bytes, 0, bytes.Length);
        return new CachedBitmap(pixelWidth, pixelHeight, dpiX, dpiY, pixelFormat, palette, bytes, stride);
    }

    public void CopyPixels(Array pixels, int stride, int offset) =>
        CopyPixels(new Int32Rect(0, 0, PixelWidth, PixelHeight), pixels, stride, offset);

    public virtual void CopyPixels(Int32Rect sourceRect, Array pixels, int stride, int offset)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var rgba = RequirePixels();
        var rect = ClampRect(sourceRect);
        CopyRegion(rgba.Pixels, rgba.Width, rect, ToByteArray(pixels), stride, offset);
    }

    public void CopyPixels(IntPtr buffer, int bufferSize, int stride) =>
        CopyPixels(new Int32Rect(0, 0, PixelWidth, PixelHeight), buffer, bufferSize, stride);

    public virtual void CopyPixels(Int32Rect sourceRect, IntPtr buffer, int bufferSize, int stride)
    {
        var rgba = RequirePixels();
        var rect = ClampRect(sourceRect);
        var rowBytes = rect.Width * 4;
        if (bufferSize < rect.Height * stride)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer is smaller than the requested region.");
        for (var y = 0; y < rect.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(rgba.Pixels, ((rect.Y + y) * rgba.Width + rect.X) * 4, buffer + y * stride, Math.Min(rowBytes, stride));
    }

    internal (byte[] Pixels, int Width, int Height) RequirePixels() =>
        RgbaPixels ?? throw new InvalidOperationException($"{GetType().Name} has no decoded pixels (unresolved UriSource or empty image).");

    internal Int32Rect ClampRect(Int32Rect rect)
    {
        var rgba = RequirePixels();
        var x = Math.Clamp(rect.X, 0, rgba.Width);
        var y = Math.Clamp(rect.Y, 0, rgba.Height);
        var w = Math.Clamp(rect.Width, 0, rgba.Width - x);
        var h = Math.Clamp(rect.Height, 0, rgba.Height - y);
        return new Int32Rect(x, y, w, h);
    }

    internal static void CopyRegion(byte[] src, int srcWidth, Int32Rect rect, byte[] dst, int stride, int offset)
    {
        var rowBytes = rect.Width * 4;
        for (var y = 0; y < rect.Height; y++)
            Buffer.BlockCopy(src, ((rect.Y + y) * srcWidth + rect.X) * 4, dst, offset + y * stride, Math.Min(rowBytes, stride));
    }

    internal static byte[] CopyArray(Array pixels) =>
        pixels switch
        {
            byte[] b => (byte[])b.Clone(),
            _ => ToByteArray(pixels),
        };

    internal static byte[] ToByteArray(Array pixels)
    {
        if (pixels is byte[] b) return b;
        var bytes = new byte[Buffer.ByteLength(pixels)];
        Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}

/// <summary>Managed pixel bitmap (matches the BitmapSource.Create product).</summary>
internal sealed class CachedBitmap : BitmapSource
{
    private readonly byte[] _pixels;

    internal CachedBitmap(int pixelWidth, int pixelHeight, double dpiX, double dpiY, PixelFormat format, BitmapPalette? palette, byte[] pixels, int stride)
    {
        if (pixelWidth < 0 || pixelHeight < 0) throw new ArgumentOutOfRangeException("Size is non-negative.");
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DpiX = dpiX <= 0 ? 96 : dpiX;
        DpiY = dpiY <= 0 ? 96 : dpiY;
        Format = format;
        Palette = palette;
        _pixels = new byte[checked(pixelWidth * pixelHeight * 4)];
        var rowBytes = Math.Min(pixelWidth * 4, stride);
        for (var y = 0; y < pixelHeight && y * stride < pixels.Length; y++)
            Buffer.BlockCopy(pixels, y * stride, _pixels, y * pixelWidth * 4, Math.Min(rowBytes, pixels.Length - y * stride));
    }

    public override PixelFormat Format { get; }
    public override int PixelWidth { get; }
    public override int PixelHeight { get; }
    public override double DpiX { get; }
    public override double DpiY { get; }
    public override BitmapPalette? Palette { get; }
    internal override (byte[] Pixels, int Width, int Height)? RgbaPixels => (_pixels, PixelWidth, PixelHeight);
}

/// <summary>Skia bridge for decode/encode (internal; RGBA straight-alpha buffers).</summary>
internal static class ImagingInterop
{
    internal static (byte[] Pixels, int Width, int Height) Decode(byte[] encoded)
    {
        using var input = new SKMemoryStream(encoded);
        using var decoded = SKBitmap.Decode(input) ?? throw new InvalidOperationException("Bytes do not decode to an image (PNG/JPEG/BMP/GIF-first-frame).");
        return ToRgba(decoded);
    }

    internal static (byte[] Pixels, int Width, int Height) Decode(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Decode(copy.ToArray());
    }

    internal static (byte[] Pixels, int Width, int Height) ToRgba(SKBitmap source)
    {
        if (source.ColorType == SKColorType.Rgba8888 && source.AlphaType == SKAlphaType.Unpremul)
        {
            var direct = new byte[checked(source.Width * source.Height * 4)];
            System.Runtime.InteropServices.Marshal.Copy(source.GetPixels(), direct, 0, direct.Length);
            return (direct, source.Width, source.Height);
        }
        using var rgba = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(rgba))
            canvas.DrawBitmap(source, new SKRect(0, 0, source.Width, source.Height), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), null);
        var bytes = new byte[checked(rgba.Width * rgba.Height * 4)];
        System.Runtime.InteropServices.Marshal.Copy(rgba.GetPixels(), bytes, 0, bytes.Length);
        return (bytes, rgba.Width, rgba.Height);
    }

    internal static byte[] EncodePng(byte[] rgba, int width, int height)
    {
        using var bitmap = FromRgba(rgba, width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    internal static byte[] EncodeJpeg(byte[] rgba, int width, int height, int quality)
    {
        using var bitmap = FromRgba(rgba, width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 0, 100));
        return data.ToArray();
    }

    internal static byte[] EncodeBmp(byte[] rgba, int width, int height)
    {
        // 32-bit BI_RGB BMP (Skia does not encode BMP): BGRA rows, bottom-up.
        var rowBytes = checked(width * 4);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x4D42);
        writer.Write(checked(54 + rowBytes * height));
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(rowBytes * height);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);
        for (var y = height - 1; y >= 0; y--)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                writer.Write(rgba[i + 2]);
                writer.Write(rgba[i + 1]);
                writer.Write(rgba[i]);
                writer.Write(rgba[i + 3]);
            }
        return stream.ToArray();
    }

    internal static SKBitmap FromRgba(byte[] rgba, int width, int height)
    {
        if (rgba.Length != checked(width * height * 4))
            throw new ArgumentException("Pixel data must contain width*height RGBA pixels.");
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        return bitmap;
    }
}

/// <summary>Bitmap image (matches System.Windows.Media.Imaging.BitmapImage).</summary>
public sealed class BitmapImage : BitmapSource
{
    private byte[]? _pixels;
    private bool _initialized;

    public BitmapImage()
    {
    }

    public BitmapImage(Uri uriSource) : this() => UriSource = uriSource;

    public BitmapImage(Uri uriSource, System.Net.Cache.RequestCachePolicy? cachePolicy) : this(uriSource)
    {
    }

    public Uri? UriSource { get; set; }
    public Stream? StreamSource { get; set; }
    public BitmapCacheOption CacheOption { get; set; } = BitmapCacheOption.Default;
    public BitmapCreateOptions CreateOptions { get; set; } = BitmapCreateOptions.None;

    public event EventHandler? DownloadCompleted;

    public bool IsDownloading => false;

    public void BeginInit()
    {
    }

    public void EndInit()
    {
        if (_initialized) return;
        _initialized = true;
        EnsurePixels();
        DownloadCompleted?.Invoke(this, EventArgs.Empty);
    }

    public override PixelFormat Format => PixelFormats.Bgra32;
    public override int PixelWidth => _pixels is null ? 0 : _w;
    public override int PixelHeight => _pixels is null ? 0 : _h;
    public override double DpiX => 96;
    public override double DpiY => 96;

    private int _w;
    private int _h;

    /// <summary>Decodes StreamSource/registered pack bytes (retained-only when absent).</summary>
    public void EnsurePixels()
    {
        if (_pixels is not null) return;
        if (StreamSource is not null)
        {
            var decoded = ImagingInterop.Decode(StreamSource);
            _pixels = decoded.Pixels;
            _w = decoded.Width;
            _h = decoded.Height;
            return;
        }
        if (UriSource is not null)
        {
            try
            {
                var parsed = PackUri.Parse(UriSource.IsAbsoluteUri ? UriSource.AbsoluteUri : UriSource.OriginalString);
                var bytes = WpfAppBoot.FindPackBytes(parsed.Path, parsed.Assembly);
                if (bytes is not null)
                {
                    var decoded = ImagingInterop.Decode(bytes);
                    _pixels = decoded.Pixels;
                    _w = decoded.Width;
                    _h = decoded.Height;
                }
            }
            catch (FormatException)
            {
                // Unresolvable URI: retained-only, like Window.Icon.
            }
        }
    }

    internal override (byte[] Pixels, int Width, int Height)? RgbaPixels
    {
        get
        {
            if (!_initialized && (StreamSource is not null || UriSource is not null)) EnsurePixels();
            return _pixels is null ? null : (_pixels, _w, _h);
        }
    }
}

/// <summary>Bitmap frame (matches System.Windows.Media.Imaging.BitmapFrame).</summary>
public class BitmapFrame : BitmapSource
{
    private readonly byte[] _pixels;

    protected BitmapFrame(byte[] pixels, int width, int height, double dpiX, double dpiY, PixelFormat format, BitmapPalette? palette, BitmapMetadata? metadata)
    {
        _pixels = pixels;
        PixelWidth = width;
        PixelHeight = height;
        DpiX = dpiX;
        DpiY = dpiY;
        Format = format;
        Palette = palette;
        Metadata = metadata;
    }

    public static BitmapFrame Create(Uri bitmapUri) => Create(bitmapUri, BitmapCreateOptions.None, BitmapCacheOption.Default);

    public static BitmapFrame Create(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
        ArgumentNullException.ThrowIfNull(bitmapUri);
        var image = new BitmapImage(bitmapUri) { CreateOptions = createOptions, CacheOption = cacheOption };
        image.EnsurePixels();
        var rgba = image.RgbaPixels;
        return new BitmapFrame(rgba?.Pixels ?? [], rgba?.Width ?? 0, rgba?.Height ?? 0, 96, 96, PixelFormats.Bgra32, null, null);
    }

    public static BitmapFrame Create(Stream bitmapStream) => Create(bitmapStream, BitmapCreateOptions.None, BitmapCacheOption.Default);

    public static BitmapFrame Create(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
        ArgumentNullException.ThrowIfNull(bitmapStream);
        var decoded = ImagingInterop.Decode(bitmapStream);
        return new BitmapFrame(decoded.Pixels, decoded.Width, decoded.Height, 96, 96, PixelFormats.Bgra32, null, null);
    }

    public static BitmapFrame Create(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var rgba = source.RequirePixels();
        return new BitmapFrame((byte[])rgba.Pixels.Clone(), rgba.Width, rgba.Height, source.DpiX, source.DpiY, source.Format, source.Palette, null);
    }

    public override PixelFormat Format { get; }
    public override int PixelWidth { get; }
    public override int PixelHeight { get; }
    public override double DpiX { get; }
    public override double DpiY { get; }
    public override BitmapPalette? Palette { get; }
    public override ImageMetadata? Metadata { get; }

    /// <summary>
    /// Creating decoder (matches BitmapFrame.Decoder): frames stamped by the
    /// decoder that produced them, so converted AvalonDock's icon picker
    /// narrows with "is IconBitmapDecoder" instead of throwing MissingMethod.
    /// Null for ad-hoc Create() frames with no decoder, like a decoderless
    /// WPF frame.
    /// </summary>
    public BitmapDecoder? Decoder { get; internal set; }

    /// <summary>Embedded thumbnail (matches BitmapFrame.Thumbnail): null when
    /// the source carries none, which is every engine decode path in v1.</summary>
    public virtual BitmapSource? Thumbnail => null;

    internal override (byte[] Pixels, int Width, int Height)? RgbaPixels => (_pixels, PixelWidth, PixelHeight);
}

/// <summary>Decoder root (matches System.Windows.Media.Imaging.BitmapDecoder).</summary>
public abstract class BitmapDecoder
{
    public abstract IReadOnlyList<BitmapFrame> Frames { get; }
    public virtual BitmapPalette? Palette => null;
    public virtual BitmapSource? Preview => null;

    public static BitmapDecoder Create(Uri bitmapUri) => Create(bitmapUri, BitmapCreateOptions.None, BitmapCacheOption.Default);

    public static BitmapDecoder Create(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption) =>
        Stamp(new InternalDecoder([BitmapFrame.Create(bitmapUri, createOptions, cacheOption)]));

    public static BitmapDecoder Create(Stream bitmapStream) => Create(bitmapStream, BitmapCreateOptions.None, BitmapCacheOption.Default);

    public static BitmapDecoder Create(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption) =>
        Stamp(new InternalDecoder([BitmapFrame.Create(bitmapStream, createOptions, cacheOption)]));

    /// <summary>Stamps frames with their creating decoder (backs BitmapFrame.Decoder).</summary>
    internal static TDecoder Stamp<TDecoder>(TDecoder decoder) where TDecoder : BitmapDecoder
    {
        foreach (var frame in decoder.Frames) frame.Decoder = decoder;
        return decoder;
    }

    private sealed class InternalDecoder(IReadOnlyList<BitmapFrame> frames) : BitmapDecoder
    {
        public override IReadOnlyList<BitmapFrame> Frames { get; } = frames;
    }
}

/// <summary>GIF decoder (matches System.Windows.Media.Imaging.GifBitmapDecoder).</summary>
public sealed class GifBitmapDecoder : BitmapDecoder
{
    private readonly IReadOnlyList<BitmapFrame> _frames;

    public GifBitmapDecoder(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption, int expectedFrameCount = 1)
    {
        _frames = BitmapDecoder.Create(bitmapUri, createOptions, cacheOption).Frames;
        Stamp(this);
    }

    public GifBitmapDecoder(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption, int expectedFrameCount = 1)
    {
        _frames = BitmapDecoder.Create(bitmapStream, createOptions, cacheOption).Frames;
        Stamp(this);
    }

    public override IReadOnlyList<BitmapFrame> Frames => _frames;
}

/// <summary>
/// Icon decoder (matches System.Windows.Media.Imaging.IconBitmapDecoder):
/// the converted AvalonDock icon picker narrows decoded frames with
/// "is IconBitmapDecoder" before comparing bit depth. Construction decodes
/// through the shared pipeline like GifBitmapDecoder; ICO-specific
/// multi-size selection stays a documented boundary (frames land in stream
/// order, first frame first).
/// </summary>
public sealed class IconBitmapDecoder : BitmapDecoder
{
    private readonly IReadOnlyList<BitmapFrame> _frames;

    public IconBitmapDecoder(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
        ArgumentNullException.ThrowIfNull(bitmapUri);
        _frames = BitmapDecoder.Create(bitmapUri, createOptions, cacheOption).Frames;
        BitmapDecoder.Stamp(this);
    }

    public IconBitmapDecoder(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
        ArgumentNullException.ThrowIfNull(bitmapStream);
        _frames = BitmapDecoder.Create(bitmapStream, createOptions, cacheOption).Frames;
        BitmapDecoder.Stamp(this);
    }

    public override IReadOnlyList<BitmapFrame> Frames => _frames;
}

/// <summary>Encoder root (matches System.Windows.Media.Imaging.BitmapEncoder).</summary>
public abstract class BitmapEncoder
{
    private readonly List<BitmapFrame> _frames = [];

    public IList<BitmapFrame> Frames => _frames;

    public abstract void Save(Stream stream);

    protected static void SaveFrame(BitmapSource frame, Stream stream, Func<byte[], int, int, byte[]> encode)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(stream);
        var rgba = frame.RequirePixels();
        var bytes = encode(rgba.Pixels, rgba.Width, rgba.Height);
        stream.Write(bytes, 0, bytes.Length);
    }
}

/// <summary>PNG encoder (matches System.Windows.Media.Imaging.PngBitmapEncoder).</summary>
public sealed class PngBitmapEncoder : BitmapEncoder
{
    public bool Interlace { get; set; }

    public override void Save(Stream stream)
    {
        if (Frames.Count == 0) throw new InvalidOperationException("PngBitmapEncoder has no frames to save.");
        SaveFrame(Frames[0], stream, ImagingInterop.EncodePng);
    }
}

/// <summary>JPEG encoder (matches System.Windows.Media.Imaging.JpegBitmapEncoder).</summary>
public sealed class JpegBitmapEncoder : BitmapEncoder
{
    public int QualityLevel { get; set; } = 75;
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public short Rotation { get; set; }

    public override void Save(Stream stream)
    {
        if (Frames.Count == 0) throw new InvalidOperationException("JpegBitmapEncoder has no frames to save.");
        var quality = QualityLevel;
        SaveFrame(Frames[0], stream, (pixels, w, h) => ImagingInterop.EncodeJpeg(pixels, w, h, quality));
    }
}

/// <summary>BMP encoder (matches System.Windows.Media.Imaging.BmpBitmapEncoder).</summary>
public sealed class BmpBitmapEncoder : BitmapEncoder
{
    public override void Save(Stream stream)
    {
        if (Frames.Count == 0) throw new InvalidOperationException("BmpBitmapEncoder has no frames to save.");
        SaveFrame(Frames[0], stream, ImagingInterop.EncodeBmp);
    }
}

/// <summary>Writable bitmap (matches System.Windows.Media.Imaging.WriteableBitmap).</summary>
public sealed class WriteableBitmap : BitmapSource
{
    private byte[] _pixels;
    private int _locks;
    private System.Runtime.InteropServices.GCHandle _pin;

    public WriteableBitmap(int pixelWidth, int pixelHeight, double dpiX, double dpiY, PixelFormat pixelFormat, BitmapPalette? palette)
        : this(pixelWidth, pixelHeight, dpiX, dpiY, pixelFormat, palette, new Int32Rect(0, 0, 0, 0))
    {
    }

    public WriteableBitmap(int pixelWidth, int pixelHeight, double dpiX, double dpiY, PixelFormat pixelFormat, BitmapPalette? palette, Int32Rect dirtyRect)
    {
        if (pixelWidth < 0 || pixelHeight < 0) throw new ArgumentOutOfRangeException("Size is non-negative.");
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DpiX = dpiX <= 0 ? 96 : dpiX;
        DpiY = dpiY <= 0 ? 96 : dpiY;
        Format = pixelFormat;
        Palette = palette;
        _pixels = new byte[checked(pixelWidth * pixelHeight * 4)];
        if (dirtyRect.Width > 0 && dirtyRect.Height > 0) AddDirtyRect(dirtyRect);
    }

    public WriteableBitmap(BitmapSource source)
        : this(source?.PixelWidth ?? 0, source?.PixelHeight ?? 0,
            source?.DpiX ?? 96, source?.DpiY ?? 96,
            source?.Format ?? PixelFormats.Bgra32, source?.Palette)
    {
        ArgumentNullException.ThrowIfNull(source);
        var rgba = source.RequirePixels();
        Buffer.BlockCopy(rgba.Pixels, 0, _pixels, 0, Math.Min(rgba.Pixels.Length, _pixels.Length));
    }

    public override PixelFormat Format { get; }
    public override int PixelWidth { get; }
    public override int PixelHeight { get; }
    public override double DpiX { get; }
    public override double DpiY { get; }
    public override BitmapPalette? Palette { get; }

    /// <summary>Pinned pixel buffer (valid between Lock/Unlock; like real BackBuffer).</summary>
    public IntPtr BackBuffer => _locks > 0 && _pin.IsAllocated ? _pin.AddrOfPinnedObject() : IntPtr.Zero;

    public int BackBufferStride => PixelWidth * 4;

    public void Lock()
    {
        _locks++;
        if (!_pin.IsAllocated) _pin = System.Runtime.InteropServices.GCHandle.Alloc(_pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
    }

    public void Unlock()
    {
        if (_locks == 0) throw new InvalidOperationException("Unlock without a matching Lock.");
        _locks--;
        if (_locks == 0 && _pin.IsAllocated) _pin.Free();
    }

    public bool IsFrozen => false;

    /// <summary>Marks a region dirty (the engine repaints whole frames, so this is accepted as-is).</summary>
    public void AddDirtyRect(Int32Rect dirtyRect)
    {
    }

    public void WritePixels(Int32Rect sourceRect, Array pixels, int stride, int offset)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var rect = ClampWriteRect(sourceRect);
        var src = ToByteArray(pixels);
        var rowBytes = rect.Width * 4;
        for (var y = 0; y < rect.Height; y++)
            Buffer.BlockCopy(src, offset + y * stride, _pixels, ((rect.Y + y) * PixelWidth + rect.X) * 4, Math.Min(rowBytes, Math.Max(0, stride)));
    }

    public void WritePixels(Int32Rect sourceRect, IntPtr buffer, int bufferSize, int stride)
    {
        var rect = ClampWriteRect(sourceRect);
        if (bufferSize < rect.Height * stride)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer is smaller than the requested region.");
        for (var y = 0; y < rect.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(buffer + y * stride, _pixels, (rect.Y + y) * PixelWidth * 4 + rect.X * 4, Math.Min(rect.Width * 4, stride));
    }

    private Int32Rect ClampWriteRect(Int32Rect rect)
    {
        var x = Math.Clamp(rect.X, 0, PixelWidth);
        var y = Math.Clamp(rect.Y, 0, PixelHeight);
        var w = Math.Clamp(rect.Width, 0, PixelWidth - x);
        var h = Math.Clamp(rect.Height, 0, PixelHeight - y);
        return new Int32Rect(x, y, w, h);
    }

    internal override (byte[] Pixels, int Width, int Height)? RgbaPixels => (_pixels, PixelWidth, PixelHeight);
}

/// <summary>Cropped bitmap (matches System.Windows.Media.Imaging.CroppedBitmap).</summary>
public sealed class CroppedBitmap : BitmapSource
{
    private readonly byte[] _pixels;

    public CroppedBitmap(BitmapSource source, Int32Rect sourceRect)
    {
        ArgumentNullException.ThrowIfNull(source);
        var rgba = source.RequirePixels();
        var x = Math.Clamp(sourceRect.X, 0, rgba.Width);
        var y = Math.Clamp(sourceRect.Y, 0, rgba.Height);
        PixelWidth = Math.Clamp(sourceRect.Width, 0, rgba.Width - x);
        PixelHeight = Math.Clamp(sourceRect.Height, 0, rgba.Height - y);
        DpiX = source.DpiX;
        DpiY = source.DpiY;
        Format = source.Format;
        Palette = source.Palette;
        _pixels = new byte[checked(PixelWidth * PixelHeight * 4)];
        CopyRegion(rgba.Pixels, rgba.Width, new Int32Rect(x, y, PixelWidth, PixelHeight), _pixels, PixelWidth * 4, 0);
    }

    public override PixelFormat Format { get; }
    public override int PixelWidth { get; }
    public override int PixelHeight { get; }
    public override double DpiX { get; }
    public override double DpiY { get; }
    public override BitmapPalette? Palette { get; }
    internal override (byte[] Pixels, int Width, int Height)? RgbaPixels => (_pixels, PixelWidth, PixelHeight);
}

/// <summary>Format-converted bitmap (matches System.Windows.Media.Imaging.FormatConvertedBitmap).</summary>
public sealed class FormatConvertedBitmap : BitmapSource
{
    private readonly byte[] _pixels;

    public FormatConvertedBitmap()
    {
        PixelWidth = 0;
        PixelHeight = 0;
        DpiX = 96;
        DpiY = 96;
        Format = PixelFormats.Bgra32;
        _pixels = [];
    }

    public FormatConvertedBitmap(BitmapSource source, PixelFormat destinationFormat, BitmapPalette? destinationPalette, double alphaThreshold)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (destinationFormat == PixelFormats.Indexed1 || destinationFormat == PixelFormats.Indexed2 ||
            destinationFormat == PixelFormats.Indexed4 || destinationFormat == PixelFormats.Indexed8)
            throw new InvalidOperationException("Indexed destination formats need palette quantization (precise boundary).");
        var rgba = source.RequirePixels();
        PixelWidth = rgba.Width;
        PixelHeight = rgba.Height;
        DpiX = source.DpiX;
        DpiY = source.DpiY;
        Format = destinationFormat;
        Palette = destinationPalette;
        _pixels = ConvertFormat(rgba.Pixels, destinationFormat);
    }

    public BitmapSource? Source { get; set; }
    public PixelFormat DestinationFormat { get; set; } = PixelFormats.Bgra32;
    public BitmapPalette? DestinationPalette { get; set; }
    public double AlphaThreshold { get; set; }

    public override PixelFormat Format { get; }
    public override int PixelWidth { get; }
    public override int PixelHeight { get; }
    public override double DpiX { get; }
    public override double DpiY { get; }
    public override BitmapPalette? Palette { get; }
    internal override (byte[] Pixels, int Width, int Height)? RgbaPixels => (_pixels, PixelWidth, PixelHeight);

    internal static byte[] ConvertFormat(byte[] rgba, PixelFormat format)
    {
        var count = rgba.Length / 4;
        if (format == PixelFormats.Bgra32 || format == PixelFormats.Pbgra32 || format == PixelFormats.Bgr32)
            return (byte[])rgba.Clone();
        if (format == PixelFormats.Bgr24 || format == PixelFormats.Rgb24)
        {
            var out24 = new byte[count * 3];
            var swap = format == PixelFormats.Rgb24;
            for (var i = 0; i < count; i++)
            {
                out24[i * 3] = swap ? rgba[i * 4] : rgba[i * 4 + 2];
                out24[i * 3 + 1] = rgba[i * 4 + 1];
                out24[i * 3 + 2] = swap ? rgba[i * 4 + 2] : rgba[i * 4];
            }
            return out24;
        }
        if (format == PixelFormats.Gray8)
        {
            var gray = new byte[count];
            for (var i = 0; i < count; i++)
                gray[i] = (byte)((rgba[i * 4] * 299 + rgba[i * 4 + 1] * 587 + rgba[i * 4 + 2] * 114) / 1000);
            return gray;
        }
        throw new InvalidOperationException($"Conversion to {PixelFormats.NameOf(format)} stays a precise boundary (Bgra32/Pbgra32/Bgr32/Bgr24/Rgb24/Gray8 convert).");
    }
}

/// <summary>Transformed bitmap (matches System.Windows.Media.Imaging.TransformedBitmap).</summary>
public sealed class TransformedBitmap : BitmapSource
{
    private readonly byte[] _pixels;

    public TransformedBitmap()
    {
        PixelWidth = 0;
        PixelHeight = 0;
        DpiX = 96;
        DpiY = 96;
        Format = PixelFormats.Bgra32;
        Transform = MatrixTransform.Identity;
        _pixels = [];
    }

    public TransformedBitmap(BitmapSource source, Transform transform)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(transform);
        var rgba = source.RequirePixels();
        var m = transform.Value;
        if (!m.HasInverse)
            throw new InvalidOperationException("TransformedBitmap needs an invertible transform.");
        var corners = new[]
        {
            m.Transform(new WpfPoint(0, 0)), m.Transform(new WpfPoint(rgba.Width, 0)),
            m.Transform(new WpfPoint(0, rgba.Height)), m.Transform(new WpfPoint(rgba.Width, rgba.Height)),
        };
        var minX = corners.Min(p => p.X);
        var minY = corners.Min(p => p.Y);
        PixelWidth = Math.Max(1, (int)Math.Ceiling(corners.Max(p => p.X) - minX));
        PixelHeight = Math.Max(1, (int)Math.Ceiling(corners.Max(p => p.Y) - minY));
        DpiX = source.DpiX;
        DpiY = source.DpiY;
        Format = source.Format;
        Palette = source.Palette;
        _pixels = new byte[checked(PixelWidth * PixelHeight * 4)];
        var inv = m.Inverse;
        for (var y = 0; y < PixelHeight; y++)
            for (var x = 0; x < PixelWidth; x++)
            {
                var src = inv.Transform(new WpfPoint(x + minX + 0.5, y + minY + 0.5));
                var sx = (int)Math.Floor(src.X);
                var sy = (int)Math.Floor(src.Y);
                if ((uint)sx >= (uint)rgba.Width || (uint)sy >= (uint)rgba.Height) continue;
                Buffer.BlockCopy(rgba.Pixels, (sy * rgba.Width + sx) * 4, _pixels, (y * PixelWidth + x) * 4, 4);
            }
    }

    public BitmapSource? Source { get; set; }
    public Transform Transform { get; set; } = MatrixTransform.Identity;

    public override PixelFormat Format { get; }
    public override int PixelWidth { get; }
    public override int PixelHeight { get; }
    public override double DpiX { get; }
    public override double DpiY { get; }
    public override BitmapPalette? Palette { get; }
    internal override (byte[] Pixels, int Width, int Height)? RgbaPixels => (_pixels, PixelWidth, PixelHeight);
}

/// <summary>Render target (matches System.Windows.Media.Imaging.RenderTargetBitmap).</summary>
public sealed class RenderTargetBitmap : BitmapSource
{
    private byte[] _pixels;

    public RenderTargetBitmap(int pixelWidth, int pixelHeight, double dpiX, double dpiY, PixelFormat pixelFormat)
    {
        if (pixelWidth < 0 || pixelHeight < 0) throw new ArgumentOutOfRangeException("Size is non-negative.");
        if (pixelFormat != PixelFormats.Bgra32 && pixelFormat != PixelFormats.Pbgra32 &&
            pixelFormat != PixelFormats.Bgr32 && pixelFormat != PixelFormats.Bgr24)
            throw new InvalidOperationException($"RenderTargetBitmap format {PixelFormats.NameOf(pixelFormat)} stays a precise boundary (Bgra32/Pbgra32/Bgr32/Bgr24 render).");
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DpiX = dpiX <= 0 ? 96 : dpiX;
        DpiY = dpiY <= 0 ? 96 : dpiY;
        Format = pixelFormat;
        _pixels = new byte[checked(pixelWidth * pixelHeight * 4)];
    }

    public override PixelFormat Format { get; }
    public override int PixelWidth { get; }
    public override int PixelHeight { get; }
    public override double DpiX { get; }
    public override double DpiY { get; }
    internal override (byte[] Pixels, int Width, int Height)? RgbaPixels => (_pixels, PixelWidth, PixelHeight);

    /// <summary>Replays a container's hosted visuals into a command list.</summary>
    private static IReadOnlyList<DrawCommand> ReplayContainer(ContainerVisual container)
    {
        var dc = new DrawingContext();
        container.Render(dc);
        return dc.Commands;
    }

    /// <summary>Rasterizes a visual at the target size (UIElement, DrawingVisual and ContainerVisual).</summary>
    public void Render(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        IReadOnlyList<DrawCommand> commands = visual switch
        {
            UIElement element => WasmMilCore.RenderHeadless(element, new WpfSize(PixelWidth, PixelHeight)),
            DrawingVisual drawing => drawing.Recorded,
            ContainerVisual container => ReplayContainer(container),
            _ => throw new InvalidOperationException($"RenderTargetBitmap cannot render <{visual.GetType().Name}> (UIElement/DrawingVisual/ContainerVisual only)."),
        };
        using var surface = new SkiaSharp.SKBitmap(PixelWidth, PixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(surface))
        {
            canvas.Clear(SKColors.Transparent);
            using var renderer = new SkiaCommandRenderer();
            renderer.Replay(canvas, commands);
        }
        using var image = SKImage.FromBitmap(surface);
        var info = new SKImageInfo(PixelWidth, PixelHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bytes = new byte[checked(PixelWidth * PixelHeight * 4)];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            if (!image.ReadPixels(info, pin.AddrOfPinnedObject(), PixelWidth * 4, 0, 0))
                throw new InvalidOperationException("RenderTargetBitmap could not read back the raster.");
        }
        finally
        {
            pin.Free();
        }
        if (Format == PixelFormats.Bgr24)
            bytes = TakeChannels(bytes, 3, swapRedBlue: true);
        _pixels = bytes;
    }

    private static byte[] TakeChannels(byte[] rgba, int channels, bool swapRedBlue)
    {
        var count = rgba.Length / 4;
        var out24 = new byte[count * channels];
        for (var i = 0; i < count; i++)
        {
            out24[i * 3] = swapRedBlue ? rgba[i * 4 + 2] : rgba[i * 4];
            out24[i * 3 + 1] = rgba[i * 4 + 1];
            out24[i * 3 + 2] = swapRedBlue ? rgba[i * 4] : rgba[i * 4 + 2];
        }
        return out24;
    }
}

/// <summary>Visual root (matches System.Windows.Media.Visual).</summary>
public abstract class Visual : DependencyObject
{
    private readonly List<Visual> _visualChildren = new();

    /// <summary>
    /// Visual parent (set by <see cref="AddVisualChild"/> and
    /// <see cref="VisualCollection"/>). Logical parenting
    /// (<see cref="UIElement.Parent"/>) is untouched, so existing
    /// GetParent callers keep their behavior until a visual parent exists.
    /// </summary>
    public DependencyObject? VisualParent { get; internal set; }

    /// <summary>
    /// Retained visual children (backed by AddVisualChild/RemoveVisualChild;
    /// converted ScreenToGif adorners override the public getters below
    /// with their own VisualCollection while this list stays consistent
    /// for the collection-managed children).
    /// </summary>
    internal IReadOnlyList<Visual> VisualChildren => _visualChildren;

    /// <summary>Visual child count (matches Visual.VisualChildrenCount).</summary>
    protected virtual int VisualChildrenCount => _visualChildren.Count;

    /// <summary>Visual child at an index (matches Visual.GetVisualChild).</summary>
    protected virtual Visual GetVisualChild(int index) => _visualChildren[index];

    /// <summary>Same-assembly dispatch into the protected visual-child store.</summary>
    internal int VisualChildCount => VisualChildrenCount;

    /// <summary>Same-assembly dispatch into the protected visual-child store.</summary>
    internal Visual VisualChildAt(int index) => GetVisualChild(index);

    /// <summary>Adds a visual child (matches Visual.AddVisualChild).</summary>
    protected void AddVisualChild(Visual child)
    {
        ArgumentNullException.ThrowIfNull(child);
        AdoptVisualChild(this, child);
    }

    /// <summary>Removes a visual child (absent children are a no-op).</summary>
    protected void RemoveVisualChild(Visual? child)
    {
        if (child is null) return;
        DetachVisualChild(this, child);
    }

    /// <summary>Shared adoption core (backs AddVisualChild and VisualCollection).</summary>
    internal static void AdoptVisualChild(Visual parent, Visual child)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        if (ReferenceEquals(parent, child))
            throw new InvalidOperationException("A visual cannot parent itself.");
        if (child.VisualParent is not null && !ReferenceEquals(child.VisualParent, parent))
            throw new InvalidOperationException(
                $"'{child.GetType().Name}' is already parented to another visual.");
        if (parent._visualChildren.Contains(child)) return;
        parent._visualChildren.Add(child);
        var old = child.VisualParent;
        child.VisualParent = parent;
        parent.OnVisualChildrenChanged(child, null);
        child.OnVisualParentChanged(old);
    }

    /// <summary>Shared detach core (backs RemoveVisualChild and VisualCollection).</summary>
    internal static void DetachVisualChild(Visual parent, Visual child)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        if (!parent._visualChildren.Remove(child)) return;
        child.VisualParent = null;
        parent.OnVisualChildrenChanged(null, child);
        child.OnVisualParentChanged(parent);
    }

    /// <summary>Visual-children change hook (matches OnVisualChildrenChanged).</summary>
    protected internal virtual void OnVisualChildrenChanged(DependencyObject? visualAdded, DependencyObject? visualRemoved)
    {
    }

    /// <summary>Visual-parent change hook (matches OnVisualParentChanged).</summary>
    protected internal virtual void OnVisualParentChanged(DependencyObject? oldParent)
    {
    }

    /// <summary>
    /// Point hit-test hook (matches Visual.HitTestCore): the engine walk does
    /// not dispatch through it yet, so the default declines; converted visuals
    /// (AvalonEdit text lines) override it like WPF.
    /// </summary>
    protected virtual HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;

    /// <summary>
    /// Geometry hit-test hook (matches Visual.HitTestCore): default declines,
    /// like the point hook above.
    /// </summary>
    protected virtual GeometryHitTestResult? HitTestCore(GeometryHitTestParameters hitTestParameters) => null;

    /// <summary>
    /// Transform from this visual to an ancestor (matches
    /// Visual.TransformToAncestor): composes each element's layout offset
    /// and render transform up the visual/logical parent chain. A missing
    /// ancestor throws, like WPF. Converted OxyPlot DPI sync uses it to
    /// place canvas ink relative to the host window.
    /// </summary>
    public GeneralTransform TransformToAncestor(Visual ancestor)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        if (ReferenceEquals(this, ancestor)) return Transform.Identity;
        var matrix = Matrix.Identity;
        for (var current = (DependencyObject?)this; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
                return matrix.IsIdentity ? Transform.Identity : new MatrixTransform(matrix);
            if (current is FrameworkElement element)
                matrix = matrix * AccumulateStep(element);
        }
        throw new InvalidOperationException("The visual is not an ancestor of this visual.");
    }

    /// <summary>One visual's layout offset plus render transform step (shared
    /// by TransformToAncestor and the root accumulation behind
    /// TranslatePoint, so both compose identically).</summary>
    internal static Matrix AccumulateStep(FrameworkElement element)
    {
        var rect = element.RenderRect;
        var step = Matrix.Identity;
        if (element.RenderTransform is { } render && !render.Value.IsIdentity)
        {
            var origin = element.RenderTransformOrigin;
            var ox = origin.X * rect.Width;
            var oy = origin.Y * rect.Height;
            step = new Matrix(1, 0, 0, 1, -ox, -oy) * render.Value * new Matrix(1, 0, 0, 1, ox, oy);
        }
        return step * new Matrix(1, 0, 0, 1, rect.X, rect.Y);
    }

    /// <summary>Composed layout offsets from this visual to the tree root
    /// (backs TranslatePoint, which re-bases through the target's chain).</summary>
    internal Matrix TransformToRoot()
    {
        var matrix = Matrix.Identity;
        for (var current = (DependencyObject?)this; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement element)
                matrix = matrix * AccumulateStep(element);
        }
        return matrix;
    }

    /// <summary>
    /// Logical point to device pixels (matches Visual.PointToScreen):
    /// composes the visual-to-root offsets and render transforms with the
    /// same step math as <see cref="TransformToAncestor"/>, then applies the
    /// session device scale (HwndTarget.DeviceScale; 96-dpi-normalized like
    /// the DPI helpers). Converted AvalonDock DPI extensions bind it.
    /// </summary>
    public Point PointToScreen(Point point)
    {
        var matrix = Matrix.Identity;
        for (var current = (DependencyObject?)this; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement element)
            {
                var rect = element.RenderRect;
                var step = Matrix.Identity;
                if (element.RenderTransform is { } render && !render.Value.IsIdentity)
                {
                    var origin = element.RenderTransformOrigin;
                    var ox = origin.X * rect.Width;
                    var oy = origin.Y * rect.Height;
                    step = new Matrix(1, 0, 0, 1, -ox, -oy) * render.Value * new Matrix(1, 0, 0, 1, ox, oy);
                }
                step = step * new Matrix(1, 0, 0, 1, rect.X, rect.Y);
                matrix = matrix * step;
            }
        }
        var scale = HwndTarget.DeviceScale;
        return (matrix * new Matrix(scale, 0, 0, scale, 0, 0)).Transform(point);
    }
}

/// <summary>Drawing visual (matches System.Windows.Media.DrawingVisual : ContainerVisual; unsealed like WPF so converted visuals derive from it).</summary>
public class DrawingVisual : ContainerVisual
{
    private DrawingContext? _drawing;

    public DrawingContext RenderOpen() => _drawing = new DrawingContext();

    public DrawingGroup? Drawing => _drawing is null ? null : new DrawingGroup(_drawing);

    internal IReadOnlyList<DrawCommand> Recorded => _drawing?.Commands ?? [];
}

/// <summary>Image-backed drawing (matches System.Windows.Media.ImageDrawing).</summary>
public sealed class ImageDrawing : Drawing
{
    /// <summary>Creates an empty image drawing (source and rect unset).</summary>
    public ImageDrawing()
    {
    }

    /// <summary>Creates an image drawing over the given source and rect.</summary>
    public ImageDrawing(ImageSource imageSource, Rect rect)
    {
        ImageSource = imageSource;
        Rect = rect;
    }

    /// <summary>Painted image (retained; the shared tile-brush fallback paints it).</summary>
    public ImageSource? ImageSource { get; set; }

    /// <summary>Source rect (matches ImageDrawing.Rect).</summary>
    public Rect Rect { get; set; }
}

/// <summary>Retained drawing (matches System.Windows.Media.DrawingGroup).</summary>
public sealed class DrawingGroup : Drawing
{
    /// <summary>Child drawings (XAML and code-built; the recorded context stays separate).</summary>
    public DrawingCollection Children { get; } = new();

    /// <summary>Creates an empty drawing group (matches the WPF parameterless constructor).</summary>
    public DrawingGroup() : this(new DrawingContext())
    {
    }

    internal DrawingGroup(DrawingContext context) => Context = context;

    internal DrawingContext Context { get; }
}

/// <summary>Visual tree helpers (matches System.Windows.Media.VisualTreeHelper).</summary>
public static partial class VisualTreeHelper
{
    private static DpiScale _rootDpi = new(1.0, 1.0);

    /// <summary>
    /// Session DPI scale (matches GetDpi): 1.0 until the host syncs the
    /// display scale. Tests pin it through SetRootDpiForTests.
    /// </summary>
    public static DpiScale GetDpi(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        return _rootDpi;
    }

    /// <summary>
    /// Session DPI scale setter (engine host-sync entry; WPF keeps this
    /// internal, but the browser host must follow display scale changes).
    /// </summary>
    public static void SetRootDpi(DpiScale dpi) => _rootDpi = dpi;

    public static int GetChildrenCount(DependencyObject reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference is FrameworkElement fe) return fe.LogicalChildren.Count;
        if (reference is Visual visual) return visual.VisualChildCount;
        return 0;
    }

    public static DependencyObject GetChild(DependencyObject reference, int childIndex)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference is FrameworkElement fe && (uint)childIndex < (uint)fe.LogicalChildren.Count)
            return fe.LogicalChildren[childIndex];
        if (reference is Visual visual && reference is not FrameworkElement &&
            (uint)childIndex < (uint)visual.VisualChildCount)
            return visual.VisualChildAt(childIndex);
        throw new ArgumentOutOfRangeException(nameof(childIndex));
    }

    public static DependencyObject? GetParent(DependencyObject reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        // Visual parenting (VisualCollection/Adorner children) wins over
        // the logical link, like WPF's visual-parent-first lookup; the
        // logical parent still answers for AddChild-hosted content.
        if (reference is Visual visual && visual.VisualParent is not null)
            return visual.VisualParent;
        return reference is FrameworkElement fe ? fe.Parent : null;
    }
}

/// <summary>2D affine matrix (matches System.Windows.Media.Matrix).</summary>
public struct Matrix : IEquatable<Matrix>, IFormattable
{
    public static Matrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public Matrix(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
    {
        M11 = m11;
        M12 = m12;
        M21 = m21;
        M22 = m22;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public double M11 { get; set; }
    public double M12 { get; set; }
    public double M21 { get; set; }
    public double M22 { get; set; }
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }

    public double Determinant => M11 * M22 - M12 * M21;
    public bool HasInverse => Determinant != 0;
    public bool IsIdentity => this == Identity;

    public Matrix Inverse
    {
        get
        {
            var det = Determinant;
            if (det == 0) throw new InvalidOperationException("Matrix is not invertible.");
            return new Matrix(M22 / det, -M12 / det, -M21 / det, M11 / det,
                (M21 * OffsetY - M22 * OffsetX) / det, (M12 * OffsetX - M11 * OffsetY) / det);
        }
    }

    /// <summary>
    /// Appends a scale (matches Matrix.Scale; converted shell DPI helpers
    /// scale their device matrix in the static constructor). Mutable like
    /// real WPF (the engine struct dropped its readonly marker for this).
    /// </summary>
    public void Scale(double scaleX, double scaleY)
    {
        M11 *= scaleX;
        M12 *= scaleY;
        M21 *= scaleX;
        M22 *= scaleY;
        OffsetX *= scaleX;
        OffsetY *= scaleY;
    }

    public WpfPoint Transform(WpfPoint point) => new(
        point.X * M11 + point.Y * M21 + OffsetX,
        point.X * M12 + point.Y * M22 + OffsetY);

    /// <summary>
    /// WPF-shape transform (matches Matrix.Transform(Point); converted shell
    /// DPI helpers bind it while the layout core stays on WpfPoint).
    /// </summary>
    public Point Transform(Point point) => new(
        point.X * M11 + point.Y * M21 + OffsetX,
        point.X * M12 + point.Y * M22 + OffsetY);

    public static Matrix operator *(Matrix left, Matrix right) => new(
        left.M11 * right.M11 + left.M12 * right.M21,
        left.M11 * right.M12 + left.M12 * right.M22,
        left.M21 * right.M11 + left.M22 * right.M21,
        left.M21 * right.M12 + left.M22 * right.M22,
        left.OffsetX * right.M11 + left.OffsetY * right.M21 + right.OffsetX,
        left.OffsetX * right.M12 + left.OffsetY * right.M22 + right.OffsetY);

    public static bool operator ==(Matrix left, Matrix right) =>
        left.M11 == right.M11 && left.M12 == right.M12 && left.M21 == right.M21 &&
        left.M22 == right.M22 && left.OffsetX == right.OffsetX && left.OffsetY == right.OffsetY;
    public static bool operator !=(Matrix left, Matrix right) => !(left == right);

    public bool Equals(Matrix other) => this == other;
    public override bool Equals(object? obj) => obj is Matrix other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(M11, M12, M21, M22, OffsetX, OffsetY);
    public override string ToString() => $"{M11},{M12},{M21},{M22},{OffsetX},{OffsetY}";
    public string ToString(string? format, IFormatProvider? provider) => ToString();
}

/// <summary>Transform root (matches System.Windows.Media.GeneralTransform).</summary>
public abstract class GeneralTransform : Animatable
{
    public abstract GeneralTransform? Inverse { get; }
    public abstract Rect TransformBounds(Rect rect);
    public abstract bool TryTransform(Point point, out Point result);

    /// <summary>
    /// Transforms a point, throwing when it cannot be transformed (matches
    /// GeneralTransform.Transform; converted OxyPlot DPI sync calls it on
    /// the ancestor transform unconditionally).
    /// </summary>
    public Point Transform(Point point) =>
        TryTransform(point, out var result)
            ? result
            : throw new InvalidOperationException("The transform cannot transform the point.");
}

/// <summary>2D transform root (matches System.Windows.Media.Transform).</summary>
public abstract class Transform : GeneralTransform
{
    public static Transform Identity => MatrixTransform.Identity;

    public abstract Matrix Value { get; }

    public override GeneralTransform? Inverse => HasInverse ? new MatrixTransform(Value.Inverse) : null;
    public bool HasInverse => Value.HasInverse;

    public override Rect TransformBounds(Rect rect)
    {
        var corners = new[]
        {
            TransformPoint(new Point(rect.X, rect.Y)), TransformPoint(new Point(rect.X + rect.Width, rect.Y)),
            TransformPoint(new Point(rect.X, rect.Y + rect.Height)), TransformPoint(new Point(rect.X + rect.Width, rect.Y + rect.Height)),
        };
        var minX = corners.Min(p => p.X);
        var minY = corners.Min(p => p.Y);
        return new Rect(minX, minY, corners.Max(p => p.X) - minX, corners.Max(p => p.Y) - minY);
    }

    public override bool TryTransform(Point point, out Point result)
    {
        result = TransformPoint(point);
        return true;
    }

    public Point TransformPoint(Point point)
    {
        var v = Value.Transform(new WpfPoint(point.X, point.Y));
        return new Point(v.X, v.Y);
    }
}

/// <summary>Translate transform (matches System.Windows.Media.TranslateTransform).</summary>
public sealed class TranslateTransform : Transform
{
    public TranslateTransform()
    {
    }

    public TranslateTransform(double offsetX, double offsetY)
    {
        X = offsetX;
        Y = offsetY;
    }

    public double X { get; set; }
    public double Y { get; set; }

    public override Matrix Value => new(1, 0, 0, 1, X, Y);
}

/// <summary>Rotate transform (matches System.Windows.Media.RotateTransform).</summary>
public sealed class RotateTransform : Transform
{
    public RotateTransform()
    {
    }

    public RotateTransform(double angle) => Angle = angle;

    public RotateTransform(double angle, double centerX, double centerY)
    {
        Angle = angle;
        CenterX = centerX;
        CenterY = centerY;
    }

    public double Angle { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }

    public override Matrix Value
    {
        get
        {
            var radians = Angle * Math.PI / 180.0;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            var dx = CenterX * (1 - cos) + CenterY * sin;
            var dy = CenterY * (1 - cos) - CenterX * sin;
            return new Matrix(cos, sin, -sin, cos, dx, dy);
        }
    }
}

/// <summary>Scale transform (matches System.Windows.Media.ScaleTransform).</summary>
public sealed class ScaleTransform : Transform
{
    public ScaleTransform()
    {
    }

    public ScaleTransform(double scaleX, double scaleY)
    {
        ScaleX = scaleX;
        ScaleY = scaleY;
    }

    public ScaleTransform(double scaleX, double scaleY, double centerX, double centerY)
        : this(scaleX, scaleY)
    {
        CenterX = centerX;
        CenterY = centerY;
    }

    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    public double CenterX { get; set; }
    public double CenterY { get; set; }

    public override Matrix Value => new(ScaleX, 0, 0, ScaleY, CenterX - ScaleX * CenterX, CenterY - ScaleY * CenterY);
}

/// <summary>
/// Skew transform (matches System.Windows.Media.SkewTransform): shearing by
/// angle about an optional center. The Value matrix is live-verified
/// against the net9.0-windows runtime (M12 = tan(AngleY), M21 =
/// tan(AngleX), center-compensated offsets).
/// </summary>
public sealed class SkewTransform : Transform
{
    public SkewTransform()
    {
    }

    public SkewTransform(double angleX, double angleY)
    {
        AngleX = angleX;
        AngleY = angleY;
    }

    public SkewTransform(double angleX, double angleY, double centerX, double centerY)
        : this(angleX, angleY)
    {
        CenterX = centerX;
        CenterY = centerY;
    }

    public double AngleX { get; set; }

    public double AngleY { get; set; }

    public double CenterX { get; set; }

    public double CenterY { get; set; }

    public override Matrix Value
    {
        get
        {
            var tanX = Math.Tan(AngleX * Math.PI / 180.0);
            var tanY = Math.Tan(AngleY * Math.PI / 180.0);
            return new Matrix(1, tanY, tanX, 1, -tanX * CenterY, -tanY * CenterX);
        }
    }
}

/// <summary>Matrix transform (matches System.Windows.Media.MatrixTransform).</summary>
public sealed class MatrixTransform : Transform
{
    public static new MatrixTransform Identity { get; } = new(Matrix.Identity);

    public MatrixTransform()
        : this(Matrix.Identity)
    {
    }

    public MatrixTransform(Matrix matrix) => Matrix = matrix;

    /// <summary>
    /// Element-wise transform (matches MatrixTransform(double, ...); converted
    /// shell chrome workers bind it for their framework-issue fixups).
    /// </summary>
    public MatrixTransform(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
        => Matrix = new Matrix(m11, m12, m21, m22, offsetX, offsetY);

    public Matrix Matrix { get; set; }

    public override Matrix Value => Matrix;
}

/// <summary>Transform group (matches System.Windows.Media.TransformGroup).</summary>
public sealed class TransformGroup : Transform
{
    public TransformCollection Children { get; } = new();

    public override Matrix Value
    {
        get
        {
            var result = Matrix.Identity;
            foreach (var child in Children) result *= child.Value;
            return result;
        }
    }
}

/// <summary>Transform collection (matches System.Windows.Media.TransformCollection).</summary>
public sealed class TransformCollection : List<Transform>
{
    /// <summary>Value enumerator (matches TransformCollection.Enumerator; converted foreach binds here).</summary>
    public new Enumerator GetEnumerator() => new(this);

    /// <summary>Transform enumerator (matches System.Windows.Media.TransformCollection/Enumerator).</summary>
    public struct Enumerator : IEnumerator<Transform>
    {
        private readonly List<Transform> _list;
        private int _index;

        internal Enumerator(List<Transform> list)
        {
            _list = list;
            _index = -1;
        }

        public Transform Current => (uint)_index < (uint)_list.Count
            ? _list[_index]
            : throw new InvalidOperationException("Enumeration has not started or has already finished.");

        object System.Collections.IEnumerator.Current => Current;
        public bool MoveNext() => ++_index < _list.Count;
        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}

/// <summary>Render options (matches System.Windows.RenderOptions).</summary>
/// <summary>ClearType hint (matches System.Windows.Media.ClearTypeHint; verified against live .NET: Auto=0, Enabled=1).</summary>
public enum ClearTypeHint
{
    Auto = 0,
    Enabled = 1,
}

public static class RenderOptions
{
    /// <summary>
    /// Edge-mode attached contract (matches RenderOptions.EdgeModeProperty):
    /// converted OxyPlot stroke setup sets it through the DP, template
    /// inflation lands here too, and the XAML/template parsers dual-write
    /// the attached bag like the ScrollViewer knobs.
    /// </summary>
    public static readonly DependencyProperty EdgeModeProperty =
        DependencyProperty.RegisterAttached("EdgeMode", typeof(EdgeMode), typeof(RenderOptions), EdgeMode.Unspecified);

    /// <summary>ClearType hint attached contract (matches RenderOptions.ClearTypeHintProperty).</summary>
    public static readonly DependencyProperty ClearTypeHintProperty =
        DependencyProperty.RegisterAttached("ClearTypeHint", typeof(ClearTypeHint), typeof(RenderOptions), ClearTypeHint.Auto);

    /// <summary>Reads the ClearType hint (matches RenderOptions.GetClearTypeHint; unset means Auto).</summary>
    public static ClearTypeHint GetClearTypeHint(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(ClearTypeHintProperty) is ClearTypeHint hint ? hint : ClearTypeHint.Auto;
    }

    /// <summary>Stores the ClearType hint (matches RenderOptions.SetClearTypeHint; OxyPlot's CanvasRenderContext sets Enabled on its text blocks).</summary>
    public static void SetClearTypeHint(DependencyObject target, ClearTypeHint value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(ClearTypeHintProperty, value);
    }

    /// <summary>Caching-hint attached contract (matches RenderOptions.CachingHintProperty).</summary>
    public static readonly DependencyProperty CachingHintProperty =
        DependencyProperty.RegisterAttached("CachingHint", typeof(CachingHint), typeof(RenderOptions), CachingHint.Unspecified);

    /// <summary>Reads the caching hint (matches RenderOptions.GetCachingHint; unset means Unspecified).</summary>
    public static CachingHint GetCachingHint(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(CachingHintProperty) is CachingHint hint ? hint : CachingHint.Unspecified;
    }

    /// <summary>Stores the caching hint (matches RenderOptions.SetCachingHint; the converted MaterialDesign FlipperClassic sets Cache on its plane).</summary>
    public static void SetCachingHint(DependencyObject target, CachingHint value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(CachingHintProperty, value);
    }

    /// <summary>Cache-invalidation minimum attached contract (matches RenderOptions.CacheInvalidationThresholdMinimumProperty).</summary>
    public static readonly DependencyProperty CacheInvalidationThresholdMinimumProperty =
        DependencyProperty.RegisterAttached("CacheInvalidationThresholdMinimum", typeof(double), typeof(RenderOptions), 0.0);

    /// <summary>Reads the cache-invalidation minimum (matches RenderOptions.GetCacheInvalidationThresholdMinimum).</summary>
    public static double GetCacheInvalidationThresholdMinimum(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(CacheInvalidationThresholdMinimumProperty) is double value ? value : 0.0;
    }

    /// <summary>Stores the cache-invalidation minimum (matches RenderOptions.SetCacheInvalidationThresholdMinimum).</summary>
    public static void SetCacheInvalidationThresholdMinimum(DependencyObject target, double value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(CacheInvalidationThresholdMinimumProperty, value);
    }

    /// <summary>Cache-invalidation maximum attached contract (matches RenderOptions.CacheInvalidationThresholdMaximumProperty).</summary>
    public static readonly DependencyProperty CacheInvalidationThresholdMaximumProperty =
        DependencyProperty.RegisterAttached("CacheInvalidationThresholdMaximum", typeof(double), typeof(RenderOptions), 1.0);

    /// <summary>Reads the cache-invalidation maximum (matches RenderOptions.GetCacheInvalidationThresholdMaximum).</summary>
    public static double GetCacheInvalidationThresholdMaximum(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(CacheInvalidationThresholdMaximumProperty) is double value ? value : 1.0;
    }

    /// <summary>Stores the cache-invalidation maximum (matches RenderOptions.SetCacheInvalidationThresholdMaximum).</summary>
    public static void SetCacheInvalidationThresholdMaximum(DependencyObject target, double value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(CacheInvalidationThresholdMaximumProperty, value);
    }

    public static BitmapScalingMode GetBitmapScalingMode(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target is UIElement e && e.GetAttached("RenderOptions.BitmapScalingMode") is BitmapScalingMode mode
            ? mode
            : BitmapScalingMode.Unspecified;
    }

    public static void SetBitmapScalingMode(DependencyObject target, BitmapScalingMode value)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target is UIElement e) e.SetAttached("RenderOptions.BitmapScalingMode", value);
    }

    public static EdgeMode GetEdgeMode(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.GetValue(EdgeModeProperty) is EdgeMode mode && mode != EdgeMode.Unspecified)
            return mode;
        return target is UIElement e && e.GetAttached("RenderOptions.EdgeMode") is EdgeMode bagged
            ? bagged
            : EdgeMode.Unspecified;
    }

    public static void SetEdgeMode(DependencyObject target, EdgeMode value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(EdgeModeProperty, value);
        if (target is UIElement e) e.SetAttached("RenderOptions.EdgeMode", value);
    }

    /// <summary>
    /// Session compositor mode (matches RenderOptions.ProcessRenderMode;
    /// converted ScreenToGif App disables hardware acceleration through
    /// this when the user setting is on; the engine keeps one retained
    /// compositor, so SoftwareOnly selects the CPU replay path).
    /// </summary>
    public static RenderMode ProcessRenderMode { get; set; } = RenderMode.Default;
}
