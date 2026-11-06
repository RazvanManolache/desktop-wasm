using WfWasm.PdnCpu;

// Fork-side construction diagnostic for the resourcecache/rewriter bridge
// (WC2315 section 4, vendor half): WHO asked (caller site), WHAT key
// (resource-key type name), WHETHER the cache held it, WHERE it threw
// (throwing-site type+method, inner chain). Red-first: this file references
// EffectConstructionProbe before it exists.
public sealed class EffectConstructionProbeTests
{
    private static InvalidOperationException ThrowingFault()
    {
        try
        {
            ThrowInner();
            throw new InvalidOperationException("unreachable");
        }
        catch (Exception ex)
        {
            return new InvalidOperationException("outer construction wall", ex);
        }
    }

    private static void ThrowInner() => throw new NullReferenceException("null headless provider");

    [Fact]
    public void fault_with_key_and_caller_is_captured_fail_closed()
    {
        Assert.Empty(EffectConstructionProbe.TakeDrained());
        Exception fault;
        try { ThrowInner(); fault = new InvalidOperationException("unreachable"); }
        catch (Exception ex) { fault = ex; }
        Assert.True(EffectConstructionProbe.Note(
            "RelayCreateConfigForm",
            "PaintDotNet.DirectWrite.ITextLayout",
            false,
            "PdnRadioButton.<MeasureAndDraw>b__1",
            fault));
        var notes = EffectConstructionProbe.TakeDrained();
        var note = Assert.Single(notes);
        Assert.Equal("RelayCreateConfigForm", note.Stage);
        Assert.Equal("PaintDotNet.DirectWrite.ITextLayout", note.ResourceKey);
        Assert.False(note.CacheHit);
        Assert.Equal("PdnRadioButton.<MeasureAndDraw>b__1", note.CallerSite);
        Assert.Contains("ThrowInner", note.ThrowSite);
        Assert.Contains("NullReferenceException", note.Chain);
        Assert.Contains("null headless provider", note.MessageHead);
    }

    [Fact]
    public void inner_chain_reports_outer_and_inner_types()
    {
        Assert.Empty(EffectConstructionProbe.TakeDrained());
        Assert.True(EffectConstructionProbe.Note("ENTER", "K", true, "C", ThrowingFault()));
        var note = Assert.Single(EffectConstructionProbe.TakeDrained());
        Assert.Contains("InvalidOperationException", note.Chain);
        Assert.Contains("NullReferenceException", note.Chain);
        Assert.Contains("outer construction wall", note.MessageHead);
    }

    [Fact]
    public void stage_marker_without_fault_is_recorded()
    {
        Assert.Empty(EffectConstructionProbe.TakeDrained());
        Assert.True(EffectConstructionProbe.Note(
            "ENTER-RelayCreateConfigForm", null, null, "RunEffectImpl", null));
        var note = Assert.Single(EffectConstructionProbe.TakeDrained());
        Assert.Equal("ENTER-RelayCreateConfigForm", note.Stage);
        Assert.Equal("", note.ResourceKey);
        Assert.Null(note.CacheHit);
        Assert.Equal("", note.ThrowSite);
        Assert.Equal("", note.Chain);
    }

    private sealed class HostileMessageException : Exception
    {
        public override string Message => throw new InvalidOperationException("message boom");
    }

    [Fact]
    public void hostile_fault_never_throws_and_still_records()
    {
        Assert.Empty(EffectConstructionProbe.TakeDrained());
        Assert.True(EffectConstructionProbe.Note("STAGE", "KEY", null, "CALLER", new HostileMessageException()));
        var note = Assert.Single(EffectConstructionProbe.TakeDrained());
        Assert.Equal("STAGE", note.Stage);
        Assert.Equal("KEY", note.ResourceKey);
    }

    [Fact]
    public void null_stage_is_rejected_fail_closed()
    {
        Assert.Empty(EffectConstructionProbe.TakeDrained());
        Assert.False(EffectConstructionProbe.Note(null, "K", null, "C", null));
        Assert.Empty(EffectConstructionProbe.TakeDrained());
    }

    [Fact]
    public void miss_overload_records_unhit_key_without_caller_or_fault()
    {
        Assert.Empty(EffectConstructionProbe.TakeDrained());
        Assert.True(EffectConstructionProbe.NoteMiss(
            "rescache-miss", "PaintDotNet.UI.Media.DeviceBitmap"));
        var note = Assert.Single(EffectConstructionProbe.TakeDrained());
        Assert.Equal("rescache-miss", note.Stage);
        Assert.Equal("PaintDotNet.UI.Media.DeviceBitmap", note.ResourceKey);
        Assert.False(note.CacheHit);
        Assert.Equal("", note.CallerSite);
        Assert.Equal("", note.ThrowSite);
    }

    [Fact]
    public void ring_caps_and_drain_empties()
    {
        Assert.Empty(EffectConstructionProbe.TakeDrained());
        for (int i = 0; i < 40; i++)
            Assert.True(EffectConstructionProbe.Note("S" + i, null, null, null, null));
        var notes = EffectConstructionProbe.TakeDrained();
        Assert.Equal(32, notes.Length);
        Assert.Equal("S39", notes[^1].Stage);
        Assert.Empty(EffectConstructionProbe.TakeDrained());
    }
}
