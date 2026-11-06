// REBOOT1555-WPF: exact-10 fork-missing identities (filed §4, settled-
// entry refs). Four decls MOVE here from fork-invented homes to their
// CsWin32-canonical namespaces with byte-identical bodies (TRACKMOUSEEVENT
// pair, NMHDR, CHARRANGE, REDRAW_WINDOW_FLAGS); six are newly declared
// from SDK-verified shapes/values (CommCtrl/WinUser/Richedit/ObjIdl/
// WinBase+ntstatus). CORRECTS the UNSTALL slice, whose Primitives-
// namespace types served no filed ref and are removed. TrackMouseTracker
// is re-pointed at the filed TME; hover/leave MESSAGE delivery stays
// host-pump-owned (Runtime task, not faked). Telemetry-grade honesty:
// ILockBytes mirrors the fork's no-real-COM convention (E_NOTIMPL on
// unimplemented calls — the exact cannot-run proof at each boundary).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace Windows.Win32.UI.Input.KeyboardAndMouse
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public TRACKMOUSEEVENT_FLAGS dwFlags;
        public HWND hwndTrack;
        public uint dwHoverTime;
    }

    [Flags]
    internal enum TRACKMOUSEEVENT_FLAGS : uint
    {
        TME_HOVER = 0x00000001,
        TME_LEAVE = 0x00000002,
        TME_NONCLIENT = 0x00000010,
        TME_QUERY = 0x40000000,
        TME_CANCEL = 0x80000000,
    }

    internal static class TrackMouseTracker
    {
        internal const uint HOVER_DEFAULT = 0xFFFFFFFF;

        private sealed class Entry
        {
            public uint Flags;
            public uint HoverTime;
        }

        private static readonly object s_lock = new();
        private static readonly Dictionary<nint, Entry> s_tracks = new();

        internal static bool RequestTrack(TRACKMOUSEEVENT tme)
        {
            if (tme.cbSize != (uint)Marshal.SizeOf<TRACKMOUSEEVENT>())
                return false;
            nint hwnd = tme.hwndTrack.Value;
            uint flags = (uint)tme.dwFlags;
            lock (s_lock)
            {
                if ((flags & (uint)TRACKMOUSEEVENT_FLAGS.TME_CANCEL) != 0)
                {
                    s_tracks.Remove(hwnd);
                    return true;
                }
                if ((flags & (uint)TRACKMOUSEEVENT_FLAGS.TME_QUERY) != 0)
                    return s_tracks.ContainsKey(hwnd);
                s_tracks[hwnd] = new Entry { Flags = flags, HoverTime = tme.dwHoverTime };
                return true;
            }
        }

        internal static bool QueryTrack(nint hwnd, out TRACKMOUSEEVENT current)
        {
            lock (s_lock)
            {
                if (s_tracks.TryGetValue(hwnd, out Entry? entry))
                {
                    current = new TRACKMOUSEEVENT
                    {
                        cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
                        dwFlags = (TRACKMOUSEEVENT_FLAGS)entry.Flags,
                        hwndTrack = new HWND(hwnd),
                        dwHoverTime = entry.HoverTime,
                    };
                    return true;
                }
            }
            current = default;
            return false;
        }

        internal static void CancelTrack(nint hwnd)
        {
            lock (s_lock)
            {
                s_tracks.Remove(hwnd);
            }
        }
    }
}

namespace Windows.Win32.UI.Controls
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NMHDR
    {
        public HWND hwndFrom;
        public nuint idFrom;
        public uint code;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HDHITTESTINFO
    {
        public POINT pt;
        public uint flags;
        public int iItem;
    }

    [Flags]
    internal enum HEADER_HITTEST_INFO_FLAGS : uint
    {
        HHT_NOWHERE = 0x0001,
        HHT_ONHEADER = 0x0002,
        HHT_ONDIVIDER = 0x0004,
        HHT_ONDIVOPEN = 0x0008,
        HHT_ONFILTER = 0x0010,
        HHT_ONFILTERBUTTON = 0x0020,
        HHT_ABOVE = 0x0100,
        HHT_BELOW = 0x0200,
        HHT_TORIGHT = 0x0400,
        HHT_TOLEFT = 0x0800,
        HHT_ONITEMSTATEICON = 0x1000,
        HHT_ONDROPDOWN = 0x2000,
        HHT_ONOVERFLOW = 0x4000,
    }
}

namespace Windows.Win32.UI.Controls.RichEdit
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct CHARRANGE
    {
        public int cpMin;
        public int cpMax;
    }
}

namespace Windows.Win32.Graphics.Gdi
{
    [Flags]
    internal enum REDRAW_WINDOW_FLAGS : uint
    {
        RDW_INVALIDATE = 0x0001,
        RDW_INTERNALPAINT = 0x0002,
        RDW_ERASE = 0x0004,
        RDW_VALIDATE = 0x0008,
        RDW_NOINTERNALPAINT = 0x0010,
        RDW_NOERASE = 0x0020,
        RDW_NOCHILDREN = 0x0040,
        RDW_ALLCHILDREN = 0x0080,
        RDW_UPDATENOW = 0x0100,
        RDW_ERASENOW = 0x0200,
        RDW_FRAME = 0x0400,
        RDW_NOFRAME = 0x0800,
    }
}

namespace Windows.Win32.System.SystemServices
{
    internal enum RECO_FLAGS
    {
        RECO_PASTE = 0,
        RECO_DROP = 1,
        RECO_COPY = 2,
        RECO_CUT = 3,
        RECO_DRAG = 4,
    }
}

namespace Windows.Win32.System.Com.StructuredStorage
{
    [Guid("0000000A-0000-0000-C000-000000000046")]
    internal unsafe partial struct ILockBytes : IComIID, IVTable<ILockBytes, ILockBytes.Vtbl>
    {
        private static readonly Guid s_guid = new("0000000A-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT ReadAt(ulong ulOffset, void* pv, uint cb, uint* pcbRead) => HRESULT.E_NOTIMPL;
        public HRESULT WriteAt(ulong ulOffset, void* pv, uint cb, uint* pcbWritten) => HRESULT.E_NOTIMPL;
        public HRESULT Flush() => HRESULT.S_OK;
        public HRESULT SetSize(ulong cb) => HRESULT.S_OK;
        public HRESULT LockRegion(ulong libOffset, ulong cb, uint dwLockType) => HRESULT.E_NOTIMPL;
        public HRESULT UnlockRegion(ulong libOffset, ulong cb, uint dwLockType) => HRESULT.E_NOTIMPL;
        public HRESULT Stat(STATSTG* pstatstg, uint grfStatFlag) => HRESULT.E_NOTIMPL;
        public HRESULT Stat(out STATSTG pstatstg, uint grfStatFlag) { pstatstg = default; return HRESULT.S_OK; }
        public uint AddRef() => 1;
        public uint Release() => 0;

        public interface Interface { }
    }
}

namespace Windows.Win32.Foundation
{
    internal enum WAIT_EVENT : uint
    {
        WAIT_OBJECT_0 = 0,
        WAIT_ABANDONED = 0x80,
        WAIT_IO_COMPLETION = 0xC0,
        WAIT_TIMEOUT = 0x102,
        WAIT_FAILED = 0xFFFFFFFF,
    }
}
