extern alias WfDraw;

using WfDashStyle = WfDraw::System.Drawing.Drawing2D.DashStyle;
using WfLineCap = WfDraw::System.Drawing.Drawing2D.LineCap;
using WfLineJoin = WfDraw::System.Drawing.Drawing2D.LineJoin;
using WfMatrix = WfDraw::System.Drawing.Drawing2D.Matrix;
using WfPenAlignment = WfDraw::System.Drawing.Drawing2D.PenAlignment;
using WfPen = WfDraw::System.Drawing.Pen;
using WfPointF = WfDraw::System.Drawing.PointF;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WPath = WfDraw::System.Drawing.Drawing2D.GraphicsPath;

// SF04 (SF1120-CRASHQA-SF01-SF04): every diagram-item click died in
// NotSupportedException("Path widening arrives with the widen
// milestone.") from GraphicsPath.Widen, wrapped in
// TargetInvocationException by the event dispatch, so nodes never
// selected. Widen must produce the stroked outline (solid, center
// pens) so IsVisible hit-testing works; exotic pens stay on a
// truthful milestone instead of silently doing nothing.
public sealed class GraphicsPathWidenTests
{
    [Fact]
    public void Null_Pen_Throws_ArgumentNull()
    {
        using var path = new WPath();
        Assert.Throws<ArgumentNullException>(() => path.Widen(null!));
    }

    [Fact]
    public void Empty_Path_Stays_Empty()
    {
        using var path = new WPath();
        using var pen = new WfPen(WfDraw::System.Drawing.Color.Black, 2f);
        path.Widen(pen);
        Assert.Equal(0, path.PointCount);
    }

    [Fact]
    public void Rectangle_Widens_To_Border_Outline()
    {
        using var path = new WPath();
        path.AddRectangle(new WfRectangleF(0f, 0f, 10f, 10f));
        using var pen = new WfPen(WfDraw::System.Drawing.Color.Black, 2f);
        path.Widen(pen);
        Assert.True(path.PointCount > 0);
        Assert.True(path.IsVisible(new WfPointF(5f, 0.5f)));
        Assert.True(path.IsVisible(new WfPointF(5f, -0.5f)));
        Assert.False(path.IsVisible(new WfPointF(5f, 5f)));
        Assert.False(path.IsVisible(new WfPointF(5f, 3f)));
    }

    [Fact]
    public void Open_Line_Round_Caps_Cover_Endpoints()
    {
        using var path = new WPath();
        path.AddLine(0f, 0f, 10f, 0f);
        using var pen = new WfPen(WfDraw::System.Drawing.Color.Black, 2f);
        pen.StartCap = WfLineCap.Round;
        pen.EndCap = WfLineCap.Round;
        path.Widen(pen);
        Assert.True(path.IsVisible(new WfPointF(-0.5f, 0f)));
        Assert.True(path.IsVisible(new WfPointF(10.5f, 0f)));
        Assert.False(path.IsVisible(new WfPointF(5f, 2f)));
    }

    [Fact]
    public void Open_Line_Flat_Caps_Stop_At_Endpoints()
    {
        using var path = new WPath();
        path.AddLine(0f, 0f, 10f, 0f);
        using var pen = new WfPen(WfDraw::System.Drawing.Color.Black, 2f);
        path.Widen(pen);
        Assert.True(path.IsVisible(new WfPointF(5f, 0.5f)));
        Assert.False(path.IsVisible(new WfPointF(-0.5f, 0f)));
        Assert.False(path.IsVisible(new WfPointF(10.5f, 0f)));
    }

    [Fact]
    public void MiterLimit_Falls_Back_To_Bevel()
    {
        using var path = new WPath();
        path.AddLine(0f, 10f, 5f, 0f);
        path.AddLine(5f, 0f, 10f, 10f);
        using var pen = new WfPen(WfDraw::System.Drawing.Color.Black, 2f);
        pen.LineJoin = WfLineJoin.Miter;
        pen.MiterLimit = 1f;
        path.Widen(pen);
        WfRectangleF bounds = path.GetBounds();
        Assert.True(bounds.Y >= -1.5f);
        Assert.True(path.IsVisible(new WfPointF(5f, 1f)));
    }

    [Fact]
    public void Dashed_Pen_Throws_Truthful_Milestone()
    {
        using var path = new WPath();
        path.AddLine(0f, 0f, 10f, 0f);
        using var pen = new WfPen(WfDraw::System.Drawing.Color.Black, 2f);
        pen.DashStyle = WfDashStyle.Dash;
        var ex = Assert.Throws<NotSupportedException>(() => path.Widen(pen));
        Assert.Contains("dash", ex.Message);
    }

    [Fact]
    public void NonCenter_Alignment_Throws_Truthful()
    {
        using var path = new WPath();
        path.AddLine(0f, 0f, 10f, 0f);
        using var pen = new WfPen(WfDraw::System.Drawing.Color.Black, 2f);
        pen.Alignment = WfPenAlignment.Inset;
        var ex = Assert.Throws<NotSupportedException>(() => path.Widen(pen));
        Assert.Contains("alignment", ex.Message);
    }

    [Fact]
    public void Matrix_Overload_Scales_Before_Widen()
    {
        using var path = new WPath();
        path.AddRectangle(new WfRectangleF(0f, 0f, 10f, 10f));
        using var pen = new WfPen(WfDraw::System.Drawing.Color.Black, 2f);
        using var matrix = new WfMatrix();
        matrix.Scale(2f, 2f);
        path.Widen(pen, matrix);
        WfRectangleF bounds = path.GetBounds();
        Assert.True(bounds.Width >= 19f);
        Assert.True(path.IsVisible(new WfPointF(10f, 0.5f)));
        Assert.False(path.IsVisible(new WfPointF(10f, 10f)));
    }

    [Fact]
    public void Zero_Width_Widens_To_Empty()
    {
        using var path = new WPath();
        path.AddRectangle(new WfRectangleF(0f, 0f, 10f, 10f));
        using var pen = new WfPen(WfDraw::System.Drawing.Color.Black, 0f);
        path.Widen(pen);
        Assert.Equal(0, path.PointCount);
    }
}
