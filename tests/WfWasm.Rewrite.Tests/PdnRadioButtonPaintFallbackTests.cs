using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

// Pins FallbackPdnRadioButtonPaint against the real Framework binary
// (targets/ read-only: copied to temp first): OnPaintRadioButton keeps
// its DWrite MeasureAndDraw call as the try path and gains an
// Exception catch that repaints circle + text in pure GDI
// (MeasureAndDrawClassicButton x measure/draw, TextRenderer
// MeasureText/DrawText, 3-DIP gap, disabled gray). Fail-loud asserts
// below double as shape-drift pins.
public sealed class PdnRadioButtonPaintFallbackTests
{
    [Fact]
    public void Fallback_Real_RadioPaint_Keeps_DWrite_And_Adds_Gdi_Catch()
    {
        string src = @"E:\AI\Code\WindowsForms\targets\paint.net\PaintDotNet.Framework.dll";
        string tmp = Path.Combine(Path.GetTempPath(), $"PdnRadioPaint_{Guid.NewGuid()}.dll");
        File.Copy(src, tmp);
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            AssemblyRewriter.FallbackPdnRadioButtonPaint(read.MainModule);

            var type = read.MainModule.GetType("PaintDotNet.Controls.PdnRadioButton");
            var m = type.Methods.First(x => x.Name == "OnPaintRadioButton");
            Assert.Single(m.Body.ExceptionHandlers);
            var h = m.Body.ExceptionHandlers[0];
            Assert.Equal(ExceptionHandlerType.Catch, h.HandlerType);
            Assert.Equal("System.Exception", h.CatchType.FullName);
            Assert.Same(m.Body.Instructions[0], h.TryStart);
            Assert.Same(h.HandlerStart, h.TryEnd);

            // Try path untouched: exactly one MeasureAndDraw call.
            int draws = m.Body.Instructions.Count(i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference mr && mr.Name == "MeasureAndDraw");
            Assert.Equal(1, draws);

            // Catch path: classic circle (measure + draw), CPU text
            // measure + draw, no DWrite layout calls added.
            var handlerInstrs = m.Body.Instructions
                .SkipWhile(i => i != h.HandlerStart)
                .ToList();
            var calls = handlerInstrs
                .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                .Select(i => ((MethodReference)i.Operand))
                .Select(r => r.DeclaringType.Name + "::" + r.Name)
                .ToList();
            Assert.Equal(2, calls.Count(c => c == "PdnRadioButton::MeasureAndDrawClassicButton"));
            Assert.Contains("TextRenderer::MeasureText", calls);
            Assert.Contains("TextRenderer::DrawText", calls);
            Assert.DoesNotContain(calls, c => c.Contains("UIText") || c.Contains("GetCachedOrCreateResource"));
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
}
