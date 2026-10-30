using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Gallery card icon pipeline: the NavigationCardTemplate binds
/// Image.Source to ControlInfoDataItem.ImageSource (a pack Uri). The bound
/// Uri must land as a decoded BitmapImage so the card paints a DrawImage
/// (transport op 7) instead of an empty icon slot.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class GalleryCardImageTests : IDisposable
{
    private readonly Application? _previousApp = Application.Current;

    public void Dispose() => Application.Current = _previousApp;

    private sealed class CardItem
    {
        public string Title { get; set; } = "Button";

        public Uri ImageSource { get; set; } =
            new("pack://application:,,,/Pack/CardIconProbe.bin");
    }

    private sealed class StringCardItem
    {
        public Uri ImageSource => new("pack://application:,,,/Pack/CardIconProbe.bin");

        public string ImagePath => "pack://application:,,,/Pack/CardIconProbe.bin";
    }

    private static readonly byte[] ExpectedPixels = [255, 0, 0, 255, 0, 255, 0, 255];

    [Fact]
    public void Bound_pack_uri_lands_decoded_bitmapimage()
    {
        var image = new Image();
        image.DataContext = new CardItem();
        image.SetBinding(nameof(Image.Source), new Binding(nameof(CardItem.ImageSource)));
        var bitmap = Assert.IsType<BitmapImage>(image.Source);
        Assert.NotNull(bitmap.RgbaPixels);
        Assert.Equal(ExpectedPixels, bitmap.RgbaPixels!.Value.Pixels);
    }

    [Fact]
    public void Bound_string_path_lands_bitmapimage()
    {
        var image = new Image();
        image.DataContext = new StringCardItem();
        image.SetBinding(nameof(Image.Source), new Binding(nameof(StringCardItem.ImagePath)));
        Assert.IsType<BitmapImage>(image.Source);
    }

    [Fact]
    public void Card_template_paints_drawimage_through_transport()
    {
        var control = new ItemsControl
        {
            ItemTemplate = new DataTemplate(
                "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<Image Source=\"{Binding ImageSource}\" Width=\"50\" Height=\"50\"/>" +
                "<TextBlock Text=\"{Binding Title}\"/></StackPanel>"),
        };
        control.AddItem(new CardItem());
        var commands = WasmMilCore.RenderHeadless(control, new WpfSize(400, 200));
        Assert.Contains(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "Button");
        var draw = Assert.Single(commands.OfType<DrawCommand.DrawImage>());
        Assert.Equal(2, draw.Width);
        Assert.Equal(1, draw.Height);
        Assert.True(draw.Rgba.Any(b => b != 0), "Card icon pixels must be nonzero.");
        Assert.Equal(ExpectedPixels, draw.Rgba);

        var json = DrawCommandJson.Serialize(commands);
        using var ops = JsonDocument.Parse(json);
        var op7 = ops.RootElement.EnumerateArray()
            .FirstOrDefault(op => op[0].GetInt32() == 7);
        Assert.True(op7.ValueKind != JsonValueKind.Undefined, "Frame JSON must carry drawImage op 7.");
        Assert.Equal(ExpectedPixels, Convert.FromBase64String(op7[7].GetString()!));
    }

    [Fact]
    public void Staged_gallery_cards_paint_drawimage()
    {
        var items = LoadStagedGalleryCards();
        Assert.NotEmpty(items);
        var control = new ItemsControl
        {
            ItemTemplate = new DataTemplate(
                "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<Image Source=\"{Binding ImageSource}\" Width=\"50\" Height=\"50\"/>" +
                "<TextBlock Text=\"{Binding Title}\"/></StackPanel>"),
        };
        foreach (var item in items) control.AddItem(item);
        var commands = WasmMilCore.RenderHeadless(control, new WpfSize(800, 600));
        var draws = commands.OfType<DrawCommand.DrawImage>().ToList();
        Assert.Equal(items.Count, draws.Count);
        Assert.All(draws, d => Assert.True(d.Rgba.Any(b => b != 0), "Every staged card icon must paint nonzero pixels."));
        var json = DrawCommandJson.Serialize(commands);
        using var ops = JsonDocument.Parse(json);
        Assert.Equal(draws.Count, ops.RootElement.EnumerateArray().Count(op => op[0].GetInt32() == 7));
    }

    private static List<object> LoadStagedGalleryCards()
    {
        // Boot-free staging check: the staged WPFGallery assembly supplies the
        // pack image bytes (loaded, never booted, so no window, dispatcher or
        // static SystemEvents subscriptions leak into sibling tests); the
        // upstream data file supplies the real card ImagePaths.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "DesktopWasm.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var apps = System.IO.Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "wpf-gallery");
        var package = JsonSerializer.Deserialize<ApplicationPackage>(File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var entry = package.Assemblies.First(a => a.Name == "WPFGallery");
        System.Reflection.Assembly.Load(File.ReadAllBytes(System.IO.Path.Combine(apps, entry.File)));
        using var data = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(
            directory.FullName, "targets", "wpf-samples", "Sample Applications",
            "WPFGallery", "Models", "ControlsInfoData.json")));
        // Mirrors ControlsInfoDataSource.GetAllControlsInfo (skips Samples).
        var cards = new List<object>();
        foreach (var group in data.RootElement.EnumerateArray())
        {
            if (group.GetProperty("UniqueId").GetString() == "Samples") continue;
            if (!group.TryGetProperty("Items", out var children)) continue;
            foreach (var item in children.EnumerateArray())
            {
                var title = item.TryGetProperty("Title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                var path = item.TryGetProperty("ImagePath", out var p) ? p.GetString() : null;
                if (string.IsNullOrEmpty(path)) continue;
                cards.Add(new CardItem { Title = title, ImageSource = new Uri("pack://application:,,,/" + path) });
            }
        }
        return cards;
    }
}
