using System.Xml.Linq;
using WfWasm.Renderer;

public sealed class CanvasClipTests
{
    [Fact]
    public void NestedScopes_RestoreOuterClip_AndDisposeOnlyOnce()
    {
        var renderer = new RecordingRenderer();
        using (renderer.PushClip(10, 20, 30, 40))
        {
            var inner = renderer.PushClip(15, 25, 5, 5);
            renderer.FillRectangle(0, 0, 100, 100, 0xff0000ff);
            inner.Dispose();
            inner.Dispose();
            renderer.FillRectangle(12, 22, 2, 2, 0x00ff00ff);
        }
        renderer.FillRectangle(80, 80, 2, 2, 0x0000ffff);
        Assert.Equal(new DrawCommand[] {
            new PushClipCommand(10, 20, 30, 40), new PushClipCommand(15, 25, 5, 5),
            new FillRectCommand(0, 0, 100, 100, 0xff0000ff), new PopClipCommand(),
            new FillRectCommand(12, 22, 2, 2, 0x00ff00ff), new PopClipCommand(),
            new FillRectCommand(80, 80, 2, 2, 0x0000ffff)
        }, renderer.Commands);
    }

    [Fact]
    public void Scope_RestoresClip_WhenPainterThrows()
    {
        var renderer = new RecordingRenderer();
        Assert.Throws<InvalidOperationException>((Action)(() => {
            using var clip = renderer.PushClip(1, 2, 3, 4);
            throw new InvalidOperationException("paint failure");
        }));
        Assert.IsType<PopClipCommand>(renderer.Commands.Last());
    }

    [Fact]
    public void SvgClip_ContainsDrawing_ButNotFollowingSibling()
    {
        var renderer = new SvgRenderer();
        renderer.BeginFrame(100, 100);
        using (renderer.PushClip(10, 20, 30, 40))
            renderer.FillRectangle(0, 0, 100, 100, 0xff0000ff);
        renderer.FillRectangle(90, 90, 5, 5, 0x00ff00ff);
        renderer.EndFrame();
        var svg = XDocument.Parse(renderer.GetSvg()).Root!;
        XNamespace ns = "http://www.w3.org/2000/svg";
        var group = Assert.Single(svg.Elements(ns + "g"));
        Assert.Equal("url(#wcclip0)", group.Attribute("clip-path")!.Value);
        Assert.Equal("#ff0000ff", Assert.Single(group.Elements(ns + "rect")).Attribute("fill")!.Value);
        Assert.Equal("#00ff00ff", Assert.Single(svg.Elements(ns + "rect")).Attribute("fill")!.Value);
    }
}
