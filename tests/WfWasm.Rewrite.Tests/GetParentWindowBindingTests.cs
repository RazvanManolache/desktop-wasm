using Mono.Cecil;
using Mono.Cecil.Cil;

// SF01-KEYTRACE transfer (reports/SF01-KEYTRACE-20261102-dw-runtime.md):
// every traced popup close logged
// MissingMethodException: WasmEnvironment.GetParentWindow(intptr) even
// though the served rewritten Syncfusion.Shared.Base holds the MemberRef
// and the fork defines the method. Bundle forensics pinned it as mixed
// generation: the served fork (WfWasm.Host.Server copy, ACA4B44C, same
// bytes as inputs/winforms/rewritten-small) predates GetParentWindow,
// while the vendor rewrite is fresh. This pins the in-tree contract so
// the reference can never silently drift from the fork again: every
// GetParentWindow MemberRef the rewriter emits must resolve to a real
// static IntPtr(IntPtr) method on the CURRENTLY BUILT fork assembly.
public sealed class GetParentWindowBindingTests
{
    [Fact]
    public void Rewritten_Reference_Resolves_Against_Built_Fork()
    {
        string input = @"E:\AI\Code\DesktopWasm\inputs\winforms\syncfusion-staging\syncfusion-word\Syncfusion.Shared.Base.dll";
        Assert.True(File.Exists(input), "Word staging input missing: " + input);
        string forkDll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "WfWasm.WinForms", "bin", "Debug", "net9.0", "WfWasm.WinForms.dll"));
        Assert.True(File.Exists(forkDll), "fork not built (build WfWasm.WinForms first): " + forkDll);
        string cpuDll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "WfWasm.PdnCpu", "bin", "Debug", "net9.0", "WfWasm.PdnCpu.dll"));
        string tempOut = Path.Combine(Path.GetTempPath(), $"GpBind_{Guid.NewGuid():N}.dll");
        try
        {
            WfWasm.Rewrite.AssemblyRewriter.Rewrite(
                input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            using var module = AssemblyDefinition.ReadAssembly(tempOut).MainModule;
            var refs = new List<MethodReference>();
            var stack = new Stack<TypeDefinition>(module.Types);
            while (stack.Count > 0)
            {
                var type = stack.Pop();
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    foreach (var ins in method.Body.Instructions)
                    {
                        if (ins.Operand is MethodReference mr
                            && mr.Name == "GetParentWindow"
                            && mr.DeclaringType.FullName == "System.Windows.Forms.WasmEnvironment")
                            refs.Add(mr);
                    }
                }
                foreach (var nested in type.NestedTypes)
                    stack.Push(nested);
            }
            Assert.NotEmpty(refs);
            using var fork = AssemblyDefinition.ReadAssembly(forkDll).MainModule;
            var env = fork.GetType("System.Windows.Forms.WasmEnvironment");
            Assert.NotNull(env);
            var target = env.Methods.FirstOrDefault(m =>
                m.Name == "GetParentWindow" && m.IsStatic && m.Parameters.Count == 1
                && m.Parameters[0].ParameterType.FullName == "System.IntPtr"
                && m.ReturnType.FullName == "System.IntPtr");
            Assert.NotNull(target);
            foreach (var r in refs)
            {
                Assert.Equal("WfWasm.WinForms", r.DeclaringType.Scope.Name);
                Assert.Equal(fork.Assembly.Name.Name, r.DeclaringType.Scope.Name);
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
