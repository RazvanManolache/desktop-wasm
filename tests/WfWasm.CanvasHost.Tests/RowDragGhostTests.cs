using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using AppScene = WfWasm.Shim.AppScene;

// RVW-9594 Job2-fallback: SF02 row drags commit on drop but show no drag
// visual mid-drag (frozen gate_job1c mid diffs 0.0/0.0). The headless drag
// loop pumps vendor events with zero visual feedback. The host now paints a
// small ghost chip at the cursor while a RowDragLoopSession is active; these
// tests pin that contract in-process with the RenderControls harness:
// ghost appears after a held move, tracks the cursor, and is gone after
// drop with no orphan paint when no session exists.
public sealed class RowDragGhostTests
{
    private static List<DrawCommand> RenderFrame(Control target, out WinFormsAppHost host, out Form form)
    {
        host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        form = new System.Windows.Forms.Form();
        form.Controls.Add(target);
        form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        form.Left = 0;
        form.Top = 0;
        form.Show();
        var recorder = new RecordingRenderer();
        recorder.BeginFrame(800, 600);
        MethodInfo render = typeof(WinFormsAppHost).GetMethod(
            "RenderControls",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.NotNull(render);
        render.Invoke(host, [recorder, form.Controls, 0, 0]);
        recorder.EndFrame();
        return new List<DrawCommand>(recorder.Commands);
    }

    private static Panel DragPanel() => new()
    {
        Bounds = new Rectangle(10, 20, 300, 200),
    };

    [Fact]
    public void ActiveSession_AfterMove_Paints_Ghost_At_Cursor()
    {
        WinFormsAppHost.NoteHostUp(); // isolate static ghost slots
        // Red without the ghost layer: the move pumps vendor events only
        // and the frame carries no cursor-following marker.
        using var panel = DragPanel();
        var session = new RowDragLoopSession(
            panel, new LoopDataObject(), DragDropEffects.Copy | DragDropEffects.Move)
        {
            DragItemCount = 3,
        };
        try
        {
            session.OnMove(50, 60, MouseButtons.Left, 0, escapePressed: false);
            WinFormsAppHost host;
            Form form;
            var commands = RenderFrame(panel, out host, out form);
            try
            {
                // Ghost chip origin: panel abs (10,20) + cursor (50,60) + offset (12,16).
                const float gx = 10 + 50 + 12;
                const float gy = 20 + 60 + 16;
                DrawTextCommand? label = null;
                foreach (DrawCommand cmd in commands)
                    if (cmd is DrawTextCommand t && t.Text == "3 row(s)")
                        label = t;
                Assert.NotNull(label);
                Assert.Equal(gx + 10, label!.X);
                Assert.Equal(gy + 15, label.Y);
                bool chip = false;
                foreach (DrawCommand cmd in commands)
                    if (cmd is FillRoundedRectCommand r && r.X == gx && r.Y == gy)
                        chip = true;
                Assert.True(chip);
            }
            finally
            {
                host.Dispose();
                System.Windows.Forms.WasmDialogHost.Untrack(form);
            }
        }
        finally
        {
            session.Abort();
        }
    }

    [Fact]
    public void Ghost_Clears_On_Drop()
    {
        WinFormsAppHost.NoteHostUp(); // isolate static ghost slots
        using var panel = DragPanel();
        var session = new RowDragLoopSession(
            panel, new LoopDataObject(), DragDropEffects.Copy | DragDropEffects.Move)
        {
            DragItemCount = 2,
        };
        try
        {
            session.OnMove(40, 30, MouseButtons.Left, 0, escapePressed: false);
            session.OnButtonUp(120, 90, 0);
            WinFormsAppHost host;
            Form form;
            var commands = RenderFrame(panel, out host, out form);
            try
            {
                foreach (DrawCommand cmd in commands)
                {
                    if (cmd is DrawTextCommand t)
                        Assert.DoesNotContain("row(s)", t.Text);
                    if (cmd is FillRoundedRectCommand r)
                        Assert.NotEqual(RowDragLoopSession.GhostFillRgba, r.Rgba);
                }
            }
            finally
            {
                host.Dispose();
                System.Windows.Forms.WasmDialogHost.Untrack(form);
            }
        }
        finally
        {
            session.Abort();
        }
    }

    [Fact]
    public void HostGhost_Paints_Dragging_Chip_At_Cursor()
    {
        // Host-funnel path (vendor DragStart never fires served): the
        // chip shows with a "dragging" label while the held gesture is
        // past threshold. TestSetHostGhost stands in for the armed-gesture
        // registration (arming needs vendor props); the paint contract is
        // what this pins.
        using var panel = DragPanel();
        WinFormsAppHost.TestSetHostGhost(panel, 50, 60);
        try
        {
            WinFormsAppHost host;
            Form form;
            var commands = RenderFrame(panel, out host, out form);
            try
            {
                const float gx = 10 + 50 + 12;
                const float gy = 20 + 60 + 16;
                DrawTextCommand? label = null;
                foreach (DrawCommand cmd in commands)
                    if (cmd is DrawTextCommand t && t.Text == "dragging")
                        label = t;
                Assert.NotNull(label);
                Assert.Equal(gx + 10, label!.X);
                Assert.Equal(gy + 15, label.Y);
                bool chip = false;
                foreach (DrawCommand cmd in commands)
                    if (cmd is FillRoundedRectCommand r && r.X == gx && r.Y == gy
                        && r.Rgba == RowDragLoopSession.GhostFillRgba)
                        chip = true;
                Assert.True(chip);
            }
            finally
            {
                host.Dispose();
                System.Windows.Forms.WasmDialogHost.Untrack(form);
            }
        }
        finally
        {
            WinFormsAppHost.NoteHostUp();
        }
    }

    [Fact]
    public void HostGhost_Clears_On_Up()
    {
        using var panel = DragPanel();
        WinFormsAppHost.TestSetHostGhost(panel, 50, 60);
        WinFormsAppHost.NoteHostUp();
        WinFormsAppHost host;
        Form form;
        var commands = RenderFrame(panel, out host, out form);
        try
        {
            foreach (DrawCommand cmd in commands)
            {
                if (cmd is DrawTextCommand t)
                {
                    Assert.DoesNotContain("row(s)", t.Text);
                    Assert.NotEqual("dragging", t.Text);
                }
                if (cmd is FillRoundedRectCommand r)
                    Assert.NotEqual(RowDragLoopSession.GhostFillRgba, r.Rgba);
            }
        }
        finally
        {
            host.Dispose();
            System.Windows.Forms.WasmDialogHost.Untrack(form);
        }
    }

    [Fact]
    public void NoSession_Paints_No_Ghost()
    {
        WinFormsAppHost.NoteHostUp(); // isolate static ghost slots
        // No active drag anywhere: the frame must carry no ghost marker.
        using var panel = DragPanel();
        WinFormsAppHost host;
        Form form;
        var commands = RenderFrame(panel, out host, out form);
        try
        {
            foreach (DrawCommand cmd in commands)
            {
                if (cmd is DrawTextCommand t)
                    Assert.DoesNotContain("row(s)", t.Text);
                if (cmd is FillRoundedRectCommand r)
                    Assert.NotEqual(RowDragLoopSession.GhostFillRgba, r.Rgba);
            }
        }
        finally
        {
            host.Dispose();
            System.Windows.Forms.WasmDialogHost.Untrack(form);
        }
    }
}
