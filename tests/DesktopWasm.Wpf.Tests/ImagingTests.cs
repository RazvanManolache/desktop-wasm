using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Step 42: imaging pipeline, Image control, 2D transforms.</summary>
[ResetAmbientState]
public sealed class ImagingTests
{
    private static byte[] Rgba(params byte[] values) => values;

    private static WriteableBitmap TwoByOne()
    {
        var bitmap = new WriteableBitmap(2, 1, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, 2, 1),
            Rgba(255, 0, 0, 255, 0, 255, 0, 255), 8, 0);
        return bitmap;
    }

    private static byte[] PngBytes(BitmapSource source)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void PixelFormats_catalog()
    {
        Assert.Equal(32, PixelFormats.Bgra32.BitsPerPixel);
        Assert.Equal(8, PixelFormats.Gray8.BitsPerPixel);
        Assert.Equal(1, PixelFormats.Indexed1.BitsPerPixel);
        Assert.Equal(PixelFormats.Bgra32, PixelFormats.Default);
        Assert.True(PixelFormats.Bgra32 == PixelFormats.Bgra32);
        Assert.True(PixelFormats.Bgra32 != PixelFormats.Bgr24);
        Assert.Equal("Bgra32", PixelFormats.Bgra32.ToString());
    }

    [Fact]
    public void BitmapPalette_round_trips()
    {
        var palette = new BitmapPalette(new List<Color> { Colors.Red, Colors.Blue });
        Assert.Equal(2, palette.Count);
        Assert.Equal(Colors.Red, palette[0]);
        var grays = new BitmapPalette(TwoByOne(), 2);
        Assert.Equal(2, grays.Count);
    }

    [Fact]
    public void BitmapSource_create_and_copy()
    {
        var source = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null,
            Rgba(1, 2, 3, 4, 5, 6, 7, 8), 8);
        Assert.Equal(2, source.PixelWidth);
        Assert.Equal(1, source.PixelHeight);
        Assert.Equal(96, source.DpiX);
        var pixels = new byte[8];
        source.CopyPixels(pixels, 8, 0);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, pixels);
        var sub = new byte[4];
        source.CopyPixels(new Int32Rect(1, 0, 1, 1), sub, 4, 0);
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, sub);
    }

    [Fact]
    public void WriteableBitmap_write_lock_backbuffer()
    {
        var bitmap = new WriteableBitmap(2, 1, 96, 96, PixelFormats.Bgra32, null);
        Assert.Equal(IntPtr.Zero, bitmap.BackBuffer);
        Assert.Equal(8, bitmap.BackBufferStride);
        bitmap.Lock();
        try
        {
            Assert.NotEqual(IntPtr.Zero, bitmap.BackBuffer);
            bitmap.WritePixels(new Int32Rect(0, 0, 2, 1), Rgba(9, 9, 9, 9, 8, 8, 8, 8), 8, 0);
            Assert.Throws<InvalidOperationException>(() => new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null).Unlock());
        }
        finally
        {
            bitmap.Unlock();
        }
        Assert.Equal(IntPtr.Zero, bitmap.BackBuffer);
        var pixels = new byte[8];
        bitmap.CopyPixels(pixels, 8, 0);
        Assert.Equal(new byte[] { 9, 9, 9, 9, 8, 8, 8, 8 }, pixels);
        bitmap.AddDirtyRect(new Int32Rect(0, 0, 2, 1));
        Assert.False(bitmap.IsFrozen);
    }

    [Fact]
    public void FormatConvertedBitmap_converts()
    {
        var gray = new FormatConvertedBitmap(TwoByOne(), PixelFormats.Gray8, null, 0);
        Assert.Equal(PixelFormats.Gray8, gray.Format);
        var pixels = new byte[2];
        gray.CopyPixels(pixels, 2, 0);
        Assert.Equal((byte)((255 * 299) / 1000), pixels[0]);
        Assert.Equal((byte)((255 * 587) / 1000), pixels[1]);
        var noop = new FormatConvertedBitmap(TwoByOne(), PixelFormats.Bgra32, null, 0);
        Assert.Equal(8, noop.RequirePixels().Pixels.Length);
        Assert.Throws<InvalidOperationException>(() => new FormatConvertedBitmap(TwoByOne(), PixelFormats.Indexed8, null, 0));
    }

    [Fact]
    public void CroppedBitmap_crops()
    {
        var cropped = new CroppedBitmap(TwoByOne(), new Int32Rect(1, 0, 1, 1));
        Assert.Equal(1, cropped.PixelWidth);
        Assert.Equal(1, cropped.PixelHeight);
        var pixels = new byte[4];
        cropped.CopyPixels(pixels, 4, 0);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixels);
    }

    [Fact]
    public void TransformedBitmap_scales()
    {
        var scaled = new TransformedBitmap(TwoByOne(), new ScaleTransform(2, 2));
        Assert.Equal(4, scaled.PixelWidth);
        Assert.Equal(2, scaled.PixelHeight);
        var pixels = new byte[4 * 2 * 4];
        scaled.CopyPixels(pixels, 16, 0);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels[..4]);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixels[8..12]);
        Assert.Throws<InvalidOperationException>(() => new TransformedBitmap(TwoByOne(), new ScaleTransform(0, 0)));
    }

    [Fact]
    public void BitmapImage_decodes_stream()
    {
        using var stream = new MemoryStream(PngBytes(TwoByOne()));
        var image = new BitmapImage();
        var downloads = 0;
        image.DownloadCompleted += (s, e) => downloads++;
        image.BeginInit();
        image.StreamSource = stream;
        image.EndInit();
        image.EndInit();
        Assert.Equal(2, image.PixelWidth);
        Assert.Equal(1, image.PixelHeight);
        Assert.Equal(PixelFormats.Bgra32, image.Format);
        Assert.Equal(1, downloads);
        var pixels = new byte[8];
        image.CopyPixels(pixels, 8, 0);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }, pixels);
    }

    [Fact]
    public void BitmapFrame_create_paths()
    {
        using var stream = new MemoryStream(PngBytes(TwoByOne()));
        var fromStream = BitmapFrame.Create(stream);
        Assert.Equal(2, fromStream.PixelWidth);
        var fromSource = BitmapFrame.Create(TwoByOne());
        Assert.Equal(PixelFormats.Bgra32, fromSource.Format);
        var pixels = new byte[8];
        fromSource.CopyPixels(pixels, 8, 0);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }, pixels);
    }

    [Fact]
    public void Encoders_round_trip()
    {
        var source = TwoByOne();
        using var png = new MemoryStream();
        var pngEncoder = new PngBitmapEncoder();
        pngEncoder.Frames.Add(BitmapFrame.Create(source));
        pngEncoder.Save(png);
        Assert.True(png.Length > 8);
        using var jpeg = new MemoryStream();
        var jpegEncoder = new JpegBitmapEncoder { QualityLevel = 90 };
        jpegEncoder.Frames.Add(BitmapFrame.Create(source));
        jpegEncoder.Save(jpeg);
        Assert.True(jpeg.Length > 8);
        using var bmp = new MemoryStream();
        var bmpEncoder = new BmpBitmapEncoder();
        bmpEncoder.Frames.Add(BitmapFrame.Create(source));
        bmpEncoder.Save(bmp);
        Assert.True(bmp.Length > 8);
        var decoded = BitmapFrame.Create(new MemoryStream(png.ToArray()));
        Assert.Equal(2, decoded.PixelWidth);
        Assert.Throws<InvalidOperationException>(() => new PngBitmapEncoder().Save(new MemoryStream()));
    }

    [Fact]
    public void Decoders_expose_frames()
    {
        var bytes = PngBytes(TwoByOne());
        var decoder = BitmapDecoder.Create(new MemoryStream(bytes));
        Assert.Single(decoder.Frames);
        Assert.Equal(2, decoder.Frames[0].PixelWidth);
        var gif = new GifBitmapDecoder(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.Default);
        Assert.Single(gif.Frames);
        var meta = new BitmapMetadata();
        Assert.False(meta.ContainsQuery("/app1/ifd/exif"));
        meta.SetQuery("/app1/ifd/exif", 42);
        Assert.True(meta.ContainsQuery("/app1/ifd/exif"));
        Assert.Equal(42, meta.GetQuery("/app1/ifd/exif"));
    }

    [Fact]
    public void Image_measures_source_and_renders_drawimage()
    {
        var image = new Image { Source = TwoByOne() };
        image.Measure(new WpfSize(100, 100));
        Assert.Equal(2, image.DesiredSize.Width);
        Assert.Equal(1, image.DesiredSize.Height);
        image.Arrange(new WpfRect(0, 0, 100, 100));
        var commands = WasmMilCore.RenderHeadless(image, new WpfSize(100, 100));
        var draw = Assert.Single(commands.OfType<DrawCommand.DrawImage>());
        Assert.Equal(2, draw.Width);
        Assert.Equal(1, draw.Height);
        Assert.Equal(100, draw.DW);
        Assert.Equal(50, draw.DH);
        Assert.Equal(8, draw.Rgba.Length);
    }

    [Fact]
    public void Image_xaml_source_and_stretch()
    {
        var image = Assert.IsType<Image>(XamlLoader.Load(
            "<Image xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "Source=\"missing.png\" Stretch=\"UniformToFill\"/>").Root);
        Assert.Equal(Stretch.UniformToFill, image.Stretch);
        Assert.IsType<BitmapImage>(image.Source);
        var nested = Assert.IsType<Image>(XamlLoader.Load(
            "<Image xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Image.Source><BitmapImage UriSource=\"other.png\"/></Image.Source></Image>").Root);
        Assert.IsType<BitmapImage>(nested.Source);
        Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<Image xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Stretch=\"Nope\"/>"));
    }

    [Fact]
    public void Image_resource_bitmapimage_applies()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Window.Resources><ResourceDictionary>" +
            "<BitmapImage x:Key=\"Logo\" UriSource=\"logo.png\"/>" +
            "</ResourceDictionary></Window.Resources>" +
            "<Image Source=\"{StaticResource Logo}\"/></Window>").Root);
        var image = Assert.IsType<Image>(window.Content);
        Assert.IsType<BitmapImage>(image.Source);
    }

    [Fact]
    public void Matrix_math()
    {
        Assert.True(Matrix.Identity.IsIdentity);
        var translate = new Matrix(1, 0, 0, 1, 5, 7);
        var p = translate.Transform(new WpfPoint(1, 2));
        Assert.Equal(6, p.X);
        Assert.Equal(9, p.Y);
        var scale = new Matrix(2, 0, 0, 3, 0, 0);
        var both = translate * scale;
        var q = both.Transform(new WpfPoint(1, 1));
        Assert.Equal(12, q.X);
        Assert.Equal(24, q.Y);
        Assert.True(both.HasInverse);
        Assert.False(new Matrix(1, 2, 2, 4, 0, 0).HasInverse);
        Assert.Throws<InvalidOperationException>(() => _ = new Matrix(1, 2, 2, 4, 0, 0).Inverse);
        var round = (both * both.Inverse).Transform(new WpfPoint(3, 4));
        Assert.Equal(3, round.X, precision: 9);
        Assert.Equal(4, round.Y, precision: 9);
    }

    [Fact]
    public void Transforms_compose()
    {
        var move = new TranslateTransform(5, 0);
        Assert.Equal(new Matrix(1, 0, 0, 1, 5, 0), move.Value);
        var scale = new ScaleTransform(2, 2);
        var group = new TransformGroup();
        group.Children.Add(move);
        group.Children.Add(scale);
        Assert.Equal(new Matrix(2, 0, 0, 2, 10, 0), group.Value);
        var rotate = new RotateTransform(90);
        var turned = rotate.TransformPoint(new Point(1, 0));
        Assert.Equal(0, turned.X, precision: 9);
        Assert.Equal(1, turned.Y, precision: 9);
        Assert.NotNull(group.Inverse);
        var bounds = scale.TransformBounds(new Rect(0, 0, 10, 10));
        Assert.Equal(20, bounds.Width);
        Assert.Equal(20, bounds.Height);
    }

    [Fact]
    public void RenderTargetBitmap_renders_visual()
    {
        var target = new RenderTargetBitmap(100, 30, 96, 96, PixelFormats.Bgra32);
        var button = new Button { Content = "Hi" };
        target.Render(button);
        var rgba = target.RequirePixels();
        Assert.Equal(100 * 30 * 4, rgba.Pixels.Length);
        Assert.True(rgba.Pixels.Any(b => b != 0));
        var bgr = new RenderTargetBitmap(10, 10, 96, 96, PixelFormats.Bgr24);
        bgr.Render(button);
        Assert.Equal(10 * 10 * 3, bgr.RequirePixels().Pixels.Length);
        Assert.Throws<InvalidOperationException>(() => new RenderTargetBitmap(10, 10, 96, 96, PixelFormats.Gray8));
    }

    [Fact]
    public void DrawingVisual_renders_through_target()
    {
        var drawing = new DrawingVisual();
        var dc = drawing.RenderOpen();
        dc.DrawRectangle(0, 0, 10, 10, 0xFF0000FF);
        var target = new RenderTargetBitmap(10, 10, 96, 96, PixelFormats.Bgra32);
        target.Render(drawing);
        Assert.True(target.RequirePixels().Pixels.Any(b => b != 0));
        var root = new StackPanel();
        var leaf = new Button();
        root.AddChild(leaf);
        Assert.Equal(1, VisualTreeHelper.GetChildrenCount(root));
        Assert.Same(leaf, VisualTreeHelper.GetChild(root, 0));
        Assert.Same(root, VisualTreeHelper.GetParent(leaf));
        Assert.IsAssignableFrom<Visual>(leaf);
        Assert.Throws<ArgumentOutOfRangeException>(() => VisualTreeHelper.GetChild(root, 3));
    }

    [Fact]
    public void RenderOptions_round_trip()
    {
        var button = new Button();
        Assert.Equal(BitmapScalingMode.Unspecified, RenderOptions.GetBitmapScalingMode(button));
        RenderOptions.SetBitmapScalingMode(button, BitmapScalingMode.NearestNeighbor);
        Assert.Equal(BitmapScalingMode.NearestNeighbor, RenderOptions.GetBitmapScalingMode(button));
        Assert.Equal(EdgeMode.Unspecified, RenderOptions.GetEdgeMode(button));
        RenderOptions.SetEdgeMode(button, EdgeMode.Aliased);
        Assert.Equal(EdgeMode.Aliased, RenderOptions.GetEdgeMode(button));
        Assert.Equal(CachingHint.Unspecified, RenderOptions.GetCachingHint(button));
        RenderOptions.SetCachingHint(button, CachingHint.Cache);
        Assert.Equal(CachingHint.Cache, RenderOptions.GetCachingHint(button));
        Assert.Equal(0.0, RenderOptions.GetCacheInvalidationThresholdMinimum(button));
        RenderOptions.SetCacheInvalidationThresholdMinimum(button, 0.5);
        Assert.Equal(0.5, RenderOptions.GetCacheInvalidationThresholdMinimum(button));
        Assert.Equal(1.0, RenderOptions.GetCacheInvalidationThresholdMaximum(button));
        RenderOptions.SetCacheInvalidationThresholdMaximum(button, 2.0);
        Assert.Equal(2.0, RenderOptions.GetCacheInvalidationThresholdMaximum(button));
    }
}
