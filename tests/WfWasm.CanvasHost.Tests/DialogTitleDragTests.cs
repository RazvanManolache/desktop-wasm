using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsForm = System.Windows.Forms.Form;

/// <summary>USERFIX drag lane: floating-window title drag behavior at the
/// managed host level (verifier geometry mirrored). Proves the down/move/up
/// chain moves the form exactly, up clears drag state (no stuck drag), and
/// client-area presses do not drag. Browser/event-delivery behavior is owned
/// by verifier replay, not these tests.
/// </summary>
public sealed class DialogTitleDragTests
{
    [Fact]
    public void Stepped_Drag_Moves_Form_By_Total_Delta()
    {
        // Mirrors the verifier Colors drag: title grab + 20 stepped moves
        // (+140,+90 total) + up at the final point lands exactly.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 360, Height = 240 };
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 100; dlg.Top = 100; dlg.Show();
        try
        {
            host.OnMouseDown(150, 110);
            for (int i = 1; i <= 20; i++)
                host.OnMouseMove(150 + 140 * i / 20, 110 + 90 * i / 20);
            host.OnMouseUp(290, 200);
            Assert.Equal(240, dlg.Location.X);
            Assert.Equal(190, dlg.Location.Y);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Move_Then_Immediate_Up_Keeps_Final_Position()
    {
        // Up-flush ordering analog: a move dispatched immediately before up
        // (no paint between) still lands; the form is not reset by up.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 360, Height = 240 };
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 100; dlg.Top = 100; dlg.Show();
        try
        {
            host.OnMouseDown(150, 110);
            host.OnMouseMove(290, 200);
            host.OnMouseUp(290, 200);
            Assert.Equal(240, dlg.Location.X);
            Assert.Equal(190, dlg.Location.Y);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Up_Clears_Drag_No_Stuck_Drag()
    {
        // After pointer-up, further moves must not move the form.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 360, Height = 240 };
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 100; dlg.Top = 100; dlg.Show();
        try
        {
            host.OnMouseDown(150, 110);
            host.OnMouseMove(200, 150);
            host.OnMouseUp(200, 150);
            Assert.Equal(150, dlg.Location.X);
            Assert.Equal(140, dlg.Location.Y);
            host.OnMouseMove(400, 400);
            host.OnMouseMove(10, 10);
            Assert.Equal(150, dlg.Location.X);
            Assert.Equal(140, dlg.Location.Y);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Client_Area_Press_Does_Not_Drag()
    {
        // A press inside the client area (below the 30px chrome) arms no
        // title drag; moves leave the form in place.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 360, Height = 240 };
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 100; dlg.Top = 100; dlg.Show();
        try
        {
            host.OnMouseDown(150, 200);
            host.OnMouseMove(290, 300);
            host.OnMouseUp(290, 300);
            Assert.Equal(100, dlg.Location.X);
            Assert.Equal(100, dlg.Location.Y);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Stepped_Drag_Shows_Intermediate_Displacement()
    {
        // Continuous dragging: the window tracks intermediate moves, not
        // just the final up point.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 360, Height = 240 };
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 100; dlg.Top = 100; dlg.Show();
        try
        {
            host.OnMouseDown(150, 110);
            for (int i = 1; i <= 10; i++)
                host.OnMouseMove(150 + 140 * i / 20, 110 + 90 * i / 20);
            Assert.Equal(170, dlg.Location.X);
            Assert.Equal(145, dlg.Location.Y);
            for (int i = 11; i <= 20; i++)
                host.OnMouseMove(150 + 140 * i / 20, 110 + 90 * i / 20);
            Assert.Equal(240, dlg.Location.X);
            Assert.Equal(190, dlg.Location.Y);
            host.OnMouseUp(290, 200);
            Assert.Equal(240, dlg.Location.X);
            Assert.Equal(190, dlg.Location.Y);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Dragged_Position_Persists_Across_Repaint()
    {
        // Placement must not snap a user-moved window back: after a drag,
        // a full repaint still paints the title at the moved position.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 360, Height = 240 };
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 100; dlg.Top = 100; dlg.Show();
        try
        {
            host.Paint(new WfWasm.Renderer.RecordingRenderer());
            host.OnMouseDown(150, 110);
            host.OnMouseMove(290, 200);
            host.OnMouseUp(290, 200);
            Assert.Equal(240, dlg.Location.X);
            Assert.Equal(190, dlg.Location.Y);
            var recorder = new WfWasm.Renderer.RecordingRenderer();
            host.Paint(recorder);
            Assert.Equal(240, dlg.Location.X);
            Assert.Equal(190, dlg.Location.Y);
            Assert.Contains(recorder.Commands,
                c => c is WfWasm.Renderer.FillGradientCommand g && g.X == 240 && g.Y == 190);
            Assert.Contains(recorder.Commands,
                c => c is WfWasm.Renderer.DrawTextCommand t && t.Text == "Dock" && t.X == 250);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Drag_Tracks_Outside_Canvas_Unclamped()
    {
        // Managed drag applies the raw delta even outside the view (no
        // clamping in the move branch); documents actual behavior.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 360, Height = 240 };
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 100; dlg.Top = 100; dlg.Show();
        try
        {
            host.OnMouseDown(150, 110);
            host.OnMouseMove(-50, -40);
            host.OnMouseUp(-50, -40);
            Assert.Equal(-100, dlg.Location.X);
            Assert.Equal(-50, dlg.Location.Y);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }
}
