using WfWasm.PdnCpu;

// WC1345 vendor Twist TIE (WC-20261103T1345-vendor-twist): ResumeEffectApply
// caught TargetInvocationException from DoEffect.Invoke and logged only the
// outer "thrown by the target of an invocation" line, hiding the effect-leg
// fault (Twist token/render). The catch now unwraps the inner chain via
// FxApplyErrorChain; these pins lock that format (served logs must name the
// inner type + message).
public sealed class FxApplyErrorChainTests
{
    private static string Chain(System.Exception ex)
    {
        var mi = typeof(CpuPaint).GetMethod("FxApplyErrorChain",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(mi);
        return Assert.IsType<string>(mi.Invoke(null, [ex]));
    }

    [Fact]
    public void Unwraps_TargetInvocation_To_Inner_Type_And_Message()
    {
        var inner = new System.InvalidOperationException("twist-render boom");
        var outer = new System.Reflection.TargetInvocationException(inner);
        string got = Chain(outer);
        Assert.Contains("TargetInvocationException", got);
        Assert.Contains("InvalidOperationException", got);
        Assert.Contains("twist-render boom", got);
    }

    [Fact]
    public void Plain_Exception_Formats_Single_Link()
    {
        string got = Chain(new System.ArgumentNullException("surface"));
        Assert.StartsWith("ArgumentNullException", got);
        Assert.DoesNotContain("<", got);
    }

    [Fact]
    public void Appends_Innermost_Stack_Top_Frame()
    {
        System.Exception inner;
        try
        {
            throw new System.InvalidOperationException("deep fault");
        }
        catch (System.Exception ex)
        {
            inner = ex;
        }
        var outer = new System.Reflection.TargetInvocationException(inner);
        string got = Chain(outer);
        Assert.Contains("@", got);
        Assert.Contains(nameof(FxApplyErrorChainTests), got);
    }

    [Fact]
    public void Chain_Is_Capped_And_Cycle_Safe()
    {
        // 10-deep nesting formats at most 6 links and always terminates.
        System.Exception ex = new System.Exception("base 0");
        for (int i = 1; i < 10; i++)
            ex = new System.Exception("wrap " + i, ex);
        string got = Chain(ex);
        Assert.Equal(5, got.Split('<').Length - 1);
    }
}
