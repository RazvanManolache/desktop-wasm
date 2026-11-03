using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;

public sealed class FiddlerShapeGuardTests
{
    private static ModuleDefinition BuildModule(
        string assemblyName,
        bool withFields,
        bool withLocals,
        bool withHandler,
        Func<MethodDefinition, MethodDefinition, ILProcessor, Instruction>? buildCalls = null,
        string[]? helpers = null)
    {
        var asmName = new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0));
        var asm = AssemblyDefinition.CreateAssembly(asmName, assemblyName + ".dll", ModuleKind.Dll);
        var module = asm.MainModule;
        var global = module.GetType("<Module>");
        Assert.NotNull(global);

        foreach (string helper in helpers ?? new[] { "RateSetter", "ListSetter" })
        {
            if (global.Methods.Any(m => m.Name == helper))
                continue;
            var def = new MethodDefinition(
                helper,
                Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Void);
            var il = def.Body.GetILProcessor();
            il.Emit(OpCodes.Ret);
            global.Methods.Add(def);
        }

        if (withFields)
        {
            global.Fields.Add(new FieldDefinition(
                "GuardField",
                Mono.Cecil.FieldAttributes.Private | Mono.Cecil.FieldAttributes.Static,
                module.TypeSystem.Int32));
        }

        var cctor = global.Methods.FirstOrDefault(m => m.Name == ".cctor");
        if (cctor is not null)
            global.Methods.Remove(cctor);
        cctor = new MethodDefinition(
            ".cctor",
            Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static
                | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        global.Methods.Add(cctor);
        var body = cctor.Body;
        var ilp = body.GetILProcessor();

        if (withLocals)
            body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));

        if (buildCalls is not null)
        {
            var rate = global.Methods.First(m => m.Name == "RateSetter");
            var list = global.Methods.FirstOrDefault(m => m.Name == "ListSetter");
            var ret = ilp.Create(OpCodes.Ret);
            var anchor = buildCalls(rate, list!, ilp);
            if (anchor is not null)
                ilp.Append(anchor);
            else
                ilp.Append(ret);

            if (withHandler)
            {
                var first = body.Instructions.First();
                var last = body.Instructions.Last();
                body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
                {
                    TryStart = first,
                    TryEnd = last,
                    HandlerStart = last,
                    HandlerEnd = last,
                    CatchType = module.ImportReference(typeof(Exception)),
                });
            }

            return module;
        }

        // Default supported shape: RateSetter then ListSetter then ret.
        var r = global.Methods.First(m => m.Name == "RateSetter");
        var l = global.Methods.First(m => m.Name == "ListSetter");
        ilp.Emit(OpCodes.Call, r);
        ilp.Emit(OpCodes.Call, l);
        ilp.Emit(OpCodes.Ret);

        if (withHandler)
        {
            var first = body.Instructions.First();
            var last = body.Instructions.Last();
            body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                TryStart = first,
                TryEnd = last,
                HandlerStart = last,
                HandlerEnd = last,
                CatchType = module.ImportReference(typeof(Exception)),
            });
        }

        return module;
    }

    private static int InstructionCount(ModuleDefinition module)
    {
        var cctor = module.GetType("<Module>").Methods.First(m => m.Name == ".cctor");
        return cctor.Body.Instructions.Count;
    }

    [Fact]
    public void SyntheticSupportedShape_NeutralizedAndIdempotent()
    {
        var module = BuildModule("Fiddler", withFields: false, withLocals: false, withHandler: false);
        Assert.Equal(2, AssemblyRewriter.NeutralizeFiddlerModuleInit(module));
        var cctor = module.GetType("<Module>").Methods.First(m => m.Name == ".cctor");
        Assert.Single(cctor.Body.Instructions);
        Assert.Equal(OpCodes.Ret, cctor.Body.Instructions[0].OpCode);
        Assert.Equal(0, AssemblyRewriter.NeutralizeFiddlerModuleInit(module));
    }

    [Fact]
    public void ExtraInitCall_RejectedWithoutMutation()
    {
        var module = BuildModule("Fiddler", withFields: false, withLocals: false, withHandler: false,
            buildCalls: (rate, list, ilp) =>
            {
                var mod = rate.Module;
                var extra = new MethodDefinition(
                    "ExtraInit",
                    Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static,
                    mod.TypeSystem.Void);
                var eil = extra.Body.GetILProcessor();
                eil.Emit(OpCodes.Ret);
                mod.GetType("<Module>").Methods.Add(extra);
                ilp.Emit(OpCodes.Call, rate);
                ilp.Emit(OpCodes.Call, list);
                ilp.Emit(OpCodes.Call, extra);
                return ilp.Create(OpCodes.Ret);
            });
        int before = InstructionCount(module);
        var ex = Assert.Throws<InvalidOperationException>(() => AssemblyRewriter.NeutralizeFiddlerModuleInit(module));
        Assert.Contains("extra initialization call", ex.Message);
        Assert.Equal(before, InstructionCount(module));
    }

    [Fact]
    public void StateWrite_RejectedWithoutMutation()
    {
        var module = BuildModule("Fiddler", withFields: true, withLocals: false, withHandler: false,
            buildCalls: (rate, list, ilp) =>
            {
                var field = rate.Module.GetType("<Module>").Fields.First(f => f.Name == "GuardField");
                ilp.Emit(OpCodes.Call, rate);
                ilp.Emit(OpCodes.Call, list);
                ilp.Emit(OpCodes.Ldc_I4_0);
                ilp.Emit(OpCodes.Stsfld, field);
                return ilp.Create(OpCodes.Ret);
            });
        int before = InstructionCount(module);
        // Fields alone already reject; message names either fields or state write.
        var ex = Assert.Throws<InvalidOperationException>(() => AssemblyRewriter.NeutralizeFiddlerModuleInit(module));
        Assert.Contains("unsupported module-init shape", ex.Message);
        Assert.Equal(before, InstructionCount(module));
    }

    [Fact]
    public void ModuleFields_RejectedWithoutMutation()
    {
        var module = BuildModule("Fiddler", withFields: true, withLocals: false, withHandler: false);
        int before = InstructionCount(module);
        var ex = Assert.Throws<InvalidOperationException>(() => AssemblyRewriter.NeutralizeFiddlerModuleInit(module));
        Assert.Contains("field", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, InstructionCount(module));
    }

    [Fact]
    public void LocalsOrHandlers_RejectedWithoutMutation()
    {
        var withLocal = BuildModule("Fiddler", withFields: false, withLocals: true, withHandler: false);
        int beforeLocal = InstructionCount(withLocal);
        var exLocal = Assert.Throws<InvalidOperationException>(() => AssemblyRewriter.NeutralizeFiddlerModuleInit(withLocal));
        Assert.Contains("local", exLocal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(beforeLocal, InstructionCount(withLocal));

        var withEh = BuildModule("Fiddler", withFields: false, withLocals: false, withHandler: true);
        int beforeEh = InstructionCount(withEh);
        var exEh = Assert.Throws<InvalidOperationException>(() => AssemblyRewriter.NeutralizeFiddlerModuleInit(withEh));
        Assert.Contains("handler", exEh.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(beforeEh, InstructionCount(withEh));
    }

    [Fact]
    public void NonFiddlerModule_RejectedWithoutMutation()
    {
        var module = BuildModule("NotFiddler", withFields: false, withLocals: false, withHandler: false);
        int before = InstructionCount(module);
        var ex = Assert.Throws<InvalidOperationException>(() => AssemblyRewriter.NeutralizeFiddlerModuleInit(module));
        Assert.Contains("expected assembly 'Fiddler'", ex.Message);
        Assert.Equal(before, InstructionCount(module));
    }

    [Fact]
    public void UnknownHelper_RejectedWithoutMutation()
    {
        var module = BuildModule("Fiddler", withFields: false, withLocals: false, withHandler: false,
            buildCalls: (rate, list, ilp) =>
            {
                var mod2 = rate.Module;
                var unknown = new MethodDefinition(
                    "UnknownSetter",
                    Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static,
                    mod2.TypeSystem.Void);
                var uil = unknown.Body.GetILProcessor();
                uil.Emit(OpCodes.Ret);
                mod2.GetType("<Module>").Methods.Add(unknown);
                ilp.Emit(OpCodes.Call, rate);
                ilp.Emit(OpCodes.Call, unknown);
                return ilp.Create(OpCodes.Ret);
            },
            helpers: new[] { "RateSetter", "ListSetter", "UnknownSetter" });
        int before = InstructionCount(module);
        var ex = Assert.Throws<InvalidOperationException>(() => AssemblyRewriter.NeutralizeFiddlerModuleInit(module));
        Assert.Contains("unsupported module-init shape", ex.Message);
        Assert.Equal(before, InstructionCount(module));
    }

    [Fact]
    public void PartialTargetMatch_RejectedWithoutMutation()
    {
        var module = BuildModule("Fiddler", withFields: false, withLocals: false, withHandler: false,
            buildCalls: (rate, list, ilp) =>
            {
                ilp.Emit(OpCodes.Call, rate);
                return ilp.Create(OpCodes.Ret);
            });
        int before = InstructionCount(module);
        var ex = Assert.Throws<InvalidOperationException>(() => AssemblyRewriter.NeutralizeFiddlerModuleInit(module));
        Assert.Contains("RateSetter then ListSetter", ex.Message);
        Assert.Equal(before, InstructionCount(module));
    }

    [Fact]
    public void UnknownBranchPattern_RejectedWithoutMutation()
    {
        var module = BuildModule("Fiddler", withFields: false, withLocals: false, withHandler: false,
            buildCalls: (rate, list, ilp) =>
            {
                ilp.Emit(OpCodes.Call, rate);
                ilp.Emit(OpCodes.Call, list);
                var ret = ilp.Create(OpCodes.Ret);
                // Conditional branch over ret: not part of the supported
                // unconditional-br-over-junk shape.
                ilp.Emit(OpCodes.Ldc_I4_0);
                ilp.Emit(OpCodes.Brfalse, ret);
                return ret;
            });
        int before = InstructionCount(module);
        var ex = Assert.Throws<InvalidOperationException>(() => AssemblyRewriter.NeutralizeFiddlerModuleInit(module));
        Assert.Contains("unsupported module-init shape", ex.Message);
        Assert.Equal(before, InstructionCount(module));
    }
}
