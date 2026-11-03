using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WfWasm.Host.Server;

/// <summary>Explicit original-application startup adapters for the browser host.</summary>
public static class BenchmarkStartupCompatibility
{
    public const string CacheVersion = "benchmark-startup-v3";

    public static byte[] Rewrite(byte[] bytes, string app, IEnumerable<string>? searchFolders = null, bool enabled = false)
    {
        // Startup prototypes have not passed original-app acceptance yet.
        if (!enabled) return bytes;
        if (app is not ("benchmark-linqpad5" or "benchmark-pkhex" or "benchmark-openpdn")) return bytes;
        using var input = new MemoryStream(bytes, writable: false);
        using var resolver = new WfWasm.Rewrite.FrameworkResolver(AppContext.BaseDirectory, searchFolders);
        using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
        if (app == "benchmark-openpdn")
        {
            var itemFactory = assembly.MainModule.GetType("PaintDotNet.SystemLayer.PropertyItem2")?.Methods
                .SingleOrDefault(m => m.Name == "GetPropertyItem" && m.Parameters.Count == 0 && m.IsStatic);
            if (itemFactory is not null && itemFactory.ReturnType.FullName == "System.Drawing.Imaging.PropertyItem")
            {
                // Desktop code decodes a sacrificial JPEG solely to obtain an
                // empty PropertyItem (GDI+'s constructor is internal). Our
                // managed metadata type has a public constructor; its fields
                // are filled by the original ToPropertyItem immediately after.
                itemFactory.Body.ExceptionHandlers.Clear();
                itemFactory.Body.Variables.Clear();
                itemFactory.Body.Instructions.Clear();
                var constructor = new MethodReference(".ctor", assembly.MainModule.TypeSystem.Void, itemFactory.ReturnType) { HasThis = true };
                itemFactory.Body.GetILProcessor().Emit(OpCodes.Newobj, constructor);
                itemFactory.Body.GetILProcessor().Emit(OpCodes.Ret);
                return Write(assembly);
            }
            var resources = assembly.MainModule.GetType("PaintDotNet.PdnResources");
            var getter = resources?.Methods.SingleOrDefault(m => m.Name == "get_ResourcesDir" && m.Parameters.Count == 0);
            if (getter is null) return bytes;
            // Assemblies are loaded from memory; Location has no directory.
            // Original loose resources are mirrored into the browser MEMFS root.
            getter.Body.ExceptionHandlers.Clear();
            getter.Body.Variables.Clear();
            getter.Body.Instructions.Clear();
            getter.Body.GetILProcessor().Emit(OpCodes.Ldstr, "/");
            getter.Body.GetILProcessor().Emit(OpCodes.Ret);
            return Write(assembly);
        }
        if (app == "benchmark-pkhex")
        {
            var initializer = assembly.MainModule.GetType("PKHeX.WinForms.Program")?.Methods.SingleOrDefault(m => m.Name == ".cctor");
            if (initializer is null || !initializer.HasBody) return bytes;
            bool changed = false;
            foreach (var instruction in initializer.Body.Instructions)
                if (instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference call
                    && call.DeclaringType.FullName == "System.Windows.Forms.Application"
                    && call.Parameters.Count == 1
                    && ((call.Name == "SetUnhandledExceptionMode" && instruction.Previous?.OpCode == OpCodes.Ldc_I4_2)
                        || (call.Name == "SetCompatibleTextRenderingDefault" && instruction.Previous?.OpCode == OpCodes.Ldc_I4_0)))
                {
                    // The browser host already catches UI exceptions. Host
                    // controls predate this session; the Windows API's "too
                    // late" guard must not poison the app's settings cctor.
                    instruction.OpCode = OpCodes.Pop;
                    instruction.Operand = null;
                    changed = true;
                }
            return changed ? Write(assembly) : bytes;
        }
        if (assembly.Name.Name != "LINQPad") return bytes;
        var type = assembly.MainModule.GetType("LINQPad.UI.MainForm");
        if (type is null || type.Methods.Any(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0)) return bytes;
        var startup = type.Methods.SingleOrDefault(m => m.IsConstructor && !m.IsStatic
            && m.Parameters.Count == 2 && m.Parameters[0].ParameterType.FullName == "System.String"
            && m.Parameters[1].ParameterType.FullName == "System.Boolean");
        if (startup is null) return bytes;
        // Open the real main window with no query to load or execute. Calling
        // the original constructor preserves its initialization and checks.
        var entry = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig
            | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, assembly.MainModule.TypeSystem.Void);
        type.Methods.Add(entry);
        var il = entry.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, startup);
        il.Emit(OpCodes.Ret);
        return Write(assembly);
    }

    private static byte[] Write(AssemblyDefinition assembly)
    {
        using var output = new MemoryStream();
        assembly.Write(output);
        return output.ToArray();
    }
}
