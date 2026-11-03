using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WfWasm.CanvasHost;

public sealed class VirtualWindowDragTests
{
    // Like a parked Paint.NET tool window: an ordinary Location assignment
    // is snapped back unless the native moving loop updates the parked point.
    private sealed class ParkedForm : Form
    {
        internal Point Parked;
        internal readonly List<int> Messages = new();
        internal bool EnforceParking;
        protected override void OnLocationChanged(EventArgs e)
        {
            if (EnforceParking && Location != Parked) Location = Parked;
            base.OnLocationChanged(e);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg is 0x231 or 0x216 or 0x232) Messages.Add(m.Msg);
            if (m.Msg == 0x216)
                Parked = new Point(Marshal.ReadInt32(m.LParam, 0), Marshal.ReadInt32(m.LParam, 4));
            base.WndProc(ref m);
        }
    }

    [Fact]
    public void Host_Drag_Notifies_Snapping_Before_Location_And_Ends_Once()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(WfWasm.Shim.AppScene.Smoke);
        using var form = new ParkedForm { StartPosition = FormStartPosition.Manual, Location = new Point(100, 100), Size = new Size(360, 240) };
        form.Show();
        form.Parked = form.Location;
        form.EnforceParking = true;
        try
        {
            form.Location = new Point(200, 200);
            Assert.Equal(new Point(100, 100), form.Location);
            host.OnMouseDown(150, 110);
            host.OnMouseMove(220, 155);
            Assert.Equal(new Point(170, 145), form.Location); // while button held
            host.OnMouseMove(290, 200);
            host.OnMouseUp(290, 200);
            Assert.Equal(new Point(240, 190), form.Location);
            Assert.Equal(new[] { 0x231, 0x216, 0x216, 0x232 }, form.Messages);
            host.OnMouseMove(500, 400);
            Assert.Equal(new Point(240, 190), form.Location);
        }
        finally { WasmDialogHost.Untrack(form); }
    }

    [Fact]
    public void Cancellation_Ends_The_Move_Loop_Without_A_Stuck_Drag()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(WfWasm.Shim.AppScene.Smoke);
        using var form = new ParkedForm { StartPosition = FormStartPosition.Manual, Location = new Point(100, 100), Size = new Size(360, 240) };
        form.Show();
        form.Parked = form.Location;
        form.EnforceParking = true;
        try
        {
            host.OnMouseDown(150, 110);
            host.OnMouseMove(220, 155);
            host.OnPointerCanceled();
            host.OnPointerCanceled();
            host.OnMouseMove(290, 200);
            Assert.Equal(new Point(170, 145), form.Location);
            Assert.Equal(new[] { 0x231, 0x216, 0x232 }, form.Messages);
            Assert.Equal(MouseButtons.None, WasmMouseState.Buttons);
        }
        finally { WasmDialogHost.Untrack(form); }
    }
}
