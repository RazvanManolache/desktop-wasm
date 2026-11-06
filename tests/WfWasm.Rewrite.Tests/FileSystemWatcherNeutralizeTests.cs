using Mono.Cecil;
using Mono.Cecil.Cil;

// FileSystemWatcher seam slice (runtime FP-0445-949): the w27 live
// re-verify proved the gitext adapter fix ENGAGED but boot still FAILS —
// GitExecutor..ctor reads AppSettings.WslGitEnabled eagerly ->
// AppSettings..cctor -> FileSettingsCache..ctor ->
// `new FileSystemWatcher()` PNSE -> TIE poisons ALL AppSettings use.
// Same poison family as 0315 (RepositoryDescriptionProvider edge), one
// level deeper. The cure neutralizes the watcher at the source:
// field-initializer newobj -> ldnull, plus null-guards over the .ctor
// watcher block and the Dispose-cleanup run (graceful degradation:
// _forceFileChangeChecks stays false, timestamp checks take over).
// Served proof rides the shell w28 regen; this file pins the transform
// contract on a Cecil-synthesized fixture (hermetic: no input-DLL or
// lane-tmp coupling) plus a desktop behavioral proof (ctor completes,
// field stays null, cleanup runs clean).
public sealed class FileSystemWatcherNeutralizeTests
{
    private const string TargetType = "GitCommands.Settings.FileSettingsCache";

    private static string BuildFixture(string path)
    {
        var asmName = new AssemblyNameDefinition("GitCommands", new Version(1, 0, 0, 0));
        var asm = AssemblyDefinition.CreateAssembly(asmName, "GitCommands", ModuleKind.Dll);
        var module = asm.MainModule;
        var objectCtor = module.ImportReference(
            typeof(object).GetConstructor(Type.EmptyTypes)!);
        var fswType = module.ImportReference(typeof(System.IO.FileSystemWatcher));
        var fswCtor = module.ImportReference(
            typeof(System.IO.FileSystemWatcher).GetConstructor(Type.EmptyTypes)!);
        var fswSetRaise = module.ImportReference(
            typeof(System.IO.FileSystemWatcher).GetMethod("set_EnableRaisingEvents")!);
        var fswSetPath = module.ImportReference(
            typeof(System.IO.FileSystemWatcher).GetMethod("set_Path")!);
        var fswAddChanged = module.ImportReference(
            typeof(System.IO.FileSystemWatcher).GetMethod("add_Changed")!);
        var componentDispose = module.ImportReference(
            typeof(System.ComponentModel.Component).GetMethod("Dispose")!);
        var handlerType = module.ImportReference(typeof(System.IO.FileSystemEventHandler));
        var handlerCtor = module.ImportReference(
            typeof(System.IO.FileSystemEventHandler).GetConstructor(
                new[] { typeof(object), typeof(IntPtr) })!);
        var handlerArg = module.ImportReference(typeof(System.IO.FileSystemEventArgs));

        // NOTE: intentionally concrete (real type is abstract) so the
        // desktop behavioral proof can instantiate it; the arm keys on
        // method shapes, never on abstractness.
        var type = new TypeDefinition("GitCommands.Settings", "FileSettingsCache",
            TypeAttributes.Public | TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(type);

        var watcherField = new FieldDefinition("_fileWatcher",
            FieldAttributes.Private, fswType);
        type.Fields.Add(watcherField);

        var fileChanged = new MethodDefinition("FileChanged",
            MethodAttributes.Private | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        {
            var il = fileChanged.Body.GetILProcessor();
            fileChanged.Body.MaxStackSize = 8;
            il.Emit(OpCodes.Ret);
            type.Methods.Add(fileChanged);
        }

        var onChanged = new MethodDefinition("_fileWatcher_Changed",
            MethodAttributes.Private | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        onChanged.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        onChanged.Parameters.Add(new ParameterDefinition(handlerArg));
        {
            var il = onChanged.Body.GetILProcessor();
            onChanged.Body.MaxStackSize = 8;
            il.Emit(OpCodes.Ret);
            type.Methods.Add(onChanged);
        }

        // .ctor mirrors the served shape: field-init newobj, two direct
        // touches, a try/catch touch block, FileChanged tail.
        var ctor = new MethodDefinition(".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig
            | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        {
            var il = ctor.Body.GetILProcessor();
            ctor.Body.InitLocals = true;
            ctor.Body.MaxStackSize = 8;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, objectCtor);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, fswCtor);
            il.Emit(OpCodes.Stfld, watcherField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, watcherField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, fswSetRaise);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, watcherField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldftn, onChanged);
            il.Emit(OpCodes.Newobj, handlerCtor);
            il.Emit(OpCodes.Callvirt, fswAddChanged);
            var tryStart = il.Create(OpCodes.Ldarg_0);
            il.Append(tryStart);
            il.Emit(OpCodes.Ldfld, watcherField);
            il.Emit(OpCodes.Ldstr, "x");
            il.Emit(OpCodes.Callvirt, fswSetPath);
            var leaveOk = il.Create(OpCodes.Nop);
            il.Append(leaveOk);
            var handlerStart = il.Create(OpCodes.Pop);
            il.Append(handlerStart);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, watcherField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, fswSetRaise);
            var handlerEnd = il.Create(OpCodes.Nop);
            il.Append(handlerEnd);
            var tail = il.Create(OpCodes.Ldarg_0);
            il.Append(tail);
            il.Emit(OpCodes.Call, fileChanged);
            il.Emit(OpCodes.Ret);
            leaveOk.OpCode = OpCodes.Leave;
            leaveOk.Operand = tail;
            handlerEnd.OpCode = OpCodes.Leave;
            handlerEnd.Operand = tail;
            ctor.Body.ExceptionHandlers.Add(new ExceptionHandler(
                ExceptionHandlerType.Catch)
            {
                TryStart = tryStart,
                TryEnd = handlerStart,
                HandlerStart = handlerStart,
                HandlerEnd = tail,
                CatchType = module.TypeSystem.Object,
            });
            type.Methods.Add(ctor);
        }

        // Cleanup mirrors <Dispose>b__15_0: watcher teardown run.
        var cleanup = new MethodDefinition("Cleanup",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        {
            var il = cleanup.Body.GetILProcessor();
            cleanup.Body.MaxStackSize = 8;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, watcherField);
            il.Emit(OpCodes.Callvirt, componentDispose);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, fileChanged);
            il.Emit(OpCodes.Ret);
            type.Methods.Add(cleanup);
        }

        asm.Write(path);
        return path;
    }

    private static int CountNewobjFsw(MethodDefinition m)
    {
        int n = 0;
        foreach (var ins in m.Body.Instructions)
            if (ins.OpCode == OpCodes.Newobj && ins.Operand is MethodReference mr
                && mr.DeclaringType.FullName == "System.IO.FileSystemWatcher")
                n++;
        return n;
    }

    private static void AssertBranchTargetsValid(MethodDefinition m)
    {
        var set = new HashSet<Instruction>(m.Body.Instructions);
        foreach (var ins in m.Body.Instructions)
        {
            if (ins.Operand is Instruction t)
                Assert.True(set.Contains(t), $"dangling branch in {m.Name}");
            else if (ins.Operand is Instruction[] arr)
                foreach (var x in arr) Assert.True(set.Contains(x), $"dangling switch in {m.Name}");
        }
        foreach (var h in m.Body.ExceptionHandlers)
        {
            Assert.True(set.Contains(h.TryStart), $"bad TryStart in {m.Name}");
            Assert.True(set.Contains(h.HandlerStart), $"bad HandlerStart in {m.Name}");
        }
    }

    [Fact]
    public void FileSystemWatcher_FieldInit_Neutralized_And_Guarded()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"fsw_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string input = Path.Combine(dir, "FsWatchFixture.dll");
        string output = Path.Combine(dir, "FsWatchFixture.out.dll");
        try
        {
            BuildFixture(input);
            // Fixture validity gate: the untransformed shape must execute
            // on desktop (real FSW ctor succeeds here), proving any later
            // InvalidProgram comes from the arm, not the synthesis.
            {
                var asmPre = System.Reflection.Assembly.Load(File.ReadAllBytes(input));
                var tPre = asmPre.GetType(TargetType)!;
                var instPre = System.Activator.CreateInstance(tPre)!;
                Assert.NotNull(tPre.GetField("_fileWatcher",
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)!.GetValue(instPre));
            }
            using (var module = ModuleDefinition.ReadModule(input))
            {
                int sites = WfWasm.Rewrite.AssemblyRewriter
                    .NeutralizeFileSystemWatcherInFileSettingsCache(module);
                Assert.Equal(3, sites);
                var type = module.GetType(TargetType);
                Assert.NotNull(type);
                foreach (var m in type.Methods)
                {
                    if (!m.HasBody) continue;
                    Assert.Equal(0, CountNewobjFsw(m));
                    AssertBranchTargetsValid(m);
                }
                // .ctor guard lands on the FileChanged tail call.
                var ctor = type.Methods.First(m => m.Name == ".ctor");
                bool guardToTail = false;
                foreach (var ins in ctor.Body.Instructions)
                    if (ins.OpCode == OpCodes.Dup
                        && ins.Next is Instruction brf
                        && (brf.OpCode == OpCodes.Brfalse || brf.OpCode == OpCodes.Brfalse_S)
                        && brf.Operand is Instruction pop
                        && pop.OpCode == OpCodes.Pop
                        && pop.Next is Instruction tail
                        && tail.OpCode == OpCodes.Ret
                        && pop.Previous is Instruction br2
                        && (br2.OpCode == OpCodes.Br || br2.OpCode == OpCodes.Br_S)
                        && br2.Operand == (object)tail
                        && br2.Previous is Instruction callSite
                        && callSite.OpCode == OpCodes.Call
                        && callSite.Operand is MethodReference mr && mr.Name == "FileChanged")
                        guardToTail = true;
                Assert.True(guardToTail, "ctor guard must be dup/brfalse/pop/br/ret after FileChanged call");
                // Roundtrip: rewritten IL must re-read cleanly.
                module.Write(output);
            }
            using (var re = ModuleDefinition.ReadModule(output))
            {
                var type = re.GetType(TargetType);
                Assert.NotNull(type);
                foreach (var m in type.Methods)
                {
                    if (!m.HasBody) continue;
                    Assert.Equal(0, CountNewobjFsw(m));
                    AssertBranchTargetsValid(m);
                }
            }
            // Behavioral proof on desktop: ctor completes, watcher stays
            // null (desktop FSW ctor would succeed, so null proves the
            // initializer is gone, not that construction threw). Cleanup
            // runs clean on the null field (guard skips the teardown run).
            var asmLoaded = System.Reflection.Assembly.Load(File.ReadAllBytes(output));
            var tLoaded = asmLoaded.GetType(TargetType)!;
            Assert.NotNull(tLoaded);
            var instance = System.Activator.CreateInstance(tLoaded)!;
            var field = tLoaded.GetField("_fileWatcher",
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)!;
            Assert.Null(field.GetValue(instance));
            tLoaded.GetMethod("Cleanup")!.Invoke(instance, null);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void FullRewrite_GitCommands_Applies_FileSystemWatcher_Seam()
    {
        // Dispatch proof: the arm must fire through the real Rewrite
        // pipeline for assembly name GitCommands (a typo'd gate would
        // silently skip in prod and the direct unit test would not catch
        // it). Fixture assembly is already named GitCommands.
        string dir = Path.Combine(Path.GetTempPath(), $"fsw_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string input = Path.Combine(dir, "GitCommands.dll");
        string output = Path.Combine(dir, "GitCommands.out.dll");
        try
        {
            BuildFixture(input);
            string cpuDll = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "WfWasm.PdnCpu", "bin", "Debug", "net9.0", "WfWasm.PdnCpu.dll"));
            WfWasm.Rewrite.AssemblyRewriter.Rewrite(
                input, output,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);
            using var module = ModuleDefinition.ReadModule(output);
            var type = module.GetType(TargetType);
            Assert.NotNull(type);
            foreach (var m in type.Methods)
            {
                if (!m.HasBody) continue;
                Assert.Equal(0, CountNewobjFsw(m));
                AssertBranchTargetsValid(m);
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Missing_FileSettingsCache_Fails_Loud()
    {
        var asmName = new AssemblyNameDefinition("GitCommands", new Version(1, 0, 0, 0));
        using var asm = AssemblyDefinition.CreateAssembly(asmName, "GitCommands", ModuleKind.Dll);
        Assert.Throws<InvalidOperationException>(() =>
            WfWasm.Rewrite.AssemblyRewriter.NeutralizeFileSystemWatcherInFileSettingsCache(asm.MainModule));
    }
}
