using Mono.Cecil;
using Mono.Cecil.Cil;

// SF09 (SF1120-CRASHQA-SF0809): docking boot dies in
// SerializersManager.GetSerializer at Assembly.GetTypes() with
// FileNotFoundException (Microsoft.DotNet.DesignTools.Server); the
// method only catches ReflectionTypeLoadException, so the throw kills
// Config..cctor -> InitializeComponent -> Form1..ctor. The rule adds a
// FileNotFoundException catch on the same try storing an empty type
// array (the downstream scan then takes the default-serializer
// fallback). This pins the shipped docking input: both catches guard
// the GetTypes call and join at the same post-try instruction.
public sealed class SerializersManagerGuardTests
{
    [Fact]
    public void GetTypes_Guarded_By_Both_Catches()
    {
        string input = @"E:\AI\Code\DesktopWasm\inputs\winforms\syncfusion-staging\syncfusion-docking\Syncfusion.Shared.Base.dll";
        Assert.True(File.Exists(input), "Docking staging input missing: " + input);
        string cpuDll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "WfWasm.PdnCpu", "bin", "Debug", "net9.0", "WfWasm.PdnCpu.dll"));
        string tempOut = Path.Combine(Path.GetTempPath(), $"SfSer_{Guid.NewGuid():N}.dll");
        try
        {
            WfWasm.Rewrite.AssemblyRewriter.Rewrite(
                input, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            using var module = AssemblyDefinition.ReadAssembly(tempOut).MainModule;
            var sm = module.GetType("Syncfusion.XmlSerializersCreator.SerializersManager");
            Assert.NotNull(sm);
            var target = sm.Methods.FirstOrDefault(m =>
                m.Name == "GetSerializer" && m.IsStatic && m.Parameters.Count == 1 && m.HasBody);
            Assert.NotNull(target);
            ExceptionHandler? rtle = null;
            ExceptionHandler? fnfe = null;
            foreach (var h in target.Body.ExceptionHandlers)
            {
                if (h.HandlerType == ExceptionHandlerType.Catch && h.CatchType is not null)
                {
                    if (h.CatchType.FullName == "System.Reflection.ReflectionTypeLoadException")
                        rtle = h;
                    if (h.CatchType.FullName == "System.IO.FileNotFoundException")
                        fnfe = h;
                }
            }
            Assert.NotNull(rtle);
            Assert.NotNull(fnfe);
            Assert.Same(rtle.TryStart, fnfe.TryStart);
            Assert.Same(rtle.TryEnd, fnfe.TryEnd);
            Instruction? getTypes = null;
            foreach (var ins in target.Body.Instructions)
            {
                if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                    && ins.Operand is MethodReference mr && mr.Name == "GetTypes" && mr.Parameters.Count == 0)
                    getTypes = ins;
            }
            Assert.NotNull(getTypes);
            int gi = target.Body.Instructions.IndexOf(getTypes);
            Assert.True(gi >= target.Body.Instructions.IndexOf(rtle.TryStart)
                && gi < target.Body.Instructions.IndexOf(rtle.TryEnd));
            Instruction? rtleLeave = null;
            int hi0 = target.Body.Instructions.IndexOf(rtle.HandlerStart);
            int hi1 = rtle.HandlerEnd is null
                ? target.Body.Instructions.Count
                : target.Body.Instructions.IndexOf(rtle.HandlerEnd);
            for (int k = hi0; k >= 0 && k < hi1; k++)
            {
                var ins = target.Body.Instructions[k];
                if ((ins.OpCode == OpCodes.Leave || ins.OpCode == OpCodes.Leave_S)
                    && ins.Operand is Instruction t)
                {
                    rtleLeave = t;
                    break;
                }
            }
            Assert.NotNull(rtleLeave);
            bool fnfeLeavesToJoin = false;
            int fi0 = target.Body.Instructions.IndexOf(fnfe.HandlerStart);
            int fi1 = fnfe.HandlerEnd is null
                ? target.Body.Instructions.Count
                : target.Body.Instructions.IndexOf(fnfe.HandlerEnd);
            for (int k = fi0; k >= 0 && k < fi1; k++)
            {
                var ins = target.Body.Instructions[k];
                if ((ins.OpCode == OpCodes.Leave || ins.OpCode == OpCodes.Leave_S)
                    && ins.Operand is Instruction t && t == rtleLeave)
                    fnfeLeavesToJoin = true;
            }
            Assert.True(fnfeLeavesToJoin, "FNFE handler must join at the post-try instruction");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
