using DesktopWasm.Graphics;
using Xunit;

namespace DesktopWasm.Core.Tests;

/// <summary>The Canvas2D JSON transport never throws on non-finite geometry:
/// poisoned rects sanitize to extreme-but-finite values (a paint frame must
/// never crash the app).</summary>
public sealed class DrawCommandJsonTests
{
    [Fact]
    public void Serialize_sanitizes_non_finite_geometry()
    {
        var json = DrawCommandJson.Serialize([
            new DrawCommand.FillRect(0, 0, float.PositiveInfinity, float.PositiveInfinity, 0xFF0000FF),
            new DrawCommand.DrawText("Hi", 0, float.NaN, "14px sans-serif", 0x000000FF, 0, 1, -1),
        ]);
        Assert.DoesNotContain("Infinity", json);
        Assert.DoesNotContain("NaN", json);
        Assert.Contains("16383", json);
    }
}
