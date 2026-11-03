using Mono.Cecil;
using Mono.Cecil.Cil;

// TRIO-METHOD-BRIDGE (WC-20261103T0133-2791): closed static
// generic-method-instantiation calls with value-type type arguments die in
// the browser interpreter with an empty InvalidProgramException on
// dialog-construction paths (Twist Pair.Create<double,double>, Twist/Bulge
// CreateClipper<ColorBgra32>, Quantize CreateForEnum<PropertyNames>), while
// the value-read bridges execute in vivo (Invert 28800px). The rule reroutes
// the boxable by-value subset through CpuPaint.InvokeClosedGenericStatic.
// Must-not-touch (census): InvertColorsGpuEffect has no generic-method calls
// at all, the Levels creation/configUI methods have none, and byref/intrinsic
// shapes (Swap<int> in both RenderChannels, Unsafe/MemoryMarshal families)
// stay direct. GeneratePalette is an ordinary interface dispatch (not a
// generic-method call) and pointer getters (Scan0/Scan0Void/MemoryBlock) are
// absent from every PDN input, so neither needs this bridge.
public sealed class ClosedGenericMethodBridgeTests
{
    private static readonly string[] EffectInputs =
    {
        "PaintDotNet.Effects.dll",
        "PaintDotNet.Effects.Gpu.dll",
        "PaintDotNet.Effects.Legacy.dll",
        "PaintDotNet.Effects.Core.dll",
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

    private static bool IsBridgeHelperCall(Instruction ins) =>
        (ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt)
        && ins.Operand is MethodReference mr
        && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
        && (mr.Name == "InvokeClosedGenericStatic" || mr.Name == "CastOrTransferRefRuntime"
            || mr.Name == "GetEffectSourceBitmapChain");

    private static bool IsValueTypeArg(TypeReference t)
    {
        TypeReference r = t;
        while (r is TypeSpecification spec)
            r = spec.ElementType;
        return r.IsValueType;
    }

    private static bool IsConcrete(TypeReference t)
    {
        if (t is GenericParameter)
            return false;
        if (t is GenericInstanceType gi)
        {
            foreach (TypeReference a in gi.GenericArguments)
                if (!IsConcrete(a))
                    return false;
            return true;
        }
        if (t is TypeSpecification spec)
            return IsConcrete(spec.ElementType);
        return true;
    }

    // Mirrors the rule's allowlist (R4): only the named effect-factory
    // families are bridged. Any surviving allowlisted site in the
    // transformed tree is a rule miss; everything else must stay direct
    // (blanket bridging regressed the Levels histogram via SelectRO).
    private static bool IsAllowlistedCallee(string decl, string name)
    {
        if (decl.Contains('<'))
            return false;
        return (decl == "PaintDotNet.Pair" && name == "Create")
            || (decl == "System.Tuple" && name == "Create")
            || (decl == "PaintDotNet.PropertySystem.StaticListChoiceProperty" && name == "CreateForEnum")
            || (decl == "PaintDotNet.Imaging.BitmapSourceExtensions" && (name == "CreateClipper" || name == "CreateFormatConverter"))
            || (decl == "PaintDotNet.Imaging.ImagingFactoryExtensions" && name == "CreateBufferedBitmap")
            || (decl == "PaintDotNet.Effects.EffectEnvironmentExtensions" && name == "GetSourceBitmap")
            || (decl == "PaintDotNet.ComponentModel.ObjectRefExtensions" && (name == "CastOrTransferRef" || name == "TryCastOrTransferRef"));
    }

    private static bool IsDeepValueContent(TypeReference t)
    {
        TypeReference r = t;
        while (r is TypeSpecification spec)
            r = spec.ElementType;
        if (r.IsValueType)
            return true;
        if (r is GenericInstanceType gi)
        {
            foreach (TypeReference a in gi.GenericArguments)
                if (IsDeepValueContent(a))
                    return true;
        }
        return false;
    }

    private static bool IsNestedObjectRef(string decl, string name, System.Collections.Generic.IList<TypeReference> args)
    {
        if (decl != "PaintDotNet.ComponentModel.ObjectRefExtensions")
            return false;
        if (name != "CastOrTransferRef" && name != "TryCastOrTransferRef")
            return false;
        foreach (TypeReference ta in args)
        {
            if (ta is GenericInstanceType && IsDeepValueContent(ta) && IsConcrete(ta))
                return true;
        }
        return false;
    }

    private static bool IsBridgeableSite(Instruction ins)
    {
        if (ins.OpCode != OpCodes.Call)
            return false;
        if (ins.Operand is not GenericInstanceMethod gim)
            return false;
        if (gim.ContainsGenericParameter)
            return false;
        string decl = gim.DeclaringType.FullName;
        if (!IsAllowlistedCallee(decl, gim.Name))
            return false;
        bool anyVt = false;
        foreach (TypeReference ta in gim.GenericArguments)
        {
            if (!IsConcrete(ta) || (ta.FullName.Contains('<') && !IsNestedObjectRef(decl, gim.Name, gim.GenericArguments)))
                return false;
            if (IsValueTypeArg(ta))
                anyVt = true;
        }
        return anyVt || IsNestedObjectRef(decl, gim.Name, gim.GenericArguments);
    }

    private static void Walk(ModuleDefinition module, Action<TypeDefinition, MethodDefinition, Instruction> visit)
    {
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var ins in method.Body.Instructions)
                    visit(type, method, ins);
            }
            foreach (var nested in type.NestedTypes)
                stack.Push(nested);
        }
    }

    [Fact]
    public void Effect_GenericMethod_Bridge_Reroutes_Trio_Creation_Sites()
    {
        // The three failing dialog-construction call shapes must land on the
        // reflection bridge, with no bridgeable valuetype-arg generic-method
        // call surviving in the effect inputs. Pre-fix the helper does not
        // exist, so this fails before and passes after.
        int bridged = 0;
        var survivors = new List<string>();
        var trioBridged = new List<string>();
        foreach (string fileName in EffectInputs)
        {
            string tempOut = Path.Combine(Path.GetTempPath(), $"FxGim_{Guid.NewGuid():N}.dll");
            try
            {
                using var module = Transform(fileName, tempOut);
                Walk(module, (type, method, ins) =>
                {
                    if (IsBridgeHelperCall(ins))
                    {
                        bridged++;
                        string where = type.FullName + "::" + method.Name;
                        if (where.Contains("TwistGpuEffect") || where.Contains("BulgeGpuEffect")
                            || where.Contains("QuantizeBitmapEffect"))
                            trioBridged.Add(where);
                    }
                    else if (IsBridgeableSite(ins))
                    {
                        survivors.Add(type.FullName + "::" + method.Name
                            + " -> " + ((MethodReference)ins.Operand).FullName);
                    }
                });
            }
            finally
            {
                try { File.Delete(tempOut); } catch { }
            }
        }
        Assert.True(bridged > 0,
            "must route closed valuetype-arg generic-method calls through CpuPaint.InvokeClosedGenericStatic");
        Assert.True(trioBridged.Count > 0,
            "trio dialog-construction sites must be bridged: " + string.Join("; ", trioBridged.Take(5)));
        Assert.True(survivors.Count == 0,
            "bridgeable survivors: " + string.Join("; ", survivors.Take(10)));
    }

    [Fact]
    public void Effect_OpenObjectRef_Bridge_Reroutes_Interior_CastOrTransferRef()
    {
        // R6: the interior CastOrTransferRef<IEffectInputBitmap<TPixel>> call
        // inside the OPEN generic GetSourceBitmap<TPixel> dies in the browser
        // interpreter (gen30 Twist THROW fired inside the helper-invoked
        // generic body, before any interior bridge marker). The name-based
        // closed bridge cannot see it (type arguments are the enclosing
        // method's own parameters at transform time), so the open-site rule
        // reroutes it through CpuPaint.CastOrTransferRefRuntime with the
        // closed target built from ldtoken + MakeGenericType. Pre-fix the
        // direct nested call survives and this fails.
        string tempOut = Path.Combine(Path.GetTempPath(), $"FxGimOpen_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = Transform("PaintDotNet.Effects.Core.dll", tempOut);
            bool helperSeen = false;
            var directSurvivors = new List<string>();
            Walk(module, (type, method, ins) =>
            {
                if (type.FullName != "PaintDotNet.Effects.EffectEnvironmentExtensions"
                    || method.Name != "GetSourceBitmap" || !method.HasGenericParameters)
                    return;
                if (ins.Operand is MethodReference mr
                    && mr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                    && mr.Name == "CastOrTransferRefRuntime")
                    helperSeen = true;
                else if (ins.OpCode == OpCodes.Call && ins.Operand is GenericInstanceMethod gim
                    && gim.DeclaringType.FullName == "PaintDotNet.ComponentModel.ObjectRefExtensions"
                    && (gim.Name == "CastOrTransferRef" || gim.Name == "TryCastOrTransferRef"))
                    directSurvivors.Add(gim.FullName);
            });
            Assert.True(helperSeen,
                "open interior CastOrTransferRef must route through CpuPaint.CastOrTransferRefRuntime");
            Assert.True(directSurvivors.Count == 0,
                "open nested survivors: " + string.Join("; ", directSurvivors.Take(5)));
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void Effect_Chain_Bridge_Reroutes_NonGeneric_GetSourceBitmap()
    {
        // R7: Twist+Bulge die inside the non-generic GetSourceBitmap(env,
        // PixelFormat&) body after ENTER (gen31 oracle) with no interior
        // bridge marker. The byref call sites (inside the generic
        // GetSourceBitmap<TPixel> body and any direct callers) reroute
        // through CpuPaint.GetEffectSourceBitmapChain, whose per-step
        // traces name the dying interface step. Pre-fix the direct byref
        // call survives and this fails.
        string tempOut = Path.Combine(Path.GetTempPath(), $"FxGimChain_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = Transform("PaintDotNet.Effects.Core.dll", tempOut);
            bool helperSeen = false;
            var directSurvivors = new List<string>();
            Walk(module, (type, method, ins) =>
            {
                if (ins.OpCode != OpCodes.Call)
                    return;
                if (ins.Operand is GenericInstanceMethod)
                    return;
                if (ins.Operand is not MethodReference mr)
                    return;
                if (mr.DeclaringType.FullName != "PaintDotNet.Effects.EffectEnvironmentExtensions"
                    || mr.Name != "GetSourceBitmap")
                    return;
                if (type.FullName == "WfWasm.PdnCpu.CpuPaint")
                    return;
                if (mr.Parameters.Count == 2 && mr.Parameters[1].ParameterType is ByReferenceType)
                    directSurvivors.Add(type.FullName + "::" + method.Name);
            });
            Walk(module, (type, method, ins) =>
            {
                if (ins.Operand is MethodReference hmr
                    && hmr.DeclaringType.FullName == "WfWasm.PdnCpu.CpuPaint"
                    && hmr.Name == "GetEffectSourceBitmapChain")
                    helperSeen = true;
            });
            Assert.True(helperSeen,
                "non-generic GetSourceBitmap byref sites must route through CpuPaint.GetEffectSourceBitmapChain");
            Assert.True(directSurvivors.Count == 0,
                "chain survivors: " + string.Join("; ", directSurvivors.Take(5)));
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void Effect_GenericMethod_Bridge_Preserves_Proven_Paths()
    {
        // Must-not-touch census, re-verified every run: proven-executing code
        // keeps its direct calls. Invert renders 28800px and the Levels dialog
        // opens, so their methods must contain no bridge-helper calls; byref
        // (Swap<int>) and intrinsic shapes stay direct everywhere.
        var helperHits = new List<string>();
        int swapSurvivors = 0;
        int renderChannelSwap = 0;
        int intrinsicSurvivors = 0;
        int selectROSurvivors = 0;
        int maxSurvivors = 0;
        int arrayEmptySurvivors = 0;
        foreach (string fileName in EffectInputs)
        {
            string tempOut = Path.Combine(Path.GetTempPath(), $"FxGimNt_{Guid.NewGuid():N}.dll");
            try
            {
                using var module = Transform(fileName, tempOut);
                Walk(module, (type, method, ins) =>
                {
                    string where = type.FullName + "::" + method.Name;
                    // R4 allowlist: SelectRO/Max/Array/intrinsics stay direct
                    // (bridging SelectRO blanked the Levels histogram on
                    // gen25). Proven paths must contain no helper calls.
                    if (IsBridgeHelperCall(ins)
                        && (where.Contains("InvertColorsGpuEffect")
                            || where.Contains("LevelsGpuEffect::OnCreatePropertyCollection")
                            || where.Contains("LevelsGpuEffect::OnCreateConfigUI")
                            || where.Contains("HistogramControl::RenderChannel")
                            || where.Contains("SetInputHistogram")))
                        helperHits.Add(where);
                    if (ins.Operand is GenericInstanceMethod gim
                        && (ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt))
                    {
                        string callee = gim.FullName;
                        if (callee.Contains("ObjectUtil::Swap<"))
                        {
                            swapSurvivors++;
                            if (where.Contains("LevelsHistogramControl::RenderChannel"))
                                renderChannelSwap++;
                        }
                        if (callee.Contains("ReadOnlyListExtensions::SelectRO<"))
                            selectROSurvivors++;
                        if (callee.Contains("MathUtil::Max<"))
                            maxSurvivors++;
                        if (callee.Contains("System.Array::Empty<"))
                            arrayEmptySurvivors++;
                        if (gim.DeclaringType.FullName.StartsWith("System.Runtime.CompilerServices.Unsafe")
                            || gim.DeclaringType.FullName.StartsWith("System.Runtime.InteropServices.MemoryMarshal"))
                            intrinsicSurvivors++;
                    }
                });
            }
            finally
            {
                try { File.Delete(tempOut); } catch { }
            }
        }
        Assert.True(helperHits.Count == 0,
            "bridge must not touch proven paths: " + string.Join("; ", helperHits.Take(5)));
        Assert.True(swapSurvivors > 0,
            "byref Swap<int> calls must stay direct (rule skips unboxable shapes)");
        Assert.True(renderChannelSwap == 2,
            "RenderChannel Swap<int> x2 must stay direct where SelectRO is bridged, got " + renderChannelSwap);
        Assert.True(intrinsicSurvivors > 0,
            "Unsafe/MemoryMarshal calls must stay direct (rule skips intrinsics)");
        Assert.True(selectROSurvivors > 0,
            "SelectRO must stay direct (its bridge blanked the histogram on gen25)");
        Assert.True(maxSurvivors > 0,
            "MathUtil.Max must stay direct (allowlist excludes it)");
        Assert.True(arrayEmptySurvivors > 0,
            "Array.Empty must stay direct (allowlist excludes it)");
    }

    [Fact]
    public void Effect_GenericMethod_Bridge_Emits_ScopeAfterBrackets_For_GenericInstance_Params()
    {
        // R18: the Twist CreateClipper bridge key carried a generic-instance
        // parameter whose emitted runtime name placed the assembly scope
        // BETWEEN the backtick arity and the argument brackets
        // (IBitmapSource`1, PaintDotNet.Windows[[...]]). Type.GetType can only
        // parse scope-after-brackets (IBitmapSource`1[[...]], Scope), so the
        // miss path died in ResolveBridgeType even with the assembly loaded.
        // Every emitted generic-instance runtime name must use the valid
        // shape; at least one must exist (the Twist clipper site).
        var emitted = new List<string>();
        foreach (string fileName in EffectInputs)
        {
            string tempOut = Path.Combine(Path.GetTempPath(), $"FxGimShape_{Guid.NewGuid():N}.dll");
            try
            {
                using var module = Transform(fileName, tempOut);
                Walk(module, (type, method, ins) =>
                {
                    if (ins.OpCode != OpCodes.Ldstr || ins.Next == null
                        || ins.Next.OpCode != OpCodes.Stelem_Ref)
                        return;
                    if (ins.Operand is string s && s.Contains("`") && s.Contains("[["))
                        emitted.Add(s);
                });
            }
            finally
            {
                try { File.Delete(tempOut); } catch { }
            }
        }
        Assert.True(emitted.Count > 0,
            "expected at least one generic-instance runtime name in bridged sites (Twist clipper site)");
        Assert.True(emitted.Any(s => s.Contains("IBitmapSource")),
            "expected the Twist clipper source-param name among emitted generic-instance names");
        var malformed = new List<string>();
        foreach (string name in emitted)
        {
            int brackets = name.IndexOf("[[", StringComparison.Ordinal);
            int close = name.LastIndexOf("]]", StringComparison.Ordinal);
            int comma = close < 0 ? -1 : name.IndexOf(", ", close, StringComparison.Ordinal);
            string scope = comma < 0 ? "" : name.Substring(comma + 2);
            if (brackets < 0 || close < brackets || comma < 0
                || scope.Length == 0 || scope.Contains('[') || scope.Contains(']'))
                malformed.Add(name);
        }
        Assert.True(malformed.Count == 0,
            "scope must follow the argument brackets: " + string.Join("; ", malformed.Take(5)));
    }

    [Fact]
    public void Effect_GenericReceiver_Callvirt_Gets_Interface_Cast()
    {
        // R31: Paint.NET's ObjectRef/RefHandle forwarding idiom callvirts an
        // interface method on a generic-parameter-typed value (get_InnerRef/
        // get_Ref return !0) with no constrained. prefix. Desktop full-trust
        // JIT resolves it unverified, but the browser interpreter rejects the
        // method at entry with an empty InvalidProgramException. The wall is
        // Proxies ISupportBitmapEffectRendererProxy.InitializeRenderInfo (the
        // Quantize path: BitmapEffectRenderer.Initialize ENTERs, IPE, no
        // EXIT). The rule inserts castclass to the target interface before
        // such callvirts; the value genuinely implements the interface
        // wherever today's dispatch succeeds, so the cast is behavior-neutral.
        string tempOut = Path.Combine(Path.GetTempPath(), $"FxGenRecv_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = Transform("PaintDotNet.Effects.Core.dll", tempOut);
            // Multiset of castclass-then-callvirt pairs: the rule must ADD
            // pairs (pristine C# already emits some), including the proxy
            // InitializeRenderInfo wall site.
            var post = new Dictionary<string, int>(StringComparer.Ordinal);
            Walk(module, (type, method, ins) =>
            {
                if (type.FullName.Contains("BitmapEffectRendererProxy") && !type.FullName.Contains("Gpu"))
                if (ins.OpCode != OpCodes.Castclass)
                    return;
                if (ins.Next == null || ins.Next.OpCode != OpCodes.Callvirt)
                    return;
                if (ins.Operand is not TypeReference castTo)
                    return;
                if (ins.Next.Operand is not MethodReference mr)
                    return;
                string key = type.FullName + "::" + method.Name + " casts "
                    + castTo.FullName + " for " + mr.DeclaringType.FullName + "::" + mr.Name;
                post.TryGetValue(key, out int n);
                post[key] = n + 1;
            });
            var pre = new Dictionary<string, int>(StringComparer.Ordinal);
            {
                string input = Path.Combine(PaintDir(), "PaintDotNet.Effects.Core.dll");
                var resolver = new DefaultAssemblyResolver();
                resolver.AddSearchDirectory(PaintDir());
                using var pristine = AssemblyDefinition.ReadAssembly(input,
                    new ReaderParameters { AssemblyResolver = resolver }).MainModule;
                Walk(pristine, (type, method, ins) =>
                {
                    if (ins.OpCode != OpCodes.Castclass)
                        return;
                    if (ins.Next == null || ins.Next.OpCode != OpCodes.Callvirt)
                        return;
                    if (ins.Operand is not TypeReference castTo)
                        return;
                    if (ins.Next.Operand is not MethodReference mr)
                        return;
                    string key = type.FullName + "::" + method.Name + " casts "
                        + castTo.FullName + " for " + mr.DeclaringType.FullName + "::" + mr.Name;
                    pre.TryGetValue(key, out int n);
                    pre[key] = n + 1;
                });
            }
            var added = new List<string>();
            foreach (var kv in post)
            {
                pre.TryGetValue(kv.Key, out int before);
                for (int i = 0; i < kv.Value - before; i++)
                    added.Add(kv.Key);
            }
            Assert.True(added.Count > 0,
                "expected the rule to add interface casts in transformed Core (ObjectRef/RefHandle forwarding idiom)");
            // Wall-site shape: the cast must land after the receiver push
            // (get_InnerRef call) and before the argument pushes/callvirt.
            // Casting immediately before the callvirt would cast the last
            // argument instead of the receiver (invalid IL on multi-arg
            // forwards such as BitmapExtensions.CreateWindow).
            MethodDefinition? wallMethod = null;
            Walk(module, (type, method, ins) =>
            {
                if (type.FullName == "PaintDotNet.Effects.Proxies.ISupportBitmapEffectRendererProxy"
                    && method.Name == "InitializeRenderInfo")
                    wallMethod = method;
            });
            bool wallCast = false;
            if (wallMethod is not null && wallMethod.HasBody)
            {
                var seq = wallMethod.Body.Instructions.ToArray();
                int innerRef = -1, castAt = -1, site = -1;
                for (int s = 0; s < seq.Length; s++)
                {
                    if (seq[s].OpCode == OpCodes.Call
                        && seq[s].Operand is MethodReference cmr && cmr.Name == "get_InnerRef")
                        innerRef = s;
                    if (seq[s].OpCode == OpCodes.Castclass
                        && seq[s].Operand is TypeReference ctr
                        && ctr.FullName == "PaintDotNet.Effects.ISupportBitmapEffectRenderer")
                        castAt = s;
                    if ((seq[s].OpCode == OpCodes.Call || seq[s].OpCode == OpCodes.Callvirt)
                        && seq[s].Operand is MethodReference vmr && vmr.Name == "InitializeRenderInfo")
                        site = s;
                }
                if (innerRef >= 0 && castAt > innerRef && site > castAt)
                    wallCast = true;
            }
            Assert.True(wallCast,
                "Proxies ISupportBitmapEffectRendererProxy.InitializeRenderInfo must cast the receiver after get_InnerRef and before the callvirt (Quantize wall)");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void Effect_ValidatePixelFormat_Port_Emits_Balanced_TryGetPixelInfo()
    {
        // R33: the rev292-ported EnsureCpuValidatePixelFormat pushed TWO
        // receiver values for the 2-arg TryGetPixelInfo(PixelFormat&,
        // IPixelInfo&): the cmykOk branch-target Ldarg_0 doubled as the
        // fall-through push AND an extra Emit added a second Ldarg_0. The
        // resulting 3-push/2-param stack imbalance made the body invalid IL,
        // observed as an empty InvalidProgramException inside
        // BitmapEffect.ValidatePixelFormat on the Quantize path (the R32
        // step oracle localized the wall to that call). Sister ports
        // VerifyOutputPixelFormat/DefaultGammaMode were audited balanced.
        // This pin locks the exact 3-instruction window before the call.
        string tempOut = Path.Combine(Path.GetTempPath(), $"FxValidate_{Guid.NewGuid():N}.dll");
        try
        {
            using var module = Transform("PaintDotNet.Effects.Core.dll", tempOut);
            MethodDefinition? validate = null;
            Walk(module, (type, method, ins) =>
            {
                if (type.FullName == "PaintDotNet.Effects.BitmapEffect"
                    && method.Name == "ValidatePixelFormat")
                    validate = method;
            });
            Assert.True(validate is not null && validate.HasBody,
                "BitmapEffect.ValidatePixelFormat must exist in transformed Core");
            var seq = validate!.Body.Instructions.ToArray();
            int site = -1;
            for (int s = 0; s < seq.Length; s++)
            {
                if ((seq[s].OpCode == OpCodes.Call || seq[s].OpCode == OpCodes.Callvirt)
                    && seq[s].Operand is MethodReference mr && mr.Name == "TryGetPixelInfo")
                    site = s;
            }
            Assert.True(site >= 2, "ValidatePixelFormat must call TryGetPixelInfo");
            Assert.True(seq[site - 1].OpCode == OpCodes.Ldloca
                || seq[site - 1].OpCode == OpCodes.Ldloca_S,
                "TryGetPixelInfo arg2 must be the ldloca of the IPixelInfo local");
            Assert.True(seq[site - 2].OpCode == OpCodes.Ldarg_0,
                "TryGetPixelInfo arg1 must be a single ldarg_0");
            Assert.True(seq[site - 3].OpCode != OpCodes.Ldarg_0,
                "no duplicate receiver push before TryGetPixelInfo (R33 stack imbalance)");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void Effect_TransferRef_Carrier_Def_Matches_Runtime_Scan()
    {
        // TRIO-TRYTRANSFER-R34 (WC-20261103T1115-vendor-trytransfer): the
        // gen40 toolstrip-click "CpuPaint: TryTransferRef open def missing"
        // fired because the R6 scan was Public-only while the served def is
        // non-public (R16 widened the flags + added carrier preference; R34
        // pre-loads PaintDotNet.ComponentModel). The runtime binds by exact
        // shape: type PaintDotNet.ComponentModel.ObjectRefExtensions,
        // static, generic definition, exactly 1 type argument, exactly 1
        // parameter. This pin locks the pristine input side of that
        // contract (the gen59 served census proved the same single def with
        // no shadow in the serving closure): exactly one def with that
        // shape in the carrier, so the scan resolves instead of throwing.
        string input = Path.Combine(PaintDir(), "PaintDotNet.ComponentModel.dll");
        Assert.True(File.Exists(input), "pristine carrier input missing: " + input);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(PaintDir());
        using var carrier = AssemblyDefinition.ReadAssembly(input,
            new ReaderParameters { AssemblyResolver = resolver });
        var shapes = new List<string>();
        foreach (TypeDefinition type in carrier.MainModule.Types)
        {
            if (type.FullName != "PaintDotNet.ComponentModel.ObjectRefExtensions")
                continue;
            foreach (MethodDefinition m in type.Methods)
            {
                if (m.Name != "TryTransferRef" || !m.IsStatic || !m.HasGenericParameters)
                    continue;
                shapes.Add(m.GenericParameters.Count + "/" + m.Parameters.Count
                    + (m.IsPublic ? "/pub" : "/nonpub"));
            }
        }
        Assert.Single(shapes);
        Assert.Equal("1/1/nonpub", shapes[0]);
    }

    [Fact]
    public void Effect_TransferRef_Runtime_Preloads_Carrier_Before_Scan()
    {
        // R34: CastOrTransferRefRuntime must call Assembly.Load for the
        // proven-served carrier before its first GetAssemblies scan, so a
        // first call before the carrier loads resolves instead of throwing
        // "ObjectRefExtensions not loaded" / "TryTransferRef open def
        // missing" (gen40 toolstrip-click). Locks the preload against
        // silent removal; the behavioral PdnCpu pins cover the scan itself.
        string cpuDll = BinDll("WfWasm.PdnCpu", "net9.0", "WfWasm.PdnCpu");
        using var module = AssemblyDefinition.ReadAssembly(cpuDll).MainModule;
        MethodDefinition? target = null;
        Walk(module, (type, method, ins) =>
        {
            if (type.FullName == "WfWasm.PdnCpu.CpuPaint"
                && method.Name == "CastOrTransferRefRuntime")
                target = method;
        });
        Assert.True(target is not null && target.HasBody,
            "CpuPaint.CastOrTransferRefRuntime must exist in built PdnCpu");
        var seq = target!.Body.Instructions.ToArray();
        int load = -1, scan = -1;
        for (int s = 0; s < seq.Length; s++)
        {
            if (seq[s].OpCode != OpCodes.Call && seq[s].OpCode != OpCodes.Callvirt)
                continue;
            if (seq[s].Operand is not MethodReference mr)
                continue;
            if (load < 0 && mr.FullName.Contains("Assembly::Load"))
                load = s;
            if (mr.FullName.Contains("GetAssemblies"))
            {
                scan = s;
                break;
            }
        }
        Assert.True(load >= 0, "CastOrTransferRefRuntime must pre-load an assembly (R34)");
        Assert.True(scan > load, "carrier pre-load must precede the first GetAssemblies scan (R34)");
    }
}
