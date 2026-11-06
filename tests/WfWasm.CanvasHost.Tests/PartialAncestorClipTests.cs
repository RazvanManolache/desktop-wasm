using System.Text.Json;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using Forms = System.Windows.Forms;

public sealed class PartialAncestorClipTests
{
    [Fact]
    public void PartiallyVisibleButton_ClipsAllDrawing_AndRestoresBeforeSibling()
    {
        using var host = new WinFormsAppHost();
        using var form = new Forms.Form { Width = 209, Height = 217, Visible = true,
            FormBorderStyle = Forms.FormBorderStyle.None };
        form.Controls.Add(new Forms.Button { Left = 200, Top = 40, Width = 100,
            Height = 20, Text = "EDGE" });
        form.Controls.Add(new Forms.Button { Left = 10, Top = 70, Width = 100,
            Height = 20, Text = "SIBLING" });
        var renderer = new RecordingRenderer();
        host.RenderControlsClipped(renderer, form.Controls, 50, 60,
            new System.Drawing.Rectangle(50, 60, 209, 217));

        using var doc = JsonDocument.Parse(CanvasBridge.SerializeCommands(renderer.Commands));
        var ops = doc.RootElement.EnumerateArray().ToArray();
        Assert.Equal(13, ops[0][0].GetInt32());
        Assert.Equal(new[] { 50, 60, 209, 217 },
            ops[0].EnumerateArray().Skip(1).Select(v => v.GetInt32()).ToArray());
        int end = Array.FindIndex(ops, op => op[0].GetInt32() == 14);
        int edge = Array.FindIndex(ops, op => op[0].GetInt32() == 3 && op[1].GetString() == "EDGE");
        int sibling = Array.FindIndex(ops, op => op[0].GetInt32() == 3 && op[1].GetString() == "SIBLING");
        Assert.True(edge > 0 && end > edge && sibling > end,
            "All edge-button paint must be clipped; the next sibling must paint after restoration.");
        Assert.Single(ops.Where(op => op[0].GetInt32() == 13));
        Assert.Single(ops.Where(op => op[0].GetInt32() == 14));
    }

    [Fact]
    public void FullyContainedButton_KeepsExistingCommandStream()
    {
        using var host = new WinFormsAppHost();
        using var form = new Forms.Form { Width = 209, Height = 217, Visible = true,
            FormBorderStyle = Forms.FormBorderStyle.None };
        form.Controls.Add(new Forms.Button { Left = 10, Top = 40, Width = 100,
            Height = 20, Text = "INSIDE" });
        var renderer = new RecordingRenderer();
        host.RenderControlsClipped(renderer, form.Controls, 50, 60,
            new System.Drawing.Rectangle(50, 60, 209, 217));
        using var doc = JsonDocument.Parse(CanvasBridge.SerializeCommands(renderer.Commands));
        Assert.DoesNotContain(doc.RootElement.EnumerateArray(),
            op => op[0].GetInt32() is 13 or 14);
        Assert.Contains(renderer.Commands, c => c is DrawTextCommand t && t.Text == "INSIDE");
    }
}
