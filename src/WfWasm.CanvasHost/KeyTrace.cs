// SF01-WEDGE-MECH key-dispatch stage tracer (runtime lane, new file).
//
// Purpose: isolate the suggest-popup main-thread wedge (see
// reports/SF1120-CRASHQA-SF01-SF04.md): with a Syncfusion suggest
// PopupHost shown, the next key message wedges the renderer thread
// with no logs and no exception. All Host key-path code reads as
// terminating, so the wedge site must be located empirically.
//
// Usage (requires thin Host-owner integration, requested separately):
// the Host owner adds one-line KeyTrace.Mark("...") calls at the
// stages listed in reports/SF01-WEDGE-MECH-*.md; the browser probe
// enables tracing, reproduces the wedge, and (on the next healthy
// boot or via the bounded repro) dumps the marks. The last mark
// before the wedge is the loop site.
//
// This file adds NO behavior on its own: Enabled defaults to false
// and Mark is a no-op until enabled. It does not modify any
// existing file.

using Microsoft.JSInterop;

namespace WfWasm.CanvasHost;

public static class KeyTrace
{
    private static readonly List<string> s_marks = new();
    private static bool s_enabled;

    public static void SetEnabled(bool on)
    {
        s_enabled = on;
        lock (s_marks)
        {
            s_marks.Clear();
        }
    }

    public static void Mark(string stage)
    {
        if (!s_enabled)
        {
            return;
        }
        // Stream synchronously to the browser console as well as the
        // ring buffer: the wedge under study is a hard main-thread
        // spin, so post-wedge CDP reads are dead and only console
        // events received BEFORE the spin survive. Console.WriteLine
        // in the wasm runtime emits synchronously.
        try { Console.WriteLine("[KeyTrace] " + stage); } catch { }
        lock (s_marks)
        {
            s_marks.Add(DateTimeOffset.UtcNow.ToString("HH:mm:ss.fff") + " " + stage);
            if (s_marks.Count > 200)
            {
                s_marks.RemoveAt(0);
            }
        }
    }

    public static string Dump()
    {
        lock (s_marks)
        {
            return string.Join('\n', s_marks);
        }
    }

    [JSInvokable]
    public static void SetKeyTraceEnabled(bool on) => SetEnabled(on);

    [JSInvokable]
    public static string KeyTraceDump() => Dump();
}
