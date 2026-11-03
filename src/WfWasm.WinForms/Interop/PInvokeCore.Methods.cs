// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Ole;
using Windows.Win32.System.Variant;
using Windows.Win32.System.WinRT;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Windows.Win32
{
    internal static partial class PInvokeCore
    {
        internal static HDC BeginPaint(HWND hWnd, out PAINTSTRUCT lpPaint)
        {
            lpPaint = default;
            return default;
        }

        internal static unsafe HDC BeginPaint(HWND hWnd, PAINTSTRUCT* lpPaint) => default;

        internal static BOOL EndPaint(HWND hWnd, in PAINTSTRUCT lpPaint) => (BOOL)true;
        internal static unsafe BOOL EndPaint(HWND hWnd, PAINTSTRUCT* lpPaint) => (BOOL)true;

        // No message queue exists in the browser: always report "no
        // message". This leaf must exist: the upstream generic
        // PeekMessage<T> wrapper forwards here, and the CsWin32-generated
        // PInvoke sibling is not compiled into the fork, so without it
        // the wrapper resolves back to itself and recurses to stack
        // overflow on the first modal message pump (New dialog).
        internal static unsafe BOOL PeekMessage(MSG* lpMsg, HWND hWnd, uint wMsgFilterMin, uint wMsgFilterMax, PEEK_MESSAGE_REMOVE_TYPE wRemoveMsg) => (BOOL)false;

        // No shell drag-drop exists in the browser: file-drop acceptance is
        // always off. This leaf must exist: the upstream generic
        // DragAcceptFiles<T> wrapper forwards here, and the removed
        // PInvokeCore.RegisterDragDrop source is not compiled into the
        // fork, so without it the wrapper resolves back to itself and
        // recurses to stack overflow on every RichTextBox handle creation.
        internal static void DragAcceptFiles(HWND hWnd, BOOL fAccept) { }

        internal static LRESULT DefWindowProc(HWND hWnd, uint Msg, WPARAM wParam, LPARAM lParam) => default;

        internal static HWND GetDesktopWindow() => default;
        internal static HWND GetForegroundWindow() => new HWND(global::System.Windows.Forms.WasmForeground.Current);
        internal static HWND GetParent(HWND hWnd)
        {
            if (hWnd.IsNull)
            {
                return HWND.Null;
            }

            try
            {
                var control = global::System.Windows.Forms.Control.FromHandle((nint)hWnd.Value);
                if (control is not null && !control.IsDisposed)
                {
                    var parent = control.Parent;
                    if (parent is not null && !parent.IsDisposed && parent.IsHandleCreated)
                    {
                        return (HWND)parent.Handle;
                    }
                }
            }
            catch
            {
            }

            return HWND.Null;
        }

        internal static nint GetParent(nint hWnd) => GetParent((HWND)hWnd).Value;

        internal static HWND GetAncestor(HWND hWnd, GET_ANCESTOR_FLAGS flags)
        {
            if (hWnd.IsNull)
            {
                return HWND.Null;
            }

            try
            {
                var control = global::System.Windows.Forms.Control.FromHandle((nint)hWnd.Value);
                if (control is null || control.IsDisposed)
                {
                    return HWND.Null;
                }

                switch (flags)
                {
                    case GET_ANCESTOR_FLAGS.GA_PARENT:
                        if (control.Parent is not null && !control.Parent.IsDisposed && control.Parent.IsHandleCreated)
                        {
                            return (HWND)control.Parent.Handle;
                        }
                        return HWND.Null;

                    case GET_ANCESTOR_FLAGS.GA_ROOT:
                    case GET_ANCESTOR_FLAGS.GA_ROOTOWNER:
                        global::System.Windows.Forms.Control current = control;
                        int depth = 0;
                        while (current.Parent is not null && !current.Parent.IsDisposed && depth < 50)
                        {
                            depth++;
                            current = current.Parent;
                        }
                        if (flags == GET_ANCESTOR_FLAGS.GA_ROOTOWNER && current is global::System.Windows.Forms.Form form && form.Owner is not null && !form.Owner.IsDisposed && form.Owner.IsHandleCreated)
                        {
                            return (HWND)form.Owner.Handle;
                        }
                        return current.IsHandleCreated ? (HWND)current.Handle : HWND.Null;

                    default:
                        return HWND.Null;
                }
            }
            catch
            {
            }

            return HWND.Null;
        }

        internal static nint GetAncestor(nint hWnd, GET_ANCESTOR_FLAGS flags) => GetAncestor((HWND)hWnd, flags).Value;

        internal static HWND GetWindow(HWND hWnd, GET_WINDOW_CMD uCmd)
        {
            if (hWnd.IsNull)
            {
                return HWND.Null;
            }

            try
            {
                var control = global::System.Windows.Forms.Control.FromHandle((nint)hWnd.Value);
                if (control is null || control.IsDisposed)
                {
                    return HWND.Null;
                }

                switch (uCmd)
                {
                    case GET_WINDOW_CMD.GW_CHILD:
                        if (control.Controls.Count > 0)
                        {
                            for (int i = 0; i < control.Controls.Count; i++)
                            {
                                var child = control.Controls[i];
                                if (child is not null && !child.IsDisposed && child.IsHandleCreated)
                                {
                                    return (HWND)child.Handle;
                                }
                            }
                        }
                        return HWND.Null;

                    case GET_WINDOW_CMD.GW_OWNER:
                        if (control is global::System.Windows.Forms.Form form && form.Owner is not null && !form.Owner.IsDisposed && form.Owner.IsHandleCreated)
                        {
                            return (HWND)form.Owner.Handle;
                        }
                        return HWND.Null;

                    case GET_WINDOW_CMD.GW_HWNDNEXT:
                        if (control.Parent is not null && !control.Parent.IsDisposed)
                        {
                            int idx = control.Parent.Controls.IndexOf(control);
                            if (idx >= 0 && idx + 1 < control.Parent.Controls.Count)
                            {
                                var next = control.Parent.Controls[idx + 1];
                                if (next is not null && !next.IsDisposed && next.IsHandleCreated)
                                {
                                    return (HWND)next.Handle;
                                }
                            }
                        }
                        return HWND.Null;

                    case GET_WINDOW_CMD.GW_HWNDPREV:
                        if (control.Parent is not null && !control.Parent.IsDisposed)
                        {
                            int idx = control.Parent.Controls.IndexOf(control);
                            if (idx > 0 && idx - 1 < control.Parent.Controls.Count)
                            {
                                var prev = control.Parent.Controls[idx - 1];
                                if (prev is not null && !prev.IsDisposed && prev.IsHandleCreated)
                                {
                                    return (HWND)prev.Handle;
                                }
                            }
                        }
                        return HWND.Null;

                    default:
                        return HWND.Null;
                }
            }
            catch
            {
            }

            return HWND.Null;
        }

        internal static nint GetWindow(nint hWnd, GET_WINDOW_CMD uCmd) => GetWindow((HWND)hWnd, uCmd).Value;

        internal static BOOL IsChild(HWND hWndParent, HWND hWnd)
        {
            if (hWndParent.IsNull || hWnd.IsNull || hWnd.Value == hWndParent.Value)
            {
                return (BOOL)false;
            }

            try
            {
                global::System.Windows.Forms.Control? c =
                    global::System.Windows.Forms.Control.FromHandle((nint)hWnd.Value);
                int depth = 0;
                while (c is not null && !c.IsDisposed && depth < 50)
                {
                    depth++;
                    c = c.Parent;
                    if (c is not null && c.IsHandleCreated && c.Handle == (nint)hWndParent.Value)
                    {
                        return (BOOL)true;
                    }
                }
            }
            catch
            {
            }

            return (BOOL)false;
        }

        internal static bool IsChild(nint hWndParent, nint hWnd) => (bool)IsChild((HWND)hWndParent, (HWND)hWnd);
        internal static uint GetCurrentThreadId() => 1u;
        internal static uint GetThreadLocale() => 0x0409u;
        internal static unsafe uint GetWindowThreadProcessId(HWND hWnd, uint* lpdwProcessId = null) => 1;
        internal static unsafe int MapWindowPoints(HWND hWndFrom, HWND hWndTo, Point* lpPoints, uint cPoints)
        {
            if (lpPoints == null || cPoints == 0)
            {
                return 0;
            }

            // Map both windows' client origins to scene space (see
            // WasmScreenMapping); null HWND is the screen itself.
            Point fromOrigin = WasmScreenMapping.ClientOrigin(hWndFrom);
            Point toOrigin = WasmScreenMapping.ClientOrigin(hWndTo);
            int dx = fromOrigin.X - toOrigin.X;
            int dy = fromOrigin.Y - toOrigin.Y;
            for (uint i = 0; i < cPoints; i++)
            {
                lpPoints[i].X += dx;
                lpPoints[i].Y += dy;
            }

            return unchecked((int)(((uint)(ushort)dy << 16) | (uint)(ushort)dx));
        }

        internal static unsafe int MapWindowPoints(nint hWndFrom, nint hWndTo, Point* lpPoints, uint cPoints)
            => MapWindowPoints((HWND)hWndFrom, (HWND)hWndTo, lpPoints, cPoints);

        internal static unsafe int MapWindowPoints(HWND hWndFrom, HWND hWndTo, ref Point lpPoint)
        {
            fixed (Point* p = &lpPoint)
            {
                return MapWindowPoints(hWndFrom, hWndTo, p, 1);
            }
        }

        internal static unsafe int MapWindowPoints(nint hWndFrom, nint hWndTo, ref Point lpPoint)
            => MapWindowPoints((HWND)hWndFrom, (HWND)hWndTo, ref lpPoint);

        internal static unsafe int MapWindowPoints(HWND hWndFrom, HWND hWndTo, ref RECT lpRect)
        {
            fixed (RECT* pRect = &lpRect)
            {
                return MapWindowPoints(hWndFrom, hWndTo, (Point*)pRect, 2);
            }
        }

        internal static unsafe int MapWindowPoints(nint hWndFrom, nint hWndTo, ref RECT lpRect)
            => MapWindowPoints((HWND)hWndFrom, (HWND)hWndTo, ref lpRect);

        internal static BOOL ClientToScreen(HWND hWnd, ref Point lpPoint)
        {
            Point origin = WasmScreenMapping.ClientOrigin(hWnd);
            lpPoint.X += origin.X;
            lpPoint.Y += origin.Y;
            return (BOOL)true;
        }

        internal static bool ClientToScreen(nint hWnd, ref Point lpPoint) => (bool)ClientToScreen((HWND)hWnd, ref lpPoint);

        internal static unsafe BOOL ClientToScreen(HWND hWnd, Point* lpPoint)
        {
            if (lpPoint == null)
            {
                return (BOOL)false;
            }
            Point origin = WasmScreenMapping.ClientOrigin(hWnd);
            lpPoint->X += origin.X;
            lpPoint->Y += origin.Y;
            return (BOOL)true;
        }

        internal static unsafe bool ClientToScreen(nint hWnd, Point* lpPoint) => (bool)ClientToScreen((HWND)hWnd, lpPoint);

        internal static BOOL ScreenToClient(HWND hWnd, ref Point lpPoint)
        {
            Point origin = WasmScreenMapping.ClientOrigin(hWnd);
            lpPoint.X -= origin.X;
            lpPoint.Y -= origin.Y;
            return (BOOL)true;
        }

        internal static bool ScreenToClient(nint hWnd, ref Point lpPoint) => (bool)ScreenToClient((HWND)hWnd, ref lpPoint);

        internal static unsafe BOOL ScreenToClient(HWND hWnd, Point* lpPoint)
        {
            if (lpPoint == null)
            {
                return (BOOL)false;
            }
            Point origin = WasmScreenMapping.ClientOrigin(hWnd);
            lpPoint->X -= origin.X;
            lpPoint->Y -= origin.Y;
            return (BOOL)true;
        }

        internal static unsafe bool ScreenToClient(nint hWnd, Point* lpPoint) => (bool)ScreenToClient((HWND)hWnd, lpPoint);

        internal static HDC GetDC(HWND hWnd) => (HDC)(nint)1;
        internal static HDC GetDCEx(HWND hWnd, HRGN hrgnClip, GET_DCX_FLAGS flags) => (HDC)(nint)1;
        internal static HDC GetWindowDC(HWND hWnd) => (HDC)(nint)1;
        internal static int ReleaseDC(HWND hWnd, HDC hDC) => 1;

        internal static int GetDeviceCaps(HDC hdc, GET_DEVICE_CAPS_INDEX index) => index switch
        {
            GET_DEVICE_CAPS_INDEX.LOGPIXELSX => 96,
            GET_DEVICE_CAPS_INDEX.LOGPIXELSY => 96,
            GET_DEVICE_CAPS_INDEX.BITSPIXEL => 32,
            GET_DEVICE_CAPS_INDEX.PLANES => 1,
            _ => 0
        };

        internal static int GetSystemMetrics(SYSTEM_METRICS_INDEX nIndex) => nIndex switch
        {
            SYSTEM_METRICS_INDEX.SM_CXSCREEN => 1920,
            SYSTEM_METRICS_INDEX.SM_CYSCREEN => 1080,
            SYSTEM_METRICS_INDEX.SM_CXFULLSCREEN => 1920,
            SYSTEM_METRICS_INDEX.SM_CYFULLSCREEN => 1040,
            SYSTEM_METRICS_INDEX.SM_CXMAXTRACK => 10000,
            SYSTEM_METRICS_INDEX.SM_CYMAXTRACK => 10000,
            SYSTEM_METRICS_INDEX.SM_CXMAXIMIZED => 1920,
            SYSTEM_METRICS_INDEX.SM_CYMAXIMIZED => 1080,
            SYSTEM_METRICS_INDEX.SM_CXMINTRACK => 100,
            SYSTEM_METRICS_INDEX.SM_CYMINTRACK => 50,
            SYSTEM_METRICS_INDEX.SM_CXMIN => 100,
            SYSTEM_METRICS_INDEX.SM_CYMIN => 50,
            SYSTEM_METRICS_INDEX.SM_CXBORDER => 1,
            SYSTEM_METRICS_INDEX.SM_CYBORDER => 1,
            SYSTEM_METRICS_INDEX.SM_CXICON => 32,
            SYSTEM_METRICS_INDEX.SM_CYICON => 32,
            SYSTEM_METRICS_INDEX.SM_CXSMICON => 16,
            SYSTEM_METRICS_INDEX.SM_CYSMICON => 16,
            SYSTEM_METRICS_INDEX.SM_CXEDGE => 2,
            SYSTEM_METRICS_INDEX.SM_CYEDGE => 2,
            SYSTEM_METRICS_INDEX.SM_CYCAPTION => 23,
            SYSTEM_METRICS_INDEX.SM_CYMENU => 20,
            SYSTEM_METRICS_INDEX.SM_CXDRAG => 4,
            SYSTEM_METRICS_INDEX.SM_CYDRAG => 4,
            SYSTEM_METRICS_INDEX.SM_MOUSEPRESENT => 1,
            SYSTEM_METRICS_INDEX.SM_MOUSEWHEELPRESENT => 1,
            SYSTEM_METRICS_INDEX.SM_CMOUSEBUTTONS => 3,
            SYSTEM_METRICS_INDEX.SM_CMONITORS => 1,
            _ => 0
        };
        internal static uint GetSysColor(SYS_COLOR_INDEX nIndex) => 0;
        internal static HGDIOBJ GetStockObject(GET_STOCK_OBJECT_FLAGS i) => (HGDIOBJ)(nint)1;
        internal static OBJ_TYPE GetObjectType(HGDIOBJ h) => OBJ_TYPE.OBJ_MEMDC;

        internal static unsafe HRESULT CoCreateInstance<T>(in Guid rclsid, IUnknown* pUnkOuter, CLSCTX dwClsContext, out T* ppv) where T : unmanaged
        {
            ppv = null;
            return HRESULT.E_NOTIMPL;
        }

        internal static unsafe HRESULT CoCreateInstance<T>(Guid rclsid, IUnknown* pUnkOuter, CLSCTX dwClsContext, out T* ppv) where T : unmanaged
        {
            ppv = null;
            return HRESULT.E_NOTIMPL;
        }

        internal static BOOL DrawIconEx(HDC hdc, int xLeft, int yTop, HICON hIcon, int cxWidth, int cyWidth, uint istepIfAniCur, HBRUSH hbrFlickerFreeDraw, DI_FLAGS diFlags) => (BOOL)true;
        internal static unsafe BOOL EnumChildWindows(HWND hWndParent, delegate* unmanaged[Stdcall]<HWND, LPARAM, BOOL> lpEnumFunc, LPARAM lParam) => (BOOL)true;
        internal static unsafe BOOL EnumWindows(delegate* unmanaged[Stdcall]<HWND, LPARAM, BOOL> lpEnumFunc, LPARAM lParam) => (BOOL)true;
        internal static unsafe BOOL EnumDisplayMonitors(HDC hdc, RECT* lprcClip, delegate* unmanaged[Stdcall]<HMONITOR, HDC, RECT*, LPARAM, BOOL> lpfnEnum, LPARAM dwData) => (BOOL)true;
        internal static BOOL GetIconInfo(HICON hIcon, out ICONINFO piconinfo) { piconinfo = default; return (BOOL)true; }
        internal static unsafe BOOL GetIconInfo(HICON hIcon, ICONINFO* piconinfo) => (BOOL)true;
        internal static unsafe int GetObject(HGDIOBJ hgdiobj, int cbBuffer, void* lpvObject)
        {
            // Depth-carrying imagelist bitmap tokens (see
            // ImageList_GetImageInfo): report the stored depth in
            // BITMAP.bmBitsPixel so ImageList resolves its ColorDepth
            // instead of keeping the 32bpp default (which would route
            // GetBitmap into the FromHbitmap path that cannot exist
            // headless). Everything else keeps the success no-op.
            nint h = hgdiobj;
            if (lpvObject != null && cbBuffer >= 20 && (h & 0xFE000000) == 0x60000000)
            {
                ushort depth = (ushort)((h >> 16) & 0xFF);
                if (depth is 1 or 4 or 8 or 16 or 24 or 32)
                    *(ushort*)((byte*)lpvObject + 18) = depth;
            }

            return cbBuffer;
        }
        internal static unsafe uint GetPaletteEntries(HPALETTE hpal, uint iStart, uint cEntries, PALETTEENTRY* pEntries) => cEntries;
        internal static unsafe int GetWindowText(HWND hWnd, PWSTR lpString, int nMaxCount) => WindowTextStorage.CopyTo(hWnd, lpString, nMaxCount);
        private static unsafe void FillLogFont(global::Windows.Win32.Graphics.Gdi.LOGFONTW* plf, int weight)
        {
            plf->lfHeight = -12;
            plf->lfWeight = weight;
            global::System.Span<char> name = plf->lfFaceName.AsSpan();
            global::System.ReadOnlySpan<char> face = "Segoe UI";
            face.CopyTo(name);
            name[face.Length] = '\0';
        }

        private static unsafe void FillNonClientMetrics(NONCLIENTMETRICSW* pncm)
        {
            if (pncm == null) return;
            pncm->cbSize = (uint)global::System.Runtime.InteropServices.Marshal.SizeOf<NONCLIENTMETRICSW>();
            pncm->iBorderWidth = 1;
            pncm->iScrollWidth = 17;
            pncm->iScrollHeight = 17;
            pncm->iCaptionWidth = 23;
            pncm->iCaptionHeight = 23;
            FillLogFont(&pncm->lfCaptionFont, 700);
            pncm->iSmCaptionWidth = 17;
            pncm->iSmCaptionHeight = 17;
            FillLogFont(&pncm->lfSmCaptionFont, 400);
            pncm->iMenuWidth = 19;
            pncm->iMenuHeight = 19;
            FillLogFont(&pncm->lfMenuFont, 400);
            FillLogFont(&pncm->lfStatusFont, 400);
            FillLogFont(&pncm->lfMessageFont, 400);
            pncm->iPaddedBorderWidth = 0;
        }

        internal static unsafe BOOL SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION uiAction, uint uiParam, void* pvParam, SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS fWinIni)
        {
            // No system parameters in the browser: answer desktop defaults
            // for the scalar int queries we know (hover metrics feed
            // ToolStrip's MouseHoverTimer; Interval=0 throws). Struct/bool
            // queries are left untouched, exactly as before.
            if (pvParam != null)
            {
                switch (uiAction)
                {
                    case SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETWORKAREA:
                        // Desktop parity: the primary-monitor work rect the
                        // fork reports everywhere else (GetMonitorInfo.rcWork
                        // + SM_CXFULLSCREEN/CYFULLSCREEN). Zero here made
                        // every WorkingArea consumer clamp to the origin:
                        // Syncfusion MenuGridHost.GetAdjustedLocation parked
                        // the File popup at Y=0 (PreferredHeight > 0 always
                        // wins against an empty area).
                        *(RECT*)pvParam = new RECT(0, 0, 1920, 1040);
                        break;
                    case SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETNONCLIENTMETRICS:
                        // Desktop parity: Segoe UI 9pt (lfHeight -12) system
                        // faces. Zeroed metrics made SystemInformation.MenuFont
                        // come back with an empty name at 0.75px, which the
                        // Syncfusion bars then measured every menu against
                        // (host font-heal masked it). Matches the fork's
                        // Drawing SystemFonts (Segoe UI 9pt) and the virtual
                        // 1920x1080 screen metrics above.
                        FillNonClientMetrics((NONCLIENTMETRICSW*)pvParam);
                        break;
                    case SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETMOUSEHOVERTIME:
                        *(int*)pvParam = 400;
                        break;
                    case SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETMOUSEHOVERHEIGHT:
                    case SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETMOUSEHOVERWIDTH:
                        *(int*)pvParam = 4;
                        break;
                    case SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETWHEELSCROLLLINES:
                    case SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETWHEELSCROLLCHARS:
                        // Desktop default is 3 lines/chars per notch. Zero here
                        // made SystemInformation.MouseWheelScrollLines read 0,
                        // which silently disables every wheel-scroll consumer
                        // (host ListView scroll multiplies by it).
                        *(int*)pvParam = 3;
                        break;
                }
            }

            return (BOOL)true;
        }

        internal static unsafe uint GetRegionData(HRGN hrgn, uint nCount, RGNDATA* lpRgnData) => 0;

        internal static HDC CreateCompatibleDC(HDC hdc) => (HDC)1;
        internal static BOOL DeleteDC(HDC hdc) => (BOOL)true;
        internal static BOOL DeleteObject(HGDIOBJ ho) { PInvoke.WasmFillBridge.ForgetBrush(ho); global::System.Drawing.BitmapHandleMap.Release((nint)ho.Value); return (BOOL)true; }
        internal static BOOL DestroyIcon(HICON hIcon) => (BOOL)true;

        internal static unsafe HBITMAP CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, void* lpBits) => (HBITMAP)(nint)1;
        internal static HBITMAP CreateCompatibleBitmap(HDC hdc, int cx, int cy) => (HBITMAP)(nint)1;

        internal static unsafe HBITMAP CreateDIBSection(HDC hdc, in BITMAPINFO pbmi, DIB_USAGE usage, void** ppvBits, HANDLE hSection, uint offset) => (HBITMAP)(nint)1;
        internal static unsafe HBITMAP CreateDIBSection(HDC hdc, BITMAPINFO* pbmi, DIB_USAGE usage, void** ppvBits, HANDLE hSection, uint offset) => (HBITMAP)(nint)1;
        internal static unsafe HBITMAP CreateDIBSection(HDC hdc, void* pbmi, DIB_USAGE usage, void** ppvBits, HANDLE hSection, uint offset) => (HBITMAP)(nint)1;

        internal static unsafe HFONT CreateFontIndirect(LOGFONTW* lplf)
            => lplf is null ? (HFONT)(nint)1 : PInvoke.WasmTextBridge.CreateFont(*lplf);
        internal static HFONT CreateFontIndirect(in LOGFONTW lplf) => PInvoke.WasmTextBridge.CreateFont(in lplf);

        internal static HPEN CreatePen(PEN_STYLE iStyle, int cWidth, COLORREF color) => (HPEN)(nint)1;
        internal static HBRUSH CreateSolidBrush(COLORREF color) => PInvoke.WasmFillBridge.NewBrushHandle(color);
        internal static HRGN CreateRectRgn(int x1, int y1, int x2, int y2) => (HRGN)(nint)1;
        internal static GDI_REGION_TYPE CombineRgn(HRGN hrgnDst, HRGN hrgnSrc1, HRGN hrgnSrc2, RGN_COMBINE_MODE iMode) => GDI_REGION_TYPE.SIMPLEREGION;

        internal static unsafe HDC CreateDCW(PCWSTR pwszDriver, PCWSTR pwszDevice, PCWSTR pszPort, DEVMODEW* pdm) => (HDC)(nint)1;
        internal static unsafe HDC CreateDCW(char* pwszDriver, char* pwszDevice, char* pszPort, DEVMODEW* pdm) => (HDC)(nint)1;
        internal static unsafe HDC CreateDCW(string? pwszDriver, string? pwszDevice, string? pszPort, DEVMODEW* pdm) => (HDC)(nint)1;
        internal static unsafe HDC CreateICW(PCWSTR pwszDriver, PCWSTR pwszDevice, PCWSTR pszPort, DEVMODEW* pdm) => (HDC)(nint)1;
        internal static unsafe HDC CreateICW(char* pwszDriver, char* pwszDevice, char* pszPort, DEVMODEW* pdm) => (HDC)(nint)1;

        internal static HANDLE CopyImage(HANDLE h, GDI_IMAGE_TYPE type, int cx, int cy, IMAGE_FLAGS flags) => h;

        internal static HGDIOBJ SelectObject(HDC hdc, HGDIOBJ h) => PInvoke.WasmTextBridge.SelectObject(hdc, h);
        internal static GDI_REGION_TYPE SelectClipRgn(HDC hdc, HRGN hrgn) => GDI_REGION_TYPE.SIMPLEREGION;
        internal static HPALETTE SelectPalette(HDC hdc, HPALETTE hPal, BOOL bForceBkgd) => default;
        internal static uint RealizePalette(HDC hdc) => 0;

        internal static int SaveDC(HDC hdc) => 1;
        internal static BOOL RestoreDC(HDC hdc, int nSavedDC) => (BOOL)true;

        internal static COLORREF SetBkColor(HDC hdc, COLORREF color) => PInvoke.WasmTextBridge.SetBkColor(hdc, color);
        internal static BACKGROUND_MODE SetBkMode(HDC hdc, BACKGROUND_MODE mode) => PInvoke.WasmTextBridge.SetBkMode(hdc, mode);
        internal static COLORREF SetTextColor(HDC hdc, COLORREF color) => PInvoke.WasmTextBridge.SetTextColor(hdc, color);
        internal static HDC_MAP_MODE SetMapMode(HDC hdc, HDC_MAP_MODE iMode) => iMode;
        internal static R2_MODE SetROP2(HDC hdc, R2_MODE rop2) => rop2;
        internal static TEXT_ALIGN_OPTIONS SetTextAlign(HDC hdc, TEXT_ALIGN_OPTIONS align) => PInvoke.WasmTextBridge.SetTextAlign(hdc, align);

        internal static int GetClipRgn(HDC hdc, HRGN hrgn) => 0;

        internal static unsafe BOOL GetViewportOrgEx(HDC hdc, Point* lppt) => (BOOL)true;
        internal static BOOL GetViewportOrgEx(HDC hdc, out Point lppt) { lppt = Point.Empty; return (BOOL)true; }

        internal static unsafe BOOL GetWindowOrgEx(HDC hdc, Point* lppt) => (BOOL)true;
        internal static BOOL GetWindowOrgEx(HDC hdc, out Point lppt) { lppt = Point.Empty; return (BOOL)true; }

        internal static unsafe BOOL OffsetViewportOrgEx(HDC hdc, int x, int y, Point* lppt) => (BOOL)true;

        internal static unsafe BOOL GetWorldTransform(HDC hdc, XFORM* lpxf)
        {
            if (lpxf != null)
            {
                *lpxf = new XFORM { eM11 = 1.0f, eM22 = 1.0f };
            }
            return (BOOL)true;
        }

        internal static BOOL GetWorldTransform(HDC hdc, out XFORM lpxf)
        {
            lpxf = new XFORM { eM11 = 1.0f, eM22 = 1.0f };
            return (BOOL)true;
        }

        internal static BOOL Rectangle(HDC hdc, int left, int top, int right, int bottom) => (BOOL)true;

        internal static unsafe void* GlobalLock(HGLOBAL hMem) => (void*)hMem.Value;
        internal static BOOL GlobalUnlock(HGLOBAL hMem) => (BOOL)false;
        internal static nuint GlobalSize(HGLOBAL hMem) => 0;
        internal static HGLOBAL GlobalReAlloc(HGLOBAL hMem, nuint dwBytes, uint uFlags) => hMem;

        internal static HMONITOR MonitorFromPoint(Point pt, MONITOR_FROM_FLAGS dwFlags) => (HMONITOR)(nint)1;
        internal static unsafe HMONITOR MonitorFromRect(RECT* lprc, MONITOR_FROM_FLAGS dwFlags) => (HMONITOR)(nint)1;
        internal static HMONITOR MonitorFromRect(in RECT lprc, MONITOR_FROM_FLAGS dwFlags) => (HMONITOR)(nint)1;
        internal static HMONITOR MonitorFromWindow(HWND hwnd, MONITOR_FROM_FLAGS dwFlags) => (HMONITOR)(nint)1;

        internal static unsafe BOOL GetMonitorInfo(HMONITOR hMonitor, MONITORINFO* lpmi)
        {
            if (lpmi != null)
            {
                lpmi->cbSize = (uint)sizeof(MONITORINFO);
                lpmi->rcMonitor = new RECT(0, 0, 1920, 1080);
                lpmi->rcWork = new RECT(0, 0, 1920, 1040);
                lpmi->dwFlags = 1;
            }
            return (BOOL)true;
        }

        internal static BOOL GetMonitorInfo(HMONITOR hMonitor, ref MONITORINFO lpmi)
        {
            lpmi.cbSize = (uint)global::System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>();
            lpmi.rcMonitor = new RECT(0, 0, 1920, 1080);
            lpmi.rcWork = new RECT(0, 0, 1920, 1040);
            lpmi.dwFlags = 1;
            return (BOOL)true;
        }

        internal static uint RegisterClipboardFormat(PCWSTR lpszFormat) => 0xC000u;
        internal static uint RegisterClipboardFormat(string lpszFormat) => 0xC000u;

        internal static unsafe int GetClipboardFormatName(uint format, PWSTR lpszFormatName, int cchMaxCount) => 0;
        internal static unsafe int GetClipboardFormatName(uint format, char* lpszFormatName, int cchMaxCount) => 0;

        internal static unsafe HRESULT OleInitialize(void* pvReserved) => HRESULT.S_OK;
        internal static void OleUninitialize() { }

        internal static unsafe HRESULT OleGetClipboard(IDataObject** ppDataObj) => HRESULT.E_FAIL;
        internal static unsafe HRESULT OleGetClipboard(void* ppDataObj) => HRESULT.E_FAIL;

        internal static unsafe HRESULT OleSetClipboard(IDataObject* pDataObj) => HRESULT.S_OK;
        internal static unsafe HRESULT OleSetClipboard(void* pDataObj) => HRESULT.S_OK;

        internal static HRESULT OleFlushClipboard() => HRESULT.S_OK;
        internal static HANDLE OleDuplicateData(HANDLE hSrc, CLIPBOARD_FORMAT cfFormat, global::Windows.Win32.System.Memory.GLOBAL_ALLOC_FLAGS uiFlags) => hSrc;

        internal static unsafe void ReleaseStgMedium(STGMEDIUM* pmedium) { }
        internal static void ReleaseStgMedium(ref STGMEDIUM pmedium) { }

        internal static unsafe HRESULT OleCreatePictureIndirect(PICTDESC* lpPictDesc, Guid* riid, BOOL fOwn, void** lplpObj) => HRESULT.E_NOTIMPL;
        internal static unsafe HRESULT OleCreatePictureIndirect(PICTDESC* lpPictDesc, in Guid riid, BOOL fOwn, void* lplpObj) => HRESULT.E_NOTIMPL;
        internal static unsafe HRESULT OleCreatePictureIndirect(PICTDESC* lpPictDesc, in Guid riid, BOOL fOwn, out object? lplpObj)
        {
            lplpObj = null;
            return HRESULT.E_NOTIMPL;
        }

        internal static unsafe HRESULT DoDragDrop(IDataObject* pDataObj, IDropSource* pDropSource, DROPEFFECT dwOKEffects, DROPEFFECT* pdwEffect)
        {
            if (pdwEffect != null) *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
            return HRESULT.DRAGDROP_S_CANCEL;
        }

        internal static unsafe HRESULT DoDragDrop(void* pDataObj, void* pDropSource, DROPEFFECT dwOKEffects, out DROPEFFECT pdwEffect)
        {
            pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
            return HRESULT.DRAGDROP_S_CANCEL;
        }

        internal static unsafe HRESULT DoDragDrop(void* pDataObj, void* pDropSource, uint dwOKEffects, out uint pdwEffect)
        {
            pdwEffect = 0;
            return HRESULT.DRAGDROP_S_CANCEL;
        }

        internal static unsafe uint DragQueryFile(HDROP hDrop, uint iFile, PWSTR lpszFile, uint cch) => 0;

        internal static BOOL BitBlt(HDC hdc, int x, int y, int cx, int cy, HDC hdcSrc, int x1, int y1, ROP_CODE rop) => (BOOL)true;
        internal static GDI_REGION_TYPE IntersectClipRect(HDC hdc, int left, int top, int right, int bottom) => GDI_REGION_TYPE.SIMPLEREGION;
        // No native combo box exists headless, so the framework's
        // CB_GETCURSEL / CB_SETCURSEL / CB_RESETCONTENT traffic is answered
        // from managed state. Desktop returns CB_ERR (-1) when nothing is
        // selected; the old fall-through returned 0, which made
        // ComboBox.SelectedIndex lie (0) on handle-created but unselected
        // combos and threw (Items[0] of an empty list) out of
        // Control.OnHandleDestroyed's text stash on every destroy.
        // Keyed by control (not HWND) so entries die with the control;
        // creation/restore traffic (OnHandleCreated re-sends CB_SETCURSEL,
        // Clear sends CB_RESETCONTENT) keeps the mirror in sync.
        // No native tooltip exists headless, so the framework's
        // TTM_GETDELAYTIME / TTM_SETDELAYTIME traffic is answered from
        // managed state. Desktop defaults are initial 500ms, reshow 100ms,
        // autopop 5000ms (the managed _delayTimes mirror the same numbers);
        // the old fall-through returned 0, which threw (timer interval 0ms)
        // out of KeyboardToolTipStateMachine on every focus change.
        // Keyed by window (not HWND) so entries die with the window;
        // handle recreation re-sends the non-default delays.
        private sealed class TipDelays { public int Reshow = 100; public int AutoPop = 5000; public int Initial = 500; }
        private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Windows.Forms.NativeWindow, TipDelays> s_tipDelays = new();
        private static readonly object s_tipDelaysLock = new();
        private sealed class ComboSelection { public int Index = -1; }
        private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Windows.Forms.Control, ComboSelection> s_comboSelection = new();
        private static readonly object s_comboSelectionLock = new();
        // No native combo box exists headless, so CB_SHOWDROPDOWN opens
        // nothing and CB_GETDROPPEDSTATE fell through to default (0):
        // DroppedDown read back false even right after being set true.
        // The mirror restores desktop parity (set state reads back until
        // closed); keyed by control so entries die with the control.
        private sealed class ComboDropped { public bool Open; }
        private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Windows.Forms.Control, ComboDropped> s_comboDropped = new();
        private static readonly object s_comboDroppedLock = new();
        // No native list box exists headless, so the framework's
        // LB_GETCURSEL / LB_SETCURSEL / LB_SETSEL / LB_GETSEL / LB_GETSELCOUNT /
        // LB_GETSELITEMS / LB_RESETCONTENT / LB_GETTEXT / LB_GETTEXTLEN /
        // LB_INSERTSTRING / LB_DELETESTRING traffic is answered from managed
        // state. Desktop returns LB_ERR (-1) when nothing is selected; the old
        // fall-through returned 0, which made SelectedIndex lie (0) on
        // handle-created but unselected boxes: SelectedItems.Count read 1 while
        // the managed ItemArray held zero Selected bits, and the indexer threw
        // (IndexOutOfRangeException) out of Control.OnHandleDestroyed's text
        // stash on every destroy. Keyed by control (not HWND) so entries die
        // with the control; selection set traffic (LB_SETCURSEL/LB_SETSEL),
        // Clear (LB_RESETCONTENT) and handle-recreate restore (which re-sends
        // the same selection traffic) keep the mirror in sync; insert/delete
        // shift tracked indices so they keep pointing at the same items.
        // Text answers come live from the managed Items via GetItemText, so
        // they never go stale.
        private sealed class ListBoxSelection
        {
            public int Cursel = -1;
            public readonly global::System.Collections.Generic.HashSet<int> Selected = new();
        }

        private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Windows.Forms.Control, ListBoxSelection> s_listBoxSelection = new();
        private static readonly object s_listBoxSelectionLock = new();

        private static void ShiftListBoxSelection(ListBoxSelection state, int from, int delta)
        {
            if (state.Cursel >= from)
            {
                state.Cursel += delta;
            }

            if (state.Selected.Count == 0)
            {
                return;
            }

            global::System.Collections.Generic.List<int> moved = new(state.Selected.Count);
            foreach (int i in state.Selected)
            {
                moved.Add(i >= from ? i + delta : i);
            }

            state.Selected.Clear();
            foreach (int i in moved)
            {
                if (i >= 0)
                {
                    state.Selected.Add(i);
                }
            }
        }
        // No native edit control exists headless, so the framework's
        // EM_SETSEL / EM_GETSEL / EM_REPLACESEL traffic is answered
        // from managed state. Without this every handle-created
        // TextBoxBase loses its selection: SelectInternal sends
        // EM_SETSEL into the fall-through (default), GetSelectionStart-
        // AndLength reads EM_GETSEL zeros, and SetSelectedTextInternal
        // drops EM_REPLACESEL replacements. Visible effects: host caret
        // tracking never sticks (typed chars insert at 0), Ctrl+A
        // SelectAll is invisible to KeyDown handlers, and SelectedText
        // assignment silently does nothing. Desktop parity: 0/-1
        // selects all, negative start deselects (collapse to caret),
        // positions clamp to the text length, and EM_GETSEL reports
        // through its out pointers. Keyed by control (not HWND) so
        // entries die with the control, like the combo mirror above.
        private sealed class EditSelection { public int Start; public int End; }
        private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Windows.Forms.Control, EditSelection> s_editSelection = new();
        private static readonly object s_editSelectionLock = new();
        // No native list view exists headless, so the framework's
        // LVM_SETITEMSTATE / LVM_GETITEMSTATE / LVM_GETSELECTEDCOUNT / LVM_FINDITEMW /
        // LVM_GETNEXTITEM / LVM_SETSELECTIONMARK traffic is answered from
        // managed state. Without this every handle-created ListView keeps
        // no selection: row clicks toggle ListViewItem.Selected into the
        // fall-through, SelectedItems/SelectedIndices read back empty, and
        // Done-style handlers iterate nothing (silent, zero errors).
        // Desktop parity: index -1 addresses all items, state merges under
        // the mask, GETNEXTITEM scans forward from start+1 (-1 starts at
        // 0), counts and marks follow the mirror. Keyed by control (not
        // HWND) so entries die with the control, like the edit/combo
        // mirrors above. Only the selected/focused bits are tracked.
        private sealed class ListViewSelection
        {
            public HashSet<int> Selected = new();
            public HashSet<int> Focused = new();
            public int Mark = -1;
            // Display order (display index -> item id), fed by LVM_INSERTITEMW /
            // LVM_DELETEITEM / LVM_DELETEALLITEMS traffic. The native control owns order
            // on desktop; headless there is no control, so the mirror owns it. Sorting and
            // grouping are not emulated (insertion order is kept).
            public List<int> Order = new();
        }
        private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Windows.Forms.Control, ListViewSelection> s_listViewSelection = new();
        private static readonly object s_listViewSelectionLock = new();

        // No native header control exists headless, so LVM_GETHEADER answers
        // a stable per-ListView fake handle and HDM_GETITEMCOUNT answers the
        // managed column count. This keeps the ColumnHeader.Width read path
        // live: the upstream getter takes the native branch only when the
        // header is non-null and the index is below the native count.
        // Audited GETHEADER consumers: Width getter (desired path);
        // AnnounceColumnHeader (unreachable: UiaClientsAreListening is false
        // headless, plus the IsAccessibilityObjectCreated guard);
        // InvalidateColumnHeaders (stub answers TRUE, harmless);
        // dark-mode SetWindowTheme (stub S_OK); GetListHeaderFont
        // (owner-draw only; WM_GETFONT still falls through to zero, as
        // before, so no behavior change there).
        private sealed class ListViewColumnState
        {
            public List<int> Widths = new();
            public int FakeHeader;
        }
        private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Windows.Forms.Control, ListViewColumnState> s_listViewColumns = new();
        private static readonly Dictionary<int, WeakReference<global::System.Windows.Forms.ListView>> s_headerLists = new();
        private static readonly object s_headerListsLock = new();
        private static int s_nextHeaderId = 0x10000;
        // Headless approximation of native cell padding applied after a
        // measured autosize (desktop adds image/padding around text).
        private const int ListViewColumnPad = 12;

        private static ListViewColumnState GetColumnState(global::System.Windows.Forms.ListView list)
        {
            ListViewColumnState state = s_listViewColumns.GetOrCreateValue(list);
            int count;
            try { count = list.Columns.Count; }
            catch { count = 0; }
            if (state.Widths.Count != count)
            {
                // Insert/remove changes the count; preserve surviving
                // entries by index, seed new slots from managed widths.
                var next = new List<int>(count);
                for (int i = 0; i < count; i++)
                {
                    int carried = i < state.Widths.Count ? state.Widths[i] : -1;
                    if (carried < 0)
                    {
                        try { carried = list.Columns[i].WidthInternal; }
                        catch { carried = 60; }
                    }
                    next.Add(carried);
                }
                state.Widths = next;
            }
            if (state.FakeHeader == 0)
            {
                state.FakeHeader = Interlocked.Increment(ref s_nextHeaderId);
                lock (s_headerListsLock)
                    s_headerLists[state.FakeHeader] = new WeakReference<global::System.Windows.Forms.ListView>(list);
            }
            return state;
        }

        private static HashSet<int> RemapIndexSetById(HashSet<int> set, List<int> oldOrder, Dictionary<int, int> newIndex)
        {
            HashSet<int> next = new();
            foreach (int i in set)
            {
                if (i >= 0 && i < oldOrder.Count && newIndex.TryGetValue(oldOrder[i], out int mapped))
                    next.Add(mapped);
            }
            return next;
        }

        // Documented headless row geometry shared by hit testing and item
        // rects: row = Font.Height + 2, Details header = Font.Height + 8
        // when headers show. Matches the regression tests' formulas.
        private static bool ListRowMetrics(global::System.Windows.Forms.ListView list, out int rowH, out int top)
        {
            rowH = 0;
            top = 0;
            try
            {
                rowH = Math.Max(1, list.Font.Height + 2);
                if (list.View == global::System.Windows.Forms.View.Details
                    && list.HeaderStyle != global::System.Windows.Forms.ColumnHeaderStyle.None)
                    top = list.Font.Height + 8;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int HitTestListRow(global::System.Windows.Forms.ListView list, int x, int y, bool subItems, out int sub, out uint flags)
        {
            // LVHT_* values from commctrl.h; only NOWHERE / ONITEMLABEL used.
            const uint LVHT_NOWHERE = 1;
            const uint LVHT_ONITEMLABEL = 4;
            sub = 0;
            flags = LVHT_NOWHERE;
            int count;
            try { count = list.Items.Count; }
            catch { return -1; }
            if (count <= 0)
                return -1;
            if (!ListRowMetrics(list, out int rowH, out int top))
                return -1;
            if (x < 0)
                return -1;
            int row = (y - top) / rowH;
            if (row < 0 || row >= count)
                return -1;
            if (subItems && list.View == global::System.Windows.Forms.View.Details)
            {
                try
                {
                    var widths = GetColumnState(list).Widths;
                    sub = Math.Max(0, widths.Count - 1);
                    int acc = 0;
                    for (int i = 0; i < widths.Count; i++)
                    {
                        int w = Math.Max(0, widths[i]);
                        if (x >= acc && x < acc + w) { sub = i; break; }
                        acc += w;
                    }
                }
                catch { sub = 0; }
            }
            flags = LVHT_ONITEMLABEL;
            return row;
        }

        private static int MeasureListColumn(global::System.Windows.Forms.ListView list, int index, bool headerOnly)
        {
            int best = 0;
            try
            {
                using var g = list.CreateGraphics();
                var font = list.Font;
                if (headerOnly)
                {
                    string? text = list.Columns.Count > index ? list.Columns[index].Text : null;
                    if (text is not null)
                        best = (int)Math.Ceiling(g.MeasureString(text, font).Width);
                }
                else
                {
                    for (int i = 0; i < list.Items.Count; i++)
                    {
                        string? text;
                        try
                        {
                            var item = list.Items[i];
                            text = index == 0 ? item.Text
                                : item.SubItems.Count > index ? item.SubItems[index].Text : null;
                        }
                        catch { text = null; }
                        if (text is null)
                            continue;
                        best = Math.Max(best, (int)Math.Ceiling(g.MeasureString(text, font).Width));
                    }
                }
            }
            catch
            {
            }
            return best;
        }

        private static void ApplyListViewState(ListViewSelection selection, int index, uint state, uint mask)
        {
            if ((mask & (uint)LIST_VIEW_ITEM_STATE_FLAGS.LVIS_SELECTED) != 0)
            {
                if ((state & (uint)LIST_VIEW_ITEM_STATE_FLAGS.LVIS_SELECTED) != 0)
                    selection.Selected.Add(index);
                else
                    selection.Selected.Remove(index);
            }
            if ((mask & (uint)LIST_VIEW_ITEM_STATE_FLAGS.LVIS_FOCUSED) != 0)
            {
                if ((state & (uint)LIST_VIEW_ITEM_STATE_FLAGS.LVIS_FOCUSED) != 0)
                    selection.Focused.Add(index);
                else
                    selection.Focused.Remove(index);
            }
        }

        private static unsafe LVFINDINFOW ReadLvFindInfo(LPARAM lParam)
        {
            try
            {
                if ((void*)lParam.Value == null)
                    return default;
                return *(LVFINDINFOW*)lParam.Value;
            }
            catch
            {
                return default;
            }
        }

        private static unsafe string? ReadFindText(LVFINDINFOW find)
        {
            try
            {
                char* p = (char*)find.psz;
                if (p == null)
                    return null;
                return new string(p);
            }
            catch
            {
                return null;
            }
        }
        // Index-keyed selection follows display-order edits the way the native
        // control shifts selection on insert/remove: drop removeAt (>= 0), then
        // shift every stored index >= from by delta.
        private static void RenumberListViewSelection(ListViewSelection selection, int removeAt, int from, int delta)
        {
            if (removeAt >= 0)
            {
                selection.Selected.Remove(removeAt);
                selection.Focused.Remove(removeAt);
            }
            if (delta != 0)
            {
                selection.Selected = RenumberIndexSet(selection.Selected, from, delta);
                selection.Focused = RenumberIndexSet(selection.Focused, from, delta);
            }
        }

        private static HashSet<int> RenumberIndexSet(HashSet<int> set, int from, int delta)
        {
            HashSet<int> next = new();
            foreach (int i in set)
                next.Add(i >= from ? i + delta : i);
            return next;
        }

        private static unsafe void AnswerLvItem(LPARAM lParam, int id, uint stateBits, uint mask)
        {
            try
            {
                LVITEMW* p = (LVITEMW*)lParam.Value;
                if (p == null)
                    return;
                if ((mask & (uint)LIST_VIEW_ITEM_FLAGS.LVIF_PARAM) != 0)
                    p->lParam = id;
                if ((mask & (uint)LIST_VIEW_ITEM_FLAGS.LVIF_STATE) != 0)
                    p->state = (LIST_VIEW_ITEM_STATE_FLAGS)stateBits;
            }
            catch
            {
            }
        }
        private static unsafe LVITEMW ReadLvItem(LPARAM lParam)
        {
            try
            {
                if ((void*)lParam.Value == null)
                    return default;
                return *(LVITEMW*)lParam.Value;
            }
            catch
            {
                return default;
            }
        }

        private static unsafe void WriteSelectionOut(WPARAM wParam, LPARAM lParam, int start, int end)
        {
            try
            {
                if ((void*)wParam.Value != null)
                    *(int*)wParam.Value = start;
                if ((void*)lParam.Value != null)
                    *(int*)lParam.Value = end;
            }
            catch
            {
            }
        }

        private static unsafe string ReadNullTerminatedString(LPARAM lParam)
        {
            try
            {
                if ((void*)lParam.Value == null)
                    return string.Empty;
                return new string((char*)lParam.Value);
            }
            catch
            {
                return string.Empty;
            }
        }

        internal static LRESULT SendMessage(HWND hWnd, uint Msg, WPARAM wParam = default, LPARAM lParam = default)
        {
            // ToolTip throws InvalidOperationException when TTM_ADDTOOLW
            // returns zero; the headless tip window accepts every tool.
            if (Msg == PInvoke.TTM_ADDTOOLW || Msg == PInvoke.TTM_DELTOOLW)
            {
                return (LRESULT)1;
            }
            if (Msg == PInvoke.CB_GETCURSEL || Msg == PInvoke.CB_SETCURSEL || Msg == PInvoke.CB_RESETCONTENT
                || Msg == PInvoke.CB_SHOWDROPDOWN || Msg == PInvoke.CB_GETDROPPEDSTATE)
            {
                var combo = global::System.Windows.Forms.Control.FromHandle((nint)hWnd.Value) as global::System.Windows.Forms.ComboBox;
                if (combo is null)
                {
                    // No live control behind the handle: the message reaches
                    // no window, exactly as SendMessage to a destroyed HWND.
                    return default;
                }

                if (Msg == PInvoke.CB_SHOWDROPDOWN || Msg == PInvoke.CB_GETDROPPEDSTATE)
                {
                    lock (s_comboDroppedLock)
                    {
                        if (Msg == PInvoke.CB_SHOWDROPDOWN)
                        {
                            s_comboDropped.GetOrCreateValue(combo).Open = (int)wParam.Value != 0;
                            return default;
                        }
                        if (s_comboDropped.TryGetValue(combo, out ComboDropped? dropped) && dropped.Open)
                            return (LRESULT)1;
                        return default;
                    }
                }
                lock (s_comboSelectionLock)
                {
                    if (Msg == PInvoke.CB_RESETCONTENT)
                    {
                        s_comboSelection.Remove(combo);
                        return default;
                    }

                    if (Msg == PInvoke.CB_SETCURSEL)
                    {
                        int selected = (int)wParam.Value;
                        s_comboSelection.GetOrCreateValue(combo).Index = selected;
                        return (LRESULT)selected;
                    }

                    if (s_comboSelection.TryGetValue(combo, out ComboSelection? selection))
                    {
                        return (LRESULT)selection.Index;
                    }

                    // Nothing was ever selected: CB_ERR, as on desktop.
                    return (LRESULT)PInvoke.CB_ERR;
                }
            }
            if (Msg == PInvoke.LB_GETCURSEL || Msg == PInvoke.LB_SETCURSEL || Msg == PInvoke.LB_SETSEL
                || Msg == PInvoke.LB_GETSEL || Msg == PInvoke.LB_GETSELCOUNT || Msg == PInvoke.LB_GETSELITEMS
                || Msg == PInvoke.LB_RESETCONTENT || Msg == PInvoke.LB_GETTEXT || Msg == PInvoke.LB_GETTEXTLEN
                || Msg == PInvoke.LB_INSERTSTRING || Msg == PInvoke.LB_DELETESTRING)
            {
                var listBox = global::System.Windows.Forms.Control.FromHandle((nint)hWnd.Value) as global::System.Windows.Forms.ListBox;
                if (listBox is null)
                {
                    // No live control behind the handle: the message reaches
                    // no window, exactly as SendMessage to a destroyed HWND.
                    return default;
                }

                lock (s_listBoxSelectionLock)
                {
                    if (Msg == PInvoke.LB_RESETCONTENT)
                    {
                        s_listBoxSelection.Remove(listBox);
                        return default;
                    }

                    if (Msg == PInvoke.LB_SETCURSEL)
                    {
                        int selected = (int)wParam.Value;
                        ListBoxSelection cursel = s_listBoxSelection.GetOrCreateValue(listBox);
                        cursel.Cursel = selected;
                        cursel.Selected.Clear();
                        if (selected >= 0)
                        {
                            cursel.Selected.Add(selected);
                        }

                        return (LRESULT)selected;
                    }

                    if (Msg == PInvoke.LB_SETSEL)
                    {
                        bool select = (int)wParam.Value != 0;
                        int index = (int)lParam.Value;
                        ListBoxSelection set = s_listBoxSelection.GetOrCreateValue(listBox);
                        if (index < 0)
                        {
                            set.Selected.Clear();
                            if (select)
                            {
                                for (int i = 0; i < listBox.Items.Count; i++)
                                {
                                    set.Selected.Add(i);
                                }
                            }
                        }
                        else if (select)
                        {
                            set.Selected.Add(index);
                        }
                        else
                        {
                            set.Selected.Remove(index);
                        }

                        return default;
                    }

                    if (Msg == PInvoke.LB_INSERTSTRING)
                    {
                        int index = (int)wParam.Value;
                        if (s_listBoxSelection.TryGetValue(listBox, out ListBoxSelection? ins) && ins.Selected.Count != 0)
                        {
                            ShiftListBoxSelection(ins, index, +1);
                        }

                        return (LRESULT)index;
                    }

                    if (Msg == PInvoke.LB_DELETESTRING)
                    {
                        int index = (int)wParam.Value;
                        if (s_listBoxSelection.TryGetValue(listBox, out ListBoxSelection? del))
                        {
                            if (del.Cursel == index)
                            {
                                del.Cursel = PInvoke.LB_ERR;
                            }

                            del.Selected.Remove(index);
                            ShiftListBoxSelection(del, index + 1, -1);
                        }

                        return (LRESULT)listBox.Items.Count;
                    }

                    if (Msg == PInvoke.LB_GETCURSEL)
                    {
                        if (s_listBoxSelection.TryGetValue(listBox, out ListBoxSelection? cur) && cur.Cursel >= 0)
                        {
                            return (LRESULT)cur.Cursel;
                        }

                        // Nothing was ever selected: LB_ERR, as on desktop.
                        return (LRESULT)PInvoke.LB_ERR;
                    }

                    if (Msg == PInvoke.LB_GETSEL)
                    {
                        int index = (int)wParam.Value;
                        if (s_listBoxSelection.TryGetValue(listBox, out ListBoxSelection? sel) && sel.Selected.Contains(index))
                        {
                            return (LRESULT)1;
                        }

                        return default;
                    }

                    if (Msg == PInvoke.LB_GETSELCOUNT)
                    {
                        if (s_listBoxSelection.TryGetValue(listBox, out ListBoxSelection? count))
                        {
                            return (LRESULT)count.Selected.Count;
                        }

                        return default;
                    }

                    if (Msg == PInvoke.LB_GETSELITEMS)
                    {
                        int capacity = (int)wParam.Value;
                        if (lParam.Value == 0 || capacity <= 0)
                        {
                            return (LRESULT)PInvoke.LB_ERR;
                        }

                        if (!s_listBoxSelection.TryGetValue(listBox, out ListBoxSelection? items) || items.Selected.Count == 0)
                        {
                            return default;
                        }

                        global::System.Collections.Generic.List<int> ordered = new(items.Selected);
                        ordered.Sort();
                        int written = 0;
                        unsafe
                        {
                            int* dest = (int*)lParam.Value;
                            foreach (int i in ordered)
                            {
                                if (written >= capacity)
                                {
                                    break;
                                }

                                dest[written++] = i;
                            }
                        }

                        return (LRESULT)written;
                    }

                    // LB_GETTEXTLEN / LB_GETTEXT answer live from the managed
                    // Items, so release traffic can never go stale.
                    int textIndex = (int)wParam.Value;
                    if (textIndex < 0 || textIndex >= listBox.Items.Count)
                    {
                        return (LRESULT)PInvoke.LB_ERR;
                    }

                    string text = listBox.GetItemText(listBox.Items[textIndex]) ?? string.Empty;
                    if (Msg == PInvoke.LB_GETTEXTLEN)
                    {
                        return (LRESULT)text.Length;
                    }

                    unsafe
                    {
                        char* dest = (char*)lParam.Value;
                        if (dest == null)
                        {
                            return (LRESULT)PInvoke.LB_ERR;
                        }

                        for (int i = 0; i < text.Length; i++)
                        {
                            dest[i] = text[i];
                        }

                        dest[text.Length] = '\0';
                    }

                    return (LRESULT)text.Length;
                }
            }
            if (Msg == PInvoke.TTM_SETDELAYTIME || Msg == PInvoke.TTM_GETDELAYTIME)
            {
                uint tipType = (uint)wParam.Value;
                if (Msg == PInvoke.TTM_SETDELAYTIME)
                {
                    int time = (int)lParam.Value;
                    var target = global::System.Windows.Forms.NativeWindow.FromHandle((nint)hWnd.Value);
                    if (target is not null)
                    {
                        lock (s_tipDelaysLock)
                        {
                            TipDelays delays = s_tipDelays.GetOrCreateValue(target);
                            if (tipType == PInvoke.TTDT_AUTOMATIC)
                            {
                                delays.Initial = time;
                                delays.AutoPop = time * 10;
                                delays.Reshow = time / 5;
                            }
                            else if (tipType == PInvoke.TTDT_RESHOW) delays.Reshow = time;
                            else if (tipType == PInvoke.TTDT_AUTOPOP) delays.AutoPop = time;
                            else if (tipType == PInvoke.TTDT_INITIAL) delays.Initial = time;
                        }
                    }
                    return (LRESULT)1;
                }

                var source = global::System.Windows.Forms.NativeWindow.FromHandle((nint)hWnd.Value);
                if (source is not null)
                {
                    lock (s_tipDelaysLock)
                    {
                        if (s_tipDelays.TryGetValue(source, out TipDelays? recorded))
                        {
                            return (LRESULT)(tipType == PInvoke.TTDT_RESHOW ? recorded.Reshow
                                : tipType == PInvoke.TTDT_AUTOPOP ? recorded.AutoPop
                                : recorded.Initial);
                        }
                    }
                }

                // Nothing recorded: the desktop native default.
                return (LRESULT)(tipType == PInvoke.TTDT_RESHOW ? 100
                    : tipType == PInvoke.TTDT_AUTOPOP ? 5000 : 500);
            }
            // Edit-control layout queries (spell squiggles, caret math)
            // answer from the managed text geometry (cf. the renderer's
            // single-run paint in WasmEditLayout).
            if (Msg == EM_POSFROMCHAR || Msg == EM_CHARFROMPOS || Msg == EM_LINEINDEX || Msg == EM_GETLINECOUNT || Msg == EM_LINEFROMCHAR)
            {
                try
                {
                    if (global::System.Windows.Forms.Control.FromHandle((nint)hWnd.Value) is global::System.Windows.Forms.TextBoxBase box)
                    {
                        if (Msg == EM_POSFROMCHAR)
                        {
                            global::System.Drawing.Point pt = global::System.Windows.Forms.WasmEditLayout.PositionFromChar(box, (int)wParam.Value);
                            return (LRESULT)(((pt.Y & 0xFFFF) << 16) | (pt.X & 0xFFFF));
                        }
                        if (Msg == EM_CHARFROMPOS)
                        {
                            int v = (int)lParam.Value;
                            int idx = global::System.Windows.Forms.WasmEditLayout.CharFromPos(box, (short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF));
                            return (LRESULT)idx;
                        }
                        if (Msg == EM_LINEINDEX)
                            return (LRESULT)global::System.Windows.Forms.WasmEditLayout.LineIndex(box, (int)wParam.Value);
                        if (Msg == EM_GETLINECOUNT)
                            return (LRESULT)global::System.Windows.Forms.WasmEditLayout.LineCount(box);
                        return (LRESULT)global::System.Windows.Forms.WasmEditLayout.LineFromChar(box, (int)wParam.Value);
                    }
                }
                catch
                {
                }
            }
            if (Msg == PInvoke.LVM_SETITEMSTATE || Msg == PInvoke.LVM_GETITEMSTATE
                || Msg == PInvoke.LVM_GETSELECTEDCOUNT || Msg == PInvoke.LVM_GETNEXTITEM
                || Msg == PInvoke.LVM_SETSELECTIONMARK || Msg == PInvoke.LVM_FINDITEMW
                || Msg == PInvoke.LVM_GETITEMW || Msg == PInvoke.LVM_INSERTITEMW
                || Msg == PInvoke.LVM_DELETEITEM || Msg == PInvoke.LVM_DELETEALLITEMS
                || Msg == PInvoke.LVM_SETCOLUMNW || Msg == PInvoke.LVM_SETCOLUMNWIDTH
                || Msg == PInvoke.LVM_GETCOLUMNWIDTH || Msg == PInvoke.LVM_GETHEADER
                || Msg == PInvoke.LVM_SUBITEMHITTEST || Msg == PInvoke.LVM_HITTEST
                || Msg == PInvoke.LVM_SORTITEMS || Msg == PInvoke.LVM_GETITEMRECT)
            {
                if (global::System.Windows.Forms.Control.FromHandle((nint)hWnd.Value) is global::System.Windows.Forms.ListView list)
                {
                    lock (s_listViewSelectionLock)
                    {
                        ListViewSelection selection = s_listViewSelection.GetOrCreateValue(list);
                        int itemCount;
                        try { itemCount = list.Items.Count; }
                        catch { itemCount = 0; }

                                                if (Msg == PInvoke.LVM_SETITEMSTATE)
                        {
                            int index = (int)wParam.Value;
                            LVITEMW lv = ReadLvItem(lParam);
                            if ((lv.mask & LIST_VIEW_ITEM_FLAGS.LVIF_STATE) != (LIST_VIEW_ITEM_FLAGS)0)
                            {
                                uint mask = (uint)lv.stateMask;
                                uint state = (uint)lv.state;
                                if (index == -1)
                                {
                                    for (int i = 0; i < itemCount; i++)
                                        ApplyListViewState(selection, i, state, mask);
                                }
                                else if (index >= 0 && index < itemCount)
                                {
                                    ApplyListViewState(selection, index, state, mask);
                                }
                            }
                            global::System.Console.WriteLine("[LVMTR] sel={" + string.Join(",", selection.Selected) + "} foc={" + string.Join(",", selection.Focused) + "} mark=" + selection.Mark);
                            return (LRESULT)1;
                        }

                        if (Msg == PInvoke.LVM_GETITEMSTATE)
                        {
                            int index = (int)wParam.Value;
                            uint mask = unchecked((uint)(int)lParam.Value);
                            uint bits = 0;
                            if (index >= 0 && index < itemCount)
                            {
                                if (selection.Selected.Contains(index))
                                    bits |= (uint)LIST_VIEW_ITEM_STATE_FLAGS.LVIS_SELECTED;
                                if (selection.Focused.Contains(index))
                                    bits |= (uint)LIST_VIEW_ITEM_STATE_FLAGS.LVIS_FOCUSED;
                            }
                            return (LRESULT)(int)(bits & mask);
                        }

                        if (Msg == PInvoke.LVM_GETSELECTEDCOUNT)
                        {
                            return (LRESULT)selection.Selected.Count;
                        }

                        if (Msg == PInvoke.LVM_GETNEXTITEM)
                        {
                            int start = (int)wParam.Value;
                            uint flags = unchecked((uint)(int)lParam.Value);
                            bool wantSelected = (flags & 2u) != 0; // LVNI_SELECTED
                            bool wantFocused = !wantSelected && (flags & 1u) != 0; // LVNI_FOCUSED
                            for (int i = start + 1; i < itemCount; i++)
                            {
                                if (wantSelected && selection.Selected.Contains(i))
                                    return (LRESULT)i;
                                if (wantFocused && selection.Focused.Contains(i))
                                    return (LRESULT)i;
                            }
                            return (LRESULT)(-1);
                        }

                        if (Msg == PInvoke.LVM_FINDITEMW)
                        {
                            int start = (int)wParam.Value;
                            LVFINDINFOW find = ReadLvFindInfo(lParam);
                            uint flags = (uint)find.flags;
                            if ((flags & (uint)LVFINDINFOW_FLAGS.LVFI_PARAM) != 0)
                            {
                                for (int i = start + 1; i < itemCount; i++)
                                {
                                    global::System.Windows.Forms.ListViewItem candidate;
                                    try { candidate = list.Items[i]; }
                                    catch { break; }
                                    if (find.lParam == candidate._id)
                                        return (LRESULT)i;
                                }
                                return (LRESULT)(-1);
                            }
                            if ((flags & (uint)LVFINDINFOW_FLAGS.LVFI_STRING) != 0)
                            {
                                bool prefix = (flags & (uint)LVFINDINFOW_FLAGS.LVFI_PARTIAL) != 0;
                                string? needle = ReadFindText(find);
                                if (needle is not null)
                                {
                                    for (int i = start + 1; i < itemCount; i++)
                                    {
                                        string? text;
                                        try { text = list.Items[i].Text; }
                                        catch { break; }
                                        if (text is null)
                                            continue;
                                        bool hit = prefix
                                            ? text.StartsWith(needle, StringComparison.OrdinalIgnoreCase)
                                            : string.Equals(text, needle, StringComparison.OrdinalIgnoreCase);
                                        if (hit)
                                            return (LRESULT)i;
                                    }
                                }
                                return (LRESULT)(-1);
                            }
                            return (LRESULT)(-1);
                        }
                        if (Msg == PInvoke.LVM_GETITEMW)
                        {
                            LVITEMW lv = ReadLvItem(lParam);
                            int index = lv.iItem;
                            if (index >= 0 && index < selection.Order.Count)
                            {
                                uint bits = 0;
                                if (selection.Selected.Contains(index))
                                    bits |= (uint)LIST_VIEW_ITEM_STATE_FLAGS.LVIS_SELECTED;
                                if (selection.Focused.Contains(index))
                                    bits |= (uint)LIST_VIEW_ITEM_STATE_FLAGS.LVIS_FOCUSED;
                                AnswerLvItem(lParam, selection.Order[index], bits, (uint)lv.mask);
                                return (LRESULT)1;
                            }
                            return default;
                        }

                        if (Msg == PInvoke.LVM_SUBITEMHITTEST || Msg == PInvoke.LVM_HITTEST)
                        {
                            // Managed hit testing for the suggestion/commit
                            // path: the native control has no rows headless,
                            // so the fallthrough default (0) made HitTest
                            // return Items[0] for every click — and threw
                            // ArgumentOutOfRange on empty lists. Row geometry
                            // is a documented headless approximation shared
                            // with the regression tests: row = Font.Height
                            // + 2, Details header = Font.Height + 8.
                            try
                            {
                                unsafe
                                {
                                    if ((void*)lParam.Value != null)
                                    {
                                        LVHITTESTINFO* p = (LVHITTESTINFO*)lParam.Value;
                                        LVHITTESTINFO hi = *p;
                                        int hit = HitTestListRow(list, hi.pt.X, hi.pt.Y,
                                            Msg == PInvoke.LVM_SUBITEMHITTEST,
                                            out int sub, out uint flags);
                                        hi.iItem = hit;
                                        hi.iSubItem = sub;
                                        hi.flags = (LVHITTESTINFO_FLAGS)flags;
                                        *p = hi;
                                        return (LRESULT)hit;
                                    }
                                }
                            }
                            catch
                            {
                            }
                            return default;
                        }

                        if (Msg == PInvoke.LVM_SORTITEMS)
                        {
                            // The native control rearranges rows headless it
                            // cannot, so the fallthrough left Sort() a silent
                            // no-op. Emulate with the managed sorter: display
                            // order lives in the Order mirror (which backs
                            // Items[i] on created handles), so sorting it is
                            // sufficient. Selection state is display-indexed,
                            // hence remapped by id to follow the items, as on
                            // desktop. The function pointer in lParam is the
                            // managed CompareFunc thunk; invoking it in wasm
                            // is unsupported, so the public ListViewItemSorter
                            // (same comparison, same process) is used instead.
                            try
                            {
                                var sorter = list.ListViewItemSorter;
                                if (sorter is not null && selection.Order.Count > 1)
                                {
                                    var items = new List<global::System.Windows.Forms.ListViewItem>(selection.Order.Count);
                                    for (int i = 0; i < selection.Order.Count; i++)
                                    {
                                        try { items.Add(list.Items[i]); }
                                        catch { break; }
                                    }
                                    if (items.Count == selection.Order.Count)
                                    {
                                        items.Sort((a, b) => sorter.Compare(a, b));
                                        var newOrder = new List<int>(items.Count);
                                        foreach (var item in items)
                                            newOrder.Add(item._id);
                                        var newIndex = new Dictionary<int, int>(newOrder.Count);
                                        for (int i = 0; i < newOrder.Count; i++)
                                            newIndex[newOrder[i]] = i;
                                        selection.Selected = RemapIndexSetById(selection.Selected, selection.Order, newIndex);
                                        selection.Focused = RemapIndexSetById(selection.Focused, selection.Order, newIndex);
                                        if (selection.Mark >= 0 && selection.Mark < selection.Order.Count
                                            && newIndex.TryGetValue(selection.Order[selection.Mark], out int newMark))
                                            selection.Mark = newMark;
                                        else if (selection.Mark >= selection.Order.Count)
                                            selection.Mark = -1;
                                        selection.Order.Clear();
                                        selection.Order.AddRange(newOrder);
                                    }
                                }
                                return (LRESULT)1;
                            }
                            catch
                            {
                            }
                            return default;
                        }

                        if (Msg == PInvoke.LVM_GETITEMRECT)
                        {
                            // Desktop fills the row rect and returns nonzero; the
                            // fallthrough default (0) made the upstream wrapper throw
                            // ArgumentOutOfRangeException for every valid index.
                            // Only LVIR_BOUNDS (0) and LVIR_SELECTBOUNDS (3) are
                            // answered, Details view only; icon/label portions
                            // keep the old behavior. Width is the managed
                            // column total, matching the autosize mirror.
                            try
                            {
                                unsafe
                                {
                                    RECT* p = (RECT*)lParam.Value;
                                    if (p is null)
                                        return default;
                                    int portion = p->left;
                                    if ((portion != 0 && portion != 3)
                                        || list.View != global::System.Windows.Forms.View.Details)
                                        return default;
                                    int row = (int)wParam.Value;
                                    int count;
                                    try { count = list.Items.Count; }
                                    catch { return default; }
                                    if (row < 0 || row >= count)
                                        return default;
                                    if (!ListRowMetrics(list, out int rowH, out int top))
                                        return default;
                                    int width = 0;
                                    try
                                    {
                                        foreach (int w in GetColumnState(list).Widths)
                                            width += Math.Max(0, w);
                                    }
                                    catch { }
                                    p->left = 0;
                                    p->top = top + row * rowH;
                                    p->right = width;
                                    p->bottom = top + (row + 1) * rowH;
                                    return (LRESULT)1;
                                }
                            }
                            catch
                            {
                            }
                            return default;
                        }

                        if (Msg == PInvoke.LVM_GETHEADER)
                        {
                            return (LRESULT)GetColumnState(list).FakeHeader;
                        }

                        if (Msg == PInvoke.LVM_GETCOLUMNWIDTH)
                        {
                            ListViewColumnState columns = GetColumnState(list);
                            int column = (int)wParam.Value;
                            if (column >= 0 && column < columns.Widths.Count)
                                return (LRESULT)columns.Widths[column];
                            return default;
                        }

                        if (Msg == PInvoke.LVM_SETCOLUMNWIDTH)
                        {
                            // Desktop LVM_SETCOLUMNWIDTH returns void; the
                            // managed column already holds explicit widths.
                            // Autosize modes are measured from managed text
                            // so later GETCOLUMNWIDTH reads fit content.
                            ListViewColumnState columns = GetColumnState(list);
                            int column = (int)wParam.Value;
                            int width = unchecked((int)lParam.Value);
                            if (column >= 0 && column < columns.Widths.Count)
                            {
                                if (width == PInvoke.LVSCW_AUTOSIZE)
                                    columns.Widths[column] = MeasureListColumn(list, column, headerOnly: false) + ListViewColumnPad;
                                else if (width == PInvoke.LVSCW_AUTOSIZE_USEHEADER)
                                    columns.Widths[column] = MeasureListColumn(list, column, headerOnly: true) + ListViewColumnPad;
                                else if (width >= 0)
                                    columns.Widths[column] = width;
                            }
                            return default;
                        }

                        if (Msg == PInvoke.LVM_SETCOLUMNW)
                        {
                            // No native header control exists headless; the
                            // managed ColumnHeader already holds the new
                            // text/image/format (setters assign before the
                            // native call), so desktop-TRUE success needs no
                            // mirror. Answer FALSE for an unknown index, as
                            // LVM_SETCOLUMNW does, preserving the throw path
                            // for genuinely bad callers.
                            int index = (int)wParam.Value;
                            int count;
                            try { count = list.Columns.Count; }
                            catch { count = 0; }
                            if (index >= 0 && index < count)
                                return (LRESULT)1;
                            return default;
                        }

                        if (Msg == PInvoke.LVM_INSERTITEMW)
                        {
                            // Handle recreation re-inserts every item through this
                            // path while the native control starts empty; the
                            // mirror is keyed by control and survives, so a
                            // re-inserted id that is already mirrored (with a
                            // full mirror) starts a fresh realization pass.
                            // Rebuild instead of appending duplicates, which
                            // corrupt display order reads and block sorting
                            // (Order.Count must match Items.Count). Gated on
                            // fullness so a pathological same-instance
                            // double-add (Order shorter than Items) still
                            // appends instead of wiping.
                            int realizedCount;
                            try { realizedCount = list.Items.Count; }
                            catch { realizedCount = -1; }
                            int insertId;
                            try { insertId = PARAM.ToInt(ReadLvItem(lParam).lParam); }
                            catch { insertId = -1; }
                            if (insertId >= 0 && realizedCount >= 0
                                && selection.Order.Count >= realizedCount
                                && selection.Order.Contains(insertId))
                            {
                                selection.Order.Clear();
                            }
                            LVITEMW lv = ReadLvItem(lParam);
                            int id;
                            try { id = PARAM.ToInt(lv.lParam); }
                            catch { id = -1; }
                            int at = lv.iItem;
                            global::System.Windows.Forms.SortOrder style = global::System.Windows.Forms.SortOrder.None;
                            global::System.Collections.IComparer? activeSorter = null;
                            bool virtualMode = false;
                            try
                            {
                                style = list.Sorting;
                                activeSorter = list.ListViewItemSorter;
                                virtualMode = list.VirtualMode;
                            }
                            catch { }
                            if (activeSorter is null && !virtualMode && style != global::System.Windows.Forms.SortOrder.None)
                            {
                                // LVS_SORT* style with no custom sorter: desktop inserts
                                // each item at its sorted position instead of appending.
                                // Comparison matches the sibling IconComparer (CurrentCulture,
                                // case-sensitive): a documented headless choice.
                                try
                                {
                                    var texts = new Dictionary<int, string>();
                                    for (int i = 0; i < selection.Order.Count; i++)
                                    {
                                        try
                                        {
                                            var candidate = list.Items[i];
                                            texts[candidate._id] = candidate.Text ?? "";
                                        }
                                        catch { break; }
                                    }
                                    string? incoming = null;
                                    try
                                    {
                                        unsafe
                                        {
                                            char* text = lv.pszText;
                                            if (text is not null && (nint)text != -1)
                                                incoming = new string(text);
                                            else if (text is null)
                                                incoming = "";
                                        }
                                    }
                                    catch { incoming = null; }
                                    if (incoming is not null)
                                    {
                                        int pos = 0;
                                        while (pos < selection.Order.Count)
                                        {
                                            string current = texts.TryGetValue(selection.Order[pos], out string? c) ? c : "";
                                            int cmp = string.Compare(incoming, current, StringComparison.CurrentCulture);
                                            if (style == global::System.Windows.Forms.SortOrder.Ascending ? cmp < 0 : cmp > 0)
                                                break;
                                            pos++;
                                        }
                                        at = pos;
                                    }
                                }
                                catch { }
                            }
                            if (at < 0)
                                at = 0;
                            if (at > selection.Order.Count)
                                at = selection.Order.Count;
                            selection.Order.Insert(at, id);
                            RenumberListViewSelection(selection, -1, at, +1);
                            return (LRESULT)at;
                        }

                        if (Msg == PInvoke.LVM_DELETEITEM)
                        {
                            int index = (int)wParam.Value;
                            if (index >= 0 && index < selection.Order.Count)
                            {
                                selection.Order.RemoveAt(index);
                                RenumberListViewSelection(selection, index, index + 1, -1);
                                return (LRESULT)1;
                            }
                            return default;
                        }

                        if (Msg == PInvoke.LVM_DELETEALLITEMS)
                        {
                            selection.Order.Clear();
                            selection.Selected.Clear();
                            selection.Focused.Clear();
                            selection.Mark = -1;
                            return (LRESULT)1;
                        }
                        // LVM_SETSELECTIONMARK: lParam is the index; answer the previous mark.
                        int previous = selection.Mark;
                        selection.Mark = (int)lParam.Value;
                        return (LRESULT)previous;
                    }
                }
            }
            if (Msg == PInvoke.HDM_GETITEMCOUNT)
            {
                // Answered only for fake headers minted by LVM_GETHEADER
                // above; real header handles keep the old fallthrough.
                int key = unchecked((int)hWnd.Value);
                lock (s_headerListsLock)
                {
                    if (s_headerLists.TryGetValue(key, out WeakReference<global::System.Windows.Forms.ListView>? weak)
                        && weak is not null
                        && weak.TryGetTarget(out global::System.Windows.Forms.ListView? owner))
                    {
                        try { return (LRESULT)owner.Columns.Count; }
                        catch { }
                    }
                }
                return default;
            }
            // No native RichEdit exists headless, so the framework's
            // EM_GETTEXTLENGTHEX traffic falls through to zero. That zero
            // is load-bearing: RichTextBox.TextLength sends this message
            // unconditionally (no handle check), and Select() clamps by
            // TextLength, so a zero length collapses every selection to
            // (0,0) -- the caret never advances and typed chars replace
            // the whole doc (gen19 typed "Hello World" showed a lone
            // "d"). Answer from the same WindowText storage the text
            // getters read; non-edits keep the old zero.
            if (Msg == EM_GETTEXTLENGTHEX)
            {
                if (global::System.Windows.Forms.Control.FromHandle((nint)hWnd.Value) is global::System.Windows.Forms.TextBoxBase)
                {
                    return (LRESULT)WindowTextStorage.GetLength(hWnd);
                }

                return default;
            }

            if (Msg == EM_SETSEL || Msg == EM_GETSEL || Msg == EM_REPLACESEL)
            {
                if (global::System.Windows.Forms.Control.FromHandle((nint)hWnd.Value) is global::System.Windows.Forms.TextBoxBase edit)
                {
                    lock (s_editSelectionLock)
                    {
                        EditSelection selection = s_editSelection.GetOrCreateValue(edit);
                        int textLength;
                        try
                        {
                            // RichTextBox.TextLength sends EM_GETTEXTLENGTHEX,
                            // stubbed to 0 headless, so the mirror clamps every
                            // rich selection to 0/0 (caret never advances and
                            // typed chars would pile at offset 0). The window-
                            // text storage both getters read is the same text,
                            // so use its length for rich boxes; plain boxes
                            // keep the virtual read (identical value there).
                            textLength = Math.Max(0, edit is global::System.Windows.Forms.RichTextBox
                                ? WindowTextStorage.GetLength(hWnd)
                                : edit.TextLength);
                        }
                        catch { textLength = 0; }

                        if (Msg == EM_SETSEL)
                        {
                            int start = (int)wParam.Value;
                            int end = (int)lParam.Value;
                            if (start == 0 && end == -1)
                            {
                                selection.Start = 0;
                                selection.End = textLength;
                            }
                            else if (start < 0)
                            {
                                // Deselect: collapse to the caret, as on desktop.
                                int caret = Math.Min(selection.Start, selection.End);
                                caret = Math.Min(Math.Max(0, caret), textLength);
                                selection.Start = caret;
                                selection.End = caret;
                            }
                            else
                            {
                                selection.Start = Math.Min(Math.Max(0, start), textLength);
                                selection.End = Math.Min(Math.Max(0, end), textLength);
                            }
                            return default;
                        }

                        if (Msg == EM_GETSEL)
                        {
                            int start = Math.Min(Math.Max(0, selection.Start), textLength);
                            int end = Math.Min(Math.Max(0, selection.End), textLength);
                            WriteSelectionOut(wParam, lParam, start, end);
                            return (LRESULT)(((end & 0xFFFF) << 16) | (start & 0xFFFF));
                        }

                        // EM_REPLACESEL: swap the mirrored selection for the
                        // NUL-terminated string at lParam (NULL deletes).
                        string replacement = ReadNullTerminatedString(lParam);
                        string current;
                        try { current = edit.Text ?? string.Empty; }
                        catch { current = string.Empty; }
                        int lo = Math.Min(Math.Min(selection.Start, selection.End), current.Length);
                        int hi = Math.Min(Math.Max(selection.Start, selection.End), current.Length);
                        lo = Math.Max(0, lo);
                        hi = Math.Max(0, hi);
                        if (hi < lo)
                            hi = lo;
                        try { edit.Text = current.Substring(0, lo) + replacement + current.Substring(hi); }
                        catch
                        {
                        }
                        selection.Start = lo + replacement.Length;
                        selection.End = selection.Start;
                        return default;
                    }
                }
            }
            return default;
        }

        internal static unsafe HRESULT CoCreateInstance(Guid* rclsid, void* pUnkOuter, CLSCTX dwClsContext, Guid* riid, void** ppv) => HRESULT.E_NOINTERFACE;
        internal static unsafe HRESULT CoCreateInstance(in Guid rclsid, void* pUnkOuter, CLSCTX dwClsContext, in Guid riid, void** ppv) => HRESULT.E_NOINTERFACE;
        internal static unsafe HRESULT CoCreateInstance(in Guid rclsid, void* pUnkOuter, CLSCTX dwClsContext, in Guid riid, out object? ppv)
        {
            ppv = null;
            return HRESULT.E_NOINTERFACE;
        }

        internal static unsafe HRESULT PropVariantClear(PROPVARIANT* pvar) => HRESULT.S_OK;

        internal static unsafe HRESULT SafeArrayDestroy(SAFEARRAY* psa) => HRESULT.S_OK;
        internal static unsafe HRESULT SafeArrayLock(SAFEARRAY* psa) => HRESULT.S_OK;
        internal static unsafe HRESULT SafeArrayUnlock(SAFEARRAY* psa) => HRESULT.S_OK;
        internal static unsafe HRESULT SafeArrayGetElement(SAFEARRAY* psa, int* rgIndices, void* pv) => HRESULT.S_OK;
        internal static unsafe HRESULT SafeArrayPutElement(SAFEARRAY* psa, int* rgIndices, void* pv) => HRESULT.S_OK;

        internal static unsafe int WideCharToMultiByte(uint codePage, uint dwFlags, PCWSTR lpWideCharStr, int cchWideChar, PSTR lpMultiByteStr, int cbMultiByte, PCSTR lpDefaultChar, BOOL* lpUsedDefaultChar) => 0;
        internal static unsafe int WideCharToMultiByte(uint codePage, uint dwFlags, string lpWideCharStr, int cchWideChar, PSTR lpMultiByteStr, int cbMultiByte, PCSTR lpDefaultChar) => 0;
        internal static unsafe int WideCharToMultiByte(uint codePage, uint dwFlags, string lpWideCharStr, int cchWideChar, void* lpMultiByteStr, int cbMultiByte, void* lpDefaultChar) => 0;

        internal static unsafe HRESULT WindowsCreateString(PCWSTR sourceString, uint length, HSTRING* @string) => HRESULT.S_OK;
        internal static HRESULT WindowsDeleteString(HSTRING @string) => HRESULT.S_OK;

        internal static unsafe HRESULT RoActivateInstance(HSTRING activatableClassId, IInspectable** instance) => HRESULT.E_NOTIMPL;

        internal static unsafe HRESULT LoadRegTypeLib(Guid* rguid, ushort wVerMajor, ushort wVerMinor, uint lcid, void** pptlib) => HRESULT.TYPE_E_LIBNOTREGISTERED;
        internal static unsafe HRESULT LoadRegTypeLib(in Guid rguid, ushort wVerMajor, ushort wVerMinor, uint lcid, void** pptlib) => HRESULT.TYPE_E_LIBNOTREGISTERED;

        internal static unsafe BOOL EnumThreadWindows(uint dwThreadId, delegate* unmanaged[Stdcall]<HWND, LPARAM, BOOL> lpfn, LPARAM lParam) => (BOOL)true;
        internal static unsafe BOOL SystemParametersInfoForDpi(uint uiAction, uint uiParam, void* pvParam, uint fWinIni, uint dpi) => (BOOL)true;

        internal static BOOL GetClientRect(HWND hWnd, out RECT lpRect)
        {
            var r = WindowBoundsStorage.Get(hWnd);
            if (r.Width <= 0 || r.Height <= 0)
            {
                // Headless controls never got bounds registered; the
                // control's own ClientRectangle is the truth (browser).
                try
                {
                    if (global::System.Windows.Forms.Control.FromHandle(hWnd) is { } control)
                    {
                        var client = control.ClientRectangle;
                        lpRect = new RECT(0, 0, Math.Max(0, client.Width), Math.Max(0, client.Height));
                        return (BOOL)true;
                    }
                }
                catch
                {
                }
            }
            lpRect = new RECT(0, 0, Math.Max(0, r.Width), Math.Max(0, r.Height));
            return (BOOL)true;
        }
        internal static BOOL GetWindowRect(HWND hWnd, out RECT lpRect)
        {
            var r = WindowBoundsStorage.Get(hWnd);
            // Win32 contract: screen coordinates. Storage holds the
            // parent-relative bounds SetWindowPos receives, so translate
            // by the parent client origin — the same origin
            // MapWindowPoints/ClientToScreen subtract/add (see
            // WasmScreenMapping). Top-level windows have no parent: their
            // stored bounds already are screen coordinates. Without this,
            // UpdateBounds() maps relative values as screen and writes
            // back own-minus-parent (child set Left=4 on a form at 350
            // reads back -346, so host clipping skips it and it never
            // paints: the readback-exposed suite failures).
            Point origin = Point.Empty;
            try
            {
                HWND parent = GetParent(hWnd);
                if (!parent.IsNull)
                {
                    origin = WasmScreenMapping.ClientOrigin(parent);
                }
            }
            catch
            {
            }
            lpRect = new RECT(r.Left + origin.X, r.Top + origin.Y, r.Right + origin.X, r.Bottom + origin.Y);
            return (BOOL)true;
        }
        internal static int GetWindowTextLength(HWND hWnd) => WindowTextStorage.GetLength(hWnd);
        internal static BOOL PostMessage(HWND hWnd, uint Msg, WPARAM wParam, LPARAM lParam) => (BOOL)true;
        internal static HRESULT RevokeDragDrop(HWND hwnd) => HRESULT.S_OK;
        internal static unsafe LRESULT CallWindowProc(delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT> lpPrevWndFunc, HWND hWnd, uint Msg, WPARAM wParam, LPARAM lParam) => default;
        internal static unsafe LRESULT CallWindowProc(void* lpPrevWndFunc, HWND hWnd, uint Msg, WPARAM wParam, LPARAM lParam) => default;
    }
}

