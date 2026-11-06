using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

namespace WfWasm.Rewrite.Tests;

// RVW3293-VEN8: masked keychar fault on the true session. Served VFY8d
// proves OnKeyDown(1..5,Enter) reaches maskedEditBox1 while type_diff and
// commit stay 0.0. Read-only IL trace pins the fork-side half of the drop:
// the vendor char pipeline (MaskedEditBox.ProcessKeyMessage -> ... ->
// SetTextBoxText) pushes display text ONLY through its native
// SendMessage P/Invoke (StringToHGlobalAuto + SendMessage + FreeHGlobal),
// and StubModulePInvokes rewrites that P/Invoke to a default-return stub
// with no forward to the fork PInvokeCore.SendMessage emulation (where the
// VEN1 WM_SETTEXT arm lives, served-unwired: zero callers). This test pins
// the severed shape directly against the staged vendor input. The forward
// fix (route SendMessage to the emulation) turns the Trailing assertion
// red-first: a forwarded body must contain a call.
public sealed class MaskedEditVendorPushSeveredTests
{
    private static string StagingDir()
    {
        string dir = @"E:\AI\Code\DesktopWasm\inputs\winforms\syncfusion-staging\syncfusion-masked-edit";
        Assert.True(Directory.Exists(dir), "staged masked-edit inputs missing: " + dir);
        return dir;
    }

    private static ModuleDefinition LoadSharedBase()
    {
        string input = Path.Combine(StagingDir(), "Syncfusion.Shared.Base.dll");
        Assert.True(File.Exists(input), "staged vendor input missing: " + input);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(StagingDir());
        string shared = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet", "shared");
        foreach (string fw in new[] { "Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App" })
        {
            string root = Path.Combine(shared, fw);
            if (Directory.Exists(root))
                foreach (string dir in Directory.GetDirectories(root))
                    resolver.AddSearchDirectory(dir);
        }
        return AssemblyDefinition.ReadAssembly(input,
            new ReaderParameters { AssemblyResolver = resolver }).MainModule;
    }

    private static string ForkDll()
    {
        string root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        foreach (string cfg in new[] { "Debug", "Release" })
        {
            string p = Path.Combine(root, "src", "WfWasm.WinForms",
                "bin", cfg, "net9.0", "WfWasm.WinForms.dll");
            if (File.Exists(p))
                return p;
        }
        Assert.Fail("built WfWasm.WinForms.dll missing (build src/WfWasm.WinForms first)");
        throw new InvalidOperationException("unreachable");
    }

    private static MethodDefinition FindVendorSendMessage(ModuleDefinition module)
    {
        var t = module.GetType("Syncfusion.Runtime.InteropServices.NativeMethods");
        Assert.NotNull(t);
        var m = t!.Methods.FirstOrDefault(m => m.Name == "SendMessage"
            && m.Parameters.Count == 4
            && m.Parameters[0].ParameterType.FullName == "System.IntPtr"
            && m.Parameters[1].ParameterType.FullName == "System.Int32"
            && m.Parameters[2].ParameterType.FullName == "System.IntPtr"
            && m.Parameters[3].ParameterType.FullName == "System.IntPtr");
        Assert.NotNull(t.FullName + "::SendMessage(IntPtr,Int32,IntPtr,IntPtr)");
        return m!;
    }

    [Fact]
    public void Vendor_SendMessage_Push_Is_Severed_By_Stub()
    {
        ModuleDefinition module = LoadSharedBase();
        MethodDefinition send = FindVendorSendMessage(module);

        // Pre-shape: the vendor display-text push is a native P/Invoke.
        Assert.True(send.IsPInvokeImpl);

        AssemblyRewriter.StubModulePInvokes(module);

        // Post-shape: stubbed to a fake-success body with no forward.
        // IntPtr returns take the native-address branch (ldc.i4 0x1000):
        // the vendor SetTextBoxText push therefore "succeeds" silently
        // while the text lands nowhere; the fork PInvokeCore.SendMessage
        // WM_SETTEXT arm never fires.
        Assert.False(send.IsPInvokeImpl);
        Assert.NotNull(send.Body);
        Assert.DoesNotContain(send.Body!.Instructions,
            i => i.Operand is MethodReference);
        Assert.Contains(send.Body.Instructions, i => i.OpCode == OpCodes.Ldc_I4
            && i.Operand is int v && v == 0x1000);
        Assert.Contains(send.Body.Instructions, i => i.OpCode == OpCodes.Ret);
    }

    [Fact]
    public void Vendor_SendMessage_Forwards_With_ForkPath()
    {
        ModuleDefinition module = LoadSharedBase();
        MethodDefinition send = FindVendorSendMessage(module);
        Assert.True(send.IsPInvokeImpl);

        // The forward fix routes the vendor push into the fork emulation
        // (VEN1 WM_SETTEXT arm): the stubbed body must carry the call.
        AssemblyRewriter.StubModulePInvokes(module, null, ForkDll());

        Assert.False(send.IsPInvokeImpl);
        Assert.Contains(send.Body!.Instructions, i => i.Operand is MethodReference mr
            && mr.Name == "SendMessage"
            && mr.DeclaringType.FullName == "Windows.Win32.ForkSendMessage");
        Assert.Contains(send.Body.Instructions, i => i.OpCode == OpCodes.Ret);
        // The rewritten module must still serialize (bad tokens or an
        // unbalanced stack fail here, not served).
        using var ms = new MemoryStream();
        module.Write(ms);
        Assert.True(ms.Length > 0);
    }
}
