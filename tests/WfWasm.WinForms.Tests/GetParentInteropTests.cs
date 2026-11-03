using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows.Forms;
using Xunit;

namespace WfWasm.WinForms.Tests;

public sealed class GetParentInteropTests
{
    private static readonly MethodInfo GetParentNintMethod;
    private static readonly MethodInfo GetParentHwndMethod;
    private static readonly Type HwndType;
    private static readonly Assembly ForkAssembly;

    static GetParentInteropTests()
    {
        var asmPath = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\src\WfWasm.WinForms\bin\Debug\net9.0\WfWasm.WinForms.dll"));
        Assert.True(File.Exists(asmPath), $"WfWasm.WinForms.dll not found at {asmPath}");

        var alc = new AssemblyLoadContext("IsolatedTestLoadContext", isCollectible: true);
        ForkAssembly = alc.LoadFromAssemblyPath(asmPath);

        Type[] allTypes;
        try { allTypes = ForkAssembly.GetTypes(); }
        catch (ReflectionTypeLoadException rtle) { allTypes = rtle.Types.Where(t => t != null).ToArray()!; }

        var pinvokeCoreType = allTypes.FirstOrDefault(t => t.Name == "PInvokeCore");
        Assert.NotNull(pinvokeCoreType);

        HwndType = allTypes.FirstOrDefault(t => t.Name == "HWND")!;
        Assert.NotNull(HwndType);

        var getParentMethods = pinvokeCoreType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.Name == "GetParent")
            .ToArray();

        string sigs = string.Join("; ", getParentMethods.Select(m => m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName)) + ")"));

        GetParentHwndMethod = getParentMethods.FirstOrDefault(m => m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "HWND")!;
        Assert.True(GetParentHwndMethod != null, $"HWND method not found in: {sigs}");

        GetParentNintMethod = getParentMethods.FirstOrDefault(m => m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "IntPtr")!;
        Assert.True(GetParentNintMethod != null, $"IntPtr method not found in: {sigs}");
    }

    private static nint CallGetParent(nint handle)
    {
        return (nint)GetParentNintMethod.Invoke(null, new object[] { handle })!;
    }

    private static nint CallGetParentHwnd(nint handle)
    {
        var hwnd = Activator.CreateInstance(HwndType, handle);
        var resHwnd = GetParentHwndMethod.Invoke(null, new[] { hwnd });
        var valProp = HwndType.GetField("Value");
        return (nint)valProp!.GetValue(resHwnd)!;
    }

    private static (object instance, nint handle) CreateForkControl(string typeName)
    {
        var type = ForkAssembly.GetType(typeName)!;
        var ctrl = Activator.CreateInstance(type)!;
        var handleProp = type.GetProperty("Handle")!;
        var handle = (nint)handleProp.GetValue(ctrl)!;
        return (ctrl, handle);
    }

    [Fact]
    public void GetParent_NullOrZero_ReturnsNull()
    {
        Assert.Equal(IntPtr.Zero, CallGetParent(IntPtr.Zero));
        Assert.Equal(IntPtr.Zero, CallGetParentHwnd(IntPtr.Zero));
    }

    [Fact]
    public void GetParent_UnknownHandle_ReturnsNull()
    {
        // 4096 is the typical dummy handle returned by generic P/Invoke stubs
        Assert.Equal(IntPtr.Zero, CallGetParent((nint)4096));
        Assert.Equal(IntPtr.Zero, CallGetParentHwnd((nint)4096));

        // Foreign arbitrary large handle
        Assert.Equal(IntPtr.Zero, CallGetParent(unchecked((nint)0xDEADBEEF)));
        Assert.Equal(IntPtr.Zero, CallGetParentHwnd(unchecked((nint)0xDEADBEEF)));
    }

    [Fact]
    public void GetParent_RootControl_ReturnsNull()
    {
        var (rootForm, handle) = CreateForkControl("System.Windows.Forms.Form");
        try
        {
            Assert.Equal(IntPtr.Zero, CallGetParent(handle));
            Assert.Equal(IntPtr.Zero, CallGetParentHwnd(handle));
        }
        finally
        {
            ((IDisposable)rootForm).Dispose();
        }
    }

    [Fact]
    public void GetParent_NestedHierarchy_ResolvesCorrectlyAndTerminates()
    {
        var (rootForm, rootHandle) = CreateForkControl("System.Windows.Forms.Form");
        var (panel, panelHandle) = CreateForkControl("System.Windows.Forms.Panel");
        var (button, buttonHandle) = CreateForkControl("System.Windows.Forms.Button");

        try
        {
            var controlType = ForkAssembly.GetType("System.Windows.Forms.Control")!;
            var parentProp = controlType.GetProperty("Parent")!;

            // Set parent hierarchy: panel.Parent = rootForm, button.Parent = panel
            parentProp.SetValue(panel, rootForm);
            parentProp.SetValue(button, panel);

            // Button's parent is Panel
            nint buttonParent = CallGetParent(buttonHandle);
            Assert.Equal(panelHandle, buttonParent);
            Assert.Equal(panelHandle, CallGetParentHwnd(buttonHandle));

            // Panel's parent is RootForm
            nint panelParent = CallGetParent(buttonParent);
            Assert.Equal(rootHandle, panelParent);
            Assert.Equal(rootHandle, CallGetParentHwnd(buttonParent));

            // RootForm's parent is Null
            nint rootParent = CallGetParent(panelParent);
            Assert.Equal(IntPtr.Zero, rootParent);
            Assert.Equal(IntPtr.Zero, CallGetParentHwnd(panelParent));

            // Simulate PopupUtils.GetADotNetParentControl loop over the hierarchy
            var fromHandleMethod = controlType.GetMethod("FromHandle", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(IntPtr) }, null)!;

            nint current = buttonHandle;
            object? resolved = null;
            int loopCount = 0;
            while (current != IntPtr.Zero && loopCount < 50)
            {
                loopCount++;
                current = CallGetParent(current);
                if (current != IntPtr.Zero)
                {
                    resolved = fromHandleMethod.Invoke(null, new object[] { current });
                    if (resolved != null) break;
                }
            }

            Assert.Equal(1, loopCount);
            Assert.Same(panel, resolved);
        }
        finally
        {
            ((IDisposable)button).Dispose();
            ((IDisposable)panel).Dispose();
            ((IDisposable)rootForm).Dispose();
        }
    }

    [Fact]
    public void GetParent_TraversalWithUnknownFocus_TerminatesImmediately()
    {
        // When NativeMethods.GetFocus() answers 4096 (unknown handle):
        nint handle = (nint)4096;
        object? control = null;
        int loopCount = 0;

        var controlType = ForkAssembly.GetType("System.Windows.Forms.Control")!;
        var fromHandleMethod = controlType.GetMethod("FromHandle", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(IntPtr) }, null)!;

        while (handle != IntPtr.Zero && loopCount < 100)
        {
            loopCount++;
            handle = CallGetParent(handle);
            if (handle != IntPtr.Zero)
            {
                control = fromHandleMethod.Invoke(null, new object[] { handle });
                if (control != null) break;
            }
        }

        // Must terminate in 1 iteration and never infinite-loop
        Assert.Equal(1, loopCount);
        Assert.Equal(IntPtr.Zero, handle);
        Assert.Null(control);
    }

    [Fact]
    public void GetParent_DisposedControl_ReturnsNullWithoutThrowing()
    {
        var (form, handle) = CreateForkControl("System.Windows.Forms.Form");
        ((IDisposable)form).Dispose();

        var ex = Record.Exception(() =>
        {
            nint parent = CallGetParent(handle);
            Assert.Equal(IntPtr.Zero, parent);
        });

        Assert.Null(ex);
    }
}
