using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

// Pins FallbackPdnDropDownListDrawText against the real Framework binary
// (targets/ read-only: copied to temp first): Renderer.DrawText keeps
// its DWrite DrawingContextUtil.Draw call as the try path and gains an
// Exception catch that repaints the text in pure GDI
// (TextRenderer.DrawText at the same 1-DIP left margin, empty text
// returns early). Fail-loud asserts below double as shape-drift pins.
public sealed class PdnDropDownListDrawTextFallbackTests
{
    private static string FrameworkBytes()
    {
        string src = @"E:\AI\Code\WindowsForms\targets\paint.net\PaintDotNet.Framework.dll";
        string tmp = Path.Combine(Path.GetTempPath(), $"PdnDropDownText_{Guid.NewGuid()}.dll");
        File.Copy(src, tmp);
        return tmp;
    }

    [Fact]
    public void Fallback_Real_DropDownDrawText_Keeps_DWrite_And_Adds_Gdi_Catch()
    {
        string tmp = FrameworkBytes();
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            AssemblyRewriter.FallbackPdnDropDownListDrawText(read.MainModule);

            var ddl = read.MainModule.GetType("PaintDotNet.Controls.PdnDropDownList");
            var renderer = ddl.NestedTypes.First(t => t.Name == "Renderer");
            var m = renderer.Methods.First(x => x.Name == "DrawText");
            Assert.Single(m.Body.ExceptionHandlers);
            var h = m.Body.ExceptionHandlers[0];
            Assert.Equal(ExceptionHandlerType.Catch, h.HandlerType);
            Assert.Equal("System.Exception", h.CatchType.FullName);
            Assert.Same(m.Body.Instructions[0], h.TryStart);
            Assert.Same(h.HandlerStart, h.TryEnd);

            // Try path untouched: exactly one DrawingContextUtil.Draw call.
            int draws = m.Body.Instructions.Count(i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference mr && mr.Name == "Draw"
                && mr.DeclaringType.FullName == "PaintDotNet.Drawing.DrawingContextUtil");
            Assert.Equal(1, draws);

            // Catch path: empty-text early-out, 1-DIP left margin via the
            // same scale helper the DWrite path uses, GDI text draw, and
            // no DWrite layout calls added.
            var handlerInstrs = m.Body.Instructions
                .SkipWhile(i => i != h.HandlerStart)
                .ToList();
            var calls = handlerInstrs
                .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                .Select(i => ((MethodReference)i.Operand))
                .Select(r => r.DeclaringType.Name + "::" + r.Name)
                .ToList();
            Assert.Contains("String::IsNullOrEmpty", calls);
            Assert.Contains("Rectangle::get_X", calls);
            Assert.Contains("Rectangle::get_Y", calls);
            Assert.Contains("UIScaleFactor::ConvertDipsToPixelsInt", calls);
            Assert.Contains("TextRenderer::DrawText", calls);
            Assert.DoesNotContain(calls, c => c.Contains("UIText") || c.Contains("GetCachedOrCreateResource") || c.Contains("DrawingContextUtil"));
            Assert.Equal(OpCodes.Ret, m.Body.Instructions.Last().OpCode);
            Assert.Same(m.Body.Instructions.Last(), h.HandlerEnd);
            Assert.True(m.Body.MaxStackSize >= 6);
            // The targets/ Framework binary is mixed-mode, which Cecil
            // cannot write back at all (same for every existing Framework
            // rewrite: production regen supplies its own writable input),
            // so no Write round-trip here: the handler bounds above plus
            // the full-suite gate are the pins.
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    private static string WritableFrameworkInput()
    {
        string src = @"E:\AI\Code\DesktopWasm\.artifacts\tmp\runs\dwshell\rvw1105\regen-w8-pristine\PaintDotNet.Framework.dll";
        Assert.True(File.Exists(src), "writable pristine Framework input missing: " + src);
        string tmp = Path.Combine(Path.GetTempPath(), $"PdnDropDownExec_{Guid.NewGuid():N}.dll");
        File.Copy(src, tmp);
        return tmp;
    }

    private static bool IsValueLoad(Instruction ins)
    {
        var op = ins.OpCode;
        if (op == OpCodes.Ldc_I4 || op == OpCodes.Ldc_I4_S || op == OpCodes.Ldc_I4_M1
            || op == OpCodes.Ldc_I4_0 || op == OpCodes.Ldc_I4_1 || op == OpCodes.Ldc_I4_2
            || op == OpCodes.Ldc_I4_3 || op == OpCodes.Ldc_I4_4 || op == OpCodes.Ldc_I4_5
            || op == OpCodes.Ldc_I4_6 || op == OpCodes.Ldc_I4_7 || op == OpCodes.Ldc_I4_8)
            return true;
        if (op == OpCodes.Ldloc || op == OpCodes.Ldloc_S
            || op == OpCodes.Ldloc_0 || op == OpCodes.Ldloc_1
            || op == OpCodes.Ldloc_2 || op == OpCodes.Ldloc_3)
            return true;
        if (op == OpCodes.Ldarg || op == OpCodes.Ldarg_S
            || op == OpCodes.Ldarg_0 || op == OpCodes.Ldarg_1
            || op == OpCodes.Ldarg_2 || op == OpCodes.Ldarg_3)
            return true;
        if (op == OpCodes.Ldsfld)
            return true;
        return false;
    }

    private static string? AddrType(Instruction? ins)
    {
        if (ins is null) return null;
        var op = ins.OpCode;
        if ((op == OpCodes.Ldloca || op == OpCodes.Ldloca_S)
            && ins.Operand is VariableDefinition vd)
            return vd.VariableType.FullName;
        if ((op == OpCodes.Ldarga || op == OpCodes.Ldarga_S)
            && ins.Operand is ParameterDefinition pd)
            return pd.ParameterType.FullName;
        if (op == OpCodes.Ldsflda && ins.Operand is FieldDefinition fd)
            return fd.FieldType.FullName;
        return null;
    }

    [Fact]
    public void Scale_Instance_Call_Takes_Address_Not_Value()
    {
        // VEN-QC4-2057: UIScaleFactor is a readonly struct and
        // ConvertDipsToPixelsInt is an instance method. Emitting
        // `call get_Current; ldc; call convert` passes the struct VALUE
        // where `this` needs a managed pointer, so the method fails with
        // InvalidProgramException when the catch path runs (live
        // PdnDropDownList paint IPE, blank toolbar dropdowns). The scale
        // must be materialized into a local and passed by address, the
        // pristine classic-box shape. Covers all four scale emit sites
        // (checkbox/radio measure + radio/dropdown paint fallbacks).
        // In-process execution is unavailable in this checkout (converted
        // inputs reference the browser-workload corelib MVID and cannot
        // load on desktop CLR; exact probe trail in the slice report), so
        // this pins the provably-valid shape instead.
        string tmp = WritableFrameworkInput();
        try
        {
            var cecilResolver = new DefaultAssemblyResolver();
            cecilResolver.AddSearchDirectory(@"E:\AI\Code\DesktopWasm\.artifacts\tmp\runs\dwshell\rvw1105\regen-w8-pristine");
            cecilResolver.AddSearchDirectory(@"E:\AI\Code\WindowsForms\targets\paint.net");
            using (var read = AssemblyDefinition.ReadAssembly(tmp,
                new ReaderParameters { AssemblyResolver = cecilResolver }))
            {
                read.MainModule.Attributes |= ModuleAttributes.ILOnly;
                read.MainModule.Attributes &= ~ModuleAttributes.Required32Bit;
                read.MainModule.Attributes &= ~ModuleAttributes.Preferred32Bit;
                AssemblyRewriter.RedirectPdnCheckBoxMeasure(read.MainModule);
                AssemblyRewriter.RedirectPdnRadioButtonMeasure(read.MainModule);
                AssemblyRewriter.FallbackPdnRadioButtonPaint(read.MainModule);
                AssemblyRewriter.FallbackPdnDropDownListDrawText(read.MainModule);
                // Only the four rewritten methods are in scope: pristine
                // callers use other overloads (SizeInt32) or branch-fed
                // ints, which a local walk-back cannot judge.
                var targets = new List<(string Type, string Method)>
                {
                    ("PaintDotNet.Controls.PdnCheckBox", "GetPreferredSize"),
                    ("PaintDotNet.Controls.PdnRadioButton", "GetPreferredSize"),
                    ("PaintDotNet.Controls.PdnRadioButton", "OnPaintRadioButton"),
                    ("PaintDotNet.Controls.PdnDropDownList/Renderer", "DrawText"),
                };
                var bad = new List<string>();
                int sites = 0;
                var stack = new Stack<TypeDefinition>(read.MainModule.Types);
                while (stack.Count > 0)
                {
                    var type = stack.Pop();
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasBody) continue;
                        bool wanted = false;
                        foreach (var t in targets)
                        {
                            if (type.FullName == t.Type && method.Name == t.Method)
                            {
                                wanted = true;
                                break;
                            }
                        }
                        if (!wanted) continue;
                        var insns = method.Body.Instructions;
                        for (int j = 0; j < insns.Count; j++)
                        {
                            var ins = insns[j];
                            if ((ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt)
                                || ins.Operand is not MethodReference mr
                                || mr.Name != "ConvertDipsToPixelsInt"
                                || mr.DeclaringType.FullName != "PaintDotNet.UIScaleFactor")
                                continue;
                            sites++;
                            // Walk back over int/Size value argument loads; the
                            // this-producer before them must be a managed
                            // pointer to UIScaleFactor (ldloca/ldarga/ldsflda),
                            // the pristine classic-box shape. A struct VALUE
                            // (call get_Current left on the stack) fails
                            // verification with InvalidProgramException.
                            int k = j - 1;
                            while (k >= 0 && IsValueLoad(insns[k])) k--;
                            var prod = k >= 0 ? insns[k] : null;
                            string? addrType = AddrType(prod);
                            if (addrType != "PaintDotNet.UIScaleFactor")
                                bad.Add(type.FullName + "::" + method.Name
                                    + " this-by-value to ConvertDipsToPixelsInt"
                                    + " (producer "
                                    + (prod is null ? "<end>" : prod.OpCode.Name)
                                    + ")");
                        }
                    }
                    foreach (var nested in type.NestedTypes)
                        stack.Push(nested);
                }
                Assert.True(sites >= 4,
                    "expected at least the 4 scale emit sites, found " + sites);
                Assert.True(bad.Count == 0,
                    "struct-this by value (InvalidProgramException when run): "
                    + string.Join("; ", bad.Take(8)));
            }
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public void Fallback_Rejects_Double_Application()
    {
        string tmp = FrameworkBytes();
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            AssemblyRewriter.FallbackPdnDropDownListDrawText(read.MainModule);
            Assert.Throws<InvalidOperationException>(
                () => AssemblyRewriter.FallbackPdnDropDownListDrawText(read.MainModule));
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
