using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Host.Server;

namespace WfWasm.Host.Server.Tests;

public sealed class SpanEnumeratorCompatibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Net10_Span_Dispose_Executes_On_Net9_Without_Changing_Iteration(bool lines)
    {
        byte[] original = CreateSpanLoop(lines);
        var context = new AssemblyLoadContext(Guid.NewGuid().ToString(), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(SpanEnumeratorCompatibility.Rewrite(original)));
            var sum = assembly.GetType("Fixture")!.GetMethod("Sum")!;
            Assert.Equal(lines ? 3 : 10, sum.Invoke(null, [lines ? "a\nb\nc" : new ushort[] { 1, 2, 3, 4 }]));
            Assert.Equal(lines ? 1 : 0, sum.Invoke(null, [lines ? "" : Array.Empty<ushort>()]));
        }
        finally { context.Unload(); }
    }

    [Fact]
    public void Assemblies_Without_The_Exact_Framework_Dispose_Pattern_Keep_Their_Bytes()
    {
        byte[] original = File.ReadAllBytes(typeof(SpanEnumeratorCompatibilityTests).Assembly.Location);
        Assert.Same(original, SpanEnumeratorCompatibility.Rewrite(original));
    }

    [Fact]
    public void Application_Defined_Enumerator_Is_Not_Mistaken_For_The_Framework_Type()
    {
        byte[] original = CreateSpanLoop();
        using var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(original));
        var prefix = assembly.MainModule.Types.Single(t => t.Name == "Fixture").Methods[0]
            .Body.Instructions.Single(i => i.OpCode == OpCodes.Constrained);
        var receiver = (GenericInstanceType)prefix.Operand;
        receiver.ElementType.DeclaringType.Scope = new AssemblyNameReference("UserLibrary", new Version(1, 0));
        using var output = new MemoryStream();
        assembly.Write(output);
        byte[] spoofed = output.ToArray();
        Assert.Same(spoofed, SpanEnumeratorCompatibility.Rewrite(spoofed));
    }

    private static byte[] CreateSpanLoop(bool lines = false)
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("SpanFixture" + Guid.NewGuid().ToString("N"), new Version(1, 0)), "main", ModuleKind.Dll);
        var module = assembly.MainModule;
        var type = new TypeDefinition("", "Fixture", Mono.Cecil.TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(type);
        var method = new MethodDefinition("Sum", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Int32);
        method.Parameters.Add(new ParameterDefinition(module.ImportReference(lines ? typeof(string) : typeof(ushort[]))));
        type.Methods.Add(method);
        var spanType = lines ? typeof(ReadOnlySpan<char>) : typeof(ReadOnlySpan<ushort>);
        var enumeratorType = lines ? typeof(System.Text.SpanLineEnumerator) : spanType.GetMethod("GetEnumerator")!.ReturnType;
        var span = new VariableDefinition(module.ImportReference(spanType));
        var enumerator = new VariableDefinition(module.ImportReference(enumeratorType));
        var sum = new VariableDefinition(module.TypeSystem.Int32);
        method.Body.Variables.Add(span);
        method.Body.Variables.Add(enumerator);
        method.Body.Variables.Add(sum);
        method.Body.InitLocals = true;
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        if (lines) il.Emit(OpCodes.Call, module.ImportReference(typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!));
        else il.Emit(OpCodes.Newobj, module.ImportReference(spanType.GetConstructor([typeof(ushort[])])!));
        il.Emit(OpCodes.Stloc, span);
        il.Emit(lines ? OpCodes.Ldloc : OpCodes.Ldloca, span);
        il.Emit(OpCodes.Call, module.ImportReference(lines ? typeof(MemoryExtensions).GetMethod("EnumerateLines", [spanType])! : spanType.GetMethod("GetEnumerator")!));
        il.Emit(OpCodes.Stloc, enumerator);
        var check = il.Create(OpCodes.Ldloca, enumerator);
        var body = il.Create(OpCodes.Ldloc, sum);
        il.Emit(OpCodes.Br, check);
        il.Append(body);
        if (lines) il.Emit(OpCodes.Ldc_I4_1);
        else
        {
            il.Emit(OpCodes.Ldloca, enumerator);
            il.Emit(OpCodes.Call, module.ImportReference(enumeratorType.GetProperty("Current")!.GetMethod!));
            il.Emit(OpCodes.Ldind_U2);
        }
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, sum);
        il.Append(check);
        il.Emit(OpCodes.Call, module.ImportReference(enumeratorType.GetMethod("MoveNext")!));
        il.Emit(OpCodes.Brtrue, body);
        il.Emit(OpCodes.Ldloca, enumerator);
        var constraint = module.ImportReference(enumeratorType);
        if (lines)
        {
            var memoryScope = new AssemblyNameReference("System.Memory", new Version(4, 0, 2, 0)) { PublicKeyToken = Convert.FromHexString("CC7B13FFCD2DDD51") };
            module.AssemblyReferences.Add(memoryScope);
            constraint = new TypeReference("System.Text", "SpanLineEnumerator", module, memoryScope, true);
        }
        il.Emit(OpCodes.Constrained, constraint);
        il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(IDisposable).GetMethod("Dispose")!));
        il.Emit(OpCodes.Ldloc, sum);
        il.Emit(OpCodes.Ret);
        using var output = new MemoryStream();
        assembly.Write(output);
        return output.ToArray();
    }
}
