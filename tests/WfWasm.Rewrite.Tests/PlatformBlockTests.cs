using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;

public sealed class PlatformBlockTests
{
    private static string TempDir(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "wfrw-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteCryptoModule()
    {
        var asmName = new AssemblyNameDefinition("CryptoApp", new Version(1, 0, 0, 0));
        var asm = AssemblyDefinition.CreateAssembly(asmName, "CryptoApp.dll", ModuleKind.Dll);
        var module = asm.MainModule;
        var mscorlib = new AssemblyNameReference("mscorlib", new Version(4, 0, 0, 0));
        var rsaCsp = new TypeReference("System.Security.Cryptography", "RSACryptoServiceProvider", module, mscorlib);
        var cspInfo = new TypeReference("System.Security.Cryptography", "CspKeyContainerInfo", module, mscorlib);
        var ctor = new MethodReference(".ctor", module.TypeSystem.Void, rsaCsp) { HasThis = true };
        var getter = new MethodReference("get_CspKeyContainerInfo", cspInfo, rsaCsp) { HasThis = true };
        var type = new TypeDefinition("Fixtures", "CryptoUser",
            TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(type);
        var use = new MethodDefinition("Use", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
        type.Methods.Add(use);
        var il = use.Body.GetILProcessor();
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Callvirt, getter);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        string path = Path.Combine(TempDir("crypto"), "CryptoApp.dll");
        module.Write(path);
        return path;
    }

    [Fact]
    public void CspCtorAndKeyStoreSurface_ArePlatformBlocked()
    {
        using var asm = AssemblyDefinition.ReadAssembly(WriteCryptoModule());
        var module = asm.MainModule;
        int before = module.GetMemberReferences().Count();

        var blocks = GapAnalyzer.AnalyzePlatformBlocks(module);

        Assert.Contains(blocks, b =>
            b.DeclaringType == "System.Security.Cryptography.RSACryptoServiceProvider" &&
            b.Member == ".ctor" &&
            b.Reason.Contains("CAPI") &&
            b.Reason.Contains("PlatformNotSupportedException"));
        Assert.Contains(blocks, b =>
            b.DeclaringType == "System.Security.Cryptography.RSACryptoServiceProvider" &&
            b.Member == "get_CspKeyContainerInfo");
        // Pure diagnostic: the module is untouched.
        Assert.Equal(before, module.GetMemberReferences().Count());
    }

    [Fact]
    public void AbstractRsaContract_IsNotBlocked()
    {
        var asmName = new AssemblyNameDefinition("CleanApp", new Version(1, 0, 0, 0));
        var asm = AssemblyDefinition.CreateAssembly(asmName, "CleanApp.dll", ModuleKind.Dll);
        var module = asm.MainModule;
        var mscorlib = new AssemblyNameReference("mscorlib", new Version(4, 0, 0, 0));
        var rsa = new TypeReference("System.Security.Cryptography", "RSA", module, mscorlib);
        var create = new MethodReference("Create", rsa, rsa);
        var type = new TypeDefinition("Fixtures", "CleanUser",
            TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(type);
        var use = new MethodDefinition("Use",
            Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
        type.Methods.Add(use);
        var il = use.Body.GetILProcessor();
        il.Emit(OpCodes.Call, create);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        string path = Path.Combine(TempDir("clean"), "CleanApp.dll");
        module.Write(path);
        using var read = AssemblyDefinition.ReadAssembly(path);

        Assert.Empty(GapAnalyzer.AnalyzePlatformBlocks(read.MainModule));
    }
}
