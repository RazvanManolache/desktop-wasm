// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>TextBlock nested-inline styling (FACTORY WPF job 71).
/// Direct-text chunks fold in encounter order against hosted inlines, and
/// each segment paints with its effective style (weight/style/ink resolve
/// per inline, falling back to the block). Plain blocks keep the exact
/// legacy single-DrawText output.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class TextBlockInlinePaintTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Leading_literal_folds_before_inline()
    {
        Application.Current = new Application();
        var block = Assert.IsType<TextBlock>(XamlLoader.Load(
            "<TextBlock xmlns=\"" + PresentationNs + "\">Hello <Bold>World</Bold></TextBlock>").Root);
        Assert.Equal("Hello World", block.Text);
    }

    [Fact]
    public void Trailing_literal_folds_after_inline()
    {
        Application.Current = new Application();
        var block = Assert.IsType<TextBlock>(XamlLoader.Load(
            "<TextBlock xmlns=\"" + PresentationNs + "\"><Bold>W</Bold> tail</TextBlock>").Root);
        Assert.Equal("W tail", block.Text);
    }

    [Fact]
    public void Nested_bold_paints_bold_segment()
    {
        Application.Current = new Application();
        var block = Assert.IsType<TextBlock>(XamlLoader.Load(
            "<TextBlock xmlns=\"" + PresentationNs + "\">Hello <Bold>World</Bold></TextBlock>").Root);
        var frame = WasmMilCore.RenderHeadless(block, new WpfSize(400, 100));
        var texts = frame.OfType<DrawCommand.DrawText>().ToList();
        Assert.Contains("Hello", string.Concat(texts.Select(d => d.Text)));
        Assert.Contains(texts, d => d.Text.Contains("World", StringComparison.Ordinal) &&
            d.FontCss.Contains("bold", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Nested_italic_paints_italic_segment()
    {
        Application.Current = new Application();
        var block = Assert.IsType<TextBlock>(XamlLoader.Load(
            "<TextBlock xmlns=\"" + PresentationNs + "\"><Italic>Slant</Italic></TextBlock>").Root);
        var frame = WasmMilCore.RenderHeadless(block, new WpfSize(400, 100));
        var texts = frame.OfType<DrawCommand.DrawText>().ToList();
        Assert.Contains(texts, d => d.Text.Contains("Slant", StringComparison.Ordinal) &&
            d.FontCss.Contains("italic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Nested_run_foreground_paints_own_ink()
    {
        Application.Current = new Application();
        var block = Assert.IsType<TextBlock>(XamlLoader.Load(
            "<TextBlock xmlns=\"" + PresentationNs + "\"><Run Foreground=\"Red\" Text=\"R\" /></TextBlock>").Root);
        var frame = WasmMilCore.RenderHeadless(block, new WpfSize(400, 100));
        var texts = frame.OfType<DrawCommand.DrawText>().ToList();
        // WPF paints the run in Red; engine ink is 0xRRGGBBAA.
        Assert.Contains(texts, d => d.Text.Contains("R", StringComparison.Ordinal) && d.Rgba == 0xFF0000FFu);
    }

    [Fact]
    public void Plain_block_keeps_single_drawtext()
    {
        Application.Current = new Application();
        var block = new TextBlock { Text = "Hi" };
        var frame = WasmMilCore.RenderHeadless(block, new WpfSize(400, 100));
        var only = Assert.Single(frame.OfType<DrawCommand.DrawText>());
        Assert.Equal("Hi", only.Text);
        Assert.Equal("14px sans-serif", only.FontCss);
        Assert.Equal(0x000000FFu, only.Rgba);
    }

    [Fact]
    public void Real_gallery_package_nested_span_with_bold_italic_underline()
    {
        // Real package example from WPFGallery TextBlockPage.xaml:
        // <TextBlock FontSize="14">
        //     <Run FontFamily="Times New Roman">Text in a TextBlock doesn't have to be a simple string.</Run>
        //     <LineBreak />
        //     <Span>
        //         Text can be <Bold>bold</Bold>,
        //         <Italic>italic</Italic>,
        //         or <Underline>underlined</Underline>
        //         .</Span>
        // </TextBlock>
        Application.Current = new Application();
        var xaml = @"<TextBlock xmlns=""" + PresentationNs + @""" FontSize=""14"">
    <Run FontFamily=""Times New Roman"">Text in a TextBlock doesn't have to be a simple string.</Run>
    <LineBreak />
    <Span>Text can be <Bold>bold</Bold>, <Italic>italic</Italic>, or <Underline>underlined</Underline>.</Span>
</TextBlock>";
        var block = Assert.IsType<TextBlock>(XamlLoader.Load(xaml).Root);
        Assert.Equal("Text in a TextBlock doesn't have to be a simple string.\nText can be bold, italic, or underlined.", block.Text);

        var frame = WasmMilCore.RenderHeadless(block, new WpfSize(800, 200));
        var texts = frame.OfType<DrawCommand.DrawText>().ToList();

        // Run with Times New Roman
        Assert.Contains(texts, d => d.Text.Contains("Text in a TextBlock doesn't have to be a simple string.", StringComparison.Ordinal)
            && d.FontCss.Contains("Times New Roman", StringComparison.OrdinalIgnoreCase));

        // Bold segment inside nested Span
        Assert.Contains(texts, d => d.Text.Contains("bold", StringComparison.Ordinal)
            && d.FontCss.Contains("bold", StringComparison.OrdinalIgnoreCase));

        // Italic segment inside nested Span
        Assert.Contains(texts, d => d.Text.Contains("italic", StringComparison.Ordinal)
            && d.FontCss.Contains("italic", StringComparison.OrdinalIgnoreCase));

        // Underlined segment inside nested Span
        Assert.Contains(texts, d => d.Text.Contains("underlined", StringComparison.Ordinal));
    }

    [Fact]
    public void Deeply_nested_span_inherits_font_style_and_weight()
    {
        Application.Current = new Application();
        var xaml = @"<TextBlock xmlns=""" + PresentationNs + @""" FontSize=""14"">
    <Bold><Italic>Combined</Italic></Bold>
</TextBlock>";
        var block = Assert.IsType<TextBlock>(XamlLoader.Load(xaml).Root);
        Assert.Equal("Combined", block.Text);

        var frame = WasmMilCore.RenderHeadless(block, new WpfSize(400, 100));
        var texts = frame.OfType<DrawCommand.DrawText>().ToList();
        var combined = Assert.Single(texts, d => d.Text == "Combined");
        Assert.Contains("bold", combined.FontCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("italic", combined.FontCss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nested_span_foreground_inherits_and_does_not_bleed_to_siblings()
    {
        Application.Current = new Application();
        var xaml = @"<TextBlock xmlns=""" + PresentationNs + @""" FontSize=""14"">
    <Span Foreground=""Red""><Bold>Alert</Bold> inner</Span> outer
</TextBlock>";
        var block = Assert.IsType<TextBlock>(XamlLoader.Load(xaml).Root);
        Assert.Equal("Alert inner outer", block.Text);

        var frame = WasmMilCore.RenderHeadless(block, new WpfSize(400, 100));
        var texts = frame.OfType<DrawCommand.DrawText>().ToList();

        var alert = Assert.Single(texts, d => d.Text.Contains("Alert", StringComparison.Ordinal));
        Assert.Equal(0xFF0000FFu, alert.Rgba);
        Assert.Contains("bold", alert.FontCss, StringComparison.OrdinalIgnoreCase);

        var inner = Assert.Single(texts, d => d.Text.Contains("inner", StringComparison.Ordinal));
        Assert.Equal(0xFF0000FFu, inner.Rgba);
        Assert.DoesNotContain("bold", inner.FontCss, StringComparison.OrdinalIgnoreCase);

        var outer = Assert.Single(texts, d => d.Text.Contains("outer", StringComparison.Ordinal));
        Assert.Equal(0x000000FFu, outer.Rgba);
        Assert.DoesNotContain("bold", outer.FontCss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Negative_identity_invalid_child_in_span_rejected()
    {
        Application.Current = new Application();
        var xaml = @"<TextBlock xmlns=""" + PresentationNs + @""">
    <Span><Button Content=""Invalid"" /></Span>
</TextBlock>";
        Assert.ThrowsAny<Exception>(() => XamlLoader.Load(xaml));
    }

    [Fact]
    public void Negative_identity_plain_segment_never_inherits_sibling_bold_or_italic()
    {
        Application.Current = new Application();
        var xaml = @"<TextBlock xmlns=""" + PresentationNs + @""" FontSize=""14"">
    <Span><Bold>B</Bold> plain <Italic>I</Italic> end</Span>
</TextBlock>";
        var block = Assert.IsType<TextBlock>(XamlLoader.Load(xaml).Root);
        Assert.Equal("B plain I end", block.Text);

        var frame = WasmMilCore.RenderHeadless(block, new WpfSize(400, 100));
        var texts = frame.OfType<DrawCommand.DrawText>().ToList();

        var plain = Assert.Single(texts, d => d.Text.Contains("plain", StringComparison.Ordinal));
        Assert.DoesNotContain("bold", plain.FontCss, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("italic", plain.FontCss, StringComparison.OrdinalIgnoreCase);

        var end = Assert.Single(texts, d => d.Text.Contains("end", StringComparison.Ordinal));
        Assert.DoesNotContain("bold", end.FontCss, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("italic", end.FontCss, StringComparison.OrdinalIgnoreCase);
    }
}
