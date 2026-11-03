using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WfWasm.CanvasHost;

// The canvas owns the non-client title bar, so Windows cannot provide the
// native move loop. Preserve its notifications: applications use WM_MOVING
// to update snapping/parking constraints before Location raises OnMove.
internal static class VirtualWindowDrag
{
    private static readonly MethodInfo WndProc = typeof(Control).GetMethod(
        "WndProc", BindingFlags.Instance | BindingFlags.NonPublic)!;

    internal static void Begin(Form form) => Send(form, 0x0231, IntPtr.Zero);
    internal static void End(Form form) => Send(form, 0x0232, IntPtr.Zero);

    internal static Point Move(Form form, Point desired)
    {
        IntPtr rect = Marshal.AllocHGlobal(16);
        try
        {
            Marshal.WriteInt32(rect, 0, desired.X);
            Marshal.WriteInt32(rect, 4, desired.Y);
            Marshal.WriteInt32(rect, 8, desired.X + form.Width);
            Marshal.WriteInt32(rect, 12, desired.Y + form.Height);
            Send(form, 0x0216, rect);
            return new Point(Marshal.ReadInt32(rect, 0), Marshal.ReadInt32(rect, 4));
        }
        finally { Marshal.FreeHGlobal(rect); }
    }

    private static void Send(Form form, int id, IntPtr data)
    {
        if (form.IsDisposed || !form.IsHandleCreated) return;
        object[] args = { Message.Create(form.Handle, id, IntPtr.Zero, data) };
        WndProc.Invoke(form, args);
    }
}
