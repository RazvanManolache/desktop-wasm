using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WfWasm.Host.Server;

/// <summary>Browser UI scheduling has no native thread-priority API.</summary>
public static class BrowserThreadPriorityCompatibility
{
    public const string CacheVersion = "openpdn-browser-thread-priority-v1";

    public static byte[] Rewrite(byte[] bytes, string app, bool enabled = false)
    {
        if (!enabled) return bytes;
        if (app != "benchmark-openpdn") return bytes;
        using var input = new MemoryStream(bytes, writable: false);
        using var resolver = new WfWasm.Rewrite.FrameworkResolver(AppContext.BaseDirectory);
        using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
        bool changed = false;
        foreach (var type in assembly.MainModule.GetTypes())
        foreach (var method in type.Methods)
        {
            if (!method.HasBody) continue;
            var il = method.Body.GetILProcessor();
            foreach (var instruction in method.Body.Instructions.ToArray())
            {
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) continue;
                if (instruction.Operand is not MethodReference call || !call.HasThis
                    || call.DeclaringType.FullName != "System.Threading.Thread") continue;
                if (call.Name == "get_Priority" && call.Parameters.Count == 0
                    && call.ReturnType.FullName == "System.Threading.ThreadPriority")
                {
                    instruction.OpCode = OpCodes.Pop;
                    instruction.Operand = null;
                    il.InsertAfter(instruction, il.Create(OpCodes.Ldc_I4_2)); // Normal
                    changed = true;
                }
                else if (call.Name == "set_Priority" && call.Parameters.Count == 1
                    && call.Parameters[0].ParameterType.FullName == "System.Threading.ThreadPriority")
                {
                    instruction.OpCode = OpCodes.Pop;
                    instruction.Operand = null;
                    il.InsertAfter(instruction, il.Create(OpCodes.Pop));
                    changed = true;
                }
            }
        }
        if (!changed) return bytes;
        using var output = new MemoryStream();
        assembly.Write(output);
        return output.ToArray();
    }
}
