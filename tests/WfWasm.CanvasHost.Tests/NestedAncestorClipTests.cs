using System.Text.Json;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using Forms = System.Windows.Forms;

// DIRECT-1105-runtime fallback: nested-child + partially visible
// self-painted-bitmap regression pins for RenderControlsClipped.
// Root pins single-level partial + retained-layer upload split; these pin
// scope NESTING (partial container holding a partial child) and the image
// path (button Image blit inside the scope). Existing assertions untouched.
public sealed class NestedAncestorClipTests
{
    [Fact]
    public void Nested_Partial_Container_And_Child_Nest_Scopes_In_Order()
    {
        using var host = new WinFormsAppHost();
        using var form = new Forms.Form { Width = 209, Height = 217, Visible = true,
            FormBorderStyle = Forms.FormBorderStyle.None };
        using var outer = new Forms.Button { Left = 150, Top = 40, Width = 100,
            Height = 20, Text = "OUTER" };
        using var inner = new Forms.Button { Left = 30, Top = 0, Width = 100,
            Height = 20, Text = "NEST" };
        outer.Controls.Add(inner);
        form.Controls.Add(outer);
        form.Controls.Add(new Forms.Button { Left = 10, Top = 70, Width = 100,
            Height = 20, Text = "SIBLING" });
        var renderer = new RecordingRenderer();
        host.RenderControlsClipped(renderer, form.Controls, 50, 60,
            new System.Drawing.Rectangle(50, 60, 209, 217));

        using var doc = JsonDocument.Parse(CanvasBridge.SerializeCommands(renderer.Commands));
        var ops = doc.RootElement.EnumerateArray().ToArray();
        int firstPush = Array.FindIndex(ops, op => op[0].GetInt32() == 13);
        int secondPush = Array.FindIndex(ops, firstPush + 1, op => op[0].GetInt32() == 13);
        int nest = Array.FindIndex(ops, op => op[0].GetInt32() == 3 && op[1].GetString() == "NEST");
        int firstPop = Array.FindIndex(ops, op => op[0].GetInt32() == 14);
        int secondPop = Array.FindIndex(ops, firstPop + 1, op => op[0].GetInt32() == 14);
        int sibling = Array.FindIndex(ops, op => op[0].GetInt32() == 3 && op[1].GetString() == "SIBLING");
        Assert.True(firstPush >= 0 && secondPush > firstPush && nest > secondPush
            && firstPop > nest && secondPop > firstPop && sibling > secondPop,
            "Nested partial scopes must nest (push,push,NEST,pop,pop) with the sibling after restoration.");
        Assert.Equal(2, ops.Count(op => op[0].GetInt32() == 13));
        Assert.Equal(2, ops.Count(op => op[0].GetInt32() == 14));
    }

    [Fact]
    public void Partial_Button_Image_Blit_Stays_Inside_Clip_Scope()
    {
        using var host = new WinFormsAppHost();
        using var form = new Forms.Form { Width = 209, Height = 217, Visible = true,
            FormBorderStyle = Forms.FormBorderStyle.None };
        using var bmp = new System.Drawing.Bitmap(2, 2);
        bmp.SetPixel(0, 0, System.Drawing.Color.Red);
        bmp.SetPixel(1, 1, System.Drawing.Color.Blue);
        using var edge = new Forms.Button { Left = 200, Top = 40, Width = 100,
            Height = 20, Text = "EDGEIMG", Image = bmp };
        form.Controls.Add(edge);
        form.Controls.Add(new Forms.Button { Left = 10, Top = 70, Width = 100,
            Height = 20, Text = "SIBLING" });
        var renderer = new RecordingRenderer();
        host.RenderControlsClipped(renderer, form.Controls, 50, 60,
            new System.Drawing.Rectangle(50, 60, 209, 217));

        using var doc = JsonDocument.Parse(CanvasBridge.SerializeCommands(renderer.Commands));
        var ops = doc.RootElement.EnumerateArray().ToArray();
        int push = Array.FindIndex(ops, op => op[0].GetInt32() == 13);
        int pop = Array.FindIndex(ops, op => op[0].GetInt32() == 14);
        int text = Array.FindIndex(ops, op => op[0].GetInt32() == 3 && op[1].GetString() == "EDGEIMG");
        int image = Array.FindIndex(ops, op => op[0].GetInt32() is 7 or 9);
        int sibling = Array.FindIndex(ops, op => op[0].GetInt32() == 3 && op[1].GetString() == "SIBLING");
        Assert.True(push >= 0 && pop > push, "Partial button must open exactly one clip scope.");
        Assert.True(text > push && text < pop, "Edge text must be clipped.");
        Assert.True(image > push && image < pop, "Edge image blit must be clipped.");
        Assert.True(sibling > pop, "Sibling must paint after restoration.");
    }
}
