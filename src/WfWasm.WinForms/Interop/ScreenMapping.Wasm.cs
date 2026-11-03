// Fork-owned: coherent client<->screen mapping. "Screen space" in the
// browser is the host's scene space: the host feeds every pointer position
// to WasmMouseState in scene coordinates, and this walk inverts it for
// MapWindowPoints / ClientToScreen / ScreenToClient. WindowBoundsStorage
// alone is parent-relative, and the previous MapWindowPoints stub was a
// no-op, so PointToClient/PointToScreen were identity and polling APIs
// (Control.MousePosition via PointFromScreen, e.g. Telerik's diagram hit
// testing) missed every nested control. Regression: RadDiagram clicks and
// drags silently hit nothing (shapes never select).

using System.Drawing;
using Windows.Win32.Foundation;

namespace Windows.Win32;

internal static class WasmScreenMapping
{
    /// <summary>
    ///  Client origin of <paramref name="hWnd"/> in scene ("screen")
    ///  coordinates. Null/unknown windows map to the origin.
    ///  Guarantees bounded traversal (depth < 50) and safe foreign handle handling.
    /// </summary>
    internal static Point ClientOrigin(HWND hWnd)
    {
        if (hWnd.IsNull || hWnd.Value == 0)
        {
            return Point.Empty;
        }

        global::System.Windows.Forms.Control? control = null;
        try
        {
            control = global::System.Windows.Forms.Control.FromHandle((nint)hWnd.Value);
        }
        catch
        {
            return Point.Empty;
        }

        if (control is null || control.IsDisposed)
        {
            return Point.Empty;
        }

        int x = 0;
        int y = 0;
        int depth = 0;
        for (var current = control; current != null && depth < 50; depth++)
        {
            if (current.IsDisposed)
            {
                break;
            }

            try
            {
                x += current.Location.X;
                y += current.Location.Y;
                if (current is global::System.Windows.Forms.Form form && (form.TopLevel || form.Parent == null))
                {
                    y += ChromeHeight(form);
                    break;
                }
                current = current.Parent;
            }
            catch
            {
                break;
            }
        }

        return new Point(x, y);
    }

    internal static Point ClientOrigin(nint hWnd) => ClientOrigin((HWND)hWnd);

    // Must match WinFormsAppHost.ChromeHeight: the host paints a 30px bar for
    // captioned forms and none for chromeless ones; client coords start below it.
    private static int ChromeHeight(global::System.Windows.Forms.Form form)
    {
        try
        {
            if (form.IsDisposed || form.FormBorderStyle == global::System.Windows.Forms.FormBorderStyle.None)
            {
                return 0;
            }

            Type? type = form.GetType();
            while (type != null)
            {
                string name = type.Name;
                if (name.Contains("RadForm", StringComparison.Ordinal) || name.Contains("ShapedForm", StringComparison.Ordinal))
                {
                    return 0;
                }
                if (name == "Form")
                {
                    break;
                }
                type = type.BaseType;
            }

            int style = form.IsHandleCreated
                ? global::System.Windows.Forms.WasmWindowLongs.GetWindowStyle(form.Handle)
                : 0;
            if (style != 0 && (style & (int)global::Windows.Win32.UI.WindowsAndMessaging.WINDOW_STYLE.WS_CAPTION) != (int)global::Windows.Win32.UI.WindowsAndMessaging.WINDOW_STYLE.WS_CAPTION)
            {
                return 0;
            }
            return 30;
        }
        catch
        {
            return 0;
        }
    }
}
