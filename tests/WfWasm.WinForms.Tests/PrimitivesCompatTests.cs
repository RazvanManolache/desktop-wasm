// REBOOT1555-WPF: exact-10 fork-missing identities (filed §4, settled-
// entry refs). CORRECTS the UNSTALL slice: its 5 green tests bound the
// wrong namespace (System.Windows.Forms.Primitives, a hypothesis the
// filing disproves). These pins bind the FILED full names —
// KeyboardAndMouse.TRACKMOUSEEVENT/_FLAGS, Controls.HDHITTESTINFO/
// HEADER_HITTEST_INFO_FLAGS/NMHDR, Gdi.REDRAW_WINDOW_FLAGS,
// StructuredStorage.ILockBytes, SystemServices.RECO_FLAGS,
// RichEdit.CHARRANGE, Foundation.WAIT_EVENT — with Win32-exact layout/
// values (SDK-verified) plus the managed TrackMouseTracker re-pointed
// at the filed TME. Isolated-ALC pattern (GetParentInteropTests).
// FAIL until the identities land; do not weaken.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Xunit;

namespace WfWasm.WinForms.Tests;

public sealed class PrimitivesCompatTests
{
    private static readonly Assembly ForkAssembly;

    private static readonly string[] Exact10 =
    {
        "Windows.Win32.UI.Input.KeyboardAndMouse.TRACKMOUSEEVENT",
        "Windows.Win32.UI.Input.KeyboardAndMouse.TRACKMOUSEEVENT_FLAGS",
        "Windows.Win32.UI.Controls.HDHITTESTINFO",
        "Windows.Win32.UI.Controls.HEADER_HITTEST_INFO_FLAGS",
        "Windows.Win32.UI.Controls.NMHDR",
        "Windows.Win32.Graphics.Gdi.REDRAW_WINDOW_FLAGS",
        "Windows.Win32.System.Com.StructuredStorage.ILockBytes",
        "Windows.Win32.System.SystemServices.RECO_FLAGS",
        "Windows.Win32.UI.Controls.RichEdit.CHARRANGE",
        "Windows.Win32.Foundation.WAIT_EVENT",
    };

    static PrimitivesCompatTests()
    {
        var asmPath = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\src\WfWasm.WinForms\bin\Debug\net9.0\WfWasm.WinForms.dll"));
        Assert.True(File.Exists(asmPath), $"WfWasm.WinForms.dll not found at {asmPath}");

        var alc = new AssemblyLoadContext("ExactIdentitiesLoadContext", isCollectible: true);
        ForkAssembly = alc.LoadFromAssemblyPath(asmPath);
    }

    private static Type Require(string fullName)
    {
        var t = ForkAssembly.GetType(fullName);
        Assert.True(t != null, $"filed identity missing: {fullName}");
        return t!;
    }

    [Fact]
    public void Exact10_Resolve_By_Filed_Name()
    {
        foreach (var name in Exact10)
            Assert.Equal(name, Require(name).FullName);
    }

    [Fact]
    public void Tme_Layout_And_Flags_Are_Exact()
    {
        var t = Require("Windows.Win32.UI.Input.KeyboardAndMouse.TRACKMOUSEEVENT");
        Assert.True(t.IsValueType);
        Assert.Equal(24, Marshal.SizeOf(t));
        Assert.Equal(0, (int)Marshal.OffsetOf(t, "cbSize")!);
        Assert.Equal(4, (int)Marshal.OffsetOf(t, "dwFlags")!);
        Assert.Equal(8, (int)Marshal.OffsetOf(t, "hwndTrack")!);
        Assert.Equal(16, (int)Marshal.OffsetOf(t, "dwHoverTime")!);
        var f = Require("Windows.Win32.UI.Input.KeyboardAndMouse.TRACKMOUSEEVENT_FLAGS");
        Assert.True(f.IsEnum);
        Assert.Equal(0x00000001u, Convert.ToUInt32(Enum.Parse(f, "TME_HOVER")));
        Assert.Equal(0x00000002u, Convert.ToUInt32(Enum.Parse(f, "TME_LEAVE")));
        Assert.Equal(0x00000010u, Convert.ToUInt32(Enum.Parse(f, "TME_NONCLIENT")));
        Assert.Equal(0x40000000u, Convert.ToUInt32(Enum.Parse(f, "TME_QUERY")));
        Assert.Equal(0x80000000u, Convert.ToUInt32(Enum.Parse(f, "TME_CANCEL")));
    }

    [Fact]
    public void Nmhdr_Layout_Is_Exact()
    {
        var t = Require("Windows.Win32.UI.Controls.NMHDR");
        Assert.True(t.IsValueType);
        Assert.Equal(24, Marshal.SizeOf(t));
        Assert.Equal(0, (int)Marshal.OffsetOf(t, "hwndFrom")!);
        Assert.Equal(8, (int)Marshal.OffsetOf(t, "idFrom")!);
        Assert.Equal(16, (int)Marshal.OffsetOf(t, "code")!);
    }

    [Fact]
    public void HdHitTestInfo_Layout_And_Flags_Are_Exact()
    {
        var t = Require("Windows.Win32.UI.Controls.HDHITTESTINFO");
        Assert.True(t.IsValueType);
        Assert.Equal(16, Marshal.SizeOf(t));
        Assert.Equal(0, (int)Marshal.OffsetOf(t, "pt")!);
        Assert.Equal(8, (int)Marshal.OffsetOf(t, "flags")!);
        Assert.Equal(12, (int)Marshal.OffsetOf(t, "iItem")!);
        var f = Require("Windows.Win32.UI.Controls.HEADER_HITTEST_INFO_FLAGS");
        Assert.True(f.IsEnum);
        Assert.Equal(0x0001u, Convert.ToUInt32(Enum.Parse(f, "HHT_NOWHERE")));
        Assert.Equal(0x0002u, Convert.ToUInt32(Enum.Parse(f, "HHT_ONHEADER")));
        Assert.Equal(0x0020u, Convert.ToUInt32(Enum.Parse(f, "HHT_ONFILTERBUTTON")));
        Assert.Equal(0x0100u, Convert.ToUInt32(Enum.Parse(f, "HHT_ABOVE")));
        Assert.Equal(0x0800u, Convert.ToUInt32(Enum.Parse(f, "HHT_TOLEFT")));
    }

    [Fact]
    public void Charrange_Layout_Is_Exact()
    {
        var t = Require("Windows.Win32.UI.Controls.RichEdit.CHARRANGE");
        Assert.True(t.IsValueType);
        Assert.Equal(8, Marshal.SizeOf(t));
        Assert.Equal(0, (int)Marshal.OffsetOf(t, "cpMin")!);
        Assert.Equal(4, (int)Marshal.OffsetOf(t, "cpMax")!);
    }

    [Fact]
    public void RedrawWindowFlags_Are_Exact()
    {
        var f = Require("Windows.Win32.Graphics.Gdi.REDRAW_WINDOW_FLAGS");
        Assert.True(f.IsEnum);
        Assert.Equal(typeof(uint), Enum.GetUnderlyingType(f));
        Assert.Equal(0x0001u, Convert.ToUInt32(Enum.Parse(f, "RDW_INVALIDATE")));
        Assert.Equal(0x0800u, Convert.ToUInt32(Enum.Parse(f, "RDW_NOFRAME")));
    }

    [Fact]
    public void RecoFlags_SystemServices_Are_Exact()
    {
        var f = Require("Windows.Win32.System.SystemServices.RECO_FLAGS");
        Assert.True(f.IsEnum);
        Assert.Equal(0u, Convert.ToUInt32(Enum.Parse(f, "RECO_PASTE")));
        Assert.Equal(4u, Convert.ToUInt32(Enum.Parse(f, "RECO_DRAG")));
    }

    [Fact]
    public void LockBytes_Identity_And_Vtable_Are_Exact()
    {
        var t = Require("Windows.Win32.System.Com.StructuredStorage.ILockBytes");
        Assert.True(t.IsValueType, "fork COM convention: vtable struct, not interface");
        var guid = (Guid)t.GetProperty("IID_Guid",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
        Assert.Equal(new Guid("0000000A-0000-0000-C000-000000000046"), guid);
        var vtable = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => m.Name).ToHashSet();
        foreach (var m in new[] { "ReadAt", "WriteAt", "Flush", "SetSize", "LockRegion", "UnlockRegion", "Stat", "AddRef", "Release" })
            Assert.Contains(m, vtable);
    }

    [Fact]
    public void WaitEvent_Values_Are_Exact()
    {
        var f = Require("Windows.Win32.Foundation.WAIT_EVENT");
        Assert.True(f.IsEnum);
        Assert.Equal(0u, Convert.ToUInt32(Enum.Parse(f, "WAIT_OBJECT_0")));
        Assert.Equal(0x80u, Convert.ToUInt32(Enum.Parse(f, "WAIT_ABANDONED")));
        Assert.Equal(0xC0u, Convert.ToUInt32(Enum.Parse(f, "WAIT_IO_COMPLETION")));
        Assert.Equal(0x102u, Convert.ToUInt32(Enum.Parse(f, "WAIT_TIMEOUT")));
        Assert.Equal(0xFFFFFFFFu, Convert.ToUInt32(Enum.Parse(f, "WAIT_FAILED")));
    }

    private static object NewFiledTme(uint flags, nint hwnd, uint hoverTime)
    {
        var tmeType = Require("Windows.Win32.UI.Input.KeyboardAndMouse.TRACKMOUSEEVENT");
        var flagsType = Require("Windows.Win32.UI.Input.KeyboardAndMouse.TRACKMOUSEEVENT_FLAGS");
        var tme = Activator.CreateInstance(tmeType)!;
        tmeType.GetField("cbSize")!.SetValue(tme, (uint)Marshal.SizeOf(tmeType));
        tmeType.GetField("dwFlags")!.SetValue(tme, Enum.ToObject(flagsType, flags));
        var hwndField = tmeType.GetField("hwndTrack")!;
        hwndField.SetValue(tme, Activator.CreateInstance(hwndField.FieldType, hwnd)!);
        tmeType.GetField("dwHoverTime")!.SetValue(tme, hoverTime);
        return tme;
    }

    private static Type TrackerType() =>
        Require("Windows.Win32.UI.Input.KeyboardAndMouse.TrackMouseTracker");

    [Fact]
    public void Tracker_Records_Queries_Cancels_Rejects_Bad_Size()
    {
        nint hwnd = 0xA11CE;
        var request = TrackerType().GetMethod("RequestTrack",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var query = TrackerType().GetMethod("QueryTrack",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        Assert.True((bool)request.Invoke(null, new[] { NewFiledTme(0x00000001u, hwnd, 400u) })!);
        var qargs = new object?[] { hwnd, null };
        Assert.True((bool)query.Invoke(null, qargs)!);
        Assert.NotNull(qargs[1]);
        Assert.True((bool)request.Invoke(null, new[] { NewFiledTme(0x80000000u, hwnd, 0u) })!);
        qargs = new object?[] { hwnd, null };
        Assert.False((bool)query.Invoke(null, qargs)!);
        var bad = NewFiledTme(0x00000002u, 0xBE57, 0u);
        Require("Windows.Win32.UI.Input.KeyboardAndMouse.TRACKMOUSEEVENT")
            .GetField("cbSize")!.SetValue(bad, 0u);
        Assert.False((bool)request.Invoke(null, new[] { bad })!);
    }
}
