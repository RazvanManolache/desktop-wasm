extern alias WasmDraw;

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Xunit;
using Point = WasmDraw::System.Drawing.Point;

namespace WfWasm.WinForms.Tests;

public sealed class ScreenMappingSafetyTests
{
    private static readonly MethodInfo ClientOriginMethod;
    private static readonly MethodInfo ClientToScreenMethod;
    private static readonly MethodInfo ScreenToClientMethod;
    private static readonly MethodInfo ClientToScreenPtrMethod;
    private static readonly MethodInfo ScreenToClientPtrMethod;
    private static readonly MethodInfo MapWindowPointsPointMethod;
    private static readonly MethodInfo MapWindowPointsRectMethod;
    private static readonly MethodInfo MapWindowPointsArrayMethod;
    private static readonly Type RectType;
    private static readonly Assembly ForkAssembly;

    static ScreenMappingSafetyTests()
    {
        var asmPath = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\src\WfWasm.WinForms\bin\Debug\net9.0\WfWasm.WinForms.dll"));
        Assert.True(File.Exists(asmPath), $"WfWasm.WinForms.dll not found at {asmPath}");

        var alc = new AssemblyLoadContext("ScreenMappingTestAlc", isCollectible: true);
        ForkAssembly = alc.LoadFromAssemblyPath(asmPath);

        Type[] allTypes;
        try { allTypes = ForkAssembly.GetTypes(); }
        catch (ReflectionTypeLoadException rtle) { allTypes = rtle.Types.Where(t => t != null).ToArray()!; }

        var screenMappingType = allTypes.FirstOrDefault(t => t.Name == "WasmScreenMapping");
        Assert.NotNull(screenMappingType);

        ClientOriginMethod = screenMappingType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == "ClientOrigin" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(IntPtr))!;
        Assert.NotNull(ClientOriginMethod);

        var pinvokeCoreType = allTypes.FirstOrDefault(t => t.Name == "PInvokeCore");
        Assert.NotNull(pinvokeCoreType);

        RectType = allTypes.FirstOrDefault(t => t.Name == "RECT")!;
        Assert.NotNull(RectType);

        var methods = pinvokeCoreType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        ClientToScreenMethod = methods.FirstOrDefault(m => m.Name == "ClientToScreen" &&
            m.GetParameters().Length == 2 &&
            m.GetParameters()[0].ParameterType == typeof(IntPtr) &&
            m.GetParameters()[1].ParameterType == typeof(Point).MakeByRefType())!;
        Assert.NotNull(ClientToScreenMethod);

        ScreenToClientMethod = methods.FirstOrDefault(m => m.Name == "ScreenToClient" &&
            m.GetParameters().Length == 2 &&
            m.GetParameters()[0].ParameterType == typeof(IntPtr) &&
            m.GetParameters()[1].ParameterType == typeof(Point).MakeByRefType())!;
        Assert.NotNull(ScreenToClientMethod);

        MapWindowPointsPointMethod = methods.FirstOrDefault(m => m.Name == "MapWindowPoints" &&
            m.GetParameters().Length == 3 &&
            m.GetParameters()[0].ParameterType == typeof(IntPtr) &&
            m.GetParameters()[1].ParameterType == typeof(IntPtr) &&
            m.GetParameters()[2].ParameterType == typeof(Point).MakeByRefType())!;
        Assert.NotNull(MapWindowPointsPointMethod);

        MapWindowPointsRectMethod = methods.FirstOrDefault(m => m.Name == "MapWindowPoints" &&
            m.GetParameters().Length == 3 &&
            m.GetParameters()[0].ParameterType == typeof(IntPtr) &&
            m.GetParameters()[1].ParameterType == typeof(IntPtr) &&
            m.GetParameters()[2].ParameterType == RectType.MakeByRefType())!;
        Assert.NotNull(MapWindowPointsRectMethod);

        unsafe
        {
            var ptrType = typeof(Point).MakePointerType();
            ClientToScreenPtrMethod = methods.FirstOrDefault(m => m.Name == "ClientToScreen" &&
                m.GetParameters().Length == 2 &&
                m.GetParameters()[0].ParameterType == typeof(IntPtr) &&
                m.GetParameters()[1].ParameterType == ptrType)!;
            Assert.NotNull(ClientToScreenPtrMethod);

            ScreenToClientPtrMethod = methods.FirstOrDefault(m => m.Name == "ScreenToClient" &&
                m.GetParameters().Length == 2 &&
                m.GetParameters()[0].ParameterType == typeof(IntPtr) &&
                m.GetParameters()[1].ParameterType == ptrType)!;
            Assert.NotNull(ScreenToClientPtrMethod);

            MapWindowPointsArrayMethod = methods.FirstOrDefault(m => m.Name == "MapWindowPoints" &&
                m.GetParameters().Length == 4 &&
                m.GetParameters()[0].ParameterType == typeof(IntPtr) &&
                m.GetParameters()[1].ParameterType == typeof(IntPtr) &&
                m.GetParameters()[2].ParameterType == ptrType &&
                m.GetParameters()[3].ParameterType == typeof(uint))!;
            Assert.NotNull(MapWindowPointsArrayMethod);
        }
    }

    private static Point CallClientOrigin(nint handle)
        => (Point)ClientOriginMethod.Invoke(null, new object[] { handle })!;

    private static (bool result, Point pt) CallClientToScreen(nint handle, Point pt)
    {
        var args = new object[] { handle, pt };
        var res = (bool)ClientToScreenMethod.Invoke(null, args)!;
        return (res, (Point)args[1]);
    }

    private static (bool result, Point pt) CallScreenToClient(nint handle, Point pt)
    {
        var args = new object[] { handle, pt };
        var res = (bool)ScreenToClientMethod.Invoke(null, args)!;
        return (res, (Point)args[1]);
    }

    private static (int ret, Point pt) CallMapWindowPoints(nint hWndFrom, nint hWndTo, Point pt)
    {
        var args = new object[] { hWndFrom, hWndTo, pt };
        var res = (int)MapWindowPointsPointMethod.Invoke(null, args)!;
        return (res, (Point)args[2]);
    }

    private static (int ret, int left, int top, int right, int bottom) CallMapWindowPointsRect(nint hWndFrom, nint hWndTo, int left, int top, int right, int bottom)
    {
        var rectInstance = Activator.CreateInstance(RectType, left, top, right, bottom)!;
        var args = new object[] { hWndFrom, hWndTo, rectInstance };
        var res = (int)MapWindowPointsRectMethod.Invoke(null, args)!;
        var r = args[2];
        int l = (int)RectType.GetField("left")!.GetValue(r)!;
        int t = (int)RectType.GetField("top")!.GetValue(r)!;
        int rg = (int)RectType.GetField("right")!.GetValue(r)!;
        int b = (int)RectType.GetField("bottom")!.GetValue(r)!;
        return (res, l, t, rg, b);
    }

    private static unsafe bool CallClientToScreenPtr(nint handle, Point* pPt)
    {
        var boxed = Pointer.Box(pPt, typeof(Point).MakePointerType());
        return (bool)ClientToScreenPtrMethod.Invoke(null, new object[] { handle, boxed })!;
    }

    private static unsafe bool CallScreenToClientPtr(nint handle, Point* pPt)
    {
        var boxed = Pointer.Box(pPt, typeof(Point).MakePointerType());
        return (bool)ScreenToClientPtrMethod.Invoke(null, new object[] { handle, boxed })!;
    }

    private static unsafe int CallMapWindowPointsArray(nint hWndFrom, nint hWndTo, Point* pPts, uint count)
    {
        var boxed = Pointer.Box(pPts, typeof(Point).MakePointerType());
        return (int)MapWindowPointsArrayMethod.Invoke(null, new object[] { hWndFrom, hWndTo, boxed, count })!;
    }

    private static (object instance, nint handle) CreateControl(string typeName)
    {
        var type = ForkAssembly.GetType(typeName)!;
        var ctrl = Activator.CreateInstance(type)!;
        var handleProp = type.GetProperty("Handle")!;
        var handle = (nint)handleProp.GetValue(ctrl)!;
        return (ctrl, handle);
    }

    private static void SetLocation(object control, int x, int y)
    {
        var controlType = ForkAssembly.GetType("System.Windows.Forms.Control")!;
        var locProp = controlType.GetProperty("Location")!;
        locProp.SetValue(control, new Point(x, y));
    }

    private static void SetParent(object child, object parent)
    {
        var controlType = ForkAssembly.GetType("System.Windows.Forms.Control")!;
        var parentProp = controlType.GetProperty("Parent")!;
        parentProp.SetValue(child, parent);
    }

    // --- 1. ClientOrigin Traversal Safety Tests ---

    [Fact]
    public void ClientOrigin_NullOrZero_ReturnsEmpty()
    {
        Assert.Equal(Point.Empty, CallClientOrigin(IntPtr.Zero));
    }

    [Fact]
    public void ClientOrigin_ForeignHandles_ReturnEmptySafely()
    {
        Assert.Equal(Point.Empty, CallClientOrigin((nint)4096));
        Assert.Equal(Point.Empty, CallClientOrigin(unchecked((nint)0xDEADBEEF)));
        Assert.Equal(Point.Empty, CallClientOrigin((nint)(-1)));
    }

    [Fact]
    public void ClientOrigin_DisposedControl_ReturnsEmptySafely()
    {
        var (ctrl, handle) = CreateControl("System.Windows.Forms.Button");
        ((IDisposable)ctrl).Dispose();
        Assert.Equal(Point.Empty, CallClientOrigin(handle));
    }

    [Fact]
    public void ClientOrigin_CaptionedForm_IncludesChromeHeight()
    {
        var (form, handle) = CreateControl("System.Windows.Forms.Form");
        try
        {
            SetLocation(form, 100, 200);
            var origin = CallClientOrigin(handle);
            // Default Form has WS_CAPTION -> ChromeHeight = 30
            Assert.Equal(100, origin.X);
            Assert.Equal(230, origin.Y);
        }
        finally
        {
            ((IDisposable)form).Dispose();
        }
    }

    [Fact]
    public void ClientOrigin_ChromelessForm_HasZeroChromeHeight()
    {
        var (form, handle) = CreateControl("System.Windows.Forms.Form");
        try
        {
            var formType = ForkAssembly.GetType("System.Windows.Forms.Form")!;
            var borderStyleProp = formType.GetProperty("FormBorderStyle")!;
            var noneEnum = Enum.Parse(ForkAssembly.GetType("System.Windows.Forms.FormBorderStyle")!, "None");
            borderStyleProp.SetValue(form, noneEnum);

            SetLocation(form, 100, 200);
            var origin = CallClientOrigin(handle);
            Assert.Equal(100, origin.X);
            Assert.Equal(200, origin.Y);
        }
        finally
        {
            ((IDisposable)form).Dispose();
        }
    }

    [Fact]
    public void ClientOrigin_NestedHierarchy_AccumulatesLocations()
    {
        var (form, formH) = CreateControl("System.Windows.Forms.Form");
        var (panel, panelH) = CreateControl("System.Windows.Forms.Panel");
        var (button, buttonH) = CreateControl("System.Windows.Forms.Button");
        try
        {
            SetLocation(form, 50, 50);
            SetLocation(panel, 20, 30);
            SetLocation(button, 10, 15);

            SetParent(panel, form);
            SetParent(button, panel);

            // Form: (50, 50 + 30) = (50, 80)
            Assert.Equal(new Point(50, 80), CallClientOrigin(formH));

            // Panel: Form (50, 80) + Panel (20, 30) = (70, 110)
            Assert.Equal(new Point(70, 110), CallClientOrigin(panelH));

            // Button: Panel (70, 110) + Button (10, 15) = (80, 125)
            Assert.Equal(new Point(80, 125), CallClientOrigin(buttonH));
        }
        finally
        {
            ((IDisposable)button).Dispose();
            ((IDisposable)panel).Dispose();
            ((IDisposable)form).Dispose();
        }
    }

    [Fact]
    public void ClientOrigin_DeeplyNestedHierarchy_TerminatesAtFifty()
    {
        // Construct a deep hierarchy of 60 controls
        const int count = 60;
        var controls = new (object instance, nint handle)[count];
        for (int i = 0; i < count; i++)
        {
            controls[i] = CreateControl("System.Windows.Forms.Panel");
            SetLocation(controls[i].instance, 1, 1);
            if (i > 0)
            {
                SetParent(controls[i].instance, controls[i - 1].instance);
            }
        }

        try
        {
            // Deepest control is controls[count - 1]
            // Traversal must bound at depth < 50 without stack overflow or infinite loop.
            var origin = CallClientOrigin(controls[count - 1].handle);
            // Exactly 50 steps of (1, 1) accumulated
            Assert.Equal(50, origin.X);
            Assert.Equal(50, origin.Y);
        }
        finally
        {
            for (int i = 0; i < count; i++)
            {
                ((IDisposable)controls[i].instance).Dispose();
            }
        }
    }

    // --- 2. ClientToScreen & ScreenToClient Safety Tests ---

    [Fact]
    public void ClientToScreen_NullAndForeignHandles_SucceedsSafely()
    {
        var (ok1, pt1) = CallClientToScreen(IntPtr.Zero, new Point(15, 25));
        Assert.True(ok1);
        Assert.Equal(new Point(15, 25), pt1);

        var (ok2, pt2) = CallClientToScreen((nint)4096, new Point(15, 25));
        Assert.True(ok2);
        Assert.Equal(new Point(15, 25), pt2);
    }

    [Fact]
    public void ScreenToClient_NullAndForeignHandles_SucceedsSafely()
    {
        var (ok1, pt1) = CallScreenToClient(IntPtr.Zero, new Point(100, 200));
        Assert.True(ok1);
        Assert.Equal(new Point(100, 200), pt1);

        var (ok2, pt2) = CallScreenToClient((nint)4096, new Point(100, 200));
        Assert.True(ok2);
        Assert.Equal(new Point(100, 200), pt2);
    }

    [Fact]
    public void ClientToScreen_And_ScreenToClient_InvertNestedHierarchy()
    {
        var (form, formH) = CreateControl("System.Windows.Forms.Form");
        var (panel, panelH) = CreateControl("System.Windows.Forms.Panel");
        var (button, buttonH) = CreateControl("System.Windows.Forms.Button");
        try
        {
            SetLocation(form, 50, 50);
            SetLocation(panel, 20, 30);
            SetLocation(button, 10, 15);

            SetParent(panel, form);
            SetParent(button, panel);

            // Button origin is (80, 125)
            var (okScreen, screenPt) = CallClientToScreen(buttonH, new Point(5, 5));
            Assert.True(okScreen);
            Assert.Equal(new Point(85, 130), screenPt);

            var (okClient, clientPt) = CallScreenToClient(buttonH, screenPt);
            Assert.True(okClient);
            Assert.Equal(new Point(5, 5), clientPt);
        }
        finally
        {
            ((IDisposable)button).Dispose();
            ((IDisposable)panel).Dispose();
            ((IDisposable)form).Dispose();
        }
    }

    // --- 3. MapWindowPoints Traversal Safety Tests ---

    [Fact]
    public void MapWindowPoints_NullHandles_TreatedAsScreenSpace()
    {
        var (ret, pt) = CallMapWindowPoints(IntPtr.Zero, IntPtr.Zero, new Point(42, 84));
        Assert.Equal(0, ret);
        Assert.Equal(new Point(42, 84), pt);
    }

    [Fact]
    public void MapWindowPoints_ForeignAndDisposedHandles_ExecuteSafely()
    {
        var (ret1, pt1) = CallMapWindowPoints((nint)4096, IntPtr.Zero, new Point(10, 20));
        Assert.Equal(0, ret1);
        Assert.Equal(new Point(10, 20), pt1);

        var (ret2, pt2) = CallMapWindowPoints(IntPtr.Zero, (nint)4096, new Point(10, 20));
        Assert.Equal(0, ret2);
        Assert.Equal(new Point(10, 20), pt2);

        var (form, formH) = CreateControl("System.Windows.Forms.Form");
        ((IDisposable)form).Dispose();
        var (ret3, pt3) = CallMapWindowPoints(formH, IntPtr.Zero, new Point(10, 20));
        Assert.Equal(0, ret3);
        Assert.Equal(new Point(10, 20), pt3);
    }

    [Fact]
    public void MapWindowPoints_ParentAndChild_MapsCoordinatesBothDirections()
    {
        var (form, formH) = CreateControl("System.Windows.Forms.Form");
        var (panel, panelH) = CreateControl("System.Windows.Forms.Panel");
        var (button, buttonH) = CreateControl("System.Windows.Forms.Button");
        try
        {
            SetLocation(form, 50, 50);
            SetLocation(panel, 20, 30);
            SetLocation(button, 10, 15);

            SetParent(panel, form);
            SetParent(button, panel);

            // Button -> Panel: dx = 10, dy = 15
            var (ret1, pt1) = CallMapWindowPoints(buttonH, panelH, new Point(0, 0));
            Assert.Equal(new Point(10, 15), pt1);
            Assert.Equal((15 << 16) | 10, ret1);

            // Panel -> Button: dx = -10, dy = -15
            var (ret2, pt2) = CallMapWindowPoints(panelH, buttonH, new Point(10, 15));
            Assert.Equal(new Point(0, 0), pt2);
            int expectedPacked = unchecked((int)(((uint)(ushort)(-15) << 16) | (uint)(ushort)(-10)));
            Assert.Equal(expectedPacked, ret2);

            // Button -> Form: dx = 30, dy = 45
            var (ret3, pt3) = CallMapWindowPoints(buttonH, formH, new Point(0, 0));
            Assert.Equal(new Point(30, 45), pt3);
            Assert.Equal((45 << 16) | 30, ret3);

            // Button -> Screen (IntPtr.Zero): dx = 80, dy = 125
            var (ret4, pt4) = CallMapWindowPoints(buttonH, IntPtr.Zero, new Point(0, 0));
            Assert.Equal(new Point(80, 125), pt4);
            Assert.Equal((125 << 16) | 80, ret4);

            // Screen -> Button: dx = -80, dy = -125
            var (ret5, pt5) = CallMapWindowPoints(IntPtr.Zero, buttonH, new Point(80, 125));
            Assert.Equal(new Point(0, 0), pt5);
        }
        finally
        {
            ((IDisposable)button).Dispose();
            ((IDisposable)panel).Dispose();
            ((IDisposable)form).Dispose();
        }
    }

    [Fact]
    public void MapWindowPoints_TwoSiblings_MapsRelativeDelta()
    {
        var (panel, panelH) = CreateControl("System.Windows.Forms.Panel");
        var (btn1, btn1H) = CreateControl("System.Windows.Forms.Button");
        var (btn2, btn2H) = CreateControl("System.Windows.Forms.Button");
        try
        {
            SetLocation(btn1, 10, 10);
            SetLocation(btn2, 40, 60);

            SetParent(btn1, panel);
            SetParent(btn2, panel);

            // btn1 to btn2: dx = 10 - 40 = -30, dy = 10 - 60 = -50
            var (ret1, pt1) = CallMapWindowPoints(btn1H, btn2H, new Point(0, 0));
            Assert.Equal(new Point(-30, -50), pt1);

            // btn2 to btn1: dx = 30, dy = 50
            var (ret2, pt2) = CallMapWindowPoints(btn2H, btn1H, new Point(0, 0));
            Assert.Equal(new Point(30, 50), pt2);
            Assert.Equal((50 << 16) | 30, ret2);
        }
        finally
        {
            ((IDisposable)btn2).Dispose();
            ((IDisposable)btn1).Dispose();
            ((IDisposable)panel).Dispose();
        }
    }

    [Fact]
    public void MapWindowPoints_RectOverload_MapsBothCornersAccurately()
    {
        var (panel, panelH) = CreateControl("System.Windows.Forms.Panel");
        var (button, buttonH) = CreateControl("System.Windows.Forms.Button");
        try
        {
            SetLocation(panel, 0, 0);
            SetLocation(button, 15, 25);
            SetParent(button, panel);

            // Mapping from button to panel adds (15, 25) to both top-left and bottom-right
            var (ret, left, top, right, bottom) = CallMapWindowPointsRect(buttonH, panelH, 5, 10, 50, 70);
            Assert.Equal((25 << 16) | 15, ret);
            Assert.Equal(20, left);
            Assert.Equal(35, top);
            Assert.Equal(65, right);
            Assert.Equal(95, bottom);
        }
        finally
        {
            ((IDisposable)button).Dispose();
            ((IDisposable)panel).Dispose();
        }
    }

    [Fact]
    public unsafe void ClientToScreen_PointerOverload_WorksAndHandlesNullPointer()
    {
        // Null pointer handling: returns false safely
        Assert.False(CallClientToScreenPtr(IntPtr.Zero, null));
        Assert.False(CallClientToScreenPtr((nint)4096, null));

        var (form, formH) = CreateControl("System.Windows.Forms.Form");
        try
        {
            SetLocation(form, 10, 20);
            Point pt = new Point(5, 5);
            bool ok = CallClientToScreenPtr(formH, &pt);
            Assert.True(ok);
            // Form origin = (10, 20 + 30) = (10, 50) -> pt = (15, 55)
            Assert.Equal(new Point(15, 55), pt);
        }
        finally
        {
            ((IDisposable)form).Dispose();
        }
    }

    [Fact]
    public unsafe void ScreenToClient_PointerOverload_WorksAndHandlesNullPointer()
    {
        // Null pointer handling: returns false safely
        Assert.False(CallScreenToClientPtr(IntPtr.Zero, null));
        Assert.False(CallScreenToClientPtr((nint)4096, null));

        var (form, formH) = CreateControl("System.Windows.Forms.Form");
        try
        {
            SetLocation(form, 10, 20);
            Point pt = new Point(15, 55);
            bool ok = CallScreenToClientPtr(formH, &pt);
            Assert.True(ok);
            Assert.Equal(new Point(5, 5), pt);
        }
        finally
        {
            ((IDisposable)form).Dispose();
        }
    }

    [Fact]
    public unsafe void MapWindowPoints_MultiplePointsArray_OffsetsAllPoints()
    {
        // Null pointer or 0 count handling: returns 0 safely
        Assert.Equal(0, CallMapWindowPointsArray(IntPtr.Zero, IntPtr.Zero, null, 0));
        Assert.Equal(0, CallMapWindowPointsArray(IntPtr.Zero, IntPtr.Zero, null, 5));

        var (panel, panelH) = CreateControl("System.Windows.Forms.Panel");
        var (button, buttonH) = CreateControl("System.Windows.Forms.Button");
        try
        {
            SetLocation(panel, 0, 0);
            SetLocation(button, 10, 20);
            SetParent(button, panel);

            Point[] pts = new[]
            {
                new Point(0, 0),
                new Point(10, 20),
                new Point(30, 40),
            };

            fixed (Point* p = pts)
            {
                int ret = CallMapWindowPointsArray(buttonH, panelH, p, (uint)pts.Length);
                Assert.Equal((20 << 16) | 10, ret);
            }

            Assert.Equal(new Point(10, 20), pts[0]);
            Assert.Equal(new Point(20, 40), pts[1]);
            Assert.Equal(new Point(40, 60), pts[2]);
        }
        finally
        {
            ((IDisposable)button).Dispose();
            ((IDisposable)panel).Dispose();
        }
    }
}
