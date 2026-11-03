using Mono.Cecil;
using Mono.Cecil.Cil;

// Pins the Twist/Quantize/Bulge/Clouds invalid-IL retarget
// (SF1120-TWISTGATE-1032): menu clicks die in InvalidProgramException
// (empty frames, no dialog) on the generic value reads along the
// effect paths. Per-effect attribution in the twist bundles:
// TwistGpuEffect.OnCreatePropertyCollection dies on the
// ISetting<int>/IScalarAppSetting<int> trio; QuantizeBitmapEffect.
// OnSetToken layers Property<T>.get_Value, GetProperty<T>,
// Result<T>.get_IsValue/get_Value, and the static generic factories
// LazyResult.New<T>/GetSourceBitmap<T>/Values<TKey,TValue>;
// CloudsGpuEffect.OnUpdateOutput reads the same Property<T> getters.
// No direct callvirt on those getters, no GetProperty<T> callvirt,
// and none of the three static generic factory calls may survive in
// the shipped effect inputs; every rerouted site must land on its
// CpuPaint helper. Branch/handler-anchored and open-generic sites
// stay pristine and fail this test if ever present.
public sealed class EffectSettingGetterTests
{
    private static readonly string[] EffectInputs =
    {
        "PaintDotNet.Effects.dll",
        "PaintDotNet.Effects.Gpu.dll",
        "PaintDotNet.Effects.Legacy.dll",
    };

    private static readonly string[] HelperNames =
    {
        "GetEffectSettingValue",
        "GetEffectTokenProperty",
        "NewEffectLazyResult",
        "GetEffectSourceBitmap",
        "GetEnumerableValues",
    };

    private static string PaintDir()
    {
        string dir = @"E:\AI\Code\WindowsForms\targets\paint.net";
        Assert.True(Directory.Exists(dir), "pristine PDN inputs missing: " + dir);
        return dir;
    }

    private static string BinDll(string project, string tfm, string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", project, "bin", "Debug", tfm, name + ".dll"));
        Assert.True(File.Exists(dll), "dependency not built: " + dll);
        return dll;
    }

    private static ModuleDefinition Transform(string fileName, string tempOut)
    {
        string input = Path.Combine(PaintDir(), fileName);
        Assert.True(File.Exists(input), "pristine PDN input missing: " + input);
        string cpuDll = BinDll("WfWasm.PdnCpu", "net9.0", "WfWasm.PdnCpu");
        WfWasm.Rewrite.AssemblyRewriter.Rewrite(
            input, tempOut,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
            cpuDll);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(PaintDir());
        return AssemblyDefinition.ReadAssembly(tempOut,
            new ReaderParameters { AssemblyResolver = resolver }).MainModule;
    }

    private static bool IsGetterShape(MethodReference mr) =>
        mr.DeclaringType is GenericInstanceType gi
        && gi.GenericArguments.Count == 1
        && (gi.ElementType.FullName == "PaintDotNet.PropertySystem.Property`1"
            || gi.ElementType.FullName == "PaintDotNet.AppModel.ISetting`1"
            || gi.ElementType.FullName == "PaintDotNet.AppModel.IScalarAppSetting`1"
            || gi.ElementType.FullName == "PaintDotNet.PropertySystem.VectorProperty`1"
            || gi.ElementType.FullName == "PaintDotNet.Functional.Result`1")
        && (mr.Name == "get_Value" || mr.Name == "get_MinValue" || mr.Name == "get_MaxValue"
            || mr.Name == "get_IsValue" || mr.Name == "get_ValueX" || mr.Name == "get_ValueY");

    private static bool IsTokenFactoryShape(MethodReference mr) =>
        mr.Name == "GetProperty"
        && mr.DeclaringType.FullName == "PaintDotNet.Effects.PropertyBasedEffectConfigToken"
        && mr is GenericInstanceMethod gprop
        && gprop.GenericArguments.Count == 1;

    private static bool IsStaticFactoryShape(MethodReference mr) =>
        mr is GenericInstanceMethod gim
        && ((mr.Name == "New"
                && mr.DeclaringType.FullName == "PaintDotNet.Functional.LazyResult"
                && gim.GenericArguments.Count == 1)
            || (mr.Name == "GetSourceBitmap"
                && mr.DeclaringType.FullName == "PaintDotNet.Effects.EffectEnvironmentExtensions"
                && gim.GenericArguments.Count == 1)
            || (mr.Name == "Values"
                && mr.DeclaringType.FullName == "PaintDotNet.Collections.EnumerableExtensions"
                && gim.GenericArguments.Count == 2));

    private static void CountSites(ModuleDefinition module,
        out int getterSurvivors, out List<string> getterSites,
        out int factorySurvivors, out List<string> factorySites,
        out int helpers, out List<string> helperSites)
    {
        getterSurvivors = 0;
        factorySurvivors = 0;
        helpers = 0;
        getterSites = new List<string>();
        factorySites = new List<string>();
        helperSites = new List<string>();
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var ins in method.Body.Instructions)
                {
                    if (ins.Operand is not MethodReference mr)
                        continue;
                    if (ins.OpCode == OpCodes.Callvirt && IsGetterShape(mr))
                    {
                        getterSurvivors++;
                        getterSites.Add(type.FullName + "::" + method.Name + " " + mr.Name);
                    }
                    else if (ins.OpCode == OpCodes.Callvirt && IsTokenFactoryShape(mr))
                    {
                        factorySurvivors++;
                        factorySites.Add(type.FullName + "::" + method.Name + " GetProperty<T>");
                    }
                    else if (ins.OpCode == OpCodes.Call && IsStaticFactoryShape(mr))
                    {
                        factorySurvivors++;
                        factorySites.Add(type.FullName + "::" + method.Name + " " + mr.Name + "<T>");
                    }
                    else if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                        && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                        && HelperNames.Contains(mr.Name))
                    {
                        helpers++;
                        helperSites.Add(type.FullName + "::" + method.Name + " " + mr.Name);
                    }
                }
            }
            foreach (var nested in type.NestedTypes)
                stack.Push(nested);
        }
    }

    [Fact]
    public void Effect_Setting_Unwrap_Never_Unboxes_Reference_Types()
    {
        // QUANTIZE-RESIDUAL: the getValue reroute emitted Unbox_Any with
        // the setting type unconditionally. Reference-typed settings
        // (StaticListChoice Value: object; ImageColorsAnalysis,
        // ColorHistogram<T>, PaletteTable lazies) produced unbox.any on a
        // reference type, which the browser interpreter rejects with an
        // empty InvalidProgramException (Quantize OnSetToken wall, no
        // dialog). Value types keep Unbox_Any; reference types must use
        // Castclass; object needs no post-op (helper already returns it).
        const string fileName = "PaintDotNet.Effects.dll";
        string tempOut = Path.Combine(Path.GetTempPath(), $"FxUnbox_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = Transform(fileName, tempOut);
            var bad = new List<string>();
            var stack = new Stack<TypeDefinition>(module.Types);
            while (stack.Count > 0)
            {
                var type = stack.Pop();
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    foreach (var ins in method.Body.Instructions)
                    {
                        if (ins.OpCode != OpCodes.Unbox_Any
                            || ins.Operand is not TypeReference tr)
                            continue;
                        if (tr.FullName == "System.Object")
                        {
                            bad.Add(type.FullName + "::" + method.Name + " unbox.any object");
                            continue;
                        }
                        TypeDefinition? resolved = null;
                        try { resolved = tr.Resolve(); } catch { }
                        if (resolved is not null && !resolved.IsValueType)
                            bad.Add(type.FullName + "::" + method.Name
                                + " unbox.any " + tr.FullName);
                    }
                }
                foreach (var nested in type.NestedTypes)
                    stack.Push(nested);
            }
            Assert.True(bad.Count == 0,
                fileName + " unbox.any on reference types: " + string.Join("; ", bad.Take(10)));
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void Effect_Bridge_PostOp_Unwraps_Generic_Instantiations()
    {
        // TRIO-RESIDUAL-SLICE2: bridge post-ops whose target is a closed
        // generic instantiation (LazyResult'1, IEnumerable'1,
        // IEffectInputBitmap'1, ...) must go through CpuPaint.Unwrap<T>:
        // the browser interpreter rejects castclass/unbox directly on
        // those targets with an empty InvalidProgramException (Quantize
        // OnSetToken wall: kind-check present in gen21 yet the dialog
        // still never opens). Value/object shapes keep the inline op.
        const string fileName = "PaintDotNet.Effects.Legacy.dll";
        string tempOut = Path.Combine(Path.GetTempPath(), $"FxUnwrap_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = Transform(fileName, tempOut);
            int unwraps = 0;
            var bad = new List<string>();
            var stack = new Stack<TypeDefinition>(module.Types);
            while (stack.Count > 0)
            {
                var type = stack.Pop();
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    Instruction? prev = null;
                    foreach (var ins in method.Body.Instructions)
                    {
                        if (ins.Operand is not MethodReference mr)
                        {
                            if ((ins.OpCode == OpCodes.Castclass || ins.OpCode == OpCodes.Unbox_Any)
                                && ins.Operand is GenericInstanceType git
                                && !git.IsValueType
                                && prev?.Operand is MethodReference pmr
                                && pmr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                                && (HelperNames.Contains(pmr.Name) || pmr.Name == "Unwrap"))
                                bad.Add(type.FullName + "::" + method.Name
                                    + " " + ins.OpCode.Name + " " + git.FullName
                                    + " after " + pmr.Name);
                            prev = ins;
                            continue;
                        }
                        if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
                            && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                            && mr.Name == "Unwrap"
                            && mr is GenericInstanceMethod gim
                            && gim.GenericArguments.Count == 1
                            && gim.GenericArguments[0] is GenericInstanceType)
                            unwraps++;
                        prev = ins;
                    }
                }
                foreach (var nested in type.NestedTypes)
                    stack.Push(nested);
            }
            Assert.True(unwraps > 0,
                fileName + " must route at least one generic-instantiation bridge post-op through CpuPaint.Unwrap<T>");
            Assert.True(bad.Count == 0,
                fileName + " direct castclass/unbox on generic instantiations: " + string.Join("; ", bad.Take(10)));
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Theory]
    [InlineData("PaintDotNet.Effects.dll")]
    [InlineData("PaintDotNet.Effects.Gpu.dll")]
    [InlineData("PaintDotNet.Effects.Legacy.dll")]
    public void Effect_Value_Reads_Rerouted_Through_Reflection_Helpers(string fileName)
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"FxGet_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = Transform(fileName, tempOut);
            CountSites(module, out int getterSurvivors, out var getterSites,
                out int factorySurvivors, out var factorySites,
                out int helpers, out var helperSites);
            Assert.True(getterSurvivors == 0,
                fileName + " surviving generic getter callvirt: " + string.Join("; ", getterSites.Take(10)));
            Assert.True(factorySurvivors == 0,
                fileName + " surviving generic factory calls: " + string.Join("; ", factorySites.Take(10)));
            Assert.True(helpers > 0,
                fileName + " must route effect value reads through the CpuPaint helpers");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
