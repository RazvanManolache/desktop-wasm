// SF02 row-drag loop (dw-runtime): headless replacement for the native
// OLE modal drag loop for Syncfusion row drags. The vendor's
// RowDragDropController calls Control.DoDragDrop, whose headless stub
// returns None synchronously, so no DragOver/Drop ever fires. This helper
// captures the vendor's own DragStart (setting Handled so the vendor skips
// its dead DoDragDrop call) and pumps the five drag events from the
// already-routed control input (MouseMove/MouseUp/KeyDown). No WinFormsAppHost
// changes: production wiring is one EnsureRowDragLoop call per grid from the
// Host owner (R2). See reports/SF-20261102-1120-dw-runtime-sf02.md.
using System.Collections;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace WfWasm.CanvasHost;

public sealed partial class WinFormsAppHost
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, RowDragDriver> s_dragDrivers = new();
    internal static readonly List<WeakReference<RowDragDriver>> s_allDrivers = new();

    /// <summary>
    /// Attach the headless row-drag loop to a grid TableControl.
    /// Idempotent; returns false (never throws) when the control has no
    /// row-drag controller or the served closure lacks the drag surface.
    /// R2 wiring: call once per grid when the grid is created/shown.
    /// </summary>
    public static bool EnsureRowDragLoop(Control tableControl)
    {
        if (tableControl is null || tableControl.IsDisposed)
            return false;
        try
        {
            if (s_dragDrivers.TryGetValue(tableControl, out _))
                return true;
            if (!RowDragDriver.TryCreate(tableControl, out RowDragDriver? driver) || driver is null)
                return false;
            s_dragDrivers.Add(tableControl, driver);
            Console.WriteLine("[DragLoop] armed row loop for " + tableControl.GetType().FullName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Detach a previously attached loop (grid teardown).</summary>
    public static bool DetachRowDragLoop(Control tableControl)
    {
        if (tableControl is null)
            return false;
        try
        {
            if (!s_dragDrivers.TryGetValue(tableControl, out RowDragDriver? driver) || driver is null)
                return false;
            driver.Detach();
            s_dragDrivers.Remove(tableControl);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static void OnVendorRowDragStart(object? sender, EventArgs e)
    {
        try
        {
            RowDragDriver.OnDragStart(sender, e);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[DragLoop] DragStart capture EX: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
        }
    }

    /// <summary>
    /// Host-funnel gesture ghost: the vendor DragStart never fires in the
    /// served demo flow (dead DoDragDrop stub short-circuits the vendor
    /// sequence), so no session starts and the session chip never shows.
    /// The host input funnel (down/move/up) always runs: while the left
    /// button is held on an armed grid past a small threshold, show the
    /// same chip with a "dragging" label. Region-dirty rules identical to
    /// the session path; cleared on up/cancel/session-takeover.
    /// </summary>
    internal static bool IsArmedFor(Control target) =>
        target is not null && s_dragDrivers.TryGetValue(target, out _);

    internal static Control? s_pressTarget;
    internal static int s_pressX;
    internal static int s_pressY;
    internal static Control? s_hostTarget;
    internal static int s_hostX;
    internal static int s_hostY;
    internal static bool s_hasHostGhost;
    private const int HostGhostThresholdPx = 8;

    internal static void NoteHostPress(Control? target, int clientX, int clientY)
    {
        s_pressTarget = target;
        s_pressX = clientX;
        s_pressY = clientY;
        ClearHostGhost();
    }

    internal static void NoteHostMove(Control? target, int clientX, int clientY, bool leftHeld)
    {
        if (!leftHeld || target is null)
        {
            ClearHostGhost();
            return;
        }
        // A live session owns the chip for its target.
        lock (RowDragLoopSession.GhostLock)
        {
            if (RowDragLoopSession.CurrentGhost is not null && RowDragLoopSession.CurrentGhost.Active
                && ReferenceEquals(RowDragLoopSession.CurrentGhost.Target, target))
            {
                ClearHostGhost();
                return;
            }
        }
        if (!IsArmedFor(target))
        {
            ClearHostGhost();
            return;
        }
        if (!ReferenceEquals(s_pressTarget, target))
            return; // Press began elsewhere (or was never recorded).
        int dx = clientX - s_pressX;
        int dy = clientY - s_pressY;
        if (dx * dx + dy * dy <= HostGhostThresholdPx * HostGhostThresholdPx)
            return; // Below threshold: plain press, no ghost yet.
        try
        {
            Rectangle fresh = new(clientX + RowDragLoopSession.GhostOffsetX, clientY + RowDragLoopSession.GhostOffsetY, (int)RowDragLoopSession.GhostWidth, (int)RowDragLoopSession.GhostHeight);
            Rectangle dirty = s_hasHostGhost && ReferenceEquals(s_hostTarget, target)
                ? Rectangle.Union(fresh, new Rectangle(s_hostX + RowDragLoopSession.GhostOffsetX, s_hostY + RowDragLoopSession.GhostOffsetY, (int)RowDragLoopSession.GhostWidth, (int)RowDragLoopSession.GhostHeight))
                : fresh;
            dirty = Rectangle.Intersect(dirty, new Rectangle(0, 0, target.Width, target.Height));
            if (dirty.Width > 0 && dirty.Height > 0)
                WasmRepaintGate.MarkDirtyChainRect(target, dirty);
        }
        catch { }
        s_hostTarget = target;
        s_hostX = clientX;
        s_hostY = clientY;
        s_hasHostGhost = true;
    }

    internal static void NoteHostUp()
    {
        s_pressTarget = null;
        ClearHostGhost();
    }

    private static void ClearHostGhost()
    {
        s_hostTarget = null;
        s_hasHostGhost = false;
    }

    internal static void AbortAllSessions()
    {
        foreach (RowDragDriver driver in RowDragDriver.Drivers())
        {
            try { driver.AbortSession(); }
            catch { }
        }
    }

    internal static void NoteHostPressFor(Control target, int absX, int absY, int originX, int originY)
    {
        NoteHostPress(target, absX - GetControlAbsX(target, originX), absY - GetControlAbsY(target, originY));
    }

    internal static void AbortAllDragSessions()
    {
        foreach (RowDragDriver driver in RowDragDriver.Drivers())
        {
            try { driver.AbortSession(); }
            catch { }
        }
    }

    /// <summary>Test-only: registers a host ghost without arming.</summary>
    internal static void TestSetHostGhost(Control target, int clientX, int clientY)
    {
        s_hostTarget = target;
        s_hostX = clientX;
        s_hostY = clientY;
        s_hasHostGhost = true;
    }
}

/// <summary>
/// Per-control row-drag driver: owns the vendor DragStart subscription and
/// one live <see cref="RowDragLoopSession"/> at a time. All vendor member
/// access is by name (the Syncfusion controller is not a compile-time
/// reference); all framework drag members are compile-time (served closure
/// unification, ComboBoxBrowserHook precedent).
/// </summary>
internal sealed class RowDragDriver
{
    private readonly Control _target;
    private readonly object _controller;
    private readonly EventInfo _dragStartEvent;
    private readonly Delegate _dragStartHandler;
    private readonly Func<object, object?> _readGrid;
    private RowDragLoopSession? _session;
    private MouseEventHandler? _moveHandler;
    private MouseEventHandler? _upHandler;
    private KeyEventHandler? _keyHandler;

    private RowDragDriver(Control target, object controller, EventInfo dragStartEvent, Delegate dragStartHandler, Func<object, object?> readGrid)
    {
        _target = target;
        _controller = controller;
        _dragStartEvent = dragStartEvent;
        _dragStartHandler = dragStartHandler;
        _readGrid = readGrid;
    }

    internal static bool TryCreate(Control target, out RowDragDriver? driver)
    {
        driver = null;
        try
        {
            // Vendor path: TableControl.DataGrid.RowDragDropController
            // (public property; verified in served grid behavior).
            PropertyInfo? gridProp = target.GetType().GetProperty("DataGrid",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object? grid = gridProp?.GetValue(target);
            if (grid is null)
                return false;
            PropertyInfo? ctrlProp = grid.GetType().GetProperty("RowDragDropController",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object? controller = ctrlProp?.GetValue(grid);
            if (controller is null)
                return false;
            // Enabled-drag demo (SF02-R2 gate): no staged sample sets
            // AllowDraggingRows, so without this the vendor never
            // initiates and the loop is dead code. Enabling at arm time
            // makes the served grid row-draggable; remove to restore the
            // demo default. Runs before the current press is routed, so
            // the vendor's own down-handling already sees it.
            try
            {
                PropertyInfo? dragFlag = grid.GetType().GetProperty("AllowDraggingRows",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                dragFlag?.SetValue(grid, true);
            }
            catch
            {
            }
            EventInfo? dragStart = controller.GetType().GetEvent("DragStart",
                BindingFlags.Instance | BindingFlags.Public);
            if (dragStart is null || dragStart.EventHandlerType is null)
                return false;
            MethodInfo? handler = typeof(WinFormsAppHost).GetMethod("OnVendorRowDragStart",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (handler is null)
                return false;
            // Contravariant bind: handler takes base EventArgs, the vendor
            // delegate takes the derived GridRowDragStartEventArgs.
            Delegate d = Delegate.CreateDelegate(dragStart.EventHandlerType, handler);
            if (!RowDragLoopSession.SurfaceAvailable(target))
                return false;
            dragStart.AddEventHandler(controller, d);
            driver = new RowDragDriver(target, controller, dragStart, d, c =>
                c.GetType().GetProperty("DataGrid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(c));
            lock (WinFormsAppHost.s_allDrivers) WinFormsAppHost.s_allDrivers.Add(new WeakReference<RowDragDriver>(driver));
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static void OnDragStart(object? sender, EventArgs e)
    {
        if (sender is null)
            return;
        // Find the owning driver: the controller instance is the sender.
        foreach (var kv in Drivers())
        {
            if (ReferenceEquals(kv.Controller, sender))
            {
                kv.StartSession(e);
                return;
            }
        }
    }

    internal void AbortSession()
    {
        try { _session?.Abort(); }
        catch { }
    }

    internal static IEnumerable<RowDragDriver> Drivers()
    {
        // ConditionalWeakTable has no public enumerator; track drivers here.
        lock (WinFormsAppHost.s_allDrivers)
        {
            foreach (var w in WinFormsAppHost.s_allDrivers.ToArray())
            {
                if (w.TryGetTarget(out RowDragDriver? d) && d is not null)
                    yield return d;
            }
        }
    }

    internal object Controller => _controller;

    private void StartSession(EventArgs e)
    {
        try
        {
            Type t = e.GetType();
            object? records = t.GetProperty("DraggingRecords", BindingFlags.Instance | BindingFlags.Public)?.GetValue(e);
            if (records is not IList list || list.Count == 0)
                return; // Vendor also no-ops on empty records.
            t.GetProperty("Handled", BindingFlags.Instance | BindingFlags.Public)?.SetValue(e, true);
            object? grid = _readGrid(_target);
            var data = new LoopDataObject();
            data.SetData("DragSourceDataGrid", grid);
            data.SetData("Records", records);
            EndSessionHandlers();
            _session = new RowDragLoopSession(_target, data, DragDropEffects.Copy | DragDropEffects.Move)
            {
                DragItemCount = list.Count,
            };
            Console.WriteLine("[DragLoop] session start records=" + list.Count + " target=" + _target.GetType().FullName);
            _moveHandler = (_, me) => PumpMove(me);
            _upHandler = (_, me) => PumpUp(me);
            _keyHandler = (_, ke) => PumpKey(ke);
            _target.MouseMove += _moveHandler;
            _target.MouseUp += _upHandler;
            _target.KeyDown += _keyHandler;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[DragLoop] session start EX: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
        }
    }

    private void PumpMove(MouseEventArgs me)
    {
        try { _session?.OnMove(me.X, me.Y, me.Button, KeyStateBits(), escapePressed: false); }
        catch (Exception ex)
        {
            Console.WriteLine("[DragLoop] move EX: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
        }
    }

    private void PumpUp(MouseEventArgs me)
    {
        try { _session?.OnButtonUp(me.X, me.Y, KeyStateBits()); }
        catch (Exception ex)
        {
            Console.WriteLine("[DragLoop] up EX: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
        }
        finally { EndSessionHandlers(); }
    }

    private void PumpKey(KeyEventArgs ke)
    {
        if (ke.KeyCode == Keys.Escape && _session is not null && _session.Active)
        {
            try { _session.OnEscape(); }
            catch (Exception ex)
            {
                Console.WriteLine("[DragLoop] esc EX: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
            }
            finally { EndSessionHandlers(); }
        }
    }

    private void EndSessionHandlers()
    {
        try { _session?.Abort(); } catch { }
        _session = null;
        if (_moveHandler is not null) { try { _target.MouseMove -= _moveHandler; } catch { } _moveHandler = null; }
        if (_upHandler is not null) { try { _target.MouseUp -= _upHandler; } catch { } _upHandler = null; }
        if (_keyHandler is not null) { try { _target.KeyDown -= _keyHandler; } catch { } _keyHandler = null; }
    }

    internal void Detach()
    {
        try { _dragStartEvent.RemoveEventHandler(_controller, _dragStartHandler); } catch { }
        EndSessionHandlers();
    }

    private static int KeyStateBits()
    {
        // OLE MK_* bits: SHIFT=4, CONTROL=8, ALT=32.
        int bits = 0;
        try
        {
            Keys mods = WasmMouseState.Modifiers;
            if ((mods & Keys.Shift) != 0) bits |= 4;
            if ((mods & Keys.Control) != 0) bits |= 8;
            if ((mods & Keys.Alt) != 0) bits |= 32;
        }
        catch { }
        return bits;
    }
}

/// <summary>
/// Managed dictionary-backed <see cref="IDataObject"/> for the loop payload.
/// The fork's DataObject composes COM on construction (fails outside a
/// native modal loop); the vendor only reads the "Records" and
/// "DragSourceDataGrid" keys, which this serves from a dictionary.
/// </summary>
public sealed class LoopDataObject : IDataObject
{
    private readonly Dictionary<string, object?> _data = new(StringComparer.Ordinal);

    public object? GetData(string format) => GetData(format, true);

    public object? GetData(string format, bool autoConvert) =>
        _data.TryGetValue(format, out object? value) ? value : null;

    public object? GetData(Type format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return GetData(format.FullName ?? string.Empty);
    }

    public void SetData(string format, object? data) => SetData(format, true, data);

    public void SetData(object? data)
    {
        if (data is not null)
            SetData(data.GetType().FullName ?? string.Empty, data);
    }

    public void SetData(string format, bool autoConvert, object? data) => _data[format] = data;

    public void SetData(Type format, object? data)
    {
        ArgumentNullException.ThrowIfNull(format);
        SetData(format.FullName ?? string.Empty, data);
    }

    public bool GetDataPresent(string format) => GetDataPresent(format, true);

    public bool GetDataPresent(string format, bool autoConvert) => _data.ContainsKey(format);

    public bool GetDataPresent(Type format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return GetDataPresent(format.FullName ?? string.Empty);
    }

    public string[] GetFormats() => GetFormats(true);

    public string[] GetFormats(bool autoConvert) => new List<string>(_data.Keys).ToArray();
}

/// <summary>
/// Headless OLE-equivalent pump: sequences QueryContinueDrag / DragOver /
/// GiveFeedback / DragDrop / DragLeave onto the target control, mirroring
/// the desktop modal order (QC-Drop precedes DragDrop; Cancel precedes
/// DragLeave). Screen coordinates follow the desktop contract (PointToScreen
/// of the client point); the vendor's own PointToClient round-trips them.
/// </summary>
public sealed class RowDragLoopSession
{
    private static MethodInfo? s_onDragOver;
    private static MethodInfo? s_onDragDrop;
    private static MethodInfo? s_onGiveFeedback;
    private static MethodInfo? s_onQueryContinueDrag;
    private static MethodInfo? s_onDragLeave;
    private static bool s_surfaceChecked;
    private static bool s_surfaceOk;

    // Ghost chip identity (asserted by RowDragGhostTests; keep stable):
    // 132x30 rounded chip at cursor + (12,16) with "<n> row(s)" centered.
    public const uint GhostFillRgba = 0x1E40AFCCu;
    private const uint GhostLineRgba = 0x1E3A8AFFu;
    internal const float GhostWidth = 132f;
    internal const float GhostHeight = 30f;
    internal const int GhostOffsetX = 12;
    internal const int GhostOffsetY = 16;
    private const string GhostFontCss = "12px 'Segoe UI', sans-serif";

    internal static readonly object GhostLock = new();
    internal static RowDragLoopSession? CurrentGhost;

    private readonly Control _target;
    private readonly IDataObject _data;
    private readonly DragDropEffects _allowed;
    private readonly Point _screenOrigin;
    private DragDropEffects _lastEffect = DragDropEffects.None;
    private int _ghostX;
    private int _ghostY;
    private bool _hasGhost;

    /// <summary>
    /// Vendor record count for the ghost label ("N row(s)").
    /// Set by the driver from DragStart args; 0 labels "dragging".
    /// </summary>
    public int DragItemCount { get; set; }

    public RowDragLoopSession(Control target, IDataObject data, DragDropEffects allowed)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _allowed = allowed;
        if (!SurfaceAvailable(target))
            throw new InvalidOperationException("Drag surface unavailable on target.");
        _screenOrigin = target.PointToScreen(Point.Empty);
        Active = true;
    }

    public bool Active { get; private set; }

    internal Control Target => _target;

    internal static bool SurfaceAvailable(Control target)
    {
        if (s_surfaceChecked)
            return s_surfaceOk;
        try
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
            Type t = target.GetType();
            s_onDragOver = t.GetMethod("OnDragOver", f);
            s_onDragDrop = t.GetMethod("OnDragDrop", f);
            s_onGiveFeedback = t.GetMethod("OnGiveFeedback", f);
            s_onQueryContinueDrag = t.GetMethod("OnQueryContinueDrag", f);
            s_onDragLeave = t.GetMethod("OnDragLeave", f);
            s_surfaceOk = s_onDragOver is not null && s_onDragDrop is not null
                && s_onGiveFeedback is not null && s_onQueryContinueDrag is not null
                && s_onDragLeave is not null;
        }
        catch { s_surfaceOk = false; }
        s_surfaceChecked = true;
        return s_surfaceOk;
    }

    public void OnMove(int clientX, int clientY, MouseButtons buttons, int keyState, bool escapePressed)
    {
        if (!Active)
            return;
        if (escapePressed)
        {
            OnEscape();
            return;
        }
        if ((buttons & MouseButtons.Left) == 0)
            return; // Button released outside the press path; up handles it.
        Point p = ToScreen(clientX, clientY);
        RaiseQueryContinue(keyState, escapePressed: false, DragAction.Continue);
        DragEventArgs over = RaiseDragOver(p, keyState);
        _lastEffect = over.Effect;
        RaiseGiveFeedback(over.Effect);
        // Ghost tracks the last held move. The host mousemove path only
        // dirties a 96x96 cursor neighborhood, which clips the 132x30 chip
        // tail â€” so mark the old+new chip union dirty here. Region-scoped
        // (chip-sized, not full-canvas); rides the same frame as the move.
        try
        {
            Rectangle fresh = new(clientX + GhostOffsetX, clientY + GhostOffsetY, (int)GhostWidth, (int)GhostHeight);
            Rectangle dirty = _hasGhost
                ? Rectangle.Union(fresh, new Rectangle(_ghostX + GhostOffsetX, _ghostY + GhostOffsetY, (int)GhostWidth, (int)GhostHeight))
                : fresh;
            dirty = Rectangle.Intersect(dirty, new Rectangle(0, 0, _target.Width, _target.Height));
            if (dirty.Width > 0 && dirty.Height > 0)
                WasmRepaintGate.MarkDirtyChainRect(_target, dirty);
        }
        catch { }
        _ghostX = clientX;
        _ghostY = clientY;
        _hasGhost = true;
        lock (GhostLock) CurrentGhost = this;
    }

    public void OnButtonUp(int clientX, int clientY, int keyState)
    {
        if (!Active)
            return;
        Point p = ToScreen(clientX, clientY);
        RaiseQueryContinue(keyState, escapePressed: false, DragAction.Drop);
        DragEventArgs over = RaiseDragOver(p, keyState);
        _lastEffect = over.Effect;
        RaiseDragDrop(p, keyState, _lastEffect);
        EndGhost();
    }

    public void OnEscape()
    {
        if (!Active)
            return;
        RaiseQueryContinue(0, escapePressed: true, DragAction.Cancel);
        s_onDragLeave?.Invoke(_target, new object[] { EventArgs.Empty });
        EndGhost();
    }

    public void Abort() => EndGhost();

    private void EndGhost()
    {
        Active = false;
        _hasGhost = false;
        lock (GhostLock)
        {
            if (ReferenceEquals(CurrentGhost, this))
                CurrentGhost = null;
        }
    }

    /// <summary>
    /// Paints the active drag ghost for exactly the session's target
    /// control. Called once per control from the host paint overlay hook;
    /// a no-op without an active moved session, so idle frames are
    /// byte-identical to before.
    /// </summary>
    internal static void PaintDragGhost(WfWasm.Shim.ICanvas canvas, Control control, int absX, int absY)
    {
        RowDragLoopSession? s;
        lock (GhostLock) s = CurrentGhost;
        if (s is not null && s.Active && s._hasGhost && ReferenceEquals(s._target, control))
        {
            string label = s.DragItemCount > 0 ? $"{s.DragItemCount} row(s)" : "dragging";
            DrawChip(canvas, absX + s._ghostX + GhostOffsetX, absY + s._ghostY + GhostOffsetY, label);
            return;
        }
        if (WinFormsAppHost.s_hasHostGhost && ReferenceEquals(WinFormsAppHost.s_hostTarget, control))
        {
            DrawChip(canvas, absX + WinFormsAppHost.s_hostX + GhostOffsetX, absY + WinFormsAppHost.s_hostY + GhostOffsetY, "dragging");
        }
    }

    private static void DrawChip(WfWasm.Shim.ICanvas canvas, float gx, float gy, string label)
    {
        canvas.FillRoundedRectangle(gx, gy, GhostWidth, GhostHeight, 4f, GhostFillRgba);
        canvas.DrawLine(gx, gy, gx + GhostWidth, gy, 1f, GhostLineRgba);
        canvas.DrawLine(gx, gy + GhostHeight, gx + GhostWidth, gy + GhostHeight, 1f, GhostLineRgba);
        canvas.DrawLine(gx, gy, gx, gy + GhostHeight, 1f, GhostLineRgba);
        canvas.DrawLine(gx + GhostWidth, gy, gx + GhostWidth, gy + GhostHeight, 1f, GhostLineRgba);
        canvas.DrawTextAligned(label, gx + 10, gy + GhostHeight / 2f, GhostFontCss,
            0xFFFFFFFFu, WfWasm.Shim.TextHAlign.Left, WfWasm.Shim.TextVAlign.Middle);
    }

    private Point ToScreen(int clientX, int clientY) =>
        new(_screenOrigin.X + clientX, _screenOrigin.Y + clientY);

    private void RaiseQueryContinue(int keyState, bool escapePressed, DragAction action)
    {
        var args = new QueryContinueDragEventArgs(keyState, escapePressed, DragAction.Continue);
        args.Action = action;
        s_onQueryContinueDrag?.Invoke(_target, new object[] { args });
    }

    private DragEventArgs RaiseDragOver(Point screen, int keyState)
    {
        var args = new DragEventArgs(_data, keyState, screen.X, screen.Y, _allowed, _lastEffect);
        s_onDragOver?.Invoke(_target, new object[] { args });
        return args;
    }

    private void RaiseGiveFeedback(DragDropEffects effect)
    {
        var args = new GiveFeedbackEventArgs(effect, useDefaultCursors: true);
        s_onGiveFeedback?.Invoke(_target, new object[] { args });
    }

    private void RaiseDragDrop(Point screen, int keyState, DragDropEffects effect)
    {
        var args = new DragEventArgs(_data, keyState, screen.X, screen.Y, _allowed, effect);
        s_onDragDrop?.Invoke(_target, new object[] { args });
    }
}
