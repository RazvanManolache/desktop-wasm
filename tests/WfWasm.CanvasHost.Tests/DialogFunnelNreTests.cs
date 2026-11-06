using WfWasm.CanvasHost;

// WC2315-runtime-nre: host-funnel-only File>New NRE instrumentation.
// These tests pin the filter contract BEFORE the implementation lands
// (red-first): NRE + active funnel breadcrumb => captured with site/chain;
// anything else => dropped. Never touches fork frames or stack text.
public sealed class DialogFunnelNreTests
{
    public DialogFunnelNreTests()
    {
        // Hermetic setup: FirstChanceLogGate budgets are static and shared
        // process-wide (first-chance storms from unrelated suites consume
        // them nondeterministically), and FirstChanceCensusTests installs
        // the HookFirstChance recorder process-wide. With the hook live,
        // every in-scope NRE records twice (hook at throw time + the
        // explicit TryRecord under test), which is a test-order artifact:
        // in production the hook is the sole recorder. Unhook + clean gate
        // + empty ring per test so only the filter contract is pinned,
        // never the suite order. Assertions below are unchanged; the
        // census test re-hooks itself when it runs.
        WinFormsAppHost.UnhookFirstChance();
        FirstChanceLogGate.Reset();
        DialogFunnelNre.TakeFunnelNres();
    }

    private static NullReferenceException ThrowNre()
    {
        try
        {
            throw new NullReferenceException("funnel probe");
        }
        catch (NullReferenceException ex)
        {
            return ex;
        }
    }

    [Fact]
    public void Nre_Inside_Breadcrumb_Is_Captured_With_Site()
    {
        DialogFunnelNre.TakeFunnelNres();
        using (DialogFunnelNre.Enter("toolstrip:fileMenuNew"))
        {
            Assert.True(DialogFunnelNre.TryRecord(ThrowNre()));
        }
        var got = DialogFunnelNre.TakeFunnelNres();
        Assert.Single(got);
        Assert.Equal("toolstrip:fileMenuNew", got[0].Breadcrumb);
        Assert.Contains("ThrowNre", got[0].Site);
        Assert.Contains("funnel probe", got[0].MessageHead);
    }

    [Fact]
    public void Nre_Outside_Breadcrumb_Is_Dropped()
    {
        DialogFunnelNre.TakeFunnelNres();
        Assert.False(DialogFunnelNre.TryRecord(ThrowNre()));
        Assert.Empty(DialogFunnelNre.TakeFunnelNres());
    }

    [Fact]
    public void NonNre_Inside_Breadcrumb_Is_Dropped()
    {
        DialogFunnelNre.TakeFunnelNres();
        using (DialogFunnelNre.Enter("modal:fileNew:modal"))
        {
            Assert.False(DialogFunnelNre.TryRecord(new InvalidOperationException("nope")));
        }
        Assert.Empty(DialogFunnelNre.TakeFunnelNres());
    }

    [Fact]
    public void Breadcrumb_Restores_On_Exit_Allowing_Nesting()
    {
        DialogFunnelNre.TakeFunnelNres();
        using (DialogFunnelNre.Enter("outer"))
        {
            using (DialogFunnelNre.Enter("inner"))
            {
                Assert.True(DialogFunnelNre.TryRecord(ThrowNre()));
            }
            Assert.True(DialogFunnelNre.TryRecord(ThrowNre()));
        }
        var got = DialogFunnelNre.TakeFunnelNres();
        Assert.Equal(2, got.Length);
        Assert.Equal("inner", got[0].Breadcrumb);
        Assert.Equal("outer", got[1].Breadcrumb);
        Assert.False(DialogFunnelNre.TryRecord(ThrowNre()));
    }

    // RVW-1105-0900-runtime Job1: the host 160char/6frame truncation must
    // not erase the decisive cause. The funnel chain must retain the
    // structured inner throwing-site (type+method), not just type+message.
    // FAILS until SafeChain carries inner TargetSite info; do not weaken.
    [Fact]
    public void Chain_Includes_Inner_Throwing_Site()
    {
        DialogFunnelNre.TakeFunnelNres();
        using (DialogFunnelNre.Enter("modal:fileNew:dlg"))
        {
            Assert.True(DialogFunnelNre.TryRecord(WrapNre()));
        }
        var got = DialogFunnelNre.TakeFunnelNres();
        Assert.Single(got);
        Assert.Contains(nameof(ThrowInnerNre), got[0].Chain);
    }

    private static NullReferenceException ThrowInnerNre()
    {
        try
        {
            throw new NullReferenceException("inner-probe");
        }
        catch (NullReferenceException ex)
        {
            return ex;
        }
    }

    private static NullReferenceException WrapNre()
    {
        NullReferenceException inner = ThrowInnerNre();
        try
        {
            throw new NullReferenceException("outer-probe", inner);
        }
        catch (NullReferenceException ex)
        {
            return ex;
        }
    }

    [Fact]
    public void Take_Drains_Ring()
    {
        DialogFunnelNre.TakeFunnelNres();
        using (DialogFunnelNre.Enter("toolstrip:x"))
        {
            Assert.True(DialogFunnelNre.TryRecord(ThrowNre()));
        }
        Assert.Single(DialogFunnelNre.TakeFunnelNres());
        Assert.Empty(DialogFunnelNre.TakeFunnelNres());
    }
}
