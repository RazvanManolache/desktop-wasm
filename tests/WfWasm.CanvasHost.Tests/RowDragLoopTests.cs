// SF02 row-drag loop tests (dw-runtime): the headless OLE-equivalent pump.
// Host-driven like VirtualWindowDragTests: a fake vendor-shaped controller
// (same member names as Syncfusion's RowDragDropController/DragStart args,
// verified against the served grid's decompiled contract) feeds DragStart;
// the loop must then sequence QueryContinueDrag/DragOver/GiveFeedback/
// DragDrop/Escape-cancel onto the target with the desktop order and the
// vendor's DataObject keys ("Records", "DragSourceDataGrid").
using System.Drawing;
using System.Windows.Forms;
using WfWasm.CanvasHost;

public sealed class RowDragLoopTests
{
    // Vendor-shaped fakes: member NAMES mirror the served contract
    // (GridRowDragStartEventArgs.DraggingRecords/Handled,
    // RowDragDropController.DragStart, TableControl.DataGrid,
    // SfDataGrid.RowDragDropController). Only the reflection mechanics and
    // the pump order are under test here; the real vendor half is gated in
    // the browser ( DragStart capture cannot fire without the vendor grid).
    private sealed class FakeStartArgs : EventArgs
    {
        public List<object> DraggingRecords { get; } = new();
        public bool Handled { get; set; }
    }

    private sealed class FakeController
    {
        public event EventHandler<FakeStartArgs>? DragStart;
        public FakeStartArgs Raise(List<object> records)
        {
            var args = new FakeStartArgs();
            args.DraggingRecords.AddRange(records);
            DragStart?.Invoke(this, args);
            return args;
        }
    }

    private sealed class FakeGrid
    {
        public FakeGrid(object? controller) => RowDragDropController = controller;
        public object? RowDragDropController { get; }
        public bool AllowDraggingRows { get; set; }
    }

    // Named exactly like the vendor grid part the R2 wiring keys on
    // (control.GetType().Name == "TableControl").
    private sealed class TableControl : Panel
    {
        public TableControl(object? grid)
        {
            DataGrid = grid;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }
        public object? DataGrid { get; }
    }

    private sealed class FakeTable : Panel
    {
        public FakeTable(object? grid)
        {
            DataGrid = grid;
            // Like the real TableControl: focusable so Escape arrives.
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }
        public object? DataGrid { get; }
    }

    private sealed class Seen
    {
        public readonly List<string> Order = new();
        public DragDropEffects LastOverEffect = DragDropEffects.None;
        public DragAction LastQueryAction = DragAction.Continue;
        public IDataObject? DropData;
        public int LeaveCount;
    }

    private static (WinFormsAppHost host, Form form, FakeTable table, FakeController controller, Seen seen) Setup()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(WfWasm.Shim.AppScene.Smoke);
        // The loop test rides the scene form: the host routes keys only to
        // controls inside the active (scene) form, like the real grid.
        // The form is host-owned (do not dispose); the table is removed in
        // Teardown so the shared scene shape is restored per test.
        var form = host.GetOrCreateForm(WfWasm.Shim.AppScene.Smoke);
        Assert.NotNull(form);
        var controller = new FakeController();
        var table = new FakeTable(new FakeGrid(controller)) { Location = new Point(30, 200), Size = new Size(300, 100) };
        form.Controls.Add(table);
        var seen = new Seen();
        table.DragOver += (_, e) => { seen.Order.Add("over"); e.Effect = DragDropEffects.Move; seen.LastOverEffect = e.Effect; };
        table.DragDrop += (_, e) => { seen.Order.Add("drop"); seen.DropData = e.Data; };
        table.GiveFeedback += (_, _) => seen.Order.Add("feedback");
        table.QueryContinueDrag += (_, e) => { seen.Order.Add("query:" + e.Action); seen.LastQueryAction = e.Action; };
        table.DragLeave += (_, _) => { seen.Order.Add("leave"); seen.LeaveCount++; };
        return (host, form, table, controller, seen);
    }

    private static void Teardown(WinFormsAppHost host, Form form, FakeTable table)
    {
        try { WinFormsAppHost.DetachRowDragLoop(table); } catch { }
        try { form.Controls.Remove(table); } catch { }
        try { table.Dispose(); } catch { }
        host.Dispose();
    }

    private static Point ClientOf(Control table, int dx, int dy)
    {
        Point origin = table.PointToScreen(Point.Empty);
        return new Point(origin.X + dx, origin.Y + dy);
    }

    [Fact]
    public void Pump_Sequences_Query_Over_Feedback_Then_Drop_On_Up()
    {
        var (host, form, table, controller, seen) = Setup();
        try
        {
            Assert.True(WinFormsAppHost.EnsureRowDragLoop(table), "attach");
            Point down = ClientOf(table, 40, 30);
            host.OnMouseDown(down.X, down.Y);
            var records = new List<object> { "row-a", "row-b" };
            FakeStartArgs start = controller.Raise(records);
            Assert.True(start.Handled, "vendor skips dead DoDragDrop");

            Point m1 = ClientOf(table, 60, 50);
            host.OnMouseMove(m1.X, m1.Y);
            Point m2 = ClientOf(table, 90, 80);
            host.OnMouseMove(m2.X, m2.Y);
            Assert.Contains("over", seen.Order);
            Assert.Contains("feedback", seen.Order);
            Assert.Contains("query:Continue", seen.Order);
            Assert.Equal(DragDropEffects.Move, seen.LastOverEffect);

            Point up = ClientOf(table, 120, 110);
            host.OnMouseUp(up.X, up.Y);
            Assert.Contains("query:Drop", seen.Order);
            Assert.Contains("drop", seen.Order);
            Assert.NotNull(seen.DropData);
            Assert.True(seen.DropData!.GetDataPresent("Records"), "records key");
            Assert.True(seen.DropData!.GetDataPresent("DragSourceDataGrid"), "source key");
            Assert.Equal(records, seen.DropData!.GetData("Records"));

            int n = seen.Order.Count;
            Point extra = ClientOf(table, 10, 10);
            host.OnMouseMove(extra.X, extra.Y);
            Assert.Equal(n, seen.Order.Count);
        }
        finally { Teardown(host, form, table); }
    }

    [Fact]
    public void Escape_Cancels_With_Leave_And_No_Drop()
    {
        var (host, form, table, controller, seen) = Setup();
        try
        {
            Assert.True(WinFormsAppHost.EnsureRowDragLoop(table), "attach");
            Point down = ClientOf(table, 40, 30);
            host.OnMouseDown(down.X, down.Y);
            controller.Raise(new List<object> { "row-a" });
            Point m1 = ClientOf(table, 70, 60);
            host.OnMouseMove(m1.X, m1.Y);
            Assert.Contains("over", seen.Order);

            host.OnKeyDown("Escape");
            Assert.Contains("query:Cancel", seen.Order);
            Assert.Contains("leave", seen.Order);
            Assert.DoesNotContain("drop", seen.Order);

            Point up = ClientOf(table, 120, 110);
            host.OnMouseUp(up.X, up.Y);
            Assert.DoesNotContain("drop", seen.Order);
        }
        finally { Teardown(host, form, table); }
    }

    [Fact]
    public void Attach_Returns_False_Without_Controller_And_Detach_Matches()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(WfWasm.Shim.AppScene.Smoke);
        using var plain = new Panel { Location = new Point(5, 5), Size = new Size(50, 50) };
        Assert.False(WinFormsAppHost.EnsureRowDragLoop(plain), "plain control has no drag controller");
        Assert.False(WinFormsAppHost.DetachRowDragLoop(plain), "nothing attached");

        var (host2, form, table, _, _) = Setup();
        try
        {
            Assert.True(WinFormsAppHost.EnsureRowDragLoop(table), "attach");
            Assert.True(WinFormsAppHost.EnsureRowDragLoop(table), "idempotent re-attach");
            Assert.True(WinFormsAppHost.DetachRowDragLoop(table), "detach");
            Assert.False(WinFormsAppHost.DetachRowDragLoop(table), "second detach is a no-op");
        }
        finally { Teardown(host2, form, table); }
    }

    [Fact]
    public void Empty_Records_Are_Not_Handled_And_Pump_Stays_Silent()
    {
        var (host, form, table, controller, seen) = Setup();
        try
        {
            Assert.True(WinFormsAppHost.EnsureRowDragLoop(table), "attach");
            Point down = ClientOf(table, 40, 30);
            host.OnMouseDown(down.X, down.Y);
            FakeStartArgs start = controller.Raise(new List<object>());
            Assert.False(start.Handled, "empty records stay unhandled like the vendor");
            Point m1 = ClientOf(table, 70, 60);
            host.OnMouseMove(m1.X, m1.Y);
            Assert.Empty(seen.Order);
            Point up = ClientOf(table, 90, 80);
            host.OnMouseUp(up.X, up.Y);
            Assert.Empty(seen.Order);
        }
        finally { Teardown(host, form, table); }
    }

    [Fact]
    public void Session_Ignores_Moves_Without_Left_Button_And_Ends_On_Up()
    {
        using var panel = new Panel { Size = new Size(100, 100) };
        var data = new LoopDataObject();
        data.SetData("Records", new List<object> { "row-a" });
        var session = new RowDragLoopSession(panel, data, DragDropEffects.Copy | DragDropEffects.Move);
        int overs = 0, drops = 0;
        panel.DragOver += (_, _) => overs++;
        panel.DragDrop += (_, _) => drops++;

        session.OnMove(10, 10, MouseButtons.None, keyState: 0, escapePressed: false);
        Assert.Equal(0, overs);
        session.OnButtonUp(10, 10, keyState: 0);
        Assert.Equal(1, drops);
        Assert.False(session.Active);
        Assert.Equal(1, overs); // The up path raises one final DragOver before Drop.
        session.OnMove(20, 20, MouseButtons.Left, keyState: 0, escapePressed: false);
        Assert.Equal(1, overs); // Inactive session stays silent.
    }
    [Fact]
    public void HostWiring_ArmsLoop_On_TableControl_Press()
    {
        // The R2 wiring in OnMouseDown arms EnsureRowDragLoop for controls
        // named TableControl. The test never calls Ensure directly: Handled
        // can only become true if the host press wired the driver.
        using var host = new WinFormsAppHost();
        host.SelectScene(WfWasm.Shim.AppScene.Smoke);
        var form = host.GetOrCreateForm(WfWasm.Shim.AppScene.Smoke);
        Assert.NotNull(form);
        var controller = new FakeController();
        using var table = new TableControl(new FakeGrid(controller))
        {
            Location = new Point(340, 200),
            Size = new Size(140, 100)
        };
        form.Controls.Add(table);
        int drops = 0;
        table.DragDrop += (_, _) => drops++;
        try
        {
            Point down = ClientOf(table, 40, 30);
            host.OnMouseDown(down.X, down.Y);
            FakeStartArgs start = controller.Raise(new List<object> { "row-a" });
            Assert.True(start.Handled, "host wiring armed the loop");
            Point m1 = ClientOf(table, 70, 60);
            host.OnMouseMove(m1.X, m1.Y);
            Point up = ClientOf(table, 100, 80);
            host.OnMouseUp(up.X, up.Y);
            Assert.Equal(1, drops);
        }
        finally
        {
            try { WinFormsAppHost.DetachRowDragLoop(table); } catch { }
            try { form.Controls.Remove(table); } catch { }
            host.Dispose();
        }
    }
    [Fact]
    public void Attach_Enables_RowDrag_On_The_Grid()
    {
        // No staged sample sets AllowDraggingRows (full staging scan), so
        // the loop enables it at arm time; without this the vendor never
        // initiates and the loop is dead code.
        using var host = new WinFormsAppHost();
        host.SelectScene(WfWasm.Shim.AppScene.Smoke);
        var form = host.GetOrCreateForm(WfWasm.Shim.AppScene.Smoke);
        Assert.NotNull(form);
        var controller = new FakeController();
        var grid = new FakeGrid(controller) { AllowDraggingRows = false };
        using var table = new TableControl(grid)
        {
            Location = new Point(340, 60),
            Size = new Size(140, 100)
        };
        form.Controls.Add(table);
        try
        {
            Point down = ClientOf(table, 40, 30);
            host.OnMouseDown(down.X, down.Y);
            Assert.True(grid.AllowDraggingRows, "arm enables the vendor flag");
            FakeStartArgs start = controller.Raise(new List<object> { "row-a" });
            Assert.True(start.Handled, "capture still works once enabled");
        }
        finally
        {
            try { WinFormsAppHost.DetachRowDragLoop(table); } catch { }
            try { form.Controls.Remove(table); } catch { }
            host.Dispose();
        }
    }
}