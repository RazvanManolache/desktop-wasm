using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

public sealed class InvertRectRewriteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvertRect_reads_native_device_edges_and_calls_managed_target(bool lowerCase)
    {
        using var assembly = Build(lowerCase, malformed: false);
        var module = assembly.MainModule;
        AssemblyRewriter.StubModulePInvokes(module);
        var method = module.GetType("Probe.Native").Methods.Single();
        Assert.False(method.IsPInvokeImpl);
        var call = Assert.Single(method.Body.Instructions.Where(i => i.OpCode == OpCodes.Call));
        var target = Assert.IsType<MethodReference>(call.Operand);
        Assert.Equal("System.Drawing.Graphics", target.DeclaringType.FullName);
        Assert.Equal("InvertHdcRect", target.Name);
        Assert.Equal("WfWasm.Drawing", target.DeclaringType.Scope.Name);
        Assert.Equal(5, target.Parameters.Count);
        Assert.Equal(new[] { "left", "top", "right", "bottom" },
            method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldfld)
                .Select(i => ((FieldReference)i.Operand).Name.ToLowerInvariant()));
    }

    [Fact]
    public void Unsupported_rectangle_shape_fails_instead_of_claiming_success()
    {
        using var assembly = Build(false, malformed: true);
        AssemblyRewriter.StubModulePInvokes(assembly.MainModule);
        var code = assembly.MainModule.GetType("Probe.Native").Methods.Single().Body.Instructions;
        Assert.Equal(OpCodes.Ldc_I4_0, code[0].OpCode);
        Assert.Equal(OpCodes.Ret, code[1].OpCode);
    }

    private static AssemblyDefinition Build(bool lowerCase, bool malformed)
    {
        var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("InvertProbe", new Version(1, 0)), "InvertProbe", ModuleKind.Dll);
        var m = assembly.MainModule;
        m.AssemblyReferences.Add(new AssemblyNameReference("System.Drawing.Common", new Version(9, 0)));
        var rect = new TypeDefinition("Probe", "RECT", TypeAttributes.Public | TypeAttributes.SequentialLayout | TypeAttributes.Sealed,
            m.ImportReference(typeof(ValueType)));
        foreach (string name in new[] { "Left", "Top", "Right", "Bottom" }.Take(malformed ? 3 : 4))
            rect.Fields.Add(new FieldDefinition(lowerCase ? name.ToLowerInvariant() : name, FieldAttributes.Public, m.TypeSystem.Int32));
        m.Types.Add(rect);
        var native = new TypeDefinition("Probe", "Native", TypeAttributes.Public, m.TypeSystem.Object);
        m.Types.Add(native);
        var method = new MethodDefinition("InvertRect", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PInvokeImpl, m.TypeSystem.Boolean);
        method.Parameters.Add(new ParameterDefinition(m.TypeSystem.IntPtr));
        method.Parameters.Add(new ParameterDefinition(new ByReferenceType(rect)));
        var dll = new ModuleReference("user32.dll"); m.ModuleReferences.Add(dll);
        method.PInvokeInfo = new PInvokeInfo(PInvokeAttributes.CallConvWinapi, "InvertRect", dll);
        native.Methods.Add(method);
        return assembly;
    }
}
