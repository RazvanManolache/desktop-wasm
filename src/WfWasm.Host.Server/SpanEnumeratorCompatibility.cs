using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WfWasm.Host.Server;

/// <summary>Preserve .NET 10 span foreach semantics on the .NET 9 browser runtime.</summary>
public static class SpanEnumeratorCompatibility
{
    // .NET 10 added IEnumerator/IDisposable to these ref structs. Their
    // Dispose implementations are empty; .NET 9 has the same iteration APIs
    // but no IDisposable interface, so the new constrained call fails to JIT.
    // Sources: dotnet/runtime v10.0.0 System/ReadOnlySpan.cs and
    // System/Text/SpanLineEnumerator.cs. Never remove arbitrary Dispose calls.
    public const string CacheVersion = "span-enumerator-dispose-v2";

    public static byte[] Rewrite(byte[] bytes, IEnumerable<string>? searchFolders = null)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var resolver = new WfWasm.Rewrite.FrameworkResolver(AppContext.BaseDirectory, searchFolders);
        using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
        bool changed = false;
        foreach (var type in Types(assembly.MainModule.Types))
        foreach (var method in type.Methods)
        {
            if (!method.HasBody) continue;
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Constrained || instruction.Operand is not TypeReference receiver
                    || !IsKnownEnumerator(receiver)) continue;
                var call = instruction.Next;
                if (call?.OpCode != OpCodes.Callvirt || call.Operand is not MethodReference target
                    || target.DeclaringType.FullName != "System.IDisposable" || target.Name != "Dispose"
                    || target.Parameters.Count != 0 || target.ReturnType.FullName != "System.Void") continue;
                // Leave branch targets and finally boundaries intact. The
                // address already on the evaluation stack still gets consumed.
                instruction.OpCode = OpCodes.Nop;
                instruction.Operand = null;
                call.OpCode = OpCodes.Pop;
                call.Operand = null;
                changed = true;
            }
        }
        if (!changed) return bytes;
        using var output = new MemoryStream();
        assembly.Write(output);
        return output.ToArray();
    }

    private static bool IsKnownEnumerator(TypeReference type)
    {
        var definition = type is GenericInstanceType generic ? generic.ElementType : type;
        return definition.Scope.Name is "System.Runtime" or "System.Private.CoreLib" or "mscorlib" or "System.Memory"
            && definition.FullName is "System.ReadOnlySpan`1/Enumerator" or "System.Text.SpanLineEnumerator";
    }

    private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Types(type.NestedTypes)) yield return nested;
        }
    }
}
