using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows.Forms;
using Xunit;

namespace WfWasm.WinForms.Tests;

public sealed class WindowHierarchySafetyTests
{
    private static readonly MethodInfo GetParentMethod;
    private static readonly MethodInfo GetAncestorMethod;
    private static readonly MethodInfo GetWindowMethod;
    private static readonly MethodInfo IsChildMethod;
    private static readonly Type HwndType;
    private static readonly Type GetAncestorFlagsType;
    private static readonly Type GetWindowCmdType;
    private static readonly Assembly ForkAssembly;

    static WindowHierarchySafetyTests()
    {
        var asmPath = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\src\WfWasm.WinForms\bin\Debug\net9.0\WfWasm.WinForms.dll"));
        Assert.True(File.Exists(asmPath), $"WfWasm.WinForms.dll not found at {asmPath}");

        var alc = new AssemblyLoadContext("HierarchyTestAlc", isCollectible: true);
        ForkAssembly = alc.LoadFromAssemblyPath(asmPath);

        Type[] allTypes;
        try { allTypes = ForkAssembly.GetTypes(); }
        catch (ReflectionTypeLoadException rtle) { allTypes = rtle.Types.Where(t => t != null).ToArray()!; }

        var pinvokeCoreType = allTypes.FirstOrDefault(t => t.Name == "PInvokeCore");
        Assert.NotNull(pinvokeCoreType);

        HwndType = allTypes.FirstOrDefault(t => t.Name == "HWND")!;
        Assert.NotNull(HwndType);

        GetAncestorFlagsType = allTypes.FirstOrDefault(t => t.Name == "GET_ANCESTOR_FLAGS")!;
        Assert.NotNull(GetAncestorFlagsType);

        GetWindowCmdType = allTypes.FirstOrDefault(t => t.Name == "GET_WINDOW_CMD")!;
        Assert.NotNull(GetWindowCmdType);

        var methods = pinvokeCoreType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        GetParentMethod = methods.FirstOrDefault(m => m.Name == "GetParent" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(IntPtr))!;
        Assert.NotNull(GetParentMethod);

        GetAncestorMethod = methods.FirstOrDefault(m => m.Name == "GetAncestor" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(IntPtr))!;
        Assert.NotNull(GetAncestorMethod);

        GetWindowMethod = methods.FirstOrDefault(m => m.Name == "GetWindow" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(IntPtr))!;
        Assert.NotNull(GetWindowMethod);

        IsChildMethod = methods.FirstOrDefault(m => m.Name == "IsChild" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(IntPtr))!;
        Assert.NotNull(IsChildMethod);
    }

    private static nint CallGetParent(nint handle) => (nint)GetParentMethod.Invoke(null, new object[] { handle })!;
    private static nint CallGetAncestor(nint handle, uint flags) => (nint)GetAncestorMethod.Invoke(null, new object[] { handle, Enum.ToObject(GetAncestorFlagsType, flags) })!;
    private static nint CallGetWindow(nint handle, uint cmd) => (nint)GetWindowMethod.Invoke(null, new object[] { handle, Enum.ToObject(GetWindowCmdType, cmd) })!;
    private static bool CallIsChild(nint parent, nint child) => (bool)IsChildMethod.Invoke(null, new object[] { parent, child })!;

    private static (object instance, nint handle) CreateControl(string typeName)
    {
        var type = ForkAssembly.GetType(typeName)!;
        var ctrl = Activator.CreateInstance(type)!;
        var handleProp = type.GetProperty("Handle")!;
        var handle = (nint)handleProp.GetValue(ctrl)!;
        return (ctrl, handle);
    }

    // --- 1. GetParent Safety Tests ---

    [Fact]
    public void GetParent_NullOrZero_ReturnsNull()
    {
        Assert.Equal(IntPtr.Zero, CallGetParent(IntPtr.Zero));
    }

    [Fact]
    public void GetParent_UnknownHandle_ReturnsNull()
    {
        Assert.Equal(IntPtr.Zero, CallGetParent((nint)4096));
        Assert.Equal(IntPtr.Zero, CallGetParent(unchecked((nint)0xDEADBEEF)));
    }

    [Fact]
    public void GetParent_NestedHierarchy_TraversesAndTerminates()
    {
        var (rootForm, rootH) = CreateControl("System.Windows.Forms.Form");
        var (panel, panelH) = CreateControl("System.Windows.Forms.Panel");
        var (button, buttonH) = CreateControl("System.Windows.Forms.Button");
        try
        {
            var controlType = ForkAssembly.GetType("System.Windows.Forms.Control")!;
            var parentProp = controlType.GetProperty("Parent")!;
            parentProp.SetValue(panel, rootForm);
            parentProp.SetValue(button, panel);

            Assert.Equal(panelH, CallGetParent(buttonH));
            Assert.Equal(rootH, CallGetParent(panelH));
            Assert.Equal(IntPtr.Zero, CallGetParent(rootH));

            // Traversal from button terminates at IntPtr.Zero in 3 hops (button -> panel -> rootForm -> Zero)
            nint cur = buttonH;
            int count = 0;
            while (cur != IntPtr.Zero && count < 10)
            {
                cur = CallGetParent(cur);
                count++;
            }
            Assert.Equal(IntPtr.Zero, cur);
            Assert.Equal(3, count);
        }
        finally
        {
            ((IDisposable)button).Dispose();
            ((IDisposable)panel).Dispose();
            ((IDisposable)rootForm).Dispose();
        }
    }

    // --- 2. GetAncestor Safety Tests ---

    [Fact]
    public void GetAncestor_NullAndForeignHandles_ReturnNull()
    {
        const uint GA_PARENT = 1;
        const uint GA_ROOT = 2;
        const uint GA_ROOTOWNER = 3;

        Assert.Equal(IntPtr.Zero, CallGetAncestor(IntPtr.Zero, GA_PARENT));
        Assert.Equal(IntPtr.Zero, CallGetAncestor(IntPtr.Zero, GA_ROOT));
        Assert.Equal(IntPtr.Zero, CallGetAncestor(IntPtr.Zero, GA_ROOTOWNER));

        Assert.Equal(IntPtr.Zero, CallGetAncestor((nint)4096, GA_PARENT));
        Assert.Equal(IntPtr.Zero, CallGetAncestor((nint)4096, GA_ROOT));
        Assert.Equal(IntPtr.Zero, CallGetAncestor((nint)4096, GA_ROOTOWNER));

        Assert.Equal(IntPtr.Zero, CallGetAncestor(unchecked((nint)0xBAADF00D), GA_ROOT));
    }

    [Fact]
    public void GetAncestor_ResolvesParentAndRootCorrectly()
    {
        const uint GA_PARENT = 1;
        const uint GA_ROOT = 2;
        const uint GA_ROOTOWNER = 3;

        var (rootForm, rootH) = CreateControl("System.Windows.Forms.Form");
        var (panel, panelH) = CreateControl("System.Windows.Forms.Panel");
        var (button, buttonH) = CreateControl("System.Windows.Forms.Button");
        try
        {
            var controlType = ForkAssembly.GetType("System.Windows.Forms.Control")!;
            var parentProp = controlType.GetProperty("Parent")!;
            parentProp.SetValue(panel, rootForm);
            parentProp.SetValue(button, panel);

            // GA_PARENT on button is panel
            Assert.Equal(panelH, CallGetAncestor(buttonH, GA_PARENT));
            // GA_PARENT on root form is Null
            Assert.Equal(IntPtr.Zero, CallGetAncestor(rootH, GA_PARENT));

            // GA_ROOT on button is rootForm
            Assert.Equal(rootH, CallGetAncestor(buttonH, GA_ROOT));
            // GA_ROOT on panel is rootForm
            Assert.Equal(rootH, CallGetAncestor(panelH, GA_ROOT));
            // GA_ROOT on root form is itself
            Assert.Equal(rootH, CallGetAncestor(rootH, GA_ROOT));
            // GA_ROOTOWNER on button is rootForm
            Assert.Equal(rootH, CallGetAncestor(buttonH, GA_ROOTOWNER));
        }
        finally
        {
            ((IDisposable)button).Dispose();
            ((IDisposable)panel).Dispose();
            ((IDisposable)rootForm).Dispose();
        }
    }

    [Fact]
    public void GetAncestor_RootOwner_ResolvesOwnedFormOwner()
    {
        const uint GA_ROOTOWNER = 3;
        var (mainForm, mainH) = CreateControl("System.Windows.Forms.Form");
        var (dialogForm, dialogH) = CreateControl("System.Windows.Forms.Form");
        try
        {
            var formType = ForkAssembly.GetType("System.Windows.Forms.Form")!;
            var ownerProp = formType.GetProperty("Owner")!;
            ownerProp.SetValue(dialogForm, mainForm);

            // Dialog form owned by mainForm resolves to mainForm for GA_ROOTOWNER
            Assert.Equal(mainH, CallGetAncestor(dialogH, GA_ROOTOWNER));
        }
        finally
        {
            ((IDisposable)dialogForm).Dispose();
            ((IDisposable)mainForm).Dispose();
        }
    }

    // --- 3. GetWindow Safety Tests ---

    [Fact]
    public void GetWindow_NullAndForeignHandles_ReturnNull()
    {
        for (uint cmd = 0; cmd <= 6; cmd++)
        {
            Assert.Equal(IntPtr.Zero, CallGetWindow(IntPtr.Zero, cmd));
            Assert.Equal(IntPtr.Zero, CallGetWindow((nint)4096, cmd));
            Assert.Equal(IntPtr.Zero, CallGetWindow(unchecked((nint)0x12345678), cmd));
        }
    }

    [Fact]
    public void GetWindow_ChildrenAndSiblings_NavigateCorrectly()
    {
        const uint GW_CHILD = 5;
        const uint GW_HWNDNEXT = 2;
        const uint GW_HWNDPREV = 3;

        var (container, containerH) = CreateControl("System.Windows.Forms.Panel");
        var (btn1, btn1H) = CreateControl("System.Windows.Forms.Button");
        var (btn2, btn2H) = CreateControl("System.Windows.Forms.Button");
        try
        {
            var controlType = ForkAssembly.GetType("System.Windows.Forms.Control")!;
            var parentProp = controlType.GetProperty("Parent")!;
            parentProp.SetValue(btn1, container);
            parentProp.SetValue(btn2, container);

            // GW_CHILD of container returns first child
            nint firstChild = CallGetWindow(containerH, GW_CHILD);
            Assert.True(firstChild == btn1H || firstChild == btn2H);

            // Sibling traversal
            nint next = CallGetWindow(btn1H, GW_HWNDNEXT);
            if (next != IntPtr.Zero)
            {
                Assert.Equal(btn2H, next);
                Assert.Equal(btn1H, CallGetWindow(btn2H, GW_HWNDPREV));
            }
        }
        finally
        {
            ((IDisposable)btn2).Dispose();
            ((IDisposable)btn1).Dispose();
            ((IDisposable)container).Dispose();
        }
    }

    // --- 4. IsChild Safety Tests ---

    [Fact]
    public void IsChild_EdgeCases_ReturnFalseSafely()
    {
        // Self is not child of self
        Assert.False(CallIsChild(IntPtr.Zero, IntPtr.Zero));
        Assert.False(CallIsChild((nint)4096, (nint)4096));

        // Foreign handles are not children
        Assert.False(CallIsChild((nint)4096, (nint)4097));
        Assert.False(CallIsChild(IntPtr.Zero, (nint)4096));
        Assert.False(CallIsChild((nint)4096, IntPtr.Zero));
    }

    [Fact]
    public void IsChild_NestedHierarchy_IdentifiesDescendants()
    {
        var (rootForm, rootH) = CreateControl("System.Windows.Forms.Form");
        var (panel, panelH) = CreateControl("System.Windows.Forms.Panel");
        var (button, buttonH) = CreateControl("System.Windows.Forms.Button");
        var (unrelated, unrelatedH) = CreateControl("System.Windows.Forms.Button");
        try
        {
            var controlType = ForkAssembly.GetType("System.Windows.Forms.Control")!;
            var parentProp = controlType.GetProperty("Parent")!;
            parentProp.SetValue(panel, rootForm);
            parentProp.SetValue(button, panel);

            // Button is child of panel
            Assert.True(CallIsChild(panelH, buttonH));
            // Button is descendant (child) of rootForm
            Assert.True(CallIsChild(rootH, buttonH));
            // Panel is child of rootForm
            Assert.True(CallIsChild(rootH, panelH));

            // Reverse relationship is false
            Assert.False(CallIsChild(buttonH, panelH));
            Assert.False(CallIsChild(buttonH, rootH));

            // Unrelated control is false
            Assert.False(CallIsChild(rootH, unrelatedH));
            Assert.False(CallIsChild(panelH, unrelatedH));
        }
        finally
        {
            ((IDisposable)unrelated).Dispose();
            ((IDisposable)button).Dispose();
            ((IDisposable)panel).Dispose();
            ((IDisposable)rootForm).Dispose();
        }
    }

    [Fact]
    public void TraversalHelpers_DisposedControls_DoNotThrow()
    {
        var (form, h) = CreateControl("System.Windows.Forms.Form");
        ((IDisposable)form).Dispose();

        var ex = Record.Exception(() =>
        {
            Assert.Equal(IntPtr.Zero, CallGetParent(h));
            Assert.Equal(IntPtr.Zero, CallGetAncestor(h, 1));
            Assert.Equal(IntPtr.Zero, CallGetWindow(h, 5));
            Assert.False(CallIsChild((nint)1234, h));
            Assert.False(CallIsChild(h, (nint)1234));
        });

        Assert.Null(ex);
    }
}
