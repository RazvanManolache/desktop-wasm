// Fork-owned: synchronous WM_SHOWWINDOW delivery for canvas-hosted windows.
// Native ShowWindow sends WM_SHOWWINDOW to the window before returning, and
// Control.WmShowWindow is what sets the visible bit and runs CreateControl
// over the visible subtree (Form.Load then fires from Form.OnCreateControl).
// The fork's ShowWindow stub used to swallow the call, so the whole show
// lifecycle was skipped: child controls were never "created" and load-gated
// frameworks (Telerik RadControl.IsLoaded) stayed unloaded forever, which
// left e.g. RadDock unable to activate its first document until a click
// forced activation through a path that bypasses the load gates.

using System.Collections.Concurrent;
using System.Reflection;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace System.Windows.Forms;

public partial class NativeWindow
{
    /// <summary>
    ///  Delivers WM_SHOWWINDOW to the window registered for <paramref name="hWnd"/>,
    ///  mirroring the synchronous notification native ShowWindow performs.
    ///  No-op when the handle is not one of our windows.
    /// </summary>
    internal static void WasmNotifyShowWindow(HWND hWnd, bool shown)
    {
        if (FromHandle((IntPtr)hWnd.Value) is not { } window)
        {
            return;
        }

        // RVW-1105-0900-runtime Job1 (WC2315 section4, fork item 1): structured
        // show-path diagnostics. Entry/exit carry the window handle, dialog
        // type and control name plus a UTC stamp that correlates with the
        // host [NRE-FUNNEL] breadcrumb lines; the NRE line adds the
        // throwing-site type+method and message head. Per-signature gated
        // (show/hide cycles on one handle must not spam). Never throws, never
        // changes the swallow-and-continue contract below, and never
        // materializes stack text (hostile frames abort the browser runtime).
        IntPtr h = (IntPtr)hWnd.Value;
        string who = NotifyWho(h, window);
        string cycleKey = "show|h=" + h.ToString("x") + "|s=" + (shown ? "1" : "0");
        if (NotifyShouldLog(cycleKey))
        {
            Console.WriteLine($"[WasmNotifyShowWindow] show h={h:x} {who} shown={(shown ? 1 : 0)} utc={NotifyUtc()}");
        }
        Message m = Message.Create((IntPtr)hWnd.Value, (int)PInvokeCore.WM_SHOWWINDOW, shown ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
        try
        {
            window.WndProc(ref m);
        }
        catch (Exception e)
        {
            // Native Callback routes WndProc failures to the thread-exception
            // handler instead of letting them escape out of ShowWindow. The
            // fork's exception-dialog path is not browser-safe (its icon load
            // fails), so surface the failure on the console and keep showing.
            string site = NotifySite(e);
            string head = NotifyHead(e.Message);
            if (NotifyShouldLog("shownre|" + site + "|" + head))
            {
                Console.WriteLine($"[WasmNotifyShowWindow] NRE h={h:x} {who} site={site} msg={head} utc={NotifyUtc()}");
            }
            return;
        }
        if (NotifyShouldLog(cycleKey))
        {
            Console.WriteLine($"[WasmNotifyShowWindow] shown h={h:x} {who} shown={(shown ? 1 : 0)} utc={NotifyUtc()}");
        }
        // Top-level popups (Telerik menus, combo dropdowns) are parentless
        // non-Form controls: track them for the host paint layer, which
        // otherwise only knows the active form, tracked dialogs and
        // ToolStrip dropdowns. WndProc above already flipped the visible
        // bit, so visibility reads are current here.
        WasmPopupHost.NoteShowWindow((IntPtr)hWnd.Value, shown);
    }

    /// <summary>
    ///  Delivers WM_DESTROY to the window registered for <paramref name="hWnd"/>,
    ///  mirroring the synchronous notification native DestroyWindow performs.
    ///  No-op when the handle is not one of our windows.
    ///  Control.WmDestroy is what stashes Text into _text on the way down
    ///  ("see wmDestroy" in the fork); without this delivery, any handle
    ///  recreation re-seeds an empty caption and the text is silently lost
    ///  (BugTracker dock captions: Projects/Bugs/Details).
    /// </summary>
    /// <summary>
    ///  Delivers a synthetic WM_PAINT to the NativeWindow registered for
    ///  <paramref name="hWnd"/> (no-op when none is attached). Desktop
    ///  painters that subclass a control (Telerik's TextBoxAdaptor, which
    ///  paints spell squiggles via Graphics.FromHwnd in its AdapterPaint
    ///  handler) rely on the EDIT window receiving WM_PAINT; headless
    ///  controls never produce one. The host calls this for dirty controls
    ///  just before compositing their overlay, so the subclass over-paints
    ///  the same frame the control rastered (desktop order: control first,
    ///  subclass second).
    /// </summary>
    private static readonly ConcurrentDictionary<Type, bool> s_wndProcOverride = new();

    private static bool OverridesWndProc(Type type)
        => s_wndProcOverride.GetOrAdd(type, t =>
            t.GetMethod("WndProc", BindingFlags.NonPublic | BindingFlags.Instance)?.DeclaringType != typeof(Control));

    public static void WasmNotifyPaint(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || FromHandle(hWnd) is not { } window)
        {
            return;
        }

        // Plain windows with no WndProc override need no synthetic dispatch:
        // the host already rastered them through OnPaint this frame, and the
        // dispatch would just re-run the same chain (double OnPaint per frame
        // for every dirty control). Subclassed windows (adaptor NativeWindows,
        // nested in their own types) and WndProc-overriding controls keep the
        // dispatch: their chain over-paints the overlay layer.
        if (window.GetType().DeclaringType == typeof(Control)
            && Control.FromHandle(hWnd) is { } control
            && !OverridesWndProc(control.GetType()))
        {
            return;
        }

        Message m = Message.Create(hWnd, (int)PInvokeCore.WM_PAINT, IntPtr.Zero, IntPtr.Zero);
        try
        {
            window.WndProc(ref m);
        }
        catch (Exception e)
        {
            string who;
            try { who = Control.FromHandle(hWnd)?.GetType().FullName ?? window.GetType().FullName ?? "?"; }
            catch { who = "?"; }
            Console.WriteLine($"[WasmNotifyPaint] {who} :: {e}");
        }
    }

    internal static void WasmNotifyDestroyWindow(HWND hWnd)
    {
        if (FromHandle((IntPtr)hWnd.Value) is not { } window)
        {
            return;
        }

        // RVW-1105-0900-runtime Job1 (WC2315 section4, fork item 2): structured
        // destroy-path diagnostics, same contract as the show path above:
        // handle + dialog type + control name + UTC correlation stamp on
        // entry/exit, throwing-site type+method + message head on NRE.
        // Per-signature gated, never throws, swallow-and-continue intact,
        // no stack-text materialization.
        IntPtr dh = (IntPtr)hWnd.Value;
        string dwho = NotifyWho(dh, window);
        string dcycleKey = "destroy|h=" + dh.ToString("x");
        if (NotifyShouldLog(dcycleKey))
        {
            Console.WriteLine($"[WasmNotifyDestroyWindow] destroy h={dh:x} {dwho} utc={NotifyUtc()}");
        }
        Message m = Message.Create((IntPtr)hWnd.Value, (int)PInvokeCore.WM_DESTROY, IntPtr.Zero, IntPtr.Zero);
        try
        {
            window.WndProc(ref m);
            if (NotifyShouldLog(dcycleKey))
            {
                Console.WriteLine($"[WasmNotifyDestroyWindow] destroyed h={dh:x} {dwho} utc={NotifyUtc()}");
            }
        }
        catch (Exception e)
        {
            string dsite = NotifySite(e);
            string dhead = NotifyHead(e.Message);
            if (NotifyShouldLog("destroynre|" + dsite + "|" + dhead))
            {
                Console.WriteLine($"[WasmNotifyDestroyWindow] NRE h={dh:x} {dwho} site={dsite} msg={dhead} utc={NotifyUtc()}");
            }
            return;
        }
    }

    // RVW-1105-0900-runtime Job1: gate + formatting for the show/destroy
    // diagnostics above. Per-signature budgets mirror the host
    // FirstChanceLogGate so a hot show/hide cycle cannot spam the console.
    // Every helper is fail-closed and never materializes stack text
    // (hostile frames abort the browser runtime); throwing-site identity
    // travels via TargetSite only, the same pattern the host SiteOf uses.
    private static readonly object s_notifyGate = new();
    private static readonly Dictionary<string, int> s_notifyCounts = new(StringComparer.Ordinal);
    private static int s_notifyTotal;
    private const int NotifyPerSignatureCap = 8;
    internal static void NotifyGateResetForTests()
    {
        try { lock (s_notifyGate) { s_notifyCounts.Clear(); s_notifyTotal = 0; } } catch { }
    }
    private const int NotifyGlobalCap = 500;

    private static bool NotifyShouldLog(string key)
    {
        try
        {
            lock (s_notifyGate)
            {
                if (s_notifyTotal >= NotifyGlobalCap) return false;
                s_notifyCounts.TryGetValue(key, out int n);
                if (n >= NotifyPerSignatureCap) return false;
                s_notifyCounts[key] = n + 1;
                s_notifyTotal++;
                return true;
            }
        }
        catch
        {
            return true;
        }
    }

    private static string NotifyWho(IntPtr hWnd, NativeWindow window)
    {
        try
        {
            Control? c = null;
            try { c = Control.FromHandle(hWnd); } catch { }
            string type = c?.GetType().FullName ?? window.GetType().FullName ?? "?";
            string name = "?";
            try { name = c?.Name ?? "?"; } catch { }
            if (string.IsNullOrEmpty(name)) name = "?";
            return "type=" + type + " name=" + name;
        }
        catch
        {
            return "type=? name=?";
        }
    }

    private static string NotifySite(Exception ex)
    {
        try
        {
            var target = ex.TargetSite;
            var dt = target?.DeclaringType;
            if (target is null || dt is null) return "?";
            return dt.FullName + "." + target.Name;
        }
        catch
        {
            return "?";
        }
    }

    private static string NotifyHead(string? text)
    {
        try
        {
            string line = (text ?? "").Split('\n')[0].Trim().Replace('\r', ' ');
            return line.Length > 160 ? line.Substring(0, 160) : line;
        }
        catch
        {
            return "";
        }
    }

    private static string NotifyUtc()
    {
        try { return DateTime.UtcNow.ToString("HH:mm:ss.fff"); }
        catch { return "?"; }
    }
}
