using DesktopWasm.Wpf;
using Xunit;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Property-element syntax holding a nested element
/// (&lt;Tile.Source&gt;&lt;Image/&gt;...) assigns the value and keeps
/// following siblings at their own level (no end-tag drift).</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class PropertyElementTests
{
    internal sealed class TileLike : UserControl
    {
        public string? Title { get; set; }
        public object? Source { get; set; }
    }

    [Fact]
    public void NestedElementPropertyKeepsSiblingsAtOwnLevel()
    {
        const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:local=\"clr-namespace:X\"";
        var xaml =
            $"<ScrollViewer {ns}>" +
            "<StackPanel>" +
            "<local:TileLike Title=\"A\"><local:TileLike.Source><Image /></local:TileLike.Source></local:TileLike>" +
            "<local:TileLike Title=\"B\"><local:TileLike.Source><Image /></local:TileLike.Source></local:TileLike>" +
            "</StackPanel></ScrollViewer>";
        XamlLoader.CustomElementSource = typeof(TileLike).Assembly;
        try
        {
            var result = XamlLoader.Load(xaml);
            var panel = Assert.IsType<StackPanel>(Assert.IsType<ScrollViewer>(result.Root).Content);
            Assert.Equal(2, panel.LogicalChildren.Count);
            var first = Assert.IsType<TileLike>(panel.LogicalChildren[0]);
            var second = Assert.IsType<TileLike>(panel.LogicalChildren[1]);
            Assert.Equal("A", first.Title);
            Assert.Equal("B", second.Title);
            Assert.IsType<Image>(first.Source);
            Assert.IsType<Image>(second.Source);
        }
        finally
        {
            XamlLoader.CustomElementSource = null;
        }
    }
}
