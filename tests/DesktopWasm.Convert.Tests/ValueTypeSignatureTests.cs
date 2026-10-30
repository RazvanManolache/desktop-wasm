using DesktopWasm.Convert;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DesktopWasm.Convert.Tests;

public sealed class ValueTypeSignatureTests
{
    [Fact]
    public void Remapped_enum_returns_keep_value_type_identity_in_method_operands()
    {
        var work = Path.Combine(Path.GetTempPath(), "wpf-enum-signature-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var input = Path.Combine(work, "EnumFixture.dll");
            using (var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("EnumFixture", new Version(1, 0)), "EnumFixture", ModuleKind.Dll))
            {
                var module = assembly.MainModule;
                var framework = new AssemblyNameReference("PresentationFramework", new Version(4, 0));
                module.AssemblyReferences.Add(framework);
                var result = new TypeReference("System.Windows", "MessageBoxResult", module, framework, true);
                var box = new TypeReference("System.Windows", "MessageBox", module, framework);
                var show = new MethodReference("Show", result, box);
                show.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                var owner = new TypeDefinition("Fixture", "Dialog", TypeAttributes.Public, module.TypeSystem.Object);
                module.Types.Add(owner);
                var method = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static, result);
                owner.Methods.Add(method);
                var il = method.Body.GetILProcessor();
                il.Emit(OpCodes.Ldstr, "Hello"); il.Emit(OpCodes.Call, show); il.Emit(OpCodes.Ret);
                assembly.Write(input);
            }
            File.WriteAllText(Path.Combine(work, "MainWindow.xaml"), "<Window Title=\"Signature fixture\"/>");
            var output = Path.Combine(work, "converted");
            var converted = AssemblyConverter.TryConvert(input, work, output);
            Assert.True(converted.Ok, string.Join(", ", converted.Gaps));
            using var rewritten = AssemblyDefinition.ReadAssembly(Path.Combine(output, "EnumFixture.dll"));
            var run = rewritten.MainModule.Types.Single(t => t.Name == "Dialog").Methods.Single();
            var call = Assert.IsType<MethodReference>(run.Body.Instructions.Single(i => i.OpCode == OpCodes.Call).Operand);
            Assert.Equal("DesktopWasm.Wpf.MessageBoxResult", call.ReturnType.FullName);
            Assert.True(call.ReturnType.IsValueType);
            Assert.True(run.ReturnType.IsValueType);
        }
        finally { Directory.Delete(work, recursive: true); }
    }
}
