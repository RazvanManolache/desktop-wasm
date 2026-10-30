using DesktopWasm.Graphics;
using SkiaSharp;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ScreenToGif FlowDocument slice: History Content documents, DownloadDialog
/// release-note paragraphs and the InlineExtensions link builders need real
/// block/inline hosting with layout and paint. XAML shapes mirror the
/// DownloadDialog viewer.
/// </summary>
[ResetAmbientState]
public sealed class FlowDocumentTests
{
    [Fact]
    public void Text_element_carries_wpf_defaults_and_add_owner()
    {
        Assert.Equal(12.0, (double)TextElement.FontSizeProperty.DefaultMetadata.DefaultValue!);
        Assert.Equal(new FontFamily("Segoe UI"), (FontFamily)TextElement.FontFamilyProperty.DefaultMetadata.DefaultValue!);
        Assert.Equal(FontStretches.Normal, (FontStretch)TextElement.FontStretchProperty.DefaultMetadata.DefaultValue!);
        Assert.Equal(FontStyles.Normal, (FontStyle)TextElement.FontStyleProperty.DefaultMetadata.DefaultValue!);
        Assert.Equal(FontWeights.Normal, (FontWeight)TextElement.FontWeightProperty.DefaultMetadata.DefaultValue!);

        var owned = TextElement.FontFamilyProperty.AddOwner(typeof(Paragraph));
        Assert.Same(TextElement.FontFamilyProperty, owned);
        Assert.Same(TextElement.FontFamilyProperty, DependencyProperty.FromName(typeof(Paragraph), "FontFamily"));

        var run = new Run("hi");
        Assert.Equal("hi", run.Text);
        Assert.Equal("Segoe UI", TextElement.GetFontFamily(run).Source);
        TextElement.SetFontSize(run, 20);
        Assert.Equal(20, TextElement.GetFontSize(run));
        Assert.Equal(20, run.FontSize);
    }

    [Fact]
    public void Font_stretch_and_flow_direction_values_match_wpf()
    {
        Assert.Equal(FlowDirection.LeftToRight, (FlowDirection)0);
        Assert.Equal(FlowDirection.RightToLeft, (FlowDirection)1);
        Assert.Equal("Normal", FontStretches.Normal.ToString());
        Assert.Equal("Normal", FontStretches.Medium.ToString());
        Assert.Equal("Condensed", FontStretches.Condensed.ToString());
        Assert.Equal("UltraExpanded", FontStretches.UltraExpanded.ToString());
        Assert.True(FontStretches.Normal == FontStretches.Medium);
        Assert.False(FontStretches.Normal == FontStretches.Expanded);
        Assert.Equal(5, FontStretches.Normal.ToOpenTypeStretch());
    }

    [Fact]
    public void Paragraph_hosts_inlines_with_siblings_and_text()
    {
        var paragraph = new Paragraph(new Run("hello"));
        paragraph.Inlines.Add(" world");
        paragraph.Inlines.Add(new LineBreak());
        var link = new Hyperlink(new Run("details"));
        paragraph.Inlines.Add(link);

        Assert.Equal(4, paragraph.Inlines.Count);
        Assert.Same(paragraph.Inlines[0], paragraph.Inlines.FirstInline);
        Assert.Same(link, paragraph.Inlines.LastInline);
        Assert.Same(paragraph, paragraph.Inlines[0].Parent);
        Assert.Same(paragraph.Inlines[1], paragraph.Inlines[0].NextInline);
        Assert.Same(paragraph.Inlines[0], paragraph.Inlines[1].PreviousInline);
        Assert.Null(paragraph.Inlines[0].PreviousInline);
        Assert.Null(link.NextInline);
        Assert.Equal("hello world\ndetails", paragraph.Text);

        Assert.True(paragraph.Inlines.Remove(link));
        Assert.Equal("hello world\n", paragraph.Text);
        Assert.Null(link.Parent);
        paragraph.Inlines.Clear();
        Assert.Empty(paragraph.Inlines);
    }

    [Fact]
    public void Collection_insert_after_before_and_range_keep_parenting()
    {
        var paragraph = new Paragraph();
        var first = new Run("a");
        var last = new Run("c");
        paragraph.Inlines.Add(first);
        paragraph.Inlines.Add(last);
        var middle = new Run("b");
        paragraph.Inlines.InsertAfter(first, middle);
        Assert.Equal("abc", paragraph.Text);
        var zero = new Run("0");
        paragraph.Inlines.InsertBefore(first, zero);
        Assert.Equal("0abc", paragraph.Text);
        Assert.Same(paragraph, middle.Parent);

        var more = new Paragraph();
        more.Inlines.AddRange(new Inline[] { new Run("x"), new Run("y") });
        Assert.Equal("xy", more.Text);

        var document = new FlowDocument();
        var one = new Paragraph(new Run("1"));
        var two = new Paragraph(new Run("2"));
        document.Blocks.Add(one);
        document.Blocks.Add(two);
        Assert.Same(one, document.Blocks.FirstBlock);
        Assert.Same(two, document.Blocks.LastBlock);
        Assert.Same(two, one.NextBlock);
        Assert.Same(one, two.PreviousBlock);
        Assert.True(document.Blocks.Remove(one));
        Assert.Null(one.Parent);
        Assert.Equal("2\n", document.Text);
    }

    [Fact]
    public void History_shaped_document_lays_out_and_paints()
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(3),
            FontFamily = new FontFamily("Segoe UI"),
        };
        document.Blocks.Add(new Paragraph(new Run("Upload failed.")));
        var paragraph = new Paragraph();
        paragraph.KeepTogether = true;
        paragraph.Inlines.Add(new Run("Detail "));
        paragraph.Inlines.Add(new Hyperlink(new Run("open")) { NavigateUri = new Uri("https://example.com") });
        document.Blocks.Add(paragraph);

        Assert.Equal(2, document.Blocks.Count);
        Assert.Equal(new Thickness(3), document.PagePadding);

        document.Measure(new WpfSize(400, 1000));
        Assert.True(document.DesiredSize.Width > 0);
        Assert.True(document.DesiredSize.Height > 0);
        document.Arrange(new WpfRect(0, 0, 400, document.DesiredSize.Height));

        var first = (Paragraph)document.Blocks[0];
        Assert.True(first.DesiredSize.Height > 0);
        var second = (Paragraph)document.Blocks[1];
        Assert.Equal(second.ArrangedRect.Y, first.ArrangedRect.Y + first.ArrangedRect.Height, precision: 3);

        var window = new Window { Content = document };
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(400, 600));
        var texts = commands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();
        Assert.Contains("Upload failed.", texts);
        Assert.Contains("Detail ", texts);
        Assert.Contains("open", texts);
    }

    [Fact]
    public void Fonts_inherit_from_document_through_paragraph_to_run()
    {
        var document = new FlowDocument { FontSize = 20 };
        var paragraph = new Paragraph(new Run("plain"));
        document.Blocks.Add(paragraph);
        var run = (Run)paragraph.Inlines[0];
        Assert.Equal(20, run.FontSize);

        paragraph.FontSize = 30;
        Assert.Equal(30, run.FontSize);
        run.FontSize = 10;
        Assert.Equal(10, run.FontSize);

        document.Foreground = new SolidColorBrush(Colors.Red);
        Assert.Equal(new SolidColorBrush(Colors.Red).Color, ((SolidColorBrush)run.Foreground!).Color);
    }

    [Fact]
    public void Hyperlink_child_wrapping_folds_clicks_and_hit_tests()
    {
        var paragraph = new Paragraph();
        paragraph.Measure(new WpfSize(400, 1000));
        paragraph.Arrange(new WpfRect(10, 20, 400, 100));
        var link = new Hyperlink();
        link.Inlines.Add(new Run("click "));
        link.Inlines.Add(new Run("here"));
        paragraph.Inlines.Add(link);

        Assert.Equal("click here", link.Text);
        paragraph.Measure(new WpfSize(400, 1000));
        paragraph.Arrange(new WpfRect(10, 20, 400, 100));
        var rect = paragraph.GetLinkRect(link);
        Assert.NotNull(rect);
        Assert.True(link.HitTest(new WpfPoint(rect!.Value.X + 1, rect.Value.Y + 1)));
        Assert.False(link.HitTest(new WpfPoint(rect.Value.X - 50, rect.Value.Y)));

        RequestNavigateEventArgs? navigated = null;
        link.NavigateUri = new Uri("https://example.com/page");
        link.RequestNavigate += (_, e) => navigated = e;
        link.DoClick();
        Assert.NotNull(navigated);
        Assert.Equal("https://example.com/page", navigated!.Uri.AbsoluteUri);
    }

    [Fact]
    public void Viewer_hosts_zoom_and_scroll_visibility()
    {
        var viewer = new FlowDocumentScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var document = new FlowDocument();
        document.Blocks.Add(new Paragraph(new Run("notes")));
        viewer.Document = document;
        Assert.Same(viewer, document.Parent);
        Assert.Same(document, viewer.Document);

        Assert.Equal(100.0, viewer.Zoom);
        Assert.Equal(80.0, viewer.MinZoom);
        Assert.Equal(200.0, viewer.MaxZoom);
        Assert.Equal(10.0, viewer.ZoomIncrement);
        Assert.Equal(ScrollBarVisibility.Auto, viewer.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Visible, new FlowDocumentScrollViewer().VerticalScrollBarVisibility);
        Assert.False(viewer.IsToolBarVisible);
        Assert.True(viewer.IsSelectionEnabled);
        Assert.True(viewer.CanDecreaseZoom);
        viewer.DecreaseZoom();
        Assert.Equal(90.0, viewer.Zoom, precision: 9);
        viewer.IncreaseZoom();
        Assert.Equal(100.0, viewer.Zoom, precision: 9);
        viewer.Zoom = 10000;
        Assert.Equal(viewer.MaxZoom, viewer.Zoom);

        viewer.Measure(new WpfSize(400, 1000));
        Assert.True(viewer.DesiredSize.Height > 0);
        viewer.Arrange(new WpfRect(0, 0, 400, viewer.DesiredSize.Height));
        var window = new Window { Content = viewer };
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(400, 600));
        Assert.Contains(commands.OfType<DrawCommand.DrawText>().Select(t => t.Text), t => t == "notes");
    }

    [Fact]
    public void Download_dialog_shaped_xaml_loads_lays_out_and_paints()
    {
        const string xaml = """
            <Window Title="Update">
              <FlowDocumentScrollViewer VerticalScrollBarVisibility="Auto">
                <FlowDocument TextAlignment="Left">
                  <Paragraph TextAlignment="Center" FontWeight="Light">
                    <Run Text="New release" FontSize="30"/>
                    <Run Text="Version 2.22" FontSize="20"/>
                  </Paragraph>
                  <Paragraph FontSize="12" Margin="5">
                    <Run Text="Fixes"/>
                  </Paragraph>
                </FlowDocument>
              </FlowDocumentScrollViewer>
            </Window>
            """;
        var window = (Window)XamlLoader.Load(xaml).Root;
        var viewer = Assert.IsType<FlowDocumentScrollViewer>(window.Content);
        Assert.Equal(ScrollBarVisibility.Auto, viewer.VerticalScrollBarVisibility);
        var document = Assert.IsType<FlowDocument>(viewer.Document);
        Assert.Equal(TextAlignment.Left, document.TextAlignment);
        Assert.Equal(2, document.Blocks.Count);

        var title = Assert.IsType<Paragraph>(document.Blocks[0]);
        Assert.Equal(TextAlignment.Center, title.TextAlignment);
        Assert.Equal(FontWeights.Light, title.FontWeight);
        Assert.Equal(2, title.Inlines.Count);
        var headline = Assert.IsType<Run>(title.Inlines[0]);
        Assert.Equal("New release", headline.Text);
        Assert.Equal(30, headline.FontSize);

        var body = Assert.IsType<Paragraph>(document.Blocks[1]);
        Assert.Equal(12, body.FontSize);
        Assert.Equal(new Thickness(5), body.Margin);

        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(400, 600));
        var texts = commands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();
        Assert.Contains("New release", texts);
        Assert.Contains("Version 2.22", texts);
        Assert.Contains("Fixes", texts);
    }

    [Fact]
    public void Paragraph_text_wraps_and_center_alignment_offsets_runs()
    {
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run("aa bb cc dd ee ff gg hh ii jj kk ll mm nn oo pp"));
        paragraph.Measure(new WpfSize(60, 1000));
        var lines = paragraph.ComputeLines(60);
        Assert.True(lines.Count > 1);
        paragraph.TextAlignment = TextAlignment.Center;
        paragraph.Inlines.Add(new Run(" qq rr ss tt uu vv ww xx yy zz 11 22 33 44 55 66 77 88 99"));
        var window = new Window { Content = paragraph };
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(120, 600));
        Assert.True(commands.OfType<DrawCommand.DrawText>().Count() > 1);
    }

    [Fact]
    public void Document_pixels_show_headline_and_body_ink()
    {
        var document = new FlowDocument();
        var title = new Paragraph(new Run("Headline")) { TextAlignment = TextAlignment.Left };
        document.Blocks.Add(title);
        document.Blocks.Add(new Paragraph(new Run("body copy here")));
        var window = new Window { Content = document };

        using var surface = new SkiaSurface(400, 300);
        var mil = new WasmMilCore(surface);
        var commands = mil.Render(window, new WpfSize(400, 300), new WpfRect(0, 0, 400, 300));
        mil.CommitChannel(commands);

        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        var ink = 0;
        for (var y = 0; y < 120; y += 2)
            for (var x = 0; x < 400; x += 2)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red < 128 || pixel.Green < 128 || pixel.Blue < 128) ink++;
            }
        Assert.True(ink > 20, $"expected headline/body ink pixels, saw {ink}");
    }

    [Fact]
    public void Framework_content_element_carries_tag_cursor_and_force()
    {
        var paragraph = new Paragraph { Tag = "marker", ForceCursor = true, Cursor = Cursors.Hand };
        Assert.Equal("marker", paragraph.Tag);
        Assert.True(paragraph.ForceCursor);
        Assert.Same(Cursors.Hand, paragraph.Cursor);
        Assert.Equal(string.Empty, paragraph.Name);
    }
}
